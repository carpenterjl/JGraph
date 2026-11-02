using System.Xml.Linq;

namespace JGraph.Scripting.Editing;

/// <summary>
/// The editor's bookmarks, free of a window (R2025b's <c>rtc/plugins/bookmarks/Bookmarks</c>): a line is marked or unmarked by Ctrl+F2 unless
/// it is empty, F2 goes to the next marked line below and round to the first, Shift+F2 to the one above and round to the last, and an empty
/// line's mark does not count while it is empty.
/// </summary>
public static class BookmarkRules
{
    /// <summary>Marks or unmarks <paramref name="line"/> (zero-based) in <paramref name="marked"/> (<c>_toggleForNode</c>); an empty line is left as it is.</summary>
    /// <returns>Whether anything changed.</returns>
    public static bool Toggle(ISet<int> marked, int line, Func<int, bool> isBlank)
    {
        ArgumentNullException.ThrowIfNull(marked);
        ArgumentNullException.ThrowIfNull(isBlank);
        if (isBlank(line))
        {
            return false;
        }

        if (!marked.Remove(line))
        {
            marked.Add(line);
        }

        return true;
    }

    /// <summary>
    /// The line the next (or, <paramref name="previous"/>, the previous) bookmark is on (<c>_jumpToNextBookmark</c>), from <paramref name="current"/>
    /// among <paramref name="lineCount"/> lines, or null with none: the lines below the current one and then from the top; above it and then from
    /// the bottom down to the current line itself. A marked line that is empty is passed over.
    /// </summary>
    public static int? Next(IReadOnlySet<int> marked, int lineCount, int current, bool previous, Func<int, bool> isBlank)
    {
        ArgumentNullException.ThrowIfNull(marked);
        ArgumentNullException.ThrowIfNull(isBlank);
        if (marked.Count == 0)
        {
            return null;
        }

        bool Marked(int line) => marked.Contains(line) && line >= 0 && line < lineCount && !isBlank(line);
        return previous
            ? InRange(current, 0, Marked) ?? InRange(lineCount, current, Marked)
            : InRange(current, lineCount, Marked) ?? InRange(-1, current, Marked);
    }

    /// <summary><c>_findNextBookmarkInLineRange</c>: upward from <paramref name="from"/> (excluded) to <paramref name="to"/> (excluded), or downward to <paramref name="to"/> included.</summary>
    private static int? InRange(int from, int to, Func<int, bool> marked)
    {
        if (from <= to)
        {
            for (int line = from + 1; line < to; line++)
            {
                if (marked(line))
                {
                    return line;
                }
            }
        }
        else
        {
            for (int line = from - 1; line >= to; line--)
            {
                if (marked(line))
                {
                    return line;
                }
            }
        }

        return null;
    }
}

/// <summary>
/// Where the bookmarks of every file are kept between sessions (R2025b's <c>BookmarksPreferences</c>): <c>bookmarks.xml</c> in the preferences
/// folder, a <c>file</c> element per path with a <c>line</c> element per mark, each line a zero-based index.
/// </summary>
public sealed class BookmarkStore
{
    /// <summary>The file's name.</summary>
    public const string FileName = "bookmarks.xml";

    private readonly string _path;

    /// <summary>A store at <paramref name="path"/>.</summary>
    public BookmarkStore(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        _path = path;
    }

    /// <summary>The store's file.</summary>
    public string Path => _path;

    /// <summary>The marked lines (zero-based) kept for <paramref name="filePath"/>, in the order kept; none when the store cannot be read.</summary>
    public IReadOnlyList<int> Read(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        XElement? root = Load();
        XElement? file = root?.Elements("file").FirstOrDefault(f => (string?)f.Attribute("absolutepath") == filePath);
        if (file is null)
        {
            return [];
        }

        var lines = new List<int>();
        foreach (XElement line in file.Elements("line"))
        {
            if (int.TryParse((string?)line.Attribute("number"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int number))
            {
                lines.Add(number);
            }
        }

        return lines;
    }

    /// <summary>Keeps <paramref name="lines"/> (zero-based) for <paramref name="filePath"/>, in place of what was kept.</summary>
    public void Write(string filePath, IEnumerable<int> lines)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(lines);
        XElement root = Load() ?? new XElement("bookmarks");
        XElement? file = root.Elements("file").FirstOrDefault(f => (string?)f.Attribute("absolutepath") == filePath);
        if (file is null)
        {
            file = new XElement("file", new XAttribute("absolutepath", filePath));
            root.Add(file);
        }

        file.RemoveNodes();
        foreach (int line in lines)
        {
            file.Add(new XElement("line", new XAttribute("number", line)));
        }

        try
        {
            if (System.IO.Path.GetDirectoryName(_path) is { Length: > 0 } folder)
            {
                Directory.CreateDirectory(folder);
            }

            new XDocument(root).Save(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            // A store that cannot be written loses nothing but the marks' persistence.
        }
    }

    private XElement? Load()
    {
        try
        {
            return File.Exists(_path) ? XDocument.Load(_path).Root : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return null;
        }
    }
}
