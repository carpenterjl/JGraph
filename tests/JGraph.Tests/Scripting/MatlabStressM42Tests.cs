using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M42: the numerics the stress scripts demanded — sparse storage and its operators, Gilbert–Peierls
/// LU, Arnoldi eigs, integer-class conversion with <c>.empty</c> statics, the parallel dense matrix
/// product, matrix square root/logarithm round-trips, and ode45. Each fact pins an invariant the
/// kernel must keep, not a printed string.
/// </summary>
[Collection("JG facade")]
public class MatlabStressM42Tests : IDisposable
{
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabStressM42Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private ScriptRunResult RunMatlab(string code)
    {
        var context = new ScriptContext(_output, (_, figure) => _figures.Add(figure), null);
        return JgsRunner.Run(code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
    }

    private string RunAndRead(string code)
    {
        ScriptRunResult result = RunMatlab(code);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText;
    }

    // --- Sparse storage -------------------------------------------------------------------------

    [Fact]
    public void SparseRoundTripsThroughFull()
    {
        string output = RunAndRead("""
            A = [1 0 0; 0 2 0; 3 0 0];
            S = sparse(A);
            fprintf('%d %d %d\n', issparse(S), nnz(S), isequal(full(S), A));
            """);

        Assert.Contains("1 3 1", output, StringComparison.Ordinal);
    }

    [Fact]
    public void SparseBuildsFromTriplets()
    {
        string output = RunAndRead("""
            S = sparse([1 2], [2 1], [5 6], 2, 3);
            fprintf('%d\n', isequal(full(S), [0 5 0; 6 0 0]));
            """);

        Assert.Contains("1", output, StringComparison.Ordinal);
    }

    [Fact]
    public void SparseArithmeticMatchesDense()
    {
        string output = RunAndRead("""
            A = [1 0 2; 0 3 0; 4 0 5];
            B = [0 1 0; 2 0 3; 0 4 0];
            SA = sparse(A); SB = sparse(B);
            ok1 = isequal(full(SA + SB), A + B);
            ok2 = isequal(full(SA - SB), A - B);
            ok3 = isequal(full(SA * SB), A * B);
            ok4 = isequal(full(2 * SA), 2 * A);
            fprintf('%d %d %d %d\n', ok1, ok2, ok3, ok4);
            """);

        Assert.Contains("1 1 1 1", output, StringComparison.Ordinal);
    }

    [Fact]
    public void SparseTimesDenseIsDense()
    {
        string output = RunAndRead("""
            A = [1 0 2; 0 3 0; 4 0 5];
            x = [1; 2; 3];
            y = sparse(A) * x;
            fprintf('%d %d\n', issparse(y), isequal(y, A * x));
            """);

        Assert.Contains("0 1", output, StringComparison.Ordinal);
    }

    [Fact]
    public void SparseLuReassemblesTheMatrix()
    {
        string output = RunAndRead("""
            S = sparse(magic(6));
            [L, U] = lu(S);
            err = max(max(abs(full(L * U) - full(S))));
            fprintf('%d\n', err < 1e-10);
            """);

        Assert.Contains("1", output, StringComparison.Ordinal);
    }

    [Fact]
    public void SparseUnsupportedOperatorPointsAtFull()
    {
        ScriptRunResult result = RunMatlab("S = sparse(eye(3));\nT = S ./ S;");

        Assert.False(result.Success);
        Assert.Contains("full", result.Message!, StringComparison.Ordinal);
    }

    // --- eigs -----------------------------------------------------------------------------------

    [Fact]
    public void EigsFindsTheLargestEigenvalues()
    {
        // Diagonal matrix: the spectrum is on the diagonal, so the two largest are 9 and 7.
        string output = RunAndRead("""
            A = diag([1 9 3 7 5]);
            e = eigs(sparse(A), 2);
            fprintf('%d\n', max(abs(sort(abs(e)) - [7; 9])) < 1e-8);
            """);

        Assert.Contains("1", output, StringComparison.Ordinal);
    }

    [Fact]
    public void EigsVectorsSatisfyTheDefinition()
    {
        string output = RunAndRead("""
            A = [4 1 0; 1 3 1; 0 1 2];
            [V, D] = eigs(sparse(A), 1);
            residual = max(abs(A * V - V * D));
            fprintf('%d\n', residual < 1e-6);
            """);

        Assert.Contains("1", output, StringComparison.Ordinal);
    }

    // --- Integer classes ------------------------------------------------------------------------

    [Fact]
    public void IntegerClassesRoundAndSaturate()
    {
        string output = RunAndRead("""
            fprintf('%d %d %d %d %d\n', uint8(300), uint8(-5), int8(-200), uint8(2.5), int16(NaN));
            """);

        Assert.Contains("255 0 -128 3 0", output, StringComparison.Ordinal);
    }

    [Fact]
    public void IntegerEmptyStaticBuildsAShapedEmpty()
    {
        string output = RunAndRead("""
            E = uint8.empty(0, 5);
            fprintf('%d %d\n', isempty(E), isequal(size(E), [0 5]));
            """);

        Assert.Contains("1 1", output, StringComparison.Ordinal);
    }

    // --- Matrix functions and the dense product -------------------------------------------------

    [Fact]
    public void SqrtmAndLogmRoundTrip()
    {
        string output = RunAndRead("""
            A = [4 1; 2 3];
            X = sqrtm(A);
            ok1 = max(max(abs(X * X - A))) < 1e-8;
            B = [0.2 0.1; 0 0.3];
            ok2 = max(max(abs(logm(expm(B)) - B))) < 1e-8;
            fprintf('%d %d\n', ok1, ok2);
            """);

        Assert.Contains("1 1", output, StringComparison.Ordinal);
    }

    [Fact]
    public void DenseProductMatchesTheHandComputedAnswer()
    {
        Assert.Contains("1", RunAndRead(
            "fprintf('%d\\n', isequal([1 2; 3 4] * [5 6; 7 8], [19 22; 43 50]));"), StringComparison.Ordinal);
    }

    [Fact]
    public void ComplexEigenvaluesKeepTraceAndDeterminant()
    {
        // The rotation matrix has eigenvalues ±i: their sum is the trace (0), their product det (1).
        string output = RunAndRead("""
            e = eig([0 -1; 1 0]);
            s = e(1) + e(2);
            p = e(1) * e(2);
            fprintf('%d %d\n', abs(s) < 1e-12, abs(p - 1) < 1e-12);
            """);

        Assert.Contains("1 1", output, StringComparison.Ordinal);
    }

    // --- ode45 ----------------------------------------------------------------------------------

    [Fact]
    public void Ode45TracksTheCosine()
    {
        // y'' = -y as a first-order system from [1; 0]: the first component is cos(t).
        string output = RunAndRead("""
            f = @(t, y) [y(2); -y(1)];
            [t, y] = ode45(f, [0 pi], [1; 0]);
            fprintf('%d\n', abs(y(end, 1) - (-1)) < 5e-3);
            """);

        Assert.Contains("1", output, StringComparison.Ordinal);
    }

    // --- Plot options ---------------------------------------------------------------------------

    [Fact]
    public void PlotAcceptsNameValuePairsAndMatrixColumns()
    {
        ScriptRunResult result = RunMatlab("""
            t = (0:0.1:1)';
            Y = [t, 2 * t];
            plot(t, Y, 'LineWidth', 2, 'Color', 'r');
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
    }
}
