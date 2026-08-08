using JGraph.Statistics.Distributions;
using JGraph.Statistics.Regression;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M53 wave G, part three: the penalized paths, partial least squares, the multinomial fit, the
/// multivariate fit and proportional hazards.
/// </summary>
/// <remarks>
/// These are the names whose answer is a family rather than a single fit — a path of penalties, a
/// sequence of components, one coefficient set per response category. What they share is that the
/// interesting output is a matrix whose columns are ordered, and every one of them puts that order the
/// way MathWorks does even where another order would read better.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// <c>[B, FitInfo] = lasso(X, y, …)</c> and <c>lassoglm(X, y, distr, …)</c>: the whole path of
    /// penalized fits, from the one that keeps everything to the one that keeps nothing.
    /// </summary>
    private static JgsValue[] PenalizedPath(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        bool generalized = name == "lassoglm";
        ParsedArgs parsed = (PenalizedOptions with { Builtin = name })
            .Parse(args, generalized ? 3 : 2, line, col);
        if (parsed.Positional.Count < 2)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}(X, y) needs the predictors and the response.");
        }

        double[,] predictors = AsRectangle(name, parsed.Positional[0], line, col);
        double mixing = parsed.Scalar("Alpha", 1);
        double[]? lambda = parsed.Vector("Lambda");
        var plan = new PenalizedRegression.PathPlan(
            parsed.Whole("NumLambda", 100), parsed.Scalar("LambdaRatio", 1e-4));
        bool standardize = parsed.Named("Standardize") is { } scaling
            ? OnOrOff(name, scaling, "Standardize", line, col)
            : true;
        double tolerance = parsed.Scalar("RelTol", 1e-8);
        int maximumDf = parsed.Whole("DFmax", 0);

        if (parsed.Named("PredictorNames") is not null)
        {
            // The names would only be echoed back; nothing here reports by name.
            throw new JgsRuntimeException(line, col,
                $"{name}: 'PredictorNames' has nothing to label — the answer is a matrix of "
                + "coefficients in the order the predictors were given.");
        }

        PenalizedRegression.Path path;
        string criterion;
        if (generalized)
        {
            GlmFamily family = parsed.Positional.Count > 2
                ? Family(name, parsed.Positional[2], line, col)
                : GlmFamily.Normal;
            (GlmLink link, double power) = CanonicalOrNamed(name, parsed, family, line, col);
            (double[] response, double[]? trials) =
                BinomialResponse(name, parsed.Positional[1], family, line, col);
            double[]? offset = parsed.Vector("Offset");

            path = Guarded(
                name,
                () => PenalizedRegression.FitGeneralized(
                    predictors, response, family, link, power, mixing, lambda, plan, standardize,
                    trials, offset, tolerance, maximumDf),
                line,
                col);
            criterion = "Deviance";
        }
        else
        {
            double[] y = FlattenColumnMajor(name, parsed.Positional[1], line, col);
            double[]? weights = parsed.Vector("Weights");
            path = Guarded(
                name,
                () => PenalizedRegression.Fit(
                    predictors, y, mixing, lambda, plan, standardize, weights, tolerance, maximumDf),
                line,
                col);
            criterion = "MSE";
        }

        var degrees = new double[path.Df.Length];
        for (int i = 0; i < degrees.Length; i++)
        {
            degrees[i] = path.Df[i];
        }

        // Without cross-validation MathWorks reports the criterion on the data the fit saw, which is
        // the fit's own error and is not a basis for choosing a penalty. Asking for folds is what
        // turns it into one, and the two extra fields only exist then.
        var fields = new List<(string, JgsValue)>
        {
            ("Intercept", RowVector(path.Intercepts)),
            ("Lambda", RowVector(path.Lambda)),
            ("Alpha", JgsValue.Number(mixing)),
            ("DF", RowVector(degrees)),
            (criterion, RowVector(path.Criterion)),
        };

        if (parsed.Named("CV") is { } folds)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: cross-validation over {Describe(folds)} is not computed here; the path and "
                + "its penalties are, and a fold loop over them is a few lines of script.");
        }

        return Outputs(wanted, Rectangle(path.Coefficients), Structure([.. fields]));
    }

    private static string Describe(JgsValue value) =>
        value.Type == JgsType.Number ? $"{value.AsNumber} folds" : "the folds asked for";

    // --- plsregress ----------------------------------------------------------------------------------

    /// <summary>
    /// <c>[XL, YL, XS, YS, BETA, PCTVAR, MSE, stats] = plsregress(X, Y, ncomp)</c>: a regression through
    /// directions chosen to explain the response.
    /// </summary>
    private static JgsValue[] PartialLeastSquaresFit(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = PartialLeastSquaresOptions.Parse(args, 3, line, col);
        if (parsed.Positional.Count < 2)
        {
            throw new JgsRuntimeException(line, col,
                "plsregress(X, Y, ncomp) needs the predictors and the responses.");
        }

        double[,] x = AsRectangle("plsregress", parsed.Positional[0], line, col);
        double[,] y = AsRectangle("plsregress", parsed.Positional[1], line, col);
        int components = parsed.Positional.Count > 2
            ? Count("plsregress", parsed.Positional, 2, line, col)
            : Math.Min(x.GetLength(0) - 1, x.GetLength(1));

        if (parsed.Named("CV") is not null)
        {
            throw new JgsRuntimeException(line, col,
                "plsregress: the error table here is the fit's own, not a cross-validated one; "
                + "'CV' would report a different quantity under the same name.");
        }

        PartialLeastSquares.PlsFit fit = Guarded(
            "plsregress", () => PartialLeastSquares.Fit(x, y, components), line, col);

        JgsValue explained = JgsMatrix.Build(
            2, components, (r, c) => r == 0 ? fit.ExplainedX[c] : fit.ExplainedY[c]);
        JgsValue error = JgsMatrix.Build(
            2, components + 1, (r, c) => r == 0 ? fit.XMeanSquaredError[c] : fit.YMeanSquaredError[c]);

        JgsValue stats = Structure(
            ("W", Rectangle(fit.Weights)),
            ("T2", ColumnOfAnswers(fit.T2)),
            ("Xresiduals", Rectangle(fit.XResiduals)),
            ("Yresiduals", Rectangle(fit.YResiduals)));

        return Outputs(
            wanted,
            Rectangle(fit.XLoadings),
            Rectangle(fit.YLoadings),
            Rectangle(fit.XScores),
            Rectangle(fit.YScores),
            Rectangle(fit.Beta),
            explained,
            error,
            stats);
    }

    // --- mnrfit and mnrval ----------------------------------------------------------------------------

    /// <summary>
    /// <c>[B, dev, stats] = mnrfit(X, Y, …)</c>: a logistic regression for a response of several
    /// categories.
    /// </summary>
    private static JgsValue[] MultinomialFit(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = MultinomialOptions.Parse(args, 2, line, col);
        if (parsed.Positional.Count != 2)
        {
            throw new JgsRuntimeException(line, col, "mnrfit(X, Y) needs the predictors and the response.");
        }

        double[,] predictors = AsRectangle("mnrfit", parsed.Positional[0], line, col);
        double[,] counts = CategoryCounts("mnrfit", parsed.Positional[1], predictors.GetLength(0), line, col);
        (MultinomialModel model, GlmLink link, bool separate) = MultinomialShape(parsed, line, col);

        MultinomialRegression.MultinomialFit fit = Guarded(
            "mnrfit",
            () => MultinomialRegression.Fit(predictors, counts, model, link, separate),
            line,
            col);

        JgsValue coefficients = MultinomialCoefficients(
            fit.Coefficients, counts.GetLength(1), predictors.GetLength(1), separate);

        if (wanted <= 1)
        {
            return [coefficients];
        }

        JgsValue stats = Structure(
            ("beta", ColumnOfAnswers(fit.Coefficients)),
            ("dfe", JgsValue.Number(fit.Df)),
            ("s", JgsValue.Number(1)),
            ("estdisp", JgsValue.Bool(false)),
            ("covb", Rectangle(fit.Covariance)),
            ("se", ColumnOfAnswers(fit.StandardErrors)),
            ("coeffcorr", Rectangle(Correlations(fit.Covariance))),
            ("t", ColumnOfAnswers(fit.T)),
            ("p", ColumnOfAnswers(fit.P)),
            ("model", JgsValue.Str(model.ToString().ToLowerInvariant())),
            ("link", JgsValue.Str(LinkName(link))),
            ("interactions", JgsValue.Bool(separate)),
            ("categories", JgsValue.Number(counts.GetLength(1))),
            ("predictors", JgsValue.Number(predictors.GetLength(1))));

        return Outputs(wanted, coefficients, JgsValue.Number(fit.Deviance), stats);
    }

    /// <summary>
    /// <c>[pihat, dlow, dhi] = mnrval(B, X, …)</c>: the probability of each category at each row.
    /// </summary>
    private static JgsValue[] MultinomialValue(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = MultinomialValueOptions.Parse(args, 3, line, col);
        if (parsed.Positional.Count < 2)
        {
            throw new JgsRuntimeException(line, col,
                "mnrval(B, X) needs the coefficients and the rows to evaluate at.");
        }

        double[,] given = AsRectangle("mnrval", parsed.Positional[0], line, col);
        double[,] predictors = AsRectangle("mnrval", parsed.Positional[1], line, col);
        (MultinomialModel model, GlmLink link, bool separate) = MultinomialShape(parsed, line, col);

        int p = predictors.GetLength(1);
        (double[] theta, int categories) = FlatCoefficients("mnrval", given, p, separate, line, col);

        double[,] probabilities = Guarded(
            "mnrval",
            () => MultinomialRegression.Probabilities(predictors, theta, model, link, separate, categories, p),
            line,
            col);

        string kind = parsed.Word("type", "category", "category", "cumulative", "conditional");
        double[,] answer = kind switch
        {
            "cumulative" => MultinomialRegression.Cumulative(probabilities),
            "conditional" => MultinomialRegression.Conditional(probabilities),
            _ => probabilities,
        };

        if (wanted <= 1)
        {
            return [Rectangle(answer)];
        }

        if (parsed.Positional.Count < 3 || parsed.Positional[2].Type != JgsType.Struct)
        {
            throw new JgsRuntimeException(line, col,
                "mnrval: the interval needs the record mnrfit produced — "
                + "[p, lo, hi] = mnrval(B, X, stats).");
        }

        Dictionary<string, JgsValue> stats = parsed.Positional[2].AsStruct;
        if (!stats.TryGetValue("covb", out JgsValue? covb))
        {
            throw new JgsRuntimeException(line, col, "mnrval: the record from mnrfit is missing 'covb'.");
        }

        double[,] covariance = AsRectangle("mnrval", covb, line, col);
        double confidence = parsed.Scalar("confidence", 0.95);
        int df = stats.TryGetValue("dfe", out JgsValue? dfe) ? (int)dfe.AsNumber : 0;
        double critical = df > 0
            ? ContinuousDistributions.TInv(1 - ((1 - confidence) / 2), df)
            : ContinuousDistributions.NormalInv(1 - ((1 - confidence) / 2), 0, 1);

        // A probability is a smooth function of the coefficients, so its spread is the coefficients'
        // covariance seen through that function's own slope — taken by differencing, because the
        // three models bend it three different ways.
        double[,] spread = ProbabilitySpread(
            predictors, theta, model, link, separate, categories, p, covariance, kind);

        int rows = answer.GetLength(0);
        int columns = answer.GetLength(1);
        JgsValue half = JgsMatrix.Build(rows, columns, (r, c) => critical * spread[r, c]);
        return Outputs(wanted, Rectangle(answer), half, half);
    }

    /// <summary>The standard error of each reported probability, by differencing through the model.</summary>
    private static double[,] ProbabilitySpread(
        double[,] predictors,
        double[] theta,
        MultinomialModel model,
        GlmLink link,
        bool separate,
        int categories,
        int p,
        double[,] covariance,
        string kind)
    {
        double[,] Report(double[] parameters)
        {
            double[,] raw = MultinomialRegression.Probabilities(
                predictors, parameters, model, link, separate, categories, p);
            return kind switch
            {
                "cumulative" => MultinomialRegression.Cumulative(raw),
                "conditional" => MultinomialRegression.Conditional(raw),
                _ => raw,
            };
        }

        double[,] at = Report(theta);
        int rows = at.GetLength(0);
        int columns = at.GetLength(1);
        int size = theta.Length;
        var slopes = new double[rows, columns, size];
        for (int a = 0; a < size; a++)
        {
            double step = 1e-6 * Math.Max(1, Math.Abs(theta[a]));
            var moved = (double[])theta.Clone();
            moved[a] += step;
            double actual = moved[a] - theta[a];
            double[,] shifted = Report(moved);
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    slopes[r, c, a] = (shifted[r, c] - at[r, c]) / actual;
                }
            }
        }

        var spread = new double[rows, columns];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                double variance = 0;
                for (int a = 0; a < size; a++)
                {
                    for (int b = 0; b < size; b++)
                    {
                        variance += slopes[r, c, a] * covariance[a, b] * slopes[r, c, b];
                    }
                }

                spread[r, c] = Math.Sqrt(Math.Max(0, variance));
            }
        }

        return spread;
    }

    /// <summary>Which of the three models, which link, and whether each category gets its own slopes.</summary>
    private static (MultinomialModel Model, GlmLink Link, bool Separate) MultinomialShape(
        ParsedArgs parsed, int line, int col)
    {
        string word = parsed.Word("model", "nominal", "nominal", "ordinal", "hierarchical");
        MultinomialModel model = word switch
        {
            "ordinal" => MultinomialModel.Ordinal,
            "hierarchical" => MultinomialModel.Hierarchical,
            _ => MultinomialModel.Nominal,
        };

        // A nominal model compares each category with a reference, so separate slopes are its natural
        // reading; the ordered models share one set unless told otherwise.
        bool separate = model == MultinomialModel.Nominal;
        if (parsed.Named("interactions") is { } given)
        {
            separate = OnOrOff("mnrfit", given, "interactions", line, col);
            if (model == MultinomialModel.Nominal && !separate)
            {
                separate = false;
            }
        }

        GlmLink link = GlmLink.Logit;
        if (parsed.Named("link") is { } named)
        {
            (link, _) = LinkFunction("mnrfit", named, line, col);
        }

        return (model, link, separate);
    }

    /// <summary>The response as a table of counts, however it was written.</summary>
    private static double[,] CategoryCounts(
        string name, JgsValue value, int observations, int line, int col)
    {
        double[,] given = AsRectangle(name, value, line, col);
        if (given.GetLength(1) > 1)
        {
            if (given.GetLength(0) != observations)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: the response has {given.GetLength(0)} rows for {observations} observations.");
            }

            return given;
        }

        // A single column names each observation's category, so the counts are the indicators.
        int n = given.GetLength(0);
        if (n != observations)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: the response has {n} values for {observations} observations.");
        }

        int most = 0;
        for (int i = 0; i < n; i++)
        {
            double category = given[i, 0];
            if (category != Math.Floor(category) || category < 1)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: a category is named by the whole numbers 1, 2, …, and {category} is not one.");
            }

            most = Math.Max(most, (int)category);
        }

        var counts = new double[n, Math.Max(2, most)];
        for (int i = 0; i < n; i++)
        {
            counts[i, (int)given[i, 0] - 1] = 1;
        }

        return counts;
    }

    /// <summary>The coefficients arranged the way the documentation shows them.</summary>
    private static JgsValue MultinomialCoefficients(
        double[] theta, int categories, int predictors, bool separate)
    {
        if (!separate)
        {
            return ColumnOfAnswers(theta);
        }

        // One column per category boundary, the intercept on top of that category's own slopes.
        return JgsMatrix.Build(
            predictors + 1,
            categories - 1,
            (r, c) => r == 0 ? theta[c] : theta[categories - 1 + (c * predictors) + r - 1]);
    }

    /// <summary>The flat coefficient vector a reported matrix stands for, and how many categories it implies.</summary>
    private static (double[] Theta, int Categories) FlatCoefficients(
        string name, double[,] given, int predictors, bool separate, int line, int col)
    {
        if (!separate)
        {
            int size = given.GetLength(0) * given.GetLength(1);
            var flat = new double[size];
            int at = 0;
            for (int c = 0; c < given.GetLength(1); c++)
            {
                for (int r = 0; r < given.GetLength(0); r++)
                {
                    flat[at++] = given[r, c];
                }
            }

            int categories = size - predictors + 1;
            if (categories < 2)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: {size} coefficients cannot describe {predictors} predictors and at least "
                    + "two categories.");
            }

            return (flat, categories);
        }

        if (given.GetLength(0) != predictors + 1)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: the coefficients have {given.GetLength(0)} rows for {predictors} predictors "
                + "and an intercept.");
        }

        int boundaries = given.GetLength(1);
        var theta = new double[boundaries + (boundaries * predictors)];
        for (int c = 0; c < boundaries; c++)
        {
            theta[c] = given[0, c];
            for (int j = 0; j < predictors; j++)
            {
                theta[boundaries + (c * predictors) + j] = given[j + 1, c];
            }
        }

        return (theta, boundaries + 1);
    }

    private static string LinkName(GlmLink link) => link switch
    {
        GlmLink.Probit => "probit",
        GlmLink.ComplementaryLogLog => "comploglog",
        GlmLink.LogLog => "loglog",
        _ => "logit",
    };

    // --- mvregress and mvregresslike -------------------------------------------------------------------

    /// <summary>
    /// <c>[beta, Sigma, E, CovB, logL] = mvregress(X, Y)</c>: several responses fitted together, so that
    /// what they leave over is allowed to be correlated.
    /// </summary>
    private static JgsValue[] MultivariateFit(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = MultivariateOptions.Parse(args, 2, line, col);
        if (parsed.Positional.Count != 2)
        {
            throw new JgsRuntimeException(line, col, "mvregress(X, Y) needs the design and the responses.");
        }

        double[,] responses = AsRectangle("mvregress", parsed.Positional[1], line, col);
        (double[][,] designs, bool shared) = Designs(
            "mvregress", parsed.Positional[0], responses.GetLength(0), responses.GetLength(1), line, col);

        int iterations = parsed.Whole("maxiter", 0);
        double tolerance = parsed.Scalar("tolbeta", 0);
        parsed.Word("algorithm", "ecm", "ecm", "cwls", "mvn");

        MultivariateRegression.MultivariateFit fit = Guarded(
            "mvregress",
            () => MultivariateRegression.Fit(designs, responses, iterations, tolerance),
            line,
            col);

        // A shared design means each response has its own coefficients over the same columns, and the
        // readable shape for that is one column of coefficients per response.
        int d = responses.GetLength(1);
        JgsValue coefficients = shared
            ? JgsMatrix.Build(
                fit.Coefficients.Length / d, d, (r, c) => fit.Coefficients[(c * (fit.Coefficients.Length / d)) + r])
            : ColumnOfAnswers(fit.Coefficients);

        return Outputs(
            wanted,
            coefficients,
            Rectangle(fit.Covariance),
            Rectangle(fit.Residuals),
            Rectangle(fit.CoefficientCovariance),
            JgsValue.Number(fit.LogLikelihood));
    }

    /// <summary>
    /// <c>[nlogL, COVB] = mvregresslike(X, Y, beta, Sigma, algorithm)</c>: how improbable the data is at
    /// stated parameters, and how precise those parameters are.
    /// </summary>
    private static JgsValue[] MultivariateLikelihood(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = (MultivariateOptions with { Builtin = "mvregresslike", StringPositionals = 5 })
            .Parse(args, 5, line, col);
        if (parsed.Positional.Count < 4)
        {
            throw new JgsRuntimeException(line, col,
                "mvregresslike(X, Y, beta, Sigma) needs the design, the responses, the coefficients and "
                + "the error covariance.");
        }

        double[,] responses = AsRectangle("mvregresslike", parsed.Positional[1], line, col);
        (double[][,] designs, _) = Designs(
            "mvregresslike", parsed.Positional[0], responses.GetLength(0), responses.GetLength(1), line, col);
        double[] beta = FlattenColumnMajor("mvregresslike", parsed.Positional[2], line, col);
        double[,] sigma = AsRectangle("mvregresslike", parsed.Positional[3], line, col);

        double logLikelihood = Guarded(
            "mvregresslike",
            () => MultivariateRegression.LogLikelihood(designs, responses, beta, sigma),
            line,
            col);

        if (wanted <= 1)
        {
            return [JgsValue.Number(-logLikelihood)];
        }

        double[,] coefficientCovariance = Guarded(
            "mvregresslike",
            () => MultivariateRegression.CoefficientCovariance(designs, sigma, beta.Length),
            line,
            col);

        string format = parsed.Word("varformat", "beta", "beta", "full");
        if (format == "beta")
        {
            return [JgsValue.Number(-logLikelihood), Rectangle(coefficientCovariance)];
        }

        // The full format adds the error covariance's own free elements below the coefficients. The
        // two blocks are independent for a normal model, so the matrix really is block diagonal
        // rather than merely reported as one.
        double[,] spread = MultivariateRegression.CovarianceOfCovariance(sigma, responses.GetLength(0));
        int k = beta.Length;
        int extra = spread.GetLength(0);
        JgsValue full = JgsMatrix.Build(
            k + extra,
            k + extra,
            (r, c) => r < k && c < k
                ? coefficientCovariance[r, c]
                : r >= k && c >= k
                    ? spread[r - k, c - k]
                    : 0);

        return [JgsValue.Number(-logLikelihood), full];
    }

    /// <summary>
    /// One design per observation, from either a shared design matrix or a cell array of them.
    /// </summary>
    private static (double[][,] Designs, bool Shared) Designs(
        string name, JgsValue value, int observations, int responses, int line, int col)
    {
        if (value.Type != JgsType.Cell)
        {
            double[,] shared = AsRectangle(name, value, line, col);
            if (shared.GetLength(0) != observations)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: the design has {shared.GetLength(0)} rows for {observations} observations.");
            }

            return (MultivariateRegression.Expand(shared, responses), true);
        }

        IReadOnlyList<JgsValue> cells = value.AsCell;
        if (cells.Count != observations)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: there are {cells.Count} designs for {observations} observations.");
        }

        var designs = new double[observations][,];
        for (int i = 0; i < observations; i++)
        {
            designs[i] = AsRectangle(name, cells[i], line, col);
        }

        return (designs, false);
    }

    // --- coxphfit -------------------------------------------------------------------------------------

    /// <summary>
    /// <c>[b, logl, H, stats] = coxphfit(X, T, …)</c>: how a set of predictors multiplies the rate at
    /// which something fails, without ever saying what that rate is.
    /// </summary>
    private static JgsValue[] HazardFit(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = HazardOptions.Parse(args, 2, line, col);
        if (parsed.Positional.Count != 2)
        {
            throw new JgsRuntimeException(line, col,
                "coxphfit(X, T) needs the predictors and the times at which each observation ended.");
        }

        double[,] predictors = AsRectangle("coxphfit", parsed.Positional[0], line, col);
        double[] times = FlattenColumnMajor("coxphfit", parsed.Positional[1], line, col);
        int n = predictors.GetLength(0);

        bool[]? censored = null;
        if (parsed.Vector("censoring") is { } flags)
        {
            if (flags.Length != n)
            {
                throw new JgsRuntimeException(line, col,
                    $"coxphfit: 'censoring' names {flags.Length} observations, and there are {n}.");
            }

            censored = new bool[n];
            for (int i = 0; i < n; i++)
            {
                censored[i] = flags[i] != 0;
            }
        }

        double[]? frequency = parsed.Vector("frequency");
        double[]? baseline = null;
        if (parsed.Named("baseline") is { } given && !IsPlaceholderValue(given))
        {
            double[] at = FlattenColumnMajor("coxphfit", given, line, col);
            baseline = at.Length == 1 && predictors.GetLength(1) > 1
                ? Filled(predictors.GetLength(1), at[0])
                : at;
        }

        double[]? start = parsed.Vector("init");
        TieHandling ties = parsed.Word("ties", "breslow", "breslow", "efron") == "efron"
            ? TieHandling.Efron
            : TieHandling.Breslow;

        ProportionalHazards.HazardFit fit = Guarded(
            "coxphfit",
            () => ProportionalHazards.Fit(predictors, times, censored, frequency, baseline, ties, start),
            line,
            col);

        JgsValue hazard = fit.Times.Length == 0
            ? JgsValue.Array([])
            : JgsMatrix.Build(
                fit.Times.Length, 2, (r, c) => c == 0 ? fit.Times[r] : fit.CumulativeHazard[r]);

        JgsValue stats = Structure(
            ("covb", Rectangle(fit.Covariance)),
            ("beta", ColumnOfAnswers(fit.Coefficients)),
            ("se", ColumnOfAnswers(fit.StandardErrors)),
            ("z", ColumnOfAnswers(fit.Z)),
            ("p", ColumnOfAnswers(fit.P)),
            ("csres", ColumnOfAnswers(fit.CoxSnell)),
            ("devres", ColumnOfAnswers(fit.Deviance)),
            ("martres", ColumnOfAnswers(fit.Martingale)),
            ("schres", Rectangle(fit.Schoenfeld)),
            ("scores", Rectangle(fit.Scores)));

        return Outputs(
            wanted,
            ColumnOfAnswers(fit.Coefficients),
            JgsValue.Number(fit.LogLikelihood),
            hazard,
            stats);
    }

    private static double[] Filled(int count, double value)
    {
        var filled = new double[count];
        Array.Fill(filled, value);
        return filled;
    }
}
