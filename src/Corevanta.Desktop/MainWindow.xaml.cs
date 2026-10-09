using Corevanta.Desktop.Models;
using Corevanta.Desktop.Services;
using Microsoft.Msagl.Core.Geometry.Curves;
using Microsoft.Msagl.Core.Layout;
using Microsoft.Msagl.Layout.Layered;
using Microsoft.Msagl.Miscellaneous;
using Microsoft.Win32;
using Nodify;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using System.Windows.Xps.Packaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using IOPath = System.IO.Path;
using MsaglEdge = Microsoft.Msagl.Core.Layout.Edge;
using MsaglNode = Microsoft.Msagl.Core.Layout.Node;
using MsaglPoint = Microsoft.Msagl.Core.Geometry.Point;

namespace Corevanta.Desktop;

public partial class MainWindow : Window
{
    private readonly EbParser _parser = new();
    private readonly DatabaseService _database;
    private readonly string _workingDatabasePath;
    private readonly string _projectStatePath;
    private readonly string _unsavedImportLogPath;
    private readonly LicenseService _licenseService;
    private readonly ObservableCollection<string> _importLog = [];
    private readonly ObservableCollection<EntitySearchResult> _searchResults = [];
    private readonly ObservableCollection<string> _detailPointTypeFilters = [];
    private readonly ObservableCollection<PropertyRow> _propertyRows = [];
    private readonly ObservableCollection<ClProgramListItem> _clResults = [];
    private readonly ObservableCollection<LogicBlockSummary> _logicResults = [];
    private readonly ObservableCollection<EntitySearchResult> _chartResults = [];
    private readonly ObservableCollection<ChangeManagementEntry> _changeManagementEntries = [];
    private readonly ObservableCollection<AbPlcSummary> _abPlcSystems = [];
    private readonly ObservableCollection<AbPlcLadderFile> _abPlcLadderFiles = [];
    private readonly ObservableCollection<ModiconSummary> _modiconSystems = [];
    private readonly ObservableCollection<ModiconProgramFile> _modiconProgramFiles = [];
    private readonly ObservableCollection<HardwareBoxSummary> _hardwareBoxes = [];
    private readonly ObservableCollection<HardwareTreeNode> _hardwareAssignedPlcNodes = [];
    private readonly ObservableCollection<HardwareTreeNode> _hardwareTreeNodes = [];
    private readonly ObservableCollection<HardwarePointSummary> _hardwarePoints = [];
    private readonly ObservableCollection<ModiconTrafficCopEntry> _hardwareTrafficCopEntries = [];
    private readonly ObservableCollection<HardwarePointSummary> _hardwarePlcPoints = [];
    private readonly ObservableCollection<LogicVertex> _logicDiagramNodes = [];
    private readonly ObservableCollection<LogicEdge> _logicDiagramEdges = [];
    private readonly ObservableCollection<LogicVertex> _chartDiagramNodes = [];
    private readonly ObservableCollection<LogicEdge> _chartDiagramEdges = [];
    private long? _activeLogicEntityId;
    private long? _selectedDetailEntityId;
    private long? _activeChartEntityId;
    private long? _activeAbPlcSystemId;
    private long? _activeModiconSystemId;
    private HardwareBoxDetail? _activeHardwareBoxDetail;
    private HardwareModuleSummary? _activeHardwareModule;
    private XpsDocument? _activeAbPlcXpsDocument;
    private XpsDocument? _activeModiconXpsDocument;
    private bool _suppressAbPlcFileSelectionDisplay;
    private bool _suppressModiconFileSelectionDisplay;
    private List<int> _abPlcXpsSearchFlIndices = [];
    private int _abPlcXpsSearchIndex = -1;
    private List<int> _modiconXpsSearchFlIndices = [];
    private int _modiconXpsSearchIndex = -1;
    private readonly Dictionary<string, Dictionary<int, string>> _xpsOcrCache = new(StringComparer.OrdinalIgnoreCase);
    private Point? _chartMouseDownPoint;
    private Point? _chartMouseDownVertexPosition;
    private string? _chartMouseDownVertexKey;
    private long? _chartPendingClProgramId;
    private long? _chartPendingLogicEntityId;
    private bool _chartPendingExpandUpstream;
    private bool _chartPendingExpandDownstream;
    private string? _lastMainTabHeader;
    private string? _currentProjectPath;
    private string? _projectPassword;
    private bool _hasUnsavedProjectChanges;
    private bool _suppressImportLogPersistence;
    private bool _isDiagramPanning;
    private Point _diagramPanLastPoint;
    private NodifyEditor? _diagramPanEditor;
    private readonly List<LogicVertex> _logicFollowerTrackedGates = [];
    private readonly Dictionary<LogicVertex, List<LogicEdge>> _logicFollowerEdgesByTarget = [];
    private readonly Dictionary<LogicVertex, Point> _logicFollowerLastGatePosition = [];
    private bool _logicFollowerSyncInProgress;
    private int _chartUpstreamDepth = 1;
    private int _chartDownstreamDepth = 1;
    private readonly HashSet<string> _chartExpandableUpstreamKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _chartExpandableDownstreamKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _chartExpandedUpstreamKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _chartExpandedDownstreamKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<DiagramDragUndoAction> _diagramDragUndoStack = [];
    private Dictionary<LogicVertex, Point>? _logicDragSnapshotBefore;
    private bool _logicDragFinalizeQueued;
    private Dictionary<LogicVertex, Point>? _chartDragSnapshotBefore;
    private bool _chartDragFinalizeQueued;
    private IReadOnlyDictionary<string, long>? _entityLookup;
    private int _chartLoadRequestVersion;
    private string _clFindQuery = string.Empty;
    private int _clFindLastIndex = -1;
    private bool _isUpdatingDetailPointTypeFilter;
    private const string AllDetailPointTypesOption = "(All Point Types)";
    private const string ImportActionEbFile = "EB_FILE";
    private const string ImportActionClFolder = "CL_FOLDER";
    private const string ImportActionAbSystem = "AB_SYSTEM";
    private const string ImportActionModiconSystem = "MODICON_SYSTEM";
    private const string ImportActionSoftConnectionsFile = "SOFT_CONNECTIONS_FILE";
    private const string ImportActionOpcIntegratorFile = "OPC_INTEGRATOR_FILE";
    private const string ImportActionGraphicsFile = "GRAPHICS_FILE";
    private const string ImportActionPiFile = "PI_FILE";
    private static readonly byte[] ProjectEncryptionMagic = Encoding.ASCII.GetBytes("CVP1");
    private const int ProjectEncryptionVersion = 1;
    private const int ProjectPasswordIterations = 200_000;

    private sealed class DiagramDragUndoAction
    {
        public List<(LogicVertex Vertex, Point Before, Point After)> Moves { get; } = [];
    }

    public MainWindow()
    {
        // Nodify can throw on load when auto-focusing with empty/virtualized containers.
        NodifyEditor.AutoFocusFirstElement = false;
        InitializeComponent();
#if !DEBUG
        Cv2TestButton.Visibility = Visibility.Collapsed;
#endif
#if FREE_GITHUB_BUILD
        Title = "Corevanta GitHub Free";
        LicenseButton.Content = "Free Build";
        LicenseButton.IsEnabled = false;
#endif

        var dbDirectory = IOPath.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
#if FREE_GITHUB_BUILD
            "CorevantaGitHubFree",
#else
            "Corevanta",
#endif
            "Data");
        _workingDatabasePath = IOPath.Combine(dbDirectory, "corevanta.db");
        _projectStatePath = IOPath.Combine(dbDirectory, "project-path.txt");
        _unsavedImportLogPath = IOPath.Combine(dbDirectory, "unsaved.import.log");
        _licenseService = new LicenseService(dbDirectory);

        _database = new DatabaseService(_workingDatabasePath);
        RestoreCurrentProjectPath();

        ImportLogList.ItemsSource = _importLog;
        _importLog.CollectionChanged += ImportLog_CollectionChanged;
        EntityResultsList.ItemsSource = _searchResults;
        DetailPointTypeFilterComboBox.ItemsSource = _detailPointTypeFilters;
        DetailsDataGrid.ItemsSource = _propertyRows;
        ClResultsList.ItemsSource = _clResults;
        LogicResultsList.ItemsSource = _logicResults;
        ChartResultsList.ItemsSource = _chartResults;
        ChangeManagementGrid.ItemsSource = _changeManagementEntries;
        AbPlcSystemsList.ItemsSource = _abPlcSystems;
        AbPlcFilesList.ItemsSource = _abPlcLadderFiles;
        ModiconSystemsList.ItemsSource = _modiconSystems;
        ModiconFilesList.ItemsSource = _modiconProgramFiles;
        HardwareResultsList.ItemsSource = _hardwareBoxes;
        HardwareAssignedPlcTreeView.ItemsSource = _hardwareAssignedPlcNodes;
        HardwareTreeView.ItemsSource = _hardwareTreeNodes;
        HardwarePointsGrid.ItemsSource = _hardwarePoints;
        HardwareTrafficCopGrid.ItemsSource = _hardwareTrafficCopEntries;
        HardwarePlcPointsGrid.ItemsSource = _hardwarePlcPoints;
        ConfigureDiagramEditors();
        UpdateChartDepthText();
        UpdateProjectStatusText();

        Loaded += MainWindow_Loaded;
        Deactivated += MainWindow_Deactivated;
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!EnsureLicenseActivated())
            {
                Close();
                return;
            }

            await _database.InitializeAsync();
            _importLog.Add($"[{DateTime.Now:HH:mm:ss}] Database ready: {DateTime.Now:yyyy-MM-dd}");
            await RunSearchAsync();
            await RunClSearchAsync();
            await RunLogicSearchAsync();
            await RunChartSearchAsync();
            await RunAbPlcSearchAsync();
            await RunModiconSearchAsync();
            await RunHardwareSearchAsync();
            await RefreshImportSummariesAsync();
            await RefreshChangeManagementAsync();
            UpdateProjectStatusText();
            if (MainTabControl.SelectedItem is TabItem selectedTab && selectedTab.Header is string header)
            {
                _lastMainTabHeader = header;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to initialize database.\n\n{ex.Message}", "Initialization Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        ClearAbPlcXpsDocument();
        ClearModiconXpsDocument();
    }

    private void ImportLog_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_suppressImportLogPersistence || e.Action != NotifyCollectionChangedAction.Add || e.NewItems is null)
        {
            return;
        }

        var replacements = new List<(int Index, string Value)>();
        var startIndex = e.NewStartingIndex;
        var offset = 0;
        foreach (var item in e.NewItems)
        {
            if (item is not string line || string.IsNullOrWhiteSpace(line))
            {
                offset++;
                continue;
            }

            var normalizedLine = line.Trim();
            if (!IsFullTimestampedImportLogLine(normalizedLine))
            {
                normalizedLine = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {normalizedLine}";
                var targetIndex = startIndex + offset;
                if (targetIndex >= 0 && targetIndex < _importLog.Count)
                {
                    replacements.Add((targetIndex, normalizedLine));
                }
            }

            PersistImportLogLine(normalizedLine);
            offset++;
        }

        if (replacements.Count == 0)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            _suppressImportLogPersistence = true;
            try
            {
                foreach (var replacement in replacements)
                {
                    if (replacement.Index >= 0 && replacement.Index < _importLog.Count)
                    {
                        _importLog[replacement.Index] = replacement.Value;
                    }
                }
            }
            finally
            {
                _suppressImportLogPersistence = false;
            }
        }));
    }

    private string GetProjectImportLogPath(string projectPath) => $"{projectPath}.import.log";

    private string GetActiveImportLogPath() =>
        !string.IsNullOrWhiteSpace(_currentProjectPath)
            ? GetProjectImportLogPath(_currentProjectPath)
            : _unsavedImportLogPath;

    private void PersistImportLogLine(string line)
    {
        try
        {
            var logPath = GetActiveImportLogPath();
            var directory = IOPath.GetDirectoryName(logPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(logPath, line + Environment.NewLine);
        }
        catch
        {
            // Non-fatal: UI log should continue even if disk log cannot be written.
        }
    }

    private static bool IsFullTimestampedImportLogLine(string line)
    {
        return Regex.IsMatch(
            line ?? string.Empty,
            @"^\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}\s+\|",
            RegexOptions.CultureInvariant);
    }

    private void LoadPersistedImportLogForProject(string projectPath)
    {
        var logPath = GetProjectImportLogPath(projectPath);
        _suppressImportLogPersistence = true;
        try
        {
            _importLog.Clear();
            if (!File.Exists(logPath))
            {
                return;
            }

            foreach (var line in File.ReadLines(logPath))
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    _importLog.Add(line);
                }
            }
        }
        finally
        {
            _suppressImportLogPersistence = false;
        }
    }

    private void PromoteUnsavedImportLogToProject(string projectPath)
    {
        try
        {
            if (!File.Exists(_unsavedImportLogPath))
            {
                return;
            }

            var targetPath = GetProjectImportLogPath(projectPath);
            var targetDirectory = IOPath.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            var unsavedLines = File.ReadAllLines(_unsavedImportLogPath);
            if (unsavedLines.Length > 0)
            {
                File.AppendAllLines(targetPath, unsavedLines);
            }

            File.Delete(_unsavedImportLogPath);
        }
        catch
        {
            // Non-fatal: project still functions without history promotion.
        }
    }

    private void CopyProjectImportLogIfNeeded(string? sourceProjectPath, string targetProjectPath)
    {
        if (string.IsNullOrWhiteSpace(sourceProjectPath)
            || sourceProjectPath.Equals(targetProjectPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            var sourceLog = GetProjectImportLogPath(sourceProjectPath);
            if (!File.Exists(sourceLog))
            {
                return;
            }

            var targetLog = GetProjectImportLogPath(targetProjectPath);
            var targetDirectory = IOPath.GetDirectoryName(targetLog);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            File.Copy(sourceLog, targetLog, overwrite: true);
        }
        catch
        {
            // Non-fatal: save should not fail if log copy fails.
        }
    }

    private bool EnsureLicenseActivated()
    {
#if FREE_GITHUB_BUILD
        _importLog.Add($"[{DateTime.Now:HH:mm:ss}] Free GitHub build: license activation skipped.");
        return true;
#else
        var validation = _licenseService.ValidateStoredLicense();
        if (validation.IsValid)
        {
            _importLog.Add($"[{DateTime.Now:HH:mm:ss}] License active.");
            return true;
        }

        while (true)
        {
            var dialog = new LicenseActivationDialog(_licenseService.MachineHash, _licenseService.GetTestLicenseKey())
            {
                Owner = this
            };

            var result = dialog.ShowDialog();
            if (result != true)
            {
                return false;
            }

            var activation = _licenseService.ActivateLicense(dialog.LicenseKey, dialog.EmailOrUser);
            if (activation.IsValid)
            {
                var expiryText = activation.ExpiresUtc.HasValue
                    ? $" Expires {activation.ExpiresUtc.Value:yyyy-MM-dd}."
                    : string.Empty;
                _importLog.Add($"[{DateTime.Now:HH:mm:ss}] License activated.{expiryText}");
                MessageBox.Show(this, $"License activated successfully.{expiryText}", "License Activated", MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return true;
            }

            dialog.ShowValidationError(activation.Message);
            MessageBox.Show(this, activation.Message, "License Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
#endif
    }

    private void LicenseActivation_Click(object sender, RoutedEventArgs e)
    {
#if FREE_GITHUB_BUILD
        MessageBox.Show(this, "This GitHub build does not require license activation.", "Free Build",
            MessageBoxButton.OK, MessageBoxImage.Information);
#else
        var dialog = new LicenseActivationDialog(_licenseService.MachineHash, _licenseService.GetTestLicenseKey())
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var activation = _licenseService.ActivateLicense(dialog.LicenseKey, dialog.EmailOrUser);
        if (!activation.IsValid)
        {
            MessageBox.Show(this, activation.Message, "License Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var expiryText = activation.ExpiresUtc.HasValue
            ? $" Expires {activation.ExpiresUtc.Value:yyyy-MM-dd}."
            : string.Empty;
        _importLog.Add($"[{DateTime.Now:HH:mm:ss}] License updated.{expiryText}");
        MessageBox.Show(this, $"License activated successfully.{expiryText}", "License Activated", MessageBoxButton.OK,
            MessageBoxImage.Information);
#endif
    }

    private void Cv2ActivationTest_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new LicenseActivationDialog(_licenseService.MachineHash, string.Empty)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(dialog.LicenseKey))
        {
            MessageBox.Show(
                this,
                "For CV2 online activation testing, leave License key blank and provide purchaser email only.",
                "CV2 Test",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(dialog.EmailOrUser))
        {
            MessageBox.Show(this, "Purchaser email is required for CV2 online activation test.", "CV2 Test",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var activation = _licenseService.ActivateLicense(string.Empty, dialog.EmailOrUser);
        if (!activation.IsValid)
        {
            MessageBox.Show(this, $"CV2 activation test failed.\n\n{activation.Message}", "CV2 Test Failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var storedValidation = _licenseService.ValidateStoredLicense();
        if (!storedValidation.IsValid)
        {
            MessageBox.Show(this,
                $"CV2 activation succeeded, but stored license validation failed.\n\n{storedValidation.Message}",
                "CV2 Test Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        var expiryText = storedValidation.ExpiresUtc.HasValue
            ? $" Expires {storedValidation.ExpiresUtc.Value:yyyy-MM-dd}."
            : string.Empty;
        _importLog.Add(
            $"[{DateTime.Now:HH:mm:ss}] CV2 activation self-test passed for {dialog.EmailOrUser}. MachineHash={_licenseService.MachineHash}.{expiryText}");

        MessageBox.Show(
            this,
            $"CV2 activation self-test passed.\n\nMachine hash: {_licenseService.MachineHash}\n{storedValidation.Message}{expiryText}\n\nRelaunch Corevanta once to confirm persisted startup validation.",
            "CV2 Test Passed",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void ImportEbFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "EB files (*.EB)|*.EB|All files (*.*)|*.*",
            Multiselect = true,
            Title = "Select one or more EB files"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var previousSnapshot = await _database.GetEntitySnapshotAsync();

        await _database.ClearAllDataAsync(clearChangeManagementHistory: false);
        _entityLookup = null;
        _importLog.Add($"[{DateTime.Now:HH:mm:ss}] Database cleared for fresh EB import.");

        var importedFiles = 0;
        var importedEntities = 0;

        foreach (var file in dialog.FileNames)
        {
            try
            {
                var entities = _parser.ParseFile(file);
                var count = await _database.ImportFileAsync(file, entities);
                importedFiles++;
                importedEntities += count;
                await _database.RecordImportActionAsync(
                    ImportActionEbFile,
                    $"EB: {IOPath.GetFileName(file)}",
                    $"{count} entities",
                    file);
                _importLog.Add($"[{DateTime.Now:HH:mm:ss}] Imported {IOPath.GetFileName(file)} ({count} entities)");
            }
            catch (Exception ex)
            {
                _importLog.Add($"[{DateTime.Now:HH:mm:ss}] ERROR {IOPath.GetFileName(file)}: {ex.Message}");
            }
        }

        if (importedFiles > 0)
        {
            var changeSummary = await _database.RecordEbImportChangeSetAsync(previousSnapshot, importedFiles, importedEntities);
            ChangeManagementSummaryText.Text =
                $"Last EB import: {changeSummary.ChangedCount} changed, {changeSummary.DeletedCount} deleted ({changeSummary.ImportedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}).";
            _importLog.Add(
                $"[{DateTime.Now:HH:mm:ss}] Change Management: changed={changeSummary.ChangedCount}, deleted={changeSummary.DeletedCount}");
        }

        ImportSummaryText.Text = $"Imported {importedFiles} files, {importedEntities} entities.";
        ClImportSummaryText.Text = "CL index cleared. Re-import CL folder.";
        AbPlcImportSummaryText.Text = "AB imports cleared. Re-import AB PLC5 files.";
        AbLogixImportSummaryText.Text = "AB imports cleared. Re-import AB Logix files.";
        ModiconImportSummaryText.Text = "Modicon imports cleared. Re-import Modicon files.";
        SoftConnectionsImportSummaryText.Text = "Soft connections cleared. Re-import soft connections workbook.";
        OpcIntegratorImportSummaryText.Text = "OPC Integrator mappings cleared. Re-import OPCI TSV file.";
        GraphicsPointsImportSummaryText.Text = "HMIWeb graphics import cleared. Re-import HMIWeb graphics workbook.";
        PiImportSummaryText.Text = "PI mappings cleared. Re-import PI workbook.";
        ExportPointsSummaryText.Text = "No point export run yet.";
        await RunSearchAsync();
        await RunClSearchAsync();
        await RunLogicSearchAsync();
        await RunChartSearchAsync();
        await RunAbPlcSearchAsync();
        await RunModiconSearchAsync();
        await RunHardwareSearchAsync();
        await RefreshChangeManagementAsync();
        MarkProjectDirty();
    }

    private async void IndexClFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select CL root folder (contains AMCL and PMCL)",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var count = await _database.ImportClProgramsFromFolderAsync(dialog.FolderName);
            await _database.RecordImportActionAsync(
                ImportActionClFolder,
                $"CL Folder: {IOPath.GetFileName(dialog.FolderName)}",
                $"{count} file(s)",
                dialog.FolderName);
            ClSummaryText.Text = $"Indexed {count} CL files.";
            ClImportSummaryText.Text = $"Indexed {count} CL files.";
            _importLog.Add($"[{DateTime.Now:HH:mm:ss}] Indexed CL folder: {count} files");
            await RunClSearchAsync();
            await RefreshSelectedEntityDetailsAsync();
            await RefreshActiveChartAsync();
            MarkProjectDirty();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to index CL files.\n\n{ex.Message}", "CL Index Error", MessageBoxButton.OK,
            MessageBoxImage.Error);
        }
    }

    private async void ImportAbPlcFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AbPlcImportDialog
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var mappingCount = await _database.ImportAbPlcAsync(
                dialog.PlcName,
                dialog.ConnectionType,
                dialog.HwyNum,
                dialog.HwyBoxNumbers,
                dialog.NodeAddress,
                dialog.SerialHpmNumber,
                dialog.SerialModuleNumber,
                dialog.SerialDevAddr,
                dialog.Plc5FileAddress,
                dialog.SystemFilePath,
                dialog.LadderFiles);

            var plcFileDisplay = string.IsNullOrWhiteSpace(dialog.Plc5FileAddress)
                ? "(from AB_DATA2)"
                : dialog.Plc5FileAddress;
            var abNetworkLabel = GetAbNetworkLabel(dialog.ConnectionType);
            await _database.RecordImportActionAsync(
                ImportActionAbSystem,
                $"AB PLC5: {dialog.PlcName}",
                $"{dialog.ConnectionType}  {abNetworkLabel}:{dialog.HwyNum} BOX:{dialog.HwyBoxNumbersCsv}",
                dialog.PlcName.Trim().ToUpperInvariant());
            AbPlcImportSummaryText.Text =
                $"Imported AB PLC5 {dialog.PlcName}: {abNetworkLabel} {dialog.HwyNum} BOX {dialog.HwyBoxNumbersCsv} NODE {dialog.NodeAddress} FILE {plcFileDisplay} - XPS {dialog.LadderFiles.Count}, mappings {mappingCount}.";
            _importLog.Add($"[{DateTime.Now:HH:mm:ss}] Imported AB PLC5 {dialog.PlcName}: XPS={dialog.LadderFiles.Count}, mappings={mappingCount}");

            await RunAbPlcSearchAsync();
            var selected = _abPlcSystems.FirstOrDefault(
                x => x.PlcName.Equals(dialog.PlcName, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                AbPlcSystemsList.SelectedItem = selected;
            }

            MarkProjectDirty();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to import AB PLC5 import files.\n\n{ex.Message}", "AB PLC5 Import Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void ImportAbLogixFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AbLogixImportDialog
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var mappingCount = await _database.ImportAbPlcAsync(
                dialog.PlcName,
                dialog.ConnectionType,
                dialog.HwyNum,
                dialog.HwyBoxNumbers,
                dialog.NodeAddress,
                dialog.SerialHpmNumber,
                dialog.SerialModuleNumber,
                dialog.SerialDevAddr,
                dialog.Plc5FileAddress,
                dialog.SystemFilePath,
                dialog.LadderFiles);

            var xpsCount = 1 + dialog.LadderFiles.Count; // hardware + program/tags
            var abNetworkLabel = GetAbNetworkLabel(dialog.ConnectionType);
            await _database.RecordImportActionAsync(
                ImportActionAbSystem,
                $"AB Logix: {dialog.PlcName}",
                $"{dialog.ConnectionType}  {abNetworkLabel}:{dialog.HwyNum} BOX:{dialog.HwyBoxNumbersCsv}",
                dialog.PlcName.Trim().ToUpperInvariant());
            AbLogixImportSummaryText.Text =
                $"Imported AB Logix {dialog.PlcName}: {abNetworkLabel} {dialog.HwyNum} BOX {dialog.HwyBoxNumbersCsv} NODE {dialog.NodeAddress} - XPS {xpsCount}, mappings {mappingCount}.";
            _importLog.Add($"[{DateTime.Now:HH:mm:ss}] Imported AB Logix {dialog.PlcName}: XPS={xpsCount}, mappings={mappingCount}");

            await RunAbPlcSearchAsync();
            var selected = _abPlcSystems.FirstOrDefault(
                x => x.PlcName.Equals(dialog.PlcName, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                AbPlcSystemsList.SelectedItem = selected;
            }

            MarkProjectDirty();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to import AB Logix files.\n\n{ex.Message}", "AB Logix Import Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void ImportModiconFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ModiconImportDialog
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var mappingCount = await _database.ImportModiconAsync(
                dialog.PlcName,
                dialog.ConnectionType,
                dialog.HwyNum,
                dialog.HwyBoxNumbers,
                dialog.NodeAddress,
                dialog.SerialHpmNumber,
                dialog.SerialModuleNumber,
                dialog.RegistersFilePath,
                dialog.TrafficCopFilePath,
                dialog.ProgramFiles);

            var nodeText = dialog.NodeAddress.HasValue ? $" NODE {dialog.NodeAddress.Value}" : string.Empty;
            var trafficCopText = string.IsNullOrWhiteSpace(dialog.TrafficCopFilePath) ? string.Empty : " + PLC map file";
            await _database.RecordImportActionAsync(
                ImportActionModiconSystem,
                $"Modicon: {dialog.PlcName}",
                $"{dialog.ConnectionType}  HWY:{dialog.HwyNum} BOX:{dialog.HwyBoxNumbersCsv}{nodeText}",
                dialog.PlcName.Trim().ToUpperInvariant());
            ModiconImportSummaryText.Text =
                $"Imported {dialog.PlcName}: HWY {dialog.HwyNum} BOX {dialog.HwyBoxNumbersCsv}{nodeText} - XPS {dialog.ProgramFiles.Count}, mappings {mappingCount}{trafficCopText}.";
            _importLog.Add($"[{DateTime.Now:HH:mm:ss}] Imported Modicon {dialog.PlcName}: XPS={dialog.ProgramFiles.Count}, mappings={mappingCount}{trafficCopText}");

            await RunModiconSearchAsync();
            var selected = _modiconSystems.FirstOrDefault(
                x => x.PlcName.Equals(dialog.PlcName, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                ModiconSystemsList.SelectedItem = selected;
            }

            MarkProjectDirty();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to import Modicon files.\n\n{ex.Message}", "Modicon Import Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void ImportSoftConnections_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Excel workbook (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|Excel 97-2003 workbook (*.xls)|*.xls|All files (*.*)|*.*",
            Title = "Select soft connections workbook"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var result = await _database.ImportSoftConnectionsAsync(dialog.FileName);
            await _database.RecordImportActionAsync(
                ImportActionSoftConnectionsFile,
                $"Experion Connections: {IOPath.GetFileName(dialog.FileName)}",
                $"{result.ImportedCount} mapped / {result.TotalRows} rows",
                dialog.FileName);
            SoftConnectionsImportSummaryText.Text =
                $"Imported soft connections: {result.ImportedCount} mapped, {result.SkippedCount} skipped from {result.TotalRows} rows.";
            _importLog.Add(
                $"[{DateTime.Now:HH:mm:ss}] Imported soft connections {IOPath.GetFileName(dialog.FileName)}: mapped={result.ImportedCount}, skipped={result.SkippedCount}, rows={result.TotalRows}");

            await RefreshSelectedEntityDetailsAsync();
            await RefreshActiveChartAsync();
            MarkProjectDirty();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to import Experion connections workbook.\n\n{ex.Message}", "Experion Connections Import Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void ImportOpcIntegratorMappings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "TSV files (*.tsv)|*.tsv|Text files (*.txt)|*.txt|All files (*.*)|*.*",
            Title = "Select OPC Integrator online items export (TSV)"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var result = await _database.ImportOpcIntegratorMappingsAsync(dialog.FileName);
            await _database.RecordImportActionAsync(
                ImportActionOpcIntegratorFile,
                $"OPC Integrator: {IOPath.GetFileName(dialog.FileName)}",
                $"{result.ImportedCount} mapped / {result.TotalRows} rows",
                dialog.FileName);
            OpcIntegratorImportSummaryText.Text =
                $"OPC Integrator mappings: {result.ImportedCount} mapped, {result.SkippedCount} skipped from {result.TotalRows} rows.";
            _importLog.Add(
                $"[{DateTime.Now:HH:mm:ss}] Imported OPC Integrator mappings {IOPath.GetFileName(dialog.FileName)}: mapped={result.ImportedCount}, skipped={result.SkippedCount}, rows={result.TotalRows}");

            await RefreshSelectedEntityDetailsAsync();
            await RefreshActiveChartAsync();
            MarkProjectDirty();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to import OPC Integrator mappings.\n\n{ex.Message}", "OPC Integrator Import Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void ImportGraphicsPoints_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Excel workbook (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|Excel 97-2003 workbook (*.xls)|*.xls|All files (*.*)|*.*",
            Title = "Select HMIWeb graphics workbook"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var result = await _database.ImportGraphicsPointsAsync(dialog.FileName);
            await _database.RecordImportActionAsync(
                ImportActionGraphicsFile,
                $"HMIWeb Graphics: {IOPath.GetFileName(dialog.FileName)}",
                $"{result.ImportedCount} mapped / {result.TotalRows} rows",
                dialog.FileName);
            GraphicsPointsImportSummaryText.Text =
                $"HMIWeb graphics import: {result.ImportedCount} mapped, {result.SkippedCount} skipped from {result.TotalRows} rows.";
            _importLog.Add(
                $"[{DateTime.Now:HH:mm:ss}] HMIWeb graphics import {IOPath.GetFileName(dialog.FileName)}: mapped={result.ImportedCount}, skipped={result.SkippedCount}, rows={result.TotalRows}");

            await RefreshSelectedEntityDetailsAsync();
            MarkProjectDirty();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed HMIWeb graphics import.\n\n{ex.Message}", "HMIWeb Graphics Import Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void ImportPiMappings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Excel workbook (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|Excel 97-2003 workbook (*.xls)|*.xls|All files (*.*)|*.*",
            Title = "Select PI workbook"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var result = await _database.ImportPiMappingsAsync(dialog.FileName);
            await _database.RecordImportActionAsync(
                ImportActionPiFile,
                $"PI: {IOPath.GetFileName(dialog.FileName)}",
                $"{result.ImportedCount} mapped / {result.TotalRows} rows",
                dialog.FileName);
            PiImportSummaryText.Text =
                $"PI mappings imported: {result.ImportedCount} mapped, {result.SkippedCount} skipped from {result.TotalRows} rows.";
            _importLog.Add(
                $"[{DateTime.Now:HH:mm:ss}] PI import {IOPath.GetFileName(dialog.FileName)}: mapped={result.ImportedCount}, skipped={result.SkippedCount}, rows={result.TotalRows}");

            await RefreshActiveChartAsync();
            MarkProjectDirty();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed PI import.\n\n{ex.Message}", "PI Import Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void ChartShowPiMappingsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        await RefreshActiveChartAsync();
    }

    private async void UndoImport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var history = await _database.GetImportActionHistoryAsync();
            if (history.Count == 0)
            {
                MessageBox.Show(this, "No import history is available to undo.", "Undo Import", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new ImportUndoDialog(history)
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true || dialog.SelectedAction is null)
            {
                return;
            }

            var selected = dialog.SelectedAction;
            var confirm = MessageBox.Show(
                this,
                $"Undo this import?\n\n{selected.DisplayLabel}",
                "Confirm Undo Import",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            var resultMessage = await _database.UndoImportActionAsync(selected.Id);
            _importLog.Add($"[{DateTime.Now:HH:mm:ss}] {resultMessage}");
            await RunSearchAsync();
            await RunClSearchAsync();
            await RunLogicSearchAsync();
            await RunChartSearchAsync();
            await RunAbPlcSearchAsync();
            await RunModiconSearchAsync();
            await RunHardwareSearchAsync();
            await RefreshImportSummariesAsync();
            MarkProjectDirty();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to undo import.\n\n{ex.Message}", "Undo Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ExportPointDetailsExcel_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Excel workbook (*.xlsx)|*.xlsx|All files (*.*)|*.*",
            DefaultExt = ".xlsx",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"Corevanta_PointDetails_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
            Title = "Export Point Details to Excel"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        ExportPointDetailsButton.IsEnabled = false;
        ExportPointsSummaryText.Text = "Exporting point details...";
        try
        {
            var exportedPointCount = await _database.ExportPointDetailsToExcelAsync(dialog.FileName, excludeInternalChannels: true);
            ExportPointsSummaryText.Text = $"Exported {exportedPointCount} points to {IOPath.GetFileName(dialog.FileName)}.";
            _importLog.Add($"[{DateTime.Now:HH:mm:ss}] Exported point details to {IOPath.GetFileName(dialog.FileName)} ({exportedPointCount} points)");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to export point details.\n\n{ex.Message}", "Point Export Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            ExportPointDetailsButton.IsEnabled = true;
        }
    }

    private async void NewProject_Click(object sender, RoutedEventArgs e)
    {
        if (!await PromptSaveIfDirtyAsync("starting a new project"))
        {
            return;
        }

        await StartNewProjectAsync();
    }

    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (!await PromptSaveIfDirtyAsync("opening another project"))
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "Corevanta Project (*.cvproj)|*.cvproj|SQLite Database (*.db)|*.db|All files (*.*)|*.*",
            Title = "Open Corevanta Project"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await OpenProjectFromFileAsync(dialog.FileName);
    }

    private async void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        await SaveProjectAsync(promptForPath: string.IsNullOrWhiteSpace(GetKnownProjectPath()));
    }

    private void ProjectPassword_Click(object sender, RoutedEventArgs e)
    {
        ConfigureProjectPassword();
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e) => await RunSearchAsync();

    private async void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await RunSearchAsync();
        }
    }

    private async void DetailPointTypeFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingDetailPointTypeFilter)
        {
            return;
        }

        await RunSearchAsync();
    }

    private async void SearchClButton_Click(object sender, RoutedEventArgs e) => await RunClSearchAsync();

    private async void ClSearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await RunClSearchAsync();
        }
    }

    private async void SearchLogicButton_Click(object sender, RoutedEventArgs e) => await RunLogicSearchAsync();

    private async void LogicSearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await RunLogicSearchAsync();
        }
    }

    private async void SearchChartButton_Click(object sender, RoutedEventArgs e) => await RunChartSearchAsync();

    private async void ChartSearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await RunChartSearchAsync();
        }
    }

    private async void SearchHardwareButton_Click(object sender, RoutedEventArgs e) => await RunHardwareSearchAsync();

    private async void SearchAbPlcButton_Click(object sender, RoutedEventArgs e) => await RunAbPlcSearchAsync();
    private async void SearchModiconButton_Click(object sender, RoutedEventArgs e) => await RunModiconSearchAsync();

    private async void HardwareSearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await RunHardwareSearchAsync();
        }
    }

    private async void AbPlcSearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await RunAbPlcSearchAsync();
        }
    }

    private async void ModiconSearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await RunModiconSearchAsync();
        }
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || e.Key != Key.Z)
        {
            return;
        }

        if (Keyboard.FocusedElement is TextBoxBase || Keyboard.FocusedElement is PasswordBox)
        {
            return;
        }

        if (!TryUndoLastDiagramDrag())
        {
            return;
        }

        e.Handled = true;
    }

    private async void ChartExpandUpstream_Click(object sender, RoutedEventArgs e)
    {
        _chartUpstreamDepth = Math.Min(8, _chartUpstreamDepth + 1);
        ClearChartDirectionalExpansionSelection();
        await RefreshActiveChartAsync();
    }

    private async void ChartMinimizeUpstream_Click(object sender, RoutedEventArgs e)
    {
        _chartUpstreamDepth = Math.Max(0, _chartUpstreamDepth - 1);
        ClearChartDirectionalExpansionSelection();
        await RefreshActiveChartAsync();
    }

    private async void ChartExpandDownstream_Click(object sender, RoutedEventArgs e)
    {
        _chartDownstreamDepth = Math.Min(8, _chartDownstreamDepth + 1);
        ClearChartDirectionalExpansionSelection();
        await RefreshActiveChartAsync();
    }

    private async void ChartMinimizeDownstream_Click(object sender, RoutedEventArgs e)
    {
        _chartDownstreamDepth = Math.Max(0, _chartDownstreamDepth - 1);
        ClearChartDirectionalExpansionSelection();
        await RefreshActiveChartAsync();
    }

    private async void ChartResetDepth_Click(object sender, RoutedEventArgs e)
    {
        _chartUpstreamDepth = 1;
        _chartDownstreamDepth = 1;
        ClearChartDirectionalExpansionSelection();
        await RefreshActiveChartAsync();
    }

    private void ClFindNextButton_Click(object sender, RoutedEventArgs e) => FindInCurrentClText(forward: true);

    private void ClFindPreviousButton_Click(object sender, RoutedEventArgs e) => FindInCurrentClText(forward: false);

    private void ClFindTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _clFindQuery = string.Empty;
        _clFindLastIndex = -1;
        ClFindStatusText.Text = string.IsNullOrWhiteSpace(ClFindTextBox.Text)
            ? "Find: enter text"
            : "Find: ready";
    }

    private void ClFindTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        var forward = (Keyboard.Modifiers & ModifierKeys.Shift) == 0;
        FindInCurrentClText(forward);
    }

    private async void EntityResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EntityResultsList.SelectedItem is not EntitySearchResult selected)
        {
            _selectedDetailEntityId = null;
            _propertyRows.Clear();
            return;
        }

        _selectedDetailEntityId = selected.EntityId;
        await LoadEntityDetailsAsync(selected.EntityId);
    }

    private async void ClResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ClResultsList.SelectedItem is ClProgramListItem selected)
        {
            await LoadClProgramAsync(selected.ClProgramId);
        }
    }

    private async void LogicResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LogicResultsList.SelectedItem is LogicBlockSummary selected)
        {
            await LoadLogicBlockAsync(selected.EntityId);
            return;
        }

        _activeLogicEntityId = null;
    }

    private async void ChartResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ChartResultsList.SelectedItem is EntitySearchResult selected)
        {
            try
            {
                await LoadChartAsync(selected.EntityId);
            }
            catch (Exception ex)
            {
                _importLog.Add($"[{DateTime.Now:HH:mm:ss}] ERROR chart select {selected.DisplayName}: {ex.Message}");
                _importLog.Add($"[{DateTime.Now:HH:mm:ss}] ERROR chart select detail: {ex}");
                ChartHeaderText.Text = $"Chart load failed for {selected.DisplayName}. See Import log.";
                ClearChartDiagram();
            }
        }
    }

    private async void HardwareResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HardwareResultsList.SelectedItem is HardwareBoxSummary selected)
        {
            if (selected.EntityType.Equals("HWY_BOX", StringComparison.OrdinalIgnoreCase)
                && selected.HighwayNumber is int hwyNum
                && selected.BoxNumber is int boxNum
                && hwyNum > 0
                && boxNum > 0)
            {
                await LoadHardwareHwyBoxAsync(selected, hwyNum, boxNum);
            }
            else
            {
                await LoadHardwareBoxAsync(selected.BoxEntityId);
            }
            return;
        }

        _activeHardwareBoxDetail = null;
        _activeHardwareModule = null;
        _hardwareAssignedPlcNodes.Clear();
        _hardwareTreeNodes.Clear();
        _hardwarePoints.Clear();
        _hardwareTrafficCopEntries.Clear();
        _hardwarePlcPoints.Clear();
        HardwareHeaderText.Text = "No hardware box selected.";
        HardwareTrafficCopHeaderText.Text = "PLC Mapping: no hardware box selected.";
    }

    private async void AbPlcSystemsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AbPlcSystemsList.SelectedItem is AbPlcSummary selected)
        {
            await LoadAbPlcSystemAsync(selected.PlcSystemId);
            return;
        }

        _activeAbPlcSystemId = null;
        _abPlcLadderFiles.Clear();
        AbPlcHeaderText.Text = "No AB import selected.";
        AbPlcViewerHeaderText.Text = "Select one or more XPS files to view ladder logic.";
        ClearAbPlcXpsDocument();
    }

    private void AbPlcFilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressAbPlcFileSelectionDisplay)
        {
            return;
        }

        if (AbPlcFilesList.SelectedItems.Count == 0)
        {
            AbPlcViewerHeaderText.Text = "Select one or more XPS files to view ladder logic.";
            ClearAbPlcXpsDocument();
            return;
        }

        if (AbPlcFilesList.SelectedItems[0] is AbPlcLadderFile selectedFile)
        {
            DisplayAbPlcXps(selectedFile);
        }
    }

    private async void ModiconSystemsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModiconSystemsList.SelectedItem is ModiconSummary selected)
        {
            await LoadModiconSystemAsync(selected.ModiconSystemId);
            if (_activeHardwareBoxDetail is not null)
            {
                var assignedAbSystems = GetAssignedAbPlcSystemsForHardwareBox(_activeHardwareBoxDetail);
                var assignedModiconSystems = GetAssignedModiconSystemsForHardwareBox(_activeHardwareBoxDetail);
                await RefreshHardwareAssignedPlcListAsync(assignedAbSystems, assignedModiconSystems);
                await BuildHardwareTreeNodesAsync(_activeHardwareBoxDetail, assignedAbSystems, assignedModiconSystems);
            }
            ClearHardwareTrafficCopPanel();
            return;
        }

        _activeModiconSystemId = null;
        _modiconProgramFiles.Clear();
        ModiconHeaderText.Text = "No Modicon PLC selected.";
        ModiconViewerHeaderText.Text = "Select one or more XPS files to view ladder logic.";
        ClearModiconXpsDocument();
        if (_activeHardwareBoxDetail is not null)
        {
            var assignedAbSystems = GetAssignedAbPlcSystemsForHardwareBox(_activeHardwareBoxDetail);
            var assignedModiconSystems = GetAssignedModiconSystemsForHardwareBox(_activeHardwareBoxDetail);
            await RefreshHardwareAssignedPlcListAsync(assignedAbSystems, assignedModiconSystems);
            await BuildHardwareTreeNodesAsync(_activeHardwareBoxDetail, assignedAbSystems, assignedModiconSystems);
        }
        ClearHardwareTrafficCopPanel();
    }

    private void ModiconFilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressModiconFileSelectionDisplay)
        {
            return;
        }

        if (ModiconFilesList.SelectedItems.Count == 0)
        {
            ModiconViewerHeaderText.Text = "Select one or more XPS files to view ladder logic.";
            ClearModiconXpsDocument();
            return;
        }

        if (ModiconFilesList.SelectedItems[0] is ModiconProgramFile selectedFile)
        {
            DisplayModiconXps(selectedFile);
        }
    }

    private async void HardwareTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not HardwareTreeNode node)
        {
            return;
        }

        if (node.IsModule && node.Module is not null)
        {
            await LoadHardwareModulePointsAsync(node.Module);
            return;
        }

        if (node.IsModiconTrafficNode)
        {
            await LoadHardwareTrafficCopEntriesForNodeSelectionAsync(
                node.ModiconDropNumber,
                node.ModiconSlotNumber,
                node.ModiconSystemId);
            return;
        }

        if (node.IsAbPlcNode && node.AbPlcSystemId.HasValue)
        {
            await LoadAbHardwareEntriesForNodeSelectionAsync(node.AbPlcSystemId.Value);
            return;
        }

        ClearHardwareTrafficCopPanel();
    }

    private async void HardwareAssignedPlcTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not HardwareTreeNode node)
        {
            return;
        }

        if (node.IsAbPlcNode && node.AbPlcSystemId.HasValue)
        {
            await LoadAbHardwareEntriesForNodeSelectionAsync(node.AbPlcSystemId.Value);
            return;
        }

        if (!node.IsModiconTrafficNode)
        {
            return;
        }

        await LoadHardwareTrafficCopEntriesForNodeSelectionAsync(
            node.ModiconDropNumber,
            node.ModiconSlotNumber,
            node.ModiconSystemId);
    }

    private async void HardwarePointChartLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Hyperlink hyperlink
            || !TryParseEntityId(hyperlink.Tag, out var entityId)
            || entityId <= 0)
        {
            return;
        }

        await OpenChartEntityAsync(entityId);
    }

    private async void HardwarePointDetailLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Hyperlink hyperlink
            || !TryParseEntityId(hyperlink.Tag, out var entityId)
            || entityId <= 0)
        {
            return;
        }

        await OpenDetailEntityAsync(entityId);
    }

    private async void HardwareAddressAbLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Hyperlink hyperlink)
        {
            return;
        }

        try
        {
            var address = ExtractPrimaryAddressToken(hyperlink.Tag?.ToString() ?? string.Empty);
            if (string.IsNullOrWhiteSpace(address))
            {
                return;
            }

            await OpenHardwareAddressReferenceAsync(address, "AB");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to open AB address link.\n\n{ex.Message}", "Address Link Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void HardwareAddressModiconLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Hyperlink hyperlink)
        {
            return;
        }

        try
        {
            var address = ExtractPrimaryAddressToken(hyperlink.Tag?.ToString() ?? string.Empty);
            if (string.IsNullOrWhiteSpace(address))
            {
                return;
            }

            await OpenHardwareAddressReferenceAsync(address, "MODICON");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to open Modicon address link.\n\n{ex.Message}", "Address Link Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task OpenHardwareAddressReferenceAsync(string registerAddress, string interfaceType)
    {
        var references = (await _database.GetPlcReferencesByRegisterAddressAsync(registerAddress, interfaceType))
            .ToList();

        if (_activeHardwareBoxDetail is not null)
        {
            HashSet<long> allowedSystemIds;
            if (interfaceType.Equals("AB", StringComparison.OrdinalIgnoreCase))
            {
                allowedSystemIds = GetAssignedAbPlcSystemsForHardwareBox(_activeHardwareBoxDetail)
                    .Select(system => system.PlcSystemId)
                    .ToHashSet();
            }
            else
            {
                allowedSystemIds = GetAssignedModiconSystemsForHardwareBox(_activeHardwareBoxDetail)
                    .Select(system => system.ModiconSystemId)
                    .ToHashSet();
            }

            if (allowedSystemIds.Count > 0)
            {
                references = references
                    .Where(reference => allowedSystemIds.Contains(reference.PlcSystemId))
                    .ToList();
            }
        }

        references = references
            .OrderBy(reference => TryExtractSerialArrayElement(reference.SourceLine, out _, out var elementIndex) ? elementIndex : int.MaxValue)
            .ThenBy(reference => reference.PlcName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reference => reference.SourceLine, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (references.Count == 0)
        {
            MessageBox.Show(
                this,
                $"No PLC reference found for {registerAddress} in the PLC systems assigned to this hardware box.",
                "Open PLC Reference",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var opened = await OpenPlcReferenceInXpsAsync(references, referenceIndex: 0);
        if (!opened)
        {
            MessageBox.Show(
                this,
                $"Could not open PLC XPS for {registerAddress}.",
                "Open PLC Reference",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (IsAbPlcReference(references[0]))
        {
            var rawFindTerm = string.IsNullOrWhiteSpace(references[0].RegisterAddress)
                ? registerAddress
                : references[0].RegisterAddress;
            var findTerm = BuildXpsFindTermForRegisterAddress(rawFindTerm);
            AbPlcXpsSearchBox.Text = findTerm;
            SeedNativeFindTerm(AbPlcXpsViewer, findTerm);
        }
        else
        {
            var rawFindTerm = string.IsNullOrWhiteSpace(references[0].RegisterAddress)
                ? registerAddress
                : references[0].RegisterAddress;
            var findTerm = BuildXpsFindTermForRegisterAddress(rawFindTerm);
            ModiconXpsSearchBox.Text = findTerm;
            SeedNativeFindTerm(ModiconXpsViewer, findTerm);
        }
    }

    private async void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, MainTabControl))
        {
            return;
        }

        ResetDiagramInteractionState();

        if (MainTabControl.SelectedItem is not TabItem selectedTab)
        {
            return;
        }

        if (selectedTab.Header is not string header)
        {
            return;
        }

        var previousHeader = _lastMainTabHeader;
        _lastMainTabHeader = header;

        if (header.Equals("Detail", StringComparison.Ordinal))
        {
            if (string.Equals(previousHeader, "Chart", StringComparison.Ordinal))
            {
                await SyncChartSelectionToDetailAsync();
            }

            return;
        }

        if (header.Equals("Chart", StringComparison.Ordinal))
        {
            if (string.Equals(previousHeader, "Logic Block", StringComparison.Ordinal))
            {
                return;
            }

            var contextEntityId = GetSelectedEntityIdForTab(previousHeader);
            if (contextEntityId is long selectedEntityId)
            {
                await SyncEntitySelectionToChartAsync(selectedEntityId);
            }
            else if (_activeChartEntityId is null)
            {
                await SyncDetailSelectionToChartAsync();
            }
            return;
        }

        if (header.Equals("Logic Block", StringComparison.Ordinal))
        {
            return;
        }
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        ResetDiagramInteractionState();
    }

    private async void PropertyLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Hyperlink hyperlink)
        {
            return;
        }

        var token = hyperlink.Tag?.ToString();
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var parts = token.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return;
        }

        if (!long.TryParse(parts[1], out var id))
        {
            return;
        }

        if (parts[0].Equals("CL", StringComparison.OrdinalIgnoreCase))
        {
            await OpenClProgramAsync(id, switchToClTab: true);
            return;
        }

        if (parts[0].Equals("LB", StringComparison.OrdinalIgnoreCase))
        {
            await OpenLogicBlockAsync(id, switchToLogicTab: true);
            return;
        }

        if (parts[0].Equals("EN", StringComparison.OrdinalIgnoreCase))
        {
            await OpenDetailEntityAsync(id);
            return;
        }

        if (parts[0].Equals("HW", StringComparison.OrdinalIgnoreCase))
        {
            int? moduleNumber = null;
            long? pointEntityId = null;
            if (parts.Length > 2 && int.TryParse(parts[2], out var parsedModule) && parsedModule > 0)
            {
                moduleNumber = parsedModule;
            }

            if (parts.Length > 3 && long.TryParse(parts[3], out var parsedPointEntityId) && parsedPointEntityId > 0)
            {
                pointEntityId = parsedPointEntityId;
            }

            await OpenHardwareLinkAsync(id, moduleNumber, pointEntityId);
            return;
        }

        if (parts[0].Equals("PLCR", StringComparison.OrdinalIgnoreCase))
        {
            await OpenPlcRungsFromDetailAsync(id);
        }
    }

    private async Task RunSearchAsync()
    {
        await RefreshDetailPointTypeFiltersAsync();

        var selectedTypeFilter = GetSelectedDetailPointTypeFilter();
        var results = await _database.SearchEntitiesAsync(
            SearchTextBox.Text,
            maxResults: 5000,
            excludeInternalChannels: true,
            entityTypeFilter: selectedTypeFilter);

        _searchResults.Clear();
        foreach (var result in results)
        {
            _searchResults.Add(result);
        }

        SearchSummaryText.Text = $"{results.Count} matches";

        if (_searchResults.Count > 0)
        {
            EntityResultsList.SelectedIndex = 0;
        }
        else
        {
            _propertyRows.Clear();
        }
    }

    private string? GetSelectedDetailPointTypeFilter()
    {
        if (DetailPointTypeFilterComboBox.SelectedItem is not string selected)
        {
            return null;
        }

        selected = selected.Trim();
        if (string.IsNullOrWhiteSpace(selected)
            || selected.Equals(AllDetailPointTypesOption, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return selected;
    }

    private async Task RefreshDetailPointTypeFiltersAsync()
    {
        var currentSelection = DetailPointTypeFilterComboBox.SelectedItem as string;
        var types = await _database.GetSearchableEntityTypesAsync(excludeInternalChannels: true);

        _isUpdatingDetailPointTypeFilter = true;
        try
        {
            _detailPointTypeFilters.Clear();
            _detailPointTypeFilters.Add(AllDetailPointTypesOption);
            foreach (var type in types)
            {
                _detailPointTypeFilters.Add(type);
            }

            if (!string.IsNullOrWhiteSpace(currentSelection)
                && _detailPointTypeFilters.Contains(currentSelection))
            {
                DetailPointTypeFilterComboBox.SelectedItem = currentSelection;
            }
            else
            {
                DetailPointTypeFilterComboBox.SelectedItem = AllDetailPointTypesOption;
            }
        }
        finally
        {
            _isUpdatingDetailPointTypeFilter = false;
        }
    }

    private async Task RunClSearchAsync()
    {
        var results = await _database.SearchClProgramsAsync(ClSearchTextBox.Text);

        _clResults.Clear();
        foreach (var item in results)
        {
            _clResults.Add(item);
        }

        ClSummaryText.Text = $"{results.Count} CL programs";

        if (_clResults.Count > 0)
        {
            if (ClResultsList.SelectedItem is not ClProgramListItem current
                || !_clResults.Any(x => x.ClProgramId == current.ClProgramId))
            {
                ClResultsList.SelectedIndex = 0;
            }
        }
        else
        {
            ClProgramHeaderText.Text = "No CL programs found.";
            ClTextViewer.Text = string.Empty;
            _clFindQuery = string.Empty;
            _clFindLastIndex = -1;
            ClFindStatusText.Text = "Find: no CL text loaded";
        }
    }

    private void FindInCurrentClText(bool forward)
    {
        var query = (ClFindTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            ClFindStatusText.Text = "Find: enter text";
            return;
        }

        var text = ClTextViewer.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            ClFindStatusText.Text = "Find: no CL text loaded";
            return;
        }

        var sameQuery = query.Equals(_clFindQuery, StringComparison.OrdinalIgnoreCase);
        var comparison = StringComparison.OrdinalIgnoreCase;
        int index;

        if (forward)
        {
            var startIndex = sameQuery && _clFindLastIndex >= 0
                ? Math.Min(text.Length, _clFindLastIndex + Math.Max(1, query.Length))
                : 0;
            index = text.IndexOf(query, startIndex, comparison);
            if (index < 0 && startIndex > 0)
            {
                index = text.IndexOf(query, 0, comparison);
            }
        }
        else
        {
            var startIndex = sameQuery && _clFindLastIndex >= 0
                ? Math.Max(0, _clFindLastIndex - 1)
                : text.Length - 1;
            index = text.LastIndexOf(query, startIndex, comparison);
            if (index < 0 && startIndex < text.Length - 1)
            {
                index = text.LastIndexOf(query, text.Length - 1, comparison);
            }
        }

        if (index < 0)
        {
            _clFindQuery = query;
            _clFindLastIndex = -1;
            ClFindStatusText.Text = $"Find: '{query}' not found";
            return;
        }

        _clFindQuery = query;
        _clFindLastIndex = index;
        ClTextViewer.Focus();
        ClTextViewer.Select(index, query.Length);
        var lineIndex = ClTextViewer.GetLineIndexFromCharacterIndex(index);
        if (lineIndex >= 0)
        {
            ClTextViewer.ScrollToLine(lineIndex);
        }

        ClFindStatusText.Text = $"Find: line {lineIndex + 1}";
    }

    private async Task RunLogicSearchAsync()
    {
        var results = await _database.SearchLogicBlocksAsync(LogicSearchTextBox.Text);

        _logicResults.Clear();
        foreach (var item in results)
        {
            _logicResults.Add(item);
        }

        LogicSummaryText.Text = $"{results.Count} logic blocks";

        if (_logicResults.Count > 0)
        {
            var desiredEntityId = _activeLogicEntityId
                                  ?? (_selectedDetailEntityId is long detailId
                                      && _logicResults.Any(x => x.EntityId == detailId)
                                      ? detailId
                                      : (long?)null);
            if (desiredEntityId.HasValue)
            {
                var desired = _logicResults.FirstOrDefault(x => x.EntityId == desiredEntityId.Value);
                LogicResultsList.SelectedItem = desired;
            }
            else if (LogicResultsList.SelectedItem is LogicBlockSummary current
                     && !_logicResults.Any(x => x.EntityId == current.EntityId))
            {
                LogicResultsList.SelectedItem = null;
                _activeLogicEntityId = null;
                LogicHeaderText.Text = "Select a logic block to view diagram.";
                ClearLogicDiagram();
            }
        }
        else
        {
            _activeLogicEntityId = null;
            LogicHeaderText.Text = "No logic blocks found.";
            ClearLogicDiagram();
        }
    }

    private async Task RunChartSearchAsync()
    {
        var results = await _database.SearchEntitiesAsync(ChartSearchTextBox.Text, maxResults: 5000, excludeInternalChannels: true);

        _chartResults.Clear();
        foreach (var item in results)
        {
            _chartResults.Add(item);
        }

        ChartSummaryText.Text = $"{results.Count} matches";

        if (_chartResults.Count > 0)
        {
            if (ChartResultsList.SelectedItem is not EntitySearchResult current
                || !_chartResults.Any(x => x.EntityId == current.EntityId))
            {
                ChartResultsList.SelectedIndex = 0;
            }
        }
        else
        {
            _activeChartEntityId = null;
            ChartHeaderText.Text = "No entities found.";
            ClearChartDiagram();
        }
    }

    private async Task RunHardwareSearchAsync()
    {
        var results = (await _database.SearchHardwareBoxesAsync(HardwareSearchTextBox.Text)).ToList();
        var hwyBoxGroups = await _database.SearchHardwareHwyBoxGroupsAsync(HardwareSearchTextBox.Text);
        var existingPairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var box in results)
        {
            if (box.HighwayNumber is int hwy && hwy > 0
                && box.BoxNumber is int bx && bx > 0)
            {
                existingPairs.Add($"{hwy:0000}|{bx:0000}");
            }
        }

        foreach (var group in hwyBoxGroups)
        {
            if (group.HighwayNumber is not int hwy || hwy <= 0
                || group.BoxNumber is not int bx || bx <= 0)
            {
                continue;
            }

            var key = $"{hwy:0000}|{bx:0000}";
            if (existingPairs.Contains(key))
            {
                continue;
            }

            results.Add(group);
            existingPairs.Add(key);
        }

        results = results
            .OrderBy(x => x.HighwayNumber ?? int.MaxValue)
            .ThenBy(x => x.BoxNumber ?? int.MaxValue)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _hardwareBoxes.Clear();
        foreach (var item in results)
        {
            _hardwareBoxes.Add(item);
        }

        HardwareSummaryText.Text = $"{results.Count} hardware boxes";

        if (_hardwareBoxes.Count > 0)
        {
            if (HardwareResultsList.SelectedItem is not HardwareBoxSummary current
                || !_hardwareBoxes.Any(x => x.BoxEntityId == current.BoxEntityId))
            {
                HardwareResultsList.SelectedIndex = 0;
            }
        }
        else
        {
            _activeHardwareBoxDetail = null;
            _activeHardwareModule = null;
            _hardwareAssignedPlcNodes.Clear();
            _hardwareTreeNodes.Clear();
            _hardwarePoints.Clear();
            _hardwareTrafficCopEntries.Clear();
            _hardwarePlcPoints.Clear();
            HardwareHeaderText.Text = "No hardware boxes found.";
            HardwareTrafficCopHeaderText.Text = "PLC Mapping: no hardware box selected.";
        }
    }

    private async Task LoadHardwareHwyBoxAsync(HardwareBoxSummary selected, int hwyNum, int boxNum)
    {
        _activeHardwareBoxDetail = null;
        _activeHardwareModule = null;
        _hardwareAssignedPlcNodes.Clear();
        _hardwareTrafficCopEntries.Clear();
        _hardwarePlcPoints.Clear();
        _hardwareTreeNodes.Clear();

        var points = await _database.GetHardwarePointsForHwyBoxAsync(hwyNum, boxNum);
        ApplyHardwareAddressLinkAvailability(points);
        _hardwarePoints.Clear();
        foreach (var point in points)
        {
            _hardwarePoints.Add(point);
        }

        HardwareHeaderText.Text =
            $"{selected.Name} [HWY_BOX]  HWY:{hwyNum:00} BOX:{boxNum:00}  Tags: {points.Count}";
        HardwareTrafficCopHeaderText.Text = "PLC Mapping: import Modicon and AB PLC5/Logix files to enable PLC module view.";

        var root = new HardwareTreeNode
        {
            Key = $"HWYBOX:{hwyNum:00}:{boxNum:00}",
            Title = $"{selected.Name}  ({points.Count} tags)",
            IsExpanded = true
        };
        root.Children.Add(new HardwareTreeNode
        {
            Key = $"HWYBOX:{hwyNum:00}:{boxNum:00}:TAGS",
            Title = "Tag list (derived from EB point properties)"
        });
        _hardwareTreeNodes.Add(root);
    }

    private async Task RunAbPlcSearchAsync()
    {
        var results = await _database.SearchAbPlcSystemsAsync(AbPlcSearchTextBox.Text);

        _abPlcSystems.Clear();
        foreach (var item in results)
        {
            _abPlcSystems.Add(item);
        }

        AbPlcSummaryText.Text = string.Empty;

        if (_abPlcSystems.Count > 0)
        {
            var selected =
                _activeAbPlcSystemId.HasValue
                    ? _abPlcSystems.FirstOrDefault(x => x.PlcSystemId == _activeAbPlcSystemId.Value)
                    : null;

            AbPlcSystemsList.SelectedItem = selected ?? _abPlcSystems[0];
        }
        else
        {
            _activeAbPlcSystemId = null;
            _abPlcLadderFiles.Clear();
            AbPlcHeaderText.Text = "No AB imports found.";
            AbPlcViewerHeaderText.Text = "Select one or more XPS files to view ladder logic.";
            ClearAbPlcXpsDocument();
        }
    }

    private async Task RunModiconSearchAsync()
    {
        var results = await _database.SearchModiconSystemsAsync(ModiconSearchTextBox.Text);

        _modiconSystems.Clear();
        foreach (var item in results)
        {
            _modiconSystems.Add(item);
        }

        ModiconSummaryText.Text = $"{results.Count} Modicon system(s)";

        if (_modiconSystems.Count > 0)
        {
            var selected =
                _activeModiconSystemId.HasValue
                    ? _modiconSystems.FirstOrDefault(x => x.ModiconSystemId == _activeModiconSystemId.Value)
                    : null;

            ModiconSystemsList.SelectedItem = selected ?? _modiconSystems[0];
        }
        else
        {
            _activeModiconSystemId = null;
            _modiconProgramFiles.Clear();
            ModiconHeaderText.Text = "No Modicon systems found.";
            ModiconViewerHeaderText.Text = "Select one or more XPS files to view ladder logic.";
            ClearModiconXpsDocument();
        }
    }

    private async Task RefreshActiveChartAsync()
    {
        UpdateChartDepthText();
        if (_activeChartEntityId is long entityId)
        {
            try
            {
                await LoadChartAsync(entityId);
            }
            catch (Exception ex)
            {
                _importLog.Add($"[{DateTime.Now:HH:mm:ss}] ERROR chart refresh {entityId}: {ex.Message}");
                _importLog.Add($"[{DateTime.Now:HH:mm:ss}] ERROR chart refresh detail: {ex}");
                ChartHeaderText.Text = "Chart refresh failed. See Import log.";
                ClearChartDiagram();
            }
        }
    }

    private void UpdateChartDepthText()
    {
        ChartDepthText.Text = $"U:{_chartUpstreamDepth} D:{_chartDownstreamDepth}";
    }

    private long? GetSelectedEntityIdForTab(string? tabHeader)
    {
        if (string.Equals(tabHeader, "Detail", StringComparison.Ordinal))
        {
            return _selectedDetailEntityId;
        }

        if (string.Equals(tabHeader, "Chart", StringComparison.Ordinal))
        {
            return _activeChartEntityId;
        }

        if (string.Equals(tabHeader, "Logic Block", StringComparison.Ordinal))
        {
            return _activeLogicEntityId;
        }

        return null;
    }

    private async Task SyncEntitySelectionToChartAsync(long selectedEntityId)
    {
        if (selectedEntityId <= 0)
        {
            return;
        }

        if (ChartResultsList.SelectedItem is EntitySearchResult selected
            && selected.EntityId == selectedEntityId)
        {
            return;
        }

        var match = _chartResults.FirstOrDefault(x => x.EntityId == selectedEntityId);
        if (match is not null)
        {
            ChartResultsList.SelectedItem = match;
            return;
        }

        var entity = await _database.GetEntitySearchResultByIdAsync(selectedEntityId);
        if (entity is not null)
        {
            match = _chartResults.FirstOrDefault(x => x.EntityId == selectedEntityId);
            if (match is null)
            {
                _chartResults.Insert(0, entity);
                ChartSummaryText.Text = $"{_chartResults.Count} matches";
                match = entity;
            }

            ChartResultsList.SelectedItem = match;
            return;
        }

        await LoadChartAsync(selectedEntityId);
    }

    private async Task SyncDetailSelectionToChartAsync()
    {
        if (_selectedDetailEntityId is not long selectedEntityId)
        {
            return;
        }

        await SyncEntitySelectionToChartAsync(selectedEntityId);
    }

    private async Task SyncEntitySelectionToDetailAsync(long selectedEntityId)
    {
        if (selectedEntityId <= 0)
        {
            return;
        }

        if (EntityResultsList.SelectedItem is EntitySearchResult selected
            && selected.EntityId == selectedEntityId)
        {
            return;
        }

        var match = _searchResults.FirstOrDefault(x => x.EntityId == selectedEntityId);
        if (match is not null)
        {
            EntityResultsList.SelectedItem = match;
            return;
        }

        var entity = await _database.GetEntitySearchResultByIdAsync(selectedEntityId);
        if (entity is not null)
        {
            match = _searchResults.FirstOrDefault(x => x.EntityId == selectedEntityId);
            if (match is null)
            {
                _searchResults.Insert(0, entity);
                SearchSummaryText.Text = $"{_searchResults.Count} matches";
                match = entity;
            }

            EntityResultsList.SelectedItem = match;
            return;
        }

        EntityResultsList.SelectedItem = null;
        _selectedDetailEntityId = selectedEntityId;
        await LoadEntityDetailsAsync(selectedEntityId);
    }

    private async Task SyncChartSelectionToDetailAsync()
    {
        if (_activeChartEntityId is not long selectedEntityId)
        {
            return;
        }

        await SyncEntitySelectionToDetailAsync(selectedEntityId);
    }

    private async Task SyncLogicSelectionToDetailAsync()
    {
        if (_activeLogicEntityId is not long selectedEntityId)
        {
            return;
        }

        await SyncEntitySelectionToDetailAsync(selectedEntityId);
    }

    private async Task SyncEntitySelectionToLogicAsync(long selectedEntityId)
    {
        if (selectedEntityId <= 0)
        {
            return;
        }

        if (LogicResultsList.SelectedItem is LogicBlockSummary selected
            && selected.EntityId == selectedEntityId)
        {
            return;
        }

        var match = _logicResults.FirstOrDefault(x => x.EntityId == selectedEntityId);
        if (match is not null)
        {
            LogicResultsList.SelectedItem = match;
            return;
        }

        var entity = await _database.GetEntitySearchResultByIdAsync(selectedEntityId);
        if (entity is null
            || !string.Equals(entity.EntityType, "LOGICNIM", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        LogicSearchTextBox.Text = entity.DisplayName;
        await RunLogicSearchAsync();

        match = _logicResults.FirstOrDefault(x => x.EntityId == selectedEntityId);
        if (match is not null)
        {
            LogicResultsList.SelectedItem = match;
            return;
        }

        await LoadLogicBlockAsync(selectedEntityId);
    }

    private async Task SyncDetailSelectionToLogicAsync()
    {
        if (_selectedDetailEntityId is not long selectedEntityId)
        {
            return;
        }

        await SyncEntitySelectionToLogicAsync(selectedEntityId);
    }

    private async Task LoadEntityDetailsAsync(long entityId)
    {
        var details = await _database.GetEntityPropertiesAsync(entityId);

        _propertyRows.Clear();
        foreach (var row in details)
        {
            _propertyRows.Add(row);
        }

        var chartDetail = await _database.GetChartDetailAsync(entityId, includeClReferences: false);
        var plcReferences = GetDistinctPlcRungReferences(chartDetail?.PlcReferences ?? []);
        if (plcReferences.Count > 0)
        {
            var abCount = plcReferences.Count(x => IsAbPlcReference(x));
            var modiconCount = plcReferences.Count - abCount;
            var scopeText = modiconCount > 0
                ? $"{abCount} AB, {modiconCount} Modicon"
                : $"{abCount} AB";

            _propertyRows.Add(new PropertyRow
            {
                Property = "PLC Rungs",
                Value = $"{plcReferences.Count} reference(s) ({scopeText})",
                IsLink = true,
                LinkToken = $"PLCR:{entityId}"
            });
        }
    }

    private async Task OpenPlcRungsFromDetailAsync(long entityId)
    {
        var detail = await _database.GetChartDetailAsync(entityId, includeClReferences: false);
        if (detail is null)
        {
            MessageBox.Show(this, "Entity is no longer available.", "PLC Rungs", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var references = GetDistinctPlcRungReferences(detail.PlcReferences);
        if (references.Count == 0)
        {
            MessageBox.Show(this, "No PLC rung references were found for this point.", "PLC Rungs", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var opened = await OpenPlcReferenceInXpsAsync(references, referenceIndex: 0);
        if (!opened)
        {
            MessageBox.Show(
                this,
                "Could not open the first PLC reference in XPS (system/file may be missing).",
                "PLC Rungs",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private async Task LoadClProgramAsync(long clProgramId)
    {
        var detail = await _database.GetClProgramAsync(clProgramId);
        if (detail is null)
        {
            ClProgramHeaderText.Text = "CL program no longer available.";
            ClTextViewer.Text = string.Empty;
            _clFindQuery = string.Empty;
            _clFindLastIndex = -1;
            ClFindStatusText.Text = "Find: no CL text loaded";
            return;
        }

        ClProgramHeaderText.Text = $"{detail.ProgramName} [{detail.ProgramType}]  {detail.FilePath}";
        ClTextViewer.Text = detail.FileText;
        ClTextViewer.ScrollToHome();
        _clFindQuery = string.Empty;
        _clFindLastIndex = -1;
        ClFindStatusText.Text = string.IsNullOrWhiteSpace(ClFindTextBox.Text)
            ? "Find: enter text"
            : "Find: ready";
    }

    private async Task LoadLogicBlockAsync(long entityId)
    {
        var detail = await _database.GetLogicBlockDetailAsync(entityId);
        if (detail is null)
        {
            _activeLogicEntityId = null;
            LogicHeaderText.Text = "Logic block no longer available.";
            ClearLogicDiagram();
            return;
        }

        var savedLayout = await _database.GetLogicLayoutAsync(entityId);
        _activeLogicEntityId = entityId;
        _selectedDetailEntityId = entityId;
        LogicHeaderText.Text = $"{detail.Name}  {detail.Description}  [{detail.SourceFile}]";
        RenderLogicDiagram(detail, savedLayout);
    }

    private async Task LoadChartAsync(long entityId)
    {
        var requestVersion = System.Threading.Interlocked.Increment(ref _chartLoadRequestVersion);

        var detail = await _database.GetChartDetailAsync(entityId, includeClReferences: true);
        if (requestVersion != _chartLoadRequestVersion)
        {
            return;
        }

        if (detail is null)
        {
            _activeChartEntityId = null;
            ChartHeaderText.Text = "Entity no longer available.";
            ClearChartDiagram();
            return;
        }

        if (_activeChartEntityId != entityId)
        {
            ClearChartDirectionalExpansionSelection();
        }

        _activeChartEntityId = entityId;
        _selectedDetailEntityId = entityId;
        ChartHeaderText.Text =
            $"{detail.Name} [{detail.EntityType}]  Inputs: {detail.Inputs.Count}  Outputs: {detail.Outputs.Count}  XRefs: {detail.CrossReferences.Count}  SoftRefs: {detail.SoftReferences.Count}  LBRefs: {detail.LogicReferences.Count}  CLRefs: {detail.ClReferences.Count}  PLCRefs: {detail.PlcReferences.Count}  [{detail.SourceFile}]";

        await RenderChartDiagramAsync(detail);
        if (requestVersion != _chartLoadRequestVersion)
        {
            return;
        }
    }

    private async Task LoadHardwareBoxAsync(long boxEntityId)
    {
        var detail = await _database.GetHardwareBoxDetailAsync(boxEntityId);
        if (detail is null)
        {
            _activeHardwareBoxDetail = null;
            _activeHardwareModule = null;
            _hardwareAssignedPlcNodes.Clear();
            _hardwareTreeNodes.Clear();
            _hardwarePoints.Clear();
            _hardwareTrafficCopEntries.Clear();
            _hardwarePlcPoints.Clear();
            HardwareHeaderText.Text = "Hardware box no longer available.";
            HardwareTrafficCopHeaderText.Text = "PLC Mapping: no hardware box selected.";
            return;
        }

        _activeHardwareBoxDetail = detail;
        _activeHardwareModule = null;
        _hardwarePoints.Clear();
        _hardwareTrafficCopEntries.Clear();
        _hardwarePlcPoints.Clear();

        HardwareHeaderText.Text =
            $"{detail.Name} [{detail.EntityType}]  NTWK:{FormatHardwareNumber(detail.NetworkNumber)} NODE:{FormatHardwareNumber(detail.NodeNumber)}  Modules:{detail.Modules.Count}  [{detail.SourceFile}]";

        var assignedAbSystems = GetAssignedAbPlcSystemsForHardwareBox(detail);
        var assignedModiconSystems = GetAssignedModiconSystemsForHardwareBox(detail);
        await RefreshHardwareAssignedPlcListAsync(assignedAbSystems, assignedModiconSystems);
        await BuildHardwareTreeNodesAsync(detail, assignedAbSystems, assignedModiconSystems);
        ClearHardwareTrafficCopPanel();

        if (detail.Modules.Count > 0)
        {
            await LoadHardwareModulePointsAsync(detail.Modules[0]);
            return;
        }

        if (detail.HighwayNumber is int hwyNum
            && detail.BoxNumber is int boxNum
            && hwyNum > 0
            && boxNum > 0)
        {
            var points = await _database.GetHardwarePointsForHwyBoxAsync(hwyNum, boxNum);
            ApplyHardwareAddressLinkAvailability(points);
            _hardwarePoints.Clear();
            foreach (var point in points)
            {
                _hardwarePoints.Add(point);
            }

            HardwareHeaderText.Text =
                $"{detail.Name} [{detail.EntityType}]  HWY:{hwyNum:00} BOX:{boxNum:00}  Tags:{points.Count}  [{detail.SourceFile}]";
        }
    }

    private async Task LoadHardwareModulePointsAsync(HardwareModuleSummary module)
    {
        if (_activeHardwareBoxDetail is null
            || !_activeHardwareBoxDetail.NetworkNumber.HasValue
            || !_activeHardwareBoxDetail.NodeNumber.HasValue)
        {
            return;
        }

        _activeHardwareModule = module;
        var points = await _database.GetHardwarePointsForModuleAsync(
            _activeHardwareBoxDetail.BoxEntityId,
            _activeHardwareBoxDetail.NetworkNumber.Value,
            _activeHardwareBoxDetail.NodeNumber.Value,
            module);

        ApplyHardwareAddressLinkAvailability(points);
        _hardwarePoints.Clear();
        foreach (var point in points)
        {
            _hardwarePoints.Add(point);
        }

        ClearHardwareTrafficCopPanel();

        HardwareHeaderText.Text =
            $"{_activeHardwareBoxDetail.Name} [{_activeHardwareBoxDetail.EntityType}]  Module: {module.DisplayLabel}  Points: {points.Count}  [{_activeHardwareBoxDetail.SourceFile}]";
    }

    private async Task BuildHardwareTreeNodesAsync(
        HardwareBoxDetail detail,
        IReadOnlyList<AbPlcSummary>? assignedAbSystems = null,
        IReadOnlyList<ModiconSummary>? assignedModiconSystems = null)
    {
        _hardwareTreeNodes.Clear();

        var root = new HardwareTreeNode
        {
            Key = $"BOX:{detail.BoxEntityId}",
            Title = detail.Name,
            IsExpanded = true
        };

        var abSystems = assignedAbSystems?.ToList()
            ?? GetAssignedAbPlcSystemsForHardwareBox(detail).ToList();
        var modiconSystems = assignedModiconSystems?.ToList()
            ?? GetAssignedModiconSystemsForHardwareBox(detail).ToList();

        if (abSystems.Count == 0 && modiconSystems.Count == 0)
        {
            root.Children.Add(new HardwareTreeNode
            {
                Key = $"BOX:{detail.BoxEntityId}:MODICON:NONE",
                Title = "No assigned PLC systems"
            });
        }
        else
        {
            if (abSystems.Count > 0)
            {
                var abRoot = new HardwareTreeNode
                {
                    Key = $"BOX:{detail.BoxEntityId}:AB",
                    Title = $"AB PLCs ({abSystems.Count})",
                    IsExpanded = false
                };

                foreach (var system in abSystems
                             .OrderBy(x => x.PlcName, StringComparer.OrdinalIgnoreCase)
                             .ThenBy(x => x.PlcSystemId))
                {
                    var name = string.IsNullOrWhiteSpace(system.PlcName)
                        ? $"AB #{system.PlcSystemId}"
                        : system.PlcName.Trim();
                    var displayHwy = ResolveAbHardwareDisplayHwy(system, detail);
                    var hwyLabel = GetAbNetworkLabel(system.ConnectionType);
                    var title = $"{name}  {hwyLabel}:{FormatHardwareNumber(displayHwy)} BOX:{system.HwyBoxDisplay}";

                    var plcNode = new HardwareTreeNode
                    {
                        Key = $"BOX:{detail.BoxEntityId}:AB:PLC:{system.PlcSystemId}",
                        Title = title,
                        IsAbPlcNode = true,
                        AbPlcSystemId = system.PlcSystemId
                    };
                    abRoot.Children.Add(plcNode);
                }

                root.Children.Add(abRoot);
            }

            if (modiconSystems.Count > 0)
            {
                var modiconRoot = new HardwareTreeNode
                {
                    Key = $"BOX:{detail.BoxEntityId}:MODICON",
                    Title = $"Modicon PLCs ({modiconSystems.Count})",
                    IsExpanded = true
                };

                var preferredDropNumber = NormalizeHardwareDropNumber(detail.NodeNumber);
                foreach (var system in modiconSystems
                             .OrderBy(x => x.PlcName, StringComparer.OrdinalIgnoreCase)
                             .ThenBy(x => x.ModiconSystemId))
                {
                    var entries = await QueryHardwareTrafficCopEntriesAsync(
                        preferredDropNumber,
                        null,
                        system.ModiconSystemId,
                        applyHeuristicFallback: false);

                    if (entries.Count == 0 && preferredDropNumber.HasValue)
                    {
                        // If node mapping isn't available for this box, still allow browsing this assigned PLC.
                        entries = await QueryHardwareTrafficCopEntriesAsync(
                            null,
                            null,
                            system.ModiconSystemId,
                            applyHeuristicFallback: false);
                    }

                    var plcName = string.IsNullOrWhiteSpace(system.PlcName)
                        ? $"Modicon #{system.ModiconSystemId}"
                        : system.PlcName.Trim();
                    var plcNode = new HardwareTreeNode
                    {
                        Key = $"BOX:{detail.BoxEntityId}:MODICON:PLC:{system.ModiconSystemId}",
                        Title = plcName,
                        IsExpanded = false,
                        IsModiconTrafficNode = true,
                        ModiconSystemId = system.ModiconSystemId,
                        ModiconDropNumber = preferredDropNumber
                    };

                    if (entries.Count == 0)
                    {
                        plcNode.Children.Add(new HardwareTreeNode
                        {
                            Key = $"BOX:{detail.BoxEntityId}:MODICON:PLC:{system.ModiconSystemId}:NONE",
                            Title = "No PLC mapping rows imported"
                        });
                    }
                    else
                    {
                        foreach (var dropGroup in entries
                                     .GroupBy(x => x.DropNumber)
                                     .OrderBy(x => x.Key ?? int.MaxValue))
                        {
                            var dropLabel = dropGroup.Key.HasValue ? dropGroup.Key.Value.ToString("00") : "--";
                            var dropNode = new HardwareTreeNode
                            {
                                Key = $"BOX:{detail.BoxEntityId}:MODICON:PLC:{system.ModiconSystemId}:DROP:{dropLabel}",
                                Title = $"Drop {dropLabel} ({dropGroup.Count()})",
                                IsExpanded = false,
                                IsModiconTrafficNode = true,
                                ModiconSystemId = system.ModiconSystemId,
                                ModiconDropNumber = dropGroup.Key,
                            };

                            foreach (var slotGroup in dropGroup
                                         .GroupBy(x => x.SlotNumber)
                                         .OrderBy(x => x.Key))
                            {
                                var sample = slotGroup.First();
                                var slotTitle = $"Slot {slotGroup.Key:000}  {sample.ModuleText}".TrimEnd();
                                if (!string.IsNullOrWhiteSpace(sample.ReferenceRange))
                                {
                                    slotTitle = $"{slotTitle}  {sample.ReferenceRange}";
                                }

                                if (slotGroup.Count() > 1)
                                {
                                    slotTitle = $"{slotTitle}  ({slotGroup.Count()} rows)";
                                }

                                dropNode.Children.Add(new HardwareTreeNode
                                {
                                    Key = $"BOX:{detail.BoxEntityId}:MODICON:PLC:{system.ModiconSystemId}:DROP:{dropLabel}:SLOT:{slotGroup.Key:000}",
                                    Title = slotTitle,
                                    IsModiconTrafficNode = true,
                                    ModiconSystemId = system.ModiconSystemId,
                                    ModiconDropNumber = dropGroup.Key,
                                    ModiconSlotNumber = slotGroup.Key
                                });
                            }

                            plcNode.Children.Add(dropNode);
                        }
                    }

                    modiconRoot.Children.Add(plcNode);
                }

                root.Children.Add(modiconRoot);
            }
        }

        if (detail.Modules.Count > 0)
        {
            var hpmNode = new HardwareTreeNode
            {
                Key = $"BOX:{detail.BoxEntityId}:HPM",
                Title = $"HPM Modules ({detail.Modules.Count})",
                IsExpanded = true
            };

            foreach (var module in detail.Modules)
            {
                hpmNode.Children.Add(new HardwareTreeNode
                {
                    Key = $"BOX:{detail.BoxEntityId}:MOD:{module.ModuleIndex}",
                    Title = module.DisplayLabel,
                    IsModule = true,
                    Module = module
                });
            }

            root.Children.Add(hpmNode);
        }
        _hardwareTreeNodes.Add(root);
    }

    private IReadOnlyList<ModiconSummary> GetAssignedModiconSystemsForHardwareBox(HardwareBoxDetail detail)
    {
        return _modiconSystems
            .Where(system => IsModiconAssignedToHardwareBox(system, detail))
            .OrderBy(system => system.PlcName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(system => system.ModiconSystemId)
            .ToList();
    }

    private IReadOnlyList<AbPlcSummary> GetAssignedAbPlcSystemsForHardwareBox(HardwareBoxDetail detail)
    {
        return _abPlcSystems
            .Where(system => IsAbPlcAssignedToHardwareBox(system, detail))
            .OrderBy(system => system.PlcName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(system => system.PlcSystemId)
            .ToList();
    }

    private async Task RefreshHardwareAssignedPlcListAsync(
        IReadOnlyList<AbPlcSummary> assignedAbSystems,
        IReadOnlyList<ModiconSummary> assignedModiconSystems)
    {
        _hardwareAssignedPlcNodes.Clear();

        var abSystems = assignedAbSystems
            .Where(x => !string.IsNullOrWhiteSpace(x.PlcName))
            .OrderBy(x => x.PlcName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.PlcSystemId)
            .ToList();

        var modiconSystems = assignedModiconSystems
            .Where(x => !string.IsNullOrWhiteSpace(x.PlcName))
            .OrderBy(x => x.PlcName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ModiconSystemId)
            .ToList();

        if (abSystems.Count == 0 && modiconSystems.Count == 0)
        {
            _hardwareAssignedPlcNodes.Add(new HardwareTreeNode
            {
                Key = "ASSIGNED_PLC:NONE",
                Title = "(none)",
                IsExpanded = true
            });
            return;
        }

        foreach (var system in abSystems)
        {
            var plcName = system.PlcName.Trim();
            var boxDisplay = string.IsNullOrWhiteSpace(system.HwyBoxDisplay) ? "--" : system.HwyBoxDisplay;
            var displayHwy = ResolveAbHardwareDisplayHwy(system, _activeHardwareBoxDetail);
            var hwyLabel = GetAbNetworkLabel(system.ConnectionType);
            var title = $"AB  {plcName}  {hwyLabel}:{FormatHardwareNumber(displayHwy)} BOX:{boxDisplay}";
            var plcNode = new HardwareTreeNode
            {
                Key = $"ASSIGNED_PLC:AB:{system.PlcSystemId}",
                Title = title,
                IsExpanded = false,
                IsAbPlcNode = true,
                AbPlcSystemId = system.PlcSystemId
            };

            _hardwareAssignedPlcNodes.Add(plcNode);
        }

        foreach (var system in modiconSystems)
        {
            var plcName = system.PlcName.Trim();
            var plcNode = new HardwareTreeNode
            {
                Key = $"ASSIGNED_PLC:MODICON:{system.ModiconSystemId}",
                Title = $"MOD  {plcName}",
                IsExpanded = true,
                IsModiconTrafficNode = true,
                ModiconSystemId = system.ModiconSystemId
            };

            var entries = await _database.GetModiconTrafficCopEntriesForHardwareAsync(
                dropNumber: null,
                moduleIndex: null,
                modiconSystemId: system.ModiconSystemId);

            if (entries.Count == 0)
            {
                plcNode.Children.Add(new HardwareTreeNode
                {
                    Key = $"ASSIGNED_PLC:{system.ModiconSystemId}:NONE",
                    Title = "(no PLC mapping rows)"
                });
            }
            else
            {
                foreach (var dropGroup in entries
                             .GroupBy(x => x.DropNumber)
                             .OrderBy(g => g.Key ?? int.MaxValue))
                {
                    var dropLabel = dropGroup.Key is int drop && drop > 0
                        ? $"Drop {drop:00}"
                        : "Drop --";
                    var dropNode = new HardwareTreeNode
                    {
                        Key = $"ASSIGNED_PLC:{system.ModiconSystemId}:DROP:{dropGroup.Key?.ToString() ?? "--"}",
                        Title = $"{dropLabel} ({dropGroup.Count()})",
                        IsExpanded = false,
                        IsModiconTrafficNode = true,
                        ModiconSystemId = system.ModiconSystemId,
                        ModiconDropNumber = dropGroup.Key
                    };

                    foreach (var entry in dropGroup
                                 .OrderBy(x => x.SlotNumber)
                                 .ThenBy(x => x.EntryId))
                    {
                        var slotText = $"Slot {entry.SlotNumber:000}";
                        var moduleText = string.IsNullOrWhiteSpace(entry.ModuleText) ? string.Empty : $"  {entry.ModuleText.Trim()}";
                        var rangeText = string.IsNullOrWhiteSpace(entry.ReferenceRange) ? string.Empty : $"  {entry.ReferenceRange.Trim()}";
                        dropNode.Children.Add(new HardwareTreeNode
                        {
                            Key = $"ASSIGNED_PLC:{system.ModiconSystemId}:DROP:{dropGroup.Key?.ToString() ?? "--"}:SLOT:{entry.SlotNumber}:{entry.EntryId}",
                            Title = $"{slotText}{moduleText}{rangeText}",
                            IsModiconTrafficNode = true,
                            ModiconSystemId = system.ModiconSystemId,
                            ModiconDropNumber = dropGroup.Key,
                            ModiconSlotNumber = entry.SlotNumber
                        });
                    }

                    plcNode.Children.Add(dropNode);
                }
            }

            _hardwareAssignedPlcNodes.Add(plcNode);
        }
    }

    private static bool IsAbPlcAssignedToHardwareBox(AbPlcSummary system, HardwareBoxDetail detail)
    {
        var systemBoxes = ParseModiconBoxNumbers(system.HwyBoxNumbersCsv, system.HwyBoxNumber);
        var systemHasExplicitBoxes = systemBoxes.Count > 0;

        if (detail.BoxNumber is int boxNumber && boxNumber > 0 && systemHasExplicitBoxes)
        {
            if (!systemBoxes.Contains(boxNumber))
            {
                return false;
            }

            if (detail.HighwayNumber.HasValue && system.HwyNum.HasValue)
            {
                return detail.HighwayNumber.Value == system.HwyNum.Value;
            }

            return true;
        }

        if (detail.NodeNumber is int nodeNumber
            && nodeNumber > 0
            && system.NodeAddress is int nodeAddress
            && nodeAddress > 0
            && nodeNumber == nodeAddress)
        {
            return true;
        }

        return false;
    }

    private static int? ResolveAbHardwareDisplayHwy(AbPlcSummary system, HardwareBoxDetail? detail)
    {
        var isSerial = IsAbSerialInterfaceConnection(system.ConnectionType);
        if (!isSerial)
        {
            return system.HwyNum;
        }

        if (system.HwyNum is > 0)
        {
            return system.HwyNum;
        }

        if (detail?.NetworkNumber is > 0)
        {
            return detail.NetworkNumber;
        }

        return system.HwyNum;
    }

    private static bool IsAbSerialInterfaceConnection(string? connectionType) =>
        !string.IsNullOrWhiteSpace(connectionType)
        && connectionType.Trim().Equals("Serial Interface", StringComparison.OrdinalIgnoreCase);

    private static string GetAbNetworkLabel(string? connectionType) =>
        IsAbSerialInterfaceConnection(connectionType) ? "UCN" : "HWY";

    private static bool IsAbHardwareXpsFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        return fileName.IndexOf("hardware", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsModiconAssignedToHardwareBox(ModiconSummary system, HardwareBoxDetail detail)
    {
        var systemBoxes = ParseModiconBoxNumbers(system.HwyBoxNumbersCsv, system.HwyBoxNumber);
        var systemHasExplicitBoxes = systemBoxes.Count > 0;

        if (detail.BoxNumber is int boxNumber && boxNumber > 0 && systemHasExplicitBoxes)
        {
            if (!systemBoxes.Contains(boxNumber))
            {
                return false;
            }

            if (detail.HighwayNumber.HasValue && system.HwyNum.HasValue)
            {
                return detail.HighwayNumber.Value == system.HwyNum.Value;
            }

            return true;
        }

        if (detail.NodeNumber is int nodeNumber
            && nodeNumber > 0
            && system.NodeAddress is int nodeAddress
            && nodeAddress > 0
            && nodeNumber == nodeAddress)
        {
            return true;
        }

        return false;
    }

    private static HashSet<int> ParseModiconBoxNumbers(string csv, int? fallbackBoxNumber)
    {
        var values = new HashSet<int>();
        if (fallbackBoxNumber is int fallback && fallback > 0)
        {
            values.Add(fallback);
        }

        if (string.IsNullOrWhiteSpace(csv))
        {
            return values;
        }

        foreach (var token in Regex.Split(csv, @"[^0-9]+"))
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            if (int.TryParse(token.Trim(), out var parsed) && parsed > 0)
            {
                values.Add(parsed);
            }
        }

        return values;
    }

    private void ClearHardwareTrafficCopPanel()
    {
        _hardwareTrafficCopEntries.Clear();
        _hardwarePlcPoints.Clear();
        HardwareTrafficCopHeaderText.Text = "PLC Mapping: select a PLC/drop/slot in the tree.";
    }

    private async Task LoadAbHardwareEntriesForNodeSelectionAsync(long abPlcSystemId)
    {
        _hardwareTrafficCopEntries.Clear();
        _hardwarePlcPoints.Clear();

        var entries = await ParseAbHardwareEntriesAsync(abPlcSystemId);
        var isLogixView = entries.Count > 0
                          && entries.All(entry => entry.DataType.Equals("Logix", StringComparison.OrdinalIgnoreCase));
        HardwareTrafficCopDropColumn.Header = isLogixView ? "Rack" : "Drop";
        foreach (var entry in entries)
        {
            _hardwareTrafficCopEntries.Add(entry);
        }

        var plcName = _abPlcSystems.FirstOrDefault(x => x.PlcSystemId == abPlcSystemId)?.PlcName;
        if (string.IsNullOrWhiteSpace(plcName))
        {
            plcName = $"#{abPlcSystemId}";
        }

        HardwareTrafficCopHeaderText.Text = entries.Count > 0
            ? $"AB Hardware Mapping  PLC:{plcName}  Rows:{entries.Count}"
            : $"AB Hardware Mapping  PLC:{plcName}  No hardware.xps slot/module rows found.";
    }

    private async Task<IReadOnlyList<ModiconTrafficCopEntry>> ParseAbHardwareEntriesAsync(long abPlcSystemId)
    {
        var hardwareFiles = (await GetAbHardwareXpsFilesAsync(abPlcSystemId))
            .Where(file => File.Exists(file.FilePath))
            .ToList();

        if (hardwareFiles.Count == 0)
        {
            return [];
        }

        var entries = new List<ModiconTrafficCopEntry>();
        long entryId = 1;
        var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var logixModuleDescriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var hardwareFile in hardwareFiles)
        {
            using var archive = ZipFile.OpenRead(hardwareFile.FilePath);
            var pages = archive.Entries
                .Where(e => e.FullName.EndsWith(".fpage", StringComparison.OrdinalIgnoreCase))
                .Select(e => new { Entry = e, PageNumber = TryParseXpsPageNumber(e.FullName) })
                .OrderBy(e => e.PageNumber ?? int.MaxValue)
                .ThenBy(e => e.Entry.FullName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var currentRack = (int?)null;
            var currentChassis = string.Empty;
            var currentAddressingModeSlots = (int?)null;
            var currentLogixRack = string.Empty;

            foreach (var page in pages)
            {
                string pageText;
                using (var pageStream = page.Entry.Open())
                using (var reader = new StreamReader(pageStream))
                {
                    pageText = reader.ReadToEnd();
                }

                var glyphRows = BuildAbGlyphRows(pageText);
                if (glyphRows.Count == 0)
                {
                    continue;
                }

                foreach (var row in glyphRows)
                {
                    var rowText = string.Join(' ', row.Select(x => x.Text)).Trim();
                    if (string.IsNullOrWhiteSpace(rowText))
                    {
                        continue;
                    }

                    var backplaneMatch = Regex.Match(
                        rowText,
                        @"\b1756\s+Backplane,\s*(?<chassis>1756-[A-Z0-9]+)\s*:\s*(?<rack>.+)$",
                        RegexOptions.IgnoreCase);
                    if (backplaneMatch.Success)
                    {
                        currentChassis = backplaneMatch.Groups["chassis"].Value.Trim();
                        currentLogixRack = backplaneMatch.Groups["rack"].Value.Trim();
                        continue;
                    }

                    if (TryExtractLogixModuleTypeDescriptionFromRow(rowText, out var moduleType, out var moduleDescription))
                    {
                        logixModuleDescriptions[moduleType] = moduleDescription;
                        continue;
                    }

                    var logixModuleMatch = Regex.Match(
                        rowText,
                        @"^(?:(?<local>Local:\s*)?)\[(?<slot>\d+)\]\s+(?<module>1756-[A-Z0-9]+)\s*(?<name>.*)$",
                        RegexOptions.IgnoreCase);
                    if (logixModuleMatch.Success)
                    {
                        var logixSlotNumber = int.Parse(logixModuleMatch.Groups["slot"].Value, CultureInfo.InvariantCulture);
                        var logixModuleText = logixModuleMatch.Groups["module"].Value.Trim();
                        var rawName = logixModuleMatch.Groups["name"].Value.Trim();
                        var displayName = string.IsNullOrWhiteSpace(rawName) || rawName.Equals("<no name>", StringComparison.OrdinalIgnoreCase)
                            ? string.Empty
                            : rawName;
                        var moduleTypeDescription = TryResolveLogixModuleDescription(logixModuleText, logixModuleDescriptions);
                        if (string.IsNullOrWhiteSpace(moduleTypeDescription) && LooksLikeLogixCardDescription(displayName))
                        {
                            moduleTypeDescription = displayName;
                            displayName = string.Empty;
                        }
                        moduleTypeDescription = CleanLogixModuleDescription(moduleTypeDescription);
                        displayName = CleanLogixModuleDescription(displayName);

                        var rackLabel = logixModuleMatch.Groups["local"].Success
                            ? "Local"
                            : string.IsNullOrWhiteSpace(currentLogixRack) ? "RK?" : currentLogixRack;
                        var dropNumber = TryParseLogixRackNumber(rackLabel);
                        var referenceRange = string.IsNullOrWhiteSpace(currentChassis)
                            ? rackLabel
                            : $"{rackLabel} ({currentChassis})";
                        var logixDedupeKey = $"LOGIX|{rackLabel}|{logixSlotNumber}|{logixModuleText}|{displayName}";
                        if (!dedupe.Add(logixDedupeKey))
                        {
                            continue;
                        }

                        entries.Add(new ModiconTrafficCopEntry
                        {
                            EntryId = entryId++,
                            ModiconSystemId = 0,
                            DropNumber = dropNumber,
                            SlotNumber = logixSlotNumber,
                            ModuleText = logixModuleText,
                            ReferenceStart = string.Empty,
                            ReferenceEnd = string.Empty,
                            ReferenceRange = referenceRange,
                            DataType = "Logix",
                            Description = string.IsNullOrWhiteSpace(moduleTypeDescription) ? displayName : moduleTypeDescription,
                            SourceLine = rowText
                        });
                        continue;
                    }

                    var chassisMatch = Regex.Match(rowText, @"\bChassis[_\s]+(?<num>\d+)\b", RegexOptions.IgnoreCase);
                    if (chassisMatch.Success)
                    {
                        currentChassis = $"Chassis {chassisMatch.Groups["num"].Value}";
                        continue;
                    }

                    var rackMatch = Regex.Match(rowText, @"\bRack:\s*(?<num>\d+)\b", RegexOptions.IgnoreCase);
                    if (rackMatch.Success)
                    {
                        currentRack = int.Parse(rackMatch.Groups["num"].Value, CultureInfo.InvariantCulture);
                        continue;
                    }

                    var addressingModeMatch = Regex.Match(rowText, @"\bAddressing\s+Mode:\s*(?<mode>\d+)\s*Slot\b", RegexOptions.IgnoreCase);
                    if (addressingModeMatch.Success)
                    {
                        currentAddressingModeSlots = int.Parse(addressingModeMatch.Groups["mode"].Value, CultureInfo.InvariantCulture);
                        continue;
                    }

                    // Process only clear module table rows (slot/module/description columns).
                    var slotCandidate = row
                        .Where(x => x.X <= 45)
                        .Select(x => x.Text)
                        .FirstOrDefault(text => Regex.IsMatch(text, @"^\d{1,2}$"));
                    if (string.IsNullOrWhiteSpace(slotCandidate))
                    {
                        continue;
                    }

                    var moduleCandidate = row
                        .Where(x => x.X >= 45 && x.X <= 220)
                        .Select(x => x.Text)
                        .FirstOrDefault(text => IsAbModuleTypeToken(text));
                    if (string.IsNullOrWhiteSpace(moduleCandidate))
                    {
                        continue;
                    }

                    var description = string.Join(' ',
                            row.Where(x => x.X >= 220)
                                .Select(x => x.Text))
                        .Trim();

                    // Fallback if description split into unexpected columns.
                    if (string.IsNullOrWhiteSpace(description))
                    {
                        description = rowText
                            .Replace(slotCandidate, string.Empty, StringComparison.Ordinal)
                            .Replace(moduleCandidate, string.Empty, StringComparison.Ordinal)
                            .Trim();
                    }

                    var moduleText = moduleCandidate.Trim();
                    if (string.IsNullOrWhiteSpace(moduleText) || string.IsNullOrWhiteSpace(description))
                    {
                        continue;
                    }

                    var slotNumber = int.Parse(slotCandidate, CultureInfo.InvariantCulture);
                    var dedupeKey = $"{currentRack?.ToString() ?? "--"}|{slotNumber}|{moduleText}|{description}";
                    if (!dedupe.Add(dedupeKey))
                    {
                        continue;
                    }

                    entries.Add(new ModiconTrafficCopEntry
                    {
                        EntryId = entryId++,
                        ModiconSystemId = 0,
                        DropNumber = currentRack,
                        SlotNumber = slotNumber,
                        ModuleText = moduleText,
                        ReferenceStart = string.Empty,
                        ReferenceEnd = string.Empty,
                        ReferenceRange = currentChassis,
                        DataType = BuildAbModuleRangeSummary(currentRack, slotNumber, moduleText, description, currentAddressingModeSlots),
                        Description = description,
                        SourceLine = rowText
                    });
                }
            }
        }

        var orderedEntries = entries
            .OrderBy(entry => entry.DropNumber ?? int.MaxValue)
            .ThenBy(entry => entry.SlotNumber)
            .ThenBy(entry => entry.ModuleText, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (orderedEntries.Count > 0
            && orderedEntries.All(entry => entry.DataType.Equals("Logix", StringComparison.OrdinalIgnoreCase)))
        {
            return BuildLogixHardwareTreeEntries(orderedEntries);
        }

        return orderedEntries;
    }

    private static int? TryParseLogixRackNumber(string rackLabel)
    {
        if (string.IsNullOrWhiteSpace(rackLabel))
        {
            return null;
        }

        if (rackLabel.Equals("Local", StringComparison.OrdinalIgnoreCase)
            || rackLabel.IndexOf("Local Modules", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return 0;
        }

        var match = Regex.Match(rackLabel, @"\bRK\s*(?<num>\d+)\b", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        return int.TryParse(match.Groups["num"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static bool TryExtractLogixModuleTypeDescriptionFromRow(
        string rowText,
        out string moduleType,
        out string description)
    {
        moduleType = string.Empty;
        description = string.Empty;
        if (string.IsNullOrWhiteSpace(rowText))
        {
            return false;
        }

        var normalized = Regex.Replace(rowText.Trim(), @"\s+", " ");
        var patterns = new[]
        {
            @"(?<module>1756-[A-Z0-9]+)\s*[-:]\s*(?<description>.+)$",
            @"(?<module>1756-[A-Z0-9]+)\s+(?<description>(?:\d+\s*Point\b.*|.+\b(?:Input|Output|Analog|Digital|Adapter|Communication|Comm|Backplane)\b.*))$"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(normalized, pattern, RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                continue;
            }

            var parsedType = match.Groups["module"].Value.Trim().ToUpperInvariant();
            var parsedDescription = CleanLogixModuleDescription(match.Groups["description"].Value);
            if (string.IsNullOrWhiteSpace(parsedType) || string.IsNullOrWhiteSpace(parsedDescription))
            {
                continue;
            }

            if (parsedDescription.StartsWith("[", StringComparison.Ordinal)
                || parsedDescription.StartsWith("RK", StringComparison.OrdinalIgnoreCase)
                || parsedDescription.StartsWith("Local", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            moduleType = parsedType;
            description = parsedDescription;
            return true;
        }

        return false;
    }

    private static string TryResolveLogixModuleDescription(
        string moduleType,
        IReadOnlyDictionary<string, string> parsedDescriptions)
    {
        if (string.IsNullOrWhiteSpace(moduleType))
        {
            return string.Empty;
        }

        var normalized = moduleType.Trim().ToUpperInvariant();
        if (parsedDescriptions.TryGetValue(normalized, out var parsed))
        {
            return parsed;
        }

        if (normalized.EndsWith("I", StringComparison.Ordinal) && normalized.Length > 1)
        {
            var withoutSuffix = normalized[..^1];
            if (parsedDescriptions.TryGetValue(withoutSuffix, out var suffixFallback))
            {
                return suffixFallback;
            }
        }

        return TryGetKnownLogixModuleDescription(normalized);
    }

    private static bool LooksLikeLogixCardDescription(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Regex.IsMatch(
            value,
            @"\b(\d+\s*Point|Input|Output|Analog|Digital|Adapter|Communication|Comm)\b",
            RegexOptions.IgnoreCase);
    }

    private static string CleanLogixModuleDescription(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = Regex.Replace(
            value.Trim(),
            @"\s*[,;]?\s*\(?\s*Parent\s*:\s*RK\d+\s*\)?\s*$",
            string.Empty,
            RegexOptions.IgnoreCase);

        cleaned = Regex.Replace(cleaned, @"\s{2,}", " ").Trim();
        return cleaned;
    }

    private static string TryGetKnownLogixModuleDescription(string moduleType)
    {
        return moduleType switch
        {
            "1756-IA16" => "16 Point 79V-132V AC Input",
            "1756-IA16I" => "16 Point 79V-132V AC Input",
            "1756-OA16" => "16 Point 79V-132V AC Output",
            "1756-OA16I" => "16 Point 79V-132V AC Output",
            _ => string.Empty
        };
    }

    private static IReadOnlyList<ModiconTrafficCopEntry> BuildLogixHardwareTreeEntries(
        IReadOnlyList<ModiconTrafficCopEntry> modules)
    {
        var rackBuckets = new Dictionary<string, (string Chassis, int? Drop, List<ModiconTrafficCopEntry> Modules)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var module in modules)
        {
            var rackLabel = "Local";
            var chassis = string.Empty;
            if (!TryParseLogixReferenceRange(module.ReferenceRange, out rackLabel, out chassis))
            {
                rackLabel = module.DropNumber.GetValueOrDefault() == 0 ? "Local" : $"RK{module.DropNumber}";
            }

            if (!rackBuckets.TryGetValue(rackLabel, out var bucket))
            {
                bucket = (chassis, module.DropNumber, []);
            }

            if (string.IsNullOrWhiteSpace(bucket.Chassis) && !string.IsNullOrWhiteSpace(chassis))
            {
                bucket.Chassis = chassis;
            }

            if (!bucket.Drop.HasValue && module.DropNumber.HasValue)
            {
                bucket.Drop = module.DropNumber;
            }

            bucket.Modules.Add(module);
            rackBuckets[rackLabel] = bucket;
        }

        if (rackBuckets.Count == 0)
        {
            return modules;
        }

        var localRackKey = rackBuckets.Keys.FirstOrDefault(key =>
                key.Equals("Local", StringComparison.OrdinalIgnoreCase)
                || key.IndexOf("Local", StringComparison.OrdinalIgnoreCase) >= 0)
            ?? rackBuckets.Keys.First();
        var remoteRackKeys = rackBuckets.Keys
            .Where(key => !key.Equals(localRackKey, StringComparison.OrdinalIgnoreCase))
            .OrderBy(key => TryParseLogixRackNumber(key) ?? int.MaxValue)
            .ThenBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new List<ModiconTrafficCopEntry>();
        long entryId = 1;

        var local = rackBuckets[localRackKey];
        var localHeader = string.IsNullOrWhiteSpace(local.Chassis)
            ? "1756 Backplane"
            : $"1756 Backplane, {local.Chassis}";
        result.Add(NewTreeRow(entryId++, 0, true, local.Drop ?? 0, -1, localHeader, localRackKey));

        foreach (var module in local.Modules.OrderBy(x => x.SlotNumber))
        {
            result.Add(CloneAsTreeRow(module, entryId++, 1, isHeader: false));
        }

        if (remoteRackKeys.Count > 0)
        {
            result.Add(NewTreeRow(entryId++, 1, true, local.Drop ?? 0, -1, "Ethernet", string.Empty));
        }

        foreach (var rackKey in remoteRackKeys)
        {
            var bucket = rackBuckets[rackKey];
            var rackDrop = bucket.Drop ?? TryParseLogixRackNumber(rackKey);
            var bridgeLabel = $"1756-EN2TR {rackKey}".Trim();
            result.Add(NewTreeRow(entryId++, 2, true, rackDrop, -1, bridgeLabel, string.Empty));

            var backplaneLabel = string.IsNullOrWhiteSpace(bucket.Chassis)
                ? "1756 Backplane"
                : $"1756 Backplane, {bucket.Chassis}";
            result.Add(NewTreeRow(entryId++, 3, true, rackDrop, -1, backplaneLabel, rackKey));

            foreach (var module in bucket.Modules.OrderBy(x => x.SlotNumber))
            {
                result.Add(CloneAsTreeRow(module, entryId++, 4, isHeader: false));
            }
        }

        return result;
    }

    private static ModiconTrafficCopEntry CloneAsTreeRow(ModiconTrafficCopEntry source, long entryId, int level, bool isHeader)
    {
        return new ModiconTrafficCopEntry
        {
            EntryId = entryId,
            ModiconSystemId = source.ModiconSystemId,
            DropNumber = source.DropNumber,
            SlotNumber = source.SlotNumber,
            ModuleText = source.SlotNumber >= 0 ? $"[{source.SlotNumber}] {source.ModuleText}" : source.ModuleText,
            ReferenceStart = source.ReferenceStart,
            ReferenceEnd = source.ReferenceEnd,
            ReferenceRange = source.ReferenceRange,
            DataType = source.DataType,
            Description = source.Description,
            SourceLine = source.SourceLine,
            TreeLevel = level,
            IsTreeHeader = isHeader
        };
    }

    private static ModiconTrafficCopEntry NewTreeRow(
        long entryId,
        int level,
        bool isHeader,
        int? drop,
        int slot,
        string moduleText,
        string referenceRange)
    {
        return new ModiconTrafficCopEntry
        {
            EntryId = entryId,
            ModiconSystemId = 0,
            DropNumber = drop,
            SlotNumber = slot,
            ModuleText = moduleText,
            ReferenceStart = string.Empty,
            ReferenceEnd = string.Empty,
            ReferenceRange = referenceRange,
            DataType = "Logix",
            Description = string.Empty,
            SourceLine = moduleText,
            TreeLevel = level,
            IsTreeHeader = isHeader
        };
    }

    private static bool TryParseLogixReferenceRange(string raw, out string rackLabel, out string chassis)
    {
        rackLabel = string.Empty;
        chassis = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var match = Regex.Match(raw.Trim(), @"^(?<rack>[^()]+?)\s*(?:\((?<chassis>[^)]+)\))?$", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return false;
        }

        rackLabel = match.Groups["rack"].Value.Trim();
        chassis = match.Groups["chassis"].Value.Trim();
        return !string.IsNullOrWhiteSpace(rackLabel);
    }

    private static List<List<(double X, string Text)>> BuildAbGlyphRows(string fpageContent)
    {
        if (string.IsNullOrWhiteSpace(fpageContent))
        {
            return [];
        }

        var glyphRegex = new Regex(
            @"<Glyphs[^>]*\bOriginX=""(?<x>[-+]?\d*\.?\d+)""[^>]*\bOriginY=""(?<y>[-+]?\d*\.?\d+)""[^>]*\bUnicodeString=""(?<text>[^""]*)""[^>]*/>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var glyphs = new List<(double X, double Y, string Text)>();

        foreach (Match match in glyphRegex.Matches(fpageContent))
        {
            if (!double.TryParse(match.Groups["x"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
            {
                continue;
            }

            if (!double.TryParse(match.Groups["y"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            {
                continue;
            }

            var text = WebUtility.HtmlDecode(match.Groups["text"].Value).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            glyphs.Add((x, y, text));
        }

        // Group by line using Y proximity tolerance.
        var rowBuckets = new List<(double Y, List<(double X, string Text)> Cells)>();
        foreach (var glyph in glyphs.OrderBy(g => g.Y).ThenBy(g => g.X))
        {
            var rowIndex = rowBuckets.FindLastIndex(bucket => Math.Abs(bucket.Y - glyph.Y) <= 0.75);
            if (rowIndex < 0)
            {
                rowBuckets.Add((glyph.Y, [(glyph.X, glyph.Text)]));
                continue;
            }

            var bucket = rowBuckets[rowIndex];
            bucket.Cells.Add((glyph.X, glyph.Text));
            rowBuckets[rowIndex] = bucket;
        }

        var rows = rowBuckets
            .OrderBy(bucket => bucket.Y)
            .Select(bucket => bucket.Cells)
            .ToList();

        foreach (var row in rows)
        {
            row.Sort((a, b) => a.X.CompareTo(b.X));
        }

        return rows;
    }

    private static bool IsAbModuleTypeToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var value = token.Trim();
        return Regex.IsMatch(value, @"^\d{4}-[A-Z0-9]{2,}$", RegexOptions.IgnoreCase)
               || Regex.IsMatch(value, @"^[A-Z]{1,4}\d{1,4}(-[A-Z0-9]+)+$", RegexOptions.IgnoreCase);
    }

    private static string ExtractAbChannelSummary(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return string.Empty;
        }

        var match = Regex.Match(description, @"\b(?<count>\d+)\s*([-\s])?\s*ch(annel)?\b", RegexOptions.IgnoreCase);
        return match.Success ? $"{match.Groups["count"].Value} ch" : string.Empty;
    }

    private static string BuildAbModuleRangeSummary(int? rackNumber, int slotNumber, string moduleType, string description, int? addressingModeSlots)
    {
        var ioKind = GetAbModuleIoKind(moduleType, description);
        var channelCount = TryExtractChannelCount(description);
        if (!rackNumber.HasValue || rackNumber.Value < 0 || string.IsNullOrWhiteSpace(ioKind) || !channelCount.HasValue || channelCount.Value <= 0)
        {
            var fallbackParts = new List<string>();
            if (rackNumber.HasValue && rackNumber.Value >= 0)
            {
                fallbackParts.Add($"R{rackNumber.Value} S{slotNumber}");
            }
            else
            {
                fallbackParts.Add($"S{slotNumber}");
            }

            if (channelCount.HasValue && channelCount.Value > 0)
            {
                fallbackParts.Add($"Ch 0-{channelCount.Value - 1}");
            }

            if (!string.IsNullOrWhiteSpace(ioKind))
            {
                fallbackParts.Add(ioKind);
            }

            return string.Join("  ", fallbackParts);
        }

        // PLC-5 I/O image format: a:bbc/dd (a=I/O, bb=rack octal, c=group octal, dd=bit octal).
        var addressType = ioKind.Equals("Input", StringComparison.OrdinalIgnoreCase) ? "I" : "O";
        var rackOctal = Convert.ToString(rackNumber.Value, 8).PadLeft(2, '0');
        var groupValue = addressingModeSlots == 2 ? slotNumber / 2 : slotNumber;
        var groupOctal = Convert.ToString(Math.Max(0, groupValue), 8);
        var maxBit = Math.Max(0, channelCount.Value - 1);
        var maxBitOctal = Convert.ToString(maxBit, 8).PadLeft(2, '0');

        var imageRange = $"{addressType}:{rackOctal}{groupOctal}/00-{maxBitOctal}";
        return $"{imageRange} (ch0-{maxBit})";
    }

    private static int? TryExtractChannelCount(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var match = Regex.Match(description, @"\b(?<count>\d+)\s*pt\b", RegexOptions.IgnoreCase);
        if (!match.Success || !int.TryParse(match.Groups["count"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
        {
            return null;
        }

        return count > 0 ? count : null;
    }

    private static string GetAbModuleIoKind(string moduleType, string description)
    {
        var upperModule = string.IsNullOrWhiteSpace(moduleType) ? string.Empty : moduleType.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(upperModule))
        {
            var suffixMatch = Regex.Match(upperModule, @"-(?<suffix>[A-Z0-9]{1,3})$");
            if (suffixMatch.Success)
            {
                var suffix = suffixMatch.Groups["suffix"].Value;
                if (suffix.StartsWith("I", StringComparison.Ordinal))
                {
                    // Common AB PLC-5 examples: ID, IF, IA, IV, IM...
                    return "Input";
                }

                if (suffix.StartsWith("O", StringComparison.Ordinal))
                {
                    // Common AB PLC-5 examples: OD, OF, OA, OV, OM...
                    return "Output";
                }

                if (suffix.StartsWith("IO", StringComparison.Ordinal))
                {
                    return "I/O";
                }
            }
        }

        var upperDescription = string.IsNullOrWhiteSpace(description) ? string.Empty : description.Trim().ToUpperInvariant();
        if (upperDescription.Contains("OUTPUT", StringComparison.Ordinal)
            || upperDescription.Contains("DRIVER", StringComparison.Ordinal))
        {
            return "Output";
        }

        if (upperDescription.Contains("INPUT", StringComparison.Ordinal))
        {
            return "Input";
        }

        if (upperDescription.Contains("INPUT/OUTPUT", StringComparison.Ordinal)
            || upperDescription.Contains("I/O", StringComparison.Ordinal))
        {
            return "I/O";
        }

        return string.Empty;
    }

    private async Task<List<(long Id, string FilePath, string FileName)>> GetAbHardwareXpsFilesAsync(long plcSystemId)
    {
        var files = new List<(long Id, string FilePath, string FileName)>();
        var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var ladderFiles = await _database.GetAbPlcLadderFilesAsync(plcSystemId);
        foreach (var file in ladderFiles
                     .Where(file => IsAbHardwareXpsFileName(file.FileName))
                     .OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(file => file.LadderFileId))
        {
            if (!dedupe.Add(file.FilePath))
            {
                continue;
            }

            files.Add((file.LadderFileId, file.FilePath, file.FileName));
        }

        var summary = _abPlcSystems.FirstOrDefault(x => x.PlcSystemId == plcSystemId);
        if (summary is not null
            && !string.IsNullOrWhiteSpace(summary.SystemFilePath)
            && summary.SystemFilePath.EndsWith(".xps", StringComparison.OrdinalIgnoreCase)
            && IsAbHardwareXpsFileName(Path.GetFileName(summary.SystemFilePath))
            && dedupe.Add(summary.SystemFilePath))
        {
            files.Add((0, summary.SystemFilePath, Path.GetFileName(summary.SystemFilePath)));
        }

        return files
            .OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.Id)
            .ToList();
    }

    private async Task LoadHardwareTrafficCopEntriesForNodeSelectionAsync(int? dropNumber, int? slotNumber, long? modiconSystemId)
    {
        _hardwareTrafficCopEntries.Clear();
        HardwareTrafficCopDropColumn.Header = "Drop";

        var normalizedDropNumber = NormalizeHardwareDropNumber(dropNumber);
        var resolvedSystemId = modiconSystemId is > 0 ? modiconSystemId : _activeModiconSystemId;
        var allowSystemFallback = modiconSystemId is null or <= 0;
        var (entries, usedAllSystemsFallback) = await GetTrafficCopEntriesWithSystemFallbackAsync(
            normalizedDropNumber,
            slotNumber,
            applyHeuristicFallback: false,
            preferredSystemId: resolvedSystemId,
            allowSystemFallback: allowSystemFallback);

        foreach (var entry in entries)
        {
            _hardwareTrafficCopEntries.Add(entry);
        }

        var effectiveSystemId = usedAllSystemsFallback ? null : resolvedSystemId;
        var plcText = BuildModiconPlcScopeText(effectiveSystemId);
        var dropText = normalizedDropNumber.HasValue ? normalizedDropNumber.Value.ToString("00") : "ALL";
        var slotText = slotNumber.HasValue ? $" SLOT:{slotNumber.Value:000}" : string.Empty;
        HardwareTrafficCopHeaderText.Text = $"PLC Mapping{plcText}  Drop:{dropText}{slotText}  Rows:{entries.Count}";

        await LoadHardwarePointsForTrafficCopSelectionAsync(entries, effectiveSystemId);
    }

    private async Task LoadHardwarePointsForTrafficCopSelectionAsync(
        IReadOnlyList<ModiconTrafficCopEntry> entries,
        long? modiconSystemId)
    {
        var points = await _database.GetHardwarePointsForModiconTrafficCopAsync(entries, modiconSystemId);

        _hardwarePlcPoints.Clear();
        foreach (var point in points)
        {
            _hardwarePlcPoints.Add(point);
        }
    }

    private async Task<(IReadOnlyList<ModiconTrafficCopEntry> Entries, bool UsedAllSystemsFallback)> GetTrafficCopEntriesWithSystemFallbackAsync(
        int? dropNumber,
        int? moduleIndex,
        bool applyHeuristicFallback,
        long? preferredSystemId = null,
        bool allowSystemFallback = true)
    {
        var requestedSystemId = preferredSystemId;
        if (!requestedSystemId.HasValue || requestedSystemId.Value <= 0)
        {
            requestedSystemId = _activeModiconSystemId;
        }

        var entries = await QueryHardwareTrafficCopEntriesAsync(
            dropNumber,
            moduleIndex,
            requestedSystemId,
            applyHeuristicFallback);
        var usedAllSystemsFallback = false;

        if (entries.Count == 0 && allowSystemFallback && requestedSystemId.HasValue)
        {
            entries = await QueryHardwareTrafficCopEntriesAsync(
                dropNumber,
                moduleIndex,
                null,
                applyHeuristicFallback);
            usedAllSystemsFallback = entries.Count > 0;
        }

        return (entries, usedAllSystemsFallback);
    }

    private string BuildModiconPlcScopeText(long? modiconSystemId)
    {
        if (!modiconSystemId.HasValue || modiconSystemId.Value <= 0)
        {
            return string.Empty;
        }

        var match = _modiconSystems.FirstOrDefault(x => x.ModiconSystemId == modiconSystemId.Value);
        var plcName = match?.PlcName?.Trim();
        if (string.IsNullOrWhiteSpace(plcName))
        {
            plcName = $"#{modiconSystemId.Value}";
        }

        return $" PLC:{plcName}";
    }

    private async Task<IReadOnlyList<ModiconTrafficCopEntry>> QueryHardwareTrafficCopEntriesAsync(
        int? dropNumber,
        int? moduleIndex,
        long? modiconSystemId,
        bool applyHeuristicFallback = true)
    {
        var entries = await _database.GetModiconTrafficCopEntriesForHardwareAsync(dropNumber, moduleIndex, modiconSystemId);

        if (!applyHeuristicFallback)
        {
            return entries;
        }

        // Module numbering schemes can differ between BOX and trafficcop; if no match, retry without module filter.
        if (entries.Count == 0 && moduleIndex.HasValue)
        {
            entries = await _database.GetModiconTrafficCopEntriesForHardwareAsync(dropNumber, null, modiconSystemId);
        }

        // If drop mapping doesn't line up, retry without drop filter so hardware view still shows trafficcop rows.
        if (entries.Count == 0 && dropNumber.HasValue)
        {
            entries = await _database.GetModiconTrafficCopEntriesForHardwareAsync(null, moduleIndex, modiconSystemId);
            if (entries.Count == 0 && moduleIndex.HasValue)
            {
                entries = await _database.GetModiconTrafficCopEntriesForHardwareAsync(null, null, modiconSystemId);
            }
        }

        return entries;
    }

    private static int? NormalizeHardwareDropNumber(int? nodeNumber)
    {
        if (!nodeNumber.HasValue || nodeNumber.Value <= 0)
        {
            return null;
        }

        return nodeNumber;
    }

    private async void SaveLogicLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeLogicEntityId is not long entityId)
        {
            MessageBox.Show(this, "Select a logic block first.", "Save Layout", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_logicDiagramNodes.Count == 0)
        {
            MessageBox.Show(this, "No diagram is loaded to save.", "Save Layout", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var positions = _logicDiagramNodes
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .Select(x => new LogicLayoutNodePosition
            {
                VertexKey = x.Key,
                X = x.Position.X,
                Y = x.Position.Y
            })
            .ToList();

        await _database.SaveLogicLayoutAsync(entityId, positions);
        _importLog.Add($"[{DateTime.Now:HH:mm:ss}] Saved logic layout for entity {entityId} ({positions.Count} nodes).");
        LogicSummaryText.Text = $"{_logicResults.Count} logic blocks (layout saved)";
        MarkProjectDirty();
    }

    private async void ResetLogicLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeLogicEntityId is not long entityId)
        {
            MessageBox.Show(this, "Select a logic block first.", "Reset Layout", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await _database.DeleteLogicLayoutAsync(entityId);
        _importLog.Add($"[{DateTime.Now:HH:mm:ss}] Reset logic layout for entity {entityId}.");
        await LoadLogicBlockAsync(entityId);
        LogicSummaryText.Text = $"{_logicResults.Count} logic blocks (layout reset)";
        MarkProjectDirty();
    }

    private async Task RefreshSelectedEntityDetailsAsync()
    {
        if (EntityResultsList.SelectedItem is EntitySearchResult selected)
        {
            await LoadEntityDetailsAsync(selected.EntityId);
        }
    }

    private async Task LoadAbPlcSystemAsync(long plcSystemId)
    {
        var selected = _abPlcSystems.FirstOrDefault(x => x.PlcSystemId == plcSystemId);
        if (selected is null)
        {
            return;
        }

        _activeAbPlcSystemId = plcSystemId;
        var ladderFiles = await _database.GetAbPlcLadderFilesAsync(plcSystemId);

        _abPlcLadderFiles.Clear();
        foreach (var file in ladderFiles)
        {
            _abPlcLadderFiles.Add(file);
        }

        var abNetworkLabel = GetAbNetworkLabel(selected.ConnectionType);
        AbPlcHeaderText.Text =
            $"{selected.PlcName} [{selected.ConnectionType}] {abNetworkLabel}:{selected.HwyNum} BOX:{selected.HwyBoxDisplay} NODE:{selected.NodeAddress} FILE:{selected.Plc5FileAddress}  XPS:{selected.LadderFileCount}  Map:{selected.MappingCount}";

        if (_abPlcLadderFiles.Count > 0)
        {
            AbPlcFilesList.SelectedIndex = 0;
        }
        else
        {
            AbPlcViewerHeaderText.Text = "No ladder XPS files imported for this PLC.";
            ClearAbPlcXpsDocument();
        }
    }

    private void DisplayAbPlcXps(AbPlcLadderFile ladderFile, string? referenceSummary = null, int? pageNumber = null)
    {
        if (string.IsNullOrWhiteSpace(ladderFile.FilePath) || !File.Exists(ladderFile.FilePath))
        {
            AbPlcViewerHeaderText.Text = $"File not found: {ladderFile.FileName}";
            ClearAbPlcXpsDocument();
            return;
        }

        try
        {
            ClearAbPlcXpsDocument();
            _activeAbPlcXpsDocument = new XpsDocument(ladderFile.FilePath, FileAccess.Read);
            AbPlcXpsViewer.Document = _activeAbPlcXpsDocument.GetFixedDocumentSequence();
            AbPlcViewerHeaderText.Text = string.IsNullOrWhiteSpace(referenceSummary)
                ? $"{ladderFile.FileName}  ({ladderFile.FilePath})"
                : $"{ladderFile.FileName}  ({ladderFile.FilePath})  | {referenceSummary}";
            NavigateDocumentViewerToPage(AbPlcXpsViewer, pageNumber);
        }
        catch (Exception ex)
        {
            AbPlcViewerHeaderText.Text = $"Failed to open {ladderFile.FileName}: {ex.Message}";
            ClearAbPlcXpsDocument();
        }
    }

    private async Task LoadModiconSystemAsync(long modiconSystemId)
    {
        var selected = _modiconSystems.FirstOrDefault(x => x.ModiconSystemId == modiconSystemId);
        if (selected is null)
        {
            return;
        }

        _activeModiconSystemId = modiconSystemId;
        var programFiles = await _database.GetModiconProgramFilesAsync(modiconSystemId);

        _modiconProgramFiles.Clear();
        foreach (var file in programFiles)
        {
            _modiconProgramFiles.Add(file);
        }

        var nodeDisplay = selected.NodeAddress.HasValue ? $" NODE:{selected.NodeAddress.Value}" : string.Empty;
        ModiconHeaderText.Text =
            $"{selected.PlcName} [{selected.ConnectionType}] HWY:{selected.HwyNum} BOX:{selected.HwyBoxDisplay}{nodeDisplay}  XPS:{selected.ProgramFileCount}  Map:{selected.MappingCount}  TCOP:{selected.TrafficCopEntryCount}";

        if (_modiconProgramFiles.Count > 0)
        {
            ModiconFilesList.SelectedIndex = 0;
        }
        else
        {
            ModiconViewerHeaderText.Text = "No program XPS files imported for this PLC.";
            ClearModiconXpsDocument();
        }
    }

    private void DisplayModiconXps(ModiconProgramFile programFile, string? referenceSummary = null, int? pageNumber = null)
    {
        if (string.IsNullOrWhiteSpace(programFile.FilePath) || !File.Exists(programFile.FilePath))
        {
            ModiconViewerHeaderText.Text = $"File not found: {programFile.FileName}";
            ClearModiconXpsDocument();
            return;
        }

        try
        {
            ClearModiconXpsDocument();
            _activeModiconXpsDocument = new XpsDocument(programFile.FilePath, FileAccess.Read);
            ModiconXpsViewer.Document = _activeModiconXpsDocument.GetFixedDocumentSequence();
            ModiconViewerHeaderText.Text = string.IsNullOrWhiteSpace(referenceSummary)
                ? $"{programFile.FileName}  ({programFile.FilePath})"
                : $"{programFile.FileName}  ({programFile.FilePath})  | {referenceSummary}";
            NavigateDocumentViewerToPage(ModiconXpsViewer, pageNumber);
        }
        catch (Exception ex)
        {
            ModiconViewerHeaderText.Text = $"Failed to open {programFile.FileName}: {ex.Message}";
            ClearModiconXpsDocument();
        }
    }

    private void AbPlcXpsSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = RunAbPlcXpsSearchAsync();
        }
    }

    private void AbPlcXpsFindButton_Click(object sender, RoutedEventArgs e) => OpenNativeFind(AbPlcXpsViewer);
    private void AbPlcXpsGoRefButton_Click(object sender, RoutedEventArgs e) => _ = RunAbPlcXpsSearchAsync();

    private void AbPlcXpsPrevButton_Click(object sender, RoutedEventArgs e) =>
        NavigateAbPlcXpsSearch(-1);

    private void AbPlcXpsNextButton_Click(object sender, RoutedEventArgs e) =>
        NavigateAbPlcXpsSearch(1);

    private void ModiconXpsSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = RunModiconXpsSearchAsync();
        }
    }

    private void ModiconXpsFindButton_Click(object sender, RoutedEventArgs e) => OpenNativeFind(ModiconXpsViewer);
    private void ModiconXpsGoRefButton_Click(object sender, RoutedEventArgs e) => _ = RunModiconXpsSearchAsync();

    private static void OpenNativeFind(DocumentViewer viewer)
    {
        if (viewer.Document is null)
        {
            return;
        }

        viewer.Focus();
        viewer.Find();
    }

    private static void SeedNativeFindTerm(DocumentViewer viewer, string term)
    {
        if (viewer.Document is null || string.IsNullOrWhiteSpace(term))
        {
            return;
        }

        viewer.Focus();
        viewer.Find();

        var searchTerm = term.Trim();
        viewer.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                var findBox = FindDocumentViewerFindTextBox(viewer);
                if (findBox is null)
                {
                    return;
                }

                findBox.Text = searchTerm;
                findBox.CaretIndex = findBox.Text.Length;
                findBox.SelectAll();
                findBox.Focus();
            }));
    }

    private void ModiconXpsPrevButton_Click(object sender, RoutedEventArgs e) =>
        NavigateModiconXpsSearch(-1);

    private void ModiconXpsNextButton_Click(object sender, RoutedEventArgs e) =>
        NavigateModiconXpsSearch(1);

    private async Task RunAbPlcXpsSearchAsync()
    {
        var query = AbPlcXpsSearchBox.Text?.Trim() ?? string.Empty;
        _abPlcXpsSearchFlIndices = [];
        _abPlcXpsSearchIndex = -1;
        AbPlcXpsPrevButton.IsEnabled = false;
        AbPlcXpsNextButton.IsEnabled = false;

        if (string.IsNullOrWhiteSpace(query))
        {
            AbPlcXpsSearchStatusText.Text = string.Empty;
            return;
        }

        var matchCount = 0;
        var mappingPageMatches = new List<int>();
        if (_activeAbPlcSystemId.HasValue)
        {
            (matchCount, mappingPageMatches) = await _database.GetFlIndicesForXpsSearchAsync(
                _activeAbPlcSystemId.Value, isAbPlc: true, query);
        }

        var currentXpsPath = (AbPlcFilesList.SelectedItem as AbPlcLadderFile)?.FilePath;
        var xpsPageMatches = new List<int>();
        if (!string.IsNullOrWhiteSpace(currentXpsPath) && File.Exists(currentXpsPath))
        {
            AbPlcXpsSearchStatusText.Text = "Searching open XPS pages...";
            var ocrIndex = await GetOrBuildXpsOcrIndexAsync(
                currentXpsPath,
                status => AbPlcXpsSearchStatusText.Text = status);

            xpsPageMatches = SearchXpsOcrIndex(ocrIndex, query);
        }

        _abPlcXpsSearchFlIndices = xpsPageMatches
            .Concat(mappingPageMatches)
            .Where(page => page > 0)
            .Distinct()
            .OrderBy(page => page)
            .ToList();

        if (_abPlcXpsSearchFlIndices.Count > 0)
        {
            _abPlcXpsSearchIndex = 0;
            AbPlcXpsSearchStatusText.Text = $"Match 1 of {_abPlcXpsSearchFlIndices.Count} (map:{mappingPageMatches.Count}, xps:{xpsPageMatches.Count})";
            NavigateToAbPlcXpsSearchCurrent();
            return;
        }

        AbPlcXpsSearchStatusText.Text = matchCount > 0
            ? $"Found in {matchCount} mapping rows, but no XPS file is open"
            : "No matches";
    }

    private void NavigateAbPlcXpsSearch(int delta)
    {
        if (_abPlcXpsSearchFlIndices.Count == 0)
        {
            return;
        }

        _abPlcXpsSearchIndex =
            (_abPlcXpsSearchIndex + delta + _abPlcXpsSearchFlIndices.Count) % _abPlcXpsSearchFlIndices.Count;
        NavigateToAbPlcXpsSearchCurrent();
    }

    private void NavigateToAbPlcXpsSearchCurrent()
    {
        AbPlcXpsPrevButton.IsEnabled = true;
        AbPlcXpsNextButton.IsEnabled = true;
        AbPlcXpsSearchStatusText.Text = $"Match {_abPlcXpsSearchIndex + 1} of {_abPlcXpsSearchFlIndices.Count}";
        NavigateDocumentViewerToPage(AbPlcXpsViewer, _abPlcXpsSearchFlIndices[_abPlcXpsSearchIndex]);
        SyncViewerFindHighlight(
            AbPlcXpsViewer,
            BuildXpsFindTermForRegisterAddress(AbPlcXpsSearchBox.Text ?? string.Empty),
            AbPlcXpsSearchStatusText);
    }

    private async Task RunModiconXpsSearchAsync()
    {
        var query = ModiconXpsSearchBox.Text?.Trim() ?? string.Empty;
        _modiconXpsSearchFlIndices = [];
        _modiconXpsSearchIndex = -1;
        ModiconXpsPrevButton.IsEnabled = false;
        ModiconXpsNextButton.IsEnabled = false;

        if (string.IsNullOrWhiteSpace(query))
        {
            ModiconXpsSearchStatusText.Text = string.Empty;
            return;
        }

        var matchCount = 0;
        var mappingPageMatches = new List<int>();
        if (_activeModiconSystemId.HasValue)
        {
            (matchCount, mappingPageMatches) = await _database.GetFlIndicesForXpsSearchAsync(
                _activeModiconSystemId.Value, isAbPlc: false, query);
        }

        var currentXpsPath = (ModiconFilesList.SelectedItem as ModiconProgramFile)?.FilePath;
        var xpsPageMatches = new List<int>();
        if (!string.IsNullOrWhiteSpace(currentXpsPath) && File.Exists(currentXpsPath))
        {
            ModiconXpsSearchStatusText.Text = "Searching open XPS pages...";
            var ocrIndex = await GetOrBuildXpsOcrIndexAsync(
                currentXpsPath,
                status => ModiconXpsSearchStatusText.Text = status);

            xpsPageMatches = SearchXpsOcrIndex(ocrIndex, query);
        }

        _modiconXpsSearchFlIndices = xpsPageMatches
            .Concat(mappingPageMatches)
            .Where(page => page > 0)
            .Distinct()
            .OrderBy(page => page)
            .ToList();

        if (_modiconXpsSearchFlIndices.Count > 0)
        {
            _modiconXpsSearchIndex = 0;
            ModiconXpsSearchStatusText.Text = $"Match 1 of {_modiconXpsSearchFlIndices.Count} (map:{mappingPageMatches.Count}, xps:{xpsPageMatches.Count})";
            NavigateToModiconXpsSearchCurrent();
            return;
        }

        ModiconXpsSearchStatusText.Text = matchCount > 0
            ? $"Found in {matchCount} mapping rows, but no XPS file is open"
            : "No matches";
    }

    private void NavigateModiconXpsSearch(int delta)
    {
        if (_modiconXpsSearchFlIndices.Count == 0)
        {
            return;
        }

        _modiconXpsSearchIndex =
            (_modiconXpsSearchIndex + delta + _modiconXpsSearchFlIndices.Count) % _modiconXpsSearchFlIndices.Count;
        NavigateToModiconXpsSearchCurrent();
    }

    private void NavigateToModiconXpsSearchCurrent()
    {
        ModiconXpsPrevButton.IsEnabled = true;
        ModiconXpsNextButton.IsEnabled = true;
        ModiconXpsSearchStatusText.Text = $"Match {_modiconXpsSearchIndex + 1} of {_modiconXpsSearchFlIndices.Count}";
        NavigateDocumentViewerToPage(ModiconXpsViewer, _modiconXpsSearchFlIndices[_modiconXpsSearchIndex]);
        SyncViewerFindHighlight(
            ModiconXpsViewer,
            BuildXpsFindTermForRegisterAddress(ModiconXpsSearchBox.Text ?? string.Empty),
            ModiconXpsSearchStatusText);
    }

    private static void SyncViewerFindHighlight(DocumentViewer viewer, string? query, TextBlock statusText)
    {
        var term = (query ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(term) || viewer.Document is null)
        {
            return;
        }

        // Run after page navigation and render settle so native highlight lands on the target rung.
        viewer.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                if (!TryRunNativeFind(viewer, term))
                {
                    // One retry after UI settles further.
                    viewer.Dispatcher.BeginInvoke(
                        DispatcherPriority.ApplicationIdle,
                        new Action(() =>
                        {
                            if (!TryRunNativeFind(viewer, term))
                            {
                                statusText.Text += " (native highlight unavailable)";
                            }
                        }));
                }
            }));
    }

    private static bool TryRunNativeFind(DocumentViewer viewer, string term)
    {
        if (viewer.Document is null || string.IsNullOrWhiteSpace(term))
        {
            return false;
        }

        var highlightTerm = BuildXpsFindTermForRegisterAddress(term);
        if (string.IsNullOrWhiteSpace(highlightTerm))
        {
            return false;
        }

        viewer.Focus();
        if (ApplicationCommands.Find.CanExecute(null, viewer))
        {
            ApplicationCommands.Find.Execute(null, viewer);
        }
        else
        {
            viewer.Find();
        }
        var findBox = FindDocumentViewerFindTextBox(viewer);
        if (findBox is null)
        {
            return false;
        }

        findBox.Text = highlightTerm;
        findBox.CaretIndex = findBox.Text.Length;
        findBox.SelectAll();
        findBox.Focus();

        if (NavigationCommands.Search.CanExecute(highlightTerm, findBox))
        {
            NavigationCommands.Search.Execute(highlightTerm, findBox);
        }
        else if (NavigationCommands.Search.CanExecute(highlightTerm, viewer))
        {
            NavigationCommands.Search.Execute(highlightTerm, viewer);
        }

        var source = PresentationSource.FromVisual(findBox);
        if (source is null)
        {
            return false;
        }

        // Force the same path as manual bottom find (Enter in the find box).
        var keyArgs = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Enter)
        {
            RoutedEvent = Keyboard.KeyDownEvent
        };
        findBox.RaiseEvent(keyArgs);
        findBox.RaiseEvent(keyArgs);
        return true;
    }

    private static TextBox? FindDocumentViewerFindTextBox(DependencyObject root)
    {
        foreach (var textBox in FindVisualDescendants<TextBox>(root))
        {
            if (HasAncestorTypeName(textBox, "FindToolBar"))
            {
                return textBox;
            }
        }

        return null;
    }

    private static bool HasAncestorTypeName(DependencyObject node, string typeNameFragment)
    {
        var current = VisualTreeHelper.GetParent(node);
        while (current is not null)
        {
            var typeName = current.GetType().Name;
            if (typeName.IndexOf(typeNameFragment, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindVisualDescendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    private async Task<bool> OpenPlcReferenceInXpsAsync(IReadOnlyList<ChartPlcReference> references, int referenceIndex)
    {
        if (references.Count == 0 || referenceIndex < 0 || referenceIndex >= references.Count)
        {
            return false;
        }

        var plcReference = references[referenceIndex];
        var totalReferenceCount = references.Count;
        var registerAddress = (plcReference.RegisterAddress ?? string.Empty).Trim();
        var sourceLine = (plcReference.SourceLine ?? string.Empty).Trim();
        var summary = string.IsNullOrWhiteSpace(sourceLine)
            ? $"PLC refs: showing {referenceIndex + 1} of {totalReferenceCount} for {registerAddress}"
            : $"PLC refs: showing {referenceIndex + 1} of {totalReferenceCount} for {registerAddress} ({sourceLine})";

        if (IsAbPlcReference(plcReference))
        {
            MainTabControl.SelectedIndex = 6;
            _suppressAbPlcFileSelectionDisplay = true;
            try
            {
                await LoadAbPlcSystemAsync(plcReference.PlcSystemId);
                var match = FindFirstXpsReferenceMatch(_abPlcLadderFiles, plcReference);
                var ladderFile = match.File as AbPlcLadderFile ?? _abPlcLadderFiles.FirstOrDefault();
                if (ladderFile is null)
                {
                    return false;
                }

                AbPlcFilesList.SelectedItem = ladderFile;
                DisplayAbPlcXps(ladderFile, summary, match.PageNumber);
            }
            finally
            {
                _suppressAbPlcFileSelectionDisplay = false;
            }
            return true;
        }

        MainTabControl.SelectedIndex = 7;
        _suppressModiconFileSelectionDisplay = true;
        try
        {
            await LoadModiconSystemAsync(plcReference.PlcSystemId);
            var modiconMatch = FindFirstXpsReferenceMatch(_modiconProgramFiles, plcReference);
            var programFile = modiconMatch.File as ModiconProgramFile ?? _modiconProgramFiles.FirstOrDefault();
            if (programFile is null)
            {
                return false;
            }

            ModiconFilesList.SelectedItem = programFile;
            DisplayModiconXps(programFile, summary, modiconMatch.PageNumber);
        }
        finally
        {
            _suppressModiconFileSelectionDisplay = false;
        }
        return true;
    }

    private static (object? File, int? PageNumber) FindFirstXpsReferenceMatch<TFile>(
        IEnumerable<TFile> files,
        ChartPlcReference plcReference)
        where TFile : class
    {
        var candidates = BuildXpsSearchCandidates(plcReference);
        var flIndex = 0;
        var canUseFlAsPageFallback = !plcReference.SourceLine.StartsWith("SERIAL ARRAY ", StringComparison.OrdinalIgnoreCase)
                                     && TryExtractFlIndex(plcReference.SourceLine, out flIndex);

        TFile? firstValidFile = null;

        foreach (var file in files)
        {
            var filePath = file switch
            {
                AbPlcLadderFile ladder => ladder.FilePath,
                ModiconProgramFile program => program.FilePath,
                _ => string.Empty
            };

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                continue;
            }

            firstValidFile ??= file;

            if (candidates.Count > 0)
            {
                var pageNumber = FindFirstXpsPageContainingAny(filePath, candidates);
                if (pageNumber.HasValue)
                {
                    return (file, pageNumber);
                }
            }
        }

        // Text search failed (e.g. image-based XPS). Fall back to FL index as page number.
        // The caller will pick the first available file if File is null.
        if (canUseFlAsPageFallback && flIndex > 0)
        {
            return (firstValidFile, flIndex);
        }

        return (null, null);
    }

    private static List<string> BuildXpsSearchCandidates(ChartPlcReference plcReference)
    {
        var candidates = new List<string>();

        void AddCandidate(string value)
        {
            var trimmed = value.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return;
            }

            if (!candidates.Any(x => x.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                candidates.Add(trimmed);
            }
        }

        AddCandidate(plcReference.SourceLine ?? string.Empty);
        AddCandidate(plcReference.RegisterAddress ?? string.Empty);
        AddCandidate(plcReference.TagName ?? string.Empty);

        return candidates;
    }

    private static int? FindFirstXpsPageContainingAny(string xpsPath, IReadOnlyList<string> candidates)
    {
        try
        {
            using var archive = ZipFile.OpenRead(xpsPath);
            var pageEntries = archive.Entries
                .Where(entry => entry.FullName.EndsWith(".fpage", StringComparison.OrdinalIgnoreCase))
                .Select(entry => new
                {
                    Entry = entry,
                    PageNumber = TryParseXpsPageNumber(entry.FullName)
                })
                .Where(x => x.PageNumber.HasValue)
                .OrderBy(x => x.PageNumber!.Value)
                .ToList();

            foreach (var pageEntry in pageEntries)
            {
                using var stream = pageEntry.Entry.Open();
                using var reader = new StreamReader(stream);
                var content = reader.ReadToEnd();
                foreach (var candidate in candidates)
                {
                    if (content.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return pageEntry.PageNumber;
                    }
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static int? TryParseXpsPageNumber(string fullName)
    {
        var match = Regex.Match(fullName, @"(?<page>\d+)\.fpage$", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        return int.TryParse(match.Groups["page"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pageNumber)
            ? pageNumber
            : null;
    }

    private static void NavigateDocumentViewerToPage(DocumentViewer viewer, int? pageNumber)
    {
        if (pageNumber is null or < 1)
        {
            return;
        }

        // Delay the page jump until the document is fully attached/rendered.
        viewer.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => viewer.GoToPage(pageNumber.Value)));
    }

    private async Task<Dictionary<int, string>> GetOrBuildXpsOcrIndexAsync(string xpsPath, Action<string> onProgress)
    {
        if (_xpsOcrCache.TryGetValue(xpsPath, out var cached))
        {
            return cached;
        }

        var index = new Dictionary<int, string>();

        OcrEngine? ocrEngine = null;
        try
        {
            ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();
        }
        catch
        {
            ocrEngine = null;
        }

        try
        {
            using var archive = ZipFile.OpenRead(xpsPath);
            var pageEntries = archive.Entries
                .Where(e => e.FullName.EndsWith(".fpage", StringComparison.OrdinalIgnoreCase))
                .Select(e => new { Entry = e, PageNumber = TryParseXpsPageNumber(e.FullName) })
                .Where(x => x.PageNumber.HasValue)
                .OrderBy(x => x.PageNumber!.Value)
                .ToList();

            var total = pageEntries.Count;
            for (var i = 0; i < pageEntries.Count; i++)
            {
                onProgress($"Indexing XPS text: page {i + 1} of {total}...");

                var page = pageEntries[i];
                string fpageContent;
                using (var fpageStream = page.Entry.Open())
                using (var reader = new StreamReader(fpageStream))
                {
                    fpageContent = reader.ReadToEnd();
                }

                // First path: extract text directly from XPS Glyphs UnicodeString.
                var extractedText = ExtractTextFromFpage(fpageContent);
                if (!string.IsNullOrWhiteSpace(extractedText))
                {
                    index[page.PageNumber!.Value] = extractedText;
                    continue;
                }

                // Second path: OCR image-backed pages when OCR engine is available.
                if (ocrEngine is null)
                {
                    continue;
                }

                var imageSourceMatch = Regex.Match(
                    fpageContent,
                    @"ImageSource\s*=\s*""([^""]+)""",
                    RegexOptions.IgnoreCase);

                if (!imageSourceMatch.Success)
                {
                    continue;
                }

                var imageSource = imageSourceMatch.Groups[1].Value.Replace('\\', '/');
                var imagePath = imageSource.StartsWith('/')
                    ? imageSource.TrimStart('/')
                    : ResolveXpsRelativePath(page.Entry.FullName, imageSource);
                var imageEntry = archive.GetEntry(imagePath)
                    ?? archive.Entries.FirstOrDefault(e =>
                        e.FullName.EndsWith(IOPath.GetFileName(imagePath), StringComparison.OrdinalIgnoreCase));
                if (imageEntry == null)
                {
                    continue;
                }

                try
                {
                    byte[] imageBytes;
                    using (var imgStream = imageEntry.Open())
                    using (var memStream = new MemoryStream())
                    {
                        imgStream.CopyTo(memStream);
                        imageBytes = memStream.ToArray();
                    }

                    using var iras = new InMemoryRandomAccessStream();
                    var dataWriter = new DataWriter(iras.GetOutputStreamAt(0));
                    dataWriter.WriteBytes(imageBytes);
                    await dataWriter.StoreAsync();
                    dataWriter.DetachStream();
                    iras.Seek(0);

                    var decoder = await BitmapDecoder.CreateAsync(iras);
                    var rawBitmap = await decoder.GetSoftwareBitmapAsync();

                    SoftwareBitmap ocrBitmap;
                    if (rawBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8
                        || rawBitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
                    {
                        ocrBitmap = SoftwareBitmap.Convert(
                            rawBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                        rawBitmap.Dispose();
                    }
                    else
                    {
                        ocrBitmap = rawBitmap;
                    }

                    using (ocrBitmap)
                    {
                        onProgress($"Running OCR: page {i + 1} of {total}...");
                        var ocrResult = await ocrEngine.RecognizeAsync(ocrBitmap);
                        index[page.PageNumber!.Value] = ocrResult.Text;
                    }
                }
                catch
                {
                    // Skip pages that fail to decode or OCR
                }
            }
        }
        catch
        {
            return index;
        }

        _xpsOcrCache[xpsPath] = index;
        return index;
    }

    private static string ExtractTextFromFpage(string fpageContent)
    {
        if (string.IsNullOrWhiteSpace(fpageContent))
        {
            return string.Empty;
        }

        var matches = Regex.Matches(
            fpageContent,
            @"UnicodeString\s*=\s*(?:""([^""]*)""|'([^']*)')",
            RegexOptions.IgnoreCase);

        if (matches.Count == 0)
        {
            return string.Empty;
        }

        var chunks = new List<string>(matches.Count);
        foreach (Match match in matches)
        {
            var raw = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var decoded = WebUtility.HtmlDecode(raw).Trim();
            if (!string.IsNullOrWhiteSpace(decoded))
            {
                chunks.Add(decoded);
            }
        }

        return chunks.Count == 0 ? string.Empty : string.Join("\n", chunks);
    }

    private static List<int> SearchXpsOcrIndex(IReadOnlyDictionary<int, string> ocrIndex, string query)
    {
        var trimmedQuery = (query ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmedQuery))
        {
            return [];
        }

        var directMatches = ocrIndex
            .Where(kv => kv.Value.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .OrderBy(p => p)
            .ToList();
        if (directMatches.Count > 0)
        {
            return directMatches;
        }

        // Ladder XPS commonly renders bit addresses split across runs/lines:
        // "N22:44" on one line and "10" below. Allow N22:44/10 queries to match that layout.
        var slashIndex = trimmedQuery.IndexOf('/');
        if (slashIndex <= 0 || slashIndex >= trimmedQuery.Length - 1)
        {
            return [];
        }

        var baseToken = trimmedQuery[..slashIndex].Trim();
        var bitToken = trimmedQuery[(slashIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(baseToken) || string.IsNullOrWhiteSpace(bitToken))
        {
            return [];
        }

        var escapedBase = Regex.Escape(baseToken);
        var escapedBit = Regex.Escape(bitToken);
        var splitPattern = new Regex(
            $@"(?<![A-Za-z0-9]){escapedBase}(?:\s|[\r\n])+{escapedBit}(?![A-Za-z0-9])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        var splitMatches = ocrIndex
            .Where(kv => splitPattern.IsMatch(kv.Value))
            .Select(kv => kv.Key)
            .OrderBy(p => p)
            .ToList();
        if (splitMatches.Count > 0)
        {
            return splitMatches;
        }

        // AB Logix tags often render as N12[2].8 instead of N12:2/8.
        if (TryParsePlcRegisterSortParts(trimmedQuery, out var filePrefix, out var fileNumber, out var word, out var bit)
            && bit >= 0)
        {
            var logixPattern = new Regex(
                $@"(?<![A-Za-z0-9_]){Regex.Escape(filePrefix)}{fileNumber}\s*\[\s*{word}\s*\]\s*\.\s*{bit}(?![A-Za-z0-9_])",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);
            var logixMatches = ocrIndex
                .Where(kv => logixPattern.IsMatch(kv.Value))
                .Select(kv => kv.Key)
                .OrderBy(p => p)
                .ToList();
            if (logixMatches.Count > 0)
            {
                return logixMatches;
            }
        }

        // For exact bit searches, avoid loose token fallback (causes false positives).
        if (slashIndex > 0)
        {
            return [];
        }

        // OCR text can alter punctuation. Fall back to token-sequence matching.
        var queryTokens = ExtractAlphaNumericTokens($"{baseToken} {bitToken}");
        if (queryTokens.Count == 0)
        {
            return [];
        }

        return ocrIndex
            .Where(kv => ContainsTokenSequenceWithMaxGap(ExtractAlphaNumericTokens(kv.Value), queryTokens, maxGap: 8))
            .Select(kv => kv.Key)
            .OrderBy(p => p)
            .ToList();
    }

    private static List<string> ExtractAlphaNumericTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return Regex.Matches(text.ToUpperInvariant(), @"[A-Z0-9]+")
            .Select(match => match.Value)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToList();
    }

    private static bool ContainsTokenSequenceWithMaxGap(
        IReadOnlyList<string> sourceTokens,
        IReadOnlyList<string> queryTokens,
        int maxGap)
    {
        if (sourceTokens.Count == 0 || queryTokens.Count == 0)
        {
            return false;
        }

        for (var start = 0; start < sourceTokens.Count; start++)
        {
            if (!sourceTokens[start].Equals(queryTokens[0], StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var sourceIndex = start;
            var matched = true;
            for (var queryIndex = 1; queryIndex < queryTokens.Count; queryIndex++)
            {
                var nextMatch = -1;
                var maxIndex = Math.Min(sourceTokens.Count - 1, sourceIndex + maxGap + 1);
                for (var cursor = sourceIndex + 1; cursor <= maxIndex; cursor++)
                {
                    if (sourceTokens[cursor].Equals(queryTokens[queryIndex], StringComparison.OrdinalIgnoreCase))
                    {
                        nextMatch = cursor;
                        break;
                    }
                }

                if (nextMatch < 0)
                {
                    matched = false;
                    break;
                }

                sourceIndex = nextMatch;
            }

            if (matched)
            {
                return true;
            }
        }

        return false;
    }

    private static string ResolveXpsRelativePath(string fpageFullName, string relativeSource)
    {
        // Get the directory of the fpage within the XPS ZIP
        var lastSlash = fpageFullName.LastIndexOf('/');
        var fpageDir = lastSlash >= 0 ? fpageFullName[..(lastSlash + 1)] : string.Empty;

        // Combine and normalize (handle ../ segments)
        var combined = fpageDir + relativeSource;
        var parts = combined.Split('/');
        var resolved = new List<string>();
        foreach (var part in parts)
        {
            if (part == "..")
            {
                if (resolved.Count > 0)
                {
                    resolved.RemoveAt(resolved.Count - 1);
                }
            }
            else if (part != "." && part.Length > 0)
            {
                resolved.Add(part);
            }
        }

        return string.Join("/", resolved);
    }

    private static IReadOnlyList<ChartPlcReference> GetDistinctPlcRungReferences(IReadOnlyList<ChartPlcReference> references)
    {
        if (references.Count == 0)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var distinct = new List<ChartPlcReference>();
        foreach (var reference in references
                     .OrderBy(x => IsAbPlcReference(x) ? 0 : 1)
                     .ThenBy(x => x.PlcName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(x => x.RegisterAddress, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(x => x.SourceLine, StringComparer.OrdinalIgnoreCase))
        {
            var key = $"{(IsAbPlcReference(reference) ? "AB" : "MODICON")}|{reference.PlcSystemId}|{reference.RegisterAddress}|{reference.SourceLine}";
            if (!seen.Add(key))
            {
                continue;
            }

            distinct.Add(reference);
        }

        return distinct;
    }

    private static bool IsAbPlcReference(ChartPlcReference reference) =>
        string.IsNullOrWhiteSpace(reference.InterfaceType)
        || reference.InterfaceType.Equals("AB", StringComparison.OrdinalIgnoreCase)
        || reference.InterfaceType.Equals("ALLENBRADLEY", StringComparison.OrdinalIgnoreCase);

    private static string FormatPlcReferenceForList(ChartPlcReference reference)
    {
        var interfaceType = IsAbPlcReference(reference) ? "AB" : "MODICON";
        var plcName = string.IsNullOrWhiteSpace(reference.PlcName) ? $"PLC #{reference.PlcSystemId}" : reference.PlcName.Trim();
        var registerAddress = (reference.RegisterAddress ?? string.Empty).Trim();
        var sourceLine = (reference.SourceLine ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(sourceLine)
            ? $"{interfaceType} {plcName} {registerAddress}"
            : $"{interfaceType} {plcName} {registerAddress} [{sourceLine}]";
    }

    private void ClearAbPlcXpsDocument()
    {
        AbPlcXpsViewer.Document = null;
        _activeAbPlcXpsDocument?.Close();
        _activeAbPlcXpsDocument = null;
    }

    private void ClearModiconXpsDocument()
    {
        ModiconXpsViewer.Document = null;
        _activeModiconXpsDocument?.Close();
        _activeModiconXpsDocument = null;
    }

    private async Task OpenClProgramAsync(long clProgramId, bool switchToClTab)
    {
        if (switchToClTab)
        {
            MainTabControl.SelectedIndex = 2;
        }

        var match = _clResults.FirstOrDefault(x => x.ClProgramId == clProgramId);
        if (match is not null)
        {
            ClResultsList.SelectedItem = match;
            await LoadClProgramAsync(match.ClProgramId);
            return;
        }

        await LoadClProgramAsync(clProgramId);
    }

    private async Task OpenLogicBlockAsync(long entityId, bool switchToLogicTab)
    {
        if (switchToLogicTab)
        {
            MainTabControl.SelectedIndex = 3;
        }

        var match = _logicResults.FirstOrDefault(x => x.EntityId == entityId);
        if (match is not null)
        {
            LogicResultsList.SelectedItem = match;
            await LoadLogicBlockAsync(match.EntityId);
            return;
        }

        await LoadLogicBlockAsync(entityId);
    }

    private async Task OpenChartEntityAsync(long entityId)
    {
        MainTabControl.SelectedIndex = 4;

        if (ChartResultsList.SelectedItem is EntitySearchResult selected
            && selected.EntityId == entityId)
        {
            await LoadChartAsync(entityId);
            return;
        }

        var match = _chartResults.FirstOrDefault(x => x.EntityId == entityId);
        if (match is null)
        {
            var entity = await _database.GetEntitySearchResultByIdAsync(entityId);
            if (entity is not null)
            {
                _chartResults.Insert(0, entity);
                ChartSummaryText.Text = $"{_chartResults.Count} matches";
                match = entity;
            }
        }

        if (match is not null)
        {
            ChartResultsList.SelectedItem = match;
            return;
        }

        await LoadChartAsync(entityId);
    }

    private async Task OpenDetailEntityAsync(long entityId)
    {
        MainTabControl.SelectedIndex = 1;

        if (EntityResultsList.SelectedItem is EntitySearchResult selected
            && selected.EntityId == entityId)
        {
            _selectedDetailEntityId = entityId;
            await LoadEntityDetailsAsync(entityId);
            return;
        }

        var match = _searchResults.FirstOrDefault(x => x.EntityId == entityId);
        if (match is null)
        {
            var entity = await _database.GetEntitySearchResultByIdAsync(entityId);
            if (entity is not null)
            {
                _searchResults.Insert(0, entity);
                SearchSummaryText.Text = $"{_searchResults.Count} matches";
                match = entity;
            }
        }

        if (match is not null)
        {
            EntityResultsList.SelectedItem = match;
            return;
        }

        EntityResultsList.SelectedItem = null;
        _selectedDetailEntityId = entityId;
        await LoadEntityDetailsAsync(entityId);
    }

    private async Task OpenHardwareLinkAsync(long boxEntityId, int? moduleNumber, long? pointEntityId)
    {
        MainTabControl.SelectedIndex = 5;

        var match = _hardwareBoxes.FirstOrDefault(x => x.BoxEntityId == boxEntityId);
        if (match is not null)
        {
            HardwareResultsList.SelectedItem = match;
        }

        if (_activeHardwareBoxDetail is null || _activeHardwareBoxDetail.BoxEntityId != boxEntityId)
        {
            await LoadHardwareBoxAsync(boxEntityId);
        }

        if (_activeHardwareBoxDetail is null || _activeHardwareBoxDetail.BoxEntityId != boxEntityId)
        {
            return;
        }

        if (match is null)
        {
            _hardwareBoxes.Insert(0, new HardwareBoxSummary
            {
                BoxEntityId = _activeHardwareBoxDetail.BoxEntityId,
                Name = _activeHardwareBoxDetail.Name,
                EntityType = _activeHardwareBoxDetail.EntityType,
                SourceFile = _activeHardwareBoxDetail.SourceFile,
                HighwayNumber = _activeHardwareBoxDetail.HighwayNumber,
                BoxNumber = _activeHardwareBoxDetail.BoxNumber,
                NetworkNumber = _activeHardwareBoxDetail.NetworkNumber,
                NodeNumber = _activeHardwareBoxDetail.NodeNumber,
                ModuleCount = _activeHardwareBoxDetail.Modules.Count
            });
            HardwareSummaryText.Text = $"{_hardwareBoxes.Count} hardware boxes";
            HardwareResultsList.SelectedItem = _hardwareBoxes[0];
        }

        if (moduleNumber is not int requestedModule || requestedModule <= 0)
        {
            if (pointEntityId is long pointIdNoModule)
            {
                await SelectHardwarePointIfPresentAsync(pointIdNoModule);
            }
            return;
        }

        var module = FindModuleForHardwareLink(_activeHardwareBoxDetail.Modules, requestedModule);
        if (module is null)
        {
            _activeHardwareModule = null;
            _hardwarePoints.Clear();
            ClearHardwareTrafficCopPanel();
            HardwareHeaderText.Text =
                $"{_activeHardwareBoxDetail.Name} [{_activeHardwareBoxDetail.EntityType}]  No IO module mapping for MODNUM:{requestedModule:00}  [{_activeHardwareBoxDetail.SourceFile}]";
            if (pointEntityId is long pointIdNoModuleMatch)
            {
                await SelectHardwarePointIfPresentAsync(pointIdNoModuleMatch);
            }
            return;
        }

        await LoadHardwareModulePointsAsync(module);
        if (pointEntityId is long pointId)
        {
            await SelectHardwarePointIfPresentAsync(pointId);
        }
    }

    private async Task<bool> PromptSaveIfDirtyAsync(string actionDescription)
    {
        var hasNeverBeenSaved = string.IsNullOrWhiteSpace(GetKnownProjectPath());
        var shouldPrompt = _hasUnsavedProjectChanges;

        if (!shouldPrompt && hasNeverBeenSaved)
        {
            var counts = await _database.GetImportCountsAsync();
            shouldPrompt =
                counts.ImportedFiles > 0
                || counts.Entities > 0
                || counts.ClPrograms > 0
                || counts.AbSystems > 0
                || counts.ModiconSystems > 0
                || counts.SoftConnections > 0
                || counts.OpcIntegratorMappings > 0
                || counts.GraphicsMappings > 0
                || counts.PiMappings > 0;
        }

        if (!shouldPrompt)
        {
            return true;
        }

        var promptText = hasNeverBeenSaved && !_hasUnsavedProjectChanges
            ? $"Current project has not been saved yet. Save it before {actionDescription}?"
            : $"Save current project before {actionDescription}?";

        var result = MessageBox.Show(
            this,
            promptText,
            "Unsaved Changes",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Cancel)
        {
            return false;
        }

        if (result == MessageBoxResult.Yes)
        {
            return await SaveProjectAsync(promptForPath: string.IsNullOrWhiteSpace(GetKnownProjectPath()));
        }

        return true;
    }

    private async Task StartNewProjectAsync()
    {
        try
        {
            await _database.ClearAllDataAsync();
            _entityLookup = null;
            _selectedDetailEntityId = null;
            _activeChartEntityId = null;
            _activeLogicEntityId = null;
            _activeAbPlcSystemId = null;
            _activeModiconSystemId = null;
            _activeHardwareBoxDetail = null;
            _activeHardwareModule = null;
            ClearAbPlcXpsDocument();
            ClearModiconXpsDocument();
            _currentProjectPath = null;
            _projectPassword = null;
            PersistCurrentProjectPath();
            _hasUnsavedProjectChanges = false;
            SearchTextBox.Text = string.Empty;
            ClSearchTextBox.Text = string.Empty;
            LogicSearchTextBox.Text = string.Empty;
            ChartSearchTextBox.Text = string.Empty;
            HardwareSearchTextBox.Text = string.Empty;
            AbPlcSearchTextBox.Text = string.Empty;
            ModiconSearchTextBox.Text = string.Empty;

            ImportSummaryText.Text = "No files imported yet.";
            ClImportSummaryText.Text = "No CL folder indexed yet.";
            AbPlcImportSummaryText.Text = "No AB PLC5 files imported yet.";
            AbLogixImportSummaryText.Text = "No AB Logix files imported yet.";
            ModiconImportSummaryText.Text = "No Modicon files imported yet.";
            SoftConnectionsImportSummaryText.Text = "No soft connections imported yet.";
            OpcIntegratorImportSummaryText.Text = "No OPC Integrator mappings imported yet.";
            GraphicsPointsImportSummaryText.Text = "No HMIWeb graphics imported yet.";
            PiImportSummaryText.Text = "No PI mappings imported yet.";
            ExportPointsSummaryText.Text = "No point export run yet.";
            ChangeManagementSummaryText.Text = "No EB import comparison has been run yet.";
            _changeManagementEntries.Clear();
            _importLog.Clear();
            if (File.Exists(_unsavedImportLogPath))
            {
                File.Delete(_unsavedImportLogPath);
            }
            _importLog.Add($"[{DateTime.Now:HH:mm:ss}] Started a new project.");

            await ReloadAllViewsAsync();
            MainTabControl.SelectedIndex = 0;
            UpdateProjectStatusText();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to start a new project.\n\n{ex.Message}", "New Project Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task OpenProjectFromFileAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                MessageBox.Show(this, $"Project file not found.\n\n{filePath}", "Open Project Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            await LoadProjectFileIntoWorkingDatabaseAsync(filePath);
            _entityLookup = null;
            _selectedDetailEntityId = null;
            _activeChartEntityId = null;
            _activeLogicEntityId = null;
            _activeAbPlcSystemId = null;
            _activeModiconSystemId = null;
            _activeHardwareBoxDetail = null;
            _activeHardwareModule = null;
            ClearAbPlcXpsDocument();
            ClearModiconXpsDocument();
            SearchTextBox.Text = string.Empty;
            ClSearchTextBox.Text = string.Empty;
            LogicSearchTextBox.Text = string.Empty;
            ChartSearchTextBox.Text = string.Empty;
            HardwareSearchTextBox.Text = string.Empty;
            AbPlcSearchTextBox.Text = string.Empty;
            ModiconSearchTextBox.Text = string.Empty;

            await _database.InitializeAsync();
            await ReloadAllViewsAsync();
            await RefreshImportSummariesAsync();

            _currentProjectPath = filePath;
            PersistCurrentProjectPath();
            _hasUnsavedProjectChanges = false;
            LoadPersistedImportLogForProject(filePath);
            UpdateProjectStatusText();
            _importLog.Add($"[{DateTime.Now:HH:mm:ss}] Opened project: {IOPath.GetFileName(filePath)}");
            MainTabControl.SelectedIndex = 0;
        }
        catch (OperationCanceledException)
        {
            // User canceled password entry.
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to open project.\n\n{ex.Message}", "Open Project Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task<bool> SaveProjectAsync(bool promptForPath)
    {
        var knownProjectPath = GetKnownProjectPath();
        var previousProjectPath = knownProjectPath;
        var savePath = knownProjectPath;
        if (promptForPath || string.IsNullOrWhiteSpace(savePath))
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Corevanta Project (*.cvproj)|*.cvproj|SQLite Database (*.db)|*.db|All files (*.*)|*.*",
                DefaultExt = ".cvproj",
                AddExtension = true,
                OverwritePrompt = true,
                Title = "Save Corevanta Project"
            };

            if (!string.IsNullOrWhiteSpace(knownProjectPath))
            {
                dialog.FileName = IOPath.GetFileName(knownProjectPath);
            }

            if (dialog.ShowDialog(this) != true)
            {
                return false;
            }

            savePath = dialog.FileName;
        }

        try
        {
            await _database.InitializeAsync();
            await SaveWorkingDatabaseToProjectFileAsync(savePath!);
            if (string.IsNullOrWhiteSpace(previousProjectPath))
            {
                PromoteUnsavedImportLogToProject(savePath!);
            }
            else
            {
                CopyProjectImportLogIfNeeded(previousProjectPath, savePath!);
            }
            _currentProjectPath = savePath;
            PersistCurrentProjectPath();
            _hasUnsavedProjectChanges = false;
            UpdateProjectStatusText();
            _importLog.Add($"[{DateTime.Now:HH:mm:ss}] Saved project: {IOPath.GetFileName(savePath)}");
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to save project.\n\n{ex.Message}", "Save Project Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private async Task ReloadAllViewsAsync()
    {
        await RunSearchAsync();
        await RunClSearchAsync();
        await RunLogicSearchAsync();
        await RunChartSearchAsync();
        await RunAbPlcSearchAsync();
        await RunModiconSearchAsync();
        await RunHardwareSearchAsync();
        await RefreshChangeManagementAsync();
    }

    private void RestoreCurrentProjectPath()
    {
        try
        {
            if (!File.Exists(_projectStatePath))
            {
                _currentProjectPath = null;
                return;
            }

            var savedPath = File.ReadAllText(_projectStatePath).Trim();
            _currentProjectPath = !string.IsNullOrWhiteSpace(savedPath) && File.Exists(savedPath)
                ? savedPath
                : null;
        }
        catch
        {
            _currentProjectPath = null;
        }
    }

    private string? GetKnownProjectPath()
    {
        if (!string.IsNullOrWhiteSpace(_currentProjectPath))
        {
            return _currentProjectPath;
        }

        try
        {
            if (!File.Exists(_projectStatePath))
            {
                return null;
            }

            var savedPath = File.ReadAllText(_projectStatePath).Trim();
            if (string.IsNullOrWhiteSpace(savedPath) || !File.Exists(savedPath))
            {
                return null;
            }

            _currentProjectPath = savedPath;
            return _currentProjectPath;
        }
        catch
        {
            return null;
        }
    }

    private void PersistCurrentProjectPath()
    {
        try
        {
            var directory = IOPath.GetDirectoryName(_projectStatePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (string.IsNullOrWhiteSpace(_currentProjectPath))
            {
                if (File.Exists(_projectStatePath))
                {
                    File.Delete(_projectStatePath);
                }

                return;
            }

            File.WriteAllText(_projectStatePath, _currentProjectPath);
        }
        catch
        {
            // Non-fatal: this only affects convenience path persistence for Save behavior.
        }
    }

    private async void RefreshChangeManagement_Click(object sender, RoutedEventArgs e)
    {
        await RefreshChangeManagementAsync();
    }

    private async Task RefreshChangeManagementAsync()
    {
        var entries = await _database.GetChangeManagementEntriesAsync();
        _changeManagementEntries.Clear();
        foreach (var entry in entries)
        {
            _changeManagementEntries.Add(entry);
        }

        var changedCount = entries.Count(x => string.Equals(x.ChangeType, "CHANGED", StringComparison.OrdinalIgnoreCase));
        var deletedCount = entries.Count(x => string.Equals(x.ChangeType, "DELETED", StringComparison.OrdinalIgnoreCase));

        ChangeManagementSummaryText.Text = entries.Count == 0
            ? "No EB import comparison has been run yet."
            : $"Running list: {entries.Count} records ({changedCount} changed, {deletedCount} deleted).";
    }

    private async Task RefreshImportSummariesAsync()
    {
        var counts = await _database.GetImportCountsAsync();

        ImportSummaryText.Text = counts.ImportedFiles > 0 || counts.Entities > 0
            ? $"Imported {counts.ImportedFiles} files, {counts.Entities} entities."
            : "No files imported yet.";

        ClImportSummaryText.Text = counts.ClPrograms > 0
            ? $"Indexed {counts.ClPrograms} CL files."
            : "No CL folder indexed yet.";

        var abSummary = counts.AbSystems > 0
            ? $"Imported {counts.AbSystems} AB system import(s)."
            : "No AB files imported yet.";
        AbPlcImportSummaryText.Text = abSummary;
        AbLogixImportSummaryText.Text = abSummary;

        ModiconImportSummaryText.Text = counts.ModiconSystems > 0
            ? $"Imported {counts.ModiconSystems} Modicon system(s)."
            : "No Modicon files imported yet.";

        SoftConnectionsImportSummaryText.Text = counts.SoftConnections > 0
            ? $"Imported soft connections: {counts.SoftConnections} rows."
            : "No soft connections imported yet.";
        OpcIntegratorImportSummaryText.Text = counts.OpcIntegratorMappings > 0
            ? $"OPC Integrator mappings imported: {counts.OpcIntegratorMappings} rows."
            : "No OPC Integrator mappings imported yet.";

        GraphicsPointsImportSummaryText.Text = counts.GraphicsMappings > 0
            ? $"HMIWeb graphics import: {counts.GraphicsMappings} rows."
            : "No HMIWeb graphics imported yet.";

        PiImportSummaryText.Text = counts.PiMappings > 0
            ? $"PI mappings imported: {counts.PiMappings} rows."
            : "No PI mappings imported yet.";
        ExportPointsSummaryText.Text = "No point export run yet.";
    }

    private void ConfigureProjectPassword()
    {
        if (!string.IsNullOrEmpty(_projectPassword))
        {
            var choice = MessageBox.Show(
                this,
                "Project password is set.\n\nYes: Change password\nNo: Remove password\nCancel: Keep current password",
                "Project Password",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (choice == MessageBoxResult.Cancel)
            {
                return;
            }

            if (choice == MessageBoxResult.No)
            {
                _projectPassword = null;
                UpdateProjectStatusText();
                _importLog.Add($"[{DateTime.Now:HH:mm:ss}] Project password removed for next save.");
                MarkProjectDirty();
                return;
            }
        }

        if (!TryPromptForPassword("Set Project Password", requireConfirmation: true, out var password))
        {
            return;
        }

        _projectPassword = password;
        UpdateProjectStatusText();
        _importLog.Add($"[{DateTime.Now:HH:mm:ss}] Project password updated for next save.");
        MarkProjectDirty();
    }

    private bool TryPromptForPassword(string title, bool requireConfirmation, out string password)
    {
        password = string.Empty;

        var dialog = new Window
        {
            Title = title,
            Width = 390,
            Height = requireConfirmation ? 215 : 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (requireConfirmation)
        {
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var prompt = new TextBlock
        {
            Text = requireConfirmation
                ? "Enter a password to protect this project file:"
                : "Enter the project password:",
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(prompt, 0);
        root.Children.Add(prompt);

        var passwordBox = new PasswordBox
        {
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(passwordBox, 1);
        root.Children.Add(passwordBox);

        PasswordBox? confirmBox = null;
        if (requireConfirmation)
        {
            confirmBox = new PasswordBox();
            Grid.SetRow(confirmBox, 2);
            root.Children.Add(confirmBox);
        }

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetRow(buttonPanel, requireConfirmation ? 4 : 3);

        var okButton = new Button
        {
            Content = "OK",
            Width = 80,
            IsDefault = true
        };
        okButton.Click += (_, _) => dialog.DialogResult = true;

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 80,
            Margin = new Thickness(8, 0, 0, 0),
            IsCancel = true
        };
        cancelButton.Click += (_, _) => dialog.DialogResult = false;

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        root.Children.Add(buttonPanel);

        dialog.Content = root;
        passwordBox.Focus();

        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        var entered = passwordBox.Password;
        if (string.IsNullOrWhiteSpace(entered))
        {
            MessageBox.Show(this, "Password cannot be empty.", title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (requireConfirmation && !string.Equals(entered, confirmBox!.Password, StringComparison.Ordinal))
        {
            MessageBox.Show(this, "Passwords do not match.", title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        password = entered;
        return true;
    }

    private async Task LoadProjectFileIntoWorkingDatabaseAsync(string filePath)
    {
        var sourceBytes = await File.ReadAllBytesAsync(filePath);
        if (!IsEncryptedProjectBytes(sourceBytes))
        {
            _projectPassword = null;
            CopyProjectFile(filePath, _workingDatabasePath);
            return;
        }

        if (!TryPromptForPassword("Open Password-Protected Project", requireConfirmation: false, out var password))
        {
            throw new OperationCanceledException("Password entry canceled.");
        }

        byte[] decryptedBytes;
        try
        {
            decryptedBytes = DecryptProjectBytes(sourceBytes, password);
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("Invalid project password.");
        }

        var targetDirectory = IOPath.GetDirectoryName(_workingDatabasePath);
        if (!string.IsNullOrWhiteSpace(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        var tempPath = $"{_workingDatabasePath}.tmp";
        await File.WriteAllBytesAsync(tempPath, decryptedBytes);
        File.Copy(tempPath, _workingDatabasePath, true);
        File.Delete(tempPath);

        _projectPassword = password;
    }

    private async Task SaveWorkingDatabaseToProjectFileAsync(string savePath)
    {
        if (string.IsNullOrWhiteSpace(_projectPassword))
        {
            CopyProjectFile(_workingDatabasePath, savePath);
            return;
        }

        var dbBytes = await ReadFileBytesWithRetryAsync(_workingDatabasePath);
        var encryptedBytes = EncryptProjectBytes(dbBytes, _projectPassword);
        var targetFullPath = IOPath.GetFullPath(savePath);
        var targetDirectory = IOPath.GetDirectoryName(targetFullPath);
        if (!string.IsNullOrWhiteSpace(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        var tempPath = $"{targetFullPath}.tmp";
        await File.WriteAllBytesAsync(tempPath, encryptedBytes);
        File.Copy(tempPath, targetFullPath, true);
        File.Delete(tempPath);
    }

    private static async Task<byte[]> ReadFileBytesWithRetryAsync(string path)
    {
        const int maxAttempts = 8;
        const int delayMs = 120;
        Exception? lastError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);

                var bytes = new byte[stream.Length];
                var offset = 0;
                while (offset < bytes.Length)
                {
                    var read = await stream.ReadAsync(bytes.AsMemory(offset, bytes.Length - offset));
                    if (read == 0)
                    {
                        break;
                    }

                    offset += read;
                }

                if (offset != bytes.Length)
                {
                    throw new IOException($"Could not read full file. Expected {bytes.Length} bytes, got {offset}.");
                }

                return bytes;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                lastError = ex;
                if (attempt == maxAttempts)
                {
                    break;
                }

                await Task.Delay(delayMs);
            }
        }

        throw new IOException(
            $"Unable to read project database file '{path}'. It may still be locked by an active operation.",
            lastError);
    }

    private static bool IsEncryptedProjectBytes(byte[] bytes)
    {
        if (bytes.Length < ProjectEncryptionMagic.Length + 1)
        {
            return false;
        }

        for (var i = 0; i < ProjectEncryptionMagic.Length; i++)
        {
            if (bytes[i] != ProjectEncryptionMagic[i])
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] EncryptProjectBytes(byte[] plainBytes, string password)
    {
        Span<byte> salt = stackalloc byte[16];
        Span<byte> nonce = stackalloc byte[12];
        RandomNumberGenerator.Fill(salt);
        RandomNumberGenerator.Fill(nonce);

        var key = DeriveProjectKey(password, salt, ProjectPasswordIterations);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[16];

        using (var aes = new AesGcm(key, 16))
        {
            aes.Encrypt(nonce, plainBytes, cipher, tag);
        }

        var output = new byte[ProjectEncryptionMagic.Length + 1 + 4 + salt.Length + nonce.Length + tag.Length + cipher.Length];
        var offset = 0;
        ProjectEncryptionMagic.CopyTo(output, offset);
        offset += ProjectEncryptionMagic.Length;
        output[offset++] = ProjectEncryptionVersion;
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset, 4), ProjectPasswordIterations);
        offset += 4;
        salt.CopyTo(output.AsSpan(offset, salt.Length));
        offset += salt.Length;
        nonce.CopyTo(output.AsSpan(offset, nonce.Length));
        offset += nonce.Length;
        tag.CopyTo(output.AsSpan(offset, tag.Length));
        offset += tag.Length;
        cipher.CopyTo(output, offset);

        return output;
    }

    private static byte[] DecryptProjectBytes(byte[] encryptedBytes, string password)
    {
        if (!IsEncryptedProjectBytes(encryptedBytes))
        {
            return encryptedBytes;
        }

        var offset = ProjectEncryptionMagic.Length;
        var version = encryptedBytes[offset++];
        if (version != ProjectEncryptionVersion)
        {
            throw new CryptographicException("Unsupported project encryption version.");
        }

        var iterations = BinaryPrimitives.ReadInt32LittleEndian(encryptedBytes.AsSpan(offset, 4));
        offset += 4;
        var salt = encryptedBytes.AsSpan(offset, 16).ToArray();
        offset += 16;
        var nonce = encryptedBytes.AsSpan(offset, 12).ToArray();
        offset += 12;
        var tag = encryptedBytes.AsSpan(offset, 16).ToArray();
        offset += 16;
        var cipher = encryptedBytes.AsSpan(offset).ToArray();

        var key = DeriveProjectKey(password, salt, iterations);
        var plain = new byte[cipher.Length];
        using (var aes = new AesGcm(key, 16))
        {
            aes.Decrypt(nonce, cipher, tag, plain);
        }

        return plain;
    }

    private static byte[] DeriveProjectKey(string password, ReadOnlySpan<byte> salt, int iterations)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt.ToArray(),
            iterations,
            HashAlgorithmName.SHA256,
            32);
    }

    private static void CopyProjectFile(string sourcePath, string targetPath)
    {
        var sourceFullPath = IOPath.GetFullPath(sourcePath);
        var targetFullPath = IOPath.GetFullPath(targetPath);
        if (sourceFullPath.Equals(targetFullPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var targetDirectory = IOPath.GetDirectoryName(targetFullPath);
        if (!string.IsNullOrWhiteSpace(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        var tempPath = $"{targetFullPath}.tmp";
        File.Copy(sourceFullPath, tempPath, true);
        File.Copy(tempPath, targetFullPath, true);
        File.Delete(tempPath);
    }

    private void MarkProjectDirty()
    {
        _hasUnsavedProjectChanges = true;
        UpdateProjectStatusText();
    }

    private void UpdateProjectStatusText()
    {
        var projectName = string.IsNullOrWhiteSpace(_currentProjectPath)
            ? "(unsaved working project)"
            : IOPath.GetFileName(_currentProjectPath);
        var passwordSuffix = string.IsNullOrEmpty(_projectPassword) ? string.Empty : " [Password]";
        ProjectStatusText.Text = _hasUnsavedProjectChanges
            ? $"Project: {projectName}{passwordSuffix} *"
            : $"Project: {projectName}{passwordSuffix}";
    }

    private async Task RenderChartDiagramAsync(ChartDetail rootDetail)
    {
        _entityLookup ??= await _database.GetEntityNameLookupAsync();
        var showPiMappings = ChartShowPiMappingsCheckBox.IsChecked != false;

        var detailCache = new Dictionary<long, ChartDetail?> { [rootDetail.EntityId] = rootDetail };

        async Task<ChartDetail?> GetDetailAsync(long entityId)
        {
            if (detailCache.TryGetValue(entityId, out var cached))
            {
                return cached;
            }

            var loaded = await _database.GetChartDetailAsync(entityId, includeClReferences: true);
            detailCache[entityId] = loaded;
            return loaded;
        }

        static bool IsCustomOrRegamEntity(ChartDetail detail) =>
            detail.EntityType.Equals("CUSTOMAM", StringComparison.OrdinalIgnoreCase)
            || detail.EntityType.Equals("REGAM", StringComparison.OrdinalIgnoreCase);

        static string NormalizeProgramKey(string value) =>
            string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToUpperInvariant();

        static bool IsAttachedProgramForEntity(ChartDetail detail, string programKey)
        {
            if (string.IsNullOrWhiteSpace(programKey))
            {
                return false;
            }

            if (NormalizeProgramKey(detail.Name).Equals(programKey, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return detail.PackageNames.Any(pkg =>
                NormalizeProgramKey(pkg).Equals(programKey, StringComparison.OrdinalIgnoreCase));
        }

        var rootAttachedProgramKeysToSuppress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (IsCustomOrRegamEntity(rootDetail))
        {
            var rootNameKey = NormalizeProgramKey(rootDetail.Name);
            if (!string.IsNullOrWhiteSpace(rootNameKey))
            {
                rootAttachedProgramKeysToSuppress.Add(rootNameKey);
            }

            foreach (var pkg in rootDetail.PackageNames)
            {
                var pkgKey = NormalizeProgramKey(pkg);
                if (!string.IsNullOrWhiteSpace(pkgKey))
                {
                    rootAttachedProgramKeysToSuppress.Add(pkgKey);
                }
            }
        }

        var rootEntityKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedRootEntityName = NormalizeChartEntityToken(rootDetail.Name);
        if (!string.IsNullOrWhiteSpace(normalizedRootEntityName))
        {
            rootEntityKeys.Add(normalizedRootEntityName);
        }

        bool ShouldSuppressChartClReference(ChartDetail ownerDetail, ChartClReference clReference)
        {
            var programKey = NormalizeProgramKey(clReference.ProgramName);
            if (string.IsNullOrWhiteSpace(programKey))
            {
                return false;
            }

            if (IsCustomOrRegamEntity(ownerDetail) && IsAttachedProgramForEntity(ownerDetail, programKey))
            {
                return true;
            }

            if (!rootAttachedProgramKeysToSuppress.Contains(programKey))
            {
                return false;
            }

            // Only suppress the root attached-program fanout duplicate when this CL reference
            // is explicitly anchored to the selected root entity token.
            var externalEntityKey = NormalizeChartEntityToken(clReference.ExternalTag);
            return !string.IsNullOrWhiteSpace(externalEntityKey)
                && rootEntityKeys.Contains(externalEntityKey)
                && ownerDetail.EntityId != rootDetail.EntityId;
        }

        IEnumerable<(string RelatedEntity, string Label, int Order, bool IsSoft, bool IsPi, bool IsOpcIntegrator)> EnumerateUpstreamReferences(ChartDetail detail)
        {
            static string ExtractReferenceParameter(string raw)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return string.Empty;
                }

                var value = raw.Trim();
                var dotIndex = value.IndexOf('.');
                if (dotIndex <= 0 || dotIndex >= value.Length - 1)
                {
                    return string.Empty;
                }

                return value[(dotIndex + 1)..].Trim().ToUpperInvariant();
            }

            static string BuildCrossLabel(ChartCrossReference cross)
            {
                var property = string.IsNullOrWhiteSpace(cross.PropertyName)
                    ? string.Empty
                    : cross.PropertyName.Trim().ToUpperInvariant();
                var parameter = ExtractReferenceParameter(cross.ReferenceRaw);

                // Upstream cross-reference: related entity writes via CODSTN(#) to selected.<param>
                // Flow is CODSTN(#) -> <param>.
                if (!string.IsNullOrWhiteSpace(property) && !string.IsNullOrWhiteSpace(parameter))
                {
                    return $"{property}->{parameter}";
                }

                return !string.IsNullOrWhiteSpace(property) ? property : parameter;
            }

            foreach (var input in detail.Inputs
                         .OrderBy(x => x.Index)
                         .ThenBy(x => x.SourceProperty, StringComparer.OrdinalIgnoreCase))
            {
                var inputProperty = string.IsNullOrWhiteSpace(input.SourceProperty)
                    ? "PISRC"
                    : input.SourceProperty.Trim().ToUpperInvariant();
                if (inputProperty.Equals("CL_EXT", StringComparison.OrdinalIgnoreCase))
                {
                    yield return (input.SourceEntity, string.Empty, input.Index, false, false, false);
                    continue;
                }

                var label = string.IsNullOrWhiteSpace(input.DestinationParameter)
                    ? $"{inputProperty}({input.Index})"
                    : $"{inputProperty}({input.Index})->{input.DestinationParameter}";
                yield return (input.SourceEntity, label, input.Index, false, false, false);
            }

            var crossOrder = 100_000;
            foreach (var cross in detail.CrossReferences
                         .Where(x => x.IsInboundToSelected)
                         .OrderBy(x => x.RelatedEntity, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(x => x.Index)
                         .ThenBy(x => x.PropertyName, StringComparer.OrdinalIgnoreCase))
            {
                var label = BuildCrossLabel(cross);
                if (!string.IsNullOrWhiteSpace(label))
                {
                    yield return (cross.RelatedEntity, label, crossOrder++, false, false, false);
                }
            }

            var softOrder = 200_000;
            foreach (var soft in detail.SoftReferences
                         .Where(x => x.IsInboundToSelected)
                         .OrderBy(x => x.RelatedEntity, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(x => x.SourceRowNumber))
            {
                var sourceParameter = ExtractReferenceParameter(soft.SourceReference);
                var targetParameter = ExtractReferenceParameter(soft.TargetReference);
                var label = !string.IsNullOrWhiteSpace(sourceParameter) && !string.IsNullOrWhiteSpace(targetParameter)
                    ? $"{sourceParameter}->{targetParameter}"
                    : (!string.IsNullOrWhiteSpace(sourceParameter) ? sourceParameter : targetParameter);
                if (!string.IsNullOrWhiteSpace(label))
                {
                    var isOpcIntegrator = soft.SoftType.StartsWith("OPCI_INTEGRATOR", StringComparison.OrdinalIgnoreCase);
                    yield return (soft.RelatedEntity, label, softOrder++, true, false, isOpcIntegrator);
                }
            }
        }

        IEnumerable<(string RelatedEntity, string Label, int Order, bool IsSoft, bool IsPi, bool IsOpcIntegrator)> EnumerateDownstreamReferences(ChartDetail detail)
        {
            static string ExtractReferenceParameter(string raw)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return string.Empty;
                }

                var value = raw.Trim();
                var dotIndex = value.IndexOf('.');
                if (dotIndex <= 0 || dotIndex >= value.Length - 1)
                {
                    return string.Empty;
                }

                return value[(dotIndex + 1)..].Trim().ToUpperInvariant();
            }

            static string BuildCrossLabel(ChartCrossReference cross)
            {
                var property = string.IsNullOrWhiteSpace(cross.PropertyName)
                    ? string.Empty
                    : cross.PropertyName.Trim().ToUpperInvariant();
                var parameter = ExtractReferenceParameter(cross.ReferenceRaw);

                // Downstream cross-reference: selected.<param> feeds related input property (PISRC/CISRC).
                // Flow is <param> -> PISRC/CISRC.
                if (!string.IsNullOrWhiteSpace(property) && !string.IsNullOrWhiteSpace(parameter))
                {
                    return $"{parameter}->{property}";
                }

                return !string.IsNullOrWhiteSpace(parameter) ? parameter : property;
            }

            foreach (var output in detail.Outputs.OrderBy(x => x.Index))
            {
                var sourceProperty = string.IsNullOrWhiteSpace(output.SourceProperty)
                    ? "CODSTN"
                    : output.SourceProperty.Trim().ToUpperInvariant();
                if (sourceProperty.Equals("CL_SET", StringComparison.OrdinalIgnoreCase))
                {
                    yield return (output.DestinationEntity, string.Empty, output.Index, false, false, false);
                    continue;
                }

                yield return (output.DestinationEntity, $"{sourceProperty}({output.Index})", output.Index, false, false, false);
            }

            var crossOrder = 100_000;
            foreach (var cross in detail.CrossReferences
                         .Where(x => !x.IsInboundToSelected)
                         .OrderBy(x => x.RelatedEntity, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(x => x.Index)
                         .ThenBy(x => x.PropertyName, StringComparer.OrdinalIgnoreCase))
            {
                var label = BuildCrossLabel(cross);
                if (!string.IsNullOrWhiteSpace(label))
                {
                    yield return (cross.RelatedEntity, label, crossOrder++, false, false, false);
                }
            }

            var softOrder = 200_000;
            foreach (var soft in detail.SoftReferences
                         .Where(x => !x.IsInboundToSelected)
                         .OrderBy(x => x.RelatedEntity, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(x => x.SourceRowNumber))
            {
                var sourceParameter = ExtractReferenceParameter(soft.SourceReference);
                var targetParameter = ExtractReferenceParameter(soft.TargetReference);
                var label = !string.IsNullOrWhiteSpace(sourceParameter) && !string.IsNullOrWhiteSpace(targetParameter)
                    ? $"{sourceParameter}->{targetParameter}"
                    : (!string.IsNullOrWhiteSpace(targetParameter) ? targetParameter : sourceParameter);
                if (!string.IsNullOrWhiteSpace(label))
                {
                    var isOpcIntegrator = soft.SoftType.StartsWith("OPCI_INTEGRATOR", StringComparison.OrdinalIgnoreCase);
                    yield return (soft.RelatedEntity, label, softOrder++, true, false, isOpcIntegrator);
                }
            }

            if (showPiMappings)
            {
                var piOrder = 300_000;
                foreach (var pi in detail.PiReferences
                             .OrderBy(x => x.PiTagName, StringComparer.OrdinalIgnoreCase)
                             .ThenBy(x => x.SourceRowNumber))
                {
                    var piTagName = pi.PiTagName.Trim();
                    if (string.IsNullOrWhiteSpace(piTagName))
                    {
                        continue;
                    }

                    var label = string.IsNullOrWhiteSpace(pi.InstrumentTag)
                        ? "PI"
                        : $"PI:{pi.InstrumentTag.Trim()}";
                    yield return (piTagName, label, piOrder++, false, true, false);
                }
            }
        }

        var nodeTitles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var nodeKinds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var nodeColumns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var nodeOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var edgeGroups = new Dictionary<string, Dictionary<string, (int Order, List<string> Labels)>>(StringComparer.OrdinalIgnoreCase);
        var hasAdditionalUpstream = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasAdditionalDownstream = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddNode(string key, string title, string kind, int column, int order)
        {
            var normalizedTitle = string.IsNullOrWhiteSpace(title) ? key : title.Trim();

            if (!nodeTitles.TryGetValue(key, out var existingTitle))
            {
                nodeTitles[key] = normalizedTitle;
                nodeKinds[key] = kind;
                nodeColumns[key] = column;
                nodeOrder[key] = order;
                return;
            }

            if (!string.IsNullOrWhiteSpace(normalizedTitle)
                && (string.IsNullOrWhiteSpace(existingTitle)
                    || existingTitle.StartsWith("$", StringComparison.Ordinal)
                    || normalizedTitle.Length > existingTitle.Length))
            {
                nodeTitles[key] = normalizedTitle;
            }

            if (!nodeKinds.ContainsKey(key))
            {
                nodeKinds[key] = kind;
            }

            if (nodeColumns.TryGetValue(key, out var existingColumn))
            {
                var existingDistance = Math.Abs(existingColumn);
                var candidateDistance = Math.Abs(column);
                if (candidateDistance < existingDistance
                    || (candidateDistance == existingDistance && column < existingColumn))
                {
                    nodeColumns[key] = column;
                }
            }
            else
            {
                nodeColumns[key] = column;
            }

            if (nodeOrder.TryGetValue(key, out var existingOrder))
            {
                nodeOrder[key] = Math.Min(existingOrder, order);
            }
            else
            {
                nodeOrder[key] = order;
            }
        }

        void AddEdge(string sourceKey, string targetKey, string label, int order)
        {
            if (sourceKey.Equals(targetKey, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!edgeGroups.TryGetValue(sourceKey, out var targetMap))
            {
                targetMap = new Dictionary<string, (int Order, List<string> Labels)>(StringComparer.OrdinalIgnoreCase);
                edgeGroups[sourceKey] = targetMap;
            }

            if (!targetMap.TryGetValue(targetKey, out var existing))
            {
                var labels = new List<string>();
                if (!string.IsNullOrWhiteSpace(label))
                {
                    labels.Add(label.Trim());
                }

                targetMap[targetKey] = (order, labels);
                return;
            }

            var updatedOrder = Math.Min(existing.Order, order);
            if (!string.IsNullOrWhiteSpace(label)
                && !existing.Labels.Any(x => x.Equals(label.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                existing.Labels.Add(label.Trim());
            }

            targetMap[targetKey] = (updatedOrder, existing.Labels);
        }

        static bool TryParseEntityNodeKey(string key, out long entityId)
        {
            entityId = 0;
            if (string.IsNullOrWhiteSpace(key) || !key.StartsWith("ENTITY:", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var separatorIndex = key.LastIndexOf(':');
            if (separatorIndex < 0 || separatorIndex >= key.Length - 1)
            {
                return false;
            }

            return long.TryParse(key[(separatorIndex + 1)..], out entityId) && entityId > 0;
        }

        long? TryResolveEntityId(string entityToken)
        {
            var normalized = NormalizeChartEntityToken(entityToken);
            if (string.IsNullOrWhiteSpace(normalized)
                || normalized.StartsWith("$", StringComparison.Ordinal))
            {
                return null;
            }

            return _entityLookup is not null
                   && _entityLookup.TryGetValue(normalized.ToUpperInvariant(), out var found)
                ? found
                : null;
        }

        bool HasRenderableDirectionalReferences(ChartDetail detail, bool upstream)
        {
            var references = upstream
                ? EnumerateUpstreamReferences(detail)
                : EnumerateDownstreamReferences(detail);

            foreach (var reference in references)
            {
                var referenceName = reference.IsPi
                    ? reference.RelatedEntity.Trim()
                    : NormalizeChartEntityToken(reference.RelatedEntity);
                if (!string.IsNullOrWhiteSpace(referenceName)
                    && !referenceName.StartsWith("$", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        var rootKey = $"ENTITY:{rootDetail.EntityId}";
        var rootChartTitle = BuildChartRootTitle(rootDetail);
        AddNode(rootKey, rootChartTitle, "CHART", 0, 0);

        string BuildDirectionalEntityNodeKey(long entityId, bool upstream)
        {
            if (entityId == rootDetail.EntityId)
            {
                return rootKey;
            }

            return $"ENTITY:{(upstream ? "U" : "D")}:{entityId}";
        }

        async Task ExpandDirectionAsync(bool upstream, int maxDepth, IReadOnlySet<string> expandedKeys)
        {
            if (maxDepth <= 0)
            {
                return;
            }

            var depthByEntity = new Dictionary<long, int> { [rootDetail.EntityId] = 0 };
            var queue = new Queue<long>();
            queue.Enqueue(rootDetail.EntityId);

            while (queue.Count > 0)
            {
                var currentEntityId = queue.Dequeue();
                var currentDepth = depthByEntity[currentEntityId];
                var currentColumn = upstream ? -currentDepth : currentDepth;
                var currentKey = BuildDirectionalEntityNodeKey(currentEntityId, upstream);
                var currentDetail = await GetDetailAsync(currentEntityId);

                if (currentDetail is null)
                {
                    continue;
                }

                AddNode(currentKey, currentDetail.Name, "CHART", currentColumn, currentDepth * 1_000_000);

                if (currentDepth >= maxDepth && !expandedKeys.Contains(currentKey))
                {
                    if (upstream && HasRenderableDirectionalReferences(currentDetail, upstream: true))
                    {
                        hasAdditionalUpstream.Add(currentKey);
                    }

                    if (!upstream && HasRenderableDirectionalReferences(currentDetail, upstream: false))
                    {
                        hasAdditionalDownstream.Add(currentKey);
                    }

                    continue;
                }

                var nextDepth = currentDepth + 1;
                var nextColumn = upstream ? -nextDepth : nextDepth;
                var referenceOrderBase = nextDepth * 1_000_000;
                var localOffset = 0;
                var references = upstream
                    ? EnumerateUpstreamReferences(currentDetail)
                    : EnumerateDownstreamReferences(currentDetail);

                foreach (var reference in references)
                {
                    var referenceName = reference.IsPi
                        ? reference.RelatedEntity.Trim()
                        : NormalizeChartEntityToken(reference.RelatedEntity);
                    if (string.IsNullOrWhiteSpace(referenceName)
                        || referenceName.StartsWith("$", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var referenceOrder = referenceOrderBase + reference.Order + localOffset++;
                    var relatedEntityId = reference.IsPi ? null : TryResolveEntityId(referenceName);
                    string relatedKey;

                    if (relatedEntityId is long resolvedEntityId)
                    {
                        var relatedDetail = await GetDetailAsync(resolvedEntityId);
                        relatedKey = BuildDirectionalEntityNodeKey(resolvedEntityId, upstream);
                        AddNode(
                            relatedKey,
                            relatedDetail?.Name ?? referenceName,
                            "CHART",
                            nextColumn,
                            referenceOrder);

                        if (!depthByEntity.TryGetValue(resolvedEntityId, out var knownDepth) || nextDepth < knownDepth)
                        {
                            depthByEntity[resolvedEntityId] = nextDepth;
                            queue.Enqueue(resolvedEntityId);
                        }
                    }
                    else
                    {
                        relatedKey = reference.IsPi
                            ? BuildPiExternalNodeKey(referenceName, upstream)
                            : BuildChartExternalNodeKey(referenceName, upstream);
                        var referenceKind = reference.IsPi
                            ? "PI_REF"
                            : reference.IsSoft
                                ? (reference.IsOpcIntegrator ? "SOFT_REF_OPCI" : "SOFT_REF")
                                : "CHART";
                        var referenceTitle = reference.IsPi
                            ? $"PI\n{referenceName}"
                            : referenceName;
                        AddNode(relatedKey, referenceTitle, referenceKind, nextColumn, referenceOrder);
                    }

                    if (upstream)
                    {
                        AddEdge(relatedKey, currentKey, reference.Label, referenceOrder);
                    }
                    else
                    {
                        AddEdge(currentKey, relatedKey, reference.Label, referenceOrder);
                    }
                }
            }
        }

        await ExpandDirectionAsync(upstream: true, _chartUpstreamDepth, _chartExpandedUpstreamKeys);
        await ExpandDirectionAsync(upstream: false, _chartDownstreamDepth, _chartExpandedDownstreamKeys);

        // Treat CL links on non-root entities as secondary branches.
        // They are hidden by default and exposed only when that node/direction is expanded.
        foreach (var nodeKey in nodeColumns.Keys)
        {
            if (nodeKey.Equals(rootKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!nodeKey.StartsWith("ENTITY:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var entitySeparator = nodeKey.LastIndexOf(':');
            if (entitySeparator < 0
                || entitySeparator >= nodeKey.Length - 1
                || !long.TryParse(nodeKey[(entitySeparator + 1)..], out var entityId)
                || entityId <= 0)
            {
                continue;
            }

            var entityDetail = await GetDetailAsync(entityId);
            if (entityDetail is null)
            {
                continue;
            }

            var visibleClReferences = entityDetail.ClReferences
                .Where(x => !ShouldSuppressChartClReference(entityDetail, x))
                .ToList();

            if (visibleClReferences.Any(x => x.IsWriteToTag))
            {
                hasAdditionalUpstream.Add(nodeKey);
            }

            if (visibleClReferences.Any(x => !x.IsWriteToTag))
            {
                hasAdditionalDownstream.Add(nodeKey);
            }

            if (entityDetail.LogicReferences.Any(x => x.IsInboundToSelected))
            {
                hasAdditionalUpstream.Add(nodeKey);
            }

            if (entityDetail.LogicReferences.Any(x => !x.IsInboundToSelected))
            {
                hasAdditionalDownstream.Add(nodeKey);
            }
        }

        _chartExpandableUpstreamKeys.Clear();
        _chartExpandableDownstreamKeys.Clear();
        _chartExpandableUpstreamKeys.UnionWith(hasAdditionalUpstream);
        _chartExpandableDownstreamKeys.UnionWith(hasAdditionalDownstream);

        if (_chartUpstreamDepth > 0 || _chartDownstreamDepth > 0)
        {
            static string BuildLogicReferenceLabel(ChartLogicReference logicReference)
            {
                static string ExtractReferenceParameter(string raw)
                {
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        return string.Empty;
                    }

                    var value = raw.Trim();
                    var dotIndex = value.IndexOf('.');
                    if (dotIndex <= 0 || dotIndex >= value.Length - 1)
                    {
                        return string.Empty;
                    }

                    return value[(dotIndex + 1)..].Trim().ToUpperInvariant();
                }

                var property = string.IsNullOrWhiteSpace(logicReference.PropertyName)
                    ? string.Empty
                    : logicReference.PropertyName.Trim().ToUpperInvariant();
                var parameter = ExtractReferenceParameter(logicReference.ReferenceRaw);

                if (logicReference.IsInboundToSelected)
                {
                    if (!string.IsNullOrWhiteSpace(property) && !string.IsNullOrWhiteSpace(parameter))
                    {
                        return $"{property}->{parameter}";
                    }

                    return !string.IsNullOrWhiteSpace(property) ? property : parameter;
                }

                if (!string.IsNullOrWhiteSpace(property) && !string.IsNullOrWhiteSpace(parameter))
                {
                    return $"{parameter}->{property}";
                }

                return !string.IsNullOrWhiteSpace(parameter) ? parameter : property;
            }

        var logicOrderBase = 850_000_000;
        var logicOffset = 0;
        var logicDetailCache = new Dictionary<long, LogicBlockDetail?>();
        async Task<LogicBlockDetail?> GetLogicDetailAsync(long logicEntityId)
        {
            if (logicDetailCache.TryGetValue(logicEntityId, out var cached))
            {
                return cached;
            }

            var loaded = await _database.GetLogicBlockDetailAsync(logicEntityId);
            logicDetailCache[logicEntityId] = loaded;
            return loaded;
        }

            var logicEntityNodes = nodeColumns
                .Where(x => TryParseEntityNodeKey(x.Key, out _))
                .OrderBy(x => Math.Abs(x.Value))
                .ThenBy(x => x.Value)
                .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var processedLogicNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entityNode in logicEntityNodes)
            {
                if (!TryParseEntityNodeKey(entityNode.Key, out var entityNodeId))
                {
                    continue;
                }

                var entityDetail = await GetDetailAsync(entityNodeId);
                if (entityDetail is null)
                {
                    continue;
                }

                var isRootEntityNode = entityNode.Key.Equals(rootKey, StringComparison.OrdinalIgnoreCase);
                var showSecondaryUpstream = _chartExpandedUpstreamKeys.Contains(entityNode.Key);
                var showSecondaryDownstream = _chartExpandedDownstreamKeys.Contains(entityNode.Key);

                var downstreamDistance = Math.Max(0, entityNode.Value);
                var upstreamDistance = Math.Max(0, -entityNode.Value);

                foreach (var logicReference in entityDetail.LogicReferences
                             .OrderBy(x => x.LogicEntity, StringComparer.OrdinalIgnoreCase)
                             .ThenBy(x => x.IsInboundToSelected ? 0 : 1)
                             .ThenBy(x => x.Index)
                             .ThenBy(x => x.PropertyName, StringComparer.OrdinalIgnoreCase))
                {
                    var isInbound = logicReference.IsInboundToSelected;
                    if (isInbound && _chartUpstreamDepth <= 0)
                    {
                        continue;
                    }

                    if (!isInbound && _chartDownstreamDepth <= 0)
                    {
                        continue;
                    }

                    if (isInbound && !isRootEntityNode && !showSecondaryUpstream)
                    {
                        continue;
                    }

                    if (!isInbound && !isRootEntityNode && !showSecondaryDownstream)
                    {
                        continue;
                    }

                    if (isInbound && upstreamDistance >= _chartUpstreamDepth && !showSecondaryUpstream)
                    {
                        continue;
                    }

                    if (!isInbound && downstreamDistance >= _chartDownstreamDepth && !showSecondaryDownstream)
                    {
                        continue;
                    }

                    var logicNodeKey = $"LB:{logicReference.LogicEntityId}";
                    var logicTitle = $"{logicReference.LogicEntity}\nLOGIC BLOCK";
                    var logicColumn = entityNode.Value + (isInbound ? -1 : 1);
                    AddNode(logicNodeKey, logicTitle, "LOGIC_REF", logicColumn, logicOrderBase + logicOffset++);

                    var label = BuildLogicReferenceLabel(logicReference);
                    if (isInbound)
                    {
                        AddEdge(logicNodeKey, entityNode.Key, label, logicOrderBase + logicOffset++);
                    }
                    else
                    {
                        AddEdge(entityNode.Key, logicNodeKey, label, logicOrderBase + logicOffset++);
                    }

                    if (!processedLogicNodes.Add(logicNodeKey))
                    {
                        continue;
                    }

                    try
                    {
                        var logicDetail = await GetLogicDetailAsync(logicReference.LogicEntityId);
                        if (logicDetail is null)
                        {
                            continue;
                        }

                        foreach (var input in logicDetail.Inputs)
                        {
                            var inputEntityToken = NormalizeChartEntityToken(input.Source);
                            if (string.IsNullOrWhiteSpace(inputEntityToken)
                                || inputEntityToken.StartsWith("$", StringComparison.Ordinal))
                            {
                                continue;
                            }

                            var inputEntityId = TryResolveEntityId(inputEntityToken);
                            if (inputEntityId is null)
                            {
                                continue;
                            }

                            var matchingEntityNodeKeys = nodeColumns.Keys
                                .Where(key => TryParseEntityNodeKey(key, out var parsedId) && parsedId == inputEntityId.Value)
                                .ToList();
                            if (matchingEntityNodeKeys.Count == 0)
                            {
                                continue;
                            }

                            var sourceEntityNodeKey = isInbound
                                ? matchingEntityNodeKeys
                                    .OrderByDescending(key => nodeColumns.GetValueOrDefault(key) <= logicColumn)
                                    .ThenByDescending(key => nodeColumns.GetValueOrDefault(key))
                                    .ThenBy(key => key, StringComparer.OrdinalIgnoreCase)
                                    .FirstOrDefault()
                                : matchingEntityNodeKeys
                                    .OrderByDescending(key => nodeColumns.GetValueOrDefault(key) >= logicColumn)
                                    .ThenBy(key => nodeColumns.GetValueOrDefault(key))
                                    .ThenBy(key => key, StringComparer.OrdinalIgnoreCase)
                                    .FirstOrDefault();
                            if (string.IsNullOrWhiteSpace(sourceEntityNodeKey))
                            {
                                continue;
                            }

                            AddEdge(sourceEntityNodeKey, logicNodeKey, "LISRC", logicOrderBase + logicOffset++);
                        }

                        foreach (var output in logicDetail.Outputs)
                        {
                            var outputEntityToken = NormalizeChartEntityToken(output.Destination);
                            if (string.IsNullOrWhiteSpace(outputEntityToken)
                                || outputEntityToken.StartsWith("$", StringComparison.Ordinal))
                            {
                                continue;
                            }

                            var outputEntityId = TryResolveEntityId(outputEntityToken);
                            if (outputEntityId is null)
                            {
                                continue;
                            }

                            var matchingEntityNodeKeys = nodeColumns.Keys
                                .Where(key => TryParseEntityNodeKey(key, out var parsedId) && parsedId == outputEntityId.Value)
                                .ToList();
                            if (matchingEntityNodeKeys.Count == 0)
                            {
                                continue;
                            }

                            var targetEntityNodeKey = isInbound
                                ? matchingEntityNodeKeys
                                    .OrderByDescending(key => nodeColumns.GetValueOrDefault(key) >= logicColumn)
                                    .ThenBy(key => nodeColumns.GetValueOrDefault(key))
                                    .ThenBy(key => key, StringComparer.OrdinalIgnoreCase)
                                    .FirstOrDefault()
                                : matchingEntityNodeKeys
                                    .OrderByDescending(key => nodeColumns.GetValueOrDefault(key) <= logicColumn)
                                    .ThenByDescending(key => nodeColumns.GetValueOrDefault(key))
                                    .ThenBy(key => key, StringComparer.OrdinalIgnoreCase)
                                    .FirstOrDefault();
                            if (string.IsNullOrWhiteSpace(targetEntityNodeKey))
                            {
                                continue;
                            }

                            AddEdge(logicNodeKey, targetEntityNodeKey, "LODSTN", logicOrderBase + logicOffset++);
                        }
                    }
                    catch
                    {
                        // Keep chart rendering resilient even if one logic block has malformed data.
                    }
                }
            }

            var clOrderBase = 900_000_000;
            var clOffset = 0;
            var entityNodes = nodeColumns
                .Where(x => TryParseEntityNodeKey(x.Key, out _))
                .OrderBy(x => Math.Abs(x.Value))
                .ThenBy(x => x.Value)
                .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var entityNode in entityNodes)
            {
                if (!TryParseEntityNodeKey(entityNode.Key, out var entityNodeId))
                {
                    continue;
                }

                var entityDetail = await GetDetailAsync(entityNodeId);
                if (entityDetail is null)
                {
                    continue;
                }

                var isRootEntityNode = entityNode.Key.Equals(rootKey, StringComparison.OrdinalIgnoreCase);
                var showSecondaryUpstream = _chartExpandedUpstreamKeys.Contains(entityNode.Key);
                var showSecondaryDownstream = _chartExpandedDownstreamKeys.Contains(entityNode.Key);

                var downstreamDistance = Math.Max(0, entityNode.Value);
                var upstreamDistance = Math.Max(0, -entityNode.Value);

                foreach (var clReference in entityDetail.ClReferences
                             .OrderBy(x => x.ProgramType, StringComparer.OrdinalIgnoreCase)
                             .ThenBy(x => x.ProgramName, StringComparer.OrdinalIgnoreCase)
                             .ThenBy(x => x.IsWriteToTag ? 1 : 0)
                             .ThenBy(x => x.LineNumber))
                {
                    if (ShouldSuppressChartClReference(entityDetail, clReference))
                    {
                        continue;
                    }

                    var isWrite = clReference.IsWriteToTag;
                    if (isWrite && !isRootEntityNode && !showSecondaryUpstream)
                    {
                        continue;
                    }

                    if (!isWrite && !isRootEntityNode && !showSecondaryDownstream)
                    {
                        continue;
                    }

                    if (isWrite && _chartUpstreamDepth <= 0)
                    {
                        continue;
                    }

                    if (!isWrite && _chartDownstreamDepth <= 0)
                    {
                        continue;
                    }

                    // Avoid showing CL references one hop beyond the current depth frontier,
                    // unless this node was explicitly expanded with its side arrow.
                    if (isWrite && upstreamDistance >= _chartUpstreamDepth && !showSecondaryUpstream)
                    {
                        continue;
                    }

                    if (!isWrite && downstreamDistance >= _chartDownstreamDepth && !showSecondaryDownstream)
                    {
                        continue;
                    }

                    var clNodeKey = isWrite
                        ? $"CLW:{clReference.ClProgramId}"
                        : $"CLR:{clReference.ClProgramId}";
                    var clTitle = $"{clReference.ProgramName}\n{clReference.ProgramType}";
                    var column = isWrite ? entityNode.Value - 1 : entityNode.Value + 1;
                    AddNode(clNodeKey, clTitle, "CHART", column, clOrderBase + clOffset++);

                    var label = isWrite
                        ? $"SET {clReference.ExternalTag}"
                        : string.Empty;

                    if (isWrite)
                    {
                        AddEdge(clNodeKey, entityNode.Key, label, clOrderBase + clOffset++);
                    }
                    else
                    {
                        AddEdge(entityNode.Key, clNodeKey, label, clOrderBase + clOffset++);
                    }
                }
            }

            var plcOrderBase = 950_000_000;
            var plcOffset = 0;
            var plcReferences = rootDetail.PlcReferences.ToList();
            if (rootDetail.EntityType.Equals("ARRAY", StringComparison.OrdinalIgnoreCase))
            {
                var hasArrayElementMappings = plcReferences.Any(reference => TryExtractSerialArrayElement(reference.SourceLine, out _, out _));
                if (hasArrayElementMappings)
                {
                    plcReferences = plcReferences
                        .Where(reference => TryExtractSerialArrayElement(reference.SourceLine, out _, out _))
                        .ToList();
                }
            }

            var orderedPlcReferences = plcReferences
                .Select(reference =>
                {
                    var hasArrayElement = TryExtractSerialArrayElement(reference.SourceLine, out var arrayLane, out var arrayElementIndex);
                    var hasRegisterParts = TryParsePlcRegisterSortParts(
                        reference.RegisterAddress,
                        out var filePrefix,
                        out var fileNumber,
                        out var word,
                        out var bit);
                    return new
                    {
                        Reference = reference,
                        HasArrayElement = hasArrayElement,
                        ArrayLane = arrayLane,
                        ArrayElementIndex = hasArrayElement ? arrayElementIndex : int.MaxValue,
                        FilePrefix = hasRegisterParts ? filePrefix : string.Empty,
                        FileNumber = hasRegisterParts ? fileNumber : int.MaxValue,
                        Word = hasRegisterParts ? word : int.MaxValue,
                        Bit = hasRegisterParts ? bit : int.MaxValue
                    };
                })
                .OrderBy(x => x.HasArrayElement ? 0 : 1)
                .ThenBy(x => GetSerialArrayLaneSortOrder(x.ArrayLane))
                .ThenBy(x => x.ArrayElementIndex)
                .ThenBy(x => x.FilePrefix, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.FileNumber)
                .ThenBy(x => x.Word)
                .ThenBy(x => x.Bit)
                .ThenBy(x => x.Reference.PlcName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Reference.RegisterAddress, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Reference)
                .ToList();

            foreach (var plcReference in orderedPlcReferences)
            {
                var isEplcgInput = plcReference.SourceLine.Contains("PCADDRI", StringComparison.OrdinalIgnoreCase);
                var isEplcgOutput = plcReference.SourceLine.Contains("PCADDRO", StringComparison.OrdinalIgnoreCase);
                var plcColumn = isEplcgOutput ? 1 : -1;

                var plcNodeKey =
                    $"PLC:{(string.IsNullOrWhiteSpace(plcReference.InterfaceType) ? "AB" : plcReference.InterfaceType.ToUpperInvariant())}:{plcReference.PlcSystemId}:{plcReference.RegisterAddress.ToUpperInvariant()}";
                var plcTitle = BuildPlcReferenceTitle(plcReference);
                var plcKind = plcColumn > 0 ? "PLC_REF_RIGHT" : "PLC_REF_LEFT";
                AddNode(plcNodeKey, plcTitle, plcKind, plcColumn, plcOrderBase + plcOffset++);

                if (isEplcgOutput)
                {
                    AddEdge(rootKey, plcNodeKey, string.Empty, plcOrderBase + plcOffset++);
                }
                else if (isEplcgInput)
                {
                    AddEdge(plcNodeKey, rootKey, string.Empty, plcOrderBase + plcOffset++);
                }
                else
                {
                    if (plcColumn > 0)
                    {
                        AddEdge(rootKey, plcNodeKey, string.Empty, plcOrderBase + plcOffset++);
                    }
                    else
                    {
                        AddEdge(plcNodeKey, rootKey, string.Empty, plcOrderBase + plcOffset++);
                    }
                }
            }
        }

        if (nodeTitles.Count == 0)
        {
            ClearChartDiagram();
            return;
        }

        var columnNodes = nodeColumns
            .GroupBy(x => x.Value)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(x => x.Key)
                    .OrderBy(x => nodeOrder.GetValueOrDefault(x, int.MaxValue))
                    .ThenBy(x => nodeTitles.GetValueOrDefault(x, x), StringComparer.OrdinalIgnoreCase)
                    .ToList());

        var outgoingByNode = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var incomingByNode = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        static void AddNeighbor(Dictionary<string, List<string>> map, string key, string value)
        {
            if (!map.TryGetValue(key, out var list))
            {
                list = [];
                map[key] = list;
            }

            if (!list.Any(x => x.Equals(value, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(value);
            }
        }

        foreach (var (sourceKey, targetMap) in edgeGroups)
        {
            foreach (var targetKey in targetMap.Keys)
            {
                AddNeighbor(outgoingByNode, sourceKey, targetKey);
                AddNeighbor(incomingByNode, targetKey, sourceKey);
            }
        }

        var rowIndexByNode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (column, keys) in columnNodes)
        {
            for (var i = 0; i < keys.Count; i++)
            {
                rowIndexByNode[keys[i]] = i;
            }
        }

        var remainingColumns = columnNodes.Keys.Where(x => x != 0).ToHashSet();
        var columnSweepOrder = new List<int>();
        for (var distance = 1; remainingColumns.Count > 0; distance++)
        {
            var leftColumn = -distance;
            if (remainingColumns.Remove(leftColumn))
            {
                columnSweepOrder.Add(leftColumn);
            }

            var rightColumn = distance;
            if (remainingColumns.Remove(rightColumn))
            {
                columnSweepOrder.Add(rightColumn);
            }
        }

        double GetAlignmentScore(string nodeKey, int column)
        {
            if (!rowIndexByNode.TryGetValue(nodeKey, out var fallbackRow))
            {
                fallbackRow = 0;
            }

            var neighborCandidates = new List<string>();
            if (column < 0)
            {
                if (outgoingByNode.TryGetValue(nodeKey, out var outgoing))
                {
                    neighborCandidates.AddRange(outgoing.Where(x => nodeColumns.GetValueOrDefault(x) > column));
                }

                if (incomingByNode.TryGetValue(nodeKey, out var incoming))
                {
                    neighborCandidates.AddRange(incoming.Where(x => nodeColumns.GetValueOrDefault(x) > column));
                }

                if (neighborCandidates.Count == 0)
                {
                    return fallbackRow;
                }

                var nearestColumn = neighborCandidates
                    .Select(x => nodeColumns.GetValueOrDefault(x))
                    .Where(x => x > column)
                    .DefaultIfEmpty(column + 1)
                    .Min();
                var nearestRows = neighborCandidates
                    .Where(x => nodeColumns.GetValueOrDefault(x) == nearestColumn)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(x => (double)rowIndexByNode.GetValueOrDefault(x, fallbackRow))
                    .ToList();

                return nearestRows.Count == 0 ? fallbackRow : nearestRows.Average();
            }

            if (column > 0)
            {
                if (incomingByNode.TryGetValue(nodeKey, out var incoming))
                {
                    neighborCandidates.AddRange(incoming.Where(x => nodeColumns.GetValueOrDefault(x) < column));
                }

                if (outgoingByNode.TryGetValue(nodeKey, out var outgoing))
                {
                    neighborCandidates.AddRange(outgoing.Where(x => nodeColumns.GetValueOrDefault(x) < column));
                }

                if (neighborCandidates.Count == 0)
                {
                    return fallbackRow;
                }

                var nearestColumn = neighborCandidates
                    .Select(x => nodeColumns.GetValueOrDefault(x))
                    .Where(x => x < column)
                    .DefaultIfEmpty(column - 1)
                    .Max();
                var nearestRows = neighborCandidates
                    .Where(x => nodeColumns.GetValueOrDefault(x) == nearestColumn)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(x => (double)rowIndexByNode.GetValueOrDefault(x, fallbackRow))
                    .ToList();

                return nearestRows.Count == 0 ? fallbackRow : nearestRows.Average();
            }

            return fallbackRow;
        }

        for (var pass = 0; pass < 8; pass++)
        {
            foreach (var column in columnSweepOrder)
            {
                if (!columnNodes.TryGetValue(column, out var keys) || keys.Count < 2)
                {
                    continue;
                }

                var allPlcNodes = keys.All(key =>
                    nodeKinds.TryGetValue(key, out var kind)
                    && IsPlcReferenceKind(kind));
                if (allPlcNodes)
                {
                    continue;
                }

                keys.Sort((left, right) =>
                {
                    var leftScore = GetAlignmentScore(left, column);
                    var rightScore = GetAlignmentScore(right, column);
                    var scoreCompare = leftScore.CompareTo(rightScore);
                    if (scoreCompare != 0)
                    {
                        return scoreCompare;
                    }

                    var orderCompare = nodeOrder.GetValueOrDefault(left, int.MaxValue)
                        .CompareTo(nodeOrder.GetValueOrDefault(right, int.MaxValue));
                    if (orderCompare != 0)
                    {
                        return orderCompare;
                    }

                    return string.Compare(
                        nodeTitles.GetValueOrDefault(left, left),
                        nodeTitles.GetValueOrDefault(right, right),
                        StringComparison.OrdinalIgnoreCase);
                });

                for (var i = 0; i < keys.Count; i++)
                {
                    rowIndexByNode[keys[i]] = i;
                }
            }
        }

        const double topMargin = 40;
        const double rowSpacing = 150;
        const double columnSpacing = 420;
        const double centerX = 700;
        var showUpstreamIndicators = new HashSet<string>(hasAdditionalUpstream, StringComparer.OrdinalIgnoreCase);
        var showDownstreamIndicators = new HashSet<string>(hasAdditionalDownstream, StringComparer.OrdinalIgnoreCase);
        showUpstreamIndicators.UnionWith(_chartExpandedUpstreamKeys);
        showDownstreamIndicators.UnionWith(_chartExpandedDownstreamKeys);

        var maxRows = Math.Max(1, columnNodes.Values.Max(x => x.Count));
        var centerY = topMargin + ((maxRows - 1) * rowSpacing / 2d);

        static double ComputeRowY(double centerY, double rowSpacing, int rowIndex, int rowCount)
        {
            if (rowCount <= 1)
            {
                return centerY;
            }

            return centerY + ((rowIndex - ((rowCount - 1) / 2d)) * rowSpacing);
        }

        var graph = new LogicGraph();
        var vertices = new Dictionary<string, LogicVertex>(StringComparer.OrdinalIgnoreCase);
        var positions = new Dictionary<LogicVertex, Point>();

        LogicVertex GetOrCreateVertex(string key)
        {
            if (vertices.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var vertex = new LogicVertex
            {
                Key = key,
                Title = nodeTitles.GetValueOrDefault(key, key),
                Kind = nodeKinds.GetValueOrDefault(key, "CHART")
            };
            vertex.ShowUpstreamIndicator = showUpstreamIndicators.Contains(key);
            vertex.ShowDownstreamIndicator = showDownstreamIndicators.Contains(key);
            vertex.IsUpstreamExpanded = _chartExpandedUpstreamKeys.Contains(key);
            vertex.IsDownstreamExpanded = _chartExpandedDownstreamKeys.Contains(key);

            vertices[key] = vertex;
            graph.AddVertex(vertex);
            return vertex;
        }

        foreach (var (column, keys) in columnNodes.OrderBy(x => x.Key))
        {
            for (var rowIndex = 0; rowIndex < keys.Count; rowIndex++)
            {
                var key = keys[rowIndex];
                var vertex = GetOrCreateVertex(key);
                var x = centerX + (column * columnSpacing);
                var y = ComputeRowY(centerY, rowSpacing, rowIndex, keys.Count);
                positions[vertex] = new Point(x, y);
            }
        }

        foreach (var (sourceKey, targetMap) in edgeGroups)
        {
            if (!vertices.TryGetValue(sourceKey, out var sourceVertex))
            {
                continue;
            }

            foreach (var target in targetMap
                         .OrderBy(x => x.Value.Order)
                         .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (!vertices.TryGetValue(target.Key, out var targetVertex))
                {
                    continue;
                }

                var (startLabel, endLabel) = BuildChartEdgeEndpointLabels(target.Value.Labels);
                var isPlcEdge = IsPlcReferenceKind(sourceVertex.Kind)
                    || IsPlcReferenceKind(targetVertex.Kind);
                var isLogicReferenceEdge = sourceVertex.Kind.Equals("LOGIC_REF", StringComparison.OrdinalIgnoreCase)
                    || targetVertex.Kind.Equals("LOGIC_REF", StringComparison.OrdinalIgnoreCase);
                if (isPlcEdge)
                {
                    startLabel = string.Empty;
                    endLabel = string.Empty;
                }
                else if (isLogicReferenceEdge)
                {
                    // Logic reference nodes summarize LODSTN/LOSRC in the node title.
                    // Suppress endpoint overlays so they don't obscure the logic block name.
                    startLabel = string.Empty;
                    endLabel = string.Empty;
                }

                var edge = new LogicEdge(sourceVertex, targetVertex)
                {
                    Text = BuildChartEdgeLabel(target.Value.Labels),
                    StartLabel = startLabel,
                    EndLabel = endLabel
                };

                var hasReciprocal = edgeGroups.TryGetValue(target.Key, out var reverseTargets)
                    && reverseTargets.ContainsKey(sourceKey);
                if (hasReciprocal)
                {
                    var sourceComesFirst = string.Compare(sourceKey, target.Key, StringComparison.OrdinalIgnoreCase) <= 0;
                    var slot = sourceComesFirst ? 0 : 1;
                    edge.SourceSlot = slot;
                    edge.SourceSlotCount = 2;
                    edge.TargetSlot = slot;
                    edge.TargetSlotCount = 2;
                }

                graph.AddEdge(edge);
            }
        }

        static string NormalizeNodeLabel(string label)
        {
            var text = (label ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            return text;
        }

        static (string SourceSide, string TargetSide) ParseLabelForNodeText(string raw)
        {
            var label = (raw ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(label))
            {
                return (string.Empty, string.Empty);
            }

            var arrowIndex = label.IndexOf("->", StringComparison.Ordinal);
            if (arrowIndex > 0 && arrowIndex < label.Length - 2)
            {
                var left = label[..arrowIndex].Trim();
                var right = label[(arrowIndex + 2)..].Trim();
                return (left, right);
            }

            if (label.StartsWith("EXTERNAL ", StringComparison.OrdinalIgnoreCase))
            {
                return (string.Empty, label["EXTERNAL ".Length..].Trim());
            }

            if (label.StartsWith("SET ", StringComparison.OrdinalIgnoreCase))
            {
                return (string.Empty, label["SET ".Length..].Trim());
            }

            return (label, string.Empty);
        }

        var incomingLabelByNode = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var outgoingLabelByNode = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (sourceKey, targetMap) in edgeGroups)
        {
            foreach (var (targetKey, grouped) in targetMap)
            {
                var anyAdded = false;
                foreach (var rawLabel in grouped.Labels)
                {
                    var (sourceSide, targetSide) = ParseLabelForNodeText(rawLabel);
                    var normalizedStart = NormalizeNodeLabel(sourceSide);
                    var normalizedEnd = NormalizeNodeLabel(targetSide);

                    if (!string.IsNullOrWhiteSpace(normalizedStart))
                    {
                        if (!outgoingLabelByNode.TryGetValue(sourceKey, out var outBucket))
                        {
                            outBucket = [];
                            outgoingLabelByNode[sourceKey] = outBucket;
                        }

                        if (!outBucket.Any(x => x.Equals(normalizedStart, StringComparison.OrdinalIgnoreCase)))
                        {
                            outBucket.Add(normalizedStart);
                            anyAdded = true;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(normalizedEnd))
                    {
                        if (!incomingLabelByNode.TryGetValue(targetKey, out var inBucket))
                        {
                            inBucket = [];
                            incomingLabelByNode[targetKey] = inBucket;
                        }

                        if (!inBucket.Any(x => x.Equals(normalizedEnd, StringComparison.OrdinalIgnoreCase)))
                        {
                            inBucket.Add(normalizedEnd);
                            anyAdded = true;
                        }
                    }
                }

                if (anyAdded)
                {
                    continue;
                }

                // Fallback: always retain at least one raw label so chart blocks never lose context.
                var fallback = grouped.Labels
                    .Select(NormalizeNodeLabel)
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
                if (string.IsNullOrWhiteSpace(fallback))
                {
                    continue;
                }

                if (!outgoingLabelByNode.TryGetValue(sourceKey, out var fallbackOut))
                {
                    fallbackOut = [];
                    outgoingLabelByNode[sourceKey] = fallbackOut;
                }

                if (!fallbackOut.Any(x => x.Equals(fallback, StringComparison.OrdinalIgnoreCase)))
                {
                    fallbackOut.Add(fallback);
                }
            }
        }

        static string SummarizeLabelList(IReadOnlyList<string> labels)
        {
            if (labels.Count == 0)
            {
                return string.Empty;
            }

            var visible = labels.Take(2).ToList();
            var summary = string.Join(" / ", visible);
            if (labels.Count > visible.Count)
            {
                summary += $" (+{labels.Count - visible.Count})";
            }

            return summary;
        }

        static string SummarizeLogicReferenceLabels(IReadOnlyList<string> labels)
        {
            if (labels.Count == 0)
            {
                return string.Empty;
            }

            var lodstn = labels
                .Where(x => x.StartsWith("LODSTN(", StringComparison.OrdinalIgnoreCase))
                .Take(3)
                .ToList();
            if (lodstn.Count > 0)
            {
                var summary = string.Join(" / ", lodstn);
                var extra = labels.Count(x => x.StartsWith("LODSTN(", StringComparison.OrdinalIgnoreCase)) - lodstn.Count;
                return extra > 0 ? $"{summary} (+{extra})" : summary;
            }

            var losrc = labels
                .Where(x => x.StartsWith("LOSRC(", StringComparison.OrdinalIgnoreCase))
                .Take(3)
                .ToList();
            if (losrc.Count > 0)
            {
                var summary = string.Join(" / ", losrc);
                var extra = labels.Count(x => x.StartsWith("LOSRC(", StringComparison.OrdinalIgnoreCase)) - losrc.Count;
                return extra > 0 ? $"{summary} (+{extra})" : summary;
            }

            return SummarizeLabelList(labels);
        }

        foreach (var (key, baseTitle) in nodeTitles.ToList())
        {
            if (!nodeKinds.TryGetValue(key, out var kind)
                || string.IsNullOrWhiteSpace(baseTitle))
            {
                continue;
            }

            incomingLabelByNode.TryGetValue(key, out var inLabels);
            outgoingLabelByNode.TryGetValue(key, out var outLabels);
            inLabels ??= [];
            outLabels ??= [];

            var inSummary = SummarizeLabelList(inLabels);
            var outSummary = SummarizeLabelList(outLabels);

            if (kind.Equals("LOGIC_REF", StringComparison.OrdinalIgnoreCase))
            {
                var logicSummary = SummarizeLogicReferenceLabels(outLabels);
                nodeTitles[key] = string.IsNullOrWhiteSpace(logicSummary)
                    ? baseTitle
                    : $"{logicSummary}\n{baseTitle}";
                continue;
            }

            if (!kind.Equals("CHART", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(inSummary) && string.IsNullOrWhiteSpace(outSummary))
            {
                continue;
            }

            // Keep chart blocks focused on entity names.
            nodeTitles[key] = baseTitle;
        }

        // Vertices may already be created from earlier titles; refresh to ensure IN/OUT text appears.
        foreach (var (key, vertex) in vertices)
        {
            if (nodeTitles.TryGetValue(key, out var updatedTitle)
                && !string.IsNullOrWhiteSpace(updatedTitle))
            {
                vertex.Title = updatedTitle;
            }
        }

        ApplyChartDiagram(graph, positions, runMsaglLayout: true);
    }

    private void RenderLogicDiagram(LogicBlockDetail detail, IReadOnlyDictionary<string, LogicLayoutNodePosition>? savedLayout = null)
    {
        const double topMargin = 30;
        const double gateRowSpacing = 180;
        const double gateWidth = 180;
        const double gateHeight = 96;
        const double gateColumnBaseX = 300;
        const double gateColumnSpacing = 400;
        const double inputToGateOffsetX = 150;
        const double outputFromGateOffsetX = 430;
        const double inputPinSpacing = 40;

        var graph = new LogicGraph();
        var vertices = new Dictionary<string, LogicVertex>(StringComparer.OrdinalIgnoreCase);
        var positions = new Dictionary<LogicVertex, Point>();

        LogicVertex GetOrCreateVertex(string key, string title, string kind, string algorithm = "")
        {
            if (vertices.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var vertex = new LogicVertex { Key = key, Title = title, Kind = kind, Algorithm = algorithm };
            vertices[key] = vertex;
            graph.AddVertex(vertex);
            return vertex;
        }

        var gates = detail.Gates.OrderBy(x => x.Index).ToList();
        var gatesByIndex = gates.ToDictionary(x => x.Index);
        var outputs = detail.Outputs
            .Where(x => !string.IsNullOrWhiteSpace(x.Destination) && !x.Destination.Trim().Equals("--.--", StringComparison.Ordinal))
            .OrderBy(x => x.Index)
            .ToList();

        var inputSourceByIndex = detail.Inputs.ToDictionary(x => x.Index, x => x.Source);
        var numericValueByIndex = detail.Numerics.ToDictionary(x => x.Index, x => x.Value);

        var predecessorGatesByIndex = gates.ToDictionary(
            g => g.Index,
            g => g.Inputs
                .Select(x => x.Trim())
                .Where(x => TryParseSourceGateIndex(x, out _))
                .Select(x =>
                {
                    _ = TryParseSourceGateIndex(x, out var idx);
                    return idx;
                })
                .Where(gatesByIndex.ContainsKey)
                .Distinct()
                .ToList());

        var successorGatesByIndex = gates.ToDictionary(g => g.Index, _ => new List<int>());
        foreach (var (gateIndex, predecessors) in predecessorGatesByIndex)
        {
            foreach (var predecessor in predecessors)
            {
                successorGatesByIndex[predecessor].Add(gateIndex);
            }
        }

        var gateDepthByIndex = ComputeLongestPathSourceDepth();

        var maxDepth = gateDepthByIndex.Count > 0 ? gateDepthByIndex.Values.Max() : 0;
        var gateLayers = new Dictionary<int, List<int>>();
        for (var depth = 0; depth <= maxDepth; depth++)
        {
            gateLayers[depth] = gates
                .Where(g => gateDepthByIndex[g.Index] == depth)
                .Select(g => g.Index)
                .OrderBy(x => x)
                .ToList();
        }

        Dictionary<int, int> ComputeLongestPathSourceDepth()
        {
            var depths = gates.ToDictionary(g => g.Index, _ => 0);
            var indegree = gates.ToDictionary(g => g.Index, g => predecessorGatesByIndex[g.Index].Count);
            var queue = new Queue<int>(
                indegree
                    .Where(x => x.Value == 0)
                    .Select(x => x.Key)
                    .OrderBy(x => x));
            var processed = new HashSet<int>();

            while (queue.Count > 0)
            {
                var gateIndex = queue.Dequeue();
                if (!processed.Add(gateIndex))
                {
                    continue;
                }

                foreach (var successor in successorGatesByIndex[gateIndex].OrderBy(x => x))
                {
                    var candidateDepth = depths[gateIndex] + 1;
                    if (candidateDepth > depths[successor])
                    {
                        depths[successor] = candidateDepth;
                    }

                    indegree[successor]--;
                    if (indegree[successor] == 0)
                    {
                        queue.Enqueue(successor);
                    }
                }
            }

            if (processed.Count == gates.Count)
            {
                return depths;
            }

            // Fallback for cycles: preserve existing depth and resolve as deep as possible.
            var visiting = new HashSet<int>();
            int ResolveDepth(int gateIndex)
            {
                if (processed.Contains(gateIndex))
                {
                    return depths[gateIndex];
                }

                if (visiting.Contains(gateIndex))
                {
                    return depths[gateIndex];
                }

                visiting.Add(gateIndex);
                var maxParentDepth = -1;
                foreach (var predecessor in predecessorGatesByIndex[gateIndex])
                {
                    if (!gatesByIndex.ContainsKey(predecessor))
                    {
                        continue;
                    }

                    var predecessorDepth = ResolveDepth(predecessor);
                    if (predecessorDepth > maxParentDepth)
                    {
                        maxParentDepth = predecessorDepth;
                    }
                }

                visiting.Remove(gateIndex);

                var resolvedDepth = Math.Max(depths[gateIndex], maxParentDepth + 1);
                if (resolvedDepth < 0)
                {
                    resolvedDepth = 0;
                }

                depths[gateIndex] = resolvedDepth;
                processed.Add(gateIndex);
                return resolvedDepth;
            }

            foreach (var gate in gates.OrderBy(g => g.Index))
            {
                ResolveDepth(gate.Index);
            }

            return depths;
        }

        var outputLinkedGatesByOutputIndex = new Dictionary<int, List<int>>();
        var outputLinksByGate = gates.ToDictionary(g => g.Index, _ => new List<int>());
        var outputSourceGateByOutputIndex = new Dictionary<int, int>();
        var outputEnableGateByOutputIndex = new Dictionary<int, int>();
        foreach (var output in outputs)
        {
            var linked = new HashSet<int>();

            if (TryParseSourceGateIndex(output.Source.Trim(), out var sourceGate) && gatesByIndex.ContainsKey(sourceGate))
            {
                linked.Add(sourceGate);
                outputSourceGateByOutputIndex[output.Index] = sourceGate;
            }

            var enableToken = output.Enable?.Trim() ?? string.Empty;
            if (TryParseSourceGateIndex(enableToken, out var enableGate) && gatesByIndex.ContainsKey(enableGate))
            {
                linked.Add(enableGate);
                outputEnableGateByOutputIndex[output.Index] = enableGate;
            }

            var linkedList = linked.OrderBy(x => x).ToList();
            outputLinkedGatesByOutputIndex[output.Index] = linkedList;
            foreach (var gateIndex in linkedList)
            {
                outputLinksByGate[gateIndex].Add(output.Index);
            }
        }

        var reachableOutputsByGate = new Dictionary<int, List<int>>();
        var reachableOutputsVisiting = new HashSet<int>();
        foreach (var gate in gates)
        {
            ResolveReachableOutputs(gate.Index);
        }

        var orderedOutputsForLayout = outputs.OrderBy(x => x.Index).ToList();
        var gateRank = new Dictionary<int, double>();
        var outputRank = new Dictionary<int, double>();

        List<int> ResolveReachableOutputs(int gateIndex)
        {
            if (reachableOutputsByGate.TryGetValue(gateIndex, out var cached))
            {
                return cached;
            }

            if (!reachableOutputsVisiting.Add(gateIndex))
            {
                return outputLinksByGate[gateIndex]
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();
            }

            var linkedOutputs = new HashSet<int>(outputLinksByGate[gateIndex]);
            foreach (var successor in successorGatesByIndex[gateIndex])
            {
                foreach (var outputIndex in ResolveReachableOutputs(successor))
                {
                    linkedOutputs.Add(outputIndex);
                }
            }

            reachableOutputsVisiting.Remove(gateIndex);
            var resolved = linkedOutputs.OrderBy(x => x).ToList();
            reachableOutputsByGate[gateIndex] = resolved;
            return resolved;
        }

        void RebuildGateRank()
        {
            var rank = 0;
            for (var depth = 0; depth <= maxDepth; depth++)
            {
                foreach (var gateIndex in gateLayers[depth])
                {
                    gateRank[gateIndex] = rank++;
                }
            }
        }

        void RebuildOutputRank()
        {
            for (var i = 0; i < orderedOutputsForLayout.Count; i++)
            {
                outputRank[orderedOutputsForLayout[i].Index] = i;
            }
        }

        double GetGateSortKey(int gateIndex)
        {
            var self = gateRank[gateIndex];
            var predecessorMean = GetNeighborMean(predecessorGatesByIndex[gateIndex], gateRank, self);
            var successorMean = GetNeighborMean(successorGatesByIndex[gateIndex], gateRank, self);
            var outputMean = reachableOutputsByGate[gateIndex].Count > 0
                ? GetNeighborMean(reachableOutputsByGate[gateIndex], outputRank, self)
                : self;

            return (predecessorMean * 0.25) + (successorMean * 0.25) + (outputMean * 0.50);
        }

        double GetOutputSortKey(LogicOutput output)
        {
            var linked = outputLinkedGatesByOutputIndex[output.Index];
            return linked.Count > 0
                ? GetNeighborMean(linked, gateRank, 100000 + output.Index)
                : 100000 + output.Index;
        }

        int CountCrossings(IReadOnlyList<int> leftOrder, IReadOnlyList<int> rightOrder, IReadOnlyList<(int Left, int Right)> edges)
        {
            if (edges.Count < 2 || leftOrder.Count == 0 || rightOrder.Count == 0)
            {
                return 0;
            }

            var leftPos = new Dictionary<int, int>();
            for (var i = 0; i < leftOrder.Count; i++)
            {
                leftPos[leftOrder[i]] = i;
            }

            var rightPos = new Dictionary<int, int>();
            for (var i = 0; i < rightOrder.Count; i++)
            {
                rightPos[rightOrder[i]] = i;
            }

            var filtered = edges
                .Where(e => leftPos.ContainsKey(e.Left) && rightPos.ContainsKey(e.Right))
                .ToList();

            var crossings = 0;
            for (var i = 0; i < filtered.Count - 1; i++)
            {
                var a = filtered[i];
                var aLeft = leftPos[a.Left];
                var aRight = rightPos[a.Right];
                for (var j = i + 1; j < filtered.Count; j++)
                {
                    var b = filtered[j];
                    var bLeft = leftPos[b.Left];
                    var bRight = rightPos[b.Right];
                    if ((aLeft - bLeft) * (aRight - bRight) < 0)
                    {
                        crossings++;
                    }
                }
            }

            return crossings;
        }

        int CountLayerCrossings(int depth, IReadOnlyList<int> layerCandidate)
        {
            var crossings = 0;

            if (depth > 0)
            {
                var prevLayer = gateLayers[depth - 1];
                var prevEdges = new List<(int Left, int Right)>();
                foreach (var gateIndex in layerCandidate)
                {
                    foreach (var predecessor in predecessorGatesByIndex[gateIndex])
                    {
                        if (gateDepthByIndex.TryGetValue(predecessor, out var predecessorDepth) && predecessorDepth == depth - 1)
                        {
                            prevEdges.Add((predecessor, gateIndex));
                        }
                    }
                }

                crossings += CountCrossings(prevLayer, layerCandidate, prevEdges);
            }

            if (depth < maxDepth)
            {
                var nextLayer = gateLayers[depth + 1];
                var nextEdges = new List<(int Left, int Right)>();
                foreach (var gateIndex in layerCandidate)
                {
                    foreach (var successor in successorGatesByIndex[gateIndex])
                    {
                        if (gateDepthByIndex.TryGetValue(successor, out var successorDepth) && successorDepth == depth + 1)
                        {
                            nextEdges.Add((gateIndex, successor));
                        }
                    }
                }

                crossings += CountCrossings(layerCandidate, nextLayer, nextEdges);
            }

            if (orderedOutputsForLayout.Count > 0)
            {
                var outputOrder = orderedOutputsForLayout.Select(x => x.Index).ToList();
                var outputEdges = new List<(int Left, int Right)>();
                foreach (var gateIndex in layerCandidate)
                {
                    foreach (var outputIndex in reachableOutputsByGate[gateIndex])
                    {
                        outputEdges.Add((gateIndex, outputIndex));
                    }
                }

                crossings += CountCrossings(layerCandidate, outputOrder, outputEdges);
            }

            return crossings;
        }

        int CountOutputCrossings(IReadOnlyList<LogicOutput> outputCandidate)
        {
            if (outputCandidate.Count < 2)
            {
                return 0;
            }

            var gateOrder = gateRank.OrderBy(x => x.Value).Select(x => x.Key).ToList();
            var outputOrder = outputCandidate.Select(x => x.Index).ToList();
            var edges = new List<(int Left, int Right)>();
            foreach (var (gateIndex, outputIndexes) in outputLinksByGate)
            {
                foreach (var outputIndex in outputIndexes)
                {
                    edges.Add((gateIndex, outputIndex));
                }
            }

            return CountCrossings(gateOrder, outputOrder, edges);
        }

        static double Median(IReadOnlyList<double> values, double fallback)
        {
            if (values.Count == 0)
            {
                return fallback;
            }

            var ordered = values.OrderBy(x => x).ToList();
            var mid = ordered.Count / 2;
            return ordered.Count % 2 == 0
                ? (ordered[mid - 1] + ordered[mid]) / 2d
                : ordered[mid];
        }

        void ImproveGateLayerBySwapping(int depth)
        {
            if (gateLayers[depth].Count < 2)
            {
                return;
            }

            var changed = true;
            while (changed)
            {
                changed = false;
                for (var i = 0; i < gateLayers[depth].Count - 1; i++)
                {
                    var baseline = CountLayerCrossings(depth, gateLayers[depth]);
                    (gateLayers[depth][i], gateLayers[depth][i + 1]) = (gateLayers[depth][i + 1], gateLayers[depth][i]);
                    var swapped = CountLayerCrossings(depth, gateLayers[depth]);
                    if (swapped < baseline)
                    {
                        changed = true;
                    }
                    else
                    {
                        (gateLayers[depth][i], gateLayers[depth][i + 1]) = (gateLayers[depth][i + 1], gateLayers[depth][i]);
                    }
                }
            }
        }

        void ImproveOutputOrderBySwapping()
        {
            if (orderedOutputsForLayout.Count < 2)
            {
                return;
            }

            var changed = true;
            while (changed)
            {
                changed = false;
                for (var i = 0; i < orderedOutputsForLayout.Count - 1; i++)
                {
                    var baseline = CountOutputCrossings(orderedOutputsForLayout);
                    (orderedOutputsForLayout[i], orderedOutputsForLayout[i + 1]) =
                        (orderedOutputsForLayout[i + 1], orderedOutputsForLayout[i]);
                    var swapped = CountOutputCrossings(orderedOutputsForLayout);
                    if (swapped < baseline)
                    {
                        changed = true;
                    }
                    else
                    {
                        (orderedOutputsForLayout[i], orderedOutputsForLayout[i + 1]) =
                            (orderedOutputsForLayout[i + 1], orderedOutputsForLayout[i]);
                    }
                }
            }
        }

        void ApplyBfsLayerSweepOrdering(int passes)
        {
            if (maxDepth <= 0 || passes <= 0)
            {
                return;
            }

            for (var pass = 0; pass < passes; pass++)
            {
                for (var depth = 1; depth <= maxDepth; depth++)
                {
                    var layer = gateLayers[depth];
                    if (layer.Count < 2)
                    {
                        continue;
                    }

                    var previousLayer = gateLayers[depth - 1];
                    var previousPositionByGate = previousLayer
                        .Select((gateIndex, index) => (gateIndex, index))
                        .ToDictionary(x => x.gateIndex, x => x.index);
                    var currentPositionByGate = layer
                        .Select((gateIndex, index) => (gateIndex, index))
                        .ToDictionary(x => x.gateIndex, x => x.index);

                    double ForwardScore(int gateIndex)
                    {
                        var parentPositions = predecessorGatesByIndex[gateIndex]
                            .Where(previousPositionByGate.ContainsKey)
                            .Select(parentGate => (double)previousPositionByGate[parentGate])
                            .ToList();

                        if (parentPositions.Count > 0)
                        {
                            return parentPositions.Average();
                        }

                        return GetNeighborMean(reachableOutputsByGate[gateIndex], outputRank, currentPositionByGate[gateIndex]);
                    }

                    gateLayers[depth] = layer
                        .OrderBy(ForwardScore)
                        .ThenBy(gateIndex => currentPositionByGate[gateIndex])
                        .ThenBy(gateIndex => gateIndex)
                        .ToList();
                }

                RebuildGateRank();

                for (var depth = maxDepth - 1; depth >= 0; depth--)
                {
                    var layer = gateLayers[depth];
                    if (layer.Count < 2)
                    {
                        continue;
                    }

                    var nextLayer = gateLayers[depth + 1];
                    var nextPositionByGate = nextLayer
                        .Select((gateIndex, index) => (gateIndex, index))
                        .ToDictionary(x => x.gateIndex, x => x.index);
                    var currentPositionByGate = layer
                        .Select((gateIndex, index) => (gateIndex, index))
                        .ToDictionary(x => x.gateIndex, x => x.index);

                    double BackwardScore(int gateIndex)
                    {
                        var childPositions = successorGatesByIndex[gateIndex]
                            .Where(nextPositionByGate.ContainsKey)
                            .Select(childGate => (double)nextPositionByGate[childGate])
                            .ToList();

                        if (childPositions.Count > 0)
                        {
                            return childPositions.Average();
                        }

                        return GetNeighborMean(reachableOutputsByGate[gateIndex], outputRank, currentPositionByGate[gateIndex]);
                    }

                    gateLayers[depth] = layer
                        .OrderBy(BackwardScore)
                        .ThenBy(gateIndex => currentPositionByGate[gateIndex])
                        .ThenBy(gateIndex => gateIndex)
                        .ToList();
                }

                RebuildGateRank();
            }
        }

        void ApplySugiyamaLayerSweeps(int passes)
        {
            if (passes <= 0 || maxDepth <= 0)
            {
                return;
            }

            for (var pass = 0; pass < passes; pass++)
            {
                for (var depth = 1; depth <= maxDepth; depth++)
                {
                    var layer = gateLayers[depth];
                    if (layer.Count < 2)
                    {
                        continue;
                    }

                    var prevLayer = gateLayers[depth - 1];
                    var prevPos = prevLayer
                        .Select((gateIndex, index) => (gateIndex, index))
                        .ToDictionary(x => x.gateIndex, x => (double)x.index);

                    gateLayers[depth] = layer
                        .Select((gateIndex, index) => (gateIndex, index))
                        .OrderBy(x =>
                        {
                            var parentPositions = predecessorGatesByIndex[x.gateIndex]
                                .Where(prevPos.ContainsKey)
                                .Select(parent => prevPos[parent])
                                .ToList();
                            if (parentPositions.Count > 0)
                            {
                                return Median(parentPositions, x.index);
                            }

                            return GetNeighborMean(reachableOutputsByGate[x.gateIndex], outputRank, x.index);
                        })
                        .ThenBy(x => x.index)
                        .ThenBy(x => x.gateIndex)
                        .Select(x => x.gateIndex)
                        .ToList();
                }

                RebuildGateRank();

                for (var depth = maxDepth - 1; depth >= 0; depth--)
                {
                    var layer = gateLayers[depth];
                    if (layer.Count < 2)
                    {
                        continue;
                    }

                    var nextLayer = gateLayers[depth + 1];
                    var nextPos = nextLayer
                        .Select((gateIndex, index) => (gateIndex, index))
                        .ToDictionary(x => x.gateIndex, x => (double)x.index);

                    gateLayers[depth] = layer
                        .Select((gateIndex, index) => (gateIndex, index))
                        .OrderBy(x =>
                        {
                            var childPositions = successorGatesByIndex[x.gateIndex]
                                .Where(nextPos.ContainsKey)
                                .Select(child => nextPos[child])
                                .ToList();
                            if (childPositions.Count > 0)
                            {
                                return Median(childPositions, x.index);
                            }

                            return GetNeighborMean(reachableOutputsByGate[x.gateIndex], outputRank, x.index);
                        })
                        .ThenBy(x => x.index)
                        .ThenBy(x => x.gateIndex)
                        .Select(x => x.gateIndex)
                        .ToList();
                }

                RebuildGateRank();

                for (var depth = 0; depth <= maxDepth; depth++)
                {
                    ImproveGateLayerBySwapping(depth);
                }

                RebuildGateRank();
                ImproveOutputOrderBySwapping();
                RebuildOutputRank();
            }
        }

        void ApplyGlobalLayerSifting(int passes)
        {
            if (passes <= 0)
            {
                return;
            }

            for (var pass = 0; pass < passes; pass++)
            {
                var changed = false;
                for (var depth = 0; depth <= maxDepth; depth++)
                {
                    var layer = gateLayers[depth];
                    if (layer.Count < 3)
                    {
                        continue;
                    }

                    var working = layer.ToList();
                    for (var gateOrdinal = 0; gateOrdinal < layer.Count; gateOrdinal++)
                    {
                        var gateIndex = layer[gateOrdinal];
                        working = gateLayers[depth].ToList();
                        var currentIndex = working.IndexOf(gateIndex);
                        if (currentIndex < 0)
                        {
                            continue;
                        }

                        working.RemoveAt(currentIndex);
                        var bestIndex = currentIndex;
                        var bestScore = int.MaxValue;
                        for (var candidateIndex = 0; candidateIndex <= working.Count; candidateIndex++)
                        {
                            var candidate = working.ToList();
                            candidate.Insert(candidateIndex, gateIndex);
                            gateLayers[depth] = candidate;
                            var score = CountLayerCrossings(depth, candidate);
                            if (score < bestScore)
                            {
                                bestScore = score;
                                bestIndex = candidateIndex;
                            }
                        }

                        working.Insert(bestIndex, gateIndex);
                        gateLayers[depth] = working;
                        if (bestIndex != currentIndex)
                        {
                            changed = true;
                        }
                    }
                }

                RebuildGateRank();
                ImproveOutputOrderBySwapping();
                RebuildOutputRank();
                if (!changed)
                {
                    break;
                }
            }
        }

        RebuildGateRank();
        RebuildOutputRank();
        ApplyBfsLayerSweepOrdering(passes: 4);
        ApplySugiyamaLayerSweeps(passes: 12);
        ApplyGlobalLayerSifting(passes: 5);
        RebuildGateRank();
        for (var iteration = 0; iteration < 10; iteration++)
        {
            for (var depth = 0; depth <= maxDepth; depth++)
            {
                gateLayers[depth] = gateLayers[depth]
                    .OrderBy(GetGateSortKey)
                    .ThenBy(gateIndex => gateIndex)
                    .ToList();
            }

            RebuildGateRank();

            for (var depth = maxDepth; depth >= 0; depth--)
            {
                gateLayers[depth] = gateLayers[depth]
                    .OrderBy(GetGateSortKey)
                    .ThenBy(gateIndex => gateIndex)
                    .ToList();
            }

            RebuildGateRank();

            orderedOutputsForLayout = orderedOutputsForLayout
                .OrderBy(GetOutputSortKey)
                .ThenBy(x => x.Index)
                .ToList();
            RebuildOutputRank();

            for (var depth = 0; depth <= maxDepth; depth++)
            {
                ImproveGateLayerBySwapping(depth);
            }

            RebuildGateRank();
            ImproveOutputOrderBySwapping();
            RebuildOutputRank();
        }

        double GetOutputAnchorByGateRows(LogicOutput output, IReadOnlyDictionary<int, int> gateRows)
        {
            var fallback = 100000 + output.Index;

            var sourceRow = 0d;
            var enableRow = 0d;

            var hasSource = false;
            if (outputSourceGateByOutputIndex.TryGetValue(output.Index, out var sourceGate)
                && gateRows.TryGetValue(sourceGate, out var sourceGateRow))
            {
                hasSource = true;
                sourceRow = sourceGateRow;
            }

            var hasEnable = false;
            if (outputEnableGateByOutputIndex.TryGetValue(output.Index, out var enableGate)
                && gateRows.TryGetValue(enableGate, out var enableGateRow))
            {
                hasEnable = true;
                enableRow = enableGateRow;
            }

            if (hasSource && hasEnable)
            {
                return (sourceRow * 0.85) + (enableRow * 0.15);
            }

            if (hasSource)
            {
                return sourceRow;
            }

            if (hasEnable)
            {
                return enableRow;
            }

            if (!outputLinkedGatesByOutputIndex.TryGetValue(output.Index, out var linkedGates) || linkedGates.Count == 0)
            {
                return fallback;
            }

            return linkedGates
                .Where(gateRows.ContainsKey)
                .Select(gateIndex => (double)gateRows[gateIndex])
                .DefaultIfEmpty(fallback)
                .Average();
        }

        double GetOutputOrderKey(LogicOutput output, IReadOnlyDictionary<int, int> gateRows)
        {
            var anchor = GetOutputAnchorByGateRows(output, gateRows);
            var indexBias = Math.Max(0, output.Index - 1);
            return (anchor * 0.90) + (indexBias * 0.10);
        }

        Dictionary<int, int> ComputeGateRows(
            IReadOnlyList<LogicOutput> outputOrder,
            IReadOnlyDictionary<int, int>? seedRows = null)
        {
            var outputRowByIndex = new Dictionary<int, double>();
            for (var i = 0; i < outputOrder.Count; i++)
            {
                outputRowByIndex[outputOrder[i].Index] = i;
            }

            var outputIndexRowByIndex = outputOrder
                .ToDictionary(
                    output => output.Index,
                    output => (double)Math.Max(0, output.Index - 1));

            var maxRowSeed = Math.Max(gates.Count, outputOrder.Count) - 1;
            if (maxRowSeed < 0)
            {
                maxRowSeed = 0;
            }

            var gateTargetRow = new Dictionary<int, double>();
            foreach (var gate in gates)
            {
                var gateIndex = gate.Index;
                var seed = seedRows is not null && seedRows.TryGetValue(gateIndex, out var seededRow)
                    ? seededRow
                    : gates.Count > 1
                        ? (gateRank[gateIndex] / (gates.Count - 1d)) * maxRowSeed
                        : 0d;

                var directOutputMean = outputLinksByGate[gateIndex].Count > 0
                    ? outputLinksByGate[gateIndex]
                        .Where(outputRowByIndex.ContainsKey)
                        .Select(outputIndex => outputRowByIndex[outputIndex])
                        .DefaultIfEmpty(seed)
                        .Average()
                    : seed;

                var reachableOutputMean = reachableOutputsByGate[gateIndex].Count > 0
                    ? reachableOutputsByGate[gateIndex]
                        .Where(outputRowByIndex.ContainsKey)
                        .Select(outputIndex => outputRowByIndex[outputIndex])
                        .DefaultIfEmpty(directOutputMean)
                        .Average()
                    : directOutputMean;

                var indexAnchorMean = reachableOutputsByGate[gateIndex].Count > 0
                    ? reachableOutputsByGate[gateIndex]
                        .Where(outputIndexRowByIndex.ContainsKey)
                        .Select(outputIndex => outputIndexRowByIndex[outputIndex])
                        .DefaultIfEmpty(reachableOutputMean)
                        .Average()
                    : seed;

                var directWeight = outputLinksByGate[gateIndex].Count > 0 ? 0.78 : 0.0;
                var reachableWeight = reachableOutputsByGate[gateIndex].Count > 0 ? 0.12 : 0.0;
                var indexWeight = reachableOutputsByGate[gateIndex].Count > 0 ? 0.06 : 0.0;
                var seedWeight = 1.0 - directWeight - reachableWeight - indexWeight;

                gateTargetRow[gateIndex] = (directOutputMean * directWeight)
                                           + (reachableOutputMean * reachableWeight)
                                           + (indexAnchorMean * indexWeight)
                                           + (seed * seedWeight);
            }

            for (var relaxIteration = 0; relaxIteration < 10; relaxIteration++)
            {
                var nextRows = new Dictionary<int, double>();
                foreach (var gate in gates)
                {
                    var gateIndex = gate.Index;
                    var self = gateTargetRow[gateIndex];
                    var predecessorMean = GetNeighborMean(predecessorGatesByIndex[gateIndex], gateTargetRow, self);
                    var successorMean = GetNeighborMean(successorGatesByIndex[gateIndex], gateTargetRow, self);

                    var directOutputMean = outputLinksByGate[gateIndex].Count > 0
                        ? outputLinksByGate[gateIndex]
                            .Where(outputRowByIndex.ContainsKey)
                            .Select(outputIndex => outputRowByIndex[outputIndex])
                            .DefaultIfEmpty(self)
                            .Average()
                        : self;

                    var reachableOutputMean = reachableOutputsByGate[gateIndex].Count > 0
                        ? reachableOutputsByGate[gateIndex]
                            .Where(outputRowByIndex.ContainsKey)
                            .Select(outputIndex => outputRowByIndex[outputIndex])
                            .DefaultIfEmpty(directOutputMean)
                            .Average()
                        : directOutputMean;

                    var indexAnchorMean = reachableOutputsByGate[gateIndex].Count > 0
                        ? reachableOutputsByGate[gateIndex]
                            .Where(outputIndexRowByIndex.ContainsKey)
                            .Select(outputIndex => outputIndexRowByIndex[outputIndex])
                            .DefaultIfEmpty(reachableOutputMean)
                            .Average()
                        : self;

                    var directWeight = outputLinksByGate[gateIndex].Count > 0 ? 0.68 : 0.0;
                    var reachableWeight = reachableOutputsByGate[gateIndex].Count > 0 ? 0.14 : 0.0;
                    var indexWeight = reachableOutputsByGate[gateIndex].Count > 0 ? 0.08 : 0.0;
                    var predecessorWeight = predecessorGatesByIndex[gateIndex].Count > 0 ? 0.06 : 0.0;
                    var successorWeight = successorGatesByIndex[gateIndex].Count > 0 ? 0.04 : 0.0;
                    var selfWeight = 1.0 - directWeight - reachableWeight - indexWeight - predecessorWeight - successorWeight;

                    nextRows[gateIndex] = (directOutputMean * directWeight)
                                          + (reachableOutputMean * reachableWeight)
                                          + (indexAnchorMean * indexWeight)
                                          + (predecessorMean * predecessorWeight)
                                          + (successorMean * successorWeight)
                                          + (self * selfWeight);
                }

                gateTargetRow = nextRows;
            }

            var rows = new Dictionary<int, int>();
            for (var depth = 0; depth <= maxDepth; depth++)
            {
                var usedRows = new HashSet<int>();
                var layer = gateLayers[depth]
                    .OrderBy(gateIndex => gateTargetRow[gateIndex])
                    .ThenBy(gateIndex => gateIndex)
                    .ToList();

                foreach (var gateIndex in layer)
                {
                    var preferredRow = (int)Math.Round(gateTargetRow[gateIndex], MidpointRounding.AwayFromZero);
                    var assignedRow = FindNearestFreeRow(preferredRow, usedRows);
                    usedRows.Add(assignedRow);
                    rows[gateIndex] = assignedRow;
                }
            }

            return rows;
        }

        var gateXByIndex = gates.ToDictionary(
            gate => gate.Index,
            gate => gateColumnBaseX + (gateDepthByIndex[gate.Index] * gateColumnSpacing));
        var maxGateX = gateXByIndex.Count > 0 ? gateXByIndex.Values.Max() : gateColumnBaseX;
        var outputXByIndex = new Dictionary<int, double>();
        foreach (var output in outputs)
        {
            double? anchorGateX = null;
            if (outputSourceGateByOutputIndex.TryGetValue(output.Index, out var sourceGate)
                && gateXByIndex.TryGetValue(sourceGate, out var sourceGateX))
            {
                anchorGateX = sourceGateX;
            }

            if (outputEnableGateByOutputIndex.TryGetValue(output.Index, out var enableGate)
                && gateXByIndex.TryGetValue(enableGate, out var enableGateX))
            {
                anchorGateX = anchorGateX.HasValue
                    ? Math.Max(anchorGateX.Value, enableGateX)
                    : enableGateX;
            }

            outputXByIndex[output.Index] = (anchorGateX ?? maxGateX) + outputFromGateOffsetX;
        }

        double GetOutputX(int outputIndex)
        {
            return outputXByIndex.TryGetValue(outputIndex, out var x)
                ? x
                : maxGateX + outputFromGateOffsetX;
        }

        Dictionary<int, int> AssignOutputRows(IReadOnlyDictionary<int, int> gateRows)
        {
            var rows = new Dictionary<int, int>();
            var outputCount = orderedOutputsForLayout.Count;
            if (outputCount == 0)
            {
                return rows;
            }

            var costs = new int[outputCount, outputCount];
            for (var i = 0; i < outputCount; i++)
            {
                var output = orderedOutputsForLayout[i];
                var anchor = GetOutputAnchorByGateRows(output, gateRows);
                if (double.IsNaN(anchor) || double.IsInfinity(anchor) || anchor > 10000)
                {
                    anchor = i;
                }

                var sourceAnchor = outputSourceGateByOutputIndex.TryGetValue(output.Index, out var sourceGate)
                                   && gateRows.TryGetValue(sourceGate, out var sourceGateRow)
                    ? sourceGateRow
                    : anchor;
                var enableAnchor = outputEnableGateByOutputIndex.TryGetValue(output.Index, out var enableGate)
                                   && gateRows.TryGetValue(enableGate, out var enableGateRow)
                    ? enableGateRow
                    : anchor;

                for (var row = 0; row < outputCount; row++)
                {
                    var baseCost = Math.Abs(row - anchor) * 80d;
                    var sourceCost = Math.Abs(row - sourceAnchor) * 50d;
                    var enableCost = Math.Abs(row - enableAnchor) * 30d;
                    var orderCost = Math.Abs(row - i) * 8d;
                    costs[i, row] = (int)Math.Round(baseCost + sourceCost + enableCost + orderCost, MidpointRounding.AwayFromZero);
                }
            }

            var assignment = SolveAssignmentHungarian(costs);
            for (var i = 0; i < outputCount; i++)
            {
                var output = orderedOutputsForLayout[i];
                rows[output.Index] = assignment[i];
            }

            return rows;
        }

        static int[] SolveAssignmentHungarian(int[,] cost)
        {
            var n = cost.GetLength(0);
            var m = cost.GetLength(1);
            if (n == 0)
            {
                return [];
            }

            var u = new int[n + 1];
            var v = new int[m + 1];
            var p = new int[m + 1];
            var way = new int[m + 1];

            for (var i = 1; i <= n; i++)
            {
                p[0] = i;
                var j0 = 0;
                var minv = new int[m + 1];
                Array.Fill(minv, int.MaxValue / 4);
                var used = new bool[m + 1];

                do
                {
                    used[j0] = true;
                    var i0 = p[j0];
                    var delta = int.MaxValue / 4;
                    var j1 = 0;
                    for (var j = 1; j <= m; j++)
                    {
                        if (used[j])
                        {
                            continue;
                        }

                        var cur = cost[i0 - 1, j - 1] - u[i0] - v[j];
                        if (cur < minv[j])
                        {
                            minv[j] = cur;
                            way[j] = j0;
                        }

                        if (minv[j] < delta)
                        {
                            delta = minv[j];
                            j1 = j;
                        }
                    }

                    for (var j = 0; j <= m; j++)
                    {
                        if (used[j])
                        {
                            u[p[j]] += delta;
                            v[j] -= delta;
                        }
                        else
                        {
                            minv[j] -= delta;
                        }
                    }

                    j0 = j1;
                }
                while (p[j0] != 0);

                do
                {
                    var j1 = way[j0];
                    p[j0] = p[j1];
                    j0 = j1;
                }
                while (j0 != 0);
            }

            var assignment = new int[n];
            Array.Fill(assignment, -1);
            for (var j = 1; j <= m; j++)
            {
                if (p[j] > 0 && p[j] <= n)
                {
                    assignment[p[j] - 1] = j - 1;
                }
            }

            for (var i = 0; i < n; i++)
            {
                if (assignment[i] < 0)
                {
                    assignment[i] = i;
                }
            }

            return assignment;
        }

        static bool SegmentCrossesRect(
            double x1,
            double y1,
            double x2,
            double y2,
            double left,
            double top,
            double right,
            double bottom)
        {
            if (left >= right || top >= bottom)
            {
                return false;
            }

            var minX = Math.Min(x1, x2);
            var maxX = Math.Max(x1, x2);
            if (maxX < left || minX > right)
            {
                return false;
            }

            if (Math.Abs(x2 - x1) < 0.0001)
            {
                var x = x1;
                if (x < left || x > right)
                {
                    return false;
                }

                var minY = Math.Min(y1, y2);
                var maxY = Math.Max(y1, y2);
                return !(maxY < top || minY > bottom);
            }

            var startX = Math.Max(minX, left);
            var endX = Math.Min(maxX, right);
            var t1 = (startX - x1) / (x2 - x1);
            var t2 = (endX - x1) / (x2 - x1);
            var yy1 = y1 + ((y2 - y1) * t1);
            var yy2 = y1 + ((y2 - y1) * t2);
            var minYInBand = Math.Min(yy1, yy2);
            var maxYInBand = Math.Max(yy1, yy2);
            return !(maxYInBand < top || minYInBand > bottom);
        }

        static bool SegmentsProperlyCross(
            double ax,
            double ay,
            double bx,
            double by,
            double cx,
            double cy,
            double dx,
            double dy)
        {
            const double eps = 0.001;

            static double Orientation(double px, double py, double qx, double qy, double rx, double ry)
            {
                return ((qx - px) * (ry - py)) - ((qy - py) * (rx - px));
            }

            static bool SamePoint(double px, double py, double qx, double qy, double tolerance)
            {
                return Math.Abs(px - qx) <= tolerance && Math.Abs(py - qy) <= tolerance;
            }

            if (SamePoint(ax, ay, cx, cy, eps)
                || SamePoint(ax, ay, dx, dy, eps)
                || SamePoint(bx, by, cx, cy, eps)
                || SamePoint(bx, by, dx, dy, eps))
            {
                return false;
            }

            var o1 = Orientation(ax, ay, bx, by, cx, cy);
            var o2 = Orientation(ax, ay, bx, by, dx, dy);
            var o3 = Orientation(cx, cy, dx, dy, ax, ay);
            var o4 = Orientation(cx, cy, dx, dy, bx, by);

            if (Math.Abs(o1) <= eps || Math.Abs(o2) <= eps || Math.Abs(o3) <= eps || Math.Abs(o4) <= eps)
            {
                return false;
            }

            return (o1 > 0) != (o2 > 0) && (o3 > 0) != (o4 > 0);
        }

        var gateRowByIndex = ComputeGateRows(orderedOutputsForLayout);
        orderedOutputsForLayout = orderedOutputsForLayout
            .OrderBy(output => GetOutputOrderKey(output, gateRowByIndex))
            .ThenBy(output => output.Index)
            .ToList();

        gateRowByIndex = ComputeGateRows(orderedOutputsForLayout, gateRowByIndex);
        orderedOutputsForLayout = orderedOutputsForLayout
            .OrderBy(output => GetOutputOrderKey(output, gateRowByIndex))
            .ThenBy(output => output.Index)
            .ToList();

        var outputRowByIndex = AssignOutputRows(gateRowByIndex);

        int CountLineOverlapsForGate(
            int gateIndex,
            IReadOnlyDictionary<int, int> gateRows,
            IReadOnlyDictionary<int, int> outputRowsByIndex)
        {
            if (!gateRows.TryGetValue(gateIndex, out var gateRow))
            {
                return 0;
            }

            var gateX = gateXByIndex[gateIndex];
            var gateY = topMargin + (gateRow * gateRowSpacing);
            var rectLeft = gateX + 6;
            var rectTop = gateY + 6;
            var rectRight = gateX + gateWidth - 6;
            var rectBottom = gateY + gateHeight - 6;

            double GateCenterY(int idx) => topMargin + (gateRows[idx] * gateRowSpacing) + (gateHeight / 2d);
            double OutputCenterY(int idx) => topMargin + (outputRowsByIndex[idx] * gateRowSpacing) + (gateHeight / 2d);

            var overlaps = 0;

            foreach (var toGate in gates)
            {
                var toGateIndex = toGate.Index;
                if (!gateRows.ContainsKey(toGateIndex))
                {
                    continue;
                }

                foreach (var predecessor in predecessorGatesByIndex[toGateIndex])
                {
                    if (!gateRows.ContainsKey(predecessor))
                    {
                        continue;
                    }

                    if (predecessor == gateIndex || toGateIndex == gateIndex)
                    {
                        continue;
                    }

                    var x1 = gateXByIndex[predecessor] + gateWidth;
                    var y1 = GateCenterY(predecessor);
                    var x2 = gateXByIndex[toGateIndex];
                    var y2 = GateCenterY(toGateIndex);
                    if (SegmentCrossesRect(x1, y1, x2, y2, rectLeft, rectTop, rectRight, rectBottom))
                    {
                        overlaps++;
                    }
                }
            }

            foreach (var output in orderedOutputsForLayout)
            {
                if (!outputRowsByIndex.ContainsKey(output.Index))
                {
                    continue;
                }

                var targetX = GetOutputX(output.Index);
                var targetY = OutputCenterY(output.Index);

                if (outputSourceGateByOutputIndex.TryGetValue(output.Index, out var sourceGate)
                    && gateRows.ContainsKey(sourceGate)
                    && sourceGate != gateIndex)
                {
                    var sourceX = gateXByIndex[sourceGate] + gateWidth;
                    var sourceY = GateCenterY(sourceGate);
                    if (SegmentCrossesRect(sourceX, sourceY, targetX, targetY, rectLeft, rectTop, rectRight, rectBottom))
                    {
                        overlaps++;
                    }
                }

                if (outputEnableGateByOutputIndex.TryGetValue(output.Index, out var enableGate)
                    && gateRows.ContainsKey(enableGate)
                    && enableGate != gateIndex)
                {
                    var sourceX = gateXByIndex[enableGate] + gateWidth;
                    var sourceY = GateCenterY(enableGate);
                    if (SegmentCrossesRect(sourceX, sourceY, targetX, targetY, rectLeft, rectTop, rectRight, rectBottom))
                    {
                        overlaps++;
                    }
                }
            }

            return overlaps;
        }

        int CountGlobalLineCrossings(
            IReadOnlyDictionary<int, int> gateRows,
            IReadOnlyDictionary<int, int> outputRowsByIndex)
        {
            if (gateRows.Count == 0)
            {
                return 0;
            }

            double GateCenterY(int idx) => topMargin + (gateRows[idx] * gateRowSpacing) + (gateHeight / 2d);
            double OutputCenterY(int idx) => topMargin + (outputRowsByIndex[idx] * gateRowSpacing) + (gateHeight / 2d);

            var segments = new List<(int SourceId, string TargetType, int TargetId, double X1, double Y1, double X2, double Y2)>();

            foreach (var toGate in gates)
            {
                var toGateIndex = toGate.Index;
                if (!gateRows.ContainsKey(toGateIndex))
                {
                    continue;
                }

                foreach (var predecessor in predecessorGatesByIndex[toGateIndex])
                {
                    if (!gateRows.ContainsKey(predecessor))
                    {
                        continue;
                    }

                    segments.Add((
                        SourceId: predecessor,
                        TargetType: "G",
                        TargetId: toGateIndex,
                        X1: gateXByIndex[predecessor] + gateWidth,
                        Y1: GateCenterY(predecessor),
                        X2: gateXByIndex[toGateIndex],
                        Y2: GateCenterY(toGateIndex)));
                }
            }

            foreach (var output in orderedOutputsForLayout)
            {
                if (!outputRowsByIndex.ContainsKey(output.Index))
                {
                    continue;
                }

                var targetX = GetOutputX(output.Index);
                var targetY = OutputCenterY(output.Index);

                if (outputSourceGateByOutputIndex.TryGetValue(output.Index, out var sourceGate)
                    && gateRows.ContainsKey(sourceGate))
                {
                    segments.Add((
                        SourceId: sourceGate,
                        TargetType: "O",
                        TargetId: output.Index,
                        X1: gateXByIndex[sourceGate] + gateWidth,
                        Y1: GateCenterY(sourceGate),
                        X2: targetX,
                        Y2: targetY));
                }

                if (outputEnableGateByOutputIndex.TryGetValue(output.Index, out var enableGate)
                    && gateRows.ContainsKey(enableGate))
                {
                    segments.Add((
                        SourceId: enableGate,
                        TargetType: "O",
                        TargetId: output.Index,
                        X1: gateXByIndex[enableGate] + gateWidth,
                        Y1: GateCenterY(enableGate),
                        X2: targetX,
                        Y2: targetY));
                }
            }

            var crossings = 0;
            for (var i = 0; i < segments.Count - 1; i++)
            {
                var left = segments[i];
                for (var j = i + 1; j < segments.Count; j++)
                {
                    var right = segments[j];
                    if (left.SourceId == right.SourceId)
                    {
                        continue;
                    }

                    if (left.TargetType == right.TargetType && left.TargetId == right.TargetId)
                    {
                        continue;
                    }

                    if (SegmentsProperlyCross(
                        left.X1,
                        left.Y1,
                        left.X2,
                        left.Y2,
                        right.X1,
                        right.Y1,
                        right.X2,
                        right.Y2))
                    {
                        crossings++;
                    }
                }
            }

            return crossings;
        }

        int ComputeRowLayoutScore(
            IReadOnlyDictionary<int, int> gateRows,
            IReadOnlyDictionary<int, int> outputRowsByIndex)
        {
            var crossings = CountGlobalLineCrossings(gateRows, outputRowsByIndex);
            var overlaps = gateRows.Keys.Sum(gateIndex => CountLineOverlapsForGate(gateIndex, gateRows, outputRowsByIndex));
            var alignment = 0;
            foreach (var output in orderedOutputsForLayout)
            {
                if (!outputRowsByIndex.TryGetValue(output.Index, out var outputRow))
                {
                    continue;
                }

                if (outputSourceGateByOutputIndex.TryGetValue(output.Index, out var sourceGate)
                    && gateRows.TryGetValue(sourceGate, out var sourceRow))
                {
                    alignment += Math.Abs(outputRow - sourceRow);
                }

                if (outputEnableGateByOutputIndex.TryGetValue(output.Index, out var enableGate)
                    && gateRows.TryGetValue(enableGate, out var enableRow))
                {
                    alignment += Math.Abs(outputRow - enableRow);
                }
            }

            return (crossings * 100000) + (overlaps * 1000) + (alignment * 300);
        }

        void OptimizeGateRowsAgainstLineOverlap(Dictionary<int, int> gateRows, Dictionary<int, int> outputRowsByIndex)
        {
            for (var pass = 0; pass < 4; pass++)
            {
                var changed = false;
                for (var depth = 0; depth <= maxDepth; depth++)
                {
                    var layer = gateLayers[depth];
                    if (layer.Count < 2)
                    {
                        continue;
                    }

                    var orderedLayer = layer
                        .OrderByDescending(gateIndex => CountLineOverlapsForGate(gateIndex, gateRows, outputRowsByIndex))
                        .ThenBy(gateIndex => gateIndex)
                        .ToList();

                    foreach (var gateIndex in orderedLayer)
                    {
                        var baselineScore = ComputeRowLayoutScore(gateRows, outputRowsByIndex);
                        if (baselineScore == 0)
                        {
                            continue;
                        }

                        var currentRow = gateRows[gateIndex];
                        var occupiedRows = layer
                            .Where(x => x != gateIndex)
                            .Select(x => gateRows[x])
                            .ToHashSet();
                        var preferredRow = currentRow;

                        var bestRow = currentRow;
                        var bestOutputRows = outputRowsByIndex.ToDictionary(pair => pair.Key, pair => pair.Value);
                        var bestScore = baselineScore + (Math.Abs(currentRow - preferredRow) * 6);

                        for (var delta = 1; delta <= 20; delta++)
                        {
                            foreach (var candidateRow in new[] { currentRow - delta, currentRow + delta })
                            {
                                if (candidateRow < 0 || occupiedRows.Contains(candidateRow))
                                {
                                    continue;
                                }

                                gateRows[gateIndex] = candidateRow;
                                var candidateOutputRows = AssignOutputRows(gateRows);
                                var candidateScore = ComputeRowLayoutScore(gateRows, candidateOutputRows)
                                                     + (Math.Abs(candidateRow - preferredRow) * 6)
                                                     + Math.Abs(candidateRow - currentRow);
                                if (candidateScore < bestScore)
                                {
                                    bestScore = candidateScore;
                                    bestRow = candidateRow;
                                    bestOutputRows = candidateOutputRows;
                                }
                            }
                        }

                        gateRows[gateIndex] = bestRow;
                        if (bestRow != currentRow)
                        {
                            changed = true;
                            outputRowsByIndex.Clear();
                            foreach (var pair in bestOutputRows)
                            {
                                outputRowsByIndex[pair.Key] = pair.Value;
                            }
                        }
                    }
                }

                if (!changed)
                {
                    break;
                }
            }
        }

        void OptimizeGateRowsByAdjacentSwaps(Dictionary<int, int> gateRows, Dictionary<int, int> outputRowsByIndex)
        {
            for (var pass = 0; pass < 8; pass++)
            {
                var changed = false;
                for (var depth = 0; depth <= maxDepth; depth++)
                {
                    var layer = gateLayers[depth]
                        .Where(gateRows.ContainsKey)
                        .OrderBy(gateIndex => gateRows[gateIndex])
                        .ThenBy(gateIndex => gateIndex)
                        .ToList();
                    if (layer.Count < 2)
                    {
                        continue;
                    }

                    for (var i = 0; i < layer.Count - 1; i++)
                    {
                        var upper = layer[i];
                        var lower = layer[i + 1];
                        var upperRow = gateRows[upper];
                        var lowerRow = gateRows[lower];
                        if (upperRow == lowerRow)
                        {
                            continue;
                        }

                        var baselineScore = ComputeRowLayoutScore(gateRows, outputRowsByIndex);

                        gateRows[upper] = lowerRow;
                        gateRows[lower] = upperRow;
                        var candidateOutputRows = AssignOutputRows(gateRows);
                        var candidateScore = ComputeRowLayoutScore(gateRows, candidateOutputRows);
                        if (candidateScore < baselineScore)
                        {
                            changed = true;
                            outputRowsByIndex.Clear();
                            foreach (var pair in candidateOutputRows)
                            {
                                outputRowsByIndex[pair.Key] = pair.Value;
                            }

                            (layer[i], layer[i + 1]) = (layer[i + 1], layer[i]);
                        }
                        else
                        {
                            gateRows[upper] = upperRow;
                            gateRows[lower] = lowerRow;
                        }
                    }
                }

                if (!changed)
                {
                    break;
                }
            }
        }

        OptimizeGateRowsAgainstLineOverlap(gateRowByIndex, outputRowByIndex);
        OptimizeGateRowsByAdjacentSwaps(gateRowByIndex, outputRowByIndex);
        outputRowByIndex = AssignOutputRows(gateRowByIndex);

        var gateYByIndex = new Dictionary<int, double>();
        foreach (var (gateIndex, row) in gateRowByIndex)
        {
            gateYByIndex[gateIndex] = topMargin + (row * gateRowSpacing);
        }

        foreach (var gate in gates)
        {
            var key = $"GATE{gate.Index}";
            var symbol = GetAnsiGateSymbol(gate.Algorithm);
            var kind = GetGateKind(gate.Algorithm);
            var title = BuildLogicGateTitle(gate, symbol);
            var v = GetOrCreateVertex(key, title, kind, gate.Algorithm);
            positions[v] = new Point(gateXByIndex[gate.Index], gateYByIndex[gate.Index]);
        }

        // Create per-gate input strings so labels line up with each gate's input pins.
        foreach (var gate in gates)
        {
            var gateKey = $"GATE{gate.Index}";
            var gateVertex = vertices[gateKey];
            var gateX = gateXByIndex[gate.Index];
            var gateY = gateYByIndex[gate.Index];

            var inputTokens = OrderGateInputTokens(gate, gateYByIndex);
            var totalSlots = inputTokens.Count;
            if (totalSlots == 0)
            {
                continue;
            }

            for (var inputSlot = 0; inputSlot < totalSlots; inputSlot++)
            {
                var token = inputTokens[inputSlot];
                var coreToken = GetGateInputTokenCore(token);
                var isInverted = IsInvertedGateInputToken(token);
                if (IsGateOutputToken(coreToken))
                {
                    continue;
                }

                var inputKey = $"IN_{gate.Index}_{inputSlot + 1}";
                var kind = GetInputKind(coreToken);
                var baseTitle = FormatInputLabel(coreToken, inputSourceByIndex, numericValueByIndex);
                var title = baseTitle;
                var inputVertex = GetOrCreateVertex(inputKey, title, kind);

                var (_, inputHeight) = ComputeCompactTextVertexSize(title);
                static double ComputeSlotAnchorY(double height, int slot, int slotCount, double margin)
                {
                    var clampedHeight = Math.Max(1, height);
                    if (slotCount <= 1 || slot < 0)
                    {
                        return clampedHeight / 2d;
                    }

                    var usableHeight = Math.Max(1d, clampedHeight - (2d * margin));
                    var normalized = slotCount <= 1 ? 0.5d : slot / Math.Max(1d, slotCount - 1d);
                    return margin + (normalized * usableHeight);
                }

                // Align source/target slot anchors so these edges render horizontal.
                var targetAnchorY = gateY + ComputeSlotAnchorY(gateHeight, inputSlot, totalSlots, margin: 8);
                var sourceAnchorOffsetY = ComputeSlotAnchorY(inputHeight, inputSlot, totalSlots, margin: 4);
                var inputY = targetAnchorY - sourceAnchorOffsetY;
                positions[inputVertex] = new Point(gateX - inputToGateOffsetX, inputY);
                if (isInverted)
                {
                    var inversionKey = $"IN_NOT_{gate.Index}_{inputSlot + 1}";
                    var inversionVertex = GetOrCreateVertex(inversionKey, "NOT", "GATE_NOT_INLINE", "NOT");
                    positions[inversionVertex] = new Point(
                        gateX - (inputToGateOffsetX * 0.65),
                        targetAnchorY - (gateHeight / 2d));

                    graph.AddEdge(new LogicEdge(inputVertex, inversionVertex)
                    {
                        Text = coreToken
                    });

                    graph.AddEdge(new LogicEdge(inversionVertex, gateVertex)
                    {
                        Text = token,
                        TargetSlot = inputSlot,
                        TargetSlotCount = totalSlots
                    });
                }
                else
                {
                    graph.AddEdge(new LogicEdge(inputVertex, gateVertex)
                    {
                        Text = token,
                        SourceSlot = inputSlot,
                        SourceSlotCount = totalSlots,
                        TargetSlot = inputSlot,
                        TargetSlotCount = totalSlots
                    });
                }
            }
        }

        var outputRows = outputRowByIndex
            .ToDictionary(
                x => x.Key,
                x => topMargin + (x.Value * gateRowSpacing));

        var pendingGateEdges = new List<(
            int SourceGateIndex,
            LogicVertex TargetVertex,
            string Text,
            int TargetSlot,
            int TargetSlotCount,
            double TargetSortY,
            int TieBreak)>();

        static double GetTargetSlotOffset(int targetSlot, int targetSlotCount, double spacing)
        {
            return targetSlotCount > 1
                ? (targetSlot - ((targetSlotCount - 1) / 2.0)) * spacing
                : 0;
        }

        foreach (var output in orderedOutputsForLayout)
        {
            var key = $"OUT{output.Index}";
            var outputY = outputRows[output.Index];

            var title = string.IsNullOrWhiteSpace(output.Enable)
                ? output.Destination
                : $"{output.Destination}\nEN: {output.Enable}";
            var v = GetOrCreateVertex(key, title, "OUTPUT");
            positions[v] = new Point(GetOutputX(output.Index), outputY);
        }

        foreach (var gate in gates)
        {
            var to = vertices[$"GATE{gate.Index}"];
            var inputTokens = OrderGateInputTokens(gate, gateYByIndex);
            var totalSlots = inputTokens.Count;
            for (var inputSlot = 0; inputSlot < totalSlots; inputSlot++)
            {
                var rawToken = inputTokens[inputSlot];
                var key = GetGateInputTokenCore(rawToken);
                var isInverted = IsInvertedGateInputToken(rawToken);
                if (!IsGateOutputToken(key))
                {
                    continue;
                }

                if (TryParseSourceGateIndex(key, out var sourceGateIndex)
                    && gatesByIndex.ContainsKey(sourceGateIndex))
                {
                    var targetSortY = gateYByIndex[gate.Index]
                                      + GetTargetSlotOffset(inputSlot, totalSlots, inputPinSpacing);
                    if (isInverted)
                    {
                        var sourceVertex = vertices[$"GATE{sourceGateIndex}"];
                        var inversionKey = $"EDGE_NOT_{gate.Index}_{inputSlot + 1}";
                        var inversionVertex = GetOrCreateVertex(inversionKey, "NOT", "GATE_NOT_INLINE", "NOT");
                        positions[inversionVertex] = new Point(
                            gateXByIndex[gate.Index] - (inputToGateOffsetX * 0.65),
                            targetSortY - (gateHeight / 2d));

                        // Route source-gate fanout through the common pending-edge pipeline
                        // so 3+ fanout soft-reference rules can apply consistently.
                        pendingGateEdges.Add((
                            SourceGateIndex: sourceGateIndex,
                            TargetVertex: inversionVertex,
                            Text: key,
                            TargetSlot: -1,
                            TargetSlotCount: 0,
                            TargetSortY: targetSortY,
                            TieBreak: inputSlot));

                        graph.AddEdge(new LogicEdge(inversionVertex, to)
                        {
                            Text = rawToken,
                            TargetSlot = inputSlot,
                            TargetSlotCount = totalSlots
                        });
                    }
                    else
                    {
                        pendingGateEdges.Add((
                            SourceGateIndex: sourceGateIndex,
                            TargetVertex: to,
                            Text: rawToken,
                            TargetSlot: inputSlot,
                            TargetSlotCount: totalSlots,
                            TargetSortY: targetSortY,
                            TieBreak: inputSlot));
                    }
                }
            }
        }

        foreach (var output in outputs)
        {
            var fromKey = output.Source.Trim();
            var enableKey = output.Enable?.Trim() ?? string.Empty;
            var to = vertices[$"OUT{output.Index}"];

            var hasSourceToken = !string.IsNullOrWhiteSpace(fromKey);
            var hasEnableGate = TryParseSourceGateIndex(enableKey, out var enableGateIndex)
                                && gatesByIndex.ContainsKey(enableGateIndex);
            var targetSlotCount = Math.Max(1, (hasSourceToken ? 1 : 0) + (hasEnableGate ? 1 : 0));
            var targetSlot = 0;

            if (hasSourceToken)
            {
                if (TryParseSourceGateIndex(fromKey, out var sourceGateIndex)
                    && gatesByIndex.ContainsKey(sourceGateIndex))
                {
                    var targetSortY = outputRows[output.Index]
                                      + GetTargetSlotOffset(targetSlot, targetSlotCount, Math.Min(inputPinSpacing, 28));
                    pendingGateEdges.Add((
                        SourceGateIndex: sourceGateIndex,
                        TargetVertex: to,
                        Text: $"SO{output.Index}",
                        TargetSlot: targetSlot,
                        TargetSlotCount: targetSlotCount,
                        TargetSortY: targetSortY,
                        TieBreak: targetSlot));
                }
                else
                {
                    var sourceLabel = FormatInputLabel(fromKey, inputSourceByIndex, numericValueByIndex);
                    var sourceKind = GetInputKind(fromKey);
                    var sourceVertex = GetOrCreateVertex($"OUTSRC_{output.Index}", sourceLabel, sourceKind);
                    var sourceYOffset = GetTargetSlotOffset(targetSlot, targetSlotCount, Math.Min(inputPinSpacing, 28));
                    positions[sourceVertex] = new Point(GetOutputX(output.Index) - inputToGateOffsetX, outputRows[output.Index] + sourceYOffset);
                    graph.AddEdge(new LogicEdge(sourceVertex, to)
                    {
                        Text = fromKey,
                        SourceSlot = targetSlot,
                        SourceSlotCount = targetSlotCount,
                        TargetSlot = targetSlot,
                        TargetSlotCount = targetSlotCount
                    });
                }

                targetSlot++;
            }

            if (hasEnableGate)
            {
                var targetSortY = outputRows[output.Index]
                                  + GetTargetSlotOffset(targetSlot, targetSlotCount, Math.Min(inputPinSpacing, 28));
                pendingGateEdges.Add((
                    SourceGateIndex: enableGateIndex,
                    TargetVertex: to,
                    Text: $"EN {enableKey}",
                    TargetSlot: targetSlot,
                    TargetSlotCount: targetSlotCount,
                    TargetSortY: targetSortY,
                    TieBreak: targetSlot));
            }
        }

        static bool SamePendingEdge(
            (int SourceGateIndex, LogicVertex TargetVertex, string Text, int TargetSlot, int TargetSlotCount, double TargetSortY, int TieBreak) left,
            (int SourceGateIndex, LogicVertex TargetVertex, string Text, int TargetSlot, int TargetSlotCount, double TargetSortY, int TieBreak) right)
        {
            return left.SourceGateIndex == right.SourceGateIndex
                   && ReferenceEquals(left.TargetVertex, right.TargetVertex)
                   && left.TargetSlot == right.TargetSlot
                   && left.TargetSlotCount == right.TargetSlotCount
                   && left.TieBreak == right.TieBreak
                   && left.Text.Equals(right.Text, StringComparison.OrdinalIgnoreCase);
        }

        static List<LogicEdge> BuildPreviewEdges(
            IReadOnlyList<(int SourceGateIndex, LogicVertex TargetVertex, string Text, int TargetSlot, int TargetSlotCount, double TargetSortY, int TieBreak)> pending,
            IReadOnlyDictionary<int, LogicVertex> sourceVertices)
        {
            var preview = new List<LogicEdge>(pending.Count);
            foreach (var sourceGroup in pending
                         .GroupBy(edge => edge.SourceGateIndex)
                         .OrderBy(group => group.Key))
            {
                if (!sourceVertices.TryGetValue(sourceGroup.Key, out var sourceVertex))
                {
                    continue;
                }

                var ordered = sourceGroup
                    .OrderBy(edge => edge.TargetSortY)
                    .ThenBy(edge => edge.TargetSlot)
                    .ThenBy(edge => edge.TieBreak)
                    .ThenBy(edge => edge.Text, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var sourceSlotCount = ordered.Count;
                for (var sourceSlot = 0; sourceSlot < ordered.Count; sourceSlot++)
                {
                    var edge = ordered[sourceSlot];
                    preview.Add(new LogicEdge(sourceVertex, edge.TargetVertex)
                    {
                        Text = edge.Text,
                        SourceSlot = sourceSlot,
                        SourceSlotCount = sourceSlotCount,
                        TargetSlot = edge.TargetSlot,
                        TargetSlotCount = edge.TargetSlotCount
                    });
                }
            }

            return preview;
        }

        static bool EdgeCrossesAny(
            LogicEdge candidate,
            IReadOnlyList<LogicEdge> existingEdges,
            IReadOnlyDictionary<LogicVertex, Point> layoutPositions)
        {
            var candidateSegment = CreateSegment(candidate, layoutPositions);
            foreach (var edge in existingEdges)
            {
                if (ReferenceEquals(edge, candidate))
                {
                    continue;
                }

                if (ReferenceEquals(edge.Source, candidate.Source))
                {
                    continue;
                }

                var segment = CreateSegment(edge, layoutPositions);
                if (SegmentsProperlyCross(
                        candidateSegment.Source.X,
                        candidateSegment.Source.Y,
                        candidateSegment.Target.X,
                        candidateSegment.Target.Y,
                        segment.Source.X,
                        segment.Source.Y,
                        segment.Target.X,
                        segment.Target.Y))
                {
                    return true;
                }
            }

            return false;
        }

        var sourceVertices = gatesByIndex.Keys.ToDictionary(index => index, index => vertices[$"GATE{index}"]);
        var hardPendingGateEdges = pendingGateEdges.ToList();
        var softPendingGateEdges = new List<(
            int SourceGateIndex,
            LogicVertex TargetVertex,
            string Text,
            int TargetSlot,
            int TargetSlotCount,
            double TargetSortY,
            int TieBreak)>();

        // Conservative soft-fanout:
        // - If a source gate fans out to 3+ branches, force soft refs for branches beyond 2 hard wires.
        // - Otherwise use crossing-based conversion plus a light fallback.
        foreach (var sourceGroup in pendingGateEdges
                     .GroupBy(edge => edge.SourceGateIndex)
                     .OrderBy(group => group.Key))
        {
            var ordered = sourceGroup
                .OrderBy(edge => edge.TargetSortY)
                .ThenBy(edge => edge.TargetSlot)
                .ThenBy(edge => edge.TieBreak)
                .ThenBy(edge => edge.Text, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (ordered.Count < 2)
            {
                continue;
            }

            var softEligibleFanout = ordered
                .Where(edge =>
                    edge.TargetVertex.Kind.Equals("OUTPUT", StringComparison.OrdinalIgnoreCase)
                    || edge.TargetVertex.Kind.StartsWith("GATE", StringComparison.OrdinalIgnoreCase))
                .OrderBy(edge => edge.TargetSortY)
                .ToList();

            var convertedForSource = false;

            if (ordered.Count >= 3 && softEligibleFanout.Count > 0)
            {
                var forcedSoftCount = Math.Min(
                    softEligibleFanout.Count,
                    Math.Max(1, ordered.Count - 2));

                foreach (var forced in softEligibleFanout.TakeLast(forcedSoftCount))
                {
                    hardPendingGateEdges.RemoveAll(edge => SamePendingEdge(edge, forced));
                    softPendingGateEdges.Add(forced);
                    convertedForSource = true;
                }
            }

            for (var i = 1; i < ordered.Count; i++)
            {
                var candidate = ordered[i];

                if (!hardPendingGateEdges.Any(edge => SamePendingEdge(edge, candidate)))
                {
                    continue;
                }

                var previewEdges = BuildPreviewEdges(hardPendingGateEdges, sourceVertices);
                var previewCandidate = previewEdges.FirstOrDefault(edge =>
                    edge.Source.Key.Equals($"GATE{candidate.SourceGateIndex}", StringComparison.OrdinalIgnoreCase)
                    && ReferenceEquals(edge.Target, candidate.TargetVertex)
                    && edge.TargetSlot == candidate.TargetSlot
                    && edge.TargetSlotCount == candidate.TargetSlotCount
                    && edge.Text.Equals(candidate.Text, StringComparison.OrdinalIgnoreCase));
                if (previewCandidate is null)
                {
                    continue;
                }

                var baselineEdges = new List<LogicEdge>(graph.Edges.Count + previewEdges.Count);
                baselineEdges.AddRange(graph.Edges);
                baselineEdges.AddRange(previewEdges);

                if (!EdgeCrossesAny(previewCandidate, baselineEdges, positions))
                {
                    continue;
                }

                hardPendingGateEdges.RemoveAll(edge => SamePendingEdge(edge, candidate));
                softPendingGateEdges.Add(candidate);
                convertedForSource = true;
            }

            if (!convertedForSource)
            {
                // Conservative fallback: if a gate fans out and has at least one gate/output branch,
                // soft-convert one branch even when strict crossing detection finds none.
                // Keep at least one hard wire from the source gate.
                if (ordered.Count >= 2 && softEligibleFanout.Count >= 1)
                {
                    var fallback = softEligibleFanout[^1];
                    hardPendingGateEdges.RemoveAll(edge => SamePendingEdge(edge, fallback));
                    softPendingGateEdges.Add(fallback);
                }
            }
        }

        foreach (var sourceGroup in hardPendingGateEdges
                     .GroupBy(edge => edge.SourceGateIndex)
                     .OrderBy(group => group.Key))
        {
            var sourceVertex = vertices[$"GATE{sourceGroup.Key}"];
            var orderedEdges = sourceGroup
                .OrderBy(edge => edge.TargetSortY)
                .ThenBy(edge => edge.TargetSlot)
                .ThenBy(edge => edge.TieBreak)
                .ThenBy(edge => edge.Text, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var sourceSlotCount = orderedEdges.Count;
            for (var sourceSlot = 0; sourceSlot < orderedEdges.Count; sourceSlot++)
            {
                var edge = orderedEdges[sourceSlot];
                graph.AddEdge(new LogicEdge(sourceVertex, edge.TargetVertex)
                {
                    Text = edge.Text,
                    SourceSlot = sourceSlot,
                    SourceSlotCount = sourceSlotCount,
                    TargetSlot = edge.TargetSlot,
                    TargetSlotCount = edge.TargetSlotCount
                });
            }
        }

        var softRefByTarget = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in softPendingGateEdges)
        {
            var targetKey = edge.TargetVertex.Key;
            softRefByTarget[targetKey] = softRefByTarget.TryGetValue(targetKey, out var existing)
                ? existing + 1
                : 1;
            var softIndex = softRefByTarget[targetKey];

            var targetX = positions.TryGetValue(edge.TargetVertex, out var targetPosition)
                ? targetPosition.X
                : edge.TargetVertex.Position.X;
            var targetY = positions.TryGetValue(edge.TargetVertex, out targetPosition)
                ? targetPosition.Y
                : edge.TargetVertex.Position.Y;
            var slotOffset = GetTargetSlotOffset(edge.TargetSlot, edge.TargetSlotCount, Math.Min(inputPinSpacing, 28));

            var sourceLabel = $"GATE{edge.SourceGateIndex}.OUT";
            var softKey = $"SOFT_OUT_{edge.SourceGateIndex}_{targetKey}_{softIndex}";
            var softVertex = GetOrCreateVertex(softKey, sourceLabel, "REF");
            positions[softVertex] = new Point(
                targetX - (inputToGateOffsetX * 0.8),
                targetY + slotOffset - 12);

            graph.AddEdge(new LogicEdge(softVertex, edge.TargetVertex)
            {
                Text = sourceLabel,
                SourceSlot = edge.TargetSlot,
                SourceSlotCount = edge.TargetSlotCount,
                TargetSlot = edge.TargetSlot,
                TargetSlotCount = edge.TargetSlotCount
            });
        }

        if (savedLayout is not null && savedLayout.Count > 0)
        {
            foreach (var (key, savedPosition) in savedLayout)
            {
                if (vertices.TryGetValue(key, out var vertex))
                {
                    positions[vertex] = new Point(savedPosition.X, savedPosition.Y);
                }
            }
        }

        static bool TryParseSourceGateIndex(string sourceKey, out int gateIndex)
        {
            gateIndex = 0;
            var trimmed = GetGateInputTokenCore(sourceKey).Trim().ToUpperInvariant();
            if (!trimmed.StartsWith("SO", StringComparison.Ordinal))
            {
                return false;
            }

            return int.TryParse(trimmed[2..], out gateIndex);
        }

        static bool IsGateOutputToken(string token) => TryParseSourceGateIndex(token, out _);

        static string GetGateInputTokenCore(string token)
        {
            var value = (token ?? string.Empty).Trim();
            if (value.StartsWith("NOT ", StringComparison.OrdinalIgnoreCase))
            {
                return value[4..].Trim();
            }

            if (value.StartsWith("!", StringComparison.Ordinal))
            {
                return value[1..].Trim();
            }

            return value;
        }

        static bool IsInvertedGateInputToken(string token)
        {
            var value = (token ?? string.Empty).Trim();
            return value.StartsWith("NOT ", StringComparison.OrdinalIgnoreCase)
                   || value.StartsWith("!", StringComparison.Ordinal);
        }

        static List<string> OrderGateInputTokens(LogicGate gate, IReadOnlyDictionary<int, double>? gateYByIndex = null)
        {
            var tokens = gate.Inputs
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (gateYByIndex is null || tokens.Count < 2)
                return tokens;

            // Identify which slots hold gate-output references (cross-tier edges).
            // Reorder only those slots by their source gate Y so the top source maps
            // to the top slot and the bottom source to the bottom slot, eliminating
            // structural crossings that routing alone cannot resolve.
            // Signal inputs retain their original slot positions.
            var gateOutputSlots = new List<int>();
            for (var i = 0; i < tokens.Count; i++)
            {
                var core = GetGateInputTokenCore(tokens[i]);
                if (IsGateOutputToken(core)
                    && TryParseSourceGateIndex(core, out var srcIdx)
                    && gateYByIndex.ContainsKey(srcIdx))
                {
                    gateOutputSlots.Add(i);
                }
            }

            if (gateOutputSlots.Count < 2)
                return tokens;

            var sortedTokens = gateOutputSlots
                .Select(slot => tokens[slot])
                .OrderBy(token =>
                {
                    var core = GetGateInputTokenCore(token);
                    TryParseSourceGateIndex(core, out var srcIdx);
                    return gateYByIndex.TryGetValue(srcIdx, out var y) ? y : double.MaxValue;
                })
                .ToList();

            var result = tokens.ToList();
            for (var i = 0; i < gateOutputSlots.Count; i++)
                result[gateOutputSlots[i]] = sortedTokens[i];

            return result;
        }

        static double GetNeighborMean(IReadOnlyList<int> neighbors, IReadOnlyDictionary<int, double> rankMap, double fallback)
        {
            if (neighbors.Count == 0)
            {
                return fallback;
            }

            var sum = 0d;
            var count = 0;
            foreach (var neighbor in neighbors)
            {
                if (!rankMap.TryGetValue(neighbor, out var rank))
                {
                    continue;
                }

                sum += rank;
                count++;
            }

            return count == 0 ? fallback : sum / count;
        }

        static int FindNearestFreeRow(int targetRow, HashSet<int> usedRows)
        {
            if (targetRow < 0)
            {
                targetRow = 0;
            }

            if (!usedRows.Contains(targetRow))
            {
                return targetRow;
            }

            for (var delta = 1; delta < 300; delta++)
            {
                var down = targetRow + delta;
                if (!usedRows.Contains(down))
                {
                    return down;
                }

                var up = targetRow - delta;
                if (up >= 0 && !usedRows.Contains(up))
                {
                    return up;
                }
            }

            return targetRow;
        }

        static string GetInputKind(string token)
        {
            var normalized = token.Trim().ToUpperInvariant();
            if (normalized.StartsWith("L", StringComparison.Ordinal))
            {
                return "INPUT";
            }

            if (normalized.StartsWith("NN", StringComparison.Ordinal))
            {
                return "NUMERIC";
            }

            return "REF";
        }

        static string FormatInputLabel(
            string token,
            IReadOnlyDictionary<int, string> inputSourceByIndex,
            IReadOnlyDictionary<int, string> numericValueByIndex)
        {
            var normalized = token.Trim().ToUpperInvariant();
            if (normalized.StartsWith("L", StringComparison.Ordinal)
                && int.TryParse(normalized[1..], out var lIndex)
                && inputSourceByIndex.TryGetValue(lIndex, out var source))
            {
                return $"L{lIndex} ({source})";
            }

            if (normalized.StartsWith("NN", StringComparison.Ordinal)
                && int.TryParse(normalized[2..], out var nnIndex)
                && numericValueByIndex.TryGetValue(nnIndex, out var value))
            {
                return $"NN{nnIndex} ({value})";
            }

            return token;
        }

        var hasSavedLayout = savedLayout is { Count: > 0 };
        ApplyLogicDiagram(graph, positions, runMsaglLayout: !hasSavedLayout);
    }

    private void ConfigureDiagramEditors()
    {
        LogicDiagramEditor.ItemsSource = _logicDiagramNodes;
        LogicDiagramEditor.Connections = _logicDiagramEdges;
        LogicDiagramEditor.DisableAutoPanning = true;
        LogicDiagramEditor.DisplayConnectionsOnTop = true;

        ChartDiagramEditor.ItemsSource = _chartDiagramNodes;
        ChartDiagramEditor.Connections = _chartDiagramEdges;
        ChartDiagramEditor.DisableAutoPanning = true;
        ChartDiagramEditor.DisplayConnectionsOnTop = true;
    }

    private void ClearLogicDiagram()
    {
        ResetLogicGateFollowerBindings();
        _logicDiagramEdges.Clear();
        _logicDiagramNodes.Clear();
        _logicDragSnapshotBefore = null;
        _diagramDragUndoStack.Clear();
    }

    private void ClearChartDiagram()
    {
        _chartDiagramEdges.Clear();
        _chartDiagramNodes.Clear();
        _chartExpandableUpstreamKeys.Clear();
        _chartExpandableDownstreamKeys.Clear();
        ClearChartDirectionalExpansionSelection();
    }

    private void ClearChartDirectionalExpansionSelection()
    {
        _chartExpandedUpstreamKeys.Clear();
        _chartExpandedDownstreamKeys.Clear();
    }

    private void ApplyLogicDiagram(
        LogicGraph graph,
        IReadOnlyDictionary<LogicVertex, Point> positions,
        bool runMsaglLayout)
    {
        ApplyDiagram(
            editor: LogicDiagramEditor,
            targetNodes: _logicDiagramNodes,
            targetEdges: _logicDiagramEdges,
            graph: graph,
            initialPositions: positions,
            showEdgeLabels: false,
            showEndpointLabels: false,
            runMsaglLayout: runMsaglLayout,
            optimizeGateMiddleCrossings: runMsaglLayout,
            minViewportZoom: 0,
            useCompactLogicTextNodes: true,
            anchorNotGatesToInputs: true);

        ConfigureLogicGateFollowerBindings();
    }

    private void ApplyChartDiagram(
        LogicGraph graph,
        IReadOnlyDictionary<LogicVertex, Point> positions,
        bool runMsaglLayout)
    {
        ApplyDiagram(
            editor: ChartDiagramEditor,
            targetNodes: _chartDiagramNodes,
            targetEdges: _chartDiagramEdges,
            graph: graph,
            initialPositions: positions,
            showEdgeLabels: false,
            showEndpointLabels: true,
            runMsaglLayout: runMsaglLayout,
            optimizeGateMiddleCrossings: false,
            // Use natural fit for chart; deferred re-fit handles first-open sizing.
            minViewportZoom: 0,
            useCompactLogicTextNodes: false,
            anchorNotGatesToInputs: false);
    }

    private static void ConfigureDiagramVertexDefaults(LogicVertex vertex, bool useCompactLogicTextNodes)
    {
        vertex.LineHeight = double.NaN;
        vertex.IsArrayMappingChart = false;

        if (vertex.Kind is "INPUT" or "NUMERIC" or "REF")
        {
            if (useCompactLogicTextNodes)
            {
                var (width, height) = ComputeCompactTextVertexSize(vertex.Title);
                vertex.Width = width;
                vertex.Height = height;
            }
            else
            {
                vertex.Width = 230;
                vertex.Height = 26;
            }
        }
        else if (vertex.Kind is "PLC_REF" or "PLC_REF_LEFT" or "PLC_REF_RIGHT")
        {
            vertex.Width = 230;
            vertex.Height = 26;
        }
        else if (vertex.Kind.Equals("GATE_NOT_INLINE", StringComparison.OrdinalIgnoreCase))
        {
            // Keep inline reverse-input NOT blocks proportionally smaller than standard gates.
            vertex.Width = 136;
            vertex.Height = 72;
        }
        else if (vertex.Kind.Equals("CHART", StringComparison.OrdinalIgnoreCase))
        {
            var (width, height) = ComputeChartVertexSize(vertex.Title);
            vertex.Width = width;
            vertex.Height = height;

            if (TryGetArrayChartMappingLineCount(vertex.Title, out var mappingLineCount) && mappingLineCount > 0)
            {
                // Match text row spacing to chart input slot spacing so FL rows align with arrows.
                vertex.IsArrayMappingChart = true;
                vertex.LineHeight = Math.Clamp(
                    height / (mappingLineCount + 1d),
                    16d,
                    80d);
            }
        }
        else
        {
            vertex.Width = 180;
            vertex.Height = 96;
        }

        var isClProgram = vertex.Key.StartsWith("CL:", StringComparison.OrdinalIgnoreCase)
                          || vertex.Key.StartsWith("CLR:", StringComparison.OrdinalIgnoreCase)
                          || vertex.Key.StartsWith("CLW:", StringComparison.OrdinalIgnoreCase);
        var isLogicReference = vertex.Key.StartsWith("LB:", StringComparison.OrdinalIgnoreCase);
        vertex.IsClProgram = isClProgram || isLogicReference;
        vertex.IsDraggable = true;
    }

    private static (double Width, double Height) ComputeChartVertexSize(string title)
    {
        var normalizedTitle = (title ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var lines = normalizedTitle.Split('\n');
        var lineCount = Math.Max(1, lines.Length);
        var longestLineLength = lines.Length == 0
            ? 0
            : lines.Max(line => line.TrimEnd().Length);

        var width = Math.Max(380, Math.Min(1100, 110 + (longestLineLength * 7.2)));
        if (lineCount >= 12)
        {
            width = Math.Max(width, 680);
        }

        var perLineHeight = lineCount switch
        {
            >= 48 => 31.0,
            >= 32 => 29.0,
            >= 20 => 27.0,
            >= 12 => 24.0,
            _ => 21.0
        };
        var height = 100 + (lineCount * perLineHeight);
        if (lineCount >= 40)
        {
            height = Math.Max(height, (lineCount - 1) * 32.0 + 180);
        }

        height = Math.Max(180, Math.Min(5000, height));
        return (width, height);
    }

    private static (double Width, double Height) ComputeCompactTextVertexSize(string title)
    {
        var normalizedTitle = (title ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var lines = normalizedTitle.Split('\n');
        var lineCount = Math.Max(1, lines.Length);
        var longestLineLength = lines.Length == 0
            ? 0
            : lines.Max(line => line.TrimEnd().Length);

        var width = 14d + (longestLineLength * 6.7d);
        width = Math.Max(44d, Math.Min(320d, width));

        var height = 22d + ((lineCount - 1) * 15d);
        height = Math.Max(22d, Math.Min(72d, height));

        return (Math.Ceiling(width), Math.Ceiling(height));
    }

    private void ApplyDiagram(
        NodifyEditor editor,
        ObservableCollection<LogicVertex> targetNodes,
        ObservableCollection<LogicEdge> targetEdges,
        LogicGraph graph,
        IReadOnlyDictionary<LogicVertex, Point> initialPositions,
        bool showEdgeLabels,
        bool showEndpointLabels,
        bool runMsaglLayout,
        bool optimizeGateMiddleCrossings,
        double minViewportZoom,
        bool useCompactLogicTextNodes,
        bool anchorNotGatesToInputs)
    {
        var vertices = graph.Vertices.ToList();
        foreach (var vertex in vertices)
        {
            ConfigureDiagramVertexDefaults(vertex, useCompactLogicTextNodes);
        }

        var resolvedPositions = new Dictionary<LogicVertex, Point>();
        foreach (var vertex in vertices)
        {
            if (initialPositions.TryGetValue(vertex, out var position))
            {
                resolvedPositions[vertex] = position;
                continue;
            }

            resolvedPositions[vertex] = new Point(40 + (resolvedPositions.Count % 5) * 220, 40 + (resolvedPositions.Count / 5) * 140);
        }

        if (runMsaglLayout)
        {
            var msaglPositions = TryComputeMsaglLayout(vertices, graph.Edges, resolvedPositions);
            if (msaglPositions is not null && msaglPositions.Count == resolvedPositions.Count)
            {
                var baselinePenalty = ComputeLayoutPenalty(vertices, graph.Edges, resolvedPositions);
                var msaglPenalty = ComputeLayoutPenalty(vertices, graph.Edges, msaglPositions);
                resolvedPositions = msaglPenalty <= baselinePenalty
                    ? msaglPositions
                    : resolvedPositions;
            }
        }

        NormalizePlcReferenceOrdering(vertices, resolvedPositions);

        if (optimizeGateMiddleCrossings)
        {
            OptimizeGateColumnOrderForCrossings(vertices, graph.Edges, resolvedPositions);
        }

        for (var pass = 0; pass < 3; pass++)
        {
            ReassignTargetSlotsBySourceY(graph.Edges, resolvedPositions);
            ReassignSourceSlotsByTargetY(graph.Edges, resolvedPositions);
        }

        OptimizeGatePortOrderingForCrossings(vertices, graph.Edges, resolvedPositions);
        ReassignTargetSlotsBySourceY(graph.Edges, resolvedPositions);
        ReassignSourceSlotsByTargetY(graph.Edges, resolvedPositions);
        AlignSingleConnectionGateToGateRows(graph.Edges, resolvedPositions);
        ReassignTargetSlotsBySourceY(graph.Edges, resolvedPositions);
        ReassignSourceSlotsByTargetY(graph.Edges, resolvedPositions);
        ResolvePrimaryNodeOverlaps(vertices, resolvedPositions);
        ReassignTargetSlotsBySourceY(graph.Edges, resolvedPositions);
        ReassignSourceSlotsByTargetY(graph.Edges, resolvedPositions);
        OptimizeTextNodePlacement(vertices, graph.Edges, resolvedPositions);
        if (anchorNotGatesToInputs)
        {
            AlignNotGatesToTargetInputs(graph.Edges, resolvedPositions);
        }
        AlignInputSourcesToFinalGateAnchors(graph.Edges, resolvedPositions);
        if (anchorNotGatesToInputs)
        {
            NudgeInlineNotAwayFromLineCrossings(graph.Edges, resolvedPositions);
            AlignInputSourcesToFinalGateAnchors(graph.Edges, resolvedPositions);
        }
        if (optimizeGateMiddleCrossings)
        {
            // Final crossing cleanup after late anchoring/nudging steps.
            ReassignTargetSlotsBySourceY(graph.Edges, resolvedPositions);
            ReassignSourceSlotsByTargetY(graph.Edges, resolvedPositions);
            OptimizeGateColumnOrderForCrossings(vertices, graph.Edges, resolvedPositions);
            ReassignTargetSlotsBySourceY(graph.Edges, resolvedPositions);
            ReassignSourceSlotsByTargetY(graph.Edges, resolvedPositions);

            if (anchorNotGatesToInputs)
            {
                AlignNotGatesToTargetInputs(graph.Edges, resolvedPositions);
                NudgeInlineNotAwayFromLineCrossings(graph.Edges, resolvedPositions);
            }

            AlignInputSourcesToFinalGateAnchors(graph.Edges, resolvedPositions);
        }

        targetEdges.Clear();
        targetNodes.Clear();

        foreach (var vertex in vertices
                     .OrderBy(v => resolvedPositions[v].Y)
                     .ThenBy(v => resolvedPositions[v].X))
        {
            vertex.Position = resolvedPositions[vertex];
            targetNodes.Add(vertex);
        }

        foreach (var edge in graph.Edges)
        {
            if (edge.Target.Kind.StartsWith("GATE_NOT", StringComparison.OrdinalIgnoreCase))
            {
                edge.TargetSlot = -1;
                edge.TargetSlotCount = 0;
            }

            if (edge.Source.Kind.StartsWith("GATE_NOT", StringComparison.OrdinalIgnoreCase))
            {
                edge.SourceSlot = -1;
                edge.SourceSlotCount = 0;
            }

            var isPlcEdge = IsPlcReferenceKind(edge.Source.Kind)
                || IsPlcReferenceKind(edge.Target.Kind);
            var isIoDirectionLabel = edge.Text.Contains("Input", StringComparison.OrdinalIgnoreCase)
                || edge.Text.Contains("Output", StringComparison.OrdinalIgnoreCase);

            if (isIoDirectionLabel)
            {
                // Force direction labels into the centered white label box only.
                edge.StartLabel = string.Empty;
                edge.EndLabel = string.Empty;
            }

            edge.ShowText = (showEdgeLabels || isPlcEdge || isIoDirectionLabel) && !string.IsNullOrWhiteSpace(edge.Text);
            edge.ShowEndpointLabels = showEndpointLabels
                && !isPlcEdge
                && !isIoDirectionLabel
                && (!string.IsNullOrWhiteSpace(edge.StartLabel) || !string.IsNullOrWhiteSpace(edge.EndLabel));
            edge.ForceLeftTargetInput = edge.Target.Kind.StartsWith("GATE", StringComparison.OrdinalIgnoreCase);
            edge.UpdateConnectionPoints();
            targetEdges.Add(edge);
        }

        ApplyOrthogonalEdgeRouting(graph.Edges, resolvedPositions);

        FitDiagramToScreen(editor, minViewportZoom);
    }

    private static void ApplyOrthogonalEdgeRouting(
        IReadOnlyList<LogicEdge> edges,
        Dictionary<LogicVertex, Point> positions)
    {
        const double nodeMargin = 8.0;
        const double edgeSeparation = 14.0;  // px between adjacent elbow columns
        const double sameYThreshold = 2.0;

        // Build inflated bounding rects keyed by vertex so source/target can be
        // excluded from obstacle checks for their own edge.
        // "REF" nodes are soft-reference text labels placed inside routing corridors;
        // they are annotations, not physical blocks, so edges may cross them freely.
        var vertexBounds = positions
            .Where(kvp => !kvp.Key.Kind.Equals("REF", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                kvp => kvp.Key,
                kvp => new Rect(
                    kvp.Value.X - nodeMargin,
                    kvp.Value.Y - nodeMargin,
                    kvp.Key.Width  + nodeMargin * 2,
                    kvp.Key.Height + nodeMargin * 2));

        var allBounds = vertexBounds.Values.ToArray();

        // True if a thin vertical strip at x spanning [yMin..yMax] intersects any
        // non-excluded bounding rect.
        bool ColumnBlocked(double x, double yMin, double yMax, Rect srcRect, Rect tgtRect)
        {
            var strip = new Rect(x - 2, yMin, 4, Math.Max(1, yMax - yMin));
            foreach (var b in allBounds)
            {
                if (b == srcRect || b == tgtRect) continue;
                if (b.IntersectsWith(strip)) return true;
            }
            return false;
        }

        // True if a thin horizontal strip at y spanning [xMin..xMax] intersects any
        // non-excluded bounding rect.
        bool RowBlocked(double xMin, double xMax, double y, Rect srcRect, Rect tgtRect)
        {
            var strip = new Rect(xMin, y - 2, Math.Max(1, xMax - xMin), 4);
            foreach (var b in allBounds)
            {
                if (b == srcRect || b == tgtRect) continue;
                if (b.IntersectsWith(strip)) return true;
            }
            return false;
        }

        double FindSafeColumn(double preferred, double yMin, double yMax,
            double leftBound, double rightBound, Rect srcRect, Rect tgtRect)
        {
            if (!ColumnBlocked(preferred, yMin, yMax, srcRect, tgtRect))
                return preferred;

            for (var step = edgeSeparation; step < rightBound - leftBound; step += edgeSeparation)
            {
                var tryL = preferred - step;
                if (tryL > leftBound + 4 && !ColumnBlocked(tryL, yMin, yMax, srcRect, tgtRect))
                    return tryL;

                var tryR = preferred + step;
                if (tryR < rightBound - 4 && !ColumnBlocked(tryR, yMin, yMax, srcRect, tgtRect))
                    return tryR;
            }

            return preferred;
        }

        // Scan the full [leftBound..rightBound] range for an elbowX where the vertical
        // column AND both horizontal legs are all clear of obstacles.
        double FindClearElbow(double preferred, double yMin, double yMax,
            double srcX, double tgtX, double srcY, double tgtY,
            Rect srcRect, Rect tgtRect)
        {
            var leftBound  = srcX + 8;
            var rightBound = tgtX - 8;
            if (leftBound >= rightBound) return preferred;

            bool AllClear(double ex) =>
                !ColumnBlocked(ex, yMin, yMax, srcRect, tgtRect) &&
                !RowBlocked(Math.Min(srcX, ex), Math.Max(srcX, ex), srcY, srcRect, tgtRect) &&
                !RowBlocked(Math.Min(ex, tgtX), Math.Max(ex, tgtX), tgtY, srcRect, tgtRect);

            // Try the preferred position first, then scan outward in alternating steps.
            if (AllClear(preferred)) return preferred;

            for (var step = edgeSeparation; step < rightBound - leftBound; step += edgeSeparation)
            {
                var tryL = preferred - step;
                if (tryL >= leftBound && AllClear(tryL)) return tryL;

                var tryR = preferred + step;
                if (tryR <= rightBound && AllClear(tryR)) return tryR;
            }

            // Fall back to best vertical-only safe column if no fully clear position found.
            return FindSafeColumn(preferred, yMin, yMax, srcX, tgtX, srcRect, tgtRect);
        }

        // Classify edges using vertex POSITIONS, not connection points.
        // Wide input nodes can have their right-edge connection point overshoot the
        // gate's left-pin X, making a geometrically forward edge look backward by
        // connection-point comparison alone.  Position-based classification is reliable.
        double SrcPosX(LogicEdge e) => positions.TryGetValue(e.Source, out var p) ? p.X : e.SourcePoint.X;
        double TgtPosX(LogicEdge e) => positions.TryGetValue(e.Target, out var p) ? p.X : e.TargetPoint.X;
        bool IsGeometricForward(LogicEdge e) => TgtPosX(e) > SrcPosX(e) + 4;

        // Group all forward edges by their routing corridor (source column → target column).
        // This ensures edges from DIFFERENT source nodes that travel the same corridor are
        // spread apart, not just edges that share the same single source.
        const double corridorGrid = 15.0;

        var corridorGroups = edges
            .Where(e => IsGeometricForward(e)
                     && Math.Abs(e.SourcePoint.Y - e.TargetPoint.Y) > sameYThreshold)
            .GroupBy(e => (
                srcBucket: (int)Math.Round(SrcPosX(e) / corridorGrid),
                tgtBucket: (int)Math.Round(TgtPosX(e) / corridorGrid)));

        foreach (var corridor in corridorGroups)
        {
            // Sort by the start of each edge's vertical span so the interval
            // coloring below processes edges in ascending Y order.
            var items = corridor
                .Select(e => (
                    Edge: e,
                    YMin: Math.Min(e.SourcePoint.Y, e.TargetPoint.Y),
                    YMax: Math.Max(e.SourcePoint.Y, e.TargetPoint.Y)))
                .OrderBy(x => x.YMin)
                .ToList();

            // ── Interval-graph lane assignment ───────────────────────────────────
            // Only edges whose vertical Y spans actually overlap need separate elbow
            // columns (lanes).  Edges that occupy distinct Y zones can share a lane
            // without any visual conflict, so we don't waste stagger budget on them.
            var laneEnds = new List<double>(); // highest Y reached by each lane so far
            var lanePerId = new int[items.Count];

            for (var j = 0; j < items.Count; j++)
            {
                var yMin = items[j].YMin;
                var yMax = items[j].YMax;

                // Find the first lane whose last edge ends strictly before this one starts.
                var lane = -1;
                for (var k = 0; k < laneEnds.Count; k++)
                {
                    if (laneEnds[k] < yMin)
                    {
                        lane = k;
                        laneEnds[k] = yMax;
                        break;
                    }
                }

                if (lane < 0)
                {
                    lane = laneEnds.Count;
                    laneEnds.Add(yMax);
                }

                lanePerId[j] = lane;
            }

            var totalLanes = laneEnds.Count;

            for (var j = 0; j < items.Count; j++)
            {
                var (edge, yMin, yMax) = items[j];
                var src = edge.SourcePoint;
                var tgt = edge.TargetPoint;

                var srcRect = vertexBounds.TryGetValue(edge.Source, out var sr) ? sr : Rect.Empty;
                var tgtRect = vertexBounds.TryGetValue(edge.Target, out var tr) ? tr : Rect.Empty;

                var midX    = (src.X + tgt.X) / 2d;
                var lane    = lanePerId[j];
                var stagger = totalLanes > 1 ? (lane - (totalLanes - 1) / 2.0) * edgeSeparation : 0.0;
                // Some wide-node pin geometries can make src/tgt connection X bounds appear
                // reversed even when the edge is position-forward. Normalize clamp bounds to
                // avoid runtime ArgumentException ("min cannot be greater than max").
                var clampMin = Math.Min(src.X, tgt.X) + 8;
                var clampMax = Math.Max(src.X, tgt.X) - 8;
                var cand    = clampMin <= clampMax
                    ? Math.Clamp(midX + stagger, clampMin, clampMax)
                    : midX + stagger;

                var elbowX = FindClearElbow(cand, yMin, yMax,
                    src.X, tgt.X, src.Y, tgt.Y, srcRect, tgtRect);

                edge.SetRoute([src, new Point(elbowX, src.Y), new Point(elbowX, tgt.Y), tgt]);
            }
        }

        // ── Backward / feedback edges ─────────────────────────────────────────────
        //
        // Backward edges (target is to the LEFT of source) represent feedback loops.
        // BuildHorizontalPreferredRoute uses a hardcoded -36 px offset that ignores
        // node positions and passes straight through intermediate blocks.  Here we
        // replace that route with one that clears above the topmost node in the
        // horizontal span, then stagger multiple backward edges into separate lanes.

        const double backwardSideGap = 24.0;  // stub length right of source / left of target
        const double backwardLaneGap = 18.0;  // vertical separation between stacked back-routes

        // Only treat edges as backward when the source vertex is genuinely to the RIGHT
        // of the target vertex by position.  Edges where a wide input node's connection
        // point overshoots the gate pin (IsGeometricForward == false but positions say
        // left-to-right) are NOT feedback loops and must not get the loop-above route.
        var backwardEdges = edges
            .Where(e => !IsGeometricForward(e))
            .OrderBy(e => Math.Min(SrcPosX(e), TgtPosX(e))) // stable order
            .ToList();

        // Assign each backward edge a lane index (0 = closest above, 1 = one step higher…)
        // so that multiple feedback loops stack cleanly without overlapping.
        for (var bi = 0; bi < backwardEdges.Count; bi++)
        {
            var edge   = backwardEdges[bi];
            var src    = edge.SourcePoint;
            var tgt    = edge.TargetPoint;

            var srcRect = vertexBounds.TryGetValue(edge.Source, out var bsr) ? bsr : Rect.Empty;
            var tgtRect = vertexBounds.TryGetValue(edge.Target, out var btr) ? btr : Rect.Empty;

            // X span the route will travel horizontally (with side stubs included).
            var xLeft  = Math.Min(tgt.X, src.X) - backwardSideGap;
            var xRight = Math.Max(tgt.X, src.X) + backwardSideGap;

            // Find the topmost (minimum) Y of every node whose bounds overlap
            // the horizontal span.  Route above all of them.
            var clearanceTop = allBounds
                .Where(b => b != srcRect && b != tgtRect
                         && b.Right > xLeft && b.Left < xRight)
                .Select(b => b.Top)
                .DefaultIfEmpty(Math.Min(src.Y, tgt.Y))
                .Min();

            // Each additional backward edge gets its own lane further above.
            var routeY = clearanceTop - 30.0 - (bi * backwardLaneGap);

            // Clamp stub columns just outside source/target node bounds so the
            // short vertical stubs don't re-enter the node rectangles.
            var rightStubX = srcRect != Rect.Empty
                ? srcRect.Right + 4
                : src.X + backwardSideGap;
            var leftStubX  = tgtRect != Rect.Empty
                ? tgtRect.Left - 4
                : tgt.X - backwardSideGap;

            edge.SetRoute([
                src,
                new Point(rightStubX, src.Y),
                new Point(rightStubX, routeY),
                new Point(leftStubX,  routeY),
                new Point(leftStubX,  tgt.Y),
                tgt,
            ]);
        }
    }

    private static void AlignNotGatesToTargetInputs(
        IReadOnlyList<LogicEdge> edges,
        Dictionary<LogicVertex, Point> positions)
    {
        if (edges.Count == 0)
        {
            return;
        }

        static bool IsInlineInversionNot(LogicVertex vertex) =>
            vertex.Kind.Equals("GATE_NOT_INLINE", StringComparison.OrdinalIgnoreCase);

        var groupedByTargetGate = new Dictionary<LogicVertex, List<(LogicVertex NotVertex, double AnchorY)>>();

        foreach (var notVertex in edges
                     .SelectMany(edge => new[] { edge.Source, edge.Target })
                     .Where(IsInlineInversionNot)
                     .Distinct())
        {
            var notToGateEdge = edges.FirstOrDefault(edge =>
                ReferenceEquals(edge.Source, notVertex)
                && edge.Target.Kind.StartsWith("GATE", StringComparison.OrdinalIgnoreCase));
            if (notToGateEdge is null)
            {
                continue;
            }

            if (!positions.TryGetValue(notVertex, out var notPosition))
            {
                notPosition = notVertex.Position;
            }

            if (!positions.TryGetValue(notToGateEdge.Target, out var targetPosition))
            {
                targetPosition = notToGateEdge.Target.Position;
            }

            var targetHeight = Math.Max(1d, notToGateEdge.Target.Height);
            var targetMargin = ComputeTargetSlotMargin(notToGateEdge.Target, notToGateEdge.TargetSlotCount);
            var targetAnchorY = targetPosition.Y + ComputeSlotY(
                targetHeight,
                notToGateEdge.TargetSlot,
                notToGateEdge.TargetSlotCount,
                margin: targetMargin);

            var notWidth = Math.Max(1d, notVertex.Width);
            var notHeight = Math.Max(1d, notVertex.Height);
            var x = targetPosition.X - Math.Max(20d, notWidth + 10d);
            var y = targetAnchorY - (notHeight / 2d);
            positions[notVertex] = new Point(x, y);

            if (!groupedByTargetGate.TryGetValue(notToGateEdge.Target, out var bucket))
            {
                bucket = [];
                groupedByTargetGate[notToGateEdge.Target] = bucket;
            }

            bucket.Add((notVertex, targetAnchorY));
        }

        // Prevent stacked inline NOTs from overlapping when gate input anchors are close.
        const double minGap = 24d;
        foreach (var (_, bucket) in groupedByTargetGate)
        {
            if (bucket.Count < 2)
            {
                continue;
            }

            var ordered = bucket
                .OrderBy(item => item.AnchorY)
                .ToList();

            double? previousBottom = null;
            for (var i = 0; i < ordered.Count; i++)
            {
                var (notVertex, _) = ordered[i];
                if (!positions.TryGetValue(notVertex, out var notPosition))
                {
                    notPosition = notVertex.Position;
                }

                var notHeight = Math.Max(1d, notVertex.Height);
                var top = notPosition.Y;
                if (previousBottom is double bottom && top < bottom + minGap)
                {
                    top = bottom + minGap;
                    positions[notVertex] = new Point(notPosition.X, top);
                }

                previousBottom = top + notHeight;
            }
        }
    }

    private static void NudgeInlineNotAwayFromLineCrossings(
        IReadOnlyList<LogicEdge> edges,
        Dictionary<LogicVertex, Point> positions)
    {
        if (edges.Count == 0)
        {
            return;
        }

        static bool IsInlineInversionNot(LogicVertex vertex) =>
            vertex.Kind.Equals("GATE_NOT_INLINE", StringComparison.OrdinalIgnoreCase);

        var inlineNots = edges
            .SelectMany(edge => new[] { edge.Source, edge.Target })
            .Where(IsInlineInversionNot)
            .Distinct()
            .ToList();
        if (inlineNots.Count == 0)
        {
            return;
        }

        int CountCrossingThroughVertex(LogicVertex vertex)
        {
            if (!positions.TryGetValue(vertex, out var position))
            {
                position = vertex.Position;
            }

            var left = position.X + 6;
            var top = position.Y + 6;
            var right = position.X + Math.Max(1, vertex.Width) - 6;
            var bottom = position.Y + Math.Max(1, vertex.Height) - 6;

            var overlaps = 0;
            foreach (var edge in edges)
            {
                if (ReferenceEquals(edge.Source, vertex) || ReferenceEquals(edge.Target, vertex))
                {
                    continue;
                }

                var segment = CreateSegment(edge, positions);
                if (SegmentCrossesRect(
                        segment.Source.X,
                        segment.Source.Y,
                        segment.Target.X,
                        segment.Target.Y,
                        left,
                        top,
                        right,
                        bottom))
                {
                    overlaps++;
                }
            }

            return overlaps;
        }

        var xOffsets = new[] { 0d, -12d, -24d, -36d, -52d, 8d };
        var yOffsets = new[] { 0d, -18d, 18d, -30d, 30d, -44d, 44d, -60d, 60d };
        foreach (var notVertex in inlineNots)
        {
            if (!positions.TryGetValue(notVertex, out var basePosition))
            {
                basePosition = notVertex.Position;
            }

            var best = basePosition;
            var bestBodyCrossings = CountCrossingThroughVertex(notVertex);
            var bestGlobalCrossings = CountEdgeCrossings(edges, positions);
            var bestComposite = (bestBodyCrossings * 1_000_000d)
                                + (bestGlobalCrossings * 1_000d);

            foreach (var dx in xOffsets)
            {
                foreach (var dy in yOffsets)
                {
                    var candidate = new Point(basePosition.X + dx, basePosition.Y + dy);
                    positions[notVertex] = candidate;

                    var bodyCrossings = CountCrossingThroughVertex(notVertex);
                    var globalCrossings = CountEdgeCrossings(edges, positions);
                    var score = (bodyCrossings * 1_000_000d)
                                + (globalCrossings * 1_000d)
                                + Math.Abs(dx)
                                + Math.Abs(dy);
                    if (score < bestComposite)
                    {
                        best = candidate;
                        bestBodyCrossings = bodyCrossings;
                        bestGlobalCrossings = globalCrossings;
                        bestComposite = score;
                    }
                }
            }

            positions[notVertex] = best;
        }
    }

    private static void ResolvePrimaryNodeOverlaps(
        IReadOnlyList<LogicVertex> vertices,
        Dictionary<LogicVertex, Point> positions)
    {
        if (vertices.Count <= 1)
        {
            return;
        }

        static bool IsTextNodeKind(string kind) =>
            kind is "INPUT" or "NUMERIC" or "REF" or "PLC_REF" or "PLC_REF_LEFT" or "PLC_REF_RIGHT" or "LOGIC_REF" or "SOFT_REF" or "PI_REF";

        const double columnTolerance = 100d;
        const double minVerticalGap = 22d;

        var primaryNodes = vertices
            .Where(vertex => !IsTextNodeKind(vertex.Kind))
            .ToList();
        if (primaryNodes.Count <= 1)
        {
            return;
        }

        var columns = new List<(double MeanX, List<LogicVertex> Nodes)>();
        foreach (var node in primaryNodes
                     .OrderBy(vertex => positions.TryGetValue(vertex, out var p) ? p.X : vertex.Position.X))
        {
            var x = positions.TryGetValue(node, out var point) ? point.X : node.Position.X;
            var assigned = false;
            for (var i = 0; i < columns.Count; i++)
            {
                if (Math.Abs(columns[i].MeanX - x) > columnTolerance)
                {
                    continue;
                }

                columns[i].Nodes.Add(node);
                var updatedMean = columns[i].Nodes
                    .Average(vertex => positions.TryGetValue(vertex, out var p) ? p.X : vertex.Position.X);
                columns[i] = (updatedMean, columns[i].Nodes);
                assigned = true;
                break;
            }

            if (!assigned)
            {
                columns.Add((x, [node]));
            }
        }

        foreach (var (_, nodes) in columns)
        {
            if (nodes.Count <= 1)
            {
                continue;
            }

            var ordered = nodes
                .OrderBy(vertex => positions.TryGetValue(vertex, out var p) ? p.Y : vertex.Position.Y)
                .ToList();

            for (var i = 1; i < ordered.Count; i++)
            {
                var previous = ordered[i - 1];
                var current = ordered[i];

                var previousPosition = positions.TryGetValue(previous, out var prevPoint) ? prevPoint : previous.Position;
                var currentPosition = positions.TryGetValue(current, out var curPoint) ? curPoint : current.Position;
                var previousBottom = previousPosition.Y + Math.Max(1, previous.Height);
                var minimumCurrentTop = previousBottom + minVerticalGap;
                if (currentPosition.Y >= minimumCurrentTop)
                {
                    continue;
                }

                positions[current] = new Point(currentPosition.X, minimumCurrentTop);
            }
        }
    }

    private static void AlignInputSourcesToFinalGateAnchors(
        IReadOnlyList<LogicEdge> edges,
        Dictionary<LogicVertex, Point> positions)
    {
        if (edges.Count == 0)
        {
            return;
        }

        var edgeCountBySource = edges
            .GroupBy(edge => edge.Source)
            .ToDictionary(group => group.Key, group => group.Count());

        foreach (var edge in edges)
        {
            if (edge.Source.Kind is not ("INPUT" or "NUMERIC" or "REF"))
            {
                continue;
            }

            if (!edgeCountBySource.TryGetValue(edge.Source, out var sourceEdgeCount) || sourceEdgeCount != 1)
            {
                continue;
            }

            if (!positions.TryGetValue(edge.Source, out var sourcePosition))
            {
                sourcePosition = edge.Source.Position;
            }

            if (!positions.TryGetValue(edge.Target, out var targetPosition))
            {
                targetPosition = edge.Target.Position;
            }

            var sourceHeight = Math.Max(1d, edge.Source.Height);
            var targetHeight = Math.Max(1d, edge.Target.Height);
            var sourceMargin = ComputeAdaptiveSlotMargin(edge.SourceSlotCount, baseMargin: 4);
            var targetMargin = ComputeTargetSlotMargin(edge.Target, edge.TargetSlotCount);

            var sourceAnchorOffsetY = ComputeSlotY(
                sourceHeight,
                edge.SourceSlot,
                edge.SourceSlotCount,
                margin: sourceMargin);
            var targetAnchorY = targetPosition.Y + ComputeSlotY(
                targetHeight,
                edge.TargetSlot,
                edge.TargetSlotCount,
                margin: targetMargin);
            var sourceX = sourcePosition.X;
            var targetIsInlineInversionNot =
                edge.Target.Kind.Equals("GATE_NOT_INLINE", StringComparison.OrdinalIgnoreCase);
            if (targetIsInlineInversionNot)
            {
                // Force text input nodes to remain clearly left of inline NOT blocks.
                sourceX = Math.Min(
                    sourceX,
                    targetPosition.X - Math.Max(8d, edge.Source.Width + 8d));
            }
            else if (edge.Source.Kind.Equals("REF", StringComparison.OrdinalIgnoreCase)
                     && (edge.Target.Kind.StartsWith("GATE", StringComparison.OrdinalIgnoreCase)
                         || edge.Target.Kind.Equals("OUTPUT", StringComparison.OrdinalIgnoreCase)))
            {
                // Keep single-edge reference labels close to the destination input side.
                sourceX = targetPosition.X - Math.Max(18d, edge.Source.Width + 18d);
            }

            positions[edge.Source] = new Point(sourceX, targetAnchorY - sourceAnchorOffsetY);
        }
    }

    private static void AlignSingleConnectionGateToGateRows(
        IReadOnlyList<LogicEdge> edges,
        Dictionary<LogicVertex, Point> positions)
    {
        if (edges.Count == 0)
        {
            return;
        }

        var outgoingCountByGate = edges
            .Where(edge => edge.Source.Kind.StartsWith("GATE", StringComparison.OrdinalIgnoreCase))
            .GroupBy(edge => edge.Source)
            .ToDictionary(group => group.Key, group => group.Count());
        var incomingCountByGate = edges
            .Where(edge => edge.Target.Kind.StartsWith("GATE", StringComparison.OrdinalIgnoreCase))
            .GroupBy(edge => edge.Target)
            .ToDictionary(group => group.Key, group => group.Count());

        foreach (var edge in edges)
        {
            if (!edge.Source.Kind.StartsWith("GATE", StringComparison.OrdinalIgnoreCase)
                || !edge.Target.Kind.StartsWith("GATE", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var sourceOutgoing = outgoingCountByGate.TryGetValue(edge.Source, out var outCount) ? outCount : 0;
            var targetIncoming = incomingCountByGate.TryGetValue(edge.Target, out var inCount) ? inCount : 0;
            if (sourceOutgoing != 1 || targetIncoming != 1)
            {
                continue;
            }

            if (!positions.TryGetValue(edge.Source, out var sourcePosition))
            {
                sourcePosition = edge.Source.Position;
            }

            if (!positions.TryGetValue(edge.Target, out var targetPosition))
            {
                targetPosition = edge.Target.Position;
            }

            var sourceHeight = Math.Max(1d, edge.Source.Height);
            var targetHeight = Math.Max(1d, edge.Target.Height);
            var sourceMargin = ComputeAdaptiveSlotMargin(edge.SourceSlotCount, baseMargin: 4);
            var targetMargin = ComputeTargetSlotMargin(edge.Target, edge.TargetSlotCount);
            var sourceAnchorOffsetY = ComputeSlotY(
                sourceHeight,
                edge.SourceSlot,
                edge.SourceSlotCount,
                margin: sourceMargin);
            var targetAnchorOffsetY = ComputeSlotY(
                targetHeight,
                edge.TargetSlot,
                edge.TargetSlotCount,
                margin: targetMargin);

            var sourceAnchorY = sourcePosition.Y + sourceAnchorOffsetY;
            var desiredTargetY = sourceAnchorY - targetAnchorOffsetY;
            positions[edge.Target] = new Point(targetPosition.X, desiredTargetY);
        }
    }

    private static void OptimizeTextNodePlacement(
        IReadOnlyList<LogicVertex> vertices,
        IReadOnlyList<LogicEdge> edges,
        Dictionary<LogicVertex, Point> positions)
    {
        var textNodes = vertices
            .Where(v => v.Kind is "INPUT" or "NUMERIC" or "REF")
            .ToList();
        if (textNodes.Count == 0 || edges.Count == 0)
        {
            return;
        }

        var anchorPositions = textNodes.ToDictionary(v => v, v => positions[v]);
        var edgeCountBySource = edges
            .GroupBy(edge => edge.Source)
            .ToDictionary(group => group.Key, group => group.Count());
        var lockedTextNodes = edges
            .Where(edge =>
                edge.Source.Kind is "INPUT" or "NUMERIC" or "REF"
                && edge.Target.Kind.StartsWith("GATE", StringComparison.OrdinalIgnoreCase))
            .Select(edge => edge.Source)
            .ToHashSet();
        foreach (var (source, count) in edgeCountBySource)
        {
            if (count == 1 && source.Kind is "INPUT" or "NUMERIC" or "REF")
            {
                lockedTextNodes.Add(source);
            }
        }
        foreach (var node in textNodes)
        {
            if (node.Key.StartsWith("IN_", StringComparison.OrdinalIgnoreCase))
            {
                // Keep all input columns pinned to their computed anchor rows.
                lockedTextNodes.Add(node);
            }
        }
        static Rect GetRect(LogicVertex vertex, Point p, double margin = 0)
            => new(
                p.X - margin,
                p.Y - margin,
                Math.Max(1, vertex.Width) + (margin * 2),
                Math.Max(1, vertex.Height) + (margin * 2));

        int ScoreNode(LogicVertex candidate, Point candidatePosition)
        {
            var rect = GetRect(candidate, candidatePosition, margin: 2);
            var overlaps = 0;
            var segments = edges
                .Select(edge => CreateSegment(edge, positions))
                .ToList();

            foreach (var segment in segments)
            {
                if (ReferenceEquals(segment.Edge.Source, candidate) || ReferenceEquals(segment.Edge.Target, candidate))
                {
                    continue;
                }

                if (SegmentCrossesRect(
                        segment.Source.X,
                        segment.Source.Y,
                        segment.Target.X,
                        segment.Target.Y,
                        rect.Left,
                        rect.Top,
                        rect.Right,
                        rect.Bottom))
                {
                    overlaps++;
                }
            }

            var nodeCollisions = 0;
            foreach (var other in vertices)
            {
                if (ReferenceEquals(other, candidate))
                {
                    continue;
                }

                var otherRect = GetRect(other, positions[other], margin: 1);
                if (rect.IntersectsWith(otherRect))
                {
                    nodeCollisions++;
                }
            }

            var anchor = anchorPositions[candidate];
            var drift = Math.Abs(candidatePosition.Y - anchor.Y);
            return (overlaps * 10_000) + (nodeCollisions * 2_000) + (int)Math.Round(drift * 12, MidpointRounding.AwayFromZero);
        }

        var offsets = new[] { 0d, -12d, 12d, -24d, 24d, -36d, 36d, -48d, 48d, -64d, 64d };
        for (var pass = 0; pass < 4; pass++)
        {
            var changed = false;
            foreach (var node in textNodes.OrderBy(v => positions[v].Y))
            {
                if (lockedTextNodes.Contains(node))
                {
                    continue;
                }

                var anchor = anchorPositions[node];
                var current = positions[node];
                var best = current;
                var bestScore = ScoreNode(node, current);
                foreach (var offset in offsets)
                {
                    var candidate = new Point(anchor.X, anchor.Y + offset);
                    positions[node] = candidate;
                    var score = ScoreNode(node, candidate);
                    if (score < bestScore)
                    {
                        best = candidate;
                        bestScore = score;
                    }
                }

                positions[node] = best;
                if (Math.Abs(best.Y - current.Y) > 0.1)
                {
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }
    }

    private static void NormalizePlcReferenceOrdering(
        IReadOnlyList<LogicVertex> vertices,
        Dictionary<LogicVertex, Point> positions)
    {
        static void Reorder(
            IReadOnlyList<LogicVertex> columnVertices,
            Dictionary<LogicVertex, Point> positions)
        {
            if (columnVertices.Count < 2)
            {
                return;
            }

            var ySlots = columnVertices
                .Select(vertex => positions.TryGetValue(vertex, out var point) ? point.Y : vertex.Position.Y)
                .OrderBy(y => y)
                .ToList();
            if (ySlots.Count < 2)
            {
                return;
            }

            var orderedByRegister = columnVertices
                .Select(vertex =>
                {
                    var hasParts = TryParsePlcRegisterSortParts(
                        vertex.Title,
                        out var filePrefix,
                        out var fileNumber,
                        out var word,
                        out var bit);
                    return new
                    {
                        Vertex = vertex,
                        FilePrefix = hasParts ? filePrefix : string.Empty,
                        FileNumber = hasParts ? fileNumber : int.MaxValue,
                        Word = hasParts ? word : int.MaxValue,
                        Bit = hasParts ? bit : int.MaxValue
                    };
                })
                .OrderBy(x => x.FilePrefix, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.FileNumber)
                .ThenBy(x => x.Word)
                .ThenBy(x => x.Bit)
                .ThenBy(x => x.Vertex.Title, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Vertex)
                .ToList();

            for (var i = 0; i < orderedByRegister.Count && i < ySlots.Count; i++)
            {
                var vertex = orderedByRegister[i];
                var point = positions.TryGetValue(vertex, out var current) ? current : vertex.Position;
                positions[vertex] = new Point(point.X, ySlots[i]);
            }
        }

        var leftPlcNodes = vertices
            .Where(vertex => vertex.Kind.Equals("PLC_REF_LEFT", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var rightPlcNodes = vertices
            .Where(vertex => vertex.Kind.Equals("PLC_REF_RIGHT", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var legacyPlcNodes = vertices
            .Where(vertex => vertex.Kind.Equals("PLC_REF", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Reorder(leftPlcNodes, positions);
        Reorder(rightPlcNodes, positions);
        Reorder(legacyPlcNodes, positions);
    }

    private static void OptimizeGatePortOrderingForCrossings(
        IReadOnlyList<LogicVertex> vertices,
        IReadOnlyList<LogicEdge> edges,
        IReadOnlyDictionary<LogicVertex, Point> positions)
    {
        static bool PreserveDeclaredComparatorOrder(LogicVertex vertex)
        {
            if (!vertex.Kind.Equals("GATE_COMPARE", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var algorithm = (vertex.Algorithm ?? string.Empty).Trim();
            return algorithm.Equals("GT", StringComparison.OrdinalIgnoreCase)
                   || algorithm.Equals("GE", StringComparison.OrdinalIgnoreCase)
                   || algorithm.Equals("LT", StringComparison.OrdinalIgnoreCase)
                   || algorithm.Equals("LE", StringComparison.OrdinalIgnoreCase)
                   || algorithm.Equals("EQ", StringComparison.OrdinalIgnoreCase)
                   || algorithm.Equals("NE", StringComparison.OrdinalIgnoreCase)
                   || algorithm.Equals("NEQ", StringComparison.OrdinalIgnoreCase);
        }

        if (edges.Count < 2)
        {
            return;
        }

        var gateTargets = vertices
            .Where(vertex => vertex.Kind.StartsWith("GATE", StringComparison.OrdinalIgnoreCase))
            .ToHashSet();

        for (var pass = 0; pass < 4; pass++)
        {
            var changed = false;

            foreach (var group in edges
                         .Where(edge => gateTargets.Contains(edge.Target))
                         .GroupBy(edge => edge.Target))
            {
                if (PreserveDeclaredComparatorOrder(group.Key))
                {
                    continue;
                }

                var ordered = group
                    .OrderBy(edge => edge.TargetSlot)
                    .ThenBy(edge => edge.Source.Key, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (ordered.Count < 2)
                {
                    continue;
                }

                for (var i = 0; i < ordered.Count - 1; i++)
                {
                    var left = ordered[i];
                    var right = ordered[i + 1];
                    var leftSlot = left.TargetSlot;
                    var rightSlot = right.TargetSlot;

                    var baseline = ComputeLayoutPenalty(vertices, edges, positions);
                    left.TargetSlot = rightSlot;
                    right.TargetSlot = leftSlot;
                    ReassignSourceSlotsByTargetY(edges, positions);
                    var candidate = ComputeLayoutPenalty(vertices, edges, positions);
                    if (candidate < baseline)
                    {
                        changed = true;
                        (ordered[i], ordered[i + 1]) = (ordered[i + 1], ordered[i]);
                    }
                    else
                    {
                        left.TargetSlot = leftSlot;
                        right.TargetSlot = rightSlot;
                        ReassignSourceSlotsByTargetY(edges, positions);
                    }
                }
            }

            foreach (var group in edges
                         .Where(edge => edge.Source.Kind.StartsWith("GATE", StringComparison.OrdinalIgnoreCase))
                         .GroupBy(edge => edge.Source))
            {
                var ordered = group
                    .OrderBy(edge => edge.SourceSlot)
                    .ThenBy(edge => edge.Target.Key, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (ordered.Count < 2)
                {
                    continue;
                }

                for (var i = 0; i < ordered.Count - 1; i++)
                {
                    var left = ordered[i];
                    var right = ordered[i + 1];
                    var leftSlot = left.SourceSlot;
                    var rightSlot = right.SourceSlot;

                    var baseline = ComputeLayoutPenalty(vertices, edges, positions);
                    left.SourceSlot = rightSlot;
                    right.SourceSlot = leftSlot;
                    ReassignTargetSlotsBySourceY(edges, positions);
                    var candidate = ComputeLayoutPenalty(vertices, edges, positions);
                    if (candidate < baseline)
                    {
                        changed = true;
                        (ordered[i], ordered[i + 1]) = (ordered[i + 1], ordered[i]);
                    }
                    else
                    {
                        left.SourceSlot = leftSlot;
                        right.SourceSlot = rightSlot;
                        ReassignTargetSlotsBySourceY(edges, positions);
                    }
                }
            }

            if (!changed)
            {
                break;
            }
        }
    }

    private static void FitDiagramToScreen(NodifyEditor editor, double minViewportZoom)
    {
        void ApplyFitAndClamp()
        {
            if (editor.ActualWidth < 40 || editor.ActualHeight < 40)
            {
                return;
            }

            editor.FitToScreen(null);
            if (minViewportZoom > 0 && editor.ViewportZoom < minViewportZoom)
            {
                var currentZoom = Math.Max(editor.ViewportZoom, 0.0001);
                var centerX = editor.ViewportLocation.X + (editor.ActualWidth / (2.0 * currentZoom));
                var centerY = editor.ViewportLocation.Y + (editor.ActualHeight / (2.0 * currentZoom));
                editor.ViewportZoom = minViewportZoom;
                editor.ViewportLocation = new Point(
                    centerX - (editor.ActualWidth / (2.0 * minViewportZoom)),
                    centerY - (editor.ActualHeight / (2.0 * minViewportZoom)));
            }
        }

        void ScheduleDeferredFit(int retriesRemaining)
        {
            editor.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (editor.ActualWidth < 40 || editor.ActualHeight < 40)
                {
                    if (retriesRemaining > 0)
                    {
                        ScheduleDeferredFit(retriesRemaining - 1);
                    }

                    return;
                }

                ApplyFitAndClamp();
            }), DispatcherPriority.ContextIdle);
        }

        // Fit once immediately and then defer a few retries so first-open renders
        // still fit correctly when the chart tab/editor is not fully measured yet.
        ApplyFitAndClamp();
        ScheduleDeferredFit(4);
    }

    private void ConfigureLogicGateFollowerBindings()
    {
        ResetLogicGateFollowerBindings();

        var sourceUsageCount = _logicDiagramEdges
            .GroupBy(edge => edge.Source)
            .ToDictionary(group => group.Key, group => group.Count());

        foreach (var grouping in _logicDiagramEdges
                     .Where(edge =>
                         edge.Source.Kind is "INPUT" or "NUMERIC" or "REF"
                         && edge.Target.Kind is not ("INPUT" or "NUMERIC" or "REF")
                         && sourceUsageCount.TryGetValue(edge.Source, out var count)
                         && count == 1)
                     .GroupBy(edge => edge.Target))
        {
            var followerEdges = grouping
                .ToList();
            if (followerEdges.Count == 0)
            {
                continue;
            }

            var target = grouping.Key;
            _logicFollowerEdgesByTarget[target] = followerEdges;
            _logicFollowerLastGatePosition[target] = target.Position;
            _logicFollowerTrackedGates.Add(target);
            target.PositionChanged += LogicGateFollower_PositionChanged;
        }
    }

    private void ResetLogicGateFollowerBindings()
    {
        foreach (var gate in _logicFollowerTrackedGates)
        {
            gate.PositionChanged -= LogicGateFollower_PositionChanged;
        }

        _logicFollowerTrackedGates.Clear();
        _logicFollowerEdgesByTarget.Clear();
        _logicFollowerLastGatePosition.Clear();
        _logicFollowerSyncInProgress = false;
    }

    private void LogicGateFollower_PositionChanged(object? sender, EventArgs e)
    {
        if (_logicFollowerSyncInProgress || sender is not LogicVertex gate)
        {
            return;
        }

        if (!_logicFollowerEdgesByTarget.TryGetValue(gate, out var followerEdges) || followerEdges.Count == 0)
        {
            return;
        }

        var current = gate.Position;
        if (!_logicFollowerLastGatePosition.TryGetValue(gate, out var previous))
        {
            _logicFollowerLastGatePosition[gate] = current;
            return;
        }

        _logicFollowerLastGatePosition[gate] = current;
        var deltaX = current.X - previous.X;
        var deltaY = current.Y - previous.Y;
        if (Math.Abs(deltaX) < 0.01 && Math.Abs(deltaY) < 0.01)
        {
            return;
        }

        _logicFollowerSyncInProgress = true;
        try
        {
            foreach (var edge in followerEdges)
            {
                var source = edge.Source;
                var target = edge.Target;
                var sourceWidth = Math.Max(1d, source.Width);
                var sourceHeight = Math.Max(1d, source.Height);
                var targetHeight = Math.Max(1d, target.Height);
                var sourceMargin = ComputeAdaptiveSlotMargin(edge.SourceSlotCount, baseMargin: 4);
                var targetMargin = ComputeTargetSlotMargin(edge.Target, edge.TargetSlotCount);
                var sourceAnchorOffsetY = ComputeSlotY(
                    sourceHeight,
                    edge.SourceSlot,
                    edge.SourceSlotCount,
                    margin: sourceMargin);
                var targetAnchorY = target.Position.Y + ComputeSlotY(
                    targetHeight,
                    edge.TargetSlot,
                    edge.TargetSlotCount,
                    margin: targetMargin);

                var alignedX = target.Position.X - sourceWidth - 10d;
                var alignedY = targetAnchorY - sourceAnchorOffsetY;
                source.Position = new Point(alignedX, alignedY);
            }
        }
        finally
        {
            _logicFollowerSyncInProgress = false;
        }
    }

    private static void OptimizeGateColumnOrderForCrossings(
        IReadOnlyList<LogicVertex> vertices,
        IReadOnlyList<LogicEdge> edges,
        Dictionary<LogicVertex, Point> positions)
    {
        var gates = vertices
            .Where(vertex => vertex.Kind.StartsWith("GATE", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (gates.Count < 3)
        {
            return;
        }

        const double columnTolerance = 120d;
        var sortedByX = gates.OrderBy(vertex => positions[vertex].X).ToList();
        var columns = new List<(double MeanX, List<LogicVertex> Nodes)>();

        foreach (var gate in sortedByX)
        {
            var x = positions[gate].X;
            if (columns.Count == 0)
            {
                columns.Add((x, [gate]));
                continue;
            }

            var last = columns[^1];
            if (Math.Abs(last.MeanX - x) <= columnTolerance)
            {
                last.Nodes.Add(gate);
                var updatedMean = last.Nodes.Average(node => positions[node].X);
                columns[^1] = (updatedMean, last.Nodes);
                continue;
            }

            columns.Add((x, [gate]));
        }

        ReassignTargetSlotsBySourceY(edges, positions);
        ReassignSourceSlotsByTargetY(edges, positions);
        var bestScore = ComputeLayoutPenalty(vertices, edges, positions);

        for (var pass = 0; pass < 10; pass++)
        {
            var changed = false;
            foreach (var (_, nodes) in columns)
            {
                if (nodes.Count < 2)
                {
                    continue;
                }

                nodes.Sort((left, right) => positions[left].Y.CompareTo(positions[right].Y));

                for (var i = 0; i < nodes.Count - 1; i++)
                {
                    var upper = nodes[i];
                    var lower = nodes[i + 1];

                    var upperPosition = positions[upper];
                    var lowerPosition = positions[lower];
                    if (Math.Abs(upperPosition.Y - lowerPosition.Y) < 0.001)
                    {
                        continue;
                    }

                    positions[upper] = new Point(upperPosition.X, lowerPosition.Y);
                    positions[lower] = new Point(lowerPosition.X, upperPosition.Y);

                    ReassignTargetSlotsBySourceY(edges, positions);
                    ReassignSourceSlotsByTargetY(edges, positions);
                    var candidateScore = ComputeLayoutPenalty(vertices, edges, positions);
                    if (candidateScore < bestScore)
                    {
                        bestScore = candidateScore;
                        changed = true;
                        (nodes[i], nodes[i + 1]) = (nodes[i + 1], nodes[i]);
                        continue;
                    }

                    positions[upper] = upperPosition;
                    positions[lower] = lowerPosition;
                    ReassignTargetSlotsBySourceY(edges, positions);
                    ReassignSourceSlotsByTargetY(edges, positions);
                }
            }

            if (!changed)
            {
                break;
            }
        }

        const double minimumGateGap = 112d;
        const double nudgeStep = 42d;
        for (var pass = 0; pass < 6; pass++)
        {
            var changed = false;

            foreach (var (_, nodes) in columns)
            {
                if (nodes.Count == 0)
                {
                    continue;
                }

                nodes.Sort((left, right) => positions[left].Y.CompareTo(positions[right].Y));

                for (var i = 0; i < nodes.Count; i++)
                {
                    var gate = nodes[i];
                    var currentPosition = positions[gate];
                    var currentScore = ComputeLayoutPenalty(vertices, edges, positions);

                    var minY = i > 0
                        ? positions[nodes[i - 1]].Y + minimumGateGap
                        : currentPosition.Y - (2 * nudgeStep);
                    var maxY = i < nodes.Count - 1
                        ? positions[nodes[i + 1]].Y - minimumGateGap
                        : currentPosition.Y + (8 * nudgeStep);
                    if (maxY <= minY + 1)
                    {
                        continue;
                    }

                    var bestY = currentPosition.Y;
                    var bestLocalScore = currentScore;

                    var offsets = new[] { 1, 2, 3, 4, 5, 6, -1, -2 };
                    foreach (var offset in offsets)
                    {
                        var candidateY = currentPosition.Y + (offset * nudgeStep);
                        if (candidateY < minY || candidateY > maxY)
                        {
                            continue;
                        }

                        positions[gate] = new Point(currentPosition.X, candidateY);
                        ReassignTargetSlotsBySourceY(edges, positions);
                        ReassignSourceSlotsByTargetY(edges, positions);

                        var candidateScore = ComputeLayoutPenalty(vertices, edges, positions)
                                             + (long)(Math.Abs(offset) * 8);
                        if (candidateScore < bestLocalScore)
                        {
                            bestLocalScore = candidateScore;
                            bestY = candidateY;
                        }
                    }

                    positions[gate] = new Point(currentPosition.X, bestY);
                    ReassignTargetSlotsBySourceY(edges, positions);
                    ReassignSourceSlotsByTargetY(edges, positions);

                    if (Math.Abs(bestY - currentPosition.Y) > 0.01)
                    {
                        changed = true;
                    }
                }
            }

            if (!changed)
            {
                break;
            }
        }
    }

    private static void ReassignSourceSlotsByTargetY(
        IReadOnlyList<LogicEdge> edges,
        IReadOnlyDictionary<LogicVertex, Point> positions)
    {
        foreach (var sourceGroup in edges
                     .GroupBy(edge => edge.Source)
                     .OrderBy(group => group.Key.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (sourceGroup.Key.Kind.StartsWith("GATE_NOT", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var edge in sourceGroup)
                {
                    edge.SourceSlot = -1;
                    edge.SourceSlotCount = 0;
                }

                continue;
            }

            var ordered = sourceGroup
                .OrderBy(edge => GetTargetAnchorY(edge, positions))
                .ThenBy(edge => edge.Target.Key, StringComparer.OrdinalIgnoreCase)
                .ThenBy(edge => edge.Text, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (ordered.Count == 1
                && sourceGroup.Key.Kind is "INPUT" or "NUMERIC" or "REF")
            {
                // Preserve horizontal alignment for single-edge text sources by
                // mirroring the assigned target slot geometry.
                ordered[0].SourceSlot = ordered[0].TargetSlot;
                ordered[0].SourceSlotCount = ordered[0].TargetSlotCount;
                continue;
            }

            var slotCount = ordered.Count;
            for (var i = 0; i < ordered.Count; i++)
            {
                ordered[i].SourceSlot = i;
                ordered[i].SourceSlotCount = slotCount;
            }
        }
    }

    private static void ReassignTargetSlotsBySourceY(
        IReadOnlyList<LogicEdge> edges,
        IReadOnlyDictionary<LogicVertex, Point> positions)
    {
        static bool PreserveDeclaredComparatorOrder(LogicVertex target)
        {
            if (!target.Kind.Equals("GATE_COMPARE", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var algorithm = (target.Algorithm ?? string.Empty).Trim();
            return algorithm.Equals("GT", StringComparison.OrdinalIgnoreCase)
                   || algorithm.Equals("GE", StringComparison.OrdinalIgnoreCase)
                   || algorithm.Equals("LT", StringComparison.OrdinalIgnoreCase)
                   || algorithm.Equals("LE", StringComparison.OrdinalIgnoreCase)
                   || algorithm.Equals("EQ", StringComparison.OrdinalIgnoreCase)
                   || algorithm.Equals("NE", StringComparison.OrdinalIgnoreCase)
                   || algorithm.Equals("NEQ", StringComparison.OrdinalIgnoreCase);
        }

        foreach (var targetGroup in edges
                     .GroupBy(edge => edge.Target)
                     .OrderBy(group => group.Key.Key, StringComparer.OrdinalIgnoreCase))
        {
            var target = targetGroup.Key;
            var supportsTargetSlots = target.Kind.StartsWith("GATE", StringComparison.OrdinalIgnoreCase)
                                      || target.Kind.Equals("CHART", StringComparison.OrdinalIgnoreCase);
            if (!supportsTargetSlots)
            {
                continue;
            }

            if (target.Kind.StartsWith("GATE_NOT", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var edge in targetGroup)
                {
                    edge.TargetSlot = -1;
                    edge.TargetSlotCount = 0;
                }

                continue;
            }

            if (PreserveDeclaredComparatorOrder(target))
            {
                continue;
            }

            var ordered = targetGroup
                .OrderBy(edge => GetSourceAnchorY(edge, positions))
                .ThenBy(edge => edge.Source.Key, StringComparer.OrdinalIgnoreCase)
                .ThenBy(edge => edge.Text, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var slotCount = ordered.Count;
            for (var i = 0; i < ordered.Count; i++)
            {
                ordered[i].TargetSlot = i;
                ordered[i].TargetSlotCount = slotCount;
            }

            if (target.Kind.Equals("CHART", StringComparison.OrdinalIgnoreCase)
                && target.IsArrayMappingChart)
            {
                // Array chart titles render as: header line + FL lines.
                // Place PLC targets at FL-line centers (skip header row).
                var plcEdges = ordered
                    .Where(edge => IsPlcReferenceKind(edge.Source.Kind))
                    .ToList();
                if (plcEdges.Count > 0)
                {
                    var plcSlotCount = (plcEdges.Count * 2) + 1;
                    for (var i = 0; i < plcEdges.Count; i++)
                    {
                        plcEdges[i].TargetSlot = (i * 2) + 2;
                        plcEdges[i].TargetSlotCount = plcSlotCount;
                    }
                }
            }
        }
    }

    private static int CountEdgeCrossings(
        IReadOnlyList<LogicEdge> edges,
        IReadOnlyDictionary<LogicVertex, Point> positions)
    {
        if (edges.Count < 2)
        {
            return 0;
        }

        var segments = edges
            .Select(edge => CreateSegment(edge, positions))
            .ToList();

        var crossings = 0;
        for (var i = 0; i < segments.Count - 1; i++)
        {
            var a = segments[i];
            for (var j = i + 1; j < segments.Count; j++)
            {
                var b = segments[j];

                // Ignore sibling fan-out crossings from the same source;
                // source-slot ordering addresses these independently.
                if (ReferenceEquals(a.Edge.Source, b.Edge.Source))
                {
                    continue;
                }

                if (SegmentsProperlyCross(
                        a.Source.X,
                        a.Source.Y,
                        a.Target.X,
                        a.Target.Y,
                        b.Source.X,
                        b.Source.Y,
                        b.Target.X,
                        b.Target.Y))
                {
                    crossings++;
                }
            }
        }

        return crossings;
    }

    private static int CountGateLineOverlaps(
        IReadOnlyList<LogicVertex> vertices,
        IReadOnlyList<LogicEdge> edges,
        IReadOnlyDictionary<LogicVertex, Point> positions)
    {
        if (vertices.Count == 0 || edges.Count == 0)
        {
            return 0;
        }

        var segments = edges
            .Select(edge => CreateSegment(edge, positions))
            .ToList();

        var overlaps = 0;
        foreach (var gate in vertices.Where(vertex => vertex.Kind.StartsWith("GATE", StringComparison.OrdinalIgnoreCase)))
        {
            if (!positions.TryGetValue(gate, out var gatePosition))
            {
                gatePosition = gate.Position;
            }

            var left = gatePosition.X + 6;
            var top = gatePosition.Y + 6;
            var right = gatePosition.X + Math.Max(1, gate.Width) - 6;
            var bottom = gatePosition.Y + Math.Max(1, gate.Height) - 6;

            foreach (var segment in segments)
            {
                if (ReferenceEquals(segment.Edge.Source, gate) || ReferenceEquals(segment.Edge.Target, gate))
                {
                    continue;
                }

                if (SegmentCrossesRect(
                        segment.Source.X,
                        segment.Source.Y,
                        segment.Target.X,
                        segment.Target.Y,
                        left,
                        top,
                        right,
                        bottom))
                {
                    overlaps++;
                }
            }
        }

        return overlaps;
    }

    private static long ComputeLayoutPenalty(
        IReadOnlyList<LogicVertex> vertices,
        IReadOnlyList<LogicEdge> edges,
        IReadOnlyDictionary<LogicVertex, Point> positions)
    {
        var crossings = CountEdgeCrossings(edges, positions);
        var gateLineOverlaps = CountGateLineOverlaps(vertices, edges, positions);
        var labelLineOverlaps = CountLabelLineOverlaps(vertices, edges, positions);
        var gateOutputDistance = ComputeGateOutputDistancePenalty(edges, positions);

        // Overlaps through gate bodies are much harder to read than simple crossings.
        return (gateLineOverlaps * 1_000_000L)
             + (labelLineOverlaps * 250_000L)
             + (crossings * 2_000L)
             + gateOutputDistance;
    }

    private static long ComputeGateOutputDistancePenalty(
        IReadOnlyList<LogicEdge> edges,
        IReadOnlyDictionary<LogicVertex, Point> positions)
    {
        if (edges.Count == 0)
        {
            return 0;
        }

        const double deadbandPx = 18d;
        const long weightPerPixel = 10L;
        long penalty = 0;

        foreach (var edge in edges)
        {
            if (!edge.Source.Kind.StartsWith("GATE", StringComparison.OrdinalIgnoreCase)
                || !edge.Target.Kind.Equals("OUTPUT", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var segment = CreateSegment(edge, positions);
            var verticalDelta = Math.Abs(segment.Target.Y - segment.Source.Y);
            if (verticalDelta <= deadbandPx)
            {
                continue;
            }

            penalty += (long)Math.Round((verticalDelta - deadbandPx) * weightPerPixel, MidpointRounding.AwayFromZero);
        }

        return penalty;
    }

    private static (LogicEdge Edge, Point Source, Point Target) CreateSegment(
        LogicEdge edge,
        IReadOnlyDictionary<LogicVertex, Point> positions)
    {
        if (!positions.TryGetValue(edge.Source, out var sourcePosition))
        {
            sourcePosition = edge.Source.Position;
        }

        if (!positions.TryGetValue(edge.Target, out var targetPosition))
        {
            targetPosition = edge.Target.Position;
        }

        var sourceWidth = Math.Max(1, edge.Source.Width);
        var sourceHeight = Math.Max(1, edge.Source.Height);
        var targetHeight = Math.Max(1, edge.Target.Height);

        var sourceMargin = ComputeAdaptiveSlotMargin(edge.SourceSlotCount, baseMargin: 4);
        var targetMargin = ComputeTargetSlotMargin(edge.Target, edge.TargetSlotCount);
        var sourceY = edge.Source.Kind.StartsWith("GATE_NOT", StringComparison.OrdinalIgnoreCase)
            ? sourceHeight / 2d
            : ComputeSlotY(sourceHeight, edge.SourceSlot, edge.SourceSlotCount, margin: sourceMargin);
        var targetY = edge.Target.Kind.StartsWith("GATE_NOT", StringComparison.OrdinalIgnoreCase)
            ? targetHeight / 2d
            : ComputeSlotY(targetHeight, edge.TargetSlot, edge.TargetSlotCount, margin: targetMargin);

        var sourcePoint = new Point(
            sourcePosition.X + Math.Max(0, sourceWidth - 2),
            sourcePosition.Y + sourceY);

        var targetX = edge.ForceLeftTargetInput
                      || edge.Target.Kind.StartsWith("GATE", StringComparison.OrdinalIgnoreCase)
                      || edge.TargetSlotCount > 1
            ? targetPosition.X + 8
            : targetPosition.X + 8;

        var targetPoint = new Point(targetX, targetPosition.Y + targetY);
        return (edge, sourcePoint, targetPoint);
    }

    private static bool SegmentCrossesRect(
        double x1,
        double y1,
        double x2,
        double y2,
        double left,
        double top,
        double right,
        double bottom)
    {
        if (left >= right || top >= bottom)
        {
            return false;
        }

        var minX = Math.Min(x1, x2);
        var maxX = Math.Max(x1, x2);
        if (maxX < left || minX > right)
        {
            return false;
        }

        if (Math.Abs(x2 - x1) < 0.0001)
        {
            var x = x1;
            if (x < left || x > right)
            {
                return false;
            }

            var minY = Math.Min(y1, y2);
            var maxY = Math.Max(y1, y2);
            return !(maxY < top || minY > bottom);
        }

        var startX = Math.Max(minX, left);
        var endX = Math.Min(maxX, right);
        var t1 = (startX - x1) / (x2 - x1);
        var t2 = (endX - x1) / (x2 - x1);
        var yy1 = y1 + ((y2 - y1) * t1);
        var yy2 = y1 + ((y2 - y1) * t2);
        var minYInBand = Math.Min(yy1, yy2);
        var maxYInBand = Math.Max(yy1, yy2);
        return !(maxYInBand < top || minYInBand > bottom);
    }

    private static int CountLabelLineOverlaps(
        IReadOnlyList<LogicVertex> vertices,
        IReadOnlyList<LogicEdge> edges,
        IReadOnlyDictionary<LogicVertex, Point> positions)
    {
        if (vertices.Count == 0 || edges.Count == 0)
        {
            return 0;
        }

        var segments = edges
            .Select(edge => CreateSegment(edge, positions))
            .ToList();

        var overlaps = 0;
        foreach (var vertex in vertices)
        {
            if (!TryGetLabelRect(vertex, positions, out var left, out var top, out var right, out var bottom))
            {
                continue;
            }

            foreach (var segment in segments)
            {
                if (ReferenceEquals(segment.Edge.Source, vertex) || ReferenceEquals(segment.Edge.Target, vertex))
                {
                    continue;
                }

                if (SegmentCrossesRect(
                        segment.Source.X,
                        segment.Source.Y,
                        segment.Target.X,
                        segment.Target.Y,
                        left,
                        top,
                        right,
                        bottom))
                {
                    overlaps++;
                }
            }
        }

        return overlaps;
    }

    private static bool TryGetLabelRect(
        LogicVertex vertex,
        IReadOnlyDictionary<LogicVertex, Point> positions,
        out double left,
        out double top,
        out double right,
        out double bottom)
    {
        if (!positions.TryGetValue(vertex, out var vertexPosition))
        {
            vertexPosition = vertex.Position;
        }

        var width = Math.Max(1, vertex.Width);
        var height = Math.Max(1, vertex.Height);
        if (width < 10 || height < 10)
        {
            left = top = right = bottom = 0;
            return false;
        }

        if (vertex.Kind.StartsWith("GATE", StringComparison.OrdinalIgnoreCase))
        {
            left = vertexPosition.X + (width * 0.34);
            right = vertexPosition.X + (width * 0.92);
            top = vertexPosition.Y + (height * 0.24);
            bottom = vertexPosition.Y + (height * 0.78);
        }
        else
        {
            left = vertexPosition.X + 10;
            right = vertexPosition.X + width - 10;
            top = vertexPosition.Y + 4;
            bottom = vertexPosition.Y + height - 4;
        }

        if (left >= right || top >= bottom)
        {
            left = top = right = bottom = 0;
            return false;
        }

        return true;
    }

    private static double GetTargetAnchorY(LogicEdge edge, IReadOnlyDictionary<LogicVertex, Point> positions)
    {
        var segment = CreateSegment(edge, positions);
        return segment.Target.Y;
    }

    private static double GetSourceAnchorY(LogicEdge edge, IReadOnlyDictionary<LogicVertex, Point> positions)
    {
        var segment = CreateSegment(edge, positions);
        return segment.Source.Y;
    }

    private static double ComputeSlotY(double height, int slot, int slotCount, double margin)
    {
        var clampedHeight = Math.Max(1, height);
        if (slotCount <= 1 || slot < 0)
        {
            return clampedHeight / 2d;
        }

        var usableHeight = Math.Max(1d, clampedHeight - (2d * margin));
        var normalized = slotCount <= 1 ? 0.5d : slot / Math.Max(1d, slotCount - 1d);
        return margin + (normalized * usableHeight);
    }

    private static double ComputeAdaptiveSlotMargin(int slotCount, double baseMargin)
    {
        if (slotCount <= 2)
        {
            return baseMargin;
        }

        var extra = Math.Min(10d, (slotCount - 2) * 1.25d);
        return baseMargin + extra;
    }

    private static double ComputeTargetSlotMargin(LogicVertex target, int targetSlotCount)
    {
        // Gates need wider visual pin spread so 3+ incoming lines don't stack.
        var baseMargin = target.Kind.StartsWith("GATE", StringComparison.OrdinalIgnoreCase)
            ? 4d
            : 8d;
        return ComputeAdaptiveSlotMargin(targetSlotCount, baseMargin);
    }

    private static bool SegmentsProperlyCross(
        double ax,
        double ay,
        double bx,
        double by,
        double cx,
        double cy,
        double dx,
        double dy)
    {
        const double epsilon = 0.001;

        static double Orientation(double px, double py, double qx, double qy, double rx, double ry)
        {
            return ((qx - px) * (ry - py)) - ((qy - py) * (rx - px));
        }

        static bool SamePoint(double px, double py, double qx, double qy, double tolerance)
        {
            return Math.Abs(px - qx) <= tolerance && Math.Abs(py - qy) <= tolerance;
        }

        if (SamePoint(ax, ay, cx, cy, epsilon)
            || SamePoint(ax, ay, dx, dy, epsilon)
            || SamePoint(bx, by, cx, cy, epsilon)
            || SamePoint(bx, by, dx, dy, epsilon))
        {
            return false;
        }

        var o1 = Orientation(ax, ay, bx, by, cx, cy);
        var o2 = Orientation(ax, ay, bx, by, dx, dy);
        var o3 = Orientation(cx, cy, dx, dy, ax, ay);
        var o4 = Orientation(cx, cy, dx, dy, bx, by);

        if (Math.Abs(o1) <= epsilon || Math.Abs(o2) <= epsilon || Math.Abs(o3) <= epsilon || Math.Abs(o4) <= epsilon)
        {
            return false;
        }

        return (o1 > 0) != (o2 > 0) && (o3 > 0) != (o4 > 0);
    }

    private static Dictionary<LogicVertex, Point>? TryComputeMsaglLayout(
        IReadOnlyList<LogicVertex> vertices,
        IReadOnlyList<LogicEdge> edges,
        IReadOnlyDictionary<LogicVertex, Point> seedPositions)
    {
        if (vertices.Count == 0)
        {
            return [];
        }

        try
        {
            var geometryGraph = new GeometryGraph();
            var msaglNodes = new Dictionary<LogicVertex, MsaglNode>();

            foreach (var vertex in vertices)
            {
                var boundary = CurveFactory.CreateRectangle(
                    width: Math.Max(24, vertex.Width),
                    height: Math.Max(16, vertex.Height),
                    center: new MsaglPoint(0, 0));
                var node = new MsaglNode(boundary, vertex.Key) { UserData = vertex };
                msaglNodes[vertex] = node;
                geometryGraph.Nodes.Add(node);
            }

            foreach (var edge in edges)
            {
                if (!msaglNodes.TryGetValue(edge.Source, out var sourceNode)
                    || !msaglNodes.TryGetValue(edge.Target, out var targetNode))
                {
                    continue;
                }

                geometryGraph.Edges.Add(new MsaglEdge(sourceNode, targetNode) { UserData = edge });
            }

            var settings = new SugiyamaLayoutSettings
            {
                LayerSeparation = 100,
                Transformation = PlaneTransformation.Rotation(Math.PI / 2d),
                RepetitionCoefficientForOrdering = 8,
                RandomSeedForOrdering = 1
            };

            var nodesByColumn = BuildColumnBuckets(vertices, seedPositions);
            foreach (var column in nodesByColumn)
            {
                if (column.Count > 1)
                {
                    settings.PinNodesToSameLayer(column.Select(v => msaglNodes[v]).ToArray());
                }
            }

            for (var i = 0; i < nodesByColumn.Count - 1; i++)
            {
                var leftColumn = nodesByColumn[i];
                var rightColumn = nodesByColumn[i + 1];
                foreach (var left in leftColumn)
                {
                    foreach (var right in rightColumn)
                    {
                        settings.AddUpDownConstraint(msaglNodes[left], msaglNodes[right]);
                    }
                }
            }

            LayoutHelpers.CalculateLayout(geometryGraph, settings, null, null);

            var minLeft = double.PositiveInfinity;
            var minTop = double.PositiveInfinity;
            foreach (var vertex in vertices)
            {
                var node = msaglNodes[vertex];
                var left = node.Center.X - (node.Width / 2d);
                var top = node.Center.Y - (node.Height / 2d);
                minLeft = Math.Min(minLeft, left);
                minTop = Math.Min(minTop, top);
            }

            if (double.IsInfinity(minLeft) || double.IsInfinity(minTop))
            {
                return null;
            }

            var result = new Dictionary<LogicVertex, Point>();
            foreach (var vertex in vertices)
            {
                var node = msaglNodes[vertex];
                var x = (node.Center.X - (node.Width / 2d)) - minLeft + 40;
                var y = (node.Center.Y - (node.Height / 2d)) - minTop + 40;
                result[vertex] = new Point(x, y);
            }

            return result;
        }
        catch
        {
            return null;
        }
    }

    private static List<List<LogicVertex>> BuildColumnBuckets(
        IReadOnlyList<LogicVertex> vertices,
        IReadOnlyDictionary<LogicVertex, Point> seedPositions)
    {
        const double tolerance = 24d;
        var columns = new List<(double X, List<LogicVertex> Vertices)>();

        foreach (var vertex in vertices.OrderBy(v => seedPositions.GetValueOrDefault(v).X))
        {
            var x = seedPositions.GetValueOrDefault(vertex).X;
            var assigned = false;
            for (var i = 0; i < columns.Count; i++)
            {
                if (Math.Abs(columns[i].X - x) <= tolerance)
                {
                    columns[i].Vertices.Add(vertex);
                    columns[i] = ((columns[i].X + x) / 2d, columns[i].Vertices);
                    assigned = true;
                    break;
                }
            }

            if (!assigned)
            {
                columns.Add((x, [vertex]));
            }
        }

        return columns
            .OrderBy(x => x.X)
            .Select(x => x.Vertices)
            .ToList();
    }

    private void ChartDiagramEditor_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is NodifyEditor chartEditor
            && !TryGetChartVertexFromSource(e.OriginalSource as DependencyObject, out _))
        {
            BeginDiagramPan(chartEditor, e.GetPosition(this));
            e.Handled = true;
            return;
        }

        _chartMouseDownPoint = e.GetPosition(ChartDiagramEditor);
        _chartMouseDownVertexPosition = null;
        _chartMouseDownVertexKey = null;
        _chartPendingClProgramId = null;
        _chartPendingLogicEntityId = null;
        _chartPendingExpandUpstream = false;
        _chartPendingExpandDownstream = false;

        if (IsClickOnItemContainer(e.OriginalSource as DependencyObject))
        {
            BeginChartDragUndoCapture();
        }

        if (!TryGetChartVertexFromSource(e.OriginalSource as DependencyObject, out var vertex))
        {
            return;
        }

        if (TryParseClProgramIdFromChartVertex(vertex, out var clProgramId))
        {
            if (!IsTextClickSource(e.OriginalSource as DependencyObject))
            {
                // Allow perimeter click to act as drag, not navigation.
                return;
            }

            _chartMouseDownVertexPosition = vertex.Position;
            _chartMouseDownVertexKey = vertex.Key;
            _chartPendingClProgramId = clProgramId;
            e.Handled = true;
            return;
        }

        if (TryParseLogicEntityIdFromChartVertex(vertex, out var logicEntityId))
        {
            if (!IsTextClickSource(e.OriginalSource as DependencyObject))
            {
                // Allow perimeter click to act as drag, not navigation.
                return;
            }

            _chartMouseDownVertexPosition = vertex.Position;
            _chartMouseDownVertexKey = vertex.Key;
            _chartPendingLogicEntityId = logicEntityId;
            e.Handled = true;
            return;
        }

        if (!TryParseChartEntityIdFromChartVertex(vertex, out _))
        {
            return;
        }

        if (!TryResolveChartDirectionalExpansion(vertex, e.GetPosition(ChartDiagramEditor), out var expandUpstream, out var expandDownstream))
        {
            return;
        }

        _chartMouseDownVertexKey = vertex.Key;
        _chartPendingExpandUpstream = expandUpstream;
        _chartPendingExpandDownstream = expandDownstream;
        e.Handled = true;
    }

    private async void ChartDiagramEditor_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDiagramPanning && e.ChangedButton == MouseButton.Left)
        {
            EndDiagramPan();
            QueueChartDragUndoFinalize();
            e.Handled = true;
            return;
        }

        QueueChartDragUndoFinalize();

        var pendingProgramId = _chartPendingClProgramId;
        var pendingLogicEntityId = _chartPendingLogicEntityId;
        var pendingExpandUpstream = _chartPendingExpandUpstream;
        var pendingExpandDownstream = _chartPendingExpandDownstream;
        var pendingVertexKey = _chartMouseDownVertexKey;
        var pendingVertexPosition = _chartMouseDownVertexPosition;
        _chartPendingClProgramId = null;
        _chartPendingLogicEntityId = null;
        _chartPendingExpandUpstream = false;
        _chartPendingExpandDownstream = false;
        _chartMouseDownVertexPosition = null;
        _chartMouseDownVertexKey = null;

        var mouseUp = e.GetPosition(ChartDiagramEditor);
        if (_chartMouseDownPoint is Point mouseDown)
        {
            var movedX = Math.Abs(mouseUp.X - mouseDown.X);
            var movedY = Math.Abs(mouseUp.Y - mouseDown.Y);
            if (movedX > 5 || movedY > 5)
            {
                return;
            }
        }

        if ((pendingProgramId is not null || pendingLogicEntityId is not null)
            && pendingVertexPosition is Point downVertexPosition
            && !string.IsNullOrWhiteSpace(pendingVertexKey))
        {
            var trackedVertex = _chartDiagramNodes.FirstOrDefault(v =>
                v.Key.Equals(pendingVertexKey, StringComparison.OrdinalIgnoreCase));
            if (trackedVertex is not null)
            {
                var movedNodeX = Math.Abs(trackedVertex.Position.X - downVertexPosition.X);
                var movedNodeY = Math.Abs(trackedVertex.Position.Y - downVertexPosition.Y);
                if (movedNodeX > 0.5 || movedNodeY > 0.5)
                {
                    // Dragging a CL/LB node should not trigger navigation/open.
                    return;
                }
            }
        }

        LogicVertex selectedVertex;
        if (pendingProgramId is long pendingId)
        {
            if (TryGetChartVertexFromSource(e.OriginalSource as DependencyObject, out var vertex)
                && !string.IsNullOrWhiteSpace(pendingVertexKey)
                && !vertex.Key.Equals(pendingVertexKey, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            e.Handled = true;
            ResetDiagramInteractionState();
            await OpenClProgramAsync(pendingId, switchToClTab: true);
            return;
        }
        else if (pendingLogicEntityId is long pendingLogicId)
        {
            if (TryGetChartVertexFromSource(e.OriginalSource as DependencyObject, out var vertex)
                && !string.IsNullOrWhiteSpace(pendingVertexKey)
                && !vertex.Key.Equals(pendingVertexKey, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            e.Handled = true;
            ResetDiagramInteractionState();
            await OpenLogicBlockAsync(pendingLogicId, switchToLogicTab: true);
            return;
        }
        else if (!TryGetChartVertexFromSource(e.OriginalSource as DependencyObject, out selectedVertex))
        {
            return;
        }

        if (pendingExpandUpstream || pendingExpandDownstream)
        {
            if (!string.IsNullOrWhiteSpace(pendingVertexKey)
                && !selectedVertex.Key.Equals(pendingVertexKey, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            e.Handled = true;
            await ToggleChartDirectionalExpansionAsync(
                selectedVertex,
                pendingExpandUpstream,
                pendingExpandDownstream);
            return;
        }

        if (TryParseClProgramIdFromChartVertex(selectedVertex, out var resolvedClProgramId))
        {
            if (!IsTextClickSource(e.OriginalSource as DependencyObject))
            {
                return;
            }

            e.Handled = true;
            await OpenClProgramAsync(resolvedClProgramId, switchToClTab: true);
            return;
        }

        if (!TryParseLogicEntityIdFromChartVertex(selectedVertex, out var logicEntityId))
        {
            return;
        }

        if (!IsTextClickSource(e.OriginalSource as DependencyObject))
        {
            return;
        }

        e.Handled = true;
        await OpenLogicBlockAsync(logicEntityId, switchToLogicTab: true);
    }

    private void LogicDiagramEditor_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Clean up any stuck pan state (e.g. button released while app was unfocused).
        if (_isDiagramPanning)
        {
            EndDiagramPan();
        }

        if (sender is not NodifyEditor logicEditor)
        {
            return;
        }

        // If the click originated inside a Nodify ItemContainer, let Nodify handle drag/select.
        if (IsClickOnItemContainer(e.OriginalSource as DependencyObject))
        {
            BeginLogicDragUndoCapture();
            return;
        }

        BeginDiagramPan(logicEditor, e.GetPosition(this));
        e.Handled = true;
    }

    private static bool IsClickOnItemContainer(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ItemContainer)
            {
                return true;
            }

            source = source is Visual or Visual3D
                ? VisualTreeHelper.GetParent(source)
                : source is FrameworkContentElement fce
                    ? fce.Parent
                    : LogicalTreeHelper.GetParent(source);
        }

        return false;
    }

    private void LogicDiagramEditor_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDiagramPanning || e.ChangedButton != MouseButton.Left)
        {
            QueueLogicDragUndoFinalize();
            return;
        }

        EndDiagramPan();
        QueueLogicDragUndoFinalize();
        e.Handled = true;
    }

    private void DiagramEditor_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not NodifyEditor editor)
        {
            return;
        }

        BeginDiagramPan(editor, e.GetPosition(this));
        e.Handled = true;
    }

    private void DiagramEditor_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDiagramPanning
            && sender is NodifyEditor editor
            && ReferenceEquals(editor, ChartDiagramEditor))
        {
            UpdateChartHoverCursor(e);
        }

        if (!_isDiagramPanning || _diagramPanEditor is null)
        {
            return;
        }

        var current = e.GetPosition(this);
        var delta = current - _diagramPanLastPoint;
        _diagramPanLastPoint = current;

        _diagramPanEditor.ViewportLocation = new Point(
            _diagramPanEditor.ViewportLocation.X - delta.X,
            _diagramPanEditor.ViewportLocation.Y - delta.Y);
        e.Handled = true;
    }

    private void DiagramEditor_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndDiagramPan();
        e.Handled = true;
    }

    private void DiagramEditor_LostMouseCapture(object sender, MouseEventArgs e)
    {
        EndDiagramPan();
        QueueLogicDragUndoFinalize();
        QueueChartDragUndoFinalize();
    }

    private void ResetDiagramInteractionState()
    {
        EndDiagramPan();

        _chartMouseDownPoint = null;
        _chartMouseDownVertexPosition = null;
        _chartMouseDownVertexKey = null;
        _chartPendingClProgramId = null;
        _chartPendingLogicEntityId = null;
        _chartPendingExpandUpstream = false;
        _chartPendingExpandDownstream = false;
        _logicDragSnapshotBefore = null;
        _chartDragSnapshotBefore = null;
        _logicDragFinalizeQueued = false;
        _chartDragFinalizeQueued = false;

        if (ChartDiagramEditor.IsMouseCaptured)
        {
            ChartDiagramEditor.ReleaseMouseCapture();
        }

        if (LogicDiagramEditor.IsMouseCaptured)
        {
            LogicDiagramEditor.ReleaseMouseCapture();
        }

        ResetNodifyEditorInteraction(ChartDiagramEditor);
        ResetNodifyEditorInteraction(LogicDiagramEditor);
    }

    private static void ResetNodifyEditorInteraction(NodifyEditor editor)
    {
        if (editor.IsDragging)
        {
            editor.CancelDragging();
        }

        if (editor.IsPanning)
        {
            editor.CancelPanning();
        }

        if (editor.IsSelecting)
        {
            editor.CancelSelecting();
        }

        if (editor.IsPushingItems)
        {
            editor.CancelPushingItems();
        }

        if (editor.IsCutting)
        {
            editor.CancelCutting();
        }
    }

    private void EndDiagramPan()
    {
        _isDiagramPanning = false;
        if (_diagramPanEditor is not null && _diagramPanEditor.IsMouseCaptured)
        {
            _diagramPanEditor.ReleaseMouseCapture();
        }

        _diagramPanEditor = null;
        Mouse.OverrideCursor = null;
    }

    private void BeginChartDragUndoCapture()
    {
        _chartDragFinalizeQueued = false;

        if (_chartDiagramNodes.Count == 0)
        {
            _chartDragSnapshotBefore = null;
            return;
        }

        _chartDragSnapshotBefore = _chartDiagramNodes.ToDictionary(
            node => node,
            node => node.Position);
    }

    private void QueueChartDragUndoFinalize()
    {
        if (_chartDragSnapshotBefore is null || _chartDragFinalizeQueued)
        {
            return;
        }

        _chartDragFinalizeQueued = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() =>
            {
                _chartDragFinalizeQueued = false;
                EndChartDragUndoCapture();
            }));
    }

    private void EndChartDragUndoCapture()
    {
        if (_chartDragSnapshotBefore is null || _chartDragSnapshotBefore.Count == 0)
        {
            _chartDragFinalizeQueued = false;
            _chartDragSnapshotBefore = null;
            return;
        }

        var action = new DiagramDragUndoAction();
        foreach (var (vertex, before) in _chartDragSnapshotBefore)
        {
            var after = vertex.Position;
            if (Math.Abs(after.X - before.X) < 0.5 && Math.Abs(after.Y - before.Y) < 0.5)
            {
                continue;
            }

            action.Moves.Add((vertex, before, after));
        }

        _chartDragSnapshotBefore = null;
        _chartDragFinalizeQueued = false;
        if (action.Moves.Count == 0)
        {
            return;
        }

        _diagramDragUndoStack.Push(action);
        MarkProjectDirty();
    }

    private void BeginLogicDragUndoCapture()
    {
        _logicDragFinalizeQueued = false;

        if (_logicDiagramNodes.Count == 0)
        {
            _logicDragSnapshotBefore = null;
            return;
        }

        _logicDragSnapshotBefore = _logicDiagramNodes.ToDictionary(
            node => node,
            node => node.Position);
    }

    private void QueueLogicDragUndoFinalize()
    {
        if (_logicDragSnapshotBefore is null || _logicDragFinalizeQueued)
        {
            return;
        }

        _logicDragFinalizeQueued = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() =>
            {
                _logicDragFinalizeQueued = false;
                EndLogicDragUndoCapture();
            }));
    }

    private void EndLogicDragUndoCapture()
    {
        if (_logicDragSnapshotBefore is null || _logicDragSnapshotBefore.Count == 0)
        {
            _logicDragFinalizeQueued = false;
            _logicDragSnapshotBefore = null;
            return;
        }

        var action = new DiagramDragUndoAction();
        foreach (var (vertex, before) in _logicDragSnapshotBefore)
        {
            var after = vertex.Position;
            if (Math.Abs(after.X - before.X) < 0.5 && Math.Abs(after.Y - before.Y) < 0.5)
            {
                continue;
            }

            action.Moves.Add((vertex, before, after));
        }

        _logicDragSnapshotBefore = null;
        _logicDragFinalizeQueued = false;
        if (action.Moves.Count == 0)
        {
            return;
        }

        _diagramDragUndoStack.Push(action);
        MarkProjectDirty();
    }

    private bool TryUndoLastDiagramDrag()
    {
        if (_diagramDragUndoStack.Count == 0)
        {
            return false;
        }

        var action = _diagramDragUndoStack.Pop();
        if (action.Moves.Count == 0)
        {
            return false;
        }

        foreach (var move in action.Moves)
        {
            move.Vertex.Position = move.Before;
        }

        MarkProjectDirty();
        return true;
    }

    private void UpdateChartHoverCursor(MouseEventArgs e)
    {
        if (!TryGetChartVertexFromSource(e.OriginalSource as DependencyObject, out var vertex))
        {
            Mouse.OverrideCursor = null;
            return;
        }

        if (!TryParseChartEntityIdFromChartVertex(vertex, out _))
        {
            Mouse.OverrideCursor = null;
            return;
        }

        var mousePoint = e.GetPosition(ChartDiagramEditor);
        var isExpandable = TryResolveChartDirectionalExpansion(vertex, mousePoint, out _, out _);
        Mouse.OverrideCursor = isExpandable ? Cursors.Hand : null;
    }

    private async Task ToggleChartDirectionalExpansionAsync(
        LogicVertex vertex,
        bool toggleUpstream,
        bool toggleDownstream)
    {
        if (!TryParseChartEntityIdFromChartVertex(vertex, out _))
        {
            return;
        }

        var changed = false;

        if (toggleUpstream)
        {
            if (!_chartExpandedUpstreamKeys.Add(vertex.Key))
            {
                changed = _chartExpandedUpstreamKeys.Remove(vertex.Key) || changed;
            }
            else
            {
                changed = true;
            }
        }

        if (toggleDownstream)
        {
            if (!_chartExpandedDownstreamKeys.Add(vertex.Key))
            {
                changed = _chartExpandedDownstreamKeys.Remove(vertex.Key) || changed;
            }
            else
            {
                changed = true;
            }
        }

        if (changed)
        {
            await RefreshActiveChartAsync();
        }
    }

    private void BeginDiagramPan(NodifyEditor editor, Point startPoint)
    {
        _isDiagramPanning = true;
        _diagramPanEditor = editor;
        _diagramPanLastPoint = startPoint;
        editor.CaptureMouse();
        Mouse.OverrideCursor = Cursors.SizeAll;
    }

    private static bool TryGetChartVertexFromSource(DependencyObject? source, out LogicVertex vertex)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { DataContext: LogicVertex dataContextVertex })
            {
                vertex = dataContextVertex;
                return true;
            }

            source = GetParentObject(source);
        }

        vertex = null!;
        return false;
    }

    private static DependencyObject? GetParentObject(DependencyObject? source)
    {
        if (source is null)
        {
            return null;
        }

        if (source is Visual || source is Visual3D)
        {
            return VisualTreeHelper.GetParent(source);
        }

        if (source is FrameworkContentElement frameworkContentElement)
        {
            return frameworkContentElement.Parent;
        }

        return LogicalTreeHelper.GetParent(source);
    }

    private static bool TryParseClProgramIdFromChartVertex(LogicVertex vertex, out long clProgramId)
    {
        clProgramId = 0;
        if (string.IsNullOrWhiteSpace(vertex.Key))
        {
            return false;
        }

        var key = vertex.Key.Trim();
        if (!key.StartsWith("CL:", StringComparison.OrdinalIgnoreCase)
            && !key.StartsWith("CLR:", StringComparison.OrdinalIgnoreCase)
            && !key.StartsWith("CLW:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var separatorIndex = key.LastIndexOf(':');
        if (separatorIndex < 0 || separatorIndex == key.Length - 1)
        {
            return false;
        }

        return long.TryParse(key.AsSpan(separatorIndex + 1), out clProgramId);
    }

    private static bool IsTextClickSource(DependencyObject? source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is TextBlock)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseLogicEntityIdFromChartVertex(LogicVertex vertex, out long logicEntityId)
    {
        logicEntityId = 0;
        if (string.IsNullOrWhiteSpace(vertex.Key))
        {
            return false;
        }

        var key = vertex.Key.Trim();
        if (!key.StartsWith("LB:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var separatorIndex = key.LastIndexOf(':');
        if (separatorIndex < 0 || separatorIndex == key.Length - 1)
        {
            return false;
        }

        return long.TryParse(key.AsSpan(separatorIndex + 1), out logicEntityId);
    }

    private static bool TryParseChartEntityIdFromChartVertex(LogicVertex vertex, out long entityId)
    {
        entityId = 0;
        if (string.IsNullOrWhiteSpace(vertex.Key))
        {
            return false;
        }

        var key = vertex.Key.Trim();
        if (!key.StartsWith("ENTITY:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var separatorIndex = key.LastIndexOf(':');
        if (separatorIndex < 0 || separatorIndex == key.Length - 1)
        {
            return false;
        }

        return long.TryParse(key.AsSpan(separatorIndex + 1), out entityId);
    }

    private bool TryResolveChartDirectionalExpansion(
        LogicVertex vertex,
        Point clickPoint,
        out bool expandUpstream,
        out bool expandDownstream)
    {
        expandUpstream = false;
        expandDownstream = false;

        var canExpandUpstream = _chartExpandableUpstreamKeys.Contains(vertex.Key)
                                || _chartExpandedUpstreamKeys.Contains(vertex.Key);
        var canExpandDownstream = _chartExpandableDownstreamKeys.Contains(vertex.Key)
                                  || _chartExpandedDownstreamKeys.Contains(vertex.Key);
        if (!canExpandUpstream && !canExpandDownstream)
        {
            return false;
        }

        var zoom = ChartDiagramEditor.ViewportZoom;
        if (zoom <= 0.0001)
        {
            zoom = 1;
        }

        var diagramPoint = new Point(
            (clickPoint.X / zoom) + ChartDiagramEditor.ViewportLocation.X,
            (clickPoint.Y / zoom) + ChartDiagramEditor.ViewportLocation.Y);

        var width = Math.Max(80d, vertex.Width);
        var relativeX = diagramPoint.X - vertex.Position.X;
        if (relativeX < 0 || relativeX > width)
        {
            return false;
        }

        // Wide edge targets to make arrow clicks reliable and avoid accidental drags.
        var edgeClickWidth = Math.Max(44d, Math.Min(110d, width * 0.42d));
        var clickedLeftIndicator = relativeX >= 0 && relativeX <= edgeClickWidth;
        var clickedRightIndicator = relativeX >= (width - edgeClickWidth) && relativeX <= width;

        if (canExpandUpstream && clickedLeftIndicator)
        {
            expandUpstream = true;
            return true;
        }

        if (canExpandDownstream && clickedRightIndicator)
        {
            expandDownstream = true;
            return true;
        }

        return false;
    }

    private static string NormalizeChartEntityToken(string value)
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

        var dotIndex = normalized.IndexOf('.');
        if (dotIndex > 0)
        {
            normalized = normalized[..dotIndex];
        }

        return normalized.Trim();
    }

    private static bool TryParseEntityId(object? tag, out long entityId)
    {
        entityId = 0;
        if (tag is null)
        {
            return false;
        }

        return long.TryParse(tag.ToString(), out entityId);
    }

    private static string ExtractPrimaryAddressToken(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return string.Empty;
        }

        var separators = new[] { ',', ';', '\r', '\n', '\t' };
        var token = rawValue
            .Split(separators, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .FirstOrDefault(part => !string.IsNullOrWhiteSpace(part));

        return token ?? string.Empty;
    }

    private void ApplyHardwareAddressLinkAvailability(IReadOnlyList<HardwarePointSummary> points)
    {
        var hasAssignedAb = _activeHardwareBoxDetail is not null
                            && GetAssignedAbPlcSystemsForHardwareBox(_activeHardwareBoxDetail).Count > 0;
        var hasAssignedModicon = _activeHardwareBoxDetail is not null
                                 && GetAssignedModiconSystemsForHardwareBox(_activeHardwareBoxDetail).Count > 0;

        foreach (var point in points)
        {
            point.CanOpenAbInAddress = hasAssignedAb && !string.IsNullOrWhiteSpace(point.AbAddressIn);
            point.CanOpenAbOutAddress = hasAssignedAb && !string.IsNullOrWhiteSpace(point.AbAddressOut);
            point.CanOpenModbusInAddress = hasAssignedModicon && !string.IsNullOrWhiteSpace(point.ModbusAddressIn);
            point.CanOpenModbusOutAddress = hasAssignedModicon && !string.IsNullOrWhiteSpace(point.ModbusAddressOut);
        }
    }

    private static string BuildXpsFindTermForRegisterAddress(string registerAddress)
    {
        var token = ExtractPrimaryAddressToken(registerAddress);
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        // XPS ladder text often renders bit index on a separate run/line, so "N22:44/10"
        // is more reliably found as "N22:44".
        var slashIndex = token.IndexOf('/');
        if (slashIndex > 0)
        {
            return token[..slashIndex].Trim();
        }

        return token;
    }

    private static HardwareModuleSummary? FindModuleForHardwareLink(
        IReadOnlyList<HardwareModuleSummary> modules,
        int moduleNumber)
    {
        if (moduleNumber <= 0)
        {
            return null;
        }

        return modules.FirstOrDefault(x => x.ModuleIndex == moduleNumber)
               ?? modules.FirstOrDefault(x =>
                   x.CardA.GetValueOrDefault() == moduleNumber
                   || x.CardB.GetValueOrDefault() == moduleNumber);
    }

    private async Task SelectHardwarePointIfPresentAsync(long pointEntityId)
    {
        if (pointEntityId <= 0 || _activeHardwareBoxDetail is null)
        {
            return;
        }

        bool TrySelectFromCurrentList()
        {
            var match = _hardwarePoints.FirstOrDefault(x => x.EntityId == pointEntityId);
            if (match is null)
            {
                return false;
            }

            HardwarePointsGrid.SelectedItem = match;
            HardwarePointsGrid.ScrollIntoView(match);
            return true;
        }

        if (TrySelectFromCurrentList())
        {
            return;
        }

        foreach (var module in _activeHardwareBoxDetail.Modules)
        {
            await LoadHardwareModulePointsAsync(module);
            if (TrySelectFromCurrentList())
            {
                return;
            }
        }
    }

    private static string BuildChartExternalNodeKey(string entityName, bool upstream)
    {
        var normalized = NormalizeChartEntityToken(entityName);
        return $"EXT:{(upstream ? "UP" : "DOWN")}:{normalized.ToUpperInvariant()}";
    }

    private static string BuildPiExternalNodeKey(string piTagName, bool upstream)
    {
        var normalized = (piTagName ?? string.Empty).Trim();
        return $"PI:{(upstream ? "UP" : "DOWN")}:{normalized.ToUpperInvariant()}";
    }

    private static string BuildChartRootTitle(ChartDetail detail)
    {
        var baseTitle = string.IsNullOrWhiteSpace(detail.Name) ? "(unnamed)" : detail.Name.Trim();
        if (detail.EntityType.Equals("CUSTOMAM", StringComparison.OrdinalIgnoreCase)
            || detail.EntityType.Equals("REGAM", StringComparison.OrdinalIgnoreCase))
        {
            var pkg = detail.PackageNames
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();
            if (pkg.Count > 0)
            {
                var suffix = detail.PackageNames.Count > 3
                    ? $" (+{detail.PackageNames.Count - 3})"
                    : string.Empty;
                return $"{baseTitle}\nPKG: {string.Join(", ", pkg)}{suffix}";
            }
        }

        if (!detail.EntityType.Equals("ARRAY", StringComparison.OrdinalIgnoreCase))
        {
            return baseTitle;
        }

        var mappings = new List<(string Lane, int ElementIndex, string RegisterAddress)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plcReference in detail.PlcReferences)
        {
            if (!TryExtractSerialArrayElement(plcReference.SourceLine, out var lane, out var elementIndex))
            {
                continue;
            }

            var registerAddress = (plcReference.RegisterAddress ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(registerAddress))
            {
                continue;
            }

            var uniqueKey = $"{lane}|{elementIndex}|{registerAddress.ToUpperInvariant()}";
            if (!seen.Add(uniqueKey))
            {
                continue;
            }

            mappings.Add((lane, elementIndex, registerAddress));
        }

        if (mappings.Count == 0)
        {
            return baseTitle;
        }

        const int maxDisplayedMappings = 200;
        var ordered = mappings
            .Select(mapping =>
            {
                var hasParts = TryParsePlcRegisterSortParts(
                    mapping.RegisterAddress,
                    out var filePrefix,
                    out var fileNumber,
                    out var word,
                    out var bit);
                return new
                {
                    mapping.Lane,
                    mapping.ElementIndex,
                    mapping.RegisterAddress,
                    FilePrefix = hasParts ? filePrefix : string.Empty,
                    FileNumber = hasParts ? fileNumber : int.MaxValue,
                    Word = hasParts ? word : int.MaxValue,
                    Bit = hasParts ? bit : int.MaxValue
                };
            })
            .OrderBy(x => GetSerialArrayLaneSortOrder(x.Lane))
            .ThenBy(x => x.ElementIndex)
            .ThenBy(x => x.FilePrefix, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.FileNumber)
            .ThenBy(x => x.Word)
            .ThenBy(x => x.Bit)
            .ThenBy(x => x.RegisterAddress, StringComparer.OrdinalIgnoreCase)
            .Select(x => (x.Lane, x.ElementIndex, x.RegisterAddress))
            .ToList();
        var lines = ordered
            .Take(maxDisplayedMappings)
            .Select(x => $"{x.Lane}({x.ElementIndex})")
            .ToList();
        if (ordered.Count > maxDisplayedMappings)
        {
            lines.Add($"+{ordered.Count - maxDisplayedMappings} more mappings");
        }

        return $"{baseTitle}\n{string.Join("\n", lines)}";
    }

    // Remove spaces around '/' in PLC bit addresses (e.g. "N22:33/ 3" → "N22:33/3")
    private static string NormalizePlcAddress(string address)
    {
        var compact = System.Text.RegularExpressions.Regex.Replace(address ?? string.Empty, @"\s*/\s*", "/");
        return NormalizeSingleLineText(compact);
    }

    // Force rendered chart labels to stay on one visual line.
    private static string NormalizeSingleLineText(string value) =>
        System.Text.RegularExpressions.Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();

    private static string BuildPlcReferenceTitle(ChartPlcReference plcReference)
    {
        var plcName = NormalizeSingleLineText(plcReference.PlcName ?? string.Empty);
        var registerAddress = NormalizePlcAddress((plcReference.RegisterAddress ?? string.Empty).Trim());
        var interfaceType = string.IsNullOrWhiteSpace(plcReference.InterfaceType)
            ? string.Empty
            : NormalizeSingleLineText(plcReference.InterfaceType).ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(plcName))
        {
            return string.IsNullOrWhiteSpace(interfaceType)
                ? registerAddress
                : $"{interfaceType} {registerAddress}";
        }

        if (string.IsNullOrWhiteSpace(registerAddress))
        {
            return string.IsNullOrWhiteSpace(interfaceType)
                ? plcName
                : $"{interfaceType} {plcName}";
        }

        return string.IsNullOrWhiteSpace(interfaceType)
            ? $"{plcName} {registerAddress}"
            : $"{interfaceType} {plcName} {registerAddress}";
    }

    private static bool IsPlcReferenceKind(string kind)
    {
        return kind.Equals("PLC_REF", StringComparison.OrdinalIgnoreCase)
               || kind.Equals("PLC_REF_LEFT", StringComparison.OrdinalIgnoreCase)
               || kind.Equals("PLC_REF_RIGHT", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetArrayChartMappingLineCount(string chartTitle, out int mappingLineCount)
    {
        mappingLineCount = 0;
        if (string.IsNullOrWhiteSpace(chartTitle))
        {
            return false;
        }

        var normalized = chartTitle
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var lines = normalized.Split('\n');
        foreach (var line in lines)
        {
            if (Regex.IsMatch(line.TrimStart(), @"^(FL|NN|STR|TIME)\s*\(", RegexOptions.IgnoreCase))
            {
                mappingLineCount++;
            }
        }

        return mappingLineCount > 0;
    }

    private static bool TryParsePlcRegisterSortParts(
        string registerAddress,
        out string filePrefix,
        out int fileNumber,
        out int word,
        out int bit)
    {
        filePrefix = string.Empty;
        fileNumber = 0;
        word = 0;
        bit = -1;

        var raw = (registerAddress ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var addressMatch = Regex.Match(
            raw,
            @"(?i)\b(?<prefix>[A-Z]+)?\s*(?<file>\d+)\s*:\s*(?<word>\d+)(?:\s*/\s*(?<bit>\d+))?\b");
        if (addressMatch.Success)
        {
            filePrefix = addressMatch.Groups["prefix"].Success
                ? addressMatch.Groups["prefix"].Value.Trim().ToUpperInvariant()
                : string.Empty;

            return int.TryParse(addressMatch.Groups["file"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out fileNumber)
                && int.TryParse(addressMatch.Groups["word"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out word)
                && (!addressMatch.Groups["bit"].Success
                    || int.TryParse(addressMatch.Groups["bit"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out bit));
        }

        var colonIndex = raw.IndexOf(':');
        if (colonIndex <= 0 || colonIndex >= raw.Length - 1)
        {
            return false;
        }

        var fileToken = raw[..colonIndex].Trim();
        var addressToken = raw[(colonIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(fileToken) || string.IsNullOrWhiteSpace(addressToken))
        {
            return false;
        }

        var prefixLength = 0;
        while (prefixLength < fileToken.Length && char.IsLetter(fileToken[prefixLength]))
        {
            prefixLength++;
        }

        if (prefixLength <= 0 || prefixLength >= fileToken.Length)
        {
            return false;
        }

        filePrefix = fileToken[..prefixLength].ToUpperInvariant();
        var fileNumberToken = fileToken[prefixLength..].Trim();
        if (!int.TryParse(fileNumberToken, out fileNumber))
        {
            return false;
        }

        var slashIndex = addressToken.IndexOf('/');
        var wordToken = slashIndex >= 0 ? addressToken[..slashIndex] : addressToken;
        if (!int.TryParse(wordToken.Trim(), out word))
        {
            return false;
        }

        if (slashIndex < 0)
        {
            return true;
        }

        var bitToken = addressToken[(slashIndex + 1)..].Trim();
        return int.TryParse(bitToken, out bit);
    }

    private static bool TryExtractFlIndex(string sourceLine, out int flIndex)
    {
        flIndex = 0;
        if (string.IsNullOrWhiteSpace(sourceLine))
        {
            return false;
        }

        var text = sourceLine.AsSpan();
        for (var i = 0; i < text.Length - 2; i++)
        {
            if (!char.Equals(char.ToUpperInvariant(text[i]), 'F')
                || !char.Equals(char.ToUpperInvariant(text[i + 1]), 'L'))
            {
                continue;
            }

            var previous = i > 0 ? text[i - 1] : '\0';
            if (char.IsLetterOrDigit(previous) || previous == '_')
            {
                continue;
            }

            var cursor = i + 2;
            while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
            {
                cursor++;
            }

            if (cursor >= text.Length || text[cursor] != '(')
            {
                continue;
            }

            cursor++;
            while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
            {
                cursor++;
            }

            var numberStart = cursor;
            while (cursor < text.Length && char.IsDigit(text[cursor]))
            {
                cursor++;
            }

            if (cursor == numberStart)
            {
                continue;
            }

            while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
            {
                cursor++;
            }

            if (cursor >= text.Length || text[cursor] != ')')
            {
                continue;
            }

            var token = new string(text[numberStart..(cursor)]);
            if (int.TryParse(token, out flIndex) && flIndex > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryExtractSerialArrayElement(string sourceLine, out string lane, out int elementIndex)
    {
        lane = string.Empty;
        elementIndex = 0;
        if (string.IsNullOrWhiteSpace(sourceLine))
        {
            return false;
        }

        var match = Regex.Match(sourceLine, @"(?<![A-Za-z0-9_])(?<lane>FL|NN|STR|TIME)\s*\(\s*(?<index>\d+)\s*\)", RegexOptions.IgnoreCase);
        if (!match.Success
            || !int.TryParse(match.Groups["index"].Value, out elementIndex)
            || elementIndex <= 0)
        {
            return false;
        }

        lane = match.Groups["lane"].Value.ToUpperInvariant();
        return true;
    }

    private static int GetSerialArrayLaneSortOrder(string lane) =>
        lane.ToUpperInvariant() switch
        {
            "FL" => 0,
            "NN" => 1,
            "STR" => 2,
            "TIME" => 3,
            _ => 9
        };

    private static string FormatHardwareNumber(int? value) => value.HasValue ? value.Value.ToString("00") : "--";

    private static string BuildChartEdgeLabel(IReadOnlyList<string> labels)
    {
        if (labels.Count == 0)
        {
            return string.Empty;
        }

        var trimmed = labels
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();

        var extra = labels
            .Select(x => x.Trim())
            .Count(x => !string.IsNullOrWhiteSpace(x))
            - trimmed.Count;

        if (trimmed.Count == 0)
        {
            return string.Empty;
        }

        return extra > 0
            ? $"{string.Join(", ", trimmed)} (+{extra})"
            : string.Join(", ", trimmed);
    }

    private static (string StartLabel, string EndLabel) BuildChartEdgeEndpointLabels(IReadOnlyList<string> labels)
    {
        static string Summarize(IEnumerable<string> values)
        {
            var list = values
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();
            return list.Count == 0 ? string.Empty : string.Join(" / ", list);
        }

        var starts = new List<string>();
        var ends = new List<string>();

        foreach (var raw in labels)
        {
            var label = raw?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(label))
            {
                continue;
            }

            var arrowIndex = label.IndexOf("->", StringComparison.Ordinal);
            if (arrowIndex > 0 && arrowIndex < label.Length - 2)
            {
                var left = label[..arrowIndex].Trim();
                var right = label[(arrowIndex + 2)..].Trim();
                if (!string.IsNullOrWhiteSpace(left))
                {
                    starts.Add(left);
                }

                if (!string.IsNullOrWhiteSpace(right))
                {
                    ends.Add(right);
                }

                continue;
            }

            if (label.StartsWith("CODSTN(", StringComparison.OrdinalIgnoreCase))
            {
                starts.Add(label);
                continue;
            }

            if (label.StartsWith("LODSTN(", StringComparison.OrdinalIgnoreCase)
                || label.StartsWith("LOSRC(", StringComparison.OrdinalIgnoreCase))
            {
                starts.Add(label);
                continue;
            }

            if (label.StartsWith("PISRC(", StringComparison.OrdinalIgnoreCase)
                || label.StartsWith("CISRC(", StringComparison.OrdinalIgnoreCase)
                || label.StartsWith("LISRC(", StringComparison.OrdinalIgnoreCase))
            {
                ends.Add(label);
                continue;
            }

            if (label.StartsWith("EXTERNAL ", StringComparison.OrdinalIgnoreCase))
            {
                ends.Add(label["EXTERNAL ".Length..].Trim());
                continue;
            }

            if (label.StartsWith("SET ", StringComparison.OrdinalIgnoreCase))
            {
                ends.Add(label["SET ".Length..].Trim());
                continue;
            }

            starts.Add(label);
        }

        return (Summarize(starts), Summarize(ends));
    }

    private static string BuildLogicGateTitle(LogicGate gate, string symbol)
    {
        var algorithm = (gate.Algorithm ?? string.Empty).Trim().ToUpperInvariant();
        var title = $"GATE {gate.Index}\n{algorithm} {symbol}";

        if ((algorithm.Equals("ONDLY", StringComparison.OrdinalIgnoreCase)
             || algorithm.Equals("OFFDLY", StringComparison.OrdinalIgnoreCase)
             || algorithm.Equals("PULSE", StringComparison.OrdinalIgnoreCase)
             || algorithm.Equals("MAXPULSE", StringComparison.OrdinalIgnoreCase))
            && gate.PeriodSeconds.HasValue)
        {
            var seconds = gate.PeriodSeconds.Value.ToString("0.###", CultureInfo.InvariantCulture);
            title += $"\n{seconds} s";
        }

        return title;
    }

    private static string GetGateKind(string algorithm) =>
        algorithm.ToUpperInvariant() switch
        {
            "AND" => "GATE_AND",
            "OR" => "GATE_OR",
            "NOR" => "GATE_NOR",
            "NAND" => "GATE_NAND",
            "NOT" => "GATE_NOT",
            "XOR" => "GATE_XOR",
            "XNOR" => "GATE_XNOR",
            "GT" => "GATE_COMPARE",
            "GE" => "GATE_COMPARE",
            "LT" => "GATE_COMPARE",
            "LE" => "GATE_COMPARE",
            "EQ" => "GATE_COMPARE",
            "NE" => "GATE_COMPARE",
            "PULSE" => "GATE_PULSE",
            "MAXPULSE" => "GATE_TIMER",
            "ONDLY" => "GATE_TIMER",
            "OFFDLY" => "GATE_TIMER",
            "WATCHDOG" => "GATE_MONITOR",
            "CHDETECT" => "GATE_MONITOR",
            "CHECKBAD" => "GATE_MONITOR",
            _ => "GATE_OTHER"
        };

    private static string GetAnsiGateSymbol(string algorithm) =>
        algorithm.ToUpperInvariant() switch
        {
            "AND" => "\u2227",
            "OR" => "\u2228",
            "NOT" => "\u00AC",
            "NOR" => "\u22BD",
            "NAND" => "\u22BC",
            "XOR" => "\u2295",
            "XNOR" => "\u2299",
            "PULSE" => "\u25AD",
            "ONDLY" => "\u23F1",
            "OFFDLY" => "\u23F2",
            "MAXPULSE" => "\u25AD",
            "WATCHDOG" => "\u231A",
            "CHDETECT" => "\u2206",
            "CHECKBAD" => "\u26A0",
            "GT" => ">",
            "GE" => "\u2265",
            "LT" => "<",
            "LE" => "\u2264",
            "EQ" => "=",
            "NE" => "\u2260",
            _ => algorithm
        };
}



