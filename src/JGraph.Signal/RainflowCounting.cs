using System;

namespace JGraph.Signal;

/// <summary>One counted cycle: how much of one it is, its range, its mean and where it ran.</summary>
public readonly record struct LoadCycle(double Count, double Range, double Mean, int Start, int End);

/// <summary>
/// MATLAB's <c>rainflow</c>: the ASTM E1049 cycle count, which turns a load history into a list of
/// closed hysteresis loops.
/// </summary>
/// <remarks>
/// The algorithm reads the reversals one at a time onto a stack and, whenever the range between the
/// last two is at least as large as the one before it, closes that inner range off as a cycle and
/// takes it off the stack. A range closed from a stack of exactly three is half a cycle — the outer
/// half is still open — and one closed from a deeper stack is a whole one.
/// </remarks>
public static class RainflowCounting
{
    /// <summary>
    /// The turning points of a load history, with the first and last samples always kept.
    /// </summary>
    public static int[] Extrema(double[] x)
    {
        ArgumentNullException.ThrowIfNull(x);
        var index = new double[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            index[i] = i + 1;
        }

        var negated = new double[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            negated[i] = -x[i];
        }

        var criteria = new PeakFinding.Criteria();
        int[] peaks = PeakFinding.Find(x, index, criteria).Indices;
        int[] valleys = PeakFinding.Find(negated, index, new PeakFinding.Criteria()).Indices;
        var all = new List<int>(peaks.Length + valleys.Length + 2) { 0 };
        all.AddRange(peaks);
        all.AddRange(valleys);
        all.Sort(1, all.Count - 1, Comparer<int>.Default);
        all.Add(x.Length - 1);
        return [.. all];
    }

    /// <summary>MATLAB's <c>countCycles</c>, run over the turning points.</summary>
    public static LoadCycle[] Cycles(double[] y)
    {
        ArgumentNullException.ThrowIfNull(y);
        int m = y.Length;
        var stack = new int[m + 1];
        var counted = new List<LoadCycle>();
        int depth = 0;
        int next = 0;
        for (int step = 0; step < m; step++)
        {
            stack[depth] = next;
            depth++;
            next++;
            while (depth >= 3
                && System.Math.Abs(y[stack[depth - 2]] - y[stack[depth - 3]])
                    <= System.Math.Abs(y[stack[depth - 1]] - y[stack[depth - 2]]))
            {
                double range = System.Math.Abs(y[stack[depth - 2]] - y[stack[depth - 3]]);
                double mean = (y[stack[depth - 2]] + y[stack[depth - 3]]) / 2;
                if (depth == 3)
                {
                    if (range > 0)
                    {
                        counted.Add(new LoadCycle(0.5, range, mean, stack[0], stack[1]));
                    }

                    stack[0] = stack[1];
                    stack[1] = stack[2];
                    depth = 2;
                }
                else
                {
                    if (range > 0)
                    {
                        counted.Add(new LoadCycle(1.0, range, mean, stack[depth - 3], stack[depth - 2]));
                    }

                    stack[depth - 3] = stack[depth - 1];
                    depth -= 2;
                }
            }
        }

        for (int i = 0; i < depth - 1; i++)
        {
            double range = System.Math.Abs(y[stack[i]] - y[stack[i + 1]]);
            double mean = (y[stack[i]] + y[stack[i + 1]]) / 2;
            if (range > 0)
            {
                counted.Add(new LoadCycle(0.5, range, mean, stack[i], stack[i + 1]));
            }
        }

        return [.. counted];
    }
}
