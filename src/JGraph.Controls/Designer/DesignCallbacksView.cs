using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using JGraph.Controls.Themes;
using JGraph.Scripting.Designer;

namespace JGraph.Controls.Designer;

/// <summary>
/// The inspector's Callbacks tab (designer plan DS5; App Designer's <c>CallbackSection</c>): for the
/// selected component, a row per callback property in App Designer's groups - the property's name,
/// a box holding the callback bound to it, and a button to go to that function. The box offers what
/// App Designer's does: <c>&lt;no callback&gt;</c> to unbind, <c>&lt;add ... callback&gt;</c> for a new
/// function named as App Designer names it, and the app's other callbacks; a name typed makes or
/// binds a function. The figure's rows begin with the app's own <c>StartupFcn</c>, which App Designer
/// shows on its app node.
/// </summary>
public sealed class DesignCallbacksView : UserControl
{
    private const string NoCallback = "<no callback>";
    private const string UseDefault = "AD_Use_Default";
    private const string StartupProperty = "StartupFcn";

    private readonly StackPanel _panel = new() { Margin = new Thickness(4) };
    private readonly List<Row> _rows = [];
    private DesignDocument? _document;
    private DesignComponent? _shown;
    private bool _refreshing;
    private bool _queued;

    public DesignCallbacksView()
    {
        var scroll = new ScrollViewer { Content = _panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        scroll.SetResourceReference(BackgroundProperty, ThemeKeys.Panel);
        Content = scroll;
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, "Callbacks");
    }

