namespace Corevanta.Desktop.Models;

public sealed class ChartDetail
{
    public long EntityId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public string SourceFile { get; init; } = string.Empty;
    public IReadOnlyList<string> PackageNames { get; init; } = [];
    public IReadOnlyList<ChartInputReference> Inputs { get; init; } = [];
    public IReadOnlyList<ChartOutputReference> Outputs { get; init; } = [];
    public IReadOnlyList<ChartCrossReference> CrossReferences { get; init; } = [];
    public IReadOnlyList<ChartLogicReference> LogicReferences { get; init; } = [];
    public IReadOnlyList<ChartClReference> ClReferences { get; init; } = [];
    public IReadOnlyList<ChartPlcReference> PlcReferences { get; init; } = [];
    public IReadOnlyList<ChartSoftReference> SoftReferences { get; init; } = [];
    public IReadOnlyList<ChartPiReference> PiReferences { get; init; } = [];
}

public sealed class ChartInputReference
{
    public string SourceProperty { get; init; } = string.Empty;
    public int Index { get; init; }
    public string SourceRaw { get; init; } = string.Empty;
    public string SourceEntity { get; init; } = string.Empty;
    public string DestinationParameter { get; init; } = string.Empty;
}

public sealed class ChartOutputReference
{
    public string SourceProperty { get; init; } = string.Empty;
    public int Index { get; init; }
    public string DestinationRaw { get; init; } = string.Empty;
    public string DestinationEntity { get; init; } = string.Empty;
}

public sealed class ChartCrossReference
{
    public long RelatedEntityId { get; init; }
    public string RelatedEntity { get; init; } = string.Empty;
    public string PropertyName { get; init; } = string.Empty;
    public int Index { get; init; }
    public string ReferenceRaw { get; init; } = string.Empty;

    // True: related entity points into selected entity (related -> selected).
    // False: selected entity points into related entity (selected -> related).
    public bool IsInboundToSelected { get; init; }
}

public sealed class ChartClReference
{
    public long ClProgramId { get; init; }
    public string ProgramName { get; init; } = string.Empty;
    public string ProgramType { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public string ReferenceKind { get; init; } = string.Empty;
    public string ExternalTag { get; init; } = string.Empty;
    public int LineNumber { get; init; }
    public string DeclarationText { get; init; } = string.Empty;
    public bool IsWriteToTag { get; init; }
    public int? SetLineNumber { get; init; }
}

public sealed class ChartLogicReference
{
    public long LogicEntityId { get; init; }
    public string LogicEntity { get; init; } = string.Empty;
    public string PropertyName { get; init; } = string.Empty;
    public int Index { get; init; }
    public string ReferenceRaw { get; init; } = string.Empty;

    // True: logic block points into selected entity (logic -> selected).
    // False: selected entity points into logic block (selected -> logic).
    public bool IsInboundToSelected { get; init; }
}

public sealed class ChartPlcReference
{
    public string InterfaceType { get; init; } = string.Empty;
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
    public string RegisterAddress { get; init; } = string.Empty;
    public string TagName { get; init; } = string.Empty;
    public string SourceLine { get; init; } = string.Empty;

    public string HwyBoxDisplay =>
        string.IsNullOrWhiteSpace(HwyBoxNumbersCsv)
            ? (HwyBoxNumber.HasValue ? HwyBoxNumber.Value.ToString() : "--")
            : HwyBoxNumbersCsv.Trim();
}

public sealed class ChartSoftReference
{
    public string RelatedEntity { get; init; } = string.Empty;
    public string SourceReference { get; init; } = string.Empty;
    public string TargetReference { get; init; } = string.Empty;
    public string SoftType { get; init; } = string.Empty;
    public int SourceRowNumber { get; init; }

    // True: related entity points into selected entity (related -> selected).
    // False: selected entity points into related entity (selected -> related).
    public bool IsInboundToSelected { get; init; }
}

public sealed class ChartPiReference
{
    public string PiTagName { get; init; } = string.Empty;
    public string InstrumentTag { get; init; } = string.Empty;
    public int SourceRowNumber { get; init; }
}
