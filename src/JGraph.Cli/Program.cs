using System.Diagnostics;
using JGraph.Core.Model;
using JGraph.Plugins;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Startup;
using JGraph.Serialization.Settings;

namespace JGraph.Cli;

/// <summary>
/// <c>jgraph.exe</c> — the command-line face of JGraph.
/// </summary>
/// <remarks>
/// A WPF executable has no console, so it can neither write to standard output nor hand a shell a
/// meaningful exit code. This launcher owns both. <c>-batch</c> runs here, in a process that never
/// touches WPF, so a script can be run over a remote session or from a build step with no display at
/// all; the modes that genuinely need a window are handed to the application as a child process.
/// </remarks>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        StartupOptions options = StartupCommandLine.Parse(args);

        if (options.HasUsageError)
        {
            Console.Error.WriteLine("jgraph: " + options.UsageError);
            Console.Error.WriteLine();
            Console.Error.WriteLine(StartupHelp.UsageText);
            return StartupExitCodes.UsageError;
        }

        return options.Mode switch
        {
            StartupMode.Help => ShowHelp(),
            StartupMode.Batch when !options.ShowFigures => await RunHeadlessAsync(options).ConfigureAwait(false),
            StartupMode.Batch => GuiLauncher.RunAndWait(args),
            _ => StartApplication(args),
        };
    }

    /// <summary>
    /// Prints the flag reference and opens the HTML guide when it was deployed. The text is printed
    /// either way: the guide documents the *language*, and what was asked for is the *options*.
    /// </summary>
    private static int ShowHelp()
    {
        Console.WriteLine(StartupHelp.UsageText);

        if (StartupHelp.FindGuide(AppContext.BaseDirectory) is not { } guide)
        {
            return StartupExitCodes.Success;
        }

        try
        {
            using Process? _ = Process.Start(new ProcessStartInfo(guide) { UseShellExecute = true });
            Console.WriteLine();
            Console.WriteLine($"Opening the scripting guide: {guide}");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No browser, or no desktop session at all — the text above is the whole answer then.
            Console.WriteLine();
            Console.WriteLine($"The scripting guide is at: {guide}");
        }

        return StartupExitCodes.Success;
    }

    /// <summary>
    /// Runs the script in this process: no WPF, no window, no display required. Figures are not shown
    /// (<c>-showfigures</c> is the way to ask for that); scripts produce output with
    /// <c>exportfigure</c>/<c>savefigure</c>, which work perfectly well headless.
    /// </summary>
    private static async Task<int> RunHeadlessAsync(StartupOptions options)
    {
        // The same preferences the application reads, so a batch run behaves like an interactive one:
        // the user's plugin choices and their JGS language options.
        UserSettingsDto settings = LoadUserSettings();

        // The theme and colormap plugins the application loads (minus any the user disabled), so a
        // batch-rendered figure matches what the same script draws interactively.
        PluginLoader.LoadDefault(
            Path.Combine(AppContext.BaseDirectory, "plugins"),
            plugin => !settings.DisabledPlugins.Contains(plugin.GetType().FullName ?? plugin.GetType().Name, StringComparer.Ordinal));

        IScriptOutput console = ConsoleScriptOutput.Instance;
        TeeScriptOutput? tee = options.LogFile is { Length: > 0 } log
            ? new TeeScriptOutput(console, new FileScriptOutput(log))
            : null;
        IScriptOutput output = tee ?? console;

        using CancellationTokenSource cancellation = new();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true; // Stop the script, then exit tidily — do not kill the process outright.
            cancellation.Cancel();
        };
        Console.CancelKeyPress += onCancel;

        try
        {
            var jgsOptions = new JgsLanguageOptions(
                RequireLet: !settings.JgsOptionalLet, IndexBase: settings.JgsIndexBase);
            IScriptEngine[] engines =
            [
                new JgsScriptEngine(() => jgsOptions),
                new MatlabScriptEngine(),
                new CSharpScriptEngine(),
                new PythonScriptEngine(),
            ];

            return await BatchRunner.RunAsync(
                options,
                engines,
                output,
                (number, figure) => SuppressFigure(output, number, figure),
                new CliFigureFiles(),
                audio: null,
                cancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
            tee?.Dispose();
        }
    }

    /// <summary>
    /// Reads the user's preferences from <c>%AppData%\JGraph\settings.json</c>, or the shipped defaults
    /// when the file is missing or unreadable — a batch run must never fail over settings.
    /// </summary>
    private static UserSettingsDto LoadUserSettings()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JGraph", "settings.json");
        try
        {
            if (File.Exists(path) && UserSettingsFormat.Deserialize(File.ReadAllText(path)) is { } settings)
            {
                return settings;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fall through to defaults.
        }

        return new UserSettingsDto();
    }

    private static void SuppressFigure(IScriptOutput output, int number, FigureModel figure) =>
        output.WriteLine(
            $"Figure {number} ({figure.Axes.Count} axes) was not displayed — add -showfigures to see it.");

    private static int StartApplication(IReadOnlyList<string> args) =>
        GuiLauncher.StartDetached(args) ? StartupExitCodes.Success : StartupExitCodes.UsageError;
}
