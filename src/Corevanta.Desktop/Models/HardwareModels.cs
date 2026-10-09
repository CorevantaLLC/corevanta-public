using System.Collections.ObjectModel;

namespace Corevanta.Desktop.Models;

public sealed class HardwareBoxSummary
{
    public long BoxEntityId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public string SourceFile { get; init; } = string.Empty;
    public int? HighwayNumber { get; init; }
    public int? BoxNumber { get; init; }
    public int? NetworkNumber { get; init; }
    public int? NodeNumber { get; init; }
    public int ModuleCount { get; init; }

    public string DisplayLabel =>
        $"{Name}  NTWK:{FormatNetworkNode(NetworkNumber)} NODE:{FormatNetworkNode(NodeNumber)}  Modules:{ModuleCount}";

    private static string FormatNetworkNode(int? value) => value.HasValue ? value.Value.ToString("00") : "--";
}

public sealed class HardwareModuleSummary
{
    public int ModuleIndex { get; init; }
    public string ModuleType { get; init; } = string.Empty;
    public string Redundancy { get; init; } = string.Empty;
    public int? FileA { get; init; }
    public int? CardA { get; init; }
    public int? FileB { get; init; }
    public int? CardB { get; init; }

    public string DisplayLabel
    {
        get
        {
            var sideA = $"A:F{Format(FileA)}/C{Format(CardA)}";
            if (CardB.GetValueOrDefault() > 0)
            {
                return $"M{ModuleIndex:00} {ModuleType} {sideA}  B:F{Format(FileB)}/C{Format(CardB)}  {Redundancy}";
            }

            return $"M{ModuleIndex:00} {ModuleType} {sideA}  {Redundancy}";
        }
    }

    private static string Format(int? value) => value.HasValue ? value.Value.ToString() : "0";
}

public sealed class HardwareBoxDetail
{
    public long BoxEntityId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public string SourceFile { get; init; } = string.Empty;
    public int? HighwayNumber { get; init; }
    public int? BoxNumber { get; init; }
    public int? NetworkNumber { get; init; }
    public int? NodeNumber { get; init; }
    public IReadOnlyList<HardwareModuleSummary> Modules { get; init; } = [];
}

public sealed class HardwarePointSummary
{
    public long EntityId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string TagName { get; init; } = string.Empty;
    public int? Channel { get; init; }
    public string EntityType { get; init; } = string.Empty;
    public string SourceFile { get; init; } = string.Empty;
    public string RegisterAddress { get; init; } = string.Empty;
    public string RegisterDescription { get; init; } = string.Empty;
    public string ModbusAddressIn { get; set; } = string.Empty;
    public string ModbusAddressOut { get; set; } = string.Empty;
    public string AbAddressIn { get; set; } = string.Empty;
    public string AbAddressOut { get; set; } = string.Empty;
    public string AbAddress { get; set; } = string.Empty;
    public bool CanOpenModbusInAddress { get; set; }
    public bool CanOpenModbusOutAddress { get; set; }
    public bool CanOpenAbInAddress { get; set; }
    public bool CanOpenAbOutAddress { get; set; }
    public string CustomLocation { get; init; } = string.Empty;
    public int? NetworkNumber { get; init; }
    public int? NodeNumber { get; init; }
    public int? ModuleNumber { get; init; }
    public int? SlotNumber { get; init; }
    public bool HasEntityLink => EntityId > 0;

    public string Location =>
        !string.IsNullOrWhiteSpace(CustomLocation)
            ? CustomLocation
            : $"N{Format(NetworkNumber)}-D{Format(NodeNumber)}-M{Format(ModuleNumber)}-S{Format(SlotNumber)}";

    private static string Format(int? value) => value.HasValue ? value.Value.ToString() : "--";
}

public sealed class HardwareTreeNode
{
    public string Key { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public bool IsExpanded { get; init; }
    public bool IsModule { get; init; }
    public HardwareModuleSummary? Module { get; init; }
    public bool IsAbPlcNode { get; init; }
    public long? AbPlcSystemId { get; init; }
    public bool IsModiconTrafficNode { get; init; }
    public long? ModiconSystemId { get; init; }
    public int? ModiconDropNumber { get; init; }
    public int? ModiconSlotNumber { get; init; }
    public ObservableCollection<HardwareTreeNode> Children { get; init; } = [];
}

public sealed class HardwareLinkTarget
{
    public long BoxEntityId { get; init; }
    public string BoxName { get; init; } = string.Empty;
    public int? ModuleNumber { get; init; }
    public long? PointEntityId { get; init; }
}
