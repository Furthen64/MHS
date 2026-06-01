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

    static SoftwareCubeViewport()
    {
        AffectsRender<SoftwareCubeViewport>(EditorStateProperty, InteractionPresetProperty);
    }

    private bool _isPanning;
    private Point _lastPanPoint;

    public SoftwareCubeViewport()
    {
        ClipToBounds = true;
        Focusable = true;

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

        foreach (var renderable in SceneRenderOrder.GetVisibleBackToFront(state, Bounds))
        {
            var sceneObject = renderable.SceneObject;
            var visibility = renderable.Visibility;

            var drawPosition = sceneObject.Position;
            var drawRotation = sceneObject.RotationZDegrees;
            var drawSize = sceneObject.EffectiveSize;
            if (state.IsSelectionRotationMode
                && state.SelectedObject?.Id == sceneObject.Id
                && state.SelectionRotationPreviewPosition.HasValue)
            {
                drawPosition = state.SelectionRotationPreviewPosition.Value;
                drawRotation = state.SelectionRotationPreviewDegrees;
                drawSize = sceneObject.GetEffectiveSize(drawRotation);
            }

            var opacity = visibility == ObjectVisibilityMode.SolidActiveLayer
                ? (string.Equals(sceneObject.PartType, "Conveyor", StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.9)
                : 0.3;
            if (state.IsMovingSelection && state.SelectedObject?.Id == sceneObject.Id)
            {
                opacity = 0.2;
            }

            var renderInfo = PartRenderCatalog.Resolve(sceneObject.PartType);
            var color = renderInfo.BaseColor.ToAvaloniaColor();
            if (string.Equals(sceneObject.PartType, "Conveyor", StringComparison.OrdinalIgnoreCase))
            {
                DrawConveyorStrip(context, drawPosition, drawSize, drawRotation, color, renderInfo, opacity, false, state);
            }
            else
            {
                DrawIsoBox(context, drawPosition, drawSize, color, opacity, drawOutline: false, state);
                DrawFacingMarker(context, drawPosition, drawSize, drawRotation, renderInfo, opacity, state);
            }
        }

        DrawConveyorSceneJoins(context, state);

        if (state.IsMovingSelection && state.SelectedObject is { } moving && state.MovePreviewPosition is { } target)
        {
            var moveColor = state.MovePreviewIsValid
                ? PartRenderCatalog.Resolve(moving.PartType).BaseColor.ToAvaloniaColor()
                : Color.FromRgb(230, 90, 90);
            DrawIsoBox(context, target, moving.EffectiveSize, moveColor, 0.45, drawOutline: true, state);
            DrawFacingMarker(context, target, moving.EffectiveSize, moving.RotationZDegrees, PartRenderCatalog.Resolve(moving.PartType), 0.45, state);
        }

        if (state.GhostPreview is { } ghost)
        {
            if (state.FitsWithinActiveFloor(ghost.Position, ghost.EffectiveSize))
            {
                var ghostColor = ghost.IsValid
                    ? ghost.Part.Color
                    : Color.FromRgb(230, 90, 90);
                DrawIsoBox(context, ghost.Position, ghost.EffectiveSize, ghostColor, 0.4, drawOutline: true, state);
                DrawFacingMarker(context, ghost.Position, ghost.EffectiveSize, ghost.RotationZDegrees, PartRenderCatalog.Resolve(ghost.Part.Id), 0.4, state);
            }
        }

        if (state.ActiveConveyorRoute is { } route)
        {
            DrawConveyorRoutePreview(context, route, state);
        }

        if (state.HoveredObject is { } hovered
            && state.IntersectsActiveLayer(hovered)
            && state.SelectedObject?.Id != hovered.Id)
        {
            DrawOutline(context, hovered.Position, hovered.EffectiveSize, Color.FromRgb(215, 215, 130), 1.5, state);
        }

        if (state.SelectedObject is { } selected && state.IntersectsActiveLayer(selected))
        {
            var outlinePosition = state.IsSelectionRotationMode && state.SelectionRotationPreviewPosition.HasValue
                ? state.SelectionRotationPreviewPosition.Value
                : selected.Position;
            var outlineRotation = state.IsSelectionRotationMode
                ? state.SelectionRotationPreviewDegrees
                : selected.RotationZDegrees;
            var outlineSize = selected.GetEffectiveSize(outlineRotation);
            DrawOutline(context, outlinePosition, outlineSize, Color.FromRgb(120, 180, 255), 2, state);
        }

        DrawRotationAxisGuide(context, state);
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
        Focus(NavigationMethod.Pointer, KeyModifiers.None);

        var current = e.GetCurrentPoint(this);
        if (CanStartPan(current.Properties, EditorState))
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
            RotationPlaneVoxel = null,
            IsLeftButtonPressed = false,
            IsRightButtonPressed = false,
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

        var pointer = e.GetPosition(this);
        if (!ViewportMath.ApplyZoomAtPointer(state, Bounds, pointer, e.Delta.Y))
        {
            return;
        }

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
        var rotationPlaneVoxel = state.SelectedObject is { } selected && state.IsSelectionRotationMode
            ? TryMapPointToVoxel(point, selected.Position.Z)
            : null;
        var pointerProperties = e.GetCurrentPoint(this).Properties;

        var context = new ViewportPointerContext
        {
            EditorState = state,
            PointerPoint = point,
            HoveredVoxel = hovered,
            RotationPlaneVoxel = rotationPlaneVoxel,
            IsLeftButtonPressed = pointerProperties.IsLeftButtonPressed,
            IsRightButtonPressed = pointerProperties.IsRightButtonPressed,
            PickObjectAtPoint = PickObjectAtPoint
        };

        state.ActiveTool.OnPointerMoved(context);

        if (isPressed)
        {
            state.ActiveTool.OnPointerPressed(context);

            if (e is PointerPressedEventArgs pressed
                && pressed.ClickCount >= 2
                && state.ActiveTool is ConveyorRouteTool routeTool
                && routeTool.HasFinishableRoute(state))
            {
                routeTool.FinishRoute(state);
            }
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
        if (EditorState is not { } state)
        {
            return null;
        }

        return ViewportMath.TryMapPointToVoxel(point, Bounds, state, absoluteZ);
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

    private void DrawRotationAxisGuide(DrawingContext context, EditorState state)
    {
        if (state.SelectedObject is not { } selected || !state.HasRotationAxisFor(selected.Id))
        {
            return;
        }

        if (state.IsSelectionRotationMode)
        {
            var previewRotation = state.SelectionRotationPreviewDegrees;
            var previewSize = selected.GetEffectiveSize(previewRotation);
            var previewPosition = state.SelectionRotationPreviewPosition ?? selected.Position;
            var centerX = state.RotationAxisPivotX;
            var centerY = state.RotationAxisPivotY;
            var topZ = previewPosition.Z + previewSize.HeightZ;
            var radius = Math.Max(previewSize.WidthX, previewSize.DepthY) * 0.65 + 0.35;

            var guidePen = new Pen(new SolidColorBrush(Color.FromArgb(220, 170, 220, 255)), 1.6);
            Point? previous = null;
            const int segments = 48;
            for (var i = 0; i <= segments; i++)
            {
                var theta = i * (Math.PI * 2.0 / segments);
                var x = centerX + Math.Cos(theta) * radius;
                var y = centerY + Math.Sin(theta) * radius;
                var point = Project(x, y, topZ, state);
                if (previous is { } last)
                {
                    context.DrawLine(guidePen, last, point);
                }

                previous = point;
            }

            var (fdx, fdy) = RotationHelper.NormalizeDegrees(previewRotation) switch
            {
                0 => (1.0, 0.0),
                90 => (0.0, 1.0),
                180 => (-1.0, 0.0),
                _ => (0.0, -1.0)
            };
            var dot = Project(centerX + fdx * radius, centerY + fdy * radius, topZ, state);
            var dotBrush = new SolidColorBrush(state.SelectionRotationPreviewIsValid
                ? Color.FromArgb(245, 255, 240, 90)
                : Color.FromArgb(245, 255, 95, 95));
            context.DrawEllipse(dotBrush, null, dot, 4.4, 4.4);
            return;
        }

        var axisColor = Color.FromArgb(210, 140, 210, 255);
        var axisPen = new Pen(new SolidColorBrush(axisColor), 1.6, dashStyle: DashStyle.Dash);
        var start = Project(state.RotationAxisPivotX, state.RotationAxisPivotY, state.RotationAxisMinZ, state);
        var end = Project(state.RotationAxisPivotX, state.RotationAxisPivotY, state.RotationAxisMaxZ, state);
        context.DrawLine(axisPen, start, end);
    }

    private void DrawConveyorRoutePreview(DrawingContext context, ConveyorRouteDraft route, EditorState state)
    {
        var renderInfo = PartRenderCatalog.Resolve("conveyor");

        for (var i = 1; i < route.Anchors.Count; i++)
        {
            var start = route.Anchors[i - 1];
            var end = route.Anchors[i];
            if (!ConveyorRouteGeometry.TryCreateSegment(start, end, out var segment, out _))
            {
                continue;
            }

            var position = segment.Position;
            var size = segment.Size;
            if (i > 1)
            {
                ConveyorRouteGeometry.TrimSegmentStartCell(start, end, ref position, ref size);
            }

            DrawIsoBox(context, position, size, Color.FromRgb(78, 158, 216), 0.35, drawOutline: true, state);
            DrawFacingMarker(context, position, size, segment.RotationZDegrees, renderInfo, 0.35, state);

            if (i < route.Anchors.Count - 1)
            {
                var nextStart = route.Anchors[i];
                var nextEnd = route.Anchors[i + 1];
                if (ConveyorRouteRendering.TryGetTurnJoinCell(start, end, nextStart, nextEnd, out var joinCell))
                {
                    DrawConveyorJoinCap(context, joinCell, Color.FromRgb(78, 158, 216), 0.42, state);
                }
            }
        }

        if (route.Anchors.Count > 0 && route.PreviewEnd.HasValue)
        {
            var start = route.Anchors[^1];
            var end = route.PreviewEnd.Value;
            if (ConveyorRouteGeometry.TryCreateSegment(start, end, out var segment, out _))
            {
                var position = segment.Position;
                var size = segment.Size;
                if (route.Anchors.Count > 1)
                {
                    ConveyorRouteGeometry.TrimSegmentStartCell(start, end, ref position, ref size);
                }

                var previewColor = route.PreviewIsValid ? Color.FromRgb(70, 190, 90) : Color.FromRgb(230, 90, 90);
                DrawConveyorStrip(context, position, size, segment.RotationZDegrees, previewColor, renderInfo, 0.45, true, state);

                if (route.Anchors.Count > 1)
                {
                    var previousStart = route.Anchors[^2];
                    var previousEnd = route.Anchors[^1];
                    if (ConveyorRouteRendering.TryGetTurnJoinCell(previousStart, previousEnd, start, end, out var joinCell))
                    {
                        DrawConveyorJoinCap(context, joinCell, previewColor, 0.52, state);
                    }
                }
            }
        }

        foreach (var anchor in route.Anchors)
        {
            DrawIsoBox(context, anchor, new VoxelSize(1, 1, 1), Color.FromRgb(245, 220, 80), 0.75, drawOutline: true, state);
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

    private void DrawConveyorStrip(DrawingContext context, VoxelCoord position, VoxelSize size, int rotationZDegrees,
        Color color, PartRenderInfo renderInfo, double opacity, bool drawOutline, EditorState state)
    {
        var visualHeight = Math.Min(0.28, size.HeightZ);
        var x0 = position.X;
        var x1 = position.X + size.WidthX;
        var y0 = position.Y;
        var y1 = position.Y + size.DepthY;
        var z0 = position.Z;
        var z1 = position.Z + visualHeight;

        var topA = Project(x0, y0, z1, state);
        var topB = Project(x1, y0, z1, state);
        var topC = Project(x1, y1, z1, state);
        var topD = Project(x0, y1, z1, state);

        var bottomB = Project(x1, y0, z0, state);
        var bottomC = Project(x1, y1, z0, state);
        var bottomD = Project(x0, y1, z0, state);

        var topBrush = new SolidColorBrush(WithOpacity(Darken(color, 0.92), opacity));
        var rightBrush = new SolidColorBrush(WithOpacity(Darken(color, 0.70), opacity * 0.7));
        var leftBrush = new SolidColorBrush(WithOpacity(Darken(color, 0.58), opacity * 0.7));

        context.DrawGeometry(topBrush, null, Polygon(topA, topB, topC, topD));
        context.DrawGeometry(rightBrush, null, Polygon(topB, bottomB, bottomC, topC));
        context.DrawGeometry(leftBrush, null, Polygon(topD, topC, bottomC, bottomD));

        DrawConveyorMarker(context, position, size, rotationZDegrees, renderInfo, opacity, state);

        if (drawOutline)
        {
            var outline = new Pen(new SolidColorBrush(WithOpacity(Color.FromRgb(230, 230, 230), Math.Min(opacity + 0.18, 1))), 1);
            context.DrawGeometry(null, outline, Polygon(topA, topB, topC, topD));
        }
    }

    private void DrawConveyorJoinCap(DrawingContext context, VoxelCoord position, Color color, double opacity, EditorState state)
    {
        var capSize = new VoxelSize(1, 1, 1);
        var visualHeight = Math.Min(0.34, capSize.HeightZ);
        var x0 = position.X;
        var x1 = position.X + capSize.WidthX;
        var y0 = position.Y;
        var y1 = position.Y + capSize.DepthY;
        var z0 = position.Z;
        var z1 = position.Z + visualHeight;

        var topA = Project(x0, y0, z1, state);
        var topB = Project(x1, y0, z1, state);
        var topC = Project(x1, y1, z1, state);
        var topD = Project(x0, y1, z1, state);

        var bottomB = Project(x1, y0, z0, state);
        var bottomC = Project(x1, y1, z0, state);
        var bottomD = Project(x0, y1, z0, state);

        var topBrush = new SolidColorBrush(WithOpacity(Darken(color, 0.96), opacity));
        var rightBrush = new SolidColorBrush(WithOpacity(Darken(color, 0.76), opacity * 0.8));
        var leftBrush = new SolidColorBrush(WithOpacity(Darken(color, 0.68), opacity * 0.8));

        context.DrawGeometry(topBrush, null, Polygon(topA, topB, topC, topD));
        context.DrawGeometry(rightBrush, null, Polygon(topB, bottomB, bottomC, topC));
        context.DrawGeometry(leftBrush, null, Polygon(topD, topC, bottomC, bottomD));

        var outline = new Pen(new SolidColorBrush(WithOpacity(Color.FromRgb(230, 230, 230), Math.Min(opacity + 0.12, 1))), 1);
        context.DrawGeometry(null, outline, Polygon(topA, topB, topC, topD));
    }

    private void DrawConveyorSceneJoins(DrawingContext context, EditorState state)
    {
        for (var i = 1; i < state.Scene.Objects.Count; i++)
        {
            var previous = state.Scene.Objects[i - 1];
            var next = state.Scene.Objects[i];
            if (!ConveyorRouteRendering.TryGetSceneTurnJoinCell(previous, next, out var joinCell))
            {
                continue;
            }

            var previousVisibility = ObjectVisibility.GetVisibility(previous, state.ActiveFloor, state.ActiveAbsoluteZ);
            var nextVisibility = ObjectVisibility.GetVisibility(next, state.ActiveFloor, state.ActiveAbsoluteZ);
            if (previousVisibility == ObjectVisibilityMode.Hidden || nextVisibility == ObjectVisibilityMode.Hidden)
            {
                continue;
            }

            var opacity = previousVisibility == ObjectVisibilityMode.SolidActiveLayer || nextVisibility == ObjectVisibilityMode.SolidActiveLayer
                ? 1.0
                : 0.3;
            DrawConveyorJoinCap(context, joinCell, Color.FromRgb(78, 158, 216), opacity, state);
        }
    }

    private Point Project(double x, double y, double z, EditorState state)
    {
        return ViewportMath.Project(x, y, z, Bounds, state);
    }

    private static bool CanStartPan(PointerPointProperties pointerProperties, EditorState? state)
        => pointerProperties.IsMiddleButtonPressed
            || (pointerProperties.IsRightButtonPressed
                && state?.IsSelectionRotationMode != true
                && !CanFinishActiveRoute(state));

    private static bool IsPanReleased(PointerPointProperties pointerProperties)
        => !pointerProperties.IsMiddleButtonPressed && !pointerProperties.IsRightButtonPressed;

    private static bool CanFinishActiveRoute(EditorState? state)
        => state?.ActiveTool is ConveyorRouteTool routeTool && routeTool.HasFinishableRoute(state);

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
        int rotationZDegrees, PartRenderInfo renderInfo, double opacity, EditorState state)
    {
        if (!renderInfo.ShowFacingMarker)
        {
            return;
        }

        if (string.Equals(renderInfo.PartId, "conveyor", StringComparison.OrdinalIgnoreCase))
        {
            DrawConveyorMarker(context, position, effectiveSize, rotationZDegrees, renderInfo, opacity, state);
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

        var markerColor = renderInfo.FacingMarkerColor.ToAvaloniaColor();
        var brush = new SolidColorBrush(WithOpacity(markerColor, Math.Min(opacity + 0.25, 1.0)));
        context.DrawGeometry(brush, null, Polygon(tip, base1, base2));
    }

    private void DrawConveyorMarker(DrawingContext context, VoxelCoord position, VoxelSize effectiveSize, int rotationZDegrees,
        PartRenderInfo renderInfo, double opacity, EditorState state)
    {
        var normalized = RotationHelper.NormalizeDegrees(rotationZDegrees);
        var z = position.Z + Math.Min(0.32, effectiveSize.HeightZ);
        var markerColor = renderInfo.FacingMarkerColor.ToAvaloniaColor();
        var pen = new Pen(new SolidColorBrush(WithOpacity(markerColor, Math.Min(opacity + 0.22, 1.0))), 1.6);

        if (normalized is 0 or 180)
        {
            var y = position.Y + effectiveSize.DepthY / 2.0;
            var start = Project(position.X + 0.12, y, z, state);
            var end = Project(position.X + effectiveSize.WidthX - 0.12, y, z, state);
            context.DrawLine(pen, start, end);
            return;
        }

        var x = position.X + effectiveSize.WidthX / 2.0;
        var startY = Project(x, position.Y + 0.12, z, state);
        var endY = Project(x, position.Y + effectiveSize.DepthY - 0.12, z, state);
        context.DrawLine(pen, startY, endY);
    }
}
