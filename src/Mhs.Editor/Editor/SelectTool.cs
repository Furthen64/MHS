namespace Mhs.Editor.Editor;

public sealed class SelectTool : IEditorTool
{
    public string Name => "Select";

    public void OnPointerMoved(ViewportPointerContext context)
    {
        var state = context.EditorState;
        state.HoveredVoxel = context.HoveredVoxel;
        state.GhostPreview = null;

        if (state.IsMovingSelection && state.SelectedObject is { } moving)
        {
            if (!context.HoveredVoxel.HasValue)
            {
                state.SetMovePreview(null, false, "No hovered voxel");
                return;
            }

            var target = context.HoveredVoxel.Value with { Z = state.ActiveAbsoluteZ };
            var validation = state.ValidatePlacement(target, moving.EffectiveSize, moving.Id);
            state.SetMovePreview(target, validation.IsValid, validation.Reason);
            return;
        }

        state.SetMovePreview(null, false, null);
        state.HoveredObject = context.HoveredVoxel.HasValue
            ? context.PickObjectAtPoint(context.PointerPoint)
            : null;
    }

    public void OnPointerPressed(ViewportPointerContext context)
    {
        var state = context.EditorState;
        if (state.IsMovingSelection && state.SelectedObject is { } selected)
        {
            if (!state.MovePreviewPosition.HasValue || !state.MovePreviewIsValid)
            {
                state.StatusMessage = $"Blocked | Move blocked: {state.MovePreviewInvalidReason ?? "invalid target"}";
                return;
            }

            selected.Position = state.MovePreviewPosition.Value;
            state.ClearMoveState();
            state.StatusMessage = "Ready";
            return;
        }

        if (!context.HoveredVoxel.HasValue)
        {
            state.SelectedObject = null;
            state.HoveredObject = null;
            return;
        }

        var picked = context.PickObjectAtPoint(context.PointerPoint);
        state.HoveredObject = picked;
        state.SelectedObject = picked;
    }

    public void OnPointerReleased(ViewportPointerContext context)
    {
    }

    public void OnCancel(EditorState editorState)
    {
        editorState.ClearMoveState();
        editorState.HoveredObject = null;
    }
}
