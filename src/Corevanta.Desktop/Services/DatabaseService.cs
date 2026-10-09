using Corevanta.Desktop.Models;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Corevanta.Desktop.Services;

public sealed class DatabaseService
{
    private static readonly Regex IndexedPropertyRegex = new(@"^([A-Z0-9$]+)\((\d+)\)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex GateInputPropertyRegex = new(@"^([RS]\d*)\((\d+)\)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ExternalDeclarationRegex = new(@"^\s*EXTERNAL\s+(.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SetAssignmentRegex = new(@"\bSET\s+([A-Z0-9_$]+(?:\([^)]*\))?(?:\.[A-Z0-9_$]+)?)\s*=", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ClIdentifierRegex = new(@"\b[A-Z][A-Z0-9_$-]*\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ClIfRegex = new(@"\b(IF|ELSEIF)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ClElseRegex = new(@"\bELSE\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ClCallRegex = new(@"\b(CALL|GOSUB|SUBROUTINE)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ClTagConditionRegex = new(@"\b(TAG|CONDITION)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PlcArrowMappingRegex = new(@"(?<left>[A-Z]{1,3}\d+:[A-Z0-9]+(?:/[A-Z0-9]+)?(?:\.[A-Z0-9]+)?)\s*[-=]+\s*>\s*(?<right>[A-Z0-9_$-]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SoftConnectionValueRegex = new(@"^\*?(?<tag>[A-Z0-9_]+)\.(?<parameter>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly string _connectionString;

    public DatabaseService(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        _connectionString = $"Data Source={databasePath}";
    }

    public async Task InitializeAsync()
    {
        await using var connection = await OpenConnectionAsync();

        var sql = """
            CREATE TABLE IF NOT EXISTS ImportedFiles (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FilePath TEXT NOT NULL UNIQUE,
                FileName TEXT NOT NULL,
                ImportedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Entities (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FileId INTEGER NOT NULL,
                SystemEntityHeader TEXT NOT NULL,
                EntityType TEXT NULL,
                EntityName TEXT NULL,
                SearchKey TEXT NOT NULL,
                FOREIGN KEY (FileId) REFERENCES ImportedFiles(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS EntityProperties (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                EntityId INTEGER NOT NULL,
                PropertyName TEXT NOT NULL,
                PropertyValue TEXT NOT NULL,
                SortOrder INTEGER NOT NULL,
                FOREIGN KEY (EntityId) REFERENCES Entities(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS ClPrograms (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProgramName TEXT NOT NULL,
                ProgramNameKey TEXT NOT NULL,
                ProgramType TEXT NOT NULL,
                FilePath TEXT NOT NULL UNIQUE,
                FileText TEXT NOT NULL,
                ImportedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS LogicLayouts (
                EntityId INTEGER NOT NULL,
                VertexKey TEXT NOT NULL,
                X REAL NOT NULL,
                Y REAL NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                PRIMARY KEY (EntityId, VertexKey),
                FOREIGN KEY (EntityId) REFERENCES Entities(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS AbPlcSystems (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PlcName TEXT NOT NULL,
                PlcNameKey TEXT NOT NULL UNIQUE,
                ConnectionType TEXT NOT NULL,
                ConnectionDetails TEXT NULL,
                HwyNum INTEGER NULL,
                HwyBoxNumber INTEGER NULL,
                HwyBoxNumbersCsv TEXT NULL,
                NodeAddress INTEGER NULL,
                Plc5FileAddress TEXT NULL,
                SerialPort TEXT NULL,
                SerialSettings TEXT NULL,
                SystemFilePath TEXT NOT NULL,
                ImportedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS AbPlcLadderFiles (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PlcSystemId INTEGER NOT NULL,
                FilePath TEXT NOT NULL,
                FileName TEXT NOT NULL,
                ImportedAtUtc TEXT NOT NULL,
                UNIQUE (PlcSystemId, FilePath),
                FOREIGN KEY (PlcSystemId) REFERENCES AbPlcSystems(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS AbPlcRegisterMappings (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PlcSystemId INTEGER NOT NULL,
                RegisterAddress TEXT NOT NULL,
                RegisterKey TEXT NOT NULL,
                TagName TEXT NOT NULL,
                TagKey TEXT NOT NULL,
                SourceLine TEXT NULL,
                FOREIGN KEY (PlcSystemId) REFERENCES AbPlcSystems(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS ModiconSystems (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PlcName TEXT NOT NULL,
                PlcNameKey TEXT NOT NULL UNIQUE,
                ConnectionType TEXT NOT NULL,
                ConnectionDetails TEXT NULL,
                HwyNum INTEGER NULL,
                HwyBoxNumber INTEGER NULL,
                HwyBoxNumbersCsv TEXT NULL,
                NodeAddress INTEGER NULL,
                RegistersFilePath TEXT NOT NULL,
                TrafficCopFilePath TEXT NULL,
                SerialPort TEXT NULL,
                SerialSettings TEXT NULL,
                SystemFilePath TEXT NOT NULL,
                ImportedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ModiconProgramFiles (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ModiconSystemId INTEGER NOT NULL,
                FilePath TEXT NOT NULL,
                FileName TEXT NOT NULL,
                ImportedAtUtc TEXT NOT NULL,
                UNIQUE (ModiconSystemId, FilePath),
                FOREIGN KEY (ModiconSystemId) REFERENCES ModiconSystems(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS ModiconRegisterMappings (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ModiconSystemId INTEGER NOT NULL,
                RegisterAddress TEXT NOT NULL,
                RegisterKey TEXT NOT NULL,
                TagName TEXT NOT NULL,
                TagKey TEXT NOT NULL,
                SourceLine TEXT NULL,
                FOREIGN KEY (ModiconSystemId) REFERENCES ModiconSystems(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS ModiconTrafficCopEntries (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ModiconSystemId INTEGER NOT NULL,
                DropNumber INTEGER NULL,
                SlotNumber INTEGER NOT NULL,
                ModuleText TEXT NOT NULL,
                ReferenceStart TEXT NULL,
                ReferenceEnd TEXT NULL,
                ReferenceRange TEXT NULL,
                DataType TEXT NULL,
                Description TEXT NULL,
                SourceLine TEXT NULL,
                FOREIGN KEY (ModiconSystemId) REFERENCES ModiconSystems(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS SoftConnections (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SourceFilePath TEXT NOT NULL,
                SourceRowNumber INTEGER NOT NULL,
                Name TEXT NOT NULL,
                Type TEXT NOT NULL,
                ParameterName TEXT NOT NULL,
                ParameterValue TEXT NOT NULL,
                SourceEntityKey TEXT NOT NULL,
                SourceReference TEXT NOT NULL,
                TargetEntityKey TEXT NOT NULL,
                TargetReference TEXT NOT NULL,
                ImportedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS GraphicsMappings (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SourceFilePath TEXT NOT NULL,
                SourceRowNumber INTEGER NOT NULL,
                GraphicName TEXT NOT NULL,
                TagKey TEXT NOT NULL,
                ImportedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS PiMappings (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SourceFilePath TEXT NOT NULL,
                SourceRowNumber INTEGER NOT NULL,
                PiTagName TEXT NOT NULL,
                InstrumentTag TEXT NOT NULL,
                InstrumentTagKey TEXT NOT NULL,
                ImportedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ImportActions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ActionType TEXT NOT NULL,
                Title TEXT NOT NULL,
                Detail TEXT NULL,
                TargetKey TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS EbImportRuns (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ImportedAtUtc TEXT NOT NULL,
                ImportedFiles INTEGER NOT NULL,
                ImportedEntities INTEGER NOT NULL,
                ChangedCount INTEGER NOT NULL,
                DeletedCount INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS EbImportEntityChanges (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RunId INTEGER NOT NULL,
                ImportedAtUtc TEXT NOT NULL,
                ChangeType TEXT NOT NULL,
                SearchKey TEXT NOT NULL,
                EntityName TEXT NOT NULL,
                EntityType TEXT NULL,
                Details TEXT NULL,
                FOREIGN KEY (RunId) REFERENCES EbImportRuns(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_Entities_SearchKey ON Entities(SearchKey);
            CREATE INDEX IF NOT EXISTS IX_EntityProperties_EntityId ON EntityProperties(EntityId);
            CREATE INDEX IF NOT EXISTS IX_ClPrograms_ProgramNameKey ON ClPrograms(ProgramNameKey);
            CREATE INDEX IF NOT EXISTS IX_LogicLayouts_EntityId ON LogicLayouts(EntityId);
            CREATE INDEX IF NOT EXISTS IX_AbPlcLadderFiles_PlcSystemId ON AbPlcLadderFiles(PlcSystemId);
            CREATE INDEX IF NOT EXISTS IX_AbPlcRegisterMappings_TagKey ON AbPlcRegisterMappings(TagKey);
            CREATE INDEX IF NOT EXISTS IX_AbPlcRegisterMappings_RegisterKey ON AbPlcRegisterMappings(RegisterKey);
            CREATE INDEX IF NOT EXISTS IX_ModiconProgramFiles_ModiconSystemId ON ModiconProgramFiles(ModiconSystemId);
            CREATE INDEX IF NOT EXISTS IX_ModiconRegisterMappings_TagKey ON ModiconRegisterMappings(TagKey);
            CREATE INDEX IF NOT EXISTS IX_ModiconRegisterMappings_RegisterKey ON ModiconRegisterMappings(RegisterKey);
            CREATE INDEX IF NOT EXISTS IX_ModiconTrafficCopEntries_SystemDropSlot ON ModiconTrafficCopEntries(ModiconSystemId, DropNumber, SlotNumber);
            CREATE INDEX IF NOT EXISTS IX_SoftConnections_SourceEntityKey ON SoftConnections(SourceEntityKey);
            CREATE INDEX IF NOT EXISTS IX_SoftConnections_TargetEntityKey ON SoftConnections(TargetEntityKey);
            CREATE INDEX IF NOT EXISTS IX_GraphicsMappings_TagKey ON GraphicsMappings(TagKey);
            CREATE INDEX IF NOT EXISTS IX_PiMappings_InstrumentTagKey ON PiMappings(InstrumentTagKey);
            CREATE INDEX IF NOT EXISTS IX_ImportActions_CreatedAtUtc ON ImportActions(CreatedAtUtc DESC);
            CREATE INDEX IF NOT EXISTS IX_EbImportRuns_ImportedAtUtc ON EbImportRuns(ImportedAtUtc DESC);
            CREATE INDEX IF NOT EXISTS IX_EbImportEntityChanges_RunId ON EbImportEntityChanges(RunId);
            CREATE INDEX IF NOT EXISTS IX_EbImportEntityChanges_ImportedAtUtc ON EbImportEntityChanges(ImportedAtUtc DESC);
            """;

        await using var command = new SqliteCommand(sql, connection);
        await command.ExecuteNonQueryAsync();

        await EnsureColumnExistsAsync(connection, "AbPlcSystems", "HwyNum", "INTEGER NULL");
        await EnsureColumnExistsAsync(connection, "AbPlcSystems", "HwyBoxNumber", "INTEGER NULL");
        await EnsureColumnExistsAsync(connection, "AbPlcSystems", "HwyBoxNumbersCsv", "TEXT NULL");
        await EnsureColumnExistsAsync(connection, "AbPlcSystems", "NodeAddress", "INTEGER NULL");
        await EnsureColumnExistsAsync(connection, "AbPlcSystems", "Plc5FileAddress", "TEXT NULL");
        await EnsureColumnExistsAsync(connection, "AbPlcSystems", "SerialPort", "TEXT NULL");
        await EnsureColumnExistsAsync(connection, "AbPlcSystems", "SerialSettings", "TEXT NULL");
        await EnsureColumnExistsAsync(connection, "ModiconSystems", "HwyNum", "INTEGER NULL");
        await EnsureColumnExistsAsync(connection, "ModiconSystems", "HwyBoxNumber", "INTEGER NULL");
        await EnsureColumnExistsAsync(connection, "ModiconSystems", "HwyBoxNumbersCsv", "TEXT NULL");
        await EnsureColumnExistsAsync(connection, "ModiconSystems", "NodeAddress", "INTEGER NULL");
        await EnsureColumnExistsAsync(connection, "ModiconSystems", "TrafficCopFilePath", "TEXT NULL");
        await EnsureColumnExistsAsync(connection, "ModiconSystems", "SerialPort", "TEXT NULL");
        await EnsureColumnExistsAsync(connection, "ModiconSystems", "SerialSettings", "TEXT NULL");
    }

    public async Task<(int ImportedFiles, int Entities, int ClPrograms, int AbSystems, int ModiconSystems, int SoftConnections, int OpcIntegratorMappings, int GraphicsMappings, int PiMappings)> GetImportCountsAsync()
    {
        await using var connection = await OpenConnectionAsync();

        const string sql = """
            SELECT
                (SELECT COUNT(1) FROM ImportedFiles) AS ImportedFiles,
                (SELECT COUNT(1) FROM Entities) AS Entities,
                (SELECT COUNT(1) FROM ClPrograms) AS ClPrograms,
                (SELECT COUNT(1) FROM AbPlcSystems) AS AbSystems,
                (SELECT COUNT(1) FROM ModiconSystems) AS ModiconSystems,
                (SELECT COUNT(1) FROM SoftConnections WHERE UPPER(Type) <> 'OPCI_INTEGRATOR') AS SoftConnections,
                (SELECT COUNT(1) FROM SoftConnections WHERE UPPER(Type) = 'OPCI_INTEGRATOR') AS OpcIntegratorMappings,
                (SELECT COUNT(1) FROM GraphicsMappings) AS GraphicsMappings,
                (SELECT COUNT(1) FROM PiMappings) AS PiMappings;
            """;

        await using var command = new SqliteCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return (0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        return (
            ImportedFiles: reader.GetInt32(0),
            Entities: reader.GetInt32(1),
            ClPrograms: reader.GetInt32(2),
            AbSystems: reader.GetInt32(3),
            ModiconSystems: reader.GetInt32(4),
            SoftConnections: reader.GetInt32(5),
            OpcIntegratorMappings: reader.GetInt32(6),
            GraphicsMappings: reader.GetInt32(7),
            PiMappings: reader.GetInt32(8));
    }

    public async Task<Dictionary<string, EntitySnapshotRow>> GetEntitySnapshotAsync()
    {
        await using var connection = await OpenConnectionAsync();
        const string sql = """
            SELECT
                e.Id,
                e.SearchKey,
                COALESCE(NULLIF(TRIM(e.EntityName), ''), TRIM(e.SystemEntityHeader)) AS DisplayName,
                COALESCE(e.EntityType, '') AS EntityType,
                p.PropertyName,
                p.PropertyValue,
                p.SortOrder
            FROM Entities e
            LEFT JOIN EntityProperties p ON p.EntityId = e.Id
            ORDER BY e.Id, p.SortOrder, p.PropertyName, p.Id;
            """;

        await using var command = new SqliteCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        var result = new Dictionary<string, EntitySnapshotRow>(StringComparer.OrdinalIgnoreCase);
        long currentEntityId = -1;
        string currentSearchKey = string.Empty;
        string currentEntityName = string.Empty;
        string currentEntityType = string.Empty;
        var signatureBuilder = new StringBuilder();
        var currentProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void flushCurrent()
        {
            if (currentEntityId <= 0 || string.IsNullOrWhiteSpace(currentSearchKey))
            {
                return;
            }

            var signature = ComputeSha256Hex(signatureBuilder.ToString());
            result[currentSearchKey] = new EntitySnapshotRow
            {
                SearchKey = currentSearchKey,
                EntityName = currentEntityName,
                EntityType = currentEntityType,
                Signature = signature,
                Properties = new Dictionary<string, string>(currentProperties, StringComparer.OrdinalIgnoreCase)
            };
        }

        while (await reader.ReadAsync())
        {
            var entityId = reader.GetInt64(0);
            if (entityId != currentEntityId)
            {
                flushCurrent();

                currentEntityId = entityId;
                currentSearchKey = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                currentEntityName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                currentEntityType = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                signatureBuilder.Clear();
                currentProperties.Clear();
                signatureBuilder
                    .Append("NAME=").Append(currentEntityName)
                    .Append(";TYPE=").Append(currentEntityType)
                    .Append(';');
            }

            if (!reader.IsDBNull(4))
            {
                var propertyName = reader.GetString(4);
                var propertyValue = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);
                if (!string.IsNullOrWhiteSpace(propertyName))
                {
                    if (!currentProperties.TryGetValue(propertyName, out var existing))
                    {
                        currentProperties[propertyName] = propertyValue;
                    }
                    else if (!existing.Equals(propertyValue, StringComparison.Ordinal))
                    {
                        currentProperties[propertyName] = $"{existing} | {propertyValue}";
                    }
                }

                signatureBuilder
                    .Append(propertyName)
                    .Append('=')
                    .Append(propertyValue)
                    .Append(';');
            }
        }

        flushCurrent();
        return result;
    }

    public async Task<EbImportChangeSummary> RecordEbImportChangeSetAsync(
        IReadOnlyDictionary<string, EntitySnapshotRow> previousSnapshot,
        int importedFiles,
        int importedEntities)
    {
        var before = previousSnapshot ?? new Dictionary<string, EntitySnapshotRow>(StringComparer.OrdinalIgnoreCase);
        var after = await GetEntitySnapshotAsync();
        var importedAtUtc = DateTime.UtcNow;

        var changed = new List<(EntitySnapshotRow Before, EntitySnapshotRow After)>();
        var deleted = new List<EntitySnapshotRow>();

        foreach (var kvp in before)
        {
            if (!after.TryGetValue(kvp.Key, out var current))
            {
                deleted.Add(kvp.Value);
                continue;
            }

            if (!string.Equals(kvp.Value.Signature, current.Signature, StringComparison.Ordinal))
            {
                changed.Add((kvp.Value, current));
            }
        }

        await using var connection = await OpenConnectionAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        const string insertRunSql = """
            INSERT INTO EbImportRuns (ImportedAtUtc, ImportedFiles, ImportedEntities, ChangedCount, DeletedCount)
            VALUES ($importedAtUtc, $importedFiles, $importedEntities, $changedCount, $deletedCount);
            SELECT last_insert_rowid();
            """;

        await using var runCommand = new SqliteCommand(insertRunSql, connection, transaction);
        runCommand.Parameters.AddWithValue("$importedAtUtc", importedAtUtc.ToString("O"));
        runCommand.Parameters.AddWithValue("$importedFiles", importedFiles);
        runCommand.Parameters.AddWithValue("$importedEntities", importedEntities);
        runCommand.Parameters.AddWithValue("$changedCount", changed.Count);
        runCommand.Parameters.AddWithValue("$deletedCount", deleted.Count);

        var runId = (long)(await runCommand.ExecuteScalarAsync() ?? 0L);

        const string insertChangeSql = """
            INSERT INTO EbImportEntityChanges (RunId, ImportedAtUtc, ChangeType, SearchKey, EntityName, EntityType, Details)
            VALUES ($runId, $importedAtUtc, $changeType, $searchKey, $entityName, $entityType, $details);
            """;

        foreach (var item in changed)
        {
            await using var changeCommand = new SqliteCommand(insertChangeSql, connection, transaction);
            changeCommand.Parameters.AddWithValue("$runId", runId);
            changeCommand.Parameters.AddWithValue("$importedAtUtc", importedAtUtc.ToString("O"));
            changeCommand.Parameters.AddWithValue("$changeType", "CHANGED");
            changeCommand.Parameters.AddWithValue("$searchKey", item.After.SearchKey);
            changeCommand.Parameters.AddWithValue("$entityName", item.After.EntityName);
            changeCommand.Parameters.AddWithValue("$entityType", item.After.EntityType);
            changeCommand.Parameters.AddWithValue("$details", BuildEntityChangeDetails(item.Before, item.After));
            await changeCommand.ExecuteNonQueryAsync();
        }

        foreach (var item in deleted)
        {
            await using var changeCommand = new SqliteCommand(insertChangeSql, connection, transaction);
            changeCommand.Parameters.AddWithValue("$runId", runId);
            changeCommand.Parameters.AddWithValue("$importedAtUtc", importedAtUtc.ToString("O"));
            changeCommand.Parameters.AddWithValue("$changeType", "DELETED");
            changeCommand.Parameters.AddWithValue("$searchKey", item.SearchKey);
            changeCommand.Parameters.AddWithValue("$entityName", item.EntityName);
            changeCommand.Parameters.AddWithValue("$entityType", item.EntityType);
            changeCommand.Parameters.AddWithValue("$details", "Entity was present in previous import but not in current import.");
            await changeCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        return new EbImportChangeSummary
        {
            RunId = runId,
            ImportedAtUtc = importedAtUtc,
            ChangedCount = changed.Count,
            DeletedCount = deleted.Count
        };
    }

    public async Task<IReadOnlyList<ChangeManagementEntry>> GetChangeManagementEntriesAsync(int maxResults = 5000)
    {
        await using var connection = await OpenConnectionAsync();
        const string sql = """
            SELECT
                c.ImportedAtUtc,
                c.ChangeType,
                c.EntityName,
                c.EntityType,
                c.Details
            FROM EbImportEntityChanges c
            ORDER BY datetime(c.ImportedAtUtc) DESC, c.Id DESC
            LIMIT $maxResults;
            """;

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("$maxResults", maxResults <= 0 ? 5000 : maxResults);

        var entries = new List<ChangeManagementEntry>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            DateTime.TryParse(
                reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var importedAtUtc);

            entries.Add(new ChangeManagementEntry
            {
                ImportedAtUtc = importedAtUtc == default ? DateTime.UtcNow : importedAtUtc,
                ChangeType = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                EntityName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                EntityType = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                Details = reader.IsDBNull(4) ? string.Empty : reader.GetString(4)
            });
        }

        return entries;
    }

    private static string ComputeSha256Hex(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static string BuildEntityChangeDetails(EntitySnapshotRow before, EntitySnapshotRow after)
    {
        var details = new List<string>();

        if (!string.Equals(before.EntityName, after.EntityName, StringComparison.Ordinal))
        {
            details.Add($"Name: {FormatDiffValue(before.EntityName, exists: true)} -> {FormatDiffValue(after.EntityName, exists: true)}");
        }

        if (!string.Equals(before.EntityType, after.EntityType, StringComparison.Ordinal))
        {
            details.Add($"Type: {FormatDiffValue(before.EntityType, exists: true)} -> {FormatDiffValue(after.EntityType, exists: true)}");
        }

        var allKeys = before.Properties.Keys
            .Concat(after.Properties.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        const int maxLines = 20;
        var addedLines = 0;

        foreach (var key in allKeys)
        {
            var hasBefore = before.Properties.TryGetValue(key, out var beforeValue);
            var hasAfter = after.Properties.TryGetValue(key, out var afterValue);
            var sameValue = hasBefore == hasAfter
                            && string.Equals(beforeValue ?? string.Empty, afterValue ?? string.Empty, StringComparison.Ordinal);
            if (sameValue)
            {
                continue;
            }

            if (addedLines < maxLines)
            {
                details.Add($"{key}: {FormatDiffValue(beforeValue, hasBefore)} -> {FormatDiffValue(afterValue, hasAfter)}");
            }

            addedLines++;
        }

        if (addedLines > maxLines)
        {
            details.Add($"+{addedLines - maxLines} more parameter changes");
        }

        return details.Count == 0
            ? "Entity definition changed."
            : string.Join("; ", details);
    }

    private static string FormatDiffValue(string? value, bool exists)
    {
        if (!exists)
        {
            return "(missing)";
        }

        var normalized = string.IsNullOrWhiteSpace(value)
            ? "(blank)"
            : value.Trim().Replace('\r', ' ').Replace('\n', ' ');
        if (normalized.Length > 120)
        {
            normalized = normalized[..117] + "...";
        }

        return $"'{normalized}'";
    }

    public async Task RecordImportActionAsync(string actionType, string title, string detail, string targetKey)
    {
        if (string.IsNullOrWhiteSpace(actionType) || string.IsNullOrWhiteSpace(targetKey))
        {
            return;
        }

        await using var connection = await OpenConnectionAsync();
        const string sql = """
            INSERT INTO ImportActions (ActionType, Title, Detail, TargetKey, CreatedAtUtc)
            VALUES ($actionType, $title, $detail, $targetKey, $createdAtUtc);
            """;

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("$actionType", actionType.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("$title", string.IsNullOrWhiteSpace(title) ? actionType.Trim() : title.Trim());
        command.Parameters.AddWithValue("$detail", string.IsNullOrWhiteSpace(detail) ? string.Empty : detail.Trim());
        command.Parameters.AddWithValue("$targetKey", targetKey.Trim());
        command.Parameters.AddWithValue("$createdAtUtc", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<ImportActionRecord>> GetImportActionHistoryAsync(int maxResults = 250)
    {
        await using var connection = await OpenConnectionAsync();
        const string sql = """
            SELECT Id, ActionType, Title, Detail, TargetKey, CreatedAtUtc
            FROM ImportActions
            ORDER BY datetime(CreatedAtUtc) DESC, Id DESC
            LIMIT $maxResults;
            """;

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("$maxResults", maxResults <= 0 ? 250 : maxResults);

        var results = new List<ImportActionRecord>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            DateTime.TryParse(
                reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var createdAtUtc);

            results.Add(new ImportActionRecord
            {
                Id = reader.GetInt64(0),
                ActionType = reader.GetString(1),
                Title = reader.GetString(2),
                Detail = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                TargetKey = reader.GetString(4),
                CreatedAtUtc = createdAtUtc == default ? DateTime.UtcNow : createdAtUtc
            });
        }

        return results;
    }

    public async Task<string> UndoImportActionAsync(long importActionId)
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        const string getActionSql = """
            SELECT ActionType, Title, Detail, TargetKey
            FROM ImportActions
            WHERE Id = $id;
            """;

        await using var getActionCommand = new SqliteCommand(getActionSql, connection, transaction);
        getActionCommand.Parameters.AddWithValue("$id", importActionId);
        await using var reader = await getActionCommand.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            await transaction.RollbackAsync();
            return "Import action not found.";
        }

        var actionType = reader.GetString(0).Trim().ToUpperInvariant();
        var title = reader.GetString(1);
        var targetKey = reader.GetString(3).Trim();
        await reader.CloseAsync();

        var undoRows = 0;
        switch (actionType)
        {
            case "EB_FILE":
            {
                const string sql = "DELETE FROM ImportedFiles WHERE FilePath = $targetKey;";
                await using var command = new SqliteCommand(sql, connection, transaction);
                command.Parameters.AddWithValue("$targetKey", targetKey);
                undoRows = await command.ExecuteNonQueryAsync();
                break;
            }
            case "CL_FOLDER":
            {
                const string sql = """
                    DELETE FROM ClPrograms
                    WHERE UPPER(FilePath) LIKE UPPER($prefix) || '%';
                    """;
                await using var command = new SqliteCommand(sql, connection, transaction);
                command.Parameters.AddWithValue("$prefix", targetKey);
                undoRows = await command.ExecuteNonQueryAsync();
                break;
            }
            case "AB_SYSTEM":
            {
                const string sql = "DELETE FROM AbPlcSystems WHERE PlcNameKey = $targetKey;";
                await using var command = new SqliteCommand(sql, connection, transaction);
                command.Parameters.AddWithValue("$targetKey", targetKey.ToUpperInvariant());
                undoRows = await command.ExecuteNonQueryAsync();
                break;
            }
            case "MODICON_SYSTEM":
            {
                const string sql = "DELETE FROM ModiconSystems WHERE PlcNameKey = $targetKey;";
                await using var command = new SqliteCommand(sql, connection, transaction);
                command.Parameters.AddWithValue("$targetKey", targetKey.ToUpperInvariant());
                undoRows = await command.ExecuteNonQueryAsync();
                break;
            }
            case "SOFT_CONNECTIONS_FILE":
            {
                const string sql = "DELETE FROM SoftConnections WHERE SourceFilePath = $targetKey;";
                await using var command = new SqliteCommand(sql, connection, transaction);
                command.Parameters.AddWithValue("$targetKey", targetKey);
                undoRows = await command.ExecuteNonQueryAsync();
                break;
            }
            case "OPC_INTEGRATOR_FILE":
            {
                const string sql = """
                    DELETE FROM SoftConnections
                    WHERE SourceFilePath = $targetKey
                      AND UPPER(Type) = 'OPCI_INTEGRATOR';
                    """;
                await using var command = new SqliteCommand(sql, connection, transaction);
                command.Parameters.AddWithValue("$targetKey", targetKey);
                undoRows = await command.ExecuteNonQueryAsync();
                break;
            }
            case "GRAPHICS_FILE":
            {
                const string sql = "DELETE FROM GraphicsMappings WHERE SourceFilePath = $targetKey;";
                await using var command = new SqliteCommand(sql, connection, transaction);
                command.Parameters.AddWithValue("$targetKey", targetKey);
                undoRows = await command.ExecuteNonQueryAsync();
                break;
            }
            case "PI_FILE":
            {
                const string sql = "DELETE FROM PiMappings WHERE SourceFilePath = $targetKey;";
                await using var command = new SqliteCommand(sql, connection, transaction);
                command.Parameters.AddWithValue("$targetKey", targetKey);
                undoRows = await command.ExecuteNonQueryAsync();
                break;
            }
            default:
                await transaction.RollbackAsync();
                return $"Undo is not supported for action type '{actionType}'.";
        }

        const string deleteActionSql = "DELETE FROM ImportActions WHERE Id = $id;";
        await using var deleteActionCommand = new SqliteCommand(deleteActionSql, connection, transaction);
        deleteActionCommand.Parameters.AddWithValue("$id", importActionId);
        await deleteActionCommand.ExecuteNonQueryAsync();

        await transaction.CommitAsync();
        return $"Undid import '{title}' ({undoRows} base row(s) removed).";
    }

    public async Task<int> ImportFileAsync(string filePath, IReadOnlyList<ParsedEntity> entities)
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        var fileId = await UpsertFileAsync(connection, transaction, filePath);
        await DeleteFileEntitiesAsync(connection, transaction, fileId);

        var entityInsertSql = """
            INSERT INTO Entities (FileId, SystemEntityHeader, EntityType, EntityName, SearchKey)
            VALUES ($fileId, $systemEntityHeader, $entityType, $entityName, $searchKey);
            SELECT last_insert_rowid();
            """;

        var propertyInsertSql = """
            INSERT INTO EntityProperties (EntityId, PropertyName, PropertyValue, SortOrder)
            VALUES ($entityId, $propertyName, $propertyValue, $sortOrder);
            """;

        foreach (var entity in entities)
        {
            await using var entityCommand = new SqliteCommand(entityInsertSql, connection, transaction);
            entityCommand.Parameters.AddWithValue("$fileId", fileId);
            entityCommand.Parameters.AddWithValue("$systemEntityHeader", entity.SystemEntityHeader);
            entityCommand.Parameters.AddWithValue("$entityType", (object?)entity.EntityType ?? DBNull.Value);
            entityCommand.Parameters.AddWithValue("$entityName", (object?)entity.EntityName ?? DBNull.Value);
            entityCommand.Parameters.AddWithValue("$searchKey", BuildSearchKey(entity));

            var entityId = (long)(await entityCommand.ExecuteScalarAsync() ?? 0L);

            foreach (var property in entity.Properties)
            {
                await using var propertyCommand = new SqliteCommand(propertyInsertSql, connection, transaction);

                propertyCommand.Parameters.AddWithValue("$entityId", entityId);
                propertyCommand.Parameters.AddWithValue("$propertyName", property.PropertyName);
                propertyCommand.Parameters.AddWithValue("$propertyValue", property.PropertyValue);
                propertyCommand.Parameters.AddWithValue("$sortOrder", property.SortOrder);
                await propertyCommand.ExecuteNonQueryAsync();
            }
        }

        await transaction.CommitAsync();
        return entities.Count;
    }

    public async Task ClearAllDataAsync(bool clearChangeManagementHistory = true)
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        var sql = """
            DELETE FROM LogicLayouts;
            DELETE FROM EntityProperties;
            DELETE FROM Entities;
            DELETE FROM ImportedFiles;
            DELETE FROM ClPrograms;
            DELETE FROM AbPlcRegisterMappings;
            DELETE FROM AbPlcLadderFiles;
            DELETE FROM AbPlcSystems;
            DELETE FROM ModiconTrafficCopEntries;
            DELETE FROM ModiconRegisterMappings;
            DELETE FROM ModiconProgramFiles;
            DELETE FROM ModiconSystems;
            DELETE FROM SoftConnections;
            DELETE FROM GraphicsMappings;
            DELETE FROM PiMappings;
            DELETE FROM ImportActions;
            """;

        if (clearChangeManagementHistory)
        {
            sql += """

                DELETE FROM EbImportEntityChanges;
                DELETE FROM EbImportRuns;
                """;
        }

        await using var command = new SqliteCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    public async Task<int> ImportClProgramsFromFolderAsync(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"Folder not found: {folderPath}");
        }

        var clFiles = Directory
            .EnumerateFiles(folderPath, "*.cl", SearchOption.AllDirectories)
            .ToList();

        await using var connection = await OpenConnectionAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        const string upsertSql = """
            INSERT INTO ClPrograms (ProgramName, ProgramNameKey, ProgramType, FilePath, FileText, ImportedAtUtc)
            VALUES ($programName, $programNameKey, $programType, $filePath, $fileText, $importedAtUtc)
            ON CONFLICT(FilePath) DO UPDATE SET
                ProgramName = excluded.ProgramName,
                ProgramNameKey = excluded.ProgramNameKey,
                ProgramType = excluded.ProgramType,
                FileText = excluded.FileText,
                ImportedAtUtc = excluded.ImportedAtUtc;
            """;

        foreach (var file in clFiles)
        {
            var programName = Path.GetFileNameWithoutExtension(file).Trim();
            var text = await File.ReadAllTextAsync(file);

            await using var command = new SqliteCommand(upsertSql, connection, transaction);
            command.Parameters.AddWithValue("$programName", programName);
            command.Parameters.AddWithValue("$programNameKey", NormalizeProgramKey(programName));
            command.Parameters.AddWithValue("$programType", GetProgramTypeFromPath(file));
            command.Parameters.AddWithValue("$filePath", file);
            command.Parameters.AddWithValue("$fileText", text);
            command.Parameters.AddWithValue("$importedAtUtc", DateTime.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        return clFiles.Count;
    }

    public async Task<(int ImportedCount, int SkippedCount, int TotalRows)> ImportSoftConnectionsAsync(string workbookPath)
    {
        if (string.IsNullOrWhiteSpace(workbookPath))
        {
            throw new ArgumentException("Workbook path is required.", nameof(workbookPath));
        }

        var normalizedWorkbookPath = workbookPath.Trim();
        if (!File.Exists(normalizedWorkbookPath))
        {
            throw new FileNotFoundException($"Workbook not found: {normalizedWorkbookPath}", normalizedWorkbookPath);
        }

        using var workbook = new XLWorkbook(normalizedWorkbookPath);
        var worksheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidOperationException("Workbook does not contain any worksheets.");
        var headerRow = worksheet.FirstRowUsed()
            ?? throw new InvalidOperationException("Workbook does not contain a header row.");

        var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in headerRow.CellsUsed())
        {
            var text = cell.GetString().Trim();
            if (!string.IsNullOrWhiteSpace(text) && !headerMap.ContainsKey(text))
            {
                headerMap[text] = cell.Address.ColumnNumber;
            }
        }

        if (!headerMap.TryGetValue("Name", out var nameCol)
            || !headerMap.TryGetValue("Type", out var typeCol)
            || !headerMap.TryGetValue("Parameter Name", out var parameterNameCol)
            || !headerMap.TryGetValue("Parameter Value", out var parameterValueCol))
        {
            throw new InvalidOperationException(
                "Workbook must include headers: Name, Type, Parameter Name, Parameter Value.");
        }

        var firstDataRow = headerRow.RowNumber() + 1;
        var lastDataRow = worksheet.LastRowUsed()?.RowNumber() ?? firstDataRow - 1;

        var rows = new List<(
            int SourceRowNumber,
            string Name,
            string Type,
            string ParameterName,
            string ParameterValue,
            string SourceEntityKey,
            string SourceReference,
            string TargetEntityKey,
            string TargetReference)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var totalRows = 0;
        var skipped = 0;
        for (var rowNumber = firstDataRow; rowNumber <= lastDataRow; rowNumber++)
        {
            var row = worksheet.Row(rowNumber);
            var name = row.Cell(nameCol).GetString().Trim();
            var type = row.Cell(typeCol).GetString().Trim();
            var parameterNameRaw = row.Cell(parameterNameCol).GetString().Trim();
            var parameterValueRaw = row.Cell(parameterValueCol).GetString().Trim();

            if (string.IsNullOrWhiteSpace(name)
                && string.IsNullOrWhiteSpace(type)
                && string.IsNullOrWhiteSpace(parameterNameRaw)
                && string.IsNullOrWhiteSpace(parameterValueRaw))
            {
                continue;
            }

            totalRows++;

            if (string.IsNullOrWhiteSpace(parameterValueRaw))
            {
                skipped++;
                continue;
            }

            var parameterValue = NormalizeConnectionValue(parameterValueRaw).ToUpperInvariant();
            var match = SoftConnectionValueRegex.Match(parameterValue);
            if (!match.Success)
            {
                skipped++;
                continue;
            }

            var tdcTag = match.Groups["tag"].Value.Trim().ToUpperInvariant();
            var tdcParameter = match.Groups["parameter"].Value.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(tdcTag) || string.IsNullOrWhiteSpace(tdcParameter))
            {
                skipped++;
                continue;
            }

            var parameterName = NormalizeSoftParameterName(parameterNameRaw);
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(parameterName))
            {
                skipped++;
                continue;
            }

            var systemReference = $"{name.Trim()}.{parameterName}";
            var tdcReference = $"{tdcTag}.{tdcParameter}";
            var isPush = type.Equals("UTILITY:PUSH", StringComparison.OrdinalIgnoreCase);
            var upperParameterName = parameterName.ToUpperInvariant();
            var pushHasOut = upperParameterName.Contains("OUT", StringComparison.Ordinal);

            // Direction rules:
            // - UTILITY:PUSH + OUT => input to TDC (system -> TDC)
            // - UTILITY:PUSH + IN => output from TDC (TDC -> system)
            // - Non-PUSH => output from TDC (TDC -> system)
            var sourceReference = (isPush && pushHasOut)
                ? systemReference
                : tdcReference;
            var targetReference = (isPush && pushHasOut)
                ? tdcReference
                : systemReference;

            var sourceEntityKey = ExtractConnectionEntity(sourceReference).ToUpperInvariant();
            var targetEntityKey = ExtractConnectionEntity(targetReference).ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(sourceEntityKey) || string.IsNullOrWhiteSpace(targetEntityKey))
            {
                skipped++;
                continue;
            }

            var uniqueKey = $"{sourceEntityKey}|{sourceReference}|{targetEntityKey}|{targetReference}";
            if (!seen.Add(uniqueKey))
            {
                continue;
            }

            rows.Add((
                rowNumber,
                name,
                type,
                parameterName,
                parameterValueRaw,
                sourceEntityKey,
                sourceReference,
                targetEntityKey,
                targetReference));
        }

        await using var connection = await OpenConnectionAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        await using (var clearCommand = new SqliteCommand("DELETE FROM SoftConnections WHERE UPPER(Type) <> 'OPCI_INTEGRATOR';", connection, transaction))
        {
            await clearCommand.ExecuteNonQueryAsync();
        }

        const string insertSql = """
            INSERT INTO SoftConnections (
                SourceFilePath,
                SourceRowNumber,
                Name,
                Type,
                ParameterName,
                ParameterValue,
                SourceEntityKey,
                SourceReference,
                TargetEntityKey,
                TargetReference,
                ImportedAtUtc)
            VALUES (
                $sourceFilePath,
                $sourceRowNumber,
                $name,
                $type,
                $parameterName,
                $parameterValue,
                $sourceEntityKey,
                $sourceReference,
                $targetEntityKey,
                $targetReference,
                $importedAtUtc);
            """;

        var importedAtUtc = DateTime.UtcNow.ToString("O");
        foreach (var row in rows)
        {
            await using var insertCommand = new SqliteCommand(insertSql, connection, transaction);
            insertCommand.Parameters.AddWithValue("$sourceFilePath", normalizedWorkbookPath);
            insertCommand.Parameters.AddWithValue("$sourceRowNumber", row.SourceRowNumber);
            insertCommand.Parameters.AddWithValue("$name", row.Name);
            insertCommand.Parameters.AddWithValue("$type", row.Type);
            insertCommand.Parameters.AddWithValue("$parameterName", row.ParameterName);
            insertCommand.Parameters.AddWithValue("$parameterValue", row.ParameterValue);
            insertCommand.Parameters.AddWithValue("$sourceEntityKey", row.SourceEntityKey);
            insertCommand.Parameters.AddWithValue("$sourceReference", row.SourceReference);
            insertCommand.Parameters.AddWithValue("$targetEntityKey", row.TargetEntityKey);
            insertCommand.Parameters.AddWithValue("$targetReference", row.TargetReference);
            insertCommand.Parameters.AddWithValue("$importedAtUtc", importedAtUtc);
            await insertCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        return (rows.Count, skipped, totalRows);
    }

    public async Task<(int ImportedCount, int SkippedCount, int TotalRows)> ImportOpcIntegratorMappingsAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path is required.", nameof(filePath));
        }

        var normalizedFilePath = filePath.Trim();
        if (!File.Exists(normalizedFilePath))
        {
            throw new FileNotFoundException($"File not found: {normalizedFilePath}", normalizedFilePath);
        }

        var rows = new List<(
            int SourceRowNumber,
            string Name,
            string Type,
            string ParameterName,
            string ParameterValue,
            string SourceEntityKey,
            string SourceReference,
            string TargetEntityKey,
            string TargetReference)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var totalRows = 0;
        var skipped = 0;
        var sourceRowNumber = 0;

        foreach (var rawLine in File.ReadLines(normalizedFilePath))
        {
            sourceRowNumber++;
            var line = rawLine?.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            totalRows++;

            var parts = line.Split('\t');
            if (parts.Length < 2)
            {
                skipped++;
                continue;
            }

            var sourceItemPath = NormalizeConnectionValue(parts[0]);
            var destinationItemPath = NormalizeConnectionValue(parts[^1]);
            if (string.IsNullOrWhiteSpace(sourceItemPath) || string.IsNullOrWhiteSpace(destinationItemPath))
            {
                skipped++;
                continue;
            }

            var sourceTag = ExtractOpcIntegratorTag(sourceItemPath);
            var destinationTag = ExtractOpcIntegratorTag(destinationItemPath);
            if (string.IsNullOrWhiteSpace(sourceTag) || string.IsNullOrWhiteSpace(destinationTag))
            {
                skipped++;
                continue;
            }

            var sourceReference = sourceTag.ToUpperInvariant();
            var targetReference = destinationTag.ToUpperInvariant();
            var sourceEntityKey = ExtractConnectionEntity(sourceReference).ToUpperInvariant();
            var targetEntityKey = ExtractConnectionEntity(targetReference).ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(sourceEntityKey) || string.IsNullOrWhiteSpace(targetEntityKey))
            {
                skipped++;
                continue;
            }

            var uniqueKey = $"{sourceEntityKey}|{sourceReference}|{targetEntityKey}|{targetReference}";
            if (!seen.Add(uniqueKey))
            {
                continue;
            }

            var directionType = (IsLikelyTdcTag(sourceTag), IsLikelyTdcTag(destinationTag)) switch
            {
                (true, false) => "OPCI_INTEGRATOR:TDC_TO_DEST",
                (false, true) => "OPCI_INTEGRATOR:SOURCE_TO_TDC",
                (true, true) => "OPCI_INTEGRATOR:TDC_TO_TDC",
                _ => "OPCI_INTEGRATOR"
            };

            rows.Add((
                sourceRowNumber,
                "OPC Integrator",
                "OPCI_INTEGRATOR",
                directionType,
                $"{sourceItemPath} -> {destinationItemPath}",
                sourceEntityKey,
                sourceReference,
                targetEntityKey,
                targetReference));
        }

        await using var connection = await OpenConnectionAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        await using (var clearCommand = new SqliteCommand("DELETE FROM SoftConnections WHERE UPPER(Type) = 'OPCI_INTEGRATOR';", connection, transaction))
        {
            await clearCommand.ExecuteNonQueryAsync();
        }

        const string insertSql = """
            INSERT INTO SoftConnections (
                SourceFilePath,
                SourceRowNumber,
                Name,
                Type,
                ParameterName,
                ParameterValue,
                SourceEntityKey,
                SourceReference,
                TargetEntityKey,
                TargetReference,
                ImportedAtUtc)
            VALUES (
                $sourceFilePath,
                $sourceRowNumber,
                $name,
                $type,
                $parameterName,
                $parameterValue,
                $sourceEntityKey,
                $sourceReference,
                $targetEntityKey,
                $targetReference,
                $importedAtUtc);
            """;

        var importedAtUtc = DateTime.UtcNow.ToString("O");
        foreach (var row in rows)
        {
            await using var insertCommand = new SqliteCommand(insertSql, connection, transaction);
            insertCommand.Parameters.AddWithValue("$sourceFilePath", normalizedFilePath);
            insertCommand.Parameters.AddWithValue("$sourceRowNumber", row.SourceRowNumber);
            insertCommand.Parameters.AddWithValue("$name", row.Name);
            insertCommand.Parameters.AddWithValue("$type", row.Type);
            insertCommand.Parameters.AddWithValue("$parameterName", row.ParameterName);
            insertCommand.Parameters.AddWithValue("$parameterValue", row.ParameterValue);
            insertCommand.Parameters.AddWithValue("$sourceEntityKey", row.SourceEntityKey);
            insertCommand.Parameters.AddWithValue("$sourceReference", row.SourceReference);
            insertCommand.Parameters.AddWithValue("$targetEntityKey", row.TargetEntityKey);
            insertCommand.Parameters.AddWithValue("$targetReference", row.TargetReference);
            insertCommand.Parameters.AddWithValue("$importedAtUtc", importedAtUtc);
            await insertCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        return (rows.Count, skipped, totalRows);
    }

    public async Task<(int ImportedCount, int SkippedCount, int TotalRows)> ImportGraphicsPointsAsync(string workbookPath)
    {
        if (string.IsNullOrWhiteSpace(workbookPath))
        {
            throw new ArgumentException("Workbook path is required.", nameof(workbookPath));
        }

        var normalizedWorkbookPath = workbookPath.Trim();
        if (!File.Exists(normalizedWorkbookPath))
        {
            throw new FileNotFoundException($"Workbook not found: {normalizedWorkbookPath}", normalizedWorkbookPath);
        }

        using var workbook = new XLWorkbook(normalizedWorkbookPath);
        var worksheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidOperationException("Workbook does not contain any worksheets.");

        var headerRow = worksheet.RowsUsed()
            .FirstOrDefault(row =>
            {
                var headers = row.CellsUsed()
                    .Select(cell => NormalizeGraphicsHeader(cell.GetString()))
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .ToList();

                var hasEntityName = headers.Any(header => header.Contains("entityname", StringComparison.OrdinalIgnoreCase));
                var hasParamRef = headers.Any(header =>
                    header.Contains("paramvalue", StringComparison.OrdinalIgnoreCase)
                    || header.Contains("paramname", StringComparison.OrdinalIgnoreCase));
                return hasEntityName && hasParamRef;
            })
            ?? throw new InvalidOperationException("Workbook must include headers containing EntityName and paramValue/paramName.");

        var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in headerRow.CellsUsed())
        {
            var text = NormalizeGraphicsHeader(cell.GetString());
            if (!string.IsNullOrWhiteSpace(text) && !headerMap.ContainsKey(text))
            {
                headerMap[text] = cell.Address.ColumnNumber;
            }
        }

        var entityNameCol = FindHeaderColumn(headerMap, "entityname");
        var paramValueCol = FindHeaderColumn(headerMap, "paramvalue");
        if (paramValueCol <= 0)
        {
            paramValueCol = FindHeaderColumn(headerMap, "paramname");
        }

        if (entityNameCol <= 0 || paramValueCol <= 0)
        {
            throw new InvalidOperationException("Workbook must include headers containing EntityName and paramValue/paramName.");
        }

        var firstDataRow = headerRow.RowNumber() + 1;
        var lastDataRow = worksheet.LastRowUsed()?.RowNumber() ?? firstDataRow - 1;

        var rows = new List<(int SourceRowNumber, string GraphicName, string TagKey)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var totalRows = 0;
        var skipped = 0;

        for (var rowNumber = firstDataRow; rowNumber <= lastDataRow; rowNumber++)
        {
            var row = worksheet.Row(rowNumber);
            var graphicRaw = row.Cell(entityNameCol).GetString().Trim();
            var paramValueRaw = row.Cell(paramValueCol).GetString().Trim();

            if (string.IsNullOrWhiteSpace(graphicRaw) && string.IsNullOrWhiteSpace(paramValueRaw))
            {
                continue;
            }

            totalRows++;

            var graphicName = NormalizeGraphicName(graphicRaw);
            var tagKey = ExtractConnectionEntity(paramValueRaw).ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(graphicName) || string.IsNullOrWhiteSpace(tagKey))
            {
                skipped++;
                continue;
            }

            var uniqueKey = $"{tagKey}|{graphicName}";
            if (!seen.Add(uniqueKey))
            {
                continue;
            }

            rows.Add((rowNumber, graphicName, tagKey));
        }

