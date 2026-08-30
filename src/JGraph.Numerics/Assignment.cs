namespace JGraph.Numerics;

/// <summary>
/// The linear assignment problem: pair every row of a square cost matrix with a distinct column so
/// that the total is as small as it can be.
/// </summary>
/// <remarks>
/// <para>
/// Solved by the shortest-augmenting-path method — one row is added at a time, and a Dijkstra
/// search over reduced costs finds the cheapest way to fit it in, shifting the potentials so that
/// every reduced cost stays non-negative. Because the search runs on reduced costs rather than on
/// the matrix itself, the total is optimal after every row rather than only at the end.
/// </para>
/// <para>
/// An infinite entry is a forbidden pairing, and needs no special handling: its reduced cost is
/// infinite too, so the search never chooses it while any finite path remains. The caller is
/// responsible for handing over a matrix that <em>has</em> a finite perfect matching; without one
/// the search has nowhere to go.
/// </para>
/// </remarks>
public static class Assignment
{
    /// <summary>
    /// The minimum-cost perfect matching of an n-by-n cost matrix.
    /// </summary>
    /// <param name="cost">The matrix, column-major, with no NaN.</param>
    /// <param name="n">Its order.</param>
    /// <returns>
    /// For each column, the row it is matched with, and for each row, the column — both zero-based.
    /// </returns>
    public static (int[] ColumnToRow, int[] RowToColumn) PerfectMatching(double[] cost, int n)
    {
        // One slot of slack at the front of each array is the free row the search starts from, which
        // is what lets the augmenting walk terminate on a test rather than a length.
        var rowPotential = new double[n + 1];
        var columnPotential = new double[n + 1];
        var matchedRow = new int[n + 1];
        var cameFrom = new int[n + 1];
        var distance = new double[n + 1];
        var settled = new bool[n + 1];

        for (int row = 1; row <= n; row++)
        {
            matchedRow[0] = row;
            int column = 0;
            System.Array.Fill(distance, double.PositiveInfinity);
            System.Array.Fill(settled, false);

            do
            {
                settled[column] = true;
                int from = matchedRow[column];
                double step = double.PositiveInfinity;
                int next = 0;
                for (int j = 1; j <= n; j++)
                {
                    if (settled[j])
                    {
                        continue;
                    }

                    double reduced = cost[((j - 1) * n) + from - 1] - rowPotential[from] - columnPotential[j];
                    if (reduced < distance[j])
                    {
                        distance[j] = reduced;
                        cameFrom[j] = column;
                    }

                    if (distance[j] < step)
                    {
                        step = distance[j];
                        next = j;
                    }
                }

                for (int j = 0; j <= n; j++)
                {
                    if (settled[j])
                    {
                        rowPotential[matchedRow[j]] += step;
                        columnPotential[j] -= step;
                    }
                    else
                    {
                        distance[j] -= step;
                    }
                }

                column = next;
            }
            while (matchedRow[column] != 0);

            // Walk the path back, moving each column onto the row that reached it.
            do
            {
                int previous = cameFrom[column];
                matchedRow[column] = matchedRow[previous];
                column = previous;
            }
            while (column != 0);
        }

        var columnToRow = new int[n];
        var rowToColumn = new int[n];
        for (int j = 1; j <= n; j++)
        {
            columnToRow[j - 1] = matchedRow[j] - 1;
            rowToColumn[matchedRow[j] - 1] = j - 1;
        }

        return (columnToRow, rowToColumn);
    }
}
