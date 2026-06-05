using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
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
    private readonly DispatcherTimer _blinkTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(250)
    };

    public SoftwareCubeViewport()
    {
        ClipToBounds = true;
        Focusable = true;

        PointerMoved += OnPointerMoved;
        PointerPressed += OnPointerPressed;
        PointerReleased += OnPointerReleased;
        PointerExited += OnPointerExited;
        PointerWheelChanged += OnPointerWheelChanged;
        _blinkTimer.Tick += OnBlinkTick;
        _blinkTimer.Start();
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
        var conveyorCellsByObject = ConveyorRouteCellVisualization.BuildSceneObjectCells(state.Scene.Objects);

        foreach (var renderable in SceneRenderOrder.GetVisibleBackToFront(state, Bounds))
        {
            var sceneObject = renderable.SceneObject;
            var visibility = renderable.Visibility;

            var drawPosition = sceneObject.Position;
            var drawRotation = sceneObject.IsConveyor
                ? sceneObject.GetConveyorFlowRotationDegrees()
                : sceneObject.RotationZDegrees;
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
                DrawConveyorCellsSw(context, sceneObject, drawPosition, drawSize, drawRotation, color, renderInfo, opacity, state, conveyorCellsByObject);
            }
            else
            {
                DrawIsoBox(context, drawPosition, drawSize, color, opacity, drawOutline: false, state);
                DrawFacingMarker(context, drawPosition, drawSize, drawRotation, renderInfo, opacity, state);
            }
        }

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

        DrawMaterialTokens(context, state);
        DrawRotationAxisGuide(context, state);
    }

    private void DrawMaterialTokens(DrawingContext context, EditorState state)
    {
        var tokens = state.Scene.MaterialFlow.GetTokens();
        if (tokens.Count == 0)
        {
            return;
        }

        var snapshot = state.GetPortConnectivitySnapshot();
        var blinkOn = IsBlinkOn();
        foreach (var token in tokens)
        {
            if (!snapshot.TryGetPort(token.Location.PortId, out var port))
            {
                continue;
            }

            if (token.State == MaterialTokenState.Blocked && !blinkOn)
            {
                continue;
            }

            var color = token.State == MaterialTokenState.Active
                ? Color.FromArgb(230, 230, 140, 40)
                : Color.FromArgb(230, 255, 72, 72);

            var cx = port.WorldPosition.X;
            var cy = port.WorldPosition.Y;
            var cz = port.WorldPosition.Z + 0.1;

            var center = Project(cx, cy, cz, state);
            const double r = 7.0;
            var brush = new SolidColorBrush(color);
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)), 1.0);
            context.DrawGeometry(brush, pen, Polygon(
                new Point(center.X, center.Y - r),
                new Point(center.X + r, center.Y),
                new Point(center.X, center.Y + r),
                new Point(center.X - r, center.Y)));
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

    private void OnBlinkTick(object? sender, EventArgs e)
    {
        if (EditorState is { } state
            && (state.Scene.MaterialFlow.GetTokens().Count > 0
                || state.Scene.Objects.Any(static obj => obj.IsConveyor)
                || state.ActiveConveyorRoute is not null))
        {
            InvalidateVisual();
        }
    }

    private static bool IsBlinkOn()
    {
        var phase = (DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond) / 350;
        return phase % 2 == 0;
    }

    private static double Lerp(double a, double b, double t) => a + ((b - a) * t);

    private static double Wrap01(double value)
    {
        var wrapped = value % 1.0;
        return wrapped < 0 ? wrapped + 1.0 : wrapped;
    }

    private static double GetConveyorAnimationPhase(VoxelCoord position, double speed, double sign)
    {
        var seconds = DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerSecond;
        var seed = ((position.X * 73856093) ^ (position.Y * 19349663) ^ (position.Z * 83492791)) & 1023;
        var offset = seed / 1024.0;
        return Wrap01(offset + seconds * speed * sign);
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

            if (e is PointerPressedEventArgs pressed && pressed.ClickCount >= 2)
            {
                if (state.ActiveTool is ConveyorRouteTool routeTool && routeTool.HasFinishableRoute(state))
                {
                    routeTool.FinishRoute(state);
                }
                else if (state.ActiveTool is SelectTool && state.SelectedObject is { } obj)
                {
                    var cx = obj.Position.X + obj.EffectiveSize.WidthX * 0.5;
                    var cy = obj.Position.Y + obj.EffectiveSize.DepthY * 0.5;
                    var cz = obj.Position.Z + obj.EffectiveSize.HeightZ * 0.5;
                    ViewportMath.CenterViewOn(state, Bounds, cx, cy, cz);
                }
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

        SceneObject? bestNonConveyor = null;
        SceneObject? bestConveyor = null;

        for (var i = state.Scene.Objects.Count - 1; i >= 0; i--)
        {
            var sceneObject = state.Scene.Objects[i];
            if (!state.IntersectsActiveLayer(sceneObject) || !state.IsObjectWithinGrid(sceneObject))
            {
                continue;
            }

            var bounds = GetObjectScreenBounds(sceneObject, state).Inflate(2);
            if (!bounds.Contains(point))
            {
                continue;
            }

            if (sceneObject.IsConveyor)
            {
                bestConveyor ??= sceneObject;
            }
            else
            {
                bestNonConveyor ??= sceneObject;
            }
        }

        return bestNonConveyor ?? bestConveyor;
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
        var (committedCells, previewCells) = ConveyorRouteCellVisualization.BuildRouteDraftCells(
            route.Anchors, route.PreviewEnd);

        var committedColor = Color.FromRgb(78, 158, 216);
        foreach (var cell in committedCells)
        {
            DrawConveyorCellSw(context, cell, committedColor, 0.55, state);
        }

        var previewColor = route.PreviewIsValid ? Color.FromRgb(70, 190, 90) : Color.FromRgb(230, 90, 90);
        foreach (var cell in previewCells)
        {
            DrawConveyorCellSw(context, cell, previewColor, 0.45, state);
        }

        foreach (var anchor in route.Anchors)
        {
            var outline = new Pen(new SolidColorBrush(WithOpacity(Color.FromRgb(245, 220, 80), 0.75)), 1.5);
            var x0 = anchor.X;
            var x1 = anchor.X + 1;
            var y0 = anchor.Y;
            var y1 = anchor.Y + 1;
            var z = anchor.Z;
            var topA = Project(x0, y0, z, state);
            var topB = Project(x1, y0, z, state);
            var topC = Project(x1, y1, z, state);
            var topD = Project(x0, y1, z, state);
            context.DrawLine(outline, topA, topB);
            context.DrawLine(outline, topB, topC);
            context.DrawLine(outline, topC, topD);
            context.DrawLine(outline, topD, topA);
        }
    }

    private void DrawConveyorCellsSw(
        DrawingContext context,
        SceneObject sceneObject,
        VoxelCoord fallbackPosition,
        VoxelSize fallbackSize,
        int fallbackRotationZDegrees,
        Color color,
        PartRenderInfo renderInfo,
        double opacity,
        EditorState state,
        IReadOnlyDictionary<Guid, IReadOnlyList<ConveyorVisualCell>> cellsByObject)
    {
        if (!cellsByObject.TryGetValue(sceneObject.Id, out var cells) || cells.Count == 0)
        {
            DrawConveyorStrip(context, fallbackPosition, fallbackSize, fallbackRotationZDegrees, color, renderInfo, opacity, false, state);
            return;
        }

        var isSelected = state.SelectedObject?.Id == sceneObject.Id;
        var isHovered = state.HoveredObject?.Id == sceneObject.Id;
        foreach (var cell in cells)
        {
            DrawConveyorCellSw(context, cell, color, opacity, state, isSelected, isHovered);
        }
    }

    private void DrawConveyorCellSw(
        DrawingContext context,
        ConveyorVisualCell cell,
        Color color,
        double opacity,
        EditorState state,
        bool isSelected = false,
        bool isHovered = false)
    {
        const double beltH = 0.14;
        const double railH = 0.18;
        const double railW = 0.09;

        var x0 = cell.Position.X;
        var x1 = cell.Position.X + 1;
        var y0 = cell.Position.Y;
        var y1 = cell.Position.Y + 1;
        var z0 = cell.Position.Z;

        var beltTop = Color.FromRgb(44, 47, 54);
        var beltRight = Color.FromRgb(33, 36, 43);
        var beltFront = Color.FromRgb(29, 32, 39);
        var railTop = Color.FromRgb(196, 203, 212);
        var railRight = Color.FromRgb(142, 151, 163);
        var railFront = Color.FromRgb(120, 129, 142);
        if (isSelected)
        {
            beltTop = TintColor(Color.FromRgb(120, 180, 255), beltTop, 0.22);
            beltRight = TintColor(Color.FromRgb(120, 180, 255), beltRight, 0.18);
            beltFront = TintColor(Color.FromRgb(120, 180, 255), beltFront, 0.18);
            railTop = TintColor(Color.FromRgb(170, 210, 255), railTop, 0.20);
            railRight = TintColor(Color.FromRgb(170, 210, 255), railRight, 0.16);
            railFront = TintColor(Color.FromRgb(170, 210, 255), railFront, 0.16);
        }
        else if (isHovered)
        {
            beltTop = TintColor(Color.FromRgb(235, 220, 130), beltTop, 0.14);
            beltRight = TintColor(Color.FromRgb(235, 220, 130), beltRight, 0.10);
            beltFront = TintColor(Color.FromRgb(235, 220, 130), beltFront, 0.10);
        }

        var isXFlow = cell.MainFlowDirection is PortDirection.PositiveX or PortDirection.NegativeX;

        if (cell.Kind is ConveyorVisualCellKind.Straight or ConveyorVisualCellKind.Endpoint)
        {
            if (isXFlow)
            {
                DrawConveyorBarSw(context, x0, x1, y0 + railW, y1 - railW, z0, z0 + beltH, beltTop, beltRight, beltFront, opacity, state);
                DrawConveyorBarSw(context, x0, x1, y0, y0 + railW, z0, z0 + railH, railTop, railRight, railFront, opacity, state);
                DrawConveyorBarSw(context, x0, x1, y1 - railW, y1, z0, z0 + railH, railTop, railRight, railFront, opacity, state);
            }
            else
            {
                DrawConveyorBarSw(context, x0 + railW, x1 - railW, y0, y1, z0, z0 + beltH, beltTop, beltRight, beltFront, opacity, state);
                DrawConveyorBarSw(context, x0, x0 + railW, y0, y1, z0, z0 + railH, railTop, railRight, railFront, opacity, state);
                DrawConveyorBarSw(context, x1 - railW, x1, y0, y1, z0, z0 + railH, railTop, railRight, railFront, opacity, state);
            }
        }
        else
        {
            DrawConveyorBarSw(context, x0 + railW, x1 - railW, y0 + railW, y1 - railW, z0, z0 + beltH, beltTop, beltRight, beltFront, opacity, state);
            DrawConveyorBarSw(context, x0,          x1,          y0,          y0 + railW,  z0, z0 + railH, railTop, railRight, railFront, opacity, state);
            DrawConveyorBarSw(context, x0,          x1,          y1 - railW,  y1,          z0, z0 + railH, railTop, railRight, railFront, opacity, state);
            DrawConveyorBarSw(context, x0,          x0 + railW,  y0 + railW,  y1 - railW,  z0, z0 + railH, railTop, railRight, railFront, opacity, state);
            DrawConveyorBarSw(context, x1 - railW,  x1,          y0 + railW,  y1 - railW,  z0, z0 + railH, railTop, railRight, railFront, opacity, state);
        }

        DrawConveyorBeltMotionSw(context, cell, opacity, state);
        if (state.Scene.ConveyorRouteFlow.TryGetPacketAtCell(cell.Position, out var packet))
        {
            DrawConveyorRoutePacketSw(context, cell, packet!, opacity, state);
        }

        var topA = Project(x0, y0, z0 + railH, state);
        var topB = Project(x1, y0, z0 + railH, state);
        var topC = Project(x1, y1, z0 + railH, state);
        var topD = Project(x0, y1, z0 + railH, state);
        var boundaryColor = isSelected
            ? Color.FromRgb(160, 205, 255)
            : isHovered
                ? Color.FromRgb(232, 224, 150)
                : Color.FromRgb(226, 226, 226);
        var boundaryOpacity = isSelected || isHovered
            ? Math.Min(opacity + 0.20, 1.0)
            : Math.Min(opacity + 0.12, 1.0);
        var boundaryPen = new Pen(new SolidColorBrush(WithOpacity(boundaryColor, boundaryOpacity)), 1);
        context.DrawLine(boundaryPen, topA, topB);
        context.DrawLine(boundaryPen, topB, topC);
        context.DrawLine(boundaryPen, topC, topD);
        context.DrawLine(boundaryPen, topD, topA);

        DrawConveyorCellFlowSw(context, cell, opacity, state);
    }

    private void DrawConveyorBarSw(DrawingContext context, double x0, double x1, double y0, double y1,
        double z0, double z1, Color topColor, Color rightColor, Color frontColor, double opacity, EditorState state)
    {
        var tA = Project(x0, y0, z1, state);
        var tB = Project(x1, y0, z1, state);
        var tC = Project(x1, y1, z1, state);
        var tD = Project(x0, y1, z1, state);
        var bB = Project(x1, y0, z0, state);
        var bC = Project(x1, y1, z0, state);
        var bD = Project(x0, y1, z0, state);
        context.DrawGeometry(new SolidColorBrush(WithOpacity(topColor,   opacity)),        null, Polygon(tA, tB, tC, tD));
        context.DrawGeometry(new SolidColorBrush(WithOpacity(rightColor, opacity * 0.80)), null, Polygon(tB, bB, bC, tC));
        context.DrawGeometry(new SolidColorBrush(WithOpacity(frontColor, opacity * 0.80)), null, Polygon(tD, tC, bC, bD));
    }

    private static Color TintColor(Color tint, Color baseColor, double tintStrength)
    {
        return Color.FromArgb(
            baseColor.A,
            (byte)Math.Clamp((int)(baseColor.R * (1 - tintStrength) + tint.R * tintStrength), 0, 255),
            (byte)Math.Clamp((int)(baseColor.G * (1 - tintStrength) + tint.G * tintStrength), 0, 255),
            (byte)Math.Clamp((int)(baseColor.B * (1 - tintStrength) + tint.B * tintStrength), 0, 255));
    }

    private void DrawConveyorCellFlowSw(DrawingContext context, ConveyorVisualCell cell, double opacity, EditorState state)
    {
        var z = cell.Position.Z + 0.208;
        var centerX = cell.Position.X + 0.5;
        var centerY = cell.Position.Y + 0.5;
        var flowColor = Color.FromRgb(245, 215, 145);
        var glyphOpacity = Math.Clamp(opacity * 0.44 + 0.10, 0.12, 0.54);
        var flowPen = new Pen(new SolidColorBrush(WithOpacity(flowColor, glyphOpacity)), 1.5);
        var arrowBrush = new SolidColorBrush(WithOpacity(flowColor, glyphOpacity));

        var arrowDirection = cell.ExitDirection ?? cell.MainFlowDirection;
        var (arrowDx, arrowDy) = DirectionToPlanarVector(arrowDirection);
        var tail = Project(centerX - arrowDx * 0.26, centerY - arrowDy * 0.26, z, state);
        var head = Project(centerX + arrowDx * 0.14, centerY + arrowDy * 0.14, z, state);
        context.DrawLine(flowPen, tail, head);
        DrawArrowHeadSw(context, arrowBrush, centerX, centerY, z, arrowDx, arrowDy, 0.16, 0.19, state);
    }

    private void DrawConveyorBeltMotionSw(DrawingContext context, ConveyorVisualCell cell, double opacity, EditorState state)
    {
        const double railW = 0.09;
        var stripeColor = Color.FromRgb(104, 110, 122);
        var stripeOpacity = Math.Clamp(opacity * 0.62 + 0.12, 0.15, 0.9);
        var z = cell.Position.Z + 0.141;

        var minX = cell.Position.X + railW;
        var maxX = cell.Position.X + 1.0 - railW;
        var minY = cell.Position.Y + railW;
        var maxY = cell.Position.Y + 1.0 - railW;

        var flowDirection = cell.ExitDirection ?? cell.MainFlowDirection;
        var alongX = flowDirection is PortDirection.PositiveX or PortDirection.NegativeX;
        var directionSign = flowDirection is PortDirection.PositiveX or PortDirection.PositiveY ? 1.0 : -1.0;
        var phase = GetConveyorAnimationPhase(cell.Position, 0.95, directionSign);

        const double stripeHalfWidth = 0.027;
        for (var i = -1; i <= 2; i++)
        {
            var t = Wrap01(phase + i * 0.31);
            if (alongX)
            {
                var cx = Lerp(minX, maxX, t);
                DrawConveyorTopQuadSw(context, cx - stripeHalfWidth, cx + stripeHalfWidth, minY, maxY, z, stripeColor, stripeOpacity, state);
            }
            else
            {
                var cy = Lerp(minY, maxY, t);
                DrawConveyorTopQuadSw(context, minX, maxX, cy - stripeHalfWidth, cy + stripeHalfWidth, z, stripeColor, stripeOpacity, state);
            }
        }
    }

    private void DrawConveyorRoutePacketSw(DrawingContext context, ConveyorVisualCell cell, OrePacket packet, double opacity, EditorState state)
    {
        var flowDirection = cell.ExitDirection ?? cell.MainFlowDirection;
        var (flowX, flowY) = DirectionToPlanarVector(flowDirection);
        var sideX = -flowY;
        var sideY = flowX;
        const double packetSideOffset = 0.17;
        var packetX = cell.Position.X + 0.5 + sideX * packetSideOffset;
        var packetY = cell.Position.Y + 0.5 + sideY * packetSideOffset;
        var clumpUnits = Math.Max(1, packet.UnitCount);
        var clumpScale = Math.Min(2.05, 1.18 + (Math.Sqrt(clumpUnits) - 1.0) * 0.42);
        var clumpHeightScale = Math.Min(2.1, 1.0 + (clumpUnits - 1) * 0.14);

        var packetHalfLength = (Math.Abs(flowX) > 0.5 ? 0.12 : 0.09) * clumpScale;
        var packetHalfWidth = (Math.Abs(flowX) > 0.5 ? 0.09 : 0.12) * clumpScale;
        var z0 = cell.Position.Z + 0.145;
        var material = MaterialCatalog.Resolve(packet.MaterialId);
        var particleOpacity = Math.Min(opacity + 0.05, 1.0);
        if (TryResolveGranuleProfile(material.Id, clumpUnits, out var granuleCount, out var minGranuleSize, out var maxGranuleSize, out var granuleHeightScale))
        {
            DrawConveyorRouteGranulesSw(
                context,
                cell,
                packet,
                material,
                particleOpacity,
                state,
                packetX - packetHalfLength,
                packetX + packetHalfLength,
                packetY - packetHalfWidth,
                packetY + packetHalfWidth,
                z0,
                granuleCount,
                minGranuleSize,
                maxGranuleSize,
                granuleHeightScale);
            return;
        }

        var z1 = z0 + 0.08 * clumpHeightScale;
        DrawOutlinedConveyorBarSw(
            context,
            packetX - packetHalfLength,
            packetX + packetHalfLength,
            packetY - packetHalfWidth,
            packetY + packetHalfWidth,
            z0,
            z1,
            material.TopColor,
            material.RightColor,
            material.FrontColor,
            particleOpacity,
            GetMaterialOutlineColor(material),
            state);
    }

    private void DrawConveyorRouteGranulesSw(
        DrawingContext context,
        ConveyorVisualCell cell,
        OrePacket packet,
        MaterialDefinition material,
        double opacity,
        EditorState state,
        double minX,
        double maxX,
        double minY,
        double maxY,
        double baseZ,
        int granuleCount,
        double minGranuleSize,
        double maxGranuleSize,
        double granuleHeightScale)
    {
        for (var i = 0; i < granuleCount; i++)
        {
            var sizeSeed = HashPacketGranuleSeed(cell.Position, packet.UnitCount, i, material.Id, 11);
            var posSeedX = HashPacketGranuleSeed(cell.Position, packet.UnitCount, i, material.Id, 23);
            var posSeedY = HashPacketGranuleSeed(cell.Position, packet.UnitCount, i, material.Id, 37);
            var zSeed = HashPacketGranuleSeed(cell.Position, packet.UnitCount, i, material.Id, 53);
            var tintSeed = HashPacketGranuleSeed(cell.Position, packet.UnitCount, i, material.Id, 71);

            var size = Lerp(minGranuleSize, maxGranuleSize, HashToUnit(posSeedX ^ sizeSeed));
            var halfSize = size * 0.5;
            var centerX = Lerp(minX + halfSize, maxX - halfSize, HashToUnit(posSeedX));
            var centerY = Lerp(minY + halfSize, maxY - halfSize, HashToUnit(posSeedY));
            var z0 = baseZ + 0.004 + HashToUnit(zSeed) * 0.022;
            var z1 = z0 + size * granuleHeightScale;

            var topColor = TintColor(Color.FromRgb(250, 250, 250), material.TopColor, 0.05 + HashToUnit(tintSeed) * 0.12);
            var rightColor = TintColor(Color.FromRgb(20, 20, 20), material.RightColor, 0.06 + HashToUnit(tintSeed ^ 0x3D4E5F71u) * 0.16);
            var frontColor = TintColor(Color.FromRgb(20, 20, 20), material.FrontColor, 0.06 + HashToUnit(tintSeed ^ 0x9B7AA321u) * 0.16);

            DrawOutlinedConveyorBarSw(
                context,
                centerX - halfSize,
                centerX + halfSize,
                centerY - halfSize,
                centerY + halfSize,
                z0,
                z1,
                topColor,
                rightColor,
                frontColor,
                opacity,
                GetMaterialOutlineColor(material),
                state);
        }
    }

    private void DrawOutlinedConveyorBarSw(
        DrawingContext context,
        double x0,
        double x1,
        double y0,
        double y1,
        double z0,
        double z1,
        Color topColor,
        Color rightColor,
        Color frontColor,
        double opacity,
        Color outlineColor,
        EditorState state)
    {
        const double outlinePad = 0.014;
        DrawConveyorBarSw(
            context,
            x0 - outlinePad,
            x1 + outlinePad,
            y0 - outlinePad,
            y1 + outlinePad,
            z0 - 0.003,
            z1 + 0.006,
            outlineColor,
            outlineColor,
            outlineColor,
            Math.Min(opacity + 0.12, 1.0),
            state);
        DrawConveyorBarSw(context, x0, x1, y0, y1, z0, z1, topColor, rightColor, frontColor, opacity, state);
    }

    private void DrawConveyorTopQuadSw(
        DrawingContext context,
        double x0,
        double x1,
        double y0,
        double y1,
        double z,
        Color color,
        double opacity,
        EditorState state)
    {
        var a = Project(x0, y0, z, state);
        var b = Project(x1, y0, z, state);
        var c = Project(x1, y1, z, state);
        var d = Project(x0, y1, z, state);
        context.DrawGeometry(new SolidColorBrush(WithOpacity(color, opacity)), null, Polygon(a, b, c, d));
    }

    private void DrawConveyorChevronSw(
        DrawingContext context,
        Pen pen,
        double centerX,
        double centerY,
        double z,
        double dirX,
        double dirY,
        double depth,
        double halfWidth,
        EditorState state)
    {
        var perpX = -dirY;
        var perpY = dirX;
        var tip = Project(centerX + dirX * depth, centerY + dirY * depth, z, state);
        var left = Project(centerX - dirX * depth + perpX * halfWidth, centerY - dirY * depth + perpY * halfWidth, z, state);
        var right = Project(centerX - dirX * depth - perpX * halfWidth, centerY - dirY * depth - perpY * halfWidth, z, state);
        context.DrawLine(pen, left, tip);
        context.DrawLine(pen, right, tip);
    }

    private void DrawArrowHeadSw(
        DrawingContext context,
        IBrush brush,
        double centerX,
        double centerY,
        double z,
        double dirX,
        double dirY,
        double tipDistance,
        double halfWidth,
        EditorState state)
    {
        var perpX = -dirY;
        var perpY = dirX;
        var tip = Project(centerX + dirX * (tipDistance + 0.19), centerY + dirY * (tipDistance + 0.19), z, state);
        var baseX = centerX + dirX * tipDistance;
        var baseY = centerY + dirY * tipDistance;
        var arrowA = Project(baseX + perpX * halfWidth, baseY + perpY * halfWidth, z, state);
        var arrowB = Project(baseX - perpX * halfWidth, baseY - perpY * halfWidth, z, state);
        context.DrawGeometry(brush, null, Polygon(tip, arrowA, arrowB));
    }

    private static (double X, double Y) DirectionToPlanarVector(PortDirection direction) => direction switch
    {
        PortDirection.PositiveX => (1, 0),
        PortDirection.NegativeX => (-1, 0),
        PortDirection.PositiveY => (0, 1),
        PortDirection.NegativeY => (0, -1),
        _ => (0, 0)
    };

    private static bool TryResolveGranuleProfile(
        string materialId,
        int clumpUnits,
        out int granuleCount,
        out double minGranuleSize,
        out double maxGranuleSize,
        out double granuleHeightScale)
    {
        if (string.Equals(materialId, "Coal", StringComparison.OrdinalIgnoreCase))
        {
            granuleCount = Math.Min(Math.Max(clumpUnits, 1), 100);
            minGranuleSize = 0.105;
            maxGranuleSize = 0.190;
            granuleHeightScale = 1.0;
            return true;
        }

        if (string.Equals(materialId, "Sand", StringComparison.OrdinalIgnoreCase))
        {
            granuleCount = Math.Min(Math.Max(clumpUnits, 1), 100);
            minGranuleSize = 0.050;
            maxGranuleSize = 0.092;
            granuleHeightScale = 0.72;
            return true;
        }

        granuleCount = 0;
        minGranuleSize = 0;
        maxGranuleSize = 0;
        granuleHeightScale = 0;
        return false;
    }

    private static uint HashPacketGranuleSeed(VoxelCoord cell, int unitCount, int granuleIndex, string materialId, uint salt)
    {
        unchecked
        {
            var hash = 2166136261u;
            hash = (hash ^ (uint)(cell.X * 73856093)) * 16777619u;
            hash = (hash ^ (uint)(cell.Y * 19349663)) * 16777619u;
            hash = (hash ^ (uint)(cell.Z * 83492791)) * 16777619u;
            hash = (hash ^ (uint)unitCount) * 16777619u;
            hash = (hash ^ (uint)granuleIndex) * 16777619u;
            foreach (var ch in materialId)
            {
                hash = (hash ^ ch) * 16777619u;
            }

            return (hash ^ salt) * 16777619u;
        }
    }

    private static double HashToUnit(uint hash)
        => (hash & 0x00FFFFFFu) / 16777215.0;

    private static Color GetMaterialOutlineColor(MaterialDefinition material)
    {
        var luminance = (material.TopColor.R * 0.299) + (material.TopColor.G * 0.587) + (material.TopColor.B * 0.114);
        return luminance < 115
            ? Color.FromRgb(202, 211, 219)
            : Color.FromRgb(42, 45, 52);
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
        if (renderInfo.FlowMarkerKind == FlowMarkerKind.Incoming)
        {
            fdx = -fdx;
            fdy = -fdy;
        }

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
        var markerOpacity = Math.Min(opacity + 0.25, 1.0);
        if (renderInfo.FlowMarkerKind == FlowMarkerKind.Incoming)
        {
            var wingDistance = arrowLen * 0.55;
            var wingSpread = arrowBase * 0.85;
            var wing1 = Project(tipX - fdx * wingDistance + px * wingSpread, tipY - fdy * wingDistance + py * wingSpread, z1, state);
            var wing2 = Project(tipX - fdx * wingDistance - px * wingSpread, tipY - fdy * wingDistance - py * wingSpread, z1, state);
            var pen = new Pen(new SolidColorBrush(WithOpacity(markerColor, markerOpacity)), 1.8);
            context.DrawLine(pen, tip, wing1);
            context.DrawLine(pen, tip, wing2);
            return;
        }

        var brush = new SolidColorBrush(WithOpacity(markerColor, markerOpacity));
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
