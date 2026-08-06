using JGraph.Imaging;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M46 wave B: the filtering, neighbourhood-statistics and block-processing builtins —
/// <c>padarray</c>, <c>imgaussfilt</c>, <c>imboxfilt</c>, the integral images, <c>ordfilt2</c> and the
/// <c>*filt</c> family, <c>wiener2</c>, and the <c>im2col</c>/<c>blockproc</c> block machinery.
/// </summary>
/// <remarks>
/// MATLAB draws no line between an image and a matrix, and most of these functions are used on both:
/// <c>padarray</c> on a coordinate list, <c>ordfilt2</c> on a cost surface, <c>blockproc</c> on
/// anything at all. Each one therefore reads its data through <see cref="ImgLike"/> and answers in the
/// form it was asked in, so a script never has to convert just to call a filter.
/// </remarks>
internal static partial class JgsBuiltins
{
    private static readonly OptionSpec PadArraySpec = new(
        "padarray",
        ["circular", "replicate", "symmetric", "pre", "post", "both"],
        [],
        AllowNumericFlag: true);

    private static readonly OptionSpec GaussFilterSpec = new(
        "imgaussfilt", [], ["FilterSize", "Padding", "FilterDomain"]);

    private static readonly OptionSpec BoxFilterSpec = new(
        "imboxfilt", [], ["Padding", "NormalizationFactor"]);

    private static readonly OptionSpec IntegralBoxSpec = new(
        "integralBoxFilter", [], ["NormalizationFactor"]);

    private static readonly OptionSpec OrdFiltSpec = new(
        "ordfilt2", ["zeros", "symmetric"], []);

    private static readonly OptionSpec ModeFiltSpec = new(
        "modefilt", ["zeros", "symmetric", "replicate"], []);

    private static readonly OptionSpec BlockProcSpec = new(
        "blockproc",
        [],
        ["BorderSize", "PadPartialBlocks", "PadMethod", "TrimBorder", "UseParallel", "DisplayWaitbar"]);

