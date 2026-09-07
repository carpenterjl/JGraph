using System.Numerics;
using JGraph.Numerics;

namespace JGraph.Signal;

/// <summary>
/// Cascades of short filters — second-order sections and their fourth-order cousin — and the
/// pairing, ordering and scaling rules that decide which root goes in which section (M133).
/// </summary>
/// <remarks>
/// <para>
/// A tenth-order filter written as one ratio of polynomials cannot be evaluated. Its coefficients
/// span so many decades that the leading ones are noise by the time the trailing ones are exact, and
/// a rounding error in a coefficient moves a pole by far more than the rounding error in the pole
/// would. Written as five second-order sections in a row it is fine, because each section's
/// coefficients are the sum and product of one conjugate pair and nothing more.
/// </para>
/// <para>
/// So the interesting part of this file is not the arithmetic — a section is a quadratic over a
/// quadratic — but the bookkeeping. Which pole pairs with which zero, which section comes first, and
/// how the overall gain is spread across the sections all change the answer's last digits and its
/// overflow behaviour, and none of them changes the filter. MATLAB's rules are: poles ordered by
/// distance from the unit circle, each zero pulled to the pole nearest it, and the gain either
/// embedded in the first section or handed back separately.
/// </para>
/// </remarks>
public static class SecondOrderSections
{
    /// <summary>How the sections are ordered relative to the unit circle.</summary>
    public enum Direction
    {
        /// <summary>Poles closest to the origin first.</summary>
        Up,

        /// <summary>Poles closest to the unit circle first.</summary>
        Down,
    }

    /// <summary>How the numerators are scaled across the cascade.</summary>
    public enum Scaling
    {
        /// <summary>No scaling: the gain stays where it is.</summary>
        None,

        /// <summary>Infinity-norm scaling, which minimises the chance of overflow.</summary>
        Infinity,

        /// <summary>Two-norm scaling, which minimises the peak roundoff noise.</summary>
        Two,
    }

    /// <summary>A cascade and the gain that has not been folded into it.</summary>
    /// <param name="Sections">One row per section, six or ten columns wide.</param>
    /// <param name="Rows">How many sections there are.</param>
    /// <param name="Columns">Six for second-order sections, ten for fourth-order ones.</param>
    /// <param name="Gain">The gain the cascade does not already carry.</param>
    public readonly record struct Cascade(double[] Sections, int Rows, int Columns, double Gain);

    // --- Zero-pole-gain to a cascade ------------------------------------------------------------

    /// <summary>
    /// <c>zp2sos</c>: the roots grouped into second-order sections, ordered and scaled.
    /// </summary>
    /// <param name="zeros">The numerator's roots.</param>
    /// <param name="poles">The denominator's roots.</param>
    /// <param name="gain">The overall gain.</param>
    /// <param name="direction">Which end of the cascade the poles nearest the circle go.</param>
    /// <param name="scaling">Which norm, if any, the numerators are scaled by.</param>
    /// <param name="keepRealZeroPairs">
    /// Whether real zeros that are each other's negative are kept together, which makes a section's
    /// middle numerator coefficient zero.
    /// </param>
    public static Cascade FromRoots(
        ReadOnlySpan<Complex> zeros, ReadOnlySpan<Complex> poles, double gain,
        Direction direction = Direction.Up, Scaling scaling = Scaling.None, bool keepRealZeroPairs = false) =>
        FromRoots(zeros, poles, gain, 2, direction, scaling, keepRealZeroPairs);

