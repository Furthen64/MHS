using Mhs.Editor.Editor;

namespace Mhs.Editor.ViewModels;

public sealed class SceneTreeNodeViewModel
{
    public SceneTreeNodeViewModel(string label, string statusText, bool isGroupHeader, SceneObject? sceneObject = null)
    {
        Label = label;
        StatusText = statusText;
        IsGroupHeader = isGroupHeader;
        SceneObject = sceneObject;
    }

    public string Label { get; }
    public string StatusText { get; }
    public bool IsGroupHeader { get; }
    public bool HasStatus => !string.IsNullOrEmpty(StatusText);
    public bool IsSelectable => !IsGroupHeader;
    public SceneObject? SceneObject { get; }
}
