using System.Numerics;
using JGraph.Numerics.Sparse;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Single sparse arithmetic in single (open item 345, ADR 0381; probes p8b_a, p8b_b). R2025b sums,
/// multiplies and accumulates a single sparse matrix in single precision, one stored entry after
/// another: <c>sum(sparse(single([1e8; 3; 3])))</c> is 1e8, where adding in double and rounding
/// once makes 100000008. Each product of <c>A * B</c> is rounded to single and added to a single
/// accumulator in the order of the inner index, with no fused multiply-add.
/// </summary>
internal static partial class JgsBuiltins
{
    // --- the reductions: sum, prod, mean, cumsum, cumprod -------------------------------------

    /// <summary>
    /// A reduction of a single sparse matrix, in single (or in double when <c>'double'</c> asks), the
    /// stored entries walked in column-major order. Null hands the call back to the general road:
    /// an empty matrix, a dimension past the second, or anything the parse left over.
    /// </summary>
    private static JgsValue? SingleSparseReduction(
        string name, IReadOnlyList<JgsValue> args, int? dim, int[]? vecdim, JgsValue[] extra, bool all, bool omitNan, bool reverse)
    {
        if (extra.Length > 0)
        {
            return null;
        }

        JgsValue subject = args[0];
        (int rows, int cols) = subject.SparseExtent;
        if (rows == 0 || cols == 0)
        {
            return null;
        }

        if (vecdim is not null)
        {
            bool one = Array.IndexOf(vecdim, 1) >= 0, two = Array.IndexOf(vecdim, 2) >= 0;
            if (one && two)
            {
                all = true;
            }
            else if (one || two)
            {
                dim = one ? 1 : 2;
            }
            else
            {
                return null;
            }
        }

        bool cumulative = name is "cumsum" or "cumprod";
        if (all && cumulative)
        {
            return null;
        }

        int along = all ? 0 : dim ?? (rows != 1 ? 1 : 2);
        if (along > 2)
        {
            return null;
        }

        bool inDouble = !cumulative && args.Skip(1).Any(a => IsTextScalar(a) && TextOf(a).Equals("double", StringComparison.OrdinalIgnoreCase));
        bool product = name is "prod" or "cumprod";
        Complex[] full = SparseColumnMajor(subject);

        // The walks: each output collects its inputs in column-major order (down a column, along a
        // row in column order, or the whole matrix), so one sequence per output.
        int outRows = all || along == 1 ? (cumulative ? rows : 1) : rows;
        int outCols = all ? 1 : along == 2 ? (cumulative ? cols : 1) : cols;
        int lines = all ? 1 : along == 1 ? cols : rows;
        int length = all ? rows * cols : along == 1 ? rows : cols;
        var answer = new Complex[outRows * outCols];
        bool complex = subject.IsComplexSparse;

        for (int lineAt = 0; lineAt < lines; lineAt++)
        {
            int At(int step) => all ? step : along == 1 ? lineAt * rows + step : step * rows + lineAt;
            Complex acc = product ? Complex.One : Complex.Zero;
            int counted = 0;
            for (int s = 0; s < length; s++)
            {
                int step = reverse ? length - 1 - s : s;
                Complex v = full[At(step)];
                bool nan = double.IsNaN(v.Real) || double.IsNaN(v.Imaginary);
                if (!(omitNan && nan))
                {
                    counted++;
                    // A sum skips the zeros it is not storing (adding zero changes nothing); a
                    // product multiplies through them, as the dense fold does.
                    if (product || v != Complex.Zero)
                    {
                        acc = product ? SingleTimes(acc, v, inDouble, complex) : SinglePlus(acc, v, inDouble);
                    }
                }

                if (cumulative)
                {
                    answer[At(step)] = acc;
                }
            }

            if (!cumulative)
            {
                if (name == "mean")
                {
                    acc = counted == 0 ? new Complex(double.NaN, 0)
                        : inDouble ? acc / counted
                        : new Complex(RoundToSingle((float)acc.Real / counted), RoundToSingle((float)acc.Imaginary / counted));
                }

                answer[lineAt] = acc;
            }
        }

        JgsComplexSparse stored = JgsComplexSparse.FromColumnMajor(answer, outRows, outCols);
        if (inDouble)
        {
            return SparseOfComplex(stored);
        }

        return SingleSparseOf(stored, keepFlag: false);
    }

    /// <summary>A sparse matrix's entries full, column-major, real or complex.</summary>
    private static Complex[] SparseColumnMajor(JgsValue sparse) =>
        sparse.IsComplexSparse
            ? sparse.AsComplexSparse.ToColumnMajor()
            : Array.ConvertAll(SparseAsDenseFlat(sparse.AsSparse), static v => new Complex(v, 0));

    private static double[] SparseAsDenseFlat(CscMatrix matrix)
    {
        var flat = new double[matrix.Rows * matrix.Cols];
        for (int c = 0; c < matrix.Cols; c++)
        {
            for (int k = matrix.ColumnStarts[c]; k < matrix.ColumnStarts[c + 1]; k++)
            {
                flat[c * matrix.Rows + matrix.RowIndices[k]] = matrix.Values[k];
            }
        }

        return flat;
    }

    /// <summary>acc + v, each part rounded to single unless the reduction is in double.</summary>
    private static Complex SinglePlus(Complex acc, Complex v, bool inDouble) =>
        inDouble ? acc + v : new Complex(RoundToSingle((float)acc.Real + (float)v.Real), RoundToSingle((float)acc.Imaginary + (float)v.Imaginary));

