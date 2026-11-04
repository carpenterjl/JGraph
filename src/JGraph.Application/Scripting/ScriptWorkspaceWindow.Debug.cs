using System.IO;
using System.Linq;
using System.Windows.Input;
using JGraph.Scripting;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Jgs.Debug;
using JGraph.Scripting.Workspace;

namespace JGraph.Application.Scripting;

/// <summary>
/// The sessions' own debuggers (App Designer window plan W11): each JGS or MATLAB console session keeps a
/// breakpoint store - what its editors' margins show and set, what <c>dbstop</c>, <c>dbclear</c> and
/// <c>dbstatus</c> work on, what App Designer's code views share - and a debugger hooked into the
/// interpreter while the store has anything in it, which pauses whatever runs in the session: a run (F5),
/// a statement typed at the prompt, an app's callback fired from its window. The shell follows every
/// pause the same way it followed a one-shot debug run's: the <c>K&gt;&gt;</c> prompt, the Call Stack and
/// Workspace panes, the execution marker, Continue and the steps; a pause in an idle session's callback
/// marks the session paused and returns it to idle on resume.
/// </summary>
public partial class ScriptWorkspaceWindow
{
    /// <summary>Follows a session's debugger and seeds its store with what the editors and the saved layout hold.</summary>
    private void AttachDebugger(IScriptSession session)
    {
        if (session is not IScriptDebuggerHost jgs)
        {
            return;
        }

        jgs.Debugger.Paused += OnDebugPaused;
        jgs.Debugger.Resumed += OnDebugResumed;
        jgs.Breakpoints.Changed += (_, _) => Dispatcher.BeginInvoke(() => OnBreakpointStoreChanged(session, jgs.Breakpoints));

        // A store knows no language: the persisted files of every language go in, and a file that is
        // not this session's is never run by it, so its lines are never reached.
        foreach ((string file, List<int> lines) in _persistedBreakpoints)
        {
            foreach (int line in lines)
            {
                jgs.Breakpoints.Set(file, line);
            }
        }

        foreach (DocumentEntry document in _documents)
        {
            if (string.Equals(document.Model.Language, session.Language, StringComparison.Ordinal))
            {
                PushEditorBreakpoints(document, jgs.Breakpoints);
            }
        }
    }

    /// <summary>The breakpoint store a document's language's session keeps, the session made if need be; null for a language with none.</summary>
    private JgsBreakpointStore? StoreFor(DocumentEntry entry) => (SessionFor(entry.Model.Language) as IScriptDebuggerHost)?.Breakpoints;

    /// <summary>The debugger the Pause command and the steps act on: the paused one, else the running or active language's.</summary>
    private JgsDebugSession? ActiveDebugger
    {
        get
        {
            if (_debugSession is { } current)
            {
                return current;
            }

            string? language = _session.RunningLanguage ?? ActiveDocument?.Model.Language;
            return language is null ? null : (_sessions.GetValueOrDefault(language) as IScriptDebuggerHost)?.Debugger;
        }
    }

    /// <summary>The user toggled a line in an editor's margin: the store takes the editor's lines, each keeping its condition and state, a MATLAB file's lines snapped to code.</summary>
    private void OnEditorBreakpointsToggled(DocumentEntry entry)
    {
        if (StoreFor(entry) is { } store)
        {
            PushEditorBreakpoints(entry, store);
        }
    }

    private void PushEditorBreakpoints(DocumentEntry entry, JgsBreakpointStore store)
    {
        string file = SourceIdOf(entry);
        bool matlab = string.Equals(entry.Model.Language, "MATLAB", StringComparison.Ordinal);
        var kept = new List<JgsBreakpoint>();
        foreach (int shown in entry.Editor.Breakpoints.OrderBy(static l => l))
        {
            int line = shown;
            if (matlab && store.Find(file, shown) is null && DesignDebug.Snap(entry.Editor.ScriptText, shown) is int landed)
            {
                line = landed; // a line without code moves to the next executable one, as R2025b's does
            }

            if (kept.All(b => b.Line != line))
            {
                kept.Add(store.Find(file, line) ?? new JgsBreakpoint(file, line, string.Empty, Enabled: true));
            }
        }

        if (!kept.SequenceEqual(store.In(file)))
        {
            store.Replace(file, kept);
        }
        else
        {
            ShowStoreInEditors(store, file);
        }
    }

    /// <summary>The store changed (a margin, dbstop at the prompt, App Designer): every editor of the session's language shows it, and the layout remembers the files' lines.</summary>
    private void OnBreakpointStoreChanged(IScriptSession session, JgsBreakpointStore store)
    {
        foreach (DocumentEntry document in _documents)
        {
            if (string.Equals(document.Model.Language, session.Language, StringComparison.Ordinal))
            {
                ShowStoreInEditors(store, SourceIdOf(document));
            }
        }

        // The layout keeps every file's lines, from whichever session set them.
        foreach (string file in _persistedBreakpoints.Keys.ToList())
        {
            if (store.In(file).Count == 0 && !_documents.Any(d => string.Equals(d.Model.FilePath, file, StringComparison.OrdinalIgnoreCase) && !string.Equals(d.Model.Language, session.Language, StringComparison.Ordinal)))
            {
                _persistedBreakpoints.Remove(file);
            }
        }

        foreach (JgsBreakpoint breakpoint in store.All)
        {
            if (breakpoint.File.Length > 0)
            {
                _persistedBreakpoints[breakpoint.File] = store.In(breakpoint.File).Select(static b => b.Line).OrderBy(static l => l).ToList();
            }
        }

        CommandManager.InvalidateRequerySuggested();
    }

    private void ShowStoreInEditors(JgsBreakpointStore store, string file)
    {
        IReadOnlyList<JgsBreakpoint> held = store.In(file);
        foreach (DocumentEntry document in _documents)
        {
            if (string.Equals(SourceIdOf(document), file, StringComparison.OrdinalIgnoreCase))
            {
                document.Editor.SetBreakpoints(held.Select(static b => b.Line));
                document.Editor.SetBreakpointStates(held.Where(static b => !b.Enabled).Select(static b => b.Line), held.Where(static b => b.Condition.Length > 0).Select(static b => b.Line));
            }
        }
    }

    /// <summary>Quits a paused session debugger when nothing else can unwind the pause: Stop with no run token in flight.</summary>
    private void QuitPausedDebugger()
    {
        if (_debugSession is { IsPaused: true, IsAttached: true } paused)
        {
            paused.Quit();
        }
    }
}
