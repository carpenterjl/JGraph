using System.Windows;
using JGraph.Runtime.Desktop.Services;
using JGraph.Scripting;
using JGraph.Scripting.Startup;

namespace JGraph.Runtime.Desktop;

/// <summary>
/// Runs a script whose figures are shown in windows and then keeps the program alive until the last
/// window closes: <c>-batch -showfigures</c> in JGraph's application, and the windowed programs of
/// the compiler and SDK plan's later stages. Moved out of the application in stage K5, unchanged.
/// </summary>
/// <remarks>
/// The script runs in a session that outlives it (app-building plan, U1): a GUI it leaves on screen
/// keeps answering its user, in the workspace that built it, until the last window closes; then the
/// session ends, and with it the run's files and native host. The windows answer their user from the
/// first statement on, so a wait inside the script (uiwait, waitfor, a blocking dialog) is a real one.
/// </remarks>
public sealed class WindowedBatchRun
{
    private readonly System.Windows.Application _app;
    private readonly IFigureWindowService _figureWindows;
    private readonly CancellationTokenSource _pumpStop = new();

    /// <summary>The session the run keeps for its windows' callbacks.</summary>
    private IScriptSession? _session;

    /// <summary>The run's <c>-logfile</c>, kept open with the session for what the callbacks print.</summary>
    private TeeScriptOutput? _log;

    /// <summary>
    /// Whether a pump run is under way. A flag set before the run starts, never the run's task: a
    /// drain that finishes before its first await completes the task synchronously, and assigning
    /// that finished task after its own finally had cleared the field left the pump looking busy for
    /// ever, so no event was delivered again (found by the U1 window check).
    /// </summary>
    private bool _pumping;

