namespace Corevanta.Desktop.Models;

public sealed class PropertyRow
{
    public string Property { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public bool IsLink { get; init; }
    public string? LinkToken { get; init; }
}
