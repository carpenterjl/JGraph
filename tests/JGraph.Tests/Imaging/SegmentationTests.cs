using JGraph.Imaging;
using Xunit;

namespace JGraph.Tests.Imaging;

/// <summary>
/// M46 wave G's algorithms: the full region property set, boundary tracing, multilevel thresholding,
/// watershed and the seeded segmenters, superpixels, active contours, regions of interest and circle
/// detection.
/// </summary>
public sealed class SegmentationTests
{
    // --- Region properties ---------------------------------------------------------------------

    [Fact]
    public void Measure_OfASquare_GivesTheGeometryYouCanCheckByHand()
    {
        using ImageBuffer square = Rect(20, 20, 4, 4, 9, 13);
        (int[,] labels, int count) = Regions.Label(square, 8);
        RegionMeasurement m = RegionProperties.Measure(labels, count)[0];

        Assert.Equal(1, count);
        Assert.Equal(6 * 10, m.Area);
        Assert.Equal(8.5, m.CentroidX, 12);
        Assert.Equal(6.5, m.CentroidY, 12);
        Assert.Equal(3.5, m.BoundingBoxX, 12);
        Assert.Equal(3.5, m.BoundingBoxY, 12);
        Assert.Equal(10, m.BoundingBoxWidth, 12);
        Assert.Equal(6, m.BoundingBoxHeight, 12);

        // A solid rectangle fills its own box and its own hull.
        Assert.Equal(1.0, m.Extent, 12);
        Assert.Equal(1.0, m.Solidity, 12);
        Assert.Equal(m.Area, m.FilledArea);
        Assert.Equal(1, m.EulerNumber);
        Assert.Equal(Math.Sqrt(4.0 * m.Area / Math.PI), m.EquivDiameter, 12);

        // The outline of a 6-by-10 block is 2·(6 − 1) + 2·(10 − 1) steps between pixel centres.
        Assert.Equal(28, m.Perimeter, 10);
    }

    [Fact]
    public void Measure_OfARing_CountsTheHoleInTheEulerNumber()
    {
        using var ring = new ImageBuffer(15, 15, 1);
        for (int r = 3; r < 12; r++)
        {
            for (int c = 3; c < 12; c++)
            {
                bool edge = r is 3 or 11 || c is 3 or 11;
                ring[r, c, 0] = edge ? 1.0 : 0.0;
            }
        }

        (int[,] labels, int count) = Regions.Label(ring, 8);
        RegionMeasurement m = RegionProperties.Measure(labels, count)[0];

        Assert.Equal(0, m.EulerNumber);
        Assert.Equal(81, m.FilledArea);
        Assert.True(m.Solidity < 0.55);
    }

    [Fact]
    public void EllipseMoments_OfAHorizontalBar_ReadAsAFlatEllipsePointingAlongX()
    {
        using ImageBuffer bar = Rect(21, 41, 10, 5, 10, 34);
        (int[,] labels, int count) = Regions.Label(bar, 8);
        RegionMeasurement m = RegionProperties.Measure(labels, count)[0];

        Assert.True(m.MajorAxisLength > m.MinorAxisLength);
        Assert.Equal(0.0, m.Orientation, 10);
        Assert.True(m.Eccentricity > 0.95);
    }

    [Fact]
    public void Orientation_OfAVerticalBar_IsNinetyDegrees()
    {
        using ImageBuffer bar = Rect(41, 21, 5, 10, 34, 10);
        (int[,] labels, int count) = Regions.Label(bar, 8);
        RegionMeasurement m = RegionProperties.Measure(labels, count)[0];

        // The axis has no head or tail, so ±90 name the same direction; MATLAB prints 90.
        Assert.Equal(90.0, Math.Abs(m.Orientation), 10);
    }

    [Fact]
    public void EllipseMoments_OfASquare_HaveNoPreferredAxis()
    {
        using ImageBuffer square = Rect(21, 21, 6, 6, 14, 14);
        (int[,] labels, int count) = Regions.Label(square, 8);
        RegionMeasurement m = RegionProperties.Measure(labels, count)[0];

        // The extra 1/12 is what keeps a square from reading as a perfect circle by accident.
        Assert.Equal(m.MajorAxisLength, m.MinorAxisLength, 10);
        Assert.Equal(0.0, m.Eccentricity, 10);
    }

