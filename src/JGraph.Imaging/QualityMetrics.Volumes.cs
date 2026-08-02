namespace JGraph.Imaging;

/// <summary>
/// The volume forms of the similarity metrics (MATLAB <c>multissim3</c>). Everything about the method
/// carries over from the picture case; the only thing that changes is that the window, the pyramid and
/// the mean all run in three dimensions rather than two.
/// </summary>
public static partial class QualityMetrics
{
    /// <summary>
    /// Structural similarity between two same-size volumes, as an overall score and the map it is the
    /// mean of.
    /// </summary>
    /// <remarks>
    /// Reading a volume slice by slice would compare each plane with its own neighbourhood only, and
    /// two volumes that differ solely in how far apart their features sit through the stack would
    /// score as identical. The window is spherical here for the same reason the picture's is square.
    /// </remarks>
    public static (double Score, Volume Map) StructuralSimilarity(
        Volume a, Volume b, SsimOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        RequireSameSize(a, b);

        double range = options.DynamicRange <= 0 ? 1.0 : options.DynamicRange;
        double radius = options.Radius <= 0 ? 1.5 : options.Radius;
        double[] exponents = options.Weights;
        double[] constants = options.Constants;
        if (exponents.Length != 3)
        {
            throw new ArgumentException("multissim3 takes three exponents: luminance, contrast and structure.");
        }

        if (constants.Length != 2)
        {
            throw new ArgumentException("multissim3 takes two regularization constants.");
        }

        double c1 = constants[0] * range * (constants[0] * range);
        double c2 = constants[1] * range * (constants[1] * range);
        double c3 = c2 / 2;

        double[] window = Filters.Gaussian1D(radius, (2 * (int)Math.Ceiling(3 * radius)) + 1);
        using Volume meanA = Smooth(a, window);
        using Volume meanB = Smooth(b, window);
        using Volume productAA = Product(a, a);
        using Volume productBB = Product(b, b);
        using Volume productAB = Product(a, b);
        using Volume squareA = Smooth(productAA, window);
        using Volume squareB = Smooth(productBB, window);
        using Volume cross = Smooth(productAB, window);

        var map = Volume.Like(a);
        Span<double> values = map.Samples;
        ReadOnlySpan<double> mA = meanA.Samples;
        ReadOnlySpan<double> mB = meanB.Samples;
        ReadOnlySpan<double> sqA = squareA.Samples;
        ReadOnlySpan<double> sqB = squareB.Samples;
        ReadOnlySpan<double> ab = cross.Samples;
        bool plain = exponents[0] == 1 && exponents[1] == 1 && exponents[2] == 1;
        double total = 0;
        for (int i = 0; i < values.Length; i++)
        {
            double varA = sqA[i] - (mA[i] * mA[i]);
            double varB = sqB[i] - (mB[i] * mB[i]);
            double covariance = ab[i] - (mA[i] * mB[i]);
            double luminance = ((2 * mA[i] * mB[i]) + c1) / ((mA[i] * mA[i]) + (mB[i] * mB[i]) + c1);
            double value;
            if (plain)
            {
                value = luminance * (((2 * covariance) + c2) / (varA + varB + c2));
            }
            else
            {
                double sdA = Math.Sqrt(Math.Max(0, varA));
                double sdB = Math.Sqrt(Math.Max(0, varB));
                double contrast = ((2 * sdA * sdB) + c2) / (varA + varB + c2);
                double structure = (covariance + c3) / ((sdA * sdB) + c3);
                value = Math.Pow(luminance, exponents[0])
                    * Math.Pow(contrast, exponents[1])
                    * Math.Pow(structure, exponents[2]);
            }

            values[i] = value;
            total += value;
        }

        return (total / values.Length, map);
    }

