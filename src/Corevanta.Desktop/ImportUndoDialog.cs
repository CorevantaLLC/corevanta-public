using Corevanta.Desktop.Models;
using System.Windows;
using System.Windows.Controls;

namespace Corevanta.Desktop;

public sealed class ImportUndoDialog : Window
{
    private readonly ListBox _historyList = new()
    {
        MinHeight = 280,
        MinWidth = 760,
        DisplayMemberPath = nameof(ImportActionRecord.DisplayLabel)
    };

    public ImportActionRecord? SelectedAction { get; private set; }

    public ImportUndoDialog(IReadOnlyList<ImportActionRecord> actions)
    {
        Title = "Undo Import";
        Width = 900;
        Height = 470;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;

        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = "Select one import action to undo.",
            Margin = new Thickness(0, 0, 0, 8),
            FontWeight = FontWeights.SemiBold
        });

        Grid.SetRow(_historyList, 1);
        foreach (var action in actions)
        {
            _historyList.Items.Add(action);
        }

        if (_historyList.Items.Count > 0)
        {
            _historyList.SelectedIndex = 0;
        }

        root.Children.Add(_historyList);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        Grid.SetRow(buttonPanel, 2);

        var undoButton = new Button
        {
            Width = 110,
            Padding = new Thickness(10, 6, 10, 6),
            Content = "Undo",
            IsDefault = true
        };
        undoButton.Click += (_, _) => ConfirmUndo();
        buttonPanel.Children.Add(undoButton);

        var cancelButton = new Button
        {
            Width = 110,
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(8, 0, 0, 0),
            Content = "Cancel",
            IsCancel = true
        };
        cancelButton.Click += (_, _) => DialogResult = false;
        buttonPanel.Children.Add(cancelButton);
        root.Children.Add(buttonPanel);

        Content = root;
    }

    private void ConfirmUndo()
    {
        SelectedAction = _historyList.SelectedItem as ImportActionRecord;
        if (SelectedAction is null)
        {
            MessageBox.Show(this, "Select an import action to undo.", "Undo Import", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }
}
