namespace Mhs.Editor.Settings;

public sealed class AppPreferences
{
    public bool OnboardingCompleted { get; set; }

    public string PreferredOpenGlGpuName { get; set; } = string.Empty;
}
