using System;
using System.Numerics;

namespace JGraph.Signal;

/// <summary>How <c>istft</c> puts the frames back together.</summary>
public enum OverlapAddMethod
{
    /// <summary>Overlap–add: the frames are summed and divided by the summed window.</summary>
    Ola,

    /// <summary>Weighted overlap–add: the frames are windowed again before the sum.</summary>
    Wola,
}

/// <summary>The estimate a short-time transform is asked for.</summary>
public sealed class ShortTimeRequest
{
    /// <summary>The window applied to every frame; its length is the frame length.</summary>
    public double[] Window { get; set; } = [];

    /// <summary>Samples shared between adjoining frames.</summary>
    public int Overlap { get; set; }

    /// <summary>The transform length, used when <see cref="Frequencies"/> is null.</summary>
    public int Nfft { get; set; }

    /// <summary>The frequencies to evaluate, when the caller named a vector rather than a length.</summary>
    public double[]? Frequencies { get; set; }

    /// <summary>The sample rate; <c>2*pi</c> when the caller gave none.</summary>
    public double SampleRate { get; set; } = 1;

    /// <summary>Whether to keep one side of the spectrum or both.</summary>
    public SpectralRange Range { get; set; } = SpectralRange.OneSided;

    /// <summary>Whether zero frequency belongs in the middle.</summary>
    public bool CenterDc { get; set; }

    /// <summary>Density or power.</summary>
    public SpectralScaling Scaling { get; set; } = SpectralScaling.Psd;

    /// <summary>Whether every estimate moves to its centre of gravity.</summary>
    public bool Reassign { get; set; }

    /// <summary>Estimates below this — a linear power, not decibels — are set to zero.</summary>
    public double Threshold { get; set; }

    /// <summary>Whether the caller wants the power spectrogram as well as the transform.</summary>
    public bool WantPower { get; set; }

    /// <summary>Whether the caller wants the frequency centres of gravity.</summary>
    public bool WantFrequencyCentres { get; set; }

    /// <summary>Whether the caller wants the time centres of gravity.</summary>
    public bool WantTimeCentres { get; set; }
}

/// <summary>What a short-time transform gives back, one array per frame.</summary>
public sealed class ShortTimeAnswer
{
    /// <summary>The windowed transform of every frame, indexed frame then frequency.</summary>
    public Complex[][] Values { get; set; } = [];

    /// <summary>The frequency of every row.</summary>
    public double[] Frequencies { get; set; } = [];

    /// <summary>The centre time of every frame.</summary>
    public double[] Times { get; set; } = [];

    /// <summary>The power spectrogram, when it was asked for.</summary>
    public Complex[][]? Power { get; set; }

    /// <summary>The frequency centre of gravity of every estimate.</summary>
    public double[][]? FrequencyCentres { get; set; }

    /// <summary>The time centre of gravity of every estimate.</summary>
    public double[][]? TimeCentres { get; set; }
}

/// <summary>
/// MATLAB's short-time Fourier transform: the frames, their transforms, and the way back.
/// </summary>
/// <remarks>
/// <c>spectrogram</c>, <c>xspectrogram</c>, <c>stft</c> and <c>istft</c> all divide a signal the
/// same way — <c>signal.internal.stft.getSTFTColumns</c> — and differ afterwards in what they do
/// with the transform. The frames here are indexed frame-first, which is the order the arithmetic
/// wants; the MATLAB-facing layer transposes on the way out.
/// </remarks>
public static class ShortTimeTransforms
{
    /// <summary>
    /// MATLAB's <c>getSTFTColumns</c>: the overlapping frames and the time at the centre of each.
    /// </summary>
    public static (Complex[][] Frames, double[] Times) Columns(
        ReadOnlySpan<Complex> x,
        int frame,
        int overlap,
        double fs)
    {
        int hop = frame - overlap;
        int count = hop <= 0 ? 0 : (x.Length - overlap) / hop;
        if (count < 0)
        {
            count = 0;
        }

        var frames = new Complex[count][];
        var times = new double[count];
        for (int c = 0; c < count; c++)
        {
            int start = c * hop;
            var column = new Complex[frame];
            for (int i = 0; i < frame; i++)
            {
                column[i] = x[start + i];
            }

            frames[c] = column;
            times[c] = (start + (frame / 2.0)) / fs;
        }

        return (frames, times);
    }

