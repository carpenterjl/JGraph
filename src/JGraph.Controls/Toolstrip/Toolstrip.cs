using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using JGraph.Controls.Themes;
using JGraph.Scripting.Toolstrip;

namespace JGraph.Controls.Toolstrip;

/// <summary>What a toolstrip control did: the control, its action, and its new value for a toggle or an input.</summary>
/// <param name="Tag">The control's tag, or a dynamic popup item's.</param>
/// <param name="ActionId">The control's action, or null.</param>
/// <param name="Value">A toggle's state (bool), an input's committed value (string or double), a dynamic item's text; null for a button.</param>
public sealed record ToolstripInvokedEventArgs(string? Tag, string? ActionId, object? Value);

/// <summary>
/// A toolstrip laid out as R2025b's toolstrip framework lays one out (App Designer window plan,
/// decision WD2, stage W1): tabs of titled sections of columns, large and small buttons, split and
/// drop-down buttons with their popups, toggles and button groups, combo boxes, spinners and text
/// fields; contextual tabs the host shows by tab group; the quick access bar; sections that drop
/// their labels and collapse to drop-downs as the window narrows (<see cref="ToolstripResizer"/>);
/// the whole toolstrip minimized to its tabs; Alt key tips (<see cref="ToolstripKeyTips"/>).
/// </summary>
/// <remarks>
/// The control renders a <see cref="ToolstripLayout"/> and knows nothing of App Designer. Each
/// control with an action id is bound to the command <see cref="CommandResolver"/> gives for it,
/// sent to <see cref="CommandTarget"/> (a host sets its window: routed from the focused element,
/// a text box's own Undo would answer instead); every control also raises <see cref="Invoked"/>.
/// </remarks>
public sealed partial class Toolstrip : Border
{
    /// <summary>The height of a tab's controls: three small controls, or one large one.</summary>
    public const double ContentHeight = 70;

    /// <summary>The height of a small control.</summary>
    public const double RowHeight = 22;

