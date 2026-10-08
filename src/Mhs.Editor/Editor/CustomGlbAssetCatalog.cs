using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mhs.Editor.Editor;

public sealed class CustomGlbAssetEntry
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("internalAssetPath")]
    public string InternalAssetPath { get; init; } = string.Empty;

    [JsonPropertyName("importedAtUtc")]
    public DateTimeOffset ImportedAtUtc { get; init; }

    [JsonPropertyName("originalFileName")]
    public string OriginalFileName { get; init; } = string.Empty;
}

public sealed class CustomGlbAssetCatalog
{
    public const int RecentLimit = 5;
    private const string CatalogFileName = "custom-glbs.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _assetsDirectory;
    private readonly string _catalogPath;

    public CustomGlbAssetCatalog(string? appDataRoot = null)
    {
        var root = appDataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MHS");
        _assetsDirectory = Path.Combine(root, "Assets", "CustomGlb");
        _catalogPath = Path.Combine(root, "Assets", CatalogFileName);
    }

    public string AssetsDirectory => _assetsDirectory;
    public string CatalogPath => _catalogPath;

    public IReadOnlyList<CustomGlbAssetEntry> LoadRecent()
        => LoadAll()
            .Where(entry => !string.IsNullOrWhiteSpace(entry.InternalAssetPath) && File.Exists(entry.InternalAssetPath))
            .OrderByDescending(entry => entry.ImportedAtUtc)
            .Take(RecentLimit)
            .ToList();

    public CustomGlbAssetEntry Import(string sourceGlbPath)
    {
        if (string.IsNullOrWhiteSpace(sourceGlbPath) || !File.Exists(sourceGlbPath))
        {
            throw new FileNotFoundException("The selected .glb file could not be found.", sourceGlbPath);
        }

        if (!string.Equals(Path.GetExtension(sourceGlbPath), ".glb", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Only binary .glb files are supported for custom parts.");
        }

        Directory.CreateDirectory(_assetsDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(_catalogPath)!);

        var destinationPath = AllocateDestinationPath(Path.GetFileName(sourceGlbPath));
        File.Copy(sourceGlbPath, destinationPath, overwrite: false);

        var importedAt = DateTimeOffset.UtcNow;
        var entry = new CustomGlbAssetEntry
        {
            Id = $"custom_glb_{Guid.NewGuid():N}",
            DisplayName = Path.GetFileNameWithoutExtension(sourceGlbPath),
            InternalAssetPath = destinationPath,
            ImportedAtUtc = importedAt,
            OriginalFileName = Path.GetFileName(sourceGlbPath)
        };

        var entries = LoadAll()
            .Where(existing => File.Exists(existing.InternalAssetPath))
            .Prepend(entry)
            .GroupBy(existing => existing.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(existing => existing.ImportedAtUtc)
            .Take(RecentLimit)
            .ToList();
        Save(entries);
        // TODO: cleanup old copied assets that fall out of the recent list.
        return entry;
    }

    private string AllocateDestinationPath(string fileName)
    {
        var safeName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "custom.glb";
        var stem = Path.GetFileNameWithoutExtension(safeName);
        var ext = Path.GetExtension(safeName);
        var candidate = Path.Combine(_assetsDirectory, safeName);
        if (!File.Exists(candidate)) return candidate;
        var suffix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        candidate = Path.Combine(_assetsDirectory, $"{stem}-{suffix}{ext}");
        var counter = 2;
        while (File.Exists(candidate)) candidate = Path.Combine(_assetsDirectory, $"{stem}-{suffix}-{counter++}{ext}");
        return candidate;
    }

    private List<CustomGlbAssetEntry> LoadAll()
    {
        try
        {
            if (!File.Exists(_catalogPath)) return [];
            var json = File.ReadAllText(_catalogPath);
            return JsonSerializer.Deserialize<List<CustomGlbAssetEntry>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Save(IReadOnlyList<CustomGlbAssetEntry> entries)
        => File.WriteAllText(_catalogPath, JsonSerializer.Serialize(entries, JsonOptions));
}
