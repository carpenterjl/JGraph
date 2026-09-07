using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M131: <c>pdepe</c> and <c>pdeval</c> — the one-dimensional parabolic-elliptic solver and its
/// interpolant — beside the legacy expression helpers that share the <c>funfun</c> folder with them:
/// <c>symvar</c>, <c>vectorize</c>, <c>inline</c>, <c>inlineeval</c> and <c>fcnchk</c>.
/// </summary>
/// <remarks>
/// <para>
/// The two halves of the milestone look unrelated and are not. <c>fcnchk</c> is what every legacy
/// funfun called on its first argument so that a string could stand for a function, <c>inline</c> is
/// what it built when the string was an expression, and <c>symvar</c> is how <c>inline</c> decided
/// what the arguments were. They are documented as superseded by anonymous functions, so an
/// <c>inline</c> here <em>is</em> an anonymous function — one that remembers the formula it was
/// written as, because <c>formula</c>, <c>argnames</c> and <c>char</c> are answers about the text and
/// not about the parsed expression.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the partial-differential pair and the legacy expression helpers.</summary>
    internal static void RegisterPdeBuiltins(JgsEnvironment env, JGraphScriptGlobals host)
    {
        env.DeclareFunction("pdepe", JgsValue.Function(new BuiltinFunction("pdepe",
            (args, line, col) => SolvePde(env, host, args, 1, line, col)[0])
        {
            MultiOutput = (args, wanted, line, col) => SolvePde(env, host, args, wanted, line, col),
        }));

        env.DeclareFunction("pdeval", JgsValue.Function(new BuiltinFunction("pdeval",
            (args, line, col) => EvaluatePde(args, 1, line, col)[0])
        {
            MultiOutput = (args, wanted, line, col) => EvaluatePde(args, wanted, line, col),
        }));

        env.DeclareFunction("symvar", JgsValue.Function(new BuiltinFunction("symvar", (args, line, col) =>
        {
            Arity("symvar", args, 1, line, col);
            if (args[0].Type == JgsType.Function && args[0].AsCallable is InlineFunction existing)
            {
                return CellColumnOf(existing.ArgumentNames);
            }

            return CellColumnOf(SymbolicVariables(TextArgument("symvar", args[0], line, col), line, col));
        })));

        env.DeclareFunction("vectorize", JgsValue.Function(new BuiltinFunction("vectorize", (args, line, col) =>
        {
            Arity("vectorize", args, 1, line, col);
            if (args[0].Type == JgsType.Function && args[0].AsCallable is InlineFunction inlined)
            {
                return MakeInline(env, Vectorized(inlined.Formula), inlined.ArgumentNames, line, col);
            }

            string vectorized = Vectorized(TextArgument("vectorize", args[0], line, col));

            // MATLAB answers [] for an empty formula rather than an empty char row, which is what
            // makes numel(vectorize('')) zero and its class double.
            return vectorized.Length == 0 ? JgsMatrix.FromColumnMajorDims([], [0, 0]) : JgsValue.Str(vectorized);
        })));

        env.DeclareFunction("inline", JgsValue.Function(new BuiltinFunction("inline", (args, line, col) =>
        {
            if (args.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:Inline:Inline:onlyStringAllowed",
                    "inline: the expression must be a character vector.");
            }

            string expression = TextArgument("inline", args[0], line, col).Trim(' ', '\0');
            string[] names;
            if (args.Count == 1)
            {
                names = SymbolicVariables(expression, line, col);
                if (names.Length == 0)
                {
                    names = ["x"];
                }
            }
            else if (!IsTextScalar(args[1]))
            {
                // inline(EXPR, N): the arguments are x and N placeholders named P1 … PN, which is how
                // the legacy funfuns passed extra parameters through to a user expression.
                int extra = (int)Num("inline", args, 1, line, col);
                names = new string[extra + 1];
                names[0] = "x";
                for (int k = 1; k <= extra; k++)
                {
                    names[k] = $"P{k}";
                }
            }
            else
            {
                names = new string[args.Count - 1];
                for (int k = 1; k < args.Count; k++)
                {
                    names[k - 1] = TextArgument("inline", args[k], line, col);
                }
            }

            return MakeInline(env, expression, names, line, col);
        })));

        env.DeclareFunction("formula", JgsValue.Function(new BuiltinFunction("formula", (args, line, col) =>
        {
            Arity("formula", args, 1, line, col);
            return JgsValue.Str(FormulaOf("formula", args[0], line, col));
        })));

        env.DeclareFunction("argnames", JgsValue.Function(new BuiltinFunction("argnames", (args, line, col) =>
        {
            Arity("argnames", args, 1, line, col);
            return CellColumnOf(ArgumentNamesOf("argnames", args[0], line, col));
        })));

        env.DeclareFunction("inlineeval", JgsValue.Function(new BuiltinFunction("inlineeval", (args, line, col) =>
        {
            Arity("inlineeval", args, 3, line, col);
            if (args[0].Type != JgsType.Cell)
            {
                throw new JgsRuntimeException(line, col,
                    "inlineeval: the first argument must be a cell array of the inputs.");
            }

            string assignments = TextArgument("inlineeval", args[1], line, col);
            string body = TextArgument("inlineeval", args[2], line, col);
            return MakeInline(env, body, ParameterNamesOf(assignments, line, col), line, col)
                .AsCallable.Call(args[0].AsCell, line, col);
        })));

        env.DeclareFunction("fcnchk", JgsValue.Function(new BuiltinFunction("fcnchk", (args, line, col) =>
        {
            if (args.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:fcnchk:invalidFunctionSpecifier",
                    "fcnchk: FUN must be a function handle, a function name, or an expression.");
            }

            int given = args.Count;
            bool vectorizing = given > 1 && IsTextScalar(args[^1]) && TextOf(args[^1]) == "vectorized";
            if (vectorizing)
            {
                given--;
            }

            if (args[0].Type == JgsType.Function)
            {
                return args[0];
            }

            if (!IsTextScalar(args[0]))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:fcnchk:invalidFunctionSpecifier",
                    "fcnchk: FUN must be a function handle, a function name, or an expression.");
            }

            string text = TextOf(args[0]).Trim();
            if (text.Length == 0)
            {
                return MakeInline(env, "[]", ["x"], line, col);
            }

            if (!vectorizing && IsIdentifier(text) && env.TryGet(text, out JgsValue named)
                && named.Type == JgsType.Function)
            {
                return named;
            }

            var arguments = new List<JgsValue> { JgsValue.Str(vectorizing ? Vectorized(text) : text) };
            for (int k = 1; k < given; k++)
            {
                arguments.Add(args[k]);
            }

            JgsValue built = env.TryGet("inline", out JgsValue maker) && maker.Type == JgsType.Function
                ? maker.AsCallable.Call(arguments, line, col)
                : throw new JgsRuntimeException(line, col, "fcnchk: inline is not available.");
            if (!vectorizing || built.AsCallable is not InlineFunction shaped)
            {
                return built;
            }

            // 'vectorized' promises an answer the size of the input even when the formula does not
            // mention it, which is what a funfun needs before it hands the answer to a plotter.
            string[] names = shaped.ArgumentNames;
            return MakeInline(env, $"{shaped.Formula}.*ones(size({names[0]}))", names, line, col);
        })));
    }

    /// <summary>
    /// <c>sol = pdepe(m, pdefun, icfun, bcfun, xmesh, tspan, options)</c> and its five-output event
    /// form.
    /// </summary>
    private static JgsValue[] SolvePde(JgsEnvironment env, JGraphScriptGlobals host,
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("pdepe", args, 6, 7, line, col);
        int m = (int)Num("pdepe", args, 0, line, col);
        IJgsCallable pdefun = OdeFunctionOf(env, "pdepe", args[1], line, col);
        IJgsCallable icfun = OdeFunctionOf(env, "pdepe", args[2], line, col);
        IJgsCallable bcfun = OdeFunctionOf(env, "pdepe", args[3], line, col);
        double[] xmesh = ToDoubles("pdepe", args[4], line, col);
        double[] tspan = ToDoubles("pdepe", args[5], line, col);
        JgsValue? options = args.Count > 6 && args[6].Type == JgsType.Struct ? args[6] : null;
        if (args.Count > 6 && options is null && !(args[6].Type == JgsType.Array && args[6].ArrayLength == 0))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:odearguments:OptionsNotStruct",
                "pdepe: the seventh argument must be an options structure created with odeset, or [].");
        }

        PdeCoefficients coefficients = (x, t, u, dudx) =>
        {
            JgsValue state = JgsMatrix.FromColumnMajorDims((double[])u.Clone(), [u.Length, 1]);
            JgsValue slope = JgsMatrix.FromColumnMajorDims((double[])dudx.Clone(), [dudx.Length, 1]);
            JgsValue[] answers = CallForOutputs(pdefun,
                [JgsValue.Number(x), JgsValue.Number(t), state, slope], 3, line, col);
            return new PdeCoefficientReading(
                ToDoubles("pdepe", answers[0], line, col),
                ToDoubles("pdepe", answers[1], line, col),
                ToDoubles("pdepe", answers[2], line, col));
        };

        PdeInitialCondition initial = x =>
            ToDoubles("pdepe", icfun.Call([JgsValue.Number(x)], line, col), line, col);

        PdeBoundary boundary = (xl, ul, xr, ur, t) =>
        {
            JgsValue left = JgsMatrix.FromColumnMajorDims((double[])ul.Clone(), [ul.Length, 1]);
            JgsValue right = JgsMatrix.FromColumnMajorDims((double[])ur.Clone(), [ur.Length, 1]);
            JgsValue[] answers = CallForOutputs(bcfun,
                [JgsValue.Number(xl), left, JgsValue.Number(xr), right, JgsValue.Number(t)], 4, line, col);
            return new PdeBoundaryReading(
                ToDoubles("pdepe", answers[0], line, col),
                ToDoubles("pdepe", answers[1], line, col),
                ToDoubles("pdepe", answers[2], line, col),
                ToDoubles("pdepe", answers[3], line, col));
        };

        // pdepe reads only the options the method of lines can honour; everything about the mass
        // matrix, the Jacobian and the vectorization is decided by the discretisation instead.
        double[]? absolute = options is not null && TryReadField(options, "AbsTol", out JgsValue atol)
            && !IsUnsetOption(atol)
            ? ToDoubles("odeset", atol, line, col)
            : null;
        var settings = new OdeOptions
        {
            RelativeTolerance = OdeNumber(options, "RelTol") ?? 1e-3,
            AbsoluteTolerance = absolute,
            NormControl = options is not null && TryReadField(options, "NormControl", out JgsValue norm)
                && !IsUnsetOption(norm) && Truth(norm),
            MaxStep = OdeNumber(options, "MaxStep"),
            InitialStep = OdeNumber(options, "InitialStep"),
            Warn = message => Warn(env, host, message, line, col),
        };

        PdeEventFunction? events = null;
        if (options is not null && TryReadField(options, "Events", out JgsValue watcher)
            && !IsUnsetOption(watcher))
        {
            IJgsCallable handle = OdeFunctionOf(env, "pdepe", watcher, line, col);
            JgsValue mesh = JgsMatrix.FromColumnMajorDims((double[])xmesh.Clone(), [1, xmesh.Length]);
            events = (t, umesh) =>
            {
                JgsValue state = JgsMatrix.FromColumnMajorDims((double[])umesh.Clone(), [umesh.Length, 1]);
                JgsValue[] outputs = CallForOutputs(handle,
                    [JgsValue.Number(m), JgsValue.Number(t), mesh, state], 3, line, col);
                double[] values = ToDoubles("Events", outputs[0], line, col);
                double[] terminal = outputs.Length > 1 ? ToDoubles("Events", outputs[1], line, col) : [];
                double[] direction = outputs.Length > 2 ? ToDoubles("Events", outputs[2], line, col) : [];
                var stops = new bool[values.Length];
                var directions = new int[values.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    stops[i] = terminal.Length > i ? terminal[i] != 0 : terminal.Length == 1 && terminal[0] != 0;
                    directions[i] = direction.Length > i ? Math.Sign(direction[i])
                        : direction.Length == 1 ? Math.Sign(direction[0]) : 0;
                }

                return new OdeEventReading(values, stops, directions);
            };
        }

        PdeResult answer;
        try
        {
            answer = Pdepe.Solve(m, coefficients, initial, boundary, xmesh, tspan, settings, events);
        }
        catch (OdeArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, ex.Identifier, ex.Message);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            throw new JgsRuntimeException(line, col, $"pdepe: {ex.Message}");
        }

        JgsValue surface = Surface(answer.Times.Length, answer.Nx, answer.Npde, answer.States);
        if (wanted <= 1 || events is null)
        {
            return [surface];
        }

        int found = answer.EventTimes.Length;
        return
        [
            surface,
            JgsMatrix.Build(answer.Times.Length, 1, (r, _) => answer.Times[r]),
            Surface(found, answer.Nx, answer.Npde, answer.EventStates),
            JgsMatrix.Build(found, 1, (r, _) => answer.EventTimes[r]),
            JgsMatrix.Build(found, 1, (r, _) => answer.EventIndices[r] + 1),
        ];
    }

    /// <summary>The nt-by-nx-by-npde array <c>pdepe</c> answers, out of the flat mesh states.</summary>
    private static JgsValue Surface(int nt, int nx, int npde, double[][] states)
    {
        var flat = new double[nt * nx * npde];
        for (int j = 0; j < nt; j++)
        {
            for (int k = 0; k < nx; k++)
            {
                for (int i = 0; i < npde; i++)
                {
                    flat[j + (nt * k) + (nt * nx * i)] = states[j][(k * npde) + i];
                }
            }
        }

        return JgsMatrix.FromColumnMajorDims(flat, npde == 1 ? [nt, nx] : [nt, nx, npde]);
    }

    /// <summary><c>[uout, duoutdx] = pdeval(m, xmesh, ui, xout)</c>.</summary>
    private static JgsValue[] EvaluatePde(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        Arity("pdeval", args, 4, line, col);
        int m = (int)Num("pdeval", args, 0, line, col);
        double[] xmesh = ToDoubles("pdeval", args[1], line, col);
        double[] ui = ToDoubles("pdeval", args[2], line, col);
        double[] xout = ToDoubles("pdeval", args[3], line, col);
        if (ui.Length != xmesh.Length)
        {
            throw new JgsRuntimeException(line, col,
                $"pdeval: UI must have one value per mesh point ({xmesh.Length}), but has {ui.Length}.");
        }

        var uout = new double[xout.Length];
        var duoutdx = new double[xout.Length];
        try
        {
            Pdepe.Evaluate(m, xmesh, ui, xout, uout, duoutdx);
        }
        catch (OdeArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, ex.Identifier, ex.Message);
        }

        // The answers take the shape of the points asked for, which is how a row of query points
        // comes back as a row.
        int[] shape = args[3].Type is JgsType.Number or JgsType.Bool
            ? [1, 1]
            : [args[3].Rows, args[3].Cols];
        JgsValue values = JgsMatrix.FromColumnMajorDims(uout, shape);
        return wanted >= 2 ? [values, JgsMatrix.FromColumnMajorDims(duoutdx, shape)] : [values];
    }

    /// <summary>An <c>inline</c> value: an anonymous function that remembers the text it was written as.</summary>
    private static JgsValue MakeInline(JgsEnvironment env, string formula, string[] names, int line, int col)
    {
        if (!env.TryGet("str2func", out JgsValue maker) || maker.Type != JgsType.Function)
        {
            throw new JgsRuntimeException(line, col, "inline: str2func is not available.");
        }

        JgsValue handle = maker.AsCallable.Call(
            [JgsValue.Str($"@({string.Join(", ", names)}) {formula}")], line, col);
        return JgsValue.Function(new InlineFunction(handle.AsCallable, formula, names));
    }

    /// <summary>The formula of an inline, or the body an anonymous handle was written as.</summary>
    private static string FormulaOf(string name, JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.Function && value.AsCallable is InlineFunction inlined)
        {
            return inlined.Formula;
        }

        if (value.Type == JgsType.Function && value.AsCallable is AnonymousFunction anonymous)
        {
            return AstPrinter.Print(anonymous.Declaration.Body);
        }

        throw new JgsRuntimeException(line, col, $"{name} expects an inline function or a function handle.");
    }

    /// <summary>The argument names of an inline, or the parameters an anonymous handle declares.</summary>
    private static string[] ArgumentNamesOf(string name, JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.Function && value.AsCallable is InlineFunction inlined)
        {
            return inlined.ArgumentNames;
        }

        if (value.Type == JgsType.Function && value.AsCallable is AnonymousFunction anonymous)
        {
            return [.. anonymous.Declaration.Parameters];
        }

        throw new JgsRuntimeException(line, col, $"{name} expects an inline function or a function handle.");
    }

    /// <summary>The names <c>inlineeval</c>'s assignment string binds, in the order it binds them.</summary>
    private static string[] ParameterNamesOf(string assignments, int line, int col)
    {
        var names = new List<string>();
        foreach (string piece in assignments.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = piece.IndexOf('=');
            string head = equals < 0 ? string.Empty : piece[..equals].Trim();
            if (!IsIdentifier(head))
            {
                throw new JgsRuntimeException(line, col,
                    "inlineeval: the input expression must be a list of assignments from INLINE_INPUTS_.");
            }

            names.Add(head);
        }

        return [.. names];
    }

    /// <summary>A cell column of names, which is the shape <c>symvar</c> and <c>argnames</c> answer.</summary>
    private static JgsValue CellColumnOf(IReadOnlyList<string> names)
    {
        JgsValue[] cells = [.. names.Select(JgsValue.Str)];
        JgsValue column = JgsValue.Cell(cells);
        column.Reshape(cells.Length, cells.Length == 0 ? 0 : 1);
        return column;
    }

    /// <summary>Text out of a char row or a string scalar, refusing anything else the way MATLAB does.</summary>
    private static string TextArgument(string name, JgsValue value, int line, int col)
    {
        if (IsTextScalar(value))
        {
            return TextOf(value);
        }

        if (value.Type == JgsType.Array && value.ArrayLength == 0)
        {
            return string.Empty;
        }

        throw new JgsRuntimeException(line, col, $"{name} expects a character vector.");
    }

    /// <summary>Whether the text is a bare MATLAB identifier — a letter followed by word characters.</summary>
    private static bool IsIdentifier(string text)
    {
        if (text.Length == 0 || !char.IsLetter(text[0]))
        {
            return false;
        }

        foreach (char c in text)
        {
            if (!char.IsLetter(c) && !char.IsAsciiDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// <c>vectorize</c>: a dot before every <c>*</c>, <c>/</c> and <c>^</c>, with any dot already
    /// there stripped first so that an expression twice vectorized is vectorized once.
    /// </summary>
    private static string Vectorized(string source) =>
        source.Length == 0
            ? string.Empty
            : source.Replace(".*", "*").Replace("./", "/").Replace(".^", "^")
                .Replace("*", ".*").Replace("/", "./").Replace("^", ".^");

    /// <summary>
    /// <c>symvar</c>: the identifiers in an expression that are neither constants nor called as
    /// functions, sorted and answered once each.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rules are the ones MATLAB's own <c>symvar</c> applies, and every one of them is there to
    /// answer a piece of MATLAB syntax rather than to be principled. A run of word characters counts
    /// only when it starts with a letter, so <c>_a</c> is not a name. A run followed by <c>(</c> is a
    /// function call, and blanks before the parenthesis are removed first so that <c>foo (x)</c> is
    /// one too. A run preceded by a dot is a field name or the tail of <c>1.e10</c>. Text inside
    /// quotes is skipped, and a quote that follows an identifier, a closing bracket or another quote
    /// is a transpose rather than the start of a string. What is left is filtered against the
    /// constants <c>i j pi inf Inf nan NaN eps</c> — case-sensitively, so <c>I</c> and <c>J</c>
    /// survive.
    /// </para>
    /// </remarks>
    private static string[] SymbolicVariables(string source, int line, int col)
    {
        if (source.Length == 0)
        {
            return [];
        }

        char[] text = source.ToCharArray();
        var dropped = new bool[text.Length];
        for (int i = 0; i < text.Length;)
        {
            if (text[i] != ' ')
            {
                i++;
                continue;
            }

            int run = i;
            while (run < text.Length && text[run] == ' ')
            {
                run++;
            }

            if (run < text.Length && text[run] == '(')
            {
                for (int k = i; k < run; k++)
                {
                    dropped[k] = true;
                }
            }

            i = run;
        }

        var kept = new List<char>(text.Length);
        foreach ((char c, bool skip) in text.Zip(dropped))
        {
            if (!skip)
            {
                kept.Add(c);
            }
        }

        char[] s = [.. kept];
        bool[] quoted = QuoteMask(s, line, col);
        var names = new SortedSet<string>(StringComparer.Ordinal);
        var constants = new HashSet<string>(StringComparer.Ordinal)
        {
            "i", "j", "pi", "inf", "Inf", "nan", "NaN", "eps",
        };

        for (int i = 0; i < s.Length;)
        {
            if (quoted[i] || !IsWord(s[i]))
            {
                i++;
                continue;
            }

            int end = i;
            while (end < s.Length && IsWord(s[end]) && !quoted[end])
            {
                end++;
            }

            string candidate = new(s, i, end - i);
            bool call = end < s.Length && s[end] == '(';
            bool field = i > 0 && s[i - 1] == '.';
            if (char.IsLetter(s[i]) && !call && !field && !constants.Contains(candidate))
            {
                names.Add(candidate);
            }

            i = end;
        }

        return [.. names];
    }

    private static bool IsWord(char c) => char.IsLetter(c) || char.IsAsciiDigit(c) || c == '_';

    /// <summary>
    /// Which characters sit inside a quoted string. The hard part is the apostrophe, which is a
    /// transpose when it follows a name, a closing bracket, a dot or another transpose, and the start
    /// of a string otherwise — and which of the two it is depends on whether a string is already open.
    /// </summary>
    private static bool[] QuoteMask(char[] s, int line, int col)
    {
        var mask = new bool[s.Length];
        var quotes = new List<int>();
        bool inside = false;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != '\'')
            {
                continue;
            }

            bool afterValue = i > 0 && (IsWord(s[i - 1]) || s[i - 1] is ')' or ']' or '}' or '.' or '\'');
            if (!inside && afterValue)
            {
                continue;
            }

            quotes.Add(i);
            inside = !inside;
        }

        if (quotes.Count == 0)
        {
            return mask;
        }

        if (quotes.Count % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:symvar:SQuotesNotPaired",
                "Single quotes in the expression are not paired.");
        }

        for (int k = 0; k < quotes.Count; k += 2)
        {
            for (int i = quotes[k]; i <= quotes[k + 1]; i++)
            {
                mask[i] = true;
            }
        }

        return mask;
    }
}

