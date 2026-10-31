using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using JGraph.Controls.Themes;
using JGraph.Scripting.Designer;

namespace JGraph.Controls.Designer;

/// <summary>
/// The visual designer's inspector (designer plan DS3): the selected component's code name, and its
/// properties in App Designer's groups (<see cref="DesignInspector"/>), each edited as App Designer's
/// inspector edits it - text, on/off, a choice, a colour, a context menu, or the value as MATLAB
/// writes it. An edit goes to the document, which sets it through the component and keeps what the
/// component kept, and the rows show what it holds. What the generator never writes is shown and not edited.
/// Over several components (stage W7b) the rows are those they share, showing the last's values, and an edit is
/// one step on all of them. A toolbar searches the rows and chooses the group or the alphabetical view
/// (<c>InspectorToolbar</c>, <c>InspectorClientManager</c>); what is open and which view shows are kept per
/// app for each class or set of classes, and a selection clears the search.
/// </summary>
public sealed partial class DesignInspectorView : UserControl
{
    private readonly StackPanel _panel = new() { Margin = new Thickness(4) };
    private readonly StackPanel _header = new();
    private readonly StackPanel _groupPanel = new();
    private readonly StackPanel _alphaPanel = new() { Visibility = Visibility.Collapsed };
    private readonly TextBox _search = new() { Height = 22, Padding = new Thickness(2, 1, 16, 1), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock _searchHint = new() { Text = "Search", Opacity = 0.55, IsHitTestVisible = false, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _clear = new() { Content = "\u00D7", Width = 16, Height = 16, Padding = new Thickness(0), Margin = new Thickness(0, 0, 3, 0), BorderThickness = new Thickness(0), Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, ToolTip = "Clear search", Focusable = false };
    private readonly ToggleButton _groupView = new() { Content = "\u2630", Width = 24, Height = 22, Padding = new Thickness(0), ToolTip = "Group View" };
    private readonly ToggleButton _alphaView = new() { Content = "AZ", Width = 24, Height = 22, Padding = new Thickness(0), Margin = new Thickness(3, 0, 0, 0), FontSize = 10, ToolTip = "Alphabetical View" };
    private readonly DockPanel _toolbar = new() { Margin = new Thickness(5, 3, 5, 3) };
    private readonly ConditionalWeakTable<DesignDocument, DesignInspectorState> _states = new();
    private readonly List<Row> _rows = [];
    private readonly List<Row> _alphaRows = [];
    private DesignDocument? _document;
    private DesignInspection? _inspection;
    private DesignInspectorState.Entry? _state;
    private string _keyShown = string.Empty;
    private DesignComponent? _shown;
    private bool _alphaBuilt;
    private bool _settingView;
    private TextBox? _labelText;
    private string? _labelShown;
    private bool _refreshing;
    private bool _queued;
    private TextBox? _name;
    private string _nameShown = string.Empty;
    private string _tab = "Component";

    public DesignInspectorView()
    {
        System.Windows.Automation.AutomationProperties.SetAutomationId(_search, "Inspector.Search");
        System.Windows.Automation.AutomationProperties.SetName(_search, "Search");
        System.Windows.Automation.AutomationProperties.SetAutomationId(_clear, "Inspector.ClearSearch");
        System.Windows.Automation.AutomationProperties.SetName(_clear, "Clear search");
        System.Windows.Automation.AutomationProperties.SetAutomationId(_groupView, "Inspector.GroupView");
        System.Windows.Automation.AutomationProperties.SetName(_groupView, "Group View");
        System.Windows.Automation.AutomationProperties.SetAutomationId(_alphaView, "Inspector.AlphabeticalView");
        System.Windows.Automation.AutomationProperties.SetName(_alphaView, "Alphabetical View");

        // R2025b's .ToolbarContainer: 3 by 5 around a 22 high field, then its two buttons.
        var searchHost = new Grid { Margin = new Thickness(0, 0, 5, 0) };
        searchHost.Children.Add(_search);
        searchHost.Children.Add(_searchHint);
        searchHost.Children.Add(_clear);
        DockPanel.SetDock(_alphaView, Dock.Right);
        DockPanel.SetDock(_groupView, Dock.Right);
        _toolbar.Children.Add(_alphaView);
        _toolbar.Children.Add(_groupView);
        _toolbar.Children.Add(searchHost);
        _search.TextChanged += (_, _) => ShowSearch();
        _search.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                _search.Text = string.Empty;
                e.Handled = true;
            }
        };
        _clear.Click += (_, _) => _search.Text = string.Empty;

        // Two buttons of one group (buttonGroup): pressing the one that is on leaves it on.
        _groupView.Checked += (_, _) => ChooseView(alphabetical: false);
        _alphaView.Checked += (_, _) => ChooseView(alphabetical: true);
        _groupView.Unchecked += (_, _) => ShowView();
        _alphaView.Unchecked += (_, _) => ShowView();

        _panel.Children.Add(_header);
        _panel.Children.Add(_groupPanel);
        _panel.Children.Add(_alphaPanel);
        Grid.SetIsSharedSizeScope(_alphaPanel, true);
        var scroll = new ScrollViewer { Content = _panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        scroll.SetResourceReference(BackgroundProperty, ThemeKeys.Panel);
        var root = new DockPanel();
        DockPanel.SetDock(_toolbar, Dock.Top);
        root.Children.Add(_toolbar);
        root.Children.Add(scroll);
        Content = root;
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, "Inspector");
    }

