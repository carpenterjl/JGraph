using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using JGraph.Controls.Themes;
using JGraph.Scripting.Toolstrip;

namespace JGraph.Controls.Toolstrip;

// Building the controls of a layout, and the popups they open.
public sealed partial class Toolstrip
{
    private void Rebuild()
    {
        HideKeyTips();
        CloseTransients();
        _tabs.Items.Clear();
        _tabStates.Clear();
        _byTag.Clear();
        _byAction.Clear();
        _commandSources.Clear();
        _openers.Clear();
        _toggles.Clear();
        _columnOf.Clear();
        _quickAccess.Children.Clear();
        _host.Content = null;

        foreach (ToolstripTab tab in _layout.Tabs)
        {
            if (_tabStates.ContainsKey(tab.Tag))
            {
                continue;
            }

            TabState state = BuildTab(tab);
            _tabStates[tab.Tag] = state;
            _tabs.Items.Add(state.Header);
            state.Header.Visibility = IsShown(tab) ? Visibility.Visible : Visibility.Collapsed;
        }

        int index = 0;
        foreach (ToolstripItem item in _layout.QuickAccess)
        {
            var button = new ToolstripPushButton
            {
                Content = new ToolstripFace(item.Icon, "", vertical: false, arrow: false),
                Width = 22,
                Height = 20,
                Padding = new Thickness(1),
            };
            Describe(button, item, ToolstripRules.Label(Words(item.Text), vertical: false));
            BindButton(button, item, () => null);
            ToolstripKeyTipService.SetTip(button, ToolstripRules.QuickAccessKeyTip(index++) ?? "");
            _quickAccess.Children.Add(button);
        }

        // Key tips: the tabs' from the layout or their titles; each tab's controls' likewise.
        IReadOnlyList<string> tabTips = ToolstripKeyTips.Assign([.. _tabStates.Values.Select(s => (s.Spec.Mnemonic, (string?)s.Spec.Title))]);
        int t = 0;
        foreach (TabState state in _tabStates.Values)
        {
            state.Tip = tabTips[t++];
            ToolstripKeyTipService.SetTip(state.Header, state.Tip);
            IReadOnlyList<string> tips = ToolstripKeyTips.Assign([.. state.Tipped.Select(e => (e.Mnemonic, (string?)e.Label))]);
            for (int i = 0; i < tips.Count; i++)
            {
                ToolstripKeyTipService.SetTip(state.Tipped[i].Element, tips[i]);
            }
        }

        _tabs.SelectedItem = _tabs.Items.OfType<TabItem>().FirstOrDefault(i => i.Visibility == Visibility.Visible);
        OnTabSelected();
    }

    private TabState BuildTab(ToolstripTab tab)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        KeyboardNavigation.SetDirectionalNavigation(panel, KeyboardNavigationMode.Continue);
        var sections = new List<ToolstripSectionView>();
        var state = new TabState(tab, ToolstripTabStrip.Header(tab.Title, tab.Tag), panel, sections);
        foreach (ToolstripSection section in tab.Sections)
        {
            var columns = new List<ToolstripColumnView>();
            foreach (ToolstripColumn column in section.Columns)
            {
                columns.Add(BuildColumn(column, (c, i) => ToolstripRules.IsVertical(c, i), state, ContentHeight));
            }

            var view = new ToolstripSectionView(section, columns, ContentHeight);
            state.Tipped.Add((null, section.Title ?? "", view.DropDown));
            view.DropDown.Checked += (_, _) => HideKeyTips();
            sections.Add(view);
            panel.Children.Add(view);
        }

