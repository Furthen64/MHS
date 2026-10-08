using System;

namespace Mhs.Editor.Editor;

public sealed class SceneObject
{
    public const float DefaultMaterialUnitsPerSecond = 1.0f;
    public const int DefaultMaterialGranulesPerPacket = 1;
    public const string DefaultMaterialId = "Brown";

    public Guid Id { get; init; } = Guid.NewGuid();
    public string PartId { get; init; } = string.Empty;
    public string PartType { get; init; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    // TODO: include custom .glb asset references in project serialization.
    public string CustomGlbAssetPath { get; init; } = string.Empty;
    public VoxelCoord Position { get; set; }
    public VoxelSize BaseSize { get; init; }

    public int RotationZDegrees { get; set; }
    public VoxelCoord? RouteStartCell { get; set; }
    public VoxelCoord? RouteEndCell { get; set; }
    public bool RouteFlowReversed { get; set; }
    public float MaterialUnitsPerSecond { get; set; } = DefaultMaterialUnitsPerSecond;
    public int MaterialGranulesPerPacket { get; set; } = DefaultMaterialGranulesPerPacket;
    public string MaterialId { get; set; } = DefaultMaterialId;

    public VoxelSize EffectiveSize => GetEffectiveSize(RotationZDegrees);

    public VoxelSize GetEffectiveSize(int rotationZDegrees)
        => string.Equals(PartType, "Conveyor", StringComparison.OrdinalIgnoreCase)
            ? BaseSize
            : RotationHelper.GetEffectiveSize(BaseSize, rotationZDegrees);

    public int MinX => Position.X;
    public int MaxX => Position.X + EffectiveSize.WidthX - 1;
    public int MinY => Position.Y;
    public int MaxY => Position.Y + EffectiveSize.DepthY - 1;
    public int MinZ => Position.Z;
    public int MaxZ => Position.Z + EffectiveSize.HeightZ - 1;

    public bool IsConveyor
        => string.Equals(PartId, "conveyor", StringComparison.OrdinalIgnoreCase)
           || string.Equals(PartType, "Conveyor", StringComparison.OrdinalIgnoreCase);

    public bool IsRouteConveyorSegment
        => IsConveyor && RouteStartCell.HasValue && RouteEndCell.HasValue;

    public (VoxelCoord Start, VoxelCoord End) GetConveyorFlowEndpoints()
    {
        var start = RouteStartCell ?? Position;
        var end = RouteEndCell ?? Position;
        return RouteFlowReversed
            ? (end, start)
            : (start, end);
    }

    public int GetConveyorFlowRotationDegrees()
    {
        if (!IsConveyor)
        {
            return RotationHelper.NormalizeDegrees(RotationZDegrees);
        }

        var (start, end) = GetConveyorFlowEndpoints();
        if (start.X != end.X)
        {
            return end.X > start.X ? 0 : 180;
        }

        if (start.Y != end.Y)
        {
            return end.Y > start.Y ? 90 : 270;
        }

        return RotationHelper.NormalizeDegrees(RotationZDegrees);
    }
}
