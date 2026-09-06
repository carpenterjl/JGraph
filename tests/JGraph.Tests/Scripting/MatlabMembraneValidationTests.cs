using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

[Collection("JG facade")]
public sealed class MatlabMembraneValidationTests : IDisposable
{
    public MatlabMembraneValidationTests() => JG.Reset();
    public void Dispose() => JG.Reset();

    [Theory]
    [InlineData("validateattributes(ones(2,3,4,5),{'numeric'},{'size',[2 4;3 5]});")]
    [InlineData("validateattributes([1 2 3],{'numeric'},{'size',[1 3]});")]
    [InlineData("validateattributes(ones(2,3,4),{'numeric'},{'size',[2 NaN 4]});")]
    [InlineData("validateattributes(ones(1,3),{'numeric'},{'size',[1;3;1]});")]
    [InlineData("validateattributes(zeros(0,3),{'numeric'},{'size',[0 3]});")]
    [InlineData("validateattributes({'a','b'},{'cell'},{'size',[1 2]});")]
    [InlineData("validateattributes(char('ab','cd'),{'char'},{'size',[2 2]});")]
    [InlineData("validateattributes([1+2i 3],{'numeric'},{'size',[1 2]});")]
    [InlineData("failed=false; try; validateattributes(ones(2,3,4),{'numeric'},{'size',[2 3]}); catch; failed=true; end; assert(failed);")]
    [InlineData("failed=false; try; validateattributes(ones(2,3),{'numeric'},{'size',[3 2]}); catch; failed=true; end; assert(failed);")]
    [InlineData("failed=false; try; validateattributes(ones(2,3),{'numeric'},{'size'}); catch; failed=true; end; assert(failed);")]
    [InlineData("failed=false; try; validateattributes(ones(2,3),{'numeric'},{'size',[2 -1]}); catch; failed=true; end; assert(failed);")]
    [InlineData("failed=false; try; validateattributes(ones(2,3),{'numeric'},{'size',[2 Inf]}); catch; failed=true; end; assert(failed);")]
    [InlineData("failed=false; try; membrane(0); catch; failed=true; end; assert(failed);")]
    [InlineData("failed=false; try; membrane(13); catch; failed=true; end; assert(failed);")]
    [InlineData("failed=false; try; membrane(1,0); catch; failed=true; end; assert(failed);")]
    [InlineData("failed=false; try; membrane(1,3,4,5); catch; failed=true; end; assert(failed);")]
    [InlineData("failed=false; try; membrane(1.5); catch; failed=true; end; assert(failed);")]
    [InlineData("A=membrane(1,4,4,4); assert(isequal(size(A),[9 9])); assert(max(abs([A(2,7) A(4,6) A(7,7)]-[0.520561475683672 1 0.437452915483019]))<1e-10); assert(abs(max(abs(A(:)))-1)<1e-12);")]
    [InlineData("A=membrane(2,4,4,4); assert(isequal(size(A),[9 9])); assert(max(abs([A(2,7) A(4,6) A(7,7)]-[-0.107853781820161 0 0.924216220489399]))<1e-10); assert(abs(max(abs(A(:)))-1)<1e-12);")]
    [InlineData("A=membrane(3,4,4,4); assert(isequal(size(A),[9 9])); assert(max(abs([A(2,7) A(4,6) A(7,7)]-[-0.707958173529638 -0.499629453546186 1]))<1e-10); assert(abs(max(abs(A(:)))-1)<1e-12);")]
    [InlineData("A=membrane(4,4,4,4); assert(isequal(size(A),[9 9])); assert(max(abs([A(2,7) A(4,6) A(7,7)]-[-0.379487435185441 0 -0.815193658497232]))<1e-10); assert(abs(max(abs(A(:)))-1)<1e-12);")]
    [InlineData("A=membrane(5,4,4,4); assert(isequal(size(A),[9 9])); assert(max(abs([A(2,7) A(4,6) A(7,7)]-[-1 0.739888898823298 -0.453904213235238]))<1e-10); assert(abs(max(abs(A(:)))-1)<1e-12);")]
    [InlineData("A=membrane(6,4,4,4); assert(isequal(size(A),[9 9])); assert(max(abs([A(2,7) A(4,6) A(7,7)]-[0.0998432179434939 0.791650277556848 0.17415835886338]))<1e-10); assert(abs(max(abs(A(:)))-1)<1e-12);")]
    [InlineData("A=membrane(7,4,4,4); assert(isequal(size(A),[9 9])); assert(max(abs([A(2,7) A(4,6) A(7,7)]-[0.286952014430878 0 -0.123800309121997]))<1e-10); assert(abs(max(abs(A(:)))-1)<1e-12);")]
    [InlineData("A=membrane(8,4,4,4); assert(isequal(size(A),[9 9])); assert(max(abs([A(2,7) A(4,6) A(7,7)]-[0.70483563672753 -0.00165654891756974 -7.52162836021531e-05]))<1e-10); assert(abs(max(abs(A(:)))-1)<1e-12);")]
    [InlineData("A=membrane(9,4,4,4); assert(isequal(size(A),[9 9])); assert(max(abs([A(2,7) A(4,6) A(7,7)]-[0.70483563672753 -0.00165654891756974 -7.52162836021531e-05]))<1e-10); assert(abs(max(abs(A(:)))-1)<1e-12);")]
    [InlineData("A=membrane(10,4,4,4); assert(isequal(size(A),[9 9])); assert(max(abs([A(2,7) A(4,6) A(7,7)]-[0.56675823017805 0.273677814179364 0.444569547460416]))<1e-10); assert(abs(max(abs(A(:)))-1)<1e-12);")]
    [InlineData("A=membrane(11,4,4,4); assert(isequal(size(A),[9 9])); assert(max(abs([A(2,7) A(4,6) A(7,7)]-[0.839176022752546 0 -0.458032153859251]))<1e-10); assert(abs(max(abs(A(:)))-1)<1e-12);")]
    [InlineData("A=membrane(12,4,4,4); assert(isequal(size(A),[9 9])); assert(max(abs([A(2,7) A(4,6) A(7,7)]-[0.03091388354078 -0.169575065644953 -0.5]))<1e-10); assert(abs(max(abs(A(:)))-1)<1e-12);")]
    [InlineData("A=membrane; assert(isequal(size(A),[31 31])); assert(isequal(A,membrane(1)));")]
    [InlineData("tiledlayout(2,1); a=nexttile; contourf(peaks); b=nexttile; contourf(membrane); cb=colorbar; cb.Layout.Tile='east'; assert(strcmp(cb.Layout.Tile,'east')); assert(a.Layout.Tile==1); assert(b.Layout.Tile==2); close all;")]
    public void MatchesMatlab(string code)
    {
        var result = JgsRunner.Run(code, new ScriptContext(new RecordingScriptOutput(), (_, _) => { }), default,
            sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message);
    }