        return state;
    }

    private ToolstripColumnView BuildColumn(ToolstripColumn column, Func<ToolstripColumn, ToolstripItem, bool> vertical, TabState tab, double height)
    {
        var view = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(1, 0, 1, 0) };
        if (column.Width > 0)
        {
            view.Width = column.Width;
        }

        HorizontalAlignment align = column.HorizontalAlignment switch
        {
            "center" => HorizontalAlignment.Center,
            "right" => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Stretch,
        };
        var labelled = new List<IToolstripLabelled>();
        var state = new ColumnState(column, vertical);
        foreach (ToolstripItem item in column.Items)
        {
            FrameworkElement element = BuildItem(item, column, vertical(column, item), tab, height, labelled);
            if (element.HorizontalAlignment == HorizontalAlignment.Stretch)
            {
                element.HorizontalAlignment = align;
            }

            state.Members.Add((item, element));
            _columnOf[element] = state;
            view.Children.Add(element);
        }

        return new ToolstripColumnView(view, state.CanHideLabels, labelled);
    }

    private FrameworkElement BuildItem(ToolstripItem item, ToolstripColumn column, bool vertical, TabState tab, double height, List<IToolstripLabelled> labelled)
    {
        string oneLine = ToolstripRules.Label(Words(item.Text), vertical: false);
        double inputWidth = column.Width > 0 ? column.Width - 2 : 0;
        switch (item.Kind)
        {
            case ToolstripItemKind.PushButton:
            {
                var button = new ToolstripPushButton();
                button.FaceState = new ToolstripFaceState(button, item.Icon, Words(item.Text), arrow: false, height);
                button.FaceState.SetVertical(vertical);
                Describe(button, item, oneLine);
                BindButton(button, item, () => null);
                Tip(tab, item, oneLine, button);
                labelled.Add(button);
                return button;
            }

            case ToolstripItemKind.ToggleButton:
            {
                var toggle = new ToolstripToggleButton { Group = item.ButtonGroup, IsChecked = item.Selected };
                toggle.FaceState = new ToolstripFaceState(toggle, item.Icon, Words(item.Text), arrow: false, height);
                toggle.FaceState.SetVertical(vertical);
                Describe(toggle, item, oneLine);
                if (item.Selected)
                {
                    _selected[Id(item)] = true;
                }

                BindButton(toggle, item, () => toggle.IsChecked == true);
                _toggles.Add(toggle);
                toggle.Checked += (_, _) => Toggled(toggle, item, true);
                toggle.Unchecked += (_, _) => Toggled(toggle, item, false);
                Tip(tab, item, oneLine, toggle);
                labelled.Add(toggle);
                return toggle;
            }

            case ToolstripItemKind.DropDownButton:
            {
                var drop = new ToolstripPopupButton();
                drop.FaceState = new ToolstripFaceState(drop, item.Icon, Words(item.Text), arrow: true, height);
                drop.FaceState.SetVertical(vertical);
                Describe(drop, item, oneLine);
                drop.OpenPopup = b => OpenMenu(b, item);
                _openers.Add(drop);
                Tip(tab, item, oneLine, drop);
                labelled.Add(drop);
                return drop;
            }

            case ToolstripItemKind.SplitButton:
            {
                var split = new ToolstripSplitButton(item.Icon, Words(item.Text), vertical, height);
                Describe(split.Main, item, oneLine);
                AutomationProperties.SetName(split.Arrow, oneLine);
                AutomationProperties.SetAutomationId(split.Arrow, Id(item) + ".arrow");
                split.Arrow.ToolTip = split.Main.ToolTip;
                BindButton(split.Main, item, () => null);
                split.Arrow.OpenPopup = b => OpenMenu(b, item);
                _openers.Add(split.Arrow);
                Register(split.Arrow, item with { Tag = null });
                Tip(tab, item, oneLine, split.Main);
                labelled.Add(split);
                return split;
            }

            case ToolstripItemKind.ComboBox:
            {
                var combo = new ComboBox
                {
                    IsEditable = item.Editable,
                    Height = RowHeight,
                    Width = inputWidth > 0 ? inputWidth : 100,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 1),
                };
                foreach (string choice in item.Items)
                {
                    combo.Items.Add(choice);
                }

                if (item.Value is { } chosen)
                {
                    combo.SelectedItem = item.Items.FirstOrDefault(i => i == chosen);
                    if (item.Editable)
                    {
                        combo.Text = chosen;
                    }
                }

                Describe(combo, item, oneLine);
                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.SelectedItem is string value && !combo.IsKeyboardFocusWithin)
                    {
                        Raise(item.Tag, item.ActionId, value);
                    }
                };
                combo.DropDownClosed += (_, _) =>
                {
                    if (combo.SelectedItem is string value)
                    {
                        Raise(item.Tag, item.ActionId, value);
                    }
                };
                combo.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter)
                    {
                        Raise(item.Tag, item.ActionId, combo.Text);
                    }
                };
                Tip(tab, item, oneLine, combo);
                return combo;
            }

            case ToolstripItemKind.Spinner:
            {
                var spinner = new ToolstripSpinner(item.ErrorText, item.Minimum, item.Maximum, item.Step, item.Editable)
                {
                    Height = RowHeight,
                    Width = inputWidth > 0 ? inputWidth : 50,
                    Margin = new Thickness(0, 0, 0, 1),
                };
                spinner.Text = item.Value ?? "";
                Describe(spinner.Box, item, oneLine);
                spinner.Committed += v => Raise(item.Tag, item.ActionId, v);
                Tip(tab, item, oneLine, spinner.Box);
                return spinner;
            }

            case ToolstripItemKind.TextField:
            {
                var box = new TextBox
                {
                    Height = RowHeight,
                    Width = inputWidth > 0 ? inputWidth : 100,
                    Text = item.Value ?? "",
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 1),
                };
                Describe(box, item, oneLine);
                string committed = box.Text;
                void Commit()
                {
                    if (box.Text != committed)
                    {
                        committed = box.Text;
                        Raise(item.Tag, item.ActionId, box.Text);
                    }
                }

                box.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter)
                    {
                        Commit();
                    }
                };
                box.LostKeyboardFocus += (_, _) => Commit();
                Tip(tab, item, oneLine, box);
                return box;
            }

            case ToolstripItemKind.Label:
            {
                var text = new TextBlock
                {
                    Text = oneLine,
                    Height = RowHeight,
                    Padding = new Thickness(2, 3, 2, 0),
                    ToolTip = string.IsNullOrEmpty(item.Description) ? null : Words(item.Description),
                };
                Register(text, item);
                return text;
            }

            case ToolstripItemKind.Panel:
            {
                var rows = new StackPanel { Orientation = Orientation.Vertical };
                foreach (IReadOnlyList<ToolstripPanelChild> row in ToolstripRules.PanelRows(item))
                {
                    var line = new StackPanel { Orientation = Orientation.Horizontal };
                    foreach (ToolstripPanelChild child in row)
                    {
                        if (child.Column is { } inner)
                        {
                            ToolstripColumnView view = BuildColumn(inner, (c, i) => ToolstripRules.IsVerticalInPanel(column, c, i), tab, height);
                            view.View.Margin = new Thickness(0);
                            labelled.AddRange(view.Labelled);
                            line.Children.Add(view.View);
                        }
                        else if (child.Item is { } control)
                        {
                            var single = new ToolstripColumn(null, 0, null, [control]);
                            line.Children.Add(BuildItem(control, single, vertical: false, tab, height, labelled));
                        }
                    }

                    rows.Children.Add(line);
                }

                Register(rows, item);
                return rows;
            }

            default:
            {
                var gap = new Border { Height = RowHeight };
                Register(gap, item);
                return gap;
            }
        }
    }

    // The words a host turns ${0} into, or the layout's own.
    private string Words(string? text) => text is null ? "" : TextFormatter?.Invoke(text) ?? text;

    private static string Id(ToolstripItem item) => item.Tag ?? item.ActionId ?? "";

    private void Describe(FrameworkElement element, ToolstripItem item, string oneLine)
    {
        string tip = ToolstripRules.ToolTip(oneLine, Words(item.Description), item.Shortcut);
        if (tip.Length > 0)
        {
            element.ToolTip = tip;
        }

        AutomationProperties.SetName(element, oneLine.Length > 0 ? oneLine : Words(item.Description));
        AutomationProperties.SetAutomationId(element, Id(item));
        if (item.Description is { Length: > 0 } description)
        {
            AutomationProperties.SetHelpText(element, Words(description));
        }

        if (item.Enabled == false)
        {
            element.IsEnabled = false;
        }

        Register(element, item);
    }

    private void Register(FrameworkElement element, ToolstripItem item)
    {
        if (item.Tag is { } tag)
        {
            Add(_byTag, tag, element);
        }

        if (item.ActionId is { } action)
        {
            Add(_byAction, action, element);
        }

        static void Add(Dictionary<string, List<FrameworkElement>> map, string key, FrameworkElement element)
        {
            if (!map.TryGetValue(key, out List<FrameworkElement>? list))
            {
                map[key] = list = [];
            }

            list.Add(element);
        }
    }

    // Binds a button to its action's command, sent to the command target, and raises Invoked after it.
    private void BindButton(ButtonBase button, ToolstripItem item, Func<object?> value)
    {
        if (item.ActionId is { } action && CommandResolver?.Invoke(action) is { } command)
        {
            button.Command = command;
            button.CommandTarget = _commandTarget;
            _commandSources.Add(button);
        }

        if (button is not ToggleButton)
        {
            button.Click += (_, _) =>
            {
                HideKeyTips();
                CloseTransients();
                RestoreFocus();
                Raise(item.Tag, item.ActionId, value());
            };
        }
    }

    private void Toggled(ToolstripToggleButton toggle, ToolstripItem item, bool on)
    {
        string id = Id(item);
        if (!on && toggle.Group is { } group && !_quiet && !_toggles.Any(t => t.Group == group && t.IsChecked == true))
        {
            // A pressed button of a group stays pressed when clicked again: one of the group is always chosen.
            _quiet = true;
            toggle.IsChecked = true;
            _quiet = false;
            return;
        }

        _selected[id] = on;
        if (item.ActionId is { } action && item.Tag is not null)
        {
            _selected[action] = on;
        }

        if (on && toggle.Group is { } g)
        {
            bool quiet = _quiet;
            _quiet = true;
            foreach (ToolstripToggleButton other in _toggles.Where(o => o != toggle && o.Group == g && o.IsChecked == true))
            {
                other.IsChecked = false;
            }

            _quiet = quiet;
        }

        if (!_quiet)
        {
            HideKeyTips();
            CloseTransients();
        }

        Raise(item.Tag, item.ActionId, on);
    }

    private void Tip(TabState tab, ToolstripItem item, string label, FrameworkElement element) =>
        tab.Tipped.Add((item.Mnemonic, label, element));

    // ---- popups ----------------------------------------------------------------------------

    private bool OpenMenu(ToolstripPopupButton button, ToolstripItem item)
    {
        if (_layout.Popup(item.PopupTag) is not { } popup)
        {
            return false;
        }

        HideKeyTips();
        var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
        AutomationProperties.SetAutomationId(menu, popup.Tag);
        AddEntries(menu, popup.Entries);
        if (menu.Items.Count == 0)
        {
            return false;
        }

        menu.Closed += (_, _) => button.PopupClosed();
        button.ClosePopup = () => menu.IsOpen = false;
        menu.IsOpen = true;
        return true;
    }

    private void AddEntries(ItemsControl menu, IEnumerable<ToolstripPopupEntry> entries)
    {
        foreach (ToolstripPopupEntry entry in entries)
        {
            switch (entry.Kind)
            {
                case ToolstripPopupEntryKind.Separator:
                    menu.Items.Add(new Separator());
                    break;

                case ToolstripPopupEntryKind.Header:
                {
                    var title = new TextBlock { Text = Words(entry.Text), FontWeight = FontWeights.SemiBold };
                    title.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextSecondary);
                    var header = new MenuItem { Header = title, Focusable = false, IsHitTestVisible = false };
                    AutomationProperties.SetAutomationId(header, entry.Tag ?? "");
                    menu.Items.Add(header);
                    break;
                }

                case ToolstripPopupEntryKind.Panel:
                    if (entry.Tag is { } panel && PopupPanelProvider?.Invoke(panel) is { } dynamic)
                    {
                        AddEntries(menu, dynamic);
                    }

                    break;

                default:
                    menu.Items.Add(MenuEntry(entry));
                    break;
            }
        }
    }

    private MenuItem MenuEntry(ToolstripPopupEntry entry)
    {
        string text = ToolstripRules.Label(Words(entry.Text), vertical: false);
        object header = new TextBlock { Text = text };
        if (entry.ShowDescription && entry.Description is { Length: > 0 } description)
        {
            var two = new StackPanel();
            two.Children.Add(new TextBlock { Text = text });
            var second = new TextBlock { Text = Words(description), FontSize = 10, MaxWidth = 280, TextWrapping = TextWrapping.Wrap };
            second.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextSecondary);
            two.Children.Add(second);
            header = two;
        }

        var menuItem = new MenuItem
        {
            Header = header,
            InputGestureText = entry.Shortcut ?? "",
            Icon = ToolstripIcons.Create(entry.Icon, 16),
        };
        if (!entry.ShowDescription && entry.Description is { Length: > 0 } tip && tip != text)
        {
            menuItem.ToolTip = Words(tip);
        }

        string id = entry.Tag ?? entry.ActionId ?? text;
        AutomationProperties.SetAutomationId(menuItem, id);
        AutomationProperties.SetName(menuItem, text);
        if (entry.ActionId is { } action && CommandResolver?.Invoke(action) is { } command)
        {
            menuItem.Command = command;
            menuItem.CommandTarget = _commandTarget;
        }

        if (entry.Kind == ToolstripPopupEntryKind.CheckItem)
        {
            menuItem.IsCheckable = true;
            menuItem.IsChecked = _selected.TryGetValue(id, out bool on) ? on : entry.Selected;
            menuItem.StaysOpenOnClick = !entry.ClosePopupOnClick;
        }

        menuItem.Click += (_, _) =>
        {
            object? value = entry.Kind == ToolstripPopupEntryKind.CheckItem ? menuItem.IsChecked : entry.ActionId is null ? text : null;
            if (entry.Kind == ToolstripPopupEntryKind.CheckItem)
            {
                _selected[id] = menuItem.IsChecked;
                if (entry.ActionId is { } a)
                {
                    _selected[a] = menuItem.IsChecked;
                }
            }

            if (entry.Kind != ToolstripPopupEntryKind.CheckItem || entry.ClosePopupOnClick)
            {
                CloseTransients();
            }

            Raise(entry.Tag, entry.ActionId, value);
        };
        return menuItem;
    }
}

