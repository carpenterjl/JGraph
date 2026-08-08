using JGraph.Statistics;
using JGraph.Statistics.Distributions;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M53 wave E, part one: the distributions of a vector rather than a number — the multivariate normal
/// and t, the Wishart pair, the covariance factorization they all rest on, and the multivariate kernel
/// density.
/// </summary>
/// <remarks>
/// <para>
/// Every name here reads its data the same way: one observation per row, one variable per column, so a
/// row vector is a single point in as many dimensions as it is long and a column vector is that many
/// points in one dimension. Getting that backwards is the easiest mistake to make in this corner of the
/// toolbox, so it is the thing the tests pin hardest.
/// </para>
/// <para>
/// The parameters expand the way the univariate distributions' do — one mean for every point, or one
/// mean shared by all of them — but only along the observations. A covariance is a matrix and cannot be
/// broadcast, so a page of them per observation, which MathWorks accepts, is refused by name here.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the multivariate distribution builtins.</summary>
    private static void RegisterMultivariateBuiltins(JgsEnvironment env, Random random)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        Define("cholcov",
            (args, line, col) => CovarianceFactor(args, 1, line, col)[0],
            (args, wanted, line, col) => CovarianceFactor(args, wanted, line, col));

        Define("mvnpdf", (args, line, col) => MultivariateNormalDensity(args, line, col));
        Define("mvtpdf", (args, line, col) => MultivariateTDensity(args, line, col));

        Define("mvncdf",
            (args, line, col) => MultivariateNormalProbability(args, 1, line, col)[0],
            (args, wanted, line, col) => MultivariateNormalProbability(args, wanted, line, col));
        Define("mvtcdf",
            (args, line, col) => MultivariateTProbability(args, 1, line, col)[0],
            (args, wanted, line, col) => MultivariateTProbability(args, wanted, line, col));

        Define("mvnrnd", (args, line, col) => MultivariateNormalDraw(random, args, line, col));
        Define("mvtrnd", (args, line, col) => MultivariateTDraw(random, args, line, col));

        Define("wishrnd",
            (args, line, col) => WishartDraw(random, args, 1, line, col)[0],
            (args, wanted, line, col) => WishartDraw(random, args, wanted, line, col));
        Define("iwishrnd",
            (args, line, col) => InverseWishartDraw(random, args, 1, line, col)[0],
            (args, wanted, line, col) => InverseWishartDraw(random, args, wanted, line, col));

        Define("mvksdensity", (args, line, col) => MultivariateKernelDensity(args, line, col));
    }

    // --- cholcov -----------------------------------------------------------------------------------

    /// <summary>
    /// <c>[T, num] = cholcov(SIGMA)</c>: a factor with <c>Tᵀ·T = SIGMA</c>, and how many rows it has.
    /// </summary>
    private static JgsValue[] CovarianceFactor(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("cholcov", args, 1, 2, line, col);
        (double[] flat, int n) = SquareMatrix("cholcov", args[0], line, col);
        bool choleskyOnly = args.Count > 1 && Count("cholcov", args, 1, line, col) == 0;

        double[,] sigma = Square(flat, n);
        (double[,] Factor, int Rank)? factored = Multivariate.CovarianceFactor(sigma);

        // A matrix with a negative eigenvalue is not a covariance, and neither is one that is only
        // semi-definite when the caller asked for the definite-only factorization. MathWorks answers
        // both the same way: no factor, and a negative count saying so.
        if (factored is null || (choleskyOnly && factored.Value.Rank < n))
        {
            return wanted <= 1 ? [JgsValue.Array([])] : [JgsValue.Array([]), JgsValue.Number(-1)];
        }

        (double[,] factor, int rank) = factored.Value;
        JgsValue answer = FromDense(factor);
        return wanted <= 1 ? [answer] : [answer, JgsValue.Number(rank)];
    }

    // --- Densities ---------------------------------------------------------------------------------

    /// <summary><c>y = mvnpdf(X, Mu, Sigma)</c>: the multivariate normal density, one value per row.</summary>
    private static JgsValue MultivariateNormalDensity(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("mvnpdf", args, 1, 3, line, col);
        (double[][] points, int width) = Observations("mvnpdf", args[0], line, col);
        double[][] means = MeanRows("mvnpdf", args, 1, points.Length, width, line, col);
        double[,] sigma = CovarianceArgument("mvnpdf", args, 2, width, line, col);

        var densities = new double[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            double[] point = points[i];
            double[] mean = means[i];
            densities[i] = Guarded("mvnpdf", () => Multivariate.NormalPdf(point, mean, sigma), line, col);
        }

        return ColumnOfAnswers(densities);
    }

    /// <summary><c>y = mvtpdf(X, C, df)</c>: the multivariate t density, one value per row.</summary>
    private static JgsValue MultivariateTDensity(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("mvtpdf", args, 3, line, col);
        (double[][] points, int width) = Observations("mvtpdf", args[0], line, col);
        double[,] correlation = CorrelationArgument("mvtpdf", args[1], width, line, col);
        double df = DegreesOfFreedom("mvtpdf", args, 2, line, col);

        var densities = new double[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            double[] point = points[i];
            densities[i] = Guarded("mvtpdf", () => Multivariate.TPdf(point, correlation, df), line, col);
        }

        return ColumnOfAnswers(densities);
    }

    // --- Distribution functions --------------------------------------------------------------------

    /// <summary>
    /// <c>p = mvncdf(X, mu, sigma)</c> and <c>mvncdf(xl, xu, mu, sigma)</c>: the probability of falling
    /// below a point, or inside a box.
    /// </summary>
    private static JgsValue[] MultivariateNormalProbability(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        IReadOnlyList<JgsValue> given = WithoutOptionsStructure(args);
        if (given.Count is not (1 or 3 or 4))
        {
            throw new JgsRuntimeException(line, col,
                "mvncdf(X), mvncdf(X, mu, sigma) or mvncdf(xl, xu, mu, sigma) — the four-argument form is a box.");
        }

        bool box = given.Count == 4;
        int shift = box ? 1 : 0;
        (double[][] uppers, int width) = Observations("mvncdf", given[box ? 1 : 0], line, col);
        double[][] lowers = box
            ? Observations("mvncdf", given[0], line, col).Rows
            : Filled(uppers.Length, width, double.NegativeInfinity);

        if (lowers.Length != uppers.Length)
        {
            throw new JgsRuntimeException(line, col,
                "mvncdf: the lower and upper limits must have the same number of rows.");
        }

        double[][] means = MeanRows("mvncdf", given, 1 + shift, uppers.Length, width, line, col);
        double[,] sigma = CovarianceArgument("mvncdf", given, 2 + shift, width, line, col);

        var probabilities = new double[uppers.Length];
        double worst = 0;
        for (int i = 0; i < uppers.Length; i++)
        {
            var low = new double[width];
            var high = new double[width];
            for (int v = 0; v < width; v++)
            {
                low[v] = lowers[i][v] - means[i][v];
                high[v] = uppers[i][v] - means[i][v];
            }

            (double probability, double error) = Guarded(
                "mvncdf", () => Multivariate.NormalCdf(low, high, sigma), line, col);
            probabilities[i] = probability;
            worst = Math.Max(worst, error);
        }

        JgsValue answer = ColumnOfAnswers(probabilities);
        return wanted <= 1 ? [answer] : [answer, JgsValue.Number(worst)];
    }

    /// <summary><c>p = mvtcdf(X, C, df)</c> and <c>mvtcdf(xl, xu, C, df)</c>.</summary>
    private static JgsValue[] MultivariateTProbability(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        IReadOnlyList<JgsValue> given = WithoutOptionsStructure(args);
        ArityRange("mvtcdf", given, 3, 4, line, col);

        bool box = given.Count == 4;
        (double[][] uppers, int width) = Observations("mvtcdf", given[box ? 1 : 0], line, col);
        double[][] lowers = box
            ? Observations("mvtcdf", given[0], line, col).Rows
            : Filled(uppers.Length, width, double.NegativeInfinity);

        if (lowers.Length != uppers.Length)
        {
            throw new JgsRuntimeException(line, col,
                "mvtcdf: the lower and upper limits must have the same number of rows.");
        }

        double[,] correlation = CorrelationArgument("mvtcdf", given[box ? 2 : 1], width, line, col);
        double df = DegreesOfFreedom("mvtcdf", given, box ? 3 : 2, line, col);

        var probabilities = new double[uppers.Length];
        double worst = 0;
        for (int i = 0; i < uppers.Length; i++)
        {
            double[] low = lowers[i];
            double[] high = uppers[i];
            (double probability, double error) = Guarded(
                "mvtcdf", () => Multivariate.TCdf(low, high, correlation, df), line, col);
            probabilities[i] = probability;
            worst = Math.Max(worst, error);
        }

        JgsValue answer = ColumnOfAnswers(probabilities);
        return wanted <= 1 ? [answer] : [answer, JgsValue.Number(worst)];
    }

    // --- Draws -------------------------------------------------------------------------------------

    /// <summary><c>R = mvnrnd(mu, sigma, n)</c>: n draws, one per row.</summary>
    private static JgsValue MultivariateNormalDraw(
        Random random, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("mvnrnd", args, 2, 3, line, col);
        (double[][] givenMeans, int width) = Observations("mvnrnd", args[0], line, col);
        double[,] sigma = CovarianceArgument("mvnrnd", args, 1, width, line, col);

        int count = givenMeans.Length;
        if (args.Count > 2)
        {
            int asked = Count("mvnrnd", args, 2, line, col);
            if (givenMeans.Length != 1 && givenMeans.Length != asked)
            {
                throw new JgsRuntimeException(line, col,
                    $"mvnrnd: {givenMeans.Length} means cannot produce {asked} draws.");
            }

            count = Math.Max(0, asked);
        }

        (double[,] Factor, int Rank)? factored = Multivariate.CovarianceFactor(sigma);
        if (factored is null)
        {
            throw new JgsRuntimeException(line, col, "mvnrnd: sigma must be a covariance matrix.");
        }

        var flat = new double[count * width];
        for (int i = 0; i < count; i++)
        {
            double[] draw = Multivariate.NormalSample(
                random, givenMeans[givenMeans.Length == 1 ? 0 : i], factored.Value.Factor);
            for (int v = 0; v < width; v++)
            {
                flat[i + (v * count)] = draw[v];
            }
        }

        return count == 1 && width == 1 ? JgsValue.Number(flat[0]) : JgsMatrix.FromColumnMajor(flat, count, width);
    }

    /// <summary><c>R = mvtrnd(C, df, n)</c>: n draws from the multivariate t, one per row.</summary>
    private static JgsValue MultivariateTDraw(Random random, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("mvtrnd", args, 2, 3, line, col);
        (double[] flatCorrelation, int width) = SquareMatrix("mvtrnd", args[0], line, col);
        double[,] correlation = Square(flatCorrelation, width);
        double df = DegreesOfFreedom("mvtrnd", args, 1, line, col);
        int count = args.Count > 2 ? Math.Max(0, Count("mvtrnd", args, 2, line, col)) : 1;

        (double[,] Factor, int Rank)? factored = Multivariate.CovarianceFactor(correlation);
        if (factored is null)
        {
            throw new JgsRuntimeException(line, col, "mvtrnd: C must be a correlation matrix.");
        }

        var values = new double[count * width];
        for (int i = 0; i < count; i++)
        {
            double[] draw = Multivariate.TSample(random, factored.Value.Factor, df);
            for (int v = 0; v < width; v++)
            {
                values[i + (v * count)] = draw[v];
            }
        }

        return count == 1 && width == 1 ? JgsValue.Number(values[0]) : JgsMatrix.FromColumnMajor(values, count, width);
    }

    /// <summary><c>[W, D] = wishrnd(sigma, df)</c>: one Wishart draw, with mean <c>df·sigma</c>.</summary>
    private static JgsValue[] WishartDraw(
        Random random, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("wishrnd", args, 2, 3, line, col);
        double df = DegreesOfFreedom("wishrnd", args, 1, line, col);
        double[,] factor = ReusableFactor("wishrnd", args, line, col);

        double[,] draw = Guarded("wishrnd", () => Multivariate.WishartSample(random, factor, df), line, col);
        return wanted <= 1 ? [FromDense(draw)] : [FromDense(draw), FromDense(factor)];
    }

    /// <summary>
    /// <c>[W, DI] = iwishrnd(tau, df)</c>: one inverse Wishart draw — the inverse of a Wishart draw
    /// taken with the inverse scale, which is what the distribution means.
    /// </summary>
    private static JgsValue[] InverseWishartDraw(
        Random random, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("iwishrnd", args, 2, 3, line, col);
        double df = DegreesOfFreedom("iwishrnd", args, 1, line, col);

        double[,] factor;
        if (args.Count > 2)
        {
            (double[] flat, int order) = SquareMatrix("iwishrnd", args[2], line, col);
            factor = Square(flat, order);
        }
        else
        {
            (double[] flat, int order) = SquareMatrix("iwishrnd", args[0], line, col);
            double[,] inverse = Guarded(
                "iwishrnd", () => Multivariate.SymmetricInverse(Square(flat, order)), line, col);
            factor = Multivariate.CovarianceFactor(inverse) is { } found
                ? found.Factor
                : throw new JgsRuntimeException(line, col, "iwishrnd: tau must be a covariance matrix.");
        }

        double[,] wishart = Guarded("iwishrnd", () => Multivariate.WishartSample(random, factor, df), line, col);
        double[,] draw = Guarded("iwishrnd", () => Multivariate.SymmetricInverse(wishart), line, col);
        return wanted <= 1 ? [FromDense(draw)] : [FromDense(draw), FromDense(factor)];
    }

    /// <summary>The Cholesky-style factor a Wishart draw is scaled by, given or computed.</summary>
    private static double[,] ReusableFactor(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        (double[] flat, int order) = SquareMatrix(name, args[args.Count > 2 ? 2 : 0], line, col);
        if (args.Count > 2)
        {
            return Square(flat, order);
        }

        return Multivariate.CovarianceFactor(Square(flat, order)) is { } found
            ? found.Factor
            : throw new JgsRuntimeException(line, col, $"{name}: sigma must be a covariance matrix.");
    }

    // --- Multivariate kernel density -----------------------------------------------------------------

    private static readonly OptionSpec MvKsdensityOptions = new(
        "mvksdensity", [], ["Bandwidth", "Kernel", "Weights"]);

    /// <summary>
    /// <c>f = mvksdensity(x, pts, 'Bandwidth', bw)</c>: a product-kernel density estimate, evaluated at
    /// the given points.
    /// </summary>
    /// <remarks>
    /// The bandwidth is required, as MathWorks requires it: there is no agreed rule of thumb in more
    /// than one dimension, and inventing one would make every answer depend on a choice the caller
    /// never saw.
    /// </remarks>
    private static JgsValue MultivariateKernelDensity(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ParsedArgs parsed = MvKsdensityOptions.Parse(args, 2, line, col);
        if (parsed.Positional.Count != 2)
        {
            throw new JgsRuntimeException(line, col,
                "mvksdensity(x, pts, 'Bandwidth', bw) estimates the density of x at the points pts.");
        }

        (double[][] sample, int width) = Observations("mvksdensity", parsed.Positional[0], line, col);
        (double[][] points, int pointWidth) = Observations("mvksdensity", parsed.Positional[1], line, col);
        if (pointWidth != width)
        {
            throw new JgsRuntimeException(line, col,
                $"mvksdensity: the data has {width} variables and the evaluation points have {pointWidth}.");
        }

        if (parsed.Named("Bandwidth") is not JgsValue bandwidthValue)
        {
            throw new JgsRuntimeException(line, col,
                "mvksdensity needs 'Bandwidth': there is no default bandwidth in more than one dimension.");
        }

        double[] bandwidth = NumericVector("mvksdensity", bandwidthValue, line, col);
        if (bandwidth.Length == 1 && width > 1)
        {
            bandwidth = Enumerable.Repeat(bandwidth[0], width).ToArray();
        }

        if (bandwidth.Length != width)
        {
            throw new JgsRuntimeException(line, col,
                $"mvksdensity: the bandwidth needs one value per variable, or one for all {width}.");
        }

        foreach (double each in bandwidth)
        {
            if (!(each > 0))
            {
                throw new JgsRuntimeException(line, col, "mvksdensity: every bandwidth must be positive.");
            }
        }

        EmpiricalDistribution.Kernel kernel = parsed.Named("Kernel") is JgsValue word
            ? KernelWord("mvksdensity", word, line, col)
            : EmpiricalDistribution.Kernel.Normal;

        double[] weights = parsed.Named("Weights") is JgsValue given
            ? NumericVector("mvksdensity", given, line, col)
            : Enumerable.Repeat(1.0, sample.Length).ToArray();
        if (weights.Length != sample.Length)
        {
            throw new JgsRuntimeException(line, col,
                $"mvksdensity: {weights.Length} weights for {sample.Length} observations.");
        }

        double totalWeight = weights.Sum();
        if (!(totalWeight > 0))
        {
            throw new JgsRuntimeException(line, col, "mvksdensity: the weights must not all be zero.");
        }

        double volume = 1;
        foreach (double h in bandwidth)
        {
            volume *= h;
        }

        var estimate = new double[points.Length];
        for (int p = 0; p < points.Length; p++)
        {
            double sum = 0;
            for (int i = 0; i < sample.Length; i++)
            {
                double product = weights[i];
                for (int v = 0; v < width && product != 0; v++)
                {
                    product *= EmpiricalDistribution.KernelWeight(
                        kernel, (points[p][v] - sample[i][v]) / bandwidth[v]);
                }

                sum += product;
            }

            estimate[p] = sum / (totalWeight * volume);
        }

        return ColumnOfAnswers(estimate);
    }

    /// <summary>Reads a kernel name, refusing a misspelling by listing the ones that exist.</summary>
    private static EmpiricalDistribution.Kernel KernelWord(string name, JgsValue value, int line, int col)
    {
        string word = value.Type == JgsType.String
            ? value.AsString
            : throw new JgsRuntimeException(line, col, $"{name}: 'Kernel' takes the name of a kernel.");

        return word.ToLowerInvariant() switch
        {
            "normal" => EmpiricalDistribution.Kernel.Normal,
            "box" => EmpiricalDistribution.Kernel.Box,
            "triangle" => EmpiricalDistribution.Kernel.Triangle,
            "epanechnikov" => EmpiricalDistribution.Kernel.Epanechnikov,
            _ => throw new JgsRuntimeException(line, col,
                $"{name}: unknown kernel '{word}' — the kernels are 'normal', 'box', 'triangle' and 'epanechnikov'."),
        };
    }

    // --- Shared argument reading ---------------------------------------------------------------------

    /// <summary>
    /// A value read as observations: one row per observation, one column per variable. A plain vector
    /// is one observation of as many variables as it is long only when it was written as a row; a
    /// column of numbers is that many observations of one variable, which is the same rule MATLAB's
    /// own multivariate functions use.
    /// </summary>
    private static (double[][] Rows, int Width) Observations(
        string name, JgsValue value, int line, int col)
    {
        (double[] flat, int rows, int columns) = DenseMatrix(name, value, line, col);
        var observations = new double[rows][];
        for (int r = 0; r < rows; r++)
        {
            observations[r] = new double[columns];
            for (int c = 0; c < columns; c++)
            {
                observations[r][c] = flat[r + (c * rows)];
            }
        }

        return (observations, columns);
    }

    /// <summary>A numeric value read as a two-dimensional matrix, however it was shaped.</summary>
    private static (double[] Flat, int Rows, int Columns) DenseMatrix(
        string name, JgsValue value, int line, int col)
    {
        int[] dims = JgsMatrix.DimsOf(value);
        if (dims.Length > 2)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} takes a vector or a matrix, not an array with more than two dimensions.");
        }

        double[] flat = FlattenColumnMajor(name, value, line, col);
        int rows = dims.Length > 0 ? dims[0] : 1;
        int columns = dims.Length > 1 ? dims[1] : 1;

        // A plain number carries no dimensions and an unshaped list carries none that account for its
        // storage; both read as a single row, which is what they behave like everywhere else.
        if ((long)rows * columns != flat.Length)
        {
            rows = 1;
            columns = flat.Length;
        }

        return (flat, rows, columns);
    }

    /// <summary>
    /// The mean for each observation: one row given for all of them, one row each, or — where the
    /// argument was left out — the origin.
    /// </summary>
    private static double[][] MeanRows(
        string name, IReadOnlyList<JgsValue> args, int slot, int count, int width, int line, int col)
    {
        if (args.Count <= slot || IsPlaceholderValue(args[slot]))
        {
            return Filled(count, width, 0);
        }

        (double[][] rows, int given) = Observations(name, args[slot], line, col);
        if (given == 1 && width > 1 && rows.Length == 1)
        {
            // A single number means the same mean in every variable.
            return Filled(count, width, rows[0][0]);
        }

        if (given != width)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: the mean has {given} variables and the data has {width}.");
        }

        if (rows.Length == 1)
        {
            var shared = new double[count][];
            Array.Fill(shared, rows[0]);
            return shared;
        }

        if (rows.Length != count)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: {rows.Length} means for {count} observations.");
        }

        return rows;
    }

    /// <summary>
    /// The covariance argument: a full matrix, a row of variances standing for a diagonal one, a single
    /// variance shared by every variable, or — left out — the identity.
    /// </summary>
    private static double[,] CovarianceArgument(
        string name, IReadOnlyList<JgsValue> args, int slot, int width, int line, int col)
    {
        if (args.Count <= slot || IsPlaceholderValue(args[slot]))
        {
            var identity = new double[width, width];
            for (int i = 0; i < width; i++)
            {
                identity[i, i] = 1;
            }

            return identity;
        }

        (double[] flat, int rows, int columns) = DenseMatrix(name, args[slot], line, col);
        if (rows == columns && rows == width)
        {
            return Square(flat, width);
        }

        if (rows == 1 || columns == 1)
        {
            double[] variances = flat.Length == 1
                ? Enumerable.Repeat(flat[0], width).ToArray()
                : flat;
            if (variances.Length != width)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: {variances.Length} variances for {width} variables.");
            }

            var diagonal = new double[width, width];
            for (int i = 0; i < width; i++)
            {
                diagonal[i, i] = variances[i];
            }

            return diagonal;
        }

        throw new JgsRuntimeException(line, col,
            $"{name}: sigma must be a {width}-by-{width} matrix or a row of {width} variances; "
            + "a separate covariance for each observation is not accepted.");
    }

    /// <summary>A correlation matrix argument, which unlike a covariance has no vector shorthand.</summary>
    private static double[,] CorrelationArgument(string name, JgsValue value, int width, int line, int col)
    {
        (double[] flat, int order) = SquareMatrix(name, value, line, col);
        if (order != width)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: the correlation matrix is {order}-by-{order} and the data has {width} variables.");
        }

        return Square(flat, order);
    }

    /// <summary>A positive degrees-of-freedom argument.</summary>
    private static double DegreesOfFreedom(string name, IReadOnlyList<JgsValue> args, int slot, int line, int col)
    {
        double[] given = ToDoubles(name, args[slot], line, col);
        if (given.Length != 1 || !(given[0] > 0))
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: the degrees of freedom must be one number above zero.");
        }

        return given[0];
    }

    /// <summary>
    /// The arguments with a trailing options structure removed. MathWorks lets the multivariate
    /// probabilities take one to set the tolerance of a Monte Carlo rule; the rule here is
    /// deterministic and has no tolerance to set, so the argument is accepted and has no effect.
    /// </summary>
    private static IReadOnlyList<JgsValue> WithoutOptionsStructure(IReadOnlyList<JgsValue> args) =>
        args.Count > 0 && args[^1].Type == JgsType.Struct ? args.Take(args.Count - 1).ToList() : args;

    private static double[][] Filled(int count, int width, double value)
    {
        var row = new double[width];
        Array.Fill(row, value);
        var rows = new double[count][];
        Array.Fill(rows, row);
        return rows;
    }

    private static double[,] Square(double[] columnMajor, int n)
    {
        var square = new double[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                square[r, c] = columnMajor[r + (c * n)];
            }
        }

        return square;
    }

    private static JgsValue FromDense(double[,] matrix)
    {
        int rows = matrix.GetLength(0);
        int columns = matrix.GetLength(1);
        var flat = new double[rows * columns];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                flat[r + (c * rows)] = matrix[r, c];
            }
        }

        return JgsMatrix.FromColumnMajor(flat, rows, columns);
    }

    /// <summary>A column of answers, collapsed to a number when a single observation was asked about.</summary>
    private static JgsValue ColumnOfAnswers(double[] values) =>
        values.Length == 1 ? JgsValue.Number(values[0]) : JgsMatrix.FromColumnMajor(values, values.Length, 1);

    /// <summary>Turns a refusal from the numeric layer into a script-level error naming the builtin.</summary>
    private static T Guarded<T>(string name, Func<T> body, int line, int col)
    {
        try
        {
            return body();
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"{name}: {ex.Message}");
        }
    }
}
