using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M92: the script-visible face of wiring the packed kernels up. Nothing here is new behaviour —
/// that is the whole claim — so every test is a statement about something that must <em>still</em>
/// be true now that unary minus, the elementwise maths family, the reductions, <c>nnz</c>,
/// <c>find</c> and a masked read reach the flat kernels instead of a delegate per element.
/// </summary>
/// <remarks>
/// The awkward cases are deliberately the ones a fast path gets wrong: the promotion to complex
/// that a whole-array domain check must still trigger, a NaN sitting beside the element that
/// triggers it, the two zeros, the shape a masked read comes back in, and the errors that used to
/// come from code the fast path now skips.
/// </remarks>
[Collection("JG facade")]
public class MatlabPackedKernelsM92Tests : IDisposable
{
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabPackedKernelsM92Tests() => JG.Reset();

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

    private string RunExpectingFailure(string code)
    {
        ScriptRunResult result = RunMatlab(code);
        Assert.False(result.Success, "the script was expected to fail");
        return result.Message + _output.ErrorText;
    }

    // --- The elementwise family still leaves the reals when it should ---------------------------

    [Fact]
    public void SqrtOfAnArrayWithOneNegativeStillPromotesTheWholeArray()
    {
        RunAsserting("""
            x = ones(1, 5000);
            x(4321) = -4;
            y = sqrt(x);
            assert(~isreal(y));
            assert(abs(y(4321) - 2i) < 1e-15);
            assert(y(1) == 1);
            """);
    }

    [Fact]
    public void ANaNBesideTheNegativeDoesNotHideIt()
    {
        // The domain check reads a whole tile at a time now, and a tile's minimum would have been
        // NaN here — which is why it asks whether anything is below zero instead.
        RunAsserting("""
            x = ones(1, 5000);
            x(10) = NaN;
            x(11) = -9;
            y = sqrt(x);
            assert(~isreal(y));
            assert(isnan(real(y(10))));
            assert(abs(y(11) - 3i) < 1e-14);
            """);
    }

    [Fact]
    public void ANaNOnItsOwnKeepsSqrtAndLogReal()
    {
        RunAsserting("""
            x = [1 4 NaN 9];
            y = sqrt(x);
            assert(isreal(y));
            assert(isnan(y(3)));
            assert(isequal(y([1 2 4]), [1 2 3]));

            L = log([1 NaN exp(1)]);
            assert(isreal(L));
            assert(isnan(L(2)));
            """);
    }

    [Fact]
    public void LogOfANegativeStillPromotes()
    {
        RunAsserting("""
            x = ones(1, 3000);
            x(2999) = -1;
            y = log(x);
            assert(~isreal(y));
            assert(abs(y(2999) - 1i*pi) < 1e-14);
            """);
    }

    [Fact]
    public void TheElementwiseFamilyKeepsItsShape()
    {
        RunAsserting("""
            A = reshape(1:12, 3, 4);
            assert(isequal(size(sqrt(A)), [3 4]));
            assert(isequal(size(sin(A)), [3 4]));
            assert(isequal(size(abs(-A)), [3 4]));
            assert(isequal(size(floor(A/2)), [3 4]));
            assert(isequal(abs(-A), A));
            """);
    }

    [Fact]
    public void RoundStillRoundsAwayFromZeroRatherThanToEven()
    {
        // PackedMath.Round is the banker's rule and MATLAB's round is not, which is why `round` is
        // the one name in the family that names no kernel.
        RunAsserting("""
            r = round([0.5 1.5 2.5 -0.5 -1.5 -2.5]);
            assert(isequal(r, [1 2 3 -1 -2 -3]));
            """);
    }

    // --- Unary minus ------------------------------------------------------------------------------

    [Fact]
    public void NegatingAPackedArrayKeepsTheSignOfZero()
    {
        RunAsserting("""
            x = zeros(1, 4000);
            y = -x;
            assert(all(1./y == -Inf));
            assert(all(1./(-y) == Inf));
            """);
    }

    [Fact]
    public void NegationStaysInsideItsNumericClass()
    {
        RunAsserting("""
            a = -uint8([5 0 200]);
            assert(isa(a, 'uint8'));
            assert(isequal(double(a), [0 0 0]));
            b = -int8(100);
            assert(isequal(double(b), -100));
            """);
    }

    [Fact]
    public void NegatingAMatrixKeepsItsShape()
    {
        RunAsserting("""
            A = reshape(1:6, 2, 3);
            B = -A;
            assert(isequal(size(B), [2 3]));
            assert(isequal(B, 0 - A));
            """);
    }

    // --- Reductions -------------------------------------------------------------------------------

    [Fact]
    public void TheWholeArrayReductionsAnswerWhatTheFoldAnswers()
    {
        RunAsserting("""
            v = (1:1000) / 7;
            assert(abs(sum(v) - sum(v(:)')) < 1e-12);
            assert(abs(mean(v) - sum(v)/1000) == 0);
            assert(min(v) == v(1));
            assert(max(v) == v(1000));
            """);
    }

