using Avalonia.Media;

namespace Mhs.Editor.Editor;

public sealed class PartDefinition
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public VoxelSize Size { get; init; }
    public Color Color { get; init; }
    public string CustomGlbAssetPath { get; init; } = string.Empty;
}
