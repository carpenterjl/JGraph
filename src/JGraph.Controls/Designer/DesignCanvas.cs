using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using JGraph.Controls.Themes;
using JGraph.Core.Primitives;
using JGraph.Scripting.Designer;

namespace JGraph.Controls.Designer;

/// <summary>
/// The visual designer's canvas (designer plan DS2): an app's figure drawn by the runtime's own
/// renderer and component layer, in design mode - nothing takes a press but the canvas - with App
/// Designer's design gestures over it (<see cref="DesignGestures"/>, which this turns the mouse and
/// keys into and draws the marks of: the selection and its handles, the guides, the rubber band,
/// the container a drop goes into, the grid, and a hidden component faded). Every edit is the
/// <see cref="Document"/>'s, and undoes there.
/// </summary>
public sealed class DesignCanvas : UserControl
{
    /// <summary>The space around the figure on the canvas.</summary>
    private const double Gutter = 24;

    private static readonly Brush GuideBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xE8, 0x71, 0x0A)));
    private static readonly Brush FixedGuideBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x00, 0x76, 0xA8)));
    private static readonly Brush GridBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80)));
    private static readonly Brush BandBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x20, 0x00, 0x76, 0xA8)));

    private readonly Grid _outer;
    private readonly Grid _surface;
    private readonly FigureControl _figure;
    private readonly UiComponentLayer _layer;
    private readonly Overlay _overlay;

    private DesignDocument? _document;
    private DesignGestures? _gestures;
    private bool _showGrid;
    private bool _snapToGrid = true;
    private double _gridInterval = DesignGeometry.GridInterval;
    private bool _showAlignmentHints = true;

    public DesignCanvas()
    {
        Focusable = true;
        _figure = new FigureControl { IsHitTestVisible = false };
        _layer = new UiComponentLayer { IsHitTestVisible = false, DesignMode = true };
        KeyboardNavigation.SetTabNavigation(_layer, KeyboardNavigationMode.None);
        _surface = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new System.Windows.Thickness(Gutter),
        };
        _surface.Children.Add(_figure);
        _surface.Children.Add(_layer);
        _overlay = new Overlay(this);
        _outer = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        _outer.Children.Add(_surface);
        _outer.Children.Add(_overlay);
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _outer,
            Focusable = false,
        };
        scroll.SetResourceReference(BackgroundProperty, ThemeKeys.Panel);
        Content = scroll;
    }

    /// <summary>Raised when the selection, the design or a gesture's state changed, for the window's status and commands.</summary>
    public event EventHandler? DesignChanged;

    /// <summary>The app on the canvas.</summary>
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
                _gestures?.Cancel();
                _document.Changed -= OnChanged;
                _gestures!.Changed -= OnChanged;
            }

            _document = value;
            _gestures = null;
            _figure.Figure = value?.Runtime.Figure;
            _layer.Bind(value?.Runtime.Figure);
            if (value is not null)
            {
                _gestures = new DesignGestures(value)
                {
                    TabHeadingAt = at => _layer.TabHeadingAt(new Point(at.X, at.Y)) is { } tab ? value.Runtime.ComponentOf(tab) : null,
                    DragThreshold = System.Math.Max(SystemParameters.MinimumHorizontalDragDistance, SystemParameters.MinimumVerticalDragDistance),
                };
                ApplyOptions();
                value.Changed += OnChanged;
                _gestures.Changed += OnChanged;
                if (value.Selection.Count == 0)
                {
                    value.Select([]);
                }
            }

            Refresh();
        }
    }

    /// <summary>Whether the grid is drawn, which a move and a resize then snap to.</summary>
    public bool ShowGrid
    {
        get => _showGrid;
        set
        {
            _showGrid = value;
            ApplyOptions();
        }
    }

    /// <summary>Whether a shown grid is snapped to (App Designer's default).</summary>
    public bool SnapToGrid
    {
        get => _snapToGrid;
        set
        {
            _snapToGrid = value;
            ApplyOptions();
        }
    }

    /// <summary>The grid's interval, within App Designer's 10 to 50 pixels.</summary>
    public double GridInterval
    {
        get => _gridInterval;
        set
        {
            _gridInterval = DesignGeometry.GridIntervalWithin(value);
            ApplyOptions();
        }
    }

    /// <summary>Whether a move or a resize shows alignment guides and snaps to them.</summary>
    public bool ShowAlignmentHints
    {
        get => _showAlignmentHints;
        set
        {
            _showAlignmentHints = value;
            ApplyOptions();
        }
    }

    private void ApplyOptions()
    {
        if (_gestures is { } gestures)
        {
            gestures.ShowGrid = _showGrid;
            gestures.SnapToGrid = _snapToGrid;
            gestures.GridInterval = _gridInterval;
            gestures.ShowAlignmentHints = _showAlignmentHints;
        }

        _overlay.InvalidateVisual();
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _gestures))
        {
            _overlay.InvalidateVisual();
            return;
        }

        Refresh();
    }

    /// <summary>Draws the runtime's components as they stand, and the canvas's marks over them.</summary>
    private void Refresh()
    {
        if (_document is { } document)
        {
            Size2D size = document.Runtime.Figure.Size;
            _surface.Width = size.Width;
            _surface.Height = size.Height;
            _outer.Width = size.Width + (2 * Gutter);
            _outer.Height = size.Height + (2 * Gutter);
            _layer.Apply(document.Runtime.TakeFrame());
            _figure.RequestRender();
        }

        _overlay.InvalidateVisual();
        DesignChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Cancels a gesture under way: its components go back to where they were.</summary>
    public void CancelGesture() => _gestures?.Cancel();

    // --- input -------------------------------------------------------------------------------------

    private Point2D OnSurface(MouseEventArgs e)
    {
        Point at = e.GetPosition(_overlay);
        return new Point2D(at.X - Gutter, at.Y - Gutter);
    }

    private static DesignModifiers Modifiers() =>
        ((Keyboard.Modifiers & ModifierKeys.Control) != 0 ? DesignModifiers.Control : DesignModifiers.None)
        | ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? DesignModifiers.Shift : DesignModifiers.None)
        | ((Keyboard.Modifiers & ModifierKeys.Alt) != 0 ? DesignModifiers.Alt : DesignModifiers.None);

    private void OnPressed(MouseButtonEventArgs e)
    {
        Focus();
        DesignButton button = e.ChangedButton switch
        {
            MouseButton.Left => DesignButton.Left,
            MouseButton.Right => DesignButton.Right,
            _ => DesignButton.Middle,
        };
        if (_gestures?.Press(OnSurface(e), button, Modifiers()) == true)
        {
            _overlay.CaptureMouse();
        }
    }

    private void OnMoved(MouseEventArgs e)
    {
        if (_gestures is not { } gestures)
        {
            return;
        }

        Point2D at = OnSurface(e);
        if (gestures.IsPressed)
        {
            gestures.Drag(at, Modifiers());
            return;
        }

        _overlay.Cursor = gestures.CursorAt(at) switch
        {
            DesignCursor.Move => Cursors.SizeAll,
            DesignCursor.SizeNorthSouth => Cursors.SizeNS,
            DesignCursor.SizeWestEast => Cursors.SizeWE,
            DesignCursor.SizeNorthWestSouthEast => Cursors.SizeNWSE,
            DesignCursor.SizeNorthEastSouthWest => Cursors.SizeNESW,
            _ => Cursors.Arrow,
        };
    }

    private void OnReleased()
    {
        if (_gestures is { IsPressed: true } gestures)
        {
            gestures.Release();
            _overlay.ReleaseMouseCapture();
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        DesignKey? key = e.Key switch
        {
            Key.Left => DesignKey.Left,
            Key.Right => DesignKey.Right,
            Key.Up => DesignKey.Up,
            Key.Down => DesignKey.Down,
            Key.Escape => DesignKey.Escape,
            Key.A when (Keyboard.Modifiers & ModifierKeys.Control) != 0 => DesignKey.SelectAll,
            _ => null,
        };
        if (!e.Handled && key is { } pressed && _gestures?.KeyDown(pressed, Modifiers()) == true)
        {
            if (pressed == DesignKey.Escape)
            {
                _overlay.ReleaseMouseCapture();
            }

            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            _gestures?.ArrowUp(Keyboard.IsKeyDown(Key.Left) || Keyboard.IsKeyDown(Key.Right) || Keyboard.IsKeyDown(Key.Up) || Keyboard.IsKeyDown(Key.Down));
        }
    }

    /// <inheritdoc />
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        _gestures?.EndKeyMove();
    }

    // --- drawing ---------------------------------------------------------------------------------------

    private void Draw(DrawingContext dc)
    {
        // The overlay takes every press over the canvas, the gutter included.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, _overlay.ActualWidth, _overlay.ActualHeight));
        if (_document is not { } document || _gestures is not { } gestures)
        {
            return;
        }

        Brush accent = TryFindResource(ThemeKeys.Accent) as Brush ?? Brushes.DodgerBlue;
        var outline = new Pen(accent, 1);
        outline.Freeze();
        var dashed = new Pen(accent, 1) { DashStyle = DashStyles.Dash };
        dashed.Freeze();
        dc.PushTransform(new TranslateTransform(Gutter, Gutter));
        Size2D size = document.Runtime.Figure.Size;

        if (_showGrid)
        {
            var line = new Pen(GridBrush, 1);
            line.Freeze();
            for (double x = _gridInterval; x < size.Width; x += _gridInterval)
            {
                dc.DrawLine(line, new Point(x + 0.5, 0), new Point(x + 0.5, size.Height));
            }

            for (double y = _gridInterval; y < size.Height; y += _gridInterval)
            {
                dc.DrawLine(line, new Point(0, y + 0.5), new Point(size.Width, y + 0.5));
            }
        }

        // A hidden component is drawn faded, as App Designer's canvas draws it (35% opaque).
        JGraph.Core.Drawing.Color back = document.Runtime.Figure.Background;
        var fade = new SolidColorBrush(Color.FromArgb(0xA6, back.R, back.G, back.B));
        fade.Freeze();
        foreach (DesignBox box in document.Boxes)
        {
            Rect seen = Rect.Intersect(Wpf(box.Box), Wpf(box.Clip));
            if (box.Shown && box.Hidden && !seen.IsEmpty)
            {
                dc.DrawRectangle(fade, null, seen);
            }
        }

        if (gestures.DropTarget is { } target)
        {
            var pen = new Pen(accent, 2);
            pen.Freeze();
            dc.DrawRectangle(null, pen, Wpf(document.Runtime.InnerOnSurface(target)));
        }

        foreach (DesignComponent selected in document.Selection)
        {
            if (selected.Parent is null)
            {
                dc.DrawRectangle(null, outline, Inset(new Rect(0, 0, size.Width, size.Height)));
            }
            else if (document.BoxOf(selected) is { Shown: true } box)
            {
                dc.DrawRectangle(null, outline, Inset(Wpf(box.Box)));
            }
        }

        foreach (DesignHandle handle in gestures.Handles())
        {
            dc.DrawRectangle(Brushes.White, outline, Inset(Wpf(handle.Box)));
        }

        foreach (DesignComponent banded in gestures.Banded)
        {
            if (document.BoxOf(banded) is { } box)
            {
                dc.DrawRectangle(null, dashed, Inset(Wpf(box.Box)));
            }
        }

        if (gestures.Band is { } band)
        {
            dc.DrawRectangle(BandBrush, dashed, Inset(Wpf(band)));
        }

        foreach (DesignGuide guide in gestures.Guides)
        {
            var pen = new Pen(guide.Fixed ? FixedGuideBrush : GuideBrush, 1);
            pen.Freeze();
            if (guide.Vertical)
            {
                dc.DrawLine(pen, new Point(guide.Position + 0.5, guide.From), new Point(guide.Position + 0.5, guide.To));
            }
            else
            {
                dc.DrawLine(pen, new Point(guide.From, guide.Position + 0.5), new Point(guide.To, guide.Position + 0.5));
            }
        }

        dc.Pop();
    }

    private static Rect Wpf(Rect2D r) => new(r.X, r.Y, System.Math.Max(0, r.Width), System.Math.Max(0, r.Height));

    /// <summary>A rectangle whose one-pixel outline falls on whole pixels.</summary>
    private static Rect Inset(Rect r) => new(r.X + 0.5, r.Y + 0.5, System.Math.Max(0, r.Width - 1), System.Math.Max(0, r.Height - 1));

    private static Brush Frozen(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    /// <summary>The element over the figure that takes the canvas's presses and draws its marks.</summary>
    private sealed class Overlay(DesignCanvas owner) : FrameworkElement
    {
        protected override void OnRender(DrawingContext drawingContext) => owner.Draw(drawingContext);

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            owner.OnPressed(e);
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            owner.OnMoved(e);
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            owner.OnReleased();
            e.Handled = true;
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            if (owner._gestures is { IsPressed: true } gestures)
            {
                gestures.Cancel();
            }
        }
    }
}
