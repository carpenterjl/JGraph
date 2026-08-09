using JGraph.Statistics;
using JGraph.Statistics.Cluster;
using JGraph.Statistics.Distributions;
using JGraph.Statistics.Multivariate;
using JGraph.Statistics.Sampling;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M53 wave J, the dependence and simulation half: the copulas, the two distributions described by
/// their own moments or quantiles, the two Markov chains, the piecewise distribution with fitted
/// tails, the two neighbourhood searchers, the covariance of a maximum likelihood estimate, and the
/// stochastic embedding.
/// </summary>
/// <remarks>
/// What these have in common is that each takes something other than plain data: a copula takes
/// probabilities, the chains take a density as a function, the searchers take a set of points to be
/// asked about later, and the moment-matching pair take a description of a distribution that has no
/// name.
/// </remarks>
internal static partial class JgsBuiltins
{
    private static readonly OptionSpec CopulaFitOptions = new(
        "copulafit", [], ["alpha", "options", "method", "lower", "upper", "df"], StringPositionals: 1);

    private static readonly OptionSpec ChainOptions = new(
        "mhsample",
        [],
        ["pdf", "logpdf", "proppdf", "logproppdf", "proprnd", "symmetric", "burnin", "thin", "nchain"]);

    private static readonly OptionSpec SliceOptions = new(
        "slicesample", [], ["pdf", "logpdf", "width", "burnin", "thin"]);

    private static readonly OptionSpec SearcherOptions = new(
        "createns", [], ["nsmethod", "distance", "p", "cov", "scale", "bucketsize"]);

    private static readonly OptionSpec EmbeddingOptions = new(
        "tsne",
        [],
        [
            "algorithm", "distance", "numdimensions", "numpcacomponents", "initialy", "perplexity",
            "exaggeration", "learnrate", "numprint", "options", "standardize", "theta", "verbose",
        ]);

