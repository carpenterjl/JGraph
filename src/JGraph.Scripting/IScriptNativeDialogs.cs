namespace JGraph.Scripting;

/// <summary>One line of a file dialog's type list: the patterns it admits, and what it is called.</summary>
/// <param name="Pattern">Wildcard patterns separated by semicolons, as in <c>*.m;*.mlx</c>.</param>
/// <param name="Description">The words shown for it, or empty to show the pattern itself.</param>
public sealed record ScriptFileFilter(string Pattern, string Description);

/// <summary>A font as <c>uisetfont</c> speaks of one.</summary>
/// <param name="Name">The family.</param>
/// <param name="SizePoints">The size, in points.</param>
/// <param name="Bold">Whether it is bold.</param>
/// <param name="Italic">Whether it is italic.</param>
public sealed record ScriptFontChoice(string Name, double SizePoints, bool Bold, bool Italic);

/// <summary>
/// The system's own dialogs, for the verbs that ask the user for a file, a folder, a colour or a
/// font (app-building plan, U4): <c>uigetfile</c>, <c>uiputfile</c>, <c>uigetdir</c>,
/// <c>uisetcolor</c>, <c>uisetfont</c> and the three built on them. The script thread calls these and
/// waits; an implementation shows its dialog on the thread its windows live on. Every method answers
/// null when the user cancels. A host with no windows installs none, and the verbs then refuse as
/// R2025b does without a display.
/// </summary>
public interface IScriptNativeDialogs
{
    /// <summary>Asks for one file, or several, to open.</summary>
    /// <param name="title">The dialog's title.</param>
    /// <param name="filters">The type list; never empty.</param>
    /// <param name="initialPath">A folder to start in, or a file to start on, or null.</param>
    /// <param name="multiSelect">Whether several files may be chosen.</param>
    /// <returns>The chosen files' full paths and the 1-based index of the type chosen, or null.</returns>
    (IReadOnlyList<string> Paths, int FilterIndex)? OpenFiles(
        string title, IReadOnlyList<ScriptFileFilter> filters, string? initialPath, bool multiSelect);

    /// <summary>Asks for a file to write.</summary>
    /// <returns>The chosen file's full path and the 1-based index of the type chosen, or null.</returns>
    (string Path, int FilterIndex)? SaveFile(string title, IReadOnlyList<ScriptFileFilter> filters, string? initialPath);

    /// <summary>Asks for a folder.</summary>
    string? PickFolder(string title, string? initialPath);

    /// <summary>Asks for a colour, as red, green and blue from 0 to 1.</summary>
    (double R, double G, double B)? PickColor(string title, (double R, double G, double B)? initial);

    /// <summary>Asks for a font.</summary>
    ScriptFontChoice? PickFont(string title, ScriptFontChoice? initial);
}
