using JGraph.Core.Model;

namespace JGraph.Scripting.Startup;

/// <summary>
/// Runs one script for a non-interactive start (<c>-batch</c>) and reports a process exit code. It is
/// UI-free, so the headless launcher and the WPF application share it exactly — they differ only in
/// the output sink they pass and in what their <c>showFigure</c> callback does.
/// </summary>
public static class BatchRunner
{
    /// <summary>
    /// Resolves the statement, picks the engine, runs it, and maps the outcome to an exit code.
    /// </summary>
    /// <param name="options">The parsed command line; <see cref="StartupOptions.Statement"/> is what runs.</param>
    /// <param name="engines">The available engines, matched by <see cref="IScriptEngine.Language"/>.</param>
    /// <param name="output">Where the script and this runner write.</param>
    /// <param name="showFigure">What <c>show()</c> does — suppress and log, or open a window.</param>
    /// <param name="figureFiles">Figure save/load/export services, or null.</param>
    /// <param name="audio">Audio playback, or null (headless hosts have none).</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>0 on success, 1 on script failure, 2 when nothing could be run, or the code the
    /// script passed to <c>exit</c>.</returns>
    public static async Task<int> RunAsync(
        StartupOptions options,
        IReadOnlyList<IScriptEngine> engines,
        IScriptOutput output,
        Action<int, FigureModel> showFigure,
        IScriptFigureFiles? figureFiles = null,
        IScriptAudio? audio = null,
        CancellationToken cancellationToken = default)
    {
        (int code, IScriptSession? _) = await RunCoreAsync(
            options, engines, output, showFigure, figureFiles, audio, inSession: false, cancellationToken).ConfigureAwait(false);
        return code;
    }

    /// <summary>
    /// Runs the statement as <see cref="RunAsync"/> does, but in an interactive session it then hands
    /// back alive (app-building plan, U1): what <c>-batch -showfigures</c> needs, because a script that
    /// left a GUI on screen has callbacks still to run, and they run in the workspace that made them.
    /// The run's end — its workspace's destruction, its open files, its native host — waits for the
    /// caller to dispose the session, which it does when the last window closes. An engine without
    /// sessions runs as <see cref="RunAsync"/> does and hands back none.
    /// <paramref name="closeFigure"/> closes the window of a figure the script deleted or closed: a
    /// dialog's OK button deletes its figure, and its window has to go with it.
    /// </summary>
    public static Task<(int Code, IScriptSession? Session)> RunInSessionAsync(
        StartupOptions options,
        IReadOnlyList<IScriptEngine> engines,
        IScriptOutput output,
        Action<int, FigureModel> showFigure,
        IScriptFigureFiles? figureFiles = null,
        IScriptAudio? audio = null,
        CancellationToken cancellationToken = default,
        Action<int>? closeFigure = null) =>
        RunCoreAsync(options, engines, output, showFigure, figureFiles, audio, inSession: true, cancellationToken, closeFigure);

    private static async Task<(int Code, IScriptSession? Session)> RunCoreAsync(
        StartupOptions options,
        IReadOnlyList<IScriptEngine> engines,
        IScriptOutput output,
        Action<int, FigureModel> showFigure,
        IScriptFigureFiles? figureFiles,
        IScriptAudio? audio,
        bool inSession,
        CancellationToken cancellationToken,
        Action<int>? closeFigure = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(engines);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(showFigure);

        string workingDirectory = options.StartDirectory ?? Environment.CurrentDirectory;
        if (!Directory.Exists(workingDirectory))
        {
            output.WriteError($"jgraph: directory '{workingDirectory}' does not exist.");
            return (StartupExitCodes.UsageError, null);
        }

        workingDirectory = Path.GetFullPath(workingDirectory);

        // --deployed with no -batch (K3): an archive, extracted into the runtime cache, whose
        // manifest names the main function. The cache's entry is held until the run ends.
        ScriptDeployment? deployment = null;
        DeployedLogOutput? log = null;
        string? statement = options.Statement;
        if (options.DeployedRoot is { } archive && statement is null)
        {
            string archivePath = Path.GetFullPath(Path.Combine(workingDirectory, archive));
            try
            {
                deployment = ScriptDeployment.FromArchive(archivePath, options.ProgramArguments);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                output.WriteError(File.Exists(archivePath)
                    ? $"jgraph: cannot run '{archivePath}': {ex.Message}"
                    : $"jgraph: the archive '{archivePath}' does not exist.");
                return (StartupExitCodes.UsageError, null);
            }

            if (deployment.MainFile is not { } mainFile)
            {
                deployment.Dispose();
                output.WriteError($"jgraph: the archive '{archivePath}' has no main function; it is a library.");
                return (StartupExitCodes.UsageError, null);
            }

            statement = mainFile;

            // The runtime options the program was built with (mcc -R, stage K4).
            output = StartRuntime(deployment.RuntimeOptions, output, workingDirectory, out log);
        }

        try
        {
            return await RunResolvedAsync(
                options, statement, deployment, workingDirectory, engines, output, showFigure, figureFiles, audio, inSession,
                cancellationToken, closeFigure).ConfigureAwait(false);
        }
        finally
        {
            // A session handed back alive keeps running the program; the process's end lets the entry go then.
            if (!inSession)
            {
                deployment?.Dispose();
                log?.Dispose();
            }
        }
    }