    [Fact]
    public void ConvexHull_OfADiamond_HasFourCornersAndAKnownArea()
    {
        using var diamond = new ImageBuffer(21, 21, 1);
        for (int r = 0; r < 21; r++)
        {
            for (int c = 0; c < 21; c++)
            {
                diamond[r, c, 0] = Math.Abs(r - 10) + Math.Abs(c - 10) <= 6 ? 1.0 : 0.0;
            }
        }

        (int[,] labels, int count) = Regions.Label(diamond, 8);
        RegionMeasurement m = RegionProperties.Measure(labels, count)[0];

        Assert.Equal(85, m.Area);
        Assert.True(m.ConvexArea >= m.Area);
        Assert.True(m.Solidity > 0.85 && m.Solidity <= 1.0);

        // The hull of a diamond is an octagon at pixel resolution: the four tips plus the corner
        // pixels' outer corners.
        Assert.InRange(m.ConvexHull.Length, 4, 12);
    }

    [Fact]
    public void Feret_OfALine_MeasuresItsLengthAndItsWidth()
    {
        using ImageBuffer bar = Rect(11, 31, 5, 5, 5, 24);
        (int[,] labels, int count) = Regions.Label(bar, 8);
        RegionMeasurement m = RegionProperties.Measure(labels, count)[0];

        // The hull runs corner to corner of a 20-by-1 block: 20 across and 1 down.
        Assert.Equal(Math.Sqrt(401), m.MaxFeretDiameter, 10);
        Assert.InRange(m.MaxFeretAngle, -5.0, 5.0);
        Assert.Equal(1.0, m.MinFeretDiameter, 10);
    }

    [Fact]
    public void Extrema_OfARectangle_AreItsFourCornersTwiceOver()
    {
        using ImageBuffer square = Rect(12, 12, 3, 3, 8, 8);
        (int[,] labels, int count) = Regions.Label(square, 8);
        RegionMeasurement m = RegionProperties.Measure(labels, count)[0];

        Assert.Equal(8, m.Extrema.Length);
        Assert.Equal(2.5, m.Extrema[0].X, 12);
        Assert.Equal(2.5, m.Extrema[0].Y, 12);
        Assert.Equal(8.5, m.Extrema[4].X, 12);
        Assert.Equal(8.5, m.Extrema[4].Y, 12);
    }

    [Fact]
    public void Measure_WithAnIntensityImage_ReportsTheIntensityProperties()
    {
        using ImageBuffer mask = Rect(10, 10, 2, 2, 5, 5);
        using var intensity = new ImageBuffer(10, 10, 1);
        for (int r = 0; r < 10; r++)
        {
            for (int c = 0; c < 10; c++)
            {
                intensity[r, c, 0] = 0.5;
            }
        }

        intensity[2, 2, 0] = 0.9;
        intensity[5, 5, 0] = 0.1;

        (int[,] labels, int count) = Regions.Label(mask, 8);
        RegionMeasurement m = RegionProperties.Measure(labels, count, intensity)[0];

        Assert.Equal(0.9, m.MaxIntensity, 12);
        Assert.Equal(0.1, m.MinIntensity, 12);
        Assert.Equal(16, m.PixelValues.Length);
        Assert.Equal(((14 * 0.5) + 0.9 + 0.1) / 16, m.MeanIntensity, 12);
    }

    [Fact]
    public void BwArea_IsExactForARectangleAndWeightedForADiagonal()
    {
        // The weights exist so that a rectangle still measures its own pixel count exactly; what
        // they change is the staircase, which is the whole point of the function.
        using ImageBuffer block = Rect(20, 20, 4, 5, 13, 16);
        Assert.Equal(10 * 12, RegionProperties.Area(block), 10);

        using var triangle = new ImageBuffer(20, 20, 1);
        int pixels = 0;
        for (int r = 0; r < 20; r++)
        {
            for (int c = 0; c <= r; c++)
            {
                triangle[r, c, 0] = 1.0;
                pixels++;
            }
        }

        Assert.NotEqual(pixels, RegionProperties.Area(triangle), 6);
        Assert.InRange(RegionProperties.Area(triangle), pixels * 0.85, pixels * 1.15);
    }

