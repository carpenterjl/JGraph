using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using JGraph.Controls.Themes;
using JGraph.Scripting.Designer;

namespace JGraph.Application.Scripting;

/// <summary>
/// App Designer's dialogs in the designer's window (designer plan DS5): Add Callback Function, the
/// App Details' Code Options (input arguments, single running instance), and a rename that shows
/// what will change before it does.
/// </summary>
internal static class AppDesignerDialogs
{
    /// <summary>
    /// App Designer's "Add Callback Function" dialog: a component with an unbound callback property
    /// (the app itself, for its startup function, while it has none; the figure; then every component
    /// below it), the property, and the function's name - App Designer's default, or an existing
    /// callback to bind the property to. Nothing when every property is bound, as App Designer says.
    /// </summary>
    /// <returns>The function's name when one was added or bound; null when the dialog was cancelled or nothing could be added.</returns>
    public static string? AddCallback(Window owner, DesignDocument document, DesignComponent? selected)
    {
        var candidates = new List<(string Label, DesignComponent? Component)>();
        if (document.Callbacks.All(static c => !c.IsStartup))
        {
            candidates.Add((document.Code?.ClassName ?? "app", null));
        }

        foreach (DesignComponent component in document.Design.Components)
        {
            if (document.CallbackPropertiesOf(component).Any(p => document.CallbackOf(component, p) is null))
            {
                candidates.Add((component.CodeName, component));
            }
        }

        if (candidates.Count == 0)
        {
            MessageBox.Show(owner, "Callbacks have been defined for all available components.\nTo add additional callbacks, add more components.", "App Designer", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        var dialog = Dialog(owner, "Add Callback Function");
        var grid = Form();
        var componentBox = new ComboBox { ItemsSource = candidates.Select(static c => c.Label).ToArray(), MinWidth = 220 };
        var callbackBox = new ComboBox { MinWidth = 220 };
        var nameBox = new ComboBox { IsEditable = true, MinWidth = 220 };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        error.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Error);
        Row(grid, 0, "Component:", componentBox);
        Row(grid, 1, "Callback:", callbackBox);
        Row(grid, 2, "Name:", nameBox);
        Grid.SetRow(error, 3);
        Grid.SetColumnSpan(error, 2);
        grid.Children.Add(error);
        Button add = Buttons(dialog, grid, "Add Callback");
        System.Windows.Automation.AutomationProperties.SetAutomationId(componentBox, "AddCallback.Component");
        System.Windows.Automation.AutomationProperties.SetAutomationId(callbackBox, "AddCallback.Callback");
        System.Windows.Automation.AutomationProperties.SetAutomationId(nameBox, "AddCallback.Name");
        System.Windows.Automation.AutomationProperties.SetAutomationId(add, "AddCallback.Add");

        DesignComponent? Component() => componentBox.SelectedIndex < 0 ? null : candidates[componentBox.SelectedIndex].Component;
        bool IsApp() => componentBox.SelectedIndex >= 0 && candidates[componentBox.SelectedIndex].Component is null;

        void Validate()
        {
            string name = nameBox.Text.Trim();
            string? why = name.Length == 0 ? "A name is needed."
                : IsApp() ? (document.Callbacks.Any(c => c.Name == name) ? "Startup function name must be unique." : document.WhyNotFunctionName(name))
                : document.Callbacks.Any(c => c.Name == name && c.IsStartup) ? "Name cannot be the same as the startup function."
                : document.Callbacks.Any(c => c.Name == name && !c.IsGenerated) ? null
                : document.WhyNotFunctionName(name);
            error.Text = why ?? string.Empty;
            add.IsEnabled = why is null;
        }

        void FillName()
        {
            string property = callbackBox.SelectedItem as string ?? string.Empty;
            DesignComponent? component = Component();
            string suggested = component is null ? DesignCallbacks.Unique(DesignCode.StartupName, document.NamesInUse()) : property.Length == 0 ? string.Empty : document.DefaultCallbackName(component, property);
            nameBox.ItemsSource = component is null ? [] : document.Callbacks.Where(static c => !c.IsStartup && !c.IsGenerated).Select(static c => c.Name).OrderBy(static n => n, StringComparer.Ordinal).ToArray();
            nameBox.Text = suggested;
            Validate();
        }

        void FillCallbacks()
        {
            DesignComponent? component = Component();
            callbackBox.ItemsSource = component is null ? ["StartupFcn"] : document.CallbackPropertiesOf(component).Where(p => document.CallbackOf(component, p) is null).ToArray();
            callbackBox.SelectedIndex = 0;
            FillName();
        }

        componentBox.SelectionChanged += (_, _) => FillCallbacks();
        callbackBox.SelectionChanged += (_, _) => FillName();
        nameBox.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => Validate()));
        nameBox.SelectionChanged += (_, _) => dialog.Dispatcher.BeginInvoke(Validate);

