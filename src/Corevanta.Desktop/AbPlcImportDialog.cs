using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Corevanta.Desktop;

public sealed class AbPlcImportDialog : Window
{
    private readonly TextBox _plcNameTextBox = new() { MinWidth = 320, Padding = new Thickness(6, 4, 6, 4) };
    private readonly ComboBox _connectionTypeComboBox = new() { MinWidth = 180 };
    private readonly TextBox _hwyNumTextBox = new() { MinWidth = 120, Padding = new Thickness(6, 4, 6, 4) };
    private readonly TextBox _hwyBoxNumberTextBox = new() { MinWidth = 120, Padding = new Thickness(6, 4, 6, 4) };
    private readonly TextBox _plc5FileAddressTextBox = new() { MinWidth = 120, Padding = new Thickness(6, 4, 6, 4) };
    private readonly TextBox _hpmNumberTextBox = new() { MinWidth = 120, Padding = new Thickness(6, 4, 6, 4) };
    private readonly TextBox _devAddrTextBox = new() { MinWidth = 120, Padding = new Thickness(6, 4, 6, 4) };
    private readonly TextBox _systemFileTextBox = new() { IsReadOnly = true, MinWidth = 420 };
    private readonly ListBox _ladderFileList = new() { MinHeight = 140 };
    private readonly List<string> _ladderFiles = [];
    private UIElement? _hwyNumRow;
    private UIElement? _hwyBoxNumberRow;
    private UIElement? _plc5FileAddressRow;
    private UIElement? _serialSectionRow;
    private UIElement? _hpmNumberRow;
    private UIElement? _devAddrRow;

    public AbPlcImportDialog()
    {
        Title = "AB PLC5 Import Files";
        Width = 860;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;

        _connectionTypeComboBox.Items.Add("EPLCG");
        _connectionTypeComboBox.Items.Add("EPLCG + HPM");
        _connectionTypeComboBox.Items.Add("Serial Interface");
        _connectionTypeComboBox.SelectedIndex = 0;
        _connectionTypeComboBox.SelectionChanged += (_, _) => UpdateConnectionTypeVisibility();

        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(BuildFormRow("PLC Name", _plcNameTextBox, 0));
        root.Children.Add(BuildFormRow("Connection Type", _connectionTypeComboBox, 1));
        _hwyNumRow = BuildFormRow("HWYNUM", _hwyNumTextBox, 2);
        root.Children.Add(_hwyNumRow);
        _hwyBoxNumberRow = BuildFormRow("HWY Box Number", _hwyBoxNumberTextBox, 3);
        root.Children.Add(_hwyBoxNumberRow);
        _plc5FileAddressRow = BuildFormRow("PLC5 File Address", _plc5FileAddressTextBox, 4);
        root.Children.Add(_plc5FileAddressRow);
        _serialSectionRow = BuildSectionRow("Serial Interface", 5);
        root.Children.Add(_serialSectionRow);
        _hpmNumberRow = BuildFormRow("HPM Number", _hpmNumberTextBox, 6);
        root.Children.Add(_hpmNumberRow);
        _devAddrRow = BuildFormRow("DEVADDR", _devAddrTextBox, 8);
        root.Children.Add(_devAddrRow);

        var systemGrid = new Grid();
        systemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        systemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        systemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        systemGrid.Margin = new Thickness(0, 10, 0, 0);
        systemGrid.Children.Add(new TextBlock
        {
            Text = "Hardware File",
            VerticalAlignment = VerticalAlignment.Center,
            Width = 170,
            Margin = new Thickness(0, 0, 10, 0)
        });
        Grid.SetColumn(_systemFileTextBox, 1);
        systemGrid.Children.Add(_systemFileTextBox);
        var browseSystemButton = new Button
        {
            Content = "Browse...",
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(10, 0, 0, 0)
        };
        browseSystemButton.Click += BrowseSystemButton_Click;
        Grid.SetColumn(browseSystemButton, 2);
        systemGrid.Children.Add(browseSystemButton);
        Grid.SetRow(systemGrid, 10);
        root.Children.Add(systemGrid);

        var ladderGrid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        ladderGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        ladderGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var ladderHeader = new StackPanel { Orientation = Orientation.Horizontal };
        ladderHeader.Children.Add(new TextBlock
        {
            Text = "Ladder XPS Files",
            VerticalAlignment = VerticalAlignment.Center,
            Width = 170,
            Margin = new Thickness(0, 0, 10, 0)
        });
        var addLadderButton = new Button { Content = "Add Files...", Padding = new Thickness(10, 4, 10, 4) };
        addLadderButton.Click += AddLadderButton_Click;
        ladderHeader.Children.Add(addLadderButton);
        var clearLadderButton = new Button
        {
            Content = "Clear",
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(10, 4, 10, 4)
        };
        clearLadderButton.Click += (_, _) =>
        {
            _ladderFiles.Clear();
            RefreshLadderList();
        };
        ladderHeader.Children.Add(clearLadderButton);
        ladderGrid.Children.Add(ladderHeader);
        Grid.SetRow(_ladderFileList, 1);
        _ladderFileList.Margin = new Thickness(0, 8, 0, 0);
        ladderGrid.Children.Add(_ladderFileList);
        Grid.SetRow(ladderGrid, 11);
        root.Children.Add(ladderGrid);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var okButton = new Button { Content = "Import", Width = 100, Padding = new Thickness(10, 6, 10, 6) };
        okButton.Click += OkButton_Click;
        buttonPanel.Children.Add(okButton);
        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 100,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(10, 6, 10, 6)
        };
        cancelButton.Click += (_, _) => DialogResult = false;
        buttonPanel.Children.Add(cancelButton);
        Grid.SetRow(buttonPanel, 13);
        root.Children.Add(buttonPanel);

