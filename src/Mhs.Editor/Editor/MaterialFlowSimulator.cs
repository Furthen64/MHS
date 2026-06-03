using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Mhs.Editor.Editor;

public enum MaterialKind
{
    DebugOre
}

public enum MaterialTokenState
{
    Active,
    Blocked,
    Consumed
}

public readonly record struct MaterialTokenLocation(Guid ObjectId, string PortId);

public sealed record MaterialToken(
    Guid TokenId,
    MaterialKind MaterialKind,
    MaterialTokenLocation Location,
    MaterialTokenState State,
    string? StatusText = null);

public sealed class MaterialFlowSimulator
{
    private readonly List<MaterialToken> _tokens = [];

    public MaterialToken InjectToken(Guid objectId, string portId, MaterialKind materialKind = MaterialKind.DebugOre)
    {
        var token = new MaterialToken(
            Guid.NewGuid(),
            materialKind,
            new MaterialTokenLocation(objectId, portId),
            MaterialTokenState.Active);
        _tokens.Add(token);
        return token;
    }

    public int Step(PortConnectivitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var movedCount = 0;
        for (var index = 0; index < _tokens.Count; index++)
        {
            var token = _tokens[index];
            if (token.State != MaterialTokenState.Active)
            {
                continue;
            }

            if (!snapshot.TryGetPort(token.Location.PortId, out var port))
            {
                _tokens[index] = token with
                {
                    State = MaterialTokenState.Blocked,
                    StatusText = "port missing"
                };
                continue;
            }

            if (CanOutput(port.Kind))
            {
                var outgoing = snapshot.GetOutgoingConnectionsForPort(port.PortId);
                if (outgoing.Count == 0)
                {
                    _tokens[index] = token with
                    {
                        State = MaterialTokenState.Blocked,
                        StatusText = "no outgoing connection"
                    };
                    continue;
                }

                var next = outgoing[0];
                _tokens[index] = token with
                {
                    Location = new MaterialTokenLocation(next.ToObjectId, next.ToPortId),
                    StatusText = null
                };
                movedCount++;
                continue;
            }

            if (CanInput(port.Kind))
            {
                var targetPort = snapshot.Ports
                    .Where(candidate => candidate.OwnerSceneObjectId == port.OwnerSceneObjectId
                        && !string.Equals(candidate.PortId, port.PortId, StringComparison.Ordinal)
                        && CanOutput(candidate.Kind))
                    .OrderBy(candidate => candidate.PortId, StringComparer.Ordinal)
                    .FirstOrDefault();

                if (targetPort is null)
                {
                    _tokens[index] = token with
                    {
                        State = MaterialTokenState.Blocked,
                        StatusText = "no internal output port"
                    };
                    continue;
                }

                _tokens[index] = token with
                {
                    Location = new MaterialTokenLocation(targetPort.OwnerSceneObjectId, targetPort.PortId),
                    StatusText = null
                };
                movedCount++;
                continue;
            }

            _tokens[index] = token with
            {
                State = MaterialTokenState.Blocked,
                StatusText = "unsupported port kind"
            };
        }

        return movedCount;
    }

    public void ClearTokens() => _tokens.Clear();

    public IReadOnlyList<MaterialToken> GetTokens() => new ReadOnlyCollection<MaterialToken>(_tokens);

    private static bool CanInput(PortKind kind) => kind is PortKind.Input or PortKind.Bidirectional;

    private static bool CanOutput(PortKind kind) => kind is PortKind.Output or PortKind.Bidirectional;
}
