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
    public FlowMarkerKind FlowMarkerKind { get; init; } = FlowMarkerKind.Outgoing;
}

public enum FlowMarkerKind
{
    Outgoing,
    Incoming
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
        ["conveyor_straight"] = new PartRenderInfo
        {
            PartId = "conveyor_straight",
            DisplayName = "Conveyor (Straight)",
            BaseColor = new Rgba(0.35f, 0.39f, 0.44f, 1f),
            ShowFacingMarker = true,
            FacingMarkerColor = new Rgba(1f, 0.90f, 0.24f, 1f)
        },
        ["conveyor_incline"] = new PartRenderInfo
        {
            PartId = "conveyor_incline",
            DisplayName = "Conveyor (Incline)",
            BaseColor = new Rgba(0.41f, 0.39f, 0.35f, 1f),
            ShowFacingMarker = true,
            FacingMarkerColor = new Rgba(1f, 0.90f, 0.24f, 1f)
        },
        ["conveyor_curve"] = new PartRenderInfo
        {
            PartId = "conveyor_curve",
            DisplayName = "Conveyor (Curve)",
            BaseColor = new Rgba(0.43f, 0.41f, 0.37f, 1f),
            ShowFacingMarker = true,
            FacingMarkerColor = new Rgba(1f, 0.90f, 0.24f, 1f)
        },
        ["conveyor_merge"] = new PartRenderInfo
        {
            PartId = "conveyor_merge",
            DisplayName = "Conveyor (Merge)",
            BaseColor = new Rgba(0.38f, 0.41f, 0.45f, 1f),
            ShowFacingMarker = true,
            FacingMarkerColor = new Rgba(1f, 0.90f, 0.24f, 1f)
        },
        ["conveyor_split"] = new PartRenderInfo
        {
            PartId = "conveyor_split",
            DisplayName = "Conveyor (Split)",
            BaseColor = new Rgba(0.39f, 0.42f, 0.46f, 1f),
            ShowFacingMarker = true,
            FacingMarkerColor = new Rgba(1f, 0.90f, 0.24f, 1f)
        },
        ["transfer_plate"] = new PartRenderInfo
        {
            PartId = "transfer_plate",
            DisplayName = "Transfer Plate",
            BaseColor = new Rgba(0.47f, 0.47f, 0.43f, 1f),
            ShowFacingMarker = false,
            FacingMarkerColor = new Rgba(0.94f, 0.94f, 0.94f, 1f)
        },
        ["chute"] = new PartRenderInfo
        {
            PartId = "chute",
            DisplayName = "Chute",
            BaseColor = new Rgba(0.59f, 0.59f, 0.59f, 1f),
            ShowFacingMarker = true,
            FacingMarkerColor = new Rgba(0.70f, 0.86f, 1f, 1f)
        },
        ["mtrlsrc"] = new PartRenderInfo
        {
            PartId = "mtrlsrc",
            DisplayName = "MtrlSrc",
            BaseColor = new Rgba(0.78f, 0.42f, 0.24f, 1f),
            ShowFacingMarker = true,
            FacingMarkerColor = new Rgba(1f, 0.90f, 0.24f, 1f)
        },
        ["mtrlrecv"] = new PartRenderInfo
        {
            PartId = "mtrlrecv",
            DisplayName = "MtrlRecv",
            BaseColor = new Rgba(0.31f, 0.70f, 0.57f, 1f),
            ShowFacingMarker = true,
            FacingMarkerColor = new Rgba(0.87f, 0.98f, 1f, 1f),
            FlowMarkerKind = FlowMarkerKind.Incoming
        },
        ["lift_elevator"] = new PartRenderInfo
        {
            PartId = "lift_elevator",
            DisplayName = "Lift / Elevator",
            BaseColor = new Rgba(0.65f, 0.65f, 0.67f, 1f),
            ShowFacingMarker = true,
            FacingMarkerColor = new Rgba(0.74f, 0.89f, 1f, 1f)
        },
        ["drop_chute"] = new PartRenderInfo
        {
            PartId = "drop_chute",
            DisplayName = "Drop Chute",
            BaseColor = new Rgba(0.53f, 0.53f, 0.53f, 1f),
            ShowFacingMarker = true,
            FacingMarkerColor = new Rgba(0.7f, 0.86f, 1f, 1f)
        },
        ["spiral_lift"] = new PartRenderInfo
        {
            PartId = "spiral_lift",
            DisplayName = "Spiral Lift",
            BaseColor = new Rgba(0.71f, 0.57f, 0.32f, 1f),
            ShowFacingMarker = true,
            FacingMarkerColor = new Rgba(1f, 0.9f, 0.24f, 1f)
        },
        ["support_frame"] = new PartRenderInfo
        {
            PartId = "support_frame",
            DisplayName = "Support Frame",
            BaseColor = new Rgba(0.42f, 0.46f, 0.51f, 1f),
            ShowFacingMarker = false,
            FacingMarkerColor = new Rgba(0.94f, 0.94f, 0.94f, 1f)
        },
        ["beam"] = new PartRenderInfo
        {
            PartId = "beam",
            DisplayName = "Beam",
            BaseColor = new Rgba(0.46f, 0.49f, 0.53f, 1f),
            ShowFacingMarker = false,
            FacingMarkerColor = new Rgba(0.94f, 0.94f, 0.94f, 1f)
        },
        ["platform"] = new PartRenderInfo
        {
            PartId = "platform",
            DisplayName = "Platform",
            BaseColor = new Rgba(0.54f, 0.51f, 0.39f, 1f),
            ShowFacingMarker = false,
            FacingMarkerColor = new Rgba(0.94f, 0.94f, 0.94f, 1f)
        },
        ["wall"] = new PartRenderInfo
        {
            PartId = "wall",
            DisplayName = "Wall",
            BaseColor = new Rgba(0.49f, 0.52f, 0.56f, 1f),
            ShowFacingMarker = false,
            FacingMarkerColor = new Rgba(0.94f, 0.94f, 0.94f, 1f)
        },
        ["fence"] = new PartRenderInfo
        {
            PartId = "fence",
            DisplayName = "Fence",
            BaseColor = new Rgba(0.52f, 0.48f, 0.38f, 1f),
            ShowFacingMarker = false,
            FacingMarkerColor = new Rgba(0.94f, 0.94f, 0.94f, 1f)
        },
        ["ladder"] = new PartRenderInfo
        {
            PartId = "ladder",
            DisplayName = "Ladder",
            BaseColor = new Rgba(0.48f, 0.51f, 0.54f, 1f),
            ShowFacingMarker = false,
            FacingMarkerColor = new Rgba(0.94f, 0.94f, 0.94f, 1f)
        },
        ["motor"] = new PartRenderInfo
        {
            PartId = "motor",
            DisplayName = "Motor",
            BaseColor = new Rgba(0.34f, 0.49f, 0.54f, 1f),
            ShowFacingMarker = false,
            FacingMarkerColor = new Rgba(0.94f, 0.94f, 0.94f, 1f)
        },
        ["sensor"] = new PartRenderInfo
        {
            PartId = "sensor",
            DisplayName = "Sensor",
            BaseColor = new Rgba(0.49f, 0.57f, 0.42f, 1f),
            ShowFacingMarker = false,
            FacingMarkerColor = new Rgba(0.94f, 0.94f, 0.94f, 1f)
        },
        ["control_box"] = new PartRenderInfo
        {
            PartId = "control_box",
            DisplayName = "Control Box",
            BaseColor = new Rgba(0.42f, 0.44f, 0.48f, 1f),
            ShowFacingMarker = false,
            FacingMarkerColor = new Rgba(0.94f, 0.94f, 0.94f, 1f)
        }
    };

    private static readonly HashSet<string> UnknownWarnings = new(StringComparer.OrdinalIgnoreCase);

    static PartRenderCatalog()
    {
        Lookup["Hopper"] = Lookup["hopper"];
        Lookup["Tall Hopper"] = Lookup["tall_hopper"];
        Lookup["Bin"] = Lookup["bin"];
        Lookup["Conveyor"] = Lookup["conveyor"];
        Lookup["Conveyor (Straight)"] = Lookup["conveyor_straight"];
        Lookup["Conveyor (Incline)"] = Lookup["conveyor_incline"];
        Lookup["Conveyor (Curve)"] = Lookup["conveyor_curve"];
        Lookup["Conveyor (Merge)"] = Lookup["conveyor_merge"];
        Lookup["Conveyor (Split)"] = Lookup["conveyor_split"];
        Lookup["Transfer Plate"] = Lookup["transfer_plate"];
        Lookup["Chute"] = Lookup["chute"];
        Lookup["MtrlSrc"] = Lookup["mtrlsrc"];
        Lookup["MtrlRecv"] = Lookup["mtrlrecv"];
        Lookup["Lift / Elevator"] = Lookup["lift_elevator"];
        Lookup["Drop Chute"] = Lookup["drop_chute"];
        Lookup["Spiral Lift"] = Lookup["spiral_lift"];
        Lookup["Support Frame"] = Lookup["support_frame"];
        Lookup["Beam"] = Lookup["beam"];
        Lookup["Platform"] = Lookup["platform"];
        Lookup["Wall"] = Lookup["wall"];
        Lookup["Fence"] = Lookup["fence"];
        Lookup["Ladder"] = Lookup["ladder"];
        Lookup["Motor"] = Lookup["motor"];
        Lookup["Sensor"] = Lookup["sensor"];
        Lookup["Control Box"] = Lookup["control_box"];
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
