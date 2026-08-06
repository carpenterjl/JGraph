using JGraph.Imaging;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M46 wave E: the enhancement and denoising builtins — <c>adapthisteq</c>, <c>imhistmatch</c>,
/// <c>imflatfield</c>, <c>decorrstretch</c>, <c>imsharpen</c>, the four edge-preserving filters,
/// the dark-channel haze pair, and the Hessian ridge measures.
/// </summary>
/// <remarks>
/// Every one of these has a knob whose default MATLAB states in terms of the image's class — a
/// degree of smoothing of <c>0.01·range²</c>, a gradient threshold of <c>0.1·range</c>. Images here
/// are always carried on <c>[0, 1]</c> whatever their class tag, so those defaults reduce to a
/// single number apiece and mean the same thing for a <c>uint8</c> picture as for a double one.
/// </remarks>
internal static partial class JgsBuiltins
{
    private static readonly OptionSpec AdaptHistEqSpec = new(
        "adapthisteq", [], ["NumTiles", "ClipLimit", "NBins", "Range", "Distribution", "Alpha"]);

    private static readonly OptionSpec HistMatchSpec = new("imhistmatch", [], ["Method"]);

    private static readonly OptionSpec FlatFieldSpec = new("imflatfield", [], ["FilterSize"]);

    private static readonly OptionSpec DecorrStretchSpec = new(
        "decorrstretch", [], ["Mode", "TargetMean", "TargetSigma", "Tol", "SampleSubs"]);

    private static readonly OptionSpec SharpenSpec = new(
        "imsharpen", [], ["Radius", "Amount", "Threshold"]);

    private static readonly OptionSpec BilateralSpec = new(
        "imbilatfilt", [], ["NeighborhoodSize", "Padding"]);

    private static readonly OptionSpec GuidedSpec = new(
        "imguidedfilter", [], ["NeighborhoodSize", "DegreeOfSmoothing"]);

    private static readonly OptionSpec DiffuseSpec = new(
        "imdiffusefilt", [],
        ["GradientThreshold", "NumberOfIterations", "Connectivity", "ConductionMethod"]);

    private static readonly OptionSpec DiffuseEstimateSpec = new(
        "imdiffuseest", [], ["Connectivity", "ConductionMethod", "NumberOfIterations"]);

    private static readonly OptionSpec NonLocalMeansSpec = new(
        "imnlmfilt", [], ["DegreeOfSmoothing", "SearchWindowSize", "ComparisonWindowSize"]);

    private static readonly OptionSpec ReduceHazeSpec = new(
        "imreducehaze", [], ["Method", "AtmosphericLight", "ContrastEnhancement", "BoostAmount"]);

    private static readonly OptionSpec LocalBrightenSpec = new("imlocalbrighten", [], ["AlphaBlend"]);

    private static readonly OptionSpec FiberMetricSpec = new(
        "fibermetric", [], ["StructureSensitivity", "ObjectPolarity"]);

