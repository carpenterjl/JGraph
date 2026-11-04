using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using JGraph.Controls.Designer;
using JGraph.Controls.Themes;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Toolstrip;

namespace JGraph.Application.Scripting;

/// <summary>
/// The visual designer's window (designer plan DS2, decision DD3): like R2025b's App Designer, its own
/// top-level window beside the desktop, with a tab per open app. Each tab shows the app in one of
/// two views, switched by the buttons above it (Shift+F7): the Design View, the app's canvas with
/// App Designer's component library on the left; and the Code View (DS5), the app's class with the
/// code browser - callbacks, functions, properties - on the left. The component browser, over the
/// inspector and the Callbacks tab, is on the right in both. Over them is App Designer's toolstrip
/// (App Designer window plan, stage W2): DESIGNER, CANVAS and VIEW in the Design View, DESIGNER,
/// EDITOR and VIEW in the Code View, the RUN section and the quick access bar.
/// </summary>
internal sealed partial class AppDesignerWindow : Window
{
    /// <summary>Switches the active app between its Design View and its Code View (Shift+F7).</summary>
    public static readonly RoutedCommand ToggleView = new(nameof(ToggleView), typeof(AppDesignerWindow));

    /// <summary>Opens the Add Callback Function dialog.</summary>
    public static readonly RoutedCommand AddCallback = new(nameof(AddCallback), typeof(AppDesignerWindow));

    /// <summary>Adds a private function to the editable section.</summary>
    public static readonly RoutedCommand AddFunction = new(nameof(AddFunction), typeof(AppDesignerWindow));

    /// <summary>Adds a private property to the editable section.</summary>
    public static readonly RoutedCommand AddProperty = new(nameof(AddProperty), typeof(AppDesignerWindow));

    /// <summary>Opens the App Details' Code Options: input arguments and the single running instance.</summary>
    public static readonly RoutedCommand CodeOptions = new(nameof(CodeOptions), typeof(AppDesignerWindow));

    /// <summary>App Details (DESIGNER > SHARE > App Details).</summary>
    public static readonly RoutedCommand AppDetails = new(nameof(AppDetails), typeof(AppDesignerWindow));

    /// <summary>The Help Text dialog (EDITOR > App Help Text).</summary>
    public static readonly RoutedCommand HelpText = new(nameof(HelpText), typeof(AppDesignerWindow));

    /// <summary>Save Copy As... (Save's drop-down).</summary>
    public static readonly RoutedCommand SaveCopyAs = new(nameof(SaveCopyAs), typeof(AppDesignerWindow));

    /// <summary>Export to .m File... (Save's drop-down).</summary>
    public static readonly RoutedCommand ExportCode = new(nameof(ExportCode), typeof(AppDesignerWindow));

    /// <summary>Convert > 2-Panel App with Auto-Reflow.</summary>
    public static readonly RoutedCommand ConvertTwoPanel = new(nameof(ConvertTwoPanel), typeof(AppDesignerWindow));

    /// <summary>Convert > 3-Panel App with Auto-Reflow.</summary>
    public static readonly RoutedCommand ConvertThreePanel = new(nameof(ConvertThreePanel), typeof(AppDesignerWindow));

    /// <summary>Convert > App without Auto-Reflow.</summary>
    public static readonly RoutedCommand ConvertStandard = new(nameof(ConvertStandard), typeof(AppDesignerWindow));

    /// <summary>Run (F5): saves the app and runs it in the MATLAB session, as App Designer's Run does (designer plan DS8).</summary>
    public static readonly RoutedCommand RunApp = new(nameof(RunApp), typeof(AppDesignerWindow));

    /// <summary>Groups the selection (App Designer's Group, Ctrl+G; open item 150).</summary>
    public static readonly RoutedCommand GroupSelection = new(nameof(GroupSelection), typeof(AppDesignerWindow));

    /// <summary>Puts the other selected components into the selected group (Add to Group).</summary>
    public static readonly RoutedCommand AddToGroup = new(nameof(AddToGroup), typeof(AppDesignerWindow));

    /// <summary>Takes the selected members out of their group (Remove from Group).</summary>
    public static readonly RoutedCommand RemoveFromGroup = new(nameof(RemoveFromGroup), typeof(AppDesignerWindow));

    /// <summary>Takes the selected groups apart (Ungroup, Ctrl+Shift+G).</summary>
    public static readonly RoutedCommand UngroupSelection = new(nameof(UngroupSelection), typeof(AppDesignerWindow));

    private readonly TabControl _tabs = new();
    private readonly TextBlock _status = new() { Margin = new Thickness(4, 0, 4, 0) };
    private readonly DesignPaletteView _palette = new();
    private readonly DesignCodeBrowserView _codeBrowser = new();
    private readonly ContentControl _left = new();
    private readonly DesignBrowserView _browser = new();
    private readonly DesignInspectorView _inspector = new();
    private readonly DesignCallbacksView _callbacks = new();
    private readonly TabControl _right = new();
    private readonly Grid _body = new();

