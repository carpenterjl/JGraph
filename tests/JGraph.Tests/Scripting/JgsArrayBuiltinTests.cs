using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>M18: the array-operation builtins (sort, unique, find, slice, concat, …).</summary>
[Collection("JG facade")]
public class JgsArrayBuiltinTests : IDisposable
{
    private readonly JgsScriptEngine _engine = new();
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public JgsArrayBuiltinTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private Task<ScriptRunResult> Run(string code) =>
        _engine.RunAsync(code, new ScriptContext(_output, (_, figure) => _figures.Add(figure), null), default);

    private async Task<string> Eval(string expression)
    {
        _output.Normal.Clear();
        ScriptRunResult result = await Run($"print({expression})");
        Assert.True(result.Success, result.Message);
        return _output.NormalText.Trim();
    }

    [Fact]
    public async Task Sort_Numeric_AscendingByDefault_DescendingOnRequest()
    {
        Assert.Equal("[1, 2, 3]", await Eval("sort([3, 1, 2])"));
        Assert.Equal("[3, 2, 1]", await Eval("sort([3, 1, 2], \"desc\")"));
        Assert.Equal("[1, 2, 3]", await Eval("sort([3, 1, 2], \"asc\")"));
    }

    [Fact]
    public async Task Sort_Strings_Ordinal()
    {
        Assert.Equal("[SN-1, SN-10, SN-2]", await Eval("sort([\"SN-2\", \"SN-10\", \"SN-1\"])"));
    }

    [Fact]
    public async Task Sort_MixedKinds_IsRuntimeError()
    {
        ScriptRunResult result = await Run("print(sort([1, \"a\"]))");

        Assert.False(result.Success);
        Assert.Contains("all numbers or all strings", Assert.Single(result.Diagnostics).Message);
    }

    [Fact]
    public async Task Unique_SortedDistinct_NumbersAndStrings()
    {
        Assert.Equal("[1, 2, 3]", await Eval("unique([3, 1, 2, 3, 1])"));
        Assert.Equal("[a, b]", await Eval("unique([\"b\", \"a\", \"b\"])"));
        Assert.Equal("[]", await Eval("unique([])"));
    }

    [Fact]
    public async Task Find_ReturnsOneBasedIndicesOfTruthyElements()
    {
        // 1-based since M21, pairing with MATLAB paren indexing: volt(find(temp > 85)).
        Assert.Equal("[2, 4]", await Eval("find([0, 1, 0, 1])"));
        Assert.Equal("[1, 3]", await Eval("find([true, false, true])"));
        Assert.Equal("[]", await Eval("find([false])"));
    }

    [Fact]
    public async Task AnyAll_ReduceToOneBool()
    {
        Assert.Equal("true", await Eval("any([false, true])"));
        Assert.Equal("false", await Eval("any([])"));
        Assert.Equal("false", await Eval("all([true, false])"));
        Assert.Equal("true", await Eval("all([])"));
    }

    [Fact]
    public async Task Numel_MatchesLength_ForArraysAndStrings()
    {
        Assert.Equal("3", await Eval("numel([1, 2, 3])"));
        Assert.Equal("5", await Eval("numel(\"hello\")"));
    }

    [Fact]
    public async Task Concat_JoinsArraysAndAppendsScalars()
    {
        Assert.Equal("[1, 2, 3, 4]", await Eval("concat([1, 2], [3, 4])"));
        Assert.Equal("[1, 2, 5]", await Eval("concat([1, 2], 5)"));
        Assert.Equal("[1, 2, 3, x]", await Eval("concat([1], [2, 3], \"x\")"));
    }

    [Fact]
    public async Task Slice_ZeroBased_StopExclusive_DefaultsToEnd()
    {
        Assert.Equal("[30, 40]", await Eval("slice([10, 20, 30, 40], 2)"));
        Assert.Equal("[20, 30]", await Eval("slice([10, 20, 30, 40], 1, 3)"));
        Assert.Equal("[]", await Eval("slice([10, 20], 2)"));
    }

    [Fact]
    public async Task Slice_InvalidRange_IsRuntimeError()
    {
        ScriptRunResult result = await Run("print(slice([1, 2], 0, 3))");

        Assert.False(result.Success);
        Assert.Contains("invalid", Assert.Single(result.Diagnostics).Message);
    }

    [Fact]
    public async Task Indexof_FirstMatchOrMinusOne()
    {
        Assert.Equal("1", await Eval("indexof([\"a\", \"b\", \"b\"], \"b\")"));
        Assert.Equal("-1", await Eval("indexof([1, 2], 3)"));
    }

    [Fact]
    public async Task Reverse_ReturnsAReversedCopy()
    {
        Assert.Equal("[3, 2, 1]", await Eval("reverse([1, 2, 3])"));
    }

    [Fact]
    public async Task Isnan_ElementWise()
    {
        Assert.Equal("[false, true, false]", await Eval("isnan([1, 0/0, 3])"));
        Assert.Equal("true", await Eval("isnan(0/0)"));
    }

    [Fact]
    public async Task AndOrNot_ElementWiseWithBroadcast()
    {
        Assert.Equal("[true, false, false]", await Eval("and([true, true, false], [true, false, false])"));
        Assert.Equal("[true, true, false]", await Eval("or([true, false, false], [false, true, false])"));
        Assert.Equal("[false, true]", await Eval("not([1, 0])"));
        Assert.Equal("[true, false]", await Eval("and([1, 0], true)"));
    }

    [Fact]
    public async Task Contains_ArrayMembership()
    {
        Assert.Equal("true", await Eval("contains([1, 2, 3], 2)"));
        Assert.Equal("false", await Eval("contains([\"a\"], \"b\")"));
    }
}
