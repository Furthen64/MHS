using System;

namespace Mhs.Editor.Editor;

public sealed class SceneObject
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string PartType { get; init; } = string.Empty;
    public VoxelCoord Position { get; set; }
    public VoxelSize Size { get; init; }
    public double RotationDegrees { get; set; }
}
