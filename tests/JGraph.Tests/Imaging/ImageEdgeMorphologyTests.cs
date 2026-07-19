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
    }
}
