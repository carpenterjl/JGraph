using System.Text.RegularExpressions;

namespace JGraph.Scripting.Editing;

/// <summary>
/// Increase Indent and Decrease Indent, free of a window (App Designer window plan W9b, R2025b's <c>rtc/plugins/indentation/Indentation</c>):
/// Ctrl+] puts an indent's worth of spaces after each line's leading white space, Ctrl+[ takes a tab or up to an indent's worth of spaces
/// off each line's front; Tab does the first over a selection spanning lines, Shift+Tab the second.
/// </summary>
public static class IndentRules
{
    private static readonly Regex LeadingWhitespace = new(@"^\s*", RegexOptions.CultureInvariant);
    private static readonly Regex OneIndent = new("^(\\t| {1," + EditorTabs.IndentSize + "})", RegexOptions.CultureInvariant);

    /// <summary>Indents lines <paramref name="first"/> to <paramref name="last"/> (one-based) of <paramref name="lines"/> in place, those <paramref name="canEdit"/> allows (<c>_indentContainer</c>).</summary>
    /// <returns>Whether any line changed.</returns>
    public static bool Indent(IList<string> lines, int first, int last, Func<int, bool>? canEdit = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        bool changed = false;
        for (int line = first; line <= last; line++)
        {
            if (line < 1 || line > lines.Count || canEdit?.Invoke(line) == false)
            {
                continue;
            }

            string text = lines[line - 1];
            int at = LeadingWhitespace.Match(text).Length;
            lines[line - 1] = text[..at] + EditorTabs.Spaces(EditorTabs.IndentSize) + text[at..];
            changed = true;
        }

        return changed;
    }

    /// <summary>Unindents lines <paramref name="first"/> to <paramref name="last"/> (one-based) of <paramref name="lines"/> in place (<c>_unindentContainer</c>): a tab, or one to four spaces, off the front.</summary>
    /// <returns>Whether any line changed.</returns>
    public static bool Unindent(IList<string> lines, int first, int last, Func<int, bool>? canEdit = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        bool changed = false;
        for (int line = first; line <= last; line++)
        {
            if (line < 1 || line > lines.Count || canEdit?.Invoke(line) == false)
            {
                continue;
            }

            Match m = OneIndent.Match(lines[line - 1]);
            if (m.Success)
            {
                lines[line - 1] = lines[line - 1][m.Length..];
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// The lines a selection works on (<c>_getNodeRangeFromSelection</c> over <c>trimTrailingLineBreak</c>): from the line it starts on to the
    /// line it ends on, the last left out when the selection ends at its first column.
    /// </summary>
    public static (int First, int Last) LinesOf(int startLine, int endLine, int endColumn) =>
        (startLine, endLine > startLine && endColumn == 1 ? endLine - 1 : endLine);
}
