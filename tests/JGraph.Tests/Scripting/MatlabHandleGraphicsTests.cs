using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Handles on figure objects (M51). Before this, every drawing verb was a command that answered
/// nothing, so <c>ax = subplot(2,1,1)</c> bound null and <c>p.Color</c> had nowhere to look. A handle
/// is a number keyed into a runtime registry, which is what lets a script keep handles in an array and
/// compare them the way it compares anything else.
/// </summary>
[Collection("JG facade")]
public class MatlabHandleGraphicsTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabHandleGraphicsTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private IScriptSession NewSession() => Assert
        .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
        .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure))));

    private async Task RunAsserting(string code)
    {
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    [Fact]
    public Task SubplotAndPlotHandBackHandles() => RunAsserting("""
        ax1 = subplot(2,1,1);
        ax2 = subplot(2,1,2);
        assert(isnumeric(ax1));
        assert(ax1 ~= ax2);
        p = plot(ax1, 1:5, 1:5);
        assert(isnumeric(p));
        assert(p ~= ax1);
        """);

    [Fact]
    public Task AVerbAimedAtAnAxesDrawsThereWithoutMovingTheCurrentOne() => RunAsserting("""
        ax1 = subplot(2,1,1);
        ax2 = subplot(2,1,2);
        plot(ax1, 1:5, 1:5);
        title(ax1, 'Top');
        ylabel(ax1, 'Volts');
        xlabel(ax2, 'Time');
        hold(ax1, 'off');
        assert(strcmp(ax1.Title, 'Top'));
        assert(strcmp(ax1.YLabel, 'Volts'));
        assert(strcmp(ax2.XLabel, 'Time'));
        assert(isempty(ax2.Title));
        """);

    [Fact]
    public Task ALineHandleReadsAndWritesItsProperties() => RunAsserting("""
        p = plot(1:5, 1:5, 'LineWidth', 3, 'DisplayName', 'first');
        assert(p.LineWidth == 3);
        assert(strcmp(p.DisplayName, 'first'));
        assert(strcmp(p.Visible, 'on'));
        p.Visible = 'off';
        assert(strcmp(p.Visible, 'off'));
        p.LineWidth = 1.5;
        assert(p.LineWidth == 1.5);
        assert(isequal(size(p.XData), [1 5]));
        assert(isequal(p.YData, 1:5));
        """);

    [Fact]
    public Task ALinesColourIsDefiniteSoASecondLineCanMatchIt() => RunAsserting("""
        hold on
        p1 = plot(1:5, 1:5);
        c = p1.Color;
        assert(isequal(size(c), [1 3]));
        assert(abs(c(1) - 0) < 1e-9);
        assert(abs(c(2) - 0.4470) < 1e-3);
        p2 = plot(1:5, 2:6, 'Color', p1.Color);
        assert(isequal(p2.Color, c));
        assert(~isequal(plot(1:5, 3:7).Color, c));
        """);

    [Fact]
    public Task HandlesLiveInArraysAndStructsLikeAnyOtherNumber() => RunAsserting("""
        h = graphics.primitive.Line.empty(3, 0);
        assert(numel(h) == 0);
        hold on
        for i = 1:3
            p = plot(1:5, (1:5) * i);
            h(i) = p;
            rows(i).line = p;
        end
        assert(numel(h) == 3);
        gathered = [rows.line];
        assert(isequal(gathered, h));
        mask = (gathered == h(2));
        assert(sum(mask) == 1);
        assert(find(mask) == 2);
        picked = rows(mask).line;
        picked.Visible = 'off';
        assert(strcmp(h(2).Visible, 'off'));
        assert(strcmp(h(1).Visible, 'on'));
        """);

    [Fact]
    public Task LegendTakesTheHandlesItShouldShowAndHandsBackItsOwn() => RunAsserting("""
        ax = subplot(1,1,1);
        hold on
        a = plot(ax, 1:5, 1:5, 'DisplayName', 'A');
        b = plot(ax, 1:5, 2:6, 'DisplayName', 'B', 'HandleVisibility', 'off');
        lgd = legend(ax, a, 'Location', 'best');
        assert(isnumeric(lgd));
        assert(strcmp(lgd.Visible, 'on'));
        assert(strcmp(lgd.Location, 'northeast'));
        lgd.Location = 'southwest';
        assert(strcmp(lgd.Location, 'southwest'));
        lgd.ItemHitFcn = @(src, event) disp('hit');
        assert(isa(lgd.ItemHitFcn, 'function_handle'));
        """);

    [Fact]
    public Task LinkaxesTakesAVectorOfAxesHandles() => RunAsserting("""
        ax1 = subplot(2,1,1);
        ax2 = subplot(2,1,2);
        linkaxes([ax1, ax2], 'x');
        ok = 0;
        try
            linkaxes([ax1, ax2], 'z');
        catch err
            ok = ok + ~isempty(strfind(err.message, "not 'x', 'y', or 'xy'"));
        end
        try
            linkaxes(plot(1:3, 1:3));
        catch err
            ok = ok + ~isempty(strfind(err.message, 'handles to axes'));
        end
        assert(ok == 2);
        """);

    [Fact]
    public Task AMisspeltPlotOptionSaysSoRatherThanBeingIgnored() => RunAsserting("""
        ok = 0;
        try
            plot(1:5, 1:5, 'LineWidth', 2, 'Colour', 'r');
        catch err
            ok = ok + ~isempty(strfind(err.message, "unknown option 'Colour'"));
        end
        try
            p = plot(1:5, 1:5);
            p.Widthness = 3;
        catch err
            ok = ok + ~isempty(strfind(err.message, "no property 'Widthness'"));
        end
        assert(ok == 2);
        """);

    [Fact]
    public Task SingleQuotedPiecesJoinIntoOneWordAndDoubleQuotedOnesDoNot() => RunAsserting("""
        id = 'A1';
        label = ['SN:' id];
        assert(ischar(label));
        assert(strcmp(label, 'SN:A1'));
        pair = ["a", "b"];
        assert(numel(pair) == 2);
        assert(strcmp(pair(1), 'a'));
        """);
}
