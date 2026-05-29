using System;

namespace Mhs.Editor.Editor;

public sealed class SceneObject
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string PartType { get; init; } = string.Empty;
    public VoxelCoord Position { get; set; }
    public VoxelSize Size { get; init; }
    public double RotationDegrees { get; set; }

    public int MinX => Position.X;
    public int MaxX => Position.X + Size.WidthX - 1;
    public int MinY => Position.Y;
    public int MaxY => Position.Y + Size.DepthY - 1;
    public int MinZ => Position.Z;
    public int MaxZ => Position.Z + Size.HeightZ - 1;
}
