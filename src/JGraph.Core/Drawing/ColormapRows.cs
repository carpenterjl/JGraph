namespace JGraph.Core.Drawing;

/// <summary>
/// MATLAB's colormap generators as R2025b computes them: <c>jet(m)</c>, <c>hot(m)</c> and the rest,
/// each an m-by-3 table of doubles built by the arithmetic of its own source file (toolbox\matlab\
/// graphics\graphics\color), so that a script reading <c>jet(8)</c> gets R2025b's eight rows to the
/// last bit and App Designer, which names a map by <c>isequal</c> of its rows, recognises
/// <c>hot(256)</c> as hot.
/// </summary>
/// <remarks>
/// <para>
/// This is the table side of a colormap. <see cref="Colormap"/> is the drawing side: a gradient of
/// stops sampled continuously, which a renderer wants and which a table resampled to m rows is not.
/// <c>jet(8)</c> is not jet's 256 rows resampled to eight; it is jet.m's formula over eight rows,
/// whose rows sit on different breakpoints (open item 145).
/// </para>
/// <para>
/// The maps without a formula (<c>parula</c>, <c>turbo</c>, <c>nebula</c>) are R2025b's 256-row tables
/// read with <c>interp1(1:P, values, linspace(1, P, m), 'linear')</c>, the interpolation spelled
/// <c>(1 - t) * a + t * b</c> as griddedInterpolant computes it (the other spelling, <c>a + t * (b - a)</c>,
/// is off by an ulp in a few hundred places). <c>sky</c> and <c>abyss</c> are a built-in in R2025b
/// (<c>blueColormapValues</c>); their rows are a lightness ramp over one blue, found to the bit by
/// comparing candidate spellings with its output at many sizes.
/// </para>
/// </remarks>
public static class ColormapRows
{
    /// <summary>The generators R2025b ships, in App Designer's order (ColormapEditor.PREDEFINED_COLORMAPS).</summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        "parula", "jet", "hsv", "hot", "cool", "spring", "summer", "autumn", "winter", "gray",
        "bone", "copper", "pink", "lines", "colorcube", "prism", "flag", "white", "turbo", "sky",
        "abyss", "nebula",
    ];

    /// <summary>
    /// R2025b's default colour order (the palette it calls gem), which its <c>lines</c> repeats. JGraph
    /// draws with the classic order (<see cref="ClassicLineColors"/>, open item 105); this one is what
    /// a file written for R2025b to read has to hold.
    /// </summary>
    public static IReadOnlyList<double[]> MatlabLineColors { get; } =
    [
        [0.066, 0.443, 0.745],
        [0.866, 0.329, 0],
        [0.929, 0.694, 0.125],
        [0.521, 0.086, 0.819],
        [0.231, 0.666, 0.196],
        [0.184, 0.745, 0.937],
        [0.819, 0.015, 0.545],
    ];

    /// <summary>The classic default colour order (R2014b to R2024b), the one JGraph draws series with.</summary>
    public static IReadOnlyList<double[]> ClassicLineColors { get; } =
    [
        [0, 0.447, 0.741],
        [0.85, 0.325, 0.098],
        [0.929, 0.694, 0.125],
        [0.494, 0.184, 0.556],
        [0.466, 0.674, 0.188],
        [0.301, 0.745, 0.933],
        [0.635, 0.078, 0.184],
    ];

    /// <summary>Whether <paramref name="name"/> is one of R2025b's generators (case-insensitive).</summary>
    public static bool IsGenerator(string? name) =>
        name is not null && Names.Contains(name.Trim().ToLowerInvariant());

    /// <summary>
    /// The generator's <paramref name="m"/> rows, or null when <paramref name="name"/> is not one.
    /// <c>lines</c> repeats <paramref name="lineColors"/>, JGraph's own colour order when none is given.
    /// </summary>
    public static double[,]? TryOf(string? name, int m, IReadOnlyList<double[]>? lineColors = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(m);
        return name?.Trim().ToLowerInvariant() switch
        {
            "parula" => Interpolated(ColormapTables.Parula, m),
            "turbo" => Interpolated(ColormapTables.Turbo, m),
            "nebula" => Interpolated(ColormapTables.Nebula, m),
            "jet" => Jet(m),
            "hsv" => Hsv(m),
            "hot" => Hot(m),
            "cool" => Ramp(m, static r => [r, 1 - r, 1]),
            "spring" => Ramp(m, static r => [1, r, 1 - r]),
            "summer" => Ramp(m, static r => [r, .5 + (r / 2), .4]),
            "autumn" => Ramp(m, static r => [1, r, 0]),
            "winter" => Ramp(m, static r => [0, r, .5 + ((1 - r) / 2)]),
            "gray" => Ramp(m, static r => [r, r, r]),
            "bone" => Bone(m),
            "copper" => Copper(m),
            "pink" => Pink(m),
            "lines" => Repeated(lineColors ?? ClassicLineColors, m),
            "colorcube" => ColorCube(m),
            "prism" => Repeated(PrismColors, m),
            "flag" => Repeated(FlagColors, m),
            "white" => Ramp(m, static _ => [1, 1, 1]),
            "sky" => Sky(m),
            "abyss" => Abyss(m),
            _ => null,
        };
    }

    /// <summary>The generator's <paramref name="m"/> rows; an unknown name is refused.</summary>
    public static double[,] Of(string name, int m, IReadOnlyList<double[]>? lineColors = null) =>
        TryOf(name, m, lineColors) ?? throw new ArgumentException($"'{name}' is not a colormap generator.", nameof(name));

    private static readonly double[][] PrismColors =
    [
        [1, 0, 0], [1, 1.0 / 2, 0], [1, 1, 0], [0, 1, 0], [0, 0, 1], [2.0 / 3, 0, 1],
    ];

    private static readonly double[][] FlagColors = [[1, 0, 0], [1, 1, 1], [0, 0, 1], [0, 0, 0]];

    /// <summary>MATLAB's <c>eps</c>, the spacing of doubles at 1.</summary>
    private const double MachineEpsilon = 2.220446049250313e-16;

    /// <summary>
    /// MATLAB's <c>linspace(d1, d2, n)</c> (elmat\linspace.m, the general branch): <c>d1 + (0:n1).*(d2 - d1)./n1</c>
    /// with both ends put back exactly, and a lone point at <paramref name="d2"/>.
    /// </summary>
    private static double[] Linspace(double d1, double d2, int n)
    {
        var y = new double[n];
        if (n == 0)
        {
            return y;
        }

        if (n == 1)
        {
            y[0] = d2;
            return y;
        }

        int n1 = n - 1;
        for (int k = 0; k < n; k++)
        {
            y[k] = d1 + (k * (d2 - d1) / n1);
        }

        y[0] = d1;
        y[^1] = d2;
        return y;
    }

    /// <summary><c>(0:m-1)'/max(m-1,1)</c>, the ramp every linear map is made of.</summary>
    private static double[] Unit(int m)
    {
        var r = new double[m];
        int over = System.Math.Max(m - 1, 1);
        for (int k = 0; k < m; k++)
        {
            r[k] = k / (double)over;
        }

        return r;
    }

    private static double[,] Ramp(int m, Func<double, double[]> row)
    {
        double[] r = Unit(m);
        var map = new double[m, 3];
        for (int i = 0; i < m; i++)
        {
            double[] values = row(r[i]);
            map[i, 0] = values[0];
            map[i, 1] = values[1];
            map[i, 2] = values[2];
        }

        return map;
    }

    private static double[,] Repeated(IReadOnlyList<double[]> colors, int m)
    {
        var map = new double[m, 3];
        for (int i = 0; i < m; i++)
        {
            double[] c = colors[i % colors.Count];
            map[i, 0] = c[0];
            map[i, 1] = c[1];
            map[i, 2] = c[2];
        }

        return map;
    }

    /// <summary><c>interp1(1:P, values, linspace(1, P, m), 'linear')</c> over a P-row table stored row by row.</summary>
    private static double[,] Interpolated(double[] values, int m)
    {
        int p = values.Length / 3;
        double[] x = Linspace(1, p, m);
        var map = new double[m, 3];
        for (int i = 0; i < m; i++)
        {
            // The interval below the query, the last one for the last row; t is the query's place in it.
            int k = System.Math.Min((int)System.Math.Floor(x[i]), p - 1);
            double t = x[i] - k;
            for (int c = 0; c < 3; c++)
            {
                double a = values[((k - 1) * 3) + c];
                double b = values[(k * 3) + c];
                map[i, c] = ((1 - t) * a) + (t * b);
            }
        }

        return map;
    }

    /// <summary>jet.m: three overlapping ramps of <c>u</c>, placed by index.</summary>
    private static double[,] Jet(int m)
    {
        var map = new double[m, 3];
        if (m == 0)
        {
            return map;
        }

        int n = (m + 3) / 4; // ceil(m/4)
        var u = new double[(3 * n) - 1];
        for (int i = 0; i < n; i++)
        {
            u[i] = (i + 1) / (double)n;
        }

        for (int i = 0; i < n - 1; i++)
        {
            u[n + i] = 1;
        }

        for (int i = 0; i < n; i++)
        {
            u[(2 * n) - 1 + i] = (n - i) / (double)n;
        }

        // g = ceil(n/2) - (mod(m,4)==1) + (1:length(u))', 1-based; r = g + n; b = g - n.
        int first = ((n + 1) / 2) - (m % 4 == 1 ? 1 : 0) + 1;
        var green = new List<int>();
        var red = new List<int>();
        var blue = new List<int>();
        for (int i = 0; i < u.Length; i++)
        {
            int g = first + i;
            if (g <= m)
            {
                green.Add(g);
            }

            if (g + n <= m)
            {
                red.Add(g + n);
            }

            if (g - n >= 1)
            {
                blue.Add(g - n);
            }
        }

        for (int i = 0; i < red.Count; i++)
        {
            map[red[i] - 1, 0] = u[i];
        }

        for (int i = 0; i < green.Count; i++)
        {
            map[green[i] - 1, 1] = u[i];
        }

        int offset = u.Length - blue.Count;
        for (int i = 0; i < blue.Count; i++)
        {
            map[blue[i] - 1, 2] = u[offset + i];
        }

        return map;
    }

    /// <summary>hsv.m: <c>hsv2rgb([h ones(m,2)])</c> with <c>h = (0:m-1)'/max(m,1)</c>, by hsv2rgb.m's sectors.</summary>
    private static double[,] Hsv(int m)
    {
        var map = new double[m, 3];
        double over = System.Math.Max(m, 1);
        double peak = 0;
        for (int i = 0; i < m; i++)
        {
            double h = 6 * (i / over);
            double k = System.Math.Floor(h);
            double p = h - k;
            double n = 1 - p;          // 1 - s.*p
            double q = 1 - (1 - p);    // 1 - (s.*(1 - p))
            (double r, double g, double b) = k switch
            {
                0 or 6 => (1.0, q, 0.0),
                1 => (n, 1.0, 0.0),
                2 => (0.0, 1.0, q),
                3 => (0.0, n, 1.0),
                4 => (q, 0.0, 1.0),
                _ => (1.0, 0.0, n),
            };
            map[i, 0] = r;
            map[i, 1] = g;
            map[i, 2] = b;
            peak = System.Math.Max(peak, System.Math.Max(r, System.Math.Max(g, b)));
        }

        // rout = v./max(rout(:)) .* rout, with v = 1.
        if (peak != 1 && m > 0)
        {
            for (int i = 0; i < m; i++)
            {
                for (int c = 0; c < 3; c++)
                {
                    map[i, c] = 1 / peak * map[i, c];
                }
            }
        }

        return map;
    }

    /// <summary>hot.m: red rises over the first 3/8, green over the next, blue over the rest.</summary>
    private static double[,] Hot(int m)
    {
        var map = new double[m, 3];
        int n = (int)(3.0 / 8 * m);
        for (int i = 0; i < m; i++)
        {
            map[i, 0] = i < n ? (i + 1) / (double)n : 1;
            map[i, 1] = i < n ? 0 : i < 2 * n ? (i - n + 1) / (double)n : 1;
            map[i, 2] = i < 2 * n ? 0 : (i - (2 * n) + 1) / (double)(m - (2 * n));
        }

        return map;
    }

    /// <summary>bone.m: <c>(7*gray(m) + fliplr(hot(m)))/8</c>.</summary>
    private static double[,] Bone(int m)
    {
        double[] g = Unit(m);
        double[,] hot = Hot(m);
        var map = new double[m, 3];
        for (int i = 0; i < m; i++)
        {
            for (int c = 0; c < 3; c++)
            {
                map[i, c] = ((7 * g[i]) + hot[i, 2 - c]) / 8;
            }
        }

        return map;
    }

    /// <summary>copper.m: <c>min(1, gray(m)*diag([1.2500 0.7812 0.4975]))</c>.</summary>
    private static double[,] Copper(int m)
    {
        double[] g = Unit(m);
        double[] scale = [1.25, 0.7812, 0.4975];
        var map = new double[m, 3];
        for (int i = 0; i < m; i++)
        {
            for (int c = 0; c < 3; c++)
            {
                map[i, c] = System.Math.Min(1, g[i] * scale[c]);
            }
        }

        return map;
    }

    /// <summary>pink.m: <c>sqrt((2*gray(m) + hot(m))/3)</c>.</summary>
    private static double[,] Pink(int m)
    {
        double[] g = Unit(m);
        double[,] hot = Hot(m);
        var map = new double[m, 3];
        for (int i = 0; i < m; i++)
        {
            for (int c = 0; c < 3; c++)
            {
                map[i, c] = System.Math.Sqrt(((2 * g[i]) + hot[i, c]) / 3);
            }
        }

        return map;
    }

    /// <summary>
    /// The lightness ramp under <c>sky</c> and <c>abyss</c>: <c>low + (k*(1 - low))/(m - 1)</c>, with
    /// neither end put back and the top held to 1, and a lone row at 1. Spelled so because it matches
    /// R2025b's built-in at every size probed (1 to 257), where linspace's put-back ends miss at m = 10
    /// and the unheld ramp misses at 27 and 100.
    /// </summary>
    private static double[] LightnessRamp(double low, int m)
    {
        var f = new double[m];
        if (m == 1)
        {
            f[0] = 1;
            return f;
        }

        for (int k = 0; k < m; k++)
        {
            f[k] = System.Math.Min(1, low + (k * (1 - low) / (m - 1)));
        }

        return f;
    }

    /// <summary>R2025b's sky: <c>f.*c + (1 - f)</c> over its blue <c>c</c>, <c>f</c> rising from 0.1 to 1.</summary>
    private static double[,] Sky(int m)
    {
        double[] blue = [0.066, 0.443, 0.745];
        double[] f = LightnessRamp(0.1, m);
        var map = new double[m, 3];
        for (int i = 0; i < m; i++)
        {
            for (int c = 0; c < 3; c++)
            {
                map[i, c] = (f[i] * blue[c]) + (1 - f[i]);
            }
        }

        return map;
    }

    /// <summary>R2025b's abyss: its blue <c>c</c> scaled by a ramp rising from 0.25 to 1.</summary>
    private static double[,] Abyss(int m)
    {
        double[] blue = [0.149, 0.549, 0.866];
        double[] f = LightnessRamp(0.25, m);
        var map = new double[m, 3];
        for (int i = 0; i < m; i++)
        {
            for (int c = 0; c < 3; c++)
            {
                map[i, c] = blue[c] * f[i];
            }
        }

        return map;
    }

    /// <summary>
    /// colorcube.m: the RGB cube on as many steps as fit (blue one fewer when the cube fills the map
    /// exactly), less its grey line and its pure axes, then a red, a green and a blue ramp, black, and
    /// a grey ramp. Fewer than eight rows is <c>gray(m)</c>.
    /// </summary>
    private static double[,] ColorCube(int m)
    {
        if (m < 8)
        {
            return Ramp(m, static r => [r, r, r]);
        }

        int rgSteps = (int)(System.Math.Pow(m, 1.0 / 3) + MachineEpsilon);
        int extra = m - (rgSteps * rgSteps * rgSteps);
        int bSteps = extra == 0 && rgSteps > 2 ? rgSteps - 1 : rgSteps;
        double[] rgFull = Colon(0, 1.0 / (rgSteps - 1));
        double[] bFull = Colon(0, 1.0 / (bSteps - 1));

        // meshgrid(x, y, z) read down its columns: y fastest, then x, then z.
        var rows = new List<double[]>();
        foreach (double b in bFull)
        {
            foreach (double r in rgFull)
            {
                foreach (double g in rgFull)
                {
                    bool grey = System.Math.Abs(g - r) + System.Math.Abs(b - g) == 0;
                    bool axis = r + g == 0 || g + b == 0 || r + b == 0;
                    if (!grey && !axis)
                    {
                        rows.Add([r, g, b]);
                    }
                }
            }
        }

        int remaining = m - rows.Count - 1;
        int rgbSteps = (int)System.Math.Floor(remaining / 4.0);
        int kSteps = remaining - (3 * rgbSteps);
        double rgbStep = 1.0 / rgbSteps;
        double kStep = 1.0 / kSteps;
        double[] rgbRamp = Colon(rgbStep, rgbStep);
        double[] kRamp = Colon(kStep, kStep);

        foreach (double v in rgbRamp)
        {
            rows.Add([v, 0, 0]);
        }

        foreach (double v in rgbRamp)
        {
            rows.Add([0, v, 0]);
        }

        foreach (double v in rgbRamp)
        {
            rows.Add([0, 0, v]);
        }

        rows.Add([0, 0, 0]);
        foreach (double v in kRamp)
        {
            rows.Add([v, v, v]);
        }

        var map = new double[rows.Count, 3];
        for (int i = 0; i < rows.Count; i++)
        {
            map[i, 0] = rows[i][0];
            map[i, 1] = rows[i][1];
            map[i, 2] = rows[i][2];
        }

        return map;
    }

    /// <summary>
    /// MATLAB's <c>a:d:1</c> for a positive step: the count by colon's tolerance, the first half
    /// counted up from <paramref name="a"/> and the second half down from the end it reaches, which is
    /// snapped to 1 when within the tolerance. An infinite step is empty.
    /// </summary>
    private static double[] Colon(double a, double d)
    {
        const double B = 1;
        if (!double.IsFinite(d) || d <= 0 || a > B)
        {
            return [];
        }

        double tol = 2.0 * MachineEpsilon * System.Math.Max(System.Math.Abs(a), System.Math.Abs(B));
        int n = (int)System.Math.Floor(((B - a) / d) + (tol / d));
        double c = a + (n * d);
        if (System.Math.Abs(c - B) < tol)
        {
            c = B;
        }

        // The middle element of an even count is counted down from the end (colorcube(10)'s 2/3 is
        // 1 - 1/3 there, not 1/3 + 1/3).
        var v = new double[n + 1];
        for (int k = 0; k <= n; k++)
        {
            v[k] = 2 * k < n ? a + (k * d) : c - ((n - k) * d);
        }

        return v;
    }
}
