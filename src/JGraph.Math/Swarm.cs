using JGraph.Core.Drawing;

namespace JGraph.Maths;

/// <summary>
/// Where to draw points that share a value so that all of them can be seen — the arithmetic behind
/// MATLAB's <c>swarmchart</c> and behind the <c>XJitter</c>/<c>YJitter</c> properties every marker
/// chart carries.
/// <para>
/// The offsets are a drawing decision, not a change to the data: everything here answers "how far
/// sideways", and the reading itself is left alone. That is what lets a swarm chart still report the
/// x it was given, which is what a script reading <c>XData</c> back expects.
/// </para>
/// </summary>
public static class Swarm
{
    /// <summary>
    /// The offsets to draw a set of points at, one per point, all of them inside
    /// ±<paramref name="width"/>/2.
    /// </summary>
    /// <param name="positions">
    /// The coordinate being spread. Points sharing one of these values are one group, and a group is
    /// spread within itself — which is what keeps two columns of a swarm chart from being laid out as
    /// though they were one.
    /// </param>
    /// <param name="crowded">
    /// The other coordinate: the one whose crowding the density spread is reading. Points close
    /// together in this are what the spread has to separate.
    /// </param>
    /// <param name="style">Which of MATLAB's four spreads to use.</param>
    /// <param name="width">The full width the spread is allowed to occupy.</param>
    public static double[] Offsets(
        IReadOnlyList<double> positions, IReadOnlyList<double> crowded, JitterStyle style, double width)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(crowded);
        return style switch
        {
            JitterStyle.Density => ByGroup(positions, crowded, width),
            JitterStyle.Rand => Scattered(positions.Count, width, bell: false),
            JitterStyle.Randn => Scattered(positions.Count, width, bell: true),
            _ => new double[positions.Count],
        };
    }

    /// <summary>
    /// The density spread, run once per group of points sharing a position. A column of a swarm chart
    /// is spread against its own crowd and no one else's; a scatter whose positions are all distinct is
    /// a chart of one-point groups, and every offset comes out zero — which is the honest answer, since
    /// there is nothing there to separate.
    /// </summary>
    private static double[] ByGroup(
        IReadOnlyList<double> positions, IReadOnlyList<double> crowded, double width)
    {
        var offsets = new double[positions.Count];
        var groups = new Dictionary<double, List<int>>();
        for (int i = 0; i < positions.Count && i < crowded.Count; i++)
        {
            if (!groups.TryGetValue(positions[i], out List<int>? members))
            {
                groups[positions[i]] = members = [];
            }

            members.Add(i);
        }

        foreach (List<int> members in groups.Values)
        {
            double[] spread = Density([.. members.Select(i => crowded[i])], width);
            for (int j = 0; j < members.Count; j++)
            {
                offsets[members[j]] = spread[j];
            }
        }

        return offsets;
    }

    /// <summary>
    /// The beeswarm spread: readings are binned by value, and the points in a bin are laid out evenly
    /// across a slice of the width proportional to how full that bin is against the fullest one.
    /// <para>
    /// So the widest part of the swarm is the busiest part of the data, which is the whole reason to
    /// draw one — the outline of the cloud is its own histogram, turned on its side. A bin holding a
    /// single point puts it on the centre line rather than off to one side, because a lone reading is
    /// not evidence of a direction.
    /// </para>
    /// </summary>
    private static double[] Density(IReadOnlyList<double> values, double width)
    {
        var offsets = new double[values.Count];
        if (values.Count == 0 || !(width > 0))
        {
            return offsets;
        }

        double low = double.PositiveInfinity;
        double high = double.NegativeInfinity;
        foreach (double value in values)
        {
            if (double.IsFinite(value))
            {
                low = System.Math.Min(low, value);
                high = System.Math.Max(high, value);
            }
        }

        if (low > high)
        {
            return offsets;
        }

        // A run of identical readings is one bin, which is the case the spread matters most for.
        int bins = Binning.SquareRootChoice(values.Count);
        double[] edges = high > low
            ? Binning.Spanning(low, high, bins)
            : Binning.Spanning(low - 0.5, low + 0.5, 1);

        var members = new List<int>[edges.Length - 1];
        for (int i = 0; i < members.Length; i++)
        {
            members[i] = [];
        }

        for (int i = 0; i < values.Count; i++)
        {
            int bin = Binning.BinOf(values[i], edges);
            if (bin >= 0)
            {
                members[bin].Add(i);
            }
        }

        int fullest = 0;
        foreach (List<int> bin in members)
        {
            fullest = System.Math.Max(fullest, bin.Count);
        }

        foreach (List<int> bin in members)
        {
            if (bin.Count == 0)
            {
                continue;
            }

            double slice = width * bin.Count / fullest;
            for (int j = 0; j < bin.Count; j++)
            {
                double place = bin.Count == 1 ? 0 : ((double)j / (bin.Count - 1)) - 0.5;
                offsets[bin[j]] = place * slice;
            }
        }

        return offsets;
    }

    /// <summary>
    /// The random spreads, drawn from the point's own position in the list rather than from a random
    /// number generator.
    /// <para>
    /// This is a recorded divergence: MATLAB's <c>'rand'</c> and <c>'randn'</c> jitter land somewhere
    /// new every time the chart is drawn, so the same script twice gives two pictures. Here the offset
    /// is a function of which point it is, so a chart redrawn, saved and loaded, or exported is the
    /// chart that was on screen. The spread is the same shape either way; only its repeatability
    /// differs.
    /// </para>
    /// </summary>
    private static double[] Scattered(int count, double width, bool bell)
    {
        var offsets = new double[count];
        for (int i = 0; i < count; i++)
        {
            // A cheap integer hash, taken to [0, 1): neighbouring points get unrelated offsets, which
            // is the only property a jitter needs from its numbers.
            uint mixed = (uint)i * 2654435761u;
            mixed ^= mixed >> 15;
            mixed *= 2246822519u;
            mixed ^= mixed >> 13;
            double unit = mixed / 4294967296.0;

            // The bell form spends the width on three standard deviations, so the tails are inside it
            // and nothing has to be clamped back in — a clamped tail would pile points on the edges.
            offsets[i] = bell
                ? System.Math.Clamp(Normal(unit) / 6, -0.5, 0.5) * width
                : (unit - 0.5) * width;
        }

        return offsets;
    }

    /// <summary>A standard normal deviate from a uniform one, by the Beasley–Springer–Moro tails.</summary>
    private static double Normal(double unit)
    {
        double p = System.Math.Clamp(unit, 1e-9, 1 - 1e-9);
        double q = p - 0.5;
        if (System.Math.Abs(q) <= 0.425)
        {
            double r = 0.180625 - (q * q);
            return q * (((((((2509.0809287301226727 * r) + 33430.575583588128105) * r) + 67265.770927008700853) * r)
                + 45921.953931549871457) / ((((((5226.495278852854561 * r) + 28729.085735721942674) * r)
                + 39307.89580009271061) * r) + 13342.373642905469458));
        }

        double tail = System.Math.Sqrt(-System.Math.Log(q < 0 ? p : 1 - p));
        double value = (((2.938163982698783 * tail) + 4.374664141464968) * tail) + 2.759285104469687;
        value /= ((1.637067800794387 * tail) + 1.0) * tail;
        return q < 0 ? -value : value;
    }

    /// <summary>
    /// The width to spread over when nobody said: nine tenths of the closest two distinct readings get
    /// to each other, so neighbouring groups stay apart, and nine tenths of a unit when there is only
    /// one group to place.
    /// </summary>
    public static double AutomaticWidth(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        double[] sorted = [.. values.Where(double.IsFinite).Distinct().Order()];
        double closest = double.PositiveInfinity;
        for (int i = 1; i < sorted.Length; i++)
        {
            closest = System.Math.Min(closest, sorted[i] - sorted[i - 1]);
        }

        return 0.9 * (double.IsFinite(closest) && closest > 0 ? closest : 1);
    }
}
