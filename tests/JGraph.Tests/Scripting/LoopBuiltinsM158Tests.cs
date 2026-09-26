using JGraph.Api;
using JGraph.Maths;
using JGraph.Numerics;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// ADR 0158 (item 11): the builtin fills that thread — histcounts' tally, discretize's bin
/// writer, sortrows' gather — answer the same bits and counts on one thread and on sixteen, and
/// the parity gaps the item's fixture surfaced are closed: diff's empty shapes, sortrows with a
/// direction word alone, the two zeros as one sortrows key, and histcounts' requested-count
/// width written as its decimal.
/// </summary>
[Collection("JG facade")]
public class LoopBuiltinsM158Tests : IDisposable
{
    private RecordingScriptOutput _output = new();

    public LoopBuiltinsM158Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private const int Large = ParallelKernels.DefaultMemoryBoundThreshold + 54_321;

    [Fact]
    public void HistogramTallyIsTheSameOnOneThreadAndSixteen()
    {
        double[] data = Series(Large, specials: true);
        using NumericBuffer source = ManagedBuffer.Adopt(data);
        foreach (double[] edges in new[] { Binning.CountedEdges(-1.3, 1.3, 256), Spread(-1.3, 1.3, 65_536), Spread(-1.3, 1.3, 70_000), new[] { -1.0, 0.0, 1.0 } })
        {
            long[] alone;
            double[] whichAlone;
            long[] together;
            double[] whichTogether;
            using (var which = new ManagedBuffer(Large))
            {
                alone = AtDegree(1, () => JgsBuiltins.CountBins(source, edges, which, 1));
                whichAlone = which.AsSpan().ToArray();
            }

            using (var which = new ManagedBuffer(Large))
            {
                together = AtDegree(16, () => JgsBuiltins.CountBins(source, edges, which, 1));
                whichTogether = which.AsSpan().ToArray();
            }

            Assert.Equal(alone, together);
            Assert.Equal(whichAlone, whichTogether);
            Assert.Equal(alone, AtDegree(16, () => JgsBuiltins.CountBins(source, edges, null, 1)));

            // And the serial loop's own answer, bin by bin.
            var expected = new long[edges.Length - 1];
            var finder = Binning.BinFinder.For(edges);
            for (int i = 0; i < data.Length; i++)
            {
                int bin = finder.Of(data[i]);
                Assert.Equal(bin < 0 ? 0 : bin + 1, whichAlone[i]);
                if (bin >= 0)
                {
                    expected[bin]++;
                }
            }

            Assert.Equal(expected, alone);
        }
    }

