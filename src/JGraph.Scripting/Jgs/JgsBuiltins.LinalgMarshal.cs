using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The marshalling the dense linear-algebra verbs share: a script value in as flat column-major
/// doubles, and a flat column-major result back out as a script value.
/// </summary>
/// <remarks>
/// Column-major is what LAPACK reads and what packed script storage already is, so a packed operand
/// is one block copy — the copy the factorizations need anyway, because LAPACK overwrites what it
/// factors. The rectangular <c>double[,]</c> road these verbs used to travel allocated one heap
/// object per element on the way in (n² of them for an n-by-n matrix) and rebuilt jagged rows on
/// the way out; at n = 2000 that marshalling cost several times the factorization it wrapped.
/// A boxed or nested value still travels the old road, element by element, and lands in the same
/// layout — the two representations must reach the kernels with identical numbers, never with
/// different kernels.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// A scalar, vector, or matrix value as flat column-major doubles. A vector is one row, exactly
    /// as <see cref="RectOf"/> reads it, so the shape rules the verbs apply do not change.
    /// </summary>
    internal static double[] ColumnMajorOf(string name, JgsValue value, out int rows, out int cols, int line, int col)
    {
        if (JgsLinalg.IsPlainPackedReal(value))
        {
            rows = value.Rows;
            cols = value.Cols;
            int length = value.ArrayLength;
            if ((long)rows * cols == length)
            {
                double[] packed = GC.AllocateUninitializedArray<double>(length);
                NumericBuffer buffer = value.AsBuffer;
                buffer.AsSpan(0, length).CopyTo(packed);
                GC.KeepAlive(buffer);
                return packed;
            }
        }

        double[][] jagged = AsJaggedRows(name, value, line, col);
        rows = jagged.Length;
        cols = rows == 0 ? 0 : jagged[0].Length;
        double[] flat = GC.AllocateUninitializedArray<double>(rows * cols);
        for (int r = 0; r < rows; r++)
        {
            if (jagged[r].Length != cols)
            {
                throw new JgsRuntimeException(line, col, $"{name}: matrix rows must have equal lengths.");
            }

            for (int c = 0; c < cols; c++)
            {
                flat[((long)c * rows) + r] = jagged[r][c];
            }
        }

        return flat;
    }

    /// <summary>The same, refusing anything that is not square — the shape a factorization needs.</summary>
    internal static double[] SquareColumnMajorOf(string name, JgsValue value, out int n, int line, int col)
    {
        double[] flat = ColumnMajorOf(name, value, out int rows, out int cols, line, col);
        if (rows != cols)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} needs a square matrix, but got {rows}x{cols}.");
        }

        n = rows;
        return flat;
    }

    /// <summary>
    /// A flat column-major result as a script value, with the collapse rules <see cref="FromRect"/>
    /// applies: a 1-by-1 is a number, a single row or column is a vector, and a column keeps its
    /// orientation. The array is adopted rather than copied when packing is on.
    /// </summary>
    internal static JgsValue FromColumnMajorRect(double[] flat, int rows, int cols) =>
        rows == 1 && cols == 1 ? JgsValue.Number(flat[0]) : JgsMatrix.FromColumnMajor(flat, rows, cols);

    /// <summary>Writes a freshly zeroed column-major result. See <see cref="BuildColumnMajor"/>.</summary>
    internal delegate void ColumnMajorFill(Span<double> destination);

    /// <summary>
    /// Builds a rows-by-cols result straight into the storage that becomes the value — no managed
    /// array in between when packing is on.
    /// </summary>
    /// <remarks>
    /// The destination arrives zeroed, and for a large packed result those zeros come from the
    /// operating system's own zero pages rather than from a pass over the memory: a permutation
    /// matrix writes n entries into n² of storage, and at n = 2000 that is the difference between
    /// 20 ms of zero-filling and none. Filling the whole thing costs the same either way.
    /// </remarks>
    internal static JgsValue BuildColumnMajor(int rows, int cols, ColumnMajorFill fill)
    {
        long length = (long)rows * cols;
        if (!JgsPacking.Enabled || length == 0)
        {
            var flat = new double[length];
            fill(flat);
            return FromColumnMajorRect(flat, rows, cols);
        }

        NumericBuffer buffer = JgsPacking.Allocate(length);
        fill(buffer.AsSpan());
        GC.KeepAlive(buffer);

        if (rows == 1 && cols == 1)
        {
            double scalar = buffer.AsSpan()[0];
            GC.KeepAlive(buffer);
            buffer.Dispose();
            return JgsValue.Number(scalar);
        }

        JgsValue value = JgsValue.Packed(buffer);
        if (rows > 1)
        {
            // A row vector already has the shape an array has; a column keeps its orientation, and
            // a matrix says both — the same three cases JgsMatrix.FromColumnMajor distinguishes.
            value.Reshape(rows, cols);
        }

        return value;
    }

    /// <summary>The n-by-n identity, column-major.</summary>
    internal static double[] IdentityColumnMajor(int n)
    {
        var identity = new double[(long)n * n];
        for (int i = 0; i < n; i++)
        {
            identity[((long)i * n) + i] = 1;
        }

        return identity;
    }
}
