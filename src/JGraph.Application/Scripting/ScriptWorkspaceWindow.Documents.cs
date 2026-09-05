using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AvalonDock.Layout;
using JGraph.Application.Services;
using JGraph.Controls.Scripting;
using JGraph.Scripting;
using JGraph.Scripting.Workspace;
using Microsoft.Win32;

namespace JGraph.Application.Scripting;

/// <summary>
/// Script tabs: creating them (New Script, Open, a restored session), the one-entry-per-file
/// bookkeeping that keeps a document's dock tab, editor control and UI-free model together, and
/// saving.
/// </summary>
public partial class ScriptWorkspaceWindow
{
    /// <summary>
    /// Fills the New Script menu with one entry per registered engine, in registration order, plus a
    /// plain text file. An engine with no runtime (Python, typically) is listed but disabled with the
    /// same explanation Run gives — hiding it outright would leave the user guessing why.
    /// </summary>
    private void BuildNewScriptMenu(IReadOnlyList<IScriptEngine> engines)
    {
        foreach (IScriptEngine engine in engines)
        {
            NewScriptMenu.Items.Add(new MenuItem
            {
                Header = $"{engine.Language} script ({ScriptDocumentModel.ExtensionForLanguage(engine.Language)})",
                Command = WorkspaceCommands.NewScript,
                CommandParameter = engine.Language,
                IsEnabled = engine.IsAvailable,
                ToolTip = engine.IsAvailable ? null : PythonScriptEngine.UnavailableMessage,
            });
        }

        NewScriptMenu.Items.Add(new Separator());
        NewScriptMenu.Items.Add(new MenuItem
        {
            Header = "Text file (.txt)",
            Command = WorkspaceCommands.NewScript,
            CommandParameter = "Text",
        });
    }

    /// <summary>The language a blank New Script opens in: the user's preference when it names an
    /// available engine, otherwise MATLAB — the same default the console prompt starts on.</summary>
    private string DefaultNewScriptLanguage()
    {
        string? preferred = _settings?.Current.DefaultNewScriptLanguage;
        return preferred is not null && _engines.ContainsKey(preferred) ? preferred
            : _engines.ContainsKey(DefaultConsoleLanguage) ? DefaultConsoleLanguage
            : "JGS";
    }

    /// <summary>The folder open/save dialogs start in when no workspace is open, from the user's settings.</summary>
    private string? DefaultScriptDirectory()
    {
        string? directory = _settings?.Current.DefaultScriptDirectory;
        return string.IsNullOrWhiteSpace(directory) ? null : directory;
    }

    private void OpenNewScript(string language)
    {
        // A near-blank stub (M21): a comment header the user fills in, dated at creation time. Text
        // documents get nothing — there is no comment syntax to write it in.
        string? comment = language switch
        {
            "JGS" or "C#" => "//",
            "MATLAB" => "%",
            "Python" => "#",
            _ => null,
        };
        string stub = comment is null
            ? string.Empty
            : $"""
                {comment} <description>
                {comment} Created by:
                {comment} Date: {DateTime.Now:yyyy-MM-dd}

                """;
        var model = new ScriptDocumentModel(path: null, stub, language);
        AddDocument(model, activate: true);
    }

