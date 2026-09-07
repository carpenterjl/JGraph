using System.Numerics;

namespace JGraph.Signal;

/// <summary>
/// The ways of running a filter over a signal that <see cref="DigitalFilter"/>'s single forward pass
/// does not cover: zero phase, cascaded sections, block convolution and the initial conditions that
/// make a filter start where another one left off (M133).
/// </summary>
/// <remarks>
/// <para>
/// One recurrence, four shapes. <c>filtfilt</c> runs it twice in opposite directions so the phase
/// cancels; <c>sosfilt</c> runs a chain of short ones so the coefficients stay conditioned;
/// <c>fftfilt</c> replaces the recurrence with a transform when the filter is long enough that
/// multiplying spectra costs less than multiplying samples; and <c>filtic</c> computes the delay
/// line that makes a filter behave as though it had already seen a given past.
/// </para>
/// <para>
/// The zero-phase pass itself was written for M132's <c>demod</c> and lives in
/// <see cref="ZeroPhaseFilter"/>. What is added here is the name, the cascade form, the walk down
/// several columns, and — the part that actually matters for parity — MATLAB's two arithmetic
/// routes through the same algorithm.
/// </para>
/// </remarks>
public static class FilterPasses
{
    /// <summary>
    /// Above this many samples MATLAB stops building the reflected signal as one array and filters
    /// the pieces separately, which is the same algebra in a different order and so not the same
    /// last bit.
    /// </summary>
    private const int ConcatenationLimit = 10000;

    /// <summary>
    /// <c>filtfilt</c>: the cascade run forwards and backwards over every column.
    /// </summary>
    /// <param name="b">One numerator per stage, row by row.</param>
    /// <param name="a">One denominator per stage, row by row.</param>
    /// <param name="stages">How many stages the cascade has.</param>
    /// <param name="x">The signal, column major.</param>
    /// <param name="rows">How long each column is.</param>
    /// <param name="columns">How many columns there are.</param>
    public static double[] ZeroPhase(double[,] b, double[,] a, int stages, double[] x, int rows, int columns)
    {
        int p = b.GetLength(1);
        int q = a.GetLength(1);
        int m = System.Math.Max(p, q);

        var bn = new double[stages, m];
        var an = new double[stages, m];
        for (int s = 0; s < stages; s++)
        {
            if (a[s, 0] == 0)
            {
                throw new ArgumentException("A stage's leading denominator coefficient cannot be zero.", nameof(a));
            }

            for (int j = 0; j < p; j++)
            {
                bn[s, j] = b[s, j] / a[s, 0];
            }

            for (int j = 0; j < q; j++)
            {
                an[s, j] = a[s, j] / a[s, 0];
            }
        }

        int order = 0;
        for (int s = 0; s < stages; s++)
        {
            order += System.Math.Max(EffectiveLength(b, s, p), EffectiveLength(a, s, q)) - 1;
        }

        int edge = System.Math.Max(1, 3 * order);
        if (rows <= edge)
        {
            throw new ArgumentException(
                $"Zero-phase filtering needs more than {edge} samples for a filter of order {order}.", nameof(x));
        }

        double[,] zi = SteadyStates(bn, an, stages, m);

        var y = new double[x.Length];
        if (columns == 1 && rows < ConcatenationLimit)
        {
            double[] column = ConcatenatedPass(bn, an, zi, stages, m, x, rows, edge);
            Array.Copy(column, y, rows);
            return y;
        }

        var work = new double[rows];
        for (int c = 0; c < columns; c++)
        {
            Array.Copy(x, c * rows, work, 0, rows);
            double[] filtered = SplitPass(bn, an, zi, stages, m, work, rows, edge);
            Array.Copy(filtered, 0, y, c * rows, rows);
        }

        return y;
    }

