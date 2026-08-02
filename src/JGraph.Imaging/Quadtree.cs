namespace JGraph.Imaging;

/// <summary>
/// Quadtree decomposition — <c>qtdecomp</c> and the block accessors that read and write what it
/// found.
/// </summary>
/// <remarks>
/// The idea is to spend detail only where a picture has any: start with the whole square, ask
/// whether it is uniform enough to keep, and split it into four when it is not. Sky becomes one
/// enormous block and a hedge becomes a thousand tiny ones, so the decomposition is a map of where
/// the information in a picture actually lives — which is why it underlies both region-based
/// compression and adaptive segmentation.
/// </remarks>
public static class Quadtree
{
    /// <summary>
    /// Decomposes a square picture, returning the block-size map: the entry at a block's top-left
    /// corner is that block's side, and every other entry is zero.
    /// </summary>
    /// <param name="image">The picture, row-major and square.</param>
    /// <param name="shouldSplit">
    /// Asked once per level with every block of that size at once — flattened row-major, one array
    /// each — and answering for each whether it should be split further. Batching by level rather
    /// than calling per block is what makes a script-supplied test affordable.
    /// </param>
    /// <param name="minDim">The smallest block to produce; blocks this size are never tested.</param>
    /// <param name="maxDim">The largest block to keep; anything bigger is split without being asked.</param>
    /// <returns>The block-size map, the same size as the picture.</returns>
    public static int[,] Decompose(
        double[,] image, Func<double[][], bool[]> shouldSplit, int minDim, int maxDim)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(shouldSplit);
        int side = Check(image, minDim, maxDim);

        var sizes = new int[side, side];
        var current = new List<(int Row, int Col)> { (0, 0) };
        int dim = side;

        // Anything larger than the ceiling is split on sight — no test, because the answer is fixed.
        while (dim > maxDim)
        {
            current = Split(current, dim);
            dim /= 2;
        }

        while (dim > minDim)
        {
            double[][] blocks = Read(image, current, dim);
            bool[] verdicts = shouldSplit(blocks);
            if (verdicts.Length != current.Count)
            {
                throw new ArgumentException(
                    $"the split test answered {verdicts.Length} times for {current.Count} blocks.",
                    nameof(shouldSplit));
            }

            var next = new List<(int Row, int Col)>();
            for (int i = 0; i < current.Count; i++)
            {
                if (verdicts[i])
                {
                    AddChildren(next, current[i], dim);
                }
                else
                {
                    sizes[current[i].Row, current[i].Col] = dim;
                }
            }

            current = next;
            dim /= 2;
        }

        foreach ((int row, int col) in current)
        {
            sizes[row, col] = dim;
        }

