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
        "exist", "who", "which", "builtin", "narginchk", "nargoutchk", "nargchk",

        // M145: feval resolves the name it is handed through the interpreter, so it moved here.
        "feval",
        "nargin", "nargout",
        "lasterr", "lasterror", "lastwarn", "refreshdata",

        // M62: the error objects — error is re-declared over its plain form, the rest are new.
        "error", "MException", "throw", "rethrow", "throwAsCaller",
        "func2str", "functions", "mfilename", "inputname",

        // M62: the search path is interpreter state, so the builtins that manage it are declared here.
        "path", "addpath", "rmpath", "genpath", "pathsep",

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
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body)));

        void DefineBare(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { AutoCallsBare = true }));

        RegisterPathBuiltins(env, interpreter, host);
        RegisterEvaluation(Define, env, interpreter, host);
        RegisterWorkspaceQuestions(Define, env, interpreter, host);
        RegisterErrorHistory(Define, env, interpreter, host);

        // builtin(name, args…) is its own callable rather than a delegate: everything it does — the
        // output count, the statement form, the call site — is the target's (M145, step 7).
        env.Builtins.Register("builtin", JgsValue.Function(new JgsBuiltinForwarder(interpreter)));
        RegisterErrorObjects(Define, interpreter, env, host, dialect);
        RegisterIntrospection(Define, DefineBare, interpreter, host);
        RegisterLegacyFunctionPlotBuiltins(env, interpreter);
        RegisterClassBuiltins(env, interpreter);

        // refreshdata belongs with the handle verbs and is registered here only because it is the one
        // of them that reads a workspace, which is a thing only the interpreter knows about.
        env.Builtins.Register("refreshdata", JgsValue.Function(new BuiltinFunction(
            "refreshdata", (args, line, col) => RefreshData(args, interpreter, line, col))
        { BindsAnsAsStatement = false, AutoCallsBare = true }));
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

            // A named handle reports what it captured: the kind of target, and the file it came
            // from — a path function's own file, not the run's (M145).
            IJgsCallable callable = args[0].AsCallable;
            string file = callable is NamedHandle named
                ? named.File ?? string.Empty
                : callable is UserFunction ? host.RunScriptPath ?? string.Empty : string.Empty;
            IJgsCallable target = callable is NamedHandle captured ? captured.Captured : callable;
            return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["function"] = JgsValue.Str(SourceTextOf("functions", args[0], line, col).TrimStart('@')),
                ["type"] = JgsValue.Str(target is AnonymousFunction ? "anonymous" : target is UserFunction ? "simple" : "builtin"),
                ["file"] = JgsValue.Str(file),
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

        // str2num evaluates its text inside brackets, which is exactly what separates it from
        // str2double: '[1 2 3]' is a vector, '1+1' is 2, and '1 2; 3 4' is a matrix because the
        // brackets make the spaces and the semicolon separators. Evaluated bare, as this once was,
        // '1,2' was two statements and answered the last one. A char matrix is one row of the
        // bracket per row of text. Text that does not evaluate answers the 0-by-0 empty rather than
        // failing, which is MATLAB's behaviour and the reason callers who only ever have a number
        // are steered to str2double instead; [x, ok] = str2num(...) says which happened.
        JgsValue[] EvaluateBracketed(IReadOnlyList<JgsValue> args, int line, int col)
        {
            Arity("str2num", args, 1, line, col);
            string[] rows = args[0].IsCharMatrix
                ? args[0].CharMatrixRows()
                : [Str("str2num", args, 0, line, col)];
            string source = "[" + string.Join(";", rows) + "]";

            try
            {
                JgsValue answer = interpreter.EvaluateSource(source, interpreter.CurrentFrame, line, col);
                if (answer.Type == JgsType.Null)
                {
                    answer = JgsEmpty.Zero();
                }
                else if (answer.Type == JgsType.Array && answer.ArrayLength == 1 && answer.ElementAt(0).Type == JgsType.Complex)
                {
                    answer = answer.ElementAt(0); // [3+4i] is the complex scalar, as 3+4i is
                }

                return [answer, JgsValue.Bool(true)];
            }
            catch (JgsException)
            {
                return [JgsEmpty.Zero(), JgsValue.Bool(false)];
            }
        }

        env.Builtins.Register("str2num", JgsValue.Function(new BuiltinFunction(
            "str2num", (args, line, col) => EvaluateBracketed(args, line, col)[0])
        {
            MultiOutput = (args, wanted, line, col) => EvaluateBracketed(args, line, col),
        }));

        // regexprep's ${...} replacements are MATLAB code — regexprep('hello', '^(.)', '${upper($1)}')
        // — so the builtin is declared again here with the interpreter to run them, and wrapped again
        // so that a container subject still maps element by element.
        env.Builtins.Register("regexprep", JgsValue.Function(new BuiltinFunction(
            "regexprep",
            (args, line, col) => ReplaceMatches(
                args,
                (code, l, c) => interpreter.EvaluateSource(code, interpreter.CurrentFrame, l, c),
                line, col))));
        MapTextSubject(env, new SubjectMap("regexprep", TextAnswer.Text));

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

        // The named workspace is installed as the current frame while the text runs, so a function
        // the text calls sees it as its caller (M145); the text's own names still resolve from the
        // file that called evalin, as MATLAB's do.
        Define("evalin", (args, line, col) =>
        {
            Arity("evalin", args, 2, line, col);
            return interpreter.EvaluateSourceIn(
                Str("evalin", args, 1, line, col), WorkspaceNamed("evalin", args, 0, interpreter, line, col), line, col);
        });

        Define("assignin", (args, line, col) =>
        {
            Arity("assignin", args, 3, line, col);
            JgsEnvironment target = WorkspaceNamed("assignin", args, 0, interpreter, line, col);
            string name = Str("assignin", args, 1, line, col);

            // An anonymous function's workspace is the snapshot its handle took: a captured name can
            // be changed, a new one cannot be added, and MATLAB refuses with these words (M145).
            if (target.IsStaticWorkspace && !target.IsBoundWithinStaticWorkspace(name))
            {
                throw new JgsRuntimeException(line, col, $"Attempt to add \"{name}\" to a static workspace.");
            }

            target.Declare(name, args[2]);
            return JgsValue.Null;
        });

        // feval takes a handle *or a name*, and answers as many outputs as it is asked for. Both
        // halves were missing until M69's form probe ran the documented syntaxes: `feval('sin', x)`
        // is the form MATLAB documents first and this refused it by type, and `[q, r] = feval(@f, x)`
        // silently produced one value because the entry carried no MultiOutput body — a wrong answer
        // rather than an error, which is the worse of the two failures.
        env.Builtins.Register("feval", JgsValue.Function(new BuiltinFunction(
            "feval",
            (args, line, col) => FevalTarget(interpreter, args, line, col)
                .Call(args.Skip(1).ToArray(), line, col))
        {
            MultiOutput = (args, wanted, line, col) =>
            {
                IJgsCallable target = FevalTarget(interpreter, args, line, col);
                IReadOnlyList<JgsValue> rest = args.Skip(1).ToArray();
                return target is IJgsMultiCallable several
                    ? several.CallMultiple(rest, wanted, line, col)
                    : [target.Call(rest, line, col)];
            },
        }));

        Define("str2func", (args, line, col) =>
        {
            Arity("str2func", args, 1, line, col);
            string text = Str("str2func", args, 0, line, col).Trim();

            // An @(…) body is a function to build; anything else is the name of one that exists.
            if (text.StartsWith('@'))
            {
                return interpreter.EvaluateSource(text, interpreter.CurrentFrame, line, col);
            }

            // The same handle @name would make where the call stands (M145).
            return interpreter.TryMakeHandle(text, interpreter.CurrentFrame, out JgsValue handle)
                ? handle
                : throw new JgsRuntimeException(line, col, $"str2func: '{text}' is not a function.");
        });

        _ = host;
    }

    /// <summary>
    /// What <c>feval</c> is being asked to call: a function handle, or the name of one as text.
    /// </summary>
    /// <remarks>
    /// MATLAB documents <c>feval(name, x1, ..., xn)</c> before the handle form, and a ported script
    /// is as likely to hold the name in a variable as the handle. The name resolves the way the same
    /// name written as a call would — Invoke mode, from the frame the call is made in — so a path
    /// file answers to it as readily as a builtin (M145; it used to be refused by name).
    /// </remarks>
    private static IJgsCallable FevalTarget(
        Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "feval needs a function to call.");
        }

        if (args[0].Type == JgsType.Function)
        {
            return args[0].AsCallable;
        }

        if (args[0].Type == JgsType.String)
        {
            // Both phases, as a written call: a bound handle is called, and anything else is resolved
            // with the classes of the arguments feval will pass on.
            string name = args[0].AsString;
            Resolution found = interpreter.Resolver.Invoke(name, interpreter.CurrentFrame);
            if (found.Layer != ResolutionLayer.Bound)
            {
                found = interpreter.Resolver.Invoke(name, found, args.Skip(1).ToArray());
            }

            if (found.Found && found.Value.Type == JgsType.Function)
            {
                return found.Value.AsCallable;
            }

            throw new JgsRuntimeException(line, col, $"feval: '{name}' is not a function.");
        }

        throw new JgsRuntimeException(
            line, col, $"feval expects a function handle or a function name, but got a {args[0].TypeName}.");
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
        // nargin and nargout are two different questions wearing one name. Inside a function body
        // they are the *variables* a call declared, bound by JgsCallable when the frame opens; called
        // with an argument they ask about a function that is not running. The local binding shadows
        // this one, which is MATLAB's arrangement and not a collision to be resolved.
        Define("nargin", (args, line, col) =>
            JgsValue.Number(DeclaredArgumentCount("nargin", args, env, interpreter, line, col)));

        Define("nargout", (args, line, col) =>
            JgsValue.Number(DeclaredArgumentCount("nargout", args, env, interpreter, line, col)));

        // exist and which ask Query mode (M145, step 7): every layer that holds the name as a
        // function, in the resolver's order with no argument to dispatch on — a nested or local
        // function, the running file's private/, the current folder, the addpath folders, the
        // built-in. In the JGS dialect the walk is the whole order, and a function anywhere in it
        // is the dialect's "built-in" answer, as it always was.
        bool IsBuiltinHere(string name) =>
            interpreter.Dialect.IsMatlab
                ? interpreter.Resolver.BuiltinOf(name) is { Type: JgsType.Function }
                : interpreter.Resolver.Lookup(name, interpreter.CurrentFrame).Value is { Type: JgsType.Function };

        bool AFileAnswers(string name) =>
            interpreter.Dialect.IsMatlab
            && interpreter.Resolver.Query(name, interpreter.CurrentFrame)
                .Any(found => found.Layer != ResolutionLayer.Builtin);

        Define("exist", (args, line, col) =>
        {
            ArityRange("exist", args, 1, 2, line, col);
            string name = Str("exist", args, 0, line, col);
            string? kind = args.Count == 2 ? Str("exist", args, 1, line, col) : null;

            bool wantVariable = kind is null or "var";
            bool wantBuiltin = kind is null or "builtin";
            bool wantFile = kind is null or "file";
            bool wantFolder = kind is null or "file" or "dir";

            // MATLAB's return code is a category, not a boolean: 1 variable, 2 file, 5 builtin.
            if (wantVariable && interpreter.CurrentFrame.TryGet(name, out JgsValue found)
                && found.Type != JgsType.Function)
            {
                return JgsValue.Number(1);
            }

            // A built-in is 5 whether or not a file shadows it — R2025b answers 5 for exist('max')
            // beside a max.m that takes every call — and it outranks a file or a folder of the same
            // name when nothing said which kind was meant: exist('fix') beside a folder called fix
            // is 5, and only exist('fix', 'dir') reaches the folder (M109). exist('sin', 'file') is
            // 0: naming 'file' asks about the disk alone. (exist('mean') is 5 here and 2 in MATLAB,
            // whose mean is a .m file — the recorded divergence.)
            if (wantBuiltin && IsBuiltinHere(name))
            {
                return JgsValue.Number(5);
            }

            // A function the resolver would run from a file — a local function of the running file
            // included, which R2025b also reports as 2 — before a plain file of that exact name.
            if (wantFile && AFileAnswers(name))
            {
                return JgsValue.Number(2);
            }

            string resolved = host.Resolve(name);
            if (wantFile && File.Exists(resolved))
            {
                return JgsValue.Number(2);
            }

            if (wantFolder && Directory.Exists(resolved))
            {
                return JgsValue.Number(7);
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

        // which(name) is the layer that would answer the name; which(name, '-all') is every layer
        // that holds it, files first, as a cell column — the shape R2025b hands back, and 0-by-0
        // when nothing does. A built-in keeps JGraph's phrasing: there is no file path to print.
        Define("which", (args, line, col) =>
        {
            ArityRange("which", args, 1, 2, line, col);
            string first = Str("which", args, 0, line, col);
            string? second = args.Count == 2 ? Str("which", args, 1, line, col) : null;
            bool all = second is not null;
            string name = second switch
            {
                null => first,
                "-all" => first,
                _ when first == "-all" => second,
                _ => throw new JgsRuntimeException(line, col, $"which: '{second}' is not an option; only '-all' is."),
            };

            var where = new List<string>();
            foreach (Resolution found in interpreter.Resolver.Query(name, interpreter.CurrentFrame))
            {
                where.Add(found.Layer == ResolutionLayer.Builtin
                    ? $"{name} is a built-in function."
                    : found.File is { Length: > 0 } file ? file : $"{name} is a local function.");
                if (!all)
                {
                    break;
                }
            }

            // Not a function at all: a data file the name spells out, when there is one.
            if (where.Count == 0)
            {
                string resolved = host.Resolve(name);
                if (File.Exists(resolved))
                {
                    where.Add(resolved);
                }
            }

            return all ? CellColumn(where) : JgsValue.Str(where.Count > 0 ? where[0] : string.Empty);
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
        JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
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

        // [msg, id] = lastwarn reads what warning recorded on the host, shown or suppressed; one
        // argument replaces the message and clears the identifier, two replace both (R2025b).
        JgsValue[] LastWarn(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
        {
            ArityRange("lastwarn", args, 0, 2, line, col);
            JgsWarningState state = host.Warnings;
            string message = state.LastMessage;
            string identifier = state.LastIdentifier;
            if (args.Count >= 1)
            {
                state.Record(
                    args.Count == 2 ? Str("lastwarn", args, 1, line, col) : string.Empty,
                    Str("lastwarn", args, 0, line, col));
            }

            return wanted >= 2 ? [JgsValue.Str(message), JgsValue.Str(identifier)] : [JgsValue.Str(message)];
        }

        env.Builtins.Register("lastwarn", JgsValue.Function(new BuiltinFunction("lastwarn",
            (args, line, col) => LastWarn(args, 1, line, col)[0])
        {
            MultiOutput = LastWarn,
        }));
    }

    /// <summary>
    /// <c>nargin(f)</c> and <c>nargout(f)</c>: how many inputs or outputs a function <em>declares</em>,
    /// which is a different question from how many a running call was given (M122).
    /// </summary>
    /// <remarks>
    /// <para>
    /// MATLAB's sign convention is kept: a declaration ending in <c>varargin</c> or <c>varargout</c>
    /// answers a negative number, <c>-(fixed + 1)</c>, so <c>-1</c> means "any number" and <c>-3</c>
    /// means "two, then any number". A caller that only wants the fixed count reads
    /// <c>abs(n) - 1</c> when <c>n</c> is negative, which is what MATLAB's own documentation says.
    /// </para>
    /// <para>
    /// For a function written in a script or a file the answer is read from its own header, so it is
    /// the same answer MATLAB gives for the same file. For a builtin it is read from
    /// <see cref="JgsBuiltinCatalog"/> — the signature <c>help</c> prints and the editor completes —
    /// because a builtin here has no header to read: it validates its arguments as it runs. That is a
    /// recorded divergence rather than a defect. MATLAB's number describes MATLAB's implementation of
    /// the name and this one describes JGraph's, and the two are not the same function; of the 1,641
    /// names both engines carry, 671 answer the same number.
    /// </para>
    /// <para>
    /// <c>nargout</c> of a builtin is the one place the answer is not read from the catalog, because
    /// the catalog does not describe outputs. It is read from the registration instead, where it is a
    /// fact rather than a guess: a builtin with no multiple-output form has exactly one output, and
    /// one that has such a form answers however many the caller asks for.
    /// </para>
    /// </remarks>
    private static double DeclaredArgumentCount(
        string verb, IReadOnlyList<JgsValue> args, JgsEnvironment env, Interpreter interpreter,
        int line, int col)
    {
        Arity(verb, args, 1, line, col);

        JgsValue given = args[0];
        if (IsTextScalar(given))
        {
            // Query mode: the first layer holding the name as a function, the workspace walk before
            // the files, as the resolver orders them.
            string name = TextOf(given);
            given = interpreter.Resolver.Query(name, env).FirstOrDefault(Resolution.None).Value;
        }

        // A named handle answers for what it captured, which has a header — or a catalog entry — to read.
        if (given.Type == JgsType.Function && given.AsCallable is NamedHandle named)
        {
            given = JgsValue.Function(named.Captured);
        }

        if (given.Type != JgsType.Function)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginout:notValidMfile",
                "Not a valid MATLAB file.");
        }

        bool inputs = verb == "nargin";
        switch (given.AsCallable)
        {
            case UserFunction user:
                return Counted(inputs ? user.Declaration.Parameters : user.Declaration.Outputs, inputs);

            case AnonymousFunction anonymous:
                // A handle names no outputs: it hands back however many the body it wraps hands
                // back, which is what MATLAB's -1 says and is measured rather than assumed —
                // nargout(@(x) x) answers -1 in R2024a, not 1.
                return inputs ? Counted(anonymous.Declaration.Parameters, true) : -1;

            case BuiltinFunction builtin when !inputs:
                return builtin.MultiOutput is null ? 1 : -1;

            case JgsBuiltinForwarder:
                return 1; // what R2025b answers for both, though the forwarder takes and answers any number

            case BuiltinFunction builtin:
                if (JgsBuiltinCatalog.Find(builtin.Name) is { } info)
                {
                    // The catalog spells an open-ended tail by ending the last parameter's name in an
                    // ellipsis — deal(value, more...) — which is the same thing varargin says.
                    var declared = new string[info.Parameters.Count];
                    for (int i = 0; i < declared.Length; i++)
                    {
                        declared[i] = info.Parameters[i].Name.EndsWith("...", StringComparison.Ordinal)
                            ? "varargin"
                            : info.Parameters[i].Name;
                    }

                    return Counted(declared, true);
                }

                return -1;

            default:
                throw new JgsRuntimeException(line, col, "MATLAB:narginout:notValidMfile",
                    "Not a valid MATLAB file.");
        }

        static double Counted(IReadOnlyList<string> declared, bool inputs)
        {
            string open = inputs ? "varargin" : "varargout";
            return declared.Count > 0 && declared[^1] == open
                ? -declared.Count
                : declared.Count;
        }
    }
}