    /// <summary>
    /// The same, at a chosen section order: two for <c>zp2sos</c>, four for <c>zp2ctf</c>'s wider
    /// sections, which pair two conjugate pairs into one quartic and lose less to rounding again.
    /// </summary>
    public static Cascade FromRoots(
        ReadOnlySpan<Complex> zeros, ReadOnlySpan<Complex> poles, double gain, int sectionOrder,
        Direction direction, Scaling scaling, bool keepRealZeroPairs)
    {
        int lz = zeros.Length;
        int lp = poles.Length;
        if (lz > lp)
        {
            throw new ArgumentException(
                "A cascade cannot have more zeros than poles.", nameof(zeros));
        }

        int sections = (int)System.Math.Ceiling(lp / (double)sectionOrder);
        if (sections == 0)
        {
            sections = 1;
        }

        Complex[] orderedZeros = lz == 0 ? [] : PhaseSequences.ConjugatePairs(zeros, PhaseSequences.PairingTolerance);
        Complex[] orderedPoles = lp == 0 ? [] : PhaseSequences.ConjugatePairs(poles, PhaseSequences.PairingTolerance);

        (Complex[] zConjugate, double[] zReal) = SplitReal(orderedZeros);
        (Complex[] pConjugate, double[] pReal) = SplitReal(orderedPoles);

        (Complex[] pairedPoles, Complex[] pConjugateOrdered, double[] pRealOrdered) = OrderPoles(pConjugate, pReal);
        Complex[] pairedZeros = OrderZeros(zConjugate, pConjugateOrdered, zReal, pRealOrdered, keepRealZeroPairs);

        int width = sectionOrder == 4 ? 10 : 6;
        double[] rows = sectionOrder == 4
            ? FormFourthOrder(pairedZeros, pairedPoles, lz, lp, sections)
            : FormSecondOrder(pairedZeros, pairedPoles, lz, lp, sections);

        int count = rows.Length / width;
        if (direction == Direction.Down)
        {
            rows = FlipRows(rows, count, width);
        }

        return Scale(rows, count, width, gain, scaling);
    }

    /// <summary>The complex values and the real ones, kept apart because they pair differently.</summary>
    private static (Complex[] Conjugate, double[] Real) SplitReal(ReadOnlySpan<Complex> values)
    {
        var complex = new List<Complex>();
        var real = new List<double>();
        foreach (Complex v in values)
        {
            if (System.Math.Abs(v.Imaginary) > 0)
            {
                complex.Add(v);
            }
            else
            {
                real.Add(v.Real);
            }
        }

        return ([.. complex], [.. real]);
    }

    /// <summary>
    /// Poles ordered by distance from the unit circle, the complex ones before the real ones.
    /// </summary>
    /// <remarks>
    /// The distance is measured to the point on the circle at the pole's own angle, which for a real
    /// pole is plus or minus one — so a pole at 0.99 and a pole at −0.99 are equally close, and a
    /// pole at zero is as far away as it gets.
    /// </remarks>
    private static (Complex[] All, Complex[] Conjugate, double[] Real) OrderPoles(Complex[] conjugate, double[] real)
    {
        var complexOrder = new int[conjugate.Length];
        var complexDistance = new double[conjugate.Length];
        for (int i = 0; i < conjugate.Length; i++)
        {
            complexOrder[i] = i;
            Complex onCircle = Complex.Exp(Complex.ImaginaryOne * conjugate[i].Phase);
            complexDistance[i] = Complex.Abs(conjugate[i] - onCircle);
        }

        StableSort(complexOrder, complexDistance);

        var realOrder = new int[real.Length];
        var realDistance = new double[real.Length];
        for (int i = 0; i < real.Length; i++)
        {
            realOrder[i] = i;
            realDistance[i] = System.Math.Abs(real[i] - System.Math.Sign(real[i]));
        }

        StableSort(realOrder, realDistance);

        var conjugateOut = new Complex[conjugate.Length];
        for (int i = 0; i < conjugate.Length; i++)
        {
            conjugateOut[i] = conjugate[complexOrder[i]];
        }

        var realOut = new double[real.Length];
        for (int i = 0; i < real.Length; i++)
        {
            realOut[i] = real[realOrder[i]];
        }

        var all = new Complex[conjugate.Length + real.Length];
        for (int i = 0; i < conjugateOut.Length; i++)
        {
            all[i] = conjugateOut[i];
        }

        for (int i = 0; i < realOut.Length; i++)
        {
            all[conjugateOut.Length + i] = realOut[i];
        }

        return (all, conjugateOut, realOut);
    }

