using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M28.S4: running MATLAB. Where the two languages disagree about what a program *means* — the index
/// base, whether assignment copies, what <c>*</c> does, whether a block has a scope of its own — these
/// tests pin MATLAB's answer and, alongside it, that JGS still gives its own.
/// </summary>
[Collection("JG facade")]
public class MatlabSemanticsTests : IDisposable
{
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabSemanticsTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private ScriptRunResult RunMatlab(string code) => Run(code, JgsDialect.Matlab);

    private ScriptRunResult Run(string code, JgsDialect dialect)
    {
        var context = new ScriptContext(_output, (_, figure) => _figures.Add(figure), null);
        return JgsRunner.Run(code, context, default, sourceId: "", hook: null, dialect);
    }

    private string Output => _output.NormalText;

    private string RunAndRead(string code)
    {
        ScriptRunResult result = RunMatlab(code);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return Output;
    }

    // --- Indexing -------------------------------------------------------------------------------

    [Fact]
    public void Indexing_CountsFromOne()
    {
        Assert.Contains("10", RunAndRead("x = [10 20 30];\ndisp(x(1))"), StringComparison.Ordinal);
        _output.Normal.Clear();
        Assert.Contains("30", RunAndRead("x = [10 20 30];\ndisp(x(end))"), StringComparison.Ordinal);
        _output.Normal.Clear();
        Assert.Contains("[20, 30]", RunAndRead("x = [10 20 30];\ndisp(x(2:end))"), StringComparison.Ordinal);
    }

