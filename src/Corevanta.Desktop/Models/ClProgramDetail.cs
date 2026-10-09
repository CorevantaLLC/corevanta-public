namespace Corevanta.Desktop.Models;

public sealed class ClProgramDetail
{
    public long ClProgramId { get; init; }
    public string ProgramName { get; init; } = string.Empty;
    public string ProgramType { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public string FileText { get; init; } = string.Empty;
}
