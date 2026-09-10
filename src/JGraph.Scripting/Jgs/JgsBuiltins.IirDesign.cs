using System.Numerics;
using JGraph.Signal;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The classical IIR designs, their analogue prototypes, the band transforms between them, and the
/// two maps from the s-plane to the z-plane (M134).
/// </summary>
/// <remarks>
/// <para>
/// Five design names share one parser and one pipeline. What each call has to decide is which band
/// it wants — from a word, or from how many cutoffs it was given — whether it wants an analogue
/// filter, and which of four output forms it is being asked for: coefficients, roots, a cascade, or
/// a state-space quadruple. The number of outputs alone chooses between the last three, which is
/// why every one of these is a multi-output builtin even though most calls take two.
/// </para>
/// <para>
/// A single output is the numerator alone. That is not obvious and it is not symmetric — one output
/// from <c>butter</c> is <c>b</c>, and the denominator is simply not returned — but it is what
/// MATLAB does, and the divergence M124 recorded against the old two-row answer is closed here.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the twenty-two classical design, prototype and transform names.</summary>
    internal static void RegisterIirDesignBuiltins(JgsEnvironment env, JGraphScriptGlobals host)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        void DefineMulti(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> body) =>
            Define(name, (args, line, col) => body(args, 1, line, col)[0], body);

        DefineMulti("butter", (args, wanted, line, col) => ClassicalDesign("butter", PrototypeKind.Butterworth, 0, args, wanted, line, col));
        DefineMulti("cheby1", (args, wanted, line, col) => ClassicalDesign("cheby1", PrototypeKind.Chebyshev1, 1, args, wanted, line, col));
        DefineMulti("cheby2", (args, wanted, line, col) => ClassicalDesign("cheby2", PrototypeKind.Chebyshev2, 1, args, wanted, line, col));
        DefineMulti("ellip", (args, wanted, line, col) => ClassicalDesign("ellip", PrototypeKind.Elliptic, 2, args, wanted, line, col));
        DefineMulti("besself", (args, wanted, line, col) => ClassicalDesign("besself", PrototypeKind.Bessel, 0, args, wanted, line, col));

        DefineMulti("buttord", (args, wanted, line, col) => MinimumOrder("buttord", args, wanted, line, col));
        DefineMulti("cheb1ord", (args, wanted, line, col) => MinimumOrder("cheb1ord", args, wanted, line, col));
        DefineMulti("cheb2ord", (args, wanted, line, col) => MinimumOrder("cheb2ord", args, wanted, line, col));
        DefineMulti("ellipord", (args, wanted, line, col) => MinimumOrder("ellipord", args, wanted, line, col));

        DefineMulti("buttap", (args, wanted, line, col) => Prototype("buttap", args, wanted, 1, line, col));
        DefineMulti("cheb1ap", (args, wanted, line, col) => Prototype("cheb1ap", args, wanted, 2, line, col));
        DefineMulti("cheb2ap", (args, wanted, line, col) => Prototype("cheb2ap", args, wanted, 2, line, col));
        DefineMulti("ellipap", (args, wanted, line, col) => Prototype("ellipap", args, wanted, 3, line, col));
        DefineMulti("besselap", (args, wanted, line, col) => Prototype("besselap", args, wanted, 1, line, col));

        DefineMulti("lp2lp", (args, wanted, line, col) => BandTransform("lp2lp", args, wanted, line, col));
        DefineMulti("lp2hp", (args, wanted, line, col) => BandTransform("lp2hp", args, wanted, line, col));
        DefineMulti("lp2bp", (args, wanted, line, col) => BandTransform("lp2bp", args, wanted, line, col));
        DefineMulti("lp2bs", (args, wanted, line, col) => BandTransform("lp2bs", args, wanted, line, col));

        DefineMulti("bilinear", BilinearMap);
        DefineMulti("impinvar", (args, wanted, line, col) => ImpulseInvariance(env, host, args, wanted, line, col));
        DefineMulti("freqs", AnalogFrequencyResponse);
    }

    // --- The five classical designs ------------------------------------------------------------

    /// <summary>The parsed form of a classical design call.</summary>
    private readonly record struct DesignRequest(
        FilterBandType Band, bool Analog, bool Cascade, int SectionOrder);

    /// <summary>
    /// <c>butter</c>, <c>cheby1</c>, <c>cheby2</c>, <c>ellip</c> and <c>besself</c>, which differ in
    /// their prototype and in how many ripple arguments come before the cutoff.
    /// </summary>
    private static JgsValue[] ClassicalDesign(
        string name, PrototypeKind kind, int ripples, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 2 + ripples, 5 + ripples, line, col);
        int order = Count(name, args, 0, line, col);

        double first = ripples >= 1 ? Num(name, args, 1, line, col) : 0;
        double second = ripples >= 2 ? Num(name, args, 2, line, col) : 0;
        double[] cutoffs = FilterVector(name, args[1 + ripples], line, col);

        DesignRequest request = ReadDesignOptions(name, args, 2 + ripples, cutoffs.Length, line, col);
        if (kind == PrototypeKind.Bessel && !request.Analog)
        {
            // besself has no digital form at all: MATLAB passes 's' to its own parser, and a call
            // that asks for a digital Bessel filter is a call for something that does not exist.
            request = request with { Analog = true };
        }

        IirDesign.Design design;
        try
        {
            design = IirDesign.Classical(kind, order, cutoffs, request.Band, request.Analog, first, second);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"{name}: {ex.Message}");
        }

        if (wanted == 4)
        {
            return StateSpaceValues(
                new FilterCoefficients.StateSpace(design.Block.A, design.Block.B, design.Block.C, design.Block.D),
                4);
        }

        if (request.Cascade)
        {
            SecondOrderSections.Cascade cascade = SecondOrderSections.FromRoots(
                design.Zeros, design.Poles, design.Gain, request.SectionOrder,
                SecondOrderSections.Direction.Up, SecondOrderSections.Scaling.None, false);
            return wanted >= 3
                ? [
                    JgsMatrix.FromColumnMajor(NumeratorHalf(cascade), cascade.Rows, request.SectionOrder + 1),
                    JgsMatrix.FromColumnMajor(DenominatorHalf(cascade), cascade.Rows, request.SectionOrder + 1),
                    JgsValue.Number(cascade.Gain),
                ]
                : [
                    JgsMatrix.FromColumnMajor(NumeratorHalf(cascade, cascade.Gain), cascade.Rows, request.SectionOrder + 1),
                    JgsMatrix.FromColumnMajor(DenominatorHalf(cascade), cascade.Rows, request.SectionOrder + 1),
                ];
        }

        if (wanted == 3)
        {
            return [ComplexColumn(design.Zeros), ComplexColumn(design.Poles), JgsValue.Number(design.Gain)];
        }

        (double[] b, double[] a) = IirDesign.ToTransferFunction(design);
        return wanted <= 1 ? [Numbers(b)] : [Numbers(b), Numbers(a)];
    }

    /// <summary>The numerator half of a cascade, optionally with the gain folded into its first row.</summary>
    private static double[] NumeratorHalf(SecondOrderSections.Cascade cascade, double gain = 1)
    {
        int half = cascade.Columns / 2;
        var slice = new double[cascade.Rows * half];
        for (int c = 0; c < half; c++)
        {
            for (int r = 0; r < cascade.Rows; r++)
            {
                slice[(c * cascade.Rows) + r] = cascade.Sections[(c * cascade.Rows) + r] * (r == 0 ? gain : 1);
            }
        }

        return slice;
    }

    /// <summary>The denominator half of a cascade.</summary>
    private static double[] DenominatorHalf(SecondOrderSections.Cascade cascade)
    {
        int half = cascade.Columns / 2;
        var slice = new double[cascade.Rows * half];
        for (int c = 0; c < half; c++)
        {
            for (int r = 0; r < cascade.Rows; r++)
            {
                slice[(c * cascade.Rows) + r] = cascade.Sections[((half + c) * cascade.Rows) + r];
            }
        }

        return slice;
    }

    /// <summary>The band word, the analogue flag and the cascade request a design call carries.</summary>
    private static DesignRequest ReadDesignOptions(
        string name, IReadOnlyList<JgsValue> args, int at, int cutoffs, int line, int col)
    {
        FilterBandType? band = null;
        bool analog = false;
        bool cascade = false;
        int sectionOrder = 2;

        for (int i = at; i < args.Count; i++)
        {
            if (!IsTextScalar(args[i]))
            {
                throw new JgsRuntimeException(line, col, $"{name}'s options are words.");
            }

            string word = StrOf(name, args[i], line, col).ToLowerInvariant();
            switch (word)
            {
                case "s":
                    analog = true;
                    break;

                case "ctf":
                    cascade = true;
                    break;

                case "sectionorder":
                    if (i + 1 >= args.Count)
                    {
                        throw new JgsRuntimeException(line, col, $"{name}'s SectionOrder needs a value.");
                    }

                    sectionOrder = Count(name, args, i + 1, line, col);
                    if (sectionOrder is not (2 or 4))
                    {
                        throw new JgsRuntimeException(line, col, $"{name}'s SectionOrder is 2 or 4.");
                    }

                    i++;
                    break;

                case "low":
                    band = FilterBandType.LowPass;
                    break;

                case "high":
                    band = FilterBandType.HighPass;
                    break;

                case "bandpass":
                    band = FilterBandType.BandPass;
                    break;

                case "stop":
                    band = FilterBandType.BandStop;
                    break;

                default:
                    throw new JgsRuntimeException(line, col,
                        $"{name}: '{word}' is not one of 'low', 'high', 'bandpass', 'stop', 's' or 'ctf'.");
            }
        }

        band ??= cutoffs == 2 ? FilterBandType.BandPass : FilterBandType.LowPass;
        if (cutoffs == 2 && band is FilterBandType.LowPass or FilterBandType.HighPass)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} needs one cutoff for a lowpass or highpass design and two for a bandpass or bandstop.");
        }

        return new DesignRequest(band.Value, analog, cascade, sectionOrder);
    }

    // --- Minimum order ---------------------------------------------------------------------------

    /// <summary>The four <c>*ord</c> names, which differ only in the rule they apply.</summary>
    private static JgsValue[] MinimumOrder(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 4, 5, line, col);
        double[] wp = FilterVector(name, args[0], line, col);
        double[] ws = FilterVector(name, args[1], line, col);
        double rp = Num(name, args, 2, line, col);
        double rs = Num(name, args, 3, line, col);

        bool analog = false;
        if (args.Count == 5)
        {
            string word = StrOf(name, args[4], line, col).ToLowerInvariant();
            if (word is not ("z" or "s"))
            {
                throw new JgsRuntimeException(line, col, $"{name}'s last argument is 'z' or 's', not '{word}'.");
            }

            analog = word == "s";
        }

        IirDesign.Selection answer;
        try
        {
            answer = name switch
            {
                "buttord" => IirDesign.ButterworthOrder(wp, ws, rp, rs, analog),
                "cheb1ord" => IirDesign.Chebyshev1Order(wp, ws, rp, rs, analog),
                "cheb2ord" => IirDesign.Chebyshev2Order(wp, ws, rp, rs, analog),
                _ => IirDesign.EllipticOrder(wp, ws, rp, rs, analog),
            };
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"{name}: {ex.Message}");
        }

        return wanted <= 1
            ? [JgsValue.Number(answer.Order)]
            : [JgsValue.Number(answer.Order), Numbers(answer.Frequencies)];
    }

    // --- The analogue prototypes -------------------------------------------------------------------

    /// <summary>The five <c>*ap</c> names.</summary>
    private static JgsValue[] Prototype(
        string name, IReadOnlyList<JgsValue> args, int wanted, int arity, int line, int col)
    {
        Arity(name, args, arity, line, col);
        int order = Count(name, args, 0, line, col);
        double first = arity >= 2 ? Num(name, args, 1, line, col) : 0;
        double second = arity >= 3 ? Num(name, args, 2, line, col) : 0;

        (Complex[] zeros, Complex[] poles, double gain) answer;
        try
        {
            answer = name switch
            {
                "buttap" => AnalogPrototypes.Butterworth(order),
                "cheb1ap" => AnalogPrototypes.Chebyshev1(order, first),
                "cheb2ap" => AnalogPrototypes.Chebyshev2(order, first),
                "ellipap" => AnalogPrototypes.Elliptic(order, first, second),
                _ => AnalogPrototypes.Bessel(order),
            };
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new JgsRuntimeException(line, col, $"{name}: {ex.Message}");
        }

        return wanted <= 1
            ? [ComplexColumn(answer.zeros)]
            : wanted == 2
                ? [ComplexColumn(answer.zeros), ComplexColumn(answer.poles)]
                : [ComplexColumn(answer.zeros), ComplexColumn(answer.poles), JgsValue.Number(answer.gain)];
    }

    // --- The band transforms ------------------------------------------------------------------------

    /// <summary>The four <c>lp2*</c> names, in both their coefficient and state-space forms.</summary>
    private static JgsValue[] BandTransform(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        bool twoFrequencies = name is "lp2bp" or "lp2bs";
        int shortForm = twoFrequencies ? 4 : 3;
        int longForm = twoFrequencies ? 6 : 5;

        if (args.Count != shortForm && args.Count != longForm)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} takes {shortForm} arguments on coefficients or {longForm} on a state-space quadruple.");
        }

        bool stateSpace = args.Count == longForm;
        int frequencyAt = stateSpace ? 4 : 2;
        double centre = Num(name, args, frequencyAt, line, col);
        double bandwidth = twoFrequencies ? Num(name, args, frequencyAt + 1, line, col) : 0;

        Func<FrequencyTransforms.Block, FrequencyTransforms.Block> transform = name switch
        {
            "lp2lp" => block => FrequencyTransforms.LowpassToLowpass(block, centre),
            "lp2hp" => block => FrequencyTransforms.LowpassToHighpass(block, centre),
            "lp2bp" => block => FrequencyTransforms.LowpassToBandpass(block, centre, bandwidth),
            _ => block => FrequencyTransforms.LowpassToBandstop(block, centre, bandwidth),
        };

        if (!stateSpace)
        {
            double[] num = FilterVector(name, args[0], line, col);
            double[] den = FilterVector(name, args[1], line, col);
            (double[] b, double[] a) = FrequencyTransforms.TransformTransferFunction(num, den, transform);
            return wanted <= 1 ? [Numbers(b)] : [Numbers(b), Numbers(a)];
        }

        (double[,] am, double[,] bm, double[,] cm, double[,] dm, _) = ReadStateSpace(name, args, 4, line, col);
        FrequencyTransforms.Block moved = transform(new FrequencyTransforms.Block(am, bm, cm, dm));
        return StateSpaceValues(
            new FilterCoefficients.StateSpace(moved.A, moved.B, moved.C, moved.D), System.Math.Max(wanted, 2));
    }

    // --- The two maps to the unit circle ------------------------------------------------------------

    /// <summary><c>bilinear</c> in all three of its forms, told apart by how many outputs are wanted.</summary>
    private static JgsValue[] BilinearMap(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("bilinear", args, 3, 6, line, col);

        if (wanted == 4)
        {
            (double[,] a, double[,] b, double[,] c, double[,] d, _) = ReadStateSpace("bilinear", args, 4, line, col);
            double fs = Num("bilinear", args, 4, line, col);
            if (args.Count == 6)
            {
                fs = FrequencyTransforms.PreWarp(fs, Num("bilinear", args, 5, line, col));
            }

            FrequencyTransforms.Block moved =
                FrequencyTransforms.BilinearBlock(new FrequencyTransforms.Block(a, b, c, d), fs);
            return StateSpaceValues(
                new FilterCoefficients.StateSpace(moved.A, moved.B, moved.C, moved.D), 4);
        }

        int[] firstDims = SizeDims(args[0]);
        int[] secondDims = SizeDims(args[1]);
        bool roots = (secondDims.Length < 2 || secondDims[1] == 1)
            && (firstDims.Length < 2 || firstDims[1] <= 1 || ElementCount(args[0]) == 0);

        if (roots && wanted >= 3)
        {
            Complex[] z = FilterRoots("bilinear", args[0], line, col);
            Complex[] p = FilterRoots("bilinear", args[1], line, col);
            double k = Num("bilinear", args, 2, line, col);
            double fs = Num("bilinear", args, 3, line, col);
            if (args.Count == 5)
            {
                fs = FrequencyTransforms.PreWarp(2 * fs, Num("bilinear", args, 4, line, col)) / 2;
            }

            (Complex[] zd, Complex[] pd, double kd) = FrequencyTransforms.BilinearRoots(z, p, k, fs);
            return [ComplexColumn(zd), ComplexColumn(pd), JgsValue.Number(kd)];
        }

        double[] num = FilterVector("bilinear", args[0], line, col);
        double[] den = FilterVector("bilinear", args[1], line, col);
        double sampleRate = Num("bilinear", args, 2, line, col);
        if (args.Count == 4)
        {
            sampleRate = FrequencyTransforms.PreWarp(sampleRate, Num("bilinear", args, 3, line, col));
        }

        (double[] b2, double[] a2) = FrequencyTransforms.BilinearTransferFunction(num, den, sampleRate);
        return wanted <= 1 ? [Numbers(b2)] : [Numbers(b2), Numbers(a2)];
    }

    /// <summary><c>impinvar</c>: the digital filter whose impulse response samples the analogue one.</summary>
    private static JgsValue[] ImpulseInvariance(
        JgsEnvironment env, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("impinvar", args, 2, 4, line, col);
        double[] b = FilterVector("impinvar", args[0], line, col);
        double[] a = FilterVector("impinvar", args[1], line, col);
        double fs = args.Count >= 3 ? Num("impinvar", args, 2, line, col) : 1;
        double tolerance = args.Count >= 4 ? Num("impinvar", args, 3, line, col) : 1e-3;

        double[] bz;
        double[] az;
        bool robust;
        try
        {
            (bz, az, robust) = FrequencyTransforms.ImpulseInvariant(b, a, fs, tolerance);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"impinvar: {ex.Message}");
        }

        if (!robust)
        {
            Warn(env, host, "impinvar: the answer has an imaginary part it should not, so the design may not be reliable.", line, col);
        }

        return wanted <= 1 ? [Numbers(bz)] : [Numbers(bz), Numbers(az)];
    }

    /// <summary><c>freqs</c>: an analogue system's response, over a grid it picks or one it is given.</summary>
    private static JgsValue[] AnalogFrequencyResponse(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("freqs", args, 2, 3, line, col);
        double[] b = FilterVector("freqs", args[0], line, col);
        double[] a = FilterVector("freqs", args[1], line, col);
        (b, a) = TrimSharedTrailingZeros(b, a);

        double[] w;
        if (args.Count < 3)
        {
            w = FrequencyTransforms.FrequencyGrid(b, a, 200);
        }
        else if (ElementCount(args[2]) == 1)
        {
            w = FrequencyTransforms.FrequencyGrid(b, a, Count("freqs", args, 2, line, col));
        }
        else
        {
            w = FilterVector("freqs", args[2], line, col);
        }

        Complex[] h = FrequencyTransforms.AnalogResponse(b, a, w);
        if (wanted == 0)
        {
            DrawAnalogResponse(w, h);
            return [ComplexColumn(h)];
        }

        return wanted <= 1 ? [ComplexColumn(h)] : [ComplexColumn(h), Numbers(w)];
    }

    /// <summary>The trailing zeros both sides share, which <c>freqs</c> cancels before it starts.</summary>
    private static (double[] B, double[] A) TrimSharedTrailingZeros(double[] b, double[] a)
    {
        int bLast = Array.FindLastIndex(b, v => v != 0);
        int aLast = Array.FindLastIndex(a, v => v != 0);
        int shared = System.Math.Min(b.Length - 1 - bLast, a.Length - 1 - aLast);
        if (shared <= 0)
        {
            return (b, a);
        }

        return (b[..(b.Length - shared)], a[..(a.Length - shared)]);
    }

}