        int initial = selected is null ? 0 : candidates.FindIndex(c => ReferenceEquals(c.Component, selected));
        if (initial < 0 && selected?.Parent is { } parent)
        {
            initial = candidates.FindIndex(c => ReferenceEquals(c.Component, parent));
        }

        componentBox.SelectedIndex = Math.Max(0, initial);
        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        string chosen = nameBox.Text.Trim();
        string? refused;
        if (Component() is { } target)
        {
            string property = (string)callbackBox.SelectedItem;
            refused = document.Callbacks.Any(c => c.Name == chosen && !c.IsStartup && !c.IsGenerated)
                ? document.BindCallback(target, property, chosen)
                : document.AddCallback(target, property, chosen);
        }
        else
        {
            refused = document.AddStartup(chosen);
        }

        if (refused is not null)
        {
            MessageBox.Show(owner, refused, "Invalid Name", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }

        return chosen;
    }

    /// <summary>What App Details' Code Options set: the input arguments, the single-instance setting, both or neither.</summary>
    public readonly record struct CodeOptionsResult(bool Arguments, bool SingleInstance)
    {
        /// <summary>Whether anything was set.</summary>
        public bool Any => Arguments || SingleInstance;
    }

    /// <summary>
    /// App Designer's App Details > Code Options: Single Running Instance, and the Input Arguments
    /// (<c>arg1, arg2, ...</c>), tidied and checked as typed. OK sets what changed.
    /// </summary>
    /// <returns>What was set.</returns>
    public static CodeOptionsResult CodeOptions(Window owner, DesignDocument document)
    {
        var dialog = Dialog(owner, "App Details");
        var grid = Form();
        var single = new CheckBox { Content = "Single Running Instance", IsChecked = document.SingleInstance, ToolTip = "App allows only one running instance at a time.", VerticalAlignment = VerticalAlignment.Center };
        var arguments = new TextBox { Text = document.InputParameters, MinWidth = 240, ToolTip = "arg1, arg2, ..." };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        error.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Error);
        var hint = new TextBlock { Text = "Code Options", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        Grid.SetRow(hint, 0);
        Grid.SetColumnSpan(hint, 2);
        grid.Children.Add(hint);
        Grid.SetRow(single, 1);
        Grid.SetColumnSpan(single, 2);
        grid.Children.Add(single);
        Row(grid, 2, "Input Arguments", arguments);
        Grid.SetRow(error, 3);
        Grid.SetColumnSpan(error, 2);
        grid.Children.Add(error);
        Button ok = Buttons(dialog, grid, "OK");
        System.Windows.Automation.AutomationProperties.SetAutomationId(single, "CodeOptions.SingleInstance");
        System.Windows.Automation.AutomationProperties.SetAutomationId(arguments, "CodeOptions.InputArguments");
        System.Windows.Automation.AutomationProperties.SetAutomationId(ok, "CodeOptions.OK");

        arguments.TextChanged += (_, _) =>
        {
            string? why = DesignDocument.WhyNotInputParameters(arguments.Text);
            error.Text = why ?? string.Empty;
            ok.IsEnabled = why is null;
        };
        arguments.LostKeyboardFocus += (_, _) =>
        {
            string tidy = DesignDocument.SanitizeInputParameters(arguments.Text);
            if (tidy != arguments.Text)
            {
                arguments.Text = tidy;
            }
        };

        if (dialog.ShowDialog() != true)
        {
            return default;
        }

        bool argumentsChanged = false;
        bool singleChanged = false;
        string parameters = DesignDocument.SanitizeInputParameters(arguments.Text);
        if (parameters != document.InputParameters)
        {
            if (document.SetInputParameters(parameters) is { } refused)
            {
                MessageBox.Show(owner, refused, "App Designer", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                argumentsChanged = true;
            }
        }

        if ((single.IsChecked == true) != document.SingleInstance)
        {
            document.SetSingleInstance(single.IsChecked == true);
            singleChanged = true;
        }

        return new CodeOptionsResult(argumentsChanged, singleChanged);
    }

    /// <summary>
    /// A rename with a preview: the new name, checked as typed, over the lines of the user's code
    /// that will change with it. App Designer renames in place and shows nothing beforehand; the
    /// preview is the designer's own.
    /// </summary>
    /// <param name="owner">The window.</param>
    /// <param name="title">The dialog's title.</param>
    /// <param name="current">The name now.</param>
    /// <param name="validate">Why a name cannot be taken; null when it can.</param>
    /// <param name="preview">The lines that would change for a name, each with its line number.</param>
    /// <returns>The new name, or null when cancelled or unchanged.</returns>
    public static string? Rename(Window owner, string title, string current, Func<string, string?> validate, Func<string, IReadOnlyList<string>> preview)
    {
        var dialog = Dialog(owner, title);
        var grid = Form();
        var nameBox = new TextBox { Text = current, MinWidth = 240 };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        error.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Error);
        var changes = new ListBox { MinHeight = 80, MaxHeight = 240, MinWidth = 360, FontFamily = (System.Windows.Media.FontFamily)owner.FindResource(ThemeKeys.FontMono) };
        var caption = new TextBlock { Margin = new Thickness(0, 8, 0, 2) };
        Row(grid, 0, "Name:", nameBox);
        Grid.SetRow(error, 1);
        Grid.SetColumnSpan(error, 2);
        grid.Children.Add(error);
        Grid.SetRow(caption, 2);
        Grid.SetColumnSpan(caption, 2);
        grid.Children.Add(caption);
        Grid.SetRow(changes, 3);
        Grid.SetColumnSpan(changes, 2);
        grid.Children.Add(changes);
        Button ok = Buttons(dialog, grid, "Rename");
        System.Windows.Automation.AutomationProperties.SetAutomationId(nameBox, "Rename.Name");
        System.Windows.Automation.AutomationProperties.SetAutomationId(changes, "Rename.Preview");
        System.Windows.Automation.AutomationProperties.SetAutomationId(ok, "Rename.OK");

        void Refresh()
        {
            string name = nameBox.Text.Trim();
            string? why = name == current ? "The name is unchanged." : validate(name);
            error.Text = why ?? string.Empty;
            ok.IsEnabled = why is null;
            IReadOnlyList<string> lines = preview(name);
            changes.ItemsSource = lines;
            caption.Text = lines.Count == 0 ? "No code of yours names it." : lines.Count == 1 ? "This line of your code changes with it:" : $"These {lines.Count} lines of your code change with it:";
        }

        nameBox.TextChanged += (_, _) => Refresh();
        Refresh();
        nameBox.SelectAll();
        nameBox.Focus();
        return dialog.ShowDialog() == true && nameBox.Text.Trim() != current ? nameBox.Text.Trim() : null;
    }

