using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using JGraph.Application.Mvvm;
using JGraph.Application.Scripting;
using JGraph.Application.Services;
using JGraph.Application.Startup;
using JGraph.Application.Theming;
using JGraph.Core.Model;
using JGraph.Numerics;
using JGraph.Plugins;
using JGraph.Reporting;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Startup;
using Microsoft.Extensions.DependencyInjection;

namespace JGraph.Application;

/// <summary>
/// The MVVM application shell. Its <see cref="OnStartup"/> builds the dependency-injection container
/// (the composition root), then acts on the startup options: normally it shows the figure window, but
/// <c>-batch -showfigures</c> runs a script with no main window at all and <c>-r</c> runs one and then
/// leaves the session open. The plain <c>-batch</c> case never reaches here — it runs headlessly in
/// <c>jgraph.exe</c>, which needs neither WPF nor a display.
/// </summary>
public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;
    private int? _pendingExitCode;
    private int _alerted;
    private bool _headless;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        InstallCrashGuards();

        StartupOptions options = StartupCommandLine.Parse(e.Args);
        if (options.HasUsageError)
        {
            ShowText(options.UsageError + Environment.NewLine + Environment.NewLine + StartupHelp.UsageText, "JGraph");
            Shutdown(StartupExitCodes.UsageError);
            return;
        }

        if (options.Mode == StartupMode.Help)
        {
            ShowHelp();
            Shutdown(StartupExitCodes.Success);
            return;
        }

        // Delete numeric-buffer temp files orphaned by power loss (a crash alone never orphans:
        // they are opened delete-on-close). Fire-and-forget; files held by live processes are skipped.
        Task.Run(() => BufferAllocator.SweepOrphans(BufferAllocator.DefaultMappedDirectory));

        var collection = new ServiceCollection();
        ConfigureServices(collection);
        _services = collection.BuildServiceProvider();

        // The theme is installed before anything is on screen — the splash is the very next thing
        // created, and a window built under one theme and swapped to another re-renders visibly.
        var themes = _services.GetRequiredService<ThemeManager>();
        var settingsService = _services.GetRequiredService<ISettingsService>();
        themes.Apply(settingsService.Current.AppTheme);

        // Saving the Options dialog is the entire live-switch trigger: Changed already fires on
        // every Save, and re-applying the theme already in force is a no-op.
        settingsService.Changed += (_, _) => themes.Apply(settingsService.Current.AppTheme);

        // loadlibrary's C compiler (ADR 0181) is the Options choice, read again whenever it is saved.
        LoadLibraryCompilers.Preferred = settingsService.Current.CCompiler;
        settingsService.Changed += (_, _) => LoadLibraryCompilers.Preferred = settingsService.Current.CCompiler;

        if (options.Mode == StartupMode.Batch)
        {
            RunBatch(options);
            return;
        }

        // The scripting workspace is the application shell (M30). Figures open beside it on demand —
        // from a script, from the console, or by opening a .graph file — never as the main window.
        _ = ShowShellAsync(options);
    }

    private async Task ShowShellAsync(StartupOptions options)
    {
        // No main window exists yet, so nothing must be able to end the session before the shell is
        // up: closing the splash would otherwise look like the last window closing.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        ScriptWorkspaceWindow shell;
        try
        {
            shell = await InteractiveStartup.PrepareShellAsync(_services!);
        }
        catch (Exception ex)
        {
            ShowText("JGraph could not start: " + ex.Message, "JGraph");
            Shutdown(StartupExitCodes.ScriptError);
            return;
        }

        // Inside the try as well: showing the shell is the step that materialises the restored
        // dock layout, and until ShutdownMode moves off OnExplicitShutdown a throw here would leave
        // a dispatcher running with no window, no taskbar entry and no way out but Task Manager.
        try
        {
            MainWindow = shell;
            shell.Show();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
        catch (Exception ex)
        {
            ShowText("JGraph could not open its main window: " + ex.Message, "JGraph");
            Shutdown(StartupExitCodes.ScriptError);
            return;
        }

        if (options.Mode == StartupMode.Run && options.Statement is { Length: > 0 } statement)
        {
            try
            {
                _services!.GetRequiredService<IScriptingService>().OpenEditorAndRun(statement, options.LogFile);
            }
            catch (Exception ex)
            {
                // The shell is up, so this is reportable without ending the session.
                ShowText("JGraph could not run " + statement + ": " + ex.Message, "JGraph");
            }
        }
    }

    /// <summary>
    /// Catches what would otherwise end the process without a word. A WPF application with no handler
    /// here dies to the operating system on any exception that escapes an event handler or a command,
    /// with nothing on screen to say why. Every guard funnels into <see cref="ReportCrash"/>, which
    /// shows the bug-report dialog prefilled with the fault and then ends the session — the state is
    /// untrusted after an unhandled exception, so the dialog is the last thing the process does
    /// (M114; this supersedes the earlier report-and-carry-on behaviour).
    /// </summary>
    private void InstallCrashGuards()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            ReportCrash(args.Exception);

            // ReportCrash already ends the session on the first crash; this is the backstop for
            // the ones it declines (latch taken, headless). Swallowing those with no window open
            // and nothing that will ever close would leave a process with no UI and no way out
            // but Task Manager.
            if (Windows.Count == 0 && ShutdownMode == ShutdownMode.OnExplicitShutdown)
            {
                Shutdown(StartupExitCodes.ScriptError);
            }
        };

        // A background thread's exception cannot be handled — the runtime is already unwinding —
        // but ReportCrash blocks on the crash dialog before this handler returns, which is the
        // difference between a bug report and "it just closed".
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                ReportCrash(ex);
            }
        };

        // A faulted fire-and-forget task is silent by default, and the startup path is one.
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
            ReportCrash(args.Exception);
        };
    }

    private void ReportCrash(Exception exception)
    {
        Log(exception);

        // At most one dialog for the whole session: a fault raised from a layout or a render pass
        // recurs on the next pass, and the dialog pumps the dispatcher while it is up. The latch is
        // taken before marshalling so a second crash — on any thread — just logs while the first
        // one's dialog decides how the session ends. Headless runs never had a dialog and still
        // do not; their report is the log and the exit code.
        if (_headless || Interlocked.Exchange(ref _alerted, 1) != 0)
        {
            return;
        }

        if (Dispatcher.CheckAccess())
        {
            PromptAndExit(exception);
            return;
        }

        // A background thread's crash must BLOCK here: the AppDomain handler's return is the last
        // thing before the runtime tears the process down, so the dialog gets its say first.
        try
        {
            Dispatcher.Invoke(() => PromptAndExit(exception));
        }
        catch (Exception marshalling) when (marshalling is TaskCanceledException or InvalidOperationException)
        {
            // The dispatcher is already shutting down; the fault is in the log.
        }
    }

    /// <summary>
    /// The last thing the session does: the crash dialog (prefilled with the fault, offering the
    /// script on screen as an attachment), then a clean shutdown. Anything failing in here — the
    /// dialog itself throwing on a poisoned render loop — skips straight to ending the process,
    /// because the alternative is a crash loop with no way out but Task Manager.
    /// </summary>
    private void PromptAndExit(Exception exception)
    {
        try
        {
            Scripting.ScriptWorkspaceWindow? shell = Windows.OfType<Scripting.ScriptWorkspaceWindow>().FirstOrDefault();
            if (_services?.GetService<IBugReportService>() is { } reports)
            {
                reports.ShowCrashDialog(exception, shell?.GetActiveScriptSnapshot());
            }
            else
            {
                // Before the container exists there is no dialog to show; say what the dialog would.
                MessageBox.Show(
                    "JGraph hit an unexpected error and has to close." + Environment.NewLine
                    + Environment.NewLine + exception.Message,
                    "JGraph",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            Shutdown(StartupExitCodes.ScriptError);
        }
        catch (Exception)
        {
            Environment.Exit(StartupExitCodes.ScriptError);
        }
    }

    /// <summary>
    /// Records a fault where it can be read afterwards. Debug.WriteLine is compiled out of a release
    /// build and a windowed process has no console attached, so without a file the only report of a
    /// swallowed exception would be the one dialog — and only for the first.
    /// </summary>
    private static void Log(Exception exception)
    {
        Console.Error.WriteLine("jgraph: " + exception.Message);
        try
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JGraph", "crash.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(
                path,
                $"{DateTime.Now:u} {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            // Nothing left to report with.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // The batch session's run ends here, after its last window: its workspace, files and native
        // host go now, as a one-shot run's go when its script returns.
        if (_batchSession is not null)
        {
            try
            {
                EndBatchSessionAsync().Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // Leaving anyway: the process is ending.
            }
        }

        if (_pendingExitCode is { } code)
        {
            // A batch run that ended while its figure windows were still open: the process exits when
            // the user closes the last one, but with the code the script earned.
            e.ApplicationExitCode = code;
        }

        _services?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Runs a <c>-batch -showfigures</c> script: no main window, figures in standalone windows, output
    /// on the standard streams (the launcher gave us a pipe). The process ends as soon as the script
    /// does — unless it left windows open, in which case it waits for the user to close them, since
    /// exiting immediately would make the figures it was asked to show flash past unseen.
    /// </summary>
    private void RunBatch(StartupOptions options)
    {
        // No dialogs from here on. This mode reports on the standard streams and must end with an
        // exit code, so a modal box would hang a scripted run for ever with nobody to click it.
        _headless = true;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        IScriptOutput console = ConsoleScriptOutput.Instance;
        TeeScriptOutput? tee = options.LogFile is { Length: > 0 } log
            ? new TeeScriptOutput(console, new FileScriptOutput(log))
            : null;
        IScriptOutput output = tee ?? console;

        var figureWindows = _services!.GetRequiredService<IFigureWindowService>();
        IScriptEngine[] engines = _services!.GetServices<IScriptEngine>().ToArray();

        _ = RunBatchAsync(options, engines, output, figureWindows, tee);
    }

    private async Task RunBatchAsync(
        StartupOptions options,
        IScriptEngine[] engines,
        IScriptOutput output,
        IFigureWindowService figureWindows,
        TeeScriptOutput? tee)
    {
        // This mode has a real dispatcher, so drawnow can be a real render barrier here too.
        ScriptRenderPump.SetFlusher(() => Dispatcher.Invoke(
            static () => { }, System.Windows.Threading.DispatcherPriority.Render));

        // The script runs in a session that outlives it (app-building plan, U1): a GUI it leaves on
        // screen keeps answering its user, in the workspace that built it, until the last window
        // closes — then the session ends, and with it the run's files and native host.
        // The windows answer their user from the first statement on, so a wait inside the script
        // (uiwait, waitfor, a blocking dialog) is a real one: the pump is what says somebody can
        // answer. While the script runs there is no session to drain and the pump does nothing;
        // the script's own waits and drawnow deliver the events.
        ScriptEventQueue.InstallPump(() => Dispatcher.BeginInvoke(new Action(PumpBatchSession)));

        int code;
        try
        {
            (code, _batchSession) = await BatchRunner.RunInSessionAsync(
                options,
                engines,
                output,
                (number, figure) => Dispatcher.Invoke(() => figureWindows.ShowScriptFigure(number, figure)),
                new AppScriptFigureFiles(),
                audio: null,
                closeFigure: number => Dispatcher.Invoke(() => figureWindows.CloseScriptFigure(number)));
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
            if (Windows.Count == 0)
            {
                tee?.Dispose();
            }
            else
            {
                _batchLog = tee;
            }
        }

        if (Windows.Count == 0)
        {
            await EndBatchSessionAsync();
            Shutdown(code);
            return;
        }

        // Figures are on screen: hand control back to the user and keep the code for the way out.
        // Their callbacks are delivered by the pump below, the IDE's idle pump in miniature.
        _pendingExitCode = code;
        ShutdownMode = ShutdownMode.OnLastWindowClose;
        ScriptUiTrace.Write($"batch: run ended with {code}, {Windows.Count} window(s), session {_batchSession is not null}");
        PumpBatchSession();
    }

    /// <summary>The session a <c>-batch -showfigures</c> run keeps for its windows' callbacks.</summary>
    private IScriptSession? _batchSession;

    /// <summary>The run's <c>-logfile</c>, kept open with the session for what the callbacks print.</summary>
    private TeeScriptOutput? _batchLog;

    /// <summary>
    /// Whether a pump run is under way. A flag set before the run starts, never the run's task: a
    /// drain that finishes before its first await completes the task synchronously, and assigning
    /// that finished task after its own finally had cleared the field left the pump looking busy for
    /// ever, so no event was delivered again (found by the U1 window check).
    /// </summary>
    private bool _batchPumping;
    private readonly CancellationTokenSource _batchPumpStop = new();

    /// <summary>
    /// Delivers queued callbacks to the batch session when it is idle. UI thread; called when an
    /// event is queued and again when a pump run ends, so an event that arrived mid-run is not missed.
    /// </summary>
    private void PumpBatchSession()
    {
        if (_batchPumping || _batchSession is not IGraphicsEventSession session || !ScriptEventQueue.HasWork)
        {
            ScriptUiTrace.Write($"pump: not started (running {_batchPumping}, session {_batchSession is not null}, work {ScriptEventQueue.HasWork})");
            return;
        }

        ScriptUiTrace.Write("pump: started");
        _batchPumping = true;
        _ = PumpOnceAsync(session);
    }

    private async Task PumpOnceAsync(IGraphicsEventSession session)
    {
        try
        {
            await session.DrainGraphicsEventsAsync(null, _batchPumpStop.Token).ConfigureAwait(true);
        }
        catch (Exception ex) when (ScriptExitException.Unwrap(ex) is { } exit)
        {
            // A callback called exit: the run ends with the code it gave, windows or no windows.
            _pendingExitCode = exit.ExitCode;
            Shutdown(exit.ExitCode);
            return;
        }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException or ObjectDisposedException)
        {
            // A callback's own errors were already reported; only the pump itself lands here.
            ScriptUiTrace.Write("pump: " + ex.GetType().Name + " " + ex.Message);
        }
        finally
        {
            _batchPumping = false;
            ScriptUiTrace.Write("pump: ended");
        }

        PumpBatchSession();
    }

    private async Task EndBatchSessionAsync()
    {
        ScriptEventQueue.InstallPump(null);
        _batchPumpStop.Cancel();
        if (_batchSession is { } session)
        {
            _batchSession = null;
            await session.DisposeAsync();
        }

        _batchLog?.Dispose();
        _batchLog = null;
    }

    /// <summary>Opens the HTML scripting guide, falling back to the flag reference in a dialog.</summary>
    private static void ShowHelp()
    {
        if (StartupHelp.FindGuide(AppContext.BaseDirectory) is { } guide)
        {
            try
            {
                using Process? _ = Process.Start(new ProcessStartInfo(guide) { UseShellExecute = true });
                return;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // No handler for .html — fall through to the text.
            }
        }

        ShowText(StartupHelp.UsageText, "JGraph — startup options");
    }

    private static void ShowText(string text, string caption) =>
        MessageBox.Show(text, caption, MessageBoxButton.OK, MessageBoxImage.Information);

    private static void ConfigureServices(IServiceCollection services)
    {
        // User settings, loaded first: the plugin filter and the JGS engine's language options both
        // read from them.
        var settings = new SettingsService();
        services.AddSingleton<ISettingsService>(settings);

        // The plugin registry: the built-in standard library (Light/Dark/Presentation/IEEE themes and
        // colormaps) plus anything discovered in a "plugins" folder next to the executable that the
        // user has not turned off.
        // Registered as a factory, not an instance: scanning the folder is the slowest thing at
        // startup, so the splash resolves it off the UI thread and reports progress while it runs.
        string pluginDirectory = Path.Combine(AppContext.BaseDirectory, "plugins");
        services.AddSingleton(_ => PluginLoader.LoadDefault(
            pluginDirectory, plugin => settings.Current.IsPluginEnabled(plugin.GetType().FullName ?? plugin.GetType().Name)));

        // Application chrome. The catalog is a fixed built-in set (see IAppThemeCatalog); the
        // manager owns the one swappable entry in Application.Resources.MergedDictionaries.
        services.AddSingleton<IAppThemeCatalog, AppThemeCatalog>();
        services.AddSingleton(sp => new ThemeManager(sp.GetRequiredService<IAppThemeCatalog>()));

        services.AddSingleton<IFigureFactory, SampleFigureFactory>();
        services.AddSingleton<IFigureExportService, FigureExportService>();
        services.AddSingleton<IFigureDocumentService, FigureDocumentService>();
        services.AddSingleton<IDataImportService, DataImportService>();

        // The print and page dialogs (M84), beside the export and document services they sit with.
        services.AddSingleton<IFigurePrintService, Printing.FigurePrintService>();

        // Scripting engines: MATLAB, JGS and C# are always available; Python is available when a
        // CPython runtime is found. JGS reads the user's language options on each run. The order is
        // the order of every picker (New Script, the console's language list), MATLAB first because
        // it is the language the window defaults to.
        services.AddSingleton<IScriptEngine, MatlabScriptEngine>();
        services.AddSingleton<IScriptEngine>(new JgsScriptEngine(() => settings.Current.ToJgsOptions()));
        services.AddSingleton<IScriptEngine, CSharpScriptEngine>();
        services.AddSingleton<IScriptEngine, PythonScriptEngine>();
        services.AddSingleton<IWorkspaceStateService, WorkspaceStateService>();
        services.AddSingleton<IFigureWindowService, FigureWindowService>();
        services.AddSingleton<IScriptingService, ScriptingService>();
        services.AddSingleton<IOptionsService, OptionsService>();

        // Bug reports (ADR 0116): the product's one outbound network call. The transport aims at
        // the deployed relay; the environment variable exists so a test relay can be tried without
        // rebuilding. No credential is involved anywhere - the relay sends the mail.
        services.AddSingleton<IBugReportTransport>(new HttpBugReportTransport(
            Environment.GetEnvironmentVariable("JGRAPH_BUGREPORT_URL") ?? BugReportRelay.Url));
        services.AddSingleton<IBugReportService, BugReportService>();

        // The shell is a singleton — it is the main window, and closing it ends the session. Figure
        // windows stay transient: FigureWindowService mints one per figure number.
        services.AddSingleton(sp => new ScriptWorkspaceWindow(
            sp.GetServices<IScriptEngine>().ToList(),
            sp.GetRequiredService<IWorkspaceStateService>(),
            sp.GetRequiredService<IFigureWindowService>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<IOptionsService>(),
            sp.GetRequiredService<IBugReportService>()));

        services.AddTransient<FigureViewModel>();
        services.AddTransient<FigureWindow>();
    }
}