    [Fact]
    public void DiscretizeFillIsTheSameOnOneThreadAndSixteen()
    {
        double[] data = Series(Large, specials: true);
        double[] edges = Spread(-1.2, 1.2, 257);
        double[] labels = Enumerable.Range(0, 256).Select(k => 256.0 - k).ToArray();
        using NumericBuffer source = ManagedBuffer.Adopt(data);
        var finder = Binning.BinFinder.For(edges);
        foreach (bool right in new[] { false, true })
        {
            foreach (double[]? values in new[] { null, labels })
            {
                double[] alone = AtDegree(1, () => Fill(source, finder, right, values));
                double[] together = AtDegree(16, () => Fill(source, finder, right, values));
                Assert.True(alone.SequenceEqual(together) || alone.Zip(together).All(p => BitConverter.DoubleToInt64Bits(p.First) == BitConverter.DoubleToInt64Bits(p.Second)));
                for (int i = 0; i < data.Length; i += 997)
                {
                    int bin = right ? finder.OfRightClosed(data[i]) : finder.Of(data[i]);
                    double expected = bin < 0 ? double.NaN : values is null ? bin + 1 : values[bin];
                    Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(alone[i]));
                }
            }
        }
    }

    [Fact]
    public void RowGatherIsTheSameOnOneThreadAndSixteen()
    {
        const int rows = 700_000;
        const int columns = 3;
        var random = new Random(11);
        double[] flat = Series(rows * columns, specials: false);
        int[] order = Enumerable.Range(0, rows).OrderBy(_ => random.Next()).ToArray();
        using NumericBuffer source = ManagedBuffer.Adopt(flat);
        double[] alone = AtDegree(1, () => Gather(source, order, rows, columns));
        double[] together = AtDegree(16, () => Gather(source, order, rows, columns));
        Assert.Equal(alone, together);
        for (int r = 0; r < rows; r += 1009)
        {
            for (int c = 0; c < columns; c++)
            {
                Assert.Equal(flat[order[r] + (c * rows)], alone[r + (c * rows)]);
            }
        }
    }

    [Fact]
    public void DiffOfAScalarOrPastTheLengthAnswersMatlabsEmptyShapes()
    {
        string text = Run("""
            M = reshape((1:12) .^ 2, 3, 4);
            fprintf('%s|%s|%s|%s|%s|%s|%s', mat2str(size(diff(M, 5))), mat2str(size(diff(7))), ...
                mat2str(size(diff([]))), mat2str(size(diff([1 2 3], 4))), mat2str(size(diff([1; 2; 3], 3))), ...
                mat2str(size(diff(M, 5, 2))), mat2str(diff([1 4 9], 2)));
            """);
        Assert.Equal("[0 4]|[0 0]|[0 0]|[1 0]|[0 1]|[3 0]|2", text);
    }

    [Fact]
    public void SortrowsTiesTheTwoZerosAndTakesADirectionWordAlone()
    {
        string text = Run("""
            A = [3 1; 1 2; 3 0; NaN 1; 1 NaN; -0 5; 0 4; 0 3; -0 2; 1 2; NaN NaN; 3 1];
            [B, i] = sortrows(A);
            [C, j] = sortrows(A, 'descend');
            [D, k] = sortrows(A, [-1 -2]);
            [E, m] = sortrows([-0; 0; -0; 0], -1);
            fprintf('%s|%s|%d|%s|%s', mat2str(i'), mat2str(j'), isequaln(C, D) && isequal(j, k), ...
                mat2str(m'), reshape(num2hex(E).', 1, []));
            """);
        Assert.Equal(
            "[9 8 7 6 2 10 5 3 1 12 4 11]|[11 4 1 12 3 5 2 10 6 7 8 9]|1|[1 2 3 4]|8000000000000000000000000000000080000000000000000000000000000000",
            text);
    }

    [Fact]
    public void ARequestedBinCountsWidthIsWrittenAsItsDecimal()
    {
        // 1002 hundred-thousandths: multiplying by the inexact 1e-5 lands an ulp above the decimal,
        // dividing by the exact 1e5 lands on it, and R2025b's edges are the decimal's multiples.
        double[] edges = Binning.CountedEdges(-1.25, 1.3151200000000003, 256);
        Assert.Equal(257, edges.Length);
        Assert.Equal(-1.25, edges[0]);
        Assert.Equal(-1.2099200000000001, edges[4]);
        for (int i = 1; i < 256; i++)
        {
            Assert.Equal(-1.25 + (i * 0.01002), edges[i]);
        }

        // The left edge is written as itself, so a -0 minimum keeps its sign, as R2025b's does.
        double[] fromMinusZero = Binning.CountedEdges(-0.0, 1, 2);
        Assert.True(double.IsNegative(fromMinusZero[0]));
        Assert.Equal([-0.0, 0.5, 1], fromMinusZero);
    }

    // --- Helpers ---------------------------------------------------------------------------------

    private string Run(string code)
    {
        _output = new RecordingScriptOutput();
        var context = new ScriptContext(_output, (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Trim();
    }

    private static double[] Fill(NumericBuffer source, Binning.BinFinder finder, bool right, double[]? values)
    {
        using var dest = new ManagedBuffer(source.Length);
        JgsBuiltins.DiscretizeInto(source, dest, finder, right, values, 1);
        return dest.AsSpan().ToArray();
    }

    private static double[] Gather(NumericBuffer source, int[] order, int rows, int columns)
    {
        using var dest = new ManagedBuffer(rows * columns);
        JgsBuiltins.GatherRows(source, dest, order, rows, columns);
        return dest.AsSpan().ToArray();
    }

    private static double[] Series(int n, bool specials)
    {
        var data = new double[n];
        for (int i = 0; i < n; i++)
        {
            double a = ((i + 1) * 0.618033988749895) % 1.0;
            double b = ((i + 1) * 0.381966011250105) % 1.0;
            data[i] = (2 * a) - 1 + (0.3 * (b - 0.5));
        }

        if (specials)
        {
            for (int i = 0; i < n; i += 1000)
            {
                data[i] = double.NaN;
                data[i + 1] = double.PositiveInfinity;
                data[i + 2] = double.NegativeInfinity;
                data[i + 3] = -0.0;
                data[i + 4] = 0.0;
                data[i + 5] = -1.2;
                data[i + 6] = 1.2;
            }
        }

        return data;
    }

    private static double[] Spread(double low, double high, int edges)
    {
        var result = new double[edges];
        for (int i = 0; i < edges; i++)
        {
            result[i] = low + ((high - low) * i / (edges - 1));
        }

        return result;
    }

    private static T AtDegree<T>(int degree, Func<T> body)
    {
        int previous = ParallelKernels.MaxDegree;
        ParallelKernels.MaxDegree = degree;
        try
        {
            return body();
        }
        finally
        {
            ParallelKernels.MaxDegree = previous;
        }
    }
}
