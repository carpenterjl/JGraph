using System.Numerics;
using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// R2025b's concatenation of numbers, logicals and text, in every class and any number of
/// dimensions (open item 346, ADR 0386): the bracket, <c>horzcat</c>, <c>vertcat</c> and
/// <c>cat(dim, ...)</c> in MATLAB code.
/// </summary>
/// <remarks>
/// <para>
/// Measured (lane 8C's probe_cat and probe_cat2: 16 sample values, every pair under the bracket's
/// two directions and <c>cat(3, ...)</c>, and the N-D and empty shapes). The class of the join:
/// char when any piece is char, and then a logical piece is <c>MATLAB:UnableToConvert</c>
/// ("Conversion to char from logical is not possible.") and a complex piece, a zero-imaginary flagged
/// one included, the same identifier ("Complex values cannot be converted to chars"); else the first
/// integer class in reading order, empties included (<c>[int8(1) int16(300)]</c> is an int8 127);
/// else single when any piece is; else logical when every piece is, a <c>[]</c> included
/// (<c>[[] true]</c> is a double); else double. A number becomes a char by truncation, clamped to 0
/// and 65535, NaN as 0. The join is complex when a piece has an imaginary part, or when a flagged
/// piece's class is the join's class (<c>[complex(4, 0) true]</c> keeps the flag,
/// <c>[complex(4, 0) single(1)]</c> drops it, <c>[complex(single(4), 0) 2.5]</c> keeps it).
/// </para>
/// <para>
/// The shape: a join of 2-D pieces along the first two dimensions leaves out every empty piece (and
/// an all-empty join takes <see cref="JgsEmpty"/>'s shape); a join with an N-D piece, or along a
/// third or later dimension, leaves out only a 0-by-0 piece (unless every piece is one:
/// <c>cat(3, [], [])</c> is 0-by-0-by-2), and every other piece must agree in every dimension but
/// the joined one (<c>MATLAB:catenate:dimensionMismatch</c>), so <c>[ones(2,2,2), ones(2,1,2)]</c>
/// is 2-by-3-by-2 and <c>[ones(2,2,2), zeros(1,0)]</c> refuses.
/// </para>
/// <para>
/// The bracket asks this engine only for a join it did not already answer: an N-D piece, pieces of
/// more than one kind (class, logical, char, complex), or stacked char rows with an empty among them.
/// A join of a complex piece into an integer class is left to the old road (no complex integers here).
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    private enum MatlabCatKind : byte
    {
        Numeric,
        Logical,
        Char,
    }

    /// <summary>One piece of a join, read out column-major.</summary>
    private sealed class MatlabCatPiece
    {
        public MatlabCatKind Kind;
        public JgsNumericClass Class;
        public int[] Dims = [0, 0];
        public double[] Re = [];

        /// <summary>The imaginary parts, or null for a piece with no complex storage.</summary>
        public double[]? Im;

        /// <summary>Whether some imaginary part is non-zero.</summary>
        public bool HasImaginary;

        public bool Flagged => Im is not null && !HasImaginary;

        public bool ZeroByZero => Dims.Length == 2 && Dims[0] == 0 && Dims[1] == 0;
    }

    /// <summary>What a piece is, without reading its elements: false for anything but a number, logical or char array.</summary>
    private static bool TryMatlabCatSignature(JgsValue v, out MatlabCatKind kind, out JgsNumericClass numericClass,
        out byte complexity, out bool nd, out bool empty)
    {
        kind = MatlabCatKind.Numeric;
        numericClass = v.NumericClass;
        complexity = 0; // 0 real, 1 flagged, 2 an imaginary part
        nd = false;
        empty = false;
        if (v.IsTime || v.IsCategorical || v.IsStringArray)
        {
            return false;
        }

        switch (v.Type)
        {
            case JgsType.Number:
                return true;
            case JgsType.Bool:
                kind = MatlabCatKind.Logical;
                return true;
            case JgsType.Complex:
                complexity = v.AsComplex.Imaginary != 0 ? (byte)2 : (byte)1;
                return true;
            case JgsType.String:
                kind = MatlabCatKind.Char;
                empty = v.AsString.Length == 0;
                return true;
            case JgsType.Array:
                break;
            default:
                return false;
        }

        nd = v.IsNd;
        empty = v.ArrayLength == 0;
        if (v.IsCharMatrix)
        {
            kind = MatlabCatKind.Char;
            return true;
        }

        if (v.IsPackedComplex)
        {
            JgsPackedComplex planes = v.AsPackedComplex;
            complexity = planes.Im.AsSpan(0, v.ArrayLength).ContainsAnyExcept(0.0) ? (byte)2 : planes.PreserveComplex ? (byte)1 : (byte)0;
            GC.KeepAlive(planes);
            return true;
        }

        if (v.IsPacked)
        {
            kind = v.PackedKind == JgsPackedKind.Bool ? MatlabCatKind.Logical : MatlabCatKind.Numeric;
            return true;
        }

        if (JgsMatrix.IsNested(v))
        {
            return false;
        }

        int length = v.ArrayLength;
        bool allBool = length > 0;
        for (int i = 0; i < length; i++)
        {
            JgsValue e = v.ElementAt(i);
            switch (e.Type)
            {
                case JgsType.Number:
                    allBool = false;
                    break;
                case JgsType.Bool:
                    break;
                case JgsType.Complex:
                    allBool = false;
                    complexity = Math.Max(complexity, e.AsComplex.Imaginary != 0 ? (byte)2 : (byte)1);
                    break;
                default:
                    return false;
            }
        }

        kind = allBool || (length == 0 && v.PackedKind == JgsPackedKind.Bool) ? MatlabCatKind.Logical : MatlabCatKind.Numeric;
        return true;
    }

    /// <summary>Whether a value is a number, logical or char array this engine joins.</summary>
    internal static bool IsMatlabCatPlain(JgsValue v) => TryMatlabCatSignature(v, out _, out _, out _, out _, out _);

    /// <summary>A piece's elements, after <see cref="TryMatlabCatSignature"/> accepted it.</summary>
    private static MatlabCatPiece ReadMatlabCatPiece(JgsValue v, MatlabCatKind kind, JgsNumericClass numericClass, byte complexity)
    {
        var piece = new MatlabCatPiece { Kind = kind, Class = kind == MatlabCatKind.Numeric ? numericClass : JgsNumericClass.Double };
        switch (v.Type)
        {
            case JgsType.Number or JgsType.Bool:
                piece.Dims = [1, 1];
                piece.Re = [v.AsNumber];
                return piece;
            case JgsType.Complex:
                piece.Dims = [1, 1];
                piece.Re = [v.AsComplex.Real];
                piece.Im = [v.AsComplex.Imaginary];
                piece.HasImaginary = v.AsComplex.Imaginary != 0;
                return piece;
            case JgsType.String:
            {
                string text = v.AsString;
                piece.Dims = text.Length == 0 ? [0, 0] : [1, text.Length];
                piece.Re = new double[text.Length];
                for (int i = 0; i < text.Length; i++)
                {
                    piece.Re[i] = text[i];
                }

                return piece;
            }
        }

        piece.Dims = SizeDims(v);
        int length = v.ArrayLength;
        if (v.IsPackedComplex)
        {
            JgsPackedComplex planes = v.AsPackedComplex;
            piece.Re = planes.Re.AsSpan(0, length).ToArray();
            piece.Im = planes.Im.AsSpan(0, length).ToArray();
            GC.KeepAlive(planes);
            piece.HasImaginary = complexity == 2;
            if (complexity == 0)
            {
                piece.Im = null;
            }

            return piece;
        }

        if (v.IsPacked)
        {
            NumericBuffer buffer = v.AsBuffer;
            piece.Re = buffer.AsSpan(0, length).ToArray();
            GC.KeepAlive(buffer);
            return piece;
        }

        piece.Re = new double[length];
        if (complexity > 0)
        {
            piece.Im = new double[length];
        }

        for (int i = 0; i < length; i++)
        {
            JgsValue e = v.ElementAt(i);
            switch (e.Type)
            {
                case JgsType.Complex:
                    piece.Re[i] = e.AsComplex.Real;
                    piece.Im![i] = e.AsComplex.Imaginary;
                    piece.HasImaginary |= e.AsComplex.Imaginary != 0;
                    break;
                case JgsType.String:
                    piece.Re[i] = e.AsString.Length > 0 ? e.AsString[0] : 0;
                    break;
                default:
                    piece.Re[i] = e.AsNumber;
                    break;
            }
        }

        return piece;
    }

    /// <summary>
    /// The bracket's join through this engine when its pieces need it (see the type's remarks); false
    /// leaves the bracket to its own roads.
    /// </summary>
    internal static bool TryMatlabBracketJoin(List<JgsValue[]> rows, int line, int col, out JgsValue joined)
    {
        joined = default!;
        bool anyNd = false, anyEmpty = false, allChar = true, mixed = false, first = true;
        (MatlabCatKind Kind, JgsNumericClass Class, byte Complexity) signature = default;
        int count = 0;
        foreach (JgsValue[] row in rows)
        {
            foreach (JgsValue piece in row)
            {
                if (!TryMatlabCatSignature(piece, out MatlabCatKind kind, out JgsNumericClass numericClass, out byte complexity, out bool nd, out bool empty))
                {
                    return false;
                }

                count++;
                anyNd |= nd;
                anyEmpty |= empty;
                allChar &= kind == MatlabCatKind.Char;
                var mine = (kind, kind == MatlabCatKind.Numeric ? numericClass : JgsNumericClass.Double, complexity);
                if (first)
                {
                    signature = mine;
                    first = false;
                }
                else
                {
                    mixed |= mine != signature;
                }
            }
        }

        if (count == 0 || !(anyNd || mixed || (allChar && anyEmpty && rows.Count > 1)))
        {
            return false;
        }

        var grid = new List<MatlabCatPiece[]>(rows.Count);
        foreach (JgsValue[] row in rows)
        {
            var read = new MatlabCatPiece[row.Length];
            for (int i = 0; i < row.Length; i++)
            {
                TryMatlabCatSignature(row[i], out MatlabCatKind kind, out JgsNumericClass numericClass, out byte complexity, out _, out _);
                read[i] = ReadMatlabCatPiece(row[i], kind, numericClass, complexity);
            }

            grid.Add(read);
        }

        if (!TryMatlabCatClass(grid.SelectMany(static r => r), line, col, out MatlabCatResult result))
        {
            return false;
        }

        var joinedRows = new List<MatlabCatPiece>(grid.Count);
        foreach (MatlabCatPiece[] row in grid)
        {
            joinedRows.Add(JoinMatlabCatPieces(ConvertMatlabCatPieces(row, result), 2, anyNd, line, col));
        }

        MatlabCatPiece whole = joinedRows.Count == 1 ? joinedRows[0] : JoinMatlabCatPieces(joinedRows, 1, anyNd, line, col);
        joined = MatlabCatValue(whole, result);
        return true;
    }

    /// <summary>
    /// <c>cat(dim, ...)</c> past the second dimension through this engine, or null when a piece is not
    /// a number, logical or char array (or a complex piece meets an integer class).
    /// </summary>
    internal static JgsValue? TryMatlabCatAlong(int dim, IReadOnlyList<JgsValue> pieces, int line, int col)
    {
        var read = new MatlabCatPiece[pieces.Count];
        for (int i = 0; i < pieces.Count; i++)
        {
            if (!TryMatlabCatSignature(pieces[i], out MatlabCatKind kind, out JgsNumericClass numericClass, out byte complexity, out _, out _))
            {
                return null;
            }

            read[i] = ReadMatlabCatPiece(pieces[i], kind, numericClass, complexity);
        }

        if (read.Length == 0 || !TryMatlabCatClass(read, line, col, out MatlabCatResult result))
        {
            return null;
        }

        return MatlabCatValue(JoinMatlabCatPieces(ConvertMatlabCatPieces(read, result), dim, strict: true, line, col), result);
    }

    /// <summary>
    /// R2025b's refusals of <c>cat</c>'s dimension (lane 8C's probe_msg): a positive whole real number,
    /// not a logical, a char or a complex value, else <c>MATLAB:catenate:invalidDimension</c>; past
    /// the largest dimension count <c>MATLAB:catenate:maxNDims</c>. Null when the dimension is fine.
    /// </summary>
    internal static JgsRuntimeException? MatlabCatDimensionRefusal(JgsValue dim, int line, int col)
    {
        static JgsRuntimeException Invalid(int line, int col) => new(line, col, "MATLAB:catenate:invalidDimension",
            "Dimension argument must be a real, positive, integer scalar.");

        bool numeric = dim.Type == JgsType.Number
            || (dim.Type == JgsType.Array && dim.ArrayLength == 1 && dim.IsPacked && dim.PackedKind == JgsPackedKind.Number && !dim.IsCharMatrix)
            // the boxed lane's [1] is a one-element array holding a number (ChipScalarArgumentTests caught it at the gate)
            || (dim.Type == JgsType.Array && dim.ArrayLength == 1 && !dim.IsPacked && !dim.IsCharMatrix && !HoldsComplexStorage(dim) && dim.ElementAt(0).Type == JgsType.Number);
        if (!numeric || dim.IsTime || dim.IsCategorical)
        {
            return Invalid(line, col);
        }

        double d = dim.Type == JgsType.Number ? dim.AsNumber : dim.ElementAt(0).AsNumber;
        if (double.IsNaN(d) || d < 1 || (double.IsFinite(d) && d != Math.Floor(d)))
        {
            return Invalid(line, col);
        }

        return d > int.MaxValue
            ? new JgsRuntimeException(line, col, "MATLAB:catenate:maxNDims", "DIM input exceeds maximum number of dimensions.")
            : null;
    }

    /// <summary>What a join answers: its kind, its class and whether it is complex (and only flagged).</summary>
    private readonly record struct MatlabCatResult(MatlabCatKind Kind, JgsNumericClass Class, bool Complex, bool FlagOnly);

    /// <summary>The class rule in the type's remarks; false for a complex piece joining an integer class.</summary>
    private static bool TryMatlabCatClass(IEnumerable<MatlabCatPiece> pieces, int line, int col, out MatlabCatResult result)
    {
        MatlabCatPiece[] all = [.. pieces];
        result = default;
        if (Array.Exists(all, static p => p.Kind == MatlabCatKind.Char))
        {
            foreach (MatlabCatPiece p in all)
            {
                if (p.Kind == MatlabCatKind.Logical)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:UnableToConvert",
                        "The following error occurred converting from logical to char:\nConversion to char from logical is not possible.");
                }

                if (p.Im is not null)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:UnableToConvert",
                        $"The following error occurred converting from {p.Class.MatlabName()} to char:\nComplex values cannot be converted to chars");
                }
            }

            result = new MatlabCatResult(MatlabCatKind.Char, JgsNumericClass.Double, false, false);
            return true;
        }

        JgsNumericClass numericClass = JgsNumericClass.Double;
        bool integer = false, single = false, allLogical = true;
        foreach (MatlabCatPiece p in all)
        {
            allLogical &= p.Kind == MatlabCatKind.Logical;
            if (p.Kind != MatlabCatKind.Numeric)
            {
                continue;
            }

            if (!integer && p.Class.IsInteger())
            {
                integer = true;
                numericClass = p.Class;
            }

            single |= p.Class == JgsNumericClass.Single;
        }

        if (!integer && single)
        {
            numericClass = JgsNumericClass.Single;
        }

        bool imaginary = Array.Exists(all, static p => p.HasImaginary);
        bool flagKept = Array.Exists(all, p => p.Flagged && p.Class == numericClass);
        if (integer && imaginary)
        {
            return false;
        }

        result = allLogical
            ? new MatlabCatResult(MatlabCatKind.Logical, JgsNumericClass.Double, false, false)
            : new MatlabCatResult(MatlabCatKind.Numeric, numericClass, imaginary || flagKept, !imaginary && flagKept);
        return true;
    }

    /// <summary>Each piece's elements in the join's class (and with imaginary parts when the join is complex).</summary>
    private static List<MatlabCatPiece> ConvertMatlabCatPieces(IEnumerable<MatlabCatPiece> pieces, MatlabCatResult result)
    {
        var converted = new List<MatlabCatPiece>();
        foreach (MatlabCatPiece p in pieces)
        {
            var c = new MatlabCatPiece { Kind = result.Kind, Class = result.Class, Dims = p.Dims, HasImaginary = p.HasImaginary };
            c.Re = new double[p.Re.Length];
            for (int i = 0; i < c.Re.Length; i++)
            {
                c.Re[i] = MatlabCatConvert(p.Re[i], result, p.Kind == MatlabCatKind.Char);
            }

            if (result.Complex)
            {
                c.Im = new double[p.Re.Length];
                if (p.Im is not null)
                {
                    for (int i = 0; i < c.Im.Length; i++)
                    {
                        c.Im[i] = MatlabCatConvert(p.Im[i], result, false);
                    }
                }
            }

            converted.Add(c);
        }

        return converted;
    }

    private static double MatlabCatConvert(double x, MatlabCatResult result, bool alreadyChar)
    {
        if (result.Kind == MatlabCatKind.Char)
        {
            return alreadyChar ? x : double.IsNaN(x) ? 0 : Math.Clamp(Math.Truncate(x), 0, char.MaxValue);
        }

        return result.Kind == MatlabCatKind.Logical ? x : JgsNumericClasses.Convert(x, result.Class);
    }

    /// <summary>Joins converted pieces along <paramref name="dim"/> by the shape rules in the type's remarks.</summary>
    private static MatlabCatPiece JoinMatlabCatPieces(List<MatlabCatPiece> pieces, int dim, bool strict, int line, int col)
    {
        static JgsRuntimeException Mismatch(int line, int col) => new(line, col, "MATLAB:catenate:dimensionMismatch",
            "Dimensions of arrays being concatenated are not consistent.");

        strict |= dim > 2 || pieces.Exists(static p => p.Dims.Length > 2);
        List<MatlabCatPiece> kept;
        if (strict)
        {
            kept = pieces.FindAll(static p => !p.ZeroByZero);
            if (kept.Count == 0)
            {
                kept = pieces;
            }
        }
        else
        {
            kept = pieces.FindAll(static p => p.Re.Length > 0);
            if (kept.Count == 0)
            {
                var shapes = pieces.ConvertAll(static p => (p.Dims[0], p.Dims[1]));
                (int rows, int cols) = dim == 2 ? JgsEmpty.JoinAcross(shapes) : JgsEmpty.JoinDown(shapes);
                return new MatlabCatPiece { Kind = pieces[0].Kind, Class = pieces[0].Class, Dims = [rows, cols], Im = pieces[0].Im is null ? null : [] };
            }
        }

        if (kept.Count == 1)
        {
            return kept[0];
        }

        int rank = Math.Max(dim, kept.Max(static p => p.Dims.Length));
        var padded = kept.ConvertAll(p => Extend(p.Dims, rank));
        for (int d = 0; d < rank; d++)
        {
            if (d == dim - 1)
            {
                continue;
            }

            foreach (int[] s in padded)
            {
                if (s[d] != padded[0][d])
                {
                    throw Mismatch(line, col);
                }
            }
        }

        for (int i = 0; i < kept.Count; i++)
        {
            kept[i] = new MatlabCatPiece { Kind = kept[i].Kind, Class = kept[i].Class, Dims = padded[i], Re = kept[i].Re, Im = kept[i].Im, HasImaginary = kept[i].HasImaginary };
        }

        return JoinAlong(kept, dim);

        static MatlabCatPiece JoinAlong(List<MatlabCatPiece> parts, int dim)
        {
            int rank = Math.Max(dim, parts.Max(static p => p.Dims.Length));
            var sizes = parts.ConvertAll(p => Extend(p.Dims, rank));
            long inner = 1;
            for (int d = 0; d < dim - 1; d++)
            {
                inner *= sizes[0][d];
            }

            long outer = 1;
            for (int d = dim; d < rank; d++)
            {
                outer *= sizes[0][d];
            }

            int joined = sizes.Sum(s => s[dim - 1]);
            bool complex = parts[0].Im is not null;
            long total = inner * joined * outer;
            var re = new double[total];
            double[]? im = complex ? new double[total] : null;
            long at = 0;
            for (long o = 0; o < outer; o++)
            {
                for (int i = 0; i < parts.Count; i++)
                {
                    long run = inner * sizes[i][dim - 1];
                    Array.Copy(parts[i].Re, o * run, re, at, run);
                    if (im is not null)
                    {
                        Array.Copy(parts[i].Im!, o * run, im, at, run);
                    }

                    at += run;
                }
            }

            int[] dims = (int[])sizes[0].Clone();
            dims[dim - 1] = joined;
            return new MatlabCatPiece
            {
                Kind = parts[0].Kind, Class = parts[0].Class, Dims = dims, Re = re, Im = im,
                HasImaginary = parts.Exists(static p => p.HasImaginary),
            };
        }
    }

    /// <summary>A joined piece as the value it is: a char row or matrix, a logical, a classed number or a complex array.</summary>
    private static JgsValue MatlabCatValue(MatlabCatPiece whole, MatlabCatResult result)
    {
        int[] dims = TrimTrailingOnes(whole.Dims);
        int count = whole.Re.Length;
        switch (result.Kind)
        {
            case MatlabCatKind.Char:
            {
                if (dims.Length == 2)
                {
                    if (count == 0)
                    {
                        return EmptyChar(dims[0], dims[1]);
                    }

                    if (dims[0] == 1)
                    {
                        var text = new char[count];
                        for (int i = 0; i < count; i++)
                        {
                            text[i] = (char)whole.Re[i];
                        }

                        return JgsValue.Str(new string(text));
                    }

                    JgsValue matrix = JgsMatrix.FromColumnMajor(whole.Re, dims[0], dims[1]);
                    matrix.Reshape(dims[0], dims[1]);
                    return matrix.MarkCharMatrix();
                }

                return JgsMatrix.FromColumnMajorDims(whole.Re, dims).MarkCharMatrix();
            }

            case MatlabCatKind.Logical:
            {
                if (count == 1)
                {
                    return JgsValue.Bool(whole.Re[0] != 0);
                }

                if (count == 0 && JgsPacking.Enabled)
                {
                    JgsValue empty = JgsValue.Packed(JgsPacking.Allocate(0), JgsPackedKind.Bool);
                    empty.ReshapeDims(dims);
                    return empty;
                }

                var flags = new JgsValue[count];
                for (int i = 0; i < count; i++)
                {
                    flags[i] = JgsValue.Bool(whole.Re[i] != 0);
                }

                JgsValue mask = JgsMatrix.FromElementsDims(flags, dims);
                if (count == 0)
                {
                    mask.ReshapeDims(dims);
                }

                return mask;
            }
        }

        JgsValue value;
        if (result.Complex)
        {
            var values = new Complex[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = new Complex(whole.Re[i], whole.Im![i]);
            }

            // A complex join is boxed complex elements, as the bracket's literal road builds one (a
            // planar array is what a flagged one needs).
            value = result.FlagOnly
                ? ComplexStorage(values, dims, preserveComplex: true)
                : count == 1 ? JgsValue.ComplexNum(values[0])
                : JgsMatrix.FromElementsDims(Array.ConvertAll(values, JgsValue.ComplexNum), dims);
        }
        else if (count == 1)
        {
            value = JgsValue.Number(whole.Re[0]);
        }
        else if (count == 0 && dims.Length == 2)
        {
            value = JgsEmpty.Shaped(dims[0], dims[1]);
        }
        else
        {
            value = JgsMatrix.FromColumnMajorDims(whole.Re, dims);
        }

        if (result.Class != JgsNumericClass.Double)
        {
            value.SetNumericClass(result.Class);
        }

        return value;
    }

    private static int[] TrimTrailingOnes(int[] dims)
    {
        int n = dims.Length;
        while (n > 2 && dims[n - 1] == 1)
        {
            n--;
        }

        return n == dims.Length ? dims : dims[..n];
    }
}
