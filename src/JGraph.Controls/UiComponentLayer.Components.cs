using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Rendering.Layout;
using JGraph.Scripting;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;

namespace JGraph.Controls;

/// <summary>
/// The <c>uifigure</c> components of the layer (app-building plan, U5): a label, the buttons, the
/// edit fields, a text area, a drop-down, a list, a check box, a radio button, a slider, a spinner,
/// an image and a hyperlink, and the dialogs a figure lays over itself. Like the classic controls
/// they are fed only frames, and what a person does to one goes back through
/// <see cref="ScriptGraphicsCallbacks.NotifyComponent"/>.
/// </summary>
public sealed partial class UiComponentLayer
{
    private readonly Dictionary<UiComponentModel, Shown> _components = new(ReferenceEqualityComparer.Instance);
    private IReadOnlyList<UiComponentPlacement> _componentPlacements = [];
    private readonly Dictionary<UiOverlayModel, FrameworkElement> _overlays = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(UiContainerModel Grid, bool Vertical), ScrollBar> _scrollBars = [];
    private readonly List<UiPanelPlacement> _scrolling = [];
    private bool _arrangingBars;
    private Grid? _overlayHost;

    /// <summary>One component as it stands in the window.</summary>
    private sealed class Shown(UiComponentModel source, UiComponentKind kind, FrameworkElement element)
    {
        public UiComponentModel Source { get; } = source;

        public UiComponentKind Kind { get; } = kind;

        /// <summary>What sits on the canvas.</summary>
        public FrameworkElement Element { get; } = element;

        /// <summary>The control that takes the keyboard, when the element is a wrapper round one.</summary>
        public Control? Input { get; init; }

        public TextBlock? Label { get; init; }

        public Image? Picture { get; init; }

        public TextBlock? Hint { get; init; }

        public UiSliderFace? Slider { get; init; }

        public UiComponentFrame? Frame { get; set; }

        /// <summary>True while a frame is being applied, so the control's own events are not taken for a person's.</summary>
        public bool Applying { get; set; }

        /// <summary>A text field's text as last committed or last shown from a frame.</summary>
        public string Committed { get; set; } = string.Empty;

        /// <summary>The person's last action not yet seen in a frame; 0 when none is outstanding.</summary>
        public long PendingSeq { get; set; }

        public int FocusSeen { get; set; }

        public UiImage? Drawn { get; set; }
    }

    /// <summary>The component of either kind that has the keyboard, or null.</summary>
    public GraphObject? FocusedObject
    {
        get
        {
            if (FocusedComponent is { } control)
            {
                return control;
            }

            foreach ((UiComponentModel model, Shown shown) in _components)
            {
                if (shown.Element.IsKeyboardFocusWithin)
                {
                    return model;
                }
            }

            return null;
        }
    }

    /// <summary>Whether a field a person types into has the keyboard: what a press elsewhere commits first.</summary>
    public bool HasFocusedTextEntry =>
        FocusedComponent is { Style: UiControlStyle.Edit }
        || _components.Values.Any(static shown => shown.Input is TextBox { IsKeyboardFocusWithin: true } or ComboBox { IsEditable: true, IsKeyboardFocusWithin: true });

    private void ClearComponents()
    {
        _components.Clear();
        _componentPlacements = [];
        _overlays.Clear();
        _overlayHost = null;
    }

    private void CommitFocusedComponent()
    {
        foreach (Shown shown in _components.Values)
        {
            if (shown.Input is TextBox { IsKeyboardFocusWithin: true })
            {
                CommitText(shown);
            }
        }
    }

    private void ApplyComponents(UiFrame frame)
    {
        var present = new HashSet<UiComponentModel>(ReferenceEqualityComparer.Instance);
        foreach (UiComponentFrame component in frame.Components)
        {
            present.Add(component.Source);
            if (!_components.TryGetValue(component.Source, out Shown? shown))
            {
                shown = Make(component);
                _components[component.Source] = shown;
                Children.Add(shown.Element);
            }

            shown.Applying = true;
            try
            {
                Refresh(shown, component);
            }
            finally
            {
                shown.Applying = false;
            }

            shown.Frame = component;
        }

        foreach (UiComponentModel gone in _components.Keys.Where(model => !present.Contains(model)).ToList())
        {
            Children.Remove(_components[gone].Element);
            _components.Remove(gone);
        }

        ApplyOverlays(frame);
    }

