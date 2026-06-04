using System;
using Avalonia;
using Mhs.Editor.Editor;

namespace Mhs.Editor.Viewport;

public static class ViewportMath
{
    public const double TileWidth = 48;
    public const double TileHeight = 24;
    public const double HeightScale = 36;
    public const double MinZoom = 0.45;
    public const double MaxZoom = 2.75;

    public static Point GetViewOrigin(Rect bounds) =>
        new(bounds.Center.X, bounds.Top + bounds.Height * 0.28);

    public static Point GetTransformedOrigin(Rect bounds, EditorState state)
    {
        var origin = GetViewOrigin(bounds);
        return new Point(origin.X + state.ViewportPanX, origin.Y + state.ViewportPanY);
    }

    public static Point Project(double x, double y, double z, Rect bounds, EditorState state)
    {
        var origin = GetTransformedOrigin(bounds, state);
        var tileWidth = TileWidth * state.ViewportZoom;
        var tileHeight = TileHeight * state.ViewportZoom;
        var heightScale = HeightScale * state.ViewportZoom;

        return new Point(
            origin.X + (x - y) * (tileWidth / 2.0),
            origin.Y + (x + y) * (tileHeight / 2.0) - z * heightScale);
    }

    public static VoxelCoord? TryMapPointToVoxel(Point point, Rect bounds, EditorState state, int absoluteZ)
    {
        if (!bounds.Contains(point))
        {
            return null;
        }

        var origin = GetTransformedOrigin(bounds, state);
        var tileWidth = TileWidth * state.ViewportZoom;
        var tileHeight = TileHeight * state.ViewportZoom;
        var heightScale = HeightScale * state.ViewportZoom;

        var dx = (point.X - origin.X) / (tileWidth / 2.0);
        var dy = (point.Y - origin.Y + absoluteZ * heightScale) / (tileHeight / 2.0);

        var x = (dx + dy) / 2.0;
        var y = (dy - dx) / 2.0;
        var coord = new VoxelCoord((int)Math.Round(x), (int)Math.Round(y), absoluteZ);
        return coord.X < WorldGridSettings.MinCoord
            || coord.X > WorldGridSettings.MaxCoord
            || coord.Y < WorldGridSettings.MinCoord
            || coord.Y > WorldGridSettings.MaxCoord
            ? null
            : coord;
    }

    public static bool ApplyZoomAtPointer(EditorState state, Rect bounds, Point pointer, double wheelDeltaY)
    {
        var zoomStep = 1.1;
        var factor = wheelDeltaY > 0 ? zoomStep : 1 / zoomStep;
        var nextZoom = Math.Clamp(state.ViewportZoom * factor, MinZoom, MaxZoom);
        if (Math.Abs(nextZoom - state.ViewportZoom) < 0.0001)
        {
            return false;
        }

        var originBefore = GetViewOrigin(bounds);
        var worldOffsetX = (pointer.X - originBefore.X - state.ViewportPanX) / state.ViewportZoom;
        var worldOffsetY = (pointer.Y - originBefore.Y - state.ViewportPanY) / state.ViewportZoom;

        state.ViewportZoom = nextZoom;
        state.ViewportPanX = pointer.X - originBefore.X - worldOffsetX * state.ViewportZoom;
        state.ViewportPanY = pointer.Y - originBefore.Y - worldOffsetY * state.ViewportZoom;
        return true;
    }

    public static void CenterViewOn(EditorState state, Rect bounds, double worldX, double worldY, double worldZ)
    {
        var tileWidth = TileWidth * state.ViewportZoom;
        var tileHeight = TileHeight * state.ViewportZoom;
        var heightScale = HeightScale * state.ViewportZoom;

        state.ViewportPanX = -(worldX - worldY) * (tileWidth / 2.0);
        state.ViewportPanY = bounds.Height * 0.22 - (worldX + worldY) * (tileHeight / 2.0) + worldZ * heightScale;
    }
}