    /// <summary>
    /// The route MATLAB takes for one short column: build the reflected signal whole, filter it
    /// forwards, reverse it, filter it again, and read the middle back out backwards.
    /// </summary>
    private static double[] ConcatenatedPass(
        double[,] b, double[,] a, double[,] zi, int stages, int m, double[] x, int rows, int edge)
    {
        var yout = new double[rows];
        Array.Copy(x, yout, rows);

        var ytemp = new double[rows + (2 * edge)];
        for (int s = 0; s < stages; s++)
        {
            for (int i = 0; i < edge; i++)
            {
                ytemp[i] = (2 * yout[0]) - yout[edge - i];
            }

            Array.Copy(yout, 0, ytemp, edge, rows);
            for (int i = 0; i < edge; i++)
            {
                ytemp[edge + rows + i] = (2 * yout[rows - 1]) - yout[rows - 2 - i];
            }

            ytemp = RunFrom(b, a, zi, s, m, ytemp, ytemp[0]);
            Array.Reverse(ytemp);
            ytemp = RunFrom(b, a, zi, s, m, ytemp, ytemp[0]);

            for (int i = 0; i < rows; i++)
            {
                yout[i] = ytemp[rows + edge - 1 - i];
            }
        }

        return yout;
    }

    /// <summary>
    /// The route MATLAB takes for a long column or for several: filter the leading reflection to
    /// pick up a state, carry it through the signal and the trailing reflection, and turn round.
    /// </summary>
    /// <remarks>
    /// This is the same algorithm as the concatenated one and not the same arithmetic. Nothing here
    /// ever holds the reflected signal whole, so the sums that build each output sample are taken in
    /// a different order and the two routes part company in the last figure. MATLAB switches between
    /// them at ten thousand samples; so does this, because a fixture that pins one route would fail
    /// on the other.
    /// </remarks>
    private static double[] SplitPass(
        double[,] b, double[,] a, double[,] zi, int stages, int m, double[] x, int rows, int edge)
    {
        var current = new double[rows];
        Array.Copy(x, current, rows);

        var head = new double[edge];
        var tail = new double[edge];
        for (int s = 0; s < stages; s++)
        {
            for (int i = 0; i < edge; i++)
            {
                head[i] = (2 * current[0]) - current[edge - i];
            }

            var state = new double[m - 1];
            for (int i = 0; i < state.Length; i++)
            {
                state[i] = zi[i, s] * head[0];
            }

            Stage(b, a, s, m, head, state);
            double[] middle = Stage(b, a, s, m, current, state);

            for (int i = 0; i < edge; i++)
            {
                tail[i] = (2 * current[rows - 1]) - current[rows - 2 - i];
            }

            double[] after = Stage(b, a, s, m, tail, state);

            var reversedAfter = new double[edge];
            for (int i = 0; i < edge; i++)
            {
                reversedAfter[i] = after[edge - 1 - i];
            }

            var back = new double[m - 1];
            for (int i = 0; i < back.Length; i++)
            {
                back[i] = zi[i, s] * after[edge - 1];
            }

            Stage(b, a, s, m, reversedAfter, back);

            var reversedMiddle = new double[rows];
            for (int i = 0; i < rows; i++)
            {
                reversedMiddle[i] = middle[rows - 1 - i];
            }

            double[] last = Stage(b, a, s, m, reversedMiddle, back);
            for (int i = 0; i < rows; i++)
            {
                current[i] = last[rows - 1 - i];
            }
        }

        return current;
    }

    /// <summary>One stage's coefficients applied to a signal, updating the state in place.</summary>
    private static double[] Stage(double[,] b, double[,] a, int s, int m, double[] x, double[] state)
    {
        var bn = new double[m];
        var an = new double[m];
        for (int j = 0; j < m; j++)
        {
            bn[j] = b[s, j];
            an[j] = a[s, j];
        }

        return DigitalFilter.Filter(bn, an, x, state);
    }

    /// <summary>One stage started from its scaled steady state, whose own final state is discarded.</summary>
    private static double[] RunFrom(double[,] b, double[,] a, double[,] zi, int s, int m, double[] x, double first)
    {
        var state = new double[m - 1];
        for (int i = 0; i < state.Length; i++)
        {
            state[i] = zi[i, s] * first;
        }

        return Stage(b, a, s, m, x, state);
    }

    /// <summary>The last coefficient of a row that is not zero, after the row is normalised.</summary>
    private static int EffectiveLength(double[,] rows, int s, int width)
    {
        double largest = 0;
        for (int j = 0; j < width; j++)
        {
            largest = System.Math.Max(largest, System.Math.Abs(rows[s, j]));
        }

        for (int j = width - 1; j >= 0; j--)
        {
            double value = largest == 0 ? rows[s, j] : rows[s, j] / largest;
            if (value != 0)
            {
                return j + 1;
            }
        }

        return 0;
    }

