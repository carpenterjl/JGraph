namespace JGraph.Imaging;

/// <summary>The RGB encodings the colour conversions understand.</summary>
public enum RgbColorSpace
{
    /// <summary>sRGB: D65 primaries with the piecewise 2.4 transfer function. The default everywhere.</summary>
    Srgb,

    /// <summary>Adobe RGB (1998): a wider D65 gamut with a plain 2.19921875 gamma.</summary>
    AdobeRgb1998,

    /// <summary>ProPhoto RGB: a very wide D50 gamut with a 1.8 gamma and a linear toe.</summary>
    ProPhotoRgb,

    /// <summary>sRGB primaries with no transfer function — values already proportional to light.</summary>
    LinearRgb,
}

/// <summary>
/// The point-wise colour conversions, all of them working on an <c>n×3</c> block of triples so an
/// image and a colormap take the same path.
/// </summary>
/// <remarks>
/// Every RGB-to-anything route is the same three steps — undo the transfer function, multiply by the
/// space's primary matrix, adapt to the requested white point — so they are written once here rather
/// than once per function. That is also why <c>rgb2lab</c> and <c>rgb2xyz</c> cannot disagree.
/// </remarks>
public static class ColorSpaces
{
    // The Bradford cone response, the matrix nearly every modern chromatic adaptation is built on.
    private static readonly double[,] Bradford =
    {
        { 0.8951, 0.2664, -0.1614 },
        { -0.7502, 1.7135, 0.0367 },
        { 0.0389, -0.0685, 1.0296 },
    };

    // Hunt–Pointer–Estévez, normalized to D65 — the cone space von Kries adaptation uses.
    private static readonly double[,] VonKries =
    {
        { 0.40024, 0.70760, -0.08081 },
        { -0.22630, 1.16532, 0.04570 },
        { 0.0, 0.0, 0.91822 },
    };

    /// <summary>The CIE XYZ tristimulus of a named standard illuminant (MATLAB <c>whitepoint</c>).</summary>
    /// <exception cref="ArgumentException">The name is not one of the standard illuminants.</exception>
    public static double[] WhitePoint(string name) => (name ?? string.Empty).ToLowerInvariant() switch
    {
        "a" => [1.0985, 1.0000, 0.3558],
        "c" => [0.9807, 1.0000, 1.1822],
        "e" => [1.0000, 1.0000, 1.0000],
        "d50" => [0.9642, 1.0000, 0.8249],
        "d55" => [0.9568, 1.0000, 0.9214],
        "d65" => [0.9504, 1.0000, 1.0888],
        "icc" => [31595 / 32768.0, 1.0, 27030 / 32768.0],
        _ => throw new ArgumentException(
            $"'{name}' is not a standard illuminant (use 'a', 'c', 'e', 'd50', 'd55', 'd65', or 'icc')"),
    };

    /// <summary>The white point an RGB space is defined against.</summary>
    public static double[] NativeWhitePoint(RgbColorSpace space) =>
        space == RgbColorSpace.ProPhotoRgb ? WhitePoint("d50") : WhitePoint("d65");

    /// <summary>Undoes an RGB space's transfer function, giving values proportional to light.</summary>
    public static double[,] RgbToLinear(double[,] rgb, RgbColorSpace space) =>
        Map(rgb, v => Decode(v, space));

    /// <summary>Applies an RGB space's transfer function to linear values.</summary>
    public static double[,] LinearToRgb(double[,] linear, RgbColorSpace space) =>
        Map(linear, v => Encode(v, space));

    /// <summary>Converts RGB to CIE 1931 XYZ under the given white point.</summary>
    public static double[,] RgbToXyz(double[,] rgb, RgbColorSpace space, double[] whitePoint) =>
        Transform(RgbToLinear(rgb, space), ToXyzMatrix(space, whitePoint));

    /// <summary>Converts CIE 1931 XYZ under the given white point back to RGB.</summary>
    public static double[,] XyzToRgb(double[,] xyz, RgbColorSpace space, double[] whitePoint) =>
        LinearToRgb(Transform(xyz, Invert3(ToXyzMatrix(space, whitePoint))), space);

    /// <summary>
    /// The primary matrix, adapted from the space's own white point to the one asked for. Asking
    /// sRGB — a D65 space — for XYZ under D50 is a legitimate request, and answering it without the
    /// adaptation step would put the white somewhere it is not.
    /// </summary>
    private static double[,] ToXyzMatrix(RgbColorSpace space, double[] whitePoint) =>
        Multiply(Adaptation(NativeWhitePoint(space), whitePoint, Bradford), Primaries(space));

