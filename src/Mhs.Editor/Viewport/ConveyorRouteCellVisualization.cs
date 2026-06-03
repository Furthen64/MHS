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
        var normalizedAnchors = NormalizeAnchors(anchors);

        // Build one MutableCell list per committed segment
        var segmentLists = new List<List<MutableCell>>();
        for (var i = 1; i < normalizedAnchors.Count; i++)
        {
            var cells = BuildSegmentCells(normalizedAnchors[i - 1], normalizedAnchors[i], skipFirst: i > 1);
            if (cells.Count > 0)
            {
                segmentLists.Add(cells);
            }
        }

        // Build optional preview segment cells
        List<MutableCell>? previewList = null;
        if (normalizedAnchors.Count > 0 && previewEnd.HasValue && previewEnd.Value != normalizedAnchors[^1])
        {
            var start = normalizedAnchors[^1];
            var end = previewEnd.Value;
            var cells = BuildSegmentCells(start, end, skipFirst: normalizedAnchors.Count > 1);
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
        var resultMutable = new Dictionary<Guid, List<MutableCell>>();
        var routeSegments = new List<SceneObject>();

        // Separate route segments from standalone conveyors
        foreach (var sceneObject in sceneObjects)
        {
            if (!sceneObject.IsConveyor)
            {
                continue;
            }

            if (sceneObject.IsRouteConveyorSegment)
            {
                routeSegments.Add(sceneObject);
            }
            else if (TryBuildObjectCells(sceneObject, out var cells))
            {
                resultMutable[sceneObject.Id] = cells;
            }
        }

        // Group route segments into connected chains and build cells using the same
        // flat tessellation as the draft preview (BuildSegmentCells + ApplySegmentCornerJoin).
        var visitedIds = new HashSet<Guid>();
        foreach (var segment in routeSegments)
        {
            if (visitedIds.Contains(segment.Id))
            {
                continue;
            }

            // Only start a chain from a chain head (no predecessor points to this segment)
            var segStart = segment.GetConveyorFlowEndpoints().Start;
            var isChainHead = true;
            foreach (var other in routeSegments)
            {
                if (other.Id == segment.Id)
                {
                    continue;
                }

                if (IsAdjacent(other.GetConveyorFlowEndpoints().End, segStart))
                {
                    isChainHead = false;
                    break;
                }
            }

            if (!isChainHead)
            {
                continue;
            }

            // Follow the chain forward
            var chain = new List<SceneObject>();
            var current = segment;
            while (true)
            {
                chain.Add(current);
                visitedIds.Add(current.Id);
                var currentEnd = current.GetConveyorFlowEndpoints().End;
                SceneObject? next = null;
                foreach (var other in routeSegments)
                {
                    if (visitedIds.Contains(other.Id))
                    {
                        continue;
                    }

                    if (IsAdjacent(currentEnd, other.GetConveyorFlowEndpoints().Start))
                    {
                        next = other;
                        break;
                    }
                }

                if (next is null)
                {
                    break;
                }

                current = next;
            }

            BuildChainCells(chain, resultMutable);
        }

        // Handle segments not reachable from any chain head (isolated single-cell or cycles)
        foreach (var segment in routeSegments)
        {
            if (!visitedIds.Contains(segment.Id) && TryBuildObjectCells(segment, out var cells))
            {
                resultMutable[segment.Id] = cells;
            }
        }

        return resultMutable.ToDictionary(
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

    /// <summary>
    /// Reconstructs the anchor list for a connected chain of route segments and builds
    /// per-cell visual data using the same flat tessellation as <see cref="BuildRouteDraftCells"/>.
    /// </summary>
    private static void BuildChainCells(List<SceneObject> chain, Dictionary<Guid, List<MutableCell>> result)
    {
        // Anchor list: start of chain, then end of each segment
        var rawAnchors = new List<VoxelCoord>(chain.Count + 1)
        {
            chain[0].GetConveyorFlowEndpoints().Start
        };
        foreach (var seg in chain)
        {
            rawAnchors.Add(seg.GetConveyorFlowEndpoints().End);
        }

        var anchors = NormalizeAnchors(rawAnchors);

        if (anchors.Count < 2)
        {
            // Degenerate chain: fall back to per-object cell building
            foreach (var seg in chain)
            {
                if (TryBuildObjectCells(seg, out var cells))
                {
                    result[seg.Id] = cells;
                }
            }

            return;
        }

        // Build flat segment lists — identical logic to BuildRouteDraftCells
        var segmentLists = new List<List<MutableCell>>(anchors.Count - 1);
        for (var i = 1; i < anchors.Count; i++)
        {
            segmentLists.Add(BuildSegmentCells(anchors[i - 1], anchors[i], skipFirst: i > 1));
        }

        // Apply corner joins between adjacent segment lists
        for (var i = 1; i < segmentLists.Count; i++)
        {
            ApplySegmentCornerJoin(segmentLists[i - 1], segmentLists[i]);
        }

        // Map each segment list back to its owning SceneObject
        for (var k = 0; k < chain.Count && k < segmentLists.Count; k++)
        {
            if (segmentLists[k].Count > 0)
            {
                result[chain[k].Id] = segmentLists[k];
            }
        }
    }

    private static bool IsAdjacent(VoxelCoord a, VoxelCoord b)
        => a.Z == b.Z && Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) == 1;

    private static List<VoxelCoord> NormalizeAnchors(IReadOnlyList<VoxelCoord> anchors)
    {
        var result = new List<VoxelCoord>(anchors.Count);
        foreach (var anchor in anchors)
        {
            if (result.Count == 0 || result[^1] != anchor)
            {
                result.Add(anchor);
            }
        }

        return result;
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
