using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Rendering;
using JGraph.Maths.Ticks;
using JGraph.Tests.TestDoubles;
using Xunit;
namespace JGraph.Tests.Rendering;
public sealed class MatlabSurfaceDurationRenderingTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(.5, 128)]
    [InlineData(1, 255)]
    public void RgbSurfacePreservesColorAndAlpha(double alpha, int expectedAlpha)
    {
        var figure = new FigureModel();
        var axes = figure.AddAxes();
        var surface = axes.AddSurface([0,1], [0,1], new double[,] {{0,0},{0,0}});
        surface.TextureData = [0xFF008040,0xFF008040,0xFF008040,0xFF008040];
        surface.FaceAlpha = alpha;
        surface.Style = SurfaceStyle.Filled;
        var context = new RecordingRenderContext(new Size2D(400,300));
        new FigureRenderer().Render(figure, context);
        if (alpha == 0) { Assert.Empty(context.TriangleColors); return; }
        Assert.NotEmpty(context.TriangleColors);
        Assert.All(context.TriangleColors, color => {
            Assert.Equal(expectedAlpha, (int)(color >> 24));
            Assert.Equal(0x008040u, color & 0xFFFFFF);
        });
    }
    [Theory]
    [InlineData(90,"mm:ss","01:30")]
    [InlineData(-90,"mm:ss","-01:30")]
    [InlineData(90061,"hh:mm:ss","25:01:01")]
    [InlineData(.125,"hh:mm:ss.SSS","00:00:00.125")]
    [InlineData(90061,"dd:hh:mm:ss","01:01:01:01")]
    public void DurationLabelsKeepElapsedTime(double seconds, string format, string expected) =>
        Assert.Equal(expected, DurationTickGenerator.Format(seconds,format));
    [Fact]
    public void DurationTicksUseDayCoordinatesAndElapsedLabels()
    {
        var ticks = new DurationTickGenerator("mm:ss").Generate(new DataRange(0,180.0/86400),6);
        Assert.Contains(ticks.MajorTicks,t => t.Label == "01:40" && Math.Abs(t.Value-100.0/86400)<1e-12);
    }
}
