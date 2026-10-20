using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using JGraph.Controls.Designer;
using JGraph.Controls.Themes;
using JGraph.Scripting.Designer;

namespace JGraph.Application.Scripting;

/// <summary>
/// The visual designer's window (designer plan DS2, decision DD3): like R2025b's App Designer, its own
/// top-level window beside the desktop, with a tab per open app. Each tab shows the app in one of
/// two views, switched by the buttons above it (Shift+F7): the Design View, the app's canvas with
/// App Designer's component library on the left; and the Code View (DS5), the app's class with the
/// code browser - callbacks, functions, properties - on the left. The component browser, over the
/// inspector and the Callbacks tab, is on the right in both. The toolbar is App Designer's canvas
/// tab (save, undo and redo, the grid, the alignment guides) and the Editor tab's insertions: a
/// callback, a function, a property, the app's input arguments and the single-instance setting.
/// </summary>
internal sealed class AppDesignerWindow : Window
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

    private readonly TabControl _tabs = new();
    private readonly TextBlock _status = new() { Margin = new Thickness(4, 0, 4, 0) };
    private readonly ToggleButton _showGrid = new() { Content = "Show Grid", ToolTip = "Show a grid on the canvas; moves and resizes snap to it" };
    private readonly ToggleButton _snap = new() { Content = "Snap to Grid", IsChecked = true, ToolTip = "Snap to the grid while it is shown" };
    private readonly ComboBox _interval = new() { Width = 56, ToolTip = "Grid interval, in pixels" };
    private readonly ToggleButton _hints = new() { Content = "Alignment Hints", IsChecked = true, ToolTip = "Show alignment guides and snap to them" };
    private readonly ToggleButton _singleInstance = new() { Content = "Single Running Instance", ToolTip = "App allows only one running instance at a time." };
    private readonly DesignPaletteView _palette = new();
    private readonly DesignCodeBrowserView _codeBrowser = new();
    private readonly ContentControl _left = new();
    private readonly DesignBrowserView _browser = new();
    private readonly DesignInspectorView _inspector = new();
    private readonly DesignCallbacksView _callbacks = new();
    private readonly TabControl _right = new();
    private bool _settingSingleInstance;

    public AppDesignerWindow()
    {
        Title = "App Designer";
        Width = 1100;
        Height = 760;
        ShowInTaskbar = true;
        SetResourceReference(BackgroundProperty, ThemeKeys.Window);
        SetResourceReference(ForegroundProperty, ThemeKeys.Text);

        for (double i = DesignGeometry.GridMinimum; i <= DesignGeometry.GridMaximum; i += DesignGeometry.GridStep)
        {
            _interval.Items.Add(i);
        }

        _interval.SelectedItem = DesignGeometry.GridInterval;
        // The state, not the click: a toggle set by the keyboard or through automation raises no Click.
        foreach (ToggleButton toggle in (ToggleButton[])[_showGrid, _snap, _hints])
        {
            toggle.Checked += (_, _) => ApplyCanvasOptions();
            toggle.Unchecked += (_, _) => ApplyCanvasOptions();
        }
        _interval.SelectionChanged += (_, _) => ApplyCanvasOptions();
        _singleInstance.Checked += (_, _) => SetSingleInstance(true);
        _singleInstance.Unchecked += (_, _) => SetSingleInstance(false);
        System.Windows.Automation.AutomationProperties.SetAutomationId(_singleInstance, "SingleRunningInstance");

        var toolbar = new ToolBar();
        toolbar.Items.Add(new Button { Content = "Open…", Command = ApplicationCommands.Open, ToolTip = "Open an app (Ctrl+O)" });
        toolbar.Items.Add(new Button { Content = "Save", Command = ApplicationCommands.Save, ToolTip = "Save the app (Ctrl+S)" });
        toolbar.Items.Add(new Separator());
        toolbar.Items.Add(new Button { Content = "Undo", Command = ApplicationCommands.Undo, ToolTip = "Undo (Ctrl+Z)" });
        toolbar.Items.Add(new Button { Content = "Redo", Command = ApplicationCommands.Redo, ToolTip = "Redo (Ctrl+Y)" });
        toolbar.Items.Add(new Button { Content = "Delete", Command = ApplicationCommands.Delete, ToolTip = "Delete the selected components (Delete)" });
        toolbar.Items.Add(new Separator());
        toolbar.Items.Add(_showGrid);
        toolbar.Items.Add(_snap);
        toolbar.Items.Add(new TextBlock { Text = "Interval", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0) });
        toolbar.Items.Add(_interval);
        toolbar.Items.Add(new Separator());
        toolbar.Items.Add(_hints);
        toolbar.Items.Add(new Separator());
        toolbar.Items.Add(new Button { Content = "Callback", Command = AddCallback, ToolTip = "Add a callback function to respond to user interactions" });
        toolbar.Items.Add(new Button { Content = "Function", Command = AddFunction, ToolTip = "Add a function to organize your code into helper or utility functions" });
        toolbar.Items.Add(new Button { Content = "Property", Command = AddProperty, ToolTip = "Add a property to create a variable to store and share data between callbacks and functions" });
        toolbar.Items.Add(new Button { Content = "App Input Arguments…", Command = CodeOptions, ToolTip = "Allow app to take in input arguments" });
        toolbar.Items.Add(_singleInstance);

        // The toolbar's commands are the window's: routed from the focused element, Undo would reach
        // an inspector text box's own (empty) undo instead of the design's.
        foreach (Button button in toolbar.Items.OfType<Button>())
        {
            button.CommandTarget = this;
        }

        var statusBar = new StatusBar();
        statusBar.Items.Add(new StatusBarItem { Content = _status });

        // The library or the code browser, the apps, and the browser over the inspector's two tabs, as App Designer lays them out.
        _left.Content = _palette;
        _right.Items.Add(new TabItem { Header = "Inspector", Content = _inspector });
        _right.Items.Add(new TabItem { Header = "Callbacks", Content = _callbacks });
        System.Windows.Automation.AutomationProperties.SetAutomationId(_right, "PropertiesPanel");
        var right = new Grid();
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
        var rowSplitter = new GridSplitter { Height = 4, HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetRow(rowSplitter, 1);
        Grid.SetRow(_right, 2);
        right.Children.Add(_browser);
        right.Children.Add(rowSplitter);
        right.Children.Add(_right);

        var body = new Grid();
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
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(statusBar, Dock.Bottom);
        root.Children.Add(toolbar);
        root.Children.Add(statusBar);
        root.Children.Add(body);
        Content = root;

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
        _inspector.Problem += (_, problem) => _status.Text = problem;
        _callbacks.Problem += (_, problem) => _status.Text = problem;
        _callbacks.GoTo += (_, name) => GoToFunction(name);
        _browser.RenameRequested += (_, component) => RenameComponent(component);
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
                _browser.Document = Active?.Document;
                _inspector.Document = Active?.Document;
                _callbacks.Document = Active?.Document;
                _codeBrowser.Document = Active?.Document;
                ShowView();
                ShowState();
            }
        };

        CommandBindings.Add(new CommandBinding(ApplicationCommands.Open, (_, _) => PromptOpen()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, (_, _) => Save(Active),
            (_, e) => e.CanExecute = Active is { Document.CanSave: true }));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo, (_, _) => Active?.Document.Undo(),
            (_, e) => e.CanExecute = Active is { Document.CanUndo: true }));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Redo, (_, _) => Active?.Document.Redo(),
            (_, e) => e.CanExecute = Active is { Document.CanRedo: true }));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Delete, (_, _) => Active?.Document.Delete(Active.Document.Selection),
            (_, e) => e.CanExecute = Active is { } a && a.Document.Selection.Any(a.Document.CanDelete)));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Close, (_, _) => CloseTab(Active),
            (_, e) => e.CanExecute = Active is not null));
        CommandBindings.Add(new CommandBinding(ToggleView, (_, _) => SetView(Active, Active?.InCodeView == false),
            (_, e) => e.CanExecute = Active is not null));
        CommandBindings.Add(new CommandBinding(AddCallback, (_, _) => ShowAddCallback(),
            (_, e) => e.CanExecute = Active is { Document.Code: not null }));
        CommandBindings.Add(new CommandBinding(AddFunction, (_, _) => Declared(Active?.Document.InsertUserFunction("private"), function: true),
            (_, e) => e.CanExecute = Active is { Document.Code: not null }));
        CommandBindings.Add(new CommandBinding(AddProperty, (_, _) => Declared(Active?.Document.InsertUserProperty("private"), function: false),
            (_, e) => e.CanExecute = Active is { Document.Code: not null }));
        CommandBindings.Add(new CommandBinding(CodeOptions, (_, _) => ShowCodeOptions(),
            (_, e) => e.CanExecute = Active is { Document.Code: not null }));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Close, Key.W, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Redo, Key.Z, ModifierKeys.Control | ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(ToggleView, Key.F7, ModifierKeys.Shift));
    }

    /// <summary>Raised after an app was saved, with its file and its class's new text, for an editor tab showing it.</summary>
    public event Action<string, string>? AppSaved;

    private AppTab? Active => (_tabs.SelectedItem as TabItem)?.Tag as AppTab;

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
            document = DesignDocument.Open(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return ex.Message;
        }

        var canvas = new DesignCanvas { Document = document };
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
        var item = new TabItem { Content = content, ToolTip = path };
        var app = new AppTab(document, canvas, codeView, designButton, codeButton, item);
        item.Tag = app;
        // The state, not the click: a toggle set by the keyboard or through automation raises no Click.
        designButton.Checked += (_, _) => SetView(app, code: false);
        codeButton.Checked += (_, _) => SetView(app, code: true);
        designButton.Unchecked += (_, _) => { if (codeButton.IsChecked != true) { designButton.IsChecked = true; } };
        codeButton.Unchecked += (_, _) => { if (designButton.IsChecked != true) { codeButton.IsChecked = true; } };
        canvas.DesignChanged += (_, _) =>
        {
            item.Header = app.Header;
            if (ReferenceEquals(Active, app))
            {
                ShowState();
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
        document.Changed += (_, _) =>
        {
            item.Header = app.Header;
            if (ReferenceEquals(Active, app))
            {
                // The toolbar's single-instance toggle follows the document through an undo too.
                ShowView();
                ShowState();
            }
        };
        item.Header = app.Header;
        _tabs.Items.Add(item);
        _tabs.SelectedItem = item;
        _browser.Document = document;
        _inspector.Document = document;
        _callbacks.Document = document;
        _codeBrowser.Document = document;
        ApplyCanvasOptions();
        ShowView();
        ShowState();
        Bring();
        canvas.Focus();
        return null;
    }

    /// <summary>Brings the window up as it is: what <c>appdesigner</c> with no file does.</summary>
    public void ShowWindow()
    {
        Bring();
        ShowState();
    }

    private void PromptOpen()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open app",
            Filter = "MATLAB App (*.mlapp)|*.mlapp",
            InitialDirectory = Active?.Document.Path is { } current ? Path.GetDirectoryName(current) : null,
        };
        if (dialog.ShowDialog(this) == true && Open(dialog.FileName) is { } refused)
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

        app.Canvas.ShowGrid = _showGrid.IsChecked == true;
        app.Canvas.SnapToGrid = _snap.IsChecked == true;
        app.Canvas.ShowAlignmentHints = _hints.IsChecked == true;
        if (_interval.SelectedItem is double interval)
        {
            app.Canvas.GridInterval = interval;
        }
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

    /// <summary>The left pane follows the active app's view: the library in the Design View, the code browser in the Code View.</summary>
    private void ShowView()
    {
        bool code = Active?.InCodeView == true;
        _left.Content = code ? _codeBrowser : _palette;
        _settingSingleInstance = true;
        _singleInstance.IsChecked = Active?.Document.SingleInstance == true;
        _singleInstance.IsEnabled = Active is { Document.Code: not null };
        _settingSingleInstance = false;
    }

    private void SetSingleInstance(bool on)
    {
        if (_settingSingleInstance || Active is not { Document: { Code: not null } document })
        {
            return;
        }

        document.SetSingleInstance(on);
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

    private void ShowCodeOptions()
    {
        if (Active is not { Document: { Code: not null } document } app)
        {
            return;
        }

        app.CodeView.Push();
        if (AppDesignerDialogs.CodeOptions(this, document).Arguments)
        {
            // App Designer lands in the Code View, at the startup function, after a change of the input
            // arguments alone (InputArgumentsCommand); the single-instance setting switches nothing.
            SetView(app, code: true);
            if (document.Callbacks.FirstOrDefault(static c => c.IsStartup) is { } startup)
            {
                app.CodeView.GoToFunction(startup.Name);
            }
        }
    }

    /// <summary>Renames a component after showing what changes with it: the lines of the user's code that name it, and the callbacks named after it.</summary>
    private void RenameComponent(DesignComponent component)
    {
        if (Active is not { Document: var document } app || !document.IsEditable(component, "CodeName") && document.IsRegion(component))
        {
            return;
        }

        app.CodeView.Push();
        string? name = AppDesignerDialogs.Rename(this, $"Rename {component.CodeName}", component.CodeName,
            candidate => DesignNames.WhyNotName(candidate, component.CodeName, document.NamesInUse()),
            candidate => Preview(document.RenamePreview(component, Probe(component.CodeName, candidate)), component.CodeName, candidate));
        if (name is not null && document.Rename(component, name) is { } refused)
        {
            _status.Text = refused;
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

        string selected = document.Selection switch
        {
            [var one] => $"{one.CodeName} ({one.Type})" + (document.Runtime.PositionOf(one) is { } p && one.Parent is not null
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
            ? $"  View only: createComponents has {document.ReadProblems.Count} line(s) the designer cannot place, so a save would lose them."
            : document.Code is null ? "  View only: " + document.CodeProblem
            : document.Runtime.Problems.Count > 0
                ? $"  The canvas could not make everything: {string.Join(" ", document.Runtime.Problems)}"
                : string.Empty;
        _status.Text = selected + note;

        static string Whole(double v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private bool Save(AppTab? app)
    {
        if (app is null)
        {
            return false;
        }

        try
        {
            // Text the document would not take is not saved over: the status line says what to put right.
            if (!app.CodeView.Push())
            {
                return false;
            }

            _status.Text = app.Document.Save();
            AppSaved?.Invoke(app.Document.Path!, app.Document.Text);
            app.Item.Header = app.Header;
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
                $"The code typed into {Path.GetFileName(app.Document.Path)} no longer matches the app's structure, so it cannot be kept. Close anyway and lose it?",
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
            $"Save the changes to {Path.GetFileName(app.Document.Path)}?", "App Designer",
            app.Document.CanSave ? MessageBoxButton.YesNoCancel : MessageBoxButton.OKCancel, MessageBoxImage.Question);
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
        if (_tabs.Items.Count == 0)
        {
            Hide();
        }
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
        foreach (AppTab app in Apps.ToList())
        {
            app.Canvas.Document = null;
            app.CodeView.Document = null;
            app.Document.Dispose();
        }

        _tabs.Items.Clear();
    }

    private sealed record AppTab(DesignDocument Document, DesignCanvas Canvas, DesignCodeView CodeView, ToggleButton DesignButton, ToggleButton CodeButton, TabItem Item)
    {
        public bool InCodeView { get; set; }

        public string Header => Path.GetFileName(Document.Path) + (Document.IsDirty ? " *" : string.Empty);
    }
}