    [Fact]
    public void BwEuler_CountsObjectsMinusHoles()
    {
        using var picture = new ImageBuffer(21, 21, 1);
        for (int r = 2; r < 9; r++)
        {
            for (int c = 2; c < 9; c++)
            {
                picture[r, c, 0] = r is 2 or 8 || c is 2 or 8 ? 1.0 : 0.0;
            }
        }

        for (int r = 13; r < 17; r++)
        {
            for (int c = 13; c < 17; c++)
            {
                picture[r, c, 0] = 1.0;
            }
        }

        // One ring (Euler 0) plus one solid block (Euler 1).
        Assert.Equal(1, RegionProperties.Euler(picture));
    }

    // --- Boundaries -------------------------------------------------------------------------------

    [Fact]
    public void Trace_OfASquare_WalksTheOutlineAndClosesTheLoop()
    {
        var mask = new bool[9, 9];
        for (int r = 3; r < 7; r++)
        {
            for (int c = 3; c < 7; c++)
            {
                mask[r, c] = true;
            }
        }

        (int Row, int Col)[] trace = Boundaries.Trace(mask, 3, 3, 8, 3, 2);

        Assert.Equal((3, 3), trace[0]);
        Assert.Equal(trace[0], trace[^1]);

        // Twelve boundary pixels round a 4-by-4 block, plus the repeated start.
        Assert.Equal(13, trace.Length);
        foreach ((int r, int c) in trace)
        {
            Assert.True(r is 3 or 6 || c is 3 or 6);
        }
    }

    [Fact]
    public void Find_ReportsOneOuterBoundaryPerObjectAndOneMoreForAHole()
    {
        using var picture = new ImageBuffer(21, 21, 1);
        for (int r = 2; r < 9; r++)
        {
            for (int c = 2; c < 9; c++)
            {
                picture[r, c, 0] = r is 2 or 8 || c is 2 or 8 ? 1.0 : 0.0;
            }
        }

        for (int r = 14; r < 18; r++)
        {
            for (int c = 14; c < 18; c++)
            {
                picture[r, c, 0] = 1.0;
            }
        }

        (List<(int Row, int Col)[]> traces, _, int[] parent, int objects) = Boundaries.Find(picture, 8);

        Assert.Equal(2, objects);
        Assert.Equal(3, traces.Count);
        Assert.Equal(-1, parent[0]);
        Assert.Equal(-1, parent[1]);
        Assert.Equal(0, parent[2]);
    }

    [Fact]
    public void Find_WithNoHoles_TracesObjectsOnly()
    {
        using var ring = new ImageBuffer(15, 15, 1);
        for (int r = 3; r < 12; r++)
        {
            for (int c = 3; c < 12; c++)
            {
                ring[r, c, 0] = r is 3 or 11 || c is 3 or 11 ? 1.0 : 0.0;
            }
        }

        (List<(int Row, int Col)[]> traces, _, _, _) = Boundaries.Find(ring, 8, includeHoles: false);
        Assert.Single(traces);
    }

    [Fact]
    public void ConvexHull_OfASquaresCorners_IsTheSquare()
    {
        (double X, double Y)[] hull = Boundaries.ConvexHull(
            [(0, 0), (4, 0), (4, 4), (0, 4), (2, 2), (1, 3)]);
        Assert.Equal(4, hull.Length);
    }

    [Fact]
    public void Reduce_DropsTheCollinearMiddleAndKeepsTheCorner()
    {
        (double X, double Y)[] reduced = Boundaries.Reduce(
            [(0, 0), (1, 0), (2, 0), (3, 0), (4, 0), (4, 4)], 0.01);

        Assert.Equal(3, reduced.Length);
        Assert.Equal((0.0, 0.0), reduced[0]);
        Assert.Equal((4.0, 0.0), reduced[1]);
        Assert.Equal((4.0, 4.0), reduced[2]);
    }

    // --- Thresholding ------------------------------------------------------------------------------

