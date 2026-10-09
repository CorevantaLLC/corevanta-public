using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Corevanta.Desktop;

public sealed class ModiconImportDialog : Window
{
    private readonly TextBox _plcNameTextBox = new() { MinWidth = 320, Padding = new Thickness(6, 4, 6, 4) };
    private readonly ComboBox _connectionTypeComboBox = new() { MinWidth = 180 };
    private readonly TextBox _hwyNumTextBox = new() { MinWidth = 120, Padding = new Thickness(6, 4, 6, 4) };
    private readonly TextBox _hwyBoxNumberTextBox = new() { MinWidth = 120, Padding = new Thickness(6, 4, 6, 4) };
    private readonly TextBox _hpmNumberTextBox = new() { MinWidth = 120, Padding = new Thickness(6, 4, 6, 4) };
    private readonly TextBox _moduleNumberTextBox = new() { MinWidth = 120, Padding = new Thickness(6, 4, 6, 4) };
    private readonly TextBox _registersFileTextBox = new() { IsReadOnly = true, MinWidth = 420 };
    private readonly TextBox _trafficCopFileTextBox = new() { IsReadOnly = true, MinWidth = 420 };
    private readonly ListBox _programFileList = new() { MinHeight = 140 };
    private readonly List<string> _programFiles = [];
    private UIElement? _hwyNumRow;
    private UIElement? _hwyBoxNumberRow;
    private UIElement? _serialSectionRow;
    private UIElement? _hpmNumberRow;
    private UIElement? _moduleNumberRow;

    public ModiconImportDialog()
    {
        Title = "Import Modicon PLC Files";
        Width = 860;
        Height = 740;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;

        _connectionTypeComboBox.Items.Add("EPLCG");
        _connectionTypeComboBox.Items.Add("EPLCG + HPM");
        _connectionTypeComboBox.Items.Add("Serial Interface");
        _connectionTypeComboBox.SelectedIndex = 0;
        _connectionTypeComboBox.SelectionChanged += (_, _) => UpdateConnectionTypeVisibility();

        var root = new Grid { Margin = new Thickness(14) };
        for (var i = 0; i < 15; i++)
        {
            root.RowDefinitions.Add(new RowDefinition { Height = i == 14 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        }

        root.Children.Add(BuildFormRow("PLC Name", _plcNameTextBox, 0));
        root.Children.Add(BuildFormRow("Connection Type", _connectionTypeComboBox, 1));
        _hwyNumRow = BuildFormRow("HWYNUM", _hwyNumTextBox, 2);
        root.Children.Add(_hwyNumRow);
        _hwyBoxNumberRow = BuildFormRow("HWY Box Number", _hwyBoxNumberTextBox, 3);
        root.Children.Add(_hwyBoxNumberRow);
        _serialSectionRow = BuildSectionRow("Serial Interface", 5);
        root.Children.Add(_serialSectionRow);
        _hpmNumberRow = BuildFormRow("HPM Number", _hpmNumberTextBox, 6);
        root.Children.Add(_hpmNumberRow);
        _moduleNumberRow = BuildFormRow("HPM Module", _moduleNumberTextBox, 7);
        root.Children.Add(_moduleNumberRow);

        var registersGrid = BuildFilePickerRow("Registers File", _registersFileTextBox, "Browse...", BrowseRegistersButton_Click);
        Grid.SetRow(registersGrid, 8);
        root.Children.Add(registersGrid);

        var trafficCopGrid = BuildFilePickerRow("Traffic Cop File", _trafficCopFileTextBox, "Browse...", BrowseTrafficCopButton_Click);
        Grid.SetRow(trafficCopGrid, 9);
        root.Children.Add(trafficCopGrid);

        var programGrid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        programGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        programGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var programHeader = new StackPanel { Orientation = Orientation.Horizontal };
        programHeader.Children.Add(new TextBlock
        {
            Text = "Program XPS Files",
            VerticalAlignment = VerticalAlignment.Center,
            Width = 170,
            Margin = new Thickness(0, 0, 10, 0)
        });
        var addProgramButton = new Button { Content = "Add Files...", Padding = new Thickness(10, 4, 10, 4) };
        addProgramButton.Click += AddProgramButton_Click;
        programHeader.Children.Add(addProgramButton);
        var clearProgramButton = new Button
        {
            Content = "Clear",
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(10, 4, 10, 4)
        };
        clearProgramButton.Click += (_, _) =>
        {
            _programFiles.Clear();
            RefreshProgramList();
        };
        programHeader.Children.Add(clearProgramButton);

        programGrid.Children.Add(programHeader);
        Grid.SetRow(_programFileList, 1);
        _programFileList.Margin = new Thickness(0, 8, 0, 0);
        programGrid.Children.Add(_programFileList);
        Grid.SetRow(programGrid, 10);
        root.Children.Add(programGrid);

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
        Grid.SetRow(buttonPanel, 11);
        root.Children.Add(buttonPanel);

        Content = root;
        UpdateConnectionTypeVisibility();
    }

    public string PlcName => _plcNameTextBox.Text.Trim();
    public string ConnectionType => _connectionTypeComboBox.SelectedItem?.ToString()?.Trim() ?? "EPLCG";
    public int HwyNum { get; private set; }
    public IReadOnlyList<int> HwyBoxNumbers { get; private set; } = [];
    public int? NodeAddress { get; private set; }
    public int? SerialHpmNumber { get; private set; }
    public int? SerialModuleNumber { get; private set; }
    public string RegistersFilePath => _registersFileTextBox.Text.Trim();
    public string TrafficCopFilePath => _trafficCopFileTextBox.Text.Trim();
    public IReadOnlyList<string> ProgramFiles => _programFiles;
    public string HwyBoxNumbersCsv => string.Join(", ", HwyBoxNumbers);

    private static Grid BuildFormRow(string label, Control editor, int row)
    {
        var grid = new Grid { Margin = new Thickness(0, row == 0 ? 0 : 10, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 170,
            Margin = new Thickness(0, 0, 10, 0)
        });
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        Grid.SetRow(grid, row);
        return grid;
    }

    private static Grid BuildFilePickerRow(string label, TextBox textBox, string buttonLabel, RoutedEventHandler clickHandler)
    {
        var grid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 170,
            Margin = new Thickness(0, 0, 10, 0)
        });
        Grid.SetColumn(textBox, 1);
        grid.Children.Add(textBox);
        var button = new Button
        {
            Content = buttonLabel,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(10, 0, 0, 0)
        };
        button.Click += clickHandler;
        Grid.SetColumn(button, 2);
        grid.Children.Add(button);
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
        SetRowVisibility(_serialSectionRow, showSerialFields);
        SetRowVisibility(_hpmNumberRow, showSerialFields);
        SetRowVisibility(_moduleNumberRow, showSerialFields);
    }

