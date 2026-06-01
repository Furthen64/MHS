using System;
using System.IO;

namespace Mhs.Editor.Settings;

public static class StartupDiagnostics
{
    private static readonly object SyncRoot = new();
    private static readonly string LogFilePath = BuildLogFilePath();

    public static void Log(string message)
    {
        try
        {
            lock (SyncRoot)
            {
                var directory = Path.GetDirectoryName(LogFilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var line = $"{DateTime.UtcNow:O} {message}{Environment.NewLine}";
                File.AppendAllText(LogFilePath, line);
            }
        }
        catch
        {
            // ignored
        }
    }

    private static string BuildLogFilePath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            home = AppContext.BaseDirectory;
        }

        return Path.Combine(home, ".config", "MHS", "startup.log");
    }
}