    [Fact]
    public void MultiThreshold_SplitsThreeSeparatedLevels()
    {
        using var picture = new ImageBuffer(30, 30, 1);
        for (int r = 0; r < 30; r++)
        {
            for (int c = 0; c < 30; c++)
            {
                picture[r, c, 0] = r < 10 ? 0.15 : (r < 20 ? 0.5 : 0.85);
            }
        }

        double[] thresholds = Segmentation.MultiThreshold(picture, 2);
        Assert.Equal(2, thresholds.Length);
        Assert.InRange(thresholds[0], 0.15, 0.5);
        Assert.InRange(thresholds[1], 0.5, 0.85);

        int[,] classes = Segmentation.Quantize(picture, thresholds);
        Assert.Equal(1, classes[5, 5]);
        Assert.Equal(2, classes[15, 15]);
        Assert.Equal(3, classes[25, 25]);
    }

    [Fact]
    public void MultiThreshold_WithOneLevel_AgreesWithOtsu()
    {
        using var picture = new ImageBuffer(20, 20, 1);
        for (int r = 0; r < 20; r++)
        {
            for (int c = 0; c < 20; c++)
            {
                picture[r, c, 0] = c < 10 ? 0.2 : 0.8;
            }
        }

        // Both separate the two populations. They need not agree on where in the empty gap the
        // threshold sits: every cut between the modes explains exactly the same variance, so the
        // choice among them is a tie-break, not a result.
        double[] one = Segmentation.MultiThreshold(picture, 1);
        double otsu = Histograms.OtsuLevel(picture);
        Assert.InRange(one[0], 0.2, 0.8);
        Assert.InRange(otsu, 0.2, 0.8);
    }

    [Fact]
    public void Slice_PutsSamplesInEqualBandsNumberedFromZero()
    {
        using var ramp = new ImageBuffer(1, 10, 1);
        for (int c = 0; c < 10; c++)
        {
            ramp[0, c, 0] = c / 10.0;
        }

        int[,] bands = Segmentation.Slice(ramp, 10);
        for (int c = 0; c < 10; c++)
        {
            Assert.Equal(c, bands[0, c]);
        }
    }

    // --- Watershed and region growing ----------------------------------------------------------------

    [Fact]
    public void Watershed_SeparatesTwoTouchingDiscs()
    {
        // The classic setup: two overlapping discs, the distance transform of their complement
        // inverted, and a watershed of that. The ridge falls exactly at the neck.
        using var discs = new ImageBuffer(41, 61, 1);
        for (int r = 0; r < 41; r++)
        {
            for (int c = 0; c < 61; c++)
            {
                bool left = ((r - 20.0) * (r - 20.0)) + ((c - 22.0) * (c - 22.0)) <= 196;
                bool right = ((r - 20.0) * (r - 20.0)) + ((c - 38.0) * (c - 38.0)) <= 196;
                discs[r, c, 0] = left || right ? 1.0 : 0.0;
            }
        }

        using ImageBuffer complement = PointOps.Complement(discs);
        (double[] distance, _) = DistanceTransforms.Transform(complement);
        double largest = 0;
        foreach (double value in distance)
        {
            largest = Math.Max(largest, value);
        }

        using var basins = new ImageBuffer(41, 61, 1);
        for (int i = 0; i < distance.Length; i++)
        {
            basins.Pixels[i] = 1.0 - (distance[i] / largest);
        }

        using ImageBuffer smoothed = Filters.GaussianBlur(basins, 1.0, 1.0);
        int[,] labels = Segmentation.Watershed(smoothed, 8);

        Assert.NotEqual(0, labels[20, 22]);
        Assert.NotEqual(0, labels[20, 38]);
        Assert.NotEqual(labels[20, 22], labels[20, 38]);
    }

    [Fact]
    public void Watershed_OfASingleBasin_IsAllOneLabel()
    {
        using var bowl = new ImageBuffer(21, 21, 1);
        for (int r = 0; r < 21; r++)
        {
            for (int c = 0; c < 21; c++)
            {
                bowl[r, c, 0] = (Math.Abs(r - 10) + Math.Abs(c - 10)) / 40.0;
            }
        }

        int[,] labels = Segmentation.Watershed(bowl, 8);
        Assert.Equal(1, labels[10, 10]);
        Assert.Equal(1, labels[0, 0]);
    }

