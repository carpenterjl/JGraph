using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open item 86 (ADR 0238): the start of a property name R2025b's get, set, findobj and the creating
/// functions take, by the rank-and-count rule read off probe_86, and the places that take whole
/// names only. Every expectation is a recorded R2025b answer.
/// </summary>
[Collection("JG facade")]
public class PartialPropertyNameTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public PartialPropertyNameTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private IScriptSession NewSession() => Assert
        .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
        .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure))));

    private async Task<IReadOnlyList<string>> Lines(string code)
    {
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalLines;
    }

    private async Task<string> Failure(string code)
    {
        int before = _output.Errors.Count;
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.False(result.Success);
        return string.Concat(_output.Errors.Skip(before));
    }

    [Fact]
    public async Task AStartMeansTheOneSettableListedNameItBegins()
    {
        IReadOnlyList<string> lines = await Lines("""
            f = figure('Visible', 'off'); ax = axes(f); ln = plot(ax, 1:3);
            disp(numel(get(ax, 'Pos')));        % Position, although PositionConstraint begins the same
            disp(mat2str(get(ax, 'XLi')));       % XLim over XLimMode and XLimitMethod
            disp(get(ax, 'xlimm'));              % a Mode twin alone, in any case
            disp(get(ax, 'YA'));                 % YAxisLocation over the read-only YAxis
            disp(get(ax, 'N'));                  % NextPlot over the read-only NextSeriesIndex
            disp(['[' get(ax, 'Ta') ']']);       % Tag over the hidden TargetManager
            disp(get(ln, 'LineS'));              % LineStyle over the hidden LineSmoothing
            disp(char(get(f, 'Nu')));            % NumberTitle over the read-only Number
            disp(get(0, 'Un'));
            """);
        Assert.Equal(["4", "[1 3]", "auto", "left", "replace", "[]", "-", "on", "pixels"], lines.Select(static l => l.Trim()).ToArray());
    }

    [Theory]
    [InlineData("get(ax, 'Tit')", "Unrecognized property Tit for class matlab.graphics.axis.Axes.")]
    [InlineData("get(ax, 'Col')", "Unrecognized property Col for class matlab.graphics.axis.Axes.")]
    [InlineData("get(ax, 'PositionC')", "Unrecognized property PositionC for class matlab.graphics.axis.Axes.")]
    [InlineData("get(ax, 'XLimi')", "Unrecognized property XLimi for class matlab.graphics.axis.Axes.")]
    [InlineData("get(ln, 'Mark')", "Unrecognized property Mark for class matlab.graphics.chart.primitive.Line.")]
    [InlineData("set(ax, 'Tit', 1)", "Unrecognized property Tit for class Axes.")]
    [InlineData("plot(ax, 1:3, 'Mark', 'o')", "Unrecognized property Mark for class Line.")]
    [InlineData("ax.Pos","Unrecognized method, property, or field 'Pos' for class 'matlab.graphics.axis.Axes'.")]
    public async Task AnAmbiguousOrWholeOnlyStartIsRefusedInR2025bsWords(string code, string message)
    {
        string error = await Failure($"f = figure('Visible', 'off'); ax = axes(f); ln = plot(ax, 1:3); {code}");
        Assert.Contains(message, error);
    }

    [Fact]
    public async Task TheFormsThatTakeAStartAndTheOnesThatDoNot()
    {
        IReadOnlyList<string> lines = await Lines("""
            f = figure('Visible', 'off'); ax = axes(f);
            set(ax, 'Pos', [0.2 0.2 0.5 0.5]); disp(mat2str(ax.Position, 4));
            set(ax, {'XLi'}, {[1 5]}); disp(mat2str(ax.XLim));
            set(ax, struct('YLi', [2 6])); disp(mat2str(ax.YLim));
            ln = plot(ax, 1:3, 'LineW', 2, 'Col', 'r'); disp(ln.LineWidth);
            t = text(ax, 1, 1, 'a', 'FontSi', 12); disp(t.FontSize);
            g = figure('Nam', 'x', 'Vis', 'off'); disp(g.Name);
            u = uicontrol(f, 'Str', 'x', 'Sty', 'text'); disp(u.String);
            disp(double(isprop(ax, 'Pos')));
            disp(numel(findobj(f, 'Typ', 'uicontrol')));
            disp(numel(findobj(f, '-property', 'Pos')));
            """);
        Assert.Equal(["[0.2 0.2 0.5 0.5]", "[1 5]", "[2 6]", "2", "12", "x", "x", "0", "1", "0"], lines.Select(static l => l.Trim()).ToArray());
    }

    [Fact]
    public void TheRuleRanksListedSettableNamesFirst()
    {
        FigureModel figure = JG.Figure();
        AxesModel axes = JG.Gca();
        _ = new JgsRunningDialect(JgsDialect.Matlab); // the thread's running dialect, as a MATLAB body entry sets it
        try
        {
            Assert.True(JgsGraphicsProperties.TryPartialName(axes, "Pos", out string position));
            Assert.Equal("Position", position);
            Assert.True(JgsGraphicsProperties.TryPartialName(axes, "CameraP", out string camera));
            Assert.Equal("CameraPosition", camera);
            Assert.False(JgsGraphicsProperties.TryPartialName(axes, "Tit", out _));
            Assert.False(JgsGraphicsProperties.TryPartialName(axes, "Bo", out _));
            Assert.False(JgsGraphicsProperties.TryPartialName(axes, "U", out _));
            Assert.False(JgsGraphicsProperties.TryPartialName(axes, "", out _));
            Assert.True(JgsGraphicsProperties.TryPartialName(figure, "Men", out string menu));
            Assert.Equal("MenuBar", menu);
            Assert.False(JgsGraphicsProperties.TryPartialName(figure, "Win", out _));
        }
        finally
        {
            _ = new JgsRunningDialect(JgsDialect.Jgs);
        }

        Assert.False(JgsGraphicsProperties.TryPartialName(axes, "Pos", out _)); // the JGS dialect takes whole names
    }
}