    /// <summary>
    /// Multiscale structural similarity between two volumes (MATLAB <c>multissim3</c>): the
    /// contrast-and-structure terms from every scale, weighted together, with the luminance term taken
    /// from the coarsest alone.
    /// </summary>
    public static (double Score, Volume[] Maps) MultiScaleSimilarity(
        Volume a, Volume b, int scales, double[]? weights = null, SsimOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        RequireSameSize(a, b);
        ArgumentOutOfRangeException.ThrowIfLessThan(scales, 1);

        double[] scaleWeights = weights ?? DefaultScaleWeights;
        if (scaleWeights.Length < scales)
        {
            throw new ArgumentException(
                $"multissim3 was asked for {scales} scales but given {scaleWeights.Length} weights.");
        }

        int smallest = Math.Min(a.Height, Math.Min(a.Width, a.Depth));
        int needed = (int)Math.Pow(2, scales - 1) * 2;
        if (smallest < needed)
        {
            throw new ArgumentException(
                $"multissim3 over {scales} scales needs a volume at least {needed} samples on a side; " +
                $"this one is {a.Height}-by-{a.Width}-by-{a.Depth}. Ask for fewer scales.");
        }

        Volume currentA = a;
        Volume currentB = b;
        var maps = new Volume[scales];
        double logScore = 0;
        double weightTotal = 0;
        try
        {
            for (int level = 0; level < scales; level++)
            {
                (double score, Volume map) = StructuralSimilarity(currentA, currentB, options);
                maps[level] = map;

                double contribution = level == scales - 1
                    ? score
                    : score / LuminanceMean(currentA, currentB, options);
                double weight = scaleWeights[level];
                weightTotal += weight;
                logScore += weight * Math.Log(Math.Max(contribution, 1e-12));

                if (level == scales - 1)
                {
                    break;
                }

                Volume nextA = Halve(currentA);
                Volume nextB = Halve(currentB);
                if (!ReferenceEquals(currentA, a))
                {
                    currentA.Dispose();
                    currentB.Dispose();
                }

                currentA = nextA;
                currentB = nextB;
            }
        }
        finally
        {
            if (!ReferenceEquals(currentA, a))
            {
                currentA.Dispose();
                currentB.Dispose();
            }
        }

        return (Math.Exp(logScore / (weightTotal == 0 ? 1 : weightTotal)), maps);
    }

    private static void RequireSameSize(Volume a, Volume b)
    {
        if (!Volume.SameSize(a, b))
        {
            throw new ArgumentException(
                $"the volumes are {a.Height}x{a.Width}x{a.Depth} and {b.Height}x{b.Width}x{b.Depth}; " +
                "a similarity score compares samples at the same place, so they must be the same size.");
        }
    }

    private static double LuminanceMean(Volume a, Volume b, SsimOptions options)
    {
        double range = options.DynamicRange <= 0 ? 1.0 : options.DynamicRange;
        double radius = options.Radius <= 0 ? 1.5 : options.Radius;
        double c1 = options.Constants[0] * range * (options.Constants[0] * range);
        double[] window = Filters.Gaussian1D(radius, (2 * (int)Math.Ceiling(3 * radius)) + 1);
        using Volume meanA = Smooth(a, window);
        using Volume meanB = Smooth(b, window);
        ReadOnlySpan<double> mA = meanA.Samples;
        ReadOnlySpan<double> mB = meanB.Samples;
        double total = 0;
        for (int i = 0; i < mA.Length; i++)
        {
            total += ((2 * mA[i] * mB[i]) + c1) / ((mA[i] * mA[i]) + (mB[i] * mB[i]) + c1);
        }

        double mean = total / mA.Length;
        return mean == 0 ? 1 : mean;
    }

    private static Volume Smooth(Volume volume, double[] window) =>
        VolumeFilters.Separable(volume, window, window, window, Filters.Boundary.Replicate);

    private static Volume Product(Volume a, Volume b)
    {
        var result = Volume.Like(a);
        Span<double> target = result.Samples;
        ReadOnlySpan<double> x = a.Samples;
        ReadOnlySpan<double> y = b.Samples;
        for (int i = 0; i < target.Length; i++)
        {
            target[i] = x[i] * y[i];
        }

        GC.KeepAlive(a);
        GC.KeepAlive(b);
        return result;
    }

    /// <summary>One step down the pyramid: a 2×2×2 average, then every other sample per axis.</summary>
    private static Volume Halve(Volume volume)
    {
        int height = Math.Max(1, volume.Height / 2);
        int width = Math.Max(1, volume.Width / 2);
        int depth = Math.Max(1, volume.Depth / 2);
        var result = new Volume(height, width, depth);
        for (int p = 0; p < depth; p++)
        {
            int p0 = Math.Min((2 * p) + 1, volume.Depth - 1);
            for (int c = 0; c < width; c++)
            {
                int c0 = Math.Min((2 * c) + 1, volume.Width - 1);
                for (int r = 0; r < height; r++)
                {
                    int r0 = Math.Min((2 * r) + 1, volume.Height - 1);
                    double sum = volume[2 * r, 2 * c, 2 * p] + volume[r0, 2 * c, 2 * p]
                        + volume[2 * r, c0, 2 * p] + volume[r0, c0, 2 * p]
                        + volume[2 * r, 2 * c, p0] + volume[r0, 2 * c, p0]
                        + volume[2 * r, c0, p0] + volume[r0, c0, p0];
                    result[r, c, p] = sum / 8;
                }
            }
        }

        return result;
    }
}
