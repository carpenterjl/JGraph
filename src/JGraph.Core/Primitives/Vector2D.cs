namespace JGraph.Core.Primitives;

/// <summary>An immutable 2D displacement vector.</summary>
public readonly struct Vector2D : IEquatable<Vector2D>
{
    public Vector2D(double x, double y)
    {
        X = x;
        Y = y;
    }

    public double X { get; }

    public double Y { get; }

    public static Vector2D Zero => new(0, 0);

    public double Length => System.Math.Sqrt((X * X) + (Y * Y));

    public double LengthSquared => (X * X) + (Y * Y);

    public static Vector2D operator +(Vector2D a, Vector2D b) => new(a.X + b.X, a.Y + b.Y);

    public static Vector2D operator -(Vector2D a, Vector2D b) => new(a.X - b.X, a.Y - b.Y);

    public static Vector2D operator *(Vector2D v, double scalar) => new(v.X * scalar, v.Y * scalar);

    public static Vector2D operator /(Vector2D v, double scalar) => new(v.X / scalar, v.Y / scalar);

    public bool Equals(Vector2D other) => X.Equals(other.X) && Y.Equals(other.Y);

    public override bool Equals(object? obj) => obj is Vector2D other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y);

    public static bool operator ==(Vector2D left, Vector2D right) => left.Equals(right);

    public static bool operator !=(Vector2D left, Vector2D right) => !left.Equals(right);

    public override string ToString() => $"<{X:G6}, {Y:G6}>";
}