    private void ArrangeComponents(UiLayoutResult layout)
    {
        _componentPlacements = layout.Components;
        foreach (UiComponentPlacement placement in _componentPlacements)
        {
            if (!_components.TryGetValue(placement.Component.Source, out Shown? shown))
            {
                continue;
            }

            // A slider draws its ticks and labels outside its track, in the rectangle round it.
            Rect2D box = placement.Box;
            if (shown.Slider is not null)
            {
                box = placement.Component.Upright
                    ? new Rect2D(box.X - 7, box.Y - 8, box.Width + 36, box.Height + 16)
                    : new Rect2D(box.X - 7, box.Y - 6, box.Width + 16, box.Height + 36);
            }

            FrameworkElement element = shown.Element;
            Rect2D seen = UiLayout.Intersect(placement.Clip, box);
            SetLeft(element, box.X);
            SetTop(element, box.Y);
            element.Width = System.Math.Max(0, box.Width);
            element.Height = System.Math.Max(0, box.Height);
            element.Visibility = placement.Visible && !seen.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
            element.Clip = ClipOf(placement.Occluders, box, seen);
            SetZIndex(element, placement.Order);
        }

        ArrangeScrollBars(layout);
        if (_overlayHost is not null)
        {
            _overlayHost.Width = ActualWidth;
            _overlayHost.Height = ActualHeight;
        }
    }

    // --- a scrollable grid's bars ----------------------------------------------------------------

    /// <summary>
    /// Gives every scrollable grid whose tracks do not fit the bars the layout left room for. A bar
    /// that moves tells the script side where the grid now is, as a component tells its value; the
    /// grid moves when the next frame says so.
    /// </summary>
    private void ArrangeScrollBars(UiLayoutResult layout)
    {
        _scrolling.Clear();
        void Collect(IReadOnlyList<UiPanelPlacement> panels)
        {
            foreach (UiPanelPlacement panel in panels)
            {
                if (panel.Visible && (panel.VerticalBar is not null || panel.HorizontalBar is not null))
                {
                    _scrolling.Add(panel);
                }

                Collect(panel.Children);
            }
        }

        Collect(layout.Panels);
        var wanted = new HashSet<(UiContainerModel, bool)>();
        _arrangingBars = true;
        try
        {
            foreach (UiPanelPlacement panel in _scrolling)
            {
                if (panel.VerticalBar is { } right)
                {
                    wanted.Add((panel.Panel.Source, true));
                    PlaceBar(panel, vertical: true, right, panel.Content.Height, panel.Inner.Height, panel.ScrollY);
                }

                if (panel.HorizontalBar is { } bottom)
                {
                    wanted.Add((panel.Panel.Source, false));
                    PlaceBar(panel, vertical: false, bottom, panel.Content.Width, panel.Inner.Width, panel.ScrollX);
                }
            }
        }
        finally
        {
            _arrangingBars = false;
        }

        foreach ((UiContainerModel, bool) gone in _scrollBars.Keys.Where(key => !wanted.Contains(key)).ToList())
        {
            Children.Remove(_scrollBars[gone]);
            _scrollBars.Remove(gone);
        }
    }

    private void PlaceBar(UiPanelPlacement panel, bool vertical, Rect2D box, double content, double viewport, double offset)
    {
        UiContainerModel grid = panel.Panel.Source;
        if (!_scrollBars.TryGetValue((grid, vertical), out ScrollBar? bar))
        {
            bar = new ScrollBar
            {
                Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal,
                Minimum = 0,
                SmallChange = 20,
                Focusable = false,
            };
            System.Windows.Automation.AutomationProperties.SetAutomationId(bar, vertical ? "JG.Scroll.Vertical" : "JG.Scroll.Horizontal");
            bar.ValueChanged += (_, _) =>
            {
                if (_arrangingBars)
                {
                    return;
                }

                double x = _scrollBars.TryGetValue((grid, false), out ScrollBar? across) ? across.Value : 0;
                double y = _scrollBars.TryGetValue((grid, true), out ScrollBar? down) ? down.Value : 0;
                ScriptGraphicsCallbacks.NotifyComponent(grid, "value", new[] { x, y });
            };
            _scrollBars[(grid, vertical)] = bar;
            Children.Add(bar);
        }

        Rect2D seen = UiLayout.Intersect(panel.Clip, box);
        SetLeft(bar, box.X);
        SetTop(bar, box.Y);
        bar.Width = System.Math.Max(0, box.Width);
        bar.Height = System.Math.Max(0, box.Height);
        bar.Visibility = seen.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        bar.Clip = ClipOf([], box, seen);
        bar.Maximum = System.Math.Max(0, content - viewport);
        bar.ViewportSize = viewport;
        bar.LargeChange = System.Math.Max(1, viewport);

        // A thumb being dragged is ahead of the frame that answers it; leave it where the hand has it.
        if (!bar.IsMouseCaptureWithin && System.Math.Abs(bar.Value - offset) > 0.5)
        {
            bar.Value = offset;
        }

        SetZIndex(bar, int.MaxValue - 1);
    }

