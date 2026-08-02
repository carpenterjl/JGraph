using JGraph.Imaging;
using JGraph.Numerics.Sparse;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M46 wave H: the cosine transform (<c>dct2</c>/<c>idct2</c>/<c>dctmtx</c>), the Radon pair and the
/// head phantom, quadtree decomposition and its block accessors, and the two correlation searches
/// (<c>normxcorr2</c> and <c>imregcorr</c>).
/// </summary>
/// <remarks>
/// Everything here answers with plain numbers rather than a picture, even when a picture went in.
/// A set of DCT coefficients, a sinogram and a correlation surface are all quantities measured
/// <em>about</em> an image and are signed, unbounded, and in the wrong units to display — which is
/// exactly what MATLAB means by returning <c>double</c> from all of them.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>The interpolation words <c>iradon</c> accepts.</summary>
    private static readonly string[] IradonInterpolations = ["nearest", "linear"];

    /// <summary>The filter words <c>iradon</c> accepts, spelled as MATLAB spells them.</summary>
    private static readonly string[] IradonFilters =
        ["Ram-Lak", "Shepp-Logan", "Cosine", "Hamming", "Hann", "none"];

    private static readonly ImgOptionSpec ImRegCorrSpec = new(
        "imregcorr",
        ["translation", "rigid", "similarity"],
        ["transformType", "Window"],
        StringPositionals: 0);

    private static void DefineTransformBuiltins(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> define, JgsDialect dialect)
    {
        // --- The cosine transform ----------------------------------------------------------------
        define("dctmtx", (args, line, col) =>
        {
            Arity("dctmtx", args, 1, line, col);
            try
            {
                return MatrixToRows(CosineTransforms.Matrix(Count("dctmtx", args, 0, line, col)));
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new JgsRuntimeException(line, col, $"dctmtx: {ex.Message}");
            }
        });

        define("dct2", (args, line, col) => CosineValue("dct2", args, line, col, forward: true));
        define("idct2", (args, line, col) => CosineValue("idct2", args, line, col, forward: false));

        // --- Radon -------------------------------------------------------------------------------
        define("radon", (args, line, col) => RadonOutputs(args, 1, line, col)[0]);
        define("iradon", (args, line, col) => IradonOutputs(args, 1, line, col)[0]);
        define("phantom", (args, line, col) => PhantomOutputs(args, 1, line, col)[0]);

        // --- Quadtrees ---------------------------------------------------------------------------
        define("qtdecomp", (args, line, col) =>
        {
            ArityRange("qtdecomp", args, 1, 3, line, col);
            using ImgArg source = ImgLike("qtdecomp", args, 0, line, col);
            double[,] values = PointOps.ToMatrix(source.Buffer, 0);
            int side = values.GetLength(0);

            int minDim = 1;
            int maxDim = side;
            Func<double[][], bool[]> test;

            if (args.Count >= 2 && args[1].Type == JgsType.Function)
            {
                IJgsCallable fun = Callable("qtdecomp", args, 1, line, col);
                test = blocks => BlockVerdicts(fun, blocks, line, col);
                if (args.Count > 2)
                {
                    throw new JgsRuntimeException(line, col,
                        "qtdecomp: extra arguments are not passed through to the test function; " +
                        "capture what it needs in the function itself.");
                }
            }
            else
            {
                double threshold = args.Count >= 2 ? Num("qtdecomp", args, 1, line, col) : 0.0;
                test = blocks => Quadtree.SpreadTest(blocks, threshold);
                if (args.Count == 3)
                {
                    double[] limits = NumericVector("qtdecomp", args, 2, line, col);
                    minDim = WholeSize(limits[0]);
                    maxDim = limits.Length >= 2 ? WholeSize(limits[1]) : side;
                    if (limits.Length > 2)
                    {
                        throw new JgsRuntimeException(line, col,
                            "qtdecomp: the size limit is one number or a [mindim maxdim] pair.");
                    }
                }
            }

            try
            {
                return SizeMap(Quadtree.Decompose(values, test, minDim, maxDim));
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"qtdecomp: {ex.Message}");
            }
        });

        define("qtgetblk", (args, line, col) => QtGetBlkOutputs(args, 1, line, col, dialect)[0]);

        define("qtsetblk", (args, line, col) =>
        {
            Arity("qtsetblk", args, 4, line, col);
            using ImgArg source = ImgLike("qtsetblk", args, 0, line, col);
            double[,] values = PointOps.ToMatrix(source.Buffer, 0);
            int[,] sizes = ReadSizeMap("qtsetblk", args, 1, line, col);
            int dim = Count("qtsetblk", args, 2, line, col);
            IReadOnlyList<(int Row, int Col)> corners = Quadtree.Corners(sizes, dim);

            double[][] blocks = ReadPages("qtsetblk", args[3], dim, corners.Count, line, col);
            try
            {
                return MatrixToRows(Quadtree.Write(values, corners, dim, blocks));
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"qtsetblk: {ex.Message}");
            }
            catch (IndexOutOfRangeException)
            {
                throw new JgsRuntimeException(line, col,
                    $"qtsetblk: the block map does not describe blocks of size {dim} in this picture.");
            }
        });

        // --- Correlation -------------------------------------------------------------------------
        define("normxcorr2", (args, line, col) =>
        {
            Arity("normxcorr2", args, 2, line, col);
            using ImgArg template = ImgLike("normxcorr2", args, 0, line, col);
            using ImgArg picture = ImgLike("normxcorr2", args, 1, line, col);
            try
            {
                return MatrixToRows(Correlation.Normalized(
                    PointOps.ToMatrix(template.Buffer, 0), PointOps.ToMatrix(picture.Buffer, 0)));
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"normxcorr2: {ex.Message}");
            }
        });

        define("imregcorr", (args, line, col) => ImRegCorrOutputs(args, 1, line, col)[0]);
    }

    /// <summary>Shared body for <c>dct2</c> and <c>idct2</c>, including their resizing forms.</summary>
    private static JgsValue CosineValue(
        string name, IReadOnlyList<JgsValue> args, int line, int col, bool forward)
    {
        ArityRange(name, args, 1, 3, line, col);
        using ImgArg source = ImgLike(name, args, 0, line, col);
        double[,] values = PointOps.ToMatrix(source.Buffer, 0);

        if (args.Count > 1)
        {
            int rows;
            int cols;
            if (args.Count == 2)
            {
                double[] size = NumericVector(name, args, 1, line, col);
                if (size.Length != 2)
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name}(A, [m n]) takes a two-element size, or {name}(A, m, n).");
                }

                rows = WholeSize(size[0]);
                cols = WholeSize(size[1]);
            }
            else
            {
                rows = Count(name, args, 1, line, col);
                cols = Count(name, args, 2, line, col);
            }

            values = CosineTransforms.Resize(values, rows, cols);
        }

        return MatrixToRows(forward ? CosineTransforms.Forward(values) : CosineTransforms.Inverse(values));
    }

    /// <summary><c>[R, xp] = radon(I, theta)</c>.</summary>
    private static JgsValue[] RadonOutputs(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("radon", args, 1, 2, line, col);
        using ImgArg source = ImgLike("radon", args, 0, line, col);

        double[] angles;
        if (args.Count == 2)
        {
            angles = NumericVector("radon", args, 1, line, col);
        }
        else
        {
            // MATLAB's default sweep: half a turn, because a projection and its opposite carry the
            // same information.
            angles = new double[180];
            for (int i = 0; i < 180; i++)
            {
                angles[i] = i;
            }
        }

        try
        {
            (double[,] sinogram, double[] coordinates) = RadonTransform.Forward(
                PointOps.ToMatrix(source.Buffer, 0), angles);
            return wanted < 2
                ? [MatrixToRows(sinogram)]
                : [MatrixToRows(sinogram), Numbers(coordinates)];
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"radon: {ex.Message}");
        }
    }

    /// <summary><c>[I, H] = iradon(R, theta, …)</c>.</summary>
    /// <remarks>
    /// MATLAB does not fix the order of the trailing arguments: it looks at each one and decides
    /// what it must be. A word is an interpolation or a filter depending on which list it appears
    /// in, and a number is the frequency scaling if it is at most one and the output size otherwise
    /// — because a scaling is a fraction and a picture is never one pixel across.
    /// </remarks>
    private static JgsValue[] IradonOutputs(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("iradon", args, 2, 6, line, col);
        double[,] sinogram = Rectangle("iradon argument 1", args[0], line, col);
        double[] angles = AnglesFor(sinogram.GetLength(1), args[1], line, col);

        var interpolation = RadonTransform.Interpolation.Linear;
        var filter = RadonTransform.Filter.RamLak;
        double scaling = 1.0;
        int size = RadonTransform.DefaultOutputSize(sinogram.GetLength(0));

        for (int i = 2; i < args.Count; i++)
        {
            JgsValue arg = args[i];
            if (arg.Type == JgsType.String)
            {
                string word = arg.AsString;
                if (Matches(IradonInterpolations, word))
                {
                    interpolation = word.Equals("nearest", StringComparison.OrdinalIgnoreCase)
                        ? RadonTransform.Interpolation.Nearest
                        : RadonTransform.Interpolation.Linear;
                }
                else if (Matches(IradonFilters, word))
                {
                    filter = RadonTransform.ParseFilter(word.ToLowerInvariant());
                }
                else
                {
                    throw new JgsRuntimeException(line, col,
                        $"iradon: unknown option '{word}' (interpolation: " +
                        $"{string.Join(", ", IradonInterpolations.Select(w => $"'{w}'"))}; filter: " +
                        $"{string.Join(", ", IradonFilters.Select(w => $"'{w}'"))}).");
                }

                continue;
            }

            double number = Num("iradon", args, i, line, col);
            if (number <= 1)
            {
                scaling = number;
            }
            else
            {
                size = WholeSize(number);
            }
        }

        try
        {
            (double[,] image, double[] response) = RadonTransform.Inverse(
                sinogram, angles, interpolation, filter, scaling, size);
            return wanted < 2 ? [MatrixToRows(image)] : [MatrixToRows(image), Numbers(response)];
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"iradon: {ex.Message}");
        }
    }

    /// <summary><c>[P, E] = phantom(def, n)</c>.</summary>
    private static JgsValue[] PhantomOutputs(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("phantom", args, 0, 2, line, col);
        double[,] ellipses = Phantoms.ModifiedSheppLogan;
        int size = 256;
        int next = 0;

        if (args.Count > 0 && args[0].Type == JgsType.String)
        {
            try
            {
                ellipses = Phantoms.Parse(args[0].AsString);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"phantom: {ex.Message}");
            }

            next = 1;
        }
        else if (args.Count > 0 && args[0].Type == JgsType.Array &&
                 JgsMatrix.DimsOf(args[0]) is [_, 6])
        {
            // A six-column table is a set of ellipses to draw, which is how a script builds its own
            // phantom rather than one of the two named ones.
            ellipses = Rectangle("phantom argument 1", args[0], line, col);
            next = 1;
        }

        if (args.Count > next)
        {
            size = Count("phantom", args, next, line, col);
        }

        try
        {
            double[,] picture = Phantoms.Draw(ellipses, size);
            return wanted < 2 ? [MatrixToRows(picture)] : [MatrixToRows(picture), MatrixToRows(ellipses)];
        }
        catch (ArgumentException ex)
        {
            // ArgumentOutOfRangeException is one of these, and it is what a non-positive size raises.
            throw new JgsRuntimeException(line, col, $"phantom: {ex.Message}");
        }
    }

    /// <summary><c>[vals, r, c] = qtgetblk(I, S, dim)</c>.</summary>
    private static JgsValue[] QtGetBlkOutputs(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col, JgsDialect dialect)
    {
        Arity("qtgetblk", args, 3, line, col);
        using ImgArg source = ImgLike("qtgetblk", args, 0, line, col);
        double[,] values = PointOps.ToMatrix(source.Buffer, 0);
        int[,] sizes = ReadSizeMap("qtgetblk", args, 1, line, col);
        int dim = Count("qtgetblk", args, 2, line, col);
        IReadOnlyList<(int Row, int Col)> corners = Quadtree.Corners(sizes, dim);

        double[][] blocks;
        try
        {
            blocks = Quadtree.Read(values, corners, dim);
        }
        catch (IndexOutOfRangeException)
        {
            throw new JgsRuntimeException(line, col,
                $"qtgetblk: the block map does not describe blocks of size {dim} in this picture.");
        }

        if (corners.Count == 0)
        {
            // No block of that size: an empty answer, not a page of nothing.
            JgsValue nothing = JgsMatrix.FromColumnMajorDims([], [0, 0]);
            return wanted < 2 ? [nothing] : [nothing, Numbers([]), Numbers([])];
        }

        // The blocks stack into pages of one array, which is how MATLAB hands back a variable number
        // of equally sized things.
        var flat = new double[dim * dim * corners.Count];
        for (int p = 0; p < corners.Count; p++)
        {
            for (int r = 0; r < dim; r++)
            {
                for (int c = 0; c < dim; c++)
                {
                    flat[r + (c * dim) + (p * dim * dim)] = blocks[p][(r * dim) + c];
                }
            }
        }

        JgsValue pages = JgsMatrix.FromColumnMajorDims(flat, [dim, dim, corners.Count]);
        if (wanted < 2)
        {
            return [pages];
        }

        var rows = new double[corners.Count];
        var cols = new double[corners.Count];
        for (int i = 0; i < corners.Count; i++)
        {
            rows[i] = corners[i].Row + dialect.IndexBase;
            cols[i] = corners[i].Col + dialect.IndexBase;
        }

        return [pages, Numbers(rows), Numbers(cols)];
    }

    /// <summary><c>[tform, peak] = imregcorr(moving, fixed, …)</c>.</summary>
    private static JgsValue[] ImRegCorrOutputs(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("imregcorr", args, 2, 8, line, col);
        ImgArgs parsed = ImRegCorrSpec.Parse(args, 2, line, col);
        if (parsed.Positional.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "imregcorr(moving, fixed) needs two pictures.");
        }

        // The type may arrive as a bare word or behind 'transformType' — MATLAB documents the
        // name-value form and accepts the bare one, and a script written either way must work.
        string kind = parsed.Text("transformType")
            ?? parsed.OneOf("similarity", "translation", "rigid", "similarity");
        bool translationOnly = kind.Equals("translation", StringComparison.OrdinalIgnoreCase);
        bool scaling = kind.Equals("similarity", StringComparison.OrdinalIgnoreCase);
        bool rigid = kind.Equals("rigid", StringComparison.OrdinalIgnoreCase);
        if (!translationOnly && !scaling && !rigid)
        {
            throw new JgsRuntimeException(line, col,
                $"imregcorr: unknown transform type '{kind}' " +
                "(use 'translation', 'rigid', or 'similarity').");
        }

        bool rotation = !translationOnly;

        using ImgArg moving = ImgLike("imregcorr", parsed.Positional, 0, line, col);
        using ImgArg reference = ImgLike("imregcorr", parsed.Positional, 1, line, col);
        double[,] movingValues = PointOps.ToMatrix(moving.Buffer, 0);

        (double scale, double degrees, double dx, double dy, double peak) = Correlation.Register(
            movingValues, PointOps.ToMatrix(reference.Buffer, 0), rotation, scaling);

        // The rotation is about the moving picture's own centre, so the translation the transform
        // carries has to absorb wherever that centre landed.
        double radians = degrees * Math.PI / 180.0;
        double a = scale * Math.Cos(radians);
        double b = scale * Math.Sin(radians);
        double centreX = (movingValues.GetLength(1) + 1) / 2.0;
        double centreY = (movingValues.GetLength(0) + 1) / 2.0;

        var matrix = new double[,]
        {
            { a, b, 0 },
            { -b, a, 0 },
            {
                centreX + dx - ((a * centreX) - (b * centreY)),
                centreY + dy - ((b * centreX) + (a * centreY)),
                1
            },
        };

        JgsValue tform = TransformValue(rigid ? "rigid2d" : "affine2d", new GeometricTransform(matrix));
        return wanted < 2 ? [tform] : [tform, JgsValue.Number(peak)];
    }

    /// <summary>Asks a script's own test function about a whole level of blocks at once.</summary>
    private static bool[] BlockVerdicts(IJgsCallable fun, double[][] blocks, int line, int col)
    {
        if (blocks.Length == 0)
        {
            return [];
        }

        int dim = (int)Math.Round(Math.Sqrt(blocks[0].Length));
        var flat = new double[dim * dim * blocks.Length];
        for (int p = 0; p < blocks.Length; p++)
        {
            for (int r = 0; r < dim; r++)
            {
                for (int c = 0; c < dim; c++)
                {
                    flat[r + (c * dim) + (p * dim * dim)] = blocks[p][(r * dim) + c];
                }
            }
        }

        JgsValue answered = fun.Call(
            [JgsMatrix.FromColumnMajorDims(flat, [dim, dim, blocks.Length])], line, col);

        // A single block gets a single answer, and a single answer is a scalar rather than a
        // one-element array — so the answer is read the way any numeric argument is.
        double[] verdicts = NumericVector("qtdecomp", [answered], 0, line, col);
        if (verdicts.Length != blocks.Length)
        {
            throw new JgsRuntimeException(line, col,
                $"qtdecomp: the test function was given {blocks.Length} blocks but answered " +
                $"{verdicts.Length} times.");
        }

        return verdicts.Select(v => v != 0).ToArray();
    }

    /// <summary>
    /// Wraps a block-size map as a sparse matrix, which is what it is: one entry per block, and a
    /// picture split into a hundred blocks has a hundred entries however many pixels it holds.
    /// </summary>
    private static JgsValue SizeMap(int[,] sizes)
    {
        var triplets = new List<(int Row, int Col, double Value)>();
        for (int c = 0; c < sizes.GetLength(1); c++)
        {
            for (int r = 0; r < sizes.GetLength(0); r++)
            {
                if (sizes[r, c] != 0)
                {
                    triplets.Add((r, c, sizes[r, c]));
                }
            }
        }

        return JgsValue.Sparse(CscMatrix.FromTriplets(sizes.GetLength(0), sizes.GetLength(1), triplets));
    }

    /// <summary>Reads a block-size map back, whether it arrived sparse or was made dense along the way.</summary>
    private static int[,] ReadSizeMap(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        if (args[index].Type == JgsType.Sparse)
        {
            CscMatrix sparse = args[index].AsSparse;
            var sizes = new int[sparse.Rows, sparse.Cols];
            double[] dense = sparse.ToColumnMajor();
            for (int c = 0; c < sparse.Cols; c++)
            {
                for (int r = 0; r < sparse.Rows; r++)
                {
                    sizes[r, c] = (int)Math.Round(dense[r + (c * sparse.Rows)]);
                }
            }

            return sizes;
        }

        double[,] values = Rectangle($"{name} argument {index + 1}", args[index], line, col);
        var map = new int[values.GetLength(0), values.GetLength(1)];
        for (int r = 0; r < map.GetLength(0); r++)
        {
            for (int c = 0; c < map.GetLength(1); c++)
            {
                map[r, c] = (int)Math.Round(values[r, c]);
            }
        }

        return map;
    }

    /// <summary>Reads a stack of equally sized blocks back out of an <c>m×m×k</c> array.</summary>
    private static double[][] ReadPages(
        string name, JgsValue value, int dim, int count, int line, int col)
    {
        double[] flat = ToDoubles(name, value, line, col);
        if (flat.Length != dim * dim * count)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: expected {count} blocks of {dim}-by-{dim} ({dim * dim * count} values), " +
                $"but got {flat.Length}.");
        }

        var blocks = new double[count][];
        for (int p = 0; p < count; p++)
        {
            var block = new double[dim * dim];
            for (int r = 0; r < dim; r++)
            {
                for (int c = 0; c < dim; c++)
                {
                    block[(r * dim) + c] = flat[r + (c * dim) + (p * dim * dim)];
                }
            }

            blocks[p] = block;
        }

        return blocks;
    }

    /// <summary>
    /// Reads <c>iradon</c>'s angle argument, which MATLAB lets you give either as one angle per
    /// column or as a single increment covering half a turn.
    /// </summary>
    private static double[] AnglesFor(int columns, JgsValue value, int line, int col)
    {
        double[] given = NumericVector("iradon", [value], 0, line, col);
        if (given.Length == columns)
        {
            return given;
        }

        if (given.Length == 1)
        {
            var swept = new double[columns];
            for (int i = 0; i < columns; i++)
            {
                swept[i] = i * given[0];
            }

            return swept;
        }

        throw new JgsRuntimeException(line, col,
            $"iradon: {given.Length} angles were given for {columns} projections; " +
            "give one angle per column, or a single increment.");
    }

    private static bool Matches(string[] words, string given) =>
        words.Any(w => string.Equals(w, given, StringComparison.OrdinalIgnoreCase));

    /// <summary>A size argument as a whole number, refusing anything that is not one.</summary>
    private static int WholeSize(double value)
    {
        int rounded = (int)Math.Round(value);
        return rounded < 1 ? 1 : rounded;
    }
}
