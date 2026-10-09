using System;
using System.Collections.Generic;

namespace Corevanta.Desktop.Models;

public sealed class EntitySnapshotRow
{
    public string SearchKey { get; init; } = string.Empty;
    public string EntityName { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public string Signature { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Properties { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed class EbImportChangeSummary
{
    public long RunId { get; init; }
    public DateTime ImportedAtUtc { get; init; }
    public int ChangedCount { get; init; }
    public int DeletedCount { get; init; }
}

public sealed class ChangeManagementEntry
{
    public DateTime ImportedAtUtc { get; init; }
    public string ChangeType { get; init; } = string.Empty;
    public string EntityName { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
    public string ImportedAtLocalText => ImportedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}
