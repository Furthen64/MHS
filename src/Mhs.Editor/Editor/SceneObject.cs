using System;

namespace Mhs.Editor.Editor;

public sealed class SceneObject
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string PartType { get; init; } = string.Empty;
    public VoxelCoord Position { get; set; }
    public VoxelSize BaseSize { get; init; }

    public int RotationZDegrees { get; set; }

    public VoxelSize EffectiveSize => RotationHelper.GetEffectiveSize(BaseSize, RotationZDegrees);

    public int MinX => Position.X;
    public int MaxX => Position.X + EffectiveSize.WidthX - 1;
    public int MinY => Position.Y;
    public int MaxY => Position.Y + EffectiveSize.DepthY - 1;
    public int MinZ => Position.Z;
    public int MaxZ => Position.Z + EffectiveSize.HeightZ - 1;
}
