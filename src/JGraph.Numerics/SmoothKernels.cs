using System.Numerics.Tensors;

namespace JGraph.Numerics;

/// <summary>
/// The smoothing methods whose window is a fixed <em>shape</em> rather than a running summary.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="WindowKernels"/> carries a window whose answer is a fold — a sum, a largest, a
/// median — from one point to the next. The methods here cannot be carried, because each answer
/// weighs the whole window and the weights are not all one: a Gaussian average leans on the middle
/// of its window, and a local polynomial fit leans on the middle twice, once through the weights
/// and once through the fit. Nothing about them is a running total.
/// </para>
/// <para>
/// What they are instead is a <em>constant kernel</em>, and that is just as good. When the readings
/// are evenly spaced, every interior window sees the same offsets from its own centre, so every
/// interior window weighs its neighbours by the same numbers; and a least-squares fit read off at
/// one point is a linear functional of the values it was fitted through, so it too collapses to one
/// row of numbers. Work that row out once and each answer is a dot product — which is the
/// difference between rebuilding a normal system per output sample and one fused multiply-add per
/// reading. Only the ends, where the window is cut short and the offsets differ point by point, are
/// still worked out one at a time.
/// </para>
/// <para>
/// The kernel is a different route to the same number rather than the same arithmetic, so the last
/// place can move: a fit reached by solving a normal system per window and one reached by applying
/// that system's answer as a row of weights do not round identically. What does not move is the
/// shape of the answer — the same window, the same weights, the same degree, and the same three
/// retreats when a window cannot support the question asked of it: nothing at all in the window is
/// missing, too few readings for the degree is their plain mean, and a system that will not factor
/// is their weighted mean.
/// </para>
/// </remarks>
public static class SmoothKernels
{
    /// <summary>
    /// How many answers are worked out before moving on. One tap of the kernel is applied across a
    /// tile at a time so that the tile, and the stretch of readings it draws on, both stay in cache
    /// while every remaining tap is applied to them — the difference between reading the whole
    /// series once per tap and reading it once per tile.
    /// </summary>
    private const int Tile = 4096;

    /// <summary>The pivot below which a normal system is called unsolvable, as the walk called it.</summary>
    private const double Singular = 1e-12;

    /// <summary>How many readings are missing, which is what prices a constant kernel here.</summary>
    /// <remarks>
    /// A window that drops its missing readings is not the same shape as one that keeps them, so
    /// with <c>'omitnan'</c> asked for there is no one row of weights that answers the windows a
    /// missing reading falls in. There is still one that answers all the others, which is why the
    /// count matters and not merely whether the count is zero.
    /// </remarks>
    public static int Missing(ReadOnlySpan<double> values)
    {
        int missing = 0;
        foreach (double value in values)
        {
            if (double.IsNaN(value))
            {
                missing++;
            }
        }

        return missing;
    }

    /// <summary>
    /// A Gaussian-weighted average of every window, whose standard deviation is a quarter of the
    /// window — the same weights the walk computed, worked out once instead of once per sample.
    /// </summary>
    public static double[] Gaussian(ReadOnlySpan<double> values, int behind, int ahead, double window)
    {
        int n = values.Length;
        var result = new double[n];
        if (n == 0)
        {
            return result;
        }

        double sigma = Math.Max(window / 4.0, 1e-12);
        int width = behind + ahead + 1;
        var kernel = new double[width];
        for (int m = 0; m < width; m++)
        {
            double z = (m - behind) / sigma;
            kernel[m] = Math.Exp(-0.5 * z * z);
        }

        (int from, int to) = Interior(n, behind, ahead);
        if (to > from)
        {
            double whole = 0;
            foreach (double tap in kernel)
            {
                whole += tap;
            }

            Span<double> centre = result.AsSpan(from, to - from);
            Scatter(values, kernel, centre);
            if (whole == 0)
            {
                centre.Fill(double.NaN);
            }
            else
            {
                TensorPrimitives.Divide<double>(centre, whole, centre);
            }
        }

        // The ends, where the window is cut short: the same taps, but only the ones that landed on
        // a reading, and divided by only those.
        for (int i = 0; i < n; i++)
        {
            if (i >= from && i < to)
            {
                continue;
            }

            int start = Math.Max(0, i - behind);
            int stop = Math.Min(n - 1, i + ahead);
            ReadOnlySpan<double> taps = kernel.AsSpan(start - i + behind, stop - start + 1);
            double weight = TensorPrimitives.Sum<double>(taps);
            result[i] = weight == 0
                ? double.NaN
                : TensorPrimitives.Dot<double>(values.Slice(start, stop - start + 1), taps) / weight;
        }

        return result;
    }

