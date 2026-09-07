using System.Numerics;
using JGraph.Numerics;

namespace JGraph.Signal;

/// <summary>
/// The filters that are not linear time-invariant: the local polynomial fit, the running median, the
/// outlier detector built on it, the amplitude envelope and the gap filler (M133).
/// </summary>
/// <remarks>
/// <para>
/// Everything else in M133 is a recurrence with fixed coefficients. These five are not. A
/// Savitzky–Golay filter is a least-squares fit through a sliding frame, which happens to be a
/// convolution in the middle and is not one at the ends; a median filter has no coefficients at all;
/// <c>hampel</c> chooses per sample whether to replace it; the envelope is an amplitude rather than
/// a signal; and <c>fillgaps</c> extrapolates a missing stretch from a model fitted either side of
/// it and crossfades the two guesses.
/// </para>
/// <para>
/// The ends are where each of them is decided. Savitzky–Golay handles its first and last half-frame
/// by evaluating the same fit off-centre rather than by shortening the window, which is why its
/// answer at sample one is a projection matrix row and not a filter tap.
/// </para>
/// </remarks>
public static class SmoothingFilters
{
    /// <summary>
    /// <c>sgolay</c>: the projection matrix of a least-squares polynomial fit over a frame, and the
    /// matrix of differentiation filters that comes with it.
    /// </summary>
    /// <param name="order">The polynomial's degree.</param>
    /// <param name="frameLength">How many samples the fit sees; an odd number.</param>
    /// <param name="weights">One weight per sample, or empty for an unweighted fit.</param>
    public static (double[,] B, double[,] G) SavitzkyGolay(
        int order, int frameLength, ReadOnlySpan<double> weights)
    {
        if (frameLength % 2 == 0)
        {
            throw new ArgumentException("A Savitzky-Golay frame length is odd.", nameof(frameLength));
        }

        if (order >= frameLength)
        {
            throw new ArgumentException(
                "A Savitzky-Golay polynomial's degree is below its frame length.", nameof(order));
        }

        int terms = order + 1;
        var design = new double[frameLength, terms];
        double half = (frameLength - 1) / 2.0;
        for (int i = 0; i < frameLength; i++)
        {
            double t = i - half;
            double power = 1;
            for (int j = 0; j < terms; j++)
            {
                design[i, j] = power;
                power *= t;
            }
        }

        bool weighted = weights.Length > 0;
        var root = new double[frameLength];
        for (int i = 0; i < frameLength; i++)
        {
            root[i] = weighted ? System.Math.Sqrt(weights[i]) : 1;
        }

        if (weighted)
        {
            for (int i = 0; i < frameLength; i++)
            {
                for (int j = 0; j < terms; j++)
                {
                    design[i, j] *= root[i];
                }
            }
        }

        (double[,] q, double[,] r) = ThinQr(design);

        var b = new double[frameLength, frameLength];
        var g = new double[frameLength, terms];

        if (weighted)
        {
            // The weighted fit projects through Q scaled one way and Q scaled the other, which is
            // what makes the projection symmetric in the weighted inner product rather than in the
            // plain one.
            for (int i = 0; i < frameLength; i++)
            {
                for (int j = 0; j < frameLength; j++)
                {
                    double sum = 0;
                    for (int t = 0; t < terms; t++)
                    {
                        sum += q[i, t] * root[i] * q[j, t] / root[j];
                    }

                    b[j, i] = sum;
                }
            }

            for (int i = 0; i < frameLength; i++)
            {
                for (int t = 0; t < terms; t++)
                {
                    g[i, t] = q[i, t] * root[i];
                }
            }
        }
        else
        {
            for (int i = 0; i < frameLength; i++)
            {
                for (int j = 0; j < frameLength; j++)
                {
                    double sum = 0;
                    for (int t = 0; t < terms; t++)
                    {
                        sum += q[i, t] * q[j, t];
                    }

                    b[i, j] = sum;
                }
            }

            for (int i = 0; i < frameLength; i++)
            {
                for (int t = 0; t < terms; t++)
                {
                    g[i, t] = q[i, t];
                }
            }
        }

        // G is the projection divided by R transposed, which turns the fit's coefficients into the
        // derivatives of the polynomial at the frame's centre.
        var differentiators = new double[frameLength, terms];
        for (int i = 0; i < frameLength; i++)
        {
            for (int k = terms - 1; k >= 0; k--)
            {
                double sum = g[i, k];
                for (int j = k + 1; j < terms; j++)
                {
                    sum -= differentiators[i, j] * r[k, j];
                }

                differentiators[i, k] = r[k, k] == 0 ? 0 : sum / r[k, k];
            }
        }

        return (b, differentiators);
    }

