using System.Windows;
using System.Windows.Input;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Core.Undo;
using JGraph.Interaction;
using JGraph.Rendering;
using JGraph.Rendering.Skia;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace JGraph.Controls;

/// <summary>
/// A WPF control that renders a <see cref="FigureModel"/> with the SkiaSharp backend and hosts the
/// interaction system (mouse-wheel zoom, drag pan, rubber-band zoom, data cursor, and undo/redo
/// navigation). It repaints only when the model invalidates or an interaction changes the view, never
/// on a timer, and scales the canvas for the display DPI so output stays crisp.
/// </summary>
public class FigureControl : SKElement, IInteractionSurface, IFigureNavigator
{
    /// <summary>Identifies the <see cref="Figure"/> dependency property.</summary>
    public static readonly DependencyProperty FigureProperty = DependencyProperty.Register(
        nameof(Figure),
        typeof(FigureModel),
        typeof(FigureControl),
        new FrameworkPropertyMetadata(null, OnFigureChanged));

    private readonly FigureRenderer _renderer = new();
    private readonly InteractionController _controller;
    private Point2D? _rightDown;
    private ITheme _theme = Core.Drawing.Theme.Light;
    private FigureRenderResult _lastResult = FigureRenderResult.Empty;
    private bool _isRendering;

    public FigureControl()
    {
        IgnorePixelScaling = true;
        Focusable = true;
        UndoStack = new UndoStack();
        _controller = new InteractionController(this);
        _controller.StateChanged += (_, _) => UpdateCursor();
        UndoStack.StateChanged += (_, _) => NavigationStateChanged?.Invoke(this, EventArgs.Empty);
        UpdateCursor();
    }

    /// <summary>Raised as the pointer moves, with the data-space position under it (null when outside any axes).</summary>
    public event EventHandler<Point2D?>? CursorDataPositionChanged;

    /// <summary>Raised when undo/redo availability changes.</summary>
    public event EventHandler? NavigationStateChanged;

    /// <summary>The figure to render.</summary>
    public FigureModel? Figure
    {
        get => (FigureModel?)GetValue(FigureProperty);
        set => SetValue(FigureProperty, value);
    }

    /// <summary>The theme used for chrome colors and the series palette.</summary>
    public ITheme Theme
    {
        get => _theme;
        set
        {
            _theme = value ?? Core.Drawing.Theme.Light;
            InvalidateVisual();
        }
    }

    /// <summary>The active interaction mode.</summary>
    public InteractionModeKind ActiveMode
    {
        get => _controller.CurrentMode.Kind;
        set => _controller.SetMode(value);
    }

    /// <inheritdoc />
    public UndoStack UndoStack { get; }

    /// <summary>The shared selection used by the edit mode, plot browser, and property inspector.</summary>
    public SelectionManager Selection => _controller.Selection;

    /// <inheritdoc />
    public AxesModel? DefaultAxes => Figure is { Axes.Count: > 0 } figure ? figure.Axes[0] : null;

    /// <inheritdoc />
    public ICoordinateMapper? FigureMapper => _lastResult.FigureMapper;

    public bool CanUndo => UndoStack.CanUndo;

    public bool CanRedo => UndoStack.CanRedo;

    /// <inheritdoc />
    public Size2D ViewportSize => ActualWidth > 0 && ActualHeight > 0
        ? new Size2D(ActualWidth, ActualHeight)
        : Figure?.Size ?? new Size2D(640, 480);

    public void Undo() => _controller.Undo();

    public void Redo() => _controller.Redo();

    public void ResetView() => _controller.ResetView();

    /// <summary>
    /// Copies the figure to the clipboard as an image at the current viewport size and theme.
    /// Returns false when there is no figure or the clipboard was unavailable.
    /// </summary>
    public bool CopyToClipboard(double scale = 2.0)
    {
        if (Figure is not { } figure)
        {
            return false;
        }

        return FigureClipboard.CopyImage(figure, new Export.ExportOptions
        {
            Size = ViewportSize,
            Scale = scale,
            Theme = _theme,
        });
    }

    /// <inheritdoc />
    public bool TryGetAxesAt(Point2D pixel, out AxesModel axes, out ICoordinateMapper mapper, out Rect2D plotArea)
    {
        AxesRenderInfo? info = _lastResult.HitTest(pixel);
        if (info is not null)
        {
            axes = info.Axes;
            mapper = info.Transform;
            plotArea = info.PlotArea;
            return true;
        }

        axes = null!;
        mapper = null!;
        plotArea = Rect2D.Empty;
        return false;
    }

