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
        // This mode has a real dispatcher, so drawnow can be a real render barrier here too. There
        // is no live session to pump events through — a one-shot run's callbacks queue and never
        // fire, the documented degradation — so only the flusher is installed.
        ScriptRenderPump.SetFlusher(() => Dispatcher.Invoke(
            static () => { }, System.Windows.Threading.DispatcherPriority.Render));

        int code;
        try
        {
            code = await BatchRunner.RunAsync(
                options,
                engines,
                output,
                (number, figure) => Dispatcher.Invoke(() => figureWindows.ShowScriptFigure(number, figure)),
                new AppScriptFigureFiles(),
                audio: null);
        }
        catch (Exception ex)
        {
            // Nothing above this can report a failure any more, so say it plainly and fail the run.
            output.WriteError("jgraph: " + ex.Message);
            code = StartupExitCodes.ScriptError;
        }
        finally
        {
            tee?.Dispose();
        }

        if (Windows.Count == 0)
        {
            Shutdown(code);
            return;
        }

        // Figures are on screen: hand control back to the user and keep the code for the way out.
        _pendingExitCode = code;
        ShutdownMode = ShutdownMode.OnLastWindowClose;
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
