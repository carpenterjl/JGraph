using JGraph.Imaging;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M46 wave F: structuring elements, the reconstruction family, the binary neighbourhood operations,
/// and the three distance transforms.
/// </summary>
/// <remarks>
/// <para>
/// Wave F is where the imaging surface stops being a collection of filters and starts being able to
/// answer questions about shape. Almost everything here is defined in terms of two primitives —
/// erosion over a structuring element, and reconstruction of a marker under a mask — so the file is
/// mostly the naming of compositions: a top hat is a picture minus its own opening, border clearing is
/// a reconstruction from the border subtracted, extended maxima are the regional maxima of the
/// h-maxima transform.
/// </para>
/// <para>
/// <c>strel</c> becomes a tagged struct here, the same device wave C used for the geometric
/// transforms: MATLAB ships it as a class, JGraph has no object system, and a struct whose
/// <c>Type</c> field says <c>'strel'</c> gives a script <c>se.Neighborhood</c> and
/// <c>class(se)</c> without one. The old matrix form still works everywhere an element is taken,
/// because it is what every JGS script written before this wave passes.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    private static readonly OptionSpec ImFillSpec = new("imfill", ["holes"], [], StringPositionals: 0);

    private static readonly OptionSpec BwSkelSpec = new("bwskel", [], ["MinBranchLength"]);

    private static readonly OptionSpec BwDistSpec = new(
        "bwdist", ["euclidean", "cityblock", "chessboard", "quasi-euclidean"], []);

    private static readonly OptionSpec BwUlterodeSpec = new(
        "bwulterode", ["euclidean", "cityblock", "chessboard", "quasi-euclidean"], []);

    private static void DefineMorphologyBuiltins(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> define, JgsDialect dialect)
    {
        // --- Structuring elements ----------------------------------------------------------------
        define("strel", (args, line, col) =>
        {
            ArityRange("strel", args, 1, 3, line, col);

            // strel(nhood) with no shape word: the neighbourhood itself, which is how a script hands
            // over an element it built by hand.
            if (args[0].Type != JgsType.String)
            {
                Arity("strel", args, 1, line, col);
                return ElementValue(
                    StructuringElement.Arbitrary(Matrix("strel", args, 0, line, col)), "strel");
            }

            string shape = Str("strel", args, 0, line, col).ToLowerInvariant();
            double[] size = args.Count >= 2 ? NumericVector("strel", args, 1, line, col) : [];
            double second = args.Count >= 3 ? Num("strel", args, 2, line, col) : double.NaN;

            try
            {
                StructuringElement element = shape switch
                {
                    "square" => StructuringElement.Square(Whole("strel", size, 0, 3, line, col)),
                    "rectangle" => StructuringElement.Rectangle(
                        Whole("strel", size, 0, 3, line, col), Whole("strel", size, 1, 3, line, col)),
                    "disk" => StructuringElement.Disk(Whole("strel", size, 0, 1, line, col)),
                    "diamond" => StructuringElement.Diamond(Whole("strel", size, 0, 3, line, col)),
                    "octagon" => StructuringElement.Octagon(Whole("strel", size, 0, 3, line, col)),
                    "line" => StructuringElement.Line(
                        size.Length > 0 ? size[0] : 3, double.IsNaN(second) ? 0 : second),
                    "cube" => StructuringElement.Cube(Whole("strel", size, 0, 3, line, col)),
                    "cuboid" => StructuringElement.Cuboid(
                        Whole("strel", size, 0, 3, line, col),
                        Whole("strel", size, 1, 3, line, col),
                        Whole("strel", size, 2, 3, line, col)),
                    "sphere" => StructuringElement.Sphere(Whole("strel", size, 0, 1, line, col)),
                    "arbitrary" => StructuringElement.Arbitrary(Matrix("strel", args, 1, line, col)),
                    _ => throw new JgsRuntimeException(line, col,
                        $"strel: unknown shape '{shape}' (one of: 'square', 'rectangle', 'disk', " +
                        "'diamond', 'octagon', 'line', 'cube', 'cuboid', 'sphere', 'arbitrary')."),
                };

                return ElementValue(element, "strel");
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"strel: {ex.Message}");
            }
        });

        define("offsetstrel", (args, line, col) =>
        {
            ArityRange("offsetstrel", args, 1, 4, line, col);
            if (args[0].Type != JgsType.String)
            {
                Arity("offsetstrel", args, 1, line, col);
                return ElementValue(
                    StructuringElement.Offset(Matrix("offsetstrel", args, 0, line, col)), "offsetstrel");
            }

            string shape = Str("offsetstrel", args, 0, line, col).ToLowerInvariant();
            try
            {
                StructuringElement element = shape switch
                {
                    "ball" => StructuringElement.Ball(
                        Num("offsetstrel", args, 1, line, col), Num("offsetstrel", args, 2, line, col)),
                    "offset" => StructuringElement.Offset(Matrix("offsetstrel", args, 1, line, col)),
                    _ => throw new JgsRuntimeException(line, col,
                        $"offsetstrel: unknown shape '{shape}' (use 'ball' or 'offset')."),
                };

                return ElementValue(element, "offsetstrel");
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"offsetstrel: {ex.Message}");
            }
        });

        define("conndef", (args, line, col) =>
        {
            ArityRange("conndef", args, 1, 2, line, col);
            int rank = Count("conndef", args, 0, line, col);
            string kind = args.Count == 2 ? Str("conndef", args, 1, line, col).ToLowerInvariant() : "maximal";
            bool minimal = kind switch
            {
                "minimal" => true,
                "maximal" => false,
                _ => throw new JgsRuntimeException(line, col,
                    $"conndef: unknown type '{kind}' (use 'minimal' or 'maximal')."),
            };

            if (rank == 2)
            {
                return MatrixToRows(BinaryMorphology.ConnectivityDefinition(minimal));
            }

            if (rank != 3)
            {
                throw new JgsRuntimeException(line, col, "conndef: the rank is 2 or 3.");
            }

            double[,,] cube = BinaryMorphology.ConnectivityDefinition3(minimal);
            var flat = new double[27];
            for (int p = 0; p < 3; p++)
            {
                for (int c = 0; c < 3; c++)
                {
                    for (int r = 0; r < 3; r++)
                    {
                        flat[r + (c * 3) + (p * 9)] = cube[r, c, p];
                    }
                }
            }

            return JgsMatrix.FromColumnMajorDims(flat, [3, 3, 3]);
        });

        define("iptcheckconn", (args, line, col) =>
        {
            ArityRange("iptcheckconn", args, 1, 4, line, col);
            string caller = args.Count >= 2 ? Str("iptcheckconn", args, 1, line, col) : "iptcheckconn";
            string variable = args.Count >= 3 ? Str("iptcheckconn", args, 2, line, col) : "CONN";
            if (!BinaryMorphology.IsConnectivity(
                    Rectangle("iptcheckconn argument 1", args[0], line, col)))
            {
                throw new JgsRuntimeException(line, col,
                    $"{caller}: {variable} must be 1, 4, 8, 6, 18 or 26, or a symmetric odd-sized " +
                    "array of 0s and 1s with a 1 at its centre.");
            }

            return JgsValue.Null;
        });

        // --- Erosion, dilation, and the compositions of them --------------------------------------
        define("imerode", (args, line, col) => MorphOp("imerode", args, line, col, Morphology.Erode));
        define("imdilate", (args, line, col) => MorphOp("imdilate", args, line, col, Morphology.Dilate));
        define("imopen", (args, line, col) => MorphOp("imopen", args, line, col, Morphology.Open));
        define("imclose", (args, line, col) => MorphOp("imclose", args, line, col, Morphology.Close));
        define("imtophat", (args, line, col) => MorphOp("imtophat", args, line, col, Morphology.TopHat));
        define("imbothat", (args, line, col) => MorphOp("imbothat", args, line, col, Morphology.BottomHat));

        define("bwhitmiss", (args, line, col) =>
        {
            ArityRange("bwhitmiss", args, 2, 3, line, col);
            using ImgArg source = ImgLike("bwhitmiss", args, 0, line, col);

            StructuringElement hits;
            StructuringElement misses;
            if (args.Count == 3)
            {
                hits = ReadElement("bwhitmiss", args, 1, line, col);
                misses = ReadElement("bwhitmiss", args, 2, line, col);
            }
            else
            {
                // The interval form: one matrix of 1 (must be foreground), −1 (must be background)
                // and 0 (do not care), which is the compact way to write a hit-or-miss template.
                double[,] interval = Matrix("bwhitmiss", args, 1, line, col);
                int rows = interval.GetLength(0);
                int cols = interval.GetLength(1);
                var wanted = new double[rows, cols];
                var forbidden = new double[rows, cols];
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        wanted[r, c] = interval[r, c] > 0 ? 1.0 : 0.0;
                        forbidden[r, c] = interval[r, c] < 0 ? 1.0 : 0.0;
                    }
                }

                hits = StructuringElement.Arbitrary(wanted);
                misses = StructuringElement.Arbitrary(forbidden);
            }

            try
            {
                return ImgMaskOut(Morphology.HitMiss(source.Buffer, hits, misses), source);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"bwhitmiss: {ex.Message}");
            }
        });

        // --- Reconstruction and everything defined through it -------------------------------------
        define("imreconstruct", (args, line, col) =>
        {
            ArityRange("imreconstruct", args, 2, 3, line, col);
            using ImgArg marker = ImgLike("imreconstruct", args, 0, line, col);
            using ImgArg mask = ImgLike("imreconstruct", args, 1, line, col);
            int connectivity = args.Count == 3 ? Connectivity("imreconstruct", args, 2, line, col) : 8;
            try
            {
                return ImgLikeOut(
                    MorphologicalReconstruction.Reconstruct(marker.Buffer, mask.Buffer, connectivity), mask);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"imreconstruct: {ex.Message}");
            }
        });

        define("imclearborder", (args, line, col) =>
        {
            ArityRange("imclearborder", args, 1, 2, line, col);
            using ImgArg source = ImgLike("imclearborder", args, 0, line, col);
            int connectivity = args.Count == 2 ? Connectivity("imclearborder", args, 1, line, col) : 8;
            return ImgLikeOut(
                MorphologicalReconstruction.ClearBorder(source.Buffer, connectivity), source);
        });

        define("imhmax", (args, line, col) => HExtremum("imhmax", args, line, col, MorphologicalReconstruction.HMax));
        define("imhmin", (args, line, col) => HExtremum("imhmin", args, line, col, MorphologicalReconstruction.HMin));

        define("imextendedmax", (args, line, col) => HExtremum(
            "imextendedmax", args, line, col, MorphologicalReconstruction.ExtendedMax, mask: true));
        define("imextendedmin", (args, line, col) => HExtremum(
            "imextendedmin", args, line, col, MorphologicalReconstruction.ExtendedMin, mask: true));

        define("imregionalmax", (args, line, col) => Regional("imregionalmax", args, line, col, true));
        define("imregionalmin", (args, line, col) => Regional("imregionalmin", args, line, col, false));

        define("imimposemin", (args, line, col) =>
        {
            ArityRange("imimposemin", args, 2, 3, line, col);
            using ImgArg source = ImgLike("imimposemin", args, 0, line, col);
            using ImgArg marker = ImgLike("imimposemin", args, 1, line, col);
            int connectivity = args.Count == 3 ? Connectivity("imimposemin", args, 2, line, col) : 8;
            try
            {
                return ImgLikeOut(
                    MorphologicalReconstruction.ImposeMin(source.Buffer, marker.Buffer, connectivity), source);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"imimposemin: {ex.Message}");
            }
        });

        define("imfill", (args, line, col) =>
        {
            ArityRange("imfill", args, 1, 3, line, col);
            ParsedArgs parsed = ImFillSpec.Parse(args, 2, line, col);
            if (parsed.Positional.Count < 1)
            {
                throw new JgsRuntimeException(line, col, "imfill needs an image.");
            }

            using ImgArg source = ImgLike("imfill", parsed.Positional, 0, line, col);
            bool holes = parsed.Has("holes") || parsed.Positional.Count == 1;

            try
            {
                if (holes)
                {
                    // With 'holes' the trailing argument is a connectivity; without it, it is the list
                    // of places to fill from — MATLAB's own way of telling the two forms apart.
                    int connectivity = parsed.Positional.Count >= 2
                        ? Connectivity("imfill", parsed.Positional, 1, line, col)
                        : 4;
                    return ImgLikeOut(
                        MorphologicalReconstruction.FillHoles(source.Buffer, connectivity), source);
                }

                IReadOnlyList<(int Row, int Col)> seeds = SeedPixels(
                    "imfill", parsed.Positional[1], source.Buffer, dialect, line, col);
                int seedConnectivity = parsed.Positional.Count >= 3
                    ? Connectivity("imfill", parsed.Positional, 2, line, col)
                    : 4;
                return ImgLikeOut(
                    MorphologicalReconstruction.FillFrom(source.Buffer, seeds, seedConnectivity), source);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"imfill: {ex.Message}");
            }
        });

        // --- The neighbourhood table and what rides on it -----------------------------------------
        define("makelut", (args, line, col) =>
        {
            ArityRange("makelut", args, 1, 2, line, col);
            IJgsCallable rule = Callable("makelut", args, 0, line, col);
            int order = args.Count == 2 ? Count("makelut", args, 1, line, col) : 3;
            try
            {
                return Numbers(BinaryMorphology.MakeLut(
                    window => NumOf("makelut", rule.Call([MatrixToRows(ToNumbers(window))], line, col), line, col),
                    order));
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new JgsRuntimeException(line, col, $"makelut: {ex.Message}");
            }
        });

        define("bwlookup", (args, line, col) => Lookup("bwlookup", args, line, col));
        define("applylut", (args, line, col) => Lookup("applylut", args, line, col));

        define("bwperim", (args, line, col) =>
        {
            ArityRange("bwperim", args, 1, 2, line, col);
            using ImgArg source = ImgLike("bwperim", args, 0, line, col);
            int connectivity = args.Count == 2 ? Connectivity("bwperim", args, 1, line, col) : 4;
            return ImgMaskOut(BinaryMorphology.Perimeter(source.Buffer, connectivity), source);
        });

        define("bwmorph", (args, line, col) =>
        {
            ArityRange("bwmorph", args, 2, 3, line, col);
            using ImgArg source = ImgLike("bwmorph", args, 0, line, col);
            string operation = Str("bwmorph", args, 1, line, col);
            int iterations = 1;
            if (args.Count == 3)
            {
                double n = Num("bwmorph", args, 2, line, col);
                iterations = double.IsPositiveInfinity(n) ? int.MaxValue : (int)Math.Round(n);
            }

            try
            {
                return ImgMaskOut(BinaryMorphology.Morph(source.Buffer, operation, iterations), source);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"bwmorph: {ex.Message}");
            }
        });

        define("bwskel", (args, line, col) =>
        {
            ArityRange("bwskel", args, 1, 3, line, col);
            ParsedArgs parsed = BwSkelSpec.Parse(args, 1, line, col);
            if (parsed.Positional.Count < 1)
            {
                throw new JgsRuntimeException(line, col, "bwskel needs a binary image.");
            }

            using ImgArg source = ImgLike("bwskel", parsed.Positional, 0, line, col);
            int minBranch = (int)Math.Round(parsed.Scalar("MinBranchLength", 0));
            try
            {
                return ImgMaskOut(BinaryMorphology.Skeleton(source.Buffer, minBranch), source);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"bwskel: {ex.Message}");
            }
        });

        define("bwulterode", (args, line, col) =>
        {
            ArityRange("bwulterode", args, 1, 3, line, col);
            ParsedArgs parsed = BwUlterodeSpec.Parse(args, 1, line, col);
            if (parsed.Positional.Count < 1)
            {
                throw new JgsRuntimeException(line, col, "bwulterode needs a binary image.");
            }

            using ImgArg source = ImgLike("bwulterode", parsed.Positional, 0, line, col);
            DistanceTransforms.Metric metric = MetricOf(parsed.OneOf(
                "euclidean", "euclidean", "cityblock", "chessboard", "quasi-euclidean"));
            return ImgMaskOut(BinaryMorphology.UltimateErode(source.Buffer, metric), source);
        });

        // --- Distance -----------------------------------------------------------------------------
        define("bwdist", (args, line, col) => BwDistOutputs(args, 1, line, col, dialect)[0]);

        define("bwdistgeodesic", (args, line, col) =>
        {
            ArityRange("bwdistgeodesic", args, 2, 4, line, col);
            using ImgArg source = ImgLike("bwdistgeodesic", args, 0, line, col);
            (IReadOnlyList<(int Row, int Col)> seeds, DistanceTransforms.Metric metric) =
                SeedsAndMetric("bwdistgeodesic", args, source.Buffer, dialect, line, col);
            try
            {
                double[] distance = DistanceTransforms.Geodesic(source.Buffer, seeds, metric);
                return DistanceValue(distance, source.Buffer);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"bwdistgeodesic: {ex.Message}");
            }
        });

        define("graydist", (args, line, col) =>
        {
            ArityRange("graydist", args, 2, 4, line, col);
            using ImgArg source = ImgLike("graydist", args, 0, line, col);
            (IReadOnlyList<(int Row, int Col)> seeds, DistanceTransforms.Metric metric) =
                SeedsAndMetric("graydist", args, source.Buffer, dialect, line, col);
            try
            {
                double[] distance = DistanceTransforms.GrayWeighted(source.Buffer, seeds, metric);
                return DistanceValue(distance, source.Buffer);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"graydist: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// The body of <c>bwdist</c>, shared by the plain call and the <c>[D, idx] = bwdist(BW)</c> form.
    /// </summary>
    private static JgsValue[] BwDistOutputs(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col, JgsDialect dialect)
    {
        ArityRange("bwdist", args, 1, 2, line, col);
        ParsedArgs parsed = BwDistSpec.Parse(args, 1, line, col);
        if (parsed.Positional.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "bwdist needs a binary image.");
        }

        using ImgArg source = ImgLike("bwdist", parsed.Positional, 0, line, col);
        DistanceTransforms.Metric metric = MetricOf(parsed.OneOf(
            "euclidean", "euclidean", "cityblock", "chessboard", "quasi-euclidean"));

        (double[] distance, int[] nearest) = DistanceTransforms.Transform(source.Buffer, metric);
        JgsValue map = DistanceValue(distance, source.Buffer);
        if (wanted < 2)
        {
            return [map];
        }

        // The index answers "which seed", so it is quoted the way this dialect numbers pixels: a
        // column-major linear index under MATLAB, the row-major flat one JGS uses everywhere else.
        int h = source.Buffer.Height;
        int w = source.Buffer.Width;
        var indices = new double[distance.Length];
        for (int i = 0; i < indices.Length; i++)
        {
            if (nearest[i] < 0)
            {
                indices[i] = 0;
                continue;
            }

            int r = nearest[i] / w;
            int c = nearest[i] % w;
            indices[i] = dialect.IsMatlab ? (c * h) + r + 1 : nearest[i];
        }

        return [map, DistanceValue(indices, source.Buffer)];
    }

    /// <summary>Wraps a flat row-major plane of measurements as the matrix a script reads.</summary>
    private static JgsValue DistanceValue(double[] plane, ImageBuffer shape)
    {
        var values = new double[shape.Height, shape.Width];
        for (int r = 0; r < shape.Height; r++)
        {
            for (int c = 0; c < shape.Width; c++)
            {
                values[r, c] = plane[(r * shape.Width) + c];
            }
        }

        return MatrixToRows(values);
    }

    /// <summary>The seed list and metric shared by <c>bwdistgeodesic</c> and <c>graydist</c>.</summary>
    private static (IReadOnlyList<(int Row, int Col)> Seeds, DistanceTransforms.Metric Metric) SeedsAndMetric(
        string name, IReadOnlyList<JgsValue> args, ImageBuffer image, JgsDialect dialect, int line, int col)
    {
        // Both take either a mask, a list of places, or a pair of column and row vectors, and the
        // trailing method word is optional in each. Reading from the end is what disentangles them.
        int last = args.Count - 1;
        DistanceTransforms.Metric metric = DistanceTransforms.Metric.Chessboard;
        if (args[last].Type == JgsType.String)
        {
            metric = MetricOf(Str(name, args, last, line, col).ToLowerInvariant());
            last--;
        }

        if (last == 2)
        {
            double[] columns = NumericVector(name, args[1], line, col);
            double[] rows = NumericVector(name, args[2], line, col);
            if (columns.Length != rows.Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: the column and row vectors must be the same length.");
            }

            var pairs = new List<(int Row, int Col)>(rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                pairs.Add(Subscript(rows[i], columns[i], image, dialect, line, col, $"{name}: the seed"));
            }

            return (pairs, metric);
        }

        if (last != 1)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} takes a mask, a list of places, or a column and a row vector.");
        }

        return (SeedPixels(name, args[1], image, dialect, line, col), metric);
    }

    /// <summary>
    /// The places an operation starts from: a mask the size of the picture, a list of linear indices,
    /// or an n×2 array of subscripts — the three forms MATLAB accepts wherever it says "locations".
    /// </summary>
    private static IReadOnlyList<(int Row, int Col)> SeedPixels(
        string name, JgsValue value, ImageBuffer image, JgsDialect dialect, int line, int col)
    {
        var pairs = new List<(int Row, int Col)>();
        if (value.Type == JgsType.Image)
        {
            ImageBuffer mask = value.AsImage;
            if (mask.Height != image.Height || mask.Width != image.Width)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: the seed mask is {mask.Height}x{mask.Width} but the image is " +
                    $"{image.Height}x{image.Width}.");
            }

            for (int r = 0; r < mask.Height; r++)
            {
                for (int c = 0; c < mask.Width; c++)
                {
                    if (mask[r, c, 0] != 0)
                    {
                        pairs.Add((r, c));
                    }
                }
            }

            return pairs;
        }

        double[,] places = Rectangle($"{name} locations", value, line, col);
        int rows = places.GetLength(0);
        int cols = places.GetLength(1);

        if (rows == image.Height && cols == image.Width && !(rows == 1 && cols == 2))
        {
            // Same shape as the picture: a mask, not a list. The one-pixel-tall exception is there
            // because a 1×2 picture would otherwise make [row col] unreadable.
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (places[r, c] != 0)
                    {
                        pairs.Add((r, c));
                    }
                }
            }

            return pairs;
        }

        if (cols == 2)
        {
            for (int r = 0; r < rows; r++)
            {
                pairs.Add(Subscript(places[r, 0], places[r, 1], image, dialect, line, col, $"{name}: a location"));
            }

            return pairs;
        }

        // A plain vector of linear indices, counted the way this dialect counts pixels.
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int index = (int)Math.Round(places[r, c]) - dialect.IndexBase;
                int total = image.Height * image.Width;
                if (index < 0 || index >= total)
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name}: index {places[r, c]} is outside the {image.Height}x{image.Width} image.");
                }

                pairs.Add(dialect.IsMatlab
                    ? (index % image.Height, index / image.Height)
                    : (index / image.Width, index % image.Width));
            }
        }

        return pairs;
    }

    /// <summary>Applies a lookup table, under either of the two names MATLAB gives the operation.</summary>
    private static JgsValue Lookup(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity(name, args, 2, line, col);
        using ImgArg source = ImgLike(name, args, 0, line, col);
        double[] table = ToDoubles(name, args[1], line, col);
        try
        {
            ImageBuffer result = BinaryMorphology.ApplyLut(source.Buffer, table);
            return result.IsBinary ? ImgMaskOut(result, source) : ImgLikeOut(result, source);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"{name}: {ex.Message}");
        }
    }

    private static JgsValue Regional(string name, IReadOnlyList<JgsValue> args, int line, int col, bool maxima)
    {
        ArityRange(name, args, 1, 2, line, col);
        using ImgArg source = ImgLike(name, args, 0, line, col);
        int connectivity = args.Count == 2 ? Connectivity(name, args, 1, line, col) : 8;
        ImageBuffer result = maxima
            ? MorphologicalReconstruction.RegionalMax(source.Buffer, connectivity)
            : MorphologicalReconstruction.RegionalMin(source.Buffer, connectivity);
        return ImgMaskOut(result, source);
    }

    private static JgsValue HExtremum(
        string name, IReadOnlyList<JgsValue> args, int line, int col,
        Func<ImageBuffer, double, int, ImageBuffer> op, bool mask = false)
    {
        ArityRange(name, args, 2, 3, line, col);
        using ImgArg source = ImgLike(name, args, 0, line, col);
        double h = Num(name, args, 1, line, col);
        int connectivity = args.Count == 3 ? Connectivity(name, args, 2, line, col) : 8;
        try
        {
            ImageBuffer result = op(source.Buffer, h, connectivity);
            return mask ? ImgMaskOut(result, source) : ImgLikeOut(result, source);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new JgsRuntimeException(line, col, $"{name}: {ex.Message}");
        }
    }

    /// <summary>The body every erosion-shaped builtin shares.</summary>
    private static JgsValue MorphOp(
        string name, IReadOnlyList<JgsValue> args, int line, int col,
        Func<ImageBuffer, StructuringElement, ImageBuffer> op)
    {
        ArityRange(name, args, 1, 2, line, col);
        using ImgArg source = ImgLike(name, args, 0, line, col);
        StructuringElement element = args.Count == 2
            ? ReadElement(name, args, 1, line, col)
            : StructuringElement.Square(3);
        try
        {
            return ImgLikeOut(op(source.Buffer, element), source);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"{name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Reads a structuring-element argument: the tagged struct <c>strel</c> builds, or a plain 0/1
    /// matrix, which is what every JGS script written before this wave passes.
    /// </summary>
    private static StructuringElement ReadElement(
        string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        JgsValue value = args[index];
        if (value.Type == JgsType.Struct)
        {
            Dictionary<string, JgsValue> fields = value.AsStruct;
            if (fields.TryGetValue("Offset", out JgsValue? offset) && offset is not null &&
                offset.Type != JgsType.Null)
            {
                return StructuringElement.Offset(
                    Rectangle($"{name} argument {index + 1}", offset, line, col));
            }

            if (fields.TryGetValue("Neighborhood", out JgsValue? nhood) && nhood is not null)
            {
                return StructuringElement.Arbitrary(
                    Rectangle($"{name} argument {index + 1}", nhood, line, col));
            }

            throw new JgsRuntimeException(line, col,
                $"{name} expects argument {index + 1} to be a structuring element (build one with strel).");
        }

        return StructuringElement.Arbitrary(Matrix(name, args, index, line, col));
    }

    /// <summary>Wraps a structuring element as the tagged struct a script sees.</summary>
    private static JgsValue ElementValue(StructuringElement element, string type)
    {
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            [TransformTag] = JgsValue.Str(type),
            ["Neighborhood"] = NeighborhoodValue(element),
            ["Dimensionality"] = JgsValue.Number(element.Is3D ? 3 : 2),
        };

        if (!element.IsFlat)
        {
            fields["Offset"] = MatrixToRows(element.ToOffsetMatrix());
        }

        return JgsValue.Struct(fields);
    }

    private static JgsValue NeighborhoodValue(StructuringElement element)
    {
        if (!element.Is3D)
        {
            return MatrixToRows(element.ToMatrix());
        }

        var flat = new double[element.Rows * element.Cols * element.Pages];
        for (int p = 0; p < element.Pages; p++)
        {
            for (int c = 0; c < element.Cols; c++)
            {
                for (int r = 0; r < element.Rows; r++)
                {
                    flat[r + (c * element.Rows) + (p * element.Rows * element.Cols)] =
                        element.Member(r, c, p) ? 1.0 : 0.0;
                }
            }
        }

        return JgsMatrix.FromColumnMajorDims(flat, [element.Rows, element.Cols, element.Pages]);
    }

    /// <summary>
    /// A connectivity argument: 4 or 8, or the 3×3 neighbourhood matrix MATLAB also accepts, which is
    /// read as one of the two by whether its corners take part.
    /// </summary>
    private static int Connectivity(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        JgsValue value = args[index];
        if (value.Type is JgsType.Number or JgsType.Bool)
        {
            int connectivity = (int)Math.Round(value.AsNumber);
            if (connectivity is not (4 or 8))
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: connectivity is 4 or 8 for a picture, not {connectivity}.");
            }

            return connectivity;
        }

        double[,] neighbourhood = Matrix(name, args, index, line, col);
        if (neighbourhood.GetLength(0) != 3 || neighbourhood.GetLength(1) != 3)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: a connectivity given as an array is 3-by-3.");
        }

        bool corners = neighbourhood[0, 0] != 0 || neighbourhood[0, 2] != 0 ||
                       neighbourhood[2, 0] != 0 || neighbourhood[2, 2] != 0;
        return corners ? 8 : 4;
    }

    private static DistanceTransforms.Metric MetricOf(string word) => word switch
    {
        "cityblock" => DistanceTransforms.Metric.CityBlock,
        "chessboard" => DistanceTransforms.Metric.Chessboard,
        "quasi-euclidean" => DistanceTransforms.Metric.QuasiEuclidean,
        _ => DistanceTransforms.Metric.Euclidean,
    };

    private static double[,] ToNumbers(bool[,] window)
    {
        int rows = window.GetLength(0);
        int cols = window.GetLength(1);
        var values = new double[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                values[r, c] = window[r, c] ? 1.0 : 0.0;
            }
        }

        return values;
    }

    private static int Whole(string name, double[] size, int index, int fallback, int line, int col)
    {
        if (size.Length == 0)
        {
            return fallback;
        }

        if (index >= size.Length)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: this shape needs {index + 1} size values, but got {size.Length}.");
        }

        return (int)Math.Round(size[index]);
    }
}
