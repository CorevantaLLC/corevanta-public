using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Corevanta.Desktop;

public sealed class AbLogixImportDialog : Window
{
    private readonly TextBox _plcNameTextBox = new() { MinWidth = 320, Padding = new Thickness(6, 4, 6, 4) };
    private readonly ComboBox _connectionTypeComboBox = new() { MinWidth = 180 };
    private readonly TextBox _hwyNumTextBox = new() { MinWidth = 120, Padding = new Thickness(6, 4, 6, 4) };
    private readonly TextBox _hwyBoxNumberTextBox = new() { MinWidth = 120, Padding = new Thickness(6, 4, 6, 4) };
    private readonly TextBox _plcFileAddressTextBox = new() { MinWidth = 120, Padding = new Thickness(6, 4, 6, 4) };
    private readonly TextBox _hpmNumberTextBox = new() { MinWidth = 120, Padding = new Thickness(6, 4, 6, 4) };
    private readonly TextBox _devAddrTextBox = new() { MinWidth = 120, Padding = new Thickness(6, 4, 6, 4) };
    private readonly TextBox _hardwareXpsTextBox = new() { IsReadOnly = true, MinWidth = 420 };
    private readonly TextBox _programXpsTextBox = new() { IsReadOnly = true, MinWidth = 420 };
    private readonly TextBox _tagsXpsTextBox = new() { IsReadOnly = true, MinWidth = 420 };
    private UIElement? _hwyNumRow;
    private UIElement? _hwyBoxNumberRow;
    private UIElement? _plcFileAddressRow;
    private UIElement? _serialSectionRow;
    private UIElement? _hpmNumberRow;
    private UIElement? _devAddrRow;

