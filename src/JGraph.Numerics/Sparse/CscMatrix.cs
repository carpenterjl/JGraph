namespace JGraph.Numerics.Sparse;

/// <summary>
/// An immutable sparse matrix in compressed sparse column form — column-major like everything else
/// in JGraph, so a column's nonzeros are one contiguous slice. Immutability is what lets script
/// bindings share instances without copy-on-assign bookkeeping.
/// </summary>
public sealed class CscMatrix
{
    private CscMatrix(int rows, int cols, int[] columnStarts, int[] rowIndices, double[] values)
    {
        Rows = rows;
        Cols = cols;
        ColumnStarts = columnStarts;
        RowIndices = rowIndices;
        Values = values;
    }

    public int Rows { get; }

    public int Cols { get; }

    /// <summary>Index into <see cref="RowIndices"/>/<see cref="Values"/> where each column begins; length Cols+1.</summary>
    public int[] ColumnStarts { get; }

    /// <summary>Row index of each stored entry, ascending within a column.</summary>
    public int[] RowIndices { get; }

    public double[] Values { get; }

    public int NonZeroCount => ColumnStarts[Cols];

    /// <summary>Builds from unordered (row, col, value) triplets; duplicates sum, zeros are kept out.</summary>
    public static CscMatrix FromTriplets(int rows, int cols, IReadOnlyList<(int Row, int Col, double Value)> triplets)
    {
        var perColumn = new List<(int Row, double Value)>[cols];
        foreach ((int row, int colIndex, double value) in triplets)
        {
            if ((uint)row >= (uint)rows || (uint)colIndex >= (uint)cols)
            {
                throw new ArgumentOutOfRangeException(nameof(triplets), "A triplet lies outside the matrix.");
            }

            (perColumn[colIndex] ??= []).Add((row, value));
        }

        var starts = new int[cols + 1];
        var rowsOut = new List<int>();
        var valuesOut = new List<double>();
        for (int c = 0; c < cols; c++)
        {
            starts[c] = rowsOut.Count;
            List<(int Row, double Value)>? entries = perColumn[c];
            if (entries is null)
            {
                continue;
            }

            entries.Sort(static (a, b) => a.Row.CompareTo(b.Row));
            int i = 0;
            while (i < entries.Count)
            {
                int row = entries[i].Row;
                double sum = 0;
                while (i < entries.Count && entries[i].Row == row)
                {
                    sum += entries[i].Value;
                    i++;
                }

                if (sum != 0)
                {
                    rowsOut.Add(row);
                    valuesOut.Add(sum);
                }
            }
        }

        starts[cols] = rowsOut.Count;
        return new CscMatrix(rows, cols, starts, rowsOut.ToArray(), valuesOut.ToArray());
    }

    /// <summary>Builds from a dense column-major buffer, dropping exact zeros.</summary>
    public static CscMatrix FromColumnMajor(double[] flat, int rows, int cols)
    {
        var starts = new int[cols + 1];
        var rowsOut = new List<int>();
        var valuesOut = new List<double>();
        for (int c = 0; c < cols; c++)
        {
            starts[c] = rowsOut.Count;
            int origin = c * rows;
            for (int r = 0; r < rows; r++)
            {
                double value = flat[origin + r];
                if (value != 0)
                {
                    rowsOut.Add(r);
                    valuesOut.Add(value);
                }
            }
        }

        starts[cols] = rowsOut.Count;
        return new CscMatrix(rows, cols, starts, rowsOut.ToArray(), valuesOut.ToArray());
    }

    /// <summary>The dense column-major expansion — the escape hatch <c>full</c> uses.</summary>
    public double[] ToColumnMajor()
    {
        var flat = new double[(long)Rows * Cols];
        for (int c = 0; c < Cols; c++)
        {
            for (int i = ColumnStarts[c]; i < ColumnStarts[c + 1]; i++)
            {
                flat[(c * Rows) + RowIndices[i]] = Values[i];
            }
        }

        return flat;
    }