    /// <summary>
    /// Zeros ordered by which pole they are nearest, the conjugate pairs first and the real ones
    /// after, which is what puts a section's zero beside the pole it most nearly cancels.
    /// </summary>
    private static Complex[] OrderZeros(
        Complex[] zConjugate, Complex[] pConjugate, double[] zReal, double[] pReal, bool keepRealPairs)
    {
        var conjugate = new List<Complex>(zConjugate);
        var poleConjugate = new List<Complex>(pConjugate);
        var poleReal = new List<double>(pReal);
        var ordered = new List<Complex>(zConjugate.Length + zReal.Length);

        int pairs = zConjugate.Length / 2;
        for (int i = 0; i < pairs; i++)
        {
            if (poleConjugate.Count > 0)
            {
                int first = Nearest(conjugate, poleConjugate[0]);
                ordered.Add(conjugate[first]);
                conjugate.RemoveAt(first);

                int second = Nearest(conjugate, poleConjugate[1]);
                ordered.Add(conjugate[second]);
                conjugate.RemoveAt(second);

                poleConjugate.RemoveRange(0, System.Math.Min(2, poleConjugate.Count));
            }
            else if (poleReal.Count > 0)
            {
                int at = Nearest(conjugate, poleReal[0]);
                ordered.Add(conjugate[at]);
                ordered.Add(conjugate[System.Math.Min(at + 1, conjugate.Count - 1)]);
                conjugate.RemoveRange(at, System.Math.Min(2, conjugate.Count - at));
                poleReal.RemoveRange(0, System.Math.Min(2, poleReal.Count));
            }
            else
            {
                ordered.AddRange(conjugate);
                conjugate.Clear();
                break;
            }
        }

        var remaining = new List<double>(zReal);
        if (keepRealPairs && remaining.Count > 0)
        {
            var left = new List<double>();
            while (remaining.Count > 0)
            {
                double head = remaining[0];
                int mirror = head == 0 ? -1 : remaining.FindIndex(v => v == -head);
                if (mirror >= 0 && head != 0)
                {
                    ordered.Add(head);
                    ordered.Add(remaining[mirror]);
                    remaining.RemoveAt(mirror);
                    remaining.RemoveAt(0);
                }
                else
                {
                    left.Add(head);
                    remaining.RemoveAt(0);
                }
            }

            remaining = left;
        }

        int count = remaining.Count;
        for (int i = 0; i < count; i++)
        {
            if (poleConjugate.Count > 0)
            {
                int at = NearestReal(remaining, poleConjugate[0]);
                ordered.Add(remaining[at]);
                remaining.RemoveAt(at);
                poleConjugate.RemoveAt(0);
            }
            else if (poleReal.Count > 0)
            {
                int at = NearestReal(remaining, poleReal[0]);
                ordered.Add(remaining[at]);
                remaining.RemoveAt(at);
                poleReal.RemoveAt(0);
            }
            else
            {
                foreach (double v in remaining)
                {
                    ordered.Add(v);
                }

                break;
            }
        }

        return [.. ordered];
    }

    /// <summary>The index of the value nearest a given point, first one winning a tie.</summary>
    private static int Nearest(List<Complex> values, Complex to)
    {
        int at = 0;
        double best = double.PositiveInfinity;
        for (int i = 0; i < values.Count; i++)
        {
            double distance = Complex.Abs(values[i] - to);
            if (distance < best)
            {
                best = distance;
                at = i;
            }
        }

        return at;
    }

    /// <summary>The same, over a list of real values.</summary>
    private static int NearestReal(List<double> values, Complex to)
    {
        int at = 0;
        double best = double.PositiveInfinity;
        for (int i = 0; i < values.Count; i++)
        {
            double distance = Complex.Abs(values[i] - to);
            if (distance < best)
            {
                best = distance;
                at = i;
            }
        }

        return at;
    }

