using System;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Mhs.Editor.Editor;

namespace Mhs.Editor.Viewport;

public sealed class SoftwareCubeViewport : Control
{
    public static readonly StyledProperty<EditorState?> EditorStateProperty =
        AvaloniaProperty.Register<SoftwareCubeViewport, EditorState?>(nameof(EditorState));
    public static readonly StyledProperty<ViewportInteractionPreset> InteractionPresetProperty =
        AvaloniaProperty.Register<SoftwareCubeViewport, ViewportInteractionPreset>(
            nameof(InteractionPreset),
            defaultValue: ViewportInteractionPreset.BlenderLike);

    private const double TileWidth = 48;
    private const double TileHeight = 24;
    private const double HeightScale = 36;
    private const double MinZoom = 0.45;
    private const double MaxZoom = 2.75;

    static SoftwareCubeViewport()
    {
        AffectsRender<SoftwareCubeViewport>(EditorStateProperty, InteractionPresetProperty);
    }

    private bool _isPanning;
    private Point _lastPanPoint;

    public SoftwareCubeViewport()
    {
        ClipToBounds = true;

        PointerMoved += OnPointerMoved;
        PointerPressed += OnPointerPressed;
        PointerReleased += OnPointerReleased;
        PointerExited += OnPointerExited;
        PointerWheelChanged += OnPointerWheelChanged;
    }

    public EditorState? EditorState
    {
        get => GetValue(EditorStateProperty);
        set => SetValue(EditorStateProperty, value);
    }

    public ViewportInteractionPreset InteractionPreset
    {
        get => GetValue(InteractionPresetProperty);
        set => SetValue(InteractionPresetProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == EditorStateProperty)
        {
            if (change.OldValue is EditorState oldState)
            {
                DetachState(oldState);
            }

            if (change.NewValue is EditorState newState)
            {
                AttachState(newState);
            }
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        context.FillRectangle(new SolidColorBrush(Color.FromRgb(25, 30, 35)), Bounds);

        if (Bounds.Width < 2 || Bounds.Height < 2)
        {
            return;
        }

        var state = EditorState;
        if (state is null)
        {
            return;
        }

        DrawFloorOutlines(context, state.ActiveFloor);
        DrawGrid(context, state.ActiveAbsoluteZ);

        foreach (var sceneObject in state.Scene.Objects)
        {
            if (!state.IntersectsActiveFloor(sceneObject))
            {
                continue;
            }

            var isActiveLayer = state.IntersectsActiveLayer(sceneObject);
            var opacity = isActiveLayer ? 0.9 : 0.3;
            if (state.IsMovingSelection && state.SelectedObject?.Id == sceneObject.Id)
            {
                opacity = 0.2;
            }

            var color = ColorForPart(sceneObject.PartType);
            DrawIsoBox(context, sceneObject.Position, sceneObject.EffectiveSize, color, opacity, drawOutline: false, state);
            DrawFacingMarker(context, sceneObject.Position, sceneObject.EffectiveSize, sceneObject.RotationZDegrees, sceneObject.PartType, opacity, state);
        }

        if (state.IsMovingSelection && state.SelectedObject is { } moving && state.MovePreviewPosition is { } target)
        {
            var moveColor = state.MovePreviewIsValid ? ColorForPart(moving.PartType) : Color.FromRgb(230, 90, 90);
            DrawIsoBox(context, target, moving.EffectiveSize, moveColor, 0.45, drawOutline: true, state);
            DrawFacingMarker(context, target, moving.EffectiveSize, moving.RotationZDegrees, moving.PartType, 0.45, state);
        }

        if (state.GhostPreview is { } ghost)
        {
            if (state.FitsWithinActiveFloor(ghost.Position, ghost.EffectiveSize))
            {
                var ghostColor = ghost.IsValid
                    ? ghost.Part.Color
                    : Color.FromRgb(230, 90, 90);
                DrawIsoBox(context, ghost.Position, ghost.EffectiveSize, ghostColor, 0.4, drawOutline: true, state);
                DrawFacingMarker(context, ghost.Position, ghost.EffectiveSize, ghost.RotationZDegrees, ghost.Part.DisplayName, 0.4, state);
            }
        }

        if (state.HoveredObject is { } hovered
            && state.IntersectsActiveLayer(hovered)
            && state.SelectedObject?.Id != hovered.Id)
        {
            DrawOutline(context, hovered.Position, hovered.EffectiveSize, Color.FromRgb(215, 215, 130), 1.5, state);
        }

        if (state.SelectedObject is { } selected && state.IntersectsActiveLayer(selected))
        {
            DrawOutline(context, selected.Position, selected.EffectiveSize, Color.FromRgb(120, 180, 255), 2, state);
        }
    }

    private void AttachState(EditorState state)
    {
        state.PropertyChanged += OnStatePropertyChanged;
        state.Scene.Objects.CollectionChanged += OnObjectsChanged;
        InvalidateVisual();
    }

    private void DetachState(EditorState state)
    {
        state.PropertyChanged -= OnStatePropertyChanged;
        state.Scene.Objects.CollectionChanged -= OnObjectsChanged;
    }

    private void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        InvalidateVisual();
    }

