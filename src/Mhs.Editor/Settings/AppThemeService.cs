using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace Mhs.Editor.Settings;

public static class AppThemeService
{
    public const string DarkMode = "dark";
    public const string LightMode = "light";
    public const string HighContrastMode = "highcontrast";

    private static readonly string[] KnownColors =
    [
        "#0F1217", "#111820", "#121519", "#121922", "#131922", "#13251C", "#151A20", "#151B23", "#151D27", "#161A21", "#171C22", "#172217", "#18212B", "#1A1F29", "#1B1D23", "#1B2129", "#1B212C", "#1B2530", "#1B2633", "#1C212A", "#1D3950", "#1D5D97", "#1E242E", "#1F2934", "#1F4F83", "#20252C", "#20262E", "#202632", "#202834", "#202A36", "#203246", "#213247", "#214A76", "#223247", "#232833", "#233A5A", "#242830", "#243750", "#2472B8", "#26313B", "#293241", "#2A3038", "#2A3A4D", "#2B3442", "#2B3E52", "#2B3E58", "#2D343F", "#2E394A", "#2E3B49", "#303844", "#303946", "#32455C", "#324563", "#32475B", "#334557", "#335C8A", "#35404D", "#354352", "#374657", "#394454", "#3B4252", "#3B4C61", "#3C4C5C", "#3E4652", "#3E4A5E", "#3F5368", "#3F6C99", "#404853", "#445468", "#455366", "#465569", "#475B70", "#49617A", "#4A6F9F", "#4D7BA4", "#586A82", "#5E7A94", "#667181", "#69B6FF", "#6BA9FF", "#6C8F59", "#6FADB5", "#738398", "#7EE787", "#7FDBFF", "#808080", "#88B8FF", "#89C4FF", "#8BC7FF", "#8DA0B7", "#8EA2BA", "#8EACCC", "#8FA2B8", "#8FC6FF", "#8FC7FF", "#91A4B8", "#9AAABD", "#9AD28E", "#9CCDFF", "#9EACBA", "#9FB4CC", "#A7D397", "#AEBAC8", "#AFA694", "#AFC6E9", "#B5DDFF", "#B8AB95", "#B8C7D9", "#B9C6D4", "#BFD2EA", "#C4B8A6", "#C7D1DD", "#C8D2DE", "#C8D4E2", "#C8D6E4", "#C8D8E8", "#C9D3DF", "#D4DEE9", "#D8D0C2", "#D8DEE9", "#D9131820", "#D9DEE5", "#DDE7F3", "#DDEBFA", "#DEE6F0", "#E4ECF6", "#E5182028", "#E5E9F0", "#E6DECF", "#E6ECF4", "#E7EDF5", "#E8F1FC", "#EAF5F0E6", "#ECEFF4", "#EDF3FA", "#EEF4FB", "#F0F4FA", "#F3F6FB", "#F4F7FB", "#F4F8FD", "#F5FAFF", "#F6FBFF", "#F7FBFF", "#FF7B72", "#FFD15A", "#FFFFFF"
    ];

