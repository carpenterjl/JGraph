namespace JGraph.Scripting.Jgs;

using JGraph.Numerics;

/// <summary>
/// The one place that knows how a matrix is laid out. A matrix is an array value carrying a
/// <see cref="JgsValue.Rows"/>-by-<see cref="JgsValue.Cols"/> shape over flat column-major storage
/// (ADR 0043), which is what makes <c>A(i, j)</c>, linear <c>A(k)</c> and <c>A(:)</c> agree with
/// MATLAB.
/// </summary>
/// <remarks>
/// Values written before the shape existed — and values a MAT-file load or a workspace restore can
/// still produce — are an array whose elements are row arrays. Every reader here accepts both, so
/// the builtins that ask for <see cref="ToRows"/> did not have to change when construction moved to
/// the shaped form.
/// </remarks>
internal static class JgsMatrix
{
    /// <summary>Whether a value is a matrix: shaped with more than one row, or nested row arrays.</summary>
    public static bool IsMatrix(JgsValue value) =>
        value.Type == JgsType.Array
        && (value.IsShaped || IsNested(value));

    /// <summary>Whether a value is the pre-shape representation: an array whose first element is an array.</summary>
    public static bool IsNested(JgsValue value) =>
        value.Type == JgsType.Array && !value.IsPacked && !value.IsPackedComplex
        && value.ArrayLength > 0 && value.ElementAt(0).Type == JgsType.Array;

    /// <summary>The row count of any array value: a plain vector is one row.</summary>
    public static int RowCount(JgsValue value) => IsNested(value) ? value.ArrayLength : value.Rows;

    /// <summary>The column count of any array value.</summary>
    public static int ColCount(JgsValue value) =>
        IsNested(value) ? value.ElementAt(0).ArrayLength : value.Cols;

    /// <summary>Element <c>(row, col)</c> of any array value, whichever representation it uses.</summary>
    public static JgsValue At(JgsValue value, int row, int col) =>
        IsNested(value) ? value.ElementAt(row).ElementAt(col) : value.ElementAt(row + (col * value.Rows));

