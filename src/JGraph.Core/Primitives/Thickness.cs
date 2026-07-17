namespace JGraph.Core.Primitives;

/// <summary>Immutable per-side spacing (margins, padding, insets), analogous to WPF's Thickness.</summary>
public readonly struct Thickness : IEquatable<Thickness>
{
    public Thickness(double uniform)
        : this(uniform, uniform, uniform, uniform)
    {
    }

    public Thickness(double left, double top, double right, double bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public double Left { get; }

    public double Top { get; }

    public double Right { get; }

    public double Bottom { get; }

    public static Thickness Zero => new(0);

    public double Horizontal => Left + Right;

    public double Vertical => Top + Bottom;

    public bool Equals(Thickness other) =>
        Left.Equals(other.Left) && Top.Equals(other.Top) && Right.Equals(other.Right) && Bottom.Equals(other.Bottom);

    public override bool Equals(object? obj) => obj is Thickness other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);

    public static bool operator ==(Thickness left, Thickness right) => left.Equals(right);

    public static bool operator !=(Thickness left, Thickness right) => !left.Equals(right);

    public override string ToString() => $"({Left}, {Top}, {Right}, {Bottom})";
}