        Content = root;
        UpdateConnectionTypeVisibility();
    }

    public string PlcName => _plcNameTextBox.Text.Trim();

    public string ConnectionType => _connectionTypeComboBox.SelectedItem?.ToString()?.Trim() ?? "EPLCG";

    public int HwyNum { get; private set; }

    public IReadOnlyList<int> HwyBoxNumbers { get; private set; } = [];

    public int HwyBoxNumber => HwyBoxNumbers.Count > 0 ? HwyBoxNumbers[0] : 0;

    public string HwyBoxNumbersCsv => string.Join(", ", HwyBoxNumbers);

    public int NodeAddress { get; private set; }

    public int? SerialHpmNumber { get; private set; }

    public int? SerialModuleNumber { get; private set; }
    
    public int? SerialDevAddr { get; private set; }

    public string Plc5FileAddress => _plc5FileAddressTextBox.Text.Trim().ToUpperInvariant();

    public string SystemFilePath => _systemFileTextBox.Text.Trim();

    public IReadOnlyList<string> LadderFiles => _ladderFiles;

    private static Grid BuildFormRow(string label, Control editor, int row)
    {
        var grid = new Grid { Margin = new Thickness(0, row == 0 ? 0 : 10, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelText = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 170,
            Margin = new Thickness(0, 0, 10, 0)
        };
        grid.Children.Add(labelText);

        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);

        Grid.SetRow(grid, row);
        return grid;
    }

    private static UIElement BuildSectionRow(string label, int row)
    {
        var text = new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, row == 0 ? 0 : 12, 0, 0)
        };

        Grid.SetRow(text, row);
        return text;
    }

    private void UpdateConnectionTypeVisibility()
    {
        var isSerialInterface = ConnectionType.Equals("Serial Interface", StringComparison.OrdinalIgnoreCase);
        var isMixedConnection = ConnectionType.Equals("EPLCG + HPM", StringComparison.OrdinalIgnoreCase);
        var showEplcgFields = !isSerialInterface;
        var showSerialFields = isSerialInterface || isMixedConnection;

        SetRowVisibility(_hwyNumRow, showEplcgFields);
        SetRowVisibility(_hwyBoxNumberRow, showEplcgFields);
        SetRowVisibility(_plc5FileAddressRow, showEplcgFields);
        SetRowVisibility(_serialSectionRow, showSerialFields);
        SetRowVisibility(_hpmNumberRow, showSerialFields);
        SetRowVisibility(_devAddrRow, showSerialFields);
    }

    private static void SetRowVisibility(UIElement? row, bool isVisible)
    {
        if (row is null)
        {
            return;
        }

        row.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BrowseSystemButton_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select AB hardware file",
            Filter = "Hardware files (*.txt;*.log;*.csv;*.xps)|*.txt;*.log;*.csv;*.xps|All files (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _systemFileTextBox.Text = dialog.FileName;
        if (string.IsNullOrWhiteSpace(_plcNameTextBox.Text))
        {
            _plcNameTextBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
        }
    }

    private void AddLadderButton_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select one or more ladder XPS files",
            Filter = "XPS/OXPS files (*.xps;*.oxps)|*.xps;*.oxps|XPS files (*.xps)|*.xps|OXPS files (*.oxps)|*.oxps|All files (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        foreach (var file in dialog.FileNames)
        {
            if (_ladderFiles.Any(existing => existing.Equals(file, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _ladderFiles.Add(file);
        }

        RefreshLadderList();
    }

    private void RefreshLadderList()
    {
        _ladderFileList.ItemsSource = null;
        _ladderFileList.ItemsSource = _ladderFiles.Select(Path.GetFileName).ToList();
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PlcName))
        {
            MessageBox.Show(this, "Enter a PLC name.", "AB PLC5 Import", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var isSerialInterface = ConnectionType.Equals("Serial Interface", StringComparison.OrdinalIgnoreCase);
        var isEplcgAndHpm = ConnectionType.Equals("EPLCG + HPM", StringComparison.OrdinalIgnoreCase);
        var requiresEplcgFields = !isSerialInterface;
        var requiresSerialFields = isSerialInterface || isEplcgAndHpm;
        var hwyNum = 0;
        IReadOnlyList<int> hwyBoxNumbers = [0];
        var nodeAddress = 0;
        int? serialHpmNumber = null;
        int? serialDevAddr = null;

        var hasHpmText = !string.IsNullOrWhiteSpace(_hpmNumberTextBox.Text);
        if (hasHpmText)
        {
            if (!TryParsePositiveInt(_hpmNumberTextBox.Text, out var parsedHpm))
            {
                MessageBox.Show(this, "Enter a valid HPM Number.", "AB PLC5 Import", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            serialHpmNumber = parsedHpm;
        }

        var hasDevAddrText = !string.IsNullOrWhiteSpace(_devAddrTextBox.Text);
        if (hasDevAddrText)
        {
            if (!TryParsePositiveInt(_devAddrTextBox.Text, out var parsedDevAddr))
            {
                MessageBox.Show(this, "Enter a valid DEVADDR.", "AB PLC5 Import", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            serialDevAddr = parsedDevAddr;
        }

        if (requiresSerialFields)
        {
            if (!serialHpmNumber.HasValue || !serialDevAddr.HasValue)
            {
                MessageBox.Show(this, "Enter HPM Number and DEVADDR for serial mapping.", "AB PLC5 Import", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        if (isSerialInterface)
        {
            if (!serialHpmNumber.HasValue)
            {
                MessageBox.Show(this, "Enter HPM Number for Serial Interface.", "AB PLC5 Import", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            nodeAddress = serialHpmNumber.GetValueOrDefault();
        }
        else if (requiresEplcgFields)
        {
            if (!TryParsePositiveInt(_hwyNumTextBox.Text, out hwyNum))
            {
                MessageBox.Show(this, "Enter a valid HWYNUM.", "AB PLC5 Import", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryParsePositiveIntList(_hwyBoxNumberTextBox.Text, out hwyBoxNumbers))
            {
                MessageBox.Show(this, "Enter one or more valid HWY Box Numbers separated by commas.", "AB PLC5 Import", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            nodeAddress = isEplcgAndHpm ? serialHpmNumber.GetValueOrDefault() : 0;
        }

        if (requiresEplcgFields && string.IsNullOrWhiteSpace(Plc5FileAddress))
        {
            MessageBox.Show(this, "Enter PLC5 File Address (example: N12).", "AB PLC5 Import", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(SystemFilePath))
        {
            MessageBox.Show(this, "Select the hardware file.", "AB PLC5 Import", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_ladderFiles.Count == 0)
        {
            MessageBox.Show(this, "Select at least one ladder XPS file.", "AB PLC5 Import", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        HwyNum = hwyNum;
        HwyBoxNumbers = hwyBoxNumbers;
        NodeAddress = nodeAddress;
        SerialHpmNumber = serialHpmNumber;
        SerialModuleNumber = null;
        SerialDevAddr = serialDevAddr;
        DialogResult = true;
    }

    private static bool TryParsePositiveIntList(string text, out IReadOnlyList<int> values)
    {
        var parsedValues = new List<int>();
        foreach (var segment in text.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(segment.Trim(), out var value) || value < 0)
            {
                values = [];
                return false;
            }

            parsedValues.Add(value);
        }

        values = parsedValues
            .Distinct()
            .ToList();
        return values.Count > 0;
    }

    private static bool TryParsePositiveInt(string text, out int value)
    {
        if (int.TryParse(text.Trim(), out value))
        {
            return value >= 0;
        }

        return false;
    }
}