    /// <summary>Creates a run in <paramref name="app"/>, whose figures <paramref name="figureWindows"/> shows.</summary>
    public WindowedBatchRun(System.Windows.Application app, IFigureWindowService figureWindows)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _figureWindows = figureWindows ?? throw new ArgumentNullException(nameof(figureWindows));
    }

    /// <summary>
    /// The exit code a run that ended with windows still open earned: the process exits when the user
    /// closes the last one, but with this code. Null until then.
    /// </summary>
    public int? PendingExitCode { get; private set; }

    /// <summary>Whether the run still holds its session (its windows are up).</summary>
    public bool HoldsSession => _session is not null;

    /// <summary>
    /// Runs the script. The program shuts down when it ends with no window open; otherwise control
    /// goes back to the user, and the program shuts down when the last window closes.
    /// </summary>
    public async Task RunAsync(StartupOptions options, IScriptEngine[] engines, IScriptOutput output, TeeScriptOutput? tee)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(engines);
        ArgumentNullException.ThrowIfNull(output);

        // This mode has a real dispatcher, so drawnow can be a real render barrier here too.
        ScriptRenderPump.SetFlusher(() => _app.Dispatcher.Invoke(
            static () => { }, System.Windows.Threading.DispatcherPriority.Render));

        // While the script runs there is no session to drain and the pump does nothing; the script's
        // own waits and drawnow deliver the events.
        ScriptEventQueue.InstallPump(() => _app.Dispatcher.BeginInvoke(new Action(Pump)));

        int code;
        try
        {
            (code, _session) = await BatchRunner.RunInSessionAsync(
                options,
                engines,
                output,
                (number, figure) => _app.Dispatcher.Invoke(() => _figureWindows.ShowScriptFigure(number, figure)),
                new AppScriptFigureFiles(),
                audio: null,
                closeFigure: number => _app.Dispatcher.Invoke(() => _figureWindows.CloseScriptFigure(number)));
        }
        catch (Exception ex)
        {
            // Nothing above this can report a failure any more, so say it plainly and fail the run.
            output.WriteError("jgraph: " + ex.Message);
            code = StartupExitCodes.ScriptError;
        }
        finally
        {
            // The log stays open while windows are up: their callbacks print to it (U9).
            if (_app.Windows.Count == 0)
            {
                tee?.Dispose();
            }
            else
            {
                _log = tee;
            }
        }

        if (_app.Windows.Count == 0)
        {
            await EndAsync();
            _app.Shutdown(code);
            return;
        }

        // Figures are on screen: hand control back to the user and keep the code for the way out.
        // Their callbacks are delivered by the pump below, the IDE's idle pump in miniature.
        PendingExitCode = code;

        // The program ends when the last window closes - once what that close queued for the script
        // thread has been delivered: the figure's DeleteFcn, an app's delete method. WPF's own
        // OnLastWindowClose shut the program down before the pump could run them (the Hello World
        // app, ADR 0278), so the end is decided here, when the window service reports the close.
        _app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        ScriptGraphicsCallbacks.WindowClosed += OnWindowClosed;
        ScriptUiTrace.Write($"batch: run ended with {code}, {_app.Windows.Count} window(s), session {_session is not null}");
        Pump();
    }

    /// <summary>
    /// How long the program waits, after its last window closed, for the work that close queued to
    /// be delivered before it shuts down regardless. R2025b's take 12-20 s after theirs (k6_windows).
    /// </summary>
    private static readonly TimeSpan ClosingGrace = TimeSpan.FromSeconds(10);

    private System.Diagnostics.Stopwatch? _sinceLastWindow;
    private bool _ending;
    private bool _endRetryScheduled;

    private void OnWindowClosed() => _app.Dispatcher.BeginInvoke(new Action(EndWhenDone));

    /// <summary>
    /// Shuts the program down once its last window has closed and nothing that close queued is
    /// still owed. A drain under way, or events still queued, are given until <see cref="ClosingGrace"/>
    /// has passed since the last window went. UI thread.
    /// </summary>
    private void EndWhenDone()
    {
        if (_ending || PendingExitCode is not { } code || _app.Windows.Count > 0)
        {
            return;
        }

        _sinceLastWindow ??= System.Diagnostics.Stopwatch.StartNew();
        if ((_pumping || ScriptEventQueue.HasWork) && _sinceLastWindow.Elapsed < ClosingGrace)
        {
            Pump();
            if (!_endRetryScheduled)
            {
                _endRetryScheduled = true;
                _ = RetryEndAsync();
            }

            return;
        }

        _ending = true;
        ScriptUiTrace.Write($"batch: last window closed, shutting down with {code}");
        _app.Shutdown(code);
    }

    private async Task RetryEndAsync()
    {
        try
        {
            await Task.Delay(50).ConfigureAwait(true);
        }
        finally
        {
            _endRetryScheduled = false;
        }

        EndWhenDone();
    }

    /// <summary>
    /// Delivers queued callbacks to the session when it is idle. UI thread; called when an event is
    /// queued and again when a pump run ends, so an event that arrived mid-run is not missed.
    /// </summary>
    private void Pump()
    {
        if (_pumping || _session is not IGraphicsEventSession session || !ScriptEventQueue.HasWork)
        {
            ScriptUiTrace.Write($"pump: not started (running {_pumping}, session {_session is not null}, work {ScriptEventQueue.HasWork})");
            return;
        }

        ScriptUiTrace.Write("pump: started");
        _pumping = true;
        _ = PumpOnceAsync(session);
    }

    private async Task PumpOnceAsync(IGraphicsEventSession session)
    {
        try
        {
            await session.DrainGraphicsEventsAsync(null, _pumpStop.Token).ConfigureAwait(true);
        }
        catch (Exception ex) when (ScriptExitException.Unwrap(ex) is { } exit)
        {
            // A callback called exit: the run ends with the code it gave, windows or no windows.
            PendingExitCode = exit.ExitCode;
            _app.Shutdown(exit.ExitCode);
            return;
        }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException or ObjectDisposedException)
        {
            // A callback's own errors were already reported; only the pump itself lands here.
            ScriptUiTrace.Write("pump: " + ex.GetType().Name + " " + ex.Message);
        }
        finally
        {
            _pumping = false;
            ScriptUiTrace.Write("pump: ended");
        }

        Pump();
        EndWhenDone();
    }

    /// <summary>Ends the run's session: its workspace, files and native host, and the log.</summary>
    public async Task EndAsync()
    {
        ScriptGraphicsCallbacks.WindowClosed -= OnWindowClosed;
        ScriptEventQueue.InstallPump(null);
        _pumpStop.Cancel();
        if (_session is { } session)
        {
            _session = null;
            await session.DisposeAsync();
        }

        _log?.Dispose();
        _log = null;
    }
}