    [Fact]
    public void DiscardedMembranePlotsSurface()
    {
        MatchesMatlab("membrane(2,8);");
        Assert.Single(JG.Gca().Plots);
    }

    [Theory]
    [InlineData("east")]
    [InlineData("west")]
    [InlineData("north")]
    [InlineData("south")]
    public void SharedColorbarOccupiesOuterBand(string side)
    {
        MatchesMatlab($"tiledlayout(2,1); nexttile; contourf(peaks); nexttile; contourf(membrane); cb=colorbar; cb.Layout.Tile='{side}';");
        var figure = JG.Gcf();
        var context = new JGraph.Tests.TestDoubles.RecordingRenderContext(new JGraph.Core.Primitives.Size2D(800, 600));
        var rendered = new JGraph.Rendering.FigureRenderer().Render(figure, context);
        var bar = figure.Axes[1].Colorbar;
        Assert.NotNull(bar.LastBox);
        var box = bar.LastBox.Value;
        if (side is "east" or "west")
        {
            Assert.True(box.Height > rendered.Axes[0].PlotArea.Height);
            Assert.All(rendered.Axes, a => Assert.True(side == "east" ? box.Left > a.PlotArea.Right : box.Right < a.PlotArea.Left));
        }
        else
        {
            Assert.True(bar.IsHorizontal);
            Assert.All(rendered.Axes, a => Assert.True(side == "north" ? box.Bottom < a.PlotArea.Top : box.Top > a.PlotArea.Bottom));
        }
    }
}