    /// <summary>One steady-state delay line per stage, stored one stage per column.</summary>
    private static double[,] SteadyStates(double[,] b, double[,] a, int stages, int m)
    {
        var zi = new double[System.Math.Max(0, m - 1), stages];
        if (m <= 1)
        {
            return zi;
        }

        for (int s = 0; s < stages; s++)
        {
            var bn = new double[m];
            var an = new double[m];
            for (int j = 0; j < m; j++)
            {
                bn[j] = b[s, j];
                an[j] = a[s, j];
            }

            double[] state = ZeroPhaseFilter.SteadyStateOf(bn, an, m);
            for (int i = 0; i < state.Length; i++)
            {
                zi[i, s] = state[i];
            }
        }

        return zi;
    }

    // --- Cascaded sections ------------------------------------------------------------------------

    /// <summary>
    /// <c>sosfilt</c>: each column run through the sections in order, one section's output being the
    /// next one's input.
    /// </summary>
    public static double[] Cascade(double[] sos, int sections, double[] x, int rows, int columns)
    {
        var y = new double[x.Length];
        Array.Copy(x, y, x.Length);

        var b = new double[3];
        var a = new double[3];
        var column = new double[rows];

        for (int s = 0; s < sections; s++)
        {
            double a0 = sos[(3 * sections) + s];
            if (a0 == 0)
            {
                throw new ArgumentException("A section's leading denominator coefficient cannot be zero.", nameof(sos));
            }

            for (int j = 0; j < 3; j++)
            {
                b[j] = sos[(j * sections) + s] / a0;
                a[j] = sos[((3 + j) * sections) + s] / a0;
            }

            for (int c = 0; c < columns; c++)
            {
                Array.Copy(y, c * rows, column, 0, rows);
                double[] filtered = DigitalFilter.Filter(b, a, column);
                Array.Copy(filtered, 0, y, c * rows, rows);
            }
        }

        return y;
    }

    // --- Block convolution ------------------------------------------------------------------------

    /// <summary>
    /// <c>fftfilt</c>: overlap-add convolution, with MATLAB's own cost table choosing the block
    /// length when the caller does not.
    /// </summary>
    /// <remarks>
    /// The table is a measured count of butterfly operations for each power-of-two transform length,
    /// and the choice minimises the number of blocks times the cost of one. It is the reason two
    /// implementations of overlap-add that are both correct give different answers in the last
    /// figure: a different block length is a different grouping of the same additions.
    /// </remarks>
    public static Complex[] BlockConvolve(
        Complex[] b, int bRows, int bColumns, Complex[] x, int rows, int columns, int? nfftGiven)
    {
        int nb = bRows;
        int nx = rows;

        int nfft;
        int blockLength;
        if (nfftGiven is int given)
        {
            int wanted = System.Math.Max(given, nb);
            nfft = 1 << (int)System.Math.Ceiling(System.Math.Log(wanted) / System.Math.Log(2));
            blockLength = nfft - nb + 1;
        }
        else if (nb >= nx || nb > 1 << 20)
        {
            nfft = Fft.NextPowerOfTwo(nb + nx - 1);
            blockLength = nx;
        }
        else
        {
            (nfft, blockLength) = ChooseBlock(nb, nx);
        }

        int wide = System.Math.Max(bColumns, columns);
        var transformed = new Complex[wide][];
        for (int c = 0; c < wide; c++)
        {
            var padded = new Complex[nfft];
            int from = bColumns == 1 ? 0 : c;
            for (int i = 0; i < nb; i++)
            {
                padded[i] = b[(from * nb) + i];
            }

            transformed[c] = Fft.Forward(padded);
        }

        var y = new Complex[nx * wide];
        var block = new Complex[nfft];

        for (int c = 0; c < wide; c++)
        {
            int from = columns == 1 ? 0 : c;
            int start = 0;
            while (start < nx)
            {
                int end = System.Math.Min(start + blockLength - 1, nx - 1);
                Array.Clear(block);
                if (end == start)
                {
                    for (int i = 0; i < nfft; i++)
                    {
                        block[i] = x[(from * nx) + start];
                    }
                }
                else
                {
                    for (int i = start; i <= end; i++)
                    {
                        block[i - start] = x[(from * nx) + i];
                    }
                }

                Complex[] spectrum = Fft.Forward(block);
                for (int i = 0; i < nfft; i++)
                {
                    spectrum[i] *= transformed[c][i];
                }

                Complex[] piece = Fft.Inverse(spectrum);
                int last = System.Math.Min(nx - 1, start + nfft - 1);
                for (int i = start; i <= last; i++)
                {
                    y[(c * nx) + i] += piece[i - start];
                }

                start += blockLength;
            }
        }

        return y;
    }

