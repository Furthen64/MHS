using System;
using System.Collections.Generic;

namespace Mhs.Editor.Editor;

public sealed class ConveyorRouteTool : IEditorTool
{
    private bool _awaitingLeftButtonRelease;
    private const string FinishHint = "Enter/RMB: finish | Esc: cancel";

    public string Name => "Conveyor Route";

    public void OnPointerMoved(ViewportPointerContext context)
    {
        var state = context.EditorState;
        state.HoveredVoxel = context.HoveredVoxel;
        state.HoveredObject = null;

        var draft = state.ActiveConveyorRoute;
        if (draft is null || draft.Anchors.Count == 0)
        {
            UpdateStartGhost(state, context.HoveredVoxel);
            return;
        }

        state.GhostPreview = null;

        if (!context.HoveredVoxel.HasValue)
        {
            draft.PreviewEnd = null;
            draft.PreviewIsValid = false;
            draft.InvalidReason = "out of grid bounds";
            draft.PreviewRotationZDegrees = null;
            state.StatusMessage = "Route blocked: out of grid bounds";
            return;
        }

        var snapped = context.HoveredVoxel.Value with { Z = draft.Z };
        var start = draft.Anchors[^1];
        var end = ConveyorRouteGeometry.SnapToDominantAxis(start, snapped);
        ApplyPreviewValidation(state, draft, start, end);
        state.StatusMessage = draft.PreviewIsValid
            ? $"Route | Anchors: {draft.Anchors.Count} | Preview length: {GetPreviewLength(start, end)} | Valid | {FinishHint}"
            : $"Route blocked: {draft.InvalidReason ?? "invalid"}";
    }

    public void OnPointerPressed(ViewportPointerContext context)
    {
        if (context.IsRightButtonPressed)
        {
            if (FinishCommittedRoute(context.EditorState))
            {
                context.EditorState.GhostPreview = null;
            }

            return;
        }

        if (_awaitingLeftButtonRelease)
        {
            return;
        }

        _awaitingLeftButtonRelease = true;

        var state = context.EditorState;
        if (!context.HoveredVoxel.HasValue)
        {
            state.StatusMessage = "Route blocked: out of grid bounds";
            return;
        }

        var hovered = context.HoveredVoxel.Value with { Z = state.ActiveAbsoluteZ };
        var draft = state.ActiveConveyorRoute;

        if (draft is null)
        {
            if (!state.IsWithinGrid(hovered))
            {
                state.StatusMessage = "Route blocked: out of grid bounds";
                return;
            }

            draft = new ConveyorRouteDraft
            {
                Z = state.ActiveAbsoluteZ,
                PreviewIsValid = false
            };
            draft.Anchors.Add(hovered);
            state.ActiveConveyorRoute = draft;
            state.StatusMessage = $"Route | Anchors: 1 | Click next anchor | {FinishHint}";
            return;
        }

        var start = draft.Anchors[^1];
        var end = ConveyorRouteGeometry.SnapToDominantAxis(start, hovered with { Z = draft.Z });
        if (!ValidateSegment(state, draft, start, end, out var segment, out var reason))
        {
            draft.PreviewEnd = end;
            draft.PreviewIsValid = false;
            draft.InvalidReason = reason;
            draft.PreviewRotationZDegrees = null;
            state.StatusMessage = $"Route blocked: {reason}";
            return;
        }

        draft.Anchors.Add(segment.End);
        draft.PreviewEnd = null;
        draft.PreviewIsValid = true;
        draft.InvalidReason = null;
        draft.PreviewRotationZDegrees = null;
        state.StatusMessage = $"Route | Anchors: {draft.Anchors.Count} | {FinishHint}";
    }

    public void OnPointerReleased(ViewportPointerContext context)
    {
        if (!context.IsLeftButtonPressed)
        {
            TryFinishDragRoute(context.EditorState);
            _awaitingLeftButtonRelease = false;
        }
    }

    public void OnCancel(EditorState editorState)
    {
        _awaitingLeftButtonRelease = false;
        editorState.ActiveConveyorRoute = null;
        editorState.GhostPreview = null;
    }

    public bool HasFinishableRoute(EditorState state)
    {
        var draft = state.ActiveConveyorRoute;
        return draft is not null && draft.Anchors.Count >= 1;
    }

