using System.Globalization;
using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Maths.Ticks;

/// <summary>
/// Generates "nice" evenly spaced ticks for a linear axis using the classic 1-2-2.5-5-10 step
/// selection, so tick values land on visually round numbers close to the requested count.
/// </summary>
public sealed class LinearTickGenerator : ITickGenerator
{
    public static readonly LinearTickGenerator Instance = new();

    public AxisScaleType ScaleType => AxisScaleType.Linear;

    public TickSet Generate(DataRange range, int targetCount, string? labelFormat = null)
    {
        if (!range.IsValid || targetCount < 2)
        {
            return TickSet.Empty;
        }

        double step = NiceStep(range.Length / targetCount, out int minorSubdivisions);
        if (step <= 0 || !double.IsFinite(step))
        {
            return TickSet.Empty;
        }

        double first = System.Math.Ceiling(range.Min / step) * step;
        int decimals = DecimalsFor(step);

        var majors = new List<Tick>();
        var minors = new List<double>();
        double minorStep = step / minorSubdivisions;

        // A small tolerance so a tick landing exactly on the range edge is not dropped by rounding.
        double epsilon = step * 1e-9;

        for (double value = first; value <= range.Max + epsilon; value += step)
        {
            double snapped = Snap(value, step);
            majors.Add(new Tick(snapped, FormatValue(snapped, decimals, labelFormat)));
        }

        // Minor ticks span the full range, including partial intervals at the ends.
        double minorFirst = System.Math.Ceiling(range.Min / minorStep) * minorStep;
        for (double value = minorFirst; value <= range.Max + epsilon; value += minorStep)
        {
            double snapped = Snap(value, minorStep);
            // Skip positions that coincide with a major tick.
            if (System.Math.Abs((snapped / step) - System.Math.Round(snapped / step)) > 1e-6)
            {
                minors.Add(snapped);
            }
        }

        return new TickSet(majors, minors, step);
    }

    /// <summary>Rounds a step to the nearest 1, 2, 2.5, or 5 times a power of ten.</summary>
    private static double NiceStep(double rawStep, out int minorSubdivisions)
    {
        double magnitude = System.Math.Pow(10, System.Math.Floor(System.Math.Log10(rawStep)));
        double residual = rawStep / magnitude; // in [1, 10)

        double niceMultiple;
        if (residual < 1.5)
        {
            niceMultiple = 1;
            minorSubdivisions = 5;
        }
        else if (residual < 3)
        {
            niceMultiple = 2;
            minorSubdivisions = 4;
        }
        else if (residual < 7)
        {
            niceMultiple = 5;
            minorSubdivisions = 5;
        }
        else
        {
            niceMultiple = 10;
            minorSubdivisions = 5;
        }

        return niceMultiple * magnitude;
    }

    /// <summary>Removes floating-point noise by snapping a value to a multiple of the step.</summary>
    private static double Snap(double value, double step)
    {
        double snapped = System.Math.Round(value / step) * step;
        // Guard against -0.0 in output.
        return snapped == 0 ? 0 : snapped;
    }

    private static int DecimalsFor(double step)
    {
        if (step <= 0)
        {
            return 0;
        }

        int decimals = (int)System.Math.Ceiling(-System.Math.Log10(step));
        return System.Math.Clamp(decimals, 0, 12);
    }

    private static string FormatValue(double value, int decimals, string? labelFormat)
    {
        if (!string.IsNullOrEmpty(labelFormat))
        {
            return value.ToString(labelFormat, CultureInfo.CurrentCulture);
        }

        double abs = System.Math.Abs(value);

        // Fall back to scientific notation for very large or very small magnitudes.
        if (abs != 0 && (abs >= 1e6 || abs < 1e-4))
        {
            return value.ToString("0.###e+0", CultureInfo.CurrentCulture);
        }

        return value.ToString("F" + decimals, CultureInfo.CurrentCulture);
    }
}
