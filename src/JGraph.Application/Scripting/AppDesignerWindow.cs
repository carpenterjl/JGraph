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
/// top-level window beside the desktop, with a tab per open app, each tab the app's canvas. The
/// toolbar is App Designer's canvas tab: save, undo and redo, the grid (shown, snapped to, its
/// interval) and the alignment guides. DS3: App Designer's component library on the left, and its
/// component browser over its inspector on the right, both showing the app in the tab chosen.
/// </summary>
internal sealed class AppDesignerWindow : Window
{
    private readonly TabControl _tabs = new();
    private readonly TextBlock _status = new() { Margin = new Thickness(4, 0, 4, 0) };
    private readonly ToggleButton _showGrid = new() { Content = "Show Grid", ToolTip = "Show a grid on the canvas; moves and resizes snap to it" };
    private readonly ToggleButton _snap = new() { Content = "Snap to Grid", IsChecked = true, ToolTip = "Snap to the grid while it is shown" };
    private readonly ComboBox _interval = new() { Width = 56, ToolTip = "Grid interval, in pixels" };
    private readonly ToggleButton _hints = new() { Content = "Alignment Hints", IsChecked = true, ToolTip = "Show alignment guides and snap to them" };
    private readonly DesignPaletteView _palette = new();
    private readonly DesignBrowserView _browser = new();
    private readonly DesignInspectorView _inspector = new();

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

        // The toolbar's commands are the window's: routed from the focused element, Undo would reach
        // an inspector text box's own (empty) undo instead of the design's.
        foreach (Button button in toolbar.Items.OfType<Button>())
        {
            button.CommandTarget = this;
        }

        var statusBar = new StatusBar();
        statusBar.Items.Add(new StatusBarItem { Content = _status });

        // The library, the canvases, and the browser over the inspector, as App Designer lays them out.
        var right = new Grid();
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
        var rowSplitter = new GridSplitter { Height = 4, HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetRow(rowSplitter, 1);
        Grid.SetRow(_inspector, 2);
        right.Children.Add(_browser);
        right.Children.Add(rowSplitter);
        right.Children.Add(_inspector);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
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
        body.Children.Add(_palette);
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

        _tabs.SelectionChanged += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, _tabs))
            {
                ApplyCanvasOptions();
                _palette.Chosen = null;
                _browser.Document = Active?.Document;
                _inspector.Document = Active?.Document;
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
        InputBindings.Add(new KeyBinding(ApplicationCommands.Close, Key.W, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Redo, Key.Z, ModifierKeys.Control | ModifierKeys.Shift));
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
        var item = new TabItem { Content = canvas, ToolTip = path };
        var app = new AppTab(document, canvas, item);
        item.Tag = app;
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
        item.Header = app.Header;
        _tabs.Items.Add(item);
        _tabs.SelectedItem = item;
        _browser.Document = document;
        _inspector.Document = document;
        ApplyCanvasOptions();
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

    /// <summary>Asks about unsaved edits; false when the user cancels.</summary>
    private bool ConfirmClose(AppTab app)
    {
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
        if (ReferenceEquals(_inspector.Document, app.Document))
        {
            _inspector.Document = null;
            _browser.Document = null;
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
        _browser.Document = null;
        foreach (AppTab app in Apps.ToList())
        {
            app.Canvas.Document = null;
            app.Document.Dispose();
        }

        _tabs.Items.Clear();
    }

    private sealed record AppTab(DesignDocument Document, DesignCanvas Canvas, TabItem Item)
    {
        public string Header => Path.GetFileName(Document.Path) + (Document.IsDirty ? " *" : string.Empty);
    }
}