    /// <summary>Converts CIE XYZ to CIE L*a*b*.</summary>
    public static double[,] XyzToLab(double[,] xyz, double[] whitePoint)
    {
        ArgumentNullException.ThrowIfNull(xyz);
        ArgumentNullException.ThrowIfNull(whitePoint);
        int n = xyz.GetLength(0);
        var lab = new double[n, 3];
        for (int i = 0; i < n; i++)
        {
            double fx = LabF(xyz[i, 0] / whitePoint[0]);
            double fy = LabF(xyz[i, 1] / whitePoint[1]);
            double fz = LabF(xyz[i, 2] / whitePoint[2]);
            lab[i, 0] = (116.0 * fy) - 16.0;
            lab[i, 1] = 500.0 * (fx - fy);
            lab[i, 2] = 200.0 * (fy - fz);
        }

        return lab;
    }

    /// <summary>Converts CIE L*a*b* back to CIE XYZ.</summary>
    public static double[,] LabToXyz(double[,] lab, double[] whitePoint)
    {
        ArgumentNullException.ThrowIfNull(lab);
        ArgumentNullException.ThrowIfNull(whitePoint);
        int n = lab.GetLength(0);
        var xyz = new double[n, 3];
        for (int i = 0; i < n; i++)
        {
            double fy = (lab[i, 0] + 16.0) / 116.0;
            double fx = fy + (lab[i, 1] / 500.0);
            double fz = fy - (lab[i, 2] / 200.0);
            xyz[i, 0] = LabInverseF(fx) * whitePoint[0];
            xyz[i, 1] = LabInverseF(fy) * whitePoint[1];
            xyz[i, 2] = LabInverseF(fz) * whitePoint[2];
        }

        return xyz;
    }

    /// <summary>Converts RGB straight to CIE L*a*b*.</summary>
    public static double[,] RgbToLab(double[,] rgb, RgbColorSpace space, double[] whitePoint) =>
        XyzToLab(RgbToXyz(rgb, space, whitePoint), whitePoint);

    /// <summary>Converts CIE L*a*b* straight to RGB.</summary>
    public static double[,] LabToRgb(double[,] lab, RgbColorSpace space, double[] whitePoint) =>
        XyzToRgb(LabToXyz(lab, whitePoint), space, whitePoint);

    /// <summary>Converts RGB in [0, 1] to hue, saturation and value, all in [0, 1].</summary>
    public static double[,] RgbToHsv(double[,] rgb)
    {
        ArgumentNullException.ThrowIfNull(rgb);
        int n = rgb.GetLength(0);
        var hsv = new double[n, 3];
        for (int i = 0; i < n; i++)
        {
            double r = rgb[i, 0];
            double g = rgb[i, 1];
            double b = rgb[i, 2];
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double span = max - min;

            double hue = 0.0;
            if (span > 0)
            {
                if (max == r)
                {
                    hue = (g - b) / span;
                }
                else if (max == g)
                {
                    hue = 2.0 + ((b - r) / span);
                }
                else
                {
                    hue = 4.0 + ((r - g) / span);
                }

                hue /= 6.0;
                if (hue < 0)
                {
                    hue += 1.0;
                }
            }

            hsv[i, 0] = hue;
            hsv[i, 1] = max <= 0 ? 0.0 : span / max;
            hsv[i, 2] = max;
        }

        return hsv;
    }

    /// <summary>Converts hue, saturation and value back to RGB.</summary>
    public static double[,] HsvToRgb(double[,] hsv)
    {
        ArgumentNullException.ThrowIfNull(hsv);
        int n = hsv.GetLength(0);
        var rgb = new double[n, 3];
        for (int i = 0; i < n; i++)
        {
            double h = (hsv[i, 0] - Math.Floor(hsv[i, 0])) * 6.0;
            double s = Math.Clamp(hsv[i, 1], 0.0, 1.0);
            double v = hsv[i, 2];

            int sector = (int)Math.Floor(h);
            double f = h - sector;
            double p = v * (1.0 - s);
            double q = v * (1.0 - (s * f));
            double t = v * (1.0 - (s * (1.0 - f)));

            (double r, double g, double b) = (sector % 6) switch
            {
                0 => (v, t, p),
                1 => (q, v, p),
                2 => (p, v, t),
                3 => (p, q, v),
                4 => (t, p, v),
                _ => (v, p, q),
            };

            rgb[i, 0] = r;
            rgb[i, 1] = g;
            rgb[i, 2] = b;
        }

        return rgb;
    }

