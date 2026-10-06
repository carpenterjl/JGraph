using System.Windows;
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
/// The WPF realisation of a figure's components (app-building plan, section A, stages U1 and U3). It
/// lies over the figure canvas, takes no input outside its controls, and is fed only frames: it never
/// reads the model, so a script writing the model on its own thread cannot tear what it shows. A
/// user's action goes back through <see cref="ScriptGraphicsCallbacks.NotifyUserValue"/>, and the
/// control keeps showing what the user did until a frame arrives that has taken it.
/// <para>
/// Every <c>uicontrol</c> style has a control here: a push button, a toggle button, a radio button, a
/// check box, an edit field of one line or of several, a label, a scroll bar for <c>slider</c>, a
/// bordered box for <c>frame</c>, a list and a drop-down list. A control that is not <c>'on'</c> takes
/// no input; a press on it, and a right press on any control, is the control's <c>ButtonDownFcn</c>.
/// </para>
/// </summary>
public sealed partial class UiComponentLayer : Canvas
{
    private static readonly ResourceDictionary Styles = new()
    {
        Source = new Uri("pack://application:,,,/JGraph.Controls;component/Themes/UiComponents.xaml"),
    };

    private readonly Dictionary<UiControlModel, Realised> _controls = new(ReferenceEqualityComparer.Instance);
    private IReadOnlyList<UiPlacement> _placements = [];
    private SelectionKind? _reportedDown;
    private FigureModel? _figure;
    private UiFrame? _frame;

    public UiComponentLayer()
    {
        ClipToBounds = true;
        Background = null; // the canvas below takes every click that misses a control
        SizeChanged += (_, _) => Arrange();

        // The parts inside a list, a drop-down list and a multi-line field — their scroll bars and
        // their rows — would otherwise wear the IDE's implicit styles. An empty style of the type,
        // found here first, leaves them the system's own look, which is the one a component wears.
        foreach (Type part in new[]
                 {
                     typeof(ScrollBar), typeof(ListBoxItem), typeof(ComboBoxItem), typeof(ToggleButton), typeof(TextBox),

                     // A table's grid and a tab group's headings (U8), part by part.
                     typeof(DataGrid), typeof(DataGridCell), typeof(DataGridRow), typeof(DataGridColumnHeader), typeof(DataGridRowHeader),
                     typeof(CheckBox), typeof(ComboBox), typeof(TabControl), typeof(TabItem),

                     // A tree and a date picker's calendar (U9).
                     typeof(TreeView), typeof(TreeViewItem), typeof(Calendar), typeof(CalendarItem), typeof(CalendarDayButton), typeof(CalendarButton),
                 })
        {
            Resources.Add(part, new Style(part));
        }
    }

