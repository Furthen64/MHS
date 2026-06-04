using System;
using System.Linq;

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
        if (!IsKnownPartDefinition(state))
        {
            state.StatusMessage = "Blocked | Placement blocked: unknown part definition";
            return;
        }

        var hovered = state.HoveredVoxel;
        if (!hovered.HasValue)
        {
            return;
        }

        var position = hovered.Value with { Z = state.ActiveAbsoluteZ };
        var rotation = state.ActivePlacementRotationZDegrees;
        var validation = state.ValidatePartPlacement(_partDefinition, position, rotation);
        if (!validation.IsValid)
        {
            state.StatusMessage = $"Blocked | Placement blocked: {validation.Reason ?? "invalid"}";
            return;
        }

        var sceneObject = new SceneObject
        {
            PartId = _partDefinition.Id,
            PartType = _partDefinition.DisplayName,
            Position = position,
            BaseSize = _partDefinition.Size,
            RotationZDegrees = rotation,
            MaterialUnitsPerSecond = string.Equals(_partDefinition.Id, "mtrlsrc", StringComparison.OrdinalIgnoreCase)
                ? SceneObject.DefaultMaterialUnitsPerSecond
                : 0f,
            MaterialId = string.Equals(_partDefinition.Id, "mtrlsrc", StringComparison.OrdinalIgnoreCase)
                ? SceneObject.DefaultMaterialId
                : string.Empty
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
        if (!IsKnownPartDefinition(state))
        {
            state.GhostPreview = null;
            return;
        }

        if (!state.HoveredVoxel.HasValue)
        {
            state.GhostPreview = null;
            return;
        }

        var hovered = state.HoveredVoxel.Value;
        var voxel = new VoxelCoord(hovered.X, hovered.Y, state.ActiveAbsoluteZ);
        var rotation = state.ActivePlacementRotationZDegrees;
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

    private bool IsKnownPartDefinition(EditorState state)
        => !string.IsNullOrWhiteSpace(_partDefinition.Id)
           && state.PartDefinitions.Any(part => string.Equals(part.Id, _partDefinition.Id, StringComparison.Ordinal));
}
