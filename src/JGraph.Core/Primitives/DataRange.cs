namespace JGraph.Core.Primitives;

/// <summary>
/// An immutable closed interval [Min, Max] over the real numbers, used for axis ranges and
/// data extents. An "empty" range (created with <see cref="Empty"/>) has Min &gt; Max and acts
/// as the identity element for <see cref="Union(JGraph.Core.Primitives.DataRange)"/>.
/// </summary>
public readonly struct DataRange : IEquatable<DataRange>
{
    public DataRange(double min, double max)
    {
        Min = min;
        Max = max;
    }

    public double Min { get; }

    public double Max { get; }

    /// <summary>
    /// An empty/uninitialized range (+inf..-inf) that yields the other operand when unioned.
    /// </summary>
    public static DataRange Empty => new(double.PositiveInfinity, double.NegativeInfinity);

    /// <summary>The default fallback range shown when there is no data, [0, 1].</summary>
    public static DataRange Unit => new(0, 1);

    public double Length => Max - Min;

    public double Center => (Min + Max) / 2.0;

    /// <summary>True when the range describes a real, positive-width interval.</summary>
    public bool IsValid => double.IsFinite(Min) && double.IsFinite(Max) && Max > Min;

    /// <summary>True when the range is the empty/identity range.</summary>
    public bool IsEmpty => Min > Max;

    public bool Contains(double value) => value >= Min && value <= Max;

    /// <summary>Returns the smallest range containing both this range and <paramref name="value"/>.</summary>
    public DataRange Include(double value)
    {
        if (double.IsNaN(value))
        {
            return this;
        }

        return new DataRange(System.Math.Min(Min, value), System.Math.Max(Max, value));
    }

    /// <summary>Returns the smallest range containing both this and <paramref name="other"/>.</summary>
    public DataRange Union(DataRange other) =>
        new(System.Math.Min(Min, other.Min), System.Math.Max(Max, other.Max));

    /// <summary>Returns this range grown symmetrically by <paramref name="fraction"/> of its length on each side.</summary>
    public DataRange Expand(double fraction)
    {
        double pad = Length * fraction;
        return new DataRange(Min - pad, Max + pad);
    }

    /// <summary>
    /// Returns a guaranteed-valid range: if this range has zero or negative width, it is expanded
    /// to a small interval around its value so downstream transforms never divide by zero.
    /// </summary>
    public DataRange EnsureValid()
    {
        if (IsValid)
        {
            return this;
        }

        if (IsEmpty || double.IsNaN(Min) || double.IsNaN(Max) || double.IsInfinity(Min) || double.IsInfinity(Max))
        {
            return Unit;
        }

        // Finite but zero (or inverted) width: pad around the midpoint.
        double center = Center;
        double pad = center == 0 ? 0.5 : System.Math.Abs(center) * 0.05;
        return new DataRange(center - pad, center + pad);
    }

    public bool Equals(DataRange other) => Min.Equals(other.Min) && Max.Equals(other.Max);

    public override bool Equals(object? obj) => obj is DataRange other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Min, Max);

    public static bool operator ==(DataRange left, DataRange right) => left.Equals(right);

    public static bool operator !=(DataRange left, DataRange right) => !left.Equals(right);

    public override string ToString() => $"[{Min:G6}, {Max:G6}]";
}