    /// <summary>
    /// A numeric array or matrix as rectangular rows of doubles; a vector becomes a single row.
    /// This is the shape-agnostic reader the builtins call.
    /// </summary>
    public static double[][] ToRows(string name, JgsValue value, int line, int col)
    {
        if (IsNested(value))
        {
            return NestedToRows(name, value, line, col);
        }

        int rows = value.Rows;
        int cols = value.Cols;
        var result = new double[rows][];
        for (int r = 0; r < rows; r++)
        {
            result[r] = new double[cols];
        }

        for (int c = 0; c < cols; c++)
        {
            int origin = c * rows;
            for (int r = 0; r < rows; r++)
            {
                JgsValue element = value.ElementAt(origin + r);
                if (element.Type is not (JgsType.Number or JgsType.Bool))
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name} needs numbers, but element ({r}, {c}) was a {element.TypeName}.");
                }

                result[r][c] = element.AsNumber;
            }
        }

        return result;
    }

    private static double[][] NestedToRows(string name, JgsValue value, int line, int col)
    {
        var rows = new double[value.ArrayLength][];
        for (int r = 0; r < rows.Length; r++)
        {
            JgsValue row = value.ElementAt(r);
            if (row.Type != JgsType.Array)
            {
                throw new JgsRuntimeException(line, col, $"{name}: matrix row {r} is a {row.TypeName}, not an array.");
            }

            rows[r] = RowOf(name, row, line, col);
            if (rows[r].Length != rows[0].Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: matrix rows must all be the same length (row 0 has {rows[0].Length}, row {r} has {rows[r].Length}).");
            }
        }

        return rows;
    }

    private static double[] RowOf(string name, JgsValue value, int line, int col)
    {
        int length = value.ArrayLength;
        var row = new double[length];
        for (int i = 0; i < length; i++)
        {
            JgsValue element = value.ElementAt(i);
            if (element.Type is not (JgsType.Number or JgsType.Bool))
            {
                throw new JgsRuntimeException(line, col,
                    $"{name} needs numbers, but an element was a {element.TypeName}.");
            }

            row[i] = element.AsNumber;
        }

        return row;
    }

    /// <summary>
    /// Wraps rectangular rows as a matrix value. A single row stays a 1-by-n vector, which is what it
    /// already was, so the collapse callers relied on before the shape existed still happens.
    /// </summary>
    public static JgsValue FromRows(double[][] rows)
    {
        int height = rows.Length;
        int width = height == 0 ? 0 : rows[0].Length;
        var flat = new double[height * width];
        for (int c = 0; c < width; c++)
        {
            int origin = c * height;
            for (int r = 0; r < height; r++)
            {
                flat[origin + r] = rows[r][c];
            }
        }

        return FromColumnMajor(flat, height, width);
    }

    /// <summary>Builds a rows-by-cols matrix from a per-cell function; a 1-by-1 result is a scalar.</summary>
    public static JgsValue Build(int rows, int cols, Func<int, int, double> cell)
    {
        rows = System.Math.Max(rows, 0);
        cols = System.Math.Max(cols, 0);
        if (rows == 1 && cols == 1)
        {
            return JgsValue.Number(cell(0, 0));
        }

        var flat = new double[rows * cols];
        for (int c = 0; c < cols; c++)
        {
            int origin = c * rows;
            for (int r = 0; r < rows; r++)
            {
                flat[origin + r] = cell(r, c);
            }
        }

        return FromColumnMajor(flat, rows, cols);
    }

    /// <summary>
    /// Builds a rows-by-cols matrix from a per-cell function that may return any value — complex,
    /// logical, a string. Homogeneous results still pack; anything mixed stays boxed.
    /// </summary>
    public static JgsValue BuildValues(int rows, int cols, Func<int, int, JgsValue> cell)
    {
        rows = System.Math.Max(rows, 0);
        cols = System.Math.Max(cols, 0);
        if (rows == 1 && cols == 1)
        {
            return cell(0, 0);
        }

        var elements = new JgsValue[rows * cols];
        for (int c = 0; c < cols; c++)
        {
            int origin = c * rows;
            for (int r = 0; r < rows; r++)
            {
                elements[origin + r] = cell(r, c);
            }
        }

        return FromElements(elements, rows, cols);
    }

    /// <summary>
    /// Gives a freshly built result the shape of the value it was computed from. Shape lives on the
    /// wrapper, so every helper that mints a new array has to carry it across or an elementwise map
    /// silently flattens the matrix it was given. N-D shapes ride along too — a shape can be worth
    /// keeping even when the first dimension is 1 (a 1-by-1-by-4, say), which is why this checks
    /// <see cref="JgsValue.IsNd"/> and not just <see cref="JgsValue.IsShaped"/>.
    /// </summary>
    public static JgsValue Like(JgsValue source, JgsValue result)
    {
        if ((source.IsShaped || source.IsNd) && source.ArrayLength == result.ArrayLength)
        {
            result.TakeShapeOf(source);
        }

        return result;
    }

    /// <summary>The size of any array value per dimension; the pre-shape nested form reads as 2-D.</summary>
    public static int[] DimsOf(JgsValue value) =>
        IsNested(value) ? [value.ArrayLength, value.ElementAt(0).ArrayLength] : value.Dims;

    /// <summary>Adopts an already-column-major buffer as an array with the given dimensions.</summary>
    public static JgsValue FromColumnMajorDims(double[] flat, IReadOnlyList<int> dims)
    {
        JgsValue value = Adopt(flat);
        value.ReshapeDims(dims);
        return value;
    }

    /// <summary>Packs freshly built column-major elements if they are homogeneous, then shapes them.</summary>
    public static JgsValue FromElements(JgsValue[] columnMajor, int rows, int cols)
    {
        if (JgsPacking.Enabled && PackedOps.TryPackElements(columnMajor, out JgsValue packed))
        {
            packed.Reshape(rows, cols);
            return packed;
        }

        return rows == 1 ? JgsValue.Array(columnMajor) : JgsValue.Shaped(columnMajor, rows, cols);
    }

    /// <summary>Adopts an already-column-major buffer as a rows-by-cols matrix value.</summary>
    public static JgsValue FromColumnMajor(double[] flat, int rows, int cols)
    {
        if (rows == 1 || cols == 1 || flat.Length == 0)
        {
            // A vector needs no shape beyond the one an array already has, except that a column
            // vector's orientation is exactly the thing worth keeping.
            JgsValue vector = Adopt(flat);
            if (cols == 1 && rows > 1)
            {
                vector.Reshape(rows, 1);
            }

            return vector;
        }

        JgsValue value = Adopt(flat);
        value.Reshape(rows, cols);
        return value;
    }

    private static JgsValue Adopt(double[] values)
    {
        if (JgsPacking.Enabled)
        {
            return JgsValue.Packed(ManagedBuffer.Adopt(values));
        }

        var boxed = new JgsValue[values.Length];
        for (int i = 0; i < boxed.Length; i++)
        {
            boxed[i] = JgsValue.Number(values[i]);
        }

        return JgsValue.Array(boxed);
    }

    /// <summary>
    /// Rewrites a nested array-of-rows value into the shaped form. Used at the boundaries that can
    /// still hand one in — MAT-file load, workspace restore, a script that built rows by hand.
    /// </summary>
    public static JgsValue Normalize(string name, JgsValue value, int line, int col) =>
        IsNested(value) ? FromRows(NestedToRows(name, value, line, col)) : value;
}
