using JGraph.Imaging;
using Xunit;

namespace JGraph.Tests.Imaging;

/// <summary>M24: hand-computed anchors for the point, histogram, and geometry algorithms.</summary>
public class ImageAlgorithmTests
{
    private static ImageBuffer Rgb(double r, double g, double b)
    {
        var image = new ImageBuffer(1, 1, 3);
        image[0, 0, 0] = r;
        image[0, 0, 1] = g;
        image[0, 0, 2] = b;
        return image;
    }

    [Fact]
    public void ToGray_UsesRec601Weights()
    {
        using ImageBuffer rgb = Rgb(1.0, 1.0, 1.0);
        using ImageBuffer gray = PointOps.ToGray(rgb);
        Assert.Equal(1, gray.Channels);
        Assert.Equal(0.9999, gray[0, 0, 0], 6); // 0.2989 + 0.5870 + 0.1140 = 0.9999

        using ImageBuffer red = Rgb(1.0, 0.0, 0.0);
        using ImageBuffer redGray = PointOps.ToGray(red);
        Assert.Equal(0.2989, redGray[0, 0, 0], 4);
    }

    [Fact]
    public void Complement_Inverts()
    {
        using ImageBuffer image = Rgb(0.25, 0.5, 0.75);
        using ImageBuffer inverted = PointOps.Complement(image);
        Assert.Equal(0.75, inverted[0, 0, 0], 6);
        Assert.Equal(0.5, inverted[0, 0, 1], 6);
        Assert.Equal(0.25, inverted[0, 0, 2], 6);
    }

    [Fact]
    public void Add_SaturatesAtOne()
    {
        using var a = new ImageBuffer(1, 1, 1);
        using var b = new ImageBuffer(1, 1, 1);
        a[0, 0, 0] = 0.7;
        b[0, 0, 0] = 0.6;
        using ImageBuffer sum = PointOps.Add(a, b);
        Assert.Equal(1.0, sum[0, 0, 0], 6); // 1.3 clamps to 1
    }

    [Fact]
    public void Otsu_SplitsABimodalImage()
    {
        // Half the pixels at 0.1, half at 0.9 → threshold falls between the two clusters.
        var image = new ImageBuffer(1, 4, 1);
        image[0, 0, 0] = 0.1;
        image[0, 1, 0] = 0.1;
        image[0, 2, 0] = 0.9;
        image[0, 3, 0] = 0.9;
        double level = Histograms.OtsuLevel(image);
        image.Dispose();
        Assert.InRange(level, 0.1, 0.9);
    }

    [Fact]
    public void Binarize_ProducesAZeroOneImage()
    {
        var image = new ImageBuffer(1, 3, 1);
        image[0, 0, 0] = 0.2;
        image[0, 1, 0] = 0.5;
        image[0, 2, 0] = 0.8;
        using ImageBuffer binary = Histograms.Binarize(image, 0.5);
        image.Dispose();
        Assert.Equal(0.0, binary[0, 0, 0]);
        Assert.Equal(0.0, binary[0, 1, 0]); // 0.5 is not > 0.5
        Assert.Equal(1.0, binary[0, 2, 0]);
        Assert.True(binary.IsBinary);
    }

    [Fact]
    public void Histogram_CountsIntoBins()
    {
        var image = new ImageBuffer(1, 4, 1);
        image[0, 0, 0] = 0.0;
        image[0, 1, 0] = 0.0;
        image[0, 2, 0] = 1.0;
        image[0, 3, 0] = 1.0;
        double[] counts = Histograms.Histogram(image, 2);
        image.Dispose();
        Assert.Equal(2.0, counts[0]); // [0, 0.5)
        Assert.Equal(2.0, counts[1]); // [0.5, 1]
    }

