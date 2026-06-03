using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Mhs.Editor.Editor;

public enum PortKind
{
    Input,
    Output,
    Bidirectional
}

public enum PortDirection
{
    PositiveX,
    NegativeX,
    PositiveY,
    NegativeY,
    PositiveZ,
    NegativeZ
}

public enum PortConnectionStatus
{
    Unconnected,
    Connected,
    InvalidNearby,
    AdapterRequired
}

public enum ConnectionInvalidReason
{
    IncompatiblePortKind,
    SameOwner,
    DifferentZ,
    WrongFacing,
    AmbiguousCandidate
}

public enum ConnectionKind
{
    Direct
}

public enum ConnectionCandidateStatus
{
    Direct,
    AdapterRequired,
    Invalid,
    Ambiguous
}

public readonly record struct PortPosition(double X, double Y, double Z);

public sealed record ScenePort(
    string PortId,
    string Name,
    Guid OwnerSceneObjectId,
    PortKind Kind,
    PortPosition LocalPosition,
    PortPosition WorldPosition,
    PortDirection Direction);

public sealed record Connection(
    Guid FromObjectId,
    string FromPortId,
    Guid ToObjectId,
    string ToPortId,
    ConnectionKind ConnectionKind);

public sealed record InvalidConnectionCandidate(
    string PortAId,
    string PortBId,
    ConnectionInvalidReason Reason);

public sealed record ConnectionCandidate(
    Guid FromObjectId,
    string FromPortId,
    Guid ToObjectId,
    string ToPortId,
    ConnectionCandidateStatus Status,
    string Reason,
    IReadOnlyList<string> PossibleAdapters);

public sealed record PortStatusInfo(
    ScenePort Port,
    PortConnectionStatus Status,
    int ConnectionCount,
    int InvalidNearbyCount,
    IReadOnlyList<ConnectionInvalidReason> InvalidReasons,
    string Diagnostic,
    int AdapterRequiredCount,
    IReadOnlyList<ConnectionCandidate> AdapterRequiredCandidates);

public sealed class PortConnectivitySnapshot
{
    private readonly ReadOnlyDictionary<string, PortStatusInfo> _statusByPortId;
    private readonly ReadOnlyDictionary<string, ScenePort> _portByPortId;
    private readonly ReadOnlyDictionary<string, IReadOnlyList<Connection>> _incomingByPortId;
    private readonly ReadOnlyDictionary<string, IReadOnlyList<Connection>> _outgoingByPortId;
    private readonly ReadOnlyDictionary<Guid, IReadOnlyList<Connection>> _incomingByObjectId;
    private readonly ReadOnlyDictionary<Guid, IReadOnlyList<Connection>> _outgoingByObjectId;
    private readonly ReadOnlyDictionary<string, IReadOnlyList<InvalidConnectionCandidate>> _invalidByPortId;
    private readonly ReadOnlyDictionary<string, IReadOnlyList<ConnectionCandidate>> _adapterRequiredByPortId;

    public PortConnectivitySnapshot(
        IReadOnlyList<ScenePort> ports,
        IReadOnlyList<Connection> connections,
        IReadOnlyList<InvalidConnectionCandidate> invalidNearbyCandidates,
        IReadOnlyList<ConnectionCandidate> adapterRequiredCandidates,
        IReadOnlyList<PortStatusInfo> portStatuses)
    {
        Ports = ports;
        Connections = connections;
        InvalidNearbyCandidates = invalidNearbyCandidates;
        AdapterRequiredCandidates = adapterRequiredCandidates;
        PortStatuses = portStatuses;
        _statusByPortId = new ReadOnlyDictionary<string, PortStatusInfo>(portStatuses.ToDictionary(s => s.Port.PortId, StringComparer.Ordinal));
        _portByPortId = new ReadOnlyDictionary<string, ScenePort>(ports.ToDictionary(port => port.PortId, StringComparer.Ordinal));
        _incomingByPortId = ToReadOnlyGroupedDictionary(connections, connection => connection.ToPortId, StringComparer.Ordinal);
        _outgoingByPortId = ToReadOnlyGroupedDictionary(connections, connection => connection.FromPortId, StringComparer.Ordinal);
        _incomingByObjectId = ToReadOnlyGroupedDictionary(connections, connection => connection.ToObjectId);
        _outgoingByObjectId = ToReadOnlyGroupedDictionary(connections, connection => connection.FromObjectId);
        _invalidByPortId = BuildInvalidByPort(invalidNearbyCandidates);
        _adapterRequiredByPortId = BuildAdapterRequiredByPort(adapterRequiredCandidates);
    }