    public AbLogixImportDialog()
    {
        Title = "AB Logix Import Files";
        Width = 860;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;

        _connectionTypeComboBox.Items.Add("EPLCG");
        _connectionTypeComboBox.Items.Add("EPLCG + HPM");
        _connectionTypeComboBox.Items.Add("Serial Interface");
        _connectionTypeComboBox.SelectedIndex = 0;
        _connectionTypeComboBox.SelectionChanged += (_, _) => UpdateConnectionTypeVisibility();

        var root = new Grid { Margin = new Thickness(14) };
        for (var row = 0; row < 16; row++)
        {
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(BuildFormRow("PLC Name", _plcNameTextBox, 0));
        root.Children.Add(BuildFormRow("Connection Type", _connectionTypeComboBox, 1));
        _hwyNumRow = BuildFormRow("HWYNUM", _hwyNumTextBox, 2);
        root.Children.Add(_hwyNumRow);
        _hwyBoxNumberRow = BuildFormRow("HWY Box Number(s)", _hwyBoxNumberTextBox, 3);
        root.Children.Add(_hwyBoxNumberRow);
        _plcFileAddressRow = BuildFormRow("PLC File Address", _plcFileAddressTextBox, 4);
        root.Children.Add(_plcFileAddressRow);
        _serialSectionRow = BuildSectionRow("Serial Interface", 5);
        root.Children.Add(_serialSectionRow);
        _hpmNumberRow = BuildFormRow("HPM Number", _hpmNumberTextBox, 6);
        root.Children.Add(_hpmNumberRow);
        _devAddrRow = BuildFormRow("DEVADDR", _devAddrTextBox, 8);
        root.Children.Add(_devAddrRow);

        root.Children.Add(BuildFilePickerRow("Hardware XPS", _hardwareXpsTextBox, 10, "Select AB Logix hardware XPS file", isProgramLike: false));
        root.Children.Add(BuildFilePickerRow("Program XPS", _programXpsTextBox, 11, "Select AB Logix program XPS file", isProgramLike: true));
        root.Children.Add(BuildFilePickerRow("Tags XPS", _tagsXpsTextBox, 12, "Select AB Logix tags XPS file", isProgramLike: true));

        var hintText = new TextBlock
        {
            Text = "Expected files: Hardware.xps, Program.xps, Tags.xps",
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = System.Windows.Media.Brushes.DimGray
        };
        Grid.SetRow(hintText, 13);
        root.Children.Add(hintText);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
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
        Grid.SetRow(buttonPanel, 14);
        root.Children.Add(buttonPanel);

        Content = root;
        UpdateConnectionTypeVisibility();
    }

    public string PlcName => _plcNameTextBox.Text.Trim();
    public string ConnectionType => _connectionTypeComboBox.SelectedItem?.ToString()?.Trim() ?? "EPLCG";
    public int HwyNum { get; private set; }
    public IReadOnlyList<int> HwyBoxNumbers { get; private set; } = [];
    public string HwyBoxNumbersCsv => string.Join(", ", HwyBoxNumbers);
    public int NodeAddress { get; private set; }
    public int? SerialHpmNumber { get; private set; }
    public int? SerialModuleNumber { get; private set; }
    public int? SerialDevAddr { get; private set; }
    public string Plc5FileAddress => _plcFileAddressTextBox.Text.Trim().ToUpperInvariant();
    public string HardwareXpsPath => _hardwareXpsTextBox.Text.Trim();
    public string ProgramXpsPath => _programXpsTextBox.Text.Trim();
    public string TagsXpsPath => _tagsXpsTextBox.Text.Trim();
    public string SystemFilePath => HardwareXpsPath;
    public IReadOnlyList<string> LadderFiles => [ProgramXpsPath, TagsXpsPath];

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

    private Grid BuildFilePickerRow(string label, TextBox targetTextBox, int row, string dialogTitle, bool isProgramLike)
    {
        var fileGrid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fileGrid.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 170,
            Margin = new Thickness(0, 0, 10, 0)
        });
        Grid.SetColumn(targetTextBox, 1);
        fileGrid.Children.Add(targetTextBox);
        var browseButton = new Button
        {
            Content = "Browse...",
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(10, 0, 0, 0)
        };
        browseButton.Click += (_, _) => BrowseXpsFile(targetTextBox, dialogTitle, isProgramLike);
        Grid.SetColumn(browseButton, 2);
        fileGrid.Children.Add(browseButton);
        Grid.SetRow(fileGrid, row);
        return fileGrid;
    }

    private void BrowseXpsFile(TextBox targetTextBox, string title, bool isProgramLike)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "XPS/OXPS files (*.xps;*.oxps)|*.xps;*.oxps|XPS files (*.xps)|*.xps|OXPS files (*.oxps)|*.oxps|All files (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        targetTextBox.Text = dialog.FileName;
        if (string.IsNullOrWhiteSpace(_plcNameTextBox.Text) && isProgramLike)
        {
            _plcNameTextBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName)
                .Replace("Program", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("Tags", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim(' ', '-', '_');
        }
    }

    private void UpdateConnectionTypeVisibility()
    {
        var isSerialInterface = ConnectionType.Equals("Serial Interface", StringComparison.OrdinalIgnoreCase);
        var isMixedConnection = ConnectionType.Equals("EPLCG + HPM", StringComparison.OrdinalIgnoreCase);
        var showEplcgFields = !isSerialInterface;
        var showSerialFields = isSerialInterface || isMixedConnection;

        SetRowVisibility(_hwyNumRow, showEplcgFields);
        SetRowVisibility(_hwyBoxNumberRow, showEplcgFields);
        SetRowVisibility(_plcFileAddressRow, showEplcgFields);
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

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PlcName))
        {
            MessageBox.Show(this, "Enter a PLC name.", "AB Logix Import", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                MessageBox.Show(this, "Enter a valid HPM Number.", "AB Logix Import", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            serialHpmNumber = parsedHpm;
        }

        var hasDevAddrText = !string.IsNullOrWhiteSpace(_devAddrTextBox.Text);
        if (hasDevAddrText)
        {
            if (!TryParsePositiveInt(_devAddrTextBox.Text, out var parsedDevAddr))
            {
                MessageBox.Show(this, "Enter a valid DEVADDR.", "AB Logix Import", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            serialDevAddr = parsedDevAddr;
        }

        if (requiresSerialFields && (!serialHpmNumber.HasValue || !serialDevAddr.HasValue))
        {
            MessageBox.Show(this, "Enter HPM Number and DEVADDR for serial mapping.", "AB Logix Import", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (isSerialInterface)
        {
            nodeAddress = serialHpmNumber!.Value;
        }
        else if (requiresEplcgFields)
        {
            if (!TryParsePositiveInt(_hwyNumTextBox.Text, out hwyNum))
            {
                MessageBox.Show(this, "Enter a valid HWYNUM.", "AB Logix Import", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryParsePositiveIntList(_hwyBoxNumberTextBox.Text, out hwyBoxNumbers))
            {
                MessageBox.Show(this, "Enter one or more valid HWY Box Numbers separated by commas.", "AB Logix Import", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            nodeAddress = isEplcgAndHpm ? serialHpmNumber.GetValueOrDefault() : 0;
        }

        if (requiresEplcgFields && string.IsNullOrWhiteSpace(Plc5FileAddress))
        {
            MessageBox.Show(this, "Enter PLC File Address (example: N12).", "AB Logix Import", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(HardwareXpsPath) || !File.Exists(HardwareXpsPath))
        {
            MessageBox.Show(this, "Select a valid Hardware XPS file.", "AB Logix Import", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(ProgramXpsPath) || !File.Exists(ProgramXpsPath))
        {
            MessageBox.Show(this, "Select a valid Program XPS file.", "AB Logix Import", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(TagsXpsPath) || !File.Exists(TagsXpsPath))
        {
            MessageBox.Show(this, "Select a valid Tags XPS file.", "AB Logix Import", MessageBoxButton.OK, MessageBoxImage.Warning);
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
