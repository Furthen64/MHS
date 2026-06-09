using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using Mhs.Editor.Editor;
using Mhs.Editor.Settings;
using Mhs.Editor.Viewport.Gl;

namespace Mhs.Editor.Viewport;

public sealed class OpenGlViewportControl : OpenGlControlBase
{
    public static readonly StyledProperty<EditorState?> EditorStateProperty =
        AvaloniaProperty.Register<OpenGlViewportControl, EditorState?>(nameof(EditorState));
    public static readonly StyledProperty<ViewportInteractionPreset> InteractionPresetProperty =
        AvaloniaProperty.Register<OpenGlViewportControl, ViewportInteractionPreset>(
            nameof(InteractionPreset),
            defaultValue: ViewportInteractionPreset.BlenderLike);

    private GlRenderer? _renderer;
    private string? _initError;
    private bool _isPanning;
    private Point _lastPanPoint;
    private readonly DispatcherTimer _blinkTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(250)
    };

    static OpenGlViewportControl()
    {
        AffectsRender<OpenGlViewportControl>(EditorStateProperty, InteractionPresetProperty);
    }

    public OpenGlViewportControl()
    {
        ClipToBounds = true;
        IsHitTestVisible = true;
        Focusable = true;

        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerExitedEvent, OnPointerExited, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
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

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, Bounds);
        base.Render(context);
        DrawViewCubeOverlay(context);
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

    protected override void OnOpenGlInit(GlInterface gl)
    {
        try
        {
            _initError = null;
            _renderer?.Dispose();
            _renderer = new GlRenderer(gl);
            var diagnostics = $"{_renderer.Vendor} | {_renderer.Renderer} | {_renderer.Version}";
            Console.WriteLine($"[OpenGL] Initialized: {diagnostics}");
            StartupDiagnostics.Log($"OpenGL initialized: {diagnostics}");
            if (EditorState is { } state)
            {
                state.OpenGlBackendInfo = diagnostics;
                if (state.StatusMessage.StartsWith("OpenGL", StringComparison.OrdinalIgnoreCase))
                {
                    state.StatusMessage = "Ready";
                }
            }
        }
        catch (Exception ex)
        {
            _initError = ex.Message;
            Console.Error.WriteLine($"[OpenGL] Initialization failed: {ex}");
            StartupDiagnostics.Log($"OpenGL initialization failed: {ex}");
            if (EditorState is { } state)
            {
                state.OpenGlBackendInfo = $"Init failed: {_initError}";
                state.StatusMessage = $"OpenGL init failed: {_initError}";
            }
        }
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (Bounds.Width < 2 || Bounds.Height < 2)
        {
            return;
        }

        var state = EditorState;
        if (state is null || _renderer is null)
        {
            return;
        }

        _renderer.BeginFrame(fb, Bounds.Size, GetViewportBackgroundColor(state));

        if (!string.IsNullOrWhiteSpace(_initError))
        {
            _renderer.RenderFrame();
            return;
        }

        if (state.ViewportVisualMode == ViewportVisualMode.Presentation)
        {
            DrawPresentationFloor(state.ActiveAbsoluteZ);
        }

        if (state.ShowBounds)
        {
            DrawFloorOutlines(state.ActiveFloor);
        }

        if (state.ShowGrid)
        {
            DrawGrid(state.ActiveAbsoluteZ);
        }

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
            if (sceneObject.IsConveyor)
            {
                DrawConveyorCells(sceneObject, drawPosition, drawSize, drawRotation, color, renderInfo, opacity, state, conveyorCellsByObject);
            }
            else
            {
                DrawPartShape(drawPosition, drawSize, drawRotation, color, renderInfo, opacity, drawOutline: false, state);
                DrawFacingMarker(drawPosition, drawSize, drawRotation, renderInfo, opacity, state);
                if (string.Equals(sceneObject.PartId, "mtrlsrc", StringComparison.OrdinalIgnoreCase))
                {
                    DrawMtrlSrcOutputPulse(sceneObject, drawPosition, drawSize, drawRotation, renderInfo, opacity, state);
                }
            }
        }

        if (state.IsMovingSelection && state.SelectedObject is { } moving && state.MovePreviewPosition is { } target)
        {
            var moveColor = state.MovePreviewIsValid
                ? PartRenderCatalog.Resolve(moving.PartType).BaseColor.ToAvaloniaColor()
                : Color.FromRgb(230, 90, 90);
            var movingRenderInfo = PartRenderCatalog.Resolve(moving.PartType);
            DrawPartShape(target, moving.EffectiveSize, moving.RotationZDegrees, moveColor, movingRenderInfo, 0.45, drawOutline: true, state);
            DrawFacingMarker(target, moving.EffectiveSize, moving.RotationZDegrees, movingRenderInfo, 0.45, state);
        }

        if (state.GhostPreview is { } ghost)
        {
            var ghostColor = ghost.IsValid
                ? ghost.Part.Color
                : Color.FromRgb(230, 90, 90);
            var ghostRenderInfo = PartRenderCatalog.Resolve(ghost.Part.Id);
            DrawPartShape(ghost.Position, ghost.EffectiveSize, ghost.RotationZDegrees, ghostColor, ghostRenderInfo, 0.4, drawOutline: true, state);
            DrawFacingMarker(ghost.Position, ghost.EffectiveSize, ghost.RotationZDegrees, ghostRenderInfo, 0.4, state);
        }

        if (state.ActiveConveyorRoute is { } route)
        {
            DrawConveyorRoutePreview(route, state);
        }

        if (state.HoveredObject is { } hovered
            && state.IntersectsActiveLayer(hovered)
            && state.SelectedObject?.Id != hovered.Id)
        {
            DrawOutline(hovered.Position, hovered.EffectiveSize, Color.FromRgb(232, 224, 150), 0.92, state);
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
            DrawOutline(outlinePosition, outlineSize, Color.FromRgb(88, 196, 255), selected.IsConveyor ? 0.95 : 1.0, state);
        }

        DrawPortDebug(state);
        if (state.ShowFlow)
        {
            DrawSelectedConveyorRouteOverlay(state);
            DrawMaterialTokens(state);
        }

        DrawRotationAxisGuide(state);

        _renderer.RenderFrame();
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        _renderer?.Dispose();
        _renderer = null;
        _initError = null;
    }

    protected override void OnOpenGlLost()
    {
        _initError = "OpenGL context was lost";
        _renderer?.Dispose();
        _renderer = null;
        if (EditorState is { } state)
        {
            state.OpenGlBackendInfo = "Context lost";
            state.StatusMessage = "OpenGL context lost";
        }
    }

    private void AttachState(EditorState state)
    {
        state.PropertyChanged += OnStatePropertyChanged;
        state.Scene.Objects.CollectionChanged += OnObjectsChanged;
        RequestNextFrameRendering();
    }

    private void DetachState(EditorState state)
    {
        state.PropertyChanged -= OnStatePropertyChanged;
        state.Scene.Objects.CollectionChanged -= OnObjectsChanged;
    }

    private void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RequestNextFrameRendering();
    }

    private void OnObjectsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RequestNextFrameRendering();
    }

    private void OnBlinkTick(object? sender, EventArgs e)
    {
        if (EditorState is { } state
            && (state.Scene.MaterialFlow.GetTokens().Count > 0
                || state.Scene.Objects.Any(static obj => obj.IsConveyor)
                || state.ActiveConveyorRoute is not null))
        {
            RequestNextFrameRendering();
        }
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
            RequestNextFrameRendering();
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
        RequestNextFrameRendering();
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

        RequestNextFrameRendering();
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

        RequestNextFrameRendering();
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


    private static Color GetViewportBackgroundColor(EditorState state)
        => AppThemeService.ViewportColors().Background;

    private void DrawPresentationFloor(int absoluteZ)
    {
        if (EditorState is not { } state || _renderer is null)
        {
            return;
        }

        var viewportColors = AppThemeService.ViewportColors();
        var floorColor = viewportColors.Floor;
        var alternateFloorColor = viewportColors.AlternateFloor;

        for (var x = WorldGridSettings.MinCoord; x < WorldGridSettings.MaxCoord; x++)
        {
            for (var y = WorldGridSettings.MinCoord; y < WorldGridSettings.MaxCoord; y++)
            {
                var a = Project(x, y, absoluteZ, state);
                var b = Project(x + 1, y, absoluteZ, state);
                var c = Project(x + 1, y + 1, absoluteZ, state);
                var d = Project(x, y + 1, absoluteZ, state);
                var tileColor = ((x + y) & 1) == 0 ? floorColor : alternateFloorColor;
                _renderer.AddFilledQuad(a, b, c, d, tileColor, 0.78);
            }
        }
    }

    private void DrawFloorOutlines(int activeFloor)
    {
        if (EditorState is not { } state || _renderer is null)
        {
            return;
        }

        for (var floor = 0; floor < WorldVerticalSettings.FloorCount; floor++)
        {
            var z = floor * WorldVerticalSettings.LayersPerFloor;
            var isActive = floor == activeFloor;
            var viewportColors = AppThemeService.ViewportColors();
            var color = isActive ? viewportColors.ActiveFloorLine : viewportColors.InactiveFloorLine;

            var a = Project(WorldGridSettings.MinCoord, WorldGridSettings.MinCoord, z, state);
            var b = Project(WorldGridSettings.MaxCoord, WorldGridSettings.MinCoord, z, state);
            var c = Project(WorldGridSettings.MaxCoord, WorldGridSettings.MaxCoord, z, state);
            var d = Project(WorldGridSettings.MinCoord, WorldGridSettings.MaxCoord, z, state);

            _renderer.AddLine(a, b, color, 1.0);
            _renderer.AddLine(b, c, color, 1.0);
            _renderer.AddLine(c, d, color, 1.0);
            _renderer.AddLine(d, a, color, 1.0);
        }
    }

    private void DrawGrid(int absoluteZ)
    {
        if (EditorState is not { } state || _renderer is null)
        {
            return;
        }

        var color = AppThemeService.ViewportColors().GridLine;

        for (var x = WorldGridSettings.MinCoord; x <= WorldGridSettings.MaxCoord; x++)
        {
            var start = Project(x, WorldGridSettings.MinCoord, absoluteZ, state);
            var end = Project(x, WorldGridSettings.MaxCoord, absoluteZ, state);
            _renderer.AddLine(start, end, color, 1.0);
        }

        for (var y = WorldGridSettings.MinCoord; y <= WorldGridSettings.MaxCoord; y++)
        {
            var start = Project(WorldGridSettings.MinCoord, y, absoluteZ, state);
            var end = Project(WorldGridSettings.MaxCoord, y, absoluteZ, state);
            _renderer.AddLine(start, end, color, 1.0);
        }
    }

    private void DrawRotationAxisGuide(EditorState state)
    {
        if (_renderer is null || state.SelectedObject is not { } selected || !state.HasRotationAxisFor(selected.Id))
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

            var guideColor = Color.FromArgb(220, 170, 220, 255);
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
                    _renderer.AddLine(last, point, guideColor, 1.0);
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
            var dotColor = state.SelectionRotationPreviewIsValid
                ? Color.FromArgb(245, 255, 240, 90)
                : Color.FromArgb(245, 255, 95, 95);
            DrawMarkerDot(dot, dotColor);
            return;
        }

        var axisColor = Color.FromArgb(210, 140, 210, 255);
        var start = Project(state.RotationAxisPivotX, state.RotationAxisPivotY, state.RotationAxisMinZ, state);
        var end = Project(state.RotationAxisPivotX, state.RotationAxisPivotY, state.RotationAxisMaxZ, state);
        _renderer.AddLine(start, end, axisColor, 1.0);
    }

    private void DrawMaterialTokens(EditorState state)
    {
        if (_renderer is null)
        {
            return;
        }

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
            _renderer.AddFilledQuad(
                new Point(center.X, center.Y - r),
                new Point(center.X + r, center.Y),
                new Point(center.X, center.Y + r),
                new Point(center.X - r, center.Y),
                color, 0.9);
        }
    }

    private void DrawPortDebug(EditorState state)
    {
        if (_renderer is null || !state.ShowConveyorDebug)
        {
            return;
        }

        var snapshot = state.GetPortConnectivitySnapshot();
        if (snapshot.Ports.Count == 0)
        {
            return;
        }

        var showObjectIds = state.Scene.Objects
            .Where(obj => state.IntersectsActiveFloor(obj))
            .Select(obj => obj.Id)
            .ToHashSet();
        var blockedPortIds = state.Scene.MaterialFlow.GetTokens()
            .Where(token => token.State == MaterialTokenState.Blocked)
            .Select(token => token.Location.PortId)
            .ToHashSet(StringComparer.Ordinal);
        var blinkOn = IsBlinkOn();

        foreach (var status in snapshot.PortStatuses)
        {
            var port = status.Port;
            if (!showObjectIds.Contains(port.OwnerSceneObjectId))
            {
                continue;
            }

            var markerColor = status.Status switch
            {
                PortConnectionStatus.Connected => Color.FromArgb(185, 74, 224, 120),
                PortConnectionStatus.InvalidNearby => Color.FromArgb(190, 244, 94, 94),
                _ => Color.FromArgb(170, 255, 214, 96)
            };
            if (blockedPortIds.Contains(port.PortId))
            {
                markerColor = blinkOn
                    ? Color.FromArgb(245, 255, 70, 70)
                    : Color.FromArgb(115, 255, 70, 70);
            }

            var (dx, dy, dz) = port.Direction.ToVector();
            var markerZ = port.WorldPosition.Z + 0.26;
            var markerX = port.WorldPosition.X + dx * 0.14;
            var markerY = port.WorldPosition.Y + dy * 0.14;
            var center = Project(markerX, markerY, markerZ, state);
            DrawPortMarker(center, port.Kind, markerColor, status.Status);

            var directionTip = Project(
                markerX + dx * 0.26,
                markerY + dy * 0.26,
                markerZ + dz * 0.22,
                state);
            _renderer.AddLine(center, directionTip, markerColor, 0.55);
        }

        foreach (var connection in snapshot.Connections)
        {
            if (!showObjectIds.Contains(connection.FromObjectId)
                || !showObjectIds.Contains(connection.ToObjectId)
                || !snapshot.TryGetPort(connection.FromPortId, out var fromPort)
                || !snapshot.TryGetPort(connection.ToPortId, out var toPort))
            {
                continue;
            }

            var start = Project(fromPort.WorldPosition.X, fromPort.WorldPosition.Y, fromPort.WorldPosition.Z + 0.24, state);
            var end = Project(toPort.WorldPosition.X, toPort.WorldPosition.Y, toPort.WorldPosition.Z + 0.24, state);
            _renderer.AddLine(start, end, Color.FromArgb(185, 92, 238, 140), 0.8);
        }

        foreach (var invalid in snapshot.InvalidNearbyCandidates)
        {
            if (!snapshot.TryGetPort(invalid.PortAId, out var a)
                || !snapshot.TryGetPort(invalid.PortBId, out var b)
                || !showObjectIds.Contains(a.OwnerSceneObjectId)
                || !showObjectIds.Contains(b.OwnerSceneObjectId))
            {
                continue;
            }

            var issueColor = invalid.Reason switch
            {
                ConnectionInvalidReason.WrongFacing => Color.FromArgb(205, 255, 176, 68),
                ConnectionInvalidReason.IncompatiblePortKind => Color.FromArgb(205, 189, 120, 255),
                ConnectionInvalidReason.DifferentZ => Color.FromArgb(205, 104, 174, 255),
                ConnectionInvalidReason.SameOwner => Color.FromArgb(180, 160, 160, 160),
                ConnectionInvalidReason.AmbiguousCandidate => Color.FromArgb(205, 255, 110, 110),
                _ => Color.FromArgb(205, 255, 120, 120)
            };
            var start = Project(a.WorldPosition.X, a.WorldPosition.Y, a.WorldPosition.Z + 0.28, state);
            var end = Project(b.WorldPosition.X, b.WorldPosition.Y, b.WorldPosition.Z + 0.28, state);
            _renderer.AddLine(start, end, issueColor, 0.8);
        }
    }

    private void DrawMarkerDot(Point center, Color color)
    {
        if (_renderer is null)
        {
            return;
        }

        const double radius = 4.4;
        var a = new Point(center.X, center.Y - radius);
        var b = new Point(center.X + radius, center.Y);
        var c = new Point(center.X, center.Y + radius);
        var d = new Point(center.X - radius, center.Y);
        _renderer.AddFilledQuad(a, b, c, d, color, 1.0);
    }

    private void DrawPortMarker(Point center, PortKind kind, Color color, PortConnectionStatus status)
    {
        if (_renderer is null)
        {
            return;
        }

        const double radius = 4.6;
        var a = new Point(center.X, center.Y - radius);
        var b = new Point(center.X + radius, center.Y);
        var c = new Point(center.X, center.Y + radius);
        var d = new Point(center.X - radius, center.Y);
        _renderer.AddFilledQuad(a, b, c, d, color, 0.72);

        var accent = status == PortConnectionStatus.InvalidNearby
            ? Color.FromArgb(255, 255, 230, 230)
            : Color.FromArgb(255, 245, 245, 245);
        var inner = radius * 0.55;
        switch (kind)
        {
            case PortKind.Output:
                _renderer.AddFilledTriangle(
                    new Point(center.X + inner, center.Y),
                    new Point(center.X - inner * 0.75, center.Y - inner * 0.65),
                    new Point(center.X - inner * 0.75, center.Y + inner * 0.65),
                    accent,
                    0.74);
                break;
            case PortKind.Input:
                _renderer.AddFilledQuad(
                    new Point(center.X - inner, center.Y - inner),
                    new Point(center.X + inner, center.Y - inner),
                    new Point(center.X + inner, center.Y + inner),
                    new Point(center.X - inner, center.Y + inner),
                    accent,
                    0.74);
                break;
            default:
                _renderer.AddFilledQuad(
                    new Point(center.X, center.Y - inner),
                    new Point(center.X + inner, center.Y),
                    new Point(center.X, center.Y + inner),
                    new Point(center.X - inner, center.Y),
                    accent,
                    0.74);
                break;
        }
    }

    private void DrawOutline(VoxelCoord position, VoxelSize size, Color color, double opacity, EditorState state)
    {
        if (_renderer is null)
        {
            return;
        }

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

        _renderer.AddLine(topA, topB, color, opacity);
        _renderer.AddLine(topB, topC, color, opacity);
        _renderer.AddLine(topC, topD, color, opacity);
        _renderer.AddLine(topD, topA, color, opacity);

        _renderer.AddLine(topA, Project(x0, y0, z0, state), color, opacity);
        _renderer.AddLine(topB, Project(x1, y0, z0, state), color, opacity);
        _renderer.AddLine(topC, Project(x1, y1, z0, state), color, opacity);
        _renderer.AddLine(topD, Project(x0, y1, z0, state), color, opacity);
    }

    private void DrawSelectedConveyorRouteOverlay(EditorState state)
    {
        if (_renderer is null || state.SelectedObject is not { IsConveyor: true } selected)
        {
            return;
        }

        var route = state.Scene.ConveyorRouteFlow.Routes
            .FirstOrDefault(candidate => candidate.SegmentObjectIds.Contains(selected.Id));
        if (route is null)
        {
            return;
        }

        var overlayColor = Color.FromRgb(112, 214, 255);
        foreach (var cell in route.Cells)
        {
            DrawConveyorTopQuad(
                cell.X + 0.04,
                cell.X + 0.96,
                cell.Y + 0.04,
                cell.Y + 0.96,
                cell.Z + 0.205,
                Color.FromRgb(70, 190, 255),
                0.16,
                state);

            var a = Project(cell.X + 0.04, cell.Y + 0.04, cell.Z + 0.215, state);
            var b = Project(cell.X + 0.96, cell.Y + 0.04, cell.Z + 0.215, state);
            var c = Project(cell.X + 0.96, cell.Y + 0.96, cell.Z + 0.215, state);
            var d = Project(cell.X + 0.04, cell.Y + 0.96, cell.Z + 0.215, state);
            DrawConveyorCellTopBoundary(a, b, c, d, overlayColor, 0.98);
        }
    }


    private void DrawPartShape(VoxelCoord position, VoxelSize size, int rotationZDegrees, Color color,
        PartRenderInfo renderInfo, double opacity, bool drawOutline, EditorState state)
    {
        switch (renderInfo.RenderStyle)
        {
            case PartRenderStyle.ConveyorStraight:
                DrawConceptConveyorStraight(position, size, rotationZDegrees, opacity, drawOutline, state);
                break;
            case PartRenderStyle.ConveyorCurve:
                DrawConceptConveyorCurve(position, size, opacity, drawOutline, state);
                break;
            case PartRenderStyle.OpenContainer:
                DrawOpenTopContainer(position, size, color, renderInfo.DetailColor.ToAvaloniaColor(), opacity, drawOutline, state);
                break;
            case PartRenderStyle.Hopper:
                DrawHopper(position, size, color, renderInfo.DetailColor.ToAvaloniaColor(), opacity, drawOutline, state);
                break;
            case PartRenderStyle.Chute:
                DrawChute(position, size, rotationZDegrees, color, renderInfo.DetailColor.ToAvaloniaColor(), opacity, drawOutline, state);
                break;
            case PartRenderStyle.MaterialSource:
                DrawMaterialEndpoint(position, size, color, renderInfo.DetailColor.ToAvaloniaColor(), opacity, drawOutline, state, isSource: true);
                break;
            case PartRenderStyle.MaterialReceiver:
                DrawMaterialEndpoint(position, size, color, renderInfo.DetailColor.ToAvaloniaColor(), opacity, drawOutline, state, isSource: false);
                break;
            case PartRenderStyle.Platform:
                DrawPlatform(position, size, color, renderInfo.DetailColor.ToAvaloniaColor(), opacity, drawOutline, state);
                break;
            case PartRenderStyle.Fence:
                DrawFence(position, size, color, renderInfo.DetailColor.ToAvaloniaColor(), opacity, drawOutline, state);
                break;
            case PartRenderStyle.SupportFrame:
                DrawSupportFrame(position, size, color, opacity, drawOutline, state);
                break;
            case PartRenderStyle.VerticalLift:
                DrawVerticalLift(position, size, color, opacity, drawOutline, state, spiral: false);
                break;
            case PartRenderStyle.SpiralLift:
                DrawVerticalLift(position, size, color, opacity, drawOutline, state, spiral: true);
                break;
            default:
                DrawIsoBox(position, size, color, opacity, drawOutline, state);
                break;
        }
    }

    private void DrawConceptConveyorStraight(VoxelCoord position, VoxelSize size, int rotationZDegrees, double opacity, bool drawOutline, EditorState state)
    {
        var rail = Math.Min(0.12, Math.Min(size.WidthX, size.DepthY) * 0.18);
        var height = Math.Min(0.30, size.HeightZ);
        var x0 = position.X;
        var x1 = position.X + size.WidthX;
        var y0 = position.Y;
        var y1 = position.Y + size.DepthY;
        var z0 = position.Z;
        var isX = RotationHelper.NormalizeDegrees(rotationZDegrees) is 0 or 180;
        var beltTop = Color.FromRgb(30, 32, 38);
        var beltRight = Color.FromRgb(24, 26, 31);
        var beltFront = Color.FromRgb(20, 22, 27);
        var railTop = Color.FromRgb(160, 168, 178);
        var railRight = Color.FromRgb(108, 117, 128);
        var railFront = Color.FromRgb(96, 103, 114);
        var safety = Color.FromRgb(255, 211, 55);

        if (isX)
        {
            DrawConveyorBar(x0, x1, y0 + rail, y1 - rail, z0, z0 + height * 0.56, beltTop, beltRight, beltFront, opacity, state);
            DrawConveyorBar(x0, x1, y0, y0 + rail, z0, z0 + height, railTop, railRight, railFront, opacity, state);
            DrawConveyorBar(x0, x1, y1 - rail, y1, z0, z0 + height, railTop, railRight, railFront, opacity, state);
            DrawConveyorTopQuad(x0, x1, y0 + rail, y0 + rail * 1.42, z0 + height + 0.006, safety, opacity, state);
            DrawConveyorTopQuad(x0, x1, y1 - rail * 1.42, y1 - rail, z0 + height + 0.006, safety, opacity, state);
            for (var i = 1; i <= Math.Max(2, size.WidthX); i++)
            {
                var x = x0 + i * size.WidthX / (Math.Max(2, size.WidthX) + 1.0);
                DrawConveyorTopQuad(x - 0.025, x + 0.025, y0 + rail * 1.6, y1 - rail * 1.6, z0 + height * 0.58, Color.FromRgb(92, 98, 108), opacity * 0.85, state);
            }
        }
        else
        {
            DrawConveyorBar(x0 + rail, x1 - rail, y0, y1, z0, z0 + height * 0.56, beltTop, beltRight, beltFront, opacity, state);
            DrawConveyorBar(x0, x0 + rail, y0, y1, z0, z0 + height, railTop, railRight, railFront, opacity, state);
            DrawConveyorBar(x1 - rail, x1, y0, y1, z0, z0 + height, railTop, railRight, railFront, opacity, state);
            DrawConveyorTopQuad(x0 + rail, x0 + rail * 1.42, y0, y1, z0 + height + 0.006, safety, opacity, state);
            DrawConveyorTopQuad(x1 - rail * 1.42, x1 - rail, y0, y1, z0 + height + 0.006, safety, opacity, state);
            for (var i = 1; i <= Math.Max(2, size.DepthY); i++)
            {
                var y = y0 + i * size.DepthY / (Math.Max(2, size.DepthY) + 1.0);
                DrawConveyorTopQuad(x0 + rail * 1.6, x1 - rail * 1.6, y - 0.025, y + 0.025, z0 + height * 0.58, Color.FromRgb(92, 98, 108), opacity * 0.85, state);
            }
        }

        DrawConveyorMarker(position, size, rotationZDegrees, PartRenderCatalog.Resolve("conveyor_straight"), opacity, state);
        if (drawOutline)
        {
            DrawOutline(position, size, Color.FromRgb(230, 230, 230), Math.Min(opacity + 0.18, 1), state);
        }
    }

    private void DrawConceptConveyorCurve(VoxelCoord position, VoxelSize size, double opacity, bool drawOutline, EditorState state)
    {
        var height = Math.Min(0.30, size.HeightZ);
        var x0 = position.X;
        var x1 = position.X + size.WidthX;
        var y0 = position.Y;
        var y1 = position.Y + size.DepthY;
        var z = position.Z + height;
        DrawConveyorBar(x0, x1, y0, y1, position.Z, position.Z + height * 0.55, Color.FromRgb(31, 33, 39), Color.FromRgb(23, 25, 30), Color.FromRgb(20, 22, 27), opacity, state);
        const int steps = 6;
        var inner = new Point[steps + 1];
        var outer = new Point[steps + 1];
        for (var i = 0; i <= steps; i++)
        {
            var t = i / (double)steps;
            var a = Math.PI * 0.5 * t;
            var ox = x0 + Math.Cos(a) * size.WidthX;
            var oy = y0 + Math.Sin(a) * size.DepthY;
            var ix = x0 + 0.34 * size.WidthX + Math.Cos(a) * size.WidthX * 0.58;
            var iy = y0 + 0.34 * size.DepthY + Math.Sin(a) * size.DepthY * 0.58;
            outer[i] = Project(ox, oy, z + 0.012, state);
            inner[i] = Project(ix, iy, z + 0.012, state);
        }
        for (var i = 0; i < steps; i++)
        {
            _renderer!.AddFilledQuad(outer[i], outer[i + 1], inner[i + 1], inner[i], Color.FromRgb(38, 41, 48), opacity);
            _renderer.AddLine(outer[i], outer[i + 1], Color.FromRgb(255, 211, 55), Math.Min(opacity + 0.18, 1));
        }
        DrawConveyorCellTopBoundary(Project(x0, y0, z, state), Project(x1, y0, z, state), Project(x1, y1, z, state), Project(x0, y1, z, state), Color.FromRgb(145, 153, 164), opacity * 0.8);
        if (drawOutline)
        {
            DrawOutline(position, size, Color.FromRgb(230, 230, 230), Math.Min(opacity + 0.18, 1), state);
        }
    }

    private void DrawOpenTopContainer(VoxelCoord position, VoxelSize size, Color color, Color fillColor, double opacity, bool drawOutline, EditorState state)
    {
        if (_renderer is null)
        {
            return;
        }

        var wall = Math.Min(0.16, Math.Min(size.WidthX, size.DepthY) * 0.16);
        var z0 = position.Z;
        var z1 = position.Z + size.HeightZ;
        var x0 = position.X;
        var x1 = position.X + size.WidthX;
        var y0 = position.Y;
        var y1 = position.Y + size.DepthY;
        var side = Darken(color, 0.72);
        var front = Darken(color, 0.58);

        DrawConveyorBar(x0, x1, y0, y0 + wall, z0, z1, color, side, front, opacity, state);
        DrawConveyorBar(x0, x1, y1 - wall, y1, z0, z1, color, side, front, opacity, state);
        DrawConveyorBar(x0, x0 + wall, y0 + wall, y1 - wall, z0, z1, color, side, front, opacity, state);
        DrawConveyorBar(x1 - wall, x1, y0 + wall, y1 - wall, z0, z1, color, side, front, opacity, state);
        DrawMaterialFillSurface(x0 + wall * 1.25, x1 - wall * 1.25, y0 + wall * 1.25, y1 - wall * 1.25, z1 - size.HeightZ * 0.18, fillColor, opacity, state);

        var rim = Color.FromRgb(232, 232, 218);
        DrawConveyorCellTopBoundary(Project(x0, y0, z1 + 0.01, state), Project(x1, y0, z1 + 0.01, state), Project(x1, y1, z1 + 0.01, state), Project(x0, y1, z1 + 0.01, state), rim, Math.Min(opacity + 0.16, 1));
        DrawConveyorCellTopBoundary(Project(x0 + wall, y0 + wall, z1 + 0.012, state), Project(x1 - wall, y0 + wall, z1 + 0.012, state), Project(x1 - wall, y1 - wall, z1 + 0.012, state), Project(x0 + wall, y1 - wall, z1 + 0.012, state), Darken(fillColor, 0.70), Math.Min(opacity + 0.10, 1));

        if (drawOutline)
        {
            DrawOutline(position, size, Color.FromRgb(230, 230, 230), Math.Min(opacity + 0.20, 1), state);
        }
    }

    private void DrawHopper(VoxelCoord position, VoxelSize size, Color color, Color fillColor, double opacity, bool drawOutline, EditorState state)
    {
        DrawOpenTopContainer(position, size, color, fillColor, opacity, drawOutline, state);
        DrawContainerLegs(position, size, color, opacity, state);
        var spoutWidth = Math.Min(0.24, Math.Min(size.WidthX, size.DepthY) * 0.22);
        var cx = position.X + size.WidthX * 0.5;
        var cy = position.Y + size.DepthY * 0.5;
        DrawConveyorBar(cx - spoutWidth, cx + spoutWidth, cy - spoutWidth, cy + spoutWidth, Math.Max(0, position.Z - 0.42), position.Z + 0.08, Darken(color, 0.78), Darken(color, 0.58), Darken(color, 0.48), opacity, state);
    }

    private void DrawChute(VoxelCoord position, VoxelSize size, int rotationZDegrees, Color color, Color linerColor, double opacity, bool drawOutline, EditorState state)
    {
        if (_renderer is null)
        {
            return;
        }

        var normalized = RotationHelper.NormalizeDegrees(rotationZDegrees);
        var isX = normalized is 0 or 180;
        var x0 = position.X;
        var x1 = position.X + size.WidthX;
        var y0 = position.Y;
        var y1 = position.Y + size.DepthY;
        var zLow = position.Z + Math.Min(0.18, size.HeightZ * 0.35);
        var zHigh = position.Z + Math.Min(size.HeightZ, 0.78);
        var slopeForward = normalized is 0 or 90;

        Point a; Point b; Point c; Point d;
        if (isX)
        {
            var za = slopeForward ? zHigh : zLow;
            var zb = slopeForward ? zLow : zHigh;
            a = Project(x0, y0, za, state); b = Project(x1, y0, zb, state); c = Project(x1, y1, zb, state); d = Project(x0, y1, za, state);
        }
        else
        {
            var za = slopeForward ? zHigh : zLow;
            var zb = slopeForward ? zLow : zHigh;
            a = Project(x0, y0, za, state); b = Project(x1, y0, za, state); c = Project(x1, y1, zb, state); d = Project(x0, y1, zb, state);
        }

        _renderer.AddFilledQuad(a, b, c, d, color, opacity);
        _renderer.AddLine(a, b, Color.FromRgb(230, 226, 214), Math.Min(opacity + 0.18, 1));
        _renderer.AddLine(c, d, Color.FromRgb(230, 226, 214), Math.Min(opacity + 0.18, 1));
        _renderer.AddFilledQuad(a, b, Project((x0 + x1) * 0.5, (y0 + y1) * 0.5, (zLow + zHigh) * 0.5 - 0.04, state), d, linerColor, opacity * 0.32);
        DrawSupportFeet(position, size, color, opacity, state);
        if (drawOutline)
        {
            DrawOutline(position, size, Color.FromRgb(230, 230, 230), Math.Min(opacity + 0.18, 1), state);
        }
    }

    private void DrawMaterialEndpoint(VoxelCoord position, VoxelSize size, Color color, Color fillColor, double opacity, bool drawOutline, EditorState state, bool isSource)
    {
        if (isSource)
        {
            DrawHopper(position, size, color, fillColor, opacity, drawOutline, state);
            return;
        }

        DrawOpenTopContainer(position, size, color, fillColor, opacity, drawOutline, state);
        DrawSupportFeet(position, size, color, opacity, state);
    }

    private void DrawPlatform(VoxelCoord position, VoxelSize size, Color color, Color detailColor, double opacity, bool drawOutline, EditorState state)
    {
        var deckHeight = Math.Min(0.20, size.HeightZ);
        DrawConveyorBar(position.X, position.X + size.WidthX, position.Y, position.Y + size.DepthY, position.Z + size.HeightZ - deckHeight, position.Z + size.HeightZ, color, Darken(color, 0.70), Darken(color, 0.56), opacity, state);
        DrawContainerLegs(position, size, color, opacity, state);
        DrawRailings(position, size, detailColor, opacity, state, includeFront: false);
        var stripeZ = position.Z + size.HeightZ + 0.012;
        DrawConveyorTopQuad(position.X + 0.10, position.X + size.WidthX - 0.10, position.Y + 0.10, position.Y + 0.16, stripeZ, detailColor, opacity * 0.85, state);
        if (drawOutline)
        {
            DrawOutline(position, size, Color.FromRgb(230, 230, 230), Math.Min(opacity + 0.18, 1), state);
        }
    }

    private void DrawFence(VoxelCoord position, VoxelSize size, Color color, Color detailColor, double opacity, bool drawOutline, EditorState state)
    {
        var postWidth = Math.Min(0.10, Math.Min(size.WidthX, size.DepthY) * 0.18);
        var railHeight = Math.Max(0.36, size.HeightZ);
        DrawRailRun(position.X, position.X + size.WidthX, position.Y, position.Z, railHeight, postWidth, color, detailColor, opacity, state);
        DrawRailRun(position.X, position.X + size.WidthX, position.Y + size.DepthY - postWidth, position.Z, railHeight, postWidth, color, detailColor, opacity, state);
        if (drawOutline)
        {
            DrawOutline(position, size, Color.FromRgb(230, 230, 230), Math.Min(opacity + 0.18, 1), state);
        }
    }

    private void DrawSupportFrame(VoxelCoord position, VoxelSize size, Color color, double opacity, bool drawOutline, EditorState state)
    {
        var w = Math.Min(0.12, Math.Min(size.WidthX, size.DepthY) * 0.22);
        var z0 = position.Z;
        var z1 = position.Z + size.HeightZ;
        foreach (var (x, y) in new[] { (position.X, position.Y), (position.X + size.WidthX - w, position.Y), (position.X, position.Y + size.DepthY - w), (position.X + size.WidthX - w, position.Y + size.DepthY - w) })
        {
            DrawConveyorBar(x, x + w, y, y + w, z0, z1, color, Darken(color, 0.74), Darken(color, 0.60), opacity, state);
        }
        DrawConveyorBar(position.X, position.X + size.WidthX, position.Y, position.Y + w, z1 - w, z1, color, Darken(color, 0.74), Darken(color, 0.60), opacity, state);
        DrawConveyorBar(position.X, position.X + size.WidthX, position.Y + size.DepthY - w, position.Y + size.DepthY, z1 - w, z1, color, Darken(color, 0.74), Darken(color, 0.60), opacity, state);
        DrawConveyorBar(position.X, position.X + w, position.Y, position.Y + size.DepthY, z1 - w, z1, color, Darken(color, 0.74), Darken(color, 0.60), opacity, state);
        DrawConveyorBar(position.X + size.WidthX - w, position.X + size.WidthX, position.Y, position.Y + size.DepthY, z1 - w, z1, color, Darken(color, 0.74), Darken(color, 0.60), opacity, state);
        DrawTrussDiagonals(position, size, Color.FromRgb(210, 218, 226), opacity, state);
        DrawSupportFeet(position, size, color, opacity, state);
        if (drawOutline)
        {
            DrawOutline(position, size, Color.FromRgb(230, 230, 230), Math.Min(opacity + 0.20, 1), state);
        }
    }

    private void DrawVerticalLift(VoxelCoord position, VoxelSize size, Color color, double opacity, bool drawOutline, EditorState state, bool spiral)
    {
        var visualHeight = Math.Max(size.HeightZ, Math.Min(4, size.HeightZ + 1));
        var cx = position.X + size.WidthX * 0.5;
        var cy = position.Y + size.DepthY * 0.5;
        var half = Math.Min(size.WidthX, size.DepthY) * 0.22;
        DrawConveyorBar(cx - half, cx + half, cy - half, cy + half, position.Z, position.Z + visualHeight, color, Darken(color, 0.78), Darken(color, 0.62), opacity, state);
        var railColor = Color.FromRgb(210, 218, 226);
        foreach (var (x, y) in new[] { (cx - half * 1.7, cy - half * 1.7), (cx + half * 1.5, cy - half * 1.7), (cx - half * 1.7, cy + half * 1.5), (cx + half * 1.5, cy + half * 1.5) })
        {
            DrawConveyorBar(x, x + half * 0.20, y, y + half * 0.20, position.Z, position.Z + visualHeight, railColor, Darken(railColor, 0.72), Darken(railColor, 0.60), opacity, state);
        }
        if (spiral)
        {
            Point? prev = null;
            for (var i = 0; i <= 16; i++)
            {
                var t = i / 16.0;
                var a = t * Math.PI * 4.5;
                var r = half * 1.55;
                var pt = Project(cx + Math.Cos(a) * r, cy + Math.Sin(a) * r, position.Z + visualHeight * t, state);
                if (prev is { } p)
                {
                    _renderer!.AddLine(p, pt, Color.FromRgb(255, 211, 80), Math.Min(opacity + 0.10, 1));
                }
                prev = pt;
            }
        }
        if (drawOutline)
        {
            DrawOutline(position, new VoxelSize(size.WidthX, size.DepthY, visualHeight), Color.FromRgb(230, 230, 230), Math.Min(opacity + 0.20, 1), state);
        }
    }


    private void DrawContainerLegs(VoxelCoord position, VoxelSize size, Color color, double opacity, EditorState state)
    {
        var legWidth = Math.Min(0.10, Math.Min(size.WidthX, size.DepthY) * 0.16);
        var z0 = position.Z;
        var z1 = position.Z + Math.Max(0.08, size.HeightZ);
        foreach (var (x, y) in new[]
        {
            (position.X + legWidth * 0.8, position.Y + legWidth * 0.8),
            (position.X + size.WidthX - legWidth * 1.8, position.Y + legWidth * 0.8),
            (position.X + legWidth * 0.8, position.Y + size.DepthY - legWidth * 1.8),
            (position.X + size.WidthX - legWidth * 1.8, position.Y + size.DepthY - legWidth * 1.8)
        })
        {
            DrawConveyorBar(x, x + legWidth, y, y + legWidth, z0, z1, color, Darken(color, 0.66), Darken(color, 0.52), opacity, state);
        }
    }

    private void DrawTrussDiagonals(VoxelCoord position, VoxelSize size, Color color, double opacity, EditorState state)
    {
        if (_renderer is null)
        {
            return;
        }

        var x0 = position.X + Math.Min(0.12, size.WidthX * 0.16);
        var x1 = position.X + size.WidthX - Math.Min(0.12, size.WidthX * 0.16);
        var y0 = position.Y + Math.Min(0.12, size.DepthY * 0.16);
        var y1 = position.Y + size.DepthY - Math.Min(0.12, size.DepthY * 0.16);
        var z0 = position.Z + size.HeightZ * 0.18;
        var z1 = position.Z + size.HeightZ * 0.82;
        var trussOpacity = Math.Min(opacity + 0.10, 1);

        _renderer.AddLine(Project(x0, y0, z0, state), Project(x1, y0, z1, state), color, trussOpacity);
        _renderer.AddLine(Project(x1, y0, z0, state), Project(x0, y0, z1, state), Darken(color, 0.72), trussOpacity);
        _renderer.AddLine(Project(x0, y1, z0, state), Project(x1, y1, z1, state), color, trussOpacity);
        _renderer.AddLine(Project(x1, y1, z0, state), Project(x0, y1, z1, state), Darken(color, 0.72), trussOpacity);
    }

    private void DrawSupportFeet(VoxelCoord position, VoxelSize size, Color color, double opacity, EditorState state)
    {
        var foot = Math.Min(0.18, Math.Min(size.WidthX, size.DepthY) * 0.22);
        var z0 = position.Z;
        var z1 = position.Z + Math.Min(0.09, Math.Max(0.05, size.HeightZ * 0.16));
        foreach (var (x, y) in new[]
        {
            (position.X + foot * 0.35, position.Y + foot * 0.35),
            (position.X + size.WidthX - foot * 1.35, position.Y + foot * 0.35),
            (position.X + foot * 0.35, position.Y + size.DepthY - foot * 1.35),
            (position.X + size.WidthX - foot * 1.35, position.Y + size.DepthY - foot * 1.35)
        })
        {
            DrawConveyorBar(x, x + foot, y, y + foot, z0, z1, Darken(color, 0.74), Darken(color, 0.56), Darken(color, 0.46), opacity, state);
        }
    }

    private void DrawRailings(VoxelCoord position, VoxelSize size, Color color, double opacity, EditorState state, bool includeFront)
    {
        var postWidth = Math.Min(0.08, Math.Min(size.WidthX, size.DepthY) * 0.10);
        var z0 = position.Z + size.HeightZ;
        var railHeight = 0.48;
        DrawRailRun(position.X, position.X + size.WidthX, position.Y, z0, railHeight, postWidth, color, color, opacity, state);
        DrawRailRun(position.X, position.X + size.WidthX, position.Y + size.DepthY - postWidth, z0, railHeight, postWidth, color, color, opacity, state);
        if (includeFront)
        {
            DrawRailRunY(position.X, position.Y, position.Y + size.DepthY, z0, railHeight, postWidth, color, color, opacity, state);
            DrawRailRunY(position.X + size.WidthX - postWidth, position.Y, position.Y + size.DepthY, z0, railHeight, postWidth, color, color, opacity, state);
        }
    }

    private void DrawRailRun(double x0, double x1, double y, double z0, double railHeight, double postWidth, Color postColor, Color railColor, double opacity, EditorState state)
    {
        var spans = Math.Max(2, (int)Math.Ceiling(x1 - x0));
        for (var i = 0; i <= spans; i++)
        {
            var x = x0 + (x1 - x0 - postWidth) * i / spans;
            DrawConveyorBar(x, x + postWidth, y, y + postWidth, z0, z0 + railHeight, postColor, Darken(postColor, 0.66), Darken(postColor, 0.52), opacity, state);
        }

        DrawConveyorBar(x0, x1, y, y + postWidth, z0 + railHeight * 0.78, z0 + railHeight * 0.90, railColor, Darken(railColor, 0.68), Darken(railColor, 0.54), opacity, state);
        DrawConveyorBar(x0, x1, y, y + postWidth, z0 + railHeight * 0.42, z0 + railHeight * 0.52, railColor, Darken(railColor, 0.68), Darken(railColor, 0.54), opacity * 0.92, state);
    }

    private void DrawRailRunY(double x, double y0, double y1, double z0, double railHeight, double postWidth, Color postColor, Color railColor, double opacity, EditorState state)
    {
        var spans = Math.Max(2, (int)Math.Ceiling(y1 - y0));
        for (var i = 0; i <= spans; i++)
        {
            var y = y0 + (y1 - y0 - postWidth) * i / spans;
            DrawConveyorBar(x, x + postWidth, y, y + postWidth, z0, z0 + railHeight, postColor, Darken(postColor, 0.66), Darken(postColor, 0.52), opacity, state);
        }

        DrawConveyorBar(x, x + postWidth, y0, y1, z0 + railHeight * 0.78, z0 + railHeight * 0.90, railColor, Darken(railColor, 0.68), Darken(railColor, 0.54), opacity, state);
        DrawConveyorBar(x, x + postWidth, y0, y1, z0 + railHeight * 0.42, z0 + railHeight * 0.52, railColor, Darken(railColor, 0.68), Darken(railColor, 0.54), opacity * 0.92, state);
    }

    private void DrawMaterialFillSurface(double x0, double x1, double y0, double y1, double z, Color fillColor, double opacity, EditorState state)
    {
        if (x1 <= x0 || y1 <= y0)
        {
            return;
        }

        DrawConveyorTopQuad(x0, x1, y0, y1, z, fillColor, Math.Min(opacity + 0.08, 1), state);
        var ridge = TintColor(Color.FromRgb(255, 238, 170), fillColor, 0.18);
        var cx = (x0 + x1) * 0.5;
        var cy = (y0 + y1) * 0.5;
        _renderer?.AddLine(Project(x0 + (x1 - x0) * 0.18, cy, z + 0.012, state), Project(cx, y0 + (y1 - y0) * 0.18, z + 0.035, state), ridge, opacity * 0.64);
        _renderer?.AddLine(Project(cx, y0 + (y1 - y0) * 0.18, z + 0.035, state), Project(x1 - (x1 - x0) * 0.18, cy, z + 0.012, state), ridge, opacity * 0.64);
        _renderer?.AddLine(Project(x0 + (x1 - x0) * 0.18, cy, z + 0.012, state), Project(cx, y1 - (y1 - y0) * 0.18, z + 0.028, state), Darken(fillColor, 0.72), opacity * 0.50);
        _renderer?.AddLine(Project(cx, y1 - (y1 - y0) * 0.18, z + 0.028, state), Project(x1 - (x1 - x0) * 0.18, cy, z + 0.012, state), Darken(fillColor, 0.72), opacity * 0.50);
    }

    private void DrawIsoBox(VoxelCoord position, VoxelSize size, Color color, double opacity, bool drawOutline, EditorState state)
    {
        if (_renderer is null)
        {
            return;
        }

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

        var bottomB = Project(x1, y0, z0, state);
        var bottomC = Project(x1, y1, z0, state);
        var bottomD = Project(x0, y1, z0, state);

        _renderer.AddFilledQuad(topA, topB, topC, topD, color, opacity);
        _renderer.AddFilledQuad(topB, bottomB, bottomC, topC, Darken(color, 0.78), opacity);
        _renderer.AddFilledQuad(topD, topC, bottomC, bottomD, Darken(color, 0.62), opacity);

        if (drawOutline)
        {
            var outline = Color.FromRgb(230, 230, 230);
            DrawOutline(position, size, outline, Math.Min(opacity + 0.3, 1), state);
        }
    }

    private void DrawConveyorCells(
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
            DrawConveyorStrip(fallbackPosition, fallbackSize, fallbackRotationZDegrees, color, renderInfo, opacity, false, state);
            return;
        }

        var isSelected = state.SelectedObject?.Id == sceneObject.Id;
        var isHovered = state.HoveredObject?.Id == sceneObject.Id;

        foreach (var cell in cells)
        {
            DrawConveyorCell(cell, color, opacity, state, isSelected, isHovered);
        }
    }

    private void DrawConveyorCell(
        ConveyorVisualCell cell,
        Color color,
        double opacity,
        EditorState state,
        bool isSelected = false,
        bool isHovered = false,
        bool isPreview = false,
        bool previewIsValid = true)
    {
        if (_renderer is null)
        {
            return;
        }

        const double beltH = 0.14;
        const double railH = 0.18;
        const double railW = 0.09;

        var x0 = cell.Position.X;
        var x1 = cell.Position.X + 1.0;
        var y0 = cell.Position.Y;
        var y1 = cell.Position.Y + 1.0;
        var z0 = cell.Position.Z;

        var beltTop = Color.FromRgb(44, 47, 54);
        var beltRight = Color.FromRgb(33, 36, 43);
        var beltFront = Color.FromRgb(29, 32, 39);
        var railTop = Color.FromRgb(196, 203, 212);
        var railRight = Color.FromRgb(142, 151, 163);
        var railFront = Color.FromRgb(120, 129, 142);
        if (!isPreview && isSelected)
        {
            beltTop = TintColor(Color.FromRgb(120, 180, 255), beltTop, 0.22);
            beltRight = TintColor(Color.FromRgb(120, 180, 255), beltRight, 0.18);
            beltFront = TintColor(Color.FromRgb(120, 180, 255), beltFront, 0.18);
            railTop = TintColor(Color.FromRgb(170, 210, 255), railTop, 0.20);
            railRight = TintColor(Color.FromRgb(170, 210, 255), railRight, 0.16);
            railFront = TintColor(Color.FromRgb(170, 210, 255), railFront, 0.16);
        }
        else if (!isPreview && isHovered)
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
                DrawConveyorBar(x0, x1, y0 + railW, y1 - railW, z0, z0 + beltH, beltTop, beltRight, beltFront, opacity, state);
                DrawConveyorBar(x0, x1, y0, y0 + railW, z0, z0 + railH, railTop, railRight, railFront, opacity, state);
                DrawConveyorBar(x0, x1, y1 - railW, y1, z0, z0 + railH, railTop, railRight, railFront, opacity, state);
            }
            else
            {
                DrawConveyorBar(x0 + railW, x1 - railW, y0, y1, z0, z0 + beltH, beltTop, beltRight, beltFront, opacity, state);
                DrawConveyorBar(x0, x0 + railW, y0, y1, z0, z0 + railH, railTop, railRight, railFront, opacity, state);
                DrawConveyorBar(x1 - railW, x1, y0, y1, z0, z0 + railH, railTop, railRight, railFront, opacity, state);
            }
        }
        else
        {
            DrawConveyorBar(x0 + railW, x1 - railW, y0 + railW, y1 - railW, z0, z0 + beltH, beltTop, beltRight, beltFront, opacity, state);
            DrawConveyorBar(x0,          x1,          y0,          y0 + railW,  z0, z0 + railH, railTop, railRight, railFront, opacity, state);
            DrawConveyorBar(x0,          x1,          y1 - railW,  y1,          z0, z0 + railH, railTop, railRight, railFront, opacity, state);
            DrawConveyorBar(x0,          x0 + railW,  y0 + railW,  y1 - railW,  z0, z0 + railH, railTop, railRight, railFront, opacity, state);
            DrawConveyorBar(x1 - railW,  x1,          y0 + railW,  y1 - railW,  z0, z0 + railH, railTop, railRight, railFront, opacity, state);
        }

        DrawConveyorBeltMotion(cell, opacity, state, isPreview, previewIsValid);
        if (state.ShowFlow && !isPreview && state.Scene.ConveyorRouteFlow.TryGetPacketAtCell(cell.Position, out var packet))
        {
            DrawConveyorRoutePacket(cell, packet!, opacity, state);
        }

        if (!isPreview && state.ShowConveyorDebug)
        {
            DrawConveyorInputMarkers(cell, opacity, state);
        }

        var topA = Project(x0, y0, z0 + railH, state);
        var topB = Project(x1, y0, z0 + railH, state);
        var topC = Project(x1, y1, z0 + railH, state);
        var topD = Project(x0, y1, z0 + railH, state);
        var boundaryColor = !isPreview && isSelected
            ? Color.FromRgb(160, 205, 255)
            : !isPreview && isHovered
                ? Color.FromRgb(232, 224, 150)
                : Color.FromRgb(226, 226, 226);
        var boundaryBoost = !isPreview && (isSelected || isHovered) ? 0.20 : 0.12;
        if (state.ShowBounds)
        {
            DrawConveyorCellTopBoundary(topA, topB, topC, topD, boundaryColor, Math.Min(opacity + boundaryBoost, 1.0));
        }

        if (state.ShowFlow)
        {
            DrawConveyorCellFlow(cell, opacity, state, isPreview, previewIsValid);
        }
    }

    private void DrawConveyorBar(double x0, double x1, double y0, double y1, double z0, double z1,
        Color topColor, Color rightColor, Color frontColor, double opacity, EditorState state)
    {
        if (_renderer is null)
        {
            return;
        }

        var tA = Project(x0, y0, z1, state);
        var tB = Project(x1, y0, z1, state);
        var tC = Project(x1, y1, z1, state);
        var tD = Project(x0, y1, z1, state);
        var bB = Project(x1, y0, z0, state);
        var bC = Project(x1, y1, z0, state);
        var bD = Project(x0, y1, z0, state);
        _renderer.AddFilledQuad(tA, tB, tC, tD, topColor, opacity);
        _renderer.AddFilledQuad(tB, bB, bC, tC, rightColor, opacity * 0.80);
        _renderer.AddFilledQuad(tD, tC, bC, bD, frontColor, opacity * 0.80);
    }

    private static Color TintColor(Color tint, Color baseColor, double tintStrength)
    {
        return Color.FromArgb(
            baseColor.A,
            (byte)Math.Clamp((int)(baseColor.R * (1 - tintStrength) + tint.R * tintStrength), 0, 255),
            (byte)Math.Clamp((int)(baseColor.G * (1 - tintStrength) + tint.G * tintStrength), 0, 255),
            (byte)Math.Clamp((int)(baseColor.B * (1 - tintStrength) + tint.B * tintStrength), 0, 255));
    }

    private void DrawConveyorCellTopBoundary(Point topA, Point topB, Point topC, Point topD, Color boundaryColor, double opacity)
    {
        if (_renderer is null)
        {
            return;
        }

        _renderer.AddLine(topA, topB, boundaryColor, opacity);
        _renderer.AddLine(topB, topC, boundaryColor, opacity);
        _renderer.AddLine(topC, topD, boundaryColor, opacity);
        _renderer.AddLine(topD, topA, boundaryColor, opacity);
    }

    private void DrawConveyorCellFlow(
        ConveyorVisualCell cell,
        double opacity,
        EditorState state,
        bool isPreview = false,
        bool previewIsValid = true)
    {
        if (_renderer is null)
        {
            return;
        }

        var z = cell.Position.Z + 0.208;
        var centerX = cell.Position.X + 0.5;
        var centerY = cell.Position.Y + 0.5;
        var flowColor = isPreview && !previewIsValid
            ? Color.FromRgb(236, 145, 145)
            : Color.FromRgb(245, 215, 145);
        var glyphOpacity = Math.Clamp(opacity * 0.44 + (isPreview ? 0.07 : 0.10), 0.10, isPreview ? 0.46 : 0.54);

        var arrowDirection = cell.ExitDirection ?? cell.MainFlowDirection;
        var (arrowDx, arrowDy) = DirectionToPlanarVector(arrowDirection);
        var stemTail = Project(centerX - arrowDx * 0.26, centerY - arrowDy * 0.26, z, state);
        var stemHead = Project(centerX + arrowDx * 0.14, centerY + arrowDy * 0.14, z, state);
        _renderer.AddLine(stemTail, stemHead, flowColor, glyphOpacity * 0.9);
        DrawArrowHeadGl(centerX, centerY, z, arrowDx, arrowDy, 0.16, 0.19, flowColor, glyphOpacity, state);
    }

    private void DrawConveyorBeltMotion(
        ConveyorVisualCell cell,
        double opacity,
        EditorState state,
        bool isPreview,
        bool previewIsValid)
    {
        if (_renderer is null)
        {
            return;
        }

        const double railW = 0.09;
        var stripeColor = isPreview && !previewIsValid
            ? Color.FromRgb(156, 72, 72)
            : Color.FromRgb(104, 110, 122);
        var stripeOpacity = Math.Clamp(opacity * 0.62 + (isPreview ? 0.06 : 0.12), 0.15, 0.9);
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
                DrawConveyorTopQuad(cx - stripeHalfWidth, cx + stripeHalfWidth, minY, maxY, z, stripeColor, stripeOpacity, state);
            }
            else
            {
                var cy = Lerp(minY, maxY, t);
                DrawConveyorTopQuad(minX, maxX, cy - stripeHalfWidth, cy + stripeHalfWidth, z, stripeColor, stripeOpacity, state);
            }
        }
    }

    private void DrawConveyorRoutePacket(ConveyorVisualCell cell, OrePacket packet, double opacity, EditorState state)
    {
        var flowDirection = cell.ExitDirection ?? cell.MainFlowDirection;
        var (flowX, flowY) = DirectionToPlanarVector(flowDirection);
        if (flowX == 0 && flowY == 0)
        {
            return;
        }

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
            DrawConveyorRouteGranules(
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
        DrawOutlinedConveyorBar(
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

    private void DrawConveyorRouteGranules(
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

            DrawOutlinedConveyorBar(
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

    private void DrawOutlinedConveyorBar(
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
        DrawConveyorBar(
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
        DrawConveyorBar(x0, x1, y0, y1, z0, z1, topColor, rightColor, frontColor, opacity, state);
    }

    private void DrawConveyorInputMarkers(ConveyorVisualCell cell, double opacity, EditorState state)
    {
        var attachments = GetSelectedRouteAttachmentsForCell(state, cell.Position);
        if (attachments.Count == 0)
        {
            return;
        }

        var z = cell.Position.Z + 0.212;
        const double markerSize = 0.10;
        const double padding = 0.04;
        var maxMarkers = Math.Min(attachments.Count, 3);
        for (var i = 0; i < maxMarkers; i++)
        {
            var material = MaterialCatalog.Resolve(attachments[i].MaterialId);
            var x0 = cell.Position.X + padding + i * (markerSize + 0.03);
            var y0 = cell.Position.Y + 1.0 - padding - markerSize;
            DrawConveyorTopQuad(
                x0,
                x0 + markerSize,
                y0,
                y0 + markerSize,
                z,
                material.TopColor,
                Math.Min(opacity + 0.18, 1.0),
                state);
        }
    }

    private static IReadOnlyList<RouteInputAttachmentRuntime> GetSelectedRouteAttachmentsForCell(EditorState state, VoxelCoord cellPosition)
    {
        if (state.SelectedObject is not { IsConveyor: true } selected)
        {
            return Array.Empty<RouteInputAttachmentRuntime>();
        }

        var route = state.Scene.ConveyorRouteFlow.Routes
            .FirstOrDefault(candidate => candidate.SegmentObjectIds.Contains(selected.Id));
        if (route is null)
        {
            return Array.Empty<RouteInputAttachmentRuntime>();
        }

        var routeCellIndex = -1;
        for (var i = 0; i < route.Cells.Count; i++)
        {
            if (route.Cells[i] == cellPosition)
            {
                routeCellIndex = i;
                break;
            }
        }

        if (routeCellIndex < 0)
        {
            return Array.Empty<RouteInputAttachmentRuntime>();
        }

        return route.InputAttachments
            .Where(attachment => attachment.RouteCellIndex == routeCellIndex)
            .OrderBy(attachment => attachment.ObjectId)
            .ToArray();
    }

    private void DrawConveyorTopQuad(
        double x0,
        double x1,
        double y0,
        double y1,
        double z,
        Color color,
        double opacity,
        EditorState state)
    {
        if (_renderer is null)
        {
            return;
        }

        var a = Project(x0, y0, z, state);
        var b = Project(x1, y0, z, state);
        var c = Project(x1, y1, z, state);
        var d = Project(x0, y1, z, state);
        _renderer.AddFilledQuad(a, b, c, d, color, opacity);
    }

    private void DrawConveyorChevronGl(
        double centerX,
        double centerY,
        double z,
        double dirX,
        double dirY,
        double depth,
        double halfWidth,
        Color color,
        double opacity,
        EditorState state)
    {
        if (_renderer is null)
        {
            return;
        }

        var perpX = -dirY;
        var perpY = dirX;
        var tip = Project(centerX + dirX * depth, centerY + dirY * depth, z, state);
        var left = Project(centerX - dirX * depth + perpX * halfWidth, centerY - dirY * depth + perpY * halfWidth, z, state);
        var right = Project(centerX - dirX * depth - perpX * halfWidth, centerY - dirY * depth - perpY * halfWidth, z, state);
        _renderer.AddLine(left, tip, color, opacity);
        _renderer.AddLine(right, tip, color, opacity);
    }

    private void DrawArrowHeadGl(
        double centerX,
        double centerY,
        double z,
        double dirX,
        double dirY,
        double tipDistance,
        double halfWidth,
        Color color,
        double opacity,
        EditorState state)
    {
        if (_renderer is null)
        {
            return;
        }

        var perpX = -dirY;
        var perpY = dirX;
        var tip = Project(centerX + dirX * (tipDistance + 0.19), centerY + dirY * (tipDistance + 0.19), z, state);
        var baseX = centerX + dirX * tipDistance;
        var baseY = centerY + dirY * tipDistance;
        var arrowA = Project(baseX + perpX * halfWidth, baseY + perpY * halfWidth, z, state);
        var arrowB = Project(baseX - perpX * halfWidth, baseY - perpY * halfWidth, z, state);
        _renderer.AddFilledTriangle(tip, arrowA, arrowB, color, opacity);
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

    private static bool IsBlinkOn()
    {
        var phase = (DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond) / 350;
        return phase % 2 == 0;
    }

    private void DrawConveyorStrip(VoxelCoord position, VoxelSize size, int rotationZDegrees, Color color,
        PartRenderInfo renderInfo, double opacity, bool drawOutline, EditorState state)
    {
        if (_renderer is null)
        {
            return;
        }

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

        _renderer.AddFilledQuad(topA, topB, topC, topD, Darken(color, 0.92), opacity);
        _renderer.AddFilledQuad(topB, bottomB, bottomC, topC, Darken(color, 0.70), opacity * 0.7);
        _renderer.AddFilledQuad(topD, topC, bottomC, bottomD, Darken(color, 0.58), opacity * 0.7);

        DrawConveyorMarker(position, size, rotationZDegrees, renderInfo, opacity, state);

        if (drawOutline)
        {
            DrawOutline(position, new VoxelSize(size.WidthX, size.DepthY, 1), Color.FromRgb(230, 230, 230), Math.Min(opacity + 0.18, 1), state);
        }
    }

    private void DrawConveyorJoinCap(VoxelCoord position, Color color, double opacity, EditorState state)
    {
        if (_renderer is null)
        {
            return;
        }

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

        _renderer.AddFilledQuad(topA, topB, topC, topD, Darken(color, 0.96), opacity);
        _renderer.AddFilledQuad(topB, bottomB, bottomC, topC, Darken(color, 0.76), opacity * 0.8);
        _renderer.AddFilledQuad(topD, topC, bottomC, bottomD, Darken(color, 0.68), opacity * 0.8);

        DrawOutline(position, capSize, Color.FromRgb(230, 230, 230), Math.Min(opacity + 0.12, 1), state);
    }

    private void DrawConveyorSceneJoins(EditorState state)
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
            DrawConveyorJoinCap(joinCell, Color.FromRgb(78, 158, 216), opacity, state);
        }
    }

    private Point Project(double x, double y, double z, EditorState state)
    {
        return ViewportMath.Project(x, y, z, Bounds, state);
    }

    private void DrawConveyorRoutePreview(ConveyorRouteDraft route, EditorState state)
    {
        var (committedCells, previewCells) = ConveyorRouteCellVisualization.BuildRouteDraftCells(
            route.Anchors, route.PreviewEnd);

        var committedColor = Color.FromRgb(78, 158, 216);
        foreach (var cell in committedCells)
        {
            DrawConveyorCell(cell, committedColor, 0.55, state, isPreview: true, previewIsValid: true);
        }

        var previewColor = route.PreviewIsValid ? Color.FromRgb(70, 190, 90) : Color.FromRgb(230, 90, 90);
        foreach (var cell in previewCells)
        {
            DrawConveyorCell(cell, previewColor, 0.45, state, isPreview: true, previewIsValid: route.PreviewIsValid);
        }

        foreach (var anchor in route.Anchors)
        {
            DrawOutline(anchor, new VoxelSize(1, 1, 1), Color.FromRgb(245, 220, 80), 0.75, state);
        }
    }

    private void DrawFacingMarker(VoxelCoord position, VoxelSize effectiveSize,
        int rotationZDegrees, PartRenderInfo renderInfo, double opacity, EditorState state, Color? markerColorOverride = null)
    {
        if (_renderer is null)
        {
            return;
        }

        if (!renderInfo.ShowFacingMarker)
        {
            return;
        }

        if (string.Equals(renderInfo.PartId, "conveyor", StringComparison.OrdinalIgnoreCase))
        {
            DrawConveyorMarker(position, effectiveSize, rotationZDegrees, renderInfo, opacity, state);
            return;
        }

        var normalized = RotationHelper.NormalizeDegrees(rotationZDegrees);
        var (fdx, fdy) = normalized switch
        {
            0 => (1.0, 0.0),
            90 => (0.0, 1.0),
            180 => (-1.0, 0.0),
            _ => (0.0, -1.0)
        };
        if (renderInfo.FlowMarkerKind == FlowMarkerKind.Incoming)
        {
            fdx = -fdx;
            fdy = -fdy;
        }

        var z1 = position.Z + effectiveSize.HeightZ;
        var cx = position.X + effectiveSize.WidthX / 2.0;
        var cy = position.Y + effectiveSize.DepthY / 2.0;

        var halfFacing = (Math.Abs(fdx) > 0.5 ? effectiveSize.WidthX : effectiveSize.DepthY) / 2.0;
        var halfTransverse = (Math.Abs(fdx) > 0.5 ? effectiveSize.DepthY : effectiveSize.WidthX) / 2.0;

        var arrowLen = halfFacing * 0.55;
        var arrowBase = halfTransverse * 0.40;

        var tipX = cx + fdx * arrowLen;
        var tipY = cy + fdy * arrowLen;

        var px = -fdy;
        var py = fdx;
        var tailX = cx - fdx * arrowLen * 0.3;
        var tailY = cy - fdy * arrowLen * 0.3;

        var base1 = Project(tailX + px * arrowBase, tailY + py * arrowBase, z1, state);
        var base2 = Project(tailX - px * arrowBase, tailY - py * arrowBase, z1, state);
        var tip = Project(tipX, tipY, z1, state);

        var markerColor = markerColorOverride ?? renderInfo.FacingMarkerColor.ToAvaloniaColor();
        var markerOpacity = Math.Min(opacity + 0.25, 1.0);
        if (renderInfo.FlowMarkerKind == FlowMarkerKind.Incoming)
        {
            var wingDistance = arrowLen * 0.55;
            var wingSpread = arrowBase * 0.85;
            var wing1 = Project(tipX - fdx * wingDistance + px * wingSpread, tipY - fdy * wingDistance + py * wingSpread, z1, state);
            var wing2 = Project(tipX - fdx * wingDistance - px * wingSpread, tipY - fdy * wingDistance - py * wingSpread, z1, state);
            _renderer.AddLine(tip, wing1, markerColor, markerOpacity);
            _renderer.AddLine(tip, wing2, markerColor, markerOpacity);
            return;
        }

        _renderer.AddFilledTriangle(tip, base1, base2, markerColor, markerOpacity);
    }

    private void DrawMtrlSrcOutputPulse(
        SceneObject sourceObject,
        VoxelCoord position,
        VoxelSize size,
        int rotationZDegrees,
        PartRenderInfo renderInfo,
        double opacity,
        EditorState state)
    {
        if (_renderer is null
            || !state.Scene.ConveyorRouteFlow.TryGetSourceOutputPulse(sourceObject.Id, out var pulse))
        {
            return;
        }

        var intensity = Math.Clamp(pulse.NormalizedIntensity, 0f, 1f);
        if (intensity <= 0f)
        {
            return;
        }

        var material = MaterialCatalog.Resolve(pulse.MaterialId);
        var markerOpacity = Math.Min(opacity + 0.45 + intensity * 0.35, 1.0);
        DrawFacingMarker(position, size, rotationZDegrees, renderInfo, markerOpacity, state, material.TopColor);

        var outlineColor = TintColor(Color.FromRgb(255, 255, 255), material.TopColor, 0.30);
        DrawOutline(position, size, outlineColor, Math.Min(0.30 + intensity * 0.60, 1.0), state);

        var sourceCenterX = position.X + size.WidthX * 0.5;
        var sourceCenterY = position.Y + size.DepthY * 0.5;
        var sourceZ = position.Z + size.HeightZ + 0.06;
        var targetCenterX = pulse.TargetCell.X + 0.5;
        var targetCenterY = pulse.TargetCell.Y + 0.5;

        var dirX = targetCenterX - sourceCenterX;
        var dirY = targetCenterY - sourceCenterY;
        var dirLength = Math.Sqrt((dirX * dirX) + (dirY * dirY));
        if (dirLength < 0.001)
        {
            var normalized = RotationHelper.NormalizeDegrees(rotationZDegrees);
            (dirX, dirY) = normalized switch
            {
                0 => (1.0, 0.0),
                90 => (0.0, 1.0),
                180 => (-1.0, 0.0),
                _ => (0.0, -1.0)
            };
        }
        else
        {
            dirX /= dirLength;
            dirY /= dirLength;
        }

        var progress = 1.0 - intensity;
        var travel = 0.18 + progress * 0.82;
        var flashX = sourceCenterX + dirX * travel;
        var flashY = sourceCenterY + dirY * travel;
        var halfSize = 0.060 + intensity * 0.042;

        var top = Project(flashX, flashY - halfSize, sourceZ, state);
        var right = Project(flashX + halfSize, flashY, sourceZ, state);
        var bottom = Project(flashX, flashY + halfSize, sourceZ, state);
        var left = Project(flashX - halfSize, flashY, sourceZ, state);

        var flashOpacity = Math.Min(0.40 + intensity * 0.55, 1.0);
        _renderer.AddFilledTriangle(top, right, bottom, material.TopColor, flashOpacity);
        _renderer.AddFilledTriangle(top, bottom, left, material.TopColor, flashOpacity);
        _renderer.AddLine(top, right, Color.FromRgb(245, 245, 245), Math.Min(0.30 + intensity * 0.40, 1.0));
        _renderer.AddLine(right, bottom, Color.FromRgb(245, 245, 245), Math.Min(0.30 + intensity * 0.40, 1.0));
        _renderer.AddLine(bottom, left, Color.FromRgb(245, 245, 245), Math.Min(0.30 + intensity * 0.40, 1.0));
        _renderer.AddLine(left, top, Color.FromRgb(245, 245, 245), Math.Min(0.30 + intensity * 0.40, 1.0));
    }

    private void DrawConveyorMarker(VoxelCoord position, VoxelSize effectiveSize, int rotationZDegrees,
        PartRenderInfo renderInfo, double opacity, EditorState state)
    {
        if (_renderer is null)
        {
            return;
        }

        var normalized = RotationHelper.NormalizeDegrees(rotationZDegrees);
        var z = position.Z + Math.Min(0.32, effectiveSize.HeightZ);
        var markerColor = renderInfo.FacingMarkerColor.ToAvaloniaColor();

        if (normalized is 0 or 180)
        {
            var y = position.Y + effectiveSize.DepthY / 2.0;
            var start = Project(position.X + 0.12, y, z, state);
            var end = Project(position.X + effectiveSize.WidthX - 0.12, y, z, state);
            _renderer.AddLine(start, end, markerColor, Math.Min(opacity + 0.22, 1.0));
            return;
        }

        var x = position.X + effectiveSize.WidthX / 2.0;
        var startY = Project(x, position.Y + 0.12, z, state);
        var endY = Project(x, position.Y + effectiveSize.DepthY - 0.12, z, state);
        _renderer.AddLine(startY, endY, markerColor, Math.Min(opacity + 0.22, 1.0));
    }

    private void DrawViewCubeOverlay(DrawingContext context)
    {
        if (Bounds.Width < 160 || Bounds.Height < 140)
        {
            return;
        }

        var cubeSize = 92.0;
        var origin = new Point(Bounds.Width - cubeSize - 18, Bounds.Height - cubeSize - 18);
        var panelRect = new Rect(origin.X - 10, origin.Y - 10, cubeSize + 20, cubeSize + 20);
        var isPresentation = EditorState?.ViewportVisualMode == ViewportVisualMode.Presentation;
        var panelBrush = new SolidColorBrush(isPresentation
            ? Color.FromArgb(218, 246, 241, 232)
            : Color.FromArgb(218, 18, 24, 32));
        var borderPen = new Pen(new SolidColorBrush(isPresentation
            ? Color.FromRgb(177, 165, 145)
            : Color.FromRgb(78, 95, 116)), 1);
        context.DrawRectangle(panelBrush, borderPen, new RoundedRect(panelRect, 12));

        var center = new Point(origin.X + 44, origin.Y + 50);
        var top = new Point(center.X, center.Y - 33);
        var left = new Point(center.X - 34, center.Y + 10);
        var right = new Point(center.X + 34, center.Y + 10);
        var bottom = new Point(center.X, center.Y + 34);

        var upBrush = new SolidColorBrush(Color.FromRgb(105, 168, 229));
        var leftBrush = new SolidColorBrush(Color.FromRgb(236, 201, 91));
        var frontBrush = new SolidColorBrush(Color.FromRgb(112, 192, 102));
        var edgePen = new Pen(new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)), 1);

        context.DrawGeometry(leftBrush, edgePen, BuildPolygon(center, left, bottom, new Point(center.X - 2, center.Y + 20)));
        context.DrawGeometry(frontBrush, edgePen, BuildPolygon(center, right, bottom, new Point(center.X + 2, center.Y + 20)));
        context.DrawGeometry(upBrush, edgePen, BuildPolygon(center, left, top, right));

        var axisPenX = new Pen(new SolidColorBrush(Color.FromRgb(238, 99, 82)), 2);
        var axisPenY = new Pen(new SolidColorBrush(Color.FromRgb(112, 192, 102)), 2);
        var axisPenZ = new Pen(new SolidColorBrush(Color.FromRgb(105, 168, 229)), 2);
        context.DrawLine(axisPenX, new Point(origin.X + 44, origin.Y + 86), new Point(origin.X + 72, origin.Y + 86));
        context.DrawLine(axisPenY, new Point(origin.X + 44, origin.Y + 86), new Point(origin.X + 24, origin.Y + 72));
        context.DrawLine(axisPenZ, new Point(origin.X + 44, origin.Y + 86), new Point(origin.X + 44, origin.Y + 58));

        DrawOverlayLabel(context, "Up", new Point(origin.X + 35, origin.Y + 8), Brushes.White);
        DrawOverlayLabel(context, "Left", new Point(origin.X + 5, origin.Y + 54), Brushes.White);
        DrawOverlayLabel(context, "Front", new Point(origin.X + 50, origin.Y + 54), Brushes.White);
    }

    private static StreamGeometry BuildPolygon(params Point[] points)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0], isFilled: true);
            for (var i = 1; i < points.Length; i++)
            {
                ctx.LineTo(points[i]);
            }

            ctx.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private static void DrawOverlayLabel(DrawingContext context, string text, Point point, IBrush brush)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            10,
            brush);
        context.DrawText(formatted, point);
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

}
