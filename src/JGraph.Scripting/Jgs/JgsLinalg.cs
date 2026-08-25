using JGraph.Numerics;
using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The bridge between packed script values and the dense linear-algebra provider — the one place
/// that combines <see cref="JgsValue"/> with <see cref="DenseLinalg"/> (what <c>PackedMath</c> is
/// to elementwise ops, this is to linear algebra). Packed storage is already flat column-major —
/// exactly the provider's layout — so a product here reads both operand buffers in place and
/// writes one fresh result buffer: zero copies, against the boxed path's four.
/// </summary>
/// <remarks>
/// Lifetime: every native call spans a window where a buffer's memory must not be reclaimed, so
/// each buffer read here is followed by <see cref="GC.KeepAlive(object)"/> after its last use —
/// the same contract <c>PackedMath</c> honors centrally (see <see cref="NumericBuffer"/> remarks).
/// </remarks>
internal static class JgsLinalg
{
    /// <summary>
    /// MATLAB's matrix <c>*</c> for two packed real operands through the provider. Returns false —
    /// leaving the boxed path to run — when the provider is managed (that path already funnels
    /// into the same kernel, so the extra marshalling is the only thing at stake), or when either
    /// operand is boxed, complex, N-D, time-tagged, or empty. Shape rules mirror the boxed path
    /// exactly: shapes as written are tried first, only a vector is reoriented, two bare rows are
    /// refused as ambiguous — and a reoriented vector costs nothing, because a contiguous vector's
    /// storage is the same bytes either way up.
    /// </summary>
    public static bool TryMatrixProduct(JgsValue left, JgsValue right, Node at, out JgsValue product)
    {
        product = null!;
        if (!LinalgProvider.Current.IsNative || !IsPlainPackedReal(left) || !IsPlainPackedReal(right)
            || left.ArrayLength == 0 || right.ArrayLength == 0)
        {
            return false;
        }

        if (left.Rows == 1 && right.Rows == 1 && left.ArrayLength > 1 && right.ArrayLength > 1)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                "'*' between two row vectors is ambiguous: transpose one of them to say which product "
                + "you mean, or use '.*' for the elementwise product and dot(a, b) for the inner product.");
        }

        int rowsA = left.Rows;
        int colsA = left.Cols;
        int rowsB = right.Rows;
        int colsB = right.Cols;
        if (colsA != rowsB)
        {
            if (rowsB == 1 || colsB == 1)
            {
                (rowsB, colsB) = (colsB, rowsB);
            }
            else if (rowsA == 1 || colsA == 1)
            {
                (rowsA, colsA) = (colsA, rowsA);
            }
        }

        if (colsA != rowsB)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                $"Matrix dimensions do not agree for '*': the left has {colsA} columns and the right has {rowsB} rows.");
        }

        NumericBuffer a = left.AsBuffer;
        NumericBuffer b = right.AsBuffer;
        NumericBuffer c = JgsPacking.Allocate((long)rowsA * colsB);
        LinalgProvider.Current.Gemm(transA: false, transB: false, rowsA, colsB, colsA,
            a.AsSpan(), rowsA, b.AsSpan(), colsA, c.AsSpan(), rowsA);
        GC.KeepAlive(a);
        GC.KeepAlive(b);

        if (rowsA == 1 && colsB == 1)
        {
            double scalar = c.AsSpan()[0];
            GC.KeepAlive(c);
            c.Dispose();
            product = JgsValue.Number(scalar);
            return true;
        }

        product = ShapeResult(c, rowsA, colsB);
        return true;
    }

    /// <summary>
    /// The symmetric product <c>A'*A</c> (<paramref name="transposeFirst"/>) or <c>A*A'</c>,
    /// recognized syntactically at the <c>*</c> site the way MATLAB recognizes its syrk patterns.
    /// One triangle is computed and mirrored, so the result is <em>exactly</em> symmetric — under
    /// a blocked native gemm the two independently-summed triangles differ in their last ulps,
    /// and <c>ldl(A'*A)</c> would refuse its own input. Half the flops, none of the asymmetry.
    /// </summary>
    public static bool TrySymmetricProduct(JgsValue baseValue, bool transposeFirst, out JgsValue product)
    {
        product = null!;
        if (!LinalgProvider.Current.IsNative || !IsPlainPackedReal(baseValue) || baseValue.ArrayLength == 0)
        {
            return false;
        }

        int rows = baseValue.Rows;
        int cols = baseValue.Cols;
        int n = transposeFirst ? cols : rows;   // A'*A is cols×cols; A*A' is rows×rows
        int k = transposeFirst ? rows : cols;
        NumericBuffer a = baseValue.AsBuffer;
        NumericBuffer c = JgsPacking.Allocate((long)n * n);
        LinalgProvider.Current.Syrk(transposeFirst, n, k, a.AsSpan(), rows, c.AsSpan(), n);
        GC.KeepAlive(a);

        if (n == 1)
        {
            double scalar = c.AsSpan()[0];
            GC.KeepAlive(c);
            c.Dispose();
            product = JgsValue.Number(scalar);
            return true;
        }

        product = ShapeResult(c, n, n);
        return true;
    }

    /// <summary>
    /// Transposes a packed real 2-D array with a blocked span copy — the boxed path allocates one
    /// wrapper per element and re-packs, which at 2000² costs six times the multiply the transpose
    /// usually feeds. Conjugation is free: real numbers are their own conjugates. Tags (numeric
    /// class, time) are the caller's to carry, exactly as on the boxed path.
    /// </summary>
    public static bool TryTranspose(JgsValue value, out JgsValue result)
    {
        result = null!;
        if (value.Type != JgsType.Array || !value.IsPacked || value.IsNd
            || value.IsStringArray || value.ArrayLength == 0)
        {
            return false;
        }

        int rows = value.Rows;
        int cols = value.Cols;
        NumericBuffer source = value.AsBuffer;
        NumericBuffer flipped = JgsPacking.Allocate((long)rows * cols);
        TransposeBlocked(source.AsSpan(), flipped.AsSpan(), rows, cols);
        GC.KeepAlive(source);

        JgsValue transposed = JgsValue.Packed(flipped, value.PackedKind);
        transposed.Reshape(cols, rows);
        result = transposed;
        return true;
    }

    /// <summary>Tiled out-of-place transpose: source is rows×cols column-major, dest cols×rows.</summary>
    public static void TransposeBlocked(ReadOnlySpan<double> source, Span<double> dest, int rows, int cols)
    {
        const int Tile = 64; // 64×64 doubles = two 32 KB panels, comfortably inside L1+L2
        for (int c0 = 0; c0 < cols; c0 += Tile)
        {
            int cEnd = Math.Min(c0 + Tile, cols);
            for (int r0 = 0; r0 < rows; r0 += Tile)
            {
                int rEnd = Math.Min(r0 + Tile, rows);
                for (int c = c0; c < cEnd; c++)
                {
                    int sourceBase = c * rows;
                    for (int r = r0; r < rEnd; r++)
                    {
                        dest[(r * cols) + c] = source[sourceBase + r];
                    }
                }
            }
        }
    }

    /// <summary>
    /// Whether a value can go to the provider directly: a packed real (or logical) 2-D array with
    /// no time tag. Everything else — boxed, nested rows, complex, N-D, datetime — keeps the
    /// boxed path, whose kernels answer through the same provider.
    /// </summary>
    public static bool IsPlainPackedReal(JgsValue value) =>
        value.Type == JgsType.Array && value.IsPacked && !value.IsNd
        && value.TimeTag is null && !value.IsStringArray;

    /// <summary>A fresh result buffer as a value, with the same collapse rules as the boxed path.</summary>
    private static JgsValue ShapeResult(NumericBuffer buffer, int rows, int cols)
    {
        JgsValue value = JgsValue.Packed(buffer);
        if (rows == 1 || cols == 1)
        {
            // A vector needs no shape beyond the one an array already has, except that a column
            // vector's orientation is exactly the thing worth keeping.
            if (cols == 1 && rows > 1)
            {
                value.Reshape(rows, 1);
            }

            return value;
        }

        value.Reshape(rows, cols);
        return value;
    }
}
