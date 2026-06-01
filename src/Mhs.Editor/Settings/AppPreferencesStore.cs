using System;
using System.IO;
using System.Text.Json;

namespace Mhs.Editor.Settings;

public sealed class AppPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    public AppPreferencesStore()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            home = AppContext.BaseDirectory;
        }

        var settingsDir = Path.Combine(home, ".config", "MHS");
        _filePath = Path.Combine(settingsDir, "mhs.json");
    }

    public AppPreferences Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new AppPreferences();
            }

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<AppPreferences>(json) ?? new AppPreferences();
        }
        catch
        {
            return new AppPreferences();
        }
    }

    public void Save(AppPreferences preferences)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(preferences, JsonOptions);
        File.WriteAllText(_filePath, json);
    }
}
