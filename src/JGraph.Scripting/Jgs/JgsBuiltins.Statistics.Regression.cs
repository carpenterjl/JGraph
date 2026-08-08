using JGraph.Numerics.LinearAlgebra;
using JGraph.Statistics.Regression;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M53 wave G, part one: the linear model and its neighbours — <c>regress</c> and the diagnostics
/// <c>regstats</c> reports, the two ways of building a design matrix, the penalized and robust fits,
/// the generalized linear model, and the stepwise search.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these takes the design matrix the caller built, intercept column and all. That is
/// MathWorks' convention for <c>regress</c> and it is deliberately not smoothed over here: a model
/// with no intercept is a real model, and a function that quietly added one would make it
/// unreachable. The names that do add an intercept — <c>robustfit</c>, <c>glmfit</c>,
/// <c>stepwisefit</c> — are the ones whose documentation says they do, and each takes the word that
/// turns it off.
/// </para>
/// <para>
/// The outputs are again in MathWorks' order rather than a tidier one, and the structures carry the
/// field names their documentation lists, because a script that reads <c>stats.covb</c> is written
/// against those names and nothing else.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    private static readonly OptionSpec PolynomialConfidenceOptions = new(
        "polyconf", [], ["alpha", "mu", "predopt", "simopt"]);

    private static readonly OptionSpec InversePredictionOptions = new(
        "invpred", [], ["alpha", "predopt"]);

    private static readonly OptionSpec GeneralizedFitOptions = new(
        "glmfit",
        [],
        ["link", "estdisp", "weights", "offset", "constant", "B0", "options"],
        StringPositionals: 3);

    private static readonly OptionSpec GeneralizedValueOptions = new(
        "glmval", [], ["confidence", "size", "offset", "constant", "simultaneous"], StringPositionals: 3);

    private static readonly OptionSpec StepwiseOptions = new(
        "stepwisefit",
        [],
        ["penter", "premove", "display", "maxiter", "keep", "scale", "inmodel"]);

    /// <summary>Registers the regression builtins.</summary>
    private static void RegisterRegressionBuiltins(JgsEnvironment env)
    {
        void DefineBoth(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> both) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, (args, line, col) => both(args, 1, line, col)[0])
                { MultiOutput = both }));

        DefineBoth("regress", LinearRegress);
        DefineBoth("regstats", RegressionStatistics);
        env.Declare("leverage", JgsValue.Function(new BuiltinFunction("leverage", HatDiagonal)));
        env.Declare("ridge", JgsValue.Function(new BuiltinFunction("ridge", RidgeFit)));
        env.Declare("x2fx", JgsValue.Function(new BuiltinFunction("x2fx", TermsToDesign)));
        env.Declare("dummyvar", JgsValue.Function(new BuiltinFunction("dummyvar", GroupIndicators)));

        DefineBoth("polyconf", PolynomialConfidence);
        DefineBoth("invpred", InversePredict);
        DefineBoth("robustfit", RobustFit);
        DefineBoth("glmfit", GeneralizedFit);
        DefineBoth("glmval", GeneralizedValue);
        DefineBoth("stepwisefit", StepwiseFit);

        RegisterNonlinearRegressionBuiltins(env);
    }

    // --- regress -------------------------------------------------------------------------------------

    /// <summary>
    /// <c>[b, bint, r, rint, stats] = regress(y, X, alpha)</c>: the least-squares coefficients, an
    /// interval for each, the residuals, an interval for each of those, and the model's own test.
    /// </summary>
    private static JgsValue[] LinearRegress(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("regress", args, 2, 3, line, col);
        double[] y = FlattenColumnMajor("regress", args[0], line, col);
        double[,] design = AsRectangle("regress", args[1], line, col);
        double alpha = args.Count > 2 ? Num("regress", args, 2, line, col) : 0.05;

        LinearRegression.Regression fit = Guarded(
            "regress", () => LinearRegression.Regress(y, design, alpha), line, col);

        JgsValue interval = JgsMatrix.Build(
            fit.Coefficients.Length, 2, (r, c) => c == 0 ? fit.Lower[r] : fit.Upper[r]);
        JgsValue residualInterval = JgsMatrix.Build(
            fit.Residuals.Length, 2, (r, c) => c == 0 ? fit.ResidualLower[r] : fit.ResidualUpper[r]);

        return Outputs(
            wanted,
            ColumnOfAnswers(fit.Coefficients),
            interval,
            ColumnOfAnswers(fit.Residuals),
            residualInterval,
            RowVector([fit.RSquare, fit.F, fit.P, fit.ErrorVariance]));
    }

    // --- regstats ------------------------------------------------------------------------------------

    /// <summary>Every field <c>regstats</c> can report, in the order MathWorks documents them.</summary>
    private static readonly string[] StatisticsFields =
    [
        "Q", "R", "beta", "covb", "yhat", "r", "mse", "rsquare", "adjrsquare", "leverage", "hatmat",
        "s2_i", "beta_i", "standres", "studres", "dfbetas", "dffit", "dffits", "covratio", "cookd",
        "tstat", "fstat", "dwstat",
    ];

    /// <summary>
    /// <c>stats = regstats(y, X, model, whichstats)</c>: one fit, described every way the documentation
    /// lists. Naming one statistic answers it directly; naming several answers a structure of them.
    /// </summary>
    private static JgsValue[] RegressionStatistics(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("regstats", args, 2, 4, line, col);
        double[] y = FlattenColumnMajor("regstats", args[0], line, col);
        double[,] predictors = AsRectangle("regstats", args[1], line, col);
        double[,] design = ModelDesign("regstats", predictors, args, 2, line, col);

        // Which statistics were asked for is settled before anything is fitted, so a misspelled name
        // is reported as a misspelled name rather than behind whatever the fit happens to complain
        // about first.
        string[] asked = RequestedFields(args, line, col);
        LinearRegression.Diagnostics stats = Guarded(
            "regstats", () => LinearRegression.Describe(y, design), line, col);
        var built = new List<JgsValue>();
        foreach (string field in asked)
        {
            built.Add(StatisticValue(field, stats, design, line, col));
        }

        // A single named statistic is answered on its own; anything else is a structure, unless the
        // call asked for several outputs, which takes them one per output in the order named.
        if (asked.Length == 1 && args.Count > 3)
        {
            return [built[0]];
        }

        if (wanted > 1 && args.Count > 3)
        {
            return [.. built[..Math.Min(wanted, built.Count)]];
        }

        var fields = new (string, JgsValue)[asked.Length];
        for (int i = 0; i < asked.Length; i++)
        {
            fields[i] = (asked[i], built[i]);
        }

        return [Structure(fields)];
    }

    /// <summary>Which statistics the call asked for, defaulting to all of them.</summary>
    private static string[] RequestedFields(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count <= 3 || IsPlaceholderValue(args[3]))
        {
            return StatisticsFields;
        }

        var asked = new List<string>();
        if (args[3].Type == JgsType.String)
        {
            asked.Add(args[3].AsString);
        }
        else if (args[3].Type == JgsType.Cell)
        {
            foreach (JgsValue entry in args[3].AsCell)
            {
                if (entry.Type != JgsType.String)
                {
                    throw new JgsRuntimeException(line, col,
                        "regstats: the statistics to report are named by word.");
                }

                asked.Add(entry.AsString);
            }
        }
        else
        {
            throw new JgsRuntimeException(line, col,
                "regstats: the fourth argument names which statistics to report.");
        }

        var canonical = new List<string>();
        foreach (string name in asked)
        {
            if (string.Equals(name, "all", StringComparison.OrdinalIgnoreCase))
            {
                return StatisticsFields;
            }

            string? found = null;
            foreach (string field in StatisticsFields)
            {
                if (string.Equals(field, name, StringComparison.Ordinal))
                {
                    found = field;
                    break;
                }
            }

            canonical.Add(found ?? throw new JgsRuntimeException(line, col,
                $"regstats: there is no statistic called '{name}' "
                + $"(expected one of '{string.Join("', '", StatisticsFields)}')."));
        }

        return [.. canonical];
    }

    /// <summary>One named statistic, read off the single fit.</summary>
    private static JgsValue StatisticValue(
        string field, LinearRegression.Diagnostics stats, double[,] design, int line, int col)
    {
        switch (field)
        {
            case "Q":
                {
                    double[,] q = QrDecomposition.Factor(design).Q;
                    return Rectangle(q);
                }

            case "R":
                return Rectangle(QrDecomposition.Factor(design).R);
            case "beta":
                return ColumnOfAnswers(stats.Fit.Coefficients);
            case "covb":
                return Rectangle(stats.Fit.Covariance);
            case "yhat":
                return ColumnOfAnswers(stats.Fit.Fitted);
            case "r":
                return ColumnOfAnswers(stats.Fit.Residuals);
            case "mse":
                return JgsValue.Number(stats.Fit.MeanSquaredError);
            case "rsquare":
                return JgsValue.Number(stats.RSquare);
            case "adjrsquare":
                return JgsValue.Number(stats.AdjustedRSquare);
            case "leverage":
                return ColumnOfAnswers(stats.Fit.Leverage);
            case "hatmat":
                return Rectangle(stats.HatMatrix);
            case "s2_i":
                return ColumnOfAnswers(stats.DeletedVariance);
            case "beta_i":
                return Rectangle(stats.DeletedCoefficients);
            case "standres":
                return ColumnOfAnswers(stats.StandardizedResiduals);
            case "studres":
                return ColumnOfAnswers(stats.StudentizedResiduals);
            case "dfbetas":
                return Rectangle(stats.DfBetas);
            case "dffit":
                return ColumnOfAnswers(stats.DfFit);
            case "dffits":
                return ColumnOfAnswers(stats.DfFits);
            case "covratio":
                return ColumnOfAnswers(stats.CovarianceRatio);
            case "cookd":
                return ColumnOfAnswers(stats.CooksDistance);
            case "tstat":
                return Structure(
                    ("beta", ColumnOfAnswers(stats.Fit.Coefficients)),
                    ("se", ColumnOfAnswers(stats.StandardErrors)),
                    ("t", ColumnOfAnswers(stats.T)),
                    ("pval", ColumnOfAnswers(stats.TProbability)),
                    ("dfe", JgsValue.Number(stats.Fit.Df)));
            case "fstat":
                return Structure(
                    ("sse", JgsValue.Number(stats.Fit.ResidualSumOfSquares)),
                    ("ssr", JgsValue.Number(stats.RegressionSumOfSquares)),
                    ("dfr", JgsValue.Number(Math.Max(0, stats.Fit.Rank - 1))),
                    ("dfe", JgsValue.Number(stats.Fit.Df)),
                    ("f", JgsValue.Number(stats.ModelF)),
                    ("pval", JgsValue.Number(stats.ModelP)));
            case "dwstat":
                return Structure(
                    ("dw", JgsValue.Number(stats.DurbinWatsonStatistic)),
                    ("pval", JgsValue.Number(stats.DurbinWatsonProbability)));
            default:
                throw new JgsRuntimeException(line, col, $"regstats: there is no statistic called '{field}'.");
        }
    }

    // --- leverage, ridge, x2fx, dummyvar ---------------------------------------------------------------

    /// <summary><c>h = leverage(data, model)</c>: how far each observation pulls its own fitted value.</summary>
    private static JgsValue HatDiagonal(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("leverage", args, 1, 2, line, col);
        double[,] predictors = AsRectangle("leverage", args[0], line, col);
        double[,] design = ModelDesign("leverage", predictors, args, 1, line, col);
        return ColumnOfAnswers(Guarded("leverage", () => LinearRegression.Leverage(design), line, col));
    }

    /// <summary><c>b = ridge(y, X, k, scaled)</c>: coefficients whose own size is part of what is minimized.</summary>
    private static JgsValue RidgeFit(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("ridge", args, 3, 4, line, col);
        double[] y = FlattenColumnMajor("ridge", args[0], line, col);
        double[,] predictors = AsRectangle("ridge", args[1], line, col);
        double[] penalties = FlattenColumnMajor("ridge", args[2], line, col);

        // MathWorks' fourth argument is a scaling flag whose default is 1, so the plain call answers
        // the standardized coefficients; a zero asks for them back on the original scale, with an
        // intercept written in front.
        bool scaled = args.Count <= 3 || IsPlaceholderValue(args[3]) || Num("ridge", args, 3, line, col) != 0;
        double[,] fitted = Guarded(
            "ridge", () => LinearRegression.Ridge(y, predictors, penalties, scaled), line, col);
        return Rectangle(fitted);
    }

    /// <summary><c>D = x2fx(X, model, categ, catlevels)</c>: the design matrix a model description names.</summary>
    private static JgsValue TermsToDesign(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("x2fx", args, 1, 4, line, col);
        double[,] predictors = AsRectangle("x2fx", args[0], line, col);

        var categorical = new List<int>();
        if (args.Count > 2 && !IsPlaceholderValue(args[2]))
        {
            foreach (double index in FlattenColumnMajor("x2fx", args[2], line, col))
            {
                if (index != Math.Floor(index) || index < 1 || index > predictors.GetLength(1))
                {
                    throw new JgsRuntimeException(line, col,
                        $"x2fx: {index} does not name one of the {predictors.GetLength(1)} predictors.");
                }

                categorical.Add((int)index - 1);
            }
        }

        List<int[]> terms = ModelTerms("x2fx", predictors.GetLength(1), args, 1, line, col);
        return Rectangle(Guarded(
            "x2fx",
            () => categorical.Count == 0
                ? DesignMatrix.Expand(predictors, terms)
                : DesignMatrix.Expand(predictors, terms, categorical),
            line,
            col));
    }

    /// <summary><c>D = dummyvar(group)</c>: an indicator column for every level of every grouping column.</summary>
    private static JgsValue GroupIndicators(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("dummyvar", args, 1, line, col);
        double[,] groups = AsRectangle("dummyvar", args[0], line, col);
        return Rectangle(Guarded("dummyvar", () => DesignMatrix.Indicators(groups), line, col));
    }

    /// <summary>The model description at <paramref name="slot"/>, as a list of exponent rows.</summary>
    private static List<int[]> ModelTerms(
        string name, int predictors, IReadOnlyList<JgsValue> args, int slot, int line, int col)
    {
        if (args.Count <= slot || IsPlaceholderValue(args[slot]))
        {
            return DesignMatrix.Terms(ModelShape.Linear, predictors);
        }

        if (args[slot].Type == JgsType.String)
        {
            string word = args[slot].AsString;
            ModelShape shape = word.ToLowerInvariant() switch
            {
                "linear" => ModelShape.Linear,
                "interaction" => ModelShape.Interaction,
                "quadratic" => ModelShape.Quadratic,
                "purequadratic" => ModelShape.PureQuadratic,
                _ => throw new JgsRuntimeException(line, col,
                    $"{name}: there is no model called '{word}' "
                    + "(expected 'linear', 'interaction', 'quadratic' or 'purequadratic')."),
            };

            return DesignMatrix.Terms(shape, predictors);
        }

        // A matrix of exponents says exactly which terms are wanted: one row per term, holding the
        // power each predictor is raised to. A row of zeros is the intercept.
        double[,] written = AsRectangle(name, args[slot], line, col);
        if (written.GetLength(1) != predictors)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: the model matrix has {written.GetLength(1)} columns for {predictors} predictors.");
        }

        var terms = new List<int[]>();
        for (int r = 0; r < written.GetLength(0); r++)
        {
            var powers = new int[predictors];
            for (int c = 0; c < predictors; c++)
            {
                double power = written[r, c];
                if (power != Math.Floor(power) || power < 0)
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name}: a model term raises a predictor to a whole power that is not negative, "
                        + $"and {power} is not one.");
                }

                powers[c] = (int)power;
            }

            terms.Add(powers);
        }

        return terms;
    }

    /// <summary>The design matrix a named model over these predictors expands to.</summary>
    private static double[,] ModelDesign(
        string name, double[,] predictors, IReadOnlyList<JgsValue> args, int slot, int line, int col)
    {
        List<int[]> terms = ModelTerms(name, predictors.GetLength(1), args, slot, line, col);
        return Guarded(name, () => DesignMatrix.Expand(predictors, terms), line, col);
    }

    // --- polyconf and invpred ---------------------------------------------------------------------------

    /// <summary>
    /// <c>[y, delta] = polyconf(p, x, S, …)</c>: a polynomial evaluated with an interval around it,
    /// from the record <c>polyfit</c> produced.
    /// </summary>
    private static JgsValue[] PolynomialConfidence(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = PolynomialConfidenceOptions.Parse(args, 3, line, col);
        if (parsed.Positional.Count < 2)
        {
            throw new JgsRuntimeException(line, col,
                "polyconf(p, x) evaluates a polynomial; polyconf(p, x, S) adds the interval around it.");
        }

        double[] coefficients = FlattenColumnMajor("polyconf", parsed.Positional[0], line, col);
        double[] at = FlattenColumnMajor("polyconf", parsed.Positional[1], line, col);
        double alpha = parsed.Scalar("alpha", 0.05);
        bool observation = parsed.Word("predopt", "observation", "observation", "curve") == "observation";
        bool simultaneous = parsed.Word("simopt", "off", "on", "off") == "on";

        double centre = 0, scale = 1;
        if (parsed.Vector("mu") is { Length: 2 } centring)
        {
            centre = centring[0];
            scale = centring[1];
        }
        else if (parsed.Named("mu") is not null)
        {
            throw new JgsRuntimeException(line, col, "polyconf: 'mu' takes the centre and the scale.");
        }

        var scaled = new double[at.Length];
        for (int i = 0; i < at.Length; i++)
        {
            scaled[i] = (at[i] - centre) / scale;
        }

        var evaluated = new double[at.Length];
        for (int i = 0; i < at.Length; i++)
        {
            double value = 0;
            foreach (double coefficient in coefficients)
            {
                value = (value * scaled[i]) + coefficient;
            }

            evaluated[i] = value;
        }

        JgsValue answer = ShapedNumbers(evaluated, SizeDims(parsed.Positional[1]));
        if (wanted <= 1)
        {
            return [answer];
        }

        if (parsed.Positional.Count < 3 || parsed.Positional[2].Type != JgsType.Struct)
        {
            throw new JgsRuntimeException(line, col,
                "polyconf: the interval needs polyfit's record — [y, delta] = polyconf(p, x, S).");
        }

        Dictionary<string, JgsValue> record = parsed.Positional[2].AsStruct;
        foreach (string field in new[] { "R", "df", "normr" })
        {
            if (!record.ContainsKey(field))
            {
                throw new JgsRuntimeException(line, col,
                    $"polyconf: the record from polyfit is missing '{field}'.");
            }
        }

        int terms = coefficients.Length;
        double[,] triangular = AsRectangle("polyconf", record["R"], line, col);
        if (triangular.GetLength(0) != terms || triangular.GetLength(1) != terms)
        {
            throw new JgsRuntimeException(line, col,
                "polyconf: the record's R does not match the number of coefficients.");
        }

        var rows = new double[at.Length, terms];
        for (int r = 0; r < at.Length; r++)
        {
            double power = 1;
            for (int c = terms - 1; c >= 0; c--)
            {
                rows[r, c] = power;
                power *= scaled[r];
            }
        }

        double[] spread = Guarded(
            "polyconf",
            () => LinearRegression.PolynomialInterval(
                triangular, record["df"].AsNumber, record["normr"].AsNumber, rows, alpha,
                observation, simultaneous),
            line,
            col);

        return [answer, ShapedNumbers(spread, SizeDims(parsed.Positional[1]))];
    }

    /// <summary>
    /// <c>[x0, dxlo, dxup] = invpred(x, y, y0, …)</c>: the predictor value at which a straight-line fit
    /// would have produced <c>y0</c>.
    /// </summary>
    private static JgsValue[] InversePredict(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = InversePredictionOptions.Parse(args, 3, line, col);
        if (parsed.Positional.Count != 3)
        {
            throw new JgsRuntimeException(line, col,
                "invpred(x, y, y0) inverts a straight-line fit at y0.");
        }

        double[] x = FlattenColumnMajor("invpred", parsed.Positional[0], line, col);
        double[] y = FlattenColumnMajor("invpred", parsed.Positional[1], line, col);
        double[] targets = FlattenColumnMajor("invpred", parsed.Positional[2], line, col);
        double alpha = parsed.Scalar("alpha", 0.05);
        bool observation = parsed.Word("predopt", "observation", "observation", "curve") == "observation";

        var answers = new double[targets.Length];
        var below = new double[targets.Length];
        var above = new double[targets.Length];
        for (int i = 0; i < targets.Length; i++)
        {
            double target = targets[i];
            (double x0, double lower, double upper) = Guarded(
                "invpred",
                () => LinearRegression.InversePrediction(x, y, target, alpha, observation),
                line,
                col);
            answers[i] = x0;
            below[i] = x0 - lower;
            above[i] = upper - x0;
        }

        int[] shape = SizeDims(parsed.Positional[2]);
        return Outputs(
            wanted,
            ShapedNumbers(answers, shape),
            ShapedNumbers(below, shape),
            ShapedNumbers(above, shape));
    }

    // --- robustfit -----------------------------------------------------------------------------------

    /// <summary>
    /// <c>[b, stats] = robustfit(X, y, wfun, tune, const)</c>: least squares run again and again, each
    /// time trusting the observations the previous fit already explained.
    /// </summary>
    private static JgsValue[] RobustFit(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("robustfit", args, 2, 5, line, col);
        double[,] predictors = AsRectangle("robustfit", args[0], line, col);
        double[] y = FlattenColumnMajor("robustfit", args[1], line, col);

        RobustWeight weight = RobustWeight.Bisquare;
        if (args.Count > 2 && !IsPlaceholderValue(args[2]))
        {
            if (args[2].Type != JgsType.String)
            {
                throw new JgsRuntimeException(line, col,
                    "robustfit: the weight function is named by word — 'bisquare', 'huber' and so on.");
            }

            weight = WeightFunction(args[2].AsString, line, col);
        }

        double tuning = args.Count > 3 && !IsPlaceholderValue(args[3])
            ? Num("robustfit", args, 3, line, col)
            : 0;

        bool intercept = true;
        if (args.Count > 4 && !IsPlaceholderValue(args[4]))
        {
            intercept = OnOrOff("robustfit", args[4], "const", line, col);
        }

        double[,] design = intercept ? LeastSquares.WithIntercept(predictors) : predictors;
        RobustRegression.RobustFit fit = Guarded(
            "robustfit", () => RobustRegression.Fit(design, y, weight, tuning), line, col);

        if (wanted <= 1)
        {
            return [ColumnOfAnswers(fit.Coefficients)];
        }

        JgsValue stats = Structure(
            ("ols_s", JgsValue.Number(fit.OlsScale)),
            ("robust_s", JgsValue.Number(fit.RobustScale)),
            ("mad_s", JgsValue.Number(fit.MadScale)),
            ("s", JgsValue.Number(fit.Scale)),
            ("resid", ColumnOfAnswers(fit.Residuals)),
            ("rstud", ColumnOfAnswers(fit.StudentizedResiduals)),
            ("se", ColumnOfAnswers(fit.StandardErrors)),
            ("covb", Rectangle(fit.Covariance)),
            ("coeffcorr", Rectangle(Correlations(fit.Covariance))),
            ("t", ColumnOfAnswers(fit.T)),
            ("p", ColumnOfAnswers(fit.P)),
            ("w", ColumnOfAnswers(fit.Weights)),
            ("h", ColumnOfAnswers(fit.Leverage)),
            ("dfe", JgsValue.Number(fit.Df)),
            ("R", Rectangle(QrDecomposition.Factor(design).R)));

        return [ColumnOfAnswers(fit.Coefficients), stats];
    }

    /// <summary>The weight function a word names.</summary>
    private static RobustWeight WeightFunction(string word, int line, int col) =>
        word.ToLowerInvariant() switch
        {
            "andrews" => RobustWeight.Andrews,
            "bisquare" => RobustWeight.Bisquare,
            "cauchy" => RobustWeight.Cauchy,
            "fair" => RobustWeight.Fair,
            "huber" => RobustWeight.Huber,
            "logistic" => RobustWeight.Logistic,
            "ols" => RobustWeight.Ols,
            "talwar" => RobustWeight.Talwar,
            "welsch" => RobustWeight.Welsch,
            _ => throw new JgsRuntimeException(line, col,
                $"there is no weight function called '{word}' (expected one of 'andrews', 'bisquare', "
                + "'cauchy', 'fair', 'huber', 'logistic', 'ols', 'talwar', 'welsch')."),
        };

    // --- glmfit and glmval ---------------------------------------------------------------------------

    /// <summary>
    /// <c>[b, dev, stats] = glmfit(X, y, distr, …)</c>: a linear model whose response is counted,
    /// proportioned or positive rather than normal.
    /// </summary>
    private static JgsValue[] GeneralizedFit(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = GeneralizedFitOptions.Parse(args, 3, line, col);
        if (parsed.Positional.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "glmfit(X, y, distr) needs the predictors and the response.");
        }

        double[,] predictors = AsRectangle("glmfit", parsed.Positional[0], line, col);
        GlmFamily family = parsed.Positional.Count > 2
            ? Family("glmfit", parsed.Positional[2], line, col)
            : GlmFamily.Normal;

        (GlmLink link, double power) = CanonicalOrNamed("glmfit", parsed, family, line, col);
        bool intercept = parsed.Text("constant") is not { } word
            || OnOrOff("glmfit", JgsValue.Str(word), "constant", line, col);
        double[,] design = intercept ? LeastSquares.WithIntercept(predictors) : predictors;

        (double[] response, double[]? trials) =
            BinomialResponse("glmfit", parsed.Positional[1], family, line, col);
        double[]? weights = parsed.Vector("weights");
        double[]? offset = parsed.Vector("offset");
        // A binomial or a Poisson has no free scale, so its dispersion is one unless the caller says
        // to estimate it; the other three always estimate theirs.
        bool estimate = parsed.Named("estdisp") is { } given
            ? OnOrOff("glmfit", given, "estdisp", line, col)
            : GeneralizedLinear.EstimatesDispersionByDefault(family);

        GeneralizedLinear.GlmFit fit = Guarded(
            "glmfit",
            () => GeneralizedLinear.Fit(
                design, response, family, link, power, trials, weights, offset, estimate),
            line,
            col);

        if (wanted <= 1)
        {
            return [ColumnOfAnswers(fit.Coefficients)];
        }

        JgsValue stats = Structure(
            ("beta", ColumnOfAnswers(fit.Coefficients)),
            ("dfe", JgsValue.Number(fit.Df)),
            ("sfit", JgsValue.Number(fit.FittedDispersion)),
            ("s", JgsValue.Number(fit.Dispersion)),
            ("estdisp", JgsValue.Bool(estimate)),
            ("covb", Rectangle(fit.Covariance)),
            ("se", ColumnOfAnswers(fit.StandardErrors)),
            ("coeffcorr", Rectangle(Correlations(fit.Covariance))),
            ("t", ColumnOfAnswers(fit.T)),
            ("p", ColumnOfAnswers(fit.P)),
            ("resid", ColumnOfAnswers(fit.Residuals)),
            ("residp", ColumnOfAnswers(fit.PearsonResiduals)),
            ("residd", ColumnOfAnswers(fit.DevianceResiduals)),
            ("resida", ColumnOfAnswers(fit.AnscombeResiduals)));

        return Outputs(
            wanted, ColumnOfAnswers(fit.Coefficients), JgsValue.Number(fit.Deviance), stats);
    }

    /// <summary>
    /// <c>[yhat, dlo, dhi] = glmval(b, X, link, …)</c>: the mean the fit predicts, and how far the
    /// interval reaches either side of it.
    /// </summary>
    private static JgsValue[] GeneralizedValue(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = GeneralizedValueOptions.Parse(args, 4, line, col);
        if (parsed.Positional.Count < 3)
        {
            throw new JgsRuntimeException(line, col,
                "glmval(b, X, link) needs the coefficients, the rows to predict at and the link.");
        }

        double[] coefficients = FlattenColumnMajor("glmval", parsed.Positional[0], line, col);
        double[,] predictors = AsRectangle("glmval", parsed.Positional[1], line, col);
        (GlmLink link, double power) = LinkFunction("glmval", parsed.Positional[2], line, col);

        bool intercept = parsed.Text("constant") is not { } word
            || OnOrOff("glmval", JgsValue.Str(word), "constant", line, col);
        double[,] design = intercept ? LeastSquares.WithIntercept(predictors) : predictors;

        double[,]? covariance = null;
        int df = 0;
        if (parsed.Positional.Count > 3 && parsed.Positional[3].Type == JgsType.Struct)
        {
            Dictionary<string, JgsValue> stats = parsed.Positional[3].AsStruct;
            if (!stats.TryGetValue("covb", out JgsValue? covb))
            {
                throw new JgsRuntimeException(line, col,
                    "glmval: the record from glmfit is missing 'covb'.");
            }

            covariance = AsRectangle("glmval", covb, line, col);
            df = stats.TryGetValue("dfe", out JgsValue? dfe) ? (int)dfe.AsNumber : 0;
        }

        double confidence = parsed.Scalar("confidence", 0.95);
        double[]? offset = parsed.Vector("offset");
        bool simultaneous = parsed.Text("simultaneous") is { } together
            && OnOrOff("glmval", JgsValue.Str(together), "simultaneous", line, col);

        (double[] predicted, double[] lower, double[] upper) = Guarded(
            "glmval",
            () => GeneralizedLinear.Evaluate(
                coefficients, design, link, power, covariance, df, 1 - confidence, simultaneous, offset),
            line,
            col);

        // A binomial fit predicts a proportion; 'size' turns that back into an expected count.
        if (parsed.Vector("size") is { } trials)
        {
            Scale(predicted, trials, line, col);
            Scale(lower, trials, line, col);
            Scale(upper, trials, line, col);
        }

        if (wanted <= 1 || covariance is null)
        {
            if (wanted > 1)
            {
                throw new JgsRuntimeException(line, col,
                    "glmval: the interval needs the record glmfit produced — "
                    + "[y, lo, hi] = glmval(b, X, link, stats).");
            }

            return [ColumnOfAnswers(predicted)];
        }

        return Outputs(
            wanted, ColumnOfAnswers(predicted), ColumnOfAnswers(lower), ColumnOfAnswers(upper));
    }

    /// <summary>Multiplies predictions by the number of trials behind each of them.</summary>
    private static void Scale(double[] values, double[] trials, int line, int col)
    {
        if (trials.Length != values.Length && trials.Length != 1)
        {
            throw new JgsRuntimeException(line, col,
                $"glmval: 'size' names {trials.Length} trial counts for {values.Length} rows.");
        }

        for (int i = 0; i < values.Length; i++)
        {
            values[i] *= trials.Length == 1 ? trials[0] : trials[i];
        }
    }

    /// <summary>The error distribution a word names.</summary>
    private static GlmFamily Family(string name, JgsValue value, int line, int col)
    {
        if (value.Type != JgsType.String)
        {
            throw new JgsRuntimeException(line, col, $"{name}: the distribution is named by word.");
        }

        return value.AsString.ToLowerInvariant() switch
        {
            "normal" => GlmFamily.Normal,
            "binomial" => GlmFamily.Binomial,
            "poisson" => GlmFamily.Poisson,
            "gamma" => GlmFamily.Gamma,
            "inverse gaussian" or "inversegaussian" => GlmFamily.InverseGaussian,
            _ => throw new JgsRuntimeException(line, col,
                $"{name}: there is no distribution called '{value.AsString}' (expected 'normal', "
                + "'binomial', 'poisson', 'gamma' or 'inverse gaussian')."),
        };
    }

    /// <summary>The link a word — or a number, which means a power — names.</summary>
    private static (GlmLink Link, double Power) LinkFunction(
        string name, JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.Number)
        {
            return (GlmLink.Power, value.AsNumber);
        }

        if (value.Type != JgsType.String)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: the link is named by word, or by the power it raises the mean to.");
        }

        return value.AsString.ToLowerInvariant() switch
        {
            "identity" => (GlmLink.Identity, 0),
            "log" => (GlmLink.Log, 0),
            "logit" => (GlmLink.Logit, 0),
            "probit" => (GlmLink.Probit, 0),
            "comploglog" => (GlmLink.ComplementaryLogLog, 0),
            "loglog" => (GlmLink.LogLog, 0),
            "reciprocal" => (GlmLink.Reciprocal, 0),
            _ => throw new JgsRuntimeException(line, col,
                $"{name}: there is no link called '{value.AsString}' (expected 'identity', 'log', "
                + "'logit', 'probit', 'comploglog', 'loglog', 'reciprocal', or a power)."),
        };
    }

    /// <summary>The link named by the options, or the family's own.</summary>
    private static (GlmLink Link, double Power) CanonicalOrNamed(
        string name, ParsedArgs parsed, GlmFamily family, int line, int col) =>
        parsed.Named("link") is { } given
            ? LinkFunction(name, given, line, col)
            : GeneralizedLinear.CanonicalLink(family);

    /// <summary>
    /// The response as the fit wants it: a proportion beside its number of trials for a binomial,
    /// whatever was given for everything else. A two-column binomial response is a count and a total.
    /// </summary>
    private static (double[] Response, double[]? Trials) BinomialResponse(
        string name, JgsValue value, GlmFamily family, int line, int col)
    {
        if (family != GlmFamily.Binomial)
        {
            return (FlattenColumnMajor(name, value, line, col), null);
        }

        double[,] given = AsRectangle(name, value, line, col);
        int n = given.GetLength(0);
        if (given.GetLength(1) == 1)
        {
            var single = new double[n];
            for (int i = 0; i < n; i++)
            {
                single[i] = given[i, 0];
            }

            return (single, null);
        }

        if (given.GetLength(1) != 2)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: a binomial response is a proportion, or a count beside its number of trials.");
        }

        var proportions = new double[n];
        var trials = new double[n];
        for (int i = 0; i < n; i++)
        {
            trials[i] = given[i, 1];
            if (trials[i] <= 0)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: observation {i + 1} has no trials behind it.");
            }

            proportions[i] = given[i, 0] / trials[i];
        }

        return (proportions, trials);
    }

    // --- stepwisefit ---------------------------------------------------------------------------------

    /// <summary>
    /// <c>[b, se, pval, inmodel, stats, nextstep, history] = stepwisefit(X, y, …)</c>: which predictors
    /// belong in the model, found by adding and removing them one at a time.
    /// </summary>
    private static JgsValue[] StepwiseFit(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = StepwiseOptions.Parse(args, 2, line, col);
        if (parsed.Positional.Count != 2)
        {
            throw new JgsRuntimeException(line, col, "stepwisefit(X, y) needs the predictors and the response.");
        }

        double[,] predictors = AsRectangle("stepwisefit", parsed.Positional[0], line, col);
        double[] y = FlattenColumnMajor("stepwisefit", parsed.Positional[1], line, col);
        int terms = predictors.GetLength(1);

        double enter = parsed.Scalar("penter", 0.05);
        double remove = parsed.Scalar("premove", Math.Max(0.10, enter * 2));
        int budget = parsed.Whole("maxiter", 0);
        bool[]? start = Membership("stepwisefit", parsed.Named("inmodel"), terms, line, col);
        bool[]? keep = Membership("stepwisefit", parsed.Named("keep"), terms, line, col);
        DisplayOff("stepwisefit", parsed.Text("display"), line, col);

        StepwiseSelection.Selection chosen = Guarded(
            "stepwisefit",
            () => StepwiseSelection.Fit(predictors, y, enter, remove, start, keep, budget),
            line,
            col);

        // The history is one row per step: which terms were in the model after it, and what that
        // model cost. A step's own term is readable from the row before it, so it is not repeated.
        int steps = chosen.History.Count;
        JgsValue membership;
        if (steps == 0)
        {
            membership = JgsValue.Array([]);
        }
        else
        {
            var flags = new JgsValue[steps * terms];
            for (int s = 0; s < steps; s++)
            {
                for (int t = 0; t < terms; t++)
                {
                    flags[s + (t * steps)] = JgsValue.Bool(chosen.History[s].InModel[t]);
                }
            }

            membership = JgsValue.Array(flags);
            membership.ReshapeDims([steps, terms]);
        }

        JgsValue history = Structure(
            ("in", membership),
            ("rmse", ColumnOfAnswers([.. chosen.History.Select(static move => move.Rmse)])),
            ("df0", ColumnOfAnswers([.. chosen.History.Select(static move => (double)move.ModelDf)])));

        JgsValue stats = Structure(
            ("source", JgsValue.Str("stepwisefit")),
            ("dfe", JgsValue.Number(chosen.Df)),
            ("df0", JgsValue.Number(chosen.ModelDf)),
            ("SStotal", JgsValue.Number(chosen.TotalSumOfSquares)),
            ("SSresid", JgsValue.Number(chosen.ResidualSumOfSquares)),
            ("fstat", JgsValue.Number(chosen.F)),
            ("pval", JgsValue.Number(chosen.ModelP)),
            ("rmse", JgsValue.Number(chosen.Rmse)),
            ("xr", Rectangle(chosen.XResiduals)),
            ("yr", ColumnOfAnswers(chosen.YResiduals)),
            ("B", ColumnOfAnswers(chosen.Coefficients)),
            ("SE", ColumnOfAnswers(chosen.StandardErrors)),
            ("TSTAT", ColumnOfAnswers(Ratios(chosen.Coefficients, chosen.StandardErrors))),
            ("PVAL", ColumnOfAnswers(chosen.P)),
            ("intercept", JgsValue.Number(chosen.Intercept)),
            ("covb", Rectangle(chosen.Covariance)));

        return Outputs(
            wanted,
            ColumnOfAnswers(chosen.Coefficients),
            ColumnOfAnswers(chosen.StandardErrors),
            ColumnOfAnswers(chosen.P),
            LogicalRow(chosen.InModel),
            stats,
            JgsValue.Number(chosen.NextTerm + 1),
            history);
    }

    /// <summary>A logical vector option, read against a known number of terms.</summary>
    private static bool[]? Membership(string name, JgsValue? value, int terms, int line, int col)
    {
        if (value is null || IsPlaceholderValue(value))
        {
            return null;
        }

        double[] flags = FlattenColumnMajor(name, value, line, col);
        if (flags.Length != terms)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: the list names {flags.Length} terms, and there are {terms} predictors.");
        }

        var membership = new bool[terms];
        for (int i = 0; i < terms; i++)
        {
            membership[i] = flags[i] != 0;
        }

        return membership;
    }

    /// <summary>Refuses a request to draw, which nothing in this surface does.</summary>
    private static void DisplayOff(string name, string? word, int line, int col)
    {
        if (word is null || string.Equals(word, "off", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new JgsRuntimeException(line, col,
            $"{name}: 'display' can only be 'off' here — nothing in this surface is drawn, and the "
            + "numbers a display would show are the ones already answered.");
    }

    // --- shared helpers ------------------------------------------------------------------------------

    /// <summary>A rectangle of numbers, as a matrix value.</summary>
    private static JgsValue Rectangle(double[,] matrix) =>
        matrix.GetLength(0) == 0 || matrix.GetLength(1) == 0
            ? JgsValue.Array([])
            : JgsMatrix.Build(matrix.GetLength(0), matrix.GetLength(1), (r, c) => matrix[r, c]);

    /// <summary>A row of logical values.</summary>
    private static JgsValue LogicalRow(bool[] flags)
    {
        var elements = new JgsValue[flags.Length];
        for (int i = 0; i < flags.Length; i++)
        {
            elements[i] = JgsValue.Bool(flags[i]);
        }

        JgsValue row = JgsValue.Array(elements);
        row.ReshapeDims([1, flags.Length]);
        return row;
    }

    /// <summary>Each estimate over its own standard error.</summary>
    private static double[] Ratios(double[] estimates, double[] errors)
    {
        var ratios = new double[estimates.Length];
        for (int i = 0; i < estimates.Length; i++)
        {
            ratios[i] = errors[i] > 0 ? estimates[i] / errors[i] : double.NaN;
        }

        return ratios;
    }

    /// <summary>A covariance matrix rescaled into correlations.</summary>
    private static double[,] Correlations(double[,] covariance)
    {
        int k = covariance.GetLength(0);
        var correlations = new double[k, k];
        for (int a = 0; a < k; a++)
        {
            for (int b = 0; b < k; b++)
            {
                double scale = Math.Sqrt(Math.Max(0, covariance[a, a]) * Math.Max(0, covariance[b, b]));
                correlations[a, b] = scale > 0 ? covariance[a, b] / scale : double.NaN;
            }
        }

        return correlations;
    }

    /// <summary>An option that is written as the word 'on' or 'off', or as true or false.</summary>
    private static bool OnOrOff(string builtin, JgsValue value, string name, int line, int col)
    {
        if (value.Type == JgsType.Bool)
        {
            return value.AsBool;
        }

        if (value.Type == JgsType.Number)
        {
            return value.AsNumber != 0;
        }

        if (value.Type == JgsType.String)
        {
            string word = value.AsString;
            if (string.Equals(word, "on", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(word, "off", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        throw new JgsRuntimeException(line, col, $"{builtin}: '{name}' is either 'on' or 'off'.");
    }
}