    /// <summary>The wheel over a scrollable grid moves it, three lines a notch, before anything else hears.</summary>
    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        Point at = e.GetPosition(this);
        for (int i = _scrolling.Count - 1; i >= 0; i--)
        {
            UiPanelPlacement panel = _scrolling[i];
            if (Contains(panel.Box, at) && Contains(panel.Clip, at)
                && _scrollBars.TryGetValue((panel.Panel.Source, panel.VerticalBar is not null), out ScrollBar? bar))
            {
                bar.Value = System.Math.Clamp(bar.Value - (e.Delta / 120.0 * 48), bar.Minimum, bar.Maximum);
                e.Handled = true;
                return;
            }
        }
    }

    /// <summary>The <c>uifigure</c> component showing at a point of the layer, or null.</summary>
    private UiComponentModel? UiComponentAt(Point point)
    {
        for (int i = _componentPlacements.Count - 1; i >= 0; i--)
        {
            UiComponentPlacement placement = _componentPlacements[i];
            if (placement.Visible && Contains(placement.Box, point) && Contains(placement.Clip, point)
                && !placement.Occluders.Any(over => Contains(over, point)))
            {
                return placement.Component.Source;
            }
        }

        return null;
    }

    // --- making ----------------------------------------------------------------------------------

    private static void Tell(Shown shown, string action, object? value = null)
    {
        if (shown.Applying)
        {
            return;
        }

        long seq = ScriptGraphicsCallbacks.NotifyComponent(shown.Source, action, value);
        if (action == "value")
        {
            shown.PendingSeq = seq;
        }

        ScriptUiTrace.Write($"layer: {shown.Kind} {action} [{value}] as {seq}");
    }

    private Shown Make(UiComponentFrame frame)
    {
        UiComponentModel source = frame.Source;
        switch (frame.Kind)
        {
            case UiComponentKind.Button:
            {
                var button = new Button { Style = (Style)Styles["JG.Ui.PushButton"] };
                Shown shown = Faced(source, frame.Kind, button);
                button.Click += (_, _) => Tell(shown, "pushed");
                return shown;
            }

            case UiComponentKind.StateButton:
            {
                var button = new ToggleButton { Style = (Style)Styles["JG.Ui.ToggleButton"] };
                Shown shown = Faced(source, frame.Kind, button);
                button.Checked += (_, _) => Tell(shown, "value", true);
                button.Unchecked += (_, _) => Tell(shown, "value", false);
                return shown;
            }

            case UiComponentKind.ToggleButton:
            {
                // In a group: a press selects it, and a press on the selected one leaves it so.
                var button = new ToggleButton { Style = (Style)Styles["JG.Ui.ToggleButton"] };
                Shown shown = Faced(source, frame.Kind, button);
                // Heard as it changes rather than as it is clicked, so that UI Automation's Toggle —
                // which presses nothing — is a press too.
                button.Checked += (_, _) =>
                {
                    if (shown.Frame?.Checked != true)
                    {
                        Tell(shown, "value", true);
                    }
                };
                button.Unchecked += (_, _) =>
                {
                    if (!shown.Applying && shown.Frame?.Checked == true)
                    {
                        button.IsChecked = true;
                    }
                };
                return shown;
            }

            case UiComponentKind.CheckBox:
            {
                var check = new CheckBox { Style = (Style)Styles["JG.Ui.CheckBox"] };
                var face = new Border { Child = check, SnapsToDevicePixels = true };
                var shown = new Shown(source, frame.Kind, face) { Input = check };
                check.Checked += (_, _) => Tell(shown, "value", true);
                check.Unchecked += (_, _) => Tell(shown, "value", false);
                return shown;
            }

            case UiComponentKind.RadioButton:
            {
                // A group of its own, so WPF never deselects a neighbour: the model says which is on.
                var radio = new RadioButton { Style = (Style)Styles["JG.Ui.RadioButton"], GroupName = Guid.NewGuid().ToString("N") };
                var face = new Border { Child = radio, SnapsToDevicePixels = true };
                var shown = new Shown(source, frame.Kind, face) { Input = radio };
                radio.Checked += (_, _) => Tell(shown, "value", true);
                return shown;
            }

            case UiComponentKind.EditField:
            case UiComponentKind.NumericEditField:
            case UiComponentKind.TextArea:
            {
                var box = new TextBox { Style = (Style)Styles["JG.Ui.Edit"] };
                bool lines = frame.Kind == UiComponentKind.TextArea;
                if (lines)
                {
                    box.AcceptsReturn = true;
                    box.VerticalContentAlignment = VerticalAlignment.Top;
                    box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                }

                var hint = new TextBlock
                {
                    IsHitTestVisible = false,
                    Opacity = 0.55,
                    Margin = new System.Windows.Thickness(4, lines ? 2 : 0, 4, 0),
                    VerticalAlignment = lines ? VerticalAlignment.Top : VerticalAlignment.Center,
                };
                var wrap = new Grid();
                wrap.Children.Add(box);
                wrap.Children.Add(hint);
                var shown = new Shown(source, frame.Kind, wrap) { Input = box, Hint = hint };
                box.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Return && !lines)
                    {
                        CommitText(shown);
                    }
                };
                box.LostKeyboardFocus += (_, _) => CommitText(shown);
                box.TextChanged += (_, _) =>
                {
                    hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                    if (!shown.Applying && box.IsKeyboardFocusWithin && shown.Kind != UiComponentKind.NumericEditField)
                    {
                        Tell(shown, "changing", box.Text);
                    }
                };
                return shown;
            }

            case UiComponentKind.Spinner:
            {
                var box = new TextBox { Style = (Style)Styles["JG.Ui.Edit"] };
                var up = new RepeatButton { Style = (Style)Styles["JG.Ui.SpinButton"], Content = "▴" };
                var down = new RepeatButton { Style = (Style)Styles["JG.Ui.SpinButton"], Content = "▾" };
                AutomationProperties.SetAutomationId(up, "Up");
                AutomationProperties.SetAutomationId(down, "Down");
                var arrows = new UniformGrid { Rows = 2, Columns = 1, Width = 16 };
                arrows.Children.Add(up);
                arrows.Children.Add(down);
                var wrap = new DockPanel { LastChildFill = true };
                DockPanel.SetDock(arrows, Dock.Right);
                wrap.Children.Add(arrows);
                wrap.Children.Add(box);
                var shown = new Shown(source, frame.Kind, wrap) { Input = box };
                void Step(int direction)
                {
                    if (shown.Frame is not { Editable: true } current)
                    {
                        return;
                    }

                    // From what is typed, when it is a number; otherwise from the value shown last.
                    double from = double.TryParse(box.Text, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double typed)
                        ? typed
                        : current.HasNumber ? current.Number : 0;
                    double stepped = from + (direction * current.Step);
                    shown.Committed = box.Text;
                    Tell(shown, "value", stepped);
                }

                up.Click += (_, _) => Step(1);
                down.Click += (_, _) => Step(-1);
                box.PreviewKeyDown += (_, e) =>
                {
                    if (e.Key is Key.Up or Key.Down)
                    {
                        Step(e.Key == Key.Up ? 1 : -1);
                        e.Handled = true;
                    }
                    else if (e.Key == Key.Return)
                    {
                        CommitText(shown);
                    }
                };
                box.LostKeyboardFocus += (_, _) => CommitText(shown);
                return shown;
            }

            case UiComponentKind.DropDown:
            {
                var combo = new ComboBox { Style = (Style)Styles["JG.Ui.PopupMenu"] };
                var shown = new Shown(source, frame.Kind, combo) { Input = combo };
                combo.DropDownOpened += (_, _) => Tell(shown, "opening");
                combo.SelectionChanged += (_, _) =>
                {
                    if (!shown.Applying && combo.SelectedIndex >= 0)
                    {
                        Tell(shown, "value", combo.SelectedIndex);
                    }
                };
                combo.PreviewMouseLeftButtonUp += (_, _) => Tell(shown, "clicked", combo.SelectedIndex);
                void CommitTyped()
                {
                    if (combo.IsEditable && !shown.Applying && combo.Text != shown.Committed)
                    {
                        shown.Committed = combo.Text;
                        Tell(shown, "value", combo.Text);
                    }
                }

                combo.LostKeyboardFocus += (_, _) => CommitTyped();
                combo.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Return)
                    {
                        CommitTyped();
                    }
                };
                return shown;
            }

            case UiComponentKind.ListBox:
            {
                var list = new ListBox { Style = (Style)Styles["JG.Ui.ListBox"] };
                var shown = new Shown(source, frame.Kind, list) { Input = list };
                list.SelectionChanged += (_, _) =>
                {
                    if (shown.Applying)
                    {
                        return;
                    }

                    var picked = new List<int>();
                    foreach (object item in list.SelectedItems)
                    {
                        picked.Add(list.Items.IndexOf(item));
                    }

                    Tell(shown, "value", picked.ToArray());
                };
                list.PreviewMouseLeftButtonUp += (_, _) => Dispatcher.BeginInvoke(() => Tell(shown, "clicked", list.SelectedIndex));
                list.MouseDoubleClick += (_, _) => Tell(shown, "doubleclicked", list.SelectedIndex);
                return shown;
            }

            case UiComponentKind.Slider:
            case UiComponentKind.RangeSlider:
            {
                var face = new UiSliderFace { IsRange = frame.Kind == UiComponentKind.RangeSlider };
                var shown = new Shown(source, frame.Kind, face) { Slider = face };
                face.Changing += values => Tell(shown, "changing", face.IsRange ? values : values[0]);
                face.Changed += values => Tell(shown, "value", face.IsRange ? values : values[0]);
                return shown;
            }

            case UiComponentKind.Image:
            {
                var picture = new Image();
                var border = new Border { Child = picture, SnapsToDevicePixels = true, Background = Brushes.Transparent };
                var shown = new Shown(source, frame.Kind, border) { Picture = picture };
                border.MouseLeftButtonUp += (_, _) =>
                {
                    if (shown.Frame is { Enabled: true } current)
                    {
                        Tell(shown, "image");
                        Follow(current.Text);
                    }
                };
                return shown;
            }

            case UiComponentKind.Hyperlink:
            {
                var label = new UiLinkText { TextDecorations = TextDecorations.Underline, Cursor = Cursors.Hand };
                var border = new Border { Child = label, SnapsToDevicePixels = true, Background = Brushes.Transparent };
                var shown = new Shown(source, frame.Kind, border) { Label = label };
                label.Followed += (_, _) =>
                {
                    if (shown.Frame is { Enabled: true } current)
                    {
                        Tell(shown, "link");
                        Follow(current.Text);
                    }
                };
                return shown;
            }

            default:
            {
                var label = new TextBlock();
                var border = new Border { Child = label, SnapsToDevicePixels = true };
                return new Shown(source, frame.Kind, border) { Label = label };
            }
        }
    }

    /// <summary>A button with its face: an icon beside its text.</summary>
    private static Shown Faced(UiComponentModel source, UiComponentKind kind, ButtonBase button)
    {
        var picture = new Image { Stretch = Stretch.Uniform, MaxHeight = 16, MaxWidth = 16, Margin = new System.Windows.Thickness(0, 0, 4, 0), Visibility = Visibility.Collapsed };
        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
        var content = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(picture, Dock.Left);
        content.Children.Add(picture);
        content.Children.Add(label);
        button.Content = content;
        return new Shown(source, kind, button) { Input = button, Label = label, Picture = picture };
    }

    /// <summary>Opens a link's address in the person's browser, when it has one.</summary>
    private static void Follow(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        string address = url.Contains("://", StringComparison.Ordinal) || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            ? url
            : "https://" + url;
        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https" or "mailto"))
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ScriptUiTrace.Write($"layer: could not open {uri.AbsoluteUri}: {ex.Message}");
        }
    }

    /// <summary>Hands a text field's changed text to the script, once.</summary>
    private static void CommitText(Shown shown)
    {
        if (shown.Input is not TextBox box || box.Text == shown.Committed || shown.Applying)
        {
            return;
        }

        shown.Committed = box.Text;
        Tell(shown, "value", shown.Kind == UiComponentKind.TextArea
            ? box.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            : box.Text);
    }

    // --- refreshing ------------------------------------------------------------------------------

    private static void Refresh(Shown shown, UiComponentFrame frame)
    {
        FrameworkElement element = shown.Element;
        Brush foreground = BrushOf(frame.Foreground)!;
        Brush? background = BrushOf(frame.Background);
        var family = new FontFamily(UiLayout.FontFamily(frame.FontName));
        FontWeight weight = frame.Bold ? FontWeights.Bold : FontWeights.Normal;
        FontStyle style = frame.Italic ? FontStyles.Italic : FontStyles.Normal;
        double size = System.Math.Max(1, frame.FontSize);
        element.IsEnabled = frame.Enabled;
        element.Opacity = frame.Enabled ? 1 : 0.5;
        element.ToolTip = frame.Tooltip.Length > 0 ? frame.Tooltip : null;
        string caption = string.Join("\n", frame.Lines);

        // A label, a link and a picture sit in a border, which UI Automation does not see: the
        // name goes on what it does see.
        FrameworkElement named = shown.Input ?? (element is Border ? (FrameworkElement?)shown.Label ?? shown.Picture : null) ?? element;
        AutomationProperties.SetAutomationId(named, frame.Tag);

        bool waiting = shown.PendingSeq > 0 && frame.UserWriteSeq < shown.PendingSeq;
        if (!waiting)
        {
            shown.PendingSeq = 0;
        }

        if (shown.Input is { } control)
        {
            control.Foreground = foreground;
            control.FontFamily = family;
            control.FontWeight = weight;
            control.FontStyle = style;
            control.FontSize = size;
        }

        if (shown.Label is { } label)
        {
            label.Foreground = shown.Kind == UiComponentKind.Hyperlink && frame.Checked ? BrushOf(frame.Accent) ?? foreground : foreground;
            label.FontFamily = family;
            label.FontWeight = weight;
            label.FontStyle = style;
            label.FontSize = size;
            label.Text = caption;
            label.TextWrapping = frame.WordWrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
            label.TextAlignment = frame.Horizontal switch
            {
                UiHorizontalAlignment.Left => TextAlignment.Left,
                UiHorizontalAlignment.Right => TextAlignment.Right,
                _ => TextAlignment.Center,
            };
        }

        switch (shown.Kind)
        {
            case UiComponentKind.Label:
            case UiComponentKind.Hyperlink:
            {
                var border = (Border)element;
                border.Background = background ?? Brushes.Transparent;
                shown.Label!.HorizontalAlignment = Across(frame.Horizontal);
                shown.Label.VerticalAlignment = Down(frame.Vertical);
                AutomationProperties.SetName(element, caption);
                break;
            }

            case UiComponentKind.Button:
            case UiComponentKind.StateButton:
            case UiComponentKind.ToggleButton:
            {
                var button = (ButtonBase)element;
                button.Background = background ?? Brushes.Transparent;
                button.HorizontalContentAlignment = Across(frame.Horizontal);
                button.VerticalContentAlignment = Down(frame.Vertical);
                AutomationProperties.SetName(button, caption);
                if (shown.Picture is { } picture && !ReferenceEquals(shown.Drawn, frame.Image))
                {
                    shown.Drawn = frame.Image;
                    picture.Source = SourceOf(frame.Image);
                    picture.Visibility = frame.Image is null ? Visibility.Collapsed : Visibility.Visible;
                }

                if (button is ToggleButton toggle && !waiting)
                {
                    toggle.IsChecked = frame.Checked;
                }

                break;
            }

            case UiComponentKind.CheckBox:
            case UiComponentKind.RadioButton:
            {
                var mark = (ToggleButton)shown.Input!;
                mark.Content = caption;
                AutomationProperties.SetName(mark, caption);
                if (!waiting)
                {
                    mark.IsChecked = frame.Checked;
                }

                break;
            }

            case UiComponentKind.EditField:
            case UiComponentKind.NumericEditField:
            case UiComponentKind.TextArea:
            case UiComponentKind.Spinner:
            {
                var box = (TextBox)shown.Input!;
                box.Background = background ?? Brushes.Transparent;
                box.IsReadOnly = !frame.Editable;
                box.TextAlignment = frame.Horizontal switch
                {
                    UiHorizontalAlignment.Left => TextAlignment.Left,
                    UiHorizontalAlignment.Right => TextAlignment.Right,
                    _ => TextAlignment.Center,
                };
                if (shown.Kind == UiComponentKind.TextArea)
                {
                    box.TextWrapping = frame.WordWrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
                    box.HorizontalScrollBarVisibility = frame.WordWrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
                }

                if (shown.Hint is { } hint)
                {
                    hint.Text = frame.Placeholder;
                    hint.FontFamily = family;
                    hint.FontSize = size;
                    hint.Foreground = foreground;
                    hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                }

                if (waiting)
                {
                    break;
                }

                // Mid-edit, what the person is typing stays until it is committed.
                if (box.IsKeyboardFocusWithin && box.Text != shown.Committed)
                {
                    break;
                }

                if (box.Text.Replace("\r\n", "\n", StringComparison.Ordinal) != frame.Text)
                {
                    box.Text = frame.Text;
                }

                shown.Committed = box.Text;
                break;
            }

            case UiComponentKind.DropDown:
            {
                var combo = (ComboBox)shown.Input!;
                combo.Background = background ?? Brushes.Transparent;
                combo.IsEditable = frame.Editable;
                SetItemTexts(combo, frame.Items);
                if (!waiting && !combo.IsDropDownOpen)
                {
                    if (frame.Editable && frame.HasNumber && frame.Text.Length > 0)
                    {
                        combo.SelectedIndex = -1;
                        combo.Text = frame.Text;
                    }
                    else
                    {
                        combo.SelectedIndex = frame.Selected.Count > 0 && frame.Selected[0] < combo.Items.Count ? frame.Selected[0] : -1;
                    }

                    shown.Committed = combo.Text;
                }

                break;
            }

            case UiComponentKind.ListBox:
            {
                var list = (ListBox)shown.Input!;
                list.Background = background ?? Brushes.Transparent;
                SetItemTexts(list, frame.Items);
                list.SelectionMode = frame.Multi ? SelectionMode.Extended : SelectionMode.Single;
                if (!waiting)
                {
                    var wanted = new HashSet<int>(frame.Selected.Where(i => i >= 0 && i < list.Items.Count));
                    var current = new HashSet<int>();
                    foreach (object item in list.SelectedItems)
                    {
                        current.Add(list.Items.IndexOf(item));
                    }

                    if (!current.SetEquals(wanted))
                    {
                        if (frame.Multi)
                        {
                            list.SelectedItems.Clear();
                            foreach (int index in wanted.OrderBy(static i => i))
                            {
                                list.SelectedItems.Add(list.Items[index]);
                            }
                        }
                        else
                        {
                            list.SelectedIndex = wanted.Count == 0 ? -1 : wanted.Min();
                        }
                    }
                }

                break;
            }

            case UiComponentKind.Slider:
            case UiComponentKind.RangeSlider:
            {
                UiSliderFace face = shown.Slider!;
                face.Minimum = frame.Min;
                face.Maximum = frame.Max;
                face.Step = frame.Step;
                face.Upright = frame.Upright;
                face.MajorTicks = frame.MajorTicks;
                face.MinorTicks = frame.MinorTicks;
                face.TickLabels = frame.TickLabels;
                face.Foreground = foreground;
                face.Face = new Typeface(family, style, weight, FontStretches.Normal);
                face.FontSize = size;
                if (!waiting && !face.IsMouseCaptured)
                {
                    face.Low = frame.Number;
                    face.High = frame.Number2;
                }

                face.InvalidateVisual();
                break;
            }

            case UiComponentKind.Image:
            {
                var border = (Border)element;
                border.Background = background ?? Brushes.Transparent;
                border.Cursor = frame.Text.Length > 0 ? Cursors.Hand : null;
                Image picture = shown.Picture!;
                if (!ReferenceEquals(shown.Drawn, frame.Image))
                {
                    shown.Drawn = frame.Image;
                    picture.Source = SourceOf(frame.Image);
                }

                picture.Stretch = frame.Word switch
                {
                    "fill" => Stretch.UniformToFill,
                    "none" => Stretch.None,
                    "stretch" => Stretch.Fill,
                    _ => Stretch.Uniform,
                };
                picture.StretchDirection = frame.Word switch
                {
                    "scaledown" => StretchDirection.DownOnly,
                    "scaleup" => StretchDirection.UpOnly,
                    _ => StretchDirection.Both,
                };
                picture.HorizontalAlignment = Across(frame.Horizontal);
                picture.VerticalAlignment = Down(frame.Vertical);
                break;
            }
        }

        // focus(h): the keyboard goes to the component, once per asking.
        if (frame.FocusRequests > shown.FocusSeen)
        {
            shown.FocusSeen = frame.FocusRequests;
            UIElement target = shown.Input ?? (UIElement)element;
            if (target.Focusable && element.IsEnabled)
            {
                target.Dispatcher.BeginInvoke(() => target.Focus());
            }
        }
    }

    private static void SetItemTexts(ItemsControl list, IReadOnlyList<string> items)
    {
        bool same = list.Items.Count == items.Count;
        for (int i = 0; same && i < items.Count; i++)
        {
            same = (string)((ContentControl)list.Items[i]).Content == items[i];
        }

        if (same)
        {
            return;
        }

        list.Items.Clear();
        foreach (string item in items)
        {
            list.Items.Add(list is ComboBox ? new ComboBoxItem { Content = item } : new ListBoxItem { Content = item });
        }
    }

    private static BitmapSource? SourceOf(UiImage? image) => image is null
        ? null
        : BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Bgra, image.Width * 4);

    private static WpfHorizontalAlignment Across(UiHorizontalAlignment alignment) => alignment switch
    {
        UiHorizontalAlignment.Left => WpfHorizontalAlignment.Left,
        UiHorizontalAlignment.Right => WpfHorizontalAlignment.Right,
        _ => WpfHorizontalAlignment.Center,
    };

    private static VerticalAlignment Down(UiVerticalAlignment alignment) => alignment switch
    {
        UiVerticalAlignment.Top => VerticalAlignment.Top,
        UiVerticalAlignment.Bottom => VerticalAlignment.Bottom,
        _ => VerticalAlignment.Center,
    };

    // --- the dialogs over the figure -------------------------------------------------------------

    private void ApplyOverlays(UiFrame frame)
    {
        if (frame.Overlays.Count == 0)
        {
            if (_overlayHost is not null)
            {
                Children.Remove(_overlayHost);
                _overlayHost = null;
                _overlays.Clear();
            }

            return;
        }

        if (_overlayHost is null)
        {
            _overlayHost = new Grid { Width = ActualWidth, Height = ActualHeight };
            SetZIndex(_overlayHost, int.MaxValue);
            Children.Add(_overlayHost);
        }

        // Rebuilt whole: there are never many, and a progress bar's value is the only thing that moves.
        _overlayHost.Children.Clear();
        _overlays.Clear();
        bool modal = frame.Overlays.Any(static overlay => overlay.Modal || overlay.Kind != UiOverlayKind.Alert);
        _overlayHost.Background = modal ? new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0)) : null;
        foreach (UiOverlayFrame overlay in frame.Overlays)
        {
            FrameworkElement dialog = MakeOverlay(overlay);
            _overlays[overlay.Source] = dialog;
            _overlayHost.Children.Add(dialog);
        }
    }

    private FrameworkElement MakeOverlay(UiOverlayFrame overlay)
    {
        UiOverlayModel source = overlay.Source;
        var title = new TextBlock
        {
            Text = overlay.Title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Margin = new System.Windows.Thickness(12, 8, 12, 8),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var header = new Border { Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)), Child = title };
        var message = new TextBlock
        {
            Text = string.Join("\n", overlay.Message),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(message, "JG.Overlay.Message");
        var body = new DockPanel { Margin = new System.Windows.Thickness(12, 12, 12, 8), LastChildFill = true };
        if (IconOf(overlay) is { } icon)
        {
            DockPanel.SetDock(icon, Dock.Left);
            body.Children.Add(icon);
        }

        body.Children.Add(message);
        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(body);

        if (overlay.Kind == UiOverlayKind.Progress)
        {
            var bar = new ProgressBar
            {
                Style = (Style)Styles["JG.Ui.Progress"],
                Minimum = 0,
                Maximum = 1,
                Value = overlay.Value,
                IsIndeterminate = overlay.Indeterminate,
                Height = 8,
                Margin = new System.Windows.Thickness(12, 0, 12, 10),
            };
            AutomationProperties.SetAutomationId(bar, "JG.Overlay.Progress");
            stack.Children.Add(bar);
            if (overlay.ShowPercentage && !overlay.Indeterminate)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = $"{System.Math.Round(overlay.Value * 100)}%",
                    FontSize = 11,
                    HorizontalAlignment = WpfHorizontalAlignment.Right,
                    Margin = new System.Windows.Thickness(12, -6, 12, 8),
                });
            }
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = WpfHorizontalAlignment.Right,
            Margin = new System.Windows.Thickness(12, 0, 12, 12),
        };
        void AddButton(string text, int option, bool isDefault)
        {
            var button = new Button
            {
                Style = (Style)Styles["JG.Ui.PushButton"],
                Content = text,
                MinWidth = 72,
                Height = 24,
                Padding = new System.Windows.Thickness(10, 0, 10, 0),
                Margin = new System.Windows.Thickness(8, 0, 0, 0),
                Background = isDefault ? new SolidColorBrush(Color.FromRgb(0xDD, 0xEC, 0xFA)) : new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)),
                IsDefault = isDefault,
            };
            AutomationProperties.SetAutomationId(button, $"JG.Overlay.Option{option + 1}");
            AutomationProperties.SetName(button, text);
            button.Click += (_, _) => ScriptGraphicsCallbacks.NotifyOverlay(source, option);
            buttons.Children.Add(button);
        }

        if (overlay.Kind == UiOverlayKind.Progress)
        {
            if (overlay.Cancelable)
            {
                AddButton(overlay.CancelText, 0, isDefault: false);
            }
        }
        else
        {
            for (int i = 0; i < overlay.Options.Count; i++)
            {
                AddButton(overlay.Options[i], i, i == overlay.DefaultOption);
            }
        }

        if (buttons.Children.Count > 0)
        {
            stack.Children.Add(buttons);
        }

        var dialog = new Border
        {
            Child = stack,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x7D, 0x7D, 0x7D)),
            BorderThickness = new System.Windows.Thickness(1),
            CornerRadius = new CornerRadius(4),
            MinWidth = 260,
            MaxWidth = 420,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true,
        };
        AutomationProperties.SetAutomationId(dialog, "JG.Overlay");
        AutomationProperties.SetName(dialog, overlay.Title);
        return dialog;
    }

    /// <summary>A dialog's icon: the picture it was given, or a coloured disc with the stock word's sign.</summary>
    private static FrameworkElement? IconOf(UiOverlayFrame overlay)
    {
        if (overlay.Image is { } image)
        {
            return new Image
            {
                Source = SourceOf(image),
                Width = 32,
                Height = 32,
                Margin = new System.Windows.Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Top,
            };
        }

        (string sign, Color colour) = overlay.Icon switch
        {
            "error" => ("✕", Color.FromRgb(0xD3, 0x2F, 0x2F)),
            "warning" => ("!", Color.FromRgb(0xED, 0x8B, 0x00)),
            "info" => ("i", Color.FromRgb(0x00, 0x78, 0xD7)),
            "success" => ("✓", Color.FromRgb(0x2E, 0x8B, 0x3D)),
            "question" => ("?", Color.FromRgb(0x00, 0x78, 0xD7)),
            _ => (string.Empty, Colors.Transparent),
        };
        if (sign.Length == 0)
        {
            return null;
        }

        var disc = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(colour),
            Margin = new System.Windows.Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = sign,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 16,
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        return disc;
    }
}