    /// <summary>
    /// <c>sgolayfilt</c>: the fit's middle row used as a filter over the body of the signal and its
    /// off-centre rows used at the two ends.
    /// </summary>
    public static double[] SavitzkyGolayFilter(
        double[] x, int rows, int columns, int order, int frameLength, ReadOnlySpan<double> weights)
    {
        if (rows < frameLength)
        {
            throw new ArgumentException(
                "A Savitzky-Golay filter needs at least a frame's worth of samples.", nameof(x));
        }

        (double[,] b, _) = SavitzkyGolay(order, frameLength, weights);
        int half = (frameLength - 1) / 2;

        var centre = new double[frameLength];
        for (int j = 0; j < frameLength; j++)
        {
            centre[j] = b[half, j];
        }

        var y = new double[x.Length];
        var column = new double[rows];

        for (int c = 0; c < columns; c++)
        {
            Array.Copy(x, c * rows, column, 0, rows);
            double[] middle = DigitalFilter.Filter(centre, [1.0], column);

            for (int i = 0; i < half; i++)
            {
                double sum = 0;
                for (int j = 0; j < frameLength; j++)
                {
                    sum += b[frameLength - 1 - i, j] * column[frameLength - 1 - j];
                }

                y[(c * rows) + i] = sum;
            }

            for (int i = half; i < rows - half; i++)
            {
                y[(c * rows) + i] = middle[i + half];
            }

            for (int i = 0; i < half; i++)
            {
                double sum = 0;
                for (int j = 0; j < frameLength; j++)
                {
                    sum += b[half - 1 - i, j] * column[rows - 1 - j];
                }

                y[(c * rows) + rows - half + i] = sum;
            }
        }

        return y;
    }

    /// <summary>A thin QR factorisation, which is what the fit's normal equations are solved by.</summary>
    private static (double[,] Q, double[,] R) ThinQr(double[,] design)
    {
        int m = design.GetLength(0);
        int n = design.GetLength(1);
        var q = new double[m, n];
        var r = new double[n, n];

        for (int j = 0; j < n; j++)
        {
            var v = new double[m];
            for (int i = 0; i < m; i++)
            {
                v[i] = design[i, j];
            }

            for (int k = 0; k < j; k++)
            {
                double dot = 0;
                for (int i = 0; i < m; i++)
                {
                    dot += q[i, k] * design[i, j];
                }

                r[k, j] = dot;
                for (int i = 0; i < m; i++)
                {
                    v[i] -= dot * q[i, k];
                }
            }

            double norm = 0;
            for (int i = 0; i < m; i++)
            {
                norm += v[i] * v[i];
            }

            norm = System.Math.Sqrt(norm);
            r[j, j] = norm;
            if (norm == 0)
            {
                continue;
            }

            for (int i = 0; i < m; i++)
            {
                q[i, j] = v[i] / norm;
            }
        }

        return (q, r);
    }

    // --- Median and outliers --------------------------------------------------------------------