    /// <summary>
    /// Converts RGB to the studio-swing Y′CbCr of ITU-R BT.601, expressed in [0, 1] — so luma runs
    /// 16/255 to 235/255 and the chroma channels sit either side of 128/255, exactly as MATLAB's
    /// double-precision <c>rgb2ycbcr</c> does.
    /// </summary>
    public static double[,] RgbToYCbCr(double[,] rgb)
    {
        ArgumentNullException.ThrowIfNull(rgb);
        int n = rgb.GetLength(0);
        var ycbcr = new double[n, 3];
        for (int i = 0; i < n; i++)
        {
            double r = rgb[i, 0];
            double g = rgb[i, 1];
            double b = rgb[i, 2];
            ycbcr[i, 0] = (16.0 + (65.481 * r) + (128.553 * g) + (24.966 * b)) / 255.0;
            ycbcr[i, 1] = (128.0 - (37.797 * r) - (74.203 * g) + (112.0 * b)) / 255.0;
            ycbcr[i, 2] = (128.0 + (112.0 * r) - (93.786 * g) - (18.214 * b)) / 255.0;
        }

        return ycbcr;
    }

    /// <summary>Converts studio-swing Y′CbCr in [0, 1] back to RGB.</summary>
    public static double[,] YCbCrToRgb(double[,] ycbcr)
    {
        ArgumentNullException.ThrowIfNull(ycbcr);
        int n = ycbcr.GetLength(0);
        var rgb = new double[n, 3];
        for (int i = 0; i < n; i++)
        {
            double y = (ycbcr[i, 0] * 255.0) - 16.0;
            double cb = (ycbcr[i, 1] * 255.0) - 128.0;
            double cr = (ycbcr[i, 2] * 255.0) - 128.0;
            rgb[i, 0] = (0.00456621 * y) + (0.00625893 * cr);
            rgb[i, 1] = (0.00456621 * y) - (0.00153632 * cb) - (0.00318811 * cr);
            rgb[i, 2] = (0.00456621 * y) + (0.00791071 * cb);
        }

        return rgb;
    }

    /// <summary>Converts RGB to the NTSC luminance/chrominance triple (YIQ).</summary>
    public static double[,] RgbToNtsc(double[,] rgb) => Transform(rgb, NtscMatrix);

    /// <summary>Converts an NTSC YIQ triple back to RGB.</summary>
    public static double[,] NtscToRgb(double[,] yiq) => Transform(yiq, Invert3(NtscMatrix));

    /// <summary>The L* channel alone — perceptual lightness without the colour.</summary>
    public static double[] Lightness(double[,] rgb, RgbColorSpace space, double[] whitePoint)
    {
        double[,] lab = RgbToLab(rgb, space, whitePoint);
        var lightness = new double[lab.GetLength(0)];
        for (int i = 0; i < lightness.Length; i++)
        {
            lightness[i] = lab[i, 0];
        }

        return lightness;
    }

    /// <summary>
    /// The angle in degrees between two RGB triples read as vectors — a measure of hue difference
    /// that ignores how bright either one is.
    /// </summary>
    public static double ColorAngle(double[] a, double[] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        double dot = 0;
        double na = 0;
        double nb = 0;
        for (int i = 0; i < 3; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }

        double denominator = Math.Sqrt(na) * Math.Sqrt(nb);
        return denominator <= 0 ? 0.0 : Math.Acos(Math.Clamp(dot / denominator, -1.0, 1.0)) * 180.0 / Math.PI;
    }

    /// <summary>CIE76 colour difference: plain Euclidean distance in L*a*b*.</summary>
    public static double[] DeltaE76(double[,] lab1, double[,] lab2)
    {
        ArgumentNullException.ThrowIfNull(lab1);
        ArgumentNullException.ThrowIfNull(lab2);
        int n = lab1.GetLength(0);
        var difference = new double[n];
        for (int i = 0; i < n; i++)
        {
            double dl = lab1[i, 0] - lab2[i, 0];
            double da = lab1[i, 1] - lab2[i, 1];
            double db = lab1[i, 2] - lab2[i, 2];
            difference[i] = Math.Sqrt((dl * dl) + (da * da) + (db * db));
        }

        return difference;
    }

