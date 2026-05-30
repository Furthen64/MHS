using System;
using System.Collections.Generic;
using Avalonia.Media;

namespace Mhs.Editor.Viewport;

public sealed class PartRenderInfo
{
    public string PartId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public Rgba BaseColor { get; init; }
    public bool ShowFacingMarker { get; init; }
    public Rgba FacingMarkerColor { get; init; }
}

public readonly record struct Rgba(float R, float G, float B, float A)
{
    public Color ToAvaloniaColor()
        => Color.FromArgb(
            (byte)Math.Clamp((int)(A * 255f), 0, 255),
            (byte)Math.Clamp((int)(R * 255f), 0, 255),
            (byte)Math.Clamp((int)(G * 255f), 0, 255),
            (byte)Math.Clamp((int)(B * 255f), 0, 255));
}

public static class PartRenderCatalog
{
    private static readonly PartRenderInfo UnknownPart = new()
    {
        PartId = "unknown",
        DisplayName = "Unknown",
        BaseColor = new Rgba(0.71f, 0.71f, 0.71f, 1f),
        ShowFacingMarker = false,
        FacingMarkerColor = new Rgba(0.94f, 0.94f, 0.94f, 1f)
    };

    private static readonly Dictionary<string, PartRenderInfo> Lookup = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hopper"] = new PartRenderInfo
        {
            PartId = "hopper",
            DisplayName = "Hopper",
            BaseColor = new Rgba(0.94f, 0.78f, 0.35f, 1f),
            ShowFacingMarker = true,
            FacingMarkerColor = new Rgba(1f, 0.43f, 0.16f, 1f)
        },
        ["tall_hopper"] = new PartRenderInfo
        {
            PartId = "tall_hopper",
            DisplayName = "Tall Hopper",
            BaseColor = new Rgba(0.84f, 0.52f, 0.26f, 1f),
            ShowFacingMarker = true,
            FacingMarkerColor = new Rgba(1f, 0.43f, 0.16f, 1f)
        },
        ["bin"] = new PartRenderInfo
        {
            PartId = "bin",
            DisplayName = "Bin",
            BaseColor = new Rgba(0.35f, 0.59f, 0.94f, 1f),
            ShowFacingMarker = false,
            FacingMarkerColor = new Rgba(0.94f, 0.94f, 0.94f, 1f)
        },
        ["conveyor"] = new PartRenderInfo
        {
            PartId = "conveyor",
            DisplayName = "Conveyor",
            BaseColor = new Rgba(0.27f, 0.31f, 0.35f, 1f),
            ShowFacingMarker = true,
            FacingMarkerColor = new Rgba(1f, 0.90f, 0.24f, 1f)
        },
        ["chute"] = new PartRenderInfo
        {
            PartId = "chute",
            DisplayName = "Chute",
            BaseColor = new Rgba(0.59f, 0.59f, 0.59f, 1f),
            ShowFacingMarker = true,
            FacingMarkerColor = new Rgba(0.70f, 0.86f, 1f, 1f)
        }
    };

    private static readonly HashSet<string> UnknownWarnings = new(StringComparer.OrdinalIgnoreCase);

    static PartRenderCatalog()
    {
        Lookup["Hopper"] = Lookup["hopper"];
        Lookup["Tall Hopper"] = Lookup["tall_hopper"];
        Lookup["Bin"] = Lookup["bin"];
        Lookup["Conveyor"] = Lookup["conveyor"];
        Lookup["Chute"] = Lookup["chute"];
    }

    public static PartRenderInfo Resolve(string partIdOrDisplayName)
    {
        if (!string.IsNullOrWhiteSpace(partIdOrDisplayName)
            && Lookup.TryGetValue(partIdOrDisplayName, out var info))
        {
            return info;
        }

        var key = string.IsNullOrWhiteSpace(partIdOrDisplayName) ? "<empty>" : partIdOrDisplayName;
        if (UnknownWarnings.Add(key))
        {
            Console.Error.WriteLine($"[Renderer] Unknown part render metadata: '{key}'. Using fallback color.");
        }

        return UnknownPart;
    }
}
