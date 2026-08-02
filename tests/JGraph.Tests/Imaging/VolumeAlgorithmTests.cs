using JGraph.Imaging;
using Xunit;

namespace JGraph.Tests.Imaging;

/// <summary>
/// M46 wave K: the volume algorithms against hand-computed answers on fixtures small enough to check
/// by eye, and against the properties that justify the functions existing at all.
/// </summary>
public sealed class VolumeAlgorithmTests
{
    /// <summary>A volume whose sample value is its own linear index, so any resampling is traceable.</summary>
    private static Volume Ramp(int height, int width, int depth)
    {
        var volume = new Volume(height, width, depth);
        Span<double> samples = volume.Samples;
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = i;
        }

        return volume;
    }

    private static Volume Constant(int height, int width, int depth, double value)
    {
        var volume = new Volume(height, width, depth);
        volume.Samples.Fill(value);
        return volume;
    }

    /// <summary>A solid box of ones inside a volume of zeros.</summary>
    private static Volume Box(int side, int start, int size)
    {
        var volume = new Volume(side, side, side);
        for (int p = start; p < start + size; p++)
        {
            for (int c = start; c < start + size; c++)
            {
                for (int r = start; r < start + size; r++)
                {
                    volume[r, c, p] = 1;
                }
            }
        }

        return volume;
    }

    [Fact]
    public void Layout_IsColumnMajor_LikeTheArraysAVolumeArrivesAs()
    {
        using Volume volume = Ramp(2, 3, 4);

        // Stepping one row moves one sample; one column moves Height; one plane moves Height*Width.
        Assert.Equal(1, volume[1, 0, 0]);
        Assert.Equal(2, volume[0, 1, 0]);
        Assert.Equal(6, volume[0, 0, 1]);
        Assert.Equal(23, volume[1, 2, 3]);
    }

    [Fact]
    public void SymmetricBoundary_FoldsRepeatedly_ForAPadWiderThanTheVolume()
    {
        using Volume volume = Ramp(3, 1, 1);

        // Mirror on the sample: -1 reads row 0, -4 has bounced twice and reads row 2.
        Assert.Equal(0, volume.At(-1, 0, 0, Filters.Boundary.Symmetric));
        Assert.Equal(2, volume.At(-4, 0, 0, Filters.Boundary.Symmetric));
        Assert.Equal(2, volume.At(3, 0, 0, Filters.Boundary.Symmetric));
    }

    [Fact]
    public void Median_LeavesAConstantVolumeAlone_AndRemovesASingleSpike()
    {
        using Volume volume = Constant(5, 5, 5, 0.4);
        volume[2, 2, 2] = 1.0;

        using Volume filtered = VolumeFilters.Median(volume);

        Assert.Equal(0.4, filtered[2, 2, 2], 12);
        Assert.Equal(0.4, filtered[0, 0, 0], 12);
    }

    [Fact]
    public void GaussianBlur_LeavesAConstantVolumeAlone_BecauseTheKernelSumsToOne()
    {
        using Volume volume = Constant(8, 8, 8, 0.375);

        using Volume blurred = VolumeFilters.GaussianBlur(volume, (1.0, 1.0, 1.0));

        // Every sample, not just the mean: a replicated border means the edges see the same constant
        // the interior does, so a normalized kernel cannot move any of them.
        foreach (double sample in blurred.Samples)
        {
            Assert.Equal(0.375, sample, 12);
        }

        Assert.Equal(0.375, Mean(blurred), 12);
    }

    [Fact]
    public void GaussianBlur_ReachesThroughThePlanes_NotJustWithinThem()
    {
        using var volume = new Volume(5, 5, 5);
        volume[2, 2, 2] = 1;

        using Volume blurred = VolumeFilters.GaussianBlur(volume, (1.0, 1.0, 1.0));

        // The neighbouring plane picks up weight, which is the whole difference between this and
        // filtering each slice on its own.
        Assert.True(blurred[2, 2, 1] > 0);
        Assert.True(blurred[2, 2, 3] > 0);
        Assert.Equal(blurred[2, 2, 1], blurred[2, 2, 3], 12);
    }

    [Fact]
    public void BoxMean_OverAConstantVolume_IsThatConstant()
    {
        using Volume volume = Constant(6, 6, 6, 0.25);

        using Volume filtered = VolumeFilters.BoxMean(volume, (3, 3, 3));

        Assert.Equal(0.25, filtered[3, 3, 3], 12);
        Assert.Equal(0.25, filtered[0, 0, 0], 12);
    }

    [Fact]
    public void BoxMean_RefusesAnEvenWindow_BecauseItHasNoCentre()
    {
        using Volume volume = Constant(4, 4, 4, 1);

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => VolumeFilters.BoxMean(volume, (2, 3, 3)));

        Assert.Contains("odd", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IntegralVolume_HoldsTheSumOfEveryCornerBox()
    {
        using Volume volume = Constant(3, 4, 5, 2);

        using Volume integral = VolumeFilters.Integral(volume);

        Assert.Equal(4, integral.Height);
        Assert.Equal(5, integral.Width);
        Assert.Equal(6, integral.Depth);
        Assert.Equal(0, integral[0, 0, 0]);
        Assert.Equal(2 * 3 * 4 * 5, integral[3, 4, 5], 9);
    }

    [Fact]
    public void IntegralBoxFilter_AgreesWithTheDirectBoxMean_OnTheInterior()
    {
        using Volume volume = Ramp(7, 7, 7);
        using Volume integral = VolumeFilters.Integral(volume);

        using Volume viaIntegral = VolumeFilters.IntegralBoxFilter(integral, (3, 3, 3));
        using Volume direct = VolumeFilters.BoxMean(volume, (3, 3, 3));

        // The integral form only covers positions where the window fits, so its (0,0,0) is the
        // direct form's (1,1,1).
        Assert.Equal(5, viaIntegral.Height);
        for (int p = 0; p < 5; p++)
        {
            for (int c = 0; c < 5; c++)
            {
                for (int r = 0; r < 5; r++)
                {
                    Assert.Equal(direct[r + 1, c + 1, p + 1], viaIntegral[r, c, p], 9);
                }
            }
        }
    }

    [Fact]
    public void LaplacianKernel_SumsToZero_SoAFlatFieldAnswersZero()
    {
        using Volume kernel = VolumeFilters.Laplacian(0.5, 0.25);

        double total = 0;
        foreach (double weight in kernel.Samples)
        {
            total += weight;
        }

        Assert.Equal(0, total, 12);
    }

    [Fact]
    public void LaplacianOfGaussianKernel_SumsToZero_AfterTheSamplingCorrection()
    {
        using Volume kernel = VolumeFilters.LaplacianOfGaussian((5, 5, 5), 1.0);

        double total = 0;
        foreach (double weight in kernel.Samples)
        {
            total += weight;
        }

        Assert.Equal(0, total, 12);
    }

    [Fact]
    public void EllipsoidKernel_IsNormalized_AndFollowsItsSemiAxes()
    {
        using Volume kernel = VolumeFilters.Ellipsoid((3, 3, 1));

        double total = 0;
        foreach (double weight in kernel.Samples)
        {
            total += weight;
        }

        Assert.Equal(1, total, 12);
        Assert.Equal(7, kernel.Height);
        Assert.Equal(7, kernel.Width);
        Assert.Equal(3, kernel.Depth);
    }

    [Fact]
    public void GradientXYZ_PointsAlongTheDirectionTheSamplesGrow()
    {
        // A ramp that only varies with the plane index: the z gradient carries all of it.
        using var volume = new Volume(4, 4, 4);
        for (int p = 0; p < 4; p++)
        {
            for (int c = 0; c < 4; c++)
            {
                for (int r = 0; r < 4; r++)
                {
                    volume[r, c, p] = p;
                }
            }
        }

        (Volume gx, Volume gy, Volume gz) = VolumeFilters.GradientXYZ(volume);
        using (gx)
        using (gy)
        using (gz)
        {
            Assert.Equal(0, gx[1, 1, 1], 12);
            Assert.Equal(0, gy[1, 1, 1], 12);
            Assert.True(gz[1, 1, 1] > 0);
        }
    }

    [Fact]
    public void Gradient_ReportsElevationOfNinetyDegrees_WhenTheChangeIsAllThroughTheStack()
    {
        using var volume = new Volume(4, 4, 4);
        for (int p = 0; p < 4; p++)
        {
            for (int c = 0; c < 4; c++)
            {
                for (int r = 0; r < 4; r++)
                {
                    volume[r, c, p] = p;
                }
            }
        }

        (Volume magnitude, Volume azimuth, Volume elevation) = VolumeFilters.Gradient(volume);
        using (magnitude)
        using (azimuth)
        using (elevation)
        {
            Assert.True(magnitude[1, 1, 1] > 0);
            Assert.Equal(90, elevation[1, 1, 1], 9);
        }
    }

    [Fact]
    public void Adjust_ClipsToTheInputWindowAndStretchesOntoTheOutputOne()
    {
        using var volume = new Volume(2, 2, 1);
        volume[0, 0, 0] = 0.0;
        volume[1, 0, 0] = 0.25;
        volume[0, 1, 0] = 0.5;
        volume[1, 1, 0] = 1.0;

        using Volume adjusted = VolumeFilters.Adjust(volume, 0.25, 0.5, 0, 1, 1);

        Assert.Equal(0, adjusted[0, 0, 0], 12);
        Assert.Equal(0, adjusted[1, 0, 0], 12);
        Assert.Equal(1, adjusted[0, 1, 0], 12);
        Assert.Equal(1, adjusted[1, 1, 0], 12);
    }

    [Fact]
    public void MatchHistogram_MovesAVolumeTowardsTheReferencesLevel()
    {
        using Volume dim = Constant(4, 4, 4, 0.2);
        using Volume bright = Constant(4, 4, 4, 0.8);

        using Volume matched = VolumeFilters.MatchHistogram(dim, bright);

        Assert.Equal(0.8, matched[2, 2, 2], 1);
    }

    [Fact]
    public void Edge_FindsTheFaceOfABox_AndLeavesItsInteriorAlone()
    {
        using Volume box = Box(12, 3, 6);

        using Volume edges = VolumeFilters.Edge(box, VolumeFilters.EdgeMethod.Sobel, (0.2, 0.4));

        // The centre of the box is uniform, so no gradient; a voxel on the face has one.
        Assert.Equal(0, edges[6, 6, 6], 12);
        Assert.Equal(1, edges[3, 6, 6], 12);
    }

    [Fact]
    public void Resize_ByAFactorAndBack_LandsOnTheGridItStartedFrom()
    {
        using Volume volume = Ramp(8, 8, 8);

        using Volume bigger = VolumeGeometry.Resize(volume, (16, 16, 16));
        using Volume back = VolumeGeometry.Resize(bigger, (8, 8, 8));

        // The half-sample-centre mapping is what makes this true; an off-by-half convention would
        // drift a little further on every round trip.
        Assert.Equal(volume[4, 4, 4], back[4, 4, 4], 6);
    }

    [Fact]
    public void Resize_WithNearestNeighbour_InventsNoNewValues()
    {
        using Volume labels = Box(8, 2, 4);

        using Volume smaller = VolumeGeometry.Resize(
            labels, (4, 4, 4), VolumeGeometry.Interpolation.Nearest);

        foreach (double sample in smaller.Samples)
        {
            Assert.True(sample is 0 or 1);
        }
    }

    [Fact]
    public void Rotate_ByThreeSixtyDegrees_ReturnsTheVolumeUnchanged()
    {
        using Volume volume = Ramp(5, 5, 5);

        using Volume turned = VolumeGeometry.Rotate(volume, 360, (0, 0, 1), loose: false);

        Assert.Equal(volume[2, 2, 2], turned[2, 2, 2], 9);
        Assert.Equal(volume[1, 3, 4], turned[1, 3, 4], 9);
    }

    [Fact]
    public void Rotate_AboutThePlaneAxis_TurnsEachSliceTheSameWayImrotateDoes()
    {
        using var volume = new Volume(5, 5, 1);
        volume[2, 3, 0] = 1;

        using Volume turned = VolumeGeometry.Rotate(
            volume, 90, (0, 0, 1), VolumeGeometry.Interpolation.Nearest, loose: false);

        // A quarter turn counter-clockwise takes the sample one right of centre to one above it.
        Assert.Equal(1, turned[1, 2, 0], 9);
    }

    [Fact]
    public void Rotate_Loose_GrowsTheOutputToHoldTheCorners()
    {
        using Volume volume = Constant(10, 10, 1, 1);

        using Volume turned = VolumeGeometry.Rotate(volume, 45, (0, 0, 1));

        Assert.True(turned.Height > 10);
        Assert.True(turned.Width > 10);
        Assert.Equal(1, turned.Depth);
    }

    [Fact]
    public void Crop_TakesTheBoxItWasGiven_AndClampsOneThatHangsOver()
    {
        using Volume volume = Ramp(6, 6, 6);

        using Volume inside = VolumeGeometry.Crop(volume, (1, 1, 1), (2, 2, 2));
        using Volume overhanging = VolumeGeometry.Crop(volume, (4, 4, 4), (10, 10, 10));

        Assert.Equal(2, inside.Height);
        Assert.Equal(volume[1, 1, 1], inside[0, 0, 0]);
        Assert.Equal(2, overhanging.Height);
        Assert.Equal(2, overhanging.Depth);
    }

    [Fact]
    public void ObliqueSlice_ThroughAnAxisAlignedPlane_ReadsThatPlane()
    {
        using Volume volume = Ramp(6, 6, 6);

        (ImageBuffer slice, double[,] _, double[,] _, double[,] zs) = VolumeGeometry.ObliqueSlice(
            volume, (2, 2, 3), (0, 0, 1));

        using (slice)
        {
            // The normal points along the plane axis, so every sample comes from plane 3.
            Assert.Equal(3, zs[slice.Height / 2, slice.Width / 2], 9);
            Assert.Equal(6, slice.Height);
            Assert.Equal(6, slice.Width);
        }
    }

    [Fact]
    public void ObliqueSlice_FullOutput_IsLargeEnoughForAnyOrientation()
    {
        using Volume volume = Ramp(6, 6, 6);

        (ImageBuffer slice, _, _, _) = VolumeGeometry.ObliqueSlice(
            volume, (2.5, 2.5, 2.5), (1, 1, 1), full: true);

        using (slice)
        {
            Assert.True(slice.Height >= 6);
            Assert.True(slice.Width >= 6);
        }
    }

    [Fact]
    public void Label_SeparatesTwoCubes_UnderEveryConnectivity()
    {
        using var volume = new Volume(7, 7, 7);
        volume[1, 1, 1] = 1;
        volume[5, 5, 5] = 1;

        foreach (int connectivity in new[] { 6, 18, 26 })
        {
            (_, int count) = VolumeRegions.Label(volume, connectivity);
            Assert.Equal(2, count);
        }
    }

    [Fact]
    public void Label_CountsACornerTouchAsOneObjectOrTwo_DependingOnConnectivity()
    {
        using var volume = new Volume(4, 4, 4);
        volume[1, 1, 1] = 1;
        volume[2, 2, 2] = 1;

        (_, int sixCount) = VolumeRegions.Label(volume, 6);
        (_, int twentySixCount) = VolumeRegions.Label(volume, 26);

        // The two voxels share only a corner, which is the entire content of the choice.
        Assert.Equal(2, sixCount);
        Assert.Equal(1, twentySixCount);
    }

    [Fact]
    public void AreaOpen_RemovesTheSmallRegionAndKeepsTheLarge()
    {
        using var volume = new Volume(10, 10, 10);
        volume[1, 1, 1] = 1;
        for (int p = 5; p < 8; p++)
        {
            for (int c = 5; c < 8; c++)
            {
                for (int r = 5; r < 8; r++)
                {
                    volume[r, c, p] = 1;
                }
            }
        }

        using Volume opened = VolumeRegions.AreaOpen(volume, 10);

        Assert.Equal(0, opened[1, 1, 1]);
        Assert.Equal(1, opened[6, 6, 6]);
    }

    [Fact]
    public void Select_KeepsOnlyTheRegionASeedLandsIn()
    {
        using var volume = new Volume(8, 8, 8);
        volume[1, 1, 1] = 1;
        volume[6, 6, 6] = 1;

        using Volume selected = VolumeRegions.Select(volume, [(6, 6, 6)]);

        Assert.Equal(0, selected[1, 1, 1]);
        Assert.Equal(1, selected[6, 6, 6]);
    }

    [Fact]
    public void Morph_Remove_LeavesTheSurfaceOfASolidBox()
    {
        using Volume box = Box(9, 2, 5);

        using Volume shell = VolumeRegions.Morph(box, VolumeRegions.MorphOperation.Remove);

        Assert.Equal(0, shell[4, 4, 4]);
        Assert.Equal(1, shell[2, 4, 4]);
    }

    [Fact]
    public void Morph_Clean_DropsAnIsolatedVoxelAndKeepsAPair()
    {
        using var volume = new Volume(6, 6, 6);
        volume[1, 1, 1] = 1;
        volume[4, 4, 4] = 1;
        volume[4, 4, 5] = 1;

        using Volume cleaned = VolumeRegions.Morph(volume, VolumeRegions.MorphOperation.Clean);

        Assert.Equal(0, cleaned[1, 1, 1]);
        Assert.Equal(1, cleaned[4, 4, 4]);
    }

    [Fact]
    public void Morph_Fill_ClosesAOneVoxelHole()
    {
        using Volume box = Box(9, 2, 5);
        box[4, 4, 4] = 0;

        using Volume filled = VolumeRegions.Morph(box, VolumeRegions.MorphOperation.Fill);

        Assert.Equal(1, filled[4, 4, 4]);
    }

    [Fact]
    public void Measure_ReportsTheVolumeCentroidAndBoxOfASolidCube()
    {
        using Volume box = Box(10, 2, 4);
        (int[] labels, int count) = VolumeRegions.Label(box);

        VolumeMeasurement[] measured = VolumeRegions.Measure(labels, count, (10, 10, 10));

        Assert.Single(measured);
        VolumeMeasurement region = measured[0];
        Assert.Equal(64, region.Volume);
        Assert.Equal(3.5, region.Centroid.X, 9);
        Assert.Equal(3.5, region.Centroid.Y, 9);
        Assert.Equal(3.5, region.Centroid.Z, 9);
        Assert.Equal(1.5, region.BoundingBox.X, 9);
        Assert.Equal(4, region.BoundingBox.Width, 9);
        Assert.Equal(1, region.Extent, 9);
    }

    [Fact]
    public void Measure_CountsTheOutwardFacesOfACubeAsItsSurfaceArea()
    {
        using Volume box = Box(10, 2, 4);
        (int[] labels, int count) = VolumeRegions.Label(box);

        VolumeMeasurement[] measured = VolumeRegions.Measure(labels, count, (10, 10, 10));

        // Six faces of a 4x4 cube.
        Assert.Equal(6 * 4 * 4, measured[0].SurfaceArea, 9);
    }

    [Fact]
    public void Measure_GivesALongRegionOneLongPrincipalAxis()
    {
        using var volume = new Volume(12, 12, 12);
        for (int c = 2; c < 10; c++)
        {
            volume[5, c, 5] = 1;
        }

        (int[] labels, int count) = VolumeRegions.Label(volume);
        VolumeMeasurement[] measured = VolumeRegions.Measure(labels, count, (12, 12, 12));

        double[] axes = measured[0].PrincipalAxisLength;
        Assert.True(axes[0] > 3 * axes[1]);
        Assert.Equal(axes[1], axes[2], 6);
    }

    [Fact]
    public void KMeans_SeparatesTwoLevels_AtTheirOwnMeans()
    {
        using var volume = new Volume(4, 4, 4);
        Span<double> samples = volume.Samples;
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = i < samples.Length / 2 ? 0.1 : 0.9;
        }

        (int[] labels, double[] centers) = VolumeRegions.KMeans(volume, 2);

        Assert.Equal(2, centers.Length);
        Assert.Equal(0.1, Math.Min(centers[0], centers[1]), 9);
        Assert.Equal(0.9, Math.Max(centers[0], centers[1]), 9);
        Assert.NotEqual(labels[0], labels[^1]);
    }

    [Fact]
    public void Superpixels_NumbersItsSupervoxelsContiguously()
    {
        using Volume volume = Ramp(8, 8, 8);

        (int[] labels, int count) = VolumeRegions.Superpixels(volume, 8);

        Assert.True(count >= 1);
        var seen = new HashSet<int>(labels);
        Assert.Equal(count, seen.Count);
        Assert.Equal(count, seen.Max());
        Assert.DoesNotContain(0, seen);
    }

    [Fact]
    public void StructuralSimilarity_OfAVolumeAgainstItself_IsExactlyOne()
    {
        using Volume volume = Ramp(8, 8, 8);
        Normalize(volume);

        (double score, Volume map) = QualityMetrics.StructuralSimilarity(volume, volume);

        using (map)
        {
            Assert.Equal(1.0, score, 12);
            foreach (double sample in map.Samples)
            {
                Assert.Equal(1.0, sample, 12);
            }
        }
    }

    [Fact]
    public void MultiScaleSimilarity_OfAVolumeAgainstItself_IsOneAtEveryScale()
    {
        using Volume volume = Ramp(16, 16, 16);
        Normalize(volume);

        (double score, Volume[] maps) = QualityMetrics.MultiScaleSimilarity(volume, volume, 3);

        try
        {
            Assert.Equal(1.0, score, 9);
            Assert.Equal(3, maps.Length);
            Assert.Equal(16, maps[0].Height);
            Assert.Equal(8, maps[1].Height);
            Assert.Equal(4, maps[2].Height);
        }
        finally
        {
            foreach (Volume map in maps)
            {
                map.Dispose();
            }
        }
    }

    [Fact]
    public void MultiScaleSimilarity_RefusesAVolumeTooSmallForTheScalesAsked()
    {
        using Volume volume = Ramp(8, 8, 8);

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => QualityMetrics.MultiScaleSimilarity(volume, volume, 5));

        Assert.Contains("fewer scales", error.Message);
    }

    [Fact]
    public void Pad_ExtendsEveryAxis_AndReplicatesTheEdgeWhenAsked()
    {
        using Volume volume = Ramp(3, 3, 3);

        using Volume padded = VolumeFilters.Pad(
            volume, (1, 1, 1), (1, 1, 1), Filters.Boundary.Replicate);

        Assert.Equal(5, padded.Height);
        Assert.Equal(5, padded.Width);
        Assert.Equal(5, padded.Depth);
        Assert.Equal(volume[0, 0, 0], padded[0, 0, 0]);
        Assert.Equal(volume[2, 2, 2], padded[4, 4, 4]);
    }

    private static double Mean(Volume volume)
    {
        double total = 0;
        foreach (double sample in volume.Samples)
        {
            total += sample;
        }

        return total / volume.SampleCount;
    }

    private static void Normalize(Volume volume)
    {
        Span<double> samples = volume.Samples;
        double top = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            top = Math.Max(top, samples[i]);
        }

        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] /= top;
        }
    }
}
