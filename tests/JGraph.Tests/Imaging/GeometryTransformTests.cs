using JGraph.Imaging;
using Xunit;

namespace JGraph.Tests.Imaging;

/// <summary>
/// M46 wave C's algorithm layer: the resampling kernels, the pyramid and checkerboard generators, the
/// geometric-transform matrix and its estimators, and the warp that puts them together.
/// </summary>
public sealed class GeometryTransformTests
{
    [Fact]
    public void Resize_BicubicReproducesALinearRampExactly()
    {
        // Keys' cubic reconstructs any straight line exactly, so doubling a ramp must land on the
        // ramp — anywhere all four taps are real samples rather than mirrored ones.
        using var image = new ImageBuffer(1, 8, 1);
        for (int c = 0; c < 8; c++)
        {
            image[0, c, 0] = (c + 1) / 10.0;
        }

        // Only where all four taps are real samples: past x = 13 the kernel reaches column 9, which
        // the mirror fills with a reflected value rather than a continuation of the ramp.
        using ImageBuffer wide = Geometry.Resize(image, 1, 16, Geometry.Interpolation.Bicubic);
        for (int x = 4; x <= 13; x++)
        {
            double source = (x / 2.0) + 0.25; // MATLAB's one-based half-pixel mapping
            Assert.Equal(source / 10.0, wide[0, x - 1, 0], 10);
        }
    }

    [Fact]
    public void Resize_BoxKernelAveragesWhereNearestPointSamples()
    {
        using var image = new ImageBuffer(4, 4, 1);
        for (int r = 0; r < 4; r++)
        {
            for (int c = 0; c < 4; c++)
            {
                image[r, c, 0] = ((r * 4) + c) / 16.0;
            }
        }

        // Halving with the box kernel and antialiasing on is exactly the mean of each 2×2 block…
        using ImageBuffer averaged = Geometry.Resize(image, 2, 2, Geometry.Interpolation.Nearest, antialiasing: true);
        double mean = (image[0, 0, 0] + image[0, 1, 0] + image[1, 0, 0] + image[1, 1, 0]) / 4.0;
        Assert.Equal(mean, averaged[0, 0, 0], 10);

        // …and with it off, the same kernel picks one pixel out of the four.
        using ImageBuffer sampled = Geometry.Resize(image, 2, 2, Geometry.Interpolation.Nearest, antialiasing: false);
        Assert.Equal(image[1, 1, 0], sampled[0, 0, 0], 10);
    }

    [Fact]
    public void Resize_LanczosKernelsHaveTheirDocumentedReach()
    {
        // A single bright pixel spreads exactly as far as the kernel's lobes reach: two source pixels
        // for lanczos2 and three for lanczos3, which is the whole difference between them.
        using var image = new ImageBuffer(1, 9, 1);
        image[0, 4, 0] = 1.0;

        using ImageBuffer two = Geometry.Resize(image, 1, 18, Geometry.Interpolation.Lanczos2);
        using ImageBuffer three = Geometry.Resize(image, 1, 18, Geometry.Interpolation.Lanczos3);

        // Output column 5 reads the source 2.25 pixels from the impulse: outside two lobes, inside three.
        Assert.Equal(0.0, two[0, 4, 0], 10);
        Assert.NotEqual(0.0, three[0, 4, 0]);

        // Column 3 is 3.25 pixels away, which is outside both.
        Assert.Equal(0.0, three[0, 2, 0], 10);
    }

    [Fact]
    public void Pyramid_HalvesAndDoublesWhilePreservingAFlatField()
    {
        using var image = new ImageBuffer(5, 7, 1);
        for (int r = 0; r < 5; r++)
        {
            for (int c = 0; c < 7; c++)
            {
                image[r, c, 0] = 0.4;
            }
        }

        using ImageBuffer reduced = Geometry.Pyramid(image, expand: false);
        Assert.Equal(3, reduced.Height);
        Assert.Equal(4, reduced.Width);
        Assert.Equal(0.4, reduced[1, 1, 0], 12);
        Assert.Equal(0.4, reduced[0, 0, 0], 12); // replicate borders keep the edges flat too

        using ImageBuffer expanded = Geometry.Pyramid(reduced, expand: true);
        Assert.Equal(5, expanded.Height);
        Assert.Equal(7, expanded.Width);
        Assert.Equal(0.4, expanded[2, 3, 0], 12);
        Assert.Equal(0.4, expanded[0, 0, 0], 12);
    }

