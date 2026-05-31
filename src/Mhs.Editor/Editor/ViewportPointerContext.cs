using System;
using Avalonia;

namespace Mhs.Editor.Editor;

public sealed class ViewportPointerContext
{
    public required EditorState EditorState { get; init; }
    public required Point PointerPoint { get; init; }
    public required VoxelCoord? HoveredVoxel { get; init; }
    public required VoxelCoord? RotationPlaneVoxel { get; init; }
    public required bool IsLeftButtonPressed { get; init; }
    public required bool IsRightButtonPressed { get; init; }
    public required Func<Point, SceneObject?> PickObjectAtPoint { get; init; }
}
