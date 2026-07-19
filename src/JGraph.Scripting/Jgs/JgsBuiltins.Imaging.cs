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
        Random random)
    {
        // --- File IO -------------------------------------------------------------------------
        define("imread", (args, line, col) =>
        {
            Arity("imread", args, 1, line, col);
            string path = host.Resolve(Str("imread", args, 0, line, col));
            try
            {
                return JgsValue.Image(ImageCodec.Read(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                throw new JgsRuntimeException(line, col, $"imread: cannot read '{path}': {ex.Message}");
            }
        });

        define("imwrite", (args, line, col) =>
        {
            ArityRange("imwrite", args, 2, 3, line, col);
            ImageBuffer image = Img("imwrite", args, 0, line, col);
            string path = host.ResolveForWrite(Str("imwrite", args, 1, line, col));
            int? quality = args.Count == 3 ? Count("imwrite", args, 2, line, col) : null;
            try
            {
                ImageCodec.Write(path, image, quality);
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
            Arity("imshow", args, 1, line, col);
            if (args[0].Type != JgsType.Image)
            {
                throw new JgsRuntimeException(line, col,
                    args[0].Type == JgsType.Array
                        ? "imshow displays an image value; for a numeric matrix use imagesc."
                        : $"imshow expects an image, but got a {args[0].TypeName}.");
            }

            ImageBuffer image = args[0].AsImage;
            if (image.Channels == 1)
            {
                ImagePlot plot = JG.Image(ToScalarField(image));
                plot.Colormap = Colormap.Grayscale;
                plot.AutoScaleColor = false;
                plot.ColorMin = 0;
                plot.ColorMax = 1;
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
            ImageBuffer image = Img("rgb2gray", args, 0, line, col);
            if (image.Channels != 3)
            {
                throw new JgsRuntimeException(line, col, "rgb2gray expects an RGB image; a grayscale image is already gray.");
            }

            return JgsValue.Image(PointOps.ToGray(image));
        });

        define("im2gray", (args, line, col) =>
        {
            Arity("im2gray", args, 1, line, col);
            return JgsValue.Image(PointOps.ToGray(Img("im2gray", args, 0, line, col)));
        });

        define("mat2im", (args, line, col) =>
        {
            Arity("mat2im", args, 1, line, col);
            return JgsValue.Image(PointOps.FromMatrix(Matrix("mat2im", args, 0, line, col)));
        });

        define("mat2gray", (args, line, col) =>
        {
            Arity("mat2gray", args, 1, line, col);
            return JgsValue.Image(PointOps.Normalize(Matrix("mat2gray", args, 0, line, col)));
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

            return MatrixToRows(PointOps.ToMatrix(image, channel));
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
                return JgsValue.Image(PointOps.Adjust(image, lowIn, highIn, lowOut, highOut, gamma));
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
            int bins = args.Count == 2 ? Count("imhist", args, 1, line, col) : 256;
            try
            {
                return Numbers(Histograms.Histogram(image, bins));
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }
        });

        define("histeq", (args, line, col) =>
        {
            ArityRange("histeq", args, 1, 2, line, col);
            ImageBuffer image = Img("histeq", args, 0, line, col);
            int bins = args.Count == 2 ? Count("histeq", args, 1, line, col) : 64;
            try
            {
                return JgsValue.Image(Histograms.Equalize(image, bins));
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
            ArityRange("imbinarize", args, 1, 2, line, col);
            ImageBuffer image = Img("imbinarize", args, 0, line, col);
            double? level = args.Count == 2 ? Num("imbinarize", args, 1, line, col) : null;
            return JgsValue.Image(Histograms.Binarize(image, level));
        });

        // --- Arithmetic ----------------------------------------------------------------------
        define("imadd", (args, line, col) => ImageArithmetic("imadd", args, line, col, PointOps.Add, PointOps.AddScalar));
        define("imsubtract", (args, line, col) => ImageArithmetic("imsubtract", args, line, col, PointOps.Subtract, PointOps.SubtractScalar));
        define("imcomplement", (args, line, col) =>
        {
            Arity("imcomplement", args, 1, line, col);
            return JgsValue.Image(PointOps.Complement(Img("imcomplement", args, 0, line, col)));
        });

        define("imnoise", (args, line, col) =>
        {
            ArityRange("imnoise", args, 1, 3, line, col);
            ImageBuffer image = Img("imnoise", args, 0, line, col);
            string kind = args.Count >= 2 ? Str("imnoise", args, 1, line, col).ToLowerInvariant() : "gaussian";
            return kind switch
            {
                "gaussian" => JgsValue.Image(PointOps.GaussianNoise(image, 0.0,
                    args.Count >= 3 ? Num("imnoise", args, 2, line, col) : 0.01, random)),
                "salt & pepper" or "salt&pepper" or "saltpepper" => JgsValue.Image(PointOps.SaltPepperNoise(image,
                    args.Count >= 3 ? Num("imnoise", args, 2, line, col) : 0.05, random)),
                _ => throw new JgsRuntimeException(line, col, $"imnoise: unknown noise type '{kind}' (use 'gaussian' or 'salt & pepper')."),
            };
        });

        // --- Geometry ------------------------------------------------------------------------
        define("imresize", (args, line, col) =>
        {
            ArityRange("imresize", args, 2, 3, line, col);
            ImageBuffer image = Img("imresize", args, 0, line, col);
            (int newHeight, int newWidth) = ResizeTarget(image, args[1], line, col);
            Geometry.Interpolation method = args.Count == 3
                ? ParseInterpolation(Str("imresize", args, 2, line, col), line, col)
                : Geometry.Interpolation.Bilinear;
            return JgsValue.Image(Geometry.Resize(image, newHeight, newWidth, method));
        });

        define("imrotate", (args, line, col) =>
        {
            ArityRange("imrotate", args, 2, 4, line, col);
            ImageBuffer image = Img("imrotate", args, 0, line, col);
            double degrees = Num("imrotate", args, 1, line, col);
            Geometry.Interpolation method = Geometry.Interpolation.Bilinear;
            bool loose = true;
            for (int i = 2; i < args.Count; i++)
            {
                string option = Str("imrotate", args, i, line, col).ToLowerInvariant();
                switch (option)
                {
                    case "nearest": method = Geometry.Interpolation.Nearest; break;
                    case "bilinear": case "linear": method = Geometry.Interpolation.Bilinear; break;
                    case "crop": loose = false; break;
                    case "loose": loose = true; break;
                    default: throw new JgsRuntimeException(line, col, $"imrotate: unknown option '{option}'.");
                }
            }

            return JgsValue.Image(Geometry.Rotate(image, degrees, method, loose));
        });

        define("imcrop", (args, line, col) =>
        {
            Arity("imcrop", args, 2, line, col);
            ImageBuffer image = Img("imcrop", args, 0, line, col);
            double[] rect = ToDoubles("imcrop", args[1], line, col);
            if (rect.Length != 4)
            {
                throw new JgsRuntimeException(line, col, "imcrop rect must be [x, y, width, height].");
            }

            return JgsValue.Image(Geometry.Crop(image,
                (int)Math.Round(rect[0]), (int)Math.Round(rect[1]),
                (int)Math.Round(rect[2]), (int)Math.Round(rect[3])));
        });

        DefineImagingWaveB(define);

        // --- Filtering -----------------------------------------------------------------------
        define("imfilter", (args, line, col) =>
        {
            ArityRange("imfilter", args, 2, 3, line, col);
            ImageBuffer image = Img("imfilter", args, 0, line, col);
            double[,] kernel = Matrix("imfilter", args, 1, line, col);
            Filters.Boundary boundary = args.Count == 3
                ? ParseBoundary(Str("imfilter", args, 2, line, col), line, col)
                : Filters.Boundary.Zero;
            return JgsValue.Image(Filters.Correlate(image, kernel, boundary));
        });

        define("conv2", (args, line, col) =>
        {
            ArityRange("conv2", args, 2, 3, line, col);
            double[,] a = Matrix("conv2", args, 0, line, col);
            double[,] b = Matrix("conv2", args, 1, line, col);
            Conv2Shape shape = args.Count == 3 ? ParseConv2Shape(Str("conv2", args, 2, line, col), line, col) : Conv2Shape.Full;
            return MatrixToRows(Filters.Convolve2(a, b, shape));
        });

        define("medfilt2", (args, line, col) =>
        {
            ArityRange("medfilt2", args, 1, 2, line, col);
            ImageBuffer image = Img("medfilt2", args, 0, line, col);
            int mh = 3;
            int mw = 3;
            if (args.Count == 2)
            {
                double[] size = ToDoubles("medfilt2", args[1], line, col);
                if (size.Length != 2)
                {
                    throw new JgsRuntimeException(line, col, "medfilt2 window must be [rows, cols].");
                }

                mh = Math.Max(1, (int)Math.Round(size[0]));
                mw = Math.Max(1, (int)Math.Round(size[1]));
            }

            return JgsValue.Image(Filters.Median(image, mh, mw));
        });

        define("fspecial", (args, line, col) =>
        {
            ArityRange("fspecial", args, 1, 3, line, col);
            string type = Str("fspecial", args, 0, line, col).ToLowerInvariant();
            double[,] kernel = type switch
            {
                "average" => Kernels.Average(args.Count >= 2 ? Count("fspecial", args, 1, line, col) : 3),
                "gaussian" => Kernels.Gaussian(
                    args.Count >= 2 ? Count("fspecial", args, 1, line, col) : 3,
                    args.Count >= 3 ? Num("fspecial", args, 2, line, col) : 0.5),
                "sobel" => Kernels.Sobel(),
                "prewitt" => Kernels.Prewitt(),
                "laplacian" => Kernels.Laplacian(args.Count >= 2 ? Num("fspecial", args, 1, line, col) : 0.2),
                "disk" => Kernels.Disk(args.Count >= 2 ? Count("fspecial", args, 1, line, col) : 5),
                "log" => Kernels.LaplacianOfGaussian(
                    args.Count >= 2 ? Count("fspecial", args, 1, line, col) : 5,
                    args.Count >= 3 ? Num("fspecial", args, 2, line, col) : 0.5),
                _ => throw new JgsRuntimeException(line, col,
                    $"fspecial: unknown filter '{type}' (use average, gaussian, sobel, prewitt, laplacian, disk, or log)."),
            };

            return MatrixToRows(kernel);
        });
    }

    private static Filters.Boundary ParseBoundary(string mode, int line, int col) =>
        mode.ToLowerInvariant() switch
        {
            "zero" or "0" => Filters.Boundary.Zero,
            "replicate" => Filters.Boundary.Replicate,
            "symmetric" => Filters.Boundary.Symmetric,
            _ => throw new JgsRuntimeException(line, col, $"imfilter: unknown boundary '{mode}' (use zero, replicate, or symmetric)."),
        };

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
        define("edge", (args, line, col) =>
        {
            ArityRange("edge", args, 1, 3, line, col);
            ImageBuffer image = Img("edge", args, 0, line, col);
            EdgeDetection.Method method = args.Count >= 2
                ? ParseEdgeMethod(Str("edge", args, 1, line, col), line, col)
                : EdgeDetection.Method.Sobel;
            double? threshold = args.Count >= 3 ? Num("edge", args, 2, line, col) : null;
            return JgsValue.Image(EdgeDetection.Detect(image, method, threshold));
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
                return JgsValue.Array([JgsValue.Image(Regions.LabelsToImage(labels)), JgsValue.Number(count)]);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }
        });

        define("regionprops", (args, line, col) =>
        {
            Arity("regionprops", args, 1, line, col);
            ImageBuffer labelImage = Img("regionprops", args, 0, line, col);
            int[,] labels = Regions.ImageToLabels(labelImage);
            int count = 0;
            foreach (int label in labels)
            {
                count = Math.Max(count, label);
            }

            Regions.RegionProperty[] props = Regions.Measure(labels, count);
            return JgsValue.Table(RegionPropertiesToTable(props));
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
        return JgsValue.Image(op(image, element));
    }

    private static EdgeDetection.Method ParseEdgeMethod(string method, int line, int col) =>
        method.ToLowerInvariant() switch
        {
            "sobel" => EdgeDetection.Method.Sobel,
            "prewitt" => EdgeDetection.Method.Prewitt,
            "canny" => EdgeDetection.Method.Canny,
            _ => throw new JgsRuntimeException(line, col, $"edge: unknown method '{method}' (use 'sobel', 'prewitt', or 'canny')."),
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

    private static JGraph.Data.Table RegionPropertiesToTable(Regions.RegionProperty[] props)
    {
        int n = props.Length;
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
        }

        return new JGraph.Data.Table(new List<JGraph.Data.TableColumn>
        {
            new JGraph.Data.NumberColumn("Label", label),
            new JGraph.Data.NumberColumn("Area", area),
            new JGraph.Data.NumberColumn("CentroidX", cx),
            new JGraph.Data.NumberColumn("CentroidY", cy),
            new JGraph.Data.NumberColumn("BBoxX", bx),
            new JGraph.Data.NumberColumn("BBoxY", by),
            new JGraph.Data.NumberColumn("BBoxW", bw),
            new JGraph.Data.NumberColumn("BBoxH", bh),
        });
    }

    private static JgsValue ImageArithmetic(
        string name, IReadOnlyList<JgsValue> args, int line, int col,
        Func<ImageBuffer, ImageBuffer, ImageBuffer> combine,
        Func<ImageBuffer, double, ImageBuffer> scalar)
    {
        Arity(name, args, 2, line, col);
        ImageBuffer image = Img(name, args, 0, line, col);
        if (args[1].Type == JgsType.Image)
        {
            try
            {
                return JgsValue.Image(combine(image, args[1].AsImage));
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }
        }

        return JgsValue.Image(scalar(image, Num(name, args, 1, line, col)));
    }

    private static (double Low, double High) Pair(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        double[] pair = ToDoubles(name, args[index], line, col);
        if (pair.Length != 2)
        {
            throw new JgsRuntimeException(line, col, $"{name} expects argument {index + 1} to be a [low, high] pair.");
        }

        return (pair[0], pair[1]);
    }

    private static (int Height, int Width) ResizeTarget(ImageBuffer image, JgsValue target, int line, int col)
    {
        if (target.Type == JgsType.Number)
        {
            double scale = target.AsNumber;
            if (scale <= 0)
            {
                throw new JgsRuntimeException(line, col, "imresize scale must be positive.");
            }

            return (Math.Max(1, (int)Math.Round(image.Height * scale)), Math.Max(1, (int)Math.Round(image.Width * scale)));
        }

        double[] size = ToDoubles("imresize", target, line, col);
        if (size.Length != 2)
        {
            throw new JgsRuntimeException(line, col, "imresize size must be a scale or a [height, width] pair.");
        }

        return (Math.Max(1, (int)Math.Round(size[0])), Math.Max(1, (int)Math.Round(size[1])));
    }

    private static Geometry.Interpolation ParseInterpolation(string method, int line, int col) =>
        method.ToLowerInvariant() switch
        {
            "nearest" => Geometry.Interpolation.Nearest,
            "bilinear" or "linear" => Geometry.Interpolation.Bilinear,
            _ => throw new JgsRuntimeException(line, col, $"unknown interpolation '{method}' (use 'nearest' or 'bilinear')."),
        };

    /// <summary>Builds a JGS nested-array matrix (array of row arrays) from a scalar field.</summary>
    private static JgsValue MatrixToRows(double[,] values)
    {
        int rows = values.GetLength(0);
        int cols = values.GetLength(1);
        var result = new JgsValue[rows];
        for (int r = 0; r < rows; r++)
        {
            var row = new double[cols];
            for (int c = 0; c < cols; c++)
            {
                row[c] = values[r, c];
            }

            result[r] = Numbers(row);
        }

        return JgsValue.Array(result);
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