    /// <summary>Raised with what an edit was refused for, which App Designer says in its alert (<c>_showErrorDialog</c>).</summary>
    public event EventHandler<string>? Problem;

    /// <summary>Raised when what the first tab says changes.</summary>
    public event EventHandler? TabChanged;

    /// <summary>What the first tab says (<c>_updateContentPaneTitle</c>): the component's class, <c>Multiple Components</c> or <c>Component</c>.</summary>
    public string Tab => _tab;

    /// <summary>Whether anything is selected to inspect; the tabs hide when not (<c>_tabContainer.hide</c>).</summary>
    public bool HasSelection => _document is { AppSelected: true } or { SelectionIsEmpty: false };

    /// <summary>The app whose selection is inspected.</summary>
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

    /// <summary>Changes come in runs (a drag's every step); the rows are brought up to date once the run is over.</summary>
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
            // The same components, unless one went into or out of a grid, which changes what is edited.
            if (_document?.Inspection is { } now && _inspection is { } was && now.Components.SequenceEqual(was.Components)
                && now.Labeled == was.Labeled && SheetKey(now) == _keyShown)
            {
                _inspection = now;
                SayTab();
                RefreshValues();
            }
            else
            {
                Rebuild();
            }
        });
    }

    private string SheetKey(DesignInspection inspection)
    {
        (string key, int properties) = DesignInspectorSheet.KeyOf(inspection);
        int rows = _document is { } d ? d.SheetOf(inspection).Sum(static g => g.Rows.Count) : 0;
        return $"{key}|{properties}|{rows}";
    }

    // --- building ----------------------------------------------------------------------------------

    private void SayTab()
    {
        _tab = _document?.InspectorTab ?? DesignInspectorSheet.NoComponent;
        TabChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Rebuild()
    {
        _header.Children.Clear();
        _groupPanel.Children.Clear();
        _alphaPanel.Children.Clear();
        _rows.Clear();
        _alphaRows.Clear();
        _alphaBuilt = false;
        _name = null;
        _shown = null;
        _labelText = null;
        _state = null;
        _keyShown = string.Empty;
        _inspection = _document?.Inspection;
        SayTab();
        _search.Text = string.Empty; // a selection clears the search (InspectorClientManager.inspect)
        if (_document is not { } document || _inspection is not { } inspection)
        {
            _toolbar.Visibility = Visibility.Collapsed;
            if (_document is { AppSelected: true } app)
            {
                _header.Children.Add(new TextBlock { Text = app.AppName, Margin = new Thickness(2) });
            }

            ShowView();
            return;
        }

        _toolbar.Visibility = Visibility.Visible;
        DesignComponent component = inspection.Anchor;
        _shown = component;
        (string key, int properties) = DesignInspectorSheet.KeyOf(inspection);
        _state = _states.GetValue(document, static _ => new DesignInspectorState()).For(key, properties);
        _keyShown = SheetKey(inspection);

        // The code name is JGraph's own row, over one component's rows in either view.
        if (inspection.Kind is DesignInspectionKind.Single or DesignInspectionKind.Labeled)
        {
            _header.Children.Add(CodeNameRow(component));
        }

        // A labelled component's label text, over the field's groups (LabeledComponentTemplate).
        if (inspection.Labeled is { Label: { } pairLabel })
        {
            var labelRow = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var caption = new TextBlock { Text = "Label", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            DockPanel.SetDock(caption, Dock.Left);
            _labelText = new TextBox { Text = (pairLabel.Get("Text") as CharValue)?.Text ?? string.Empty, ToolTip = "The label's text: app." + pairLabel.CodeName + ".Text" };
            _labelShown = _labelText.Text;
            Identify(_labelText, "Inspector.LabelText", "Label text");
            Commits(_labelText, () => _labelShown ?? string.Empty, text =>
            {
                if (_document is { } d && _inspection is { Labeled.Label: { } l } && d.SetProperty(l, "Text", new CharValue(text)) is { } why)
                {
                    Say(why);
                }
            });
            labelRow.Children.Add(caption);
            labelRow.Children.Add(_labelText);
            _groupPanel.Children.Add(labelRow);
        }

        foreach (DesignInspectorGroup group in document.SheetOf(inspection))
        {
            _groupPanel.Children.Add(BuildGroup(component, group));
        }

        ShowView();
        RefreshValues();
    }

    private FrameworkElement CodeNameRow(DesignComponent component)
    {
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        _name = new TextBox { Text = component.CodeName, FontWeight = FontWeights.SemiBold, ToolTip = "The component's code name: app." + component.CodeName };
        _nameShown = component.CodeName;
        System.Windows.Automation.AutomationProperties.SetAutomationId(_name, "Inspector.CodeName");
        System.Windows.Automation.AutomationProperties.SetName(_name, "Code name");
        Commits(_name, () => _nameShown, text =>
        {
            if (_document is { } d && _shown is { } c && d.Rename(c, text.Trim()) is { } why)
            {
                Say(why);
                _name!.Text = _nameShown = c.CodeName;
            }
        });
        var type = new TextBlock { Text = component.Type, Opacity = 0.7, Margin = new Thickness(0, 2, 0, 0) };
        DockPanel.SetDock(type, Dock.Bottom);
        header.Children.Add(type);
        header.Children.Add(_name);
        return header;
    }

    private static Grid NewRowGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto), MinWidth = 90, SharedSizeGroup = "InspectorNames" });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return grid;
    }

    private void AddRow(Grid grid, Row row)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        int at = grid.RowDefinitions.Count - 1;
        Grid.SetRow(row.Label, at);
        Grid.SetRow(row.Control, at);
        Grid.SetColumn(row.Control, 1);
        grid.Children.Add(row.Label);
        grid.Children.Add(row.Control);
    }

    /// <summary>A group: its rows, with each sub-group's behind the bar under them (<c>InspectorPropertySubGroup</c>).</summary>
    private Expander BuildGroup(DesignComponent component, DesignInspectorGroup group)
    {
        var body = new StackPanel { Margin = new Thickness(2, 2, 2, 6) };
        Grid.SetIsSharedSizeScope(body, true);
        bool open = _state!.Open.TryGetValue(group.Title, out bool kept) ? kept : group.Expanded;
        var expander = new Expander { Header = group.Title, IsExpanded = open, Content = body };
        System.Windows.Automation.AutomationProperties.SetName(expander, group.Title);
        var groupRows = new List<Row>();
        var subRows = new Dictionary<int, List<Row>>();
        var subOpen = new Dictionary<int, bool>();
        foreach (IGrouping<(int? Sub, int Run), DesignInspectorRow> run in Runs(group.Rows))
        {
            Grid grid = NewRowGrid();
            int? sub = run.Key.Sub;
            var built = new List<Row>();
            foreach (DesignEditor shown in run.SelectMany(static r => r.Editors))
            {
                Row row = BuildRow(component, shown);
                AddRow(grid, row);
                built.Add(row);
            }

            groupRows.AddRange(built);
            if (sub is not { } number)
            {
                body.Children.Add(grid);
                continue;
            }

            string key = group.Title + "#" + number;
            bool shownOpen = _state.Open.TryGetValue(key, out bool was) && was;
            subRows[number] = built;
            subOpen[number] = shownOpen;
            grid.Visibility = shownOpen ? Visibility.Visible : Visibility.Collapsed;
            body.Children.Add(grid);
            body.Children.Add(SubGroupBar(group.Title, number, grid, built, () => expander.IsExpanded, shownOpen, key, subOpen));
        }

        void Settle()
        {
            foreach (Row row in groupRows)
            {
                bool inClosedSub = subRows.Any(s => !subOpen[s.Key] && s.Value.Contains(row));
                row.Visible = expander.IsExpanded && !inClosedSub;
            }
        }

        // A collapsed group reads its values when it is opened.
        expander.Expanded += (_, _) =>
        {
            _state!.Open[group.Title] = true;
            Settle();
            foreach (Row row in groupRows.Where(static r => r.Visible))
            {
                Show(row);
            }
        };
        expander.Collapsed += (_, _) =>
        {
            _state!.Open[group.Title] = false;
            Settle();
        };
        Settle();
        _rows.AddRange(groupRows);
        return expander;
    }

    /// <summary>The rows of a group in runs: those of the group itself, and each sub-group's.</summary>
    private static IEnumerable<IGrouping<(int? Sub, int Run), DesignInspectorRow>> Runs(IReadOnlyList<DesignInspectorRow> rows)
    {
        int run = 0;
        int? last = null;
        var keyed = new List<((int? Sub, int Run) Key, DesignInspectorRow Row)>();
        foreach (DesignInspectorRow row in rows)
        {
            if (row.SubGroup != last)
            {
                run++;
                last = row.SubGroup;
            }

            keyed.Add(((row.SubGroup, run), row));
        }

        return keyed.GroupBy(static k => k.Key, static k => k.Row);
    }

    /// <summary>The 12 pixel bar under a sub-group's rows, with its arrow (R2025b's <c>.property-more</c>).</summary>
    private Button SubGroupBar(string group, int number, Grid grid, List<Row> rows, Func<bool> groupOpen, bool shownOpen, string key, Dictionary<int, bool> subOpen)
    {
        var bar = new Button
        {
            Height = 12,
            Margin = new Thickness(9, shownOpen ? 3 : 0, 9, 2),
            Padding = new Thickness(0),
            FontSize = 8,
            Background = Brushes.Transparent,
            Content = shownOpen ? "\u25B2" : "\u25BC",
        };
        void Say()
        {
            System.Windows.Automation.AutomationProperties.SetName(bar, subOpen[number] ? "Fewer Properties" : "More Properties");
        }

        System.Windows.Automation.AutomationProperties.SetAutomationId(bar, $"Inspector.More.{group}.{number}");
        Say();
        bar.Click += (_, _) =>
        {
            bool open = !subOpen[number];
            subOpen[number] = open;
            _state!.Open[key] = open;
            grid.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            bar.Content = open ? "\u25B2" : "\u25BC";
            bar.Margin = new Thickness(9, open ? 3 : 0, 9, 2);
            Say();
            foreach (Row row in rows)
            {
                row.Visible = open && groupOpen();
                if (row.Visible)
                {
                    Show(row);
                }
            }
        };
        return bar;
    }

    /// <summary>One property's row: what it says, with its help, and its editor - shown and not edited where the component's place keeps it (DS4).</summary>
    private Row BuildRow(DesignComponent component, DesignEditor shown)
    {
        DesignEditor editor = _document?.IsEditable(component, shown.Name) == false
            ? shown with { Kind = DesignEditorKind.ReadOnly }
            : shown;
        Row row = Build(component, editor);
        row.Label = new TextBlock { Text = editor.DisplayName, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 6, 1) };
        if (Help(editor) is { } help)
        {
            row.Label.ToolTip = help;
            ToolTipService.SetInitialShowDelay(row.Label, 1000);
        }

        // On the editor too, for the row's own help (not just a long name).
        if (editor.Tooltip.Length > 0 && Help(editor) is { } second)
        {
            row.Control.ToolTip = second;
            ToolTipService.SetInitialShowDelay(row.Control, 1000);
        }

        return row;
    }

    /// <summary>A row's help (<c>_createTooltip</c>); null for none.</summary>
    private static FrameworkElement? Help(DesignEditor editor)
    {
        (string? name, string? heading, string? body) = DesignInspectorSheet.Help(editor);
        if (name is null && body is null)
        {
            return null;
        }

        var tip = new StackPanel { MaxWidth = 360 };
        if (name is not null)
        {
            tip.Children.Add(new TextBlock { Text = name });
            if (body is not null)
            {
                tip.Children.Add(new Separator());
            }
        }

        if (heading is not null)
        {
            tip.Children.Add(new TextBlock { Text = heading, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
        }

        if (body is not null)
        {
            tip.Children.Add(new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap });
        }

        return tip;
    }

    // --- the views and the search ---------------------------------------------------------------------

    private void ChooseView(bool alphabetical)
    {
        if (_settingView)
        {
            return;
        }

        if (_state is { } state)
        {
            state.Alphabetical = alphabetical; // _viewPropertiesChanged: kept for this class or set of classes
        }

        ShowView();
    }

    /// <summary>
    /// The view the state and the search ask for (<c>InspectorToolbar._handleSearch</c>): a search is always over the
    /// alphabetical view, both buttons off; cleared, the view chosen comes back.
    /// </summary>
    private void ShowView()
    {
        if (_settingView)
        {
            return;
        }

        bool searching = _search.Text.Length > 0;
        bool chosen = _state?.Alphabetical == true;
        bool alphabetical = searching || chosen;
        bool wasShown = _alphaPanel.Visibility == Visibility.Visible;
        if (alphabetical && _inspection is not null && !_alphaBuilt)
        {
            BuildAlphabetical();
        }

        _groupPanel.Visibility = alphabetical ? Visibility.Collapsed : Visibility.Visible;
        _alphaPanel.Visibility = alphabetical ? Visibility.Visible : Visibility.Collapsed;
        _settingView = true;
        _groupView.IsChecked = !chosen;
        _alphaView.IsChecked = chosen;
        _settingView = false;
        _groupView.IsEnabled = !searching;
        _alphaView.IsEnabled = !searching;
        foreach (Row row in _alphaRows)
        {
            row.Visible = alphabetical;
        }

        if (alphabetical)
        {
            ArrangeAlphabetical();
            if (!wasShown)
            {
                foreach (Row row in _alphaRows)
                {
                    Show(row);
                }
            }
        }
    }

    private void BuildAlphabetical()
    {
        _alphaBuilt = true;
        if (_document is not { } document || _inspection is not { } inspection)
        {
            return;
        }

        foreach (DesignEditor editor in DesignInspectorSheet.Alphabetical(document.SheetOf(inspection)))
        {
            Row row = BuildRow(inspection.Anchor, editor);
            row.Holder = NewRowGrid();
            AddRow(row.Holder, row);
            _alphaRows.Add(row);
        }
    }

    private void ShowSearch()
    {
        bool typed = _search.Text.Length > 0;
        _searchHint.Visibility = typed ? Visibility.Collapsed : Visibility.Visible;
        _clear.Visibility = typed ? Visibility.Visible : Visibility.Collapsed;
        ShowView();
    }

    /// <summary>The alphabetical rows as the search leaves them (<c>_handleSearch</c>), what it found marked.</summary>
    private void ArrangeAlphabetical()
    {
        string text = _search.Text;
        DesignInspectorMatches matches = DesignInspectorSheet.Search(_alphaRows.Select(static r => r.Editor).ToArray(), text);
        _alphaPanel.Children.Clear();
        Row Of(DesignEditor editor) => _alphaRows.First(r => ReferenceEquals(r.Editor, editor) || r.Editor.SheetName == editor.SheetName);
        foreach (DesignEditor editor in matches.Direct)
        {
            Row row = Of(editor);
            Mark(row.Label, editor.DisplayName, text);
            _alphaPanel.Children.Add(row.Holder!);
        }

        if (matches.Help.Count > 0)
        {
            _alphaPanel.Children.Add(SearchWords(DesignInspectorSheet.HelpResults, bold: true, after: matches.Direct.Count > 0));
            foreach (DesignEditor editor in matches.Help)
            {
                Row row = Of(editor);
                Mark(row.Label, editor.DisplayName, string.Empty);
                _alphaPanel.Children.Add(row.Holder!);
            }
        }
        else if (matches.None && _alphaRows.Count > 0)
        {
            _alphaPanel.Children.Add(SearchWords(DesignInspectorSheet.NoMatches, bold: false, after: false));
        }
    }

    /// <summary>R2025b's .searchMatchesLabel: 22 high, 10 in; the help's heading bold, and 22 under what was found directly.</summary>
    private static TextBlock SearchWords(string words, bool bold, bool after)
    {
        var said = new TextBlock
        {
            Text = words,
            Height = 22,
            Padding = new Thickness(10, 3, 0, 0),
            Margin = new Thickness(0, after ? 22 : 0, 0, 0),
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(said, "Inspector.SearchWords");
        System.Windows.Automation.AutomationProperties.SetName(said, words);
        return said;
    }

    private static void Mark(TextBlock label, string name, string text)
    {
        label.Inlines.Clear();
        int at = text.Length == 0 ? -1 : name.IndexOf(text, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
        {
            label.Text = name;
            return;
        }

        int from = 0;
        for (; at >= 0; at = name.IndexOf(text, from, StringComparison.OrdinalIgnoreCase))
        {
            label.Inlines.Add(new System.Windows.Documents.Run(name[from..at]));
            label.Inlines.Add(new System.Windows.Documents.Run(name.Substring(at, text.Length)) { Background = Brushes.Gold, Foreground = Brushes.Black });
            from = at + text.Length;
        }

        label.Inlines.Add(new System.Windows.Documents.Run(name[from..]));
    }

    /// <summary>One property's editor, and how it shows the value.</summary>
    private sealed class Row(DesignEditor editor, FrameworkElement control, Action<DesignValue?> show)
    {
        public DesignEditor Editor { get; } = editor;

        public FrameworkElement Control { get; } = control;

        public Action<DesignValue?> ShowValue { get; } = show;

        public TextBlock Label { get; set; } = null!;

        public Grid? Holder { get; set; }

        public bool Visible { get; set; }
    }

    private Row Build(DesignComponent component, DesignEditor editor)
    {
        string id = "Inspector." + editor.Name;
        switch (editor.Kind)
        {
            case DesignEditorKind.OnOff or DesignEditorKind.Logical:
            {
                var box = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) };
                Identify(box, id, editor.Name);
                RoutedEventHandler changed = (_, _) =>
                {
                    if (!_refreshing)
                    {
                        bool on = box.IsChecked == true;
                        Set(editor.Name, editor.Kind == DesignEditorKind.OnOff ? (on ? OnOffValue.On : OnOffValue.Off) : NumericValue.Logical(on));
                    }
                };
                box.Checked += changed;
                box.Unchecked += changed;
                return new Row(editor, box, value => box.IsChecked = value switch
                {
                    OnOffValue o => o.IsOn,
                    CharValue c => c.Text == "on",
                    NumericValue { Data: [var d] } => d != 0,
                    _ => false,
                });
            }

            case DesignEditorKind.Choice or DesignEditorKind.Component:
            {
                var combo = new ComboBox { Margin = new Thickness(0, 1, 0, 1) };
                Identify(combo, id, editor.Name);
                bool menus = editor.Kind == DesignEditorKind.Component;
                IEnumerable<string> choices = menus
                    ? [string.Empty, .. (_document?.Design.Components ?? []).Where(static c => c.Type == "matlab.ui.container.ContextMenu").Select(static c => c.CodeName)]
                    : editor.Choices;
                foreach (string choice in choices)
                {
                    combo.Items.Add(choice);
                }

                combo.SelectionChanged += (_, _) =>
                {
                    if (!_refreshing && combo.SelectedItem is string chosen)
                    {
                        Set(editor.Name, menus
                            ? chosen.Length == 0 ? null : new ComponentsValue([chosen], bracketed: false)
                            : new CharValue(chosen));
                    }
                };
                return new Row(editor, combo, value =>
                {
                    string shown = value switch
                    {
                        ComponentsValue { CodeNames: [var one] } => one,
                        CharValue c => c.Text,
                        StringValue s => s.Text,
                        _ => string.Empty,
                    };
                    if (!combo.Items.Contains(shown))
                    {
                        combo.Items.Add(shown);
                    }

                    combo.SelectedItem = shown;
                });
            }

            case DesignEditorKind.ReadOnly:
            {
                var text = new TextBlock { Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                Identify(text, id, editor.Name);
                return new Row(editor, text, value => text.Text = value is null ? string.Empty : Shown(value, plain: false));
            }

            case DesignEditorKind.Lines or DesignEditorKind.Items or DesignEditorKind.Toggle or DesignEditorKind.ToggleGroup
                or DesignEditorKind.FontName or DesignEditorKind.FontSize or DesignEditorKind.Fields or DesignEditorKind.Spinner
                or DesignEditorKind.File or DesignEditorKind.ScaleColors or DesignEditorKind.Ticks or DesignEditorKind.Columns
                or DesignEditorKind.ViewFormat or DesignEditorKind.Date or DesignEditorKind.DateLimits or DesignEditorKind.DisabledDates
                or DesignEditorKind.DaysOfWeek or DesignEditorKind.Color or DesignEditorKind.ColorList:
                return BuildEditor(editor, id);

            default:
            {
                // Text, or the value as MATLAB writes it.
                bool plain = editor.Kind == DesignEditorKind.Text;
                var box = new TextBox { Margin = new Thickness(0, 1, 0, 1) };
                Identify(box, id, editor.Name);
                string current = string.Empty;
                Commits(box, () => current, text =>
                {
                    if (!plain && component.Type == DesignDocument.GridType && editor.Name is "RowHeight" or "ColumnWidth"
                        && _document is { } document)
                    {
                        // A grid's tracks take App Designer's comma form too, and keep room for its components.
                        (DesignValue? tracks, string? refused) = document.ParseTracks(
                            component, editor.Name == "RowHeight" ? GridAxis.Row : GridAxis.Column, text);
                        if (tracks is null)
                        {
                            Say(refused ?? string.Empty);
                            RefreshValues();
                            return;
                        }

                        Set(editor.Name, tracks);
                        return;
                    }

                    DesignValue value = plain ? new CharValue(text) : DesignValueText.Parse(text);
                    Set(editor.Name, value);
                });
                return new Row(editor, box, value =>
                {
                    // A box being typed in keeps what is typed; one showing what it last showed takes the new value.
                    bool untouched = box.Text == current || !box.IsKeyboardFocusWithin;
                    current = value is null ? string.Empty : Shown(value, plain);
                    if (untouched)
                    {
                        box.Text = current;
                    }
                });
            }
        }
    }

    /// <summary>A value as the inspector shows it: text as itself, anything else as MATLAB writes it.</summary>
    private static string Shown(DesignValue value, bool plain) => value switch
    {
        CharValue c when plain => c.Text,
        StringValue s when plain => s.Text,
        CellValue { Items: [CharValue one] } when plain => one.Text,
        _ => DesignValueText.Format(value),
    };

    private static void Identify(FrameworkElement element, string id, string name)
    {
        System.Windows.Automation.AutomationProperties.SetAutomationId(element, id);
        System.Windows.Automation.AutomationProperties.SetName(element, name);
    }

    /// <summary>A text box that commits on Enter and on losing focus when its text changed, and goes back on Escape.</summary>
    private static void Commits(TextBox box, Func<string> shown, Action<string> commit)
    {
        void Commit()
        {
            if (box.Text != shown())
            {
                commit(box.Text);
            }
        }

        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                box.Text = shown();
                e.Handled = true;
            }
        };
        box.LostKeyboardFocus += (_, _) => Commit();
    }

    // --- values ------------------------------------------------------------------------------------

    private void Set(string name, DesignValue? value)
    {
        if (_document is not { } document || _inspection is not { } inspection)
        {
            return;
        }

        if (document.SetInspected(inspection, name, value) is { } refused)
        {
            Say(refused);
            RefreshValues();
        }
    }

    /// <summary>Says a refusal once the focus has settled, as R2025b's alert waits for its timer (<c>_showErrorDialog</c>).</summary>
    private void Say(string refused) => Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Problem?.Invoke(this, refused));

    private void RefreshValues()
    {
        if (_document is not { } document || _shown is not { } component)
        {
            return;
        }

        if (_name is not null && (_name.Text == _nameShown || !_name.IsKeyboardFocusWithin))
        {
            _name.Text = _nameShown = component.CodeName;
        }
        else
        {
            _nameShown = component.CodeName;
        }

        if (_labelText is not null && _inspection is { Labeled.Label: { } label })
        {
            string text = (label.Get("Text") as CharValue)?.Text ?? string.Empty;
            if (_labelText.Text == _labelShown || !_labelText.IsKeyboardFocusWithin)
            {
                _labelText.Text = text;
            }

            _labelShown = text;
        }

        foreach (Row row in _rows.Concat(_alphaRows).Where(static r => r.Visible))
        {
            Show(row);
        }
    }

    private void Show(Row row)
    {
        if (_document is not { } document || _shown is not { } component)
        {
            return;
        }

        _refreshing = true;
        try
        {
            row.ShowValue(row.Editor.Name == "ContextMenu" ? component.Get("ContextMenu") : document.ValueOf(component, row.Editor.Name));
        }
        finally
        {
            _refreshing = false;
        }
    }
}