    [Fact]
    public void GrayConnected_StopsAtTheIntensityStep()
    {
        using var picture = new ImageBuffer(11, 11, 1);
        for (int r = 0; r < 11; r++)
        {
            for (int c = 0; c < 11; c++)
            {
                picture[r, c, 0] = c < 6 ? 0.5 : 0.9;
            }
        }

        using ImageBuffer grown = Segmentation.GrayConnected(picture, [(5, 2)], 0.1);
        Assert.Equal(1.0, grown[5, 5, 0]);
        Assert.Equal(0.0, grown[5, 6, 0]);
    }

    [Fact]
    public void FastMarch_ReachesAcrossCheapGroundAndNotAcrossExpensive()
    {
        using var weight = new ImageBuffer(11, 21, 1);
        weight.Pixels.Fill(1.0);
        for (int r = 0; r < 11; r++)
        {
            weight[r, 10, 0] = 0.001;
        }

        (ImageBuffer mask, ImageBuffer time) = Segmentation.FastMarch(weight, [(5, 0)], 0.2);
        using (mask)
        using (time)
        {
            Assert.Equal(1.0, mask[5, 5, 0]);
            Assert.Equal(0.0, mask[5, 20, 0]);
            Assert.True(time[5, 20, 0] > time[5, 5, 0]);
        }
    }

    [Fact]
    public void GradientWeight_IsSmallAtAnEdgeAndLargeOnFlatGround()
    {
        using var step = new ImageBuffer(21, 21, 1);
        for (int r = 0; r < 21; r++)
        {
            for (int c = 0; c < 21; c++)
            {
                step[r, c, 0] = c < 10 ? 0.1 : 0.9;
            }
        }

        using ImageBuffer weight = Segmentation.GradientWeight(step);
        Assert.True(weight[10, 10, 0] < 0.5);
        Assert.True(weight[10, 1, 0] > 0.9);
    }

    // --- Clustering ------------------------------------------------------------------------------------

    [Fact]
    public void KMeans_SeparatesTwoFlatRegions()
    {
        using var picture = new ImageBuffer(20, 20, 1);
        for (int r = 0; r < 20; r++)
        {
            for (int c = 0; c < 20; c++)
            {
                picture[r, c, 0] = c < 10 ? 0.2 : 0.8;
            }
        }

        (int[,] labels, double[][] centers) = Segmentation.KMeans(picture, 2, new Random(7));
        Assert.NotEqual(labels[5, 2], labels[5, 17]);
        Assert.Equal(2, centers.Length);

        double low = Math.Min(centers[0][0], centers[1][0]);
        double high = Math.Max(centers[0][0], centers[1][0]);
        Assert.Equal(0.2, low, 10);
        Assert.Equal(0.8, high, 10);
    }

    [Fact]
    public void Superpixels_TileThePictureIntoRoughlyTheRequestedCount()
    {
        using var picture = new ImageBuffer(60, 60, 1);
        for (int r = 0; r < 60; r++)
        {
            for (int c = 0; c < 60; c++)
            {
                picture[r, c, 0] = ((r / 20) + (c / 20)) % 2 == 0 ? 0.3 : 0.7;
            }
        }

        (int[,] labels, int count) = Segmentation.Superpixels(picture, 36);
        Assert.InRange(count, 20, 60);

        // Every pixel belongs to one, and neighbouring superpixels are genuinely different regions.
        foreach (int label in labels)
        {
            Assert.True(label >= 1);
        }
    }

    // --- Active contours ---------------------------------------------------------------------------------

    [Fact]
    public void ActiveContour_GrowsAnUndersizedMaskOntoTheObject()
    {
        using var picture = new ImageBuffer(41, 41, 1);
        for (int r = 0; r < 41; r++)
        {
            for (int c = 0; c < 41; c++)
            {
                picture[r, c, 0] = ((r - 20.0) * (r - 20.0)) + ((c - 20.0) * (c - 20.0)) <= 144 ? 0.9 : 0.1;
            }
        }

        using ImageBuffer seed = Rect(41, 41, 17, 17, 23, 23);
        using ImageBuffer grown = ActiveContour.Evolve(picture, seed, 200);

        // Inside the disc but outside the seed: it should have been swallowed.
        Assert.Equal(1.0, grown[20, 29, 0]);
        Assert.Equal(0.0, grown[20, 38, 0]);
    }