    /// <summary>The component with the keyboard, or null when none of this layer's controls has it.</summary>
    public UiControlModel? FocusedComponent
    {
        get
        {
            foreach ((UiControlModel model, Realised realised) in _controls)
            {
                if (realised.Element.IsKeyboardFocusWithin)
                {
                    return model;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Shows <paramref name="figure"/>'s components, starting from the last frame its script took.
    /// Switching to another figure drops every control the previous one had.
    /// </summary>
    public void Bind(FigureModel? figure)
    {
        if (!ReferenceEquals(figure, _figure))
        {
            Children.Clear();
            _controls.Clear();
            ClearComponents();
            _placements = [];
            _frame = null;
            _figure = figure;
        }

        if (figure?.LastComponentFrame is { } frame)
        {
            Apply(frame);
        }
    }

    /// <summary>Applies a frame of the bound figure; a frame of another figure, or an older one, is ignored.</summary>
    public void Apply(UiFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!ReferenceEquals(frame.Figure, _figure) || (_frame is not null && frame.Sequence <= _frame.Sequence))
        {
            return;
        }

        _frame = frame;
        ScriptUiTrace.Write($"layer: frame {frame.Sequence} applied, {frame.Controls.Count} controls");
        var present = new HashSet<UiControlModel>(ReferenceEqualityComparer.Instance);
        int z = 0;
        foreach (UiControlFrame control in frame.Controls)
        {
            present.Add(control.Source);
            if (!_controls.TryGetValue(control.Source, out Realised? realised) || realised.Kind != KindOf(control))
            {
                if (realised is not null)
                {
                    Children.Remove(realised.Element);
                }

                realised = Realise(control);
                _controls[control.Source] = realised;
                Children.Add(realised.Element);
            }

            SetZIndex(realised.Element, z++);
            realised.Applying = true;
            try
            {
                Update(realised, control);
            }
            finally
            {
                realised.Applying = false;
            }

            realised.Frame = control;
        }

        foreach (UiControlModel gone in _controls.Keys.Where(model => !present.Contains(model)).ToList())
        {
            Children.Remove(_controls[gone].Element);
            _controls.Remove(gone);
        }

        ApplyComponents(frame);
        Arrange();
    }

    /// <summary>Commits the focused edit field, if its text changed — what a click elsewhere does first.</summary>
    public void CommitFocusedEdit()
    {
        foreach (Realised realised in _controls.Values)
        {
            if (realised.Control is TextBox { IsKeyboardFocusWithin: true })
            {
                Commit(realised);
            }
        }

        CommitFocusedComponent();
    }

    /// <summary>
    /// The component showing at a point of the layer — the front one, where it is not clipped away or
    /// covered by a panel — or null when the point is on none.
    /// </summary>
    public UiControlModel? ComponentAt(Point point)
    {
        for (int i = _placements.Count - 1; i >= 0; i--)
        {
            UiPlacement placement = _placements[i];
            if (!placement.Visible || !Contains(placement.Box, point) || !Contains(placement.Clip, point))
            {
                continue;
            }

            bool covered = false;
            foreach (Rect2D over in placement.Occluders)
            {
                covered |= Contains(over, point);
            }

            if (!covered)
            {
                return placement.Control.Source;
            }
        }

        return null;
    }

    /// <summary>
    /// A press on a control that is not <c>'on'</c> — and a right or middle press on any control — is
    /// the control's <c>ButtonDownFcn</c>, and an <c>'inactive'</c> control hears nothing more of it.
    /// </summary>
    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);

        // In a uifigure a press on a component is the window's too: WindowButtonDownFcn runs for it
        // and the component still answers (R2025b, window session u1w_uitest).
        if (_figure is { IsUiFigure: true } && UiComponentAt(e.GetPosition(this)) is not null)
        {
            Point over = e.GetPosition(this);
            SelectionKind gesture = e.ClickCount > 1 ? SelectionKind.Open
                : e.ChangedButton == MouseButton.Right || Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? SelectionKind.Alt
                : e.ChangedButton == MouseButton.Middle || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? SelectionKind.Extend
                : SelectionKind.Normal;
            ScriptGraphicsCallbacks.NotifyWindowButton(_figure, pressed: true, gesture, (over.X, over.Y));
            _reportedDown = gesture;
            return;
        }

        if (_figure is null || ComponentAt(e.GetPosition(this)) is not { } component
            || !_controls.TryGetValue(component, out Realised? realised) || realised.Frame is not { } frame)
        {
            return;
        }

        int button = e.ChangedButton switch
        {
            MouseButton.Middle => 2,
            MouseButton.Right => 3,
            _ => 1,
        };
        SelectionKind selection = e.ClickCount > 1 ? SelectionKind.Open
            : e.ChangedButton == MouseButton.Right || Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? SelectionKind.Alt
            : e.ChangedButton == MouseButton.Middle || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? SelectionKind.Extend
            : SelectionKind.Normal;
        if (frame.Enable == UiEnable.On && button == 1)
        {
            // The control's own press: the window hears nothing, and the gesture is on record for
            // the control's callback to read.
            ScriptGraphicsCallbacks.NoteSelectionType(_figure, selection);
            return;
        }

        // R2025b's order (window session u3w_clicks): the window's WindowButtonDownFcn, then the
        // control's ButtonDownFcn; its Callback does not run.
        ScriptUiTrace.Write($"layer: button {button} down on a control that is {frame.Enable}");
        Point at = e.GetPosition(this);
        ScriptGraphicsCallbacks.NotifyWindowButton(_figure, pressed: true, selection, (at.X, at.Y));
        ScriptGraphicsCallbacks.NotifyButtonDown(_figure, component, null, null, button);
        _reportedDown = selection;
        if (frame.Enable != UiEnable.On)
        {
            e.Handled = true;
        }
    }

    /// <summary>The release of a press the window was told of is the window's to hear too.</summary>
    protected override void OnPreviewMouseUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseUp(e);
        if (_reportedDown is { } selection && _figure is not null)
        {
            _reportedDown = null;
            Point at = e.GetPosition(this);
            ScriptGraphicsCallbacks.NotifyWindowButton(_figure, pressed: false, selection, (at.X, at.Y));
        }
    }

