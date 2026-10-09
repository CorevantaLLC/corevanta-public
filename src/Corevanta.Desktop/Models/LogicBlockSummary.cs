namespace Corevanta.Desktop.Models;

public sealed class LogicBlockSummary
{
    public long EntityId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string SourceFile { get; init; } = string.Empty;
    public string DisplayLabel => string.IsNullOrWhiteSpace(Description) ? Name : $"{Name} - {Description}";
}