    /// <summary>Makes the window over App Designer's settings (WO7), at their factory values when none are given.</summary>
    public AppDesignerWindow(DesignSettings? settings = null)
    {
        Settings = settings ?? new DesignSettings();
        Title = "App Designer";
        Width = 1100;
        Height = 760;
        ShowInTaskbar = true;
        SetResourceReference(BackgroundProperty, ThemeKeys.Window);
        SetResourceReference(ForegroundProperty, ThemeKeys.Text);

        BuildToolstrip();

        var statusBar = new StatusBar();
        statusBar.Items.Add(new StatusBarItem { Content = _status });

        // The library or the code browser, the apps, and the browser over the inspector's two tabs, as App Designer lays them out.
        _left.Content = _palette;
        var inspectorTab = new TabItem { Header = _inspector.Tab, Content = _inspector };
        System.Windows.Automation.AutomationProperties.SetAutomationId(inspectorTab, "PropertiesPanel.Inspector");
        _inspector.TabChanged += (_, _) =>
        {
            // The first tab says what is selected, and the panel hides with nothing selected (_updateContentPaneTitle).
            inspectorTab.Header = _inspector.Tab;
            _right.Visibility = _inspector.HasSelection ? Visibility.Visible : Visibility.Hidden;
        };
        _right.Items.Add(inspectorTab);
        var callbacksTab = new TabItem { Header = "Callbacks", Content = _callbacks };
        System.Windows.Automation.AutomationProperties.SetAutomationId(callbacksTab, "PropertiesPanel.Callbacks");
        _right.Items.Add(callbacksTab);
        System.Windows.Automation.AutomationProperties.SetAutomationId(_right, "PropertiesPanel");
        var right = new Grid();
        var browserRow = new RowDefinition { Height = new GridLength(98) };
        right.RowDefinitions.Add(browserRow);
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var rowSplitter = new GridSplitter { Height = 4, HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetRow(rowSplitter, 1);
        Grid.SetRow(_right, 2);
        right.Children.Add(_browser);
        right.Children.Add(rowSplitter);
        right.Children.Add(_right);
        BuildBrowser(right, browserRow, rowSplitter);

        Grid body = _body;
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        var leftSplitter = new GridSplitter { Width = 4, HorizontalAlignment = HorizontalAlignment.Stretch };
        var rightSplitter = new GridSplitter { Width = 4, HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetColumn(leftSplitter, 1);
        Grid.SetColumn(_tabs, 2);
        Grid.SetColumn(rightSplitter, 3);
        Grid.SetColumn(right, 4);
        body.Children.Add(_left);
        body.Children.Add(leftSplitter);
        body.Children.Add(_tabs);
        body.Children.Add(rightSplitter);
        body.Children.Add(right);

        var root = new DockPanel();
        DockPanel.SetDock(_toolstrip, Dock.Top);
        DockPanel.SetDock(statusBar, Dock.Bottom);
        // The Start Page takes the panes' place while no app is open (W3).
        var center = new Grid();
        center.Children.Add(body);
        center.Children.Add(_startPage);
        root.Children.Add(_toolstrip);
        root.Children.Add(statusBar);
        root.Children.Add(center);
        Content = root;
        BuildStartPage();

        _palette.ChosenChanged += (_, entry) =>
        {
            if (Active is { } app)
            {
                app.Canvas.DrawEntry = entry;
                app.Canvas.Focus();
            }
            else
            {
                _palette.Chosen = null;
            }
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_status, "AppDesigner.Status");
        _inspector.Problem += (_, problem) => ShowAlert("App Designer", problem);
        _inspector.AppDetailsAsked += (_, codeOptions) => ShowAppDetails(codeOptions);
        _inspector.InputArgumentsEdited += (_, _) =>
        {
            if (Active is { } app)
            {
                GoToStartup(app);
            }
        };
        _callbacks.Problem += (_, problem) => ShowAlert(DesignCallbackRules.InvalidNameTitle, problem);
        _callbacks.GoTo += (_, name) => GoToFunction(name);
        BuildAppLayout();
        _codeBrowser.Problem += (_, problem) => _status.Text = problem;
        _codeBrowser.Picked += (_, item) => Picked(item);
        _codeBrowser.GoToRequested += (_, item) => GoTo(item);
        _codeBrowser.RenameRequested += (_, item) => RenameCodeItem(item);
        _codeBrowser.InsertRequested += (_, text) => InsertAtCursor(text);
        _codeBrowser.AddCallbackRequested += (_, _) => ShowAddCallback();
        _codeBrowser.InputArgumentsRequested += (_, _) => ShowCodeOptions();
        _codeBrowser.Added += (_, item) => Declared(item.Name, item.Kind == DesignCodeItemKind.Function);

        _tabs.SelectionChanged += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, _tabs))
            {
                ApplyCanvasOptions();
                _palette.Chosen = null;
                _palette.SetUserEntries(Active?.Document.UserPaletteEntries ?? []);
                _browser.Document = Active?.Document;
                _inspector.Document = Active?.Document;
                _callbacks.Document = Active?.Document;
                _codeBrowser.Document = Active?.Document;
                ShowView();
                ShowState();
                ShowTitle();
            }
        };

