using Avalonia;
using System;
using Mhs.Editor.Settings;

namespace Mhs.Editor;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        var preferences = new AppPreferencesStore().Load();
        if (string.IsNullOrWhiteSpace(preferences.PreferredOpenGlGpuName))
        {
            preferences.PreferredOpenGlGpuName = "System default GPU";
        }

        StartupDiagnostics.Log($"Startup begin. Preferred GPU: {preferences.PreferredOpenGlGpuName}");
        GpuDiscoveryService.ApplyProcessGpuPreference(preferences.PreferredOpenGlGpuName);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