    private static bool Contains(Rect2D box, Point point) =>
        point.X >= box.X && point.X < box.X + box.Width && point.Y >= box.Y && point.Y < box.Y + box.Height;

    private void Arrange()
    {
        if (_frame is null)
        {
            return;
        }

        UiLayoutResult layout = UiLayout.Compute(_frame, new Size2D(ActualWidth, ActualHeight));
        _placements = layout.Controls;
        ArrangeComponents(layout);
        foreach (UiPlacement placement in _placements)
        {
            if (!_controls.TryGetValue(placement.Control.Source, out Realised? realised))
            {
                continue;
            }

            FrameworkElement element = realised.Element;
            Rect2D box = placement.Box;
            Rect2D shown = UiLayout.Intersect(placement.Clip, box);
            SetLeft(element, box.X);
            SetTop(element, box.Y);
            element.Width = box.Width;
            element.Height = box.Height;
            element.Visibility = placement.Visible && !shown.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
            element.Clip = ClipOf(placement.Occluders, box, shown);
            SetZIndex(element, placement.Order);

            // A slider lies along its longer side.
            if (realised.Control is ScrollBar bar)
            {
                Orientation lie = box.Width >= box.Height ? Orientation.Horizontal : Orientation.Vertical;
                if (bar.Orientation != lie)
                {
                    bar.Orientation = lie;
                    if (realised.Frame is { } frame)
                    {
                        realised.Applying = true;
                        SetSlider(bar, frame);
                        realised.Applying = false;
                    }
                }
            }
        }
    }

    /// <summary>
    /// What a control's containers leave of it (app-building plan, U2), in its own coordinates: the
    /// part inside every ancestor panel's inner area, less every panel stacked over it — the panels
    /// are drawn on the canvas below, so a control they cover has to be cut away rather than covered.
    /// Null when the control shows whole, which is nearly always.
    /// </summary>
    private static Geometry? ClipOf(IReadOnlyList<Rect2D> occluders, Rect2D box, Rect2D shown)
    {
        bool whole = shown.X <= box.X && shown.Y <= box.Y && shown.Right >= box.Right && shown.Bottom >= box.Bottom;
        if (whole && occluders.Count == 0)
        {
            return null;
        }

        Geometry clip = new RectangleGeometry(new Rect(shown.X - box.X, shown.Y - box.Y, shown.Width, shown.Height));
        foreach (Rect2D over in occluders)
        {
            clip = new CombinedGeometry(
                GeometryCombineMode.Exclude,
                clip,
                new RectangleGeometry(new Rect(over.X - box.X, over.Y - box.Y, over.Width, over.Height)));
        }

        clip.Freeze();
        return clip;
    }

    // --- one control ---------------------------------------------------------------------------

    /// <summary>The control a frame asks for: its style, and for an edit field whether it has lines.</summary>
    private static (UiControlStyle Style, bool Multiple) KindOf(UiControlFrame control) =>
        (control.Style, control.Style == UiControlStyle.Edit && control.IsMultiple);

    private sealed class Realised(UiControlModel source, (UiControlStyle, bool) kind, FrameworkElement element, FrameworkElement control)
    {
        public UiControlModel Source { get; } = source;

