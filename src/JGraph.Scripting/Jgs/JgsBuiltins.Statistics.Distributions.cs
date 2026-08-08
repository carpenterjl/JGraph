using JGraph.Statistics.Distributions;
using JGraph.Statistics.Optimize;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M53 waves C and D: the probability distributions. Twenty-three families — seventeen continuous and
/// six discrete — each with its density, distribution function, quantile, random draw and moments,
/// plus the fitters, the likelihoods, and the five generic names that take the distribution as a word.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here is written once per family. A family is a record — <see cref="DistributionFamily"/> —
/// and everything on this page works from the record: how many parameters it takes, which of them
/// must stay positive, and the five functions themselves. That is what makes <c>pdf('Weibull', …)</c>
/// and <c>wblpdf(…)</c> literally the same code path rather than two that have to be kept in step.
/// Wave D added the six discrete families by adding six records: not one line of the code below
/// changed to make <c>binopdf</c> and <c>pdf('Poisson', …)</c> work.
/// </para>
/// <para>
/// What the families cannot describe is the <em>call</em> surface, and that lives here: which
/// parameters have documented defaults (a Weibull's scale and shape both default to one; a beta's do
/// not default at all), how many positional arguments each fitter takes, and which of the four shapes
/// a fitter returns its answer in. MathWorks is not uniform about that last one — <c>normfit</c>
/// gives four separate outputs, <c>wblfit</c> one vector and one matrix, <c>expfit</c> a scalar and a
/// pair — so the shape is recorded per name rather than guessed from the parameter count.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Which of the five functions an elementwise distribution builtin is.</summary>
    private enum DistributionPart
    {
        /// <summary>The density.</summary>
        Pdf,

        /// <summary>The distribution function.</summary>
        Cdf,

        /// <summary>The quantile.</summary>
        Inv,
    }

    /// <summary>The shape a fitter returns its estimate and interval in.</summary>
    private enum FitShape
    {
        /// <summary>One row of estimates and a two-row matrix of limits: <c>[phat, pci]</c>.</summary>
        Vector,

        /// <summary>Each parameter and its own interval separately, which is <c>normfit</c>.</summary>
        Split,

        /// <summary>One estimate and its interval, which is <c>expfit</c> and <c>raylfit</c>.</summary>
        Single,
    }

    /// <summary>
    /// Everything about a family that belongs to the way it is <em>called</em> rather than to the
    /// distribution itself.
    /// </summary>
    /// <param name="Family">The distribution.</param>
    /// <param name="Defaults">
    /// Each parameter's documented default, or null where MathWorks requires it to be given.
    /// </param>
    /// <param name="FitName">What the fitter is called, or null where the family has no fitter.</param>
    /// <param name="Shape">The shape that fitter answers in.</param>
    /// <param name="FitPositionalMax">How many positional arguments the fitter documents.</param>
    /// <param name="FittedParameters">
    /// How many of the family's parameters the fitter estimates. The generalized Pareto is the one
    /// family where this is smaller than the parameter count: its threshold is known, not fitted.
    /// </param>
    /// <param name="HasLikelihood">Whether a <c>*like</c> name exists for this family.</param>
    private sealed record DistributionCall(
        DistributionFamily Family,
        double?[] Defaults,
        string? FitName,
        FitShape Shape,
        int FitPositionalMax,
        int FittedParameters,
        bool HasLikelihood);

    /// <summary>The call surface of every family, keyed by its name stem.</summary>
    private static readonly Dictionary<string, DistributionCall> DistributionCalls = BuildDistributionCalls();

    /// <summary>Registers the distribution builtins, continuous and discrete alike.</summary>
    private static void RegisterDistributionBuiltins(JgsEnvironment env, Random random)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        foreach (DistributionCall call in DistributionCalls.Values)
        {
            string stem = call.Family.Prefix;

            Define(stem + "pdf", (args, line, col) =>
                DistributionValue(stem + "pdf", call, DistributionPart.Pdf, args, line, col));
            Define(stem + "cdf", (args, line, col) =>
                DistributionValue(stem + "cdf", call, DistributionPart.Cdf, args, line, col));
            Define(stem + "inv", (args, line, col) =>
                DistributionValue(stem + "inv", call, DistributionPart.Inv, args, line, col));
            Define(stem + "rnd", (args, line, col) =>
                DistributionDraw(stem + "rnd", call, random, args, line, col));

            // Every moment function is its stem with "stat" after it, save one: MathWorks writes
            // poisstat with a single s where poisspdf has two, and a name that is nearly right is a
            // name that does not exist.
            string statName = stem == "poiss" ? "poisstat" : stem + "stat";
            Define(statName,
                (args, line, col) => DistributionMoments(statName, call, args, 1, line, col)[0],
                (args, wanted, line, col) => DistributionMoments(statName, call, args, wanted, line, col));

            if (call.FitName is string fitName)
            {
                Define(fitName,
                    (args, line, col) => DistributionFit(fitName, call, args, 1, line, col)[0],
                    (args, wanted, line, col) => DistributionFit(fitName, call, args, wanted, line, col));
            }

            if (call.HasLikelihood)
            {
                string likeName = stem + "like";
                Define(likeName,
                    (args, line, col) => DistributionLikelihood(likeName, call, args, 1, line, col)[0],
                    (args, wanted, line, col) => DistributionLikelihood(likeName, call, args, wanted, line, col));
            }
        }

        // The generic names take the distribution as a word. They are the same code with one extra
        // argument peeled off the front, which is the point of describing families as records.
        Define("pdf", (args, line, col) => GenericDistribution("pdf", DistributionPart.Pdf, args, line, col));
        Define("cdf", (args, line, col) => GenericDistribution("cdf", DistributionPart.Cdf, args, line, col));
        Define("icdf", (args, line, col) => GenericDistribution("icdf", DistributionPart.Inv, args, line, col));
        Define("random", (args, line, col) => GenericDraw("random", random, args, line, col));

        RegisterDiscreteDistributionBuiltins(env, random);

        Define("mle",
            (args, line, col) => MaximumLikelihood("mle", args, 1, line, col)[0],
            (args, wanted, line, col) => MaximumLikelihood("mle", args, wanted, line, col));
    }

    // --- The elementwise three --------------------------------------------------------------------

    /// <summary>
    /// <c>normpdf(x, mu, sigma)</c> and its ninety-odd siblings: read the argument and the
    /// parameters, expand them against each other, and apply the family's function elementwise.
    /// </summary>
    private static JgsValue DistributionValue(
        string name, DistributionCall call, DistributionPart part,
        IReadOnlyList<JgsValue> args, int line, int col)
    {
        // 'upper' is only documented on the distribution functions, so it is only accepted there —
        // and a script that misspells it on a density is told the word is not an option rather than
        // having it silently ignored.
        string[] flags = part == DistributionPart.Cdf ? ["upper"] : [];
        var spec = new OptionSpec(name, flags, []);
        ParsedArgs parsed = spec.Parse(args, 1 + call.Family.ParameterCount, line, col);

        if (parsed.Positional.Count == 0)
        {
            throw new JgsRuntimeException(line, col, Usage(name, call, part));
        }

        List<JgsValue> pieces = WithDefaults(name, call, parsed.Positional, line, col);
        (double[][] columns, int[] dims) = AlignArguments(name, pieces, line, col);
        if (columns[0].Length == 0)
        {
            return JgsValue.Array([]);
        }

        bool upper = parsed.Has("upper");
        var parameters = new double[call.Family.ParameterCount];
        var answer = new double[columns[0].Length];
        for (int i = 0; i < answer.Length; i++)
        {
            for (int p = 0; p < parameters.Length; p++)
            {
                parameters[p] = columns[p + 1][i];
            }

            answer[i] = part switch
            {
                DistributionPart.Pdf => call.Family.Pdf(columns[0][i], parameters),
                DistributionPart.Cdf => upper
                    ? UpperTail(call.Family, columns[0][i], parameters)
                    : call.Family.Cdf(columns[0][i], parameters),
                _ => call.Family.Inv(columns[0][i], parameters),
            };
        }

        return ShapedResult(answer, dims);
    }

    /// <summary>
    /// The probability of exceeding <paramref name="x"/>. For a symmetric family this is the
    /// distribution function read from the other side, which keeps every significant figure in a tail
    /// that <c>1 − p</c> would round away entirely; elsewhere there is nothing better to do than
    /// subtract.
    /// </summary>
    private static double UpperTail(DistributionFamily family, double x, double[] parameters) =>
        family.Prefix switch
        {
            "norm" => family.Cdf((2 * parameters[0]) - x, parameters),
            "t" => family.Cdf(-x, parameters),

            // Each discrete tail below is the same incomplete integral as its distribution function
            // read from the other end, so it keeps every figure the subtraction would lose.
            "bino" => DiscreteDistributions.BinomialUpper(x, parameters[0], parameters[1]),
            "poiss" => DiscreteDistributions.PoissonUpper(x, parameters[0]),
            "geo" => DiscreteDistributions.GeometricUpper(x, parameters[0]),
            "nbin" => DiscreteDistributions.NegativeBinomialUpper(x, parameters[0], parameters[1]),
            _ => 1 - family.Cdf(x, parameters),
        };

    /// <summary>
    /// Fills the parameters the call left out with their documented defaults, and names the ones that
    /// have none.
    /// </summary>
    private static List<JgsValue> WithDefaults(
        string name, DistributionCall call, IReadOnlyList<JgsValue> positional, int line, int col)
    {
        var pieces = new List<JgsValue>(call.Family.ParameterCount + 1) { positional[0] };
        for (int i = 0; i < call.Family.ParameterCount; i++)
        {
            if (i + 1 < positional.Count)
            {
                pieces.Add(positional[i + 1]);
            }
            else if (call.Defaults[i] is double fallback)
            {
                pieces.Add(JgsValue.Number(fallback));
            }
            else
            {
                throw new JgsRuntimeException(line, col,
                    $"{name} needs {call.Family.ParameterNames[i]}: {name}(x, {string.Join(", ", call.Family.ParameterNames)}).");
            }
        }

        return pieces;
    }

    /// <summary>What a distribution builtin should have been called with.</summary>
    private static string Usage(string name, DistributionCall call, DistributionPart part)
    {
        string first = part == DistributionPart.Inv ? "p" : "x";
        return $"{name}({first}, {string.Join(", ", call.Family.ParameterNames)}) "
            + $"is the {call.Family.Name} distribution.";
    }

    // --- Moments and draws ------------------------------------------------------------------------

    /// <summary><c>[m, v] = normstat(mu, sigma)</c>: the mean and variance, elementwise.</summary>
    private static JgsValue[] DistributionMoments(
        string name, DistributionCall call, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        int n = call.Family.ParameterCount;
        var pieces = new List<JgsValue>(n);
        for (int i = 0; i < n; i++)
        {
            if (i < args.Count)
            {
                pieces.Add(args[i]);
            }
            else if (call.Defaults[i] is double fallback)
            {
                pieces.Add(JgsValue.Number(fallback));
            }
            else
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}({string.Join(", ", call.Family.ParameterNames)}) needs {call.Family.ParameterNames[i]}.");
            }
        }

        if (args.Count > n)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} takes {n} argument{(n == 1 ? "" : "s")}: {name}({string.Join(", ", call.Family.ParameterNames)}).");
        }

        (double[][] columns, int[] dims) = AlignArguments(name, pieces, line, col);
        int length = columns[0].Length;
        var means = new double[length];
        var variances = new double[length];
        var parameters = new double[n];

        for (int i = 0; i < length; i++)
        {
            for (int p = 0; p < n; p++)
            {
                parameters[p] = columns[p][i];
            }

            (means[i], variances[i]) = call.Family.Stat(parameters);
        }

        JgsValue mean = ShapedResult(means, dims);
        return wanted <= 1 ? [mean] : [mean, ShapedResult(variances, dims)];
    }

    /// <summary>
    /// <c>normrnd(mu, sigma, m, n)</c>: draws from the family. Every parameter must be given — none
    /// of the <c>*rnd</c> names documents a default — which is what makes the arguments after them
    /// unambiguously sizes.
    /// </summary>
    private static JgsValue DistributionDraw(
        string name, DistributionCall call, Random random, IReadOnlyList<JgsValue> args, int line, int col)
    {
        int n = call.Family.ParameterCount;
        if (args.Count < n)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}({string.Join(", ", call.Family.ParameterNames)}) needs every parameter; sizes come after them.");
        }

        (double[][] columns, int[] dims) = AlignArguments(name, args.Take(n).ToList(), line, col);

        if (args.Count > n)
        {
            int[] wanted = SquareDims(name, args.Skip(n).ToList(), line, col);
            long total = 1;
            foreach (int dim in wanted)
            {
                total *= dim;
            }

            if (columns[0].Length != 1 && columns[0].Length != total)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: the parameters are a {string.Join("x", dims)} array, which does not fill a {string.Join("x", wanted)} result.");
            }

            var drawn = new double[total];
            var each = new double[n];
            for (long i = 0; i < total; i++)
            {
                for (int p = 0; p < n; p++)
                {
                    each[p] = columns[p][columns[p].Length == 1 ? 0 : (int)i];
                }

                drawn[i] = call.Family.Sample(random, each);
            }

            return total == 1 ? JgsValue.Number(drawn[0]) : JgsMatrix.FromColumnMajorDims(drawn, wanted);
        }

        var answer = new double[columns[0].Length];
        var parameters = new double[n];
        for (int i = 0; i < answer.Length; i++)
        {
            for (int p = 0; p < n; p++)
            {
                parameters[p] = columns[p][i];
            }

            answer[i] = call.Family.Sample(random, parameters);
        }

        return ShapedResult(answer, dims);
    }

    // --- Fitting ----------------------------------------------------------------------------------

    /// <summary>
    /// <c>[phat, pci] = wblfit(x, alpha, censoring, freq)</c> and its siblings. The estimate is one
    /// calculation for every family; only the shape it is handed back in differs.
    /// </summary>
    private static JgsValue[] DistributionFit(
        string name, DistributionCall call, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count == 0 || args.Count > call.FitPositionalMax)
        {
            throw new JgsRuntimeException(line, col, FitUsage(name, call));
        }

        double[] data = ToDoubles(name, args[0], line, col);
        double alpha = OptionalAlpha(name, args, 1, line, col);
        // Only the fitters documenting four positional arguments have a censoring vector and a
        // frequency vector in slots three and four. Where the documented third argument is an options
        // structure instead, reading it as censoring would throw on the struct rather than ignore it.
        bool takesVectors = call.FitPositionalMax >= 4;
        double[]? censoring = takesVectors && args.Count > 2 && !IsPlaceholderValue(args[2])
            ? ToDoubles(name, args[2], line, col)
            : null;
        double[]? frequency = takesVectors && args.Count > 3 && !IsPlaceholderValue(args[3])
            ? ToDoubles(name, args[3], line, col)
            : null;

        DistributionFitting.Sample sample = MakeFitSample(name, data, censoring, frequency, line, col);
        if (sample.Values.Length == 0)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs at least one observation.");
        }

        bool[]? held = call.FittedParameters < call.Family.ParameterCount
            ? HeldTail(call.Family.ParameterCount, call.FittedParameters)
            : null;

        DistributionFitting.FitOutcome fit;
        try
        {
            fit = call.Family.Discrete
                ? FitDiscrete(call.Family, sample, alpha)
                : DistributionFitting.Fit(call.Family, sample, alpha, held);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"{name}: {ex.Message}");
        }

        int reported = call.FittedParameters;
        return call.Shape switch
        {
            FitShape.Single => wanted <= 1
                ? [JgsValue.Number(fit.Parameters[0])]
                : [JgsValue.Number(fit.Parameters[0]), Column(fit.Lower[0], fit.Upper[0])],

            FitShape.Split => SplitFitOutputs(fit, reported, wanted),

            _ => wanted <= 1
                ? [Row(fit.Parameters, reported)]
                : [Row(fit.Parameters, reported), Limits(fit, reported)],
        };
    }

    /// <summary>
    /// The four separate outputs <c>normfit</c> and <c>unifit</c> answer with: each estimate, then
    /// each interval, rather than one vector and one matrix.
    /// </summary>
    private static JgsValue[] SplitFitOutputs(
        DistributionFitting.FitOutcome fit, int reported, int wanted)
    {
        var outputs = new List<JgsValue>(2 * reported);
        for (int i = 0; i < reported; i++)
        {
            outputs.Add(JgsValue.Number(fit.Parameters[i]));
        }

        for (int i = 0; i < reported; i++)
        {
            outputs.Add(Column(fit.Lower[i], fit.Upper[i]));
        }

        return [.. outputs.Take(Math.Max(1, Math.Min(wanted, outputs.Count)))];
    }

    /// <summary>
    /// <c>[nlogL, avar] = wbllike(params, data)</c>: how unlikely the data is under the parameters
    /// given, and how precisely those parameters could have been estimated from it.
    /// </summary>
    private static JgsValue[] DistributionLikelihood(
        string name, DistributionCall call, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count is < 2 or > 4)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}(params, data) needs the parameters [{string.Join(" ", call.Family.ParameterNames)}] and the data, and takes censoring and frequency after them.");
        }

        double[] given = ToDoubles(name, args[0], line, col);
        var parameters = new double[call.Family.ParameterCount];
        for (int i = 0; i < parameters.Length; i++)
        {
            if (i < given.Length)
            {
                parameters[i] = given[i];
            }
            else if (call.Defaults[i] is double fallback)
            {
                parameters[i] = fallback;
            }
            else
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: params must hold {call.Family.ParameterCount} value(s) — [{string.Join(" ", call.Family.ParameterNames)}].");
            }
        }

        double[] data = ToDoubles(name, args[1], line, col);
        double[]? censoring = args.Count > 2 && !IsPlaceholderValue(args[2])
            ? ToDoubles(name, args[2], line, col)
            : null;
        double[]? frequency = args.Count > 3 && !IsPlaceholderValue(args[3])
            ? ToDoubles(name, args[3], line, col)
            : null;

        DistributionFitting.Sample sample = MakeFitSample(name, data, censoring, frequency, line, col);
        double negative = DistributionFitting.NegativeLogLikelihood(call.Family, parameters, sample);
        if (wanted <= 1)
        {
            return [JgsValue.Number(negative)];
        }

        bool[]? held = call.FittedParameters < call.Family.ParameterCount
            ? HeldTail(call.Family.ParameterCount, call.FittedParameters)
            : null;
        double[,] covariance = DistributionFitting.AsymptoticCovariance(call.Family, parameters, sample, held);

        int reported = call.FittedParameters;
        var flat = new double[reported * reported];
        for (int columnIndex = 0; columnIndex < reported; columnIndex++)
        {
            for (int rowIndex = 0; rowIndex < reported; rowIndex++)
            {
                flat[(columnIndex * reported) + rowIndex] = covariance[rowIndex, columnIndex];
            }
        }

        return [JgsValue.Number(negative), JgsMatrix.FromColumnMajor(flat, reported, reported)];
    }

    /// <summary>
    /// <c>mle(data)</c>: the maximum likelihood fit, of a named family or of a density the caller
    /// supplies as a function.
    /// </summary>
    private static JgsValue[] MaximumLikelihood(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        var spec = new OptionSpec(name,
            [],
            ["distribution", "alpha", "censoring", "frequency", "pdf", "logpdf", "start", "options", "ntrials"]);
        ParsedArgs parsed = spec.Parse(args, 1, line, col);

        if (parsed.Positional.Count != 1)
        {
            throw new JgsRuntimeException(line, col,
                "mle(data) fits a normal; mle(data, 'distribution', name) fits a named family; "
                + "mle(data, 'pdf', @f, 'start', p0) fits a density you supply.");
        }

        double[] data = ToDoubles(name, parsed.Positional[0], line, col);
        double alpha = parsed.Scalar("alpha", 0.05);
        double[]? censoring = parsed.Vector("censoring");
        double[]? frequency = parsed.Vector("frequency");
        DistributionFitting.Sample sample = MakeFitSample(name, data, censoring, frequency, line, col);

        JgsValue? density = parsed.Named("pdf") ?? parsed.Named("logpdf");
        if (density is not null)
        {
            bool logged = parsed.Named("pdf") is null;
            return CustomLikelihood(name, density, logged, parsed.Named("start"), sample, alpha, wanted, line, col);
        }

        string family = parsed.Text("distribution") ?? "Normal";
        if (!DistributionCalls.TryGetValue(ContinuousFamilies.Normalize(family), out DistributionCall? call))
        {
            throw new JgsRuntimeException(line, col, UnknownFamily(name, family));
        }

        bool[]? held = call.FittedParameters < call.Family.ParameterCount
            ? HeldTail(call.Family.ParameterCount, call.FittedParameters)
            : null;
        DistributionFitting.FitOutcome fit = call.Family.Discrete
            ? FitDiscreteForLikelihood(name, call.Family, sample, alpha, parsed.Named("ntrials"), line, col)
            : DistributionFitting.Fit(call.Family, sample, alpha, held);

        int reported = call.FittedParameters;
        return wanted <= 1
            ? [Row(fit.Parameters, reported)]
            : [Row(fit.Parameters, reported), Limits(fit, reported)];
    }

    /// <summary>
    /// The <c>mle</c> form that takes a density rather than a family name. The function is called with
    /// the whole data vector and the parameters as scalars, which is the shape MathWorks documents
    /// and the shape that lets a vectorized density be written naturally.
    /// </summary>
    private static JgsValue[] CustomLikelihood(
        string name, JgsValue density, bool logged, JgsValue? start,
        DistributionFitting.Sample sample, double alpha, int wanted, int line, int col)
    {
        if (density.Type != JgsType.Function)
        {
            throw new JgsRuntimeException(line, col, $"{name}: 'pdf' and 'logpdf' take a function handle.");
        }

        if (start is null)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: a density of your own needs 'start' — there is no way to guess how many parameters it takes.");
        }

        double[] initial = ToDoubles(name, start, line, col);
        if (initial.Length == 0)
        {
            throw new JgsRuntimeException(line, col, $"{name}: 'start' must hold at least one parameter.");
        }

        JgsValue observations = JgsMatrix.FromColumnMajor(sample.Values, sample.Values.Length, 1);
        IJgsCallable callable = density.AsCallable;

        double Objective(double[] parameters)
        {
            var call = new JgsValue[parameters.Length + 1];
            call[0] = observations;
            for (int i = 0; i < parameters.Length; i++)
            {
                call[i + 1] = JgsValue.Number(parameters[i]);
            }

            double[] answered = ToDoubles(name, callable.Call(call, line, col), line, col);
            if (answered.Length != sample.Values.Length && answered.Length != 1)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: the density answered {answered.Length} value(s) for {sample.Values.Length} observation(s).");
            }

            double total = 0;
            for (int i = 0; i < sample.Values.Length; i++)
            {
                double each = answered[answered.Length == 1 ? 0 : i];
                double contribution = logged ? each : (each > 0 ? Math.Log(each) : double.NegativeInfinity);
                if (double.IsNaN(contribution) || double.IsNegativeInfinity(contribution))
                {
                    return double.PositiveInfinity;
                }

                total += sample.Frequency[i] * contribution;
            }

            return -total;
        }

        NelderMead.Result result = NelderMead.Minimize(
            Objective, initial,
            new NelderMead.Settings(MaxIterations: 2000, MaxEvaluations: 4000));

        JgsValue estimate = Row(result.Solution, result.Solution.Length);
        if (wanted <= 1)
        {
            return [estimate];
        }

        // The interval comes from the curvature of the likelihood the caller supplied, differenced
        // numerically — the same observed-information argument the named families use, applied to a
        // density this build has never seen.
        double[,] covariance = DistributionFitting.ObservedCovariance(Objective, result.Solution);
        double z = ContinuousDistributions.NormalInv(1 - (alpha / 2), 0, 1);

        var limits = new double[2 * result.Solution.Length];
        for (int i = 0; i < result.Solution.Length; i++)
        {
            double variance = covariance[i, i];
            double half = variance > 0 ? z * Math.Sqrt(variance) : double.NaN;
            limits[i * 2] = result.Solution[i] - half;
            limits[(i * 2) + 1] = result.Solution[i] + half;
        }

        return [estimate, JgsMatrix.FromColumnMajor(limits, 2, result.Solution.Length)];
    }

    // --- The generic names ------------------------------------------------------------------------

    /// <summary><c>pdf('Normal', x, 0, 1)</c> and its two siblings.</summary>
    private static JgsValue GenericDistribution(
        string name, DistributionPart part, IReadOnlyList<JgsValue> args, int line, int col)
    {
        DistributionCall call = NamedFamily(name, args, line, col);
        return DistributionValue(name, call, part, args.Skip(1).ToList(), line, col);
    }

    /// <summary><c>random('Weibull', a, b, 3, 4)</c>.</summary>
    private static JgsValue GenericDraw(
        string name, Random random, IReadOnlyList<JgsValue> args, int line, int col)
    {
        DistributionCall call = NamedFamily(name, args, line, col);
        return DistributionDraw(name, call, random, args.Skip(1).ToList(), line, col);
    }

    /// <summary>Reads the leading distribution name of a generic call.</summary>
    private static DistributionCall NamedFamily(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0 || args[0].Type != JgsType.String)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}(name, …) takes the distribution's name first, as in {name}('Normal', x, 0, 1).");
        }

        string wanted = args[0].AsString;
        if (!DistributionCalls.TryGetValue(ContinuousFamilies.Normalize(wanted), out DistributionCall? call))
        {
            throw new JgsRuntimeException(line, col, UnknownFamily(name, wanted));
        }

        return call;
    }

    /// <summary>What to say when a distribution name is not one this toolbox knows.</summary>
    private static string UnknownFamily(string name, string wanted)
    {
        var known = ContinuousFamilies.All.Concat(DiscreteFamilies.All)
            .Select(f => f.Name)
            .Order(StringComparer.Ordinal);
        return $"{name}: '{wanted}' is not a distribution this build knows. It knows {string.Join(", ", known)}.";
    }

    // --- Shared argument machinery ----------------------------------------------------------------

    /// <summary>
    /// Expands several arguments against each other under MATLAB's singleton rule and returns each
    /// one flattened to the common size, so an elementwise distribution function can walk them in
    /// step.
    /// </summary>
    /// <remarks>
    /// This does the same alignment as <see cref="JgsBroadcast"/>, but for any number of operands at
    /// once and answering plain doubles. Folding the pairwise version would have worked for the shape
    /// and not for the values: a density of three arguments is not a composition of two-argument
    /// densities, so all three have to arrive together.
    /// </remarks>
    private static (double[][] Columns, int[] Dims) AlignArguments(
        string name, IReadOnlyList<JgsValue> values, int line, int col)
    {
        int count = values.Count;
        var flats = new double[count][];
        var sizes = new int[count][];
        int rank = 2;

        for (int i = 0; i < count; i++)
        {
            flats[i] = ToDoubles(name, values[i], line, col);
            sizes[i] = JgsMatrix.DimsOf(values[i]);

            // A plain number is not an array and carries no dimensions of its own, so it reads here as
            // the one-by-one it behaves like. Anything whose recorded size does not account for its
            // storage is read as a row of that length, which is what an unshaped list is.
            long recorded = 1;
            foreach (int dim in sizes[i])
            {
                recorded *= dim;
            }

            if (recorded != flats[i].Length)
            {
                sizes[i] = [1, flats[i].Length];
            }

            rank = Math.Max(rank, sizes[i].Length);
        }

        var outDims = new int[rank];
        var padded = new int[count][];
        for (int i = 0; i < count; i++)
        {
            padded[i] = new int[rank];
            for (int d = 0; d < rank; d++)
            {
                padded[i][d] = d < sizes[i].Length ? sizes[i][d] : 1;
            }
        }

        for (int d = 0; d < rank; d++)
        {
            int size = 1;
            for (int i = 0; i < count; i++)
            {
                if (padded[i][d] == 1)
                {
                    continue;
                }

                if (size != 1 && size != padded[i][d])
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name}: the arguments are not the same size — a {string.Join("x", sizes[0])} array and a {string.Join("x", sizes[i])} array.");
                }

                size = padded[i][d];
            }

            outDims[d] = size;
        }

        long total = 1;
        foreach (int dim in outDims)
        {
            total *= dim;
        }

        var strides = new long[count][];
        for (int i = 0; i < count; i++)
        {
            strides[i] = new long[rank];
            long run = 1;
            for (int d = 0; d < rank; d++)
            {
                strides[i][d] = padded[i][d] == 1 ? 0 : run;
                run *= padded[i][d];
            }
        }

        var columns = new double[count][];
        for (int i = 0; i < count; i++)
        {
            columns[i] = new double[total];
        }

        var counter = new int[rank];
        for (long n = 0; n < total; n++)
        {
            for (int i = 0; i < count; i++)
            {
                long slot = 0;
                for (int d = 0; d < rank; d++)
                {
                    slot += counter[d] * strides[i][d];
                }

                columns[i][n] = flats[i].Length == 0 ? double.NaN : flats[i][(int)slot];
            }

            for (int d = 0; d < rank; d++)
            {
                if (++counter[d] < outDims[d])
                {
                    break;
                }

                counter[d] = 0;
            }
        }

        return (columns, outDims);
    }

    /// <summary>A result of the given shape, collapsed to a plain number when there is only one.</summary>
    private static JgsValue ShapedResult(double[] flat, int[] dims)
    {
        if (flat.Length == 0)
        {
            return JgsValue.Array([]);
        }

        return flat.Length == 1 ? JgsValue.Number(flat[0]) : JgsMatrix.FromColumnMajorDims(flat, dims);
    }

    /// <summary>The confidence level a fitter was asked for, defaulting to the usual five percent.</summary>
    private static double OptionalAlpha(
        string name, IReadOnlyList<JgsValue> args, int slot, int line, int col)
    {
        if (args.Count <= slot || IsPlaceholderValue(args[slot]))
        {
            return 0.05;
        }

        double[] given = ToDoubles(name, args[slot], line, col);
        if (given.Length != 1 || !(given[0] > 0) || !(given[0] < 1))
        {
            throw new JgsRuntimeException(line, col, $"{name}: alpha must be one number between 0 and 1.");
        }

        return given[0];
    }

    /// <summary>Builds the fitting sample, turning a length mismatch into a script-level error.</summary>
    private static DistributionFitting.Sample MakeFitSample(
        string name, double[] data, double[]? censoring, double[]? frequency, int line, int col)
    {
        try
        {
            return DistributionFitting.MakeSample(data, censoring, frequency);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"{name}: {ex.Message}");
        }
    }

    /// <summary>
    /// A mask holding every parameter past the first <paramref name="fitted"/> of them — the shape the
    /// generalized Pareto needs, whose threshold is known rather than estimated.
    /// </summary>
    private static bool[] HeldTail(int count, int fitted)
    {
        var held = new bool[count];
        for (int i = fitted; i < count; i++)
        {
            held[i] = true;
        }

        return held;
    }

    /// <summary>The first <paramref name="count"/> estimates as a row.</summary>
    private static JgsValue Row(double[] values, int count) =>
        count == 1 ? JgsValue.Number(values[0]) : JgsMatrix.FromColumnMajor(values[..count], 1, count);

    /// <summary>One parameter's interval, as the column MATLAB answers with.</summary>
    private static JgsValue Column(double lower, double upper) =>
        JgsMatrix.FromColumnMajor([lower, upper], 2, 1);

    /// <summary>Every parameter's interval, lower limits on the first row and upper on the second.</summary>
    private static JgsValue Limits(DistributionFitting.FitOutcome fit, int count)
    {
        var flat = new double[2 * count];
        for (int i = 0; i < count; i++)
        {
            flat[i * 2] = fit.Lower[i];
            flat[(i * 2) + 1] = fit.Upper[i];
        }

        return JgsMatrix.FromColumnMajor(flat, 2, count);
    }

    /// <summary>What a fitter should have been called with.</summary>
    private static string FitUsage(string name, DistributionCall call)
    {
        string tail = call.FitPositionalMax switch
        {
            >= 5 => "x, alpha, censoring, freq, options",
            4 => "x, alpha, censoring, freq",
            3 => "x, alpha, options",
            _ => "x, alpha",
        };

        return $"{name}({tail}) fits a {call.Family.Name} distribution by maximum likelihood.";
    }

    /// <summary>The call surface of each family, as MathWorks documents it name by name.</summary>
    private static Dictionary<string, DistributionCall> BuildDistributionCalls()
    {
        // (stem, defaults, fit name, fit shape, positional arguments the fitter takes, parameters it
        // estimates, whether a *like name exists). A null default is a parameter with no documented
        // default, which the call has to supply.
        (string Stem, double?[] Defaults, string? Fit, FitShape Shape, int Max, int Fitted, bool Like)[] rows =
        [
            ("norm", [0, 1], "normfit", FitShape.Split, 5, 2, true),
            ("exp", [1], "expfit", FitShape.Single, 4, 1, true),
            ("gam", [null, 1], "gamfit", FitShape.Vector, 5, 2, true),
            ("beta", [null, null], "betafit", FitShape.Vector, 2, 2, true),
            ("chi2", [null], null, FitShape.Vector, 0, 1, false),
            ("t", [null], null, FitShape.Vector, 0, 1, false),
            ("f", [null, null], null, FitShape.Vector, 0, 2, false),
            ("unif", [0, 1], "unifit", FitShape.Split, 2, 2, false),
            ("logn", [0, 1], "lognfit", FitShape.Vector, 5, 2, true),
            ("wbl", [1, 1], "wblfit", FitShape.Vector, 5, 2, true),
            ("ev", [0, 1], "evfit", FitShape.Vector, 5, 2, true),
            ("gev", [null, null, null], "gevfit", FitShape.Vector, 3, 3, true),
            ("gp", [null, 1, 0], "gpfit", FitShape.Vector, 3, 2, true),
            ("rayl", [1], "raylfit", FitShape.Single, 2, 1, false),
            ("ncx2", [null, null], null, FitShape.Vector, 0, 2, false),
            ("ncf", [null, null, null], null, FitShape.Vector, 0, 3, false),
            ("nct", [null, null], null, FitShape.Vector, 0, 2, false),

            // The discrete families. None of them documents a *like name, and only three are fitted:
            // binofit is written out separately because it alone takes its trial count as a second
            // vector of data rather than as an option.
            ("bino", [null, null], null, FitShape.Vector, 0, 1, false),
            ("poiss", [null], "poissfit", FitShape.Single, 2, 1, false),
            ("geo", [null], null, FitShape.Vector, 0, 1, false),
            ("hyge", [null, null, null], null, FitShape.Vector, 0, 3, false),
            ("nbin", [null, null], "nbinfit", FitShape.Vector, 3, 2, false),
            ("unid", [null], null, FitShape.Vector, 0, 1, false),
        ];

        var calls = new Dictionary<string, DistributionCall>(StringComparer.Ordinal);
        foreach ((string stem, double?[] defaults, string? fit, FitShape shape, int max, int fitted, bool like) in rows)
        {
            DistributionFamily family = ContinuousFamilies.Find(stem) ?? DiscreteFamilies.Find(stem)
                ?? throw new InvalidOperationException($"No distribution family is registered for '{stem}'.");
            var call = new DistributionCall(family, defaults, fit, shape, max, fitted, like);

            calls[stem] = call;
            foreach (string alias in family.Aliases)
            {
                calls[ContinuousFamilies.Normalize(alias)] = call;
            }

            calls[ContinuousFamilies.Normalize(family.Name)] = call;
        }

        return calls;
    }
}