    /// <summary>Registers the copulas, the samplers, the searchers and the embedding.</summary>
    private static void RegisterCopulaBuiltins(JgsEnvironment env, Random random)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        void DefineBoth(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> both) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, (args, line, col) => both(args, 1, line, col)[0]) { MultiOutput = both }));

        Define("copulacdf", (args, line, col) => CopulaCurve("copulacdf", args, line, col));
        Define("copulapdf", (args, line, col) => CopulaCurve("copulapdf", args, line, col));
        Define("copularnd", (args, line, col) => CopulaSample(random, args, line, col));
        Define("copulastat", CopulaStatistic);
        Define("copulaparam", CopulaParameter);
        DefineBoth("copulafit", (args, wanted, line, col) => CopulaFit(args, wanted, line, col));

        DefineBoth("johnsrnd", (args, wanted, line, col) => JohnsonDraw(random, args, wanted, line, col));
        DefineBoth("pearsrnd", (args, wanted, line, col) => PearsonDraw(random, args, wanted, line, col));
        DefineBoth("mhsample", (args, wanted, line, col) => MetropolisDraw(random, args, wanted, line, col));
        DefineBoth("slicesample", (args, wanted, line, col) => SliceDraw(random, args, wanted, line, col));

        Define("mlecov", LikelihoodCovarianceOf);
        Define("paretotails", TailedDistribution);

        Define("createns", (args, line, col) => Searcher("createns", args, line, col));
        Define("ExhaustiveSearcher", (args, line, col) => Searcher("ExhaustiveSearcher", args, line, col));
        Define("KDTreeSearcher", (args, line, col) => Searcher("KDTreeSearcher", args, line, col));

        DefineBoth("tsne", (args, wanted, line, col) => Embed(random, args, wanted, line, col));
    }

    // --- Copulas -------------------------------------------------------------------------------------

    /// <summary>The family a copula name was asked for.</summary>
    private static Copulas.Family CopulaFamily(string name, JgsValue value, int line, int col)
    {
        if (value.Type != JgsType.String)
        {
            throw new JgsRuntimeException(line, col, $"{name}: the first argument names the copula family.");
        }

        return value.AsString.ToLowerInvariant() switch
        {
            "gaussian" or "normal" => Copulas.Family.Gaussian,
            "t" => Copulas.Family.T,
            "clayton" => Copulas.Family.Clayton,
            "frank" => Copulas.Family.Frank,
            "gumbel" => Copulas.Family.Gumbel,
            _ => throw new JgsRuntimeException(line, col,
                $"{name}: '{value.AsString}' is not a copula family. The families are 'Gaussian', 't', "
                + "'Clayton', 'Frank' and 'Gumbel'."),
        };
    }

    /// <summary><c>copulacdf(family, U, …)</c> and <c>copulapdf(family, U, …)</c>, one row per point.</summary>
    private static JgsValue CopulaCurve(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange(name, args, 3, 4, line, col);
        Copulas.Family family = CopulaFamily(name, args[0], line, col);
        (double[][] points, int width) = Observations(name, args[1], line, col);
        bool cdf = name == "copulacdf";

        var answers = new double[points.Length];
        if (Copulas.IsArchimedean(family))
        {
            double alpha = CopulaAlpha(name, family, args[2], line, col);
            if (width != 2)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: an Archimedean copula is bivariate, so U needs exactly two columns.");
            }

            for (int i = 0; i < points.Length; i++)
            {
                answers[i] = cdf
                    ? Copulas.ArchimedeanCdf(family, points[i][0], points[i][1], alpha)
                    : Copulas.ArchimedeanPdf(family, points[i][0], points[i][1], alpha);
            }
        }
        else
        {
            double[,] correlation = CopulaCorrelation(name, args[2], width, line, col);
            double? df = family == Copulas.Family.T
                ? DegreesOfFreedomArgument(name, args, 3, line, col)
                : null;

            for (int i = 0; i < points.Length; i++)
            {
                answers[i] = cdf
                    ? Copulas.EllipticalCdf(points[i], correlation, df)
                    : Copulas.EllipticalPdf(points[i], correlation, df);
            }
        }

        return JgsMatrix.FromColumnMajor(answers, answers.Length, 1);
    }

    /// <summary><c>U = copularnd(family, param, n)</c>: n draws, one per row.</summary>
    private static JgsValue CopulaSample(Random random, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("copularnd", args, 3, 4, line, col);
        Copulas.Family family = CopulaFamily("copularnd", args[0], line, col);

        if (Copulas.IsArchimedean(family))
        {
            double alpha = CopulaAlpha("copularnd", family, args[1], line, col);
            int count = Count("copularnd", args, 2, line, col);
            var flat = new double[count * 2];
            for (int i = 0; i < count; i++)
            {
                (double u, double v) = Copulas.ArchimedeanSample(random, family, alpha);
                flat[i] = u;
                flat[count + i] = v;
            }

            return JgsMatrix.FromColumnMajor(flat, count, 2);
        }

        double[,] correlation = CopulaCorrelation("copularnd", args[1], 0, line, col);
        int width = correlation.GetLength(0);
        double? df = null;
        int countIndex = 2;
        if (family == Copulas.Family.T)
        {
            df = DegreesOfFreedomArgument("copularnd", args, 2, line, col);
            countIndex = 3;
        }

        if (args.Count <= countIndex)
        {
            throw new JgsRuntimeException(line, col, "copularnd needs the number of draws.");
        }

        int rows = Count("copularnd", args, countIndex, line, col);
        (double[,] Factor, int Rank)? factor = Multivariate.CovarianceFactor(correlation);
        if (factor is null)
        {
            throw new JgsRuntimeException(line, col,
                "copularnd: the correlation matrix is not positive semidefinite, so nothing has that dependence.");
        }

        var draws = new double[rows * width];
        for (int i = 0; i < rows; i++)
        {
            double[] u = Copulas.EllipticalSample(random, factor.Value.Factor, df);
            for (int j = 0; j < width; j++)
            {
                draws[i + (j * rows)] = u[j];
            }
        }

        return JgsMatrix.FromColumnMajor(draws, rows, width);
    }

    /// <summary><c>r = copulastat(family, param)</c>: the rank correlation the parameter produces.</summary>
    private static JgsValue CopulaStatistic(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "copulastat needs a family and its parameter.");
        }

        Copulas.Family family = CopulaFamily("copulastat", args[0], line, col);
        bool spearman = RankKind("copulastat", args, line, col);

        if (Copulas.IsArchimedean(family))
        {
            double alpha = CopulaAlpha("copulastat", family, args[1], line, col);
            return JgsValue.Number(spearman
                ? Copulas.SpearmanRho(family, alpha)
                : Copulas.KendallTau(family, alpha));
        }

        // A single correlation answers a single rank correlation: expanding it into the two-by-two
        // matrix it stands for would answer with a matrix nobody asked for.
        if (args[1].Type is JgsType.Number or JgsType.Bool)
        {
            double r = args[1].AsNumber;
            return JgsValue.Number(spearman
                ? Copulas.SpearmanRho(family, r)
                : Copulas.KendallTau(family, r));
        }

        double[,] correlation = CopulaCorrelation("copulastat", args[1], 0, line, col);
        int n = correlation.GetLength(0);
        var answer = new double[n * n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                answer[i + (j * n)] = i == j
                    ? 1
                    : spearman
                        ? Copulas.SpearmanRho(family, correlation[i, j])
                        : Copulas.KendallTau(family, correlation[i, j]);
            }
        }

        return n == 1 ? JgsValue.Number(answer[0]) : JgsMatrix.FromColumnMajor(answer, n, n);
    }

    /// <summary><c>param = copulaparam(family, r)</c>: the parameter that produces a rank correlation.</summary>
    private static JgsValue CopulaParameter(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "copulaparam needs a family and a rank correlation.");
        }

        Copulas.Family family = CopulaFamily("copulaparam", args[0], line, col);
        bool spearman = RankKind("copulaparam", args, line, col);

        if (Copulas.IsArchimedean(family))
        {
            double target = Num("copulaparam", args, 1, line, col);
            RefuseImpossibleRank("copulaparam", family, target, line, col);
            return JgsValue.Number(Copulas.ParameterFor(family, target, spearman));
        }

        (double[] flat, int rows, int columns) = DenseMatrix("copulaparam", args[1], line, col);
        var answer = new double[flat.Length];
        for (int i = 0; i < flat.Length; i++)
        {
            answer[i] = rows == columns && i % (rows + 1) == 0 && rows > 1
                ? 1
                : Copulas.ParameterFor(family, flat[i], spearman);
        }

        return flat.Length == 1
            ? JgsValue.Number(answer[0])
            : JgsMatrix.FromColumnMajor(answer, rows, columns);
    }

    /// <summary>
    /// <c>[param, nu] = copulafit(family, U)</c>: the parameter that makes a set of observed
    /// probabilities most likely.
    /// </summary>
    private static JgsValue[] CopulaFit(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "copulafit needs a family and the data to fit it to.");
        }

        ParsedArgs parsed = CopulaFitOptions.Parse(args, 2, line, col);
        Copulas.Family family = CopulaFamily("copulafit", parsed.Positional[0], line, col);
        (double[] flat, int rows, int columns) = DenseMatrix("copulafit", parsed.Positional[1], line, col);

        foreach (double u in flat)
        {
            if (!(u > 0 && u < 1))
            {
                throw new JgsRuntimeException(line, col,
                    "copulafit: every value is a probability, strictly between zero and one.");
            }
        }

        if (Copulas.IsArchimedean(family))
        {
            if (columns != 2)
            {
                throw new JgsRuntimeException(line, col,
                    "copulafit: an Archimedean copula is bivariate, so U needs exactly two columns.");
            }

            double[] left = flat[..rows];
            double[] right = flat[rows..];
            double alpha = Guarded("copulafit", () => Copulas.FitArchimedean(family, left, right), line, col);
            return Outputs(wanted, JgsValue.Number(alpha), JgsValue.Array([]));
        }

        var u2 = new double[rows, columns];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                u2[r, c] = flat[r + (c * rows)];
            }
        }

        double? df = null;
        if (family == Copulas.Family.T)
        {
            df = parsed.Named("df") is { } given
                ? NumOf("copulafit", given, line, col)
                : Guarded("copulafit", () => Copulas.FitDegreesOfFreedom(u2), line, col);
        }

        double[,] correlation = Guarded("copulafit", () => Copulas.FitElliptical(u2, df), line, col);
        return Outputs(
            wanted,
            Rectangle(correlation),
            df is { } freedom ? JgsValue.Number(freedom) : JgsValue.Array([]));
    }

    /// <summary>Whether a rank correlation was asked for as Kendall's, which is the default, or Spearman's.</summary>
    private static bool RankKind(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        for (int i = 2; i + 1 < args.Count; i++)
        {
            if (args[i].Type == JgsType.String
                && string.Equals(args[i].AsString, "type", StringComparison.OrdinalIgnoreCase))
            {
                string word = Str(name, args, i + 1, line, col).ToLowerInvariant();
                return word switch
                {
                    "spearman" => true,
                    "kendall" => false,
                    _ => throw new JgsRuntimeException(line, col,
                        $"{name}: 'type' is 'Kendall' or 'Spearman'."),
                };
            }
        }

        return false;
    }

    private static void RefuseImpossibleRank(
        string name, Copulas.Family family, double target, int line, int col)
    {
        bool possible = family switch
        {
            Copulas.Family.Clayton or Copulas.Family.Gumbel => target is >= 0 and < 1,
            _ => target is > -1 and < 1,
        };

        if (!possible)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: no {family} copula has a rank correlation of {target}. Clayton and Gumbel "
                + "describe positive dependence only.");
        }
    }

    /// <summary>The one number an Archimedean family is parameterized by, checked against its range.</summary>
    private static double CopulaAlpha(
        string name, Copulas.Family family, JgsValue value, int line, int col)
    {
        double alpha = NumOf(name, value, line, col);
        (double low, double high) = Copulas.ParameterRange(family);
        if (!(alpha >= low && alpha <= high))
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: a {family} copula's parameter lies between {low} and {high}, but {alpha} was given.");
        }

        return alpha;
    }

    /// <summary>
    /// A correlation matrix argument, which may be written as a single number when there are two
    /// variables — that being the whole of a two-by-two correlation matrix that is not already one.
    /// </summary>
    private static double[,] CopulaCorrelation(
        string name, JgsValue value, int width, int line, int col)
    {
        if (value.Type is JgsType.Number or JgsType.Bool)
        {
            double r = value.AsNumber;
            if (!(r is >= -1 and <= 1))
            {
                throw new JgsRuntimeException(line, col, $"{name}: a correlation lies between -1 and 1.");
            }

            return new[,] { { 1.0, r }, { r, 1.0 } };
        }

        double[,] matrix = AsRectangle(name, value, line, col);
        if (matrix.GetLength(0) != matrix.GetLength(1))
        {
            throw new JgsRuntimeException(line, col, $"{name}: the correlation matrix must be square.");
        }

        if (width > 0 && matrix.GetLength(0) != width)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: U has {width} columns but the correlation matrix is {matrix.GetLength(0)}-by-{matrix.GetLength(0)}.");
        }

        return matrix;
    }

    private static double DegreesOfFreedomArgument(
        string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        if (args.Count <= index || IsPlaceholderValue(args[index]))
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: a t copula also needs its degrees of freedom.");
        }

        double df = Num(name, args, index, line, col);
        if (!(df > 0))
        {
            throw new JgsRuntimeException(line, col, $"{name}: the degrees of freedom must be above zero.");
        }

        return df;
    }

    // --- Distributions described rather than named ---------------------------------------------------

    /// <summary>
    /// <c>[r, type, coefs] = johnsrnd(quantiles, m, n)</c>: draws from the member of the Johnson system
    /// that passes through four quantiles.
    /// </summary>
    private static JgsValue[] JohnsonDraw(
        Random random, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("johnsrnd", args, 1, 3, line, col);
        (double[] flat, int rows, int columns) = DenseMatrix("johnsrnd", args[0], line, col);

        double[] deviates;
        double[] values;
        if (rows == 2 && columns == 4)
        {
            deviates = [flat[0], flat[2], flat[4], flat[6]];
            values = [flat[1], flat[3], flat[5], flat[7]];
        }
        else if (flat.Length == 4)
        {
            // The four standard normal deviates MathWorks pairs the quantiles with when the caller
            // gives only the quantiles.
            deviates = [-1.5, -0.5, 0.5, 1.5];
            values = flat;
        }
        else
        {
            throw new JgsRuntimeException(line, col,
                "johnsrnd: the quantiles are four values, or a 2-by-4 matrix of deviates over values.");
        }

        MomentMatching.JohnsonCurve curve = Guarded(
            "johnsrnd", () => MomentMatching.Johnson(deviates, values), line, col);

        (int drawRows, int drawColumns) = DrawShape("johnsrnd", args, 1, line, col);
        var draws = new double[drawRows * drawColumns];
        for (int i = 0; i < draws.Length; i++)
        {
            draws[i] = curve.At(StandardNormal(random));
        }

        JgsValue coefficients = RowVector([curve.Gamma, curve.Eta, curve.Xi, curve.Lambda]);
        return Outputs(
            wanted,
            Shaped(draws, drawRows, drawColumns),
            JgsValue.Str(curve.Kind.ToString()),
            coefficients);
    }

    /// <summary>
    /// <c>[r, type, coefs] = pearsrnd(mu, sigma, skew, kurt, m, n)</c>: draws from the member of the
    /// Pearson system with the given four moments.
    /// </summary>
    private static JgsValue[] PearsonDraw(
        Random random, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("pearsrnd", args, 4, 6, line, col);
        double mean = Num("pearsrnd", args, 0, line, col);
        double deviation = Num("pearsrnd", args, 1, line, col);
        double skewness = Num("pearsrnd", args, 2, line, col);
        double kurtosis = Num("pearsrnd", args, 3, line, col);

        MomentMatching.PearsonCurve curve = Guarded(
            "pearsrnd", () => MomentMatching.Pearson(mean, deviation, skewness, kurtosis), line, col);

        (int rows, int columns) = DrawShape("pearsrnd", args, 4, line, col);
        var draws = new double[rows * columns];
        for (int i = 0; i < draws.Length; i++)
        {
            draws[i] = curve.Quantile(random.NextDouble());
        }

        return Outputs(
            wanted,
            Shaped(draws, rows, columns),
            JgsValue.Number(curve.Type),
            RowVector(curve.Coefficients));
    }

    /// <summary>How many draws were asked for, and in what shape.</summary>
    private static (int Rows, int Columns) DrawShape(
        string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        if (args.Count <= index || IsPlaceholderValue(args[index]))
        {
            return (1, 1);
        }

        double[] first = FlattenColumnMajor(name, args[index], line, col);
        if (first.Length == 2 && args.Count <= index + 1)
        {
            return ((int)first[0], (int)first[1]);
        }

        int rows = (int)first[0];
        int columns = args.Count > index + 1 && !IsPlaceholderValue(args[index + 1])
            ? Count(name, args, index + 1, line, col)
            : rows;

        if (rows < 0 || columns < 0)
        {
            throw new JgsRuntimeException(line, col, $"{name}: the number of draws cannot be negative.");
        }

        return (rows, columns);
    }

    private static JgsValue Shaped(double[] values, int rows, int columns) =>
        rows * columns == 1 ? JgsValue.Number(values[0]) : JgsMatrix.FromColumnMajor(values, rows, columns);

    private static double StandardNormal(Random random)
    {
        double u = 1 - random.NextDouble();
        double v = random.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v);
    }

    // --- The two Markov chains ------------------------------------------------------------------------

    /// <summary>
    /// <c>[smpl, accept] = mhsample(start, n, 'pdf', f, 'proprnd', g, …)</c>: a Metropolis-Hastings chain
    /// whose long-run distribution is the one the density describes.
    /// </summary>
    private static JgsValue[] MetropolisDraw(
        Random random, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "mhsample needs a starting point and a number of draws.");
        }

        ParsedArgs parsed = ChainOptions.Parse(args, 2, line, col);
        double[] start = FlattenColumnMajor("mhsample", parsed.Positional[0], line, col);
        int count = WholeOf("mhsample", parsed.Positional[1], line, col);

        Func<double[], double> logTarget = LogDensity("mhsample", parsed, "pdf", "logpdf", line, col);
        if (parsed.Named("proprnd") is not { } proposal || proposal.Type != JgsType.Function)
        {
            throw new JgsRuntimeException(line, col,
                "mhsample: 'proprnd' is the function that proposes the next point, and it is required.");
        }

        bool symmetric = parsed.Named("symmetric") is { } flag && flag.IsTruthy;
        Func<double[], double[], double>? logProposal = null;
        if (!symmetric)
        {
            Func<double[], double[], double>? density = PairDensity(parsed, line, col);
            logProposal = density
                ?? throw new JgsRuntimeException(line, col,
                    "mhsample: an asymmetric proposal needs 'proppdf' or 'logproppdf'. Say 'symmetric', true "
                    + "when the proposal is symmetric and the term cancels.");
        }

        int burnIn = parsed.Whole("burnin", 0);
        int thin = parsed.Whole("thin", 1);
        if (parsed.Whole("nchain", 1) != 1)
        {
            throw new JgsRuntimeException(line, col,
                "mhsample: 'nchain' runs several chains at once, which is not supported. Call it once per chain.");
        }

        MarkovChain.Chain chain = Guarded(
            "mhsample",
            () => MarkovChain.Using(random, () => MarkovChain.Metropolis(
                start,
                count,
                logTarget,
                point => FlattenColumnMajor("mhsample", proposal.AsCallable.Call([RowVector(point)], line, col), line, col),
                logProposal,
                burnIn,
                thin)),
            line,
            col);

        return Outputs(wanted, ChainMatrix(chain.Samples, start.Length), JgsValue.Number(chain.Accepted));
    }

    /// <summary>
    /// <c>[rnd, neval] = slicesample(start, n, 'pdf', f, …)</c>: a slice sampler, which needs no proposal
    /// because it draws from under the density directly.
    /// </summary>
    private static JgsValue[] SliceDraw(
        Random random, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "slicesample needs a starting point and a number of draws.");
        }

        ParsedArgs parsed = SliceOptions.Parse(args, 2, line, col);
        double[] start = FlattenColumnMajor("slicesample", parsed.Positional[0], line, col);
        int count = WholeOf("slicesample", parsed.Positional[1], line, col);
        Func<double[], double> logTarget = LogDensity("slicesample", parsed, "pdf", "logpdf", line, col);

        double[] width = parsed.Named("width") is { } given
            ? NumericVector("slicesample", given, line, col)
            : [10];

        foreach (double one in width)
        {
            if (!(one > 0))
            {
                throw new JgsRuntimeException(line, col, "slicesample: 'width' must be above zero.");
            }
        }

        int burnIn = parsed.Whole("burnin", 0);
        int thin = parsed.Whole("thin", 1);

        MarkovChain.Chain chain = Guarded(
            "slicesample",
            () => MarkovChain.Using(random, () => MarkovChain.Slice(start, count, logTarget, width, burnIn, thin)),
            line,
            col);

        return Outputs(wanted, ChainMatrix(chain.Samples, start.Length), JgsValue.Number(chain.Evaluations));
    }

    /// <summary>
    /// The logarithm of the density a chain is aimed at, from either the density itself or its logarithm.
    /// Working in the logarithm throughout is what keeps a small density from underflowing to zero and
    /// making every comparison meaningless.
    /// </summary>
    private static Func<double[], double> LogDensity(
        string name, ParsedArgs parsed, string plain, string logged, int line, int col)
    {
        JgsValue? density = parsed.Named(plain);
        JgsValue? logarithm = parsed.Named(logged);

        if (density is not null && logarithm is not null)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: give the density or its logarithm, not both.");
        }

        JgsValue chosen = logarithm ?? density
            ?? throw new JgsRuntimeException(line, col,
                $"{name}: the target distribution arrives as '{plain}' or '{logged}'.");

        if (chosen.Type != JgsType.Function)
        {
            throw new JgsRuntimeException(line, col, $"{name}: the target distribution must be a function.");
        }

        bool alreadyLogged = logarithm is not null;
        return point =>
        {
            JgsValue answer = chosen.AsCallable.Call([RowVector(point)], line, col);
            double value = NumOf(name, answer, line, col);
            return alreadyLogged ? value : value > 0 ? Math.Log(value) : double.NegativeInfinity;
        };
    }

    /// <summary>The proposal density of one point given another, in the logarithm.</summary>
    private static Func<double[], double[], double>? PairDensity(ParsedArgs parsed, int line, int col)
    {
        JgsValue? density = parsed.Named("proppdf");
        JgsValue? logarithm = parsed.Named("logproppdf");
        JgsValue? chosen = logarithm ?? density;
        if (chosen is null || chosen.Type != JgsType.Function)
        {
            return null;
        }

        bool alreadyLogged = logarithm is not null;
        return (from, to) =>
        {
            JgsValue answer = chosen.AsCallable.Call([RowVector(from), RowVector(to)], line, col);
            double value = NumOf("mhsample", answer, line, col);
            return alreadyLogged ? value : value > 0 ? Math.Log(value) : double.NegativeInfinity;
        };
    }

    private static JgsValue ChainMatrix(double[][] samples, int width)
    {
        var flat = new double[samples.Length * width];
        for (int i = 0; i < samples.Length; i++)
        {
            for (int j = 0; j < width; j++)
            {
                flat[i + (j * samples.Length)] = samples[i][j];
            }
        }

        return JgsMatrix.FromColumnMajor(flat, samples.Length, width);
    }

    // --- The covariance of an estimate ---------------------------------------------------------------

    /// <summary>
    /// <c>acov = mlecov(params, data, 'pdf', f)</c>: how precise a maximum likelihood estimate is, from
    /// the curvature of the likelihood around it.
    /// </summary>
    private static JgsValue LikelihoodCovarianceOf(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 3)
        {
            throw new JgsRuntimeException(line, col,
                "mlecov needs the estimate, the data, and the density the estimate maximizes.");
        }

        var spec = new OptionSpec("mlecov", [], ["pdf", "logpdf", "cdf", "logsf", "options"]);
        ParsedArgs parsed = spec.Parse(args, 2, line, col);
        double[] estimate = FlattenColumnMajor("mlecov", parsed.Positional[0], line, col);
        double[] data = FlattenColumnMajor("mlecov", parsed.Positional[1], line, col);

        JgsValue? density = parsed.Named("pdf");
        JgsValue? logarithm = parsed.Named("logpdf");
        JgsValue chosen = logarithm ?? density
            ?? throw new JgsRuntimeException(line, col,
                "mlecov: the density arrives as 'pdf' or 'logpdf'.");

        if (chosen.Type != JgsType.Function)
        {
            throw new JgsRuntimeException(line, col, "mlecov: the density must be a function.");
        }

        bool alreadyLogged = logarithm is not null;
        JgsValue observations = JgsMatrix.FromColumnMajor(data, data.Length, 1);

        double NegativeLogLikelihood(double[] parameters)
        {
            var call = new List<JgsValue> { observations };
            foreach (double parameter in parameters)
            {
                call.Add(JgsValue.Number(parameter));
            }

            JgsValue answer = chosen.AsCallable.Call([.. call], line, col);
            double[] each = FlattenColumnMajor("mlecov", answer, line, col);
            double total = 0;
            foreach (double one in each)
            {
                total += alreadyLogged ? one : one > 0 ? Math.Log(one) : -1e12;
            }

            return -total;
        }

        double[,] covariance = Guarded(
            "mlecov", () => LikelihoodCovariance.Of(NegativeLogLikelihood, estimate), line, col);
        return Rectangle(covariance);
    }

    // --- The piecewise distribution -------------------------------------------------------------------

    /// <summary>
    /// <c>pd = paretotails(x, pl, pu)</c>: empirical in the middle, generalized Pareto in each tail.
    /// </summary>
    private static JgsValue TailedDistribution(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("paretotails", args, 3, 4, line, col);
        double[] values = FlattenColumnMajor("paretotails", args[0], line, col);
        double lower = Num("paretotails", args, 1, line, col);
        double upper = Num("paretotails", args, 2, line, col);

        if (args.Count > 3 && !IsPlaceholderValue(args[3]))
        {
            string word = Str("paretotails", args, 3, line, col).ToLowerInvariant();
            if (word is not ("ecdf" or "kernel" or "function"))
            {
                throw new JgsRuntimeException(line, col,
                    "paretotails: the middle is fitted by 'ecdf', which is the only choice supported here.");
            }

            if (word != "ecdf")
            {
                throw new JgsRuntimeException(line, col,
                    $"paretotails: '{word}' fits the middle by something other than the empirical "
                    + "distribution function, which is not supported. The tails are the part this name is about.");
            }
        }

        ParetoTails fitted = Guarded(
            "paretotails", () => new ParetoTails(values, lower, upper), line, col);

        return Structure(
            (TransformTag, JgsValue.Str("paretotails")),
            ("DistributionName", JgsValue.Str("Piecewise distribution with Pareto tails")),
            ("NumObservations", JgsValue.Number(fitted.Count)),
            ("boundary", RowVector([lower, upper])),
            ("cutoff", RowVector([fitted.LowerBoundary, fitted.UpperBoundary])),
            ("lowerparams", RowVector([.. fitted.LowerParameters])),
            ("upperparams", RowVector([.. fitted.UpperParameters])),
            ("x", JgsMatrix.FromColumnMajor(SortedCopy(values), values.Length, 1)));
    }

    private static double[] SortedCopy(double[] values)
    {
        var sorted = (double[])values.Clone();
        Array.Sort(sorted);
        return sorted;
    }

    /// <summary>Whether a value is a piecewise distribution, and the distribution it carries.</summary>
    private static bool TryReadParetoTails(JgsValue value, out ParetoTails? fitted)
    {
        fitted = null;
        if (TaggedClassOf(value) != "paretotails")
        {
            return false;
        }

        IReadOnlyDictionary<string, JgsValue> map = value.AsStruct;
        if (!map.TryGetValue("x", out JgsValue? data) || !map.TryGetValue("boundary", out JgsValue? bounds))
        {
            return false;
        }

        double[] values = Flatten(data);
        double[] limits = Flatten(bounds);
        if (limits.Length != 2)
        {
            return false;
        }

        fitted = new ParetoTails(values, limits[0], limits[1]);
        return true;
    }

    // --- The neighbourhood searchers -------------------------------------------------------------------

    /// <summary>
    /// <c>ns = createns(X)</c>, and the two class names that build the same thing directly: a set of
    /// points prepared to be asked for neighbours later.
    /// </summary>
    /// <remarks>
    /// The two classes differ in MATLAB by the structure they build — one keeps the points in a tree and
    /// the other keeps them in a list — and not by the answer they give, which is exact either way. Here
    /// the search is exhaustive underneath both, so the object records which was asked for and answers
    /// the class question with it, and the neighbours come back the same.
    /// </remarks>
    private static JgsValue Searcher(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs the points to search among.");
        }

        ParsedArgs parsed = SearcherOptions.Parse(args, 1, line, col);
        (double[][] rows, int width) = Observations(name, parsed.Positional[0], line, col);
        _ = rows;

        string kind = name switch
        {
            "KDTreeSearcher" => "KDTreeSearcher",
            "ExhaustiveSearcher" => "ExhaustiveSearcher",
            _ => parsed.Word("nsmethod", "exhaustive", "exhaustive", "kdtree") == "kdtree"
                ? "KDTreeSearcher"
                : "ExhaustiveSearcher",
        };

        string metric = parsed.Named("distance") is { } given
            ? StrOf(name + ": Distance", given, line, col)
            : kind == "KDTreeSearcher" ? "euclidean" : "euclidean";

        var fields = new List<(string, JgsValue)>
        {
            (TransformTag, JgsValue.Str(kind)),
            ("X", parsed.Positional[0]),
            ("Distance", JgsValue.Str(metric)),
            ("NumVariables", JgsValue.Number(width)),
        };

        if (parsed.Named("p") is { } exponent)
        {
            fields.Add(("DistParameter", exponent));
        }
        else if (parsed.Named("cov") is { } covariance)
        {
            fields.Add(("DistParameter", covariance));
        }
        else if (parsed.Named("scale") is { } scale)
        {
            fields.Add(("DistParameter", scale));
        }
        else
        {
            fields.Add(("DistParameter", JgsValue.Array([])));
        }

        if (kind == "KDTreeSearcher")
        {
            fields.Add(("BucketSize", JgsValue.Number(parsed.Whole("bucketsize", 50))));
        }

        return Structure([.. fields]);
    }

    /// <summary>
    /// A neighbour search's arguments with a searcher object unpacked into the points it holds and the
    /// metric it was built with, so that <c>knnsearch(ns, y)</c> and <c>knnsearch(X, y, 'Distance', d)</c>
    /// are the same call by the time either reaches the search.
    /// </summary>
    private static IReadOnlyList<JgsValue> ExpandSearcher(IReadOnlyList<JgsValue> args)
    {
        if (args.Count == 0 || TaggedClassOf(args[0]) is not ("ExhaustiveSearcher" or "KDTreeSearcher"))
        {
            return args;
        }

        IReadOnlyDictionary<string, JgsValue> map = args[0].AsStruct;
        var expanded = new List<JgsValue> { map.TryGetValue("X", out JgsValue? points) ? points : JgsValue.Array([]) };
        for (int i = 1; i < args.Count; i++)
        {
            expanded.Add(args[i]);
        }

        // What the object carries is a default: a metric written at the call wins, because it was
        // written later and more specifically.
        bool named = false;
        for (int i = 1; i + 1 < expanded.Count; i++)
        {
            named |= expanded[i].Type == JgsType.String
                && string.Equals(expanded[i].AsString, "distance", StringComparison.OrdinalIgnoreCase);
        }

        if (!named && map.TryGetValue("Distance", out JgsValue? metric))
        {
            expanded.Add(JgsValue.Str("Distance"));
            expanded.Add(metric);

            if (map.TryGetValue("DistParameter", out JgsValue? parameter)
                && !(parameter.Type == JgsType.Array && parameter.ArrayLength == 0))
            {
                expanded.Add(JgsValue.Str(metric.AsString.ToLowerInvariant() == "mahalanobis" ? "Cov"
                    : metric.AsString.ToLowerInvariant() == "seuclidean" ? "Scale" : "P"));
                expanded.Add(parameter);
            }
        }

        return expanded;
    }

    // --- The embedding ----------------------------------------------------------------------------------

    /// <summary><c>[Y, loss] = tsne(X)</c>: a low-dimensional picture of a high-dimensional set of points.</summary>
    private static JgsValue[] Embed(
        Random random, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "tsne needs some observations.");
        }

        ParsedArgs parsed = EmbeddingOptions.Parse(args, 1, line, col);
        (double[][] rows, int width) = Observations("tsne", parsed.Positional[0], line, col);

        if (parsed.Word("algorithm", "exact", "exact", "barneshut") == "barneshut")
        {
            throw new JgsRuntimeException(line, col,
                "tsne: 'Algorithm','barneshut' approximates the repulsion with a tree. The embedding here "
                + "is exact over all pairs, which is what 'exact' asks for.");
        }

        if (parsed.Named("numpcacomponents") is { } reduced && NumOf("tsne", reduced, line, col) > 0)
        {
            throw new JgsRuntimeException(line, col,
                "tsne: 'NumPCAComponents' reduces the data before embedding it. Call pca first and hand "
                + "the scores to tsne, which is the same thing said in two steps.");
        }

        DistanceMeasure measure = NamedMetric("tsne", parsed, rows, line, col);
        int dimensions = parsed.Whole("numdimensions", 2);
        double perplexity = parsed.Scalar("perplexity", Math.Min(30, (rows.Length - 1) / 3.0));
        double exaggeration = parsed.Scalar("exaggeration", 4);
        // The published default is five hundred, which is tuned for the thousands of points the method
        // is usually run on: the step is proportional to the largest of the original probabilities, and
        // those grow as the number of points falls. Scaling the rate with the number of points is what
        // keeps a small set from being thrown apart on its first step.
        double rate = parsed.Scalar("learnrate", Math.Max(rows.Length / 20.0, 2));
        int iterations = Math.Max(SettingWhole(parsed, "numprint", "MaxIter", 1000), 1);

        double[][] data = rows;
        if (parsed.Named("standardize") is { } standardize && standardize.IsTruthy)
        {
            data = Standardized(rows, width);
        }

        double[][]? start = null;
        if (parsed.Named("initialy") is { } initial)
        {
            (double[][] given, int givenWidth) = Observations("tsne", initial, line, col);
            if (given.Length != rows.Length || givenWidth != dimensions)
            {
                throw new JgsRuntimeException(line, col,
                    "tsne: 'InitialY' needs one starting position per observation, in as many dimensions as asked for.");
            }

            start = given;
        }

        StochasticEmbedding.Embedding embedding = Guarded(
            "tsne",
            () => StochasticEmbedding.Embed(
                random, data, dimensions, perplexity, exaggeration, rate, iterations, measure, start),
            line,
            col);

        var flat = new double[rows.Length * dimensions];
        for (int i = 0; i < rows.Length; i++)
        {
            for (int d = 0; d < dimensions; d++)
            {
                flat[i + (d * rows.Length)] = embedding.Coordinates[i][d];
            }
        }

        return Outputs(
            wanted,
            JgsMatrix.FromColumnMajor(flat, rows.Length, dimensions),
            JgsValue.Number(embedding.Loss.Length == 0 ? double.NaN : embedding.Loss[^1]));
    }

    /// <summary>Each column centred and scaled to unit spread, which is what standardizing means here.</summary>
    private static double[][] Standardized(double[][] rows, int width)
    {
        var scaled = new double[rows.Length][];
        for (int i = 0; i < rows.Length; i++)
        {
            scaled[i] = (double[])rows[i].Clone();
        }

        for (int c = 0; c < width; c++)
        {
            var column = new double[rows.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                column[i] = rows[i][c];
            }

            double mean = DescriptiveStatistics.Mean(column);
            double spread = DescriptiveStatistics.StandardDeviation(column, population: false);
            for (int i = 0; i < rows.Length; i++)
            {
                scaled[i][c] = spread > 0 ? (column[i] - mean) / spread : 0;
            }
        }

        return scaled;
    }
}
