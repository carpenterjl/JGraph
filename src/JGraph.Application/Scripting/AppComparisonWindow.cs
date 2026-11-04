using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using JGraph.Controls.Scripting;
using JGraph.Controls.Themes;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Editing;
using Microsoft.Win32;

namespace JGraph.Application.Scripting;

/// <summary>
/// R2025b's Comparison Tool window as JGraph has it (App Designer window plan W14): "Comparison Tool - left vs. right"
/// over a <see cref="FileComparison"/>, with the COMPARISON tab's words on a toolbar - Previous and Next (the Up and
/// Down keys), Swap Sides, Refresh, Mergeable Only (an app comparison's "Navigate mergeable differences only"), Replace
/// Content ("Replace content in right pane with content in the left pane"), Undo, Save Result and Publish (to HTML) -
/// the summary line, the two files' names over a <see cref="TextDiffView"/>, and for two apps a second tab with the
/// designs' differences, which R2025b does not show. A merge replaces the right side's lines with the left's for the
/// current difference where the right's lines are the user's (<c>isLinesMergeable</c>; "Merging generated code is not
/// supported." otherwise), and Save Result writes the right file.
/// </summary>
internal sealed class AppComparisonWindow : Window
{
    private readonly TextDiffView _view = new();
    private readonly TextBlock _summary = new() { Margin = new Thickness(8, 4, 8, 2), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { Margin = new Thickness(8, 2, 8, 4), TextWrapping = TextWrapping.Wrap, FontStyle = FontStyles.Italic };
    private readonly TextBlock _leftTitle = new() { Margin = new Thickness(8, 2, 8, 2), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _rightTitle = new() { Margin = new Thickness(8, 2, 8, 2), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly ToggleButton _mergeableOnly = new() { Content = "Mergeable Only", ToolTip = "Navigate mergeable differences only", IsChecked = true, Padding = new Thickness(8, 3, 8, 3) };
    private readonly Button _replace = new() { Content = "Replace Content", ToolTip = "Replace content in right pane with content in the left pane", Padding = new Thickness(8, 3, 8, 3) };
    private readonly Button _undo = new() { Content = "Undo", ToolTip = "Undo content replacement", Padding = new Thickness(8, 3, 8, 3) };
    private readonly Button _save = new() { Content = "Save Result", ToolTip = "Save the merged file", Padding = new Thickness(8, 3, 8, 3) };
    private readonly Button _previous = new() { Content = "Previous", ToolTip = "Go to the previous change", Padding = new Thickness(8, 3, 8, 3) };
    private readonly Button _next = new() { Content = "Next", ToolTip = "Go to the next change", Padding = new Thickness(8, 3, 8, 3) };
    private readonly ListView _design = new() { Margin = new Thickness(8) };
    private readonly TabItem? _designTab;
    private readonly Stack<FileComparison> _history = new();
    private FileComparison _comparison;

    /// <summary>Makes the window over <paramref name="comparison"/>.</summary>
    public AppComparisonWindow(FileComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        _comparison = comparison;
        Width = Math.Min(SystemParameters.PrimaryScreenWidth * 0.85, 1100);
        Height = Math.Min(SystemParameters.PrimaryScreenHeight * 0.85, 720);
        MinWidth = 600;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, ThemeKeys.Window);
        SetResourceReference(ForegroundProperty, ThemeKeys.Text);
        AutomationProperties.SetAutomationId(this, "ComparisonWindow");

        var root = new DockPanel();
        root.Children.Add(BuildToolbar());
        AutomationProperties.SetAutomationId(_summary, "Compare.Summary");
        AutomationProperties.SetAutomationId(_status, "Compare.Status");
        _status.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextSecondary);
        var words = new StackPanel();
        words.Children.Add(_summary);
        words.Children.Add(_status);
        DockPanel.SetDock(words, Dock.Top);
        root.Children.Add(words);

        var titles = new Grid();
        titles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        titles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AutomationProperties.SetAutomationId(_leftTitle, "Compare.LeftTitle");
        AutomationProperties.SetAutomationId(_rightTitle, "Compare.RightTitle");
        Grid.SetColumn(_rightTitle, 2);
        titles.Children.Add(_leftTitle);
        titles.Children.Add(_rightTitle);
        var body = new DockPanel();
        DockPanel.SetDock(titles, Dock.Top);
        body.Children.Add(titles);
        body.Children.Add(_view);
        AutomationProperties.SetAutomationId(_view, "Compare.View");

        if (comparison.Kind == ComparisonKind.App)
        {
            var tabs = new TabControl();
            var codeTab = new TabItem { Header = comparison.DisplayType, Content = body };
            AutomationProperties.SetAutomationId(codeTab, "Compare.CodeTab");
            _designTab = new TabItem { Content = _design };
            AutomationProperties.SetAutomationId(_designTab, "Compare.DesignTab");
            tabs.Items.Add(codeTab);
            tabs.Items.Add(_designTab);
            root.Children.Add(tabs);
        }
        else
        {
            root.Children.Add(body);
        }

        BuildDesignList();
        Content = root;
        _view.CurrentChanged += (_, _) => ShowState();
        InputBindings.Add(new KeyBinding(new RelayCommand(() => Navigate(1)), Key.Down, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => Navigate(-1)), Key.Up, ModifierKeys.None));
        Closing += (_, e) =>
        {
            if (HasUnsavedMerges && MessageBox.Show(this, "Discard unsaved merge changes?", "Close Comparison", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            {
                e.Cancel = true;
            }
        };
        Show(comparison, resetHistory: true);
    }

    /// <summary>Raised after Save Result wrote the right file, with its path.</summary>
    public event Action<string>? Saved;

    /// <summary>The comparison as it stands, merges included.</summary>
    public FileComparison Comparison => _comparison;

    /// <summary>Whether a merge was made and not saved.</summary>
    public bool HasUnsavedMerges => _history.Count > 0;

    /// <summary>The key a comparison window is found by again: both paths.</summary>
    public static string KeyOf(string left, string right) => left + "|" + right;

    private UIElement BuildToolbar()
    {
        var bar = new ToolBar { ToolTip = null };
        bar.SetResourceReference(BackgroundProperty, ThemeKeys.ToolBar);
        AutomationProperties.SetAutomationId(_previous, "Compare.Previous");
        AutomationProperties.SetAutomationId(_next, "Compare.Next");
        _previous.Click += (_, _) => Navigate(-1);
        _next.Click += (_, _) => Navigate(1);
        bar.Items.Add(_previous);
        bar.Items.Add(_next);
        bar.Items.Add(new Separator());
        bar.Items.Add(ToolButton("Swap Sides", "Compare.SwapSides", "Swap sides and rerun comparison", SwapSides));
        bar.Items.Add(ToolButton("Refresh", "Compare.Refresh", "Rerun comparison", Refresh));
        bar.Items.Add(new Separator());
        if (_comparison.Kind == ComparisonKind.App)
        {
            AutomationProperties.SetAutomationId(_mergeableOnly, "Compare.MergeableOnly");
            _mergeableOnly.Click += (_, _) => ShowState();
            bar.Items.Add(_mergeableOnly);
        }

        AutomationProperties.SetAutomationId(_replace, "Compare.ReplaceContent");
        _replace.Click += (_, _) => ReplaceContent();
        bar.Items.Add(_replace);
        AutomationProperties.SetAutomationId(_undo, "Compare.Undo");
        _undo.Click += (_, _) => Undo();
        bar.Items.Add(_undo);
        AutomationProperties.SetAutomationId(_save, "Compare.SaveResult");
        _save.Click += (_, _) => SaveResult();
        bar.Items.Add(_save);
        bar.Items.Add(new Separator());
        bar.Items.Add(ToolButton("Publish", "Compare.Publish", "Save the comparison results as an HTML file", Publish));
        var tray = new ToolBarTray { IsLocked = true };
        tray.SetResourceReference(BackgroundProperty, ThemeKeys.ToolBar);
        tray.ToolBars.Add(bar);
        DockPanel.SetDock(tray, Dock.Top);
        return tray;
    }

    private static Button ToolButton(string text, string id, string tip, Action click)
    {
        var button = new Button { Content = text, ToolTip = tip, Padding = new Thickness(8, 3, 8, 3) };
        AutomationProperties.SetAutomationId(button, id);
        button.Click += (_, _) => click();
        return button;
    }

    private void BuildDesignList()
    {
        var view = new GridView();
        view.Columns.Add(new GridViewColumn { Header = "Change", Width = 110, DisplayMemberBinding = new System.Windows.Data.Binding("Kind") });
        view.Columns.Add(new GridViewColumn { Header = "Component", Width = 160, DisplayMemberBinding = new System.Windows.Data.Binding("CodeName") });
        view.Columns.Add(new GridViewColumn { Header = "Property", Width = 120, DisplayMemberBinding = new System.Windows.Data.Binding("Property") });
        view.Columns.Add(new GridViewColumn { Header = "Left", Width = 240, DisplayMemberBinding = new System.Windows.Data.Binding("Left") });
        view.Columns.Add(new GridViewColumn { Header = "Right", Width = 240, DisplayMemberBinding = new System.Windows.Data.Binding("Right") });
        _design.View = view;
        AutomationProperties.SetAutomationId(_design, "Compare.Design");
    }

    private void Show(FileComparison comparison, bool resetHistory)
    {
        if (resetHistory)
        {
            _history.Clear();
        }

        _comparison = comparison;
        Title = $"Comparison Tool - {Path.GetFileName(comparison.LeftPath)} vs. {Path.GetFileName(comparison.RightPath)}";
        _leftTitle.Text = comparison.LeftPath;
        _leftTitle.ToolTip = comparison.LeftPath;
        _rightTitle.Text = comparison.RightPath + (HasUnsavedMerges ? " (Result)" : string.Empty);
        _rightTitle.ToolTip = comparison.RightPath;
        _summary.Text = comparison.Summary;
        _view.Show(comparison);
        _design.ItemsSource = comparison.DesignDifferences;
        if (_designTab is not null)
        {
            _designTab.Header = $"Design comparison ({comparison.DesignDifferences.Count})";
        }

        ShowState();
    }

    private void ShowState()
    {
        int current = _view.Current;
        DiffHunk? hunk = current >= 0 && current < _comparison.Hunks.Count ? _comparison.Hunks[current] : null;
        _replace.IsEnabled = hunk is not null && _comparison.IsMergeable(hunk);
        _replace.ToolTip = hunk is not null && _comparison.WhyNotMergeable(hunk) is { } why ? why : "Replace content in right pane with content in the left pane";
        _undo.IsEnabled = _history.Count > 0;
        _save.IsEnabled = HasUnsavedMerges;
        Func<DiffHunk, bool>? accept = Accept();
        _previous.IsEnabled = hunk is not null && Enumerable.Range(0, current).Any(i => accept is null || accept(_comparison.Hunks[i]));
        _next.IsEnabled = hunk is not null && Enumerable.Range(current + 1, Math.Max(0, _comparison.Hunks.Count - current - 1)).Any(i => accept is null || accept(_comparison.Hunks[i]));
        if (_comparison.MergeRefusal is { } refusal)
        {
            _status.Text = refusal;
        }
        else if (hunk is not null && _comparison.Hunks.Count > 0)
        {
            _status.Text = $"Difference {current + 1} of {_comparison.Hunks.Count}" + (_comparison.WhyNotMergeable(hunk) is { } w ? ". " + w : string.Empty);
        }
    }

    private Func<DiffHunk, bool>? Accept() => _comparison.Kind == ComparisonKind.App && _mergeableOnly.IsChecked == true ? _comparison.IsMergeable : null;

    private void Navigate(int direction)
    {
        if (direction > 0 ? !_view.Next(Accept()) : !_view.Previous(Accept()))
        {
            _status.Text = direction > 0 ? "No more differences below." : "No more differences above.";
        }
    }

    private void ReplaceContent()
    {
        int current = _view.Current;
        if (current < 0 || current >= _comparison.Hunks.Count)
        {
            return;
        }

        DiffHunk hunk = _comparison.Hunks[current];
        if (_comparison.WhyNotMergeable(hunk) is { } why)
        {
            _status.Text = why;
            return;
        }

        _history.Push(_comparison);
        Show(_comparison.Merge(hunk), resetHistory: false);
        _status.Text = "Content replaced; Save Result writes the right file.";
    }

    private void Undo()
    {
        if (_history.Count == 0)
        {
            return;
        }

        Show(_history.Pop(), resetHistory: false);
        _status.Text = "Content replacement undone.";
    }

    private void SaveResult()
    {
        try
        {
            string done = _comparison.SaveResult();
            _history.Clear();
            Show(FileComparison.Compare(_comparison.LeftPath, _comparison.RightPath), resetHistory: true);
            _status.Text = done;
            Saved?.Invoke(_comparison.RightPath);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or InvalidDataException)
        {
            _status.Text = ex.Message;
            MessageBox.Show(this, $"Unable to save file \"{_comparison.RightPath}\". {ex.Message}", "Save Result", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private bool ConfirmDiscard(string title)
    {
        return !HasUnsavedMerges || MessageBox.Show(this, "Discard unsaved merge changes?", title, MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;
    }

    private void Refresh()
    {
        if (!ConfirmDiscard("Refresh Comparison"))
        {
            return;
        }

        Reread(_comparison.LeftPath, _comparison.RightPath);
    }

    private void SwapSides()
    {
        if (!ConfirmDiscard("Swap Sides"))
        {
            return;
        }

        Reread(_comparison.RightPath, _comparison.LeftPath);
    }

    private void Reread(string left, string right)
    {
        try
        {
            Show(FileComparison.Compare(left, right), resetHistory: true);
            _status.Text = string.Empty;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _status.Text = ex.Message;
        }
    }

    private void Publish()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save HTML Report As",
            Filter = "HTML Files (*.html)|*.html",
            FileName = Path.GetFileNameWithoutExtension(_comparison.LeftPath) + "_" + Path.GetFileNameWithoutExtension(_comparison.RightPath) + ".html",
            InitialDirectory = Path.GetDirectoryName(_comparison.LeftPath),
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, _comparison.HtmlReport());
            _status.Text = $"Published {dialog.FileName}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _status.Text = $"Unable to write to file '{dialog.FileName}'. {ex.Message}";
        }
    }

    private sealed class RelayCommand(Action run) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => run();
    }
}

/// <summary>
/// The "Compare To" dialog (App Designer window plan W14): R2025b opens a system file chooser titled "Compare To" over the
/// current app or file; JGraph's dialog names the file to compare to, offers the other open files and a Browse button, so
/// that a check can drive it. Automation ids <c>CompareTo.Path</c>, <c>CompareTo.Browse</c>, <c>CompareTo.OK</c>.
/// </summary>
internal sealed class CompareToDialog : Window
{
    private readonly ComboBox _path = new() { IsEditable = true, MinWidth = 420, Margin = new Thickness(0, 4, 0, 4) };
    private readonly TextBlock _problem = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };

    /// <summary>Makes the dialog for <paramref name="current"/> with <paramref name="others"/> to pick from.</summary>
    public CompareToDialog(string current, IEnumerable<string> others)
    {
        ArgumentException.ThrowIfNullOrEmpty(current);
        Title = "Compare To";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, ThemeKeys.Window);
        SetResourceReference(ForegroundProperty, ThemeKeys.Text);
        AutomationProperties.SetAutomationId(this, "CompareToDialog");
        Current = Path.GetFullPath(current);
        foreach (string other in others.Where(o => !string.Equals(o, Current, StringComparison.OrdinalIgnoreCase)))
        {
            _path.Items.Add(other);
        }

        if (_path.Items.Count > 0)
        {
            _path.SelectedIndex = 0;
        }

        AutomationProperties.SetAutomationId(_path, "CompareTo.Path");
        _problem.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Error);
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = $"Compare {Path.GetFileName(Current)} to the file:", Margin = new Thickness(0, 0, 0, 4) });
        var row = new DockPanel();
        var browse = new Button { Content = "Browse…", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 4, 0, 4) };
        AutomationProperties.SetAutomationId(browse, "CompareTo.Browse");
        browse.Click += (_, _) => Browse();
        DockPanel.SetDock(browse, Dock.Right);
        row.Children.Add(browse);
        row.Children.Add(_path);
        panel.Children.Add(row);
        panel.Children.Add(_problem);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var ok = new Button { Content = "Compare", IsDefault = true, MinWidth = 90, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0) };
        AutomationProperties.SetAutomationId(ok, "CompareTo.OK");
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, Padding = new Thickness(10, 3, 10, 3) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;
    }

    /// <summary>The file compared, as given.</summary>
    public string Current { get; }

    /// <summary>The file chosen to compare to; null until accepted.</summary>
    public string? Chosen { get; private set; }

    private void Browse()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Compare To",
            Filter = "All Files (*.*)|*.*",
            InitialDirectory = Path.GetDirectoryName(Current),
        };
        if (dialog.ShowDialog(this) == true)
        {
            _path.Text = dialog.FileName;
        }
    }

    private void Accept()
    {
        string text = (_path.Text ?? string.Empty).Trim().Trim('"');
        if (text.Length == 0)
        {
            _problem.Text = "Name a file to compare to.";
            return;
        }

        string full = Path.IsPathRooted(text) ? text : Path.Combine(Path.GetDirectoryName(Current) ?? string.Empty, text);
        if (!File.Exists(full))
        {
            _problem.Text = $"Unable to load file '{full}'.";
            return;
        }

        Chosen = Path.GetFullPath(full);
        DialogResult = true;
    }
}