        public (UiControlStyle Style, bool Multiple) Kind { get; } = kind;

        /// <summary>What sits on the canvas: the control, or a face behind it carrying its background.</summary>
        public FrameworkElement Element { get; } = element;

        /// <summary>The control itself.</summary>
        public FrameworkElement Control { get; } = control;

        /// <summary>For a label, the block inside the border; for a button, the text on its face.</summary>
        public TextBlock? Label { get; init; }

        /// <summary>For a button, the picture on its face.</summary>
        public Image? Picture { get; init; }

        /// <summary>The frame last applied.</summary>
        public UiControlFrame? Frame { get; set; }

        /// <summary>True while a frame is being applied, so the control's own events are not taken for the user's.</summary>
        public bool Applying { get; set; }

        /// <summary>An edit field's text as last committed or as last shown from a frame.</summary>
        public string Committed { get; set; } = string.Empty;

        /// <summary>The user's last action not yet seen in a frame; 0 when none is outstanding.</summary>
        public long PendingSeq { get; set; }

        /// <summary>The <c>ListboxTop</c> last scrolled to, and the focus request last answered.</summary>
        public double ListboxTop { get; set; } = double.NaN;

        public int FocusSeen { get; set; }

        /// <summary>The picture last drawn, so it is rebuilt only when it changes.</summary>
        public UiImage? Drawn { get; set; }

        /// <summary>A drop-down list's item when it opened.</summary>
        public int OpenedAt { get; set; } = -1;
    }

    private Realised Realise(UiControlFrame control)
    {
        (UiControlStyle Style, bool Multiple) kind = KindOf(control);
        switch (control.Style)
        {
            case UiControlStyle.Edit:
            {
                var box = new TextBox { Style = (Style)Styles["JG.Ui.Edit"] };
                if (kind.Multiple)
                {
                    box.AcceptsReturn = true;
                    box.TextWrapping = TextWrapping.Wrap;
                    box.VerticalContentAlignment = VerticalAlignment.Top;
                    box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                }

                var realised = new Realised(control.Source, kind, box, box);
                box.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Return && !kind.Multiple)
                    {
                        Commit(realised);
                    }
                };
                box.LostKeyboardFocus += (_, _) => Commit(realised);
                return realised;
            }

            case UiControlStyle.PushButton:
            {
                var button = new Button { Style = (Style)Styles["JG.Ui.PushButton"] };
                Realised realised = WithFace(control, kind, button, button);
                button.Click += (_, _) =>
                {
                    ScriptUiTrace.Write("layer: button clicked");
                    Send(realised, null);
                };
                return realised;
            }

            case UiControlStyle.ToggleButton:
            {
                var button = new ToggleButton { Style = (Style)Styles["JG.Ui.ToggleButton"] };
                Realised realised = WithFace(control, kind, button, button);
                button.Checked += (_, _) => Send(realised, 1.0);
                button.Unchecked += (_, _) => Send(realised, 0.0);
                return realised;
            }

            case UiControlStyle.CheckBox:
            {
                var check = new CheckBox { Style = (Style)Styles["JG.Ui.CheckBox"] };
                var face = new Border { Child = check, SnapsToDevicePixels = true };
                var realised = new Realised(control.Source, kind, face, check);
                check.Checked += (_, _) => Send(realised, 1.0);
                check.Unchecked += (_, _) => Send(realised, 0.0);
                return realised;
            }

            case UiControlStyle.RadioButton:
            {
                // A group of its own, so that WPF never deselects a neighbour: which button is on is
                // the model's to say. A radio button outside a button group goes off when pressed
                // again, as MATLAB's does.
                var radio = new RadioButton { Style = (Style)Styles["JG.Ui.RadioButton"], GroupName = Guid.NewGuid().ToString("N") };
                var face = new Border { Child = radio, SnapsToDevicePixels = true };
                var realised = new Realised(control.Source, kind, face, radio);
                radio.PreviewMouseLeftButtonDown += (_, _) => radio.Tag = radio.IsChecked == true;
                radio.PreviewKeyDown += (_, _) => radio.Tag = radio.IsChecked == true;
                radio.Checked += (_, _) => Send(realised, 1.0);
                radio.Unchecked += (_, _) => Send(realised, 0.0);

                // WPF never turns a radio button off for a press; one that stands alone goes off.
                radio.Click += (_, _) =>
                {
                    if (radio.Tag is true && radio.IsChecked == true && realised.Frame is { InGroup: false })
                    {
                        radio.IsChecked = false;
                    }

                    radio.Tag = null;
                };
                return realised;
            }

