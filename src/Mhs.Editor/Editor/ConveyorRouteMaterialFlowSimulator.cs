using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Mhs.Editor.Viewport;

namespace Mhs.Editor.Editor;

public sealed class OrePacket
{
    public string MaterialId { get; set; } = SceneObject.DefaultMaterialId;
    public int UnitCount { get; set; } = 1;
}

public enum RouteInputAttachmentStatus
{
    WaitingForRate,
    WaitingForSlot,
    WaitingForTurn,
    Injected
}

public sealed class RouteInputAttachmentRuntime
{
    public Guid ObjectId { get; init; }
    public int RouteCellIndex { get; init; }
    public float UnitsPerSecond { get; set; } = SceneObject.DefaultMaterialUnitsPerSecond;
    public int GranulesPerPacket { get; set; } = SceneObject.DefaultMaterialGranulesPerPacket;
    public float Accumulator { get; set; }
    public string MaterialId { get; set; } = SceneObject.DefaultMaterialId;
    public RouteInputAttachmentStatus LastStatus { get; set; } = RouteInputAttachmentStatus.WaitingForRate;
    public float OutputPulseTimer { get; set; }
    public string LastInjectedMaterialId { get; set; } = SceneObject.DefaultMaterialId;
}

public readonly record struct SourceOutputPulseState(
    VoxelCoord TargetCell,
    string MaterialId,
    float NormalizedIntensity);

public sealed class ConveyorRouteRuntime
{
    public ConveyorRouteRuntime(
        string key,
        IReadOnlyList<Guid> segmentObjectIds,
        IReadOnlyList<VoxelCoord> cells,
        IReadOnlyList<RouteInputAttachmentRuntime> inputAttachments,
        Guid? receiverObjectId)
    {
        Key = key;
        SegmentObjectIds = segmentObjectIds;
        Cells = cells;
        Slots = new OrePacket?[cells.Count];
        InputAttachments = inputAttachments;
        ReceiverObjectId = receiverObjectId;
    }

    public string Key { get; }
    public IReadOnlyList<Guid> SegmentObjectIds { get; }
    public IReadOnlyList<VoxelCoord> Cells { get; }
    public OrePacket?[] Slots { get; }
    public IReadOnlyList<RouteInputAttachmentRuntime> InputAttachments { get; set; }
    public float StepTimer { get; set; }
    public float SecondsPerStep { get; set; } = 0.35f;
    public Guid? ReceiverObjectId { get; set; }
    public Dictionary<int, int> NextInputAttachmentIndexByCell { get; } = [];
}

public sealed class ConveyorRouteMaterialFlowSimulator
{
    private const float SourceOutputPulseDurationSeconds = 0.28f;
    private readonly List<ConveyorRouteRuntime> _routes = [];

    public IReadOnlyList<ConveyorRouteRuntime> Routes => new ReadOnlyCollection<ConveyorRouteRuntime>(_routes);

    public void Clear()
    {
        _routes.Clear();
    }

    public int OccupiedCellCount()
        => _routes.Sum(route => route.Slots.Count(slot => slot is not null));

    public bool HasPacketAtCell(VoxelCoord position)
        => TryGetPacketAtCell(position, out _);

    public bool TryGetPacketAtCell(VoxelCoord position, out OrePacket? packet)
    {
        foreach (var route in _routes)
        {
            for (var i = 0; i < route.Cells.Count; i++)
            {
                if (route.Cells[i] == position && route.Slots[i] is not null)
                {
                    packet = route.Slots[i];
                    return true;
                }
            }
        }

        packet = null;
        return false;
    }

    public int Step(PortConnectivitySnapshot snapshot, IReadOnlyList<SceneObject> sceneObjects)
    {
        SyncRoutes(snapshot, sceneObjects);
        var moved = 0;
        foreach (var route in _routes)
        {
            moved += SimulateRouteStep(route);
        }

        return moved;
    }

    public int Update(float dtSeconds, PortConnectivitySnapshot snapshot, IReadOnlyList<SceneObject> sceneObjects)
    {
        SyncRoutes(snapshot, sceneObjects);

        if (dtSeconds <= 0f)
        {
            return 0;
        }

        var moved = 0;
        foreach (var route in _routes)
        {
            AccumulateInputAttachments(route, dtSeconds);
            route.StepTimer += dtSeconds;
            var stepped = false;
            while (route.StepTimer >= route.SecondsPerStep)
            {
                route.StepTimer -= route.SecondsPerStep;
                moved += SimulateRouteStep(route);
                stepped = true;
            }

            if (!stepped)
            {
                moved += ProcessInputAttachments(route);
            }
        }

        return moved;
    }

