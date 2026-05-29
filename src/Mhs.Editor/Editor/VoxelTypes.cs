namespace Mhs.Editor.Editor;

public readonly record struct VoxelCoord(int X, int Y, int Z)
{
    public override string ToString() => $"{X}, {Y}, {Z}";
}

public readonly record struct VoxelSize(int WidthX, int DepthY, int HeightZ)
{
    public override string ToString() => $"{WidthX} x {DepthY} x {HeightZ}";
}
