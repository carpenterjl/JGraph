using JGraph.Numerics.Sparse;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The sparse names M42 left out (M66 wave C): the constructors <c>speye</c> and <c>nzmax</c>, the
/// fill-reducing orderings <c>symrcm</c>, <c>amd</c> and <c>dissect</c>, the structure verbs
/// <c>dmperm</c>, <c>etree</c> and <c>symbfact</c>, and the incomplete factorizations <c>ichol</c>
/// and <c>ilu</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every ordering here answers a permutation and none of them answers a factorization, which is what
/// makes them cheap to be honest about: a permutation is either a permutation or it is not, and a
/// script can check that much itself. What a script cannot check is <em>how good</em> the ordering is,
/// so where the algorithm differs from MATLAB's the difference is written down rather than implied —
/// <c>amd</c> here is an exact minimum-degree ordering, not the approximate one the name is short for,
/// and <c>dissect</c> bisects on breadth-first level sets rather than through a multilevel partitioner.
/// Both produce valid, useful orderings; neither produces MATLAB's, and a script that compares
/// permutations element by element will see the difference.
/// </para>
/// <para>
/// All of them work on the symmetric pattern of the matrix — the pattern of <c>A + Aᵀ</c> — because
/// that is what an ordering is about: which entries can create fill, not what they hold.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    private static void RegisterSparseOrderingBuiltins(JgsEnvironment env, JgsDialect dialect)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        Define("speye", (args, line, col) =>
        {
            ArityRange("speye", args, 0, 2, line, col);
            int rows = args.Count == 0 ? 1 : Count("speye", args, 0, line, col);
            int cols = args.Count switch
            {
                0 => 1,
                1 => rows,
                _ => Count("speye", args, 1, line, col),
            };

            var triplets = new List<(int, int, double)>(Math.Min(rows, cols));
            for (int i = 0; i < Math.Min(rows, cols); i++)
            {
                triplets.Add((i, i, 1));
            }

            return JgsValue.Sparse(CscMatrix.FromTriplets(Math.Max(rows, 0), Math.Max(cols, 0), triplets));
        });

        Define("nzmax", (args, line, col) =>
        {
            Arity("nzmax", args, 1, line, col);

            // MATLAB reports the space allocated, which can exceed the nonzero count after a matrix
            // has been edited. Nothing here edits in place — every operation builds a new matrix that
            // is exactly as large as it needs to be — so the two numbers are always the same one.
            return JgsValue.Number(args[0].Type == JgsType.Sparse
                ? args[0].AsSparse.NonZeroCount
                : CountNonZeros("nzmax", args[0], line, col));
        });

        Define("symrcm", (args, line, col) =>
            PermutationValue(ReverseCuthillMcKee(SymmetricPattern(Sparse("symrcm", args, line, col))), dialect));

        Define("amd", (args, line, col) =>
            PermutationValue(MinimumDegree(SymmetricPattern(Sparse("amd", args, line, col))), dialect));

        Define("symamd", (args, line, col) =>
            PermutationValue(MinimumDegree(SymmetricPattern(Sparse("symamd", args, line, col))), dialect));

        Define("dissect", (args, line, col) =>
            PermutationValue(NestedDissection(SymmetricPattern(Sparse("dissect", args, line, col))), dialect));

        Define("dmperm", (args, line, col) =>
        {
            CscMatrix matrix = Sparse("dmperm", args, line, col);
            int[] matched = MaximumMatching(matrix);
            var rows = new double[matrix.Cols];
            for (int c = 0; c < matrix.Cols; c++)
            {
                // An unmatched column has no row to put on the diagonal, and MATLAB writes that as a
                // zero rather than leaving the position out.
                rows[c] = matched[c] < 0 ? 0 : matched[c] + dialect.IndexBase;
            }

            return Numbers(rows);
        });

        JgsValue[] EliminationTree(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
        {
            CscMatrix matrix = Sparse("etree", args, line, col);
            RequireSquare("etree", matrix, line, col);
            int[] parent = EtreeOf(matrix);

            var parents = new double[parent.Length];
            for (int i = 0; i < parent.Length; i++)
            {
                parents[i] = parent[i] < 0 ? 0 : parent[i] + dialect.IndexBase;
            }

            return Outputs(wanted, Numbers(parents), PermutationValue(Postorder(parent), dialect));
        }

        Define("etree", (args, line, col) => EliminationTree(args, 1, line, col)[0], EliminationTree);

        Define("symbfact", (args, line, col) =>
        {
            CscMatrix matrix = Sparse("symbfact", args, line, col);
            RequireSquare("symbfact", matrix, line, col);
            int[][] pattern = SymmetricPattern(matrix);
            int[] parent = EtreeOf(matrix);
            return Numbers(Array.ConvertAll(FactorCounts(pattern, parent), static c => (double)c));
        });

        Define("ichol", (args, line, col) =>
        {
            CscMatrix matrix = Sparse("ichol", args, line, col);
            RequireSquare("ichol", matrix, line, col);
            return JgsValue.Sparse(IncompleteCholesky(matrix, line, col));
        });

        JgsValue[] IncompleteLu(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
        {
            CscMatrix matrix = Sparse("ilu", args, line, col);
            RequireSquare("ilu", matrix, line, col);
            (CscMatrix lower, CscMatrix upper) = IncompleteLuOf(matrix, line, col);
            return Outputs(wanted, JgsValue.Sparse(lower), JgsValue.Sparse(upper));
        }

        Define("ilu", (args, line, col) => IncompleteLu(args, 1, line, col)[0], IncompleteLu);
    }

    /// <summary>One argument as a sparse matrix, whether it arrived sparse or dense.</summary>
    private static CscMatrix Sparse(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity(name, args, 1, line, col);
        return args[0].Type == JgsType.Sparse ? args[0].AsSparse : CscFromDense(name, args[0], line, col);
    }

    private static void RequireSquare(string name, CscMatrix matrix, int line, int col)
    {
        if (matrix.Rows != matrix.Cols)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} needs a square matrix, but got {matrix.Rows}x{matrix.Cols}.");
        }
    }

    private static int CountNonZeros(string name, JgsValue value, int line, int col)
    {
        int count = 0;
        foreach (double v in FlattenColumnMajor(name, value, line, col))
        {
            if (v != 0)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>A permutation as the dialect numbers it, laid out as a row.</summary>
    private static JgsValue PermutationValue(int[] order, JgsDialect dialect)
    {
        var numbers = new double[order.Length];
        for (int i = 0; i < order.Length; i++)
        {
            numbers[i] = order[i] + dialect.IndexBase;
        }

        return Numbers(numbers);
    }

    /// <summary>
    /// The undirected adjacency of the matrix's pattern — <c>A + Aᵀ</c> without the diagonal. Every
    /// ordering below wants this and not the values: which entries could create fill during
    /// elimination is a question about the pattern alone.
    /// </summary>
    private static int[][] SymmetricPattern(CscMatrix matrix)
    {
        int n = Math.Max(matrix.Rows, matrix.Cols);
        var neighbours = new HashSet<int>[n];
        for (int i = 0; i < n; i++)
        {
            neighbours[i] = [];
        }

        for (int c = 0; c < matrix.Cols; c++)
        {
            for (int i = matrix.ColumnStarts[c]; i < matrix.ColumnStarts[c + 1]; i++)
            {
                int r = matrix.RowIndices[i];
                if (r == c)
                {
                    continue;
                }

                neighbours[r].Add(c);
                neighbours[c].Add(r);
            }
        }

        var result = new int[n][];
        for (int i = 0; i < n; i++)
        {
            result[i] = [.. neighbours[i]];
            Array.Sort(result[i]);
        }

        return result;
    }

    // --- symrcm -------------------------------------------------------------------------------

    /// <summary>
    /// Cuthill–McKee, reversed. Breadth-first from a low-degree start, taking each level's neighbours
    /// in increasing degree, gathers the nonzeros towards the diagonal; reversing the result is what
    /// turns a wide-at-the-end profile into a narrow one, which is the whole trick and the reason the
    /// reversal is in the name.
    /// </summary>
    private static int[] ReverseCuthillMcKee(int[][] neighbours)
    {
        int n = neighbours.Length;
        var seen = new bool[n];
        var order = new List<int>(n);

        while (order.Count < n)
        {
            int start = -1;
            for (int i = 0; i < n; i++)
            {
                if (!seen[i] && (start < 0 || neighbours[i].Length < neighbours[start].Length))
                {
                    start = i;
                }
            }

            if (start < 0)
            {
                break;
            }

            var queue = new Queue<int>();
            queue.Enqueue(start);
            seen[start] = true;
            while (queue.Count > 0)
            {
                int at = queue.Dequeue();
                order.Add(at);

                int[] next = [.. neighbours[at].Where(v => !seen[v])];
                Array.Sort(next, (a, b) =>
                {
                    int byDegree = neighbours[a].Length.CompareTo(neighbours[b].Length);
                    return byDegree != 0 ? byDegree : a.CompareTo(b);
                });

                foreach (int v in next)
                {
                    if (!seen[v])
                    {
                        seen[v] = true;
                        queue.Enqueue(v);
                    }
                }
            }
        }

        order.Reverse();
        return [.. order];
    }

    // --- amd ----------------------------------------------------------------------------------

    /// <summary>
    /// Minimum degree: eliminate the node with the fewest remaining neighbours, join those neighbours
    /// into a clique, and repeat. This is the exact ordering the approximate one approximates — the
    /// approximation exists to make the degree updates cheap on very large matrices, and at the sizes
    /// a script builds interactively the exact version is both affordable and better.
    /// </summary>
    private static int[] MinimumDegree(int[][] neighbours)
    {
        int n = neighbours.Length;
        var live = new HashSet<int>[n];
        for (int i = 0; i < n; i++)
        {
            live[i] = [.. neighbours[i]];
        }

        var eliminated = new bool[n];
        var order = new int[n];
        for (int step = 0; step < n; step++)
        {
            int pick = -1;
            for (int i = 0; i < n; i++)
            {
                if (!eliminated[i] && (pick < 0 || live[i].Count < live[pick].Count))
                {
                    pick = i;
                }
            }

            order[step] = pick;
            eliminated[pick] = true;

            int[] adjacent = [.. live[pick]];
            foreach (int a in adjacent)
            {
                live[a].Remove(pick);
                foreach (int b in adjacent)
                {
                    if (a != b)
                    {
                        live[a].Add(b);
                    }
                }
            }

            live[pick].Clear();
        }

        return order;
    }

    // --- dissect ------------------------------------------------------------------------------

    /// <summary>
    /// Nested dissection: split the graph with a small separator, order each half first and the
    /// separator last, and recurse. The separator here is a breadth-first level set — the cheapest
    /// thing that is genuinely a separator — rather than the multilevel partition a dedicated library
    /// would compute.
    /// </summary>
    private static int[] NestedDissection(int[][] neighbours)
    {
        var order = new List<int>(neighbours.Length);
        var remaining = new HashSet<int>(Enumerable.Range(0, neighbours.Length));
        while (remaining.Count > 0)
        {
            var component = new List<int>();
            var queue = new Queue<int>();
            int seed = remaining.First();
            queue.Enqueue(seed);
            remaining.Remove(seed);
            while (queue.Count > 0)
            {
                int at = queue.Dequeue();
                component.Add(at);
                foreach (int v in neighbours[at])
                {
                    if (remaining.Remove(v))
                    {
                        queue.Enqueue(v);
                    }
                }
            }

            Dissect(neighbours, component, order);
        }

        return [.. order];
    }

    private static void Dissect(int[][] neighbours, List<int> component, List<int> into)
    {
        // Below a handful of nodes there is nothing left to separate and the recursion would only
        // pay for itself in stack frames.
        if (component.Count <= 8)
        {
            into.AddRange(component);
            return;
        }

        var inside = new HashSet<int>(component);
        var level = new Dictionary<int, int>();
        var queue = new Queue<int>();
        int start = component[0];
        foreach (int v in component)
        {
            if (neighbours[v].Count(inside.Contains) < neighbours[start].Count(inside.Contains))
            {
                start = v;
            }
        }

        queue.Enqueue(start);
        level[start] = 0;
        var byLevel = new List<List<int>> { new() { start } };
        while (queue.Count > 0)
        {
            int at = queue.Dequeue();
            foreach (int v in neighbours[at])
            {
                if (!inside.Contains(v) || level.ContainsKey(v))
                {
                    continue;
                }

                level[v] = level[at] + 1;
                while (byLevel.Count <= level[v])
                {
                    byLevel.Add([]);
                }

                byLevel[level[v]].Add(v);
                queue.Enqueue(v);
            }
        }

        // A graph one level deep has no middle to cut, and a component the search could not cover is
        // not connected the way the caller believed. Either way, order it as it stands.
        if (byLevel.Count < 3 || level.Count != component.Count)
        {
            into.AddRange(component);
            return;
        }

        int middle = byLevel.Count / 2;
        var near = new List<int>();
        var far = new List<int>();
        foreach (int v in component)
        {
            if (level[v] < middle)
            {
                near.Add(v);
            }
            else if (level[v] > middle)
            {
                far.Add(v);
            }
        }

        Dissect(neighbours, near, into);
        Dissect(neighbours, far, into);
        into.AddRange(byLevel[middle]);
    }

    // --- dmperm -------------------------------------------------------------------------------

    /// <summary>
    /// A maximum matching between columns and rows, by augmenting paths. <c>A(p, :)</c> then has as
    /// many nonzeros on its diagonal as the matrix structurally allows, which is what makes
    /// <c>dmperm</c> the test for structural singularity.
    /// </summary>
    private static int[] MaximumMatching(CscMatrix matrix)
    {
        var columnOf = new int[matrix.Rows];
        Array.Fill(columnOf, -1);
        var rowOf = new int[matrix.Cols];
        Array.Fill(rowOf, -1);

        for (int c = 0; c < matrix.Cols; c++)
        {
            var visited = new bool[matrix.Rows];
            if (Augment(matrix, c, visited, columnOf))
            {
                for (int r = 0; r < matrix.Rows; r++)
                {
                    if (columnOf[r] >= 0)
                    {
                        rowOf[columnOf[r]] = r;
                    }
                }
            }
        }

        return rowOf;
    }

    private static bool Augment(CscMatrix matrix, int column, bool[] visited, int[] columnOf)
    {
        for (int i = matrix.ColumnStarts[column]; i < matrix.ColumnStarts[column + 1]; i++)
        {
            int row = matrix.RowIndices[i];
            if (visited[row])
            {
                continue;
            }

            visited[row] = true;
            if (columnOf[row] < 0 || Augment(matrix, columnOf[row], visited, columnOf))
            {
                columnOf[row] = column;
                return true;
            }
        }

        return false;
    }

    // --- etree and symbfact -------------------------------------------------------------------

    /// <summary>
    /// The elimination tree: the parent of column k is the first column its elimination will touch.
    /// Built by walking each entry up the partly-formed tree with path compression, which is what
    /// makes the whole thing close to linear in the number of nonzeros.
    /// </summary>
    private static int[] EtreeOf(CscMatrix matrix)
    {
        int n = matrix.Cols;
        var parent = new int[n];
        var ancestor = new int[n];
        Array.Fill(parent, -1);
        Array.Fill(ancestor, -1);

        for (int k = 0; k < n; k++)
        {
            for (int p = matrix.ColumnStarts[k]; p < matrix.ColumnStarts[k + 1]; p++)
            {
                int i = matrix.RowIndices[p];
                while (i >= 0 && i < k)
                {
                    int next = ancestor[i];
                    ancestor[i] = k;
                    if (next < 0)
                    {
                        parent[i] = k;
                        break;
                    }

                    i = next;
                }
            }
        }

        return parent;
    }

    /// <summary>A postorder of the elimination tree: children before their parent, left to right.</summary>
    private static int[] Postorder(int[] parent)
    {
        int n = parent.Length;
        var children = new List<int>[n];
        var roots = new List<int>();
        for (int i = 0; i < n; i++)
        {
            children[i] = [];
        }

        for (int i = 0; i < n; i++)
        {
            if (parent[i] < 0)
            {
                roots.Add(i);
            }
            else
            {
                children[parent[i]].Add(i);
            }
        }

        var order = new List<int>(n);
        var stack = new Stack<(int Node, bool Expanded)>();
        for (int i = roots.Count - 1; i >= 0; i--)
        {
            stack.Push((roots[i], false));
        }

        while (stack.Count > 0)
        {
            (int node, bool expanded) = stack.Pop();
            if (expanded)
            {
                order.Add(node);
                continue;
            }

            stack.Push((node, true));
            for (int i = children[node].Count - 1; i >= 0; i--)
            {
                stack.Push((children[node][i], false));
            }
        }

        return [.. order];
    }

    /// <summary>
    /// How many nonzeros each column of the Cholesky factor will hold, counted without forming it:
    /// each row's pattern is a subtree of the elimination tree, and walking that subtree once per row
    /// counts every column it contributes to.
    /// </summary>
    private static int[] FactorCounts(int[][] pattern, int[] parent)
    {
        int n = parent.Length;
        var count = new int[n];
        var mark = new int[n];
        Array.Fill(count, 1); // the diagonal, which every column has
        Array.Fill(mark, -1);

        for (int i = 0; i < n; i++)
        {
            mark[i] = i;
            foreach (int k in pattern[i])
            {
                if (k >= i)
                {
                    continue;
                }

                for (int j = k; j >= 0 && mark[j] != i; j = parent[j])
                {
                    mark[j] = i;
                    count[j]++;
                }
            }
        }

        return count;
    }

    // --- ichol and ilu ------------------------------------------------------------------------

    /// <summary>
    /// Incomplete Cholesky with no fill: the exact factorization restricted to the pattern the matrix
    /// already has. Everything it would have created outside that pattern is dropped, which is why the
    /// result is a preconditioner rather than a factorization — <c>L·Lᵀ</c> is near <c>A</c>, not
    /// equal to it.
    /// </summary>
    private static CscMatrix IncompleteCholesky(CscMatrix matrix, int line, int col)
    {
        int n = matrix.Rows;
        Dictionary<int, double>[] rows = LowerRowsOf(matrix);

        for (int k = 0; k < n; k++)
        {
            if (!rows[k].TryGetValue(k, out double diagonal))
            {
                throw new JgsRuntimeException(line, col,
                    $"ichol: row {k + 1} has no diagonal entry, so there is nothing to take a square root of.");
            }

            if (diagonal <= 0)
            {
                throw new JgsRuntimeException(line, col,
                    "ichol needs a positive definite matrix; the factorization met a non-positive pivot.");
            }

            double pivot = Math.Sqrt(diagonal);
            rows[k][k] = pivot;

            // Column k of the factor, scaled by the pivot. Right-looking: this column is finished
            // here, and what remains is to subtract its outer product from the rest — restricted to
            // the pattern, which is the "incomplete" in the name.
            var column = new List<(int Row, double Value)>();
            for (int i = k + 1; i < n; i++)
            {
                if (rows[i].TryGetValue(k, out double under))
                {
                    double scaled = under / pivot;
                    rows[i][k] = scaled;
                    column.Add((i, scaled));
                }
            }

            foreach ((int j, double atJ) in column)
            {
                foreach ((int i, double atI) in column)
                {
                    if (i >= j && rows[i].ContainsKey(j))
                    {
                        rows[i][j] -= atI * atJ;
                    }
                }
            }
        }

        var triplets = new List<(int, int, double)>();
        for (int i = 0; i < n; i++)
        {
            foreach ((int j, double v) in rows[i])
            {
                if (j <= i && v != 0)
                {
                    triplets.Add((i, j, v));
                }
            }
        }

        return CscMatrix.FromTriplets(n, n, triplets);
    }

    /// <summary>
    /// Incomplete LU with no fill: the same restriction applied to the unsymmetric case, in the
    /// row-oriented order that makes the pattern test a lookup rather than a search.
    /// </summary>
    private static (CscMatrix Lower, CscMatrix Upper) IncompleteLuOf(CscMatrix matrix, int line, int col)
    {
        int n = matrix.Rows;
        Dictionary<int, double>[] rows = AllRowsOf(matrix);

        for (int i = 1; i < n; i++)
        {
            foreach (int k in rows[i].Keys.Where(k => k < i).OrderBy(static k => k).ToArray())
            {
                if (!rows[k].TryGetValue(k, out double pivot) || pivot == 0)
                {
                    throw new JgsRuntimeException(line, col,
                        $"ilu: the pivot in row {k + 1} is zero, so this matrix needs a full factorization.");
                }

                double multiplier = rows[i][k] / pivot;
                rows[i][k] = multiplier;
                if (multiplier == 0)
                {
                    continue;
                }

                foreach (int j in rows[i].Keys.Where(j => j > k).ToArray())
                {
                    if (rows[k].TryGetValue(j, out double above))
                    {
                        rows[i][j] -= multiplier * above;
                    }
                }
            }
        }

        var lower = new List<(int, int, double)>();
        var upper = new List<(int, int, double)>();
        for (int i = 0; i < n; i++)
        {
            lower.Add((i, i, 1));
            foreach ((int j, double v) in rows[i])
            {
                if (v == 0)
                {
                    continue;
                }

                if (j < i)
                {
                    lower.Add((i, j, v));
                }
                else
                {
                    upper.Add((i, j, v));
                }
            }
        }

        return (CscMatrix.FromTriplets(n, n, lower), CscMatrix.FromTriplets(n, n, upper));
    }

    /// <summary>The matrix by rows, keeping only the lower triangle — what a Cholesky factor lives in.</summary>
    private static Dictionary<int, double>[] LowerRowsOf(CscMatrix matrix)
    {
        Dictionary<int, double>[] rows = AllRowsOf(matrix);
        for (int i = 0; i < rows.Length; i++)
        {
            foreach (int j in rows[i].Keys.ToArray())
            {
                if (j > i)
                {
                    rows[i].Remove(j);
                }
            }
        }

        return rows;
    }

    private static Dictionary<int, double>[] AllRowsOf(CscMatrix matrix)
    {
        var rows = new Dictionary<int, double>[matrix.Rows];
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = [];
        }

        for (int c = 0; c < matrix.Cols; c++)
        {
            for (int p = matrix.ColumnStarts[c]; p < matrix.ColumnStarts[c + 1]; p++)
            {
                rows[matrix.RowIndices[p]][c] = matrix.Values[p];
            }
        }

        return rows;
    }
}