    /// <summary>acc · v, every partial product rounded to single unless the reduction is in double.</summary>
    private static Complex SingleTimes(Complex acc, Complex v, bool inDouble, bool complex)
    {
        if (inDouble)
        {
            return complex ? acc * v : new Complex(acc.Real * v.Real, 0);
        }

        if (!complex)
        {
            return new Complex(RoundToSingle((float)acc.Real * (float)v.Real), 0);
        }

        float ar = (float)acc.Real, ai = (float)acc.Imaginary, br = (float)v.Real, bi = (float)v.Imaginary;
        return new Complex(RoundToSingle(RoundToSingle(ar * br) - RoundToSingle(ai * bi)), RoundToSingle(RoundToSingle(ar * bi) + RoundToSingle(ai * br)));
    }

    /// <summary>Rounds to single: the cast C# keeps even where the arithmetic is held wider.</summary>
    private static float RoundToSingle(float value) => (float)value;

    // --- the matrix product ---------------------------------------------------------------------

    /// <summary>
    /// <c>A * B</c> answered in single, with a sparse operand and a single one (probes p8b_a,
    /// p8b_b: s_mtimes_*, sa_mtimes_*, sa_c_mtimes*). Column j of the answer is gathered over the
    /// inner index k in order — over B's stored entries when B is sparse, over every k when it is
    /// dense — adding A(:, k)·B(k, j) entry by entry, each product and each sum rounded to single.
    /// A scalar factor is the elementwise road's (null), as is anything that is not two numeric
    /// matrices of agreeing inner size.
    /// </summary>
    private static JgsValue? SingleSparseProduct(JgsValue left, JgsValue right)
    {
        if (left.NumericClass != JgsNumericClass.Single && right.NumericClass != JgsNumericClass.Single)
        {
            return null;
        }

        if (IsOneElement(left) || IsOneElement(right))
        {
            return null;
        }

        if (ProductOperand(left) is not { } a || ProductOperand(right) is not { } b || a.Cols != b.Rows)
        {
            return null;
        }

        bool complex = a.Complex || b.Complex;
        int m = a.Rows, n = b.Cols;
        var answer = new Complex[m * n];
        var acc = new Complex[m];
        for (int j = 0; j < n; j++)
        {
            Array.Clear(acc);
            foreach ((int k, Complex bkj) in b.Column(j))
            {
                foreach ((int i, Complex aik) in a.Column(k))
                {
                    Complex p = complex ? SingleProduct(aik, bkj) : new Complex(RoundToSingle((float)aik.Real * (float)bkj.Real), 0);
                    acc[i] = new Complex(RoundToSingle((float)acc[i].Real + (float)p.Real), RoundToSingle((float)acc[i].Imaginary + (float)p.Imaginary));
                }
            }

            Array.Copy(acc, 0, answer, j * m, m);
        }

        if (left.Type == JgsType.Sparse && right.Type == JgsType.Sparse)
        {
            return SingleSparseOf(JgsComplexSparse.FromColumnMajor(answer, m, n), keepFlag: false);
        }

        JgsValue dense = Array.TrueForAll(answer, static z => z.Imaginary == 0)
            ? JgsMatrix.FromColumnMajor(Array.ConvertAll(answer, static z => z.Real), m, n)
            : ComplexStorage(answer, [m, n]);
        return JgsNumericClasses.Stamp(dense, JgsNumericClass.Single);
    }

    private static Complex SingleProduct(Complex a, Complex b)
    {
        float ar = (float)a.Real, ai = (float)a.Imaginary, br = (float)b.Real, bi = (float)b.Imaginary;
        float rr = RoundToSingle(ar * br), ii = RoundToSingle(ai * bi), ri = RoundToSingle(ar * bi), ir = RoundToSingle(ai * br);
        return new Complex(RoundToSingle(rr - ii), RoundToSingle(ri + ir));
    }

    /// <summary>One factor of <see cref="SingleSparseProduct"/>: its size and a column walker.</summary>
    private sealed record ProductFactor(int Rows, int Cols, bool Complex, Func<int, IEnumerable<(int Index, Complex Value)>> Column);

    private static ProductFactor? ProductOperand(JgsValue value)
    {
        if (value.Type == JgsType.Sparse)
        {
            (int rows, int cols) = value.SparseExtent;
            if (value.IsComplexSparse)
            {
                JgsComplexSparse c = value.AsComplexSparse;
                return new ProductFactor(rows, cols, true, j => StoredColumn(c.ColumnStarts, c.RowIndices, j, k => c.Values[k]));
            }

            CscMatrix r = value.AsSparse;
            return new ProductFactor(rows, cols, false, j => StoredColumn(r.ColumnStarts, r.RowIndices, j, k => new Complex(r.Values[k], 0)));
        }

        if (value.Type != JgsType.Array || value.IsCharMatrix || value.IsStringArray || value.IsTime || value.IsNd)
        {
            return null;
        }

        int[] dims = SizeDims(value);
        if (dims.Length != 2)
        {
            return null;
        }

        Complex[] flat;
        try
        {
            flat = ColumnComplex("mtimes", value, 0, 0);
        }
        catch (JgsRuntimeException)
        {
            return null;
        }

        int height = dims[0];
        bool complex = Array.Exists(flat, static z => z.Imaginary != 0);
        return new ProductFactor(dims[0], dims[1], complex, j => DenseColumn(flat, height, j));
    }

    private static IEnumerable<(int Index, Complex Value)> StoredColumn(int[] starts, int[] rows, int j, Func<int, Complex> value)
    {
        for (int k = starts[j]; k < starts[j + 1]; k++)
        {
            yield return (rows[k], value(k));
        }
    }

    private static IEnumerable<(int Index, Complex Value)> DenseColumn(Complex[] flat, int height, int j)
    {
        for (int i = 0; i < height; i++)
        {
            yield return (i, flat[j * height + i]);
        }
    }
}
