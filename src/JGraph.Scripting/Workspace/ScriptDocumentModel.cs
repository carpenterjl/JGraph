using System.IO;

namespace JGraph.Scripting.Workspace;

/// <summary>
/// The UI-free state of one open script document: its file identity, its language (derived from the
/// file extension), its current text, and whether that text differs from what is saved. The WPF
/// document view binds to this so the logic is testable without a UI.
/// </summary>
public sealed class ScriptDocumentModel
{
    private string _savedText;
    private string _text;

    /// <summary>Opens a document over a file's <paramref name="path"/> and saved <paramref name="text"/>.</summary>
    public ScriptDocumentModel(string? path, string text)
    {
        FilePath = path;
        _savedText = text;
        _text = text;
        Language = LanguageForFile(path);
    }

    /// <summary>The document's file path, or null while it is unsaved.</summary>
    public string? FilePath { get; private set; }

    /// <summary>The file name shown on the tab ("Untitled" while unsaved).</summary>
    public string FileName => FilePath is null ? "Untitled" : Path.GetFileName(FilePath);

    /// <summary>The scripting language, decided by the file extension (JGS when unknown).</summary>
    public string Language { get; private set; }

    /// <summary>The current buffer text.</summary>
    public string Text => _text;

    /// <summary>Whether the buffer differs from the last saved text.</summary>
    public bool IsDirty => !string.Equals(_text, _savedText, StringComparison.Ordinal);

    /// <summary>Maps a file path to its scripting language: .jgs → JGS, .csx/.cs → C#, .py → Python;
    /// other extensions are plain "Text" (no engine, no highlighting). An unsaved document (null path)
    /// is a JGS scratch script.</summary>
    public static string LanguageForFile(string? path) => path is null
        ? "JGS"
        : Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jgs" => "JGS",
            ".csx" or ".cs" => "C#",
            ".py" => "Python",
            _ => "Text",
        };

    /// <summary>Updates the buffer text (typically from the editor's TextChanged).</summary>
    public void SetText(string text) => _text = text ?? string.Empty;

    /// <summary>Marks the current text as saved.</summary>
    public void MarkSaved() => _savedText = _text;

    /// <summary>Re-homes the document to <paramref name="path"/> (Save As), updating the language.</summary>
    public void SetFilePath(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        FilePath = path;
        Language = LanguageForFile(path);
    }
}
