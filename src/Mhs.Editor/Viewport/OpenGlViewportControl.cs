using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
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

        _renderer.BeginFrame(fb, Bounds.Size);

        if (!string.IsNullOrWhiteSpace(_initError))
        {
            _renderer.RenderFrame();
            return;
        }

        DrawFloorOutlines(state.ActiveFloor);
        DrawGrid(state.ActiveAbsoluteZ);

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
                DrawConveyorCells(sceneObject, drawPosition, drawSize, drawRotation, color, renderInfo, opacity, state, conveyorCellsByObject);
            }
            else
            {
                DrawIsoBox(drawPosition, drawSize, color, opacity, drawOutline: false, state);
                DrawFacingMarker(drawPosition, drawSize, drawRotation, renderInfo, opacity, state);
            }
        }

        if (state.IsMovingSelection && state.SelectedObject is { } moving && state.MovePreviewPosition is { } target)
        {
            var moveColor = state.MovePreviewIsValid
                ? PartRenderCatalog.Resolve(moving.PartType).BaseColor.ToAvaloniaColor()
                : Color.FromRgb(230, 90, 90);
            DrawIsoBox(target, moving.EffectiveSize, moveColor, 0.45, drawOutline: true, state);
            DrawFacingMarker(target, moving.EffectiveSize, moving.RotationZDegrees, PartRenderCatalog.Resolve(moving.PartType), 0.45, state);
        }

        if (state.GhostPreview is { } ghost)
        {
            var ghostColor = ghost.IsValid
                ? ghost.Part.Color
                : Color.FromRgb(230, 90, 90);
            DrawIsoBox(ghost.Position, ghost.EffectiveSize, ghostColor, 0.4, drawOutline: true, state);
            DrawFacingMarker(ghost.Position, ghost.EffectiveSize, ghost.RotationZDegrees, PartRenderCatalog.Resolve(ghost.Part.Id), 0.4, state);
        }

        if (state.ActiveConveyorRoute is { } route)
        {
            DrawConveyorRoutePreview(route, state);
        }

        if (state.HoveredObject is { } hovered
            && state.IntersectsActiveLayer(hovered)
            && state.SelectedObject?.Id != hovered.Id)
        {
            DrawOutline(hovered.Position, hovered.EffectiveSize, Color.FromRgb(215, 215, 130), 0.8, state);
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
            DrawOutline(outlinePosition, outlineSize, Color.FromRgb(120, 180, 255), 1.0, state);
        }

        DrawPortDebug(state);
        DrawMaterialTokens(state);
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
        if (EditorState?.Scene.MaterialFlow.GetTokens().Any(token => token.State == MaterialTokenState.Blocked) == true)
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
            var color = isActive ? Color.FromArgb(200, 150, 190, 255) : Color.FromArgb(70, 130, 140, 160);

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

        var color = Color.FromArgb(125, 160, 190, 220);

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
        if (_renderer is null)
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

        var showFullCellBounds = state.SelectedObject?.Id == sceneObject.Id
            || state.HoveredObject?.Id == sceneObject.Id;

        foreach (var cell in cells)
        {
            DrawConveyorCell(cell, color, opacity, showFullCellBounds, state);
        }
    }

    private void DrawConveyorCell(ConveyorVisualCell cell, Color color, double opacity, bool showFullCellBounds, EditorState state)
    {
        if (_renderer is null)
        {
            return;
        }

        var visualHeight = 0.28;
        var x0 = cell.Position.X;
        var x1 = cell.Position.X + 1;
        var y0 = cell.Position.Y;
        var y1 = cell.Position.Y + 1;
        var z0 = cell.Position.Z;
        var z1 = cell.Position.Z + visualHeight;

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

        DrawConveyorCellTopBoundary(topA, topB, topC, topD, opacity);
        if (showFullCellBounds)
        {
            DrawOutline(cell.Position, new VoxelSize(1, 1, 1), Color.FromRgb(226, 226, 226), Math.Min(opacity + 0.16, 1), state);
        }

        DrawConveyorCellFlow(cell, opacity, state);
    }

    private void DrawConveyorCellTopBoundary(Point topA, Point topB, Point topC, Point topD, double opacity)
    {
        if (_renderer is null)
        {
            return;
        }

        var boundaryColor = Color.FromRgb(226, 226, 226);
        var boundaryOpacity = Math.Min(opacity + 0.12, 1.0);
        _renderer.AddLine(topA, topB, boundaryColor, boundaryOpacity);
        _renderer.AddLine(topB, topC, boundaryColor, boundaryOpacity);
        _renderer.AddLine(topC, topD, boundaryColor, boundaryOpacity);
        _renderer.AddLine(topD, topA, boundaryColor, boundaryOpacity);
    }

    private void DrawConveyorCellFlow(ConveyorVisualCell cell, double opacity, EditorState state)
    {
        if (_renderer is null)
        {
            return;
        }

        var z = cell.Position.Z + 0.31;
        var centerX = cell.Position.X + 0.5;
        var centerY = cell.Position.Y + 0.5;
        var flowColor = cell.Kind == ConveyorVisualCellKind.Corner
            ? Color.FromRgb(255, 214, 96)
            : Color.FromRgb(255, 228, 112);
        var glyphOpacity = Math.Min(opacity + 0.26, 1.0);

        if (cell.Kind == ConveyorVisualCellKind.Corner && cell.EntryDirection.HasValue)
        {
            var toEntrySide = cell.EntryDirection.Value;
            var (entryDx, entryDy) = DirectionToPlanarVector(toEntrySide);
            var entryPoint = Project(centerX + entryDx * 0.34, centerY + entryDy * 0.34, z, state);

            var exitDirection = cell.ExitDirection ?? cell.MainFlowDirection;
            var (exitDx, exitDy) = DirectionToPlanarVector(exitDirection);
            var pivotX = centerX + (entryDx + exitDx) * 0.07;
            var pivotY = centerY + (entryDy + exitDy) * 0.07;
            var pivot = Project(pivotX, pivotY, z, state);
            _renderer.AddLine(entryPoint, pivot, flowColor, glyphOpacity);

            var cornerTip = Project(centerX + exitDx * 0.37, centerY + exitDy * 0.37, z, state);
            _renderer.AddLine(pivot, cornerTip, flowColor, glyphOpacity);

            var cornerPerpX = -exitDy;
            var cornerPerpY = exitDx;
            var cornerBaseX = centerX + exitDx * 0.18;
            var cornerBaseY = centerY + exitDy * 0.18;
            var cornerArrowA = Project(cornerBaseX + cornerPerpX * 0.14, cornerBaseY + cornerPerpY * 0.14, z, state);
            var cornerArrowB = Project(cornerBaseX - cornerPerpX * 0.14, cornerBaseY - cornerPerpY * 0.14, z, state);
            _renderer.AddFilledTriangle(cornerTip, cornerArrowA, cornerArrowB, flowColor, glyphOpacity);
            return;
        }

        var arrowDirection = cell.ExitDirection ?? cell.MainFlowDirection;
        var (arrowDx, arrowDy) = DirectionToPlanarVector(arrowDirection);
        var tail = Project(centerX - arrowDx * 0.16, centerY - arrowDy * 0.16, z, state);
        var tip = Project(centerX + arrowDx * 0.38, centerY + arrowDy * 0.38, z, state);
        _renderer.AddLine(tail, tip, flowColor, glyphOpacity);

        var perpX = -arrowDy;
        var perpY = arrowDx;
        var baseX = centerX + arrowDx * 0.18;
        var baseY = centerY + arrowDy * 0.18;
        var arrowA = Project(baseX + perpX * 0.14, baseY + perpY * 0.14, z, state);
        var arrowB = Project(baseX - perpX * 0.14, baseY - perpY * 0.14, z, state);
        _renderer.AddFilledTriangle(tip, arrowA, arrowB, flowColor, glyphOpacity);
    }

    private static (double X, double Y) DirectionToPlanarVector(PortDirection direction) => direction switch
    {
        PortDirection.PositiveX => (1, 0),
        PortDirection.NegativeX => (-1, 0),
        PortDirection.PositiveY => (0, 1),
        PortDirection.NegativeY => (0, -1),
        _ => (0, 0)
    };

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
            DrawConveyorCell(cell, committedColor, 0.55, showFullCellBounds: false, state);
        }

        var previewColor = route.PreviewIsValid ? Color.FromRgb(70, 190, 90) : Color.FromRgb(230, 90, 90);
        foreach (var cell in previewCells)
        {
            DrawConveyorCell(cell, previewColor, 0.45, showFullCellBounds: false, state);
        }

        foreach (var anchor in route.Anchors)
        {
            DrawOutline(anchor, new VoxelSize(1, 1, 1), Color.FromRgb(245, 220, 80), 0.75, state);
        }
    }

    private void DrawFacingMarker(VoxelCoord position, VoxelSize effectiveSize,
        int rotationZDegrees, PartRenderInfo renderInfo, double opacity, EditorState state)
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

        var markerColor = renderInfo.FacingMarkerColor.ToAvaloniaColor();
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
