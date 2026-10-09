namespace Corevanta.Desktop.Models;

public sealed class LogicBlockDetail
{
    public long EntityId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string SourceFile { get; init; } = string.Empty;
    public IReadOnlyList<LogicInput> Inputs { get; init; } = [];
    public IReadOnlyList<LogicNumeric> Numerics { get; init; } = [];
    public IReadOnlyList<LogicGate> Gates { get; init; } = [];
    public IReadOnlyList<LogicOutput> Outputs { get; init; } = [];
}

public sealed class LogicInput
{
    public int Index { get; init; }
    public string Source { get; init; } = string.Empty;
}

public sealed class LogicNumeric
{
    public int Index { get; init; }
    public string Value { get; init; } = string.Empty;
}

public sealed class LogicGate
{
    public int Index { get; init; }
    public string Algorithm { get; init; } = string.Empty;
    public IReadOnlyList<string> Inputs { get; init; } = [];
    public double? PeriodSeconds { get; init; }
}

public sealed class LogicOutput
{
    public int Index { get; init; }
    public string Source { get; init; } = string.Empty;
    public string Destination { get; init; } = string.Empty;
    public string Enable { get; init; } = string.Empty;
}