    /// <summary>
    /// One entry, without expanding anything. A stored zero and an absent entry are the same number,
    /// which is why a lookup that finds nothing is an answer rather than a miss.
    /// </summary>
    public double At(int row, int col)
    {
        if (row < 0 || row >= Rows || col < 0 || col >= Cols)
        {
            throw new ArgumentOutOfRangeException(nameof(row), "Subscript is outside the matrix.");
        }

        for (int i = ColumnStarts[col]; i < ColumnStarts[col + 1]; i++)
        {
            if (RowIndices[i] == row)
            {
                return Values[i];
            }
        }

        return 0;
    }

    public CscMatrix Transpose()
    {
        var triplets = new List<(int, int, double)>(NonZeroCount);
        for (int c = 0; c < Cols; c++)
        {
            for (int i = ColumnStarts[c]; i < ColumnStarts[c + 1]; i++)
            {
                triplets.Add((c, RowIndices[i], Values[i]));
            }
        }

        return FromTriplets(Cols, Rows, triplets);
    }

    /// <summary>this + scale·other, elementwise over the union of patterns.</summary>
    public CscMatrix Add(CscMatrix other, double scale = 1)
    {
        if (other.Rows != Rows || other.Cols != Cols)
        {
            throw new ArgumentException($"Cannot combine {Rows}x{Cols} with {other.Rows}x{other.Cols}.");
        }

        var triplets = new List<(int, int, double)>(NonZeroCount + other.NonZeroCount);
        for (int c = 0; c < Cols; c++)
        {
            for (int i = ColumnStarts[c]; i < ColumnStarts[c + 1]; i++)
            {
                triplets.Add((RowIndices[i], c, Values[i]));
            }

            for (int i = other.ColumnStarts[c]; i < other.ColumnStarts[c + 1]; i++)
            {
                triplets.Add((other.RowIndices[i], c, scale * other.Values[i]));
            }
        }

        return FromTriplets(Rows, Cols, triplets);
    }

    public CscMatrix Scale(double factor)
    {
        var scaled = (double[])Values.Clone();
        for (int i = 0; i < scaled.Length; i++)
        {
            scaled[i] *= factor;
        }

        return new CscMatrix(Rows, Cols, ColumnStarts, RowIndices, scaled);
    }

    /// <summary>this · other, sparse × sparse, column at a time with a dense accumulator.</summary>
    public CscMatrix Multiply(CscMatrix other)
    {
        if (Cols != other.Rows)
        {
            throw new ArgumentException($"Inner dimensions disagree: {Rows}x{Cols} times {other.Rows}x{other.Cols}.");
        }

        var starts = new int[other.Cols + 1];
        var rowsOut = new List<int>();
        var valuesOut = new List<double>();
        var accumulator = new double[Rows];
        var touched = new List<int>();
        for (int c = 0; c < other.Cols; c++)
        {
            starts[c] = rowsOut.Count;
            for (int i = other.ColumnStarts[c]; i < other.ColumnStarts[c + 1]; i++)
            {
                int k = other.RowIndices[i];
                double factor = other.Values[i];
                for (int j = ColumnStarts[k]; j < ColumnStarts[k + 1]; j++)
                {
                    int row = RowIndices[j];
                    if (accumulator[row] == 0)
                    {
                        touched.Add(row);
                    }

                    accumulator[row] += factor * Values[j];
                }
            }

            touched.Sort();
            foreach (int row in touched)
            {
                if (accumulator[row] != 0)
                {
                    rowsOut.Add(row);
                    valuesOut.Add(accumulator[row]);
                }

                accumulator[row] = 0;
            }

            touched.Clear();
        }

        starts[other.Cols] = rowsOut.Count;
        return new CscMatrix(Rows, other.Cols, starts, rowsOut.ToArray(), valuesOut.ToArray());
    }

    /// <summary>this · x for a dense vector.</summary>
    public double[] MultiplyVector(double[] x)
    {
        if (x.Length != Cols)
        {
            throw new ArgumentException($"A {Rows}x{Cols} matrix cannot multiply a {x.Length}-vector.");
        }

        var y = new double[Rows];
        for (int c = 0; c < Cols; c++)
        {
            double factor = x[c];
            if (factor == 0)
            {
                continue;
            }

            for (int i = ColumnStarts[c]; i < ColumnStarts[c + 1]; i++)
            {
                y[RowIndices[i]] += factor * Values[i];
            }
        }

        return y;
    }