    public IReadOnlyList<ScenePort> Ports { get; }
    public IReadOnlyList<Connection> Connections { get; }
    public IReadOnlyList<InvalidConnectionCandidate> InvalidNearbyCandidates { get; }
    public IReadOnlyList<ConnectionCandidate> AdapterRequiredCandidates { get; }
    public IReadOnlyList<PortStatusInfo> PortStatuses { get; }

    public PortStatusInfo GetPortStatus(ScenePort port)
        => _statusByPortId.TryGetValue(port.PortId, out var status)
            ? status
            : new PortStatusInfo(port, PortConnectionStatus.Unconnected, 0, 0, Array.Empty<ConnectionInvalidReason>(), "unconnected", 0, Array.Empty<ConnectionCandidate>());

    public IReadOnlyList<PortStatusInfo> GetPortStatusesForOwner(Guid ownerSceneObjectId)
        => PortStatuses.Where(status => status.Port.OwnerSceneObjectId == ownerSceneObjectId).ToList();

    public IReadOnlyList<Connection> GetAllConnections()
        => Connections;

    public IReadOnlyList<Connection> GetIncomingConnectionsForObject(Guid objectId)
        => _incomingByObjectId.TryGetValue(objectId, out var connections)
            ? connections
            : Array.Empty<Connection>();

    public IReadOnlyList<Connection> GetOutgoingConnectionsForObject(Guid objectId)
        => _outgoingByObjectId.TryGetValue(objectId, out var connections)
            ? connections
            : Array.Empty<Connection>();

    public IReadOnlyList<Connection> GetIncomingConnectionsForPort(string portId)
        => _incomingByPortId.TryGetValue(portId, out var connections)
            ? connections
            : Array.Empty<Connection>();

    public IReadOnlyList<Connection> GetOutgoingConnectionsForPort(string portId)
        => _outgoingByPortId.TryGetValue(portId, out var connections)
            ? connections
            : Array.Empty<Connection>();

    public IReadOnlyList<InvalidConnectionCandidate> GetInvalidNearbyCandidatesForPort(string portId)
        => _invalidByPortId.TryGetValue(portId, out var candidates)
            ? candidates
            : Array.Empty<InvalidConnectionCandidate>();

    public IReadOnlyList<ConnectionCandidate> GetAdapterRequiredCandidatesForPort(string portId)
        => _adapterRequiredByPortId.TryGetValue(portId, out var candidates)
            ? candidates
            : Array.Empty<ConnectionCandidate>();

    public bool TryGetPort(string portId, out ScenePort port)
        => _portByPortId.TryGetValue(portId, out port!);

    public ScenePort? GetConnectedPeerPort(string portId)
    {
        if (_outgoingByPortId.TryGetValue(portId, out var outgoing) && outgoing.Count > 0)
        {
            return _portByPortId.TryGetValue(outgoing[0].ToPortId, out var target) ? target : null;
        }

        if (_incomingByPortId.TryGetValue(portId, out var incoming) && incoming.Count > 0)
        {
            return _portByPortId.TryGetValue(incoming[0].FromPortId, out var source) ? source : null;
        }

        return null;
    }

    public IReadOnlyList<Guid> GetReachableObjects(Guid startObjectId)
    {
        var visited = new HashSet<Guid>();
        var queue = new Queue<Guid>();
        visited.Add(startObjectId);
        queue.Enqueue(startObjectId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var connection in GetOutgoingConnectionsForObject(current))
            {
                if (visited.Add(connection.ToObjectId))
                {
                    queue.Enqueue(connection.ToObjectId);
                }
            }
        }

