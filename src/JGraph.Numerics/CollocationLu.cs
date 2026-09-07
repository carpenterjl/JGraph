namespace JGraph.Numerics;

/// <summary>
/// The global Jacobian of a collocation system, factored once and solved against many right-hand
/// sides: a sparse LU with partial pivoting, in the natural column order.
/// </summary>
/// <remarks>
/// <para>
/// The matrix a boundary value problem hands the linear solver is block bidiagonal — each mesh
/// interval couples its two end points and nothing else — with the boundary conditions as a border
/// of rows that reach both ends of the mesh, and the unknown parameters as a border of columns.
/// That is a banded matrix with a frame around it, and the frame is what keeps a pure band
/// factorization from applying: a boundary condition may mix <c>y(a)</c> and <c>y(b)</c> in any
/// way it likes, so no ordering of the unknowns makes the whole thing banded.
/// </para>
/// <para>
/// A left-looking factorization in the natural column order is what the structure asks for anyway.
/// Column k reaches back only through the interval that ends at its mesh point, so the elimination
/// stays inside the band, and the only rows that stay live across the whole sweep are the boundary
/// ones — a fill of at most n columns' worth. The factors are kept because the Newton step and
/// every probe of its line search solve against the same matrix; refactoring per probe would cost
/// four times the elimination and answer the same numbers.
/// </para>
/// </remarks>
internal sealed class CollocationLu
{
    private const double Epsilon = 2.220446049250313e-16;

    private readonly int _n;
    private readonly int[] _lowerStart;
    private readonly int[] _lowerRow;
    private readonly double[] _lowerValue;
    private readonly int[] _upperStart;
    private readonly int[] _upperRow;
    private readonly double[] _upperValue;
    private readonly int[] _pivotOfRow;   // original row -> its position in pivot order
    private readonly double[] _diagonal;  // U's diagonal, by pivot position

    private CollocationLu(int n, int[] lowerStart, int[] lowerRow, double[] lowerValue,
        int[] upperStart, int[] upperRow, double[] upperValue, int[] pivotOfRow, double[] diagonal,
        double oneNorm)
    {
        _n = n;
        _lowerStart = lowerStart;
        _lowerRow = lowerRow;
        _lowerValue = lowerValue;
        _upperStart = upperStart;
        _upperRow = upperRow;
        _upperValue = upperValue;
        _pivotOfRow = pivotOfRow;
        _diagonal = diagonal;
        OneNorm = oneNorm;
    }

    /// <summary>The 1-norm of the matrix that was factored.</summary>
    public double OneNorm { get; }

