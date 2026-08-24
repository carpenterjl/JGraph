using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M88: the script-visible face of the provider-backed matrix product and the packed transpose
/// fast path. The shape rules, the error texts, and the tag-carrying behavior of <c>*</c> and
/// <c>'</c> must be exactly what the boxed paths produced — these pin them.
/// </summary>
[Collection("JG facade")]
public class MatlabLinalgProviderM88Tests : IDisposable
{
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabLinalgProviderM88Tests() => JG.Reset();

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

    [Fact]
    public void MatrixProductGivesExactSmallValues()
    {
        string output = RunAndRead("""
            M = [1 2; 3 4] * [5 6; 7 8];
            fprintf('%g %g %g %g\n', M(1,1), M(1,2), M(2,1), M(2,2));
            """);

        Assert.Contains("19 22 43 50", output, StringComparison.Ordinal);
    }

    [Fact]
    public void InnerProductCollapsesToAScalar()
    {
        string output = RunAndRead("""
            r = [1 2 3] * [4; 5; 6];
            fprintf('%g %d\n', r, isscalar(r));
            """);

        Assert.Contains("32 1", output, StringComparison.Ordinal);
    }

    [Fact]
    public void OuterProductKeepsItsShape()
    {
        string output = RunAndRead("""
            M = [1; 2; 3] * [4 5];
            fprintf('%d %d %g %g\n', size(M, 1), size(M, 2), M(3,1), M(3,2));
            """);

        Assert.Contains("3 2 12 15", output, StringComparison.Ordinal);
    }

    [Fact]
    public void MatrixTimesVectorReorientsTheVector()
    {
        // The row [1 1] does not conform as written, so it is stood up as a column.
        string output = RunAndRead("""
            v = [1 2; 3 4] * [1 1];
            fprintf('%g %g\n', v(1), v(2));
            """);

        Assert.Contains("3 7", output, StringComparison.Ordinal);
    }

    [Fact]
    public void RowTimesRowIsRefusedAsAmbiguous()
    {
        ScriptRunResult result = RunMatlab("r = [1 2 3] * [4 5 6];");
        Assert.False(result.Success);
        Assert.Contains("ambiguous", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MismatchedMatricesReportTheirDimensions()
    {
        ScriptRunResult result = RunMatlab("M = [1 2; 3 4] * [1 2; 3 4; 5 6];");
        Assert.False(result.Success);
        Assert.Contains("the left has 2 columns and the right has 3 rows", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TransposeRoundTripsAndFlipsShape()
    {
        string output = RunAndRead("""
            A = [1 2 3; 4 5 6];
            B = A';
            C = B';
            fprintf('%d %d %g %g %d\n', size(B, 1), size(B, 2), B(3,1), B(1,2), isequal(A, C));
            """);

        Assert.Contains("3 2 3 4 1", output, StringComparison.Ordinal);
    }

    [Fact]
    public void TransposeKeepsALogicalLogical()
    {
        string output = RunAndRead("""
            L = [true false true; false true false]';
            fprintf('%d %d %d\n', islogical(L), size(L, 1), sum(L(:)));
            """);

        Assert.Contains("1 3 3", output, StringComparison.Ordinal);
    }

    [Fact]
    public void TransposeKeepsAnIntegerClass()
    {
        string output = RunAndRead("""
            K = int8([1 2; 3 4])';
            fprintf('%s %d\n', class(K), K(1,2));
            """);

        Assert.Contains("int8 3", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ColumnVectorTransposeIsARow()
    {
        string output = RunAndRead("""
            v = (1:5)';
            w = v';
            fprintf('%d %d %d %d\n', size(v, 1), size(v, 2), size(w, 1), size(w, 2));
            """);

        Assert.Contains("5 1 1 5", output, StringComparison.Ordinal);
    }

    [Fact]
    public void GramProductIsExactlySymmetric()
    {
        // The syrk recognition: A'*A must satisfy isequal(B, B') bitwise, or ldl refuses its own
        // input — the stress suite's stess_6 caught exactly this under the blocked native kernel.
        string output = RunAndRead("""
            n = 40;
            [I, J] = meshgrid(1:n, 1:n);
            A = sin(0.7*I) + cos(1.3*J);
            B = A'*A;
            C = A*A';
            [L, D] = ldl(B + n*eye(n));
            fprintf('%d %d %g\n', isequal(B, B'), isequal(C, C'), round(sum(diag(D))));
            """);

        string[] parts = output.Trim().Split(' ');
        Assert.Equal("1", parts[0]);
        Assert.Equal("1", parts[1]);
    }

    [Fact]
    public void GramProductValuesAreRight()
    {
        string output = RunAndRead("""
            A = [1 2; 3 4; 5 6];
            B = A'*A;
            C = A*A';
            r = A(2,:) * A(2,:)';
            fprintf('%g %g %g %g %g %g\n', B(1,1), B(1,2), B(2,2), C(1,1), C(3,3), r);
            """);

        Assert.Contains("35 44 56 5 61 25", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ComplexProductStillWorksThroughTheBoxedPath()
    {
        string output = RunAndRead("""
            M = [1+2i 0; 0 1] * [1; 1i];
            fprintf('%g %g\n', real(M(1)), imag(M(1)));
            """);

        Assert.Contains("1 2", output, StringComparison.Ordinal);
    }

    [Fact]
    public void VersionReportsTheLinalgBackend()
    {
        string output = RunAndRead("disp(version('-blas'));");
        Assert.True(
            output.Contains("OpenBLAS", StringComparison.Ordinal)
            || output.Contains("managed", StringComparison.Ordinal),
            $"unexpected -blas report: {output}");
    }
}