    public bool FinishRoute(EditorState state)
    {
        var draft = state.ActiveConveyorRoute;
        if (draft is null || !TryGetFinishAnchors(draft, out var finishAnchors))
        {
            state.StatusMessage = "Route needs at least one point";
            return false;
        }

        return FinishRoute(state, draft, finishAnchors);
    }

    public bool FinishCommittedRoute(EditorState state)
    {
        var draft = state.ActiveConveyorRoute;
        if (draft is null || draft.Anchors.Count < 1)
        {
            state.StatusMessage = "Route needs at least one point";
            return false;
        }

        return FinishRoute(state, draft, draft.Anchors);
    }

    private static bool FinishRoute(EditorState state, ConveyorRouteDraft draft, IReadOnlyList<VoxelCoord> finishAnchors)
    {
        // Normalize: remove consecutive duplicate anchors to prevent zero-length segments
        var anchors = new List<VoxelCoord>(finishAnchors.Count);
        foreach (var anchor in finishAnchors)
        {
            if (anchors.Count == 0 || anchors[^1] != anchor)
            {
                anchors.Add(anchor);
            }
        }

        var created = new List<SceneObject>();
        if (anchors.Count == 1)
        {
            var anchor = anchors[0];
            created.Add(CreateRouteConveyor(
                anchor,
                new VoxelSize(1, 1, 1),
                0,
                anchor,
                anchor));
        }

        for (var i = 1; i < anchors.Count; i++)
        {
            var start = anchors[i - 1];
            var end = anchors[i];
            if (!ValidateSegment(state, draft, start, end, out var segment, out var reason, validateDraftCollisions: false))
            {
                state.StatusMessage = $"Route blocked: {reason}";
                return false;
            }

            var position = segment.Position;
            var size = segment.Size;
            var flowStart = start;
            var flowEnd = end;
            if (i > 1)
            {
                ConveyorRouteGeometry.TrimSegmentStartCell(start, end, ref position, ref size);
                flowStart = StepToward(start, end);
            }

            created.Add(CreateRouteConveyor(position, size, segment.RotationZDegrees, flowStart, flowEnd));
        }

        foreach (var sceneObject in created)
        {
            state.Scene.Objects.Add(sceneObject);
        }

        state.ActiveConveyorRoute = null;
        state.SelectedObject = null;
        state.HoveredObject = null;
        state.ActiveTool = new SelectTool();
        state.StatusMessage = created.Count > 0 ? $"Route finished: {created.Count} segment(s)" : "Route needs at least one point";
        return created.Count > 0;
    }

    public bool RemoveLastAnchor(EditorState state)
    {
        var draft = state.ActiveConveyorRoute;
        if (draft is null)
        {
            return false;
        }

        if (draft.Anchors.Count > 0)
        {
            draft.Anchors.RemoveAt(draft.Anchors.Count - 1);
        }

        if (draft.Anchors.Count == 0)
        {
            state.ActiveConveyorRoute = null;
            state.StatusMessage = "Route canceled";
        }
        else
        {
            draft.PreviewEnd = null;
            draft.PreviewIsValid = false;
            draft.InvalidReason = null;
            draft.PreviewRotationZDegrees = null;
            state.StatusMessage = $"Route | Anchors: {draft.Anchors.Count} | {FinishHint}";
        }

        return true;
    }

    private static void UpdateStartGhost(EditorState state, VoxelCoord? hoveredVoxel)
    {
        if (!hoveredVoxel.HasValue || TryGetConveyorPart(state) is not { } conveyorPart)
        {
            state.GhostPreview = null;
            return;
        }

        var position = hoveredVoxel.Value with { Z = state.ActiveAbsoluteZ };
        var validation = state.ValidatePartPlacement(conveyorPart, position, 0);
        state.GhostPreview = new GhostPreview
        {
            Part = conveyorPart,
            Position = position,
            RotationZDegrees = 0,
            IsValid = validation.IsValid,
            InvalidReason = validation.Reason
        };
    }

    private static PartDefinition? TryGetConveyorPart(EditorState state)
    {
        foreach (var part in state.PartDefinitions)
        {
            if (string.Equals(part.Id, "conveyor", StringComparison.Ordinal))
            {
                return part;
            }
        }

        return null;
    }

