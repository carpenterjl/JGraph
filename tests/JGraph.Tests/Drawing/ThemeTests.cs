using JGraph.Core.Drawing;
using JGraph.Core.Model;
using Xunit;

namespace JGraph.Tests.Drawing;

public class ThemeTests
{
    private static (FigureModel Figure, AxesModel Axes) Sample()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        return (figure, axes);
    }

    [Fact]
    public void Light_MatchesModelDefaults_SoApplyingItIsANoOpOnTypography()
    {
        (FigureModel figure, AxesModel axes) = Sample();
        double figureTitle = figure.TitleStyle.FontSize;
        double axesTitle = axes.TitleStyle.FontSize;
        double axisLabel = axes.PrimaryXAxis.LabelStyle.FontSize;
        double tick = axes.PrimaryXAxis.TickLabelStyle.FontSize;

        Theme.Light.Apply(figure);

        Assert.Equal(figureTitle, figure.TitleStyle.FontSize);
        Assert.Equal(axesTitle, axes.TitleStyle.FontSize);
        Assert.Equal(axisLabel, axes.PrimaryXAxis.LabelStyle.FontSize);
        Assert.Equal(tick, axes.PrimaryXAxis.TickLabelStyle.FontSize);
        Assert.Equal("Segoe UI", figure.TitleStyle.FontFamily);
    }

    [Fact]
    public void Apply_SetsColors()
    {
        (FigureModel figure, AxesModel axes) = Sample();

        Theme.Dark.Apply(figure);

        Assert.Equal(Theme.Dark.FigureBackground, figure.Background);
        Assert.Equal(Theme.Dark.AxesBackground, axes.Background);
        Assert.Equal(Theme.Dark.Title, figure.TitleStyle.Color);
        Assert.Equal(Theme.Dark.MajorGrid, axes.Grid.MajorLineStyle.Color);
    }

    [Fact]
    public void Presentation_AppliesLargeBoldTypography()
    {
        (FigureModel figure, AxesModel axes) = Sample();

        Theme.Presentation.Apply(figure);

        Assert.Equal("Segoe UI Semibold", figure.TitleStyle.FontFamily);
        Assert.Equal(26, figure.TitleStyle.FontSize);
        Assert.Equal(22, axes.TitleStyle.FontSize);
        Assert.Equal(19, axes.PrimaryYAxis.LabelStyle.FontSize);
        Assert.Equal(16, axes.PrimaryYAxis.TickLabelStyle.FontSize);
        Assert.True(figure.TitleStyle.Bold);
    }

    [Fact]
    public void Ieee_AppliesCompactSerifTypography()
    {
        (FigureModel figure, AxesModel axes) = Sample();

        Theme.Ieee.Apply(figure);

        Assert.Equal("Times New Roman", axes.TitleStyle.FontFamily);
        Assert.Equal(11, figure.TitleStyle.FontSize);
        Assert.Equal(10, axes.TitleStyle.FontSize);
        Assert.Equal(9, axes.PrimaryXAxis.LabelStyle.FontSize);
        Assert.Equal(8, axes.PrimaryXAxis.TickLabelStyle.FontSize);
        Assert.False(figure.TitleStyle.Bold);
        Assert.False(axes.TitleStyle.Bold);
    }

    [Fact]
    public void Apply_PreservesItalic()
    {
        (FigureModel figure, AxesModel axes) = Sample();
        axes.PrimaryXAxis.LabelStyle = new TextStyle(Colors.Black, italic: true);

        Theme.Presentation.Apply(figure);

        Assert.True(axes.PrimaryXAxis.LabelStyle.Italic);
    }
}
