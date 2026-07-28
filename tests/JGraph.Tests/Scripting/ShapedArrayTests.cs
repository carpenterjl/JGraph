using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Arrays carry a rows-by-columns shape over column-major storage (M40, ADR 0043). These are the
/// answers a ported MATLAB script depends on, and each one used to be wrong: two subscripts named
/// nothing, a linear index on a matrix returned a whole row, <c>A(:)</c> returned the rows, and a
/// transposed vector was the same vector back.
/// </summary>
/// <remarks>
/// The assertions run <em>inside</em> the script so they pin MATLAB's answers rather than JGraph's
/// display formatting — the M36 convention.
/// </remarks>
[Collection("JG facade")]
public class ShapedArrayTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public ShapedArrayTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private IScriptSession NewSession() => Assert
        .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
        .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure))));

    private async Task RunAsserting(string code)
    {
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    private async Task<string> RunFailing(string code)
    {
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.False(result.Success, "expected this to fail: " + code);
        return result.Message ?? string.Empty;
    }

    [Fact]
    public Task TwoSubscripts_SelectAnElementOrASubmatrix() => RunAsserting("""
        A = [1 2 3; 4 5 6; 7 8 9];
        assert(A(2, 3) == 6);
        assert(A(end, end) == 9);
        assert(isequal(A(2, :), [4 5 6]));
        assert(isequal(A(:, 2), [2; 5; 8]));
        assert(isequal(A(1:2, 2:3), [2 3; 5 6]));
        assert(isequal(A([1 3], [1 3]), [1 3; 7 9]));

        % A logical mask works in a slot of its own.
        assert(isequal(A([true false true], 2), [2; 8]));
        """);

    [Fact]
    public Task End_ResolvesPerDimension_NotPerValue() => RunAsserting("""
        % The whole point of two subscripts: 'end' means the last row in the first slot and the
        % last column in the second, so a 2-by-5 does not report 5 twice.
        A = [1 2 3 4 5; 6 7 8 9 10];
        assert(A(end, end) == 10);
        assert(isequal(A(end, :), [6 7 8 9 10]));
        assert(isequal(A(:, end), [5; 10]));
        assert(isequal(A(end - 1, end - 1), 4));
        """);

    [Fact]
    public Task LinearIndexing_IsColumnMajor_SoFindIndicesRoundTrip() => RunAsserting("""
        A = [1 2 3; 4 5 6; 7 8 9];

        % Element 5 counting down the columns is the middle one, as MATLAB counts.
        assert(A(5) == 5);
        assert(isequal(A(:)', [1 4 7 2 5 8 3 6 9]));
        assert(isequal(size(A(:)), [9 1]));

        % The claim that makes column-major worth having: an index from find indexes back.
        idx = find(A > 5);
        assert(isequal(size(idx), [4 1]));
        assert(isequal(A(idx), [7; 8; 6; 9]));
        assert(isequal(A(A > 5), A(idx)));
        """);

    [Fact]
    public Task GatherOrientation_FollowsTheIndex_ExceptBetweenTwoVectors() => RunAsserting("""
        A = [1 2 3; 4 5 6; 7 8 9];

        % A matrix source takes the index's orientation.
        assert(isequal(size(A([1 2])), [1 2]));
        assert(isequal(size(A([1; 2])), [2 1]));

        % A logical mask always gathers into a column, since what it picked out is scattered.
        assert(isequal(size(A(A > 8)), [1 1]));
        assert(isequal(size(A(A > 5)), [4 1]));

        % Between two vectors the source's own orientation wins, so v(1:2) looks like v.
        v = [10 20 30];
        assert(isequal(size(v(1:2)), [1 2]));
        c = v';
        assert(isequal(size(c(1:2)), [2 1]));
        """);

    [Fact]
    public Task Writes_FillASelection_AndCheckItsShape() => RunAsserting("""
        A = [1 2 3; 4 5 6; 7 8 9];
        A(2, 3) = 99;
        assert(A(2, 3) == 99);

        A(1, :) = [0 0 0];
        assert(isequal(A(1, :), [0 0 0]));

        A(:, 2) = [7; 7; 7];
        assert(isequal(A(:, 2), [7; 7; 7]));

        % A scalar broadcasts over the whole selection.
        A(2:3, 1:2) = 5;
        assert(isequal(A(2:3, 1:2), [5 5; 5 5]));

        % A compound operator reads and writes the same element once.
        A(1, 1) = A(1, 1) + 10;
        assert(A(1, 1) == 10);
        """);

    [Fact]
    public Task Growth_ZeroFills_AndDeletionTakesWholeRowsOrColumns() => RunAsserting("""
        A = [1 2; 3 4];
        A(4, 3) = 7;
        assert(isequal(size(A), [4 3]));
        assert(A(4, 3) == 7);
        assert(A(3, 3) == 0);
        assert(isequal(A(1:2, 1:2), [1 2; 3 4]));

        A(2, :) = [];
        assert(isequal(size(A), [3 3]));
        A(:, 1) = [];
        assert(isequal(size(A), [3 2]));

        % A vector grows and shrinks along whichever way it already ran.
        v = [1 2 3];
        v(5) = 9;
        assert(isequal(v, [1 2 3 0 9]));
        v(2) = [];
        assert(isequal(v, [1 3 0 9]));
        assert(isrow(v));

        c = [1; 2; 3];
        c(2) = [];
        assert(isequal(c, [1; 3]));
        assert(iscolumn(c));
        """);

    [Fact]
    public async Task DeletingOneElementOfAMatrix_IsRefused_NotGuessedAt()
    {
        // There is no rectangle left after removing a lone cell, so MATLAB refuses and so does this.
        string message = await RunFailing("A = [1 2; 3 4];\nA(1, 1) = [];");
        Assert.Contains("whole row or column", message, StringComparison.Ordinal);
    }

    [Fact]
    public Task Transpose_IsGenuine_ForVectorsAsWellAsMatrices() => RunAsserting("""
        A = [1 2; 3 4];
        assert(isequal(A', [1 3; 2 4]));
        assert(isequal(size([1 2 3]'), [3 1]));
        assert(isequal(size((1:3)''), [1 3]));

        % Which is what lets transposed vectors be stacked into a matrix at all.
        M = [(1:3)', (4:6)'];
        assert(isequal(size(M), [3 2]));
        assert(isequal(M(:, 2), [4; 5; 6]));
        """);

    [Fact]
    public Task Concatenation_TilesBlocks_AndSaysWhenTheyDoNotFit() => RunAsserting("""
        A = [1 2; 3 4];
        assert(isequal([A, A], [1 2 1 2; 3 4 3 4]));
        assert(isequal([A; A], [1 2; 3 4; 1 2; 3 4]));
        assert(isequal([A, [5; 6]], [1 2 5; 3 4 6]));
        assert(isequal([A; [5 6]], [1 2; 3 4; 5 6]));

        % An empty contributes nothing, exactly as in MATLAB.
        assert(isequal([A, []], A));

        % Two genuine row vectors stack into a matrix; neither of them is a column.
        assert(isequal(size([[1 2 3]; [4 5 6]]), [2 3]));
        """);

    [Fact]
    public async Task Concatenation_MismatchedBlocks_NameBothShapes()
    {
        string sideBySide = await RunFailing("x = [[1 2; 3 4], [1 2 3]];");
        Assert.Contains("side by side", sideBySide, StringComparison.Ordinal);

        string stacked = await RunFailing("x = [[1 2; 3 4]; [1 2 3]];");
        Assert.Contains("columns wide", stacked, StringComparison.Ordinal);
    }

    [Fact]
    public Task StackedVectors_ReadAsColumns_WhenOneOfThemIsOne() => RunAsserting("""
        % The documented leniency: a JGS vector's orientation is often incidental, so padding a
        % signal that came from a reader with zeros(k, 1) still means "put these end to end".
        a = [1 2 3];
        x = [a; zeros(2, 1)];
        assert(isequal(size(x), [5 1]));
        assert(isequal(x', [1 2 3 0 0]));
        """);

    [Fact]
    public Task ShapeQuestions_ReadTheShape() => RunAsserting("""
        A = [1 2 3; 4 5 6];
        assert(isequal(size(A), [2 3]));
        assert(size(A, 1) == 2 && size(A, 2) == 3);
        assert(numel(A) == 6);
        assert(ndims(A) == 2);
        assert(~isvector(A) && ~isscalar(A) && ismatrix(A));

        assert(isrow([1 2 3]) && ~iscolumn([1 2 3]));
        assert(iscolumn([1; 2; 3]) && ~isrow([1; 2; 3]));
        assert(isrow(5) && iscolumn(5) && isscalar(5));
        assert(isvector([1 2 3]) && isvector([1; 2; 3]));
        """);

    [Fact]
    public Task ElementwiseOperationsAndMaps_KeepTheShape() => RunAsserting("""
        A = [1 2; 3 4];
        assert(isequal(size(A + 1), [2 2]));
        assert(isequal(size(A .* A), [2 2]));
        assert(isequal(size(A > 2), [2 2]));
        assert(isequal(size(sqrt(A)), [2 2]));
        assert(isequal(size(-A), [2 2]));
        assert(isequal(A + A, [2 4; 6 8]));

        % A binding copies in the MATLAB dialect, and the copy has to keep the shape too.
        B = A;
        B(1, 1) = 0;
        assert(isequal(size(B), [2 2]));
        assert(A(1, 1) == 1);
        """);

    [Fact]
    public Task MatrixProduct_UsesRealOrientation_AndTurnsAVectorOnlyWhenItMustg() => RunAsserting("""
        A = [1 0; 0 1];

        % A row vector's orientation is incidental, so it is turned to make the product work.
        assert(isequal(A * [4 6], [4; 6]));
        assert(isequal(A * [4; 6], [4; 6]));

        % With real orientation, an inner product and an outer product are both unambiguous.
        u = [1; 2; 3];
        assert(u' * u == 14);
        assert(isequal(size(u * u'), [3 3]));
        """);

    [Fact]
    public async Task TwoBareRowVectors_AreStillRefused_RatherThanGuessedAt()
    {
        // Neither one says which product was meant, and an elementwise answer would be a wrong number.
        string message = await RunFailing("disp([1 2] * [3 4])");
        Assert.Contains("ambiguous", message, StringComparison.Ordinal);
    }

    [Fact]
    public Task LogicalsCompareEqualToTheNumbersTheyStandFor() => RunAsserting("""
        assert(true == 1);
        assert(false == 0);
        assert(~(true == 0));
        assert(isequal([1 0 1] == 1, [true false true]));
        assert(isequal([true false] == [1 0], [true true]));

        % Ordering was always lenient; equality now agrees with it.
        assert(true > 0);
        assert(true >= 1);

        % Text is unrelated to a number: false, never an error.
        assert(isequal([1, 'x'] == 1, [true false]));
        """);

    [Fact]
    public Task NaN_EqualsNothing_ItselfIncluded() => RunAsserting("""
        assert(~(NaN == NaN));
        assert(NaN ~= NaN);
        assert(~isequal(NaN, NaN));
        assert(isequaln(NaN, NaN));
        assert(isequal(isnan([1 NaN 3]), [false true false]));
        """);

    [Fact]
    public Task SwitchMatchesALogicalAgainstANumber() => RunAsserting("""
        picked = 0;
        switch true
            case 1
                picked = 1;
            otherwise
                picked = 2;
        end
        assert(picked == 1);
        """);

    [Fact]
    public Task IsequalComparesSizes_NotJustElements() => RunAsserting("""
        % Same nine numbers in the same order, different shapes: MATLAB says these differ, and the
        % old element-by-element comparison could not tell them apart.
        assert(~isequal([1 2 3], [1; 2; 3]));
        assert(~isequal(reshape(1:6, 2, 3), reshape(1:6, 3, 2)));
        assert(isequal([1 2; 3 4], [1 2; 3 4]));
        """);

    [Fact]
    public Task ReshapeAndFlattenAgreeOnColumnMajorOrder() => RunAsserting("""
        A = reshape(1:6, 2, 3);
        assert(isequal(size(A), [2 3]));
        assert(isequal(A, [1 3 5; 2 4 6]));
        assert(isequal(A(:)', 1:6));
        assert(isequal(reshape(A, 3, 2), [1 4; 2 5; 3 6]));
        """);
}
