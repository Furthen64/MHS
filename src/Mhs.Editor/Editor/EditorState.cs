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

    public EditorState()
    {
        PartDefinitions =
        [
            new PartDefinition { Id = "hopper", DisplayName = "Hopper", Size = new VoxelSize(1, 1, 1), Color = Color.FromRgb(240, 200, 90) },
            new PartDefinition { Id = "bin", DisplayName = "Bin", Size = new VoxelSize(2, 1, 2), Color = Color.FromRgb(90, 150, 240) },
            new PartDefinition { Id = "conveyor", DisplayName = "Conveyor", Size = new VoxelSize(3, 1, 1), Color = Color.FromRgb(70, 80, 90) },
            new PartDefinition { Id = "chute", DisplayName = "Chute", Size = new VoxelSize(2, 1, 1), Color = Color.FromRgb(150, 150, 150) }
        ];

        _activeTool = new SelectTool();
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

    public bool CanPlaceAt(PartDefinition part, VoxelCoord position, Guid? excludeId = null)
    {
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

    private static bool Intersects(VoxelCoord aPos, VoxelSize aSize, VoxelCoord bPos, VoxelSize bSize)
    {
        static bool Overlaps(int aMin, int aSize, int bMin, int bSize)
        {
            var aMax = aMin + aSize;
            var bMax = bMin + bSize;
            return aMin < bMax && bMin < aMax;
        }

        return Overlaps(aPos.X, aSize.Width, bPos.X, bSize.Width)
            && Overlaps(aPos.Y, aSize.Height, bPos.Y, bSize.Height)
            && Overlaps(aPos.Z, aSize.Depth, bPos.Z, bSize.Depth);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
