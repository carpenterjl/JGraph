namespace JGraph.Numerics;

/// <summary>
/// Changes of storage layout that are copies and never arithmetic: the bits that go in are the
/// bits that come out, only elsewhere.
/// </summary>
/// <remarks>
/// A column-major rectangle read row-major is its transpose, so the one operation here is both
/// the transpose of a packed matrix and the conversion between the interpreter's column-major
/// buffers and a <c>double[,]</c>. Done as one strided loop it walks the source a whole column
/// stride per element and misses on every line; done by tiles it reads and writes a tile's
/// worth of lines that stay in the core's own cache until the tile is finished (item 08a of the
/// head2head_v3 gap-closure plan, ADR 0155).
/// </remarks>
public static class MatrixLayout
{
    /// <summary>
    /// The tile side, in elements: sixty-four doubles is 512 bytes, so a tile is sixty-four lines
    /// of eight in and the same out, thirty-two kilobytes each way, which fits a first-level cache
    /// with room to spare.
    /// </summary>
    public const int Tile = 64;

    /// <summary>
    /// Writes the transpose of <paramref name="source"/>, a column-major <paramref name="rows"/>
    /// by <paramref name="cols"/> rectangle, into <paramref name="target"/> as a column-major
    /// <paramref name="cols"/> by <paramref name="rows"/> one — the same bytes as the source laid
    /// out row-major.
    /// </summary>
    /// <remarks>
    /// The work is cut into strips of <see cref="Tile"/> source columns, each of which owns the
    /// same <see cref="Tile"/> rows of the target and nothing else; the strips run in parallel when
    /// the rectangle is at least <see cref="ParallelKernels.MemoryBoundThreshold"/> elements. The
    /// cut is a function of the shape alone, and a copy has no rounding to reorder, so the bits do
    /// not depend on the thread count.
    /// </remarks>
    public static void Transpose(ReadOnlySpan<double> source, int rows, int cols, Span<double> target)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rows);
        ArgumentOutOfRangeException.ThrowIfNegative(cols);
        long count = (long)rows * cols;
        if (source.Length != count || target.Length != count)
        {
            throw new ArgumentException(
                $"a {rows}x{cols} transpose needs {count} elements in and out, got {source.Length} and {target.Length}.");
        }

        if (count == 0)
        {
            return;
        }

        if (rows == 1 || cols == 1)
        {
            // A vector's two layouts are the same bytes.
            source.CopyTo(target);
            return;
        }

        int strips = ((cols - 1) / Tile) + 1;
        int bands = ((rows - 1) / Tile) + 1;
        bool wide = count >= ParallelKernels.MemoryBoundThreshold;
        unsafe
        {
            fixed (double* ps = source, pt = target)
            {
                double* s = ps;
                double* t = pt;
                ParallelKernels.ForBlocks(strips, wide, strip =>
                {
                    int c0 = strip * Tile;
                    int c1 = Math.Min(c0 + Tile, cols);
                    int width = c1 - c0;
                    for (int band = 0; band < bands; band++)
                    {
                        int r0 = band * Tile;
                        int r1 = Math.Min(r0 + Tile, rows);
                        for (int r = r0; r < r1; r++)
                        {
                            // One target line per source row of the tile: written contiguously,
                            // read a column stride apart.
                            double* line = t + ((long)r * cols) + c0;
                            double* from = s + r + ((long)c0 * rows);
                            for (int c = 0; c < width; c++)
                            {
                                line[c] = from[(long)c * rows];
                            }
                        }
                    }
                });
            }
        }
    }
}
