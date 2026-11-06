using System.Globalization;
using System.Text;
using JGraph.Scripting.Jgs.Debug;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The debugger's words (App Designer window plan W11): <c>dbstop</c>, <c>dbclear</c> and <c>dbstatus</c>
/// over the session's breakpoints (<see cref="JgsBreakpointStore"/>), <c>dbstack</c> and <c>dbtype</c>,
/// and <c>dbcont</c>, <c>dbstep</c>, <c>dbquit</c>, <c>dbup</c> and <c>dbdown</c>, which the host answers
/// at its <c>K&gt;&gt;</c> prompt and which refuse anywhere else, as R2025b's do. The grammar, where a
/// breakpoint lands, the printed lines and every refusal were measured headless (<c>w0_debug</c>,
/// <c>w11_debug</c>). A one-shot run has no store: the words refuse in R2025b's words for a missing file,
/// or do nothing.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>Every name <see cref="RegisterDebugBuiltins"/> declares.</summary>
    internal static IReadOnlyList<string> DebugBuiltinNames { get; } =
        ["dbstop", "dbclear", "dbstatus", "dbcont", "dbstep", "dbquit", "dbstack", "dbup", "dbdown", "dbtype"];

    private const string DebugOnlyWhenPaused = "Debug commands only allowed when stopped in debug mode.";

    private static void RegisterDebugBuiltins(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        // Each word is told how many outputs are wanted: a bare dbstatus prints, s = dbstatus answers the struct.
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> body, int maxOutputs) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) => FirstOf(body(args, 1, line, col)))
            {
                AutoCallsBare = true,
                KeepsStringArguments = true,
                BindsAnsAsStatement = false,
                KnowsWhenDiscarded = true,
                MultiOutput = (args, wanted, line, col) => wanted > maxOutputs
                    ? throw new JgsRuntimeException(line, col, "MATLAB:maxlhs", "Too many output arguments.")
                    : body(args, wanted, line, col),
            }));

        Define("dbstop", (args, _, line, col) => { Dbstop(interpreter, host, args, line, col); return []; }, 0);
        Define("dbclear", (args, _, line, col) => { Dbclear(interpreter, host, args, line, col); return []; }, 0);
        Define("dbstatus", (args, wanted, line, col) => Dbstatus(interpreter, host, args, wanted, line, col), 1);
        Define("dbstack", (args, wanted, line, col) => Dbstack(interpreter, host, args, wanted, line, col), 2);
        Define("dbtype", (args, _, line, col) => { Dbtype(interpreter, host, args, line, col); return []; }, 0);
        foreach (string word in (string[])["dbcont", "dbstep", "dbquit", "dbup", "dbdown"])
        {
            // The host's K>> prompt takes these while paused; code reaching them is not paused.
            Define(word, (_, _, line, col) => throw new JgsRuntimeException(line, col, "MATLAB:dbOnlyInDebugMode", DebugOnlyWhenPaused), 0);
        }
    }

    // --- dbstop ---------------------------------------------------------------------------------------

    private static void Dbstop(Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
        }

        string[] words = DebugWords(args, line, col);
        JgsBreakpointStore? store = host.Breakpoints;
        int i = 0;
        if (words[0] == "if")
        {
            (StopCondition? stopIf, string? identifier) = ParseStopIf(words, line, col);
            if (stopIf is { } known)
            {
                store?.StopIf(known, identifier);
            }

            return;
        }

        if (words[0] == "at")
        {
            // dbstop at N without a file: accepted and nothing set (R2025b, headless: no current editor file).
            if (words.Length < 2)
            {
                throw BadOption(line, col);
            }

            return;
        }

        string file;
        if (words[0] == "in")
        {
            if (words.Length < 2)
            {
                throw BadOption(line, col);
            }

            file = words[1];
            i = 2;
        }
        else
        {
            file = words[0];
            i = 1;
        }

        string? lineWord = null;
        string? condition = null;
        if (i < words.Length && words[i] == "at")
        {
            if (i + 1 >= words.Length)
            {
                throw BadOption(line, col);
            }

            lineWord = words[i + 1];
            i += 2;
        }
        else if (i < words.Length && words[i] != "if" && words[0] != "in")
        {
            // dbstop('file', '4'): the functional form without the keywords.
            lineWord = words[i];
            i++;
        }

        if (i < words.Length && words[i] == "if")
        {
            if (i + 1 >= words.Length)
            {
                throw BadOption(line, col);
            }

            condition = string.Join(' ', words.Skip(i + 1));
            i = words.Length;
        }

        if (i < words.Length)
        {
            throw BadOption(line, col);
        }

        (string path, string? function) = DebugFileOf(interpreter, host, file, line, col);
        if (function is not null && lineWord is not null)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:dbstop:lineNumberAndLocalNestedCombinationInvalid",
                "Invalid option combination. Specify the line number or specify the local or nested function.");
        }

        int target = DebugLineOf(path, Path.GetFileNameWithoutExtension(file.Split('>')[0]), function, lineWord, line, col);
        store?.Set(path, target, condition ?? string.Empty);
    }

    // --- dbclear --------------------------------------------------------------------------------------

    private static void Dbclear(Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
        }

        string[] words = DebugWords(args, line, col);
        JgsBreakpointStore? store = host.Breakpoints;
        int i = 0;
        if (words[0] == "all")
        {
            if (words.Length == 1)
            {
                store?.ClearAll();
                return;
            }

            if (words[1] != "in" || words.Length != 3)
            {
                throw BadOption(line, col);
            }

            (string whole, _) = DebugFileOf(interpreter, host, words[2], line, col);
            store?.ClearFile(whole);
            return;
        }

        if (words[0] == "if")
        {
            (StopCondition? condition, string? identifier) = ParseStopIf(words, line, col);
            if (condition is { } known && store is not null && !store.ClearStopIf(known, identifier, out string? refusal))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:dbclear:cannotClearWhenAllEnabled", refusal!);
            }

            return;
        }

        string file;
        if (words[0] == "in")
        {
            if (words.Length < 2)
            {
                throw BadOption(line, col);
            }

            file = words[1];
            i = 2;
        }
        else
        {
            file = words[0];
            i = 1;
        }

        string? lineWord = null;
        if (i < words.Length && words[i] == "at")
        {
            if (i + 1 >= words.Length)
            {
                throw BadOption(line, col);
            }

            lineWord = words[i + 1];
            i += 2;
        }
        else if (i < words.Length && words[0] != "in")
        {
            lineWord = words[i];
            i++;
        }

        if (i < words.Length)
        {
            throw BadOption(line, col);
        }

        (string path, string? function) = DebugFileOf(interpreter, host, file, line, col);
        if (function is not null && lineWord is not null)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:dbstop:lineNumberAndLocalNestedCombinationInvalid",
                "Invalid option combination. Specify the line number or specify the local or nested function.");
        }

        if (function is null && lineWord is null)
        {
            store?.ClearFile(path);
            return;
        }

        if (function is not null)
        {
            // dbclear in file>fn: every breakpoint of that function.
            JgsSourceMap map = MapOf(path, line, col);
            JgsFunctionSpan? span = map.Functions.FirstOrDefault(f => f.Name == function)
                ?? throw new JgsRuntimeException(line, col, "MATLAB:dispatcher:cannotFindFunction", $"Cannot find function \"{function}\" within \"{Path.GetFileNameWithoutExtension(file.Split('>')[0])}\".");
            if (store is not null)
            {
                foreach (JgsBreakpoint breakpoint in store.In(path))
                {
                    if (breakpoint.Line >= span.Signature && breakpoint.Line <= span.End)
                    {
                        store.Clear(path, breakpoint.Line);
                    }
                }
            }

            return;
        }

        int target = DebugLineOf(path, Path.GetFileNameWithoutExtension(file), null, lineWord, line, col);
        store?.Clear(path, target);
    }

    // --- dbstatus -------------------------------------------------------------------------------------

    private static JgsValue[] Dbstatus(Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        string[] words = DebugWords(args, line, col);
        bool completeNames = false;
        string? filter = null;
        foreach (string word in words)
        {
            if (word.Equals("-completenames", StringComparison.OrdinalIgnoreCase))
            {
                completeNames = true;
            }
            else if (filter is null)
            {
                filter = DebugFileOf(interpreter, host, word, line, col).Path;
            }
            else
            {
                throw BadOption(line, col);
            }
        }

        JgsBreakpointStore? store = host.Breakpoints;
        var entries = new List<(string Name, string File, List<JgsBreakpoint> Lines)>();
        if (store is not null)
        {
            foreach (IGrouping<string, JgsBreakpoint> file in store.All.Where(b => filter is null || string.Equals(b.File, filter, StringComparison.OrdinalIgnoreCase)).GroupBy(static b => b.File))
            {
                JgsSourceMap? map = TryMapOf(file.Key);
                foreach (JgsBreakpoint breakpoint in file)
                {
                    string name = map?.NameAt(breakpoint.Line, completeNames) ?? (completeNames ? file.Key : Path.GetFileNameWithoutExtension(file.Key));
                    int at = entries.FindIndex(e => e.Name == name && e.File == file.Key);
                    if (at < 0)
                    {
                        entries.Add((name, file.Key, []));
                        at = entries.Count - 1;
                    }

                    entries[at].Lines.Add(breakpoint);
                }
            }
        }

        entries.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        IReadOnlyList<(StopCondition Condition, IReadOnlyList<string> Identifiers)> conditions =
            store is null || filter is not null ? [] : store.Conditions;

        // The printed form (R2025b's words, the opentoline links left out: JGraph's console has none).
        var text = new StringBuilder();
        foreach ((string name, _, List<JgsBreakpoint> lines) in entries)
        {
            string numbers = string.Join(", ", lines.Select(static b => b.Line.ToString(CultureInfo.InvariantCulture)));
            text.Append(lines.Count == 1 ? $"Breakpoint for {name} is on line {numbers}." : $"Breakpoints for {name} are on lines {numbers}.").Append('\n');
            bool conditional = false;
            foreach (JgsBreakpoint breakpoint in lines.Where(static b => b.Condition.Length > 0))
            {
                text.Append($"  Breakpoint on line {breakpoint.Line} has conditional expression \"{breakpoint.Condition}\".").Append('\n');
                conditional = true;
            }

            if (conditional)
            {
                text.Append('\n');
            }
        }

        foreach ((StopCondition condition, IReadOnlyList<string> identifiers) in conditions)
        {
            string word = JgsBreakpointStore.Word(condition);
            string with = identifiers.Count == 0 || identifiers[0] == "all" ? string.Empty
                : identifiers.Count == 1 ? $" with identifier {identifiers[0]}"
                : $" with identifiers {string.Join(", ", identifiers)}"; // several identifiers: not measured
            text.Append($"Stop if {word}{with}.").Append('\n');
        }

        if (wanted == 0)
        {
            if (text.Length > 0)
            {
                host.print(text.ToString().TrimEnd('\n'));
            }

            return [];
        }

        // The struct array: name, file, line, anonymous, expression, cond, identifier (R2025b's fields and order).
        var rows = new List<Dictionary<string, JgsValue>>();
        foreach ((string name, string file, List<JgsBreakpoint> lines) in entries)
        {
            rows.Add(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["name"] = JgsValue.Str(name),
                ["file"] = JgsValue.Str(file),
                ["line"] = JgsMatrix.FromColumnMajor(lines.Select(static b => (double)b.Line).ToArray(), 1, lines.Count),
                ["anonymous"] = JgsMatrix.FromColumnMajor(new double[lines.Count], 1, lines.Count),
                ["expression"] = JgsValue.Cell(lines.Select(static b => JgsValue.Str(b.Condition)).ToArray()),
                ["cond"] = JgsValue.Str(string.Empty),
                ["identifier"] = JgsValue.Cell([]),
            });
        }

        foreach ((StopCondition condition, IReadOnlyList<string> identifiers) in conditions)
        {
            rows.Add(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["name"] = JgsValue.Str(string.Empty),
                ["file"] = JgsValue.Str(string.Empty),
                ["line"] = JgsMatrix.FromColumnMajor([], 0, 0),
                ["anonymous"] = JgsMatrix.FromColumnMajor([], 0, 0),
                ["expression"] = JgsValue.Cell([]),
                ["cond"] = JgsValue.Str(JgsBreakpointStore.Word(condition)),
                ["identifier"] = JgsValue.Cell(identifiers.Select(JgsValue.Str).ToArray()),
            });
        }

        return [JgsValue.StructArray(new JgsStructArray(rows.ToArray(), DbstatusFields), rows.Count, 1)];
    }

    private static readonly string[] DbstatusFields = ["name", "file", "line", "anonymous", "expression", "cond", "identifier"];

    // --- dbstack --------------------------------------------------------------------------------------

    private static JgsValue[] Dbstack(Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        string[] words = DebugWords(args, line, col);
        bool completeNames = false;
        int skip = 0;
        foreach (string word in words)
        {
            if (word.Equals("-completenames", StringComparison.OrdinalIgnoreCase))
            {
                completeNames = true;
            }
            else if (int.TryParse(word, NumberStyles.None, CultureInfo.InvariantCulture, out int n))
            {
                skip = n;
            }
            else
            {
                throw BadOption(line, col);
            }
        }

        // At the K>> prompt the stack is the paused one, the innermost frame at the paused line; running code reports its own.
        // The base workspace's own statement is no frame: a pause reached from the prompt lists the functions alone.
        List<(string Name, string File, int Line)> frames = host.Debugger is { IsPaused: true } paused
            ? paused.GetCallStack().Where(static f => f.SourceId.Length > 0 || f.FunctionName != "(script)").Select(static f => (f.FunctionName, f.SourceId, f.Line)).ToList()
            : interpreter.CurrentStack(line);
        if (skip > 0)
        {
            frames.RemoveRange(0, Math.Min(skip, frames.Count));
        }

        if (wanted == 0)
        {
            var text = new StringBuilder();
            foreach ((string name, string file, int at) in frames)
            {
                // The printed frame is qualified by its package and a local function by its file
                // (pkg.stkloc>inner), where the struct's name stays bare (R2025b, measured in P1).
                string shown = completeNames && file.Length > 0 ? $"{file}>{name}" : interpreter.PrintedFrameName(name, file);
                text.Append($"> In {shown} (line {at})").Append('\n');
            }

            if (text.Length > 0)
            {
                host.print(text.ToString().TrimEnd('\n'));
            }

            return [];
        }

        return wanted >= 2 ? [StackValue(frames), JgsValue.Number(1)] : [StackValue(frames)];
    }

    // --- dbtype ---------------------------------------------------------------------------------------

    private static void Dbtype(Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
        }

        string[] words = DebugWords(args, line, col);
        if (words.Length > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        string? path = TryDebugFile(interpreter, host, words[0]);
        if (path is null)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:type:fileNotFound", $"File '{words[0]}' not found.");
        }

        string[] lines = JgsMlapp.ReadSource(path).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0)
        {
            lines = lines[..^1];
        }

        int first = 1;
        int last = lines.Length;
        if (words.Length == 2)
        {
            string[] range = words[1].Split(':');
            if (range.Length is 0 or > 2 || !int.TryParse(range[0], NumberStyles.None, CultureInfo.InvariantCulture, out first)
                || (range.Length == 2 && !int.TryParse(range[1], NumberStyles.None, CultureInfo.InvariantCulture, out last)))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:dbtype:badLineRange", $"Invalid line range '{words[1]}'.");
            }

            if (range.Length == 1)
            {
                last = first;
            }

            if (first > lines.Length)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:dbtype:dbLineBeyondEnd", "Line number(s) beyond end-of-file.");
            }
        }

        var text = new StringBuilder("\n");
        for (int n = Math.Max(first, 1); n <= Math.Min(last, lines.Length); n++)
        {
            text.Append(n.ToString(CultureInfo.InvariantCulture).PadRight(6)).Append(lines[n - 1]).Append('\n');
        }

        host.print(text.ToString().TrimEnd('\n'));
    }

    // --- shared ---------------------------------------------------------------------------------------

    /// <summary>The words of a debug command: every argument a text scalar (R2025b: <c>MATLAB:string:MustBeStringScalarOrCharacterVector</c>).</summary>
    private static string[] DebugWords(IReadOnlyList<JgsValue> args, int line, int col)
    {
        var words = new string[args.Count];
        for (int i = 0; i < args.Count; i++)
        {
            if (!IsTextScalar(args[i]))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:string:MustBeStringScalarOrCharacterVector", "Argument must be a text scalar.");
            }

            words[i] = TextOf(args[i]);
        }

        return words;
    }

    private static JgsRuntimeException BadOption(int line, int col) =>
        new(line, col, "MATLAB:badopt", "Unknown command option.");

    /// <summary>
    /// <c>if error</c>, <c>if caught error</c>, <c>if warning</c>, <c>if naninf</c> or <c>if infnan</c>, each with one identifier at most
    /// (a second word is <c>MATLAB:badopt</c>, as is an identifier without a colon); an unknown condition is accepted and
    /// ignored, as R2025b accepts <c>dbstop if bogus</c> (W0).
    /// </summary>
    private static (StopCondition? Condition, string? Identifier) ParseStopIf(string[] words, int line, int col)
    {
        if (words.Length < 2)
        {
            throw BadOption(line, col);
        }

        int next = 2;
        StopCondition? condition = words[1] switch
        {
            "error" => StopCondition.Error,
            "warning" => StopCondition.Warning,
            "naninf" or "infnan" => StopCondition.NanInf,
            "caught" when words.Length > 2 && words[2] == "error" => StopCondition.CaughtError,
            _ => null,
        };
        if (condition == StopCondition.CaughtError)
        {
            next = 3;
        }

        if (condition is null)
        {
            return (null, null);
        }

        string? identifier = null;
        if (next < words.Length)
        {
            identifier = words[next];
            if (!identifier.Contains(':', StringComparison.Ordinal) || next + 1 < words.Length || condition == StopCondition.NanInf)
            {
                throw BadOption(line, col);
            }
        }

        return (condition, identifier);
    }

    /// <summary>
    /// The file a debug word names, and the function after its <c>&gt;</c>: a rooted path that exists, a name the path
    /// finds (<c>which</c>'s order, an <c>.mlapp</c> before an <c>.m</c>), a name with its extension; else
    /// <c>MATLAB:dispatcher:cannotFindFunction</c> in R2025b's words.
    /// </summary>
    private static (string Path, string? Function) DebugFileOf(Interpreter interpreter, JGraphScriptGlobals host, string asked, int line, int col)
    {
        string file = asked;
        string? function = null;
        int arrow = asked.IndexOf('>');
        if (arrow > 0)
        {
            file = asked[..arrow];
            function = asked[(arrow + 1)..];
        }

        string? path = TryDebugFile(interpreter, host, file)
            ?? throw new JgsRuntimeException(line, col, "MATLAB:dispatcher:cannotFindFunction", $"Cannot find function \"{file}\".");
        return (path, function);
    }

    private static string? TryDebugFile(Interpreter interpreter, JGraphScriptGlobals host, string file)
    {
        if (file.Length == 0)
        {
            return null;
        }

        if (Path.IsPathRooted(file) && File.Exists(file))
        {
            return Path.GetFullPath(file);
        }

        string? found = null;
        try
        {
            found = interpreter.FunctionPath?.FindFile(file);
        }
        catch (ArgumentException)
        {
            // a name the path cannot take: tried beside the current folder below
        }

        if (found is not null)
        {
            return found;
        }

        // A package member by its dotted name: dbstop in pkg.f (packages plan, P1, measured).
        if (file.Contains('.', StringComparison.Ordinal) && file.IndexOfAny(['/', '\\']) < 0
            && interpreter.Resolver.Dotted(file) is { File: { Length: > 0 } packaged })
        {
            return packaged;
        }

        foreach (string extension in Path.GetExtension(file).Length > 0 ? [string.Empty] : (string[])[string.Empty, .. JgsFunctionPath.CodeExtensions])
        {
            string beside = host.Resolve(file + extension);
            if (File.Exists(beside))
            {
                return Path.GetFullPath(beside);
            }
        }

        return null;
    }

    /// <summary>The source map of a code file for the debug words; a file that does not parse is refused as a load error of that file.</summary>
    private static JgsSourceMap MapOf(string path, int line, int col)
    {
        try
        {
            return JgsSourceMap.Load(path);
        }
        catch (JgsSyntaxException error)
        {
            throw new JgsRuntimeException(line, col, $"Error: File: {Path.GetFileName(path)} Line: {error.Line} Column: {error.Column}\n{error.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new JgsRuntimeException(line, col, $"Cannot read '{path}': {ex.Message}");
        }
    }

    private static JgsSourceMap? TryMapOf(string path)
    {
        try
        {
            return File.Exists(path) ? JgsSourceMap.Load(path) : null;
        }
        catch (Exception ex) when (ex is JgsException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// Where a breakpoint lands in <paramref name="path"/>: the first executable line of <paramref name="function"/> when one is named,
    /// else the first executable line at or after <paramref name="lineWord"/> (the file's first with none), refused past the last
    /// (<c>MATLAB:lineBeyondFileEnd</c>); a word that is no line number is refused as a function the file does not have.
    /// </summary>
    private static int DebugLineOf(string path, string stem, string? function, string? lineWord, int line, int col)
    {
        JgsSourceMap map = MapOf(path, line, col);
        if (function is not null)
        {
            return map.FirstLineOf(function)
                ?? throw new JgsRuntimeException(line, col, "MATLAB:dispatcher:cannotFindFunction", $"Cannot find function \"{function}\" within \"{stem}\".");
        }

        int asked = 1;
        if (lineWord is not null && !int.TryParse(lineWord, NumberStyles.None, CultureInfo.InvariantCulture, out asked))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:dispatcher:cannotFindFunction", $"Cannot find function \"{lineWord}\" within \"{stem}\".");
        }

        return map.Snap(Math.Max(asked, 1))
            ?? throw new JgsRuntimeException(line, col, "MATLAB:lineBeyondFileEnd", "Setting or clearing breakpoints past the start of the last expression is not supported.");
    }
}
