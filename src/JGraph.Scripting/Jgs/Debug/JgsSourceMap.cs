using System.IO;
using JGraph.Scripting.Editing;

namespace JGraph.Scripting.Jgs.Debug;

/// <summary>
/// One function of a code file as the debugger names it (App Designer window plan W11).
/// </summary>
/// <param name="Name">The name <c>dbstatus</c> prints after the file's <c>&gt;</c>: a local function's own, a method's <c>Class.method</c>, a nested function's <c>outer/inner</c>.</param>
/// <param name="Signature">The 1-based line of the <c>function</c> word.</param>
/// <param name="End">The 1-based line of its <c>end</c>, or the last line of its body when the file writes no <c>end</c>.</param>
/// <param name="HasEnd">Whether <paramref name="End"/> is an <c>end</c> line, which R2025b treats as a line a breakpoint can sit on.</param>
/// <param name="IsMain">Whether this is the file's main function, which <c>dbstatus</c> names by the file alone.</param>
internal sealed record JgsFunctionSpan(string Name, int Signature, int End, bool HasEnd, bool IsMain);

/// <summary>
/// What the debugger knows about the lines of one code file (App Designer window plan W11), read from
/// its parse tree: which lines carry a statement a breakpoint can pause at, where a breakpoint asked
/// for on another line lands, which function a line is in and how <c>dbstatus</c> names it. R2025b's
/// rules were measured headless (<c>w0_debug</c>, <c>w11_debug</c>): a line without code moves to the
/// next executable line (a comment or blank before <c>y = ...</c> lands on it, a classdef or property
/// line on the first method's first statement, the blank between two functions on the second's first
/// statement); a function's <c>end</c> line is one of them; a line past the last is refused
/// (<c>MATLAB:lineBeyondFileEnd</c>); the names are <c>file</c> for a script or a main function,
/// <c>file&gt;local</c>, <c>file&gt;Class.method</c>.
/// </summary>
internal sealed class JgsSourceMap
{
    private readonly string _stem;
    private readonly int[] _executable;
    private readonly List<JgsFunctionSpan> _functions = [];
    private readonly string? _className;

    private JgsSourceMap(string sourceId, IReadOnlyList<string> lines, IReadOnlyList<Stmt> program)
    {
        SourceId = sourceId;
        Lines = lines;
        _stem = Path.GetFileNameWithoutExtension(sourceId);
        IReadOnlyList<SmartIndent.Block> blocks = SmartIndent.Blocks(lines);
        var executable = new SortedSet<int>();
        _className = program.OfType<ClassdefStmt>().FirstOrDefault()?.Name;

        bool first = true;
        foreach (Stmt statement in program)
        {
            switch (statement)
            {
                case ClassdefStmt classdef:
                    foreach (ClassMethod method in classdef.Methods)
                    {
                        if (!method.Abstract)
                        {
                            AddFunction(method.Function, $"{classdef.Name}.{method.Function.Name}", isMain: false, blocks, executable, lines);
                        }
                    }

                    break;

                case FnStmt function:
                    // A function file's first function is its main one, named by the file; the rest are
                    // local. A class file's functions after the classdef are local to it too.
                    AddFunction(function, function.Name, isMain: first && _className is null, blocks, executable, lines);
                    break;

                default:
                    Collect(statement, executable, blocks, lines, parent: null);
                    break;
            }

            first = false;
        }

        _executable = executable.ToArray();
    }

    /// <summary>The file's path as the interpreter names the statements parsed from it.</summary>
    public string SourceId { get; }

    /// <summary>The file's lines.</summary>
    public IReadOnlyList<string> Lines { get; }

    /// <summary>The 1-based lines a breakpoint can sit on, in order.</summary>
    public IReadOnlyList<int> ExecutableLines => _executable;

    /// <summary>The file's functions, in the order written.</summary>
    public IReadOnlyList<JgsFunctionSpan> Functions => _functions;

    /// <summary>Whether the file declares a class.</summary>
    public bool IsClass => _className is not null;

    /// <summary>Where <c>dbstop in</c> a class file lands: the constructor's first line, or line 1 when the class writes none (R2025b, measured in P2).</summary>
    public int ClassEntryLine => _className is not null ? FirstLineOf($"{_className}.{_className}") ?? 1 : 1;

