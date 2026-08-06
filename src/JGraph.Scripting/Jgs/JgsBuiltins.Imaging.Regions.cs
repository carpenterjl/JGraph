using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Imaging;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M46 wave G: the full <c>regionprops</c> property set, connected-component analysis, boundaries,
/// thresholding and quantizing, watershed and the seeded segmenters, superpixels, active contours,
/// regions of interest, and the label displays.
/// </summary>
/// <remarks>
/// <para>
/// This is the wave where measurements stop being numbers and start being structures. A region has an
/// area, but it also has a pixel list, a convex hull, eight extrema and its own cropped mask, and a
/// table cannot hold those. Under the MATLAB dialect <c>regionprops</c> therefore returns a struct
/// array — a cell of structs, which is how M41 represents one — so <c>stats(3).ConvexHull</c> and
/// <c>[stats.Area]</c> read exactly as they do in MATLAB. JGS keeps the Table it has always had, with
/// the scalar properties as columns.
/// </para>
/// <para>
/// Coordinates are the one thing that differs by dialect throughout: MATLAB quotes centroids,
/// boundaries and circle centres as 1-based <c>[x y]</c>, and JGS as the 0-based pixel coordinates
/// ADR 0028 fixed. The algorithms below JGraph.Imaging know nothing about either; the shift happens
/// once, at the return.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    private static readonly OptionSpec BwAreaFiltSpec = new("bwareafilt", ["largest", "smallest"], []);

    // The property word is positional, not an option — 'Area' has to reach the body rather than be
    // reported as an unrecognized option.
    private static readonly OptionSpec BwPropFiltSpec = new(
        "bwpropfilt", ["largest", "smallest"], [], StringPositionals: 3);

    private static readonly OptionSpec SuperpixelsSpec = new(
        "superpixels", [], ["Compactness", "IsInputLab", "Method", "NumIterations"]);

    private static readonly OptionSpec KMeansSpec = new(
        "imsegkmeans", [], ["NormalizeInput", "NumAttempts", "MaxIterations", "Threshold"]);

    private static readonly OptionSpec ActiveContourSpec = new(
        "activecontour", ["Chan-Vese", "edge"], ["SmoothFactor", "ContractionBias"]);

    private static readonly OptionSpec FindCirclesSpec = new(
        "imfindcircles", [], ["ObjectPolarity", "Sensitivity", "EdgeThreshold", "Method"]);

    private static readonly OptionSpec LabelOverlaySpec = new(
        "labeloverlay", [], ["Colormap", "IncludedLabels", "Transparency"]);

    private static readonly OptionSpec HoughSpec = new("hough", [], ["Theta", "RhoResolution"]);

    private static readonly OptionSpec HoughPeaksSpec = new(
        "houghpeaks", [], ["Threshold", "NHoodSize"]);

    private static readonly OptionSpec VisSpec = new(
        "viscircles", [], ["Color", "LineWidth", "LineStyle", "EnhanceVisibility"]);

    private static void DefineRegionBuiltins(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> define,
        Random random,
        JgsDialect dialect)
    {
        // --- Measurement --------------------------------------------------------------------------
        define("regionprops", (args, line, col) => RegionPropsValue(args, line, col, dialect));

        define("bwconncomp", (args, line, col) =>
        {
            ArityRange("bwconncomp", args, 1, 2, line, col);

            // MATLAB documents bwconncomp as N-D, so a three-dimensional array is a volume here even
            // though wave A reads the same shape as colour planes: a mask has no colour to read.
            if (IsVolumeArg(args[0]))
            {
                return ComponentsOfVolume(args, line, col, dialect);
            }

            using ImgArg source = ImgLike("bwconncomp", args, 0, line, col);
            int connectivity = args.Count == 2 ? Connectivity("bwconncomp", args, 1, line, col) : 8;
            (int[,] labels, int count) = Regions.Label(source.Buffer, connectivity);
            return ConnectedComponents(labels, count, source.Buffer.Height, source.Buffer.Width,
                connectivity, dialect);
        });

        define("labelmatrix", (args, line, col) =>
        {
            Arity("labelmatrix", args, 1, line, col);
            (int[,] labels, _) = LabelsFromComponents("labelmatrix", args[0], dialect, line, col);
            return MatrixToRows(ToDoubleMatrix(labels));
        });

        define("label2idx", (args, line, col) =>
        {
            Arity("label2idx", args, 1, line, col);
            using ImgArg source = ImgLike("label2idx", args, 0, line, col);
            (int[,] labels, int count) = ReadLabels(source.Buffer);
            List<(int Row, int Col)>[] lists = Regions.PixelLists(labels, count);
            var cells = new JgsValue[count];
            for (int i = 0; i < count; i++)
            {
                cells[i] = LinearIndices(lists[i], source.Buffer.Height, source.Buffer.Width, dialect);
            }

            return JgsValue.Cell(cells);
        });

        define("bwarea", (args, line, col) =>
        {
            Arity("bwarea", args, 1, line, col);
            using ImgArg source = ImgLike("bwarea", args, 0, line, col);
            return JgsValue.Number(RegionProperties.Area(source.Buffer));
        });

        define("bweuler", (args, line, col) =>
        {
            ArityRange("bweuler", args, 1, 2, line, col);
            using ImgArg source = ImgLike("bweuler", args, 0, line, col);
            int connectivity = args.Count == 2 ? Connectivity("bweuler", args, 1, line, col) : 8;
            return JgsValue.Number(RegionProperties.Euler(source.Buffer, connectivity));
        });

        define("bwferet", (args, line, col) =>
        {
            ArityRange("bwferet", args, 1, 2, line, col);
            using ImgArg source = ImgLike("bwferet", args, 0, line, col);
            (int[,] labels, int count) = ReadLabels(source.Buffer);
            RegionMeasurement[] measured = RegionProperties.Measure(labels, count);

            // MATLAB returns a table here in both dialects, because a table is what it returns.
            var maxDiameter = new double[count];
            var maxAngle = new double[count];
            var minDiameter = new double[count];
            var minAngle = new double[count];
            for (int i = 0; i < count; i++)
            {
                maxDiameter[i] = measured[i].MaxFeretDiameter;
                maxAngle[i] = measured[i].MaxFeretAngle;
                minDiameter[i] = measured[i].MinFeretDiameter;
                minAngle[i] = measured[i].MinFeretAngle;
            }

            string wanted = args.Count == 2 ? Str("bwferet", args, 1, line, col).ToLowerInvariant() : "all";
            var columns = new List<JGraph.Data.TableColumn>();
            if (wanted is "all" or "max")
            {
                columns.Add(new JGraph.Data.NumberColumn("MaxDiameter", maxDiameter));
                columns.Add(new JGraph.Data.NumberColumn("MaxAngle", maxAngle));
            }

            if (wanted is "all" or "min")
            {
                columns.Add(new JGraph.Data.NumberColumn("MinDiameter", minDiameter));
                columns.Add(new JGraph.Data.NumberColumn("MinAngle", minAngle));
            }

            if (columns.Count == 0)
            {
                throw new JgsRuntimeException(line, col,
                    $"bwferet: unknown property '{wanted}' (use 'all', 'max' or 'min').");
            }

            return JgsValue.Table(new JGraph.Data.Table(columns));
        });

        // --- Component selection and filtering ------------------------------------------------------
        define("bwselect", (args, line, col) =>
        {
            ArityRange("bwselect", args, 2, 4, line, col);
            using ImgArg source = ImgLike("bwselect", args, 0, line, col);
            var seeds = new List<(int Row, int Col)>();
            int connectivity = 8;

            if (args.Count >= 3 && args[1].Type != JgsType.String)
            {
                double[] columns = NumericVector("bwselect", args, 1, line, col);
                double[] rows = NumericVector("bwselect", args, 2, line, col);
                if (columns.Length != rows.Length)
                {
                    throw new JgsRuntimeException(line, col,
                        "bwselect: the column and row vectors must be the same length.");
                }

                for (int i = 0; i < rows.Length; i++)
                {
                    seeds.Add(Subscript(rows[i], columns[i], source.Buffer, dialect, line, col, "bwselect: the seed"));
                }

                if (args.Count == 4)
                {
                    connectivity = Connectivity("bwselect", args, 3, line, col);
                }
            }
            else
            {
                seeds.AddRange(SeedPixels("bwselect", args[1], source.Buffer, dialect, line, col));
                if (args.Count == 3)
                {
                    connectivity = Connectivity("bwselect", args, 2, line, col);
                }
            }

            return ImgMaskOut(Regions.Select(source.Buffer, seeds, connectivity), source);
        });

        define("bwareafilt", (args, line, col) => PropertyFilter("bwareafilt", args, line, col, dialect));
        define("bwpropfilt", (args, line, col) => PropertyFilter("bwpropfilt", args, line, col, dialect));

        // --- Boundaries ------------------------------------------------------------------------------
        define("bwboundaries", (args, line, col) => BoundariesOutputs(args, 1, line, col, dialect)[0]);

        define("bwtraceboundary", (args, line, col) =>
        {
            ArityRange("bwtraceboundary", args, 3, 6, line, col);
            using ImgArg source = ImgLike("bwtraceboundary", args, 0, line, col);
            double[] point = NumericVector("bwtraceboundary", args, 1, line, col);
            if (point.Length != 2)
            {
                throw new JgsRuntimeException(line, col,
                    "bwtraceboundary: the starting point is a [row, column] pair.");
            }

            (int startRow, int startCol) = Subscript(
                point[0], point[1], source.Buffer, dialect, line, col, "bwtraceboundary: the start");
            (int dr, int dc) = CompassStep(Str("bwtraceboundary", args, 2, line, col), line, col);
            int connectivity = args.Count >= 4 ? Connectivity("bwtraceboundary", args, 3, line, col) : 8;
            int? maxPoints = null;
            if (args.Count >= 5 && args[4].Type == JgsType.Number && !double.IsInfinity(args[4].AsNumber))
            {
                maxPoints = (int)Math.Round(args[4].AsNumber);
            }

            bool clockwise = args.Count < 6 ||
                !Str("bwtraceboundary", args, 5, line, col).Equals("counterclockwise", StringComparison.OrdinalIgnoreCase);

            try
            {
                bool[,] mask = MaskOf(source.Buffer);
                (int Row, int Col)[] trace = Boundaries.Trace(
                    mask, startRow, startCol, connectivity, startRow + dr, startCol + dc, maxPoints, clockwise);
                return TraceValue(trace, dialect);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"bwtraceboundary: {ex.Message}");
            }
        });

        define("boundarymask", (args, line, col) =>
        {
            ArityRange("boundarymask", args, 1, 2, line, col);
            using ImgArg source = ImgLike("boundarymask", args, 0, line, col);
            int connectivity = args.Count == 2 ? Connectivity("boundarymask", args, 1, line, col) : 8;
            return ImgMaskOut(Boundaries.BoundaryMask(source.Buffer, connectivity), source);
        });

        define("bwconvhull", (args, line, col) =>
        {
            ArityRange("bwconvhull", args, 1, 3, line, col);
            using ImgArg source = ImgLike("bwconvhull", args, 0, line, col);
            string method = args.Count >= 2 ? Str("bwconvhull", args, 1, line, col) : "union";
            int connectivity = args.Count >= 3 ? Connectivity("bwconvhull", args, 2, line, col) : 8;
            try
            {
                return ImgMaskOut(Boundaries.ConvexHullImage(source.Buffer, method, connectivity), source);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"bwconvhull: {ex.Message}");
            }
        });

        define("reducepoly", (args, line, col) =>
        {
            ArityRange("reducepoly", args, 1, 2, line, col);
            double[,] points = Rectangle("reducepoly argument 1", args[0], line, col);
            if (points.GetLength(1) != 2)
            {
                throw new JgsRuntimeException(line, col, "reducepoly takes an n-by-2 array of [x y] rows.");
            }

            double tolerance = args.Count == 2 ? Num("reducepoly", args, 1, line, col) : 0.001;
            var polyline = new (double X, double Y)[points.GetLength(0)];
            for (int i = 0; i < polyline.Length; i++)
            {
                polyline[i] = (points[i, 0], points[i, 1]);
            }

            (double X, double Y)[] reduced = Boundaries.Reduce(polyline, tolerance);
            return JgsMatrix.Build(reduced.Length, 2, (i, c) => c == 0 ? reduced[i].X : reduced[i].Y);
        });

        // --- Thresholding and quantizing --------------------------------------------------------------
        define("multithresh", (args, line, col) => MultiThresholdOutputs(args, 1, line, col)[0]);

        define("imquantize", (args, line, col) =>
        {
            ArityRange("imquantize", args, 2, 3, line, col);
            using ImgArg source = ImgLike("imquantize", args, 0, line, col);
            double[] thresholds = NumericVector("imquantize", args, 1, line, col);
            int[,] levels = Segmentation.Quantize(source.Buffer, thresholds);
            if (args.Count < 3)
            {
                return MatrixToRows(ToDoubleMatrix(levels));
            }

            double[] values = NumericVector("imquantize", args, 2, line, col);
            if (values.Length != thresholds.Length + 1)
            {
                throw new JgsRuntimeException(line, col,
                    $"imquantize: {thresholds.Length} thresholds need {thresholds.Length + 1} values, " +
                    $"but got {values.Length}.");
            }

            var mapped = new double[levels.GetLength(0), levels.GetLength(1)];
            for (int r = 0; r < mapped.GetLength(0); r++)
            {
                for (int c = 0; c < mapped.GetLength(1); c++)
                {
                    mapped[r, c] = values[levels[r, c] - 1];
                }
            }

            return MatrixToRows(mapped);
        });

        define("grayslice", (args, line, col) =>
        {
            ArityRange("grayslice", args, 1, 2, line, col);
            using ImgArg source = ImgLike("grayslice", args, 0, line, col);
            int levels = args.Count == 2 ? Count("grayslice", args, 1, line, col) : 10;
            return MatrixToRows(ToDoubleMatrix(Segmentation.Slice(source.Buffer, levels)));
        });

        // --- Watershed and the seeded segmenters ------------------------------------------------------
        define("watershed", (args, line, col) =>
        {
            ArityRange("watershed", args, 1, 2, line, col);
            using ImgArg source = ImgLike("watershed", args, 0, line, col);
            int connectivity = args.Count == 2 ? Connectivity("watershed", args, 1, line, col) : 8;
            return MatrixToRows(ToDoubleMatrix(Segmentation.Watershed(source.Buffer, connectivity)));
        });

        define("grayconnected", (args, line, col) =>
        {
            ArityRange("grayconnected", args, 3, 4, line, col);
            using ImgArg source = ImgLike("grayconnected", args, 0, line, col);
            (int row, int column) = Subscript(
                Num("grayconnected", args, 1, line, col), Num("grayconnected", args, 2, line, col),
                source.Buffer, dialect, line, col, "grayconnected: the seed");
            double tolerance = args.Count == 4 ? Num("grayconnected", args, 3, line, col) : 32.0 / 255.0;
            return ImgMaskOut(
                Segmentation.GrayConnected(source.Buffer, [(row, column)], tolerance), source);
        });

        define("gradientweight", (args, line, col) =>
        {
            ArityRange("gradientweight", args, 1, 2, line, col);
            using ImgArg source = ImgLike("gradientweight", args, 0, line, col);
            double sigma = args.Count == 2 ? Num("gradientweight", args, 1, line, col) : 1.5;
            return ImgLikeOut(Segmentation.GradientWeight(source.Buffer, sigma), source);
        });

        define("graydiffweight", (args, line, col) =>
        {
            ArityRange("graydiffweight", args, 2, 3, line, col);
            using ImgArg source = ImgLike("graydiffweight", args, 0, line, col);
            IReadOnlyList<(int Row, int Col)> seeds = args.Count == 3
                ? [Subscript(Num("graydiffweight", args, 2, line, col), Num("graydiffweight", args, 1, line, col),
                    source.Buffer, dialect, line, col, "graydiffweight: the seed")]
                : SeedPixels("graydiffweight", args[1], source.Buffer, dialect, line, col);
            try
            {
                return ImgLikeOut(Segmentation.GrayDifferenceWeight(source.Buffer, seeds), source);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"graydiffweight: {ex.Message}");
            }
        });

        define("imsegfmm", (args, line, col) => FastMarchOutputs(args, 1, line, col, dialect)[0]);

        // --- Clustering ---------------------------------------------------------------------------------
        define("imsegkmeans", (args, line, col) => KMeansOutputs(args, 1, line, col, random)[0]);
        define("superpixels", (args, line, col) => SuperpixelsOutputs(args, 1, line, col)[0]);

        define("activecontour", (args, line, col) =>
        {
            ArityRange("activecontour", args, 2, 8, line, col);
            ParsedArgs parsed = ActiveContourSpec.Parse(args, 3, line, col);
            if (parsed.Positional.Count < 2)
            {
                throw new JgsRuntimeException(line, col, "activecontour(A, mask) needs a picture and a mask.");
            }

            using ImgArg source = ImgLike("activecontour", parsed.Positional, 0, line, col);
            using ImgArg mask = ImgLike("activecontour", parsed.Positional, 1, line, col);
            int iterations = parsed.Positional.Count >= 3
                ? Count("activecontour", parsed.Positional, 2, line, col)
                : 100;

            ActiveContour.Method method = parsed.OneOf("Chan-Vese", "Chan-Vese", "edge") == "edge"
                ? ActiveContour.Method.Edge
                : ActiveContour.Method.ChanVese;
            double smooth = parsed.Scalar("SmoothFactor", method == ActiveContour.Method.Edge ? 1.0 : 0.0);
            double bias = parsed.Scalar("ContractionBias", method == ActiveContour.Method.Edge ? 0.3 : 0.0);

            try
            {
                return ImgMaskOut(
                    ActiveContour.Evolve(source.Buffer, mask.Buffer, iterations, method, smooth, bias), source);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"activecontour: {ex.Message}");
            }
        });

        // --- Regions of interest -------------------------------------------------------------------------
        define("poly2mask", (args, line, col) =>
        {
            Arity("poly2mask", args, 4, line, col);
            double[] xs = NumericVector("poly2mask", args, 0, line, col);
            double[] ys = NumericVector("poly2mask", args, 1, line, col);
            int height = Count("poly2mask", args, 2, line, col);
            int width = Count("poly2mask", args, 3, line, col);
            try
            {
                return ImgOut(
                    RoiOps.PolygonMask(Shift(xs, -dialect.IndexBase), Shift(ys, -dialect.IndexBase), height, width),
                    ImageClass.Logical);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"poly2mask: {ex.Message}");
            }
        });

        define("poly2label", (args, line, col) =>
        {
            ArityRange("poly2label", args, 2, 3, line, col);
            if (args[0].Type != JgsType.Cell)
            {
                throw new JgsRuntimeException(line, col,
                    "poly2label takes a cell array of n-by-2 vertex lists.");
            }

            JgsValue[] shapes = args[0].BoxedElements();
            double[] ids = NumericVector("poly2label", args, 1, line, col);
            double[] size = args.Count == 3
                ? NumericVector("poly2label", args, 2, line, col)
                : throw new JgsRuntimeException(line, col, "poly2label needs the output size.");
            if (size.Length < 2)
            {
                throw new JgsRuntimeException(line, col, "poly2label's size is [rows, cols].");
            }

            var polygons = new List<(double[] X, double[] Y)>();
            var labels = new List<int>();
            for (int i = 0; i < shapes.Length; i++)
            {
                double[,] vertices = Rectangle($"poly2label polygon {i + 1}", shapes[i], line, col);
                if (vertices.GetLength(1) != 2)
                {
                    throw new JgsRuntimeException(line, col,
                        $"poly2label: polygon {i + 1} must be an n-by-2 array of [x y] rows.");
                }

                var xs = new double[vertices.GetLength(0)];
                var ys = new double[vertices.GetLength(0)];
                for (int k = 0; k < xs.Length; k++)
                {
                    xs[k] = vertices[k, 0] - dialect.IndexBase;
                    ys[k] = vertices[k, 1] - dialect.IndexBase;
                }

                polygons.Add((xs, ys));
                labels.Add((int)Math.Round(i < ids.Length ? ids[i] : i + 1));
            }

            return MatrixToRows(ToDoubleMatrix(RoiOps.PolygonLabels(
                polygons, labels, (int)Math.Round(size[0]), (int)Math.Round(size[1]))));
        });

        define("roipoly", (args, line, col) =>
        {
            ArityRange("roipoly", args, 1, 3, line, col);
            using ImgArg source = ImgLike("roipoly", args, 0, line, col);
            if (args.Count < 3)
            {
                // MATLAB opens a window and waits for a polygon to be drawn. There is none in a batch
                // run, so the honest answer is the whole picture.
                var everything = new ImageBuffer(source.Buffer.Height, source.Buffer.Width, 1);
                everything.Pixels.Fill(1.0);
                return ImgOut(everything, ImageClass.Logical);
            }

            double[] xs = NumericVector("roipoly", args, 1, line, col);
            double[] ys = NumericVector("roipoly", args, 2, line, col);
            return ImgOut(
                RoiOps.PolygonMask(
                    Shift(xs, -dialect.IndexBase), Shift(ys, -dialect.IndexBase),
                    source.Buffer.Height, source.Buffer.Width),
                ImageClass.Logical);
        });

        define("roicolor", (args, line, col) =>
        {
            ArityRange("roicolor", args, 2, 3, line, col);
            using ImgArg source = ImgLike("roicolor", args, 0, line, col);
            if (args.Count == 3)
            {
                return ImgMaskOut(
                    RoiOps.SelectByColor(source.Buffer,
                        Num("roicolor", args, 1, line, col), Num("roicolor", args, 2, line, col)),
                    source);
            }

            return ImgMaskOut(
                RoiOps.SelectByValues(source.Buffer, NumericVector("roicolor", args, 1, line, col)), source);
        });

        define("roifilt2", (args, line, col) =>
        {
            ArityRange("roifilt2", args, 3, 3, line, col);

            // Two shapes: roifilt2(h, I, mask) filters with a kernel, roifilt2(I, mask, fun) with a
            // function. The first argument tells them apart — a kernel is a matrix, a picture is not
            // the third argument's function.
            if (args[2].Type == JgsType.Function)
            {
                using ImgArg picture = ImgLike("roifilt2", args, 0, line, col);
                using ImgArg mask = ImgLike("roifilt2", args, 1, line, col);
                IJgsCallable fun = Callable("roifilt2", args, 2, line, col);
                JgsValue answered = fun.Call([ImgLikeOut(picture.Buffer.Clone(), picture)], line, col);
                using ImgArg filtered = ImgLike("roifilt2", [answered], 0, line, col);
                return ImgLikeOut(
                    RoiOps.FilterInMask(picture.Buffer, filtered.Buffer, mask.Buffer), picture);
            }

            double[,] kernel = Matrix("roifilt2", args, 0, line, col);
            using ImgArg image = ImgLike("roifilt2", args, 1, line, col);
            using ImgArg region = ImgLike("roifilt2", args, 2, line, col);
            using ImageBuffer blurred = Filters.Filter(
                image.Buffer, kernel, Filters.Boundary.Replicate, 0.0, convolve: true);
            return ImgLikeOut(RoiOps.FilterInMask(image.Buffer, blurred, region.Buffer), image);
        });

        define("regionfill", (args, line, col) =>
        {
            ArityRange("regionfill", args, 2, 3, line, col);
            using ImgArg source = ImgLike("regionfill", args, 0, line, col);
            ImageBuffer? polygonMask = null;
            try
            {
                ImageBuffer mask;
                if (args.Count == 3)
                {
                    double[] xs = NumericVector("regionfill", args, 1, line, col);
                    double[] ys = NumericVector("regionfill", args, 2, line, col);
                    polygonMask = RoiOps.PolygonMask(
                        Shift(xs, -dialect.IndexBase), Shift(ys, -dialect.IndexBase),
                        source.Buffer.Height, source.Buffer.Width);
                    mask = polygonMask;
                }
                else
                {
                    using ImgArg given = ImgLike("regionfill", args, 1, line, col);
                    return ImgLikeOut(RoiOps.FillRegion(source.Buffer, given.Buffer), source);
                }

                return ImgLikeOut(RoiOps.FillRegion(source.Buffer, mask), source);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"regionfill: {ex.Message}");
            }
            finally
            {
                polygonMask?.Dispose();
            }
        });

        // --- Display -------------------------------------------------------------------------------------
        define("label2rgb", (args, line, col) =>
        {
            ArityRange("label2rgb", args, 1, 4, line, col);
            using ImgArg source = ImgLike("label2rgb", args, 0, line, col);
            (int[,] labels, _) = ReadLabels(source.Buffer);

            (double R, double G, double B)[]? colors = args.Count >= 2 ? ReadColormap(args[1], line, col) : null;
            (double R, double G, double B)? background =
                args.Count >= 3 ? ReadColor("label2rgb", args[2], line, col) : null;
            bool shuffle = args.Count >= 4 &&
                Str("label2rgb", args, 3, line, col).Equals("shuffle", StringComparison.OrdinalIgnoreCase);
            return ImgOut(
                LabelDisplay.LabelToRgb(labels, colors, background, shuffle, random), ImageClass.Double);
        });

        define("labeloverlay", (args, line, col) =>
        {
            ArityRange("labeloverlay", args, 2, 8, line, col);
            ParsedArgs parsed = LabelOverlaySpec.Parse(args, 2, line, col);
            if (parsed.Positional.Count < 2)
            {
                throw new JgsRuntimeException(line, col, "labeloverlay(A, L) needs a picture and labels.");
            }

            using ImgArg source = ImgLike("labeloverlay", parsed.Positional, 0, line, col);
            using ImgArg map = ImgLike("labeloverlay", parsed.Positional, 1, line, col);
            (int[,] labels, _) = ReadLabels(map.Buffer);

            (double R, double G, double B)[]? colors =
                parsed.Named("Colormap") is { } cmap ? ReadColormap(cmap, line, col) : null;
            double transparency = parsed.Scalar("Transparency", 0.65);
            double[]? included = parsed.Vector("IncludedLabels");
            int[]? wanted = null;
            if (included is not null)
            {
                wanted = new int[included.Length];
                for (int i = 0; i < included.Length; i++)
                {
                    wanted[i] = (int)Math.Round(included[i]);
                }
            }

            try
            {
                return ImgOut(
                    LabelDisplay.LabelOverlay(source.Buffer, labels, colors, transparency, wanted),
                    ImageClass.Double);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"labeloverlay: {ex.Message}");
            }
        });

        define("imoverlay", (args, line, col) =>
        {
            ArityRange("imoverlay", args, 2, 3, line, col);
            using ImgArg source = ImgLike("imoverlay", args, 0, line, col);
            using ImgArg mask = ImgLike("imoverlay", args, 1, line, col);
            (double R, double G, double B) color =
                args.Count == 3 ? ReadColor("imoverlay", args[2], line, col) ?? (1, 0, 0) : (1, 0, 0);
            try
            {
                return ImgOut(LabelDisplay.Overlay(source.Buffer, mask.Buffer, color), ImageClass.Double);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"imoverlay: {ex.Message}");
            }
        });

        define("viscircles", (args, line, col) =>
        {
            ArityRange("viscircles", args, 2, 10, line, col);
            ParsedArgs parsed = VisSpec.Parse(args, 2, line, col);
            if (parsed.Positional.Count < 2)
            {
                throw new JgsRuntimeException(line, col, "viscircles(centers, radii) needs both.");
            }

            double[,] centers = Rectangle("viscircles argument 1", parsed.Positional[0], line, col);
            if (centers.GetLength(1) != 2)
            {
                throw new JgsRuntimeException(line, col, "viscircles: the centres are an n-by-2 array of [x y] rows.");
            }

            double[] radii = NumericVector("viscircles", parsed.Positional, 1, line, col);
            JgsValue? color = parsed.Named("Color");
            double width = parsed.Scalar("LineWidth", 2.0);

            AxesModel axes = JG.Gca();
            for (int i = 0; i < centers.GetLength(0); i++)
            {
                double radius = radii.Length == 1 ? radii[0] : radii[Math.Min(i, radii.Length - 1)];
                DrawCircle(axes, centers[i, 0], centers[i, 1], radius, color, width, line, col);
            }

            return JgsValue.Null;
        });

        define("visboundaries", (args, line, col) =>
        {
            ArityRange("visboundaries", args, 1, 9, line, col);
            ParsedArgs parsed = VisSpec.Parse(args, 1, line, col);
            if (parsed.Positional.Count < 1)
            {
                throw new JgsRuntimeException(line, col, "visboundaries needs a mask or a set of boundaries.");
            }

            JgsValue? color = parsed.Named("Color");
            double width = parsed.Scalar("LineWidth", 2.0);
            AxesModel axes = JG.Gca();

            var traces = new List<double[,]>();
            if (parsed.Positional[0].Type == JgsType.Cell)
            {
                foreach (JgsValue element in parsed.Positional[0].BoxedElements())
                {
                    traces.Add(Rectangle("visboundaries boundary", element, line, col));
                }
            }
            else
            {
                using ImgArg source = ImgLike("visboundaries", parsed.Positional, 0, line, col);
                (List<(int Row, int Col)[]> found, _, _, int objects) =
                    Boundaries.Find(source.Buffer, 8, includeHoles: true);
                _ = objects;
                foreach ((int Row, int Col)[] trace in found)
                {
                    var rows = new double[trace.Length, 2];
                    for (int i = 0; i < trace.Length; i++)
                    {
                        rows[i, 0] = trace[i].Row + dialect.IndexBase;
                        rows[i, 1] = trace[i].Col + dialect.IndexBase;
                    }

                    traces.Add(rows);
                }
            }

            foreach (double[,] trace in traces)
            {
                int n = trace.GetLength(0);
                if (n < 2)
                {
                    continue;
                }

                var xs = new double[n];
                var ys = new double[n];
                for (int i = 0; i < n; i++)
                {
                    // A boundary is [row col]; a plot wants x then y.
                    ys[i] = trace[i, 0];
                    xs[i] = trace[i, 1];
                }

                LinePlot plot = axes.AddLine(xs, ys);
                plot.LineWidth = width;
                if (color is not null)
                {
                    plot.Color = OptionColor(color, line, col, "visboundaries");
                }
            }

            return JgsValue.Null;
        });

        define("imfindcircles", (args, line, col) => FindCirclesOutputs(args, 1, line, col, dialect)[0]);
    }

    // --- Multi-output bodies ------------------------------------------------------------------------

    /// <summary>The body of <c>[B, L, n, A] = bwboundaries(BW)</c>.</summary>
    private static JgsValue[] BoundariesOutputs(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col, JgsDialect dialect)
    {
        ArityRange("bwboundaries", args, 1, 3, line, col);
        using ImgArg source = ImgLike("bwboundaries", args, 0, line, col);
        int connectivity = 8;
        bool holes = true;
        for (int i = 1; i < args.Count; i++)
        {
            if (args[i].Type == JgsType.String)
            {
                string word = Str("bwboundaries", args, i, line, col).ToLowerInvariant();
                holes = word switch
                {
                    "holes" => true,
                    "noholes" => false,
                    _ => throw new JgsRuntimeException(line, col,
                        $"bwboundaries: unknown option '{word}' (use 'holes' or 'noholes')."),
                };
            }
            else
            {
                connectivity = Connectivity("bwboundaries", args, i, line, col);
            }
        }

        (List<(int Row, int Col)[]> traces, int[,] labels, int[] parent, int objects) =
            Boundaries.Find(source.Buffer, connectivity, holes);

        var cells = new JgsValue[traces.Count];
        for (int i = 0; i < traces.Count; i++)
        {
            cells[i] = TraceValue(traces[i], dialect);
        }

        JgsValue boundaries = JgsValue.Cell(cells);
        if (wanted < 2)
        {
            return [boundaries];
        }

        JgsValue labelValue = MatrixToRows(ToDoubleMatrix(labels));
        if (wanted < 3)
        {
            return [boundaries, labelValue];
        }

        JgsValue countValue = JgsValue.Number(objects);
        if (wanted < 4)
        {
            return [boundaries, labelValue, countValue];
        }

        // The adjacency matrix: A(i, j) is 1 when boundary i sits inside boundary j.
        JgsValue adjacency = JgsMatrix.Build(traces.Count, traces.Count,
            (i, j) => parent[i] == j ? 1.0 : 0.0);
        return [boundaries, labelValue, countValue, adjacency];
    }

    /// <summary>The body of <c>[thresh, metric] = multithresh(A, N)</c>.</summary>
    private static JgsValue[] MultiThresholdOutputs(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("multithresh", args, 1, 2, line, col);
        using ImgArg source = ImgLike("multithresh", args, 0, line, col);
        int levels = args.Count == 2 ? Count("multithresh", args, 1, line, col) : 1;
        try
        {
            double[] thresholds = Segmentation.MultiThreshold(source.Buffer, levels);
            if (wanted < 2)
            {
                return [Numbers(thresholds)];
            }

            // The effectiveness metric: how much of the total variance the split explains, which is
            // the same measure graythresh reports for one threshold.
            int[,] classes = Segmentation.Quantize(source.Buffer, thresholds);
            return [Numbers(thresholds), JgsValue.Number(Effectiveness(source.Buffer, classes, levels + 1))];
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new JgsRuntimeException(line, col, $"multithresh: {ex.Message}");
        }
    }

    /// <summary>The body of <c>[BW, D] = imsegfmm(W, mask, thresh)</c>.</summary>
    private static JgsValue[] FastMarchOutputs(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col, JgsDialect dialect)
    {
        ArityRange("imsegfmm", args, 3, 4, line, col);
        using ImgArg weight = ImgLike("imsegfmm", args, 0, line, col);

        IReadOnlyList<(int Row, int Col)> seeds;
        double threshold;
        if (args.Count == 4)
        {
            double[] columns = NumericVector("imsegfmm", args, 1, line, col);
            double[] rows = NumericVector("imsegfmm", args, 2, line, col);
            if (columns.Length != rows.Length)
            {
                throw new JgsRuntimeException(line, col,
                    "imsegfmm: the column and row vectors must be the same length.");
            }

            var pairs = new List<(int Row, int Col)>();
            for (int i = 0; i < rows.Length; i++)
            {
                pairs.Add(Subscript(rows[i], columns[i], weight.Buffer, dialect, line, col, "imsegfmm: the seed"));
            }

            seeds = pairs;
            threshold = Num("imsegfmm", args, 3, line, col);
        }
        else
        {
            seeds = SeedPixels("imsegfmm", args[1], weight.Buffer, dialect, line, col);
            threshold = Num("imsegfmm", args, 2, line, col);
        }

        try
        {
            (ImageBuffer mask, ImageBuffer time) = Segmentation.FastMarch(weight.Buffer, seeds, threshold);
            JgsValue maskValue = ImgMaskOut(mask, weight);
            if (wanted < 2)
            {
                time.Dispose();
                return [maskValue];
            }

            return [maskValue, ImgLikeOut(time, weight)];
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"imsegfmm: {ex.Message}");
        }
    }

    /// <summary>The body of <c>[L, centers] = imsegkmeans(I, k)</c>.</summary>
    private static JgsValue[] KMeansOutputs(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col, Random random)
    {
        ArityRange("imsegkmeans", args, 2, 10, line, col);
        ParsedArgs parsed = KMeansSpec.Parse(args, 2, line, col);
        if (parsed.Positional.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "imsegkmeans(I, k) needs a picture and a cluster count.");
        }

        using ImgArg source = ImgLike("imsegkmeans", parsed.Positional, 0, line, col);
        int clusters = Count("imsegkmeans", parsed.Positional, 1, line, col);
        int iterations = (int)Math.Round(parsed.Scalar("MaxIterations", 100));

        try
        {
            (int[,] labels, double[][] centers) =
                Segmentation.KMeans(source.Buffer, clusters, random, iterations);
            JgsValue labelValue = MatrixToRows(ToDoubleMatrix(labels));
            if (wanted < 2)
            {
                return [labelValue];
            }

            return
            [
                labelValue,
                JgsMatrix.Build(centers.Length, centers[0].Length, (i, c) => centers[i][c]),
            ];
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new JgsRuntimeException(line, col, $"imsegkmeans: {ex.Message}");
        }
    }

    /// <summary>The body of <c>[L, N] = superpixels(A, N)</c>.</summary>
    private static JgsValue[] SuperpixelsOutputs(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("superpixels", args, 2, 10, line, col);
        ParsedArgs parsed = SuperpixelsSpec.Parse(args, 2, line, col);
        if (parsed.Positional.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "superpixels(A, N) needs a picture and a count.");
        }

        using ImgArg source = ImgLike("superpixels", parsed.Positional, 0, line, col);
        int requested = Count("superpixels", parsed.Positional, 1, line, col);
        double compactness = parsed.Scalar("Compactness", 10.0);
        int iterations = (int)Math.Round(parsed.Scalar("NumIterations", 10));
        string method = parsed.Text("Method") ?? "slic0";

        bool zeroParameter = method.ToLowerInvariant() switch
        {
            "slic0" => true,
            "slic" => false,
            _ => throw new JgsRuntimeException(line, col,
                $"superpixels: unknown 'Method' value '{method}' (use 'slic0' or 'slic')."),
        };

        try
        {
            (int[,] labels, int count) = Segmentation.Superpixels(
                source.Buffer, requested, compactness, zeroParameter, iterations);
            JgsValue labelValue = MatrixToRows(ToDoubleMatrix(labels));
            return wanted < 2 ? [labelValue] : [labelValue, JgsValue.Number(count)];
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new JgsRuntimeException(line, col, $"superpixels: {ex.Message}");
        }
    }

    /// <summary>The body of <c>[centers, radii, metric] = imfindcircles(A, radiusRange)</c>.</summary>
    private static JgsValue[] FindCirclesOutputs(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col, JgsDialect dialect)
    {
        ArityRange("imfindcircles", args, 2, 10, line, col);
        ParsedArgs parsed = FindCirclesSpec.Parse(args, 2, line, col);
        if (parsed.Positional.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "imfindcircles(A, radius) needs a picture and a radius.");
        }

        using ImgArg source = ImgLike("imfindcircles", parsed.Positional, 0, line, col);
        double[] range = NumericVector("imfindcircles", parsed.Positional, 1, line, col);
        (double low, double high) = range.Length switch
        {
            1 => (range[0] * 0.8, range[0] * 1.2),
            2 => (range[0], range[1]),
            _ => throw new JgsRuntimeException(line, col,
                "imfindcircles: the radius is one number or a [min max] pair."),
        };

        string polarityWord = parsed.Text("ObjectPolarity") ?? "bright";
        CircleDetection.Polarity polarity = polarityWord.ToLowerInvariant() switch
        {
            "bright" => CircleDetection.Polarity.Bright,
            "dark" => CircleDetection.Polarity.Dark,
            _ => throw new JgsRuntimeException(line, col,
                $"imfindcircles: unknown 'ObjectPolarity' value '{polarityWord}' (use 'bright' or 'dark')."),
        };

        double sensitivity = parsed.Scalar("Sensitivity", 0.85);
        double edge = parsed.Scalar("EdgeThreshold", double.NaN);

        try
        {
            CircleDetection.Circle[] circles = CircleDetection.Find(
                source.Buffer, low, high, polarity, sensitivity, double.IsNaN(edge) ? null : edge);

            JgsValue centers = JgsMatrix.Build(circles.Length, 2,
                (i, c) => (c == 0 ? circles[i].CenterX : circles[i].CenterY) + dialect.IndexBase);
            if (wanted < 2)
            {
                return [centers];
            }

            var radii = new double[circles.Length];
            var metric = new double[circles.Length];
            for (int i = 0; i < circles.Length; i++)
            {
                radii[i] = circles[i].Radius;
                metric[i] = circles[i].Strength;
            }

            return wanted < 3
                ? [centers, Numbers(radii)]
                : [centers, Numbers(radii), Numbers(metric)];
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new JgsRuntimeException(line, col, $"imfindcircles: {ex.Message}");
        }
    }

    // --- Helpers ------------------------------------------------------------------------------------

    /// <summary>
    /// <c>regionprops</c>: reads the label map, the optional intensity image, the property selection,
    /// and the output form, then hands back a struct array or a table.
    /// </summary>
    private static JgsValue RegionPropsValue(
        IReadOnlyList<JgsValue> args, int line, int col, JgsDialect dialect)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "regionprops needs a label or binary image.");
        }

        // MATLAB puts the output form first when it is given at all.
        bool asTable = false;
        int start = 0;
        if (args[0].Type == JgsType.String)
        {
            string form = args[0].AsString.ToLowerInvariant();
            asTable = form switch
            {
                "table" => true,
                "struct" => false,
                _ => throw new JgsRuntimeException(line, col,
                    $"regionprops: unknown output format '{args[0].AsString}' (use 'table' or 'struct')."),
            };
            start = 1;
        }

        if (start >= args.Count)
        {
            throw new JgsRuntimeException(line, col, "regionprops needs a label or binary image.");
        }

        using ImgArg source = ImgLike("regionprops", args, start, line, col);
        int next = start + 1;

        ImgArg? intensityArg = null;
        if (next < args.Count && args[next].Type != JgsType.String && args[next].Type != JgsType.Cell)
        {
            intensityArg = ImgLike("regionprops", args, next, line, col);
            next++;
        }

        var wanted = new List<string>();
        for (int i = next; i < args.Count; i++)
        {
            if (args[i].Type == JgsType.Cell)
            {
                foreach (JgsValue element in args[i].BoxedElements())
                {
                    wanted.Add(element.Type == JgsType.String
                        ? element.AsString
                        : throw new JgsRuntimeException(line, col, "regionprops: property names are words."));
                }

                continue;
            }

            wanted.Add(Str("regionprops", args, i, line, col));
        }

        try
        {
            (int[,] labels, int count) = ReadLabels(source.Buffer);
            RegionMeasurement[] measured = RegionProperties.Measure(
                labels, count, intensityArg?.Buffer);

            List<string> properties = ResolveProperties(wanted, intensityArg is not null, line, col);
            return asTable || !dialect.IsMatlab
                ? JgsValue.Table(RegionTable(measured, properties, dialect))
                : RegionStructArray(measured, properties, source.Buffer.Height, source.Buffer.Width, dialect);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"regionprops: {ex.Message}");
        }
        finally
        {
            intensityArg?.Dispose();
        }
    }

    private static List<string> ResolveProperties(
        IReadOnlyList<string> requested, bool withIntensity, int line, int col)
    {
        var resolved = new List<string>();

        void AddAll(IEnumerable<string> names)
        {
            foreach (string name in names)
            {
                if (!resolved.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    resolved.Add(name);
                }
            }
        }

        if (requested.Count == 0)
        {
            AddAll(RegionProperties.Basic);
            if (withIntensity)
            {
                AddAll(["MeanIntensity", "WeightedCentroid"]);
            }

            return resolved;
        }

        foreach (string name in requested)
        {
            if (name.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                AddAll(RegionProperties.All);
                if (withIntensity)
                {
                    AddAll(RegionProperties.Intensity);
                }

                continue;
            }

            if (name.Equals("basic", StringComparison.OrdinalIgnoreCase))
            {
                AddAll(RegionProperties.Basic);
                continue;
            }

            string? match = Array.Find(RegionProperties.All,
                candidate => candidate.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? Array.Find(RegionProperties.Intensity,
                    candidate => candidate.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                throw new JgsRuntimeException(line, col,
                    $"regionprops: unknown property '{name}' (one of: " +
                    $"{string.Join(", ", RegionProperties.All)}, {string.Join(", ", RegionProperties.Intensity)}).");
            }

            if (Array.IndexOf(RegionProperties.Intensity, match) >= 0 && !withIntensity)
            {
                throw new JgsRuntimeException(line, col,
                    $"regionprops: '{match}' needs an intensity image as the second argument.");
            }

            AddAll([match]);
        }

        return resolved;
    }

    /// <summary>The struct array MATLAB returns: a cell of structs, one per region.</summary>
    private static JgsValue RegionStructArray(
        RegionMeasurement[] measured, List<string> properties, int height, int width, JgsDialect dialect)
    {
        var elements = new JgsValue[measured.Length];
        for (int i = 0; i < measured.Length; i++)
        {
            var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
            foreach (string property in properties)
            {
                fields[property] = RegionField(property, measured[i], height, width, dialect);
            }

            elements[i] = JgsValue.Struct(fields);
        }

        return JgsValue.Cell(elements);
    }

    /// <summary>
    /// One property of one region, in the shape MATLAB documents: a scalar, an <c>[x y]</c> pair, an
    /// n×2 list, or a logical matrix.
    /// </summary>
    private static JgsValue RegionField(
        string property, RegionMeasurement m, int height, int width, JgsDialect dialect)
    {
        double shift = dialect.IndexBase;
        return property switch
        {
            "Area" => JgsValue.Number(m.Area),
            "Centroid" => Numbers([m.CentroidX + shift, m.CentroidY + shift]),
            "BoundingBox" => Numbers(
                [m.BoundingBoxX + shift, m.BoundingBoxY + shift, m.BoundingBoxWidth, m.BoundingBoxHeight]),
            "Circularity" => JgsValue.Number(m.Circularity),
            "ConvexArea" => JgsValue.Number(m.ConvexArea),
            "ConvexHull" => JgsMatrix.Build(m.ConvexHull.Length, 2,
                (i, c) => (c == 0 ? m.ConvexHull[i].X : m.ConvexHull[i].Y) + shift),
            "ConvexImage" => MatrixToRows(ToDoubleMatrix(m.ConvexImage)),
            "Eccentricity" => JgsValue.Number(m.Eccentricity),
            "EquivDiameter" => JgsValue.Number(m.EquivDiameter),
            "EulerNumber" => JgsValue.Number(m.EulerNumber),
            "Extent" => JgsValue.Number(m.Extent),
            "Extrema" => JgsMatrix.Build(m.Extrema.Length, 2,
                (i, c) => (c == 0 ? m.Extrema[i].X : m.Extrema[i].Y) + shift),
            "FilledArea" => JgsValue.Number(m.FilledArea),
            "FilledImage" => MatrixToRows(ToDoubleMatrix(m.FilledImage)),
            "Image" => MatrixToRows(ToDoubleMatrix(m.Image)),
            "MajorAxisLength" => JgsValue.Number(m.MajorAxisLength),
            "MinorAxisLength" => JgsValue.Number(m.MinorAxisLength),
            "MaxFeretAngle" => JgsValue.Number(m.MaxFeretAngle),
            "MaxFeretDiameter" => JgsValue.Number(m.MaxFeretDiameter),
            "MinFeretAngle" => JgsValue.Number(m.MinFeretAngle),
            "MinFeretDiameter" => JgsValue.Number(m.MinFeretDiameter),
            "Orientation" => JgsValue.Number(m.Orientation),
            "Perimeter" => JgsValue.Number(m.Perimeter),
            "PixelIdxList" => LinearIndices([.. m.Pixels], height, width, dialect),
            "PixelList" => JgsMatrix.Build(m.Pixels.Length, 2,
                (i, c) => (c == 0 ? m.Pixels[i].Col : m.Pixels[i].Row) + shift),
            "Solidity" => JgsValue.Number(m.Solidity),
            "SubarrayIdx" => JgsValue.Cell(
            [
                Numbers(Sequence(m.BoundingBoxY + 0.5 + shift, m.BoundingBoxHeight)),
                Numbers(Sequence(m.BoundingBoxX + 0.5 + shift, m.BoundingBoxWidth)),
            ]),
            "MaxIntensity" => JgsValue.Number(m.MaxIntensity),
            "MeanIntensity" => JgsValue.Number(m.MeanIntensity),
            "MinIntensity" => JgsValue.Number(m.MinIntensity),
            "PixelValues" => Numbers(m.PixelValues),
            "WeightedCentroid" => Numbers([m.WeightedCentroidX + shift, m.WeightedCentroidY + shift]),
            _ => JgsValue.Null,
        };
    }

    /// <summary>
    /// The Table form JGS keeps. Only the scalar properties fit a table, so the list-valued ones —
    /// pixel lists, hulls, cropped masks — are left out rather than flattened into something a column
    /// cannot mean.
    /// </summary>
    private static JGraph.Data.Table RegionTable(
        RegionMeasurement[] measured, List<string> properties, JgsDialect dialect)
    {
        int n = measured.Length;
        double shift = dialect.IndexBase;
        var columns = new List<JGraph.Data.TableColumn>
        {
            new JGraph.Data.NumberColumn("Label", Column(measured, m => m.Label)),
        };

        void Scalar(string name, Func<RegionMeasurement, double> read) =>
            columns.Add(new JGraph.Data.NumberColumn(name, Column(measured, read)));

        foreach (string property in properties)
        {
            switch (property)
            {
                case "Area":
                    Scalar("Area", m => m.Area);
                    break;
                case "Centroid":
                    Scalar("CentroidX", m => m.CentroidX + shift);
                    Scalar("CentroidY", m => m.CentroidY + shift);
                    break;
                case "BoundingBox":
                    Scalar("BBoxX", m => m.BoundingBoxX + shift);
                    Scalar("BBoxY", m => m.BoundingBoxY + shift);
                    Scalar("BBoxWidth", m => m.BoundingBoxWidth);
                    Scalar("BBoxHeight", m => m.BoundingBoxHeight);
                    break;
                case "Circularity": Scalar("Circularity", m => m.Circularity); break;
                case "ConvexArea": Scalar("ConvexArea", m => m.ConvexArea); break;
                case "Eccentricity": Scalar("Eccentricity", m => m.Eccentricity); break;
                case "EquivDiameter": Scalar("EquivDiameter", m => m.EquivDiameter); break;
                case "EulerNumber": Scalar("EulerNumber", m => m.EulerNumber); break;
                case "Extent": Scalar("Extent", m => m.Extent); break;
                case "FilledArea": Scalar("FilledArea", m => m.FilledArea); break;
                case "MajorAxisLength": Scalar("MajorAxisLength", m => m.MajorAxisLength); break;
                case "MinorAxisLength": Scalar("MinorAxisLength", m => m.MinorAxisLength); break;
                case "MaxFeretAngle": Scalar("MaxFeretAngle", m => m.MaxFeretAngle); break;
                case "MaxFeretDiameter": Scalar("MaxFeretDiameter", m => m.MaxFeretDiameter); break;
                case "MinFeretAngle": Scalar("MinFeretAngle", m => m.MinFeretAngle); break;
                case "MinFeretDiameter": Scalar("MinFeretDiameter", m => m.MinFeretDiameter); break;
                case "Orientation": Scalar("Orientation", m => m.Orientation); break;
                case "Perimeter": Scalar("Perimeter", m => m.Perimeter); break;
                case "Solidity": Scalar("Solidity", m => m.Solidity); break;
                case "MaxIntensity": Scalar("MaxIntensity", m => m.MaxIntensity); break;
                case "MeanIntensity": Scalar("MeanIntensity", m => m.MeanIntensity); break;
                case "MinIntensity": Scalar("MinIntensity", m => m.MinIntensity); break;
                case "WeightedCentroid":
                    Scalar("WeightedCentroidX", m => m.WeightedCentroidX + shift);
                    Scalar("WeightedCentroidY", m => m.WeightedCentroidY + shift);
                    break;
                default:
                    // ConvexHull, Extrema, PixelList, the cropped masks: not table-shaped.
                    break;
            }
        }

        _ = n;
        return new JGraph.Data.Table(columns);
    }

    private static double[] Column(RegionMeasurement[] measured, Func<RegionMeasurement, double> read)
    {
        var values = new double[measured.Length];
        for (int i = 0; i < measured.Length; i++)
        {
            values[i] = read(measured[i]);
        }

        return values;
    }

    /// <summary>The <c>bwconncomp</c> struct.</summary>
    private static JgsValue ConnectedComponents(
        int[,] labels, int count, int height, int width, int connectivity, JgsDialect dialect)
    {
        List<(int Row, int Col)>[] lists = Regions.PixelLists(labels, count);
        var cells = new JgsValue[count];
        for (int i = 0; i < count; i++)
        {
            cells[i] = LinearIndices(lists[i], height, width, dialect);
        }

        return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Connectivity"] = JgsValue.Number(connectivity),
            ["ImageSize"] = Numbers([height, width]),
            ["NumObjects"] = JgsValue.Number(count),
            ["PixelIdxList"] = JgsValue.Cell(cells),
        });
    }

    /// <summary>Rebuilds a label map from a <c>bwconncomp</c> struct or reads one straight from an image.</summary>
    private static (int[,] Labels, int Count) LabelsFromComponents(
        string name, JgsValue value, JgsDialect dialect, int line, int col)
    {
        if (value.Type != JgsType.Struct)
        {
            using ImgArg source = ImgLike(name, [value], 0, line, col);
            return ReadLabels(source.Buffer);
        }

        Dictionary<string, JgsValue> fields = value.AsStruct;
        if (!fields.TryGetValue("ImageSize", out JgsValue? sizeValue) ||
            !fields.TryGetValue("PixelIdxList", out JgsValue? listValue) || listValue.Type != JgsType.Cell)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: the component struct needs ImageSize and PixelIdxList fields (build one with bwconncomp).");
        }

        double[] size = NumericVector(name, sizeValue, line, col);
        if (size.Length < 2)
        {
            throw new JgsRuntimeException(line, col, $"{name}: ImageSize is [rows, cols].");
        }

        int height = (int)Math.Round(size[0]);
        int width = (int)Math.Round(size[1]);
        var labels = new int[height, width];
        JgsValue[] lists = listValue.BoxedElements();
        for (int i = 0; i < lists.Length; i++)
        {
            foreach (double index in NumericVector(name, lists[i], line, col))
            {
                int flat = (int)Math.Round(index) - dialect.IndexBase;
                if (flat < 0 || flat >= height * width)
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name}: index {index} is outside the {height}x{width} image.");
                }

                (int r, int c) = dialect.IsMatlab
                    ? (flat % height, flat / height)
                    : (flat / width, flat % width);
                labels[r, c] = i + 1;
            }
        }

        return (labels, lists.Length);
    }

    /// <summary>Reads a label map out of a picture, labelling it first if it is binary.</summary>
    private static (int[,] Labels, int Count) ReadLabels(ImageBuffer image)
    {
        // A binary picture is labelled here, 8-connected, matching bwlabel's default. A one-region
        // label map is indistinguishable from binary, but labelling it is a no-op.
        if (image.IsBinary)
        {
            return Regions.Label(image, 8);
        }

        var labels = new int[image.Height, image.Width];
        int count = 0;
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++)
            {
                labels[r, c] = (int)Math.Round(image[r, c, 0]);
                count = Math.Max(count, labels[r, c]);
            }
        }

        GC.KeepAlive(image);
        return (labels, count);
    }

    private static JgsValue PropertyFilter(
        string name, IReadOnlyList<JgsValue> args, int line, int col, JgsDialect dialect)
    {
        OptionSpec spec = name == "bwareafilt" ? BwAreaFiltSpec : BwPropFiltSpec;
        ParsedArgs parsed = spec.Parse(args, name == "bwareafilt" ? 3 : 5, line, col);
        if (parsed.Positional.Count < 2)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs an image and a range or a count.");
        }

        using ImgArg source = ImgLike(name, parsed.Positional, 0, line, col);
        int slot = 1;
        ImgArg? intensity = null;
        string property = "Area";

        try
        {
            if (name == "bwpropfilt")
            {
                // bwpropfilt(BW, [I], prop, ...): the property word is what separates the optional
                // intensity image from the rest.
                if (parsed.Positional[slot].Type != JgsType.String)
                {
                    intensity = ImgLike(name, parsed.Positional, slot, line, col);
                    slot++;
                }

                property = Str(name, parsed.Positional, slot, line, col);
                slot++;
            }

            if (slot >= parsed.Positional.Count)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs a range or a count.");
            }

            double[] selector = NumericVector(name, parsed.Positional, slot, line, col);
            slot++;
            int connectivity = slot < parsed.Positional.Count
                ? Connectivity(name, parsed.Positional, slot, line, col)
                : 8;

            (int[,] labels, int count) = Regions.Label(source.Buffer, connectivity);
            RegionMeasurement[] measured = RegionProperties.Measure(labels, count, intensity?.Buffer);

            List<string> resolved = ResolveProperties([property], intensity is not null, line, col);
            var values = new double[count];
            for (int i = 0; i < count; i++)
            {
                JgsValue field = RegionField(
                    resolved[0], measured[i], source.Buffer.Height, source.Buffer.Width, dialect);
                if (field.Type != JgsType.Number)
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name}: '{resolved[0]}' is not a single number, so components cannot be ranked by it.");
                }

                values[i] = field.AsNumber;
            }

            bool largest = parsed.OneOf("largest", "largest", "smallest") == "largest";
            ImageBuffer kept = selector.Length switch
            {
                1 => Regions.Filter(source.Buffer, values, labels, count, null, (int)Math.Round(selector[0]), largest),
                2 => Regions.Filter(source.Buffer, values, labels, count, (selector[0], selector[1])),
                _ => throw new JgsRuntimeException(line, col,
                    $"{name}: give a count, or a [low high] range."),
            };

            return ImgMaskOut(kept, source);
        }
        finally
        {
            intensity?.Dispose();
        }
    }

    /// <summary>Otsu's effectiveness metric over an arbitrary number of classes.</summary>
    private static double Effectiveness(ImageBuffer image, int[,] classes, int levels)
    {
        double total = 0;
        int n = image.Height * image.Width;
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++)
            {
                total += image[r, c, 0];
            }
        }

        double mean = total / n;
        double variance = 0;
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++)
            {
                double d = image[r, c, 0] - mean;
                variance += d * d;
            }
        }

        variance /= n;
        if (variance <= 0)
        {
            return 0;
        }

        var sums = new double[levels + 1];
        var counts = new int[levels + 1];
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++)
            {
                int k = Math.Clamp(classes[r, c], 1, levels);
                sums[k] += image[r, c, 0];
                counts[k]++;
            }
        }

        double between = 0;
        for (int k = 1; k <= levels; k++)
        {
            if (counts[k] == 0)
            {
                continue;
            }

            double classMean = sums[k] / counts[k];
            between += counts[k] / (double)n * (classMean - mean) * (classMean - mean);
        }

        GC.KeepAlive(image);
        return between / variance;
    }

    private static JgsValue TraceValue((int Row, int Col)[] trace, JgsDialect dialect) =>
        JgsMatrix.Build(trace.Length, 2,
            (i, c) => (c == 0 ? trace[i].Row : trace[i].Col) + dialect.IndexBase);

    private static JgsValue LinearIndices(
        IReadOnlyList<(int Row, int Col)> pixels, int height, int width, JgsDialect dialect)
    {
        var indices = new double[pixels.Count];
        for (int i = 0; i < pixels.Count; i++)
        {
            (int r, int c) = pixels[i];

            // MATLAB counts down the columns; JGS counts across the rows, like every other flat index
            // it hands out.
            indices[i] = dialect.IsMatlab ? (c * height) + r + 1 : (r * width) + c;
        }

        return Numbers(indices);
    }

    private static bool[,] MaskOf(ImageBuffer image)
    {
        var mask = new bool[image.Height, image.Width];
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++)
            {
                mask[r, c] = image[r, c, 0] != 0;
            }
        }

        GC.KeepAlive(image);
        return mask;
    }

    private static double[,] ToDoubleMatrix(int[,] values)
    {
        int h = values.GetLength(0);
        int w = values.GetLength(1);
        var result = new double[h, w];
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                result[r, c] = values[r, c];
            }
        }

        return result;
    }

    private static double[,] ToDoubleMatrix(bool[,] values)
    {
        int h = values.GetLength(0);
        int w = values.GetLength(1);
        var result = new double[h, w];
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                result[r, c] = values[r, c] ? 1.0 : 0.0;
            }
        }

        return result;
    }

    private static double[] Sequence(double from, double count)
    {
        var values = new double[(int)Math.Round(count)];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = from + i;
        }

        return values;
    }

    private static double[] Shift(double[] values, int by)
    {
        var shifted = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            shifted[i] = values[i] + by;
        }

        return shifted;
    }

    /// <summary>
    /// The cell to set out from, given the direction MATLAB's <c>fstep</c> says to start searching
    /// in. The tracer sweeps forward from wherever it arrived, so arriving from the neighbour just
    /// <em>before</em> the requested direction is what makes that direction the first one tried —
    /// and it is also what keeps the arrival cell off the object, which a naive reading does not.
    /// </summary>
    private static (int R, int C) CompassStep(string word, int line, int col) =>
        word.ToUpperInvariant() switch
        {
            "N" => (-1, -1),
            "NE" => (-1, 0),
            "E" => (-1, 1),
            "SE" => (0, 1),
            "S" => (1, 1),
            "SW" => (1, 0),
            "W" => (1, -1),
            "NW" => (0, -1),
            _ => throw new JgsRuntimeException(line, col,
                $"bwtraceboundary: unknown direction '{word}' (N, NE, E, SE, S, SW, W, NW)."),
        };

    private static (double R, double G, double B)[]? ReadColormap(JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.String)
        {
            // A named colormap word; anything but the default palette is out of scope here, so the
            // word is accepted and the palette chosen by label count is used.
            return null;
        }

        double[,] map = Rectangle("the colormap", value, line, col);
        if (map.GetLength(1) != 3)
        {
            throw new JgsRuntimeException(line, col, "a colormap is an n-by-3 array of [r g b] rows.");
        }

        var colors = new (double, double, double)[map.GetLength(0)];
        for (int i = 0; i < colors.Length; i++)
        {
            colors[i] = (map[i, 0], map[i, 1], map[i, 2]);
        }

        return colors;
    }

    private static (double R, double G, double B)? ReadColor(string name, JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.String)
        {
            return value.AsString.ToLowerInvariant() switch
            {
                "r" or "red" => (1.0, 0.0, 0.0),
                "g" or "green" => (0.0, 1.0, 0.0),
                "b" or "blue" => (0.0, 0.0, 1.0),
                "c" or "cyan" => (0.0, 1.0, 1.0),
                "m" or "magenta" => (1.0, 0.0, 1.0),
                "y" or "yellow" => (1.0, 1.0, 0.0),
                "k" or "black" => (0.0, 0.0, 0.0),
                "w" or "white" => (1.0, 1.0, 1.0),
                _ => throw new JgsRuntimeException(line, col,
                    $"{name}: unknown colour '{value.AsString}'."),
            };
        }

        double[] rgb = NumericVector(name, value, line, col);
        if (rgb.Length != 3)
        {
            throw new JgsRuntimeException(line, col, $"{name}: a colour is a word or an [r g b] triple.");
        }

        return (rgb[0], rgb[1], rgb[2]);
    }

    private static void DrawCircle(
        AxesModel axes, double centerX, double centerY, double radius,
        JgsValue? color, double width, int line, int col)
    {
        const int steps = 72;
        var xs = new double[steps + 1];
        var ys = new double[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            double angle = 2 * Math.PI * i / steps;
            xs[i] = centerX + (radius * Math.Cos(angle));
            ys[i] = centerY + (radius * Math.Sin(angle));
        }

        LinePlot plot = axes.AddLine(xs, ys);
        plot.LineWidth = width;
        if (color is not null)
        {
            plot.Color = OptionColor(color, line, col, "viscircles");
        }
    }
}
