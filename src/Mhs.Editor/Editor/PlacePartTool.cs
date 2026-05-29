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

        if (!context.HoveredVoxel.HasValue)
        {
            context.EditorState.GhostPreview = null;
            return;
        }

        var hovered = context.HoveredVoxel.Value;
        var voxel = new VoxelCoord(hovered.X, hovered.Y, context.EditorState.ActiveAbsoluteZ);
        if (!context.EditorState.FitsWithinGrid(voxel, _partDefinition.Size))
        {
            context.EditorState.GhostPreview = null;
            return;
        }

        var isValid = context.EditorState.CanPlaceAt(_partDefinition, voxel);

        context.EditorState.GhostPreview = new GhostPreview
        {
            Part = _partDefinition,
            Position = voxel,
            IsValid = isValid
        };
    }

    public void OnPointerPressed(ViewportPointerContext context)
    {
        var hovered = context.EditorState.HoveredVoxel;
        if (!hovered.HasValue)
        {
            return;
        }

        var position = hovered.Value with { Z = context.EditorState.ActiveAbsoluteZ };
        if (!context.EditorState.FitsWithinGrid(position, _partDefinition.Size) || !context.EditorState.CanPlaceAt(_partDefinition, position))
        {
            return;
        }

        var sceneObject = new SceneObject
        {
            PartType = _partDefinition.DisplayName,
            Position = position,
            Size = _partDefinition.Size,
            RotationDegrees = 0
        };

        context.EditorState.Scene.Objects.Add(sceneObject);
        context.EditorState.SelectedObject = sceneObject;
    }

    public void OnPointerReleased(ViewportPointerContext context)
    {
    }

    public void OnCancel(EditorState editorState)
    {
        editorState.GhostPreview = null;
    }
}
