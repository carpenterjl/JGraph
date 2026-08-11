using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>
/// How a bubble chart turns a data value into a marker diameter: the span of values that gets the
/// whole size range (MATLAB's <c>bubblelim</c>), and the smallest and largest diameter in points
/// (MATLAB's <c>bubblesize</c>).
/// <para>
/// The mapping is linear in <b>area</b> rather than in diameter, which is the only reading of a
/// bubble chart that does not mislead: a value twice as large draws a bubble covering twice as much
/// of the page. It is stated here rather than left implicit because the legend has to reproduce it
/// exactly — the whole point of a bubble legend is that its bubbles are the same size the chart's
/// would be.
/// </para>
/// </summary>
public readonly record struct BubbleScale(DataRange Limits, DataRange SizeRange)
{
    /// <summary>The scale an axes starts with: diameters from 6 to 40 points, over whatever data arrives.</summary>
    public static DataRange DefaultSizeRange => new(6, 40);

    /// <summary>A scale reading its limits off a list of sizes, for a chart not yet added to an axes.</summary>
    public static BubbleScale ForValues(IReadOnlyList<double>? values)
    {
        DataRange limits = DataRange.Empty;
        if (values is not null)
        {
            foreach (double value in values)
            {
                if (double.IsFinite(value))
                {
                    limits = limits.Include(value);
                }
            }
        }

        return new BubbleScale(limits.IsEmpty ? DataRange.Unit : limits, DefaultSizeRange);
    }

    /// <summary>
    /// The diameter in points a value is drawn at. A value outside the limits is clamped to the end it
    /// went past, so setting <c>bubblelim</c> narrower than the data hides no bubbles — it flattens
    /// the ones beyond it, which is what MATLAB does too.
    /// </summary>
    public double DiameterFor(double value)
    {
        double low = System.Math.Max(0, System.Math.Min(SizeRange.Min, SizeRange.Max));
        double high = System.Math.Max(low, System.Math.Max(SizeRange.Min, SizeRange.Max));

        if (!double.IsFinite(value))
        {
            return low;
        }

        // One distinct size is neither the largest nor the smallest thing in the data, so it is drawn
        // halfway up the range rather than being made to look loud or timid by an accident of scaling.
        double span = Limits.Max - Limits.Min;
        double t = span > 0 ? System.Math.Clamp((value - Limits.Min) / span, 0, 1) : 0.5;

        return System.Math.Sqrt(((1 - t) * low * low) + (t * high * high));
    }
}

/// <summary>
/// A plot whose markers are sized from data through the axes' <see cref="BubbleScale"/> rather than
/// from a size of their own. The axes needs this to work out its automatic bubble limits, and the
/// bubble legend needs it to find the chart it is legending — neither can see the plot types
/// themselves, which live a layer above.
/// </summary>
public interface IBubbleData
{
    /// <summary>One size per point, or null when the plot is not drawing bubbles.</summary>
    IReadOnlyList<double>? SizeData { get; }

    /// <summary>
    /// Whether <see cref="SizeData"/> means bubble sizes (mapped through the axes' scale) rather than
    /// MATLAB <c>scatter</c>'s marker areas in points squared. The same array means both things in
    /// MATLAB depending on which verb drew it, so the plot has to say which it was.
    /// </summary>
    bool BubbleSizing { get; }

    /// <summary>The colour the bubbles are filled with, or null to take the series colour.</summary>
    Drawing.Color? BubbleFaceColor { get; }
}
