using System.Text.RegularExpressions;

namespace JGraph.Scripting.Editing;

/// <summary>
/// Wrap Comments, free of a window (App Designer window plan W9b, R2025b's <c>rtc/plugins/language/matlab/commentCode/WrapComments</c>
/// with its <c>CommentSection</c>, <c>LineCommentSection</c> and <c>BlockCommentSection</c>, carried over event by event): Ctrl+J joins the
/// run of comment lines the caret is in - those sharing its sign and indent, bullets and bare signs left out - or a block comment's
/// paragraphs, and breaks them again at white space so that no line passes the width (<c>matlab.editor.language.matlab.comments.MaxWidth</c>,
/// 75), counted from the line's start or from the comment sign (<c>FromCommentStart</c>). A selection wraps every comment run in it.
/// </summary>
public static class WrapComments
{
    /// <summary>The width lines are held to by default (<c>WRAP_DEFAULT_LENGTH</c>).</summary>
    public const int DefaultMaxWidth = 75;

    /// <summary>A comment line's sign with the white space either side (<c>COMMENT_ANNOTATION_REGEXP</c>).</summary>
    public static readonly Regex Annotation = new(@"^\s*%+\s*", RegexOptions.CultureInvariant);

    /// <summary>A bullet (<c>COMMENT_BULLET_REGEX</c>).</summary>
    public static readonly Regex Bullet = new(@"\s*[%]+\s*[*#]", RegexOptions.CultureInvariant);

    /// <summary>A line that opens a block comment (<c>BLOCK_COMMENT_START_REGEX</c>).</summary>
    public static readonly Regex BlockStart = new(@"^\s*(%\{)\s*$", RegexOptions.CultureInvariant);

    /// <summary>A line that closes one (<c>BLOCK_COMMENT_END_REGEX</c>).</summary>
    public static readonly Regex BlockEnd = new(@"^\s*(%\})\s*$", RegexOptions.CultureInvariant);

    /// <summary>What a line is to the wrap: code, a line comment, or part of a block comment (its delimiters included).</summary>
    public enum LineKind
    {
        Code,
        Comment,
        Block,
    }

    /// <summary>Each line's kind, as R2025b's tokens would say: a block opens with <c>%{</c> alone on a line and closes with <c>%}</c> alone, nesting.</summary>
    public static IReadOnlyList<LineKind> Classify(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var kinds = new LineKind[lines.Count];
        int open = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            string trimmed = lines[i].Trim();
            if (open == 0 && trimmed == "%{")
            {
                kinds[i] = LineKind.Block;
                open = 1;
                continue;
            }

            if (open > 0)
            {
                kinds[i] = LineKind.Block;
                if (trimmed == "%{")
                {
                    open++;
                }
                else if (trimmed == "%}")
                {
                    open--;
                }

                continue;
            }

            kinds[i] = trimmed.StartsWith('%') ? LineKind.Comment : LineKind.Code;
        }

