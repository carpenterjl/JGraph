using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// V6 (ADR 0167), third sub-stage: indexed assignment into a sparse matrix (appendix A #26). A
/// write rebuilds the stored entries — the answer stays sparse, a written zero is dropped, a
/// subscript past the extent grows the matrix — and is stored back through the target's own
/// entry, so an alias taken before the write keeps what it had.
/// </summary>
/// <remarks>
/// The parity fixture <c>sparse_indexed_assign</c> holds R2025b's answers; these pin the roads the
/// sub-stage touched, one assertion a road, so a regression names itself.
/// </remarks>
[Collection("JG facade")]
public class SparseIndexedAssignM167Tests : IDisposable
{
    private readonly MatlabScriptEngine _engine = new();
    private readonly RecordingScriptOutput _output = new();

    public SparseIndexedAssignM167Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private string RunAndRead(string code)
    {
        ScriptRunResult result = _engine.RunAsync(
            code,
            new ScriptContext(_output, (_, _) => { }, Path.GetTempPath(), resolvePath: null, figureFiles: new TestFigureFiles()),
            CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Trim();
    }

    [Theory]
    [InlineData("T(1, 1) = 7;", "3 [7 0 2;0 3 0]")]
    [InlineData("T(2, 1) = 5;", "4 [1 0 2;5 3 0]")]
    [InlineData("T(2, 2) = 0;", "2 [1 0 2;0 0 0]")]
    [InlineData("T(2, 1) = 0;", "3 [1 0 2;0 3 0]")]
    [InlineData("T(4) = 9;", "3 [1 0 2;0 9 0]")]
    [InlineData("T([2 6]) = [8 9];", "5 [1 0 2;8 3 9]")]
    [InlineData("T(2, :) = [7 0 9];", "4 [1 0 2;7 0 9]")]
    [InlineData("T(:, 2) = [5; 0];", "3 [1 5 2;0 0 0]")]
    [InlineData("T(1:2, 2:3) = 6;", "5 [1 6 6;0 6 6]")]
    [InlineData("T(:) = 1:6;", "6 [1 3 5;2 4 6]")]
    [InlineData("T(logical([1 0 0; 0 1 1])) = [7 8 9];", "4 [7 0 2;0 8 9]")]
    [InlineData("T(T > 1) = 0;", "1 [1 0 0;0 0 0]")]
    [InlineData("T(end, end) = 4; T(end) = 5; T(1, end - 1) = 6;", "5 [1 6 2;0 3 5]")]
    [InlineData("T(1, :) = sparse([0 4 0]);", "2 [0 4 0;0 3 0]")]
    [InlineData("T([2 1], :) = T;", "3 [0 3 0;1 0 2]")]
    [InlineData("T(1, 2) = true;", "4 [1 1 2;0 3 0]")]
    public void AWriteRebuildsTheCopyAndLeavesTheOriginal(string write, string expected)
    {
        string text = RunAndRead($$"""
            S = sparse([1 0 2; 0 3 0]);
            T = S;
            {{write}}
            fprintf('%d %s\n', nnz(T), mat2str(full(T)));
            assert(issparse(T) && issparse(S) && isequal(full(S), [1 0 2; 0 3 0]));
            """);
        Assert.Equal(expected, text);
    }

    [Fact]
    public void AWritePastTheExtentGrowsAndALinearOneOnlyGrowsAVector()
    {
        string text = RunAndRead("""
            S = sparse([1 0 2; 0 3 0]);
            S(3, 5) = 1;
            V = sparse([1 0 2]);
            V(6) = 3;
            fprintf('%s %d %s %s\n', mat2str(size(S)), nnz(S), mat2str(size(V)), mat2str(full(V)));
            M = sparse([1 0; 0 2]);
            try
                M(9) = 1;
            catch err
                disp(err.message);
            end
            fprintf('%s\n', mat2str(full(M)));
            """);
        Assert.Equal("[3 5] 4 [1 6] [1 0 2 0 0 3]\nIn an assignment  A(I) = B, a matrix A cannot be resized.\n[1 0;0 2]",
            text.Replace("\r", ""));
    }

    [Theory]
    [InlineData("T(1, :) = [];", "[1 3] [0 3 0]")]
    [InlineData("T(:, 2) = [];", "[2 2] [1 2;0 0]")]
    [InlineData("T(logical([0 1]), :) = [];", "[1 3] [1 0 2]")]
    public void ADeletionClosesTheMatrixUpAndLeavesTheOriginal(string write, string expected)
    {
        string text = RunAndRead($$"""
            S = sparse([1 0 2; 0 3 0]);
            T = S;
            {{write}}
            fprintf('%s %s\n', mat2str(size(T)), mat2str(full(T)));
            assert(issparse(T) && isequal(size(S), [2 3]));
            """);
        Assert.Equal(expected, text);
    }

    [Fact]
    public void AWriteThroughACellAFieldAndAnArgumentLandsInThatHolderAlone()
    {
        string text = RunAndRead("""
            S = sparse([1 0 2; 0 3 0]);
            c = {S}; d = c;
            d{1}(1, 1) = 9;
            st.m = S; r = st;
            r.m(2, 2) = 0;
            fprintf('%d %d %d %d %d\n', full(c{1}(1, 1)), full(d{1}(1, 1)), nnz(st.m), nnz(r.m), issparse(r.m));
            """);
        Assert.Equal("1 9 3 2 1", text);
    }

    [Theory]
    [InlineData("S(1, :) = [1 2];", "not compatible with the size of the right side")]
    [InlineData("S(0, 1) = 1;", "Array indices must be positive integers or logical values.")]
    [InlineData("S(1, 1, 2) = 1;", "N-dimensional indexing allowed for full matrices only.")]
    [InlineData("S(1, 1) = {1};", "Conversion to double from cell is not possible.")]
    public void ARefusedWriteSaysWhyAndLeavesTheMatrix(string write, string fragment)
    {
        string text = RunAndRead($$"""
            S = sparse([1 0 2; 0 3 0]);
            try
                {{write}}
                disp('no error');
            catch err
                fprintf('%d %d %s\n', contains(err.message, '{{fragment}}'), nnz(S), mat2str(full(S)));
            end
            """);
        Assert.Equal("1 3 [1 0 2;0 3 0]", text);
    }

    [Fact]
    public void ASparseRightHandSideGoesIntoADenseArrayAsItsValues()
    {
        string text = RunAndRead("""
            A = zeros(2, 3);
            A(1, :) = sparse([1 0 2]);
            A(2, 2) = sparse(5);
            fprintf('%d %s\n', issparse(A), mat2str(A));
            """);
        Assert.Equal("0 [1 0 2;0 5 0]", text);
    }

    [Fact]
    public void ALoopFillsADiagonalOneEntryAtATime()
    {
        string text = RunAndRead("""
            S = sparse(4, 4);
            for k = 1:4
                S(k, k) = k;
            end
            fprintf('%d %d %s\n', issparse(S), nnz(S), mat2str(full(diag(S))'));
            """);
        Assert.Equal("1 4 [1 2 3 4]", text);
    }
}
