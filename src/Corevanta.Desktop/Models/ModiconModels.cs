using System.Globalization;

namespace Corevanta.Desktop.Models;

public sealed class ModiconSummary
{
    public long ModiconSystemId { get; init; }
    public string PlcName { get; init; } = string.Empty;
    public string ConnectionType { get; init; } = string.Empty;
    public string ConnectionDetails { get; init; } = string.Empty;
    public int? HwyNum { get; init; }
    public int? HwyBoxNumber { get; init; }
    public string HwyBoxNumbersCsv { get; init; } = string.Empty;
    public int? NodeAddress { get; init; }
    public string RegistersFilePath { get; init; } = string.Empty;
    public string TrafficCopFilePath { get; init; } = string.Empty;
    public string SerialPort { get; init; } = string.Empty;
    public string SerialSettings { get; init; } = string.Empty;
    public int ProgramFileCount { get; init; }
    public int MappingCount { get; init; }
    public int TrafficCopEntryCount { get; init; }
    public string HwyBoxDisplay => FormatBoxNumbers(HwyBoxNumbersCsv, HwyBoxNumber);

    public string DisplayLabel
    {
        get
        {
            var nodeDisplay = NodeAddress.HasValue ? $" NODE:{NodeAddress.Value}" : string.Empty;
            return $"{PlcName} [{ConnectionType}] HWY:{FormatNumber(HwyNum)} BOX:{HwyBoxDisplay}{nodeDisplay}  XPS:{ProgramFileCount}  Map:{MappingCount}  TCOP:{TrafficCopEntryCount}";
        }
    }

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
}

public sealed class ModiconProgramFile
{
    public long ProgramFileId { get; init; }
    public long ModiconSystemId { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
}

public sealed class ModiconTrafficCopEntry
{
    public long EntryId { get; init; }
    public long ModiconSystemId { get; init; }
    public int? DropNumber { get; init; }
    public int SlotNumber { get; init; }
    public string ModuleText { get; init; } = string.Empty;
    public string ReferenceStart { get; init; } = string.Empty;
    public string ReferenceEnd { get; init; } = string.Empty;
    public string ReferenceRange { get; init; } = string.Empty;
    public string DataType { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string SourceLine { get; init; } = string.Empty;
    public int TreeLevel { get; init; }
    public bool IsTreeHeader { get; init; }

    public string DropDisplay => DropNumber.HasValue ? DropNumber.Value.ToString(CultureInfo.InvariantCulture) : "--";
    public string SlotDisplay => SlotNumber >= 0 ? SlotNumber.ToString(CultureInfo.InvariantCulture) : string.Empty;
    public string ModuleDisplay
    {
        get
        {
            return $"{new string(' ', Math.Max(0, TreeLevel) * 2)}{ModuleText}";
        }
    }
}
