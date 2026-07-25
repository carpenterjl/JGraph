using JGraph.Scripting;

namespace JGraph.Application.Scripting;

/// <summary>
/// The console pane's write path. Output is coalesced off the interpreter thread and flushed to the
/// TextBox in batches, with a per-run line budget and a total character cap so a print-heavy loop
/// can neither block the interpreter nor grow the UI without bound.
/// </summary>
public partial class ScriptWorkspaceWindow
{
    /// <summary>
    /// Queues host/status text for the console. Callers may be on any thread; the text is buffered and
    /// flushed to the UI in a coalesced batch, so this never blocks the caller. Host messages (run
    /// banners, status) are always shown — only script output is subject to the runaway-output budget.
    /// </summary>
    private void AppendConsole(string text, bool newline = true) => EnqueueConsole(text, newline);

    /// <summary>
    /// Queues one piece of script (stdout) output. Called from the interpreter's background thread via
    /// <see cref="ConsoleOutput"/>. Subject to the per-run line budget so a print-heavy loop can't grow
    /// the pending buffer without bound; once tripped, a single notice is emitted and the rest dropped.
    /// </summary>
    private void AppendScriptOutput(string text, bool newline)
    {
        if (_runOutputTruncated)
        {
            return;
        }

        if (newline && ++_runOutputLines > MaxRunOutputLines)
        {
            _runOutputTruncated = true;
            EnqueueConsole(
                $"(output truncated — this run produced more than {MaxRunOutputLines:N0} lines; " +
                "wrap prints in a condition or reduce the loop)",
                newline: true);
            return;
        }

        EnqueueConsole(text, newline);
    }

    private void EnqueueConsole(string text, bool newline)
    {
        bool scheduleFlush;
        lock (_consoleLock)
        {
            _pendingConsole.Append(text);
            if (newline)
            {
                _pendingConsole.Append(Environment.NewLine);
            }

            scheduleFlush = !_consoleFlushScheduled;
            _consoleFlushScheduled = true;
        }

        // BeginInvoke (never Invoke): the caller — often the interpreter thread — must not block on the
        // UI. While this flush is pending or running, further writes just accumulate in the buffer, so
        // one flush drains everything queued in the meantime: a million writes collapse to a few flushes.
        if (scheduleFlush)
        {
            Dispatcher.BeginInvoke(FlushConsole);
        }
    }

    private void FlushConsole()
    {
        string batch;
        lock (_consoleLock)
        {
            _consoleFlushScheduled = false;
            if (_pendingConsole.Length == 0)
            {
                return;
            }

            batch = _pendingConsole.ToString();
            _pendingConsole.Clear();
        }

        ConsoleBox.AppendText(batch);
        TrimConsole();
        ConsoleBox.ScrollToEnd();
    }

    /// <summary>Caps the console TextBox at <see cref="MaxConsoleChars"/>, dropping the oldest lines.</summary>
    private void TrimConsole()
    {
        string text = ConsoleBox.Text;
        if (text.Length <= MaxConsoleChars)
        {
            return;
        }

        // Drop the oldest characters (this also removes any previous trim marker, which sits at the
        // very front) and realign to the next line boundary so the retained text starts on a clean line.
        int cut = text.Length - MaxConsoleChars;
        int newline = text.IndexOf('\n', cut);
        cut = newline >= 0 ? newline + 1 : cut;

        ConsoleBox.Text = "⋯ earlier output trimmed ⋯" + Environment.NewLine + text[cut..];
        ConsoleBox.CaretIndex = ConsoleBox.Text.Length;
    }

    /// <summary>Bridges the engine's <see cref="IScriptOutput"/> onto the window's thread-safe console.</summary>
    private sealed class ConsoleOutput : IScriptOutput
    {
        private readonly ScriptWorkspaceWindow _window;

        public ConsoleOutput(ScriptWorkspaceWindow window) => _window = window;

        public void Write(string text) => _window.AppendScriptOutput(text, newline: false);

        public void WriteLine(string text) => _window.AppendScriptOutput(text, newline: true);

        public void WriteError(string text) => _window.AppendScriptOutput(text, newline: true);
    }
}