    [Fact]
    public void Checkerboard_TilesFourSquaresAndGreysTheRightHalf()
    {
        using ImageBuffer board = Geometry.Checkerboard(2, 1, 2);
        Assert.Equal(4, board.Height);
        Assert.Equal(8, board.Width);

        Assert.Equal(0.0, board[0, 0, 0], 12); // dark top-left of the first tile
        Assert.Equal(1.0, board[0, 2, 0], 12); // white, because it is in the left half
        Assert.Equal(0.7, board[0, 6, 0], 12); // the same square in the right half is grey
        Assert.Equal(0.0, board[0, 4, 0], 12);
        Assert.Equal(1.0, board[2, 0, 0], 12); // the light square below the first one
    }

    [Fact]
    public void Transform_ForwardAndInverseAgreeOnAProjectiveMap()
    {
        var transform = new GeometricTransform(new double[,]
        {
            { 2, 0.3, 0.001 },
            { -0.4, 1.5, 0.002 },
            { 5, -3, 1 },
        });

        Assert.False(transform.IsAffine);
        (double x, double y) = transform.Forward(7.5, -2.25);
        (double back, double alsoBack) = transform.Inverse(x, y);
        Assert.Equal(7.5, back, 10);
        Assert.Equal(-2.25, alsoBack, 10);
    }

    [Fact]
    public void Fit_RecoversAnAffineMapFromThreePairs()
    {
        double[,] moving = { { 0, 0 }, { 1, 0 }, { 0, 1 } };
        double[,] fixedPoints = { { 1, 2 }, { 3, 2 }, { 1, 5 } }; // u = 2x + 1, v = 3y + 2

        GeometricTransform transform = GeometricTransform.Fit(moving, fixedPoints, TransformKind.Affine);
        Assert.True(transform.IsAffine);
        (double u, double v) = transform.Forward(2, 3);
        Assert.Equal(5.0, u, 10);
        Assert.Equal(11.0, v, 10);
    }

    [Fact]
    public void Fit_RecoversRotationAndScaleWithoutReflection()
    {
        // 40° and a scale of 1.7, plus a shift — four pairs, so the fit is over-determined.
        double angle = 40 * Math.PI / 180.0;
        double scale = 1.7;
        double[,] moving = { { 0, 0 }, { 3, 1 }, { -2, 4 }, { 5, -6 } };
        var fixedPoints = new double[4, 2];
        for (int i = 0; i < 4; i++)
        {
            fixedPoints[i, 0] = (scale * ((moving[i, 0] * Math.Cos(angle)) - (moving[i, 1] * Math.Sin(angle)))) + 12;
            fixedPoints[i, 1] = (scale * ((moving[i, 0] * Math.Sin(angle)) + (moving[i, 1] * Math.Cos(angle)))) - 4;
        }

        GeometricTransform transform =
            GeometricTransform.Fit(moving, fixedPoints, TransformKind.NonreflectiveSimilarity);
        for (int i = 0; i < 4; i++)
        {
            (double u, double v) = transform.Forward(moving[i, 0], moving[i, 1]);
            Assert.Equal(fixedPoints[i, 0], u, 8);
            Assert.Equal(fixedPoints[i, 1], v, 8);
        }
    }

    [Fact]
    public void Fit_RecoversAHomographyFromFourPairs()
    {
        var truth = new GeometricTransform(new double[,]
        {
            { 1.2, 0.1, 0.002 },
            { -0.15, 0.9, -0.001 },
            { 30, 12, 1 },
        });

        double[,] moving = { { 0, 0 }, { 100, 0 }, { 100, 80 }, { 0, 80 }, { 40, 25 } };
        var fixedPoints = new double[5, 2];
        for (int i = 0; i < 5; i++)
        {
            (fixedPoints[i, 0], fixedPoints[i, 1]) = truth.Forward(moving[i, 0], moving[i, 1]);
        }

        GeometricTransform fitted = GeometricTransform.Fit(moving, fixedPoints, TransformKind.Projective);
        (double u, double v) = fitted.Forward(63, 47);
        (double tu, double tv) = truth.Forward(63, 47);
        Assert.Equal(tu, u, 6);
        Assert.Equal(tv, v, 6);
    }