    public bool TryInjectFromSender(Guid senderObjectId)
    {
        var route = _routes.FirstOrDefault(candidate => candidate.InputAttachments.Any(source => source.ObjectId == senderObjectId));
        if (route is null || route.Slots.Length == 0)
        {
            return false;
        }

        var attachment = route.InputAttachments.First(candidate => candidate.ObjectId == senderObjectId);
        if (attachment.RouteCellIndex < 0
            || attachment.RouteCellIndex >= route.Slots.Length
            || route.Slots[attachment.RouteCellIndex] is not null)
        {
            return false;
        }

        route.Slots[attachment.RouteCellIndex] = new OrePacket
        {
            MaterialId = MaterialCatalog.NormalizeId(attachment.MaterialId),
            UnitCount = NormalizeGranulesPerPacket(attachment.GranulesPerPacket)
        };
        attachment.OutputPulseTimer = SourceOutputPulseDurationSeconds;
        attachment.LastInjectedMaterialId = route.Slots[attachment.RouteCellIndex]!.MaterialId;
        attachment.LastStatus = RouteInputAttachmentStatus.Injected;
        return true;
    }

    public bool TryGetSourceOutputPulse(Guid sourceObjectId, out SourceOutputPulseState pulse)
    {
        foreach (var route in _routes)
        {
            var attachment = route.InputAttachments.FirstOrDefault(candidate => candidate.ObjectId == sourceObjectId);
            if (attachment is null
                || attachment.OutputPulseTimer <= 0f
                || attachment.RouteCellIndex < 0
                || attachment.RouteCellIndex >= route.Cells.Count)
            {
                continue;
            }

            var normalized = Math.Clamp(attachment.OutputPulseTimer / SourceOutputPulseDurationSeconds, 0f, 1f);
            pulse = new SourceOutputPulseState(
                route.Cells[attachment.RouteCellIndex],
                MaterialCatalog.NormalizeId(attachment.LastInjectedMaterialId),
                normalized);
            return true;
        }

        pulse = default;
        return false;
    }

