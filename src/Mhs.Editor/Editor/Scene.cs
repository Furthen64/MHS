using System.Collections.ObjectModel;

namespace Mhs.Editor.Editor;

public sealed class Scene
{
    public ObservableCollection<SceneObject> Objects { get; } = [];
}
