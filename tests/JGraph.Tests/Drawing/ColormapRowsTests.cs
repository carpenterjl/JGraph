using JGraph.Core.Drawing;
using Xunit;

namespace JGraph.Tests.Drawing;

/// <summary>
/// Open item 145: R2025b's colormap generators, row for row. The values are R2025b's own (probe
/// cmap_probe, printed to 17 digits); the full sweep over 22 maps and 24 sizes is the parity fixture
/// oi_colormap_generators.
/// </summary>
public class ColormapRowsTests
{
    [Fact]
    public void JetIsItsFormulaOverTheRowsAskedFor()
    {
        double[,] expected =
        {
            { 0, 0, 1 }, { 0, 0.5, 1 }, { 0, 1, 1 }, { 0.5, 1, 0.5 },
            { 1, 1, 0 }, { 1, 0.5, 0 }, { 1, 0, 0 }, { 0.5, 0, 0 },
        };
        Assert.Equal(expected, ColormapRows.Of("jet", 8));
        Assert.Equal(new double[,] { { 0, 1, 1 } }, ColormapRows.Of("jet", 1));
        Assert.Equal(new double[,] { { 0, 0, 1 }, { 0, 1, 1 }, { 1, 1, 0 } }, ColormapRows.Of("jet", 3));
    }

    [Fact]
    public void ATableMapIsInterpolatedBetweenItsEnds()
    {
        // parula(2) is the table's first and last rows; parula(1) and turbo(1) its last.
        Assert.Equal(new double[,] { { 0.2422, 0.1504, 0.6603 }, { 0.9769, 0.9839, 0.0805 } }, ColormapRows.Of("parula", 2));
        Assert.Equal(new double[,] { { 0.4796, 0.01583, 0.01055 } }, ColormapRows.Of("turbo", 1));
    }

    [Fact]
    public void SkyAndAbyssAreRampsOverOneBlue()
    {
        Assert.Equal(
            new double[,] { { 0.9066000000000001, 0.9443, 0.9745 }, { 0.48629999999999995, 0.69365, 0.85975 }, { 0.066, 0.443, 0.745 } },
            ColormapRows.Of("sky", 3));
        Assert.Equal(
            new double[,] { { 0.03725, 0.13725, 0.2165 }, { 0.093125, 0.343125, 0.54125 }, { 0.149, 0.549, 0.866 } },
            ColormapRows.Of("abyss", 3));

        // The m = 10 sky stops just short of its blue, as R2025b's does.
        Assert.Equal(0.0660000000000001, ColormapRows.Of("sky", 10)[9, 0]);
    }

    [Fact]
    public void TheSmallSizesAndTheEmptyOne()
    {
        Assert.Equal(new double[,] { { 1, 1, 0.5 }, { 1, 1, 1 } }, ColormapRows.Of("hot", 2));
        Assert.Equal(new double[,] { { 0.125, 0.125, 0.125 } }, ColormapRows.Of("bone", 1));
        Assert.Equal(0.57735026918962573, ColormapRows.Of("pink", 1)[0, 0]);
        foreach (string name in ColormapRows.Names)
        {
            Assert.Equal(0, ColormapRows.Of(name, 0).GetLength(0));
        }
    }

    [Fact]
    public void ColorcubeIsTheCubeLessItsGreysAndAxesThenTheRamps()
    {
        double[,] expected =
        {
            { 1, 1, 0 }, { 0, 1, 1 }, { 1, 0, 1 }, { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 }, { 0, 0, 0 }, { 1, 1, 1 },
        };
        Assert.Equal(expected, ColormapRows.Of("colorcube", 8));
        Assert.Equal(0.66666666666666674, ColormapRows.Of("colorcube", 10)[8, 0]); // 1 - 1/3, colon's way
        Assert.Equal(ColormapRows.Of("gray", 7), ColormapRows.Of("colorcube", 7));
    }

    [Fact]
    public void LinesTakesTheColourOrderItIsGiven()
    {
        Assert.Equal(0.066, ColormapRows.Of("lines", 1, ColormapRows.MatlabLineColors)[0, 0]);
        Assert.Equal(0, ColormapRows.Of("lines", 1)[0, 0]);
        Assert.Equal(ColormapRows.Of("lines", 8)[0, 1], ColormapRows.Of("lines", 8)[7, 1]);
        Assert.Null(ColormapRows.TryOf("viridis", 4));
    }
}
