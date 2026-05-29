namespace Mhs.Editor.Editor;

public static class WorldVerticalSettings
{
    public const int FloorCount = 3;
    public const int LayersPerFloor = 3;
    public const int MinZ = 0;
    public const int MaxZ = FloorCount * LayersPerFloor - 1;

    public static int ToAbsoluteZ(int floor, int layer) => floor * LayersPerFloor + layer;

    public static int ToFloor(int absoluteZ) => absoluteZ / LayersPerFloor;

    public static int ToLayer(int absoluteZ) => absoluteZ % LayersPerFloor;
}