    /// <summary>
    /// <c>medfilt1</c>: the running median, with the ends either padded with zeros or shortened.
    /// </summary>
    public static double[] RunningMedian(
        double[] x, int rows, int columns, int width, bool zeroPad, bool includeNan)
    {
        var y = new double[x.Length];
        if (width == 0)
        {
            Array.Fill(y, double.NaN);
            return y;
        }

        // An even window is not symmetric: MATLAB's moving statistics put the extra sample behind
        // the current one rather than ahead of it, so a window of eight looks four back and three
        // forward. The envelope's running mean asks for the opposite split by naming it explicitly.
        int behind = width / 2;
        int ahead = (width - 1) / 2;
        var column = new double[rows];

        for (int c = 0; c < columns; c++)
        {
            Array.Copy(x, c * rows, column, 0, rows);
            double[] run = WindowKernels.Slide(
                WindowStat.Median, column, behind, ahead,
                zeroPad ? WindowEnds.Pad : WindowEnds.Shrink, 0, !includeNan, double.NaN);
            Array.Copy(run, 0, y, c * rows, rows);
        }

        return y;
    }

    /// <summary>What a Hampel pass answers: the corrected signal and its three explanations.</summary>
    public readonly record struct Hampel(double[] Filtered, bool[] Outliers, double[] Median, double[] Sigma);

    /// <summary>
    /// <c>hampel</c>: a sample is an outlier when it sits further than a few robust standard
    /// deviations from the median of the window around it, and is replaced by that median.
    /// </summary>
    /// <remarks>
    /// The robust standard deviation is the median absolute deviation scaled by about 1.4826, which
    /// is the factor that makes it agree with the ordinary standard deviation for Gaussian data.
    /// MATLAB writes it as the reciprocal of the inverse complementary error function at three
    /// halves times the square root of two, and this reproduces that constant rather than the
    /// rounded one, because the two differ in the twelfth figure.
    /// </remarks>
    public static Hampel HampelFilter(double[] x, int rows, int columns, int k, double sigmas)
    {
        int width = (2 * k) + 1;
        int behind = (width - 1) / 2;
        int ahead = width / 2;

        var filtered = new double[x.Length];
        var outliers = new bool[x.Length];
        var medians = new double[x.Length];
        var deviations = new double[x.Length];

        double scale = -1 / (System.Math.Sqrt(2) * SpecialFunctions.ErfcInverse(1.5));
        var column = new double[rows];

        for (int c = 0; c < columns; c++)
        {
            Array.Copy(x, c * rows, column, 0, rows);
            double[] median = WindowKernels.Slide(
                WindowStat.Median, column, behind, ahead, WindowEnds.Shrink, 0, true, double.NaN);
            double[] deviation = WindowKernels.Slide(
                WindowStat.MedianDeviation, column, behind, ahead, WindowEnds.Shrink, 0, true, double.NaN);

            for (int i = 0; i < rows; i++)
            {
                int at = (c * rows) + i;
                medians[at] = median[i];
                deviations[at] = scale * deviation[i];
                bool inside = System.Math.Abs(column[i] - median[i]) <= sigmas * deviations[at];
                outliers[at] = !inside;
                filtered[at] = inside ? column[i] : median[i];
            }
        }

        return new Hampel(filtered, outliers, medians, deviations);
    }

    // --- Envelope -------------------------------------------------------------------------------

    /// <summary>
    /// <c>envelope</c>: the upper and lower bounds a signal's amplitude traces out, by whichever of
    /// the three readings the caller asks for.
    /// </summary>
    /// <param name="x">The signal, column major.</param>
    /// <param name="rows">How long each column is.</param>
    /// <param name="columns">How many columns there are.</param>
    /// <param name="n">The filter length, window width or peak separation, depending on the method.</param>
    /// <param name="method">One of <c>analytic</c>, <c>rms</c> or <c>peaks</c>.</param>
    public static (double[] Upper, double[] Lower) Envelope(
        double[] x, int rows, int columns, int? n, string method)
    {
        if (method == "peaks")
        {
            return PeakEnvelope(x, rows, columns, n ?? 1);
        }

        var upper = new double[x.Length];
        var lower = new double[x.Length];
        var column = new double[rows];

        for (int c = 0; c < columns; c++)
        {
            Array.Copy(x, c * rows, column, 0, rows);
            double mean = 0;
            foreach (double v in column)
            {
                mean += v;
            }

            mean /= rows;

            var centred = new double[rows];
            for (int i = 0; i < rows; i++)
            {
                centred[i] = column[i] - mean;
            }

            double[] amplitude = method switch
            {
                "rms" => RootMeanSquare(centred, n ?? rows),
                _ when n is int taps => FirEnvelope(centred, taps),
                _ => AnalyticAmplitude(centred),
            };

            for (int i = 0; i < rows; i++)
            {
                upper[(c * rows) + i] = mean + amplitude[i];
                lower[(c * rows) + i] = mean - amplitude[i];
            }
        }

        return (upper, lower);
    }

