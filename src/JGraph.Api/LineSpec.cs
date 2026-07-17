using JGraph.Core.Drawing;

namespace JGraph.Api;

/// <summary>
/// The parsed result of a MATLAB-style line specification string such as <c>"r--o"</c>: an optional
/// color, dash style, and marker, plus flags recording which elements the string actually specified
/// (so callers can distinguish "line only", "markers only", and "line + markers").
/// </summary>
public readonly struct LineSpec
{
    public LineSpec(
        Color? color,
        DashStyle? dash,
        MarkerType? marker,
        bool lineSpecified,
        bool markerSpecified)
    {
        Color = color;
        Dash = dash;
        Marker = marker;
        LineSpecified = lineSpecified;
        MarkerSpecified = markerSpecified;
    }

    public Color? Color { get; }

    public DashStyle? Dash { get; }

    public MarkerType? Marker { get; }

    /// <summary>True when the string included an explicit line style (<c>-</c>, <c>--</c>, <c>:</c>, <c>-.</c>).</summary>
    public bool LineSpecified { get; }

    /// <summary>True when the string included a marker glyph.</summary>
    public bool MarkerSpecified { get; }

    /// <summary>Parses a MATLAB line-spec string. Unknown characters are ignored.</summary>
    public static LineSpec Parse(string? spec)
    {
        Color? color = null;
        DashStyle? dash = null;
        MarkerType? marker = null;
        bool lineSpecified = false;
        bool markerSpecified = false;

        if (string.IsNullOrEmpty(spec))
        {
            return new LineSpec(color, dash, marker, lineSpecified, markerSpecified);
        }

        int i = 0;
        while (i < spec.Length)
        {
            char c = spec[i];

            // Two-character line styles must be tested before the single '-'.
            if (c == '-' && i + 1 < spec.Length && spec[i + 1] == '-')
            {
                dash = DashStyle.Dash;
                lineSpecified = true;
                i += 2;
                continue;
            }

            if (c == '-' && i + 1 < spec.Length && spec[i + 1] == '.')
            {
                dash = DashStyle.DashDot;
                lineSpecified = true;
                i += 2;
                continue;
            }

            switch (c)
            {
                case '-':
                    dash = DashStyle.Solid;
                    lineSpecified = true;
                    break;
                case ':':
                    dash = DashStyle.Dot;
                    lineSpecified = true;
                    break;

                default:
                    if (TryColor(c, out Color parsedColor))
                    {
                        color = parsedColor;
                    }
                    else if (TryMarker(c, out MarkerType parsedMarker))
                    {
                        marker = parsedMarker;
                        markerSpecified = true;
                    }

                    break;
            }

            i++;
        }

        return new LineSpec(color, dash, marker, lineSpecified, markerSpecified);
    }

    private static bool TryColor(char c, out Color color)
    {
        color = c switch
        {
            'b' => Colors.Blue,
            'g' => Colors.Green,
            'r' => Colors.Red,
            'c' => Colors.Cyan,
            'm' => Colors.Magenta,
            'y' => Colors.Yellow,
            'k' => Colors.Black,
            'w' => Colors.White,
            _ => default,
        };
        return c is 'b' or 'g' or 'r' or 'c' or 'm' or 'y' or 'k' or 'w';
    }

    private static bool TryMarker(char c, out MarkerType marker)
    {
        marker = c switch
        {
            'o' => MarkerType.Circle,
            '.' => MarkerType.Point,
            'x' => MarkerType.Cross,
            '+' => MarkerType.Plus,
            '*' => MarkerType.Star,
            's' => MarkerType.Square,
            'd' => MarkerType.Diamond,
            '^' => MarkerType.TriangleUp,
            'v' => MarkerType.TriangleDown,
            'p' => MarkerType.Star,
            'h' => MarkerType.Star,
            _ => MarkerType.None,
        };
        return marker != MarkerType.None;
    }
}
