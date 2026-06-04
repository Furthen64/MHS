using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Mhs.Editor.Viewport;

namespace Mhs.Editor.Editor;

public sealed class OrePacket
{
    public string MaterialId { get; set; } = "DebugOre";
}

public sealed class ConveyorRouteRuntime
{
    public ConveyorRouteRuntime(
        string key,
        IReadOnlyList<Guid> segmentObjectIds,
        IReadOnlyList<VoxelCoord> cells,
        Guid? senderObjectId,
        Guid? receiverObjectId)
    {
        Key = key;
        SegmentObjectIds = segmentObjectIds;
        Cells = cells;
        Slots = new OrePacket?[cells.Count];
        SenderObjectId = senderObjectId;
        ReceiverObjectId = receiverObjectId;
    }

    public string Key { get; }
    public IReadOnlyList<Guid> SegmentObjectIds { get; }
    public IReadOnlyList<VoxelCoord> Cells { get; }
    public OrePacket?[] Slots { get; }
    public float StepTimer { get; set; }
    public float SecondsPerStep { get; set; } = 0.35f;
    public Guid? SenderObjectId { get; set; }
    public Guid? ReceiverObjectId { get; set; }
}

public sealed class ConveyorRouteMaterialFlowSimulator
{
    private readonly List<ConveyorRouteRuntime> _routes = [];

    public IReadOnlyList<ConveyorRouteRuntime> Routes => new ReadOnlyCollection<ConveyorRouteRuntime>(_routes);

    public void Clear()
    {
        _routes.Clear();
    }

    public int OccupiedCellCount()
        => _routes.Sum(route => route.Slots.Count(slot => slot is not null));

    public bool HasPacketAtCell(VoxelCoord position)
    {
        foreach (var route in _routes)
        {
            for (var i = 0; i < route.Cells.Count; i++)
            {
                if (route.Cells[i] == position && route.Slots[i] is not null)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public int Step(PortConnectivitySnapshot snapshot, IReadOnlyList<SceneObject> sceneObjects)
    {
        SyncRoutes(snapshot, sceneObjects);
        var moved = 0;
        foreach (var route in _routes)
        {
            moved += AdvanceOneStep(route);
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
            route.StepTimer += dtSeconds;
            while (route.StepTimer >= route.SecondsPerStep)
            {
                route.StepTimer -= route.SecondsPerStep;
                moved += AdvanceOneStep(route);
            }
        }

        return moved;
    }

    public bool TryInjectFromSender(Guid senderObjectId)
    {
        var route = _routes.FirstOrDefault(candidate => candidate.SenderObjectId == senderObjectId);
        if (route is null || route.Slots.Length == 0 || route.Slots[0] is not null)
        {
            return false;
        }

        route.Slots[0] = new OrePacket();
        return true;
    }

    private void SyncRoutes(PortConnectivitySnapshot snapshot, IReadOnlyList<SceneObject> sceneObjects)
    {
        var objectById = sceneObjects.ToDictionary(objectRef => objectRef.Id);
        var conveyorCellsByObject = ConveyorRouteCellVisualization.BuildSceneObjectCells(sceneObjects);
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

        var existingByKey = _routes.ToDictionary(route => route.Key, StringComparer.Ordinal);
        _routes.Clear();
        foreach (var descriptor in descriptors)
        {
            if (existingByKey.TryGetValue(descriptor.Key, out var existing)
                && existing.Cells.SequenceEqual(descriptor.Cells))
            {
                existing.SenderObjectId = descriptor.SenderObjectId;
                existing.ReceiverObjectId = descriptor.ReceiverObjectId;
                _routes.Add(existing);
                continue;
            }

            _routes.Add(new ConveyorRouteRuntime(
                descriptor.Key,
                descriptor.SegmentObjectIds,
                descriptor.Cells,
                descriptor.SenderObjectId,
                descriptor.ReceiverObjectId));
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
        Guid? sender = null;
        Guid? receiver = null;

        foreach (var connection in snapshot.Connections)
        {
            if (connection.ToObjectId == headId
                && objectById.TryGetValue(connection.FromObjectId, out var fromObject)
                && string.Equals(fromObject.PartId, "mtrlsrc", StringComparison.OrdinalIgnoreCase))
            {
                sender = fromObject.Id;
            }

            if (connection.FromObjectId == tailId
                && objectById.TryGetValue(connection.ToObjectId, out var toObject)
                && string.Equals(toObject.PartId, "mtrlrecv", StringComparison.OrdinalIgnoreCase))
            {
                receiver = toObject.Id;
            }
        }

        var key = string.Join(">", chainObjectIds.Select(id => id.ToString("N")));
        return new RouteDescriptor(key, chainObjectIds.ToArray(), cells, sender, receiver);
    }

    private static int AdvanceOneStep(ConveyorRouteRuntime route)
    {
        if (route.Slots.Length == 0)
        {
            return 0;
        }

        var changes = 0;
        var last = route.Slots.Length - 1;

        if (route.Slots[last] is not null && route.ReceiverObjectId.HasValue)
        {
            route.Slots[last] = null;
            changes++;
        }

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

        if (route.Slots[0] is null && route.SenderObjectId.HasValue)
        {
            route.Slots[0] = new OrePacket();
            changes++;
        }

        return changes;
    }

    private sealed record RouteDescriptor(
        string Key,
        IReadOnlyList<Guid> SegmentObjectIds,
        IReadOnlyList<VoxelCoord> Cells,
        Guid? SenderObjectId,
        Guid? ReceiverObjectId);
}
