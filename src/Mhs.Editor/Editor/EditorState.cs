using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;

namespace Mhs.Editor.Editor;

public sealed class EditorState : INotifyPropertyChanged
{
    private IEditorTool _activeTool;
    private SceneObject? _selectedObject;
    private VoxelCoord? _hoveredVoxel;
    private GhostPreview? _ghostPreview;
    private int _activeFloor;
    private int _activeLayer;

    public EditorState()
    {
        PartDefinitions =
        [
            new PartDefinition { Id = "hopper", DisplayName = "Hopper", Size = new VoxelSize(1, 1, 2), Color = Color.FromRgb(240, 200, 90) },
            new PartDefinition { Id = "bin", DisplayName = "Bin", Size = new VoxelSize(2, 2, 1), Color = Color.FromRgb(90, 150, 240) },
            new PartDefinition { Id = "conveyor", DisplayName = "Conveyor", Size = new VoxelSize(3, 1, 1), Color = Color.FromRgb(70, 80, 90) },
            new PartDefinition { Id = "chute", DisplayName = "Chute", Size = new VoxelSize(2, 1, 1), Color = Color.FromRgb(150, 150, 150) },
            new PartDefinition { Id = "tall_hopper", DisplayName = "Tall Hopper", Size = new VoxelSize(2, 2, 4), Color = Color.FromRgb(214, 132, 66) }
        ];

        _activeTool = new SelectTool();
        _activeFloor = 0;
        _activeLayer = 0;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Scene Scene { get; } = new();

    public IReadOnlyList<PartDefinition> PartDefinitions { get; }

    public IEditorTool ActiveTool
    {
        get => _activeTool;
        set => SetField(ref _activeTool, value);
    }

    public SceneObject? SelectedObject
    {
        get => _selectedObject;
        set => SetField(ref _selectedObject, value);
    }

    public VoxelCoord? HoveredVoxel
    {
        get => _hoveredVoxel;
        set => SetField(ref _hoveredVoxel, value);
    }

    public GhostPreview? GhostPreview
    {
        get => _ghostPreview;
        set => SetField(ref _ghostPreview, value);
    }

    public int ActiveFloor
    {
        get => _activeFloor;
        set
        {
            var clamped = Math.Clamp(value, 0, WorldVerticalSettings.FloorCount - 1);
            if (!SetField(ref _activeFloor, clamped))
            {
                return;
            }

            OnPropertyChanged(nameof(ActiveAbsoluteZ));
            OnActiveLayerContextChanged();
        }
    }

    public int ActiveLayer
    {
        get => _activeLayer;
        set
        {
            var clamped = Math.Clamp(value, 0, WorldVerticalSettings.LayersPerFloor - 1);
            if (!SetField(ref _activeLayer, clamped))
            {
                return;
            }

            OnPropertyChanged(nameof(ActiveAbsoluteZ));
            OnActiveLayerContextChanged();
        }
    }

    public int ActiveAbsoluteZ => WorldVerticalSettings.ToAbsoluteZ(ActiveFloor, ActiveLayer);

    public int ActiveFloorStartZ => ActiveFloor * WorldVerticalSettings.LayersPerFloor;

    public int ActiveFloorEndZ => ActiveFloorStartZ + WorldVerticalSettings.LayersPerFloor - 1;

    public bool IsWithinGrid(VoxelCoord position)
        => position.X >= WorldGridSettings.MinCoord
        && position.X <= WorldGridSettings.MaxCoord
        && position.Y >= WorldGridSettings.MinCoord
        && position.Y <= WorldGridSettings.MaxCoord
        && position.Z >= WorldVerticalSettings.MinZ
        && position.Z <= WorldVerticalSettings.MaxZ;

    public bool FitsWithinGrid(VoxelCoord position, VoxelSize size)
        => position.X >= WorldGridSettings.MinCoord
        && position.Y >= WorldGridSettings.MinCoord
        && position.Z >= WorldVerticalSettings.MinZ
        && position.X + size.WidthX - 1 <= WorldGridSettings.MaxCoord
        && position.Y + size.DepthY - 1 <= WorldGridSettings.MaxCoord
        && position.Z + size.HeightZ - 1 <= WorldVerticalSettings.MaxZ;

    public bool IsObjectWithinGrid(SceneObject sceneObject)
        => FitsWithinGrid(sceneObject.Position, sceneObject.Size);

    public bool CanPlaceAt(PartDefinition part, VoxelCoord position, Guid? excludeId = null)
    {
        if (!FitsWithinGrid(position, part.Size))
        {
            return false;
        }

        foreach (var existing in Scene.Objects)
        {
            if (excludeId.HasValue && existing.Id == excludeId.Value)
            {
                continue;
            }

            if (Intersects(position, part.Size, existing.Position, existing.Size))
            {
                return false;
            }
        }

        return true;
    }

    public bool IsObjectSelected(SceneObject sceneObject) => SelectedObject?.Id == sceneObject.Id;

    public bool IntersectsActiveLayer(SceneObject obj)
        => IntersectsLayer(obj, ActiveAbsoluteZ);

    public bool IntersectsActiveFloor(SceneObject obj)
        => IntersectsFloor(obj, ActiveFloorStartZ, ActiveFloorEndZ);

    public static bool IntersectsLayer(SceneObject obj, int activeZ)
        => obj.MinZ <= activeZ && activeZ <= obj.MaxZ;

    public static bool IntersectsFloor(SceneObject obj, int floorStartZ, int floorEndZ)
        => obj.MinZ <= floorEndZ && obj.MaxZ >= floorStartZ;

    private static bool Intersects(VoxelCoord aPos, VoxelSize aSize, VoxelCoord bPos, VoxelSize bSize)
    {
        static bool Overlaps(int aMin, int aSize, int bMin, int bSize)
        {
            var aMax = aMin + aSize;
            var bMax = bMin + bSize;
            return aMin < bMax && bMin < aMax;
        }

        return Overlaps(aPos.X, aSize.WidthX, bPos.X, bSize.WidthX)
            && Overlaps(aPos.Y, aSize.DepthY, bPos.Y, bSize.DepthY)
            && Overlaps(aPos.Z, aSize.HeightZ, bPos.Z, bSize.HeightZ);
    }

    private void OnActiveLayerContextChanged()
    {
        if (HoveredVoxel.HasValue)
        {
            var hovered = HoveredVoxel.Value;
            hovered = hovered with { Z = ActiveAbsoluteZ };
            HoveredVoxel = IsWithinGrid(hovered) ? hovered : null;
        }

        if (GhostPreview is { } ghost)
        {
            var position = ghost.Position with { Z = ActiveAbsoluteZ };
            GhostPreview = FitsWithinGrid(position, ghost.Part.Size)
                ? new GhostPreview
                {
                    Part = ghost.Part,
                    Position = position,
                    IsValid = CanPlaceAt(ghost.Part, position)
                }
                : null;
        }

        if (SelectedObject is { } selected && (!IntersectsActiveLayer(selected) || !IsObjectWithinGrid(selected)))
        {
            SelectedObject = null;
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
