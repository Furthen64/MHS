using System;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
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
                DrawConveyorStrip(drawPosition, drawSize, drawRotation, color, renderInfo, opacity, false, state);
            }
            else
            {
                DrawIsoBox(drawPosition, drawSize, color, opacity, drawOutline: false, state);
                DrawFacingMarker(drawPosition, drawSize, drawRotation, renderInfo, opacity, state);
            }
        }

        DrawConveyorSceneJoins(state);

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

            DrawIsoBox(position, size, Color.FromRgb(78, 158, 216), 0.35, drawOutline: true, state);
            DrawFacingMarker(position, size, segment.RotationZDegrees, renderInfo, 0.35, state);

            if (i < route.Anchors.Count - 1)
            {
                var nextStart = route.Anchors[i];
                var nextEnd = route.Anchors[i + 1];
                if (ConveyorRouteRendering.TryGetTurnJoinCell(start, end, nextStart, nextEnd, out var joinCell))
                {
                    DrawConveyorJoinCap(joinCell, Color.FromRgb(78, 158, 216), 0.42, state);
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
                DrawConveyorStrip(position, size, segment.RotationZDegrees, previewColor, renderInfo, 0.45, true, state);

                if (route.Anchors.Count > 1)
                {
                    var previousStart = route.Anchors[^2];
                    var previousEnd = route.Anchors[^1];
                    if (ConveyorRouteRendering.TryGetTurnJoinCell(previousStart, previousEnd, start, end, out var joinCell))
                    {
                        DrawConveyorJoinCap(joinCell, previewColor, 0.52, state);
                    }
                }
            }
        }

        foreach (var anchor in route.Anchors)
        {
            DrawIsoBox(anchor, new VoxelSize(1, 1, 1), Color.FromRgb(245, 220, 80), 0.75, drawOutline: true, state);
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
        _renderer.AddFilledTriangle(tip, base1, base2, markerColor, Math.Min(opacity + 0.25, 1.0));
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
            || (pointerProperties.IsRightButtonPressed && state?.IsSelectionRotationMode != true);

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

}