    /// <summary>
    /// MATLAB's <c>dfwin</c>: the window multiplied by a time ramp centred on the frame, which
    /// differentiates its transform with respect to frequency.
    /// </summary>
    public static double[] WindowRamp(double[] window, double fs)
    {
        ArgumentNullException.ThrowIfNull(window);
        int n = window.Length;
        var ramped = new double[n];
        for (int i = 0; i < n; i++)
        {
            ramped[i] = window[i] * ((1 - n + (2.0 * i)) / 2) / fs;
        }

        return ramped;
    }

    /// <summary>The short-time transform, and the power spectrogram when it is asked for.</summary>
    public static ShortTimeAnswer Transform(Complex[] x, ShortTimeRequest request)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(request);

        double fs = request.SampleRate;
        double[] window = request.Window;
        bool scalarNfft = request.Frequencies is null;
        int nfft = scalarNfft ? request.Nfft : request.Frequencies!.Length;
        double[] grid = scalarNfft
            ? SpectralEstimation.FrequencyGrid(nfft, fs)
            : request.Frequencies!;

        (Complex[][] frames, double[] times) = Columns(x, window.Length, request.Overlap, fs);
        var raw = new Complex[frames.Length][];
        for (int c = 0; c < frames.Length; c++)
        {
            raw[c] = TransformOne(frames[c], window, nfft, request.Frequencies, fs);
        }

        bool wantCentres = request.Reassign || request.WantFrequencyCentres;
        bool wantTimes = request.Reassign || request.WantTimeCentres;
        double[][]? fcorr = wantCentres ? new double[frames.Length][] : null;
        double[][]? tcorr = wantTimes ? new double[frames.Length][] : null;
        if (wantCentres)
        {
            double[] derivative = SpectralEstimation.WindowDerivative(window, fs);
            for (int c = 0; c < frames.Length; c++)
            {
                Complex[] yc = TransformOne(frames[c], derivative, nfft, request.Frequencies, fs);
                var row = new double[yc.Length];
                for (int i = 0; i < yc.Length; i++)
                {
                    double shift = -(yc[i] / raw[c][i]).Imaginary;
                    row[i] = grid[i] + (double.IsFinite(shift) ? shift : 0);
                }

                fcorr![c] = row;
            }
        }

        if (wantTimes)
        {
            double[] ramp = WindowRamp(window, fs);
            for (int c = 0; c < frames.Length; c++)
            {
                Complex[] yc = TransformOne(frames[c], ramp, nfft, request.Frequencies, fs);
                var row = new double[yc.Length];
                for (int i = 0; i < yc.Length; i++)
                {
                    double shift = (yc[i] / raw[c][i]).Real;
                    row[i] = times[c] + (double.IsFinite(shift) ? shift : 0);
                }

                tcorr![c] = row;
            }
        }

        // Truncate to one side before anything else looks at the rows, which is where MATLAB does
        // it: the reassignment below then works on the grid the caller will actually be handed.
        int keep = grid.Length;
        double[] frequencies = grid;
        if (scalarNfft && request.Range == SpectralRange.OneSided)
        {
            keep = SpectralEstimation.OneSidedLength(nfft);
            frequencies = grid[..keep];
            for (int c = 0; c < raw.Length; c++)
            {
                raw[c] = raw[c][..keep];
                if (fcorr is not null)
                {
                    fcorr[c] = fcorr[c][..keep];
                }

                if (tcorr is not null)
                {
                    tcorr[c] = tcorr[c][..keep];
                }
            }
        }

        if (fcorr is not null)
        {
            bool negative = request.CenterDc || (!scalarNfft && NegativeFrequency(grid));
            for (int c = 0; c < fcorr.Length; c++)
            {
                for (int i = 0; i < fcorr[c].Length; i++)
                {
                    fcorr[c][i] = negative
                        ? Modulo(fcorr[c][i] + (fs / 2), fs) - (fs / 2)
                        : Modulo(fcorr[c][i], fs);
                }
            }
        }

        var answer = new ShortTimeAnswer
        {
            Values = raw,
            Frequencies = frequencies,
            Times = times,
            FrequencyCentres = request.WantFrequencyCentres || request.Reassign ? fcorr : null,
            TimeCentres = request.WantTimeCentres || request.Reassign ? tcorr : null,
        };

        if (request.WantPower)
        {
            answer.Power = PowerSpectrogram(answer, request, keep, nfft, scalarNfft);
        }

