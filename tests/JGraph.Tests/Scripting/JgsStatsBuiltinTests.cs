using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>M18: the statistics builtins (std, variance, median, mode, percentile, cumsum, cumprod, diff).</summary>
[Collection("JG facade")]
public class JgsStatsBuiltinTests : IDisposable
{
    private readonly JgsScriptEngine _engine = new();
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public JgsStatsBuiltinTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private Task<ScriptRunResult> Run(string code) =>
        _engine.RunAsync(code, new ScriptContext(_output, (_, figure) => _figures.Add(figure), null), default);

    private async Task<string> Eval(string expression)
    {
        ScriptRunResult result = await Run($"print({expression})");
        Assert.True(result.Success, result.Message);
        return _output.NormalText.Trim();
    }

    [Fact]
    public async Task StdAndVariance_UseSampleDenominator()
    {
        // [1,3,5]: mean 3, squared deviations 4+0+4 = 8, / (n-1) = 4 -> std 2.
        Assert.Equal("4", await Eval("variance([1, 3, 5])"));
    }

    [Fact]
    public async Task Std_IsSquareRootOfVariance() =>
        Assert.Equal("2", await Eval("std([1, 3, 5])"));

    [Fact]
    public async Task Std_WithFewerThanTwoValues_IsRuntimeError()
    {
        ScriptRunResult result = await Run("print(std([1]))");

        Assert.False(result.Success);
        Assert.Contains("at least 2", Assert.Single(result.Diagnostics).Message);
    }

    [Fact]
    public async Task Median_OddAndEvenCounts()
    {
        Assert.Equal("2", await Eval("median([3, 1, 2])"));
        _output.Normal.Clear();
        Assert.Equal("2.5", await Eval("median([4, 1, 3, 2])"));
    }

    [Fact]
    public async Task Mode_SmallestValueWinsTies() =>
        Assert.Equal("1", await Eval("mode([3, 1, 3, 1, 2])"));

    [Fact]
    public async Task Percentile_InterpolatesLinearly()
    {
        Assert.Equal("5", await Eval("percentile([0, 10], 50)"));
        _output.Normal.Clear();
        Assert.Equal("1.75", await Eval("percentile([1, 2, 3, 4], 25)"));
        _output.Normal.Clear();
        Assert.Equal("4", await Eval("percentile([1, 2, 3, 4], 100)"));
    }

    [Fact]
    public async Task Percentile_OutOfRangeP_IsRuntimeError()
    {
        ScriptRunResult result = await Run("print(percentile([1, 2], 101))");

        Assert.False(result.Success);
        Assert.Contains("between 0 and 100", Assert.Single(result.Diagnostics).Message);
    }

    [Fact]
    public async Task CumsumCumprodDiff_ProduceRunningArrays()
    {
        Assert.Equal("[1, 3, 6]", await Eval("cumsum([1, 2, 3])"));
        _output.Normal.Clear();
        Assert.Equal("[2, 6, 24]", await Eval("cumprod([2, 3, 4])"));
        _output.Normal.Clear();
        Assert.Equal("[3, 5]", await Eval("diff([1, 4, 9])"));
        _output.Normal.Clear();
        Assert.Equal("[]", await Eval("diff([7])"));
    }

    [Fact]
    public async Task NaN_PropagatesThroughStatistics()
    {
        // No silent skipping: clean first with isnan + a mask.
        Assert.Equal("NaN", await Eval("mean([1, 0/0, 3])"));
        _output.Normal.Clear();
        Assert.Equal("2", await Eval("mean(([1, 0/0, 3])(not(isnan([1, 0/0, 3]))))"));
    }
}
