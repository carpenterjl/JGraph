using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Numerics;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M93 seen from a script. Two things changed underneath that a script can notice, and this file is
/// where each of them is pinned down. The first is that a large enough statement now runs on several
/// threads — which must change nothing, so most of what is here is arithmetic checked against a
/// hand-written answer at a size that is certain to be split. The second is that transcendentals
/// over arrays of 32K elements and up take a vector kernel that is a few ulps away from
/// <see cref="Math"/>'s: below that line the two paths must still be the same path, and above it the
/// difference must be small enough to be invisible to everything but a bit comparison.
/// </summary>
[Collection("JG facade")]
public class MatlabThreadedKernelsM93Tests : IDisposable
{
    /// <summary>Over the threading threshold and not a multiple of the grain — a partial last grain.</summary>
    private const int Split = ParallelKernels.MemoryBoundThreshold + 4_321;

    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabThreadedKernelsM93Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private ScriptRunResult RunMatlab(string code)
    {
        var context = new ScriptContext(_output, (_, figure) => _figures.Add(figure), null);
        return JgsRunner.Run(code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
    }

    private void RunAsserting(string code)
    {
        ScriptRunResult result = RunMatlab(code);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    // --- A split statement is the same statement ------------------------------------------------

    [Fact]
    public void ASplitElementwiseChainAgreesWithTheFoldElementForElement()
    {
        RunAsserting($$"""
            n = {{Split}};
            x = (1:n) * 0.000001;
            y = -x .* 2 + 1;
            assert(numel(y) == n);
            assert(y(1) == -0.000001 * 2 + 1);
            assert(y(n) == -(n * 0.000001) * 2 + 1);
            assert(y(1234567) == -(1234567 * 0.000001) * 2 + 1);
            """);
    }

    [Fact]
    public void ASplitComparisonAndMaskCountTheSameMatches()
    {
        RunAsserting($$"""
            n = {{Split}};
            x = mod(1:n, 10);
            m = x > 6;
            full = floor(n / 10);
            expected = 3 * full + sum(mod((full * 10 + 1):n, 10) > 6);
            assert(nnz(m) == expected);
            z = x(m);
            assert(numel(z) == nnz(m));
            assert(all(z > 6));
            assert(z(1) == 7);
            assert(isequal(find(x > 8, 3), [9 19 29]));
            """);
    }

    [Fact]
    public void ASplitExtractKeepsTheOrderItFoundThingsIn()
    {
        // Every grain writes its own stretch of the answer; if the offsets were computed any way
        // but "add the counts up in index order" this comes back shuffled.
        RunAsserting($$"""
            n = {{Split}};
            x = 1:n;
            z = x(mod(x, 3) == 1);
            assert(numel(z) == ceil(n / 3));
            assert(isequal(z(1:4), [1 4 7 10]));
            assert(all(diff(z) == 3));
            assert(z(end) == 1 + 3 * (numel(z) - 1));
            """);
    }

    [Fact]
    public void ASplitDomainCheckStillPromotesForANegativeAnywhereInIt()
    {
        RunAsserting($$"""
            n = {{Split}};
            x = ones(1, n);
            x(n) = -9;
            y = sqrt(x);
            assert(~isreal(y));
            assert(abs(y(n) - 3i) < 1e-15);
            assert(y(1) == 1);

            x(n) = 1;
            x(2) = -9;
            y = sqrt(x);
            assert(~isreal(y));
            assert(abs(y(2) - 3i) < 1e-15);
            """);
    }

    [Fact]
    public void ASplitRemainderAndPowerAreStillTheScalarOnes()
    {
        // Both stay scalar loops on purpose (Math.Pow is not correctly rounded, so x.^2 is not
        // x.*x); threading them must not have changed which loop runs.
        RunAsserting($$"""
            n = {{Split}};
            x = (1:n) * 0.5;
            assert(isequal(mod(x(7), 3), mod(3.5, 3)));
            p = x .^ 2;
            assert(p(9) == 4.5 ^ 2);
            assert(p(n) == (n * 0.5) ^ 2);
            """);
    }

    // --- A whole-array reduction over a shaped array --------------------------------------------

    [Fact]
    public void AReductionOverAMatrixReadsItInTheOrderItIsStored()
    {
        // FlattenColumnMajor takes a packed array's own storage now instead of rebuilding it out of
        // jagged rows. Same numbers in the same order, or none of these hold.
        RunAsserting("""
            A = reshape(1:600, 20, 30);
            assert(min(A(:)) == 1);
            assert(max(A(:)) == 600);
            assert(sum(A(:)) == 600 * 601 / 2);
            v = A(:);
            assert(numel(v) == 600);
            assert(isequal(size(v), [600 1]));
            assert(v(21) == 21);
            down = sum(A, 1);
            across = sum(A, 2);
            assert(isequal(size(down), [1 30]));
            assert(isequal(size(across), [20 1]));
            assert(down(1) == sum(1:20));
            assert(across(1) == sum(1:20:600));
            """);
    }

    [Fact]
    public void AColumnAndARowReduceToTheSameNumber()
    {
        RunAsserting("""
            c = (1:1000)';
            r = 1:1000;
            assert(min(c) == min(r));
            assert(max(c) == max(r));
            assert(sum(c) == sum(r));
            assert(mean(c) == mean(r));
            cc = cumsum(c);
            cr = cumsum(r);
            assert(isequal(size(cc), [1000 1]));
            assert(isequal(size(cr), [1 1000]));
            assert(cc(end) == cr(end));
            """);
    }

    [Fact]
    public void ALogicalMatrixStillReducesAsZerosAndOnes()
    {
        RunAsserting("""
            A = reshape(1:600, 20, 30);
            L = A > 300;
            assert(sum(L(:)) == 300);
            assert(nnz(L) == 300);
            assert(islogical(L));
            assert(isequal(size(sum(L, 1)), [1 30]));
            """);
    }

    [Fact]
    public void AMatrixWithNaNStillReducesTheWayItDid()
    {
        RunAsserting("""
            A = reshape(1:12, 3, 4);
            A(2, 3) = NaN;
            assert(isnan(sum(A(:))));
            assert(min(A(:)) == 1);
            assert(max(A(:)) == 12);
            down = sum(A, 1);
            assert(isnan(down(3)));
            assert(down(1) == 6);
            """);
    }

    // --- The approximate tier -------------------------------------------------------------------

    [Fact]
    public void BelowTheThresholdAPackedTranscendentalIsStillBitForBitTheBoxedOne()
    {
        int under = PackedMath.ApproximateThreshold - 1;
        double[] packed = SumOfSines(under, usePacking: true);
        double[] boxed = SumOfSines(under, usePacking: false);
        Assert.Equal(BitConverter.DoubleToInt64Bits(boxed[0]), BitConverter.DoubleToInt64Bits(packed[0]));
        Assert.Equal(BitConverter.DoubleToInt64Bits(boxed[1]), BitConverter.DoubleToInt64Bits(packed[1]));
    }

    [Fact]
    public void AboveTheThresholdAPackedTranscendentalIsWithinAFewUlpsOfTheBoxedOne()
    {
        int over = PackedMath.ApproximateThreshold;
        double[] packed = SumOfSines(over, usePacking: true);
        double[] boxed = SumOfSines(over, usePacking: false);

        // Element by element the difference is ulps; over a 32K-term sum it is still nothing a
        // script prints. Both are asserted, because only the pair says "small, and small for the
        // right reason".
        Assert.True(Math.Abs(packed[0] - boxed[0]) <= 1e-15 * Math.Max(1, Math.Abs(boxed[0])),
            $"one element: {packed[0]:G17} vs {boxed[0]:G17}");
        Assert.True(Math.Abs(packed[1] - boxed[1]) <= 1e-9 * Math.Max(1, Math.Abs(boxed[1])),
            $"the sum: {packed[1]:G17} vs {boxed[1]:G17}");
    }

    /// <summary>One element of <c>sin</c> and the sum of them all, over <paramref name="length"/> points.</summary>
    private double[] SumOfSines(int length, bool usePacking)
    {
        bool previous = JgsPacking.Enabled;
        JgsPacking.Enabled = usePacking;
        try
        {
            JG.Reset();
            _output.Normal.Clear();
            ScriptRunResult result = RunMatlab($$"""
                x = linspace(0, 20, {{length}});
                y = sin(x);
                fprintf('%.17g\n%.17g\n', y(1234), sum(y));
                """);
            Assert.True(result.Success, result.Message + _output.ErrorText);

            double[] probe = _output.NormalLines
                .Select(line => double.Parse(line.Trim(), System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();
            _output.Normal.Clear();
            Assert.Equal(2, probe.Length);
            return probe;
        }
        finally
        {
            JgsPacking.Enabled = previous;
        }
    }
}