    /// <summary>CIE94 colour difference, with the graphic-arts weights as defaults.</summary>
    public static double[] DeltaE94(double[,] lab1, double[,] lab2, double kL, double k1, double k2)
    {
        int n = lab1.GetLength(0);
        var difference = new double[n];
        for (int i = 0; i < n; i++)
        {
            double c1 = Math.Sqrt((lab1[i, 1] * lab1[i, 1]) + (lab1[i, 2] * lab1[i, 2]));
            double c2 = Math.Sqrt((lab2[i, 1] * lab2[i, 1]) + (lab2[i, 2] * lab2[i, 2]));
            double dl = lab1[i, 0] - lab2[i, 0];
            double dc = c1 - c2;
            double da = lab1[i, 1] - lab2[i, 1];
            double db = lab1[i, 2] - lab2[i, 2];

            // ΔH is what is left of the chromatic difference once the chroma part is taken out; the
            // subtraction can go a hair negative on identical colours, hence the floor.
            double dh2 = Math.Max(0.0, (da * da) + (db * db) - (dc * dc));
            double sc = 1.0 + (k1 * c1);
            double sh = 1.0 + (k2 * c1);
            double termL = dl / kL;
            double termC = dc / sc;
            difference[i] = Math.Sqrt((termL * termL) + (termC * termC) + (dh2 / (sh * sh)));
        }

        return difference;
    }

    /// <summary>CIEDE2000 colour difference, the current standard.</summary>
    public static double[] DeltaE2000(double[,] lab1, double[,] lab2, double kL, double kC, double kH)
    {
        int n = lab1.GetLength(0);
        var difference = new double[n];
        const double Pow25To7 = 6103515625.0; // 25^7

        for (int i = 0; i < n; i++)
        {
            double l1 = lab1[i, 0];
            double a1 = lab1[i, 1];
            double b1 = lab1[i, 2];
            double l2 = lab2[i, 0];
            double a2 = lab2[i, 1];
            double b2 = lab2[i, 2];

            double c1 = Math.Sqrt((a1 * a1) + (b1 * b1));
            double c2 = Math.Sqrt((a2 * a2) + (b2 * b2));
            double meanC = (c1 + c2) / 2.0;
            double meanC7 = Math.Pow(meanC, 7);
            double g = 0.5 * (1.0 - Math.Sqrt(meanC7 / (meanC7 + Pow25To7)));

            double ap1 = (1.0 + g) * a1;
            double ap2 = (1.0 + g) * a2;
            double cp1 = Math.Sqrt((ap1 * ap1) + (b1 * b1));
            double cp2 = Math.Sqrt((ap2 * ap2) + (b2 * b2));
            double hp1 = Hue(b1, ap1);
            double hp2 = Hue(b2, ap2);

            double dl = l2 - l1;
            double dc = cp2 - cp1;
            double dhp;
            if (cp1 * cp2 == 0)
            {
                dhp = 0;
            }
            else if (Math.Abs(hp2 - hp1) <= 180)
            {
                dhp = hp2 - hp1;
            }
            else
            {
                dhp = hp2 > hp1 ? hp2 - hp1 - 360 : hp2 - hp1 + 360;
            }

            double dh = 2.0 * Math.Sqrt(cp1 * cp2) * Sin(dhp / 2.0);

            double meanL = (l1 + l2) / 2.0;
            double meanCp = (cp1 + cp2) / 2.0;
            double meanHp;
            if (cp1 * cp2 == 0)
            {
                meanHp = hp1 + hp2;
            }
            else if (Math.Abs(hp1 - hp2) <= 180)
            {
                meanHp = (hp1 + hp2) / 2.0;
            }
            else
            {
                meanHp = hp1 + hp2 < 360 ? (hp1 + hp2 + 360) / 2.0 : (hp1 + hp2 - 360) / 2.0;
            }

            double t = 1.0
                       - (0.17 * Cos(meanHp - 30))
                       + (0.24 * Cos(2 * meanHp))
                       + (0.32 * Cos((3 * meanHp) + 6))
                       - (0.20 * Cos((4 * meanHp) - 63));

            double meanCp7 = Math.Pow(meanCp, 7);
            double rc = 2.0 * Math.Sqrt(meanCp7 / (meanCp7 + Pow25To7));
            double sl = 1.0 + (0.015 * (meanL - 50) * (meanL - 50) / Math.Sqrt(20 + ((meanL - 50) * (meanL - 50))));
            double sc = 1.0 + (0.045 * meanCp);
            double sh = 1.0 + (0.015 * meanCp * t);
            double rt = -Sin(2.0 * 30.0 * Math.Exp(-Square((meanHp - 275) / 25.0))) * rc;

            double termL = dl / (kL * sl);
            double termC = dc / (kC * sc);
            double termH = dh / (kH * sh);
            difference[i] = Math.Sqrt(
                (termL * termL) + (termC * termC) + (termH * termH) + (rt * termC * termH));
        }

        return difference;
    }

