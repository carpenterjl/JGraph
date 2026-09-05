using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Writing into a table by variable: <c>T.Var = column</c> replaces or adds a variable, and
/// <c>T.Var(i) = v</c> writes one element. Before these, <c>T.Var</c> read out a copy and a write
/// into it changed nothing — silently.
/// </summary>
[Collection("JG facade")]
public class MatlabTableWriteTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();

    public MatlabTableWriteTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private async Task<ScriptRunResult> Run(string code)
    {
        await using IScriptSession session = new MatlabScriptEngine().CreateSession(new ScriptContext(_output, (_, _) => { }));
        return await session.ExecuteAsync(code, "", CancellationToken.None);
    }

    private const string Make = "t = table([1;2],[3;4],{'x';'y'},'VariableNames',{'A','B','C'});";

    [Fact]
    public async Task ElementWrite_ChangesTheTable()
    {
        ScriptRunResult result = await Run(Make + "t.B(2) = 40; disp(t.B(2)); disp(t.B(1))");
        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "40", "3" }, _output.NormalLines.Select(l => l.Trim()));
    }

    [Fact]
    public async Task ElementWrite_IntoATextColumn_TakesAStringOrACell()
    {
        ScriptRunResult result = await Run(Make + "t.C(2) = 'q'; t.C(1) = {'p'}; disp(t.C{1}); disp(t.C{2})");
        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "p", "q" }, _output.NormalLines.Select(l => l.Trim()));
    }

    [Fact]
    public async Task CompoundElementWrite_ReadsBeforeItWrites()
    {
        ScriptRunResult result = await Run(Make + "t.A(1) += 10; disp(t.A(1))");
        Assert.True(result.Success, result.Message);
        Assert.Equal("11", _output.NormalLines[^1].Trim());
    }

    [Fact]
    public async Task ColumnWrite_ReplacesTheVariable()
    {
        ScriptRunResult result = await Run(Make + "t.B = [10;20]; disp(t.B(1) + t.B(2)); disp(size(t, 2))");
        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "30", "3" }, _output.NormalLines.Select(l => l.Trim()));
    }

    [Fact]
    public async Task ColumnWrite_ToANewName_AddsTheVariable()
    {
        ScriptRunResult result = await Run(Make + "t.D = [7;8]; disp(t.D(2)); disp(size(t, 2)); disp(t{2, 'D'})");
        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "8", "4", "8" }, _output.NormalLines.Select(l => l.Trim()));
    }

    [Fact]
    public async Task ColumnWrite_WithTheWrongRowCount_IsRefused()
    {
        ScriptRunResult result = await Run(Make + "t.B = [1;2;3];");
        Assert.False(result.Success);
        Assert.Contains("number of rows must match", result.Message);
    }

    [Fact]
    public async Task ElementWrite_PastTheEnd_IsRefused_AndLeavesTheTableAlone()
    {
        ScriptRunResult result = await Run(Make + "try, t.B(3) = 5; catch e, disp(e.message); end; disp(size(t, 1))");
        Assert.True(result.Success, result.Message);
        Assert.Contains("number of rows must match", _output.NormalLines[0]);
        Assert.Equal("2", _output.NormalLines[^1].Trim());
    }

    [Fact]
    public async Task TheTableIsAValue_SoACopyMadeBeforeTheWriteKeepsTheOldColumn()
    {
        ScriptRunResult result = await Run(Make + "u = t; t.B(1) = 99; disp(u.B(1)); disp(t.B(1))");
        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "3", "99" }, _output.NormalLines.Select(l => l.Trim()));
    }
}
