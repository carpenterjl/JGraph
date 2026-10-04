using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Rendering.Layout;
using JGraph.Scripting;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;

namespace JGraph.Controls;

/// <summary>
/// The WPF realisation of a figure's components (app-building plan, section A, stage U1). It lies
/// over the figure canvas, takes no input outside its controls, and is fed only frames: it never
/// reads the model, so a script writing the model on its own thread cannot tear what it shows. A
/// user's action goes back through <see cref="ScriptGraphicsCallbacks.NotifyUserValue"/>, and the
/// control keeps showing what the user did until a frame arrives that has taken it.
/// </summary>
public sealed class UiComponentLayer : Canvas
{
    private static readonly ResourceDictionary Styles = new()
    {
        Source = new Uri("pack://application:,,,/JGraph.Controls;component/Themes/UiComponents.xaml"),
    };

    private readonly Dictionary<UiControlModel, Realised> _controls = new(ReferenceEqualityComparer.Instance);
    private FigureModel? _figure;
    private UiFrame? _frame;

    public UiComponentLayer()
    {
        ClipToBounds = true;
        Background = null; // the canvas below takes every click that misses a control
        SizeChanged += (_, _) => Arrange();
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
            if (!_controls.TryGetValue(control.Source, out Realised? realised) || realised.Style != control.Style)
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
            Update(realised, control);
        }

        foreach (UiControlModel gone in _controls.Keys.Where(model => !present.Contains(model)).ToList())
        {
            Children.Remove(_controls[gone].Element);
            _controls.Remove(gone);
        }

        Arrange();
    }

    /// <summary>Commits the focused edit field, if its text changed — what a click elsewhere does first.</summary>
    public void CommitFocusedEdit()
    {
        foreach (Realised realised in _controls.Values)
        {
            if (realised.Element is TextBox { IsKeyboardFocusWithin: true })
            {
                Commit(realised);
            }
        }
    }

    private void Arrange()
    {
        if (_frame is null)
        {
            return;
        }

        foreach (UiPlacement placement in UiLayout.Place(_frame, new Size2D(ActualWidth, ActualHeight)))
        {
            if (!_controls.TryGetValue(placement.Control.Source, out Realised? realised))
            {
                continue;
            }

            FrameworkElement element = realised.Element;
            SetLeft(element, placement.Box.X);
            SetTop(element, placement.Box.Y);
            element.Width = placement.Box.Width;
            element.Height = placement.Box.Height;
            element.Visibility = placement.Visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // --- one control ---------------------------------------------------------------------------

    private sealed class Realised(UiControlModel source, UiControlStyle style, FrameworkElement element)
    {
        public UiControlModel Source { get; } = source;

        public UiControlStyle Style { get; } = style;

        public FrameworkElement Element { get; } = element;

        /// <summary>For a text block, the block inside the border.</summary>
        public TextBlock? Label { get; init; }

        /// <summary>An edit field's text as last committed or as last shown from a frame.</summary>
        public string Committed { get; set; } = string.Empty;

        /// <summary>The user's last commit not yet seen in a frame; 0 when none is outstanding.</summary>
        public long PendingSeq { get; set; }
    }

    private Realised Realise(UiControlFrame control)
    {
        switch (control.Style)
        {
            case UiControlStyle.Edit:
            {
                var box = new TextBox { Style = (Style)Styles["JG.Ui.Edit"] };
                var realised = new Realised(control.Source, control.Style, box);
                box.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Return)
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
                var realised = new Realised(control.Source, control.Style, button);
                button.Click += (_, _) =>
                {
                    ScriptUiTrace.Write("layer: button clicked");
                    ScriptGraphicsCallbacks.NotifyUserValue(realised.Source, null);
                };
                return realised;
            }

            default:
            {
                // text, and for now every style U3 has still to realise: a label showing its String.
                var label = new TextBlock { TextWrapping = TextWrapping.Wrap };
                var border = new Border { Style = (Style)Styles["JG.Ui.Text"], Child = label };
                return new Realised(control.Source, control.Style, border) { Label = label };
            }
        }
    }

    /// <summary>Hands an edit field's changed text to the script, once.</summary>
    private static void Commit(Realised realised)
    {
        if (realised.Element is not TextBox box || box.Text == realised.Committed)
        {
            return;
        }

        realised.Committed = box.Text;
        realised.PendingSeq = ScriptGraphicsCallbacks.NotifyUserValue(realised.Source, box.Text);
        ScriptUiTrace.Write($"layer: edit committed [{box.Text}] as {realised.PendingSeq}");
    }

    private static void Update(Realised realised, UiControlFrame control)
    {
        FrameworkElement element = realised.Element;
        Brush? background = BrushOf(control.Background);
        Brush foreground = BrushOf(control.Foreground) ?? Brushes.Transparent;
        var family = new FontFamily(UiLayout.FontFamily(control.FontName));
        FontWeight weight = control.Bold ? FontWeights.Bold : FontWeights.Normal;
        FontStyle style = control.Italic ? FontStyles.Italic : FontStyles.Normal;
        double size = System.Math.Max(1, control.FontSize);
        element.IsEnabled = control.Enable != UiEnable.Off;
        element.IsHitTestVisible = control.Enable != UiEnable.Inactive;
        element.ToolTip = control.Tooltip.Length > 0 ? control.Tooltip : null;
        string text = control.Text.Joined;

        switch (element)
        {
            case TextBox box:
                box.Background = background ?? Brushes.Transparent;
                box.Foreground = foreground;
                box.FontFamily = family;
                box.FontWeight = weight;
                box.FontStyle = style;
                box.FontSize = size;
                box.TextAlignment = TextAlignmentOf(control.Alignment);
                box.Focusable = control.Enable == UiEnable.On;

                // What the user typed stays on screen until the model has taken it; after that the
                // model's text is the truth, and a script that cleared the field is obeyed.
                if (realised.PendingSeq > 0 && control.UserWriteSeq < realised.PendingSeq)
                {
                    break;
                }

                realised.PendingSeq = 0;
                if (box.IsKeyboardFocusWithin && box.Text != realised.Committed)
                {
                    realised.Committed = text; // mid-edit: the user's typing wins until it is committed
                    break;
                }

                realised.Committed = text;
                if (box.Text != text)
                {
                    box.Text = text;
                }

                break;

            case Button button:
                button.Background = background ?? Brushes.Transparent;
                button.Foreground = foreground;
                button.FontFamily = family;
                button.FontWeight = weight;
                button.FontStyle = style;
                button.FontSize = size;
                button.HorizontalContentAlignment = HorizontalOf(control.Alignment);
                button.Content = new TextBlock { Text = text, TextTrimming = TextTrimming.None };
                button.Focusable = control.Enable == UiEnable.On;
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
    }

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
