using System.Numerics;
using JGraph.Signal;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The spectral names MATLAB kept from before the toolbox settled on <c>pwelch</c> (M136):
/// <c>csd</c>, <c>cohere</c>, <c>tfe</c> and <c>specgram</c>, and the two — <c>psd</c> and
/// <c>spectrum</c> — that were removed outright rather than kept working.
/// </summary>
/// <remarks>
/// These are not the modern names with the arguments in another order, which is the easy mistake to
/// make and the reason they are written out here rather than routed. They read
/// <c>(nfft, fs, window, noverlap, p, dflag)</c> positionally, default to a Hann window over the
/// whole transform with no overlap and a sample rate of two, divide by the window's energy times the
/// segment count rather than by the sample rate, keep the sum rather than the mean, and accept the
/// detrending flag the modern names dropped. A script that hands them the modern argument order gets
/// a different answer, not an error — so the old reading is kept exactly.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the legacy spectral names.</summary>
    internal static void RegisterLegacySpectralBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.DeclareFunction(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        void Legacy(string name, Func<string, IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> body) =>
            Define(name,
                (args, line, col) => body(name, args, 1, line, col)[0],
                (args, wanted, line, col) => body(name, args, wanted, line, col));

        Legacy("csd", LegacyCross);
        Legacy("cohere", LegacyCross);
        Legacy("tfe", LegacyCross);
        Legacy("specgram", LegacySpectrogram);
        Define("psd", (args, line, col) => throw new JgsRuntimeException(line, col,
            "psd has been removed. Use periodogram or pwelch instead."));
        Define("spectrum", (args, line, col) => throw new JgsRuntimeException(line, col,
            "spectrum has been removed. Use periodogram, pwelch, pburg or pcov instead."));
    }

    /// <summary>What <c>psdchk</c> reads out of the arguments after the signals.</summary>
    private sealed record LegacySpectralOptions(
        int Nfft, double SampleRate, double[] Window, int Overlap, double? Confidence, string Detrend);

    /// <summary>
    /// MATLAB's <c>psdchk</c>: a positional list in which a word may stand where a number would, and
    /// each position that is not reached takes its default.
    /// </summary>
    private static LegacySpectralOptions ReadLegacyOptions(
        string name, IReadOnlyList<JgsValue> args, int start, int length, int line, int col)
    {
        int count = args.Count - start;
        int nfft = System.Math.Min(length, 256);
        double fs = 2;
        double[]? window = null;
        int overlap = 0;
        double? confidence = null;
        string detrend = "none";

        // Each arm below is one of psdchk's, which differ in which position may carry the flag.
        JgsValue At(int i) => args[start + i];
        bool IsWord(int i) => IsTextScalar(At(i));
        bool IsEmpty(int i) => ElementCount(At(i)) == 0;

        if (count >= 1 && !IsWord(0) && !IsEmpty(0))
        {
            nfft = (int)Num(name, args, start, line, col);
        }

        if (count == 1 && IsWord(0))
        {
            detrend = LegacyDetrend(name, At(0), line, col);
        }

        if (count >= 2)
        {
            if (IsWord(1))
            {
                detrend = LegacyDetrend(name, At(1), line, col);
            }
            else if (!IsEmpty(1))
            {
                fs = Num(name, args, start + 1, line, col);
            }
        }

        if (count >= 3)
        {
            if (IsWord(2))
            {
                detrend = LegacyDetrend(name, At(2), line, col);
            }
            else if (!IsEmpty(2))
            {
                double[] given = ToDoubles(name, At(2), line, col);
                window = given.Length == 1 ? SignalWindows.Hanning((int)given[0]) : given;
            }
        }

        if (count >= 4)
        {
            if (IsWord(3))
            {
                detrend = LegacyDetrend(name, At(3), line, col);
            }
            else if (!IsEmpty(3))
            {
                overlap = (int)Num(name, args, start + 3, line, col);
            }
        }

        if (count >= 5)
        {
            if (IsWord(4))
            {
                detrend = LegacyDetrend(name, At(4), line, col);
            }
            else
            {
                confidence = IsEmpty(4) ? 0.95 : Num(name, args, start + 4, line, col);
            }
        }

        if (count >= 6)
        {
            if (!IsWord(5))
            {
                throw new JgsRuntimeException(line, col, $"{name} needs a word for its detrending flag.");
            }

            detrend = LegacyDetrend(name, At(5), line, col);
        }

        window ??= SignalWindows.Hanning(nfft);
        if (nfft < window.Length)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a transform at least as long as its window.");
        }

        if (overlap >= window.Length)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs an overlap shorter than its window.");
        }

        if (confidence is double p && (p < 0 || p > 1))
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a confidence between 0 and 1.");
        }

        return new LegacySpectralOptions(nfft, fs, window, overlap, confidence, detrend);
    }

    /// <summary>The detrending flag, matched on its first letter the way MATLAB matches it.</summary>
    private static string LegacyDetrend(string name, JgsValue value, int line, int col)
    {
        string given = StrOf(name, value, line, col).ToLowerInvariant();
        if (given.StartsWith("n", StringComparison.Ordinal))
        {
            return "none";
        }

        if (given.StartsWith("l", StringComparison.Ordinal))
        {
            return "linear";
        }

        if (given.StartsWith("m", StringComparison.Ordinal))
        {
            return "mean";
        }

        throw new JgsRuntimeException(line, col,
            $"{name} knows the detrending flags 'none', 'linear' and 'mean'.");
    }

    /// <summary>One segment, detrended the way the flag asks and then windowed.</summary>
    private static Complex[] LegacySegment(Complex[] x, int start, double[] window, string detrend)
    {
        int n = window.Length;
        var segment = new Complex[n];
        for (int i = 0; i < n; i++)
        {
            segment[i] = x[start + i];
        }

        if (detrend == "mean")
        {
            Complex mean = 0;
            for (int i = 0; i < n; i++)
            {
                mean += segment[i];
            }

            mean /= n;
            for (int i = 0; i < n; i++)
            {
                segment[i] -= mean;
            }
        }
        else if (detrend == "linear")
        {
            double sumT = 0;
            double sumTT = 0;
            Complex sumY = 0;
            Complex sumTY = 0;
            for (int i = 0; i < n; i++)
            {
                sumT += i;
                sumTT += (double)i * i;
                sumY += segment[i];
                sumTY += i * segment[i];
            }

            double denominator = (n * sumTT) - (sumT * sumT);
            Complex slope = denominator == 0 ? 0 : ((n * sumTY) - (sumT * sumY)) / denominator;
            Complex intercept = (sumY - (slope * sumT)) / n;
            for (int i = 0; i < n; i++)
            {
                segment[i] -= intercept + (slope * i);
            }
        }

        for (int i = 0; i < n; i++)
        {
            segment[i] *= window[i];
        }

        return segment;
    }

    /// <summary><c>csd</c>, <c>cohere</c> and <c>tfe</c>, which differ only in what they divide.</summary>
    private static JgsValue[] LegacyCross(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 2, 8, line, col);
        Complex[] x = ComplexArrayOf(name, args[0], line, col);
        Complex[] y = ComplexArrayOf(name, args[1], line, col);
        if (x.Length != y.Length)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs two signals of the same length.");
        }

        LegacySpectralOptions options = ReadLegacyOptions(name, args, 2, x.Length, line, col);
        int nwind = options.Window.Length;
        int n = x.Length;
        if (n < nwind)
        {
            Array.Resize(ref x, nwind);
            Array.Resize(ref y, nwind);
            n = nwind;
        }

        int advance = nwind - options.Overlap;
        int k = (n - options.Overlap) / advance;
        int nfft = options.Nfft;
        var pxx = new double[nfft];
        var pyy = new double[nfft];
        var pxy = new Complex[nfft];
        for (int segment = 0; segment < k; segment++)
        {
            int start = segment * advance;
            Complex[] xw = LegacySegment(x, start, options.Window, options.Detrend);
            Complex[] yw = LegacySegment(y, start, options.Window, options.Detrend);
            Complex[] xx = SpectralEstimation.Transform(xw, nfft);
            Complex[] yy = SpectralEstimation.Transform(yw, nfft);
            for (int i = 0; i < nfft; i++)
            {
                // The legacy names square the magnitude rather than adding the two squares, and
                // a magnitude is a hypotenuse: the two differ in the last bit, and the difference
                // shows up wherever the cross spectrum nearly cancels.
                double xm = xx[i].Magnitude;
                double ym = yy[i].Magnitude;
                pxx[i] += xm * xm;
                pyy[i] += ym * ym;
                pxy[i] += yy[i] * Complex.Conjugate(xx[i]);
            }
        }

        bool real = true;
        foreach (Complex z in x)
        {
            real &= z.Imaginary == 0;
        }

        foreach (Complex z in y)
        {
            real &= z.Imaginary == 0;
        }

        int keep = real ? SpectralEstimation.OneSidedLength(nfft) : nfft;
        var frequencies = new double[keep];
        for (int i = 0; i < keep; i++)
        {
            frequencies[i] = i * options.SampleRate / nfft;
        }

        double energy = 0;
        foreach (double w in options.Window)
        {
            energy += w * w;
        }

        double kmu = k * energy;
        var values = new Complex[keep];
        for (int i = 0; i < keep; i++)
        {
            values[i] = name switch
            {
                "cohere" => pxy[i].Magnitude * pxy[i].Magnitude / (pxx[i] * pyy[i]),
                "tfe" => pxy[i] / pxx[i],
                _ => pxy[i] / kmu,
            };
        }

        JgsValue estimate = ComplexColumn(values);
        JgsValue f = ColumnOfDoubles(frequencies);
        if (name != "csd")
        {
            return wanted <= 1 ? [estimate] : [estimate, f];
        }

        if (wanted <= 1)
        {
            return [estimate];
        }

        if (wanted == 2)
        {
            return [estimate, f];
        }

        double level = options.Confidence ?? 0.95;
        (double lower, double upper) = SpectralEstimation.Chi2Confidence(level, k);
        // The interval is the cross spectrum itself times each multiplier, so it is complex where
        // the estimate is — csd is the one name in the family whose confidence carries a phase.
        var interval = new Complex[keep * 2];
        for (int i = 0; i < keep; i++)
        {
            interval[i] = pxy[i] * lower / kmu;
            interval[keep + i] = pxy[i] * upper / kmu;
        }

        return [estimate, ComplexShaped(interval, [keep, 2]), f];
    }

    /// <summary><c>[b, f, t] = specgram(x, nfft, fs, window, noverlap)</c>, the short-time transform of 1993.</summary>
    private static JgsValue[] LegacySpectrogram(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 1, 5, line, col);
        Complex[] x = ComplexArrayOf(name, args[0], line, col);
        int nfft = System.Math.Min(x.Length, 256);
        double[]? frequencies = null;
        if (args.Count > 1 && ElementCount(args[1]) > 0)
        {
            double[] given = ToDoubles(name, args[1], line, col);
            if (given.Length == 1)
            {
                nfft = (int)given[0];
            }
            else
            {
                frequencies = given;
            }
        }

        double fs = args.Count > 2 && ElementCount(args[2]) > 0 ? Num(name, args, 2, line, col) : 2;
        double[] window;
        if (args.Count > 3 && ElementCount(args[3]) > 0)
        {
            double[] given = ToDoubles(name, args[3], line, col);
            window = given.Length == 1 ? SignalWindows.Hanning((int)given[0]) : given;
        }
        else if (frequencies is not null)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a window when it is given a frequency vector.");
        }
        else
        {
            window = SignalWindows.Hanning(nfft);
        }

        int nwind = window.Length;
        int overlap = args.Count > 4 && ElementCount(args[4]) > 0
            ? (int)Num(name, args, 4, line, col)
            : (nwind + 1) / 2;
        if (frequencies is null && nfft < nwind)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a transform at least as long as its window.");
        }

        if (overlap >= nwind)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs an overlap shorter than its window.");
        }

        int nx = System.Math.Max(x.Length, nwind);
        int advance = nwind - overlap;
        int columns = (nx - overlap) / advance;
        int needed = nwind + ((columns - 1) * advance);
        if (x.Length < needed)
        {
            Array.Resize(ref x, needed);
        }

        bool real = true;
        foreach (Complex z in x)
        {
            real &= z.Imaginary == 0;
        }

        int keep;
        double[] grid;
        if (frequencies is not null)
        {
            keep = frequencies.Length;
            grid = frequencies;
        }
        else
        {
            keep = real ? SpectralEstimation.OneSidedLength(nfft) : nfft;
            grid = new double[keep];
            for (int i = 0; i < keep; i++)
            {
                grid[i] = i * fs / nfft;
            }
        }

        var flat = new Complex[keep * columns];
        for (int c = 0; c < columns; c++)
        {
            int start = c * advance;
            var segment = new Complex[nwind];
            for (int i = 0; i < nwind; i++)
            {
                segment[i] = x[start + i] * window[i];
            }

            Complex[] spectrum = frequencies is null
                ? SpectralEstimation.Transform(segment, nfft)
                : SpectralEstimation.Transform(segment, frequencies, fs);
            for (int i = 0; i < keep; i++)
            {
                flat[(c * keep) + i] = spectrum[i];
            }
        }

        JgsValue values = ComplexShaped(flat, [keep, columns]);
        if (wanted <= 1)
        {
            return [values];
        }

        JgsValue f = ColumnOfDoubles(grid);
        if (wanted == 2)
        {
            return [values, f];
        }

        var times = new double[columns];
        for (int c = 0; c < columns; c++)
        {
            times[c] = c * advance / fs;
        }

        return [values, f, ColumnOfDoubles(times)];
    }
}