    /// <summary>
    /// Sparse LU with partial pivoting (Gilbert–Peierls, left-looking with a dense working column —
    /// simple and dependable at the densities scripts build with sprand; no fill-reducing ordering).
    /// Returns L (unit diagonal) and U with the row permutation folded into L, so L·U = A.
    /// </summary>
    public (CscMatrix Lower, CscMatrix Upper) LowerUpper()
    {
        Factorization factored = Factorize("lu");
        int size = Rows;
        var lowerTriplets = new List<(int, int, double)>();

        // With original-row storage the permutation is already folded into L (MATLAB's
        // [L, U] = lu(A) form): column k's unit diagonal sits on the row that supplied pivot k.
        for (int k = 0; k < size; k++)
        {
            lowerTriplets.Add((factored.Permutation[k], k, 1));
            foreach ((int row, double value) in factored.LowerColumns[k]!)
            {
                lowerTriplets.Add((row, k, value));
            }
        }

        var upperTriplets = new List<(int, int, double)>();
        for (int k = 0; k < size; k++)
        {
            foreach ((int row, double value) in factored.UpperColumns[k]!)
            {
                upperTriplets.Add((row, k, value));
            }
        }

        return (FromTriplets(size, size, lowerTriplets), FromTriplets(size, size, upperTriplets));
    }

    /// <summary>
    /// Solves <c>A·x = b</c> through the same factorization, in pivot order. Going through
    /// <see cref="LowerUpper"/> and substituting afterwards would mean recovering the permutation from
    /// L's pattern; keeping it here means the substitutions know it.
    /// </summary>
    public double[] Solve(double[] b)
    {
        if (b.Length != Rows)
        {
            throw new ArgumentException($"A {Rows}x{Cols} matrix cannot be solved against {b.Length} values.");
        }

        Factorization factored = Factorize("\\");
        int n = Rows;

        // Forward substitution against unit lower triangular L, in pivot order.
        var y = new double[n];
        for (int k = 0; k < n; k++)
        {
            y[k] = b[factored.Permutation[k]];
        }

        for (int k = 0; k < n; k++)
        {
            double value = y[k];
            if (value == 0)
            {
                continue;
            }

            foreach ((int row, double multiplier) in factored.LowerColumns[k]!)
            {
                y[factored.WhereIs[row]] -= multiplier * value;
            }
        }

        // Back substitution against U, column by column rather than row by row: U is stored by
        // column, and a row-oriented sweep would have to transpose it first — the same substitution
        // read the other way round.
        var x = new double[n];
        for (int k = n - 1; k >= 0; k--)
        {
            double diagonal = 0;
            foreach ((int row, double value) in factored.UpperColumns[k]!)
            {
                if (row == k)
                {
                    diagonal = value;
                }
            }

            if (diagonal == 0)
            {
                throw new InvalidOperationException("The matrix is singular: the system has no single solution.");
            }

            x[k] = y[k] / diagonal;
            foreach ((int row, double value) in factored.UpperColumns[k]!)
            {
                if (row != k)
                {
                    y[row] -= value * x[k];
                }
            }
        }

        // x is indexed by column, which is the order the answer is asked in: only the rows were
        // permuted, and the forward solve above already undid that.
        return x;
    }

    /// <summary>What one Gilbert–Peierls pass leaves behind: the pivot order and the two factors by column.</summary>
    private readonly record struct Factorization(
        int[] Permutation,
        int[] WhereIs,
        List<(int Row, double Value)>?[] LowerColumns,
        List<(int Row, double Value)>?[] UpperColumns);

