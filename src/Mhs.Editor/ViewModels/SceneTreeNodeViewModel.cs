using System;
using System.Collections.Generic;
using System.Linq;
using Mhs.Editor.Editor;

namespace Mhs.Editor.ViewModels;

public sealed class SceneTreeNodeViewModel
{
    private readonly HashSet<Guid> _sceneObjectIds;

    public SceneTreeNodeViewModel(
        string label,
        string statusText,
        bool isGroupHeader,
        int indentLevel = 0,
        SceneObject? sceneObject = null,
        IEnumerable<Guid>? relatedSceneObjectIds = null,
        double? focusWorldX = null,
        double? focusWorldY = null,
        double? focusWorldZ = null)
    {
        Label = label;
        StatusText = statusText;
        IsGroupHeader = isGroupHeader;
        IndentLevel = Math.Max(0, indentLevel);
        SceneObject = sceneObject;
        _sceneObjectIds = [];
        if (sceneObject is not null)
        {
            _sceneObjectIds.Add(sceneObject.Id);
        }

        if (relatedSceneObjectIds is not null)
        {
            foreach (var id in relatedSceneObjectIds.Where(id => id != Guid.Empty))
            {
                _sceneObjectIds.Add(id);
            }
        }

        FocusWorldX = focusWorldX;
        FocusWorldY = focusWorldY;
        FocusWorldZ = focusWorldZ;
    }

    public string Label { get; }
    public string StatusText { get; }
    public bool IsGroupHeader { get; }
    public int IndentLevel { get; }
    public string ContentMargin => $"{IndentLevel * 12},0,0,0";
    public bool HasStatus => !string.IsNullOrEmpty(StatusText);
    public bool IsSelectable => !IsGroupHeader;
    public SceneObject? SceneObject { get; }
    public double? FocusWorldX { get; }
    public double? FocusWorldY { get; }
    public double? FocusWorldZ { get; }
    public bool HasFocusTarget => FocusWorldX.HasValue && FocusWorldY.HasValue && FocusWorldZ.HasValue;

    public bool Matches(SceneObject? sceneObject)
        => sceneObject is not null && _sceneObjectIds.Contains(sceneObject.Id);
}
