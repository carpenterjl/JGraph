using System.Numerics;
using JGraph.Numerics.Sparse;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// A complex sparse matrix (open item 298, ADR 0364): compressed sparse columns, as
/// <see cref="CscMatrix"/> keeps a real one, with a complex value per stored entry. Immutable, as
/// <see cref="CscMatrix"/> is, so bindings share it without copying.
/// </summary>
/// <remarks>
/// It lives beside the real storage rather than inside it, so the solvers, the orderings and the
/// Krylov methods, which read <see cref="CscMatrix.Values"/> as reals, never meet it: a verb not
/// taught the complex kind is refused by <see cref="JgsValue.AsSparse"/> in words that say so,
/// rather than reading half of every entry. An entry is stored when either part is non-zero. A
/// matrix whose every stored entry has a zero imaginary part is one R2025b still calls complex —
/// <c>sparse(complex([1 0; 0 2], 0))</c> — and the verbs that would make it real (arithmetic,
/// indexing) hand back a <see cref="CscMatrix"/> instead (<see cref="JgsBuiltins.SparseOfComplex"/>).
/// </remarks>
internal sealed class JgsComplexSparse
{
    private JgsComplexSparse(int rows, int cols, int[] columnStarts, int[] rowIndices, Complex[] values)
    {
        Rows = rows;
        Cols = cols;
        ColumnStarts = columnStarts;
        RowIndices = rowIndices;
        Values = values;
    }

    public int Rows { get; }

    public int Cols { get; }

    /// <summary>Where each column's entries begin in <see cref="RowIndices"/>; length Cols+1.</summary>
    public int[] ColumnStarts { get; }

    /// <summary>The row of each stored entry, ascending within a column.</summary>
    public int[] RowIndices { get; }

    /// <summary>The value of each stored entry.</summary>
    public Complex[] Values { get; }

    /// <summary>The stored entries: what <c>nnz</c> counts.</summary>
    public int NonZeroCount => ColumnStarts[Cols];

    /// <summary>Whether any stored entry has a non-zero imaginary part.</summary>
    public bool AnyImaginary => Array.Exists(Values, static v => v.Imaginary != 0);

    /// <summary>From unordered (row, col, value) triplets: duplicates sum, zeros are kept out.</summary>
    public static JgsComplexSparse FromTriplets(int rows, int cols, IEnumerable<(int Row, int Col, Complex Value)> triplets)
    {
        var perColumn = new List<(int Row, Complex Value)>?[cols];
        foreach ((int row, int col, Complex value) in triplets)
        {
            if ((uint)row >= (uint)rows || (uint)col >= (uint)cols)
            {
                throw new ArgumentOutOfRangeException(nameof(triplets), "A triplet lies outside the matrix.");
            }

            (perColumn[col] ??= []).Add((row, value));
        }

        var starts = new int[cols + 1];
        var rowsOut = new List<int>();
        var valuesOut = new List<Complex>();
        for (int c = 0; c < cols; c++)
        {
            starts[c] = rowsOut.Count;
            List<(int Row, Complex Value)>? entries = perColumn[c];
            if (entries is null)
            {
                continue;
            }

            entries.Sort(static (a, b) => a.Row.CompareTo(b.Row));
            int i = 0;
            while (i < entries.Count)
            {
                int row = entries[i].Row;
                Complex sum = Complex.Zero;
                while (i < entries.Count && entries[i].Row == row)
                {
                    sum += entries[i].Value;
                    i++;
                }

                if (sum != Complex.Zero)
                {
                    rowsOut.Add(row);
                    valuesOut.Add(sum);
                }
            }
        }

        starts[cols] = rowsOut.Count;
        return new JgsComplexSparse(rows, cols, starts, rowsOut.ToArray(), valuesOut.ToArray());
    }

    /// <summary>From a dense column-major array, leaving out the zeros.</summary>
    public static JgsComplexSparse FromColumnMajor(Complex[] flat, int rows, int cols)
    {
        var starts = new int[cols + 1];
        var rowsOut = new List<int>();
        var valuesOut = new List<Complex>();
        for (int c = 0; c < cols; c++)
        {
            starts[c] = rowsOut.Count;
            for (int r = 0; r < rows; r++)
            {
                Complex value = flat[(c * rows) + r];
                if (value != Complex.Zero)
                {
                    rowsOut.Add(r);
                    valuesOut.Add(value);
                }
            }
        }

        starts[cols] = rowsOut.Count;
        return new JgsComplexSparse(rows, cols, starts, rowsOut.ToArray(), valuesOut.ToArray());
    }

