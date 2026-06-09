using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Mhs.Editor.Editor;
using Mhs.Editor.Settings;
using Mhs.Editor.Viewport;

namespace Mhs.Editor.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private const float MaterialFlowTickSeconds = 0.25f;
    private const float ManualMaterialFlowStepSeconds = 0.35f;
    private const string CatalogAllCategoryName = "All";
    private static readonly IReadOnlyList<string> CatalogCategoryOrder = ["Transport", "Feed & Discharge", "Vertical", "Structure", "Utilities"];
    private static readonly IReadOnlyList<int> MtrlSrcGranuleCountOptions = [1, 5, 10, 50, 100];
    private readonly IEditorTool _selectTool = new SelectTool();
    private readonly ConveyorRouteTool _conveyorRouteTool = new();
    private readonly Dictionary<string, RelayCommand> _placePartToolCommandsByPartId = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyList<PartCatalogMetadataEntry> _partCatalogEntries;
    private ViewportInteractionPreset _interactionPreset = ViewportInteractionPreset.BlenderLike;
    private bool _useOpenGlViewport = true;
    private string _preferredOpenGlGpuName = "System default GPU";
    private int _defaultSceneFloor;
    private int _defaultSceneLayer;
    private bool _defaultUseOpenGlViewport = true;
    private bool _expertMode;
    private string _selectedMtrlSrcUnitsPerSecondText = FormatRate(SceneObject.DefaultMaterialUnitsPerSecond);
    private string _selectedMtrlSrcMaterialId = SceneObject.DefaultMaterialId;
    private int _selectedMtrlSrcGranulesPerPacket = SceneObject.DefaultMaterialGranulesPerPacket;
    private string _selectedMtrlSrcRateStatusText = "Select MtrlSrc";
    private SceneTreeNodeViewModel? _selectedSceneTreeNode;
    private string _partSearchText = string.Empty;
    private string _selectedPartCategoryFilter = CatalogAllCategoryName;
    private bool _isCatalogBrowserOpen;
    private string _activeViewMode = "Iso";

    public MainWindowViewModel()
    {
        EditorState = new EditorState();
        EditorState.PropertyChanged += OnEditorStatePropertyChanged;
        EditorState.Scene.Objects.CollectionChanged += OnSceneObjectsChanged;

        SelectToolCommand = new RelayCommand(() => SetTool(_selectTool));
        ConveyorRouteToolCommand = new RelayCommand(() => SetTool(_conveyorRouteTool));
        MoveSelectionCommand = new RelayCommand(StartMoveSelection);
        RotateSelectionCommand = new RelayCommand(RotateAction);
        DeleteSelectionCommand = new RelayCommand(DeleteSelection);
        UndoCommand = new RelayCommand(() => { }, canExecute: () => false);
        RedoCommand = new RelayCommand(() => { }, canExecute: () => false);
        RunTestCommand = new RelayCommand(() =>
        {
            EditorState.StatusMessage = "Test runner is not wired yet.";
            RaiseComputed();
        });
        LoadDemoFactorySceneCommand = new RelayCommand(LoadDemoFactoryScene);
        BrowseMorePartsCommand = new RelayCommand(ToggleCatalogBrowser);
        Floor0Command = new RelayCommand(() => SetActiveFloor(0));
        Floor1Command = new RelayCommand(() => SetActiveFloor(1));
        Floor2Command = new RelayCommand(() => SetActiveFloor(2));
        Layer0Command = new RelayCommand(() => SetActiveLayer(0));
        Layer1Command = new RelayCommand(() => SetActiveLayer(1));
        Layer2Command = new RelayCommand(() => SetActiveLayer(2));
        BlenderLikeSettingsCommand = new RelayCommand(() => SetInteractionPreset(ViewportInteractionPreset.BlenderLike));
        AutoCadLikeSettingsCommand = new RelayCommand(() => SetInteractionPreset(ViewportInteractionPreset.AutoCadLike));
        UseSoftwareViewportCommand = new RelayCommand(() => SetViewportMode(useOpenGl: false));
        UseOpenGlViewportCommand = new RelayCommand(() => SetViewportMode(useOpenGl: true));
        UsePresentationViewportModeCommand = new RelayCommand(() => SetViewportVisualMode(ViewportVisualMode.Presentation));
        UseTechnicalViewportModeCommand = new RelayCommand(() => SetViewportVisualMode(ViewportVisualMode.Technical));
        InjectDebugOreCommand = new RelayCommand(InjectDebugOre);
        StepMaterialFlowCommand = new RelayCommand(StepMaterialFlow);
        ClearMaterialFlowCommand = new RelayCommand(ClearMaterialFlow);
        ApplySelectedMtrlSrcRateCommand = new RelayCommand(ApplySelectedMtrlSrcRate);
        IncreaseSelectedMtrlSrcRateCommand = new RelayCommand(() => NudgeSelectedMtrlSrcRate(0.5f));
        DecreaseSelectedMtrlSrcRateCommand = new RelayCommand(() => NudgeSelectedMtrlSrcRate(-0.5f));

        foreach (var part in EditorState.PartDefinitions)
        {
            var command = new RelayCommand(() => SetTool(new PlacePartTool(part)));
            _placePartToolCommandsByPartId[part.Id] = command;
        }

        HopperToolCommand = ResolvePlacePartToolCommand("hopper");
        BinToolCommand = ResolvePlacePartToolCommand("bin");
        ChuteToolCommand = ResolvePlacePartToolCommand("chute");
        TallHopperToolCommand = ResolvePlacePartToolCommand("tall_hopper");
        MtrlSrcToolCommand = ResolvePlacePartToolCommand("mtrlsrc");
        MtrlRecvToolCommand = ResolvePlacePartToolCommand("mtrlrecv");
        SplitterToolCommand = ResolvePlacePartToolCommand("conveyor_split");
        LiftToolCommand = ResolvePlacePartToolCommand("lift_elevator");
        TurnToolCommand = ResolvePlacePartToolCommand("conveyor_curve");
        MergeToolCommand = ResolvePlacePartToolCommand("conveyor_merge");
        SupportToolCommand = ResolvePlacePartToolCommand("support_frame");
        OtherPartsCommand = BrowseMorePartsCommand;
        IsoViewCommand = new RelayCommand(() => SetActiveViewMode("Iso"));
        TopViewCommand = new RelayCommand(() => SetActiveViewMode("Top"));
        FrontViewCommand = new RelayCommand(() => SetActiveViewMode("Front"));
        RightViewCommand = new RelayCommand(() => SetActiveViewMode("Right"));

        _partCatalogEntries = PartCatalogLoader.LoadCatalog();
        RebuildPartCatalogCategoryFilters();
        RebuildPartCatalogSections();

        SetTool(_selectTool);
        SyncSelectedMtrlSrcEditor();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public EditorState EditorState { get; }

    public ICommand SelectToolCommand { get; }
    public ICommand ConveyorRouteToolCommand { get; }
    public ICommand MoveSelectionCommand { get; }
    public ICommand RotateSelectionCommand { get; }
    public ICommand DeleteSelectionCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand RunTestCommand { get; }
    public ICommand LoadDemoFactorySceneCommand { get; }
    public ICommand BrowseMorePartsCommand { get; }
    public ICommand HopperToolCommand { get; }
    public ICommand BinToolCommand { get; }
    public ICommand ChuteToolCommand { get; }
    public ICommand TallHopperToolCommand { get; }
    public ICommand MtrlSrcToolCommand { get; }
    public ICommand MtrlRecvToolCommand { get; }
    public ICommand SplitterToolCommand { get; }
    public ICommand LiftToolCommand { get; }
    public ICommand TurnToolCommand { get; }
    public ICommand MergeToolCommand { get; }
    public ICommand SupportToolCommand { get; }
    public ICommand OtherPartsCommand { get; }
    public ICommand IsoViewCommand { get; }
    public ICommand TopViewCommand { get; }
    public ICommand FrontViewCommand { get; }
    public ICommand RightViewCommand { get; }
    public ICommand Floor0Command { get; }
    public ICommand Floor1Command { get; }
    public ICommand Floor2Command { get; }
    public ICommand Layer0Command { get; }
    public ICommand Layer1Command { get; }
    public ICommand Layer2Command { get; }
    public ICommand BlenderLikeSettingsCommand { get; }
    public ICommand AutoCadLikeSettingsCommand { get; }
    public ICommand UseSoftwareViewportCommand { get; }
    public ICommand UseOpenGlViewportCommand { get; }
    public ICommand UsePresentationViewportModeCommand { get; }
    public ICommand UseTechnicalViewportModeCommand { get; }
    public ICommand InjectDebugOreCommand { get; }
    public ICommand StepMaterialFlowCommand { get; }
    public ICommand ClearMaterialFlowCommand { get; }
    public ICommand ApplySelectedMtrlSrcRateCommand { get; }
    public ICommand IncreaseSelectedMtrlSrcRateCommand { get; }
    public ICommand DecreaseSelectedMtrlSrcRateCommand { get; }

    public ObservableCollection<SceneTreeNodeViewModel> SceneTreeNodes { get; } = [];
    public ObservableCollection<PartCatalogSectionViewModel> PartCatalogSections { get; } = [];
    public ObservableCollection<PartCatalogCategoryFilterViewModel> PartCatalogCategoryFilters { get; } = [];

    public SceneTreeNodeViewModel? SelectedSceneTreeNode
    {
        get => _selectedSceneTreeNode;
        set
        {
            if (value?.IsGroupHeader == true)
            {
                if (_selectedSceneTreeNode is not null)
                {
                    _selectedSceneTreeNode = null;
                    OnPropertyChanged();
                }

                return;
            }

            if (_selectedSceneTreeNode == value)
            {
                return;
            }

            _selectedSceneTreeNode = value;
            OnPropertyChanged();
            SelectObjectFromTree(value?.SceneObject);
        }
    }

    public void SelectObjectFromTree(SceneObject? sceneObject)
    {
        if (EditorState.ActiveTool is not SelectTool)
        {
            SetTool(_selectTool);
        }

        if (sceneObject is not null)
        {
            EditorState.ActiveFloor = WorldVerticalSettings.ToFloor(sceneObject.Position.Z);
            EditorState.ActiveLayer = WorldVerticalSettings.ToLayer(sceneObject.Position.Z);
        }

        EditorState.SelectedObject = sceneObject;
    }

    public bool IsSelectActive => EditorState.ActiveTool is SelectTool;
    public bool IsMoveActionActive => EditorState.IsMovingSelection;
    public bool IsRotateActionActive => EditorState.IsSelectionRotationMode;
    public bool IsHopperActive => EditorState.ActiveTool.Name == "Hopper";
    public bool IsBinActive => EditorState.ActiveTool.Name == "Bin";
    public bool IsConveyorRouteActive => EditorState.ActiveTool is ConveyorRouteTool;
    public bool IsChuteActive => EditorState.ActiveTool.Name == "Chute";
    public bool IsTallHopperActive => EditorState.ActiveTool.Name == "Tall Hopper";
    public bool IsMtrlSrcActive => EditorState.ActiveTool.Name == "MtrlSrc";
    public bool IsMtrlRecvActive => EditorState.ActiveTool.Name == "MtrlRecv";
    public bool IsSplitterActive => EditorState.ActiveTool.Name == "Conveyor (Split)";
    public bool IsLiftActive => EditorState.ActiveTool.Name == "Lift / Elevator";
    public bool IsTurnActive => EditorState.ActiveTool.Name == "Conveyor (Curve)";
    public bool IsMergeActive => EditorState.ActiveTool.Name == "Conveyor (Merge)";
    public bool IsSupportActive => EditorState.ActiveTool.Name == "Support Frame";
    public bool IsIsoViewActive => string.Equals(_activeViewMode, "Iso", StringComparison.Ordinal);
    public bool IsTopViewActive => string.Equals(_activeViewMode, "Top", StringComparison.Ordinal);
    public bool IsFrontViewActive => string.Equals(_activeViewMode, "Front", StringComparison.Ordinal);
    public bool IsRightViewActive => string.Equals(_activeViewMode, "Right", StringComparison.Ordinal);
    public bool IsFloor0Active => EditorState.ActiveFloor == 0;
    public bool IsFloor1Active => EditorState.ActiveFloor == 1;
    public bool IsFloor2Active => EditorState.ActiveFloor == 2;
    public bool IsLayer0Active => EditorState.ActiveLayer == 0;
    public bool IsLayer1Active => EditorState.ActiveLayer == 1;
    public bool IsLayer2Active => EditorState.ActiveLayer == 2;
    public bool IsBlenderLikeSettingsActive => _interactionPreset == ViewportInteractionPreset.BlenderLike;
    public bool IsAutoCadLikeSettingsActive => _interactionPreset == ViewportInteractionPreset.AutoCadLike;
    public bool IsSoftwareViewportMode => !_useOpenGlViewport;
    public bool IsOpenGlViewportMode => _useOpenGlViewport;
    public bool IsPresentationViewportVisualMode => EditorState.ViewportVisualMode == ViewportVisualMode.Presentation;
    public bool IsTechnicalViewportVisualMode => EditorState.ViewportVisualMode == ViewportVisualMode.Technical;
    public bool IsExpertMode => _expertMode;
    public bool IsSimpleMode => !_expertMode;
    public ViewportInteractionPreset InteractionPreset => _interactionPreset;
    public bool UseOpenGlViewport => _useOpenGlViewport;
    public string PreferredOpenGlGpuName => _preferredOpenGlGpuName;
    public int SceneViewportColumnSpan => 1;
    public string PartSearchText
    {
        get => _partSearchText;
        set
        {
            var next = value ?? string.Empty;
            if (string.Equals(_partSearchText, next, StringComparison.Ordinal))
            {
                return;
            }

            _partSearchText = next;
            RebuildPartCatalogSections();
            OnPropertyChanged();
            OnPropertyChanged(nameof(PartCatalogSummaryText));
        }
    }

    public bool IsCatalogBrowserOpen => _isCatalogBrowserOpen;
    public string BrowseMorePartsLabel => _isCatalogBrowserOpen ? "Hide Catalog Browser" : "Browse More Parts...";
    public string PartCatalogSummaryText => BuildPartCatalogSummaryText();

    public string LayerDisplayText => $"Layer {EditorState.ActiveLayer}/{WorldVerticalSettings.LayersPerFloor - 1}";
    public string AbsoluteZDisplayText => $"Z {EditorState.ActiveAbsoluteZ}";
    public string ViewportSummaryText => $"{_activeViewMode} · {ViewportVisualModeLabel} · Floor {EditorState.ActiveFloor} · Layer {EditorState.ActiveLayer}";

    public string ViewportVisualModeLabel => EditorState.ViewportVisualMode == ViewportVisualMode.Presentation
        ? "Presentation"
        : "Technical";

    public string ViewportPanelBackground => IsPresentationViewportVisualMode ? "#D8D0C2" : "#121519";
    public string ViewportPanelBorderBrush => IsPresentationViewportVisualMode ? "#AFA694" : "#3B4252";
    public string ViewportSurfaceBackground => IsPresentationViewportVisualMode ? "#E6DECF" : "#121519";
    public string ViewportToolbarBackground => IsPresentationViewportVisualMode ? "#EAF5F0E6" : "#E5182028";
    public string ViewportToolbarBorderBrush => IsPresentationViewportVisualMode ? "#B8AB95" : "#5A6B80";
    public string ViewportToolbarTextBrush => IsPresentationViewportVisualMode ? "#667181" : "#9FB4CC";
    public string ViewportToolbarControlBrush => IsPresentationViewportVisualMode ? "#3E4652" : "#D4DEE9";
    public string ViewportToolbarSeparatorBrush => IsPresentationViewportVisualMode ? "#C4B8A6" : "#455366";
    public string ViewportToolbarSubtleTextBrush => IsPresentationViewportVisualMode ? "#667181" : "#8EA2BA";

    public string StatusText
    {
        get
        {
            var hotkeys = _interactionPreset == ViewportInteractionPreset.BlenderLike
                ? "G=Move  R=Rotate  Del=Delete  Esc=Cancel"
                : "M=Move  R=Rotate  Del=Delete  Esc=Cancel";
            if (EditorState.ActiveTool is ConveyorRouteTool)
            {
                hotkeys = "LMB drag=Draw  LMB=Anchor  RMB/Enter=Finish  Backspace=Undo  Esc=Cancel";
            }

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

            if (EditorState.IsSelectionRotationMode)
            {
                var previewRotation = EditorState.SelectionRotationPreviewDegrees;
                var previewStatus = EditorState.SelectionRotationPreviewIsValid ? "Valid" : "Blocked";
                return $"Rotate | Selected: {EditorState.SelectedObject?.PartType ?? "None"} | Rot {previewRotation}° | {previewStatus} | RMB=Confirm Esc=Cancel | Objects: {EditorState.Scene.Objects.Count} | {hotkeys}";
            }

            var rotation = EditorState.ActiveTool is PlacePartTool
                ? $" | Rot {EditorState.ActivePlacementRotationZDegrees}°"
                : string.Empty;
            var viewportMode = _useOpenGlViewport ? "Viewport: OpenGL/Silk.NET" : "Viewport: Software";
            var openGlInfo = _expertMode && _useOpenGlViewport && !string.IsNullOrWhiteSpace(EditorState.OpenGlBackendInfo)
                ? $" | Preferred GPU: {PreferredOpenGlGpuName} | GL {EditorState.OpenGlBackendInfo}"
                : string.Empty;

            return $"{EditorState.StatusMessage} | {viewportMode}{openGlInfo} | Tool: {EditorState.ActiveTool.Name}{rotation} | Floor {EditorState.ActiveFloor} · Layer {EditorState.ActiveLayer}/{WorldVerticalSettings.LayersPerFloor - 1} · Z {EditorState.ActiveAbsoluteZ} | X {(EditorState.HoveredVoxel?.X.ToString() ?? "-")} Y {(EditorState.HoveredVoxel?.Y.ToString() ?? "-")} | Objects: {EditorState.Scene.Objects.Count} | {hotkeys}";
        }
    }

    public string InspectorSelectedText =>
        EditorState.SelectedObject is null
            ? "Selected: None"
            : $"Selected: {EditorState.SelectedObject.DisplayName}";

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
            : $"Type: {EditorState.SelectedObject.PartType}";

    public bool HasSelectedObject => EditorState.SelectedObject is not null;

    public Bitmap? SelectedObjectThumbnailImage
    {
        get
        {
            if (EditorState.SelectedObject is not { } selected)
            {
                return null;
            }

            var entry = _partCatalogEntries.FirstOrDefault(catalogEntry =>
                string.Equals(catalogEntry.Id, selected.PartId, StringComparison.OrdinalIgnoreCase));

            return entry is null ? null : PartCatalogItemViewModel.GetThumbnailImage(entry.Thumbnail);
        }
    }

    public string SelectedObjectName
    {
        get => EditorState.SelectedObject?.DisplayName ?? string.Empty;
        set
        {
            if (EditorState.SelectedObject is not { } selected)
            {
                return;
            }

            var normalized = NormalizeDisplayName(selected, value);
            var targets = GetObjectsSharingDisplayName(selected).ToList();
            if (targets.Count > 0 && targets.All(target => string.Equals(target.DisplayName, normalized, StringComparison.Ordinal)))
            {
                return;
            }

            foreach (var target in targets)
            {
                target.DisplayName = normalized;
            }

            RebuildSceneTree();
            RaiseComputed();
        }
    }


    public int InspectorPositionX
    {
        get => EditorState.SelectedObject?.Position.X ?? 0;
        set => TryMoveSelectedObject(selected => selected.Position with { X = value });
    }

    public int InspectorPositionY
    {
        get => EditorState.SelectedObject?.Position.Y ?? 0;
        set => TryMoveSelectedObject(selected => selected.Position with { Y = value });
    }

    public int InspectorPositionZ
    {
        get => EditorState.SelectedObject?.Position.Z ?? EditorState.ActiveAbsoluteZ;
        set => TryMoveSelectedObject(selected => selected.Position with { Z = value });
    }

    public int InspectorRotationZDegrees
    {
        get => EditorState.SelectedObject?.RotationZDegrees ?? EditorState.ActivePlacementRotationZDegrees;
        set
        {
            if (EditorState.SelectedObject is not { } selected)
            {
                return;
            }

            var normalized = RotationHelper.NormalizeDegrees(value);
            var targetSize = selected.GetEffectiveSize(normalized);
            var validation = EditorState.ValidatePlacement(selected.Position, targetSize, selected.Id);
            if (!validation.IsValid)
            {
                EditorState.StatusMessage = $"Inspector blocked rotation: {validation.Reason ?? "invalid"}";
                RaiseComputed();
                return;
            }

            selected.RotationZDegrees = normalized;
            if (selected.IsRouteConveyorSegment)
            {
                selected.RouteFlowReversed = normalized is 180 or 270;
            }

            RefreshConveyorFlowTopology();
            EditorState.StatusMessage = $"Inspector updated rotation to {normalized}°";
            RaiseComputed();
        }
    }

    public string InspectorBaseSizeValue => EditorState.SelectedObject?.BaseSize.ToString() ?? "-";

    public string InspectorEffectiveSizeValue => EditorState.SelectedObject?.EffectiveSize.ToString() ?? "-";

    public string InspectorPartTypeText => EditorState.SelectedObject is null ? "Type: -" : EditorState.SelectedObject.PartType;

    public string InspectorValidityText
    {
        get
        {
            if (EditorState.SelectedObject is null)
            {
                return "No selection";
            }

            return SelectedPlacementValidation().IsValid ? "Valid" : "Invalid";
        }
    }

    public string InspectorValidityDetail
    {
        get
        {
            if (EditorState.SelectedObject is null)
            {
                return "Select a part to inspect";
            }

            var validation = SelectedPlacementValidation();
            return validation.IsValid ? "Placement and bounds check passed" : validation.Reason ?? "Placement check failed";
        }
    }

    public string InspectorValidityBrush => EditorState.SelectedObject is null
        ? "#8EA2BA"
        : SelectedPlacementValidation().IsValid ? "#7EE787" : "#FF7B72";

    public string InspectorConveyorLengthText
    {
        get
        {
            if (EditorState.SelectedObject is not { } selected || !selected.IsConveyor)
            {
                return "Select a conveyor";
            }

            if (selected.RouteStartCell is { } start && selected.RouteEndCell is { } end)
            {
                return $"{Math.Abs(end.X - start.X) + Math.Abs(end.Y - start.Y) + Math.Abs(end.Z - start.Z) + 1} cells";
            }

            return $"{Math.Max(selected.EffectiveSize.WidthX, selected.EffectiveSize.DepthY)} cells";
        }
    }

    public string InspectorConveyorRangeText
    {
        get
        {
            if (EditorState.SelectedObject is not { } selected || !selected.IsConveyor)
            {
                return "Range: -";
            }

            if (selected.RouteStartCell is { } start && selected.RouteEndCell is { } end)
            {
                return $"{start} → {end}";
            }

            return $"X {selected.MinX}..{selected.MaxX}, Y {selected.MinY}..{selected.MaxY}, Z {selected.MinZ}..{selected.MaxZ}";
        }
    }

    public bool InspectorConveyorDirectionEditable => EditorState.SelectedObject?.IsRouteConveyorSegment == true;

    public bool InspectorConveyorFlowReversed
    {
        get => EditorState.SelectedObject?.RouteFlowReversed == true;
        set
        {
            if (EditorState.SelectedObject is not { } selected || !selected.IsRouteConveyorSegment || selected.RouteFlowReversed == value)
            {
                return;
            }

            selected.RouteFlowReversed = value;
            RefreshConveyorFlowTopology();
            EditorState.StatusMessage = value ? "Inspector reversed conveyor flow" : "Inspector restored conveyor flow";
            RaiseComputed();
        }
    }

    public string InspectorTransferBehaviorText
    {
        get
        {
            if (EditorState.SelectedObject is null)
            {
                return "Select a part";
            }

            if (SelectedMtrlSrcPanelVisible)
            {
                return "Material source injects packets into the connected conveyor route.";
            }

            if (EditorState.SelectedObject.IsConveyor)
            {
                return "Conveyor transfers packets from input to output ports along the route.";
            }

            return "No editable transfer behavior is stored for this part.";
        }
    }

    public string InspectorPaintText => EditorState.SelectedObject is null
        ? "-"
        : GetSelectedPartColorHex() is { } color ? $"Catalog paint {color} (read-only)" : "Catalog paint (read-only)";

    public string InspectorAccentColorText => "Not stored on scene objects";

    public string InspectorDiagnosticPositionText => InspectorPositionText;
    public string InspectorDiagnosticSizeText => InspectorSizeText;
    public string InspectorDiagnosticEffectiveSizeText => InspectorRotationText;
    public string InspectorDiagnosticRotationText => InspectorStatusText;
    public string InspectorDiagnosticZSpanText => InspectorContextText;
    public string InspectorDiagnosticRangeText => InspectorRangeText;
    public string InspectorDiagnosticExtraText => InspectorExtraText;

    public bool ShowConveyorDebug
    {
        get => EditorState.ShowConveyorDebug;
        set
        {
            if (EditorState.ShowConveyorDebug == value)
            {
                return;
            }

            EditorState.ShowConveyorDebug = value;
            RaiseComputed();
        }
    }

    public bool ShowGrid
    {
        get => EditorState.ShowGrid;
        set
        {
            if (EditorState.ShowGrid == value)
            {
                return;
            }

            EditorState.ShowGrid = value;
            RaiseComputed();
        }
    }

    public bool ShowBounds
    {
        get => EditorState.ShowBounds;
        set
        {
            if (EditorState.ShowBounds == value)
            {
                return;
            }

            EditorState.ShowBounds = value;
            RaiseComputed();
        }
    }

    public bool ShowFlow
    {
        get => EditorState.ShowFlow;
        set
        {
            if (EditorState.ShowFlow == value)
            {
                return;
            }

            EditorState.ShowFlow = value;
            RaiseComputed();
        }
    }

    public bool SelectedMtrlSrcPanelVisible =>
        EditorState.SelectedObject is { } selected
        && string.Equals(selected.PartId, "mtrlsrc", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<string> AvailableMtrlSrcMaterialIds => MaterialCatalog.AvailableMaterialIds;

    public IReadOnlyList<int> AvailableMtrlSrcGranuleCounts => MtrlSrcGranuleCountOptions;

    public string SelectedMtrlSrcUnitsPerSecondText
    {
        get => _selectedMtrlSrcUnitsPerSecondText;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_selectedMtrlSrcUnitsPerSecondText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _selectedMtrlSrcUnitsPerSecondText = normalized;
            _selectedMtrlSrcRateStatusText = SelectedMtrlSrcPanelVisible
                ? "Press Apply to update source settings"
                : "Select MtrlSrc";
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedMtrlSrcRateStatusText));
        }
    }

    public string SelectedMtrlSrcRateStatusText => _selectedMtrlSrcRateStatusText;

    public string SelectedMtrlSrcMaterialId
    {
        get => _selectedMtrlSrcMaterialId;
        set
        {
            var normalized = MaterialCatalog.NormalizeId(value);
            if (string.Equals(_selectedMtrlSrcMaterialId, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _selectedMtrlSrcMaterialId = normalized;
            _selectedMtrlSrcRateStatusText = SelectedMtrlSrcPanelVisible
                ? "Press Apply to update source settings"
                : "Select MtrlSrc";
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedMtrlSrcRateStatusText));
        }
    }

    public int SelectedMtrlSrcGranulesPerPacket
    {
        get => _selectedMtrlSrcGranulesPerPacket;
        set
        {
            var normalized = NormalizeMtrlSrcGranulesPerPacket(value);
            if (_selectedMtrlSrcGranulesPerPacket == normalized)
            {
                return;
            }

            _selectedMtrlSrcGranulesPerPacket = normalized;
            _selectedMtrlSrcRateStatusText = SelectedMtrlSrcPanelVisible
                ? "Press Apply to update source settings"
                : "Select MtrlSrc";
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedMtrlSrcRateStatusText));
        }
    }

    public string InspectorPortText
    {
        get
        {
            if (EditorState.SelectedObject is null)
            {
                return "Ports: Select object";
            }

            var snapshot = EditorState.GetPortConnectivitySnapshot();
            var statuses = snapshot.GetPortStatusesForOwner(EditorState.SelectedObject.Id);
            if (statuses.Count == 0)
            {
                return "Ports: None";
            }

            var connected = statuses.Count(status => status.Status == PortConnectionStatus.Connected);
            var adapterRequired = statuses.Count(status => status.Status == PortConnectionStatus.AdapterRequired);
            var invalid = statuses.Count(status => status.Status == PortConnectionStatus.InvalidNearby);
            var unconnected = statuses.Count - connected - adapterRequired - invalid;
            var details = string.Join(", ", statuses.Select(status =>
            {
                var label = $"{status.Port.Name}:{StatusLabel(status.Status)}";
                var peer = snapshot.GetConnectedPeerPort(status.Port.PortId);
                if (peer is not null)
                {
                    label = $"{label} -> {ShortId(peer.OwnerSceneObjectId)}:{peer.Name}";
                }

                if (status.Status == PortConnectionStatus.AdapterRequired && status.AdapterRequiredCandidates.Count > 0)
                {
                    var adapterDetails = string.Join("; ", status.AdapterRequiredCandidates.Select(c =>
                        $"[{c.Reason}: {string.Join("/", c.PossibleAdapters)}]"));
                    label = $"{label} {adapterDetails}";
                }

                return $"{label} ({status.Diagnostic})";
            }));
            return $"Ports: {connected} connected, {unconnected} open, {invalid} invalid, {adapterRequired} adapter-required | {details}";
        }
    }

    public string InspectorMaterialFlowText
    {
        get
        {
            var routes = EditorState.Scene.ConveyorRouteFlow.Routes;
            if (routes.Count == 0)
            {
                return "Material Flow: no conveyor routes";
            }

            var occupied = EditorState.Scene.ConveyorRouteFlow.OccupiedCellCount();
            var details = string.Join(" | ", routes.Select((route, index) =>
            {
                var routeOccupied = route.Slots.Count(slot => slot is not null);
                var sender = route.InputAttachments.Count > 0
                    ? string.Join(",", route.InputAttachments.Select(source => ShortId(source.ObjectId)).Distinct())
                    : "-";
                var materials = route.InputAttachments.Count > 0
                    ? string.Join("/", route.InputAttachments.Select(source => MaterialCatalog.NormalizeId(source.MaterialId)).Distinct(StringComparer.OrdinalIgnoreCase))
                    : "-";
                var receiver = route.ReceiverObjectId.HasValue ? ShortId(route.ReceiverObjectId.Value) : "-";
                return $"R{index + 1}: {routeOccupied}/{route.Slots.Length} src={sender} in={route.InputAttachments.Count} mat={materials} recv={receiver}";
            }));
            return $"Material Flow: {occupied} packet(s) on {routes.Count} route(s) | {details}";
        }
    }

    public bool FloatingDebugPanelVisible => _expertMode && ShowConveyorDebug;
    public bool ExpertInspectorVisible => true;
    public bool ExpertGpuLabelsVisible => _expertMode;

    public string ConveyorDebugSelectionText
    {
        get
        {
            if (EditorState.SelectedObject is not { } selected)
            {
                return "Selection: none";
            }

            return selected.IsConveyor
                ? $"Selection: conveyor {ShortId(selected.Id)}"
                : $"Selection: {selected.PartType} {ShortId(selected.Id)}";
        }
    }

    public string ConveyorDebugRouteSummaryText
    {
        get
        {
            if (TryGetSelectedConveyorRoute(out var route, out var selectedCells))
            {
                var occupied = route.Slots.Count(slot => slot is not null);
                var sender = route.InputAttachments.Count > 0
                    ? string.Join(",", route.InputAttachments.Select(source => ShortId(source.ObjectId)).Distinct())
                    : "-";
                var sourceRate = route.InputAttachments.Count > 0
                    ? string.Join(" | ", route.InputAttachments.Select(source =>
                        $"{ShortId(source.ObjectId)}->{source.RouteCellIndex}:{source.UnitsPerSecond.ToString("0.##", CultureInfo.InvariantCulture)}"))
                    : "-";
                var receiver = route.ReceiverObjectId.HasValue ? ShortId(route.ReceiverObjectId.Value) : "-";
                return $"Route: {route.Cells.Count} cells | occupied {occupied}/{route.Slots.Length} | sender {sender} | recv {receiver} | inputs {route.InputAttachments.Count} | src {sourceRate} u/s | selected seg {selectedCells.Count} cell(s)";
            }

            if (EditorState.ActiveConveyorRoute is { } draft)
            {
                return $"Draft: {draft.Anchors.Count} anchor(s) on Z {draft.Z} | preview {(draft.PreviewEnd.HasValue ? (draft.PreviewIsValid ? "valid" : "blocked") : "waiting")}";
            }

            return "Route: select a conveyor to inspect its cell path";
        }
    }

    public string ConveyorDebugGridText
    {
        get
        {
            if (TryGetSelectedConveyorRoute(out var route, out var selectedCells))
            {
                return BuildConveyorGridText(route, selectedCells);
            }

            if (EditorState.ActiveConveyorRoute is { } draft)
            {
                return $"anchors={draft.Anchors.Count}\npreview={(draft.PreviewEnd.HasValue ? draft.PreviewEnd.Value.ToString() : "-")}\nblocked={(draft.PreviewIsValid ? "-" : draft.InvalidReason ?? "-")}";
            }

            return "Select a conveyor to view its occupied cells in a grid.";
        }
    }

    public string ConveyorDebugLegendText =>
        "Legend: [nn]=route order  [##]=occupied packet  [S#]=selected conveyor cell  [..]=empty grid | selected route shows colored input markers in the viewport";

    public bool ConveyorRoutePanelVisible => EditorState.ActiveConveyorRoute is not null;

    public string ConveyorRouteAnchorsValue =>
        EditorState.ActiveConveyorRoute is { } route ? route.Anchors.Count.ToString() : "-";

    public string ConveyorRouteZValue =>
        EditorState.ActiveConveyorRoute is { } route ? route.Z.ToString() : "-";

    public string ConveyorRoutePreviewValue
    {
        get
        {
            if (EditorState.ActiveConveyorRoute is not { } route)
            {
                return "-";
            }

            if (!route.PreviewEnd.HasValue)
            {
                return "Waiting for next anchor";
            }

            return route.PreviewIsValid ? "Valid" : "Blocked";
        }
    }

    public string ConveyorRouteBlockedReasonValue
    {
        get
        {
            if (EditorState.ActiveConveyorRoute is not { } route)
            {
                return "-";
            }

            if (!route.PreviewEnd.HasValue || route.PreviewIsValid)
            {
                return "-";
            }

            return route.InvalidReason ?? "invalid";
        }
    }

    public string Floor2StackText => FloorStackText(2);
    public string Floor1StackText => FloorStackText(1);
    public string Floor0StackText => FloorStackText(0);
    public string ActiveFloorSummaryText => $"Active Floor: {EditorState.ActiveFloor}";
    public string ActiveLayerSummaryText => $"Layer {EditorState.ActiveLayer} of {WorldVerticalSettings.LayersPerFloor - 1}";
    public string ActiveAbsoluteZSummaryText => $"Absolute Z: {EditorState.ActiveAbsoluteZ}";
    public string OpenGlBackendSummaryText => $"OpenGL: {EditorState.OpenGlBackendInfo}";
    public string PreferredOpenGlGpuSummaryText => $"Preferred GPU: {PreferredOpenGlGpuName}";
    public string Floor2StackBackground => EditorState.ActiveFloor == 2 ? "#324563" : "#1E242E";
    public string Floor1StackBackground => EditorState.ActiveFloor == 1 ? "#324563" : "#1E242E";
    public string Floor0StackBackground => EditorState.ActiveFloor == 0 ? "#324563" : "#1E242E";
    public string Floor2StackBorder => EditorState.ActiveFloor == 2 ? "#88B8FF" : "#394454";
    public string Floor1StackBorder => EditorState.ActiveFloor == 1 ? "#88B8FF" : "#394454";
    public string Floor0StackBorder => EditorState.ActiveFloor == 0 ? "#88B8FF" : "#394454";

    public bool HandleKeyDown(Key key)
    {
        if (EditorState.ActiveTool is ConveyorRouteTool routeTool && EditorState.ActiveConveyorRoute is not null)
        {
            if (key == Key.Enter)
            {
                routeTool.FinishRoute(EditorState);
                RaiseComputed();
                return true;
            }

            switch (key)
            {
                case Key.Back:
                    routeTool.RemoveLastAnchor(EditorState);
                    RaiseComputed();
                    return true;
                case Key.Escape:
                    CancelAction();
                    RaiseComputed();
                    return true;
            }
        }

        switch (key)
        {
            case Key.Space:
                ResetToSelectAndClearSelection();
                break;
            case Key.R:
                RotateAction();
                break;
            case Key.Delete:
                DeleteSelection();
                break;
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
            default:
                return false;
        }

        RaiseComputed();
        return true;
    }

    public void NewScene()
    {
        LoadSceneFileData(new SceneFileData
        {
            ActiveFloor = _defaultSceneFloor,
            ActiveLayer = _defaultSceneLayer,
            RendererMode = _defaultUseOpenGlViewport ? "opengl" : "software"
        });
        EditorState.StatusMessage = "New scene";
        RaiseComputed();
    }


    public void LoadDemoFactoryScene()
    {
        var z = WorldVerticalSettings.ToAbsoluteZ(1, 0);
        var objects = new List<SceneFileObjectData>
        {
            DemoObject("mtrlsrc", "ROM Feed Source", -11, -4, z, rotationZDegrees: 0, materialId: "Coal", unitsPerSecond: 2.5f, granulesPerPacket: 10),
            DemoObject("mtrlrecv", "Screen House Receiver", 0, 8, z, rotationZDegrees: 90),

            DemoRouteSegment("Highlighted Conveyor Route", -10, -4, z, 6, -4),
            DemoRouteSegment("Highlighted Conveyor Route", 7, -4, z, 7, 4),
            DemoRouteSegment("Highlighted Conveyor Route", 6, 4, z, -7, 4),
            DemoRouteSegment("Highlighted Conveyor Route", -8, 4, z, -8, -1),
            DemoRouteSegment("Highlighted Conveyor Route", -7, -1, z, 0, -1),
            DemoRouteSegment("Highlighted Conveyor Route", 0, 0, z, 0, 7),

            DemoObject("support_frame", "Route Support A", -8, -4, 0),
            DemoObject("support_frame", "Route Support B", -2, -4, 0),
            DemoObject("support_frame", "Route Support C", 7, -3, 0),
            DemoObject("support_frame", "Route Support D", 6, 4, 0),
            DemoObject("support_frame", "Route Support E", -8, 3, 0),
            DemoObject("support_frame", "Route Support F", 0, 4, 0),
            DemoObject("beam", "Elevated Conveyor Cross Beam", -5, -5, 2, sizeOverride: new VoxelSize(8, 1, 1)),
            DemoObject("platform", "Maintenance Platform", 5, 1, 2),
            DemoObject("ladder", "Access Ladder", 4, 4, 0),

            DemoObject("hopper", "Primary Hopper", -11, 2, 5),
            DemoObject("hopper", "Secondary Hopper", 3, 5, 5),
            DemoObject("drop_chute", "Hopper Drop Chute", -10, 3, 2),
            DemoObject("chute", "Discharge Chute", 1, 6, 5, rotationZDegrees: 90),
            DemoObject("bin", "Coarse Ore Stockpile", -4, -9, 0),
            DemoObject("bin", "Fine Ore Stockpile", 5, -9, 0),
            DemoObject("bin", "Finished Product Bin", 8, 6, 0),

            DemoObject("lift_elevator", "Bucket Elevator", 10, -2, 0),
            DemoObject("conveyor_curve", "Loop Corner Visual", 6, -6, z, rotationZDegrees: 90),
            DemoObject("conveyor_split", "Sampling Splitter", -3, 6, z, rotationZDegrees: 180),
            DemoObject("conveyor_merge", "Return Merge", -11, -1, z),
            DemoObject("motor", "Route Drive Motor", 1, -3, z),
            DemoObject("sensor", "Flow Sensor", -4, 3, z),
            DemoObject("control_box", "Line Control Panel", 9, 1, 0)
        };

        LoadSceneFileData(new SceneFileData
        {
            ActiveFloor = 1,
            ActiveLayer = 0,
            RendererMode = _useOpenGlViewport ? "opengl" : "software",
            Objects = objects
        });

        EditorState.ViewportVisualMode = ViewportVisualMode.Presentation;
        EditorState.StatusMessage = "Loaded demo factory scene";
        RaiseComputed();
    }

    public SceneFileData CreateSceneFileData()
    {
        var objects = new List<SceneFileObjectData>(EditorState.Scene.Objects.Count);
        foreach (var sceneObject in EditorState.Scene.Objects)
        {
            var partDefinition = TryResolvePartDefinition(sceneObject, out var resolvedPartId);
            if (string.IsNullOrWhiteSpace(resolvedPartId))
            {
                throw new InvalidDataException($"Cannot save object with unknown part type '{sceneObject.PartType}'.");
            }

            VoxelSize? sizeOverride = null;
            if (partDefinition is null || partDefinition.Size != sceneObject.BaseSize)
            {
                sizeOverride = sceneObject.BaseSize;
            }

            objects.Add(new SceneFileObjectData
            {
                PartId = resolvedPartId,
                DisplayName = string.IsNullOrWhiteSpace(sceneObject.DisplayName) ? null : sceneObject.DisplayName.Trim(),
                Position = sceneObject.Position,
                RotationZDegrees = sceneObject.RotationZDegrees,
                MaterialUnitsPerSecond = sceneObject.MaterialUnitsPerSecond,
                MaterialGranulesPerPacket = sceneObject.MaterialGranulesPerPacket,
                MaterialId = string.Equals(sceneObject.PartId, "mtrlsrc", StringComparison.OrdinalIgnoreCase)
                    ? MaterialCatalog.NormalizeId(sceneObject.MaterialId)
                    : null,
                SizeOverride = sizeOverride,
                RouteStartCell = sceneObject.RouteStartCell,
                RouteEndCell = sceneObject.RouteEndCell,
                RouteFlowReversed = sceneObject.RouteFlowReversed
            });
        }

        return new SceneFileData
        {
            ActiveFloor = EditorState.ActiveFloor,
            ActiveLayer = EditorState.ActiveLayer,
            RendererMode = _useOpenGlViewport ? "opengl" : "software",
            Objects = objects
        };
    }

    public void LoadSceneFileData(SceneFileData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var objectDataItems = data.Objects ?? new List<SceneFileObjectData>();
        var loadedObjects = new List<SceneObject>(objectDataItems.Count);
        foreach (var objectData in objectDataItems)
        {
            var partDefinition = ResolvePartDefinition(objectData.PartId);
            var baseSize = objectData.SizeOverride ?? partDefinition.Size;
            if (baseSize.WidthX <= 0 || baseSize.DepthY <= 0 || baseSize.HeightZ <= 0)
            {
                throw new InvalidDataException($"Scene object '{objectData.PartId}' has an invalid size.");
            }

            var sceneObject = new SceneObject
            {
                PartId = partDefinition.Id,
                PartType = partDefinition.DisplayName,
                DisplayName = objectData.DisplayName?.Trim() ?? string.Empty,
                Position = objectData.Position,
                BaseSize = baseSize,
                RotationZDegrees = RotationHelper.NormalizeDegrees(objectData.RotationZDegrees),
                MaterialUnitsPerSecond = ResolveMaterialUnitsPerSecond(partDefinition.Id, objectData.MaterialUnitsPerSecond),
                MaterialGranulesPerPacket = ResolveMaterialGranulesPerPacket(partDefinition.Id, objectData.MaterialGranulesPerPacket),
                MaterialId = ResolveMaterialId(partDefinition.Id, objectData.MaterialId),
                RouteStartCell = objectData.RouteStartCell,
                RouteEndCell = objectData.RouteEndCell,
                RouteFlowReversed = objectData.RouteFlowReversed
            };

            if (!EditorState.IsObjectWithinGrid(sceneObject))
            {
                throw new InvalidDataException($"Scene object '{objectData.PartId}' is out of bounds.");
            }

            loadedObjects.Add(sceneObject);
        }

        EditorState.ActiveTool.OnCancel(EditorState);
        EditorState.Scene.MaterialFlow.ClearTokens();
        EditorState.Scene.ConveyorRouteFlow.Clear();
        EditorState.Scene.Objects.Clear();
        foreach (var sceneObject in loadedObjects)
        {
            EditorState.Scene.Objects.Add(sceneObject);
        }

        EditorState.SelectedObject = null;
        EditorState.HoveredObject = null;
        EditorState.HoveredVoxel = null;
        EditorState.GhostPreview = null;
        EditorState.ActiveConveyorRoute = null;
        EditorState.ClearMoveState();
        EditorState.ClearRotationAxis();
        EditorState.ActivePlacementRotationZDegrees = 0;
        EditorState.ActiveFloor = data.ActiveFloor;
        EditorState.ActiveLayer = data.ActiveLayer;

        if (!string.IsNullOrWhiteSpace(data.RendererMode))
        {
            _useOpenGlViewport = string.Equals(data.RendererMode, "opengl", StringComparison.OrdinalIgnoreCase);
        }

        SetTool(_selectTool);
        EditorState.StatusMessage = "Ready";
        RaiseComputed();
    }

    public void SetSceneStatus(string status)
    {
        EditorState.StatusMessage = status;
        RaiseComputed();
    }

    public void SetPreferredOpenGlGpu(string gpuName)
    {
        _preferredOpenGlGpuName = string.IsNullOrWhiteSpace(gpuName)
            ? "System default GPU"
            : gpuName.Trim();
        EditorState.StatusMessage = $"Preferred OpenGL GPU set to {_preferredOpenGlGpuName}";
        RaiseComputed();
    }

    public void ApplyAppPreferences(AppPreferences preferences, bool updateStatus = false)
    {
        _defaultSceneFloor = Math.Clamp(preferences.DefaultFloor, 0, WorldVerticalSettings.FloorCount - 1);
        _defaultSceneLayer = Math.Clamp(preferences.DefaultLayer, 0, WorldVerticalSettings.LayersPerFloor - 1);
        _defaultUseOpenGlViewport = !string.Equals(preferences.DefaultRendererMode, "software", StringComparison.OrdinalIgnoreCase);
        _expertMode = string.Equals(preferences.UiMode, "expert", StringComparison.OrdinalIgnoreCase);
        _useOpenGlViewport = _defaultUseOpenGlViewport;
        _preferredOpenGlGpuName = string.IsNullOrWhiteSpace(preferences.PreferredOpenGlGpuName)
            ? "System default GPU"
            : preferences.PreferredOpenGlGpuName.Trim();
        EditorState.ActiveFloor = _defaultSceneFloor;
        EditorState.ActiveLayer = _defaultSceneLayer;
        if (updateStatus)
        {
            EditorState.StatusMessage = "Settings updated";
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
        EditorState.ActiveConveyorRoute is { } route
            ? "Preview: See Conveyor Route panel"
            : EditorState.GhostPreview is null
                ? "Preview: None"
                : $"Preview: {EditorState.GhostPreview.Part.DisplayName} @ {EditorState.GhostPreview.Position} ({EditorState.GhostPreview.EffectiveSize})";

    private void InjectDebugOre()
    {
        if (EditorState.SelectedObject is null
            || !string.Equals(EditorState.SelectedObject.PartId, "mtrlsrc", StringComparison.OrdinalIgnoreCase))
        {
            EditorState.StatusMessage = "Inject failed: select MtrlSrc";
            RaiseComputed();
            return;
        }

        var snapshot = EditorState.GetPortConnectivitySnapshot();
        EditorState.Scene.ConveyorRouteFlow.Update(0f, snapshot, EditorState.Scene.Objects);
        var injected = EditorState.Scene.ConveyorRouteFlow.TryInjectFromSender(EditorState.SelectedObject.Id);
        var materialId = MaterialCatalog.NormalizeId(EditorState.SelectedObject.MaterialId);
        EditorState.StatusMessage = injected
            ? $"Injected {materialId} package from {ShortId(EditorState.SelectedObject.Id)}"
            : "Inject failed: source route input slot occupied or disconnected";
        RaiseComputed();
    }

    private void StepMaterialFlow()
    {
        var snapshot = EditorState.GetPortConnectivitySnapshot();
        var changed = EditorState.Scene.ConveyorRouteFlow.Update(ManualMaterialFlowStepSeconds, snapshot, EditorState.Scene.Objects);
        EditorState.StatusMessage = changed > 0
            ? $"Material flow stepped: {changed} route change(s)"
            : "Material flow stepped: no route changes";
        RaiseComputed();
    }

    public void TickMaterialFlow()
    {
        if (EditorState.Scene.Objects.Count == 0)
        {
            return;
        }

        var snapshot = EditorState.GetPortConnectivitySnapshot();
        var moved = EditorState.Scene.ConveyorRouteFlow.Update(MaterialFlowTickSeconds, snapshot, EditorState.Scene.Objects);
        if (moved > 0)
        {
            RaiseComputed();
        }
    }

    private void ClearMaterialFlow()
    {
        EditorState.Scene.MaterialFlow.ClearTokens();
        EditorState.Scene.ConveyorRouteFlow.Clear();
        EditorState.StatusMessage = "Material flow cleared";
        RaiseComputed();
    }

    private void ApplySelectedMtrlSrcRate()
    {
        if (!TryGetSelectedMtrlSrc(out var selected))
        {
            _selectedMtrlSrcRateStatusText = "Select MtrlSrc";
            RaiseComputed();
            return;
        }

        if (!TryParseUnitsPerSecond(_selectedMtrlSrcUnitsPerSecondText, out var unitsPerSecond, out var error))
        {
            _selectedMtrlSrcRateStatusText = error;
            RaiseComputed();
            return;
        }

        selected.MaterialUnitsPerSecond = unitsPerSecond;
        selected.MaterialId = MaterialCatalog.NormalizeId(_selectedMtrlSrcMaterialId);
        selected.MaterialGranulesPerPacket = NormalizeMtrlSrcGranulesPerPacket(_selectedMtrlSrcGranulesPerPacket);
        _selectedMtrlSrcUnitsPerSecondText = FormatRate(unitsPerSecond);
        _selectedMtrlSrcMaterialId = selected.MaterialId;
        _selectedMtrlSrcGranulesPerPacket = selected.MaterialGranulesPerPacket;
        _selectedMtrlSrcRateStatusText = FormatMtrlSrcStatus(selected.MaterialId, unitsPerSecond, selected.MaterialGranulesPerPacket);
        RefreshConveyorFlowTopology();
        EditorState.StatusMessage = $"MtrlSrc set to {selected.MaterialId} at {FormatRate(unitsPerSecond)} units/second, {selected.MaterialGranulesPerPacket} granules/packet";
        RaiseComputed();
    }

    private void NudgeSelectedMtrlSrcRate(float delta)
    {
        if (!TryGetSelectedMtrlSrc(out var selected))
        {
            _selectedMtrlSrcRateStatusText = "Select MtrlSrc";
            RaiseComputed();
            return;
        }

        var next = Math.Max(0.1f, selected.MaterialUnitsPerSecond + delta);
        _selectedMtrlSrcUnitsPerSecondText = FormatRate(next);
        ApplySelectedMtrlSrcRate();
    }


    private PlacementValidationResult SelectedPlacementValidation()
    {
        if (EditorState.SelectedObject is not { } selected)
        {
            return PlacementValidationResult.Valid;
        }

        return EditorState.ValidatePlacement(selected.Position, selected.EffectiveSize, selected.Id);
    }

    private void TryMoveSelectedObject(Func<SceneObject, VoxelCoord> createTarget)
    {
        if (EditorState.SelectedObject is not { } selected)
        {
            return;
        }

        var target = createTarget(selected);
        if (target == selected.Position)
        {
            return;
        }

        var validation = EditorState.ValidatePlacement(target, selected.EffectiveSize, selected.Id);
        if (!validation.IsValid)
        {
            EditorState.StatusMessage = $"Inspector blocked move: {validation.Reason ?? "invalid"}";
            RaiseComputed();
            return;
        }

        var deltaX = target.X - selected.Position.X;
        var deltaY = target.Y - selected.Position.Y;
        var deltaZ = target.Z - selected.Position.Z;
        selected.Position = target;
        if (selected.RouteStartCell is { } start)
        {
            selected.RouteStartCell = start with { X = start.X + deltaX, Y = start.Y + deltaY, Z = start.Z + deltaZ };
        }

        if (selected.RouteEndCell is { } end)
        {
            selected.RouteEndCell = end with { X = end.X + deltaX, Y = end.Y + deltaY, Z = end.Z + deltaZ };
        }

        RefreshConveyorFlowTopology();
        EditorState.StatusMessage = $"Inspector moved {selected.DisplayName} to {selected.Position}";
        RaiseComputed();
    }

    private string? GetSelectedPartColorHex()
    {
        if (EditorState.SelectedObject is not { } selected)
        {
            return null;
        }

        var part = EditorState.PartDefinitions.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, selected.PartId, StringComparison.OrdinalIgnoreCase));
        return part is null ? null : $"#{part.Color.R:X2}{part.Color.G:X2}{part.Color.B:X2}";
    }

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

    private RelayCommand ResolvePlacePartToolCommand(string partId)
    {
        if (_placePartToolCommandsByPartId.TryGetValue(partId, out var command))
        {
            return command;
        }

        return new RelayCommand(() => { });
    }

    private void ToggleCatalogBrowser()
    {
        _isCatalogBrowserOpen = !_isCatalogBrowserOpen;
        if (_isCatalogBrowserOpen)
        {
            SelectPartCatalogCategory(CatalogAllCategoryName);
            EditorState.StatusMessage = $"Catalog browser opened with {_partCatalogEntries.Count} searchable parts.";
        }
        else
        {
            EditorState.StatusMessage = "Catalog browser collapsed.";
        }

        OnPropertyChanged(nameof(IsCatalogBrowserOpen));
        OnPropertyChanged(nameof(BrowseMorePartsLabel));
        OnPropertyChanged(nameof(PartCatalogSummaryText));
        RaiseComputed();
    }

    private void SelectPartCatalogCategory(string category)
    {
        _selectedPartCategoryFilter = string.IsNullOrWhiteSpace(category) ? CatalogAllCategoryName : category;
        RebuildPartCatalogCategoryFilters();
        RebuildPartCatalogSections();
        OnPropertyChanged(nameof(PartCatalogSummaryText));
    }

    private void RebuildPartCatalogCategoryFilters()
    {
        PartCatalogCategoryFilters.Clear();
        PartCatalogCategoryFilters.Add(new PartCatalogCategoryFilterViewModel
        {
            Name = CatalogAllCategoryName,
            DisplayName = "All",
            Count = _partCatalogEntries.Count,
            IsSelected = string.Equals(_selectedPartCategoryFilter, CatalogAllCategoryName, StringComparison.OrdinalIgnoreCase),
            ApplyCommand = new RelayCommand(() => SelectPartCatalogCategory(CatalogAllCategoryName))
        });

        foreach (var category in _partCatalogEntries
            .Select(entry => entry.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(CatalogCategorySortIndex))
        {
            var categoryName = category;
            PartCatalogCategoryFilters.Add(new PartCatalogCategoryFilterViewModel
            {
                Name = categoryName,
                DisplayName = categoryName,
                Count = _partCatalogEntries.Count(entry => string.Equals(entry.Category, categoryName, StringComparison.OrdinalIgnoreCase)),
                IsSelected = string.Equals(_selectedPartCategoryFilter, categoryName, StringComparison.OrdinalIgnoreCase),
                ApplyCommand = new RelayCommand(() => SelectPartCatalogCategory(categoryName))
            });
        }
    }

    private string BuildPartCatalogSummaryText()
    {
        var visibleCount = PartCatalogSections.Sum(section => section.Items.Count);
        var categoryLabel = string.Equals(_selectedPartCategoryFilter, CatalogAllCategoryName, StringComparison.OrdinalIgnoreCase)
            ? "all concept categories"
            : _selectedPartCategoryFilter;
        var searchLabel = string.IsNullOrWhiteSpace(_partSearchText) ? "" : $" matching ‘{_partSearchText.Trim()}’";
        return $"Showing {visibleCount} of {_partCatalogEntries.Count} parts in {categoryLabel}{searchLabel}.";
    }

    private static int CatalogCategorySortIndex(string category)
    {
        var index = CatalogCategoryOrder
            .Select((name, position) => new { name, position })
            .FirstOrDefault(item => string.Equals(item.name, category, StringComparison.OrdinalIgnoreCase))?.position;
        return index ?? CatalogCategoryOrder.Count;
    }

    private void RebuildPartCatalogSections()
    {
        IEnumerable<PartCatalogMetadataEntry> source = _partCatalogEntries;
        if (!string.Equals(_selectedPartCategoryFilter, CatalogAllCategoryName, StringComparison.OrdinalIgnoreCase))
        {
            source = source.Where(entry => string.Equals(entry.Category, _selectedPartCategoryFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(_partSearchText))
        {
            var query = _partSearchText.Trim();
            source = source.Where(entry =>
                entry.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || entry.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
                || entry.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                || entry.VisualStyle.Contains(query, StringComparison.OrdinalIgnoreCase)
                || entry.Tags.Any(tag => tag.Contains(query, StringComparison.OrdinalIgnoreCase))
                || entry.SearchTerms.Any(term => term.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        var grouped = source
            .GroupBy(entry => entry.Category, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => CatalogCategorySortIndex(group.Key));

        PartCatalogSections.Clear();
        foreach (var group in grouped)
        {
            var items = group
                .Select(entry => new PartCatalogItemViewModel
                {
                    Id = entry.Id,
                    DisplayName = entry.DisplayName,
                    Category = entry.Category,
                    Description = entry.Description,
                    Tags = entry.Tags,
                    SearchTerms = entry.SearchTerms,
                    Thumbnail = entry.Thumbnail,
                    ToolType = entry.ToolType,
                    IsPlaceable = entry.IsPlaceable,
                    ActivateCommand = new RelayCommand(() => ActivatePartCatalogEntry(entry))
                })
                .ToList();

            PartCatalogSections.Add(new PartCatalogSectionViewModel
            {
                Name = group.Key.ToUpperInvariant(),
                Items = items
            });
        }
    }

    private void ActivatePartCatalogEntry(PartCatalogMetadataEntry entry)
    {
        if (entry.ToolType.Equals("route", StringComparison.OrdinalIgnoreCase))
        {
            SetTool(_conveyorRouteTool);
            return;
        }

        if (!entry.ToolType.Equals("place", StringComparison.OrdinalIgnoreCase))
        {
            EditorState.StatusMessage = $"{entry.DisplayName} is not available yet.";
            RaiseComputed();
            return;
        }

        if (_placePartToolCommandsByPartId.TryGetValue(entry.Id, out var command))
        {
            command.Execute(null);
            return;
        }

        EditorState.StatusMessage = $"{entry.DisplayName} has no placement tool yet.";
        RaiseComputed();
    }

    private void SetTool(IEditorTool tool)
    {
        EditorState.ActiveTool.OnCancel(EditorState);
        EditorState.ActiveTool = tool;
        EditorState.ClearMoveState();
        EditorState.ClearSelectionRotationMode();
        if (tool is SelectTool)
        {
            EditorState.ActivePlacementRotationZDegrees = 0;
        }

        EditorState.StatusMessage = "Ready";
        RaiseComputed();
    }

    private void SetActiveViewMode(string viewMode)
    {
        if (string.Equals(_activeViewMode, viewMode, StringComparison.Ordinal))
        {
            return;
        }

        _activeViewMode = viewMode;
        EditorState.StatusMessage = $"{viewMode} view selected.";
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

    private void SetViewportMode(bool useOpenGl)
    {
        if (_useOpenGlViewport == useOpenGl)
        {
            return;
        }

        _useOpenGlViewport = useOpenGl;
        RaiseComputed();
    }

    private void SetViewportVisualMode(ViewportVisualMode visualMode)
    {
        if (EditorState.ViewportVisualMode == visualMode)
        {
            return;
        }

        EditorState.ViewportVisualMode = visualMode;
        EditorState.StatusMessage = $"Viewport visual mode set to {ViewportVisualModeLabel}.";
        RaiseComputed();
    }

    private PartDefinition ResolvePartDefinition(string partId)
    {
        if (TryResolvePartDefinition(partId) is { } definition)
        {
            return definition;
        }

        throw new InvalidDataException($"Unknown part id '{partId}'.");
    }

    private PartDefinition? TryResolvePartDefinition(string partId)
    {
        if (string.IsNullOrWhiteSpace(partId))
        {
            return null;
        }

        return EditorState.PartDefinitions.FirstOrDefault(part =>
            string.Equals(part.Id, partId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(part.DisplayName, partId, StringComparison.OrdinalIgnoreCase));
    }

    private PartDefinition? TryResolvePartDefinition(SceneObject sceneObject, out string partId)
    {
        if (TryResolvePartDefinition(sceneObject.PartId) is { } byId)
        {
            partId = byId.Id;
            return byId;
        }

        if (TryResolvePartDefinition(sceneObject.PartType) is { } byType)
        {
            partId = byType.Id;
            return byType;
        }

        partId = string.Empty;
        return null;
    }


    private static SceneFileObjectData DemoObject(
        string partId,
        string displayName,
        int x,
        int y,
        int z,
        int rotationZDegrees = 0,
        string? materialId = null,
        float unitsPerSecond = 0f,
        int granulesPerPacket = 0,
        VoxelSize? sizeOverride = null)
        => new()
        {
            PartId = partId,
            DisplayName = displayName,
            Position = new VoxelCoord(x, y, z),
            RotationZDegrees = rotationZDegrees,
            MaterialId = materialId,
            MaterialUnitsPerSecond = unitsPerSecond,
            MaterialGranulesPerPacket = granulesPerPacket,
            SizeOverride = sizeOverride
        };

    private static SceneFileObjectData DemoRouteSegment(string displayName, int startX, int startY, int z, int endX, int endY)
    {
        var minX = Math.Min(startX, endX);
        var minY = Math.Min(startY, endY);
        var width = Math.Abs(endX - startX) + 1;
        var depth = Math.Abs(endY - startY) + 1;

        return new SceneFileObjectData
        {
            PartId = "conveyor",
            DisplayName = displayName,
            Position = new VoxelCoord(minX, minY, z),
            SizeOverride = new VoxelSize(width, depth, 1),
            RotationZDegrees = startX == endX
                ? (endY > startY ? 90 : 270)
                : (endX > startX ? 0 : 180),
            RouteStartCell = new VoxelCoord(startX, startY, z),
            RouteEndCell = new VoxelCoord(endX, endY, z)
        };
    }

    private void RotateAction()
    {
        if (EditorState.ActiveTool is PlacePartTool placeTool)
        {
            EditorState.ClearRotationAxis();
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
        if (EditorState.IsSelectionRotationMode)
        {
            return;
        }

        EditorState.StartSelectionRotation(selected);
        EditorState.StatusMessage = "Rotate";
    }

    private void ResetToSelectAndClearSelection()
    {
        if (EditorState.ActiveTool is not SelectTool)
        {
            SetTool(_selectTool);
        }
        else
        {
            EditorState.ActiveTool.OnCancel(EditorState);
            EditorState.ClearMoveState();
            EditorState.ClearSelectionRotationMode();
        }

        EditorState.SelectedObject = null;
        EditorState.HoveredObject = null;
        EditorState.GhostPreview = null;
        EditorState.ActiveConveyorRoute = null;
        EditorState.StatusMessage = "Ready";
    }

    private void DeleteSelection()
    {
        if (EditorState.SelectedObject is null)
        {
            return;
        }

        EditorState.ClearRotationAxis();
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
        if (EditorState.ActiveTool is not SelectTool
            || EditorState.SelectedObject is null
            || EditorState.IsMovingSelection
            || EditorState.IsSelectionRotationMode)
        {
            return;
        }

        EditorState.ClearRotationAxis();
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
        if (EditorState.IsSelectionRotationMode)
        {
            EditorState.ClearSelectionRotationMode();
            EditorState.StatusMessage = "Ready";
            return;
        }

        if (EditorState.IsMovingSelection)
        {
            if (EditorState.SelectedObject is { } selected && EditorState.MoveOriginalPosition.HasValue)
            {
                selected.Position = EditorState.MoveOriginalPosition.Value;
            }

            EditorState.ClearMoveState();
            EditorState.ClearRotationAxis();
            EditorState.StatusMessage = "Ready";
            return;
        }

        if (EditorState.ActiveTool is PlacePartTool)
        {
            SetTool(_selectTool);
            EditorState.GhostPreview = null;
            return;
        }

        if (EditorState.ActiveTool is ConveyorRouteTool routeTool)
        {
            if (EditorState.ActiveConveyorRoute is not null)
            {
                routeTool.OnCancel(EditorState);
                EditorState.StatusMessage = "Route canceled";
                return;
            }

            SetTool(_selectTool);
            return;
        }

        if (EditorState.ActiveTool is SelectTool && EditorState.SelectedObject is not null)
        {
            EditorState.SelectedObject = null;
            EditorState.ClearRotationAxis();
            EditorState.StatusMessage = "Ready";
        }
    }

    private void OnEditorStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorState.SelectedObject))
        {
            SyncSceneTreeSelection();
        }

        SyncSelectedMtrlSrcEditor();
        RaiseComputed();
    }

    private void OnSceneObjectsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshConveyorFlowTopology();
        EnsureDisplayNames();
        RebuildSceneTree();
        SyncSelectedMtrlSrcEditor();
        RaiseComputed();
    }

    private void RaiseComputed()
    {
        OnPropertyChanged(nameof(IsSelectActive));
        OnPropertyChanged(nameof(IsMoveActionActive));
        OnPropertyChanged(nameof(IsRotateActionActive));
        OnPropertyChanged(nameof(IsHopperActive));
        OnPropertyChanged(nameof(IsBinActive));
        OnPropertyChanged(nameof(IsConveyorRouteActive));
        OnPropertyChanged(nameof(IsChuteActive));
        OnPropertyChanged(nameof(IsTallHopperActive));
        OnPropertyChanged(nameof(IsMtrlSrcActive));
        OnPropertyChanged(nameof(IsMtrlRecvActive));
        OnPropertyChanged(nameof(IsSplitterActive));
        OnPropertyChanged(nameof(IsLiftActive));
        OnPropertyChanged(nameof(IsTurnActive));
        OnPropertyChanged(nameof(IsMergeActive));
        OnPropertyChanged(nameof(IsSupportActive));
        OnPropertyChanged(nameof(IsIsoViewActive));
        OnPropertyChanged(nameof(IsTopViewActive));
        OnPropertyChanged(nameof(IsFrontViewActive));
        OnPropertyChanged(nameof(IsRightViewActive));
        OnPropertyChanged(nameof(IsFloor0Active));
        OnPropertyChanged(nameof(IsFloor1Active));
        OnPropertyChanged(nameof(IsFloor2Active));
        OnPropertyChanged(nameof(IsLayer0Active));
        OnPropertyChanged(nameof(IsLayer1Active));
        OnPropertyChanged(nameof(IsLayer2Active));
        OnPropertyChanged(nameof(IsBlenderLikeSettingsActive));
        OnPropertyChanged(nameof(IsAutoCadLikeSettingsActive));
        OnPropertyChanged(nameof(IsSoftwareViewportMode));
        OnPropertyChanged(nameof(IsOpenGlViewportMode));
        OnPropertyChanged(nameof(IsPresentationViewportVisualMode));
        OnPropertyChanged(nameof(IsTechnicalViewportVisualMode));
        OnPropertyChanged(nameof(IsExpertMode));
        OnPropertyChanged(nameof(IsSimpleMode));
        OnPropertyChanged(nameof(InteractionPreset));
        OnPropertyChanged(nameof(SceneViewportColumnSpan));
        OnPropertyChanged(nameof(LayerDisplayText));
        OnPropertyChanged(nameof(AbsoluteZDisplayText));
        OnPropertyChanged(nameof(ViewportSummaryText));
        OnPropertyChanged(nameof(ViewportVisualModeLabel));
        OnPropertyChanged(nameof(ViewportPanelBackground));
        OnPropertyChanged(nameof(ViewportPanelBorderBrush));
        OnPropertyChanged(nameof(ViewportSurfaceBackground));
        OnPropertyChanged(nameof(ViewportToolbarBackground));
        OnPropertyChanged(nameof(ViewportToolbarBorderBrush));
        OnPropertyChanged(nameof(ViewportToolbarTextBrush));
        OnPropertyChanged(nameof(ViewportToolbarControlBrush));
        OnPropertyChanged(nameof(ViewportToolbarSeparatorBrush));
        OnPropertyChanged(nameof(ViewportToolbarSubtleTextBrush));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsCatalogBrowserOpen));
        OnPropertyChanged(nameof(BrowseMorePartsLabel));
        OnPropertyChanged(nameof(PartCatalogSummaryText));
        OnPropertyChanged(nameof(InspectorSelectedText));
        OnPropertyChanged(nameof(InspectorIdText));
        OnPropertyChanged(nameof(InspectorPositionText));
        OnPropertyChanged(nameof(InspectorSizeText));
        OnPropertyChanged(nameof(InspectorRotationText));
        OnPropertyChanged(nameof(InspectorStatusText));
        OnPropertyChanged(nameof(InspectorContextText));
        OnPropertyChanged(nameof(InspectorRangeText));
        OnPropertyChanged(nameof(InspectorExtraText));
        OnPropertyChanged(nameof(HasSelectedObject));
        OnPropertyChanged(nameof(SelectedObjectThumbnailImage));
        OnPropertyChanged(nameof(SelectedObjectName));
        OnPropertyChanged(nameof(InspectorPositionX));
        OnPropertyChanged(nameof(InspectorPositionY));
        OnPropertyChanged(nameof(InspectorPositionZ));
        OnPropertyChanged(nameof(InspectorRotationZDegrees));
        OnPropertyChanged(nameof(InspectorBaseSizeValue));
        OnPropertyChanged(nameof(InspectorEffectiveSizeValue));
        OnPropertyChanged(nameof(InspectorPartTypeText));
        OnPropertyChanged(nameof(InspectorValidityText));
        OnPropertyChanged(nameof(InspectorValidityDetail));
        OnPropertyChanged(nameof(InspectorValidityBrush));
        OnPropertyChanged(nameof(InspectorConveyorLengthText));
        OnPropertyChanged(nameof(InspectorConveyorRangeText));
        OnPropertyChanged(nameof(InspectorConveyorDirectionEditable));
        OnPropertyChanged(nameof(InspectorConveyorFlowReversed));
        OnPropertyChanged(nameof(InspectorTransferBehaviorText));
        OnPropertyChanged(nameof(InspectorPaintText));
        OnPropertyChanged(nameof(InspectorAccentColorText));
        OnPropertyChanged(nameof(InspectorDiagnosticPositionText));
        OnPropertyChanged(nameof(InspectorDiagnosticSizeText));
        OnPropertyChanged(nameof(InspectorDiagnosticEffectiveSizeText));
        OnPropertyChanged(nameof(InspectorDiagnosticRotationText));
        OnPropertyChanged(nameof(InspectorDiagnosticZSpanText));
        OnPropertyChanged(nameof(InspectorDiagnosticRangeText));
        OnPropertyChanged(nameof(InspectorDiagnosticExtraText));
        OnPropertyChanged(nameof(ShowConveyorDebug));
        OnPropertyChanged(nameof(ShowGrid));
        OnPropertyChanged(nameof(ShowBounds));
        OnPropertyChanged(nameof(ShowFlow));
        OnPropertyChanged(nameof(SelectedMtrlSrcPanelVisible));
        OnPropertyChanged(nameof(AvailableMtrlSrcMaterialIds));
        OnPropertyChanged(nameof(AvailableMtrlSrcGranuleCounts));
        OnPropertyChanged(nameof(SelectedMtrlSrcUnitsPerSecondText));
        OnPropertyChanged(nameof(SelectedMtrlSrcMaterialId));
        OnPropertyChanged(nameof(SelectedMtrlSrcGranulesPerPacket));
        OnPropertyChanged(nameof(SelectedMtrlSrcRateStatusText));
        OnPropertyChanged(nameof(InspectorPortText));
        OnPropertyChanged(nameof(InspectorMaterialFlowText));
        OnPropertyChanged(nameof(FloatingDebugPanelVisible));
        OnPropertyChanged(nameof(ExpertInspectorVisible));
        OnPropertyChanged(nameof(ExpertGpuLabelsVisible));
        OnPropertyChanged(nameof(ConveyorDebugSelectionText));
        OnPropertyChanged(nameof(ConveyorDebugRouteSummaryText));
        OnPropertyChanged(nameof(ConveyorDebugGridText));
        OnPropertyChanged(nameof(ConveyorDebugLegendText));
        OnPropertyChanged(nameof(ConveyorRoutePanelVisible));
        OnPropertyChanged(nameof(ConveyorRouteAnchorsValue));
        OnPropertyChanged(nameof(ConveyorRouteZValue));
        OnPropertyChanged(nameof(ConveyorRoutePreviewValue));
        OnPropertyChanged(nameof(ConveyorRouteBlockedReasonValue));
        OnPropertyChanged(nameof(Floor2StackText));
        OnPropertyChanged(nameof(Floor1StackText));
        OnPropertyChanged(nameof(Floor0StackText));
        OnPropertyChanged(nameof(ActiveFloorSummaryText));
        OnPropertyChanged(nameof(ActiveLayerSummaryText));
        OnPropertyChanged(nameof(ActiveAbsoluteZSummaryText));
        OnPropertyChanged(nameof(OpenGlBackendSummaryText));
        OnPropertyChanged(nameof(PreferredOpenGlGpuSummaryText));
        OnPropertyChanged(nameof(Floor2StackBackground));
        OnPropertyChanged(nameof(Floor1StackBackground));
        OnPropertyChanged(nameof(Floor0StackBackground));
        OnPropertyChanged(nameof(Floor2StackBorder));
        OnPropertyChanged(nameof(Floor1StackBorder));
        OnPropertyChanged(nameof(Floor0StackBorder));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static string StatusLabel(PortConnectionStatus status) => status switch
    {
        PortConnectionStatus.Connected => "Connected",
        PortConnectionStatus.InvalidNearby => "Invalid",
        PortConnectionStatus.AdapterRequired => "AdapterReq",
        _ => "Open"
    };

    private static string ShortId(Guid id) => id.ToString("N")[..8];

    private void RefreshConveyorFlowTopology()
    {
        var snapshot = EditorState.GetPortConnectivitySnapshot();
        EditorState.Scene.ConveyorRouteFlow.Update(0f, snapshot, EditorState.Scene.Objects);
    }

    private void SyncSelectedMtrlSrcEditor()
    {
        if (TryGetSelectedMtrlSrc(out var selected))
        {
            var formatted = FormatRate(selected.MaterialUnitsPerSecond);
            if (!string.Equals(_selectedMtrlSrcUnitsPerSecondText, formatted, StringComparison.Ordinal))
            {
                _selectedMtrlSrcUnitsPerSecondText = formatted;
                OnPropertyChanged(nameof(SelectedMtrlSrcUnitsPerSecondText));
            }

            var materialId = MaterialCatalog.NormalizeId(selected.MaterialId);
            if (!string.Equals(_selectedMtrlSrcMaterialId, materialId, StringComparison.Ordinal))
            {
                _selectedMtrlSrcMaterialId = materialId;
                OnPropertyChanged(nameof(SelectedMtrlSrcMaterialId));
            }

            var granulesPerPacket = NormalizeMtrlSrcGranulesPerPacket(selected.MaterialGranulesPerPacket);
            if (_selectedMtrlSrcGranulesPerPacket != granulesPerPacket)
            {
                _selectedMtrlSrcGranulesPerPacket = granulesPerPacket;
                OnPropertyChanged(nameof(SelectedMtrlSrcGranulesPerPacket));
            }

            var status = FormatMtrlSrcStatus(materialId, selected.MaterialUnitsPerSecond, granulesPerPacket);
            if (!string.Equals(_selectedMtrlSrcRateStatusText, status, StringComparison.Ordinal))
            {
                _selectedMtrlSrcRateStatusText = status;
                OnPropertyChanged(nameof(SelectedMtrlSrcRateStatusText));
            }

            return;
        }

        const string idleText = "1";
        if (!string.Equals(_selectedMtrlSrcUnitsPerSecondText, idleText, StringComparison.Ordinal))
        {
            _selectedMtrlSrcUnitsPerSecondText = idleText;
            OnPropertyChanged(nameof(SelectedMtrlSrcUnitsPerSecondText));
        }

        if (!string.Equals(_selectedMtrlSrcMaterialId, SceneObject.DefaultMaterialId, StringComparison.Ordinal))
        {
            _selectedMtrlSrcMaterialId = SceneObject.DefaultMaterialId;
            OnPropertyChanged(nameof(SelectedMtrlSrcMaterialId));
        }

        if (_selectedMtrlSrcGranulesPerPacket != SceneObject.DefaultMaterialGranulesPerPacket)
        {
            _selectedMtrlSrcGranulesPerPacket = SceneObject.DefaultMaterialGranulesPerPacket;
            OnPropertyChanged(nameof(SelectedMtrlSrcGranulesPerPacket));
        }

        if (!string.Equals(_selectedMtrlSrcRateStatusText, "Select MtrlSrc", StringComparison.Ordinal))
        {
            _selectedMtrlSrcRateStatusText = "Select MtrlSrc";
            OnPropertyChanged(nameof(SelectedMtrlSrcRateStatusText));
        }
    }

    private bool TryGetSelectedMtrlSrc(out SceneObject selected)
    {
        if (EditorState.SelectedObject is { } source
            && string.Equals(source.PartId, "mtrlsrc", StringComparison.OrdinalIgnoreCase))
        {
            selected = source;
            return true;
        }

        selected = null!;
        return false;
    }

    private bool TryGetSelectedConveyorRoute(out ConveyorRouteRuntime route, out HashSet<VoxelCoord> selectedCells)
    {
        selectedCells = [];
        if (EditorState.SelectedObject is not { } selected || !selected.IsConveyor)
        {
            route = null!;
            return false;
        }

        var allCells = Mhs.Editor.Viewport.ConveyorRouteCellVisualization.BuildSceneObjectCells(EditorState.Scene.Objects);
        if (allCells.TryGetValue(selected.Id, out var cells))
        {
            selectedCells = cells.Select(cell => cell.Position).ToHashSet();
        }

        var matchedRoute = EditorState.Scene.ConveyorRouteFlow.Routes
            .FirstOrDefault(candidate => candidate.SegmentObjectIds.Contains(selected.Id));
        if (matchedRoute is null)
        {
            route = null!;
            return false;
        }

        route = matchedRoute;
        return true;
    }

    private static string BuildConveyorGridText(ConveyorRouteRuntime route, HashSet<VoxelCoord> selectedCells)
    {
        var allCells = route.Cells;
        var minX = allCells.Min(cell => cell.X);
        var maxX = allCells.Max(cell => cell.X);
        var minY = allCells.Min(cell => cell.Y);
        var maxY = allCells.Max(cell => cell.Y);
        var indexByCell = allCells
            .Select((cell, index) => new { cell, index })
            .ToDictionary(item => item.cell, item => item.index);

        var lines = new List<string>
        {
            $"     {string.Join(" ", Enumerable.Range(minX, maxX - minX + 1).Select(x => $" X{x,2}"))}"
        };

        for (var y = maxY; y >= minY; y--)
        {
            var row = new List<string> { $"Y{y,2} " };
            for (var x = minX; x <= maxX; x++)
            {
                var coord = new VoxelCoord(x, y, allCells[0].Z);
                if (!indexByCell.TryGetValue(coord, out var index))
                {
                    row.Add("[..]");
                    continue;
                }

                var token = route.Slots[index] is not null
                    ? "##"
                    : index.ToString("00", CultureInfo.InvariantCulture);
                if (selectedCells.Contains(coord))
                {
                    token = route.Slots[index] is not null ? "S#" : $"S{index % 10}";
                }

                row.Add($"[{token}]");
            }

            lines.Add(string.Join(" ", row));
        }

        lines.Add($"Z {allCells[0].Z} | start {allCells[0]} | end {allCells[^1]}");
        if (route.InputAttachments.Count > 0)
        {
            lines.Add("Inputs:");
            lines.AddRange(route.InputAttachments
                .OrderBy(attachment => attachment.RouteCellIndex)
                .ThenBy(attachment => attachment.ObjectId)
                .Select(attachment =>
                {
                    var targetCell = attachment.RouteCellIndex >= 0 && attachment.RouteCellIndex < route.Cells.Count
                        ? route.Cells[attachment.RouteCellIndex].ToString()
                        : "?";
                    return $"  src {ShortId(attachment.ObjectId)} mat={MaterialCatalog.NormalizeId(attachment.MaterialId)} cell={attachment.RouteCellIndex} @ {targetCell} rate={attachment.UnitsPerSecond.ToString("0.##", CultureInfo.InvariantCulture)} granules={attachment.GranulesPerPacket} state={FormatAttachmentStatus(attachment.LastStatus)}";
                }));
        }
        else
        {
            lines.Add("Inputs: none");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatAttachmentStatus(RouteInputAttachmentStatus status) => status switch
    {
        RouteInputAttachmentStatus.WaitingForSlot => "blocked",
        RouteInputAttachmentStatus.WaitingForTurn => "waiting-turn",
        RouteInputAttachmentStatus.Injected => "injected",
        _ => "waiting-rate"
    };

    private static bool TryParseUnitsPerSecond(string text, out float unitsPerSecond, out string error)
    {
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out unitsPerSecond))
        {
            error = "Enter a numeric units/second value";
            return false;
        }

        if (unitsPerSecond <= 0f)
        {
            error = "Units/second must be above 0";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static float ResolveMaterialUnitsPerSecond(string partId, float storedUnitsPerSecond)
    {
        if (string.Equals(partId, "mtrlsrc", StringComparison.OrdinalIgnoreCase))
        {
            return storedUnitsPerSecond > 0f
                ? storedUnitsPerSecond
                : SceneObject.DefaultMaterialUnitsPerSecond;
        }

        return Math.Max(0f, storedUnitsPerSecond);
    }

    private static int ResolveMaterialGranulesPerPacket(string partId, int storedGranulesPerPacket)
        => string.Equals(partId, "mtrlsrc", StringComparison.OrdinalIgnoreCase)
            ? NormalizeMtrlSrcGranulesPerPacket(storedGranulesPerPacket)
            : SceneObject.DefaultMaterialGranulesPerPacket;

    private static int NormalizeMtrlSrcGranulesPerPacket(int granulesPerPacket)
        => granulesPerPacket switch
        {
            1 or 5 or 10 or 50 or 100 => granulesPerPacket,
            _ => SceneObject.DefaultMaterialGranulesPerPacket
        };

    private static string FormatMtrlSrcStatus(string materialId, float unitsPerSecond, int granulesPerPacket)
        => $"Producing {materialId} at {FormatRate(unitsPerSecond)} units/second, {granulesPerPacket} granules/packet";

    private static string ResolveMaterialId(string partId, string? storedMaterialId)
    {
        if (string.Equals(partId, "mtrlsrc", StringComparison.OrdinalIgnoreCase))
        {
            return MaterialCatalog.NormalizeId(storedMaterialId);
        }

        return string.Empty;
    }

    private static string FormatRate(float unitsPerSecond)
        => unitsPerSecond.ToString("0.##", CultureInfo.InvariantCulture);

    private void RebuildSceneTree()
    {
        SceneTreeNodes.Clear();
        var routes = EditorState.Scene.ConveyorRouteFlow.Routes;
        var conveyorRouteNodes = BuildConveyorRouteNodes(routes);
        var routeNamesByObjectId = conveyorRouteNodes
            .SelectMany(node => node.SceneObjectIds.Select(id => KeyValuePair.Create(id, node.DisplayName)))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        for (var floor = 0; floor < WorldVerticalSettings.FloorCount; floor++)
        {
            var floorStart = floor * WorldVerticalSettings.LayersPerFloor;
            var floorEnd = floorStart + WorldVerticalSettings.LayersPerFloor - 1;

            var floorObjects = EditorState.Scene.Objects
                .Where(o => EditorState.IntersectsFloor(o, floorStart, floorEnd))
                .ToList();

            if (floorObjects.Count == 0)
            {
                continue;
            }

            SceneTreeNodes.Add(new SceneTreeNodeViewModel($"Floor {floor}", string.Empty, isGroupHeader: true, indentLevel: 0));

            AddConveyorTreeGroup(conveyorRouteNodes.Where(node => node.Floor == floor));
            AddTreeGroup("Material Sources", floorObjects.Where(o => string.Equals(o.PartId, "mtrlsrc", StringComparison.OrdinalIgnoreCase)), obj => CreateSourceTreeNode(obj, routes, routeNamesByObjectId));
            AddTreeGroup("Material Receivers", floorObjects.Where(o => string.Equals(o.PartId, "mtrlrecv", StringComparison.OrdinalIgnoreCase)), obj => CreateReceiverTreeNode(obj, routes, routeNamesByObjectId));
            AddTreeGroup("Equipment", floorObjects.Where(IsEquipmentObject), CreateEquipmentTreeNode);
            AddTreeGroup("Other", floorObjects.Where(o =>
                !o.IsConveyor
                && !string.Equals(o.PartId, "mtrlsrc", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(o.PartId, "mtrlrecv", StringComparison.OrdinalIgnoreCase)
                && !IsEquipmentObject(o)), CreateEquipmentTreeNode);
        }

        SyncSceneTreeSelection();
    }

    private void AddConveyorTreeGroup(IEnumerable<ConveyorRouteTreeNode> nodes)
    {
        var list = nodes.OrderBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count == 0)
        {
            return;
        }

        SceneTreeNodes.Add(new SceneTreeNodeViewModel("Conveyors", string.Empty, isGroupHeader: true, indentLevel: 1));
        foreach (var node in list)
        {
            SceneTreeNodes.Add(new SceneTreeNodeViewModel(
                $"{node.DisplayName} — {node.CellCount} {(node.CellCount == 1 ? "cell" : "cells")}",
                string.Empty,
                isGroupHeader: false,
                indentLevel: 2,
                sceneObject: node.RepresentativeObject,
                relatedSceneObjectIds: node.SceneObjectIds,
                focusWorldX: node.FocusWorldX,
                focusWorldY: node.FocusWorldY,
                focusWorldZ: node.FocusWorldZ));
        }
    }

    private void AddTreeGroup(string groupName, IEnumerable<SceneObject> objects, Func<SceneObject, SceneTreeNodeViewModel> createNode)
    {
        var list = objects
            .OrderBy(obj => obj.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(obj => obj.Position.Z)
            .ThenBy(obj => obj.Position.Y)
            .ThenBy(obj => obj.Position.X)
            .ToList();
        if (list.Count == 0)
        {
            return;
        }

        SceneTreeNodes.Add(new SceneTreeNodeViewModel(groupName, string.Empty, isGroupHeader: true, indentLevel: 1));
        foreach (var obj in list)
        {
            SceneTreeNodes.Add(createNode(obj));
        }
    }

    private void SyncSceneTreeSelection()
    {
        SceneTreeNodeViewModel? newNode = null;
        if (EditorState.SelectedObject is { } selected)
        {
            foreach (var node in SceneTreeNodes)
            {
                if (node.Matches(selected))
                {
                    newNode = node;
                    break;
                }
            }
        }

        if (_selectedSceneTreeNode == newNode)
        {
            return;
        }

        _selectedSceneTreeNode = newNode;
        OnPropertyChanged(nameof(SelectedSceneTreeNode));
    }

    public bool FocusSelectedObject(Rect viewportBounds)
        => FocusSceneTreeNode(SelectedSceneTreeNode, viewportBounds)
           || FocusObject(EditorState.SelectedObject, viewportBounds);

    public bool FocusSceneTreeNode(SceneTreeNodeViewModel? node, Rect viewportBounds)
    {
        if (node?.SceneObject is null)
        {
            return false;
        }

        if (EditorState.ActiveTool is not SelectTool)
        {
            SetTool(_selectTool);
        }

        var sceneObject = node.SceneObject;
        EditorState.ActiveFloor = WorldVerticalSettings.ToFloor(sceneObject.Position.Z);
        EditorState.ActiveLayer = WorldVerticalSettings.ToLayer(sceneObject.Position.Z);
        EditorState.SelectedObject = sceneObject;

        var focusX = node.FocusWorldX ?? (sceneObject.Position.X + sceneObject.EffectiveSize.WidthX * 0.5);
        var focusY = node.FocusWorldY ?? (sceneObject.Position.Y + sceneObject.EffectiveSize.DepthY * 0.5);
        var focusZ = node.FocusWorldZ ?? (sceneObject.Position.Z + sceneObject.EffectiveSize.HeightZ * 0.5);
        ViewportMath.CenterViewOn(EditorState, viewportBounds, focusX, focusY, focusZ);
        RaiseComputed();
        return true;
    }

    private bool FocusObject(SceneObject? sceneObject, Rect viewportBounds)
    {
        if (sceneObject is null)
        {
            return false;
        }

        var focusX = sceneObject.Position.X + sceneObject.EffectiveSize.WidthX * 0.5;
        var focusY = sceneObject.Position.Y + sceneObject.EffectiveSize.DepthY * 0.5;
        var focusZ = sceneObject.Position.Z + sceneObject.EffectiveSize.HeightZ * 0.5;
        return FocusSceneTreeNode(new SceneTreeNodeViewModel(
            sceneObject.DisplayName,
            string.Empty,
            isGroupHeader: false,
            sceneObject: sceneObject,
            focusWorldX: focusX,
            focusWorldY: focusY,
            focusWorldZ: focusZ), viewportBounds);
    }

    private void EnsureDisplayNames()
    {
        AssignConveyorDisplayNames();

        foreach (var obj in EditorState.Scene.Objects.Where(obj => !obj.IsConveyor && string.IsNullOrWhiteSpace(obj.DisplayName)))
        {
            obj.DisplayName = CreateGeneratedDisplayName(obj);
        }
    }

    private void AssignConveyorDisplayNames()
    {
        var groups = BuildConveyorDisplayGroups();
        var usedIndices = new HashSet<int>();
        foreach (var name in groups
                     .Select(group => group.Select(obj => obj.DisplayName?.Trim()).FirstOrDefault(displayName => !string.IsNullOrWhiteSpace(displayName)))
                     .Where(name => !string.IsNullOrWhiteSpace(name)))
        {
            if (name!.StartsWith("Conveyor R", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(name["Conveyor R".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                && index > 0)
            {
                usedIndices.Add(index);
            }
        }

        var nextIndex = 1;
        foreach (var group in groups)
        {
            var existingName = group
                .Select(obj => obj.DisplayName?.Trim())
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
            if (string.IsNullOrWhiteSpace(existingName))
            {
                while (usedIndices.Contains(nextIndex))
                {
                    nextIndex++;
                }

                existingName = $"Conveyor R{nextIndex}";
                usedIndices.Add(nextIndex);
            }

            foreach (var sceneObject in group)
            {
                sceneObject.DisplayName = existingName;
            }
        }
    }

    private List<List<SceneObject>> BuildConveyorDisplayGroups()
    {
        var objectsById = EditorState.Scene.Objects.ToDictionary(obj => obj.Id);
        var groups = new List<List<SceneObject>>();
        var groupedIds = new HashSet<Guid>();

        foreach (var route in EditorState.Scene.ConveyorRouteFlow.Routes)
        {
            var group = route.SegmentObjectIds
                .Where(id => objectsById.ContainsKey(id))
                .Select(id => objectsById[id])
                .ToList();
            if (group.Count == 0)
            {
                continue;
            }

            groups.Add(group);
            foreach (var sceneObject in group)
            {
                groupedIds.Add(sceneObject.Id);
            }
        }

        foreach (var conveyor in EditorState.Scene.Objects.Where(obj => obj.IsConveyor && !groupedIds.Contains(obj.Id)))
        {
            groups.Add([conveyor]);
        }

        return groups;
    }

    private IEnumerable<SceneObject> GetObjectsSharingDisplayName(SceneObject selected)
    {
        if (!selected.IsConveyor)
        {
            return [selected];
        }

        var route = EditorState.Scene.ConveyorRouteFlow.Routes
            .FirstOrDefault(candidate => candidate.SegmentObjectIds.Contains(selected.Id));
        if (route is null)
        {
            return [selected];
        }

        var objectsById = EditorState.Scene.Objects.ToDictionary(obj => obj.Id);
        return route.SegmentObjectIds
            .Where(id => objectsById.ContainsKey(id))
            .Select(id => objectsById[id])
            .ToList();
    }

    private string NormalizeDisplayName(SceneObject sceneObject, string? value)
        => string.IsNullOrWhiteSpace(value)
            ? CreateGeneratedDisplayName(sceneObject)
            : value.Trim();

    private string CreateGeneratedDisplayName(SceneObject sceneObject)
    {
        if (sceneObject.IsConveyor)
        {
            var groups = BuildConveyorDisplayGroups();
            for (var i = 0; i < groups.Count; i++)
            {
                if (groups[i].Any(candidate => candidate.Id == sceneObject.Id))
                {
                    return $"Conveyor R{i + 1}";
                }
            }

            return "Conveyor R1";
        }

        if (string.Equals(sceneObject.PartId, "mtrlsrc", StringComparison.OrdinalIgnoreCase))
        {
            var index = 0;
            foreach (var candidate in EditorState.Scene.Objects.Where(obj => string.Equals(obj.PartId, "mtrlsrc", StringComparison.OrdinalIgnoreCase)))
            {
                index++;
                if (candidate.Id == sceneObject.Id)
                {
                    break;
                }
            }

            return $"MtrlSrc S{Math.Max(index, 1)}";
        }

        if (string.Equals(sceneObject.PartId, "mtrlrecv", StringComparison.OrdinalIgnoreCase))
        {
            var index = 0;
            foreach (var candidate in EditorState.Scene.Objects.Where(obj => string.Equals(obj.PartId, "mtrlrecv", StringComparison.OrdinalIgnoreCase)))
            {
                index++;
                if (candidate.Id == sceneObject.Id)
                {
                    break;
                }
            }

            return $"MtrlRecv Recv{Math.Max(index, 1)}";
        }

        var equipmentIndex = 0;
        foreach (var candidate in EditorState.Scene.Objects.Where(obj =>
                     !obj.IsConveyor
                     && !string.Equals(obj.PartId, "mtrlsrc", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(obj.PartId, "mtrlrecv", StringComparison.OrdinalIgnoreCase)
                     && string.Equals(obj.PartType, sceneObject.PartType, StringComparison.OrdinalIgnoreCase)))
        {
            equipmentIndex++;
            if (candidate.Id == sceneObject.Id)
            {
                break;
            }
        }

        return $"{sceneObject.PartType} {Math.Max(equipmentIndex, 1)}";
    }

    private static bool IsEquipmentObject(SceneObject obj)
        => obj.PartId is "hopper" or "bin" or "chute" or "tall_hopper";

    private SceneTreeNodeViewModel CreateSourceTreeNode(
        SceneObject obj,
        IReadOnlyList<ConveyorRouteRuntime> routes,
        IReadOnlyDictionary<Guid, string> routeNamesByObjectId)
    {
        var match = routes
            .SelectMany(route => route.InputAttachments.Select(attachment => (route, attachment)))
            .FirstOrDefault(x => x.attachment.ObjectId == obj.Id);
        var routeName = match.route is not null
            ? match.route.SegmentObjectIds.Select(id => routeNamesByObjectId.GetValueOrDefault(id)).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
            : null;
        var routeRef = string.IsNullOrWhiteSpace(routeName) ? "disconnected" : $"{MaterialCatalog.NormalizeId(obj.MaterialId)} -> {ShortRouteName(routeName)} cell {match.attachment.RouteCellIndex}";
        var label = string.IsNullOrWhiteSpace(routeName)
            ? $"{obj.DisplayName} — disconnected"
            : $"{obj.DisplayName} — {routeRef}";
        return CreateObjectTreeNode(obj, label);
    }

    private SceneTreeNodeViewModel CreateReceiverTreeNode(
        SceneObject obj,
        IReadOnlyList<ConveyorRouteRuntime> routes,
        IReadOnlyDictionary<Guid, string> routeNamesByObjectId)
    {
        var match = routes.FirstOrDefault(route => route.ReceiverObjectId == obj.Id);
        var routeName = match is not null
            ? match.SegmentObjectIds.Select(id => routeNamesByObjectId.GetValueOrDefault(id)).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
            : null;
        var label = string.IsNullOrWhiteSpace(routeName)
            ? $"{obj.DisplayName} — disconnected"
            : $"{obj.DisplayName} — <- {ShortRouteName(routeName)}";
        return CreateObjectTreeNode(obj, label);
    }

    private SceneTreeNodeViewModel CreateEquipmentTreeNode(SceneObject obj)
        => CreateObjectTreeNode(obj, obj.DisplayName);

    private SceneTreeNodeViewModel CreateObjectTreeNode(SceneObject obj, string label)
    {
        var focusX = obj.Position.X + obj.EffectiveSize.WidthX * 0.5;
        var focusY = obj.Position.Y + obj.EffectiveSize.DepthY * 0.5;
        var focusZ = obj.Position.Z + obj.EffectiveSize.HeightZ * 0.5;
        return new SceneTreeNodeViewModel(
            label,
            string.Empty,
            isGroupHeader: false,
            indentLevel: 2,
            sceneObject: obj,
            focusWorldX: focusX,
            focusWorldY: focusY,
            focusWorldZ: focusZ);
    }

    private List<ConveyorRouteTreeNode> BuildConveyorRouteNodes(IReadOnlyList<ConveyorRouteRuntime> routes)
    {
        var objectsById = EditorState.Scene.Objects.ToDictionary(obj => obj.Id);
        var cellsByObject = ConveyorRouteCellVisualization.BuildSceneObjectCells(EditorState.Scene.Objects);
        var nodes = new List<ConveyorRouteTreeNode>();
        var coveredIds = new HashSet<Guid>();

        foreach (var route in routes)
        {
            var routeObjects = route.SegmentObjectIds
                .Where(id => objectsById.ContainsKey(id))
                .Select(id => objectsById[id])
                .ToList();
            if (routeObjects.Count == 0)
            {
                continue;
            }

            var representative = routeObjects[0];
            var displayName = routeObjects
                .Select(obj => obj.DisplayName)
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
                ?? "Conveyor";
            var minX = route.Cells.Min(cell => cell.X);
            var maxX = route.Cells.Max(cell => cell.X);
            var minY = route.Cells.Min(cell => cell.Y);
            var maxY = route.Cells.Max(cell => cell.Y);
            var minZ = route.Cells.Min(cell => cell.Z);
            var maxZ = route.Cells.Max(cell => cell.Z);
            nodes.Add(new ConveyorRouteTreeNode(
                WorldVerticalSettings.ToFloor(minZ),
                representative,
                routeObjects.Select(obj => obj.Id).ToArray(),
                displayName,
                route.Cells.Count,
                (minX + maxX + 1) * 0.5,
                (minY + maxY + 1) * 0.5,
                (minZ + maxZ + 1) * 0.5));
            foreach (var sceneObject in routeObjects)
            {
                coveredIds.Add(sceneObject.Id);
            }
        }

        foreach (var conveyor in EditorState.Scene.Objects.Where(obj => obj.IsConveyor && !coveredIds.Contains(obj.Id)))
        {
            cellsByObject.TryGetValue(conveyor.Id, out var cells);
            var cellCount = cells?.Count ?? Math.Max(conveyor.EffectiveSize.WidthX, conveyor.EffectiveSize.DepthY);
            var focusX = conveyor.Position.X + conveyor.EffectiveSize.WidthX * 0.5;
            var focusY = conveyor.Position.Y + conveyor.EffectiveSize.DepthY * 0.5;
            var focusZ = conveyor.Position.Z + conveyor.EffectiveSize.HeightZ * 0.5;
            nodes.Add(new ConveyorRouteTreeNode(
                WorldVerticalSettings.ToFloor(conveyor.Position.Z),
                conveyor,
                [conveyor.Id],
                conveyor.DisplayName,
                cellCount,
                focusX,
                focusY,
                focusZ));
        }

        return nodes;
    }

    private static string ShortRouteName(string displayName)
        => displayName.StartsWith("Conveyor ", StringComparison.OrdinalIgnoreCase)
            ? displayName["Conveyor ".Length..]
            : displayName;

    private sealed record ConveyorRouteTreeNode(
        int Floor,
        SceneObject RepresentativeObject,
        IReadOnlyList<Guid> SceneObjectIds,
        string DisplayName,
        int CellCount,
        double FocusWorldX,
        double FocusWorldY,
        double FocusWorldZ);

    private sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute ?? (() => true);
        }

        public bool CanExecute(object? parameter) => _canExecute();

        public event EventHandler? CanExecuteChanged;

        public void Execute(object? parameter)
        {
            _execute();
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
