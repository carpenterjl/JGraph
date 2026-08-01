using JGraph.Imaging;
using Xunit;

namespace JGraph.Tests.Imaging;

/// <summary>
/// M46 wave B: the padding, neighbourhood-statistics, integral-image and block-rearrangement
/// algorithms, checked against fixtures small enough to work out by hand.
/// </summary>
public class NeighborhoodTests
{
    private static ImageBuffer Ramp(int height, int width)
    {
        var image = new ImageBuffer(height, width, 1);
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                image[r, c, 0] = (r * width) + c;
            }
        }

        return image;
    }

    [Fact]
    public void Pad_Replicate_CopiesTheEdgeOutwards()
    {
        using ImageBuffer source = Ramp(2, 2); // 0 1 / 2 3
        using ImageBuffer padded = Neighborhoods.Pad(source, 1, 1, Filters.Boundary.Replicate);

        Assert.Equal(4, padded.Height);
        Assert.Equal(4, padded.Width);
        Assert.Equal(0.0, padded[0, 0, 0]);
        Assert.Equal(1.0, padded[0, 3, 0]);
        Assert.Equal(3.0, padded[3, 3, 0]);
    }

    [Fact]
    public void Pad_Circular_WrapsAround()
    {
        using ImageBuffer source = Ramp(2, 2);
        using ImageBuffer padded = Neighborhoods.Pad(source, 1, 1, Filters.Boundary.Circular);

        // The pixel before column 0 is the last column, so the top-left corner is the bottom-right one.
        Assert.Equal(3.0, padded[0, 0, 0]);
        Assert.Equal(0.0, padded[3, 3, 0]);
    }

    [Fact]
    public void Pad_PostOnly_LeavesTheOriginWhereItWas()
    {
        using ImageBuffer source = Ramp(2, 2);
        using ImageBuffer padded = Neighborhoods.Pad(
            source, 2, 0, Filters.Boundary.Zero, 7.0, Neighborhoods.PadDirection.Post);

        Assert.Equal(4, padded.Height);
        Assert.Equal(2, padded.Width);
        Assert.Equal(0.0, padded[0, 0, 0]);
        Assert.Equal(7.0, padded[3, 1, 0]);
    }

    [Fact]
    public void OrderFilter_SelectsTheRequestedRank()
    {
        using ImageBuffer source = Ramp(3, 3);
        bool[,] domain = Neighborhoods.Rectangle(3, 3);

        using ImageBuffer minimum = Neighborhoods.OrderFilter(source, domain, 1, null, Filters.Boundary.Replicate);
        using ImageBuffer median = Neighborhoods.OrderFilter(source, domain, 5, null, Filters.Boundary.Replicate);
        using ImageBuffer maximum = Neighborhoods.OrderFilter(source, domain, 9, null, Filters.Boundary.Replicate);

        // The centre pixel's replicated 3×3 neighbourhood is the whole ramp, 0…8.
        Assert.Equal(0.0, minimum[1, 1, 0]);
        Assert.Equal(4.0, median[1, 1, 0]);
        Assert.Equal(8.0, maximum[1, 1, 0]);
    }

    [Fact]
    public void RangeFilter_OnAFlatImage_IsZeroEverywhere()
    {
        using var flat = new ImageBuffer(4, 4, 1);
        flat.Pixels.Fill(0.25);
        using ImageBuffer range = Neighborhoods.Range(flat, Neighborhoods.Rectangle(3, 3));

        foreach (double value in range.Pixels)
        {
            Assert.Equal(0.0, value);
        }
    }

    [Fact]
    public void StandardDeviation_MatchesTheSampleFormula()
    {
        using ImageBuffer source = Ramp(3, 3);
        using ImageBuffer std = Neighborhoods.StandardDeviation(source, Neighborhoods.Rectangle(3, 3));

        // The centre sees 0…8: mean 4, sum of squared deviations 60, divided by n−1 = 8.
        Assert.Equal(Math.Sqrt(60.0 / 8.0), std[1, 1, 0], 12);
    }

    [Fact]
    public void Entropy_OfAUniformNeighbourhood_IsZero()
    {
        using var flat = new ImageBuffer(3, 3, 1);
        flat.Pixels.Fill(0.5);
        using ImageBuffer entropy = Neighborhoods.Entropy(flat, Neighborhoods.Rectangle(3, 3));
        Assert.Equal(0.0, entropy[1, 1, 0], 12);
    }

    [Fact]
    public void Mode_BreaksTiesTowardsTheSmallerValue()
    {
        using var image = new ImageBuffer(1, 4, 1);
        image[0, 0, 0] = 0.25;
        image[0, 1, 0] = 0.25;
        image[0, 2, 0] = 0.75;
        image[0, 3, 0] = 0.75;

        using ImageBuffer mode = Neighborhoods.Mode(image, Neighborhoods.Rectangle(1, 4));
        Assert.Equal(0.25, mode[0, 1, 0], 12);
    }

    [Fact]
    public void Wiener_LeavesTheInteriorOfAFlatImageAlone()
    {
        // MATLAB's wiener2 estimates its local statistics with zero-padded box means, so the border of
        // any image looks like a step to it and contributes real variance. That is why this checks the
        // interior: a flat picture is only genuinely flat away from its own edges.
        using var flat = new ImageBuffer(9, 9, 1);
        flat.Pixels.Fill(0.4);
        (ImageBuffer result, double noise) = Neighborhoods.Wiener(flat, 3, 3);
        using (result)
        {
            Assert.True(noise > 0);
            for (int r = 2; r < 7; r++)
            {
                for (int c = 2; c < 7; c++)
                {
                    Assert.Equal(0.4, result[r, c, 0], 12);
                }
            }
        }
    }

    [Fact]
    public void Wiener_WithAStatedNoisePower_UsesItRatherThanEstimating()
    {
        using ImageBuffer source = Ramp(9, 9);
        (ImageBuffer result, double noise) = Neighborhoods.Wiener(source, 3, 3, 4.0);
        using (result)
        {
            Assert.Equal(4.0, noise);
        }
    }

    [Fact]
    public void IntegralImage_AnswersAnyRectangleInFourLookups()
    {
        using ImageBuffer source = Ramp(4, 4); // 0…15
        double[,] integral = Neighborhoods.IntegralImage(source);

        Assert.Equal(5, integral.GetLength(0));
        Assert.Equal(5, integral.GetLength(1));
        Assert.Equal(120.0, integral[4, 4]); // the whole 0…15 ramp

        // Rows 1–2, columns 1–2: 5 + 6 + 9 + 10.
        double block = integral[3, 3] - integral[1, 3] - integral[3, 1] + integral[1, 1];
        Assert.Equal(30.0, block);
    }

    [Fact]
    public void RotatedIntegralImage_SumsTheTriangleAboveEachApex()
    {
        using var ones = new ImageBuffer(4, 5, 1);
        ones.Pixels.Fill(1.0);
        double[,] rotated = Neighborhoods.RotatedIntegralImage(ones);

        Assert.Equal(5, rotated.GetLength(0));
        Assert.Equal(7, rotated.GetLength(1));

        // The triangle for apex (row 2, column 2) covers 1 + 3 + 5 = 9 pixels of an all-ones image,
        // and every one of them is inside the picture.
        Assert.Equal(9.0, rotated[3, 3], 12);

        // An apex against the left edge has its triangle clipped: rows 0…2 contribute 1, 2 and 3.
        Assert.Equal(6.0, rotated[3, 1], 12);
    }

    [Fact]
    public void IntegralBoxFilter_MatchesTheDirectMean()
    {
        using ImageBuffer source = Ramp(5, 5);
        double[,] integral = Neighborhoods.IntegralImage(source);
        double[,] filtered = Neighborhoods.IntegralBoxFilter(integral, 3, 3);

        Assert.Equal(3, filtered.GetLength(0));
        Assert.Equal(3, filtered.GetLength(1));

        // The first valid window is rows 0–2, columns 0–2 of the ramp: mean 6.
        Assert.Equal(6.0, filtered[0, 0], 12);
    }

    [Fact]
    public void Im2Col_Sliding_OrdersBlocksAndElementsColumnMajor()
    {
        var a = new double[,] { { 1, 4 }, { 2, 5 }, { 3, 6 } };
        double[,] columns = BlockProcessing.Im2Col(a, 2, 2, BlockProcessing.BlockKind.Sliding);

        Assert.Equal(4, columns.GetLength(0));
        Assert.Equal(2, columns.GetLength(1)); // two vertical positions, one horizontal

        // First block is rows 0–1 of both columns, read down then across.
        Assert.Equal(new[] { 1.0, 2.0, 4.0, 5.0 }, new[] { columns[0, 0], columns[1, 0], columns[2, 0], columns[3, 0] });
        Assert.Equal(new[] { 2.0, 3.0, 5.0, 6.0 }, new[] { columns[0, 1], columns[1, 1], columns[2, 1], columns[3, 1] });
    }

    [Fact]
    public void Im2Col_Distinct_ZeroPadsThePartialBlocks()
    {
        var a = new double[,] { { 1, 2, 3 }, { 4, 5, 6 } };
        double[,] columns = BlockProcessing.Im2Col(a, 2, 2, BlockProcessing.BlockKind.Distinct);

        Assert.Equal(4, columns.GetLength(0));
        Assert.Equal(2, columns.GetLength(1)); // one full block, one half-width block

        // The second block's right-hand column falls outside the matrix and reads as zero.
        Assert.Equal(3.0, columns[0, 1]);
        Assert.Equal(6.0, columns[1, 1]);
        Assert.Equal(0.0, columns[2, 1]);
        Assert.Equal(0.0, columns[3, 1]);
    }

    [Fact]
    public void Col2Im_Distinct_UndoesIm2Col()
    {
        var a = new double[,] { { 1, 2, 3 }, { 4, 5, 6 }, { 7, 8, 9 } };
        double[,] columns = BlockProcessing.Im2Col(a, 2, 2, BlockProcessing.BlockKind.Distinct);
        double[,] round = BlockProcessing.Col2Im(columns, 2, 2, 3, 3, BlockProcessing.BlockKind.Distinct);

        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                Assert.Equal(a[r, c], round[r, c]);
            }
        }
    }

    [Fact]
    public void BestBlockSize_PrefersAnExactDivisor()
    {
        Assert.Equal(100, BlockProcessing.BestBlockSize(500, 100)); // divides five ways, no remainder
        Assert.Equal(64, BlockProcessing.BestBlockSize(64, 100));   // smaller than the limit: take it whole
        Assert.Equal(97, BlockProcessing.BestBlockSize(97 * 3, 100));

        // A prime dimension has no divisor at all under the limit, so the fallback picks the size that
        // leaves the largest final block — 51, whose last block is 50 — rather than the largest size,
        // which would leave a final block of one.
        Assert.Equal(51, BlockProcessing.BestBlockSize(101, 100));
    }

    [Fact]
    public void GaussianBlur_PreservesTheMeanOfAFlatImage()
    {
        using var flat = new ImageBuffer(9, 9, 1);
        flat.Pixels.Fill(0.6);
        using ImageBuffer blurred = Filters.GaussianBlur(flat, 2.0, 2.0);

        foreach (double value in blurred.Pixels)
        {
            Assert.Equal(0.6, value, 12);
        }
    }

    [Fact]
    public void GaussianBlur_MatchesTheFullTwoDimensionalKernel()
    {
        using ImageBuffer source = Ramp(7, 7);
        const double sigma = 1.5;
        int size = (2 * (int)Math.Ceiling(2 * sigma)) + 1;

        using ImageBuffer separable = Filters.GaussianBlur(source, sigma, sigma, size, size);
        using ImageBuffer direct = Filters.Correlate(
            source, Kernels.Gaussian(size, sigma), Filters.Boundary.Replicate);

        // The separable pass is exact, not an approximation — the only difference allowed here is the
        // order the same products are summed in.
        for (int r = 2; r < 5; r++)
        {
            for (int c = 2; c < 5; c++)
            {
                Assert.Equal(direct[r, c, 0], separable[r, c, 0], 10);
            }
        }
    }

    [Fact]
    public void Filter_Convolution_AgreesWithConv2Same()
    {
        using ImageBuffer source = Ramp(5, 5);
        double[,] kernel = { { 1, 2 }, { 3, 4 } }; // even-sized, where the two anchors differ

        using ImageBuffer filtered = Filters.Filter(source, kernel, convolve: true);
        double[,] expected = Filters.Convolve2(PointOps.ToMatrix(source, 0), kernel, Conv2Shape.Same);

        for (int r = 0; r < 5; r++)
        {
            for (int c = 0; c < 5; c++)
            {
                Assert.Equal(expected[r, c], filtered[r, c, 0], 12);
            }
        }
    }

    [Fact]
    public void Filter_Full_IsLargerByTheKernelExtent()
    {
        using ImageBuffer source = Ramp(4, 4);
        double[,] kernel = Kernels.Average(3);
        using ImageBuffer full = Filters.Filter(source, kernel, full: true);

        Assert.Equal(6, full.Height);
        Assert.Equal(6, full.Width);
    }

    [Fact]
    public void MotionKernel_SumsToOneAndIsHorizontalAtZeroDegrees()
    {
        double[,] kernel = Kernels.Motion(5, 0);
        double sum = 0;
        foreach (double value in kernel)
        {
            sum += value;
        }

        Assert.Equal(1.0, sum, 10);

        // A horizontal sweep smears along one row, so the kernel is one row tall.
        Assert.Equal(1, kernel.GetLength(0));
        Assert.Equal(5, kernel.GetLength(1));
    }

    [Fact]
    public void UnsharpKernel_SumsToOne_SoItLeavesAFlatRegionAlone()
    {
        double sum = 0;
        foreach (double value in Kernels.Unsharp(0.2))
        {
            sum += value;
        }

        Assert.Equal(1.0, sum, 12);
    }

    [Fact]
    public void CentralDifference_IsTheHalvedNeighbourGap()
    {
        using ImageBuffer source = Ramp(3, 3); // rows step by 3, columns by 1
        (ImageBuffer gx, ImageBuffer gy) = Gradients.GradientXY(source, Gradients.Operator.Central);
        using (gx)
        using (gy)
        {
            Assert.Equal(1.0, gx[1, 1, 0], 12);
            Assert.Equal(3.0, gy[1, 1, 0], 12);
        }
    }

    [Fact]
    public void IntermediateDifference_IsTheForwardGap()
    {
        using ImageBuffer source = Ramp(3, 3);
        (ImageBuffer gx, ImageBuffer gy) = Gradients.GradientXY(source, Gradients.Operator.Intermediate);
        using (gx)
        using (gy)
        {
            Assert.Equal(1.0, gx[1, 1, 0], 12);
            Assert.Equal(3.0, gy[1, 1, 0], 12);
        }
    }

    [Fact]
    public void Edge_ReportsTheThresholdItChose()
    {
        using var image = new ImageBuffer(8, 8, 1);
        for (int r = 0; r < 8; r++)
        {
            for (int c = 4; c < 8; c++)
            {
                image[r, c, 0] = 1.0;
            }
        }

        EdgeDetection.EdgeResult automatic = EdgeDetection.Detect(
            image, EdgeDetection.Method.Sobel, null, null, null, EdgeDetection.Direction.Both);
        using (automatic.Edges)
        {
            Assert.True(automatic.High > 0);
        }

        EdgeDetection.EdgeResult given = EdgeDetection.Detect(
            image, EdgeDetection.Method.Sobel, 0.5, null, null, EdgeDetection.Direction.Both);
        using (given.Edges)
        {
            Assert.Equal(0.5, given.High);
        }
    }

    [Fact]
    public void Edge_Direction_KeepsOnlyTheOrientationAskedFor()
    {
        // A single vertical step: a vertical-edge search finds it, a horizontal-edge search does not.
        using var image = new ImageBuffer(8, 8, 1);
        for (int r = 0; r < 8; r++)
        {
            for (int c = 4; c < 8; c++)
            {
                image[r, c, 0] = 1.0;
            }
        }

        EdgeDetection.EdgeResult vertical = EdgeDetection.Detect(
            image, EdgeDetection.Method.Sobel, 0.5, null, null, EdgeDetection.Direction.Vertical);
        EdgeDetection.EdgeResult horizontal = EdgeDetection.Detect(
            image, EdgeDetection.Method.Sobel, 0.5, null, null, EdgeDetection.Direction.Horizontal);

        using (vertical.Edges)
        using (horizontal.Edges)
        {
            Assert.Contains(vertical.Edges.Pixels.ToArray(), static v => v == 1.0);
            Assert.DoesNotContain(horizontal.Edges.Pixels.ToArray(), static v => v == 1.0);
        }
    }
}
