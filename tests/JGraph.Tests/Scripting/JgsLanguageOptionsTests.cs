using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M29.T2: the two JGS language options a user may change. The engine reads them from a provider on
/// each run, so a change takes effect on the next run without a restart; MATLAB is untouched by them.
/// </summary>
[Collection("JG facade")]
public class JgsLanguageOptionsTests : IDisposable
{
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public JgsLanguageOptionsTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private ScriptContext Context => new(_output, (_, figure) => _figures.Add(figure), null);

    [Fact]
    public async Task DefaultEngine_RequiresLetAndCountsFromZero()
    {
        var engine = new JgsScriptEngine();

        ScriptRunResult needsLet = await engine.RunAsync("x = 7", Context, default);
        Assert.False(needsLet.Success);
        Assert.Contains("Declare it first with 'let'", needsLet.Message!, StringComparison.Ordinal);

        _output.Normal.Clear();
        ScriptRunResult zeroBased = await engine.RunAsync("let x = [10, 20]\ndisp(x(0))", Context, default);
        Assert.True(zeroBased.Success, zeroBased.Message);
        Assert.Contains("10", _output.NormalText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OptionalLet_LetsAFirstAssignmentOmitLet()
    {
        var engine = new JgsScriptEngine(() => new JgsLanguageOptions(RequireLet: false));

        ScriptRunResult result = await engine.RunAsync("x = 7\ndisp(x)", Context, default);

        Assert.True(result.Success, result.Message);
        Assert.Contains("7", _output.NormalText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OneBasedOption_CountsFromOne_AndSaysSoOnAnError()
    {
        var engine = new JgsScriptEngine(() => new JgsLanguageOptions(IndexBase: 1));

        ScriptRunResult first = await engine.RunAsync("let x = [10, 20, 30]\ndisp(x(1))", Context, default);
        Assert.True(first.Success, first.Message);
        Assert.Contains("10", _output.NormalText, StringComparison.Ordinal);

        ScriptRunResult outOfRange = await engine.RunAsync("let x = [10, 20]\ndisp(x(0))", Context, default);
        Assert.False(outOfRange.Success);
        Assert.Contains("1-based", outOfRange.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheProviderIsReadOnEachRun_SoAChangeAppliesNextTime()
    {
        var options = new JgsLanguageOptions(RequireLet: true);
        var engine = new JgsScriptEngine(() => options);

        ScriptRunResult before = await engine.RunAsync("x = 1", Context, default);
        Assert.False(before.Success);

        options = new JgsLanguageOptions(RequireLet: false);
        ScriptRunResult after = await engine.RunAsync("x = 1\ndisp(x)", Context, default);
        Assert.True(after.Success, after.Message);
    }

    [Fact]
    public async Task AHandEditedIndexBase_IsClampedRatherThanBreaking()
    {
        // Sanitized() keeps the interpreter out of a state no rule covers.
        var engine = new JgsScriptEngine(() => new JgsLanguageOptions(IndexBase: 5));

        ScriptRunResult result = await engine.RunAsync("let x = [10, 20]\ndisp(x(0))", Context, default);

        Assert.True(result.Success, result.Message); // fell back to 0-based
        Assert.Contains("10", _output.NormalText, StringComparison.Ordinal);
    }
}