    private void SyncRoutes(PortConnectivitySnapshot snapshot, IReadOnlyList<SceneObject> sceneObjects)
    {
        var objectById = sceneObjects.ToDictionary(objectRef => objectRef.Id);
        var conveyorCellsByObject = ConveyorRouteCellVisualization.BuildSceneObjectCells(sceneObjects);
        var routeSegments = sceneObjects
            .Where(sceneObject => sceneObject.IsRouteConveyorSegment)
            .ToArray();
        var materialSources = sceneObjects
            .Where(sceneObject => string.Equals(sceneObject.PartId, "mtrlsrc", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var conveyorIds = sceneObjects
            .Where(sceneObject => sceneObject.IsConveyor)
            .Select(sceneObject => sceneObject.Id)
            .ToHashSet();

        var nextByConveyorId = new Dictionary<Guid, Guid>();
        var previousByConveyorId = new Dictionary<Guid, Guid>();
        foreach (var connection in snapshot.Connections)
        {
            if (!conveyorIds.Contains(connection.FromObjectId) || !conveyorIds.Contains(connection.ToObjectId))
            {
                continue;
            }

            nextByConveyorId[connection.FromObjectId] = connection.ToObjectId;
            previousByConveyorId[connection.ToObjectId] = connection.FromObjectId;
        }

        MergeRouteSegmentCornerAdjacency(routeSegments, nextByConveyorId, previousByConveyorId);

        var chainHeads = conveyorIds.Where(id => !previousByConveyorId.ContainsKey(id)).ToList();
        var visited = new HashSet<Guid>();
        var descriptors = new List<RouteDescriptor>();

        foreach (var headId in chainHeads)
        {
            var chain = BuildChain(headId, nextByConveyorId, visited);
            if (chain.Count > 0)
            {
                descriptors.Add(BuildDescriptor(chain, snapshot, objectById, conveyorCellsByObject));
            }
        }

        foreach (var conveyorId in conveyorIds)
        {
            if (visited.Contains(conveyorId))
            {
                continue;
            }

            var chain = BuildChain(conveyorId, nextByConveyorId, visited);
            if (chain.Count > 0)
            {
                descriptors.Add(BuildDescriptor(chain, snapshot, objectById, conveyorCellsByObject));
            }
        }

        descriptors = AssignInputAttachments(descriptors, materialSources);

        var existingByKey = _routes.ToDictionary(route => route.Key, StringComparer.Ordinal);
        _routes.Clear();
        foreach (var descriptor in descriptors)
        {
            if (existingByKey.TryGetValue(descriptor.Key, out var existing)
                && existing.Cells.SequenceEqual(descriptor.Cells))
            {
                existing.ReceiverObjectId = descriptor.ReceiverObjectId;
                existing.InputAttachments = MergeInputAttachments(existing.InputAttachments, descriptor.InputAttachments, objectById);
                NormalizeInputAttachmentTurnState(existing);
                _routes.Add(existing);
                continue;
            }

            var runtime = new ConveyorRouteRuntime(
                descriptor.Key,
                descriptor.SegmentObjectIds,
                descriptor.Cells,
                BuildInputAttachments(descriptor.InputAttachments, objectById),
                descriptor.ReceiverObjectId);
            NormalizeInputAttachmentTurnState(runtime);
            _routes.Add(runtime);
        }
    }

    private static List<Guid> BuildChain(Guid headId, IReadOnlyDictionary<Guid, Guid> nextByConveyorId, HashSet<Guid> visited)
    {
        var chain = new List<Guid>();
        var current = headId;
        while (!visited.Contains(current))
        {
            visited.Add(current);
            chain.Add(current);

            if (!nextByConveyorId.TryGetValue(current, out var next))
            {
                break;
            }

            current = next;
        }

        return chain;
    }

    private static void MergeRouteSegmentCornerAdjacency(
        IReadOnlyList<SceneObject> routeSegments,
        IDictionary<Guid, Guid> nextByConveyorId,
        IDictionary<Guid, Guid> previousByConveyorId)
    {
        foreach (var segment in routeSegments)
        {
            if (nextByConveyorId.ContainsKey(segment.Id))
            {
                continue;
            }

            var (_, segmentEnd) = segment.GetConveyorFlowEndpoints();
            SceneObject? next = null;
            foreach (var candidate in routeSegments)
            {
                if (candidate.Id == segment.Id || previousByConveyorId.ContainsKey(candidate.Id))
                {
                    continue;
                }

                var (candidateStart, _) = candidate.GetConveyorFlowEndpoints();
                if (!IsAdjacent(segmentEnd, candidateStart))
                {
                    continue;
                }

                if (next is not null)
                {
                    next = null;
                    break;
                }

                next = candidate;
            }

            if (next is null)
            {
                continue;
            }

            nextByConveyorId[segment.Id] = next.Id;
            previousByConveyorId[next.Id] = segment.Id;
        }
    }

    private static RouteDescriptor BuildDescriptor(
        IReadOnlyList<Guid> chainObjectIds,
        PortConnectivitySnapshot snapshot,
        IReadOnlyDictionary<Guid, SceneObject> objectById,
        IReadOnlyDictionary<Guid, IReadOnlyList<ConveyorVisualCell>> conveyorCellsByObject)
    {
        var cells = new List<VoxelCoord>();
        foreach (var objectId in chainObjectIds)
        {
            if (!conveyorCellsByObject.TryGetValue(objectId, out var segmentCells))
            {
                continue;
            }

            cells.AddRange(segmentCells.Select(cell => cell.Position));
        }

        var headId = chainObjectIds[0];
        var tailId = chainObjectIds[^1];
        Guid? receiver = null;

        foreach (var connection in snapshot.Connections)
        {
            if (connection.FromObjectId == tailId
                && objectById.TryGetValue(connection.ToObjectId, out var toObject)
                && string.Equals(toObject.PartId, "mtrlrecv", StringComparison.OrdinalIgnoreCase))
            {
                receiver = toObject.Id;
            }
        }

        var key = string.Join(">", chainObjectIds.Select(id => id.ToString("N")));
        return new RouteDescriptor(key, chainObjectIds.ToArray(), cells, Array.Empty<RouteInputAttachmentDescriptor>(), receiver);
    }

    private static int SimulateRouteStep(ConveyorRouteRuntime route)
    {
        var changes = ConsumeAtReceiver(route);
        changes += MovePacketsForward(route);
        changes += ProcessInputAttachments(route);
        return changes;
    }

    private static int ConsumeAtReceiver(ConveyorRouteRuntime route)
    {
        if (route.Slots.Length == 0)
        {
            return 0;
        }

        var last = route.Slots.Length - 1;
        if (route.Slots[last] is not null && route.ReceiverObjectId.HasValue)
        {
            route.Slots[last] = null;
            return 1;
        }

        return 0;
    }

    private static int MovePacketsForward(ConveyorRouteRuntime route)
    {
        if (route.Slots.Length == 0)
        {
            return 0;
        }

        var changes = 0;
        var last = route.Slots.Length - 1;
        for (var i = last - 1; i >= 0; i--)
        {
            if (route.Slots[i] is null || route.Slots[i + 1] is not null)
            {
                continue;
            }

            route.Slots[i + 1] = route.Slots[i];
            route.Slots[i] = null;
            changes++;
        }

        return changes;
    }

    private static void AccumulateInputAttachments(ConveyorRouteRuntime route, float dtSeconds)
    {
        if (route.InputAttachments.Count == 0 || route.Slots.Length == 0 || dtSeconds <= 0f)
        {
            return;
        }

        foreach (var input in route.InputAttachments)
        {
            input.OutputPulseTimer = Math.Max(0f, input.OutputPulseTimer - dtSeconds);
            if (input.UnitsPerSecond <= 0f)
            {
                input.Accumulator = 0f;
                continue;
            }

            input.Accumulator += dtSeconds * input.UnitsPerSecond;
        }
    }

    private static int ProcessInputAttachments(ConveyorRouteRuntime route)
    {
        if (route.InputAttachments.Count == 0 || route.Slots.Length == 0)
        {
            return 0;
        }

        var injected = 0;
        foreach (var group in route.InputAttachments
                     .Where(static attachment => attachment.RouteCellIndex >= 0)
                     .GroupBy(attachment => attachment.RouteCellIndex)
                     .OrderBy(group => group.Key))
        {
            var cellIndex = group.Key;
            if (cellIndex < 0 || cellIndex >= route.Slots.Length)
            {
                continue;
            }

            var contenders = group
                .OrderBy(attachment => attachment.ObjectId)
                .ToArray();

            if (route.Slots[cellIndex] is not null)
            {
                foreach (var contender in contenders)
                {
                    contender.LastStatus = RouteInputAttachmentStatus.WaitingForSlot;
                    ClampAccumulator(contender);
                }

                continue;
            }

            var start = NormalizeTurnIndex(
                route.NextInputAttachmentIndexByCell.TryGetValue(cellIndex, out var nextIndex) ? nextIndex : 0,
                contenders.Length);
            var selectedIndex = -1;
            for (var offset = 0; offset < contenders.Length; offset++)
            {
                var contenderIndex = (start + offset) % contenders.Length;
                if (contenders[contenderIndex].Accumulator >= 1f)
                {
                    selectedIndex = contenderIndex;
                    break;
                }
            }

            if (selectedIndex < 0)
            {
                foreach (var contender in contenders)
                {
                    contender.LastStatus = RouteInputAttachmentStatus.WaitingForRate;
                    ClampAccumulator(contender);
                }

                continue;
            }

            for (var i = 0; i < contenders.Length; i++)
            {
                if (i == selectedIndex)
                {
                    continue;
                }

                contenders[i].LastStatus = contenders[i].Accumulator >= 1f
                    ? RouteInputAttachmentStatus.WaitingForTurn
                    : RouteInputAttachmentStatus.WaitingForRate;
                ClampAccumulator(contenders[i]);
            }

            var selected = contenders[selectedIndex];
            var unitCount = NormalizeGranulesPerPacket(selected.GranulesPerPacket);
            route.Slots[cellIndex] = new OrePacket
            {
                MaterialId = MaterialCatalog.NormalizeId(selected.MaterialId),
                UnitCount = unitCount
            };
            selected.Accumulator = Math.Max(0f, selected.Accumulator - 1f);
            selected.OutputPulseTimer = SourceOutputPulseDurationSeconds;
            selected.LastInjectedMaterialId = route.Slots[cellIndex]!.MaterialId;
            selected.LastStatus = RouteInputAttachmentStatus.Injected;
            route.NextInputAttachmentIndexByCell[cellIndex] = NormalizeTurnIndex(selectedIndex + 1, contenders.Length);
            injected++;
        }

        return injected;
    }

    private static void ClampAccumulator(RouteInputAttachmentRuntime input)
    {
        if (input.Accumulator > 16f)
        {
            input.Accumulator = 16f;
        }
    }

    private static int NormalizeTurnIndex(int nextIndex, int count)
        => count <= 0 ? 0 : ((nextIndex % count) + count) % count;

    private static int NormalizeGranulesPerPacket(int granulesPerPacket)
        => granulesPerPacket switch
        {
            1 or 5 or 10 or 50 or 100 => granulesPerPacket,
            _ => SceneObject.DefaultMaterialGranulesPerPacket
        };

    private static List<RouteInputAttachmentRuntime> BuildInputAttachments(
        IReadOnlyList<RouteInputAttachmentDescriptor> inputAttachments,
        IReadOnlyDictionary<Guid, SceneObject> objectById)
    {
        var attachments = new List<RouteInputAttachmentRuntime>(inputAttachments.Count);
        foreach (var descriptor in inputAttachments)
        {
            if (!objectById.TryGetValue(descriptor.SourceObjectId, out var sourceObject))
            {
                continue;
            }

            attachments.Add(new RouteInputAttachmentRuntime
            {
                ObjectId = descriptor.SourceObjectId,
                RouteCellIndex = descriptor.RouteCellIndex,
                UnitsPerSecond = Math.Max(0f, sourceObject.MaterialUnitsPerSecond),
                GranulesPerPacket = NormalizeGranulesPerPacket(sourceObject.MaterialGranulesPerPacket),
                MaterialId = MaterialCatalog.NormalizeId(sourceObject.MaterialId)
            });
        }

        return attachments;
    }

    private static IReadOnlyList<RouteInputAttachmentRuntime> MergeInputAttachments(
        IReadOnlyList<RouteInputAttachmentRuntime> existingAttachments,
        IReadOnlyList<RouteInputAttachmentDescriptor> inputAttachments,
        IReadOnlyDictionary<Guid, SceneObject> objectById)
    {
        var existingByKey = existingAttachments.ToDictionary(
            attachment => (attachment.ObjectId, attachment.RouteCellIndex));
        var merged = new List<RouteInputAttachmentRuntime>(inputAttachments.Count);
        foreach (var descriptor in inputAttachments)
        {
            if (!objectById.TryGetValue(descriptor.SourceObjectId, out var sourceObject))
            {
                continue;
            }

            if (existingByKey.TryGetValue((descriptor.SourceObjectId, descriptor.RouteCellIndex), out var existing))
            {
                existing.UnitsPerSecond = Math.Max(0f, sourceObject.MaterialUnitsPerSecond);
                existing.GranulesPerPacket = NormalizeGranulesPerPacket(sourceObject.MaterialGranulesPerPacket);
                existing.MaterialId = MaterialCatalog.NormalizeId(sourceObject.MaterialId);
                merged.Add(existing);
                continue;
            }

            merged.Add(new RouteInputAttachmentRuntime
            {
                ObjectId = descriptor.SourceObjectId,
                RouteCellIndex = descriptor.RouteCellIndex,
                UnitsPerSecond = Math.Max(0f, sourceObject.MaterialUnitsPerSecond),
                GranulesPerPacket = NormalizeGranulesPerPacket(sourceObject.MaterialGranulesPerPacket),
                MaterialId = MaterialCatalog.NormalizeId(sourceObject.MaterialId)
            });
        }

        return merged;
    }

    private static List<RouteDescriptor> AssignInputAttachments(
        IReadOnlyList<RouteDescriptor> descriptors,
        IReadOnlyList<SceneObject> materialSources)
    {
        var attachmentsByRouteKey = new Dictionary<string, List<RouteInputAttachmentDescriptor>>(StringComparer.Ordinal);
        foreach (var source in materialSources.OrderBy(source => source.Id))
        {
            if (!TryFindBestInputAttachment(descriptors, source, out var routeKey, out var routeCellIndex))
            {
                continue;
            }

            if (!attachmentsByRouteKey.TryGetValue(routeKey, out var attachments))
            {
                attachments = [];
                attachmentsByRouteKey[routeKey] = attachments;
            }

            attachments.Add(new RouteInputAttachmentDescriptor(source.Id, routeCellIndex));
        }

        return descriptors
            .Select(descriptor => descriptor with
            {
                InputAttachments = attachmentsByRouteKey.TryGetValue(descriptor.Key, out var attachments)
                    ? attachments
                        .OrderBy(attachment => attachment.RouteCellIndex)
                        .ThenBy(attachment => attachment.SourceObjectId)
                        .ToArray()
                    : Array.Empty<RouteInputAttachmentDescriptor>()
            })
            .ToList();
    }

    private static bool TryFindBestInputAttachment(
        IReadOnlyList<RouteDescriptor> descriptors,
        SceneObject source,
        out string routeKey,
        out int routeCellIndex)
    {
        routeKey = string.Empty;
        routeCellIndex = -1;
        var bestScore = double.MaxValue;
        var found = false;

        var outputPort = GetMaterialSourceOutputPort(source);
        foreach (var descriptor in descriptors)
        {
            for (var cellIndex = 0; cellIndex < descriptor.Cells.Count; cellIndex++)
            {
                var cell = descriptor.Cells[cellIndex];
                if (!IsCellAdjacentToObject(source, cell))
                {
                    continue;
                }

                var dx = cell.X + 0.5 - outputPort.X;
                var dy = cell.Y + 0.5 - outputPort.Y;
                var dz = cell.Z + 0.5 - outputPort.Z;
                var score = dx * dx + dy * dy + dz * dz;
                if (!found
                    || score < bestScore
                    || (Math.Abs(score - bestScore) < 0.0001
                        && (string.CompareOrdinal(descriptor.Key, routeKey) < 0
                            || (string.Equals(descriptor.Key, routeKey, StringComparison.Ordinal) && cellIndex < routeCellIndex))))
                {
                    found = true;
                    bestScore = score;
                    routeKey = descriptor.Key;
                    routeCellIndex = cellIndex;
                }
            }
        }

        return found;
    }

    private static PortPosition GetMaterialSourceOutputPort(SceneObject source)
    {
        var size = source.EffectiveSize;
        var z = source.Position.Z + Math.Min(size.HeightZ, 1) * 0.5;
        var rotation = RotationHelper.NormalizeDegrees(source.RotationZDegrees);
        var local = rotation switch
        {
            0 => new PortPosition(size.WidthX, size.DepthY / 2.0, 0),
            90 => new PortPosition(size.WidthX / 2.0, size.DepthY, 0),
            180 => new PortPosition(0, size.DepthY / 2.0, 0),
            _ => new PortPosition(size.WidthX / 2.0, 0, 0)
        };

        return new PortPosition(source.Position.X + local.X, source.Position.Y + local.Y, z);
    }

    private static bool IsCellAdjacentToObject(SceneObject sceneObject, VoxelCoord cell)
    {
        if (cell.Z < sceneObject.MinZ || cell.Z > sceneObject.MaxZ)
        {
            return false;
        }

        var adjacentX = (cell.X == sceneObject.MinX - 1 || cell.X == sceneObject.MaxX + 1)
                        && cell.Y >= sceneObject.MinY
                        && cell.Y <= sceneObject.MaxY;
        var adjacentY = (cell.Y == sceneObject.MinY - 1 || cell.Y == sceneObject.MaxY + 1)
                        && cell.X >= sceneObject.MinX
                        && cell.X <= sceneObject.MaxX;
        return adjacentX || adjacentY;
    }

    private static void NormalizeInputAttachmentTurnState(ConveyorRouteRuntime route)
    {
        var validCellIndices = route.InputAttachments
            .GroupBy(attachment => attachment.RouteCellIndex)
            .ToDictionary(group => group.Key, group => group.Count());
        foreach (var cellIndex in route.NextInputAttachmentIndexByCell.Keys.ToArray())
        {
            if (!validCellIndices.TryGetValue(cellIndex, out var contenderCount))
            {
                route.NextInputAttachmentIndexByCell.Remove(cellIndex);
                continue;
            }

            route.NextInputAttachmentIndexByCell[cellIndex] = NormalizeTurnIndex(
                route.NextInputAttachmentIndexByCell[cellIndex],
                contenderCount);
        }
    }

    private sealed record RouteDescriptor(
        string Key,
        IReadOnlyList<Guid> SegmentObjectIds,
        IReadOnlyList<VoxelCoord> Cells,
        IReadOnlyList<RouteInputAttachmentDescriptor> InputAttachments,
        Guid? ReceiverObjectId);

    private sealed record RouteInputAttachmentDescriptor(Guid SourceObjectId, int RouteCellIndex);

    private static bool IsAdjacent(VoxelCoord a, VoxelCoord b)
        => a.Z == b.Z && Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) == 1;
}