    private static readonly Dictionary<string, string> DarkOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["#D8D0C2"] = "#121519",
        ["#AFA694"] = "#3B4252",
        ["#E6DECF"] = "#121519",
        ["#EAF5F0E6"] = "#E5182028",
        ["#B8AB95"] = "#5A6B80",
        ["#667181"] = "#9FB4CC",
        ["#3E4652"] = "#D4DEE9",
        ["#C4B8A6"] = "#455366"
    };

    private static readonly Dictionary<string, string> LightOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["#151A20"] = "#F8FAFC",
        ["#171C22"] = "#F1F5F9",
        ["#20262E"] = "#E7EEF6",
        ["#D8D0C2"] = "#F7F1E7",
        ["#E6DECF"] = "#FFF8EB",
        ["#D8DEE9"] = "#1F2937",
        ["#AFC6E9"] = "#31547A",
        ["#8EA2BA"] = "#52677F",
        ["#FFD15A"] = "#8A5A00",
        ["#FFFFFF"] = "#FFFFFF",
        ["#FF7B72"] = "#B42318",
        ["#7EE787"] = "#067647"
    };

    private static readonly Dictionary<string, string> HighContrastOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["#FFFFFF"] = "#FFFFFF",
        ["#FFD15A"] = "#FFFF00",
        ["#7FDBFF"] = "#00FFFF",
        ["#7EE787"] = "#00FF66",
        ["#FF7B72"] = "#FF3B30",
        ["#D8DEE9"] = "#FFFFFF",
        ["#AFC6E9"] = "#00FFFF",
        ["#8EA2BA"] = "#FFFF00",
        ["#D8D0C2"] = "#000000",
        ["#E6DECF"] = "#000000",
        ["#3B4252"] = "#FFFFFF",
        ["#3F5368"] = "#00FFFF",
        ["#404853"] = "#FFFFFF",
        ["#354352"] = "#FFFFFF",
        ["#35404D"] = "#FFFFFF",
        ["#445468"] = "#FFFFFF",
        ["#32455C"] = "#FFFFFF",
        ["#3E4A5E"] = "#FFFFFF",
        ["#2E394A"] = "#FFFFFF",
        ["#394454"] = "#FFFFFF",
        ["#5A6B80"] = "#FFFFFF"
    };

    public static string CurrentThemeMode { get; private set; } = DarkMode;

    public static string NormalizeThemeMode(string? themeMode)
    {
        if (string.Equals(themeMode, LightMode, StringComparison.OrdinalIgnoreCase))
        {
            return LightMode;
        }

        if (string.Equals(themeMode, HighContrastMode, StringComparison.OrdinalIgnoreCase)
            || string.Equals(themeMode, "high-contrast", StringComparison.OrdinalIgnoreCase))
        {
            return HighContrastMode;
        }

        return DarkMode;
    }

    public static void Apply(string? themeMode)
    {
        CurrentThemeMode = NormalizeThemeMode(themeMode);

        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = CurrentThemeMode == LightMode ? ThemeVariant.Light : ThemeVariant.Dark;
            foreach (var originalHex in KnownColors)
            {
                var themedHex = GetHex(originalHex, CurrentThemeMode);
                app.Resources[ToBrushKey(originalHex)] = new SolidColorBrush(Color.Parse(themedHex));
            }
        }
    }

    public static string GetHex(string originalHex)
        => GetHex(originalHex, CurrentThemeMode);

    public static ViewportThemeColors ViewportColors()
    {
        return CurrentThemeMode switch
        {
            LightMode => new ViewportThemeColors(
                Background: Color.FromRgb(246, 241, 232),
                Floor: Color.FromRgb(231, 223, 208),
                AlternateFloor: Color.FromRgb(238, 231, 218),
                ActiveFloorLine: Color.FromArgb(190, 109, 94, 70),
                InactiveFloorLine: Color.FromArgb(85, 109, 94, 70),
                GridLine: Color.FromArgb(92, 120, 111, 96)),
            HighContrastMode => new ViewportThemeColors(
                Background: Colors.Black,
                Floor: Colors.Black,
                AlternateFloor: Color.FromRgb(12, 12, 12),
                ActiveFloorLine: Color.FromArgb(255, 255, 255, 0),
                InactiveFloorLine: Color.FromArgb(180, 0, 255, 255),
                GridLine: Color.FromArgb(190, 255, 255, 255)),
            _ => new ViewportThemeColors(
                Background: Color.FromRgb(25, 30, 35),
                Floor: Color.FromRgb(35, 43, 52),
                AlternateFloor: Color.FromRgb(30, 37, 46),
                ActiveFloorLine: Color.FromArgb(210, 150, 190, 255),
                InactiveFloorLine: Color.FromArgb(80, 130, 140, 160),
                GridLine: Color.FromArgb(125, 160, 190, 220))
        };
    }

    private static string GetHex(string originalHex, string themeMode)
    {
        originalHex = NormalizeHex(originalHex);
        if (themeMode == DarkMode)
        {
            return DarkOverrides.TryGetValue(originalHex, out var darkOverride)
                ? PreserveAlpha(originalHex, darkOverride)
                : originalHex;
        }

        var overrides = themeMode == HighContrastMode ? HighContrastOverrides : LightOverrides;
        if (overrides.TryGetValue(originalHex, out var overrideHex))
        {
            return PreserveAlpha(originalHex, overrideHex);
        }

        var color = Color.Parse(originalHex);
        var luminance = GetLuminance(color);
        var transformed = themeMode == HighContrastMode
            ? ToHighContrast(color, luminance)
            : ToLightTheme(color, luminance);
        return ToHex(transformed);
    }

    private static Color ToLightTheme(Color color, double luminance)
    {
        if (IsSaturatedAccent(color))
        {
            return Color.FromArgb(color.A, (byte)Math.Max(0, color.R - 35), (byte)Math.Max(45, color.G - 35), (byte)Math.Max(75, color.B - 20));
        }

        if (luminance < 0.36)
        {
            var amount = 0.72 + (0.36 - luminance) * 0.45;
            return Mix(color, Colors.White, Math.Clamp(amount, 0.72, 0.92));
        }

        if (luminance > 0.62)
        {
            return Mix(color, Color.FromRgb(15, 23, 42), 0.78);
        }

        return Mix(color, Color.FromRgb(71, 85, 105), 0.45);
    }

    private static Color ToHighContrast(Color color, double luminance)
    {
        if (IsSaturatedAccent(color))
        {
            if (color.G > color.R && color.G > color.B)
            {
                return Color.FromArgb(color.A, 0, 255, 102);
            }

            if (color.R > 210 && color.G > 150 && color.B < 120)
            {
                return Color.FromArgb(color.A, 255, 255, 0);
            }

            return Color.FromArgb(color.A, 0, 255, 255);
        }

        return luminance > 0.45
            ? Color.FromArgb(color.A, 255, 255, 255)
            : Color.FromArgb(color.A, 0, 0, 0);
    }

    private static bool IsSaturatedAccent(Color color)
    {
        var max = Math.Max(color.R, Math.Max(color.G, color.B));
        var min = Math.Min(color.R, Math.Min(color.G, color.B));
        return max - min > 65 && max > 110;
    }

    private static Color Mix(Color source, Color target, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            source.A,
            (byte)Math.Round(source.R + (target.R - source.R) * amount),
            (byte)Math.Round(source.G + (target.G - source.G) * amount),
            (byte)Math.Round(source.B + (target.B - source.B) * amount));
    }

    private static double GetLuminance(Color color)
        => ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255.0;

    private static string ToBrushKey(string originalHex)
        => $"ThemeBrush_{NormalizeHex(originalHex).TrimStart('#')}";

    private static string NormalizeHex(string hex)
        => hex.StartsWith('#') ? hex.ToUpperInvariant() : $"#{hex.ToUpperInvariant()}";

    private static string PreserveAlpha(string originalHex, string themedHex)
    {
        originalHex = NormalizeHex(originalHex);
        themedHex = NormalizeHex(themedHex);
        return originalHex.Length == 9 && themedHex.Length == 7
            ? $"#{originalHex[1..3]}{themedHex[1..]}"
            : themedHex;
    }

    private static string ToHex(Color color)
        => color.A == 255
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
}

public readonly record struct ViewportThemeColors(
    Color Background,
    Color Floor,
    Color AlternateFloor,
    Color ActiveFloorLine,
    Color InactiveFloorLine,
    Color GridLine);
