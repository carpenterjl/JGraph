using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using WpfWindowState = System.Windows.WindowState;

namespace JGraph.Application.Services;

/// <summary>
/// Ties a figure's window properties to the window it is shown in. Position, WindowState, Resize,
/// Pointer, ToolBar, NumberTitle and Name are things about a window rather than about a drawing, so
/// they are worth nothing until something really moves, resizes and renames one — that is what this
/// class is for.
/// </summary>
/// <remarks>
/// The traffic runs both ways: a script that sets <c>WindowState</c> maximizes the window, and a
/// person who maximizes the window sets <c>WindowState</c>. Both directions pass through
/// <see cref="_echo"/>, because a change applied to the window comes straight back as a window event
/// and would otherwise be written to the model a second time — harmless here, but the loop is real
/// and worth closing at the one place both directions cross.
/// </remarks>
internal sealed class FigureWindowBinding
{
    private readonly Window _window;
    private readonly Controls.FigureControl _view;
    private FigureModel? _figure;
    private bool _echo;
    private bool _placed;
    private bool _placementPending;
    private (Point2D Position, Size2D Size)? _wanted;

    internal FigureWindowBinding(Window window, Controls.FigureControl view)
    {
        _window = window;
        _view = view;
        _window.StateChanged += OnWindowStateChanged;
        _window.LocationChanged += OnWindowMoved;
        _window.LocationChanged += (_, _) => NoteOuterBounds();
        _window.SizeChanged += (_, _) => NoteOuterBounds();
        _window.Loaded += (_, _) => NoteOuterBounds();
    }

    /// <summary>
    /// The window's bounds as last seen on this thread, boxed so that the script thread reads one
    /// reference: OuterPosition is asked for from there, and a window may only be touched from here.
    /// </summary>
    private object? _outerBounds;

    private void NoteOuterBounds()
    {
        if (!_window.IsLoaded)
        {
            return;
        }

        double screen = SystemParameters.PrimaryScreenHeight;
        Volatile.Write(ref _outerBounds, new Rect2D(
            _window.Left, screen - _window.Top - _window.ActualHeight,
            _window.ActualWidth, _window.ActualHeight));
    }

    /// <summary>Binds to a figure, releasing whatever was bound before, and applies it at once.</summary>
    internal void Bind(FigureModel? figure)
    {
        if (ReferenceEquals(_figure, figure))
        {
            return;
        }

        if (_figure is not null)
        {
            _figure.PropertyChanged -= OnFigureChanged;
        }

        _figure = figure;
        _placed = false;
        _wanted = null;
        if (_figure is not null)
        {
            _figure.PropertyChanged += OnFigureChanged;

            // Captured here and nowhere later. This runs before the window is shown, which is the
            // only moment the figure still holds the size a script asked for: the control writes
            // the viewport's own size back into it on the first arrange, and from then on reading
            // Size gives the size the window already is.
            if (_figure.PositionSpecified)
            {
                _wanted = (_figure.Position, _figure.RequestedSize ?? _figure.Size);
            }

            ApplyAll();
        }
    }

    /// <summary>The window's own bounds on screen, chrome included, in MATLAB's upward-Y pixels.</summary>
    internal Rect2D? OuterBounds(FigureModel figure)
    {
        // Asked from the script thread: answered from what this thread last saw, never the window.
        return ReferenceEquals(figure, _figure) && Volatile.Read(ref _outerBounds) is Rect2D seen ? seen : null;
    }

