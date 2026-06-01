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
    private SceneObject? _hoveredObject;
    private VoxelCoord? _hoveredVoxel;
    private GhostPreview? _ghostPreview;
    private int _activeFloor;
    private int _activeLayer;
    private int _activePlacementRotationZDegrees;
    private bool _isMovingSelection;
    private VoxelCoord? _moveOriginalPosition;
    private VoxelCoord? _movePreviewPosition;
    private bool _movePreviewIsValid;
    private string? _movePreviewInvalidReason;
    private Guid? _rotationAxisObjectId;
    private double _rotationAxisPivotX;
    private double _rotationAxisPivotY;
    private int _rotationAxisMinZ;
    private int _rotationAxisMaxZ;
    private bool _isSelectionRotationMode;
    private int _selectionRotationPreviewDegrees;
    private VoxelCoord? _selectionRotationPreviewPosition;
    private bool _selectionRotationPreviewIsValid;
    private string? _selectionRotationPreviewInvalidReason;
    private double _viewportZoom = 1.0;
    private double _viewportPanX;
    private double _viewportPanY;
    private string _openGlBackendInfo = "N/A";
    private string _statusMessage = "Ready";
    private ConveyorRouteDraft? _activeConveyorRoute;

    public EditorState()
    {
        PartDefinitions =
        [
            new PartDefinition { Id = "hopper", DisplayName = "Hopper", Size = new VoxelSize(1, 1, 2), Color = Color.FromRgb(240, 200, 90) },
            new PartDefinition { Id = "bin", DisplayName = "Bin", Size = new VoxelSize(2, 2, 1), Color = Color.FromRgb(90, 150, 240) },
            new PartDefinition { Id = "conveyor", DisplayName = "Conveyor", Size = new VoxelSize(1, 1, 1), Color = Color.FromRgb(70, 80, 90) },
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
        set
        {
            if (!SetField(ref _selectedObject, value))
            {
                return;
            }

            if (value is null || _rotationAxisObjectId != value.Id)
            {
                ClearSelectionRotationMode();
                ClearRotationAxis();
            }
        }
    }

    public SceneObject? HoveredObject
    {
        get => _hoveredObject;
        set => SetField(ref _hoveredObject, value);
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

    public int ActivePlacementRotationZDegrees
    {
        get => _activePlacementRotationZDegrees;
        set => SetField(ref _activePlacementRotationZDegrees, RotationHelper.NormalizeDegrees(value));
    }

    public bool IsMovingSelection
    {
        get => _isMovingSelection;
        set => SetField(ref _isMovingSelection, value);
    }

    public VoxelCoord? MoveOriginalPosition
    {
        get => _moveOriginalPosition;
        set => SetField(ref _moveOriginalPosition, value);
    }

    public VoxelCoord? MovePreviewPosition
    {
        get => _movePreviewPosition;
        set => SetField(ref _movePreviewPosition, value);
    }

    public bool MovePreviewIsValid
    {
        get => _movePreviewIsValid;
        set => SetField(ref _movePreviewIsValid, value);
    }

    public string? MovePreviewInvalidReason
    {
        get => _movePreviewInvalidReason;
        set => SetField(ref _movePreviewInvalidReason, value);
    }

    public Guid? RotationAxisObjectId => _rotationAxisObjectId;
    public double RotationAxisPivotX => _rotationAxisPivotX;
    public double RotationAxisPivotY => _rotationAxisPivotY;
    public int RotationAxisMinZ => _rotationAxisMinZ;
    public int RotationAxisMaxZ => _rotationAxisMaxZ;
    public bool IsSelectionRotationMode
    {
        get => _isSelectionRotationMode;
        set => SetField(ref _isSelectionRotationMode, value);
    }

    public int SelectionRotationPreviewDegrees
    {
        get => _selectionRotationPreviewDegrees;
        set => SetField(ref _selectionRotationPreviewDegrees, RotationHelper.NormalizeDegrees(value));
    }

    public VoxelCoord? SelectionRotationPreviewPosition
    {
        get => _selectionRotationPreviewPosition;
        set => SetField(ref _selectionRotationPreviewPosition, value);
    }

    public bool SelectionRotationPreviewIsValid
    {
        get => _selectionRotationPreviewIsValid;
        set => SetField(ref _selectionRotationPreviewIsValid, value);
    }

    public string? SelectionRotationPreviewInvalidReason
    {
        get => _selectionRotationPreviewInvalidReason;
        set => SetField(ref _selectionRotationPreviewInvalidReason, value);
    }

    public double ViewportZoom
    {
        get => _viewportZoom;
        set => SetField(ref _viewportZoom, value);
    }

    public double ViewportPanX
    {
        get => _viewportPanX;
        set => SetField(ref _viewportPanX, value);
    }

    public double ViewportPanY
    {
        get => _viewportPanY;
        set => SetField(ref _viewportPanY, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, string.IsNullOrWhiteSpace(value) ? "Ready" : value);
    }

    public string OpenGlBackendInfo
    {
        get => _openGlBackendInfo;
        set => SetField(ref _openGlBackendInfo, string.IsNullOrWhiteSpace(value) ? "N/A" : value);
    }

    public ConveyorRouteDraft? ActiveConveyorRoute
    {
        get => _activeConveyorRoute;
        set => SetField(ref _activeConveyorRoute, value);
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

    public bool FitsWithinActiveFloor(VoxelCoord position, VoxelSize size)
        => FitsWithinGrid(position, size)
        && position.Z >= ActiveFloorStartZ
        && position.Z + size.HeightZ - 1 <= ActiveFloorEndZ;

    public bool IsObjectWithinGrid(SceneObject sceneObject)
        => FitsWithinGrid(sceneObject.Position, sceneObject.EffectiveSize);

    public bool HasRotationAxisFor(Guid objectId)
        => _rotationAxisObjectId == objectId;

    public void SetRotationAxis(SceneObject sceneObject)
    {
        _rotationAxisObjectId = sceneObject.Id;
        _rotationAxisPivotX = sceneObject.Position.X + sceneObject.EffectiveSize.WidthX / 2.0;
        _rotationAxisPivotY = sceneObject.Position.Y + sceneObject.EffectiveSize.DepthY / 2.0;
        _rotationAxisMinZ = WorldVerticalSettings.MinZ;
        _rotationAxisMaxZ = WorldVerticalSettings.MaxZ + 1;
        OnPropertyChanged(nameof(RotationAxisObjectId));
        OnPropertyChanged(nameof(RotationAxisPivotX));
        OnPropertyChanged(nameof(RotationAxisPivotY));
        OnPropertyChanged(nameof(RotationAxisMinZ));
        OnPropertyChanged(nameof(RotationAxisMaxZ));
    }

    public void ClearRotationAxis()
    {
        ClearSelectionRotationMode();

        if (_rotationAxisObjectId is null)
        {
            return;
        }

        _rotationAxisObjectId = null;
        OnPropertyChanged(nameof(RotationAxisObjectId));
    }

    public void StartSelectionRotation(SceneObject selected)
    {
        SetRotationAxis(selected);
        IsSelectionRotationMode = true;
        SelectionRotationPreviewDegrees = selected.RotationZDegrees;
        SelectionRotationPreviewPosition = selected.Position;
        SelectionRotationPreviewIsValid = true;
        SelectionRotationPreviewInvalidReason = null;
    }

    public void SetSelectionRotationPreview(int rotationZDegrees, VoxelCoord position, bool isValid, string? reason)
    {
        SelectionRotationPreviewDegrees = rotationZDegrees;
        SelectionRotationPreviewPosition = position;
        SelectionRotationPreviewIsValid = isValid;
        SelectionRotationPreviewInvalidReason = reason;
    }

    public void ClearSelectionRotationMode()
    {
        IsSelectionRotationMode = false;
        SelectionRotationPreviewDegrees = 0;
        SelectionRotationPreviewPosition = null;
        SelectionRotationPreviewIsValid = false;
        SelectionRotationPreviewInvalidReason = null;
    }

    public PlacementValidationResult ValidatePlacement(VoxelCoord position, VoxelSize size, Guid? excludeId = null)
    {
        var exceedsVerticalBounds = position.Z < WorldVerticalSettings.MinZ
            || position.Z + size.HeightZ - 1 > WorldVerticalSettings.MaxZ;
        if (exceedsVerticalBounds)
        {
            return PlacementValidationResult.Invalid("out of vertical bounds");
        }

        if (!FitsWithinGrid(position, size))
        {
            return PlacementValidationResult.Invalid("out of grid bounds");
        }

        foreach (var existing in Scene.Objects)
        {
            if (excludeId.HasValue && existing.Id == excludeId.Value)
            {
                continue;
            }

            if (Intersects(position, size, existing.Position, existing.EffectiveSize))
            {
                return PlacementValidationResult.Invalid("collision");
            }
        }

        return PlacementValidationResult.Valid;
    }

    public PlacementValidationResult ValidatePartPlacement(PartDefinition part, VoxelCoord position, int rotationZDegrees, Guid? excludeId = null)
    {
        var effectiveSize = RotationHelper.GetEffectiveSize(part.Size, rotationZDegrees);
        return ValidatePlacement(position, effectiveSize, excludeId);
    }

    public bool CanPlaceAt(PartDefinition part, VoxelCoord position, int rotationZDegrees, Guid? excludeId = null)
        => ValidatePartPlacement(part, position, rotationZDegrees, excludeId).IsValid;

    public bool IsObjectSelected(SceneObject sceneObject) => SelectedObject?.Id == sceneObject.Id;

    public PortConnectivitySnapshot GetPortConnectivitySnapshot()
        => Scene.GetPortConnectivitySnapshot();

    public bool IntersectsActiveLayer(SceneObject obj)
        => IntersectsLayer(obj, ActiveAbsoluteZ);

    public bool IntersectsActiveFloor(SceneObject obj)
        => IntersectsFloor(obj, ActiveFloorStartZ, ActiveFloorEndZ);

    public static bool IntersectsLayer(SceneObject obj, int activeZ)
        => obj.MinZ <= activeZ && activeZ <= obj.MaxZ;

    public static bool IntersectsFloor(SceneObject obj, int floorStartZ, int floorEndZ)
        => obj.MinZ <= floorEndZ && obj.MaxZ >= floorStartZ;

    public static bool Intersects(VoxelCoord aPos, VoxelSize aSize, VoxelCoord bPos, VoxelSize bSize)
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

    public void SetMovePreview(VoxelCoord? position, bool isValid, string? reason)
    {
        MovePreviewPosition = position;
        MovePreviewIsValid = isValid;
        MovePreviewInvalidReason = reason;
    }

    public void ClearMoveState()
    {
        IsMovingSelection = false;
        MoveOriginalPosition = null;
        MovePreviewPosition = null;
        MovePreviewInvalidReason = null;
        MovePreviewIsValid = false;
    }

    private void OnActiveLayerContextChanged()
    {
        if (ActiveConveyorRoute is not null)
        {
            ActiveConveyorRoute = null;
            StatusMessage = "Route canceled: active floor/layer changed";
        }

        if (HoveredVoxel.HasValue)
        {
            var hovered = HoveredVoxel.Value;
            hovered = hovered with { Z = ActiveAbsoluteZ };
            HoveredVoxel = IsWithinGrid(hovered) ? hovered : null;
        }

        if (GhostPreview is { } ghost)
        {
            var position = ghost.Position with { Z = ActiveAbsoluteZ };
            var validation = ValidatePartPlacement(ghost.Part, position, ghost.RotationZDegrees);
            GhostPreview = FitsWithinActiveFloor(position, ghost.EffectiveSize)
                ? new GhostPreview
                {
                    Part = ghost.Part,
                    Position = position,
                    RotationZDegrees = ghost.RotationZDegrees,
                    IsValid = validation.IsValid,
                    InvalidReason = validation.Reason
                }
                : null;
        }

        if (SelectedObject is { } selected && (!IntersectsActiveLayer(selected) || !IsObjectWithinGrid(selected)))
        {
            SelectedObject = null;
        }

        if (HoveredObject is { } hoveredObject && (!IntersectsActiveLayer(hoveredObject) || !IsObjectWithinGrid(hoveredObject)))
        {
            HoveredObject = null;
        }

        if (IsMovingSelection && SelectedObject is not null)
        {
            MovePreviewPosition = MovePreviewPosition is { } movePosition
                ? movePosition with { Z = ActiveAbsoluteZ }
                : null;
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
