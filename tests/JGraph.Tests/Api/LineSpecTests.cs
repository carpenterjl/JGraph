using JGraph.Api;
using JGraph.Core.Drawing;
using Xunit;

namespace JGraph.Tests.Api;

public class LineSpecTests
{
    [Fact]
    public void Parse_ColorLineMarker()
    {
        LineSpec spec = LineSpec.Parse("r--o");
        Assert.Equal(Colors.Red, spec.Color);
        Assert.Equal(DashStyle.Dash, spec.Dash);
        Assert.Equal(MarkerType.Circle, spec.Marker);
        Assert.True(spec.LineSpecified);
        Assert.True(spec.MarkerSpecified);
    }

    [Fact]
    public void Parse_OrderIndependent()
    {
        LineSpec a = LineSpec.Parse("--or");
        LineSpec b = LineSpec.Parse("r--o");
        Assert.Equal(a.Color, b.Color);
        Assert.Equal(a.Dash, b.Dash);
        Assert.Equal(a.Marker, b.Marker);
    }

    [Fact]
    public void Parse_DashDotBeforeSingleDash()
    {
        LineSpec spec = LineSpec.Parse("-.");
        Assert.Equal(DashStyle.DashDot, spec.Dash);
    }

    [Fact]
    public void Parse_Dotted()
    {
        LineSpec spec = LineSpec.Parse("k:");
        Assert.Equal(Colors.Black, spec.Color);
        Assert.Equal(DashStyle.Dot, spec.Dash);
    }

    [Fact]
    public void Parse_MarkerOnly_NoLine()
    {
        LineSpec spec = LineSpec.Parse("o");
        Assert.True(spec.MarkerSpecified);
        Assert.False(spec.LineSpecified);
    }

    [Fact]
    public void Parse_ColorOnly()
    {
        LineSpec spec = LineSpec.Parse("b");
        Assert.Equal(Colors.Blue, spec.Color);
        Assert.False(spec.LineSpecified);
        Assert.False(spec.MarkerSpecified);
    }

    [Fact]
    public void Parse_Empty()
    {
        LineSpec spec = LineSpec.Parse(null);
        Assert.Null(spec.Color);
        Assert.Null(spec.Dash);
        Assert.Null(spec.Marker);
    }
}
