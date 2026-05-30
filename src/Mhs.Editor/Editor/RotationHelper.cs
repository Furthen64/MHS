using System;

namespace Mhs.Editor.Editor;

public static class RotationHelper
{
    public static int NormalizeDegrees(int value) => ((value % 360) + 360) % 360;

    public static int RotateClockwise90(int current) => (NormalizeDegrees(current) + 90) % 360;

    public static VoxelSize GetEffectiveSize(VoxelSize baseSize, int rotationZDegrees)
    {
        var normalized = NormalizeDegrees(rotationZDegrees);
        return normalized is 90 or 270
            ? new VoxelSize(baseSize.DepthY, baseSize.WidthX, baseSize.HeightZ)
            : baseSize;
    }

    public static VoxelCoord RotatePositionAroundPivot(
        VoxelCoord currentPosition,
        VoxelSize rotatedSize,
        double pivotX,
        double pivotY)
    {
        var nextX = (int)Math.Round(pivotX - rotatedSize.WidthX / 2.0, MidpointRounding.AwayFromZero);
        var nextY = (int)Math.Round(pivotY - rotatedSize.DepthY / 2.0, MidpointRounding.AwayFromZero);
        return currentPosition with { X = nextX, Y = nextY };
    }
}
