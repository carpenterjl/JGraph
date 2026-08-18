using System.Text.RegularExpressions;
using JGraph.Maths.Sampling;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M58: the nine <c>ez*</c> verbs, which are the function plotters as MATLAB spelled them before the
/// <c>f*</c> family replaced them.
/// </summary>
/// <remarks>
/// <para>
/// Each one is its modern counterpart with two differences and no drawing of its own: the domain runs
/// over a turn of the circle rather than from −5 to 5, and the function may be written as text rather
/// than handed over as a handle. Everything else — where to read, what to draw, what to hand back —
/// is the same code, which is why a script can move from <c>ezsurf</c> to <c>fsurf</c> and get the
/// same picture.
/// </para>
/// <para>
/// The text form is why these live beside <c>eval</c> rather than beside <c>fplot</c>: turning
/// <c>'x*sin(y)'</c> into something callable means naming its variables and then evaluating a handle,
/// and only something holding the running interpreter can do the second half.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>A turn of the circle, which is where every <c>ez*</c> verb looks unless told otherwise.</summary>
    private const double LegacyTurn = 2 * System.Math.PI;

    /// <summary>The identifiers a text expression might be a function of.</summary>
    private static readonly Regex Identifiers = new(@"[A-Za-z_]\w*", RegexOptions.Compiled);

    /// <summary>The names an expression's variables are filled out from when it names too few.</summary>
    private static readonly string[] StandardVariables = ["x", "y", "z"];

    /// <summary>
    /// The names that are always a variable of the expression, whatever else the workspace is holding.
    /// </summary>
    /// <remarks>
    /// MATLAB reads a name that the workspace already answers to as that value rather than as a
    /// variable, which means <c>x = 1:10</c> earlier in a script quietly turns <c>ezplot('x^2')</c>
    /// into something else. These six are the letters every one of these verbs is documented in terms
    /// of, so they are read as variables even when the workspace has one of its own.
    /// </remarks>
    private static readonly HashSet<string> AlwaysVariables =
        new(["x", "y", "z", "t", "u", "v"], StringComparer.Ordinal);

    private static void RegisterLegacyFunctionPlotBuiltins(JgsEnvironment env, Interpreter interpreter)
    {
        void DefineSilent(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, body) { BindsAnsAsStatement = false }));

        DefineSilent("ezplot", OnNamedAxes((args, line, col) => EzPlot(args, env, interpreter, line, col)));
        DefineSilent("ezplot3", OnNamedAxes((args, line, col) => EzPlot3(args, env, interpreter, line, col)));
        DefineSilent("ezpolar", OnNamedAxes((args, line, col) => EzPolar(args, env, interpreter, line, col)));

        DefineSilent("ezsurf", OnNamedAxes((args, line, col) =>
            EzSurface("ezsurf", wireframe: false, contours: false, args, env, interpreter, line, col)));
        DefineSilent("ezmesh", OnNamedAxes((args, line, col) =>
            EzSurface("ezmesh", wireframe: true, contours: false, args, env, interpreter, line, col)));
        DefineSilent("ezsurfc", OnNamedAxes((args, line, col) =>
            EzSurface("ezsurfc", wireframe: false, contours: true, args, env, interpreter, line, col)));
        DefineSilent("ezmeshc", OnNamedAxes((args, line, col) =>
            EzSurface("ezmeshc", wireframe: true, contours: true, args, env, interpreter, line, col)));

        DefineSilent("ezcontour", OnNamedAxes((args, line, col) =>
            EzContour("ezcontour", filled: false, args, env, interpreter, line, col)));
        DefineSilent("ezcontourf", OnNamedAxes((args, line, col) =>
            EzContour("ezcontourf", filled: true, args, env, interpreter, line, col)));
    }

    /// <summary>
    /// What an <c>ez*</c> call said: the functions, however they were written, how many variables the
    /// first of them was naturally a function of, and the domain if one was given.
    /// </summary>
    private sealed record LegacyCall(List<JgsValue> Functions, int Variables, double[]? Domain);

    /// <summary>
    /// <c>ezplot(f)</c> over a turn of the circle. A text expression naming two variables draws the
    /// curve where it is zero, which is the one place these verbs decide what to draw from what they
    /// were handed rather than from which verb was called.
    /// </summary>
    /// <remarks>
    /// A function handle is always read as a function of one variable, because a handle carries no
    /// count of its own arguments here. The implicit curve of a handle is <c>fimplicit</c>, which is
    /// the same drawing under the name that says so.
    /// </remarks>
    private static JgsValue EzPlot(
        IReadOnlyList<JgsValue> args, JgsEnvironment env, Interpreter interpreter, int line, int col)
    {
        LegacyCall call = ReadLegacy("ezplot", args, 2, atLeast: 1, variablesEach: 1, env, interpreter, line, col);
        bool implicitCurve = call.Functions.Count == 1 && call.Variables == 2;

        double[] domain = call.Domain ?? (implicitCurve
            ? [-LegacyTurn, LegacyTurn, -LegacyTurn, LegacyTurn]
            : [-LegacyTurn, LegacyTurn]);

        JgsValue[] forwarded = [.. call.Functions, Numbers(domain)];
        return implicitCurve
            ? Implicit("ezplot", forwarded, line, col)
            : FunctionLine("ezplot", spatial: false, forwarded, line, col);
    }

    /// <summary>
    /// <c>ezplot3(fx, fy, fz)</c> over <c>[0, 2π]</c>. The trailing <c>'animate'</c> word is taken and
    /// the whole curve drawn at once: there is no animation seam in this build yet, and a traced
    /// curve and a drawn one differ only in how long they take to appear.
    /// </summary>
    private static JgsValue EzPlot3(
        IReadOnlyList<JgsValue> args, JgsEnvironment env, Interpreter interpreter, int line, int col)
    {
        LegacyCall call = ReadLegacy("ezplot3", args, 3, atLeast: 3, variablesEach: 1, env, interpreter, line, col);
        JgsValue[] forwarded = [.. call.Functions, Numbers(call.Domain ?? [0, LegacyTurn])];
        return FunctionLine("ezplot3", spatial: true, forwarded, line, col);
    }

    /// <summary>
    /// <c>ezpolar(f)</c> draws <c>r(θ)</c> round the circle over <c>[0, 2π]</c>.
    /// </summary>
    /// <remarks>
    /// The angles are chosen by looking at the drawn curve rather than at the radius: a circle is a
    /// constant radius, and a sampler watching only the radius would call it flat and draw a
    /// twenty-three-sided figure. So the sampler is handed three readings at each angle — where the
    /// point lands across, where it lands up, and the radius itself — and the drawing follows the two
    /// that move while the answer handed to <c>polarplot</c> is the third.
    /// </remarks>
    private static JgsValue EzPolar(
        IReadOnlyList<JgsValue> args, JgsEnvironment env, Interpreter interpreter, int line, int col)
    {
        LegacyCall call = ReadLegacy("ezpolar", args, 1, atLeast: 1, variablesEach: 1, env, interpreter, line, col);
        if (call.Functions.Count != 1)
        {
            throw new JgsRuntimeException(line, col, "ezpolar(f) takes one function of the angle.");
        }

        double[] domain = call.Domain ?? [0, LegacyTurn];
        IJgsCallable f = call.Functions[0].AsCallable;
        var vectorized = new bool?[1];

        AdaptiveSamples samples = AdaptiveSampler1D.Sample(
            angles =>
            {
                double[] thetas = angles as double[] ?? [.. angles];
                double[] radii = ReadingsOf("ezpolar", f, [thetas], ref vectorized[0], line, col);
                var across = new double[thetas.Length];
                var up = new double[thetas.Length];
                for (int i = 0; i < thetas.Length; i++)
                {
                    across[i] = radii[i] * System.Math.Cos(thetas[i]);
                    up[i] = radii[i] * System.Math.Sin(thetas[i]);
                }

                return [across, up, radii];
            },
            3,
            domain[0],
            domain[1]);

        return env.TryGet("polarplot", out JgsValue polar) && polar.Type == JgsType.Function
            ? polar.AsCallable.Call(
                [Numbers(samples.Parameters), Numbers(samples.Components[2])], line, col)
            : throw new JgsRuntimeException(line, col, "ezpolar needs polarplot, which is not declared here.");
    }

    private static JgsValue EzSurface(
        string verb,
        bool wireframe,
        bool contours,
        IReadOnlyList<JgsValue> args,
        JgsEnvironment env,
        Interpreter interpreter,
        int line,
        int col)
    {
        LegacyCall call = ReadLegacy(verb, args, 3, atLeast: 1, variablesEach: 2, env, interpreter, line, col);
        if (call.Functions.Count is not (1 or 3))
        {
            throw new JgsRuntimeException(line, col,
                $"{verb}(f) takes one function of x and y, or three functions of two parameters.");
        }

        JgsValue[] forwarded =
        [
            .. call.Functions,
            Numbers(call.Domain ?? [-LegacyTurn, LegacyTurn]),
            JgsValue.Str("ShowContours"), JgsValue.Str(contours ? "on" : "off"),
        ];

        return FunctionSurface(verb, wireframe, forwarded, line, col);
    }

    private static JgsValue EzContour(
        string verb,
        bool filled,
        IReadOnlyList<JgsValue> args,
        JgsEnvironment env,
        Interpreter interpreter,
        int line,
        int col)
    {
        LegacyCall call = ReadLegacy(verb, args, 1, atLeast: 1, variablesEach: 2, env, interpreter, line, col);
        JgsValue[] forwarded =
        [
            .. call.Functions,
            Numbers(call.Domain ?? [-LegacyTurn, LegacyTurn]),
            JgsValue.Str("Fill"), JgsValue.Str(filled ? "on" : "off"),
        ];

        return FunctionContour(verb, forwarded, line, col);
    }

    /// <summary>
    /// Reads the front of an <c>ez*</c> call: the functions, written as handles or as text, then the
    /// domain, then the legacy words that carry no argument.
    /// </summary>
    private static LegacyCall ReadLegacy(
        string verb,
        IReadOnlyList<JgsValue> args,
        int most,
        int atLeast,
        int variablesEach,
        JgsEnvironment env,
        Interpreter interpreter,
        int line,
        int col)
    {
        var functions = new List<JgsValue>();
        int variables = 0;
        int i = 0;

        while (i < args.Count && functions.Count < most)
        {
            if (args[i].Type == JgsType.Function)
            {
                functions.Add(args[i]);
                variables = System.Math.Max(variables, 1);
            }
            else if (args[i].Type == JgsType.String && !IsLegacyWord(args[i].AsString))
            {
                // A property name here is a script reaching for the modern verb's surface. Saying so
                // beats reading 'MeshDensity' as an expression in a variable of that name, which is
                // what it otherwise is.
                if (IsModernPropertyName(args[i].AsString))
                {
                    throw new JgsRuntimeException(line, col,
                        $"{verb} takes no properties — it is the legacy spelling. '{args[i].AsString}' belongs to the "
                            + "f-named verbs (fplot, fplot3, fsurf, fmesh, fcontour, fimplicit).");
                }

                (JgsValue handle, int named) = TextHandle(
                    verb, args[i].AsString, variablesEach, env, interpreter, line, col);
                functions.Add(handle);
                variables = System.Math.Max(variables, named);
            }
            else
            {
                break;
            }

            i++;
        }

        if (functions.Count < atLeast)
        {
            throw new JgsRuntimeException(line, col,
                $"{verb} takes {atLeast} function{(atLeast == 1 ? "" : "s")}, written as a handle or as text.");
        }

        double[]? domain = null;
        if (i < args.Count && args[i].Type is JgsType.Array or JgsType.Number)
        {
            domain = ToDoubles(verb, args[i], line, col);
            if (domain.Length is not (2 or 4 or 6) || !domain.All(double.IsFinite))
            {
                throw new JgsRuntimeException(line, col,
                    $"{verb}: a domain is [min max], or one pair for each direction, all finite.");
            }

            i++;
        }

        // 'animate' and its like carry nothing and are taken as read; anything else is a mistake.
        for (; i < args.Count; i++)
        {
            if (args[i].Type != JgsType.String || !IsLegacyWord(args[i].AsString))
            {
                throw new JgsRuntimeException(line, col,
                    $"{verb} takes its functions, an optional domain, and nothing else. "
                        + "The modern spelling of this verb takes properties.");
            }
        }

        return new LegacyCall(functions, variables, domain);
    }

    private static bool IsModernPropertyName(string text) =>
        FunctionLineOptions.Contains(text)
        || FunctionSurfaceOptions.Contains(text)
        || FunctionContourOptions.Contains(text)
        || ImplicitSurfaceOptions.Contains(text);

    private static bool IsLegacyWord(string text) =>
        text.Equals("animate", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Turns a text expression into a function handle, and answers with how many variables it names.
    /// </summary>
    /// <remarks>
    /// A name in the text that nothing in the workspace answers to is a variable — which is how
    /// <c>'x*sin(y)'</c> comes out a function of x and y while <c>sin</c> and <c>pi</c> stay
    /// themselves. The variables are taken in alphabetical order, so <c>'u*v'</c> is a function of u
    /// then v; an expression naming fewer than the verb needs is filled out from x, y and z, which is
    /// what makes <c>ezsurf('x^2')</c> a surface rather than an error.
    /// </remarks>
    private static (JgsValue Handle, int Variables) TextHandle(
        string verb,
        string text,
        int atLeast,
        JgsEnvironment env,
        Interpreter interpreter,
        int line,
        int col)
    {
        var named = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Match match in Identifiers.Matches(text))
        {
            if (AlwaysVariables.Contains(match.Value) || !env.TryGet(match.Value, out _))
            {
                named.Add(match.Value);
            }
        }

        if (named.Count > 3)
        {
            throw new JgsRuntimeException(line, col,
                $"{verb}: '{text}' names {named.Count} variables ({string.Join(", ", named)}); at most three are read.");
        }

        var parameters = new List<string>(named);
        foreach (string standard in StandardVariables)
        {
            if (parameters.Count >= atLeast)
            {
                break;
            }

            if (!parameters.Contains(standard))
            {
                parameters.Add(standard);
            }
        }

        parameters.Sort(StringComparer.Ordinal);
        JgsValue handle = interpreter.EvaluateSource(
            $"@({string.Join(", ", parameters)}) ({text})", interpreter.CurrentFrame, line, col);

        return handle.Type == JgsType.Function
            ? (handle, named.Count)
            : throw new JgsRuntimeException(line, col, $"{verb}: '{text}' is not an expression.");
    }
}
