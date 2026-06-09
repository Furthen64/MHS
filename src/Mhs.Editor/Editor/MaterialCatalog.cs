using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Media;

namespace Mhs.Editor.Editor;

public sealed record MaterialDefinition(
    string Id,
    string DisplayName,
    Color TopColor,
    Color RightColor,
    Color FrontColor);

public static class MaterialCatalog
{
    private static readonly IReadOnlyList<MaterialDefinition> Materials = new ReadOnlyCollection<MaterialDefinition>(
    [
        new MaterialDefinition(
            "Brown",
            "Brown",
            Color.FromRgb(226, 152, 63),
            Color.FromRgb(181, 118, 45),
            Color.FromRgb(166, 102, 34)),
        new MaterialDefinition(
            "Green",
            "Green",
            Color.FromRgb(112, 192, 102),
            Color.FromRgb(76, 151, 71),
            Color.FromRgb(59, 125, 56)),
        new MaterialDefinition(
            "Blue",
            "Blue",
            Color.FromRgb(105, 168, 229),
            Color.FromRgb(74, 129, 186),
            Color.FromRgb(54, 102, 153)),
        new MaterialDefinition(
            "Gray",
            "Gray",
            Color.FromRgb(193, 199, 209),
            Color.FromRgb(145, 152, 163),
            Color.FromRgb(120, 126, 137)),
        new MaterialDefinition(
            "Yellow",
            "Yellow",
            Color.FromRgb(236, 201, 91),
            Color.FromRgb(198, 164, 60),
            Color.FromRgb(171, 137, 43)),
        new MaterialDefinition(
            "Coal",
            "Coal",
            Color.FromRgb(74, 79, 88),
            Color.FromRgb(56, 60, 68),
            Color.FromRgb(43, 47, 54)),
        new MaterialDefinition(
            "Sand",
            "Sand",
            Color.FromRgb(233, 206, 144),
            Color.FromRgb(203, 174, 112),
            Color.FromRgb(180, 149, 92))
    ]);

    private static readonly IReadOnlyList<string> MaterialIds = new ReadOnlyCollection<string>(
        new List<string> { "Brown", "Green", "Blue", "Gray", "Yellow", "Coal", "Sand" });

    public static IReadOnlyList<string> AvailableMaterialIds => MaterialIds;

    public static IReadOnlyList<MaterialDefinition> AvailableMaterials => Materials;

    public static MaterialDefinition Resolve(string? materialId)
    {
        foreach (var material in Materials)
        {
            if (string.Equals(material.Id, materialId, StringComparison.OrdinalIgnoreCase))
            {
                return material;
            }
        }

        return Materials[0];
    }

    public static string NormalizeId(string? materialId)
        => Resolve(materialId).Id;
}
