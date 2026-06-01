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
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = AppContext.BaseDirectory;
        }

        var settingsDir = Path.Combine(root, "Mhs.Editor");
        _filePath = Path.Combine(settingsDir, "preferences.json");
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
