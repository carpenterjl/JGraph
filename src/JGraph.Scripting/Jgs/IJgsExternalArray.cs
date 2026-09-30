namespace JGraph.Scripting.Jgs;

/// <summary>
/// An external value that is a two-dimensional array of values of one class, as a MATLAB value
/// class's object array is (device classes plan, stage D10b, ADR 0194): <c>midimsg</c>. It indexes,
/// grows, shrinks and joins as an array, and a dot on it is a comma-separated list over its elements.
/// It is never changed in place, so every name for it may hold the one instance.
/// </summary>
/// <remarks>
/// The interpreter indexes one through a proxy (<see cref="ExternalArrays"/>): a double array of the
/// same shape holding each element's position. The subscripts, <c>end</c>, masks, deletion and
/// growth all run on the proxy through the ordinary numeric code, and the positions it comes back
/// with say which elements the answer holds, so none of that logic is written twice.
/// </remarks>
internal interface IJgsExternalArray : IJgsOwnDisp
{
    int Rows { get; }

    int Columns { get; }

    /// <summary>
    /// A new array of this class, <paramref name="rows"/> by <paramref name="columns"/>, whose
    /// element k (column-major) is <c>elements[k]</c>'s element, or the class's default element where
    /// that is null.
    /// </summary>
    IJgsExternalArray Build(IReadOnlyList<(IJgsExternalArray Source, int Index)?> elements, int rows, int columns);

    /// <summary>
    /// A value of another class made into this class where MATLAB would convert it: to join it to an
    /// array of this class, or to store it into one. Throws the class's own refusal when it cannot.
    /// </summary>
    IJgsExternalArray Coerce(JgsValue value, int line, int col);

    /// <summary><c>a.name</c> on element <paramref name="index"/>.</summary>
    JgsValue GetMember(int index, string name, int line, int col);

    /// <summary><c>a(k).name = value</c>: a new array, element <paramref name="index"/> written; this one is left as it was.</summary>
    IJgsExternalArray WithMember(int index, string name, JgsValue value, int line, int col);

    /// <summary>Whether two arrays of this class hold the same elements (<c>isequal</c>).</summary>
    bool SameAs(IJgsExternalArray other);
}

/// <summary>
/// An external value with a disp of its own, as a class with a (hidden) disp method has: <c>disp</c>
/// prints it, and the echo of a name prints "name =" and then it, as it does for a classdef object
/// that defines disp (M68).
/// </summary>
internal interface IJgsOwnDisp : IJgsExternal
{
    /// <summary>What <c>disp</c> shows, without the final newline.</summary>
    string Disp();
}

/// <summary>The array operations every <see cref="IJgsExternalArray"/> shares.</summary>
internal static class ExternalArrays
{
    public static int Count(IJgsExternalArray array) => array.Rows * array.Columns;

    /// <summary>The proxy for an array: its shape, holding <paramref name="offset"/>+1, +2, … column-major.</summary>
    public static JgsValue Proxy(int rows, int columns, int offset = 0)
    {
        double[] positions = new double[rows * columns];
        for (int i = 0; i < positions.Length; i++)
        {
            positions[i] = offset + i + 1;
        }

        return JgsMatrix.FromColumnMajor(positions, rows, columns);
    }

    /// <summary>Reads a proxy back: its shape and the positions it holds (0 for a filled gap).</summary>
    public static (int[] Positions, int Rows, int Columns) ReadProxy(JgsValue proxy, int line, int col)
    {
        switch (proxy.Type)
        {
            case JgsType.Number or JgsType.Bool:
                return ([(int)proxy.AsNumber], 1, 1);
            case JgsType.Array when proxy.IsNd && proxy.Dims.Skip(2).Any(static d => d != 1):
                throw new JgsRuntimeException(line, col, "JGraph:externalArray:NDims",
                    "An object array of more than two dimensions is not supported in JGraph.");
            case JgsType.Array:
            {
                double[] values = JgsBuiltins.ToDoubles("index", proxy, line, col);
                int rows = JgsMatrix.RowCount(proxy);
                int columns = JgsMatrix.ColCount(proxy);
                if (rows * columns != values.Length)
                {
                    (rows, columns) = values.Length == 0 ? (0, 0) : (1, values.Length);
                }
                return (Array.ConvertAll(values, static v => (int)v), rows, columns);
            }

            default:
                throw new JgsRuntimeException(line, col, "JGraph:externalArray:Proxy", "The index did not select elements of the array.");
        }
    }

    /// <summary>The elements a read of a proxy picked, as an array of the class.</summary>
    public static IJgsExternalArray Picked(IJgsExternalArray source, JgsValue proxy, int line, int col)
    {
        (int[] positions, int rows, int columns) = ReadProxy(proxy, line, col);
        var elements = new (IJgsExternalArray, int)?[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            elements[i] = (source, positions[i] - 1);
        }

        return source.Build(elements, rows, columns);
    }

    /// <summary>
    /// The array a write of a proxy left: positions up to <c>left</c>'s count are its elements, the
    /// ones after are <paramref name="right"/>'s, and 0 is a gap the growth filled.
    /// </summary>
    public static IJgsExternalArray Written(IJgsExternalArray left, IJgsExternalArray? right, JgsValue proxy, int line, int col)
    {
        (int[] positions, int rows, int columns) = ReadProxy(proxy, line, col);
        int count = Count(left);
        var elements = new (IJgsExternalArray, int)?[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            int p = positions[i];
            elements[i] = p <= 0 ? null : p <= count ? (left, p - 1) : (right!, p - count - 1);
        }

        return left.Build(elements, rows, columns);
    }