    [Fact]
    public void Resize_PreservesCornersWithAlignCorners()
    {
        // 2x2 with distinct corners; upsampling keeps the four corner values exactly.
        var image = new ImageBuffer(2, 2, 1);
        image[0, 0, 0] = 0.0;
        image[0, 1, 0] = 1.0;
        image[1, 0, 0] = 0.5;
        image[1, 1, 0] = 0.25;
        using ImageBuffer big = Geometry.Resize(image, 4, 4, Geometry.Interpolation.Bilinear);
        image.Dispose();

        Assert.Equal(4, big.Height);
        Assert.Equal(0.0, big[0, 0, 0], 6);
        Assert.Equal(1.0, big[0, 3, 0], 6);
        Assert.Equal(0.5, big[3, 0, 0], 6);
        Assert.Equal(0.25, big[3, 3, 0], 6);
        // Interior point dst(0,1) maps to srcC = 1*(1/3) = 0.333 on the top row → 0*(0.667)+1*(0.333).
        Assert.Equal(1.0 / 3.0, big[0, 1, 0], 4);
    }

    [Fact]
    public void Resize_NearestToSameSizeIsIdentity()
    {
        var image = new ImageBuffer(2, 2, 1);
        image[0, 0, 0] = 0.2;
        image[1, 1, 0] = 0.8;
        using ImageBuffer same = Geometry.Resize(image, 2, 2, Geometry.Interpolation.Nearest);
        image.Dispose();
        Assert.Equal(0.2, same[0, 0, 0], 6);
        Assert.Equal(0.8, same[1, 1, 0], 6);
    }

    [Fact]
    public void Rotate180_FlipsCorners()
    {
        var image = new ImageBuffer(2, 2, 1);
        image[0, 0, 0] = 0.1; // top-left
        image[1, 1, 0] = 0.9; // bottom-right
        using ImageBuffer rotated = Geometry.Rotate(image, 180, Geometry.Interpolation.Nearest, loose: false);
        image.Dispose();
        Assert.Equal(0.9, rotated[0, 0, 0], 6); // old bottom-right is now top-left
        Assert.Equal(0.1, rotated[1, 1, 0], 6);
    }

    [Fact]
    public void Rotate90Loose_SwapsDimensions()
    {
        var image = new ImageBuffer(2, 4, 1);
        using ImageBuffer rotated = Geometry.Rotate(image, 90, Geometry.Interpolation.Nearest, loose: true);
        image.Dispose();
        Assert.Equal(4, rotated.Height);
        Assert.Equal(2, rotated.Width);
    }

    [Fact]
    public void Crop_ExtractsTheRectangle()
    {
        var image = new ImageBuffer(4, 4, 1);
        for (int r = 0; r < 4; r++)
        {
            for (int c = 0; c < 4; c++)
            {
                image[r, c, 0] = ((r * 4) + c) / 16.0;
            }
        }

        using ImageBuffer cropped = Geometry.Crop(image, x: 2, y: 2, width: 2, height: 2);
        image.Dispose();
        Assert.Equal(2, cropped.Height);
        Assert.Equal(2, cropped.Width);
        // rect starts at 1-based (col 2, row 2) → 0-based (1,1) → value (1*4+1)/16 = 5/16
        Assert.Equal(5 / 16.0, cropped[0, 0, 0], 6);
    }

    [Fact]
    public void GaussianNoise_StaysInRangeAndShiftsMean()
    {
        var image = new ImageBuffer(64, 64, 1);
        for (int r = 0; r < 64; r++)
        {
            for (int c = 0; c < 64; c++)
            {
                image[r, c, 0] = 0.5;
            }
        }

        using ImageBuffer noisy = PointOps.GaussianNoise(image, mean: 0.0, variance: 0.01, new Random(42));
        image.Dispose();

        double sum = 0;
        foreach (double v in noisy.Pixels)
        {
            Assert.InRange(v, 0.0, 1.0);
            sum += v;
        }

        Assert.Equal(0.5, sum / (64 * 64), 1); // mean stays near 0.5 within a loose tolerance
    }

    [Fact]
    public void Adjust_StretchesContrast()
    {
        using var image = new ImageBuffer(1, 3, 1);
        image[0, 0, 0] = 0.2;
        image[0, 1, 0] = 0.5;
        image[0, 2, 0] = 0.8;
        using ImageBuffer adjusted = PointOps.Adjust(image, 0.2, 0.8, 0.0, 1.0, 1.0);
        Assert.Equal(0.0, adjusted[0, 0, 0], 6);
        Assert.Equal(0.5, adjusted[0, 1, 0], 6);
        Assert.Equal(1.0, adjusted[0, 2, 0], 6);
    }
}
