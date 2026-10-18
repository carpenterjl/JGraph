using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open item 138 (ADR 0241): R2025b's <c>image</c> and <c>imagesc</c> set the axes' limits by hand
/// to the pixel edges, half a pixel beyond the first and last centre, with the modes manual; the
/// low-level form, a held axes and a uiaxes leave them automatic, and an image of no pixels sets
/// none (probe <c>oi138_image</c>). The automatic limits over an image, alone or with a line, are
/// the ordinary tick-aligned rule over the union of extents (<c>oi138_auto</c>: two images reading
/// <c>[0 10]</c>, not the edges). The rows run in the MATLAB dialect and assert in the script.
/// </summary>
[Collection("JG facade")]
public class JgsImageLimitsTests : IDisposable
{
    public JgsImageLimitsTests() => JG.Reset();

    public void Dispose()
    {
        JG.Reset();
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("imagesc(magic(4)); assert(isequal(xlim, [0.5 4.5])); assert(strcmp(get(gca, 'XLimMode'), 'manual')); assert(isequal(ylim, [0.5 4.5])); assert(strcmp(get(gca, 'YLimMode'), 'manual')); assert(strcmp(get(gca, 'YDir'), 'reverse'));")]
    [InlineData("image(magic(4)); assert(isequal(xlim, [0.5 4.5])); assert(strcmp(get(gca, 'XLimMode'), 'manual'));")]
    [InlineData("image([2 5], [10 40], magic(4)); assert(isequal(xlim, [1.5 5.5])); assert(isequal(ylim, [5 45]));")]
    [InlineData("image(3, 7, magic(4)); assert(isequal(xlim, [2.5 6.5])); assert(isequal(ylim, [6.5 10.5]));")]
    [InlineData("image(7); assert(isequal(xlim, [0.5 1.5])); assert(isequal(ylim, [0.5 1.5]));")]
    [InlineData("imagesc(ones(3, 5)); assert(isequal(xlim, [0.5 5.5])); assert(isequal(ylim, [0.5 3.5]));")]
    [InlineData("image([5 2], [1 4], magic(4)); assert(isequal(xlim, [1.5 5.5]));")]
    [InlineData("image([1 2 10], [1 4], magic(4)); assert(isequal(xlim, [-0.5 11.5]));")] // the ends alone count, as the pixels are placed
    [InlineData("image([2 5], [1 4], (1:4)'); assert(isequal(xlim, [-1 8]));")] // one column given two ends spans twice their distance
    [InlineData("image(rand(4, 4, 3)); assert(isequal(xlim, [0.5 4.5])); assert(strcmp(get(gca, 'XLimMode'), 'manual'));")]
    [InlineData("imagesc(magic(4), [0 20]); assert(isequal(xlim, [0.5 4.5])); assert(strcmp(get(gca, 'XLimMode'), 'manual')); assert(isequal(get(gca, 'CLim'), [0 20]));")]
    [InlineData("ax = axes; imagesc(ax, magic(4)); assert(isequal(ax.XLim, [0.5 4.5])); assert(strcmp(ax.XLimMode, 'manual'));")]
    [InlineData("imagesc(1:5, 1:3, ones(3, 5)); assert(isequal(xlim, [0.5 5.5])); assert(isequal(ylim, [0.5 3.5]));")]
    [InlineData("im = image(magic(4)); im.CData = ones(2, 7); assert(isequal(xlim, [0.5 4.5])); assert(strcmp(get(gca, 'XLimMode'), 'manual'));")] // set once, not followed
    [InlineData("imagesc(magic(4)); hold on; plot(0:8, 0:8); assert(isequal(xlim, [0.5 4.5])); assert(strcmp(get(gca, 'XLimMode'), 'manual'));")]
    public void TheHighLevelFormsSetTheLimitsToThePixelEdges(string code) => Run(code);

