namespace JGraph.Numerics.Sparse;

/// <summary>
/// The structural verbs of <c>sparfun</c> that are about a matrix's pattern or its diagonals rather
/// than about solving anything: <c>sprank</c>, <c>spdiags</c> and the layout <c>treelayout</c>
/// computes for a tree.
/// </summary>
public static class SparseStructure
{
    /// <summary>
    /// Structural rank: the size of a maximum matching in the bipartite graph of the pattern, by
    /// Hopcroft–Karp.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It answers a question about the pattern alone — how many entries can be put on the diagonal
    /// at once by permuting — so it is an upper bound on the numeric rank and equals it for almost
    /// every choice of values. MATLAB writes it as <c>sum(dmperm(A) &gt; 0)</c>, which is the same
    /// number reached through the same matching.
    /// </para>
    /// <para>
    /// Hopcroft–Karp rather than repeated depth-first augmentation because the phases are what make
    /// it <c>O(sqrt(V)·E)</c>: each phase finds a maximal set of shortest augmenting paths at once,
    /// and there are only <c>O(sqrt(V))</c> distinct shortest-path lengths to work through.
    /// </para>
    /// </remarks>
    public static int StructuralRank(CscMatrix matrix)
    {
        int rows = matrix.Rows;
        int cols = matrix.Cols;
        var forColumn = new int[cols];
        var forRow = new int[rows];
        Array.Fill(forColumn, -1);
        Array.Fill(forRow, -1);

        var distance = new int[cols];
        var queue = new Queue<int>();
        int matched = 0;

        bool Phase()
        {
            queue.Clear();
            for (int c = 0; c < cols; c++)
            {
                if (forColumn[c] < 0)
                {
                    distance[c] = 0;
                    queue.Enqueue(c);
                }
                else
                {
                    distance[c] = int.MaxValue;
                }
            }

            bool reachedFree = false;
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                for (int i = matrix.ColumnStarts[c]; i < matrix.ColumnStarts[c + 1]; i++)
                {
                    if (matrix.Values[i] == 0)
                    {
                        continue;
                    }

                    int partner = forRow[matrix.RowIndices[i]];
                    if (partner < 0)
                    {
                        reachedFree = true;
                    }
                    else if (distance[partner] == int.MaxValue)
                    {
                        distance[partner] = distance[c] + 1;
                        queue.Enqueue(partner);
                    }
                }
            }

            return reachedFree;
        }

        bool Augment(int c)
        {
            for (int i = matrix.ColumnStarts[c]; i < matrix.ColumnStarts[c + 1]; i++)
            {
                if (matrix.Values[i] == 0)
                {
                    continue;
                }

                int r = matrix.RowIndices[i];
                int partner = forRow[r];
                if (partner < 0 || (distance[partner] == distance[c] + 1 && Augment(partner)))
                {
                    forRow[r] = c;
                    forColumn[c] = r;
                    return true;
                }
            }

            distance[c] = int.MaxValue;
            return false;
        }

        while (Phase())
        {
            for (int c = 0; c < cols; c++)
            {
                if (forColumn[c] < 0 && Augment(c))
                {
                    matched++;
                }
            }
        }

