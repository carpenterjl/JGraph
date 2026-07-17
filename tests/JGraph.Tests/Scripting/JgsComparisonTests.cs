using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>M18: element-wise comparisons and equality — the mask-building half of logical indexing.</summary>
[Collection("JG facade")]
public class JgsComparisonTests : IDisposable
{
    private readonly JgsScriptEngine _engine = new();
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public JgsComparisonTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private Task<ScriptRunResult> Run(string code) =>
        _engine.RunAsync(code, new ScriptContext(_output, (_, figure) => _figures.Add(figure), null), default);

    [Fact]
    public async Task Comparison_ArrayVersusScalar_YieldsMask_BothOperandOrders()
    {
        ScriptRunResult result = await Run("""
            let a = [1, 5, 3]
            print(a > 2)
            print(2 < a)
            print(a <= 3)
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("[false, true, true]", _output.NormalText);
        Assert.Contains("[true, false, true]", _output.NormalText);
    }

    [Fact]
    public async Task Comparison_ArrayVersusArray_IsPairwise()
    {
        ScriptRunResult result = await Run("print([1, 2, 3] >= [3, 2, 1])");

        Assert.True(result.Success, result.Message);
        Assert.Contains("[false, true, true]", _output.NormalText);
    }

    [Fact]
    public async Task Comparison_ArraysOfDifferentLengths_IsRuntimeError()
    {
        ScriptRunResult result = await Run("print([1, 2] > [1, 2, 3])");

        Assert.False(result.Success);
        Assert.Contains("different lengths", Assert.Single(result.Diagnostics).Message);
    }

    [Fact]
    public async Task Comparison_ScalarScalar_StillYieldsSingleBool()
    {
        ScriptRunResult result = await Run("""
            print(3 > 2)
            print(2 > 3)
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("true", _output.NormalText);
        Assert.Contains("false", _output.NormalText);
    }

    [Fact]
    public async Task Equality_StringArrayVersusScalar_YieldsMask()
    {
        // The serial-number workflow: ids == "SN-2" selects that device's rows.
        ScriptRunResult result = await Run("""
            let ids = ["SN-1", "SN-2", "SN-1"]
            print(ids == "SN-1")
            print(ids != "SN-1")
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("[true, false, true]", _output.NormalText);
        Assert.Contains("[false, true, false]", _output.NormalText);
    }

    [Fact]
    public async Task Equality_MixedElementTypes_CompareUnequal_NotAnError()
    {
        ScriptRunResult result = await Run("print([1, \"1\", true] == 1)");

        Assert.True(result.Success, result.Message);
        Assert.Contains("[true, false, false]", _output.NormalText);
    }

    [Fact]
    public async Task Equality_ArrayVersusArray_IsPairwise_AndLengthChecked()
    {
        ScriptRunResult ok = await Run("print([1, 2] == [1, 3])");
        Assert.True(ok.Success, ok.Message);
        Assert.Contains("[true, false]", _output.NormalText);

        ScriptRunResult bad = await Run("print([1] == [1, 2])");
        Assert.False(bad.Success);
        Assert.Contains("different lengths", Assert.Single(bad.Diagnostics).Message);
    }

    [Fact]
    public async Task Isequal_IsDeepWholeValueEquality_ReturningOneBool()
    {
        ScriptRunResult result = await Run("""
            print(isequal([1, [2, 3]], [1, [2, 3]]))
            print(isequal([1, 2], [1, 3]))
            print(isequal([1, 2], [1, 2, 3]))
            print(isequal("a", "a"))
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal("true\nfalse\nfalse\ntrue", _output.NormalText.Trim().Replace("\r", ""));
    }

    [Fact]
    public async Task BoolArithmetic_MaskSumsAndMultiplies_AsZeroOne()
    {
        ScriptRunResult result = await Run("""
            let mask = [1, 5, 3] > 2
            print(sum(mask))
            print(mask * 10)
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("2", _output.NormalText);
        Assert.Contains("[0, 10, 10]", _output.NormalText);
    }
}