    private static void DefineEnhancementBuiltins(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> define, JgsDialect dialect)
    {
        // --- Histogram-based enhancement -----------------------------------------------------
        define("adapthisteq", (args, line, col) =>
        {
            ArityRange("adapthisteq", args, 1, 13, line, col);
            ParsedArgs parsed = AdaptHistEqSpec.Parse(args, 1, line, col);
            if (parsed.Positional.Count < 1)
            {
                throw new JgsRuntimeException(line, col, "adapthisteq needs an image.");
            }

            using ImgArg source = ImgLike("adapthisteq", parsed.Positional, 0, line, col);
            (int tileRows, int tileCols) = parsed.Window("NumTiles") ?? (8, 8);
            double clipLimit = parsed.Scalar("ClipLimit", 0.01);
            int bins = (int)Math.Round(parsed.Scalar("NBins", 256));
            double alpha = parsed.Scalar("Alpha", 0.4);

            string rangeWord = parsed.Text("Range") ?? "full";
            (double Low, double High)? range = rangeWord.ToLowerInvariant() switch
            {
                "full" => (0.0, 1.0),
                "original" => Extremes(source.Buffer),
                _ => throw new JgsRuntimeException(line, col,
                    $"adapthisteq: unknown 'Range' value '{rangeWord}' (use 'full' or 'original')."),
            };

            string shapeWord = parsed.Text("Distribution") ?? "uniform";
            Enhancement.HistogramShape shape = shapeWord.ToLowerInvariant() switch
            {
                "uniform" => Enhancement.HistogramShape.Uniform,
                "rayleigh" => Enhancement.HistogramShape.Rayleigh,
                "exponential" => Enhancement.HistogramShape.Exponential,
                _ => throw new JgsRuntimeException(line, col,
                    $"adapthisteq: unknown 'Distribution' value '{shapeWord}' " +
                    "(use 'uniform', 'rayleigh', or 'exponential')."),
            };

            try
            {
                return ImgLikeOut(
                    Enhancement.Clahe(source.Buffer, tileRows, tileCols, clipLimit, bins, shape, alpha, range),
                    source);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"adapthisteq: {ex.Message}");
            }
        });

        define("imhistmatch", (args, line, col) => HistMatchOutputs(args, 1, line, col)[0]);

