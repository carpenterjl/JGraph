using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using JGraph.Controls.Themes;
using JGraph.Scripting.Editing;

namespace JGraph.Controls.Scripting;

/// <summary>
/// The editor's Go To dialog (App Designer window plan W9a, R2025b's <c>GotoDialog</c>): "Line number:", a field holding the line last
/// typed, OK and Cancel. OK takes a whole number above zero (<see cref="GoToLineRules"/>) and is disabled with the field's error shown
/// otherwise; the line gone to is held to the text's lines.
/// </summary>
public static class GoToLineDialog
{
    // What the field held when the dialog last closed (GotoPreferences.getGotoLine): per session, as R2025b keeps it.
    private static string _lastLine = string.Empty;

    /// <summary>Shows the dialog over <paramref name="owner"/> for a text of <paramref name="lineCount"/> lines; the line chosen (one-based), or null when cancelled.</summary>
    public static int? Show(Window? owner, int lineCount)
    {
        var dialog = new Window
        {
            Title = GoToLineRules.Title,
            Owner = owner is { IsVisible: true } ? owner : null,
            WindowStartupLocation = owner is { IsVisible: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
        };
        dialog.SetResourceReference(Control.BackgroundProperty, ThemeKeys.Window);
        dialog.SetResourceReference(Control.ForegroundProperty, ThemeKeys.Text);
        AutomationProperties.SetAutomationId(dialog, "GoToLine");

        var grid = new Grid { Margin = new Thickness(12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var label = new TextBlock { Text = GoToLineRules.LineLabel, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        grid.Children.Add(label);
        var box = new TextBox { Text = _lastLine, Width = 90 };
        AutomationProperties.SetAutomationId(box, "GoToLine.Line");
        AutomationProperties.SetName(box, GoToLineRules.LineLabel);
        Grid.SetColumn(box, 1);
        grid.Children.Add(box);
        var error = new TextBlock { Text = string.Empty, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
        error.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Error);
        AutomationProperties.SetAutomationId(error, "GoToLine.Error");
        Grid.SetRow(error, 1);
        Grid.SetColumnSpan(error, 2);
        grid.Children.Add(error);

        var ok = new Button { Content = "OK", MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        AutomationProperties.SetAutomationId(ok, "GoToLine.OK");
        var cancel = new Button { Content = "Cancel", MinWidth = 90, IsCancel = true };
        AutomationProperties.SetAutomationId(cancel, "GoToLine.Cancel");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 0, 12, 12) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(grid);
        dialog.Content = root;

        void Validate()
        {
            bool valid = GoToLineRules.IsValid(box.Text);
            ok.IsEnabled = valid;
            // The field's error shows once something was typed (R2025b marks the field on typing, not when it opens empty).
            error.Text = valid ? string.Empty : GoToLineRules.InvalidMessage;
            error.Visibility = valid || box.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        box.TextChanged += (_, _) => Validate();
        Validate();
        ok.Click += (_, _) => dialog.DialogResult = true;
        dialog.Closing += (_, _) => _lastLine = box.Text;
        dialog.Loaded += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };

        if (dialog.ShowDialog() != true || GoToLineRules.Parse(box.Text) is not { } line)
        {
            return null;
        }

        return GoToLineRules.Clamp(line, lineCount);
    }
}