    private static void DefineFilteringBuiltins(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> define, JgsDialect dialect)
    {
        // --- Padding -------------------------------------------------------------------------
        define("padarray", (args, line, col) =>
        {
            ArityRange("padarray", args, 2, 4, line, col);
            ParsedArgs parsed = PadArraySpec.Parse(args, 2, line, col);
            if (parsed.Positional.Count < 2)
            {
                throw new JgsRuntimeException(line, col, "padarray(A, [r c]) needs the array and a pad size.");
            }

            Filters.Boundary boundary = Filters.Boundary.Zero;
            if (parsed.Has("circular")) { boundary = Filters.Boundary.Circular; }
            else if (parsed.Has("replicate")) { boundary = Filters.Boundary.Replicate; }
            else if (parsed.Has("symmetric")) { boundary = Filters.Boundary.Symmetric; }

            double padValue = parsed.NumericFlag?.AsNumber ?? 0.0;
            Neighborhoods.PadDirection direction =
                parsed.Has("pre") ? Neighborhoods.PadDirection.Pre :
                parsed.Has("post") ? Neighborhoods.PadDirection.Post :
                Neighborhoods.PadDirection.Both;

            // A three-element pad size is what says the caller means all three dimensions. A 3-D array
            // with a two-element size is padded in its first two dimensions and left alone in the
            // third, which is both MATLAB's rule and the one that keeps padding an RGB array working.
            if (NumericVector("padarray", parsed.Positional[1], line, col) is { Length: 3 } spread)
            {
                return PadVolume(parsed.Positional[0], spread, boundary, padValue, direction, line, col);
            }

            using ImgArg source = ImgLike("padarray", parsed.Positional, 0, line, col);
            (int padRows, int padCols) = WindowOf("padarray", parsed.Positional[1], line, col, allowZero: true);
            return ImgLikeOut(
                Neighborhoods.Pad(source.Buffer, padRows, padCols, boundary, padValue, direction), source);
        });

        // --- Smoothing -----------------------------------------------------------------------
        define("imgaussfilt", (args, line, col) =>
        {
            ArityRange("imgaussfilt", args, 1, 8, line, col);
            ParsedArgs parsed = GaussFilterSpec.Parse(args, 2, line, col);
            using ImgArg source = ImgLike("imgaussfilt", parsed.Positional, 0, line, col);

            double[] sigmas = parsed.Positional.Count >= 2
                ? NumericVector("imgaussfilt", parsed.Positional[1], line, col)
                : [0.5];
            if (sigmas.Length is not (1 or 2))
            {
                throw new JgsRuntimeException(line, col, "imgaussfilt sigma is one number or a [sy sx] pair.");
            }

            double sigmaRows = sigmas[0];
            double sigmaCols = sigmas.Length == 2 ? sigmas[1] : sigmas[0];
            if (sigmaRows <= 0 || sigmaCols <= 0)
            {
                throw new JgsRuntimeException(line, col, "imgaussfilt sigma must be positive.");
            }

            (int filterRows, int filterCols) = parsed.Window("FilterSize") ?? (0, 0);

            // 'auto' and 'frequency' are accepted so a script written for MATLAB runs unchanged; the
            // separable spatial pass is exact, so choosing the frequency route would only change the
            // rounding, never the answer.
            string domain = parsed.Text("FilterDomain") ?? "auto";
            if (domain is not ("auto" or "spatial" or "frequency"))
            {
                throw new JgsRuntimeException(line, col,
                    $"imgaussfilt: unknown 'FilterDomain' value '{domain}' (use 'auto', 'spatial', or 'frequency').");
            }

            (Filters.Boundary boundary, _) =
                PaddingOption("imgaussfilt", parsed, Filters.Boundary.Replicate, line, col);
            return ImgLikeOut(
                Filters.GaussianBlur(source.Buffer, sigmaRows, sigmaCols, filterRows, filterCols, boundary),
                source);
        });

        define("imboxfilt", (args, line, col) =>
        {
            ArityRange("imboxfilt", args, 1, 6, line, col);
            ParsedArgs parsed = BoxFilterSpec.Parse(args, 2, line, col);
            using ImgArg source = ImgLike("imboxfilt", parsed.Positional, 0, line, col);
            (int rows, int cols) = parsed.Positional.Count >= 2
                ? WindowOf("imboxfilt", parsed.Positional[1], line, col)
                : (3, 3);
            if (rows % 2 == 0 || cols % 2 == 0)
            {
                throw new JgsRuntimeException(line, col, "imboxfilt needs an odd filter size.");
            }

            (Filters.Boundary boundary, _) =
                PaddingOption("imboxfilt", parsed, Filters.Boundary.Replicate, line, col);
            double area = (double)rows * cols;
            double factor = parsed.Scalar("NormalizationFactor", 1.0 / area);

            ImageBuffer mean = Filters.BoxMean(source.Buffer, rows, cols, boundary);
            if (factor != 1.0 / area)
            {
                // BoxMean already divides by the window area, so rescale to the requested factor
                // rather than recomputing the sums.
                Span<double> px = mean.Pixels;
                double rescale = factor * area;
                for (int i = 0; i < px.Length; i++)
                {
                    px[i] *= rescale;
                }

                GC.KeepAlive(mean);
            }

            return ImgLikeOut(mean, source);
        });

        // --- Integral images -----------------------------------------------------------------
        define("integralImage", (args, line, col) =>
        {
            ArityRange("integralImage", args, 1, 2, line, col);
            using ImgArg source = ImgLike("integralImage", args, 0, line, col);
            if (source.Buffer.Channels != 1)
            {
                throw new JgsRuntimeException(line, col,
                    "integralImage works on one plane at a time; split a colour image with im2mat first.");
            }

            string orientation = args.Count == 2 ? Str("integralImage", args, 1, line, col) : "upright";
            return orientation.ToLowerInvariant() switch
            {
                "upright" => MatrixToRows(Neighborhoods.IntegralImage(source.Buffer)),
                "rotated" => MatrixToRows(Neighborhoods.RotatedIntegralImage(source.Buffer)),
                _ => throw new JgsRuntimeException(line, col,
                    $"integralImage: unknown orientation '{orientation}' (use 'upright' or 'rotated')."),
            };
        });

        define("integralBoxFilter", (args, line, col) =>
        {
            ArityRange("integralBoxFilter", args, 1, 4, line, col);
            ParsedArgs parsed = IntegralBoxSpec.Parse(args, 2, line, col);
            double[,] integral = Matrix("integralBoxFilter", parsed.Positional, 0, line, col);
            (int rows, int cols) = parsed.Positional.Count >= 2
                ? WindowOf("integralBoxFilter", parsed.Positional[1], line, col)
                : (3, 3);
            if (rows % 2 == 0 || cols % 2 == 0)
            {
                throw new JgsRuntimeException(line, col, "integralBoxFilter needs an odd filter size.");
            }

            double? factor = parsed.Named("NormalizationFactor") is null
                ? null
                : parsed.Scalar("NormalizationFactor", 0.0);
            try
            {
                return MatrixToRows(Neighborhoods.IntegralBoxFilter(integral, rows, cols, factor));
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }
        });

        // --- Neighbourhood statistics --------------------------------------------------------
        define("ordfilt2", (args, line, col) =>
        {
            ArityRange("ordfilt2", args, 3, 5, line, col);
            ParsedArgs parsed = OrdFiltSpec.Parse(args, 4, line, col);
            if (parsed.Positional.Count < 3)
            {
                throw new JgsRuntimeException(line, col,
                    "ordfilt2(A, order, domain) needs the array, the rank, and a neighbourhood.");
            }

            using ImgArg source = ImgLike("ordfilt2", parsed.Positional, 0, line, col);
            int order = (int)Math.Round(NumOf("ordfilt2", parsed.Positional[1], line, col));
            bool[,] domain = DomainOf("ordfilt2", parsed.Positional, 2, line, col);
            double[,]? offsets = parsed.Positional.Count >= 4
                ? Matrix("ordfilt2", parsed.Positional, 3, line, col)
                : null;
            Filters.Boundary boundary = parsed.Has("symmetric") ? Filters.Boundary.Symmetric : Filters.Boundary.Zero;

            try
            {
                return ImgLikeOut(
                    Neighborhoods.OrderFilter(source.Buffer, domain, order, offsets, boundary), source);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"ordfilt2: {ex.Message}");
            }
        });

