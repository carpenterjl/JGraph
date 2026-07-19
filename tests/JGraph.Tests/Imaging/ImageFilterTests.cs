using JGraph.Imaging;
using Xunit;

namespace JGraph.Tests.Imaging;

/// <summary>M24: hand-computed anchors for 2-D correlation, convolution, median filtering, and kernels.</summary>
public class ImageFilterTests
{
    private static ImageBuffer Gray(double[,] values)
    {
        int h = values.GetLength(0);
        int w = values.GetLength(1);
        var image = new ImageBuffer(h, w, 1);
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                image[r, c, 0] = values[r, c];
            }
        }

        return image;
    }

    [Fact]
    public void Correlate_Identity_ReturnsSame()
    {
        using ImageBuffer image = Gray(new double[,] { { 0.1, 0.2 }, { 0.3, 0.4 } });
        var identity = new double[,] { { 0, 0, 0 }, { 0, 1, 0 }, { 0, 0, 0 } };
        using ImageBuffer result = Filters.Correlate(image, identity);
        Assert.Equal(0.1, result[0, 0, 0], 6);
        Assert.Equal(0.4, result[1, 1, 0], 6);
    }

    [Fact]
    public void Correlate_BoxBlur_AveragesNeighbours_ZeroBoundary()
    {
        using ImageBuffer image = Gray(new double[,] { { 1, 1, 1 }, { 1, 1, 1 }, { 1, 1, 1 } });
        var box = Kernels.Average(3);
        using ImageBuffer result = Filters.Correlate(image, box, Filters.Boundary.Zero);
        // Center pixel: all 9 neighbours are 1 → mean 1.
        Assert.Equal(1.0, result[1, 1, 0], 6);
        // Corner pixel (0,0): only 4 of 9 taps are inside (value 1), rest zero-padded → 4/9.
        Assert.Equal(4.0 / 9.0, result[0, 0, 0], 6);
    }

    [Fact]
    public void Correlate_Replicate_FillsFromEdge()
    {
        using ImageBuffer image = Gray(new double[,] { { 1, 1, 1 }, { 1, 1, 1 }, { 1, 1, 1 } });
        using ImageBuffer result = Filters.Correlate(image, Kernels.Average(3), Filters.Boundary.Replicate);
        // With edge replication every tap is 1 → mean stays 1 even at the corner.
        Assert.Equal(1.0, result[0, 0, 0], 6);
    }

    [Fact]
    public void Convolve2_Full_MatchesHandComputation()
    {
        var a = new double[,] { { 1, 2 }, { 3, 4 } };
        var b = new double[,] { { 1, 1 }, { 1, 1 } };
        double[,] full = Filters.Convolve2(a, b, Conv2Shape.Full);
        Assert.Equal(3, full.GetLength(0));
        Assert.Equal(3, full.GetLength(1));
        // Known conv2([1 2;3 4],[1 1;1 1]) = [1 3 2; 4 10 6; 3 7 4].
        Assert.Equal(1, full[0, 0]);
        Assert.Equal(10, full[1, 1]);
        Assert.Equal(4, full[2, 2]);
    }

    [Fact]
    public void Convolve2_Same_KeepsFirstOperandSize()
    {
        var a = new double[,] { { 1, 2 }, { 3, 4 } };
        var b = new double[,] { { 1, 1 }, { 1, 1 } };
        double[,] same = Filters.Convolve2(a, b, Conv2Shape.Same);
        Assert.Equal(2, same.GetLength(0));
        Assert.Equal(2, same.GetLength(1));
    }

    [Fact]
    public void Convolve2_Valid_ShrinksByKernel()
    {
        var a = new double[,] { { 1, 2, 3 }, { 4, 5, 6 }, { 7, 8, 9 } };
        var b = new double[,] { { 1, 1 }, { 1, 1 } };
        double[,] valid = Filters.Convolve2(a, b, Conv2Shape.Valid);
        Assert.Equal(2, valid.GetLength(0));
        Assert.Equal(2, valid.GetLength(1));
        // Top-left valid = 1+2+4+5 = 12.
        Assert.Equal(12, valid[0, 0]);
    }

    [Fact]
    public void Median_RemovesAnIsolatedSpike()
    {
        var values = new double[3, 3];
        values[1, 1] = 1.0; // single hot pixel in a field of zeros
        using ImageBuffer image = Gray(values);
        using ImageBuffer result = Filters.Median(image, 3, 3);
        Assert.Equal(0.0, result[1, 1, 0], 6); // median of {0×8, 1} = 0
    }

    [Fact]
    public void Gaussian_SumsToOne()
    {
        double[,] kernel = Kernels.Gaussian(5, 1.0);
        double sum = 0;
        foreach (double v in kernel)
        {
            sum += v;
        }

        Assert.Equal(1.0, sum, 6);
    }

    [Fact]
    public void Log_SumsToZero()
    {
        double[,] kernel = Kernels.LaplacianOfGaussian(5, 0.7);
        double sum = 0;
        foreach (double v in kernel)
        {
            sum += v;
        }

        Assert.Equal(0.0, sum, 6);
    }
}