    [Fact]
    public void MinAndMaxStillStepOverNaNWhereSumDoesNot()
    {
        RunAsserting("""
            v = [3 NaN 1 7];
            assert(min(v, [], 'all') == 1);
            assert(max(v, [], 'all') == 7);
            assert(isnan(sum(v)));
            """);
    }

    [Fact]
    public void MinAndMaxOverALongArrayAgreeWithTheFold()
    {
        // Long enough to run through several vector registers and a scalar tail, which is where a
        // reduction that reassociates would start to disagree with the loop it replaced.
        RunAsserting("""
            v = mod((1:100003) * 0.6180339887, 1);
            lo = v(1); hi = v(1);
            for k = 2:numel(v)
                if v(k) < lo, lo = v(k); end
                if v(k) > hi, hi = v(k); end
            end
            assert(min(v, [], 'all') == lo);
            assert(max(v, [], 'all') == hi);
            """);
    }

    [Fact]
    public void AnEmptyArrayStillSumsToZeroAndRefusesAMean()
    {
        RunAsserting("assert(sum([]) == 0);");
        Assert.Contains("non-empty", RunExpectingFailure("mean([]);"));
    }

    [Fact]
    public void ALogicalMaskStillReadsAsOnesAndZeros()
    {
        RunAsserting("""
            v = [1 5 2 9 4];
            m = v > 3;
            assert(sum(m) == 3);
            assert(mean(m) == 0.6);
            assert(nnz(m) == 3);
            """);
    }

    [Fact]
    public void DotIsStillTheConjugatedInnerProduct()
    {
        RunAsserting("""
            a = 1:1000;
            b = (1:1000) * 0.5;
            assert(abs(dot(a, b) - sum(a .* b)) < 1e-6);
            assert(dot([1 2], [3 4]) == 11);
            assert(abs(dot([1+2i 3], [1 1]) - (1-2i+3)) < 1e-14);
            """);
    }

    [Fact]
    public void DotStillRefusesMismatchedLengths()
    {
        Assert.Contains("equal length", RunExpectingFailure("dot([1 2 3], [1 2]);"));
    }

    [Fact]
    public void Atan2StillPairsItsOperandsBothWays()
    {
        RunAsserting("""
            y = (1:2000) - 1000;
            a = atan2(y, 1);
            b = atan2(1, y);
            assert(abs(a(1) - atan2(-999, 1)) == 0);
            assert(abs(b(1) - atan2(1, -999)) == 0);
            assert(numel(a) == 2000 && numel(b) == 2000);
            assert(abs(atan2(1, 1) - pi/4) < 1e-15);
            """);
    }

    // --- nnz, find, and the masked read ----------------------------------------------------------

    [Fact]
    public void NnzCountsWhatIsNotZeroIncludingNaN()
    {
        RunAsserting("""
            v = [1 0 NaN -0 3 0];
            assert(nnz(v) == 3);
            assert(nnz(zeros(1, 5000)) == 0);
            assert(nnz(ones(3, 4)) == 12);
            """);
    }

    [Fact]
    public void FindStillAnswersPositionsInTheRightShape()
    {
        RunAsserting("""
            v = [0 5 0 7 9];
            assert(isequal(find(v), [2 4 5]));
            assert(isequal(find(v > 6), [4 5]));
            assert(isequal(find(v, 2), [2 4]));
            assert(isempty(find(zeros(1, 3))));

            A = [0 1; 2 0];
            f = find(A);
            assert(isequal(size(f), [2 1]));
            assert(isequal(f, [2; 3]));
            """);
    }

    [Fact]
    public void AMaskedReadGathersTheSameElementsAsPositionsWould()
    {
        RunAsserting("""
            v = (1:5000) / 3;
            m = v > 900;
            picked = v(m);
            expected = v(find(m));
            assert(isequal(picked, expected));
            assert(numel(picked) == nnz(m));
            """);
    }

    [Fact]
    public void AMaskedReadOfAMatrixStillComesBackAsAColumn()
    {
        RunAsserting("""
            A = reshape(1:12, 3, 4);
            m = A > 8;
            picked = A(m);
            assert(isequal(size(picked), [4 1]));
            assert(isequal(picked, [9; 10; 11; 12]));
            """);
    }

    [Fact]
    public void AMaskedReadOfARowStaysARow()
    {
        RunAsserting("""
            v = [1 2 3 4 5];
            picked = v(v > 2);
            assert(isequal(size(picked), [1 3]));
            assert(isequal(picked, [3 4 5]));
            """);
    }

    [Fact]
    public void AMaskedReadKeepsTheLogicalClassOfWhatItReads()
    {
        RunAsserting("""
            b = [true false true true];
            picked = b([true true false true]);
            assert(islogical(picked));
            assert(isequal(double(picked), [1 0 1]));
            """);
    }

    [Fact]
    public void AMaskOfTheWrongLengthIsStillRefused()
    {
        string message = RunExpectingFailure("""
            v = [1 2 3 4];
            w = v(logical([1 0 1]));
            """);
        Assert.Contains("mask", message);
    }

    [Fact]
    public void AnEmptyMaskStillPicksNothing()
    {
        RunAsserting("""
            v = [1 2 3];
            picked = v(v > 99);
            assert(isempty(picked));
            """);
    }
}