    /// <summary>Raised with what an edit was refused for, for the window's status line.</summary>
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
            if (_document?.Selection is [var one] && ReferenceEquals(one, _shown))
            {
                Refresh();
            }
            else
            {
                Rebuild();
            }
        });
    }

    private void Rebuild()
    {
        _panel.Children.Clear();
        _rows.Clear();
        _shown = null;
        if (_document is not { } document)
        {
            return;
        }

        if (document.Selection is not [var component])
        {
            _panel.Children.Add(new TextBlock { Text = "Select one component to edit its callbacks.", Margin = new Thickness(2), TextWrapping = TextWrapping.Wrap });
            return;
        }

        _shown = component;
        var groups = new List<DesignCallbackGroup>();
        if (ReferenceEquals(component, document.Design.Figure))
        {
            groups.Add(new DesignCallbackGroup("APP", [StartupProperty]));
        }

        groups.AddRange(document.CallbackGroupsOf(component));
        if (groups.Count == 0)
        {
            string what = DesignNames.Of(component.Type)?.Text ?? component.Class?.ShortName ?? component.Type;
            _panel.Children.Add(new TextBlock { Text = $"{what} objects do not have callback properties.", Margin = new Thickness(2), TextWrapping = TextWrapping.Wrap });
            return;
        }

        bool titled = groups.Count > 1;
        foreach (DesignCallbackGroup group in groups)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 60 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            foreach (string property in group.Properties)
            {
                int row = grid.RowDefinitions.Count;
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                AddRow(grid, row, component, property);
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

    private void AddRow(Grid grid, int row, DesignComponent component, string property)
    {
        var label = new TextBlock { Text = property, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 6, 1), ToolTip = property };
        var box = new ComboBox { IsEditable = true, Margin = new Thickness(0, 1, 4, 1), MinWidth = 100, ToolTip = "Function name" };
        var go = new Button { Content = "→", Width = 24, Margin = new Thickness(0, 1, 0, 1), ToolTip = "Move cursor to function" };
        string id = $"Callbacks.{component.CodeName}.{property}";
        System.Windows.Automation.AutomationProperties.SetAutomationId(box, id);
        System.Windows.Automation.AutomationProperties.SetName(box, property);
        System.Windows.Automation.AutomationProperties.SetAutomationId(go, id + ".GoTo");
        System.Windows.Automation.AutomationProperties.SetName(go, "Go to " + property);

        // The figure's SizeChangedFcn row is never editable in App Designer's tab (CallbackSection, whatever
        // the app is), nor is a responsive app's region panel's (RegionPanelController.shouldDisableCallback).
        bool locked = property == "SizeChangedFcn" && _document is { } doc && (ReferenceEquals(component, doc.Design.Figure) || doc.IsRegion(component));
        box.IsEnabled = !locked;

        var entry = new Row(property, box, go);
        _rows.Add(entry);
        Grid.SetRow(label, row);
        Grid.SetRow(box, row);
        Grid.SetRow(go, row);
        Grid.SetColumn(label, 0);
        Grid.SetColumn(box, 1);
        Grid.SetColumn(go, 2);
        grid.Children.Add(label);
        grid.Children.Add(box);
        grid.Children.Add(go);
        Show(entry);

        // A choice picked from the open list commits; the arrow keys walking the list while it is
        // closed only change the text, which Enter or leaving the box commits as typed text.
        box.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && box.IsDropDownOpen && box.SelectedItem is Choice chosen)
            {
                Commit(entry, chosen.Value);
            }
        };
        box.DropDownClosed += (_, _) =>
        {
            if (!_refreshing && box.SelectedItem is Choice chosen)
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
        box.LostKeyboardFocus += (_, _) =>
        {
            if (!_refreshing && !box.IsDropDownOpen)
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
        if (_document is not { } document || _shown is not { } component)
        {
            return null;
        }

        return property == StartupProperty ? document.Callbacks.FirstOrDefault(static c => c.IsStartup)?.Name : document.CallbackOf(component, property);
    }

    /// <summary>The box's choices (<c>CallbackSection._buildCallbacksList</c>): unbind, a new function, the current one, then the others sorted.</summary>
    private IEnumerable<Choice> Choices(string property, string current)
    {
        if (current.Length > 0)
        {
            yield return new Choice(NoCallback, string.Empty);
        }

        string stem = property.EndsWith("Callback", StringComparison.Ordinal) ? property[..^"Callback".Length] : property;
        yield return new Choice($"<add {stem} callback>", UseDefault);
        if (current.Length > 0)
        {
            yield return new Choice(current, current);
        }

        bool startup = property == StartupProperty;
        foreach (DesignCallback callback in (_document?.Callbacks ?? []).Where(c => !c.IsGenerated && c.IsStartup == startup && c.Name != current).OrderBy(static c => c.Name, StringComparer.CurrentCulture))
        {
            yield return new Choice(callback.Name, callback.Name);
        }
    }

    private void Show(Row row)
    {
        _refreshing = true;
        try
        {
            string current = Current(row.Property) ?? string.Empty;
            row.Box.ItemsSource = Choices(row.Property, current).ToArray();
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
    /// What App Designer's row does with a value (<c>CallbackSection._handleCallbackNameEdited</c> and
    /// <c>MassEditCallbackCommand</c>): nothing when unchanged; a new function for the default entry, named as
    /// App Designer names it; an existing callback bound (or the property moved to it); a name no callback
    /// has made as a new function; an empty value unbinding the property - the startup function deleted,
    /// its input parameters with it.
    /// </summary>
    private void Commit(Row row, string value)
    {
        if (_document is not { } document || _shown is not { } component)
        {
            return;
        }

        value = value.Trim();
        if (value == NoCallback)
        {
            value = string.Empty;
        }

        string old = Current(row.Property) ?? string.Empty;
        if (value == old)
        {
            Show(row);
            return;
        }

        bool startup = row.Property == StartupProperty;
        string? refused = null;
        if (value == UseDefault)
        {
            value = startup ? DesignCallbacks.Unique(DesignCode.StartupName, document.NamesInUse()) : document.DefaultCallbackName(component, row.Property);
        }
        else if (value.Length > 0)
        {
            DesignCallback? existing = document.Callbacks.FirstOrDefault(c => c.Name == value);
            if (startup && existing is not null)
            {
                refused = "Startup function name must be unique.";
            }
            else if (!startup && existing is { IsStartup: true })
            {
                refused = "Name cannot be the same as the startup function.";
            }
            else if (existing is null)
            {
                refused = document.WhyNotFunctionName(value);
            }
        }

        // Only a function made here takes the user to the Code View (AddCallbackCommand); binding a
        // property to one the app has (Attach, Reassign) switches nothing.
        bool existed = document.Callbacks.Any(c => c.Name == value);
        if (refused is null)
        {
            refused = startup ? CommitStartup(document, old, value) : CommitComponent(document, component, row.Property, old, value);
        }

        if (refused is not null)
        {
            Problem?.Invoke(this, $"{component.CodeName}.{row.Property}: {refused}");
        }
        else if (value.Length > 0 && !existed && document.Callbacks.Any(c => c.Name == value))
        {
            GoTo?.Invoke(this, value);
        }

        Show(row);
    }

    private static string? CommitStartup(DesignDocument document, string old, string value)
    {
        DesignCallback? startup = document.Callbacks.FirstOrDefault(static c => c.IsStartup);
        if (value.Length == 0)
        {
            return startup is null ? null : document.DeleteCallback(startup);
        }

        // A name typed over the startup function makes a new one in its place: App Designer deletes the old.
        if (startup is not null && old != value && document.DeleteCallback(startup) is { } why)
        {
            return why;
        }

        return document.AddStartup(value);
    }

    private static string? CommitComponent(DesignDocument document, DesignComponent component, string property, string old, string value)
    {
        if (value.Length == 0)
        {
            return document.BindCallback(component, property, null);
        }

        bool exists = document.Callbacks.Any(c => c.Name == value && !c.IsStartup && !c.IsGenerated);
        return exists ? document.BindCallback(component, property, value) : document.AddCallback(component, property, value);
    }

    private sealed record Choice(string Label, string Value)
    {
        public override string ToString() => Label;
    }

    private sealed record Row(string Property, ComboBox Box, Button Go);
}