    /// <summary>An index permutation sorted by a key, ties keeping their original order.</summary>
    private static void StableSort(int[] order, double[] keys)
    {
        Array.Sort(order, (x, y) =>
        {
            int byKey = keys[x].CompareTo(keys[y]);
            return byKey != 0 ? byKey : x.CompareTo(y);
        });
    }

    // --- Building the rows ----------------------------------------------------------------------

    /// <summary>
    /// The paired roots turned into rows, filled from the bottom up so that the last pair written
    /// is the first section — which is what makes the ordering flag mean what it says.
    /// </summary>
    private static double[] FormSecondOrder(Complex[] z, Complex[] p, int lz, int lp, int sections)
    {
        var rows = new List<double[]>();

        if (lz == 0)
        {
            if (lp == 0)
            {
                return [1, 0, 0, 1, 0, 0];
            }

            if (lp % 2 == 0)
            {
                Pairs(rows, 0, (2 * sections) - 2, null, p);
            }
            else
            {
                Pairs(rows, 0, (2 * (sections - 1)) - 2, null, p);
                rows.Insert(0, LastPole(null, p[lp - 1]));
            }

            return Flatten(rows, 6);
        }

        if (lz % 2 == 0)
        {
            Pairs(rows, 0, lz - 2, z, p);
            if (lp % 2 == 0)
            {
                Pairs(rows, lz, lp - 1, null, p);
            }
            else
            {
                Pairs(rows, lz, lp - 2, null, p);
                rows.Insert(0, LastPole(null, p[lp - 1]));
            }

            return Flatten(rows, 6);
        }

        Pairs(rows, 0, lz - 2, z, p);
        if (lz == lp)
        {
            rows.Insert(0, LastPole(z[lz - 1], p[lz - 1]));
            return Flatten(rows, 6);
        }

        rows.Insert(0, Section([z[lz - 1]], p.AsSpan(lz - 1, 2), 3));

        if (lp % 2 == 0)
        {
            Pairs(rows, lz + 1, lp - 1, null, p);
        }
        else
        {
            Pairs(rows, lz + 1, lp - 2, null, p);
            rows.Insert(0, LastPole(null, p[lp - 1]));
        }

        return Flatten(rows, 6);
    }

    /// <summary>One section per conjugate pair, walked two roots at a time.</summary>
    private static void Pairs(List<double[]> rows, int start, int stop, Complex[]? z, Complex[] p)
    {
        var made = new List<double[]>();
        for (int m = start; m <= stop; m += 2)
        {
            made.Add(Section(z is null ? [] : z.AsSpan(m, 2), p.AsSpan(m, 2), 3));
        }

        made.Reverse();
        rows.InsertRange(0, made);
    }

    /// <summary>The odd pole left over, written as a first-order section padded to six columns.</summary>
    private static double[] LastPole(Complex? z, Complex p) =>
        Section(z is null ? [] : [z.Value], [p], 3);

    /// <summary>
    /// One section's row: the denominator is the poles' polynomial and the numerator is the zeros'
    /// polynomial right-aligned against it, both padded on the right to the section's width.
    /// </summary>
    /// <remarks>
    /// The right alignment is <c>zp2tf</c>'s and it is the rule a section built from the formula
    /// gets wrong. A section with no zeros at all has the numerator <c>[0 0 1]</c> rather than
    /// <c>[1 0 0]</c> — a pure delay, not a pass-through — because the numerator is padded at the
    /// front to reach the denominator's length. Two cascades that differ only in that are the same
    /// filter apart from a delay, which is exactly the sort of difference a magnitude check misses.
    /// </remarks>
    private static double[] Section(ReadOnlySpan<Complex> z, ReadOnlySpan<Complex> p, int half)
    {
        double[] den = FilterCoefficients.RealPolynomial(p);
        double[] zeroPoly = FilterCoefficients.RealPolynomial(z);

        var row = new double[2 * half];
        for (int i = 0; i < zeroPoly.Length; i++)
        {
            row[den.Length - zeroPoly.Length + i] = zeroPoly[i];
        }

        for (int i = 0; i < den.Length; i++)
        {
            row[half + i] = den[i];
        }

        return row;
    }

