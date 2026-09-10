using System.Numerics;
using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Signal;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// What a filter is, asked of its coefficients: the response, the delays, the order, the norm, the
/// symmetry, and the five predicates (M134).
/// </summary>
/// <remarks>
/// <para>
/// Every name here takes the same three optional trailings — a point count or a frequency vector,
/// the word <c>'whole'</c>, and a sample rate — in whichever combination the caller felt like, and
/// answers frequencies in radians per sample or in hertz depending on whether the last of those was
/// given. That parser is written once, in <see cref="ReadResponseOptions"/>, and every response
/// name uses it.
/// </para>
/// <para>
/// Called with no outputs, each of these draws instead of answering. The plots are the ones MATLAB
/// draws — magnitude and phase in two panels for <c>freqz</c>, a stem for <c>impz</c>, the unit
/// circle with crosses and circles for <c>zplane</c> — built from this build's own axes rather than
/// from MATLAB's, so they are the same picture and not the same pixels.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the seventeen analysis names and the rewritten <c>freqz</c>.</summary>
    internal static void RegisterFilterAnalysisBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        void DefineMulti(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> body) =>
            Define(name, (args, line, col) => body(args, 1, line, col)[0], body);

        DefineMulti("freqz", FrequencyResponse);
        DefineMulti("impz", (args, wanted, line, col) => TimeResponse("impz", args, wanted, line, col));
        DefineMulti("stepz", (args, wanted, line, col) => TimeResponse("stepz", args, wanted, line, col));
        DefineMulti("grpdelay", GroupDelayResponse);
        DefineMulti("phasez", PhaseResponse);
        DefineMulti("phasedelay", PhaseDelayResponse);
        DefineMulti("zerophase", ZeroPhaseResponse);

        Define("impzlength", ImpulseLength);
        Define("filtord", FilterOrder);
        Define("filternorm", FilterNormValue);
        Define("firtype", FirTypeValue);

        Define("isstable", (args, line, col) => Predicate("isstable", args, line, col));
        Define("isminphase", (args, line, col) => Predicate("isminphase", args, line, col));
        Define("ismaxphase", (args, line, col) => Predicate("ismaxphase", args, line, col));
        Define("isallpass", (args, line, col) => Predicate("isallpass", args, line, col));
        Define("islinphase", (args, line, col) => Predicate("islinphase", args, line, col));

        Define("zplane", (args, line, col) => PoleZeroPlot("zplane", args, line, col));
        Define("zplaneplot", (args, line, col) => PoleZeroPlot("zplaneplot", args, line, col));
    }

    // --- Shared option parsing ---------------------------------------------------------------------

    /// <summary>What a response call asks for: a grid, a range and a sample rate.</summary>
    private readonly record struct ResponseOptions(
        int Count, double[]? Frequencies, bool Whole, double? SampleRate);

    /// <summary>
    /// The trailing arguments every response name shares: a point count or a frequency vector, the
    /// word 'whole' or 'half', and a sample rate.
    /// </summary>
    private static ResponseOptions ReadResponseOptions(
        string name, IReadOnlyList<JgsValue> args, int at, int line, int col)
    {
        int count = 512;
        double[]? frequencies = null;
        bool whole = false;
        double? sampleRate = null;
        bool haveGrid = false;

        for (int i = at; i < args.Count; i++)
        {
            if (IsTextScalar(args[i]))
            {
                string word = StrOf(name, args[i], line, col).ToLowerInvariant();
                whole = word switch
                {
                    "whole" or "twosided" => true,
                    "half" or "onesided" or "ctf" => whole,
                    _ => throw new JgsRuntimeException(line, col,
                        $"{name}: '{word}' is not 'whole' or 'half'."),
                };

                continue;
            }

            if (IsEmptyValue(args[i]))
            {
                continue;
            }

            if (!haveGrid)
            {
                haveGrid = true;
                if (ElementCount(args[i]) == 1)
                {
                    count = Count(name, args, i, line, col);
                }
                else
                {
                    frequencies = FilterVector(name, args[i], line, col);
                }

                continue;
            }

            sampleRate = Num(name, args, i, line, col);
        }

        return new ResponseOptions(count, frequencies, whole, sampleRate);
    }

    /// <summary>The numerator, denominator and where the options begin.</summary>
    private static (double[] B, double[] A, int At) ReadCoefficients(
        string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a filter.");
        }

        // A designed filter stands in for the pair, which is the whole of how the digitalFilter
        // methods work: every one of them is this name with a value in front of it (M135).
        if (TryFilterCoefficients(args[0], out double[] designedB, out double[] designedA))
        {
            return (designedB, designedA, 1);
        }

        double[] b = FilterVector(name, args[0], line, col);

        // The second numeric argument is always the denominator. None of these names has a form
        // that puts a point count second — `impz(b, n)` does not exist and `impz(b, a, n)` does —
        // so a scalar there is a one-coefficient denominator and not a grid.
        if (args.Count >= 2 && IsNumericValue(args[1]) && !IsEmptyValue(args[1]))
        {
            return (b, FilterVector(name, args[1], line, col), 2);
        }

        return (b, [1.0], 1);
    }

    // --- The frequency response ----------------------------------------------------------------------

    /// <summary><c>[h, w] = freqz(b, a, n, 'whole', fs)</c>.</summary>
    private static JgsValue[] FrequencyResponse(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("freqz", args, 1, 5, line, col);
        (double[] b, double[] a, int at) = ReadCoefficients("freqz", args, line, col);
        ResponseOptions options = ReadResponseOptions("freqz", args, at, line, col);

        Complex[] h;
        double[] w;
        if (options.Frequencies is not null)
        {
            h = FilterAnalysis.ResponseAt(b, a, options.Frequencies, options.SampleRate);
            w = options.Frequencies;
        }
        else
        {
            double fs = options.SampleRate ?? (2 * System.Math.PI);
            (h, w) = FilterAnalysis.Response(b, a, options.Count, options.Whole, fs);
        }

        if (wanted == 0)
        {
            DrawResponsePanels(b, a, h, w, options.SampleRate is not null);
        }

        return wanted <= 1 ? [ComplexColumn(h)] : [ComplexColumn(h), ColumnOfDoubles(w)];
    }

    // --- Time responses -------------------------------------------------------------------------------

    /// <summary><c>[h, t] = impz(b, a, n, fs)</c> and <c>stepz</c>, which differ only in their input.</summary>
    private static JgsValue[] TimeResponse(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 1, 4, line, col);
        (double[] b, double[] a, int at) = ReadCoefficients(name, args, line, col);

        int count = -1;
        double[]? indices = null;
        double fs = 1;
        bool haveCount = false;
        for (int i = at; i < args.Count; i++)
        {
            if (IsEmptyValue(args[i]))
            {
                continue;
            }

            if (!haveCount)
            {
                haveCount = true;
                if (ElementCount(args[i]) == 1)
                {
                    count = Count(name, args, i, line, col);
                }
                else
                {
                    indices = FilterVector(name, args[i], line, col);
                }

                continue;
            }

            fs = Num(name, args, i, line, col);
        }

        if (count < 0 && indices is null)
        {
            count = FilterAnalysis.ImpulseLength(b, a, 0.00005);
        }

        int start = 0;
        if (indices is not null)
        {
            double smallest = 0;
            double largest = 0;
            foreach (double v in indices)
            {
                smallest = System.Math.Min(smallest, System.Math.Round(v));
                largest = System.Math.Max(largest, System.Math.Round(v));
            }

            start = (int)smallest;
            count = (int)largest + 1;
        }

        double[] full = name == "impz"
            ? FilterAnalysis.Impulse(b, a, count - start)
            : FilterAnalysis.Step(b, a, count - start);

        var t = new double[count - start];
        for (int i = 0; i < t.Length; i++)
        {
            t[i] = (start + i) / fs;
        }

        double[] h = full;
        double[] times = t;
        if (indices is not null)
        {
            h = new double[indices.Length];
            times = new double[indices.Length];
            for (int i = 0; i < indices.Length; i++)
            {
                int index = (int)System.Math.Round(indices[i]) - start;
                h[i] = index >= 0 && index < full.Length ? full[index] : 0;
                times[i] = (start + index) / fs;
            }
        }

        if (wanted == 0)
        {
            DrawStem(times, h, name == "impz" ? "Impulse response" : "Step response");
        }

        return wanted <= 1 ? [ColumnOfDoubles(h)] : [ColumnOfDoubles(h), ColumnOfDoubles(times)];
    }

    // --- Delays and phase --------------------------------------------------------------------------

    /// <summary><c>[gd, w] = grpdelay(b, a, n, 'whole', fs)</c>.</summary>
    private static JgsValue[] GroupDelayResponse(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("grpdelay", args, 1, 5, line, col);
        (double[] b, double[] a, int at) = ReadCoefficients("grpdelay", args, line, col);
        ResponseOptions options = ReadResponseOptions("grpdelay", args, at, line, col);

        double[] gd;
        double[] w;
        if (options.Frequencies is not null)
        {
            var omega = new double[options.Frequencies.Length];
            for (int i = 0; i < omega.Length; i++)
            {
                omega[i] = options.SampleRate is null
                    ? options.Frequencies[i]
                    : 2 * System.Math.PI * options.Frequencies[i] / options.SampleRate.Value;
            }

            gd = FilterAnalysis.GroupDelayAt(b, a, omega);
            w = options.Frequencies;
        }
        else
        {
            (gd, double[] omega) = FilterAnalysis.GroupDelay(b, a, options.Count, options.Whole);
            w = omega;
            if (options.SampleRate is double fs)
            {
                w = new double[omega.Length];
                for (int i = 0; i < omega.Length; i++)
                {
                    w[i] = omega[i] * fs / (2 * System.Math.PI);
                }
            }
        }

        if (wanted == 0)
        {
            DrawCurve(w, gd, "Group delay (samples)", options.SampleRate is not null);
        }

        return wanted <= 1 ? [ColumnOfDoubles(gd)] : [ColumnOfDoubles(gd), ColumnOfDoubles(w)];
    }

    /// <summary><c>[phi, w] = phasez(b, a, n, 'whole', fs)</c>.</summary>
    private static JgsValue[] PhaseResponse(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("phasez", args, 1, 5, line, col);
        (double[] b, double[] a, int at) = ReadCoefficients("phasez", args, line, col);
        ResponseOptions options = ReadResponseOptions("phasez", args, at, line, col);
        (double[] phi, double[] w) = FilterAnalysis.Phase(b, a, options.Count, options.Whole);
        w = Rescale(w, options.SampleRate);

        if (wanted == 0)
        {
            DrawCurve(w, phi, "Phase (rad)", options.SampleRate is not null);
        }

        return wanted <= 1 ? [ColumnOfDoubles(phi)] : [ColumnOfDoubles(phi), ColumnOfDoubles(w)];
    }

    /// <summary><c>[phi, w] = phasedelay(b, a, n, 'whole', fs)</c>.</summary>
    private static JgsValue[] PhaseDelayResponse(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("phasedelay", args, 1, 5, line, col);
        (double[] b, double[] a, int at) = ReadCoefficients("phasedelay", args, line, col);
        ResponseOptions options = ReadResponseOptions("phasedelay", args, at, line, col);
        (double[] pd, double[] w) = FilterAnalysis.PhaseDelay(b, a, options.Count, options.Whole);
        w = Rescale(w, options.SampleRate);

        if (wanted == 0)
        {
            DrawCurve(w, pd, "Phase delay (samples)", options.SampleRate is not null);
        }

        return wanted <= 1 ? [ColumnOfDoubles(pd)] : [ColumnOfDoubles(pd), ColumnOfDoubles(w)];
    }

    /// <summary><c>[Hr, w, phi] = zerophase(b, a, n, 'whole', fs)</c>.</summary>
    private static JgsValue[] ZeroPhaseResponse(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("zerophase", args, 1, 5, line, col);
        (double[] b, double[] a, int at) = ReadCoefficients("zerophase", args, line, col);
        ResponseOptions options = ReadResponseOptions("zerophase", args, at, line, col);
        (double[] hz, double[] w, double[] phi) = FilterAnalysis.ZeroPhase(b, a, options.Count, options.Whole);
        w = Rescale(w, options.SampleRate);

        if (wanted == 0)
        {
            DrawCurve(w, hz, "Amplitude", options.SampleRate is not null);
        }

        return wanted <= 1
            ? [ColumnOfDoubles(hz)]
            : wanted == 2
                ? [ColumnOfDoubles(hz), ColumnOfDoubles(w)]
                : [ColumnOfDoubles(hz), ColumnOfDoubles(w), ColumnOfDoubles(phi)];
    }

    /// <summary>Angular frequencies turned into hertz when a sample rate was given.</summary>
    private static double[] Rescale(double[] w, double? sampleRate)
    {
        if (sampleRate is not double fs)
        {
            return w;
        }

        var scaled = new double[w.Length];
        for (int i = 0; i < w.Length; i++)
        {
            scaled[i] = w[i] * fs / (2 * System.Math.PI);
        }

        return scaled;
    }

    // --- Scalar answers -------------------------------------------------------------------------------

    /// <summary><c>n = impzlength(b, a, tol)</c>.</summary>
    private static JgsValue ImpulseLength(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("impzlength", args, 1, 3, line, col);
        (double[] b, double[] a, int at) = ReadCoefficients("impzlength", args, line, col);
        double tolerance = at < args.Count && !IsEmptyValue(args[at])
            ? Num("impzlength", args, at, line, col)
            : 0.00005;
        return JgsValue.Number(FilterAnalysis.ImpulseLength(b, a, tolerance));
    }

    /// <summary><c>n = filtord(b, a)</c>.</summary>
    private static JgsValue FilterOrder(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("filtord", args, 1, 2, line, col);
        (double[] b, double[] a, _) = ReadCoefficients("filtord", args, line, col);
        return JgsValue.Number(FilterAnalysis.Order(b, a));
    }

    /// <summary><c>s = filternorm(b, a, pnorm, tol)</c>.</summary>
    private static JgsValue FilterNormValue(IReadOnlyList<JgsValue> args, int line, int col)
    {
        // filternorm is not one of the names a digitalFilter answers to — MATLAB's list of its
        // methods does not carry it — so this one keeps taking a coefficient pair and nothing else.
        ArityRange("filternorm", args, 2, 4, line, col);
        double[] b = FilterVector("filternorm", args[0], line, col);
        double[] a = FilterVector("filternorm", args[1], line, col);
        double p = args.Count >= 3 ? Num("filternorm", args, 2, line, col) : 2;
        double tolerance = args.Count >= 4 ? Num("filternorm", args, 3, line, col) : 1e-8;

        try
        {
            return JgsValue.Number(FilterAnalysis.Norm(b, a, p, tolerance));
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"filternorm: {ex.Message}");
        }
    }

    /// <summary><c>t = firtype(b)</c>.</summary>
    private static JgsValue FirTypeValue(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("firtype", args, 1, line, col);
        double[] b = TryFilterCoefficients(args[0], out double[] designed, out _)
            ? designed
            : FilterVector("firtype", args[0], line, col);
        try
        {
            return JgsValue.Number(FilterAnalysis.FirType(b));
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"firtype: {ex.Message}");
        }
    }

    /// <summary>The five predicates, which share their arguments and differ in one call.</summary>
    private static JgsValue Predicate(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange(name, args, 1, 3, line, col);
        (double[] b, double[] a, int at) = ReadCoefficients(name, args, line, col);
        double tolerance = at < args.Count && !IsEmptyValue(args[at])
            ? Num(name, args, at, line, col)
            : FilterAnalysis.DefaultTolerance;

        try
        {
            // A designed IIR filter is asked section by section rather than through the polynomial
            // its sections multiply out to. That is not a refinement: eight zeros at −1 leave the
            // expanded numerator with roots a hundredth off the circle, so the expanded filter is
            // not minimum phase when the cascade plainly is (M135).
            if (name is "isminphase" or "ismaxphase" or "isallpass"
                && TryFilterSections(args[0], out double[] sos, out int rows))
            {
                return JgsValue.Bool(EverySection(name, sos, rows, tolerance));
            }

            bool answer = name switch
            {
                "isstable" => FilterAnalysis.IsStable(a),
                "isminphase" => FilterAnalysis.IsMinimumPhase(b, a, tolerance),
                "ismaxphase" => FilterAnalysis.IsMaximumPhase(b, a, tolerance),
                "isallpass" => FilterAnalysis.IsAllPass(b, a, tolerance),
                _ => FilterAnalysis.IsLinearPhase(b, a, tolerance),
            };

            return JgsValue.Bool(answer);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"{name}: {ex.Message}");
        }
    }

    /// <summary>Whether every section of a cascade answers the predicate.</summary>
    private static bool EverySection(string name, double[] sos, int rows, double tolerance)
    {
        for (int i = 0; i < rows; i++)
        {
            double[] b = [sos[i], sos[i + rows], sos[i + (2 * rows)]];
            double[] a = [sos[i + (3 * rows)], sos[i + (4 * rows)], sos[i + (5 * rows)]];
            bool answer = name switch
            {
                "isminphase" => FilterAnalysis.IsMinimumPhase(b, a, tolerance),
                "ismaxphase" => FilterAnalysis.IsMaximumPhase(b, a, tolerance),
                _ => FilterAnalysis.IsAllPass(b, a, tolerance),
            };

            if (!answer)
            {
                return false;
            }
        }

        return true;
    }

    // --- Plots ---------------------------------------------------------------------------------------

    /// <summary><c>zplane(b, a)</c> or <c>zplane(z, p)</c>: the poles and zeros against the unit circle.</summary>
    private static JgsValue PoleZeroPlot(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange(name, args, 1, 2, line, col);

        // A column argument is already a root list; a row is a coefficient vector. That is the whole
        // of how zplane tells its two forms apart, and it is why a row of poles is read as a
        // polynomial and answers something else entirely.
        int[] dims = SizeDims(args[0]);
        bool roots = dims.Length >= 2 && dims[0] > 1;

        Complex[] zeros;
        Complex[] poles;
        if (roots)
        {
            zeros = FilterRoots(name, args[0], line, col);
            poles = args.Count >= 2 ? FilterRoots(name, args[1], line, col) : [];
        }
        else
        {
            double[] b = FilterVector(name, args[0], line, col);
            double[] a = args.Count >= 2 ? FilterVector(name, args[1], line, col) : [1.0];
            FilterCoefficients.Zpk answer = FilterCoefficients.TfToZpk(b, a);
            zeros = answer.Zeros;
            poles = answer.Poles;
        }

        AxesModel axes = JG.Gca();
        var circleX = new double[201];
        var circleY = new double[201];
        for (int i = 0; i <= 200; i++)
        {
            double angle = 2 * System.Math.PI * i / 200;
            circleX[i] = System.Math.Cos(angle);
            circleY[i] = System.Math.Sin(angle);
        }

        axes.AddLine(circleX, circleY);
        if (zeros.Length > 0)
        {
            axes.AddScatter(Array.ConvertAll(zeros, z => z.Real), Array.ConvertAll(zeros, z => z.Imaginary));
        }

        if (poles.Length > 0)
        {
            axes.AddScatter(Array.ConvertAll(poles, p => p.Real), Array.ConvertAll(poles, p => p.Imaginary));
        }

        return JgsValue.Null;
    }

    /// <summary>The two panels <c>freqz</c> draws when nothing asked for its answer.</summary>
    private static void DrawResponsePanels(
        double[] b, double[] a, Complex[] h, double[] w, bool inHertz)
    {
        var magnitude = new double[h.Length];
        for (int i = 0; i < h.Length; i++)
        {
            magnitude[i] = 20 * System.Math.Log10(System.Math.Max(Complex.Abs(h[i]), 1e-300));
        }

        (double[] phase, _) = FilterAnalysis.Phase(b, a, h.Length, false);
        AxesModel axes = JG.Gca();
        axes.AddLine(w, magnitude);
        axes.PrimaryXAxis.Label = inHertz ? "Frequency (Hz)" : "Normalized frequency (rad/sample)";
        axes.PrimaryYAxis.Label = "Magnitude (dB)";
        if (phase.Length == w.Length)
        {
            axes.AddLine(w, phase);
        }
    }

    /// <summary>One curve against frequency, which is what most of the analysis plots are.</summary>
    private static void DrawCurve(double[] w, double[] y, string label, bool inHertz)
    {
        AxesModel axes = JG.Gca();
        axes.AddLine(w, y);
        axes.PrimaryXAxis.Label = inHertz ? "Frequency (Hz)" : "Normalized frequency (rad/sample)";
        axes.PrimaryYAxis.Label = label;
    }

    /// <summary>A stem against time, which is what the two time responses draw.</summary>
    private static void DrawStem(double[] t, double[] y, string label)
    {
        AxesModel axes = JG.Gca();
        axes.AddStem(t, y);
        axes.PrimaryXAxis.Label = "n (samples)";
        axes.PrimaryYAxis.Label = label;
    }

    /// <summary>The two panels <c>freqs</c> draws when nothing asked for its answer.</summary>
    private static void DrawAnalogResponse(double[] w, Complex[] h)
    {
        var magnitude = new double[h.Length];
        var phase = new double[h.Length];
        for (int i = 0; i < h.Length; i++)
        {
            magnitude[i] = Complex.Abs(h[i]);
            phase[i] = h[i].Phase * 180 / System.Math.PI;
        }

        AxesModel axes = JG.Gca();
        axes.AddLine(w, magnitude);
        axes.AddLine(w, phase);
        axes.PrimaryXAxis.Label = "Frequency (rad/s)";
        axes.PrimaryYAxis.Label = "Magnitude";
    }
}
