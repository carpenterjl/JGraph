using System.Globalization;
using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Maths.Ticks;

/// <summary>
/// Generates evenly spaced ticks for a linear axis. A ruler that was measured takes R2025b's rule
/// (<see cref="TickRule"/>, open item 99): steps of 1, 2 and 5 times a power of ten, as many as the
/// ruler's length and font allow, preferring the step whose ticks reach furthest toward both limits.
/// A ruler nobody measured keeps the classic 1-2-5-10 ladder driven by a target count.
/// </summary>
public sealed class LinearTickGenerator : ITickGenerator
{
    public static readonly LinearTickGenerator Instance = new();

    public AxisScaleType ScaleType => AxisScaleType.Linear;

    public TickSet Generate(DataRange range, TickBudget budget, string? labelFormat = null)
    {
        if (!range.IsValid)
        {
            return TickSet.Empty;
        }

        double step;
        int minorSubdivisions;
        bool exactly = false;
        if (budget.HasCaps)
        {
            step = TickRule.ChooseStep(range.Min, range.Max, budget, out exactly);
            minorSubdivisions = SubdivisionsOf(step);
        }
        else
        {
            if (budget.Target < 2)
            {
                return TickSet.Empty;
            }

            step = NiceStep(range.Length / budget.Target, out minorSubdivisions);
        }

        if (step <= 0 || !double.IsFinite(step))
        {
            return TickSet.Empty;
        }

        var values = new List<double>();
        var minors = new List<double>();
        double minorStep = step / minorSubdivisions;

        // A small tolerance so a tick landing exactly on the range edge is not dropped by rounding,
        // unless the rule counted the step in R2025b's exact arithmetic, where it is dropped.
        double epsilon = step * 1e-9;
        int count = exactly
            ? TickRule.CountExactly(range.Min, range.Max, step, out double first, out _)
            : TickRule.CountWithin(range.Min, range.Max, step, out first, out _);
        for (int i = 0; i < count; i++)
        {
            values.Add(Snap(first + (i * step), step));
        }

        // The row is labelled at once: how many decimals R2025b writes depends on every tick (open item 97).
        string[] labels = TickFormats.Labels(values, labelFormat);
        var majors = new List<Tick>(values.Count);
        for (int i = 0; i < values.Count; i++)
        {
            majors.Add(new Tick(values[i], labels[i]));
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

    /// <summary>How many minor intervals a major step of the 1-2-5 ladder is cut into.</summary>
    private static int SubdivisionsOf(double step)
    {
        double magnitude = System.Math.Pow(10, System.Math.Floor(System.Math.Log10(step) + 1e-9));
        double mantissa = step / magnitude;
        return mantissa < 1.5 ? 5 : mantissa < 3.5 ? 4 : 5;
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
}