/// <summary>
/// An <c>inline</c> function: the anonymous function the formula compiles to, plus the formula and
/// the argument names as they were written, which <c>formula</c>, <c>argnames</c> and <c>char</c>
/// answer.
/// </summary>
/// <remarks>
/// The text has to be kept because it is not recoverable from the parsed expression: printing the
/// tree back gives <c>(x ^ 2) + y</c> where the caller wrote <c>x^2+y</c>, and <c>formula</c> is
/// documented to answer what the caller wrote.
/// </remarks>
internal sealed class InlineFunction(IJgsCallable body, string formula, string[] argumentNames)
    : IJgsCallable, IJgsMultiCallable
{
    /// <inheritdoc />
    public string Name => "@inline";

    /// <summary>The expression the inline was written as.</summary>
    public string Formula { get; } = formula;

    /// <summary>The formal parameters, in order.</summary>
    public string[] ArgumentNames { get; } = argumentNames;

    /// <inheritdoc />
    public JgsValue Call(IReadOnlyList<JgsValue> arguments, int line, int column) =>
        body.Call(arguments, line, column);

    /// <inheritdoc />
    public JgsValue[] CallMultiple(IReadOnlyList<JgsValue> arguments, int wanted, int line, int column) =>
        body is IJgsMultiCallable several
            ? several.CallMultiple(arguments, wanted, line, column)
            : [body.Call(arguments, line, column)];
}
