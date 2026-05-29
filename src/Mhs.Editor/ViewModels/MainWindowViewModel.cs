using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Mhs.Editor.Editor;

namespace Mhs.Editor.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IEditorTool _selectTool = new SelectTool();

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

    public string StatusText =>
        $"Ready | Tool: {EditorState.ActiveTool.Name} | Floor {EditorState.ActiveFloor} · Layer {EditorState.ActiveLayer}/2 · Z {EditorState.ActiveAbsoluteZ} | X {(EditorState.HoveredVoxel?.X.ToString() ?? "-")} Y {(EditorState.HoveredVoxel?.Y.ToString() ?? "-")} | Objects: {EditorState.Scene.Objects.Count}";

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
            : $"Size: {EditorState.SelectedObject.Size}";

    public string InspectorRotationText =>
        EditorState.SelectedObject is null
            ? $"Active Floor: {EditorState.ActiveFloor}"
            : $"Rotation Z: {EditorState.SelectedObject.RotationDegrees:0}";

    public string InspectorStatusText =>
        EditorState.SelectedObject is null
            ? $"Active Layer: {EditorState.ActiveLayer}"
            : $"Occupies Z: {EditorState.SelectedObject.MinZ}..{EditorState.SelectedObject.MaxZ}";

    public string InspectorContextText =>
        EditorState.SelectedObject is null
            ? $"Absolute Z: {EditorState.ActiveAbsoluteZ}"
            : OccupiesFloorsText();

    public string InspectorRangeText =>
        EditorState.SelectedObject is null
            ? PreviewText()
            : "Status: Placed";

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
            : $"Preview: {EditorState.GhostPreview.Part.DisplayName} @ {EditorState.GhostPreview.Position}";

    private void SetTool(IEditorTool tool)
    {
        EditorState.ActiveTool.OnCancel(EditorState);
        EditorState.ActiveTool = tool;
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
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(InspectorSelectedText));
        OnPropertyChanged(nameof(InspectorIdText));
        OnPropertyChanged(nameof(InspectorPositionText));
        OnPropertyChanged(nameof(InspectorSizeText));
        OnPropertyChanged(nameof(InspectorRotationText));
        OnPropertyChanged(nameof(InspectorStatusText));
        OnPropertyChanged(nameof(InspectorContextText));
        OnPropertyChanged(nameof(InspectorRangeText));
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
