namespace Mhs.Editor.Editor;

public enum ObjectVisibilityMode
{
    Hidden,
    DimmedSameFloor,
    SolidActiveLayer
}

public static class ObjectVisibility
{
    public static ObjectVisibilityMode GetVisibility(SceneObject obj, int activeFloor, int activeAbsoluteZ)
    {
        var floorStartZ = activeFloor * WorldVerticalSettings.LayersPerFloor;
        var floorEndZ = floorStartZ + WorldVerticalSettings.LayersPerFloor - 1;

        if (EditorState.IntersectsLayer(obj, activeAbsoluteZ))
        {
            return ObjectVisibilityMode.SolidActiveLayer;
        }

        if (EditorState.IntersectsFloor(obj, floorStartZ, floorEndZ))
        {
            return ObjectVisibilityMode.DimmedSameFloor;
        }

        return ObjectVisibilityMode.Hidden;
    }
}
