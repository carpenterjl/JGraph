namespace JGraph.Core.Primitives;

/// <summary>An immutable 2D size (width and height). Non-negative by convention.</summary>
public readonly struct Size2D : IEquatable<Size2D>
{
    public Size2D(double width, double height)
    {
        Width = width;
        Height = height;
    }

    public double Width { get; }

    public double Height { get; }

    public static Size2D Empty => new(0, 0);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public double Area => Width * Height;

    public bool Equals(Size2D other) => Width.Equals(other.Width) && Height.Equals(other.Height);

    public override bool Equals(object? obj) => obj is Size2D other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Width, Height);

    public static bool operator ==(Size2D left, Size2D right) => left.Equals(right);

    public static bool operator !=(Size2D left, Size2D right) => !left.Equals(right);

    public override string ToString() => $"{Width:G6} x {Height:G6}";
}