    /// <summary>
    /// A polynomial of the given degree fitted through each window and read off at its centre —
    /// lowess at degree one, loess at degree two, and Savitzky&#8211;Golay with the tricube weights
    /// turned off.
    /// </summary>
    public static double[] LocalPolynomial(
        ReadOnlySpan<double> values, int behind, int ahead, int degree, bool weighted)
    {
        int n = values.Length;
        var result = new double[n];
        if (n == 0)
        {
            return result;
        }

        int terms = degree + 1;
        var kernel = new double[behind + ahead + 1];
        // The system, and room after it for the row that solving it produces.
        var normal = new double[(terms * (terms + 1)) + terms];
        var powers = new double[terms];

        (int from, int to) = Interior(n, behind, ahead);
        if (to > from)
        {
            Span<double> centre = result.AsSpan(from, to - from);
            if (KernelFor(-behind, ahead, degree, weighted, kernel, normal, powers))
            {
                Scatter(values, kernel, centre);
            }
            else
            {
                centre.Fill(double.NaN);
            }
        }

        // The ends are fitted rather than kernelled. A kernel pays for itself when a second window
        // is the same shape, and no two cut-short windows are: working one out and then applying it
        // costs more than fitting through the readings once.
        var moments = new double[(3 * degree) + 2];
        var row = new double[terms];
        for (int i = 0; i < n; i++)
        {
            if (i >= from && i < to)
            {
                continue;
            }

            result[i] = FitAt(
                values, Math.Max(0, i - behind), Math.Min(n - 1, i + ahead), i, degree, weighted,
                moments, normal, row);
        }

        return result;
    }

    /// <summary>
    /// A weighted least-squares polynomial through one window, in powers of the distance from
    /// <paramref name="at"/> — so its constant term is the fitted value there and the rest of it
    /// answers everywhere else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Solved once and then read at as many places as the caller needs. The robust fits used to
    /// solve the system afresh for every residual they measured, which is n systems where one does:
    /// a residual asks the same polynomial about a different place, not a different polynomial
    /// about the same one.
    /// </para>
    /// <para>
    /// A system that will not factor leaves the window's weighted mean as a constant, which is what
    /// the walk answered in that case and is a polynomial like any other.
    /// </para>
    /// <para>
    /// The caller owns the scratch: <c>normal</c> holds the augmented system and needs
    /// <c>(degree + 1) * (degree + 2)</c> places, <c>powers</c> needs <c>degree + 1</c>.
    /// </para>
    /// </remarks>
    public static void Fit(
        ReadOnlySpan<double> xs, ReadOnlySpan<double> ys, ReadOnlySpan<double> weights,
        int degree, double at, Span<double> normal, Span<double> powers, Span<double> coefficients)
    {
        int terms = degree + 1;
        int stride = terms + 1;
        Span<double> system = normal[..(terms * stride)];
        system.Clear();
        for (int i = 0; i < xs.Length; i++)
        {
            double weight = weights[i];
            if (weight <= 0)
            {
                continue;
            }

            double running = 1;
            for (int p = 0; p < terms; p++)
            {
                powers[p] = running;
                running *= xs[i] - at;
            }

            for (int r = 0; r < terms; r++)
            {
                for (int c = 0; c < terms; c++)
                {
                    system[(r * stride) + c] += weight * powers[r] * powers[c];
                }

                system[(r * stride) + terms] += weight * powers[r] * ys[i];
            }
        }

        if (Solve(system, terms, coefficients))
        {
            return;
        }

        coefficients[..terms].Clear();
        coefficients[0] = WeightedMean(ys, weights);
    }

    /// <summary>A polynomial in powers of <paramref name="t"/>, read from its lowest term up.</summary>
    public static double At(ReadOnlySpan<double> coefficients, double t)
    {
        double value = coefficients[^1];
        for (int p = coefficients.Length - 2; p >= 0; p--)
        {
            value = (value * t) + coefficients[p];
        }

        return value;
    }