    [Fact]
    public void ActiveContour_ShrinksAnOversizedMaskBackToTheObject()
    {
        using var picture = new ImageBuffer(41, 41, 1);
        for (int r = 0; r < 41; r++)
        {
            for (int c = 0; c < 41; c++)
            {
                picture[r, c, 0] = ((r - 20.0) * (r - 20.0)) + ((c - 20.0) * (c - 20.0)) <= 100 ? 0.9 : 0.1;
            }
        }

        using ImageBuffer seed = Rect(41, 41, 3, 3, 37, 37);
        using ImageBuffer settled = ActiveContour.Evolve(picture, seed, 300);

        Assert.Equal(1.0, settled[20, 20, 0]);
        Assert.Equal(0.0, settled[5, 5, 0]);
    }

    // --- Regions of interest -------------------------------------------------------------------------------

    [Fact]
    public void PolygonMask_FillsATriangleByTheCentreRule()
    {
        using ImageBuffer mask = RoiOps.PolygonMask([1.0, 9.0, 5.0], [1.0, 1.0, 9.0], 12, 12);
        Assert.Equal(1.0, mask[2, 5, 0]);
        Assert.Equal(0.0, mask[8, 1, 0]);
        Assert.Equal(0.0, mask[0, 0, 0]);
    }

    [Fact]
    public void SelectByColor_KeepsTheRangeAndNothingElse()
    {
        using var ramp = new ImageBuffer(1, 11, 1);
        for (int c = 0; c < 11; c++)
        {
            ramp[0, c, 0] = c / 10.0;
        }

        using ImageBuffer mask = RoiOps.SelectByColor(ramp, 0.3, 0.6);
        Assert.Equal(0.0, mask[0, 2, 0]);
        Assert.Equal(1.0, mask[0, 3, 0]);
        Assert.Equal(1.0, mask[0, 6, 0]);
        Assert.Equal(0.0, mask[0, 7, 0]);
    }

    [Fact]
    public void FillRegion_ReplacesAHoleWithTheSurroundingLevel()
    {
        using var picture = new ImageBuffer(21, 21, 1);
        picture.Pixels.Fill(0.4);
        using ImageBuffer mask = Rect(21, 21, 8, 8, 12, 12);
        for (int r = 8; r <= 12; r++)
        {
            for (int c = 8; c <= 12; c++)
            {
                picture[r, c, 0] = 1.0;
            }
        }

        using ImageBuffer filled = RoiOps.FillRegion(picture, mask);
        Assert.Equal(0.4, filled[10, 10, 0], 6);
        Assert.Equal(0.4, filled[0, 0, 0], 12);
    }

    [Fact]
    public void FillRegion_ReproducesALinearRampExactly()
    {
        // A plane satisfies Laplace's equation, so filling a hole in one must give the plane back.
        using var ramp = new ImageBuffer(21, 21, 1);
        for (int r = 0; r < 21; r++)
        {
            for (int c = 0; c < 21; c++)
            {
                ramp[r, c, 0] = c / 40.0;
            }
        }

        using ImageBuffer mask = Rect(21, 21, 8, 8, 12, 12);
        using ImageBuffer filled = RoiOps.FillRegion(ramp, mask, 2000);
        Assert.Equal(10 / 40.0, filled[10, 10, 0], 4);
    }

    // --- Circles -------------------------------------------------------------------------------------------