    /// <summary>
    /// Factors the square matrix given by <paramref name="triplets"/>, whose duplicate entries are
    /// summed. Answers null when a column has no usable pivot — the singular Jacobian a boundary
    /// value problem refuses outright.
    /// </summary>
    public static CollocationLu? Factor(int n, IReadOnlyList<(int Row, int Col, double Value)> triplets)
    {
        // Compress by column first: the factorization walks column by column, and the collocation
        // assembly produces its entries interval by interval rather than in any order.
        var counts = new int[n + 1];
        foreach ((int _, int col, double _) in triplets)
        {
            counts[col + 1]++;
        }

        for (int j = 0; j < n; j++)
        {
            counts[j + 1] += counts[j];
        }

        var fill = (int[])counts.Clone();
        var rowOf = new int[triplets.Count];
        var valueOf = new double[triplets.Count];
        foreach ((int row, int col, double value) in triplets)
        {
            int at = fill[col]++;
            rowOf[at] = row;
            valueOf[at] = value;
        }

        // Duplicates summed, and the column's 1-norm taken while the column is in hand.
        var seen = new int[n];
        Array.Fill(seen, -1);
        var accumulated = new double[n];
        var columnStart = new int[n + 1];
        var columnRow = new int[triplets.Count];
        var columnValue = new double[triplets.Count];
        int packed = 0;
        double oneNorm = 0;
        for (int j = 0; j < n; j++)
        {
            columnStart[j] = packed;
            for (int p = counts[j]; p < counts[j + 1]; p++)
            {
                int i = rowOf[p];
                if (seen[i] != j)
                {
                    seen[i] = j;
                    accumulated[i] = valueOf[p];
                    columnRow[packed++] = i;
                }
                else
                {
                    accumulated[i] += valueOf[p];
                }
            }

            double sum = 0;
            for (int p = columnStart[j]; p < packed; p++)
            {
                columnValue[p] = accumulated[columnRow[p]];
                sum += Math.Abs(columnValue[p]);
            }

            oneNorm = Math.Max(oneNorm, sum);
        }

        columnStart[n] = packed;

        var pivotOfRow = new int[n];
        Array.Fill(pivotOfRow, -1);
        var pivotRow = new int[n];
        var diagonal = new double[n];

        var lowerStart = new int[n + 1];
        var upperStart = new int[n + 1];
        var lowerRow = new List<int>(packed * 2);
        var lowerValue = new List<double>(packed * 2);
        var upperRow = new List<int>(packed * 2);
        var upperValue = new List<double>(packed * 2);

        var work = new double[n];
        var reach = new int[n];
        var stack = new int[n];
        var path = new int[n];
        var visited = new bool[n];

        for (int k = 0; k < n; k++)
        {
            lowerStart[k] = lowerRow.Count;
            upperStart[k] = upperRow.Count;

            // x = L \ A(:,k), on the pattern reachable from column k's nonzeros through L.
            int top = SparseTriangularSolve(n, columnStart, columnRow, columnValue, k,
                lowerStart, lowerRow, lowerValue, pivotOfRow, work, reach, stack, path, visited);

            int pivot = -1;
            double best = 0;
            for (int p = top; p < n; p++)
            {
                int i = reach[p];
                if (pivotOfRow[i] < 0)
                {
                    double magnitude = Math.Abs(work[i]);
                    if (magnitude > best)
                    {
                        best = magnitude;
                        pivot = i;
                    }
                }
                else
                {
                    upperRow.Add(pivotOfRow[i]);
                    upperValue.Add(work[i]);
                }
            }

            if (pivot < 0 || best == 0)
            {
                return null;
            }

            double value2 = work[pivot];
            diagonal[k] = value2;
            upperRow.Add(k);
            upperValue.Add(value2);
            pivotOfRow[pivot] = k;
            pivotRow[k] = pivot;
            lowerRow.Add(pivot);
            lowerValue.Add(1);
            for (int p = top; p < n; p++)
            {
                int i = reach[p];
                if (pivotOfRow[i] < 0)
                {
                    lowerRow.Add(i);
                    lowerValue.Add(work[i] / value2);
                }

                work[i] = 0;
            }
        }

        lowerStart[n] = lowerRow.Count;
        upperStart[n] = upperRow.Count;

        // L's rows are recorded as the matrix's own; renumbering them into pivot order is what
        // makes L unit lower triangular, so the substitutions can run straight down it.
        var lowerRows = lowerRow.ToArray();
        for (int p = 0; p < lowerRows.Length; p++)
        {
            lowerRows[p] = pivotOfRow[lowerRows[p]];
        }

        return new CollocationLu(n, lowerStart, lowerRows, lowerValue.ToArray(),
            upperStart, upperRow.ToArray(), upperValue.ToArray(), pivotOfRow, diagonal, oneNorm);
    }

    /// <summary>Solves <c>A·x = b</c> against the factors.</summary>
    public double[] Solve(double[] b)
    {
        var x = new double[_n];
        for (int i = 0; i < _n; i++)
        {
            x[_pivotOfRow[i]] = b[i];
        }

        for (int k = 0; k < _n; k++)
        {
            double value = x[k];
            if (value == 0)
            {
                continue;
            }

            for (int p = _lowerStart[k] + 1; p < _lowerStart[k + 1]; p++)
            {
                x[_lowerRow[p]] -= _lowerValue[p] * value;
            }
        }

        for (int k = _n - 1; k >= 0; k--)
        {
            double value = x[k] / _diagonal[k];
            x[k] = value;
            if (value == 0)
            {
                continue;
            }

            for (int p = _upperStart[k]; p < _upperStart[k + 1] - 1; p++)
            {
                x[_upperRow[p]] -= _upperValue[p] * value;
            }
        }

        return x;
    }

