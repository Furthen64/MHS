namespace Mhs.Editor.Editor;

public interface IEditorTool
{
    string Name { get; }

    void OnPointerMoved(ViewportPointerContext context);
    void OnPointerPressed(ViewportPointerContext context);
    void OnPointerReleased(ViewportPointerContext context);
    void OnCancel(EditorState editorState);
}
