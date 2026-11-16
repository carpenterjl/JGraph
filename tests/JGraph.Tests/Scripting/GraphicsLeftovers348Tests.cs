using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open item 348 (ADR 0382): the parts of the graphics leftovers the parity fixture
/// <c>oi348_graphics_leftovers</c> cannot reach - a JGS-dialect tiled layout's handle kept through a
/// sweep, and the JGS dialect's own answers kept where the MATLAB dialect took R2025b's (a layout's
/// title is its words, <c>Children</c> a row, a histogram's face its colour, a replacing plot keeps the
/// axes' tag).
/// </summary>
[Collection("JG facade")]
public sealed class GraphicsLeftovers348Tests : IDisposable
{
    public GraphicsLeftovers348Tests() => JG.Reset();

    public void Dispose()
    {
        JG.Reset();
        GC.SuppressFinalize(this);
    }

    [Theory]
    // A sweep (delete runs one) reaped a JGS-dialect layout's handle: ishandle(t) went false and
    // nexttile(t) read the dead number as a tile location.
    [InlineData("let t = tiledlayout(2, 2); let a = nexttile(t); let h = plot(a, 1:3); delete(h); assert(ishandle(t)); let b = nexttile(t); assert(ishandle(b)); set(t, 'TileSpacing', 'compact'); assert(strcmp(get(t, 'TileSpacing'), 'compact'));")]
    [InlineData("let t = tiledlayout(1, 2); let a = nexttile(t); cla(a); assert(ishandle(t)); title(t, 'G'); assert(strcmp(get(t, 'Title'), 'G'));")]
    public void AJgsLayoutOutlivesASweep(string code) => Run(code, JgsDialect.Jgs);

    [Theory]
    [InlineData("let ax = axes(); plot(ax, 1:3); hold(ax, 'on'); plot(ax, 3:-1:1); assert(isequal(size(get(ax, 'Children')), [1, 2]));")]
    [InlineData("let ax = axes(); let h = histogram(ax, [1, 2, 2, 3]); assert(isnumeric(get(h, 'FaceColor')));")]
    [InlineData("let ax = axes(); set(ax, 'Tag', 'keep'); plot(ax, 1:3); assert(strcmp(get(ax, 'Tag'), 'keep'));")]
    [InlineData("let t = tiledlayout(1, 1); let r = title(t, 'G'); assert(isequal(r, t)); assert(ischar(get(t, 'Title')));")]
    public void TheJgsDialectKeepsItsAnswers(string code) => Run(code, JgsDialect.Jgs);

    [Theory]
    [InlineData("ax = axes; plot(ax, 1:3); hold(ax, 'on'); plot(ax, 3:-1:1); assert(isequal(size(ax.Children), [2 1])); assert(isequal(size(axes(figure).Children), [0 0]));")]
    [InlineData("ax = axes; h = histogram(ax, [1 2 2 3]); assert(strcmp(h.FaceColor, 'auto')); h.DisplayStyle = 'stairs'; assert(strcmp(h.EdgeColor, 'auto')); assert(strcmp(h.FaceColor, 'none'));")]
    [InlineData("ax = axes; ax.Tag = 'gone'; ax.UserData = 3; plot(ax, 1:3); assert(isempty(ax.Tag)); assert(isempty(ax.UserData));")]
    [InlineData("t = tiledlayout(1, 1); r = title(t, 'G', 'FontSize', 9); assert(isa(r, 'matlab.graphics.layout.Text')); assert(isequal(r, t.Title)); assert(r.FontSize == 9); assert(strcmp(t.Title.String, 'G'));")]
    [InlineData("f = figure; p = uipanel(f); t = tiledlayout(p, 1, 2); a = nexttile(t); assert(isequal(t.Parent, p)); assert(isequal(a.Parent, t)); assert(isequal(p.Children, t)); delete(a); assert(isvalid(t));")]
    public void TheMatlabDialectTakesR2025bsAnswers(string code) => Run("set(groot, 'DefaultFigureVisible', 'off'); " + code, JgsDialect.Matlab);

    private static void Run(string code, JgsDialect dialect)
    {
        ScriptRunResult result = JgsRunner.Run(
            code, new ScriptContext(new RecordingScriptOutput(), (_, _) => { }), default, sourceId: "", hook: null, dialect);
        Assert.True(result.Success, result.Message);
    }
}
