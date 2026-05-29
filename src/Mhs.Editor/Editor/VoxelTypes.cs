namespace Mhs.Editor.Editor;

public readonly record struct VoxelCoord(int X, int Y, int Z)
{
    public override string ToString() => $"{X}, {Y}, {Z}";
}

public readonly record struct VoxelSize(int Width, int Height, int Depth)
{
    public override string ToString() => $"{Width} x {Height} x {Depth}";
}
