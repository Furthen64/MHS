using System;

namespace Mhs.Editor.Editor;

public sealed class SelectTool : IEditorTool
{
    public string Name => "Select";

    public void OnPointerMoved(ViewportPointerContext context)
    {
        var state = context.EditorState;
        state.HoveredVoxel = context.HoveredVoxel;
        state.GhostPreview = null;

        if (state.IsSelectionRotationMode && state.SelectedObject is { } rotating)
        {
            state.HoveredObject = rotating;
            UpdateRotationPreview(state, rotating, context);
            return;
        }

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
        if (state.IsSelectionRotationMode && state.SelectedObject is { } rotating)
        {
            if (!context.IsRightButtonPressed)
            {
                return;
            }

            if (!state.SelectionRotationPreviewPosition.HasValue || !state.SelectionRotationPreviewIsValid)
            {
                state.StatusMessage = $"Blocked | Rotation blocked: {state.SelectionRotationPreviewInvalidReason ?? "invalid"}";
                return;
            }

            rotating.Position = state.SelectionRotationPreviewPosition.Value;
            rotating.RotationZDegrees = state.SelectionRotationPreviewDegrees;
            if (rotating.IsRouteConveyorSegment)
            {
                rotating.RouteFlowReversed = !rotating.RouteFlowReversed;
            }

            state.ClearSelectionRotationMode();
            state.StatusMessage = "Ready";
            return;
        }

        if (state.IsMovingSelection && state.SelectedObject is { } selected)
        {
            if (!state.MovePreviewPosition.HasValue || !state.MovePreviewIsValid)
            {
                state.StatusMessage = $"Blocked | Move blocked: {state.MovePreviewInvalidReason ?? "invalid target"}";
                return;
            }

            ApplyMove(selected, state.MovePreviewPosition.Value);
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
        editorState.ClearSelectionRotationMode();
        editorState.ClearMoveState();
        editorState.HoveredObject = null;
    }

    private static void UpdateRotationPreview(EditorState state, SceneObject selected, ViewportPointerContext context)
    {
        if (selected.IsRouteConveyorSegment)
        {
            var flippedRotation = RotationHelper.NormalizeDegrees(selected.GetConveyorFlowRotationDegrees() + 180);
            state.SetSelectionRotationPreview(flippedRotation, selected.Position, true, null);
            return;
        }

        var cursor = context.RotationPlaneVoxel ?? context.HoveredVoxel;
        if (!cursor.HasValue || !state.HasRotationAxisFor(selected.Id))
        {
            return;
        }

        var target = cursor.Value;
        var dx = target.X + 0.5 - state.RotationAxisPivotX;
        var dy = target.Y + 0.5 - state.RotationAxisPivotY;
        if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001)
        {
            return;
        }

        var targetRotation = Math.Abs(dx) >= Math.Abs(dy)
            ? (dx >= 0 ? 0 : 180)
            : (dy >= 0 ? 90 : 270);

        var targetSize = selected.GetEffectiveSize(targetRotation);
        var targetPosition = RotationHelper.RotatePositionAroundPivot(
            selected.Position,
            targetSize,
            state.RotationAxisPivotX,
            state.RotationAxisPivotY);
        var validation = state.ValidatePlacement(targetPosition, targetSize, selected.Id);
        state.SetSelectionRotationPreview(targetRotation, targetPosition, validation.IsValid, validation.Reason);
    }

    private static void ApplyMove(SceneObject selected, VoxelCoord targetPosition)
    {
        var deltaX = targetPosition.X - selected.Position.X;
        var deltaY = targetPosition.Y - selected.Position.Y;
        var deltaZ = targetPosition.Z - selected.Position.Z;

        selected.Position = targetPosition;
        if (!selected.IsRouteConveyorSegment)
        {
            return;
        }

        if (selected.RouteStartCell is { } start)
        {
            selected.RouteStartCell = start with
            {
                X = start.X + deltaX,
                Y = start.Y + deltaY,
                Z = start.Z + deltaZ
            };
        }

        if (selected.RouteEndCell is { } end)
        {
            selected.RouteEndCell = end with
            {
                X = end.X + deltaX,
                Y = end.Y + deltaY,
                Z = end.Z + deltaZ
            };
        }
    }
}
