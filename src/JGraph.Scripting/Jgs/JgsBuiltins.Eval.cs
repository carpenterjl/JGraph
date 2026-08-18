namespace JGraph.Scripting.Jgs;

/// <summary>
/// The builtins that need the running interpreter itself: <c>eval</c> and its family, the workspace
/// questions (<c>exist</c>, <c>who</c>), argument checking, and the error history. Like the operator
/// function forms, only something holding the interpreter can declare these, so the two workspace
/// owners call <see cref="RegisterEvalBuiltins"/> right after they build one.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// Every name <see cref="RegisterEvalBuiltins"/> declares that <c>CreateGlobals</c> does not, so
    /// the editor's name list can be told about them the same way it is told about <c>run</c>.
    /// (<c>warning</c> is left out: it is re-declared here, but it already exists.)
    /// </summary>
    internal static IReadOnlyList<string> EvalBuiltinNames { get; } =
    [
        "eval", "evalc", "evalin", "assignin", "str2func", "str2num",
        "exist", "who", "which", "narginchk", "nargoutchk", "nargchk",
        "lasterr", "lasterror", "lastwarn",

        // M62: the error objects — error is re-declared over its plain form, the rest are new.
        "error", "MException", "throw", "rethrow", "throwAsCaller",
        "func2str", "functions", "mfilename", "inputname",

        // M62: the search path is interpreter state, so the builtins that manage it are declared here.
        "addpath", "rmpath", "genpath", "pathsep",

        // M58: the legacy function plotters take their function as text, which needs the interpreter
        // to turn into a handle — the same reason eval itself is declared here.
        "ezplot", "ezplot3", "ezpolar", "ezsurf", "ezmesh", "ezsurfc", "ezmeshc",
        "ezcontour", "ezcontourf",

        // M68: the class questions need the interpreter, because what classes exist is interpreter
        // state — a class is defined by loading a file, exactly as a function is.
        "addCause", "isobject", "properties", "methods", "metaclass",
    ];

    /// <summary>Declares the interpreter-backed builtins into <paramref name="env"/>.</summary>
    internal static void RegisterEvalBuiltins(
        JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host, JgsDialect dialect)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        void DefineBare(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { AutoCallsBare = true }));

        RegisterPathBuiltins(env, interpreter, host);
        RegisterEvaluation(Define, env, interpreter, host);
        RegisterWorkspaceQuestions(Define, env, interpreter, host);
        RegisterErrorHistory(Define, env, interpreter);
        RegisterErrorObjects(Define, interpreter);
        RegisterIntrospection(Define, DefineBare, interpreter, host);
        RegisterLegacyFunctionPlotBuiltins(env, interpreter);
        RegisterClassBuiltins(env, interpreter);
        _ = dialect;
    }

    // --- Introspection ----------------------------------------------------------------------------

    /// <summary>
    /// The builtins that ask a function or a script about itself (M39). Each needs something the
    /// interpreter holds and nothing else does: the handle's own syntax tree, the running file, or
    /// the expression the current call was written as.
    /// </summary>
    private static void RegisterIntrospection(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define,
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> DefineBare,
        Interpreter interpreter, JGraphScriptGlobals host)
    {
        Define("func2str", (args, line, col) =>
        {
            Arity("func2str", args, 1, line, col);
            return JgsValue.Str(SourceTextOf("func2str", args[0], line, col));
        });

        Define("functions", (args, line, col) =>
        {
            Arity("functions", args, 1, line, col);
            if (args[0].Type != JgsType.Function)
            {
                throw new JgsRuntimeException(line, col, "functions expects a function handle.");
            }

            bool anonymous = args[0].AsCallable is AnonymousFunction;
            return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["function"] = JgsValue.Str(SourceTextOf("functions", args[0], line, col).TrimStart('@')),
                ["type"] = JgsValue.Str(anonymous ? "anonymous" : args[0].AsCallable is UserFunction ? "simple" : "builtin"),
                ["file"] = JgsValue.Str(args[0].AsCallable is UserFunction ? host.RunScriptPath ?? string.Empty : string.Empty),
            });
        });

        // mfilename answers when its bare name is mentioned, the way MATLAB's does; without that,
        // disp(mfilename) would hand disp the function rather than the file's name.
        DefineBare("mfilename", (args, line, col) =>
        {
            ArityRange("mfilename", args, 0, 1, line, col);
            string? path = host.RunScriptPath;

            // Bare mfilename is the name without its extension; 'fullpath' asks for the whole path
            // minus the extension. Code typed at the prompt has no file, and reports nothing.
            if (string.IsNullOrEmpty(path))
            {
                return JgsValue.Str(string.Empty);
            }

            bool full = args.Count == 1 && Str("mfilename", args, 0, line, col) == "fullpath";
            string withoutExtension = Path.Combine(
                Path.GetDirectoryName(path) ?? string.Empty, Path.GetFileNameWithoutExtension(path));

            return JgsValue.Str(full ? withoutExtension : Path.GetFileNameWithoutExtension(path));
        });

        Define("inputname", (args, line, col) =>
        {
            Arity("inputname", args, 1, line, col);
            int which = Count("inputname", args, 0, line, col);
            if (interpreter.CurrentCall is not { } call)
            {
                throw new JgsRuntimeException(line, col, "inputname can only be used inside a function.");
            }

            if (which < 1 || which > call.Arguments.Count)
            {
                throw new JgsRuntimeException(line, col,
                    $"inputname: argument {which} does not exist; the call passed {call.Arguments.Count}.");
            }

            // An argument that was not written as a plain variable has no name to report, and
            // MATLAB returns the empty string rather than inventing one.
            return JgsValue.Str(call.Arguments[which - 1] is VariableExpr variable ? variable.Name : string.Empty);
        });
    }

    /// <summary>The source text of a function handle, as func2str prints it.</summary>
    private static string SourceTextOf(string name, JgsValue value, int line, int col)
    {
        if (value.Type != JgsType.Function)
        {
            throw new JgsRuntimeException(line, col, $"{name} expects a function handle.");
        }

        return value.AsCallable switch
        {
            AnonymousFunction anonymous => AstPrinter.Print(anonymous.Declaration),
            { } callable => "@" + callable.Name,
            _ => throw new JgsRuntimeException(line, col, $"{name} expects a function handle."),
        };
    }

    // --- Evaluating text --------------------------------------------------------------------------

    private static void RegisterEvaluation(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define,
        JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        Define("eval", (args, line, col) =>
        {
            ArityRange("eval", args, 1, 2, line, col);

            // eval(try, catch) runs the second string only if the first fails — MATLAB's pre-try/catch
            // way of writing a recovery, and still in plenty of code.
            if (args.Count == 2)
            {
                try
                {
                    return interpreter.EvaluateSource(Str("eval", args, 0, line, col), interpreter.CurrentFrame, line, col);
                }
                catch (JgsRuntimeException error)
                {
                    interpreter.LastError = error.Message;
                    return interpreter.EvaluateSource(Str("eval", args, 1, line, col), interpreter.CurrentFrame, line, col);
                }
            }

            return interpreter.EvaluateSource(Str("eval", args, 0, line, col), interpreter.CurrentFrame, line, col);
        });

        // str2num evaluates its text as an expression, which is exactly what separates it from
        // str2double: '[1 2 3]' is a vector and '1+1' is 2. Text that does not evaluate answers
        // empty rather than failing, which is MATLAB's behaviour and the reason callers who only
        // ever have a number are steered to str2double instead.
        Define("str2num", (args, line, col) =>
        {
            Arity("str2num", args, 1, line, col);
            string text = Str("str2num", args, 0, line, col).Trim();
            if (text.Length == 0)
            {
                return JgsValue.Array([]);
            }

            try
            {
                return interpreter.EvaluateSource(text, interpreter.CurrentFrame, line, col);
            }
            catch (JgsRuntimeException)
            {
                return JgsValue.Array([]);
            }
        });

        Define("evalc", (args, line, col) =>
        {
            ArityRange("evalc", args, 1, 2, line, col);
            if (!host.BeginCapture())
            {
                throw new JgsRuntimeException(line, col, "evalc is already capturing output; it does not nest.");
            }

            string captured;

            try
            {
                interpreter.EvaluateSource(Str("evalc", args, 0, line, col), interpreter.CurrentFrame, line, col);
            }
            finally
            {
                // The buffer is closed even when the code failed part way through: leaving it open
                // would silently swallow the rest of the session's output.
                captured = host.EndCapture();
            }

            return JgsValue.Str(captured);
        });

        Define("evalin", (args, line, col) =>
        {
            Arity("evalin", args, 2, line, col);
            return interpreter.EvaluateSource(
                Str("evalin", args, 1, line, col), WorkspaceNamed("evalin", args, 0, interpreter, line, col), line, col);
        });

        Define("assignin", (args, line, col) =>
        {
            Arity("assignin", args, 3, line, col);
            WorkspaceNamed("assignin", args, 0, interpreter, line, col)
                .Declare(Str("assignin", args, 1, line, col), args[2]);
            return JgsValue.Null;
        });

        Define("str2func", (args, line, col) =>
        {
            Arity("str2func", args, 1, line, col);
            string text = Str("str2func", args, 0, line, col).Trim();

            // An @(…) body is a function to build; anything else is the name of one that exists.
            if (text.StartsWith('@'))
            {
                return interpreter.EvaluateSource(text, interpreter.CurrentFrame, line, col);
            }

            if (env.TryGet(text, out JgsValue found) && found.Type == JgsType.Function)
            {
                return found;
            }

            return interpreter.FunctionPath is { } search && search.TryResolve(text, out JgsValue onPath)
                ? onPath
                : throw new JgsRuntimeException(line, col, $"str2func: '{text}' is not a function.");
        });

        _ = host;
    }

    /// <summary>Resolves MATLAB's workspace words to an environment.</summary>
    private static JgsEnvironment WorkspaceNamed(
        string name, IReadOnlyList<JgsValue> args, int index, Interpreter interpreter, int line, int col) =>
        Str(name, args, index, line, col) switch
        {
            "base" => interpreter.Globals,

            // The workspace of whoever called the function that is asking. At the top level nothing
            // called it, and MATLAB's answer there is the base workspace.
            "caller" => interpreter.CallerFrame ?? interpreter.Globals,
            var other => throw new JgsRuntimeException(line, col, $"{name}: '{other}' is not 'base' or 'caller'."),
        };

    // --- Workspace questions ----------------------------------------------------------------------

    private static void RegisterWorkspaceQuestions(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define,
        JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        Define("exist", (args, line, col) =>
        {
            ArityRange("exist", args, 1, 2, line, col);
            string name = Str("exist", args, 0, line, col);
            string? kind = args.Count == 2 ? Str("exist", args, 1, line, col) : null;

            bool wantVariable = kind is null or "var";
            bool wantFile = kind is null or "file" or "dir";
            bool wantFunction = kind is null or "builtin";

            // MATLAB's return code is a category, not a boolean: 1 variable, 2 file, 5 builtin.
            if (wantVariable && interpreter.CurrentFrame.TryGet(name, out JgsValue found)
                && found.Type != JgsType.Function)
            {
                return JgsValue.Number(1);
            }

            if (wantFile)
            {
                string resolved = host.Resolve(name);
                if (File.Exists(resolved))
                {
                    return JgsValue.Number(2);
                }

                if (Directory.Exists(resolved))
                {
                    return JgsValue.Number(7);
                }
            }

            if (wantFunction && env.TryGet(name, out JgsValue callable) && callable.Type == JgsType.Function)
            {
                return JgsValue.Number(5);
            }

            // A file on the search path is MATLAB's category 2 — a file — even though calling it is
            // what a script actually does with it.
            if (kind is null or "file" && interpreter.FunctionPath?.Find(name) is not null)
            {
                return JgsValue.Number(2);
            }

            return JgsValue.Number(0);
        });

        Define("who", (args, line, col) =>
        {
            ArityRange("who", args, 0, 1, line, col);
            var names = new List<JgsValue>();
            foreach ((string name, JgsValue value) in interpreter.CurrentFrame.Locals)
            {
                // A workspace listing is variables, not the builtins sitting behind them.
                if (value.Type != JgsType.Function)
                {
                    names.Add(JgsValue.Str(name));
                }
            }

            names.Sort(static (a, b) => string.CompareOrdinal(a.AsString, b.AsString));
            return JgsValue.Cell(names.ToArray());
        });

        Define("which", (args, line, col) =>
        {
            Arity("which", args, 1, line, col);
            string name = Str("which", args, 0, line, col);
            if (env.TryGet(name, out JgsValue value) && value.Type == JgsType.Function)
            {
                return JgsValue.Str($"{name} is a built-in function.");
            }

            // The search path is asked before the plain file probe, because 'which helper' means the
            // file that would answer the name — not a file that happens to be called that.
            if (interpreter.FunctionPath?.Find(name) is { } onPath)
            {
                return JgsValue.Str(onPath);
            }

            string resolved = host.Resolve(name);
            return JgsValue.Str(File.Exists(resolved) ? resolved : string.Empty);
        });

        // narginchk and its relatives read the nargin/nargout the current frame was called with, so
        // they only mean anything inside a function — exactly as in MATLAB.
        void ArgumentCheck(string name, string counter, bool checkLow, bool checkHigh) =>
            Define(name, (args, line, col) =>
            {
                Arity(name, args, 2, line, col);
                if (!interpreter.CurrentFrame.TryGet(counter, out JgsValue count))
                {
                    throw new JgsRuntimeException(line, col, $"{name} can only be used inside a function.");
                }

                double actual = count.AsNumber;
                if (checkLow && actual < Num(name, args, 0, line, col))
                {
                    throw new JgsRuntimeException(line, col, "Not enough input arguments.");
                }

                if (checkHigh && actual > Num(name, args, 1, line, col))
                {
                    throw new JgsRuntimeException(line, col, "Too many input arguments.");
                }

                return JgsValue.Null;
            });

        ArgumentCheck("narginchk", "nargin", checkLow: true, checkHigh: true);
        ArgumentCheck("nargoutchk", "nargout", checkLow: true, checkHigh: true);
        ArgumentCheck("nargchk", "nargin", checkLow: true, checkHigh: true); // the pre-R2011 spelling
    }

    // --- Error history ----------------------------------------------------------------------------

    private static void RegisterErrorHistory(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define,
        JgsEnvironment env, Interpreter interpreter)
    {
        Define("lasterr", (args, line, col) =>
        {
            ArityRange("lasterr", args, 0, 1, line, col);
            string previous = interpreter.LastError;
            if (args.Count == 1)
            {
                interpreter.LastError = Str("lasterr", args, 0, line, col);
            }

            return JgsValue.Str(previous);
        });

        Define("lastwarn", (args, line, col) =>
        {
            ArityRange("lastwarn", args, 0, 1, line, col);
            string previous = interpreter.LastWarning;
            if (args.Count == 1)
            {
                interpreter.LastWarning = Str("lastwarn", args, 0, line, col);
            }

            return JgsValue.Str(previous);
        });

        // warning already exists; wrapping it here is what lets lastwarn report the message without
        // the warning builtin having to know the interpreter exists.
        if (env.TryGet("warning", out JgsValue existing) && existing.AsCallable is { } inner)
        {
            Define("warning", (args, line, col) =>
            {
                if (args.Count > 0 && args[0].Type == JgsType.String)
                {
                    interpreter.LastWarning = args[0].AsString;
                }

                return inner.Call(args, line, col);
            });
        }
    }
}