    private void OnFigureChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_echo || _figure is null)
        {
            return;
        }

        _window.Dispatcher.BeginInvoke(() =>
        {
            switch (e.PropertyName)
            {
                // A placement, and not Size. The figure's size is written back by the control every
                // time the window lays out, so treating that as a request would mean the window
                // asking itself to be the size it already is. A script's placement is announced as
                // a request, which carries the size it asked for (U2); a Position written straight
                // to the model, by something that made no request, is still acted on.
                case nameof(FigureModel.PlacementRequest):
                    _wanted = null;
                    ApplyPlacement();
                    break;
                case nameof(FigureModel.Position) when _figure?.RequestedSize is null:
                    _wanted = null;
                    ApplyPlacement();
                    break;
                case nameof(FigureModel.WindowState):
                    ApplyWindowState();
                    break;
                case nameof(FigureModel.Resizable):
                    ApplyResizable();
                    break;
                case nameof(FigureModel.Pointer):
                    ApplyPointer();
                    break;
                case nameof(FigureModel.ToolBar):
                    ApplyToolBar();
                    break;
                case nameof(FigureModel.MenuBar):
                    ApplyToolBar();
                    PlainnessChanged?.Invoke();
                    break;
                case nameof(FigureModel.WindowStyle):
                    WindowStyleChanged?.Invoke();
                    break;
                case nameof(FigureModel.NumberTitle):
                case nameof(FigureModel.Name):
                    TitleChanged?.Invoke();
                    break;
                case nameof(FigureModel.Visible):
                    FigureVisibilityChanged?.Invoke(_figure.Visible);
                    break;
                default:
                    break;
            }
        });
    }

    /// <summary>Raised when the window's title needs rebuilding — the service owns the number.</summary>
    internal event Action? TitleChanged;

    /// <summary>Raised when a script shows or hides the figure (its <c>Visible</c>, U1).</summary>
    internal event Action<bool>? FigureVisibilityChanged;

    /// <summary>Applies everything again now the window is loaded and can be measured.</summary>
    internal void OnWindowLoaded() => ApplyAll();

    private void ApplyAll()
    {
        ApplyPlacement();
        ApplyWindowState();
        ApplyResizable();
        ApplyPointer();
        ApplyToolBar();
    }

    /// <summary>
    /// Moves and sizes the window so the drawable area comes to the figure's own size. The chrome
    /// is measured rather than assumed: this window carries a toolbar, a status bar and two panels
    /// that a MATLAB figure has not got, so the difference is whatever it happens to be right now.
    /// </summary>
    private void ApplyPlacement()
    {
        if (_figure is not { PositionSpecified: true } figure || !_window.IsLoaded)
        {
            return;
        }

        // What was asked for, captured the moment it was asked. Between the request and the first
        // arrange the control writes the viewport's own size back into the figure, so reading the
        // size again later would place the window at the size it already had.
        _wanted ??= (figure.Position, figure.RequestedSize ?? figure.Size);
        (Point2D position, Size2D size) = _wanted.Value;

        double chromeWidth = _window.ActualWidth - _view.ActualWidth;
        double chromeHeight = _window.ActualHeight - _view.ActualHeight;
        if (chromeWidth <= 0 || chromeHeight <= 0)
        {
            // The window has been loaded but not yet arranged, so there is nothing to measure the
            // chrome against. Ask again once there is, rather than place the window by a guess.
            if (!_placementPending)
            {
                _placementPending = true;
                _window.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Loaded,
                    () =>
                    {
                        _placementPending = false;
                        ApplyPlacement();
                    });
            }

            return;
        }

        _echo = true;
        try
        {
            _window.Width = size.Width + chromeWidth;
            _window.Height = size.Height + chromeHeight;

            // MATLAB counts up from the bottom of the screen; WPF counts down from the top.
            _window.Left = position.X;
            _window.Top = SystemParameters.PrimaryScreenHeight - position.Y - _window.Height;
            _placed = true;
        }
        finally
        {
            _echo = false;
        }
    }

    private void ApplyWindowState()
    {
        if (_figure is not { } figure)
        {
            return;
        }

        _echo = true;
        try
        {
            switch (figure.WindowState)
            {
                case FigureWindowState.Minimized:
                    _window.WindowStyle = WindowStyle.SingleBorderWindow;
                    _window.WindowState = WpfWindowState.Minimized;
                    break;

                case FigureWindowState.Maximized:
                    _window.WindowStyle = WindowStyle.SingleBorderWindow;
                    _window.WindowState = WpfWindowState.Maximized;
                    break;

                // Fullscreen is a maximized window with its border taken away, which is the only
                // thing distinguishing the two on a desktop.
                case FigureWindowState.Fullscreen:
                    _window.WindowStyle = WindowStyle.None;
                    _window.WindowState = WpfWindowState.Maximized;
                    break;

                default:
                    _window.WindowStyle = WindowStyle.SingleBorderWindow;
                    _window.WindowState = WpfWindowState.Normal;
                    break;
            }
        }
        finally
        {
            _echo = false;
        }
    }

    private void ApplyResizable() =>
        _window.ResizeMode = _figure is { Resizable: false }
            ? ResizeMode.NoResize
            : ResizeMode.CanResize;

    private void ApplyPointer() =>
        _view.Cursor = _figure is { } figure ? CursorFor(figure.Pointer) : Cursors.Arrow;

    private void ApplyToolBar() => ToolBarVisibilityChanged?.Invoke(_figure is null or { ShowsToolBar: true });

    /// <summary>Raised when the figure's <c>MenuBar</c> changed, which decides the window's furniture.</summary>
    internal event Action? PlainnessChanged;

    /// <summary>Raised when the figure's <c>WindowStyle</c> changed.</summary>
    internal event Action? WindowStyleChanged;

    /// <summary>Raised with whether the toolbar should be shown; the window owns the element.</summary>
    internal event Action<bool>? ToolBarVisibilityChanged;

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (_echo || _figure is not { } figure)
        {
            return;
        }

        _echo = true;
        try
        {
            figure.WindowState = _window.WindowState switch
            {
                WpfWindowState.Minimized => FigureWindowState.Minimized,
                WpfWindowState.Maximized => _window.WindowStyle == WindowStyle.None
                    ? FigureWindowState.Fullscreen
                    : FigureWindowState.Maximized,
                _ => FigureWindowState.Normal,
            };
        }
        finally
        {
            _echo = false;
        }
    }

    private void OnWindowMoved(object? sender, EventArgs e)
    {
        // Only a figure a script already placed follows the window: writing Position is what says
        // a figure has been placed at all, and a person dragging an unplaced window must not be
        // what makes it so.
        if (_echo || !_placed || _figure is not { PositionSpecified: true } figure)
        {
            return;
        }

        _echo = true;
        try
        {
            figure.Position = new Point2D(
                _window.Left, SystemParameters.PrimaryScreenHeight - _window.Top - _window.ActualHeight);
        }
        finally
        {
            _echo = false;
        }
    }

    /// <summary>MATLAB's pointer words as the nearest cursor this toolkit has.</summary>
    private static Cursor CursorFor(PointerShape shape) => shape switch
    {
        PointerShape.Ibeam => Cursors.IBeam,
        PointerShape.Crosshair or PointerShape.Cross or PointerShape.Circle => Cursors.Cross,
        PointerShape.Watch => Cursors.Wait,
        PointerShape.Fleur => Cursors.SizeAll,
        PointerShape.Hand => Cursors.Hand,
        PointerShape.Left or PointerShape.Right => Cursors.SizeWE,
        PointerShape.Top or PointerShape.Bottom => Cursors.SizeNS,
        PointerShape.TopL or PointerShape.BotR => Cursors.SizeNWSE,
        PointerShape.TopR or PointerShape.BotL => Cursors.SizeNESW,
        _ => Cursors.Arrow,
    };
}
