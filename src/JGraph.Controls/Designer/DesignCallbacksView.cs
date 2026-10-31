using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using JGraph.Controls.Themes;
using JGraph.Scripting.Designer;

namespace JGraph.Controls.Designer;

/// <summary>
/// The inspector's Callbacks tab (designer plan DS5, window plan W7d; App Designer's <c>CallbackSection</c>): for what
/// <see cref="DesignDocument.CallbackSheet"/> says it is over, a row per callback property in App Designer's groups - the property's name,
/// a box holding the callback bound to it, and a button to go to that function. The box offers what
/// App Designer's does: <c>&lt;no callback&gt;</c> to unbind, <c>&lt;add ... callback&gt;</c> for a new
/// function named as App Designer names it, and the app's other callbacks; a name typed makes or
/// binds a function. With several selected the rows are those they have in common, each showing the last one's and an edit going to them all.
/// The app's own row has its <c>StartupFcn</c>.
/// </summary>
public sealed class DesignCallbacksView : UserControl
{
    private const string NoCallback = "<no callback>";
    private const string StartupProperty = DesignCallbackRules.StartupProperty;

    private readonly StackPanel _panel = new() { Margin = new Thickness(4) };
    private readonly List<Row> _rows = [];
    private DesignDocument? _document;
    private DesignCallbackSheet? _sheet;
    private bool _refreshing;
    private bool _queued;

    public DesignCallbacksView()
    {
        Grid.SetIsSharedSizeScope(_panel, true);
        var scroll = new ScrollViewer { Content = _panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        scroll.SetResourceReference(BackgroundProperty, ThemeKeys.Panel);
        Content = scroll;
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, "Callbacks");
    }

    /// <summary>Raised with what an edit was refused for, which App Designer says in an alert.</summary>
    public event EventHandler<string>? Problem;

    /// <summary>Raised with a function's name when its go-to button is pressed, or a callback was added: the code view shows it.</summary>
    public event EventHandler<string>? GoTo;

