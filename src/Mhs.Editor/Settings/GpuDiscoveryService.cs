using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Mhs.Editor.Settings;

public static class GpuDiscoveryService
{
    public static IReadOnlyList<GpuOption> Discover()
    {
        var discovered = DiscoverFromDxdiag();
        if (discovered.Count > 0)
        {
            return discovered;
        }

        return
        [
            new GpuOption
            {
                Name = "System default GPU",
                DeviceType = "Unknown"
            }
        ];
    }

    public static void ApplyProcessGpuPreference(string preferredGpuName)
    {
        if (string.IsNullOrWhiteSpace(preferredGpuName))
        {
            return;
        }

        if (preferredGpuName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("SHIM_MCCOMPAT", "0x800000001");
            Environment.SetEnvironmentVariable("SHIM_RENDERING_MODE", "0x2");
        }
    }

    private static IReadOnlyList<GpuOption> DiscoverFromDxdiag()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"mhs-dxdiag-{Guid.NewGuid():N}.txt");

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dxdiag",
                Arguments = $"/whql:off /t \"{tempFile}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return [];
            }

            if (!process.WaitForExit(15000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // ignored
                }
                return [];
            }

            if (!File.Exists(tempFile))
            {
                return [];
            }

            var lines = File.ReadAllLines(tempFile);
            return Parse(lines);
        }
        catch
        {
            return [];
        }
        finally
        {
            try
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
            catch
            {
                // ignored
            }
        }
    }

    private static IReadOnlyList<GpuOption> Parse(IReadOnlyList<string> lines)
    {
        var gpus = new List<GpuOption>();
        string? name = null;
        string? deviceType = null;
        string? vendorId = null;

        static string? ParseValue(string line, string key)
        {
            var index = line.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return null;
            }

            return line[(index + key.Length)..].Trim();
        }

        void AddCurrent()
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var type = deviceType ?? "Unknown";
            var isDisplayOnly = type.Contains("Display-Only", StringComparison.OrdinalIgnoreCase);
            if (isDisplayOnly)
            {
                return;
            }

            gpus.Add(new GpuOption
            {
                Name = name,
                DeviceType = type,
                VendorId = vendorId ?? string.Empty
            });
        }

        foreach (var line in lines)
        {
            var cardName = ParseValue(line, "Card name:");
            if (!string.IsNullOrWhiteSpace(cardName))
            {
                AddCurrent();
                name = cardName;
                deviceType = null;
                vendorId = null;
                continue;
            }

            var parsedDeviceType = ParseValue(line, "Device Type:");
            if (!string.IsNullOrWhiteSpace(parsedDeviceType))
            {
                deviceType = parsedDeviceType;
                continue;
            }

            var parsedVendorId = ParseValue(line, "Vendor ID:");
            if (!string.IsNullOrWhiteSpace(parsedVendorId))
            {
                vendorId = parsedVendorId;
            }
        }

        AddCurrent();

        return gpus
            .GroupBy(g => $"{g.Name}|{g.DeviceType}|{g.VendorId}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToArray();
    }
}
