namespace Mhs.Editor.Settings;

public sealed class AppPreferences
{
    public bool OnboardingCompleted { get; set; }
    public string PreferredOpenGlGpuName { get; set; } = string.Empty;
    public bool OpenMaximized { get; set; }
    public int DefaultFloor { get; set; }
    public int DefaultLayer { get; set; }
    public string DefaultRendererMode { get; set; } = "opengl";
    public string UiMode { get; set; } = "simple";
}
