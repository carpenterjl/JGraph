using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open-items batch 6 (ADR 0217): what only the host sees of items 19, 50, 74 and 80. The MATLAB side
/// of item 80 is the <c>oi_app_odds</c> fixture.
/// </summary>
[Collection("JG facade")]
public sealed class OpenItemsBatch6Tests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jgraph-oi6-" + Guid.NewGuid().ToString("N"));

    public OpenItemsBatch6Tests()
    {
        JG.Reset();
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        JG.Reset();
        Directory.Delete(_folder, recursive: true);
    }

    private Task<ScriptRunResult> RunMatlab(string code) =>
        new MatlabScriptEngine().RunAsync(
            code, new ScriptContext(_output, static (_, _) => { }, _folder, resolvePath: null, new TestFigureFiles()), default);

    private static object? Raw(ScriptRunResult result, string name) =>
        Assert.Single(result.Variables, v => v.Name == name).RawValue;

    [Fact]
    public async Task AnOversizeValueSaysSoByItsValueNotItsClassName()
    {
        // Item 19: the data viewer said "no tabular view" for an oversize double array, because the
        // Type column says double and the old check looked for "array".
        ScriptRunResult result = await RunMatlab("big = zeros(1, 3e6); wide = zeros(500, 500); c = cell(1, 300000); small = zeros(3, 3); s = 5;");
        Assert.True(result.Success, result.Message);
        Assert.Equal(3_000_000, Assert.IsType<ScriptOversizeValue>(Raw(result, "big")).Elements);
        Assert.Equal(250_000, Assert.IsType<ScriptOversizeValue>(Raw(result, "wide")).Elements);
        Assert.Equal(300_000, Assert.IsType<ScriptOversizeValue>(Raw(result, "c")).Elements);
        Assert.IsType<ScriptValueGrid>(Raw(result, "small"));
        Assert.Equal(5.0, Raw(result, "s"));
    }

    [Fact]
    public async Task UiconfirmAskedForNothingReturnsAtOnce()
    {
        // Item 50: with no output R2025b returns at once and leaves the answer to a CloseFcn. The
        // stand-in for the person sees the question and never answers it.
        int shown = 0;
        ScriptGraphicsCallbacks.OverlayShown = _ => shown++;
        try
        {
            ScriptRunResult result = await RunMatlab("f = uifigure; uiconfirm(f, 'Save?', 'Closing', 'CloseFcn', @(s, e) disp(e.SelectedOption)); after = 1;");
            Assert.True(result.Success, result.Message);
            Assert.Equal(1.0, Raw(result, "after"));
            Assert.Equal(1, shown);
        }
        finally
        {
            ScriptGraphicsCallbacks.OverlayShown = null;
        }

        // With nobody to answer, both forms are refused, as R2025b's -batch refuses them.
        ScriptRunResult refused = await RunMatlab("f = uifigure; uiconfirm(f, 'Save?', 'Closing');");
        Assert.False(refused.Success);
    }

    [Fact]
    public async Task UifigureAndUiaxesRunACreateFcnOnceTheOtherOptionsAreSet()
    {
        // Item 80, measured: the callback sees the options written after it in the call, and gcbo
        // is the new object; dialog keeps its CreateFcn without running it.
        ScriptRunResult result = await RunMatlab(
            "f = uifigure('CreateFcn', @(s, e) fprintf('fig %s %d\\n', s.Name, isequal(gcbo, s)), 'Name', 'nm');\n"
            + "ax = uiaxes(f, 'CreateFcn', @(s, e) fprintf('axes %s\\n', s.Tag), 'Tag', 'tg');\n"
            + "d = dialog('CreateFcn', @(s, e) fprintf('dialog ran\\n'), 'Visible', 'off'); delete(d);");
        Assert.True(result.Success, result.Message);
        Assert.Contains("fig nm 1", _output.NormalText, StringComparison.Ordinal);
        Assert.Contains("axes tg", _output.NormalText, StringComparison.Ordinal);
        Assert.DoesNotContain("dialog ran", _output.NormalText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TagsSurviveSavefigAndOpenfigOfThisBuildsDocument()
    {
        // Item 74: a line's, an axes' and a figure's Tag were dropped by the document.
        ScriptRunResult result = await RunMatlab(
            "f = figure('Tag', 'fg'); p = plot(1:3); set(p, 'Tag', 'mine'); set(gca, 'Tag', 'ax');\n"
            + "savefig(f, 'tags.fig'); close(f);\n"
            + "h = openfig('tags.fig');\n"
            + "line_tag = get(findobj(h, 'Type', 'line'), 'Tag'); axes_tag = get(findobj(h, 'Type', 'axes'), 'Tag'); fig_tag = get(h, 'Tag');\n"
            + "found = numel(findobj(h, 'Tag', 'mine'));");
        Assert.True(result.Success, result.Message);
        Assert.Equal("mine", Raw(result, "line_tag"));
        Assert.Equal("ax", Raw(result, "axes_tag"));
        Assert.Equal("fg", Raw(result, "fig_tag"));
        Assert.Equal(1.0, Raw(result, "found"));
    }
}
