namespace JGraph.Core.Primitives;

/// <summary>
/// An immutable 3D displacement vector. Used for surface normals, light directions, and the camera
/// basis of a 3D axes — all of which live in the projection's normalized cube space rather than in
/// data units, so that a surface whose Z spans millions lights the same as one that spans units.
/// </summary>
public readonly struct Vector3D : IEquatable<Vector3D>
{
    public Vector3D(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public double X { get; }

    public double Y { get; }

    public double Z { get; }

    public static Vector3D Zero => new(0, 0, 0);

    /// <summary>The unit vector along +Z, the fallback normal of a surface with no usable slope.</summary>
    public static Vector3D UnitZ => new(0, 0, 1);

    public double Length => System.Math.Sqrt((X * X) + (Y * Y) + (Z * Z));

    public double LengthSquared => (X * X) + (Y * Y) + (Z * Z);

    /// <summary>This vector scaled to unit length, or <see cref="Zero"/> if it has no direction.</summary>
    public Vector3D Normalized()
    {
        double length = Length;
        return length > 1e-12 ? new Vector3D(X / length, Y / length, Z / length) : Zero;
    }

    public static double Dot(Vector3D a, Vector3D b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    public static Vector3D Cross(Vector3D a, Vector3D b) => new(
        (a.Y * b.Z) - (a.Z * b.Y),
        (a.Z * b.X) - (a.X * b.Z),
        (a.X * b.Y) - (a.Y * b.X));

    public static Vector3D operator +(Vector3D a, Vector3D b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Vector3D operator -(Vector3D a, Vector3D b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Vector3D operator -(Vector3D v) => new(-v.X, -v.Y, -v.Z);

    public static Vector3D operator *(Vector3D v, double scalar) => new(v.X * scalar, v.Y * scalar, v.Z * scalar);

    public static Vector3D operator /(Vector3D v, double scalar) => new(v.X / scalar, v.Y / scalar, v.Z / scalar);

    public bool Equals(Vector3D other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

    public override bool Equals(object? obj) => obj is Vector3D other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y, Z);

    public static bool operator ==(Vector3D left, Vector3D right) => left.Equals(right);

    public static bool operator !=(Vector3D left, Vector3D right) => !left.Equals(right);

    public override string ToString() => $"<{X:G6}, {Y:G6}, {Z:G6}>";
}
