using System.Text.RegularExpressions;

namespace JGraph.Scripting.Editing;

/// <summary>
/// Comment and Uncomment, free of a window (App Designer window plan W9b, R2025b's <c>rtc/plugins/language/common/CommentCode</c>):
/// Ctrl+R puts <c>% </c> on each line of the selection at the lines' common indent - a line that is only white space is emptied and
/// takes the indent and the sign - and Ctrl+Shift+R (or Ctrl+T) takes <c>% </c>, or a bare <c>%</c>, off the front of each line, a line
/// that was only a comment sign becoming empty.
/// </summary>
public static class CommentRules
{
    /// <summary>MATLAB's comment sign.</summary>
    public const string CommentString = "%";

    /// <summary>What Comment writes (<c>getCommentAnnotation</c>): the sign and a space.</summary>
    public const string CommentAnnotation = CommentString + " ";

    private static readonly Regex OnlySign = new("^\\s*" + CommentString + "\\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex LeadingWhitespace = new(@"^\s*", RegexOptions.CultureInvariant);

    /// <summary>
    /// The lines a selection from (<paramref name="startLine"/>, <paramref name="startColumn"/>) to (<paramref name="endLine"/>, <paramref name="endColumn"/>),
    /// one-based, works on (<c>_getSelectionRangeForCursor</c>): a selection ending at a line's first column leaves that line out, unless the text ends there.
    /// </summary>
    public static (int First, int Last) LinesOf(int startLine, int startColumn, int endLine, int endColumn, bool endsAtTextEnd)
    {
        int last = endColumn == 1 && !endsAtTextEnd && endLine > startLine ? endLine - 1 : endLine;
        return (startLine, Math.Max(startLine, last));
    }

    /// <summary>
    /// The indent the comment signs go at (<c>_determineCommentLevel</c>): the least indent of the lines with text, 0 as soon as one has none,
    /// or the least indent of the white-space lines when every line is one.
    /// </summary>
    public static int CommentLevel(IReadOnlyList<string> lines, int first, int last)
    {
        ArgumentNullException.ThrowIfNull(lines);
        int blank = int.MaxValue;
        int text = int.MaxValue;
        for (int line = first; line <= last; line++)
        {
            if (line < 1 || line > lines.Count)
            {
                continue;
            }

            string content = lines[line - 1];
            int indent = EditorTabs.VisualLength(LeadingWhitespace.Match(content).Value);
            if (IsWhitespace(content))
            {
                blank = Math.Min(blank, indent);
            }
            else
            {
                if (indent == 0)
                {
                    return 0;
                }

                text = Math.Min(text, indent);
            }
        }

        return text != int.MaxValue ? text : blank == int.MaxValue ? 0 : blank;
    }

    /// <summary>Comments lines <paramref name="first"/> to <paramref name="last"/> (one-based) of <paramref name="lines"/> in place (<c>_commentLines</c>).</summary>
    public static void Comment(IList<string> lines, int first, int last)
    {
        ArgumentNullException.ThrowIfNull(lines);
        int level = CommentLevel([.. lines], first, last);
        for (int line = first; line <= last; line++)
        {
            if (line < 1 || line > lines.Count)
            {
                continue;
            }

            string content = lines[line - 1];
            if (IsWhitespace(content))
            {
                lines[line - 1] = EditorTabs.Spaces(level) + CommentAnnotation;
            }
            else
            {
                int at = EditorTabs.IndexAtVisualColumn(content, level, afterTab: true);
                lines[line - 1] = content[..at] + CommentAnnotation + content[at..];
            }
        }
    }

    /// <summary>Uncomments lines <paramref name="first"/> to <paramref name="last"/> (one-based) of <paramref name="lines"/> in place (<c>_uncommentLines</c>).</summary>
    public static void Uncomment(IList<string> lines, int first, int last)
    {
        ArgumentNullException.ThrowIfNull(lines);
        for (int line = first; line <= last; line++)
        {
            if (line < 1 || line > lines.Count)
            {
                continue;
            }

            string content = lines[line - 1];
            string remove = StringToRemove(content);
            if (remove.Length == 0)
            {
                continue;
            }

            if (OnlySign.IsMatch(content))
            {
                lines[line - 1] = string.Empty;
                continue;
            }

            int at = content.IndexOf(remove, StringComparison.Ordinal);
            lines[line - 1] = content[..at] + content[(at + remove.Length)..];
        }
    }

    /// <summary>What Uncomment takes off a line (<c>_getStringToRemove</c>): <c>% </c> when the text starts so, else <c>%</c>, else nothing.</summary>
    public static string StringToRemove(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        string trimmed = line.TrimStart();
        return trimmed.StartsWith(CommentAnnotation, StringComparison.Ordinal) ? CommentAnnotation
            : trimmed.StartsWith(CommentString, StringComparison.Ordinal) ? CommentString
            : string.Empty;
    }

    private static bool IsWhitespace(string text) => text.Length == 0 || text.All(char.IsWhiteSpace);
}
