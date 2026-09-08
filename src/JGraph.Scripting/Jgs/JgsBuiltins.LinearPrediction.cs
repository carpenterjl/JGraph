using System.Numerics;
using JGraph.Signal;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The signal-modelling names of M137: the two Levinson recursions, the four autoregressive fits,
/// the thirteen conversions between a prediction polynomial, its reflection coefficients and its
/// autocorrelation, and the four fits that start from a response rather than a signal.
/// </summary>
/// <remarks>
/// The four <c>ar*</c> fits were already written for M136's parametric spectra, which needed the
/// models rather than the names; what is new here is the argument reading and the reflection
/// coefficients that only these names return.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the modelling names.</summary>
    internal static void RegisterLinearPredictionBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.DeclareFunction(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        void Many(string name, Func<string, IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> body) =>
            Define(name,
                (args, line, col) => First(body(name, args, 1, line, col)),
                (args, wanted, line, col) => body(name, args, wanted, line, col));

        Many("levinson", LevinsonRecursion);
        Many("rlevinson", ReverseLevinsonRecursion);
        Many("lpc", LinearPredictiveCoefficients);
        Many("arburg", AutoRegressiveFit);
        Many("aryule", AutoRegressiveFit);
        Many("arcov", AutoRegressiveFit);
        Many("armcov", AutoRegressiveFit);
        Many("ac2poly", ConvertPrediction);
        Many("ac2rc", ConvertPrediction);
        Many("poly2ac", ConvertPrediction);
        Many("poly2rc", ConvertPrediction);
        Many("rc2ac", ConvertPrediction);
        Many("rc2poly", ConvertPrediction);
        Many("schurrc", ConvertPrediction);
        Define("is2rc", (args, line, col) => Elementwise("is2rc", args, line, col));
        Define("rc2is", (args, line, col) => Elementwise("rc2is", args, line, col));
        Define("lar2rc", (args, line, col) => Elementwise("lar2rc", args, line, col));
        Define("rc2lar", (args, line, col) => Elementwise("rc2lar", args, line, col));
        Define("poly2lsf", (args, line, col) => LineSpectralFrequencies(args, line, col));
        Define("lsf2poly", (args, line, col) => LineSpectralPolynomial(args, line, col));
        Many("prony", PronyFit);
        Many("stmcb", SteiglitzMcBrideFit);
        Many("invfreqz", InverseFrequencyFit);
        Many("invfreqs", InverseFrequencyFit);
    }

    // --- The two recursions -------------------------------------------------------------------------

    /// <summary><c>[a, e, k] = levinson(r, n)</c>, one answer per column of <c>r</c>.</summary>
    private static JgsValue[] LevinsonRecursion(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 1, 2, line, col);
        (Complex[][] columns, _, _) = SpectralChannels(name, args[0], line, col);
        int rows = columns[0].Length;
        int order = args.Count > 1 ? (int)Num(name, args, 1, line, col) : rows - 1;
        if (order >= rows)
        {
            order = rows - 1;
        }

        var a = new Complex[columns.Length][];
        var k = new Complex[columns.Length][];
        var e = new double[columns.Length];
        for (int c = 0; c < columns.Length; c++)
        {
            AutoRegressiveModels.Model model = AutoRegressiveModels.Levinson(columns[c], order);
            a[c] = model.A;
            k[c] = model.Reflection;
            e[c] = model.Variance;
        }

        return ModelOutputs(a, e, k, wanted);
    }

    /// <summary><c>[r, u, k, e] = rlevinson(a, efinal)</c>.</summary>
    private static JgsValue[] ReverseLevinsonRecursion(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 2, 2, line, col);
        Complex[] a = ComplexArrayOf(name, args[0], line, col);
        double efinal = Num(name, args, 1, line, col);
        if (a.Length < 2)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} needs a prediction polynomial of at least first order.");
        }

        (Complex[] r, Complex[,] u, Complex[] reflection, double[] errors) =
            LinearPrediction.ReverseLevinson(a, efinal);
        if (wanted <= 1)
        {
            return [ComplexShaped(r, [r.Length, 1])];
        }

        int size = u.GetLength(0);
        var flat = new Complex[size * size];
        for (int c = 0; c < size; c++)
        {
            for (int i = 0; i < size; i++)
            {
                flat[i + (c * size)] = u[i, c];
            }
        }

        var outputs = new List<JgsValue>
        {
            ComplexShaped(r, [r.Length, 1]),
            ComplexShaped(flat, [size, size]),
            ComplexShaped(reflection, [reflection.Length, 1]),
            JgsMatrix.FromColumnMajor(errors, 1, errors.Length),
        };
        return [.. outputs.Take(wanted)];
    }

    /// <summary><c>[a, e] = lpc(x, n)</c>.</summary>
    private static JgsValue[] LinearPredictiveCoefficients(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 1, 2, line, col);
        (Complex[][] columns, bool real, _) = SpectralChannels(name, args[0], line, col);
        int m = columns[0].Length;
        int order = args.Count > 1 ? (int)Num(name, args, 1, line, col) : m - 1;
        if (order < 0 || order > m)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} needs an order between zero and the signal's length.");
        }

        var a = new Complex[columns.Length][];
        var e = new double[columns.Length];
        for (int c = 0; c < columns.Length; c++)
        {
            Complex[] r = LinearPrediction.TransformAutocorrelation(columns[c], order);
            AutoRegressiveModels.Model model = AutoRegressiveModels.Levinson(r, order);
            a[c] = model.A;
            e[c] = model.Variance;
            if (real)
            {
                for (int i = 0; i < a[c].Length; i++)
                {
                    a[c][i] = a[c][i].Real;
                }
            }
        }

        return ModelOutputs(a, e, null, wanted);
    }

    // --- The four autoregressive fits ---------------------------------------------------------------

    /// <summary><c>[a, e, k] = arburg(x, p)</c> and its three siblings.</summary>
    private static JgsValue[] AutoRegressiveFit(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 2, 2, line, col);
        (Complex[][] columns, _, _) = SpectralChannels(name, args[0], line, col);
        double given = Num(name, args, 1, line, col);
        if (given < 0 || given != System.Math.Floor(given))
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a non-negative whole order.");
        }

        int order = (int)given;
        int least = name == "arburg" ? order + 1 : order;
        if (columns[0].Length < least)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} needs at least {least} samples for an order of {order}.");
        }

        if ((name == "arcov" || name == "armcov") && wanted > 2)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} has no reflection coefficients to give.");
        }

        var a = new Complex[columns.Length][];
        var k = new Complex[columns.Length][];
        var e = new double[columns.Length];
        for (int c = 0; c < columns.Length; c++)
        {
            AutoRegressiveModels.Model model = name switch
            {
                "arburg" => AutoRegressiveModels.Burg(columns[c], order),
                "aryule" => AutoRegressiveModels.YuleWalker(columns[c], order),
                "arcov" => AutoRegressiveModels.ParametricFit(
                    columns[c], order, CorrelationShape.Covariance),
                _ => AutoRegressiveModels.ParametricFit(
                    columns[c], order, CorrelationShape.Modified),
            };
            a[c] = model.A;
            k[c] = model.Reflection;
            e[c] = model.Variance;
        }

        return ModelOutputs(a, e, k, wanted);
    }

    // --- The conversions ----------------------------------------------------------------------------

    /// <summary>The seven conversions that go through one of the two Levinson recursions.</summary>
    private static JgsValue[] ConvertPrediction(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        switch (name)
        {
            case "ac2poly":
            case "ac2rc":
            case "schurrc":
            {
                ArityRange(name, args, 1, 1, line, col);
                (Complex[][] columns, _, _) = SpectralChannels(name, args[0], line, col);
                int rows = columns[0].Length;
                if (name == "schurrc")
                {
                    var reflection = new Complex[columns.Length][];
                    var variance = new double[columns.Length];
                    for (int c = 0; c < columns.Length; c++)
                    {
                        (double[] kk, double ee) = LinearPrediction.SchurReflection(
                            SpectralEstimators.RealPart(columns[c]));
                        reflection[c] = Boxed(kk);
                        variance[c] = ee;
                    }

                    JgsValue k0 = Stacked(reflection);
                    return wanted <= 1
                        ? [k0]
                        : [k0, JgsMatrix.FromColumnMajor(variance, variance.Length, 1)];
                }

                var a = new Complex[columns.Length][];
                var reflections = new Complex[columns.Length][];
                var errors = new double[columns.Length];
                for (int c = 0; c < columns.Length; c++)
                {
                    AutoRegressiveModels.Model model =
                        AutoRegressiveModels.Levinson(columns[c], rows - 1);
                    a[c] = model.A;
                    reflections[c] = model.Reflection;
                    errors[c] = model.Variance;
                }

                if (name == "ac2poly")
                {
                    JgsValue polynomial = Rows(a);
                    return wanted <= 1
                        ? [polynomial]
                        : [polynomial, JgsMatrix.FromColumnMajor(errors, errors.Length, 1)];
                }

                JgsValue k1 = Stacked(reflections);
                var zeroLag = new Complex[columns.Length];
                for (int c = 0; c < columns.Length; c++)
                {
                    zeroLag[c] = columns[c][0];
                }

                return wanted <= 1 ? [k1] : [k1, ComplexShaped(zeroLag, [zeroLag.Length, 1])];
            }

            case "poly2ac":
            {
                ArityRange(name, args, 2, 2, line, col);
                Complex[] a = ComplexArrayOf(name, args[0], line, col);
                double efinal = Num(name, args, 1, line, col);
                (Complex[] r, _, _, _) = LinearPrediction.ReverseLevinson(a, efinal);
                return [ComplexShaped(r, [r.Length, 1])];
            }

            case "poly2rc":
            {
                ArityRange(name, args, 1, 2, line, col);
                Complex[] a = ComplexArrayOf(name, args[0], line, col);
                double efinal = args.Count > 1 ? Num(name, args, 1, line, col) : 0;
                if (a.Length <= 1)
                {
                    return wanted <= 1
                        ? [JgsMatrix.FromColumnMajor([], 0, 0)]
                        : [JgsMatrix.FromColumnMajor([], 0, 0), JgsValue.Number(efinal)];
                }

                if (a[0] == Complex.Zero)
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name} needs a polynomial whose leading coefficient is not zero.");
                }

                var normalised = new Complex[a.Length];
                for (int i = 0; i < a.Length; i++)
                {
                    normalised[i] = a[i] / a[0];
                }

                int p = a.Length - 1;
                var reflection = new Complex[p];
                var errors = new double[p];
                errors[p - 1] = efinal;
                reflection[p - 1] = normalised[p];
                Complex[] current = normalised;
                for (int step = p - 1; step >= 1; step--)
                {
                    (current, errors[step - 1], _) = LinearPrediction.StepDown(current, errors[step]);
                    reflection[step - 1] = current[step];
                }

                if (wanted <= 1)
                {
                    return [ComplexShaped(reflection, [p, 1])];
                }

                double magnitude = reflection[0].Magnitude;
                double zero = errors[0] / (1 - (magnitude * magnitude));
                return [ComplexShaped(reflection, [p, 1]), JgsValue.Number(zero)];
            }

            case "rc2poly":
            {
                ArityRange(name, args, 1, 2, line, col);
                Complex[] k = ComplexArrayOf(name, args[0], line, col);
                double e0 = args.Count > 1 ? Num(name, args, 1, line, col) : 0;
                if (wanted > 1 && args.Count < 2)
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name} needs the zero-lag autocorrelation to give a prediction error.");
                }

                (Complex[] a, double error) = StepUpAll(k, e0);
                return wanted <= 1
                    ? [ComplexShaped(a, [1, a.Length])]
                    : [ComplexShaped(a, [1, a.Length]), JgsValue.Number(error)];
            }

            default:
            {
                ArityRange(name, args, 2, 2, line, col);
                Complex[] k = ComplexArrayOf(name, args[0], line, col);
                double r0 = Num(name, args, 1, line, col);
                (Complex[] a, double error) = StepUpAll(k, r0);
                (Complex[] r, _, _, _) = LinearPrediction.ReverseLevinson(a, error);
                return [ComplexShaped(r, [r.Length, 1])];
            }
        }
    }

    /// <summary>The step-up recursion run over every reflection coefficient in turn.</summary>
    private static (Complex[] Polynomial, double Error) StepUpAll(Complex[] k, double e0)
    {
        if (k.Length == 0)
        {
            return ([1], e0);
        }

        double magnitude = k[0].Magnitude;
        Complex[] a = [1, k[0]];
        double error = e0 * (1 - (magnitude * magnitude));
        for (int i = 1; i < k.Length; i++)
        {
            (a, error) = LinearPrediction.StepUp(a, k[i], error);
        }

        return (a, error);
    }

    /// <summary>The four conversions that are one line of arithmetic each.</summary>
    private static JgsValue Elementwise(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange(name, args, 1, 1, line, col);
        double[] values = ToDoubles(name, args[0], line, col);
        var mapped = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            double v = values[i];
            if ((name == "rc2is" || name == "rc2lar") && (v <= -1 || v >= 1))
            {
                throw new JgsRuntimeException(line, col,
                    $"{name} needs reflection coefficients strictly between -1 and 1.");
            }

            mapped[i] = name switch
            {
                "is2rc" => System.Math.Sin(v * System.Math.PI / 2),
                "rc2is" => 2 / System.Math.PI * System.Math.Asin(v),
                "lar2rc" => -System.Math.Tanh(-v / 2),
                _ => -2 * System.Math.Atanh(-v),
            };
        }

        int[] dims = SizeDims(args[0]);
        return JgsMatrix.FromColumnMajorDims(mapped, dims);
    }

    /// <summary><c>lsf = poly2lsf(a)</c>.</summary>
    private static JgsValue LineSpectralFrequencies(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("poly2lsf", args, 1, 1, line, col);
        double[] a = ToDoubles("poly2lsf", args[0], line, col);
        if (a.Length < 2)
        {
            throw new JgsRuntimeException(line, col,
                "poly2lsf needs a prediction polynomial of at least first order.");
        }

        foreach (Complex root in JGraph.Numerics.Polynomials.Roots(BoxedOf(a)))
        {
            if (root.Magnitude >= 1)
            {
                throw new JgsRuntimeException(line, col,
                    "poly2lsf needs a prediction polynomial whose roots lie inside the unit circle.");
            }
        }

        return ColumnOfDoubles(LinearPrediction.PolynomialToLineSpectral(a));
    }

    /// <summary><c>a = lsf2poly(lsf)</c>.</summary>
    private static JgsValue LineSpectralPolynomial(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("lsf2poly", args, 1, 1, line, col);
        double[] lsf = ToDoubles("lsf2poly", args[0], line, col);
        foreach (double value in lsf)
        {
            if (value < 0 || value > System.Math.PI)
            {
                throw new JgsRuntimeException(line, col,
                    "lsf2poly needs line spectral frequencies between 0 and pi.");
            }
        }

        double[] a = LinearPrediction.LineSpectralToPolynomial(lsf);
        return JgsMatrix.FromColumnMajor(a, 1, a.Length);
    }

    // --- The fits that start from a response --------------------------------------------------------

    /// <summary><c>[b, a] = prony(h, nb, na)</c>.</summary>
    private static JgsValue[] PronyFit(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 3, 3, line, col);
        Complex[] h = ComplexArrayOf(name, args[0], line, col);
        int nb = (int)Num(name, args, 1, line, col);
        int na = (int)Num(name, args, 2, line, col);
        if (nb < 0 || na < 0)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs non-negative orders.");
        }

        (Complex[] b, Complex[] a) = LinearPrediction.Prony(h, nb, na);
        return wanted <= 1
            ? [ComplexShaped(b, [1, b.Length])]
            : [ComplexShaped(b, [1, b.Length]), ComplexShaped(a, [1, a.Length])];
    }

    /// <summary><c>[b, a] = stmcb(h, nb, na, niter, ai)</c> and its two-signal form.</summary>
    private static JgsValue[] SteiglitzMcBrideFit(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 3, 6, line, col);
        Complex[] x = ComplexArrayOf(name, args[0], line, col);
        bool twoSignal = ElementCount(args[1]) != 1;
        Complex[] input;
        int at;
        if (twoSignal)
        {
            input = ComplexArrayOf(name, args[1], line, col);
            if (input.Length != x.Length)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs two signals of the same length.");
            }

            at = 2;
        }
        else
        {
            input = new Complex[x.Length];
            if (input.Length > 0)
            {
                input[0] = 1;
            }

            at = 1;
        }

        if (args.Count < at + 2)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a numerator and a denominator order.");
        }

        int nb = (int)Num(name, args, at, line, col);
        int na = (int)Num(name, args, at + 1, line, col);
        at += 2;
        int iterations = 5;
        Complex[]? start = null;
        if (args.Count > at)
        {
            iterations = (int)Num(name, args, at, line, col);
            at++;
        }

        if (args.Count > at)
        {
            start = ComplexArrayOf(name, args[at], line, col);
        }

        start ??= LinearPrediction.Prony(x, 0, na).Denominator;
        (Complex[] b, Complex[] a) = LinearPrediction.SteiglitzMcBride(
            x, input, nb, na, iterations, start);
        return wanted <= 1
            ? [ComplexShaped(b, [1, b.Length])]
            : [ComplexShaped(b, [1, b.Length]), ComplexShaped(a, [1, a.Length])];
    }

    /// <summary><c>[b, a] = invfreqz(h, w, nb, na, wt, iter, tol, 'trace')</c> and <c>invfreqs</c>.</summary>
    private static JgsValue[] InverseFrequencyFit(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 4, 9, line, col);
        Complex[] g = ComplexArrayOf(name, args[0], line, col);
        double[] w = ToDoubles(name, args[1], line, col);
        if (g.Length != w.Length)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} needs one response value for every frequency.");
        }

        var request = new ResponseFitRequest();
        int at = 2;
        if (IsTextScalar(args[at]))
        {
            string word = StrOf(name, args[at], line, col).ToLowerInvariant();
            request.Real = Matches(word, "real");
            if (!request.Real && !Matches(word, "complex"))
            {
                throw new JgsRuntimeException(line, col,
                    $"{name} takes 'real' or 'complex' as its flag.");
            }

            at++;
        }

        if (args.Count < at + 2)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a numerator and a denominator order.");
        }

        request.NumeratorOrder = (int)Num(name, args, at, line, col);
        request.DenominatorOrder = (int)Num(name, args, at + 1, line, col);
        at += 2;
        int numbers = 0;
        for (; at < args.Count; at++)
        {
            if (IsTextScalar(args[at]))
            {
                string word = StrOf(name, args[at], line, col).ToLowerInvariant();
                if (!Matches(word, "trace"))
                {
                    throw new JgsRuntimeException(line, col, $"{name} does not know the option '{word}'.");
                }

                request.Refine = true;
                continue;
            }

            numbers++;
            request.Refine = true;
            if (ElementCount(args[at]) == 0)
            {
                continue;
            }

            switch (numbers)
            {
                case 1:
                    request.Weights = ToDoubles(name, args[at], line, col);
                    if (request.Weights.Length != w.Length)
                    {
                        throw new JgsRuntimeException(line, col,
                            $"{name} needs one weight for every frequency.");
                    }

                    break;
                case 2:
                    request.MaxIterations = (int)Num(name, args, at, line, col);
                    break;
                case 3:
                    request.Tolerance = Num(name, args, at, line, col);
                    break;
                default:
                    throw new JgsRuntimeException(line, col,
                        $"{name} reads at most a weight vector, an iteration cap and a tolerance.");
            }
        }

        (Complex[] b, Complex[] a) = name == "invfreqz"
            ? FrequencyResponseFit.Discrete(g, w, request)
            : FrequencyResponseFit.Analogue(g, w, request);
        return wanted <= 1
            ? [ComplexShaped(b, [1, b.Length])]
            : [ComplexShaped(b, [1, b.Length]), ComplexShaped(a, [1, a.Length])];
    }

    // --- Shapes -------------------------------------------------------------------------------------

    /// <summary>
    /// The three outputs the fits share: the polynomials one per row, the errors as a row, and the
    /// reflection coefficients one per column.
    /// </summary>
    private static JgsValue[] ModelOutputs(
        Complex[][] a, double[] e, Complex[][]? k, int wanted)
    {
        JgsValue polynomials = Rows(a);
        if (wanted <= 1)
        {
            return [polynomials];
        }

        JgsValue errors = JgsMatrix.FromColumnMajor(e, 1, e.Length);
        if (wanted == 2 || k is null)
        {
            return [polynomials, errors];
        }

        return [polynomials, errors, Stacked(k)];
    }

    /// <summary>One polynomial per row.</summary>
    private static JgsValue Rows(Complex[][] a)
    {
        int channels = a.Length;
        int width = channels == 0 ? 0 : a[0].Length;
        var flat = new Complex[channels * width];
        for (int c = 0; c < channels; c++)
        {
            for (int i = 0; i < width; i++)
            {
                flat[c + (i * channels)] = a[c][i];
            }
        }

        return ComplexShaped(flat, [channels, width]);
    }

    /// <summary>One vector per column.</summary>
    private static JgsValue Stacked(Complex[][] k)
    {
        int channels = k.Length;
        int height = channels == 0 ? 0 : k[0].Length;
        var flat = new Complex[channels * height];
        for (int c = 0; c < channels; c++)
        {
            for (int i = 0; i < height; i++)
            {
                flat[i + (c * height)] = k[c][i];
            }
        }

        return ComplexShaped(flat, [height, channels]);
    }

    private static Complex[] Boxed(double[] values)
    {
        var boxed = new Complex[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            boxed[i] = values[i];
        }

        return boxed;
    }

    private static Complex[] BoxedOf(double[] values) => Boxed(values);
}
