using System;
using System.Linq;
using Avalonia;
using Avalonia.Media;

namespace Mhs.Editor.Viewport;

public sealed class SoftwareCubeRenderer : IViewportRenderer
{
    private static readonly (int A, int B)[] Edges =
    [
        (0, 1), (1, 2), (2, 3), (3, 0),
        (4, 5), (5, 6), (6, 7), (7, 4),
        (0, 4), (1, 5), (2, 6), (3, 7)
    ];

    private readonly CubeSceneState _sceneState;

    public SoftwareCubeRenderer(CubeSceneState sceneState)
    {
        _sceneState = sceneState;
    }

    public void Update(TimeSpan deltaTime)
    {
        var seconds = deltaTime.TotalSeconds;
        _sceneState.RotationX += seconds * 0.8;
        _sceneState.RotationY += seconds * 1.1;
        _sceneState.RotationZ += seconds * 0.5;
    }

    public void Render(DrawingContext context, Rect bounds)
    {
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(25, 30, 35)), bounds);

        if (bounds.Width < 2 || bounds.Height < 2)
        {
            return;
        }

        DrawGrid(context, bounds);

        var transformed = GetTransformedVertices();
        var points = transformed.Select(v => Project(v, bounds)).ToArray();

        var edgePen = new Pen(new SolidColorBrush(Color.FromRgb(205, 214, 244)), 2);

        foreach (var (a, b) in Edges)
        {
            context.DrawLine(edgePen, points[a], points[b]);
        }
    }

    private static void DrawGrid(DrawingContext context, Rect bounds)
    {
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(50, 180, 190, 200)), 1);
        var horizon = bounds.Center.Y + bounds.Height * 0.25;

        for (var x = bounds.X; x <= bounds.Right; x += 40)
        {
            context.DrawLine(gridPen, new Point(x, horizon), new Point(x, bounds.Bottom));
        }

        for (var y = horizon; y <= bounds.Bottom; y += 28)
        {
            context.DrawLine(gridPen, new Point(bounds.X, y), new Point(bounds.Right, y));
        }
    }

    private Vector3[] GetTransformedVertices()
    {
        var baseVertices = new[]
        {
            new Vector3(-1, -1, -1),
            new Vector3(1, -1, -1),
            new Vector3(1, 1, -1),
            new Vector3(-1, 1, -1),
            new Vector3(-1, -1, 1),
            new Vector3(1, -1, 1),
            new Vector3(1, 1, 1),
            new Vector3(-1, 1, 1)
        };

        return baseVertices.Select(Rotate).ToArray();
    }

    private Vector3 Rotate(Vector3 vertex)
    {
        var rx = _sceneState.RotationX;
        var ry = _sceneState.RotationY;
        var rz = _sceneState.RotationZ;

        var cosX = Math.Cos(rx);
        var sinX = Math.Sin(rx);
        var cosY = Math.Cos(ry);
        var sinY = Math.Sin(ry);
        var cosZ = Math.Cos(rz);
        var sinZ = Math.Sin(rz);

        var y1 = vertex.Y * cosX - vertex.Z * sinX;
        var z1 = vertex.Y * sinX + vertex.Z * cosX;

        var x2 = vertex.X * cosY + z1 * sinY;
        var z2 = -vertex.X * sinY + z1 * cosY;

        var x3 = x2 * cosZ - y1 * sinZ;
        var y3 = x2 * sinZ + y1 * cosZ;

        return new Vector3(x3, y3, z2 + 4.5);
    }

    private static Point Project(Vector3 vertex, Rect bounds)
    {
        var perspective = 2.6 / vertex.Z;
        var scale = Math.Min(bounds.Width, bounds.Height) * 0.32;

        return new Point(
            bounds.Center.X + vertex.X * perspective * scale,
            bounds.Center.Y - vertex.Y * perspective * scale);
    }

    private readonly record struct Vector3(double X, double Y, double Z);
}
