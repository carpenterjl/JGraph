using System.Text;
using System.Text.RegularExpressions;

namespace JGraph.Scripting.Jgs;

/// <summary>What one of an app's user-written stretches of code is.</summary>
public enum MlappRegionKind
{
    /// <summary>The user's own properties and helper methods, between the component properties and the callbacks.</summary>
    EditableSection,

    /// <summary>The body of the startup function.</summary>
    Startup,

    /// <summary>The body of a component callback.</summary>
    Callback,

    /// <summary>
    /// The body of a callback App Designer writes itself (a responsive app's layout function). It
    /// is listed with the callbacks and is not the user's code.
    /// </summary>
    GeneratedCallback,
}

/// <summary>
/// One stretch of an App Designer class file that App Designer keeps apart from the code it
/// generates: where it is in the text, by line (zero-based), and the function it is the body of.
/// </summary>
/// <param name="Kind">What the stretch is.</param>
/// <param name="Name">The function whose body it is; empty for the editable section.</param>
/// <param name="FirstLine">The zero-based index of its first line.</param>
/// <param name="LineCount">How many lines it has, which may be none.</param>
public sealed record MlappRegion(MlappRegionKind Kind, string Name, int FirstLine, int LineCount)
{
    /// <summary>Whether the user owns these lines: App Designer keeps them as written when it saves.</summary>
    public bool IsUserCode => Kind != MlappRegionKind.GeneratedCallback;
}

/// <summary>
/// The layout of an App Designer class file (app-building plan U7b, ADR 0205): which lines are the
/// user's and which App Designer generates from its component tree. This is the piece the editor
/// shades by, the save-back derives App Designer's copy of the code from, and a visual designer
/// would regenerate around.
/// </summary>
/// <remarks>
/// <para>
/// App Designer writes a class in a fixed order and marks each part with a comment, and its own
/// record of the user's code (<c>appModel.mat</c>'s <c>code</c>) is those parts cut out of the
/// text. Measured over the 44 apps R2025b ships (probe <c>u7b_shape</c>): the component properties
/// block comes first, under <c>% Properties that correspond to app components</c>; one empty line
/// follows it; the editable section runs from there to the empty line before
/// <c>% Callbacks that handle component events</c> (or before <c>% Component initialization</c>
/// in an app with no callbacks); each function in the callbacks block is a callback, its record
/// the lines between its signature and its <c>end</c>; the one under
/// <c>% Code that executes after component creation</c> is the startup function, and what follows
/// <c>app</c> in its signature the app's input parameters. A responsive app and a Simulink app
/// have a second generated properties block ahead of the editable section, under a comment of the
/// same wording, and a responsive app a generated layout function among the callbacks.
/// </para>
/// <para>
/// A function's <c>end</c> is found by its block structure, not by its indentation, so user code
/// indented any way is cut correctly. Text that is not laid out this way - a plain class, or an app
/// whose marker comments were removed - has no regions, and nothing of it is shaded or derived.
/// </para>
/// </remarks>
public sealed partial class JgsMlappLayout
{
    private const string ComponentProperties = "% Properties that correspond to app components";
    private const string CallbacksBlock = "% Callbacks that handle component events";
    private const string ComponentInitialization = "% Component initialization";
    private const string StartupComment = "% Code that executes after component creation";
    private const string LayoutCallbackComment = "% Changes arrangement of the app based on UIFigure width";

    private readonly List<MlappRegion> _regions = [];
    private int _componentPropertiesEnd;

    private JgsMlappLayout(string[] lines)
    {
        Lines = lines;
    }

    /// <summary>The text's lines, without their line endings.</summary>
    public IReadOnlyList<string> Lines { get; }

    /// <summary>Whether the text is laid out as App Designer lays out an app.</summary>
    public bool IsAppLayout { get; private set; }

    /// <summary>
    /// Whether every function in the callbacks block is closed. Text caught mid-edit may not be,
    /// and then the callbacks cannot be told apart: there are no callback regions.
    /// </summary>
    public bool IsComplete { get; private set; }

    /// <summary>The class the file defines; empty when the text does not begin a class.</summary>
    public string ClassName { get; private set; } = string.Empty;

    /// <summary>The user's stretches of the text and the generated callbacks' bodies, in text order.</summary>
    public IReadOnlyList<MlappRegion> Regions => _regions;

    /// <summary>The startup function's parameters after <c>app</c>, as written; empty when it has none.</summary>
    public string InputParameters { get; private set; } = string.Empty;

    /// <summary>The startup function's body, or null when the app has no startup function.</summary>
    public MlappRegion? Startup => _regions.FirstOrDefault(static r => r.Kind == MlappRegionKind.Startup);

    /// <summary>The callbacks, generated ones included, in text order.</summary>
    public IEnumerable<MlappRegion> Callbacks =>
        _regions.Where(static r => r.Kind is MlappRegionKind.Callback or MlappRegionKind.GeneratedCallback);

