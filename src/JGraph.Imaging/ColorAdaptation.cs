namespace JGraph.Imaging;

/// <summary>How <see cref="ColorAdaptation.Adapt"/> moves colours from one illuminant to another.</summary>
public enum AdaptationMethod
{
    /// <summary>The Bradford cone response — the sharpened transform most colour tools use.</summary>
    Bradford,

    /// <summary>The Hunt–Pointer–Estévez cone response, the original von Kries model.</summary>
    VonKries,

    /// <summary>A per-channel gain in linear RGB. Cheap, and often good enough for a photograph.</summary>
    Simple,
}

/// <summary>
/// White balance: estimating what light a photograph was taken under, and correcting for it.
/// </summary>
public static class ColorAdaptation
{
    /// <summary>
    /// Rebalances <paramref name="rgb"/> so that <paramref name="illuminant"/> comes out neutral
    /// (MATLAB <c>chromadapt</c>).
    /// </summary>
    /// <remarks>
    /// The illuminant matters only for its direction: it is normalized to unit luminance before the
    /// adaptation matrix is built, so an estimate scaled by any positive constant gives the same
    /// answer. That is what lets <c>illumgray</c>, <c>illumwhite</c> and <c>illumpca</c> disagree
    /// about magnitude without disagreeing about the correction.
    /// </remarks>
    public static double[,] Adapt(
        double[,] rgb, double[] illuminant, RgbColorSpace space, AdaptationMethod method)
    {
        ArgumentNullException.ThrowIfNull(rgb);
        ArgumentNullException.ThrowIfNull(illuminant);
        if (illuminant.Length != 3)
        {
            throw new ArgumentException("an illuminant is a three-element RGB triple", nameof(illuminant));
        }

        double[,] illuminantLinear = ColorSpaces.RgbToLinear(new[,]
        {
            { illuminant[0], illuminant[1], illuminant[2] },
        }, space);

        if (method == AdaptationMethod.Simple)
        {
            // Scale each channel so the illuminant becomes grey, choosing the grey level that keeps
            // the picture's overall brightness where it was rather than clipping it upwards.
            double mean = (illuminantLinear[0, 0] + illuminantLinear[0, 1] + illuminantLinear[0, 2]) / 3.0;
            double[,] linear = ColorSpaces.RgbToLinear(rgb, space);
            int count = linear.GetLength(0);
            var scaled = new double[count, 3];
            for (int i = 0; i < count; i++)
            {
                for (int c = 0; c < 3; c++)
                {
                    double gain = illuminantLinear[0, c] <= 0 ? 1.0 : mean / illuminantLinear[0, c];
                    scaled[i, c] = linear[i, c] * gain;
                }
            }

            return ColorSpaces.LinearToRgb(scaled, space);
        }

        double[] whitePoint = ColorSpaces.NativeWhitePoint(space);
        double[,] sourceXyz = ColorSpaces.RgbToXyz(new[,]
        {
            { illuminant[0], illuminant[1], illuminant[2] },
        }, space, whitePoint);

        double luminance = sourceXyz[0, 1];
        if (luminance <= 0)
        {
            throw new ArgumentException("the illuminant has no luminance to adapt from", nameof(illuminant));
        }

        double[] from = [sourceXyz[0, 0] / luminance, 1.0, sourceXyz[0, 2] / luminance];
        double[,] cone = ColorSpaces.ConeResponse(method == AdaptationMethod.Bradford);
        double[,] adaptation = ColorSpaces.Adaptation(from, whitePoint, cone);

        double[,] xyz = ColorSpaces.RgbToXyz(rgb, space, whitePoint);
        return ColorSpaces.XyzToRgb(ColorSpaces.Transform(xyz, adaptation), space, whitePoint);
    }

    /// <summary>
    /// The grey-world illuminant estimate (MATLAB <c>illumgray</c>): the Minkowski mean of every
    /// channel, over the pixels left after the darkest and brightest <paramref name="bottomPercentile"/>
    /// and <paramref name="topPercentile"/> per cent are set aside.
    /// </summary>
    /// <remarks>
    /// Trimming both tails is what makes it usable: a clipped highlight is the same value in all
    /// three channels whatever the light was, so leaving them in drags every estimate towards grey.
    /// </remarks>
    public static double[] GrayWorld(
        double[,] rgb, double bottomPercentile, double topPercentile, double norm, bool[]? mask = null)
    {
        ArgumentNullException.ThrowIfNull(rgb);
        int n = rgb.GetLength(0);
        var brightness = new List<(double Value, int Index)>(n);
        for (int i = 0; i < n; i++)
        {
            if (mask is null || mask[i])
            {
                brightness.Add((rgb[i, 0] + rgb[i, 1] + rgb[i, 2], i));
            }
        }

        if (brightness.Count == 0)
        {
            throw new ArgumentException("the mask leaves no pixels to estimate from", nameof(mask));
        }

        brightness.Sort(static (a, b) => a.Value.CompareTo(b.Value));
        int first = (int)Math.Floor(brightness.Count * bottomPercentile / 100.0);
        int last = brightness.Count - 1 - (int)Math.Floor(brightness.Count * topPercentile / 100.0);
        if (last < first)
        {
            (first, last) = (0, brightness.Count - 1);
        }

        var sums = new double[3];
        int used = last - first + 1;
        for (int k = first; k <= last; k++)
        {
            int i = brightness[k].Index;
            for (int c = 0; c < 3; c++)
            {
                sums[c] += Math.Pow(Math.Abs(rgb[i, c]), norm);
            }
        }

        var estimate = new double[3];
        for (int c = 0; c < 3; c++)
        {
            estimate[c] = Math.Pow(sums[c] / used, 1.0 / norm);
        }

        return Normalize(estimate);
    }

