using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Imaging;
using JGraph.Imaging.Codecs;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M24 image-processing builtins: file IO (<c>imread</c>/<c>imwrite</c>), display (<c>imshow</c>),
/// point/geometry/histogram operations, and — in wave B — filtering, edges, morphology, and region
/// analysis. Images are carried as <see cref="JgsType.Image"/> values wrapping an
/// <see cref="ImageBuffer"/>; every builtin returns a freshly allocated buffer (the run-end sweep in
/// <see cref="JgsRunner"/> disposes each image value exactly once, so aliasing one buffer into two
/// values must never happen).
/// </summary>
internal static partial class JgsBuiltins
{
    private static void DefineImagingBuiltins(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> define,
        JGraphScriptGlobals host,
        Random random,
        JgsDialect dialect)
    {
        // --- File IO -------------------------------------------------------------------------
        define("imread", (args, line, col) =>
        {
            ArityRange("imread", args, 1, 2, line, col);
            string path = host.Resolve(Str("imread", args, 0, line, col));
            int frame = args.Count == 2 ? Count("imread", args, 1, line, col) - dialect.IndexBase : 0;
            try
            {
                return JgsValue.Image(ImageCodec.Read(path, frame));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                          or InvalidDataException or ArgumentOutOfRangeException)
            {
                throw new JgsRuntimeException(line, col, $"imread: cannot read '{path}': {ex.Message}");
            }
        });

        define("imfinfo", (args, line, col) =>
        {
            Arity("imfinfo", args, 1, line, col);
            string path = host.Resolve(Str("imfinfo", args, 0, line, col));
            try
            {
                using ImageBuffer probe = ImageCodec.Read(path);
                var info = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
                {
                    ["Filename"] = JgsValue.Str(Path.GetFullPath(path)),
                    ["FileSize"] = JgsValue.Number(new FileInfo(path).Length),
                    ["Format"] = JgsValue.Str(Path.GetExtension(path).TrimStart('.').ToUpperInvariant()),
                    ["Width"] = JgsValue.Number(probe.Width),
                    ["Height"] = JgsValue.Number(probe.Height),
                    ["BitDepth"] = JgsValue.Number(
                        (probe.Class == ImageClass.UInt16 ? 16 : 8) * probe.Channels),
                    ["ColorType"] = JgsValue.Str(probe.Channels == 1 ? "grayscale" : "truecolor"),
                };
                return JgsValue.Struct(info);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                throw new JgsRuntimeException(line, col, $"imfinfo: cannot read '{path}': {ex.Message}");
            }
        });

        define("imwrite", (args, line, col) =>
        {
            ImgArgs parsed = WriteSpec.Parse(args, positionalMax: 3, line, col);
            if (parsed.Positional.Count < 2)
            {
                throw new JgsRuntimeException(line, col, "imwrite needs an image and a path.");
            }

            ImageBuffer image = Img("imwrite", parsed.Positional, 0, line, col);
            string path = host.ResolveForWrite(Str("imwrite", parsed.Positional, 1, line, col));

            // The pre-M46 positional third argument was the JPEG quality; 'Quality' is MATLAB's spelling
            // and both work, because JGS scripts in the wild use the short form.
            int? quality = parsed.Positional.Count == 3
                ? Count("imwrite", parsed.Positional, 2, line, col)
                : null;
            double q = parsed.Scalar("Quality", double.NaN);
            if (!double.IsNaN(q))
            {
                quality = (int)Math.Round(q);
            }

            double depth = parsed.Scalar("BitDepth", double.NaN);
            ImageBuffer? alpha = parsed.Named("Alpha") is { Type: JgsType.Image } a ? a.AsImage : null;
            try
            {
                ImageCodec.Write(path, image, new CodecWriteOptions(
                    quality, double.IsNaN(depth) ? null : (int)Math.Round(depth), alpha));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
            {
                throw new JgsRuntimeException(line, col, $"imwrite: cannot write '{path}': {ex.Message}");
            }

            return JgsValue.Null;
        });

        // --- Display -------------------------------------------------------------------------
        define("imshow", (args, line, col) =>
        {
            ImgArgs parsed = ShowSpec.Parse(args, positionalMax: 2, line, col);
            if (parsed.Positional.Count == 0 || parsed.Positional[0].Type != JgsType.Image)
            {
                JgsValue given = parsed.Positional.Count > 0 ? parsed.Positional[0] : JgsValue.Null;
                throw new JgsRuntimeException(line, col,
                    given.Type == JgsType.Array
                        ? "imshow displays an image value; for a numeric matrix use imagesc."
                        : $"imshow expects an image, but got a {given.TypeName}.");
            }

            ImageBuffer image = parsed.Positional[0].AsImage;

            // imshow(I, [low high]) and imshow(I, []) set the display window. The limits are quoted in
            // the image's own class, so a uint8 picture takes [0 255] — normalize before use.
            (double low, double high) = (0.0, 1.0);
            if (parsed.Positional.Count == 2)
            {
                double[] range = ToDoubles("imshow", parsed.Positional[1], line, col);
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
                        "imshow display range must be [low high], or [] to use the image's own range.");
                }
            }
            else if (parsed.Named("DisplayRange") is { } named)
            {
                double[] range = ToDoubles("imshow", named, line, col);
                if (range.Length != 2)
                {
                    throw new JgsRuntimeException(line, col, "imshow: 'DisplayRange' takes [low high].");
                }

                low = image.Class.FromNative(range[0]);
                high = image.Class.FromNative(range[1]);
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
            return JgsValue.Null;
        });

        // --- Colour + matrix bridging --------------------------------------------------------
        define("rgb2gray", (args, line, col) =>
        {
            Arity("rgb2gray", args, 1, line, col);

            // MATLAB's rgb2gray takes a colormap as readily as a picture, and answers with a
            // colormap — the same conversion applied to three columns instead of three planes.
            if (args[0].Type != JgsType.Image)
            {
                return MatrixToRows(
                    IndexedImages.ColormapToGray(ColormapRows("rgb2gray", args, 0, line, col)));
            }

            ImageBuffer image = args[0].AsImage;
            if (image.Channels != 3)
            {
                throw new JgsRuntimeException(line, col, "rgb2gray expects an RGB image; a grayscale image is already gray.");
            }

            return ImgOut(PointOps.ToGray(image), image);
        });

        define("im2gray", (args, line, col) =>
        {
            Arity("im2gray", args, 1, line, col);
            ImageBuffer source = Img("im2gray", args, 0, line, col);
            return ImgOut(PointOps.ToGray(source), source);
        });

        define("mat2im", (args, line, col) =>
        {
            Arity("mat2im", args, 1, line, col);
            return ImgOut(PointOps.FromMatrix(Matrix("mat2im", args, 0, line, col)), ImageClass.Double);
        });

        define("mat2gray", (args, line, col) =>
        {
            ArityRange("mat2gray", args, 1, 2, line, col);
            double[,] values = Matrix("mat2gray", args, 0, line, col);
            if (args.Count == 1)
            {
                return ImgOut(PointOps.Normalize(values), ImageClass.Double);
            }

            (double low, double high) = Pair("mat2gray", args, 1, line, col);
            if (high <= low)
            {
                throw new JgsRuntimeException(line, col, "mat2gray limits must have amax > amin.");
            }

            return ImgOut(PointOps.Normalize(values, low, high), ImageClass.Double);
        });

        define("im2mat", (args, line, col) =>
        {
            ArityRange("im2mat", args, 1, 2, line, col);
            ImageBuffer image = Img("im2mat", args, 0, line, col);
            int channel = args.Count == 2 ? Count("im2mat", args, 1, line, col) - 1 : 0;
            if ((uint)channel >= (uint)image.Channels)
            {
                throw new JgsRuntimeException(line, col, $"im2mat channel must be in 1..{image.Channels}.");
            }

            const long boxingLimit = 4_000_000;
            if (image.SampleCount > boxingLimit)
            {
                throw new JgsRuntimeException(line, col,
                    $"im2mat would box {image.Height * image.Width} elements; downsample with imresize first.");
            }

            return MatrixToRows(ScaleForDialect(PointOps.ToMatrix(image, channel), image.Class, dialect));
        });

        // --- Class conversion ----------------------------------------------------------------
        // MATLAB's im2* family changes an image's class, rescaling the numbers so the picture looks the
        // same. Here the storage is already normalized, so for an image these are pure re-tags; for a
        // plain matrix they do the arithmetic a MATLAB user expects to see in the workspace.
        void Convert(string name, ImageClass target)
        {
            define(name, (args, line, col) =>
            {
                ArityRange(name, args, 1, 2, line, col);
                if (args[0].Type == JgsType.Image)
                {
                    return ImgOut(args[0].AsImage.Clone(), target);
                }

                double[,] values = Matrix(name, args, 0, line, col);
                int rows = values.GetLength(0);
                int cols = values.GetLength(1);
                var scaled = new double[rows, cols];
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        scaled[r, c] = target.ToNative(Math.Clamp(values[r, c], 0, 1));
                    }
                }

                return MatrixToRows(scaled);
            });
        }

        Convert("im2double", ImageClass.Double);
        Convert("im2single", ImageClass.Single);
        Convert("im2uint8", ImageClass.UInt8);
        Convert("im2uint16", ImageClass.UInt16);
        Convert("im2int16", ImageClass.Int16);

        define("intlut", (args, line, col) =>
        {
            Arity("intlut", args, 2, line, col);
            ImageBuffer image = Img("intlut", args, 0, line, col);
            if (!image.Class.IsInteger())
            {
                throw new JgsRuntimeException(line, col,
                    $"intlut needs an integer-class image; this one is {image.Class.MatlabName()} " +
                    "(convert with im2uint8 first).");
            }

            double[] table = ToDoubles("intlut", args[1], line, col);
            int expected = (int)image.Class.Scale() + 1;
            if (table.Length != expected)
            {
                throw new JgsRuntimeException(line, col,
                    $"intlut needs a {expected}-entry table for a {image.Class.MatlabName()} image.");
            }

            ImageBuffer result = image.Clone();
            Span<double> px = result.Pixels;
            for (int i = 0; i < px.Length; i++)
            {
                int index = (int)Math.Clamp(image.Class.ToNative(px[i]) - image.Class.Offset(), 0, expected - 1);
                px[i] = image.Class.FromNative(table[index]);
            }

            GC.KeepAlive(result);
            return ImgOut(result, image.Class);
        });

        // --- Intensity + histogram -----------------------------------------------------------
        define("imadjust", (args, line, col) =>
        {
            ArityRange("imadjust", args, 1, 4, line, col);
            ImageBuffer image = Img("imadjust", args, 0, line, col);
            (double lowIn, double highIn) = args.Count >= 2
                ? Pair("imadjust", args, 1, line, col)
                : PointOps.StretchLimits(image);
            (double lowOut, double highOut) = args.Count >= 3 ? Pair("imadjust", args, 2, line, col) : (0.0, 1.0);
            double gamma = args.Count >= 4 ? Num("imadjust", args, 3, line, col) : 1.0;
            try
            {
                return ImgOut(PointOps.Adjust(image, lowIn, highIn, lowOut, highOut, gamma), image);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }
        });

        define("imhist", (args, line, col) =>
        {
            ArityRange("imhist", args, 1, 2, line, col);
            ImageBuffer image = Img("imhist", args, 0, line, col);
            int bins = args.Count == 2 ? Count("imhist", args, 1, line, col) : DefaultBins(image);
            try
            {
                return Numbers(Histograms.Histogram(image, bins));
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }
        });

        define("otsuthresh", (args, line, col) =>
        {
            Arity("otsuthresh", args, 1, line, col);
            double[] counts = ToDoubles("otsuthresh", args[0], line, col);
            try
            {
                return JgsValue.Number(Histograms.OtsuFromCounts(counts).Level);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }
        });

        define("stretchlim", (args, line, col) =>
        {
            ArityRange("stretchlim", args, 1, 2, line, col);
            ImageBuffer image = Img("stretchlim", args, 0, line, col);
            double lowFraction = 0.01;
            double highFraction = 0.99;
            if (args.Count == 2)
            {
                double[] tol = ToDoubles("stretchlim", args[1], line, col);
                switch (tol.Length)
                {
                    case 1:
                        lowFraction = tol[0];
                        highFraction = 1.0 - tol[0];
                        break;
                    case 2:
                        lowFraction = tol[0];
                        highFraction = tol[1];
                        break;
                    default:
                        throw new JgsRuntimeException(line, col, "stretchlim tolerance is a fraction or a [low high] pair.");
                }
            }

            (double low, double high) = PointOps.StretchLimits(image, lowFraction, highFraction);
            return JgsMatrix.Build(2, 1, (r, _) => r == 0 ? low : high);
        });

        define("adaptthresh", (args, line, col) =>
        {
            ImgArgs parsed = AdaptThreshSpec.Parse(args, positionalMax: 2, line, col);
            if (parsed.Positional.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "adaptthresh needs an image.");
            }

            ImageBuffer image = Img("adaptthresh", parsed.Positional, 0, line, col);
            double sensitivity = parsed.Positional.Count >= 2
                ? Num("adaptthresh", parsed.Positional, 1, line, col)
                : 0.5;
            string statisticWord = parsed.Text("Statistic") ?? "mean";
            Histograms.LocalStatistic statistic = statisticWord.ToLowerInvariant() switch
            {
                "mean" => Histograms.LocalStatistic.Mean,
                "median" => Histograms.LocalStatistic.Median,
                "gaussian" => Histograms.LocalStatistic.Gaussian,
                _ => throw new JgsRuntimeException(line, col,
                    $"adaptthresh: unknown statistic '{statisticWord}' (use 'mean', 'median', or 'gaussian')."),
            };

            string polarity = parsed.Text("ForegroundPolarity") ?? "bright";
            try
            {
                return ImgOut(
                    Histograms.AdaptiveThreshold(
                        image, sensitivity, parsed.Window("NeighborhoodSize"), statistic,
                        polarity.Equals("dark", StringComparison.OrdinalIgnoreCase)),
                    ImageClass.Double);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"adaptthresh: {ex.Message}");
            }
        });

        define("histeq", (args, line, col) =>
        {
            ArityRange("histeq", args, 1, 2, line, col);
            ImageBuffer image = Img("histeq", args, 0, line, col);
            int bins = args.Count == 2 ? Count("histeq", args, 1, line, col) : 64;
            try
            {
                return ImgOut(Histograms.Equalize(image, bins), image);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }
        });

        define("graythresh", (args, line, col) =>
        {
            Arity("graythresh", args, 1, line, col);
            return JgsValue.Number(Histograms.OtsuLevel(Img("graythresh", args, 0, line, col)));
        });

        define("imbinarize", (args, line, col) =>
        {
            ImgArgs parsed = BinarizeSpec.Parse(args, positionalMax: 2, line, col);
            if (parsed.Positional.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "imbinarize needs an image.");
            }

            ImageBuffer image = Img("imbinarize", parsed.Positional, 0, line, col);
            if (parsed.Has("adaptive"))
            {
                using ImageBuffer thresholds = Histograms.AdaptiveThreshold(
                    image,
                    parsed.Scalar("Sensitivity", 0.5),
                    parsed.Window("NeighborhoodSize"),
                    Histograms.LocalStatistic.Mean,
                    string.Equals(parsed.Text("ForegroundPolarity"), "dark", StringComparison.OrdinalIgnoreCase));
                return ImgOut(Histograms.Binarize(image, thresholds), ImageClass.Logical);
            }

            // A second positional argument is either a global level or a whole threshold surface, which
            // is what adaptthresh hands back — MATLAB accepts both under the same name.
            if (parsed.Positional.Count == 2)
            {
                // The level is normalized in both dialects: MATLAB documents imbinarize's threshold as
                // [0, 1] whatever the image's class, and graythresh hands back exactly that.
                return parsed.Positional[1].Type == JgsType.Image
                    ? ImgOut(Histograms.Binarize(image, parsed.Positional[1].AsImage), ImageClass.Logical)
                    : ImgOut(
                        Histograms.Binarize(image, Num("imbinarize", parsed.Positional, 1, line, col)),
                        ImageClass.Logical);
            }

            return ImgOut(Histograms.Binarize(image, (double?)null), ImageClass.Logical);
        });

        // --- Arithmetic ----------------------------------------------------------------------
        // Clamping to [0, 1] is the normalized form of MATLAB's saturating integer arithmetic, and the
        // output class follows the first image, so uint8 + uint8 stays uint8 and lands on 1/255 steps.
        define("imadd", (args, line, col) =>
            ImageArithmetic("imadd", args, line, col, PointOps.Add, PointOps.AddScalar, dialect, scalarIsLevel: true));
        define("imsubtract", (args, line, col) =>
            ImageArithmetic("imsubtract", args, line, col, PointOps.Subtract, PointOps.SubtractScalar, dialect, scalarIsLevel: true));
        define("immultiply", (args, line, col) =>
            ImageArithmetic("immultiply", args, line, col, PointOps.Multiply, PointOps.MultiplyScalar, dialect, scalarIsLevel: false));
        define("imdivide", (args, line, col) =>
            ImageArithmetic("imdivide", args, line, col, Arithmetic.Divide, Arithmetic.DivideScalar, dialect, scalarIsLevel: false));
        define("imabsdiff", (args, line, col) =>
        {
            Arity("imabsdiff", args, 2, line, col);
            ImageBuffer a = Img("imabsdiff", args, 0, line, col);
            ImageBuffer b = Img("imabsdiff", args, 1, line, col);
            try
            {
                return ImgOut(Arithmetic.AbsoluteDifference(a, b), a);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }
        });

        define("imlincomb", (args, line, col) =>
        {
            if (args.Count < 2)
            {
                throw new JgsRuntimeException(line, col, "imlincomb takes weight, image pairs: imlincomb(k1, A, k2, B).");
            }

            var weights = new List<double>();
            var images = new List<ImageBuffer>();
            for (int i = 0; i < args.Count; i++)
            {
                if (args[i].Type == JgsType.Image)
                {
                    images.Add(args[i].AsImage);
                }
                else
                {
                    weights.Add(Num("imlincomb", args, i, line, col));
                }
            }

            try
            {
                return ImgOut(Arithmetic.LinearCombination(weights, images), images[0]);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"imlincomb: {ex.Message}");
            }
        });

        define("imapplymatrix", (args, line, col) =>
        {
            ArityRange("imapplymatrix", args, 2, 3, line, col);
            double[,] matrix = Matrix("imapplymatrix", args, 0, line, col);
            ImageBuffer image = Img("imapplymatrix", args, 1, line, col);
            double[]? offsets = args.Count == 3 ? ToDoubles("imapplymatrix", args[2], line, col) : null;
            try
            {
                return ImgOut(Arithmetic.ApplyMatrix(matrix, image, offsets), image);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"imapplymatrix: {ex.Message}");
            }
        });

        define("imcomplement", (args, line, col) =>
        {
            Arity("imcomplement", args, 1, line, col);
            ImageBuffer image = Img("imcomplement", args, 0, line, col);
            return ImgOut(PointOps.Complement(image), image);
        });

        define("imnoise", (args, line, col) =>
        {
            ArityRange("imnoise", args, 1, 3, line, col);
            ImageBuffer image = Img("imnoise", args, 0, line, col);
            string kind = args.Count >= 2 ? Str("imnoise", args, 1, line, col).ToLowerInvariant() : "gaussian";
            return kind switch
            {
                "gaussian" => ImgOut(PointOps.GaussianNoise(image, 0.0,
                    args.Count >= 3 ? Num("imnoise", args, 2, line, col) : 0.01, random), image),
                "salt & pepper" or "salt&pepper" or "saltpepper" => ImgOut(PointOps.SaltPepperNoise(image,
                    args.Count >= 3 ? Num("imnoise", args, 2, line, col) : 0.05, random), image),
                _ => throw new JgsRuntimeException(line, col, $"imnoise: unknown noise type '{kind}' (use 'gaussian' or 'salt & pepper')."),
            };
        });

        // --- Geometry ------------------------------------------------------------------------
        define("imresize", (args, line, col) =>
        {
            ArityRange("imresize", args, 1, 10, line, col);
            ImgArgs parsed = ImResizeSpec.Parse(args, 2, line, col);
            if (parsed.Positional.Count < 1)
            {
                throw new JgsRuntimeException(line, col, "imresize(A, scale) needs an image or matrix.");
            }

            using ImgArg source = ImgLike("imresize", parsed.Positional, 0, line, col);
            (Geometry.Interpolation method, bool antialiasByDefault) = ResizeMethod(parsed, line, col);
            bool antialias = parsed.Flag("Antialiasing", antialiasByDefault);

            double[]? scale = parsed.Named("Scale") is { } s
                ? NumericVector("imresize", s, line, col)
                : null;
            double[]? outputSize = parsed.Named("OutputSize") is { } o
                ? NumericVector("imresize", o, line, col)
                : null;
            if (parsed.Positional.Count >= 2)
            {
                double[] target = NumericVector("imresize", parsed.Positional[1], line, col);
                // MATLAB reads one number as a factor and two as the size to land on — never the
                // other way round, so [0.5 0.5] is a half-pixel image and not a halving.
                if (target.Length == 1)
                {
                    scale = target;
                }
                else
                {
                    outputSize = target;
                }
            }

            (int newHeight, int newWidth) = outputSize is not null
                ? SizeTarget(source.Buffer, outputSize, line, col)
                : ScaleTarget(source.Buffer, scale, line, col);
            return ImgLikeOut(
                Geometry.Resize(source.Buffer, newHeight, newWidth, method, antialias), source);
        });

        define("imrotate", (args, line, col) =>
        {
            ArityRange("imrotate", args, 2, 4, line, col);
            ImgArgs parsed = ImRotateSpec.Parse(args, 2, line, col);
            if (parsed.Positional.Count < 2)
            {
                throw new JgsRuntimeException(line, col, "imrotate(A, angle) needs an image and an angle.");
            }

            using ImgArg source = ImgLike("imrotate", parsed.Positional, 0, line, col);
            double degrees = NumericVector("imrotate", parsed.Positional[1], line, col) is [var only]
                ? only
                : throw new JgsRuntimeException(line, col, "imrotate: the angle is one number, in degrees.");

            string word = parsed.OneOf("nearest", "nearest", "bilinear", "linear", "bicubic", "cubic");
            string bbox = parsed.OneOf("loose", "crop", "loose");
            return ImgLikeOut(
                Geometry.Rotate(source.Buffer, degrees, ParseInterpolation(word, line, col), bbox == "loose"),
                source);
        });

        // The MATLAB dialect replaces this with the spatial-coordinate form in
        // RegisterImagingMultiOutputForms; JGS keeps the 0-based pixel rectangle ADR 0028 settled on.
        define("imcrop", (args, line, col) =>
        {
            Arity("imcrop", args, 2, line, col);
            ImageBuffer image = Img("imcrop", args, 0, line, col);
            double[] rect = ToDoubles("imcrop", args[1], line, col);
            if (rect.Length != 4)
            {
                throw new JgsRuntimeException(line, col, "imcrop rect must be [x, y, width, height].");
            }

            return ImgOut(Geometry.Crop(image,
                (int)Math.Round(rect[0]), (int)Math.Round(rect[1]),
                (int)Math.Round(rect[2]), (int)Math.Round(rect[3])), image);
        });

        DefineImagingWaveB(define);
        DefineFilteringBuiltins(define, dialect);
        DefineGeometryBuiltins(define, dialect);
        DefineColorBuiltins(define, dialect);

        // --- Filtering -----------------------------------------------------------------------
        define("imfilter", (args, line, col) =>
        {
            ArityRange("imfilter", args, 2, 8, line, col);
            ImgArgs parsed = ImFilterSpec.Parse(args, 2, line, col);
            if (parsed.Positional.Count < 2)
            {
                throw new JgsRuntimeException(line, col, "imfilter(A, h) needs the array and a kernel.");
            }

            using ImgArg source = ImgLike("imfilter", parsed.Positional, 0, line, col);
            double[,] kernel = Matrix("imfilter", parsed.Positional, 1, line, col);

            Filters.Boundary boundary =
                parsed.Has("replicate") ? Filters.Boundary.Replicate :
                parsed.Has("symmetric") ? Filters.Boundary.Symmetric :
                parsed.Has("circular") ? Filters.Boundary.Circular :
                Filters.Boundary.Zero;
            double padValue = parsed.NumericFlag?.AsNumber ?? 0.0;
            bool convolve = parsed.OneOf("corr", "corr", "conv") == "conv";
            bool full = parsed.OneOf("same", "same", "full") == "full";

            return ImgLikeOut(
                Filters.Filter(source.Buffer, kernel, boundary, padValue, convolve, full), source);
        });

        define("conv2", (args, line, col) =>
        {
            ArityRange("conv2", args, 2, 4, line, col);

            // conv2(u, v, A) is the separable form: the outer product of two vectors is the kernel, and
            // the shape is still taken relative to A, so it is folded into the general path here rather
            // than filtering twice.
            bool separable = args.Count >= 3 && args[2].Type != JgsType.String;
            int shapeIndex = separable ? 3 : 2;
            Conv2Shape shape = args.Count > shapeIndex
                ? ParseConv2Shape(Str("conv2", args, shapeIndex, line, col), line, col)
                : Conv2Shape.Full;

            if (!separable)
            {
                double[,] a = Matrix("conv2", args, 0, line, col);
                double[,] b = Matrix("conv2", args, 1, line, col);
                return MatrixToRows(Filters.Convolve2(a, b, shape));
            }

            double[] u = ToDoubles("conv2", args[0], line, col);
            double[] v = ToDoubles("conv2", args[1], line, col);
            double[,] data = Matrix("conv2", args, 2, line, col);
            var outer = new double[u.Length, v.Length];
            for (int r = 0; r < u.Length; r++)
            {
                for (int c = 0; c < v.Length; c++)
                {
                    outer[r, c] = u[r] * v[c];
                }
            }

            return MatrixToRows(Filters.Convolve2(data, outer, shape));
        });

        define("medfilt2", (args, line, col) =>
        {
            ArityRange("medfilt2", args, 1, 3, line, col);
            ImgArgs parsed = MedFiltSpec.Parse(args, 2, line, col);
            using ImgArg source = ImgLike("medfilt2", parsed.Positional, 0, line, col);
            (int mh, int mw) = parsed.Positional.Count >= 2
                ? WindowOf("medfilt2", parsed.Positional[1], line, col)
                : (3, 3);
            Filters.Boundary boundary =
                parsed.Has("symmetric") ? Filters.Boundary.Symmetric : Filters.Boundary.Zero;
            return ImgLikeOut(Filters.Median(source.Buffer, mh, mw, boundary), source);
        });

        define("fspecial", (args, line, col) =>
        {
            ArityRange("fspecial", args, 1, 3, line, col);
            string type = Str("fspecial", args, 0, line, col).ToLowerInvariant();
            (int rows, int cols) Size(int fallback) => args.Count >= 2
                ? WindowOf("fspecial", args[1], line, col)
                : (fallback, fallback);

            double[,] kernel = type switch
            {
                "average" => Sized(Size(3), Kernels.Average),
                "gaussian" => Sized(Size(3), (r, c) =>
                    Kernels.Gaussian(r, c, args.Count >= 3 ? Num("fspecial", args, 2, line, col) : 0.5)),
                "sobel" => Kernels.Sobel(),
                "prewitt" => Kernels.Prewitt(),
                "laplacian" => Kernels.Laplacian(args.Count >= 2 ? Num("fspecial", args, 1, line, col) : 0.2),
                "disk" => Kernels.Disk(args.Count >= 2 ? Count("fspecial", args, 1, line, col) : 5),
                "log" => Sized(Size(5), (r, c) =>
                    Kernels.LaplacianOfGaussian(r, c, args.Count >= 3 ? Num("fspecial", args, 2, line, col) : 0.5)),
                "motion" => Kernels.Motion(
                    args.Count >= 2 ? Num("fspecial", args, 1, line, col) : 9,
                    args.Count >= 3 ? Num("fspecial", args, 2, line, col) : 0),
                "unsharp" => Kernels.Unsharp(args.Count >= 2 ? Num("fspecial", args, 1, line, col) : 0.2),
                _ => throw new JgsRuntimeException(line, col,
                    $"fspecial: unknown filter '{type}' (use average, gaussian, sobel, prewitt, laplacian, " +
                    "disk, log, motion, or unsharp)."),
            };

            return MatrixToRows(kernel);
        });
    }

    private static readonly ImgOptionSpec ImFilterSpec = new(
        "imfilter",
        ["replicate", "symmetric", "circular", "corr", "conv", "same", "full"],
        [],
        AllowNumericFlag: true);

    private static readonly ImgOptionSpec MedFiltSpec = new("medfilt2", ["zeros", "symmetric"], []);

    private static double[,] Sized((int Rows, int Cols) size, Func<int, int, double[,]> build) =>
        build(size.Rows, size.Cols);

    private static Conv2Shape ParseConv2Shape(string shape, int line, int col) =>
        shape.ToLowerInvariant() switch
        {
            "full" => Conv2Shape.Full,
            "same" => Conv2Shape.Same,
            "valid" => Conv2Shape.Valid,
            _ => throw new JgsRuntimeException(line, col, $"conv2: unknown shape '{shape}' (use full, same, or valid)."),
        };

    private static void DefineImagingWaveB(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> define)
    {
        // --- Edge detection ------------------------------------------------------------------
        define("edge", (args, line, col) => EdgeOutputs(args, 1, line, col)[0]);

        define("imgradientxy", (args, line, col) =>
        {
            ArityRange("imgradientxy", args, 1, 2, line, col);
            using ImgArg source = ImgLike("imgradientxy", args, 0, line, col);
            Gradients.Operator op = args.Count == 2
                ? ParseGradientOperator("imgradientxy", Str("imgradientxy", args, 1, line, col), line, col)
                : Gradients.Operator.Sobel;
            (ImageBuffer gx, ImageBuffer gy) = Gradients.GradientXY(source.Buffer, op);
            return JgsValue.Array([ImgOut(gx, ImageClass.Double), ImgOut(gy, ImageClass.Double)]);
        });

        define("imgradient", (args, line, col) =>
        {
            ArityRange("imgradient", args, 1, 2, line, col);

            // imgradient(Gx, Gy) hands over components a script computed itself; imgradient(I, method)
            // computes them. The two are told apart by the second argument, exactly as MATLAB does it.
            if (args.Count == 2 && args[1].Type != JgsType.String)
            {
                using ImgArg gxArg = ImgLike("imgradient", args, 0, line, col);
                using ImgArg gyArg = ImgLike("imgradient", args, 1, line, col);
                try
                {
                    (ImageBuffer m, ImageBuffer d) = Gradients.FromComponents(gxArg.Buffer, gyArg.Buffer);
                    return JgsValue.Array([ImgOut(m, ImageClass.Double), ImgOut(d, ImageClass.Double)]);
                }
                catch (ArgumentException ex)
                {
                    throw new JgsRuntimeException(line, col, ex.Message);
                }
            }

            using ImgArg source = ImgLike("imgradient", args, 0, line, col);
            Gradients.Operator op = args.Count == 2
                ? ParseGradientOperator("imgradient", Str("imgradient", args, 1, line, col), line, col)
                : Gradients.Operator.Sobel;
            (ImageBuffer magnitude, ImageBuffer direction) = Gradients.Gradient(source.Buffer, op);
            return JgsValue.Array([ImgOut(magnitude, ImageClass.Double), ImgOut(direction, ImageClass.Double)]);
        });

        // --- Morphology ----------------------------------------------------------------------
        define("strel", (args, line, col) =>
        {
            ArityRange("strel", args, 1, 2, line, col);
            string shape = Str("strel", args, 0, line, col).ToLowerInvariant();
            int size = args.Count == 2 ? Count("strel", args, 1, line, col) : (shape == "disk" ? 1 : 3);
            bool[,] element = shape switch
            {
                "square" => Morphology.Square(size),
                "disk" => Morphology.Disk(size),
                _ => throw new JgsRuntimeException(line, col, $"strel: unknown shape '{shape}' (use 'square' or 'disk')."),
            };

            return MatrixToRows(ElementToMatrix(element));
        });

        define("imerode", (args, line, col) => Morph("imerode", args, line, col, Morphology.Erode));
        define("imdilate", (args, line, col) => Morph("imdilate", args, line, col, Morphology.Dilate));
        define("imopen", (args, line, col) => Morph("imopen", args, line, col, Morphology.Open));
        define("imclose", (args, line, col) => Morph("imclose", args, line, col, Morphology.Close));

        // --- Region analysis -----------------------------------------------------------------
        define("bwlabel", (args, line, col) =>
        {
            ArityRange("bwlabel", args, 1, 2, line, col);
            ImageBuffer image = Img("bwlabel", args, 0, line, col);
            int connectivity = args.Count == 2 ? Count("bwlabel", args, 1, line, col) : 8;
            try
            {
                (int[,] labels, int count) = Regions.Label(image, connectivity);
                return JgsValue.Array([ImgOut(Regions.LabelsToImage(labels), ImageClass.Double), JgsValue.Number(count)]);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }
        });

        // --- Hough line detection ------------------------------------------------------------
        define("hough", (args, line, col) =>
        {
            Arity("hough", args, 1, line, col);
            ImageBuffer image = Img("hough", args, 0, line, col);
            try
            {
                (ImageBuffer accumulator, double[] theta, double[] rho) = HoughTransform.Accumulate(image);
                return JgsValue.Array([ImgOut(accumulator, ImageClass.Double), Numbers(theta), Numbers(rho)]);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"hough: {ex.Message}");
            }
        });

        define("houghpeaks", (args, line, col) =>
        {
            ArityRange("houghpeaks", args, 1, 4, line, col);
            ImageBuffer accumulator = Img("houghpeaks", args, 0, line, col);
            int count = args.Count >= 2 ? Count("houghpeaks", args, 1, line, col) : 1;
            double? threshold = args.Count >= 3 ? Num("houghpeaks", args, 2, line, col) : null;
            int origin = args.Count >= 4 ? IndexOrigin("houghpeaks", args, 3, line, col) : 0;
            (int RhoIndex, int ThetaIndex)[] peaks = HoughTransform.Peaks(accumulator, count, threshold);

            // One [rhoIndex, thetaIndex] row per peak, 0-based so the indices address rho and theta
            // directly (ADR 0028); pass a base of 1 for MATLAB numbering.
            return JgsMatrix.Build(peaks.Length, 2,
                (i, c) => (c == 0 ? peaks[i].RhoIndex : peaks[i].ThetaIndex) + origin);
        });

        define("houghlines", (args, line, col) =>
        {
            ArityRange("houghlines", args, 4, 6, line, col);
            ImageBuffer image = Img("houghlines", args, 0, line, col);
            double[] theta = ToDoubles("houghlines", args[1], line, col);
            double[] rho = ToDoubles("houghlines", args[2], line, col);
            (int, int)[] peaks = PeakIndices(args[3], line, col);
            double fillGap = args.Count >= 5 ? Num("houghlines", args, 4, line, col) : 20;
            double minLength = args.Count >= 6 ? Num("houghlines", args, 5, line, col) : 40;

            try
            {
                return JgsValue.Table(LineSegmentsToTable(
                    HoughTransform.Lines(image, theta, rho, peaks, fillGap, minLength)));
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new JgsRuntimeException(line, col, $"houghlines: {ex.Message}");
            }
        });

        define("imfill", (args, line, col) =>
        {
            ArityRange("imfill", args, 1, 2, line, col);
            ImageBuffer image = Img("imfill", args, 0, line, col);
            string mode = args.Count == 2 ? Str("imfill", args, 1, line, col).ToLowerInvariant() : "holes";
            if (mode != "holes")
            {
                throw new JgsRuntimeException(line, col, "imfill only supports the 'holes' mode.");
            }

            try
            {
                return ImgOut(Regions.FillHoles(image), ImageClass.Logical);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"imfill: {ex.Message}");
            }
        });

        define("bwareaopen", (args, line, col) =>
        {
            ArityRange("bwareaopen", args, 2, 3, line, col);
            ImageBuffer image = Img("bwareaopen", args, 0, line, col);
            int minArea = Count("bwareaopen", args, 1, line, col);
            int connectivity = args.Count == 3 ? Count("bwareaopen", args, 2, line, col) : 8;
            try
            {
                return ImgOut(Regions.AreaOpen(image, minArea, connectivity), ImageClass.Logical);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }
        });

        define("regionprops", (args, line, col) =>
        {
            ArityRange("regionprops", args, 1, 2, line, col);
            ImageBuffer labelImage = Img("regionprops", args, 0, line, col);

            // MATLAB accepts a binary image directly, so a 0/1 input is labeled here (8-connectivity,
            // matching bwlabel's default). A one-region label map is indistinguishable from binary,
            // but labeling it is a no-op, so the result is the same either way.
            int[,] labels;
            int count;
            if (labelImage.IsBinary)
            {
                (labels, count) = Regions.Label(labelImage, 8);
            }
            else
            {
                labels = Regions.ImageToLabels(labelImage);
                count = 0;
                foreach (int label in labels)
                {
                    count = Math.Max(count, label);
                }
            }

            ImageBuffer? intensity = args.Count == 2 ? Img("regionprops", args, 1, line, col) : null;
            try
            {
                return JgsValue.Table(RegionPropertiesToTable(
                    Regions.Measure(labels, count, intensity), intensity is not null));
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"regionprops: {ex.Message}");
            }
        });

        define("imcentroid", (args, line, col) =>
        {
            ArityRange("imcentroid", args, 1, 2, line, col);
            ImageBuffer image = Img("imcentroid", args, 0, line, col);

            // With a mask, weigh only what the mask keeps — the whole-image counterpart of
            // regionprops' WeightedCentroid, and immune to how many blobs the mask happens to have.
            ImageBuffer? masked = null;
            try
            {
                if (args.Count == 2)
                {
                    masked = PointOps.Multiply(image, Img("imcentroid", args, 1, line, col));
                }

                (double x, double y) = Regions.WeightedCentroid(masked ?? image);
                return JgsValue.Array([JgsValue.Number(x), JgsValue.Number(y)]);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"imcentroid: {ex.Message}");
            }
            finally
            {
                masked?.Dispose();
            }
        });
    }

    private static JgsValue Morph(
        string name, IReadOnlyList<JgsValue> args, int line, int col,
        Func<ImageBuffer, bool[,], ImageBuffer> op)
    {
        ArityRange(name, args, 1, 2, line, col);
        ImageBuffer image = Img(name, args, 0, line, col);
        bool[,] element = args.Count == 2
            ? Morphology.ToElement(Matrix(name, args, 1, line, col))
            : Morphology.Square(3);
        return ImgOut(op(image, element), image);
    }

    /// <summary>
    /// The body of <c>edge</c>, shared by the plain call and the <c>[BW, threshOut] = edge(...)</c>
    /// form the MATLAB dialect registers.
    /// </summary>
    /// <remarks>
    /// The fourth argument is a direction word for the gradient methods and a smoothing width for
    /// Canny and LoG, which is MATLAB's own overloading; it is read by type rather than by position so
    /// both spellings work without the caller declaring which they meant.
    /// </remarks>
    private static JgsValue[] EdgeOutputs(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("edge", args, 1, 5, line, col);
        using ImgArg source = ImgLike("edge", args, 0, line, col);
        EdgeDetection.Method method = args.Count >= 2
            ? ParseEdgeMethod(Str("edge", args, 1, line, col), line, col)
            : EdgeDetection.Method.Sobel;

        double? high = null;
        double? low = null;
        if (args.Count >= 3 && args[2].Type != JgsType.String)
        {
            double[] levels = NumericVector("edge", args[2], line, col);
            switch (levels.Length)
            {
                case 0: break; // edge(I, 'canny', []) asks for the automatic thresholds
                case 1: high = levels[0]; break;
                case 2: low = levels[0]; high = levels[1]; break;
                default:
                    throw new JgsRuntimeException(line, col,
                        "edge: the threshold is one number or a [low high] pair.");
            }
        }

        double? sigma = null;
        var direction = EdgeDetection.Direction.Both;
        for (int i = 3; i < args.Count; i++)
        {
            if (args[i].Type == JgsType.Number)
            {
                sigma = args[i].AsNumber;
                continue;
            }

            string word = Str("edge", args, i, line, col).ToLowerInvariant();
            switch (word)
            {
                case "horizontal": direction = EdgeDetection.Direction.Horizontal; break;
                case "vertical": direction = EdgeDetection.Direction.Vertical; break;
                case "both": direction = EdgeDetection.Direction.Both; break;

                // Thinning is always on here: the non-maximum suppression Canny needs is not optional
                // in this implementation, and the gradient methods have no separate thinning pass to
                // switch off. The words are accepted so a MATLAB script runs, and 'nothinning' is the
                // one that does nothing rather than silently changing the result.
                case "thinning":
                case "nothinning": break;
                default:
                    throw new JgsRuntimeException(line, col,
                        $"edge: unknown option '{word}' (use 'horizontal', 'vertical', 'both', or 'nothinning').");
            }
        }

        if (sigma is <= 0)
        {
            throw new JgsRuntimeException(line, col, "edge: sigma must be positive.");
        }

        EdgeDetection.EdgeResult result = EdgeDetection.Detect(source.Buffer, method, high, low, sigma, direction);
        JgsValue edges = ImgOut(result.Edges, ImageClass.Logical);
        if (wanted < 2)
        {
            return [edges];
        }

        // Canny reports the pair it used; the single-threshold methods report the one level.
        JgsValue threshold = method is EdgeDetection.Method.Canny or EdgeDetection.Method.ApproximateCanny
            ? Numbers([result.Low, result.High])
            : JgsValue.Number(result.High);
        return [edges, threshold];
    }

    private static EdgeDetection.Method ParseEdgeMethod(string method, int line, int col) =>
        method.ToLowerInvariant() switch
        {
            "sobel" => EdgeDetection.Method.Sobel,
            "prewitt" => EdgeDetection.Method.Prewitt,
            "canny" => EdgeDetection.Method.Canny,
            "approxcanny" => EdgeDetection.Method.ApproximateCanny,
            "roberts" => EdgeDetection.Method.Roberts,
            "log" or "zerocross" => EdgeDetection.Method.Log,
            _ => throw new JgsRuntimeException(line, col,
                $"edge: unknown method '{method}' (use 'sobel', 'prewitt', 'roberts', 'canny', " +
                "'approxcanny', or 'log')."),
        };

    private static Gradients.Operator ParseGradientOperator(string name, string method, int line, int col) =>
        method.ToLowerInvariant() switch
        {
            "sobel" => Gradients.Operator.Sobel,
            "prewitt" => Gradients.Operator.Prewitt,
            "roberts" => Gradients.Operator.Roberts,
            "central" => Gradients.Operator.Central,
            "intermediate" => Gradients.Operator.Intermediate,
            _ => throw new JgsRuntimeException(line, col,
                $"{name}: unknown method '{method}' (use 'sobel', 'prewitt', 'roberts', 'central', " +
                "or 'intermediate')."),
        };

    private static double[,] ElementToMatrix(bool[,] element)
    {
        int h = element.GetLength(0);
        int w = element.GetLength(1);
        var values = new double[h, w];
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                values[r, c] = element[r, c] ? 1.0 : 0.0;
            }
        }

        return values;
    }

    /// <summary>Reads a houghpeaks result — rows of 0-based [rhoIndex, thetaIndex] — back to pairs.</summary>
    private static (int RhoIndex, int ThetaIndex)[] PeakIndices(JgsValue value, int line, int col)
    {
        if (value.Type != JgsType.Array)
        {
            throw new JgsRuntimeException(line, col, "houghlines expects the peaks from houghpeaks as argument 4.");
        }

        JgsValue[] rows = value.BoxedElements();
        var peaks = new (int, int)[rows.Length];
        for (int i = 0; i < rows.Length; i++)
        {
            double[] pair = ToDoubles("houghlines", rows[i], line, col);
            if (pair.Length != 2)
            {
                throw new JgsRuntimeException(line, col, "each houghlines peak must be a [rhoIndex, thetaIndex] pair.");
            }

            peaks[i] = ((int)pair[0], (int)pair[1]);
        }

        return peaks;
    }

    private static JGraph.Data.Table LineSegmentsToTable(HoughTransform.LineSegment[] segments)
    {
        int n = segments.Length;
        var x1 = new double[n];
        var y1 = new double[n];
        var x2 = new double[n];
        var y2 = new double[n];
        var theta = new double[n];
        var rho = new double[n];
        for (int i = 0; i < n; i++)
        {
            x1[i] = segments[i].Point1X;
            y1[i] = segments[i].Point1Y;
            x2[i] = segments[i].Point2X;
            y2[i] = segments[i].Point2Y;
            theta[i] = segments[i].Theta;
            rho[i] = segments[i].Rho;
        }

        return new JGraph.Data.Table(new List<JGraph.Data.TableColumn>
        {
            new JGraph.Data.NumberColumn("Point1X", x1),
            new JGraph.Data.NumberColumn("Point1Y", y1),
            new JGraph.Data.NumberColumn("Point2X", x2),
            new JGraph.Data.NumberColumn("Point2Y", y2),
            new JGraph.Data.NumberColumn("Theta", theta),
            new JGraph.Data.NumberColumn("Rho", rho),
        });
    }

    private static JGraph.Data.Table RegionPropertiesToTable(Regions.RegionProperty[] props, bool withIntensity)
    {
        int n = props.Length;
        var mean = new double[n];
        var wx = new double[n];
        var wy = new double[n];
        var label = new double[n];
        var area = new double[n];
        var cx = new double[n];
        var cy = new double[n];
        var bx = new double[n];
        var by = new double[n];
        var bw = new double[n];
        var bh = new double[n];
        for (int i = 0; i < n; i++)
        {
            label[i] = props[i].Label;
            area[i] = props[i].Area;
            cx[i] = props[i].CentroidX;
            cy[i] = props[i].CentroidY;
            bx[i] = props[i].BoundingBoxX;
            by[i] = props[i].BoundingBoxY;
            bw[i] = props[i].BoundingBoxWidth;
            bh[i] = props[i].BoundingBoxHeight;
            mean[i] = props[i].MeanIntensity;
            wx[i] = props[i].WeightedCentroidX;
            wy[i] = props[i].WeightedCentroidY;
        }

        var columns = new List<JGraph.Data.TableColumn>
        {
            new JGraph.Data.NumberColumn("Label", label),
            new JGraph.Data.NumberColumn("Area", area),
            new JGraph.Data.NumberColumn("CentroidX", cx),
            new JGraph.Data.NumberColumn("CentroidY", cy),
            new JGraph.Data.NumberColumn("BBoxX", bx),
            new JGraph.Data.NumberColumn("BBoxY", by),
            new JGraph.Data.NumberColumn("BBoxW", bw),
            new JGraph.Data.NumberColumn("BBoxH", bh),
        };

        // The intensity columns only exist when regionprops was given an intensity image.
        if (withIntensity)
        {
            columns.Add(new JGraph.Data.NumberColumn("MeanIntensity", mean));
            columns.Add(new JGraph.Data.NumberColumn("WeightedCentroidX", wx));
            columns.Add(new JGraph.Data.NumberColumn("WeightedCentroidY", wy));
        }

        return new JGraph.Data.Table(columns);
    }

    private static JgsValue ImageArithmetic(
        string name, IReadOnlyList<JgsValue> args, int line, int col,
        Func<ImageBuffer, ImageBuffer, ImageBuffer> combine,
        Func<ImageBuffer, double, ImageBuffer> scalar,
        JgsDialect dialect,
        bool scalarIsLevel)
    {
        Arity(name, args, 2, line, col);
        ImageBuffer image = Img(name, args, 0, line, col);
        if (args[1].Type == JgsType.Image)
        {
            try
            {
                return ImgOut(combine(image, args[1].AsImage), image);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }
        }

        // An added or subtracted constant is an intensity, so under MATLAB it is quoted in the image's
        // own class — imadd(uint8Image, 50) means 50 grey levels, not 50 times the full range. A
        // multiplier is dimensionless in either dialect, and JGS quotes everything in [0, 1] (ADR 0028).
        double value = Num(name, args, 1, line, col);
        if (scalarIsLevel && dialect.IsMatlab)
        {
            value /= image.Class.Scale();
        }

        try
        {
            return ImgOut(scalar(image, value), image);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, ex.Message);
        }
    }

    /// <summary>
    /// Rescales a channel matrix to the values a script should see. MATLAB has no separate image type,
    /// so a <c>uint8</c> picture's pixels are 0–255 there; JGS documents images as [0, 1] samples and
    /// its shipped scripts rely on that, so the scale — unlike the class tag itself — is per dialect.
    /// </summary>
    private static double[,] ScaleForDialect(double[,] values, ImageClass imageClass, JgsDialect dialect)
    {
        if (!dialect.IsMatlab || !imageClass.IsInteger())
        {
            return values;
        }

        int rows = values.GetLength(0);
        int cols = values.GetLength(1);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                values[r, c] = imageClass.ToNative(values[r, c]);
            }
        }

        return values;
    }

    /// <summary>The smallest and largest sample in an image, for <c>imshow(I, [])</c>.</summary>
    private static (double Low, double High) SampleRange(ImageBuffer image)
    {
        double low = double.PositiveInfinity;
        double high = double.NegativeInfinity;
        ReadOnlySpan<double> px = image.Pixels;
        for (int i = 0; i < px.Length; i++)
        {
            if (px[i] < low) { low = px[i]; }
            if (px[i] > high) { high = px[i]; }
        }

        GC.KeepAlive(image);
        return high > low ? (low, high) : (0.0, 1.0);
    }

    /// <summary>
    /// MATLAB's default <c>imhist</c> bin count: one bin per representable level for an integer class,
    /// 256 for a floating-point image, and 2 for a logical one.
    /// </summary>
    private static int DefaultBins(ImageBuffer image) => image.Class switch
    {
        ImageClass.Logical => 2,
        ImageClass.UInt8 => 256,
        _ => 256,
    };

    // --- Option specs --------------------------------------------------------------------------
    private static readonly ImgOptionSpec WriteSpec = new(
        "imwrite", Flags: [], Names: ["Quality", "BitDepth", "Alpha"], StringPositionals: 2);

    private static readonly ImgOptionSpec ShowSpec = new(
        "imshow",
        Flags: [],
        Names: ["DisplayRange", "InitialMagnification", "Border", "Colormap", "Parent", "Interpolation"]);

    private static readonly ImgOptionSpec BinarizeSpec = new(
        "imbinarize",
        Flags: ["global", "adaptive"],
        Names: ["Sensitivity", "ForegroundPolarity", "NeighborhoodSize"]);

    private static readonly ImgOptionSpec AdaptThreshSpec = new(
        "adaptthresh",
        Flags: [],
        Names: ["NeighborhoodSize", "ForegroundPolarity", "Statistic"]);

    private static (double Low, double High) Pair(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        double[] pair = ToDoubles(name, args[index], line, col);
        if (pair.Length != 2)
        {
            throw new JgsRuntimeException(line, col, $"{name} expects argument {index + 1} to be a [low, high] pair.");
        }

        return (pair[0], pair[1]);
    }

    private static readonly ImgOptionSpec ImResizeSpec = new(
        "imresize",
        ["nearest", "box", "bilinear", "triangle", "linear", "bicubic", "cubic", "lanczos2", "lanczos3"],
        ["Antialiasing", "Method", "OutputSize", "Scale"]);

    private static readonly ImgOptionSpec ImRotateSpec = new(
        "imrotate",
        ["nearest", "bilinear", "linear", "bicubic", "cubic", "crop", "loose"],
        []);

    /// <summary>
    /// The output size for a resize given as a factor. MATLAB rounds up, so halving an odd dimension
    /// keeps the extra row rather than dropping it.
    /// </summary>
    private static (int Height, int Width) ScaleTarget(ImageBuffer image, double[]? scale, int line, int col)
    {
        if (scale is null)
        {
            throw new JgsRuntimeException(line, col,
                "imresize needs a scale, a [rows, cols] size, or an 'OutputSize'/'Scale' option.");
        }

        if (scale.Length is not (1 or 2))
        {
            throw new JgsRuntimeException(line, col, "imresize: the scale is one number or a [sy sx] pair.");
        }

        double rowScale = scale[0];
        double colScale = scale[^1];
        if (rowScale <= 0 || colScale <= 0)
        {
            throw new JgsRuntimeException(line, col, "imresize scale must be positive.");
        }

        return (
            Math.Max(1, (int)Math.Ceiling((image.Height * rowScale) - 1e-9)),
            Math.Max(1, (int)Math.Ceiling((image.Width * colScale) - 1e-9)));
    }

    /// <summary>
    /// The output size for a resize given as a size. One entry may be NaN, which asks for whatever
    /// keeps the aspect ratio — <c>imresize(I, [100 NaN])</c>.
    /// </summary>
    private static (int Height, int Width) SizeTarget(ImageBuffer image, double[] size, int line, int col)
    {
        if (size.Length != 2)
        {
            throw new JgsRuntimeException(line, col, "imresize size must be a [rows, cols] pair.");
        }

        double rows = size[0];
        double cols = size[1];
        if (double.IsNaN(rows) && double.IsNaN(cols))
        {
            throw new JgsRuntimeException(line, col, "imresize: only one of the two sizes may be NaN.");
        }

        if (double.IsNaN(rows))
        {
            rows = Math.Round(cols * image.Height / image.Width);
        }
        else if (double.IsNaN(cols))
        {
            cols = Math.Round(rows * image.Width / image.Height);
        }

        if (rows < 1 || cols < 1)
        {
            throw new JgsRuntimeException(line, col, "imresize: the output size must be at least one pixel.");
        }

        return ((int)Math.Round(rows), (int)Math.Round(cols));
    }

    /// <summary>
    /// The kernel <c>imresize</c> was asked for, and whether that kernel antialiases when shrinking.
    /// 'nearest' is the one method MATLAB leaves unantialiased by default; 'box' is the same kernel
    /// with antialiasing on, which is the whole difference between the two words.
    /// </summary>
    private static (Geometry.Interpolation Method, bool Antialias) ResizeMethod(ImgArgs parsed, int line, int col)
    {
        string flag = parsed.OneOf(
            string.Empty,
            "nearest", "box", "bilinear", "triangle", "linear", "bicubic", "cubic", "lanczos2", "lanczos3");
        string? named = parsed.Text("Method");
        if (flag.Length > 0 && named is not null)
        {
            throw new JgsRuntimeException(line, col,
                $"imresize: the method was given twice, as '{flag}' and as 'Method', '{named}'.");
        }

        string word = flag.Length > 0 ? flag : named ?? "bicubic";
        return (ParseInterpolation(word, line, col), !word.Equals("nearest", StringComparison.OrdinalIgnoreCase));
    }

    private static Geometry.Interpolation ParseInterpolation(string method, int line, int col) =>
        method.ToLowerInvariant() switch
        {
            "nearest" or "box" => Geometry.Interpolation.Nearest,
            "bilinear" or "linear" or "triangle" => Geometry.Interpolation.Bilinear,
            "bicubic" or "cubic" => Geometry.Interpolation.Bicubic,
            "lanczos2" => Geometry.Interpolation.Lanczos2,
            "lanczos3" => Geometry.Interpolation.Lanczos3,
            _ => throw new JgsRuntimeException(line, col,
                $"unknown interpolation '{method}' (use 'nearest', 'bilinear', 'bicubic', 'lanczos2', or 'lanczos3')."),
        };

    /// <summary>Builds a shaped matrix value from a scalar field.</summary>
    private static JgsValue MatrixToRows(double[,] values)
    {
        int rows = values.GetLength(0);
        int cols = values.GetLength(1);
        return JgsMatrix.Build(rows, cols, (r, c) => values[r, c]);
    }

    /// <summary>Copies a single-channel image into a <c>[rows, cols]</c> scalar field for <see cref="ImagePlot"/>.</summary>
    private static double[,] ToScalarField(ImageBuffer image)
    {
        var values = new double[image.Height, image.Width];
        ReadOnlySpan<double> px = image.Pixels;
        for (int r = 0; r < image.Height; r++)
        {
            int rowOffset = r * image.Width;
            for (int c = 0; c < image.Width; c++)
            {
                values[r, c] = px[rowOffset + c];
            }
        }

        GC.KeepAlive(image);
        return values;
    }

    /// <summary>Converts an RGB image to row-major 0xFFRRGGBB pixels (opaque), clamping to bytes.</summary>
    private static uint[] ToArgb(ImageBuffer image)
    {
        var pixels = new uint[image.Width * image.Height];
        ReadOnlySpan<double> px = image.Pixels;
        for (int i = 0; i < pixels.Length; i++)
        {
            int b = i * 3;
            uint r = ByteOf(px[b]);
            uint g = ByteOf(px[b + 1]);
            uint bl = ByteOf(px[b + 2]);
            pixels[i] = 0xFF000000u | (r << 16) | (g << 8) | bl;
        }

        GC.KeepAlive(image);
        return pixels;
    }

    private static uint ByteOf(double value) => (uint)Math.Clamp((int)Math.Round(value * 255.0), 0, 255);

    /// <summary>Applies MATLAB <c>imshow</c> axes styling: equal aspect, no frame, no ticks or labels.</summary>
    private static void StyleImageAxes(AxesModel axes)
    {
        axes.EqualAspect = true;
        axes.FrameVisible = false;
        foreach (AxisModel axis in new[] { axes.PrimaryXAxis, axes.PrimaryYAxis })
        {
            axis.ShowMajorTicks = false;
            axis.ShowMinorTicks = false;
            axis.ShowTickLabels = false;
        }
    }
}