    private readonly ToolstripTabStrip _tabs = new();
    private readonly ToolstripGroup _quickAccess = new(AutomationControlType.ToolBar) { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly ToolstripPushButton _minimize = new();
    private readonly ContentControl _host = new() { Focusable = false, ClipToBounds = true, HorizontalContentAlignment = HorizontalAlignment.Left };
    private readonly Border _hostFrame = new() { BorderThickness = new Thickness(0, 1, 0, 1) };
    private readonly Popup _overlay = new() { StaysOpen = false, Placement = PlacementMode.Bottom, AllowsTransparency = false };
    private readonly Border _overlayFrame = new() { BorderThickness = new Thickness(1) };
    private readonly AdornerDecorator _overlayDecorator = new();
    private readonly DockPanel _header = new() { LastChildFill = true };
    private readonly Dictionary<string, TabState> _tabStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<FrameworkElement>> _byTag = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<FrameworkElement>> _byAction = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _selected = new(StringComparer.Ordinal);
    private readonly List<ButtonBase> _commandSources = [];
    private readonly List<ToolstripPopupButton> _openers = [];
    private readonly List<ToolstripToggleButton> _toggles = [];
    private readonly Dictionary<FrameworkElement, ColumnState> _columnOf = [];
    private HashSet<string> _context = new(StringComparer.Ordinal);
    private readonly HashSet<string> _hiddenTabs = new(StringComparer.Ordinal);
    private ToolstripLayout _layout = ToolstripLayout.Empty;
    private IInputElement? _commandTarget;
    private bool _minimized;
    private bool _quiet;
    private bool _fitQueued;

    public Toolstrip()
    {
        SetResourceReference(BackgroundProperty, ThemeKeys.Panel);
        SetResourceReference(TextElement.ForegroundProperty, ThemeKeys.Text);
        SetResourceReference(TextElement.FontFamilyProperty, ThemeKeys.FontUI);
        TextElement.SetFontSize(this, 11);
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        // A focus scope, as WPF's ToolBar is: clicking a control leaves the document's logical focus
        // where it was, and a command without a target would go to that focused element.
        FocusManager.SetIsFocusScope(this, true);

        _minimize.Width = 22;
        _minimize.Height = 20;
        _minimize.Margin = new Thickness(2, 0, 2, 0);
        _minimize.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAutomationId(_minimize, "Toolstrip.MinimizeButton");
        _minimize.Click += (_, _) => IsMinimized = !IsMinimized;
        AutomationProperties.SetAutomationId(_quickAccess, "Toolstrip.QuickAccessBar");
        AutomationProperties.SetName(_quickAccess, "Quick Access Toolbar");
        AutomationProperties.SetAutomationId(_tabs, "Toolstrip.Tabs");
        AutomationProperties.SetName(_tabs, "Toolstrip");
        _tabs.SelectionChanged += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, _tabs))
            {
                OnTabSelected();
            }
        };
        _tabs.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (_minimized && FindAncestor<TabItem>(e.OriginalSource as DependencyObject) is not null)
            {
                Dispatcher.BeginInvoke(ShowOverlay, DispatcherPriority.Input);
            }
        };
        _tabs.MouseDoubleClick += (_, e) =>
        {
            if (FindAncestor<TabItem>(e.OriginalSource as DependencyObject) is not null)
            {
                IsMinimized = !IsMinimized;
                e.Handled = true;
            }
        };

        DockPanel.SetDock(_minimize, Dock.Right);
        DockPanel.SetDock(_quickAccess, Dock.Right);
        _header.Children.Add(_minimize);
        _header.Children.Add(_quickAccess);
        _header.Children.Add(_tabs);

        _hostFrame.SetResourceReference(BackgroundProperty, ThemeKeys.ToolBar);
        _hostFrame.SetResourceReference(BorderBrushProperty, ThemeKeys.Border);
        _hostFrame.Child = _host;
        _host.SizeChanged += (_, e) =>
        {
            if (e.WidthChanged)
            {
                QueueFit();
            }
        };
        _overlayFrame.SetResourceReference(BackgroundProperty, ThemeKeys.ToolBar);
        _overlayFrame.SetResourceReference(BorderBrushProperty, ThemeKeys.BorderStrong);
        _overlayFrame.Child = _overlayDecorator;
        _overlay.Child = _overlayFrame;
        _overlay.PlacementTarget = _header;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_hostFrame, 1);
        root.Children.Add(_header);
        root.Children.Add(_hostFrame);
        Child = new AdornerDecorator { Child = root };
        UpdateMinimizeButton();
        Loaded += (_, _) => AttachKeyTips();
        Unloaded += (_, _) => DetachKeyTips();
    }

    /// <summary>Raised when a control runs, toggles or commits a value, after its command if it has one.</summary>
    public event EventHandler<ToolstripInvokedEventArgs>? Invoked;

    /// <summary>Raised when another tab is selected.</summary>
    public event EventHandler? SelectedTabChanged;

    /// <summary>The layout shown; setting it builds the controls anew.</summary>
    public ToolstripLayout Layout
    {
        get => _layout;
        set
        {
            _layout = value;
            Rebuild();
        }
    }

    /// <summary>Gives the command a control's action id runs, or null for none. Read when the layout is built.</summary>
    public Func<string, ICommand?>? CommandResolver { get; set; }

    /// <summary>Where the controls' commands are sent: a host sets its window.</summary>
    public IInputElement? CommandTarget
    {
        get => _commandTarget;
        set
        {
            _commandTarget = value;
            foreach (ButtonBase source in _commandSources)
            {
                source.CommandTarget = value;
            }
        }
    }

    /// <summary>Gives the items of a popup's dynamic panel by the panel's tag, each time the popup opens.</summary>
    public Func<string, IReadOnlyList<ToolstripPopupEntry>?>? PopupPanelProvider { get; set; }

    /// <summary>Turns a label or tooltip into the words shown (App Designer fills "${0}" with the document's kind). Read when the layout is built.</summary>
    public Func<string, string>? TextFormatter { get; set; }

    /// <summary>The selected tab's tag.</summary>
    public string? SelectedTab
    {
        get => (_tabs.SelectedItem as TabItem)?.Tag as string;
        set
        {
            if (value is not null && _tabStates.TryGetValue(value, out TabState? state) && state.Header.Visibility == Visibility.Visible)
            {
                _tabs.SelectedItem = state.Header;
            }
        }
    }

    /// <summary>The tags of the tabs shown, left to right.</summary>
    public IReadOnlyList<string> VisibleTabs =>
        [.. _tabs.Items.OfType<TabItem>().Where(t => t.Visibility == Visibility.Visible).Select(t => (string)t.Tag)];

    /// <summary>Whether the toolstrip shows only its tabs; a tab's controls then open over the document when its tab is clicked.</summary>
    public bool IsMinimized
    {
        get => _minimized;
        set
        {
            if (_minimized == value)
            {
                return;
            }

            _minimized = value;
            _overlay.IsOpen = false;
            if (value)
            {
                _hostFrame.Child = null;
                _overlayDecorator.Child = _host;
                _hostFrame.Visibility = Visibility.Collapsed;
            }
            else
            {
                _overlayDecorator.Child = null;
                _hostFrame.Child = _host;
                _hostFrame.Visibility = Visibility.Visible;
            }

            UpdateMinimizeButton();
            QueueFit();
        }
    }

    /// <summary>Whether a minimized toolstrip's tab is open over the document.</summary>
    public bool IsOverlayOpen => _overlay.IsOpen;

    /// <summary>
    /// Shows the contextual tabs of these tab groups (or tab tags) and hides the others. When the
    /// selected tab goes, the first contextual tab newly shown is selected, else the first tab.
    /// </summary>
    public void SetContext(IEnumerable<string> tabGroupTags)
    {
        HashSet<string> before = [.. VisibleTabs];
        _context = new HashSet<string>(tabGroupTags, StringComparer.Ordinal);
        foreach (TabState state in _tabStates.Values)
        {
            state.Header.Visibility = IsShown(state.Spec) ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_tabs.SelectedItem is TabItem { Visibility: Visibility.Visible })
        {
            return;
        }

        TabItem? next = _tabs.Items.OfType<TabItem>().FirstOrDefault(t => t.Visibility == Visibility.Visible && !before.Contains((string)t.Tag) && _tabStates[(string)t.Tag].Spec.IsContextual)
            ?? _tabs.Items.OfType<TabItem>().FirstOrDefault(t => t.Visibility == Visibility.Visible);
        _tabs.SelectedItem = next;
    }

    /// <summary>
    /// Hides or shows a tab whatever the context (App Designer shows SIMULINK only for an app bound
    /// to a model); a hidden selected tab gives way to the first tab shown.
    /// </summary>
    public void SetTabVisible(string tag, bool visible)
    {
        if (visible ? !_hiddenTabs.Remove(tag) : !_hiddenTabs.Add(tag))
        {
            return;
        }

        SetContext([.. _context]);
    }

    /// <summary>Enables or disables every control and popup item with this tag or action id (a control bound to a command also follows the command).</summary>
    public void SetEnabled(string id, bool enabled)
    {
        foreach (FrameworkElement element in Elements(id))
        {
            element.IsEnabled = enabled;
        }
    }

    /// <summary>Shows or hides the controls with this tag (App Designer swaps Run, Pause and Continue).</summary>
    public void SetVisible(string tag, bool visible)
    {
        if (_byTag.TryGetValue(tag, out List<FrameworkElement>? elements))
        {
            var columns = new HashSet<ColumnState>();
            foreach (FrameworkElement element in elements)
            {
                FrameworkElement outer = Outer(element);
                outer.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                if (_columnOf.TryGetValue(outer, out ColumnState? column))
                {
                    columns.Add(column);
                }
            }

            foreach (ColumnState column in columns)
            {
                column.Relayout();
            }

            QueueFit();
        }
    }

    /// <summary>Presses or releases the toggles and check items with this tag or action id, without raising <see cref="Invoked"/>.</summary>
    public void SetSelected(string id, bool selected)
    {
        _selected[id] = selected;
        _quiet = true;
        try
        {
            foreach (ToggleButton toggle in Elements(id).OfType<ToggleButton>().Where(t => t is not ToolstripPopupButton))
            {
                toggle.IsChecked = selected;
            }
        }
        finally
        {
            _quiet = false;
        }
    }

    /// <summary>Whether the toggle or check item with this tag or action id is pressed.</summary>
    public bool IsSelected(string id) => _selected.TryGetValue(id, out bool selected) && selected;

    /// <summary>An input's value as it stands (a spinner's number, a combo box's or a text field's text, typed or chosen); null for no such input.</summary>
    public string? GetValue(string tag)
    {
        foreach (FrameworkElement element in Elements(tag))
        {
            switch (element)
            {
                case ToolstripSpinnerBox spinner:
                    return spinner.Spinner.Text;
                case ComboBox combo when combo.IsEditable:
                    return combo.Text;
                case ComboBox combo:
                    return combo.SelectedItem as string;
                case TextBox box:
                    return box.Text;
            }
        }

        return null;
    }

    /// <summary>Sets an input's value (a spinner's number, a combo box's or a text field's text), without raising <see cref="Invoked"/>.</summary>
    public void SetValue(string tag, string value)
    {
        _quiet = true;
        try
        {
            foreach (FrameworkElement element in Elements(tag))
            {
                switch (element)
                {
                    case ToolstripSpinnerBox spinner:
                        spinner.Spinner.Text = value;
                        break;
                    case ComboBox combo when combo.IsEditable:
                        combo.Text = value;
                        break;
                    case ComboBox combo:
                        combo.SelectedItem = combo.Items.OfType<string>().FirstOrDefault(i => i == value);
                        break;
                    case TextBox box:
                        box.Text = value;
                        break;
                }
            }
        }
        finally
        {
            _quiet = false;
        }
    }

    /// <summary>The elements made for the controls with this tag or action id (for checks and hosts).</summary>
    public IReadOnlyList<FrameworkElement> Elements(string id)
    {
        List<FrameworkElement> found = [];
        if (_byTag.TryGetValue(id, out List<FrameworkElement>? tagged))
        {
            found.AddRange(tagged);
        }

        if (_byAction.TryGetValue(id, out List<FrameworkElement>? acting))
        {
            found.AddRange(acting.Where(e => !found.Contains(e)));
        }

        return found;
    }

    /// <summary>Each section of a tab: its tag or title, whether it is collapsed, and whether its labels show.</summary>
    public IReadOnlyList<(string Section, bool Collapsed, bool LabelsShown)> SectionStates(string tab) =>
        _tabStates.TryGetValue(tab, out TabState? state)
            ? [.. state.Sections.Select(s => (s.Spec.Tag ?? s.Spec.Title ?? "", s.IsCollapsed, s.LabelsShown))]
            : [];

    /// <summary>The width the selected tab has, and the width its sections take with the collapse button's room (for checks).</summary>
    public (double Available, double Needed) FitWidths() =>
        Selected() is { } state ? (_host.ActualWidth, SectionsWidth(state)) : (0, 0);

    /// <summary>Fits the selected tab's sections to the width now.</summary>
    public void Refit()
    {
        _fitQueued = false;
        if (Selected() is not { } state || !state.Panel.IsVisible || _host.ActualWidth <= 0)
        {
            return;
        }

        ToolstripResizer.Resize(state.Sections, () => _host.ActualWidth, () => SectionsWidth(state));
    }

    private TabState? Selected() =>
        _tabs.SelectedItem is TabItem { Tag: string tag } && _tabStates.TryGetValue(tag, out TabState? state) ? state : null;

    // The sections' widths after a layout pass: a section that collapsed or dropped its labels has
    // only its descendants' measures made invalid, so measuring the section alone would read its
    // old width.
    private double SectionsWidth(TabState state)
    {
        UpdateLayout();
        return state.Sections.Where(s => s.Visibility != Visibility.Collapsed).Sum(s => s.DesiredSize.Width) + ToolstripResizer.CollapseButtonWidth;
    }

    private bool IsShown(ToolstripTab tab) =>
        !_hiddenTabs.Contains(tab.Tag)
        && (!tab.IsContextual || _context.Contains(tab.Tag) || tab.GroupTag is not null && _context.Contains(tab.GroupTag));

    private void QueueFit()
    {
        if (!_fitQueued)
        {
            _fitQueued = true;
            Dispatcher.BeginInvoke(Refit, DispatcherPriority.Loaded);
        }
    }

    private void OnTabSelected()
    {
        HideKeyTips();
        foreach (TabState state in _tabStates.Values)
        {
            foreach (ToolstripSectionView section in state.Sections)
            {
                section.ClosePopup();
            }
        }

        _host.Content = _tabs.SelectedItem is TabItem { Tag: string tag } && _tabStates.TryGetValue(tag, out TabState? selected) ? selected.Panel : null;
        QueueFit();
        if (_minimized && _host.Content is not null && IsLoaded)
        {
            Dispatcher.BeginInvoke(ShowOverlay, DispatcherPriority.Input);
        }

        SelectedTabChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShowOverlay()
    {
        if (!_minimized || _tabs.SelectedItem is null)
        {
            return;
        }

        _overlayFrame.Width = Math.Max(ActualWidth, 200);
        _overlay.IsOpen = true;
        QueueFit();
    }

    private void UpdateMinimizeButton()
    {
        string words = _minimized ? "Restore Toolstrip" : "Minimize Toolstrip";
        _minimize.Content = new ToolstripFace(_minimized ? "expand" : "collapse", "", vertical: false, arrow: false);
        _minimize.ToolTip = words;
        AutomationProperties.SetName(_minimize, words);
    }

    // Closes every popup a control opened and the minimized toolstrip's overlay: a control has run.
    private void CloseTransients()
    {
        foreach (ToolstripPopupButton opener in _openers)
        {
            if (opener.IsChecked == true)
            {
                opener.IsChecked = false;
            }
        }

        foreach (TabState state in _tabStates.Values)
        {
            foreach (ToolstripSectionView section in state.Sections)
            {
                section.ClosePopup();
            }
        }

        _overlay.IsOpen = false;
    }

    // After a control clicked with the mouse has run, the keyboard goes back to the document, as
    // R2025b's toolstrip restores focus; a control reached by the keyboard keeps it.
    private void RestoreFocus()
    {
        if (InputManager.Current.MostRecentInputDevice is MouseDevice && _window is not null
            && FocusManager.GetFocusedElement(_window) is IInputElement document && !IsAncestorOf(document as DependencyObject ?? this))
        {
            Keyboard.Focus(document);
        }
    }

    private void Raise(string? tag, string? actionId, object? value)
    {
        if (!_quiet)
        {
            Invoked?.Invoke(this, new ToolstripInvokedEventArgs(tag, actionId, value));
        }
    }

    // The element whose visibility hides a control: a split button's pair, a spinner's frame.
    private static FrameworkElement Outer(FrameworkElement element) => element switch
    {
        { Parent: ToolstripSplitButton split } => split,
        ToolstripSpinnerBox box => box.Spinner,
        _ => element,
    };

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null and not T)
        {
            node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }

        return node as T;
    }

    /// <summary>A built tab: its header, its sections and its controls' key tips.</summary>
    private sealed class TabState(ToolstripTab spec, TabItem header, StackPanel panel, IReadOnlyList<ToolstripSectionView> sections)
    {
        public ToolstripTab Spec { get; } = spec;

        public TabItem Header { get; } = header;

        public StackPanel Panel { get; } = panel;

        public IReadOnlyList<ToolstripSectionView> Sections { get; } = sections;

        public List<(string? Mnemonic, string Label, FrameworkElement Element)> Tipped { get; } = [];

        public string Tip { get; set; } = "";
    }
}

