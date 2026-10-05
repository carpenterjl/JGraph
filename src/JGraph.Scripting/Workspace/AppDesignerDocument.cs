using System.IO;
using System.Text.RegularExpressions;
using JGraph.Scripting.Jgs;

namespace JGraph.Scripting.Workspace;

/// <summary>
/// What the editor does with a document that is the code of an App Designer file (app-building
/// plan U7b, ADR 0205): save it back into the package, save the app under another name, export
/// its class as an <c>.m</c> file, and say what a save could not do. UI-free, so that the rules
/// are tested without a window.
/// </summary>
public static partial class AppDesignerDocument
{
    /// <summary>
    /// What the user is told, once, the first time a save changes code App Designer generates.
    /// </summary>
    public const string GeneratedCodeNotice =
        "This save changed code that App Designer generates from the app's components: the component "
        + "properties, createComponents, the constructor, delete, or a callback's name or signature.\n\n"
        + "The app runs with the change, here and in MATLAB. MATLAB's App Designer writes its own version "
        + "of that code the next time it saves the app, and the change is then lost. Code in the shaded "
        + "lines is App Designer's; the unshaded lines - your properties and methods, and the bodies of "
        + "the callbacks - are kept.\n\n"
        + "To keep an app that App Designer no longer manages, use File > Export to .m File.\n\n"
        + "This is said once.";

    /// <summary>
    /// Saves <paramref name="text"/> as the code of the App Designer file <paramref name="target"/>.
    /// When that is not the file the document was opened from, the app is saved under a new name
    /// as App Designer's Save As does it: the package is copied and its class takes the new file's
    /// name, since a class file must carry the name of its class.
    /// </summary>
    /// <param name="source">The <c>.mlapp</c> the document was opened from.</param>
    /// <param name="target">The <c>.mlapp</c> to save into.</param>
    /// <param name="text">The document's text.</param>
    /// <returns>The text as saved, which differs from <paramref name="text"/> when the class was renamed, and what the save did.</returns>
    /// <exception cref="InvalidDataException">The source is not a package with a code document.</exception>
    /// <exception cref="IOException">A file could not be read or written, or the new name is not one a class can have.</exception>
    public static (string Text, MlappSaveResult Result) Save(string source, string target, string text)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(text);

        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
        {
            return (text, JgsMlapp.WriteCode(target, text));
        }

        // The new name is App Designer's to write, not an edit to its code: what the save changed
        // is told against the old text under the new name.
        string name = ClassNameFor(target);
        JgsMlappLayout before = JgsMlappLayout.Of(JgsMlapp.ExportedCode(JgsMlapp.ReadCode(source), name));
        text = JgsMlapp.ExportedCode(text, name);

        // The copy is made beside the target and moved onto it once it holds the code, so a save
        // that fails leaves whatever was at the target as it was.
        string copy = target + ".copying";
        try
        {
            File.Copy(source, copy, overwrite: true);
            File.SetAttributes(copy, FileAttributes.Normal);
            MlappSaveResult result = JgsMlapp.WriteCode(copy, text) with
            {
                GeneratedCodeChanged = !before.SameGeneratedCode(JgsMlappLayout.Of(text)),
            };
            File.Move(copy, target, overwrite: true);
            return (text, result);
        }
        catch
        {
            try
            {
                File.Delete(copy);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The target is intact, which is what matters.
            }

            throw;
        }
    }

    /// <summary>
    /// Writes an app's class as the <c>.m</c> file <paramref name="path"/>, as MATLAB's "Export to
    /// .m File" does: the same text under the new file's name. The <c>.mlapp</c> is not touched.
    /// </summary>
    /// <returns>The text written.</returns>
    /// <exception cref="IOException">The file could not be written, or its name is not one a class can have.</exception>
    public static string Export(string path, string text)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(text);
        string exported = JgsMlapp.ExportedCode(text, ClassNameFor(path));
        File.WriteAllText(path, exported);
        return exported;
    }

    /// <summary>
    /// What to add to "Saved ..." when the save left something for the user to know; empty when it
    /// did not.
    /// </summary>
    public static string StatusNote(MlappSaveResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.HasDesignCopy)
        {
            return string.Empty;
        }

        if (!result.DesignCopyInStep)
        {
            return " The code runs as saved, but App Designer's own copy of it could not be brought up to date "
                + "(the code is not laid out as App Designer lays out an app, a function in the callbacks is not "
                + "closed, or the app keeps its design data in a form that cannot be rewritten): "
                + "App Designer will show the code it had.";
        }

        return result.GeneratedCodeChanged
            ? " The save changed code App Designer generates; App Designer will overwrite that change when it next saves the app."
            : string.Empty;
    }

    /// <summary>The class a code file at <paramref name="path"/> must define: its name, when that is a name a class can have.</summary>
    private static string ClassNameFor(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        return Identifier().IsMatch(name)
            ? name
            : throw new IOException(
                $"'{name}' cannot be the name of an app: the file is named for its class, and a class name "
                + "starts with a letter and holds only letters, digits and underscores.");
    }

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]*$")]
    private static partial Regex Identifier();
}
