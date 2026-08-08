using JGraph.Statistics.Distributions;
using JGraph.Statistics.Optimize;
using JGraph.Statistics.Regression;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M53 wave G, part two: the fits that are not linear in their parameters, not fitted by squares, or
/// not fitted one response at a time — <c>nlinfit</c> and its two intervals, the penalized paths,
/// partial least squares, the multinomial and multivariate fits, and proportional hazards.
/// </summary>
/// <remarks>
/// <para>
/// <c>nlinfit</c> is the only name in the toolbox that takes the model as a function, so it is the
/// only one that calls back into the interpreter. The parameters are handed back in the shape the
/// starting point had, because a model written as <c>b(1)*exp(b(2)*x)</c> does not care but one
/// written with <c>b'</c> in it does.
/// </para>
/// <para>
/// The penalized paths report their penalties ascending and their coefficients in matching columns,
/// which is MathWorks' order and the readable one: the last column is the empty model and reading
/// right to left is the order the predictors earned their places.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    private static readonly OptionSpec NonlinearFitOptions = new(
        "nlinfit", [], ["Weights", "ErrorModel", "ErrorParameters"]);

    private static readonly OptionSpec ParameterIntervalOptions = new(
        "nlparci", [], ["alpha", "covar", "jacobian"]);

    private static readonly OptionSpec PredictionIntervalOptions = new(
        "nlpredci",
        [],
        ["alpha", "covar", "jacobian", "mse", "predopt", "simopt", "weights", "errormodelinfo"]);

    private static readonly OptionSpec PenalizedOptions = new(
        "lasso",
        [],
        [
            "Alpha", "CV", "DFmax", "Lambda", "LambdaRatio", "NumLambda", "MCReps", "PredictorNames",
            "RelTol", "Standardize", "Weights", "Options", "Link", "Offset", "MaxIter",
        ],
        StringPositionals: 3);

    private static readonly OptionSpec PartialLeastSquaresOptions = new(
        "plsregress", [], ["CV", "MCReps", "Options"]);

    private static readonly OptionSpec MultinomialOptions = new(
        "mnrfit", [], ["model", "interactions", "link", "estdisp"]);

    private static readonly OptionSpec MultinomialValueOptions = new(
        "mnrval", [], ["model", "interactions", "link", "type", "confidence"]);

    private static readonly OptionSpec MultivariateOptions = new(
        "mvregress",
        [],
        [
            "algorithm", "covar0", "beta0", "maxiter", "outputfcn", "tolbeta", "tolobj", "varformat",
            "vartype",
        ]);

    private static readonly OptionSpec HazardOptions = new(
        "coxphfit", [], ["baseline", "censoring", "frequency", "init", "options", "ties"]);

    /// <summary>Registers the nonlinear, penalized and multi-response regressions.</summary>
    private static void RegisterNonlinearRegressionBuiltins(JgsEnvironment env)
    {
        void DefineBoth(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> both) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, (args, line, col) => both(args, 1, line, col)[0])
                { MultiOutput = both }));

        DefineBoth("nlinfit", NonlinearFit);
        env.Declare("nlparci", JgsValue.Function(new BuiltinFunction("nlparci", ParameterInterval)));
        DefineBoth("nlpredci", NonlinearPrediction);
        env.Declare("hougen", JgsValue.Function(new BuiltinFunction("hougen", HougenModel)));

        DefineBoth("lasso", (args, wanted, line, col) => PenalizedPath("lasso", args, wanted, line, col));
        DefineBoth("lassoglm", (args, wanted, line, col) => PenalizedPath("lassoglm", args, wanted, line, col));
        DefineBoth("plsregress", PartialLeastSquaresFit);
        DefineBoth("mnrfit", MultinomialFit);
        DefineBoth("mnrval", MultinomialValue);
        DefineBoth("mvregress", MultivariateFit);
        DefineBoth("mvregresslike", MultivariateLikelihood);
        DefineBoth("coxphfit", HazardFit);
    }

    // --- nlinfit -------------------------------------------------------------------------------------

    /// <summary>
    /// <c>[beta, R, J, CovB, MSE] = nlinfit(X, y, modelfun, beta0, options)</c>: the parameters that
    /// bring a model of any shape closest to the data.
    /// </summary>
    private static JgsValue[] NonlinearFit(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = NonlinearFitOptions.Parse(args, 5, line, col);
        if (parsed.Positional.Count < 4)
        {
            throw new JgsRuntimeException(line, col,
                "nlinfit(X, y, modelfun, beta0) needs the predictors, the response, the model and a "
                + "starting point.");
        }

        double[] y = FlattenColumnMajor("nlinfit", parsed.Positional[1], line, col);
        double[] start = FlattenColumnMajor("nlinfit", parsed.Positional[3], line, col);
        Func<double[], double[]> model = ModelCaller(
            "nlinfit", parsed.Positional[2], parsed.Positional[0], parsed.Positional[3], y.Length, line, col);

        (LevenbergMarquardt.Settings settings, RobustWeight? robust, double tuning) =
            SearchSettings("nlinfit", parsed.Positional, 4, line, col);

        double[]? weights = parsed.Vector("Weights");
        string errorModel = parsed.Word("ErrorModel", "constant", "constant", "proportional", "exponential");

        // A proportional error model says the spread grows with the fitted value, which is the same
        // fit weighted by one over that value squared; an exponential one says the logarithm of the
        // response is what has constant spread, which is a fit of a different response entirely.
        NonlinearRegression.NonlinearFit fit;
        if (errorModel == "exponential")
        {
            var logged = new double[y.Length];
            for (int i = 0; i < y.Length; i++)
            {
                if (y[i] <= 0)
                {
                    throw new JgsRuntimeException(line, col,
                        "nlinfit: an exponential error model needs every response to be positive.");
                }

                logged[i] = Math.Log(y[i]);
            }

            fit = Guarded(
                "nlinfit",
                () => NonlinearRegression.Fit(
                    beta => Logged(model(beta), line, col), logged, start, weights, robust, tuning, settings),
                line,
                col);
        }
        else if (errorModel == "proportional")
        {
            double[] carried = weights ?? Ones(y.Length);
            fit = Guarded(
                "nlinfit",
                () => NonlinearRegression.Fit(model, y, start, carried, robust, tuning, settings),
                line,
                col);

            for (int round = 0; round < 10; round++)
            {
                double[] predicted = model(fit.Coefficients);
                var updated = new double[y.Length];
                for (int i = 0; i < y.Length; i++)
                {
                    double size = Math.Max(1e-12, Math.Abs(predicted[i]));
                    updated[i] = (weights is null ? 1 : weights[i]) / (size * size);
                }

                NonlinearRegression.NonlinearFit next = Guarded(
                    "nlinfit",
                    () => NonlinearRegression.Fit(
                        model, y, fit.Coefficients, updated, robust, tuning, settings),
                    line,
                    col);

                double movement = 0;
                for (int a = 0; a < start.Length; a++)
                {
                    movement = Math.Max(movement, Math.Abs(next.Coefficients[a] - fit.Coefficients[a]));
                }

                fit = next;
                if (movement <= 1e-10)
                {
                    break;
                }
            }
        }
        else
        {
            fit = Guarded(
                "nlinfit",
                () => NonlinearRegression.Fit(model, y, start, weights, robust, tuning, settings),
                line,
                col);
        }

        JgsValue coefficients = ShapedNumbers(fit.Coefficients, SizeDims(parsed.Positional[3]));
        JgsValue information = Structure(
            ("ErrorModel", JgsValue.Str(errorModel)),
            ("ErrorParameters", JgsValue.Number(Math.Sqrt(fit.MeanSquaredError))),
            ("ErrorVariance", JgsValue.Number(fit.MeanSquaredError)),
            ("MSE", JgsValue.Number(fit.MeanSquaredError)),
            ("ScheffeSimPred", JgsValue.Number(fit.Coefficients.Length)),
            ("WeightFunction", JgsValue.Bool(weights is not null)),
            ("FixedWeights", JgsValue.Bool(weights is not null)),
            ("RobustWeightFunction", JgsValue.Bool(robust is not null)));

        return Outputs(
            wanted,
            coefficients,
            ColumnOfAnswers(fit.Residuals),
            Rectangle(fit.Jacobian),
            Rectangle(fit.Covariance),
            JgsValue.Number(fit.MeanSquaredError),
            information);
    }

    /// <summary><c>ci = nlparci(beta, resid, 'covar', C)</c> or <c>'jacobian', J</c>.</summary>
    private static JgsValue ParameterInterval(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ParsedArgs parsed = ParameterIntervalOptions.Parse(args, 2, line, col);
        if (parsed.Positional.Count != 2)
        {
            throw new JgsRuntimeException(line, col,
                "nlparci(beta, resid, 'covar', C) needs the parameters and the residuals.");
        }

        double[] beta = FlattenColumnMajor("nlparci", parsed.Positional[0], line, col);
        double[] residuals = FlattenColumnMajor("nlparci", parsed.Positional[1], line, col);
        double alpha = parsed.Scalar("alpha", 0.05);
        int df = Math.Max(0, residuals.Length - beta.Length);
        double[,] covariance = CovarianceFrom("nlparci", parsed, residuals, beta.Length, line, col);

        (double[] lower, double[] upper) = Guarded(
            "nlparci",
            () => NonlinearRegression.ParameterInterval(beta, covariance, df, alpha),
            line,
            col);

        return JgsMatrix.Build(beta.Length, 2, (r, c) => c == 0 ? lower[r] : upper[r]);
    }

    /// <summary>
    /// <c>[ypred, delta] = nlpredci(modelfun, X, beta, R, …)</c>: the model's prediction at new rows and
    /// how far the interval reaches either side of it.
    /// </summary>
    private static JgsValue[] NonlinearPrediction(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = PredictionIntervalOptions.Parse(args, 4, line, col);
        if (parsed.Positional.Count != 4)
        {
            throw new JgsRuntimeException(line, col,
                "nlpredci(modelfun, X, beta, R, …) needs the model, the rows, the parameters and the "
                + "residuals of the fit.");
        }

        double[] beta = FlattenColumnMajor("nlpredci", parsed.Positional[2], line, col);
        double[] residuals = FlattenColumnMajor("nlpredci", parsed.Positional[3], line, col);
        double alpha = parsed.Scalar("alpha", 0.05);
        bool observation = parsed.Word("predopt", "curve", "curve", "observation") == "observation";
        bool simultaneous = parsed.Word("simopt", "off", "on", "off") == "on";
        int df = Math.Max(0, residuals.Length - beta.Length);

        double squared = 0;
        foreach (double residual in residuals)
        {
            squared += residual * residual;
        }

        double mse = parsed.Scalar("mse", df > 0 ? squared / df : 0);
        double[,] covariance = CovarianceFrom("nlpredci", parsed, residuals, beta.Length, line, col);

        Func<double[], double[]> model = ModelCaller(
            "nlpredci", parsed.Positional[0], parsed.Positional[1], parsed.Positional[2], -1, line, col);
        double[] predicted = model(beta);
        double[,] jacobian = Guarded(
            "nlpredci", () => NonlinearRegression.Jacobian(model, beta), line, col);

        double[] delta = Guarded(
            "nlpredci",
            () => NonlinearRegression.PredictionInterval(
                jacobian, covariance, mse, df, alpha, observation, simultaneous),
            line,
            col);

        return Outputs(wanted, ColumnOfAnswers(predicted), ColumnOfAnswers(delta));
    }

    /// <summary><c>hougen</c>: the reaction rate the documentation fits as its nonlinear example.</summary>
    private static JgsValue HougenModel(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("hougen", args, 2, line, col);
        double[] beta = FlattenColumnMajor("hougen", args[0], line, col);
        double[,] rows = AsRectangle("hougen", args[1], line, col);
        if (rows.GetLength(1) != 3)
        {
            throw new JgsRuntimeException(line, col,
                $"hougen: each row holds the three partial pressures, and these rows hold "
                + $"{rows.GetLength(1)}.");
        }

        var rates = new double[rows.GetLength(0)];
        for (int r = 0; r < rates.Length; r++)
        {
            double[] x = LeastSquares.Row(rows, r);
            rates[r] = Guarded("hougen", () => NonlinearRegression.Hougen(beta, x), line, col);
        }

        return ColumnOfAnswers(rates);
    }

    /// <summary>The covariance the caller supplied, or the one its Jacobian implies.</summary>
    private static double[,] CovarianceFrom(
        string name, ParsedArgs parsed, double[] residuals, int parameters, int line, int col)
    {
        if (parsed.Named("covar") is { } given)
        {
            double[,] covariance = AsRectangle(name, given, line, col);
            if (covariance.GetLength(0) != parameters || covariance.GetLength(1) != parameters)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: the covariance is {covariance.GetLength(0)} by {covariance.GetLength(1)} "
                    + $"for {parameters} parameters.");
            }

            return covariance;
        }

        if (parsed.Named("jacobian") is not { } supplied)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: the interval needs either 'covar' and the covariance, or 'jacobian' and the "
                + "Jacobian the fit reported.");
        }

        double[,] jacobian = AsRectangle(name, supplied, line, col);
        int df = Math.Max(0, residuals.Length - parameters);
        double squared = 0;
        foreach (double residual in residuals)
        {
            squared += residual * residual;
        }

        double mse = df > 0 ? squared / df : 0;
        var zero = new double[jacobian.GetLength(0)];
        LeastSquares.Fit fit = Guarded(name, () => LeastSquares.Solve(jacobian, zero), line, col);
        var covariance2 = new double[parameters, parameters];
        for (int a = 0; a < parameters; a++)
        {
            for (int b = 0; b < parameters; b++)
            {
                covariance2[a, b] = mse * fit.CrossInverse[a, b];
            }
        }

        return covariance2;
    }

    /// <summary>A model function value, wrapped as a call from parameters to predictions.</summary>
    private static Func<double[], double[]> ModelCaller(
        string name, JgsValue model, JgsValue predictors, JgsValue shape, int expected, int line, int col)
    {
        IJgsCallable callable = model.AsCallable
            ?? throw new JgsRuntimeException(line, col,
                $"{name}: the model is a function of the parameters and the predictors, "
                + "written @(b, x) ….");

        int[] dims = SizeDims(shape);
        return beta =>
        {
            JgsValue answered = callable.Call([ShapedNumbers(beta, dims), predictors], line, col);
            double[] predicted = FlattenColumnMajor(name, answered, line, col);
            if (expected >= 0 && predicted.Length != expected)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: the model answered {predicted.Length} values for {expected} observations.");
            }

            return predicted;
        };
    }

    /// <summary>The search's budget and robust weighting, from an options structure.</summary>
    private static (LevenbergMarquardt.Settings Settings, RobustWeight? Robust, double Tuning) SearchSettings(
        string name, IReadOnlyList<JgsValue> positional, int slot, int line, int col)
    {
        if (positional.Count <= slot || IsPlaceholderValue(positional[slot]))
        {
            return (default, null, 0);
        }

        if (positional[slot].Type != JgsType.Struct)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: the options are a structure of settings — TolFun, TolX, MaxIter, "
                + "RobustWgtFun and Tune.");
        }

        Dictionary<string, JgsValue> options = positional[slot].AsStruct;
        double toleranceF = Setting(options, "TolFun", 0);
        double toleranceX = Setting(options, "TolX", 0);
        int iterations = (int)Setting(options, "MaxIter", 0);
        double tuning = Setting(options, "Tune", 0);

        RobustWeight? robust = null;
        foreach ((string key, JgsValue value) in options)
        {
            if (!string.Equals(key, "RobustWgtFun", StringComparison.OrdinalIgnoreCase)
                || IsPlaceholderValue(value))
            {
                continue;
            }

            if (value.Type != JgsType.String)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: RobustWgtFun names a weight function — 'bisquare', 'huber' and so on.");
            }

            robust = WeightFunction(value.AsString, line, col);
        }

        return (
            new LevenbergMarquardt.Settings(iterations, toleranceX, toleranceF),
            robust,
            tuning);
    }

    /// <summary>One numeric field of an options structure, matched without regard to case.</summary>
    private static double Setting(Dictionary<string, JgsValue> options, string name, double fallback)
    {
        foreach ((string key, JgsValue value) in options)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase)
                && value.Type == JgsType.Number)
            {
                return value.AsNumber;
            }
        }

        return fallback;
    }

    private static double[] Logged(double[] values, int line, int col)
    {
        var logged = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] <= 0)
            {
                throw new JgsRuntimeException(line, col,
                    "nlinfit: an exponential error model needs the model to stay positive.");
            }

            logged[i] = Math.Log(values[i]);
        }

        return logged;
    }

    private static double[] Ones(int count)
    {
        var ones = new double[count];
        Array.Fill(ones, 1.0);
        return ones;
    }
}