/// <summary>
/// A column as built: its controls and the elements made for them. R2025b takes a control out of
/// its column to hide it (Pause and Continue while Run shows), so the column lays itself out again
/// over the controls it shows: Run alone in its column is a large button.
/// </summary>
internal sealed class ColumnState(ToolstripColumn spec, Func<ToolstripColumn, ToolstripItem, bool> vertical)
{
    public List<(ToolstripItem Item, FrameworkElement Element)> Members { get; } = [];

    /// <summary>The column over the controls it shows.</summary>
    public ToolstripColumn Shown => spec with { Items = [.. Members.Where(m => m.Element.Visibility != Visibility.Collapsed).Select(m => m.Item)] };

    public bool CanHideLabels() => ToolstripRules.CanHideLabels(Shown);

    public void Relayout()
    {
        ToolstripColumn shown = Shown;
        foreach ((ToolstripItem item, FrameworkElement element) in Members)
        {
            if (element is IToolstripOrientable orientable)
            {
                orientable.SetVertical(vertical(shown, item));
            }
        }
    }
}

/// <summary>The key tip each toolstrip element carries.</summary>
internal static class ToolstripKeyTipService
{
    public static readonly DependencyProperty TipProperty = DependencyProperty.RegisterAttached(
        "Tip", typeof(string), typeof(ToolstripKeyTipService), new PropertyMetadata(""));

    public static string GetTip(DependencyObject element) => (string)element.GetValue(TipProperty);

    public static void SetTip(DependencyObject element, string tip)
    {
        element.SetValue(TipProperty, tip);
        AutomationProperties.SetAccessKey(element, tip);
    }
}
