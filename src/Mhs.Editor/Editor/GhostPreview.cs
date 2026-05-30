namespace Mhs.Editor.Editor;

public sealed class GhostPreview
{
    public required PartDefinition Part { get; init; }
    public required VoxelCoord Position { get; init; }
    public required int RotationZDegrees { get; init; }
    public required bool IsValid { get; init; }
    public string? InvalidReason { get; init; }

    public VoxelSize EffectiveSize => RotationHelper.GetEffectiveSize(Part.Size, RotationZDegrees);
}
