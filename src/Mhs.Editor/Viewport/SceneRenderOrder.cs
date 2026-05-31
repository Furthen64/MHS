using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Mhs.Editor.Editor;

namespace Mhs.Editor.Viewport;

public readonly record struct RenderableSceneObject(SceneObject SceneObject, ObjectVisibilityMode Visibility);

public static class SceneRenderOrder
{
    public static IReadOnlyList<RenderableSceneObject> GetVisibleBackToFront(EditorState state, Rect bounds)
    {
        return state.Scene.Objects
            .Select(sceneObject =>
            {
                var visibility = ObjectVisibility.GetVisibility(sceneObject, state.ActiveFloor, state.ActiveAbsoluteZ);
                var size = sceneObject.EffectiveSize;
                var center = ViewportMath.Project(
                    sceneObject.Position.X + size.WidthX / 2.0,
                    sceneObject.Position.Y + size.DepthY / 2.0,
                    sceneObject.Position.Z + size.HeightZ / 2.0,
                    bounds,
                    state);
                return new { sceneObject, visibility, center };
            })
            .Where(entry => entry.visibility != ObjectVisibilityMode.Hidden)
            .OrderBy(entry => entry.center.Y)
            .ThenBy(entry => entry.center.X)
            .Select(entry => new RenderableSceneObject(entry.sceneObject, entry.visibility))
            .ToArray();
    }
}