        define("imflatfield", (args, line, col) =>
        {
            ArityRange("imflatfield", args, 2, 5, line, col);
            ParsedArgs parsed = FlatFieldSpec.Parse(args, 3, line, col);
            if (parsed.Positional.Count < 2)
            {
                throw new JgsRuntimeException(line, col, "imflatfield(I, sigma) needs an image and a sigma.");
            }

            using ImgArg source = ImgLike("imflatfield", parsed.Positional, 0, line, col);
            double sigma = Num("imflatfield", parsed.Positional, 1, line, col);
            int filterSize = parsed.Window("FilterSize")?.Height ?? 0;
            using ImgArg mask = parsed.Positional.Count >= 3
                ? ImgLike("imflatfield", parsed.Positional, 2, line, col)
                : default;

            try
            {
                return ImgLikeOut(
                    Enhancement.FlatField(
                        source.Buffer, sigma, filterSize,
                        parsed.Positional.Count >= 3 ? mask.Buffer : null),
                    source);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"imflatfield: {ex.Message}");
            }
        });

        define("decorrstretch", (args, line, col) =>
        {
            ArityRange("decorrstretch", args, 1, 11, line, col);
            ParsedArgs parsed = DecorrStretchSpec.Parse(args, 1, line, col);
            if (parsed.Positional.Count < 1)
            {
                throw new JgsRuntimeException(line, col, "decorrstretch needs an image.");
            }

            using ImgArg source = ImgLike("decorrstretch", parsed.Positional, 0, line, col);
            string modeWord = parsed.Text("Mode") ?? "correlation";
            Enhancement.StretchMode mode = modeWord.ToLowerInvariant() switch
            {
                "correlation" => Enhancement.StretchMode.Correlation,
                "covariance" => Enhancement.StretchMode.Covariance,
                _ => throw new JgsRuntimeException(line, col,
                    $"decorrstretch: unknown 'Mode' value '{modeWord}' (use 'correlation' or 'covariance')."),
            };

            double[]? targetMean = parsed.Vector("TargetMean");
            double[]? targetSigma = parsed.Vector("TargetSigma");
            (double Low, double High)? tolerance = null;
            if (parsed.Vector("Tol") is { } tol)
            {
                tolerance = tol.Length switch
                {
                    // MATLAB reads one number as a symmetric pair, which is stretchlim's own rule.
                    1 => (tol[0], 1 - tol[0]),
                    2 => (tol[0], tol[1]),
                    _ => throw new JgsRuntimeException(line, col,
                        "decorrstretch: 'Tol' takes one number or a [low high] pair."),
                };
            }

            IReadOnlyList<(int Row, int Col)>? sample =
                SampleSubscripts(parsed.Named("SampleSubs"), source.Buffer, dialect, line, col);

            try
            {
                return ImgLikeOut(
                    Enhancement.DecorrelationStretch(
                        source.Buffer, mode, targetMean, targetSigma, tolerance, sample),
                    source);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"decorrstretch: {ex.Message}");
            }
        });

        define("imsharpen", (args, line, col) =>
        {
            ArityRange("imsharpen", args, 1, 7, line, col);
            ParsedArgs parsed = SharpenSpec.Parse(args, 1, line, col);
            if (parsed.Positional.Count < 1)
            {
                throw new JgsRuntimeException(line, col, "imsharpen needs an image.");
            }

            using ImgArg source = ImgLike("imsharpen", parsed.Positional, 0, line, col);
            try
            {
                return ImgLikeOut(
                    Enhancement.Sharpen(
                        source.Buffer,
                        parsed.Scalar("Radius", 1.0),
                        parsed.Scalar("Amount", 0.8),
                        parsed.Scalar("Threshold", 0.0)),
                    source);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"imsharpen: {ex.Message}");
            }
        });

        // --- Edge-preserving smoothing -------------------------------------------------------
        define("imbilatfilt", (args, line, col) =>
        {
            ArityRange("imbilatfilt", args, 1, 7, line, col);
            ParsedArgs parsed = BilateralSpec.Parse(args, 3, line, col);
            if (parsed.Positional.Count < 1)
            {
                throw new JgsRuntimeException(line, col, "imbilatfilt needs an image.");
            }

            using ImgArg source = ImgLike("imbilatfilt", parsed.Positional, 0, line, col);
            double smoothing = parsed.Positional.Count >= 2
                ? Num("imbilatfilt", parsed.Positional, 1, line, col)
                : 0.01;
            double spatialSigma = parsed.Positional.Count >= 3
                ? Num("imbilatfilt", parsed.Positional, 2, line, col)
                : 1.0;
            int size = parsed.Window("NeighborhoodSize")?.Height ?? 0;
            (Filters.Boundary boundary, double padValue) =
                PaddingOption("imbilatfilt", parsed, Filters.Boundary.Symmetric, line, col);

            try
            {
                return ImgLikeOut(
                    Denoising.Bilateral(source.Buffer, smoothing, spatialSigma, size, boundary, padValue),
                    source);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"imbilatfilt: {ex.Message}");
            }
        });

        define("imguidedfilter", (args, line, col) =>
        {
            ArityRange("imguidedfilter", args, 1, 6, line, col);
            ParsedArgs parsed = GuidedSpec.Parse(args, 2, line, col);
            if (parsed.Positional.Count < 1)
            {
                throw new JgsRuntimeException(line, col, "imguidedfilter needs an image.");
            }

            using ImgArg source = ImgLike("imguidedfilter", parsed.Positional, 0, line, col);

            // One argument means the picture guides itself, which is the plain denoising use.
            using ImgArg guide = parsed.Positional.Count >= 2
                ? ImgLike("imguidedfilter", parsed.Positional, 1, line, col)
                : new ImgArg(source.Buffer, ImgShape.Image);

            (int rows, int cols) = parsed.Window("NeighborhoodSize") ?? (5, 5);
            double smoothing = parsed.Scalar("DegreeOfSmoothing", 0.01);
            try
            {
                return ImgLikeOut(
                    Denoising.GuidedFilter(source.Buffer, guide.Buffer, rows, cols, smoothing), source);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"imguidedfilter: {ex.Message}");
            }
        });

        define("imdiffusefilt", (args, line, col) =>
        {
            ArityRange("imdiffusefilt", args, 1, 9, line, col);
            ParsedArgs parsed = DiffuseSpec.Parse(args, 1, line, col);
            if (parsed.Positional.Count < 1)
            {
                throw new JgsRuntimeException(line, col, "imdiffusefilt needs an image.");
            }

            using ImgArg source = ImgLike("imdiffusefilt", parsed.Positional, 0, line, col);
            int iterations = (int)Math.Round(parsed.Scalar("NumberOfIterations", 5));
            if (iterations < 1)
            {
                throw new JgsRuntimeException(line, col, "imdiffusefilt needs at least one iteration.");
            }

            double[] thresholds = parsed.Vector("GradientThreshold") ?? [0.1];
            if (thresholds.Length == 1)
            {
                double only = thresholds[0];
                thresholds = new double[iterations];
                Array.Fill(thresholds, only);
            }
            else if (thresholds.Length != iterations)
            {
                throw new JgsRuntimeException(line, col,
                    "imdiffusefilt: 'GradientThreshold' takes one number or one per iteration.");
            }

            (bool eight, Denoising.Conduction conduction) = DiffusionOptions("imdiffusefilt", parsed, line, col);
            try
            {
                return ImgLikeOut(
                    Denoising.AnisotropicDiffusion(source.Buffer, thresholds, eight, conduction), source);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"imdiffusefilt: {ex.Message}");
            }
        });

        define("imdiffuseest", (args, line, col) => DiffuseEstimateOutputs(args, 1, line, col)[0]);

        define("imnlmfilt", (args, line, col) => NonLocalMeansOutputs(args, 1, line, col)[0]);

        // --- Haze ----------------------------------------------------------------------------
        define("imreducehaze", (args, line, col) => ReduceHazeOutputs(args, 1, line, col)[0]);

        define("imlocalbrighten", (args, line, col) => LocalBrightenOutputs(args, 1, line, col)[0]);

        // --- Ridge measures ------------------------------------------------------------------
        define("fibermetric", (args, line, col) =>
        {
            ArityRange("fibermetric", args, 1, 6, line, col);
            ParsedArgs parsed = FiberMetricSpec.Parse(args, 2, line, col);
            if (parsed.Positional.Count < 1)
            {
                throw new JgsRuntimeException(line, col, "fibermetric needs an image.");
            }

            using ImgArg source = ImgLike("fibermetric", parsed.Positional, 0, line, col);
            double[] thicknesses = parsed.Positional.Count >= 2
                ? NumericVector("fibermetric", parsed.Positional[1], line, col)
                : [4, 6, 8, 10, 12, 14];

            string polarityWord = parsed.Text("ObjectPolarity") ?? "bright";
            Vesselness.Polarity polarity = polarityWord.ToLowerInvariant() switch
            {
                "bright" => Vesselness.Polarity.Bright,
                "dark" => Vesselness.Polarity.Dark,
                _ => throw new JgsRuntimeException(line, col,
                    $"fibermetric: unknown 'ObjectPolarity' value '{polarityWord}' (use 'bright' or 'dark')."),
            };

            try
            {
                ImageBuffer measured = Vesselness.FiberMetric(
                    source.Buffer, thicknesses, parsed.Scalar("StructureSensitivity", 0.01), polarity);

                // The measure is a probability-like score, never the original picture's class.
                return source.FromMatrix
                    ? ImgLikeOut(measured, source)
                    : ImgOut(measured, ImageClass.Double);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"fibermetric: {ex.Message}");
            }
        });

        define("maxhessiannorm", (args, line, col) =>
        {
            ArityRange("maxhessiannorm", args, 1, 2, line, col);
            using ImgArg source = ImgLike("maxhessiannorm", args, 0, line, col);
            double thickness = args.Count >= 2 ? Num("maxhessiannorm", args, 1, line, col) : 4;
            try
            {
                return JgsValue.Number(Vesselness.MaxHessianNorm(source.Buffer, thickness));
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"maxhessiannorm: {ex.Message}");
            }
        });
    }

    // -------------------------------------------------------------------------------------------
    // Multiple-output bodies. Each is the whole builtin; the single-output define above asks for one.
    // -------------------------------------------------------------------------------------------

    /// <summary><c>[J, T] = histeq(I, ...)</c> — the equalized picture and the mapping it used.</summary>
    private static JgsValue[] HistEqOutputs(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("histeq", args, 1, 2, line, col);
        using ImgArg source = ImgLike("histeq", args, 0, line, col);

        // A lone number is a count of levels to flatten onto; anything longer is the shape to match.
        // MATLAB writes the first as the second — histeq(I, n) is histeq against n equal bins — so
        // both go through the same routine and cannot disagree.
        double[] shape;
        if (args.Count == 2 && args[1].Type != JgsType.Number)
        {
            shape = ToDoubles("histeq", args[1], line, col);
        }
        else
        {
            shape = new double[args.Count == 2 ? Count("histeq", args, 1, line, col) : 64];
            Array.Fill(shape, 1.0);
        }

        try
        {
            (ImageBuffer result, double[] transform) = Histograms.Equalize(source.Buffer, shape);
            return wanted < 2
                ? [ImgLikeOut(result, source)]
                : [ImgLikeOut(result, source), Numbers(transform)];
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, ex.Message);
        }
    }

    /// <summary><c>[J, hgram] = imhistmatch(A, ref, N, ...)</c>.</summary>
    private static JgsValue[] HistMatchOutputs(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("imhistmatch", args, 2, 5, line, col);
        ParsedArgs parsed = HistMatchSpec.Parse(args, 3, line, col);
        if (parsed.Positional.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "imhistmatch(A, ref) needs two images.");
        }

        using ImgArg source = ImgLike("imhistmatch", parsed.Positional, 0, line, col);
        using ImgArg reference = ImgLike("imhistmatch", parsed.Positional, 1, line, col);
        int bins = parsed.Positional.Count >= 3 ? Count("imhistmatch", parsed.Positional, 2, line, col) : 64;

        string methodWord = parsed.Text("Method") ?? "uniform";
        bool smooth = methodWord.ToLowerInvariant() switch
        {
            "uniform" => false,
            "polynomial" => true,
            _ => throw new JgsRuntimeException(line, col,
                $"imhistmatch: unknown 'Method' value '{methodWord}' (use 'uniform' or 'polynomial')."),
        };

        try
        {
            (ImageBuffer result, double[] histogram) =
                Enhancement.MatchHistogram(source.Buffer, reference.Buffer, bins, smooth);
            return wanted < 2
                ? [ImgLikeOut(result, source)]
                : [ImgLikeOut(result, source), Numbers(histogram)];
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"imhistmatch: {ex.Message}");
        }
    }

    /// <summary><c>[gradThresh, numIter] = imdiffuseest(I, ...)</c>.</summary>
    private static JgsValue[] DiffuseEstimateOutputs(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("imdiffuseest", args, 1, 7, line, col);
        ParsedArgs parsed = DiffuseEstimateSpec.Parse(args, 1, line, col);
        if (parsed.Positional.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "imdiffuseest needs an image.");
        }

        using ImgArg source = ImgLike("imdiffuseest", parsed.Positional, 0, line, col);

        // The conduction and connectivity words are read and checked even though the estimate does
        // not turn on them: a script that misspells one should hear about it here rather than two
        // lines later when imdiffusefilt gets the same word.
        _ = DiffusionOptions("imdiffuseest", parsed, line, col);
        int iterations = (int)Math.Round(parsed.Scalar("NumberOfIterations", 5));
        try
        {
            (double[] thresholds, int count) = Denoising.EstimateDiffusion(source.Buffer, iterations);
            return wanted < 2
                ? [Numbers(thresholds)]
                : [Numbers(thresholds), JgsValue.Number(count)];
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"imdiffuseest: {ex.Message}");
        }
    }

    /// <summary><c>[B, estDoS] = imnlmfilt(I, ...)</c>.</summary>
    private static JgsValue[] NonLocalMeansOutputs(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("imnlmfilt", args, 1, 7, line, col);
        ParsedArgs parsed = NonLocalMeansSpec.Parse(args, 1, line, col);
        if (parsed.Positional.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "imnlmfilt needs an image.");
        }

        using ImgArg source = ImgLike("imnlmfilt", parsed.Positional, 0, line, col);

        // With no degree of smoothing given, the noise in the picture supplies one.
        double estimated = Denoising.EstimateNoise(source.Buffer);
        double smoothing = parsed.Scalar("DegreeOfSmoothing", estimated > 0 ? estimated : 0.01);
        int searchSize = OddWindow("imnlmfilt", parsed, "SearchWindowSize", 21, line, col);
        int compareSize = OddWindow("imnlmfilt", parsed, "ComparisonWindowSize", 5, line, col);

        try
        {
            ImageBuffer result = Denoising.NonLocalMeans(source.Buffer, smoothing, searchSize, compareSize);
            return wanted < 2
                ? [ImgLikeOut(result, source)]
                : [ImgLikeOut(result, source), JgsValue.Number(estimated)];
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"imnlmfilt: {ex.Message}");
        }
    }

    /// <summary><c>[B, T] = imreducehaze(A, amount, ...)</c>.</summary>
    private static JgsValue[] ReduceHazeOutputs(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("imreducehaze", args, 1, 10, line, col);
        ParsedArgs parsed = ReduceHazeSpec.Parse(args, 2, line, col);
        if (parsed.Positional.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "imreducehaze needs an image.");
        }

        using ImgArg source = ImgLike("imreducehaze", parsed.Positional, 0, line, col);
        double amount = parsed.Positional.Count >= 2
            ? Num("imreducehaze", parsed.Positional, 1, line, col)
            : 0.9;

        string methodWord = parsed.Text("Method") ?? "simpledcp";
        Enhancement.HazeMethod method = methodWord.ToLowerInvariant() switch
        {
            "simpledcp" => Enhancement.HazeMethod.SimpleDarkChannel,
            "approxdcp" => Enhancement.HazeMethod.ApproximateDarkChannel,
            _ => throw new JgsRuntimeException(line, col,
                $"imreducehaze: unknown 'Method' value '{methodWord}' (use 'simpledcp' or 'approxdcp')."),
        };

        string contrastWord = parsed.Text("ContrastEnhancement") ?? "global";
        Enhancement.HazeContrast contrast = contrastWord.ToLowerInvariant() switch
        {
            "global" => Enhancement.HazeContrast.Global,
            "boost" => Enhancement.HazeContrast.Boost,
            "none" => Enhancement.HazeContrast.None,
            _ => throw new JgsRuntimeException(line, col,
                $"imreducehaze: unknown 'ContrastEnhancement' value '{contrastWord}' " +
                "(use 'global', 'boost', or 'none')."),
        };

        double[]? light = parsed.Vector("AtmosphericLight");
        try
        {
            (ImageBuffer result, ImageBuffer transmission) = Enhancement.ReduceHaze(
                source.Buffer, amount, method, light, contrast, parsed.Scalar("BoostAmount", 0.1));
            if (wanted < 2)
            {
                transmission.Dispose();
                return [ImgLikeOut(result, source)];
            }

            return [ImgLikeOut(result, source), TransmissionOut(transmission, source)];
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"imreducehaze: {ex.Message}");
        }
    }

    /// <summary><c>[B, T] = imlocalbrighten(A, amount, ...)</c>.</summary>
    private static JgsValue[] LocalBrightenOutputs(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("imlocalbrighten", args, 1, 4, line, col);
        ParsedArgs parsed = LocalBrightenSpec.Parse(args, 2, line, col);
        if (parsed.Positional.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "imlocalbrighten needs an image.");
        }

        using ImgArg source = ImgLike("imlocalbrighten", parsed.Positional, 0, line, col);
        double amount = parsed.Positional.Count >= 2
            ? Num("imlocalbrighten", parsed.Positional, 1, line, col)
            : 1.0;

        try
        {
            (ImageBuffer result, ImageBuffer transmission) =
                Enhancement.LocalBrighten(source.Buffer, amount, parsed.Flag("AlphaBlend", false));
            if (wanted < 2)
            {
                transmission.Dispose();
                return [ImgLikeOut(result, source)];
            }

            return [ImgLikeOut(result, source), TransmissionOut(transmission, source)];
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"imlocalbrighten: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------------------------
    // Shared option readers
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>imnoise</c>'s <c>'localvar'</c> variance field, from either of the two forms MATLAB takes:
    /// a variance for every pixel, or a curve of variance against intensity.
    /// </summary>
    /// <remarks>
    /// The curve form is the useful one for modelling a real sensor, whose noise grows with the
    /// signal. It is read as a piecewise-linear function of the pixel's own value, held flat outside
    /// the curve's ends so a pixel brighter than the calibration still gets the noise the brightest
    /// calibrated level had.
    /// </remarks>
    private static double[,] LocalVariance(
        ImageBuffer image, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 3)
        {
            double[,] field = args[2].Type == JgsType.Image
                ? PointOps.ToMatrix(args[2].AsImage, 0)
                : Rectangle("imnoise 'localvar' variance", args[2], line, col);
            if (field.GetLength(0) != image.Height || field.GetLength(1) != image.Width)
            {
                throw new JgsRuntimeException(line, col,
                    "imnoise 'localvar': the variance must be the same size as the image.");
            }

            return field;
        }

        double[] intensity = ToDoubles("imnoise", args[2], line, col);
        double[] variance = ToDoubles("imnoise", args[3], line, col);
        if (intensity.Length != variance.Length || intensity.Length < 2)
        {
            throw new JgsRuntimeException(line, col,
                "imnoise 'localvar': the intensity and variance vectors must be the same length, and at least two long.");
        }

        var mapped = new double[image.Height, image.Width];
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++)
            {
                mapped[r, c] = Interpolate(intensity, variance, image[r, c, 0]);
            }
        }

        GC.KeepAlive(image);
        return mapped;
    }

    /// <summary>Piecewise-linear lookup, held flat beyond both ends.</summary>
    private static double Interpolate(double[] xs, double[] ys, double x)
    {
        if (x <= xs[0])
        {
            return ys[0];
        }

        for (int i = 1; i < xs.Length; i++)
        {
            if (x > xs[i])
            {
                continue;
            }

            double span = xs[i] - xs[i - 1];
            return span > 0
                ? ys[i - 1] + ((ys[i] - ys[i - 1]) * (x - xs[i - 1]) / span)
                : ys[i];
        }

        return ys[^1];
    }

    /// <summary>
    /// A one-channel side output — the haze transmission map — in the form its picture arrived in.
    /// </summary>
    /// <remarks>
    /// It is always a plain number per pixel, never a copy of the picture's class: a transmission of
    /// 0.4 is a fraction of light, not a grey level, and quantizing it onto a <c>uint8</c> grid
    /// because the picture was <c>uint8</c> would throw away most of what it says.
    /// </remarks>
    private static JgsValue TransmissionOut(ImageBuffer transmission, ImgArg source)
    {
        if (source.Shape == ImgShape.Image)
        {
            return ImgOut(transmission, ImageClass.Double);
        }

        using (transmission)
        {
            return MatrixToRows(PointOps.ToMatrix(transmission, 0));
        }
    }

    /// <summary>The <c>'Connectivity'</c> and <c>'ConductionMethod'</c> pair the diffusion builtins share.</summary>
    private static (bool EightConnected, Denoising.Conduction Conduction) DiffusionOptions(
        string name, ParsedArgs parsed, int line, int col)
    {
        string connectivity = parsed.Text("Connectivity") ?? "maximal";
        bool eight = connectivity.ToLowerInvariant() switch
        {
            "maximal" => true,
            "minimal" => false,
            _ => throw new JgsRuntimeException(line, col,
                $"{name}: unknown 'Connectivity' value '{connectivity}' (use 'maximal' or 'minimal')."),
        };

        string conductionWord = parsed.Text("ConductionMethod") ?? "exponential";
        Denoising.Conduction conduction = conductionWord.ToLowerInvariant() switch
        {
            "exponential" => Denoising.Conduction.Exponential,
            "quadratic" => Denoising.Conduction.Quadratic,
            _ => throw new JgsRuntimeException(line, col,
                $"{name}: unknown 'ConductionMethod' value '{conductionWord}' " +
                "(use 'exponential' or 'quadratic')."),
        };

        return (eight, conduction);
    }

    /// <summary>A window-size option that has to be odd, so the window has a centre pixel.</summary>
    private static int OddWindow(string name, ParsedArgs parsed, string option, int fallback, int line, int col)
    {
        double value = parsed.Scalar(option, fallback);
        int size = (int)Math.Round(value);
        if (size < 1 || size % 2 == 0)
        {
            throw new JgsRuntimeException(line, col, $"{name}: '{option}' must be an odd positive number.");
        }

        return size;
    }

    /// <summary>The smallest and largest sample in an image, which is <c>adapthisteq</c>'s original range.</summary>
    private static (double Low, double High) Extremes(ImageBuffer image)
    {
        double low = double.PositiveInfinity;
        double high = double.NegativeInfinity;
        foreach (double sample in image.Pixels)
        {
            if (sample < low) { low = sample; }
            if (sample > high) { high = sample; }
        }

        GC.KeepAlive(image);
        return double.IsFinite(low) ? (low, high) : (0.0, 1.0);
    }

    /// <summary>
    /// <c>decorrstretch</c>'s <c>'SampleSubs'</c>: which pixels the band statistics are measured from.
    /// </summary>
    /// <remarks>
    /// MATLAB takes a cell holding a row-index vector and a column-index vector. An <c>n×2</c> matrix
    /// of <c>[row col]</c> pairs is accepted as well, because that is the shape a script that built
    /// the list itself — from <c>find</c>, say — is already holding.
    /// </remarks>
    private static IReadOnlyList<(int Row, int Col)>? SampleSubscripts(
        JgsValue? value, ImageBuffer image, JgsDialect dialect, int line, int col)
    {
        if (value is not { } subs)
        {
            return null;
        }

        var pairs = new List<(int Row, int Col)>();
        if (subs.Type == JgsType.Cell)
        {
            JgsValue[] parts = subs.BoxedElements();
            if (parts.Length != 2)
            {
                throw new JgsRuntimeException(line, col,
                    "decorrstretch: 'SampleSubs' as a cell holds exactly the row and column index vectors.");
            }

            double[] rows = ToDoubles("decorrstretch", parts[0], line, col);
            double[] cols = ToDoubles("decorrstretch", parts[1], line, col);
            if (rows.Length != cols.Length)
            {
                throw new JgsRuntimeException(line, col,
                    "decorrstretch: 'SampleSubs' needs as many row indices as column indices.");
            }

            for (int i = 0; i < rows.Length; i++)
            {
                pairs.Add(Subscript(rows[i], cols[i], image, dialect, line, col));
            }

            return pairs;
        }

        double[,] matrix = Rectangle("decorrstretch 'SampleSubs'", subs, line, col);
        if (matrix.GetLength(1) != 2)
        {
            throw new JgsRuntimeException(line, col,
                "decorrstretch: 'SampleSubs' as a matrix has two columns, [row col].");
        }

        for (int i = 0; i < matrix.GetLength(0); i++)
        {
            pairs.Add(Subscript(matrix[i, 0], matrix[i, 1], image, dialect, line, col));
        }

        return pairs;
    }

    private static (int Row, int Col) Subscript(
        double row, double column, ImageBuffer image, JgsDialect dialect, int line, int col,
        string what = "decorrstretch: 'SampleSubs'")
    {
        int r = (int)Math.Round(row) - dialect.IndexBase;
        int c = (int)Math.Round(column) - dialect.IndexBase;
        if ((uint)r >= (uint)image.Height || (uint)c >= (uint)image.Width)
        {
            throw new JgsRuntimeException(line, col,
                $"{what} names a pixel outside the image ({row}, {column}).");
        }

        return (r, c);
    }
}