    /// <summary>
    /// App Designer's dialog for an app whose custom components could not be loaded (designer plan
    /// DS7b, <c>DeleteComponentsDialog</c> with <c>LicenseMissingDialogConfig</c>): the classes with
    /// an error, the missing classes with the word to put them on the path, and the warning that
    /// opening removes them; Open Anyway or Cancel.
    /// </summary>
    /// <returns>True to open the app without those components; false to leave it closed.</returns>
    public static bool ConfirmUnavailableUserComponents(Window owner, IReadOnlyList<string> errored, IReadOnlyList<string> missing)
    {
        var dialog = Dialog(owner, "App Designer");
        var body = new StackPanel { Margin = new Thickness(12), MaxWidth = 460 };
        body.Children.Add(new TextBlock { Text = "Some components in this file could not be loaded.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        if (errored.Count > 0)
        {
            body.Children.Add(new TextBlock { Text = "Custom UI components with error:", Margin = new Thickness(0, 0, 0, 2) });
            foreach (string name in errored)
            {
                body.Children.Add(new TextBlock { Text = name, Margin = new Thickness(10, 0, 0, 2) });
            }
        }

        if (missing.Count > 0)
        {
            body.Children.Add(new TextBlock { Text = "Missing custom UI components:", Margin = new Thickness(0, 4, 0, 2) });
            foreach (string name in missing)
            {
                body.Children.Add(new TextBlock { Text = name, Margin = new Thickness(10, 0, 0, 2) });
            }

            body.Children.Add(new TextBlock
            {
                Text = "Make sure these components are on your MATLAB path and are configured for use in App Designer.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            });
        }

        body.Children.Add(new TextBlock
        {
            Text = "Opening this file will remove these components and will cause loss of functionality or errors.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        });
        var grid = new Grid();
        grid.Children.Add(body);
        Button open = Buttons(dialog, grid, "Open Anyway");
        System.Windows.Automation.AutomationProperties.SetAutomationId(open, "UnavailableComponents.OpenAnyway");
        System.Windows.Automation.AutomationProperties.SetAutomationId(dialog, "UnavailableComponents");
        return dialog.ShowDialog() == true;
    }

    /// <summary>
    /// App Designer's Keyboard Shortcuts dialog (Ctrl+/, App Designer window plan W2): R2025b's
    /// "App Designer Keyboard Shortcuts", its table of actions and keys (<see cref="DesignActions.Shortcuts"/>),
    /// "For more shortcuts, see the Documentation", "Do not show again on session start" kept in the
    /// <c>ShowShortcutsDialog</c> setting, and Close. The documentation link opens the scripting
    /// guide's App Designer chapter.
    /// </summary>
    public static void Shortcuts(Window owner, DesignSettings settings)
    {
        var dialog = Dialog(owner, "App Designer Keyboard Shortcuts");
        var table = new Grid { Margin = new Thickness(12, 12, 12, 6) };
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55, GridUnitType.Star), MaxWidth = 420 });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(45, GridUnitType.Star), MinWidth = 150 });
        void Add(int row, string action, string keys, bool header)
        {
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var left = new TextBlock { Text = action, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 12, 2), MaxWidth = 400 };
            var right = new TextBlock { Text = keys, Margin = new Thickness(0, 2, 0, 2) };
            if (header)
            {
                left.FontWeight = right.FontWeight = FontWeights.SemiBold;
            }

            Grid.SetRow(left, row);
            Grid.SetRow(right, row);
            Grid.SetColumn(right, 1);
            table.Children.Add(left);
            table.Children.Add(right);
        }

        Add(0, "Action", "Keys", header: true);
        for (int i = 0; i < DesignActions.Shortcuts.Count; i++)
        {
            Add(i + 1, DesignActions.Shortcuts[i].Action, DesignActions.Shortcuts[i].Keys, header: false);
        }

        var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("Documentation"));
        link.Click += (_, _) => ApplicationCommandsHelp(owner);
        var more = new TextBlock { Margin = new Thickness(12, 6, 12, 6) };
        more.Inlines.Add("For more shortcuts, see the ");
        more.Inlines.Add(link);
        var again = new CheckBox { Content = "Do not show again on session start", Margin = new Thickness(12, 6, 12, 12), IsChecked = !settings.GetBool(DesignSettings.ShowShortcutsDialog) };
        again.Checked += (_, _) => settings.Set(DesignSettings.ShowShortcutsDialog, false);
        again.Unchecked += (_, _) => settings.Set(DesignSettings.ShowShortcutsDialog, true);
        var close = new Button { Content = "Close", MinWidth = 90, IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 0, 12, 12) };
        close.Click += (_, _) => dialog.Close();
        System.Windows.Automation.AutomationProperties.SetAutomationId(table, "Shortcuts.Table");
        System.Windows.Automation.AutomationProperties.SetAutomationId(again, "Shortcuts.DoNotShowAgain");
        System.Windows.Automation.AutomationProperties.SetAutomationId(close, "Shortcuts.Close");
        var root = new StackPanel();
        root.Children.Add(table);
        root.Children.Add(more);
        root.Children.Add(again);
        root.Children.Add(close);
        dialog.Content = root;
        dialog.ShowDialog();

        static void ApplicationCommandsHelp(Window owner) => System.Windows.Input.ApplicationCommands.Help.Execute(null, owner);
    }

    private static Window Dialog(Window owner, string title)
    {
        // A dialog raised before the designer's window is shown (an app opened by appdesigner(...)
        // whose custom components could not be loaded, DS7b) cannot be owned by it: WPF refuses an
        // owner not yet shown, so it stands on its own in the screen's centre.
        var dialog = new Window
        {
            Title = title,
            Owner = owner.IsVisible ? owner : null,
            WindowStartupLocation = owner.IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = !owner.IsVisible,
        };
        dialog.SetResourceReference(Control.BackgroundProperty, ThemeKeys.Window);
        dialog.SetResourceReference(Control.ForegroundProperty, ThemeKeys.Text);
        return dialog;
    }

    private static Grid Form()
    {
        var grid = new Grid { Margin = new Thickness(12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 4; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        return grid;
    }

    private static void Row(Grid grid, int row, string label, FrameworkElement control)
    {
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 4) };
        control.Margin = new Thickness(0, 0, 0, 4);
        Grid.SetRow(text, row);
        Grid.SetRow(control, row);
        Grid.SetColumn(control, 1);
        grid.Children.Add(text);
        grid.Children.Add(control);
    }

    /// <summary>The dialog's body over its OK and Cancel; the OK button is returned, to be enabled as the entries allow.</summary>
    private static Button Buttons(Window dialog, Grid body, string okLabel)
    {
        var ok = new Button { Content = okLabel, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, IsCancel = true };
        ok.Click += (_, _) => dialog.DialogResult = true;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 0, 12, 12) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(body);
        dialog.Content = root;
        return ok;
    }
}