    /// <summary>
    /// Starts the runtime under a deployed program's options, as R2025b's does before the main runs
    /// (<c>k4_ropts</c>): the start lines on standard output, one computation thread for the
    /// process under <c>-singleCompThread</c>, and the output that writes the <c>-logfile</c> too,
    /// read against the current folder.
    /// </summary>
    internal static IScriptOutput StartRuntime(
        JGraph.Deploy.RuntimeOptions runtime, IScriptOutput output, string workingDirectory, out DeployedLogOutput? log)
    {
        foreach (string line in runtime.StartLines())
        {
            output.WriteLine(line);
        }

        if (runtime.SingleCompThread)
        {
            ScriptDeployment.UseOneComputationThread();
        }

        log = null;
        if (runtime.LogFile is { } file)
        {
            try
            {
                log = new DeployedLogOutput(output, Path.Combine(workingDirectory, file));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                output.WriteError($"jgraph: cannot write the log file '{file}': {ex.Message}");
            }
        }

        return log ?? output;
    }

    private static async Task<(int Code, IScriptSession? Session)> RunResolvedAsync(
        StartupOptions options,
        string? statement,
        ScriptDeployment? deployment,
        string workingDirectory,
        IReadOnlyList<IScriptEngine> engines,
        IScriptOutput output,
        Action<int, FigureModel> showFigure,
        IScriptFigureFiles? figureFiles,
        IScriptAudio? audio,
        bool inSession,
        CancellationToken cancellationToken,
        Action<int>? closeFigure)
    {
        ResolvedStatement resolved = StartupStatement.Resolve(statement, workingDirectory);
        if (resolved.Error is { } error)
        {
            output.WriteError("jgraph: " + error);
            return (StartupExitCodes.UsageError, null);
        }

        if (SelectEngine(engines, resolved.Language) is not { } engine)
        {
            output.WriteError("jgraph: " + UnavailableReason(engines, resolved.Language));
            return (StartupExitCodes.UsageError, null);
        }

        // --deployed with -batch (K1): the file is a compiled program's main, over a folder standing
        // in for its extracted archive.
        if (deployment is null && options.DeployedRoot is { } root)
        {
            string folder = Path.GetFullPath(Path.Combine(workingDirectory, root));
            if (resolved.SourcePath is not { } main || !Directory.Exists(folder))
            {
                output.WriteError(resolved.SourcePath is null
                    ? "jgraph: --deployed needs -batch to name the main function's file."
                    : $"jgraph: the folder '{folder}' does not exist.");
                return (StartupExitCodes.UsageError, null);
            }

            deployment = ScriptDeployment.FromFolder(folder, Path.GetFullPath(main), options.ProgramArguments, options.NumericArguments);
        }

        var context = new ScriptContext(
            output,
            showFigure,
            workingDirectory,
            ReadResolver(workingDirectory, resolved.SourceDirectory),
            figureFiles,
            audio,
            closeFigure)
        {
            ScriptPath = resolved.SourcePath,
            Deployment = deployment,
        };

        IScriptSession? session = inSession && engine is IScriptRepl repl ? repl.CreateSession(context) : null;
        ScriptRunResult result;
        try
        {
            result = session is null
                ? await engine.RunAsync(resolved.Code, context, cancellationToken).ConfigureAwait(false)
                : resolved.SourcePath is { } path
                    ? await session.ExecuteFileAsync(resolved.Code, Path.GetFullPath(path), cancellationToken).ConfigureAwait(false)
                    : await session.ExecuteAsync(resolved.Code, string.Empty, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            output.WriteError("jgraph: the script run was cancelled.");
            return (StartupExitCodes.ScriptError, session);
        }

        if (result.ExitCode is { } code)
        {
            return (code, session);
        }

        if (result.Success)
        {
            return (StartupExitCodes.Success, session);
        }

        // The engines already write their diagnostics to the output as they go, so this is a single
        // closing line the shell can see rather than a second copy of everything.
        output.WriteError($"jgraph: script failed — {result.Message ?? "unknown error"}");
        return (StartupExitCodes.ScriptError, session);
    }

    /// <summary>
    /// The path resolver for *reads*. Relative names resolve against the shell's directory first — the
    /// user's own frame of reference — and then beside the script itself, so a script run by path can
    /// still find the data files sitting next to it.
    /// </summary>
    private static Func<string, string> ReadResolver(string workingDirectory, string? scriptDirectory) =>
        path =>
        {
            if (Path.IsPathRooted(path))
            {
                return path;
            }

            string candidate = Path.Combine(workingDirectory, path);
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }

            if (scriptDirectory is { Length: > 0 })
            {
                string beside = Path.Combine(scriptDirectory, path);
                if (File.Exists(beside) || Directory.Exists(beside))
                {
                    return beside;
                }
            }

            return candidate; // does not exist yet; the caller reports a sensible "not found"
        };

    private static IScriptEngine? SelectEngine(IReadOnlyList<IScriptEngine> engines, string language)
    {
        foreach (IScriptEngine engine in engines)
        {
            if (string.Equals(engine.Language, language, StringComparison.OrdinalIgnoreCase) && engine.IsAvailable)
            {
                return engine;
            }
        }

        return null;
    }

    private static string UnavailableReason(IReadOnlyList<IScriptEngine> engines, string language)
    {
        foreach (IScriptEngine engine in engines)
        {
            if (string.Equals(engine.Language, language, StringComparison.OrdinalIgnoreCase))
            {
                return language == "Python" ? PythonScriptEngine.UnavailableMessage
                    : $"the {language} engine is not available on this machine.";
            }
        }

        return $"no engine is registered for {language}.";
    }
}
