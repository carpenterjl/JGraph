using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Imaging;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M46 wave J: whole-picture statistics (<c>mean2</c>, <c>std2</c>, <c>corr2</c>, <c>entropy</c>), the
/// quality metrics (<c>immse</c>, <c>psnr</c>, <c>ssim</c>, <c>multissim</c>, <c>dice</c>,
/// <c>jaccard</c>, <c>bfscore</c>), texture by co-occurrence (<c>graycomatrix</c>,
/// <c>graycoprops</c>), the two ways of reading values back out of a picture (<c>impixel</c>,
/// <c>improfile</c>), and the display composites (<c>imcontour</c>, <c>montage</c>, <c>imfuse</c>,
/// <c>imshowpair</c>) plus the toolbox preference store.
/// </summary>
/// <remarks>
/// Everything here answers a question about a picture rather than producing another one — a number, a
/// table, a list of samples, or a frame with two pictures in it. That is why almost nothing in this
/// file goes through <c>ImgOut</c>: the one exception is <c>imfuse</c>, which is a picture, and it is
/// stamped <c>uint8</c> because MATLAB's is.
/// </remarks>
internal static partial class JgsBuiltins
{
    private static readonly OptionSpec SsimSpec = new(
        "ssim",
        [],
        ["DynamicRange", "Exponents", "RegularizationConstants", "Radius"]);

    private static readonly OptionSpec MultiSsimSpec = new(
        "multissim",
        [],
        ["NumScales", "ScaleWeights", "Sigma", "DynamicRange"]);

    private static readonly OptionSpec ComatrixSpec = new(
        "graycomatrix",
        [],
        ["NumLevels", "GrayLimits", "Offset", "Symmetric"]);

    private static readonly OptionSpec MontageSpec = new(
        "montage",
        [],
        ["Size", "BorderSize", "BackgroundColor", "DisplayRange", "ThumbnailSize"],
        StringPositionals: 1);

    private static readonly OptionSpec FuseSpec = new(
        "imfuse",
        [],
        ["Scaling", "ColorChannels"],
        StringPositionals: 3);

    private static readonly OptionSpec ShowPairSpec = new(
        "imshowpair",
        [],
        ["Scaling", "ColorChannels"],
        StringPositionals: 3);

