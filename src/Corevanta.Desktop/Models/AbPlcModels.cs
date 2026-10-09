namespace Corevanta.Desktop.Models;

public sealed class AbPlcSummary
{
    public long PlcSystemId { get; init; }
    public string PlcName { get; init; } = string.Empty;
    public string ConnectionType { get; init; } = string.Empty;
    public string ConnectionDetails { get; init; } = string.Empty;
    public int? HwyNum { get; init; }
    public int? HwyBoxNumber { get; init; }
    public string HwyBoxNumbersCsv { get; init; } = string.Empty;
    public int? NodeAddress { get; init; }
    public string Plc5FileAddress { get; init; } = string.Empty;
    public string SerialPort { get; init; } = string.Empty;
    public string SerialSettings { get; init; } = string.Empty;
    public string SystemFilePath { get; init; } = string.Empty;
    public int LadderFileCount { get; init; }
    public int MappingCount { get; init; }
    public string HwyBoxDisplay => FormatBoxNumbers(HwyBoxNumbersCsv, HwyBoxNumber);
    private string NetworkLabel => IsSerialInterfaceConnection(ConnectionType) ? "UCN" : "HWY";

    public string DisplayLabel =>
        $"{PlcName} [{ConnectionType}] {NetworkLabel}:{FormatNumber(HwyNum)} BOX:{HwyBoxDisplay} NODE:{FormatNumber(NodeAddress)} FILE:{Plc5FileAddress}  XPS:{LadderFileCount}  Map:{MappingCount}";

    private static string FormatNumber(int? value) => value.HasValue ? value.Value.ToString() : "--";

    private static string FormatBoxNumbers(string csv, int? singleValue)
    {
        var trimmed = csv.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            return trimmed;
        }

        return FormatNumber(singleValue);
    }

    private static bool IsSerialInterfaceConnection(string? connectionType) =>
        !string.IsNullOrWhiteSpace(connectionType)
        && connectionType.Trim().Equals("Serial Interface", StringComparison.OrdinalIgnoreCase);
}

public sealed class AbPlcLadderFile
{
    public long LadderFileId { get; init; }
    public long PlcSystemId { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
}
