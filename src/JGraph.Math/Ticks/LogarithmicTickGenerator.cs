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

    public TickSet Generate(DataRange range, int targetCount, string? labelFormat = null)
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
        int decadeStride = System.Math.Max(1, (int)System.Math.Ceiling(decadeSpan / (double)System.Math.Max(2, targetCount)));

        var majors = new List<Tick>();
        var minors = new List<double>();
        double epsilon = min * 1e-9;

        for (int decade = lowDecade; decade <= highDecade; decade++)
        {
            double decadeValue = System.Math.Pow(10, decade);

            if ((decade - lowDecade) % decadeStride == 0 &&
                decadeValue >= min - epsilon && decadeValue <= max + epsilon)
            {
                majors.Add(new Tick(decadeValue, FormatValue(decadeValue, labelFormat)));
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

        double step = decadeStride; // decades per major tick
        return new TickSet(majors, minors, step);
    }

    private static string FormatValue(double value, string? labelFormat)
    {
        if (!string.IsNullOrEmpty(labelFormat))
        {
            return value.ToString(labelFormat, CultureInfo.CurrentCulture);
        }

        double abs = System.Math.Abs(value);
        if (abs != 0 && (abs >= 1e5 || abs < 1e-3))
        {
            return value.ToString("0.###e+0", CultureInfo.CurrentCulture);
        }

        return value.ToString("0.######", CultureInfo.CurrentCulture);
    }
}
