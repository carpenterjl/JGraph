using JGraph.Signal;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The FIR designs: least squares, the window method, frequency sampling, the equiripple exchange,
/// the constrained pair, the pulse shapes and the two order estimates (M134).
/// </summary>
/// <remarks>
/// <para>
/// These names have more spellings between them than arithmetic. <c>fir1</c> takes its band type,
/// its window and its scaling in any order and tells them apart by type; <c>firls</c> and
/// <c>firpm</c> take an optional weight vector, an optional symmetry word and an optional grid
/// density wrapped in a cell, in that order but each independently absent; <c>firrcos</c> has eight
/// positional arguments of which the fifth changes what the third means. Every one of those is a
/// place where a call that works in MATLAB can fail here, so the parsers are written out rather
/// than shared.
/// </para>
/// <para>
/// Two names raise the order they were given rather than refusing: an even-length filter cannot pass
/// Nyquist, so a lowpass of odd order becomes one of even order and says so. That is a warning in
/// MATLAB and a warning here, raised through the script's own <c>warning</c> so that
/// <c>lastwarn</c> sees it.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the twenty FIR design and order-estimate names.</summary>
    internal static void RegisterFirDesignBuiltins(JgsEnvironment env, JGraphScriptGlobals host)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        void DefineMulti(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> body) =>
            Define(name, (args, line, col) => body(args, 1, line, col)[0], body);

        DefineMulti("fir1", (args, wanted, line, col) => WindowedDesign(env, host, args, wanted, line, col));
        DefineMulti("fir2", (args, wanted, line, col) => SampledDesign(env, host, args, wanted, line, col));
        DefineMulti("firls", (args, wanted, line, col) => LeastSquaresDesign(env, host, "firls", args, wanted, line, col));
        DefineMulti("firpm", (args, wanted, line, col) => EquirippleDesign(env, host, "firpm", args, wanted, line, col));
        DefineMulti("remez", (args, wanted, line, col) => EquirippleDesign(env, host, "remez", args, wanted, line, col));

        DefineMulti("firpmord", (args, wanted, line, col) => EquirippleOrder("firpmord", args, wanted, line, col));
        DefineMulti("remezord", (args, wanted, line, col) => EquirippleOrder("remezord", args, wanted, line, col));
        DefineMulti("kaiserord", KaiserOrderEstimate);

        DefineMulti("fircls", (args, wanted, line, col) => ConstrainedDesign(env, host, args, wanted, line, col));
        DefineMulti("fircls1", (args, wanted, line, col) => ConstrainedLowpassDesign(env, host, args, wanted, line, col));
        DefineMulti("maxflat", FlatDesign);

        Define("rcosdesign", RaisedCosineDesign);
        Define("gaussdesign", GaussianDesign);
        Define("gaussfir", GaussianFir);
        DefineMulti("firgauss", GaussianCascadeDesign);
        DefineMulti("firrcos", LegacyRaisedCosine);
        DefineMulti("intfilt", InterpolationDesign);
        DefineMulti("yulewalk", YuleWalkDesign);

        // cfirpm and cremez are not registered. The complex exchange is a second algorithm with its
        // own ascent and descent stages behind an interface that takes a caller-supplied response
        // function, and a name that is always an error is worse than a name that is absent: `exist`
        // would answer that it is there. ADR 0138 records the gap.
    }

    // --- The window method ------------------------------------------------------------------------

    /// <summary><c>b = fir1(n, Wn, ...)</c>.</summary>
    private static JgsValue[] WindowedDesign(
        JgsEnvironment env, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("fir1", args, 2, 6, line, col);
        int order = Count("fir1", args, 0, line, col);
        double[] cutoffs = FilterVector("fir1", args[1], line, col);

        WindowBandType? band = null;
        double[] window = [];
        bool scale = true;
        bool hilbert = false;
        for (int i = 2; i < args.Count; i++)
        {
            if (IsTextScalar(args[i]))
            {
                string word = StrOf("fir1", args[i], line, col).ToLowerInvariant();
                switch (word)
                {
                    case "h": hilbert = true; break;
                    case "scale": scale = true; break;
                    case "noscale": scale = false; break;
                    case "low": band = WindowBandType.Low; break;
                    case "high": band = WindowBandType.High; break;
                    case "bandpass": band = WindowBandType.BandPass; break;
                    case "stop": band = WindowBandType.Stop; break;
                    case "dc-0": band = WindowBandType.DcZero; break;
                    case "dc-1": band = WindowBandType.DcOne; break;
                    default:
                        throw new JgsRuntimeException(line, col,
                            $"fir1: '{word}' is not one of 'low', 'high', 'bandpass', 'stop', 'DC-0', 'DC-1', "
                            + "'scale' or 'noscale'.");
                }

                continue;
            }

            window = FilterVector("fir1", args[i], line, col);
        }

        double[] h;
        bool raised;
        try
        {
            h = FirWindowDesign.Windowed(order, cutoffs, band, window, scale, hilbert, out raised);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"fir1: {ex.Message}");
        }

        if (raised)
        {
            Warn(env, host, OrderRaised("fir1"), line, col);
        }

        return wanted <= 1 ? [Numbers(h)] : [Numbers(h), JgsValue.Number(1)];
    }

    /// <summary>The words MATLAB uses when a design's order has to grow by one.</summary>
    private static string OrderRaised(string name) =>
        $"{name}: an even-length filter cannot pass Nyquist, so the order was increased by one.";

    // --- Frequency sampling -----------------------------------------------------------------------

    /// <summary><c>b = fir2(n, f, m, npt, lap, window)</c>.</summary>
    private static JgsValue[] SampledDesign(
        JgsEnvironment env, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("fir2", args, 3, 6, line, col);
        int order = Count("fir2", args, 0, line, col);
        double[] f = FilterVector("fir2", args[1], line, col);
        double[] m = FilterVector("fir2", args[2], line, col);

        int taps = order + 1;
        int points = taps < 1024 ? 512 : 1 << (int)System.Math.Ceiling(System.Math.Log2(taps));
        double[] window = [];
        int lap = -1;

        // The fourth and fifth arguments are a grid size or a window, told apart by their length,
        // which is what makes fir2(n, f, m, window) and fir2(n, f, m, npt) both legal.
        var numeric = new List<double[]>();
        for (int i = 3; i < args.Count; i++)
        {
            numeric.Add(FilterVector("fir2", args[i], line, col));
        }

        foreach (double[] value in numeric)
        {
            if (value.Length == 1 && window.Length == 0)
            {
                if (points == (taps < 1024 ? 512 : points) && lap < 0)
                {
                    points = (int)value[0];
                    if (System.Math.Pow(2, System.Math.Round(System.Math.Log2(points))) != points)
                    {
                        points = 1 << (int)System.Math.Ceiling(System.Math.Log2(points));
                    }
                }
                else
                {
                    lap = (int)value[0];
                }

                continue;
            }

            window = value;
        }

        if (lap < 0)
        {
            lap = points / 25;
        }

        double[] h;
        bool raised;
        try
        {
            h = FirWindowDesign.FrequencySampled(order, f, m, points, lap, window, out raised);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"fir2: {ex.Message}");
        }

        if (raised)
        {
            Warn(env, host, OrderRaised("fir2"), line, col);
        }

        return wanted <= 1 ? [Numbers(h)] : [Numbers(h), JgsValue.Number(1)];
    }

    // --- Least squares -------------------------------------------------------------------------

    /// <summary><c>b = firls(n, f, a, w, ftype)</c>.</summary>
    private static JgsValue[] LeastSquaresDesign(
        JgsEnvironment env, JGraphScriptGlobals host, string name,
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 3, 5, line, col);
        int order = Count(name, args, 0, line, col);
        double[] f = FilterVector(name, args[1], line, col);
        double[] a = FilterVector(name, args[2], line, col);

        double[] weights = [];
        LinearPhaseType type = LinearPhaseType.Symmetric;
        for (int i = 3; i < args.Count; i++)
        {
            if (IsTextScalar(args[i]))
            {
                type = SymmetryWord(name, StrOf(name, args[i], line, col), line, col);
            }
            else if (!IsEmptyValue(args[i]))
            {
                weights = FilterVector(name, args[i], line, col);
            }
        }

        double[] h;
        bool raised;
        try
        {
            h = FirWindowDesign.LeastSquares(order, f, a, weights, type, out raised);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"{name}: {ex.Message}");
        }

        if (raised)
        {
            Warn(env, host, OrderRaised(name), line, col);
        }

        return wanted <= 1 ? [Numbers(h)] : [Numbers(h), JgsValue.Number(1)];
    }

    /// <summary>The symmetry word <c>firls</c> and <c>firpm</c> share.</summary>
    private static LinearPhaseType SymmetryWord(string name, string word, int line, int col)
    {
        string lower = word.ToLowerInvariant();
        if (lower.Length == 0)
        {
            return LinearPhaseType.Symmetric;
        }

        if ("hilbert".StartsWith(lower, StringComparison.Ordinal))
        {
            return LinearPhaseType.Hilbert;
        }

        if ("differentiator".StartsWith(lower, StringComparison.Ordinal))
        {
            return LinearPhaseType.Differentiator;
        }

        throw new JgsRuntimeException(line, col,
            $"{name} takes 'hilbert' or 'differentiator' as its type, not '{word}'.");
    }

    // --- The equiripple exchange -----------------------------------------------------------------

    /// <summary><c>[h, err, res] = firpm(n, f, a, w, ftype, {lgrid})</c>.</summary>
    private static JgsValue[] EquirippleDesign(
        JgsEnvironment env, JGraphScriptGlobals host, string name,
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 3, 6, line, col);
        int order = Count(name, args, 0, line, col);
        double[] f = FilterVector(name, args[1], line, col);

        if (!IsNumericValue(args[2]))
        {
            throw new JgsRuntimeException(line, col,
                $"{name}'s amplitudes must be numbers; a response function is cfirpm's form, which is not implemented.");
        }

        double[] a = FilterVector(name, args[2], line, col);
        double[] weights = [];
        LinearPhaseType type = LinearPhaseType.Symmetric;
        int density = FirDesign.DefaultGridDensity;

        for (int i = 3; i < args.Count; i++)
        {
            if (args[i].Type == JgsType.Cell)
            {
                JgsValue[] inside = args[i].AsCell;
                density = inside.Length > 0
                    ? (int)ToDoubles(name, inside[0], line, col)[0]
                    : density;
                if (density < 1)
                {
                    throw new JgsRuntimeException(line, col, $"{name}'s grid density must be positive.");
                }

                continue;
            }

            if (IsTextScalar(args[i]))
            {
                type = SymmetryWord(name, StrOf(name, args[i], line, col), line, col);
                continue;
            }

            if (!IsEmptyValue(args[i]))
            {
                weights = FilterVector(name, args[i], line, col);
            }
        }

        FirDesign.RemezResult answer;
        try
        {
            answer = FirDesign.Remez(order, f, a, weights, type, density);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"{name}: {ex.Message}");
        }

        if (!answer.Converged)
        {
            Warn(env, host, $"{name}: the equiripple exchange did not fully converge.", line, col);
        }

        if (wanted <= 1)
        {
            return [Numbers(answer.H)];
        }

        if (wanted == 2)
        {
            return [Numbers(answer.H), JgsValue.Number(answer.Error)];
        }

        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["fgrid"] = ColumnOfDoubles(answer.GridFrequencies),
            ["des"] = ColumnOfDoubles(answer.Desired),
            ["wt"] = ColumnOfDoubles(answer.Weights),
            ["H"] = ComplexColumn(answer.Response),
            ["error"] = ColumnOfDoubles(answer.ErrorCurve),
            ["iextr"] = ColumnOfDoubles(Array.ConvertAll(answer.ExtremalIndices, i => (double)i)),
            ["fextr"] = ColumnOfDoubles(answer.ExtremalFrequencies),
        };

        return [Numbers(answer.H), JgsValue.Number(answer.Error), JgsValue.Struct(fields)];
    }

    /// <summary>A column of numbers, which is the shape every field of <c>firpm</c>'s result has.</summary>
    private static JgsValue ColumnOfDoubles(double[] values) =>
        JgsMatrix.FromColumnMajor(values, values.Length, 1);

    // --- Order estimates ---------------------------------------------------------------------------

    /// <summary><c>[n, f, a, w] = firpmord(f, a, dev, fs)</c>.</summary>
    private static JgsValue[] EquirippleOrder(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 3, 5, line, col);
        double[] f = FilterVector(name, args[0], line, col);
        double[] a = FilterVector(name, args[1], line, col);
        double[] dev = FilterVector(name, args[2], line, col);
        double fs = args.Count >= 4 && IsNumericValue(args[3]) ? Num(name, args, 3, line, col) : 2;

        FirWindowDesign.RemezEstimate answer;
        try
        {
            answer = FirWindowDesign.RemezOrder(f, a, dev, fs);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"{name}: {ex.Message}");
        }

        bool asCell = args.Count == 5 && IsTextScalar(args[4]);
        if (asCell && wanted <= 1)
        {
            return [JgsValue.Cell(
                [JgsValue.Number(answer.Order), Numbers(answer.Frequencies),
                 Numbers(answer.Amplitudes), Numbers(answer.Weights)])];
        }

        JgsValue[] all =
        [
            JgsValue.Number(answer.Order),
            Numbers(answer.Frequencies),
            Numbers(answer.Amplitudes),
            Numbers(answer.Weights),
        ];

        return all[..System.Math.Clamp(wanted, 1, 4)];
    }

    /// <summary><c>[n, Wn, beta, ftype] = kaiserord(f, a, dev, fs)</c>.</summary>
    private static JgsValue[] KaiserOrderEstimate(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("kaiserord", args, 3, 5, line, col);
        double[] f = FilterVector("kaiserord", args[0], line, col);
        double[] a = FilterVector("kaiserord", args[1], line, col);
        double[] dev = FilterVector("kaiserord", args[2], line, col);
        double fs = args.Count >= 4 && IsNumericValue(args[3]) ? Num("kaiserord", args, 3, line, col) : 2;

        FirWindowDesign.KaiserEstimate answer;
        try
        {
            answer = FirWindowDesign.KaiserOrder(f, a, dev, fs);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"kaiserord: {ex.Message}");
        }

        string word = BandWord(answer.Type);
        bool asCell = args.Count == 5 && IsTextScalar(args[4]);
        if (asCell && wanted <= 1)
        {
            return [JgsValue.Cell(
                [JgsValue.Number(answer.Order), Numbers(answer.Cutoffs), JgsValue.Str(word),
                 Numbers(SignalWindows.Kaiser(answer.Order + 1, answer.Beta)), JgsValue.Str("noscale")])];
        }

        JgsValue[] all =
        [
            JgsValue.Number(answer.Order),
            Numbers(answer.Cutoffs),
            JgsValue.Number(answer.Beta),
            JgsValue.Str(word),
        ];

        return all[..System.Math.Clamp(wanted, 1, 4)];
    }

    /// <summary>The band word <c>kaiserord</c> hands on to <c>fir1</c>.</summary>
    private static string BandWord(WindowBandType type) => type switch
    {
        WindowBandType.Low => "low",
        WindowBandType.High => "high",
        WindowBandType.BandPass => "bandpass",
        WindowBandType.Stop => "stop",
        WindowBandType.DcZero => "DC-0",
        _ => "DC-1",
    };

    // --- The constrained designs ------------------------------------------------------------------

    /// <summary><c>b = fircls(n, f, a, up, lo)</c>.</summary>
    private static JgsValue[] ConstrainedDesign(
        JgsEnvironment env, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("fircls", args, 5, 6, line, col);
        int order = Count("fircls", args, 0, line, col);
        double[] f = FilterVector("fircls", args[1], line, col);
        double[] a = FilterVector("fircls", args[2], line, col);
        double[] up = FilterVector("fircls", args[3], line, col);
        double[] lo = FilterVector("fircls", args[4], line, col);

        double[] h;
        bool converged;
        try
        {
            h = ConstrainedFirDesign.ConstrainedLeastSquares(order, f, a, up, lo, out converged);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"fircls: {ex.Message}");
        }

        if (!converged)
        {
            Warn(env, host, "fircls: the constrained design did not converge.", line, col);
        }

        return wanted <= 1 ? [Numbers(h)] : [Numbers(h), JgsValue.Number(1)];
    }

    /// <summary><c>b = fircls1(n, wo, dp, ds, ...)</c>.</summary>
    private static JgsValue[] ConstrainedLowpassDesign(
        JgsEnvironment env, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("fircls1", args, 4, 9, line, col);
        int order = Count("fircls1", args, 0, line, col);
        double cutoff = Num("fircls1", args, 1, line, col);
        double dp = Num("fircls1", args, 2, line, col);
        double ds = Num("fircls1", args, 3, line, col);

        bool highpass = false;
        double? transition = null;
        (double Pass, double Stop, double Weight)? weighting = null;

        var numbers = new List<double>();
        for (int i = 4; i < args.Count; i++)
        {
            if (IsTextScalar(args[i]))
            {
                string word = StrOf("fircls1", args[i], line, col).ToLowerInvariant();
                if (word == "high")
                {
                    highpass = true;
                }
                else if (word != "low" && word != "trace" && word != "plots" && word != "both")
                {
                    throw new JgsRuntimeException(line, col,
                        $"fircls1: '{word}' is not one of 'low', 'high', 'trace', 'plots' or 'both'.");
                }

                continue;
            }

            numbers.Add(Num("fircls1", args, i, line, col));
        }

        if (numbers.Count == 1)
        {
            transition = numbers[0];
        }
        else if (numbers.Count >= 3)
        {
            weighting = (numbers[0], numbers[1], numbers[2]);
        }
        else if (numbers.Count == 2)
        {
            throw new JgsRuntimeException(line, col,
                "fircls1's weighted form needs a passband edge, a stopband edge and a weight.");
        }

        double[] h;
        bool converged;
        try
        {
            h = ConstrainedFirDesign.ConstrainedLowpass(
                order, cutoff, dp, ds, highpass, transition, weighting, out converged);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"fircls1: {ex.Message}");
        }

        if (!converged)
        {
            Warn(env, host, "fircls1: the constrained design did not converge.", line, col);
        }

        return wanted <= 1 ? [Numbers(h)] : [Numbers(h), JgsValue.Number(1)];
    }

    /// <summary><c>[b, a, b1, b2, sos, g] = maxflat(n, m, wo)</c>.</summary>
    private static JgsValue[] FlatDesign(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("maxflat", args, 3, 4, line, col);
        int zeros = Count("maxflat", args, 0, line, col);
        bool symmetric = IsTextScalar(args[1]);
        int poles = symmetric ? 0 : Count("maxflat", args, 1, line, col);
        if (symmetric)
        {
            string word = StrOf("maxflat", args[1], line, col).ToLowerInvariant();
            if (!"sym".StartsWith(word, StringComparison.Ordinal) && word != "symmetric")
            {
                throw new JgsRuntimeException(line, col,
                    $"maxflat's second argument is a pole count or the word 'sym', not '{word}'.");
            }
        }

        double cutoff = Num("maxflat", args, 2, line, col);

        ConstrainedFirDesign.MaximallyFlat answer;
        try
        {
            answer = ConstrainedFirDesign.FlatDesign(zeros, poles, cutoff, symmetric);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"maxflat: {ex.Message}");
        }

        JgsValue[] all =
        [
            Numbers(answer.B),
            Numbers(answer.A),
            Numbers(answer.EdgeZeros),
            Numbers(answer.PassbandZeros),
            JgsMatrix.FromColumnMajor(answer.Sections, answer.Rows, 6),
            JgsValue.Number(answer.Gain),
        ];

        return all[..System.Math.Clamp(wanted, 1, 6)];
    }

    // --- Pulse shapes ------------------------------------------------------------------------------

    /// <summary><c>b = rcosdesign(beta, span, sps, shape)</c>.</summary>
    private static JgsValue RaisedCosineDesign(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("rcosdesign", args, 3, 4, line, col);
        double beta = Num("rcosdesign", args, 0, line, col);
        double span = Num("rcosdesign", args, 1, line, col);
        int sps = Count("rcosdesign", args, 2, line, col);
        bool squareRoot = true;
        if (args.Count == 4)
        {
            string word = FilterWord("rcosdesign", args[3], line, col, "sqrt", "normal");
            squareRoot = word == "sqrt";
        }

        try
        {
            return Numbers(FirWindowDesign.RaisedCosine(beta, span, sps, squareRoot));
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"rcosdesign: {ex.Message}");
        }
    }

    /// <summary><c>h = gaussdesign(bt, span, sps)</c>.</summary>
    private static JgsValue GaussianDesign(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("gaussdesign", args, 1, 3, line, col);
        double bt = Num("gaussdesign", args, 0, line, col);
        int span = args.Count >= 2 ? Count("gaussdesign", args, 1, line, col) : 3;
        int sps = args.Count >= 3 ? Count("gaussdesign", args, 2, line, col) : 2;
        return Numbers(FirWindowDesign.Gaussian(bt, span / 2.0, (sps * span) + 1));
    }

    /// <summary><c>h = gaussfir(bt, nt, of)</c>.</summary>
    private static JgsValue GaussianFir(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("gaussfir", args, 1, 3, line, col);
        double bt = Num("gaussfir", args, 0, line, col);
        int nt = args.Count >= 2 ? Count("gaussfir", args, 1, line, col) : 3;
        int of = args.Count >= 3 ? Count("gaussfir", args, 2, line, col) : 2;
        return Numbers(FirWindowDesign.Gaussian(bt, nt, (2 * of * nt) + 1));
    }

    /// <summary><c>[h, n] = firgauss(k, n)</c> or <c>firgauss(k, 'minorder', variance)</c>.</summary>
    private static JgsValue[] GaussianCascadeDesign(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("firgauss", args, 2, 3, line, col);
        int k = Count("firgauss", args, 0, line, col);
        int n;
        if (IsTextScalar(args[1]))
        {
            string word = StrOf("firgauss", args[1], line, col).ToLowerInvariant();
            if (!"minorder".StartsWith(word, StringComparison.Ordinal))
            {
                throw new JgsRuntimeException(line, col,
                    $"firgauss's second argument is a length or the word 'minorder', not '{word}'.");
            }

            if (args.Count < 3)
            {
                throw new JgsRuntimeException(line, col, "firgauss's minimum-order form needs a variance.");
            }

            n = FirWindowDesign.GaussianCascadeLength(k, Num("firgauss", args, 2, line, col));
        }
        else
        {
            n = Count("firgauss", args, 1, line, col);
        }

        double[] h;
        try
        {
            h = FirWindowDesign.GaussianCascade(k, n);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"firgauss: {ex.Message}");
        }

        return wanted <= 1 ? [Numbers(h)] : [Numbers(h), JgsValue.Number(n)];
    }

    /// <summary><c>b = firrcos(n, fc, df, fs, ...)</c>, whose third argument changes meaning.</summary>
    private static JgsValue[] LegacyRaisedCosine(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("firrcos", args, 3, 8, line, col);
        int order = Count("firrcos", args, 0, line, col);
        double fc = Num("firrcos", args, 1, line, col);
        double third = Num("firrcos", args, 2, line, col);

        double fs = 2;
        bool squareRoot = false;
        bool rolloff = false;
        double[] window = [];
        double? delay = null;

        var words = new List<string>();
        var numbers = new List<double>();
        for (int i = 3; i < args.Count; i++)
        {
            if (IsTextScalar(args[i]))
            {
                words.Add(StrOf("firrcos", args[i], line, col).ToLowerInvariant());
            }
            else if (ElementCount(args[i]) == 1)
            {
                numbers.Add(Num("firrcos", args, i, line, col));
            }
            else if (!IsEmptyValue(args[i]))
            {
                window = FilterVector("firrcos", args[i], line, col);
            }
        }

        foreach (string word in words)
        {
            switch (word)
            {
                case "rolloff": rolloff = true; break;
                case "bandwidth": rolloff = false; break;
                case "sqrt": squareRoot = true; break;
                case "normal": squareRoot = false; break;
                default:
                    throw new JgsRuntimeException(line, col,
                        $"firrcos: '{word}' is not one of 'rolloff', 'bandwidth', 'sqrt' or 'normal'.");
            }
        }

        if (numbers.Count >= 1)
        {
            fs = numbers[0];
        }

        if (numbers.Count >= 2)
        {
            delay = numbers[1];
        }

        int taps = order + 1;
        double defaultDelay = taps % 2 == 1 ? (taps - 1) / 2.0 : taps / 2.0;
        double r = rolloff ? third : third / (2 * fc);
        if (fc <= 0 || fc >= fs / 2)
        {
            throw new JgsRuntimeException(line, col, "firrcos needs a cutoff between zero and half the sample rate.");
        }

        double[] h = FirWindowDesign.RaisedCosineLegacy(order, fc, r, fs, delay ?? defaultDelay, squareRoot);
        if (window.Length > 0)
        {
            if (window.Length != h.Length)
            {
                throw new JgsRuntimeException(line, col, "firrcos's window must be as long as the filter.");
            }

            for (int i = 0; i < h.Length; i++)
            {
                h[i] *= window[i];
            }
        }

        return wanted <= 1 ? [Numbers(h)] : [Numbers(h), JgsValue.Number(1)];
    }

    /// <summary><c>h = intfilt(r, l, alpha)</c> or <c>intfilt(r, n, 'l')</c>.</summary>
    private static JgsValue[] InterpolationDesign(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        Arity("intfilt", args, 3, line, col);
        int rate = Count("intfilt", args, 0, line, col);
        int span = Count("intfilt", args, 1, line, col);

        bool bandlimited = true;
        double alpha = 1;
        if (IsTextScalar(args[2]))
        {
            string word = FilterWord("intfilt", args[2], line, col, "bandlimited", "lagrange");
            bandlimited = word == "bandlimited";
        }
        else
        {
            alpha = Num("intfilt", args, 2, line, col);
        }

        double[] h;
        try
        {
            h = FirWindowDesign.Interpolation(rate, span, alpha, bandlimited);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"intfilt: {ex.Message}");
        }

        return wanted <= 1 ? [Numbers(h)] : [Numbers(h), JgsValue.Number(1)];
    }

    /// <summary><c>[b, a] = yulewalk(n, f, m)</c>.</summary>
    private static JgsValue[] YuleWalkDesign(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("yulewalk", args, 3, 5, line, col);
        int order = Count("yulewalk", args, 0, line, col);
        double[] f = FilterVector("yulewalk", args[1], line, col);
        double[] m = FilterVector("yulewalk", args[2], line, col);

        int points = 512;
        if (args.Count >= 4)
        {
            points = Count("yulewalk", args, 3, line, col);
            if (System.Math.Round(System.Math.Pow(2, System.Math.Round(System.Math.Log2(points)))) != points)
            {
                points = 1 << (int)System.Math.Ceiling(System.Math.Log2(points));
            }
        }

        int lap = args.Count >= 5 ? Count("yulewalk", args, 4, line, col) : points / 25;

        double[] b;
        double[] a;
        try
        {
            (b, a) = FirWindowDesign.YuleWalk(order, f, m, points, lap);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"yulewalk: {ex.Message}");
        }

        return wanted <= 1 ? [Numbers(b)] : [Numbers(b), Numbers(a)];
    }
}
