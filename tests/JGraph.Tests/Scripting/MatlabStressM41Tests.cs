using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The language and value-model work of M41 (ADR 0044), driven by the MATLAB stress-test scripts:
/// comma statement separators, nested functions, <c>persistent</c>, the recursion limit, N-D arrays,
/// implicit expansion, shaped cells, and struct arrays. Assertions run inside the scripts so they
/// pin MATLAB's answers, not JGraph's display formatting.
/// </summary>
[Collection("JG facade")]
public class MatlabStressM41Tests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabStressM41Tests() => JG.Reset();

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

    private async Task RunExpectingError(string code, string fragment)
    {
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains(fragment, result.Message + _output.ErrorText, StringComparison.OrdinalIgnoreCase);
    }

    // --- Parser: comma separators, one-line bodies, nested functions, persistent ---------------

    [Fact]
    public Task Comma_SeparatesStatements_LikeANewline() => RunAsserting("""
        if 1 < 2, x = 5; end
        for k = 1:3, x = x + 1; end
        while x < 10, x = x + 1; end
        assert(x == 10);
        """);

    [Fact]
    public Task OneLineFunctionBody_ParsesAfterTheComma() => RunAsserting("""
        assert(doubleIt(4) == 8);
        function v = doubleIt(x), v = x * 2; end
        """);

    [Fact]
    public Task NestedFunctions_ShareTheParentWorkspace() => RunAsserting("""
        [inc, get] = makeCounter(10);
        inc(); inc();
        assert(get() == 12);

        function [increment, getValue] = makeCounter(init)
        current = init;
        increment = @doInc;
        getValue = @doGet;
            function doInc()
                current = current + 1;
            end
            function v = doGet()
                v = current;
            end
        end
        """);

    [Fact]
    public Task Persistent_KeepsItsValueAcrossCalls() => RunAsserting("""
        assert(countUp() == 1);
        assert(countUp() == 2);
        assert(countUp() == 3);

        function c = countUp()
        persistent count
        if isempty(count)
            count = 0;
        end
        count = count + 1;
        c = count;
        end
        """);

    [Fact]
    public Task Recursion_Survives400_AndTheLimitIsCatchable() => RunAsserting("""
        assert(deep(400) == 400);
        caught = false;
        try
            deep(100000);
        catch
            caught = true;
        end
        assert(caught);

        function out = deep(n)
        if n <= 1
            out = 1;
        else
            out = 1 + deep(n - 1);
        end
        end
        """);

    [Fact]
    public Task For_IteratesTheColumnsOfAMatrix() => RunAsserting("""
        M = [1 2; 3 4];
        total = 0;
        count = 0;
        for x = M
            assert(iscolumn(x));
            total = total + x(2);
            count = count + 1;
        end
        assert(count == 2);
        assert(total == 7);
        """);

    [Fact]
    public Task Clear_SparesScriptFunctions_AndTakesNames() => RunAsserting("""
        a = 1; b = 2;
        clear a
        assert(~exist('a', 'var'));
        assert(b == 2);
        clear
        assert(~exist('b', 'var'));
        assert(helper(3) == 6);
        clearvars
        assert(helper(4) == 8);

        function y = helper(x)
        y = 2 * x;
        end
        """);

    // --- N-D arrays -----------------------------------------------------------------------------

    [Fact]
    public Task Reshape3D_ReportsItsSizeAndRank() => RunAsserting("""
        A = reshape(1:24, [2 3 4]);
        assert(isequal(size(A), [2 3 4]));
        assert(ndims(A) == 3);
        assert(numel(A) == 24);
        assert(~ismatrix(A));
        assert(~isvector(A));
        """);

    [Fact]
    public Task PageSlice_ReadsTheRightElements() => RunAsserting("""
        A = reshape(1:24, [2 3 4]);
        P = A(:, :, 2);
        assert(isequal(size(P), [2 3]));
        assert(P(1, 1) == 7);
        assert(P(2, 3) == 12);
        """);

    [Fact]
    public Task ThreeSubscripts_ResolveEndPerDimension() => RunAsserting("""
        A = reshape(1:24, [2 3 4]);
        assert(A(2, 3, 1) == 6);
        assert(A(1, 1, 4) == 19);
        assert(A(end, end, end) == 24);
        """);

    [Fact]
    public Task MiddleSlice_KeepsItsSingleton_AndSqueezeDropsIt() => RunAsserting("""
        A = reshape(1:24, [2 3 4]);
        M = A(:, 2, :);
        assert(isequal(size(M), [2 1 4]));
        S = squeeze(M);
        assert(isequal(size(S), [2 4]));
        assert(S(2, 3) == 16);
        """);

    [Fact]
    public Task LinearIndexing_WalksColumnMajorOverAllDimensions() => RunAsserting("""
        A = reshape(1:24, [2 3 4]);
        assert(A(24) == 24);
        assert(A(end) == 24);
        v = A(2:2:end);
        assert(numel(v) == 12);
        assert(v(1) == 2);
        """);

    [Fact]
    public Task NdConstructors_HandleEmptyAndSingletonDimensions() => RunAsserting("""
        E = zeros(5, 0, 2);
        assert(isequal(size(E), [5 0 2]));
        assert(isempty(E));
        R = rand(3, 1, 4);
        assert(isequal(size(R), [3 1 4]));
        """);

    [Fact]
    public Task Permute_ReordersDimensions() => RunAsserting("""
        A = reshape(1:24, [2 3 4]);
        B = permute(A, [2 1 3]);
        assert(isequal(size(B), [3 2 4]));
        assert(B(3, 1, 2) == A(1, 3, 2));
        """);

    [Fact]
    public Task NdWrites_LandInTheRightCells() => RunAsserting("""
        A = reshape(1:24, [2 3 4]);
        A(1, 2, 3) = 99;
        assert(A(1, 2, 3) == 99);
        assert(A(2, 2, 3) == 16);
        A(:, 1, 2) = [0; 0];
        assert(A(1, 1, 2) == 0 && A(2, 1, 2) == 0);
        """);

    [Fact]
    public Task Isequal_ComparesTheFullNdShape() => RunAsserting("""
        A = reshape(1:24, [2 3 4]);
        assert(isequal(A, reshape(A(:), [2 3 4])));
        assert(~isequal(A, reshape(A(:), [2 12])));
        """);

    [Fact]
    public Task ArrayfunAndMasks_WorkOverNd() => RunAsserting("""
        A = reshape(1:8, [2 2 2]);
        B = arrayfun(@(x) x + 1, A);
        assert(isequal(size(B), [2 2 2]));
        assert(B(2, 2, 2) == 9);
        mask = A > 4;
        assert(numel(A(mask)) == 4);
        """);

    // --- Implicit expansion ---------------------------------------------------------------------

    [Fact]
    public Task ColumnPlusRow_IsTheirOuterSum() => RunAsserting("""
        C = [1; 2] + [10 20];
        assert(isequal(C, [11 21; 12 22]));
        """);

    [Fact]
    public Task NdBroadcast_ExpandsSingletonDimensions() => RunAsserting("""
        A = rand(3, 1, 4);
        B = rand(1, 5, 4);
        C = A .* B;
        assert(isequal(size(C), [3 5 4]));
        assert(abs(C(2, 3, 2) - A(2, 1, 2) * B(1, 3, 2)) < 1e-15);
        """);

    [Fact]
    public Task OneByOneArray_BehavesAsAScalar() => RunAsserting("""
        A = magic(4);
        B = A ./ max(A(:));
        assert(abs(max(B(:)) - 1) < 1e-15);
        """);

    [Fact]
    public Task VectorReductions_ReturnTrueScalars() => RunAsserting("""
        assert(isscalar(sum((1:5)')));
        assert(isscalar(max((1:5)')));
        assert(sum((1:5)') == 15);
        c = cumsum((1:3)');
        assert(iscolumn(c));
        assert(isequal(c, [1; 3; 6]));
        """);

    [Fact]
    public Task IncompatibleShapes_StillRefuse() =>
        RunExpectingError("x = [1 2] + [1 2 3];", "different lengths");

    [Fact]
    public Task Bsxfun_AgreesWithTheOperator() => RunAsserting("""
        A = (1:4)';
        B = 1:3;
        assert(isequal(bsxfun(@times, A, B), A .* B));
        """);

    // --- Shaped cells and struct arrays ---------------------------------------------------------

    [Fact]
    public Task CellGrid_TakesTwoBraceSubscripts() => RunAsserting("""
        C = cell(2, 2);
        assert(isequal(size(C), [2 2]));
        C{2, 1} = 5;
        C{1, 2} = 'x';
        assert(C{2, 1} == 5);
        assert(strcmp(C{1, 2}, 'x'));
        assert(C{end, end - 1} == 5);
        """);

    [Fact]
    public Task StructArray_CreatesGrowsAndReadsBack() => RunAsserting("""
        S(5).A = [];
        assert(numel(S) == 5);
        for k = 1:5
            S(k).A = k * 2;
            S(k).Index = k;
        end
        assert(S(3).Index == 3);
        assert(S(5).A == 10);
        S(7).Index = 7;
        assert(numel(S) == 7);
        assert(S(7).Index == 7);
        """);

    [Fact]
    public Task ScalarStruct_AcceptsElementOneWrites() => RunAsserting("""
        S = struct('x', 1);
        S(1).x = 9;
        assert(S.x == 9);
        """);
}
