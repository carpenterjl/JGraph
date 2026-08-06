using JGraph.Imaging;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M46 wave K: the three-dimensional half of the toolbox — filtering, gradients, geometry, labelling
/// and measurement on a volume rather than a picture.
/// </summary>
/// <remarks>
/// <para>
/// A volume is a plain N-D array here, exactly as it is in MATLAB, not a new value type and not an
/// image with extra channels. That is not a shortcut: M41 gave arrays real dimensions with
/// column-major storage and the M22 allocator behind them, so <c>zeros(500,500,500)</c> already works
/// and already spills to disk when it has to. Adding a third value type would have bought nothing and
/// cost every function that takes "some numbers" a third case.
/// </para>
/// <para>
/// Which is why every builtin here refuses an image value with a message rather than silently reading
/// one channel of it. An image and a volume are both three-dimensional arrays of numbers and mean
/// entirely different things by the third dimension — colour in one, depth in the other — and a
/// function that quietly accepted either would be wrong half the time without ever saying so.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    private static readonly OptionSpec GaussFilt3Spec = new("imgaussfilt3", [],
        ["FilterSize", "Padding", "FilterDomain"]);

    private static readonly OptionSpec BoxFilt3Spec = new("imboxfilt3", [],
        ["NormalizationFactor", "Padding"]);

    private static readonly OptionSpec IntegralBox3Spec = new("integralBoxFilter3", [],
        ["NormalizationFactor"]);

    private static readonly OptionSpec Resize3Spec = new("imresize3",
        ["nearest", "linear", "cubic", "box", "triangle"],
        ["Method", "Antialiasing", "Scale", "OutputSize"], StringPositionals: 0);

    private static readonly OptionSpec Rotate3Spec = new("imrotate3",
        ["nearest", "linear", "cubic", "crop", "loose"], ["FillValues"], StringPositionals: 0);

    private static readonly OptionSpec MedFilt3Spec = new("medfilt3",
        ["symmetric", "replicate", "zeros"], []);

    private static readonly OptionSpec MultiSsim3Spec = new("multissim3", [],
        ["NumScales", "ScaleWeights", "Sigma", "DynamicRange"]);

    private static readonly OptionSpec ObliqueSpec = new("obliqueslice", [],
        ["OutputSize", "Method", "FillValues"]);

    private static readonly OptionSpec Superpixels3Spec = new("superpixels3", [],
        ["Compactness", "NumIterations", "Method"]);

    private static readonly OptionSpec KMeans3Spec = new("imsegkmeans3", [],
        ["NumAttempts", "MaxIterations", "Threshold", "NormalizeInput"]);

    // The method word sits in the second positional slot, so two slots may hold a string before the
    // option tail starts — otherwise 'Sobel' would be read as a misspelled option name.
    private static readonly OptionSpec Edge3Spec = new("edge3", [], ["alpha"], StringPositionals: 2);

    private static readonly OptionSpec FSpecial3Spec = new("fspecial3", [], [], StringPositionals: 1);

    private static readonly OptionSpec BwMorph3Spec = new("bwmorph3", [], [], StringPositionals: 2);

    private static void DefineVolumeBuiltins(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> define, JgsDialect dialect)
    {
        define("medfilt3", (args, line, col) =>
        {
            ParsedArgs parsed = MedFilt3Spec.Parse(args, 2, line, col);
            ArityRange("medfilt3", parsed.Positional, 1, 2, line, col);
            using Volume volume = Vol("medfilt3", parsed.Positional, 0, line, col);
            (int, int, int) window = parsed.Positional.Count > 1
                ? Window3("medfilt3", parsed.Positional[1], line, col)
                : (3, 3, 3);
            Filters.Boundary boundary =
                parsed.Has("zeros") ? Filters.Boundary.Zero :
                parsed.Has("replicate") ? Filters.Boundary.Replicate :
                Filters.Boundary.Symmetric;
            using Volume result = Guarded(() => VolumeFilters.Median(volume, window, boundary), line, col);
            return VolOut(result);
        });

        define("imgaussfilt3", (args, line, col) =>
        {
            ParsedArgs parsed = GaussFilt3Spec.Parse(args, 2, line, col);
            ArityRange("imgaussfilt3", parsed.Positional, 1, 2, line, col);
            using Volume volume = Vol("imgaussfilt3", parsed.Positional, 0, line, col);
            double[] sigma = parsed.Positional.Count > 1
                ? Triple("imgaussfilt3", parsed.Positional[1], line, col)
                : [0.5, 0.5, 0.5];
            double[]? given = parsed.Vector("FilterSize");
            (int Rows, int Cols, int Planes) size = default;
            if (given is not null)
            {
                double[] spread = Spread("imgaussfilt3", "FilterSize", given, line, col);
                size = (Whole3(spread[0]), Whole3(spread[1]), Whole3(spread[2]));
            }

            (Filters.Boundary boundary, _) =
                PaddingOption("imgaussfilt3", parsed, Filters.Boundary.Replicate, line, col);
            using Volume result = Guarded(
                () => VolumeFilters.GaussianBlur(volume, (sigma[0], sigma[1], sigma[2]), size, boundary),
                line, col);
            return VolOut(result);
        });

        define("imboxfilt3", (args, line, col) =>
        {
            ParsedArgs parsed = BoxFilt3Spec.Parse(args, 2, line, col);
            ArityRange("imboxfilt3", parsed.Positional, 1, 2, line, col);
            using Volume volume = Vol("imboxfilt3", parsed.Positional, 0, line, col);
            (int, int, int) size = parsed.Positional.Count > 1
                ? Window3("imboxfilt3", parsed.Positional[1], line, col)
                : (3, 3, 3);
            double? normalization = parsed.Named("NormalizationFactor") is null
                ? null
                : parsed.Scalar("NormalizationFactor", 1);
            (Filters.Boundary boundary, _) =
                PaddingOption("imboxfilt3", parsed, Filters.Boundary.Replicate, line, col);
            using Volume result = Guarded(
                () => VolumeFilters.BoxMean(volume, size, boundary, normalization), line, col);
            return VolOut(result);
        });

        define("integralImage3", (args, line, col) =>
        {
            Arity("integralImage3", args, 1, line, col);
            using Volume volume = Vol("integralImage3", args, 0, line, col);
            using Volume result = VolumeFilters.Integral(volume);
            return VolOut(result);
        });

        define("integralBoxFilter3", (args, line, col) =>
        {
            ParsedArgs parsed = IntegralBox3Spec.Parse(args, 2, line, col);
            ArityRange("integralBoxFilter3", parsed.Positional, 1, 2, line, col);
            using Volume integral = Vol("integralBoxFilter3", parsed.Positional, 0, line, col);
            (int, int, int) size = parsed.Positional.Count > 1
                ? Window3("integralBoxFilter3", parsed.Positional[1], line, col)
                : (3, 3, 3);
            double? normalization = parsed.Named("NormalizationFactor") is null
                ? null
                : parsed.Scalar("NormalizationFactor", 1);
            using Volume result = Guarded(
                () => VolumeFilters.IntegralBoxFilter(integral, size, normalization), line, col);
            return VolOut(result);
        });

        define("fspecial3", (args, line, col) =>
        {
            ParsedArgs parsed = FSpecial3Spec.Parse(args, 3, line, col);
            ArityRange("fspecial3", parsed.Positional, 1, 3, line, col);
            string type = Str("fspecial3", parsed.Positional, 0, line, col).ToLowerInvariant();
            using Volume kernel = Guarded(() => Kernel3(type, parsed.Positional, line, col), line, col);
            return VolOut(kernel);
        });

        define("imadjustn", (args, line, col) =>
        {
            ArityRange("imadjustn", args, 1, 4, line, col);
            using Volume volume = Vol("imadjustn", args, 0, line, col);
            (double low, double high) = args.Count > 1 && !IsEmpty(args[1])
                ? Pair("imadjustn", args[1], line, col)
                : VolumeFilters.StretchLimits(volume);
            (double lowOut, double highOut) = args.Count > 2 && !IsEmpty(args[2])
                ? Pair("imadjustn", args[2], line, col)
                : (0.0, 1.0);
            double gamma = args.Count > 3 ? Num("imadjustn", args, 3, line, col) : 1.0;
            using Volume result = Guarded(
                () => VolumeFilters.Adjust(volume, low, high, lowOut, highOut, gamma), line, col);
            return VolOut(result);
        });

        define("imhistmatchn", (args, line, col) =>
        {
            ArityRange("imhistmatchn", args, 2, 3, line, col);
            using Volume volume = Vol("imhistmatchn", args, 0, line, col);
            using Volume reference = Vol("imhistmatchn", args, 1, line, col);
            int bins = args.Count > 2 ? Count("imhistmatchn", args, 2, line, col) : 64;
            using Volume result = Guarded(
                () => VolumeFilters.MatchHistogram(volume, reference, bins), line, col);
            return VolOut(result);
        });

        define("edge3", (args, line, col) => Edge3Outputs(args, line, col));

        define("imgradientxyz", (args, line, col) => GradientXyzOutputs(args, 3, line, col)[0]);

        define("imgradient3", (args, line, col) => Gradient3Outputs(args, 3, line, col)[0]);

        define("imresize3", (args, line, col) =>
        {
            ParsedArgs parsed = Resize3Spec.Parse(args, 2, line, col);
            ArityRange("imresize3", parsed.Positional, 1, 2, line, col);
            using Volume volume = Vol("imresize3", parsed.Positional, 0, line, col);
            VolumeGeometry.Interpolation method = Resample3(parsed, line, col);

            JgsValue? request = parsed.Positional.Count > 1 ? parsed.Positional[1]
                : parsed.Named("OutputSize") ?? parsed.Named("Scale");
            if (request is null)
            {
                throw new JgsRuntimeException(line, col,
                    "imresize3 needs a scale factor or an output size.");
            }

            double[] wanted = NumericVector("imresize3", request, line, col);
            (int Rows, int Cols, int Planes) size;
            if (wanted.Length == 1)
            {
                size = (
                    Math.Max(1, (int)Math.Ceiling(volume.Height * wanted[0])),
                    Math.Max(1, (int)Math.Ceiling(volume.Width * wanted[0])),
                    Math.Max(1, (int)Math.Ceiling(volume.Depth * wanted[0])));
            }
            else if (wanted.Length == 3)
            {
                size = (Whole3(wanted[0]), Whole3(wanted[1]), Whole3(wanted[2]));
            }
            else
            {
                throw new JgsRuntimeException(line, col,
                    "imresize3 takes one scale factor or a [rows cols planes] size.");
            }

            bool? antialias = parsed.Named("Antialiasing") is null
                ? null
                : parsed.Flag("Antialiasing", true);
            using Volume result = Guarded(
                () => VolumeGeometry.Resize(volume, size, method, antialias), line, col);
            return VolOut(result);
        });

        define("imrotate3", (args, line, col) =>
        {
            ParsedArgs parsed = Rotate3Spec.Parse(args, 3, line, col);
            Arity("imrotate3", parsed.Positional, 3, line, col);
            using Volume volume = Vol("imrotate3", parsed.Positional, 0, line, col);
            double degrees = ScalarOf("imrotate3", parsed.Positional[1], line, col);
            double[] axis = Triple("imrotate3", parsed.Positional[2], line, col);
            VolumeGeometry.Interpolation method = Resample3(parsed, line, col);
            bool loose = !parsed.Has("crop");
            double fill = parsed.Scalar("FillValues", 0);
            using Volume result = Guarded(
                () => VolumeGeometry.Rotate(
                    volume, degrees, (axis[0], axis[1], axis[2]), method, loose, fill),
                line, col);
            return VolOut(result);
        });

        define("imcrop3", (args, line, col) =>
        {
            Arity("imcrop3", args, 2, line, col);
            using Volume volume = Vol("imcrop3", args, 0, line, col);
            double[] cuboid = NumericVector("imcrop3", args[1], line, col);
            if (cuboid.Length != 6)
            {
                throw new JgsRuntimeException(line, col,
                    "imcrop3 takes a cuboid [x y z width height depth].");
            }

            // MATLAB's cuboid names the box's near corner in spatial coordinates, half a voxel before
            // the first voxel it keeps; the extents are one less than the voxel count, so a cuboid of
            // width 0 still keeps one voxel.
            int shift = dialect.IndexBase;
            int col0 = (int)Math.Round(cuboid[0]) - shift;
            int row0 = (int)Math.Round(cuboid[1]) - shift;
            int plane0 = (int)Math.Round(cuboid[2]) - shift;
            using Volume result = VolumeGeometry.Crop(
                volume,
                (row0, col0, plane0),
                ((int)Math.Round(cuboid[4]) + 1, (int)Math.Round(cuboid[3]) + 1,
                 (int)Math.Round(cuboid[5]) + 1));
            return VolOut(result);
        });

        define("obliqueslice", (args, line, col) => ObliqueSliceOutputs(args, 1, line, col, dialect)[0]);

        define("bwlabeln", (args, line, col) => LabelNOutputs(args, 1, line, col)[0]);

        define("bwmorph3", (args, line, col) =>
        {
            ParsedArgs parsed = BwMorph3Spec.Parse(args, 2, line, col);
            Arity("bwmorph3", parsed.Positional, 2, line, col);
            using Volume volume = Vol("bwmorph3", parsed.Positional, 0, line, col);
            string operation = Str("bwmorph3", parsed.Positional, 1, line, col);
            VolumeRegions.MorphOperation which = operation.ToLowerInvariant() switch
            {
                "branchpoints" => VolumeRegions.MorphOperation.BranchPoints,
                "clean" => VolumeRegions.MorphOperation.Clean,
                "endpoints" => VolumeRegions.MorphOperation.EndPoints,
                "fill" => VolumeRegions.MorphOperation.Fill,
                "majority" => VolumeRegions.MorphOperation.Majority,
                "remove" => VolumeRegions.MorphOperation.Remove,
                _ => throw new JgsRuntimeException(line, col,
                    $"bwmorph3: unknown operation '{operation}' (one of: 'branchpoints', 'clean', " +
                    "'endpoints', 'fill', 'majority', 'remove')."),
            };

            using Volume result = VolumeRegions.Morph(volume, which);
            return VolOut(result);
        });

        define("bwselect3", (args, line, col) =>
        {
            ArityRange("bwselect3", args, 4, 5, line, col);
            using Volume volume = Vol("bwselect3", args, 0, line, col);
            double[] columns = NumericVector("bwselect3", args[1], line, col);
            double[] rows = NumericVector("bwselect3", args[2], line, col);
            double[] planes = NumericVector("bwselect3", args[3], line, col);
            if (columns.Length != rows.Length || rows.Length != planes.Length)
            {
                throw new JgsRuntimeException(line, col,
                    "bwselect3 needs the same number of column, row and plane coordinates.");
            }

            int connectivity = args.Count > 4 ? Count("bwselect3", args, 4, line, col) : 26;
            int shift = dialect.IndexBase;
            var seeds = new (int Row, int Col, int Plane)[rows.Length];
            for (int i = 0; i < seeds.Length; i++)
            {
                seeds[i] = (
                    (int)Math.Round(rows[i]) - shift,
                    (int)Math.Round(columns[i]) - shift,
                    (int)Math.Round(planes[i]) - shift);
            }

            using Volume result = Guarded(
                () => VolumeRegions.Select(volume, seeds, connectivity), line, col);
            return VolOut(result);
        });

        define("regionprops3", (args, line, col) => RegionProps3(args, line, col, dialect));

        define("imsegkmeans3", (args, line, col) => KMeans3Outputs(args, 1, line, col)[0]);

        define("superpixels3", (args, line, col) => Superpixels3Outputs(args, 1, line, col)[0]);

        define("multissim3", (args, line, col) => MultiSsim3Outputs(args, 1, line, col)[0]);
    }

    /// <summary>[BW, thresh] is not a documented pair, so edge3 is single-output.</summary>
    private static JgsValue Edge3Outputs(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ParsedArgs parsed = Edge3Spec.Parse(args, 4, line, col);
        ArityRange("edge3", parsed.Positional, 3, 4, line, col);
        using Volume volume = Vol("edge3", parsed.Positional, 0, line, col);
        string method = Str("edge3", parsed.Positional, 1, line, col).ToLowerInvariant();
        double[] threshold = NumericVector("edge3", parsed.Positional[2], line, col);
        (double Low, double High) window = threshold.Length switch
        {
            // A single number is the level an edge must clear; the lower end of the hysteresis window
            // is taken at 40% of it, which is the ratio the 2-D detector uses.
            1 => (0.4 * threshold[0], threshold[0]),
            2 => (threshold[0], threshold[1]),
            _ => throw new JgsRuntimeException(line, col,
                "edge3 takes a threshold or a [low high] pair."),
        };

        VolumeFilters.EdgeMethod which = method switch
        {
            "approxcanny" => VolumeFilters.EdgeMethod.ApproxCanny,
            "sobel" => VolumeFilters.EdgeMethod.Sobel,
            _ => throw new JgsRuntimeException(line, col,
                $"edge3: unknown method '{method}' (use 'approxcanny' or 'Sobel')."),
        };

        double sigma = parsed.Positional.Count > 3
            ? ScalarOf("edge3", parsed.Positional[3], line, col)
            : Math.Sqrt(2);
        if (which == VolumeFilters.EdgeMethod.Sobel && parsed.Named("alpha") is not null)
        {
            // The Sobel form's 'alpha' smooths before the gradient; a value of zero means no smoothing
            // at all, which is what the plain Sobel detector does.
            sigma = parsed.Scalar("alpha", 0);
        }

        using Volume result = Guarded(
            () => which == VolumeFilters.EdgeMethod.Sobel && sigma > 0
                ? SmoothedSobel(volume, window, sigma)
                : VolumeFilters.Edge(volume, which, window, sigma),
            line, col);
        return VolOut(result);
    }

    private static Volume SmoothedSobel(Volume volume, (double Low, double High) window, double sigma)
    {
        using Volume smoothed = VolumeFilters.GaussianBlur(volume, (sigma, sigma, sigma));
        return VolumeFilters.Edge(smoothed, VolumeFilters.EdgeMethod.Sobel, window);
    }

    private static JgsValue[] GradientXyzOutputs(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("imgradientxyz", args, 1, 2, line, col);
        using Volume volume = Vol("imgradientxyz", args, 0, line, col);
        Gradients.Operator op = args.Count > 1
            ? Operator3("imgradientxyz", Str("imgradientxyz", args, 1, line, col), line, col)
            : Gradients.Operator.Sobel;
        (Volume gx, Volume gy, Volume gz) = Guarded(
            () => VolumeFilters.GradientXYZ(volume, op), line, col);
        using (gx)
        using (gy)
        using (gz)
        {
            JgsValue[] outputs = [VolOut(gx), VolOut(gy), VolOut(gz)];
            return outputs[..Math.Clamp(wanted, 1, 3)];
        }
    }

    private static JgsValue[] Gradient3Outputs(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("imgradient3", args, 1, 3, line, col);

        // imgradient3(Gx, Gy, Gz) turns components a script computed itself into the magnitude and
        // angles, which is the form that lets a different derivative filter be substituted.
        if (args.Count == 3)
        {
            using Volume gx = Vol("imgradient3", args, 0, line, col);
            using Volume gy = Vol("imgradient3", args, 1, line, col);
            using Volume gz = Vol("imgradient3", args, 2, line, col);
            return AnglesFrom(gx, gy, gz, wanted, line, col);
        }

        using Volume volume = Vol("imgradient3", args, 0, line, col);
        Gradients.Operator op = args.Count > 1
            ? Operator3("imgradient3", Str("imgradient3", args, 1, line, col), line, col)
            : Gradients.Operator.Sobel;
        (Volume magnitude, Volume azimuth, Volume elevation) = Guarded(
            () => VolumeFilters.Gradient(volume, op), line, col);
        using (magnitude)
        using (azimuth)
        using (elevation)
        {
            JgsValue[] outputs = [VolOut(magnitude), VolOut(azimuth), VolOut(elevation)];
            return outputs[..Math.Clamp(wanted, 1, 3)];
        }
    }

    private static JgsValue[] AnglesFrom(Volume gx, Volume gy, Volume gz, int wanted, int line, int col)
    {
        if (!Volume.SameSize(gx, gy) || !Volume.SameSize(gy, gz))
        {
            throw new JgsRuntimeException(line, col,
                "imgradient3 needs Gx, Gy and Gz to be the same size.");
        }

        using var magnitude = Volume.Like(gx);
        using var azimuth = Volume.Like(gx);
        using var elevation = Volume.Like(gx);
        Span<double> m = magnitude.Samples;
        Span<double> a = azimuth.Samples;
        Span<double> e = elevation.Samples;
        ReadOnlySpan<double> x = gx.Samples;
        ReadOnlySpan<double> y = gy.Samples;
        ReadOnlySpan<double> z = gz.Samples;
        for (int i = 0; i < m.Length; i++)
        {
            double flat = Math.Sqrt((x[i] * x[i]) + (y[i] * y[i]));
            m[i] = Math.Sqrt((flat * flat) + (z[i] * z[i]));
            a[i] = Math.Atan2(-y[i], x[i]) * 180.0 / Math.PI;
            e[i] = Math.Atan2(z[i], flat) * 180.0 / Math.PI;
        }

        JgsValue[] outputs = [VolOut(magnitude), VolOut(azimuth), VolOut(elevation)];
        return outputs[..Math.Clamp(wanted, 1, 3)];
    }

    private static JgsValue[] LabelNOutputs(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("bwlabeln", args, 1, 2, line, col);
        using Volume mask = Vol("bwlabeln", args, 0, line, col);
        int connectivity = args.Count > 1 ? Count("bwlabeln", args, 1, line, col) : 26;
        (int[] labels, int count) = Guarded(() => VolumeRegions.Label(mask, connectivity), line, col);
        var flat = new double[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            flat[i] = labels[i];
        }

        JgsValue map = JgsMatrix.FromColumnMajorDims(flat, [mask.Height, mask.Width, mask.Depth]);
        return wanted < 2 ? [map] : [map, JgsValue.Number(count)];
    }

    private static JgsValue[] KMeans3Outputs(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = KMeans3Spec.Parse(args, 2, line, col);
        Arity("imsegkmeans3", parsed.Positional, 2, line, col);
        using Volume volume = Vol("imsegkmeans3", parsed.Positional, 0, line, col);
        int clusters = Count("imsegkmeans3", parsed.Positional, 1, line, col);
        int iterations = (int)parsed.Scalar("MaxIterations", 100);
        (int[] labels, double[] centers) = Guarded(
            () => VolumeRegions.KMeans(volume, clusters, iterations), line, col);
        var flat = new double[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            flat[i] = labels[i];
        }

        JgsValue map = JgsMatrix.FromColumnMajorDims(flat, [volume.Height, volume.Width, volume.Depth]);
        return wanted < 2 ? [map] : [map, Numbers(centers)];
    }

    private static JgsValue[] Superpixels3Outputs(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = Superpixels3Spec.Parse(args, 2, line, col);
        Arity("superpixels3", parsed.Positional, 2, line, col);
        using Volume volume = Vol("superpixels3", parsed.Positional, 0, line, col);
        int count = Count("superpixels3", parsed.Positional, 1, line, col);
        double compactness = parsed.Scalar("Compactness", 0.001);
        int iterations = (int)parsed.Scalar("NumIterations", 10);
        (int[] labels, int used) = Guarded(
            () => VolumeRegions.Superpixels(volume, count, compactness, iterations), line, col);
        var flat = new double[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            flat[i] = labels[i];
        }

        JgsValue map = JgsMatrix.FromColumnMajorDims(flat, [volume.Height, volume.Width, volume.Depth]);
        return wanted < 2 ? [map] : [map, JgsValue.Number(used)];
    }

    private static JgsValue[] MultiSsim3Outputs(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ParsedArgs parsed = MultiSsim3Spec.Parse(args, 2, line, col);
        Arity("multissim3", parsed.Positional, 2, line, col);
        using Volume volume = Vol("multissim3", parsed.Positional, 0, line, col);
        using Volume reference = Vol("multissim3", parsed.Positional, 1, line, col);
        int scales = (int)parsed.Scalar("NumScales", 5);
        double[]? weights = parsed.Vector("ScaleWeights");
        var options = new QualityMetrics.SsimOptions(
            DynamicRange: parsed.Scalar("DynamicRange", 1.0),
            Radius: parsed.Scalar("Sigma", 1.5));

        (double score, Volume[] maps) = Guarded(
            () => QualityMetrics.MultiScaleSimilarity(volume, reference, scales, weights, options),
            line, col);
        try
        {
            if (wanted < 2)
            {
                return [JgsValue.Number(score)];
            }

            var boxed = new JgsValue[maps.Length];
            for (int i = 0; i < maps.Length; i++)
            {
                boxed[i] = VolOut(maps[i]);
            }

            return [JgsValue.Number(score), JgsValue.Array(boxed)];
        }
        finally
        {
            foreach (Volume map in maps)
            {
                map.Dispose();
            }
        }
    }

    private static JgsValue[] ObliqueSliceOutputs(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col, JgsDialect dialect)
    {
        ParsedArgs parsed = ObliqueSpec.Parse(args, 3, line, col);
        Arity("obliqueslice", parsed.Positional, 3, line, col);
        using Volume volume = Vol("obliqueslice", parsed.Positional, 0, line, col);
        double[] point = Triple("obliqueslice", parsed.Positional[1], line, col);
        double[] normal = Triple("obliqueslice", parsed.Positional[2], line, col);
        bool full = string.Equals(parsed.Text("OutputSize"), "full", StringComparison.OrdinalIgnoreCase);
        VolumeGeometry.Interpolation method =
            string.Equals(parsed.Text("Method"), "nearest", StringComparison.OrdinalIgnoreCase)
                ? VolumeGeometry.Interpolation.Nearest
                : VolumeGeometry.Interpolation.Linear;
        double fill = parsed.Scalar("FillValues", 0);

        // The point arrives as (x, y, z) — column, row, plane — in the dialect's own index base.
        int shift = dialect.IndexBase;
        (double Row, double Col, double Plane) centre =
            (point[1] - shift, point[0] - shift, point[2] - shift);

        (ImageBuffer slice, double[,] xs, double[,] ys, double[,] zs) = Guarded(
            () => VolumeGeometry.ObliqueSlice(
                volume, centre, (normal[0], normal[1], normal[2]), method, full, fill),
            line, col);
        using (slice)
        {
            JgsValue plane = MatrixToRows(PointOps.ToMatrix(slice, 0));
            if (wanted < 2)
            {
                return [plane];
            }

            JgsValue[] outputs =
            [
                plane,
                MatrixToRows(Shifted(xs, shift)),
                MatrixToRows(Shifted(ys, shift)),
                MatrixToRows(Shifted(zs, shift)),
            ];
            return outputs[..Math.Clamp(wanted, 1, 4)];
        }
    }

    private static JgsValue RegionProps3(IReadOnlyList<JgsValue> args, int line, int col, JgsDialect dialect)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "regionprops3 needs a label or binary volume.");
        }

        using Volume source = Vol("regionprops3", args, 0, line, col);
        int next = 1;
        Volume? intensity = null;
        if (args.Count > 1 && args[1].Type != JgsType.String)
        {
            intensity = Vol("regionprops3", args, 1, line, col);
            next = 2;
        }

        try
        {
            var wanted = new List<string>();
            for (int i = next; i < args.Count; i++)
            {
                wanted.Add(Str("regionprops3", args, i, line, col));
            }

            (int[] labels, int count) = LabelsOfVolume(source);
            VolumeMeasurement[] measured = VolumeRegions.Measure(
                labels, count, (source.Height, source.Width, source.Depth), intensity);
            return JgsValue.Table(Region3Table(measured, wanted, intensity is not null, line, col, dialect));
        }
        finally
        {
            intensity?.Dispose();
        }
    }

    /// <summary>
    /// The measurement table. MATLAB's <c>regionprops3</c> returns a table in every dialect — the one
    /// member of the family that does not have a struct-array form — so both dialects get one here,
    /// and only the scalar measurements can be columns.
    /// </summary>
    private static JGraph.Data.Table Region3Table(
        VolumeMeasurement[] measured,
        List<string> properties,
        bool hasIntensity,
        int line,
        int col,
        JgsDialect dialect)
    {
        double shift = dialect.IndexBase;
        var columns = new List<JGraph.Data.TableColumn>
        {
            new JGraph.Data.NumberColumn("Label", Column3(measured, m => m.Label)),
        };

        void Scalar(string name, Func<VolumeMeasurement, double> read) =>
            columns.Add(new JGraph.Data.NumberColumn(name, Column3(measured, read)));

        List<string> chosen = properties.Count == 0
            ? ["Volume", "Centroid", "BoundingBox"]
            : Expanded(properties, hasIntensity, line, col);

        foreach (string property in chosen)
        {
            switch (property)
            {
                case "Volume": Scalar("Volume", m => m.Volume); break;
                case "Centroid":
                    Scalar("CentroidX", m => m.Centroid.X + shift);
                    Scalar("CentroidY", m => m.Centroid.Y + shift);
                    Scalar("CentroidZ", m => m.Centroid.Z + shift);
                    break;
                case "BoundingBox":
                    Scalar("BBoxX", m => m.BoundingBox.X + shift);
                    Scalar("BBoxY", m => m.BoundingBox.Y + shift);
                    Scalar("BBoxZ", m => m.BoundingBox.Z + shift);
                    Scalar("BBoxWidth", m => m.BoundingBox.Width);
                    Scalar("BBoxHeight", m => m.BoundingBox.Height);
                    Scalar("BBoxDepth", m => m.BoundingBox.Depth);
                    break;
                case "EquivDiameter": Scalar("EquivDiameter", m => m.EquivDiameter); break;
                case "Extent": Scalar("Extent", m => m.Extent); break;
                case "SurfaceArea": Scalar("SurfaceArea", m => m.SurfaceArea); break;
                case "PrincipalAxisLength":
                    Scalar("PrincipalAxisLength1", m => m.PrincipalAxisLength[0]);
                    Scalar("PrincipalAxisLength2", m => m.PrincipalAxisLength[1]);
                    Scalar("PrincipalAxisLength3", m => m.PrincipalAxisLength[2]);
                    break;
                case "EigenValues":
                    Scalar("EigenValue1", m => m.EigenValues[0]);
                    Scalar("EigenValue2", m => m.EigenValues[1]);
                    Scalar("EigenValue3", m => m.EigenValues[2]);
                    break;
                case "Orientation":
                    Scalar("OrientationZ", m => m.Orientation[0]);
                    Scalar("OrientationY", m => m.Orientation[1]);
                    Scalar("OrientationX", m => m.Orientation[2]);
                    break;
                case "MeanIntensity": Scalar("MeanIntensity", m => m.MeanIntensity); break;
                case "MinIntensity": Scalar("MinIntensity", m => m.MinIntensity); break;
                case "MaxIntensity": Scalar("MaxIntensity", m => m.MaxIntensity); break;
                case "WeightedCentroid":
                    Scalar("WeightedCentroidX", m => m.WeightedCentroid.X + shift);
                    Scalar("WeightedCentroidY", m => m.WeightedCentroid.Y + shift);
                    Scalar("WeightedCentroidZ", m => m.WeightedCentroid.Z + shift);
                    break;
                default:
                    // EigenVectors, VoxelList, VoxelIdxList, VoxelValues, Image: a column holds one
                    // number per region, and none of those is one number.
                    break;
            }
        }

        return new JGraph.Data.Table(columns);
    }

    /// <summary>The regionprops3 property names, with 'all' and 'basic' expanded and misspellings named.</summary>
    private static List<string> Expanded(
        List<string> properties, bool hasIntensity, int line, int col)
    {
        string[] shape =
        [
            "Volume", "Centroid", "BoundingBox", "EquivDiameter", "Extent", "SurfaceArea",
            "PrincipalAxisLength", "EigenValues", "EigenVectors", "Orientation",
            "VoxelIdxList", "VoxelList", "Image",
        ];
        string[] intensity = ["MeanIntensity", "MinIntensity", "MaxIntensity", "VoxelValues", "WeightedCentroid"];

        var chosen = new List<string>();
        foreach (string property in properties)
        {
            if (string.Equals(property, "all", StringComparison.OrdinalIgnoreCase))
            {
                chosen.AddRange(shape);
                if (hasIntensity)
                {
                    chosen.AddRange(intensity);
                }

                continue;
            }

            if (string.Equals(property, "basic", StringComparison.OrdinalIgnoreCase))
            {
                chosen.AddRange(["Volume", "Centroid", "BoundingBox"]);
                continue;
            }

            string? match = null;
            foreach (string candidate in shape)
            {
                if (string.Equals(candidate, property, StringComparison.OrdinalIgnoreCase))
                {
                    match = candidate;
                }
            }

            foreach (string candidate in intensity)
            {
                if (string.Equals(candidate, property, StringComparison.OrdinalIgnoreCase))
                {
                    match = candidate;
                }
            }

            if (match is null)
            {
                throw new JgsRuntimeException(line, col,
                    $"regionprops3: unknown property '{property}' (one of: " +
                    $"{string.Join(", ", shape)}, {string.Join(", ", intensity)}).");
            }

            if (!hasIntensity && Array.IndexOf(intensity, match) >= 0)
            {
                throw new JgsRuntimeException(line, col,
                    $"regionprops3: '{match}' needs an intensity volume as the second argument.");
            }

            chosen.Add(match);
        }

        return chosen;
    }

    private static double[] Column3(VolumeMeasurement[] measured, Func<VolumeMeasurement, double> read)
    {
        var values = new double[measured.Length];
        for (int i = 0; i < measured.Length; i++)
        {
            values[i] = read(measured[i]);
        }

        return values;
    }

    /// <summary>
    /// The labels a measurement runs over: a binary volume is labelled first, a label volume is read
    /// as it stands — the same rule <c>regionprops</c> applies to a picture.
    /// </summary>
    private static (int[] Labels, int Count) LabelsOfVolume(Volume source)
    {
        ReadOnlySpan<double> samples = source.Samples;
        bool binary = true;
        double highest = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            highest = Math.Max(highest, samples[i]);
            if (samples[i] != 0 && samples[i] != 1)
            {
                binary = false;
            }
        }

        if (binary)
        {
            return VolumeRegions.Label(source, 26);
        }

        var labels = new int[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            labels[i] = (int)Math.Round(samples[i]);
        }

        GC.KeepAlive(source);
        return (labels, (int)Math.Round(highest));
    }

    /// <summary>
    /// Whether an argument is a volume rather than a picture. Only the shape can say: a plain
    /// <c>h×w×3</c> array is an RGB image to everything in wave A and a three-plane volume to
    /// everything here, and nothing in the value distinguishes them. The functions that take both ask
    /// this question only where MATLAB's own documentation says the N-D reading wins.
    /// </summary>
    private static bool IsVolumeArg(JgsValue value) =>
        value.Type == JgsType.Array && !JgsMatrix.IsNested(value) && JgsMatrix.DimsOf(value).Length == 3;

    /// <summary>The N-D form of <c>padarray</c>, reached when the pad size names all three dimensions.</summary>
    private static JgsValue PadVolume(
        JgsValue value,
        double[] spread,
        Filters.Boundary boundary,
        double padValue,
        Neighborhoods.PadDirection direction,
        int line,
        int col)
    {
        using Volume volume = Vol("padarray", [value], 0, line, col);
        var size = (
            Rows: Math.Max(0, (int)Math.Round(spread[0])),
            Cols: Math.Max(0, (int)Math.Round(spread[1])),
            Planes: Math.Max(0, (int)Math.Round(spread[2])));
        (int, int, int) none = (0, 0, 0);
        using Volume padded = VolumeFilters.Pad(
            volume,
            direction == Neighborhoods.PadDirection.Post ? none : size,
            direction == Neighborhoods.PadDirection.Pre ? none : size,
            boundary,
            padValue);
        return VolOut(padded);
    }

    /// <summary>The N-D form of <c>bwareaopen</c>.</summary>
    private static JgsValue AreaOpenVolume(IReadOnlyList<JgsValue> args, int line, int col)
    {
        using Volume volume = Vol("bwareaopen", args, 0, line, col);
        int minVoxels = Count("bwareaopen", args, 1, line, col);
        int connectivity = args.Count == 3 ? Count("bwareaopen", args, 2, line, col) : 26;
        using Volume result = Guarded(
            () => VolumeRegions.AreaOpen(volume, minVoxels, connectivity), line, col);
        return VolOut(result);
    }

    /// <summary>The N-D form of <c>bwconncomp</c>: the same struct, with a third size and a third subscript.</summary>
    private static JgsValue ComponentsOfVolume(
        IReadOnlyList<JgsValue> args, int line, int col, JgsDialect dialect)
    {
        using Volume volume = Vol("bwconncomp", args, 0, line, col);
        int connectivity = args.Count == 2 ? Count("bwconncomp", args, 1, line, col) : 26;
        (int[] labels, int count) = Guarded(() => VolumeRegions.Label(volume, connectivity), line, col);
        var lists = new List<double>[count];
        for (int i = 0; i < count; i++)
        {
            lists[i] = [];
        }

        for (int i = 0; i < labels.Length; i++)
        {
            if (labels[i] > 0)
            {
                lists[labels[i] - 1].Add(i + dialect.IndexBase);
            }
        }

        var cells = new JgsValue[count];
        for (int i = 0; i < count; i++)
        {
            cells[i] = Numbers([.. lists[i]]);
        }

        return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Connectivity"] = JgsValue.Number(connectivity),
            ["ImageSize"] = Numbers([volume.Height, volume.Width, volume.Depth]),
            ["NumObjects"] = JgsValue.Number(count),
            ["PixelIdxList"] = JgsValue.Cell(cells),
        });
    }

    /// <summary>Reads an argument as a volume: a 3-D array, or a 2-D one read as a single plane.</summary>
    private static Volume Vol(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        JgsValue value = args[index];
        if (value.Type == JgsType.Image)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} works on a volume, but argument {index + 1} is an image. An image's third " +
                "dimension is colour, not depth — build a volume with zeros(h, w, d) or cat(3, ...), " +
                "or use the two-dimensional form of this function.");
        }

        if (value.Type is JgsType.Number or JgsType.Bool)
        {
            var single = new Volume(1, 1, 1);
            single.Samples[0] = value.AsNumber;
            return single;
        }

        if (value.Type != JgsType.Array)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} argument {index + 1} must be numeric data, but got a {value.TypeName}.");
        }

        int[] dims = JgsMatrix.DimsOf(value);
        if (dims.Length > 3)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} argument {index + 1} has {dims.Length} dimensions; a volume has three.");
        }

        if (dims.Length == 3 && !JgsMatrix.IsNested(value))
        {
            double[] flat = ToDoubles(name, value, line, col);
            return Volume.From(flat, dims[0], dims[1], dims[2]);
        }

        // A plain matrix is a volume one plane deep, which is what makes medfilt3 on a picture and
        // regionprops3 on a mask work without a separate spelling.
        double[,] plane = Rectangle($"{name} argument {index + 1}", value, line, col);
        int rows = plane.GetLength(0);
        int cols = plane.GetLength(1);
        var volume = new Volume(rows, cols, 1);
        Span<double> samples = volume.Samples;
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                samples[r + (c * rows)] = plane[r, c];
            }
        }

        return volume;
    }

    /// <summary>Hands a volume back as a plain N-D array, which is the only thing a volume ever is.</summary>
    private static JgsValue VolOut(Volume volume)
    {
        var flat = new double[volume.SampleCount];
        volume.Samples.CopyTo(flat);
        GC.KeepAlive(volume);
        return JgsMatrix.FromColumnMajorDims(flat, [volume.Height, volume.Width, volume.Depth]);
    }

    private static double[,] Shifted(double[,] values, int shift)
    {
        if (shift == 0)
        {
            return values;
        }

        int rows = values.GetLength(0);
        int cols = values.GetLength(1);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                values[r, c] += shift;
            }
        }

        return values;
    }

    private static Volume Kernel3(string type, IReadOnlyList<JgsValue> args, int line, int col)
    {
        switch (type)
        {
            case "average":
                return VolumeFilters.Average(args.Count > 1
                    ? Window3("fspecial3", args[1], line, col)
                    : (3, 3, 3));

            case "gaussian":
            {
                (int, int, int) size = args.Count > 1 && !IsEmpty(args[1])
                    ? Window3("fspecial3", args[1], line, col)
                    : (5, 5, 5);
                double sigma = args.Count > 2 ? ScalarOf("fspecial3", args[2], line, col) : 1.0;
                return VolumeFilters.Gaussian(size, sigma);
            }

            case "laplacian":
            {
                double gamma1 = args.Count > 1 ? ScalarOf("fspecial3", args[1], line, col) : 0;
                double gamma2 = args.Count > 2 ? ScalarOf("fspecial3", args[2], line, col) : 0;
                return VolumeFilters.Laplacian(gamma1, gamma2);
            }

            case "log":
            {
                (int, int, int) size = args.Count > 1 && !IsEmpty(args[1])
                    ? Window3("fspecial3", args[1], line, col)
                    : (5, 5, 5);
                double sigma = args.Count > 2 ? ScalarOf("fspecial3", args[2], line, col) : 1.0;
                return VolumeFilters.LaplacianOfGaussian(size, sigma);
            }

            case "prewitt":
                return VolumeFilters.Derivative(sobel: false);

            case "sobel":
                return VolumeFilters.Derivative(sobel: true);

            case "ellipsoid":
            {
                double[] semi = args.Count > 1
                    ? Triple("fspecial3", args[1], line, col)
                    : [5, 5, 5];
                return VolumeFilters.Ellipsoid((semi[0], semi[1], semi[2]));
            }

            default:
                throw new JgsRuntimeException(line, col,
                    $"fspecial3: unknown filter '{type}' (one of: 'average', 'ellipsoid', 'gaussian', " +
                    "'laplacian', 'log', 'prewitt', 'sobel').");
        }
    }

    private static Gradients.Operator Operator3(string name, string word, int line, int col) =>
        word.ToLowerInvariant() switch
        {
            "sobel" => Gradients.Operator.Sobel,
            "prewitt" => Gradients.Operator.Prewitt,
            "central" => Gradients.Operator.Central,
            "intermediate" => Gradients.Operator.Intermediate,
            _ => throw new JgsRuntimeException(line, col,
                $"{name}: unknown method '{word}' (one of: 'sobel', 'prewitt', 'central', 'intermediate')."),
        };

    private static VolumeGeometry.Interpolation Resample3(ParsedArgs parsed, int line, int col)
    {
        string word = parsed.Text("Method") ?? parsed.OneOf("linear", "nearest", "linear", "cubic", "box", "triangle");
        return word.ToLowerInvariant() switch
        {
            "nearest" => VolumeGeometry.Interpolation.Nearest,
            "linear" or "triangle" => VolumeGeometry.Interpolation.Linear,
            "cubic" => VolumeGeometry.Interpolation.Cubic,
            "box" => VolumeGeometry.Interpolation.Nearest,
            _ => throw new JgsRuntimeException(line, col,
                $"unknown resampling method '{word}' (one of: 'nearest', 'linear', 'cubic')."),
        };
    }

    /// <summary>A size given as one number (a cube) or as [rows cols planes].</summary>
    private static (int Rows, int Cols, int Planes) Window3(
        string name, JgsValue value, int line, int col)
    {
        double[] spread = Spread(name, "size", NumericVector(name, value, line, col), line, col);
        return (Whole3(spread[0]), Whole3(spread[1]), Whole3(spread[2]));
    }

    /// <summary>Three numbers given as one (repeated) or as three.</summary>
    private static double[] Triple(string name, JgsValue value, int line, int col) =>
        Spread(name, "value", NumericVector(name, value, line, col), line, col);

    private static double[] Spread(string name, string what, double[] given, int line, int col) =>
        given.Length switch
        {
            1 => [given[0], given[0], given[0]],
            3 => given,
            _ => throw new JgsRuntimeException(line, col,
                $"{name}: '{what}' takes one number or three, one per dimension."),
        };

    private static (double Low, double High) Pair(string name, JgsValue value, int line, int col)
    {
        double[] limits = NumericVector(name, value, line, col);
        if (limits.Length != 2)
        {
            throw new JgsRuntimeException(line, col, $"{name} takes a [low high] pair.");
        }

        return (limits[0], limits[1]);
    }

    private static double ScalarOf(string name, JgsValue value, int line, int col)
    {
        double[] numbers = NumericVector(name, value, line, col);
        if (numbers.Length != 1)
        {
            throw new JgsRuntimeException(line, col, $"{name} expects a single number here.");
        }

        return numbers[0];
    }

    private static bool IsEmpty(JgsValue value) =>
        value.Type == JgsType.Array && value.ArrayLength == 0;

    private static int Whole3(double value)
    {
        int rounded = (int)Math.Round(value);
        return rounded < 1 ? 1 : rounded;
    }

    /// <summary>Turns an algorithm-layer complaint into a script-layer one, with the call's position.</summary>
    private static T Guarded<T>(Func<T> work, int line, int col)
    {
        try
        {
            return work();
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            throw new JgsRuntimeException(line, col, ex.Message);
        }
    }
}
