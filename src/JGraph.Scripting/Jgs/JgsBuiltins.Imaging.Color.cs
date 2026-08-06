using JGraph.Imaging;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M46 wave D: colour. The CIE conversions and their white points, the Y′CbCr and NTSC transmission
/// spaces, gamma encoding, white balance, the colour-difference metrics, and the indexed-image
/// family that turns a picture into a palette and a table of indices.
/// </summary>
/// <remarks>
/// Every one of these functions takes a three-channel image <em>or</em> an n×3 colormap, because
/// MATLAB's do and because the two are the same data at different sizes. One reader flattens either
/// into a block of triples, one writer puts the answer back in the shape it arrived in, and the
/// conversions in <see cref="ColorSpaces"/> never learn which they were given.
/// </remarks>
internal static partial class JgsBuiltins
{
    private static readonly OptionSpec XyzSpec = new("rgb2xyz", [], ["ColorSpace", "WhitePoint"]);

    private static readonly OptionSpec LabSpec = new("rgb2lab", [], ["ColorSpace", "WhitePoint"]);

    private static readonly OptionSpec ToRgbSpec = new(
        "lab2rgb", [], ["ColorSpace", "WhitePoint", "OutputType"]);

    private static readonly OptionSpec WhitePointOnlySpec = new("xyz2lab", [], ["WhitePoint"]);

    private static readonly OptionSpec GammaSpec = new("rgb2lin", [], ["ColorSpace", "OutputType"]);

    private static readonly OptionSpec ChromadaptSpec = new("chromadapt", [], ["ColorSpace", "Method"]);

    private static readonly OptionSpec IllumGraySpec = new("illumgray", [], ["Mask", "Norm"]);

    private static readonly OptionSpec IllumSpec = new("illumwhite", [], ["Mask"]);

    private static readonly OptionSpec DeltaESpec = new("deltaE", [], ["isInputLab"]);

    private static readonly OptionSpec ColorDiffSpec = new(
        "imcolordiff", [], ["Standard", "isInputLab", "kL", "K1", "K2"]);