    /// <summary>
    /// The toolbox preferences, which persist for the life of the session. MATLAB's are per-user and
    /// survive a restart; these do not, because JGraph has no place to put them that would not also
    /// have to be migrated, and every preference here changes only how a picture is shown.
    /// </summary>
    private static readonly Dictionary<string, JgsValue> ImagePreferences =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["ImshowBorder"] = JgsValue.Str("loose"),
            ["ImshowAxesVisible"] = JgsValue.Str("off"),
            ["ImshowInitialMagnification"] = JgsValue.Number(100),
            ["ImtoolInitialMagnification"] = JgsValue.Str("adaptive"),
            ["ImtoolStartWithOverview"] = JgsValue.Bool(false),
            ["UseIPPL"] = JgsValue.Bool(true),
            ["VolumeViewerUseHardware"] = JgsValue.Bool(true),
        };

    private static void DefineMetricBuiltins(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> define, JgsDialect dialect)
    {
        // --- Whole-picture statistics ------------------------------------------------------------
        define("mean2", (args, line, col) =>
        {
            Arity("mean2", args, 1, line, col);
            using ImgArg source = ImgLike("mean2", args, 0, line, col);
            return JgsValue.Number(Across(source, dialect, ImageStatistics.Mean));
        });

        define("std2", (args, line, col) =>
        {
            Arity("std2", args, 1, line, col);
            using ImgArg source = ImgLike("std2", args, 0, line, col);

            // Across channels the spread is one number about all the samples, not the mean of three
            // spreads, so the planes are stacked into one column rather than averaged.
            return JgsValue.Number(ImageStatistics.StandardDeviation(Stacked(source, dialect)));
        });

        define("corr2", (args, line, col) =>
        {
            Arity("corr2", args, 2, line, col);
            using ImgArg first = ImgLike("corr2", args, 0, line, col);
            using ImgArg second = ImgLike("corr2", args, 1, line, col);
            try
            {
                return JgsValue.Number(ImageStatistics.Correlation(
                    Stacked(first, dialect), Stacked(second, dialect)));
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"corr2: {ex.Message}");
            }
        });

        define("entropy", (args, line, col) =>
        {
            Arity("entropy", args, 1, line, col);
            using ImgArg source = ImgLike("entropy", args, 0, line, col);

            // Entropy is read off the histogram, and the histogram is over the picture's own range,
            // so this one is deliberately not rescaled for the dialect.
            double[,] samples = Planes(source, dialect: null)[0];
            if (source.Buffer.Channels > 1)
            {
                samples = Stacked(source, dialect: null);
            }

            int bins = source.Buffer.Class == ImageClass.Logical ? 2 : 256;
            return JgsValue.Number(ImageStatistics.Entropy(samples, bins));
        });

        // --- Quality metrics ---------------------------------------------------------------------
        define("immse", (args, line, col) =>
        {
            Arity("immse", args, 2, line, col);
            using ImgArg first = ImgLike("immse", args, 0, line, col);
            using ImgArg second = ImgLike("immse", args, 1, line, col);
            try
            {
                return JgsValue.Number(QualityMetrics.MeanSquaredError(
                    Stacked(first, dialect), Stacked(second, dialect)));
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"immse: {ex.Message}");
            }
        });

        define("psnr", (args, line, col) => PsnrOutputs(args, 1, line, col, dialect)[0]);
        define("ssim", (args, line, col) => SsimOutputs(args, 1, line, col, dialect)[0]);
        define("multissim", (args, line, col) => MultiSsimOutputs(args, 1, line, col, dialect)[0]);

        define("dice", (args, line, col) => Similarity("dice", args, line, col, dialect));
        define("jaccard", (args, line, col) => Similarity("jaccard", args, line, col, dialect));
        define("bfscore", (args, line, col) => BfScoreOutputs(args, 1, line, col, dialect)[0]);

        // --- Texture -----------------------------------------------------------------------------
        define("graycomatrix", (args, line, col) => ComatrixOutputs(args, 1, line, col, dialect)[0]);

        define("graycoprops", (args, line, col) =>
        {
            ArityRange("graycoprops", args, 1, 2, line, col);
            double[][,] matrices = ComatrixStack("graycoprops", args[0], line, col);
            List<string> wanted = args.Count == 2
                ? ComatrixProperties(args[1], line, col)
                : ["Contrast", "Correlation", "Energy", "Homogeneity"];

            var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
            var columns = new List<JGraph.Data.TableColumn>();
            foreach (string property in wanted)
            {
                var values = new double[matrices.Length];
                for (int k = 0; k < matrices.Length; k++)
                {
                    (double contrast, double correlation, double energy, double homogeneity) =
                        TextureAnalysis.Properties(matrices[k]);
                    values[k] = property switch
                    {
                        "Contrast" => contrast,
                        "Correlation" => correlation,
                        "Energy" => energy,
                        _ => homogeneity,
                    };
                }

                fields[property] = values.Length == 1 ? JgsValue.Number(values[0]) : Numbers(values);
                columns.Add(new JGraph.Data.NumberColumn(property, values));
            }

            // MATLAB's answer is a struct with a field per property; JGS has no field access, so it
            // gets the table it uses everywhere else a measurement comes back named — the same
            // split regionprops already makes.
            return dialect.IsMatlab
                ? JgsValue.Struct(fields)
                : JgsValue.Table(new JGraph.Data.Table(columns));
        });

        // --- Reading values back out -------------------------------------------------------------
        define("impixel", (args, line, col) =>
        {
            Arity("impixel", args, 3, line, col);
            using ImgArg source = ImgLike("impixel", args, 0, line, col);
            double[] columns = NumericVector("impixel", args, 1, line, col);
            double[] rows = NumericVector("impixel", args, 2, line, col);
            if (columns.Length != rows.Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"impixel was given {columns.Length} columns and {rows.Length} rows; " +
                    "they name points, so there must be the same number of each.");
            }

            double shift = dialect.IndexBase;
            var pixelColumns = new double[columns.Length];
            var pixelRows = new double[rows.Length];
            for (int i = 0; i < columns.Length; i++)
            {
                pixelColumns[i] = columns[i] - shift;
                pixelRows[i] = rows[i] - shift;
            }

            double[,] values = Compositing.PixelValues(source.Buffer, pixelColumns, pixelRows);
            return MatrixToRows(ScaleForDialect(values, source.Buffer.Class, dialect));
        });

        define("improfile", (args, line, col) => ImProfileOutputs(args, 1, line, col, dialect)[0]);

        // --- Display composites ------------------------------------------------------------------
        define("imcontour", (args, line, col) =>
        {
            ArityRange("imcontour", args, 1, 2, line, col);
            using ImgArg source = ImgLike("imcontour", args, 0, line, col);
            if (source.Buffer.Channels != 1)
            {
                throw new JgsRuntimeException(line, col,
                    "imcontour draws the level lines of one channel; convert the picture with rgb2gray.");
            }

            double[,] values = ScaleForDialect(
                PointOps.ToMatrix(source.Buffer, 0), source.Buffer.Class, dialect);
            int height = values.GetLength(0);
            int width = values.GetLength(1);

            double[]? levels = null;
            if (args.Count == 2)
            {
                double[] given = NumericVector("imcontour", args, 1, line, col);
                levels = given.Length == 1 ? EvenLevels(values, (int)Math.Round(given[0])) : given;
            }

            double shift = dialect.IndexBase;
            var x = new double[width];
            for (int c = 0; c < width; c++)
            {
                x[c] = c + shift;
            }

            var y = new double[height];
            for (int r = 0; r < height; r++)
            {
                y[r] = r + shift;
            }

            try
            {
                JG.Contour(x, y, values, levels);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"imcontour: {ex.Message}");
            }

            // Contours of a picture belong on picture axes: square pixels, and row one at the top,
            // which is the one thing a plain contour plot gets the other way round.
            AxesModel axes = JG.Gca();
            axes.EqualAspect = true;
            axes.PrimaryYAxis.Inverted = true;
            return JgsValue.Null;
        });

        define("montage", (args, line, col) =>
        {
            ParsedArgs parsed = MontageSpec.Parse(args, positionalMax: 1, line, col);
            if (parsed.Positional.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "montage needs pictures to lay out.");
            }

            List<ImageBuffer> tiles = MontageTiles(parsed.Positional[0], line, col);
            try
            {
                (int gridRows, int gridCols) = parsed.Named("Size") is { } size
                    ? GridOf(size, line, col)
                    : (0, 0);
                int border = (int)parsed.Scalar("BorderSize", 0);
                double[] background = parsed.Named("BackgroundColor") is { } colour
                    ? NumericVector("montage", colour, line, col)
                    : [0];
                (int Height, int Width)? thumbnail = parsed.Window("ThumbnailSize");

                using ImageBuffer sheet = Compositing.Montage(
                    tiles, gridRows, gridCols, border, background, thumbnail);
                sheet.Class = tiles[0].Class;
                Display(sheet, parsed, line, col);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"montage: {ex.Message}");
            }
            finally
            {
                foreach (ImageBuffer tile in tiles)
                {
                    tile.Dispose();
                }
            }

            return JgsValue.Null;
        });

        define("imfuse", (args, line, col) =>
        {
            using ImageBuffer fused = FuseFrom("imfuse", FuseSpec, args, line, col);
            return ImgOut(fused.Clone(), ImageClass.UInt8);
        });

        define("imshowpair", (args, line, col) =>
        {
            using ImageBuffer fused = FuseFrom("imshowpair", ShowPairSpec, args, line, col);
            fused.Class = ImageClass.UInt8;
            Display(fused, null, line, col);
            return JgsValue.Null;
        });

        // --- Preferences -------------------------------------------------------------------------
        define("iptgetpref", (args, line, col) =>
        {
            ArityRange("iptgetpref", args, 0, 1, line, col);
            if (args.Count == 0)
            {
                if (dialect.IsMatlab)
                {
                    var all = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
                    foreach (KeyValuePair<string, JgsValue> pref in ImagePreferences)
                    {
                        all[pref.Key] = pref.Value;
                    }

                    return JgsValue.Struct(all);
                }

                // JGS has no field access, so the whole set comes back as the two-column table a
                // script can actually walk.
                var names = new string[ImagePreferences.Count];
                var settings = new string[ImagePreferences.Count];
                int at = 0;
                foreach (KeyValuePair<string, JgsValue> pref in ImagePreferences)
                {
                    names[at] = pref.Key;
                    settings[at] = pref.Value.Type switch
                    {
                        JgsType.String => pref.Value.AsString,
                        JgsType.Bool => pref.Value.AsBool ? "true" : "false",
                        _ => pref.Value.AsNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    };
                    at++;
                }

                return JgsValue.Table(new JGraph.Data.Table(
                [
                    new JGraph.Data.TextColumn("Name", names),
                    new JGraph.Data.TextColumn("Value", settings),
                ]));
            }

            return ImagePreferences[PreferenceName("iptgetpref", Str("iptgetpref", args, 0, line, col), line, col)];
        });

        define("iptsetpref", (args, line, col) =>
        {
            Arity("iptsetpref", args, 2, line, col);
            string name = PreferenceName("iptsetpref", Str("iptsetpref", args, 0, line, col), line, col);
            ImagePreferences[name] = args[1];
            return JgsValue.Null;
        });
    }

    /// <summary><c>[peaksnr, snr] = psnr(A, ref, peakval)</c>.</summary>
    private static JgsValue[] PsnrOutputs(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col, JgsDialect dialect)
    {
        ArityRange("psnr", args, 2, 3, line, col);
        using ImgArg source = ImgLike("psnr", args, 0, line, col);
        using ImgArg reference = ImgLike("psnr", args, 1, line, col);
        double peak = args.Count == 3
            ? Num("psnr", args, 2, line, col)
            : PeakOf(reference, dialect);

        double[,] a = Stacked(source, dialect);
        double[,] b = Stacked(reference, dialect);
        try
        {
            double ratio = QualityMetrics.PeakSignalToNoise(a, b, peak);
            if (wanted < 2)
            {
                return [JgsValue.Number(ratio)];
            }

            // The second output measures the same error against the reference's own power rather than
            // against the largest value the class can hold, which is the more useful number when the
            // reference is nowhere near full scale.
            double error = QualityMetrics.MeanSquaredError(a, b);
            double power = 0;
            foreach (double value in b)
            {
                power += value * value;
            }

            power /= b.GetLength(0) * (long)b.GetLength(1);
            double signalToNoise = error == 0
                ? double.PositiveInfinity
                : 10 * Math.Log10(power / error);
            return [JgsValue.Number(ratio), JgsValue.Number(signalToNoise)];
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"psnr: {ex.Message}");
        }
    }

    /// <summary><c>[ssimval, ssimmap] = ssim(A, ref, …)</c>.</summary>
    private static JgsValue[] SsimOutputs(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col, JgsDialect dialect)
    {
        ParsedArgs parsed = SsimSpec.Parse(args, positionalMax: 2, line, col);
        if (parsed.Positional.Count != 2)
        {
            throw new JgsRuntimeException(line, col, "ssim compares a picture against a reference.");
        }

        using ImgArg source = ImgLike("ssim", parsed.Positional, 0, line, col);
        using ImgArg reference = ImgLike("ssim", parsed.Positional, 1, line, col);
        var options = new QualityMetrics.SsimOptions(
            parsed.Scalar("DynamicRange", PeakOf(reference, dialect)),
            parsed.Scalar("Radius", 1.5),
            parsed.Vector("Exponents"),
            parsed.Vector("RegularizationConstants"));

        double[][,] a = Planes(source, dialect);
        double[][,] b = Planes(reference, dialect);
        if (a.Length != b.Length)
        {
            throw new JgsRuntimeException(line, col,
                "ssim needs both pictures to have the same number of channels.");
        }

        try
        {
            double total = 0;
            double[][,] maps = new double[a.Length][,];
            for (int ch = 0; ch < a.Length; ch++)
            {
                (double score, double[,] map) = QualityMetrics.StructuralSimilarity(a[ch], b[ch], options);
                total += score;
                maps[ch] = map;
            }

            JgsValue value = JgsValue.Number(total / a.Length);
            return wanted < 2 ? [value] : [value, PlaneStack(maps)];
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"ssim: {ex.Message}");
        }
    }

    /// <summary><c>[score, maps] = multissim(A, ref, …)</c>.</summary>
    private static JgsValue[] MultiSsimOutputs(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col, JgsDialect dialect)
    {
        ParsedArgs parsed = MultiSsimSpec.Parse(args, positionalMax: 2, line, col);
        if (parsed.Positional.Count != 2)
        {
            throw new JgsRuntimeException(line, col, "multissim compares a picture against a reference.");
        }

        using ImgArg source = ImgLike("multissim", parsed.Positional, 0, line, col);
        using ImgArg reference = ImgLike("multissim", parsed.Positional, 1, line, col);
        int scales = (int)parsed.Scalar("NumScales", 5);
        double[]? weights = parsed.Vector("ScaleWeights");
        var options = new QualityMetrics.SsimOptions(
            parsed.Scalar("DynamicRange", PeakOf(reference, dialect)),
            parsed.Scalar("Sigma", 1.5));

        double[][,] a = Planes(source, dialect);
        double[][,] b = Planes(reference, dialect);
        if (a.Length != b.Length)
        {
            throw new JgsRuntimeException(line, col,
                "multissim needs both pictures to have the same number of channels.");
        }

        try
        {
            double total = 0;
            double[][,]? first = null;
            for (int ch = 0; ch < a.Length; ch++)
            {
                (double score, double[][,] maps) = QualityMetrics.MultiScaleSimilarity(
                    a[ch], b[ch], scales, weights, options);
                total += score;
                first ??= maps;
            }

            JgsValue value = JgsValue.Number(total / a.Length);
            if (wanted < 2)
            {
                return [value];
            }

            // The maps are different sizes, one per scale, so a cell is the only thing that holds
            // them; MATLAB returns a cell here for the same reason.
            var cells = new JgsValue[first!.Length];
            for (int k = 0; k < first.Length; k++)
            {
                cells[k] = MatrixToRows(first[k]);
            }

            return [value, JgsValue.Cell(cells)];
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"multissim: {ex.Message}");
        }
    }

    /// <summary><c>[score, precision, recall] = bfscore(prediction, truth, threshold)</c>.</summary>
    private static JgsValue[] BfScoreOutputs(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col, JgsDialect dialect)
    {
        ArityRange("bfscore", args, 2, 3, line, col);
        using ImgArg prediction = ImgLike("bfscore", args, 0, line, col);
        using ImgArg truth = ImgLike("bfscore", args, 1, line, col);
        double[,] a = LabelPlane("bfscore", prediction, dialect, line, col);
        double[,] b = LabelPlane("bfscore", truth, dialect, line, col);
        double threshold = args.Count == 3
            ? Num("bfscore", args, 2, line, col)
            : QualityMetrics.DefaultBoundaryTolerance(a.GetLength(0), a.GetLength(1));

        try
        {
            (double[] score, double[] precision, double[] recall) =
                QualityMetrics.BoundaryFScore(a, b, threshold);
            JgsValue[] outputs = [Scores(score), Scores(precision), Scores(recall)];
            return wanted <= 1 ? [outputs[0]] : outputs[..Math.Min(wanted, 3)];
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"bfscore: {ex.Message}");
        }
    }

    /// <summary><c>[glcms, SI] = graycomatrix(I, …)</c>.</summary>
    private static JgsValue[] ComatrixOutputs(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col, JgsDialect dialect)
    {
        ParsedArgs parsed = ComatrixSpec.Parse(args, positionalMax: 1, line, col);
        if (parsed.Positional.Count != 1)
        {
            throw new JgsRuntimeException(line, col, "graycomatrix needs a picture.");
        }

        using ImgArg source = ImgLike("graycomatrix", parsed.Positional, 0, line, col);
        if (source.Buffer.Channels != 1)
        {
            throw new JgsRuntimeException(line, col,
                "graycomatrix reads one channel; convert the picture with rgb2gray first.");
        }

        double[,] values = ScaleForDialect(
            PointOps.ToMatrix(source.Buffer, 0), source.Buffer.Class, dialect);
        bool binary = source.Buffer.Class == ImageClass.Logical;
        int levels = (int)parsed.Scalar("NumLevels", binary ? 2 : 8);

        (double Low, double High) limits;
        if (parsed.Named("GrayLimits") is { } given)
        {
            double[] pair = NumericVector("graycomatrix", given, line, col);
            if (pair.Length == 0)
            {
                limits = Extremes(values);
            }
            else if (pair.Length == 2)
            {
                limits = (pair[0], pair[1]);
            }
            else
            {
                throw new JgsRuntimeException(line, col,
                    "graycomatrix: 'GrayLimits' is a [low high] pair, or [] for the picture's own range.");
            }
        }
        else
        {
            // An integer picture is quantized against everything its class can hold, so two exposures
            // of the same scene give comparable tables; a floating-point one has no such range and
            // falls back to its own extremes, which is MATLAB's rule as well.
            limits = source.Buffer.Class.IsInteger()
                ? (0, source.Buffer.Class.Scale())
                : Extremes(values);
        }

        var offsets = new List<(int Row, int Col)>();
        if (parsed.Named("Offset") is { } offsetValue)
        {
            double[,] pairs = Rectangle("graycomatrix 'Offset'", offsetValue, line, col);
            if (pairs.GetLength(1) != 2)
            {
                throw new JgsRuntimeException(line, col,
                    "graycomatrix: each offset is a [row col] pair, one per row.");
            }

            for (int r = 0; r < pairs.GetLength(0); r++)
            {
                offsets.Add(((int)Math.Round(pairs[r, 0]), (int)Math.Round(pairs[r, 1])));
            }
        }
        else
        {
            offsets.AddRange(TextureAnalysis.DefaultOffsets);
        }

        bool symmetric = parsed.Flag("Symmetric", false);
        try
        {
            (double[][,] matrices, double[,] scaled) = TextureAnalysis.Comatrix(
                values, levels, limits, offsets, symmetric);
            JgsValue tables = PlaneStack(matrices);
            return wanted < 2 ? [tables] : [tables, MatrixToRows(scaled)];
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"graycomatrix: {ex.Message}");
        }
    }

    /// <summary><c>[cx, cy, c] = improfile(I, xi, yi, n, method)</c>.</summary>
    private static JgsValue[] ImProfileOutputs(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col, JgsDialect dialect)
    {
        ArityRange("improfile", args, 3, 5, line, col);
        using ImgArg source = ImgLike("improfile", args, 0, line, col);
        double[] xs = NumericVector("improfile", args, 1, line, col);
        double[] ys = NumericVector("improfile", args, 2, line, col);

        int samples = 0;
        var method = Compositing.Sampling.Nearest;
        for (int i = 3; i < args.Count; i++)
        {
            if (args[i].Type == JgsType.String)
            {
                method = Str("improfile", args, i, line, col).ToLowerInvariant() switch
                {
                    "nearest" => Compositing.Sampling.Nearest,
                    "bilinear" => Compositing.Sampling.Bilinear,
                    "bicubic" => Compositing.Sampling.Bicubic,
                    _ => throw new JgsRuntimeException(line, col,
                        $"improfile: unknown method '{args[i].AsString}' " +
                        "(use 'nearest', 'bilinear' or 'bicubic')."),
                };
            }
            else
            {
                samples = Count("improfile", args, i, line, col);
            }
        }

        double shift = dialect.IndexBase;
        var pixelX = new double[xs.Length];
        var pixelY = new double[ys.Length];
        for (int i = 0; i < xs.Length; i++)
        {
            pixelX[i] = xs[i] - shift;
        }

        for (int i = 0; i < ys.Length; i++)
        {
            pixelY[i] = ys[i] - shift;
        }

        try
        {
            (double[,] values, double[] sampleX, double[] sampleY) = Compositing.Profile(
                source.Buffer, pixelX, pixelY, samples, method);
            ScaleForDialect(values, source.Buffer.Class, dialect);

            // A grey picture answers with one column, which a script reads as a plain vector; colour
            // answers with one column per channel.
            JgsValue profile = values.GetLength(1) == 1
                ? Numbers(Column(values, 0))
                : MatrixToRows(values);
            if (wanted < 2)
            {
                return [profile];
            }

            for (int i = 0; i < sampleX.Length; i++)
            {
                sampleX[i] += shift;
                sampleY[i] += shift;
            }

            JgsValue[] outputs = [Numbers(sampleX), Numbers(sampleY), profile];
            return outputs[..Math.Min(wanted, 3)];
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"improfile: {ex.Message}");
        }
    }

    /// <summary>Shared body for <c>dice</c> and <c>jaccard</c>.</summary>
    private static JgsValue Similarity(
        string name, IReadOnlyList<JgsValue> args, int line, int col, JgsDialect dialect)
    {
        Arity(name, args, 2, line, col);
        using ImgArg first = ImgLike(name, args, 0, line, col);
        using ImgArg second = ImgLike(name, args, 1, line, col);
        double[,] a = LabelPlane(name, first, dialect, line, col);
        double[,] b = LabelPlane(name, second, dialect, line, col);
        try
        {
            double[] scores = name == "dice"
                ? QualityMetrics.Dice(a, b)
                : QualityMetrics.Jaccard(a, b);
            return Scores(scores);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"{name}: {ex.Message}");
        }
    }

    /// <summary>Builds the fused picture <c>imfuse</c> returns and <c>imshowpair</c> shows.</summary>
    private static ImageBuffer FuseFrom(
        string name, OptionSpec spec, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ParsedArgs parsed = spec.Parse(args, positionalMax: 3, line, col);
        if (parsed.Positional.Count < 2)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs two pictures.");
        }

        using ImgArg first = ImgLike(name, parsed.Positional, 0, line, col);
        using ImgArg second = ImgLike(name, parsed.Positional, 1, line, col);

        Compositing.FuseMethod method = Compositing.FuseMethod.FalseColor;
        if (parsed.Positional.Count == 3)
        {
            string word = Str(name, parsed.Positional, 2, line, col);
            method = word.ToLowerInvariant() switch
            {
                "falsecolor" => Compositing.FuseMethod.FalseColor,
                "blend" => Compositing.FuseMethod.Blend,
                "diff" => Compositing.FuseMethod.Difference,
                "montage" => Compositing.FuseMethod.Montage,
                _ => throw new JgsRuntimeException(line, col,
                    $"{name}: unknown method '{word}' " +
                    "(use 'falsecolor', 'blend', 'diff' or 'montage')."),
            };
        }

        var scaling = Compositing.FuseScaling.Independent;
        if (parsed.Text("Scaling") is { } scalingWord)
        {
            scaling = scalingWord.ToLowerInvariant() switch
            {
                "independent" => Compositing.FuseScaling.Independent,
                "joint" => Compositing.FuseScaling.Joint,
                "none" => Compositing.FuseScaling.None,
                _ => throw new JgsRuntimeException(line, col,
                    $"{name}: 'Scaling' is 'independent', 'joint' or 'none'."),
            };
        }

        int[]? channels = null;
        if (parsed.Vector("ColorChannels") is { } given)
        {
            channels = new int[given.Length];
            for (int i = 0; i < given.Length; i++)
            {
                channels[i] = (int)Math.Round(given[i]);
            }
        }

        try
        {
            return Compositing.Fuse(first.Buffer, second.Buffer, method, scaling, channels);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"{name}: {ex.Message}");
        }
    }

    /// <summary>The pictures a <c>montage</c> call was handed, however they were packed.</summary>
    private static List<ImageBuffer> MontageTiles(JgsValue value, int line, int col)
    {
        var tiles = new List<ImageBuffer>();
        if (value.Type == JgsType.Cell)
        {
            JgsValue[] elements = value.AsCell;
            if (elements.Length == 0)
            {
                throw new JgsRuntimeException(line, col, "montage was given an empty list of pictures.");
            }

            var holder = new List<JgsValue>(1) { JgsValue.Null };
            foreach (JgsValue element in elements)
            {
                holder[0] = element;
                ImgArg arg = ImgLike("montage", holder, 0, line, col);

                // Every tile has to outlive the argument that produced it, and a matrix argument's
                // buffer is owned by that argument — so a matrix is copied and an image is cloned,
                // and the montage owns all of them uniformly.
                tiles.Add(arg.Buffer.Clone());
                arg.Dispose();
            }

            return tiles;
        }

        if (value.Type == JgsType.Image)
        {
            tiles.Add(value.AsImage.Clone());
            return tiles;
        }

        // JGS has no cell literal, so a list of pictures there is an array whose elements are
        // themselves pictures. A plain matrix's elements are numbers, so the two never collide.
        if (value.Type == JgsType.Array && value.BoxedElements() is [{ Type: JgsType.Array or JgsType.Image }, ..] nested)
        {
            var one = new List<JgsValue>(1) { JgsValue.Null };
            foreach (JgsValue element in nested)
            {
                one[0] = element;
                ImgArg arg = ImgLike("montage", one, 0, line, col);
                tiles.Add(arg.Buffer.Clone());
                arg.Dispose();
            }

            return tiles;
        }

        int[] dims = JgsMatrix.DimsOf(value);
        if (dims.Length is 3 or 4)
        {
            int height = dims[0];
            int width = dims[1];
            int channels = dims.Length == 4 ? dims[2] : 1;
            int count = dims.Length == 4 ? dims[3] : dims[2];
            if (channels is not (1 or 3))
            {
                throw new JgsRuntimeException(line, col,
                    $"montage: a stack has one or three channels, not {channels}.");
            }

            long page = (long)height * width;
            for (int k = 0; k < count; k++)
            {
                var tile = new ImageBuffer(height, width, channels);
                for (int ch = 0; ch < channels; ch++)
                {
                    long offset = ((k * channels) + ch) * page;
                    for (int c = 0; c < width; c++)
                    {
                        for (int r = 0; r < height; r++)
                        {
                            tile[r, c, ch] = value.ElementAt((int)(offset + r + ((long)c * height))).AsNumber;
                        }
                    }
                }

                tiles.Add(tile);
            }

            return tiles;
        }

        var single = new List<JgsValue>(1) { value };
        ImgArg only = ImgLike("montage", single, 0, line, col);
        tiles.Add(only.Buffer.Clone());
        only.Dispose();
        return tiles;
    }

    /// <summary>Shows a composite the way <c>imshow</c> would, honouring a display range if one was given.</summary>
    private static void Display(ImageBuffer image, ParsedArgs? parsed, int line, int col)
    {
        (double low, double high) = (0.0, 1.0);
        if (parsed?.Named("DisplayRange") is { } given)
        {
            double[] range = ToDoubles("montage", given, line, col);
            if (range.Length == 0)
            {
                (low, high) = SampleRange(image);
            }
            else if (range.Length == 2)
            {
                low = image.Class.FromNative(range[0]);
                high = image.Class.FromNative(range[1]);
            }
            else
            {
                throw new JgsRuntimeException(line, col,
                    "montage: 'DisplayRange' is a [low high] pair, or [] for the pictures' own range.");
            }
        }

        if (image.Channels == 1)
        {
            ImagePlot plot = JG.Image(ToScalarField(image));
            plot.Colormap = Colormap.Grayscale;
            plot.AutoScaleColor = false;
            plot.ColorMin = low;
            plot.ColorMax = high;
            plot.Interpolate = false;
        }
        else
        {
            JG.RgbImage(ToArgb(image), image.Width, image.Height);
        }

        StyleImageAxes(JG.Gca());
    }

    /// <summary>The channels of a picture, each in the units the dialect quotes, or raw when null.</summary>
    private static double[][,] Planes(ImgArg source, JgsDialect? dialect)
    {
        ImageBuffer image = source.Buffer;
        var planes = new double[image.Channels][,];
        for (int ch = 0; ch < image.Channels; ch++)
        {
            double[,] plane = PointOps.ToMatrix(image, ch);
            planes[ch] = dialect is null ? plane : ScaleForDialect(plane, image.Class, dialect);
        }

        return planes;
    }

    /// <summary>
    /// Every channel end to end as one tall matrix, which is how a metric defined over "all the
    /// samples" reaches a colour picture without pretending the channels are neighbours in space.
    /// </summary>
    private static double[,] Stacked(ImgArg source, JgsDialect? dialect)
    {
        double[][,] planes = Planes(source, dialect);
        if (planes.Length == 1)
        {
            return planes[0];
        }

        int height = planes[0].GetLength(0);
        int width = planes[0].GetLength(1);
        var stacked = new double[height * planes.Length, width];
        for (int p = 0; p < planes.Length; p++)
        {
            for (int r = 0; r < height; r++)
            {
                for (int c = 0; c < width; c++)
                {
                    stacked[(p * height) + r, c] = planes[p][r, c];
                }
            }
        }

        return stacked;
    }

    /// <summary>A summary applied across every channel and averaged.</summary>
    private static double Across(ImgArg source, JgsDialect dialect, Func<double[,], double> summary) =>
        summary(Stacked(source, dialect));

    /// <summary>The single channel an overlap measure reads, refusing a colour picture outright.</summary>
    private static double[,] LabelPlane(
        string name, ImgArg source, JgsDialect dialect, int line, int col)
    {
        if (source.Buffer.Channels != 1)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} compares masks or label maps, which have one value per pixel; " +
                "this picture has three.");
        }

        return ScaleForDialect(PointOps.ToMatrix(source.Buffer, 0), source.Buffer.Class, dialect);
    }

    /// <summary>The largest value a picture's class can hold, in the units the dialect quotes.</summary>
    private static double PeakOf(ImgArg source, JgsDialect dialect) =>
        dialect.IsMatlab && source.Buffer.Class.IsInteger() ? source.Buffer.Class.Scale() : 1.0;

    /// <summary>One score is a number; several are a vector, one per label.</summary>
    private static JgsValue Scores(double[] values) =>
        values.Length == 1 ? JgsValue.Number(values[0]) : Numbers(values);

    /// <summary>A stack of same-size planes as an <c>h×w</c> matrix or an <c>h×w×k</c> array.</summary>
    private static JgsValue PlaneStack(double[][,] planes)
    {
        if (planes.Length == 1)
        {
            return MatrixToRows(planes[0]);
        }

        int height = planes[0].GetLength(0);
        int width = planes[0].GetLength(1);
        var flat = new double[(long)height * width * planes.Length];
        for (int k = 0; k < planes.Length; k++)
        {
            long page = (long)k * height * width;
            for (int c = 0; c < width; c++)
            {
                for (int r = 0; r < height; r++)
                {
                    flat[page + r + ((long)c * height)] = planes[k][r, c];
                }
            }
        }

        return JgsMatrix.FromColumnMajorDims(flat, [height, width, planes.Length]);
    }

    /// <summary>Reads the co-occurrence tables <c>graycoprops</c> was handed, one or a stack of them.</summary>
    private static double[][,] ComatrixStack(string name, JgsValue value, int line, int col)
    {
        int[] dims = JgsMatrix.DimsOf(value);
        if (dims.Length == 3)
        {
            int levels = dims[0];
            if (dims[1] != levels)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: a co-occurrence matrix is square, but this one is " +
                    $"{dims[0]}-by-{dims[1]}.");
            }

            var stack = new double[dims[2]][,];
            long page = (long)levels * levels;
            for (int k = 0; k < dims[2]; k++)
            {
                var matrix = new double[levels, levels];
                for (int c = 0; c < levels; c++)
                {
                    for (int r = 0; r < levels; r++)
                    {
                        matrix[r, c] = value.ElementAt((int)((k * page) + r + ((long)c * levels))).AsNumber;
                    }
                }

                stack[k] = matrix;
            }

            return stack;
        }

        return [Rectangle($"{name} argument 1", value, line, col)];
    }

    /// <summary>The property names <c>graycoprops</c> was asked for.</summary>
    private static List<string> ComatrixProperties(JgsValue value, int line, int col)
    {
        var words = new List<string>();
        if (value.Type == JgsType.String)
        {
            words.Add(value.AsString);
        }
        else if (value.Type == JgsType.Cell)
        {
            foreach (JgsValue element in value.AsCell)
            {
                if (element.Type != JgsType.String)
                {
                    throw new JgsRuntimeException(line, col, "graycoprops: the properties are words.");
                }

                words.Add(element.AsString);
            }
        }
        else
        {
            throw new JgsRuntimeException(line, col,
                "graycoprops: the properties are a word or a list of words.");
        }

        var resolved = new List<string>();
        foreach (string word in words)
        {
            if (word.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                resolved.Clear();
                resolved.AddRange(["Contrast", "Correlation", "Energy", "Homogeneity"]);
                return resolved;
            }

            string? match = new[] { "Contrast", "Correlation", "Energy", "Homogeneity" }
                .FirstOrDefault(p => p.Equals(word, StringComparison.OrdinalIgnoreCase));
            resolved.Add(match ?? throw new JgsRuntimeException(line, col,
                $"graycoprops: unknown property '{word}' " +
                "(the four are 'Contrast', 'Correlation', 'Energy' and 'Homogeneity')."));
        }

        return resolved;
    }

    private static string PreferenceName(string builtin, string given, int line, int col)
    {
        foreach (string known in ImagePreferences.Keys)
        {
            if (known.Equals(given, StringComparison.OrdinalIgnoreCase))
            {
                return known;
            }
        }

        throw new JgsRuntimeException(line, col,
            $"{builtin}: '{given}' is not an image-processing preference " +
            $"(they are {string.Join(", ", ImagePreferences.Keys.Select(k => $"'{k}'"))}).");
    }

    private static (int Rows, int Cols) GridOf(JgsValue value, int line, int col)
    {
        double[] size = NumericVector("montage", value, line, col);
        if (size.Length != 2)
        {
            throw new JgsRuntimeException(line, col,
                "montage: 'Size' is a [rows cols] pair; either may be NaN to be worked out.");
        }

        int rows = double.IsNaN(size[0]) ? 0 : (int)Math.Round(size[0]);
        int cols = double.IsNaN(size[1]) ? 0 : (int)Math.Round(size[1]);
        return (Math.Max(0, rows), Math.Max(0, cols));
    }

    private static (double Low, double High) Extremes(double[,] values)
    {
        double low = double.PositiveInfinity;
        double high = double.NegativeInfinity;
        foreach (double value in values)
        {
            if (value < low) { low = value; }
            if (value > high) { high = value; }
        }

        return double.IsInfinity(low) ? (0, 1) : (low, high);
    }

    /// <summary>Evenly spaced contour levels across a picture's range, the way <c>contour(Z, n)</c> reads.</summary>
    private static double[] EvenLevels(double[,] values, int count)
    {
        (double low, double high) = Extremes(values);
        int n = Math.Max(1, count);
        var levels = new double[n];
        for (int i = 0; i < n; i++)
        {
            levels[i] = low + ((high - low) * (i + 1) / (n + 1));
        }

        return levels;
    }

    private static double[] Column(double[,] values, int column)
    {
        var result = new double[values.GetLength(0)];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = values[i, column];
        }

        return result;
    }
}