    /// <summary>The magnitude of the analytic signal, which is the envelope in its exact form.</summary>
    private static double[] AnalyticAmplitude(double[] x)
    {
        Complex[] analytic = SignalTransforms.Hilbert(x, x.Length);
        var amplitude = new double[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            amplitude[i] = Complex.Abs(analytic[i]);
        }

        return amplitude;
    }

    /// <summary>
    /// The magnitude of a truncated analytic signal: a complex Kaiser-windowed filter whose response
    /// is one-sided, convolved and kept where it overlaps.
    /// </summary>
    private static double[] FirEnvelope(double[] x, int n)
    {
        var taps = new Complex[n];
        double sum = 0;
        double[] window = SignalWindows.Kaiser(n, 8);
        for (int i = 0; i < n; i++)
        {
            double t = 0.5 * ((1 - n) / 2.0 + i);
            double sinc = t == 0 ? 1 : System.Math.Sin(System.Math.PI * t) / (System.Math.PI * t);
            Complex value = sinc * Complex.Exp(Complex.ImaginaryOne * System.Math.PI * t) * window[i];
            taps[i] = value;
            sum += value.Real;
        }

        for (int i = 0; i < n; i++)
        {
            taps[i] /= sum;
        }

        // A centred convolution: the same length as the signal, with the filter's own delay removed.
        int offset = (n - 1) / 2;
        var amplitude = new double[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            Complex value = Complex.Zero;
            for (int j = 0; j < n; j++)
            {
                int at = i + offset - j;
                if (at >= 0 && at < x.Length)
                {
                    value += taps[j] * x[at];
                }
            }

            amplitude[i] = Complex.Abs(value);
        }

        return amplitude;
    }

    /// <summary>The running root mean square, which is the envelope of a signal read as power.</summary>
    private static double[] RootMeanSquare(double[] x, int n)
    {
        int behind;
        int ahead;
        if (n % 2 == 0)
        {
            behind = (n / 2) - 1;
            ahead = n / 2;
        }
        else
        {
            behind = (n - 1) / 2;
            ahead = (n - 1) / 2;
        }

        var squares = new double[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            squares[i] = x[i] * x[i];
        }

        double[] means = WindowKernels.Slide(
            WindowStat.Mean, squares, behind, ahead, WindowEnds.Shrink, 0, false, double.NaN);

        var amplitude = new double[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            amplitude[i] = System.Math.Sqrt(means[i]);
        }

        return amplitude;
    }

    /// <summary>
    /// The envelope through the signal's own peaks, interpolated by a cubic spline: not a filter at
    /// all, but the reading a person drawing the envelope by hand would produce.
    /// </summary>
    private static (double[] Upper, double[] Lower) PeakEnvelope(double[] x, int rows, int columns, int n)
    {
        var upper = new double[x.Length];
        var lower = new double[x.Length];

        if (rows < 2)
        {
            Array.Copy(x, upper, x.Length);
            Array.Copy(x, lower, x.Length);
            return (upper, lower);
        }

        var column = new double[rows];
        for (int c = 0; c < columns; c++)
        {
            Array.Copy(x, c * rows, column, 0, rows);
            double[] over = ThroughPeaks(column, n, above: true);
            double[] under = ThroughPeaks(column, n, above: false);
            Array.Copy(over, 0, upper, c * rows, rows);
            Array.Copy(under, 0, lower, c * rows, rows);
        }

        return (upper, lower);
    }

