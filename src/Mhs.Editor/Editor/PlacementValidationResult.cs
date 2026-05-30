namespace Mhs.Editor.Editor;

public sealed record PlacementValidationResult(bool IsValid, string? Reason)
{
    public static PlacementValidationResult Valid { get; } = new(true, null);

    public static PlacementValidationResult Invalid(string reason) => new(false, reason);
}
