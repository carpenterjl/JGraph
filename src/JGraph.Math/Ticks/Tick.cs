namespace JGraph.Maths.Ticks;

/// <summary>A single major tick: a data value paired with its display label.</summary>
public readonly struct Tick
{
    public Tick(double value, string label)
    {
        Value = value;
        Label = label;
    }

    public double Value { get; }

    public string Label { get; }

    public override string ToString() => $"{Value:G6} '{Label}'";
}

/// <summary>
/// The result of tick generation for an axis range: the labeled major ticks, the unlabeled minor
/// tick positions, and the major step size that produced them.
/// </summary>
public sealed class TickSet
{
    public static readonly TickSet Empty = new(Array.Empty<Tick>(), Array.Empty<double>(), 0);

    public TickSet(IReadOnlyList<Tick> majorTicks, IReadOnlyList<double> minorTicks, double step)
    {
        MajorTicks = majorTicks;
        MinorTicks = minorTicks;
        Step = step;
    }

    /// <summary>The labeled major ticks, in ascending value order.</summary>
    public IReadOnlyList<Tick> MajorTicks { get; }

    /// <summary>The minor tick positions between major ticks, in ascending value order.</summary>
    public IReadOnlyList<double> MinorTicks { get; }

    /// <summary>The spacing between major ticks (data units), or 0 when there are none.</summary>
    public double Step { get; }
}