    /// <summary>One side's peaks, splined across every sample.</summary>
    private static double[] ThroughPeaks(double[] x, int n, bool above)
    {
        var signed = new double[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            signed[i] = above ? x[i] : -x[i];
        }

        List<int> peaks = x.Length > n + 1 ? SeparatedPeaks(signed, n) : [];

        var places = new List<int>();
        if (peaks.Count < 2)
        {
            places.Add(0);
            places.AddRange(peaks);
            places.Add(x.Length - 1);
        }
        else
        {
            places.AddRange(peaks);
        }

        var xs = new double[places.Count];
        var ys = new double[places.Count];
        for (int i = 0; i < places.Count; i++)
        {
            xs[i] = places[i] + 1;
            ys[i] = x[places[i]];
        }

        var query = new double[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            query[i] = i + 1;
        }

        return Spline(xs, ys, query);
    }

    /// <summary>
    /// A not-a-knot cubic spline through the peaks, evaluated everywhere including outside them —
    /// which is where the envelope needs it, since the first and last samples are rarely peaks.
    /// </summary>
    private static double[] Spline(double[] xs, double[] ys, double[] query)
    {
        if (xs.Length == 1)
        {
            var flat = new double[query.Length];
            Array.Fill(flat, ys[0]);
            return flat;
        }

        double[] slopes = Interpolation.SplineSlopes(xs, ys);
        var y = new double[query.Length];
        int piece = 0;
        for (int i = 0; i < query.Length; i++)
        {
            while (piece < xs.Length - 2 && query[i] > xs[piece + 1])
            {
                piece++;
            }

            y[i] = Interpolation.Hermite(
                xs[piece], xs[piece + 1], ys[piece], ys[piece + 1], slopes[piece], slopes[piece + 1], query[i]);
        }

        return y;
    }

    /// <summary>
    /// Local maxima no closer together than a given separation, largest first — the part of
    /// <c>findpeaks</c> the envelope needs, ahead of M136 which owns the name.
    /// </summary>
    private static List<int> SeparatedPeaks(double[] x, int separation)
    {
        var candidates = new List<int>();
        for (int i = 1; i < x.Length - 1; i++)
        {
            if (x[i] > x[i - 1] && x[i] >= x[i + 1])
            {
                candidates.Add(i);
            }
        }

        candidates.Sort((a, b) =>
        {
            int byHeight = x[b].CompareTo(x[a]);
            return byHeight != 0 ? byHeight : a.CompareTo(b);
        });

        var kept = new List<int>();
        foreach (int at in candidates)
        {
            bool clear = true;
            foreach (int other in kept)
            {
                if (System.Math.Abs(other - at) < separation)
                {
                    clear = false;
                    break;
                }
            }

            if (clear)
            {
                kept.Add(at);
            }
        }

        kept.Sort();
        return kept;
    }

    // --- Gap filling ----------------------------------------------------------------------------

    /// <summary>
    /// <c>fillgaps</c>: each run of missing samples predicted forwards from what came before it and
    /// backwards from what comes after, and the two guesses crossfaded.
    /// </summary>
    /// <remarks>
    /// A gap in the middle of a signal is filled by an autoregressive model fitted to the segment
    /// beside it and run on into the gap. One such prediction decays towards the segment's mean, so
    /// the fill would have a visible kink at the far edge; running the same thing backwards from the
    /// other side and weighting the two linearly across the gap removes it. A gap at either end has
    /// only one prediction and gets it unweighted.
    /// </remarks>
    public static double[] FillGaps(double[] x, int rows, int columns, double maxLength, int? order)
    {
        (double[] forward, double[] forwardWeight) = FillOneWay(x, rows, columns, maxLength, order);

        var reversed = new double[x.Length];
        for (int c = 0; c < columns; c++)
        {
            for (int i = 0; i < rows; i++)
            {
                reversed[(c * rows) + i] = x[(c * rows) + rows - 1 - i];
            }
        }

        (double[] backwardFlipped, double[] backwardWeightFlipped) =
            FillOneWay(reversed, rows, columns, maxLength, order);

        var y = new double[x.Length];
        for (int c = 0; c < columns; c++)
        {
            for (int i = 0; i < rows; i++)
            {
                int at = (c * rows) + i;
                int mirrored = (c * rows) + rows - 1 - i;
                double wf = forwardWeight[at];
                double wb = backwardWeightFlipped[mirrored];
                y[at] = ((forward[at] * wf) + (backwardFlipped[mirrored] * wb)) / (wf + wb);
            }
        }

        return y;
    }