    /// <summary>The 3×3 that carries an RGB space's linear values into XYZ at its own white point.</summary>
    /// <remarks>
    /// The published matrices are rounded to seven places, which leaves white landing a fraction off
    /// its own white point — enough that <c>rgb2lab</c> of pure white came back as b = −0.016 rather
    /// than zero. Rescaling the columns so that <c>[1 1 1]</c> maps exactly onto the white point is
    /// how the matrix is derived in the first place, and it puts white back where it belongs.
    /// </remarks>
    internal static double[,] Primaries(RgbColorSpace space)
    {
        double[,] published = PublishedPrimaries(space);
        double[] gains = Apply(Invert3(published), NativeWhitePoint(space));
        var scaled = new double[3, 3];
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                scaled[r, c] = published[r, c] * gains[c];
            }
        }

        return scaled;
    }

    private static double[,] PublishedPrimaries(RgbColorSpace space) => space switch
    {
        RgbColorSpace.AdobeRgb1998 => new[,]
        {
            { 0.5767309, 0.1855540, 0.1881852 },
            { 0.2973769, 0.6273491, 0.0752741 },
            { 0.0270343, 0.0706872, 0.9911085 },
        },
        RgbColorSpace.ProPhotoRgb => new[,]
        {
            { 0.7976749, 0.1351917, 0.0313534 },
            { 0.2880402, 0.7118741, 0.0000857 },
            { 0.0000000, 0.0000000, 0.8252100 },
        },
        _ => new[,]
        {
            { 0.4123908, 0.3575843, 0.1804808 },
            { 0.2126390, 0.7151687, 0.0721923 },
            { 0.0193308, 0.1191948, 0.9505322 },
        },
    };

    /// <summary>
    /// The chromatic adaptation that carries XYZ measured under <paramref name="from"/> to the same
    /// colour seen under <paramref name="to"/>, through the given cone response.
    /// </summary>
    internal static double[,] Adaptation(double[] from, double[] to, double[,] cone)
    {
        if (Same(from, to))
        {
            return new double[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        }

        double[] source = Apply(cone, from);
        double[] destination = Apply(cone, to);
        var gains = new double[,]
        {
            { destination[0] / source[0], 0, 0 },
            { 0, destination[1] / source[1], 0 },
            { 0, 0, destination[2] / source[2] },
        };

        return Multiply(Invert3(cone), Multiply(gains, cone));
    }

    /// <summary>The cone response matrix a named adaptation method uses.</summary>
    internal static double[,] ConeResponse(bool bradford) => bradford ? Bradford : VonKries;

    /// <summary>Multiplies every triple by a 3×3 matrix, treating each triple as a column vector.</summary>
    internal static double[,] Transform(double[,] triples, double[,] matrix)
    {
        int n = triples.GetLength(0);
        var result = new double[n, 3];
        for (int i = 0; i < n; i++)
        {
            for (int r = 0; r < 3; r++)
            {
                result[i, r] = (matrix[r, 0] * triples[i, 0])
                               + (matrix[r, 1] * triples[i, 1])
                               + (matrix[r, 2] * triples[i, 2]);
            }
        }

        return result;
    }

    /// <summary>Inverts a 3×3 matrix.</summary>
    internal static double[,] Invert3(double[,] m)
    {
        double det = (m[0, 0] * ((m[1, 1] * m[2, 2]) - (m[1, 2] * m[2, 1])))
                     - (m[0, 1] * ((m[1, 0] * m[2, 2]) - (m[1, 2] * m[2, 0])))
                     + (m[0, 2] * ((m[1, 0] * m[2, 1]) - (m[1, 1] * m[2, 0])));
        if (Math.Abs(det) < 1e-15)
        {
            throw new ArgumentException("the colour matrix is singular");
        }

        return new double[,]
        {
            {
                ((m[1, 1] * m[2, 2]) - (m[1, 2] * m[2, 1])) / det,
                ((m[0, 2] * m[2, 1]) - (m[0, 1] * m[2, 2])) / det,
                ((m[0, 1] * m[1, 2]) - (m[0, 2] * m[1, 1])) / det
            },
            {
                ((m[1, 2] * m[2, 0]) - (m[1, 0] * m[2, 2])) / det,
                ((m[0, 0] * m[2, 2]) - (m[0, 2] * m[2, 0])) / det,
                ((m[0, 2] * m[1, 0]) - (m[0, 0] * m[1, 2])) / det
            },
            {
                ((m[1, 0] * m[2, 1]) - (m[1, 1] * m[2, 0])) / det,
                ((m[0, 1] * m[2, 0]) - (m[0, 0] * m[2, 1])) / det,
                ((m[0, 0] * m[1, 1]) - (m[0, 1] * m[1, 0])) / det
            },
        };
    }

    private static readonly double[,] NtscMatrix =
    {
        { 0.299, 0.587, 0.114 },
        { 0.596, -0.274, -0.322 },
        { 0.211, -0.523, 0.312 },
    };

    private static double[] Apply(double[,] m, double[] v) =>
    [
        (m[0, 0] * v[0]) + (m[0, 1] * v[1]) + (m[0, 2] * v[2]),
        (m[1, 0] * v[0]) + (m[1, 1] * v[1]) + (m[1, 2] * v[2]),
        (m[2, 0] * v[0]) + (m[2, 1] * v[1]) + (m[2, 2] * v[2]),
    ];

    /// <summary>The 3×3 product <c>a·b</c>.</summary>
    internal static double[,] Multiply(double[,] a, double[,] b)
    {
        var result = new double[3, 3];
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                result[r, c] = (a[r, 0] * b[0, c]) + (a[r, 1] * b[1, c]) + (a[r, 2] * b[2, c]);
            }
        }

        return result;
    }

    private static bool Same(double[] a, double[] b) =>
        Math.Abs(a[0] - b[0]) < 1e-9 && Math.Abs(a[1] - b[1]) < 1e-9 && Math.Abs(a[2] - b[2]) < 1e-9;

    private static double[,] Map(double[,] triples, Func<double, double> f)
    {
        ArgumentNullException.ThrowIfNull(triples);
        int n = triples.GetLength(0);
        var result = new double[n, 3];
        for (int i = 0; i < n; i++)
        {
            for (int c = 0; c < 3; c++)
            {
                result[i, c] = f(triples[i, c]);
            }
        }

        return result;
    }

    private static double Decode(double v, RgbColorSpace space) => space switch
    {
        RgbColorSpace.LinearRgb => v,
        RgbColorSpace.AdobeRgb1998 => Signed(v, x => Math.Pow(x, 563.0 / 256.0)),
        RgbColorSpace.ProPhotoRgb => Signed(v, x => x < 1.0 / 32.0 ? x / 16.0 : Math.Pow(x, 1.8)),
        _ => Signed(v, x => x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4)),
    };

    private static double Encode(double v, RgbColorSpace space) => space switch
    {
        RgbColorSpace.LinearRgb => v,
        RgbColorSpace.AdobeRgb1998 => Signed(v, x => Math.Pow(x, 256.0 / 563.0)),
        RgbColorSpace.ProPhotoRgb => Signed(v, x => x < 1.0 / 512.0 ? x * 16.0 : Math.Pow(x, 1.0 / 1.8)),
        _ => Signed(v, x => x <= 0.0031308 ? x * 12.92 : (1.055 * Math.Pow(x, 1.0 / 2.4)) - 0.055),
    };

    /// <summary>
    /// Applies a transfer function about zero. Out-of-gamut colours legitimately go negative on the
    /// way back from Lab, and raising a negative number to a fractional power is NaN — mirroring the
    /// curve keeps those values finite so a round trip still lands where it started.
    /// </summary>
    private static double Signed(double v, Func<double, double> f) => v < 0 ? -f(-v) : f(v);

    private static double LabF(double t) =>
        t > 216.0 / 24389.0 ? Math.Cbrt(t) : ((841.0 / 108.0) * t) + (4.0 / 29.0);

    private static double LabInverseF(double t) =>
        t > 6.0 / 29.0 ? t * t * t : (t - (4.0 / 29.0)) * 108.0 / 841.0;

    private static double Hue(double b, double a)
    {
        if (a == 0 && b == 0)
        {
            return 0;
        }

        double degrees = Math.Atan2(b, a) * 180.0 / Math.PI;
        return degrees < 0 ? degrees + 360.0 : degrees;
    }

    private static double Sin(double degrees) => Math.Sin(degrees * Math.PI / 180.0);

    private static double Cos(double degrees) => Math.Cos(degrees * Math.PI / 180.0);

    private static double Square(double x) => x * x;
}
