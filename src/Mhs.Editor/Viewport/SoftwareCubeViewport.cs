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

    private const double TileWidth = 48;
    private const double TileHeight = 24;
    private const double HeightScale = 24;

    static SoftwareCubeViewport()
    {
        AffectsRender<SoftwareCubeViewport>(EditorStateProperty);
    }

    public SoftwareCubeViewport()
    {
        ClipToBounds = true;

        PointerMoved += OnPointerMoved;
        PointerPressed += OnPointerPressed;
        PointerReleased += OnPointerReleased;
        PointerExited += OnPointerExited;
    }

    public EditorState? EditorState
    {
        get => GetValue(EditorStateProperty);
        set => SetValue(EditorStateProperty, value);
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
            var color = ColorForPart(sceneObject.PartType);
            DrawIsoBox(context, sceneObject.Position, sceneObject.Size, color, opacity, drawOutline: false);
        }

        if (state.SelectedObject is { } selected && state.IntersectsActiveLayer(selected))
        {
            DrawSelectionOutline(context, selected);
        }

        if (state.GhostPreview is { } ghost)
        {
            if (state.FitsWithinGrid(ghost.Position, ghost.Part.Size))
            {
                var ghostColor = ghost.IsValid
                    ? ghost.Part.Color
                    : Color.FromRgb(230, 90, 90);
                DrawIsoBox(context, ghost.Position, ghost.Part.Size, ghostColor, 0.4, drawOutline: true);
            }
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
        RoutePointerEvent(e, isPressed: false, isReleased: false);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        RoutePointerEvent(e, isPressed: true, isReleased: false);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
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

            var bounds = GetObjectScreenBounds(sceneObject).Inflate(2);
            if (bounds.Contains(point))
            {
                return sceneObject;
            }
        }

        return null;
    }

    private Rect GetObjectScreenBounds(SceneObject sceneObject)
    {
        var x0 = sceneObject.Position.X;
        var x1 = sceneObject.Position.X + sceneObject.Size.WidthX;
        var y0 = sceneObject.Position.Y;
        var y1 = sceneObject.Position.Y + sceneObject.Size.DepthY;
        var z0 = sceneObject.Position.Z;
        var z1 = sceneObject.Position.Z + sceneObject.Size.HeightZ;

        var corners = new[]
        {
            Project(x0, y0, z0), Project(x1, y0, z0), Project(x1, y1, z0), Project(x0, y1, z0),
            Project(x0, y0, z1), Project(x1, y0, z1), Project(x1, y1, z1), Project(x0, y1, z1)
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

        var originX = Bounds.Center.X;
        var originY = Bounds.Top + Bounds.Height * 0.28;

        var dx = (point.X - originX) / (TileWidth / 2.0);
        var dy = (point.Y - originY + absoluteZ * HeightScale) / (TileHeight / 2.0);

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
        for (var floor = 0; floor < WorldVerticalSettings.FloorCount; floor++)
        {
            var z = floor * WorldVerticalSettings.LayersPerFloor;
            var isActive = floor == activeFloor;
            var pen = new Pen(
                new SolidColorBrush(isActive ? Color.FromArgb(200, 150, 190, 255) : Color.FromArgb(70, 130, 140, 160)),
                isActive ? 2 : 1);

            var a = Project(WorldGridSettings.MinCoord, WorldGridSettings.MinCoord, z);
            var b = Project(WorldGridSettings.MaxCoord, WorldGridSettings.MinCoord, z);
            var c = Project(WorldGridSettings.MaxCoord, WorldGridSettings.MaxCoord, z);
            var d = Project(WorldGridSettings.MinCoord, WorldGridSettings.MaxCoord, z);

            context.DrawLine(pen, a, b);
            context.DrawLine(pen, b, c);
            context.DrawLine(pen, c, d);
            context.DrawLine(pen, d, a);
        }
    }

    private void DrawGrid(DrawingContext context, int absoluteZ)
    {
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(125, 160, 190, 220)), 1.2);

        for (var x = WorldGridSettings.MinCoord; x <= WorldGridSettings.MaxCoord; x++)
        {
            var start = Project(x, WorldGridSettings.MinCoord, absoluteZ);
            var end = Project(x, WorldGridSettings.MaxCoord, absoluteZ);
            context.DrawLine(gridPen, start, end);
        }

        for (var y = WorldGridSettings.MinCoord; y <= WorldGridSettings.MaxCoord; y++)
        {
            var start = Project(WorldGridSettings.MinCoord, y, absoluteZ);
            var end = Project(WorldGridSettings.MaxCoord, y, absoluteZ);
            context.DrawLine(gridPen, start, end);
        }
    }

    private void DrawSelectionOutline(DrawingContext context, SceneObject sceneObject)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(120, 180, 255)), 2);

        var x0 = sceneObject.Position.X;
        var x1 = sceneObject.Position.X + sceneObject.Size.WidthX;
        var y0 = sceneObject.Position.Y;
        var y1 = sceneObject.Position.Y + sceneObject.Size.DepthY;
        var z0 = sceneObject.Position.Z;
        var z1 = sceneObject.Position.Z + sceneObject.Size.HeightZ;

        var topA = Project(x0, y0, z1);
        var topB = Project(x1, y0, z1);
        var topC = Project(x1, y1, z1);
        var topD = Project(x0, y1, z1);

        context.DrawLine(pen, topA, topB);
        context.DrawLine(pen, topB, topC);
        context.DrawLine(pen, topC, topD);
        context.DrawLine(pen, topD, topA);

        context.DrawLine(pen, topA, Project(x0, y0, z0));
        context.DrawLine(pen, topB, Project(x1, y0, z0));
        context.DrawLine(pen, topC, Project(x1, y1, z0));
        context.DrawLine(pen, topD, Project(x0, y1, z0));
    }

    private void DrawIsoBox(DrawingContext context, VoxelCoord position, VoxelSize size, Color color, double opacity, bool drawOutline)
    {
        var x0 = position.X;
        var x1 = position.X + size.WidthX;
        var y0 = position.Y;
        var y1 = position.Y + size.DepthY;
        var z0 = position.Z;
        var z1 = position.Z + size.HeightZ;

        var topA = Project(x0, y0, z1);
        var topB = Project(x1, y0, z1);
        var topC = Project(x1, y1, z1);
        var topD = Project(x0, y1, z1);

        var bottomA = Project(x0, y0, z0);
        var bottomB = Project(x1, y0, z0);
        var bottomC = Project(x1, y1, z0);
        var bottomD = Project(x0, y1, z0);

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

    private Point Project(double x, double y, double z)
    {
        var originX = Bounds.Center.X;
        var originY = Bounds.Top + Bounds.Height * 0.28;

        return new Point(
            originX + (x - y) * (TileWidth / 2.0),
            originY + (x + y) * (TileHeight / 2.0) - z * HeightScale);
    }

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