        await using var connection = await OpenConnectionAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        await using (var clearCommand = new SqliteCommand("DELETE FROM GraphicsMappings;", connection, transaction))
        {
            await clearCommand.ExecuteNonQueryAsync();
        }

        const string insertSql = """
            INSERT INTO GraphicsMappings (
                SourceFilePath,
                SourceRowNumber,
                GraphicName,
                TagKey,
                ImportedAtUtc)
            VALUES (
                $sourceFilePath,
                $sourceRowNumber,
                $graphicName,
                $tagKey,
                $importedAtUtc);
            """;

        var importedAtUtc = DateTime.UtcNow.ToString("O");
        foreach (var row in rows)
        {
            await using var insertCommand = new SqliteCommand(insertSql, connection, transaction);
            insertCommand.Parameters.AddWithValue("$sourceFilePath", normalizedWorkbookPath);
            insertCommand.Parameters.AddWithValue("$sourceRowNumber", row.SourceRowNumber);
            insertCommand.Parameters.AddWithValue("$graphicName", row.GraphicName);
            insertCommand.Parameters.AddWithValue("$tagKey", row.TagKey);
            insertCommand.Parameters.AddWithValue("$importedAtUtc", importedAtUtc);
            await insertCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        return (rows.Count, skipped, totalRows);
    }

    public async Task<(int ImportedCount, int SkippedCount, int TotalRows)> ImportPiMappingsAsync(string workbookPath)
    {
        if (string.IsNullOrWhiteSpace(workbookPath))
        {
            throw new ArgumentException("Workbook path is required.", nameof(workbookPath));
        }

        var normalizedWorkbookPath = workbookPath.Trim();
        if (!File.Exists(normalizedWorkbookPath))
        {
            throw new FileNotFoundException($"Workbook not found: {normalizedWorkbookPath}", normalizedWorkbookPath);
        }

        using var workbook = new XLWorkbook(normalizedWorkbookPath);
        var worksheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidOperationException("Workbook does not contain any worksheets.");
        var headerRow = worksheet.RowsUsed()
            .FirstOrDefault(row =>
            {
                var headers = row.CellsUsed()
                    .Select(cell => NormalizeGraphicsHeader(cell.GetString()))
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .ToList();

                var hasName = headers.Any(header => header.Equals("name", StringComparison.OrdinalIgnoreCase));
                var hasInstrumentTag = headers.Any(header =>
                    header.Equals("instrumenttag", StringComparison.OrdinalIgnoreCase)
                    || header.Equals("instrument tag", StringComparison.OrdinalIgnoreCase)
                    || header.Equals("insttag", StringComparison.OrdinalIgnoreCase));
                return hasName && hasInstrumentTag;
            })
            ?? throw new InvalidOperationException("Workbook must include headers for Name and instrumenttag.");

        var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in headerRow.CellsUsed())
        {
            var text = NormalizeGraphicsHeader(cell.GetString());
            if (!string.IsNullOrWhiteSpace(text) && !headerMap.ContainsKey(text))
            {
                headerMap[text] = cell.Address.ColumnNumber;
            }
        }

        var nameCol = FindHeaderColumn(headerMap, "name");
        var instrumentTagCol = FindHeaderColumn(headerMap, "instrumenttag");
        if (instrumentTagCol <= 0)
        {
            instrumentTagCol = FindHeaderColumn(headerMap, "instrument tag");
        }

        if (instrumentTagCol <= 0)
        {
            instrumentTagCol = FindHeaderColumn(headerMap, "insttag");
        }

        if (nameCol <= 0 || instrumentTagCol <= 0)
        {
            throw new InvalidOperationException("Workbook must include headers for Name and instrumenttag.");
        }

        var firstDataRow = headerRow.RowNumber() + 1;
        var lastDataRow = worksheet.LastRowUsed()?.RowNumber() ?? firstDataRow - 1;

        var rows = new List<(int SourceRowNumber, string PiTagName, string InstrumentTag, string InstrumentTagKey)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var totalRows = 0;
        var skipped = 0;

        for (var rowNumber = firstDataRow; rowNumber <= lastDataRow; rowNumber++)
        {
            var row = worksheet.Row(rowNumber);
            var piTagNameRaw = row.Cell(nameCol).GetString().Trim();
            var instrumentTagRaw = row.Cell(instrumentTagCol).GetString().Trim();

            if (string.IsNullOrWhiteSpace(piTagNameRaw) && string.IsNullOrWhiteSpace(instrumentTagRaw))
            {
                continue;
            }

            totalRows++;

            var piTagName = NormalizeConnectionValue(piTagNameRaw);
            var instrumentTag = NormalizeConnectionValue(instrumentTagRaw);
            var instrumentTagKey = ExtractConnectionEntity(instrumentTag).ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(piTagName) || string.IsNullOrWhiteSpace(instrumentTagKey))
            {
                skipped++;
                continue;
            }

            var uniqueKey = $"{instrumentTagKey}|{piTagName}";
            if (!seen.Add(uniqueKey))
            {
                continue;
            }

