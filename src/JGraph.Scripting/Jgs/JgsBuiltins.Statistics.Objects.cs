using JGraph.Statistics.Distributions;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M53 wave I: the distribution objects. <c>makedist</c> and <c>fitdist</c> build one, the
/// twenty-nine class names build one directly, and every function that took a distribution as a word
/// now also takes it as an object.
/// </summary>
/// <remarks>
/// <para>
/// A distribution object here is a struct carrying the properties MathWorks documents, tagged with
/// the class it stands for — the same mechanism <c>affine2d</c> and <c>strel</c> have used since M46.
/// A handle into a registry was the other candidate and is the wrong one: MathWorks' distributions
/// are <em>value</em> classes, so <c>pd2 = pd; pd2 = truncate(pd2, 0, 1)</c> must leave <c>pd</c>
/// alone, and a struct gives that for nothing where a handle would have to be taught it. The cost is
/// that a script can write <c>pd.mu = 3</c>, which MathWorks refuses; the object it gets is
/// consistent, since every function reads the parameters back out of the struct, but it is a
/// divergence and it is recorded.
/// </para>
/// <para>
/// Almost nothing here is new arithmetic. A family is still a <see cref="DistributionFamily"/>, the
/// fitter is still the one wave C wrote, and the confidence limits <c>paramci</c> answers are the
/// fitter's own — computed by asking it again at the requested confidence rather than by a second
/// formula that could disagree with the first. What the object adds is truncation, and that lives one
/// layer down in <see cref="DistributionObject"/> so that a truncated distribution answers every
/// question the same way an untruncated one does.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>How a class's documented properties map onto the parameter vector its family takes.</summary>
    private enum DistributionShape
    {
        /// <summary>One property per parameter, in order — every family but three.</summary>
        Scalars,

        /// <summary>One property holding the whole vector of category probabilities.</summary>
        Probabilities,

        /// <summary>Two properties holding the breakpoints and the probabilities at them.</summary>
        Breakpoints,

        /// <summary>A smoothing kernel and a bandwidth, over the data the object was fitted to.</summary>
        Smoothed,
    }

    /// <summary>
    /// One distribution class: the name it answers to, the properties it publishes, and how those
    /// properties become the parameter vector its family works from.
    /// </summary>
    /// <param name="ClassName">The class, as <c>class(pd)</c> reports it after the <c>prob.</c> prefix.</param>
    /// <param name="Name">The distribution name, as <c>pd.DistributionName</c> reports it.</param>
    /// <param name="Properties">The documented property names, in the order the parameters take.</param>
    /// <param name="Defaults">What <c>makedist</c> uses for a property the caller left out.</param>
    /// <param name="Descriptions">What each property means, for <c>pd.ParameterDescription</c>.</param>
    /// <param name="Family">The family's name, for the three built per instance.</param>
    /// <param name="Shape">How the properties map onto the parameter vector.</param>
    /// <param name="ByName">Whether <c>makedist</c> offers this one; the kernel fit is fitted, not made.</param>
    private sealed record DistributionClass(
        string ClassName,
        string Name,
        string[] Properties,
        double[] Defaults,
        string[] Descriptions,
        string? Family,
        DistributionShape Shape = DistributionShape.Scalars,
        bool ByName = true);

    /// <summary>Every distribution class, keyed by its name, its class name and its aliases.</summary>
    private static readonly Dictionary<string, DistributionClass> DistributionClasses = BuildDistributionClasses();

    private static readonly OptionSpec FitDistOptions = new(
        "fitdist",
        [],
        ["censoring", "frequency", "by", "options", "kernel", "support", "width", "ntrials", "theta", "mu", "n"]);

    private static readonly OptionSpec ObjectIntervalOptions = new(
        "paramci", [], ["alpha", "parameter", "type", "logflag"]);

    private static readonly OptionSpec ProfileOptions = new(
        "proflik", [], ["display", "setrange"]);

    /// <summary>
    /// Registers the object names and re-declares the nine that now also take an object. This runs
    /// after every other define, because six of those nine are base reductions that the MATLAB
    /// dialect wraps for a dimension — wrapping them any earlier would put the object check under the
    /// dimension machinery instead of in front of it.
    /// </summary>
    private static void RegisterDistributionObjectForms(JgsEnvironment env, Random random)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        void DefineBoth(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> both) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, (args, line, col) => both(args, 1, line, col)[0]) { MultiOutput = both }));

        // makedist on its own answers the list of names it knows, so the bare word has to call
        // rather than evaluate to the function itself.
        env.Declare("makedist", JgsValue.Function(new BuiltinFunction(
            "makedist", (args, line, col) => MakeDistribution(args, line, col)) { AutoCallsBare = true }));
        DefineBoth("fitdist", (args, wanted, line, col) => FitDistribution(args, wanted, line, col));
        Define("truncate", (args, line, col) => TruncateDistribution(args, line, col));
        Define("negloglik", (args, line, col) => ObjectNegativeLogLikelihood(args, line, col));
        Define("paramci", (args, line, col) => ObjectParameterInterval(args, line, col));
        DefineBoth("proflik", (args, wanted, line, col) => ProfileLikelihood(args, wanted, line, col));

        // The class names construct too. MathWorks documents makedist and fitdist as the way in and
        // says nothing about calling the class, so this is purely additive: a spelling that errored
        // now works, and no spelling that worked has changed.
        foreach (DistributionClass description in DistinctClasses())
        {
            DistributionClass captured = description;
            Define(captured.ClassName, (args, line, col) => ConstructDistribution(captured, args, line, col));
        }

        // And the nine that answer about a distribution however it was named. Each keeps whatever it
        // did before for every other argument, by calling the definition it is standing in front of.
        Guard(env, "pdf", (obj, args, line, col) => Elementwise("pdf", obj, args, 1, line, col));
        Guard(env, "cdf", (obj, args, line, col) => Elementwise("cdf", obj, args, 1, line, col));
        Guard(env, "icdf", (obj, args, line, col) => Elementwise("icdf", obj, args, 1, line, col));
        Guard(env, "random", (obj, args, line, col) => DrawFrom(obj, random, args, line, col));
        Guard(env, "mean", (obj, args, line, col) => Moment("mean", obj, args, line, col));
        Guard(env, "median", (obj, args, line, col) => Moment("median", obj, args, line, col));
        Guard(env, "std", (obj, args, line, col) => Moment("std", obj, args, line, col));
        Guard(env, "var", (obj, args, line, col) => Moment("var", obj, args, line, col));
        Guard(env, "iqr", (obj, args, line, col) => Moment("iqr", obj, args, line, col));
    }

    /// <summary>
    /// Puts an object check in front of an existing builtin. The name keeps its old behaviour for
    /// everything that is not a distribution object, because the old definition is what the guard
    /// falls through to — there is no copy of it here to drift out of step.
    /// </summary>
    private static void Guard(
        JgsEnvironment env, string name,
        Func<DistributionObject, IReadOnlyList<JgsValue>, int, int, JgsValue> onObject)
    {
        if (!env.TryGet(name, out JgsValue existing) || existing.Type != JgsType.Function)
        {
            return;
        }

        IJgsCallable inner = existing.AsCallable;
        var builtin = inner as BuiltinFunction;

        JgsValue[] Both(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
        {
            if (args.Count > 0
                && TryReadDistribution(args[0], out DistributionObject? distribution, out _)
                && distribution is not null)
            {
                return [onObject(distribution, args, line, col)];
            }

            // One output goes through the single-output body and not through the multi-output one.
            // They are not always the same code: a reduction the MATLAB dialect wrapped for a
            // dimension handles its option words in the body, and routing a one-output call through
            // the multi-output form loses them.
            return wanted > 1 && builtin?.MultiOutput is { } multi
                ? multi(args, wanted, line, col)
                : [inner.Call(args, line, col)];
        }

        env.Declare(name, JgsValue.Function(new BuiltinFunction(
            name, (args, line, col) => Both(args, 1, line, col)[0])
        {
            MultiOutput = Both,
            AutoCallsBare = builtin?.AutoCallsBare ?? false,
        }));
    }

    // --- Building an object -------------------------------------------------------------------------

    /// <summary><c>makedist('Normal', 'mu', 10, 'sigma', 2)</c>, and the bare list of names.</summary>
    private static JgsValue MakeDistribution(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            var names = DistinctClasses().Where(c => c.ByName).Select(c => c.Name)
                .Order(StringComparer.Ordinal).ToArray();
            return TextColumn(names);
        }

        DistributionClass description = NamedClass("makedist", args[0], line, col);
        if (!description.ByName)
        {
            throw new JgsRuntimeException(line, col,
                $"makedist cannot build a {description.Name} distribution: it has no parameters to be given, "
                + "only data to be fitted. Use fitdist(x, 'Kernel') instead.");
        }

        return ConstructDistribution(description, args.Skip(1).ToList(), line, col);
    }

    /// <summary>Builds an object from name-value pairs naming its documented properties.</summary>
    private static JgsValue ConstructDistribution(
        DistributionClass description, IReadOnlyList<JgsValue> args, int line, int col)
    {
        var spec = new OptionSpec(description.ClassName, [], LowerAll(description.Properties));
        ParsedArgs parsed = spec.Parse(args, 0, line, col);

        var given = new List<double[]>();
        for (int i = 0; i < description.Properties.Length; i++)
        {
            JgsValue? value = parsed.Named(description.Properties[i].ToLowerInvariant());
            given.Add(value is null
                ? DefaultFor(description, i)
                : ToDoubles(description.ClassName, value, line, col));
        }

        double[] parameters = Assemble(description, given, line, col);
        DistributionFamily family = FamilyFor(description, parameters, "normal", line, col);
        var built = new DistributionObject(family, parameters);
        Validate(description, built, line, col);
        return DistributionValueOf(description, built, "normal", null);
    }

    /// <summary>The documented default of one property, as a vector.</summary>
    private static double[] DefaultFor(DistributionClass description, int index) =>
        description.Shape switch
        {
            DistributionShape.Probabilities => [0.5, 0.5],
            DistributionShape.Breakpoints => index == 0 ? [0, 1] : [0, 1],
            _ => [description.Defaults[index]],
        };

    /// <summary>Lays the property values out as the flat parameter vector the family reads.</summary>
    private static double[] Assemble(
        DistributionClass description, List<double[]> given, int line, int col)
    {
        switch (description.Shape)
        {
            case DistributionShape.Probabilities:
            {
                double[] probabilities = given[0];
                double total = probabilities.Sum();
                if (probabilities.Length == 0 || probabilities.Any(p => p < 0) || !(total > 0))
                {
                    throw new JgsRuntimeException(line, col,
                        "A multinomial's probabilities must be a non-empty row of non-negative numbers.");
                }

                return [.. probabilities.Select(p => p / total)];
            }

            case DistributionShape.Breakpoints:
            {
                double[] x = given[0];
                double[] cumulative = given[1];
                if (x.Length != cumulative.Length || x.Length < 2)
                {
                    throw new JgsRuntimeException(line, col,
                        "A piecewise-linear distribution needs x and Fx to be vectors of the same length, "
                        + "at least two long.");
                }

                return [.. x, .. cumulative];
            }

            default:
            {
                var flat = new double[description.Properties.Length];
                for (int i = 0; i < flat.Length; i++)
                {
                    if (given[i].Length != 1)
                    {
                        throw new JgsRuntimeException(line, col,
                            $"{description.ClassName}: '{description.Properties[i]}' takes one number, "
                            + $"but {given[i].Length} were given.");
                    }

                    flat[i] = given[i][0];
                }

                return flat;
            }
        }
    }

    /// <summary>The family a class works through, built on the spot for the three that need to be.</summary>
    private static DistributionFamily FamilyFor(
        DistributionClass description, double[] parameters, string kernel, int line, int col)
    {
        switch (description.Shape)
        {
            case DistributionShape.Probabilities:
                return ObjectFamilies.Multinomial(parameters.Length);

            case DistributionShape.Breakpoints:
                return ObjectFamilies.PiecewiseLinear(parameters.Length / 2);

            case DistributionShape.Smoothed:
                return Guarded("fitdist", () => ObjectFamilies.Kernel(parameters.Length - 1, kernel), line, col);

            default:
                return ContinuousFamilies.Find(description.Family!)
                    ?? DiscreteFamilies.Find(description.Family!)
                    ?? ObjectFamilies.Find(description.Family!)
                    ?? throw new JgsRuntimeException(line, col,
                        $"This build has no {description.Name} distribution, which is a defect rather than a limit.");
        }
    }

    /// <summary>
    /// Refuses the parameter values the family itself cannot say anything sensible about. The check is
    /// the family's own claim about which parameters must stay positive, so a family added later is
    /// covered without a line changing here.
    /// </summary>
    private static void Validate(
        DistributionClass description, DistributionObject built, int line, int col)
    {
        bool[] positive = built.Family.PositiveParameters;
        for (int i = 0; i < built.Parameters.Length && i < positive.Length; i++)
        {
            if (positive[i] && !(built.Parameters[i] > 0))
            {
                string named = description.Shape == DistributionShape.Scalars
                    ? description.Properties[i]
                    : built.Family.ParameterNames[i];
                throw new JgsRuntimeException(line, col,
                    $"{description.ClassName}: '{named}' must be above zero, but {built.Parameters[i]} was given.");
            }
        }

        if (description.Shape == DistributionShape.Breakpoints)
        {
            int n = built.Parameters.Length / 2;
            for (int i = 1; i < n; i++)
            {
                if (built.Parameters[i] <= built.Parameters[i - 1]
                    || built.Parameters[n + i] < built.Parameters[n + i - 1])
                {
                    throw new JgsRuntimeException(line, col,
                        "A piecewise-linear distribution needs increasing breakpoints and a "
                        + "non-decreasing Fx along them.");
                }
            }
        }
    }

    // --- Reading an object back -----------------------------------------------------------------------

    /// <summary>The struct value an object is carried in.</summary>
    private static JgsValue DistributionValueOf(
        DistributionClass description, DistributionObject built, string kernel, JgsValue? inputData)
    {
        var fields = new List<(string, JgsValue)>
        {
            (TransformTag, JgsValue.Str("prob." + description.ClassName)),
            ("DistributionName", JgsValue.Str(description.Name)),
        };

        switch (description.Shape)
        {
            case DistributionShape.Probabilities:
                fields.Add(("probabilities", RowVector(built.Parameters)));
                break;

            case DistributionShape.Breakpoints:
            {
                int n = built.Parameters.Length / 2;
                fields.Add(("x", RowVector(built.Parameters[..n])));
                fields.Add(("Fx", RowVector(built.Parameters[n..])));
                break;
            }

            case DistributionShape.Smoothed:
                fields.Add(("Kernel", JgsValue.Str(kernel)));
                fields.Add(("BandWidth", JgsValue.Number(built.Parameters[0])));
                break;

            default:
                for (int i = 0; i < description.Properties.Length; i++)
                {
                    fields.Add((description.Properties[i], JgsValue.Number(built.Parameters[i])));
                }

                break;
        }

        bool scalars = description.Shape == DistributionShape.Scalars;
        fields.Add(("NumParameters", JgsValue.Number(PublishedCount(description))));
        fields.Add(("ParameterNames", TextRowOf(PublishedNames(description))));
        fields.Add(("ParameterDescription", TextRowOf(description.Descriptions)));
        fields.Add(("ParameterValues", scalars ? RowVector(built.Parameters)
            : description.Shape == DistributionShape.Probabilities ? RowVector(built.Parameters)
            : JgsValue.Array([])));
        fields.Add(("Truncation", RowVector([built.Lower, built.Upper])));
        fields.Add(("IsTruncated", JgsValue.Bool(built.IsTruncated)));
        fields.Add(("InputData", inputData ?? EmptyInputData()));

        return Structure([.. fields]);
    }

    /// <summary>How many parameters a class publishes; the two non-parametric ones publish none.</summary>
    private static int PublishedCount(DistributionClass description) =>
        description.Shape switch
        {
            DistributionShape.Probabilities => 1,
            DistributionShape.Breakpoints => 0,
            DistributionShape.Smoothed => 0,
            _ => description.Properties.Length,
        };

    private static string[] PublishedNames(DistributionClass description) =>
        description.Shape switch
        {
            DistributionShape.Probabilities => ["probabilities"],
            DistributionShape.Breakpoints => [],
            DistributionShape.Smoothed => [],
            _ => description.Properties,
        };

    private static JgsValue EmptyInputData() => Structure(
        ("data", JgsValue.Array([])),
        ("cens", JgsValue.Array([])),
        ("freq", JgsValue.Array([])));

    /// <summary>Whether a value is a distribution object, and the object it carries when it is.</summary>
    private static bool TryReadDistribution(
        JgsValue value,
        out DistributionObject? built,
        out DistributionClass? description)
    {
        built = null;
        description = null;

        if (value.Type != JgsType.Struct
            || !value.AsStruct.TryGetValue(TransformTag, out JgsValue? tag)
            || tag.Type != JgsType.String
            || !tag.AsString.StartsWith("prob.", StringComparison.Ordinal)
            || !DistributionClasses.TryGetValue(
                ContinuousFamilies.Normalize(tag.AsString[5..]), out description))
        {
            return false;
        }

        IReadOnlyDictionary<string, JgsValue> map = value.AsStruct;
        double[] parameters = ParametersOf(description, map);
        string kernel = map.TryGetValue("Kernel", out JgsValue? named) && named.Type == JgsType.String
            ? named.AsString
            : "normal";

        DistributionFamily family = FamilyFor(description, parameters, kernel, 0, 0);
        double lower = double.NegativeInfinity;
        double upper = double.PositiveInfinity;
        if (map.TryGetValue("Truncation", out JgsValue? limits) && limits.Type == JgsType.Array
            && limits.ArrayLength == 2)
        {
            lower = limits.ElementAt(0).AsNumber;
            upper = limits.ElementAt(1).AsNumber;
        }

        built = new DistributionObject(family, parameters, lower, upper);
        return true;
    }

    /// <summary>Rebuilds the parameter vector from the properties the object published.</summary>
    private static double[] ParametersOf(
        DistributionClass description, IReadOnlyDictionary<string, JgsValue> map)
    {
        double[] Read(string name) =>
            map.TryGetValue(name, out JgsValue? value) ? Flatten(value) : [];

        switch (description.Shape)
        {
            case DistributionShape.Probabilities:
                return Read("probabilities");

            case DistributionShape.Breakpoints:
                return [.. Read("x"), .. Read("Fx")];

            case DistributionShape.Smoothed:
            {
                double[] data = map.TryGetValue("InputData", out JgsValue? input)
                    && input.Type == JgsType.Struct
                    && input.AsStruct.TryGetValue("data", out JgsValue? observations)
                        ? Flatten(observations)
                        : [];
                return [.. Read("BandWidth"), .. data];
            }

            default:
            {
                var flat = new double[description.Properties.Length];
                for (int i = 0; i < flat.Length; i++)
                {
                    double[] one = Read(description.Properties[i]);
                    flat[i] = one.Length > 0 ? one[0] : double.NaN;
                }

                return flat;
            }
        }
    }

    /// <summary>Every number in a value, in order, with no complaint about its shape.</summary>
    private static double[] Flatten(JgsValue value)
    {
        if (value.Type == JgsType.Number)
        {
            return [value.AsNumber];
        }

        if (value.Type != JgsType.Array)
        {
            return [];
        }

        var numbers = new double[value.ArrayLength];
        for (int i = 0; i < numbers.Length; i++)
        {
            JgsValue element = value.ElementAt(i);
            numbers[i] = element.Type == JgsType.Number ? element.AsNumber
                : element.Type == JgsType.Bool ? (element.AsBool ? 1 : 0)
                : double.NaN;
        }

        return numbers;
    }

    /// <summary>The object a function was handed, or an error naming what it got instead.</summary>
    private static (DistributionObject Built, DistributionClass Description) RequireDistribution(
        string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count > 0
            && TryReadDistribution(args[0], out DistributionObject? built, out DistributionClass? description))
        {
                return (built!, description!);
        }

        throw new JgsRuntimeException(line, col,
            $"{name} takes a probability distribution object first — one made by makedist or fitdist.");
    }

    // --- Fitting ------------------------------------------------------------------------------------

    /// <summary><c>fitdist(x, 'Weibull', 'Censoring', c, 'Frequency', f, 'By', g)</c>.</summary>
    private static JgsValue[] FitDistribution(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("fitdist", args, 2, 32, line, col);
        double[] data = ToDoubles("fitdist", args[0], line, col);
        DistributionClass description = NamedClass("fitdist", args[1], line, col);
        ParsedArgs parsed = FitDistOptions.Parse(args.Skip(2).ToList(), 0, line, col);

        double[]? grouping = parsed.Named("by") is JgsValue by
            ? ToDoubles("fitdist", by, line, col)
            : null;

        if (grouping is null)
        {
            return [FitOne(description, data, parsed, line, col)];
        }

        if (grouping.Length != data.Length)
        {
            throw new JgsRuntimeException(line, col,
                "fitdist: 'By' must name one group per observation.");
        }

        // One object per group, in the order the groups first appear — which is what makes the
        // second output, the group names, line up with the first.
        var order = new List<double>();
        var buckets = new Dictionary<double, List<double>>();
        for (int i = 0; i < data.Length; i++)
        {
            if (!buckets.TryGetValue(grouping[i], out List<double>? bucket))
            {
                bucket = [];
                buckets[grouping[i]] = bucket;
                order.Add(grouping[i]);
            }

            bucket.Add(data[i]);
        }

        order.Sort();
        var objects = new JgsValue[order.Count];
        var names = new string[order.Count];
        for (int i = 0; i < order.Count; i++)
        {
            objects[i] = FitOne(description, [.. buckets[order[i]]], parsed, line, col);
            names[i] = order[i].ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }

        return Outputs(wanted, JgsValue.Cell(objects), TextColumn(names));
    }

    /// <summary>Fits one sample, and hands back the object carrying the sample it was fitted to.</summary>
    private static JgsValue FitOne(
        DistributionClass description, double[] data, ParsedArgs parsed, int line, int col)
    {
        double[]? censoring = parsed.Vector("censoring");
        double[]? frequency = parsed.Vector("frequency");
        DistributionFitting.Sample sample = Guarded("fitdist",
            () => DistributionFitting.MakeSample(data, censoring, frequency), line, col);

        if (sample.Values.Length == 0)
        {
            throw new JgsRuntimeException(line, col, "fitdist: there is nothing left to fit.");
        }

        string kernel = parsed.Word("kernel", "normal", "normal", "box", "triangle", "epanechnikov");
        double[] parameters = description.Shape switch
        {
            DistributionShape.Smoothed => [Bandwidth(parsed, sample.Values), .. sample.Values],
            DistributionShape.Probabilities => Proportions(sample.Values, line, col),
            DistributionShape.Breakpoints => Steps(sample.Values),
            _ => FittedParameters(description, sample, parsed, line, col),
        };

        DistributionFamily family = FamilyFor(description, parameters, kernel, line, col);
        var built = new DistributionObject(family, parameters);
        return DistributionValueOf(description, built, kernel, SampleStruct(sample));
    }

    /// <summary>The maximum-likelihood parameters, through the fitter wave C wrote.</summary>
    private static double[] FittedParameters(
        DistributionClass description, in DistributionFitting.Sample sample,
        ParsedArgs parsed, int line, int col)
    {
        DistributionFamily family = FamilyFor(description, [], "normal", line, col);

        // The binomial's trial count and the generalized Pareto's threshold are known rather than
        // estimated, so they are given to the fitter as pinned values instead of searched for.
        var pinned = new double?[family.ParameterCount];
        if (family.Prefix == "bino")
        {
            pinned[0] = parsed.Scalar("ntrials", 1);
        }
        else if (family.Prefix == "gp")
        {
            pinned[2] = parsed.Scalar("theta", 0);
        }

        bool any = pinned.Any(p => p is not null);
        DistributionFitting.Sample local = sample;
        return Guarded("fitdist", () => any
            ? DistributionFitting.MaximizeGiven(family, local, pinned)
            : DistributionFitting.Fit(family, local, 0.05).Parameters, line, col);
    }

    /// <summary>
    /// A bandwidth for a kernel fit. Left to itself it is Silverman's rule of thumb, which is what
    /// MathWorks uses when the caller says nothing.
    /// </summary>
    private static double Bandwidth(ParsedArgs parsed, double[] values)
    {
        double given = parsed.Scalar("width", double.NaN);
        if (given > 0)
        {
            return given;
        }

        int n = values.Length;
        double mean = values.Average();
        double variance = n > 1 ? values.Sum(v => (v - mean) * (v - mean)) / (n - 1) : 0;
        double deviation = Math.Sqrt(variance);
        double spread = JgsStdlib.Percentile(values, 75) - JgsStdlib.Percentile(values, 25);
        double scale = spread > 0 ? Math.Min(deviation, spread / 1.349) : deviation;
        return scale > 0 ? 1.06 * scale * Math.Pow(n, -0.2) : 1;
    }

    /// <summary>The observed proportion of each category, for a multinomial fit.</summary>
    private static double[] Proportions(double[] values, int line, int col)
    {
        int categories = 0;
        foreach (double value in values)
        {
            if (value != Math.Round(value) || value < 1)
            {
                throw new JgsRuntimeException(line, col,
                    "fitdist: a multinomial is fitted to category numbers, which start at one.");
            }

            categories = Math.Max(categories, (int)value);
        }

        var counts = new double[categories];
        foreach (double value in values)
        {
            counts[(int)value - 1]++;
        }

        return [.. counts.Select(c => c / values.Length)];
    }

    /// <summary>The empirical distribution function, as breakpoints and the heights at them.</summary>
    private static double[] Steps(double[] values)
    {
        double[] sorted = [.. values.Distinct().Order()];
        if (sorted.Length < 2)
        {
            sorted = [sorted[0], sorted[0] + 1];
        }

        var heights = new double[sorted.Length];
        for (int i = 0; i < sorted.Length; i++)
        {
            int below = values.Count(v => v <= sorted[i]);
            heights[i] = i == sorted.Length - 1 ? 1 : below / (double)values.Length;
        }

        heights[0] = 0;
        return [.. sorted, .. heights];
    }

    private static JgsValue SampleStruct(in DistributionFitting.Sample sample) => Structure(
        ("data", ColumnOfAnswers(sample.Values)),
        ("cens", ColumnOfFlags(sample.Censored)),
        ("freq", ColumnOfAnswers(sample.Frequency)));

    /// <summary>The sample an object was fitted to, or an error saying it was not fitted to one.</summary>
    private static DistributionFitting.Sample FittedSample(
        string name, JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.Struct
            && value.AsStruct.TryGetValue("InputData", out JgsValue? input)
            && input.Type == JgsType.Struct
            && input.AsStruct.TryGetValue("data", out JgsValue? data))
        {
            double[] values = Flatten(data);
            if (values.Length > 0)
            {
                double[]? censoring = input.AsStruct.TryGetValue("cens", out JgsValue? cens)
                    ? Flatten(cens) : null;
                double[]? frequency = input.AsStruct.TryGetValue("freq", out JgsValue? freq)
                    ? Flatten(freq) : null;
                return DistributionFitting.MakeSample(
                    values,
                    censoring is { Length: > 0 } ? censoring : null,
                    frequency is { Length: > 0 } ? frequency : null);
            }
        }

        throw new JgsRuntimeException(line, col,
            $"{name} needs a distribution that was fitted to data; this one was made from parameters, "
            + "so there is no likelihood to report.");
    }

    // --- What an object answers ---------------------------------------------------------------------

    /// <summary><c>pdf(pd, x)</c>, <c>cdf(pd, x, 'upper')</c> and <c>icdf(pd, p)</c>.</summary>
    private static JgsValue Elementwise(
        string name, DistributionObject built, IReadOnlyList<JgsValue> args, int at, int line, int col)
    {
        var spec = new OptionSpec(name, name == "cdf" ? ["upper"] : [], []);
        ParsedArgs parsed = spec.Parse(args.Skip(at).ToList(), 1, line, col);
        if (parsed.Positional.Count != 1)
        {
            throw new JgsRuntimeException(line, col, $"{name}(pd, x) takes one distribution and one array.");
        }

        bool upper = parsed.Has("upper");
        (double[][] columns, int[] dims) = AlignArguments(name, [parsed.Positional[0]], line, col);
        var answer = new double[columns[0].Length];
        for (int i = 0; i < answer.Length; i++)
        {
            double x = columns[0][i];
            answer[i] = name switch
            {
                "pdf" => built.Pdf(x),
                "cdf" => upper ? 1 - built.Cdf(x) : built.Cdf(x),
                _ => built.Inv(x),
            };
        }

        return ShapedResult(answer, dims);
    }

    /// <summary><c>random(pd)</c>, <c>random(pd, 3)</c>, <c>random(pd, 2, 5)</c>.</summary>
    private static JgsValue DrawFrom(
        DistributionObject built, Random random, IReadOnlyList<JgsValue> args, int line, int col)
    {
        int rows = args.Count > 1 ? Count("random", args, 1, line, col) : 1;
        int cols = args.Count > 2 ? Count("random", args, 2, line, col) : args.Count > 1 ? rows : 1;
        if (args.Count > 3)
        {
            throw new JgsRuntimeException(line, col, "random(pd, m, n) takes at most two sizes.");
        }

        if (rows == 1 && cols == 1)
        {
            return JgsValue.Number(built.Sample(random));
        }

        var draws = new double[rows * cols];
        for (int i = 0; i < draws.Length; i++)
        {
            draws[i] = built.Sample(random);
        }

        return JgsMatrix.FromColumnMajor(draws, rows, cols);
    }

    /// <summary>The five statistics a distribution object answers about itself.</summary>
    private static JgsValue Moment(
        string name, DistributionObject built, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count != 1)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}(pd) takes a distribution object on its own — the statistic is the distribution's, "
                + "so there is nothing else to say about it.");
        }

        return JgsValue.Number(name switch
        {
            "mean" => built.Mean(),
            "median" => built.Median(),
            "std" => built.Deviation(),
            "var" => built.Variance(),
            _ => built.InterquartileRange(),
        });
    }

    /// <summary><c>truncate(pd, lower, upper)</c>.</summary>
    private static JgsValue TruncateDistribution(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("truncate", args, 3, line, col);
        (DistributionObject built, DistributionClass description) = RequireDistribution("truncate", args, line, col);
        double lower = Num("truncate", args, 1, line, col);
        double upper = Num("truncate", args, 2, line, col);

        if (!(upper > lower))
        {
            throw new JgsRuntimeException(line, col,
                $"truncate(pd, lower, upper) needs an upper limit above its lower one, but was given {lower} and {upper}.");
        }

        DistributionObject truncated = built.Truncate(lower, upper);
        if (!(truncated.Retained > 0))
        {
            throw new JgsRuntimeException(line, col,
                $"truncate: this {description.Name} distribution puts no probability between {lower} and {upper}.");
        }

        return DistributionValueOf(
            description, truncated, KernelOf(args[0]), InputDataOf(args[0]));
    }

    private static string KernelOf(JgsValue value) =>
        value.Type == JgsType.Struct && value.AsStruct.TryGetValue("Kernel", out JgsValue? kernel)
            && kernel.Type == JgsType.String
            ? kernel.AsString
            : "normal";

    private static JgsValue? InputDataOf(JgsValue value) =>
        value.Type == JgsType.Struct && value.AsStruct.TryGetValue("InputData", out JgsValue? input)
            ? input
            : null;

    /// <summary><c>negloglik(pd)</c>.</summary>
    private static JgsValue ObjectNegativeLogLikelihood(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("negloglik", args, 1, line, col);
        (DistributionObject built, _) = RequireDistribution("negloglik", args, line, col);
        DistributionFitting.Sample sample = FittedSample("negloglik", args[0], line, col);
        return JgsValue.Number(built.NegativeLogLikelihood(sample));
    }

    /// <summary>
    /// <c>paramci(pd, 'Alpha', a)</c>. The limits come from the fitter, asked again at the requested
    /// confidence — so a family with an exact interval keeps it, and no second formula exists here to
    /// disagree with the first.
    /// </summary>
    private static JgsValue ObjectParameterInterval(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("paramci", args, 1, 8, line, col);
        (DistributionObject built, DistributionClass description) = RequireDistribution("paramci", args, line, col);
        ParsedArgs parsed = ObjectIntervalOptions.Parse(args.Skip(1).ToList(), 1, line, col);
        double alpha = parsed.Positional.Count > 0
            ? ToDoubles("paramci", parsed.Positional[0], line, col)[0]
            : parsed.Scalar("alpha", 0.05);

        if (!(alpha > 0 && alpha < 1))
        {
            throw new JgsRuntimeException(line, col, "paramci: 'Alpha' lies strictly between zero and one.");
        }

        if (description.Shape != DistributionShape.Scalars)
        {
            throw new JgsRuntimeException(line, col,
                $"paramci: a {description.Name} distribution has no estimated parameters to put an interval around.");
        }

        DistributionFitting.Sample sample = FittedSample("paramci", args[0], line, col);
        DistributionFitting.FitOutcome outcome = Guarded("paramci",
            () => DistributionFitting.Fit(built.Family, sample, alpha), line, col);

        var limits = new double[2 * outcome.Parameters.Length];
        for (int i = 0; i < outcome.Parameters.Length; i++)
        {
            limits[i * 2] = outcome.Lower[i];
            limits[(i * 2) + 1] = outcome.Upper[i];
        }

        return JgsMatrix.FromColumnMajor(limits, 2, outcome.Parameters.Length);
    }

    /// <summary>
    /// <c>proflik(pd, k)</c>: the likelihood as one parameter is walked across a range, with the
    /// others re-maximized at every step. That re-maximization is what separates a profile likelihood
    /// from a slice through the surface, and it is the fitter's own, pinned one slot at a time.
    /// </summary>
    private static JgsValue[] ProfileLikelihood(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("proflik", args, 2, 8, line, col);
        (DistributionObject built, DistributionClass description) = RequireDistribution("proflik", args, line, col);
        int slot = Count("proflik", args, 1, line, col);
        ParsedArgs parsed = ProfileOptions.Parse(args.Skip(2).ToList(), 1, line, col);

        if (description.Shape != DistributionShape.Scalars)
        {
            throw new JgsRuntimeException(line, col,
                $"proflik: a {description.Name} distribution has no estimated parameter to profile.");
        }

        if (slot < 1 || slot > built.Parameters.Length)
        {
            throw new JgsRuntimeException(line, col,
                $"proflik: this distribution has {built.Parameters.Length} parameter(s), so {slot} names none of them.");
        }

        DistributionFitting.Sample sample = FittedSample("proflik", args[0], line, col);
        double estimate = built.Parameters[slot - 1];

        double[] range = parsed.Vector("setrange") ?? Around(estimate, built, sample, slot - 1);
        double from = Math.Min(range[0], range[^1]);
        double to = Math.Max(range[0], range[^1]);

        const int Steps = 41;
        var values = new double[Steps];
        var likelihood = new double[Steps];
        var others = new double[Steps, Math.Max(built.Parameters.Length - 1, 1)];
        var converged = new double[Steps];

        for (int i = 0; i < Steps; i++)
        {
            double at = from + ((to - from) * i / (Steps - 1));
            var pinned = new double?[built.Parameters.Length];
            pinned[slot - 1] = at;

            double[] best = Guarded("proflik",
                () => DistributionFitting.MaximizeGiven(built.Family, sample, pinned), line, col);
            double negative = DistributionFitting.NegativeLogLikelihood(built.Family, best, sample);

            values[i] = at;
            likelihood[i] = -negative;
            converged[i] = double.IsFinite(negative) ? 1 : 0;
            for (int j = 0, k = 0; j < best.Length; j++)
            {
                if (j != slot - 1)
                {
                    others[i, k++] = best[j];
                }
            }
        }

        return Outputs(wanted,
            ColumnOfAnswers(likelihood),
            ColumnOfAnswers(values),
            Rectangle(others),
            ColumnOfAnswers(converged));
    }

    /// <summary>
    /// A range to profile over when the caller names none: wide enough to cross the cut-off a
    /// likelihood-ratio test would use, which is what makes the curve worth looking at.
    /// </summary>
    private static double[] Around(
        double estimate, DistributionObject built, in DistributionFitting.Sample sample, int slot)
    {
        double[,] covariance = DistributionFitting.AsymptoticCovariance(
            built.Family, built.Parameters, sample, null);
        double error = covariance[slot, slot] > 0 ? Math.Sqrt(covariance[slot, slot]) : Math.Abs(estimate) / 4;
        if (!(error > 0))
        {
            error = 1;
        }

        double low = estimate - (3 * error);
        if (built.Family.PositiveParameters[slot] && low <= 0)
        {
            low = estimate / 20;
        }

        return [low, estimate + (3 * error)];
    }

    // --- The class table ------------------------------------------------------------------------------

    private static DistributionClass NamedClass(
        string name, JgsValue value, int line, int col)
    {
        if (value.Type != JgsType.String)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} takes the distribution's name as text, as in {name}(x, 'Weibull').");
        }

        if (DistributionClasses.TryGetValue(
            ContinuousFamilies.Normalize(value.AsString), out DistributionClass? found))
        {
            return found;
        }

        var known = DistinctClasses().Select(c => c.Name).Order(StringComparer.Ordinal);
        throw new JgsRuntimeException(line, col,
            $"{name}: '{value.AsString}' is not a distribution with an object. It knows {string.Join(", ", known)}.");
    }

    /// <summary>Each class once, in the order the table declares them.</summary>
    private static IEnumerable<DistributionClass> DistinctClasses() =>
        DistributionClasses.Values.Distinct().OrderBy(c => c.ClassName, StringComparer.Ordinal);

    /// <summary>A row cell of text, which is the shape MathWorks answers a name list in.</summary>
    private static JgsValue TextRowOf(string[] names)
    {
        var cells = new JgsValue[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            cells[i] = JgsValue.Str(names[i]);
        }

        JgsValue row = JgsValue.Cell(cells);
        if (names.Length > 0)
        {
            row.ReshapeDims([1, names.Length]);
        }

        return row;
    }

    private static string[] LowerAll(string[] names) =>
        [.. names.Select(n => n.ToLowerInvariant())];

    private static Dictionary<string, DistributionClass> BuildDistributionClasses()
    {
        DistributionClass[] classes =
        [
            new("BetaDistribution", "Beta", ["a", "b"], [1, 1],
                ["First shape parameter", "Second shape parameter"], "Beta"),
            new("BinomialDistribution", "Binomial", ["N", "p"], [1, 0.5],
                ["Number of trials", "Probability of success"], "Binomial"),
            new("BirnbaumSaundersDistribution", "BirnbaumSaunders", ["beta", "gamma"], [1, 1],
                ["Scale", "Shape"], "Birnbaum-Saunders"),
            new("BurrDistribution", "Burr", ["alpha", "c", "k"], [1, 1, 1],
                ["Scale", "First shape parameter", "Second shape parameter"], "Burr"),
            new("ExponentialDistribution", "Exponential", ["mu"], [1], ["Mean"], "Exponential"),
            new("ExtremeValueDistribution", "ExtremeValue", ["mu", "sigma"], [0, 1],
                ["Location", "Scale"], "Extreme Value"),
            new("GammaDistribution", "Gamma", ["a", "b"], [1, 1], ["Shape", "Scale"], "Gamma"),
            new("GeneralizedExtremeValueDistribution", "GeneralizedExtremeValue",
                ["k", "sigma", "mu"], [0, 1, 0],
                ["Shape", "Scale", "Location"], "Generalized Extreme Value"),
            new("GeneralizedParetoDistribution", "GeneralizedPareto",
                ["k", "sigma", "theta"], [1, 1, 1],
                ["Shape", "Scale", "Threshold"], "Generalized Pareto"),
            new("HalfNormalDistribution", "HalfNormal", ["mu", "sigma"], [0, 1],
                ["Location", "Scale"], "Half Normal"),
            new("InverseGaussianDistribution", "InverseGaussian", ["mu", "lambda"], [1, 1],
                ["Scale", "Shape"], "Inverse Gaussian"),
            new("KernelDistribution", "Kernel", [], [], [], null,
                DistributionShape.Smoothed, ByName: false),
            new("LogisticDistribution", "Logistic", ["mu", "sigma"], [0, 1],
                ["Location", "Scale"], "Logistic"),
            new("LoglogisticDistribution", "Loglogistic", ["mu", "sigma"], [0, 1],
                ["Log location", "Log scale"], "Log-Logistic"),
            new("LognormalDistribution", "Lognormal", ["mu", "sigma"], [0, 1],
                ["Log mean", "Log standard deviation"], "Lognormal"),
            new("LoguniformDistribution", "Loguniform", ["Lower", "Upper"], [1, 4],
                ["Lower limit", "Upper limit"], "Loguniform"),
            new("MultinomialDistribution", "Multinomial", ["probabilities"], [],
                ["Outcome probabilities"], null, DistributionShape.Probabilities),
            new("NakagamiDistribution", "Nakagami", ["mu", "omega"], [1, 1],
                ["Shape", "Spread"], "Nakagami"),
            new("NegativeBinomialDistribution", "NegativeBinomial", ["R", "p"], [1, 0.5],
                ["Number of successes", "Probability of success"], "Negative Binomial"),
            new("NormalDistribution", "Normal", ["mu", "sigma"], [0, 1],
                ["Mean", "Standard deviation"], "Normal"),
            new("PiecewiseLinearDistribution", "PiecewiseLinear", ["x", "Fx"], [],
                ["Breakpoints", "Cumulative probabilities"], null, DistributionShape.Breakpoints),
            new("PoissonDistribution", "Poisson", ["lambda"], [1], ["Mean"], "Poisson"),
            new("RayleighDistribution", "Rayleigh", ["B"], [1], ["Scale"], "Rayleigh"),
            new("RicianDistribution", "Rician", ["s", "sigma"], [1, 1],
                ["Noncentrality", "Scale"], "Rician"),
            new("StableDistribution", "Stable", ["alpha", "beta", "gam", "delta"], [2, 0, 1, 0],
                ["First shape parameter", "Second shape parameter", "Scale", "Location"], "Stable"),
            new("tLocationScaleDistribution", "tLocationScale", ["mu", "sigma", "nu"], [0, 1, 5],
                ["Location", "Scale", "Degrees of freedom"], "t Location-Scale"),
            new("TriangularDistribution", "Triangular", ["A", "B", "C"], [0, 0.5, 1],
                ["Lower limit", "Peak location", "Upper limit"], "Triangular"),
            new("UniformDistribution", "Uniform", ["Lower", "Upper"], [0, 1],
                ["Lower limit", "Upper limit"], "Uniform"),
            new("WeibullDistribution", "Weibull", ["A", "B"], [1, 1], ["Scale", "Shape"], "Weibull"),
        ];

        var index = new Dictionary<string, DistributionClass>(StringComparer.Ordinal);
        foreach (DistributionClass description in classes)
        {
            index[ContinuousFamilies.Normalize(description.Name)] = description;
            index[ContinuousFamilies.Normalize(description.ClassName)] = description;
            if (description.Family is string family)
            {
                index[ContinuousFamilies.Normalize(family)] = description;
            }
        }

        // The spellings MathWorks accepts beside the class's own name.
        index[ContinuousFamilies.Normalize("gev")] = index[ContinuousFamilies.Normalize("GeneralizedExtremeValue")];
        index[ContinuousFamilies.Normalize("gp")] = index[ContinuousFamilies.Normalize("GeneralizedPareto")];
        index[ContinuousFamilies.Normalize("ev")] = index[ContinuousFamilies.Normalize("ExtremeValue")];
        index[ContinuousFamilies.Normalize("logn")] = index[ContinuousFamilies.Normalize("Lognormal")];
        index[ContinuousFamilies.Normalize("wbl")] = index[ContinuousFamilies.Normalize("Weibull")];
        index[ContinuousFamilies.Normalize("nbin")] = index[ContinuousFamilies.Normalize("NegativeBinomial")];
        index[ContinuousFamilies.Normalize("tls")] = index[ContinuousFamilies.Normalize("tLocationScale")];
        return index;
    }
}