    private static void DefineColorBuiltins(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> define, JgsDialect dialect)
    {
        // --- Hue, saturation, value ------------------------------------------------------------
        define("rgb2hsv", (args, line, col) =>
        {
            Arity("rgb2hsv", args, 1, line, col);
            ColorArg source = Triples("rgb2hsv", args, 0, line, col);
            return ColorOut(ColorSpaces.RgbToHsv(source.Triples), source, ImageClass.Double);
        });

        define("hsv2rgb", (args, line, col) =>
        {
            Arity("hsv2rgb", args, 1, line, col);
            ColorArg source = Triples("hsv2rgb", args, 0, line, col);
            return ColorOut(ColorSpaces.HsvToRgb(source.Triples), source, ImageClass.Double);
        });

        // --- The CIE chain ---------------------------------------------------------------------
        define("whitepoint", (args, line, col) =>
        {
            ArityRange("whitepoint", args, 0, 1, line, col);
            string name = args.Count == 0 ? "icc" : Str("whitepoint", args, 0, line, col);
            return Numbers(WhitePointOf("whitepoint", name, line, col));
        });

        define("rgb2xyz", (args, line, col) =>
        {
            ParsedArgs parsed = XyzSpec.Parse(args, 1, line, col);
            ColorArg source = Triples("rgb2xyz", parsed.Positional, 0, line, col);
            (RgbColorSpace space, double[] white) = SpaceAndWhite("rgb2xyz", parsed, line, col);
            return ColorOut(ColorSpaces.RgbToXyz(source.Triples, space, white), source, ImageClass.Double);
        });

        define("xyz2rgb", (args, line, col) =>
        {
            ParsedArgs parsed = ToRgbSpec.Parse(args, 1, line, col);
            ColorArg source = Triples("xyz2rgb", parsed.Positional, 0, line, col);
            (RgbColorSpace space, double[] white) = SpaceAndWhite("xyz2rgb", parsed, line, col);
            return ColorOut(
                ColorSpaces.XyzToRgb(source.Triples, space, white), source, OutputClass("xyz2rgb", parsed, line, col));
        });

        define("rgb2lab", (args, line, col) =>
        {
            ParsedArgs parsed = LabSpec.Parse(args, 1, line, col);
            ColorArg source = Triples("rgb2lab", parsed.Positional, 0, line, col);
            (RgbColorSpace space, double[] white) = SpaceAndWhite("rgb2lab", parsed, line, col);
            return ColorOut(ColorSpaces.RgbToLab(source.Triples, space, white), source, ImageClass.Double);
        });

        define("lab2rgb", (args, line, col) =>
        {
            ParsedArgs parsed = ToRgbSpec.Parse(args, 1, line, col);
            ColorArg source = Triples("lab2rgb", parsed.Positional, 0, line, col);
            (RgbColorSpace space, double[] white) = SpaceAndWhite("lab2rgb", parsed, line, col);
            return ColorOut(
                ColorSpaces.LabToRgb(source.Triples, space, white), source, OutputClass("lab2rgb", parsed, line, col));
        });

        define("xyz2lab", (args, line, col) =>
        {
            ParsedArgs parsed = WhitePointOnlySpec.Parse(args, 1, line, col);
            ColorArg source = Triples("xyz2lab", parsed.Positional, 0, line, col);
            double[] white = WhitePointOption("xyz2lab", parsed, line, col);
            return ColorOut(ColorSpaces.XyzToLab(source.Triples, white), source, ImageClass.Double);
        });

        define("lab2xyz", (args, line, col) =>
        {
            ParsedArgs parsed = WhitePointOnlySpec.Parse(args, 1, line, col);
            ColorArg source = Triples("lab2xyz", parsed.Positional, 0, line, col);
            double[] white = WhitePointOption("lab2xyz", parsed, line, col);
            return ColorOut(ColorSpaces.LabToXyz(source.Triples, white), source, ImageClass.Double);
        });

        define("rgb2lightness", (args, line, col) =>
        {
            Arity("rgb2lightness", args, 1, line, col);
            ColorArg source = Triples("rgb2lightness", args, 0, line, col);
            return ScalarOut(
                ColorSpaces.Lightness(source.Triples, RgbColorSpace.Srgb, ColorSpaces.WhitePoint("d65")), source);
        });

        // --- Transmission spaces ---------------------------------------------------------------
        define("rgb2ycbcr", (args, line, col) =>
        {
            Arity("rgb2ycbcr", args, 1, line, col);
            ColorArg source = Triples("rgb2ycbcr", args, 0, line, col);
            return ColorOut(ColorSpaces.RgbToYCbCr(source.Triples), source);
        });

        define("ycbcr2rgb", (args, line, col) =>
        {
            Arity("ycbcr2rgb", args, 1, line, col);
            ColorArg source = Triples("ycbcr2rgb", args, 0, line, col);
            return ColorOut(ColorSpaces.YCbCrToRgb(source.Triples), source);
        });

        define("rgb2ntsc", (args, line, col) =>
        {
            Arity("rgb2ntsc", args, 1, line, col);
            ColorArg source = Triples("rgb2ntsc", args, 0, line, col);
            return ColorOut(ColorSpaces.RgbToNtsc(source.Triples), source, ImageClass.Double);
        });

        define("ntsc2rgb", (args, line, col) =>
        {
            Arity("ntsc2rgb", args, 1, line, col);
            ColorArg source = Triples("ntsc2rgb", args, 0, line, col);
            return ColorOut(ColorSpaces.NtscToRgb(source.Triples), source, ImageClass.Double);
        });

        // --- Gamma -----------------------------------------------------------------------------
        define("rgb2lin", (args, line, col) =>
        {
            ParsedArgs parsed = GammaSpec.Parse(args, 1, line, col);
            ColorArg source = Triples("rgb2lin", parsed.Positional, 0, line, col);
            RgbColorSpace space = SpaceOption("rgb2lin", parsed, line, col);
            return ColorOut(
                ColorSpaces.RgbToLinear(source.Triples, space), source, OutputClass("rgb2lin", parsed, line, col));
        });

        define("lin2rgb", (args, line, col) =>
        {
            ParsedArgs parsed = GammaSpec.Parse(args, 1, line, col);
            ColorArg source = Triples("lin2rgb", parsed.Positional, 0, line, col);
            RgbColorSpace space = SpaceOption("lin2rgb", parsed, line, col);
            return ColorOut(
                ColorSpaces.LinearToRgb(source.Triples, space), source, OutputClass("lin2rgb", parsed, line, col));
        });

        // --- White balance ---------------------------------------------------------------------
        define("chromadapt", (args, line, col) =>
        {
            ParsedArgs parsed = ChromadaptSpec.Parse(args, 2, line, col);
            if (parsed.Positional.Count < 2)
            {
                throw new JgsRuntimeException(line, col,
                    "chromadapt(A, illuminant) needs an image and the illuminant to balance against.");
            }

            ColorArg source = Triples("chromadapt", parsed.Positional, 0, line, col);
            double[] illuminant = NumericVector("chromadapt", parsed.Positional[1], line, col);
            RgbColorSpace space = SpaceOption("chromadapt", parsed, line, col);
            string method = (parsed.Text("Method") ?? "bradford").ToLowerInvariant();
            AdaptationMethod adaptation = method switch
            {
                "bradford" => AdaptationMethod.Bradford,
                "vonkries" => AdaptationMethod.VonKries,
                "simple" => AdaptationMethod.Simple,
                _ => throw new JgsRuntimeException(line, col,
                    $"chromadapt: unknown 'Method' value '{method}' (use 'bradford', 'vonkries', or 'simple')."),
            };

            try
            {
                return ColorOut(
                    ColorAdaptation.Adapt(source.Triples, illuminant, space, adaptation), source);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"chromadapt: {ex.Message}");
            }
        });

        define("illumgray", (args, line, col) =>
        {
            ParsedArgs parsed = IllumGraySpec.Parse(args, 2, line, col);
            ColorArg source = Triples("illumgray", parsed.Positional, 0, line, col);
            double[] percentiles = parsed.Positional.Count >= 2
                ? NumericVector("illumgray", parsed.Positional[1], line, col)
                : [1.0];
            if (percentiles.Length is not (1 or 2))
            {
                throw new JgsRuntimeException(line, col,
                    "illumgray: the percentile is one number or a [bottom top] pair.");
            }

            double bottom = percentiles[0];
            double top = percentiles[^1];
            double norm = parsed.Scalar("Norm", 1.0);
            if (norm <= 0)
            {
                throw new JgsRuntimeException(line, col, "illumgray: 'Norm' must be positive.");
            }

            return Estimate("illumgray", parsed, source,
                mask => ColorAdaptation.GrayWorld(source.Triples, bottom, top, norm, mask), line, col);
        });