            case UiControlStyle.Slider:
            {
                var bar = new ScrollBar { Style = (Style)Styles["JG.Ui.Slider"], Orientation = Orientation.Horizontal };
                var realised = new Realised(control.Source, kind, bar, bar);
                // The callback is for a move that has ended: an arrow, a page, or the thumb let go.
                // While the thumb is held the value only follows it.
                bar.ValueChanged += (_, _) =>
                {
                    if (!realised.Applying && realised.Frame is { } frame && !ThumbHeld(bar))
                    {
                        Send(realised, SliderValue(bar, frame));
                    }
                };
                bar.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) =>
                {
                    if (!realised.Applying && realised.Frame is { } frame)
                    {
                        Send(realised, SliderValue(bar, frame));
                    }
                }));
                return realised;
            }

            case UiControlStyle.Frame:
            {
                var border = new Border { Style = (Style)Styles["JG.Ui.Frame"] };
                return new Realised(control.Source, kind, border, border);
            }

            case UiControlStyle.ListBox:
            {
                var list = new ListBox { Style = (Style)Styles["JG.Ui.ListBox"] };
                var realised = new Realised(control.Source, kind, list, list);

                // A press and release on the list is the user's choice, the same item again
                // included; the keyboard's moves arrive as changes of selection.
                list.PreviewMouseLeftButtonUp += (_, _) =>
                    Dispatcher.BeginInvoke(() => SendSelection(realised, list));
                list.SelectionChanged += (_, _) =>
                {
                    if (!realised.Applying && Mouse.LeftButton == MouseButtonState.Released)
                    {
                        SendSelection(realised, list);
                    }
                };
                return realised;
            }

            case UiControlStyle.PopupMenu:
            {
                var combo = new ComboBox { Style = (Style)Styles["JG.Ui.PopupMenu"] };
                var realised = new Realised(control.Source, kind, combo, combo);
                combo.DropDownOpened += (_, _) => realised.OpenedAt = combo.SelectedIndex;
                combo.DropDownClosed += (_, _) =>
                {
                    if (combo.SelectedIndex != realised.OpenedAt && combo.SelectedIndex >= 0)
                    {
                        Send(realised, combo.SelectedIndex + 1.0);
                    }
                };
                combo.SelectionChanged += (_, _) =>
                {
                    if (!realised.Applying && !combo.IsDropDownOpen && combo.SelectedIndex >= 0)
                    {
                        Send(realised, combo.SelectedIndex + 1.0);
                    }
                };
                return realised;
            }

            default:
            {
                // text: a label showing its String.
                var label = new TextBlock { TextWrapping = TextWrapping.Wrap };
                var border = new Border { Style = (Style)Styles["JG.Ui.Text"], Child = label };
                return new Realised(control.Source, kind, border, border) { Label = label };
            }
        }
    }

    /// <summary>A button with its face: a picture, and its text over it.</summary>
    private static Realised WithFace(UiControlFrame control, (UiControlStyle, bool) kind, ButtonBase button, FrameworkElement element)
    {
        var picture = new Image
        {
            Stretch = Stretch.None,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var label = new TextBlock { TextTrimming = TextTrimming.None, VerticalAlignment = VerticalAlignment.Center };
        var content = new Grid();
        content.Children.Add(picture);
        content.Children.Add(label);
        button.Content = content;
        return new Realised(control.Source, kind, element, button) { Label = label, Picture = picture };
    }

    /// <summary>Hands the script what the user did, and remembers that a frame has yet to show it.</summary>
    private static void Send(Realised realised, object? value)
    {
        if (realised.Applying)
        {
            return;
        }

        // Only an action that changed the control waits for a frame to show it.
        long seq = ScriptGraphicsCallbacks.NotifyUserValue(realised.Source, value);
        if (value is not null)
        {
            realised.PendingSeq = seq;
        }

        ScriptUiTrace.Write($"layer: {realised.Kind.Style} sent [{value}] as {seq}");
    }

    private static void SendSelection(Realised realised, ListBox list)
    {
        if (realised.Applying || !list.IsEnabled)
        {
            return;
        }

        // Each row is an item of its own, so two rows of the same text are still two rows.
        var picked = new List<double>();
        foreach (object item in list.SelectedItems)
        {
            picked.Add(list.Items.IndexOf(item) + 1);
        }

        picked.Sort();
        Send(realised, picked.ToArray());
    }

    /// <summary>Hands an edit field's changed text to the script, once.</summary>
    private static void Commit(Realised realised)
    {
        if (realised.Control is not TextBox box || box.Text == realised.Committed)
        {
            return;
        }

        realised.Committed = box.Text;
        realised.PendingSeq = ScriptGraphicsCallbacks.NotifyUserValue(
            realised.Source,
            realised.Kind.Multiple ? box.Text.Replace("\r\n", "\n").Split('\n') : box.Text);
        ScriptUiTrace.Write($"layer: edit committed [{box.Text}] as {realised.PendingSeq}");
    }

    private static void Update(Realised realised, UiControlFrame control)
    {
        FrameworkElement element = realised.Element;
        FrameworkElement inner = realised.Control;
        Brush? background = BrushOf(control.Background);
        Brush foreground = BrushOf(control.Foreground) ?? Brushes.Transparent;
        var family = new FontFamily(UiLayout.FontFamily(control.FontName));
        FontWeight weight = control.Bold ? FontWeights.Bold : FontWeights.Normal;
        FontStyle style = control.Italic ? FontStyles.Italic : FontStyles.Normal;
        double size = System.Math.Max(1, control.FontSize);
        inner.IsEnabled = control.Enable != UiEnable.Off;
        element.Opacity = control.Enable == UiEnable.Off ? 0.5 : 1;
        inner.Focusable = control.Enable == UiEnable.On && control.Style is not (UiControlStyle.Text or UiControlStyle.Frame);
        element.ToolTip = control.Tooltip.Length > 0 ? control.Tooltip : null;
        string text = control.Text.Joined;

        // What an accessibility client knows the control by: its Tag, and for a button its text.
        System.Windows.Automation.AutomationProperties.SetAutomationId(inner, control.Tag);
        if (inner is ButtonBase)
        {
            System.Windows.Automation.AutomationProperties.SetName(inner, text);
        }

        // What the user did stays on screen until the model has taken it; after that the model is
        // the truth, and a script that put the control back is obeyed.
        bool waiting = realised.PendingSeq > 0 && control.UserWriteSeq < realised.PendingSeq;
        if (!waiting)
        {
            realised.PendingSeq = 0;
        }

        if (inner is Control styled)
        {
            styled.Foreground = foreground;
            styled.FontFamily = family;
            styled.FontWeight = weight;
            styled.FontStyle = style;
            styled.FontSize = size;
        }

        switch (inner)
        {
            case TextBox box:
                box.Background = background ?? Brushes.Transparent;
                box.TextAlignment = TextAlignmentOf(control.Alignment);
                box.IsReadOnly = control.Enable == UiEnable.Inactive;
                if (waiting)
                {
                    break;
                }

                if (box.IsKeyboardFocusWithin && box.Text != realised.Committed)
                {
                    realised.Committed = text; // mid-edit: the user's typing wins until it is committed
                    break;
                }

                realised.Committed = text;
                if (box.Text.Replace("\r\n", "\n") != text)
                {
                    box.Text = text;
                    realised.Committed = box.Text;
                }

                break;

            case ToggleButton toggle when realised.Kind.Style == UiControlStyle.ToggleButton:
                toggle.Background = background ?? Brushes.Transparent;
                SetFace(realised, control, text);
                if (!waiting)
                {
                    toggle.IsChecked = IsOn(control);
                }

                break;

            case ToggleButton mark: // a check box or a radio button, on a face of its own
                ((Border)element).Background = background;
                mark.Content = text;
                if (!waiting)
                {
                    mark.IsChecked = IsOn(control);
                }

                break;

            case Button button:
                button.Background = background ?? Brushes.Transparent;
                button.HorizontalContentAlignment = HorizontalOf(control.Alignment);
                SetFace(realised, control, text);
                break;

            case ScrollBar bar:
                bar.Background = background ?? Brushes.Transparent;
                if (!waiting)
                {
                    SetSlider(bar, control);
                }

                break;

            case ListBox list:
                list.Background = background ?? Brushes.Transparent;
                SetItems(list, ItemsOf(control.Text));
                list.SelectionMode = control.IsMultiple ? SelectionMode.Extended : SelectionMode.Single;
                if (!waiting)
                {
                    SetSelection(list, control);
                }

                if (control.ListboxTop != realised.ListboxTop)
                {
                    realised.ListboxTop = control.ListboxTop;
                    int top = (int)control.ListboxTop - 1;
                    if (top >= 0 && top < list.Items.Count)
                    {
                        // To the end first, so the item asked for lands at the top and not the bottom.
                        list.ScrollIntoView(list.Items[^1]);
                        list.ScrollIntoView(list.Items[top]);
                    }
                }

                break;

            case ComboBox combo:
                SetItems(combo, ItemsOf(control.Text));
                if (!waiting && !combo.IsDropDownOpen)
                {
                    int index = (int)control.Scalar - 1;
                    combo.SelectedIndex = control.Scalar == System.Math.Floor(control.Scalar) && index >= 0 && index < combo.Items.Count
                        ? index
                        : -1;
                }

                break;

            case Border frame when realised.Kind.Style == UiControlStyle.Frame:
                frame.Background = background;
                frame.BorderBrush = foreground;
                break;

            case Border border when realised.Label is { } label:
                border.Background = background;
                label.Text = text;
                label.Foreground = foreground;
                label.FontFamily = family;
                label.FontWeight = weight;
                label.FontStyle = style;
                label.FontSize = size;
                label.TextAlignment = TextAlignmentOf(control.Alignment);
                break;
        }

        // uicontrol(h): the keyboard goes to the control, once per asking.
        if (control.FocusRequests > realised.FocusSeen)
        {
            realised.FocusSeen = control.FocusRequests;
            if (inner.Focusable && inner.IsEnabled)
            {
                inner.Dispatcher.BeginInvoke(() => inner.Focus());
            }
        }
    }

    /// <summary>
    /// Whether a button, a check box or a radio button shows as on: its <c>Value</c> is its
    /// <c>Max</c>, or the 1 a user's press writes whatever <c>Max</c> is (window session
    /// <c>u3w_clicks</c>: a check box with <c>Min</c> 2 and <c>Max</c> 7 reads 1 and 0 as it is pressed).
    /// </summary>
    private static bool IsOn(UiControlFrame control) => control.Scalar == control.Max || control.Scalar == 1;

    /// <summary>A button's face: its picture, when it has one, and its text.</summary>
    private static void SetFace(Realised realised, UiControlFrame control, string text)
    {
        if (realised.Label is { } label)
        {
            label.Text = text;
            label.HorizontalAlignment = WpfHorizontalAlignment.Center;
        }

        if (realised.Picture is { } picture && !ReferenceEquals(realised.Drawn, control.Image))
        {
            realised.Drawn = control.Image;
            picture.Source = control.Image is { } image
                ? BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Bgra, image.Width * 4)
                : null;
        }
    }

    /// <summary>A list's items: a character matrix pads its rows to one width, and the padding is not shown.</summary>
    private static IReadOnlyList<string> ItemsOf(UiText text) =>
        text.Form == UiTextForm.CharMatrix ? [.. text.Lines.Select(static line => line.TrimEnd(' '))] : text.Lines;

    private static void SetItems(ItemsControl list, IReadOnlyList<string> lines)
    {
        bool same = list.Items.Count == lines.Count;
        for (int i = 0; same && i < lines.Count; i++)
        {
            same = (string)((ContentControl)list.Items[i]).Content == lines[i];
        }

        if (same)
        {
            return;
        }

        list.Items.Clear();
        foreach (string line in lines)
        {
            list.Items.Add(list is ComboBox ? new ComboBoxItem { Content = line } : new ListBoxItem { Content = line });
        }
    }

    /// <summary>Selects the items a list's <c>Value</c> names; a value that names none selects none.</summary>
    private static void SetSelection(ListBox list, UiControlFrame control)
    {
        var wanted = new HashSet<int>();
        foreach (double item in control.Value?.Data ?? [])
        {
            if (item == System.Math.Floor(item) && item >= 1 && item <= list.Items.Count)
            {
                wanted.Add((int)item - 1);
            }
        }

        if (list.SelectionMode == SelectionMode.Single)
        {
            int only = wanted.Count == 0 ? -1 : wanted.Min();
            if (list.SelectedIndex != only)
            {
                list.SelectedIndex = only;
            }

            return;
        }

        var current = new HashSet<int>();
        foreach (object item in list.SelectedItems)
        {
            current.Add(list.Items.IndexOf(item));
        }

        if (current.SetEquals(wanted))
        {
            return;
        }

        list.SelectedItems.Clear();
        foreach (int index in wanted.OrderBy(static i => i))
        {
            list.SelectedItems.Add(list.Items[index]);
        }
    }

    /// <summary>
    /// A slider's range, steps and thumb. An upright one has its <c>Max</c> at the top, where WPF's
    /// scroll bar has its minimum, so its value is carried upside down.
    /// </summary>
    private static void SetSlider(ScrollBar bar, UiControlFrame control)
    {
        double span = control.Max - control.Min;
        bool usable = span > 0 && double.IsFinite(span) && control.Scalar >= control.Min && control.Scalar <= control.Max;
        if (!usable)
        {
            bar.IsEnabled = false;
            return;
        }

        bar.Minimum = control.Min;
        bar.Maximum = control.Max;
        bar.SmallChange = double.IsFinite(control.StepSmall) ? control.StepSmall * span : 0.01 * span;
        bar.LargeChange = double.IsFinite(control.StepLarge) ? System.Math.Max(control.StepLarge, 0) * span : 0.1 * span;
        bar.ViewportSize = bar.LargeChange > 0 ? bar.LargeChange : 0.1 * span;
        bar.Value = bar.Orientation == Orientation.Vertical ? control.Max + control.Min - control.Scalar : control.Scalar;
    }

    private static bool ThumbHeld(ScrollBar bar) =>
        bar.Template?.FindName("PART_Track", bar) is Track { Thumb.IsDragging: true };

    private static double SliderValue(ScrollBar bar, UiControlFrame control) =>
        bar.Orientation == Orientation.Vertical ? control.Max + control.Min - bar.Value : bar.Value;

    private static Brush? BrushOf(UiColor? color)
    {
        if (color is not { } rgb)
        {
            return null;
        }

        JGraph.Core.Drawing.Color c = rgb.ToColor();
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(c.R, c.G, c.B));
        brush.Freeze();
        return brush;
    }

    private static TextAlignment TextAlignmentOf(UiHorizontalAlignment alignment) => alignment switch
    {
        UiHorizontalAlignment.Left => TextAlignment.Left,
        UiHorizontalAlignment.Right => TextAlignment.Right,
        _ => TextAlignment.Center,
    };

    private static WpfHorizontalAlignment HorizontalOf(UiHorizontalAlignment alignment) => alignment switch
    {
        UiHorizontalAlignment.Left => WpfHorizontalAlignment.Left,
        UiHorizontalAlignment.Right => WpfHorizontalAlignment.Right,
        _ => WpfHorizontalAlignment.Center,
    };
}