/// <summary>The tab headers of the toolstrip: a tab control whose content the toolstrip shows itself.</summary>
internal sealed class ToolstripTabStrip : TabControl
{
    private static ControlTemplate? _template;
    private static ControlTemplate? _itemTemplate;

    public ToolstripTabStrip()
    {
        Template = _template ??= MakeTemplate();
        Style = null;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        Margin = new Thickness(4, 2, 0, 0);
        FocusVisualStyle = null;
    }

    /// <summary>A header for a tab.</summary>
    public static TabItem Header(string title, string tag)
    {
        var item = new TabItem
        {
            Header = new TextBlock { Text = title },
            Tag = tag,
            Template = _itemTemplate ??= MakeItemTemplate(),
            Style = null,
            Padding = new Thickness(10, 3, 10, 3),
            FocusVisualStyle = null,
        };
        item.SetResourceReference(Control.ForegroundProperty, ThemeKeys.Text);
        AutomationProperties.SetName(item, title);
        AutomationProperties.SetAutomationId(item, tag);
        return item;
    }

    private static ControlTemplate MakeTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(TabPanel));
        panel.SetValue(Panel.IsItemsHostProperty, true);
        panel.SetValue(KeyboardNavigation.TabIndexProperty, 1);
        var template = new ControlTemplate(typeof(TabControl)) { VisualTree = panel };
        template.Seal();
        return template;
    }

    private static ControlTemplate MakeItemTemplate()
    {
        var face = new FrameworkElementFactory(typeof(Border), "face");
        face.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        face.SetValue(Border.BorderThicknessProperty, new Thickness(1, 1, 1, 0));
        face.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        face.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        face.SetValue(Border.CornerRadiusProperty, new CornerRadius(2, 2, 0, 0));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        face.AppendChild(content);
        var template = new ControlTemplate(typeof(TabItem)) { VisualTree = face };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension(ThemeKeys.Hover), "face"));
        template.Triggers.Add(hover);
        var selected = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension(ThemeKeys.ToolBar), "face"));
        selected.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension(ThemeKeys.Border), "face"));
        selected.Setters.Add(new Setter(TextElement.FontWeightProperty, FontWeights.SemiBold));
        template.Triggers.Add(selected);
        var focused = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focused.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension(ThemeKeys.FocusRing), "face"));
        template.Triggers.Add(focused);
        template.Seal();
        return template;
    }
}