    [Fact]
    public void Fit_RefusesDegeneratePointSets()
    {
        double[,] collinear = { { 0, 0 }, { 1, 1 }, { 2, 2 } };
        double[,] target = { { 0, 0 }, { 1, 0 }, { 0, 1 } };
        Assert.Throws<ArgumentException>(
            () => GeometricTransform.Fit(collinear, target, TransformKind.Affine));

        double[,] tooFew = { { 0, 0 }, { 1, 0 }, { 0, 1 } };
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => GeometricTransform.Fit(tooFew, target, TransformKind.Projective));
        Assert.Contains("at least 4", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Warp_ShiftsByWholePixelsExactly()
    {
        using var image = new ImageBuffer(3, 3, 1);
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                image[r, c, 0] = ((r * 3) + c + 1) / 10.0;
            }
        }

        var frame = new SpatialRef(3, 3);
        using ImageBuffer moved = Warping.Warp(
            image, frame, GeometricTransform.Translation(1, 0), frame, Geometry.Interpolation.Bilinear);

        Assert.Equal(0.0, moved[0, 0, 0], 12);       // nothing shifted in from the left
        Assert.Equal(image[0, 0, 0], moved[0, 1, 0], 12);
        Assert.Equal(image[2, 1, 0], moved[2, 2, 0], 12);
    }

    [Fact]
    public void Warp_FillValuesAndSmoothEdgesOnlyChangeTheBorder()
    {
        using var image = new ImageBuffer(3, 3, 1);
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                image[r, c, 0] = 1.0;
            }
        }

        var frame = new SpatialRef(3, 3);
        GeometricTransform half = GeometricTransform.Translation(0.5, 0);

        using ImageBuffer sharp = Warping.Warp(
            image, frame, half, frame, Geometry.Interpolation.Bilinear, [0.25], smoothEdges: false);
        using ImageBuffer smooth = Warping.Warp(
            image, frame, half, frame, Geometry.Interpolation.Bilinear, [0.25], smoothEdges: true);

        Assert.Equal(1.0, sharp[1, 1, 0], 12);
        Assert.Equal(1.0, smooth[1, 1, 0], 12);

        // Half a pixel in from the left edge the source runs out; clamping holds the value at one,
        // while smoothing mixes the fill in.
        Assert.Equal(1.0, sharp[1, 0, 0], 12);
        Assert.Equal(0.625, smooth[1, 0, 0], 12);
    }

    [Fact]
    public void FollowOutput_SizesTheFrameToTheWholeTransformedImage()
    {
        var input = new SpatialRef(4, 6);
        SpatialRef shifted = Warping.FollowOutput(input, GeometricTransform.Translation(10, -2));

        Assert.Equal(4, shifted.Rows);
        Assert.Equal(6, shifted.Cols);
        Assert.Equal(10.5, shifted.XWorldMin, 10);
        Assert.Equal(16.5, shifted.XWorldMax, 10);
        Assert.Equal(-1.5, shifted.YWorldMin, 10);

        // Doubling the picture doubles the frame, because the pixel size stays the input's.
        var scale = new GeometricTransform(new double[,] { { 2, 0, 0 }, { 0, 2, 0 }, { 0, 0, 1 } });
        SpatialRef doubled = Warping.FollowOutput(input, scale);
        Assert.Equal(8, doubled.Rows);
        Assert.Equal(12, doubled.Cols);
    }

    [Fact]
    public void CenterOutput_KeepsTheInputSizeAroundTheTransformedCentre()
    {
        var input = new SpatialRef(4, 6);
        SpatialRef centred = Warping.CenterOutput(input, GeometricTransform.Translation(10, 0));

        Assert.Equal(4, centred.Rows);
        Assert.Equal(6, centred.Cols);
        Assert.Equal(3.5 + 10 - 3.0, centred.XWorldMin, 10); // centre 3.5 → 13.5, half-width 3
        Assert.Equal(16.5, centred.XWorldMax, 10);
        Assert.Equal(0.5, centred.YWorldMin, 10);
    }

    [Fact]
    public void SpatialRef_ConvertsBetweenIntrinsicAndWorld()
    {
        var frame = new SpatialRef(4, 6, xMin: 0, xMax: 12, yMin: 100, yMax: 108);
        Assert.Equal(2.0, frame.PixelExtentX, 12);
        Assert.Equal(2.0, frame.PixelExtentY, 12);

        Assert.Equal(1.0, frame.XToWorld(1), 12);   // the first column's centre
        Assert.Equal(101.0, frame.YToWorld(1), 12);
        Assert.Equal(1.0, frame.XToIntrinsic(1.0), 12);
        Assert.Equal(4.0, frame.YToIntrinsic(107.0), 12);
    }
}