    [Theory]
    [InlineData("image('CData', magic(4)); assert(strcmp(get(gca, 'XLimMode'), 'auto')); assert(isequal(xlim, [0.5 4.5])); assert(strcmp(get(gca, 'YDir'), 'normal'));")]
    [InlineData("image('XData', [2 5], 'CData', magic(4)); assert(strcmp(get(gca, 'XLimMode'), 'auto')); assert(isequal(xlim, [1.5 5.5]));")]
    [InlineData("plot(1:10, (1:10) * 2); hold on; imagesc(magic(4)); assert(strcmp(get(gca, 'XLimMode'), 'auto')); assert(strcmp(get(gca, 'YDir'), 'normal')); assert(isequal(xlim, [0 10]));")]
    [InlineData("hold on; imagesc(magic(4)); assert(strcmp(get(gca, 'XLimMode'), 'auto')); assert(isequal(xlim, [0.5 4.5]));")]
    [InlineData("image([]); assert(strcmp(get(gca, 'XLimMode'), 'auto')); assert(isequal(xlim, [0 1])); assert(isequal(ylim, [0 1]));")]
    public void TheLowLevelFormAHeldAxesAndAnEmptyImageLeaveThemAutomatic(string code) => Run(code);

    [Fact]
    public void AUiaxesTakesTheEdgesByHandToo() =>
        // R2025b leaves a uiaxes' limits automatic at the same values (its replacechildren); this
        // build's automatic rule over the smaller box would land on [0 5], so the edges are set
        // (ADR 0241's recorded divergence of the mode).
        Run("f = uifigure; ax = uiaxes(f); imagesc(ax, magic(4)); assert(isequal(ax.XLim, [0.5 4.5])); assert(strcmp(ax.XLimMode, 'manual'));");

    [Theory]
    [InlineData("imagesc(magic(4)); xlim auto; assert(strcmp(get(gca, 'XLimMode'), 'auto')); assert(isequal(xlim, [0.5 4.5])); assert(strcmp(get(gca, 'YLimMode'), 'manual'));")]
    [InlineData("imagesc(magic(4)); axis tight; assert(strcmp(get(gca, 'XLimMode'), 'auto')); assert(isequal(xlim, [0.5 4.5]));")]
    [InlineData("imagesc(magic(4)); axis image; assert(strcmp(get(gca, 'XLimMode'), 'auto')); assert(isequal(xlim, [0.5 4.5]));")]
    [InlineData("imagesc(magic(4)); hold on; plot([1 3], [1 2]); axis auto; assert(isequal(xlim, [0.5 4.5]));")]
    [InlineData("imagesc(magic(4)); hold on; plot([1 4.7], [1 2]); axis auto; assert(isequal(xlim, [0.5 5]));")]
    [InlineData("imagesc(magic(4)); hold on; plot([1 7], [1 2]); axis auto; assert(isequal(xlim, [0 7]));")]
    [InlineData("imagesc(magic(4)); hold on; plot([1 12], [1 2]); axis auto; assert(isequal(xlim, [0 12]));")]
    [InlineData("imagesc(magic(4)); hold on; plot([-2 2], [1 2]); axis auto; assert(isequal(xlim, [-2 5]));")]
    [InlineData("imagesc(magic(4)); hold on; plot([1 2], [0 6]); axis auto; assert(isequal(xlim, [0.5 4.5])); assert(isequal(ylim, [0 6]));")]
    [InlineData("imagesc(magic(4)); hold on; image([6 9], [1 4], magic(4)); axis auto; assert(isequal(xlim, [0 10]));")] // two images: the tick-aligned union, not the edges
    [InlineData("image([1 2 10], [1 4], magic(4)); xlim auto; assert(isequal(xlim, [-2 12]));")]
    public void TheAutomaticLimitsOverAnImageAreTheOrdinaryTickAlignedRule(string code) => Run(code);

    private static void Run(string code)
    {
        ScriptRunResult result = JgsRunner.Run(
            "set(groot, 'DefaultFigureVisible', 'off'); " + code,
            new ScriptContext(new RecordingScriptOutput(), (_, _) => { }), default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message);
    }
}
