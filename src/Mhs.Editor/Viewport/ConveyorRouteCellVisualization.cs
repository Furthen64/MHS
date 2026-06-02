using System;
using System.Collections.Generic;
using System.Linq;
using Mhs.Editor.Editor;

namespace Mhs.Editor.Viewport;

public enum ConveyorVisualCellKind
{
    Straight,
    Corner,
    Endpoint
}

public readonly record struct ConveyorVisualCell(
    VoxelCoord Position,
    ConveyorVisualCellKind Kind,
    PortDirection? EntryDirection,
    PortDirection? ExitDirection,
    PortDirection MainFlowDirection);

public static class ConveyorRouteCellVisualization
{
    /// <summary>
    /// Builds per-cell visual data for an in-progress route draft.
    /// </summary>
    /// <param name="anchors">The committed anchor points of the draft (may be empty).</param>
    /// <param name="previewEnd">The live cursor endpoint for the segment being drawn, if any.</param>
    /// <returns>
    /// Committed cells (anchor pair segments, with corner-join directions applied) and
    /// preview cells (last-anchor → cursor segment, with corner direction applied where applicable).
    /// </returns>
    public static (IReadOnlyList<ConveyorVisualCell> Committed, IReadOnlyList<ConveyorVisualCell> Preview) BuildRouteDraftCells(
        IReadOnlyList<VoxelCoord> anchors,
        VoxelCoord? previewEnd)
    {
        // Build one MutableCell list per committed segment
        var segmentLists = new List<List<MutableCell>>();
        for (var i = 1; i < anchors.Count; i++)
        {
            var cells = BuildSegmentCells(anchors[i - 1], anchors[i], skipFirst: i > 1);
            if (cells.Count > 0)
            {
                segmentLists.Add(cells);
            }
        }

        // Build optional preview segment cells
        List<MutableCell>? previewList = null;
        if (anchors.Count > 0 && previewEnd.HasValue && previewEnd.Value != anchors[^1])
        {
            var start = anchors[^1];
            var end = previewEnd.Value;
            var cells = BuildSegmentCells(start, end, skipFirst: anchors.Count > 1);
            if (cells.Count > 0)
            {
                previewList = cells;
            }
        }

        // Apply corner joins between all adjacent segment lists
        var allLists = new List<List<MutableCell>>(segmentLists);
        if (previewList is not null)
        {
            allLists.Add(previewList);
        }

        for (var i = 1; i < allLists.Count; i++)
        {
            ApplySegmentCornerJoin(allLists[i - 1], allLists[i]);
        }

        var committed = segmentLists.Count > 0
            ? segmentLists
                .SelectMany(x => x)
                .Select(c => new ConveyorVisualCell(c.Position, c.Kind, c.EntryDirection, c.ExitDirection, c.MainFlowDirection))
                .ToArray()
            : Array.Empty<ConveyorVisualCell>();

        var preview = previewList is not null
            ? previewList
                .Select(c => new ConveyorVisualCell(c.Position, c.Kind, c.EntryDirection, c.ExitDirection, c.MainFlowDirection))
                .ToArray()
            : Array.Empty<ConveyorVisualCell>();

        return (committed, preview);
    }

    private static List<MutableCell> BuildSegmentCells(VoxelCoord start, VoxelCoord end, bool skipFirst)
    {
        var cells = new List<MutableCell>();
        var mainDirection = GetFlowDirection(start, end, 0);
        var flowCells = ConveyorRouteGeometry.EnumerateCells(start, end).ToArray();

        var startIndex = skipFirst ? 1 : 0;
        for (var i = startIndex; i < flowCells.Length; i++)
        {
            var relI = i - startIndex;
            var entry = relI == 0 ? (PortDirection?)null : mainDirection.Opposite();
            var exit = i == flowCells.Length - 1 ? (PortDirection?)null : mainDirection;
            cells.Add(new MutableCell(
                flowCells[i],
                GetKind(entry, exit),
                entry,
                exit,
                mainDirection));
        }

        return cells;
    }

    private static void ApplySegmentCornerJoin(List<MutableCell> prev, List<MutableCell> next)
    {
        if (prev.Count == 0 || next.Count == 0)
        {
            return;
        }

        var prevLast = prev[^1];
        var nextFirst = next[0];

        var deltaX = nextFirst.Position.X - prevLast.Position.X;
        var deltaY = nextFirst.Position.Y - prevLast.Position.Y;

        if (Math.Abs(deltaX) + Math.Abs(deltaY) != 1)
        {
            return;
        }

        var directionToNext = DirectionFromDelta(deltaX, deltaY);

        if (prevLast.ExitDirection is null)
        {
            prevLast.ExitDirection = directionToNext;
            prevLast.Kind = GetKind(prevLast.EntryDirection, prevLast.ExitDirection);
        }

        if (nextFirst.EntryDirection is null)
        {
            nextFirst.EntryDirection = directionToNext.Opposite();
            nextFirst.Kind = GetKind(nextFirst.EntryDirection, nextFirst.ExitDirection);
        }
    }

