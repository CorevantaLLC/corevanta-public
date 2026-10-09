namespace Corevanta.Desktop.Models;

public sealed class EntitySearchResult
{
    public long EntityId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string? EntityType { get; init; }
    public string SourceFile { get; init; } = string.Empty;
}
