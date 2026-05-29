namespace Mhs.Editor.Editor;

public sealed class SelectTool : IEditorTool
{
    public string Name => "Select";

    public void OnPointerMoved(ViewportPointerContext context)
    {
        context.EditorState.HoveredVoxel = context.HoveredVoxel;
        context.EditorState.GhostPreview = null;
    }

    public void OnPointerPressed(ViewportPointerContext context)
    {
        var picked = context.PickObjectAtPoint(context.PointerPoint);
        context.EditorState.SelectedObject = picked;
    }

    public void OnPointerReleased(ViewportPointerContext context)
    {
    }

    public void OnCancel(EditorState editorState)
    {
    }
}