    public static IReadOnlyDictionary<Guid, IReadOnlyList<ConveyorVisualCell>> BuildSceneObjectCells(IReadOnlyList<SceneObject> sceneObjects)
    {
        var cellsByObject = new Dictionary<Guid, List<MutableCell>>();
        foreach (var sceneObject in sceneObjects)
        {
            if (!sceneObject.IsConveyor || !TryBuildObjectCells(sceneObject, out var cells))
            {
                continue;
            }

            cellsByObject[sceneObject.Id] = cells;
        }

        for (var i = 1; i < sceneObjects.Count; i++)
        {
            var previous = sceneObjects[i - 1];
            var next = sceneObjects[i];
            if (!cellsByObject.TryGetValue(previous.Id, out var previousCells)
                || !cellsByObject.TryGetValue(next.Id, out var nextCells)
                || !ConveyorRouteRendering.TryGetSceneTurnJoinCell(previous, next, out var joinCell))
            {
                continue;
            }

            var previousCell = previousCells.FirstOrDefault(cell => cell.Position == joinCell);
            if (previousCell is null || !TryFindAdjacentCell(nextCells, joinCell, out var nextCell, out var directionToNext))
            {
                continue;
            }

            if (previousCell.ExitDirection is null)
            {
                previousCell.ExitDirection = directionToNext;
            }
            else if (previousCell.EntryDirection is null)
            {
                previousCell.EntryDirection = directionToNext.Opposite();
            }

            if (nextCell.EntryDirection is null)
            {
                nextCell.EntryDirection = directionToNext.Opposite();
            }
            else if (nextCell.ExitDirection is null)
            {
                nextCell.ExitDirection = directionToNext;
            }

            previousCell.Kind = GetKind(previousCell.EntryDirection, previousCell.ExitDirection);
            nextCell.Kind = GetKind(nextCell.EntryDirection, nextCell.ExitDirection);
        }

        return cellsByObject.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<ConveyorVisualCell>)pair.Value
                .Select(cell => new ConveyorVisualCell(
                    cell.Position,
                    cell.Kind,
                    cell.EntryDirection,
                    cell.ExitDirection,
                    cell.MainFlowDirection))
                .ToArray());
    }

    private static bool TryBuildObjectCells(SceneObject sceneObject, out List<MutableCell> cells)
    {
        cells = new List<MutableCell>();

        var (start, end) = sceneObject.GetConveyorFlowEndpoints();
        if (start == end)
        {
            if (!ConveyorRouteRendering.TryGetConveyorEndpoints(sceneObject, out start, out end))
            {
                return false;
            }

            if (sceneObject.RouteFlowReversed)
            {
                (start, end) = (end, start);
            }
        }

        var mainDirection = GetFlowDirection(start, end, sceneObject.GetConveyorFlowRotationDegrees());
        var flowCells = ConveyorRouteGeometry.EnumerateCells(start, end).ToArray();
        for (var i = 0; i < flowCells.Length; i++)
        {
            var entry = i == 0 ? (PortDirection?)null : mainDirection.Opposite();
            var exit = i == flowCells.Length - 1 ? (PortDirection?)null : mainDirection;
            cells.Add(new MutableCell(
                flowCells[i],
                GetKind(entry, exit),
                entry,
                exit,
                mainDirection));
        }

        return cells.Count > 0;
    }

    private static bool TryFindAdjacentCell(
        IReadOnlyList<MutableCell> cells,
        VoxelCoord joinCell,
        out MutableCell adjacentCell,
        out PortDirection directionFromJoin)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            if (cell.Position.Z != joinCell.Z)
            {
                continue;
            }

            var deltaX = cell.Position.X - joinCell.X;
            var deltaY = cell.Position.Y - joinCell.Y;
            if (Math.Abs(deltaX) + Math.Abs(deltaY) != 1)
            {
                continue;
            }

            adjacentCell = cell;
            directionFromJoin = DirectionFromDelta(deltaX, deltaY);
            return true;
        }

        adjacentCell = null!;
        directionFromJoin = PortDirection.PositiveX;
        return false;
    }

    private static PortDirection GetFlowDirection(VoxelCoord start, VoxelCoord end, int fallbackRotation)
    {
        var deltaX = end.X - start.X;
        if (deltaX > 0)
        {
            return PortDirection.PositiveX;
        }

        if (deltaX < 0)
        {
            return PortDirection.NegativeX;
        }

        var deltaY = end.Y - start.Y;
        if (deltaY > 0)
        {
            return PortDirection.PositiveY;
        }

        if (deltaY < 0)
        {
            return PortDirection.NegativeY;
        }

        return RotationHelper.NormalizeDegrees(fallbackRotation) switch
        {
            0 => PortDirection.PositiveX,
            90 => PortDirection.PositiveY,
            180 => PortDirection.NegativeX,
            _ => PortDirection.NegativeY
        };
    }

    private static PortDirection DirectionFromDelta(int deltaX, int deltaY)
    {
        if (deltaX > 0)
        {
            return PortDirection.PositiveX;
        }

        if (deltaX < 0)
        {
            return PortDirection.NegativeX;
        }

        return deltaY > 0
            ? PortDirection.PositiveY
            : PortDirection.NegativeY;
    }

    private static ConveyorVisualCellKind GetKind(PortDirection? entryDirection, PortDirection? exitDirection)
    {
        if (!entryDirection.HasValue || !exitDirection.HasValue)
        {
            return ConveyorVisualCellKind.Endpoint;
        }

        return entryDirection.Value == exitDirection.Value.Opposite()
            ? ConveyorVisualCellKind.Straight
            : ConveyorVisualCellKind.Corner;
    }

    private sealed class MutableCell(
        VoxelCoord position,
        ConveyorVisualCellKind kind,
        PortDirection? entryDirection,
        PortDirection? exitDirection,
        PortDirection mainFlowDirection)
    {
        public VoxelCoord Position { get; } = position;
        public ConveyorVisualCellKind Kind { get; set; } = kind;
        public PortDirection? EntryDirection { get; set; } = entryDirection;
        public PortDirection? ExitDirection { get; set; } = exitDirection;
        public PortDirection MainFlowDirection { get; } = mainFlowDirection;
    }
}
