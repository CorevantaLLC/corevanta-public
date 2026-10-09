using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Corevanta.Desktop;

public sealed class LicenseActivationDialog : Window
{
    private readonly TextBox _emailTextBox;
    private readonly TextBox _licenseKeyTextBox;
    private readonly TextBlock _statusText;

    public LicenseActivationDialog(string machineHash, string testLicenseKey)
    {
        Title = "Activate Corevanta License";
        Width = 640;
        Height = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = Brushes.White;

        var root = new Grid
        {
            Margin = new Thickness(18)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var intro = new TextBlock
        {
            Text = "Activate with a purchased license key, or leave key blank and use purchaser email for online activation.",
            FontWeight = FontWeights.SemiBold,
            FontSize = 14
        };
        Grid.SetRow(intro, 0);
        root.Children.Add(intro);

        var machineLabel = new TextBlock
        {
            Margin = new Thickness(0, 10, 0, 0),
            Text = "Machine Hash (for issued licenses):"
        };
        Grid.SetRow(machineLabel, 1);
        root.Children.Add(machineLabel);

        var machineValue = new TextBox
        {
            Margin = new Thickness(0, 4, 0, 0),
            IsReadOnly = true,
            Text = machineHash
        };
        Grid.SetRow(machineValue, 2);
        root.Children.Add(machineValue);

        var emailLabel = new TextBlock
        {
            Margin = new Thickness(0, 12, 0, 0),
            Text = "Purchaser email (optional):"
        };
        Grid.SetRow(emailLabel, 3);
        root.Children.Add(emailLabel);

        _emailTextBox = new TextBox
        {
            Margin = new Thickness(0, 4, 0, 0)
        };
        Grid.SetRow(_emailTextBox, 4);
        root.Children.Add(_emailTextBox);

        var licensePanel = new StackPanel
        {
            Margin = new Thickness(0, 12, 0, 0)
        };
        Grid.SetRow(licensePanel, 5);

        licensePanel.Children.Add(new TextBlock { Text = "License key:" });
        _licenseKeyTextBox = new TextBox
        {
            Margin = new Thickness(0, 4, 0, 0),
            Height = 64,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalContentAlignment = VerticalAlignment.Top
        };
        licensePanel.Children.Add(_licenseKeyTextBox);

        _statusText = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = Brushes.DimGray,
            Text = string.IsNullOrWhiteSpace(testLicenseKey)
                ? "For online activation, enter purchaser email and click Activate."
                : $"For debug: test key is {testLicenseKey}"
        };
        licensePanel.Children.Add(_statusText);
        root.Children.Add(licensePanel);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetRow(buttonPanel, 6);

        var cancelButton = new Button
        {
            Width = 100,
            Height = 34,
            Margin = new Thickness(0, 14, 8, 0),
            Content = "Cancel"
        };
        cancelButton.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
        buttonPanel.Children.Add(cancelButton);

        var activateButton = new Button
        {
            Width = 140,
            Height = 34,
            Margin = new Thickness(0, 14, 0, 0),
            Content = "Activate"
        };
        activateButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_licenseKeyTextBox.Text) && string.IsNullOrWhiteSpace(_emailTextBox.Text))
            {
                _statusText.Foreground = Brushes.Firebrick;
                _statusText.Text = "Enter a license key, or enter purchaser email for online activation.";
                return;
            }

            DialogResult = true;
            Close();
        };
        buttonPanel.Children.Add(activateButton);
        root.Children.Add(buttonPanel);

        Content = root;
    }

    public string EmailOrUser => (_emailTextBox.Text ?? string.Empty).Trim();

    public string LicenseKey => (_licenseKeyTextBox.Text ?? string.Empty).Trim();

    public void ShowValidationError(string message)
    {
        _statusText.Foreground = Brushes.Firebrick;
        _statusText.Text = message;
    }
}
