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
    Invalid
}

public enum PortAdjacencyIssue
{
    KindMismatch,
    SameObject,
    DifferentZ,
    FacingMismatch
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

public sealed record PortConnection(ScenePort From, ScenePort To);

public sealed record PortAdjacency(ScenePort A, ScenePort B, PortAdjacencyIssue Issue);

public sealed record PortStatusInfo(
    ScenePort Port,
    PortConnectionStatus Status,
    int ValidConnectionCount,
    int InvalidAdjacencyCount);

public sealed class PortConnectivitySnapshot
{
    private readonly ReadOnlyDictionary<string, PortStatusInfo> _statusByPortId;

    public PortConnectivitySnapshot(
        IReadOnlyList<ScenePort> ports,
        IReadOnlyList<PortConnection> connections,
        IReadOnlyList<PortAdjacency> invalidAdjacencies,
        IReadOnlyList<PortStatusInfo> portStatuses)
    {
        Ports = ports;
        Connections = connections;
        InvalidAdjacencies = invalidAdjacencies;
        PortStatuses = portStatuses;
        _statusByPortId = new ReadOnlyDictionary<string, PortStatusInfo>(portStatuses.ToDictionary(s => s.Port.PortId, StringComparer.Ordinal));
    }

    public IReadOnlyList<ScenePort> Ports { get; }
    public IReadOnlyList<PortConnection> Connections { get; }
    public IReadOnlyList<PortAdjacency> InvalidAdjacencies { get; }
    public IReadOnlyList<PortStatusInfo> PortStatuses { get; }

    public PortStatusInfo GetPortStatus(ScenePort port)
        => _statusByPortId.TryGetValue(port.PortId, out var status)
            ? status
            : new PortStatusInfo(port, PortConnectionStatus.Unconnected, 0, 0);

    public IReadOnlyList<PortStatusInfo> GetPortStatusesForOwner(Guid ownerSceneObjectId)
        => PortStatuses.Where(status => status.Port.OwnerSceneObjectId == ownerSceneObjectId).ToList();
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
        var connections = new List<PortConnection>();
        var invalidAdjacencies = new List<PortAdjacency>();
        var validByPortId = new Dictionary<string, int>(StringComparer.Ordinal);
        var invalidByPortId = new Dictionary<string, int>(StringComparer.Ordinal);

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

                if (TryCreateConnection(a, b, out var connection, out var issue))
                {
                    connections.Add(connection);
                    Increment(validByPortId, connection.From.PortId);
                    Increment(validByPortId, connection.To.PortId);
                    continue;
                }

                invalidAdjacencies.Add(new PortAdjacency(a, b, issue));
                Increment(invalidByPortId, a.PortId);
                Increment(invalidByPortId, b.PortId);
            }
        }

        var portStatuses = new List<PortStatusInfo>(ports.Count);
        foreach (var port in ports)
        {
            validByPortId.TryGetValue(port.PortId, out var valid);
            invalidByPortId.TryGetValue(port.PortId, out var invalid);

            var status = valid == 1
                ? PortConnectionStatus.Connected
                : valid > 1 || invalid > 0
                    ? PortConnectionStatus.Invalid
                    : PortConnectionStatus.Unconnected;

            portStatuses.Add(new PortStatusInfo(port, status, valid, invalid));
        }

        return new PortConnectivitySnapshot(ports, connections, invalidAdjacencies, portStatuses);
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

    private static bool TryCreateConnection(ScenePort a, ScenePort b, out PortConnection connection, out PortAdjacencyIssue issue)
    {
        connection = default!;
        issue = PortAdjacencyIssue.KindMismatch;

        if (a.OwnerSceneObjectId == b.OwnerSceneObjectId)
        {
            issue = PortAdjacencyIssue.SameObject;
            return false;
        }

        if (Math.Abs(a.WorldPosition.Z - b.WorldPosition.Z) > SameZTolerance)
        {
            issue = PortAdjacencyIssue.DifferentZ;
            return false;
        }

        if (a.Direction.Opposite() != b.Direction)
        {
            issue = PortAdjacencyIssue.FacingMismatch;
            return false;
        }

        if (CanOutput(a.Kind) && CanInput(b.Kind))
        {
            connection = new PortConnection(a, b);
            return true;
        }

        if (CanOutput(b.Kind) && CanInput(a.Kind))
        {
            connection = new PortConnection(b, a);
            return true;
        }

        issue = PortAdjacencyIssue.KindMismatch;
        return false;
    }

    private static bool TryCreateConveyorPorts(SceneObject sceneObject, out ScenePort input, out ScenePort output)
    {
        input = default!;
        output = default!;
        var isConveyor = string.Equals(sceneObject.PartId, "conveyor", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sceneObject.PartType, "Conveyor", StringComparison.OrdinalIgnoreCase);
        if (!isConveyor)
        {
            return false;
        }

        var size = sceneObject.EffectiveSize;
        var z = Math.Min(size.HeightZ, 1) * 0.5;
        var rotation = RotationHelper.NormalizeDegrees(sceneObject.RotationZDegrees);

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

    private static void Increment(IDictionary<string, int> counters, string key)
    {
        if (counters.TryGetValue(key, out var current))
        {
            counters[key] = current + 1;
            return;
        }

        counters[key] = 1;
    }
}