        void Statistic(string name, int defaultExtent, Func<ImageBuffer, bool[,], ImageBuffer> compute)
        {
            define(name, (args, line, col) =>
            {
                ArityRange(name, args, 1, 2, line, col);
                using ImgArg source = ImgLike(name, args, 0, line, col);
                bool[,] domain = args.Count >= 2
                    ? DomainOf(name, args, 1, line, col)
                    : Neighborhoods.Rectangle(defaultExtent, defaultExtent);
                try
                {
                    // These measure a neighbourhood rather than transform a picture, so the answer is
                    // a plain double field even when the input was a uint8 image — MATLAB's rule too.
                    ImageBuffer result = compute(source.Buffer, domain);
                    return source.FromMatrix ? ImgLikeOut(result, source) : ImgOut(result, ImageClass.Double);
                }
                catch (ArgumentException ex)
                {
                    throw new JgsRuntimeException(line, col, $"{name}: {ex.Message}");
                }
            });
        }

        Statistic("stdfilt", 3, Neighborhoods.StandardDeviation);
        Statistic("rangefilt", 3, Neighborhoods.Range);
        Statistic("entropyfilt", 9, static (image, domain) => Neighborhoods.Entropy(image, domain));

        define("modefilt", (args, line, col) =>
        {
            ArityRange("modefilt", args, 1, 3, line, col);
            ParsedArgs parsed = ModeFiltSpec.Parse(args, 2, line, col);
            using ImgArg source = ImgLike("modefilt", parsed.Positional, 0, line, col);
            (int rows, int cols) = parsed.Positional.Count >= 2
                ? WindowOf("modefilt", parsed.Positional[1], line, col)
                : (3, 3);
            Filters.Boundary boundary =
                parsed.Has("zeros") ? Filters.Boundary.Zero :
                parsed.Has("replicate") ? Filters.Boundary.Replicate :
                Filters.Boundary.Symmetric;
            return ImgLikeOut(
                Neighborhoods.Mode(source.Buffer, Neighborhoods.Rectangle(rows, cols), boundary), source);
        });

