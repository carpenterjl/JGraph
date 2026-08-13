namespace JGraph.Objects.Internal;

/// <summary>
/// How a set of values becomes a set of wedges, and what is written beside one. Shared by
/// <see cref="PiePlot"/> and <see cref="Pie3DPlot"/> so that the reading of MATLAB's normalization
/// rule — and the wording of an automatic label — cannot drift apart between a flat pie and a
/// raised one.
/// </summary>
internal static class PieGeometry
{
    /// <summary>
    /// The wedges, in the order the values were given. A value that is not a positive finite number
    /// takes no angle, which is how a zero entry ends up drawing nothing at all.
    /// </summary>
    public static IReadOnlyList<PieSlice> Slices(
        double[] values, double[]? explode, double startAngle, bool clockwise)
    {
        double total = 0;
        foreach (double value in values)
        {
            if (double.IsFinite(value) && value > 0)
            {
                total += value;
            }
        }

        // MATLAB's rule, and the whole of it: a total over one is normalized, and one at or below it
        // is already the shares — which is the only way to ask for a pie with a piece missing.
        double scale = total > 1 ? 1 / total : 1;
        double direction = clockwise ? -1 : 1;
        double angle = startAngle * System.Math.PI / 180;

        var slices = new List<PieSlice>(values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            double value = values[i];
            double fraction = double.IsFinite(value) && value > 0 ? value * scale : 0;
            double sweep = fraction * 2 * System.Math.PI * direction;
            slices.Add(new PieSlice(i, angle, sweep, fraction, OffsetOf(explode, i)));
            angle += sweep;
        }

        return slices;
    }

    /// <summary>What is written beside wedge <paramref name="i"/>: its label, or its share.</summary>
    public static string LabelOf(string[]? labels, int i, double fraction)
    {
        if (labels is not null && i < labels.Length)
        {
            return labels[i] ?? string.Empty;
        }

        // MATLAB rounds to whole percent and says "< 1%" rather than "0%" for a sliver, because a
        // wedge that is drawn should not be labelled as though it were not there.
        double percent = fraction * 100;
        return percent > 0 && percent < 0.5
            ? "< 1%"
            : $"{System.Math.Round(percent, MidpointRounding.AwayFromZero):0}%";
    }

    /// <summary>How far the wedge is pushed out of the middle, or nothing when it is not.</summary>
    public static double OffsetOf(double[]? explode, int i) =>
        explode is { } distances && i < distances.Length && double.IsFinite(distances[i])
            ? distances[i]
            : 0;

    /// <summary>Where the tip of a wedge sits once it has been pushed out of the middle.</summary>
    public static (double X, double Y) CenterOf(PieSlice slice) =>
        slice.Offset == 0
            ? (0, 0)
            : (slice.Offset * System.Math.Cos(slice.Middle), slice.Offset * System.Math.Sin(slice.Middle));

    /// <summary>How many straight steps an arc of this sweep is drawn in — one every two degrees.</summary>
    public static int StepsFor(double sweep) => System.Math.Max(
        2, (int)System.Math.Ceiling(System.Math.Abs(sweep) / (System.Math.PI / 90)));
}