    /// <summary>The app whose selection's callbacks are shown.</summary>
    public DesignDocument? Document
    {
        get => _document;
        set
        {
            if (ReferenceEquals(value, _document))
            {
                return;
            }

            if (_document is not null)
            {
                _document.Changed -= OnChanged;
            }

            _document = value;
            if (value is not null)
            {
                value.Changed += OnChanged;
            }

            Rebuild();
        }
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        if (_queued)
        {
            return;
        }

        _queued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _queued = false;
            if (_document?.CallbackSheet is { } now && _sheet is { } was && SameSheet(now, was))
            {
                Refresh();
            }
            else
            {
                Rebuild();
            }
        });
    }

    // The same rows for the same components: the list an edit goes to stays with the tab, whichever way round it last went.
    private static bool SameSheet(DesignCallbackSheet now, DesignCallbackSheet was) =>
        ReferenceEquals(now.Anchor, was.Anchor) && now.Peers.Count == was.Peers.Count && now.Peers.All(p => was.Peers.Contains(p, ReferenceEqualityComparer.Instance))
        && now.Groups.SequenceEqual(was.Groups, GroupWords.Instance);

    private void Rebuild()
    {
        _panel.Children.Clear();
        _rows.Clear();
        _sheet = _document?.CallbackSheet;
        if (_sheet is not { } sheet)
        {
            return;
        }

        if (sheet.Message is { } message)
        {
            var text = new TextBlock { Text = message, Margin = new Thickness(2), TextWrapping = TextWrapping.Wrap };
            System.Windows.Automation.AutomationProperties.SetAutomationId(text, "Callbacks.Message");
            _panel.Children.Add(text);
            return;
        }

        bool titled = sheet.Groups.Count > 1;
        foreach (DesignCallbackGroup group in sheet.Groups)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 60, SharedSizeGroup = "Property" }); // the rows line up across groups
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            foreach (string property in group.Properties)
            {
                int row = grid.RowDefinitions.Count;
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                AddRow(grid, row, sheet.Anchor, property);
            }

            if (titled)
            {
                var expander = new Expander { Header = group.Title ?? string.Empty, IsExpanded = true, Content = grid, Margin = new Thickness(0, 0, 0, 4) };
                System.Windows.Automation.AutomationProperties.SetName(expander, group.Title ?? string.Empty);
                _panel.Children.Add(expander);
            }
            else
            {
                _panel.Children.Add(grid);
            }
        }
    }

    private void AddRow(Grid grid, int row, DesignComponent? anchor, string property)
    {
        var label = new TextBlock { Text = property, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 6, 1), ToolTip = property };
        var box = new ComboBox { IsEditable = true, Margin = new Thickness(0, 1, 4, 1), MinWidth = 100, ToolTip = DesignCallbackRules.Placeholder };
        var empty = new TextBlock { Text = DesignCallbackRules.Placeholder, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 1, 24, 1), IsHitTestVisible = false, Opacity = 0.6 };
        var go = new Button { Content = "→", Width = 24, Margin = new Thickness(0, 1, 0, 1), ToolTip = "Move cursor to function" };
        string id = $"Callbacks.{anchor?.CodeName ?? "app"}.{property}";
        System.Windows.Automation.AutomationProperties.SetAutomationId(box, id);
        System.Windows.Automation.AutomationProperties.SetName(box, property);
        System.Windows.Automation.AutomationProperties.SetAutomationId(go, id + ".GoTo");
        System.Windows.Automation.AutomationProperties.SetName(go, "Go to " + property);

        // The figure's SizeChangedFcn row is never editable in App Designer's tab (CallbackSection, whatever
        // the app is), nor is a responsive app's region panel's (RegionPanelController.shouldDisableCallback).
        bool locked = anchor is not null && _document is { } doc && ((property == "SizeChangedFcn" && anchor.Parent is null) || doc.IsCallbackLocked(anchor, property));
        box.IsEnabled = !locked;

        var entry = new Row(property, box, go);
        _rows.Add(entry);
        Grid.SetRow(label, row);
        Grid.SetRow(box, row);
        Grid.SetRow(empty, row);
        Grid.SetRow(go, row);
        Grid.SetColumn(label, 0);
        Grid.SetColumn(box, 1);
        Grid.SetColumn(empty, 1);
        Grid.SetColumn(go, 2);
        grid.Children.Add(label);
        grid.Children.Add(box);
        grid.Children.Add(empty);
        grid.Children.Add(go);
        box.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => empty.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed));
        Show(entry);

        // A choice picked from the open list commits; the arrow keys walking the list while it is
        // closed only change the text, which Enter or leaving the box commits as typed text.
        box.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && box.IsDropDownOpen && box.SelectedItem is DesignCallbackChoice chosen)
            {
                Commit(entry, chosen.Value);
            }
        };
        box.DropDownClosed += (_, _) =>
        {
            if (!_refreshing && box.SelectedItem is DesignCallbackChoice chosen)
            {
                Commit(entry, chosen.Value);
            }
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit(entry, box.Text);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Show(entry);
                e.Handled = true;
            }
        };
        box.LostKeyboardFocus += (_, e) =>
        {
            // The box's own text box taking the keyboard is not leaving it.
            if (!_refreshing && !box.IsDropDownOpen && !(e.NewFocus is DependencyObject inner && box.IsAncestorOf(inner)))
            {
                Commit(entry, box.Text);
            }
        };
        go.Click += (_, _) =>
        {
            if (Current(entry.Property) is { Length: > 0 } name)
            {
                GoTo?.Invoke(this, name);
            }
        };
    }

    /// <summary>The callback bound to <paramref name="property"/> of the shown component; the startup function's name for the app's row.</summary>
    private string? Current(string property)
    {
        if (_document is not { } document)
        {
            return null;
        }

        return property == StartupProperty ? document.Callbacks.FirstOrDefault(static c => c.IsStartup)?.Name
            : _sheet?.Anchor is { } anchor ? document.CallbackOf(anchor, property) : null;
    }

    private void Show(Row row)
    {
        _refreshing = true;
        try
        {
            string current = Current(row.Property) ?? string.Empty;
            row.Box.ItemsSource = DesignCallbackRules.Choices((_document?.Callbacks ?? []).Where(static c => !c.IsGenerated).Select(static c => (c.Name, c.IsStartup)), current, row.Property);
            row.Box.SelectedItem = null;
            row.Box.Text = current;
            row.Go.IsEnabled = current.Length > 0;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void Refresh()
    {
        foreach (Row row in _rows)
        {
            if (!row.Box.IsKeyboardFocusWithin || row.Box.Text == (Current(row.Property) ?? string.Empty))
            {
                Show(row);
            }
        }
    }

    /// <summary>
    /// What App Designer's row does with a value (<c>CallbackSection._handleCallbackNameEdited</c>, <see cref="DesignDocument.EditCallbackRow"/>); a refused one goes back, said in
    /// an alert. The app's row deletes its function for a new name or none, its input parameters with it. Only a function made here takes the user to the Code View
    /// (<c>AddCallbackCommand</c>); binding a property to one the app has (Attach, Reassign) switches nothing.
    /// </summary>
    private void Commit(Row row, string value)
    {
        if (_document is not { } document || _sheet is not { } sheet)
        {
            return;
        }

        value = value.Trim();
        if (value == NoCallback)
        {
            value = string.Empty;
        }

        string? made = null;
        string? refused = row.Property == StartupProperty
            ? CommitStartup(document, value, out made)
            : document.EditCallbackRow(sheet.Peers, sheet.Anchor!, row.Property, value, out made);
        Show(row);
        if (refused is not null)
        {
            Problem?.Invoke(this, refused);
        }
        else if (made is not null)
        {
            GoTo?.Invoke(this, made);
        }
    }

    private static string? CommitStartup(DesignDocument document, string value, out string? made)
    {
        made = null;
        DesignCallback? startup = document.Callbacks.FirstOrDefault(static c => c.IsStartup);
        if (value == (startup?.Name ?? string.Empty))
        {
            return null;
        }

        if (value.Length == 0)
        {
            return startup is null ? null : document.DeleteCallback(startup);
        }

        // The default's name is free among the names as they stand, the old function's still among them.
        if (value == DesignCallbackRules.UseDefault)
        {
            value = DesignCallbacks.Unique(DesignCode.StartupName, document.NamesInUse());
        }
        else if (document.WhyNotCallbackName(StartupProperty, value) is { } why)
        {
            return why;
        }

        // A name typed over the startup function makes a new one in its place: App Designer deletes the old.
        if (startup is not null && document.DeleteCallback(startup) is { } kept)
        {
            return kept;
        }

        string? refused = document.AddStartup(value);
        made = refused is null ? value : null;
        return refused;
    }

    private sealed record Row(string Property, ComboBox Box, Button Go);

    private sealed class GroupWords : IEqualityComparer<DesignCallbackGroup>
    {
        public static GroupWords Instance { get; } = new();

        public bool Equals(DesignCallbackGroup? a, DesignCallbackGroup? b) => a is not null && b is not null && a.Title == b.Title && a.Properties.SequenceEqual(b.Properties);

        public int GetHashCode(DesignCallbackGroup group) => group.Properties.Count;
    }
}