    private Factorization Factorize(string name)
    {
        if (Rows != Cols)
        {
            throw new ArgumentException($"{name} needs a square matrix.");
        }

        int n = Rows;
        var permutation = new int[n]; // permutation[k] = original row now acting as row k
        var whereIs = new int[n];     // inverse: current position of original row r
        for (int i = 0; i < n; i++)
        {
            permutation[i] = i;
            whereIs[i] = i;
        }

        var column = new double[n]; // dense working column, indexed by ORIGINAL row

        // L's columns store ORIGINAL row indices: a multiplier belongs to its row forever, and the
        // delayed pivot swaps then need no fix-up — a position-indexed store would silently hand a
        // multiplier to whichever row a later swap moved into that position.
        var lowerColumns = new List<(int Row, double Value)>[n];
        var upperColumns = new List<(int Row, double Value)>[n];

        for (int k = 0; k < n; k++)
        {
            upperColumns[k] = [];
            for (int i = ColumnStarts[k]; i < ColumnStarts[k + 1]; i++)
            {
                column[RowIndices[i]] = Values[i];
            }

            // Eliminate with the already-factored columns, in pivot order. Positions below k are
            // frozen (later swaps only touch k and beyond), so permutation[j] is final here.
            for (int j = 0; j < k; j++)
            {
                double pivotValue = column[permutation[j]];
                if (pivotValue == 0)
                {
                    continue;
                }

                upperColumns[k]!.Add((j, pivotValue));
                foreach ((int row, double value) in lowerColumns[j]!)
                {
                    column[row] -= pivotValue * value;
                }

                column[permutation[j]] = 0;
            }

            // Partial pivot: the largest remaining magnitude becomes row k.
            int bestOriginal = -1;
            double bestMagnitude = 0;
            for (int p = k; p < n; p++)
            {
                double candidate = Math.Abs(column[permutation[p]]);
                if (candidate > bestMagnitude)
                {
                    bestMagnitude = candidate;
                    bestOriginal = p;
                }
            }

            if (bestOriginal < 0)
            {
                // The whole remaining column is zero (structurally singular — sprand leaves the odd
                // empty column). Like MATLAB, factor on with a zero pivot: this L column is empty,
                // U's diagonal entry is zero, and L·U still reassembles A exactly.
                lowerColumns[k] = [];
                continue;
            }

            if (bestOriginal != k)
            {
                (permutation[k], permutation[bestOriginal]) = (permutation[bestOriginal], permutation[k]);
                whereIs[permutation[k]] = k;
                whereIs[permutation[bestOriginal]] = bestOriginal;
            }

            double pivot = column[permutation[k]];
            upperColumns[k]!.Add((k, pivot));
            var lowerColumn = new List<(int Row, double Value)>();
            for (int p = k + 1; p < n; p++)
            {
                int original = permutation[p];
                double value = column[original];
                if (value != 0)
                {
                    lowerColumn.Add((original, value / pivot));
                    column[original] = 0;
                }
            }

            column[permutation[k]] = 0;
            lowerColumns[k] = lowerColumn;
        }

        return new Factorization(permutation, whereIs, lowerColumns, upperColumns);
    }

    /// <summary>
    /// The k eigenvalues of largest magnitude with their Ritz vectors, by Arnoldi over the matvec.
    /// One expansion of a subspace comfortably larger than k — the accuracy the stress scripts need
    /// for extremal eigenvalues, without an implicit-restart machine. The small projected problem
    /// goes through <see cref="LinearAlgebra.Eigen"/>; projected eigenvectors come from inverse
    /// iteration at each Ritz value.
    /// </summary>
    public (System.Numerics.Complex[] Values, System.Numerics.Complex[,] Vectors) LargestEigenpairs(int count)
    {
        int n = Rows;
        if (Rows != Cols)
        {
            throw new ArgumentException("eigs needs a square matrix.");
        }

        count = Math.Min(count, n);
        int subspace = Math.Min(n, Math.Max((2 * count) + 4, 20));

        var basis = new double[subspace + 1][];
        var hessenberg = new double[subspace + 1, subspace];
        var random = new Random(17);
        var start = new double[n];
        for (int i = 0; i < n; i++)
        {
            start[i] = random.NextDouble() - 0.5;
        }

        Normalize(start);
        basis[0] = start;
        int built = 0;
        for (int j = 0; j < subspace; j++)
        {
            double[] w = MultiplyVector(basis[j]);
            for (int i = 0; i <= j; i++)
            {
                double projection = Dot(basis[i], w);
                hessenberg[i, j] = projection;
                for (int t = 0; t < n; t++)
                {
                    w[t] -= projection * basis[i][t];
                }
            }

            double norm = Math.Sqrt(Dot(w, w));
            hessenberg[j + 1, j] = norm;
            built = j + 1;
            if (norm < 1e-12)
            {
                break; // invariant subspace found — the projected problem is exact
            }

            for (int t = 0; t < n; t++)
            {
                w[t] /= norm;
            }

            basis[j + 1] = w;
        }

        var projected = new double[built, built];
        for (int r = 0; r < built; r++)
        {
            for (int c = 0; c < built; c++)
            {
                projected[r, c] = hessenberg[r, c];
            }
        }

        LinearAlgebra.Eigen eigen = LinearAlgebra.Eigen.Factor(projected);
        System.Numerics.Complex[] ritzValues = eigen.Values
            .OrderByDescending(static v => v.Magnitude)
            .Take(count)
            .ToArray();

        var vectors = new System.Numerics.Complex[n, ritzValues.Length];
        for (int k = 0; k < ritzValues.Length; k++)
        {
            System.Numerics.Complex[] y = ProjectedEigenvector(projected, ritzValues[k]);
            for (int t = 0; t < n; t++)
            {
                System.Numerics.Complex sum = 0;
                for (int i = 0; i < built; i++)
                {
                    sum += y[i] * basis[i][t];
                }

                vectors[t, k] = sum;
            }
        }

        return (ritzValues, vectors);
    }

