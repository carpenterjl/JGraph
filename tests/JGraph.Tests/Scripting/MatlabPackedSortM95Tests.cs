using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Numerics;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M95 seen from a MATLAB script: <c>sort</c> takes the packed kernels, and nothing may notice.
/// Every test here runs its script twice — packing forced on, then forced off — and the printed
/// output must be byte-identical, at seventeen significant digits with reciprocals alongside so a
/// flipped zero sign cannot hide. The scripts sweep what the fast path claims: both directions,
/// every dimension including one past the end, the option words, both outputs, N-D arrays — and
/// the forms it must refuse, whose answers and error messages have to read exactly as they did.
/// </summary>
[Collection("JG facade")]
public class MatlabPackedSortM95Tests : IDisposable
{
    public MatlabPackedSortM95Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private static (string[] Output, bool Success, string? Message) RunWith(bool packed, string code)
    {
        bool previous = JgsPacking.Enabled;
        JgsPacking.Enabled = packed;
        try
        {
            JG.Reset();
            var output = new RecordingScriptOutput();
            var figures = new List<FigureModel>();
            var context = new ScriptContext(output, (_, figure) => figures.Add(figure), null);
            ScriptRunResult result = JgsRunner.Run(
                code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
            return (output.Normal.ToArray(), result.Success, result.Message);
        }
        finally
        {
            JgsPacking.Enabled = previous;
        }
    }

    private static void AssertParity(string code, bool expectSuccess = true)
    {
        (string[] packedOut, bool packedOk, string? packedMessage) = RunWith(packed: true, code);
        (string[] boxedOut, bool boxedOk, string? boxedMessage) = RunWith(packed: false, code);

        Assert.Equal(boxedOk, packedOk);
        if (expectSuccess)
        {
            Assert.True(boxedOk, boxedMessage);
        }
        else
        {
            Assert.False(boxedOk);
            Assert.Equal(boxedMessage, packedMessage); // the refusal must read the same too
        }

        Assert.Equal(boxedOut, packedOut);
    }

    /// <summary>Prints a value bit-faithfully: the shape, whether it is logical, the digits, and
    /// the reciprocals, which is where a <c>-0</c> that turned into a <c>+0</c> shows up.</summary>
    private const string Show = """
        function show(v)
            fprintf('%dx%d L%d|', size(v, 1), size(v, 2), islogical(v));
            fprintf('%.17g ', v);
            fprintf('|');
            fprintf('%.17g ', 1 ./ v);
            fprintf('\n');
        end
        """ + "\n";

    private const string Pair = """
        function pair(b, i)
            fprintf('%dx%d|', size(b, 1), size(b, 2));
            fprintf('%.17g ', b);
            fprintf('|');
            fprintf('%.17g ', 1 ./ b);
            fprintf('|');
            fprintf('%d ', i);
            fprintf('\n');
        end
        """ + "\n";

    [Fact]
    public void EveryDirectionAndDimensionOrdersTheSameWhicheverWayTheValuesAreStored()
    {
        AssertParity(Show + """
            v = [3 1 2 1 -4 9];
            show(sort(v)); show(sort(v')); show(sort(v, 'descend')); show(sort(v, 'ascend'));
            A = [3 1; 1 4; 2 9];
            show(sort(A)); show(sort(A, 1)); show(sort(A, 2)); show(sort(A, 3)); show(sort(A, 7));
            show(sort(A, 2, 'descend')); show(sort(A, 'descend'));
            C = reshape((1:24) - 12.5, 2, 3, 4);
            show(sort(C, 1)); show(sort(C, 2)); show(sort(C, 3)); show(sort(C, 3, 'descend'));
            show(sort(5)); show(sort([2 2 2 2])); show(sort([1 2 3 4 5])); show(sort([5 4 3 2 1]));
            """);
    }

    [Fact]
    public void MissingValuesLandWhereTheyAreAskedForAndKeepTheirOrder()
    {
        AssertParity(Show + """
            n = [NaN 3 -1 NaN 2 -Inf Inf];
            show(sort(n)); show(sort(n, 'descend'));
            show(sort(n, 'MissingPlacement', 'first'));
            show(sort(n, 'MissingPlacement', 'last'));
            show(sort(n, 'descend', 'MissingPlacement', 'last'));
            show(sort(n, 'descend', 'MissingPlacement', 'first'));
            show(sort(n, 'ComparisonMethod', 'real')); show(sort(n, 'ComparisonMethod', 'auto'));
            N = reshape([NaN 1 2 NaN 3 NaN], 2, 3);
            show(sort(N, 1)); show(sort(N, 2)); show(sort(N, 2, 'MissingPlacement', 'first'));
            show(sort([NaN NaN NaN]));
            """);
    }

    [Fact]
    public void TheTwoZerosTieAndKeepTheOrderTheyArrivedIn()
    {
        AssertParity(Show + """
            z = [0 -0 1 -0 0 -1];
            z(2) = -0; z(4) = -0;
            show(sort(z)); show(sort(z, 'descend'));
            show(sort([z; -z], 2)); show(sort([z; -z], 2, 'descend'));
            show(sort([z z], 'MissingPlacement', 'first'));
            """);
    }

    [Fact]
    public void BothOutputsComeBackTheSameFromEitherStore()
    {
        AssertParity(Show + Pair + """
            [b, i] = sort([3 1 2 1]); pair(b, i);
            [b, i] = sort([3; 1; 2; 1], 'descend'); pair(b, i);
            [b, i] = sort([5 1 5 1 5]); pair(b, i);
            [b, i] = sort([5 1 5 1 5], 'descend'); pair(b, i);
            A = [3 1; 1 4; 2 9];
            [b, i] = sort(A); pair(b(:)', i(:)');
            [b, i] = sort(A, 2); pair(b(:)', i(:)');
            [b, i] = sort(A, 3); pair(b(:)', i(:)');
            n = [NaN 3 -1 NaN 2];
            [b, i] = sort(n); pair(b, i);
            [b, i] = sort(n, 'descend'); pair(b, i);
            [b, i] = sort(n, 'MissingPlacement', 'first'); pair(b, i);
            z = [0 -0 1 -0 0]; z(2) = -0; z(4) = -0;
            [b, i] = sort(z); pair(b, i);
            [b, i] = sort(z, 'descend'); pair(b, i);
            [b, i] = sort(7); pair(b, i);
            """);
    }

    [Fact]
    public void ThePermutationRebuildsTheSortedValues()
    {
        AssertParity("""
            r = mod((1:4000) * 2654435761, 97);
            r(7) = NaN; r(11) = -0; r(12) = 0; r(13) = Inf;
            [b, i] = sort(r);
            fprintf('%d %d %d\n', isequaln(r(i), b), numel(unique(i)), issorted(b(1:end-1)));
            [b, i] = sort(r, 'descend');
            fprintf('%d %d\n', isequaln(r(i), b), numel(unique(i)));
            """);
    }

    [Fact]
    public void TheFormsTheFastPathRefusesAnswerExactlyAsTheyDid()
    {
        AssertParity(Show + """
            show(sort([true false true false]));
            show(sort(zeros(1, 0)));
            show(sort([-3 1 -2], 'ComparisonMethod', 'abs'));
            show(sort(int8([5 -3 100])));
            show(sort(uint16([9 2 40000])));
            fprintf('%s\n', mat2str(sort([3+1i, 1-2i, 2]), 6));
            fprintf('%s\n', mat2str(sort([3+1i, 1-2i, 2], 'ComparisonMethod', 'real'), 6));
            """);
    }

    [Fact]
    public void ARefusedCallReadsTheSameWayFromEitherStore()
    {
        AssertParity("A = [3 1; 1 4];\nsort(A, 0);\n", expectSuccess: false);
        AssertParity("A = [3 1; 1 4];\nsort(A, 'sideways');\n", expectSuccess: false);
        AssertParity("sort();\n", expectSuccess: false);
    }

    [Fact]
    public void ASliceLongEnoughToSplitAcrossThreadsStillAgrees()
    {
        // Past SortKernels.SliceThreshold the packed road stops sorting the slice where it lies and
        // starts cutting it into buckets by value — a different algorithm, the same answer.
        int n = SortKernels.SliceThreshold + 4_211;
        AssertParity(Show + $$"""
            n = {{n}};
            x = mod((1:n) * 2654435761, 1013) - 400;
            x(3) = NaN; x(9) = -0; x(10) = 0; x(11) = -Inf; x(12) = Inf;
            s = sort(x);
            fprintf('%.17g %.17g %.17g %.17g\n', s(1), s(2), s(round(n/2)), s(n));
            fprintf('%.17g %d\n', sum(s(1:n-1)), issorted(s(1:n-1)));
            d = sort(x, 'descend');
            fprintf('%.17g %.17g %.17g\n', d(1), d(2), d(n));
            fprintf('%.17g\n', sum(d(3:n)));
            """);
    }

    [Fact]
    public void AThreadedSortWithPositionsIsAPermutationOfItsInput()
    {
        // The boxed second output is quadratic, so a slice this long cannot be asked of it; what is
        // checked instead is the property a script leans on, that A(I) is B.
        int n = SortKernels.SliceThreshold + 1_777;
        (string[] output, bool ok, string? message) = RunWith(packed: true, $$"""
            n = {{n}};
            x = mod((1:n) * 2654435761, 5003) - 2000;
            x(5) = NaN; x(6) = -0; x(7) = 0;
            [b, i] = sort(x);
            fprintf('%d %d %d\n', isequaln(x(i), b), numel(unique(i)) == n, issorted(b(1:n-1)));
            [b, i] = sort(x, 'descend');
            fprintf('%d %d\n', isequaln(x(i), b), numel(unique(i)) == n);
            """);

        Assert.True(ok, message);
        Assert.Equal(["1 1 1\n", "1 1\n"], output);
    }
}