    /// <summary>Reads and parses the code file at <paramref name="path"/> (an <c>.mlapp</c>'s class text, any other file's own text).</summary>
    /// <exception cref="JgsSyntaxException">The file does not parse.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static JgsSourceMap Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string full = Path.GetFullPath(path);
        return Parse(JgsMlapp.ReadSource(full), full, JgsRunner.DialectOfFile(full, JgsDialect.Matlab));
    }

    /// <summary>Parses <paramref name="text"/> as the file <paramref name="sourceId"/> in <paramref name="dialect"/>.</summary>
    /// <exception cref="JgsSyntaxException">The text does not parse.</exception>
    public static JgsSourceMap Parse(string text, string sourceId, JgsDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(sourceId);
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        return new JgsSourceMap(sourceId, lines, Parser.Parse(text, sourceId, dialect));
    }

    /// <summary>
    /// Where a breakpoint asked for on <paramref name="line"/> lands: that line when a statement is on it, else the next
    /// executable line; null when there is none at or after it (R2025b refuses: <c>MATLAB:lineBeyondFileEnd</c>). A line
    /// before the first is the first (R2025b accepts <c>at 0</c>).
    /// </summary>
    public int? Snap(int line)
    {
        foreach (int candidate in _executable)
        {
            if (candidate >= line)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Whether a statement sits on <paramref name="line"/>.</summary>
    public bool IsExecutable(int line) => Array.BinarySearch(_executable, line) >= 0;

    /// <summary>The innermost function whose lines hold <paramref name="line"/>, or null at a script's top level.</summary>
    public JgsFunctionSpan? FunctionAt(int line)
    {
        JgsFunctionSpan? found = null;
        foreach (JgsFunctionSpan function in _functions)
        {
            if (line >= function.Signature && line <= function.End && (found is null || function.Signature >= found.Signature))
            {
                found = function;
            }
        }

        return found;
    }

    /// <summary>
    /// The name <c>dbstatus</c> prints for a breakpoint on <paramref name="line"/>: the file's stem for a script or the
    /// main function, else <c>stem&gt;name</c>; with <paramref name="completeNames"/> the whole path stands for the
    /// stem and the main function is named too (<c>dbstatus -completenames</c>, measured).
    /// </summary>
    public string NameAt(int line, bool completeNames = false)
    {
        string prefix = completeNames ? SourceId : _stem;
        JgsFunctionSpan? function = FunctionAt(line);
        if (function is null)
        {
            // A class file's line outside any method is the constructor's (K>K.K for a class that writes none: P2, measured).
            return _className is not null ? $"{prefix}>{_className}.{_className}" : completeNames ? $"{prefix}>{_stem}" : prefix;
        }

        return function.IsMain && !completeNames ? prefix : $"{prefix}>{function.Name}";
    }

    /// <summary>The first line a breakpoint lands on in the function <paramref name="name"/> (<c>dbstop in file&gt;name</c>), or null when the file has no such function.</summary>
    public int? FirstLineOf(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (JgsFunctionSpan function in _functions)
        {
            if (string.Equals(function.Name, name, StringComparison.Ordinal))
            {
                return Snap(function.Signature + 1) is int line && line <= function.End ? line : null;
            }
        }

        return null;
    }

    /// <summary>The function whose <c>end</c> is <paramref name="line"/>, or null.</summary>
    public JgsFunctionSpan? FunctionEndingAt(int line) => _functions.FirstOrDefault(f => f.HasEnd && f.End == line);

    private void AddFunction(FnStmt function, string name, bool isMain, IReadOnlyList<SmartIndent.Block> blocks, SortedSet<int> executable, IReadOnlyList<string> lines)
    {
        (int end, bool hasEnd) = EndOf(function, blocks, lines);
        _functions.Add(new JgsFunctionSpan(name, function.Line, end, hasEnd, isMain));
        foreach (Stmt statement in function.Body)
        {
            Collect(statement, executable, blocks, lines, parent: name);
        }

        if (hasEnd)
        {
            executable.Add(end);
        }
    }

    private void Collect(Stmt statement, SortedSet<int> executable, IReadOnlyList<SmartIndent.Block> blocks, IReadOnlyList<string> lines, string? parent)
    {
        if (statement is FnStmt nested)
        {
            // A nested function: named outer/inner as the stack names it (not measured in dbstatus).
            AddFunction(nested, parent is null ? nested.Name : $"{parent}/{nested.Name}", isMain: false, blocks, executable, lines);
            return;
        }

        if (statement is ClassdefStmt)
        {
            return;
        }

        if (statement.Line > 0)
        {
            executable.Add(statement.Line);
        }

        for (int slot = 0; slot < AstChildren.SlotCount(statement); slot++)
        {
            if (AstChildren.Slot(statement, slot) is { } block)
            {
                foreach (Stmt child in block)
                {
                    Collect(child, executable, blocks, lines, parent);
                }
            }
        }
    }

    /// <summary>A function's last line: its <c>end</c> when the block scanner finds one on a line that says so, else the last line of its body.</summary>
    private static (int End, bool HasEnd) EndOf(FnStmt function, IReadOnlyList<SmartIndent.Block> blocks, IReadOnlyList<string> lines)
    {
        foreach (SmartIndent.Block block in blocks)
        {
            // The scanner's lines are zero-based; the parser's and the debugger's one-based.
            int blockFirst = block.First + 1;
            int blockLast = block.Last + 1;
            if (block.Kind == "function" && blockFirst == function.Line)
            {
                string closing = blockLast >= 1 && blockLast <= lines.Count ? lines[blockLast - 1].TrimStart() : string.Empty;
                bool hasEnd = blockLast > blockFirst && closing.StartsWith("end", StringComparison.Ordinal)
                    && (closing.Length == 3 || !(char.IsLetterOrDigit(closing[3]) || closing[3] == '_'));
                return (blockLast, hasEnd);
            }
        }

        int last = function.Line;
        foreach (Stmt statement in function.Body)
        {
            last = Math.Max(last, LastLine(statement));
        }

        return (last, false);
    }

    private static int LastLine(Stmt statement)
    {
        int last = statement.Line;
        if (statement is FnStmt function)
        {
            foreach (Stmt child in function.Body)
            {
                last = Math.Max(last, LastLine(child));
            }

            return last;
        }

        for (int slot = 0; slot < AstChildren.SlotCount(statement); slot++)
        {
            if (AstChildren.Slot(statement, slot) is { } block)
            {
                foreach (Stmt child in block)
                {
                    last = Math.Max(last, LastLine(child));
                }
            }
        }

        return last;
    }
}
