using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Input;
using Mhs.Editor.Editor;

namespace Mhs.Editor.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private const float MaterialFlowTickSeconds = 0.25f;
    private readonly IEditorTool _selectTool = new SelectTool();
    private readonly ConveyorRouteTool _conveyorRouteTool = new();
    private ViewportInteractionPreset _interactionPreset = ViewportInteractionPreset.BlenderLike;
    private bool _useOpenGlViewport = true;
    private string _preferredOpenGlGpuName = "System default GPU";

    public MainWindowViewModel()
    {
        EditorState = new EditorState();
        EditorState.PropertyChanged += OnEditorStatePropertyChanged;
        EditorState.Scene.Objects.CollectionChanged += OnSceneObjectsChanged;

        SelectToolCommand = new RelayCommand(() => SetTool(_selectTool));
        ConveyorRouteToolCommand = new RelayCommand(() => SetTool(_conveyorRouteTool));
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
        InjectDebugOreCommand = new RelayCommand(InjectDebugOre);
        StepMaterialFlowCommand = new RelayCommand(StepMaterialFlow);
        ClearMaterialFlowCommand = new RelayCommand(ClearMaterialFlow);

        foreach (var part in EditorState.PartDefinitions)
        {
            switch (part.Id)
            {
                case "hopper":
                    HopperToolCommand = new RelayCommand(() => SetTool(new PlacePartTool(part)));
                    break;
                case "bin":
                    BinToolCommand = new RelayCommand(() => SetTool(new PlacePartTool(part)));
                    break;
                case "chute":
                    ChuteToolCommand = new RelayCommand(() => SetTool(new PlacePartTool(part)));
                    break;
                case "tall_hopper":
                    TallHopperToolCommand = new RelayCommand(() => SetTool(new PlacePartTool(part)));
                    break;
                case "mtrlsrc":
                    MtrlSrcToolCommand = new RelayCommand(() => SetTool(new PlacePartTool(part)));
                    break;
                case "mtrlrecv":
                    MtrlRecvToolCommand = new RelayCommand(() => SetTool(new PlacePartTool(part)));
                    break;
            }
        }

        HopperToolCommand ??= new RelayCommand(() => { });
        BinToolCommand ??= new RelayCommand(() => { });
        ChuteToolCommand ??= new RelayCommand(() => { });
        TallHopperToolCommand ??= new RelayCommand(() => { });
        MtrlSrcToolCommand ??= new RelayCommand(() => { });
        MtrlRecvToolCommand ??= new RelayCommand(() => { });

        SetTool(_selectTool);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public EditorState EditorState { get; }

    public ICommand SelectToolCommand { get; }
    public ICommand ConveyorRouteToolCommand { get; }
    public ICommand HopperToolCommand { get; }
    public ICommand BinToolCommand { get; }
    public ICommand ChuteToolCommand { get; }
    public ICommand TallHopperToolCommand { get; }
    public ICommand MtrlSrcToolCommand { get; }
    public ICommand MtrlRecvToolCommand { get; }
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
    public ICommand InjectDebugOreCommand { get; }
    public ICommand StepMaterialFlowCommand { get; }
    public ICommand ClearMaterialFlowCommand { get; }

    public bool IsSelectActive => EditorState.ActiveTool is SelectTool;
    public bool IsHopperActive => EditorState.ActiveTool.Name == "Hopper";
    public bool IsBinActive => EditorState.ActiveTool.Name == "Bin";
    public bool IsConveyorRouteActive => EditorState.ActiveTool is ConveyorRouteTool;
    public bool IsChuteActive => EditorState.ActiveTool.Name == "Chute";
    public bool IsTallHopperActive => EditorState.ActiveTool.Name == "Tall Hopper";
    public bool IsMtrlSrcActive => EditorState.ActiveTool.Name == "MtrlSrc";
    public bool IsMtrlRecvActive => EditorState.ActiveTool.Name == "MtrlRecv";
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
    public ViewportInteractionPreset InteractionPreset => _interactionPreset;
    public bool UseOpenGlViewport => _useOpenGlViewport;
    public string PreferredOpenGlGpuName => _preferredOpenGlGpuName;
    public string LayerDisplayText => $"Layer: {EditorState.ActiveLayer} of {WorldVerticalSettings.LayersPerFloor - 1}";
    public string AbsoluteZDisplayText => $"Absolute Z: {EditorState.ActiveAbsoluteZ}";

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
            var openGlInfo = _useOpenGlViewport && !string.IsNullOrWhiteSpace(EditorState.OpenGlBackendInfo)
                ? $" | Preferred GPU: {PreferredOpenGlGpuName} | GL {EditorState.OpenGlBackendInfo}"
                : string.Empty;

            return $"{EditorState.StatusMessage} | {viewportMode}{openGlInfo} | Tool: {EditorState.ActiveTool.Name}{rotation} | Floor {EditorState.ActiveFloor} · Layer {EditorState.ActiveLayer}/{WorldVerticalSettings.LayersPerFloor - 1} · Z {EditorState.ActiveAbsoluteZ} | X {(EditorState.HoveredVoxel?.X.ToString() ?? "-")} Y {(EditorState.HoveredVoxel?.Y.ToString() ?? "-")} | Objects: {EditorState.Scene.Objects.Count} | {hotkeys}";
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
                var sender = route.SenderObjectId.HasValue ? ShortId(route.SenderObjectId.Value) : "-";
                var receiver = route.ReceiverObjectId.HasValue ? ShortId(route.ReceiverObjectId.Value) : "-";
                return $"R{index + 1}: {routeOccupied}/{route.Slots.Length} sender={sender} recv={receiver}";
            }));
            return $"Material Flow: {occupied} packet(s) on {routes.Count} route(s) | {details}";
        }
    }

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
        LoadSceneFileData(new SceneFileData());
        EditorState.StatusMessage = "New scene";
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
                Position = sceneObject.Position,
                RotationZDegrees = sceneObject.RotationZDegrees,
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
                Position = objectData.Position,
                BaseSize = baseSize,
                RotationZDegrees = RotationHelper.NormalizeDegrees(objectData.RotationZDegrees),
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
        EditorState.StatusMessage = injected
            ? $"Injected DebugOre from {ShortId(EditorState.SelectedObject.Id)}"
            : "Inject failed: source route input slot occupied or disconnected";
        RaiseComputed();
    }

    private void StepMaterialFlow()
    {
        var snapshot = EditorState.GetPortConnectivitySnapshot();
        var changed = EditorState.Scene.ConveyorRouteFlow.Step(snapshot, EditorState.Scene.Objects);
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
        EditorState.ClearSelectionRotationMode();
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

    private void SetViewportMode(bool useOpenGl)
    {
        if (_useOpenGlViewport == useOpenGl)
        {
            return;
        }

        _useOpenGlViewport = useOpenGl;
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
        RaiseComputed();
    }

    private void OnSceneObjectsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var snapshot = EditorState.GetPortConnectivitySnapshot();
        EditorState.Scene.ConveyorRouteFlow.Update(0f, snapshot, EditorState.Scene.Objects);
        RaiseComputed();
    }

    private void RaiseComputed()
    {
        OnPropertyChanged(nameof(IsSelectActive));
        OnPropertyChanged(nameof(IsHopperActive));
        OnPropertyChanged(nameof(IsBinActive));
        OnPropertyChanged(nameof(IsConveyorRouteActive));
        OnPropertyChanged(nameof(IsChuteActive));
        OnPropertyChanged(nameof(IsTallHopperActive));
        OnPropertyChanged(nameof(IsMtrlSrcActive));
        OnPropertyChanged(nameof(IsMtrlRecvActive));
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
        OnPropertyChanged(nameof(InspectorPortText));
        OnPropertyChanged(nameof(InspectorMaterialFlowText));
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