    private void OnObjectsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateVisual();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isPanning)
        {
            var point = e.GetPosition(this);
            if (EditorState is { } state)
            {
                state.ViewportPanX += point.X - _lastPanPoint.X;
                state.ViewportPanY += point.Y - _lastPanPoint.Y;
            }

            _lastPanPoint = point;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        RoutePointerEvent(e, isPressed: false, isReleased: false);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var current = e.GetCurrentPoint(this);
        if (CanStartPan(current.Properties))
        {
            _isPanning = true;
            _lastPanPoint = e.GetPosition(this);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        RoutePointerEvent(e, isPressed: true, isReleased: false);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isPanning && IsPanReleased(e.GetCurrentPoint(this).Properties))
        {
            _isPanning = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        RoutePointerEvent(e, isPressed: false, isReleased: true);
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        var state = EditorState;
        if (state is null)
        {
            return;
        }

        var context = new ViewportPointerContext
        {
            EditorState = state,
            PointerPoint = default,
            HoveredVoxel = null,
            PickObjectAtPoint = PickObjectAtPoint
        };

        state.ActiveTool.OnPointerMoved(context);
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var state = EditorState;
        if (state is null)
        {
            return;
        }

        var zoomStep = 1.1;
        var factor = e.Delta.Y > 0 ? zoomStep : 1 / zoomStep;
        var nextZoom = Math.Clamp(state.ViewportZoom * factor, MinZoom, MaxZoom);

        if (Math.Abs(nextZoom - state.ViewportZoom) < 0.0001)
        {
            return;
        }

        var pointer = e.GetPosition(this);
        var originBefore = GetViewOrigin();
        var worldOffsetX = (pointer.X - originBefore.X - state.ViewportPanX) / state.ViewportZoom;
        var worldOffsetY = (pointer.Y - originBefore.Y - state.ViewportPanY) / state.ViewportZoom;

        state.ViewportZoom = nextZoom;
        state.ViewportPanX = pointer.X - originBefore.X - worldOffsetX * state.ViewportZoom;
        state.ViewportPanY = pointer.Y - originBefore.Y - worldOffsetY * state.ViewportZoom;

        InvalidateVisual();
        e.Handled = true;
    }

    private void RoutePointerEvent(PointerEventArgs e, bool isPressed, bool isReleased)
    {
        var state = EditorState;
        if (state is null)
        {
            return;
        }

        var point = e.GetPosition(this);
        var hovered = TryMapPointToVoxel(point, state.ActiveAbsoluteZ);

        var context = new ViewportPointerContext
        {
            EditorState = state,
            PointerPoint = point,
            HoveredVoxel = hovered,
            PickObjectAtPoint = PickObjectAtPoint
        };

        state.ActiveTool.OnPointerMoved(context);

        if (isPressed)
        {
            state.ActiveTool.OnPointerPressed(context);
        }

        if (isReleased)
        {
            state.ActiveTool.OnPointerReleased(context);
        }

        e.Handled = true;
    }

    private SceneObject? PickObjectAtPoint(Point point)
    {
        var state = EditorState;
        if (state is null)
        {
            return null;
        }

        for (var i = state.Scene.Objects.Count - 1; i >= 0; i--)
        {
            var sceneObject = state.Scene.Objects[i];
            if (!state.IntersectsActiveLayer(sceneObject) || !state.IsObjectWithinGrid(sceneObject))
            {
                continue;
            }

            var bounds = GetObjectScreenBounds(sceneObject, state).Inflate(2);
            if (bounds.Contains(point))
            {
                return sceneObject;
            }
        }

        return null;
    }

    private Rect GetObjectScreenBounds(SceneObject sceneObject, EditorState state)
    {
        var x0 = sceneObject.Position.X;
        var x1 = sceneObject.Position.X + sceneObject.EffectiveSize.WidthX;
        var y0 = sceneObject.Position.Y;
        var y1 = sceneObject.Position.Y + sceneObject.EffectiveSize.DepthY;
        var z0 = sceneObject.Position.Z;
        var z1 = sceneObject.Position.Z + sceneObject.EffectiveSize.HeightZ;

        var corners = new[]
        {
            Project(x0, y0, z0, state), Project(x1, y0, z0, state), Project(x1, y1, z0, state), Project(x0, y1, z0, state),
            Project(x0, y0, z1, state), Project(x1, y0, z1, state), Project(x1, y1, z1, state), Project(x0, y1, z1, state)
        };

        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;

        foreach (var corner in corners)
        {
            minX = Math.Min(minX, corner.X);
            minY = Math.Min(minY, corner.Y);
            maxX = Math.Max(maxX, corner.X);
            maxY = Math.Max(maxY, corner.Y);
        }

        return new Rect(new Point(minX, minY), new Point(maxX, maxY));
    }

    private VoxelCoord? TryMapPointToVoxel(Point point, int absoluteZ)
    {
        if (!Bounds.Contains(point))
        {
            return null;
        }

        if (EditorState is not { } state)
        {
            return null;
        }

        var origin = GetTransformedOrigin(state);
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

    private void DrawFloorOutlines(DrawingContext context, int activeFloor)
    {
        if (EditorState is not { } state)
        {
            return;
        }

        for (var floor = 0; floor < WorldVerticalSettings.FloorCount; floor++)
        {
            var z = floor * WorldVerticalSettings.LayersPerFloor;
            var isActive = floor == activeFloor;
            var pen = new Pen(
                new SolidColorBrush(isActive ? Color.FromArgb(200, 150, 190, 255) : Color.FromArgb(70, 130, 140, 160)),
                isActive ? 2 : 1);

            var a = Project(WorldGridSettings.MinCoord, WorldGridSettings.MinCoord, z, state);
            var b = Project(WorldGridSettings.MaxCoord, WorldGridSettings.MinCoord, z, state);
            var c = Project(WorldGridSettings.MaxCoord, WorldGridSettings.MaxCoord, z, state);
            var d = Project(WorldGridSettings.MinCoord, WorldGridSettings.MaxCoord, z, state);

            context.DrawLine(pen, a, b);
            context.DrawLine(pen, b, c);
            context.DrawLine(pen, c, d);
            context.DrawLine(pen, d, a);
        }
    }

    private void DrawGrid(DrawingContext context, int absoluteZ)
    {
        if (EditorState is not { } state)
        {
            return;
        }

        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(125, 160, 190, 220)), 1.2);

        for (var x = WorldGridSettings.MinCoord; x <= WorldGridSettings.MaxCoord; x++)
        {
            var start = Project(x, WorldGridSettings.MinCoord, absoluteZ, state);
            var end = Project(x, WorldGridSettings.MaxCoord, absoluteZ, state);
            context.DrawLine(gridPen, start, end);
        }

        for (var y = WorldGridSettings.MinCoord; y <= WorldGridSettings.MaxCoord; y++)
        {
            var start = Project(WorldGridSettings.MinCoord, y, absoluteZ, state);
            var end = Project(WorldGridSettings.MaxCoord, y, absoluteZ, state);
            context.DrawLine(gridPen, start, end);
        }
    }

    private void DrawOutline(DrawingContext context, VoxelCoord position, VoxelSize size, Color color, double thickness, EditorState state)
    {
        var pen = new Pen(new SolidColorBrush(color), thickness);

        var x0 = position.X;
        var x1 = position.X + size.WidthX;
        var y0 = position.Y;
        var y1 = position.Y + size.DepthY;
        var z0 = position.Z;
        var z1 = position.Z + size.HeightZ;

        var topA = Project(x0, y0, z1, state);
        var topB = Project(x1, y0, z1, state);
        var topC = Project(x1, y1, z1, state);
        var topD = Project(x0, y1, z1, state);

        context.DrawLine(pen, topA, topB);
        context.DrawLine(pen, topB, topC);
        context.DrawLine(pen, topC, topD);
        context.DrawLine(pen, topD, topA);

        context.DrawLine(pen, topA, Project(x0, y0, z0, state));
        context.DrawLine(pen, topB, Project(x1, y0, z0, state));
        context.DrawLine(pen, topC, Project(x1, y1, z0, state));
        context.DrawLine(pen, topD, Project(x0, y1, z0, state));
    }

    private void DrawIsoBox(DrawingContext context, VoxelCoord position, VoxelSize size, Color color, double opacity, bool drawOutline, EditorState state)
    {
        var x0 = position.X;
        var x1 = position.X + size.WidthX;
        var y0 = position.Y;
        var y1 = position.Y + size.DepthY;
        var z0 = position.Z;
        var z1 = position.Z + size.HeightZ;

        var topA = Project(x0, y0, z1, state);
        var topB = Project(x1, y0, z1, state);
        var topC = Project(x1, y1, z1, state);
        var topD = Project(x0, y1, z1, state);

        var bottomA = Project(x0, y0, z0, state);
        var bottomB = Project(x1, y0, z0, state);
        var bottomC = Project(x1, y1, z0, state);
        var bottomD = Project(x0, y1, z0, state);

        var topBrush = new SolidColorBrush(WithOpacity(color, opacity));
        var rightBrush = new SolidColorBrush(WithOpacity(Darken(color, 0.78), opacity));
        var leftBrush = new SolidColorBrush(WithOpacity(Darken(color, 0.62), opacity));

        context.DrawGeometry(topBrush, null, Polygon(topA, topB, topC, topD));
        context.DrawGeometry(rightBrush, null, Polygon(topB, bottomB, bottomC, topC));
        context.DrawGeometry(leftBrush, null, Polygon(topD, topC, bottomC, bottomD));

        if (drawOutline)
        {
            var outline = new Pen(new SolidColorBrush(WithOpacity(Color.FromRgb(230, 230, 230), Math.Min(opacity + 0.3, 1))), 1);
            context.DrawGeometry(null, outline, Polygon(topA, topB, topC, topD));
            context.DrawGeometry(null, outline, Polygon(topB, bottomB, bottomC, topC));
            context.DrawGeometry(null, outline, Polygon(topD, topC, bottomC, bottomD));
        }
    }

    private Point Project(double x, double y, double z, EditorState state)
    {
        var origin = GetTransformedOrigin(state);
        var tileWidth = TileWidth * state.ViewportZoom;
        var tileHeight = TileHeight * state.ViewportZoom;
        var heightScale = HeightScale * state.ViewportZoom;

        return new Point(
            origin.X + (x - y) * (tileWidth / 2.0),
            origin.Y + (x + y) * (tileHeight / 2.0) - z * heightScale);
    }

    private Point GetViewOrigin() =>
        new(Bounds.Center.X, Bounds.Top + Bounds.Height * 0.28);

    private Point GetTransformedOrigin(EditorState state)
    {
        var origin = GetViewOrigin();
        return new Point(origin.X + state.ViewportPanX, origin.Y + state.ViewportPanY);
    }

    private static bool CanStartPan(PointerPointProperties pointerProperties)
        => pointerProperties.IsMiddleButtonPressed || pointerProperties.IsRightButtonPressed;

    private static bool IsPanReleased(PointerPointProperties pointerProperties)
        => !pointerProperties.IsMiddleButtonPressed && !pointerProperties.IsRightButtonPressed;

    private static Color Darken(Color color, double factor)
    {
        return Color.FromArgb(
            color.A,
            (byte)Math.Clamp((int)(color.R * factor), 0, 255),
            (byte)Math.Clamp((int)(color.G * factor), 0, 255),
            (byte)Math.Clamp((int)(color.B * factor), 0, 255));
    }

    private static Color WithOpacity(Color color, double opacity)
    {
        var alpha = (byte)Math.Clamp((int)(opacity * 255), 0, 255);
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    private static Geometry Polygon(params Point[] points)
    {
        var geometry = new StreamGeometry();
        using var stream = geometry.Open();
        stream.BeginFigure(points[0], isFilled: true);
        for (var i = 1; i < points.Length; i++)
        {
            stream.LineTo(points[i]);
        }

        stream.EndFigure(isClosed: true);
        return geometry;
    }

    private void DrawFacingMarker(DrawingContext context, VoxelCoord position, VoxelSize effectiveSize,
        int rotationZDegrees, string partType, double opacity, EditorState state)
    {
        if (partType is not ("Conveyor" or "Hopper" or "Tall Hopper" or "Chute"))
        {
            return;
        }

        var normalized = RotationHelper.NormalizeDegrees(rotationZDegrees);
        var (fdx, fdy) = normalized switch
        {
            0   => (1.0,  0.0),
            90  => (0.0,  1.0),
            180 => (-1.0, 0.0),
            _   => (0.0, -1.0)  // 270
        };

        var z1 = position.Z + effectiveSize.HeightZ;
        var cx = position.X + effectiveSize.WidthX / 2.0;
        var cy = position.Y + effectiveSize.DepthY / 2.0;

        // Arrow length proportional to the extent in the facing direction; base to the transverse extent
        var halfFacing    = (Math.Abs(fdx) > 0.5 ? effectiveSize.WidthX : effectiveSize.DepthY) / 2.0;
        var halfTransverse = (Math.Abs(fdx) > 0.5 ? effectiveSize.DepthY : effectiveSize.WidthX) / 2.0;

        var arrowLen  = halfFacing    * 0.55;
        var arrowBase = halfTransverse * 0.40;

        var tipX  = cx + fdx * arrowLen;
        var tipY  = cy + fdy * arrowLen;

        // Perpendicular direction for arrowhead base
        var px = -fdy;
        var py =  fdx;
        var tailX = cx - fdx * arrowLen * 0.3;
        var tailY = cy - fdy * arrowLen * 0.3;

        var base1 = Project(tailX + px * arrowBase, tailY + py * arrowBase, z1, state);
        var base2 = Project(tailX - px * arrowBase, tailY - py * arrowBase, z1, state);
        var tip   = Project(tipX, tipY, z1, state);

        var markerColor = partType switch
        {
            "Conveyor"              => Color.FromRgb(255, 230, 60),
            "Hopper" or "Tall Hopper" => Color.FromRgb(255, 110, 40),
            "Chute"                 => Color.FromRgb(180, 220, 255),
            _                       => Color.FromRgb(240, 240, 240)
        };

        var brush = new SolidColorBrush(WithOpacity(markerColor, Math.Min(opacity + 0.25, 1.0)));
        context.DrawGeometry(brush, null, Polygon(tip, base1, base2));
    }

    private static Color ColorForPart(string partType) => partType switch
    {
        "Hopper" => Color.FromRgb(240, 200, 90),
        "Tall Hopper" => Color.FromRgb(214, 132, 66),
        "Bin" => Color.FromRgb(90, 150, 240),
        "Conveyor" => Color.FromRgb(70, 80, 90),
        "Chute" => Color.FromRgb(150, 150, 150),
        _ => Color.FromRgb(180, 180, 180)
    };
}