        define("illumwhite", (args, line, col) =>
        {
            ParsedArgs parsed = IllumSpec.Parse(args, 2, line, col);
            ColorArg source = Triples("illumwhite", parsed.Positional, 0, line, col);
            double top = parsed.Positional.Count >= 2
                ? Num("illumwhite", parsed.Positional, 1, line, col)
                : 1.0;
            return Estimate("illumwhite", parsed, source,
                mask => ColorAdaptation.WhitePatch(source.Triples, top, mask), line, col);
        });

        define("illumpca", (args, line, col) =>
        {
            ParsedArgs parsed = IllumSpec.Parse(args, 2, line, col);
            ColorArg source = Triples("illumpca", parsed.Positional, 0, line, col);
            double percentage = parsed.Positional.Count >= 2
                ? Num("illumpca", parsed.Positional, 1, line, col)
                : 3.5;
            return Estimate("illumpca", parsed, source,
                mask => ColorAdaptation.PrincipalComponent(source.Triples, percentage, mask), line, col);
        });

        // --- Colour difference -----------------------------------------------------------------
        define("colorangle", (args, line, col) =>
        {
            Arity("colorangle", args, 2, line, col);
            double[] first = NumericVector("colorangle", args[0], line, col);
            double[] second = NumericVector("colorangle", args[1], line, col);
            if (first.Length != 3 || second.Length != 3)
            {
                throw new JgsRuntimeException(line, col, "colorangle compares two RGB triples.");
            }

            return JgsValue.Number(ColorSpaces.ColorAngle(first, second));
        });

        define("deltaE", (args, line, col) =>
        {
            ParsedArgs parsed = DeltaESpec.Parse(args, 2, line, col);
            (ColorArg source, double[,] lab1, double[,] lab2) =
                LabPair("deltaE", parsed, parsed.Flag("isInputLab", false), line, col);
            return ScalarOut(ColorSpaces.DeltaE76(lab1, lab2), source);
        });

        define("imcolordiff", (args, line, col) =>
        {
            ParsedArgs parsed = ColorDiffSpec.Parse(args, 2, line, col);
            (ColorArg source, double[,] lab1, double[,] lab2) =
                LabPair("imcolordiff", parsed, parsed.Flag("isInputLab", false), line, col);

            string standard = (parsed.Text("Standard") ?? "CIEDE2000").ToLowerInvariant();
            double kL = parsed.Scalar("kL", 1.0);
            double k1 = parsed.Scalar("K1", 0.045);
            double k2 = parsed.Scalar("K2", 0.015);
            return ScalarOut(standard switch
            {
                "ciede2000" => ColorSpaces.DeltaE2000(lab1, lab2, kL, 1.0, 1.0),
                "cie94" => ColorSpaces.DeltaE94(lab1, lab2, kL, k1, k2),
                _ => throw new JgsRuntimeException(line, col,
                    $"imcolordiff: unknown 'Standard' value '{standard}' (use 'CIEDE2000' or 'CIE94')."),
            }, source);
        });

        // --- Lab and XYZ encodings -------------------------------------------------------------
        define("lab2double", (args, line, col) => DecodeEncoded("lab2double", args, lab: true, line, col));
        define("xyz2double", (args, line, col) => DecodeEncoded("xyz2double", args, lab: false, line, col));
        define("lab2uint8", (args, line, col) => EncodeLab("lab2uint8", args, ImageClass.UInt8, line, col));
        define("lab2uint16", (args, line, col) => EncodeLab("lab2uint16", args, ImageClass.UInt16, line, col));

        define("xyz2uint16", (args, line, col) =>
        {
            Arity("xyz2uint16", args, 1, line, col);
            ColorArg source = Triples("xyz2uint16", args, 0, line, col);
            int n = source.Triples.GetLength(0);
            var encoded = new double[n, 3];
            for (int i = 0; i < n; i++)
            {
                for (int c = 0; c < 3; c++)
                {
                    // The ICC convention: 1.0 is 32768, so values above 1 still have headroom.
                    double native = Math.Clamp(Math.Round(source.Triples[i, c] * 32768.0), 0, 65535);
                    encoded[i, c] = source.Shape == ColorShape.Image ? native / 65535.0 : native;
                }
            }

            return ColorOut(encoded, source, ImageClass.UInt16);
        });

        // --- Indexed images --------------------------------------------------------------------
        define("gray2ind", (args, line, col) => GrayToIndOutputs(args, 1, line, col, dialect)[0]);
        define("rgb2ind", (args, line, col) => RgbToIndOutputs(args, 1, line, col, dialect)[0]);
        define("imapprox", (args, line, col) => ImApproxOutputs(args, 1, line, col, dialect)[0]);
        define("imsplit", (args, line, col) => ImSplitOutputs(args, 1, line, col)[0]);

