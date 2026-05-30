namespace Mhs.Editor.Editor;

public sealed class PlacePartTool : IEditorTool
{
    private readonly PartDefinition _partDefinition;

    public PlacePartTool(PartDefinition partDefinition)
    {
        _partDefinition = partDefinition;
    }

    public string Name => _partDefinition.DisplayName;

    public void OnPointerMoved(ViewportPointerContext context)
    {
        context.EditorState.HoveredVoxel = context.HoveredVoxel;
        context.EditorState.HoveredObject = null;
        UpdateGhost(context.EditorState);
    }

    public void OnPointerPressed(ViewportPointerContext context)
    {
        var state = context.EditorState;
        var hovered = state.HoveredVoxel;
        if (!hovered.HasValue)
        {
            return;
        }

        var position = hovered.Value with { Z = state.ActiveAbsoluteZ };
        var rotation = state.ActivePlacementRotationZDegrees;
        var effectiveSize = RotationHelper.GetEffectiveSize(_partDefinition.Size, rotation);
        var validation = state.ValidatePartPlacement(_partDefinition, position, rotation);
        if (!state.FitsWithinActiveFloor(position, effectiveSize) || !validation.IsValid)
        {
            state.StatusMessage = $"Blocked | Placement blocked: {validation.Reason ?? "invalid"}";
            return;
        }

        var sceneObject = new SceneObject
        {
            PartType = _partDefinition.DisplayName,
            Position = position,
            BaseSize = _partDefinition.Size,
            RotationZDegrees = rotation
        };

        state.Scene.Objects.Add(sceneObject);
        state.SelectedObject = sceneObject;
        state.StatusMessage = "Ready";
    }

    public void OnPointerReleased(ViewportPointerContext context)
    {
    }

    public void OnCancel(EditorState editorState)
    {
        editorState.GhostPreview = null;
    }

    public void RefreshPreview(EditorState editorState) => UpdateGhost(editorState);

    private void UpdateGhost(EditorState state)
    {
        if (!state.HoveredVoxel.HasValue)
        {
            state.GhostPreview = null;
            return;
        }

        var hovered = state.HoveredVoxel.Value;
        var voxel = new VoxelCoord(hovered.X, hovered.Y, state.ActiveAbsoluteZ);
        var rotation = state.ActivePlacementRotationZDegrees;
        var effectiveSize = RotationHelper.GetEffectiveSize(_partDefinition.Size, rotation);
        if (!state.FitsWithinActiveFloor(voxel, effectiveSize))
        {
            state.GhostPreview = null;
            return;
        }

        var validation = state.ValidatePartPlacement(_partDefinition, voxel, rotation);

        state.GhostPreview = new GhostPreview
        {
            Part = _partDefinition,
            Position = voxel,
            RotationZDegrees = rotation,
            IsValid = validation.IsValid,
            InvalidReason = validation.Reason
        };
    }
}