    private void PromptOpenFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open script",
            Filter = "Scripts (*.jgs;*.m;*.csx;*.cs;*.py)|*.jgs;*.m;*.csx;*.cs;*.py|All files (*.*)|*.*",
            InitialDirectory = _workspace?.RootPath ?? DefaultScriptDirectory(),
        };
        if (dialog.ShowDialog(this) == true)
        {
            OpenDocument(dialog.FileName);
        }
    }

    private void OpenDocument(string path)
    {
        DocumentEntry? existing = _documents.FirstOrDefault(
            d => string.Equals(d.Model.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Document.IsActive = true;
            return;
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"Could not open '{path}': {ex.Message}");
            return;
        }

        AddDocument(new ScriptDocumentModel(path, text), activate: true);
    }

    private DocumentEntry AddDocument(ScriptDocumentModel model, bool activate)
    {
        var editor = new ScriptEditorControl { ScriptLanguage = model.Language, ScriptText = model.Text };
        var document = new LayoutDocument
        {
            Title = model.FileName,
            ContentId = model.FilePath,
            Content = editor,
        };

        var entry = new DocumentEntry(document, editor, model);
        editor.TextChanged += (_, _) =>
        {
            model.SetText(editor.ScriptText);
            entry.Document.Title = model.FileName + (model.IsDirty ? " *" : string.Empty);
        };
        document.Closing += (_, e) => e.Cancel = !CanCloseDocument(entry);
        document.Closed += (_, _) =>
        {
            RememberBreakpoints(entry);
            _documents.Remove(entry);
        };

        // Restore this file's persisted breakpoints, and keep the debugger + persistence in sync
        // whenever the user toggles one.
        if (model.FilePath is not null
            && _persistedBreakpoints.TryGetValue(model.FilePath, out List<int>? persisted))
        {
            editor.SetBreakpoints(persisted);
        }

        editor.BreakpointsChanged += (_, _) =>
        {
            RememberBreakpoints(entry);
            _debugSession?.SetBreakpoints(SourceIdOf(entry), entry.Editor.Breakpoints);
        };

        editor.SetNextStatementRequested += (_, line) => RequestSetNextStatement(entry, line);
        editor.OpenSymbolRequested += (_, name) => OpenSymbol(entry, name);
        editor.CompletionWorkspaceSymbols = () => HarvestWorkspaceSymbols(entry);
        editor.CompletionWorkspaceFiles = () => _workspace is null
            ? Array.Empty<JGraph.Scripting.Completion.WorkspaceFileEntry>()
            : JGraph.Scripting.Completion.PathCompletion.Flatten(_workspace.EnumerateAll());

        // A document opened while a debug run is active (e.g. a run()-included file the debugger just
        // paused in) is executing exactly what is on disk — that text is its live-edit baseline.
        if (_debugSession is not null)
        {
            entry.DebugBaseline = model.Text;
        }

        _documents.Add(entry);
        GetDocumentPane()?.Children.Add(document);
        if (activate)
        {
            document.IsActive = true;
            editor.FocusEditor();
        }

        return entry;
    }

    private void RememberBreakpoints(DocumentEntry entry)
    {
        if (entry.Model.FilePath is not string path)
        {
            return; // unsaved documents keep breakpoints only for the current session
        }

        if (entry.Editor.Breakpoints.Count == 0)
        {
            _persistedBreakpoints.Remove(path);
        }
        else
        {
            _persistedBreakpoints[path] = entry.Editor.Breakpoints.OrderBy(static l => l).ToList();
        }
    }

    private LayoutDocumentPane? GetDocumentPane() =>
        DockManager.Layout.Descendents().OfType<LayoutDocumentPane>().FirstOrDefault();

    /// <summary>
    /// The tab the user is looking at, which is what Run, Save, Close Tab and Toggle Breakpoint all
    /// act on. <c>IsActive</c> alone is not that tab: AvalonDock keeps one active content for the
    /// whole layout, so clicking into the console or the Files pane takes it away from every
    /// document, and a restored layout never sets it at all. Falling straight through to the first
    /// document then aimed the window's primary actions at whichever tab happened to be first —
    /// running the wrong script, and closing the wrong tab. <c>IsSelected</c> is the per-pane notion
    /// and is the tab actually on screen.
    /// </summary>
    private DocumentEntry? ActiveDocument =>
        _documents.FirstOrDefault(d => d.Document.IsActive)
        ?? _documents.FirstOrDefault(d => d.Document.IsSelected)
        ?? _documents.FirstOrDefault();

    /// <summary>
    /// The script the user is looking at, for a bug report to attach. Public because the crash
    /// guard lives in <c>App</c> and <see cref="ActiveDocument"/> deliberately does not.
    /// </summary>
    public BugReportScriptSnapshot? GetActiveScriptSnapshot() =>
        ActiveDocument is { } entry ? new(entry.Model.FileName, entry.Editor.ScriptText) : null;

    /// <summary>Set once every dirty document has been dealt with in <c>OnClosing</c>, so the
    /// per-tab Closing handlers do not re-prompt while the window tears its documents down.</summary>
    private bool _shutdownApproved;

    private void SaveActive()
    {
        if (ActiveDocument is { } entry)
        {
            TrySave(entry);
        }
    }

    private void SaveAsActive()
    {
        if (ActiveDocument is { } entry)
        {
            TrySaveAs(entry);
        }
    }

    /// <summary>Saves <paramref name="entry"/> to its own path, prompting for one when it has none.
    /// False means the document is still unsaved — the dialog was cancelled or the write failed —
    /// which a pending close must treat as "do not close".</summary>
    private bool TrySave(DocumentEntry entry) =>
        entry.Model.FilePath is null
            ? TrySaveAs(entry)
            : TryWriteDocument(entry, entry.Model.FilePath) is not SaveOutcome.Failed;

    /// <summary>Save As: always prompts, writes first, and only re-homes the document (path, language,
    /// tab identity) once the write has actually succeeded.</summary>
    private bool TrySaveAs(DocumentEntry entry)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save script",
            Filter = "JGS script (*.jgs)|*.jgs|MATLAB script (*.m)|*.m|C# script (*.csx)|*.csx|"
                + "Python script (*.py)|*.py|All files (*.*)|*.*",
            InitialDirectory = Path.GetDirectoryName(entry.Model.FilePath)
                ?? _workspace?.RootPath ?? DefaultScriptDirectory(),

            // The tab is already named for the language it was created as ("NewScript.py"), so the
            // dialog only has to agree with it — name and filter both follow the document.
            FileName = entry.Model.FileName,
            FilterIndex = entry.Model.Language switch
            {
                "JGS" => 1,
                "MATLAB" => 2,
                "C#" => 3,
                "Python" => 4,
                _ => 5,
            },
        };
        if (dialog.ShowDialog(this) != true)
        {
            return false;
        }

        // Diverted means the read-only prompt sent the user round Save As again and the document is
        // already homed at the writable copy that inner call wrote. Re-homing it here would rename the
        // tab to the read-only path nothing was written to, mark it clean, and aim the next Ctrl+S at
        // the very file the user chose not to overwrite.
        switch (TryWriteDocument(entry, dialog.FileName))
        {
            case SaveOutcome.Failed:
                return false;
            case SaveOutcome.Diverted:
                return true;
            default:
                break;
        }

        entry.Model.SetFilePath(dialog.FileName);
        entry.Editor.ScriptLanguage = entry.Model.Language;
        entry.Document.ContentId = dialog.FileName;
        entry.Document.Title = entry.Model.FileName;
        return true;
    }

    /// <summary>
    /// The one place document text reaches disk. A read-only target is surfaced rather than swallowed:
    /// the user can strip the attribute, divert to a writable copy, or abort. Any remaining failure
    /// gets a dialog, not just a status-bar line a close prompt would race past.
    /// </summary>
    private SaveOutcome TryWriteDocument(DocumentEntry entry, string path)
    {
        try
        {
            if (File.Exists(path))
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    MessageBoxResult choice = MessageBox.Show(this,
                        $"'{Path.GetFileName(path)}' is read-only, so it cannot be saved as it stands.\n\n"
                        + "Yes removes the read-only attribute and saves.\n"
                        + "No saves a writable copy under a different name.\n"
                        + "Cancel does not save.",
                        "Read-only file", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                    switch (choice)
                    {
                        case MessageBoxResult.Yes:
                            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
                            break;
                        case MessageBoxResult.No:
                            return TrySaveAs(entry) ? SaveOutcome.Diverted : SaveOutcome.Failed;
                        default:
                            return SaveOutcome.Failed;
                    }
                }
            }

            File.WriteAllText(path, entry.Editor.ScriptText);
            entry.Model.SetText(entry.Editor.ScriptText);
            entry.Model.MarkSaved();
            entry.Document.Title = entry.Model.FileName;
            SetStatus($"Saved {path}");
            return SaveOutcome.Written;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"Could not save: {ex.Message}");
            MessageBox.Show(this, $"Could not save '{path}'.\n\n{ex.Message}",
                "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return SaveOutcome.Failed;
        }
    }

    /// <summary>What a write attempt did, which the caller needs because one of the three answers —
    /// the read-only prompt's "save a writable copy" — has already re-homed the document itself.</summary>
    private enum SaveOutcome
    {
        /// <summary>Nothing was written; the document is still unsaved.</summary>
        Failed,

        /// <summary>The text reached the requested path.</summary>
        Written,

        /// <summary>The text reached a different path, and the document is already homed there.</summary>
        Diverted,
    }

    /// <summary>The per-tab close gate: once app-wide shutdown has already settled every dirty
    /// document, the teardown of individual tabs must not ask again.</summary>
    private bool CanCloseDocument(DocumentEntry entry) => _shutdownApproved || ConfirmDiscardOrSave(entry);

    /// <summary>
    /// The gate every close path runs through: true means the document may close. A clean document
    /// passes silently; a dirty one asks — Yes saves (a failed or cancelled save keeps it open),
    /// No discards the edits, Cancel keeps it open.
    /// </summary>
    private bool ConfirmDiscardOrSave(DocumentEntry entry)
    {
        if (!entry.Model.IsDirty)
        {
            return true;
        }

        // A never-saved document is not restored next session, so discarding it loses it entirely —
        // say so, rather than letting "No" sound like it only rewinds a few edits.
        string consequence = entry.Model.FilePath is null
            ? "It has never been saved, so its whole content will be lost."
            : "Its unsaved changes will be lost.";
        MessageBoxResult choice = MessageBox.Show(this,
            $"Save changes to '{entry.Model.FileName}'?\n\n"
            + $"{consequence}\n\n"
            + "Yes saves, No closes without saving, Cancel keeps it open.",
            "Unsaved changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        return choice switch
        {
            MessageBoxResult.Yes => TrySave(entry),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    /// <summary>One open document: its dock tab, its editor control, and its UI-free model.</summary>
    private sealed class DocumentEntry
    {
        public DocumentEntry(LayoutDocument document, ScriptEditorControl editor, ScriptDocumentModel model)
        {
            Document = document;
            Editor = editor;
            Model = model;
        }

        public LayoutDocument Document { get; private set; }

        public ScriptEditorControl Editor { get; }

        public ScriptDocumentModel Model { get; }

        /// <summary>The document text the active debug run is executing (or that the last applied live
        /// edit installed) — the reference point for detecting pending edits while paused.</summary>
        public string? DebugBaseline { get; set; }

        /// <summary>Points the entry at the <paramref name="document"/> a layout restore created,
        /// rewiring the close handlers the original tab carried.</summary>
        public void Rebind(LayoutDocument document, Func<bool> canClose, Action onClosed, out ScriptEditorControl editor)
        {
            Document = document;
            document.Title = Model.FileName;
            document.Closing += (_, e) => e.Cancel = !canClose();
            document.Closed += (_, _) => onClosed();
            editor = Editor;
        }
    }
}