    /// <summary>
    /// The white-patch illuminant estimate (MATLAB <c>illumwhite</c>): the mean of the brightest
    /// <paramref name="topPercentile"/> per cent of pixels, on the assumption that the brightest thing
    /// in the frame is a white thing.
    /// </summary>
    public static double[] WhitePatch(double[,] rgb, double topPercentile, bool[]? mask = null)
    {
        ArgumentNullException.ThrowIfNull(rgb);
        int n = rgb.GetLength(0);
        var brightness = new List<(double Value, int Index)>(n);
        for (int i = 0; i < n; i++)
        {
            if (mask is null || mask[i])
            {
                brightness.Add((rgb[i, 0] + rgb[i, 1] + rgb[i, 2], i));
            }
        }

        if (brightness.Count == 0)
        {
            throw new ArgumentException("the mask leaves no pixels to estimate from", nameof(mask));
        }

        brightness.Sort(static (a, b) => b.Value.CompareTo(a.Value));
        int take = Math.Max(1, (int)Math.Round(brightness.Count * topPercentile / 100.0));
        var estimate = new double[3];
        for (int k = 0; k < take; k++)
        {
            int i = brightness[k].Index;
            for (int c = 0; c < 3; c++)
            {
                estimate[c] += rgb[i, c];
            }
        }

        for (int c = 0; c < 3; c++)
        {
            estimate[c] /= take;
        }

        return Normalize(estimate);
    }

    /// <summary>
    /// The principal-component illuminant estimate (MATLAB <c>illumpca</c>): keep the
    /// <paramref name="percentage"/> per cent of pixels whose colour points furthest from the average
    /// direction, and take the leading eigenvector of what is left.
    /// </summary>
    /// <remarks>
    /// The idea is Cheng, Prasad and Brown's: the pixels that carry information about the light are
    /// the ones that are strongly coloured, not the ones near the mean, so the estimate is built from
    /// the outliers rather than from everything. The eigenvector comes from power iteration on the
    /// 3×3 scatter matrix, which converges in a handful of steps at this size.
    /// </remarks>
    public static double[] PrincipalComponent(double[,] rgb, double percentage, bool[]? mask = null)
    {
        ArgumentNullException.ThrowIfNull(rgb);
        int n = rgb.GetLength(0);
        var directions = new List<double[]>(n);
        var mean = new double[3];
        for (int i = 0; i < n; i++)
        {
            if (mask is not null && !mask[i])
            {
                continue;
            }

            double length = Math.Sqrt((rgb[i, 0] * rgb[i, 0]) + (rgb[i, 1] * rgb[i, 1]) + (rgb[i, 2] * rgb[i, 2]));
            if (length <= 0)
            {
                continue;
            }

            double[] unit = [rgb[i, 0] / length, rgb[i, 1] / length, rgb[i, 2] / length];
            directions.Add(unit);
            for (int c = 0; c < 3; c++)
            {
                mean[c] += unit[c];
            }
        }

        if (directions.Count == 0)
        {
            throw new ArgumentException("there are no coloured pixels to estimate from", nameof(rgb));
        }

        double meanLength = Math.Sqrt((mean[0] * mean[0]) + (mean[1] * mean[1]) + (mean[2] * mean[2]));
        for (int c = 0; c < 3; c++)
        {
            mean[c] /= meanLength;
        }

        var scored = new List<(double Angle, double[] Unit)>(directions.Count);
        foreach (double[] unit in directions)
        {
            double dot = (unit[0] * mean[0]) + (unit[1] * mean[1]) + (unit[2] * mean[2]);
            scored.Add((Math.Acos(Math.Clamp(dot, -1.0, 1.0)), unit));
        }

        scored.Sort(static (a, b) => b.Angle.CompareTo(a.Angle));
        int take = Math.Max(1, (int)Math.Round(scored.Count * percentage / 100.0));

        var scatter = new double[3, 3];
        for (int k = 0; k < take; k++)
        {
            double[] unit = scored[k].Unit;
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    scatter[r, c] += unit[r] * unit[c];
                }
            }
        }

        double[] estimate = [1.0, 1.0, 1.0];
        for (int step = 0; step < 64; step++)
        {
            var next = new double[3];
            for (int r = 0; r < 3; r++)
            {
                next[r] = (scatter[r, 0] * estimate[0]) + (scatter[r, 1] * estimate[1]) + (scatter[r, 2] * estimate[2]);
            }

            double length = Math.Sqrt((next[0] * next[0]) + (next[1] * next[1]) + (next[2] * next[2]));
            if (length <= 0)
            {
                break;
            }

            for (int c = 0; c < 3; c++)
            {
                next[c] /= length;
            }

            estimate = next;
        }

        // The eigenvector's sign is arbitrary; an illuminant is not.
        if (estimate[0] + estimate[1] + estimate[2] < 0)
        {
            for (int c = 0; c < 3; c++)
            {
                estimate[c] = -estimate[c];
            }
        }

        return Normalize(estimate);
    }

    /// <summary>Scales an estimate so its largest channel is one, which is only a convention.</summary>
    private static double[] Normalize(double[] estimate)
    {
        double peak = Math.Max(estimate[0], Math.Max(estimate[1], estimate[2]));
        if (peak <= 0)
        {
            return [1.0, 1.0, 1.0];
        }

        return [estimate[0] / peak, estimate[1] / peak, estimate[2] / peak];
    }
}