    /// <summary>Solves <c>Aᵀ·x = b</c> against the same factors, which the condition estimate needs.</summary>
    public double[] SolveTranspose(double[] b)
    {
        var z = (double[])b.Clone();

        // Uᵀ is lower triangular in pivot order, and its diagonal is U's own.
        for (int k = 0; k < _n; k++)
        {
            double sum = z[k];
            for (int p = _upperStart[k]; p < _upperStart[k + 1] - 1; p++)
            {
                sum -= _upperValue[p] * z[_upperRow[p]];
            }

            z[k] = sum / _diagonal[k];
        }

        for (int k = _n - 1; k >= 0; k--)
        {
            double sum = z[k];
            for (int p = _lowerStart[k] + 1; p < _lowerStart[k + 1]; p++)
            {
                sum -= _lowerValue[p] * z[_lowerRow[p]];
            }

            z[k] = sum;
        }

        var x = new double[_n];
        for (int i = 0; i < _n; i++)
        {
            x[i] = z[_pivotOfRow[i]];
        }

        return x;
    }

    /// <summary>
    /// MATLAB's <c>rcond</c>: one over the product of the matrix's 1-norm and an estimate of its
    /// inverse's, by Hager's iteration on the factors that are already in hand.
    /// </summary>
    public double ReciprocalCondition()
    {
        if (OneNorm == 0)
        {
            return 0;
        }

        var v = new double[_n];
        Array.Fill(v, 1.0 / _n);
        double estimate = 0;
        var sign = new double[_n];
        for (int iteration = 0; iteration < 5; iteration++)
        {
            double[] w = Solve(v);
            double sum = 0;
            for (int i = 0; i < _n; i++)
            {
                sum += Math.Abs(w[i]);
                sign[i] = w[i] >= 0 ? 1 : -1;
            }

            if (sum <= estimate)
            {
                break;
            }

            estimate = sum;
            double[] z = SolveTranspose(sign);
            int at = 0;
            double largest = 0;
            for (int i = 0; i < _n; i++)
            {
                if (Math.Abs(z[i]) > largest)
                {
                    largest = Math.Abs(z[i]);
                    at = i;
                }
            }

            Array.Clear(v);
            v[at] = 1;
        }

        double rcond = estimate > 0 ? 1 / (OneNorm * estimate) : 0;
        return double.IsNaN(rcond) ? 0 : Math.Min(1, rcond);
    }

    /// <summary>The floor a boundary value solver calls ill-conditioned — MATLAB's <c>eps</c>.</summary>
    public const double IllConditioned = Epsilon;

    /// <summary>
    /// <c>x = L \ A(:,k)</c> on the pattern alone: a depth-first walk of L's graph from column k's
    /// own nonzeros gives the columns of L that touch the answer, in an order that lets the
    /// substitution run once through them.
    /// </summary>
    private static int SparseTriangularSolve(int n, int[] columnStart, int[] columnRow, double[] columnValue,
        int k, int[] lowerStart, List<int> lowerRow, List<double> lowerValue, int[] pivotOfRow,
        double[] work, int[] reach, int[] stack, int[] path, bool[] visited)
    {
        int top = n;
        for (int p = columnStart[k]; p < columnStart[k + 1]; p++)
        {
            int row = columnRow[p];
            if (visited[row])
            {
                continue;
            }

            // Iterative depth-first search, so a long chain of intervals cannot overflow the stack.
            int head = 0;
            stack[0] = row;
            while (head >= 0)
            {
                int node = stack[head];
                int column = pivotOfRow[node];
                if (!visited[node])
                {
                    visited[node] = true;
                    path[head] = column < 0 ? 0 : lowerStart[column] + 1;
                }

                bool descended = false;
                if (column >= 0)
                {
                    int end = lowerStart[column + 1];
                    for (int q = path[head]; q < end; q++)
                    {
                        int next = lowerRow[q];
                        if (visited[next])
                        {
                            continue;
                        }

                        path[head] = q + 1;
                        stack[++head] = next;
                        descended = true;
                        break;
                    }
                }

                if (!descended)
                {
                    head--;
                    reach[--top] = node;
                }
            }
        }

        for (int p = top; p < n; p++)
        {
            visited[reach[p]] = false;
            work[reach[p]] = 0;
        }

        for (int p = columnStart[k]; p < columnStart[k + 1]; p++)
        {
            work[columnRow[p]] = columnValue[p];
        }

        for (int p = top; p < n; p++)
        {
            int row = reach[p];
            int column = pivotOfRow[row];
            if (column < 0)
            {
                continue;
            }

            double value = work[row];
            if (value == 0)
            {
                continue;
            }

            for (int q = lowerStart[column] + 1; q < lowerStart[column + 1]; q++)
            {
                work[lowerRow[q]] -= lowerValue[q] * value;
            }
        }

        return top;
    }
}
