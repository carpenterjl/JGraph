using System.Numerics;

namespace JGraph.Numerics;

/// <summary>
/// Two operations on a sequence that are about the values' relationships to each other rather than
/// their sizes: undoing the wrap in a phase record, and ordering a set of complex numbers so that
/// each conjugate pair sits together.
/// </summary>
public static class PhaseSequences
{
    /// <summary>The default cutoff for <see cref="Unwrap"/>: a half turn.</summary>
    public const double HalfTurn = Math.PI;

    /// <summary>The default tolerance for <see cref="ConjugatePairs"/>, relative to magnitude.</summary>
    public const double PairingTolerance = 100 * 2.220446049250313e-16;

    /// <summary>
    /// Removes the wrap from a phase record: wherever consecutive samples differ by more than the
    /// cutoff, whole turns are added to the rest of the sequence so that the jump disappears.
    /// </summary>
    /// <param name="p">The phases, in radians. Modified in place.</param>
    /// <param name="cutoff">
    /// How large a step must be before it counts as a wrap rather than a real change.
    /// </param>
    /// <remarks>
    /// <para>
    /// Only the finite samples take part. A NaN in the middle of a record is not a phase and cannot
    /// be a jump, so it is passed over and the samples on either side of it are treated as adjacent
    /// — which is why unwrapping a record with a hole in it can shift everything after the hole.
    /// </para>
    /// <para>
    /// The number of turns to remove is rounded half towards zero, not half away from it. That
    /// matters exactly at a step of ±π, where the correction is half a turn and either answer is
    /// defensible: rounding towards zero leaves the step alone, so a record that steps by exactly π
    /// each sample comes back unchanged instead of being folded flat.
    /// </para>
    /// </remarks>
    public static void Unwrap(Span<double> p, double cutoff)
    {
        const double Turn = 2 * Math.PI;

        // Every step is measured against the record as it arrived, never against a sample already
        // corrected. Reading a corrected sample would fold each correction into the next step and
        // compound them, which turns a steady ramp into a runaway.
        double running = 0;
        bool started = false;
        double held = 0;
        for (int i = 0; i < p.Length; i++)
        {
            if (!double.IsFinite(p[i]))
            {
                continue;
            }

            if (!started)
            {
                started = true;
                held = p[i];
                continue;
            }

            double step = p[i] - held;
            held = p[i];
            double turns = step / Turn;

            // Round half towards zero: truncate when the fraction is no more than a half, and round
            // to nearest otherwise. Math.Round's banker's rule would send ±0.5 to zero as well but
            // would send ±1.5 to ±2 where truncation is not asked for, so the two are kept apart.
            double whole = Math.Abs(turns % 1) <= 0.5 ? Math.Truncate(turns) : Math.Round(turns);
            if (Math.Abs(step) < cutoff)
            {
                whole = 0;
            }

            running += whole;
            p[i] -= Turn * running;
        }
    }

    /// <summary>
    /// Orders a set of complex numbers so that each conjugate pair is adjacent with the negative
    /// imaginary part first, pairs ascending by real part, and the purely real values last in
    /// ascending order.
    /// </summary>
    /// <param name="values">The values to order.</param>
    /// <param name="tolerance">
    /// How far apart, relative to magnitude, two values may be and still count as a pair. A value
    /// counts as real when its imaginary part is within this of zero.
    /// </param>
    /// <returns>The ordered values.</returns>
    /// <exception cref="ArgumentException">
    /// The complex values do not form conjugate pairs within the tolerance.
    /// </exception>
    public static Complex[] ConjugatePairs(ReadOnlySpan<Complex> values, double tolerance)
    {
        var reals = new List<double>();
        var complex = new List<Complex>();
        foreach (Complex value in values)
        {
            if (Math.Abs(value.Imaginary) <= tolerance * Complex.Abs(value))
            {
                reals.Add(value.Real);
            }
            else
            {
                complex.Add(value);
            }
        }

        if (complex.Count % 2 == 1)
        {
            throw new ArgumentException("Complex numbers can't be paired.", nameof(values));
        }

        var ordered = new Complex[values.Length];
        int next = 0;

        // Sorting by real part brings each pair together, since conjugates share one. Values that
        // merely happen to share a real part come together too, and are separated below by matching
        // each imaginary part against its negation.
        complex.Sort(static (x, y) => x.Real.CompareTo(y.Real));

        for (int i = 0; i + 1 < complex.Count; i += 2)
        {
            if (Math.Abs(complex[i].Real - complex[i + 1].Real) >
                tolerance * Complex.Abs(complex[i]))
            {
                throw new ArgumentException("Complex numbers can't be paired.", nameof(values));
            }
        }

        var remaining = new List<Complex>(complex);
        while (remaining.Count > 0)
        {
            // Everything sharing this real part, to the tolerance: one pair, or several that have to
            // be matched off against each other by imaginary part.
            double at = remaining[0].Real;
            var group = new List<Complex>();
            for (int i = remaining.Count - 1; i >= 0; i--)
            {
                if (Math.Abs(remaining[i].Real - at) <= tolerance * Complex.Abs(remaining[i]))
                {
                    group.Add(remaining[i]);
                    remaining.RemoveAt(i);
                }
            }

            if (group.Count <= 1)
            {
                throw new ArgumentException("Complex numbers can't be paired.", nameof(values));
            }

            group.Sort(static (x, y) => x.Imaginary.CompareTo(y.Imaginary));

            // Sorted by imaginary part, the group is symmetric about zero if it pairs at all: the
            // first must be the negation of the last, the second of the second-to-last, and so on.
            for (int i = 0; i < group.Count; i++)
            {
                double sum = group[i].Imaginary + group[group.Count - 1 - i].Imaginary;
                if (Math.Abs(sum) > tolerance * Complex.Abs(group[i]))
                {
                    throw new ArgumentException("Complex numbers can't be paired.", nameof(values));
                }
            }

            // Emit from the outside in, so within one real part the pair furthest from the real
            // axis comes first, and each pair leads with its conjugate. The conjugate is written
            // rather than the value that was actually read, so a pair that is conjugate only to the
            // tolerance comes back exactly conjugate.
            int half = group.Count / 2;
            for (int i = group.Count - 1; i >= half; i--)
            {
                Complex above = group[i];
                ordered[next++] = Complex.Conjugate(above);
                ordered[next++] = above;
            }
        }

        reals.Sort();
        foreach (double real in reals)
        {
            ordered[next++] = new Complex(real, 0);
        }

        return ordered;
    }
}