    /// <summary>Reads the layout of a class file's <paramref name="text"/>.</summary>
    public static JgsMlappLayout Of(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string[] lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].TrimEnd('\r');
        }

        var layout = new JgsMlappLayout(lines);
        layout.Read(text);
        return layout;
    }

    /// <summary>The lines of a region.</summary>
    public IReadOnlyList<string> CodeOf(MlappRegion region)
    {
        ArgumentNullException.ThrowIfNull(region);
        return new ArraySegment<string>((string[])Lines, region.FirstLine, region.LineCount);
    }

    /// <summary>
    /// The editable section as App Designer records it: the lines between the generated
    /// properties and the callbacks, without the one empty line App Designer puts on each side.
    /// Empty when there is none.
    /// </summary>
    public IReadOnlyList<string> EditableSectionCode =>
        _regions.FirstOrDefault(static r => r.Kind == MlappRegionKind.EditableSection) is { } section
            ? Between(section.FirstLine, section.LineCount)
            : [];

    /// <summary>
    /// The editable section counted from the component properties block, any further generated
    /// properties block included. One shipped Simulink app's record of its section is cut this
    /// way, so the save-back holds a record against both cuts and keeps the one it was made with.
    /// </summary>
    internal IReadOnlyList<string> EditableSectionCodeWithGeneratedBlocks =>
        _regions.FirstOrDefault(static r => r.Kind == MlappRegionKind.EditableSection) is { } section
            ? Between(_componentPropertiesEnd + 1, section.FirstLine + section.LineCount - _componentPropertiesEnd - 1)
            : [];

    /// <summary>A run of lines without the one empty line App Designer puts on each side of it.</summary>
    private IReadOnlyList<string> Between(int first, int count)
    {
        if (count > 0 && Lines[first].Length == 0)
        {
            first++;
            count--;
        }

        if (count > 0 && Lines[first + count - 1].Length == 0)
        {
            count--;
        }

        return new ArraySegment<string>((string[])Lines, first, count);
    }

    /// <summary>
    /// The runs of lines App Designer generates, as (first line, count): everything but the user's
    /// regions. Empty when the text is not an app's.
    /// </summary>
    public IReadOnlyList<(int FirstLine, int LineCount)> GeneratedSpans
    {
        get
        {
            var spans = new List<(int, int)>();
            if (!IsAppLayout)
            {
                return spans;
            }

            int at = 0;
            foreach (MlappRegion region in _regions.Where(static r => r.IsUserCode))
            {
                if (region.FirstLine > at)
                {
                    spans.Add((at, region.FirstLine - at));
                }

                at = region.FirstLine + region.LineCount;
            }

            if (at < Lines.Count)
            {
                spans.Add((at, Lines.Count - at));
            }

            return spans;
        }
    }

    /// <summary>
    /// Whether the code App Designer generates is the same in <paramref name="other"/>: only the
    /// user's regions differ, if anything does. An edit that leaves this true is one App Designer
    /// keeps; one that does not is overwritten the next time App Designer saves the app.
    /// </summary>
    public bool SameGeneratedCode(JgsMlappLayout other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return IsAppLayout == other.IsAppLayout && string.Equals(Skeleton(), other.Skeleton(), StringComparison.Ordinal);
    }

    /// <summary>The generated lines, with a mark where each user region is.</summary>
    private string Skeleton()
    {
        if (!IsAppLayout)
        {
            return string.Join('\n', Lines);
        }

        var skeleton = new StringBuilder();
        foreach ((int first, int count) in GeneratedSpans)
        {
            for (int i = first; i < first + count; i++)
            {
                skeleton.Append(Lines[i]).Append('\n');
            }

            skeleton.Append('\0');
        }

        return skeleton.ToString();
    }

    private void Read(string text)
    {
        if (Lines.Select(static l => ClassLine().Match(l)).FirstOrDefault(static m => m.Success) is { } classLine)
        {
            ClassName = classLine.Groups[1].Value;
        }

        int properties = IndexOfLine(ComponentProperties, 0);
        int head = properties < 0 ? -1 : IndexOfLine("end", properties + 1);
        if (head < 0)
        {
            return;
        }

        _componentPropertiesEnd = head;

        // Further generated properties blocks - a responsive app's, a Simulink app's - each after
        // any number of empty lines and under a comment of the same wording.
        while (true)
        {
            int next = head + 1;
            while (next < Lines.Count && string.IsNullOrWhiteSpace(Lines[next]))
            {
                next++;
            }

            if (next + 1 >= Lines.Count || !GeneratedPropertiesComment().IsMatch(Lines[next])
                || !Lines[next + 1].TrimStart().StartsWith("properties", StringComparison.Ordinal))
            {
                break;
            }

            head = IndexOfLine("end", next + 1);
            if (head < 0)
            {
                return;
            }
        }

        int initialization = IndexOfLine(ComponentInitialization, head + 1);
        if (initialization < 0)
        {
            return;
        }

        int callbacks = IndexOfLine(CallbacksBlock, head + 1);
        if (callbacks > initialization)
        {
            callbacks = -1;
        }

        int anchor = callbacks < 0 ? initialization : callbacks;
        IsAppLayout = true;
        _regions.Add(new MlappRegion(MlappRegionKind.EditableSection, string.Empty, head + 1, anchor - head - 1));
        IsComplete = callbacks < 0 || ReadCallbacks(text, callbacks, initialization);
    }

    /// <summary>The first line at or after <paramref name="from"/> that is <paramref name="text"/> once trimmed, or -1.</summary>
    private int IndexOfLine(string text, int from)
    {
        for (int i = from; i < Lines.Count; i++)
        {
            if (Lines[i].Trim() == text)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Finds each function of the callbacks block and the lines between its signature and its
    /// <c>end</c>. False when a function is left open.
    /// </summary>
    private bool ReadCallbacks(string text, int callbacks, int initialization)
    {
        IReadOnlyList<Token> tokens = Lexer.Tokenize(text, tolerant: true, JgsDialect.Matlab);
        var found = new List<MlappRegion>();
        int depth = 0;
        int brackets = 0;
        int opened = -1;
        bool closed = false;

        // The block's own `methods` line follows the comment; its functions start below that.
        int firstLine = callbacks + 3;
        for (int i = 0; i < tokens.Count; i++)
        {
            Token token = tokens[i];
            if (token.Line < firstLine)
            {
                continue;
            }

            if (token.Line > initialization || token.Type == TokenType.Eof)
            {
                break;
            }

            switch (token.Type)
            {
                case TokenType.LParen or TokenType.LBracket or TokenType.LBrace:
                    brackets++;
                    continue;
                case TokenType.RParen or TokenType.RBracket or TokenType.RBrace:
                    brackets = Math.Max(0, brackets - 1);
                    continue;
                default:
                    break;
            }

            if (brackets > 0 || (i > 0 && tokens[i - 1].Type == TokenType.Dot))
            {
                continue; // `end` inside an index is the last element; a name after a dot is a field
            }

            if (Opens(tokens, i))
            {
                if (depth == 0)
                {
                    opened = token.Line - 1;
                }

                depth++;
            }
            else if (token.Type == TokenType.End)
            {
                depth--;
                if (depth < 0)
                {
                    closed = true; // the end of the methods block
                    break;
                }

                if (depth == 0)
                {
                    found.Add(RegionOf(opened, token.Line - 1));
                    opened = -1;
                }
            }
        }

        // A function left open takes the block's own end for its own, and the block is never closed.
        if (!closed)
        {
            return false;
        }

        _regions.AddRange(found);
        return true;
    }

    /// <summary>Whether the token begins a block that an <c>end</c> closes.</summary>
    private static bool Opens(IReadOnlyList<Token> tokens, int at)
    {
        Token token = tokens[at];
        switch (token.Type)
        {
            case TokenType.If or TokenType.For or TokenType.While or TokenType.Switch or TokenType.Try or TokenType.Function:
                return true;
            case TokenType.Identifier:
                bool startsStatement = at == 0
                    || tokens[at - 1].Type is TokenType.Newline or TokenType.Semicolon or TokenType.Comma;
                if (!startsStatement)
                {
                    return false;
                }

                // `arguments` is a block only as a line by itself; anything else of that name is a variable.
                return token.Text is "parfor" or "spmd"
                    || (token.Text == "arguments" && at + 1 < tokens.Count && tokens[at + 1].Type == TokenType.Newline);
            default:
                return false;
        }
    }

    /// <summary>The body between a function's signature line and its closing line, and what the function is.</summary>
    private MlappRegion RegionOf(int signature, int close)
    {
        Match match = FunctionLine().Match(Lines[signature]);
        string name = match.Success ? match.Groups[1].Value : string.Empty;
        string above = signature > 0 ? Lines[signature - 1].Trim() : string.Empty;
        MlappRegionKind kind = above switch
        {
            StartupComment => MlappRegionKind.Startup,
            LayoutCallbackComment => MlappRegionKind.GeneratedCallback,
            _ => MlappRegionKind.Callback,
        };

        if (kind == MlappRegionKind.Startup && match.Success)
        {
            string parameters = match.Groups[2].Value;
            int comma = parameters.IndexOf(',', StringComparison.Ordinal);
            InputParameters = comma < 0 ? string.Empty : parameters[(comma + 1)..].Trim();
        }

        return new MlappRegion(kind, name, signature + 1, Math.Max(0, close - signature - 1));
    }

    [GeneratedRegex(@"^\s*%.*[Pp]roperties that correspond to ")]
    private static partial Regex GeneratedPropertiesComment();

    [GeneratedRegex(@"^\s*classdef\s+(?:\(.*?\)\s*)?(\w+)")]
    private static partial Regex ClassLine();

    [GeneratedRegex(@"^\s*function\s+(?:(?:\[[^\]]*\]|\w+)\s*=\s*)?(\w+)\s*(?:\((.*)\))?")]
    private static partial Regex FunctionLine();
}