    /// <summary>A real sparse matrix's entries as complex ones.</summary>
    public static JgsComplexSparse FromReal(CscMatrix matrix) =>
        new(matrix.Rows, matrix.Cols, matrix.ColumnStarts, matrix.RowIndices,
            Array.ConvertAll(matrix.Values, static v => new Complex(v, 0)));

    /// <summary>The stored entries, column by column.</summary>
    public IEnumerable<(int Row, int Col, Complex Value)> Entries()
    {
        for (int c = 0; c < Cols; c++)
        {
            for (int k = ColumnStarts[c]; k < ColumnStarts[c + 1]; k++)
            {
                yield return (RowIndices[k], c, Values[k]);
            }
        }
    }

    /// <summary>The dense column-major expansion: what <c>full</c> hands back.</summary>
    public Complex[] ToColumnMajor()
    {
        var flat = new Complex[(long)Rows * Cols];
        foreach ((int r, int c, Complex v) in Entries())
        {
            flat[(c * Rows) + r] = v;
        }

        return flat;
    }

    /// <summary>One entry; an absent one is zero.</summary>
    public Complex At(int row, int col)
    {
        for (int k = ColumnStarts[col]; k < ColumnStarts[col + 1]; k++)
        {
            if (RowIndices[k] == row)
            {
                return Values[k];
            }
        }

        return Complex.Zero;
    }

    /// <summary><c>.'</c>, or <c>'</c> with <paramref name="conjugate"/>.</summary>
    public JgsComplexSparse Transpose(bool conjugate) =>
        FromTriplets(Cols, Rows, Entries().Select(e => (e.Col, e.Row, conjugate ? Complex.Conjugate(e.Value) : e.Value)));

    /// <summary>Each stored entry mapped (a zero answer leaves the pattern).</summary>
    public JgsComplexSparse Map(Func<Complex, Complex> map) =>
        FromTriplets(Rows, Cols, Entries().Select(e => (e.Row, e.Col, map(e.Value))));

    /// <summary>Each stored entry mapped to a real number (a zero answer leaves the pattern).</summary>
    public CscMatrix MapReal(Func<Complex, double> map) =>
        CscMatrix.FromTriplets(Rows, Cols, [.. Entries().Select(e => (e.Row, e.Col, map(e.Value)))]);

    /// <summary>this + sign·other over the union of the patterns.</summary>
    public JgsComplexSparse Add(JgsComplexSparse other, double sign)
    {
        if (other.Rows != Rows || other.Cols != Cols)
        {
            throw new ArgumentException("Matrix dimensions must agree.");
        }

        return FromTriplets(Rows, Cols, Entries().Concat(other.Entries().Select(e => (e.Row, e.Col, sign * e.Value))));
    }

    /// <summary>this .* other, over the entries both store.</summary>
    public JgsComplexSparse ElementwiseMultiply(JgsComplexSparse other)
    {
        if (other.Rows != Rows || other.Cols != Cols)
        {
            throw new ArgumentException("Matrix dimensions must agree.");
        }

        return FromTriplets(Rows, Cols, Entries().Select(e => (e.Row, e.Col, e.Value * other.At(e.Row, e.Col))));
    }

    /// <summary>this · other.</summary>
    public JgsComplexSparse Multiply(JgsComplexSparse other)
    {
        if (Cols != other.Rows)
        {
            throw new ArgumentException($"Inner dimensions disagree: {Rows}x{Cols} times {other.Rows}x{other.Cols}.");
        }

        var products = new List<(int, int, Complex)>();
        for (int c = 0; c < other.Cols; c++)
        {
            for (int i = other.ColumnStarts[c]; i < other.ColumnStarts[c + 1]; i++)
            {
                int k = other.RowIndices[i];
                Complex factor = other.Values[i];
                for (int j = ColumnStarts[k]; j < ColumnStarts[k + 1]; j++)
                {
                    products.Add((RowIndices[j], c, Values[j] * factor));
                }
            }
        }

        return FromTriplets(Rows, other.Cols, products);
    }
}
