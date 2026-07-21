using JGraph.Imaging;
using Xunit;

namespace JGraph.Tests.Imaging;

/// <summary>M24 wave B: edge detection, morphology, and connected-component labeling anchors.</summary>
public class ImageEdgeMorphologyTests
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
    public void Sobel_FindsAVerticalStep()
    {
        // Left half black, right half white → a vertical edge near the transition column.
        var values = new double[5, 6];
        for (int r = 0; r < 5; r++)
        {
            for (int c = 3; c < 6; c++)
            {
                values[r, c] = 1.0;
            }
        }

        using ImageBuffer image = Gray(values);
        // Explicit threshold keeps the tiny fixture deterministic (the 4×mean auto-threshold is tuned
        // for sparse edges in larger images).
        using ImageBuffer edges = EdgeDetection.Detect(image, EdgeDetection.Method.Sobel, threshold: 1.0);
        Assert.True(edges.IsBinary);

        int edgePixels = 0;
        foreach (double v in edges.Pixels)
        {
            edgePixels += (int)v;
        }

        Assert.True(edgePixels > 0, "expected the vertical step to produce edge pixels");
        // The middle row's edge should sit at the transition (columns 2 or 3).
        Assert.True(edges[2, 2, 0] == 1.0 || edges[2, 3, 0] == 1.0);
    }

    [Fact]
    public void Canny_OnAConstantImage_FindsNoEdges()
    {
        using ImageBuffer flat = Gray(new double[6, 6]);
        using ImageBuffer edges = EdgeDetection.Detect(flat, EdgeDetection.Method.Canny);
        foreach (double v in edges.Pixels)
        {
            Assert.Equal(0.0, v);
        }
    }

    [Fact]
    public void Canny_OnAStep_ProducesEdges()
    {
        var values = new double[8, 8];
        for (int r = 0; r < 8; r++)
        {
            for (int c = 4; c < 8; c++)
            {
                values[r, c] = 1.0;
            }
        }

        using ImageBuffer image = Gray(values);
        using ImageBuffer edges = EdgeDetection.Detect(image, EdgeDetection.Method.Canny);
        int edgePixels = 0;
        foreach (double v in edges.Pixels)
        {
            edgePixels += (int)v;
        }

        Assert.True(edgePixels > 0);
    }

    [Fact]
    public void Erode_ShrinksAOnePixelBlobToNothing()
    {
        var values = new double[3, 3];
        values[1, 1] = 1.0;
        using ImageBuffer image = Gray(values);
        using ImageBuffer eroded = Morphology.Erode(image, Morphology.Square(3));
        foreach (double v in eroded.Pixels)
        {
            Assert.Equal(0.0, v); // the lone pixel has a 0 neighbour → min is 0
        }
    }

    [Fact]
    public void Open_OnASolidBlock_IsIdentity()
    {
        // A 3×3 block well inside a 7×7 field: erode shrinks it to its centre, dilate restores it.
        var values = new double[7, 7];
        for (int r = 2; r <= 4; r++)
        {
            for (int c = 2; c <= 4; c++)
            {
                values[r, c] = 1.0;
            }
        }

        using ImageBuffer image = Gray(values);
        using ImageBuffer opened = Morphology.Open(image, Morphology.Square(3));
        for (int r = 0; r < 7; r++)
        {
            for (int c = 0; c < 7; c++)
            {
                Assert.Equal(image[r, c, 0], opened[r, c, 0], 6);
            }
        }
    }

    [Fact]
    public void GrayscaleErode_IsLocalMinimum()
    {
        using ImageBuffer image = Gray(new double[,]
        {
            { 0.9, 0.9, 0.9 },
            { 0.9, 0.2, 0.9 },
            { 0.9, 0.9, 0.9 },
        });
        using ImageBuffer eroded = Morphology.Erode(image, Morphology.Square(3));
        Assert.Equal(0.2, eroded[1, 1, 0], 6); // the low value propagates to the center's min
    }

    [Fact]
    public void Roberts_FindsAVerticalStep()
    {
        // Left half black, right half white; Roberts' 2×2 kernels respond along the transition.
        var values = new double[5, 6];
        for (int r = 0; r < 5; r++)
        {
            for (int c = 3; c < 6; c++)
            {
                values[r, c] = 1.0;
            }
        }

        using ImageBuffer image = Gray(values);
        using ImageBuffer edges = EdgeDetection.Detect(image, EdgeDetection.Method.Roberts, threshold: 0.5);

        Assert.True(edges.IsBinary);
        int marked = 0;
        for (int r = 0; r < 5; r++)
        {
            for (int c = 0; c < 6; c++)
            {
                if (edges[r, c, 0] == 1.0)
                {
                    marked++;
                    Assert.InRange(c, 2, 3); // the response straddles the step, nowhere else
                }
            }
        }

        Assert.True(marked > 0);
    }

    [Fact]
    public void Log_MarksTheRimOfABlobAndNothingOnAFlatImage()
    {
        var values = new double[15, 15];
        for (int r = 5; r < 10; r++)
        {
            for (int c = 5; c < 10; c++)
            {
                values[r, c] = 1.0;
            }
        }

        using ImageBuffer image = Gray(values);
        using ImageBuffer edges = EdgeDetection.Detect(image, EdgeDetection.Method.Log);
        Assert.True(edges.IsBinary);

        // Zero crossings ring the blob: nothing at the very centre, something near its border.
        Assert.Equal(0.0, edges[7, 7, 0]);
        int rim = 0;
        for (int c = 0; c < 15; c++)
        {
            rim += (int)edges[7, c, 0];
        }

        Assert.True(rim >= 2, "the row through the blob should cross zero on both sides");

        using ImageBuffer flat = Gray(new double[9, 9]);
        using ImageBuffer none = EdgeDetection.Detect(flat, EdgeDetection.Method.Log);
        for (int r = 0; r < 9; r++)
        {
            for (int c = 0; c < 9; c++)
            {
                Assert.Equal(0.0, none[r, c, 0]);
            }
        }
    }

    [Fact]
    public void GradientXY_SignsFollowTheRampDirection()
    {
        // A horizontal ramp: samples increase left to right, constant down each column.
        var values = new double[5, 5];
        for (int r = 0; r < 5; r++)
        {
            for (int c = 0; c < 5; c++)
            {
                values[r, c] = c / 4.0;
            }
        }

        using ImageBuffer image = Gray(values);
        (ImageBuffer gx, ImageBuffer gy) = Gradients.GradientXY(image);
        using (gx)
        using (gy)
        {
            Assert.True(gx[2, 2, 0] > 0);          // brightness rises with x
            Assert.Equal(0.0, gy[2, 2, 0], 10);    // nothing changes down a column
        }
    }

    [Fact]
    public void Gradient_ReportsMagnitudeAndDirectionOfAHorizontalRamp()
    {
        var values = new double[5, 5];
        for (int r = 0; r < 5; r++)
        {
            for (int c = 0; c < 5; c++)
            {
                values[r, c] = c / 4.0;
            }
        }

        using ImageBuffer image = Gray(values);
        (ImageBuffer magnitude, ImageBuffer direction) = Gradients.Gradient(image);
        using (magnitude)
        using (direction)
        {
            Assert.True(magnitude[2, 2, 0] > 0);
            Assert.Equal(0.0, direction[2, 2, 0], 6); // pure +x gradient → 0°
        }
    }

    [Fact]
    public void BwLabel_CountsThreeBlobs()
    {
        // Three separated single pixels.
        var values = new double[5, 5];
        values[0, 0] = 1.0;
        values[0, 4] = 1.0;
        values[4, 2] = 1.0;
        using ImageBuffer image = Gray(values);
        (int[,] labels, int count) = Regions.Label(image, connectivity: 8);
        Assert.Equal(3, count);
        Assert.NotEqual(labels[0, 0], labels[0, 4]);
    }

    [Fact]
    public void BwLabel_DiagonalConnectivityDiffers()
    {
        var values = new double[2, 2];
        values[0, 0] = 1.0;
        values[1, 1] = 1.0; // diagonally touching
        using ImageBuffer image = Gray(values);

        (_, int count8) = Regions.Label(image, connectivity: 8);
        (_, int count4) = Regions.Label(image, connectivity: 4);
        Assert.Equal(1, count8); // 8-connectivity joins the diagonal
        Assert.Equal(2, count4); // 4-connectivity keeps them separate
    }

    [Fact]
    public void FillHoles_ClosesAnEnclosedCavityButNotABorderBay()
    {
        // A 5×5 ring with a single hole at its centre.
        var values = new double[5, 5];
        for (int r = 1; r <= 3; r++)
        {
            for (int c = 1; c <= 3; c++)
            {
                values[r, c] = 1.0;
            }
        }

        values[2, 2] = 0.0; // the hole
        using ImageBuffer ring = Gray(values);
        using ImageBuffer filled = Regions.FillHoles(ring);

        Assert.Equal(1.0, filled[2, 2, 0]);
        Assert.Equal(0.0, filled[0, 0, 0]); // outside background is untouched

        // A U-shape open to the top edge: its interior connects to the border, so it stays background.
        var bay = new double[4, 3];
        bay[1, 0] = bay[2, 0] = bay[3, 0] = 1.0;
        bay[3, 1] = 1.0;
        bay[1, 2] = bay[2, 2] = bay[3, 2] = 1.0;
        using ImageBuffer open = Gray(bay);
        using ImageBuffer stillOpen = Regions.FillHoles(open);
        Assert.Equal(0.0, stillOpen[1, 1, 0]);
    }

    [Fact]
    public void AreaOpen_DropsTheSmallComponentsOnly()
    {
        // One 4-pixel block plus two isolated specks.
        var values = new double[6, 6];
        for (int r = 1; r <= 2; r++)
        {
            for (int c = 1; c <= 2; c++)
            {
                values[r, c] = 1.0;
            }
        }

        values[5, 0] = 1.0;
        values[0, 5] = 1.0;

        using ImageBuffer image = Gray(values);
        using ImageBuffer kept = Regions.AreaOpen(image, minArea: 4);

        Assert.Equal(1.0, kept[1, 1, 0]);
        Assert.Equal(0.0, kept[5, 0, 0]);
        Assert.Equal(0.0, kept[0, 5, 0]);
        (_, int count) = Regions.Label(kept, 8);
        Assert.Equal(1, count);
    }

    [Fact]
    public void AreaOpen_ConnectivityDecidesWhetherADiagonalPairSurvives()
    {
        var values = new double[3, 3];
        values[0, 0] = 1.0;
        values[1, 1] = 1.0; // diagonally touching → one 2-pixel region at 8, two 1-pixel ones at 4
        using ImageBuffer image = Gray(values);

        using ImageBuffer eight = Regions.AreaOpen(image, minArea: 2, connectivity: 8);
        using ImageBuffer four = Regions.AreaOpen(image, minArea: 2, connectivity: 4);

        Assert.Equal(1.0, eight[0, 0, 0]);
        Assert.Equal(0.0, four[0, 0, 0]);
    }

    [Fact]
    public void Measure_ReportsAreaAndBoundingBox()
    {
        // A 2×3 filled block at rows 1..2, cols 1..3.
        var values = new double[4, 5];
        for (int r = 1; r <= 2; r++)
        {
            for (int c = 1; c <= 3; c++)
            {
                values[r, c] = 1.0;
            }
        }

        using ImageBuffer image = Gray(values);
        (int[,] labels, int count) = Regions.Label(image, 8);
        Regions.RegionProperty[] props = Regions.Measure(labels, count);

        Assert.Single(props);
        Regions.RegionProperty region = props[0];
        Assert.Equal(6, region.Area);
        Assert.Equal(0.5 + 1, region.BoundingBoxX); // col 1 (0-based) → x = 1.5
        Assert.Equal(0.5 + 1, region.BoundingBoxY);
        Assert.Equal(3, region.BoundingBoxWidth);
        Assert.Equal(2, region.BoundingBoxHeight);
        Assert.Equal(3.0, region.CentroidX, 6); // mean col (2 in 0-based) + 1 = 3
        Assert.True(double.IsNaN(region.MeanIntensity)); // no intensity image was supplied
    }

    [Fact]
    public void Measure_WithIntensityPullsTheCentroidTowardsTheBrightSide()
    {
        // A 1×3 horizontal bar whose right pixel is four times as bright as its left one.
        var mask = new double[3, 5];
        mask[1, 1] = mask[1, 2] = mask[1, 3] = 1.0;
        var weights = new double[3, 5];
        weights[1, 1] = 0.1;
        weights[1, 2] = 0.1;
        weights[1, 3] = 0.8;

        using ImageBuffer image = Gray(mask);
        using ImageBuffer intensity = Gray(weights);
        (int[,] labels, int count) = Regions.Label(image, 8);
        Regions.RegionProperty region = Assert.Single(Regions.Measure(labels, count, intensity));

        Assert.Equal(3.0, region.CentroidX, 6);          // geometric: middle of cols 1..3 → 2 + 1
        Assert.Equal(1.0 / 3.0, region.MeanIntensity, 6); // (0.1 + 0.1 + 0.8) / 3
        // Weighted: (0.1*1 + 0.1*2 + 0.8*3) / 1.0 = 2.7, then +1 for MATLAB's 1-based coordinates.
        Assert.Equal(3.7, region.WeightedCentroidX, 6);
        Assert.Equal(2.0, region.WeightedCentroidY, 6);  // every pixel is on row 1 → 1 + 1
    }

    [Fact]
    public void Measure_WithAMismatchedIntensityImageThrows()
    {
        using ImageBuffer image = Gray(new double[3, 3]);
        using ImageBuffer wrongSize = Gray(new double[4, 3]);
        (int[,] labels, int count) = Regions.Label(image, 8);
        Assert.Throws<ArgumentException>(() => Regions.Measure(labels, count, wrongSize));
    }

    [Fact]
    public void WeightedCentroid_AveragesEverySample_AcrossDisconnectedBlobs()
    {
        // Two blobs of unequal weight, four columns apart: the centre lands two thirds of the way
        // towards the heavier one, which no per-component measurement would ever report.
        using ImageBuffer image = Gray(new double[,]
        {
            { 0.25, 0, 0, 0, 0.50 },
            { 0.25, 0, 0, 0, 0.50 },
        });

        (double x, double y) = Regions.WeightedCentroid(image);

        Assert.Equal(1 + (4 * (2.0 / 3)), x, 6); // 1-based: 1 and 5 weighted 0.5 : 1.0
        Assert.Equal(1.5, y, 6);                 // both rows carry the same weight
    }

    [Fact]
    public void WeightedCentroid_OfAnAllZeroImageThrows()
    {
        using ImageBuffer image = Gray(new double[2, 2]);
        Assert.Throws<ArgumentException>(() => Regions.WeightedCentroid(image));
    }
}
