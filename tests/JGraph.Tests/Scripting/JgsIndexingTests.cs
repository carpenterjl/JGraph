using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>M18: logical indexing / gather — both `data(mask)` (MATLAB parens) and `data[mask]`.</summary>
[Collection("JG facade")]
public class JgsIndexingTests : IDisposable
{
    private readonly JgsScriptEngine _engine = new();
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public JgsIndexingTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private Task<ScriptRunResult> Run(string code) =>
        _engine.RunAsync(code, new ScriptContext(_output, (_, figure) => _figures.Add(figure), null), default);

    [Fact]
    public async Task MaskGather_ParenForm_FiltersTheArray()
    {
        // The user's canonical example: let selected = data(parameter > threshold).
        ScriptRunResult result = await Run("""
            let data = [10, 20, 30, 40]
            let parameter = [1, 9, 2, 8]
            let selected = data(parameter > 5)
            print(selected)
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("[20, 40]", _output.NormalText);
    }

    [Fact]
    public async Task MaskGather_BracketForm_IsEquivalent()
    {
        ScriptRunResult result = await Run("""
            let data = [10, 20, 30]
            print(data[data > 15])
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("[20, 30]", _output.NormalText);
    }

    [Fact]
    public async Task IndexArrayGather_KeepsOrder_AndAllowsRepeats()
    {
        ScriptRunResult result = await Run("""
            let a = [10, 20, 30]
            print(a([2, 0, 0]))
            print(a[[1, 2]])
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("[30, 10, 10]", _output.NormalText);
        Assert.Contains("[20, 30]", _output.NormalText);
    }

    [Fact]
    public async Task ScalarIndexing_BothForms_SelectOneElement()
    {
        ScriptRunResult result = await Run("""
            let a = [10, 20, 30]
            print(a[1])
            print(a(1))
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal("20\n20", _output.NormalText.Trim().Replace("\r", ""));
    }

    [Fact]
    public async Task StringGather_YieldsAString()
    {
        ScriptRunResult result = await Run("""
            let s = "abcdef"
            print(s([0, 2, 4]))
            print(s[[true, true, false, false, false, true]])
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("ace", _output.NormalText);
        Assert.Contains("abf", _output.NormalText);
    }

    [Fact]
    public async Task MaskLengthMismatch_IsRuntimeError()
    {
        ScriptRunResult result = await Run("print([1, 2, 3]([true, false]))");

        Assert.False(result.Success);
        Assert.Contains("mask must match", Assert.Single(result.Diagnostics).Message);
    }

    [Fact]
    public async Task MixedIndexArray_IsRuntimeError()
    {
        ScriptRunResult result = await Run("print([1, 2, 3]([0, true, 2]))");

        Assert.False(result.Success);
        Assert.Contains("all numbers", Assert.Single(result.Diagnostics).Message);
    }

    [Fact]
    public async Task ParenIndexing_WithWrongArgumentCount_IsRuntimeError()
    {
        ScriptRunResult result = await Run("print([1, 2, 3](0, 1))");

        Assert.False(result.Success);
        Assert.Contains("exactly one argument", Assert.Single(result.Diagnostics).Message);
    }

    [Fact]
    public async Task CallingANonIndexableValue_StillErrors()
    {
        ScriptRunResult result = await Run("let n = 5\nprint(n(0))");

        Assert.False(result.Success);
        Assert.Contains("Cannot call a number", Assert.Single(result.Diagnostics).Message);
    }

    [Fact]
    public async Task IndexMapping_FindThenGather_StaysAlignedAcrossColumns()
    {
        // Rows found in one column select the same rows from every other column.
        ScriptRunResult result = await Run("""
            let temp = [70, 90, 80, 95]
            let volt = [3.1, 3.2, 3.3, 3.4]
            let hot = find(temp > 85)
            print(hot)
            print(volt(hot))
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("[1, 3]", _output.NormalText);
        Assert.Contains("[3.2, 3.4]", _output.NormalText);
    }
}
