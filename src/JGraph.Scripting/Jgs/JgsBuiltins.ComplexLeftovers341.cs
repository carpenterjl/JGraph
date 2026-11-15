namespace JGraph.Scripting.Jgs;

/// <summary>
/// The complex and numeric leftovers after ADR 0370 (open item 341, ADR 0377): <c>any</c> and
/// <c>all</c> of a dense complex array, <c>cell2mat</c> of every cell of numbers, text or logicals,
/// and <c>sortrows</c> and <c>unique(..., 'rows')</c> of a flagged array.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// A dense complex array as the real array <c>any</c> and <c>all</c> reduce: 0 where the entry is
    /// zero, 1 where it is not, and <paramref name="nanReadsAs"/> where either part is NaN (R2025b's
    /// <c>any</c> skips such an entry, and its <c>all</c> counts it). Null for anything that is not a
    /// dense complex value.
    /// </summary>
    internal static JgsValue? ComplexTruthPlane(JgsValue value, double nanReadsAs)
    {
        bool complex = value.Type == JgsType.Complex || value.IsPackedComplex
            || (value.Type == JgsType.Array && !value.IsPacked && !value.IsStringArray
                && Array.Exists(value.AsArray, static e => e.Type == JgsType.Complex));
        if (!complex)
        {
            return null;
        }

        // A NaN in either part decides the entry, whatever the other part holds: R2025b's
        // any(complex(NaN, 1)) and any(complex(1, NaN)) are false, all(complex(0, NaN)) true.
        double Truth(System.Numerics.Complex z) =>
            double.IsNaN(z.Real) || double.IsNaN(z.Imaginary) ? nanReadsAs
            : z.Real != 0 || z.Imaginary != 0 ? 1 : 0;

        if (value.Type == JgsType.Complex)
        {
            return JgsValue.Number(Truth(value.AsComplex));
        }

        var plane = new double[value.ArrayLength];
        for (int i = 0; i < plane.Length; i++)
        {
            plane[i] = Truth(value.ElementAt(i).AsComplex);
        }

        return JgsMatrix.FromColumnMajorDims(plane, JgsMatrix.DimsOf(value));
    }

    /// <summary>
    /// Whether <c>cell2mat</c> joins a piece through the bracket in MATLAB code: a number, a logical,
    /// a complex number, text, a numeric, logical, string, time or categorical array, or a plain
    /// struct. An object, a function handle and a cell keep their own roads.
    /// </summary>
    internal static bool JoinsAsCellContents(JgsValue piece) => piece.Type switch
    {
        JgsType.Number or JgsType.Bool or JgsType.Complex or JgsType.String => true,
        JgsType.Array => piece.IsPacked || piece.IsPackedComplex || piece.IsCharMatrix || piece.IsStringArray
            || piece.IsTime || piece.IsCategorical || piece.ArrayLength == 0
            || Array.TrueForAll(piece.AsArray, static e => e.Type is JgsType.Number or JgsType.Bool or JgsType.Complex or JgsType.String),
        JgsType.Struct => piece.ClassName is null,
        _ => false,
    };

    /// <summary>
    /// R2025b's refusals before cell2mat.m joins anything: an argument that is not a cell
    /// (its <c>arguments</c> block) and a cell holding a cell. Null when neither applies.
    /// </summary>
    internal static JgsRuntimeException? CellToMatRefusal(JgsValue argument, int line, int col)
    {
        if (argument.Type == JgsType.Cell)
        {
            return Array.Exists(argument.AsCell, static e => e.Type == JgsType.Cell)
                ? new JgsRuntimeException(line, col, "MATLAB:cell2mat:UnsupportedCellContent",
                    "cell2mat does not support cell arrays containing cell arrays.")
                : null;
        }

        return argument.Type is JgsType.Number or JgsType.Bool or JgsType.Complex or JgsType.String or JgsType.Array
            && !argument.IsStringArray && !argument.IsTime && !argument.IsCategorical
            ? new JgsRuntimeException(line, col, "MATLAB:validation:UnableToConvert",
                "Invalid argument at position 1. Value must be of type cell or be convertible to cell.")
            : null;
    }

    /// <summary>
    /// R2025b's cell2mat.m around <see cref="Interpreter.CellContentsJoined"/>: pieces of one class
    /// are joined and the bracket's own refusal stands; pieces of several classes refuse a logical
    /// beside a char (<c>MATLAB:cell2mat:IncompatibleTypes</c>), and a join that fails is
    /// <c>MATLAB:cell2mat:CatFailed</c>.
    /// </summary>
    internal static JgsValue CellContentsChecked(Interpreter interpreter, JgsValue cell, int line, int col)
    {
        JgsValue[] pieces = cell.AsCell;
        if (pieces.Length < 2)
        {
            return interpreter.CellContentsJoined(cell, line, col);
        }

        string first = ClassOf(pieces[0], JgsDialect.Matlab);
        if (Array.TrueForAll(pieces, p => ClassOf(p, JgsDialect.Matlab) == first))
        {
            return interpreter.CellContentsJoined(cell, line, col);
        }

        if (Array.Exists(pieces, IsLogicalValue) && Array.Exists(pieces, static p => ClassOf(p, JgsDialect.Matlab) == "char"))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:cell2mat:IncompatibleTypes", "Concatenation of logical and char is not supported.");
        }

        try
        {
            return interpreter.CellContentsJoined(cell, line, col);
        }
        catch (JgsRuntimeException)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:cell2mat:CatFailed", "Unable to concatenate cell array contents.");
        }
    }

    /// <summary>
    /// <c>inv</c> of a singular complex matrix that is diagonal or triangular, as R2025b's real road
    /// answers one (ADR 0355) and its complex road too (open item 341, ADR 0377): a diagonal one
    /// inverts element by element (<c>inv(complex(zeros(2)))</c> is <c>[Inf 0; 0 Inf]</c>), a
    /// triangle is Inf over its triangle and 0 elsewhere. Null for any other matrix, which stays
    /// Inf throughout.
    /// </summary>
    internal static JgsValue? SingularComplexInverse(System.Numerics.Complex[,] square)
    {
        int n = square.GetLength(0);
        bool upper = true;
        bool lower = true;
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                if (square[r, c] != System.Numerics.Complex.Zero)
                {
                    upper &= r <= c;
                    lower &= r >= c;
                }
            }
        }

        if (!upper && !lower)
        {
            return null;
        }

        var inverse = new System.Numerics.Complex[n * n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                inverse[r + (c * n)] = upper && lower
                    ? r == c ? DivideMatlab(System.Numerics.Complex.One, square[r, r]) : System.Numerics.Complex.Zero
                    : (upper ? r <= c : r >= c) ? new System.Numerics.Complex(double.PositiveInfinity, 0) : System.Numerics.Complex.Zero;
            }
        }

        return Array.TrueForAll(inverse, static z => z.Imaginary == 0)
            ? FromColumnMajorRect(Array.ConvertAll(inverse, static z => z.Real), n, n)
            : ComplexStorage(inverse, [n, n]);
    }

    /// <summary>
    /// <c>sortrows</c> of a flagged or complex array: its rows in the complex order, column by column
    /// (magnitude, then angle in (-pi, pi], NaN last ascending and first descending), a flagged array
    /// answered real as R2025b answers it, the index the verb's (open item 341, ADR 0377).
    /// <c>'ComparisonMethod', 'real'</c> of a flagged array is the verb's real order; <c>'abs'</c> and
    /// <c>'auto'</c> are the complex one. The verb is handed each column as two, its magnitude and its
    /// angle, with the columns and directions named turned to match, so its stable order is the
    /// complex one. Null for a form this does not answer (a complex array under <c>'real'</c>).
    /// </summary>
    internal static JgsValue[]? SortRowsOfComplex(BuiltinFunction sortRows, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        bool flagged = IsZeroImagFlagged(args[0]);
        JgsValue subject = flagged ? RealPlaneOfZeroImag(args[0]) : args[0];
        var rest = new List<JgsValue>(args.Skip(1));
        string method = "auto";
        for (int i = 0; i + 1 < rest.Count; i++)
        {
            if (IsTextScalar(rest[i]) && string.Equals(TextOf(rest[i]), "ComparisonMethod", StringComparison.OrdinalIgnoreCase)
                && IsTextScalar(rest[i + 1]))
            {
                method = TextOf(rest[i + 1]).ToLowerInvariant();
                rest.RemoveRange(i, 2);
                break;
            }
        }

        if (flagged && (method == "real" || subject.Type != JgsType.Array || subject.IsNd))
        {
            return sortRows.CallMultiple([subject, .. rest], Math.Max(wanted, 1), line, col);
        }

        if (!flagged && (method == "real" || subject.Type != JgsType.Array || subject.IsNd))
        {
            return null;
        }

        int rows = JgsMatrix.RowCount(subject);
        int cols = JgsMatrix.ColCount(subject);
        System.Numerics.Complex[] values;
        if (flagged)
        {
            values = Array.ConvertAll(FlattenColumnMajor("sortrows", subject, line, col), static v => new System.Numerics.Complex(v, 0));
        }
        else
        {
            System.Numerics.Complex[,] grid = ComplexRectOf("sortrows", subject, line, col);
            values = new System.Numerics.Complex[rows * cols];
            for (int c = 0; c < cols; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    values[r + (c * rows)] = grid[r, c];
                }
            }
        }
        var keys = new double[rows * cols * 2];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                System.Numerics.Complex z = values[r + (c * rows)];
                bool nan = double.IsNaN(z.Real) || double.IsNaN(z.Imaginary);
                keys[r + (2 * c * rows)] = nan ? double.NaN : System.Numerics.Complex.Abs(z);
                keys[r + (((2 * c) + 1) * rows)] = nan ? 0 : Math.Atan2(z.Imaginary + 0.0, z.Real + 0.0); // -0 is 0
            }
        }

        // A column named k is the two key columns 2k-1 and 2k, a negative one both descending; a
        // cell of directions names each of them twice.
        for (int i = 0; i < rest.Count; i++)
        {
            JgsValue spec = rest[i];
            if (i == 0 && !IsTextScalar(spec) && spec.Type != JgsType.Cell && !IsEmpty(spec))
            {
                double[] named = ToDoubles("sortrows", spec, line, col);
                var doubled = new double[named.Length * 2];
                for (int k = 0; k < named.Length; k++)
                {
                    double sign = named[k] < 0 ? -1 : 1;
                    doubled[2 * k] = sign * ((2 * Math.Abs(named[k])) - 1);
                    doubled[(2 * k) + 1] = sign * 2 * Math.Abs(named[k]);
                }

                rest[i] = JgsMatrix.FromColumnMajor(doubled, 1, doubled.Length);
            }
            else if (spec.Type == JgsType.Cell)
            {
                rest[i] = JgsValue.Cell([.. spec.AsCell.SelectMany(static w => new[] { w, w })]);
            }
        }

        JgsValue keyMatrix = JgsMatrix.FromColumnMajorDims(keys, [rows, cols * 2]);
        JgsValue[] keyed = sortRows.CallMultiple([keyMatrix, .. rest], 2, line, col);
        double[] order = FlattenColumnMajor("sortrows", keyed[1], line, col);
        var sorted = new System.Numerics.Complex[values.Length];
        for (int r = 0; r < rows; r++)
        {
            int from = (int)order[r] - 1;
            for (int c = 0; c < cols; c++)
            {
                sorted[r + (c * rows)] = values[from + (c * rows)];
            }
        }

        JgsValue sortedValue = flagged
            ? JgsMatrix.FromColumnMajorDims(Array.ConvertAll(sorted, static z => z.Real), [rows, cols])
            : ComplexStorage(sorted, [rows, cols]);
        sortedValue.SetNumericClass(subject.NumericClass);
        JgsValue[] answers = [sortedValue, keyed[1]];
        return answers[..Math.Max(wanted, 1)];
    }
}