    private static void ApplyPreviewValidation(EditorState state, ConveyorRouteDraft draft, VoxelCoord start, VoxelCoord end)
    {
        draft.PreviewEnd = end;
        if (ValidateSegment(state, draft, start, end, out var segment, out var reason))
        {
            draft.PreviewIsValid = true;
            draft.InvalidReason = null;
            draft.PreviewRotationZDegrees = segment.RotationZDegrees;
            return;
        }

        draft.PreviewIsValid = false;
        draft.InvalidReason = reason;
        draft.PreviewRotationZDegrees = null;
    }

    private void TryFinishDragRoute(EditorState state)
    {
        var draft = state.ActiveConveyorRoute;
        if (!_awaitingLeftButtonRelease
            || draft is null
            || draft.Anchors.Count != 1
            || !draft.PreviewIsValid
            || draft.PreviewEnd is not { } previewEnd
            || draft.Anchors[0] == previewEnd)
        {
            return;
        }

        FinishRoute(state);
    }

    private static bool TryGetFinishAnchors(ConveyorRouteDraft draft, out List<VoxelCoord> finishAnchors)
    {
        var raw = new List<VoxelCoord>(draft.Anchors);
        if (draft.PreviewIsValid
            && draft.PreviewEnd is { } previewEnd
            && (raw.Count == 0 || raw[^1] != previewEnd))
        {
            raw.Add(previewEnd);
        }

        // Normalize: remove consecutive duplicate anchors to prevent zero-length segments
        finishAnchors = new List<VoxelCoord>(raw.Count);
        foreach (var anchor in raw)
        {
            if (finishAnchors.Count == 0 || finishAnchors[^1] != anchor)
            {
                finishAnchors.Add(anchor);
            }
        }

        return finishAnchors.Count >= 1;
    }

    private static SceneObject CreateRouteConveyor(VoxelCoord position, VoxelSize size, int rotationZDegrees, VoxelCoord flowStart, VoxelCoord flowEnd)
        => new()
        {
            PartId = "conveyor",
            PartType = "Conveyor",
            Position = position,
            BaseSize = size,
            RotationZDegrees = rotationZDegrees,
            RouteStartCell = flowStart,
            RouteEndCell = flowEnd
        };

    private static VoxelCoord StepToward(VoxelCoord start, VoxelCoord end)
    {
        if (start.X != end.X)
        {
            return start with { X = start.X + Math.Sign(end.X - start.X) };
        }

        if (start.Y != end.Y)
        {
            return start with { Y = start.Y + Math.Sign(end.Y - start.Y) };
        }

        return start;
    }

    private static bool ValidateSegment(
        EditorState state,
        ConveyorRouteDraft draft,
        VoxelCoord start,
        VoxelCoord end,
        out ConveyorRouteSegment segment,
        out string reason,
        bool validateDraftCollisions = true)
    {
        if (!ConveyorRouteGeometry.TryCreateSegment(start, end, out segment, out var invalid))
        {
            reason = invalid ?? "invalid segment";
            return false;
        }

        var draftOccupied = CollectCommittedDraftCells(draft);
        foreach (var cell in ConveyorRouteGeometry.EnumerateCells(start, end))
        {
            if (!state.IsWithinGrid(cell))
            {
                reason = "out of grid bounds";
                return false;
            }

            if (IsOccupiedByScene(state, cell))
            {
                reason = "collision";
                return false;
            }

            if (validateDraftCollisions && draftOccupied.Contains(cell) && cell != start)
            {
                reason = "collision";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    private static HashSet<VoxelCoord> CollectCommittedDraftCells(ConveyorRouteDraft draft)
    {
        var occupied = new HashSet<VoxelCoord>();
        for (var i = 1; i < draft.Anchors.Count; i++)
        {
            foreach (var cell in ConveyorRouteGeometry.EnumerateCells(draft.Anchors[i - 1], draft.Anchors[i]))
            {
                occupied.Add(cell);
            }
        }

        return occupied;
    }

    private static bool IsOccupiedByScene(EditorState state, VoxelCoord cell)
    {
        foreach (var existing in state.Scene.Objects)
        {
            if (cell.X >= existing.MinX && cell.X <= existing.MaxX &&
                cell.Y >= existing.MinY && cell.Y <= existing.MaxY &&
                cell.Z >= existing.MinZ && cell.Z <= existing.MaxZ)
            {
                return true;
                    }
                }

                return false;
            }

            private static int GetPreviewLength(VoxelCoord start, VoxelCoord end)
                => Math.Abs(end.X - start.X) + Math.Abs(end.Y - start.Y) + 1;
}