    /// <summary>
    /// Joins the rows of a bracket, each a list of pieces, where one piece at least is an array of
    /// <paramref name="kind"/>'s class: an empty double is left out, anything else is made into the
    /// class, and the pieces of a row must agree in rows, the rows in columns.
    /// </summary>
    public static IJgsExternalArray Join(IJgsExternalArray kind, IReadOnlyList<JgsValue[]> rows, int line, int col)
    {
        var joinedRows = new List<IJgsExternalArray>();
        foreach (JgsValue[] row in rows)
        {
            var pieces = new List<IJgsExternalArray>();
            foreach (JgsValue piece in row)
            {
                if (IsEmptyDouble(piece))
                {
                    continue;
                }

                pieces.Add(piece.AsExternalOrNull() is IJgsExternalArray same && same.ClassName == kind.ClassName
                    ? same : kind.Coerce(piece, line, col));
            }

            pieces.RemoveAll(static p => Count(p) == 0);
            if (pieces.Count == 0)
            {
                continue;
            }

            int height = pieces[0].Rows;
            if (pieces.Exists(p => p.Rows != height))
            {
                throw Mismatch(line, col);
            }

            var elements = new List<(IJgsExternalArray, int)?>();
            int width = pieces.Sum(static p => p.Columns);
            for (int c = 0; c < width; c++)
            {
                (IJgsExternalArray piece, int column) = Locate(pieces, c);
                for (int r = 0; r < height; r++)
                {
                    elements.Add((piece, (column * height) + r));
                }
            }

            joinedRows.Add(kind.Build(elements, height, width));
        }

        joinedRows.RemoveAll(static r => Count(r) == 0);
        if (joinedRows.Count == 0)
        {
            return kind.Build([], 0, 0);
        }

        int across = joinedRows[0].Columns;
        if (joinedRows.Exists(r => r.Columns != across))
        {
            throw Mismatch(line, col);
        }

        int down = joinedRows.Sum(static r => r.Rows);
        var all = new (IJgsExternalArray, int)?[down * across];
        int top = 0;
        foreach (IJgsExternalArray block in joinedRows)
        {
            for (int c = 0; c < across; c++)
            {
                for (int r = 0; r < block.Rows; r++)
                {
                    all[(c * down) + top + r] = (block, (c * block.Rows) + r);
                }
            }

            top += block.Rows;
        }

        return kind.Build(all, down, across);

        static (IJgsExternalArray Piece, int Column) Locate(List<IJgsExternalArray> pieces, int c)
        {
            foreach (IJgsExternalArray piece in pieces)
            {
                if (c < piece.Columns)
                {
                    return (piece, c);
                }

                c -= piece.Columns;
            }

            throw new InvalidOperationException("column past the joined pieces");
        }
    }

    public static JgsRuntimeException Mismatch(int line, int col) =>
        new(line, col, "MATLAB:catenate:dimensionMismatch", "Dimensions of arrays being concatenated are not consistent.");

    /// <summary>Whether a value is the empty double a bracket or an assignment leaves out: <c>[]</c>.</summary>
    public static bool IsEmptyDouble(JgsValue value) =>
        value.Type == JgsType.Null
        || (value.Type == JgsType.Array && value.ArrayLength == 0 && !value.IsStringArray && value.NumericClass == JgsNumericClass.Double);

    /// <summary>Element <paramref name="index"/> as a 1-by-1 array.</summary>
    public static IJgsExternalArray Element(IJgsExternalArray array, int index) => array.Build([(array, index)], 1, 1);

    /// <summary>The array as a cell of the same shape holding its elements one by one (what arrayfun walks).</summary>
    public static JgsValue ToCell(IJgsExternalArray array)
    {
        int count = Count(array);
        var cells = new JgsValue[count];
        for (int i = 0; i < count; i++)
        {
            cells[i] = JgsValue.External(Element(array, i));
        }

        JgsValue cell = JgsValue.Cell(cells);
        cell.Reshape(array.Rows, array.Columns);
        return cell;
    }

    /// <summary>The array with its rows and columns swapped.</summary>
    public static IJgsExternalArray Transposed(IJgsExternalArray array)
    {
        int rows = array.Rows;
        int columns = array.Columns;
        var elements = new (IJgsExternalArray, int)?[rows * columns];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                elements[(r * columns) + c] = (array, (c * rows) + r);
            }
        }

        return array.Build(elements, columns, rows);
    }

    /// <summary>The same elements in column-major order, <paramref name="rows"/> by <paramref name="columns"/>.</summary>
    public static IJgsExternalArray Reshaped(IJgsExternalArray array, int rows, int columns)
    {
        var elements = new (IJgsExternalArray, int)?[rows * columns];
        for (int i = 0; i < elements.Length; i++)
        {
            elements[i] = (array, i);
        }

        return array.Build(elements, rows, columns);
    }

    /// <summary>The array tiled <paramref name="down"/> by <paramref name="across"/> times.</summary>
    public static IJgsExternalArray Tiled(IJgsExternalArray array, int down, int across)
    {
        int rows = array.Rows * down;
        int columns = array.Columns * across;
        var elements = new (IJgsExternalArray, int)?[rows * columns];
        for (int c = 0; c < columns; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                elements[(c * rows) + r] = (array, ((c % array.Columns) * array.Rows) + (r % array.Rows));
            }
        }

        return array.Build(elements, rows, columns);
    }
}