        return kinds;
    }

    /// <summary>
    /// Wraps the comments at the caret (<paramref name="caretLine"/>, zero-based) or in the selection (<paramref name="selectionFirst"/> to
    /// <paramref name="selectionLast"/>, zero-based, or -1) of <paramref name="lines"/>, in place, to <paramref name="maxWidth"/>.
    /// </summary>
    /// <returns>How many comment runs were wrapped; 0 when the caret is on nothing to wrap.</returns>
    public static int Wrap(List<string> lines, int caretLine, int selectionFirst, int selectionLast, int maxWidth = DefaultMaxWidth, bool fromCommentStart = false)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
        {
            return 0;
        }

        IReadOnlyList<LineKind> kinds = Classify(lines);
        (int first, int last)? target = selectionFirst >= 0
            ? (Math.Clamp(selectionFirst, 0, lines.Count - 1), Math.Clamp(selectionLast, 0, lines.Count - 1))
            : TargetFromCaret(lines, kinds, Math.Clamp(caretLine, 0, lines.Count - 1));
        if (target is not { } range)
        {
            return 0;
        }

        // The nodes stay live through the edits: each line remembers whose it is, so a later section starts where its first line now stands.
        var owners = new List<int>(Enumerable.Range(0, lines.Count));
        var document = new Document(lines, owners);
        List<Section> sections = CreateSections(lines, kinds, range.first, range.last, maxWidth, !fromCommentStart);
        foreach (Section section in sections)
        {
            foreach (WrapEvent e in section.GenerateWrapEvents(owners))
            {
                e.Apply(document);
            }
        }

        return sections.Count;
    }

    /// <summary><c>_getTargetNodesFromCaret</c>: the caret's run of line comments, or its block comment, or nothing.</summary>
    private static (int, int)? TargetFromCaret(IReadOnlyList<string> lines, IReadOnlyList<LineKind> kinds, int caret)
    {
        if (IsNonEmptyLineComment(lines, kinds, caret))
        {
            return (FindLineCommentStart(lines, kinds, caret), FindLineCommentEnd(lines, kinds, caret));
        }

        if (kinds[caret] == LineKind.Block)
        {
            return (FindBlockStart(lines, caret), FindBlockEnd(lines, caret));
        }

        return null;
    }

    private static bool IsNonEmptyLineComment(IReadOnlyList<string> lines, IReadOnlyList<LineKind> kinds, int line)
    {
        if (kinds[line] != LineKind.Comment)
        {
            return false;
        }

        string text = lines[line];
        string prefix = Prefix(text) ?? string.Empty;
        string rest = text[prefix.Length..].Trim();
        return rest.Length > 0 && rest != "*" && rest != "#";
    }

    private static int FindLineCommentStart(IReadOnlyList<string> lines, IReadOnlyList<LineKind> kinds, int line)
    {
        string? prefix = Prefix(lines[line]);
        while (line > 0 && Grows(lines, kinds, line - 1, prefix))
        {
            line--;
        }

        return line;
    }

    private static int FindLineCommentEnd(IReadOnlyList<string> lines, IReadOnlyList<LineKind> kinds, int line)
    {
        string? prefix = Prefix(lines[line]);
        while (line + 1 < lines.Count && Grows(lines, kinds, line + 1, prefix))
        {
            line++;
        }

        return line;
    }

    /// <summary>Whether the run of comment lines reaches <paramref name="line"/> (<c>_findLineCommentStart</c>'s test): a line comment with the same prefix, not a bullet, not the prefix alone.</summary>
    private static bool Grows(IReadOnlyList<string> lines, IReadOnlyList<LineKind> kinds, int line, string? prefix)
    {
        string text = lines[line];
        string? theirs = Prefix(text);
        return kinds[line] == LineKind.Comment && theirs == prefix && !Bullet.IsMatch(text) && text != theirs;
    }

    private static int FindBlockStart(IReadOnlyList<string> lines, int line)
    {
        if (lines[line].Trim() == "%{" || line == 0)
        {
            return line;
        }

        int depth = 1;
        while (line > 0 && depth > 0)
        {
            line--;
            string trimmed = lines[line].Trim();
            if (trimmed == "%{")
            {
                depth--;
            }

            if (trimmed == "%}")
            {
                depth++;
            }
        }

        return line;
    }

    private static int FindBlockEnd(IReadOnlyList<string> lines, int line)
    {
        if (lines[line].Trim() == "%}" || line == lines.Count - 1)
        {
            return line;
        }

        int depth = 1;
        while (line + 1 < lines.Count && depth > 0)
        {
            line++;
            string trimmed = lines[line].Trim();
            if (trimmed == "%}")
            {
                depth--;
            }

            if (trimmed == "%{")
            {
                depth++;
            }
        }

        return line;
    }

    /// <summary>A comment line's prefix (<c>_getLineCommentPrefix</c>): the annotation match, or null.</summary>
    public static string? Prefix(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Match m = Annotation.Match(text);
        return m.Success ? m.Value : null;
    }

    /// <summary><c>_createCommentSections</c>: the sections among the lines, each growing while its rule says so.</summary>
    private static List<Section> CreateSections(IReadOnlyList<string> lines, IReadOnlyList<LineKind> kinds, int first, int last, int maxWidth, bool fromStart)
    {
        var sections = new List<Section>();
        Section? current = null;
        for (int i = first; i <= last; i++)
        {
            string text = lines[i];
            if (current is not null && current.SectionShouldGrow(text))
            {
                current.GrowSection(i, text);
            }
            else if (kinds[i] == LineKind.Block)
            {
                current = new BlockSection(fromStart, maxWidth, i, text);
                sections.Add(current);
            }
            else if (kinds[i] == LineKind.Comment && text != Prefix(text))
            {
                current = new LineSection(fromStart, maxWidth, i, text);
                sections.Add(current);
            }
            else
            {
                current = null;
            }
        }

        return sections;
    }

    // --- the document the events edit -----------------------------------------------------------------------------------

    private sealed class Document
    {
        private readonly List<string> _lines;
        private readonly List<int> _owners;

        public Document(List<string> lines, List<int> owners)
        {
            _lines = lines;
            _owners = owners;
        }

        /// <summary>Replaces columns [<paramref name="startColumn"/>, <paramref name="endColumn"/>) (one-based) from one line to another with <paramref name="text"/>, which may hold line breaks.</summary>
        public void ReplaceText(string text, int startLine, int startColumn, int endLine, int endColumn)
        {
            string before = Slice(_lines[startLine - 1], 0, startColumn - 1);
            string after = _lines[endLine - 1][Math.Clamp(endColumn - 1, 0, _lines[endLine - 1].Length)..];
            string[] made = (before + text + after).Split('\n');
            int owner = _owners[startLine - 1];
            _lines.RemoveRange(startLine - 1, endLine - startLine + 1);
            _lines.InsertRange(startLine - 1, made);
            _owners.RemoveRange(startLine - 1, endLine - startLine + 1);
            _owners.InsertRange(startLine - 1, made.Select((_, k) => k == 0 ? owner : -1));
        }

        private static string Slice(string text, int from, int to) => text[Math.Clamp(from, 0, text.Length)..Math.Clamp(to, 0, text.Length)];
    }

    private abstract class WrapEvent
    {
        public abstract void Apply(Document document);

        public virtual void UpdateContent(List<List<string>> content)
        {
        }
    }

    private sealed class DeleteEvent : WrapEvent
    {
        private readonly int _startLine, _startColumn, _endLine, _endColumn, _lineIndex, _nodeIndex;

        public DeleteEvent(int startLine, int startColumn, int endLine, int endColumn, int lineIndex, int nodeIndex)
        {
            _startLine = startLine;
            _startColumn = startColumn;
            _endLine = endLine;
            _endColumn = endColumn;
            _lineIndex = lineIndex;
            _nodeIndex = nodeIndex;
        }

        public override void Apply(Document document) => document.ReplaceText(string.Empty, _startLine, _startColumn, _endLine, _endColumn);

        public override void UpdateContent(List<List<string>> content)
        {
            string text = content[_lineIndex][_nodeIndex];
            content[_lineIndex][_nodeIndex] = text[..Math.Min(_startColumn - 1, text.Length)] + text[Math.Min(_endColumn - 1, text.Length)..];
        }
    }

    private sealed class ReplaceEvent : WrapEvent
    {
        private readonly string _text;
        private readonly int _startLine, _startColumn, _endLine, _endColumn;
        private readonly int? _lineIndex, _nodeIndex;

        public ReplaceEvent(string text, int startLine, int startColumn, int endLine, int endColumn, int? lineIndex = null, int? nodeIndex = null)
        {
            _text = text;
            _startLine = startLine;
            _startColumn = startColumn;
            _endLine = endLine;
            _endColumn = endColumn;
            _lineIndex = lineIndex;
            _nodeIndex = nodeIndex;
        }

        public override void Apply(Document document) => document.ReplaceText(_text, _startLine, _startColumn, _endLine, _endColumn);

        /// <summary><c>ReplaceEventInfo.updateContent</c>: the line's prefix cut and, below the first line, the line joined to the one before.</summary>
        public override void UpdateContent(List<List<string>> content)
        {
            if (_lineIndex is not { } line || _nodeIndex is not { } node)
            {
                return;
            }

            string own = content[line][node];
            string rest = own[Math.Min(_endColumn - 1, own.Length)..];
            content[line][node] = rest;
            if (line == 0)
            {
                return;
            }

            List<string> previous = content[line - 1];
            int e = previous.Count - 1;
            string last = previous[e];
            if (_startLine != _endLine)
            {
                int columns = 0;
                for (int k = 0; k < e; k++)
                {
                    columns += previous[k].Length;
                }

                int g = _startColumn - columns - 1;
                previous[e] = last[..Math.Clamp(g, 0, last.Length)] + _text;
            }

            if (previous.Count == 1 && content[line].Count == 1)
            {
                content[line - 1] = [previous[0] + content[line][0]];
            }
            else
            {
                content[line - 1] = [.. previous, .. content[line]];
            }

            content[line] = [];
        }
    }

    private sealed class InsertEvent : WrapEvent
    {
        private readonly string _text;
        private readonly int _line, _column;

        public InsertEvent(string text, int line, int column)
        {
            _text = text;
            _line = line;
            _column = column;
        }

        public override void Apply(Document document)
        {
            if (_text.Length > 0)
            {
                document.ReplaceText(_text, _line, _column, _line, _column);
            }
        }
    }

    private sealed class LineCache
    {
        public int Line { get; private set; }
        public int Length { get; private set; }
        public int Column { get; private set; }
        public int WhitespaceLength { get; private set; }
        public int StartLine { get; private set; }
        public int EndLine { get; private set; }

        public void Update(int lines, int length, int columns, int whitespace)
        {
            Line += lines;
            Length = length;
            Column = columns;
            WhitespaceLength = whitespace;
            EndLine = Line - 1;
        }

        public void SetStartLine(int line)
        {
            StartLine = line;
            Line = line;
        }
    }

    // --- the sections (CommentSection) --------------------------------------------------------------------------------------

    private abstract class Section
    {
        protected readonly bool FromStart;
        protected readonly int MaxLength;
        protected readonly int StartIndex;
        protected readonly List<List<string>> Content = [];
        protected readonly LineCache Cache = new();
        protected List<WrapEvent> Events = [];
        protected string Prefix = string.Empty;
        protected int PrefixLength;
        private bool _wrappingValid = true;
        private int _cachedSubcontentLength;
        private int _cachedSubcontentColumns;

        protected Section(bool fromStart, int maxLength, int startIndex, string text)
        {
            FromStart = fromStart;
            MaxLength = maxLength;
            StartIndex = startIndex;
            Content.Add([text]);
        }

        public abstract bool SectionShouldGrow(string text);

        public virtual void GrowSection(int index, string text) => Content.Add([text]);

        public IReadOnlyList<WrapEvent> GenerateWrapEvents(List<int> owners)
        {
            Cache.SetStartLine(owners.IndexOf(StartIndex) is int at and >= 0 ? at + 1 : StartIndex + 1);
            Events = [];
            GenerateEventsToCombineLines();
            GenerateEventsToWrapContent();
            return _wrappingValid ? Events : [];
        }

        protected abstract Regex PrefixRegex { get; }

        protected abstract void GenerateEventsToWrapContent();

        protected abstract bool IsEdgeOfDelimiter(string text);

        protected virtual void HandleDelimiterEdge(string text, int line, int index)
        {
        }

        protected abstract int CalculateMaxContentLength();

        protected abstract int LengthOfPrefixWithoutLeadingWhitespace();

        private string UpdateSubcontentCache(string text, int from, int? to = null)
        {
            string cut = text[Math.Min(from, text.Length)..Math.Min(to ?? text.Length, text.Length)];
            _cachedSubcontentLength = CalculateContentLength(cut);
            _cachedSubcontentColumns = cut.Length;
            return cut;
        }

        /// <summary><c>_generateEventsToCombineLines</c>: each line's trailing white space cut, its prefix and the line break before it made one space.</summary>
        private void GenerateEventsToCombineLines()
        {
            int startColumn = 1;
            int previousLine = Cache.StartLine;
            int line = 0;
            string joiner = string.Empty;
            Regex prefix = PrefixRegex;
            int columns = 0;
            bool edge = true;
            for (int v = 0; v < Content.Count; v++)
            {
                List<string> c = Content[v];
                edge = false;
                line = Cache.StartLine + v;
                if (c.Count == 1 && IsEdgeOfDelimiter(c[0]))
                {
                    RemoveWhitespaceFromEndOfLine(previousLine, columns, startColumn, v - 1);
                    HandleDelimiterEdge(c[0], ++previousLine, v);
                    previousLine = line + 1;
                    startColumn = 1;
                    columns = 0;
                    edge = true;
                    joiner = string.Empty;
                    continue;
                }

                columns = c.Sum(static s => s.Length);
                RemovePrefixAndLineBreak(c, prefix, joiner, previousLine, startColumn, line, v);
                startColumn = UpdatedStartColumn(c, columns);
                previousLine = line;
                joiner = " ";
            }

            if (!edge)
            {
                RemoveWhitespaceFromEndOfLine(previousLine, columns, startColumn, Content.Count - 1);
            }

            Events.Reverse();
            foreach (WrapEvent e in Events)
            {
                e.UpdateContent(Content);
            }
        }

        private static int UpdatedStartColumn(List<string> c, int columns)
        {
            string last = c[^1];
            int trailing = Regex.Match(last, @"\s*$").Value.Length;
            return columns - trailing + 1;
        }

        private void RemovePrefixAndLineBreak(List<string> c, Regex prefix, string joiner, int startLine, int startColumn, int line, int v)
        {
            Match m = prefix.Match(c[0]);
            int p = m.Success ? m.Value.Length + 1 : 0;
            Events.Add(new ReplaceEvent(joiner, startLine, startColumn, line, p, v, 0));
        }

        private void RemoveWhitespaceFromEndOfLine(int line, int columns, int startColumn, int index)
        {
            if (columns < startColumn)
            {
                return;
            }

            if (index < 0 || index >= Content.Count)
            {
                return;
            }

            Events.Add(new DeleteEvent(line, startColumn, line, columns + 1, index, Content[index].Count - 1));
        }

        /// <summary><c>_wrapContentLine</c>: the prefix put back at the line's start and the content broken at white space to the width.</summary>
        protected void WrapContentLine(List<string> content)
        {
            Events.Add(new InsertEvent(Prefix, Cache.Line, 1));
            string breakText = "\n" + Prefix;
            int g = CalculateMaxContentLength();
            if (g < 1)
            {
                _wrappingValid = false;
            }
            else
            {
                foreach (string piece in content)
                {
                    WrapSubcontent(breakText, g, piece);
                }
            }

            Cache.Update(1, 0, 0, 0);
        }

        private void WrapSubcontent(string breakText, int max, string c)
        {
            _cachedSubcontentLength = CalculateContentLength(c);
            _cachedSubcontentColumns = c.Length;
            if (_cachedSubcontentLength == 0 && _cachedSubcontentColumns == 0)
            {
                return;
            }

            if (Cache.Length >= max)
            {
                c = HandleCachedLineAlreadyFull(breakText, c);
            }

            while (Cache.Length + _cachedSubcontentLength > max)
            {
                if (!IsSubcontentSplittable(c))
                {
                    WrapNonSplittableSubcontent(breakText);
                    return;
                }

                c = WrapSplittableSubcontent(breakText, max, c);
            }

            HandleFullSubcontentFitsOnCurrentLine(c);
        }

        private string HandleCachedLineAlreadyFull(string breakText, string e)
        {
            int n = BeginningWhitespaceColumns(e);
            if (n > 0)
            {
                e = UpdateSubcontentCache(e, n);
            }

            Events.Add(CreateReplaceEvent(breakText, Cache.Column, Cache.WhitespaceLength, n));
            int c = EndingWhitespaceColumns(e);
            if (c > 0)
            {
                e = UpdateSubcontentCache(e, 0, e.Length - c);
            }

            Cache.Update(1, 0, 0, c);
            return e;
        }

        private void HandleFullSubcontentFitsOnCurrentLine(string c)
        {
            int e = EndingWhitespaceColumns(c);
            Cache.Update(0, Cache.Length + _cachedSubcontentLength, Cache.Column + _cachedSubcontentColumns, e);
        }

        private void WrapNonSplittableSubcontent(string breakText)
        {
            int lines = 0;
            if (Cache.Length > 0)
            {
                Events.Add(CreateReplaceEvent(breakText, Cache.Column, Cache.WhitespaceLength, 0));
                lines = 1;
            }

            Cache.Update(lines, _cachedSubcontentLength, _cachedSubcontentColumns, 0);
        }

        private string WrapSplittableSubcontent(string breakText, int max, string n)
        {
            int room = max - Cache.Length + PrefixLength;
            string whole = Prefix + n;
            int p = EditorTabs.IndexAtVisualColumn(whole, room) - Prefix.Length;
            Match m = p >= 1 ? Regex.Match(n, "^.{1," + p + "}(?:\\s+|$)") : Match.Empty;
            return m.Success ? WrapPartialSubcontentOntoCurrentLine(breakText, m.Value, n) : WrapSubcontentOntoOwnLine(breakText, n);
        }

        private string WrapPartialSubcontentOntoCurrentLine(string breakText, string piece, string n)
        {
            int s = Cache.Column + piece.Length;
            int c = EndingWhitespaceColumns(piece);
            Events.Add(CreateReplaceEvent(breakText, s, c, 0));
            n = UpdateSubcontentCache(n, piece.Length);
            Cache.Update(1, 0, 0, 0);
            return n;
        }

        private string WrapSubcontentOntoOwnLine(string breakText, string e) =>
            Cache.Length > 0 ? WrapSubcontentOntoNextLine(breakText, e) : WrapSubcontentAtAnyWhitespace(breakText, e);

        private string WrapSubcontentOntoNextLine(string breakText, string e)
        {
            Events.Add(CreateReplaceEvent(breakText, Cache.Column, Cache.WhitespaceLength, 0));
            int s = EndingWhitespaceColumns(e);
            Cache.Update(1, 0, 0, s);
            return UpdateSubcontentCache(e, 0, e.Length - s);
        }

        private string WrapSubcontentAtAnyWhitespace(string breakText, string e)
        {
            Match n = Regex.Match(e, @"\s+");
            int s = n.Index;
            int c = n.Length;
            Events.Add(CreateReplaceEvent(breakText, s, 0, c));
            e = UpdateSubcontentCache(e, s + c);
            Cache.Update(1, 0, 0, 0);
            return e;
        }

        private static int EndingWhitespaceColumns(string text) => Regex.Match(text, @"\s+$").Length;

        private static int BeginningWhitespaceColumns(string text) => Regex.Match(text, @"^\s+").Length;

        private static bool IsSubcontentSplittable(string text) => Regex.IsMatch(text, @"\s");

        private ReplaceEvent CreateReplaceEvent(string text, int column, int whitespace, int extra)
        {
            int c = Prefix.Length + 1;
            int g = column - whitespace + c;
            int p = column + extra + c;
            return new ReplaceEvent(text, Cache.Line, g, Cache.Line, p);
        }

        private int CalculateContentLength(string text)
        {
            int e = EditorTabs.VisualLength(Prefix + text);
            if (!FromStart)
            {
                e -= PrefixLength - LengthOfPrefixWithoutLeadingWhitespace();
            }

            return CalculateLength(e);
        }

        protected int CalculateLength(int length) => FromStart ? length - PrefixLength : length - LengthOfPrefixWithoutLeadingWhitespace();
    }

    private sealed class LineSection : Section
    {
        public LineSection(bool fromStart, int maxLength, int startIndex, string text)
            : base(fromStart, maxLength, startIndex, text)
        {
            Prefix = WrapComments.Prefix(text) ?? string.Empty;
            PrefixLength = EditorTabs.VisualLength(Prefix);
        }

        public override bool SectionShouldGrow(string text)
        {
            Match d = Annotation.Match(text);
            if (!d.Success || d.Value != Prefix || Bullet.IsMatch(text) || BlockStart.IsMatch(text))
            {
                return false;
            }

            return d.Value != text;
        }

        protected override Regex PrefixRegex => new("^" + Regex.Escape(Prefix), RegexOptions.CultureInvariant);

        protected override bool IsEdgeOfDelimiter(string text) => false;

        protected override void GenerateEventsToWrapContent() => WrapContentLine(Content[0]);

        protected override int CalculateMaxContentLength() => CalculateLength(MaxLength);

        protected override int LengthOfPrefixWithoutLeadingWhitespace()
        {
            int at = Prefix.IndexOf('%');
            return EditorTabs.VisualLength(at < 0 ? Prefix : Prefix[at..]);
        }
    }

    private sealed class BlockSection : Section
    {
        private static readonly Regex StartWhitespace = new(@"^\s+", RegexOptions.CultureInvariant);
        private static readonly Regex EndWhitespace = new(@"\s+$", RegexOptions.CultureInvariant);
        private const string EdgeMarker = "\u0000edge";
        private int _openBlocks = 1;
        private readonly List<string> _indentsQueue = [];
        private readonly List<string> _indentStack = [];

        public BlockSection(bool fromStart, int maxLength, int startIndex, string text)
            : base(fromStart, maxLength, startIndex, text)
        {
            if (!BlockStart.IsMatch(text))
            {
                AddIndentFromFirstLine(text);
            }
        }

        public override bool SectionShouldGrow(string text) => _openBlocks != 0;

        public override void GrowSection(int index, string text)
        {
            base.GrowSection(index, text);
            if (BlockStart.IsMatch(text))
            {
                _openBlocks++;
            }

            if (BlockEnd.IsMatch(text))
            {
                _openBlocks--;
            }
        }

        protected override Regex PrefixRegex => new(@"^\s*", RegexOptions.CultureInvariant);

        protected override bool IsEdgeOfDelimiter(string text) => BlockStart.IsMatch(text) || BlockEnd.IsMatch(text) || text.Trim().Length == 0;

        private void AddIndentFromFirstLine(string text)
        {
            Match m = Regex.Match(text, @"\S");
            string indent = m.Success ? text[..m.Index] : string.Empty;
            _indentsQueue.Insert(0, indent);
            _indentStack.Insert(0, indent);
        }

        private void UpdateIndent(int i)
        {
            Prefix = i < _indentsQueue.Count ? _indentsQueue[i] : string.Empty;
            PrefixLength = EditorTabs.VisualLength(Prefix);
        }

        protected override void HandleDelimiterEdge(string text, int line, int index)
        {
            UpdateIndentLevels(text);
            if (Regex.IsMatch(text, @"\S"))
            {
                RemoveEndingWhitespace(text, line, index);
                RemoveBeginningWhitespace(text, line, index);
            }

            Content[index].Insert(0, EdgeMarker);
        }

        private void UpdateIndentLevels(string text)
        {
            if (text.Trim() == "%{")
            {
                string indent = StartWhitespace.Match(text).Value;
                _indentsQueue.Add(indent);
                _indentStack.Insert(0, indent);
                return;
            }

            if (text.Trim() == "%}" && _indentStack.Count > 0)
            {
                _indentStack.RemoveAt(0);
                if (_indentStack.Count > 0)
                {
                    _indentsQueue.Add(_indentStack[0]);
                }
            }
        }

        private void RemoveEndingWhitespace(string text, int line, int index)
        {
            Match s = EndWhitespace.Match(text);
            if (!s.Success)
            {
                return;
            }

            int c = text.Length + 1;
            int g = c - s.Length;
            Events.Add(new DeleteEvent(line, g, line, c, index, 1));
        }

        private void RemoveBeginningWhitespace(string text, int line, int index)
        {
            if (BlockStart.IsMatch(text))
            {
                return;
            }

            Match s = StartWhitespace.Match(text);
            if (!s.Success)
            {
                return;
            }

            Events.Add(new DeleteEvent(line, 1, line, s.Length + 1, index, 1));
        }

        protected override void GenerateEventsToWrapContent()
        {
            int n = 0;
            UpdateIndent(n);
            foreach (List<string> paragraph in Content)
            {
                List<string> s = paragraph;
                if (s.Count > 0 && s[0] == EdgeMarker)
                {
                    string delimiter = s.Count > 1 ? s[1] : string.Empty;
                    if (BlockStart.IsMatch(delimiter))
                    {
                        UpdateIndent(n++);
                    }
                    else if (BlockEnd.IsMatch(delimiter))
                    {
                        Events.Add(new InsertEvent(Prefix, Cache.Line, 1));
                        UpdateIndent(n++);
                    }

                    Cache.Update(1, 0, 0, 0);
                    s = s.Skip(2).ToList();
                }

                if (s.Count > 0)
                {
                    WrapContentLine(s);
                }
            }
        }

        protected override int CalculateMaxContentLength() => FromStart ? MaxLength - PrefixLength : MaxLength;

        protected override int LengthOfPrefixWithoutLeadingWhitespace() => 0;
    }
}
