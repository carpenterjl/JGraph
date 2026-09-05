using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using JGraph.Application.Services;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Jgs.Debug;
using JGraph.Scripting.Startup;
using JGraph.Scripting.Workspace;

namespace JGraph.Application.Scripting;

/// <summary>
/// Running scripts: the Run/Stop commands, the <c>-r</c> and <c>-logfile</c> startup options, and the
/// debugger — breakpoints, pause and stepping for our own interpreter (JGS and MATLAB), plus the
/// live-edit-while-paused flow. Hosted engines (C#, Python) run plain.
/// </summary>
public partial class ScriptWorkspaceWindow
{
    private void RunOrContinue()
    {
        if (_session.State == ScriptSessionState.Paused)
        {
            if (TryApplyPendingEdits())
            {
                _debugSession?.Continue();
            }
        }
        else if (_session.State == ScriptSessionState.Idle)
        {
            _ = RunActiveAsync();
        }
    }

    private async Task RunActiveAsync(DocumentEntry? restartOf = null)
    {
        DocumentEntry? entry = restartOf ?? ActiveDocument;
        if (entry is null)
        {
            return;
        }

        string language = entry.Model.Language;
        if (!_engines.TryGetValue(language, out IScriptEngine? engine) || !engine.IsAvailable)
        {
            SetStatus(language switch
            {
                "Python" when _engines.TryGetValue("Python", out IScriptEngine? py) && !py.IsAvailable
                    => PythonScriptEngine.UnavailableMessage,
                "Text" => $"'{entry.Model.FileName}' is not a runnable script.",
                _ => $"No engine available for {language}.",
            });
            return;
        }

        await YieldPumpAsync().ConfigureAwait(true);
        if (!_session.TryBeginRun(language))
        {
            return;
        }

        _runOutputLines = 0;
        _runOutputTruncated = false;
        AppendConsole($"--- Running {language} script ---");
        SetStatus($"Running {entry.Model.FileName}…");

        string? scriptDirectory = entry.Model.FilePath is null ? null : Path.GetDirectoryName(entry.Model.FilePath);
        ScriptWorkspace? workspace = _workspace;
        Func<string, string>? resolver = workspace is null
            ? null
            : path => workspace.Resolve(path, scriptDirectory);
        var context = new ScriptContext(
            _output ??= new ConsoleOutput(this), ShowFigureOnUi, scriptDirectory ?? workspace?.RootPath, resolver,
            new AppScriptFigureFiles(), _audio, CloseFigureOnUi);

        _cts = new System.Threading.CancellationTokenSource();
        ScriptRunResult result;
        bool debugged = WantsDebugger(engine);
        try
        {
            // Three ways to run, in priority order. A script with breakpoints goes under the debugger,
            // inside the live console session when there is one — the paused script is then in the
            // prompt's workspace, exactly as in MATLAB. Otherwise an engine with a console runs
            // inside the live session, so the script and the prompt share one workspace. Anything
            // else runs one-shot, exactly as before.
            if (debugged)
            {
                result = await RunJgsDebugAsync((IJgsDebuggable)engine, entry, context, SessionFor(language), _cts.Token);
            }
            else if (SessionFor(language) is { } session)
            {
                // ExecuteFileAsync, not ExecuteAsync: F5 runs a document, so a function file gets
                // its main function invoked. The prompt path stays on ExecuteAsync, where a typed
                // function definition only defines.
                result = await session.ExecuteFileAsync(
                    entry.Editor.ScriptText, SourceIdOf(entry), _cts.Token);
            }
            else
            {
                result = await engine.RunAsync(entry.Editor.ScriptText, context, _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            result = ScriptRunResult.Failed("Script run was cancelled.");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _debugSession = null;
            _selectedFrame = 0;
            _stepsRemaining = 0;
            PromptLabel.Text = IdlePrompt;
            ClearExecutionMarkers();
            _session.EndRun();
        }

        // Shared with the prompt, so a script run and a typed statement leave the window the same way.
        // exit()/quit() is honoured here too, not only under -batch: a script that says "stop" means
        // the same thing whichever way it was started.
        ShowRunResult(result, announceSuccess: true);
        if (result.ExitCode is not null)
        {
            return;
        }

        if (_restartRequested)
        {
            // An incompatible live edit chose "restart": rerun the same script with the new code.
            _restartRequested = false;
            AppendConsole("--- Restarting with the edited code ---");
            _ = RunActiveAsync(entry);
            return;
        }

        PumpGraphicsEventsWhenIdle();
    }

    /// <summary>
    /// Whether this run should go under the debugger. Only our own interpreter can be debugged, and
    /// only when a breakpoint is actually set somewhere: attaching the debugger unconditionally would
    /// mean an ordinary F5 never shared the console's workspace, which is the behaviour the whole
    /// session model exists to provide. Pause (Break All) is the escape hatch for a run that turns out
    /// to need stopping.
    /// </summary>
    private bool WantsDebugger(IScriptEngine engine) =>
        engine is IJgsDebuggable
        && (_documents.Any(static d => d.Editor.Breakpoints.Count > 0)
            || _persistedBreakpoints.Values.Any(static lines => lines.Count > 0));

    // --- Startup options ---------------------------------------------------------------------------

    /// <summary>
    /// Tees this window's console to <paramref name="path"/> as well — the <c>-logfile</c> option. The
    /// pane and the file then see identical text, so a log is a faithful transcript of the session.
    /// </summary>
    public void SetLogFile(string path)
    {
        var file = new FileScriptOutput(path);
        _logFile = file;
        _output = new TeeScriptOutput(new ConsoleOutput(this), file);
        AppendConsole($"--- Logging to {Path.GetFullPath(path)} ---");
    }

    /// <summary>
    /// Runs the <c>-r</c> statement: an existing file opens as a document, anything else becomes an
    /// unsaved JGS scratch document, and either way it runs immediately. The session then stays open,
    /// which is the whole point of <c>-r</c> — the script is a starting point, not the whole job.
    /// </summary>
    public void RunStartupStatement(string statement)
    {
        ResolvedStatement resolved = StartupStatement.Resolve(statement, Environment.CurrentDirectory);
        if (resolved.Error is { } error)
        {
            SetStatus(error);
            AppendConsole("--- " + error + " ---");
            return;
        }

        if (resolved.SourcePath is { } path)
        {
            OpenDocument(path);
        }
        else
        {
            AddDocument(new ScriptDocumentModel(null, resolved.Code, resolved.Language), activate: true);
        }

        _ = RunActiveAsync();
    }

    // --- Debugging (JGS and MATLAB) ---------------------------------------------------------------

    /// <summary>
    /// Runs a document under the debugger. With a live console session for the language the run
    /// joins its workspace; without one (an engine that could not open a session) it falls back to a
    /// workspace of its own, which is what every debug run had before the K&gt;&gt; prompt (ADR 0128).
    /// </summary>
    private Task<ScriptRunResult> RunJgsDebugAsync(
        IJgsDebuggable engine, DocumentEntry entry, ScriptContext context, IScriptSession? host,
        System.Threading.CancellationToken token)
    {
        JgsDebugSession session = engine.CreateDebugSession();
        _debugSession = session;

        // Arm every known breakpoint: the open documents' live sets plus persisted ones for files
        // that are not open (a run()-included script keeps its breakpoints without a tab).
        foreach ((string file, List<int> lines) in _persistedBreakpoints)
        {
            session.SetBreakpoints(file, lines);
        }

        foreach (DocumentEntry document in _documents)
        {
            if (document.Editor.Breakpoints.Count > 0 || document.Model.FilePath is not null)
            {
                session.SetBreakpoints(SourceIdOf(document), document.Editor.Breakpoints);
            }
        }

        session.Paused += OnDebugPaused;
        session.Resumed += OnDebugResumed;

        // Live-edit baselines: what each open document's text is as the run starts. A document whose
        // text later drifts from its baseline has pending edits to apply at the next resume.
        foreach (DocumentEntry document in _documents)
        {
            document.DebugBaseline = document.Editor.ScriptText;
        }

        return host is not null
            ? session.RunAsync(host, SourceIdOf(entry), entry.Editor.ScriptText, token)
            : session.RunAsync(SourceIdOf(entry), entry.Editor.ScriptText, context, token);
    }

    private static string SourceIdOf(DocumentEntry entry) => entry.Model.FilePath ?? "";

    /// <summary>
    /// Completion symbols for one document from the rest of the workspace: the <c>fn</c>s defined in every
    /// other JGS script. Open documents contribute their live buffer (an unsaved <c>fn</c> completes
    /// immediately); the remaining workspace <c>.jgs</c> files are read from disk through a last-write-time
    /// cache, so the provider stays cheap enough to run on every completion request.
    /// </summary>
    private IReadOnlyList<JGraph.Scripting.Completion.CompletionItem> HarvestWorkspaceSymbols(DocumentEntry current)
    {
        var items = new List<JGraph.Scripting.Completion.CompletionItem>();
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (current.Model.FilePath is string ownPath)
        {
            covered.Add(ownPath);
        }

        foreach (DocumentEntry document in _documents)
        {
            if (document == current || document.Model.Language != "JGS")
            {
                continue;
            }

            if (document.Model.FilePath is string path && !covered.Add(path))
            {
                continue;
            }

            items.AddRange(JGraph.Scripting.Jgs.Completion.JgsCompletionEngine.HarvestFunctions(
                document.Editor.ScriptText, document.Model.FileName));
        }

        if (_workspace is null)
        {
            return items;
        }

        foreach (WorkspaceEntry script in _workspace.EnumerateScripts())
        {
            if (!script.FullPath.EndsWith(".jgs", StringComparison.OrdinalIgnoreCase) || !covered.Add(script.FullPath))
            {
                continue;
            }

            try
            {
                DateTime written = File.GetLastWriteTimeUtc(script.FullPath);
                if (!_symbolCache.TryGetValue(script.FullPath, out var cached) || cached.WrittenUtc != written)
                {
                    cached = (written, JGraph.Scripting.Jgs.Completion.JgsCompletionEngine.HarvestFunctions(
                        File.ReadAllText(script.FullPath), script.Name));
                    _symbolCache[script.FullPath] = cached;
                }

                items.AddRange(cached.Items);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An unreadable script simply contributes no symbols.
            }
        }

        return items;
    }

    private void OnDebugPaused(object? sender, JgsPausedEventArgs e) =>
        // BeginInvoke, never Invoke: the interpreter thread must reach its gate without waiting on the UI.
        Dispatcher.BeginInvoke(() =>
        {
            JgsDebugSession? session = _debugSession;
            if (session is null)
            {
                return;
            }

            // dbstep N: keep stepping until the count is spent — unless something else stopped the
            // run on the way, in which case the user wants to look.
            if (_stepsRemaining > 0 && e.Reason == PauseReason.Step)
            {
                _stepsRemaining--;
                session.StepOver();
                return;
            }

            _stepsRemaining = 0;
            _session.MarkPaused();
            PromptLabel.Text = PausedPrompt;

            // The innermost frame is selected first; selecting it is what shows its variables and
            // moves the execution marker (see ShowFrame).
            CallStackList.ItemsSource = e.CallStack;
            SelectFrame(0);
            SetStatus($"Paused at line {e.Location.Line} ({e.Reason}) — the prompt is now K>>.");
        });

    private void OnDebugResumed(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            _session.MarkResumed();
            PromptLabel.Text = IdlePrompt;
            ClearExecutionMarkers();
            CallStackList.ItemsSource = null;
            if (_session.State == ScriptSessionState.Running)
            {
                SetStatus("Running…");
            }
        });

    private DocumentEntry? FindOrOpenDocument(string sourceId)
    {
        if (sourceId.Length == 0)
        {
            return _documents.FirstOrDefault(d => d.Model.FilePath is null) ?? ActiveDocument;
        }

        DocumentEntry? entry = _documents.FirstOrDefault(d =>
            string.Equals(d.Model.FilePath, sourceId, StringComparison.OrdinalIgnoreCase));
        if (entry is null && File.Exists(sourceId))
        {
            // A run()-included file paused that has no tab yet — open it so the marker has a home.
            OpenDocument(sourceId);
            entry = _documents.FirstOrDefault(d =>
                string.Equals(d.Model.FilePath, sourceId, StringComparison.OrdinalIgnoreCase));
        }

        return entry;
    }

    private void ClearExecutionMarkers()
    {
        foreach (DocumentEntry document in _documents)
        {
            document.Editor.SetCurrentLine(null);
        }
    }

    private void StepCommand(Action<JgsDebugSession> step)
    {
        if (_session.CanStep && _debugSession is { } session && TryApplyPendingEdits())
        {
            step(session);
        }
    }

    private void RequestSetNextStatement(DocumentEntry entry, int line)
    {
        if (_session.State != ScriptSessionState.Paused || _debugSession is not { } session)
        {
            return;
        }

        if (!session.TrySetNextStatement(SourceIdOf(entry), line, out string? error))
        {
            SetStatus(error ?? "Could not set the next statement.");
        }
    }

    /// <summary>
    /// Applies any edits made while paused, per document. Compatible edits take effect silently;
    /// an incompatible one asks: restart with the new code, keep debugging the old code, or stay
    /// paused. Returns false when the resume should be cancelled (restart chosen, or stay paused).
    /// </summary>
    private bool TryApplyPendingEdits()
    {
        if (_debugSession is not { IsPaused: true } session)
        {
            return true;
        }

        // Only the running language's documents: the debug session parses an edit in the run's
        // dialect, so a MATLAB tab's text must not be read as JGS or the other way round.
        foreach (DocumentEntry entry in _documents.Where(d => d.Model.Language == _session.RunningLanguage).ToList())
        {
            string text = entry.Editor.ScriptText;
            if (entry.DebugBaseline is null || string.Equals(entry.DebugBaseline, text, StringComparison.Ordinal))
            {
                continue;
            }

            LiveEditResult result;
            try
            {
                result = session.TryApplyEdit(SourceIdOf(entry), text);
            }
            catch (InvalidOperationException)
            {
                return true; // the run ended while we were asking; nothing to apply
            }

            if (result.Applied)
            {
                entry.DebugBaseline = text;
                session.SetBreakpoints(SourceIdOf(entry), entry.Editor.Breakpoints);
                if (result.NewLocation is { } location)
                {
                    FindOrOpenDocument(location.SourceId)?.Editor.SetCurrentLine(location.Line);
                }

                AppendConsole($"(Applied live edit to {entry.Model.FileName}.)");
                continue;
            }

            MessageBoxResult choice = MessageBox.Show(this,
                $"The edit to {entry.Model.FileName} cannot be applied to the paused script: {result.Message}.\n\n" +
                "Yes — stop and restart the run with the new code.\n" +
                "No — keep debugging the old code (the edit applies on the next run).\n" +
                "Cancel — stay paused.",
                "Live edit", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (choice == MessageBoxResult.Yes)
            {
                _restartRequested = true;
                _cts?.Cancel();
                return false;
            }

            if (choice == MessageBoxResult.Cancel)
            {
                return false;
            }

            entry.DebugBaseline = text; // "No": the old code keeps running; stop asking about this edit
        }

        return true;
    }
}
