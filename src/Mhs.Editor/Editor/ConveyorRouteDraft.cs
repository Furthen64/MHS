using System;
using System.Collections.Generic;

namespace Mhs.Editor.Editor;

public sealed class ConveyorRouteDraft
{
    public List<VoxelCoord> Anchors { get; } = new();
    public VoxelCoord? PreviewEnd { get; set; }
    public bool PreviewIsValid { get; set; }
    public string? InvalidReason { get; set; }
    public int? PreviewRotationZDegrees { get; set; }
    public int Z { get; init; }
}

public readonly record struct ConveyorRouteSegment(
    VoxelCoord Start,
    VoxelCoord End,
    VoxelCoord Position,
    VoxelSize Size,
    int RotationZDegrees);

public static class ConveyorRouteGeometry
{
    public static VoxelCoord SnapToDominantAxis(VoxelCoord start, VoxelCoord hovered)
    {
        var deltaX = Math.Abs(hovered.X - start.X);
        var deltaY = Math.Abs(hovered.Y - start.Y);
        return deltaX >= deltaY
            ? new VoxelCoord(hovered.X, start.Y, start.Z)
            : new VoxelCoord(start.X, hovered.Y, start.Z);
    }

    public static bool TryCreateSegment(VoxelCoord start, VoxelCoord end, out ConveyorRouteSegment segment, out string? reason)
    {
        reason = null;
        segment = default;

        if (start.Z != end.Z)
        {
            reason = "out of vertical bounds";
            return false;
        }

        if (start.X != end.X && start.Y != end.Y)
        {
            reason = "segment must be axis-aligned";
            return false;
        }

        if (start.X == end.X && start.Y == end.Y)
        {
            reason = "zero-length segment";
            return false;
        }

        var minX = Math.Min(start.X, end.X);
        var minY = Math.Min(start.Y, end.Y);
        var width = Math.Abs(end.X - start.X) + 1;
        var depth = Math.Abs(end.Y - start.Y) + 1;

        var rotation = end.X > start.X
            ? 0
            : end.X < start.X
                ? 180
                : end.Y > start.Y
                    ? 90
                    : 270;

        segment = new ConveyorRouteSegment(
            start,
            end,
            new VoxelCoord(minX, minY, start.Z),
            new VoxelSize(width, depth, 1),
            rotation);
        return true;
    }

    public static IEnumerable<VoxelCoord> EnumerateCells(VoxelCoord start, VoxelCoord end)
    {
        if (start.X == end.X)
        {
            var step = end.Y >= start.Y ? 1 : -1;
            for (var y = start.Y; ; y += step)
            {
                yield return new VoxelCoord(start.X, y, start.Z);
                if (y == end.Y)
                {
                    yield break;
                }
            }
        }
        else
        {
            var step = end.X >= start.X ? 1 : -1;
            for (var x = start.X; ; x += step)
            {
                yield return new VoxelCoord(x, start.Y, start.Z);
                if (x == end.X)
                {
                    yield break;
                }
            }
        }
    }

    public static void TrimSegmentStartCell(VoxelCoord start, VoxelCoord end, ref VoxelCoord position, ref VoxelSize size)
    {
        if (start.X != end.X)
        {
            position = new VoxelCoord(
                end.X > start.X ? start.X + 1 : end.X,
                start.Y,
                start.Z);
            size = new VoxelSize(Math.Abs(end.X - start.X), 1, 1);
            return;
        }

        position = new VoxelCoord(
            start.X,
            end.Y > start.Y ? start.Y + 1 : end.Y,
            start.Z);
        size = new VoxelSize(1, Math.Abs(end.Y - start.Y), 1);
    }
}
