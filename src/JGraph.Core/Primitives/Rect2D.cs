namespace JGraph.Core.Primitives;

/// <summary>
/// An immutable axis-aligned rectangle defined by its top-left corner plus width and height.
/// In device space, Y increases downward; in data space, callers may use it with either
/// convention as long as they are consistent.
/// </summary>
public readonly struct Rect2D : IEquatable<Rect2D>
{
    public Rect2D(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public Rect2D(Point2D location, Size2D size)
        : this(location.X, location.Y, size.Width, size.Height)
    {
    }

    public double X { get; }

    public double Y { get; }

    public double Width { get; }

    public double Height { get; }

    public static Rect2D Empty => new(0, 0, 0, 0);

    public double Left => X;

    public double Top => Y;

    public double Right => X + Width;

    public double Bottom => Y + Height;

    public double CenterX => X + (Width / 2.0);

    public double CenterY => Y + (Height / 2.0);

    public Point2D Location => new(X, Y);

    public Point2D Center => new(CenterX, CenterY);

    public Size2D Size => new(Width, Height);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>Creates a rectangle from two opposite corners in any order.</summary>
    public static Rect2D FromCorners(Point2D a, Point2D b)
    {
        double x = System.Math.Min(a.X, b.X);
        double y = System.Math.Min(a.Y, b.Y);
        double w = System.Math.Abs(a.X - b.X);
        double h = System.Math.Abs(a.Y - b.Y);
        return new Rect2D(x, y, w, h);
    }

    public bool Contains(Point2D p) => p.X >= Left && p.X <= Right && p.Y >= Top && p.Y <= Bottom;

    public bool Contains(double x, double y) => x >= Left && x <= Right && y >= Top && y <= Bottom;

    public bool IntersectsWith(Rect2D other) =>
        other.Left <= Right && other.Right >= Left && other.Top <= Bottom && other.Bottom >= Top;

    /// <summary>Returns this rectangle shrunk on every side by the given margins.</summary>
    public Rect2D Deflate(Thickness margin) => new(
        X + margin.Left,
        Y + margin.Top,
        System.Math.Max(0, Width - margin.Left - margin.Right),
        System.Math.Max(0, Height - margin.Top - margin.Bottom));

    /// <summary>Returns this rectangle expanded on every side by the given margins.</summary>
    public Rect2D Inflate(Thickness margin) => new(
        X - margin.Left,
        Y - margin.Top,
        Width + margin.Left + margin.Right,
        Height + margin.Top + margin.Bottom);

    public bool Equals(Rect2D other) =>
        X.Equals(other.X) && Y.Equals(other.Y) && Width.Equals(other.Width) && Height.Equals(other.Height);

    public override bool Equals(object? obj) => obj is Rect2D other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);

    public static bool operator ==(Rect2D left, Rect2D right) => left.Equals(right);

    public static bool operator !=(Rect2D left, Rect2D right) => !left.Equals(right);

    public override string ToString() => $"[X={X:G6} Y={Y:G6} W={Width:G6} H={Height:G6}]";
}
