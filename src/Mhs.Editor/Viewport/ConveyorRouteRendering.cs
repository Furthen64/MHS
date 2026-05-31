using System;
using Mhs.Editor.Editor;

namespace Mhs.Editor.Viewport;

public static class ConveyorRouteRendering
{
    public static bool TryGetTurnJoinCell(VoxelCoord previousStart, VoxelCoord previousEnd, VoxelCoord nextStart, VoxelCoord nextEnd, out VoxelCoord joinCell)
    {
        joinCell = default;

        if (previousStart.Z != previousEnd.Z || nextStart.Z != nextEnd.Z || previousStart.Z != nextStart.Z)
        {
            return false;
        }

        var previousHorizontal = previousStart.X != previousEnd.X;
        var nextHorizontal = nextStart.X != nextEnd.X;
        if (previousHorizontal == nextHorizontal)
        {
            return false;
        }

        if (previousStart == previousEnd || nextStart == nextEnd)
        {
            return false;
        }

        joinCell = previousEnd;
        return true;
    }

    public static bool TryGetConveyorEndpoints(SceneObject conveyor, out VoxelCoord start, out VoxelCoord end)
    {
        start = default;
        end = default;

        if (!string.Equals(conveyor.PartType, "Conveyor", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var position = conveyor.Position;
        var size = conveyor.EffectiveSize;
        var rotation = RotationHelper.NormalizeDegrees(conveyor.RotationZDegrees);

        switch (rotation)
        {
            case 0:
                start = position;
                end = position with { X = position.X + size.WidthX - 1 };
                return true;
            case 180:
                start = position with { X = position.X + size.WidthX - 1 };
                end = position;
                return true;
            case 90:
                start = position;
                end = position with { Y = position.Y + size.DepthY - 1 };
                return true;
            case 270:
                start = position with { Y = position.Y + size.DepthY - 1 };
                end = position;
                return true;
            default:
                return false;
        }
    }

    public static bool TryGetSceneTurnJoinCell(SceneObject previous, SceneObject next, out VoxelCoord joinCell)
    {
        joinCell = default;

        if (!TryGetConveyorEndpoints(previous, out var previousStart, out var previousEnd)
            || !TryGetConveyorEndpoints(next, out var nextStart, out var nextEnd))
        {
            return false;
        }

        if (previousStart.Z != nextStart.Z)
        {
            return false;
        }

        return TryGetTurnJoinCell(previousStart, previousEnd, nextStart, nextEnd, out joinCell)
            && IsAdjacentToJoinCell(nextStart, nextEnd, joinCell);
    }

    private static bool IsAdjacentToJoinCell(VoxelCoord nextStart, VoxelCoord nextEnd, VoxelCoord joinCell)
    {
        var nextHorizontal = nextStart.X != nextEnd.X;
        if (nextHorizontal)
        {
            return nextStart.Y == joinCell.Y && Math.Abs(nextStart.X - joinCell.X) == 1;
        }

        return nextStart.X == joinCell.X && Math.Abs(nextStart.Y - joinCell.Y) == 1;
    }
}