    /// <summary>
    /// The eigenvector of the projected matrix at one Ritz value: two rounds of inverse iteration
    /// with the shift nudged off the exact eigenvalue so the solve stays nonsingular.
    /// </summary>
    private static System.Numerics.Complex[] ProjectedEigenvector(double[,] projected, System.Numerics.Complex value)
    {
        int m = projected.GetLength(0);
        System.Numerics.Complex shift = value + (1e-10 * (1 + value.Magnitude));
        var shifted = new System.Numerics.Complex[m, m];
        for (int r = 0; r < m; r++)
        {
            for (int c = 0; c < m; c++)
            {
                shifted[r, c] = projected[r, c] - (r == c ? shift : 0);
            }
        }

        var y = new System.Numerics.Complex[m];
        for (int i = 0; i < m; i++)
        {
            y[i] = 1.0 / Math.Sqrt(m);
        }

        for (int pass = 0; pass < 2; pass++)
        {
            y = ComplexSolve(shifted, y);
            double norm = 0;
            foreach (System.Numerics.Complex c in y)
            {
                norm += c.Real * c.Real + c.Imaginary * c.Imaginary;
            }

            norm = Math.Sqrt(norm);
            for (int i = 0; i < m; i++)
            {
                y[i] /= norm;
            }
        }

        return y;
    }

    /// <summary>Dense complex Gaussian elimination with partial pivoting — the matrix stays small.</summary>
    private static System.Numerics.Complex[] ComplexSolve(System.Numerics.Complex[,] matrix, System.Numerics.Complex[] rhs)
    {
        int m = rhs.Length;
        var a = (System.Numerics.Complex[,])matrix.Clone();
        var b = (System.Numerics.Complex[])rhs.Clone();
        for (int k = 0; k < m; k++)
        {
            int pivot = k;
            for (int r = k + 1; r < m; r++)
            {
                if (a[r, k].Magnitude > a[pivot, k].Magnitude)
                {
                    pivot = r;
                }
            }

            if (a[pivot, k].Magnitude == 0)
            {
                a[pivot, k] = 1e-300; // keep the iteration alive; the direction still converges
            }

            if (pivot != k)
            {
                for (int c = k; c < m; c++)
                {
                    (a[k, c], a[pivot, c]) = (a[pivot, c], a[k, c]);
                }

                (b[k], b[pivot]) = (b[pivot], b[k]);
            }

            for (int r = k + 1; r < m; r++)
            {
                System.Numerics.Complex factor = a[r, k] / a[k, k];
                for (int c = k; c < m; c++)
                {
                    a[r, c] -= factor * a[k, c];
                }

                b[r] -= factor * b[k];
            }
        }

        for (int r = m - 1; r >= 0; r--)
        {
            System.Numerics.Complex sum = b[r];
            for (int c = r + 1; c < m; c++)
            {
                sum -= a[r, c] * b[c];
            }

            b[r] = sum / a[r, r];
        }

        return b;
    }

    private static double Dot(double[] a, double[] b)
    {
        double sum = 0;
        for (int i = 0; i < a.Length; i++)
        {
            sum += a[i] * b[i];
        }

        return sum;
    }

    private static void Normalize(double[] v)
    {
        double norm = Math.Sqrt(Dot(v, v));
        if (norm > 0)
        {
            for (int i = 0; i < v.Length; i++)
            {
                v[i] /= norm;
            }
        }
    }
}
