using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using JGraph.Application.Services;
using JGraph.Scripting;
using JGraph.Scripting.Startup;
using JGraph.Scripting.Workspace;

namespace JGraph.Application.Scripting;

/// <summary>
/// The interactive console: a prompt over one live <see cref="IScriptSession"/> per language, with a
/// language picker, command history, and interrupt. Statements share their workspace with F5 runs —
/// a variable defined at the prompt is visible to a script and vice versa (see ADR 0035).
/// </summary>
/// <remarks>
/// This lives in the application rather than in <c>JGraph.Controls</c> as a reusable control: the
/// prompt needs the engine list, the run-state model, the workspace path resolver and the figure
/// bridge, none of which Controls can reach. What is genuinely reusable — the session seam, the
/// interpreters, the Python protocol — already sits in <c>JGraph.Scripting</c>.
/// </remarks>
public partial class ScriptWorkspaceWindow
{
    /// <summary>One live session per language, created on first use and kept until the window closes.</summary>
    private readonly Dictionary<string, IScriptSession> _sessions = new(StringComparer.Ordinal);

    private readonly List<string> _promptHistory = new();

    /// <summary>Where Up/Down is in the history; equal to the count when the user is on a fresh line.</summary>
    private int _historyIndex;

    private string _consoleLanguage = "JGS";

    /// <summary>
    /// Fills the language picker. Engines that cannot host a session, and engines whose runtime is
    /// missing, are listed but disabled with the reason — silently omitting Python on a machine
    /// without CPython would leave the user wondering where it went.
    /// </summary>
    private void BuildConsoleLanguages(IReadOnlyList<IScriptEngine> engines)
    {
        ConsoleLanguage.Items.Clear();
        ComboBoxItem? first = null;

        foreach (IScriptEngine engine in engines)
        {
            bool repl = engine is IScriptRepl;
            var item = new ComboBoxItem
            {
                Content = engine.Language,
                Tag = engine.Language,
                IsEnabled = repl && engine.IsAvailable,
                ToolTip = repl
                    ? engine.IsAvailable ? null : PythonScriptEngine.UnavailableMessage
                    : $"{engine.Language} has no interactive console.",
            };

            ConsoleLanguage.Items.Add(item);
            first ??= item.IsEnabled ? item : null;
        }

        ConsoleLanguage.SelectedItem = first;
        _consoleLanguage = first?.Tag as string ?? "JGS";
    }

