using System.Collections.Generic;
using System.IO;
using System.Linq;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Workspace;

namespace JGraph.Application.Scripting;

/// <summary>
/// Going to where a name is defined: the editor's <em>Open name</em> (Ctrl+D, as in MATLAB) and the
/// prompt's <c>edit name</c> / <c>open name</c>. The search order is MATLAB's: the file the name
/// was used in, then the other open tabs, then the workspace — a file named for the function first,
/// then any script that defines it — and finally the built-ins, which have no file to open but can
/// at least say what they are.
/// </summary>
public partial class ScriptWorkspaceWindow
{
    /// <summary>Opens the definition of <paramref name="name"/>, looking outward from <paramref name="from"/>.</summary>
    private void OpenSymbol(DocumentEntry? from, string name)
    {
        if (!FunctionLocator.IsIdentifier(name))
        {
            SetStatus($"'{name}' is not a name that can be opened.");
            return;
        }

        // The document the name was used in, then the other tabs — an unsaved fn is found at once.
        foreach (DocumentEntry entry in OrderedFrom(from))
        {
            if (FunctionLocator.FindDefinition(entry.Editor.ScriptText, entry.Model.Language, name) is int line)
            {
                entry.Document.IsActive = true;
                entry.Editor.GoToLine(line);
                SetStatus($"{name} — {entry.Model.FileName}, line {line}.");
                return;
            }
        }

        if (_workspace is { } workspace)
        {
            IReadOnlyList<WorkspaceEntry> scripts;
            try
            {
                scripts = workspace.EnumerateScripts();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                scripts = Array.Empty<WorkspaceEntry>();
            }

            // A file named for the function is its definition, whatever it holds (MATLAB's rule).
            if (scripts.FirstOrDefault(s => FunctionLocator.FileNameMatches(s.FullPath, name)) is { } named)
            {
                OpenDocumentAt(named.FullPath, line: 1);
                return;
            }

            foreach (WorkspaceEntry script in scripts)
            {
                if (_documents.Any(d => string.Equals(d.Model.FilePath, script.FullPath, StringComparison.OrdinalIgnoreCase)))
                {
                    continue; // already searched as an open tab, with its live text
                }

                try
                {
                    string text = File.ReadAllText(script.FullPath);
                    if (FunctionLocator.FindDefinition(text, ScriptDocumentModel.LanguageForFile(script.FullPath), name) is int line)
                    {
                        OpenDocumentAt(script.FullPath, line);
                        return;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // An unreadable script simply does not define it.
                }
            }
        }

        if (JgsBuiltinCatalog.Find(name) is { } builtin)
        {
            SetStatus($"'{name}' is a built-in function: {builtin.Signature} — {builtin.Summary}");
            return;
        }

        if (JgsBuiltinCatalog.IsUnsupportedMatlabFunction(name, out string what))
        {
            SetStatus($"'{name}' is a MATLAB function JGraph does not provide ({what}).");
            return;
        }

        SetStatus($"'{name}' not found.");
    }

    /// <summary>The open documents with <paramref name="first"/> (when given) ahead of the rest.</summary>
    private IEnumerable<DocumentEntry> OrderedFrom(DocumentEntry? first)
    {
        if (first is not null)
        {
            yield return first;
        }

        foreach (DocumentEntry entry in _documents)
        {
            if (entry != first)
            {
                yield return entry;
            }
        }
    }

    /// <summary>Opens (or activates) the document at <paramref name="path"/> and puts the caret on <paramref name="line"/>.</summary>
    private void OpenDocumentAt(string path, int line)
    {
        OpenDocument(path);
        DocumentEntry? entry = _documents.FirstOrDefault(d =>
            string.Equals(d.Model.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (entry is not null)
        {
            entry.Editor.GoToLine(line);
            SetStatus($"{entry.Model.FileName}, line {line}.");
        }
    }

    /// <summary>
    /// <c>edit</c> and <c>open</c> from the prompt. <c>edit</c> alone is a new script; a name that is
    /// a workspace variable opens in the Data Viewer (<c>open x</c> is MATLAB's <c>openvar</c>); a
    /// path, absolute or relative to the script's folder or the workspace root, with or without its
    /// extension, opens in the pane its kind belongs to; anything else is looked up as a function.
    /// </summary>
    private void RunEditCommand(EditPromptCommand command)
    {
        if (command.Argument is not { } argument)
        {
            OpenNewScript(DefaultNewScriptLanguage());
            return;
        }

        if (command.Verb == EditPromptVerb.Open
            && (VariablesList.ItemsSource as IEnumerable<ScriptVariable>)?
                .FirstOrDefault(v => string.Equals(v.Name, argument, StringComparison.Ordinal)) is { } variable)
        {
            OpenVariable(variable);
            return;
        }

        if (ResolveEditPath(argument) is { } path)
        {
            switch (WorkspaceFiles.Classify(path))
            {
                case WorkspaceFileKind.Data:
                    OpenDataFile(path);
                    break;
                case WorkspaceFileKind.Figure:
                    OpenGraphFile(path);
                    break;
                case WorkspaceFileKind.Document:
                    OpenDocument(path);
                    break;
                default:
                    SetStatus($"No viewer for '{Path.GetExtension(path)}' files.");
                    break;
            }

            return;
        }

        if (FunctionLocator.IsIdentifier(argument))
        {
            OpenSymbol(ActiveDocument, argument);
            return;
        }

        SetStatus($"'{argument}' not found.");
    }

    /// <summary>
    /// The existing file <paramref name="argument"/> names: as written, or with a script extension
    /// added (<c>edit foo</c> means <c>foo.m</c>, or whichever script called foo exists), resolved
    /// against the active script's folder and then the workspace root.
    /// </summary>
    private string? ResolveEditPath(string argument)
    {
        string? scriptDirectory = ActiveDocument?.Model.FilePath is { } current ? Path.GetDirectoryName(current) : null;
        IEnumerable<string> candidates = Path.HasExtension(argument)
            ? new[] { argument }
            : new[] { argument + ".m", argument + ".jgs", argument + ".py", argument + ".csx", argument + ".txt", argument };
        foreach (string candidate in candidates)
        {
            string resolved = _workspace is { } workspace
                ? workspace.Resolve(candidate, scriptDirectory)
                : scriptDirectory is not null && !Path.IsPathRooted(candidate)
                    ? Path.Combine(scriptDirectory, candidate)
                    : candidate;
            if (File.Exists(resolved))
            {
                return Path.GetFullPath(resolved);
            }
        }

        return null;
    }
}
