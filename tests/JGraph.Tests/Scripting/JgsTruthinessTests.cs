using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>M18: MATLAB array truthiness — an array is true iff non-empty and all elements truthy.</summary>
[Collection("JG facade")]
public class JgsTruthinessTests : IDisposable
{
    private readonly JgsScriptEngine _engine = new();
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public JgsTruthinessTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private Task<ScriptRunResult> Run(string code) =>
        _engine.RunAsync(code, new ScriptContext(_output, (_, figure) => _figures.Add(figure), null), default);

    [Fact]
    public async Task IfCondition_ArrayIsTrue_OnlyWhenAllElementsTruthy()
    {
        ScriptRunResult result = await Run("""
            if [1, 2] == [1, 2] { print("all equal") }
            if [1, 2] == [1, 3] { print("should not print") }
            if [] { print("empty should not print") }
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("all equal", _output.NormalText);
        Assert.DoesNotContain("should not print", _output.NormalText);
    }

    [Fact]
    public async Task LogicalOperators_ConsumeArrayTruthiness_AsScalars()
    {
        ScriptRunResult result = await Run("""
            print([1, 1] && true)
            print([1, 0] || false)
            print(![1, 0])
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal("true\nfalse\ntrue", _output.NormalText.Trim().Replace("\r", ""));
    }

    [Fact]
    public async Task WhileCondition_MaskDrivenLoop_StopsWhenAnyElementFalsy()
    {
        ScriptRunResult result = await Run("""
            let a = [3, 2, 1]
            let steps = 0
            while a > 0 {
                a = a - 1
                steps = steps + 1
            }
            print(steps, a)
            """);

        // a > 0 is all-true until any element reaches 0: one step for [3,2,1] -> [2,1,0].
        Assert.True(result.Success, result.Message);
        Assert.Contains("1 [2, 1, 0]", _output.NormalText);
    }

    [Fact]
    public async Task EmptinessTest_RemainsLengthGreaterThanZero()
    {
        ScriptRunResult result = await Run("""
            let hits = find([false, false])
            if length(hits) > 0 { print("found") } else { print("none") }
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("none", _output.NormalText);
    }
}