        return sizes;
    }

    /// <summary>
    /// The default split test: a block stays whole while the spread between its lightest and darkest
    /// sample is no more than the threshold.
    /// </summary>
    /// <param name="blocks">The blocks at one level, each flattened.</param>
    /// <param name="threshold">The tolerated spread.</param>
    /// <returns>One answer per block.</returns>
    public static bool[] SpreadTest(double[][] blocks, double threshold)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        var verdicts = new bool[blocks.Length];
        for (int i = 0; i < blocks.Length; i++)
        {
            double low = double.PositiveInfinity;
            double high = double.NegativeInfinity;
            foreach (double value in blocks[i])
            {
                low = Math.Min(low, value);
                high = Math.Max(high, value);
            }

            verdicts[i] = high - low > threshold;
        }

        return verdicts;
    }

    /// <summary>
    /// The top-left corners of every block of the given size, in column-major order — the order
    /// <c>find</c> walks a sparse matrix, and so the order <c>qtgetblk</c> and <c>qtsetblk</c> agree on.
    /// </summary>
    /// <param name="sizes">A block-size map from <see cref="Decompose"/>.</param>
    /// <param name="dim">The block side to look for.</param>
    /// <returns>The corners.</returns>
    public static IReadOnlyList<(int Row, int Col)> Corners(int[,] sizes, int dim)
    {
        ArgumentNullException.ThrowIfNull(sizes);
        var corners = new List<(int Row, int Col)>();
        for (int c = 0; c < sizes.GetLength(1); c++)
        {
            for (int r = 0; r < sizes.GetLength(0); r++)
            {
                if (sizes[r, c] == dim)
                {
                    corners.Add((r, c));
                }
            }
        }

        return corners;
    }

    /// <summary>Copies the named blocks out of a picture, each flattened row-major.</summary>
    /// <param name="image">The picture.</param>
    /// <param name="corners">The blocks' top-left corners.</param>
    /// <param name="dim">The block side.</param>
    /// <returns>One flattened block per corner.</returns>
    public static double[][] Read(
        double[,] image, IReadOnlyList<(int Row, int Col)> corners, int dim)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(corners);
        var blocks = new double[corners.Count][];
        for (int i = 0; i < corners.Count; i++)
        {
            var block = new double[dim * dim];
            for (int r = 0; r < dim; r++)
            {
                for (int c = 0; c < dim; c++)
                {
                    block[(r * dim) + c] = image[corners[i].Row + r, corners[i].Col + c];
                }
            }

            blocks[i] = block;
        }

        return blocks;
    }

    /// <summary>Writes the given blocks back into a copy of the picture.</summary>
    /// <param name="image">The picture to copy and write into.</param>
    /// <param name="corners">The blocks' top-left corners.</param>
    /// <param name="dim">The block side.</param>
    /// <param name="blocks">One flattened block per corner.</param>
    /// <returns>The updated picture.</returns>
    public static double[,] Write(
        double[,] image, IReadOnlyList<(int Row, int Col)> corners, int dim, IReadOnlyList<double[]> blocks)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(corners);
        ArgumentNullException.ThrowIfNull(blocks);
        if (blocks.Count != corners.Count)
        {
            throw new ArgumentException(
                $"there are {corners.Count} blocks of size {dim} but {blocks.Count} were given.",
                nameof(blocks));
        }

        var result = (double[,])image.Clone();
        for (int i = 0; i < corners.Count; i++)
        {
            for (int r = 0; r < dim; r++)
            {
                for (int c = 0; c < dim; c++)
                {
                    result[corners[i].Row + r, corners[i].Col + c] = blocks[i][(r * dim) + c];
                }
            }
        }

        return result;
    }

    private static int Check(double[,] image, int minDim, int maxDim)
    {
        int rows = image.GetLength(0);
        int cols = image.GetLength(1);
        if (rows != cols)
        {
            throw new ArgumentException(
                $"qtdecomp needs a square picture, but this one is {rows}-by-{cols}.", nameof(image));
        }

        if (minDim < 1 || maxDim < minDim || maxDim > rows)
        {
            throw new ArgumentException(
                "the block sizes must satisfy 1 <= mindim <= maxdim <= the picture's side.", nameof(minDim));
        }

        // Halving has to reach exactly the floor and the ceiling, or a block would straddle them.
        if (rows % minDim != 0 || !IsPowerOfTwo(rows / minDim))
        {
            throw new ArgumentException(
                $"qtdecomp needs the side ({rows}) to be the smallest block size ({minDim}) times a power of two.",
                nameof(image));
        }

        if (maxDim % minDim != 0 || !IsPowerOfTwo(maxDim / minDim))
        {
            throw new ArgumentException(
                $"the largest block size ({maxDim}) must be the smallest ({minDim}) times a power of two.",
                nameof(maxDim));
        }

        return rows;
    }

    private static bool IsPowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;

    private static List<(int Row, int Col)> Split(List<(int Row, int Col)> blocks, int dim)
    {
        var next = new List<(int Row, int Col)>(blocks.Count * 4);
        foreach ((int Row, int Col) block in blocks)
        {
            AddChildren(next, block, dim);
        }

        return next;
    }

    private static void AddChildren(List<(int Row, int Col)> into, (int Row, int Col) block, int dim)
    {
        int half = dim / 2;
        into.Add((block.Row, block.Col));
        into.Add((block.Row + half, block.Col));
        into.Add((block.Row, block.Col + half));
        into.Add((block.Row + half, block.Col + half));
    }
}
