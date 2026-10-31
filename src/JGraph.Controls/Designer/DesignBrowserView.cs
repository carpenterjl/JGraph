using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using JGraph.Controls.Themes;
using JGraph.Scripting.Designer;

namespace JGraph.Controls.Designer;

/// <summary>
/// App Designer's component browser (designer plan DS3, App Designer window plan W7a): over its toolbar's
/// search and View drop-down (<c>ComponentTreeToolbar</c>), the app's row and its components in the view
/// chosen (<see cref="DesignBrowser.Rows"/>), each saying <c>app.CodeName</c>; or, collapsed, the breadcrumbs
/// to the selection. Rows are picked with Ctrl and Shift as well, renamed in place (F2, a double click, a
/// typed character, <c>ComponentTreeEditor</c>), dragged among their siblings and to other parents with a
/// line where they would land (<c>ReorderUtility</c>) and have the menu <see cref="MenuProvider"/> gives.
/// The rules are in <see cref="DesignBrowser"/>, free of the screen.
/// </summary>
public sealed class DesignBrowserView : UserControl
{
    /// <summary>The top and bottom of a row that a drag there drops beside it rather than on it (<c>ROW_DIVISION</c>'s "5 auto 5").</summary>
    private const double DropEdge = 5;

    private readonly TreeView _tree = new() { BorderThickness = new Thickness(0) };
    private readonly TextBox _search = new() { VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock _searchHint = new() { Text = "Search", Opacity = 0.55, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
    private readonly Button _clear = new() { Content = "×", Width = 18, Padding = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Visibility = Visibility.Collapsed, ToolTip = "Clear search" };
    private readonly Button _viewButton = new() { MinWidth = 26, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(4, 0, 4, 0) };
    private readonly TextBlock _noMatches = new() { Text = DesignBrowser.NoMatches, Margin = new Thickness(10, 10, 2, 2), Visibility = Visibility.Collapsed };
    private readonly WrapPanel _crumbs = new() { Margin = new Thickness(2), Visibility = Visibility.Collapsed };
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly Rectangle _dropLine = new() { Height = 2, Fill = Brushes.DodgerBlue, Visibility = Visibility.Collapsed };
    private readonly Dictionary<DesignComponent, TreeViewItem> _items = new(ReferenceEqualityComparer.Instance);
    private readonly ConditionalWeakTable<DesignDocument, DesignBrowserState> _states = new();
    private readonly List<TreeViewItem> _painted = [];
    private IReadOnlyList<DesignComponent?>? _chosenShown;
    private TreeViewItem? _appItem;
    private DesignDocument? _document;
    private IReadOnlyList<DesignBrowserRow> _rows = [];
    private bool _showing;
    private TreeViewItem? _justClosed;
    private string _shape = string.Empty;
    private DesignBrowserRow? _boundary;
    private bool _includeLabels;

    // A press kept for its release or a drag.
    private DesignBrowserRow? _pressed;
    private bool _pressDeferred;
    private Point _pressAt;

    // A drag under way.
    private IReadOnlyList<DesignComponent>? _dragging;
    private (DesignComponent Target, DesignBrowserPlace Place, DesignComponent? At)? _dropHere;
    private TreeViewItem? _hovered;
    private readonly DispatcherTimer _autoExpand = new() { Interval = TimeSpan.FromSeconds(1) };

    // The row being renamed.
    private TreeViewItem? _editing;
    private bool _editEnded;

    public DesignBrowserView()
    {
        _tree.SetResourceReference(BackgroundProperty, ThemeKeys.Panel);
        _tree.SetResourceReference(ForegroundProperty, ThemeKeys.Text);
        SetResourceReference(BackgroundProperty, ThemeKeys.Panel);
        AutomationId(_tree, "ComponentBrowser");
        _tree.SelectedItemChanged += (_, _) => Picked();
        _tree.KeyDown += OnTreeKey;
        _tree.PreviewTextInput += OnTreeText;
        _tree.PreviewMouseLeftButtonDown += OnPress;
        _tree.PreviewMouseMove += OnMove;
        _tree.PreviewMouseLeftButtonUp += OnRelease;
        _tree.MouseDoubleClick += OnDoubleClick;
        _tree.PreviewMouseRightButtonDown += OnRightPress;
        _tree.LostMouseCapture += (_, _) => EndDrag(commit: false);
        _tree.ContextMenu = new ContextMenu();
        _tree.ContextMenuOpening += (_, e) =>
        {
            if (!Fill(_tree.ContextMenu))
            {
                e.Handled = true;
            }
        };
        _autoExpand.Tick += (_, _) =>
        {
            _autoExpand.Stop();
            if (_hovered is not null)
            {
                _hovered.IsExpanded = true;
            }
        };

        // ComponentTreeToolbar: the search, then "View:" and its drop-down.
        AutomationId(_search, "ComponentBrowser.Search");
        System.Windows.Automation.AutomationProperties.SetName(_search, "Search");
        _search.TextChanged += (_, _) => Searched();
        _search.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                _search.Text = string.Empty;
                e.Handled = true;
            }
        };
        AutomationId(_clear, "ComponentBrowser.ClearSearch");
        _clear.Click += (_, _) => _search.Text = string.Empty;
        var searchBox = new Grid();
        searchBox.Children.Add(_search);
        searchBox.Children.Add(_searchHint);
        _clear.HorizontalAlignment = HorizontalAlignment.Right;
        searchBox.Children.Add(_clear);

        AutomationId(_viewButton, "ComponentBrowser.View");
        _viewButton.Click += (_, _) => OpenViewMenu();
        var viewLabel = new TextBlock { Text = "View:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 2, 0) };

        // R2025b's .adToolbarTopLine: 22 high, in the 33 the browser's height allows it.
        var toolbar = new DockPanel { Margin = new Thickness(4, 5, 0, 6), Height = 22 };
        DockPanel.SetDock(_viewButton, Dock.Right);
        DockPanel.SetDock(viewLabel, Dock.Right);
        toolbar.Children.Add(_viewButton);
        toolbar.Children.Add(viewLabel);
        toolbar.Children.Add(searchBox);

        AutomationId(_crumbs, "ComponentBrowser.Breadcrumbs");
        AutomationId(_noMatches, "ComponentBrowser.NoMatches");

        _overlay.Children.Add(_dropLine);
        var body = new Grid();
        body.Children.Add(_tree);
        body.Children.Add(_overlay);

        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(_noMatches, Dock.Top);
        DockPanel.SetDock(_crumbs, Dock.Top);
        root.Children.Add(toolbar);
        root.Children.Add(_noMatches);
        root.Children.Add(_crumbs);
        root.Children.Add(body);
        Content = root;
    }

    /// <summary>Raised for a name typed into a row's editor: the window renames, or says why not.</summary>
    public event EventHandler<(DesignComponent Component, string Name)>? RenameEntered;

    /// <summary>Raised with the height the tree asks for (<see cref="DesignBrowser.TopHeight"/>'s <c>asked</c>).</summary>
    public event EventHandler<double>? HeightAsked;

    /// <summary>The menu for what is selected, for an app's browser; given a way to reshow the browser and to open the row's editor.</summary>
    public Func<DesignBrowserState, Action, Action, IReadOnlyList<DesignMenuItem>>? MenuProvider { get; set; }

    /// <summary>Whether a new app's browser includes labels (<c>IncludeComponentLabels</c>).</summary>
    public bool IncludeLabelsSetting
    {
        get => _includeLabels;
        set
        {
            if (_includeLabels != value)
            {
                _includeLabels = value;

                // Every open app's browser follows it (_createSettingServiceListener, one per app).
                foreach (KeyValuePair<DesignDocument, DesignBrowserState> open in _states)
                {
                    open.Value.SettingChanged(value);
                }

                _shape = string.Empty;
                Show();
            }
        }
    }

    /// <summary>The app shown.</summary>
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

            CommitEdit();
            _document = value;
            if (value is not null)
            {
                value.Changed += OnChanged;
            }

            _shape = string.Empty;
            _boundary = null;
            _showing = true;
            _search.Text = State?.Search ?? string.Empty;
            _showing = false;
            Show();
        }
    }

    /// <summary>The browser of the app shown.</summary>
    public DesignBrowserState? State => _document is { } d ? _states.GetValue(d, _ => new DesignBrowserState(_includeLabels)) : null;

    /// <summary>Shows the tree again in place of the breadcrumbs (<c>ObjectBrowser.showTree</c>), or the breadcrumbs (a splitter dragged small).</summary>
    public void SetCollapsed(bool collapsed)
    {
        if (State is { } state && state.Collapsed != collapsed)
        {
            state.Collapsed = collapsed;
            _chosenShown = null;
            Show();
        }
    }

    private void OnChanged(object? sender, EventArgs e) => Show();

    // --- what is shown -----------------------------------------------------------------------------

    /// <summary>The tree again when the rows changed; the selection and the breadcrumbs always.</summary>
    private void Show()
    {
        _showing = true;
        try
        {
            if (_document is not { } document || State is not { } state)
            {
                _tree.Items.Clear();
                _items.Clear();
                _appItem = null;
                _rows = [];
                _noMatches.Visibility = Visibility.Collapsed;
                _crumbs.Visibility = Visibility.Collapsed;
                return;
            }

            _rows = DesignBrowser.Filtered(document.BrowserRows(state.View), state.Search);
            _viewButton.Content = state.View == DesignBrowserList.TabOrder ? "⇄" : "↓";
            _viewButton.ToolTip = state.View == DesignBrowserList.TabOrder ? DesignBrowser.SortByTabOrder : DesignBrowser.SortByStackOrder;
            bool searching = state.Search.Length > 0;
            _noMatches.Visibility = searching && _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            string shape = string.Join("|", _rows.Select(static r => r.Label + "@" + r.Parent?.CodeName)) + "#" + state.Search;
            if (shape != _shape && _editing is null)
            {
                _shape = shape;
                Build(state);
                HeightAsked?.Invoke(this, state.Collapsed ? DesignBrowser.BreadcrumbHeight : DesignBrowser.RowHeight * _rows.Count);
            }

            ShowCrumbs(document, state);
            ShowSelection(document);
        }
        finally
        {
            _showing = false;
        }
    }

    private void Build(DesignBrowserState state)
    {
        _chosenShown = null;
        _painted.Clear();
        _tree.Items.Clear();
        _items.Clear();
        _appItem = null;
        foreach (DesignBrowserRow row in _rows)
        {
            TreeViewItem item = Item(row, state);
            if (row.IsApp)
            {
                _appItem = item;
                _tree.Items.Add(item);
            }
            else if (row.Parent is { } parent && _items.TryGetValue(parent, out TreeViewItem? up))
            {
                up.Items.Add(item);
                _items[row.Component!] = item;
            }
            else
            {
                _appItem?.Items.Add(item);
                _items[row.Component!] = item;
            }
        }
    }

    private TreeViewItem Item(DesignBrowserRow row, DesignBrowserState state)
    {
        bool closed = state.Search.Length == 0 && row.Component is { } c && state.Closed.Contains(c);
        var item = new TreeViewItem { Tag = row, IsExpanded = !closed, Header = Header(row.Label, state.Search), ToolTip = row.Component?.Type ?? "App" };
        System.Windows.Automation.AutomationProperties.SetName(item, row.Component?.CodeName ?? row.Label);
        item.Collapsed += (_, e) =>
        {
            if (!ReferenceEquals(e.OriginalSource, item) || _showing)
            {
                return;
            }

            // The app's row closes the browser to its breadcrumbs, and stays open itself (_handleTreeCollapsedEvent).
            if (row.IsApp)
            {
                Closing(item);
                item.IsExpanded = true;
                SetCollapsed(true);
                HeightAsked?.Invoke(this, DesignBrowser.BreadcrumbHeight);
            }
            else
            {
                Closing(item);
                if (State is { } now && now.Search.Length == 0)
                {
                    now.Closed.Add(row.Component!);
                }
            }
        };
        item.Expanded += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, item) && !_showing && row.Component is { } open)
            {
                State?.Closed.Remove(open);
                if (SelectionInside(item))
                {
                    _chosenShown = null;
                    Show();
                }
            }
        };
        return item;
    }

    // Whatever the tree selects as a row closes is not a pick. The app's row stands open again, and shows the selection.
    private void Closing(TreeViewItem item)
    {
        _justClosed = item;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _justClosed = null;
            if (ReferenceEquals(item, _appItem))
            {
                _chosenShown = null;
                Show();
            }
        });
    }

    // A row's words, 21 high with the item's padding; what a search found marked (Highlight).
    private static TextBlock Header(string label, string search)
    {
        var text = new TextBlock { Height = 17, VerticalAlignment = VerticalAlignment.Center };
        int at = 0;
        foreach ((int start, int count) in DesignBrowser.Matches(label, search))
        {
            if (start > at)
            {
                text.Inlines.Add(new Run(label[at..start]));
            }

            text.Inlines.Add(new Run(label.Substring(start, count)) { Background = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xD7, 0x40)) });
            at = start + count;
        }

        if (at < label.Length)
        {
            text.Inlines.Add(new Run(label[at..]));
        }

        return text;
    }

    private void ShowSelection(DesignDocument document)
    {
        foreach (TreeViewItem painted in _painted)
        {
            painted.ClearValue(BackgroundProperty);
            painted.ClearValue(ForegroundProperty);
        }

        IReadOnlyList<DesignComponent?> chosen = document.BrowserSelection;

        // A selection already shown stays as it stands: a row the user closed over it stays closed.
        if (_chosenShown is not null && chosen.SequenceEqual(_chosenShown, ReferenceEqualityComparer.Instance))
        {
            foreach (TreeViewItem painted in _painted)
            {
                painted.SetResourceReference(BackgroundProperty, ThemeKeys.Selection);
                painted.SetResourceReference(ForegroundProperty, ThemeKeys.SelectionText);
            }

            return;
        }

        _painted.Clear();
        _chosenShown = chosen;
        if (chosen is [var one])
        {
            TreeViewItem? item = one is null ? _appItem : _items.GetValueOrDefault(one);
            if (item is not null)
            {
                Reveal(item);
                item.IsSelected = true;
                item.BringIntoView();
                return;
            }
        }

        if (_tree.SelectedItem is TreeViewItem selected)
        {
            selected.IsSelected = false;
        }

        foreach (DesignComponent? component in chosen)
        {
            TreeViewItem? item = component is null ? _appItem : _items.GetValueOrDefault(component);
            if (item is not null)
            {
                Reveal(item);
                item.SetResourceReference(BackgroundProperty, ThemeKeys.Selection);
                item.SetResourceReference(ForegroundProperty, ThemeKeys.SelectionText);
                _painted.Add(item);
            }
        }
    }

    private static void Reveal(TreeViewItem item)
    {
        for (ItemsControl? up = ItemsControl.ItemsControlFromItemContainer(item); up is TreeViewItem parent; up = ItemsControl.ItemsControlFromItemContainer(parent))
        {
            parent.IsExpanded = true;
        }
    }

    private void ShowCrumbs(DesignDocument document, DesignBrowserState state)
    {
        _tree.Visibility = state.Collapsed ? Visibility.Collapsed : Visibility.Visible;
        _crumbs.Visibility = state.Collapsed ? Visibility.Visible : Visibility.Collapsed;
        if (!state.Collapsed)
        {
            return;
        }

        _crumbs.Children.Clear();
        var expander = new Button { Content = "▶", Width = 18, BorderThickness = new Thickness(0), Background = Brushes.Transparent, ToolTip = "Show the tree" };
        AutomationId(expander, "ComponentBrowser.Expand");
        expander.Click += (_, _) =>
        {
            SetCollapsed(false);
            HeightAsked?.Invoke(this, DesignBrowser.RowHeight * _rows.Count);
        };
        _crumbs.Children.Add(expander);
        IReadOnlyList<DesignBreadcrumb> crumbs = document.Breadcrumbs();
        for (int i = 0; i < crumbs.Count; i++)
        {
            DesignBreadcrumb crumb = crumbs[i];
            if (i > 0)
            {
                _crumbs.Children.Add(new TextBlock { Text = "›", Margin = new Thickness(3, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center });
            }

            if (crumb.Component is null && !crumb.IsApp)
            {
                _crumbs.Children.Add(new TextBlock { Text = crumb.Text, VerticalAlignment = VerticalAlignment.Center });
                continue;
            }

            var link = new Button { Content = crumb.Text, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(1, 0, 1, 0) };
            AutomationId(link, "ComponentBrowser.Crumb." + crumb.Text);
            link.Click += (_, _) => document.BrowserSelect([crumb.Component]);
            _crumbs.Children.Add(link);
        }
    }

    // --- the toolbar -------------------------------------------------------------------------------

    private void Searched()
    {
        _searchHint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _clear.Visibility = _search.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (_showing || State is not { } state || state.Search == _search.Text)
        {
            return;
        }

        // setSearchAndFilter: the tree shows whatever the search; every row open.
        state.Search = _search.Text;
        if (state.Search.Length > 0)
        {
            state.Collapsed = false;
        }

        Show();
    }

    private void OpenViewMenu()
    {
        if (State is not { } state)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = _viewButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        menu.Items.Add(ViewItem(DesignBrowser.SortByStackOrder, "ComponentBrowser.View.Stack", state.View != DesignBrowserList.TabOrder, state.ShowStackOrder));
        menu.Items.Add(ViewItem(DesignBrowser.SortByTabOrder, "ComponentBrowser.View.Tab", state.View == DesignBrowserList.TabOrder, () => state.SetView(DesignBrowserList.TabOrder)));
        menu.IsOpen = true;
    }

    private MenuItem ViewItem(string words, string id, bool on, Action act)
    {
        var item = new MenuItem { Header = words, IsCheckable = true, IsChecked = on };
        AutomationId(item, id);
        item.Click += (_, _) =>
        {
            act();
            ViewChanged();
        };
        return item;
    }

    private void ViewChanged()
    {
        _shape = string.Empty;
        Show();
    }

    // --- pressing and picking ----------------------------------------------------------------------

    private static TreeViewItem? ItemUnder(DependencyObject? from)
    {
        for (DependencyObject? node = from; node is not null; node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (node is TreeViewItem item)
            {
                return item;
            }

            if (node is System.Windows.Controls.Primitives.ToggleButton or TextBox)
            {
                return null;
            }
        }

        return null;
    }

    private IReadOnlyList<DesignBrowserRow> SelectedRows()
    {
        if (_document is not { } document)
        {
            return [];
        }

        IReadOnlyList<DesignComponent?> chosen = document.BrowserSelection;
        return _rows.Where(r => chosen.Contains(r.Component, ReferenceEqualityComparer.Instance)).ToArray();
    }

    private void OnPress(object sender, MouseButtonEventArgs e)
    {
        CommitIfOutside(e);
        if (_document is not { } document || ItemUnder(e.OriginalSource as DependencyObject) is not { Tag: DesignBrowserRow row })
        {
            return;
        }

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        bool already = SelectedRows().Any(r => ReferenceEquals(r.Component, row.Component));
        _pressed = row;
        _pressAt = e.GetPosition(_tree);
        _pressDeferred = false;
        if (already && !ctrl && !shift && !alt)
        {
            // A selected row waits for its release, so that several can be dragged (_handleMouseDown).
            _pressDeferred = true;
            e.Handled = true;
            _tree.Focus();
            return;
        }

        if (ctrl || shift || alt)
        {
            e.Handled = true;
            _tree.Focus();
            if (DesignBrowser.Clicked(_rows, SelectedRows(), ref _boundary, row, ctrl, shift, alt) is { } asked)
            {
                document.BrowserSelect([.. asked.Select(static r => r.Component)]);
            }
        }
        else
        {
            _boundary = row;
        }
    }

    private void OnRelease(object sender, MouseButtonEventArgs e)
    {
        if (_dragging is not null)
        {
            e.Handled = true;
            EndDrag(commit: true);
        }
        else if (_pressDeferred && _pressed is { } row && _document is { } document)
        {
            _boundary = row;
            document.BrowserSelect([row.Component]);
        }

        _pressDeferred = false;
        _pressed = null;
    }

    private void OnRightPress(object sender, MouseButtonEventArgs e)
    {
        // A right click on a row not selected selects it alone (_handleContextMenu).
        if (_document is { } document && ItemUnder(e.OriginalSource as DependencyObject) is { Tag: DesignBrowserRow row } item)
        {
            if (!SelectedRows().Any(r => ReferenceEquals(r.Component, row.Component)))
            {
                _boundary = row;
                document.BrowserSelect([row.Component]);
            }

            item.Focus();
        }
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemUnder(e.OriginalSource as DependencyObject) is { Tag: DesignBrowserRow { IsApp: false } } item && ReferenceEquals(item, InnermostAt(e)))
        {
            BeginEdit(item, null);
            e.Handled = true;
        }
    }

    // A double click reaches each row round the one clicked; the row it is for.
    private TreeViewItem? InnermostAt(MouseButtonEventArgs e) => ItemUnder(_tree.InputHitTest(e.GetPosition(_tree)) as DependencyObject);

    /// <summary>
    /// A row picked without Ctrl or Shift, or by the keyboard or UI Automation. A row closing takes the tree's
    /// own selection to it when what is selected is inside it, and only then says it closed; R2025b's tree
    /// keeps the selection as it is. So the pick waits for that to be over, and a row just closed is no pick.
    /// </summary>
    private void Picked()
    {
        if (_showing || State is { Collapsed: true } || _tree.SelectedItem is not TreeViewItem picked)
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (!ReferenceEquals(_tree.SelectedItem, picked) || _document is not { } document || picked.Tag is not DesignBrowserRow row || State is { Collapsed: true })
            {
                return;
            }

            if (ReferenceEquals(_justClosed, picked))
            {
                _showing = true;
                picked.IsSelected = false;
                _showing = false;
                return;
            }

            _boundary = row;
            document.BrowserSelect([row.Component]);
        });
    }

    private bool SelectionInside(TreeViewItem row)
    {
        if (_document is not { } document)
        {
            return false;
        }

        foreach (DesignComponent? component in document.BrowserSelection)
        {
            TreeViewItem? item = component is null ? _appItem : _items.GetValueOrDefault(component);
            for (ItemsControl? up = item is null ? null : ItemsControl.ItemsControlFromItemContainer(item); up is TreeViewItem parent; up = ItemsControl.ItemsControlFromItemContainer(parent))
            {
                if (ReferenceEquals(parent, row))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void OnTreeKey(object sender, KeyEventArgs e)
    {
        if (_document is not { } document)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.F2 when _tree.SelectedItem is TreeViewItem { Tag: DesignBrowserRow { IsApp: false } } item:
                BeginEdit(item, null);
                e.Handled = true;
                break;
            case Key.Delete:
                // The tree's delete action asks canDeleteComponent, then runs the app's Delete.
                if (document.Selection.Any(document.CanDelete) && !document.AppSelected)
                {
                    document.Delete(document.Selection);
                }

                e.Handled = true;
                break;
            case Key.Up or Key.Down when (Keyboard.Modifiers & ModifierKeys.Shift) != 0:
                ExtendByKey(e.Key == Key.Up ? -1 : 1);
                e.Handled = true;
                break;
        }
    }

    // Shift with an arrow: from the row last clicked to the one past the end nearest the arrow (keyDownRangeNavigation).
    private void ExtendByKey(int by)
    {
        if (_document is not { } document || _boundary is not { } boundary)
        {
            return;
        }

        IReadOnlyList<DesignBrowserRow> selected = SelectedRows();
        if (selected.Count == 0)
        {
            return;
        }

        int From(DesignBrowserRow row) => _rows.ToList().FindIndex(r => ReferenceEquals(r.Component, row.Component));
        int anchor = From(boundary);
        int low = From(selected[0]), high = From(selected[^1]);
        int end = System.Math.Clamp((low < anchor ? low : high) + by, 0, _rows.Count - 1);
        DesignBrowserRow? kept = boundary;
        if (DesignBrowser.Clicked(_rows, selected, ref kept, _rows[end], ctrl: false, shift: true) is { } asked)
        {
            document.BrowserSelect([.. asked.Select(static r => r.Component)]);
        }
    }

    // keyDownAlphaNum: a letter or a digit opens the editor with it.
    private void OnTreeText(object sender, TextCompositionEventArgs e)
    {
        if (_editing is null && e.Text is [var c] && char.IsLetterOrDigit(c) && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == 0
            && _tree.SelectedItem is TreeViewItem { Tag: DesignBrowserRow { IsApp: false } } item)
        {
            BeginEdit(item, e.Text);
            e.Handled = true;
        }
    }

    // --- renaming ----------------------------------------------------------------------------------

    /// <summary>Opens the editor on the row selected, for the menu's Rename: a labelled component's as its field's.</summary>
    public void BeginRename()
    {
        if (_document is not { } document)
        {
            return;
        }

        DesignComponent? target = document.SelectedLabeled?.Field ?? (document.Picks is [{ Component: { } one }] ? one : null);
        if (target is not null && _items.TryGetValue(target, out TreeViewItem? item))
        {
            BeginEdit(item, null);
        }
    }

    /// <summary>ComponentTreeEditor: <c>app.</c> and a box with the name, all of it selected, or the character typed.</summary>
    private void BeginEdit(TreeViewItem item, string? first)
    {
        if (item.Tag is not DesignBrowserRow { Component: { } component } row || _document is not { } document || (document.IsRegion(component) && !document.IsEditable(component, "CodeName")))
        {
            return;
        }

        CommitEdit();
        var box = new TextBox { Text = first ?? component.CodeName, MinWidth = 90, Padding = new Thickness(0), Margin = new Thickness(0) };
        AutomationId(box, "ComponentBrowser.Rename");
        System.Windows.Automation.AutomationProperties.SetName(box, "Code name");
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = "app.", VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(box);
        item.Header = panel;
        _editing = item;
        _editEnded = false;
        box.KeyDown += (_, e) =>
        {
            e.Handled = true;
            if (e.Key == Key.Enter)
            {
                EndEdit(item, row, box.Text);
            }
            else if (e.Key == Key.Escape)
            {
                EndEdit(item, row, null);
            }
            else
            {
                e.Handled = false;
            }
        };
        box.LostKeyboardFocus += (_, _) => EndEdit(item, row, box.Text);
        box.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            box.Focus();
            if (first is null)
            {
                box.SelectAll();
            }
            else
            {
                box.CaretIndex = box.Text.Length;
            }
        });
    }

    // Whatever closes the editor: a name to set, or null for none. An empty name sets nothing (_validateAndCompleteEdit).
    private void EndEdit(TreeViewItem item, DesignBrowserRow row, string? text)
    {
        if (_editEnded || !ReferenceEquals(_editing, item))
        {
            return;
        }

        _editEnded = true;
        _editing = null;
        item.Header = Header(row.Label, State?.Search ?? string.Empty);
        _tree.Focus();
        string? name = text?.Trim();
        if (!string.IsNullOrEmpty(name) && row.Component is { } component && name != component.CodeName)
        {
            RenameEntered?.Invoke(this, (component, name));
        }

        Show();
    }

    private void CommitEdit()
    {
        if (_editing is { Tag: DesignBrowserRow row, Header: StackPanel { Children: [_, TextBox box] } } item)
        {
            EndEdit(item, row, box.Text);
        }
    }

    private void CommitIfOutside(MouseButtonEventArgs e)
    {
        if (_editing is not null && e.OriginalSource is not TextBox)
        {
            CommitEdit();
        }
    }

    // --- the menu ----------------------------------------------------------------------------------

    private bool Fill(ContextMenu menu)
    {
        menu.Items.Clear();
        if (MenuProvider is not { } provider || State is not { } state)
        {
            return false;
        }

        foreach (DesignMenuItem item in provider(state, ViewChanged, BeginRename))
        {
            menu.Items.Add(Entry(item));
        }

        return menu.Items.Count > 0;
    }

    private static object Entry(DesignMenuItem item)
    {
        if (item.IsSeparator)
        {
            return new Separator();
        }

        var entry = new MenuItem
        {
            Header = item.Label, IsEnabled = item.Enabled, IsCheckable = item.Checked is not null, IsChecked = item.Checked == true,
            InputGestureText = item.Shortcut ?? string.Empty,
        };
        AutomationId(entry, "Menu." + item.Label.Replace(" ", string.Empty, StringComparison.Ordinal));
        if (item.Items is { } children)
        {
            foreach (DesignMenuItem child in children)
            {
                entry.Items.Add(Entry(child));
            }
        }
        else if (item.Run is { } run)
        {
            entry.Click += (_, _) => run();
        }

        return entry;
    }

    // --- dragging ----------------------------------------------------------------------------------

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Point now = e.GetPosition(_tree);
        if (_dragging is null)
        {
            bool far = System.Math.Abs(now.Y - _pressAt.Y) >= SystemParameters.MinimumVerticalDragDistance
                || System.Math.Abs(now.X - _pressAt.X) >= SystemParameters.MinimumHorizontalDragDistance;
            if (_pressed is null || !far || _editing is not null || _document is not { } document)
            {
                return;
            }

            // _handleDragStart: a selected row, and only what _validateDrag lets go.
            DesignComponent[] sources = [.. SelectedRows().Where(static r => r.Component is not null).Select(static r => r.Component!)];
            if (!SelectedRows().Any(r => ReferenceEquals(r.Component, _pressed.Component)) || !DesignBrowser.CanDrag(sources))
            {
                _pressed = null;
                return;
            }

            _dragging = sources;
            _pressDeferred = false;
            _tree.CaptureMouse();
        }

        DraggedTo(now);
        e.Handled = true;
    }

    private void DraggedTo(Point at)
    {
        ClearHover();
        _dropHere = null;
        _dropLine.Visibility = Visibility.Collapsed;
        if (_document is not { } document || _dragging is not { } sources)
        {
            return;
        }

        TreeViewItem? item = ItemUnder(_tree.InputHitTest(at) as DependencyObject);
        if (item is not { Tag: DesignBrowserRow row })
        {
            _tree.Cursor = Cursors.No;
            return;
        }

        Point top = item.TranslatePoint(new Point(0, 0), _tree);
        double within = at.Y - top.Y;
        bool above = within < DropEdge;
        bool below = within >= DesignBrowser.RowHeight - DropEdge;
        IReadOnlyList<DesignComponent> openRows = OpenChildren(item);
        (DesignComponent Target, DesignBrowserPlace Place, DesignComponent? At)? drop = null;
        if (above && row.Component is { } self && row.Parent is { } parent)
        {
            drop = (parent, DesignBrowserPlace.Before, self);
        }
        else if (below && row.Component is { } it)
        {
            // Under an open row's last pixels: its first child's place (handleReorderNode).
            drop = openRows.Count > 0 ? (it, DesignBrowserPlace.Before, openRows[0])
                : row.Parent is { } up ? (up, DesignBrowserPlace.After, it) : null;
        }
        else if (!above && !below && row.Component is { } on)
        {
            drop = (on, DesignBrowserPlace.First, null);
        }

        if (drop is { } d && document.BrowserCanDrop(sources, d.Target))
        {
            _dropHere = d;
            _tree.Cursor = Cursors.Arrow;
            if (d.Place == DesignBrowserPlace.First)
            {
                _hovered = item;
                item.SetResourceReference(BackgroundProperty, ThemeKeys.Hover);
                _autoExpand.Start();
            }
            else
            {
                Point inBody = item.TranslatePoint(new Point(0, above ? 0 : DesignBrowser.RowHeight), _overlay);
                Canvas.SetLeft(_dropLine, inBody.X + 18);
                Canvas.SetTop(_dropLine, inBody.Y - 1);
                _dropLine.Width = System.Math.Max(10, _overlay.ActualWidth - inBody.X - 22);
                _dropLine.Visibility = Visibility.Visible;
            }
        }
        else
        {
            _tree.Cursor = Cursors.No;
        }
    }

    // The rows open under an item, top to bottom.
    private static IReadOnlyList<DesignComponent> OpenChildren(TreeViewItem item) =>
        item.IsExpanded ? item.Items.OfType<TreeViewItem>().Select(static i => i.Tag).OfType<DesignBrowserRow>().Select(static r => r.Component!).ToArray() : [];

    private void ClearHover()
    {
        _autoExpand.Stop();
        _hovered?.ClearValue(BackgroundProperty);
        _hovered = null;
    }

    private void EndDrag(bool commit)
    {
        if (_dragging is not { } sources)
        {
            return;
        }

        _dragging = null;
        ClearHover();
        _dropLine.Visibility = Visibility.Collapsed;
        _tree.ClearValue(CursorProperty);
        (DesignComponent Target, DesignBrowserPlace Place, DesignComponent? At)? drop = _dropHere;
        _dropHere = null;
        if (_tree.IsMouseCaptured)
        {
            _tree.ReleaseMouseCapture();
        }

        if (commit && drop is { } d && _document is { } document && State is { } state)
        {
            document.BrowserDrop(state.View, sources, d.Target, d.Place, d.At);
        }
    }

    private static void AutomationId(FrameworkElement element, string id) =>
        System.Windows.Automation.AutomationProperties.SetAutomationId(element, id);
}