    /// <summary>MATLAB's transform cost table, indexed by the power of two.</summary>
    private static readonly double[] TransformCost =
    [
        18, 59, 138, 303, 660, 1441, 3150, 6875, 14952, 32373, 69762,
        149647, 319644, 680105, 1441974, 3047619, 6422736, 13500637, 28311786,
        59244791, 59244791 * 2.09,
    ];

    /// <summary>The transform length and block length that convolve this signal most cheaply.</summary>
    private static (int Nfft, int BlockLength) ChooseBlock(int nb, int nx)
    {
        int bestNfft = 0;
        int bestLength = 0;
        double bestCost = double.PositiveInfinity;

        for (int i = 0; i < TransformCost.Length; i++)
        {
            int n = 1 << (i + 1);
            if (n <= nb - 1)
            {
                continue;
            }

            int length = n - (nb - 1);
            double cost = System.Math.Ceiling(nx / (double)length) * TransformCost[i];
            if (cost < bestCost)
            {
                bestCost = cost;
                bestNfft = n;
                bestLength = length;
            }
        }

        return (bestNfft, bestLength);
    }

    // --- Initial conditions -------------------------------------------------------------------------

    /// <summary>
    /// <c>filtic</c>: the delay line a filter would be holding if it had already produced
    /// <paramref name="pastOutput"/> from <paramref name="pastInput"/>.
    /// </summary>
    /// <remarks>
    /// Both pasts are given most recent first, which is the opposite of every other signal in the
    /// toolbox and is worth saying out loud, because a reversed past produces a plausible answer
    /// rather than an error. The state is built by running each side's coefficients backwards over
    /// its own history — a feed-forward filter each way — and adding the two.
    /// </remarks>
    public static double[] InitialConditions(
        ReadOnlySpan<double> b, ReadOnlySpan<double> a, ReadOnlySpan<double> pastOutput, ReadOnlySpan<double> pastInput)
    {
        int na = a.Length;
        int nb = b.Length;
        int m = System.Math.Max(na, nb) - 1;
        if (m < 1)
        {
            return [];
        }

        if (a.Length == 0 || a[0] == 0)
        {
            throw new ArgumentException("The leading denominator coefficient cannot be zero.", nameof(a));
        }

        var x = new double[System.Math.Max(pastInput.Length, System.Math.Max(0, nb - 1))];
        pastInput.CopyTo(x.AsSpan(0, pastInput.Length));

        var yPast = new double[System.Math.Max(pastOutput.Length, System.Math.Max(0, na - 1))];
        pastOutput.CopyTo(yPast.AsSpan(0, pastOutput.Length));

        var state = new double[m];

        if (na - 1 > 0)
        {
            var taps = new double[na - 1];
            for (int i = 0; i < na - 1; i++)
            {
                taps[i] = -a[na - 1 - i] / a[0];
            }

            double[] run = DigitalFilter.Filter(taps, [1.0], yPast.AsSpan(0, na - 1));
            for (int i = 0; i < na - 1; i++)
            {
                state[na - 2 - i] = run[i];
            }
        }

        if (nb - 1 > 0)
        {
            var taps = new double[nb - 1];
            for (int i = 0; i < nb - 1; i++)
            {
                taps[i] = b[nb - 1 - i] / a[0];
            }

            double[] run = DigitalFilter.Filter(taps, [1.0], x.AsSpan(0, nb - 1));
            for (int i = 0; i < nb - 1; i++)
            {
                state[nb - 2 - i] += run[i];
            }
        }

        return state;
    }
}
