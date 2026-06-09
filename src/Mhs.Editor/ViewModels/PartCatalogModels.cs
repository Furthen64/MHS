using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Mhs.Editor.ViewModels;

public sealed class PartCatalogItemViewModel
{
    private static readonly ConcurrentDictionary<string, Bitmap?> ThumbnailCache = new(StringComparer.OrdinalIgnoreCase);

    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public IReadOnlyList<string> Tags { get; init; } = [];
    public string Thumbnail { get; init; } = string.Empty;
    public Bitmap? ThumbnailImage => GetThumbnailImage(Thumbnail);
    public string ToolType { get; init; } = "place";
    public bool IsPlaceable { get; init; }
    public required ICommand ActivateCommand { get; init; }

    internal static Bitmap? GetThumbnailImage(string thumbnail) => string.IsNullOrWhiteSpace(thumbnail)
        ? null
        : ThumbnailCache.GetOrAdd(thumbnail, LoadThumbnail);

    private static Bitmap? LoadThumbnail(string thumbnail)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri(thumbnail));
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }
}

public sealed class PartCatalogSectionViewModel
{
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<PartCatalogItemViewModel> Items { get; init; } = [];
}

public sealed class PartCatalogMetadataEntry
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; init; } = [];

    [JsonPropertyName("thumbnail")]
    public string Thumbnail { get; init; } = string.Empty;

    [JsonPropertyName("toolType")]
    public string ToolType { get; init; } = "place";

    [JsonPropertyName("isPlaceable")]
    public bool IsPlaceable { get; init; }

    [JsonPropertyName("visualStyle")]
    public string VisualStyle { get; init; } = string.Empty;
}

public static class PartCatalogLoader
{
    private static readonly Uri CatalogUri = new("avares://Mhs.Editor/Assets/PartCatalog/parts-catalog.json");

    public static IReadOnlyList<PartCatalogMetadataEntry> LoadCatalog()
    {
        try
        {
            using var stream = AssetLoader.Open(CatalogUri);
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            var parsed = JsonSerializer.Deserialize<List<PartCatalogMetadataEntry>>(json);
            if (parsed is null)
            {
                return [];
            }

            return parsed
                .Where(IsValid)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static bool IsValid(PartCatalogMetadataEntry entry)
        => !string.IsNullOrWhiteSpace(entry.Id)
           && !string.IsNullOrWhiteSpace(entry.DisplayName)
           && !string.IsNullOrWhiteSpace(entry.Category)
           && !string.IsNullOrWhiteSpace(entry.ToolType);
}
