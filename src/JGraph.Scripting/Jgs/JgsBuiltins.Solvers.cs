using System.Globalization;
using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The quadrature pair and the ODE options structure (M121): <c>integral</c>, <c>quadgk</c>,
/// <c>odeset</c> and <c>odeget</c>.
/// </summary>
/// <remarks>
/// <para>
/// These are the three names the capability probe found missing from the solvers group, and they
/// come as a set for a reason. <c>integral</c> and <c>quadgk</c> are one method behind two
/// interfaces — MATLAB's own <c>integral</c> calls <c>quadgk</c> — so they share
/// <see cref="Quadrature"/> and differ only in what they are allowed to be asked and what they hand
/// back. <c>odeset</c> is the other half of a name that was already here: <c>ode45</c> had no way
/// to be told a tolerance, so its accuracy was whatever the default was and <c>Refine</c>,
/// <c>MaxStep</c> and <c>InitialStep</c> could not be asked for at all.
/// </para>
/// <para>
/// The integrand is called with every abscissa of a panel at once. That is MATLAB's contract too —
/// an integrand must be vectorised unless <c>'ArrayValued'</c> says otherwise — and it is the
/// difference between fifteen calls into the interpreter per panel and one.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// The 22 settings <c>odeset</c> holds, in the order MATLAB's <c>fieldnames</c> reports them.
    /// </summary>
    /// <remarks>
    /// The order is not alphabetical and not obviously anything else — it is the order MATLAB's own
    /// structure is built in, read off R2024a rather than guessed, because a script that walks
    /// <c>fieldnames(odeset)</c> sees it.
    /// </remarks>
    private static readonly string[] OdesetFields =
    [
        "AbsTol", "BDF", "Events", "InitialStep", "Jacobian", "JConstant", "JPattern", "Mass",
        "MassSingular", "MaxOrder", "MaxStep", "NonNegative", "NormControl", "OutputFcn", "OutputSel",
        "Refine", "RelTol", "Stats", "Vectorized", "MStateDependence", "MvPattern", "InitialSlope",
    ];

    /// <summary>Registers the quadrature pair and the ODE options structure.</summary>
    internal static void RegisterSolverBuiltins(JgsEnvironment env)
    {
        env.Declare("integral", JgsValue.Function(new BuiltinFunction(
            "integral", (args, line, col) => Integrate("integral", args, wanted: 1, line, col)[0])));

        env.Declare("quadgk", JgsValue.Function(new BuiltinFunction(
            "quadgk", (args, line, col) => Integrate("quadgk", args, wanted: 1, line, col)[0])
        {
            // quadgk's second output is the bound on its own error, which is the one thing it has
            // that integral does not.
            MultiOutput = (args, wanted, line, col) => Integrate("quadgk", args, wanted, line, col),
        }));

        env.Declare("odeset", JgsValue.Function(new BuiltinFunction(
            "odeset", (args, line, col) => Odeset(args, line, col))
        {
            // The bare word is a call, not a handle — the same pair of flags optimset needs, and for
            // the same reason.
            AutoCallsBare = true,
        }));

        env.Declare("odeget", JgsValue.Function(new BuiltinFunction("odeget", Odeget)));
    }

    // --- integral and quadgk ------------------------------------------------------------------------

    /// <summary>
    /// <c>q = integral(fun, a, b, ...)</c> and <c>[q, errbnd] = quadgk(fun, a, b, ...)</c>.
    /// </summary>
    private static JgsValue[] Integrate(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 3)
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{name}:NotEnoughInputs",
                $"{name} requires a function, a lower limit and an upper limit.");
        }

        if (args[0].Type != JgsType.Function)
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{name}:invalidFun",
                $"{name} expects a function handle as its first argument.");
        }

        IJgsCallable f = args[0].AsCallable;
        double a = Num(name, args, 1, line, col);
        double b = Num(name, args, 2, line, col);

        double relative = Quadrature.DefaultRelativeTolerance;
        double absolute = Quadrature.DefaultAbsoluteTolerance;
        int maximum = Quadrature.DefaultMaximumIntervals;
        bool oneAtATime = false;
        List<double>? waypoints = null;

        if ((args.Count - 3) % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{name}:ArgNameValueMismatch",
                "Arguments must occur in name-value pairs.");
        }

        for (int i = 3; i < args.Count; i += 2)
        {
            string option = Str(name, args, i, line, col).Trim();
            JgsValue value = args[i + 1];
            if (Names(option, "RelTol"))
            {
                relative = Num(name, args, i + 1, line, col);
            }
            else if (Names(option, "AbsTol"))
            {
                absolute = Num(name, args, i + 1, line, col);
            }
            else if (Names(option, "MaxIntervalCount"))
            {
                maximum = Count(name, args, i + 1, line, col);
            }
            else if (Names(option, "ArrayValued"))
            {
                oneAtATime = Truth(value);
            }
            else if (Names(option, "Waypoints"))
            {
                waypoints = [.. ToDoubles(name, value, line, col)];
            }
            else
            {
                throw new JgsRuntimeException(line, col, $"MATLAB:{name}:invalidOption",
                    $"'{option}' is not a recognized option for {name}.");
            }
        }

        double[] Sample(double[] at)
        {
            // 'ArrayValued' is MATLAB's way of saying the integrand cannot take a vector, so it is
            // asked one point at a time. Everything else is asked once for the whole panel.
            if (oneAtATime)
            {
                var one = new double[at.Length];
                for (int i = 0; i < at.Length; i++)
                {
                    one[i] = OneValueOf(name, f, JgsValue.Number(at[i]), line, col);
                }

                return one;
            }

            JgsValue answered = f.Call([Numbers(at)], line, col);
            double[] values = ToDoubles(name, answered, line, col);
            if (values.Length != at.Length)
            {
                // MATLAB's own refusal, word for word. A constant integrand written without a dot
                // — @(x) 1 — lands here rather than being spread across the panel, because being
                // spread is indistinguishable from being right and the caller would never learn
                // that the rest of the expression was not vectorised either.
                throw new JgsRuntimeException(line, col, $"MATLAB:{name}:FxNotSameSizeAsX",
                    "Output of the function must be the same size as the input. If FUN is an "
                    + "array-valued integrand, set the 'ArrayValued' option to true.");
            }

            return values;
        }

        Quadrature.Result result;
        try
        {
            result = Quadrature.Integrate(Sample, a, b, relative, absolute, waypoints, maximum);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"{name}: {ex.Message}");
        }

        return wanted >= 2
            ? [JgsValue.Number(result.Value), JgsValue.Number(result.ErrorBound)]
            : [JgsValue.Number(result.Value)];
    }

    /// <summary>One value out of an integrand asked about one point.</summary>
    private static double OneValueOf(string name, IJgsCallable f, JgsValue at, int line, int col)
    {
        JgsValue answered = f.Call([at], line, col);
        double[] values = ToDoubles(name, answered, line, col);
        return values.Length == 1
            ? values[0]
            : throw new JgsRuntimeException(line, col, $"MATLAB:{name}:invalidFun",
                $"{name}: the integrand answered {values.Length} value(s) for one point.");
    }

    /// <summary>Whether a written option name is this one, ignoring case as MATLAB does.</summary>
    private static bool Names(string typed, string option) =>
        string.Equals(typed, option, StringComparison.OrdinalIgnoreCase);

    /// <summary>An option value read as true or false.</summary>
    private static bool Truth(JgsValue value) => value.Type switch
    {
        JgsType.Bool => value.AsNumber != 0,
        JgsType.Number => value.AsNumber != 0,
        JgsType.String => !string.Equals(value.AsString, "off", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(value.AsString, "false", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    // --- odeset and odeget --------------------------------------------------------------------------

    /// <summary>
    /// <c>options = odeset(...)</c>: the structure <c>ode45</c> reads its settings from.
    /// </summary>
    /// <remarks>
    /// Every field is present and every unset one is <c>[]</c>, which is what lets the solver tell
    /// "not asked" from "asked for nothing" — the same convention <c>optimset</c> keeps, and the
    /// reason <c>odeset(old, 'Refine', 8)</c> copies only what the old structure actually set.
    /// </remarks>
    private static JgsValue Odeset(IReadOnlyList<JgsValue> args, int line, int col)
    {
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        foreach (string field in OdesetFields)
        {
            fields[field] = JgsValue.Array([]);
        }

        int from = 0;
        while (from < args.Count && args[from].Type != JgsType.String && !IsTextScalar(args[from]))
        {
            if (args[from].Type == JgsType.Array && args[from].ArrayLength == 0)
            {
                from++; // [] contributes nothing, and is a legal thing to pass
                continue;
            }

            if (args[from].Type != JgsType.Struct)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:odeset:NoPropNameOrStruct",
                    $"Expected argument {from + 1} to be a property name or an options structure "
                    + "created with ODESET.");
            }

            foreach (string field in OdesetFields)
            {
                if (TryReadField(args[from], field, out JgsValue value) && !IsUnsetOption(value))
                {
                    fields[field] = value;
                }
            }

            from++;
        }

        if ((args.Count - from) % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:odeset:ArgNameValueMismatch",
                "Arguments must occur in name-value pairs.");
        }

        for (int i = from; i < args.Count; i += 2)
        {
            fields[OdePropertyNamed("odeset", TextArgument("odeset", args, i, line, col), line, col)]
                = args[i + 1];
        }

        return JgsValue.Struct(fields);
    }

    /// <summary><c>value = odeget(options, name, default)</c>: one setting, or what to use instead.</summary>
    private static JgsValue Odeget(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:odeget:NotEnoughInputs",
                "Not enough input arguments.");
        }

        JgsValue fallback = args.Count > 2 ? args[2] : JgsValue.Array([]);
        if (args[0].Type == JgsType.Array && args[0].ArrayLength == 0)
        {
            return fallback;
        }

        if (args[0].Type != JgsType.Struct)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:odeget:Arg1NotODESETstruct",
                "First argument must be an options structure created with ODESET.");
        }

        string field = OdePropertyNamed("odeget", TextArgument("odeget", args, 1, line, col), line, col);
        return TryReadField(args[0], field, out JgsValue value) && !IsUnsetOption(value) ? value : fallback;
    }

    /// <summary>One text argument, however it was written.</summary>
    private static string TextArgument(string name, IReadOnlyList<JgsValue> args, int index, int line, int col) =>
        IsTextScalar(args[index])
            ? TextOf(args[index])
            : throw new JgsRuntimeException(line, col, $"MATLAB:{name}:InvalidPropName",
                $"Expected argument {index + 1} to be a property name.");

    /// <summary>
    /// The full property name an abbreviation stands for: case-insensitive, and any unique leading
    /// portion will do — <c>odeset('rel', 1e-3)</c> is <c>RelTol</c>, which MATLAB accepts and a
    /// script written against it relies on.
    /// </summary>
    private static string OdePropertyNamed(string caller, string typed, int line, int col)
    {
        string wanted = typed.Trim();
        var matches = new List<string>();
        foreach (string field in OdesetFields)
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

        foreach (string field in matches)
        {
            // An exact spelling wins a tie with the longer names it is a prefix of.
            if (string.Equals(field, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return field;
            }
        }

        throw matches.Count == 0
            ? new JgsRuntimeException(line, col, $"MATLAB:{caller}:InvalidPropName",
                $"Unrecognized property name '{wanted}'.")
            : new JgsRuntimeException(line, col, $"MATLAB:{caller}:AmbiguousPropName",
                $"Ambiguous property name '{wanted}' ({string.Join(", ", matches)}).");
    }

    /// <summary>One numeric setting out of an options structure, or null when it was not set.</summary>
    private static double? OdeNumber(JgsValue? options, string field)
    {
        if (options is not { Type: JgsType.Struct } given
            || !TryReadField(given, field, out JgsValue value)
            || IsUnsetOption(value))
        {
            return null;
        }

        return value.Type is JgsType.Number or JgsType.Bool ? value.AsNumber
            : value.Type == JgsType.Array && value.ArrayLength == 1 ? value.ElementAt(0).AsNumber
            : null;
    }

    /// <summary>
    /// The four settings <c>ode45</c> knows how to obey, read out of an options structure.
    /// </summary>
    /// <remarks>
    /// The other eighteen are accepted and stored — a script may set them, and <c>odeget</c> reads
    /// them back — but nothing here acts on them. That is deliberate and recorded rather than
    /// hidden: the alternative is refusing a structure over a field the solve does not need.
    /// </remarks>
    private static (double Relative, double Absolute, int Refine, double? MaxStep, double? FirstStep)
        Ode45Settings(JgsValue? options) =>
        (OdeNumber(options, "RelTol") ?? 1e-3,
         OdeNumber(options, "AbsTol") ?? 1e-6,
         (int)(OdeNumber(options, "Refine") ?? OdeSolvers.DefaultRefine),
         OdeNumber(options, "MaxStep"),
         OdeNumber(options, "InitialStep"));
}