    /// <inheritdoc />
    public ICoordinateMapper? GetMapper(AxesModel axes)
    {
        foreach (AxesRenderInfo info in _lastResult.Axes)
        {
            if (ReferenceEquals(info.Axes, axes))
            {
                return info.Transform;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public void RequestRender() => InvalidateVisual();

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);

        FigureModel? figure = Figure;
        if (figure is null)
        {
            e.Surface.Canvas.Clear();
            return;
        }

        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        double dpiScale = e.Info.Width / width;
        var canvas = e.Surface.Canvas;
        canvas.Save();
        canvas.Scale((float)dpiScale);

        _isRendering = true;
        try
        {
            using var context = new SkiaRenderContext(canvas, new Size2D(width, height), dpiScale);
            _lastResult = _renderer.Render(figure, context, _theme);
            OverlayRenderer.Draw(context, _controller, _theme);
        }
        finally
        {
            _isRendering = false;
            canvas.Restore();
        }
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        CaptureMouse();
        if (e.ChangedButton == MouseButton.Right)
        {
            _rightDown = ToPoint(e);
        }

        _controller.PointerDown(ToPointerArgs(e, ButtonOf(e)));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Point2D position = ToPoint(e);
        _controller.PointerMove(ToPointerArgs(e, PointerButton.None, position));

        Point2D? data = TryGetAxesAt(position, out _, out ICoordinateMapper mapper, out _)
            ? mapper.PixelToData(position.X, position.Y)
            : null;
        CursorDataPositionChanged?.Invoke(this, data);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        _controller.PointerUp(ToPointerArgs(e, ButtonOf(e)));
        ReleaseMouseCapture();

        // A right click (not a drag) opens the tool-aware plot context menu.
        if (e.ChangedButton == MouseButton.Right && _rightDown is { } down)
        {
            _rightDown = null;
            Point2D position = ToPoint(e);
            if (System.Math.Abs(position.X - down.X) < 4 && System.Math.Abs(position.Y - down.Y) < 4)
            {
                OpenContextMenu(position);
                e.Handled = true;
            }
        }
    }

    private void OpenContextMenu(Point2D position)
    {
        IReadOnlyList<ContextMenuItem> items = _controller.BuildContextMenu(position);
        if (items.Count == 0)
        {
            return;
        }

        var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = this };
        foreach (ContextMenuItem item in items)
        {
            if (item.IsSeparator)
            {
                menu.Items.Add(new System.Windows.Controls.Separator());
                continue;
            }

            var entry = new System.Windows.Controls.MenuItem
            {
                Header = item.Header,
                IsChecked = item.IsChecked,
            };
            if (item.Invoke is { } invoke)
            {
                entry.Click += (_, _) => invoke();
            }

            menu.Items.Add(entry);
        }

        menu.IsOpen = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        _controller.Wheel(new WheelEventArgs(ToPoint(e), e.Delta, CurrentModifiers()));
        e.Handled = true;
    }

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            _controller.KeyDown(new Interaction.KeyEventArgs(InteractionKey.Escape, CurrentModifiers()));
        }
        else if (e.Key == Key.Delete)
        {
            _controller.KeyDown(new Interaction.KeyEventArgs(InteractionKey.Delete, CurrentModifiers()));
        }
        else if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control))
        {
            Undo();
            e.Handled = true;
        }
        else if (e.Key == Key.Y && Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control))
        {
            Redo();
            e.Handled = true;
        }
        else if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control))
        {
            CopyToClipboard();
            e.Handled = true;
        }
    }

    private static PointerButton ButtonOf(MouseButtonEventArgs e) => e.ChangedButton switch
    {
        MouseButton.Left => PointerButton.Left,
        MouseButton.Middle => PointerButton.Middle,
        MouseButton.Right => PointerButton.Right,
        _ => PointerButton.None,
    };

    private static Interaction.ModifierKeys CurrentModifiers()
    {
        Interaction.ModifierKeys mods = Interaction.ModifierKeys.None;
        if (Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift))
        {
            mods |= Interaction.ModifierKeys.Shift;
        }

        if (Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control))
        {
            mods |= Interaction.ModifierKeys.Control;
        }

        if (Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt))
        {
            mods |= Interaction.ModifierKeys.Alt;
        }

        return mods;
    }

    private PointerEventArgs ToPointerArgs(MouseEventArgs e, PointerButton button) =>
        new(ToPoint(e), button, CurrentModifiers());

    private PointerEventArgs ToPointerArgs(MouseEventArgs e, PointerButton button, Point2D position) =>
        new(position, button, CurrentModifiers());

    private Point2D ToPoint(MouseEventArgs e)
    {
        System.Windows.Point p = e.GetPosition(this);
        return new Point2D(p.X, p.Y);
    }

    private void UpdateCursor() => Cursor = _controller.Cursor switch
    {
        InteractionCursor.Hand => Cursors.Hand,
        InteractionCursor.Cross => Cursors.Cross,
        InteractionCursor.SizeAll => Cursors.SizeAll,
        _ => Cursors.Arrow,
    };

    private static void OnFigureChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (FigureControl)d;
        if (e.OldValue is FigureModel oldFigure)
        {
            oldFigure.Invalidated -= control.OnFigureInvalidated;
        }

        if (e.NewValue is FigureModel newFigure)
        {
            newFigure.Invalidated += control.OnFigureInvalidated;
        }

        control.UndoStack.Clear();
        control.InvalidateVisual();
    }

    private void OnFigureInvalidated(object? sender, InvalidatedEventArgs e)
    {
        // Auto-scaling during a paint raises invalidations; ignore them because the in-progress paint
        // already reflects the updated ranges.
        if (_isRendering)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(InvalidateVisual);
        }
        else
        {
            InvalidateVisual();
        }
    }
}