    private static void SetRowVisibility(UIElement? row, bool isVisible)
    {
        if (row is null)
        {
            return;
        }

        row.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BrowseRegistersButton_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select registers.prt",
            Filter = "PRT files (*.prt)|*.prt|All files (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _registersFileTextBox.Text = dialog.FileName;
    }

    private void BrowseTrafficCopButton_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select trafficcop.prt",
            Filter = "PRT files (*.prt)|*.prt|All files (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _trafficCopFileTextBox.Text = dialog.FileName;
    }

    private void AddProgramButton_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select one or more Modicon program XPS files",
            Filter = "XPS files (*.xps)|*.xps|All files (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        foreach (var file in dialog.FileNames)
        {
            if (_programFiles.Any(existing => existing.Equals(file, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _programFiles.Add(file);
        }

        RefreshProgramList();
    }

    private void RefreshProgramList()
    {
        _programFileList.ItemsSource = null;
        _programFileList.ItemsSource = _programFiles.Select(Path.GetFileName).ToList();
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PlcName))
        {
            MessageBox.Show(this, "Enter a PLC name.", "Modicon Import", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var isSerialInterface = ConnectionType.Equals("Serial Interface", StringComparison.OrdinalIgnoreCase);
        var isEplcgAndHpm = ConnectionType.Equals("EPLCG + HPM", StringComparison.OrdinalIgnoreCase);
        var requiresEplcgFields = !isSerialInterface;
        var requiresSerialFields = isSerialInterface || isEplcgAndHpm;
        var hwyNum = 0;
        IReadOnlyList<int> hwyBoxNumbers = [0];
        int? nodeAddress = null;
        int? serialHpmNumber = null;
        int? serialModuleNumber = null;

        var hasHpmText = !string.IsNullOrWhiteSpace(_hpmNumberTextBox.Text);
        var hasModuleText = !string.IsNullOrWhiteSpace(_moduleNumberTextBox.Text);
        if (hasHpmText || hasModuleText)
        {
            if (!TryParsePositiveInt(_hpmNumberTextBox.Text, out var parsedHpm))
            {
                MessageBox.Show(this, "Enter a valid HPM Number.", "Modicon Import", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryParsePositiveInt(_moduleNumberTextBox.Text, out var parsedModule))
            {
                MessageBox.Show(this, "Enter a valid HPM Module.", "Modicon Import", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            serialHpmNumber = parsedHpm;
            serialModuleNumber = parsedModule;
        }

        if (requiresSerialFields && (!serialHpmNumber.HasValue || !serialModuleNumber.HasValue))
        {
            MessageBox.Show(this, "Enter both HPM Number and HPM Module for serial mapping.", "Modicon Import", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (isSerialInterface)
        {
            nodeAddress = serialHpmNumber.GetValueOrDefault();
            hwyBoxNumbers = [serialModuleNumber.GetValueOrDefault()];
        }
        else if (requiresEplcgFields)
        {
            if (!TryParsePositiveInt(_hwyNumTextBox.Text, out hwyNum))
            {
                MessageBox.Show(this, "Enter a valid HWYNUM.", "Modicon Import", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryParsePositiveIntList(_hwyBoxNumberTextBox.Text, out hwyBoxNumbers))
            {
                MessageBox.Show(this, "Enter one or more valid HWY Box Numbers separated by commas.", "Modicon Import", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Modicon EPLCG mappings are scoped by HWY/BOX; node is serial-only.
            nodeAddress = isEplcgAndHpm ? serialHpmNumber : null;
        }

        if (string.IsNullOrWhiteSpace(RegistersFilePath))
        {
            MessageBox.Show(this, "Select registers.prt.", "Modicon Import", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_programFiles.Count == 0)
        {
            MessageBox.Show(this, "Select at least one program XPS file.", "Modicon Import", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        HwyNum = hwyNum;
        HwyBoxNumbers = hwyBoxNumbers;
        NodeAddress = nodeAddress;
        SerialHpmNumber = serialHpmNumber;
        SerialModuleNumber = serialModuleNumber;
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
