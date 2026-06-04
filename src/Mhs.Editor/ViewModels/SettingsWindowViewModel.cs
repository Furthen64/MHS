using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Mhs.Editor.Editor;
using Mhs.Editor.Settings;

namespace Mhs.Editor.ViewModels;

public sealed class SettingsWindowViewModel
{
    public SettingsWindowViewModel(AppPreferences preferences, IReadOnlyList<GpuOption> gpuOptions)
    {
        AvailableGpus = new ObservableCollection<GpuOption>(gpuOptions);
        AvailableFloors = new ObservableCollection<int>(Enumerable.Range(0, WorldVerticalSettings.FloorCount));
        AvailableLayers = new ObservableCollection<int>(Enumerable.Range(0, WorldVerticalSettings.LayersPerFloor));
        AvailableRendererModes = new ObservableCollection<RendererModeOption>(
        [
            new RendererModeOption("OpenGL Spike", "opengl"),
            new RendererModeOption("Software", "software")
        ]);
        AvailableUiModes = new ObservableCollection<UiModeOption>(
        [
            new UiModeOption("Simple", "simple"),
            new UiModeOption("Expert", "expert")
        ]);

        OpenMaximized = preferences.OpenMaximized;
        DefaultFloor = ClampFloor(preferences.DefaultFloor);
        DefaultLayer = ClampLayer(preferences.DefaultLayer);
        SelectedGpu = AvailableGpus.FirstOrDefault(g => g.Name.Equals(preferences.PreferredOpenGlGpuName, System.StringComparison.OrdinalIgnoreCase))
            ?? AvailableGpus.FirstOrDefault();
        SelectedRendererMode = AvailableRendererModes.FirstOrDefault(mode =>
                mode.Value.Equals(preferences.DefaultRendererMode, System.StringComparison.OrdinalIgnoreCase))
            ?? AvailableRendererModes[0];
        SelectedUiMode = AvailableUiModes.FirstOrDefault(mode =>
                mode.Value.Equals(preferences.UiMode, System.StringComparison.OrdinalIgnoreCase))
            ?? AvailableUiModes[0];
    }

    public ObservableCollection<GpuOption> AvailableGpus { get; }
    public ObservableCollection<int> AvailableFloors { get; }
    public ObservableCollection<int> AvailableLayers { get; }
    public ObservableCollection<RendererModeOption> AvailableRendererModes { get; }
    public ObservableCollection<UiModeOption> AvailableUiModes { get; }
    public bool OpenMaximized { get; set; }
    public int DefaultFloor { get; set; }
    public int DefaultLayer { get; set; }
    public GpuOption? SelectedGpu { get; set; }
    public RendererModeOption? SelectedRendererMode { get; set; }
    public UiModeOption? SelectedUiMode { get; set; }

    public AppPreferences ToPreferences(AppPreferences basePreferences)
    {
        return new AppPreferences
        {
            OnboardingCompleted = basePreferences.OnboardingCompleted,
            PreferredOpenGlGpuName = SelectedGpu?.Name ?? basePreferences.PreferredOpenGlGpuName,
            OpenMaximized = OpenMaximized,
            DefaultFloor = ClampFloor(DefaultFloor),
            DefaultLayer = ClampLayer(DefaultLayer),
            DefaultRendererMode = SelectedRendererMode?.Value ?? "opengl",
            UiMode = SelectedUiMode?.Value ?? "simple"
        };
    }

    private static int ClampFloor(int floor)
        => floor < 0 ? 0 : floor >= WorldVerticalSettings.FloorCount ? WorldVerticalSettings.FloorCount - 1 : floor;

    private static int ClampLayer(int layer)
        => layer < 0 ? 0 : layer >= WorldVerticalSettings.LayersPerFloor ? WorldVerticalSettings.LayersPerFloor - 1 : layer;
}

public sealed record RendererModeOption(string Label, string Value)
{
    public override string ToString() => Label;
}

public sealed record UiModeOption(string Label, string Value)
{
    public override string ToString() => Label;
}