        define("wiener2", (args, line, col) => WienerOutputs(args, 1, line, col)[0]);

        // --- Block processing ----------------------------------------------------------------
        define("im2col", (args, line, col) =>
        {
            ArityRange("im2col", args, 2, 3, line, col);
            using ImgArg source = ImgLike("im2col", args, 0, line, col);
            (int rows, int cols) = WindowOf("im2col", args[1], line, col);
            BlockProcessing.BlockKind kind = args.Count >= 3
                ? ParseBlockKind("im2col", Str("im2col", args, 2, line, col), line, col)
                : BlockProcessing.BlockKind.Sliding;
            try
            {
                return MatrixToRows(
                    BlockProcessing.Im2Col(PointOps.ToMatrix(source.Buffer, 0), rows, cols, kind));
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }
        });

        define("col2im", (args, line, col) =>
        {
            ArityRange("col2im", args, 3, 4, line, col);
            double[,] columns = Matrix("col2im", args, 0, line, col);
            (int blockRows, int blockCols) = WindowOf("col2im", args[1], line, col);
            (int rows, int cols) = WindowOf("col2im", args[2], line, col);
            BlockProcessing.BlockKind kind = args.Count >= 4
                ? ParseBlockKind("col2im", Str("col2im", args, 3, line, col), line, col)
                : BlockProcessing.BlockKind.Sliding;
            try
            {
                return MatrixToRows(BlockProcessing.Col2Im(columns, blockRows, blockCols, rows, cols, kind));
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }
        });

        define("bestblk", (args, line, col) => BestBlkOutputs(args, 1, line, col)[0]);