        define("ind2rgb", (args, line, col) =>
        {
            Arity("ind2rgb", args, 2, line, col);
            (double[] indices, int height, int width, bool fromMatrix) =
                IndexPlane("ind2rgb", args, 0, line, col, dialect);
            double[,] map = ColormapRows("ind2rgb", args, 1, line, col);
            return ImageOfTriples(IndexedImages.Expand(indices, map), height, width, fromMatrix);
        });

        define("ind2gray", (args, line, col) =>
        {
            Arity("ind2gray", args, 2, line, col);
            (double[] indices, int height, int width, bool fromMatrix) =
                IndexPlane("ind2gray", args, 0, line, col, dialect);
            double[,] gray = IndexedImages.ColormapToGray(ColormapRows("ind2gray", args, 1, line, col));
            double[,] rgb = IndexedImages.Expand(indices, gray);

            var image = new ImageBuffer(height, width, 1);
            for (int r = 0; r < height; r++)
            {
                for (int c = 0; c < width; c++)
                {
                    image[r, c, 0] = rgb[(r * width) + c, 0];
                }
            }

            return fromMatrix
                ? JgsMatrix.Build(height, width, (r, c) => rgb[(r * width) + c, 0])
                : ImgOut(image, ImageClass.Double);
        });

        define("cmap2gray", (args, line, col) =>
        {
            Arity("cmap2gray", args, 1, line, col);
            return MatrixToRows(IndexedImages.ColormapToGray(ColormapRows("cmap2gray", args, 0, line, col)));
        });

