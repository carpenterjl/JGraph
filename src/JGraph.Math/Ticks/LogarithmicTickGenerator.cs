using System.Globalization;
using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Maths.Ticks;

/// <summary>
/// Generates decade ticks for a base-10 logarithmic axis: major ticks at integer powers of ten and
/// minor ticks at their 2-9 multiples. When the visible span covers many decades, major ticks are
/// thinned so their count stays near the requested target.
/// </summary>
public sealed class LogarithmicTickGenerator : ITickGenerator
{
    public static readonly LogarithmicTickGenerator Instance = new();

    public AxisScaleType ScaleType => AxisScaleType.Logarithmic;

    public TickSet Generate(DataRange range, TickBudget budget, string? labelFormat = null)
    {
        double min = range.Min;
        double max = range.Max;
        if (min <= 0)
        {
            // Log axes cannot show non-positive values; clamp the lower bound to a small positive floor.
            min = max > 0 ? max / 1000.0 : 1e-3;
        }

        if (!(max > min) || !double.IsFinite(min) || !double.IsFinite(max))
        {
            return TickSet.Empty;
        }

        int lowDecade = (int)System.Math.Floor(System.Math.Log10(min));
        int highDecade = (int)System.Math.Ceiling(System.Math.Log10(max));
        int decadeSpan = System.Math.Max(1, highDecade - lowDecade);

        // Thin decades so the number of labeled majors stays near the target.
        int decadeStride = System.Math.Max(1, (int)System.Math.Ceiling(decadeSpan / (double)System.Math.Max(2, budget.Target)));

        var decades = new List<int>();
        var minors = new List<double>();
        double epsilon = min * 1e-9;

        for (int decade = lowDecade; decade <= highDecade; decade++)
        {
            double decadeValue = System.Math.Pow(10, decade);

            if ((decade - lowDecade) % decadeStride == 0 &&
                decadeValue >= min - epsilon && decadeValue <= max + epsilon)
            {
                decades.Add(decade);
            }

            // Minor ticks at 2..9 within this decade (only meaningful when decades aren't thinned).
            if (decadeStride == 1)
            {
                for (int m = 2; m <= 9; m++)
                {
                    double value = m * decadeValue;
                    if (value >= min - epsilon && value <= max + epsilon)
                    {
                        minors.Add(value);
                    }
                }
            }
        }

        double[] values = decades.Select(static d => System.Math.Pow(10, d)).ToArray();
        string[] labels = Labels(values, labelFormat);
        var majors = new List<Tick>(values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            majors.Add(new Tick(values[i], labels[i]));
        }

        double step = decadeStride; // decades per major tick
        return new TickSet(majors, minors, step);
    }

    /// <summary>
    /// A log ruler's labels. R2025b writes a decade as its power, 10^{2}, automatically and under
    /// '%g' alike, whether it chose the tick or was given it (probe_97, open item 97); the TeX the
    /// labels are drawn through raises it. Any other value, or any other format, is written plainly.
    /// </summary>
    internal static string[] Labels(IReadOnlyList<double> values, string? labelFormat)
    {
        string[] labels = TickFormats.Labels(values, labelFormat);
        if (!string.IsNullOrEmpty(labelFormat) && !labelFormat.Equals("%g", StringComparison.OrdinalIgnoreCase))
        {
            return labels;
        }

        for (int i = 0; i < labels.Length; i++)
        {
            double power = System.Math.Round(System.Math.Log10(values[i]));
            if (values[i] > 0 && double.IsFinite(power) && System.Math.Pow(10, power) == values[i])
            {
                labels[i] = "10^{" + ((int)power).ToString(CultureInfo.InvariantCulture) + "}";
            }
        }

        return labels;
    }
}
