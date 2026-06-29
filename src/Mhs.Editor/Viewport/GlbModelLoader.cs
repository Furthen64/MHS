using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Media;
using SharpGLTF.Schema2;

namespace Mhs.Editor.Viewport;

public sealed record GlbTriangle(Vector3 A, Vector3 B, Vector3 C, Color Color, Vector3 Normal);

public sealed class GlbModel
{
    public IReadOnlyList<GlbTriangle> Triangles { get; init; } = [];
    public Vector3 Min { get; init; }
    public Vector3 Max { get; init; }
}

public sealed class GlbModelLoader
{
    private static readonly ConcurrentDictionary<string, GlbModel> Cache = new(StringComparer.OrdinalIgnoreCase);

    public GlbModel Load(string path) => Cache.GetOrAdd(path, LoadUncached);

    private static GlbModel LoadUncached(string path)
    {
        var model = ModelRoot.Load(path);
        var triangles = new List<GlbTriangle>();
        var scenes = model.LogicalScenes.Count > 0 ? model.LogicalScenes : [];
        foreach (var scene in scenes)
        {
            foreach (var node in scene.VisualChildren)
            {
                AddNode(node, Matrix4x4.Identity, triangles);
            }
        }

        if (triangles.Count == 0)
        {
            throw new InvalidOperationException("The .glb file did not contain any renderable triangle primitives.");
        }

        var points = triangles.SelectMany(t => new[] { t.A, t.B, t.C }).ToArray();
        return new GlbModel
        {
            Triangles = triangles,
            Min = new Vector3(points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z)),
            Max = new Vector3(points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z))
        };
    }

    private static void AddNode(Node node, Matrix4x4 parentTransform, List<GlbTriangle> triangles)
    {
        var transform = node.LocalMatrix * parentTransform;
        if (node.Mesh is { } mesh)
        {
            foreach (var primitive in mesh.Primitives)
            {
                AddPrimitive(primitive, transform, triangles);
            }
        }

        foreach (var child in node.VisualChildren)
        {
            AddNode(child, transform, triangles);
        }
    }

    private static void AddPrimitive(MeshPrimitive primitive, Matrix4x4 transform, List<GlbTriangle> triangles)
    {
        if (primitive.DrawPrimitiveType != PrimitiveType.TRIANGLES)
        {
            return;
        }

        var positions = primitive.GetVertexAccessor("POSITION")?.AsVector3Array();
        if (positions is null || positions.Count < 3)
        {
            return;
        }

        var materialColor = ToColor(primitive.Material);
        var textureSampler = TryCreateBaseColorTextureSampler(primitive.Material);
        var texCoords = textureSampler is null ? null : primitive.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();
        var indices = primitive.GetIndices();
        if (indices is { Count: >= 3 })
        {
            for (var i = 0; i + 2 < indices.Count; i += 3)
            {
                AddTriangle((int)indices[i], (int)indices[i + 1], (int)indices[i + 2]);
            }
        }
        else
        {
            for (var i = 0; i + 2 < positions.Count; i += 3)
            {
                AddTriangle(i, i + 1, i + 2);
            }
        }

        void AddTriangle(int ia, int ib, int ic)
        {
            if ((uint)ia >= positions.Count || (uint)ib >= positions.Count || (uint)ic >= positions.Count)
            {
                return;
            }

            triangles.Add(new GlbTriangle(
                Vector3.Transform(positions[ia], transform),
                Vector3.Transform(positions[ib], transform),
                Vector3.Transform(positions[ic], transform),
                ResolveTriangleColor(ia, ib, ic),
                ResolveTriangleNormal(ia, ib, ic)));
        }

        Color ResolveTriangleColor(int ia, int ib, int ic)
        {
            if (textureSampler is null || texCoords is null
                || (uint)ia >= texCoords.Count || (uint)ib >= texCoords.Count || (uint)ic >= texCoords.Count)
            {
                return materialColor;
            }

            var uv = (texCoords[ia] + texCoords[ib] + texCoords[ic]) / 3f;
            return Modulate(materialColor, textureSampler(uv));
        }

        Vector3 ResolveTriangleNormal(int ia, int ib, int ic)
        {
            var a = Vector3.Transform(positions[ia], transform);
            var b = Vector3.Transform(positions[ib], transform);
            var c = Vector3.Transform(positions[ic], transform);
            var normal = Vector3.Cross(b - a, c - a);
            return normal.LengthSquared() > 0.000001f ? Vector3.Normalize(normal) : Vector3.UnitZ;
        }
    }

    private static Color ToColor(Material? material)
    {
        var color = material?.FindChannel("BaseColor")?.Color ?? Vector4.One;
        return Color.FromArgb(ToByte(color.W), ToByte(color.X), ToByte(color.Y), ToByte(color.Z));
        static byte ToByte(float value) => (byte)Math.Clamp((int)Math.Round(value * 255f), 0, 255);
    }

    private static Func<Vector2, Color>? TryCreateBaseColorTextureSampler(Material? material)
    {
        var channel = material?.FindChannel("BaseColor");
        var texture = channel?.GetType().GetProperty("Texture")?.GetValue(channel);
        var image = texture?.GetType().GetProperty("PrimaryImage")?.GetValue(texture);
        var content = image?.GetType().GetProperty("Content")?.GetValue(image);
        var rawBytes = content?.GetType().GetProperty("Content")?.GetValue(content);
        var bytes = rawBytes switch
        {
            ArraySegment<byte> segment => segment,
            byte[] array => new ArraySegment<byte>(array),
            _ => default
        };
        if (bytes.Array is null || bytes.Count == 0)
        {
            return null;
        }

        using var stream = new MemoryStream(bytes.Array, bytes.Offset, bytes.Count, writable: false);
        using var bitmap = new Bitmap(stream);
        var width = bitmap.PixelSize.Width;
        var height = bitmap.PixelSize.Height;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var stride = width * 4;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(new PixelRect(0, 0, width, height), pixels, stride, 0, PixelFormats.Bgra8888);

        return uv =>
        {
            var u = uv.X - MathF.Floor(uv.X);
            var v = uv.Y - MathF.Floor(uv.Y);
            var x = Math.Clamp((int)MathF.Floor(u * width), 0, width - 1);
            var y = Math.Clamp((int)MathF.Floor((1f - v) * height), 0, height - 1);
            var offset = y * stride + x * 4;
            return Color.FromArgb(pixels[offset + 3], pixels[offset + 2], pixels[offset + 1], pixels[offset]);
        };
    }

    private static Color Modulate(Color baseColor, Color textureColor)
    {
        return Color.FromArgb(
            Multiply(baseColor.A, textureColor.A),
            Multiply(baseColor.R, textureColor.R),
            Multiply(baseColor.G, textureColor.G),
            Multiply(baseColor.B, textureColor.B));

        static byte Multiply(byte a, byte b) => (byte)Math.Clamp((a * b + 127) / 255, 0, 255);
    }
}
