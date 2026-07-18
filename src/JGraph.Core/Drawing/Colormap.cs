namespace JGraph.Core.Drawing;

/// <summary>
/// A named, continuous mapping from a normalized scalar in [0, 1] to a <see cref="Color"/>, used by
/// image/heatmap plots to color a scalar field. A colormap is defined by an ordered list of color
/// stops evenly spaced across [0, 1]; <see cref="Sample(double)"/> interpolates linearly between them. The
/// type is engine-independent (like the rest of the model) so rendering backends never see it.
/// </summary>
public sealed class Colormap
{
    private readonly Color[] _stops;

    /// <summary>Creates a colormap from two or more evenly spaced color stops.</summary>
    public Colormap(string name, params Color[] stops)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(stops);
        if (stops.Length < 2)
        {
            throw new ArgumentException("A colormap needs at least two stops.", nameof(stops));
        }

        Name = name;
        _stops = (Color[])stops.Clone();
    }

    /// <summary>The colormap's display name.</summary>
    public string Name { get; }

    /// <summary>The color stops, from the low end of the scale to the high end.</summary>
    public IReadOnlyList<Color> Stops => _stops;

    /// <summary>
    /// Samples the colormap at <paramref name="t"/> (clamped to [0, 1]), interpolating between the two
    /// nearest stops. A non-finite input maps to the low-end color.
    /// </summary>
    public Color Sample(double t)
    {
        if (double.IsNaN(t))
        {
            return _stops[0];
        }

        t = System.Math.Clamp(t, 0, 1);
        double scaled = t * (_stops.Length - 1);
        int index = (int)System.Math.Floor(scaled);
        if (index >= _stops.Length - 1)
        {
            return _stops[^1];
        }

        double frac = scaled - index;
        return Color.Lerp(_stops[index], _stops[index + 1], frac);
    }

    /// <summary>Maps a value within [min, max] to a color, clamping out-of-range values to the ends.</summary>
    public Color Sample(double value, double min, double max)
    {
        double span = max - min;
        double t = System.Math.Abs(span) < double.Epsilon ? 0.5 : (value - min) / span;
        return Sample(t);
    }

    /// <summary>The perceptually uniform "viridis" map (dark blue → green → yellow). A good default.</summary>
    public static Colormap Viridis { get; } = new(
        "Viridis",
        Color.FromRgb(0x44, 0x01, 0x54),
        Color.FromRgb(0x47, 0x2D, 0x7B),
        Color.FromRgb(0x3B, 0x52, 0x8B),
        Color.FromRgb(0x2C, 0x72, 0x8E),
        Color.FromRgb(0x21, 0x91, 0x8C),
        Color.FromRgb(0x28, 0xAE, 0x80),
        Color.FromRgb(0x5E, 0xC9, 0x62),
        Color.FromRgb(0xAD, 0xDC, 0x30),
        Color.FromRgb(0xFD, 0xE7, 0x25));

    /// <summary>The classic rainbow "jet" map (dark blue → cyan → yellow → red).</summary>
    public static Colormap Jet { get; } = new(
        "Jet",
        Color.FromRgb(0x00, 0x00, 0x7F),
        Color.FromRgb(0x00, 0x00, 0xFF),
        Color.FromRgb(0x00, 0x7F, 0xFF),
        Color.FromRgb(0x00, 0xFF, 0xFF),
        Color.FromRgb(0x7F, 0xFF, 0x7F),
        Color.FromRgb(0xFF, 0xFF, 0x00),
        Color.FromRgb(0xFF, 0x7F, 0x00),
        Color.FromRgb(0xFF, 0x00, 0x00),
        Color.FromRgb(0x7F, 0x00, 0x00));

    /// <summary>A black → red → yellow → white "hot" map.</summary>
    public static Colormap Hot { get; } = new(
        "Hot",
        Color.FromRgb(0x0B, 0x00, 0x00),
        Color.FromRgb(0xFF, 0x00, 0x00),
        Color.FromRgb(0xFF, 0xFF, 0x00),
        Color.FromRgb(0xFF, 0xFF, 0xFF));

    /// <summary>A cyan → magenta "cool" map.</summary>
    public static Colormap Cool { get; } = new(
        "Cool",
        Color.FromRgb(0x00, 0xFF, 0xFF),
        Color.FromRgb(0xFF, 0x00, 0xFF));

    /// <summary>A simple black → white grayscale map.</summary>
    public static Colormap Grayscale { get; } = new(
        "Grayscale",
        Colors.Black,
        Colors.White);

    /// <summary>The names accepted by <see cref="TryGetByName"/>, for error messages and completion.</summary>
    public static IReadOnlyList<string> KnownNames { get; } = ["viridis", "jet", "hot", "cool", "gray"];

    /// <summary>
    /// Looks up a built-in colormap by name, case-insensitively ("gray" and "grayscale" both match
    /// the grayscale map). Used by the scripting <c>colormap(name)</c> verb and deserialization.
    /// </summary>
    public static bool TryGetByName(string? name, out Colormap colormap)
    {
        colormap = Viridis;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        Colormap? found = name.Trim().ToLowerInvariant() switch
        {
            "viridis" => Viridis,
            "jet" => Jet,
            "hot" => Hot,
            "cool" => Cool,
            "gray" or "grey" or "grayscale" or "greyscale" => Grayscale,
            _ => null,
        };

        if (found is null)
        {
            return false;
        }

        colormap = found;
        return true;
    }
}