        visited.Remove(startObjectId);
        return visited.ToList();
    }

    private static ReadOnlyDictionary<TKey, IReadOnlyList<Connection>> ToReadOnlyGroupedDictionary<TKey>(
        IEnumerable<Connection> connections,
        Func<Connection, TKey> keySelector,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        var grouped = connections
            .GroupBy(keySelector, comparer)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<Connection>)group.ToList(), comparer);
        return new ReadOnlyDictionary<TKey, IReadOnlyList<Connection>>(grouped);
    }

    private static ReadOnlyDictionary<string, IReadOnlyList<InvalidConnectionCandidate>> BuildInvalidByPort(
        IEnumerable<InvalidConnectionCandidate> candidates)
    {
        var grouped = new Dictionary<string, List<InvalidConnectionCandidate>>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            Add(grouped, candidate.PortAId, candidate);
            Add(grouped, candidate.PortBId, candidate);
        }

        return new ReadOnlyDictionary<string, IReadOnlyList<InvalidConnectionCandidate>>(
            grouped.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<InvalidConnectionCandidate>)pair.Value,
                StringComparer.Ordinal));
    }

    private static void Add(IDictionary<string, List<InvalidConnectionCandidate>> grouped, string portId, InvalidConnectionCandidate candidate)
    {
        if (!grouped.TryGetValue(portId, out var list))
        {
            list = [];
            grouped[portId] = list;
        }

        list.Add(candidate);
    }

    private static ReadOnlyDictionary<string, IReadOnlyList<ConnectionCandidate>> BuildAdapterRequiredByPort(
        IEnumerable<ConnectionCandidate> candidates)
    {
        var grouped = new Dictionary<string, List<ConnectionCandidate>>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            AddAdapterCandidate(grouped, candidate.FromPortId, candidate);
            AddAdapterCandidate(grouped, candidate.ToPortId, candidate);
        }

        return new ReadOnlyDictionary<string, IReadOnlyList<ConnectionCandidate>>(
            grouped.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<ConnectionCandidate>)pair.Value,
                StringComparer.Ordinal));
    }

    private static void AddAdapterCandidate(IDictionary<string, List<ConnectionCandidate>> grouped, string portId, ConnectionCandidate candidate)
    {
        if (!grouped.TryGetValue(portId, out var list))
        {
            list = [];
            grouped[portId] = list;
        }

        list.Add(candidate);
    }
}

public static class PortDirectionExtensions
{
    public static PortDirection Opposite(this PortDirection direction) => direction switch
    {
        PortDirection.PositiveX => PortDirection.NegativeX,
        PortDirection.NegativeX => PortDirection.PositiveX,
        PortDirection.PositiveY => PortDirection.NegativeY,
        PortDirection.NegativeY => PortDirection.PositiveY,
        PortDirection.PositiveZ => PortDirection.NegativeZ,
        _ => PortDirection.PositiveZ
    };

    public static (double X, double Y, double Z) ToVector(this PortDirection direction) => direction switch
    {
        PortDirection.PositiveX => (1, 0, 0),
        PortDirection.NegativeX => (-1, 0, 0),
        PortDirection.PositiveY => (0, 1, 0),
        PortDirection.NegativeY => (0, -1, 0),
        PortDirection.PositiveZ => (0, 0, 1),
        _ => (0, 0, -1)
    };
}

public static class PortConnectivityAnalyzer
{
    private const double AdjacencyToleranceXY = 0.15;
    private const double SameZTolerance = 0.01;
    private const double NearbyZTolerance = 1.01;

