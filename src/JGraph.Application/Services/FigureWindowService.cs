using JGraph.Core.Model;
using Microsoft.Extensions.DependencyInjection;

namespace JGraph.Application.Services;

/// <summary>
/// The WPF implementation of <see cref="IFigureWindowService"/>: a number-keyed registry of
/// DI-minted <see cref="FigureWindow"/>/<see cref="Mvvm.FigureViewModel"/> pairs (both transient).
/// The script figure replaces the view model's sample figure before the window first shows, so
/// there is no flash; closing a window just evicts it — the next show of that number recreates it.
/// </summary>
public sealed class FigureWindowService : IFigureWindowService
{
    private readonly IServiceProvider _services;
    private readonly Dictionary<int, FigureWindow> _windows = new();

    /// <summary>Creates the service over the DI container that mints figure windows.</summary>
    public FigureWindowService(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));

        // A figure's OuterPosition is a question only the window it is in can answer, and this is
        // the one object that knows which window that is.
        JGraph.Scripting.ScriptGraphicsCallbacks.WindowBoundsProvider = OuterBoundsOf;

        // The system's file, folder, colour and font dialogs, for uigetfile and its kin (U4).
        JGraph.Scripting.ScriptGraphicsCallbacks.NativeDialogs =
            new AppScriptNativeDialogs(System.Windows.Threading.Dispatcher.CurrentDispatcher);

        // Frames of a script's components (app-building plan, U1) arrive on the script thread; they
        // are applied here, on this one, to the window showing their figure, and then acknowledged
        // so the next one can be sent. A figure with no window yet takes its last frame on opening.
        System.Windows.Threading.Dispatcher ui = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        JGraph.Scripting.ScriptComponentFrames.SetSink(frame => ui.BeginInvoke(() =>
        {
            try
            {
                ApplyComponentFrame(frame);
            }
            finally
            {
                JGraph.Scripting.ScriptComponentFrames.Applied(frame);
            }
        }));

        // A uihtml in a shown figure gets its page from the window (U9b), not a hidden one first.
        JGraph.Scripting.UiHtmlPages.WindowsHostPages = true;
    }

    private void ApplyComponentFrame(UiFrame frame)
    {
        foreach (FigureWindow window in _windows.Values)
        {
            if (window.Shows(frame.Figure))
            {
                window.ApplyComponentFrame(frame);
            }
        }
    }

    private JGraph.Core.Primitives.Rect2D? OuterBoundsOf(FigureModel figure)
    {
        foreach (FigureWindow window in _windows.Values)
        {
            if (window.OuterBoundsOf(figure) is { } bounds)
            {
                return bounds;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public void ShowScriptFigure(int number, FigureModel figure)
    {
        ArgumentNullException.ThrowIfNull(figure);
        string status = JGraph.Api.JG.IsHiddenNumber(number) ? "App — from script" : $"Figure {number} — from script";

        if (_windows.TryGetValue(number, out FigureWindow? window))
        {
            window.FigureNumber = number;
            window.ViewModel.DisplayFigure(figure, status);
            window.RebindFigure();
            if (window.WindowState == System.Windows.WindowState.Minimized)
            {
                window.WindowState = System.Windows.WindowState.Normal;
            }

            window.Show(); // No-op when already visible; deliberately does not steal focus.
            ApplyWindowStyles();
            return;
        }

        window = _services.GetRequiredService<FigureWindow>();
        window.FigureNumber = number;
        window.ViewModel.DisplayFigure(figure, status);
        window.RebindFigure();

        // A figure's OuterPosition is a question only the window it is in can answer, so the window
        // answers it: the property surface asks through here rather than guessing at the chrome.

        // Closing the window retires the figure itself, so the engine stops handing scripts a model
        // nothing can display — the next figure(n) builds a new one and opens a new window.
        window.WindowStyleChanged += ApplyWindowStyles;
        window.IsVisibleChanged += (_, _) => ApplyWindowStyles();
        window.Closed += (_, _) =>
        {
            _windows.Remove(number);
            ApplyWindowStyles();
            JGraph.Api.JG.CloseFigure(number);

            // Retiring the figure is what makes its script-side handles unreachable; this is what
            // lets go of them. Without it the registry keeps the figure, and through it the whole
            // model subtree and the window that drew it, for the rest of the session — one graph per
            // open-and-close, which is the ordinary way a plotting session is used.
            try
            {
                JGraph.Scripting.ScriptGraphicsCallbacks.ReleaseRetiredFigures();
            }
            catch (InvalidOperationException)
            {
                // The sweep walks the live model, and a script on its own thread may be adding to it
                // as this runs. Missing one sweep costs a delayed release; throwing out of a window's
                // Closed handler would cost the user a dialog.
            }
        };
        _windows[number] = window;
        window.Show();
        ApplyWindowStyles();
    }

    /// <summary>
    /// Applies each figure's <c>WindowStyle</c> (U4). A modal figure stays in front and keeps the
    /// other figure windows from being used while it is up; an always-on-top one stays in front
    /// and blocks nothing. The application's own window is left usable, so a script blocked in a
    /// dialog can still be stopped.
    /// </summary>
    private void ApplyWindowStyles()
    {
        static bool Modal(FigureWindow window) =>
            window.IsVisible && window.ShownFigure is { WindowStyle: FigureWindowStyle.Modal };

        bool anyModal = _windows.Values.Any(Modal);
        foreach (FigureWindow window in _windows.Values)
        {
            bool modal = Modal(window);
            if (modal && !window.Topmost)
            {
                window.Activate(); // a dialog is for answering: it takes the keyboard as it appears
            }

            window.IsEnabled = !anyModal || modal;
            bool front = modal || (window.IsVisible && window.ShownFigure is { WindowStyle: FigureWindowStyle.AlwaysOnTop });
            if (window.Topmost != front)
            {
                window.Topmost = front;
            }
        }
    }

    /// <summary>The window showing a figure, or null. UI thread.</summary>
    internal FigureWindow? WindowOf(FigureModel figure) =>
        _windows.Values.FirstOrDefault(window => window.Shows(figure));

    /// <inheritdoc />
    public int OpenBlankFigure()
    {
        // Through JG, so the new figure joins the same numbering scripts and the console use and
        // becomes the current figure — an immediate `plot(...)` then lands in the window just opened.
        FigureModel figure = JGraph.Api.JG.Figure();
        int number = JGraph.Api.JG.CurrentFigureNumber;
        ShowScriptFigure(number, figure);
        return number;
    }

    /// <inheritdoc />
    public void CloseScriptFigure(int number)
    {
        if (_windows.TryGetValue(number, out FigureWindow? window))
        {
            // A close reaching here was already decided script-side (closereq, delete, close after
            // its CloseRequestFcn ran) — the window must not ask the callback a second time.
            window.CloseApproved = true;
            window.Close(); // The Closed handler evicts it from the map.
        }
    }

    /// <inheritdoc />
    public void CloseAll()
    {
        foreach (FigureWindow window in _windows.Values.ToArray())
        {
            window.CloseApproved = true;
            window.Close(); // The Closed handler evicts it from the map.
        }
    }
}