        if (request.CenterDc && scalarNfft)
        {
            Centre(answer);
        }

        return answer;
    }

    /// <summary>MATLAB's <c>compute_PSD</c>: the squared transform over the window's normalisation.</summary>
    private static Complex[][] PowerSpectrogram(
        ShortTimeAnswer answer,
        ShortTimeRequest request,
        int keep,
        int nfft,
        bool scalarNfft)
    {
        double[] window = request.Window;
        int reassignPoints = request.Reassign ? (scalarNfft ? nfft : window.Length) : 0;
        double u = SpectralEstimation.WindowNormalisation(window, request.Scaling, reassignPoints);
        var sxx = new Complex[answer.Values.Length][];
        for (int c = 0; c < sxx.Length; c++)
        {
            var row = new Complex[answer.Values[c].Length];
            for (int i = 0; i < row.Length; i++)
            {
                Complex v = answer.Values[c][i];
                row[i] = ((v * Complex.Conjugate(v)).Real) / u;
            }

            sxx[c] = row;
        }

        if (request.Reassign)
        {
            sxx = Reassign(
                sxx,
                answer.Frequencies,
                answer.Times,
                answer.FrequencyCentres!,
                answer.TimeCentres!,
                scalarNfft && request.Range == SpectralRange.TwoSided);
        }

        bool fold = scalarNfft && request.Range == SpectralRange.OneSided;
        bool odd = (nfft % 2) != 0;
        for (int c = 0; c < sxx.Length; c++)
        {
            for (int i = 0; i < sxx[c].Length; i++)
            {
                if (fold && i > 0 && (odd || i < keep - 1))
                {
                    sxx[c][i] *= 2;
                }

                if (request.Scaling != SpectralScaling.Power)
                {
                    sxx[c][i] /= request.SampleRate;
                }

                if (request.Threshold > 0 && sxx[c][i].Real < request.Threshold)
                {
                    sxx[c][i] = 0;
                }
            }
        }

        return sxx;
    }

    /// <summary>
    /// MATLAB's <c>reassignSpectrum</c> over both axes: every estimate is summed into the bin
    /// nearest its centre of gravity, and the ones that land outside the map are dropped.
    /// </summary>
    public static Complex[][] Reassign(
        Complex[][] sxx,
        double[] f,
        double[] t,
        double[][] fcorr,
        double[][] tcorr,
        bool cyclic)
    {
        ArgumentNullException.ThrowIfNull(sxx);
        ArgumentNullException.ThrowIfNull(f);
        ArgumentNullException.ThrowIfNull(t);
        ArgumentNullException.ThrowIfNull(fcorr);
        ArgumentNullException.ThrowIfNull(tcorr);

        int nf = f.Length;
        int nt = t.Length;
        var moved = new Complex[nt][];
        for (int c = 0; c < nt; c++)
        {
            moved[c] = new Complex[nf];
        }

        if (nf == 0 || nt == 0)
        {
            return moved;
        }

        double fmin = f[0];
        double fspan = f[nf - 1] - fmin;
        double tmin = t[0];
        double tspan = t[nt - 1] - tmin;
        for (int c = 0; c < nt; c++)
        {
            for (int i = 0; i < nf; i++)
            {
                int row = Bin(fcorr[c][i], fmin, fspan, nf);
                if (cyclic)
                {
                    row %= nf;
                    if (row < 0)
                    {
                        row += nf;
                    }
                }

                int column = Bin(tcorr[c][i], tmin, tspan, nt);
                if (row >= 0 && row < nf && column >= 0 && column < nt)
                {
                    moved[column][row] += sxx[c][i];
                }
            }
        }

        return moved;
    }

    /// <summary>
    /// MATLAB's <c>iscola</c>: whether the window's overlapped powers sum to a constant, which is
    /// what perfect reconstruction needs.
    /// </summary>
    public static (bool Constant, double[] Sums, double Deviation) ConstantOverlapAdd(
        double[] window,
        int overlap,
        OverlapAddMethod method)
    {
        ArgumentNullException.ThrowIfNull(window);
        int n = window.Length;
        int hop = n - overlap;
        int power = method == OverlapAddMethod.Wola ? 2 : 1;
        var sums = new double[hop];
        int whole = n / hop;
        for (int k = 0; k < whole; k++)
        {
            for (int i = 0; i < hop; i++)
            {
                sums[i] += System.Math.Pow(window[(k * hop) + i], power);
            }
        }

        int remainder = n % hop;
        for (int i = 0; i < remainder; i++)
        {
            sums[i] += System.Math.Pow(window[n - remainder + i], power);
        }

        int pieces = whole + (remainder != 0 ? 1 : 0);
        double[] sorted = (double[])sums.Clone();
        Array.Sort(sorted);
        double median = sorted.Length == 0
            ? 0
            : (sorted.Length % 2) != 0
                ? sorted[sorted.Length / 2]
                : (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2;
        double deviation = 0;
        foreach (double s in sums)
        {
            deviation = System.Math.Max(deviation, System.Math.Abs(s - median));
        }

        return (deviation < pieces * 2.220446049250313e-16, sums, deviation);
    }

    /// <summary>
    /// MATLAB's <c>istft</c> synthesis: the inverse transforms are laid back down a hop apart and
    /// divided by the summed window, leaving the sum alone where it is too small to divide by.
    /// </summary>
    public static (Complex[] Signal, double[] Times) OverlapAdd(
        Complex[][] frames,
        double[] window,
        int overlap,
        OverlapAddMethod method,
        double fs)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(window);

        int n = window.Length;
        int hop = n - overlap;
        int count = frames.Length;
        int length = n + ((count - 1) * hop);
        if (count == 0)
        {
            return ([], []);
        }

        int a = method == OverlapAddMethod.Wola ? 1 : 0;
        var signal = new Complex[length];
        var norm = new double[length];
        for (int c = 0; c < count; c++)
        {
            int start = c * hop;
            for (int i = 0; i < n; i++)
            {
                signal[start + i] += frames[c][i] * System.Math.Pow(window[i], a);
                norm[start + i] += System.Math.Pow(window[i], a + 1);
            }
        }

        double floor = count * 2.220446049250313e-16;
        var times = new double[length];
        for (int i = 0; i < length; i++)
        {
            if (norm[i] < floor)
            {
                norm[i] = 1;
            }

            signal[i] /= norm[i];
            times[i] = i / fs;
        }

        return (signal, times);
    }

    /// <summary>
    /// MATLAB's <c>formatISTFTInput</c>: the whole two-sided column an inverse transform needs,
    /// rebuilt from whichever half or rotation the caller is holding.
    /// </summary>
    public static Complex[] TwoSidedColumn(Complex[] column, SpectralRange range, bool centred, int nfft)
    {
        ArgumentNullException.ThrowIfNull(column);
        if (range == SpectralRange.OneSided)
        {
            var whole = new Complex[nfft];
            int keep = column.Length;
            for (int i = 0; i < keep; i++)
            {
                whole[i] = column[i];
            }

            bool even = (nfft % 2) == 0;
            int last = even ? keep - 2 : keep - 1;
            for (int i = 1; i <= last; i++)
            {
                whole[nfft - i] = Complex.Conjugate(column[i]);
            }

            return whole;
        }

        if (!centred)
        {
            return (Complex[])column.Clone();
        }

        var moved = new Complex[nfft];
        int shift = (nfft % 2) == 0 ? -((nfft / 2) - 1) : -(nfft / 2);
        for (int i = 0; i < nfft; i++)
        {
            moved[(((i + shift) % nfft) + nfft) % nfft] = column[i];
        }

        return moved;
    }

    /// <summary>The short-time transform of a signal, in whichever range the caller works in.</summary>
    public static Complex[][] Analyse(
        Complex[] x, double[] window, int overlap, int nfft, SpectralRange range, bool centred)
    {
        ShortTimeAnswer answer = Transform(x, new ShortTimeRequest
        {
            Window = window,
            Overlap = overlap,
            Nfft = nfft,
            SampleRate = 2,
            Range = range,
            CenterDc = centred,
        });
        return answer.Values;
    }

    /// <summary>The signal a time-frequency map synthesises back to.</summary>
    public static Complex[] Synthesise(
        Complex[][] map,
        double[] window,
        int overlap,
        int nfft,
        SpectralRange range,
        bool centred,
        OverlapAddMethod method)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(window);
        var frames = new Complex[map.Length][];
        for (int c = 0; c < map.Length; c++)
        {
            Complex[] column = TwoSidedColumn(map[c], range, centred, nfft);
            Fft.Transform(column, inverse: true);
            frames[c] = column[..System.Math.Min(window.Length, column.Length)];
        }

        return OverlapAdd(frames, window, overlap, method, 1).Signal;
    }

    /// <summary>MATLAB's <c>centerest</c>: the rotation that puts zero frequency in the middle.</summary>
    public static T[] CentreRows<T>(T[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        int n = values.Length;
        if (n == 0)
        {
            return values;
        }

        int shift = (n % 2) == 0 ? (n / 2) - 1 : n / 2;
        var moved = new T[n];
        for (int i = 0; i < n; i++)
        {
            moved[(i + shift) % n] = values[i];
        }

        return moved;
    }

    /// <summary>MATLAB's <c>centerfreq</c>: the same grid measured from the middle point.</summary>
    public static double[] CentreFrequencies(double[] f)
    {
        ArgumentNullException.ThrowIfNull(f);
        int n = f.Length;
        if (n == 0)
        {
            return f;
        }

        double middle = (n % 2) == 0 ? f[(n / 2) - 1] : f[((n + 1) / 2) - 1];
        var moved = new double[n];
        for (int i = 0; i < n; i++)
        {
            moved[i] = f[i] - middle;
        }

        return moved;
    }

    /// <summary>The transform of one frame under a window, wrapping or evaluating as MATLAB does.</summary>
    private static Complex[] TransformOne(
        Complex[] column,
        double[] window,
        int nfft,
        double[]? frequencies,
        double fs)
    {
        var windowed = new Complex[window.Length];
        for (int i = 0; i < window.Length; i++)
        {
            windowed[i] = column[i] * window[i];
        }

        if (frequencies is not null)
        {
            return SpectralEstimation.Transform(windowed, frequencies, fs);
        }

        Complex[] y = SpectralEstimation.Transform(windowed, nfft);
        return Real(windowed) ? Mirrored(y) : y;
    }

    /// <summary>Whether every element of a column is real.</summary>
    private static bool Real(Complex[] column)
    {
        foreach (Complex value in column)
        {
            if (value.Imaginary != 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The upper half of a real signal's transform written as the exact conjugate of the lower,
    /// rather than computed again.
    /// </summary>
    /// <remarks>
    /// MATLAB's transform of a real vector is conjugate-symmetric to the last bit, and
    /// <c>istft</c> leans on that: an exactly symmetric column has an exactly real inverse, so a
    /// real signal comes back real. A general complex transform is symmetric only to rounding,
    /// which would leave an imaginary residue in every reconstruction.
    /// </remarks>
    private static Complex[] Mirrored(Complex[] y)
    {
        int n = y.Length;
        for (int i = 1; i < (n + 1) / 2; i++)
        {
            y[n - i] = Complex.Conjugate(y[i]);
        }

        y[0] = y[0].Real;
        if ((n % 2) == 0)
        {
            y[n / 2] = y[n / 2].Real;
        }

        return y;
    }

    private static void Centre(ShortTimeAnswer answer)
    {
        for (int c = 0; c < answer.Values.Length; c++)
        {
            answer.Values[c] = CentreRows(answer.Values[c]);
        }

        if (answer.Power is not null)
        {
            for (int c = 0; c < answer.Power.Length; c++)
            {
                answer.Power[c] = CentreRows(answer.Power[c]);
            }
        }

        if (answer.FrequencyCentres is not null)
        {
            for (int c = 0; c < answer.FrequencyCentres.Length; c++)
            {
                answer.FrequencyCentres[c] = CentreRows(answer.FrequencyCentres[c]);
            }
        }

        if (answer.TimeCentres is not null)
        {
            for (int c = 0; c < answer.TimeCentres.Length; c++)
            {
                answer.TimeCentres[c] = CentreRows(answer.TimeCentres[c]);
            }
        }

        answer.Frequencies = CentreFrequencies(answer.Frequencies);
    }

    private static int Bin(double value, double min, double span, int n)
    {
        double position = span == 0 ? 0 : (value - min) * (n - 1) / span;
        return (int)System.Math.Round(position, MidpointRounding.AwayFromZero);
    }

    private static bool NegativeFrequency(double[] grid)
    {
        foreach (double g in grid)
        {
            if (g < 0)
            {
                return true;
            }
        }

        return false;
    }

    private static double Modulo(double x, double y) => x - (y * System.Math.Floor(x / y));
}