    public static PortConnectivitySnapshot Analyze(IEnumerable<SceneObject> sceneObjects)
    {
        var ports = BuildPorts(sceneObjects);
        var candidateConnections = new List<Connection>();
        var adapterRequiredCandidates = new List<ConnectionCandidate>();
        var invalidNearbyCandidates = new List<InvalidConnectionCandidate>();
        var invalidReasonsByPortId = new Dictionary<string, HashSet<ConnectionInvalidReason>>(StringComparer.Ordinal);

        for (var i = 0; i < ports.Count; i++)
        {
            for (var j = i + 1; j < ports.Count; j++)
            {
                var a = ports[i];
                var b = ports[j];
                if (!IsNearby(a, b))
                {
                    continue;
                }

                if (TryCreateConnection(a, b, out var connection, out var reason))
                {
                    candidateConnections.Add(connection);
                    continue;
                }

                if (reason == ConnectionInvalidReason.DifferentZ && TryCreateAdapterRequired(a, b, out var adapterCandidate))
                {
                    adapterRequiredCandidates.Add(adapterCandidate);
                    continue;
                }

                var invalid = new InvalidConnectionCandidate(a.PortId, b.PortId, reason);
                invalidNearbyCandidates.Add(invalid);
                AddReason(invalidReasonsByPortId, a.PortId, reason);
                AddReason(invalidReasonsByPortId, b.PortId, reason);
            }
        }

        var ambiguousPortIds = candidateConnections
            .SelectMany(connection => new[] { connection.FromPortId, connection.ToPortId })
            .GroupBy(portId => portId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

        if (ambiguousPortIds.Count > 0)
        {
            var ambiguousInvalid = candidateConnections
                .Where(connection => ambiguousPortIds.Contains(connection.FromPortId) || ambiguousPortIds.Contains(connection.ToPortId))
                .Select(connection => new InvalidConnectionCandidate(connection.FromPortId, connection.ToPortId, ConnectionInvalidReason.AmbiguousCandidate))
                .ToList();

            invalidNearbyCandidates.AddRange(ambiguousInvalid);
            foreach (var invalid in ambiguousInvalid)
            {
                AddReason(invalidReasonsByPortId, invalid.PortAId, invalid.Reason);
                AddReason(invalidReasonsByPortId, invalid.PortBId, invalid.Reason);
            }
        }

        var connections = candidateConnections
            .Where(connection => !ambiguousPortIds.Contains(connection.FromPortId) && !ambiguousPortIds.Contains(connection.ToPortId))
            .ToList();

        var connectionCountByPortId = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var connection in connections)
        {
            Increment(connectionCountByPortId, connection.FromPortId);
            Increment(connectionCountByPortId, connection.ToPortId);
        }

        var invalidCountByPortId = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var invalid in invalidNearbyCandidates)
        {
            Increment(invalidCountByPortId, invalid.PortAId);
            Increment(invalidCountByPortId, invalid.PortBId);
        }

        var adapterCountByPortId = new Dictionary<string, int>(StringComparer.Ordinal);
        var adapterCandidatesByPortId = new Dictionary<string, List<ConnectionCandidate>>(StringComparer.Ordinal);
        foreach (var candidate in adapterRequiredCandidates)
        {
            Increment(adapterCountByPortId, candidate.FromPortId);
            Increment(adapterCountByPortId, candidate.ToPortId);
            AddCandidateToList(adapterCandidatesByPortId, candidate.FromPortId, candidate);
            AddCandidateToList(adapterCandidatesByPortId, candidate.ToPortId, candidate);
        }

        var portStatuses = new List<PortStatusInfo>(ports.Count);
        foreach (var port in ports)
        {
            connectionCountByPortId.TryGetValue(port.PortId, out var connectionCount);
            invalidCountByPortId.TryGetValue(port.PortId, out var invalidCount);
            adapterCountByPortId.TryGetValue(port.PortId, out var adapterCount);
            var reasons = invalidReasonsByPortId.TryGetValue(port.PortId, out var reasonSet)
                ? reasonSet.OrderBy(reason => reason).ToList()
                : [];
            var adapterCandidates = adapterCandidatesByPortId.TryGetValue(port.PortId, out var acList)
                ? (IReadOnlyList<ConnectionCandidate>)acList
                : Array.Empty<ConnectionCandidate>();

            var status = connectionCount > 0
                ? PortConnectionStatus.Connected
                : adapterCount > 0
                    ? PortConnectionStatus.AdapterRequired
                    : invalidCount > 0
                        ? PortConnectionStatus.InvalidNearby
                        : PortConnectionStatus.Unconnected;

            var diagnostic = BuildDiagnostic(status, connectionCount, invalidCount, reasons, adapterCandidates);
            portStatuses.Add(new PortStatusInfo(port, status, connectionCount, invalidCount, reasons, diagnostic, adapterCount, adapterCandidates));
        }

