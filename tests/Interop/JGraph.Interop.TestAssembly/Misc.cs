using System;
using System.Collections;
using System.Collections.Generic;

namespace JGTest
{
    /// <summary>A value type: MATLAB gets a copy, so mutating it must not reach the original.</summary>
    public struct Point
    {
        public Point(double x, double y) { X = x; Y = y; }
        public double X;
        public double Y;
        public double Length => Math.Sqrt(X * X + Y * Y);
        public void Move(double dx) { X += dx; }
        public static Point Shift(Point p, double dx) { p.X += dx; return p; }
        public override string ToString() => $"({X}, {Y})";
    }

    public class Resource : IDisposable
    {
        public static int Disposed { get; private set; }
        public static void ResetCount() => Disposed = 0;
        public bool IsDisposed { get; private set; }
        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            Disposed++;
        }
    }

    public class Vector2
    {
        public Vector2(double x, double y) { X = x; Y = y; }
        public double X { get; }
        public double Y { get; }
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.X + b.X, a.Y + b.Y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.X - b.X, a.Y - b.Y);
        public static Vector2 operator *(Vector2 a, double s) => new Vector2(a.X * s, a.Y * s);
        public static bool operator ==(Vector2 a, Vector2 b) => a?.X == b?.X && a?.Y == b?.Y;
        public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);
        public override bool Equals(object obj) => obj is Vector2 v && v == this;
        public override int GetHashCode() => X.GetHashCode() ^ Y.GetHashCode();
        public override string ToString() => $"<{X}, {Y}>";
    }

    public interface IGreeter
    {
        string Greet();
    }

    /// <summary>Greet is reachable only through the interface.</summary>
    public class Greeter : IGreeter
    {
        string IGreeter.Greet() => "hi from the interface";
        public string Public() => "hi";
    }

    public class Outer
    {
        public class Inner
        {
            public string Where() => "inner";
        }
        public static Inner MakeInner() => new Inner();
    }

    public class Sequence : IEnumerable<int>
    {
        private readonly int _n;
        public Sequence(int n) { _n = n; }
        public IEnumerator<int> GetEnumerator()
        {
            for (int i = 1; i <= _n; i++) yield return i;
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Returns one value of each primitive, including the ones past 2^53.</summary>
    public static class Returns
    {
        public static bool Bool() => true;
        public static byte Byte() => 200;
        public static sbyte SByte() => -100;
        public static short Int16() => -30000;
        public static ushort UInt16() => 60000;
        public static int Int32() => -2000000000;
        public static uint UInt32() => 4000000000;
        public static long Int64() => 9007199254740993L;
        public static ulong UInt64() => 18446744073709551615UL;
        public static float Single() => 1.25f;
        public static double Double() => 2.5;
        public static char Char() => 'Z';
        public static string String() => "text";
        public static string NullString() => null;
        public static object NullObject() => null;
        public static decimal Decimal() => 1.1m;
        public static IntPtr Pointer() => new IntPtr(4096);
        public static DateTime Date() => new DateTime(2026, 9, 26, 12, 30, 0);
        public static TimeSpan Span() => TimeSpan.FromSeconds(90);
        public static object BoxedDouble() => 3.5;
        public static object BoxedString() => "boxed";
        public static Guid Id() => new Guid("01234567-89ab-cdef-0123-456789abcdef");
    }
}
