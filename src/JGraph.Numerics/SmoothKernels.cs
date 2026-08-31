using System.Buffers;
using System.Numerics;
using System.Numerics.Tensors;
using System.Runtime.InteropServices;

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
    /// A Gaussian-weighted average of every window, whose standard deviation is a fifth of the
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

        double sigma = Math.Max(window / 5.0, 1e-12);
        int width = behind + ahead + 1;
        var kernel = new double[width];
        for (int m = 0; m < width; m++)
        {
            double z = (m - behind) / sigma;
            kernel[m] = Math.Exp(-0.5 * z * z);
        }

        // Every answer is one and the same convolution, the cut-short ones included: a window that
        // runs off the end of the readings is that sum with the absent readings counted as nothing,
        // which is exactly what a convolution does at its own ends. Only the divisor differs, and
        // that is read off a running total of the kernel rather than summed afresh per point. So
        // where the kernel is wide enough to be worth transforming, the ends cost no more than the
        // middle -- and they were the whole of the cost once the middle stopped being walked.
        if (width >= TransformFrom && AllFinite(values))
        {
            var padded = new double[n + width - 1];
            values.CopyTo(padded.AsSpan(behind));
            Transformed(padded, kernel, result);
            Divide(kernel, behind, ahead, result);
            return result;
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
    /// Each answer divided by the taps that landed on a reading -- the whole kernel in the middle,
    /// and only the part of it that reached the readings at either end.
    /// </summary>
    /// <remarks>
    /// The divisors are read off one running total of the kernel rather than summed again for every
    /// point, which is what turns a cut-short end from a cost that grows with the square of the
    /// window into one that does not grow with it at all.
    /// </remarks>
    private static void Divide(
        ReadOnlySpan<double> kernel, int behind, int ahead, Span<double> into)
    {
        int n = into.Length;
        int width = kernel.Length;
        var running = new double[width + 1];
        for (int m = 0; m < width; m++)
        {
            running[m + 1] = running[m] + kernel[m];
        }

        double whole = running[width];
        (int from, int to) = Interior(n, behind, ahead);
        if (to > from)
        {
            Span<double> centre = into.Slice(from, to - from);
            if (whole == 0)
            {
                centre.Fill(double.NaN);
            }
            else
            {
                TensorPrimitives.Divide<double>(centre, whole, centre);
            }
        }

        for (int i = 0; i < n; i++)
        {
            if (i >= from && i < to)
            {
                continue;
            }

            // The taps that landed are the ones whose reading exists: the window starts at
            // i - behind, so tap m reads i - behind + m and the ends clip that from both sides.
            int first = Math.Max(0, behind - i);
            int last = Math.Min(width - 1, behind - i + n - 1);
            double weight = last < first ? 0 : running[last + 1] - running[first];
            into[i] = weight == 0 ? double.NaN : into[i] / weight;
        }
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
                Apply(values, kernel, centre);
            }
            else
            {
                centre.Fill(double.NaN);
            }
        }

        // The ends are fitted rather than kernelled, and the window they are fitted through is not
        // the cut-short one. Before the first whole window fits inside the readings, the fit is
        // taken through the width nearest the point -- which, for every point at that end, is the
        // same readings. A kernel pays for itself when a second window is the same shape, and no
        // two of these are read at the same place, so they are fitted rather than weighed.
        int width = Math.Min(behind + ahead + 1, n);
        var moments = new double[(3 * degree) + 2];
        var row = new double[terms];
        if (weighted)
        {
            // Tricube weights are measured from the point the fit is read at, so a window shared
            // with its neighbour is still a different fit: each of these is solved on its own.
            for (int i = 0; i < n; i++)
            {
                if (i >= from && i < to)
                {
                    continue;
                }

                int start = Math.Clamp(i - behind, 0, n - width);
                result[i] = FitAt(
                    values, start, start + width - 1, i, degree, weighted, moments, normal, row);
            }

            return result;
        }

        // Unweighted, the whole end is one polynomial: same readings, same weights, so the only
        // thing that changes from point to point is where the answer is read off.
        EndFit(values, 0, width, 0, from, degree, moments, normal, row, result);
        EndFit(values, n - width, width, to, n, degree, moments, normal, row, result);
        return result;
    }

    /// <summary>
    /// One end of the readings answered by one fit: the readings under it are the same for every
    /// point there and the weights are all one, so the polynomial does not change from point to
    /// point and only the place it is read at does.
    /// </summary>
    private static void EndFit(
        ReadOnlySpan<double> values, int start, int count, int firstOut, int lastOut, int degree,
        Span<double> moments, Span<double> system, Span<double> row, Span<double> into)
    {
        if (lastOut <= firstOut)
        {
            return;
        }

        // Written in powers of the distance from the middle of its window, measured in halves of
        // that window: the same polynomial either way, but a normal matrix whose entries are all of
        // one size rather than spread over the window's width to twice the degree.
        double centre = start + ((count - 1) / 2.0);
        double reach = Math.Max((count - 1) / 2.0, 1);
        FitThrough(values, start, count, centre, reach, degree, moments, system, row);
        for (int i = firstOut; i < lastOut; i++)
        {
            into[i] = At(row[..(degree + 1)], (i - centre) / reach);
        }
    }

    /// <summary>
    /// An unweighted least-squares polynomial through a stretch of readings, in powers of the
    /// distance from <paramref name="centre"/> measured in units of <paramref name="reach"/>.
    /// </summary>
    /// <remarks>
    /// Both retreats are written as polynomials of their own, so that reading the answer is one
    /// operation whichever road it came by: too few readings to pin the degree is their plain mean,
    /// and a system that will not factor is the same, each a polynomial with no slope.
    /// </remarks>
    private static void FitThrough(
        ReadOnlySpan<double> values, int start, int count, double centre, double reach, int degree,
        Span<double> moments, Span<double> system, Span<double> row)
    {
        int terms = degree + 1;
        row[..terms].Clear();
        if (count <= 0)
        {
            row[0] = double.NaN;
            return;
        }

        if (count <= degree)
        {
            row[0] = Mean(values.Slice(start, count));
            return;
        }

        int highest = 2 * degree;
        moments[..((3 * degree) + 2)].Clear();
        for (int j = start; j < start + count; j++)
        {
            double t = (j - centre) / reach;
            double running = 1;
            for (int p = 0; p <= highest; p++)
            {
                moments[p] += running;
                running *= t;
            }

            running = values[j];
            for (int p = 0; p < terms; p++)
            {
                moments[highest + 1 + p] += running;
                running *= t;
            }
        }

        for (int use = degree; use >= 1; use--)
        {
            if (!Pinned(moments, highest, use, system, row))
            {
                continue;
            }

            row[(use + 1)..terms].Clear();
            return;
        }

        row[..terms].Clear();
        row[0] = Mean(values.Slice(start, count));
    }

    /// <summary>
    /// The normal system of the given degree, read off moments already gathered, and solved.
    /// </summary>
    private static bool Pinned(
        ReadOnlySpan<double> moments, int highest, int degree, Span<double> system,
        Span<double> solution)
    {
        int terms = degree + 1;
        int stride = terms + 1;
        for (int r = 0; r < terms; r++)
        {
            for (int c = 0; c < terms; c++)
            {
                system[(r * stride) + c] = moments[r + c];
            }

            system[(r * stride) + terms] = moments[highest + 1 + r];
        }

        return Solve(system, terms, solution);
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
        for (int use = degree; use >= 1; use--)
        {
            if (!Pin(xs, ys, weights, use, at, normal, powers, coefficients))
            {
                continue;
            }

            coefficients[(use + 1)..terms].Clear();
            return;
        }

        coefficients[..terms].Clear();
        coefficients[0] = WeightedMean(ys, weights);
    }

    /// <summary>
    /// One weighted least-squares polynomial of the given degree, or false when the window cannot
    /// pin one.
    /// </summary>
    /// <remarks>
    /// A window that cannot pin a polynomial of one degree will often pin a lower one, and the
    /// lower fit is what a least-squares solve of the rank-deficient system amounts to: it still
    /// passes through the readings the window can see. Dropping a degree answers those readings,
    /// where retreating straight to a weighted mean answers something in between them.
    /// </remarks>
    private static bool Pin(
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

        return Solve(system, terms, coefficients);
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

        // A lower degree is the same moments read to a shorter row, so stepping down costs
        // nothing but the solve it was going to do anyway.
        for (int use = degree; use >= 1; use--)
        {
            if (Pinned(moments, highest, use, system, solution))
            {
                return solution[0];
            }
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
    /// <summary>
    /// The kernel width from which the frequency domain is the cheaper road.
    /// </summary>
    /// <remarks>
    /// Below it a direct pass wins outright: every tap is one fused multiply-add over contiguous
    /// memory, where a transform pays for two passes over a padded block however narrow the kernel
    /// is. Above it the direct pass costs a multiply-add per tap per reading and the transform
    /// costs a logarithm, so the gap only widens.
    /// </remarks>
    /// <remarks>
    /// Measured over two million readings, the cost of the kernel alone: at sixty-five taps the
    /// direct pass takes 0.016 s against the transform's 0.044; at a hundred and twenty-nine they
    /// meet at 0.031; at two thousand and forty-nine the direct pass takes 0.485 against 0.051.
    /// The direct curve is straight in the width and the transformed one is a logarithm, which is
    /// why they cross once and never again.
    /// </remarks>
    private const int TransformFrom = 128;

    /// <summary>
    /// One kernel applied to every interior window: directly for a narrow kernel, and through the
    /// frequency domain for a wide one, where the same answers cost a logarithm per reading rather
    /// than one multiply-add per tap.
    /// </summary>
    /// <remarks>
    /// The transform is refused outright when the readings hold anything that is not finite. A
    /// direct pass lets a missing reading reach exactly the windows that read it, which is what
    /// <c>'includenan'</c> asks for; a transform spreads it across the whole block it lands in, and
    /// an infinity spreads a NaN over the same ground. That is a different answer rather than a
    /// differently rounded one, so the cheaper road is taken only when there is nothing to spread.
    /// </remarks>
    private static void Apply(
        ReadOnlySpan<double> values, ReadOnlySpan<double> kernel, Span<double> into)
    {
        if (kernel.Length >= TransformFrom && into.Length > 0
            && AllFinite(values[..(into.Length + kernel.Length - 1)]))
        {
            Transformed(values, kernel, into);
            return;
        }

        Scatter(values, kernel, into);
    }

    /// <summary>Whether every reading is a number, which is what prices the transform here.</summary>
    /// <remarks>
    /// A NaN and an infinity both carry an exponent of all ones, so one mask answers for both and
    /// the whole sweep is a handful of comparisons per cache line.
    /// </remarks>
    private static bool AllFinite(ReadOnlySpan<double> values)
    {
        const long Exponent = 0x7FF0000000000000L;
        ReadOnlySpan<long> bits = MemoryMarshal.Cast<double, long>(values);
        int i = 0;
        if (Vector.IsHardwareAccelerated && bits.Length >= Vector<long>.Count)
        {
            var mask = new Vector<long>(Exponent);
            for (; i <= bits.Length - Vector<long>.Count; i += Vector<long>.Count)
            {
                if (Vector.EqualsAny(new Vector<long>(bits.Slice(i, Vector<long>.Count)) & mask, mask))
                {
                    return false;
                }
            }
        }

        for (; i < bits.Length; i++)
        {
            if ((bits[i] & Exponent) == Exponent)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The same answers as <see cref="Scatter"/>, reached through the frequency domain: the
    /// readings are cut into blocks, each block is multiplied by the kernel's own transform, and
    /// the part of each block that no wraparound touched is kept.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Overlap-save rather than overlap-add, because what is wanted here is exactly the part of the
    /// convolution that no zero padding contributed to -- so the answers can be kept as they come
    /// out, with nothing to add together afterwards.
    /// </para>
    /// <para>
    /// Two blocks ride in every transform. A circular convolution with a real kernel is a
    /// real-linear operation, so it leaves the two halves of a complex signal alone: put one block
    /// in the real part and the next in the imaginary part and both come back convolved, for one
    /// forward and one inverse transform rather than two of each. Nothing is separated afterwards
    /// and nothing is approximated by it -- the halves never mix in the first place.
    /// </para>
    /// <para>
    /// The block is four times the kernel, which is close to the cheapest that shape gets: a
    /// shorter block spends most of its transform on the overlap it has to throw away, and a longer
    /// one pays a bigger logarithm for the room it gains.
    /// </para>
    /// </remarks>
    private static void Transformed(
        ReadOnlySpan<double> values, ReadOnlySpan<double> kernel, Span<double> into)
    {
        int width = kernel.Length;
        int size = FftKernels.NextPowerOfTwo(Math.Max(4 * width, 1024));
        int once = FftKernels.NextPowerOfTwo(into.Length + width - 1);
        if (once < size)
        {
            size = once;
        }

        int block = size - width + 1;
        var pool = ArrayPool<double>.Shared;
        double[] rentKernelRe = pool.Rent(size);
        double[] rentKernelIm = pool.Rent(size);
        double[] rentRe = pool.Rent(size);
        double[] rentIm = pool.Rent(size);
        try
        {
            Span<double> kernelRe = rentKernelRe.AsSpan(0, size);
            Span<double> kernelIm = rentKernelIm.AsSpan(0, size);
            kernelRe.Clear();
            kernelIm.Clear();

            // Turned round, because a window read forwards is a convolution read backwards: the
            // answer at a point is the kernel dotted with the readings from that point on, which is
            // those readings convolved with the kernel reversed.
            for (int m = 0; m < width; m++)
            {
                kernelRe[m] = kernel[width - 1 - m];
            }

            FftKernels.Transform(kernelRe, kernelIm, size, inverse: false);

            Span<double> re = rentRe.AsSpan(0, size);
            Span<double> im = rentIm.AsSpan(0, size);
            for (int at = 0; at < into.Length; at += 2 * block)
            {
                Load(values, at, re);
                Load(values, at + block, im);
                FftKernels.Transform(re, im, size, inverse: false);
                for (int k = 0; k < size; k++)
                {
                    double a = re[k];
                    double b = im[k];
                    re[k] = (a * kernelRe[k]) - (b * kernelIm[k]);
                    im[k] = (a * kernelIm[k]) + (b * kernelRe[k]);
                }

                FftKernels.Transform(re, im, size, inverse: true);
                Save(re, width, at, block, into);
                Save(im, width, at + block, block, into);
            }
        }
        finally
        {
            pool.Return(rentKernelRe);
            pool.Return(rentKernelIm);
            pool.Return(rentRe);
            pool.Return(rentIm);
        }
    }

    /// <summary>One block of readings, zero-filled past the end of what there is to read.</summary>
    /// <remarks>
    /// The zeros are not padding in the usual sense: they only ever feed answers that lie past the
    /// last one wanted, which are thrown away with the wraparound.
    /// </remarks>
    private static void Load(ReadOnlySpan<double> values, int at, Span<double> into)
    {
        int held = at >= values.Length ? 0 : Math.Min(values.Length - at, into.Length);
        if (held > 0)
        {
            values.Slice(at, held).CopyTo(into);
        }

        into[held..].Clear();
    }

    /// <summary>
    /// The part of a block that no wraparound touched. The first <c>width - 1</c> answers in a
    /// circular convolution are the ones the wrap reached; every one after them is whole.
    /// </summary>
    private static void Save(
        ReadOnlySpan<double> block, int width, int at, int count, Span<double> into)
    {
        if (at >= into.Length)
        {
            return;
        }

        int held = Math.Min(count, into.Length - at);
        block.Slice(width - 1, held).CopyTo(into.Slice(at, held));
    }

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