        return matched;
    }

    /// <summary>
    /// The nonzero diagonals of a matrix, as <c>[B, d]</c>: the diagonal offsets that hold anything,
    /// ascending, and a <c>min(m,n)</c>-by-p matrix whose columns are those diagonals.
    /// </summary>
    public static (double[] Compact, int Rows, int[] Offsets) ExtractDiagonals(CscMatrix matrix)
    {
        var offsets = new SortedSet<int>();
        for (int c = 0; c < matrix.Cols; c++)
        {
            for (int i = matrix.ColumnStarts[c]; i < matrix.ColumnStarts[c + 1]; i++)
            {
                if (matrix.Values[i] != 0)
                {
                    offsets.Add(c - matrix.RowIndices[i]);
                }
            }
        }

        return (ExtractDiagonals(matrix, [.. offsets]), Math.Min(matrix.Rows, matrix.Cols), [.. offsets]);
    }

    /// <summary>
    /// The named diagonals, in the compact <c>min(m,n)</c>-by-p layout <c>spdiags</c> uses. Where a
    /// diagonal is shorter than the column it lives in, the unused positions are zeros, and which
    /// end of the column they sit at depends on whether the matrix is taller than it is wide —
    /// which is the part of <c>spdiags</c> everyone gets wrong the first time.
    /// </summary>
    public static double[] ExtractDiagonals(CscMatrix matrix, int[] offsets)
    {
        int m = matrix.Rows;
        int n = matrix.Cols;
        int height = Math.Min(m, n);
        var compact = new double[(long)height * offsets.Length];
        for (int k = 0; k < offsets.Length; k++)
        {
            int d = offsets[k];
            int first = m >= n ? Math.Max(1, 1 + d) : Math.Max(1, 1 - d);
            int last = m >= n ? Math.Min(n, m + d) : Math.Min(m, n - d);
            for (int i = first; i <= last; i++)
            {
                int row = m >= n ? i - d : i;
                int col = m >= n ? i : i + d;
                if (row >= 1 && row <= m && col >= 1 && col <= n)
                {
                    compact[((long)k * height) + i - 1] = matrix.At(row - 1, col - 1);
                }
            }
        }

        return compact;
    }

    /// <summary>
    /// An m-by-n matrix built from the compact form: column k of <paramref name="compact"/> becomes
    /// diagonal <paramref name="offsets"/>[k]. When <paramref name="existing"/> is given, those
    /// diagonals replace its own and everything off them is kept.
    /// </summary>
    public static CscMatrix BuildFromDiagonals(double[] compact, int compactRows, int compactCols,
        int[] offsets, int m, int n, CscMatrix? existing)
    {
        var entries = new List<(int Row, int Col, double Value)>();
        for (int k = 0; k < offsets.Length; k++)
        {
            int d = offsets[k];
            for (int i = Math.Max(1, 1 - d); i <= Math.Min(m, n - d); i++)
            {
                // A scalar spreads over every diagonal, a row vector gives one value per diagonal,
                // and a column vector gives one per position — the four shapes MATLAB accepts.
                double value;
                if (compactRows == 1 && compactCols == 1)
                {
                    value = compact[0];
                }
                else if (compactRows == 1)
                {
                    value = k < compactCols ? compact[k * compactRows] : 0;
                }
                else
                {
                    int row = (m >= n ? i + d : i) - 1;
                    int col = compactCols == 1 ? 0 : k;
                    value = row >= 0 && row < compactRows && col < compactCols
                        ? compact[((long)col * compactRows) + row]
                        : 0;
                }

                entries.Add((i - 1, i + d - 1, value));
            }
        }

        if (existing is not null)
        {
            var replaced = new HashSet<int>(offsets);
            for (int c = 0; c < existing.Cols; c++)
            {
                for (int i = existing.ColumnStarts[c]; i < existing.ColumnStarts[c + 1]; i++)
                {
                    int r = existing.RowIndices[i];
                    if (!replaced.Contains(c - r))
                    {
                        entries.Add((r, c, existing.Values[i]));
                    }
                }
            }
        }

        return CscMatrix.FromTriplets(m, n, entries);
    }

    /// <summary>
    /// A parent vector renumbered so that every parent outranks its children, with the permutation
    /// that did it. <c>etree</c> already answers in that form; a tree written down by hand —
    /// <c>[2 4 2 0 6 4 6]</c>, MATLAB's own <c>treeplot</c> example — usually does not, and the
    /// layout sweep below reads the tree in one pass and so depends on it.
    /// </summary>
    /// <remarks>
    /// The fix is a sequence of single moves: find the first node whose parent is numbered below
    /// it, slide that node up to sit just before its parent, and renumber everything the move
    /// displaced. Each move fixes one inversion and creates none, so the loop ends, and the bound
    /// on the number of moves is the number of pairs.
    /// </remarks>
    public static (int[] Parent, int[] Permutation) FixParent(int[] parent)
    {
        int n = parent.Length;
        var a = new List<int>(n);
        var pv = new List<int>(n);
        for (int i = 0; i < n; i++)
        {
            a.Add(parent[i] == 0 ? n + 1 : parent[i]);
            pv.Add(i + 1);
        }

        int moves = 0;
        while (true)
        {
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                if (a[i] < i + 1)
                {
                    k = i + 1;
                    break;
                }
            }

            if (k == 0)
            {
                break;
            }

            int j = a[k - 1];
            var movedA = new List<int>(n);
            var movedP = new List<int>(n);
            for (int i = 0; i < j - 1; i++)
            {
                movedA.Add(a[i]);
                movedP.Add(pv[i]);
            }

            movedA.Add(a[k - 1]);
            movedP.Add(pv[k - 1]);
            for (int i = j - 1; i < k - 1; i++)
            {
                movedA.Add(a[i]);
                movedP.Add(pv[i]);
            }

            for (int i = k; i < n; i++)
            {
                movedA.Add(a[i]);
                movedP.Add(pv[i]);
            }

            var shifted = new bool[n];
            for (int i = 0; i < n; i++)
            {
                shifted[i] = movedA[i] >= j && movedA[i] < k;
            }

            for (int i = 0; i < n; i++)
            {
                if (movedA[i] == k)
                {
                    movedA[i] = j;
                }
            }

            for (int i = 0; i < n; i++)
            {
                if (shifted[i])
                {
                    movedA[i]++;
                }
            }

            a = movedA;
            pv = movedP;
            if (++moves > (n * (n - 1) / 2) + 1)
            {
                throw new ArgumentException("The parent pointers do not describe a tree.");
            }
        }

        var fixedParent = new int[n];
        for (int i = 0; i < n; i++)
        {
            fixedParent[i] = a[i] > n ? 0 : a[i];
        }

        return (fixedParent, [.. pv]);
    }

    /// <summary>
    /// Where to draw each node of a tree so the picture reads: leaves spaced evenly across the unit
    /// interval, every other node centred over the leaves below it, and height proportional to
    /// depth. <c>parent</c> is one-based with 0 for a root, as <c>etree</c> hands it back.
    /// </summary>
    /// <returns>
    /// The coordinates, the height of the tree, and the size of the top-level separator — the four
    /// outputs <c>treelayout</c> has.
    /// </returns>
    public static (double[] X, double[] Y, int Height, int Separator) TreeLayout(int[] parent, int[] postorder)
    {
        int n = parent.Length;
        if (n == 0)
        {
            return ([], [], 0, 0);
        }

        // A dummy root numbered n+1 collects the real roots, so that every node has a parent and
        // the single sweep below never has to ask whether it is at the top.
        var dad = new int[n + 2];
        var isLeaf = new bool[n + 2];
        Array.Fill(isLeaf, true);
        for (int i = 1; i <= n; i++)
        {
            dad[i] = parent[i - 1] == 0 ? n + 1 : parent[i - 1];
            isLeaf[dad[i]] = false;
        }

        var xmin = new int[n + 2];
        var xmax = new int[n + 2];
        var height = new int[n + 2];
        var children = new int[n + 2];
        Array.Fill(xmin, n);
        int leaves = 0;

        for (int i = 0; i < n; i++)
        {
            int node = postorder[i];
            if (isLeaf[node])
            {
                leaves++;
                xmin[node] = leaves;
                xmax[node] = leaves;
            }

            int up = dad[node];
            height[up] = Math.Max(height[up], height[node] + 1);
            xmin[up] = Math.Min(xmin[up], xmin[node]);
            xmax[up] = Math.Max(xmax[up], xmax[node]);
            children[up]++;
        }

        int treeHeight = height[n + 1] - 1;
        double deltaX = 1.0 / (leaves + 1);
        double deltaY = 1.0 / (treeHeight + 2);
        var x = new double[n];
        var y = new double[n];
        for (int i = 1; i <= n; i++)
        {
            x[i - 1] = deltaX * (xmin[i] + xmax[i]) / 2;
            y[i - 1] = deltaY * (height[i] + 1);
        }

        int separator = 0;
        for (int i = n + 1; i >= 1; i--)
        {
            if (children[i] != 1)
            {
                separator = n + 1 - i;
                break;
            }
        }

        return (x, y, treeHeight, separator);
    }
}
