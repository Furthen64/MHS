using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Input;
using Mhs.Editor.Editor;

namespace Mhs.Editor.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IEditorTool _selectTool = new SelectTool();
    private ViewportInteractionPreset _interactionPreset = ViewportInteractionPreset.BlenderLike;

    public MainWindowViewModel()
    {
        EditorState = new EditorState();
        EditorState.PropertyChanged += OnEditorStatePropertyChanged;
        EditorState.Scene.Objects.CollectionChanged += OnSceneObjectsChanged;

        SelectToolCommand = new RelayCommand(() => SetTool(_selectTool));
        Floor0Command = new RelayCommand(() => SetActiveFloor(0));
        Floor1Command = new RelayCommand(() => SetActiveFloor(1));
        Floor2Command = new RelayCommand(() => SetActiveFloor(2));
        Layer0Command = new RelayCommand(() => SetActiveLayer(0));
        Layer1Command = new RelayCommand(() => SetActiveLayer(1));
        Layer2Command = new RelayCommand(() => SetActiveLayer(2));
        BlenderLikeSettingsCommand = new RelayCommand(() => SetInteractionPreset(ViewportInteractionPreset.BlenderLike));
        AutoCadLikeSettingsCommand = new RelayCommand(() => SetInteractionPreset(ViewportInteractionPreset.AutoCadLike));

        foreach (var part in EditorState.PartDefinitions)
        {
            switch (part.DisplayName)
            {
                case "Hopper":
                    HopperToolCommand = new RelayCommand(() => SetTool(new PlacePartTool(part)));
                    break;
                case "Bin":
                    BinToolCommand = new RelayCommand(() => SetTool(new PlacePartTool(part)));
                    break;
                case "Conveyor":
                    ConveyorToolCommand = new RelayCommand(() => SetTool(new PlacePartTool(part)));
                    break;
                case "Chute":
                    ChuteToolCommand = new RelayCommand(() => SetTool(new PlacePartTool(part)));
                    break;
                case "Tall Hopper":
                    TallHopperToolCommand = new RelayCommand(() => SetTool(new PlacePartTool(part)));
                    break;
            }
        }

        HopperToolCommand ??= new RelayCommand(() => { });
        BinToolCommand ??= new RelayCommand(() => { });
        ConveyorToolCommand ??= new RelayCommand(() => { });
        ChuteToolCommand ??= new RelayCommand(() => { });
        TallHopperToolCommand ??= new RelayCommand(() => { });

        SetTool(_selectTool);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public EditorState EditorState { get; }

    public ICommand SelectToolCommand { get; }
    public ICommand HopperToolCommand { get; }
    public ICommand BinToolCommand { get; }
    public ICommand ConveyorToolCommand { get; }
    public ICommand ChuteToolCommand { get; }
    public ICommand TallHopperToolCommand { get; }
    public ICommand Floor0Command { get; }
    public ICommand Floor1Command { get; }
    public ICommand Floor2Command { get; }
    public ICommand Layer0Command { get; }
    public ICommand Layer1Command { get; }
    public ICommand Layer2Command { get; }
    public ICommand BlenderLikeSettingsCommand { get; }
    public ICommand AutoCadLikeSettingsCommand { get; }

    public bool IsSelectActive => EditorState.ActiveTool is SelectTool;
    public bool IsHopperActive => EditorState.ActiveTool.Name == "Hopper";
    public bool IsBinActive => EditorState.ActiveTool.Name == "Bin";
    public bool IsConveyorActive => EditorState.ActiveTool.Name == "Conveyor";
    public bool IsChuteActive => EditorState.ActiveTool.Name == "Chute";
    public bool IsTallHopperActive => EditorState.ActiveTool.Name == "Tall Hopper";
    public bool IsFloor0Active => EditorState.ActiveFloor == 0;
    public bool IsFloor1Active => EditorState.ActiveFloor == 1;
    public bool IsFloor2Active => EditorState.ActiveFloor == 2;
    public bool IsLayer0Active => EditorState.ActiveLayer == 0;
    public bool IsLayer1Active => EditorState.ActiveLayer == 1;
    public bool IsLayer2Active => EditorState.ActiveLayer == 2;
    public bool IsBlenderLikeSettingsActive => _interactionPreset == ViewportInteractionPreset.BlenderLike;
    public bool IsAutoCadLikeSettingsActive => _interactionPreset == ViewportInteractionPreset.AutoCadLike;
    public ViewportInteractionPreset InteractionPreset => _interactionPreset;
    public string LayerDisplayText => $"Layer: {EditorState.ActiveLayer} of {WorldVerticalSettings.LayersPerFloor - 1}";
    public string AbsoluteZDisplayText => $"Absolute Z: {EditorState.ActiveAbsoluteZ}";

    public string StatusText
    {
        get
        {
            var hotkeys = _interactionPreset == ViewportInteractionPreset.BlenderLike
                ? "G=Move  R=Rotate  Del=Delete  Esc=Cancel"
                : "M=Move  R=Rotate  Del=Delete  Esc=Cancel";

            if (EditorState.IsMovingSelection)
            {
                var moveTarget = EditorState.MovePreviewPosition is { } target
                    ? $"Target X {target.X} Y {target.Y} Z {target.Z}"
                    : "Target -";
                var validity = EditorState.MovePreviewPosition.HasValue
                    ? (EditorState.MovePreviewIsValid ? "Valid" : "Blocked")
                    : "Preview";
                return $"Move | Selected: {EditorState.SelectedObject?.PartType ?? "None"} | {moveTarget} | {validity} | Objects: {EditorState.Scene.Objects.Count} | {hotkeys}";
            }

            var rotation = EditorState.ActiveTool is PlacePartTool
                ? $" | Rot {EditorState.ActivePlacementRotationZDegrees}°"
                : string.Empty;

            return $"{EditorState.StatusMessage} | Tool: {EditorState.ActiveTool.Name}{rotation} | Floor {EditorState.ActiveFloor} · Layer {EditorState.ActiveLayer}/{WorldVerticalSettings.LayersPerFloor - 1} · Z {EditorState.ActiveAbsoluteZ} | X {(EditorState.HoveredVoxel?.X.ToString() ?? "-")} Y {(EditorState.HoveredVoxel?.Y.ToString() ?? "-")} | Objects: {EditorState.Scene.Objects.Count} | {hotkeys}";
        }
    }

    public string InspectorSelectedText =>
        EditorState.SelectedObject is null
            ? "Selected: None"
            : $"Selected: {EditorState.SelectedObject.PartType}";

    public string InspectorIdText =>
        EditorState.SelectedObject is null
            ? "ID: -"
            : $"ID: {EditorState.SelectedObject.Id}";

    public string InspectorPositionText =>
        EditorState.SelectedObject is null
            ? $"Hovered Voxel: {(EditorState.HoveredVoxel is { } hovered ? $"X {hovered.X}, Y {hovered.Y}, Z {hovered.Z}" : "-")}"
            : $"Position: X {EditorState.SelectedObject.Position.X}, Y {EditorState.SelectedObject.Position.Y}, Z {EditorState.SelectedObject.Position.Z}";

    public string InspectorSizeText =>
        EditorState.SelectedObject is null
            ? $"Active Tool: {EditorState.ActiveTool.Name}"
            : $"Base Size: {EditorState.SelectedObject.BaseSize}";

    public string InspectorRotationText =>
        EditorState.SelectedObject is null
            ? $"Active Floor: {EditorState.ActiveFloor}"
            : $"Effective Size: {EditorState.SelectedObject.EffectiveSize}";

    public string InspectorStatusText =>
        EditorState.SelectedObject is null
            ? $"Active Layer: {EditorState.ActiveLayer}"
            : $"Rotation Z: {EditorState.SelectedObject.RotationZDegrees}°";

    public string InspectorContextText =>
        EditorState.SelectedObject is null
            ? $"Absolute Z: {EditorState.ActiveAbsoluteZ}"
            : $"Occupies Z: {EditorState.SelectedObject.MinZ}..{EditorState.SelectedObject.MaxZ}";

    public string InspectorRangeText =>
        EditorState.SelectedObject is null
            ? MovePreviewText()
            : OccupiesFloorsText();

    public string InspectorExtraText =>
        EditorState.SelectedObject is null
            ? PreviewText()
            : "Status: Placed";

    public string Floor2StackText => FloorStackText(2);
    public string Floor1StackText => FloorStackText(1);
    public string Floor0StackText => FloorStackText(0);
    public string ActiveFloorSummaryText => $"Active Floor: {EditorState.ActiveFloor}";
    public string ActiveLayerSummaryText => $"Layer {EditorState.ActiveLayer} of {WorldVerticalSettings.LayersPerFloor - 1}";
    public string ActiveAbsoluteZSummaryText => $"Absolute Z: {EditorState.ActiveAbsoluteZ}";

    public void HandleKeyDown(Key key)
    {
        switch (key)
        {
            case Key.R:
                RotateAction();
                break;
            case Key.Delete:
            case Key.Back:
                DeleteSelection();
                break;
            case Key.M:
                StartMoveSelection();
                break;
            case Key.G when _interactionPreset == ViewportInteractionPreset.BlenderLike:
                StartMoveSelection();
                break;
            case Key.Escape:
                CancelAction();
                break;
        }

        RaiseComputed();
    }

    private string FloorStackText(int floor)
    {
        var startZ = floor * WorldVerticalSettings.LayersPerFloor;
        var endZ = startZ + WorldVerticalSettings.LayersPerFloor - 1;
        var marker = EditorState.ActiveFloor == floor ? "▶" : " ";
        return $"{marker} Floor {floor}  Z {startZ}–{endZ}";
    }

    private string OccupiesFloorsText()
    {
        if (EditorState.SelectedObject is null)
        {
            return "Occupies Floors: -";
        }

        var minFloor = WorldVerticalSettings.ToFloor(EditorState.SelectedObject.MinZ);
        var maxFloor = WorldVerticalSettings.ToFloor(EditorState.SelectedObject.MaxZ);
        return minFloor == maxFloor
            ? $"Occupies Floors: {minFloor}"
            : $"Occupies Floors: {minFloor}..{maxFloor}";
    }

    private string PreviewText() =>
        EditorState.GhostPreview is null
            ? "Preview: None"
            : $"Preview: {EditorState.GhostPreview.Part.DisplayName} @ {EditorState.GhostPreview.Position} ({EditorState.GhostPreview.EffectiveSize})";

    private string MovePreviewText()
    {
        if (!EditorState.IsMovingSelection)
        {
            return "Move Preview: None";
        }

        if (!EditorState.MovePreviewPosition.HasValue)
        {
            return "Move Preview: Target -";
        }

        var target = EditorState.MovePreviewPosition.Value;
        var validity = EditorState.MovePreviewIsValid ? "Yes" : $"No ({EditorState.MovePreviewInvalidReason ?? "invalid"})";
        return $"Move Preview: Target X {target.X}, Y {target.Y}, Z {target.Z} | Valid: {validity}";
    }

    private void SetTool(IEditorTool tool)
    {
        EditorState.ActiveTool.OnCancel(EditorState);
        EditorState.ActiveTool = tool;
        EditorState.ClearMoveState();
        if (tool is SelectTool)
        {
            EditorState.ActivePlacementRotationZDegrees = 0;
        }

        EditorState.StatusMessage = "Ready";
        RaiseComputed();
    }

    private void SetActiveFloor(int floor)
    {
        EditorState.ActiveFloor = floor;
        RaiseComputed();
    }

    private void SetActiveLayer(int layer)
    {
        EditorState.ActiveLayer = layer;
        RaiseComputed();
    }

    private void SetInteractionPreset(ViewportInteractionPreset preset)
    {
        if (_interactionPreset == preset)
        {
            return;
        }

        _interactionPreset = preset;
        RaiseComputed();
    }

    private void RotateAction()
    {
        if (EditorState.ActiveTool is PlacePartTool placeTool)
        {
            EditorState.ActivePlacementRotationZDegrees = RotationHelper.RotateClockwise90(EditorState.ActivePlacementRotationZDegrees);
            placeTool.RefreshPreview(EditorState);
            EditorState.StatusMessage = "Ready";
            return;
        }

        if (EditorState.ActiveTool is not SelectTool || EditorState.SelectedObject is null || EditorState.IsMovingSelection)
        {
            return;
        }

        var selected = EditorState.SelectedObject;
        var rotated = RotationHelper.RotateClockwise90(selected.RotationZDegrees);
        var size = RotationHelper.GetEffectiveSize(selected.BaseSize, rotated);
        var validation = EditorState.ValidatePlacement(selected.Position, size, selected.Id);
        if (!validation.IsValid)
        {
            EditorState.StatusMessage = $"Blocked | Rotation blocked: {validation.Reason ?? "invalid"}";
            return;
        }

        selected.RotationZDegrees = rotated;
        EditorState.StatusMessage = "Ready";
    }

    private void DeleteSelection()
    {
        if (EditorState.SelectedObject is null)
        {
            return;
        }

        var selectedId = EditorState.SelectedObject.Id;
        EditorState.Scene.Objects.Remove(EditorState.SelectedObject);
        if (EditorState.HoveredObject?.Id == selectedId)
        {
            EditorState.HoveredObject = null;
        }

        EditorState.SelectedObject = null;
        EditorState.ClearMoveState();
        EditorState.StatusMessage = "Ready";
    }

    private void StartMoveSelection()
    {
        if (EditorState.ActiveTool is not SelectTool || EditorState.SelectedObject is null || EditorState.IsMovingSelection)
        {
            return;
        }

        var selected = EditorState.SelectedObject;
        EditorState.IsMovingSelection = true;
        EditorState.MoveOriginalPosition = selected.Position;

        if (EditorState.HoveredVoxel is { } hovered)
        {
            var target = hovered with { Z = EditorState.ActiveAbsoluteZ };
            var validation = EditorState.ValidatePlacement(target, selected.EffectiveSize, selected.Id);
            EditorState.SetMovePreview(target, validation.IsValid, validation.Reason);
        }
        else
        {
            EditorState.SetMovePreview(null, false, "No hovered voxel");
        }

        EditorState.StatusMessage = "Move";
    }

    private void CancelAction()
    {
        if (EditorState.IsMovingSelection)
        {
            if (EditorState.SelectedObject is { } selected && EditorState.MoveOriginalPosition.HasValue)
            {
                selected.Position = EditorState.MoveOriginalPosition.Value;
            }

            EditorState.ClearMoveState();
            EditorState.StatusMessage = "Ready";
            return;
        }

        if (EditorState.ActiveTool is PlacePartTool)
        {
            SetTool(_selectTool);
            EditorState.GhostPreview = null;
            return;
        }

        if (EditorState.ActiveTool is SelectTool && EditorState.SelectedObject is not null)
        {
            EditorState.SelectedObject = null;
            EditorState.StatusMessage = "Ready";
        }
    }

    private void OnEditorStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RaiseComputed();
    }

    private void OnSceneObjectsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaiseComputed();
    }

    private void RaiseComputed()
    {
        OnPropertyChanged(nameof(IsSelectActive));
        OnPropertyChanged(nameof(IsHopperActive));
        OnPropertyChanged(nameof(IsBinActive));
        OnPropertyChanged(nameof(IsConveyorActive));
        OnPropertyChanged(nameof(IsChuteActive));
        OnPropertyChanged(nameof(IsTallHopperActive));
        OnPropertyChanged(nameof(IsFloor0Active));
        OnPropertyChanged(nameof(IsFloor1Active));
        OnPropertyChanged(nameof(IsFloor2Active));
        OnPropertyChanged(nameof(IsLayer0Active));
        OnPropertyChanged(nameof(IsLayer1Active));
        OnPropertyChanged(nameof(IsLayer2Active));
        OnPropertyChanged(nameof(IsBlenderLikeSettingsActive));
        OnPropertyChanged(nameof(IsAutoCadLikeSettingsActive));
        OnPropertyChanged(nameof(InteractionPreset));
        OnPropertyChanged(nameof(LayerDisplayText));
        OnPropertyChanged(nameof(AbsoluteZDisplayText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(InspectorSelectedText));
        OnPropertyChanged(nameof(InspectorIdText));
        OnPropertyChanged(nameof(InspectorPositionText));
        OnPropertyChanged(nameof(InspectorSizeText));
        OnPropertyChanged(nameof(InspectorRotationText));
        OnPropertyChanged(nameof(InspectorStatusText));
        OnPropertyChanged(nameof(InspectorContextText));
        OnPropertyChanged(nameof(InspectorRangeText));
        OnPropertyChanged(nameof(InspectorExtraText));
        OnPropertyChanged(nameof(Floor2StackText));
        OnPropertyChanged(nameof(Floor1StackText));
        OnPropertyChanged(nameof(Floor0StackText));
        OnPropertyChanged(nameof(ActiveFloorSummaryText));
        OnPropertyChanged(nameof(ActiveLayerSummaryText));
        OnPropertyChanged(nameof(ActiveAbsoluteZSummaryText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;

        public RelayCommand(Action execute)
        {
            _execute = execute;
        }

        public bool CanExecute(object? parameter) => true;

        public event EventHandler? CanExecuteChanged;

        public void Execute(object? parameter)
        {
            _execute();
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