        define("nlfilter", (args, line, col) =>
        {
            Arity("nlfilter", args, 3, line, col);
            using ImgArg source = ImgLike("nlfilter", args, 0, line, col);
            (int rows, int cols) = WindowOf("nlfilter", args[1], line, col);
            IJgsCallable fun = Callable("nlfilter", args, 2, line, col);
            double[,] a = PointOps.ToMatrix(source.Buffer, 0);

            int m = a.GetLength(0);
            int n = a.GetLength(1);
            var result = new double[m, n];
            int anchorR = (rows - 1) / 2;
            int anchorC = (cols - 1) / 2;
            for (int r = 0; r < m; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    double[,] block = BlockProcessing.ExtractBlock(a, r - anchorR, c - anchorC, rows, cols);
                    result[r, c] = NumOf("nlfilter", fun.Call([MatrixToRows(block)], line, col), line, col);
                }
            }

            return source.FromMatrix
                ? MatrixToRows(result)
                : ImgOut(PointOps.WrapValues(result), source.Buffer.Class);
        });

        define("colfilt", (args, line, col) =>
        {
            Arity("colfilt", args, 4, line, col);
            using ImgArg source = ImgLike("colfilt", args, 0, line, col);
            (int rows, int cols) = WindowOf("colfilt", args[1], line, col);
            BlockProcessing.BlockKind kind = ParseBlockKind("colfilt", Str("colfilt", args, 2, line, col), line, col);
            IJgsCallable fun = Callable("colfilt", args, 3, line, col);
            double[,] a = PointOps.ToMatrix(source.Buffer, 0);
            int m = a.GetLength(0);
            int n = a.GetLength(1);

            double[,] result;
            if (kind == BlockProcessing.BlockKind.Sliding)
            {
                // Padding first is what makes every output pixel have a full column, so the function
                // sees one column per pixel and the result is the same size as the input.
                int preR = (rows - 1) / 2;
                int preC = (cols - 1) / 2;
                var padded = new double[m + rows - 1, n + cols - 1];
                for (int r = 0; r < m; r++)
                {
                    for (int c = 0; c < n; c++)
                    {
                        padded[r + preR, c + preC] = a[r, c];
                    }
                }

                double[,] columns = BlockProcessing.Im2Col(padded, rows, cols, kind);
                double[] answered = ToDoubles("colfilt", fun.Call([MatrixToRows(columns)], line, col), line, col);
                if (answered.Length != m * n)
                {
                    throw new JgsRuntimeException(line, col,
                        $"colfilt: the function returned {answered.Length} values for {m * n} sliding blocks.");
                }

                result = new double[m, n];
                for (int c = 0; c < n; c++)
                {
                    for (int r = 0; r < m; r++)
                    {
                        result[r, c] = answered[(c * m) + r];
                    }
                }
            }
            else
            {
                double[,] columns = BlockProcessing.Im2Col(a, rows, cols, kind);
                double[,] answered = Matrix("colfilt", [fun.Call([MatrixToRows(columns)], line, col)], 0, line, col);
                if (answered.GetLength(0) != columns.GetLength(0) ||
                    answered.GetLength(1) != columns.GetLength(1))
                {
                    throw new JgsRuntimeException(line, col,
                        "colfilt 'distinct': the function must return a matrix the same size as the one it was given.");
                }

                result = BlockProcessing.Col2Im(answered, rows, cols, m, n, kind);
            }

            return source.FromMatrix
                ? MatrixToRows(result)
                : ImgOut(PointOps.WrapValues(result), source.Buffer.Class);
        });

        define("blockproc", (args, line, col) =>
        {
            ArityRange("blockproc", args, 3, 15, line, col);
            ParsedArgs parsed = BlockProcSpec.Parse(args, 3, line, col);
            if (parsed.Positional.Count < 3)
            {
                throw new JgsRuntimeException(line, col,
                    "blockproc(A, [m n], fun) needs the array, a block size, and a function.");
            }

            using ImgArg source = ImgLike("blockproc", parsed.Positional, 0, line, col);
            (int blockRows, int blockCols) = WindowOf("blockproc", parsed.Positional[1], line, col);
            IJgsCallable fun = Callable("blockproc", parsed.Positional, 2, line, col);
            // A border of [0 0] is legal and common, so this is read directly rather than through the
            // window helper, which insists on positive extents.
            int borderRows = 0;
            int borderCols = 0;
            if (parsed.Named("BorderSize") is { } borderValue)
            {
                (borderRows, borderCols) = WindowOf("blockproc", borderValue, line, col, allowZero: true);
            }

            bool trim = parsed.Flag("TrimBorder", true);
            bool padPartial = parsed.Flag("PadPartialBlocks", false);
            (Filters.Boundary padBoundary, double padValue) =
                PaddingOption("blockproc", parsed, Filters.Boundary.Zero, line, col, "PadMethod");

            double[,] a = PointOps.ToMatrix(source.Buffer, 0);
            int m = a.GetLength(0);
            int n = a.GetLength(1);
            int blocksDown = (m + blockRows - 1) / blockRows;
            int blocksAcross = (n + blockCols - 1) / blockCols;

            var grid = new double[blocksDown][][,];
            for (int rb = 0; rb < blocksDown; rb++)
            {
                grid[rb] = new double[blocksAcross][,];
                for (int cb = 0; cb < blocksAcross; cb++)
                {
                    int top = rb * blockRows;
                    int left = cb * blockCols;
                    int height = padPartial ? blockRows : Math.Min(blockRows, m - top);
                    int width = padPartial ? blockCols : Math.Min(blockCols, n - left);
                    double[,] data = BlockProcessing.ExtractBlock(
                        a, top - borderRows, left - borderCols,
                        height + (2 * borderRows), width + (2 * borderCols), padBoundary, padValue);

                    var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
                    {
                        ["data"] = MatrixToRows(data),
                        ["blockSize"] = Numbers([height, width]),
                        ["border"] = Numbers([borderRows, borderCols]),
                        ["imageSize"] = Numbers([m, n]),
                        ["location"] = Numbers([top + dialect.IndexBase, left + dialect.IndexBase]),
                    };

                    double[,] answered = Matrix(
                        "blockproc", [fun.Call([JgsValue.Struct(fields)], line, col)], 0, line, col);
                    grid[rb][cb] = trim && (borderRows > 0 || borderCols > 0)
                        ? TrimBlockBorder(answered, borderRows, borderCols, line, col)
                        : answered;
                }
            }

            return MatrixToRows(AssembleBlocks(grid, line, col));
        });
    }

    /// <summary>
    /// The body of <c>wiener2</c>, shared with the <c>[J, noise] = wiener2(...)</c> form. The estimated
    /// noise power is the whole point of the second output: it is what a script passes back in to
    /// filter a second frame with the same strength.
    /// </summary>
    private static JgsValue[] WienerOutputs(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("wiener2", args, 1, 3, line, col);
        using ImgArg source = ImgLike("wiener2", args, 0, line, col);
        (int rows, int cols) = args.Count >= 2 ? WindowOf("wiener2", args[1], line, col) : (3, 3);
        double? noise = args.Count >= 3 ? Num("wiener2", args, 2, line, col) : null;
        (ImageBuffer result, double estimated) = Neighborhoods.Wiener(source.Buffer, rows, cols, noise);
        JgsValue filtered = ImgLikeOut(result, source);
        return wanted < 2 ? [filtered] : [filtered, JgsValue.Number(estimated)];
    }

    /// <summary>The body of <c>bestblk</c>, shared with the <c>[mb, nb] = bestblk(...)</c> form.</summary>
    private static JgsValue[] BestBlkOutputs(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("bestblk", args, 1, 2, line, col);
        (int rows, int cols) = WindowOf("bestblk", args[0], line, col);
        int limit = args.Count >= 2 ? Count("bestblk", args, 1, line, col) : 100;
        double blockRows = BlockProcessing.BestBlockSize(rows, limit);
        double blockCols = BlockProcessing.BestBlockSize(cols, limit);
        return wanted < 2
            ? [Numbers([blockRows, blockCols])]
            : [JgsValue.Number(blockRows), JgsValue.Number(blockCols)];
    }

    /// <summary>Assembles a grid of returned blocks, checking that they actually tile.</summary>
    private static double[,] AssembleBlocks(double[][][,] grid, int line, int col)
    {
        var rowHeights = new int[grid.Length];
        var colWidths = new int[grid.Length == 0 ? 0 : grid[0].Length];
        for (int rb = 0; rb < grid.Length; rb++)
        {
            for (int cb = 0; cb < grid[rb].Length; cb++)
            {
                int h = grid[rb][cb].GetLength(0);
                int w = grid[rb][cb].GetLength(1);
                if (cb == 0) { rowHeights[rb] = h; }
                if (rb == 0) { colWidths[cb] = w; }
                if (h != rowHeights[rb] || w != colWidths[cb])
                {
                    throw new JgsRuntimeException(line, col,
                        "blockproc: the blocks the function returned do not tile — every block in a row " +
                        "must have the same height and every block in a column the same width.");
                }
            }
        }

        int totalRows = 0;
        foreach (int h in rowHeights) { totalRows += h; }
        int totalCols = 0;
        foreach (int w in colWidths) { totalCols += w; }

        var result = new double[Math.Max(1, totalRows), Math.Max(1, totalCols)];
        int rowOffset = 0;
        for (int rb = 0; rb < grid.Length; rb++)
        {
            int colOffset = 0;
            for (int cb = 0; cb < grid[rb].Length; cb++)
            {
                double[,] block = grid[rb][cb];
                for (int r = 0; r < block.GetLength(0); r++)
                {
                    for (int c = 0; c < block.GetLength(1); c++)
                    {
                        result[rowOffset + r, colOffset + c] = block[r, c];
                    }
                }

                colOffset += colWidths[cb];
            }

            rowOffset += rowHeights[rb];
        }

        return result;
    }

    private static double[,] TrimBlockBorder(double[,] block, int borderRows, int borderCols, int line, int col)
    {
        int h = block.GetLength(0) - (2 * borderRows);
        int w = block.GetLength(1) - (2 * borderCols);
        if (h <= 0 || w <= 0)
        {
            throw new JgsRuntimeException(line, col,
                "blockproc: the block the function returned is smaller than the border being trimmed off it; " +
                "pass 'TrimBorder', false if the function already trims.");
        }

        var trimmed = new double[h, w];
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                trimmed[r, c] = block[r + borderRows, c + borderCols];
            }
        }

        return trimmed;
    }

    /// <summary>A window size given as one number (square) or a [rows, cols] pair.</summary>
    private static (int Rows, int Cols) WindowOf(
        string name, JgsValue value, int line, int col, bool allowZero = false)
    {
        double[] size = NumericVector(name, value, line, col);
        if (size.Length is not (1 or 2))
        {
            throw new JgsRuntimeException(line, col, $"{name} takes a size or a [rows, cols] pair.");
        }

        int rows = (int)Math.Round(size[0]);
        int cols = (int)Math.Round(size[size.Length == 2 ? 1 : 0]);
        int floor = allowZero ? 0 : 1;
        if (rows < floor || cols < floor)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} sizes must be whole numbers of at least {floor}.");
        }

        return (rows, cols);
    }

    /// <summary>A neighbourhood from a matrix of flags; every non-zero entry takes part.</summary>
    private static bool[,] DomainOf(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        double[,] values = Matrix(name, args, index, line, col);
        int rows = values.GetLength(0);
        int cols = values.GetLength(1);
        var domain = new bool[rows, cols];
        bool any = false;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                domain[r, c] = values[r, c] != 0;
                any |= domain[r, c];
            }
        }

        if (!any)
        {
            throw new JgsRuntimeException(line, col, $"{name}: the neighbourhood has no positions in it.");
        }

        return domain;
    }

    private static IJgsCallable Callable(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        JgsValue value = args[index];
        if (value.Type != JgsType.Function)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} expects argument {index + 1} to be a function handle, but got a {value.TypeName}.");
        }

        return value.AsCallable;
    }

    /// <summary>
    /// A <c>'Padding'</c> or <c>'PadMethod'</c> option, which MATLAB lets be either a boundary word or
    /// a constant to fill with.
    /// </summary>
    private static (Filters.Boundary Boundary, double PadValue) PaddingOption(
        string name, ParsedArgs parsed, Filters.Boundary fallback, int line, int col, string option = "Padding")
    {
        if (parsed.Named(option) is not { } value)
        {
            return (fallback, 0.0);
        }

        if (value.Type == JgsType.Number)
        {
            return (Filters.Boundary.Zero, value.AsNumber);
        }

        if (value.Type != JgsType.String)
        {
            throw new JgsRuntimeException(line, col, $"{name}: '{option}' takes a word or a number.");
        }

        return value.AsString.ToLowerInvariant() switch
        {
            "replicate" => (Filters.Boundary.Replicate, 0.0),
            "symmetric" => (Filters.Boundary.Symmetric, 0.0),
            "circular" => (Filters.Boundary.Circular, 0.0),
            "zeros" or "0" => (Filters.Boundary.Zero, 0.0),
            _ => throw new JgsRuntimeException(line, col,
                $"{name}: unknown '{option}' value '{value.AsString}' " +
                "(use 'replicate', 'symmetric', 'circular', or a number)."),
        };
    }

    private static BlockProcessing.BlockKind ParseBlockKind(string name, string word, int line, int col) =>
        word.ToLowerInvariant() switch
        {
            "sliding" => BlockProcessing.BlockKind.Sliding,
            "distinct" => BlockProcessing.BlockKind.Distinct,
            _ => throw new JgsRuntimeException(line, col,
                $"{name}: unknown block type '{word}' (use 'sliding' or 'distinct')."),
        };
}