        CommandBindings.Add(new CommandBinding(ApplicationCommands.Open, (_, _) => PromptOpen()));
        // A new app has no file yet: its Save asks for one, as R2025b's does.
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, (_, _) => Save(Active),
            (_, e) => e.CanExecute = Active is { Document: { CanSave: true } or { Path: null, CanSaveAs: true } }));
        CommandBindings.Add(new CommandBinding(WorkspaceCommands.SaveAs, (_, _) => PromptSaveAs(Active),
            (_, e) => e.CanExecute = Active is { Document.CanSaveAs: true }));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo, (_, _) => Active?.Document.Undo(),
            (_, e) => e.CanExecute = Active is { Document.CanUndo: true }));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Redo, (_, _) => Active?.Document.Redo(),
            (_, e) => e.CanExecute = Active is { Document.CanRedo: true }));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Delete, (_, _) => Active?.Document.Delete(Active.Document.Selection),
            (_, e) => e.CanExecute = Active is { } a && a.Document.Selection.Any(a.Document.CanDelete)));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Close, (_, _) => CloseTab(Active),
            (_, e) => e.CanExecute = Active is not null));
        // Shift+F7 rests while the debugger is paused in the app (W11), as R2025b's does.
        CommandBindings.Add(new CommandBinding(ToggleView, (_, _) => SetView(Active, Active?.InCodeView == false),
            (_, e) => e.CanExecute = Active is not null && !_debugging));
        CommandBindings.Add(new CommandBinding(AddCallback, (_, _) => ShowAddCallback(),
            (_, e) => e.CanExecute = Active is { Document.Code: not null }));
        CommandBindings.Add(new CommandBinding(AddFunction, (_, _) => Declared(Active?.Document.InsertUserFunction("private"), function: true),
            (_, e) => e.CanExecute = Active is { Document.Code: not null }));
        CommandBindings.Add(new CommandBinding(AddProperty, (_, _) => Declared(Active?.Document.InsertUserProperty("private"), function: false),
            (_, e) => e.CanExecute = Active is { Document.Code: not null }));
        CommandBindings.Add(new CommandBinding(CodeOptions, (_, _) => ShowCodeOptions(),
            (_, e) => e.CanExecute = Active is { Document.Code: not null }));
        CommandBindings.Add(new CommandBinding(AppDetails, (_, _) => ShowAppDetails(codeOptions: false),
            (_, e) => e.CanExecute = Active is not null));
        CommandBindings.Add(new CommandBinding(HelpText, (_, _) => ShowHelpText(),
            (_, e) => e.CanExecute = Active is { Document.Code: not null }));
        CommandBindings.Add(new CommandBinding(SaveCopyAs, (_, _) => PromptSaveCopy(),
            (_, e) => e.CanExecute = Active is { Document.CanSaveAs: true }));
        CommandBindings.Add(new CommandBinding(ExportCode, (_, _) => PromptExport(),
            (_, e) => e.CanExecute = Active is { Document.Code: not null }));
        CommandBindings.Add(new CommandBinding(ConvertTwoPanel, (_, _) => Convert(2),
            (_, e) => e.CanExecute = Active is { Document: { CanConvert: true, HasConvertibleRegions: false } }));
        CommandBindings.Add(new CommandBinding(ConvertThreePanel, (_, _) => Convert(3),
            (_, e) => e.CanExecute = Active is { Document: { CanConvert: true, HasConvertibleRegions: false } }));
        CommandBindings.Add(new CommandBinding(ConvertStandard, (_, _) => Convert(0),
            (_, e) => e.CanExecute = Active is { Document: { CanConvert: true, HasConvertibleRegions: true } }));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Close, Key.W, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Redo, Key.Z, ModifierKeys.Control | ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(ToggleView, Key.F7, ModifierKeys.Shift));
        CommandBindings.Add(new CommandBinding(RunApp, (_, _) => RunActive(), (_, e) => e.CanExecute = Active is not null && !_debugging));
        InputBindings.Add(new KeyBinding(RunApp, Key.F5, ModifierKeys.None));

        // The grouping actions (GroupingAction, open item 150).
        CommandBindings.Add(new CommandBinding(GroupSelection, (_, _) => Active?.Document.Group(), (_, e) => e.CanExecute = Active is { Document.CanGroup: true }));
        CommandBindings.Add(new CommandBinding(AddToGroup, (_, _) => Active?.Document.AddToGroup(), (_, e) => e.CanExecute = Active is { Document.CanAddToGroup: true }));
        CommandBindings.Add(new CommandBinding(RemoveFromGroup, (_, _) => Active?.Document.RemoveFromGroup(), (_, e) => e.CanExecute = Active is { Document.CanRemoveFromGroup: true }));
        CommandBindings.Add(new CommandBinding(UngroupSelection, (_, _) => Active?.Document.Ungroup(), (_, e) => e.CanExecute = Active is { Document.CanUngroup: true }));
        InputBindings.Add(new KeyBinding(GroupSelection, Key.G, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(UngroupSelection, Key.G, ModifierKeys.Control | ModifierKeys.Shift));
        ShowStartPageInPlace();
    }

    /// <summary>Raised after an app was saved, with its file and its class's new text, for an editor tab showing it.</summary>
    public event Action<string, string>? AppSaved;

    private AppTab? Active => (_tabs.SelectedItem as TabItem)?.Tag as AppTab;

    /// <summary>What <see cref="Open"/> answers when the user declined to open the app (the dialog for its unavailable custom components, DS7b): no error, nothing opened.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>
    /// The session's path folders, asked for as an app opens: where a custom component's class and
    /// its <c>resources/appDesigner.json</c> are found besides the app's folder (designer plan DS7b).
    /// Null when the host has no session.
    /// </summary>
    public Func<IReadOnlyList<string>>? PathFolders { get; set; }

    /// <summary>
    /// Runs the saved app at a path in the MATLAB session as App Designer's Run does (designer plan
    /// DS8), told whether the app's code allows one running instance; answers null when it ran, else
    /// why not. Null when there is no session to run it in.
    /// </summary>
    public Func<string, bool, string, bool, Task<DesignRunOutcome>>? Runner { get; set; }

    /// <summary>A picture of the running app's window, as PNG bytes, or null (<c>captureScreenshot</c>).</summary>
    public Func<string, Task<byte[]?>>? Capturer { get; set; }

    private IEnumerable<AppTab> Apps => _tabs.Items.OfType<TabItem>().Select(static t => (AppTab)t.Tag);

    /// <summary>
    /// Shows the app <paramref name="path"/> in its tab, opening it first when it is not open: the
    /// window comes up, as App Designer's does when an app is opened.
    /// </summary>
    /// <returns>Null when it opened, else why it could not.</returns>
    public string? Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (Apps.FirstOrDefault(a => string.Equals(a.Document.Path, path, StringComparison.OrdinalIgnoreCase)) is { } open)
        {
            _tabs.SelectedItem = open.Item;
            Bring();
            return null;
        }

        DesignDocument document;
        try
        {
            document = DesignDocument.Open(path, PathFolders?.Invoke());
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return ex.Message;
        }

        // Custom components whose class is missing or errored: App Designer asks before it opens
        // the app without them, and removes them when told to (designer plan DS7b).
        if (document.MissingUserComponents.Count > 0 || document.ErroredUserComponents.Count > 0)
        {
            if (!AppDesignerDialogs.ConfirmUnavailableUserComponents(this, document.ErroredUserComponents, document.MissingUserComponents))
            {
                document.Dispose();
                return Cancelled;
            }

            document.RemoveUnavailableUserComponents();
        }

        AddApp(document);
        RememberRecent(document);
        return null;
    }

    /// <summary>Gives <paramref name="document"/> a tab of its own, shown, with its canvas, its Code View and the switch between them.</summary>
    private void AddApp(DesignDocument document)
    {
        var canvas = new DesignCanvas { Clipboard = _clipboard, Document = document };
        var codeView = new DesignCodeView { Document = document, Visibility = Visibility.Collapsed };
        var designButton = new ToggleButton { Content = "Design View", IsChecked = true, Margin = new Thickness(0, 0, 2, 0), Padding = new Thickness(10, 2, 10, 2) };
        var codeButton = new ToggleButton { Content = "Code View", Padding = new Thickness(10, 2, 10, 2), ToolTip = "Switch to Code View (Shift + F7)" };
        System.Windows.Automation.AutomationProperties.SetAutomationId(designButton, "DesignView");
        System.Windows.Automation.AutomationProperties.SetAutomationId(codeButton, "CodeView.Switch");
        var band = new StackPanel { Orientation = Orientation.Horizontal, Height = 36, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        band.Children.Add(designButton);
        band.Children.Add(codeButton);
        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(canvas, 1);
        Grid.SetRow(codeView, 1);
        content.Children.Add(band);
        content.Children.Add(canvas);
        content.Children.Add(codeView);
        var item = new TabItem { Content = content, ToolTip = document.Path ?? document.Code?.ClassName };
        var app = new AppTab(document, canvas, codeView, designButton, codeButton, item);
        item.Tag = app;
        // The state, not the click: a toggle set by the keyboard or through automation raises no Click.
        designButton.Checked += (_, _) => SetView(app, code: false);
        codeButton.Checked += (_, _) => SetView(app, code: true);
        designButton.Unchecked += (_, _) => { if (codeButton.IsChecked != true) { designButton.IsChecked = true; } };
        codeButton.Unchecked += (_, _) => { if (designButton.IsChecked != true) { codeButton.IsChecked = true; } };
        AttachTab(app);
        AddArrangeKeys(canvas);
        AddStructureKeys(canvas);
        AddClipboardKeys(canvas);
        WatchView(canvas);
        canvas.Alerted += (_, alert) => ShowAlert(alert.Title, alert.Message);
        canvas.CallbackGoTo += (_, name) => GoToFunction(name);
        canvas.CallbackSelectAsked += (_, peers) => ShowSelectCallback(peers);
        canvas.HelpAsked += (_, _) => OpenHelp("apps-designer");
        canvas.DesignChanged += (_, _) =>
        {
            ShowHeader(app);
            AppLayoutChanged();
            if (ReferenceEquals(Active, app))
            {
                ShowState();
                ShowTitle();
                ShowArrangeState();
            }

            CommandManager.InvalidateRequerySuggested();
        };
        canvas.DrawEnded += (_, _) =>
        {
            if (ReferenceEquals(Active, app))
            {
                _palette.Chosen = null;
            }
        };
        canvas.Problem += (_, problem) =>
        {
            if (ReferenceEquals(Active, app))
            {
                _status.Text = problem;
            }
        };
        codeView.Problem += (_, problem) =>
        {
            if (ReferenceEquals(Active, app))
            {
                _status.Text = problem;
            }
        };
        WireDebug(app);
        document.Changed += (_, _) =>
        {
            ShowHeader(app);
            if (ReferenceEquals(Active, app))
            {
                ShowView();
                ShowState();
                ShowTitle();
            }
        };
        // The tab's title and its close button, as R2025b's document tabs have them.
        var close = new Button { Content = "×", Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(6, 0, 0, 0), BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, ToolTip = "Close", Focusable = false };
        close.SetResourceReference(ForegroundProperty, ThemeKeys.Text);
        System.Windows.Automation.AutomationProperties.SetAutomationId(close, "AppTab.CloseButton");
        close.Click += (_, _) => CloseTab(app);
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(app.HeaderText);
        header.Children.Add(close);
        item.Header = header;
        ShowHeader(app);
        _tabs.Items.Add(item);
        _tabs.SelectedItem = item;
        _browser.Document = document;
        _inspector.Document = document;
        _callbacks.Document = document;
        _codeBrowser.Document = document;
        ShowStartPageInPlace();
        ApplyCanvasOptions();
        ShowView();
        ShowState();
        ShowTitle();
        Bring();
        canvas.Focus();
    }

    /// <summary>Brings the window up as it is - on the Start Page when no app is open: what <c>appdesigner</c> with no file does.</summary>
    public void ShowWindow()
    {
        ShowStartPageInPlace();
        Bring();
        ShowView();
        ShowState();
        ShowTitle();
    }

    private void PromptOpen()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open app",
            Filter = "MATLAB Apps (*.mlapp;*.m)|*.mlapp;*.m|MATLAB App (*.mlapp)|*.mlapp|MATLAB App, plain text (*.m)|*.m",
            InitialDirectory = Active?.Document.Path is { } current ? Path.GetDirectoryName(current) : null,
        };
        if (dialog.ShowDialog(this) == true && Open(dialog.FileName) is { } refused && refused != Cancelled)
        {
            MessageBox.Show(this, refused, "App Designer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Bring()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    private void ApplyCanvasOptions()
    {
        if (Active is not { } app)
        {
            return;
        }

        app.Canvas.ShowGrid = Settings.GetBool(DesignSettings.ShowGrid);
        app.Canvas.SnapToGrid = Settings.GetBool(DesignSettings.SnapToGrid);
        app.Canvas.ShowAlignmentHints = Settings.GetBool(DesignSettings.ShowAlignmentHints);
        app.Canvas.ShowResizingHints = Settings.GetBool(DesignSettings.ShowResizingHints);
        app.Canvas.GridInterval = Settings.GridIntervalValue;
    }

    // --- the two views (DS5) -----------------------------------------------------------------------

    /// <summary>Shows <paramref name="app"/> in its Code View (<paramref name="code"/>) or its Design View.</summary>
    private void SetView(AppTab? app, bool code)
    {
        if (app is null)
        {
            return;
        }

        app.InCodeView = code;
        app.Canvas.Visibility = code ? Visibility.Collapsed : Visibility.Visible;
        app.CodeView.Visibility = code ? Visibility.Visible : Visibility.Collapsed;
        app.DesignButton.IsChecked = !code;
        app.CodeButton.IsChecked = code;
        app.DesignButton.ToolTip = code ? "Switch to Design View (Shift + F7)" : null;
        app.CodeButton.ToolTip = code ? null : "Switch to Code View (Shift + F7)";
        if (code)
        {
            app.CodeView.Editor.FocusEditor();
        }
        else
        {
            app.CodeView.Push();
            app.Canvas.Focus();
        }

        if (ReferenceEquals(Active, app))
        {
            ShowView();
        }
    }

    /// <summary>The left pane and the toolstrip follow the active app's view: the library and the CANVAS tab in the Design View, the code browser and the EDITOR tab in the Code View.</summary>
    private void ShowView()
    {
        bool code = Active?.InCodeView == true;
        _left.Content = code ? _codePane : _palette;
        ShowToolstripState();
        if (code)
        {
            ShowAppLayout();
        }
    }

    /// <summary>Shows the Code View with the caret in the function <paramref name="name"/>.</summary>
    private void GoToFunction(string name)
    {
        if (Active is not { } app)
        {
            return;
        }

        SetView(app, code: true);
        if (!app.CodeView.GoToFunction(name))
        {
            _status.Text = $"The code has no function {name}.";
        }
    }

    /// <summary>A code browser row picked: the Code View scrolls to its code, without taking the caret (App Designer's "selection" go-to).</summary>
    private void Picked(DesignCodeItem item)
    {
        if (Active is not { InCodeView: true } app)
        {
            return;
        }

        if (item.Kind == DesignCodeItemKind.Property)
        {
            app.CodeView.GoToProperty(item.Name, selectionOnly: true);
        }
        else
        {
            app.CodeView.GoToFunction(item.Name, selectionOnly: true);
        }
    }

    private void GoTo(DesignCodeItem item)
    {
        if (Active is not { } app)
        {
            return;
        }

        SetView(app, code: true);
        bool found = item.Kind == DesignCodeItemKind.Property ? app.CodeView.GoToProperty(item.Name) : app.CodeView.GoToFunction(item.Name);
        if (!found)
        {
            _status.Text = $"The code has no {item.Name}.";
        }
    }

    /// <summary>After Add Function (<paramref name="function"/>) or Add Property: the Code View, with the new name selected so that typing replaces it.</summary>
    private void Declared(string? name, bool function)
    {
        if (name is null || Active is not { } app)
        {
            return;
        }

        SetView(app, code: true);
        app.CodeView.SelectDeclaration(name, function);
    }

    private void InsertAtCursor(string text)
    {
        if (Active is not { } app)
        {
            return;
        }

        SetView(app, code: true);
        if (!app.CodeView.InsertAtCaret(text))
        {
            _status.Text = "The cursor is on a line App Designer generates; put it in your code to insert there.";
        }
    }

    private void ShowAddCallback()
    {
        if (Active is not { Document: { Code: not null } document } app)
        {
            return;
        }

        app.CodeView.Push();
        if (AppDesignerDialogs.AddCallback(this, document, document.Selection is [var one] ? one : null) is { } name)
        {
            SetView(app, code: true);
            _codeBrowser.ShowCallback(name);
            app.CodeView.GoToFunction(name);
        }
    }

    /// <summary>Select existing callback... (<c>SharedCallbackDialog</c>): the property and the function, the function not shown, as <c>AttachCallbackCommand</c> switches nothing.</summary>
    private void ShowSelectCallback(IReadOnlyList<DesignComponent> peers)
    {
        if (Active is not { Document: { Code: not null } document } app)
        {
            return;
        }

        app.CodeView.Push();
        if (AppDesignerDialogs.SelectCallback(this, document, peers) is { } chosen
            && document.MassEditCallback([.. peers], chosen.Name, chosen.Property, detachPrevious: false, out _) is { } refused)
        {
            ShowAlert(DesignCallbackRules.InvalidNameTitle, refused);
        }
    }

    private void ShowCodeOptions() => ShowAppDetails(codeOptions: true);

    /// <summary>App Details, at Code Options when the input arguments asked for it (<c>AppDetailsAction</c>).</summary>
    private void ShowAppDetails(bool codeOptions)
    {
        if (Active is not { Document: var document } app)
        {
            return;
        }

        app.CodeView.Push();
        if (AppDesignerDialogs.AppDetails(this, document, codeOptions))
        {
            GoToStartup(app);
        }
    }

    /// <summary>App Designer lands in the Code View, at the startup function, after a change of the input arguments (<c>InputArgumentsCommand</c>); the other details switch nothing.</summary>
    private void GoToStartup(AppTab app)
    {
        SetView(app, code: true);
        if (app.Document.Callbacks.FirstOrDefault(static c => c.IsStartup) is { } startup)
        {
            app.CodeView.GoToFunction(startup.Name);
        }
    }

    /// <summary>
    /// Convert (<c>ResponsifyAppAction</c>): a new app, not saved, in a tab of its own, named for this one; the app it came from stays as it is. R2025b walks through what changed in a tutorial;
    /// here the status line says it, and an alert says what a figure's callback lost.
    /// </summary>
    private void Convert(int panels)
    {
        if (Active is not { Document: var document } app || !app.CodeView.Push())
        {
            return;
        }

        string name = DesignConversion.ConvertedName(document.AppName, Apps.Select(static a => a.Document.AppName));
        try
        {
            DesignConverted made = panels == 0 ? document.ConvertToStandard(name) : document.ConvertToPanels(panels, name);
            AddApp(made.Document);
            if (panels != 0)
            {
                _status.Text = DesignConversion.Converted;
            }

            if (made.Detached is not null)
            {
                ShowAlert("App Designer", DesignConversion.SizeChangedDetached);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException)
        {
            ShowAlert("App Designer", ex.Message);
        }
    }

    /// <summary>Save Copy As... (<c>SaveCopyAsCommand</c>): this app stays what it is; its own file is refused, with the dialog offered again (<c>_showCannotCopyOverAppDialog</c>).</summary>
    private void PromptSaveCopy()
    {
        if (Active is not { Document: var document } app || !app.CodeView.Push())
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = DesignConversion.SaveCopyTitle,
            Filter = "MATLAB App (*.mlapp)|*.mlapp|MATLAB App, plain text (*.m)|*.m",
            FilterIndex = document.Format == DesignFileFormat.PlainText ? 2 : 1,
            FileName = document.AppName,
            InitialDirectory = document.Path is { } here ? Path.GetDirectoryName(here) : _currentFolder,
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) != true || dialog.FileName is not { Length: > 0 } chosen)
        {
            return;
        }

        string full = Path.GetFullPath(chosen);
        if (document.Path is { } own && string.Equals(own, full, StringComparison.OrdinalIgnoreCase))
        {
            if (MessageBox.Show(this, DesignConversion.CopyOverApp, "App Designer", MessageBoxButton.OKCancel, MessageBoxImage.Error) == MessageBoxResult.OK)
            {
                PromptSaveCopy();
            }

            return;
        }

        try
        {
            document.SaveCopy(full);
            _status.Text = $"Saved a copy as {full}.";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or InvalidDataException)
        {
            ShowAlert("App Designer", ex.Message);
        }
    }

    /// <summary>Export to .m File... (<c>ExportModelCommand</c>): the class alone, named <c>app_exported</c> to start with.</summary>
    private void PromptExport()
    {
        if (Active is not { Document: var document } app || !app.CodeView.Push())
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "MATLAB Code File (*.m)|*.m",
            FileName = document.AppName + DesignConversion.ExportSuffix,
            InitialDirectory = document.Path is { } here ? Path.GetDirectoryName(here) : _currentFolder,
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) != true || dialog.FileName is not { Length: > 0 } chosen)
        {
            return;
        }

        try
        {
            document.Export(chosen);
            _status.Text = $"Exported {Path.GetFullPath(chosen)}.";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            ShowAlert("App Designer", ex.Message);
        }
    }

    /// <summary>The Help Text dialog (<c>HelpCommentsAction</c>).</summary>
    private void ShowHelpText()
    {
        if (Active is { Document: { Code: not null } document } app)
        {
            app.CodeView.Push();
            AppDesignerDialogs.HelpText(this, document);
        }
    }

    /// <summary>Renames a callback, function or property from the code browser after showing what changes.</summary>
    private void RenameCodeItem(DesignCodeItem item)
    {
        if (Active is not { Document: var document } app || document.Code is null)
        {
            return;
        }

        app.CodeView.Push();
        DesignCallback? callback = item.Kind == DesignCodeItemKind.Callback ? document.Callbacks.FirstOrDefault(c => c.Name == item.Name) : null;
        if (item.Kind == DesignCodeItemKind.Callback && callback is null)
        {
            return;
        }

        string? name = AppDesignerDialogs.Rename(this, $"Rename {item.Name}", item.Name,
            candidate => document.WhyNotFunctionName(candidate, item.Name),
            candidate => Preview(document.RenamePreview(item.Name, Probe(item.Name, candidate), item.Kind == DesignCodeItemKind.Property), item.Name, candidate));
        if (name is null)
        {
            return;
        }

        string? refused = item.Kind switch
        {
            DesignCodeItemKind.Callback => document.RenameCallback(callback!, name),
            DesignCodeItemKind.Function => document.RenameUserFunction(item.Name, name),
            _ => document.RenameUserProperty(item.Name, name),
        };
        if (refused is not null)
        {
            _status.Text = refused;
        }
        else
        {
            SetView(app, code: true);
            if (item.Kind == DesignCodeItemKind.Property)
            {
                app.CodeView.GoToProperty(name);
            }
            else
            {
                app.CodeView.GoToFunction(name);
            }
        }
    }

    /// <summary>The name a preview is worked out for: <paramref name="candidate"/> once it is a name of its own; before that, one that changes every line the rename would, so the lines show.</summary>
    private static string Probe(string current, string candidate) =>
        candidate.Length > 0 && candidate != current ? candidate : current + "_";

    /// <summary>The preview's lines: each line the rename changes, with its number, and as it would read once <paramref name="to"/> is a name of its own.</summary>
    private static IReadOnlyList<string> Preview(IReadOnlyList<(int Line, string Before, string After)> changes, string from, string to) =>
        changes.Select(c => $"{c.Line}: {c.Before.Trim()}" + (to.Length > 0 && to != from ? $"  →  {c.After.Trim()}" : string.Empty)).ToArray();

    /// <summary>What is selected, and what the app's state allows, on the status line.</summary>
    private void ShowState()
    {
        if (Active is not { Document: var document })
        {
            _status.Text = "No app is open. Open… opens one, as appdesigner('app.mlapp') at the prompt does.";
            return;
        }

        string selected = document.Picks switch
        {
            [{ Group: { IsLabeled: true, Field: { } field, Label: { } label } }] => $"{field.CodeName} ({field.Type}) with its label {label.CodeName}"
                + (document.Runtime.PositionOf(field) is { } fp ? $"  Position [{Whole(fp.X)} {Whole(fp.Y)} {Whole(fp.Width)} {Whole(fp.Height)}]" : string.Empty),
            [{ Group: { } group }] => $"A group of {group.AllComponents().Count()} components",
            [{ Component: { } one }] => $"{one.CodeName} ({one.Type})" + (document.Runtime.PositionOf(one) is { } p && one.Parent is not null
                ? $"  Position [{Whole(p.X)} {Whole(p.Y)} {Whole(p.Width)} {Whole(p.Height)}]"
                : string.Empty),
            { Count: var n } => $"{n} components selected",
        };
        if (Active.Canvas.Refusal is { } refused)
        {
            _status.Text = refused;
            return;
        }

        if (Active.Canvas.DrawEntry is { } drawing)
        {
            _status.Text = $"{drawing.Name}: click on the canvas to place it, or drag to draw its size. Escape cancels.";
            return;
        }

        string note = !document.CanSave && document.ReadProblems.Count > 0
            ? "  View only: " + document.SaveRefusal
            : document.Code is null ? "  View only: " + document.CodeProblem
            : document.Runtime.Problems.Count > 0
                ? $"  The canvas could not make everything: {string.Join(" ", document.Runtime.Problems)}"
                : string.Empty;
        _status.Text = selected + note;

        static string Whole(double v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Save As: the app under another name, as an <c>.mlapp</c> or as a plain-text <c>.m</c> app (designer plan DS7a).</summary>
    private void PromptSaveAs(AppTab? app)
    {
        if (app is null)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save app as",
            Filter = "MATLAB App (*.mlapp)|*.mlapp|MATLAB App, plain text (*.m)|*.m",
            FilterIndex = app.Document.Format == DesignFileFormat.PlainText ? 2 : 1,
            FileName = app.Document.Path is { } current ? Path.GetFileName(current) : app.Document.Code?.ClassName,
            InitialDirectory = app.Document.Path is { } here ? Path.GetDirectoryName(here) : _currentFolder,
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) != true || dialog.FileName is not { Length: > 0 } chosen)
        {
            return;
        }

        // A file open in another tab is not written over from this one: the two tabs would then be
        // one file's, and the other's design would write over this save on its next Save.
        string full = Path.GetFullPath(chosen);
        if (Apps.FirstOrDefault(a => !ReferenceEquals(a, app) && string.Equals(a.Document.Path, full, StringComparison.OrdinalIgnoreCase)) is { } other)
        {
            MessageBox.Show(this, $"{Path.GetFileName(full)} is open in another tab. Close that tab first, then save as this name.",
                "App Designer", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            if (!app.CodeView.Push())
            {
                return;
            }

            Saving(app, () => _status.Text = app.Document.SaveAs(chosen));
            app.Item.ToolTip = app.Document.Path;
            ShowHeader(app);
            ShowTitle();
            AppSaved?.Invoke(app.Document.Path!, app.Document.Text);
            RememberRecent(app.Document);
            ShowState();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(this, ex.Message, "App Designer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Run (designer plan DS8): the app is saved first when it has changes - a new app through Save As,
    /// as App Designer asks for a file before its first run - then run in the MATLAB session as a call
    /// of its name would run it, and the status line says what the session answered.
    /// </summary>
    private string? _runArgumentError;

    /// <summary>The Run drop-down's sets for the active app; the error of its last Run stands on the field until the drop-down closes.</summary>
    private IReadOnlyList<ToolstripPopupEntry>? RunEntries()
    {
        if (Active is not { Document: var document })
        {
            return [];
        }

        string? error = _runArgumentError;
        _runArgumentError = null;
        return DesignToolstrip.RunEntries(document.AppName, document.RunArguments.All, document.InputParameters, error, enabled: !_running);
    }

    /// <summary>An argument set's item: Run with it, or its button: taken out (<c>DeleteRunCommandWithArgs</c>), one step, and the drop-down made again.</summary>
    private void RunSetChosen(int index, string? value)
    {
        if (Active is not { Document: var document } || index >= document.RunArguments.All.Count)
        {
            return;
        }

        string set = document.RunArguments.All[index];
        if (value == ToolstripPopupEntry.RemoveValue)
        {
            document.RemoveRunArguments(set);
            ShowToolstripState();
            Dispatcher.BeginInvoke(() => _toolstrip.OpenPopupOf("AD_RUN"), System.Windows.Threading.DispatcherPriority.Loaded);
            return;
        }

        document.UseRunArguments(set);
        RunActive();
    }

    /// <summary>The field's text: Run with it, blanks trimmed (<c>_buildRunEditableArgumentItem</c>).</summary>
    private void RunTyped(string text)
    {
        if (Active is { Document: var document })
        {
            document.UseRunArguments(text.Trim());
            RunActive();
        }
    }

    private async void RunActive() => await RunActiveAsync(changeFolder: false);

    private async Task RunActiveAsync(bool changeFolder)
    {
        if (Active is not { } app)
        {
            return;
        }

        if (Runner is not { } runner)
        {
            _status.Text = "There is no MATLAB session to run the app in.";
            return;
        }

        if (!SaveBeforeRun(app))
        {
            return;
        }

        string path = app.Document.Path!;
        if (!File.Exists(path))
        {
            // AppModel.runApp: an app whose file was deleted or renamed on disk is saved again,
            // silently, before it is run; a save that fails is the run's refusal.
            try
            {
                Saving(app, () => app.Document.SaveAs(path));
                ShowHeader(app);
                AppSaved?.Invoke(path, app.Document.Text);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or InvalidDataException)
            {
                _status.Text = $"Error running '{Path.GetFileName(path)}'. {path} not found.";
                return;
            }
        }

        _status.Text = $"Running {Path.GetFileName(path)}…";
        _running = true;
        ShowToolstripState();
        DesignRunOutcome outcome;
        try
        {
            outcome = await runner(path, app.Document.SingleInstance, app.Document.RunArguments.MostRecent, changeFolder);
        }
        finally
        {
            _running = false;
            ShowToolstripState();
        }

        // The status line has one line: the message's first; the console has the whole of it.
        string? refused = outcome.Message;
        _status.Text = refused is null ? $"Running {Path.GetFileNameWithoutExtension(path)}."
            : refused.Split('\n').Select(static l => l.Trim()).FirstOrDefault(static l => l.Length > 0) ?? refused;
        if (refused is null)
        {
            await CaptureThumbnail(app, path);
            return;
        }

        if (outcome.Identifier == DesignRunOutcome.ArgumentError)
        {
            // RunSection: the drop-down opens by itself, its field holding the set, the error on it.
            _runArgumentError = refused;
            _toolstrip.SelectedTab = app.InCodeView ? DesignToolstrip.EditorTab : DesignToolstrip.CanvasTab;
            await Dispatcher.InvokeAsync(() => _toolstrip.OpenPopupOf("AD_RUN"), System.Windows.Threading.DispatcherPriority.Loaded);
        }
        else if (outcome.Identifier == DesignRunOutcome.ShadowedByCurrentFolder)
        {
            // AppModel._runCallback: Change Folder runs it again from the app's own folder.
            if (AppDesignerDialogs.ChangeFolder(this, refused))
            {
                await RunActiveAsync(changeFolder: true);
            }
        }
        else
        {
            ShowAlert("App Designer", refused);
        }
    }

    /// <summary>
    /// The picture Run takes (<c>runAppHelper</c>): the app's window, when its thumbnail is automatic and it has not run since it was saved. It goes straight into the file, as
    /// <c>captureScreenshot</c> writes it; a capture that fails goes unnoticed.
    /// </summary>
    private async Task CaptureThumbnail(AppTab app, string path)
    {
        if (app.Document.WantsThumbnailCapture && Capturer is { } capture)
        {
            try
            {
                if (await capture(path) is { } picture)
                {
                    Saving(app, () => app.Document.TakeThumbnail(picture));
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or InvalidDataException)
            {
                _status.Text = $"Running {Path.GetFileNameWithoutExtension(path)}.";
            }
        }

        app.Document.RanSinceSave();
    }

    /// <summary>
    /// The save before an app is run or shared (<c>AppModel.runApp</c>, the Share items): Save As for an app
    /// with no file yet, else the Code View's text taken first, so that the save sees every change, and the
    /// file written when it differs; false when the user cancelled or the text would not do.
    /// </summary>
    private bool SaveBeforeRun(AppTab app)
    {
        if (app.Document.Path is null)
        {
            PromptSaveAs(app);
            return app.Document.Path is not null;
        }

        return app.CodeView.Push() && (!app.Document.IsDirty || Save(app));
    }

    private bool Save(AppTab? app)
    {
        if (app is null)
        {
            return false;
        }

        if (app.Document.Path is null)
        {
            // A new app: Save is Save As until it has a file.
            PromptSaveAs(app);
            return app.Document is { Path: not null, IsDirty: false };
        }

        try
        {
            // Saving while paused in the app quits debugging first (R2025b's SaveAction: debugExit before performSave, W11).
            if (_debugging && ReferenceEquals(app, _pausedApp))
            {
                _debugger?.Quit();
                app.CodeView.Locked = false;
            }

            // Text the document would not take is not saved over: the status line says what to put right.
            if (!app.CodeView.Push())
            {
                return false;
            }

            string? fileBefore = FileTextOf(app); // the lines the breakpoints sit on, before the file is written again (W11)
            Saving(app, () => _status.Text = app.Document.Save());
            AppSaved?.Invoke(app.Document.Path!, app.Document.Text);
            ReanchorAfterSave(app, fileBefore);
            RememberRecent(app.Document);
            ShowHeader(app);
            ShowTitle();
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(this, ex.Message, "App Designer", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    /// <summary>Asks about unsaved edits, and about code the document would not take; false when the user cancels.</summary>
    private bool ConfirmClose(AppTab app)
    {
        if (!app.CodeView.Push())
        {
            MessageBoxResult keep = MessageBox.Show(this,
                $"The code typed into {app.FileName} no longer matches the app's structure, so it cannot be kept. Close anyway and lose it?",
                "App Designer", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (keep != MessageBoxResult.Yes)
            {
                return false;
            }
        }

        if (!app.Document.IsDirty)
        {
            return true;
        }

        MessageBoxResult answer = MessageBox.Show(this,
            $"Save the changes to {app.FileName}?", "App Designer",
            app.Document.CanSave || app.Document is { Path: null, CanSaveAs: true } ? MessageBoxButton.YesNoCancel : MessageBoxButton.OKCancel, MessageBoxImage.Question);
        return answer switch
        {
            MessageBoxResult.Yes => Save(app),
            MessageBoxResult.No or MessageBoxResult.OK => true,
            _ => false,
        };
    }

    private void CloseTab(AppTab? app)
    {
        if (app is null || !ConfirmClose(app))
        {
            return;
        }

        _tabs.Items.Remove(app.Item);
        DetachTab(app);
        app.Canvas.Document = null;
        app.CodeView.Document = null;
        if (ReferenceEquals(_inspector.Document, app.Document))
        {
            _inspector.Document = null;
            _callbacks.Document = null;
            _browser.Document = null;
            _codeBrowser.Document = null;
        }

        app.Document.Dispose();

        // With the last app closed the window stays, on the Start Page (R2025b opens it then).
        ShowStartPageInPlace();
        ShowView();
        ShowState();
        ShowTitle();
    }

    /// <inheritdoc />
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        foreach (AppTab app in Apps.ToList())
        {
            if (!ConfirmClose(app))
            {
                e.Cancel = true;
                return;
            }
        }

        _inspector.Document = null;
        _callbacks.Document = null;
        _browser.Document = null;
        _codeBrowser.Document = null;
        RememberPlace();
        foreach (AppTab app in Apps.ToList())
        {
            DetachTab(app);
            app.Canvas.Document = null;
            app.CodeView.Document = null;
            app.Document.Dispose();
        }

        _tabs.Items.Clear();
    }

    /// <summary>The tab's title as the app now is, for the eye and for UI Automation.</summary>
    private static void ShowHeader(AppTab app)
    {
        app.HeaderText.Text = app.Header;
        System.Windows.Automation.AutomationProperties.SetName(app.Item, app.Header);
        if (app.Item.Header is StackPanel { Children: [_, Button close] })
        {
            System.Windows.Automation.AutomationProperties.SetName(close, "Close " + app.FileName);
        }
    }

    private sealed record AppTab(DesignDocument Document, DesignCanvas Canvas, DesignCodeView CodeView, ToggleButton DesignButton, ToggleButton CodeButton, TabItem Item)
    {
        public bool InCodeView { get; set; }

        /// <summary>The tab's title, beside its close button.</summary>
        public TextBlock HeaderText { get; } = new() { VerticalAlignment = VerticalAlignment.Center };

        /// <summary>The watch on the app's file for a change made outside (W2), or null.</summary>
        public FileSystemWatcher? Watcher { get; set; }

        /// <summary>Until when a change seen on the file is this window's own save.</summary>
        public DateTime SavedUntil { get; set; }

        /// <summary>The file's last write already saved here or asked about: one write is asked about once, however many changes the watch reports for it.</summary>
        public DateTime SeenWrite { get; set; }

        /// <summary>Whether a reload prompt for the file is already up or waiting.</summary>
        public bool Prompting { get; set; }

        /// <summary>The app's file name; a new app's is its name and the extension it will be saved with (<c>app1.mlapp</c>).</summary>
        public string FileName => Document.Path is { } path ? Path.GetFileName(path) : Document.Code?.ClassName + (Document.Format == DesignFileFormat.PlainText ? ".m" : ".mlapp");

        /// <summary>The tab's title, as R2025b's <c>AppViewController</c> writes it: the file name, and "*" when it has unsaved changes.</summary>
        public string Header => FileName + (Document.IsDirty ? "*" : string.Empty);
    }
}
