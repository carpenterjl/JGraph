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

        // A packed array is already doubles, and every one of them reads back as a number, so the
        // type check below has nothing to find. Asking anyway cost a JgsValue per element — four
        // million of them for a 2048-square image, which was most of what conv2 spent (M96b).
        if (value.IsPacked && value.PackedKind is JgsPackedKind.Number or JgsPackedKind.Bool)
        {
            Span<double> flat = value.AsBuffer.AsSpan();
            for (int c = 0; c < cols; c++)
            {
                int origin = c * rows;
                for (int r = 0; r < rows; r++)
                {
                    result[r][c] = flat[origin + r];
                }
            }

            GC.KeepAlive(value);
            return result;
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

    /// <summary>
    /// The dimension a reduction walks when the script did not name one: MATLAB's first non-singleton.
    /// That single rule is what makes <c>max(A)</c> per-column for a matrix and a scalar for a row
    /// vector, without either being a special case.
    /// </summary>
    public static int DefaultDim(IReadOnlyList<int> dims)
    {
        for (int i = 0; i < dims.Count; i++)
        {
            if (dims[i] != 1)
            {
                return i + 1;
            }
        }

        return 1;
    }

    /// <summary>
    /// The one-dimensional slices of column-major storage along <paramref name="dim"/> (1-based),
    /// together with the shape a per-slice reduction takes — the same dimensions with the reduced one
    /// collapsed to 1.
    /// </summary>
    /// <remarks>
    /// A slice steps by the product of the dimensions below <paramref name="dim"/>, and the slices
    /// themselves come out in the order the reduced array stores them. That one rule covers a row
    /// vector, a column, a matrix and an N-D array alike, which is why the reductions no longer need a
    /// branch per shape. A dimension past the last is a singleton, so each element is its own slice
    /// and the reduction changes nothing — which is exactly what MATLAB does with <c>max(A, [], 5)</c>.
    /// </remarks>
    public static (double[][] Slices, int[] ReducedDims) SlicesAlong(
        double[] columnMajor, IReadOnlyList<int> dims, int dim)
    {
        int inner = 1;
        for (int i = 0; i < dim - 1 && i < dims.Count; i++)
        {
            inner *= dims[i];
        }

        int length = dim <= dims.Count ? dims[dim - 1] : 1;
        int outer = 1;
        for (int i = dim; i < dims.Count; i++)
        {
            outer *= dims[i];
        }

        var slices = new double[inner * outer][];
        for (int o = 0; o < outer; o++)
        {
            int page = o * inner * length;
            for (int i = 0; i < inner; i++)
            {
                var slice = new double[length];
                for (int j = 0; j < length; j++)
                {
                    slice[j] = columnMajor[page + i + (j * inner)];
                }

                slices[(o * inner) + i] = slice;
            }
        }

        return (slices, ShapeAlong(dims, dim, 1));
    }

    /// <summary>
    /// The inverse of <see cref="SlicesAlong"/>: writes one vector per slice back along
    /// <paramref name="dim"/>, and reports the shape that makes. Every slice must be the same length,
    /// which is the length the reduced dimension takes — the same as the original for
    /// <c>cumsum</c> and <c>sort</c>, one shorter for <c>diff</c>, and 1 for a reduction that yields
    /// a single value per slice.
    /// </summary>
    public static (double[] ColumnMajor, int[] Dims) JoinAlong(
        double[][] slices, IReadOnlyList<int> dims, int dim)
    {
        int length = slices.Length == 0 ? 0 : slices[0].Length;
        int inner = 1;
        for (int i = 0; i < dim - 1 && i < dims.Count; i++)
        {
            inner *= dims[i];
        }

        int outer = inner == 0 ? 0 : slices.Length / System.Math.Max(inner, 1);
        var joined = new double[inner * outer * length];
        for (int o = 0; o < outer; o++)
        {
            int page = o * inner * length;
            for (int i = 0; i < inner; i++)
            {
                double[] slice = slices[(o * inner) + i];
                for (int j = 0; j < length; j++)
                {
                    joined[page + i + (j * inner)] = slice[j];
                }
            }
        }

        return (joined, ShapeAlong(dims, dim, length));
    }

    /// <summary>The given dimensions with the <paramref name="dim"/>th one set to a new length.</summary>
    internal static int[] ShapeAlong(IReadOnlyList<int> dims, int dim, int length)
    {
        var shape = new int[System.Math.Max(dims.Count, dim)];
        for (int i = 0; i < shape.Length; i++)
        {
            shape[i] = i < dims.Count ? dims[i] : 1;
        }

        shape[dim - 1] = length;
        return shape;
    }

    /// <summary>Adopts an already-column-major buffer as an array with the given dimensions.</summary>
    public static JgsValue FromColumnMajorDims(double[] flat, IReadOnlyList<int> dims)
    {
        JgsValue value = Adopt(flat);
        value.ReshapeDims(dims);
        return value;
    }

    /// <summary>
    /// Packs freshly built column-major elements if they are homogeneous, then shapes them to any
    /// number of dimensions. A single element is a scalar, not a one-element array — which is what
    /// keeps <c>sum</c> of a vector a number.
    /// </summary>
    public static JgsValue FromElementsDims(JgsValue[] columnMajor, IReadOnlyList<int> dims)
    {
        if (columnMajor.Length == 1)
        {
            return columnMajor[0];
        }

        JgsValue value = JgsPacking.Enabled && PackedOps.TryPackElements(columnMajor, out JgsValue packed)
            ? packed
            : JgsValue.Array(columnMajor);
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
        // An empty result keeps the shape it was computed at (M96b). It used to fall in with the
        // vectors below and come back a bare 1-by-0 row, which made zeros(0, 0) \ zeros(0, 1) a
        // 1-by-0 where MATLAB says 0-by-1, and inv([]) a row where MATLAB says 0-by-0.
        if (flat.Length == 0 && (long)rows * cols == 0)
        {
            JgsValue empty = Adopt(flat);
            empty.Reshape(rows, cols);
            return empty;
        }

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