        return new PortConnectivitySnapshot(ports, connections, invalidNearbyCandidates, adapterRequiredCandidates, portStatuses);
    }

    private static List<ScenePort> BuildPorts(IEnumerable<SceneObject> sceneObjects)
    {
        var ports = new List<ScenePort>();
        foreach (var sceneObject in sceneObjects)
        {
            if (TryCreateConveyorPorts(sceneObject, out var input, out var output))
            {
                ports.Add(input);
                ports.Add(output);
            }
        }

        return ports;
    }

    private static bool TryCreateConnection(ScenePort a, ScenePort b, out Connection connection, out ConnectionInvalidReason reason)
    {
        connection = default!;
        reason = ConnectionInvalidReason.IncompatiblePortKind;

        if (a.OwnerSceneObjectId == b.OwnerSceneObjectId)
        {
            reason = ConnectionInvalidReason.SameOwner;
            return false;
        }

        var aIsOutput = CanOutput(a.Kind) && CanInput(b.Kind);
        var bIsOutput = CanOutput(b.Kind) && CanInput(a.Kind);
        if (!aIsOutput && !bIsOutput)
        {
            reason = ConnectionInvalidReason.IncompatiblePortKind;
            return false;
        }

        var output = aIsOutput ? a : b;
        var input = aIsOutput ? b : a;

        if (output.Direction.Opposite() != input.Direction)
        {
            reason = ConnectionInvalidReason.WrongFacing;
            return false;
        }

        if (Math.Abs(a.WorldPosition.Z - b.WorldPosition.Z) > SameZTolerance)
        {
            reason = ConnectionInvalidReason.DifferentZ;
            return false;
        }

        connection = new Connection(output.OwnerSceneObjectId, output.PortId, input.OwnerSceneObjectId, input.PortId, ConnectionKind.Direct);
        return true;
    }

    private static bool TryCreateAdapterRequired(ScenePort a, ScenePort b, out ConnectionCandidate candidate)
    {
        candidate = default!;

        var aIsOutput = CanOutput(a.Kind) && CanInput(b.Kind);
        var bIsOutput = CanOutput(b.Kind) && CanInput(a.Kind);
        if (!aIsOutput && !bIsOutput)
        {
            return false;
        }

        var output = aIsOutput ? a : b;
        var input = aIsOutput ? b : a;

        if (output.Direction.Opposite() != input.Direction)
        {
            return false;
        }

        var dz = input.WorldPosition.Z - output.WorldPosition.Z;
        var absDz = Math.Abs(dz);
        var adapters = SuggestAdapters(dz, absDz);
        var reason = $"height delta {dz:+0.#;-0.#;0} (|Δz|={absDz:0.#})";

        candidate = new ConnectionCandidate(
            output.OwnerSceneObjectId,
            output.PortId,
            input.OwnerSceneObjectId,
            input.PortId,
            ConnectionCandidateStatus.AdapterRequired,
            reason,
            adapters);
        return true;
    }

    private static IReadOnlyList<string> SuggestAdapters(double dz, double absDz)
    {
        var adapters = new List<string>();
        if (absDz > 1.5)
        {
            adapters.Add("VerticalLift");
        }
        else
        {
            adapters.Add("InclinedConveyor");
            if (dz < 0)
            {
                adapters.Add("Chute");
            }
        }

        adapters.Add("ManualAdapter");
        return adapters;
    }

    private static bool TryCreateConveyorPorts(SceneObject sceneObject, out ScenePort input, out ScenePort output)
    {
        input = default!;
        output = default!;
        if (!sceneObject.IsConveyor)
        {
            return false;
        }

        if (sceneObject.IsRouteConveyorSegment && TryBuildRouteConveyorPorts(sceneObject, out input, out output))
        {
            return true;
        }

        var size = sceneObject.EffectiveSize;
        var z = Math.Min(size.HeightZ, 1) * 0.5;
        var rotation = sceneObject.GetConveyorFlowRotationDegrees();

        var (inputLocal, outputLocal, inputDirection, outputDirection) = rotation switch
        {
            0 => (
                new PortPosition(0, size.DepthY / 2.0, z),
                new PortPosition(size.WidthX, size.DepthY / 2.0, z),
                PortDirection.NegativeX,
                PortDirection.PositiveX),
            90 => (
                new PortPosition(size.WidthX / 2.0, 0, z),
                new PortPosition(size.WidthX / 2.0, size.DepthY, z),
                PortDirection.NegativeY,
                PortDirection.PositiveY),
            180 => (
                new PortPosition(size.WidthX, size.DepthY / 2.0, z),
                new PortPosition(0, size.DepthY / 2.0, z),
                PortDirection.PositiveX,
                PortDirection.NegativeX),
            _ => (
                new PortPosition(size.WidthX / 2.0, size.DepthY, z),
                new PortPosition(size.WidthX / 2.0, 0, z),
                PortDirection.PositiveY,
                PortDirection.NegativeY)
        };

        input = CreatePort(sceneObject, "input", "Input", PortKind.Input, inputLocal, inputDirection);
        output = CreatePort(sceneObject, "output", "Output", PortKind.Output, outputLocal, outputDirection);
        return true;
    }

    private static bool TryBuildRouteConveyorPorts(SceneObject sceneObject, out ScenePort input, out ScenePort output)
    {
        input = default!;
        output = default!;

        var (flowStart, flowEnd) = sceneObject.GetConveyorFlowEndpoints();
        var z = sceneObject.Position.Z + 0.5;
        PortPosition inputWorld;
        PortPosition outputWorld;
        PortDirection inputDirection;
        PortDirection outputDirection;

        if (flowStart.X != flowEnd.X)
        {
            var positive = flowEnd.X > flowStart.X;
            inputWorld = new PortPosition(positive ? flowStart.X : flowStart.X + 1, flowStart.Y + 0.5, z);
            outputWorld = new PortPosition(positive ? flowEnd.X + 1 : flowEnd.X, flowEnd.Y + 0.5, z);
            inputDirection = positive ? PortDirection.NegativeX : PortDirection.PositiveX;
            outputDirection = positive ? PortDirection.PositiveX : PortDirection.NegativeX;
        }
        else if (flowStart.Y != flowEnd.Y)
        {
            var positive = flowEnd.Y > flowStart.Y;
            inputWorld = new PortPosition(flowStart.X + 0.5, positive ? flowStart.Y : flowStart.Y + 1, z);
            outputWorld = new PortPosition(flowEnd.X + 0.5, positive ? flowEnd.Y + 1 : flowEnd.Y, z);
            inputDirection = positive ? PortDirection.NegativeY : PortDirection.PositiveY;
            outputDirection = positive ? PortDirection.PositiveY : PortDirection.NegativeY;
        }
        else
        {
            var rotation = sceneObject.GetConveyorFlowRotationDegrees();
            var cell = flowStart;
            (inputWorld, outputWorld, inputDirection, outputDirection) = rotation switch
            {
                0 => (
                    new PortPosition(cell.X, cell.Y + 0.5, z),
                    new PortPosition(cell.X + 1, cell.Y + 0.5, z),
                    PortDirection.NegativeX,
                    PortDirection.PositiveX),
                90 => (
                    new PortPosition(cell.X + 0.5, cell.Y, z),
                    new PortPosition(cell.X + 0.5, cell.Y + 1, z),
                    PortDirection.NegativeY,
                    PortDirection.PositiveY),
                180 => (
                    new PortPosition(cell.X + 1, cell.Y + 0.5, z),
                    new PortPosition(cell.X, cell.Y + 0.5, z),
                    PortDirection.PositiveX,
                    PortDirection.NegativeX),
                _ => (
                    new PortPosition(cell.X + 0.5, cell.Y + 1, z),
                    new PortPosition(cell.X + 0.5, cell.Y, z),
                    PortDirection.PositiveY,
                    PortDirection.NegativeY)
            };
        }

        input = CreateWorldPort(sceneObject, "input", "Input", PortKind.Input, inputWorld, inputDirection);
        output = CreateWorldPort(sceneObject, "output", "Output", PortKind.Output, outputWorld, outputDirection);
        return true;
    }

    private static ScenePort CreatePort(
        SceneObject owner,
        string localPortId,
        string name,
        PortKind kind,
        PortPosition localPosition,
        PortDirection direction)
    {
        var world = new PortPosition(
            owner.Position.X + localPosition.X,
            owner.Position.Y + localPosition.Y,
            owner.Position.Z + localPosition.Z);

        return new ScenePort(
            $"{owner.Id:N}:{localPortId}",
            name,
            owner.Id,
            kind,
            localPosition,
            world,
            direction);
    }

    private static ScenePort CreateWorldPort(
        SceneObject owner,
        string localPortId,
        string name,
        PortKind kind,
        PortPosition worldPosition,
        PortDirection direction)
    {
        var local = new PortPosition(
            worldPosition.X - owner.Position.X,
            worldPosition.Y - owner.Position.Y,
            worldPosition.Z - owner.Position.Z);

        return new ScenePort(
            $"{owner.Id:N}:{localPortId}",
            name,
            owner.Id,
            kind,
            local,
            worldPosition,
            direction);
    }

    private static bool IsNearby(ScenePort a, ScenePort b)
    {
        var dx = a.WorldPosition.X - b.WorldPosition.X;
        var dy = a.WorldPosition.Y - b.WorldPosition.Y;
        var distanceXY = Math.Sqrt(dx * dx + dy * dy);
        if (distanceXY > AdjacencyToleranceXY)
        {
            return false;
        }

        var dz = Math.Abs(a.WorldPosition.Z - b.WorldPosition.Z);
        return dz <= NearbyZTolerance;
    }

    private static bool CanInput(PortKind kind) => kind is PortKind.Input or PortKind.Bidirectional;

    private static bool CanOutput(PortKind kind) => kind is PortKind.Output or PortKind.Bidirectional;

    private static string BuildDiagnostic(
        PortConnectionStatus status,
        int connectionCount,
        int invalidCount,
        IReadOnlyCollection<ConnectionInvalidReason> reasons,
        IReadOnlyList<ConnectionCandidate> adapterCandidates)
    {
        if (status == PortConnectionStatus.Connected && invalidCount == 0 && connectionCount > 0)
        {
            return "connected";
        }

        if (status == PortConnectionStatus.Unconnected)
        {
            return "unconnected";
        }

        if (status == PortConnectionStatus.AdapterRequired)
        {
            var parts = adapterCandidates.Select(c =>
                $"adapter required ({c.Reason}): {string.Join("/", c.PossibleAdapters)}");
            return string.Join("; ", parts);
        }

        var labels = new List<string>();
        if (connectionCount > 1)
        {
            labels.Add("multiple connections");
        }

        labels.AddRange(reasons.Select(ReasonLabel));

        if (status == PortConnectionStatus.Connected && labels.Count == 0)
        {
            return "connected";
        }

        return labels.Count == 0 ? "invalid adjacency" : string.Join(", ", labels.Distinct(StringComparer.Ordinal));
    }

    private static string ReasonLabel(ConnectionInvalidReason reason) => reason switch
    {
        ConnectionInvalidReason.WrongFacing => "wrong-facing",
        ConnectionInvalidReason.IncompatiblePortKind => "incompatible port kind",
        ConnectionInvalidReason.DifferentZ => "different Z",
        ConnectionInvalidReason.SameOwner => "same owner",
        ConnectionInvalidReason.AmbiguousCandidate => "ambiguous candidate",
        _ => "invalid adjacency"
    };

    private static void Increment(IDictionary<string, int> counters, string key)
    {
        if (counters.TryGetValue(key, out var current))
        {
            counters[key] = current + 1;
            return;
        }

        counters[key] = 1;
    }

    private static void AddReason(
        IDictionary<string, HashSet<ConnectionInvalidReason>> reasonsByPortId,
        string portId,
        ConnectionInvalidReason reason)
    {
        if (!reasonsByPortId.TryGetValue(portId, out var set))
        {
            set = new HashSet<ConnectionInvalidReason>();
            reasonsByPortId[portId] = set;
        }

        set.Add(reason);
    }

    private static void AddCandidateToList(
        IDictionary<string, List<ConnectionCandidate>> candidatesByPortId,
        string portId,
        ConnectionCandidate candidate)
    {
        if (!candidatesByPortId.TryGetValue(portId, out var list))
        {
            list = [];
            candidatesByPortId[portId] = list;
        }

        list.Add(candidate);
    }
}
