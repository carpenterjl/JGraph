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
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(engines);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(showFigure);

        string workingDirectory = options.StartDirectory ?? Environment.CurrentDirectory;
        if (!Directory.Exists(workingDirectory))
        {
            output.WriteError($"jgraph: directory '{workingDirectory}' does not exist.");
            return StartupExitCodes.UsageError;
        }

        workingDirectory = Path.GetFullPath(workingDirectory);
        ResolvedStatement resolved = StartupStatement.Resolve(options.Statement, workingDirectory);
        if (resolved.Error is { } error)
        {
            output.WriteError("jgraph: " + error);
            return StartupExitCodes.UsageError;
        }

        if (SelectEngine(engines, resolved.Language) is not { } engine)
        {
            output.WriteError("jgraph: " + UnavailableReason(engines, resolved.Language));
            return StartupExitCodes.UsageError;
        }

        var context = new ScriptContext(
            output,
            showFigure,
            workingDirectory,
            ReadResolver(workingDirectory, resolved.SourceDirectory),
            figureFiles,
            audio);

        ScriptRunResult result;
        try
        {
            result = await engine.RunAsync(resolved.Code, context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            output.WriteError("jgraph: the script run was cancelled.");
            return StartupExitCodes.ScriptError;
        }

        if (result.ExitCode is { } code)
        {
            return code;
        }

        if (result.Success)
        {
            return StartupExitCodes.Success;
        }

        // The engines already write their diagnostics to the output as they go, so this is a single
        // closing line the shell can see rather than a second copy of everything.
        output.WriteError($"jgraph: script failed — {result.Message ?? "unknown error"}");
        return StartupExitCodes.ScriptError;
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
