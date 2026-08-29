using System.Globalization;
using JGraph.Numerics.Optimization;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The optimfun family (M99): the four solvers base MATLAB ships and the options structure they read
/// their settings from.
/// </summary>
/// <remarks>
/// <para>
/// These are the first names JGraph implements from what MATLAB documents as <em>functions</em>
/// rather than builtins: library routines MATLAB itself writes in MATLAB and puts on the default
/// path. They are implemented here in C# for the reason every other name is — the catalog, the
/// coverage verifiers and the form probe all read the registration table, and a shipped
/// <c>.m</c> tier would be invisible to all three. The algorithms are the published ones the
/// MathWorks sources themselves cite: Nelder and Mead by way of Lagarias et al., and Brent's
/// <c>fmin</c> and <c>zeroin</c> by way of Forsythe, Malcolm and Moler, with Lawson and Hanson for
/// the non-negative least squares.
/// </para>
/// <para>
/// The engines are in <c>JGraph.Numerics.Optimization</c>; what lives here is the surface — argument
/// forms, the options structure, the display, and the output and plot callbacks. That split is what
/// lets the engines be tested against the algorithms and this file against MATLAB's syntax.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// The eight settings base MATLAB's <c>optimset</c> holds, in the order <c>fieldnames</c>
    /// reports them.
    /// </summary>
    /// <remarks>
    /// MATLAB's own <c>optimset</c> grows a much larger structure when the Optimization Toolbox is
    /// licensed, and shrinks to exactly these eight when it is not. JGraph has no toolbox to detect,
    /// so it is always the eight.
    /// </remarks>
    private static readonly string[] OptimsetFields =
    [
        "Display", "MaxFunEvals", "MaxIter", "TolFun", "TolX", "FunValCheck", "OutputFcn", "PlotFcns",
    ];

    /// <summary>How loudly a solver reports: off, only on failure, at the end, or every iteration.</summary>
    private enum OptimDisplay
    {
        /// <summary>Say nothing.</summary>
        Off = 0,

        /// <summary>Say something only when the solve did not converge.</summary>
        Notify = 1,

        /// <summary>Say how it ended.</summary>
        Final = 2,

        /// <summary>Print a line per iteration, then how it ended.</summary>
        Iterate = 3,
    }

    /// <summary>Registers the optimfun builtins into <paramref name="env"/>.</summary>
    /// <remarks>
    /// <paramref name="env"/> is captured rather than only written to, because a solver may be handed
    /// its objective as a name rather than a handle — MATLAB's <c>fcnchk</c> conversion — and
    /// resolving a name needs the scope the call was made in.
    /// </remarks>
    internal static void RegisterOptimizeBuiltins(JgsEnvironment env, JGraphScriptGlobals host)
    {
        void DefineBoth(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> both) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, (args, line, col) => both(args, 1, line, col)[0])
                { MultiOutput = both }));

        DefineBoth("fminsearch", (args, wanted, line, col) =>
            Fminsearch(env, host, args, wanted, line, col));
        DefineBoth("fminbnd", (args, wanted, line, col) =>
            Fminbnd(env, host, args, wanted, line, col));
        DefineBoth("fzero", (args, wanted, line, col) =>
            Fzero(env, host, args, wanted, line, col));
        DefineBoth("lsqnonneg", (args, wanted, line, col) =>
            Lsqnonneg(host, args, wanted, line, col));

        // optimset answers a structure when anything wants one and prints the settings when nothing
        // does, so it needs both flags: AutoCallsBare makes the bare word a call rather than a
        // handle, and KnowsWhenDiscarded is how it learns that nobody asked for the answer.
        env.Declare("optimset", JgsValue.Function(new BuiltinFunction(
            "optimset", (args, line, col) => Optimset(env, host, args, wanted: 1, line, col)[0])
        {
            AutoCallsBare = true,
            KnowsWhenDiscarded = true,
            MultiOutput = (args, wanted, line, col) => Optimset(env, host, args, wanted, line, col),
        }));

        env.Declare("optimget", JgsValue.Function(new BuiltinFunction("optimget", Optimget)));

        RegisterOptimPlotBuiltins(env);
    }

    // --- The options structure ----------------------------------------------------------------------

    /// <summary>
    /// <c>options = optimset(...)</c>: the structure the solvers take their settings from.
    /// </summary>
    /// <remarks>
    /// An unset field is <c>[]</c> rather than absent, which is what lets a solver tell "the caller
    /// did not say" from "the caller said nothing" and fall back to its own default. That is also why
    /// <c>optimset(oldopts, newopts)</c> copies only the non-empty fields across.
    /// </remarks>
    private static JgsValue[] Optimset(
        JgsEnvironment env,
        JGraphScriptGlobals host,
        IReadOnlyList<JgsValue> args,
        int wanted,
        int line,
        int col)
    {
        // optimset with nothing in and nothing out is a request to see the settings, not to build
        // one; every other shape answers a structure.
        if (args.Count == 0 && wanted == 0)
        {
            host.WriteOut(
                "                Display: [ off | iter | notify | final ]\n"
                + "            MaxFunEvals: [ positive scalar ]\n"
                + "                MaxIter: [ positive scalar ]\n"
                + "                 TolFun: [ positive scalar ]\n"
                + "                   TolX: [ positive scalar ]\n"
                + "            FunValCheck: [ on | {off} ]\n"
                + "              OutputFcn: [ function | {[]} ]\n"
                + "               PlotFcns: [ function | {[]} ]\n"
                + "\n");
            return [JgsValue.Null];
        }

        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        foreach (string field in OptimsetFields)
        {
            fields[field] = JgsValue.Array([]);
        }

        // optimset('fminbnd') and optimset(@fminbnd) both answer that solver's defaults. A single
        // name is always read this way, which is why optimset('TolX') is not a setting missing its
        // value but a solver that does not exist — MATLAB reads it the same way, and says so.
        if (args.Count == 1 && (args[0].Type == JgsType.String || args[0].Type == JgsType.Function))
        {
            string solver = args[0].Type == JgsType.String
                ? args[0].AsString.ToLowerInvariant()
                : args[0].AsCallable.Name;

            if (!env.TryGet(solver, out JgsValue named) || named.Type != JgsType.Function)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:optimset:FcnNotFoundOnPath",
                    $"No default options available: the function '{solver}' does not exist on the path.");
            }

            return [SolverDefaults(solver, line, col)];
        }

        int from = 0;

        // A leading structure is the one being altered, and a second one merges into it. Both are
        // copied field by field so that a structure from another build, holding fields this one does
        // not, contributes what it can rather than being rejected.
        while (from < args.Count && args[from].Type != JgsType.String)
        {
            if (args[from].Type == JgsType.Array && args[from].ArrayLength == 0)
            {
                from++; // [] is a valid options argument and contributes nothing
                continue;
            }

            if (args[from].Type != JgsType.Struct)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:optimset:NoParamNameOrStruct",
                    $"Expected argument {from + 1} to be a character vector or scalar string "
                    + "parameter name or an options structure created with OPTIMSET.");
            }

            foreach (string field in OptimsetFields)
            {
                if (TryReadField(args[from], field, out JgsValue value) && !IsUnsetOption(value))
                {
                    fields[field] = NormalizeOptionValue(value);
                }
            }

            from++;
        }

        if ((args.Count - from) % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:optimset:ArgNameValueMismatch",
                "Arguments must occur in name-value pairs.");
        }

        for (int i = from; i < args.Count; i += 2)
        {
            if (args[i].Type != JgsType.String)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:optimset:ParamNotString",
                    $"Expected argument {i + 1} to be a character vector or scalar string parameter name.");
            }

            string field = MatchOptionName("optimset", args[i].AsString, line, col);
            fields[field] = NormalizeOptionValue(args[i + 1]);
        }

        return [JgsValue.Struct(fields)];
    }

    /// <summary><c>value = optimget(options, name, default)</c>: one setting, or what to use instead.</summary>
    private static JgsValue Optimget(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:optimget:NotEnoughInputs",
                "Not enough input arguments.");
        }

        JgsValue fallback = args.Count > 2 ? args[2] : JgsValue.Array([]);
        if (args[0].Type == JgsType.Array && args[0].ArrayLength == 0)
        {
            return fallback;
        }

        if (args[0].Type != JgsType.Struct)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:optimget:Arg1NotStruct",
                "First argument must be an options structure created with OPTIMSET.");
        }

        if (args[1].Type != JgsType.String)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:optimget:InvalidPropName",
                $"Unrecognized option name '{args[1].Display()}'.  See OPTIMSET for possibilities.");
        }

        string field = MatchOptionName("optimget", args[1].AsString, line, col);
        return TryReadField(args[0], field, out JgsValue value) && !IsUnsetOption(value) ? value : fallback;
    }

    /// <summary>
    /// The full option name a caller's abbreviation stands for. Matching ignores case and accepts any
    /// unique leading portion, with an exact match breaking a tie between a name and a longer one it
    /// is a prefix of.
    /// </summary>
    /// <remarks>
    /// The two callers spell their failures differently, and MATLAB's identifiers are copied rather
    /// than regularised: <c>optimset</c> raises <c>InvalidParamNameWithLink</c> where <c>optimget</c>
    /// raises <c>InvalidPropName</c>. The "WithLink" in the first names a hyperlink to the options
    /// table that MATLAB's message carries and JGraph has no page for, so the message differs while
    /// the identifier — the part a <c>catch</c> branches on — does not.
    /// </remarks>
    private static string MatchOptionName(string caller, string typed, int line, int col)
    {
        string wanted = typed.Trim();
        var matches = new List<string>();
        foreach (string field in OptimsetFields)
        {
            if (field.StartsWith(wanted, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(field);
            }
        }

        if (matches.Count == 1)
        {
            return matches[0];
        }

        if (matches.Count == 0)
        {
            throw caller == "optimset"
                ? new JgsRuntimeException(line, col, "MATLAB:optimset:InvalidParamNameWithLink",
                    $"Unrecognized parameter name '{wanted}'.  The settings are "
                    + string.Join(", ", OptimsetFields) + ".")
                : new JgsRuntimeException(line, col, $"MATLAB:{caller}:InvalidPropName",
                    $"Unrecognized option name '{wanted}'.  See OPTIMSET for possibilities.");
        }

        foreach (string field in matches)
        {
            if (string.Equals(field, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return field;
            }
        }

        throw caller == "optimset"
            ? new JgsRuntimeException(line, col, "MATLAB:optimset:AmbiguousParamName",
                $"Ambiguous parameter name '{wanted}' ({string.Join(", ", matches)}).")
            : new JgsRuntimeException(line, col, $"MATLAB:{caller}:AmbiguousPropName",
                $"Ambiguous option name '{wanted}' ({string.Join(", ", matches)}.)");
    }

    /// <summary>
    /// A stored option value. Text is lowercased and trimmed on the way in, so
    /// <c>optimset('Display','Iter')</c> and <c>optimset('Display','iter')</c> hold the same thing
    /// and a solver comparing against <c>'iter'</c> needs no second reading.
    /// </summary>
    private static JgsValue NormalizeOptionValue(JgsValue value) =>
        value.Type == JgsType.String ? JgsValue.Str(value.AsString.Trim().ToLowerInvariant()) : value;

    /// <summary>Whether a field holds nothing, which is how an options structure spells "unset".</summary>
    private static bool IsUnsetOption(JgsValue value) =>
        (value.Type == JgsType.Array && value.ArrayLength == 0)
        || (value.Type == JgsType.String && value.AsString.Length == 0);

    /// <summary>Reads one field of a scalar struct, if it has it.</summary>
    private static bool TryReadField(JgsValue structure, string field, out JgsValue value)
    {
        value = JgsValue.Array([]);
        if (structure.Type != JgsType.Struct)
        {
            return false;
        }

        foreach (KeyValuePair<string, JgsValue> entry in structure.AsStruct)
        {
            if (string.Equals(entry.Key, field, StringComparison.OrdinalIgnoreCase))
            {
                value = entry.Value;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The options structure a solver answers when asked for its defaults, which is what
    /// <c>optimset('fminsearch')</c> is really asking for.
    /// </summary>
    /// <remarks>
    /// Two of these defaults are stored as <em>text</em> rather than numbers, and deliberately:
    /// <c>fminsearch</c>'s iteration and evaluation caps are 200 per free parameter, which is not a
    /// number until there is a starting point to count. MATLAB stores the recipe
    /// <c>'200*numberOfVariables'</c> and resolves it at the call, and a script that reads the field
    /// back gets the text.
    /// </remarks>
    private static JgsValue SolverDefaults(string solver, int line, int col)
    {
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        foreach (string field in OptimsetFields)
        {
            fields[field] = JgsValue.Array([]);
        }

        fields["Display"] = JgsValue.Str("notify");
        fields["FunValCheck"] = JgsValue.Str("off");

        switch (solver.ToLowerInvariant())
        {
            case "fminsearch":
                fields["MaxIter"] = JgsValue.Str("200*numberOfVariables");
                fields["MaxFunEvals"] = JgsValue.Str("200*numberOfVariables");
                fields["TolX"] = JgsValue.Number(1e-4);
                fields["TolFun"] = JgsValue.Number(1e-4);
                break;

            case "fminbnd":
                fields["MaxFunEvals"] = JgsValue.Number(500);
                fields["MaxIter"] = JgsValue.Number(500);
                fields["TolX"] = JgsValue.Number(1e-4);
                break;

            case "fzero":
                fields["TolX"] = JgsValue.Number(Math.Pow(2, -52));
                break;

            case "lsqnonneg":
                fields["TolX"] = JgsValue.Str("10*eps*norm(C,1)*length(C)");
                break;

            default:
                throw new JgsRuntimeException(line, col, "MATLAB:optimset:NoDefaultsForFcn",
                    $"No default options available for the function '{solver}'.");
        }

        return JgsValue.Struct(fields);
    }

    /// <summary>
    /// Whether <paramref name="args"/> is the undocumented single-argument <c>'defaults'</c> call
    /// every optimfun solver answers, and which is how <c>optimset(solver)</c> asks a solver what it
    /// would use.
    /// </summary>
    private static bool AsksForDefaults(IReadOnlyList<JgsValue> args, int wanted) =>
        args.Count == 1
        && wanted <= 1
        && args[0].Type == JgsType.String
        && string.Equals(args[0].AsString, "defaults", StringComparison.OrdinalIgnoreCase);

    // --- Reading the settings a solver was given ----------------------------------------------------

    /// <summary>What a solver read out of the options structure it was handed.</summary>
    /// <param name="Display">How loudly to report.</param>
    /// <param name="ToleranceX">The tolerance on the answer.</param>
    /// <param name="ToleranceFunction">The tolerance on the objective.</param>
    /// <param name="MaxFunctionEvaluations">The evaluation budget.</param>
    /// <param name="MaxIterations">The iteration budget.</param>
    /// <param name="CheckValues">Whether to refuse a NaN or complex objective value.</param>
    /// <param name="OutputFunctions">The output functions, in the order they were given.</param>
    /// <param name="PlotFunctions">The plot functions, in the order they were given.</param>
    private sealed record OptimSettings(
        OptimDisplay Display,
        double ToleranceX,
        double ToleranceFunction,
        int MaxFunctionEvaluations,
        int MaxIterations,
        bool CheckValues,
        IJgsCallable[] OutputFunctions,
        IJgsCallable[] PlotFunctions)
    {
        /// <summary>Whether anything is watching, which is what makes the callback plumbing worth running.</summary>
        public bool HasCallbacks => OutputFunctions.Length > 0 || PlotFunctions.Length > 0;
    }

    /// <summary>
    /// Reads the settings for <paramref name="solver"/> out of <paramref name="options"/>, falling
    /// back to that solver's own defaults for anything unset.
    /// </summary>
    /// <param name="solver">Which solver is asking; it decides the defaults and the error ids.</param>
    /// <param name="env">The scope an output function named as text is resolved in.</param>
    /// <param name="options">The structure the caller passed, or null when none was.</param>
    /// <param name="variables">
    /// The number of free parameters, for resolving the <c>200*numberOfVariables</c> recipe.
    /// </param>
    /// <param name="line">The call site's line, for error reporting.</param>
    /// <param name="col">The call site's column, for error reporting.</param>
    private static OptimSettings ReadOptimSettings(
        string solver, JgsEnvironment env, JgsValue? options, int variables, int line, int col)
    {
        JgsValue defaults = SolverDefaults(solver, line, col);

        JgsValue Setting(string field)
        {
            if (options is { } given && TryReadField(given, field, out JgsValue value)
                && !IsUnsetOption(value))
            {
                return value;
            }

            TryReadField(defaults, field, out JgsValue fallback);
            return fallback;
        }

        if (options is { } structure && structure.Type != JgsType.Struct)
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{solver}:ArgNotStruct",
                $"Argument {(solver == "fminsearch" ? 3 : 4)} must be an options structure.");
        }

        return new OptimSettings(
            ReadDisplay(solver, Setting("Display"), line, col),
            ReadTolerance(Setting("TolX")),
            ReadTolerance(Setting("TolFun")),
            ReadBudget(solver, "MaxFunEvals", Setting("MaxFunEvals"), variables, line, col),
            ReadBudget(solver, "MaxIter", Setting("MaxIter"), variables, line, col),
            Setting("FunValCheck") is { Type: JgsType.String } check
                && string.Equals(check.AsString, "on", StringComparison.OrdinalIgnoreCase),
            ReadCallbacks(env, Setting("OutputFcn"), "OutputFcn", line, col),
            ReadCallbacks(env, Setting("PlotFcns"), "PlotFcns", line, col));
    }

    /// <summary>
    /// How loudly a solver should report. MATLAB accepts a "-detailed" suffix on three of the four
    /// levels and answers the same as the plain form here, because nothing in this build has a
    /// second, more detailed thing to say.
    /// </summary>
    /// <remarks>
    /// <c>lsqnonneg</c> alone refuses <c>'iter'</c>, warning and falling back — it has no per-iteration
    /// line to print — and refuses an unrecognised level outright where the other three quietly treat
    /// it as <c>'notify'</c>. Both are MATLAB's asymmetries rather than this build's.
    /// </remarks>
    private static OptimDisplay ReadDisplay(string solver, JgsValue value, int line, int col)
    {
        if (value.Type != JgsType.String)
        {
            return OptimDisplay.Notify;
        }

        string level = value.AsString.ToLowerInvariant();
        if (solver == "lsqnonneg")
        {
            return level switch
            {
                "notify" or "notify-detailed" => OptimDisplay.Notify,
                "none" or "off" => OptimDisplay.Off,
                "final" or "final-detailed" => OptimDisplay.Final,
                "iter" or "iter-detailed" => OptimDisplay.Iterate,
                _ => throw new JgsRuntimeException(line, col, "MATLAB:lsqnonneg:InvalidOptParamDisplay",
                    "Bad value for options parameter: 'Display'."),
            };
        }

        return level switch
        {
            "none" or "off" => OptimDisplay.Off,
            "final" or "final-detailed" => OptimDisplay.Final,

            // 'simplex' is fminsearch's undocumented fifth level, which dumps the whole simplex each
            // iteration in a global display format it also changes. It is accepted and read as
            // 'iter'; ADR 0100 records the difference.
            "iter" or "iter-detailed" or "simplex" => OptimDisplay.Iterate,
            _ => OptimDisplay.Notify,
        };
    }

    /// <summary>A tolerance, or zero when the field held something that is not one.</summary>
    private static double ReadTolerance(JgsValue value) =>
        value.Type is JgsType.Number or JgsType.Bool ? value.AsNumber : 0;

    /// <summary>
    /// An iteration or evaluation budget. The two <c>fminsearch</c> defaults arrive as the text
    /// <c>'200*numberOfVariables'</c>, which is a recipe rather than a number and is resolved here
    /// against the problem's size; any other text is an error, because it is a setting nobody can act
    /// on.
    /// </summary>
    private static int ReadBudget(
        string solver, string field, JgsValue value, int variables, int line, int col)
    {
        if (value.Type is JgsType.Number or JgsType.Bool)
        {
            double raw = value.AsNumber;
            return raw >= int.MaxValue ? int.MaxValue : (int)raw;
        }

        if (value.Type == JgsType.String)
        {
            if (string.Equals(value.AsString, "200*numberofvariables", StringComparison.OrdinalIgnoreCase))
            {
                return 200 * Math.Max(variables, 1);
            }

            throw new JgsRuntimeException(line, col, $"MATLAB:{solver}:Opt{field}NotInteger",
                $"Option '{field}' must be an integer value if not the default.");
        }

        return 0;
    }

    /// <summary>
    /// The callables one <c>OutputFcn</c> or <c>PlotFcns</c> field names: a handle, a name as text,
    /// or a cell of either.
    /// </summary>
    private static IJgsCallable[] ReadCallbacks(
        JgsEnvironment env, JgsValue value, string field, int line, int col)
    {
        if (IsUnsetOption(value))
        {
            return [];
        }

        if (value.Type == JgsType.Cell)
        {
            JgsValue[] entries = value.AsCell;
            var callables = new IJgsCallable[entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                callables[i] = OneCallback(env, entries[i], field, line, col);
            }

            return callables;
        }

        return [OneCallback(env, value, field, line, col)];
    }

    /// <summary>One callback, from a handle or from a name this scope can resolve.</summary>
    private static IJgsCallable OneCallback(
        JgsEnvironment env, JgsValue value, string field, int line, int col)
    {
        if (value.Type == JgsType.Function)
        {
            return value.AsCallable;
        }

        if (value.Type == JgsType.String
            && env.TryGet(value.AsString, out JgsValue found)
            && found.Type == JgsType.Function)
        {
            return found.AsCallable;
        }

        throw new JgsRuntimeException(line, col,
            $"{field} names a function, written @name or @(x, optimValues, state) …, "
            + "or a cell array of them.");
    }

    // --- Calling what the caller gave us ------------------------------------------------------------

    /// <summary>
    /// The objective, as a call from a flat point to a scalar. The point is reshaped to the starting
    /// point's own dimensions before every call, so that an objective written <c>@(x) x'*x</c> over a
    /// column, or one that indexes a matrix, is handed what it expects rather than a flat row.
    /// </summary>
    /// <param name="solver">Which solver is asking; it names itself in a value-check error.</param>
    /// <param name="objective">The handle or name the caller gave.</param>
    /// <param name="dims">The starting point's shape.</param>
    /// <param name="extra">The trailing arguments a caller passed for the objective to receive.</param>
    /// <param name="checkValues">Whether a NaN or complex answer is an error rather than a value.</param>
    /// <param name="line">The call site's line, for error reporting.</param>
    /// <param name="col">The call site's column, for error reporting.</param>
    private static Func<double[], double> ObjectiveCaller(
        string solver,
        IJgsCallable objective,
        int[] dims,
        IReadOnlyList<JgsValue> extra,
        bool checkValues,
        int line,
        int col)
    {
        return point =>
        {
            var arguments = new List<JgsValue>(1 + extra.Count) { ShapedNumbers(point, dims) };
            arguments.AddRange(extra);
            JgsValue answered = objective.Call(arguments, line, col);
            return ScalarObjective(solver, objective, answered, point, checkValues, line, col);
        };
    }

    /// <summary>
    /// The single number an objective answered, refusing anything that is not one and — when the
    /// caller asked for it — refusing a NaN or a complex value too.
    /// </summary>
    private static double ScalarObjective(
        string solver,
        IJgsCallable objective,
        JgsValue answered,
        double[] at,
        bool checkValues,
        int line,
        int col)
    {
        if (answered.Type == JgsType.Complex)
        {
            if (checkValues)
            {
                throw new JgsRuntimeException(line, col, $"MATLAB:{solver}:checkfun:ComplexFval",
                    ValueCheckMessage(solver, objective, at, "a complex value"));
            }

            throw new JgsRuntimeException(line, col,
                $"{solver}: the objective must answer a real number, but it answered a complex one.");
        }

        double[] values = FlattenColumnMajor(solver, answered, line, col);
        if (values.Length != 1)
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{solver}:NonScalarObj",
                "User supplied objective function must return a scalar value.");
        }

        if (checkValues && double.IsNaN(values[0]))
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{solver}:checkfun:NaNFval",
                ValueCheckMessage(solver, objective, at, "NaN"));
        }

        return values[0];
    }

    /// <summary>
    /// The message <c>FunValCheck</c> raises, which names the objective as it was written and, for a
    /// one-parameter problem, the point it misbehaved at.
    /// </summary>
    private static string ValueCheckMessage(
        string solver, IJgsCallable objective, double[] at, string what)
    {
        string named = objective is AnonymousFunction anonymous
            ? AstPrinter.Print(anonymous.Declaration)
            : "@" + objective.Name;
        string where = at.Length == 1
            ? " at " + JgsSprintf.FormatMatlab("%g", [JgsValue.Number(at[0])])
            : string.Empty;
        return $"Objective function '{named}' returned {what} when evaluated{where}. "
            + $"{solver.ToUpperInvariant()} cannot continue.";
    }

    /// <summary>
    /// The objective a solver was handed, whether it arrived as a handle or as the name of one.
    /// MATLAB's <c>fcnchk</c> also builds an <c>inline</c> object from an expression written as text;
    /// that form is obsolete in MATLAB and absent here, and ADR 0100 records it.
    /// </summary>
    private static IJgsCallable ObjectiveOf(
        string solver, JgsEnvironment env, JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.Function)
        {
            return value.AsCallable;
        }

        if (value.Type == JgsType.String
            && env.TryGet(value.AsString, out JgsValue found)
            && found.Type == JgsType.Function)
        {
            return found.AsCallable;
        }

        throw new JgsRuntimeException(line, col, $"MATLAB:{solver}:InvalidFunctionSupplied",
            $"{solver}: the objective is a function handle, written @name or @(x) …, "
            + $"or the name of one.{(value.Type == JgsType.String ? $" '{value.AsString}' is not a function." : string.Empty)}");
    }

    // --- The problem-structure form -----------------------------------------------------------------

    /// <summary>
    /// Splits the single structure form — <c>fminsearch(problem)</c> — into the arguments the
    /// ordinary form would have been given.
    /// </summary>
    /// <remarks>
    /// The structure names its own solver, and MATLAB insists it names the one being called: a
    /// problem built for <c>fzero</c> handed to <c>fminsearch</c> is a mistake worth catching rather
    /// than a set of fields to reinterpret.
    /// </remarks>
    private static JgsValue[] UnpackProblem(
        string solver, JgsValue problem, string[] wantedFields, int line, int col)
    {
        if (!TryReadField(problem, "solver", out JgsValue named) || named.Type != JgsType.String)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:separateOptimStruct:InvalidStructInput",
                $"The problem structure must have a 'solver' field naming '{solver}'.");
        }

        if (!string.Equals(named.AsString, solver, StringComparison.OrdinalIgnoreCase))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:separateOptimStruct:InvalidSolver",
                $"Use {named.AsString.ToUpperInvariant()} function for this problem structure.");
        }

        var unpacked = new JgsValue[wantedFields.Length];
        for (int i = 0; i < wantedFields.Length; i++)
        {
            if (!TryReadField(problem, wantedFields[i], out unpacked[i]))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:separateOptimStruct:InvalidStructInput",
                    $"The problem structure for {solver.ToUpperInvariant()} needs a "
                    + $"'{wantedFields[i]}' field.");
            }
        }

        return unpacked;
    }

    /// <summary>Formats a number the way MATLAB's exit messages do.</summary>
    private static string Formatted(string format, double value) =>
        JgsSprintf.FormatMatlab(format, [JgsValue.Number(value)]);

    /// <summary>A whole number as text, for a message that counts something.</summary>
    private static string Whole(int value) => value.ToString(CultureInfo.InvariantCulture);
}