        define("demosaic", (args, line, col) =>
        {
            Arity("demosaic", args, 2, line, col);
            using ImgArg source = ImgLike("demosaic", args, 0, line, col);
            if (source.Buffer.Channels != 1)
            {
                throw new JgsRuntimeException(line, col,
                    "demosaic expects a single-channel colour-filter array, not an RGB image.");
            }

            string word = Str("demosaic", args, 1, line, col).ToLowerInvariant();
            SensorAlignment alignment = word switch
            {
                "gbrg" => SensorAlignment.Gbrg,
                "grbg" => SensorAlignment.Grbg,
                "bggr" => SensorAlignment.Bggr,
                "rggb" => SensorAlignment.Rggb,
                _ => throw new JgsRuntimeException(line, col,
                    $"demosaic: unknown sensor alignment '{word}' (use 'gbrg', 'grbg', 'bggr', or 'rggb')."),
            };

            ImageBuffer cfa = source.Buffer;
            var flat = new double[cfa.Height * cfa.Width];
            for (int r = 0; r < cfa.Height; r++)
            {
                for (int c = 0; c < cfa.Width; c++)
                {
                    flat[(r * cfa.Width) + c] = cfa[r, c, 0];
                }
            }

            try
            {
                double[,] rgb = IndexedImages.Demosaic(flat, cfa.Height, cfa.Width, alignment);
                var image = new ImageBuffer(cfa.Height, cfa.Width, 3);
                for (int r = 0; r < cfa.Height; r++)
                {
                    for (int c = 0; c < cfa.Width; c++)
                    {
                        for (int ch = 0; ch < 3; ch++)
                        {
                            image[r, c, ch] = Math.Clamp(rgb[(r * cfa.Width) + c, ch], 0.0, 1.0);
                        }
                    }
                }

                return source.FromMatrix
                    ? PlanesOf(rgb, cfa.Height, cfa.Width)
                    : ImgOut(image, cfa.Class);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"demosaic: {ex.Message}");
            }
        });
    }

    /// <summary>What a block of colour triples came from, and so what the answer should look like.</summary>
    private enum ColorShape
    {
        /// <summary>A three-channel image value.</summary>
        Image,

        /// <summary>An n×3 colormap.</summary>
        Colormap,

        /// <summary>
        /// A plain <c>h×w×3</c> numeric array — which is what MATLAB calls an RGB image, and what a
        /// script that wrote <c>zeros(h, w, 3)</c> and filled the planes has in its hands.
        /// </summary>
        Planes,
    }

    /// <summary>Colour data of any of the three shapes, flattened to triples the conversions take.</summary>
    private readonly struct ColorArg(
        double[,] triples, int height, int width, ColorShape shape, ImageClass imageClass)
    {
        /// <summary>The colours, one row each, in row-major pixel order.</summary>
        public double[,] Triples => triples;

        /// <summary>Row count when this came from a picture; zero for a colormap.</summary>
        public int Height => height;

        /// <summary>Column count when this came from a picture; zero for a colormap.</summary>
        public int Width => width;

        /// <summary>Which of the three forms arrived, and so which must come back.</summary>
        public ColorShape Shape => shape;

        /// <summary>The class the source image carried.</summary>
        public ImageClass Class => imageClass;
    }

    private static ColorArg Triples(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        if (index >= args.Count)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} needs a three-channel image, an h-by-w-by-3 array, or an n-by-3 colormap.");
        }

        JgsValue value = args[index];
        if (value.Type == JgsType.Image)
        {
            ImageBuffer image = value.AsImage;
            if (image.Channels != 3)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name} expects a three-channel image, but got a grayscale one.");
            }

            var triples = new double[image.Height * image.Width, 3];
            for (int r = 0; r < image.Height; r++)
            {
                for (int c = 0; c < image.Width; c++)
                {
                    for (int ch = 0; ch < 3; ch++)
                    {
                        triples[(r * image.Width) + c, ch] = image[r, c, ch];
                    }
                }
            }

            return new ColorArg(triples, image.Height, image.Width, ColorShape.Image, image.Class);
        }

        if (value.Type == JgsType.Array && JgsMatrix.DimsOf(value) is [int high, int wide, 3])
        {
            var planes = new double[high * wide, 3];
            for (int ch = 0; ch < 3; ch++)
            {
                for (int c = 0; c < wide; c++)
                {
                    for (int r = 0; r < high; r++)
                    {
                        // Column-major storage: page ch, column c, row r.
                        planes[(r * wide) + c, ch] = value.ElementAt(r + (c * high) + (ch * high * wide)).AsNumber;
                    }
                }
            }

            return new ColorArg(planes, high, wide, ColorShape.Planes, ImageClass.Double);
        }

        double[,] map = Rectangle($"{name} argument {index + 1}", value, line, col);
        if (map.GetLength(1) != 3)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} expects a three-channel image, an h-by-w-by-3 array, or an n-by-3 colormap.");
        }

        return new ColorArg(map, 0, 0, ColorShape.Colormap, ImageClass.Double);
    }

    /// <summary>Hands triples back in whichever of the three shapes arrived.</summary>
    private static JgsValue ColorOut(double[,] triples, ColorArg source, ImageClass? forced = null) =>
        source.Shape switch
        {
            ColorShape.Colormap => MatrixToRows(triples),
            ColorShape.Planes => PlanesOf(triples, source.Height, source.Width),
            _ => ImgOut(ImageOf(triples, source.Height, source.Width, 3), forced ?? source.Class),
        };

    /// <summary>Hands one value per colour back: a grayscale image, an h×w matrix, or an n×1 column.</summary>
    private static JgsValue ScalarOut(double[] values, ColorArg source)
    {
        if (source.Shape == ColorShape.Colormap)
        {
            return JgsMatrix.Build(values.Length, 1, (r, _) => values[r]);
        }

        if (source.Shape == ColorShape.Planes)
        {
            return JgsMatrix.Build(source.Height, source.Width, (r, c) => values[(r * source.Width) + c]);
        }

        var image = new ImageBuffer(source.Height, source.Width, 1);
        for (int r = 0; r < source.Height; r++)
        {
            for (int c = 0; c < source.Width; c++)
            {
                image[r, c, 0] = values[(r * source.Width) + c];
            }
        }

        return ImgOut(image, ImageClass.Double);
    }

    /// <summary>Packs triples back into an <c>h×w×3</c> column-major array.</summary>
    private static JgsValue PlanesOf(double[,] triples, int height, int width)
    {
        var flat = new double[height * width * 3];
        for (int ch = 0; ch < 3; ch++)
        {
            for (int c = 0; c < width; c++)
            {
                for (int r = 0; r < height; r++)
                {
                    flat[r + (c * height) + (ch * height * width)] = triples[(r * width) + c, ch];
                }
            }
        }

        return JgsMatrix.FromColumnMajorDims(flat, [height, width, 3]);
    }

    /// <summary>Builds a colour result for a picture that arrived as an image or as plain planes.</summary>
    private static JgsValue ImageOfTriples(double[,] triples, int height, int width, bool asPlanes) =>
        asPlanes
            ? PlanesOf(triples, height, width)
            : ImgOut(ImageOf(triples, height, width, 3), ImageClass.Double);

    private static ImageBuffer ImageOf(double[,] triples, int height, int width, int channels)
    {
        var image = new ImageBuffer(height, width, channels);
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                for (int ch = 0; ch < channels; ch++)
                {
                    image[r, c, ch] = triples[(r * width) + c, ch];
                }
            }
        }

        return image;
    }

    /// <summary>A named white point, or an explicit XYZ triple.</summary>
    private static double[] WhitePointOf(string name, JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.String)
        {
            return WhitePointOf(name, value.AsString, line, col);
        }

        double[] explicitWhite = NumericVector(name, value, line, col);
        if (explicitWhite.Length != 3)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: a white point is an illuminant name or an [X Y Z] triple.");
        }

        return explicitWhite;
    }

    private static double[] WhitePointOf(string name, string illuminant, int line, int col)
    {
        try
        {
            return ColorSpaces.WhitePoint(illuminant);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"{name}: {ex.Message}.");
        }
    }

    private static double[] WhitePointOption(string name, ParsedArgs parsed, int line, int col) =>
        parsed.Named("WhitePoint") is { } value
            ? WhitePointOf(name, value, line, col)
            : ColorSpaces.WhitePoint("d65");

    private static RgbColorSpace SpaceOption(string name, ParsedArgs parsed, int line, int col)
    {
        string word = (parsed.Text("ColorSpace") ?? "srgb").ToLowerInvariant();
        return word switch
        {
            "srgb" => RgbColorSpace.Srgb,
            "adobe-rgb-1998" => RgbColorSpace.AdobeRgb1998,
            "prophoto-rgb" => RgbColorSpace.ProPhotoRgb,
            "linear-rgb" => RgbColorSpace.LinearRgb,
            _ => throw new JgsRuntimeException(line, col,
                $"{name}: unknown 'ColorSpace' value '{word}' " +
                "(use 'srgb', 'adobe-rgb-1998', 'prophoto-rgb', or 'linear-rgb')."),
        };
    }

    private static (RgbColorSpace Space, double[] White) SpaceAndWhite(string name, ParsedArgs parsed, int line, int col) =>
        (SpaceOption(name, parsed, line, col), WhitePointOption(name, parsed, line, col));

    /// <summary>The <c>'OutputType'</c> option, which names the class the result should carry.</summary>
    private static ImageClass? OutputClass(string name, ParsedArgs parsed, int line, int col)
    {
        if (parsed.Text("OutputType") is not { } word)
        {
            return null;
        }

        return ImageClassInfo.FromMatlabName(word.ToLowerInvariant())
               ?? throw new JgsRuntimeException(line, col,
                   $"{name}: unknown 'OutputType' value '{word}' (use 'double', 'single', 'uint8', or 'uint16').");
    }

    /// <summary>Runs an illuminant estimator, applying a 'Mask' option first if one was given.</summary>
    private static JgsValue Estimate(
        string name, ParsedArgs parsed, ColorArg source, Func<bool[]?, double[]> estimator, int line, int col)
    {
        bool[]? mask = null;
        if (parsed.Named("Mask") is { } value)
        {
            using ImgArg maskArg = ImgLike(name, [value], 0, line, col);
            ImageBuffer buffer = maskArg.Buffer;
            if (source.Shape != ColorShape.Colormap
                && (buffer.Height != source.Height || buffer.Width != source.Width))
            {
                throw new JgsRuntimeException(line, col, $"{name}: the mask must be the size of the image.");
            }

            mask = new bool[source.Triples.GetLength(0)];
            for (int i = 0; i < mask.Length && i < buffer.Height * buffer.Width; i++)
            {
                mask[i] = buffer[i / buffer.Width, i % buffer.Width, 0] != 0;
            }
        }

        try
        {
            return Numbers(estimator(mask));
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"{name}: {ex.Message}");
        }
    }

    /// <summary>Reads the two pictures a colour-difference metric compares, as L*a*b*.</summary>
    private static (ColorArg Source, double[,] First, double[,] Second) LabPair(
        string name, ParsedArgs parsed, bool alreadyLab, int line, int col)
    {
        if (parsed.Positional.Count < 2)
        {
            throw new JgsRuntimeException(line, col, $"{name} compares two images or colormaps.");
        }

        ColorArg first = Triples(name, parsed.Positional, 0, line, col);
        ColorArg second = Triples(name, parsed.Positional, 1, line, col);
        if (first.Triples.GetLength(0) != second.Triples.GetLength(0))
        {
            throw new JgsRuntimeException(line, col, $"{name}: the two inputs must be the same size.");
        }

        double[] white = ColorSpaces.WhitePoint("d65");
        return alreadyLab
            ? (first, first.Triples, second.Triples)
            : (first,
                ColorSpaces.RgbToLab(first.Triples, RgbColorSpace.Srgb, white),
                ColorSpaces.RgbToLab(second.Triples, RgbColorSpace.Srgb, white));
    }

    /// <summary>
    /// <c>lab2double</c> and <c>xyz2double</c>: undo an integer encoding. The class tag is the only
    /// record of which encoding was used, so an untagged input cannot be decoded and says so.
    /// </summary>
    private static JgsValue DecodeEncoded(string name, IReadOnlyList<JgsValue> args, bool lab, int line, int col)
    {
        Arity(name, args, 1, line, col);
        ColorArg source = Triples(name, args, 0, line, col);
        if (!source.Class.IsInteger())
        {
            throw new JgsRuntimeException(line, col,
                $"{name} expects a uint8 or uint16 encoded array; this one is {source.Class.MatlabName()}.");
        }

        int n = source.Triples.GetLength(0);
        var decoded = new double[n, 3];
        bool wide = source.Class == ImageClass.UInt16;
        for (int i = 0; i < n; i++)
        {
            double x = source.Class.ToNative(source.Triples[i, 0]);
            double y = source.Class.ToNative(source.Triples[i, 1]);
            double z = source.Class.ToNative(source.Triples[i, 2]);
            if (!lab)
            {
                decoded[i, 0] = x / 32768.0;
                decoded[i, 1] = y / 32768.0;
                decoded[i, 2] = z / 32768.0;
                continue;
            }

            decoded[i, 0] = wide ? x * 100.0 / 65280.0 : x * 100.0 / 255.0;
            decoded[i, 1] = wide ? (y / 257.0) - 128.0 : y - 128.0;
            decoded[i, 2] = wide ? (z / 257.0) - 128.0 : z - 128.0;
        }

        return ColorOut(decoded, source, ImageClass.Double);
    }

    /// <summary><c>lab2uint8</c> and <c>lab2uint16</c>: pack L*a*b* into an integer range.</summary>
    private static JgsValue EncodeLab(
        string name, IReadOnlyList<JgsValue> args, ImageClass target, int line, int col)
    {
        Arity(name, args, 1, line, col);
        ColorArg source = Triples(name, args, 0, line, col);
        bool wide = target == ImageClass.UInt16;
        double ceiling = wide ? 65535.0 : 255.0;

        int n = source.Triples.GetLength(0);
        var encoded = new double[n, 3];
        for (int i = 0; i < n; i++)
        {
            double l = wide
                ? source.Triples[i, 0] * 65280.0 / 100.0
                : source.Triples[i, 0] * 255.0 / 100.0;
            double a = wide ? (source.Triples[i, 1] + 128.0) * 257.0 : source.Triples[i, 1] + 128.0;
            double b = wide ? (source.Triples[i, 2] + 128.0) * 257.0 : source.Triples[i, 2] + 128.0;

            bool normalize = source.Shape == ColorShape.Image;
            encoded[i, 0] = Pack(l, ceiling, normalize);
            encoded[i, 1] = Pack(a, ceiling, normalize);
            encoded[i, 2] = Pack(b, ceiling, normalize);
        }

        return ColorOut(encoded, source, target);

        // An image stores normalized samples and shows the native ones; a colormap has no class tag,
        // so the native values are what a script sees either way.
        static double Pack(double native, double ceiling, bool normalize)
        {
            double clamped = Math.Clamp(Math.Round(native), 0, ceiling);
            return normalize ? clamped / ceiling : clamped;
        }
    }

    /// <summary>
    /// <c>[X, map] = gray2ind(I, n)</c>. The palette is the grey ramp and the indices are wherever
    /// each intensity lands on it.
    /// </summary>
    private static JgsValue[] GrayToIndOutputs(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col, JgsDialect dialect)
    {
        ArityRange("gray2ind", args, 1, 2, line, col);
        using ImgArg source = ImgLike("gray2ind", args, 0, line, col);
        if (source.Buffer.Channels != 1)
        {
            throw new JgsRuntimeException(line, col, "gray2ind expects a grayscale image; use rgb2ind for colour.");
        }

        int levels = args.Count == 2 ? Count("gray2ind", args, 1, line, col) : 64;
        if (levels < 1)
        {
            throw new JgsRuntimeException(line, col, "gray2ind needs at least one level.");
        }

        ImageBuffer image = source.Buffer;
        var indices = new double[image.Height * image.Width];
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++)
            {
                double level = Math.Clamp(Math.Round(image[r, c, 0] * (levels - 1)), 0, levels - 1);
                indices[(r * image.Width) + c] = level + dialect.IndexBase;
            }
        }

        JgsValue plane = IndexValue(indices, image.Height, image.Width);
        return wanted < 2
            ? [plane]
            : [plane, MatrixToRows(IndexedImages.GrayColormap(levels))];
    }

    /// <summary>
    /// <c>[X, map] = rgb2ind(RGB, …)</c>: a palette of <c>n</c> colours by median cut, a uniform grid
    /// at a given tolerance, or a palette the caller supplies.
    /// </summary>
    private static JgsValue[] RgbToIndOutputs(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col, JgsDialect dialect)
    {
        ArityRange("rgb2ind", args, 1, 3, line, col);
        ColorArg source = Triples("rgb2ind", args, 0, line, col);
        if (source.Shape == ColorShape.Colormap)
        {
            throw new JgsRuntimeException(line, col, "rgb2ind expects an RGB picture, not a colormap.");
        }

        bool dither = DitherOption("rgb2ind", args, line, col);
        double[,] map = args.Count >= 2
            ? PaletteFor("rgb2ind", args[1], source.Triples, line, col)
            : IndexedImages.MedianCut(source.Triples, 64);

        return Indexed(source.Triples, source.Height, source.Width, map, dither, wanted, dialect);
    }

    /// <summary><c>[Y, newmap] = imapprox(X, map, …)</c>: the same picture over a smaller palette.</summary>
    private static JgsValue[] ImApproxOutputs(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col, JgsDialect dialect)
    {
        ArityRange("imapprox", args, 3, 4, line, col);
        (double[] indices, int height, int width, _) = IndexPlane("imapprox", args, 0, line, col, dialect);
        double[,] map = ColormapRows("imapprox", args, 1, line, col);
        double[,] rgb = IndexedImages.Expand(indices, map);

        bool dither = DitherOption("imapprox", args, line, col);
        double[,] reduced = PaletteFor("imapprox", args[2], rgb, line, col);
        return Indexed(rgb, height, width, reduced, dither, wanted, dialect);
    }

    /// <summary><c>[R, G, B] = imsplit(RGB)</c>: the three channels as separate grayscale images.</summary>
    private static JgsValue[] ImSplitOutputs(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        Arity("imsplit", args, 1, line, col);
        ColorArg source = Triples("imsplit", args, 0, line, col);
        if (source.Shape == ColorShape.Colormap)
        {
            throw new JgsRuntimeException(line, col, "imsplit expects a colour picture, not a colormap.");
        }

        var planes = new JgsValue[Math.Max(1, Math.Min(wanted, 3))];
        for (int ch = 0; ch < planes.Length; ch++)
        {
            int channel = ch;
            if (source.Shape == ColorShape.Planes)
            {
                planes[ch] = JgsMatrix.Build(
                    source.Height, source.Width,
                    (r, c) => source.Triples[(r * source.Width) + c, channel]);
                continue;
            }

            var plane = new ImageBuffer(source.Height, source.Width, 1);
            for (int r = 0; r < source.Height; r++)
            {
                for (int c = 0; c < source.Width; c++)
                {
                    plane[r, c, 0] = source.Triples[(r * source.Width) + c, channel];
                }
            }

            planes[ch] = ImgOut(plane, source.Class);
        }

        return planes;
    }

    /// <summary>Quantizes triples against a palette and packs both outputs.</summary>
    private static JgsValue[] Indexed(
        double[,] rgb, int height, int width, double[,] map, bool dither, int wanted, JgsDialect dialect)
    {
        double[] oneBased = IndexedImages.Quantize(rgb, height, width, map, dither);
        for (int i = 0; i < oneBased.Length; i++)
        {
            oneBased[i] += dialect.IndexBase - 1;
        }

        JgsValue plane = IndexValue(oneBased, height, width);
        return wanted < 2 ? [plane] : [plane, MatrixToRows(map)];
    }

    /// <summary>
    /// The palette a second argument asks for: a count, a tolerance below one, or a colormap given
    /// outright.
    /// </summary>
    private static double[,] PaletteFor(string name, JgsValue value, double[,] rgb, int line, int col)
    {
        if (value.Type == JgsType.Number)
        {
            double request = value.AsNumber;
            if (request <= 0)
            {
                throw new JgsRuntimeException(line, col, $"{name}: the colour count or tolerance must be positive.");
            }

            // MATLAB reads a number below one as a tolerance for uniform quantization and anything
            // else as a colour count, which is the one place these two very different requests share
            // a slot.
            return request < 1 ? UniformPalette(rgb, request) : IndexedImages.MedianCut(rgb, (int)Math.Round(request));
        }

        return ColormapRows(name, [value], 0, line, col);
    }

    /// <summary>The grid palette a tolerance asks for, holding only the cells the picture uses.</summary>
    private static double[,] UniformPalette(double[,] rgb, double tolerance)
    {
        int levels = (int)Math.Floor(1.0 / tolerance) + 1;
        var used = new SortedSet<(int R, int G, int B)>();
        for (int i = 0; i < rgb.GetLength(0); i++)
        {
            used.Add((
                Math.Clamp((int)Math.Round(rgb[i, 0] * (levels - 1)), 0, levels - 1),
                Math.Clamp((int)Math.Round(rgb[i, 1] * (levels - 1)), 0, levels - 1),
                Math.Clamp((int)Math.Round(rgb[i, 2] * (levels - 1)), 0, levels - 1)));
        }

        var map = new double[used.Count, 3];
        int row = 0;
        foreach ((int r, int g, int b) in used)
        {
            map[row, 0] = r / (double)(levels - 1);
            map[row, 1] = g / (double)(levels - 1);
            map[row, 2] = b / (double)(levels - 1);
            row++;
        }

        return map;
    }

    /// <summary>A trailing <c>'dither'</c> (the default) or <c>'nodither'</c> word.</summary>
    private static bool DitherOption(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        for (int i = 1; i < args.Count; i++)
        {
            if (args[i].Type != JgsType.String)
            {
                continue;
            }

            string word = args[i].AsString.ToLowerInvariant();
            if (word == "dither")
            {
                return true;
            }

            if (word == "nodither")
            {
                return false;
            }

            throw new JgsRuntimeException(line, col,
                $"{name}: unknown option '{args[i].AsString}' (use 'dither' or 'nodither').");
        }

        return true;
    }

    /// <summary>Wraps a plane of palette indices as the shaped matrix a script indexes with.</summary>
    private static JgsValue IndexValue(double[] indices, int height, int width) =>
        JgsMatrix.Build(height, width, (r, c) => indices[(r * width) + c]);

    /// <summary>An index plane and its shape, read from an image or a matrix of palette indices.</summary>
    private static (double[] Indices, int Height, int Width, bool FromMatrix) IndexPlane(
        string name, IReadOnlyList<JgsValue> args, int index, int line, int col, JgsDialect dialect)
    {
        using ImgArg source = ImgLike(name, args, index, line, col);
        ImageBuffer buffer = source.Buffer;
        if (buffer.Channels != 1)
        {
            throw new JgsRuntimeException(line, col, $"{name} expects a plane of indices, not an RGB image.");
        }

        var indices = new double[buffer.Height * buffer.Width];
        for (int r = 0; r < buffer.Height; r++)
        {
            for (int c = 0; c < buffer.Width; c++)
            {
                double raw = source.FromMatrix ? buffer[r, c, 0] : buffer.Class.ToNative(buffer[r, c, 0]);

                // Indices are written in whatever base the dialect counts from, so they can be used
                // as subscripts into the colormap without arithmetic; the palette code works in one.
                indices[(r * buffer.Width) + c] = raw + 1 - dialect.IndexBase;
            }
        }

        return (indices, buffer.Height, buffer.Width, source.FromMatrix);
    }

    /// <summary>An n×3 colormap argument.</summary>
    private static double[,] ColormapRows(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        double[,] map = Rectangle($"{name} argument {index + 1}", args[index], line, col);
        if (map.GetLength(1) != 3)
        {
            throw new JgsRuntimeException(line, col, $"{name} expects an n-by-3 colormap.");
        }

        return map;
    }
}
