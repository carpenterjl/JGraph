using JGraph.Core.Drawing;
using Xunit;

namespace JGraph.Tests.Primitives;

public class ColorTests
{
    [Theory]
    [InlineData("#FF0000", 255, 0, 0, 255)]
    [InlineData("00FF00", 0, 255, 0, 255)]
    [InlineData("#8000FF00", 0, 255, 0, 128)]
    [InlineData("#F00", 255, 0, 0, 255)]
    public void Parse_ReadsHexForms(string hex, int r, int g, int b, int a)
    {
        Color color = Color.Parse(hex);
        Assert.Equal((byte)r, color.R);
        Assert.Equal((byte)g, color.G);
        Assert.Equal((byte)b, color.B);
        Assert.Equal((byte)a, color.A);
    }

    [Fact]
    public void TryParse_RejectsInvalid()
    {
        Assert.False(Color.TryParse("nope", out _));
        Assert.False(Color.TryParse("#12", out _));
    }

    [Fact]
    public void ToHex_RoundTrips()
    {
        var color = Color.FromArgb(128, 10, 20, 30);
        Assert.Equal(color, Color.Parse(color.ToHex()));
    }

    [Fact]
    public void Lerp_MidpointIsAverage()
    {
        Color mid = Color.Lerp(Colors.Black, Colors.White, 0.5);
        Assert.Equal(127, mid.R);
        Assert.Equal(127, mid.G);
        Assert.Equal(127, mid.B);
    }

    [Fact]
    public void WithOpacity_ScalesAlpha()
    {
        Color faded = Colors.Black.WithOpacity(0.5);
        Assert.Equal(128, faded.A);
    }
}