    /// <summary>The fourth-order version, which takes four roots at a time instead of two.</summary>
    private static double[] FormFourthOrder(Complex[] z, Complex[] p, int lz, int lp, int sections)
    {
        var rows = new List<double[]>();

        if (lz == 0)
        {
            if (lp == 0)
            {
                return [1, 0, 0, 0, 0, 1, 0, 0, 0, 0];
            }

            if (lp % 4 == 0)
            {
                Quads(rows, 0, lp - 4, null, p);
            }
            else
            {
                Quads(rows, 0, (4 * (sections - 1)) - 4, null, p);
                rows.Insert(0, LastPoles([], p.AsSpan(lp - (lp % 4)).ToArray()));
            }

            return Flatten(rows, 10);
        }

        if (lz % 4 == 0)
        {
            Quads(rows, 0, lz - 4, z, p);
            if (lp % 4 == 0)
            {
                Quads(rows, lz, lp - 4, null, p);
            }
            else
            {
                Quads(rows, lz, lp - 4, null, p);
                rows.Insert(0, LastPoles([], p.AsSpan(lp - (lp % 4)).ToArray()));
            }

            return Flatten(rows, 10);
        }

        Quads(rows, 0, lz - 4, z, p);
        int written = lz - (lz % 4);
        int remainingPoles = lp - written;
        if (remainingPoles <= 4)
        {
            rows.Insert(0, LastPoles(z.AsSpan(written).ToArray(), p.AsSpan(written).ToArray()));
            return Flatten(rows, 10);
        }

        rows.Insert(0, LastPoles(z.AsSpan(written).ToArray(), p.AsSpan(written, 4).ToArray()));
        Complex[] rest = p.AsSpan(written + 4).ToArray();
        if (rest.Length % 4 == 0)
        {
            Quads(rows, 0, rest.Length - 4, null, rest);
        }
        else
        {
            Quads(rows, 0, rest.Length - 4, null, rest);
            rows.Insert(0, LastPoles([], rest.AsSpan(rest.Length - (rest.Length % 4)).ToArray()));
        }

        return Flatten(rows, 10);
    }

    /// <summary>One fourth-order section per four roots.</summary>
    private static void Quads(List<double[]> rows, int start, int stop, Complex[]? z, Complex[] p)
    {
        var made = new List<double[]>();
        for (int m = start; m <= stop && m + 3 < p.Length; m += 4)
        {
            made.Add(Section(z is null ? [] : z.AsSpan(m, 4), p.AsSpan(m, 4), 5));
        }

        made.Reverse();
        rows.InsertRange(0, made);
    }

    /// <summary>The roots left over at the end of a fourth-order cascade, right-padded with zeros.</summary>
    private static double[] LastPoles(Complex[] z, Complex[] p) => Section(z, p, 5);

    /// <summary>A list of rows read into one column-major block.</summary>
    private static double[] Flatten(List<double[]> rows, int width)
    {
        var flat = new double[rows.Count * width];
        for (int r = 0; r < rows.Count; r++)
        {
            for (int c = 0; c < width; c++)
            {
                flat[(c * rows.Count) + r] = rows[r][c];
            }
        }

        return flat;
    }

    /// <summary>The rows read bottom to top.</summary>
    private static double[] FlipRows(double[] rows, int count, int width)
    {
        var flipped = new double[rows.Length];
        for (int r = 0; r < count; r++)
        {
            for (int c = 0; c < width; c++)
            {
                flipped[(c * count) + r] = rows[(c * count) + count - 1 - r];
            }
        }

        return flipped;
    }