    /// <summary>Each reading's share of what the window weighs, or their plain mean if it weighs nothing.</summary>
    public static double WeightedMean(ReadOnlySpan<double> ys, ReadOnlySpan<double> weights)
    {
        double total = 0;
        double weight = 0;
        for (int i = 0; i < ys.Length; i++)
        {
            total += weights[i] * ys[i];
            weight += weights[i];
        }

        return weight == 0 ? Mean(ys) : total / weight;
    }

    /// <summary>The plain mean, summed in the order the readings arrive.</summary>
    public static double Mean(ReadOnlySpan<double> ys)
    {
        double total = 0;
        foreach (double y in ys)
        {
            total += y;
        }

        return total / ys.Length;
    }

    /// <summary>
    /// The fitted value at one point of a window that has been cut short.
    /// </summary>
    /// <remarks>
    /// The normal system is built from sums of powers rather than from an outer product per
    /// reading: every entry of the matrix depends on its row plus its column and no more, so a
    /// window of width W needs 2d+1 running sums instead of (d+1)&#178; of them, and the powers
    /// themselves are carried by one multiplication each rather than rebuilt per entry.
    /// </remarks>
    private static double FitAt(
        ReadOnlySpan<double> values, int start, int stop, int at, int degree, bool weighted,
        Span<double> moments, Span<double> system, Span<double> solution)
    {
        int count = stop - start + 1;
        if (count <= 0)
        {
            return double.NaN;
        }

        if (count <= degree)
        {
            return Mean(values.Slice(start, count));
        }

        int terms = degree + 1;
        int highest = 2 * degree;
        moments[..((3 * degree) + 2)].Clear();
        double furthest = Math.Max(Math.Abs(start - at), Math.Abs(stop - at));
        double whole = 0;
        double leaning = 0;
        for (int j = start; j <= stop; j++)
        {
            double t = j - at;
            double weight = 1;
            if (weighted && furthest != 0)
            {
                double u = Math.Abs(t) / furthest;
                double tri = 1 - (u * u * u);
                weight = Math.Max(0, tri * tri * tri);
            }

            whole += weight;
            leaning += weight * values[j];
            if (weight <= 0)
            {
                continue;
            }

            double running = weight;
            for (int p = 0; p <= highest; p++)
            {
                moments[p] += running;
                running *= t;
            }

            running = weight * values[j];
            for (int p = 0; p < terms; p++)
            {
                moments[highest + 1 + p] += running;
                running *= t;
            }
        }

        int stride = terms + 1;
        for (int r = 0; r < terms; r++)
        {
            for (int c = 0; c < terms; c++)
            {
                system[(r * stride) + c] = moments[r + c];
            }

            system[(r * stride) + terms] = moments[highest + 1 + r];
        }

        if (Solve(system, terms, solution))
        {
            return solution[0];
        }

        return whole == 0 ? Mean(values.Slice(start, count)) : leaning / whole;
    }

    /// <summary>The stretch of answers whose window fits inside the readings without being cut short.</summary>
    private static (int From, int To) Interior(int n, int behind, int ahead)
    {
        int from = Math.Min(behind, n);
        return (from, Math.Max(from, n - ahead));
    }

    /// <summary>
    /// One tap of the kernel applied across a tile of answers at a time. The answer at offset
    /// <c>q</c> from the first interior point draws on the reading at <c>q + m</c> for tap
    /// <c>m</c>, so no index arithmetic survives into the inner loop and a whole tile is one fused
    /// multiply-add over contiguous memory.
    /// </summary>
    private static void Scatter(ReadOnlySpan<double> values, ReadOnlySpan<double> kernel, Span<double> into)
    {
        into.Clear();
        for (int at = 0; at < into.Length; at += Tile)
        {
            int length = Math.Min(Tile, into.Length - at);
            Span<double> tile = into.Slice(at, length);
            for (int m = 0; m < kernel.Length; m++)
            {
                double tap = kernel[m];

                // A tap of exactly zero is a reading the fit was told to disregard — the tricube
                // weight at the far edge of a window is zero — and disregarding one is not the same
                // as multiplying by it, because a missing reading times zero is still missing.
                if (tap == 0)
                {
                    continue;
                }

                TensorPrimitives.MultiplyAdd<double>(values.Slice(at + m, length), tap, tile, tile);
            }
        }
    }

