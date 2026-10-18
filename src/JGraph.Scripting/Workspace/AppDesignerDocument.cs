using System.IO;
using System.Text.RegularExpressions;
using JGraph.Scripting.Designer;
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
        + "The app runs with the change, here and in MATLAB. A change to createComponents - a component "
        + "added, removed or set differently - is written into the app's component tree as well, so MATLAB's "
        + "App Designer shows it on its canvas; every component the change did not touch stays as App "
        + "Designer saved it. App Designer writes its own version of the rest of that code (the component "
        + "properties block, the constructor, delete, the callbacks' signatures) the next time it saves "
        + "the app. Code in the shaded lines is App Designer's; the unshaded lines - your properties and "
        + "methods, and the bodies of the callbacks - are kept.\n\n"
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
            MlappSaveResult result = JgsMlapp.WriteCode(copy, text);
            bool generated = !before.SameGeneratedCode(JgsMlappLayout.Of(text));
            result = result with { GeneratedCodeChanged = generated, Tree = generated ? result.Tree : null };
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
    /// What to add to "Saved ..." when a class that is not an app App Designer lays out was saved as
    /// a new <c>.mlapp</c> (open item 58).
    /// </summary>
    public const string CodeOnlyNote =
        " The file holds the code alone: it runs here and in MATLAB and opens here, but MATLAB's App Designer "
        + "cannot open it, since the class is not an app App Designer lays out (its createComponents could not "
        + "be read), so there is no component tree to write.";

    /// <summary>
    /// Saves <paramref name="text"/>, a class that was never an App Designer file's - a new app, an
    /// exported <c>.m</c> app, a class written by hand - as the new <c>.mlapp</c>
    /// <paramref name="target"/>. An app App Designer lays out is written whole, with the component
    /// tree and App Designer's copy of the code, so that App Designer opens it (designer plan DS6,
    /// <see cref="JgsMlapp.WriteApp"/>); any other class as its code alone (open item 58). The class
    /// takes the file's name, since a class file must carry the name of its class.
    /// </summary>
    /// <returns>The text as saved, which differs from <paramref name="text"/> when the class was renamed, and what was written.</returns>
    /// <exception cref="IOException">The text is not a class, the name is not one a class can have, or the file could not be written.</exception>
    public static (string Text, MlappWriteResult Result) SaveNew(string target, string text)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(text);
        string name = ClassNameFor(target);
        if (JgsMlappLayout.Of(text).ClassName.Length == 0)
        {
            throw new IOException(
                $"'{Path.GetFileName(target)}' would hold no class: an App Designer file is a class file, and this "
                + "document is not a class. Save it as a .m file instead.");
        }

        string saved = JgsMlapp.ExportedCode(text, name);
        return (saved, JgsMlapp.WriteApp(target, saved));
    }

    /// <summary>What to add to "Saved ..." after <see cref="SaveNew"/>.</summary>
    public static string NewFileNote(MlappWriteResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.HasDesign)
        {
            return CodeOnlyNote;
        }

        return result.Problems.Count == 0
            ? " MATLAB's App Designer opens it with its canvas."
            : $" MATLAB's App Designer opens it, but its canvas leaves out what the component tree cannot hold yet: {string.Join(" ", result.Problems)}";
    }

    /// <summary>
    /// The text of a new, blank app as App Designer makes one: a class <c>app1</c> (renamed for its
    /// file when it is saved) whose figure is its only component, its constructor and its
    /// <c>delete</c>. The component code is the designer's own writer's (DS1).
    /// </summary>
    public static string NewAppText()
    {
        DesignComponentClass figureClass = DesignCatalog.R2025b.ByType("matlab.ui.Figure")
            ?? throw new InvalidOperationException("The catalog has no figure.");
        var figure = new DesignComponent("UIFigure", figureClass);
        figure.Set("Position", NumericValue.Row(100, 100, 640, 480));
        figure.Set("Name", new CharValue("MATLAB App"));
        var design = new AppDesign(figure);
        var lines = new List<string> { "classdef app1 < matlab.apps.AppBase", string.Empty };
        lines.AddRange(DesignCodeWriter.PropertiesBlock(design));
        lines.Add(string.Empty);
        lines.Add("    % Component initialization");
        lines.Add("    methods (Access = private)");
        lines.Add(string.Empty);
        lines.AddRange(DesignCodeWriter.Method(design));
        lines.AddRange(
        [
            "    end",
            string.Empty,
            "    % App creation and deletion",
            "    methods (Access = public)",
            string.Empty,
            "        % Construct app",
            "        function app = app1",
            string.Empty,
            "            % Create UIFigure and components",
            "            createComponents(app)",
            string.Empty,
            "            % Register the app with App Designer",
            "            registerApp(app, app.UIFigure)",
            string.Empty,
            "            if nargout == 0",
            "                clear app",
            "            end",
            "        end",
            string.Empty,
            "        % Code that executes before app deletion",
            "        function delete(app)",
            string.Empty,
            "            % Delete UIFigure when app is deleted",
            "            delete(app.UIFigure)",
            "        end",
            "    end",
            "end",
        ]);
        return string.Join('\n', lines) + "\n";
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

        if (!result.GeneratedCodeChanged)
        {
            return string.Empty;
        }

        if (result.Tree is not { } tree)
        {
            return " The save changed code App Designer generates; App Designer will overwrite that change when it next saves the app.";
        }

        if (!tree.IsUpdated)
        {
            return " The save changed code App Designer generates, but App Designer's component tree could not be brought up "
                + $"to date ({string.Join(" ", tree.Problems)}): App Designer will show the components it had.";
        }

        var told = new List<string>();
        void Tell(int count, string what)
        {
            if (count > 0)
            {
                told.Add($"{count} {what}");
            }
        }

        Tell(tree.Changed.Count + tree.Rebuilt.Count, tree.Changed.Count + tree.Rebuilt.Count == 1 ? "component changed" : "components changed");
        Tell(tree.Added.Count, "added");
        Tell(tree.Removed.Count, "removed");
        string what = told.Count == 0 ? string.Empty : $" ({string.Join(", ", told)})";
        string problems = tree.Problems.Count == 0 ? string.Empty
            : $" What the tree cannot hold yet is left out of App Designer's canvas: {string.Join(" ", tree.Problems)}";
        return $" App Designer's component tree was brought up to date{what}; every other component is as App Designer saved it.{problems}";
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