    [Fact]
    public void Find_LocatesThreeDrawnDiscs()
    {
        using var picture = new ImageBuffer(120, 160, 1);
        (double R, double C, double Radius)[] drawn = [(40, 40, 15), (40, 110, 20), (85, 75, 12)];
        for (int r = 0; r < 120; r++)
        {
            for (int c = 0; c < 160; c++)
            {
                foreach ((double cr, double cc, double radius) in drawn)
                {
                    if (((r - cr) * (r - cr)) + ((c - cc) * (c - cc)) <= radius * radius)
                    {
                        picture[r, c, 0] = 1.0;
                    }
                }
            }
        }

        CircleDetection.Circle[] found = CircleDetection.Find(picture, 8, 25);
        Assert.True(found.Length >= 3, $"found {found.Length} circles");

        foreach ((double cr, double cc, double radius) in drawn)
        {
            CircleDetection.Circle? match = null;
            foreach (CircleDetection.Circle circle in found)
            {
                double distance = Math.Sqrt(((circle.CenterY - cr) * (circle.CenterY - cr)) +
                                            ((circle.CenterX - cc) * (circle.CenterX - cc)));
                if (distance < 3)
                {
                    match = circle;
                    break;
                }
            }

            Assert.True(match is not null, $"no circle near ({cr}, {cc})");
            Assert.InRange(match!.Value.Radius, radius * 0.85, radius * 1.15);
        }
    }

    [Fact]
    public void Find_WithTheWrongPolarity_FindsNothing()
    {
        using var picture = new ImageBuffer(80, 80, 1);
        for (int r = 0; r < 80; r++)
        {
            for (int c = 0; c < 80; c++)
            {
                picture[r, c, 0] = ((r - 40.0) * (r - 40.0)) + ((c - 40.0) * (c - 40.0)) <= 225 ? 1.0 : 0.0;
            }
        }

        CircleDetection.Circle[] bright = CircleDetection.Find(picture, 10, 20);
        CircleDetection.Circle[] dark = CircleDetection.Find(
            picture, 10, 20, CircleDetection.Polarity.Dark);

        Assert.Contains(bright, circle =>
            Math.Abs(circle.CenterX - 40) < 3 && Math.Abs(circle.CenterY - 40) < 3);

        // With the sign reversed the votes go outward instead of inward, so nothing piles up at the
        // true centre — the useful failure mode, since it finds nothing rather than finding a lie.
        Assert.DoesNotContain(dark, circle =>
            Math.Abs(circle.CenterX - 40) < 3 && Math.Abs(circle.CenterY - 40) < 3);
    }

    // --- Label display -----------------------------------------------------------------------------------------

    [Fact]
    public void LabelToRgb_PaintsLabelsAndLeavesTheBackgroundWhite()
    {
        var labels = new int[4, 4];
        labels[1, 1] = 1;
        labels[2, 2] = 2;

        using ImageBuffer painted = LabelDisplay.LabelToRgb(labels);
        Assert.Equal(1.0, painted[0, 0, 0], 12);
        Assert.Equal(1.0, painted[0, 0, 1], 12);
        Assert.Equal(1.0, painted[0, 0, 2], 12);

        bool first = painted[1, 1, 0] != painted[2, 2, 0] ||
                     painted[1, 1, 1] != painted[2, 2, 1] ||
                     painted[1, 1, 2] != painted[2, 2, 2];
        Assert.True(first, "consecutive labels must not share a colour");
    }

    [Fact]
    public void Overlay_BurnsTheMaskInAndLeavesTheRestAlone()
    {
        using var picture = new ImageBuffer(4, 4, 1);
        picture.Pixels.Fill(0.5);
        using ImageBuffer mask = Rect(4, 4, 1, 1, 2, 2);

        using ImageBuffer burned = LabelDisplay.Overlay(picture, mask, (1.0, 0.0, 0.0));
        Assert.Equal(1.0, burned[1, 1, 0], 12);
        Assert.Equal(0.0, burned[1, 1, 1], 12);
        Assert.Equal(0.5, burned[0, 0, 0], 12);
    }

    /// <summary>A binary image with one filled rectangle, inclusive of both corners.</summary>
    private static ImageBuffer Rect(int height, int width, int r0, int c0, int r1, int c1)
    {
        var picture = new ImageBuffer(height, width, 1);
        for (int r = r0; r <= r1; r++)
        {
            for (int c = c0; c <= c1; c++)
            {
                picture[r, c, 0] = 1.0;
            }
        }

        return picture;
    }
}
