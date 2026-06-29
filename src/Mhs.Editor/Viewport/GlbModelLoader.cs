using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Avalonia.Media;
using SharpGLTF.Schema2;

namespace Mhs.Editor.Viewport;

public sealed record GlbTriangle(Vector3 A, Vector3 B, Vector3 C, Color Color);

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

        // TODO: add texture support for custom .glb materials.
        var color = ToColor(primitive.Material);
        var indices = primitive.GetIndices()?.AsIndicesArray();
        if (indices is { Count: >= 3 })
        {
            for (var i = 0; i + 2 < indices.Count; i += 3)
            {
                AddTriangle(indices[i], indices[i + 1], indices[i + 2]);
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
                color));
        }
    }

    private static Color ToColor(Material? material)
    {
        var color = material?.PbrMetallicRoughness?.BaseColorFactor ?? Vector4.One;
        return Color.FromArgb(ToByte(color.W), ToByte(color.X), ToByte(color.Y), ToByte(color.Z));
        static byte ToByte(float value) => (byte)Math.Clamp((int)Math.Round(value * 255f), 0, 255);
    }
}
