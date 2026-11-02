namespace JGraph.Scripting.Editing;

/// <summary>
/// The editor's tab arithmetic (R2025b's <c>rtc/plugins/editortabservice/EditorTabService</c>): a tab reaches the next multiple of
/// the tab size, the Tab key inserts spaces, and the indent size is four (<c>matlab.editor.tab.*</c>).
/// </summary>
public static class EditorTabs
{
    /// <summary>The tab size (<c>matlab.editor.tab.TabSize</c>).</summary>
    public const int TabSize = 4;

    /// <summary>The indent size (<c>matlab.editor.tab.IndentSize</c>).</summary>
    public const int IndentSize = 4;

    /// <summary>The visual length of <paramref name="text"/>, each tab taking it to the next multiple of the tab size (<c>getLengthCompensatingTabs</c>).</summary>
    public static int VisualLength(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        int width = 0;
        foreach (char c in text)
        {
            width += c == '\t' ? TabSize - (width % TabSize) : 1;
        }

        return width;
    }

    /// <summary>
    /// The index in <paramref name="text"/> at which the visual column <paramref name="column"/> is reached (<c>getIndexForLengthCompensatingTabs</c>):
    /// a tab that straddles the column counts as before it, or after it when <paramref name="afterTab"/>; never past the last character.
    /// </summary>
    public static int IndexAtVisualColumn(string text, int column, bool afterTab = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        int e = 0;
        int n = 0;
        while (true)
        {
            int s = text.IndexOf('\t', e);
            if (s < 0)
            {
                break;
            }

            int c = n + (s - e);
            if (c >= column)
            {
                break;
            }

            int g = TabSize - (c % TabSize);
            if (c + g == column)
            {
                return s + 1;
            }

            if (c + g > column)
            {
                return afterTab ? s + 1 : s;
            }

            e = s + 1;
            n = c + g;
        }

        return Math.Min(e + (column - n), text.Length - 1);
    }

    /// <summary>Spaces of visual length <paramref name="length"/> (<c>getWhitespaceStringOfLength</c> with the Tab key inserting spaces).</summary>
    public static string Spaces(int length) => new(' ', Math.Max(0, length));
}
