using System.Text;

namespace JGraph.Scripting.Analysis;

/// <summary>
/// Applies a Code Analyzer fix to a text (App Designer window plan W10): the editor's Fix and
/// <c>codeIssues.fix</c> both come here. Edits are applied from the end of the text backwards, so each one's
/// columns are the columns the analyzer measured; a line's deletion takes its line break with it.
/// </summary>
public static class CodeFixer
{
    /// <summary>The text with one fix applied.</summary>
    public static string Apply(string text, CodeFix fix) => Apply(text, [fix]);

    /// <summary>The text with every fix applied, each where the analyzer measured it in the text given.</summary>
    public static string Apply(string text, IEnumerable<CodeFix> fixes)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(fixes);
        List<(string Content, string Ending)> lines = SplitKeepingEndings(text);
        IEnumerable<CodeEdit> edits = fixes.SelectMany(static f => f.Edits)
            .OrderByDescending(static e => e.Line)
            .ThenByDescending(static e => e.StartColumn)
            .ThenByDescending(static e => e.EndColumn);
        var deleted = new HashSet<int>();
        foreach (CodeEdit edit in edits)
        {
            int index = edit.Line - 1;
            if (index < 0 || index >= lines.Count)
            {
                continue;
            }

            if (edit.StartColumn == 0)
            {
                deleted.Add(index);
                continue;
            }

            (string content, string ending) = lines[index];
            int start = Math.Clamp(edit.StartColumn - 1, 0, content.Length);
            int end = Math.Clamp(edit.EndColumn, start, content.Length);
            lines[index] = (content[..start] + edit.Replacement + content[end..], ending);
        }

        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < lines.Count; i++)
        {
            if (deleted.Contains(i))
            {
                continue;
            }

            sb.Append(lines[i].Content).Append(lines[i].Ending);
        }

        return sb.ToString();
    }

    private static List<(string Content, string Ending)> SplitKeepingEndings(string text)
    {
        var lines = new List<(string, string)>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                int contentEnd = i > start && text[i - 1] == '\r' ? i - 1 : i;
                lines.Add((text[start..contentEnd], text[contentEnd..(i + 1)]));
                start = i + 1;
            }
        }

        if (start < text.Length)
        {
            lines.Add((text[start..], string.Empty));
        }

        return lines;
    }
}
