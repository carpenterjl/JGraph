namespace JGraph.Imaging;

/// <summary>
/// Rearranging an image into blocks and back: MATLAB's <c>im2col</c>, <c>col2im</c>, <c>bestblk</c>,
/// and the block extraction that <c>blockproc</c>, <c>nlfilter</c> and <c>colfilt</c> iterate over.
/// </summary>
/// <remarks>
/// Block and element order is MATLAB's throughout — column-major within a block, and column-major over
/// the grid of blocks. The matrices here are C# row-major <c>double[,]</c>, so every ordering is spelt
/// out in index arithmetic rather than inherited from the storage; getting that backwards would put
/// the right numbers in the wrong columns, which no size check would catch.
/// </remarks>
public static class BlockProcessing
{
    /// <summary>How a block grid is laid over an image.</summary>
    public enum BlockKind
    {
        /// <summary>Every m×n neighbourhood, one per pixel position where the block fits.</summary>
        Sliding,

        /// <summary>Non-overlapping tiles, zero-padded to a whole number of blocks.</summary>
        Distinct,
    }

    /// <summary>
    /// Rearranges image blocks into the columns of a matrix (MATLAB <c>im2col</c>). Sliding blocks give
    /// one column per position the block fits; distinct blocks tile the image, zero-padded to fit.
    /// </summary>
    public static double[,] Im2Col(double[,] a, int blockRows, int blockCols, BlockKind kind)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockRows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockCols);
        int m = a.GetLength(0);
        int n = a.GetLength(1);
        int elements = blockRows * blockCols;

        if (kind == BlockKind.Sliding)
        {
            if (blockRows > m || blockCols > n)
            {
                throw new ArgumentException("im2col sliding blocks must fit inside the matrix.");
            }

            int downs = m - blockRows + 1;
            int rights = n - blockCols + 1;
            var sliding = new double[elements, downs * rights];
            for (int c = 0; c < rights; c++)
            {
                for (int r = 0; r < downs; r++)
                {
                    int column = (c * downs) + r;
                    for (int dc = 0; dc < blockCols; dc++)
                    {
                        for (int dr = 0; dr < blockRows; dr++)
                        {
                            sliding[(dc * blockRows) + dr, column] = a[r + dr, c + dc];
                        }
                    }
                }
            }

            return sliding;
        }

        int blocksDown = (m + blockRows - 1) / blockRows;
        int blocksAcross = (n + blockCols - 1) / blockCols;
        var distinct = new double[elements, blocksDown * blocksAcross];
        for (int cb = 0; cb < blocksAcross; cb++)
        {
            for (int rb = 0; rb < blocksDown; rb++)
            {
                int column = (cb * blocksDown) + rb;
                for (int dc = 0; dc < blockCols; dc++)
                {
                    int sc = (cb * blockCols) + dc;
                    for (int dr = 0; dr < blockRows; dr++)
                    {
                        int sr = (rb * blockRows) + dr;
                        distinct[(dc * blockRows) + dr, column] = sr < m && sc < n ? a[sr, sc] : 0.0;
                    }
                }
            }
        }

        return distinct;
    }

    /// <summary>
    /// Rebuilds a matrix from column-packed blocks (MATLAB <c>col2im</c>). Distinct blocks are laid
    /// back into the tile grid and cropped to the requested size; sliding input holds one value per
    /// block position and is reshaped into the grid those positions form.
    /// </summary>
    public static double[,] Col2Im(
        double[,] columns, int blockRows, int blockCols, int rows, int cols, BlockKind kind)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockRows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockCols);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cols);

        if (kind == BlockKind.Sliding)
        {
            if (blockRows > rows || blockCols > cols)
            {
                throw new ArgumentException("col2im sliding blocks must fit inside the requested size.");
            }

            int downs = rows - blockRows + 1;
            int rights = cols - blockCols + 1;
            double[] flat = Flatten(columns);
            if (flat.Length != downs * rights)
            {
                throw new ArgumentException(
                    $"col2im sliding needs {downs * rights} values for a {rows}×{cols} result, but got {flat.Length}.");
            }

            var slid = new double[downs, rights];
            for (int c = 0; c < rights; c++)
            {
                for (int r = 0; r < downs; r++)
                {
                    slid[r, c] = flat[(c * downs) + r];
                }
            }

            return slid;
        }

        int elements = blockRows * blockCols;
        if (columns.GetLength(0) != elements)
        {
            throw new ArgumentException(
                $"col2im distinct needs {elements} rows for a {blockRows}×{blockCols} block, but got {columns.GetLength(0)}.");
        }

        int blocksDown = (rows + blockRows - 1) / blockRows;
        int blocksAcross = (cols + blockCols - 1) / blockCols;
        if (columns.GetLength(1) != blocksDown * blocksAcross)
        {
            throw new ArgumentException(
                $"col2im distinct needs {blocksDown * blocksAcross} columns for a {rows}×{cols} result, " +
                $"but got {columns.GetLength(1)}.");
        }

        var result = new double[rows, cols];
        for (int cb = 0; cb < blocksAcross; cb++)
        {
            for (int rb = 0; rb < blocksDown; rb++)
            {
                int column = (cb * blocksDown) + rb;
                for (int dc = 0; dc < blockCols; dc++)
                {
                    int sc = (cb * blockCols) + dc;
                    for (int dr = 0; dr < blockRows; dr++)
                    {
                        int sr = (rb * blockRows) + dr;
                        if (sr < rows && sc < cols)
                        {
                            result[sr, sc] = columns[(dc * blockRows) + dr, column];
                        }
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// A block size for one dimension, at most <paramref name="limit"/> (MATLAB <c>bestblk</c>).
    /// </summary>
    /// <remarks>
    /// The search is confined to <c>[ceil(limit/2), limit]</c>, because the point is a block close to
    /// the limit: every number is divisible by one, and a block size of one divides perfectly while
    /// being useless. Inside that window an exact divisor wins, since it leaves no partial block at
    /// all; failing that, the size whose final partial block is largest does, with ties going to the
    /// larger block. MATLAB documents the goal — blocks that divide the image evenly, or as evenly as
    /// possible — but not its tie-breaking, so this rule is stated here rather than claimed identical.
    /// </remarks>
    public static int BestBlockSize(int extent, int limit = 100)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(extent);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        if (extent <= limit)
        {
            return extent;
        }

        int floor = Math.Max(1, (limit + 1) / 2);
        for (int size = limit; size >= floor; size--)
        {
            if (extent % size == 0)
            {
                return size;
            }
        }

        int best = limit;
        int bestRemainder = -1;
        for (int size = limit; size >= floor; size--)
        {
            int last = extent % size;
            if (last > bestRemainder)
            {
                bestRemainder = last;
                best = size;
            }
        }

        return best;
    }

    /// <summary>
    /// Copies out one block, resolving positions beyond the matrix through the boundary rule. This is
    /// the read the block-iterating builtins share, so <c>blockproc</c> with a border and
    /// <c>nlfilter</c> pad the same way.
    /// </summary>
    /// <param name="a">The source matrix.</param>
    /// <param name="row">Top row of the block, which may be negative.</param>
    /// <param name="col">Left column of the block, which may be negative.</param>
    /// <param name="blockRows">Block rows.</param>
    /// <param name="blockCols">Block columns.</param>
    /// <param name="boundary">How positions outside the matrix are supplied.</param>
    /// <param name="padValue">The constant for <see cref="Filters.Boundary.Zero"/>.</param>
    public static double[,] ExtractBlock(
        double[,] a,
        int row,
        int col,
        int blockRows,
        int blockCols,
        Filters.Boundary boundary = Filters.Boundary.Zero,
        double padValue = 0.0)
    {
        ArgumentNullException.ThrowIfNull(a);
        int m = a.GetLength(0);
        int n = a.GetLength(1);
        var block = new double[blockRows, blockCols];
        for (int dr = 0; dr < blockRows; dr++)
        {
            int sr = row + dr;
            for (int dc = 0; dc < blockCols; dc++)
            {
                int sc = col + dc;
                bool inside = (uint)sr < (uint)m && (uint)sc < (uint)n;
                if (inside)
                {
                    block[dr, dc] = a[sr, sc];
                }
                else if (boundary == Filters.Boundary.Zero)
                {
                    block[dr, dc] = padValue;
                }
                else
                {
                    block[dr, dc] = a[Filters.MapIndex(sr, m, boundary), Filters.MapIndex(sc, n, boundary)];
                }
            }
        }

        return block;
    }

    /// <summary>Flattens a matrix in MATLAB's column-major order.</summary>
    public static double[] Flatten(double[,] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        int rows = values.GetLength(0);
        int cols = values.GetLength(1);
        var flat = new double[rows * cols];
        int k = 0;
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                flat[k++] = values[r, c];
            }
        }

        return flat;
    }
}
