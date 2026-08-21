namespace JGraph.Core.Drawing;

/// <summary>
/// A named, continuous mapping from a normalized scalar in [0, 1] to a <see cref="Color"/>, used by
/// image/heatmap/surface plots to color a scalar field. A colormap is defined by an ordered list of
/// color stops evenly spaced across [0, 1]; <see cref="Sample(double)"/> interpolates linearly
/// between them. The type is engine-independent (like the rest of the model) so rendering backends
/// never see it.
/// </summary>
/// <remarks>
/// <para>
/// The built-in maps reproduce MATLAB's. Most of MATLAB's are piecewise linear in RGB with
/// breakpoints at simple fractions — <c>hot</c> turns at 3/8 and 6/8, <c>copper</c>'s red saturates
/// at 4/5 — so each one carries exactly the number of stops that puts a stop on every breakpoint and
/// is then an exact reproduction, not an approximation. Two are not: <c>pink</c> is the square root
/// of a piecewise-linear map and <c>parula</c> is a published table with no closed form, so both are
/// resampled onto evenly spaced stops and their worst-case error is recorded where they are defined.
/// </para>
/// <para>
/// Sampling interpolates the stops directly rather than going through a lookup table. A 256-entry
/// table looks like the more MATLAB-faithful choice — a MATLAB colormap is a table of rows, not a
/// function — but it is not the same table. MATLAB sizes each map's rows to its own definition, so
/// <c>hot(64)</c> puts a row exactly where red saturates; a 256-row grid imposed on top of a
/// nine-stop definition lands between the turns instead and rounds them off. Measured on this map
/// set it moved <c>hot</c>'s yellow turn by one count and <c>hsv</c>'s by three, in exchange for
/// saving a multiply on a path whose results are already cached a layer up.
/// </para>
/// </remarks>
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

    /// <summary>
    /// Whether the stops are a palette of distinct colors rather than a gradient. A discrete map
    /// divides [0, 1] into one bin per stop and returns that stop's color unblended, and
    /// <see cref="Resample"/> cycles the stops instead of interpolating between them.
    /// </summary>
    public bool Discrete { get; init; }

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
        if (Discrete)
        {
            return _stops[System.Math.Min((int)(t * _stops.Length), _stops.Length - 1)];
        }

        double scaled = t * (_stops.Length - 1);
        int index = (int)System.Math.Floor(scaled);
        if (index >= _stops.Length - 1)
        {
            return _stops[^1];
        }

        return Color.Lerp(_stops[index], _stops[index + 1], scaled - index);
    }

    /// <summary>Maps a value within [min, max] to a color, clamping out-of-range values to the ends.</summary>
    public Color Sample(double value, double min, double max)
    {
        double span = max - min;
        double t = System.Math.Abs(span) < double.Epsilon ? 0.5 : (value - min) / span;
        return Sample(t);
    }

    /// <summary>
    /// The same mapping with the axes' color scale applied (MATLAB <c>ColorScale</c>): logarithmic
    /// spreads the decades evenly instead of the values. Limits that cannot be logged — zero or
    /// negative — fall back to the linear spread, and a non-positive value lands on the low end.
    /// </summary>
    public Color Sample(double value, double min, double max, bool logScale)
    {
        if (!logScale || min <= 0 || max <= 0)
        {
            return Sample(value, min, max);
        }

        double low = System.Math.Log10(min);
        double high = System.Math.Log10(max);
        double span = high - low;
        double t = value <= 0 ? 0
            : System.Math.Abs(span) < double.Epsilon ? 0.5
            : (System.Math.Log10(value) - low) / span;
        return Sample(t);
    }

    /// <summary>
    /// The colormap as a table of <paramref name="count"/> rows, the form MATLAB's colormap
    /// generators return: <c>parula(64)</c> is <c>Parula.Resample(64)</c>. A gradient is sampled at
    /// evenly spaced points including both ends; a <see cref="Discrete"/> palette cycles, so
    /// <c>Lines.Resample(64)</c> repeats its seven colors the way <c>lines(64)</c> does.
    /// </summary>
    public Color[] Resample(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

        var rows = new Color[count];
        if (Discrete)
        {
            for (int i = 0; i < count; i++)
            {
                rows[i] = _stops[i % _stops.Length];
            }

            return rows;
        }

        if (count == 1)
        {
            rows[0] = Sample(0);
            return rows;
        }

        for (int i = 0; i < count; i++)
        {
            rows[i] = Sample(i / (double)(count - 1));
        }

        return rows;
    }

    /// <summary>
    /// A brightened (<paramref name="beta"/> &gt; 0) or darkened (&lt; 0) copy, by MATLAB's
    /// <c>brighten</c> rule: every component raised to <c>1 - beta</c> going up, or
    /// <c>1 / (1 + beta)</c> going down.
    /// </summary>
    /// <remarks>
    /// The gamma is applied to the <em>sampled table</em>, not to the stops, because MATLAB brightens
    /// a colormap matrix and a gradient's stops are not that matrix. Grayscale is the case that
    /// proves it: its two stops are black and white, both fixed points of any power law, so gamma on
    /// the stops would leave the map bit-for-bit unchanged while every value between them should have
    /// moved. A discrete palette has no in-between, so there the stops <em>are</em> the table.
    /// </remarks>
    public Colormap Brighten(double beta)
    {
        // MATLAB clamps just inside +/-1, where the gamma would collapse the map to white or black.
        const double Limit = 1 - 1e-9;
        double clamped = System.Math.Clamp(beta, -Limit, Limit);
        double gamma = clamped > 0 ? 1 - clamped : 1 / (1 + clamped);

        Color[] table = Discrete ? _stops : Resample(BrightenResolution);
        var stops = new Color[table.Length];
        for (int i = 0; i < stops.Length; i++)
        {
            Color entry = table[i];
            stops[i] = Color.FromScRgb(
                System.Math.Pow(entry.R / 255.0, gamma),
                System.Math.Pow(entry.G / 255.0, gamma),
                System.Math.Pow(entry.B / 255.0, gamma),
                entry.A / 255.0);
        }

        return new Colormap(Name, stops) { Discrete = Discrete };
    }

    /// <summary>
    /// How finely a gradient is sampled before <see cref="Brighten"/> reshapes it. Enough that the
    /// result is indistinguishable from brightening the continuous curve.
    /// </summary>
    private const int BrightenResolution = 256;

    /// <summary>
    /// Builds a colormap from an m-by-3 matrix of RGB components in [0, 1] — MATLAB's
    /// <c>colormap(map)</c> form. Components outside the range are clamped. A single row is
    /// duplicated, since a colormap needs two stops to be a mapping at all.
    /// </summary>
    public static Colormap FromRows(string name, double[,] rgb, bool discrete = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(rgb);
        if (rgb.GetLength(1) != 3)
        {
            throw new ArgumentException("A colormap matrix needs exactly three columns (R, G, B).", nameof(rgb));
        }

        int rows = rgb.GetLength(0);
        if (rows < 1)
        {
            throw new ArgumentException("A colormap matrix needs at least one row.", nameof(rgb));
        }

        var stops = new Color[System.Math.Max(rows, 2)];
        for (int r = 0; r < rows; r++)
        {
            stops[r] = Color.FromScRgb(
                System.Math.Clamp(rgb[r, 0], 0, 1),
                System.Math.Clamp(rgb[r, 1], 0, 1),
                System.Math.Clamp(rgb[r, 2], 0, 1));
        }

        if (rows == 1)
        {
            stops[1] = stops[0];
        }

        return new Colormap(name, stops) { Discrete = discrete };
    }

    /// <summary>
    /// MATLAB's default map since R2014b (dark blue-violet → blue → teal → green → yellow), and
    /// JGraph's. Resampled from the published R2017a table onto 33 evenly spaced stops, which tracks
    /// it to within 5/255 on every channel.
    /// </summary>
    public static Colormap Parula { get; } = new(
        "Parula",
        Color.FromRgb(0x3E, 0x26, 0xA8),
        Color.FromRgb(0x42, 0x2E, 0xBF),
        Color.FromRgb(0x45, 0x36, 0xD5),
        Color.FromRgb(0x47, 0x41, 0xE5),
        Color.FromRgb(0x48, 0x4C, 0xEF),
        Color.FromRgb(0x47, 0x57, 0xF7),
        Color.FromRgb(0x45, 0x62, 0xFC),
        Color.FromRgb(0x3F, 0x6E, 0xFE),
        Color.FromRgb(0x34, 0x7A, 0xFD),
        Color.FromRgb(0x2E, 0x85, 0xF8),
        Color.FromRgb(0x2C, 0x90, 0xF0),
        Color.FromRgb(0x26, 0x9A, 0xE9),
        Color.FromRgb(0x21, 0xA3, 0xE3),
        Color.FromRgb(0x1A, 0xAC, 0xDD),
        Color.FromRgb(0x0C, 0xB3, 0xD3),
        Color.FromRgb(0x01, 0xB9, 0xC6),
        Color.FromRgb(0x12, 0xBE, 0xB9),
        Color.FromRgb(0x27, 0xC2, 0xAB),
        Color.FromRgb(0x34, 0xC7, 0x9C),
        Color.FromRgb(0x43, 0xCB, 0x8A),
        Color.FromRgb(0x5C, 0xCC, 0x76),
        Color.FromRgb(0x77, 0xCC, 0x60),
        Color.FromRgb(0x94, 0xCA, 0x4A),
        Color.FromRgb(0xAF, 0xC6, 0x36),
        Color.FromRgb(0xC8, 0xC1, 0x29),
        Color.FromRgb(0xDE, 0xBC, 0x2A),
        Color.FromRgb(0xF1, 0xBA, 0x37),
        Color.FromRgb(0xFE, 0xBE, 0x3C),
        Color.FromRgb(0xFE, 0xCA, 0x33),
        Color.FromRgb(0xF9, 0xD6, 0x2D),
        Color.FromRgb(0xF5, 0xE3, 0x27),
        Color.FromRgb(0xF6, 0xEF, 0x20),
        Color.FromRgb(0xF9, 0xFB, 0x15));

    /// <summary>The perceptually uniform "viridis" map (dark purple → green → yellow).</summary>
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

    /// <summary>
    /// The classic rainbow "jet" map (dark blue → cyan → yellow → red). Piecewise linear with
    /// breakpoints at the eighths, so these nine stops are exact.
    /// </summary>
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

    /// <summary>
    /// A black → red → yellow → white "hot" map. Red saturates at 3/8 of the way up and green at
    /// 6/8, so it takes nine stops to place a stop on each turn; four evenly spaced ones put the
    /// turns at the thirds instead, which is visibly the wrong map.
    /// </summary>
    public static Colormap Hot { get; } = new(
        "Hot",
        Color.FromRgb(0x00, 0x00, 0x00),
        Color.FromRgb(0x55, 0x00, 0x00),
        Color.FromRgb(0xAA, 0x00, 0x00),
        Color.FromRgb(0xFF, 0x00, 0x00),
        Color.FromRgb(0xFF, 0x55, 0x00),
        Color.FromRgb(0xFF, 0xAA, 0x00),
        Color.FromRgb(0xFF, 0xFF, 0x00),
        Color.FromRgb(0xFF, 0xFF, 0x80),
        Color.FromRgb(0xFF, 0xFF, 0xFF));

    /// <summary>A cyan → magenta "cool" map. Linear in every channel, so two stops are exact.</summary>
    public static Colormap Cool { get; } = new(
        "Cool",
        Color.FromRgb(0x00, 0xFF, 0xFF),
        Color.FromRgb(0xFF, 0x00, 0xFF));

    /// <summary>A simple black → white grayscale map.</summary>
    public static Colormap Grayscale { get; } = new(
        "Grayscale",
        Colors.Black,
        Colors.White);

    /// <summary>
    /// Google's "turbo" map (blue → cyan → green → yellow → red) — an improved rainbow that avoids
    /// jet's artificial bright bands. Stops sampled from the published lookup table.
    /// </summary>
    public static Colormap Turbo { get; } = new(
        "Turbo",
        Color.FromRgb(0x30, 0x12, 0x3B),
        Color.FromRgb(0x41, 0x45, 0xAB),
        Color.FromRgb(0x46, 0x75, 0xED),
        Color.FromRgb(0x39, 0xA2, 0xFC),
        Color.FromRgb(0x1B, 0xCF, 0xD4),
        Color.FromRgb(0x24, 0xEC, 0xA6),
        Color.FromRgb(0x61, 0xFC, 0x6C),
        Color.FromRgb(0xA4, 0xFC, 0x3C),
        Color.FromRgb(0xD1, 0xE8, 0x35),
        Color.FromRgb(0xF3, 0xC6, 0x3A),
        Color.FromRgb(0xFE, 0x92, 0x2A),
        Color.FromRgb(0xEA, 0x4F, 0x0D),
        Color.FromRgb(0xBE, 0x21, 0x02),
        Color.FromRgb(0x7A, 0x03, 0x03));

    /// <summary>
    /// The full hue circle at full saturation and value — MATLAB's <c>hsv</c>. Red through the
    /// spectrum and back to red, so the two ends are the same color and the map is only sensible for
    /// cyclic data. Piecewise linear at the sixths, so these seven stops are exact.
    /// </summary>
    public static Colormap Hsv { get; } = new(
        "Hsv",
        Color.FromRgb(0xFF, 0x00, 0x00),
        Color.FromRgb(0xFF, 0xFF, 0x00),
        Color.FromRgb(0x00, 0xFF, 0x00),
        Color.FromRgb(0x00, 0xFF, 0xFF),
        Color.FromRgb(0x00, 0x00, 0xFF),
        Color.FromRgb(0xFF, 0x00, 0xFF),
        Color.FromRgb(0xFF, 0x00, 0x00));

    /// <summary>
    /// A blue-tinted grayscale — MATLAB defines it as seven parts gray to one part <c>hot</c> with
    /// its channels reversed, which inherits hot's turns at 3/8 and 6/8 and so is exact at the
    /// eighths.
    /// </summary>
    public static Colormap Bone { get; } = new(
        "Bone",
        Color.FromRgb(0x00, 0x00, 0x00),
        Color.FromRgb(0x1C, 0x1C, 0x27),
        Color.FromRgb(0x38, 0x38, 0x4D),
        Color.FromRgb(0x54, 0x54, 0x74),
        Color.FromRgb(0x70, 0x7A, 0x8F),
        Color.FromRgb(0x8B, 0xA1, 0xAB),
        Color.FromRgb(0xA7, 0xC7, 0xC7),
        Color.FromRgb(0xD3, 0xE3, 0xE3),
        Color.FromRgb(0xFF, 0xFF, 0xFF));

    /// <summary>
    /// Black through metallic copper. Linear in every channel, but red saturates at 4/5 of the way
    /// up, so the stops are fifths to land on that turn exactly.
    /// </summary>
    public static Colormap Copper { get; } = new(
        "Copper",
        Color.FromRgb(0x00, 0x00, 0x00),
        Color.FromRgb(0x40, 0x28, 0x19),
        Color.FromRgb(0x80, 0x50, 0x33),
        Color.FromRgb(0xBF, 0x78, 0x4C),
        Color.FromRgb(0xFF, 0x9F, 0x65),
        Color.FromRgb(0xFF, 0xC7, 0x7F));

    /// <summary>
    /// Pastel shades of pink. The one built-in map that evenly spaced stops cannot reproduce
    /// exactly: MATLAB defines it as the square root of a blend of gray and <c>hot</c>, and a square
    /// root has infinite slope at zero, so no linear segment can follow it out of black. Seventeen
    /// stops hold the error to 20/255, all of it inside the darkest sixteenth of the range.
    /// </summary>
    public static Colormap Pink { get; } = new(
        "Pink",
        Color.FromRgb(0x00, 0x00, 0x00),
        Color.FromRgb(0x50, 0x34, 0x34),
        Color.FromRgb(0x70, 0x4A, 0x4A),
        Color.FromRgb(0x8A, 0x5A, 0x5A),
        Color.FromRgb(0x9F, 0x68, 0x68),
        Color.FromRgb(0xB2, 0x74, 0x74),
        Color.FromRgb(0xC3, 0x80, 0x80),
        Color.FromRgb(0xCA, 0x96, 0x8A),
        Color.FromRgb(0xD0, 0xAA, 0x93),
        Color.FromRgb(0xD7, 0xBC, 0x9C),
        Color.FromRgb(0xDD, 0xCC, 0xA5),
        Color.FromRgb(0xE3, 0xDB, 0xAD),
        Color.FromRgb(0xE9, 0xE9, 0xB4),
        Color.FromRgb(0xEF, 0xEF, 0xCA),
        Color.FromRgb(0xF4, 0xF4, 0xDD),
        Color.FromRgb(0xFA, 0xFA, 0xEF),
        Color.FromRgb(0xFF, 0xFF, 0xFF));

    /// <summary>Magenta → yellow. Linear, so two stops are exact.</summary>
    public static Colormap Spring { get; } = new(
        "Spring",
        Color.FromRgb(0xFF, 0x00, 0xFF),
        Color.FromRgb(0xFF, 0xFF, 0x00));

    /// <summary>Green → yellow, at a fixed low blue. Linear, so two stops are exact.</summary>
    public static Colormap Summer { get; } = new(
        "Summer",
        Color.FromRgb(0x00, 0x80, 0x66),
        Color.FromRgb(0xFF, 0xFF, 0x66));

    /// <summary>Red → orange → yellow. Linear, so two stops are exact.</summary>
    public static Colormap Autumn { get; } = new(
        "Autumn",
        Color.FromRgb(0xFF, 0x00, 0x00),
        Color.FromRgb(0xFF, 0xFF, 0x00));

    /// <summary>Blue → green. Linear, so two stops are exact.</summary>
    public static Colormap Winter { get; } = new(
        "Winter",
        Color.FromRgb(0x00, 0x00, 0xFF),
        Color.FromRgb(0x00, 0xFF, 0x80));

    /// <summary>
    /// MATLAB's seven default line colors as a discrete palette. <see cref="Resample"/> cycles them,
    /// which is exactly what <c>lines(n)</c> returns.
    /// </summary>
    /// <remarks>
    /// A documented divergence: applied to a surface or image, this colors the data in seven bins,
    /// whereas MATLAB's <c>colormap(lines)</c> builds a table as long as the current colormap and so
    /// repeats the seven colors roughly nine times across the range. Seven bins is the useful
    /// reading; the striped one is available by passing the matrix, <c>colormap(lines(64))</c>.
    /// </remarks>
    public static Colormap Lines { get; } = new Colormap(
        "Lines",
        Color.FromRgb(0x00, 0x72, 0xBD),
        Color.FromRgb(0xD9, 0x53, 0x19),
        Color.FromRgb(0xED, 0xB1, 0x20),
        Color.FromRgb(0x7E, 0x2F, 0x8E),
        Color.FromRgb(0x77, 0xAC, 0x30),
        Color.FromRgb(0x4D, 0xBE, 0xEE),
        Color.FromRgb(0xA2, 0x14, 0x2F))
    { Discrete = true };

    /// <summary>
    /// MATLAB's <c>flag</c>: red, white, blue and black, repeating. A palette rather than a
    /// gradient, so four stops are the whole definition and every resampling of it is exact.
    /// </summary>
    public static Colormap Flag { get; } = new Colormap(
        "Flag",
        Color.FromRgb(0xFF, 0x00, 0x00),
        Colors.White,
        Color.FromRgb(0x00, 0x00, 0xFF),
        Colors.Black)
    { Discrete = true };

    /// <summary>MATLAB's <c>prism</c>: the six spectrum colours, repeating. Exact for the same reason.</summary>
    public static Colormap Prism { get; } = new Colormap(
        "Prism",
        Color.FromRgb(0xFF, 0x00, 0x00),
        Color.FromRgb(0xFF, 0x80, 0x00),
        Color.FromRgb(0xFF, 0xFF, 0x00),
        Color.FromRgb(0x00, 0xFF, 0x00),
        Color.FromRgb(0x00, 0x00, 0xFF),
        Color.FromRgb(0x80, 0x00, 0xFF))
    { Discrete = true };

    /// <summary>The names accepted by <see cref="TryGetByName"/>, for error messages and completion.</summary>
    public static IReadOnlyList<string> KnownNames { get; } =
    [
        "parula", "viridis", "turbo", "jet", "hot", "cool", "gray", "hsv",
        "bone", "copper", "pink", "spring", "summer", "autumn", "winter", "lines",
        "flag", "prism",
    ];

    /// <summary>
    /// Looks up a built-in colormap by name, case-insensitively ("gray" and "grayscale" both match
    /// the grayscale map). Used by the scripting <c>colormap(name)</c> verb and deserialization.
    /// </summary>
    public static bool TryGetByName(string? name, out Colormap colormap)
    {
        colormap = Parula;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        Colormap? found = name.Trim().ToLowerInvariant() switch
        {
            "parula" => Parula,
            "viridis" => Viridis,
            "jet" => Jet,
            "hot" => Hot,
            "cool" => Cool,
            "gray" or "grey" or "grayscale" or "greyscale" => Grayscale,
            "turbo" => Turbo,
            "hsv" => Hsv,
            "bone" => Bone,
            "copper" => Copper,
            "pink" => Pink,
            "spring" => Spring,
            "summer" => Summer,
            "autumn" => Autumn,
            "winter" => Winter,
            "lines" => Lines,
            "flag" => Flag,
            "prism" => Prism,
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
