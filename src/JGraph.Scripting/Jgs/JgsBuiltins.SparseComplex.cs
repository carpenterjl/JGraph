using System.Numerics;
using JGraph.Numerics.Sparse;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Complex sparse matrices (open item 298, ADR 0364): built by <c>sparse</c> from a complex dense
/// matrix or complex triplets, and answered by <c>full</c>, <c>nnz</c>, <c>nzmax</c>, <c>nonzeros</c>,
/// <c>find</c>, <c>issparse</c>, <c>isreal</c>, <c>size</c>, the display, <c>+</c>, <c>-</c>,
/// <c>*</c>, a scalar's <c>/</c>, unary minus, <c>'</c>, <c>.'</c>, <c>real</c>, <c>imag</c>,
/// <c>abs</c>, <c>conj</c>, a read by subscripts and a version 5 MAT-file. Every other verb refuses one
/// through <see cref="JgsValue.AsSparse"/>.
/// </summary>
/// <remarks>
/// Measured in R2025b (probes p5c and p5e): <c>sparse</c> of a complex dense matrix is complex even
/// with every imaginary part zero, while <c>sparse(i, j, v)</c> of such a <c>v</c>, and any arithmetic
/// or subscript read, is real when nothing imaginary is left. <c>'</c>, <c>.'</c>, <c>full</c> and a
/// MAT-file keep the flag; unary minus drops it.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// A complex sparse answer, real when no stored entry has an imaginary part, unless
    /// <paramref name="keepFlag"/> says the verb keeps R2025b's complex flag.
    /// </summary>
    internal static JgsValue SparseOfComplex(JgsComplexSparse matrix, bool keepFlag = false) =>
        keepFlag || matrix.AnyImaginary
            ? JgsValue.ComplexSparse(matrix)
            : JgsValue.Sparse(matrix.MapReal(static v => v.Real));

    /// <summary>A sparse value's entries as complex ones, real or complex.</summary>
    private static JgsComplexSparse ComplexEntriesOf(JgsValue sparse) =>
        sparse.IsComplexSparse ? sparse.AsComplexSparse : JgsComplexSparse.FromReal(sparse.AsSparse);

    /// <summary>
    /// The dense value a sparse matrix stands for: <c>full</c>. A complex one is complex, flag and
    /// all, as R2025b's <c>full(sparse(complex([1 0], 0)))</c> is.
    /// </summary>
    internal static JgsValue SparseDense(JgsValue sparse)
    {
        JgsValue dense;
        if (!sparse.IsComplexSparse)
        {
            dense = SparseAsDense(sparse.AsSparse);
        }
        else
        {
            JgsComplexSparse matrix = sparse.AsComplexSparse;
            dense = ComplexStorage(matrix.ToColumnMajor(), [matrix.Rows, matrix.Cols], preserveComplex: !matrix.AnyImaginary);
        }

        // A logical matrix reads full as a logical one, a single one as single (open item 331).
        return sparse.IsLogicalSparse ? AsMask(dense)
            : IsSingleSparse(sparse) ? JgsNumericClasses.Stamp(dense, JgsNumericClass.Single)
            : dense;
    }

    /// <summary>Whether a dense value is complex: an imaginary part anywhere, or the flag.</summary>
    private static bool IsComplexDense(JgsValue value) =>
        value.Type == JgsType.Complex || (value.Type == JgsType.Array && HasComplexPart(value));

    /// <summary><c>sparse(A)</c> of a complex dense matrix, which keeps the flag (R2025b's sp1-sp3).</summary>
    private static JgsValue ComplexSparseFromDense(string name, JgsValue value, int line, int col)
    {
        int[] dims = SizeDims(value);
        if (dims.Length > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:sparse:invalidMatrix", "Sparse matrices must be 2-D.");
        }

        Complex[] flat = ColumnComplex(name, value, line, col);
        return JgsValue.ComplexSparse(JgsComplexSparse.FromColumnMajor(flat, dims[0], dims[1]));
    }

    /// <summary>
    /// <c>sparse(i, j, v, ...)</c> with a complex <c>v</c>: duplicates sum, and the answer is real when
    /// nothing imaginary is left (R2025b's s26 and s41).
    /// </summary>
    private static JgsValue ComplexSparseFromTriplets(
        double[] i, double[] j, Complex[] v, int rows, int cols, int line, int col)
    {
        var triplets = new (int, int, Complex)[i.Length];
        for (int t = 0; t < i.Length; t++)
        {
            if (!double.IsFinite(i[t]) || !double.IsFinite(j[t]) || i[t] != Math.Truncate(i[t])
                || j[t] != Math.Truncate(j[t]) || i[t] < 1 || j[t] < 1 || i[t] > rows || j[t] > cols)
            {
                throw new JgsRuntimeException(line, col, "sparse indices must be positive integers within the matrix dimensions.");
            }

            triplets[t] = ((int)i[t] - 1, (int)j[t] - 1, v[t]);
        }

        return SparseOfComplex(JgsComplexSparse.FromTriplets(rows, cols, triplets));
    }

    /// <summary><c>[i, j, v] = find(S)</c> over a complex sparse matrix's stored entries.</summary>
    internal static JgsValue[] ComplexSparseFind(JgsComplexSparse matrix, int wanted, JgsDialect dialect)
    {
        var rows = new List<double>(matrix.NonZeroCount);
        var cols = new List<double>(matrix.NonZeroCount);
        var values = new List<Complex>(matrix.NonZeroCount);
        var linear = new List<double>(matrix.NonZeroCount);
        foreach ((int r, int c, Complex v) in matrix.Entries())
        {
            rows.Add(r + dialect.IndexBase);
            cols.Add(c + dialect.IndexBase);
            values.Add(v);
            linear.Add(((long)c * matrix.Rows) + r + dialect.IndexBase);
        }

        JgsValue Column(List<double> from)
        {
            JgsValue built = Numbers([.. from]);
            built.Reshape(matrix.Rows == 1 ? 1 : from.Count, matrix.Rows == 1 ? from.Count : from.Count == 0 ? 0 : 1); // a row vector answers rows (open item 331)
            return built;
        }

        // The values are complex when any is (R2025b's s6c); a flagged matrix's are real (spf13).
        JgsValue Values() => values.Exists(static v => v.Imaginary != 0)
            ? ComplexStorage([.. values], matrix.Rows == 1 ? [1, values.Count] : [values.Count, values.Count == 0 ? 0 : 1])
            : Column(values.ConvertAll(static v => v.Real));

        return wanted <= 1
            ? [Column(linear)]
            : wanted == 2
                ? [Column(rows), Column(cols)]
                : [Column(rows), Column(cols), Values()];
    }

    /// <summary>A subscript read of a complex sparse matrix: real when nothing imaginary is picked.</summary>
    internal static JgsValue ComplexSparseSubscript(
        JgsValue value, IReadOnlyList<JgsValue> subscripts, JgsDialect dialect, int line, int col)
    {
        JgsComplexSparse matrix = value.AsComplexSparse;

        // The same reading the real storage gives, over the real and imaginary parts as two real
        // sparse matrices, so every subscript form and refusal is the one the real road has.
        JgsValue realPart = SparseSubscript(JgsValue.Sparse(matrix.MapReal(static v => v.Real)), subscripts, dialect, line, col);
        JgsValue imagPart = SparseSubscript(JgsValue.Sparse(matrix.MapReal(static v => v.Imaginary)), subscripts, dialect, line, col);
        if (realPart.Type != JgsType.Sparse)
        {
            return JgsValue.ComplexNum(new Complex(realPart.AsNumber, imagPart.AsNumber)); // the JGS dialect's scalar
        }

        CscMatrix re = realPart.AsSparse;
        CscMatrix im = imagPart.AsSparse;
        return SparseOfComplex(JgsComplexSparse.FromReal(re).Add(JgsComplexSparse.FromReal(im).Map(static v => v * Complex.ImaginaryOne), 1));
    }

    /// <summary>
    /// <c>'</c> and <c>.'</c> of a complex sparse matrix: the flag is kept (R2025b's spf2, spf10).
    /// </summary>
    internal static JgsValue ComplexSparseTransposed(JgsValue value, bool conjugate) =>
        SparseInClassOf(JgsValue.ComplexSparse(value.AsComplexSparse.Transpose(conjugate)), value);

    /// <summary>Unary minus of a sparse matrix, real or complex; the flag is dropped (R2025b's spf3).</summary>
    internal static JgsValue SparseNegated(JgsValue value) =>
        SparseInClassOf(value.IsComplexSparse
            ? SparseOfComplex(value.AsComplexSparse.Map(static v => -v))
            : JgsValue.Sparse(value.AsSparse.Scale(-1)), value, keepLogical: false); // a single stays single (open item 331)

    /// <summary>
    /// The binary operators where a complex sparse matrix is an operand, or a real sparse matrix meets
    /// a complex scalar; null for everything else, which the real road answers or refuses.
    /// </summary>
    internal static JgsValue? ComplexSparseBinary(TokenType op, JgsValue left, JgsValue right, Node at)
    {
        bool complexSparse = left.IsComplexSparse || right.IsComplexSparse;
        JgsValue other = left.Type == JgsType.Sparse ? right : left;
        bool complexScalar = other.Type == JgsType.Complex || (IsZeroImagFlagged(other) && other.ArrayLength == 1);
        if (!complexSparse && !(complexScalar && left.Type != right.Type))
        {
            return null;
        }

        int line = at.Line;
        int col = at.Column;
        bool sparseLeft = left.Type == JgsType.Sparse;
        JgsComplexSparse matrix = ComplexEntriesOf(sparseLeft ? left : right);
        try
        {
            // Both sparse: the sum, the difference and the product stay sparse.
            if (left.Type == JgsType.Sparse && right.Type == JgsType.Sparse)
            {
                JgsComplexSparse a = ComplexEntriesOf(left);
                JgsComplexSparse b = ComplexEntriesOf(right);
                return op switch
                {
                    TokenType.Plus => SparseOfComplex(a.Add(b, 1)),
                    TokenType.Minus => SparseOfComplex(a.Add(b, -1)),
                    TokenType.Star => SparseOfComplex(a.Multiply(b)),
                    TokenType.DotStar => SparseOfComplex(a.ElementwiseMultiply(b)),
                    _ => throw new JgsRuntimeException(line, col,
                        $"'{OpName(op)}' is not supported between sparse matrices; use full() first."),
                };
            }

            // A scalar scales the stored entries: s*S, S*s, S/s, S.*s, S./s.
            bool scalar = other.Type is JgsType.Number or JgsType.Bool or JgsType.Complex
                || (other.Type == JgsType.Array && other.ArrayLength == 1 && !other.IsStringArray);
            if (scalar && op is TokenType.Star or TokenType.DotStar or TokenType.Slash or TokenType.DotSlash)
            {
                Complex factor = ScalarComplex(OpName(op), other, line, col);
                if (op is TokenType.Slash or TokenType.DotSlash)
                {
                    if (!sparseLeft)
                    {
                        throw new JgsRuntimeException(line, col,
                            "dividing a scalar by a sparse matrix makes it dense; use full() first.");
                    }

                    return SparseOfComplex(matrix.Map(v => v / factor));
                }

                return SparseOfComplex(matrix.Map(v => v * factor));
            }

            // A dense operand: the sum and the difference are dense, and so is a product with a matrix.
            if (op is TokenType.Plus or TokenType.Minus)
            {
                JgsValue a = sparseLeft ? DenseOfComplexSparse(matrix) : left;
                JgsValue b = sparseLeft ? right : DenseOfComplexSparse(matrix);
                return DenseComplexSum(op == TokenType.Minus, a, b, line, col);
            }

            if (op == TokenType.Star)
            {
                return DenseComplexProduct(sparseLeft ? DenseOfComplexSparse(matrix) : left,
                    sparseLeft ? right : DenseOfComplexSparse(matrix), line, col);
            }

            // A comparison is a logical sparse mask, answered by SparseLogicalOperator (open item 331).
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, ex.Message);
        }

        throw new JgsRuntimeException(line, col,
            $"'{OpName(op)}' between a complex sparse matrix and a {other.TypeName} is not supported; use full() first.");
    }

    private static JgsValue DenseOfComplexSparse(JgsComplexSparse matrix) =>
        ComplexStorage(matrix.ToColumnMajor(), [matrix.Rows, matrix.Cols]);

    /// <summary>A dense sum or difference with implicit expansion, real when nothing imaginary is left.</summary>
    private static JgsValue DenseComplexSum(bool subtract, JgsValue a, JgsValue b, int line, int col)
    {
        int[] ad = SizeDims(a), bd = SizeDims(b);
        if (ad.Length > 2 || bd.Length > 2
            || (ad[0] != bd[0] && ad[0] != 1 && bd[0] != 1) || (ad[1] != bd[1] && ad[1] != 1 && bd[1] != 1))
        {
            throw new JgsRuntimeException(line, col, "Array dimensions must agree.");
        }

        int rows = ad[0] == 1 ? bd[0] : ad[0], cols = ad[1] == 1 ? bd[1] : ad[1];
        Complex[] av = ColumnComplex("sparse arithmetic", a, line, col);
        Complex[] bv = ColumnComplex("sparse arithmetic", b, line, col);
        var output = new Complex[rows * cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                Complex x = av[((ad[1] == 1 ? 0 : c) * ad[0]) + (ad[0] == 1 ? 0 : r)];
                Complex y = bv[((bd[1] == 1 ? 0 : c) * bd[0]) + (bd[0] == 1 ? 0 : r)];
                output[(c * rows) + r] = subtract ? x - y : x + y;
            }
        }

        return Array.Exists(output, static z => z.Imaginary != 0)
            ? ComplexStorage(output, [rows, cols])
            : JgsMatrix.FromColumnMajor(Array.ConvertAll(output, static z => z.Real), rows, cols);
    }

    /// <summary>A dense matrix product, real when nothing imaginary is left.</summary>
    private static JgsValue DenseComplexProduct(JgsValue a, JgsValue b, int line, int col)
    {
        int[] ad = SizeDims(a), bd = SizeDims(b);
        if (ad.Length > 2 || bd.Length > 2 || ad[1] != bd[0])
        {
            throw new JgsRuntimeException(line, col,
                $"Inner dimensions disagree: {ad[0]}x{ad[1]} times {bd[0]}x{bd[1]}.");
        }

        Complex[] av = ColumnComplex("*", a, line, col);
        Complex[] bv = ColumnComplex("*", b, line, col);
        int rows = ad[0], inner = ad[1], cols = bd[1];
        var output = new Complex[rows * cols];
        for (int c = 0; c < cols; c++)
        {
            for (int k = 0; k < inner; k++)
            {
                Complex factor = bv[(c * inner) + k];
                if (factor == Complex.Zero)
                {
                    continue;
                }

                for (int r = 0; r < rows; r++)
                {
                    output[(c * rows) + r] += av[(k * rows) + r] * factor;
                }
            }
        }

        return Array.Exists(output, static z => z.Imaginary != 0)
            ? ComplexStorage(output, [rows, cols])
            : JgsMatrix.FromColumnMajor(Array.ConvertAll(output, static z => z.Real), rows, cols);
    }

    /// <summary>
    /// <c>real</c>, <c>imag</c>, <c>abs</c>, <c>conj</c>, <c>nonzeros</c> and <c>nzmax</c> of a sparse
    /// matrix, real or complex (R2025b's s19-s22, s29, s40): the first three are real sparse, conj
    /// keeps the matrix complex, nonzeros is the dense column of stored values. Wrapped round the
    /// verbs as they stand, so every other argument reaches them unchanged.
    /// </summary>
    private static void WrapSparseVerbs(JgsEnvironment env)
    {
        void Wrap(string name, Func<JgsValue, JgsValue> sparse)
        {
            if (!env.Builtins.TryGet(name, out JgsValue existing) || existing.Type != JgsType.Function
                || existing.AsCallable is not BuiltinFunction inner)
            {
                return;
            }

            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) =>
                args.Count == 1 && args[0].Type == JgsType.Sparse ? sparse(args[0]) : inner.Call(args, line, col))
            {
                MultiOutput = inner.MultiOutput,
                KeepsStringArguments = inner.KeepsStringArguments,
                BindsAnsAsStatement = inner.BindsAnsAsStatement,
                AutoCallsBare = inner.AutoCallsBare,
                KnowsWhenDiscarded = inner.KnowsWhenDiscarded,
                TakesOutputCount = inner.TakesOutputCount,
            }));
        }

        Wrap("real", static s => JgsValue.Sparse(ComplexEntriesOf(s).MapReal(static v => v.Real)));
        Wrap("imag", static s => JgsValue.Sparse(ComplexEntriesOf(s).MapReal(static v => v.Imaginary)));
        Wrap("abs", static s => JgsValue.Sparse(ComplexEntriesOf(s).MapReal(static v => Complex.Abs(v))));
        Wrap("conj", static s => s.IsComplexSparse ? SparseOfComplex(s.AsComplexSparse.Map(Complex.Conjugate)) : s);
        Wrap("nonzeros", static s =>
        {
            Complex[] values = [.. ComplexEntriesOf(s).Entries().Select(static e => e.Value)];
            JgsValue column = Array.Exists(values, static v => v.Imaginary != 0)
                ? ComplexStorage(values, [values.Length, values.Length == 0 ? 0 : 1])
                : JgsMatrix.FromColumnMajor(Array.ConvertAll(values, static v => v.Real), values.Length, values.Length == 0 ? 0 : 1);
            return SparseInClassOf(column, s); // a logical matrix's are logical (open item 331)
        });
        Wrap("nzmax", static s => s.IsComplexSparse
            ? JgsValue.Number(Math.Max(1, s.AsComplexSparse.NonZeroCount))
            : JgsValue.Number(Math.Max(1, Math.Max(s.AsSparse.NonZeroCount, s.AsSparse.ReservedCapacity))));
    }
}
