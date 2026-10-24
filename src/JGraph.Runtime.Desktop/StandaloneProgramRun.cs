using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using JGraph.Core.Model;
using JGraph.Deploy;
using JGraph.Plugins;
using JGraph.Runtime.Desktop.Services;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Startup;
using JGraph.Serialization.Settings;

namespace JGraph.Runtime.Desktop;

/// <summary>
/// Runs a standalone program (compiler and SDK plan, stage K6): what the JGraph Runtime's
/// <c>jglaunch.dll</c> does once a program's launcher has started .NET. The program's archive is the
/// one appended to it, or its <c>mcc -C</c> archive beside it; its main runs as <c>jgraph --deployed</c>
/// runs one (K1, K3), on <see cref="WindowedBatchRun"/>, so a figure or app it leaves on screen keeps
/// the program alive, answering its user, until the last window closes, as R2025b's do (k6_windows).
/// </summary>
/// <remarks>
/// A windowed program (<c>mcc -e</c>) writes nothing anywhere, as R2025b's writes nothing even to the
/// handles it inherits; the error that ends one is shown in a dialog titled <c>Error</c> instead, and
/// the program exits with 249 once it is dismissed. Its splash, which the launcher shows from
/// <c>splash.png</c>, goes when the program's first window is shown or its run ends.
/// </remarks>
public static class StandaloneProgramRun
{
    /// <summary>Runs <paramref name="program"/> with <paramref name="arguments"/> on this thread, which must be STA, and answers its exit code.</summary>
    /// <param name="program">The program the launcher was started as.</param>
    /// <param name="splash">The launcher's splash window, or zero for none.</param>
    /// <param name="arguments">The program's command line after its name.</param>
    public static int Run(string program, nint splash, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(program);
        ArgumentNullException.ThrowIfNull(arguments);
        bool windowed = IsWindowed(program);
        var windowedOutput = windowed ? new WindowedProgramOutput() : null;
        IScriptOutput output = windowedOutput is null ? ConsoleScriptOutput.Instance : windowedOutput;
        StartupOptions options = StartupCommandLine.Parse(["--deployed", program, "--", .. arguments]);
        if (options.HasUsageError)
        {
            output.WriteError("jgraph: " + options.UsageError);
            return Ended(StartupExitCodes.UsageError, windowedOutput, program, splash);
        }

        var settings = new UserSettingsDto();
        var jgsOptions = new JgsLanguageOptions(RequireLet: !settings.JgsOptionalLet, IndexBase: settings.JgsIndexBase);
        IScriptEngine[] engines =
        [
            new JgsScriptEngine(() => jgsOptions),
            new MatlabScriptEngine(),
            new CSharpScriptEngine(),
            new PythonScriptEngine(),
        ];

        System.Windows.Application app = DesktopHost.NewApplication();
        PluginRegistry plugins = PluginRegistry.CreateDefault();
        var windows = new SplashClosingWindows(new FigureWindowService(() => DesktopHost.DeployedWindow(plugins)), splash);
        var run = new WindowedBatchRun(app, windows);
        app.Startup += (_, _) => _ = run.RunAsync(options, engines, output, tee: null);
        app.Exit += (_, e) =>
        {
            if (run.HoldsSession)
            {
                try
                {
                    run.EndAsync().Wait(TimeSpan.FromSeconds(5));
                }
                catch (AggregateException)
                {
                    // Leaving anyway: the process is ending.
                }
            }

            if (run.PendingExitCode is { } code)
            {
                e.ApplicationExitCode = code;
            }
        };

        return Ended(app.Run(), windowedOutput, program, splash);
    }

    /// <summary>Whether the program is a windowed one: its archive's manifest says it was built with <c>mcc -e</c>.</summary>
    private static bool IsWindowed(string program)
    {
        try
        {
            using CtfArchiveReader archive = CtfArchiveReader.Open(ScriptDeployment.ArchiveOf(program));
            return archive.Manifest.TargetType == CtfTargetType.WindowsStandaloneApplication;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // The run reports what is wrong with the archive.
            return false;
        }
    }

    /// <summary>
    /// The run's end: the splash goes, and a windowed program that could not start says why in a
    /// dialog, since it has nowhere else to say it.
    /// </summary>
    private static int Ended(int code, WindowedProgramOutput? windowed, string program, nint splash)
    {
        CloseSplash(splash);
        if (code == StartupExitCodes.UsageError && windowed?.StartErrors is { Length: > 0 } errors)
        {
            MessageBoxW(0, errors, Path.GetFileName(program), 0x10);
        }

        return code;
    }

    private static void CloseSplash(nint splash)
    {
        if (splash != 0)
        {
            PostMessageW(splash, 0x0010, 0, 0);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(nint owner, string text, string caption, uint type);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);

    /// <summary>The figure windows of the run, which close the launcher's splash when the first of them is shown.</summary>
    private sealed class SplashClosingWindows(IFigureWindowService inner, nint splash) : IFigureWindowService
    {
        private nint _splash = splash;

        public void ShowScriptFigure(int number, FigureModel figure)
        {
            inner.ShowScriptFigure(number, figure);
            CloseSplash(Interlocked.Exchange(ref _splash, 0));
        }

        public int OpenBlankFigure() => inner.OpenBlankFigure();

        public void CloseScriptFigure(int number) => inner.CloseScriptFigure(number);

        public void CloseAll() => inner.CloseAll();
    }

    /// <summary>
    /// A windowed program's output: nothing is written, as R2025b's <c>-e</c> program writes nothing;
    /// its uncaught error is a dialog titled <c>Error</c>, and the launch's own refusals are kept for
    /// the dialog <see cref="Ended"/> shows.
    /// </summary>
    private sealed class WindowedProgramOutput : IScriptOutput, IDeployedErrorDialog
    {
        private readonly System.Text.StringBuilder _start = new();

        public string StartErrors => _start.ToString().TrimEnd('\n');

        public void Write(string text)
        {
        }

        public void WriteLine(string text)
        {
        }

        public void WriteError(string text)
        {
            if (text.StartsWith("jgraph: ", StringComparison.Ordinal))
            {
                _start.Append(text["jgraph: ".Length..]).Append('\n');
            }
        }

        public void ShowError(string text) => MessageBoxW(0, text, "Error", 0x10);
    }
}