            rows.Add((rowNumber, piTagName, instrumentTag, instrumentTagKey));
        }

        await using var connection = await OpenConnectionAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        await using (var clearCommand = new SqliteCommand("DELETE FROM PiMappings;", connection, transaction))
        {
            await clearCommand.ExecuteNonQueryAsync();
        }

        const string insertSql = """
            INSERT INTO PiMappings (
                SourceFilePath,
                SourceRowNumber,
                PiTagName,
                InstrumentTag,
                InstrumentTagKey,
                ImportedAtUtc)
            VALUES (
                $sourceFilePath,
                $sourceRowNumber,
                $piTagName,
                $instrumentTag,
                $instrumentTagKey,
                $importedAtUtc);
            """;

        var importedAtUtc = DateTime.UtcNow.ToString("O");
        foreach (var row in rows)
        {
            await using var insertCommand = new SqliteCommand(insertSql, connection, transaction);
            insertCommand.Parameters.AddWithValue("$sourceFilePath", normalizedWorkbookPath);
            insertCommand.Parameters.AddWithValue("$sourceRowNumber", row.SourceRowNumber);
            insertCommand.Parameters.AddWithValue("$piTagName", row.PiTagName);
            insertCommand.Parameters.AddWithValue("$instrumentTag", row.InstrumentTag);
            insertCommand.Parameters.AddWithValue("$instrumentTagKey", row.InstrumentTagKey);
            insertCommand.Parameters.AddWithValue("$importedAtUtc", importedAtUtc);
            await insertCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        return (rows.Count, skipped, totalRows);
    }

    public async Task<IReadOnlyDictionary<string, LogicLayoutNodePosition>> GetLogicLayoutAsync(long entityId)
    {
        await using var connection = await OpenConnectionAsync();

        const string sql = """
            SELECT VertexKey, X, Y
            FROM LogicLayouts
            WHERE EntityId = $entityId;
            """;

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("$entityId", entityId);

        var result = new Dictionary<string, LogicLayoutNodePosition>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var vertexKey = reader.GetString(0).Trim();
            if (string.IsNullOrWhiteSpace(vertexKey))
            {
                continue;
            }

            var position = new LogicLayoutNodePosition
            {
                VertexKey = vertexKey,
                X = reader.GetDouble(1),
                Y = reader.GetDouble(2)
            };
            result[vertexKey] = position;
        }

        return result;
    }

    public async Task SaveLogicLayoutAsync(long entityId, IReadOnlyList<LogicLayoutNodePosition> positions)
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        const string clearSql = "DELETE FROM LogicLayouts WHERE EntityId = $entityId;";
        await using (var clearCommand = new SqliteCommand(clearSql, connection, transaction))
        {
            clearCommand.Parameters.AddWithValue("$entityId", entityId);
            await clearCommand.ExecuteNonQueryAsync();
        }

        const string insertSql = """
            INSERT INTO LogicLayouts (EntityId, VertexKey, X, Y, UpdatedAtUtc)
            VALUES ($entityId, $vertexKey, $x, $y, $updatedAtUtc);
            """;

        var updatedAtUtc = DateTime.UtcNow.ToString("O");
        foreach (var position in positions)
        {
            if (string.IsNullOrWhiteSpace(position.VertexKey))
            {
                continue;
            }

            if (double.IsNaN(position.X) || double.IsInfinity(position.X)
                || double.IsNaN(position.Y) || double.IsInfinity(position.Y))
            {
                continue;
            }

            await using var insertCommand = new SqliteCommand(insertSql, connection, transaction);
            insertCommand.Parameters.AddWithValue("$entityId", entityId);
            insertCommand.Parameters.AddWithValue("$vertexKey", position.VertexKey.Trim());
            insertCommand.Parameters.AddWithValue("$x", position.X);
            insertCommand.Parameters.AddWithValue("$y", position.Y);
            insertCommand.Parameters.AddWithValue("$updatedAtUtc", updatedAtUtc);
            await insertCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    public async Task DeleteLogicLayoutAsync(long entityId)
    {
        await using var connection = await OpenConnectionAsync();

        const string sql = "DELETE FROM LogicLayouts WHERE EntityId = $entityId;";
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("$entityId", entityId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<EntitySearchResult>> SearchEntitiesAsync(
        string query,
        int maxResults = 300,
        bool excludeInternalChannels = false,
        string? entityTypeFilter = null)
    {
        await using var connection = await OpenConnectionAsync();

        const string sql = """
            SELECT
                e.Id,
                COALESCE(NULLIF(TRIM(e.EntityName), ''), e.SystemEntityHeader) AS DisplayName,
                e.EntityType,
                f.FileName
            FROM Entities e
            INNER JOIN ImportedFiles f ON f.Id = e.FileId
            WHERE (
                    $queryLike = ''
                    OR e.SearchKey LIKE $queryLike ESCAPE '\'
                  )
              AND (
                    $excludeInternal = 0
                    OR (
                        UPPER(COALESCE(e.EntityType, '')) NOT IN ('BOX', 'PM_BOX', 'HM_HIST', '$GROUP')
                        AND UPPER(COALESCE(e.EntityName, '')) NOT LIKE '$CH%'
                        AND UPPER(COALESCE(e.EntityName, '')) NOT LIKE '$HY%'
                    )
                  )
              AND (
                    $entityTypeFilter = ''
                    OR UPPER(COALESCE(e.EntityType, '')) = $entityTypeFilter
                  )
            ORDER BY DisplayName
            LIMIT $maxResults;
            """;

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("$queryLike", BuildSqlLikePattern(query, normalizeUpper: false));
        command.Parameters.AddWithValue("$maxResults", maxResults);
        command.Parameters.AddWithValue("$excludeInternal", excludeInternalChannels ? 1 : 0);
        command.Parameters.AddWithValue("$entityTypeFilter", string.IsNullOrWhiteSpace(entityTypeFilter)
            ? string.Empty
            : entityTypeFilter.Trim().ToUpperInvariant());

        var results = new List<EntitySearchResult>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new EntitySearchResult
            {
                EntityId = reader.GetInt64(0),
                DisplayName = reader.GetString(1),
                EntityType = reader.IsDBNull(2) ? null : reader.GetString(2),
                SourceFile = reader.GetString(3)
            });
        }

        return results;
    }

    public async Task<IReadOnlyList<string>> GetSearchableEntityTypesAsync(bool excludeInternalChannels = false)
    {
        await using var connection = await OpenConnectionAsync();

        const string sql = """
            SELECT DISTINCT UPPER(TRIM(COALESCE(e.EntityType, ''))) AS EntityType
            FROM Entities e
            WHERE TRIM(COALESCE(e.EntityType, '')) <> ''
              AND (
                    $excludeInternal = 0
                    OR (
                        UPPER(COALESCE(e.EntityType, '')) NOT IN ('BOX', 'PM_BOX', 'HM_HIST', '$GROUP')
                        AND UPPER(COALESCE(e.EntityName, '')) NOT LIKE '$CH%'
                        AND UPPER(COALESCE(e.EntityName, '')) NOT LIKE '$HY%'
                    )
                  )
            ORDER BY EntityType;
            """;

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("$excludeInternal", excludeInternalChannels ? 1 : 0);

        var results = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var value = reader.GetString(0).Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                results.Add(value);
            }
        }

        return results;
    }

    public async Task<int> ExportPointDetailsToExcelAsync(string outputPath, bool excludeInternalChannels = true)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path is required.", nameof(outputPath));
        }

        var exportRows = await GetPointDetailsForExportAsync(excludeInternalChannels);
        var exportedCount = exportRows
            .Select(row => row.EntityId)
            .Distinct()
            .Count();

        await Task.Run(() => WritePointDetailsWorkbook(outputPath, exportRows));
        return exportedCount;
    }

    private async Task<IReadOnlyList<PointDetailExportRow>> GetPointDetailsForExportAsync(bool excludeInternalChannels)
    {
        await using var connection = await OpenConnectionAsync();

        const string sql = """
            SELECT
                e.Id,
                COALESCE(NULLIF(TRIM(e.EntityName), ''), e.SystemEntityHeader) AS DisplayName,
                e.EntityType,
                f.FileName,
                COALESCE(ep.PropertyName, ''),
                COALESCE(ep.PropertyValue, ''),
                COALESCE(ep.SortOrder, 0)
            FROM Entities e
            INNER JOIN ImportedFiles f ON f.Id = e.FileId
            LEFT JOIN EntityProperties ep ON ep.EntityId = e.Id
            WHERE (
                    $excludeInternal = 0
                    OR (
                        UPPER(COALESCE(e.EntityType, '')) NOT IN ('BOX', 'PM_BOX', 'HM_HIST', '$GROUP')
                        AND UPPER(COALESCE(e.EntityName, '')) NOT LIKE '$CH%'
                        AND UPPER(COALESCE(e.EntityName, '')) NOT LIKE '$HY%'
                    )
                  )
            ORDER BY UPPER(COALESCE(e.EntityType, '')), DisplayName, e.Id, COALESCE(ep.SortOrder, 0), COALESCE(ep.PropertyName, '');
            """;

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("$excludeInternal", excludeInternalChannels ? 1 : 0);

        var rows = new List<PointDetailExportRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new PointDetailExportRow
            {
                EntityId = reader.GetInt64(0),
                DisplayName = reader.GetString(1),
                EntityType = reader.IsDBNull(2) ? null : reader.GetString(2),
                SourceFile = reader.GetString(3),
                PropertyName = reader.GetString(4),
                PropertyValue = reader.GetString(5),
                SortOrder = reader.GetInt32(6)
            });
        }

        return rows;
    }

    private static void WritePointDetailsWorkbook(string outputPath, IReadOnlyList<PointDetailExportRow> exportRows)
    {
        using var workbook = new XLWorkbook();
        var worksheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var typeGroup in exportRows
                     .GroupBy(row => string.IsNullOrWhiteSpace(row.EntityType) ? "UNKNOWN" : row.EntityType!.Trim().ToUpperInvariant())
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var worksheetName = BuildUniqueWorksheetName(typeGroup.Key, worksheetNames);
            worksheetNames.Add(worksheetName);
            var worksheet = workbook.Worksheets.Add(worksheetName);

            var rowsByEntityId = new Dictionary<long, Dictionary<string, string>>();
            var orderedEntities = new List<(long EntityId, string DisplayName)>();
            var propertyColumns = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var record in typeGroup)
            {
                if (!rowsByEntityId.TryGetValue(record.EntityId, out var row))
                {
                    row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Entity Id"] = record.EntityId.ToString(CultureInfo.InvariantCulture),
                        ["Point Name"] = record.DisplayName,
                        ["Point Type"] = string.IsNullOrWhiteSpace(record.EntityType) ? string.Empty : record.EntityType.Trim(),
                        ["Source File"] = record.SourceFile
                    };

                    rowsByEntityId[record.EntityId] = row;
                    orderedEntities.Add((record.EntityId, record.DisplayName));
                }

                var propertyName = (record.PropertyName ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(propertyName) || propertyName.StartsWith("_", StringComparison.Ordinal))
                {
                    continue;
                }

                propertyColumns.Add(propertyName);
                var propertyValue = record.PropertyValue ?? string.Empty;
                if (row.TryGetValue(propertyName, out var existing) && !string.Equals(existing, propertyValue, StringComparison.Ordinal))
                {
                    row[propertyName] = string.IsNullOrWhiteSpace(existing)
                        ? propertyValue
                        : $"{existing} | {propertyValue}";
                }
                else
                {
                    row[propertyName] = propertyValue;
                }
            }

            orderedEntities = orderedEntities
                .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.EntityId)
                .ToList();

            var headers = new List<string> { "Entity Id", "Point Name", "Point Type", "Source File" };
            headers.AddRange(propertyColumns);

            for (var col = 0; col < headers.Count; col++)
            {
                worksheet.Cell(1, col + 1).Value = headers[col];
                worksheet.Cell(1, col + 1).Style.Font.Bold = true;
            }

            var rowIndex = 2;
            foreach (var ordered in orderedEntities)
            {
                var row = rowsByEntityId[ordered.EntityId];
                for (var col = 0; col < headers.Count; col++)
                {
                    var header = headers[col];
                    worksheet.Cell(rowIndex, col + 1).Value = row.TryGetValue(header, out var value) ? value : string.Empty;
                }

                rowIndex++;
            }

            if (headers.Count > 0 && rowIndex > 1)
            {
                worksheet.Range(1, 1, rowIndex - 1, headers.Count).SetAutoFilter();
            }

            worksheet.Columns().AdjustToContents();
        }

        if (workbook.Worksheets.Count == 0)
        {
            workbook.Worksheets.Add("No Points").Cell("A1").Value = "No point data found to export.";
        }

        workbook.SaveAs(outputPath);
    }

    private sealed class PointDetailExportRow
    {
        public long EntityId { get; init; }
        public string DisplayName { get; init; } = string.Empty;
        public string? EntityType { get; init; }
        public string SourceFile { get; init; } = string.Empty;
        public string PropertyName { get; init; } = string.Empty;
        public string PropertyValue { get; init; } = string.Empty;
        public int SortOrder { get; init; }
    }

    private static string BuildUniqueWorksheetName(string baseName, ISet<string> existingNames)
    {
        var sanitized = SanitizeWorksheetName(baseName);
        if (!existingNames.Contains(sanitized))
        {
            return sanitized;
        }

        for (var index = 2; index <= 999; index++)
        {
            var suffix = $"_{index}";
            var maxBaseLength = Math.Max(1, 31 - suffix.Length);
            var candidate = sanitized.Length > maxBaseLength
                ? sanitized[..maxBaseLength]
                : sanitized;
            candidate += suffix;

            if (!existingNames.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{Guid.NewGuid():N}"[..31];
    }

    private static string SanitizeWorksheetName(string value)
    {
        var name = string.IsNullOrWhiteSpace(value) ? "UNKNOWN" : value.Trim();
        var invalidChars = new[] { '\\', '/', '?', '*', '[', ']', ':' };
        foreach (var invalidChar in invalidChars)
        {
            name = name.Replace(invalidChar, '_');
        }

        name = name.Trim('\'', ' ');
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "UNKNOWN";
        }

        return name.Length > 31 ? name[..31] : name;
    }

    public async Task<int> ImportAbPlcAsync(
        string plcName,
        string connectionType,
        int hwyNum,
        IReadOnlyList<int> hwyBoxNumbers,
        int nodeAddress,
        int? serialHpmNumber,
        int? serialModuleNumber,
        int? serialDevAddr,
        string plc5FileAddress,
        string systemFilePath,
        IReadOnlyList<string> ladderXpsFiles)
    {
        var normalizedConnectionType = string.IsNullOrWhiteSpace(connectionType) ? "EPLCG" : connectionType.Trim();
        var isSerialOnlyConnection = normalizedConnectionType.Equals("Serial Interface", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(plcName))
        {
            throw new ArgumentException("PLC name is required.", nameof(plcName));
        }

        if (string.IsNullOrWhiteSpace(systemFilePath))
        {
            throw new ArgumentException("System file path is required.", nameof(systemFilePath));
        }
        if (!isSerialOnlyConnection && string.IsNullOrWhiteSpace(plc5FileAddress))
        {
            throw new ArgumentException("PLC5 file address is required.", nameof(plc5FileAddress));
        }

        var normalizedPlcName = plcName.Trim();
        var normalizedConnectionDetails = string.Empty;
        var normalizedPlc5FileAddress = string.IsNullOrWhiteSpace(plc5FileAddress)
            ? string.Empty
            : NormalizePlcFileAddress(plc5FileAddress);
        var normalizedSerialPort = string.Empty;
        var normalizedSerialSettings = string.Empty;
        var normalizedHwyBoxNumbers = (hwyBoxNumbers ?? [])
            .Where(value => value >= 0)
            .Distinct()
            .ToList();
        if (normalizedHwyBoxNumbers.Count == 0)
        {
            throw new ArgumentException("At least one HWY box number is required.", nameof(hwyBoxNumbers));
        }

        var primaryHwyBoxNumber = normalizedHwyBoxNumbers[0];
        var normalizedHwyBoxNumbersCsv = string.Join(", ", normalizedHwyBoxNumbers);
        var normalizedSystemFilePath = systemFilePath.Trim();
        var normalizedLadderFiles = ladderXpsFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        await using var connection = await OpenConnectionAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        const string upsertPlcSql = """
            INSERT INTO AbPlcSystems (
                PlcName, PlcNameKey, ConnectionType, ConnectionDetails,
                HwyNum, HwyBoxNumber, HwyBoxNumbersCsv, NodeAddress, Plc5FileAddress, SerialPort, SerialSettings,
                SystemFilePath, ImportedAtUtc)
            VALUES (
                $plcName, $plcNameKey, $connectionType, $connectionDetails,
                $hwyNum, $hwyBoxNumber, $hwyBoxNumbersCsv, $nodeAddress, $plc5FileAddress, $serialPort, $serialSettings,
                $systemFilePath, $importedAtUtc)
            ON CONFLICT(PlcNameKey) DO UPDATE SET
                PlcName = excluded.PlcName,
                ConnectionType = excluded.ConnectionType,
                ConnectionDetails = excluded.ConnectionDetails,
                HwyNum = excluded.HwyNum,
                HwyBoxNumber = excluded.HwyBoxNumber,
                HwyBoxNumbersCsv = excluded.HwyBoxNumbersCsv,
                NodeAddress = excluded.NodeAddress,
                Plc5FileAddress = excluded.Plc5FileAddress,
                SerialPort = excluded.SerialPort,
                SerialSettings = excluded.SerialSettings,
                SystemFilePath = excluded.SystemFilePath,
                ImportedAtUtc = excluded.ImportedAtUtc;
            """;

        var importedAtUtc = DateTime.UtcNow.ToString("O");
        await using (var upsertPlcCommand = new SqliteCommand(upsertPlcSql, connection, transaction))
        {
            upsertPlcCommand.Parameters.AddWithValue("$plcName", normalizedPlcName);
            upsertPlcCommand.Parameters.AddWithValue("$plcNameKey", NormalizePlcKey(normalizedPlcName));
            upsertPlcCommand.Parameters.AddWithValue("$connectionType", normalizedConnectionType);
            upsertPlcCommand.Parameters.AddWithValue("$connectionDetails", normalizedConnectionDetails);
            upsertPlcCommand.Parameters.AddWithValue("$hwyNum", hwyNum);
            upsertPlcCommand.Parameters.AddWithValue("$hwyBoxNumber", primaryHwyBoxNumber);
            upsertPlcCommand.Parameters.AddWithValue("$hwyBoxNumbersCsv", normalizedHwyBoxNumbersCsv);
            upsertPlcCommand.Parameters.AddWithValue("$nodeAddress", nodeAddress);
            upsertPlcCommand.Parameters.AddWithValue("$plc5FileAddress", normalizedPlc5FileAddress);
            upsertPlcCommand.Parameters.AddWithValue("$serialPort", normalizedSerialPort);
            upsertPlcCommand.Parameters.AddWithValue("$serialSettings", normalizedSerialSettings);
            upsertPlcCommand.Parameters.AddWithValue("$systemFilePath", normalizedSystemFilePath);
            upsertPlcCommand.Parameters.AddWithValue("$importedAtUtc", importedAtUtc);
            await upsertPlcCommand.ExecuteNonQueryAsync();
        }

        const string getPlcIdSql = "SELECT Id FROM AbPlcSystems WHERE PlcNameKey = $plcNameKey LIMIT 1;";
        long plcSystemId;
        await using (var getIdCommand = new SqliteCommand(getPlcIdSql, connection, transaction))
        {
            getIdCommand.Parameters.AddWithValue("$plcNameKey", NormalizePlcKey(normalizedPlcName));
            plcSystemId = (long)(await getIdCommand.ExecuteScalarAsync() ?? 0L);
        }

        const string deleteMappingsSql = "DELETE FROM AbPlcRegisterMappings WHERE PlcSystemId = $plcSystemId;";
        await using (var deleteMappingsCommand = new SqliteCommand(deleteMappingsSql, connection, transaction))
        {
            deleteMappingsCommand.Parameters.AddWithValue("$plcSystemId", plcSystemId);
            await deleteMappingsCommand.ExecuteNonQueryAsync();
        }

        const string deleteLadderSql = "DELETE FROM AbPlcLadderFiles WHERE PlcSystemId = $plcSystemId;";
        await using (var deleteLadderCommand = new SqliteCommand(deleteLadderSql, connection, transaction))
        {
            deleteLadderCommand.Parameters.AddWithValue("$plcSystemId", plcSystemId);
            await deleteLadderCommand.ExecuteNonQueryAsync();
        }

        const string insertLadderSql = """
            INSERT INTO AbPlcLadderFiles (PlcSystemId, FilePath, FileName, ImportedAtUtc)
            VALUES ($plcSystemId, $filePath, $fileName, $importedAtUtc);
            """;

        foreach (var ladderFile in normalizedLadderFiles)
        {
            await using var insertLadderCommand = new SqliteCommand(insertLadderSql, connection, transaction);
            insertLadderCommand.Parameters.AddWithValue("$plcSystemId", plcSystemId);
            insertLadderCommand.Parameters.AddWithValue("$filePath", ladderFile);
            insertLadderCommand.Parameters.AddWithValue("$fileName", Path.GetFileName(ladderFile));
            insertLadderCommand.Parameters.AddWithValue("$importedAtUtc", importedAtUtc);
            await insertLadderCommand.ExecuteNonQueryAsync();
        }

        var mappings = ParseAbPlcMappings(normalizedSystemFilePath);
        var eplcgMappings = await ParseEplcgMappingsFromEntitiesAsync(
            connection,
            transaction,
            normalizedPlc5FileAddress,
            hwyNum,
            normalizedHwyBoxNumbers);
        AppendMappings(mappings, eplcgMappings);

        var isSerialConnectionType = normalizedConnectionType.Equals("Serial Interface", StringComparison.OrdinalIgnoreCase);
        var isMixedConnectionType = normalizedConnectionType.Equals("EPLCG + HPM", StringComparison.OrdinalIgnoreCase)
            || normalizedConnectionType.Equals("EPLCG+HPM", StringComparison.OrdinalIgnoreCase);

        var hasExplicitSerialCriteria = serialHpmNumber.HasValue && (serialDevAddr.HasValue || serialModuleNumber.HasValue);
        var serialNodeNumber = serialHpmNumber ?? -1;
        var serialIoModuleNumber = serialModuleNumber ?? -1;
        if (!hasExplicitSerialCriteria && (isSerialConnectionType || isMixedConnectionType))
        {
            // Backward compatibility for existing imports where serial criteria were stored in these fields.
            serialNodeNumber = nodeAddress;
            serialIoModuleNumber = normalizedHwyBoxNumbers.Count > 0 ? normalizedHwyBoxNumbers[0] : -1;
        }

        if ((hasExplicitSerialCriteria || isSerialConnectionType || isMixedConnectionType)
            && serialNodeNumber >= 0
            && (serialDevAddr.HasValue || serialIoModuleNumber >= 0))
        {
            var allowAnySerialModule = serialDevAddr.HasValue && serialDevAddr.Value >= 0;
            var serialMappings = await ParseSerialInterfaceMappingsFromEntitiesAsync(
                connection,
                transaction,
                normalizedPlc5FileAddress,
                serialNodeNumber,
                serialIoModuleNumber,
                serialDevAddr,
                allowAnySerialModule);
            AppendMappings(mappings, serialMappings);
        }

        const string insertMappingSql = """
            INSERT INTO AbPlcRegisterMappings (PlcSystemId, RegisterAddress, RegisterKey, TagName, TagKey, SourceLine)
            VALUES ($plcSystemId, $registerAddress, $registerKey, $tagName, $tagKey, $sourceLine);
            """;

        foreach (var mapping in mappings)
        {
            await using var insertMappingCommand = new SqliteCommand(insertMappingSql, connection, transaction);
            insertMappingCommand.Parameters.AddWithValue("$plcSystemId", plcSystemId);
            insertMappingCommand.Parameters.AddWithValue("$registerAddress", mapping.RegisterAddress);
            insertMappingCommand.Parameters.AddWithValue("$registerKey", mapping.RegisterKey);
            insertMappingCommand.Parameters.AddWithValue("$tagName", mapping.TagName);
            insertMappingCommand.Parameters.AddWithValue("$tagKey", mapping.TagKey);
            insertMappingCommand.Parameters.AddWithValue("$sourceLine", mapping.SourceLine);
            await insertMappingCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        return mappings.Count;
    }

    public async Task<int> ImportModiconAsync(
        string plcName,
        string connectionType,
        int hwyNum,
        IReadOnlyList<int> hwyBoxNumbers,
        int? nodeAddress,
        int? serialHpmNumber,
        int? serialModuleNumber,
        string registersFilePath,
        string? trafficCopFilePath,
        IReadOnlyList<string> programXpsFiles)
    {
        var normalizedConnectionType = string.IsNullOrWhiteSpace(connectionType) ? "EPLCG" : connectionType.Trim();
        var isSerialOnlyConnection = normalizedConnectionType.Equals("Serial Interface", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(plcName))
        {
            throw new ArgumentException("PLC name is required.", nameof(plcName));
        }

        if (string.IsNullOrWhiteSpace(registersFilePath))
        {
            throw new ArgumentException("Registers file path is required.", nameof(registersFilePath));
        }

        var normalizedPlcName = plcName.Trim();
        var normalizedConnectionDetails = string.Empty;
        var normalizedSerialPort = string.Empty;
        var normalizedSerialSettings = string.Empty;
        var normalizedHwyBoxNumbers = (hwyBoxNumbers ?? [])
            .Where(value => value >= 0)
            .Distinct()
            .ToList();
        if (normalizedHwyBoxNumbers.Count == 0)
        {
            throw new ArgumentException("At least one HWY box number is required.", nameof(hwyBoxNumbers));
        }

        var primaryHwyBoxNumber = normalizedHwyBoxNumbers[0];
        var normalizedHwyBoxNumbersCsv = string.Join(", ", normalizedHwyBoxNumbers);
        var normalizedRegistersFilePath = registersFilePath.Trim();
        var normalizedTrafficCopFilePath = string.IsNullOrWhiteSpace(trafficCopFilePath) ? string.Empty : trafficCopFilePath.Trim();
        var normalizedProgramFiles = programXpsFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        await using var connection = await OpenConnectionAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        const string upsertSql = """
            INSERT INTO ModiconSystems (
                PlcName, PlcNameKey, ConnectionType, ConnectionDetails,
                HwyNum, HwyBoxNumber, HwyBoxNumbersCsv, NodeAddress, RegistersFilePath, TrafficCopFilePath, SerialPort, SerialSettings,
                SystemFilePath, ImportedAtUtc)
            VALUES (
                $plcName, $plcNameKey, $connectionType, $connectionDetails,
                $hwyNum, $hwyBoxNumber, $hwyBoxNumbersCsv, $nodeAddress, $registersFilePath, $trafficCopFilePath, $serialPort, $serialSettings,
                $systemFilePath, $importedAtUtc)
            ON CONFLICT(PlcNameKey) DO UPDATE SET
                PlcName = excluded.PlcName,
                ConnectionType = excluded.ConnectionType,
                ConnectionDetails = excluded.ConnectionDetails,
                HwyNum = excluded.HwyNum,
                HwyBoxNumber = excluded.HwyBoxNumber,
                HwyBoxNumbersCsv = excluded.HwyBoxNumbersCsv,
                NodeAddress = excluded.NodeAddress,
                RegistersFilePath = excluded.RegistersFilePath,
                TrafficCopFilePath = excluded.TrafficCopFilePath,
                SerialPort = excluded.SerialPort,
                SerialSettings = excluded.SerialSettings,
                SystemFilePath = excluded.SystemFilePath,
                ImportedAtUtc = excluded.ImportedAtUtc;
            """;

        var importedAtUtc = DateTime.UtcNow.ToString("O");
        await using (var upsertCommand = new SqliteCommand(upsertSql, connection, transaction))
        {
            upsertCommand.Parameters.AddWithValue("$plcName", normalizedPlcName);
            upsertCommand.Parameters.AddWithValue("$plcNameKey", NormalizePlcKey(normalizedPlcName));
            upsertCommand.Parameters.AddWithValue("$connectionType", normalizedConnectionType);
            upsertCommand.Parameters.AddWithValue("$connectionDetails", normalizedConnectionDetails);
            upsertCommand.Parameters.AddWithValue("$hwyNum", hwyNum);
            upsertCommand.Parameters.AddWithValue("$hwyBoxNumber", primaryHwyBoxNumber);
            upsertCommand.Parameters.AddWithValue("$hwyBoxNumbersCsv", normalizedHwyBoxNumbersCsv);
            upsertCommand.Parameters.AddWithValue("$nodeAddress", nodeAddress.HasValue ? nodeAddress.Value : DBNull.Value);
            upsertCommand.Parameters.AddWithValue("$registersFilePath", normalizedRegistersFilePath);
            upsertCommand.Parameters.AddWithValue("$trafficCopFilePath", normalizedTrafficCopFilePath);
            upsertCommand.Parameters.AddWithValue("$serialPort", normalizedSerialPort);
            upsertCommand.Parameters.AddWithValue("$serialSettings", normalizedSerialSettings);
            upsertCommand.Parameters.AddWithValue("$systemFilePath", string.Empty);
            upsertCommand.Parameters.AddWithValue("$importedAtUtc", importedAtUtc);
            await upsertCommand.ExecuteNonQueryAsync();
        }

        const string getIdSql = "SELECT Id FROM ModiconSystems WHERE PlcNameKey = $plcNameKey LIMIT 1;";
        long modiconSystemId;
        await using (var getIdCommand = new SqliteCommand(getIdSql, connection, transaction))
        {
            getIdCommand.Parameters.AddWithValue("$plcNameKey", NormalizePlcKey(normalizedPlcName));
            modiconSystemId = (long)(await getIdCommand.ExecuteScalarAsync() ?? 0L);
        }

        const string deleteMappingsSql = "DELETE FROM ModiconRegisterMappings WHERE ModiconSystemId = $modiconSystemId;";
        await using (var deleteMappingsCommand = new SqliteCommand(deleteMappingsSql, connection, transaction))
        {
            deleteMappingsCommand.Parameters.AddWithValue("$modiconSystemId", modiconSystemId);
            await deleteMappingsCommand.ExecuteNonQueryAsync();
        }

        const string deleteFilesSql = "DELETE FROM ModiconProgramFiles WHERE ModiconSystemId = $modiconSystemId;";
        await using (var deleteFilesCommand = new SqliteCommand(deleteFilesSql, connection, transaction))
        {
            deleteFilesCommand.Parameters.AddWithValue("$modiconSystemId", modiconSystemId);
            await deleteFilesCommand.ExecuteNonQueryAsync();
        }

        const string deleteTrafficCopSql = "DELETE FROM ModiconTrafficCopEntries WHERE ModiconSystemId = $modiconSystemId;";
        await using (var deleteTrafficCopCommand = new SqliteCommand(deleteTrafficCopSql, connection, transaction))
        {
            deleteTrafficCopCommand.Parameters.AddWithValue("$modiconSystemId", modiconSystemId);
            await deleteTrafficCopCommand.ExecuteNonQueryAsync();
        }

        const string insertFileSql = """
            INSERT INTO ModiconProgramFiles (ModiconSystemId, FilePath, FileName, ImportedAtUtc)
            VALUES ($modiconSystemId, $filePath, $fileName, $importedAtUtc);
            """;

        foreach (var programFile in normalizedProgramFiles)
        {
            await using var insertFileCommand = new SqliteCommand(insertFileSql, connection, transaction);
            insertFileCommand.Parameters.AddWithValue("$modiconSystemId", modiconSystemId);
            insertFileCommand.Parameters.AddWithValue("$filePath", programFile);
            insertFileCommand.Parameters.AddWithValue("$fileName", Path.GetFileName(programFile));
            insertFileCommand.Parameters.AddWithValue("$importedAtUtc", importedAtUtc);
            await insertFileCommand.ExecuteNonQueryAsync();
        }

        var mappings = await ParseModiconMappingsFromEntitiesAsync(
            connection,
            transaction,
            normalizedRegistersFilePath,
            hwyNum,
            normalizedHwyBoxNumbers,
            nodeAddress,
            normalizedConnectionType,
            serialHpmNumber,
            serialModuleNumber,
            isSerialOnlyConnection);

        const string insertMappingSql = """
            INSERT INTO ModiconRegisterMappings (ModiconSystemId, RegisterAddress, RegisterKey, TagName, TagKey, SourceLine)
            VALUES ($modiconSystemId, $registerAddress, $registerKey, $tagName, $tagKey, $sourceLine);
            """;

        foreach (var mapping in mappings)
        {
            await using var insertMappingCommand = new SqliteCommand(insertMappingSql, connection, transaction);
            insertMappingCommand.Parameters.AddWithValue("$modiconSystemId", modiconSystemId);
            insertMappingCommand.Parameters.AddWithValue("$registerAddress", mapping.RegisterAddress);
            insertMappingCommand.Parameters.AddWithValue("$registerKey", mapping.RegisterKey);
            insertMappingCommand.Parameters.AddWithValue("$tagName", mapping.TagName);
            insertMappingCommand.Parameters.AddWithValue("$tagKey", mapping.TagKey);
            insertMappingCommand.Parameters.AddWithValue("$sourceLine", mapping.SourceLine);
            await insertMappingCommand.ExecuteNonQueryAsync();
        }

        if (!string.IsNullOrWhiteSpace(normalizedTrafficCopFilePath))
        {
            var trafficCopEntries = ParseModiconTrafficCopEntries(normalizedTrafficCopFilePath);
            if (trafficCopEntries.Count > 0)
            {
                const string insertTrafficCopSql = """
                    INSERT INTO ModiconTrafficCopEntries (
                        ModiconSystemId, DropNumber, SlotNumber, ModuleText,
                        ReferenceStart, ReferenceEnd, ReferenceRange, DataType, Description, SourceLine)
                    VALUES (
                        $modiconSystemId, $dropNumber, $slotNumber, $moduleText,
                        $referenceStart, $referenceEnd, $referenceRange, $dataType, $description, $sourceLine);
                    """;

                foreach (var entry in trafficCopEntries)
                {
                    await using var insertTrafficCopCommand = new SqliteCommand(insertTrafficCopSql, connection, transaction);
                    insertTrafficCopCommand.Parameters.AddWithValue("$modiconSystemId", modiconSystemId);
                    insertTrafficCopCommand.Parameters.AddWithValue("$dropNumber", entry.DropNumber.HasValue ? entry.DropNumber.Value : DBNull.Value);
                    insertTrafficCopCommand.Parameters.AddWithValue("$slotNumber", entry.SlotNumber);
                    insertTrafficCopCommand.Parameters.AddWithValue("$moduleText", entry.ModuleText);
                    insertTrafficCopCommand.Parameters.AddWithValue("$referenceStart", entry.ReferenceStart);
                    insertTrafficCopCommand.Parameters.AddWithValue("$referenceEnd", entry.ReferenceEnd);
                    insertTrafficCopCommand.Parameters.AddWithValue("$referenceRange", entry.ReferenceRange);
                    insertTrafficCopCommand.Parameters.AddWithValue("$dataType", entry.DataType);
                    insertTrafficCopCommand.Parameters.AddWithValue("$description", entry.Description);
                    insertTrafficCopCommand.Parameters.AddWithValue("$sourceLine", entry.SourceLine);
                    await insertTrafficCopCommand.ExecuteNonQueryAsync();
                }
            }
        }

        await transaction.CommitAsync();
        return mappings.Count;
    }

    public async Task<IReadOnlyList<ModiconSummary>> SearchModiconSystemsAsync(string query, int maxResults = 300)
    {
        await using var connection = await OpenConnectionAsync();

        const string sql = """
            SELECT
                s.Id,
                s.PlcName,
                s.ConnectionType,
                COALESCE(s.ConnectionDetails, ''),
                s.HwyNum,
                s.HwyBoxNumber,
                COALESCE(s.HwyBoxNumbersCsv, ''),
                s.NodeAddress,
                COALESCE(s.RegistersFilePath, ''),
                COALESCE(s.TrafficCopFilePath, ''),
                COALESCE(s.SerialPort, ''),
                COALESCE(s.SerialSettings, ''),
                COALESCE(COUNT(DISTINCT f.Id), 0) AS ProgramFileCount,
                COALESCE(COUNT(DISTINCT m.Id), 0) AS MappingCount,
                COALESCE(COUNT(DISTINCT t.Id), 0) AS TrafficCopEntryCount
            FROM ModiconSystems s
            LEFT JOIN ModiconProgramFiles f ON f.ModiconSystemId = s.Id
            LEFT JOIN ModiconRegisterMappings m ON m.ModiconSystemId = s.Id
            LEFT JOIN ModiconTrafficCopEntries t ON t.ModiconSystemId = s.Id
            WHERE $query = ''
               OR UPPER(s.PlcName) LIKE '%' || $query || '%'
               OR UPPER(COALESCE(s.ConnectionType, '')) LIKE '%' || $query || '%'
               OR UPPER(COALESCE(s.HwyBoxNumbersCsv, '')) LIKE '%' || $query || '%'
               OR UPPER(COALESCE(s.RegistersFilePath, '')) LIKE '%' || $query || '%'
               OR UPPER(COALESCE(s.TrafficCopFilePath, '')) LIKE '%' || $query || '%'
            GROUP BY
                s.Id, s.PlcName, s.ConnectionType, s.ConnectionDetails,
                s.HwyNum, s.HwyBoxNumber, s.HwyBoxNumbersCsv, s.NodeAddress,
                s.RegistersFilePath, s.TrafficCopFilePath, s.SerialPort, s.SerialSettings
            ORDER BY s.PlcName
            LIMIT $maxResults;
            """;

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("$query", query.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("$maxResults", maxResults);

        var results = new List<ModiconSummary>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new ModiconSummary
            {
                ModiconSystemId = reader.GetInt64(0),
                PlcName = reader.GetString(1).Trim(),
                ConnectionType = reader.GetString(2).Trim(),
                ConnectionDetails = reader.GetString(3).Trim(),
                HwyNum = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                HwyBoxNumber = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                HwyBoxNumbersCsv = reader.GetString(6).Trim(),
                NodeAddress = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                RegistersFilePath = reader.GetString(8).Trim(),
                TrafficCopFilePath = reader.GetString(9).Trim(),
                SerialPort = reader.GetString(10).Trim(),
                SerialSettings = reader.GetString(11).Trim(),
                ProgramFileCount = reader.GetInt32(12),
                MappingCount = reader.GetInt32(13),
                TrafficCopEntryCount = reader.GetInt32(14)
            });
        }

        return results;
    }

    public async Task<IReadOnlyList<ModiconProgramFile>> GetModiconProgramFilesAsync(long modiconSystemId)
    {
        await using var connection = await OpenConnectionAsync();

        const string sql = """
            SELECT Id, ModiconSystemId, FilePath, FileName
            FROM ModiconProgramFiles
            WHERE ModiconSystemId = $modiconSystemId
            ORDER BY FileName;
            """;

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("$modiconSystemId", modiconSystemId);

        var results = new List<ModiconProgramFile>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new ModiconProgramFile
            {
                ProgramFileId = reader.GetInt64(0),
                ModiconSystemId = reader.GetInt64(1),
                FilePath = reader.GetString(2).Trim(),
                FileName = reader.GetString(3).Trim()
            });
        }

        return results;
    }

    public async Task<IReadOnlyList<ModiconTrafficCopEntry>> GetModiconTrafficCopEntriesForHardwareAsync(
        int? dropNumber,
        int? moduleIndex,
        long? modiconSystemId,
        int maxResults = 2000)
    {
        await using var connection = await OpenConnectionAsync();

        const string sql = """
            SELECT
                t.Id,
                t.ModiconSystemId,
                t.DropNumber,
                t.SlotNumber,
                COALESCE(t.ModuleText, ''),
                COALESCE(t.ReferenceStart, ''),
                COALESCE(t.ReferenceEnd, ''),
                COALESCE(t.ReferenceRange, ''),
                COALESCE(t.DataType, ''),
                COALESCE(t.Description, ''),
                COALESCE(t.SourceLine, '')
            FROM ModiconTrafficCopEntries t
            WHERE ($modiconSystemId <= 0 OR t.ModiconSystemId = $modiconSystemId)
              AND ($dropNumber < 0 OR COALESCE(t.DropNumber, -1) = $dropNumber)
              AND (
                    $moduleIndex < 0
                    OR t.SlotNumber = $moduleIndex
                    OR (t.SlotNumber % 100) = $moduleIndex
                  )
            ORDER BY
                COALESCE(t.DropNumber, 0),
                t.SlotNumber,
                COALESCE(t.ReferenceStart, ''),
                t.Id
            LIMIT $maxResults;
            """;

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("$modiconSystemId", modiconSystemId.GetValueOrDefault());
        command.Parameters.AddWithValue("$dropNumber", dropNumber.GetValueOrDefault(-1));
        command.Parameters.AddWithValue("$moduleIndex", moduleIndex.GetValueOrDefault(-1));
        command.Parameters.AddWithValue("$maxResults", maxResults);

        var results = new List<ModiconTrafficCopEntry>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new ModiconTrafficCopEntry
            {
                EntryId = reader.GetInt64(0),
                ModiconSystemId = reader.GetInt64(1),
                DropNumber = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                SlotNumber = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                ModuleText = reader.GetString(4).Trim(),
                ReferenceStart = reader.GetString(5).Trim(),
                ReferenceEnd = reader.GetString(6).Trim(),
                ReferenceRange = reader.GetString(7).Trim(),
                DataType = reader.GetString(8).Trim(),
                Description = reader.GetString(9).Trim(),
                SourceLine = reader.GetString(10).Trim()
            });
        }

        return results;
    }

    public async Task<IReadOnlyList<AbPlcSummary>> SearchAbPlcSystemsAsync(string query, int maxResults = 300)
    {
        await using var connection = await OpenConnectionAsync();

        const string sql = """
            SELECT
                s.Id,
                s.PlcName,
                s.ConnectionType,
                COALESCE(s.ConnectionDetails, ''),
                s.HwyNum,
                s.HwyBoxNumber,
                COALESCE(s.HwyBoxNumbersCsv, ''),
                s.NodeAddress,
                COALESCE(s.Plc5FileAddress, ''),
                COALESCE(s.SerialPort, ''),
                COALESCE(s.SerialSettings, ''),
                s.SystemFilePath,
                COALESCE(COUNT(DISTINCT f.Id), 0) AS LadderFileCount,
                COALESCE(COUNT(DISTINCT m.Id), 0) AS MappingCount
            FROM AbPlcSystems s
            LEFT JOIN AbPlcLadderFiles f ON f.PlcSystemId = s.Id
            LEFT JOIN AbPlcRegisterMappings m ON m.PlcSystemId = s.Id
            WHERE $query = ''
               OR UPPER(s.PlcName) LIKE '%' || $query || '%'
               OR UPPER(COALESCE(s.ConnectionType, '')) LIKE '%' || $query || '%'
               OR UPPER(COALESCE(s.HwyBoxNumbersCsv, '')) LIKE '%' || $query || '%'
               OR UPPER(COALESCE(s.Plc5FileAddress, '')) LIKE '%' || $query || '%'
            GROUP BY
                s.Id, s.PlcName, s.ConnectionType, s.ConnectionDetails,
                s.HwyNum, s.HwyBoxNumber, s.HwyBoxNumbersCsv, s.NodeAddress, s.Plc5FileAddress, s.SerialPort, s.SerialSettings,
                s.SystemFilePath
            ORDER BY s.PlcName
            LIMIT $maxResults;
            """;

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("$query", query.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("$maxResults", maxResults);

        var results = new List<AbPlcSummary>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new AbPlcSummary
            {
                PlcSystemId = reader.GetInt64(0),
                PlcName = reader.GetString(1).Trim(),
                ConnectionType = reader.GetString(2).Trim(),
                ConnectionDetails = reader.GetString(3).Trim(),
                HwyNum = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                HwyBoxNumber = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                HwyBoxNumbersCsv = reader.GetString(6).Trim(),
                NodeAddress = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                Plc5FileAddress = reader.GetString(8).Trim(),
                SerialPort = reader.GetString(9).Trim(),
                SerialSettings = reader.GetString(10).Trim(),
                SystemFilePath = reader.GetString(11).Trim(),
                LadderFileCount = reader.GetInt32(12),
                MappingCount = reader.GetInt32(13)
            });
        }

        return results;
    }

    public async Task<IReadOnlyList<AbPlcLadderFile>> GetAbPlcLadderFilesAsync(long plcSystemId)
    {
        await using var connection = await OpenConnectionAsync();

        const string sql = """
            SELECT Id, PlcSystemId, FilePath, FileName
            FROM AbPlcLadderFiles
            WHERE PlcSystemId = $plcSystemId
            ORDER BY FileName;
            """;

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("$plcSystemId", plcSystemId);

        var results = new List<AbPlcLadderFile>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new AbPlcLadderFile
            {
                LadderFileId = reader.GetInt64(0),
                PlcSystemId = reader.GetInt64(1),
                FilePath = reader.GetString(2).Trim(),
                FileName = reader.GetString(3).Trim()
            });
        }

        return results;
    }

    public async Task<(int MatchCount, List<int> FlIndices)> GetFlIndicesForXpsSearchAsync(long systemId, bool isAbPlc, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return (0, []);
        }

        await using var connection = await OpenConnectionAsync();

        var tableName = isAbPlc ? "AbPlcRegisterMappings" : "ModiconRegisterMappings";
        var systemColumn = isAbPlc ? "PlcSystemId" : "ModiconSystemId";
        var normalizedQuery = query.Trim();
        var isRegisterLikeQuery = Regex.IsMatch(
            normalizedQuery,
            @"^[A-Za-z]\d+\s*:\s*\d+(?:\s*/\s*\d+)?$",
            RegexOptions.IgnoreCase);

        string sql;
        await using var command = new SqliteCommand();
        command.Connection = connection;
        command.Parameters.AddWithValue("$systemId", systemId);

        if (isRegisterLikeQuery)
        {
            var normalizedAddress = Regex.Replace(normalizedQuery, @"\s+", string.Empty);
            var hasBit = normalizedAddress.Contains('/');
            if (hasBit)
            {
                sql = $"""
                    SELECT COALESCE(SourceLine, '')
                    FROM {tableName}
                    WHERE {systemColumn} = $systemId
                      AND UPPER(TRIM(COALESCE(RegisterAddress, ''))) = UPPER($exactAddress);
                    """;
                command.Parameters.AddWithValue("$exactAddress", normalizedAddress);
            }
            else
            {
                sql = $"""
                    SELECT COALESCE(SourceLine, '')
                    FROM {tableName}
                    WHERE {systemColumn} = $systemId
                      AND (
                            UPPER(TRIM(COALESCE(RegisterAddress, ''))) = UPPER($baseAddress)
                         OR UPPER(TRIM(COALESCE(RegisterAddress, ''))) LIKE UPPER($basePrefix)
                      );
                    """;
                command.Parameters.AddWithValue("$baseAddress", normalizedAddress);
                command.Parameters.AddWithValue("$basePrefix", $"{normalizedAddress}/%");
            }
        }
        else
        {
            var likeParam = $"%{normalizedQuery}%";
            sql = $"""
                SELECT COALESCE(SourceLine, '')
                FROM {tableName}
                WHERE {systemColumn} = $systemId
                  AND (
                        UPPER(TRIM(COALESCE(RegisterAddress, ''))) LIKE UPPER($like)
                     OR UPPER(TRIM(COALESCE(TagName, ''))) LIKE UPPER($like)
                     OR UPPER(TRIM(COALESCE(TagKey, ''))) LIKE UPPER($like)
                  );
                """;
            command.Parameters.AddWithValue("$like", likeParam);
        }

        command.CommandText = sql;

        var flIndices = new SortedSet<int>();
        var matchCount = 0;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            matchCount++;
            var sourceLine = reader.GetString(0);
            if (sourceLine.StartsWith("SERIAL ARRAY ", StringComparison.OrdinalIgnoreCase))
            {
                // In serial mappings, FL/NN/STR/TIME are array element labels, not XPS page numbers.
                continue;
            }
            var m = Regex.Match(sourceLine, @"(?<![A-Za-z0-9_])FL\s*\(\s*(\d+)\s*\)", RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var flIndex))
            {
                flIndices.Add(flIndex);
            }
        }

        return (matchCount, [.. flIndices]);
    }

    public async Task<IReadOnlyList<ChartPlcReference>> GetPlcReferencesByRegisterAddressAsync(string registerAddress, string? interfaceType = null)
    {
        var normalizedAddress = (registerAddress ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedAddress))
        {
            return [];
        }

        var isAb = interfaceType?.Trim().Equals("AB", StringComparison.OrdinalIgnoreCase) == true;
        var isModicon = interfaceType?.Trim().Equals("MODICON", StringComparison.OrdinalIgnoreCase) == true;

        await using var connection = await OpenConnectionAsync();

        var sql = """
            SELECT
                'AB' AS InterfaceType,
                m.PlcSystemId,
                s.PlcName,
                s.ConnectionType,
                COALESCE(s.ConnectionDetails, ''),
                s.HwyNum,
                s.HwyBoxNumber,
                COALESCE(s.HwyBoxNumbersCsv, ''),
                s.NodeAddress,
                COALESCE(s.Plc5FileAddress, ''),
                COALESCE(s.SerialPort, ''),
                COALESCE(s.SerialSettings, ''),
                TRIM(COALESCE(m.RegisterAddress, '')) AS RegisterAddress,
                COALESCE(m.TagName, '') AS TagName,
                COALESCE(m.SourceLine, '') AS SourceLine
            FROM AbPlcRegisterMappings m
            INNER JOIN AbPlcSystems s ON s.Id = m.PlcSystemId
            WHERE UPPER(TRIM(COALESCE(m.RegisterAddress, ''))) = UPPER($address)
               OR UPPER(TRIM(COALESCE(m.RegisterAddress, ''))) LIKE UPPER($addressPrefix)
            UNION ALL
            SELECT
                'MODICON' AS InterfaceType,
                m.ModiconSystemId,
                s.PlcName,
                s.ConnectionType,
                COALESCE(s.ConnectionDetails, ''),
                s.HwyNum,
                s.HwyBoxNumber,
                COALESCE(s.HwyBoxNumbersCsv, ''),
                s.NodeAddress,
                '',
                COALESCE(s.SerialPort, ''),
                COALESCE(s.SerialSettings, ''),
                TRIM(COALESCE(m.RegisterAddress, '')) AS RegisterAddress,
                COALESCE(m.TagName, '') AS TagName,
                COALESCE(m.SourceLine, '') AS SourceLine
            FROM ModiconRegisterMappings m
            INNER JOIN ModiconSystems s ON s.Id = m.ModiconSystemId
            WHERE UPPER(TRIM(COALESCE(m.RegisterAddress, ''))) = UPPER($address)
               OR UPPER(TRIM(COALESCE(m.RegisterAddress, ''))) LIKE UPPER($addressPrefix)
            ORDER BY PlcName, RegisterAddress, SourceLine;
            """;

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("$address", normalizedAddress);
        command.Parameters.AddWithValue("$addressPrefix", $"{normalizedAddress}/%");

        var results = new List<ChartPlcReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var currentInterface = reader.GetString(0).Trim();
            if (isAb && !currentInterface.Equals("AB", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (isModicon && !currentInterface.Equals("MODICON", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var plcSystemId = reader.GetInt64(1);
            var plcName = reader.GetString(2).Trim();
            var connectionType = reader.GetString(3).Trim();
            var connectionDetails = reader.GetString(4).Trim();
            var hwyNum = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5);
            var hwyBoxNumber = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6);
            var hwyBoxNumbersCsv = reader.GetString(7).Trim();
            var nodeAddress = reader.IsDBNull(8) ? (int?)null : reader.GetInt32(8);
            var plc5FileAddress = reader.GetString(9).Trim();
            var serialPort = reader.GetString(10).Trim();
            var serialSettings = reader.GetString(11).Trim();
            var mappedRegisterAddress = reader.GetString(12).Trim();
            var tagName = reader.GetString(13).Trim();
            var sourceLine = reader.GetString(14).Trim();

            var uniqueKey = $"{currentInterface}|{plcSystemId}|{mappedRegisterAddress}|{tagName}|{sourceLine}";
            if (!seen.Add(uniqueKey))
            {
                continue;
            }

            results.Add(new ChartPlcReference
            {
                InterfaceType = currentInterface,
                PlcSystemId = plcSystemId,
                PlcName = plcName,
                ConnectionType = connectionType,
                ConnectionDetails = connectionDetails,
                HwyNum = hwyNum,
                HwyBoxNumber = hwyBoxNumber,
                HwyBoxNumbersCsv = hwyBoxNumbersCsv,
                NodeAddress = nodeAddress,
                Plc5FileAddress = plc5FileAddress,
                SerialPort = serialPort,
                SerialSettings = serialSettings,
                RegisterAddress = mappedRegisterAddress,
                TagName = tagName,
                SourceLine = sourceLine
            });
        }

        return results;
    }

    public async Task<EntitySearchResult?> GetEntitySearchResultByIdAsync(long entityId)
    {
        await using var connection = await OpenConnectionAsync();

        const string sql = """
            SELECT
                e.Id,
                COALESCE(NULLIF(TRIM(e.EntityName), ''), '') AS EntityName,
                e.EntityType,
                COALESCE(NULLIF(TRIM(e.SystemEntityHeader), ''), '') AS SystemEntityHeader,
                f.FileName
            FROM Entities e
            INNER JOIN ImportedFiles f ON f.Id = e.FileId
            WHERE e.Id = $entityId
            LIMIT 1;
            """;

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("$entityId", entityId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var rawEntityName = reader.GetString(1).Trim();
        var entityType = reader.IsDBNull(2) ? null : reader.GetString(2);
        var systemEntityHeader = reader.GetString(3).Trim();
        var displayName = GetPreferredEntityDisplayName(rawEntityName, systemEntityHeader);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = rawEntityName;
        }

        return new EntitySearchResult
        {
            EntityId = reader.GetInt64(0),
            DisplayName = displayName,
            EntityType = entityType,
            SourceFile = reader.GetString(4)
        };
    }

    public async Task<IReadOnlyList<HardwareBoxSummary>> SearchHardwareBoxesAsync(string query, int maxResults = 300)
    {
        await using var connection = await OpenConnectionAsync();

        const string sql = """
            SELECT
                e.Id,
                COALESCE(NULLIF(TRIM(e.EntityName), ''), '') AS EntityName,
                COALESCE(NULLIF(TRIM(e.EntityType), ''), '') AS EntityType,
                COALESCE(NULLIF(TRIM(e.SystemEntityHeader), ''), '') AS SystemEntityHeader,
                COALESCE(NULLIF(TRIM(f.FileName), ''), '') AS SourceFile,
                COALESCE(MAX(CASE WHEN UPPER(ep.PropertyName) = 'HWYNUM' THEN TRIM(ep.PropertyValue) END), '') AS HwyNum,
                COALESCE(MAX(CASE WHEN UPPER(ep.PropertyName) = 'BOXNUM' THEN TRIM(ep.PropertyValue) END), '') AS BoxNum,
                COALESCE(MAX(CASE WHEN UPPER(ep.PropertyName) = 'NTWKNUM' THEN TRIM(ep.PropertyValue) END), '') AS NtwkNum,
                COALESCE(MAX(CASE WHEN UPPER(ep.PropertyName) = 'NODENUM' THEN TRIM(ep.PropertyValue) END), '') AS NodeNum,
                COALESCE(SUM(
                    CASE
                        WHEN UPPER(ep.PropertyName) LIKE 'IOMTYPE(%'
                         AND UPPER(TRIM(ep.PropertyValue)) <> 'NONE'
                         AND TRIM(ep.PropertyValue) <> ''
                        THEN 1
                        ELSE 0
                    END
                ), 0) AS ModuleCount
            FROM Entities e
            INNER JOIN ImportedFiles f ON f.Id = e.FileId
            LEFT JOIN EntityProperties ep ON ep.EntityId = e.Id
            WHERE UPPER(COALESCE(f.FileName, '')) LIKE '%BOX.EB'
              AND UPPER(COALESCE(e.EntityType, '')) IN ('BOX', 'PM_BOX')
              AND (
                   $query = ''
                   OR UPPER(COALESCE(e.EntityName, '')) LIKE '%' || $query || '%'
                   OR UPPER(COALESCE(e.SystemEntityHeader, '')) LIKE '%' || $query || '%'
                   OR UPPER(COALESCE(ep.PropertyValue, '')) LIKE '%' || $query || '%'
              )
            GROUP BY e.Id, EntityName, EntityType, SystemEntityHeader, SourceFile
            ORDER BY
                CAST(NULLIF(NtwkNum, '') AS INTEGER),
                CAST(NULLIF(NodeNum, '') AS INTEGER),
                EntityName,
                e.Id
            LIMIT $maxResults;
            """;

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("$query", query.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("$maxResults", maxResults);

        var results = new List<HardwareBoxSummary>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var entityName = reader.GetString(1).Trim();
            var entityType = reader.GetString(2).Trim();
            var systemEntityHeader = reader.GetString(3).Trim();
            var sourceFile = reader.GetString(4).Trim();
            var hwyNum = ParseNullableInt(reader.GetString(5));
            var boxNum = ParseNullableHwyBoxNumber(reader.GetString(6));
            var ntwkNum = ParseNullableInt(reader.GetString(7));
            var nodeNum = ParseNullableInt(reader.GetString(8));
            if (!ntwkNum.HasValue && hwyNum.HasValue)
            {
                ntwkNum = hwyNum;
            }

            if (!nodeNum.HasValue && boxNum.HasValue)
            {
                nodeNum = boxNum;
            }
            var moduleCount = reader.IsDBNull(9) ? 0 : Convert.ToInt32(reader.GetValue(9));

            results.Add(new HardwareBoxSummary
            {
                BoxEntityId = reader.GetInt64(0),
                Name = GetPreferredEntityDisplayName(entityName, systemEntityHeader),
                EntityType = entityType,
                SourceFile = sourceFile,
                HighwayNumber = hwyNum,
                BoxNumber = boxNum,
                NetworkNumber = ntwkNum,
                NodeNumber = nodeNum,
                ModuleCount = moduleCount
            });
        }

        return results;
    }

    public async Task<IReadOnlyList<HardwareBoxSummary>> SearchHardwareHwyBoxGroupsAsync(string query, int maxResults = 300)
    {
        await using var connection = await OpenConnectionAsync();

        const string sql = """
            SELECT
                CAST(TRIM(hwy.PropertyValue) AS INTEGER) AS HwyNum,
                CAST(
                    CASE
                        WHEN UPPER(TRIM(COALESCE(box.PropertyValue, ''))) LIKE 'BOX%' THEN TRIM(SUBSTR(TRIM(COALESCE(box.PropertyValue, '')), 4))
                        WHEN UPPER(TRIM(COALESCE(box.PropertyValue, ''))) LIKE 'B%' THEN TRIM(SUBSTR(TRIM(COALESCE(box.PropertyValue, '')), 2))
                        ELSE TRIM(COALESCE(box.PropertyValue, ''))
                    END
                AS INTEGER) AS BoxNum,
                COALESCE(COUNT(DISTINCT e.Id), 0) AS PointCount,
                COALESCE(MIN(NULLIF(TRIM(f.FileName), '')), '') AS SourceFile
            FROM Entities e
            INNER JOIN ImportedFiles f ON f.Id = e.FileId
            INNER JOIN EntityProperties hwy ON hwy.EntityId = e.Id AND UPPER(hwy.PropertyName) = 'HWYNUM'
            INNER JOIN EntityProperties box ON box.EntityId = e.Id AND UPPER(box.PropertyName) = 'BOXNUM'
            WHERE UPPER(COALESCE(e.EntityType, '')) NOT IN ('BOX', 'PM_BOX', '$GROUP', 'HM_HIST')
              AND TRIM(COALESCE(hwy.PropertyValue, '')) <> ''
              AND TRIM(COALESCE(box.PropertyValue, '')) <> ''
              AND CAST(TRIM(COALESCE(hwy.PropertyValue, '0')) AS INTEGER) > 0
              AND CAST(
                    CASE
                        WHEN UPPER(TRIM(COALESCE(box.PropertyValue, ''))) LIKE 'BOX%' THEN TRIM(SUBSTR(TRIM(COALESCE(box.PropertyValue, '')), 4))
                        WHEN UPPER(TRIM(COALESCE(box.PropertyValue, ''))) LIKE 'B%' THEN TRIM(SUBSTR(TRIM(COALESCE(box.PropertyValue, '')), 2))
                        ELSE TRIM(COALESCE(box.PropertyValue, ''))
                    END
                  AS INTEGER) > 0
              AND (
                    $query = ''
                    OR UPPER(COALESCE(e.EntityName, '')) LIKE '%' || $query || '%'
                    OR UPPER(COALESCE(e.SystemEntityHeader, '')) LIKE '%' || $query || '%'
                    OR UPPER(COALESCE(hwy.PropertyValue, '')) LIKE '%' || $query || '%'
                    OR UPPER(COALESCE(box.PropertyValue, '')) LIKE '%' || $query || '%'
                  )
            GROUP BY HwyNum, BoxNum
            ORDER BY HwyNum, BoxNum
            LIMIT $maxResults;
            """;

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("$query", query.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("$maxResults", maxResults);

        var results = new List<HardwareBoxSummary>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var hwyNum = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
            var boxNum = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
            var pointCount = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
            if (hwyNum <= 0 || boxNum <= 0 || pointCount <= 0)
            {
                continue;
            }

            var sourceFile = reader.IsDBNull(3) ? string.Empty : reader.GetString(3).Trim();
            var syntheticId = -((hwyNum * 1000L) + boxNum);
            results.Add(new HardwareBoxSummary
            {
                BoxEntityId = syntheticId,
                Name = $"HWY{hwyNum:00}BOX{boxNum:00}",
                EntityType = "HWY_BOX",
                SourceFile = sourceFile,
                HighwayNumber = hwyNum,
                BoxNumber = boxNum,
                NetworkNumber = null,
                NodeNumber = null,
                ModuleCount = pointCount
            });
        }

        return results;
    }

    public async Task<HardwareBoxDetail?> GetHardwareBoxDetailAsync(long boxEntityId)
    {
        await using var connection = await OpenConnectionAsync();

        const string sql = """
            SELECT
                e.Id,
                COALESCE(NULLIF(TRIM(e.EntityName), ''), '') AS EntityName,
                COALESCE(NULLIF(TRIM(e.EntityType), ''), '') AS EntityType,
                COALESCE(NULLIF(TRIM(e.SystemEntityHeader), ''), '') AS SystemEntityHeader,
                COALESCE(NULLIF(TRIM(f.FileName), ''), '') AS SourceFile
            FROM Entities e
            INNER JOIN ImportedFiles f ON f.Id = e.FileId
            WHERE e.Id = $entityId
              AND UPPER(COALESCE(f.FileName, '')) LIKE '%BOX.EB'
              AND UPPER(COALESCE(e.EntityType, '')) IN ('BOX', 'PM_BOX')
            LIMIT 1;
            """;

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("$entityId", boxEntityId);

        long entityId;
        string entityName;
        string entityType;
        string systemEntityHeader;
        string sourceFile;

        await using (var reader = await command.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync())
            {
                return null;
            }

            entityId = reader.GetInt64(0);
            entityName = reader.GetString(1).Trim();
            entityType = reader.GetString(2).Trim();
            systemEntityHeader = reader.GetString(3).Trim();
            sourceFile = reader.GetString(4).Trim();
        }

        var properties = await GetPropertyDictionaryAsync(connection, entityId);
        var hwyNum = ParseNullableInt(GetPropertyValue(properties, "HWYNUM"));
        var boxNum = ParseNullableHwyBoxNumber(GetPropertyValue(properties, "BOXNUM"));
        var ntwkNum = ParseNullableInt(GetPropertyValue(properties, "NTWKNUM"));
        var nodeNum = ParseNullableInt(GetPropertyValue(properties, "NODENUM"));
        if (!ntwkNum.HasValue && hwyNum.HasValue)
        {
            ntwkNum = hwyNum;
        }

        if (!nodeNum.HasValue && boxNum.HasValue)
        {
            nodeNum = boxNum;
        }

        var modules = GetIndexedValues(properties, "IOMTYPE")
            .Select(x =>
            {
                var moduleType = x.Value.Trim();
                var fileA = ParseNullableInt(GetIndexedProperty(properties, "IOMFILEA", x.Index));
                var cardA = ParseNullableInt(GetIndexedProperty(properties, "IOMCARDA", x.Index));
                var fileB = ParseNullableInt(GetIndexedProperty(properties, "IOMFILEB", x.Index));
                var cardB = ParseNullableInt(GetIndexedProperty(properties, "IOMCARDB", x.Index));
                var redundancy = GetIndexedProperty(properties, "IOREDOPT", x.Index).Trim();

                return new HardwareModuleSummary
                {
                    ModuleIndex = x.Index,
                    ModuleType = moduleType,
                    Redundancy = redundancy,
                    FileA = fileA,
                    CardA = cardA,
                    FileB = fileB,
                    CardB = cardB
                };
            })
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.ModuleType)
                && !x.ModuleType.Equals("NONE", StringComparison.OrdinalIgnoreCase)
                && (x.CardA.GetValueOrDefault() > 0 || x.CardB.GetValueOrDefault() > 0))
            .OrderBy(x => x.ModuleIndex)
            .ToList();

        return new HardwareBoxDetail
        {
            BoxEntityId = entityId,
            Name = GetPreferredEntityDisplayName(entityName, systemEntityHeader),
            EntityType = entityType,
            SourceFile = sourceFile,
            HighwayNumber = hwyNum,
            BoxNumber = boxNum,
            NetworkNumber = ntwkNum,
            NodeNumber = nodeNum,
            Modules = modules
        };
    }

    public async Task<IReadOnlyList<HardwarePointSummary>> GetHardwarePointsForHwyBoxAsync(
        int hwyNum,
        int boxNum,
        int maxResults = 5000)
    {
        if (hwyNum <= 0 || boxNum <= 0 || maxResults <= 0)
        {
            return [];
        }

        await using var connection = await OpenConnectionAsync();

        const string sql = """
            SELECT
                e.Id,
                COALESCE(NULLIF(TRIM(e.EntityName), ''), '') AS EntityName,
                COALESCE(NULLIF(TRIM(e.EntityType), ''), '') AS EntityType,
                COALESCE(NULLIF(TRIM(e.SystemEntityHeader), ''), '') AS SystemEntityHeader,
                COALESCE(NULLIF(TRIM(f.FileName), ''), '') AS SourceFile,
                COALESCE(NULLIF(TRIM(pNtwk.PropertyValue), ''), '') AS NtwkNum,
                COALESCE(NULLIF(TRIM(pNode.PropertyValue), ''), '') AS NodeNum,
                COALESCE(NULLIF(TRIM(pMod.PropertyValue), ''), '') AS ModNum,
                COALESCE(NULLIF(TRIM(pSlot.PropertyValue), ''), '') AS SlotNum
            FROM Entities e
            INNER JOIN ImportedFiles f ON f.Id = e.FileId
            INNER JOIN EntityProperties pHwy ON pHwy.EntityId = e.Id AND UPPER(pHwy.PropertyName) = 'HWYNUM'
            INNER JOIN EntityProperties pBox ON pBox.EntityId = e.Id AND UPPER(pBox.PropertyName) = 'BOXNUM'
            LEFT JOIN EntityProperties pNtwk ON pNtwk.EntityId = e.Id AND UPPER(pNtwk.PropertyName) = 'NTWKNUM'
            LEFT JOIN EntityProperties pNode ON pNode.EntityId = e.Id AND UPPER(pNode.PropertyName) = 'NODENUM'
            LEFT JOIN EntityProperties pMod ON pMod.EntityId = e.Id AND UPPER(pMod.PropertyName) = 'MODNUM'
            LEFT JOIN EntityProperties pSlot ON pSlot.EntityId = e.Id AND UPPER(pSlot.PropertyName) = 'SLOTNUM'
            WHERE UPPER(COALESCE(e.EntityType, '')) NOT IN ('BOX', 'PM_BOX', '$GROUP', 'HM_HIST')
              AND CAST(TRIM(COALESCE(pHwy.PropertyValue, '0')) AS INTEGER) = $hwyNum
              AND CAST(
                    CASE
                        WHEN UPPER(TRIM(COALESCE(pBox.PropertyValue, ''))) LIKE 'BOX%' THEN TRIM(SUBSTR(TRIM(COALESCE(pBox.PropertyValue, '')), 4))
                        WHEN UPPER(TRIM(COALESCE(pBox.PropertyValue, ''))) LIKE 'B%' THEN TRIM(SUBSTR(TRIM(COALESCE(pBox.PropertyValue, '')), 2))
                        ELSE TRIM(COALESCE(pBox.PropertyValue, ''))
                    END
                  AS INTEGER) = $boxNum
            ORDER BY
                CAST(TRIM(COALESCE(pNtwk.PropertyValue, '0')) AS INTEGER),
                CAST(TRIM(COALESCE(pNode.PropertyValue, '0')) AS INTEGER),
                CAST(TRIM(COALESCE(pMod.PropertyValue, '0')) AS INTEGER),
                CAST(TRIM(COALESCE(pSlot.PropertyValue, '0')) AS INTEGER),
                COALESCE(NULLIF(TRIM(e.EntityName), ''), e.SystemEntityHeader),
                e.Id
            LIMIT $maxResults;
            """;

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("$hwyNum", hwyNum);
        command.Parameters.AddWithValue("$boxNum", boxNum);
        command.Parameters.AddWithValue("$maxResults", maxResults);

        var results = new List<HardwarePointSummary>();
        var seen = new HashSet<long>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var entityId = reader.GetInt64(0);
            if (!seen.Add(entityId))
            {
                continue;
            }

            var entityName = reader.GetString(1).Trim();
            var entityType = reader.GetString(2).Trim();
            var systemEntityHeader = reader.GetString(3).Trim();
            var networkNumber = ParseNullableInt(reader.GetString(5)) ?? hwyNum;
            var nodeNumber = ParseNullableInt(reader.GetString(6)) ?? boxNum;
            results.Add(new HardwarePointSummary
            {
                EntityId = entityId,
                Name = GetPreferredEntityDisplayName(entityName, systemEntityHeader),
                EntityType = entityType,
                SourceFile = reader.GetString(4).Trim(),
                NetworkNumber = networkNumber,
                NodeNumber = nodeNumber,
                ModuleNumber = ParseNullableInt(reader.GetString(7)),
                SlotNumber = ParseNullableInt(reader.GetString(8))
            });
        }

        await PopulatePlcAddressesForHardwarePointsAsync(connection, results);
        return results;
    }

    public async Task<IReadOnlyList<HardwarePointSummary>> GetHardwarePointsForModuleAsync(
        long boxEntityId,
        int ntwkNum,
        int nodeNum,
        HardwareModuleSummary module,
        int maxResults = 5000)
    {
        await using var connection = await OpenConnectionAsync();

        async Task<List<HardwarePointSummary>> QueryPointsByModuleNumbersAsync(IReadOnlyList<int> moduleNumbers)
        {
            if (moduleNumbers.Count == 0)
            {
                return [];
            }

            var moduleFilters = moduleNumbers
                .Select((_, index) => $"CAST(TRIM(COALESCE(pMod.PropertyValue, '0')) AS INTEGER) = $mod{index}")
                .ToList();

            var sql = $"""
                SELECT
                    e.Id,
                    COALESCE(NULLIF(TRIM(e.EntityName), ''), '') AS EntityName,
                    COALESCE(NULLIF(TRIM(e.EntityType), ''), '') AS EntityType,
                    COALESCE(NULLIF(TRIM(e.SystemEntityHeader), ''), '') AS SystemEntityHeader,
                    COALESCE(NULLIF(TRIM(f.FileName), ''), '') AS SourceFile,
                    TRIM(pNtwk.PropertyValue) AS NtwkNum,
                    TRIM(pNode.PropertyValue) AS NodeNum,
                    TRIM(pMod.PropertyValue) AS ModNum,
                    TRIM(COALESCE(pSlot.PropertyValue, '')) AS SlotNum
                FROM Entities e
                INNER JOIN ImportedFiles f ON f.Id = e.FileId
                INNER JOIN EntityProperties pNtwk ON pNtwk.EntityId = e.Id AND UPPER(pNtwk.PropertyName) = 'NTWKNUM'
                INNER JOIN EntityProperties pNode ON pNode.EntityId = e.Id AND UPPER(pNode.PropertyName) = 'NODENUM'
                INNER JOIN EntityProperties pMod ON pMod.EntityId = e.Id AND UPPER(pMod.PropertyName) = 'MODNUM'
                LEFT JOIN EntityProperties pSlot ON pSlot.EntityId = e.Id AND UPPER(pSlot.PropertyName) = 'SLOTNUM'
                WHERE e.Id <> $boxEntityId
                  AND UPPER(COALESCE(e.EntityType, '')) NOT IN ('BOX', 'PM_BOX')
                  AND CAST(TRIM(COALESCE(pNtwk.PropertyValue, '0')) AS INTEGER) = $ntwkNum
                  AND CAST(TRIM(COALESCE(pNode.PropertyValue, '0')) AS INTEGER) = $nodeNum
                  AND ({string.Join(" OR ", moduleFilters)})
                ORDER BY
                    CAST(TRIM(COALESCE(pSlot.PropertyValue, '0')) AS INTEGER),
                    COALESCE(NULLIF(TRIM(e.EntityName), ''), e.SystemEntityHeader),
                    e.Id
                LIMIT $maxResults;
                """;

            await using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("$boxEntityId", boxEntityId);
            command.Parameters.AddWithValue("$ntwkNum", ntwkNum);
            command.Parameters.AddWithValue("$nodeNum", nodeNum);
            command.Parameters.AddWithValue("$maxResults", maxResults);
            for (var i = 0; i < moduleNumbers.Count; i++)
            {
                command.Parameters.AddWithValue($"$mod{i}", moduleNumbers[i]);
            }

            var results = new List<HardwarePointSummary>();
            var seen = new HashSet<long>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var entityId = reader.GetInt64(0);
                if (!seen.Add(entityId))
                {
                    continue;
                }

                var entityName = reader.GetString(1).Trim();
                var entityType = reader.GetString(2).Trim();
                var systemEntityHeader = reader.GetString(3).Trim();

                results.Add(new HardwarePointSummary
                {
                    EntityId = entityId,
                    Name = GetPreferredEntityDisplayName(entityName, systemEntityHeader),
                    EntityType = entityType,
                    SourceFile = reader.GetString(4).Trim(),
                    NetworkNumber = ParseNullableInt(reader.GetString(5)),
                    NodeNumber = ParseNullableInt(reader.GetString(6)),
                    ModuleNumber = ParseNullableInt(reader.GetString(7)),
                    SlotNumber = ParseNullableInt(reader.GetString(8))
                });
            }

            await PopulatePlcAddressesForHardwarePointsAsync(connection, results);
            return results;
        }

        var primaryModuleNumbers = new List<int>();
        if (module.ModuleIndex > 0)
        {
            primaryModuleNumbers.Add(module.ModuleIndex);
        }

        var primaryResults = await QueryPointsByModuleNumbersAsync(primaryModuleNumbers);
        if (primaryResults.Count > 0)
        {
            return primaryResults;
        }

        var fallbackModuleNumbers = new List<int>();
        if (module.CardA.GetValueOrDefault() > 0 && !fallbackModuleNumbers.Contains(module.CardA!.Value))
        {
            fallbackModuleNumbers.Add(module.CardA.Value);
        }

        if (module.CardB.GetValueOrDefault() > 0 && !fallbackModuleNumbers.Contains(module.CardB!.Value))
        {
            fallbackModuleNumbers.Add(module.CardB.Value);
        }

        if (fallbackModuleNumbers.Count == 0)
        {
            return primaryResults;
        }

        return await QueryPointsByModuleNumbersAsync(fallbackModuleNumbers);
    }

    public async Task<IReadOnlyList<HardwarePointSummary>> GetHardwarePointsForModiconTrafficCopAsync(
        IReadOnlyList<ModiconTrafficCopEntry> trafficCopEntries,
        long? modiconSystemId,
        int maxAddresses = 5000)
    {
        if (trafficCopEntries is null || trafficCopEntries.Count == 0 || maxAddresses <= 0)
        {
            return [];
        }

        var addressMetadata = new Dictionary<string, (int? DropNumber, int SlotNumber, string ModuleText)>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in trafficCopEntries)
        {
            if (!TryParseModiconTrafficCopRange(entry, out var startAddress, out var endAddress))
            {
                continue;
            }

            var low = Math.Min(startAddress, endAddress);
            var high = Math.Max(startAddress, endAddress);
            for (var address = low; address <= high; address++)
            {
                var registerAddress = address.ToString("D6", CultureInfo.InvariantCulture);
                if (addressMetadata.ContainsKey(registerAddress))
                {
                    continue;
                }

                addressMetadata[registerAddress] = (entry.DropNumber, entry.SlotNumber, entry.ModuleText);
                if (addressMetadata.Count >= maxAddresses)
                {
                    break;
                }
            }

            if (addressMetadata.Count >= maxAddresses)
            {
                break;
            }
        }

        if (addressMetadata.Count == 0)
        {
            return [];
        }

        var orderedAddresses = addressMetadata.Keys
            .OrderBy(x => TryParseAbDataRegister(x, out var parsedAddress) ? parsedAddress : int.MaxValue)
            .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await using var connection = await OpenConnectionAsync();

        var registerDescriptionLookup = new Dictionary<int, string>();
        if (modiconSystemId.HasValue && modiconSystemId.Value > 0)
        {
            const string registersFileSql = """
                SELECT COALESCE(RegistersFilePath, '')
                FROM ModiconSystems
                WHERE Id = $modiconSystemId
                LIMIT 1;
                """;

            await using var registersFileCommand = new SqliteCommand(registersFileSql, connection);
            registersFileCommand.Parameters.AddWithValue("$modiconSystemId", modiconSystemId.Value);
            var registersFileScalar = await registersFileCommand.ExecuteScalarAsync();
            var registersFilePath = (registersFileScalar?.ToString() ?? string.Empty).Trim();
            registerDescriptionLookup = ParseModiconRegisterLookup(registersFilePath);
        }

        var mappingRowsByAddress = new Dictionary<string, List<(string TagName, string TagKey)>>(StringComparer.OrdinalIgnoreCase);
        const int addressBatchSize = 400;
        for (var offset = 0; offset < orderedAddresses.Count; offset += addressBatchSize)
        {
            var batch = orderedAddresses.Skip(offset).Take(addressBatchSize).ToList();
            if (batch.Count == 0)
            {
                continue;
            }

            var placeholders = batch.Select((_, index) => $"$addr{index}").ToList();
            var sql = $"""
                SELECT
                    COALESCE(NULLIF(TRIM(m.RegisterKey), ''), TRIM(m.RegisterAddress)) AS RegisterKey,
                    COALESCE(NULLIF(TRIM(m.TagName), ''), '') AS TagName,
                    COALESCE(NULLIF(TRIM(m.TagKey), ''), '') AS TagKey
                FROM ModiconRegisterMappings m
                WHERE ($modiconSystemId <= 0 OR m.ModiconSystemId = $modiconSystemId)
                  AND UPPER(COALESCE(NULLIF(TRIM(m.RegisterKey), ''), TRIM(m.RegisterAddress))) IN ({string.Join(", ", placeholders)})
                ORDER BY RegisterKey, TagName;
                """;

            await using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("$modiconSystemId", modiconSystemId.GetValueOrDefault());
            for (var index = 0; index < batch.Count; index++)
            {
                command.Parameters.AddWithValue($"$addr{index}", batch[index].Trim().ToUpperInvariant());
            }

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var registerKey = reader.GetString(0).Trim().ToUpperInvariant();
                var tagName = reader.GetString(1).Trim();
                var tagKey = reader.GetString(2).Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(registerKey) || string.IsNullOrWhiteSpace(tagName))
                {
                    continue;
                }

                if (!mappingRowsByAddress.TryGetValue(registerKey, out var rows))
                {
                    rows = [];
                    mappingRowsByAddress[registerKey] = rows;
                }

                if (!rows.Any(x =>
                        x.TagName.Equals(tagName, StringComparison.OrdinalIgnoreCase)
                        && x.TagKey.Equals(tagKey, StringComparison.OrdinalIgnoreCase)))
                {
                    rows.Add((tagName, tagKey));
                }
            }
        }

        var tagKeys = mappingRowsByAddress.Values
            .SelectMany(x => x)
            .Select(x => x.TagKey)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var entityByTagKey = new Dictionary<string, (long EntityId, string PointName, string EntityType, string SourceFile)>(StringComparer.OrdinalIgnoreCase);
        if (tagKeys.Count > 0)
        {
            const int tagBatchSize = 300;
            const string normalizedEntityNameSql = """
                UPPER(
                    CASE
                        WHEN INSTR(COALESCE(NULLIF(TRIM(e.EntityName), ''), TRIM(e.SystemEntityHeader), ''), '.') > 0
                            THEN SUBSTR(
                                COALESCE(NULLIF(TRIM(e.EntityName), ''), TRIM(e.SystemEntityHeader), ''),
                                1,
                                INSTR(COALESCE(NULLIF(TRIM(e.EntityName), ''), TRIM(e.SystemEntityHeader), ''), '.') - 1)
                        ELSE TRIM(COALESCE(NULLIF(TRIM(e.EntityName), ''), TRIM(e.SystemEntityHeader), ''))
                    END
                )
                """;

            for (var offset = 0; offset < tagKeys.Count; offset += tagBatchSize)
            {
                var batch = tagKeys.Skip(offset).Take(tagBatchSize).ToList();
                if (batch.Count == 0)
                {
                    continue;
                }

                var placeholders = batch.Select((_, index) => $"$tag{index}").ToList();
                var sql = $"""
                    SELECT
                        e.Id,
                        COALESCE(NULLIF(TRIM(e.EntityName), ''), '') AS EntityName,
                        COALESCE(NULLIF(TRIM(e.SystemEntityHeader), ''), '') AS SystemEntityHeader,
                        COALESCE(NULLIF(TRIM(e.EntityType), ''), '') AS EntityType,
                        COALESCE(NULLIF(TRIM(f.FileName), ''), '') AS SourceFile,
                        {normalizedEntityNameSql} AS TagKey
                    FROM Entities e
                    INNER JOIN ImportedFiles f ON f.Id = e.FileId
                    WHERE {normalizedEntityNameSql} IN ({string.Join(", ", placeholders)})
                    ORDER BY e.Id;
                    """;

                await using var command = new SqliteCommand(sql, connection);
                for (var index = 0; index < batch.Count; index++)
                {
                    command.Parameters.AddWithValue($"$tag{index}", batch[index].Trim().ToUpperInvariant());
                }

                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var tagKey = reader.GetString(5).Trim().ToUpperInvariant();
                    if (string.IsNullOrWhiteSpace(tagKey) || entityByTagKey.ContainsKey(tagKey))
                    {
                        continue;
                    }

                    var entityName = reader.GetString(1).Trim();
                    var systemEntityHeader = reader.GetString(2).Trim();
                    var pointName = GetPreferredEntityDisplayName(entityName, systemEntityHeader);
                    entityByTagKey[tagKey] = (
                        reader.GetInt64(0),
                        pointName,
                        reader.GetString(3).Trim(),
                        reader.GetString(4).Trim());
                }
            }
        }

        var points = new List<HardwarePointSummary>(orderedAddresses.Count);
        var channel = 1;
        foreach (var registerAddress in orderedAddresses)
        {
            var normalizedAddress = registerAddress.Trim().ToUpperInvariant();
            mappingRowsByAddress.TryGetValue(normalizedAddress, out var mappings);
            mappings ??= [];

            var distinctTags = mappings
                .Select(x => x.TagName.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var tagSummary = FormatTagSummary(distinctTags);
            var primaryTagKey = mappings
                .Select(x => x.TagKey.Trim().ToUpperInvariant())
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                ?? string.Empty;

            var entityMatch = (EntityId: 0L, PointName: string.Empty, EntityType: string.Empty, SourceFile: string.Empty);
            var hasEntity = !string.IsNullOrWhiteSpace(primaryTagKey)
                && entityByTagKey.TryGetValue(primaryTagKey, out entityMatch);

            var registerDescription = string.Empty;
            if (TryParseAbDataRegister(normalizedAddress, out var parsedRegisterAddress))
            {
                registerDescriptionLookup.TryGetValue(parsedRegisterAddress, out registerDescription);
            }

            var registerTag = ExtractModiconTagFromRegisterDescription(registerDescription ?? string.Empty);
            var displayTag = string.IsNullOrWhiteSpace(registerTag) ? tagSummary : registerTag;
            var displayRegisterDescription = RemoveTrailingRegisterTag(registerDescription ?? string.Empty, registerTag);

            var metadata = addressMetadata[normalizedAddress];
            var dropText = metadata.DropNumber.HasValue ? metadata.DropNumber.Value.ToString("00") : "--";
            var location = $"D{dropText}-S{metadata.SlotNumber:000}";

            points.Add(new HardwarePointSummary
            {
                EntityId = hasEntity ? entityMatch.EntityId : 0,
                Name = hasEntity
                    ? (string.IsNullOrWhiteSpace(entityMatch.PointName) ? "(mapped register)" : entityMatch.PointName)
                    : (string.IsNullOrWhiteSpace(tagSummary) ? "(unmapped)" : "(mapped register)"),
                TagName = displayTag,
                Channel = channel++,
                EntityType = hasEntity
                    ? entityMatch.EntityType
                    : (distinctTags.Count > 0 ? "MODICON MAP" : "MODICON"),
                SourceFile = hasEntity ? entityMatch.SourceFile : string.Empty,
                RegisterAddress = normalizedAddress,
                RegisterDescription = displayRegisterDescription,
                CustomLocation = location,
                NodeNumber = metadata.DropNumber,
                SlotNumber = metadata.SlotNumber
            });
        }

        return points;
    }

    public async Task<IReadOnlyDictionary<string, long>> GetEntityNameLookupAsync()
    {
        await using var connection = await OpenConnectionAsync();

        const string sql = """
            SELECT Id, COALESCE(EntityName, ''), COALESCE(SystemEntityHeader, '')
            FROM Entities
            ORDER BY Id;
            """;

        await using var command = new SqliteCommand(sql, connection);

        var lookup = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var entityId = reader.GetInt64(0);
            var rawEntityName = reader.GetString(1).Trim();
            var systemEntityHeader = reader.GetString(2).Trim();
            var displayName = GetPreferredEntityDisplayName(rawEntityName, systemEntityHeader);
            var headerName = ExtractSystemEntityName(systemEntityHeader);

            void AddName(string value)
            {
                var normalized = ExtractConnectionEntity(value);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    return;
                }

                var key = normalized.ToUpperInvariant();
                if (!lookup.ContainsKey(key))
                {
                    lookup[key] = entityId;
                }
            }

            AddName(displayName);
            AddName(rawEntityName);
            AddName(headerName);
        }

        return lookup;
    }

    public async Task<IReadOnlyList<PropertyRow>> GetEntityPropertiesAsync(long entityId)
    {
        await using var connection = await OpenConnectionAsync();

        const string headerSql = """
            SELECT
                EntityName,
                EntityType,
                SystemEntityHeader
            FROM Entities
            WHERE Id = $entityId;
            """;

        var rows = new List<PropertyRow>();
        var entityType = string.Empty;
        var entityName = string.Empty;
        var rawEntityName = string.Empty;
        var systemEntityHeader = string.Empty;

        await using (var headerCommand = new SqliteCommand(headerSql, connection))
        {
            headerCommand.Parameters.AddWithValue("$entityId", entityId);
            await using var headerReader = await headerCommand.ExecuteReaderAsync();
            if (await headerReader.ReadAsync())
            {
                rawEntityName = headerReader.IsDBNull(0) ? string.Empty : headerReader.GetString(0).Trim();
                entityType = headerReader.IsDBNull(1) ? string.Empty : headerReader.GetString(1).Trim();
                systemEntityHeader = headerReader.IsDBNull(2) ? string.Empty : headerReader.GetString(2).Trim();
                entityName = GetPreferredEntityDisplayName(rawEntityName, systemEntityHeader);

                rows.Add(new PropertyRow
                {
                    Property = "Name",
                    Value = entityName,
                    IsLink = entityType.Equals("LOGICNIM", StringComparison.OrdinalIgnoreCase),
                    LinkToken = entityType.Equals("LOGICNIM", StringComparison.OrdinalIgnoreCase)
                        ? $"LB:{entityId}"
                        : null
                });

                rows.Add(new PropertyRow { Property = "_Type", Value = entityType });
            }
        }

        var pkgIsLinkable =
            entityType.Equals("CUSTOMAM", StringComparison.OrdinalIgnoreCase)
            || entityType.Equals("REGAM", StringComparison.OrdinalIgnoreCase);

        var logicLookup = await GetLogicLookupAsync(connection);

        const string propertySql = """
            SELECT PropertyName, PropertyValue
            FROM EntityProperties
            WHERE EntityId = $entityId
            ORDER BY SortOrder;
            """;

        await using var propertyCommand = new SqliteCommand(propertySql, connection);
        propertyCommand.Parameters.AddWithValue("$entityId", entityId);

        var properties = new List<(string Name, string Value)>();
        await using var propertyReader = await propertyCommand.ExecuteReaderAsync();
        while (await propertyReader.ReadAsync())
        {
            properties.Add((propertyReader.GetString(0), propertyReader.GetString(1).Trim()));
        }

        var propertyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in properties)
        {
            propertyMap[name] = value;
        }

        var hardwareTarget = await ResolveHardwareLinkTargetAsync(connection, entityId, entityType, entityName, propertyMap);
        if (hardwareTarget is not null)
        {
            var hardwareLabel = string.IsNullOrWhiteSpace(hardwareTarget.BoxName)
                ? $"Box {hardwareTarget.BoxEntityId}"
                : hardwareTarget.BoxName;
            if (hardwareTarget.ModuleNumber is int moduleNumber && moduleNumber > 0)
            {
                hardwareLabel = $"{hardwareLabel} (MODNUM {moduleNumber})";
            }

            rows.Add(new PropertyRow
            {
                Property = "Hardware",
                Value = hardwareLabel,
                IsLink = true,
                LinkToken = BuildHardwareLinkToken(hardwareTarget)
            });
        }

        if (entityType.Equals("LOGICNIM", StringComparison.OrdinalIgnoreCase))
        {
            var entityLookup = await GetEntityLookupAsync(connection);
            var logicReferenceRows = new List<PropertyRow>();
            var seenReferenceValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var input in GetIndexedValues(propertyMap, "LISRC")
                         .OrderBy(x => x.Index))
            {
                var raw = NormalizeConnectionValue(input.Value);
                var relatedEntity = ExtractConnectionEntity(raw);
                if (string.IsNullOrWhiteSpace(relatedEntity)
                    || relatedEntity.StartsWith("$", StringComparison.Ordinal))
                {
                    continue;
                }

                var uniqueKey = $"IN|{input.Index}|{raw}";
                if (!seenReferenceValues.Add(uniqueKey))
                {
                    continue;
                }

                var normalizedEntityKey = relatedEntity.ToUpperInvariant();
                var hasEntityId = entityLookup.TryGetValue(normalizedEntityKey, out var relatedEntityId);
                logicReferenceRows.Add(new PropertyRow
                {
                    Property = $"Ref In ({input.Index})",
                    Value = raw,
                    IsLink = hasEntityId,
                    LinkToken = hasEntityId ? $"EN:{relatedEntityId}" : null
                });
            }

            foreach (var output in GetIndexedValues(propertyMap, "LODSTN")
                         .OrderBy(x => x.Index))
            {
                var raw = NormalizeConnectionValue(output.Value);
                var relatedEntity = ExtractConnectionEntity(raw);
                if (string.IsNullOrWhiteSpace(relatedEntity)
                    || relatedEntity.StartsWith("$", StringComparison.Ordinal))
                {
                    continue;
                }

                var uniqueKey = $"OUT|{output.Index}|{raw}";
                if (!seenReferenceValues.Add(uniqueKey))
                {
                    continue;
                }

                var normalizedEntityKey = relatedEntity.ToUpperInvariant();
                var hasEntityId = entityLookup.TryGetValue(normalizedEntityKey, out var relatedEntityId);
                logicReferenceRows.Add(new PropertyRow
                {
                    Property = $"Ref Out ({output.Index})",
                    Value = raw,
                    IsLink = hasEntityId,
                    LinkToken = hasEntityId ? $"EN:{relatedEntityId}" : null
                });
            }

            if (logicReferenceRows.Count > 0)
            {
                rows.AddRange(logicReferenceRows
                    .OrderBy(x => x.Property, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Value, StringComparer.OrdinalIgnoreCase));
            }
        }

        foreach (var (propertyName, propertyValue) in properties)
        {
            if (pkgIsLinkable && propertyName.StartsWith("PKGNAME", StringComparison.OrdinalIgnoreCase))
            {
                var clProgram = await FindClProgramByPackageNameAsync(connection, propertyValue);
                rows.Add(new PropertyRow
                {
                    Property = propertyName,
                    Value = propertyValue,
                    IsLink = clProgram is not null,
                    LinkToken = clProgram is null ? null : $"CL:{clProgram.ClProgramId}"
                });
                continue;
            }

            var logicLinkToken = TryGetLogicLinkToken(propertyValue, logicLookup);
            rows.Add(new PropertyRow
            {
                Property = propertyName,
                Value = propertyValue,
                IsLink = logicLinkToken is not null,
                LinkToken = logicLinkToken
            });
        }

        var graphicsTagKeys = new[] { entityName, rawEntityName, ExtractSystemEntityName(systemEntityHeader) }
            .Select(ExtractConnectionEntity)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var graphics = await GetGraphicsForTagKeysAsync(connection, graphicsTagKeys);
        rows.Add(new PropertyRow
        {
            Property = "HMIWeb Graphics",
            Value = graphics.Count > 0 ? string.Join(", ", graphics) : string.Empty
        });

        return rows;
    }

    private static async Task<IReadOnlyList<string>> GetGraphicsForTagKeysAsync(SqliteConnection connection, IReadOnlyList<string> tagKeys)
    {
        if (tagKeys.Count == 0)
        {
            return [];
        }

        var whereParts = new List<string>(tagKeys.Count);
        await using var command = new SqliteCommand { Connection = connection };
        for (var i = 0; i < tagKeys.Count; i++)
        {
            var parameterName = $"$tag{i}";
            whereParts.Add($"TagKey = {parameterName}");
            command.Parameters.AddWithValue(parameterName, tagKeys[i]);
        }

        command.CommandText = $"""
            SELECT GraphicName
            FROM GraphicsMappings
            WHERE {string.Join(" OR ", whereParts)}
            ORDER BY GraphicName COLLATE NOCASE;
            """;

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var graphicName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0).Trim();
            if (string.IsNullOrWhiteSpace(graphicName) || !seen.Add(graphicName))
            {
                continue;
            }

            names.Add(graphicName);
        }

        return names;
    }

    private static async Task<Dictionary<string, long>> GetEntityLookupAsync(SqliteConnection connection)
    {
        const string sql = """
            SELECT Id, COALESCE(EntityName, ''), COALESCE(SystemEntityHeader, '')
            FROM Entities
            ORDER BY Id;
            """;

        await using var command = new SqliteCommand(sql, connection);

        var lookup = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var entityId = reader.GetInt64(0);
            var rawEntityName = reader.GetString(1).Trim();
            var systemEntityHeader = reader.GetString(2).Trim();
            var displayName = GetPreferredEntityDisplayName(rawEntityName, systemEntityHeader);
            var headerName = ExtractSystemEntityName(systemEntityHeader);

            void AddName(string value)
            {
                var normalized = ExtractConnectionEntity(value);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    return;
                }

                var key = normalized.ToUpperInvariant();
                if (!lookup.ContainsKey(key))
                {
                    lookup[key] = entityId;
                }
            }

            AddName(displayName);
            AddName(rawEntityName);
            AddName(headerName);
        }

        return lookup;
    }

    public async Task<ChartDetail?> GetChartDetailAsync(long entityId, bool includeClReferences = true)
    {
        await using var connection = await OpenConnectionAsync();

        const string entitySql = """
            SELECT
                e.Id,
                COALESCE(NULLIF(TRIM(e.EntityName), ''), '') AS EntityName,
                COALESCE(NULLIF(TRIM(e.EntityType), ''), '') AS EntityType,
                COALESCE(NULLIF(TRIM(e.SystemEntityHeader), ''), '') AS SystemEntityHeader,
                COALESCE(NULLIF(TRIM(f.FileName), ''), '') AS SourceFile
            FROM Entities e
            INNER JOIN ImportedFiles f ON f.Id = e.FileId
            WHERE e.Id = $entityId;
            """;

        await using var entityCommand = new SqliteCommand(entitySql, connection);
        entityCommand.Parameters.AddWithValue("$entityId", entityId);

        long id;
        string rawEntityName;
        string entityType;
        string systemEntityHeader;
        string sourceFile;

        await using (var reader = await entityCommand.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync())
            {
                return null;
            }

            id = reader.GetInt64(0);
            rawEntityName = reader.GetString(1);
            entityType = reader.GetString(2);
            systemEntityHeader = reader.GetString(3);
            sourceFile = reader.GetString(4);
        }

        var name = GetPreferredEntityDisplayName(rawEntityName, systemEntityHeader);
        var properties = await GetPropertyDictionaryAsync(connection, entityId);
        var packageNames = GetIndexedValues(properties, "PKGNAME")
            .Select(x => NormalizeProgramKey(x.Value))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var pInputs = GetIndexedValues(properties, "PISRC")
            .Select(x =>
            {
                var sourceRaw = NormalizeConnectionValue(x.Value);
                return new ChartInputReference
                {
                    SourceProperty = "PISRC",
                    Index = x.Index,
                    SourceRaw = sourceRaw,
                    SourceEntity = ExtractConnectionEntity(sourceRaw),
                    DestinationParameter = NormalizeConnectionValue(GetIndexedProperty(properties, "PIDSTN", x.Index))
                };
            });

        var cInputs = GetIndexedValues(properties, "CISRC")
            .Select(x =>
            {
                var sourceRaw = NormalizeConnectionValue(x.Value);
                var destinationParameter = NormalizeConnectionValue(GetIndexedProperty(properties, "CIDSTN", x.Index));
                if (string.IsNullOrWhiteSpace(destinationParameter))
                {
                    destinationParameter = NormalizeConnectionValue(GetIndexedProperty(properties, "PIDSTN", x.Index));
                }

                return new ChartInputReference
                {
                    SourceProperty = "CISRC",
                    Index = x.Index,
                    SourceRaw = sourceRaw,
                    SourceEntity = ExtractConnectionEntity(sourceRaw),
                    DestinationParameter = destinationParameter
                };
            });

        var dInputs = GetIndexedValues(properties, "DISRC")
            .Concat(GetIndexedValues(properties, "DISCRC"))
            .Select(x =>
            {
                var sourceRaw = NormalizeConnectionValue(x.Value);
                return new ChartInputReference
                {
                    SourceProperty = "DISRC",
                    Index = x.Index,
                    SourceRaw = sourceRaw,
                    SourceEntity = ExtractConnectionEntity(sourceRaw),
                    DestinationParameter = string.Empty
                };
            });

        var inputs = pInputs
            .Concat(cInputs)
            .Concat(dInputs)
            .Concat(GetIndexedValues(properties, "LISRC")
                .Select(x =>
                {
                    var sourceRaw = NormalizeConnectionValue(x.Value);
                    return new ChartInputReference
                    {
                        SourceProperty = "LISRC",
                        Index = x.Index,
                        SourceRaw = sourceRaw,
                        SourceEntity = ExtractConnectionEntity(sourceRaw),
                        DestinationParameter = string.Empty
                    };
                }))
            .Where(x => !string.IsNullOrWhiteSpace(x.SourceEntity))
            .OrderBy(x => x.Index)
            .ThenBy(x => x.SourceProperty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var outputs = GetIndexedValues(properties, "CODSTN")
            .Select(x =>
            {
                var destinationRaw = NormalizeConnectionValue(x.Value);
                return new ChartOutputReference
                {
                    SourceProperty = "CODSTN",
                    Index = x.Index,
                    DestinationRaw = destinationRaw,
                    DestinationEntity = ExtractConnectionEntity(destinationRaw)
                };
            })
            .Concat(GetIndexedValues(properties, "LODSTN")
                .Select(x =>
                {
                    var destinationRaw = NormalizeConnectionValue(x.Value);
                    return new ChartOutputReference
                    {
                        SourceProperty = "LODSTN",
                        Index = x.Index,
                        DestinationRaw = destinationRaw,
                        DestinationEntity = ExtractConnectionEntity(destinationRaw)
                    };
                }))
            .Concat(GetIndexedValues(properties, "DODSTN")
                .Select(x =>
                {
                    var destinationRaw = NormalizeConnectionValue(x.Value);
                    return new ChartOutputReference
                    {
                        SourceProperty = "DODSTN",
                        Index = x.Index,
                        DestinationRaw = destinationRaw,
                        DestinationEntity = ExtractConnectionEntity(destinationRaw)
                    };
                }))
            .Where(x => !string.IsNullOrWhiteSpace(x.DestinationEntity))
            .OrderBy(x => x.Index)
            .ThenBy(x => x.SourceProperty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (includeClReferences
            && (entityType.Equals("CUSTOMAM", StringComparison.OrdinalIgnoreCase)
                || entityType.Equals("REGAM", StringComparison.OrdinalIgnoreCase)))
        {
            var attachedProgram = await FindAttachedClProgramForEntityAsync(
                connection,
                name,
                rawEntityName,
                systemEntityHeader,
                properties);
            if (attachedProgram is not null)
            {
                var setTargetLines = ParseSetTargetLines(attachedProgram.FileText);
                var declaredTokens = ParseExternalDeclarations(attachedProgram.FileText)
                    .SelectMany(x => x.Tokens)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var existingInputEntities = new HashSet<string>(
                    inputs.Select(x => x.SourceEntity),
                    StringComparer.OrdinalIgnoreCase);
                var existingOutputEntities = new HashSet<string>(
                    outputs.Select(x => x.DestinationEntity),
                    StringComparer.OrdinalIgnoreCase);

                var ioIndex = 500_000;
                foreach (var token in declaredTokens)
                {
                    if (setTargetLines.ContainsKey(token))
                    {
                        if (existingOutputEntities.Contains(token))
                        {
                            continue;
                        }

                        outputs.Add(new ChartOutputReference
                        {
                            SourceProperty = "CL_SET",
                            Index = ioIndex++,
                            DestinationRaw = token,
                            DestinationEntity = token
                        });
                        existingOutputEntities.Add(token);
                        continue;
                    }

                    if (existingInputEntities.Contains(token))
                    {
                        continue;
                    }

                    inputs.Add(new ChartInputReference
                    {
                        SourceProperty = "CL_EXT",
                        Index = ioIndex++,
                        SourceRaw = token,
                        SourceEntity = token,
                        DestinationParameter = string.Empty
                    });
                    existingInputEntities.Add(token);
                }
            }
        }

        var selectedEntityKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddSelectedEntityKey(string value)
        {
            var candidate = ExtractConnectionEntity(value);
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            if (candidate.StartsWith("$", StringComparison.Ordinal))
            {
                return;
            }

            selectedEntityKeys.Add(candidate.ToUpperInvariant());
        }

        AddSelectedEntityKey(name);
        AddSelectedEntityKey(rawEntityName);
        AddSelectedEntityKey(ExtractSystemEntityName(systemEntityHeader));

        const string crossSql = """
            SELECT
                e.Id,
                COALESCE(NULLIF(TRIM(e.EntityName), ''), '') AS EntityName,
                COALESCE(NULLIF(TRIM(e.SystemEntityHeader), ''), '') AS SystemEntityHeader,
                ep.PropertyName,
                ep.PropertyValue
            FROM EntityProperties ep
            INNER JOIN Entities e ON e.Id = ep.EntityId
            WHERE e.Id <> $entityId
              AND (
                   UPPER(ep.PropertyName) LIKE 'PISRC(%'
                   OR UPPER(ep.PropertyName) LIKE 'CISRC(%'
                   OR UPPER(ep.PropertyName) LIKE 'DISRC(%'
                   OR UPPER(ep.PropertyName) LIKE 'DISCRC(%'
                   OR UPPER(ep.PropertyName) LIKE 'CODSTN(%'
                   OR UPPER(ep.PropertyName) LIKE 'DODSTN(%'
              );
            """;

        var crossReferences = new List<ChartCrossReference>();
        if (selectedEntityKeys.Count > 0)
        {
            await using var crossCommand = new SqliteCommand(crossSql, connection);
            crossCommand.Parameters.AddWithValue("$entityId", entityId);

            await using var crossReader = await crossCommand.ExecuteReaderAsync();
            while (await crossReader.ReadAsync())
            {
                var relatedEntityId = crossReader.GetInt64(0);
                var relatedEntityNameRaw = crossReader.GetString(1).Trim();
                var relatedSystemEntityHeader = crossReader.GetString(2).Trim();
                var propertyName = crossReader.GetString(3).Trim();
                var referenceRaw = NormalizeConnectionValue(crossReader.GetString(4));

                if (!TryParseIndexedPropertyName(propertyName, out var propertyPrefix, out var propertyIndex))
                {
                    continue;
                }

                if (!propertyPrefix.Equals("PISRC", StringComparison.OrdinalIgnoreCase)
                    && !propertyPrefix.Equals("CISRC", StringComparison.OrdinalIgnoreCase)
                    && !propertyPrefix.Equals("DISRC", StringComparison.OrdinalIgnoreCase)
                    && !propertyPrefix.Equals("DISCRC", StringComparison.OrdinalIgnoreCase)
                    && !propertyPrefix.Equals("CODSTN", StringComparison.OrdinalIgnoreCase)
                    && !propertyPrefix.Equals("DODSTN", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var referenceEntity = ExtractConnectionEntity(referenceRaw);
                if (string.IsNullOrWhiteSpace(referenceEntity))
                {
                    continue;
                }

                if (!selectedEntityKeys.Contains(referenceEntity.ToUpperInvariant()))
                {
                    continue;
                }

                crossReferences.Add(new ChartCrossReference
                {
                    RelatedEntityId = relatedEntityId,
                    RelatedEntity = GetPreferredEntityDisplayName(relatedEntityNameRaw, relatedSystemEntityHeader),
                    PropertyName = propertyName,
                    Index = propertyIndex,
                    ReferenceRaw = referenceRaw,
                    IsInboundToSelected =
                        propertyPrefix.Equals("CODSTN", StringComparison.OrdinalIgnoreCase)
                        || propertyPrefix.Equals("DODSTN", StringComparison.OrdinalIgnoreCase)
                });
            }
        }

        crossReferences = crossReferences
            .OrderBy(x => x.IsInboundToSelected ? 0 : 1)
            .ThenBy(x => x.RelatedEntity, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Index)
            .ToList();

        var logicReferences = includeClReferences && selectedEntityKeys.Count > 0
            ? await FindLogicBlockReferencesAsync(connection, entityId, selectedEntityKeys)
            : [];
        var clReferences = includeClReferences && selectedEntityKeys.Count > 0
            ? await FindClExternalReferencesAsync(connection, selectedEntityKeys)
            : [];
        var pointHwyNum = ParseNullableInt(GetPropertyValue(properties, "HWYNUM"));
        var pointBoxNum = ParseNullableHwyBoxNumber(GetPropertyValue(properties, "BOXNUM"))
            ?? ParseNullableHwyBoxNumber(GetPropertyValue(properties, "INBOXNM"))
            ?? ParseNullableHwyBoxNumber(GetPropertyValue(properties, "OUTBOXNM"));
        var plcReferences = selectedEntityKeys.Count > 0
            ? await FindAbPlcReferencesAsync(connection, selectedEntityKeys, pointHwyNum, pointBoxNum)
            : [];
        var softReferences = selectedEntityKeys.Count > 0
            ? await FindSoftConnectionReferencesAsync(connection, selectedEntityKeys)
            : [];
        var piReferences = selectedEntityKeys.Count > 0
            ? await FindPiReferencesAsync(connection, selectedEntityKeys)
            : [];

        return new ChartDetail
        {
            EntityId = id,
            Name = name,
            EntityType = entityType,
            SourceFile = sourceFile,
            PackageNames = packageNames,
            Inputs = inputs,
            Outputs = outputs,
            CrossReferences = crossReferences,
            LogicReferences = logicReferences,
            ClReferences = clReferences,
            PlcReferences = plcReferences,
            SoftReferences = softReferences,
            PiReferences = piReferences
        };
    }

    private sealed class AttachedClProgram
    {
        public long Id { get; init; }
        public string ProgramName { get; init; } = string.Empty;
        public string ProgramType { get; init; } = string.Empty;
        public string FilePath { get; init; } = string.Empty;
        public string FileText { get; init; } = string.Empty;
    }

    private async Task<AttachedClProgram?> FindAttachedClProgramForEntityAsync(
        SqliteConnection connection,
        string displayName,
        string rawEntityName,
        string systemEntityHeader,
        IReadOnlyDictionary<string, string> propertyMap)
    {
        var candidateKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddCandidate(string value)
        {
            var key = NormalizeProgramKey(value);
            if (!string.IsNullOrWhiteSpace(key))
            {
                candidateKeys.Add(key);
            }
        }

        AddCandidate(displayName);
        AddCandidate(rawEntityName);
        AddCandidate(ExtractSystemEntityName(systemEntityHeader));

        foreach (var (propertyName, propertyValue) in propertyMap)
        {
            if (!propertyName.StartsWith("PKGNAME", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AddCandidate(propertyValue);
        }

        if (candidateKeys.Count == 0)
        {
            return null;
        }

        var predicates = candidateKeys
            .Select((_, i) => $"ProgramNameKey = $k{i}")
            .ToList();
        var sql = $"""
            SELECT Id, ProgramName, ProgramType, FilePath, FileText
            FROM ClPrograms
            WHERE {string.Join(" OR ", predicates)}
            ORDER BY
                CASE ProgramType
                    WHEN 'AMCL' THEN 0
                    WHEN 'PMCL' THEN 1
                    ELSE 2
                END,
                ProgramName,
                Id
            LIMIT 1;
            """;

        await using var command = new SqliteCommand(sql, connection);
        var keyIndex = 0;
        foreach (var key in candidateKeys)
        {
            command.Parameters.AddWithValue($"$k{keyIndex++}", key);
        }

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new AttachedClProgram
        {
            Id = reader.GetInt64(0),
            ProgramName = reader.GetString(1).Trim(),
            ProgramType = reader.GetString(2).Trim(),
            FilePath = reader.GetString(3).Trim(),
            FileText = reader.GetString(4)
        };
    }

    public async Task<IReadOnlyList<LogicBlockSummary>> SearchLogicBlocksAsync(string query, int maxResults = 500)
    {
        await using var connection = await OpenConnectionAsync();

        const string sql = """
            SELECT
                e.Id,
                COALESCE(NULLIF(TRIM(e.EntityName), ''), e.SystemEntityHeader) AS Name,
                COALESCE(MAX(CASE WHEN ep.PropertyName = 'PTDESC' THEN ep.PropertyValue END), '') AS Description,
                f.FileName
            FROM Entities e
            INNER JOIN ImportedFiles f ON f.Id = e.FileId
            LEFT JOIN EntityProperties ep ON ep.EntityId = e.Id
            WHERE UPPER(COALESCE(e.EntityType, '')) = 'LOGICNIM'
              AND (
                   $queryLike = ''
                   OR UPPER(COALESCE(e.EntityName, '')) LIKE $queryLike ESCAPE '\'
                   OR UPPER(COALESCE(e.SystemEntityHeader, '')) LIKE $queryLike ESCAPE '\'
                   OR UPPER(COALESCE(ep.PropertyValue, '')) LIKE $queryLike ESCAPE '\'
              )
            GROUP BY e.Id, Name, f.FileName
            ORDER BY Name
            LIMIT $maxResults;
            """;

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("$queryLike", BuildSqlLikePattern(query, normalizeUpper: true));
        command.Parameters.AddWithValue("$maxResults", maxResults);

        var results = new List<LogicBlockSummary>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new LogicBlockSummary
            {
                EntityId = reader.GetInt64(0),
                Name = reader.GetString(1),
                Description = reader.GetString(2).Trim(),
                SourceFile = reader.GetString(3)
            });
        }

        return results;
    }

    public async Task<LogicBlockDetail?> GetLogicBlockDetailAsync(long entityId)
    {
        await using var connection = await OpenConnectionAsync();

        const string entitySql = """
            SELECT
                e.Id,
                COALESCE(NULLIF(TRIM(e.EntityName), ''), e.SystemEntityHeader) AS Name,
                COALESCE(NULLIF(TRIM(f.FileName), ''), '') AS SourceFile
            FROM Entities e
            INNER JOIN ImportedFiles f ON f.Id = e.FileId
            WHERE e.Id = $entityId
              AND UPPER(COALESCE(e.EntityType, '')) = 'LOGICNIM';
            """;

        await using var entityCommand = new SqliteCommand(entitySql, connection);
        entityCommand.Parameters.AddWithValue("$entityId", entityId);

        long id;
        string name;
        string sourceFile;

        await using (var reader = await entityCommand.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync())
            {
                return null;
            }

            id = reader.GetInt64(0);
            name = reader.GetString(1).Trim();
            sourceFile = reader.GetString(2).Trim();
        }

        var properties = await GetPropertyDictionaryAsync(connection, entityId);
        var description = GetPropertyValue(properties, "PTDESC");

        var inputs = GetIndexedValues(properties, "LISRC")
            .Select(x => new LogicInput { Index = x.Index, Source = x.Value })
            .Where(x => !string.IsNullOrWhiteSpace(x.Source) && x.Source != "--.--")
            .ToList();

        var numerics = GetIndexedValues(properties, "NN")
            .Select(x => new LogicNumeric { Index = x.Index, Value = x.Value })
            .ToList();

        var gates = new List<LogicGate>();
        foreach (var gate in GetIndexedValues(properties, "LOGALGID"))
        {
            if (string.Equals(gate.Value, "NULL", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var gateInputs = GetGateInputs(properties, gate.Index);
            var periodRaw = GetIndexedProperty(properties, "DLYTIME", gate.Index);
            double? periodSeconds = null;
            if (!string.IsNullOrWhiteSpace(periodRaw)
                && double.TryParse(periodRaw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedPeriod))
            {
                periodSeconds = parsedPeriod;
            }

            gates.Add(new LogicGate
            {
                Index = gate.Index,
                Algorithm = gate.Value,
                Inputs = gateInputs,
                PeriodSeconds = periodSeconds
            });
        }

        var outputs = GetIndexedValues(properties, "LOSRC")
            .Select(x => new LogicOutput
            {
                Index = x.Index,
                Source = x.Value,
                Destination = GetIndexedProperty(properties, "LODSTN", x.Index),
                Enable = GetIndexedProperty(properties, "LOENBL", x.Index)
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Source) || !string.IsNullOrWhiteSpace(x.Destination))
            .ToList();

        return new LogicBlockDetail
        {
            EntityId = id,
            Name = name,
            Description = description,
            SourceFile = sourceFile,
            Inputs = inputs,
            Numerics = numerics,
            Gates = gates,
            Outputs = outputs
        };
    }

    public async Task<LogicBlockSummary?> FindLogicBlockByNameAsync(string entityName)
    {
        if (string.IsNullOrWhiteSpace(entityName))
        {
            return null;
        }

        await using var connection = await OpenConnectionAsync();

        const string sql = """
            SELECT
                e.Id,
                COALESCE(NULLIF(TRIM(e.EntityName), ''), e.SystemEntityHeader) AS Name,
                COALESCE(MAX(CASE WHEN ep.PropertyName = 'PTDESC' THEN ep.PropertyValue END), '') AS Description,
                f.FileName
            FROM Entities e
            INNER JOIN ImportedFiles f ON f.Id = e.FileId
            LEFT JOIN EntityProperties ep ON ep.EntityId = e.Id
            WHERE UPPER(COALESCE(e.EntityType, '')) = 'LOGICNIM'
              AND UPPER(COALESCE(e.EntityName, '')) = $name
            GROUP BY e.Id, Name, f.FileName
            ORDER BY e.Id
            LIMIT 1;
            """;

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("$name", entityName.Trim().ToUpperInvariant());

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new LogicBlockSummary
        {
            EntityId = reader.GetInt64(0),
            Name = reader.GetString(1),
            Description = reader.GetString(2).Trim(),
            SourceFile = reader.GetString(3)
        };
    }

    public async Task<IReadOnlyList<ClProgramListItem>> SearchClProgramsAsync(string query, int maxResults = 500)
    {
        await using var connection = await OpenConnectionAsync();

        const string sql = """
            SELECT Id, ProgramName, ProgramType, FilePath
            FROM ClPrograms
            WHERE $queryLike = ''
               OR ProgramNameKey LIKE $queryLike ESCAPE '\'
            ORDER BY ProgramName
            LIMIT $maxResults;
            """;

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("$queryLike", BuildSqlLikePattern(query, normalizeUpper: true));
        command.Parameters.AddWithValue("$maxResults", maxResults);

        var results = new List<ClProgramListItem>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new ClProgramListItem
            {
                ClProgramId = reader.GetInt64(0),
                ProgramName = reader.GetString(1),
                ProgramType = reader.GetString(2),
                FilePath = reader.GetString(3)
            });
        }

        return results;
    }

    public async Task<ClProgramDetail?> GetClProgramAsync(long clProgramId)
    {
        await using var connection = await OpenConnectionAsync();

        const string sql = """
            SELECT Id, ProgramName, ProgramType, FilePath, FileText
            FROM ClPrograms
            WHERE Id = $id;
            """;

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("$id", clProgramId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new ClProgramDetail
        {
            ClProgramId = reader.GetInt64(0),
            ProgramName = reader.GetString(1),
            ProgramType = reader.GetString(2),
            FilePath = reader.GetString(3),
            FileText = reader.GetString(4)
        };
    }

    public async Task<ClProgramListItem?> FindClProgramByPackageNameAsync(string pkgName)
    {
        await using var connection = await OpenConnectionAsync();
        return await FindClProgramByPackageNameAsync(connection, pkgName);
    }

    private async Task<ClProgramListItem?> FindClProgramByPackageNameAsync(SqliteConnection connection, string pkgName)
    {
        var key = NormalizeProgramKey(pkgName);
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        const string sql = """
            SELECT Id, ProgramName, ProgramType, FilePath
            FROM ClPrograms
            WHERE ProgramNameKey = $key
            ORDER BY
                CASE ProgramType
                    WHEN 'AMCL' THEN 0
                    WHEN 'PMCL' THEN 1
                    ELSE 2
                END,
                ProgramName,
                Id
            LIMIT 1;
            """;

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("$key", key);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new ClProgramListItem
        {
            ClProgramId = reader.GetInt64(0),
            ProgramName = reader.GetString(1),
            ProgramType = reader.GetString(2),
            FilePath = reader.GetString(3)
        };
    }

    private static async Task<List<ChartClReference>> FindClExternalReferencesAsync(
        SqliteConnection connection,
        IReadOnlyCollection<string> entityKeys)
    {
        var tags = entityKeys
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (tags.Count == 0)
        {
            return [];
        }

        var whereClauses = tags
            .Select((_, index) => $"UPPER(FileText) LIKE '%' || $tag{index} || '%'")
            .ToList();
        var sql = $"""
            SELECT Id, ProgramName, ProgramType, FilePath, FileText
            FROM ClPrograms
            WHERE ({string.Join(" OR ", whereClauses)});
            """;

        await using var command = new SqliteCommand(sql, connection);
        for (var i = 0; i < tags.Count; i++)
        {
            command.Parameters.AddWithValue($"$tag{i}", tags[i]);
        }

        var tagSet = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);
        var references = new List<ChartClReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var clProgramId = reader.GetInt64(0);
            var programName = reader.GetString(1).Trim();
            var programNameKey = programName.ToUpperInvariant();
            var programType = reader.GetString(2).Trim();
            var filePath = reader.GetString(3).Trim();
            var fileText = reader.GetString(4);
            var setTargetLines = ParseSetTargetLines(fileText);
            var includeAllProgramExternalTokens = tagSet.Contains(programNameKey);

            foreach (var usage in ParseClLogicUsageTagReferences(fileText, tagSet))
            {
                var uniqueKey = $"LOGIC|{clProgramId}|{usage.Tag}|{usage.LineNumber}|{usage.ReferenceKind}";
                if (!seen.Add(uniqueKey))
                {
                    continue;
                }

                references.Add(new ChartClReference
                {
                    ClProgramId = clProgramId,
                    ProgramName = programName,
                    ProgramType = programType,
                    FilePath = filePath,
                    ReferenceKind = usage.ReferenceKind,
                    ExternalTag = usage.Tag,
                    LineNumber = usage.LineNumber,
                    DeclarationText = usage.StatementText,
                    IsWriteToTag = false,
                    SetLineNumber = null
                });
            }

            foreach (var declaration in ParseExternalDeclarations(fileText))
            {
                foreach (var token in declaration.Tokens)
                {
                    if (!includeAllProgramExternalTokens && !tagSet.Contains(token))
                    {
                        continue;
                    }

                    var uniqueKey = $"{clProgramId}|{token}|{declaration.LineNumber}";
                    if (!seen.Add(uniqueKey))
                    {
                        continue;
                    }

                    var hasSetWrite = false;
                    int? setLineNumber = null;
                    if (setTargetLines.TryGetValue(token, out var writeLines) && writeLines.Count > 0)
                    {
                        hasSetWrite = true;
                        setLineNumber = writeLines[0];
                    }

                    references.Add(new ChartClReference
                    {
                        ClProgramId = clProgramId,
                        ProgramName = programName,
                        ProgramType = programType,
                        FilePath = filePath,
                        ReferenceKind = "EXTERNAL",
                        ExternalTag = token,
                        LineNumber = declaration.LineNumber,
                        DeclarationText = declaration.DeclarationText,
                        IsWriteToTag = hasSetWrite,
                        SetLineNumber = setLineNumber
                    });
                }
            }
        }

        return references
            .OrderBy(x => x.ProgramType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ProgramName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.IsWriteToTag ? 1 : 0)
            .ThenBy(x => x.ReferenceKind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.LineNumber)
            .ToList();
    }

    private static async Task<List<ChartLogicReference>> FindLogicBlockReferencesAsync(
        SqliteConnection connection,
        long selectedEntityId,
        IReadOnlyCollection<string> selectedEntityKeys)
    {
        if (selectedEntityKeys.Count == 0)
        {
            return [];
        }

        const string sql = """
            SELECT
                e.Id,
                COALESCE(NULLIF(TRIM(e.EntityName), ''), '') AS EntityName,
                COALESCE(NULLIF(TRIM(e.SystemEntityHeader), ''), '') AS SystemEntityHeader,
                ep.PropertyName,
                ep.PropertyValue
            FROM EntityProperties ep
            INNER JOIN Entities e ON e.Id = ep.EntityId
            WHERE e.Id <> $entityId
              AND UPPER(COALESCE(e.EntityType, '')) = 'LOGICNIM'
              AND (
                    UPPER(ep.PropertyName) LIKE 'LISRC(%'
                    OR UPPER(ep.PropertyName) LIKE 'LOSRC(%'
                    OR UPPER(ep.PropertyName) LIKE 'LODSTN(%'
                  );
            """;

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("$entityId", selectedEntityId);

        var selectedKeySet = new HashSet<string>(
            selectedEntityKeys
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToUpperInvariant()),
            StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<ChartLogicReference>();

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var logicEntityId = reader.GetInt64(0);
            var logicEntityRaw = reader.GetString(1).Trim();
            var logicSystemEntityHeader = reader.GetString(2).Trim();
            var propertyName = reader.GetString(3).Trim();
            var referenceRaw = NormalizeConnectionValue(reader.GetString(4));

            if (!TryParseIndexedPropertyName(propertyName, out var propertyPrefix, out var propertyIndex))
            {
                continue;
            }

            if (!propertyPrefix.Equals("LISRC", StringComparison.OrdinalIgnoreCase)
                && !propertyPrefix.Equals("LOSRC", StringComparison.OrdinalIgnoreCase)
                && !propertyPrefix.Equals("LODSTN", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var referenceEntity = ExtractConnectionEntity(referenceRaw);
            if (string.IsNullOrWhiteSpace(referenceEntity)
                || !selectedKeySet.Contains(referenceEntity.ToUpperInvariant()))
            {
                continue;
            }

            var uniqueKey = $"{logicEntityId}|{propertyName}|{referenceRaw}";
            if (!seen.Add(uniqueKey))
            {
                continue;
            }

            results.Add(new ChartLogicReference
            {
                LogicEntityId = logicEntityId,
                LogicEntity = GetPreferredEntityDisplayName(logicEntityRaw, logicSystemEntityHeader),
                PropertyName = propertyName,
                Index = propertyIndex,
                ReferenceRaw = referenceRaw,
                IsInboundToSelected = propertyPrefix.Equals("LODSTN", StringComparison.OrdinalIgnoreCase)
            });
        }

        return results
            .OrderBy(x => x.IsInboundToSelected ? 0 : 1)
            .ThenBy(x => x.LogicEntity, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Index)
            .ThenBy(x => x.PropertyName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<List<ChartPlcReference>> FindAbPlcReferencesAsync(
        SqliteConnection connection,
        IReadOnlyCollection<string> entityKeys,
        int? pointHwyNum = null,
        int? pointBoxNum = null)
    {
        var tags = entityKeys
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (tags.Count == 0)
        {
            return [];
        }

        var tagPlaceholders = tags.Select((_, index) => $"$tag{index}").ToList();
        var sql = $"""
            SELECT
                'AB' AS InterfaceType,
                m.PlcSystemId,
                s.PlcName,
                s.ConnectionType,
                COALESCE(s.ConnectionDetails, ''),
                s.HwyNum,
                s.HwyBoxNumber,
                COALESCE(s.HwyBoxNumbersCsv, ''),
                s.NodeAddress,
                COALESCE(s.Plc5FileAddress, ''),
                COALESCE(s.SerialPort, ''),
                COALESCE(s.SerialSettings, ''),
                m.RegisterAddress,
                m.TagName,
                COALESCE(m.SourceLine, '')
            FROM AbPlcRegisterMappings m
            INNER JOIN AbPlcSystems s ON s.Id = m.PlcSystemId
            WHERE UPPER(m.TagKey) IN ({string.Join(", ", tagPlaceholders)})
               OR UPPER(
                    CASE
                        WHEN INSTR(COALESCE(m.TagName, ''), '.') > 0
                            THEN SUBSTR(COALESCE(m.TagName, ''), 1, INSTR(COALESCE(m.TagName, ''), '.') - 1)
                        ELSE TRIM(COALESCE(m.TagName, ''))
                    END
                ) IN ({string.Join(", ", tagPlaceholders)})
            UNION ALL
            SELECT
                'MODICON' AS InterfaceType,
                m.ModiconSystemId,
                s.PlcName,
                s.ConnectionType,
                COALESCE(s.ConnectionDetails, ''),
                s.HwyNum,
                s.HwyBoxNumber,
                COALESCE(s.HwyBoxNumbersCsv, ''),
                s.NodeAddress,
                '',
                COALESCE(s.SerialPort, ''),
                COALESCE(s.SerialSettings, ''),
                m.RegisterAddress,
                m.TagName,
                COALESCE(m.SourceLine, '')
            FROM ModiconRegisterMappings m
            INNER JOIN ModiconSystems s ON s.Id = m.ModiconSystemId
            WHERE UPPER(m.TagKey) IN ({string.Join(", ", tagPlaceholders)})
               OR UPPER(
                    CASE
                        WHEN INSTR(COALESCE(m.TagName, ''), '.') > 0
                            THEN SUBSTR(COALESCE(m.TagName, ''), 1, INSTR(COALESCE(m.TagName, ''), '.') - 1)
                        ELSE TRIM(COALESCE(m.TagName, ''))
                    END
                ) IN ({string.Join(", ", tagPlaceholders)})
            ORDER BY PlcName, RegisterAddress, TagName;
            """;

        await using var command = new SqliteCommand(sql, connection);
        for (var i = 0; i < tags.Count; i++)
        {
            command.Parameters.AddWithValue($"$tag{i}", tags[i]);
        }

        var results = new List<ChartPlcReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var interfaceType = reader.GetString(0).Trim();
            var plcSystemId = reader.GetInt64(1);
            var plcName = reader.GetString(2).Trim();
            var connectionType = reader.GetString(3).Trim();
            var connectionDetails = reader.GetString(4).Trim();
            var hwyNum = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5);
            var hwyBoxNumber = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6);
            var hwyBoxNumbersCsv = reader.GetString(7).Trim();
            var nodeAddress = reader.IsDBNull(8) ? (int?)null : reader.GetInt32(8);
            var plc5FileAddress = reader.GetString(9).Trim();
            var serialPort = reader.GetString(10).Trim();
            var serialSettings = reader.GetString(11).Trim();
            var registerAddress = reader.GetString(12).Trim();
            var tagName = reader.GetString(13).Trim();
            var sourceLine = reader.GetString(14).Trim();

            if (pointHwyNum.HasValue
                && pointBoxNum.HasValue
                && sourceLine.Contains("EPLCG", StringComparison.OrdinalIgnoreCase)
                && !PlcSystemMatchesHwyBox(hwyNum, hwyBoxNumber, hwyBoxNumbersCsv, pointHwyNum.Value, pointBoxNum.Value))
            {
                continue;
            }

            var uniqueKey = $"{interfaceType}|{plcSystemId}|{registerAddress}|{tagName}";
            if (!seen.Add(uniqueKey))
            {
                continue;
            }

            results.Add(new ChartPlcReference
            {
                InterfaceType = interfaceType,
                PlcSystemId = plcSystemId,
                PlcName = plcName,
                ConnectionType = connectionType,
                ConnectionDetails = connectionDetails,
                HwyNum = hwyNum,
                HwyBoxNumber = hwyBoxNumber,
                HwyBoxNumbersCsv = hwyBoxNumbersCsv,
                NodeAddress = nodeAddress,
                Plc5FileAddress = plc5FileAddress,
                SerialPort = serialPort,
                SerialSettings = serialSettings,
                RegisterAddress = registerAddress,
                TagName = tagName,
                SourceLine = sourceLine
            });
        }

        return results;
    }

    private static async Task<List<ChartSoftReference>> FindSoftConnectionReferencesAsync(
        SqliteConnection connection,
        IReadOnlyCollection<string> entityKeys)
    {
        var tags = entityKeys
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (tags.Count == 0)
        {
            return [];
        }

        var placeholders = tags.Select((_, index) => $"$tag{index}").ToList();
        var sql = $"""
            SELECT
                SourceEntityKey,
                SourceReference,
                TargetEntityKey,
                TargetReference,
                Type,
                SourceRowNumber
            FROM SoftConnections
            WHERE SourceEntityKey IN ({string.Join(", ", placeholders)})
               OR TargetEntityKey IN ({string.Join(", ", placeholders)})
            ORDER BY SourceRowNumber, SourceReference, TargetReference;
            """;

        await using var command = new SqliteCommand(sql, connection);
        for (var i = 0; i < tags.Count; i++)
        {
            command.Parameters.AddWithValue($"$tag{i}", tags[i]);
        }

        var selected = tags.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var results = new List<ChartSoftReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var sourceEntityKey = reader.GetString(0).Trim().ToUpperInvariant();
            var sourceReference = reader.GetString(1).Trim();
            var targetEntityKey = reader.GetString(2).Trim().ToUpperInvariant();
            var targetReference = reader.GetString(3).Trim();
            var softType = reader.GetString(4).Trim();
            var sourceRowNumber = reader.GetInt32(5);

            var sourceIsSelected = selected.Contains(sourceEntityKey);
            var targetIsSelected = selected.Contains(targetEntityKey);
            if (!sourceIsSelected && !targetIsSelected)
            {
                continue;
            }

            if (sourceEntityKey.Equals(targetEntityKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var isInbound = !sourceIsSelected && targetIsSelected;
            var relatedEntity = isInbound ? sourceEntityKey : targetEntityKey;
            if (string.IsNullOrWhiteSpace(relatedEntity))
            {
                continue;
            }

            var uniqueKey = $"{relatedEntity}|{sourceReference}|{targetReference}|{isInbound}";
            if (!seen.Add(uniqueKey))
            {
                continue;
            }

            results.Add(new ChartSoftReference
            {
                RelatedEntity = relatedEntity,
                SourceReference = sourceReference,
                TargetReference = targetReference,
                SoftType = softType,
                SourceRowNumber = sourceRowNumber,
                IsInboundToSelected = isInbound
            });
        }

        return results
            .OrderBy(x => x.IsInboundToSelected ? 0 : 1)
            .ThenBy(x => x.RelatedEntity, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SourceRowNumber)
            .ToList();
    }

    private static async Task<List<ChartPiReference>> FindPiReferencesAsync(
        SqliteConnection connection,
        IReadOnlyCollection<string> entityKeys)
    {
        var tags = entityKeys
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (tags.Count == 0)
        {
            return [];
        }

        var placeholders = tags.Select((_, index) => $"$tag{index}").ToList();
        var sql = $"""
            SELECT
                PiTagName,
                InstrumentTag,
                SourceRowNumber
            FROM PiMappings
            WHERE InstrumentTagKey IN ({string.Join(", ", placeholders)})
            ORDER BY SourceRowNumber, PiTagName;
            """;

        await using var command = new SqliteCommand(sql, connection);
        for (var i = 0; i < tags.Count; i++)
        {
            command.Parameters.AddWithValue($"$tag{i}", tags[i]);
        }

        var results = new List<ChartPiReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var piTagName = reader.GetString(0).Trim();
            var instrumentTag = reader.GetString(1).Trim();
            var sourceRowNumber = reader.GetInt32(2);
            if (string.IsNullOrWhiteSpace(piTagName) || string.IsNullOrWhiteSpace(instrumentTag))
            {
                continue;
            }

            var uniqueKey = $"{instrumentTag}|{piTagName}";
            if (!seen.Add(uniqueKey))
            {
                continue;
            }

            results.Add(new ChartPiReference
            {
                PiTagName = piTagName,
                InstrumentTag = instrumentTag,
                SourceRowNumber = sourceRowNumber
            });
        }

        return results
            .OrderBy(x => x.PiTagName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SourceRowNumber)
            .ToList();
    }

    private static async Task<List<(string RegisterAddress, string RegisterKey, string TagName, string TagKey, string SourceLine)>> ParseModiconMappingsFromEntitiesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string registersFilePath,
        int hwyNum,
        IReadOnlyList<int> hwyBoxNumbers,
        int? nodeAddress,
        string connectionType,
        int? serialHpmNumber,
        int? serialModuleNumber,
        bool isSerialOnlyConnection)
    {
        var mappings = new List<(string RegisterAddress, string RegisterKey, string TagName, string TagKey, string SourceLine)>();
        var registerLookup = ParseModiconRegisterLookup(registersFilePath);
        var specifByEntityAndProperty = new Dictionary<(long EntityId, string PropertyName), int>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedHwyBoxes = (hwyBoxNumbers ?? [])
            .Where(value => value >= 0)
            .ToHashSet();
        var normalizedConnectionType = string.IsNullOrWhiteSpace(connectionType) ? "EPLCG" : connectionType.Trim();
        var isMixedConnectionType = normalizedConnectionType.Equals("EPLCG + HPM", StringComparison.OrdinalIgnoreCase)
            || normalizedConnectionType.Equals("EPLCG+HPM", StringComparison.OrdinalIgnoreCase);
        var wantsSerialScope = isSerialOnlyConnection || isMixedConnectionType;
        var wantsEplcgScope = !isSerialOnlyConnection;
        var serialNodeNumber = serialHpmNumber ?? -1;
        var serialIoModuleNumber = serialModuleNumber ?? -1;
        var hasExplicitSerialCriteria = serialHpmNumber.HasValue && serialModuleNumber.HasValue;
        if (!hasExplicitSerialCriteria && wantsSerialScope)
        {
            // Backward compatibility with older imports where serial criteria were stored in node/box fields.
            serialNodeNumber = nodeAddress ?? -1;
            serialIoModuleNumber = normalizedHwyBoxes.Count > 0 ? normalizedHwyBoxes.Min() : -1;
        }

        const string specifSql = """
            SELECT
                e.Id,
                UPPER(TRIM(ep.PropertyName)) AS PropertyName,
                TRIM(ep.PropertyValue) AS PropertyValue
            FROM Entities e
            INNER JOIN EntityProperties pt ON pt.EntityId = e.Id
                AND UPPER(pt.PropertyName) = 'PNTPCTY'
                AND UPPER(TRIM(pt.PropertyValue)) = 'MODICON'
            INNER JOIN EntityProperties ep ON ep.EntityId = e.Id
            WHERE UPPER(ep.PropertyName) LIKE 'SPECIFI%'
               OR UPPER(ep.PropertyName) LIKE 'SPECIFO%';
            """;

        await using (var specifCommand = new SqliteCommand(specifSql, connection, transaction))
        await using (var specifReader = await specifCommand.ExecuteReaderAsync())
        {
            while (await specifReader.ReadAsync())
            {
                var entityId = specifReader.GetInt64(0);
                var specifPropertyName = specifReader.GetString(1).Trim().ToUpperInvariant();
                var specifRaw = specifReader.GetString(2).Trim();
                if (string.IsNullOrWhiteSpace(specifPropertyName))
                {
                    continue;
                }

                if (!TryParseDecimalInteger(specifRaw, out var specifValue) || specifValue < 0)
                {
                    continue;
                }

                specifByEntityAndProperty[(entityId, specifPropertyName)] = specifValue;
            }
        }

        const string sql = """
            SELECT
                e.Id,
                COALESCE(NULLIF(TRIM(e.EntityName), ''), '') AS EntityName,
                COALESCE(NULLIF(TRIM(e.SystemEntityHeader), ''), '') AS SystemEntityHeader,
                COALESCE(NULLIF(TRIM(e.EntityType), ''), '') AS EntityType,
                COALESCE(MAX(CASE WHEN UPPER(ea.PropertyName) = 'HWYNUM' THEN TRIM(ea.PropertyValue) END), '') AS HwyNum,
                COALESCE(MAX(CASE WHEN UPPER(ea.PropertyName) = 'BOXNUM' THEN TRIM(ea.PropertyValue) END), '') AS BoxNum,
                COALESCE(MAX(CASE WHEN UPPER(ea.PropertyName) = 'INBOXNM' THEN TRIM(ea.PropertyValue) END), '') AS InBoxNum,
                COALESCE(MAX(CASE WHEN UPPER(ea.PropertyName) = 'OUTBOXNM' THEN TRIM(ea.PropertyValue) END), '') AS OutBoxNum,
                COALESCE(MAX(CASE WHEN UPPER(ea.PropertyName) = 'NODENUM' THEN TRIM(ea.PropertyValue) END), '') AS NodeNum,
                COALESCE(MAX(CASE WHEN UPPER(ea.PropertyName) = 'IOPNUM' THEN TRIM(ea.PropertyValue) END), '') AS IopNum,
                ep.PropertyName,
                ep.PropertyValue
            FROM Entities e
            INNER JOIN EntityProperties pt ON pt.EntityId = e.Id
                AND UPPER(pt.PropertyName) = 'PNTPCTY'
                AND UPPER(TRIM(pt.PropertyValue)) = 'MODICON'
            LEFT JOIN EntityProperties ea ON ea.EntityId = e.Id
            INNER JOIN EntityProperties ep ON ep.EntityId = e.Id
            WHERE UPPER(ep.PropertyName) LIKE 'PCADDRI%'
               OR UPPER(ep.PropertyName) LIKE 'PCADDRO%'
            GROUP BY
                e.Id,
                EntityName,
                SystemEntityHeader,
                EntityType,
                ep.PropertyName,
                ep.PropertyValue;
            """;

        await using var command = new SqliteCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var entityId = reader.GetInt64(0);
            var entityName = reader.GetString(1).Trim();
            var systemEntityHeader = reader.GetString(2).Trim();
            var entityType = reader.GetString(3).Trim();
            var hwyNumRaw = reader.GetString(4).Trim();
            var boxNumRaw = reader.GetString(5).Trim();
            var inBoxNumRaw = reader.GetString(6).Trim();
            var outBoxNumRaw = reader.GetString(7).Trim();
            var nodeNumRaw = reader.GetString(8).Trim();
            var iopNumRaw = reader.GetString(9).Trim();
            var propertyName = reader.GetString(10).Trim().ToUpperInvariant();
            var propertyValue = reader.GetString(11).Trim();

            int? entityHwyNum = null;
            int? entityBoxNum = null;
            if (TryParseDecimalInteger(hwyNumRaw, out var parsedHwyNum))
            {
                entityHwyNum = parsedHwyNum;
            }

            if (TryParseDecimalInteger(boxNumRaw, out var parsedBoxNum))
            {
                entityBoxNum = parsedBoxNum;
            }
            else if (TryParseDecimalInteger(inBoxNumRaw, out var parsedInBoxNum))
            {
                entityBoxNum = parsedInBoxNum;
            }
            else if (TryParseDecimalInteger(outBoxNumRaw, out var parsedOutBoxNum))
            {
                entityBoxNum = parsedOutBoxNum;
            }

            var hasHwyScope = entityHwyNum.HasValue && entityBoxNum.HasValue;
            var matchesEplcgScope = hasHwyScope
                && entityHwyNum.GetValueOrDefault() == hwyNum
                && normalizedHwyBoxes.Contains(entityBoxNum.GetValueOrDefault());

            int? entityNodeNum = null;
            int? entityIopNum = null;
            if (TryParseDecimalInteger(nodeNumRaw, out var parsedNodeNum))
            {
                entityNodeNum = parsedNodeNum;
            }

            if (TryParseDecimalInteger(iopNumRaw, out var parsedIopNum))
            {
                entityIopNum = parsedIopNum;
            }

            var hasSerialScope = entityNodeNum.HasValue && entityIopNum.HasValue;
            var matchesSerialScope = wantsSerialScope
                && serialNodeNumber >= 0
                && serialIoModuleNumber >= 0
                && hasSerialScope
                && entityNodeNum.GetValueOrDefault() == serialNodeNumber
                && entityIopNum.GetValueOrDefault() == serialIoModuleNumber;

            var includeByScope = isSerialOnlyConnection
                ? matchesSerialScope
                : true;
            if (!includeByScope)
            {
                continue;
            }

            var tagName = GetPreferredEntityDisplayName(entityName, systemEntityHeader).Trim();
            if (string.IsNullOrWhiteSpace(tagName))
            {
                continue;
            }

            var tagKey = ExtractConnectionEntity(tagName).ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(tagKey))
            {
                continue;
            }

            if (!TryParseAbDataRegister(propertyValue, out var addressNumber) || addressNumber < 0)
            {
                continue;
            }

            var isHoldingRegisterEntity = entityType.Equals("ANLINHG", StringComparison.OrdinalIgnoreCase)
                || entityType.Equals("ANLOUTHG", StringComparison.OrdinalIgnoreCase)
                || entityType.Equals("ANLOTHG", StringComparison.OrdinalIgnoreCase);
            var isInput = propertyName.StartsWith("PCADDRI", StringComparison.OrdinalIgnoreCase);
            var addressIndexSuffix = ExtractTrailingDigits(propertyName);
            if (string.IsNullOrWhiteSpace(addressIndexSuffix))
            {
                addressIndexSuffix = "1";
            }

            var specifPropertyName = $"{(isInput ? "SPECIFI" : "SPECIFO")}{addressIndexSuffix}";
            int? specifPrefix = null;
            if (specifByEntityAndProperty.TryGetValue((entityId, specifPropertyName), out var specifValue)
                && specifValue is >= 0 and <= 9)
            {
                specifPrefix = specifValue;
            }

            var addressSuffix = addressNumber.ToString("D5", CultureInfo.InvariantCulture);
            var prefixDigit = specifPrefix?.ToString(CultureInfo.InvariantCulture)
                ?? (isHoldingRegisterEntity ? "3" : (isInput ? "1" : "0"));
            var registerAddress = $"{prefixDigit}{addressSuffix}";
            var registerKey = registerAddress.ToUpperInvariant();
            var uniqueKey = $"{registerKey}|{tagKey}";
            if (!seen.Add(uniqueKey))
            {
                continue;
            }

            registerLookup.TryGetValue(addressNumber, out var registerDescription);
            if (string.IsNullOrWhiteSpace(registerDescription)
                && int.TryParse(registerAddress, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fullRegisterNumber))
            {
                registerLookup.TryGetValue(fullRegisterNumber, out registerDescription);
            }

            var serialQualifier = matchesSerialScope
                ? $"SERIAL NODENUM={serialNodeNumber} IOPNUM={serialIoModuleNumber}"
                : (entityHwyNum.HasValue && entityBoxNum.HasValue
                    ? $"EPLCG HWY={entityHwyNum.Value} BOX={entityBoxNum.Value}"
                    : "EPLCG");
            var sourceLine =
                $"MODICON {serialQualifier} {entityType} {propertyName}={propertyValue} {specifPropertyName}={(specifPrefix?.ToString(CultureInfo.InvariantCulture) ?? "--")}"
                + (string.IsNullOrWhiteSpace(registerDescription) ? string.Empty : $" REG={registerDescription}");

            mappings.Add((registerAddress, registerKey, tagName, tagKey, sourceLine));
        }

        return mappings;
    }

    private static Dictionary<int, string> ParseModiconRegisterLookup(string registersFilePath)
    {
        var lookup = new Dictionary<int, string>();
        if (string.IsNullOrWhiteSpace(registersFilePath) || !File.Exists(registersFilePath))
        {
            return lookup;
        }

        try
        {
            foreach (var rawLine in File.ReadLines(registersFilePath))
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                {
                    continue;
                }

                var line = rawLine.Replace("\0", string.Empty, StringComparison.Ordinal);
                var match = Regex.Match(line, @"^\s*(\d{4,7})\s+(.+?)\s*$");
                if (!match.Success)
                {
                    continue;
                }

                if (!int.TryParse(match.Groups[1].Value.TrimStart('0'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var address))
                {
                    if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out address))
                    {
                        continue;
                    }
                }

                var description = match.Groups[2].Value.Trim();
                if (string.IsNullOrWhiteSpace(description))
                {
                    continue;
                }

                if (!lookup.ContainsKey(address))
                {
                    lookup[address] = description;
                }
            }
        }
        catch
        {
            return lookup;
        }

        return lookup;
    }

    private static List<ModiconTrafficCopEntry> ParseModiconTrafficCopEntries(string trafficCopFilePath)
    {
        var entries = new List<ModiconTrafficCopEntry>();
        if (string.IsNullOrWhiteSpace(trafficCopFilePath) || !File.Exists(trafficCopFilePath))
        {
            return entries;
        }

        try
        {
            int? currentDrop = null;
            foreach (var rawLine in File.ReadLines(trafficCopFilePath))
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                {
                    continue;
                }

                var line = rawLine.Replace("\0", string.Empty, StringComparison.Ordinal).TrimEnd();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var dropMatch = Regex.Match(line, @"^\s*Drop\s+(\d+)\s+of\s+\d+\b", RegexOptions.IgnoreCase);
                if (dropMatch.Success)
                {
                    if (int.TryParse(dropMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDrop))
                    {
                        currentDrop = parsedDrop;
                    }

                    continue;
                }

                var slotMatch = Regex.Match(line, @"^\s*(\d{3})\s+(.+)$");
                if (!slotMatch.Success)
                {
                    continue;
                }

                if (!int.TryParse(slotMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var slotNumber))
                {
                    continue;
                }

                var remainder = slotMatch.Groups[2].Value.Trim();
                if (string.IsNullOrWhiteSpace(remainder))
                {
                    continue;
                }

                var fields = Regex
                    .Split(remainder, @"\s{2,}")
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();
                if (fields.Count == 0)
                {
                    continue;
                }

                var moduleText = fields[0];
                var referenceRange = string.Empty;
                var referenceStart = string.Empty;
                var referenceEnd = string.Empty;
                var dataType = string.Empty;
                var description = string.Empty;
                var fieldIndex = 1;

                if (fields.Count > fieldIndex)
                {
                    var rangeMatch = Regex.Match(fields[fieldIndex], @"^(\d{6})-(\d{6})$");
                    if (rangeMatch.Success)
                    {
                        referenceRange = fields[fieldIndex];
                        referenceStart = rangeMatch.Groups[1].Value;
                        referenceEnd = rangeMatch.Groups[2].Value;
                        fieldIndex++;
                    }
                }

                if (fields.Count > fieldIndex)
                {
                    var possibleDataType = fields[fieldIndex];
                    if (Regex.IsMatch(possibleDataType, @"^[A-Z]{2,8}$"))
                    {
                        dataType = possibleDataType;
                        fieldIndex++;
                    }
                }

                if (fields.Count > fieldIndex)
                {
                    description = string.Join(" ", fields.Skip(fieldIndex)).Trim();
                }

                entries.Add(new ModiconTrafficCopEntry
                {
                    DropNumber = currentDrop,
                    SlotNumber = slotNumber,
                    ModuleText = moduleText,
                    ReferenceStart = referenceStart,
                    ReferenceEnd = referenceEnd,
                    ReferenceRange = referenceRange,
                    DataType = dataType,
                    Description = description,
                    SourceLine = line.Trim()
                });
            }
        }
        catch
        {
            return entries;
        }

        return entries;
    }

    private static bool TryParseModiconTrafficCopRange(ModiconTrafficCopEntry entry, out int startAddress, out int endAddress)
    {
        startAddress = 0;
        endAddress = 0;
        if (entry is null)
        {
            return false;
        }

        if (TryParseAbDataRegister(entry.ReferenceStart, out var start)
            && TryParseAbDataRegister(entry.ReferenceEnd, out var end)
            && start > 0
            && end > 0)
        {
            startAddress = start;
            endAddress = end;
            return true;
        }

        var rangeText = entry.ReferenceRange?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rangeText))
        {
            return false;
        }

        var rangeMatch = Regex.Match(rangeText, @"^(\d{4,7})\s*-\s*(\d{4,7})$");
        if (!rangeMatch.Success)
        {
            return false;
        }

        if (!TryParseAbDataRegister(rangeMatch.Groups[1].Value, out start)
            || !TryParseAbDataRegister(rangeMatch.Groups[2].Value, out end)
            || start <= 0
            || end <= 0)
        {
            return false;
        }

        startAddress = start;
        endAddress = end;
        return true;
    }

    private static string FormatTagSummary(IReadOnlyList<string> tags)
    {
        if (tags is null || tags.Count == 0)
        {
            return string.Empty;
        }

        const int maxShown = 3;
        var shown = tags
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxShown)
            .ToList();
        if (shown.Count == 0)
        {
            return string.Empty;
        }

        var remaining = tags
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() - shown.Count;

        var summary = string.Join(" / ", shown);
        if (remaining > 0)
        {
            summary = $"{summary} (+{remaining})";
        }

        return summary;
    }

    private static string ExtractModiconTagFromRegisterDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return string.Empty;
        }

        var text = description
            .Replace("\0", string.Empty, StringComparison.Ordinal)
            .Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        // Common registers.prt format: "<description><2+ spaces><tag>"
        var paddedMatch = Regex.Match(text, @"\s{2,}(?<tag>[A-Z0-9_$-]{3,})\s*$", RegexOptions.IgnoreCase);
        if (paddedMatch.Success)
        {
            var candidate = paddedMatch.Groups["tag"].Value.Trim().ToUpperInvariant();
            if (IsLikelyModiconTag(candidate))
            {
                return candidate;
            }
        }

        var tokens = text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Reverse();
        foreach (var token in tokens)
        {
            var candidate = token.Trim().Trim(',', ';', ':', '.').ToUpperInvariant();
            if (IsLikelyModiconTag(candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    private static string RemoveTrailingRegisterTag(string description, string tag)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return string.Empty;
        }

        var normalized = description
            .Replace("\0", string.Empty, StringComparison.Ordinal)
            .Trim();
        if (string.IsNullOrWhiteSpace(normalized) || string.IsNullOrWhiteSpace(tag))
        {
            return normalized;
        }

        var escapedTag = Regex.Escape(tag.Trim());
        var stripped = Regex.Replace(
            normalized,
            $@"\s{{2,}}{escapedTag}\s*$",
            string.Empty,
            RegexOptions.IgnoreCase).Trim();

        if (!string.IsNullOrWhiteSpace(stripped) && !stripped.Equals(normalized, StringComparison.Ordinal))
        {
            return stripped;
        }

        var fallback = Regex.Replace(
            normalized,
            $@"\s+{escapedTag}\s*$",
            string.Empty,
            RegexOptions.IgnoreCase).Trim();

        return string.IsNullOrWhiteSpace(fallback) ? normalized : fallback;
    }

    private static bool IsLikelyModiconTag(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length < 4)
        {
            return false;
        }

        if (!Regex.IsMatch(token, @"^[A-Z0-9_$-]+$", RegexOptions.IgnoreCase))
        {
            return false;
        }

        var hasLetter = token.Any(char.IsLetter);
        var hasDigit = token.Any(char.IsDigit);
        if (!hasLetter || !hasDigit)
        {
            return false;
        }

        if (Regex.IsMatch(token, @"^\d+V$", RegexOptions.IgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static string ExtractTrailingDigits(string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return string.Empty;
        }

        var match = Regex.Match(propertyName.Trim(), @"(\d+)\s*$");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static List<(string RegisterAddress, string RegisterKey, string TagName, string TagKey, string SourceLine)> ParseAbPlcMappings(string systemFilePath)
    {
        var mappings = new List<(string RegisterAddress, string RegisterKey, string TagName, string TagKey, string SourceLine)>();
        if (string.IsNullOrWhiteSpace(systemFilePath) || !File.Exists(systemFilePath))
        {
            return mappings;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(systemFilePath);
        }
        catch
        {
            return mappings;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in lines)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            var match = PlcArrowMappingRegex.Match(rawLine);
            if (!match.Success)
            {
                continue;
            }

            var left = match.Groups["left"].Value.Trim();
            var right = match.Groups["right"].Value.Trim();
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                continue;
            }

            var leftIsRegister = IsLikelyPlcRegister(left);
            var rightIsRegister = IsLikelyPlcRegister(right);
            if (leftIsRegister == rightIsRegister)
            {
                continue;
            }

            var registerAddress = leftIsRegister ? left : right;
            var tagName = leftIsRegister ? right : left;
            var tagKey = ExtractPlcMappingTagKey(tagName);
            if (string.IsNullOrWhiteSpace(tagKey))
            {
                continue;
            }

            var registerKey = registerAddress.Trim().ToUpperInvariant();
            var uniqueKey = $"{registerKey}|{tagKey}";
            if (!seen.Add(uniqueKey))
            {
                continue;
            }

            mappings.Add((registerAddress.Trim(), registerKey, tagName.Trim(), tagKey, rawLine.Trim()));
        }

        return mappings;
    }

    private static async Task<List<(string RegisterAddress, string RegisterKey, string TagName, string TagKey, string SourceLine)>> ParseEplcgMappingsFromEntitiesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string plc5FileAddress,
        int hwyNum,
        IReadOnlyCollection<int> hwyBoxNumbers)
    {
        var mappings = new List<(string RegisterAddress, string RegisterKey, string TagName, string TagKey, string SourceLine)>();
        if (string.IsNullOrWhiteSpace(plc5FileAddress))
        {
            return mappings;
        }

        var normalizedHwyBoxNumbers = (hwyBoxNumbers ?? [])
            .Where(value => value >= 0)
            .ToHashSet();

        const string sql = """
            SELECT
                COALESCE(NULLIF(TRIM(e.EntityName), ''), '') AS EntityName,
                COALESCE(NULLIF(TRIM(e.SystemEntityHeader), ''), '') AS SystemEntityHeader,
                UPPER(COALESCE(e.EntityType, '')) AS EntityType,
                COALESCE(MAX(CASE WHEN UPPER(ep.PropertyName) = 'HWYNUM' THEN TRIM(ep.PropertyValue) END), '') AS HWYNUM,
                COALESCE(MAX(CASE WHEN UPPER(ep.PropertyName) = 'BOXNUM' THEN TRIM(ep.PropertyValue) END), '') AS BOXNUM,
                COALESCE(MAX(CASE WHEN UPPER(ep.PropertyName) = 'INBOXNM' THEN TRIM(ep.PropertyValue) END), '') AS INBOXNM,
                COALESCE(MAX(CASE WHEN UPPER(ep.PropertyName) = 'OUTBOXNM' THEN TRIM(ep.PropertyValue) END), '') AS OUTBOXNM,
                COALESCE(MAX(CASE WHEN UPPER(ep.PropertyName) = 'PCADDRI1' THEN TRIM(ep.PropertyValue) END), '') AS PCADDRI1,
                COALESCE(MAX(CASE WHEN UPPER(ep.PropertyName) = 'PCBITI1' THEN TRIM(ep.PropertyValue) END), '') AS PCBITI1,
                COALESCE(MAX(CASE WHEN UPPER(ep.PropertyName) = 'PCADDRO1' THEN TRIM(ep.PropertyValue) END), '') AS PCADDRO1,
                COALESCE(MAX(CASE WHEN UPPER(ep.PropertyName) = 'PCBITO1' THEN TRIM(ep.PropertyValue) END), '') AS PCBITO1
            FROM Entities e
            LEFT JOIN EntityProperties ep ON ep.EntityId = e.Id
            WHERE UPPER(COALESCE(e.EntityType, '')) IN ('DIGINHG', 'DIGCMPHG', 'DIGOUTHG', 'ANLINHG', 'ANLOUTHG', 'ANLOTHG')
            GROUP BY e.Id, EntityName, SystemEntityHeader, EntityType;
            """;

        await using var command = new SqliteCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (await reader.ReadAsync())
        {
            var entityName = reader.GetString(0).Trim();
            var systemEntityHeader = reader.GetString(1).Trim();
            var entityType = reader.GetString(2).Trim();
            var hwyNumRaw = reader.GetString(3).Trim();
            var boxNumRaw = reader.GetString(4).Trim();
            var inBoxNumRaw = reader.GetString(5).Trim();
            var outBoxNumRaw = reader.GetString(6).Trim();
            var pcAddrI1 = reader.GetString(7).Trim();
            var pcBitI1 = reader.GetString(8).Trim();
            var pcAddrO1 = reader.GetString(9).Trim();
            var pcBitO1 = reader.GetString(10).Trim();

            if (!TryParseDecimalInteger(hwyNumRaw, out var entityHwyNum)
                || entityHwyNum != hwyNum)
            {
                continue;
            }

            var entityBoxNum = ParseNullableHwyBoxNumber(boxNumRaw);
            var entityInBoxNum = ParseNullableHwyBoxNumber(inBoxNumRaw);
            var entityOutBoxNum = ParseNullableHwyBoxNumber(outBoxNumRaw);
            var matchesInputScope = EplcgPointMatchesImportScope(entityBoxNum, entityInBoxNum, normalizedHwyBoxNumbers);
            var matchesOutputScope = EplcgPointMatchesImportScope(entityBoxNum, entityOutBoxNum, normalizedHwyBoxNumbers);
            if (!matchesInputScope && !matchesOutputScope)
            {
                continue;
            }

            var tagName = GetPreferredEntityDisplayName(entityName, systemEntityHeader).Trim();
            if (string.IsNullOrWhiteSpace(tagName))
            {
                continue;
            }

            var tagKey = ExtractConnectionEntity(tagName).ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(tagKey))
            {
                continue;
            }

            if (entityType.Equals("DIGINHG", StringComparison.OrdinalIgnoreCase)
                || entityType.Equals("DIGCMPHG", StringComparison.OrdinalIgnoreCase))
            {
                if (matchesInputScope)
                {
                    TryAddEplcgMapping(mappings, seen, plc5FileAddress, tagName, tagKey, "I1", pcAddrI1, pcBitI1, entityType);
                }
            }

            if (entityType.Equals("DIGOUTHG", StringComparison.OrdinalIgnoreCase)
                || entityType.Equals("DIGCMPHG", StringComparison.OrdinalIgnoreCase))
            {
                if (matchesOutputScope)
                {
                    TryAddEplcgMapping(mappings, seen, plc5FileAddress, tagName, tagKey, "O1", pcAddrO1, pcBitO1, entityType);
                }
            }

            if (matchesInputScope && entityType.Equals("ANLINHG", StringComparison.OrdinalIgnoreCase))
            {
                TryAddEplcgWordMapping(mappings, seen, plc5FileAddress, tagName, tagKey, "I1", pcAddrI1, entityType);
            }

            if (matchesOutputScope
                && (entityType.Equals("ANLOUTHG", StringComparison.OrdinalIgnoreCase)
                    || entityType.Equals("ANLOTHG", StringComparison.OrdinalIgnoreCase)))
            {
                TryAddEplcgWordMapping(mappings, seen, plc5FileAddress, tagName, tagKey, "O1", pcAddrO1, entityType);
            }
        }

        return mappings;
    }

    private static async Task<List<(string RegisterAddress, string RegisterKey, string TagName, string TagKey, string SourceLine)>> ParseSerialInterfaceMappingsFromEntitiesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string plc5FileAddress,
        int hpmNodeNumber,
        int moduleNumber,
        int? serialDevAddr,
        bool allowAnyModuleForDevAddr = false)
    {
        var mappings = new List<(string RegisterAddress, string RegisterKey, string TagName, string TagKey, string SourceLine)>();
        var useDevAddrFilter = serialDevAddr.HasValue && serialDevAddr.Value >= 0;
        if (hpmNodeNumber < 0 || (moduleNumber < 0 && !useDevAddrFilter))
        {
            return mappings;
        }

        var referencedArrayIndices = await LoadReferencedArrayElementIndicesAsync(connection, transaction);

        const string sql = """
            SELECT
                COALESCE(NULLIF(TRIM(e.EntityName), ''), '') AS EntityName,
                COALESCE(NULLIF(TRIM(e.SystemEntityHeader), ''), '') AS SystemEntityHeader,
                COALESCE(MAX(CASE WHEN UPPER(ep.PropertyName) = 'NODENUM' THEN TRIM(ep.PropertyValue) END), '') AS NodeNum,
                COALESCE(MAX(CASE WHEN UPPER(ep.PropertyName) = 'IOPNUM' THEN TRIM(ep.PropertyValue) END), '') AS IopNum,
                COALESCE(MAX(CASE WHEN UPPER(ep.PropertyName) = 'DEVADDR' THEN TRIM(ep.PropertyValue) END), '') AS DevAddr,
                COALESCE(MAX(CASE WHEN UPPER(ep.PropertyName) IN ('ABDATA2', 'AB_DATA2') THEN TRIM(ep.PropertyValue) END), '') AS AbData2,
                COALESCE(MAX(CASE WHEN UPPER(ep.PropertyName) IN ('ABDATA3', 'AB_DATA3') THEN TRIM(ep.PropertyValue) END), '') AS AbData3,
                COALESCE(MAX(CASE WHEN UPPER(ep.PropertyName) = 'FLSTIX' THEN TRIM(ep.PropertyValue) END), '') AS FlStix,
                COALESCE(MAX(CASE WHEN UPPER(ep.PropertyName) = 'NFLAG' THEN TRIM(ep.PropertyValue) END), '') AS NFlag,
                COALESCE(MAX(CASE WHEN UPPER(ep.PropertyName) = 'NNSTIX' THEN TRIM(ep.PropertyValue) END), '') AS NnStix,
                COALESCE(MAX(CASE WHEN UPPER(ep.PropertyName) = 'NNUMERIC' THEN TRIM(ep.PropertyValue) END), '') AS NNumeric,
                COALESCE(MAX(CASE WHEN UPPER(ep.PropertyName) = 'STRSTIX' THEN TRIM(ep.PropertyValue) END), '') AS StrStix,
                COALESCE(MAX(CASE WHEN UPPER(ep.PropertyName) = 'STRLEN' THEN TRIM(ep.PropertyValue) END), '') AS StrLen,
                COALESCE(MAX(CASE WHEN UPPER(ep.PropertyName) = 'NSTRING' THEN TRIM(ep.PropertyValue) END), '') AS NString,
                COALESCE(MAX(CASE WHEN UPPER(ep.PropertyName) = 'TIMESTIX' THEN TRIM(ep.PropertyValue) END), '') AS TimeStix,
                COALESCE(MAX(CASE WHEN UPPER(ep.PropertyName) = 'NTIME' THEN TRIM(ep.PropertyValue) END), '') AS NTime,
                COALESCE(MAX(CASE WHEN UPPER(ep.PropertyName) = 'FLDESC' THEN TRIM(ep.PropertyValue) END), '') AS FlDesc
            FROM Entities e
            LEFT JOIN EntityProperties ep ON ep.EntityId = e.Id
            WHERE UPPER(COALESCE(e.EntityType, '')) = 'ARRAY'
            GROUP BY e.Id, EntityName, SystemEntityHeader;
            """;

        await using var command = new SqliteCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<SerialArrayRow>();
        while (await reader.ReadAsync())
        {
            rows.Add(new SerialArrayRow
            {
                EntityName = reader.GetString(0).Trim(),
                SystemEntityHeader = reader.GetString(1).Trim(),
                NodeNum = reader.GetString(2).Trim(),
                IopNum = reader.GetString(3).Trim(),
                DevAddr = reader.GetString(4).Trim(),
                AbData2 = reader.GetString(5).Trim(),
                AbData3 = reader.GetString(6).Trim(),
                FlStix = reader.GetString(7).Trim(),
                NFlag = reader.GetString(8).Trim(),
                NnStix = reader.GetString(9).Trim(),
                NNumeric = reader.GetString(10).Trim(),
                StrStix = reader.GetString(11).Trim(),
                StrLen = reader.GetString(12).Trim(),
                NString = reader.GetString(13).Trim(),
                TimeStix = reader.GetString(14).Trim(),
                NTime = reader.GetString(15).Trim(),
                FlDesc = reader.GetString(16).Trim()
            });
        }

        var candidateRows = rows
            .Where(row =>
                TryParseDecimalInteger(row.NodeNum, out var nodeNum)
                && TryParseDecimalInteger(row.IopNum, out var iopNum)
                && nodeNum == hpmNodeNumber
                && (allowAnyModuleForDevAddr || iopNum == moduleNumber)
                && (!useDevAddrFilter
                    || (TryParseIntegerLike(row.DevAddr, out var devAddr) && devAddr == serialDevAddr!.Value)))
            .ToList();

        var usedFallbackCriteria = false;
        if (candidateRows.Count == 0)
        {
            candidateRows = rows
                .Where(row =>
                    row.FlDesc.Contains("SERIAL INTERFACE", StringComparison.OrdinalIgnoreCase)
                    && (!useDevAddrFilter
                        || (TryParseIntegerLike(row.DevAddr, out var devAddr) && devAddr == serialDevAddr!.Value))
                    && TryParseAbDataRegister(row.AbData2, out _))
                .ToList();
            usedFallbackCriteria = candidateRows.Count > 0;
        }

        foreach (var row in candidateRows)
        {
            var entityName = row.EntityName;
            var systemEntityHeader = row.SystemEntityHeader;
            var nodeNumRaw = row.NodeNum;
            var iopNumRaw = row.IopNum;
            var devAddrRaw = row.DevAddr;
            var abData2Raw = row.AbData2;
            var flDescRaw = row.FlDesc;

            var tagName = GetPreferredEntityDisplayName(entityName, systemEntityHeader).Trim();
            var tagKey = ExtractConnectionEntity(tagName).ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(tagName) || string.IsNullOrWhiteSpace(tagKey))
            {
                continue;
            }

            var elementMappingsWritten = 0;
            var hasReferencedIndices = referencedArrayIndices.TryGetValue(tagKey, out var referencedIndices) && referencedIndices.Count > 0;

            elementMappingsWritten += AddSerialArrayLaneMappings(
                mappings,
                seen,
                plc5FileAddress,
                row,
                tagName,
                tagKey,
                "FL",
                row.FlStix,
                row.NFlag,
                isBitLane: true,
                strideWords: 1,
                preferredFilePrefix: "N",
                usedFallbackCriteria);

            elementMappingsWritten += AddSerialArrayLaneMappings(
                mappings,
                seen,
                plc5FileAddress,
                row,
                tagName,
                tagKey,
                "NN",
                row.NnStix,
                row.NNumeric,
                isBitLane: false,
                strideWords: 1,
                preferredFilePrefix: IsFloatingPointSerialArray(row) ? "F" : "N",
                usedFallbackCriteria);

            var stringStrideWords = 1;
            if (TryParseIntegerLike(row.StrLen, out var strLen) && strLen > 0)
            {
                stringStrideWords = Math.Max(1, (int)Math.Ceiling(strLen / 2.0));
            }

            elementMappingsWritten += AddSerialArrayLaneMappings(
                mappings,
                seen,
                plc5FileAddress,
                row,
                tagName,
                tagKey,
                "STR",
                row.StrStix,
                row.NString,
                isBitLane: false,
                strideWords: stringStrideWords,
                preferredFilePrefix: "N",
                usedFallbackCriteria);

            elementMappingsWritten += AddSerialArrayLaneMappings(
                mappings,
                seen,
                plc5FileAddress,
                row,
                tagName,
                tagKey,
                "TIME",
                row.TimeStix,
                row.NTime,
                isBitLane: false,
                strideWords: 1,
                preferredFilePrefix: "N",
                usedFallbackCriteria);

            if (elementMappingsWritten == 0 && hasReferencedIndices)
            {
                if (!TryResolveSerialArrayBaseAddress(
                        plc5FileAddress,
                        abData2Raw,
                        row.FlStix,
                        flDescRaw,
                        "N",
                        out var baseFileAddress,
                        out var baseWord))
                {
                    continue;
                }

                foreach (var elementIndex in referencedIndices!)
                {
                    if (elementIndex <= 0)
                    {
                        continue;
                    }

                    var elementOffset = elementIndex - 1;
                    var plcRegister = baseWord + (elementOffset / 16);
                    var plcBit = elementOffset % 16;
                    if (plcRegister < 0)
                    {
                        continue;
                    }

                    var elementRegisterAddress = $"{baseFileAddress}:{plcRegister}/{plcBit}";
                    var elementRegisterKey = elementRegisterAddress.ToUpperInvariant();
                    var elementUniqueKey = $"{elementRegisterKey}|{tagKey}";
                    if (!seen.Add(elementUniqueKey))
                    {
                        continue;
                    }

                    mappings.Add((
                        elementRegisterAddress,
                        elementRegisterKey,
                        tagName,
                        tagKey,
                        $"SERIAL ARRAY NODENUM={nodeNumRaw} IOPNUM={iopNumRaw} DEVADDR={devAddrRaw} AB_DATA2={abData2Raw} FLSTIX={row.FlStix} FLDESC={flDescRaw} FL({elementIndex}){(usedFallbackCriteria ? " MATCH=FALLBACK" : string.Empty)}"));
                    elementMappingsWritten++;
                }
            }

            if (elementMappingsWritten > 0)
            {
                continue;
            }

            if (!TryResolveSerialArrayBaseAddress(
                    plc5FileAddress,
                    abData2Raw,
                    row.FlStix,
                    flDescRaw,
                    "N",
                    out var fallbackFileAddress,
                    out var fallbackWord))
            {
                continue;
            }

            var registerAddress = $"{fallbackFileAddress}:{fallbackWord}";
            var registerKey = registerAddress.ToUpperInvariant();
            var uniqueKey = $"{registerKey}|{tagKey}";
            if (!seen.Add(uniqueKey))
            {
                continue;
            }

            mappings.Add((
                registerAddress,
                registerKey,
                tagName,
                tagKey,
                $"SERIAL ARRAY NODENUM={nodeNumRaw} IOPNUM={iopNumRaw} DEVADDR={devAddrRaw} AB_DATA2={abData2Raw} FLSTIX={row.FlStix} FLDESC={flDescRaw}{(usedFallbackCriteria ? " MATCH=FALLBACK" : string.Empty)}"));
        }

        return mappings;
    }

    private sealed class SerialArrayRow
    {
        public string EntityName { get; init; } = string.Empty;
        public string SystemEntityHeader { get; init; } = string.Empty;
        public string NodeNum { get; init; } = string.Empty;
        public string IopNum { get; init; } = string.Empty;
        public string DevAddr { get; init; } = string.Empty;
        public string AbData2 { get; init; } = string.Empty;
        public string AbData3 { get; init; } = string.Empty;
        public string FlStix { get; init; } = string.Empty;
        public string NFlag { get; init; } = string.Empty;
        public string NnStix { get; init; } = string.Empty;
        public string NNumeric { get; init; } = string.Empty;
        public string StrStix { get; init; } = string.Empty;
        public string StrLen { get; init; } = string.Empty;
        public string NString { get; init; } = string.Empty;
        public string TimeStix { get; init; } = string.Empty;
        public string NTime { get; init; } = string.Empty;
        public string FlDesc { get; init; } = string.Empty;
    }

    private static bool IsFloatingPointSerialArray(SerialArrayRow row) =>
        TryParseIntegerLike(row.AbData3, out var abData3)
        && (abData3 == 6 || abData3 == 8);

    private static int AddSerialArrayLaneMappings(
        List<(string RegisterAddress, string RegisterKey, string TagName, string TagKey, string SourceLine)> mappings,
        HashSet<string> seen,
        string plc5FileAddress,
        SerialArrayRow row,
        string tagName,
        string tagKey,
        string laneName,
        string startIndexRaw,
        string countRaw,
        bool isBitLane,
        int strideWords,
        string preferredFilePrefix,
        bool usedFallbackCriteria)
    {
        if (!TryParseIntegerLike(countRaw, out var count) || count <= 0)
        {
            return 0;
        }

        if (!TryResolveSerialArrayBaseAddress(
                plc5FileAddress,
                row.AbData2,
                startIndexRaw,
                row.FlDesc,
                preferredFilePrefix,
                out var baseFileAddress,
                out var baseWord))
        {
            return 0;
        }

        var written = 0;
        var maxElements = Math.Min(count, 4096);
        var normalizedStrideWords = Math.Max(1, strideWords);
        for (var elementIndex = 1; elementIndex <= maxElements; elementIndex++)
        {
            var elementOffset = elementIndex - 1;
            string elementRegisterAddress;
            if (isBitLane)
            {
                var plcRegister = baseWord + (elementOffset / 16);
                var plcBit = elementOffset % 16;
                if (plcRegister < 0)
                {
                    continue;
                }

                elementRegisterAddress = $"{baseFileAddress}:{plcRegister}/{plcBit}";
            }
            else
            {
                var plcRegister = baseWord + (elementOffset * normalizedStrideWords);
                if (plcRegister < 0)
                {
                    continue;
                }

                elementRegisterAddress = $"{baseFileAddress}:{plcRegister}";
            }

            var elementRegisterKey = elementRegisterAddress.ToUpperInvariant();
            var elementUniqueKey = $"{elementRegisterKey}|{tagKey}|{laneName}";
            if (!seen.Add(elementUniqueKey))
            {
                continue;
            }

            mappings.Add((
                elementRegisterAddress,
                elementRegisterKey,
                tagName,
                tagKey,
                $"SERIAL ARRAY NODENUM={row.NodeNum} IOPNUM={row.IopNum} DEVADDR={row.DevAddr} AB_DATA2={row.AbData2} AB_DATA3={row.AbData3} FLSTIX={row.FlStix} NFLAG={row.NFlag} NNSTIX={row.NnStix} NNUMERIC={row.NNumeric} STRSTIX={row.StrStix} STRLEN={row.StrLen} NSTRING={row.NString} TIMESTIX={row.TimeStix} NTIME={row.NTime} FLDESC={row.FlDesc} {laneName}({elementIndex}){(usedFallbackCriteria ? " MATCH=FALLBACK" : string.Empty)}"));
            written++;
        }

        return written;
    }

    private static bool TryResolveSerialArrayBaseAddress(
        string plc5FileAddress,
        string abData2Raw,
        string flStixRaw,
        string flDescRaw,
        string preferredFilePrefix,
        out string baseFileAddress,
        out int baseWord)
    {
        baseFileAddress = string.Empty;
        baseWord = 0;

        if (TryParseSerialArrayBaseFromDescription(flDescRaw, out baseFileAddress, out baseWord))
        {
            return true;
        }

        if (TryParseAbDataRegister(abData2Raw, out var fileNumber) && fileNumber >= 0)
        {
            var startWord = 1;
            if (TryParseAbDataRegister(flStixRaw, out var parsedStartWord) && parsedStartWord >= 0)
            {
                startWord = parsedStartWord;
            }

            var filePrefix = string.IsNullOrWhiteSpace(preferredFilePrefix)
                ? "N"
                : preferredFilePrefix.Trim().ToUpperInvariant();
            baseFileAddress = $"{filePrefix}{fileNumber}";
            baseWord = startWord;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(plc5FileAddress))
        {
            var normalizedFile = NormalizePlcFileAddress(plc5FileAddress);
            if (!string.IsNullOrWhiteSpace(normalizedFile))
            {
                baseFileAddress = normalizedFile;
                baseWord = 0;
                return true;
            }
        }

        return false;
    }

    private static bool TryParseSerialArrayBaseFromDescription(string raw, out string plcFileAddress, out int registerIndex)
    {
        plcFileAddress = string.Empty;
        registerIndex = 0;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var match = Regex.Match(raw, @"(?i)\b([A-Z]\d+)\s*:\s*(\d+)(?:\s*/\s*\d+)?\b");
        if (!match.Success)
        {
            return false;
        }

        var normalizedFileAddress = NormalizePlcFileAddress(match.Groups[1].Value);
        if (string.IsNullOrWhiteSpace(normalizedFileAddress))
        {
            return false;
        }

        if (!int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out registerIndex)
            || registerIndex < 0)
        {
            return false;
        }

        plcFileAddress = normalizedFileAddress;
        return true;
    }

    private static async Task<Dictionary<string, SortedSet<int>>> LoadReferencedArrayElementIndicesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        const string sql = """
            SELECT COALESCE(TRIM(PropertyValue), '')
            FROM EntityProperties
            WHERE TRIM(COALESCE(PropertyValue, '')) <> '';
            """;

        await using var command = new SqliteCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new Dictionary<string, SortedSet<int>>(StringComparer.OrdinalIgnoreCase);

        while (await reader.ReadAsync())
        {
            var raw = reader.GetString(0);
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            foreach (Match match in Regex.Matches(raw, @"(?i)\b([A-Z][A-Z0-9_]*)\s*\.\s*FL\s*\(\s*(\d+)\s*\)"))
            {
                if (!match.Success || match.Groups.Count < 3)
                {
                    continue;
                }

                var tagKey = match.Groups[1].Value.Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(tagKey))
                {
                    continue;
                }

                if (!int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) || index <= 0)
                {
                    continue;
                }

                if (!result.TryGetValue(tagKey, out var indices))
                {
                    indices = new SortedSet<int>();
                    result[tagKey] = indices;
                }

                indices.Add(index);
            }
        }

        return result;
    }

    private static void TryAddEplcgMapping(
        List<(string RegisterAddress, string RegisterKey, string TagName, string TagKey, string SourceLine)> mappings,
        HashSet<string> seen,
        string plc5FileAddress,
        string tagName,
        string tagKey,
        string ioLabel,
        string octalAddressRaw,
        string octalBitRaw,
        string entityType)
    {
        if (!TryParseHoneywellNumeric(octalAddressRaw, out var decimalAddress)
            || !TryParseHoneywellNumeric(octalBitRaw, out var baseBit))
        {
            return;
        }

        var offsetBit = baseBit + 1;
        var plcBit = offsetBit > 15 && baseBit >= 0 && baseBit <= 15 ? baseBit : offsetBit;
        if (plcBit < 0)
        {
            return;
        }

        var registerAddress = $"{plc5FileAddress}:{decimalAddress}/{plcBit}";
        var registerKey = registerAddress.ToUpperInvariant();
        var uniqueKey = $"{registerKey}|{tagKey}";
        if (!seen.Add(uniqueKey))
        {
            return;
        }

        var sourceLine = $"EPLCG {entityType} PCADDR{ioLabel}={octalAddressRaw} PCBIT{ioLabel}={octalBitRaw}";
        mappings.Add((registerAddress, registerKey, tagName, tagKey, sourceLine));
    }

    private static bool EplcgPointMatchesImportScope(
        int? entityBoxNum,
        int? directionalBoxNum,
        IReadOnlySet<int> importedBoxes)
    {
        if (importedBoxes.Count == 0)
        {
            return true;
        }

        if (directionalBoxNum.HasValue)
        {
            return importedBoxes.Contains(directionalBoxNum.Value);
        }

        return entityBoxNum.HasValue && importedBoxes.Contains(entityBoxNum.Value);
    }

    private static void TryAddEplcgWordMapping(
        List<(string RegisterAddress, string RegisterKey, string TagName, string TagKey, string SourceLine)> mappings,
        HashSet<string> seen,
        string plc5FileAddress,
        string tagName,
        string tagKey,
        string ioLabel,
        string addressRaw,
        string entityType)
    {
        if (!TryParseHoneywellNumeric(addressRaw, out var decimalAddress) || decimalAddress < 0)
        {
            return;
        }

        var registerAddress = $"{plc5FileAddress}:{decimalAddress}";
        var registerKey = registerAddress.ToUpperInvariant();
        var uniqueKey = $"{registerKey}|{tagKey}";
        if (!seen.Add(uniqueKey))
        {
            return;
        }

        var sourceLine = $"EPLCG {entityType} PCADDR{ioLabel}={addressRaw}";
        mappings.Add((registerAddress, registerKey, tagName, tagKey, sourceLine));
    }

    private static bool TryParseHoneywellNumeric(string raw, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var token = raw.Trim();
        if (Regex.IsMatch(token, "^[0-7]+$"))
        {
            try
            {
                value = Convert.ToInt32(token, 8);
                return true;
            }
            catch
            {
                return false;
            }
        }

        return int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseAbDataRegister(string raw, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var token = raw.Trim();
        if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDouble))
        {
            value = (int)Math.Round(parsedDouble, MidpointRounding.AwayFromZero);
            return value >= 0;
        }

        return TryParseHoneywellNumeric(token, out value) && value >= 0;
    }

    private static bool TryParseDecimalInteger(string raw, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        return int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseIntegerLike(string raw, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var token = raw.Trim();
        if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDouble))
        {
            value = (int)Math.Round(parsedDouble, MidpointRounding.AwayFromZero);
            return true;
        }

        return false;
    }

    private static void AppendMappings(
        List<(string RegisterAddress, string RegisterKey, string TagName, string TagKey, string SourceLine)> mappings,
        IReadOnlyList<(string RegisterAddress, string RegisterKey, string TagName, string TagKey, string SourceLine)> additionalMappings)
    {
        if (additionalMappings.Count == 0)
        {
            return;
        }

        var seen = new HashSet<string>(
            mappings.Select(x => $"{x.RegisterKey}|{x.TagKey}"),
            StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in additionalMappings)
        {
            var uniqueKey = $"{mapping.RegisterKey}|{mapping.TagKey}";
            if (seen.Add(uniqueKey))
            {
                mappings.Add(mapping);
            }
        }
    }

    private static bool IsLikelyPlcRegister(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().ToUpperInvariant();
        return normalized.Contains(':')
               && normalized.Length >= 4
               && char.IsLetter(normalized[0])
               && normalized.Skip(1).Any(char.IsDigit);
    }

    private static IEnumerable<(int LineNumber, string DeclarationText, IReadOnlyList<string> Tokens)> ParseExternalDeclarations(
        string fileText)
    {
        if (string.IsNullOrWhiteSpace(fileText))
        {
            yield break;
        }

        var lines = fileText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var trimmedStart = line.TrimStart();
            if (trimmedStart.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var match = ExternalDeclarationRegex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var firstTokens = ParseExternalTokens(match.Groups[1].Value);
            if (firstTokens.Count > 0)
            {
                yield return (lineIndex + 1, line.TrimEnd(), firstTokens);
            }

            var nextLine = lineIndex + 1;
            while (nextLine < lines.Length)
            {
                var continuationRaw = lines[nextLine];
                if (string.IsNullOrWhiteSpace(continuationRaw))
                {
                    break;
                }

                var continuationTrimmed = continuationRaw.TrimStart();
                if (continuationTrimmed.StartsWith("--", StringComparison.Ordinal))
                {
                    nextLine++;
                    continue;
                }

                if (!continuationTrimmed.StartsWith("&", StringComparison.Ordinal))
                {
                    break;
                }

                var continuationPayload = continuationTrimmed[1..];
                var continuationTokens = ParseExternalTokens(continuationPayload);
                if (continuationTokens.Count > 0)
                {
                    yield return (nextLine + 1, continuationRaw.TrimEnd(), continuationTokens);
                }

                nextLine++;
            }

            lineIndex = nextLine - 1;
        }
    }

    private static string NormalizeClExternalToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        var normalized = token.Trim();
        normalized = normalized.Trim(',', ';', ':', '&');
        normalized = normalized.Trim('(', ')', '[', ']', '{', '}');
        return normalized;
    }

    private static List<string> ParseExternalTokens(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        var effectivePayload = payload;
        var commentIndex = effectivePayload.IndexOf("--", StringComparison.Ordinal);
        if (commentIndex >= 0)
        {
            effectivePayload = effectivePayload[..commentIndex];
        }

        var tokens = new List<string>();
        foreach (var commaSegment in effectivePayload.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = commaSegment.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var token = NormalizeClExternalToken(part);
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                tokens.Add(token.ToUpperInvariant());
            }
        }

        return tokens;
    }

    private static Dictionary<string, List<int>> ParseSetTargetLines(string fileText)
    {
        var targets = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(fileText))
        {
            return targets;
        }

        var lines = fileText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var trimmedStart = line.TrimStart();
            if (trimmedStart.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var codeOnly = StripClInlineComment(line);
            if (string.IsNullOrWhiteSpace(codeOnly))
            {
                continue;
            }

            var matches = SetAssignmentRegex.Matches(codeOnly);
            if (matches.Count == 0)
            {
                continue;
            }

            foreach (Match match in matches)
            {
                if (!match.Success)
                {
                    continue;
                }

                var target = NormalizeSetTargetToken(match.Groups[1].Value);
                if (string.IsNullOrWhiteSpace(target))
                {
                    continue;
                }

                if (!targets.TryGetValue(target, out var rows))
                {
                    rows = [];
                    targets[target] = rows;
                }

                rows.Add(lineIndex + 1);
            }
        }

        return targets;
    }

    private static string StripClInlineComment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var commentIndex = value.IndexOf("--", StringComparison.Ordinal);
        var code = commentIndex >= 0 ? value[..commentIndex] : value;
        return code.TrimEnd();
    }

    private static IEnumerable<(int LineNumber, string Tag, string ReferenceKind, string StatementText)> ParseClLogicUsageTagReferences(
        string fileText,
        HashSet<string> tagSet)
    {
        if (string.IsNullOrWhiteSpace(fileText) || tagSet.Count == 0)
        {
            yield break;
        }

        var lines = fileText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var rawLine = lines[lineIndex];
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            var trimmedStart = rawLine.TrimStart();
            if (trimmedStart.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var codeOnly = StripClInlineComment(rawLine);
            if (string.IsNullOrWhiteSpace(codeOnly))
            {
                continue;
            }

            var referenceKind = GetClLogicReferenceKind(codeOnly);
            if (string.IsNullOrWhiteSpace(referenceKind))
            {
                continue;
            }

            var matchedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in ClIdentifierRegex.Matches(codeOnly))
            {
                if (!match.Success)
                {
                    continue;
                }

                var candidate = match.Value.Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(candidate) || !tagSet.Contains(candidate))
                {
                    continue;
                }

                matchedTags.Add(candidate);
            }

            if (matchedTags.Count == 0)
            {
                continue;
            }

            var statementText = rawLine.TrimEnd();
            foreach (var tag in matchedTags)
            {
                yield return (lineIndex + 1, tag, referenceKind, statementText);
            }
        }
    }

    private static string GetClLogicReferenceKind(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }

        if (ClCallRegex.IsMatch(code))
        {
            return "CALL";
        }

        if (ClIfRegex.IsMatch(code))
        {
            return "IF";
        }

        if (ClElseRegex.IsMatch(code))
        {
            return "ELSE";
        }

        if (ClTagConditionRegex.IsMatch(code))
        {
            return "CONDITION";
        }

        return string.Empty;
    }

    private static string NormalizeSetTargetToken(string value)
    {
        var normalized = NormalizeClExternalToken(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var dotIndex = normalized.IndexOf('.');
        if (dotIndex > 0)
        {
            normalized = normalized[..dotIndex];
        }

        normalized = normalized.Trim();
        if (normalized.StartsWith("$", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return normalized.ToUpperInvariant();
    }

    private static async Task<Dictionary<string, long>> GetLogicLookupAsync(SqliteConnection connection)
    {
        const string sql = """
            SELECT Id, UPPER(TRIM(COALESCE(EntityName, '')))
            FROM Entities
            WHERE UPPER(COALESCE(EntityType, '')) = 'LOGICNIM';
            """;

        await using var command = new SqliteCommand(sql, connection);
        var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var id = reader.GetInt64(0);
            var key = reader.GetString(1);
            if (!string.IsNullOrWhiteSpace(key))
            {
                map[key] = id;
            }
        }

        return map;
    }

    private static string? TryGetLogicLinkToken(string propertyValue, Dictionary<string, long> logicLookup)
    {
        if (string.IsNullOrWhiteSpace(propertyValue))
        {
            return null;
        }

        var candidate = propertyValue.Trim();
        var dot = candidate.IndexOf('.');
        if (dot > 0)
        {
            candidate = candidate[..dot];
        }

        if (logicLookup.TryGetValue(candidate.ToUpperInvariant(), out var id))
        {
            return $"LB:{id}";
        }

        return null;
    }

    private async Task<HardwareLinkTarget?> ResolveHardwareLinkTargetAsync(
        SqliteConnection connection,
        long entityId,
        string entityType,
        string entityName,
        IReadOnlyDictionary<string, string> properties)
    {
        var moduleNumber = ParseNullableInt(GetPropertyValue(properties, "MODNUM"));
        if (moduleNumber.GetValueOrDefault() <= 0)
        {
            moduleNumber = null;
        }

        if (entityType.Equals("BOX", StringComparison.OrdinalIgnoreCase)
            || entityType.Equals("PM_BOX", StringComparison.OrdinalIgnoreCase))
        {
            return new HardwareLinkTarget
            {
                BoxEntityId = entityId,
                BoxName = entityName,
                ModuleNumber = moduleNumber,
                PointEntityId = entityId
            };
        }

        var ntwkNum = ParseNullableInt(GetPropertyValue(properties, "NTWKNUM"));
        var nodeNum = ParseNullableInt(GetPropertyValue(properties, "NODENUM"));
        if (ntwkNum.HasValue && nodeNum.HasValue)
        {
            var pmTarget = await FindHardwareBoxByNetworkNodeAsync(connection, ntwkNum.Value, nodeNum.Value, moduleNumber, entityId);
            if (pmTarget is not null)
            {
                return pmTarget;
            }
        }

        var hwyNum = ParseNullableInt(GetPropertyValue(properties, "HWYNUM"));
        var boxNum = ParseNullableHwyBoxNumber(GetPropertyValue(properties, "BOXNUM"));
        if (hwyNum.HasValue && boxNum.HasValue)
        {
            return await FindHardwareBoxByHighwayBoxAsync(connection, hwyNum.Value, boxNum.Value, moduleNumber, entityId);
        }

        return null;
    }

    private static async Task<HardwareLinkTarget?> FindHardwareBoxByNetworkNodeAsync(
        SqliteConnection connection,
        int networkNumber,
        int nodeNumber,
        int? moduleNumber,
        long pointEntityId)
    {
        const string sql = """
            SELECT
                e.Id,
                COALESCE(NULLIF(TRIM(e.EntityName), ''), '') AS EntityName,
                COALESCE(NULLIF(TRIM(e.SystemEntityHeader), ''), '') AS SystemEntityHeader
            FROM Entities e
            INNER JOIN ImportedFiles f ON f.Id = e.FileId
            INNER JOIN EntityProperties pNtwk ON pNtwk.EntityId = e.Id AND UPPER(pNtwk.PropertyName) = 'NTWKNUM'
            INNER JOIN EntityProperties pNode ON pNode.EntityId = e.Id AND UPPER(pNode.PropertyName) = 'NODENUM'
            WHERE UPPER(COALESCE(f.FileName, '')) LIKE '%BOX.EB'
              AND UPPER(COALESCE(e.EntityType, '')) IN ('PM_BOX', 'BOX')
              AND CAST(TRIM(COALESCE(pNtwk.PropertyValue, '0')) AS INTEGER) = $ntwkNum
              AND CAST(TRIM(COALESCE(pNode.PropertyValue, '0')) AS INTEGER) = $nodeNum
            ORDER BY
                CASE WHEN UPPER(COALESCE(e.EntityType, '')) = 'PM_BOX' THEN 0 ELSE 1 END,
                e.Id
            LIMIT 1;
            """;

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("$ntwkNum", networkNumber);
        command.Parameters.AddWithValue("$nodeNum", nodeNumber);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var entityName = reader.GetString(1).Trim();
        var systemEntityHeader = reader.GetString(2).Trim();

        return new HardwareLinkTarget
        {
            BoxEntityId = reader.GetInt64(0),
            BoxName = GetPreferredEntityDisplayName(entityName, systemEntityHeader),
            ModuleNumber = moduleNumber,
            PointEntityId = pointEntityId
        };
    }

    private static async Task<HardwareLinkTarget?> FindHardwareBoxByHighwayBoxAsync(
        SqliteConnection connection,
        int highwayNumber,
        int boxNumber,
        int? moduleNumber,
        long pointEntityId)
    {
        const string sql = """
            SELECT
                e.Id,
                COALESCE(NULLIF(TRIM(e.EntityName), ''), '') AS EntityName,
                COALESCE(NULLIF(TRIM(e.SystemEntityHeader), ''), '') AS SystemEntityHeader
            FROM Entities e
            INNER JOIN ImportedFiles f ON f.Id = e.FileId
            INNER JOIN EntityProperties pHwy ON pHwy.EntityId = e.Id AND UPPER(pHwy.PropertyName) = 'HWYNUM'
            INNER JOIN EntityProperties pBox ON pBox.EntityId = e.Id AND UPPER(pBox.PropertyName) = 'BOXNUM'
            WHERE UPPER(COALESCE(f.FileName, '')) LIKE '%BOX.EB'
              AND UPPER(COALESCE(e.EntityType, '')) = 'BOX'
              AND CAST(TRIM(COALESCE(pHwy.PropertyValue, '0')) AS INTEGER) = $hwyNum
              AND CAST(
                    CASE
                        WHEN UPPER(TRIM(COALESCE(pBox.PropertyValue, ''))) LIKE 'BOX%' THEN TRIM(SUBSTR(TRIM(COALESCE(pBox.PropertyValue, '')), 4))
                        WHEN UPPER(TRIM(COALESCE(pBox.PropertyValue, ''))) LIKE 'B%' THEN TRIM(SUBSTR(TRIM(COALESCE(pBox.PropertyValue, '')), 2))
                        ELSE TRIM(COALESCE(pBox.PropertyValue, ''))
                    END
                  AS INTEGER) = $boxNum
            ORDER BY e.Id
            LIMIT 1;
            """;

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("$hwyNum", highwayNumber);
        command.Parameters.AddWithValue("$boxNum", boxNumber);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var entityName = reader.GetString(1).Trim();
        var systemEntityHeader = reader.GetString(2).Trim();

        return new HardwareLinkTarget
        {
            BoxEntityId = reader.GetInt64(0),
            BoxName = GetPreferredEntityDisplayName(entityName, systemEntityHeader),
            ModuleNumber = moduleNumber,
            PointEntityId = pointEntityId
        };
    }

    private static string BuildHardwareLinkToken(HardwareLinkTarget target)
    {
        if (target.PointEntityId is long pointEntityId && pointEntityId > 0)
        {
            if (target.ModuleNumber is int moduleNumber && moduleNumber > 0)
            {
                return $"HW:{target.BoxEntityId}:{moduleNumber}:{pointEntityId}";
            }

            return $"HW:{target.BoxEntityId}:0:{pointEntityId}";
        }

        if (target.ModuleNumber is int moduleOnly && moduleOnly > 0)
        {
            return $"HW:{target.BoxEntityId}:{moduleOnly}";
        }

        return $"HW:{target.BoxEntityId}";
    }

    private static async Task<Dictionary<string, string>> GetPropertyDictionaryAsync(SqliteConnection connection, long entityId)
    {
        const string sql = """
            SELECT PropertyName, PropertyValue
            FROM EntityProperties
            WHERE EntityId = $entityId
            ORDER BY SortOrder;
            """;

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("$entityId", entityId);

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            map[reader.GetString(0)] = reader.GetString(1).Trim();
        }

        return map;
    }

    private static async Task PopulatePlcAddressesForHardwarePointsAsync(
        SqliteConnection connection,
        List<HardwarePointSummary> points)
    {
        if (points.Count == 0)
        {
            return;
        }

        var pointKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var point in points)
        {
            var key = NormalizeHardwarePointTagKey(point.Name);
            if (string.IsNullOrWhiteSpace(key))
            {
                key = NormalizeHardwarePointTagKey(point.TagName);
            }

            if (!string.IsNullOrWhiteSpace(key))
            {
                pointKeys.Add(key);
            }
        }

        var modbusByTagKey = await QueryModiconAddressesByTagKeyAsync(
            connection,
            pointKeys.ToList());
        var abByTagKey = await QueryAbAddressesByTagKeyAsync(
            connection,
            pointKeys.ToList());
        var directAddressesByEntityId = await QueryDirectPointAddressesByEntityIdAsync(
            connection,
            points
                .Where(x => x.EntityId > 0)
                .Select(x => x.EntityId)
                .Distinct()
                .ToList());
        var specifByEntityId = await QuerySpecifPrefixesByEntityIdAsync(
            connection,
            points
                .Where(x => x.EntityId > 0)
                .Select(x => x.EntityId)
                .Distinct()
                .ToList());
        var protocolTypeByEntityId = await QueryProtocolTypeByEntityIdAsync(
            connection,
            points
                .Where(x => x.EntityId > 0)
                .Select(x => x.EntityId)
                .Distinct()
                .ToList());

        foreach (var point in points)
        {
            var lookupKeys = new List<string>();
            var nameKey = NormalizeHardwarePointTagKey(point.Name);
            if (!string.IsNullOrWhiteSpace(nameKey))
            {
                lookupKeys.Add(nameKey);
            }

            var tagKey = NormalizeHardwarePointTagKey(point.TagName);
            if (!string.IsNullOrWhiteSpace(tagKey)
                && !lookupKeys.Contains(tagKey, StringComparer.OrdinalIgnoreCase))
            {
                lookupKeys.Add(tagKey);
            }

            var mergedModbusIn = new List<string>();
            var mergedModbusOut = new List<string>();
            var mergedAbIn = new List<string>();
            var mergedAbOut = new List<string>();

            foreach (var key in lookupKeys)
            {
                if (modbusByTagKey.TryGetValue(key, out var modbusAddresses))
                {
                    foreach (var address in modbusAddresses.ModbusIn)
                    {
                        if (!mergedModbusIn.Contains(address, StringComparer.OrdinalIgnoreCase))
                        {
                            mergedModbusIn.Add(address);
                        }
                    }

                    foreach (var address in modbusAddresses.ModbusOut)
                    {
                        if (!mergedModbusOut.Contains(address, StringComparer.OrdinalIgnoreCase))
                        {
                            mergedModbusOut.Add(address);
                        }
                    }
                }

                if (abByTagKey.TryGetValue(key, out var abAddresses))
                {
                    foreach (var address in abAddresses.AbIn)
                    {
                        if (!mergedAbIn.Contains(address, StringComparer.OrdinalIgnoreCase))
                        {
                            mergedAbIn.Add(address);
                        }
                    }

                    foreach (var address in abAddresses.AbOut)
                    {
                        if (!mergedAbOut.Contains(address, StringComparer.OrdinalIgnoreCase))
                        {
                            mergedAbOut.Add(address);
                        }
                    }
                }
            }

            if (point.EntityId > 0 && directAddressesByEntityId.TryGetValue(point.EntityId, out var direct))
            {
                foreach (var address in direct.ModbusIn)
                {
                    if (!mergedModbusIn.Contains(address, StringComparer.OrdinalIgnoreCase))
                    {
                        mergedModbusIn.Add(address);
                    }
                }

                foreach (var address in direct.ModbusOut)
                {
                    if (!mergedModbusOut.Contains(address, StringComparer.OrdinalIgnoreCase))
                    {
                        mergedModbusOut.Add(address);
                    }
                }

                foreach (var address in direct.AbIn)
                {
                    if (!mergedAbIn.Contains(address, StringComparer.OrdinalIgnoreCase))
                    {
                        mergedAbIn.Add(address);
                    }
                }

                foreach (var address in direct.AbOut)
                {
                    if (!mergedAbOut.Contains(address, StringComparer.OrdinalIgnoreCase))
                    {
                        mergedAbOut.Add(address);
                    }
                }
            }

            mergedModbusIn = NormalizeDistinctModbusAddresses(mergedModbusIn);
            mergedModbusOut = NormalizeDistinctModbusAddresses(mergedModbusOut);
            mergedAbIn = NormalizeDistinctModbusAddresses(mergedAbIn);
            mergedAbOut = NormalizeDistinctModbusAddresses(mergedAbOut);
            mergedModbusOut = NormalizeEntitySpecificModbusOutAddresses(
                point.EntityId,
                point.EntityType,
                mergedModbusOut,
                specifByEntityId);

            var protocolType = point.EntityId > 0 && protocolTypeByEntityId.TryGetValue(point.EntityId, out var resolvedProtocol)
                ? resolvedProtocol
                : string.Empty;
            var isAllenBradleyPoint = protocolType.Equals("ALLENBRD", StringComparison.OrdinalIgnoreCase);
            if (isAllenBradleyPoint || mergedAbIn.Count > 0 || mergedAbOut.Count > 0)
            {
                mergedModbusIn.Clear();
                mergedModbusOut.Clear();
            }

            point.ModbusAddressIn = mergedModbusIn.Count == 0 ? string.Empty : string.Join(", ", mergedModbusIn);
            point.ModbusAddressOut = mergedModbusOut.Count == 0 ? string.Empty : string.Join(", ", mergedModbusOut);
            point.AbAddressIn = mergedAbIn.Count == 0 ? string.Empty : string.Join(", ", mergedAbIn);
            point.AbAddressOut = mergedAbOut.Count == 0 ? string.Empty : string.Join(", ", mergedAbOut);
            point.AbAddress = string.Join(", ",
                mergedAbIn
                    .Concat(mergedAbOut)
                    .Distinct(StringComparer.OrdinalIgnoreCase));
        }
    }

    private static List<string> NormalizeEntitySpecificModbusOutAddresses(
        long entityId,
        string entityType,
        List<string> addresses,
        IReadOnlyDictionary<long, (int? SpecifIn, int? SpecifOut)> specifByEntityId)
    {
        if (!entityType.Equals("ANLCMPHG", StringComparison.OrdinalIgnoreCase) || addresses.Count == 0)
        {
            return addresses;
        }

        var prefixDigit = 4;
        if (specifByEntityId.TryGetValue(entityId, out var specif)
            && specif.SpecifOut is >= 0 and <= 9)
        {
            prefixDigit = specif.SpecifOut.Value;
        }

        var normalized = new List<string>(addresses.Count);
        foreach (var token in addresses)
        {
            if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericToken))
            {
                normalized.Add(token);
                continue;
            }

            // ANLCMPHG outputs should use SPECIFO1 as the leading register-family digit.
            if (token.Length <= 5 && numericToken >= 0)
            {
                normalized.Add($"{prefixDigit}{numericToken:D5}");
                continue;
            }

            normalized.Add(token);
        }

        return NormalizeDistinctModbusAddresses(normalized);
    }

    private static List<string> NormalizeDistinctModbusAddresses(IEnumerable<string> addresses)
    {
        var cleaned = addresses
            .Select(x => (x ?? string.Empty).Trim().ToUpperInvariant())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // If a chart-style 6-digit register exists (e.g., 100555 / 000832),
        // suppress the short raw form (e.g., 555 / 832) from direct properties.
        var suppressed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var longNumeric = cleaned
            .Where(x => x.Length >= 6 && x.All(char.IsDigit))
            .ToList();
        foreach (var token in cleaned)
        {
            if (token.Length > 5 || !token.All(char.IsDigit))
            {
                continue;
            }

            var padded = token.PadLeft(5, '0');
            if (longNumeric.Any(x => x.EndsWith(padded, StringComparison.OrdinalIgnoreCase)))
            {
                suppressed.Add(token);
            }
        }

        var remaining = cleaned
            .Where(x => !suppressed.Contains(x))
            .ToList();

        // Collapse equivalent numeric tokens (001122 vs 1122), preferring chart-style width.
        var byKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in remaining)
        {
            var key = int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? $"N:{parsed}"
                : $"T:{token}";

            if (!byKey.TryGetValue(key, out var existing))
            {
                byKey[key] = token;
                continue;
            }

            if (token.Length > existing.Length
                || (token.Length == existing.Length && string.Compare(token, existing, StringComparison.OrdinalIgnoreCase) < 0))
            {
                byKey[key] = token;
            }
        }

        return byKey.Values
            .OrderBy(x => int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : int.MaxValue)
            .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<Dictionary<long, (int? SpecifIn, int? SpecifOut)>> QuerySpecifPrefixesByEntityIdAsync(
        SqliteConnection connection,
        IReadOnlyList<long> entityIds)
    {
        var results = new Dictionary<long, (int? SpecifIn, int? SpecifOut)>();
        if (entityIds.Count == 0)
        {
            return results;
        }

        const int batchSize = 300;
        for (var offset = 0; offset < entityIds.Count; offset += batchSize)
        {
            var batch = entityIds.Skip(offset).Take(batchSize).ToList();
            if (batch.Count == 0)
            {
                continue;
            }

            var placeholders = batch.Select((_, index) => $"$id{index}").ToList();
            var sql = $"""
                SELECT
                    ep.EntityId,
                    UPPER(TRIM(COALESCE(ep.PropertyName, ''))) AS PropertyName,
                    TRIM(COALESCE(ep.PropertyValue, '')) AS PropertyValue
                FROM EntityProperties ep
                WHERE ep.EntityId IN ({string.Join(", ", placeholders)})
                  AND UPPER(TRIM(COALESCE(ep.PropertyName, ''))) IN ('SPECIFI1', 'SPECIFO1');
                """;

            await using var command = new SqliteCommand(sql, connection);
            for (var index = 0; index < batch.Count; index++)
            {
                command.Parameters.AddWithValue($"$id{index}", batch[index]);
            }

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var entityId = reader.GetInt64(0);
                var propertyName = reader.GetString(1).Trim().ToUpperInvariant();
                var propertyValue = reader.GetString(2).Trim();
                if (!int.TryParse(propertyValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    continue;
                }

                results.TryGetValue(entityId, out var current);
                if (propertyName.Equals("SPECIFI1", StringComparison.OrdinalIgnoreCase))
                {
                    current.SpecifIn = parsed;
                }
                else if (propertyName.Equals("SPECIFO1", StringComparison.OrdinalIgnoreCase))
                {
                    current.SpecifOut = parsed;
                }

                results[entityId] = current;
            }
        }

        return results;
    }

    private static async Task<Dictionary<long, string>> QueryProtocolTypeByEntityIdAsync(
        SqliteConnection connection,
        IReadOnlyList<long> entityIds)
    {
        var results = new Dictionary<long, string>();
        if (entityIds.Count == 0)
        {
            return results;
        }

        const int batchSize = 300;
        for (var offset = 0; offset < entityIds.Count; offset += batchSize)
        {
            var batch = entityIds.Skip(offset).Take(batchSize).ToList();
            if (batch.Count == 0)
            {
                continue;
            }

            var placeholders = batch.Select((_, index) => $"$id{index}").ToList();
            var sql = $"""
                SELECT
                    ep.EntityId,
                    UPPER(TRIM(COALESCE(ep.PropertyValue, ''))) AS PropertyValue
                FROM EntityProperties ep
                WHERE ep.EntityId IN ({string.Join(", ", placeholders)})
                  AND UPPER(TRIM(COALESCE(ep.PropertyName, ''))) = 'PNTPCTY';
                """;

            await using var command = new SqliteCommand(sql, connection);
            for (var index = 0; index < batch.Count; index++)
            {
                command.Parameters.AddWithValue($"$id{index}", batch[index]);
            }

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var entityId = reader.GetInt64(0);
                var protocol = reader.GetString(1).Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(protocol))
                {
                    continue;
                }

                results[entityId] = protocol;
            }
        }

        return results;
    }

    private static async Task<Dictionary<long, (List<string> ModbusIn, List<string> ModbusOut, List<string> AbIn, List<string> AbOut)>> QueryDirectPointAddressesByEntityIdAsync(
        SqliteConnection connection,
        IReadOnlyList<long> entityIds)
    {
        var results = new Dictionary<long, (List<string> ModbusIn, List<string> ModbusOut, List<string> AbIn, List<string> AbOut)>();
        if (entityIds.Count == 0)
        {
            return results;
        }

        const int batchSize = 300;
        for (var offset = 0; offset < entityIds.Count; offset += batchSize)
        {
            var batch = entityIds.Skip(offset).Take(batchSize).ToList();
            if (batch.Count == 0)
            {
                continue;
            }

            var placeholders = batch.Select((_, index) => $"$id{index}").ToList();
            var sql = $"""
                SELECT
                    ep.EntityId,
                    UPPER(TRIM(COALESCE(ep.PropertyName, ''))) AS PropertyName,
                    TRIM(COALESCE(ep.PropertyValue, '')) AS PropertyValue
                FROM EntityProperties ep
                WHERE ep.EntityId IN ({string.Join(", ", placeholders)})
                  AND (
                        UPPER(COALESCE(ep.PropertyName, '')) LIKE '%PCADDRI%'
                        OR UPPER(COALESCE(ep.PropertyName, '')) LIKE '%PCADDRO%'
                        OR UPPER(COALESCE(ep.PropertyName, '')) LIKE '%PLCADDRI%'
                        OR UPPER(COALESCE(ep.PropertyName, '')) LIKE '%PLCADDRO%'
                      )
                ORDER BY ep.EntityId, ep.SortOrder;
                """;

            await using var command = new SqliteCommand(sql, connection);
            for (var index = 0; index < batch.Count; index++)
            {
                command.Parameters.AddWithValue($"$id{index}", batch[index]);
            }

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var entityId = reader.GetInt64(0);
                var propertyName = reader.GetString(1).Trim().ToUpperInvariant();
                var propertyValue = reader.GetString(2).Trim();
                if (string.IsNullOrWhiteSpace(propertyValue))
                {
                    continue;
                }

                var isInput = propertyName.Contains("PCADDRI") || propertyName.Contains("PLCADDRI");

                if (!results.TryGetValue(entityId, out var bucket))
                {
                    bucket = ([], [], [], []);
                    results[entityId] = bucket;
                }

                foreach (Match match in Regex.Matches(propertyValue, @"(?i)\b(?:[A-Z]{1,3}\d+:\d+(?:/\d+)?|\d{1,7})\b"))
                {
                    var token = match.Value.Trim().ToUpperInvariant();
                    if (string.IsNullOrWhiteSpace(token))
                    {
                        continue;
                    }

                    var isAb = token.Contains(':');
                    if (isAb)
                    {
                        if (isInput)
                        {
                            if (!bucket.AbIn.Contains(token, StringComparer.OrdinalIgnoreCase))
                            {
                                bucket.AbIn.Add(token);
                            }
                        }
                        else
                        {
                            if (!bucket.AbOut.Contains(token, StringComparer.OrdinalIgnoreCase))
                            {
                                bucket.AbOut.Add(token);
                            }
                        }
                    }
                    else if (isInput)
                    {
                        if (!bucket.ModbusIn.Contains(token, StringComparer.OrdinalIgnoreCase))
                        {
                            bucket.ModbusIn.Add(token);
                        }
                    }
                    else
                    {
                        if (!bucket.ModbusOut.Contains(token, StringComparer.OrdinalIgnoreCase))
                        {
                            bucket.ModbusOut.Add(token);
                        }
                    }
                }

                results[entityId] = bucket;
            }
        }

        return results;
    }

    private static async Task<Dictionary<string, (List<string> ModbusIn, List<string> ModbusOut)>> QueryModiconAddressesByTagKeyAsync(
        SqliteConnection connection,
        IReadOnlyList<string> tagKeys)
    {
        var results = new Dictionary<string, (List<string> ModbusIn, List<string> ModbusOut)>(StringComparer.OrdinalIgnoreCase);
        if (tagKeys.Count == 0)
        {
            return results;
        }

        const int batchSize = 300;
        for (var offset = 0; offset < tagKeys.Count; offset += batchSize)
        {
            var batch = tagKeys.Skip(offset).Take(batchSize).ToList();
            if (batch.Count == 0)
            {
                continue;
            }

            var placeholders = batch.Select((_, index) => $"$tag{index}").ToList();
            var inList = string.Join(", ", placeholders);
            var tagNamePrefixExpr = """
                UPPER(
                    CASE
                        WHEN INSTR(COALESCE(TagName, ''), '.') > 0
                            THEN SUBSTR(COALESCE(TagName, ''), 1, INSTR(COALESCE(TagName, ''), '.') - 1)
                        ELSE TRIM(COALESCE(TagName, ''))
                    END
                )
                """;
            var sql = $"""
                SELECT
                    UPPER(
                        CASE
                            WHEN UPPER(TRIM(COALESCE(TagKey, ''))) IN ({inList})
                                THEN TRIM(COALESCE(TagKey, ''))
                            ELSE
                                CASE
                                    WHEN INSTR(COALESCE(TagName, ''), '.') > 0
                                        THEN SUBSTR(COALESCE(TagName, ''), 1, INSTR(COALESCE(TagName, ''), '.') - 1)
                                    ELSE TRIM(COALESCE(TagName, ''))
                                END
                        END
                    ) AS MatchedKey,
                    TRIM(COALESCE(RegisterAddress, '')) AS RegisterAddress,
                    UPPER(TRIM(COALESCE(SourceLine, ''))) AS SourceLine
                FROM ModiconRegisterMappings
                WHERE (
                        UPPER(TRIM(COALESCE(TagKey, ''))) IN ({inList})
                        OR {tagNamePrefixExpr} IN ({inList})
                      )
                  AND TRIM(COALESCE(RegisterAddress, '')) <> '';
                """;

            await using var command = new SqliteCommand(sql, connection);
            for (var index = 0; index < batch.Count; index++)
            {
                command.Parameters.AddWithValue($"$tag{index}", batch[index]);
            }

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var key = reader.GetString(0).Trim().ToUpperInvariant();
                var address = reader.GetString(1).Trim();
                var sourceLine = reader.GetString(2).Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(address))
                {
                    continue;
                }

                if (!results.TryGetValue(key, out var bucket))
                {
                    bucket = ([], []);
                    results[key] = bucket;
                }

                var isInput = sourceLine.Contains("PCADDRI", StringComparison.OrdinalIgnoreCase)
                    || sourceLine.Contains("PLCADDRI", StringComparison.OrdinalIgnoreCase);
                var isOutput = sourceLine.Contains("PCADDRO", StringComparison.OrdinalIgnoreCase)
                    || sourceLine.Contains("PLCADDRO", StringComparison.OrdinalIgnoreCase);

                if (isInput && !bucket.ModbusIn.Contains(address, StringComparer.OrdinalIgnoreCase))
                {
                    bucket.ModbusIn.Add(address);
                }

                if (isOutput && !bucket.ModbusOut.Contains(address, StringComparer.OrdinalIgnoreCase))
                {
                    bucket.ModbusOut.Add(address);
                }

                results[key] = bucket;
            }
        }

        return results;
    }

    private static async Task<Dictionary<string, (List<string> AbIn, List<string> AbOut)>> QueryAbAddressesByTagKeyAsync(
        SqliteConnection connection,
        IReadOnlyList<string> tagKeys)
    {
        var results = new Dictionary<string, (List<string> AbIn, List<string> AbOut)>(StringComparer.OrdinalIgnoreCase);
        if (tagKeys.Count == 0)
        {
            return results;
        }

        const int batchSize = 300;
        for (var offset = 0; offset < tagKeys.Count; offset += batchSize)
        {
            var batch = tagKeys.Skip(offset).Take(batchSize).ToList();
            if (batch.Count == 0)
            {
                continue;
            }

            var placeholders = batch.Select((_, index) => $"$tag{index}").ToList();
            var inList = string.Join(", ", placeholders);
            var tagNamePrefixExpr = """
                UPPER(
                    CASE
                        WHEN INSTR(COALESCE(TagName, ''), '.') > 0
                            THEN SUBSTR(COALESCE(TagName, ''), 1, INSTR(COALESCE(TagName, ''), '.') - 1)
                        ELSE TRIM(COALESCE(TagName, ''))
                    END
                )
                """;
            var sql = $"""
                SELECT
                    UPPER(
                        CASE
                            WHEN UPPER(TRIM(COALESCE(TagKey, ''))) IN ({inList})
                                THEN TRIM(COALESCE(TagKey, ''))
                            ELSE
                                CASE
                                    WHEN INSTR(COALESCE(TagName, ''), '.') > 0
                                        THEN SUBSTR(COALESCE(TagName, ''), 1, INSTR(COALESCE(TagName, ''), '.') - 1)
                                    ELSE TRIM(COALESCE(TagName, ''))
                                END
                        END
                    ) AS MatchedKey,
                    TRIM(COALESCE(RegisterAddress, '')) AS RegisterAddress,
                    UPPER(TRIM(COALESCE(SourceLine, ''))) AS SourceLine
                FROM AbPlcRegisterMappings
                WHERE (
                        UPPER(TRIM(COALESCE(TagKey, ''))) IN ({inList})
                        OR {tagNamePrefixExpr} IN ({inList})
                      )
                  AND TRIM(COALESCE(RegisterAddress, '')) <> ''
                ORDER BY RegisterAddress;
                """;

            await using var command = new SqliteCommand(sql, connection);
            for (var index = 0; index < batch.Count; index++)
            {
                command.Parameters.AddWithValue($"$tag{index}", batch[index]);
            }

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var key = reader.GetString(0).Trim().ToUpperInvariant();
                var address = reader.GetString(1).Trim();
                var sourceLine = reader.GetString(2).Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(address))
                {
                    continue;
                }

                if (!results.TryGetValue(key, out var bucket))
                {
                    bucket = ([], []);
                    results[key] = bucket;
                }

                var isInput = sourceLine.Contains("PCADDRI", StringComparison.OrdinalIgnoreCase)
                    || sourceLine.Contains("PLCADDRI", StringComparison.OrdinalIgnoreCase);
                var isOutput = sourceLine.Contains("PCADDRO", StringComparison.OrdinalIgnoreCase)
                    || sourceLine.Contains("PLCADDRO", StringComparison.OrdinalIgnoreCase);

                if (isInput && !bucket.AbIn.Contains(address, StringComparer.OrdinalIgnoreCase))
                {
                    bucket.AbIn.Add(address);
                }

                if (isOutput && !bucket.AbOut.Contains(address, StringComparer.OrdinalIgnoreCase))
                {
                    bucket.AbOut.Add(address);
                }
            }
        }

        return results;
    }

    private static string NormalizeHardwarePointTagKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var token = value.Trim();
        var dotIndex = token.IndexOf('.');
        if (dotIndex > 0)
        {
            token = token[..dotIndex];
        }

        return token.Trim().ToUpperInvariant();
    }

    private static bool PlcSystemMatchesHwyBox(
        int? systemHwyNum,
        int? primaryBoxNum,
        string hwyBoxNumbersCsv,
        int pointHwyNum,
        int pointBoxNum)
    {
        if (systemHwyNum.GetValueOrDefault(-1) != pointHwyNum)
        {
            return false;
        }

        var boxes = new HashSet<int>();
        if (primaryBoxNum.HasValue)
        {
            boxes.Add(primaryBoxNum.Value);
        }

        foreach (var segment in (hwyBoxNumbersCsv ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var parsedBox = ParseNullableHwyBoxNumber(segment);
            if (parsedBox.HasValue)
            {
                boxes.Add(parsedBox.Value);
            }
        }

        return boxes.Contains(pointBoxNum);
    }

    private static string GetPropertyValue(IReadOnlyDictionary<string, string> properties, string key) =>
        properties.TryGetValue(key, out var value) ? value : string.Empty;

    private static int? ParseNullableInt(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return int.TryParse(value.Trim(), out var parsed) ? parsed : null;
    }

    private static int? ParseNullableHwyBoxNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var token = value.Trim();
        if (token.StartsWith("BOX", StringComparison.OrdinalIgnoreCase))
        {
            token = token[3..].Trim();
        }
        else if (token.StartsWith("B", StringComparison.OrdinalIgnoreCase) && token.Length > 1)
        {
            token = token[1..].Trim();
        }

        return ParseNullableInt(token);
    }

    private static string GetIndexedProperty(Dictionary<string, string> properties, string prefix, int index)
    {
        var key = $"{prefix}({index})";
        return properties.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private static List<(int Index, string Value)> GetIndexedValues(Dictionary<string, string> properties, string prefix)
    {
        var results = new List<(int Index, string Value)>();
        foreach (var pair in properties)
        {
            var match = IndexedPropertyRegex.Match(pair.Key);
            if (!match.Success || !match.Groups[1].Value.Equals(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (int.TryParse(match.Groups[2].Value, out var index))
            {
                results.Add((index, pair.Value));
            }
        }

        results.Sort((a, b) => a.Index.CompareTo(b.Index));
        return results;
    }

    private static bool TryParseIndexedPropertyName(string propertyName, out string prefix, out int index)
    {
        prefix = string.Empty;
        index = 0;

        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return false;
        }

        var match = IndexedPropertyRegex.Match(propertyName.Trim());
        if (!match.Success || !int.TryParse(match.Groups[2].Value, out index))
        {
            return false;
        }

        prefix = match.Groups[1].Value;
        return true;
    }

    private static List<string> GetGateInputs(Dictionary<string, string> properties, int gateIndex)
    {
        var matches = new List<(string Pin, int Order, string Value, bool IsReversed)>();

        foreach (var pair in properties)
        {
            var match = GateInputPropertyRegex.Match(pair.Key);
            if (!match.Success)
            {
                continue;
            }

            if (!int.TryParse(match.Groups[2].Value, out var index) || index != gateIndex)
            {
                continue;
            }

            var value = pair.Value.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var pin = match.Groups[1].Value.ToUpperInvariant();
            var order = 0;
            if (pin.Length > 1)
            {
                int.TryParse(pin[1..], out order);
            }

            var isReversed = false;
            if (pin.StartsWith("S", StringComparison.OrdinalIgnoreCase))
            {
                var reversePropertyName = $"{pin}REV({gateIndex})";
                if (properties.TryGetValue(reversePropertyName, out var reverseValue)
                    && reverseValue.Trim().Equals("ON", StringComparison.OrdinalIgnoreCase))
                {
                    isReversed = true;
                }
            }

            matches.Add((pin, order, value, isReversed));
        }

        return matches
            .OrderBy(x => x.Pin[0])
            .ThenBy(x => x.Order)
            .Select(x => x.IsReversed ? $"NOT {x.Value}" : x.Value)
            .ToList();
    }

    private static string BuildSearchKey(ParsedEntity entity) =>
        $"{entity.SystemEntityHeader} {entity.EntityName} {entity.EntityType}".ToLowerInvariant();

    private static string GetPreferredEntityDisplayName(string entityName, string systemEntityHeader)
    {
        var trimmedEntityName = entityName.Trim();
        var headerName = ExtractSystemEntityName(systemEntityHeader);

        if (string.IsNullOrWhiteSpace(trimmedEntityName))
        {
            return headerName;
        }

        var upper = trimmedEntityName.ToUpperInvariant();
        if (upper == "$NM" || upper == "$OGROUP" || upper.StartsWith("$OGROUP(", StringComparison.Ordinal))
        {
            return headerName;
        }

        return trimmedEntityName;
    }

    private static string ExtractSystemEntityName(string systemEntityHeader)
    {
        if (string.IsNullOrWhiteSpace(systemEntityHeader))
        {
            return string.Empty;
        }

        var header = systemEntityHeader.Trim();
        if (header.StartsWith("{SYSTEM ENTITY", StringComparison.OrdinalIgnoreCase))
        {
            header = header["{SYSTEM ENTITY".Length..].Trim();
        }

        if (header.EndsWith("}"))
        {
            header = header[..^1].TrimEnd();
        }

        var bracketIndex = header.LastIndexOf('(');
        if (bracketIndex > 0)
        {
            header = header[..bracketIndex].TrimEnd();
        }

        return header;
    }

    private static string NormalizeSoftParameterName(string value)
    {
        var normalized = NormalizeConnectionValue(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (normalized.StartsWith(">", StringComparison.Ordinal))
        {
            normalized = normalized[1..].TrimStart();
        }

        return normalized.Trim();
    }

    private static string NormalizeConnectionValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        if (normalized.Length >= 2
            && ((normalized[0] == '\'' && normalized[^1] == '\'')
                || (normalized[0] == '"' && normalized[^1] == '"')))
        {
            normalized = normalized[1..^1].Trim();
        }

        if (normalized.Equals("--.--", StringComparison.Ordinal) || normalized.Equals("--", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return normalized;
    }

    private static string NormalizeGraphicName(string value)
    {
        var normalized = NormalizeConnectionValue(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        normalized = Regex.Replace(normalized, "almgrp", string.Empty, RegexOptions.IgnoreCase).Trim();
        normalized = normalized.Trim(' ', '-', '_', '.', ':', ';');
        return normalized;
    }

    private static string NormalizeGraphicsHeader(string value)
    {
        var normalized = NormalizeConnectionValue(value).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var colon = normalized.LastIndexOf(':');
        if (colon >= 0 && colon < normalized.Length - 1)
        {
            normalized = normalized[(colon + 1)..].Trim();
        }

        return normalized;
    }

    private static int FindHeaderColumn(Dictionary<string, int> headerMap, string marker)
    {
        foreach (var (key, value) in headerMap)
        {
            if (key.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return -1;
    }

    private static string ExtractOpcIntegratorTag(string itemPath)
    {
        var normalized = NormalizeConnectionValue(itemPath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        normalized = normalized.TrimEnd('/');
        var slashIndex = normalized.LastIndexOf('/');
        if (slashIndex >= 0 && slashIndex < normalized.Length - 1)
        {
            normalized = normalized[(slashIndex + 1)..];
        }

        return normalized.Trim();
    }

    private static bool IsLikelyTdcTag(string tag)
    {
        var normalized = NormalizeConnectionValue(tag);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var dotIndex = normalized.IndexOf('.');
        if (dotIndex <= 0 || dotIndex >= normalized.Length - 1)
        {
            return false;
        }

        return normalized.IndexOf('.', dotIndex + 1) < 0;
    }

    private static string ExtractConnectionEntity(string value)
    {
        var normalized = NormalizeConnectionValue(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var dot = normalized.IndexOf('.');
        if (dot > 0)
        {
            normalized = normalized[..dot];
        }

        return normalized.Trim();
    }

    private static string ExtractPlcMappingTagKey(string mappingText)
    {
        if (string.IsNullOrWhiteSpace(mappingText))
        {
            return string.Empty;
        }

        var matches = Regex.Matches(mappingText, @"(?i)\b([A-Z][A-Z0-9_]*)\s*\.\s*[A-Z][A-Z0-9_]*\s*(?:\(|$)");
        if (matches.Count > 0)
        {
            var token = matches[^1].Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(token))
            {
                return token.ToUpperInvariant();
            }
        }

        return ExtractConnectionEntity(mappingText).ToUpperInvariant();
    }

    private async Task<long> UpsertFileAsync(SqliteConnection connection, SqliteTransaction transaction, string filePath)
    {
        const string upsertSql = """
            INSERT INTO ImportedFiles (FilePath, FileName, ImportedAtUtc)
            VALUES ($filePath, $fileName, $importedAtUtc)
            ON CONFLICT(FilePath) DO UPDATE SET
                FileName = excluded.FileName,
                ImportedAtUtc = excluded.ImportedAtUtc;
            """;

        await using (var upsertCommand = new SqliteCommand(upsertSql, connection, transaction))
        {
            upsertCommand.Parameters.AddWithValue("$filePath", filePath);
            upsertCommand.Parameters.AddWithValue("$fileName", Path.GetFileName(filePath));
            upsertCommand.Parameters.AddWithValue("$importedAtUtc", DateTime.UtcNow.ToString("O"));
            await upsertCommand.ExecuteNonQueryAsync();
        }

        const string getIdSql = "SELECT Id FROM ImportedFiles WHERE FilePath = $filePath;";
        await using var idCommand = new SqliteCommand(getIdSql, connection, transaction);
        idCommand.Parameters.AddWithValue("$filePath", filePath);
        return (long)(await idCommand.ExecuteScalarAsync() ?? 0L);
    }

    private static async Task DeleteFileEntitiesAsync(SqliteConnection connection, SqliteTransaction transaction, long fileId)
    {
        const string deleteSql = "DELETE FROM Entities WHERE FileId = $fileId;";
        await using var deleteCommand = new SqliteCommand(deleteSql, connection, transaction);
        deleteCommand.Parameters.AddWithValue("$fileId", fileId);
        await deleteCommand.ExecuteNonQueryAsync();
    }

    private async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var pragmaCommand = new SqliteCommand("PRAGMA foreign_keys = ON;", connection);
        await pragmaCommand.ExecuteNonQueryAsync();

        return connection;
    }

    private static string GetProgramTypeFromPath(string path)
    {
        if (path.Contains("\\AMCL\\", StringComparison.OrdinalIgnoreCase))
        {
            return "AMCL";
        }

        if (path.Contains("\\PMCL\\", StringComparison.OrdinalIgnoreCase))
        {
            return "PMCL";
        }

        return "CL";
    }

    private static string NormalizeProgramKey(string value) => value.Trim().ToUpperInvariant();

    private static string NormalizePlcKey(string value) => value.Trim().ToUpperInvariant();

    private static string BuildSqlLikePattern(string value, bool normalizeUpper)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = normalizeUpper
            ? value.Trim().ToUpperInvariant()
            : value.Trim().ToLowerInvariant();
        var builder = new StringBuilder(normalized.Length + 4);

        foreach (var ch in normalized)
        {
            switch (ch)
            {
                case '*':
                    builder.Append('%');
                    break;
                case '?':
                    builder.Append('_');
                    break;
                case '%':
                case '_':
                case '\\':
                    builder.Append('\\').Append(ch);
                    break;
                default:
                    builder.Append(ch);
                    break;
            }
        }

        if (builder.Length == 0)
        {
            return string.Empty;
        }

        // Keep wildcard search in "contains" mode so 4*463 matches "...4LX463..."
        // the same way plain text search behaves.
        var pattern = builder.ToString();
        if (!pattern.StartsWith('%'))
        {
            pattern = "%" + pattern;
        }

        if (!pattern.EndsWith('%'))
        {
            pattern += "%";
        }

        return pattern;
    }

    private static string NormalizePlcFileAddress(string value)
    {
        var normalized = value.Trim().ToUpperInvariant();
        var colon = normalized.IndexOf(':');
        if (colon > 0)
        {
            normalized = normalized[..colon];
        }

        return normalized;
    }

    private static async Task EnsureColumnExistsAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string columnDefinition)
    {
        var pragmaSql = $"PRAGMA table_info({tableName});";
        await using var pragmaCommand = new SqliteCommand(pragmaSql, connection);
        await using var reader = await pragmaCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var existingColumn = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            if (existingColumn.Equals(columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        var alterSql = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        await using var alterCommand = new SqliteCommand(alterSql, connection);
        await alterCommand.ExecuteNonQueryAsync();
    }
}