    /// <summary>
    /// The row of weights that turns the readings of one window into the fitted value at the point
    /// the window is centred on, for offsets running from <paramref name="lo"/> to
    /// <paramref name="hi"/>. False when the window holds nothing at all.
    /// </summary>
    private static bool KernelFor(
        int lo, int hi, int degree, bool weighted,
        Span<double> kernel, Span<double> normal, Span<double> powers)
    {
        int count = hi - lo + 1;
        if (count <= 0)
        {
            return false;
        }

        // Too few readings to pin a polynomial of this degree: the walk answered their plain mean,
        // weights and all disregarded, and so does this.
        if (count <= degree)
        {
            kernel[..count].Fill(1.0 / count);
            return true;
        }

        double furthest = Math.Max(Math.Abs(lo), Math.Abs(hi));
        for (int m = 0; m < count; m++)
        {
            if (!weighted || furthest == 0)
            {
                kernel[m] = 1;
                continue;
            }

            double u = Math.Abs(lo + m) / furthest;
            double tri = 1 - (u * u * u);
            kernel[m] = Math.Max(0, tri * tri * tri);
        }

        int terms = degree + 1;
        int stride = terms + 1;
        Span<double> system = normal[..(terms * stride)];
        system.Clear();
        for (int m = 0; m < count; m++)
        {
            double weight = kernel[m];
            if (weight <= 0)
            {
                continue;
            }

            double running = 1;
            for (int p = 0; p < terms; p++)
            {
                powers[p] = running;
                running *= lo + m;
            }

            for (int r = 0; r < terms; r++)
            {
                for (int c = 0; c < terms; c++)
                {
                    system[(r * stride) + c] += weight * powers[r] * powers[c];
                }
            }
        }

        // The fitted value at the centre is the constant term, so the row that produces it is the
        // first row of the inverted normal matrix read back through the design — and solving
        // against the first unit vector is that row, at the price of one solve rather than an
        // inversion.
        system[terms] = 1;
        Span<double> solution = normal[(terms * stride)..][..terms];
        if (!Solve(system, terms, solution))
        {
            // A system that will not factor was answered by the weighted mean, which is itself a
            // row of weights: each reading's own share of what the window weighs in total.
            double whole = 0;
            for (int m = 0; m < count; m++)
            {
                whole += kernel[m];
            }

            if (whole == 0)
            {
                kernel[..count].Fill(1.0 / count);
                return true;
            }

            for (int m = 0; m < count; m++)
            {
                kernel[m] /= whole;
            }

            return true;
        }

        for (int m = 0; m < count; m++)
        {
            double weight = kernel[m];
            if (weight <= 0)
            {
                kernel[m] = 0;
                continue;
            }

            double running = 1;
            double row = 0;
            for (int p = 0; p < terms; p++)
            {
                row += solution[p] * running;
                running *= lo + m;
            }

            kernel[m] = weight * row;
        }

        return true;
    }

    /// <summary>
    /// Gaussian elimination with partial pivoting over an augmented row-major system, which is the
    /// solver the walk used and reaches the same verdict about which systems will not factor.
    /// </summary>
    private static bool Solve(Span<double> system, int terms, Span<double> solution)
    {
        int stride = terms + 1;
        for (int pivot = 0; pivot < terms; pivot++)
        {
            int best = pivot;
            for (int r = pivot + 1; r < terms; r++)
            {
                if (Math.Abs(system[(r * stride) + pivot]) > Math.Abs(system[(best * stride) + pivot]))
                {
                    best = r;
                }
            }

            if (Math.Abs(system[(best * stride) + pivot]) < Singular)
            {
                return false;
            }

            if (best != pivot)
            {
                for (int c = pivot; c <= terms; c++)
                {
                    (system[(pivot * stride) + c], system[(best * stride) + c]) =
                        (system[(best * stride) + c], system[(pivot * stride) + c]);
                }
            }

            for (int r = pivot + 1; r < terms; r++)
            {
                double factor = system[(r * stride) + pivot] / system[(pivot * stride) + pivot];
                for (int c = pivot; c <= terms; c++)
                {
                    system[(r * stride) + c] -= factor * system[(pivot * stride) + c];
                }
            }
        }

        for (int r = terms - 1; r >= 0; r--)
        {
            double sum = system[(r * stride) + terms];
            for (int c = r + 1; c < terms; c++)
            {
                sum -= system[(r * stride) + c] * solution[c];
            }

            solution[r] = sum / system[(r * stride) + r];
        }

        return true;
    }
}
