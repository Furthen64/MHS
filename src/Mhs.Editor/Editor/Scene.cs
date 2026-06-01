using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Mhs.Editor.Editor;

public sealed class Scene
{
    public ObservableCollection<SceneObject> Objects { get; } = [];

    public PortConnectivitySnapshot GetPortConnectivitySnapshot()
        => PortConnectivityAnalyzer.Analyze(Objects);

    public IReadOnlyList<ScenePort> GetPorts()
        => GetPortConnectivitySnapshot().Ports;
}
