using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Maths.Ticks;

/// <summary>
/// Puts an axis' own choice of tick values and labels in front of a generator's. Either half may be
/// manual on its own: naming the values leaves them labeled with their numbers, and naming the labels
/// leaves the generator to pick where they go.
/// <para>
/// This wraps rather than replaces, so every scale keeps its own behaviour underneath — manual labels
/// on a logarithmic axis still land on the decades that generator chose.
/// </para>
/// </summary>
public sealed class ManualTickGenerator : ITickGenerator
{
    private readonly ITickGenerator _automatic;
    private readonly IReadOnlyList<double>? _positions;
    private readonly IReadOnlyList<string>? _labels;

    public ManualTickGenerator(
        ITickGenerator automatic,
        IReadOnlyList<double>? positions,
        IReadOnlyList<string>? labels)
    {
        ArgumentNullException.ThrowIfNull(automatic);
        _automatic = automatic;
        _positions = positions;
        _labels = labels;
    }

    public AxisScaleType ScaleType => _automatic.ScaleType;

    public TickSet Generate(DataRange range, int targetCount, string? labelFormat = null)
    {
        TickSet automatic = _positions is null
            ? _automatic.Generate(range, targetCount, labelFormat)
            : TickSet.Empty;

        IReadOnlyList<double> values = _positions ?? automatic.MajorTicks.Select(static t => t.Value).ToArray();
        double step = _positions is null ? automatic.Step : SpacingOf(_positions);
        int decimals = LinearTickGenerator.DecimalsFor(step);

        // A tick outside the visible range is skipped rather than dropped from the axis, and the label
        // for a position keeps that position's index, so which label belongs to which tick does not
        // change when the axis is zoomed.
        double epsilon = System.Math.Max(System.Math.Abs(range.Length), 1) * 1e-9;
        var majors = new List<Tick>(values.Count);
        for (int i = 0; i < values.Count; i++)
        {
            double value = values[i];
            if (!double.IsFinite(value) || value < range.Min - epsilon || value > range.Max + epsilon)
            {
                continue;
            }

            majors.Add(new Tick(value, LabelFor(i, value, decimals, labelFormat, automatic)));
        }

        return new TickSet(
            majors,
            _positions is null ? automatic.MinorTicks : Array.Empty<double>(),
            step);
    }

    private string LabelFor(int index, double value, int decimals, string? labelFormat, TickSet automatic)
    {
        if (_labels is null)
        {
            return _positions is null
                ? automatic.MajorTicks[index].Label
                : LinearTickGenerator.FormatValue(value, decimals, labelFormat);
        }

        // MATLAB cycles a short list of labels over a long row of ticks, and an empty list blanks them.
        return _labels.Count == 0 ? string.Empty : _labels[index % _labels.Count] ?? string.Empty;
    }

    /// <summary>
    /// The spacing manual ticks imply, which is what decides how many decimals their labels carry.
    /// Uneven spacing has no one answer, so the smallest gap wins: it is the one that needs the digits.
    /// </summary>
    private static double SpacingOf(IReadOnlyList<double> positions)
    {
        double smallest = double.PositiveInfinity;
        for (int i = 1; i < positions.Count; i++)
        {
            double gap = System.Math.Abs(positions[i] - positions[i - 1]);
            if (gap > 0 && gap < smallest)
            {
                smallest = gap;
            }
        }

        return double.IsFinite(smallest) ? smallest : 1;
    }
}
