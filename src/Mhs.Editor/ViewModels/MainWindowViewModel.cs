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
            }
        }

        HopperToolCommand ??= new RelayCommand(() => { });
        BinToolCommand ??= new RelayCommand(() => { });
        ConveyorToolCommand ??= new RelayCommand(() => { });
        ChuteToolCommand ??= new RelayCommand(() => { });

        SetTool(_selectTool);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public EditorState EditorState { get; }

    public ICommand SelectToolCommand { get; }
    public ICommand HopperToolCommand { get; }
    public ICommand BinToolCommand { get; }
    public ICommand ConveyorToolCommand { get; }
    public ICommand ChuteToolCommand { get; }

    public bool IsSelectActive => EditorState.ActiveTool is SelectTool;
    public bool IsHopperActive => EditorState.ActiveTool.Name == "Hopper";
    public bool IsBinActive => EditorState.ActiveTool.Name == "Bin";
    public bool IsConveyorActive => EditorState.ActiveTool.Name == "Conveyor";
    public bool IsChuteActive => EditorState.ActiveTool.Name == "Chute";

    public string StatusText =>
        $"Ready | Tool: {EditorState.ActiveTool.Name} | Voxel: {(EditorState.HoveredVoxel?.ToString() ?? "-")} | Objects: {EditorState.Scene.Objects.Count}";

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
            ? $"Hovered Voxel: {(EditorState.HoveredVoxel?.ToString() ?? "-")}"
            : $"Position: {EditorState.SelectedObject.Position}";

    public string InspectorSizeText =>
        EditorState.SelectedObject is null
            ? $"Active Tool: {EditorState.ActiveTool.Name}"
            : $"Size: {EditorState.SelectedObject.Size}";

    public string InspectorRotationText =>
        EditorState.SelectedObject is null
            ? PreviewText()
            : $"Rotation: {EditorState.SelectedObject.RotationDegrees:0}";

    public string InspectorStatusText =>
        EditorState.SelectedObject is null
            ? "Status: Ready"
            : "Status: Placed";

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
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(InspectorSelectedText));
        OnPropertyChanged(nameof(InspectorIdText));
        OnPropertyChanged(nameof(InspectorPositionText));
        OnPropertyChanged(nameof(InspectorSizeText));
        OnPropertyChanged(nameof(InspectorRotationText));
        OnPropertyChanged(nameof(InspectorStatusText));
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
