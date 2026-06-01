using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

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
        var tempXmlFile = Path.Combine(Path.GetTempPath(), $"mhs-dxdiag-{Guid.NewGuid():N}.xml");
        var tempTextFile = Path.Combine(Path.GetTempPath(), $"mhs-dxdiag-{Guid.NewGuid():N}.txt");

        try
        {
            if (TryRunDxdiag($"/whql:off /x \"{tempXmlFile}\"") && File.Exists(tempXmlFile))
            {
                var parsedXml = ParseXml(tempXmlFile);
                if (parsedXml.Count > 0)
                {
                    return parsedXml;
                }
            }

            if (!TryRunDxdiag($"/whql:off /t \"{tempTextFile}\"") || !File.Exists(tempTextFile))
            {
                return [];
            }

            var lines = File.ReadAllLines(tempTextFile);
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
                if (File.Exists(tempXmlFile))
                {
                    File.Delete(tempXmlFile);
                }

                if (File.Exists(tempTextFile))
                {
                    File.Delete(tempTextFile);
                }
            }
            catch
            {
                // ignored
            }
        }
    }

    private static bool TryRunDxdiag(string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dxdiag",
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return false;
        }

        if (process.WaitForExit(15000))
        {
            return process.ExitCode == 0;
        }

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // ignored
        }

        return false;
    }

    private static IReadOnlyList<GpuOption> ParseXml(string filePath)
    {
        var document = XDocument.Load(filePath);
        var gpus = document
            .Descendants()
            .Where(node => node.Name.LocalName.Equals("DisplayDevice", StringComparison.OrdinalIgnoreCase))
            .Select(node =>
            {
                var name = GetFirstValue(node, "CardName", "Description", "DeviceName");
                if (string.IsNullOrWhiteSpace(name))
                {
                    return null;
                }

                var deviceType = GetFirstValue(node, "DeviceType") ?? "Unknown";
                if (deviceType.Contains("Display-Only", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                var deviceKey = GetFirstValue(node, "DeviceKey", "PNPDeviceID", "PnpDeviceId") ?? string.Empty;
                var vendorId = ExtractVendorId(deviceKey);

                return new GpuOption
                {
                    Name = name,
                    DeviceType = deviceType,
                    VendorId = vendorId
                };
            })
            .Where(gpu => gpu is not null)
            .Select(gpu => gpu!)
            .GroupBy(g => $"{g.Name}|{g.DeviceType}|{g.VendorId}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToArray();

        return gpus;
    }

    private static string? GetFirstValue(XElement node, params string[] names)
    {
        foreach (var name in names)
        {
            var value = node.Elements()
                .FirstOrDefault(element => element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?.Value
                .Trim();

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string ExtractVendorId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var match = Regex.Match(value, @"VEN_([0-9A-F]{4})", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : string.Empty;
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