    private void OnConsoleLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ConsoleLanguage.SelectedItem is ComboBoxItem { Tag: string language })
        {
            _consoleLanguage = language;
            SetStatus($"Console language: {language}.");
        }
    }

    private void OnConsolePromptKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when (Keyboard.Modifiers & ModifierKeys.Shift) == 0:
                e.Handled = true;
                SubmitPrompt();
                break;

            // Ctrl+C only interrupts when there is something to interrupt; otherwise it stays Copy,
            // which is what a user selecting text in the prompt expects.
            case Key.C when (Keyboard.Modifiers & ModifierKeys.Control) != 0
                            && ConsolePrompt.SelectionLength == 0 && _session.CanStop:
                e.Handled = true;
                _cts?.Cancel();
                AppendConsole("--- Interrupt requested ---");
                break;

            case Key.Up when IsOnFirstLine():
                e.Handled = RecallHistory(-1);
                break;

            case Key.Down when IsOnLastLine():
                e.Handled = RecallHistory(+1);
                break;
        }
    }

    private bool IsOnFirstLine() => ConsolePrompt.GetLineIndexFromCharacterIndex(ConsolePrompt.CaretIndex) == 0;

    private bool IsOnLastLine() =>
        ConsolePrompt.GetLineIndexFromCharacterIndex(ConsolePrompt.CaretIndex) == ConsolePrompt.LineCount - 1;

    private bool RecallHistory(int direction)
    {
        if (_promptHistory.Count == 0)
        {
            return false;
        }

        int target = Math.Clamp(_historyIndex + direction, 0, _promptHistory.Count);
        if (target == _historyIndex)
        {
            return false;
        }

        _historyIndex = target;
        ConsolePrompt.Text = target == _promptHistory.Count ? string.Empty : _promptHistory[target];
        ConsolePrompt.CaretIndex = ConsolePrompt.Text.Length;
        return true;
    }

    private void SubmitPrompt()
    {
        string code = ConsolePrompt.Text;
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        if (_session.State != ScriptSessionState.Idle)
        {
            SetStatus("Busy — stop the current run first.");
            return;
        }

        if (_promptHistory.LastOrDefault() != code)
        {
            _promptHistory.Add(code);
        }

        _historyIndex = _promptHistory.Count;
        ConsolePrompt.Clear();
        _ = RunPromptAsync(code);
    }

    private async Task RunPromptAsync(string code)
    {
        // Echo the statement into the scrollback so the transcript reads like a session, not like a
        // list of unattributed results.
        foreach (string line in code.Split('\n'))
        {
            AppendConsole(">> " + line.TrimEnd('\r'));
        }

        // 'analysis.jgs' at the prompt runs that file; 'disp(1)' runs as source. This is the same
        // disambiguation -r already does, reused rather than re-guessed.
        ResolvedStatement resolved = StartupStatement.Resolve(
            code, _workspace?.RootPath ?? Environment.CurrentDirectory);
        bool isFile = resolved.SourcePath is not null && resolved.Error is null;
        string language = isFile ? resolved.Language : _consoleLanguage;

        if (SessionFor(language) is not { } session)
        {
            AppendConsole($"--- {language} has no interactive console on this machine. ---");
            return;
        }

        if (!_session.TryBeginRun(language))
        {
            return;
        }

        _runOutputLines = 0;
        _runOutputTruncated = false;
        _cts = new System.Threading.CancellationTokenSource();
        ScriptRunResult result;
        try
        {
            result = await session
                .ExecuteAsync(isFile ? resolved.Code : code, resolved.SourcePath ?? string.Empty, _cts.Token)
                .ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            result = ScriptRunResult.Failed(ex.Message);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _session.EndRun();
        }

        ShowRunResult(result, announceSuccess: false);
        ConsolePrompt.Focus();
    }

    /// <summary>
    /// The live session for <paramref name="language"/>, created on first use, or null when that
    /// language has no console. Sessions are per language and outlive individual statements; that is
    /// the whole point.
    /// </summary>
    private IScriptSession? SessionFor(string language)
    {
        if (_sessions.TryGetValue(language, out IScriptSession? existing))
        {
            return existing;
        }

        if (!_engines.TryGetValue(language, out IScriptEngine? engine)
            || engine is not IScriptRepl repl
            || !engine.IsAvailable)
        {
            return null;
        }

        IScriptSession session = repl.CreateSession(SessionContext());
        _sessions[language] = session;
        return session;
    }

    /// <summary>
    /// The context a session keeps for its whole life. The path resolver reads <c>_workspace</c> when
    /// it is called rather than capturing it, so re-rooting the file browser also re-roots the
    /// prompt's <c>readcsv("data.csv")</c> without recreating the session and losing its variables.
    /// </summary>
    private ScriptContext SessionContext() => new(
        _output ??= new ConsoleOutput(this),
        ShowFigureOnUi,
        _workspace?.RootPath,
        path => _workspace is { } workspace ? workspace.Resolve(path, null) : path,
        new AppScriptFigureFiles(),
        _audio);

    /// <summary>
    /// Reports a finished run or statement in the console, the status bar and the Workspace pane.
    /// Shared by F5 and the prompt so both leave the window in the same state.
    /// </summary>
    private void ShowRunResult(ScriptRunResult result, bool announceSuccess)
    {
        VariablesList.ItemsSource = result.Variables;
        CallStackList.ItemsSource = null;

        if (!result.Success)
        {
            AppendConsole($"--- Failed: {result.Message} ---");
            SetStatus($"Failed: {result.Message}");
        }
        else
        {
            if (announceSuccess)
            {
                AppendConsole($"--- Done. {result.FiguresShown} figure(s) displayed. ---");
            }

            SetStatus($"Done — {result.FiguresShown} figure(s), {result.Variables.Count} variable(s).");
        }

        if (result.ExitCode is { } exitCode)
        {
            AppendConsole($"--- exit({exitCode}) — closing JGraph. ---");
            System.Windows.Application.Current?.Shutdown(exitCode);
        }
    }

    /// <summary>
    /// Empties every live session: variables go, the buffers they held are released, and the figure
    /// registry resets. This is the only point besides closing the window at which a long session
    /// gives its memory back, because nothing is discarded between statements any more.
    /// </summary>
    private void ClearWorkspaceSessions()
    {
        foreach (IScriptSession session in _sessions.Values)
        {
            session.Clear();
        }

        VariablesList.ItemsSource = null;
        AppendConsole("--- Workspace cleared. ---");
        SetStatus("Workspace cleared.");
    }

    /// <summary>Ends every session, killing the Python child process along with them.</summary>
    private void DisposeSessions()
    {
        foreach (IScriptSession session in _sessions.Values)
        {
            try
            {
                session.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or ObjectDisposedException)
            {
                // Shutdown must not be blocked by a session that is already broken.
            }
        }

        _sessions.Clear();
    }
}
