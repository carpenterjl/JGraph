namespace JGraph.Core.Primitives;

/// <summary>
/// An immutable 2D point in a Cartesian coordinate space. Used both for data-space
/// coordinates and (after transformation) for device/pixel-space coordinates.
/// </summary>
public readonly struct Point2D : IEquatable<Point2D>
{
    public Point2D(double x, double y)
    {
        X = x;
        Y = y;
    }

    public double X { get; }

    public double Y { get; }

    /// <summary>The origin, (0, 0).</summary>
    public static Point2D Zero => new(0, 0);

    /// <summary>A point whose components are both <see cref="double.NaN"/>, used to denote "no value".</summary>
    public static Point2D NaN => new(double.NaN, double.NaN);

    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);

    public Point2D WithX(double x) => new(x, Y);

    public Point2D WithY(double y) => new(X, y);

    public double DistanceTo(Point2D other)
    {
        double dx = X - other.X;
        double dy = Y - other.Y;
        return System.Math.Sqrt((dx * dx) + (dy * dy));
    }

    public static Point2D operator +(Point2D p, Vector2D v) => new(p.X + v.X, p.Y + v.Y);

    public static Point2D operator -(Point2D p, Vector2D v) => new(p.X - v.X, p.Y - v.Y);

    public static Vector2D operator -(Point2D a, Point2D b) => new(a.X - b.X, a.Y - b.Y);

    public bool Equals(Point2D other) => X.Equals(other.X) && Y.Equals(other.Y);

    public override bool Equals(object? obj) => obj is Point2D other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y);

    public static bool operator ==(Point2D left, Point2D right) => left.Equals(right);

    public static bool operator !=(Point2D left, Point2D right) => !left.Equals(right);

    public override string ToString() => $"({X:G6}, {Y:G6})";
}
