using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using JGraph.Controls.Themes;
using JGraph.Scripting.Designer;

namespace JGraph.Application.Scripting;

/// <content>
/// A custom UI component's dialogs (App Designer window plan W15): Add Event–Public Callback Pair, Add Property
/// (R2025b's <c>AddEventDialog</c> and <c>AddPropertyDialog</c> in its App Designer client, their words from its
/// resource bundle), and the "App Designer Custom UI Component Metadata" dialog (<c>MetadataUI_exported</c>,
/// <c>MetadataUIViewModel</c>), which <c>appdesigner.customcomponent.configureMetadata</c> and Configure for Apps open.
/// </content>
internal static partial class AppDesignerDialogs
{
    /// <summary>
    /// App Designer's "Add Event–Public Callback Pair" dialog, or the same over <paramref name="existing"/> to change
    /// it: the event's name (which must not end in Fcn, the callback property being the name with Fcn after it),
    /// its description, the callback property it makes, and the line that triggers it with a Copy button.
    /// </summary>
    /// <returns>The event's name when one was added or changed; null when cancelled.</returns>
    public static string? AddEvent(Window owner, DesignDocument document, DesignCodeEvent? existing)
    {
        var dialog = Dialog(owner, existing is null ? "Add Event–Public Callback Pair" : "Edit Event–Public Callback Pair");
        var grid = Form();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var nameBox = new TextBox { MinWidth = 260, Text = existing?.Name ?? string.Empty };
        var descriptionBox = new TextBox { MinWidth = 260, Text = existing?.Description ?? string.Empty };
        var callback = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        var trigger = new TextBox { IsReadOnly = true, MinWidth = 200, VerticalAlignment = VerticalAlignment.Center };
        var copy = new Button { Content = DesignComponentCode.CopyTriggerCode, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(6, 1, 6, 1) };
        var triggerRow = new DockPanel();
        DockPanel.SetDock(copy, Dock.Right);
        triggerRow.Children.Add(copy);
        triggerRow.Children.Add(trigger);
        var help = new TextBlock { Text = DesignComponentCode.AddEventDescription, TextWrapping = TextWrapping.Wrap, MaxWidth = 360, Margin = new Thickness(0, 0, 0, 8), Opacity = 0.8 };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), MaxWidth = 360 };
        error.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Error);
        Grid.SetColumnSpan(help, 2);
        grid.Children.Add(help);
        Row(grid, 1, "Event Name:", nameBox);
        Row(grid, 2, "Description:", descriptionBox);
        Row(grid, 3, "Public Callback:", callback);
        Row(grid, 4, "Trigger:", triggerRow);
        Grid.SetRow(error, 5);
        Grid.SetColumnSpan(error, 2);
        grid.Children.Add(error);
        Button ok = Buttons(dialog, grid, existing is null ? "Add" : "OK");
        System.Windows.Automation.AutomationProperties.SetAutomationId(nameBox, "AddEvent.Name");
        System.Windows.Automation.AutomationProperties.SetAutomationId(descriptionBox, "AddEvent.Description");
        System.Windows.Automation.AutomationProperties.SetAutomationId(callback, "AddEvent.Callback");
        System.Windows.Automation.AutomationProperties.SetAutomationId(trigger, "AddEvent.Trigger");
        System.Windows.Automation.AutomationProperties.SetAutomationId(copy, "AddEvent.Copy");
        System.Windows.Automation.AutomationProperties.SetAutomationId(ok, "AddEvent.Add");

        void Validate()
        {
            string name = nameBox.Text.Trim();
            var preview = new DesignCodeEvent(name.Length == 0 ? "Name" : name);
            callback.Text = preview.CallbackProperty;
            trigger.Text = preview.NotifyLine;
            string? why = name.Length == 0 ? "A name is needed." : document.WhyNotEventName(name, existing?.Name);
            error.Text = why ?? string.Empty;
            ok.IsEnabled = why is null;
        }

        nameBox.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => Validate()));
        copy.Click += (_, _) => Clipboard.SetText(trigger.Text);
        Validate();
        nameBox.Focus();
        nameBox.SelectAll();
        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        string chosen = nameBox.Text.Trim();
        string? refused = existing is null ? document.AddEvent(chosen, descriptionBox.Text.Trim()) : document.UpdateEvent(existing, chosen, descriptionBox.Text.Trim());
        if (refused is not null)
        {
            MessageBox.Show(owner, refused, "Invalid Name", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }

        return chosen;
    }

    /// <summary>
    /// App Designer's "Add Property" dialog for a custom UI component, or the same over <paramref name="existing"/>:
    /// the name, access, data type, size, default value, validation functions (by their categories, each with its
    /// input arguments when it takes any), description and attributes. The default is checked against the
    /// declaration before the dialog closes (<c>validatePropertyValue</c>), the refusal shown in it.
    /// </summary>
    /// <returns>The property's name when one was added or changed; null when cancelled.</returns>
    public static string? AddManagedProperty(Window owner, DesignDocument document, DesignManagedProperty? existing, string access)
    {
        var dialog = Dialog(owner, existing is null ? "Add Property" : "Edit Property");
        dialog.ResizeMode = ResizeMode.CanResize;
        var grid = Form();
        for (int i = 0; i < 6; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        var nameBox = new TextBox { MinWidth = 280, Text = existing?.Name ?? string.Empty };
        var accessBox = new ComboBox { ItemsSource = DesignComponentCode.AccessChoices, SelectedItem = existing?.Access ?? access, MinWidth = 120, HorizontalAlignment = HorizontalAlignment.Left };
        var typeBox = new ComboBox { IsEditable = true, ItemsSource = DesignComponentCode.DataTypes, Text = existing?.DataType ?? string.Empty, MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "The class the value must have, or empty for any" };
        var sizeBox = new TextBox { MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left, Text = existing is null ? string.Empty : string.Join(",", existing.Size), ToolTip = "The size the value must have, as 1,1 or 1,:; empty for any" };
        var defaultBox = new TextBox { MinWidth = 280, Text = existing?.DefaultValue ?? string.Empty, ToolTip = "The initial value, as MATLAB code" };
        var descriptionBox = new TextBox { MinWidth = 280, Text = existing?.Description ?? string.Empty };

        // The attributes, with the dialog's words for each.
        var attributes = new WrapPanel();
        var attributeBoxes = new List<CheckBox>();
        foreach (DesignPropertyAttributeEntry attribute in DesignComponentCode.PropertyAttributes)
        {
            var box = new CheckBox { Content = attribute.Name, ToolTip = attribute.Description, Margin = new Thickness(0, 2, 12, 2), IsChecked = existing?.Flags.Contains(attribute.Name, StringComparer.Ordinal) == true };
            System.Windows.Automation.AutomationProperties.SetAutomationId(box, "AddProperty.Attribute." + attribute.Name);
            attributeBoxes.Add(box);
            attributes.Children.Add(box);
        }

        // The validation functions by category, each a check with an argument box when it takes arguments.
        var validations = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
        var validationRows = new List<(DesignValidationFunctionEntry Entry, CheckBox Box, TextBox? Arguments)>();
        foreach (IGrouping<string, DesignValidationFunctionEntry> category in DesignComponentCode.ValidationFunctions.GroupBy(static v => v.Category))
        {
            var panel = new StackPanel();
            foreach (DesignValidationFunctionEntry entry in category)
            {
                DesignValidationFunction? held = existing?.ValidationFunctions.FirstOrDefault(v => v.Name == entry.Name);
                var box = new CheckBox { Content = entry.Name, ToolTip = entry.Description, Margin = new Thickness(0, 1, 8, 1), VerticalAlignment = VerticalAlignment.Center, IsChecked = held is not null };
                System.Windows.Automation.AutomationProperties.SetAutomationId(box, "AddProperty.Validation." + entry.Name);
                TextBox? arguments = null;
                var row = new DockPanel { LastChildFill = true };
                row.Children.Add(box);
                if (entry.InputCount > 0)
                {
                    arguments = new TextBox { MinWidth = 140, Margin = new Thickness(0, 1, 0, 1), ToolTip = entry.Placeholder, Text = held?.InputArguments ?? string.Empty, IsEnabled = held is not null };
                    System.Windows.Automation.AutomationProperties.SetAutomationId(arguments, "AddProperty.Validation." + entry.Name + ".Arguments");
                    TextBox captured = arguments;
                    box.Checked += (_, _) => captured.IsEnabled = true;
                    box.Unchecked += (_, _) => captured.IsEnabled = false;
                    row.Children.Add(arguments);
                }

                panel.Children.Add(row);
                validationRows.Add((entry, box, arguments));
            }

            validations.Children.Add(new Expander { Header = category.Key, Content = panel, IsExpanded = category.Key == "Numeric", Margin = new Thickness(0, 0, 0, 2) });
        }

        var validationScroll = new ScrollViewer { Content = validations, MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), MaxWidth = 420 };
        error.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Error);
        Row(grid, 0, "Name:", nameBox);
        Row(grid, 1, "Access:", accessBox);
        Row(grid, 2, "Data Type:", typeBox);
        Row(grid, 3, "Size:", sizeBox);
        Row(grid, 4, "Default Value:", defaultBox);
        Row(grid, 5, "Validation Functions:", validationScroll);
        Row(grid, 6, "Description:", descriptionBox);
        Row(grid, 7, "Attributes:", attributes);
        Grid.SetRow(error, 8);
        Grid.SetColumnSpan(error, 2);
        grid.Children.Add(error);
        System.Windows.Automation.AutomationProperties.SetAutomationId(nameBox, "AddProperty.Name");
        System.Windows.Automation.AutomationProperties.SetAutomationId(accessBox, "AddProperty.Access");
        System.Windows.Automation.AutomationProperties.SetAutomationId(typeBox, "AddProperty.DataType");
        System.Windows.Automation.AutomationProperties.SetAutomationId(sizeBox, "AddProperty.Size");
        System.Windows.Automation.AutomationProperties.SetAutomationId(defaultBox, "AddProperty.Default");
        System.Windows.Automation.AutomationProperties.SetAutomationId(descriptionBox, "AddProperty.Description");

        // The OK of this dialog keeps it open on a refusal, which it shows under the rows.
        var ok = new Button { Content = existing is null ? "Add" : "OK", MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, IsCancel = true };
        System.Windows.Automation.AutomationProperties.SetAutomationId(ok, "AddProperty.Add");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 0, 12, 12) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 640 });
        dialog.Content = root;

        DesignManagedProperty Built()
        {
            var property = new DesignManagedProperty(nameBox.Text.Trim(), accessBox.SelectedItem as string ?? access)
            {
                DataType = typeBox.Text.Trim(),
                DefaultValue = defaultBox.Text.Trim(),
                Description = descriptionBox.Text.Trim(),
            };
            property.Size.AddRange(sizeBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            foreach ((DesignValidationFunctionEntry entry, CheckBox box, TextBox? arguments) in validationRows)
            {
                if (box.IsChecked == true)
                {
                    property.ValidationFunctions.Add(new DesignValidationFunction(entry.Name, arguments?.Text.Trim() ?? string.Empty));
                }
            }

            property.SetFlags(attributeBoxes.Where(static b => b.IsChecked == true).Select(static b => (string)b.Content));
            return property;
        }

        void Validate()
        {
            string name = nameBox.Text.Trim();
            string? why = name.Length == 0 ? "A name is needed." : document.WhyNotManagedPropertyName(name, existing?.Name);
            error.Text = why ?? string.Empty;
            ok.IsEnabled = why is null;
        }

        string? made = null;
        ok.Click += (_, _) =>
        {
            DesignManagedProperty property = Built();
            string? refused = existing is null ? document.AddManagedProperty(property) : document.ModifyManagedProperty(existing, property);
            if (refused is not null)
            {
                error.Text = refused;
                return;
            }

            made = property.Name;
            dialog.DialogResult = true;
        };
        nameBox.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => Validate()));
        Validate();
        nameBox.Focus();
        nameBox.SelectAll();
        return dialog.ShowDialog() == true ? made : null;
    }

    /// <summary>
    /// The "App Designer Custom UI Component Metadata" dialog over the component class <paramref name="fullFileName"/>
    /// (<c>MetadataUI_exported</c>): the file, Component Library Appearance (Name, Category, Icon with Browse) and
    /// Component Details (Description, Version, Author's Name, Author's Email), with Help, Ok and Cancel. Ok checks the
    /// fields as R2025b's view model does, makes the component once through <paramref name="checker"/> for its default
    /// position and the checks on it, writes the registry, and confirms with Add to Path or OK; Add to Path runs
    /// <c>addpath</c> through <paramref name="statement"/>. The dialog is modeless, as R2025b's uifigure is.
    /// </summary>
    /// <param name="owner">The window the dialog stands over: the designer's when it is up, else the shell's.</param>
    /// <param name="fullFileName">The component class, validated by the builtin.</param>
    /// <param name="checker">Makes the component once in the MATLAB session: its position, or why it cannot be registered.</param>
    /// <param name="statement">Runs a statement in the MATLAB session (Add to Path's <c>addpath</c>); null without a session.</param>
    /// <param name="registered">Called with the registry once it is written, for the window's library.</param>
    public static Window ConfigureMetadata(Window owner, string fullFileName, Func<string, Task<DesignComponentCheck>> checker, Func<string, Task>? statement, Action<DesignComponentMetadata>? registered)
    {
        DesignComponentMetadata model = DesignComponentMetadata.Load(fullFileName);
        DesignComponentMetadataRecord record = model.InitialRecord(model.IsRegistered ? null : DesignComponentMetadata.HelpLineOf(fullFileName));
        bool wasRegistered = model.IsRegistered;

        var dialog = new Window
        {
            Title = DesignComponentMetadata.Title,
            Owner = owner.IsVisible ? owner : null,
            WindowStartupLocation = owner.IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Width = 485,
            Height = 617,
            MinWidth = 420,
            MinHeight = 480,
            ShowInTaskbar = !owner.IsVisible,
        };
        dialog.SetResourceReference(Control.BackgroundProperty, ThemeKeys.Window);
        dialog.SetResourceReference(Control.ForegroundProperty, ThemeKeys.Text);
        System.Windows.Automation.AutomationProperties.SetAutomationId(dialog, "ComponentMetadata");

        var grid = new Grid { Margin = new Thickness(12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        int rows = 0;
        void Add(FrameworkElement element, bool span = false)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(element, rows);
            if (span)
            {
                Grid.SetColumnSpan(element, 2);
            }

            grid.Children.Add(element);
            rows++;
        }

        void AddRow(string label, FrameworkElement control, string? tip = null)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 6), ToolTip = tip };
            control.Margin = new Thickness(0, 0, 0, 6);
            Grid.SetRow(text, rows);
            Grid.SetRow(control, rows);
            Grid.SetColumn(control, 1);
            grid.Children.Add(text);
            grid.Children.Add(control);
            rows++;
        }

        static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 6) };

        Add(new TextBlock { Text = DesignComponentMetadata.Heading, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) }, span: true);
        var filePath = new TextBlock { Text = DesignComponentMetadata.ShortenedPath(fullFileName), ToolTip = fullFileName, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetAutomationId(filePath, "ComponentMetadata.FilePath");
        AddRow("Component File", filePath);

        Add(Heading("Component Library Appearance"), span: true);
        var nameBox = new TextBox { Text = record.ComponentName };
        var categories = model.Categories.ToList();
        if (!categories.Contains(DesignComponentMetadata.DefaultCategory, StringComparer.Ordinal))
        {
            categories.Add(DesignComponentMetadata.DefaultCategory);
        }

        var categoryBox = new ComboBox { IsEditable = true, ItemsSource = categories, Text = record.Category, ToolTip = DesignComponentMetadata.CategoryHelp };
        var icon = new Image { Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Stretch = System.Windows.Media.Stretch.Uniform };
        string iconUri = record.Icon.Length > 0 ? record.Icon : DesignComponentMetadata.DefaultIcon;
        ShowIcon(icon, iconUri);
        var browse = new Button { Content = "Browse", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(8, 1, 8, 1) };
        var iconRow = new StackPanel { Orientation = Orientation.Horizontal };
        iconRow.Children.Add(icon);
        iconRow.Children.Add(browse);
        AddRow("Name", nameBox);
        AddRow("Category", categoryBox, DesignComponentMetadata.CategoryHelp);
        AddRow("Icon", iconRow);

        Add(Heading("Component Details"), span: true);
        var descriptionBox = new TextBox { Text = record.Description.Replace("\n", Environment.NewLine, StringComparison.Ordinal), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60, MaxHeight = 120, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, ToolTip = DesignComponentMetadata.ComponentDetailsHelp };
        var versionBox = new TextBox { Text = record.Version.Length == 0 ? DesignComponentMetadata.DefaultVersion : record.Version, MaxWidth = 120, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 100 };
        var authorBox = new TextBox { Text = record.AuthorName };
        var emailBox = new TextBox { Text = record.AuthorEmail };
        AddRow("Description", descriptionBox, DesignComponentMetadata.ComponentDetailsHelp);
        AddRow("Version", versionBox);
        AddRow("Author's Name", authorBox);
        AddRow("Author's Email", emailBox);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        status.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextSecondary);
        Add(status, span: true);
        System.Windows.Automation.AutomationProperties.SetAutomationId(nameBox, "ComponentMetadata.Name");
        System.Windows.Automation.AutomationProperties.SetAutomationId(categoryBox, "ComponentMetadata.Category");
        System.Windows.Automation.AutomationProperties.SetAutomationId(browse, "ComponentMetadata.Browse");
        System.Windows.Automation.AutomationProperties.SetAutomationId(descriptionBox, "ComponentMetadata.Description");
        System.Windows.Automation.AutomationProperties.SetAutomationId(versionBox, "ComponentMetadata.Version");
        System.Windows.Automation.AutomationProperties.SetAutomationId(authorBox, "ComponentMetadata.AuthorName");
        System.Windows.Automation.AutomationProperties.SetAutomationId(emailBox, "ComponentMetadata.AuthorEmail");
        System.Windows.Automation.AutomationProperties.SetAutomationId(status, "ComponentMetadata.Status");

        var help = new Button { Content = "Help", MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
        var ok = new Button { Content = "Ok", MinWidth = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", MinWidth = 80, IsCancel = true };
        System.Windows.Automation.AutomationProperties.SetAutomationId(ok, "ComponentMetadata.Ok");
        System.Windows.Automation.AutomationProperties.SetAutomationId(cancel, "ComponentMetadata.Cancel");
        System.Windows.Automation.AutomationProperties.SetAutomationId(help, "ComponentMetadata.Help");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 0, 12, 12) };
        buttons.Children.Add(help);
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        dialog.Content = root;

        // Each field is checked as it is left, the refusal said and the field put back (the ValueChanged callbacks).
        void Guard(TextBox box, Func<string, string?> check, Func<string> previous, Action<string> keep)
        {
            box.LostKeyboardFocus += (_, _) =>
            {
                if (check(box.Text) is { } why)
                {
                    MessageBox.Show(dialog, why, DesignComponentMetadata.Header, MessageBoxButton.OK, MessageBoxImage.Error);
                    box.Text = previous();
                }
                else
                {
                    keep(box.Text);
                }
            };
        }

        Guard(nameBox, DesignComponentMetadata.WhyNotComponentName, () => record.ComponentName, v => record.ComponentName = v);
        Guard(versionBox, DesignComponentMetadata.WhyNotVersion, () => record.Version, v => record.Version = v);
        Guard(emailBox, DesignComponentMetadata.WhyNotEmail, () => record.AuthorEmail, v => record.AuthorEmail = v);
        categoryBox.LostKeyboardFocus += (_, _) =>
        {
            if (DesignComponentMetadata.WhyNotCategory(categoryBox.Text) is { } why)
            {
                MessageBox.Show(dialog, why, DesignComponentMetadata.Header, MessageBoxButton.OK, MessageBoxImage.Error);
                categoryBox.Text = record.Category;
            }
            else
            {
                record.Category = categoryBox.Text;
            }
        };

        browse.Click += (_, _) =>
        {
            var picker = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Choose a File",
                Filter = "All Image Files (*.gif,*.jpeg,*.jpg,*.png)|*.gif;*.jpeg;*.jpg;*.png|All Files (*.*)|*.*",
            };
            if (picker.ShowDialog(dialog) != true)
            {
                return;
            }

            string extension = Path.GetExtension(picker.FileName).ToLowerInvariant();
            if (extension is not (".gif" or ".jpeg" or ".jpg" or ".png"))
            {
                MessageBox.Show(dialog, DesignComponentMetadata.IconFileError, DesignComponentMetadata.Header, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                iconUri = ResizedIconUri(picker.FileName);
                ShowIcon(icon, iconUri);
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or InvalidOperationException)
            {
                MessageBox.Show(dialog, DesignComponentMetadata.IconFileError, DesignComponentMetadata.Header, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        help.Click += (_, _) => MessageBox.Show(dialog, DesignComponentMetadata.Heading + "\n\n" + DesignComponentMetadata.CategoryHelp + "\n" + DesignComponentMetadata.ComponentDetailsHelp, DesignComponentMetadata.Header, MessageBoxButton.OK, MessageBoxImage.Information);
        cancel.Click += (_, _) => dialog.Close();

        bool busy = false;
        ok.Click += async (_, _) =>
        {
            if (busy)
            {
                return;
            }

            // The checks in the view model's order; a failing one leaves the dialog as it is (OkButtonPushed).
            if (DesignComponentMetadata.WhyNotComponentName(nameBox.Text) is not null || DesignComponentMetadata.WhyNotCategory(categoryBox.Text) is not null
                || DesignComponentMetadata.WhyNotVersion(versionBox.Text) is not null || DesignComponentMetadata.WhyNotEmail(emailBox.Text) is not null)
            {
                status.Text = DesignComponentMetadata.WhyNotComponentName(nameBox.Text) ?? DesignComponentMetadata.WhyNotCategory(categoryBox.Text) ?? DesignComponentMetadata.WhyNotVersion(versionBox.Text) ?? DesignComponentMetadata.WhyNotEmail(emailBox.Text);
                return;
            }

            busy = true;
            ok.IsEnabled = false;
            status.Text = DesignComponentMetadata.InProgress;
            try
            {
                DesignComponentCheck check = await checker(fullFileName);
                if (check.Error is not null || check.Position is null)
                {
                    status.Text = string.Empty;
                    MessageBox.Show(dialog, $"An error occurred while configuring metadata for the component.\n\n{check.Error ?? "The component gave no position."}", DesignComponentMetadata.Header, MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                record.ComponentName = nameBox.Text;
                record.Version = versionBox.Text;
                record.Description = descriptionBox.Text.Replace("\r\n", "\n", StringComparison.Ordinal);
                record.Category = categoryBox.Text;
                record.Icon = iconUri;
                record.AuthorName = authorBox.Text;
                record.AuthorEmail = emailBox.Text;
                record.Avatar = string.Empty;
                record.AvatarDark = string.Empty;
                record.DefaultPosition = check.Position;
                try
                {
                    model.Save(record);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    status.Text = string.Empty;
                    MessageBox.Show(dialog, $"An error occurred while configuring metadata for the component.\n\n{DesignComponentMetadata.NoWriteAccess}\n({ex.Message})", DesignComponentMetadata.Header, MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                registered?.Invoke(model);
                status.Text = string.Empty;
                dialog.Hide();
                bool addToPath = Confirm(owner.IsVisible ? owner : dialog, wasRegistered ? DesignComponentMetadata.UpdateSuccess : DesignComponentMetadata.RegistrationSuccess, DesignComponentMetadata.Header, DesignComponentMetadata.AddToPath, "OK");
                if (addToPath && statement is not null)
                {
                    await statement($"addpath('{model.Directory.Replace("'", "''", StringComparison.Ordinal)}')");
                }

                dialog.Close();
            }
            finally
            {
                busy = false;
                ok.IsEnabled = true;
            }
        };

        dialog.Loaded += (_, _) =>
        {
            if (!model.IsValid)
            {
                MessageBox.Show(dialog, DesignComponentMetadata.InvalidModelWarning, DesignComponentMetadata.Header, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        dialog.Show();
        return dialog;
    }

    /// <summary>A two-button confirmation (R2025b's <c>uiconfirm</c> with its options): true for the first button.</summary>
    private static bool Confirm(Window owner, string message, string title, string first, string second)
    {
        var dialog = new Window
        {
            Title = title,
            Owner = owner.IsVisible ? owner : null,
            WindowStartupLocation = owner.IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
        };
        dialog.SetResourceReference(Control.BackgroundProperty, ThemeKeys.Window);
        dialog.SetResourceReference(Control.ForegroundProperty, ThemeKeys.Text);
        System.Windows.Automation.AutomationProperties.SetAutomationId(dialog, "ComponentMetadata.Confirm");
        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 420, Margin = new Thickness(16) };
        var one = new Button { Content = first, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var two = new Button { Content = second, MinWidth = 90, IsCancel = true };
        System.Windows.Automation.AutomationProperties.SetAutomationId(one, "ComponentMetadata.Confirm.First");
        System.Windows.Automation.AutomationProperties.SetAutomationId(two, "ComponentMetadata.Confirm.Second");
        one.Click += (_, _) => dialog.DialogResult = true;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 0, 16, 16) };
        buttons.Children.Add(one);
        buttons.Children.Add(two);
        var root = new StackPanel();
        root.Children.Add(text);
        root.Children.Add(buttons);
        dialog.Content = root;
        return dialog.ShowDialog() == true;
    }

    private static void ShowIcon(Image image, string dataUri)
    {
        if (DesignAppDetails.OfDataUri(dataUri) is not { } picture)
        {
            image.Source = null;
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(picture.Bytes);
            bitmap.EndInit();
            bitmap.Freeze();
            image.Source = bitmap;
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or InvalidOperationException)
        {
            image.Source = null;
        }
    }

    /// <summary>The picture file brought to the library's 24 by 24 as a PNG data URI (<c>resizeIconImage</c>, <c>getImageDataURIFromFile</c>).</summary>
    private static string ResizedIconUri(string path)
    {
        var source = new BitmapImage();
        source.BeginInit();
        source.CacheOption = BitmapCacheOption.OnLoad;
        source.UriSource = new Uri(Path.GetFullPath(path));
        source.EndInit();
        source.Freeze();
        var scaled = new TransformedBitmap(source, new System.Windows.Media.ScaleTransform(24.0 / Math.Max(1, source.PixelWidth), 24.0 / Math.Max(1, source.PixelHeight)));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(scaled));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return DesignAppDetails.DataUri(stream.ToArray(), "png");
    }
}
