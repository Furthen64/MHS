namespace Mhs.Editor.Settings;

public sealed class GpuOption
{
    public required string Name { get; init; }

    public required string DeviceType { get; init; }

    public string VendorId { get; init; } = string.Empty;

    public override string ToString() => Name;
}