    // --- Scaling --------------------------------------------------------------------------------

    /// <summary>
    /// The numerators scaled so that the signal reaching each section has a bounded norm, and the
    /// leftover gain moved to the end.
    /// </summary>
    /// <remarks>
    /// The rule walks the cascade forwards accumulating the transfer function seen so far, measures
    /// its norm at each section boundary, and divides one section's numerator by the ratio of two
    /// consecutive norms. What comes out is a cascade whose intermediate signals are all about the
    /// same size, which is what stops a fixed-point realisation overflowing in the middle of a
    /// filter that has plenty of headroom at both ends.
    /// </remarks>
    private static Cascade Scale(double[] rows, int count, int width, double gain, Scaling scaling)
    {
        if (scaling == Scaling.None || count == 0)
        {
            return new Cascade(rows, count, width, gain);
        }

        int half = width / 2;
        double norm = scaling == Scaling.Infinity ? double.PositiveInfinity : 2;
        var s = new double[count];

        double[] den = Row(rows, count, width, 0, half, half);
        s[0] = FilterNorm([1.0], den, norm);

        var fnum = new double[] { 1 };
        var fden = new double[] { 1 };

        for (int m = 1; m < count; m++)
        {
            den = Row(rows, count, width, m, half, half);
            fnum = FilterCoefficients.Convolve(fnum, Row(rows, count, width, m - 1, 0, half));
            fden = FilterCoefficients.Convolve(fden, Row(rows, count, width, m - 1, half, half));
            double[] fden2 = FilterCoefficients.Convolve(fden, den);
            s[m] = FilterNorm(fnum, fden2, norm);

            double factor = s[m - 1] / s[m];
            for (int c = 0; c < half; c++)
            {
                rows[(c * count) + m - 1] *= factor;
            }
        }

        for (int c = 0; c < half; c++)
        {
            rows[(c * count) + count - 1] *= gain * s[count - 1];
        }

        return new Cascade(rows, count, width, 1 / s[0]);
    }

    /// <summary>One row's slice, read out of the column-major block.</summary>
    private static double[] Row(double[] rows, int count, int width, int row, int from, int length)
    {
        var slice = new double[length];
        for (int i = 0; i < length; i++)
        {
            slice[i] = rows[((from + i) * count) + row];
        }

        return slice;
    }

    /// <summary>
    /// A filter's norm: the peak of its frequency response, or the energy of its impulse response.
    /// </summary>
    /// <remarks>
    /// This is <c>filternorm</c>'s arithmetic, written here because the section scaling above needs
    /// it and M133 comes before M134. The <c>filternorm</c> name, its tolerance argument and its
    /// stability check are M134's.
    /// </remarks>
    public static double FilterNorm(ReadOnlySpan<double> b, ReadOnlySpan<double> a, double norm)
    {
        if (double.IsPositiveInfinity(norm))
        {
            (Complex[] response, _) = DigitalFilter.Freqz(b, a, 1024, 2);
            double peak = 0;
            foreach (Complex h in response)
            {
                peak = System.Math.Max(peak, Complex.Abs(h));
            }

            return peak;
        }

        if (IsFeedForward(a))
        {
            double sum = 0;
            foreach (double v in b)
            {
                sum += v * v;
            }

            return System.Math.Sqrt(sum);
        }

        int length = ImpulseLength(b, a, 1e-8);
        var impulse = new double[length];
        impulse[0] = 1;
        double[] h2 = DigitalFilter.Filter(b, a, impulse);
        double energy = 0;
        foreach (double v in h2)
        {
            energy += v * v;
        }

        return System.Math.Sqrt(energy);
    }

