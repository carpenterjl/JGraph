using System.Globalization;

namespace JGraph.Core.Drawing;

/// <summary>
/// An immutable, engine-independent 32-bit RGBA color. JGraph never uses
/// <c>System.Windows.Media.Color</c> or SkiaSharp colors in its model layers; rendering
/// backends translate this type at the boundary.
/// </summary>
public readonly struct Color : IEquatable<Color>
{
    public Color(byte r, byte g, byte b, byte a = 255)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    public byte R { get; }

    public byte G { get; }

    public byte B { get; }

    public byte A { get; }

    public bool IsTransparent => A == 0;

    public static Color FromRgb(byte r, byte g, byte b) => new(r, g, b);

    public static Color FromArgb(byte a, byte r, byte g, byte b) => new(r, g, b, a);

    /// <summary>Creates a color from normalized [0, 1] components.</summary>
    public static Color FromScRgb(double r, double g, double b, double a = 1.0) => new(
        ToByte(r), ToByte(g), ToByte(b), ToByte(a));

    /// <summary>Returns this color with a replaced alpha channel.</summary>
    public Color WithAlpha(byte alpha) => new(R, G, B, alpha);

    /// <summary>Returns this color with alpha scaled by the given opacity in [0, 1].</summary>
    public Color WithOpacity(double opacity) => new(R, G, B, ToByte(A / 255.0 * opacity));

    /// <summary>Linearly interpolates between two colors. <paramref name="t"/> is clamped to [0, 1].</summary>
    public static Color Lerp(Color a, Color b, double t)
    {
        t = System.Math.Clamp(t, 0, 1);
        return new Color(
            (byte)(a.R + ((b.R - a.R) * t)),
            (byte)(a.G + ((b.G - a.G) * t)),
            (byte)(a.B + ((b.B - a.B) * t)),
            (byte)(a.A + ((b.A - a.A) * t)));
    }

    /// <summary>Packs the color into a 0xAARRGGBB integer.</summary>
    public uint ToArgb() => ((uint)A << 24) | ((uint)R << 16) | ((uint)G << 8) | B;

    /// <summary>Parses "#RGB", "#RRGGBB", or "#AARRGGBB" (leading '#' optional).</summary>
    public static Color Parse(string hex)
    {
        if (TryParse(hex, out Color color))
        {
            return color;
        }

        throw new FormatException($"'{hex}' is not a valid hex color.");
    }

    public static bool TryParse(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        ReadOnlySpan<char> s = hex.AsSpan().Trim();
        if (s.Length > 0 && s[0] == '#')
        {
            s = s[1..];
        }

        switch (s.Length)
        {
            case 3:
            {
                if (!TryHex(s[0], out int r3) || !TryHex(s[1], out int g3) || !TryHex(s[2], out int b3))
                {
                    return false;
                }

                color = new Color((byte)(r3 * 17), (byte)(g3 * 17), (byte)(b3 * 17));
                return true;
            }

            case 6:
            {
                if (!TryByte(s.Slice(0, 2), out byte r) ||
                    !TryByte(s.Slice(2, 2), out byte g) ||
                    !TryByte(s.Slice(4, 2), out byte b))
                {
                    return false;
                }

                color = new Color(r, g, b);
                return true;
            }

            case 8:
            {
                if (!TryByte(s.Slice(0, 2), out byte a) ||
                    !TryByte(s.Slice(2, 2), out byte r) ||
                    !TryByte(s.Slice(4, 2), out byte g) ||
                    !TryByte(s.Slice(6, 2), out byte b))
                {
                    return false;
                }

                color = new Color(r, g, b, a);
                return true;
            }

            default:
                return false;
        }
    }

    public string ToHex() => A == 255
        ? $"#{R:X2}{G:X2}{B:X2}"
        : $"#{A:X2}{R:X2}{G:X2}{B:X2}";

    public bool Equals(Color other) => R == other.R && G == other.G && B == other.B && A == other.A;

    public override bool Equals(object? obj) => obj is Color other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(R, G, B, A);

    public static bool operator ==(Color left, Color right) => left.Equals(right);

    public static bool operator !=(Color left, Color right) => !left.Equals(right);

    public override string ToString() => ToHex();

    private static byte ToByte(double v) => (byte)System.Math.Clamp(System.Math.Round(v * 255.0), 0, 255);

    private static bool TryByte(ReadOnlySpan<char> s, out byte value) =>
        byte.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);

    private static bool TryHex(char c, out int value)
    {
        value = 0;
        if (c is >= '0' and <= '9')
        {
            value = c - '0';
        }
        else if (c is >= 'a' and <= 'f')
        {
            value = c - 'a' + 10;
        }
        else if (c is >= 'A' and <= 'F')
        {
            value = c - 'A' + 10;
        }
        else
        {
            return false;
        }

        return true;
    }
}