    /// <summary>One direction's fill and the confidence it carries.</summary>
    private static (double[] Filled, double[] Weight) FillOneWay(
        double[] x, int rows, int columns, double maxLength, int? order)
    {
        var y = new double[x.Length];
        Array.Copy(x, y, x.Length);
        var w = new double[x.Length];
        Array.Fill(w, 1.0);

        for (int c = 0; c < columns; c++)
        {
            int first = -1;
            for (int i = 0; i < rows; i++)
            {
                if (!double.IsNaN(y[(c * rows) + i]))
                {
                    first = i;
                    break;
                }
            }

            if (first < 0)
            {
                continue;
            }

            while (true)
            {
                int gapStart = -1;
                for (int i = 0; i < rows; i++)
                {
                    if (double.IsNaN(y[(c * rows) + i]))
                    {
                        gapStart = i;
                        break;
                    }
                }

                if (gapStart < 0)
                {
                    break;
                }

                int gapEnd = rows - 1;
                for (int i = gapStart; i < rows; i++)
                {
                    if (!double.IsNaN(y[(c * rows) + i]))
                    {
                        gapEnd = i - 1;
                        break;
                    }
                }

                int from = System.Math.Max(first, (int)System.Math.Max(0, gapStart - maxLength));
                int gapLength = gapEnd - gapStart + 1;
                int segment = gapStart - from;

                double[] guess;
                double[] weights;
                if (segment > 1)
                {
                    var history = new double[segment];
                    for (int i = 0; i < segment; i++)
                    {
                        history[i] = y[(c * rows) + from + i];
                    }

                    guess = Predict(history, gapLength, order);
                    weights = GapWeights(gapLength, gapStart, gapEnd, rows);
                }
                else if (gapStart > 0)
                {
                    guess = new double[gapLength];
                    Array.Fill(guess, y[(c * rows) + gapStart - 1]);
                    weights = GapWeights(gapLength, gapStart, gapEnd, rows);
                }
                else
                {
                    guess = new double[gapLength];
                    weights = new double[gapLength];
                }

                for (int i = 0; i < gapLength; i++)
                {
                    y[(c * rows) + gapStart + i] = guess[i];
                    w[(c * rows) + gapStart + i] = weights[i];
                }
            }
        }

        return (y, w);
    }

    /// <summary>A gap's crossfade: flat at either end of the signal, and a ramp in the middle.</summary>
    private static double[] GapWeights(int n, int gapStart, int gapEnd, int rows)
    {
        var w = new double[n];
        if (gapStart == 0 || gapEnd == rows - 1)
        {
            Array.Fill(w, 1.0);
            return w;
        }

        for (int i = 0; i < n; i++)
        {
            w[i] = n - i;
        }

        return w;
    }

    /// <summary>
    /// The autoregressive prediction: Burg's method fits a model to the segment and the model is run
    /// on from the segment's own last samples.
    /// </summary>
    private static double[] Predict(double[] x, int n, int? order)
    {
        double mean = 0;
        foreach (double v in x)
        {
            mean += v;
        }

        mean /= x.Length;

        var centred = new double[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            centred[i] = x[i] - mean;
        }

        double[] a = order is int p
            ? Burg(centred, System.Math.Min(x.Length - 1, p))
            : BurgByInformation(centred);

        foreach (double v in a)
        {
            if (double.IsNaN(v))
            {
                var flat = new double[n];
                Array.Fill(flat, mean);
                return flat;
            }
        }

        var past = new double[centred.Length];
        for (int i = 0; i < centred.Length; i++)
        {
            past[i] = centred[centred.Length - 1 - i];
        }

        double[] state = FilterPasses.InitialConditions([1.0], a, past, []);
        double[] run = DigitalFilter.Filter([1.0], a, new double[n], state);

        var y = new double[n];
        for (int i = 0; i < n; i++)
        {
            y[i] = mean + run[i];
        }

        return y;
    }