    /// <summary>Whether the denominator is a scalar, so that the filter has no feedback.</summary>
    private static bool IsFeedForward(ReadOnlySpan<double> a)
    {
        if (a.Length == 0)
        {
            return true;
        }

        for (int i = 1; i < a.Length; i++)
        {
            if (a[i] != 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// <c>impzlength</c>: how many samples of the impulse response are worth having, which is set by
    /// the slowest pole's decay or, for a pole on the circle, by five periods of its oscillation.
    /// </summary>
    public static int ImpulseLength(ReadOnlySpan<double> b, ReadOnlySpan<double> a, double tolerance)
    {
        if (IsFeedForward(a))
        {
            return System.Math.Max(1, b.Length);
        }

        int delay = 0;
        for (int i = 0; i < b.Length; i++)
        {
            if (b[i] != 0)
            {
                delay = i;
                break;
            }
        }

        var coefficients = new Complex[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            coefficients[i] = a[i];
        }

        Complex[] poles = Polynomials.Roots(coefficients);
        double n;

        bool unstable = false;
        double worst = 0;
        foreach (Complex p in poles)
        {
            if (Complex.Abs(p) > 1.0001)
            {
                unstable = true;
                worst = System.Math.Max(worst, Complex.Abs(p));
            }
        }

        if (unstable)
        {
            n = 6 / System.Math.Log10(worst);
        }
        else
        {
            n = StableLength(poles, tolerance, delay);
        }

        n = System.Math.Max(a.Length + b.Length - 1, n);
        return (int)System.Math.Floor(n);
    }

    /// <summary>The decay length of a stable or marginally stable pole set.</summary>
    private static double StableLength(Complex[] poles, double tolerance, int delay)
    {
        var oscillating = new List<Complex>();
        var damped = new List<Complex>();
        foreach (Complex value in poles)
        {
            Complex p = System.Math.Abs(Complex.Abs(value - 1) ) < 1e-5 ? -value : value;
            if (System.Math.Abs(Complex.Abs(p) - 1) < 1e-5)
            {
                oscillating.Add(p);
            }
            else
            {
                damped.Add(p);
            }
        }

        if (damped.Count == 0)
        {
            return 5 * MaxPeriod(oscillating);
        }

        int at = 0;
        double largest = 0;
        for (int i = 0; i < damped.Count; i++)
        {
            double magnitude = Complex.Abs(damped[i]);
            if (magnitude > largest)
            {
                largest = magnitude;
                at = i;
            }
        }

        double decay = Multiplicity(damped, at) * System.Math.Log10(tolerance) / System.Math.Log10(largest);
        if (oscillating.Count == 0)
        {
            return decay + delay;
        }

        return System.Math.Max(5 * MaxPeriod(oscillating), decay) + delay;
    }

    /// <summary>The longest period among a set of poles on the unit circle.</summary>
    private static double MaxPeriod(List<Complex> poles)
    {
        double longest = 0;
        foreach (Complex p in poles)
        {
            double angle = System.Math.Abs(p.Phase);
            longest = System.Math.Max(longest, 2 * System.Math.PI / angle);
        }

        return longest;
    }

    /// <summary>How many poles sit on top of the one at a given index.</summary>
    private static int Multiplicity(List<Complex> poles, int at)
    {
        double threshold = 0.001;
        bool anyZero = false;
        foreach (Complex p in poles)
        {
            if (p == Complex.Zero)
            {
                anyZero = true;
                break;
            }
        }

        double reach = anyZero ? threshold : threshold * Complex.Abs(poles[at]);
        int count = 0;
        foreach (Complex p in poles)
        {
            if (Complex.Abs(p - poles[at]) < reach)
            {
                count++;
            }
        }

        return count;
    }

    // --- Reading a cascade back -----------------------------------------------------------------

    /// <summary>
    /// <c>sos2tf</c>: the sections multiplied out into one ratio of polynomials, with a trailing
    /// zero dropped from each side when the cascade is longer than one section.
    /// </summary>
    public static (double[] B, double[] A) ToTransferFunction(double[] sos, int rows, double gain)
    {
        if (rows == 0)
        {
            return ([], []);
        }

        var b = new double[] { 1 };
        var a = new double[] { 1 };
        for (int m = 0; m < rows; m++)
        {
            b = FilterCoefficients.Convolve(b, Row(sos, rows, 6, m, 0, 3));
            a = FilterCoefficients.Convolve(a, Row(sos, rows, 6, m, 3, 3));
        }

        for (int i = 0; i < b.Length; i++)
        {
            b[i] *= gain;
        }

        if (rows > 1)
        {
            if (b[^1] == 0)
            {
                b = b[..^1];
            }

            if (a[^1] == 0)
            {
                a = a[..^1];
            }
        }

        return (b, a);
    }

    /// <summary>
    /// <c>sos2zp</c>: each section's roots, gathered. A section that is really first order — its
    /// third numerator and sixth denominator coefficient both zero — is read as one rather than
    /// contributing a spurious root at the origin.
    /// </summary>
    public static (Complex[] Zeros, Complex[] Poles, Complex Gain) ToRoots(double[] sos, int rows, double gain)
    {
        var zeros = new List<Complex>();
        var poles = new List<Complex>();
        Complex k = gain;

        for (int n = 0; n < rows; n++)
        {
            double[] b;
            double[] a;
            if (sos[(5 * rows) + n] == 0 && sos[(2 * rows) + n] == 0)
            {
                b = Row(sos, rows, 6, n, 0, 2);
                a = Row(sos, rows, 6, n, 3, 2);
            }
            else
            {
                b = Row(sos, rows, 6, n, 0, 3);
                a = Row(sos, rows, 6, n, 3, 3);
            }

            if (b[^1] == 0 && a[^1] == 0)
            {
                b = [b[0]];
                a = [a[0]];
            }

            var numerator = new Complex[b.Length];
            for (int i = 0; i < b.Length; i++)
            {
                numerator[i] = b[i];
            }

            var denominator = new Complex[a.Length];
            for (int i = 0; i < a.Length; i++)
            {
                denominator[i] = a[i];
            }

            FilterCoefficients.Zpk section = FilterCoefficients.TfToZp(numerator, 1, numerator.Length, denominator);
            zeros.AddRange(section.Zeros);
            poles.AddRange(section.Poles);
            k *= section.Gains.Length > 0 ? section.Gains[0] : Complex.One;
        }

        return ([.. zeros], [.. poles], k);
    }

    /// <summary>
    /// <c>scaleFilterSections</c>: a cascade's scale values folded into its numerators, one section
    /// at a time, with the sign of the overall gain landing on the last section.
    /// </summary>
    /// <remarks>
    /// A single scale value is spread evenly — each of the K sections takes the K-th root of its
    /// magnitude — while a full list gives each section its own. Either way the sign goes to the
    /// last section rather than being spread, because a K-th root of a negative number is not real.
    /// </remarks>
    public static double[] ScaleSections(double[] numerators, int rows, int columns, ReadOnlySpan<double> scales)
    {
        bool allOne = true;
        foreach (double v in scales)
        {
            if (v != 1)
            {
                allOne = false;
                break;
            }
        }

        if (allOne)
        {
            return numerators;
        }

        var scaled = new double[numerators.Length];
        Array.Copy(numerators, scaled, numerators.Length);

        if (scales.Length == 1)
        {
            double root = System.Math.Pow(System.Math.Abs(scales[0]), 1.0 / rows);
            for (int i = 0; i < scaled.Length; i++)
            {
                scaled[i] *= root;
            }

            double sign = System.Math.Sign(scales[0]);
            for (int c = 0; c < columns; c++)
            {
                scaled[(c * rows) + rows - 1] *= sign;
            }

            return scaled;
        }

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                scaled[(c * rows) + r] *= System.Math.Pow(System.Math.Abs(scales[rows]), 1.0 / rows) * scales[r];
            }
        }

        double lastSign = System.Math.Sign(scales[rows]);
        for (int c = 0; c < columns; c++)
        {
            scaled[(c * rows) + rows - 1] *= lastSign;
        }

        return scaled;
    }
}