    [Fact]
    public void IndexZero_IsOutOfRange_AndSaysWhy()
    {
        ScriptRunResult result = RunMatlab("x = [10 20 30];\ndisp(x(0))");

        Assert.False(result.Success);
        Assert.Contains("out of range", result.Message!, StringComparison.Ordinal);
        Assert.Contains("1-based", result.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public void Jgs_StillCountsFromZero()
    {
        ScriptRunResult result = Run("let x = [10, 20, 30]\ndisp(x(0))\ndisp(x(end))", JgsDialect.Jgs);

        Assert.True(result.Success, result.Message);
        Assert.Contains("10", Output, StringComparison.Ordinal);
        Assert.Contains("30", Output, StringComparison.Ordinal);
    }

    // --- Declaration and scope ------------------------------------------------------------------

    [Fact]
    public void AssignmentNeedsNoLet()
    {
        Assert.Contains("7", RunAndRead("x = 7;\ndisp(x)"), StringComparison.Ordinal);
    }

    [Fact]
    public void Jgs_StillRequiresLet()
    {
        ScriptRunResult result = Run("x = 7", JgsDialect.Jgs);

        Assert.False(result.Success);
        Assert.Contains("Declare it first with 'let'", result.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public void ABlockHasNoScopeOfItsOwn()
    {
        // MATLAB has function scope: a variable first assigned inside an 'if' outlives it.
        Assert.Contains("2", RunAndRead("if true\n  y = 2;\nend\ndisp(y)"), StringComparison.Ordinal);

        _output.Normal.Clear();
        Assert.Contains("6", RunAndRead("total = 0;\nfor k = 1:3\n  total = total + k;\nend\ndisp(total)"), StringComparison.Ordinal);
    }

    [Fact]
    public void Jgs_StillScopesEachBlock()
    {
        ScriptRunResult result = Run("if true { let y = 2 }\ndisp(y)", JgsDialect.Jgs);

        Assert.False(result.Success);
        Assert.Contains("'y' is not defined", result.Message!, StringComparison.Ordinal);
    }

    // --- Copy semantics -------------------------------------------------------------------------

    [Fact]
    public void AssignmentCopiesAnArray()
    {
        // The whole reason this matters: with shared references, 'a' would read 99 here.
        Assert.Contains("1", RunAndRead("a = [1 2 3];\nb = a;\nb(1) = 99;\ndisp(a(1))"), StringComparison.Ordinal);
    }

    [Fact]
    public void AFunctionCannotWriteThroughItsArgument()
    {
        string output = RunAndRead("""
            a = [1 2 3];
            clobber(a);
            disp(a(1))

            function clobber(v)
            v(1) = 99;
            end
            """);

        Assert.Contains("1", output, StringComparison.Ordinal);
        Assert.DoesNotContain("99", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Jgs_StillSharesTheReference()
    {
        ScriptRunResult result = Run("let a = [1, 2, 3]\nlet b = a\nb(0) = 99\ndisp(a(0))", JgsDialect.Jgs);

        Assert.True(result.Success, result.Message);
        Assert.Contains("99", Output, StringComparison.Ordinal);
    }

    // --- Operators ------------------------------------------------------------------------------

    [Fact]
    public void DottedOperators_AreElementwise()
    {
        Assert.Contains("[3, 8]", RunAndRead("disp([1 2] .* [3 4])"), StringComparison.Ordinal);
        _output.Normal.Clear();
        Assert.Contains("[1, 4, 9]", RunAndRead("disp([1 2 3] .^ 2)"), StringComparison.Ordinal);
    }

    [Fact]
    public void StarBetweenAMatrixAndAVector_IsAMatrixProduct()
    {
        Assert.Contains("[4, 6]", RunAndRead("A = [1 0; 0 1];\nv = [4 6];\ndisp(A * v)"), StringComparison.Ordinal);
    }

    [Fact]
    public void StarBetweenTwoVectors_IsRefused_NotGuessedAt()
    {
        // An elementwise answer here would be a wrong number, which is worse than an error.
        ScriptRunResult result = RunMatlab("disp([1 2] * [3 4])");

        Assert.False(result.Success);
        Assert.Contains("ambiguous", result.Message!, StringComparison.Ordinal);
        Assert.Contains(".*", result.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public void ScalarsMultiplyNormally()
    {
        Assert.Contains("12", RunAndRead("disp(3 * 4)"), StringComparison.Ordinal);
        _output.Normal.Clear();
        Assert.Contains("[2, 4, 6]", RunAndRead("disp(2 * [1 2 3])"), StringComparison.Ordinal);
    }

    [Fact]
    public void Jgs_StarIsStillElementwise()
    {
        ScriptRunResult result = Run("disp([1, 2] * [3, 4])", JgsDialect.Jgs);

        Assert.True(result.Success, result.Message);
        Assert.Contains("[3, 8]", Output, StringComparison.Ordinal);
    }

    [Fact]
    public void ElementwiseLogicals_ProduceAMask()
    {
        Assert.Contains("[true, false, false]", RunAndRead("disp([1 0 1] & [1 1 0])"), StringComparison.Ordinal);
        _output.Normal.Clear();
        Assert.Contains("[true, true, false]", RunAndRead("disp([1 0 0] | [0 1 0])"), StringComparison.Ordinal);
    }

    // --- Transpose ------------------------------------------------------------------------------

    [Fact]
    public void TransposingAVector_KeepsItsValues()
    {
        // JGraph's arrays carry no row/column orientation, so the ubiquitous column idiom is a copy.
        Assert.Contains("[1, 2, 3]", RunAndRead("disp((1:3)')"), StringComparison.Ordinal);
    }

    [Fact]
    public void TransposingAMatrix_SwapsRowsAndColumns()
    {
        Assert.Contains("[[1, 3], [2, 4]]", RunAndRead("A = [1 2; 3 4];\ndisp(A')"), StringComparison.Ordinal);
    }

    [Fact]
    public void ConjugateTranspose_FlipsTheImaginaryPart()
    {
        string conjugated = RunAndRead("disp([1+2i]')");
        Assert.Contains("-2", conjugated, StringComparison.Ordinal);

        _output.Normal.Clear();
        string plain = RunAndRead("disp([1+2i].')");
        Assert.DoesNotContain("-2", plain, StringComparison.Ordinal);
    }

    // --- Comments and continuations, end to end -------------------------------------------------

    [Fact]
    public void AScriptWithMatlabSpellings_RunsUnchanged()
    {
        string output = RunAndRead("""
            % Sum the odd entries of a vector, MATLAB-style throughout.
            values = [3 -4 5 ...
                      -6 7];
            total = 0;
            for k = 1:numel(values)
                if mod(values(k), 2) ~= 0
                    total = total + values(k);
                end
            end
            fprintf('total = %d\n', total);
            """);

        Assert.Contains("total = 15", output, StringComparison.Ordinal);
    }
}