    /// <summary>Burg's method at a fixed order.</summary>
    public static double[] Burg(double[] x, int order)
    {
        int n = x.Length;
        var a = new double[order + 1];
        a[0] = 1;
        if (order == 0)
        {
            return a;
        }

        var forward = new double[n - 1];
        var backward = new double[n - 1];
        for (int i = 0; i < n - 1; i++)
        {
            forward[i] = x[i + 1];
            backward[i] = x[i];
        }

        for (int p = 1; p <= order; p++)
        {
            double top = 0;
            double bottom = 0;
            for (int i = 0; i < forward.Length; i++)
            {
                top += backward[i] * forward[i];
                bottom += (forward[i] * forward[i]) + (backward[i] * backward[i]);
            }

            double k = bottom == 0 ? 0 : -2 * top / bottom;

            if (forward.Length > 1)
            {
                var nextForward = new double[forward.Length - 1];
                var nextBackward = new double[forward.Length - 1];
                for (int i = 0; i < nextForward.Length; i++)
                {
                    nextForward[i] = forward[i + 1] + (k * backward[i + 1]);
                    nextBackward[i] = backward[i] + (k * forward[i]);
                }

                forward = nextForward;
                backward = nextBackward;
            }

            var updated = new double[order + 1];
            Array.Copy(a, updated, order + 1);
            for (int i = 1; i < p; i++)
            {
                updated[i] = a[i] + (k * a[p - i]);
            }

            updated[p] = k;
            a = updated;
        }

        return a;
    }

    /// <summary>
    /// Burg's method with the order chosen by Akaike's information criterion, stopping when the
    /// criterion has failed to improve for long enough.
    /// </summary>
    private static double[] BurgByInformation(double[] x)
    {
        int n = x.Length;
        int pmax = n - 1;
        if (pmax < 1)
        {
            return [1.0];
        }

        var forward = new double[n - 1];
        var backward = new double[n - 1];
        for (int i = 0; i < n - 1; i++)
        {
            forward[i] = x[i + 1];
            backward[i] = x[i];
        }

        double energy = 0;
        foreach (double v in x)
        {
            energy += v * v;
        }

        double logE = System.Math.Log(energy / n);
        double best = double.PositiveInfinity;
        double[] bestA = [];
        bool stalled = false;
        int postMax = 30;
        int postCount = 0;

        var a = new double[pmax + 1];
        a[0] = 1;

        for (int p = 2; p <= pmax + 1; p++)
        {
            double top = 0;
            double bottom = 0;
            for (int i = 0; i < forward.Length; i++)
            {
                top += backward[i] * forward[i];
                bottom += (forward[i] * forward[i]) + (backward[i] * backward[i]);
            }

            double k = bottom == 0 ? double.NaN : -2 * top / bottom;

            if (forward.Length > 1)
            {
                var nextForward = new double[forward.Length - 1];
                var nextBackward = new double[forward.Length - 1];
                for (int i = 0; i < nextForward.Length; i++)
                {
                    nextForward[i] = forward[i + 1] + (k * backward[i + 1]);
                    nextBackward[i] = backward[i] + (k * forward[i]);
                }

                forward = nextForward;
                backward = nextBackward;
            }

            logE += System.Math.Log(1 - (k * k));
            double criterion = logE + (2.0 * (p + 1) / n);

            if (criterion < best)
            {
                best = criterion;
                postMax = System.Math.Max(30, p / 4);
                postCount = 0;
                stalled = false;
            }
            else
            {
                postCount++;
                if (!stalled)
                {
                    bestA = a[..(p - 1)];
                    stalled = true;
                }

                if (double.IsNaN(k) || postCount > postMax)
                {
                    break;
                }
            }

            var updated = new double[pmax + 1];
            Array.Copy(a, updated, pmax + 1);
            for (int i = 1; i < p; i++)
            {
                updated[i] = a[i] + (k * a[p - 1 - i]);
            }

            a = updated;
        }

        return stalled ? bestA : a;
    }
}
