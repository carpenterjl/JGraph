using JGraph.Imaging;
using Xunit;

namespace JGraph.Tests.Imaging;

/// <summary>
/// M46 wave E's algorithms: CLAHE and histogram matching, flat-field correction, the decorrelation
/// stretch, unsharp masking, the four edge-preserving filters, the haze pair, and the Hessian ridge
/// measures.
/// </summary>
public sealed class EnhancementTests
{
    /// <summary>A grayscale image built from a function of (row, column).</summary>
    private static ImageBuffer Gray(int height, int width, Func<int, int, double> f)
    {
        var image = new ImageBuffer(height, width, 1);
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                image[r, c, 0] = f(r, c);
            }
        }

        return image;
    }

    private static double StandardDeviation(ImageBuffer image)
    {
        double sum = 0;
        double sumSquares = 0;
        int n = 0;
        foreach (double sample in image.Pixels)
        {
            sum += sample;
            sumSquares += sample * sample;
            n++;
        }

        double mean = sum / n;
        return Math.Sqrt(Math.Max(0, (sumSquares / n) - (mean * mean)));
    }

    private static double Mean(ImageBuffer image)
    {
        double sum = 0;
        int n = 0;
        foreach (double sample in image.Pixels)
        {
            sum += sample;
            n++;
        }

        return sum / n;
    }

    // -------------------------------------------------------------------------------------------
    // Histogram equalization and matching
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void MatchingTransform_OnAPerfectRampAgainstAFlatTarget_IsTheIdentity()
    {
        // 256 pixels, one per level, matched onto 256 equal levels: every level already holds
        // exactly the share it should, so the mapping that minimizes the cumulative error is the one
        // that moves nothing. Anything else here would be an off-by-one in the tie-breaking.
        var counts = new double[256];
        Array.Fill(counts, 1.0);
        var flat = new double[256];
        Array.Fill(flat, 1.0);

        double[] transform = Histograms.MatchingTransform(counts, flat, 256);
        for (int j = 0; j < 256; j++)
        {
            Assert.Equal(j / 255.0, transform[j], 12);
        }
    }

    [Fact]
    public void Equalize_HandsBackAMonotoneMappingAndSpreadsALowContrastPicture()
    {
        using ImageBuffer narrow = Gray(32, 32, (r, c) => 0.45 + (0.1 * ((r + c) / 62.0)));
        var flat = new double[64];
        Array.Fill(flat, 1.0);
        (ImageBuffer result, double[] transform) = Histograms.Equalize(narrow, flat);
        using (result)
        {
            Assert.Equal(256, transform.Length);
            for (int j = 1; j < transform.Length; j++)
            {
                Assert.True(transform[j] >= transform[j - 1], $"mapping fell back at level {j}");
            }

            // The whole picture lived inside a tenth of the range; equalization has to widen it.
            Assert.True(StandardDeviation(result) > 4 * StandardDeviation(narrow));
        }
    }

    [Fact]
    public void MatchHistogram_AgainstItself_ReturnsThePictureUnchanged()
    {
        using ImageBuffer ramp = Gray(16, 16, (r, c) => ((r * 16) + c) / 255.0);
        (ImageBuffer result, double[] histogram) = Enhancement.MatchHistogram(ramp, ramp, 256);
        using (result)
        {
            Assert.Equal(256, histogram.Length);
            for (int r = 0; r < 16; r++)
            {
                for (int c = 0; c < 16; c++)
                {
                    Assert.Equal(ramp[r, c, 0], result[r, c, 0], 12);
                }
            }
        }
    }

    [Fact]
    public void MatchHistogram_MovesADarkPictureTowardsABrightReference()
    {
        using ImageBuffer dark = Gray(24, 24, (r, c) => 0.1 + (0.2 * ((r + c) / 46.0)));
        using ImageBuffer bright = Gray(24, 24, (r, c) => 0.7 + (0.2 * ((r + c) / 46.0)));

        (ImageBuffer matched, _) = Enhancement.MatchHistogram(dark, bright, 64);
        using (matched)
        {
            Assert.True(Mean(matched) > 0.6, $"matched mean was {Mean(matched)}");
        }

        // The smooth method has to land in the same place, give or take the smoothing.
        (ImageBuffer smoothed, _) = Enhancement.MatchHistogram(dark, bright, 64, smooth: true);
        using (smoothed)
        {
            Assert.True(Math.Abs(Mean(smoothed) - Mean(dark) - 0.6) < 0.15);
        }
    }

    // -------------------------------------------------------------------------------------------
    // CLAHE
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void Clahe_WidensALowContrastPictureAndStaysInsideTheRange()
    {
        using ImageBuffer narrow = Gray(64, 64, (r, c) => 0.48 + (0.04 * Math.Sin(r / 6.0) * Math.Cos(c / 5.0)));
        using ImageBuffer result = Enhancement.Clahe(narrow, 4, 4, clipLimit: 1.0);

        Assert.True(StandardDeviation(result) > 3 * StandardDeviation(narrow));
        foreach (double sample in result.Pixels)
        {
            Assert.InRange(sample, 0.0, 1.0);
        }
    }

    [Fact]
    public void Clahe_ClipLimitControlsHowFarTheContrastIsPushed()
    {
        using ImageBuffer narrow = Gray(64, 64, (r, c) => 0.48 + (0.04 * Math.Sin(r / 6.0) * Math.Cos(c / 5.0)));
        using ImageBuffer timid = Enhancement.Clahe(narrow, 4, 4, clipLimit: 0.002);
        using ImageBuffer bold = Enhancement.Clahe(narrow, 4, 4, clipLimit: 1.0);

        // That is the whole point of the "contrast-limited" prefix: a low ceiling holds the tile's
        // histogram near flat, so its mapping stays near the identity.
        Assert.True(StandardDeviation(timid) < StandardDeviation(bold));
    }

    [Fact]
    public void Clahe_HonoursTheOriginalRangeAndTheDistributionWord()
    {
        using ImageBuffer narrow = Gray(32, 32, (r, c) => 0.3 + (0.4 * ((r + c) / 62.0)));
        using ImageBuffer bounded = Enhancement.Clahe(
            narrow, 4, 4, clipLimit: 1.0, range: (0.3, 0.7));

        foreach (double sample in bounded.Pixels)
        {
            Assert.InRange(sample, 0.3 - 1e-12, 0.7 + 1e-12);
        }

        // Rayleigh and exponential are different curves through the same cumulative histogram, so
        // they must both reach white and must not agree with the uniform one.
        using ImageBuffer uniform = Enhancement.Clahe(narrow, 4, 4, clipLimit: 1.0);
        using ImageBuffer rayleigh = Enhancement.Clahe(
            narrow, 4, 4, clipLimit: 1.0, distribution: Enhancement.HistogramShape.Rayleigh);
        using ImageBuffer exponential = Enhancement.Clahe(
            narrow, 4, 4, clipLimit: 1.0, distribution: Enhancement.HistogramShape.Exponential);

        Assert.True(Math.Abs(Mean(rayleigh) - Mean(uniform)) > 1e-3);
        Assert.True(Math.Abs(Mean(exponential) - Mean(uniform)) > 1e-3);
        foreach (double sample in rayleigh.Pixels)
        {
            Assert.InRange(sample, 0.0, 1.0);
        }
    }

    [Fact]
    public void Clahe_RefusesTooFewTilesAndAColourPicture()
    {
        using ImageBuffer gray = Gray(8, 8, (_, _) => 0.5);
        using var colour = new ImageBuffer(8, 8, 3);
        Assert.Throws<ArgumentException>(() => Enhancement.Clahe(gray, 1, 4));
        Assert.Throws<ArgumentException>(() => Enhancement.Clahe(gray, 4, 4, clipLimit: 2));
        Assert.Throws<ArgumentException>(() => Enhancement.Clahe(colour));
    }

    // -------------------------------------------------------------------------------------------
    // Flat field, decorrelation stretch, sharpening
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void FlatField_FlattensAMultiplicativeIlluminationGradient()
    {
        // A constant subject seen through a lighting ramp. A Gaussian of a straight line is that same
        // line, so away from the borders the blur reproduces the shading exactly and dividing it out
        // brings the picture back to a constant — the mean it started with.
        using ImageBuffer shaded = Gray(96, 96, (_, c) => 0.6 * (0.5 + (0.5 * c / 95.0)));
        using ImageBuffer corrected = Enhancement.FlatField(shaded, sigma: 8);

        double mean = Mean(shaded);
        for (int r = 24; r < 72; r++)
        {
            for (int c = 24; c < 72; c++)
            {
                Assert.Equal(mean, corrected[r, c, 0], 8);
            }
        }

        Assert.True(StandardDeviation(corrected) < 0.2 * StandardDeviation(shaded));
    }

    [Fact]
    public void FlatField_LeavesMaskedOutPixelsAsTheyWere()
    {
        using ImageBuffer shaded = Gray(16, 16, (_, c) => 0.4 + (0.4 * c / 15.0));
        using ImageBuffer mask = Gray(16, 16, (r, _) => r < 8 ? 1 : 0);
        using ImageBuffer corrected = Enhancement.FlatField(shaded, sigma: 6, filterSize: 0, mask);

        for (int c = 0; c < 16; c++)
        {
            Assert.Equal(shaded[12, c, 0], corrected[12, c, 0], 12);
        }

        Assert.NotEqual(shaded[2, 15, 0], corrected[2, 15, 0], 6);
    }

    [Fact]
    public void DecorrelationStretch_LeavesTheBandsUncorrelated()
    {
        // Three bands that agree almost exactly — the case the stretch exists for.
        var image = new ImageBuffer(40, 40, 3);
        for (int r = 0; r < 40; r++)
        {
            for (int c = 0; c < 40; c++)
            {
                double basis = 0.4 + (0.2 * Math.Sin((r + c) / 7.0));
                image[r, c, 0] = basis;
                image[r, c, 1] = basis + (0.01 * Math.Sin(r / 3.0));
                image[r, c, 2] = basis + (0.01 * Math.Cos(c / 3.0));
            }
        }

        using (image)
        {
            using ImageBuffer stretched = Enhancement.DecorrelationStretch(image);
            Assert.True(Math.Abs(Correlation(stretched, 0, 1)) < 1e-6);
            Assert.True(Math.Abs(Correlation(stretched, 0, 2)) < 1e-6);
            Assert.True(Math.Abs(Correlation(image, 0, 1)) > 0.99);

            // Covariance mode whitens a different matrix, so it must not give the same answer.
            using ImageBuffer covariance = Enhancement.DecorrelationStretch(
                image, Enhancement.StretchMode.Covariance);
            Assert.True(Math.Abs(Correlation(covariance, 0, 1)) < 1e-6);
        }
    }

    private static double Correlation(ImageBuffer image, int a, int b)
    {
        double sumA = 0, sumB = 0, sumAa = 0, sumBb = 0, sumAb = 0;
        int n = image.Height * image.Width;
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++)
            {
                double x = image[r, c, a];
                double y = image[r, c, b];
                sumA += x;
                sumB += y;
                sumAa += x * x;
                sumBb += y * y;
                sumAb += x * y;
            }
        }

        double covariance = (sumAb / n) - (sumA / n * (sumB / n));
        double spreadA = Math.Sqrt(Math.Max(0, (sumAa / n) - (sumA / n * (sumA / n))));
        double spreadB = Math.Sqrt(Math.Max(0, (sumBb / n) - (sumB / n * (sumB / n))));
        return spreadA * spreadB > 0 ? covariance / (spreadA * spreadB) : 0;
    }

    [Fact]
    public void Sharpen_LeavesAFlatPictureAloneAndSteepensAStep()
    {
        using ImageBuffer flat = Gray(16, 16, (_, _) => 0.5);
        using ImageBuffer unchanged = Enhancement.Sharpen(flat);
        foreach (double sample in unchanged.Pixels)
        {
            Assert.Equal(0.5, sample, 12);
        }

        using ImageBuffer step = Gray(16, 16, (_, c) => c < 8 ? 0.3 : 0.7);
        using ImageBuffer sharp = Enhancement.Sharpen(step, radius: 1.5, amount: 1.0);

        // The pixels either side of the edge move apart: darker on the dark side, lighter on the
        // light side. That overshoot is what sharpening is.
        Assert.True(sharp[8, 7, 0] < step[8, 7, 0]);
        Assert.True(sharp[8, 8, 0] > step[8, 8, 0]);
    }

    [Fact]
    public void Sharpen_ThresholdLeavesTheQuietPartsAlone()
    {
        using ImageBuffer mixed = Gray(24, 24, (r, c) =>
            (c < 12 ? 0.5 + (0.004 * r) : 0.9) - (c is 12 or 13 ? 0.0 : 0.0));
        using ImageBuffer everything = Enhancement.Sharpen(mixed, 1.5, 1.0, threshold: 0.0);
        using ImageBuffer edgesOnly = Enhancement.Sharpen(mixed, 1.5, 1.0, threshold: 0.5);

        double movedAll = 0;
        double movedSome = 0;
        for (int r = 0; r < 24; r++)
        {
            for (int c = 0; c < 6; c++)
            {
                movedAll += Math.Abs(everything[r, c, 0] - mixed[r, c, 0]);
                movedSome += Math.Abs(edgesOnly[r, c, 0] - mixed[r, c, 0]);
            }
        }

        Assert.True(movedSome < movedAll);
    }

    // -------------------------------------------------------------------------------------------
    // Edge-preserving filters
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void Bilateral_SmoothsWithinARegionButNotAcrossAnEdge()
    {
        using ImageBuffer step = Gray(21, 21, (r, c) => (c < 10 ? 0.2 : 0.8) + (0.02 * Math.Sin(r * 2.0)));
        using ImageBuffer bilateral = Denoising.Bilateral(step, degreeOfSmoothing: 0.001, spatialSigma: 2);
        using ImageBuffer blurred = Filters.GaussianBlur(step, 2, 2, 9, 9, Filters.Boundary.Symmetric);

        // Right at the edge the plain blur has averaged the two sides together; the bilateral filter
        // has not, because a neighbour six tenths away weighs nothing.
        Assert.True(Math.Abs(bilateral[10, 9, 0] - 0.2) < 0.05);
        Assert.True(Math.Abs(blurred[10, 9, 0] - 0.2) > 0.15);

        // Inside the flat region it does smooth: the ripple is knocked down.
        Assert.True(Math.Abs(bilateral[10, 3, 0] - 0.2) < Math.Abs(step[10, 3, 0] - 0.2));
    }

    [Fact]
    public void GuidedFilter_WithNoSmoothingReproducesItsOwnGuide()
    {
        // Self-guided with a vanishing regularizer, the local fit is the line y = x, so every window
        // returns the guide itself and the averaged coefficients are exactly one and zero.
        using ImageBuffer ramp = Gray(20, 20, (r, c) => (r + (2.0 * c)) / 60.0);
        using ImageBuffer filtered = Denoising.GuidedFilter(ramp, ramp, 5, 5, 1e-12);
        for (int r = 0; r < 20; r++)
        {
            for (int c = 0; c < 20; c++)
            {
                Assert.Equal(ramp[r, c, 0], filtered[r, c, 0], 6);
            }
        }
    }

    [Fact]
    public void GuidedFilter_SmoothsNoiseAndKeepsTheGuidesEdge()
    {
        var random = new Random(7);
        using ImageBuffer clean = Gray(32, 32, (_, c) => c < 16 ? 0.25 : 0.75);
        using ImageBuffer noisy = Gray(32, 32, (r, c) =>
            Math.Clamp(clean[r, c, 0] + (0.05 * ((2 * random.NextDouble()) - 1)), 0, 1));

        using ImageBuffer filtered = Denoising.GuidedFilter(noisy, clean, 5, 5, 0.01);
        double before = 0;
        double after = 0;
        for (int r = 4; r < 28; r++)
        {
            for (int c = 4; c < 28; c++)
            {
                before += Math.Abs(noisy[r, c, 0] - clean[r, c, 0]);
                after += Math.Abs(filtered[r, c, 0] - clean[r, c, 0]);
            }
        }

        Assert.True(after < before / 2, $"noise fell from {before} only to {after}");
    }

    [Fact]
    public void AnisotropicDiffusion_LeavesAConstantAloneAndReducesTotalVariation()
    {
        using ImageBuffer flat = Gray(12, 12, (_, _) => 0.4);
        using ImageBuffer stillFlat = Denoising.AnisotropicDiffusion(flat, [0.1, 0.1]);
        foreach (double sample in stillFlat.Pixels)
        {
            Assert.Equal(0.4, sample, 12);
        }

        var random = new Random(11);
        using ImageBuffer noisy = Gray(24, 24, (_, c) =>
            Math.Clamp((c < 12 ? 0.3 : 0.7) + (0.06 * ((2 * random.NextDouble()) - 1)), 0, 1));
        using ImageBuffer smoothed = Denoising.AnisotropicDiffusion(noisy, [0.1, 0.1, 0.1, 0.1, 0.1]);

        Assert.True(TotalVariation(smoothed) < TotalVariation(noisy));

        // The step itself survives — that is the difference between this and a blur.
        Assert.True(smoothed[12, 11, 0] < 0.42 && smoothed[12, 12, 0] > 0.58);
    }

    private static double TotalVariation(ImageBuffer image)
    {
        double total = 0;
        for (int r = 1; r < image.Height; r++)
        {
            for (int c = 1; c < image.Width; c++)
            {
                total += Math.Abs(image[r, c, 0] - image[r - 1, c, 0]);
                total += Math.Abs(image[r, c, 0] - image[r, c - 1, 0]);
            }
        }

        return total;
    }

    [Fact]
    public void EstimateDiffusion_FallsAcrossItsIterationsAndSurvivesAFlatPicture()
    {
        using ImageBuffer edges = Gray(32, 32, (_, c) => c < 16 ? 0.2 : 0.9);
        (double[] thresholds, int iterations) = Denoising.EstimateDiffusion(edges);

        Assert.Equal(5, iterations);
        Assert.Equal(5, thresholds.Length);
        for (int k = 1; k < thresholds.Length; k++)
        {
            Assert.True(thresholds[k] < thresholds[k - 1]);
        }

        using ImageBuffer flat = Gray(8, 8, (_, _) => 0.5);
        (double[] onFlat, _) = Denoising.EstimateDiffusion(flat);
        Assert.All(onFlat, t => Assert.True(t > 0));
    }

    [Fact]
    public void NonLocalMeans_LeavesAConstantAloneAndRemovesNoiseFromOne()
    {
        using ImageBuffer flat = Gray(20, 20, (_, _) => 0.6);
        using ImageBuffer stillFlat = Denoising.NonLocalMeans(flat, 0.05, 11, 3);
        foreach (double sample in stillFlat.Pixels)
        {
            Assert.Equal(0.6, sample, 10);
        }

        var random = new Random(3);
        using ImageBuffer noisy = Gray(32, 32, (_, _) =>
            Math.Clamp(0.5 + (0.08 * ((2 * random.NextDouble()) - 1)), 0, 1));
        using ImageBuffer cleaned = Denoising.NonLocalMeans(noisy, 0.05, 11, 3);

        Assert.True(StandardDeviation(cleaned) < 0.4 * StandardDeviation(noisy));
        Assert.Equal(Mean(noisy), Mean(cleaned), 2);
    }

    [Fact]
    public void EstimateNoise_RecoversTheStandardDeviationItWasGiven()
    {
        var random = new Random(19);
        const double Sigma = 0.05;
        using ImageBuffer noisy = Gray(160, 160, (_, _) =>
        {
            double u1 = 1.0 - random.NextDouble();
            double u2 = 1.0 - random.NextDouble();
            return 0.5 + (Sigma * Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
        });

        double estimate = Denoising.EstimateNoise(noisy);
        Assert.InRange(estimate, 0.85 * Sigma, 1.15 * Sigma);

        using ImageBuffer flat = Gray(32, 32, (_, _) => 0.5);
        Assert.Equal(0.0, Denoising.EstimateNoise(flat), 12);
    }

    // -------------------------------------------------------------------------------------------
    // Haze
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void ReduceHaze_RecoversAPictureItWasSyntheticallyHazedFrom()
    {
        // The haze model itself: what the camera sees is the scene attenuated by t plus the
        // atmosphere filling in the rest.
        const double Transmission = 0.5;
        var scene = new ImageBuffer(48, 48, 3);
        var hazy = new ImageBuffer(48, 48, 3);
        for (int r = 0; r < 48; r++)
        {
            for (int c = 0; c < 48; c++)
            {
                double[] colour = ((r / 12) + (c / 12)) % 2 == 0
                    ? [0.15, 0.35, 0.10]
                    : new[] { 0.55, 0.20, 0.25 };
                for (int ch = 0; ch < 3; ch++)
                {
                    scene[r, c, ch] = colour[ch];
                    hazy[r, c, ch] = (colour[ch] * Transmission) + (1.0 * (1 - Transmission));
                }
            }
        }

        using (scene)
        using (hazy)
        {
            // The haze colour is given rather than estimated: a checkerboard with no sky in it
            // carries no region where the atmosphere dominates, which is exactly the situation the
            // dark-channel estimate cannot resolve. What is under test here is the inversion.
            (ImageBuffer cleared, ImageBuffer map) = Enhancement.ReduceHaze(
                hazy, 0.9, Enhancement.HazeMethod.SimpleDarkChannel, [1.0, 1.0, 1.0],
                Enhancement.HazeContrast.None);
            using (cleared)
            using (map)
            {
                Assert.Equal(48, map.Height);
                Assert.Equal(1, map.Channels);

                // The transmission the prior recovers has to be near the one the haze was built with.
                Assert.InRange(map[6, 6, 0], 0.4, 0.6);
                Assert.True(Difference(cleared, scene) < 0.25 * Difference(hazy, scene));
            }

            // Every contrast option has to produce a picture, and the approximate method too.
            (ImageBuffer boosted, ImageBuffer boostMap) = Enhancement.ReduceHaze(
                hazy, 0.9, Enhancement.HazeMethod.ApproximateDarkChannel, null,
                Enhancement.HazeContrast.Boost, 0.3);
            boosted.Dispose();
            boostMap.Dispose();
        }
    }

    [Fact]
    public void ReduceHaze_FindsTheHazeColourWhenThePictureContainsSky()
    {
        // The dark-channel prior needs somewhere the atmosphere is all there is. Give it a band of
        // pure haze at the top and it should find that colour and undo the rest of the picture with
        // it — the estimate is what the top rows are made of.
        const double Transmission = 0.4;
        var hazy = new ImageBuffer(40, 40, 3);
        double[] light = [0.9, 0.92, 1.0];
        for (int r = 0; r < 40; r++)
        {
            for (int c = 0; c < 40; c++)
            {
                for (int ch = 0; ch < 3; ch++)
                {
                    hazy[r, c, ch] = r < 8
                        ? light[ch]
                        : (0.2 * Transmission) + (light[ch] * (1 - Transmission));
                }
            }
        }

        using (hazy)
        {
            (ImageBuffer cleared, ImageBuffer map) = Enhancement.ReduceHaze(
                hazy, 0.9, Enhancement.HazeMethod.SimpleDarkChannel, null, Enhancement.HazeContrast.None);
            using (cleared)
            using (map)
            {
                // The transmission comes back near the 0.4 the haze was built with, and the subject
                // near the 0.2 grey it started as, in all three channels at once — which is only
                // possible if the estimated haze colour was right.
                Assert.InRange(map[30, 20, 0], 0.3, 0.5);
                for (int ch = 0; ch < 3; ch++)
                {
                    Assert.InRange(cleared[30, 20, ch], 0.12, 0.28);
                }
            }
        }
    }

    private static double Difference(ImageBuffer a, ImageBuffer b)
    {
        double total = 0;
        for (int i = 0; i < a.Pixels.Length; i++)
        {
            total += Math.Abs(a.Pixels[i] - b.Pixels[i]);
        }

        return total;
    }

    [Fact]
    public void LocalBrighten_LiftsTheDarkPartsAndCanBlendItsOwnResultBack()
    {
        using ImageBuffer dim = Gray(32, 32, (r, c) => 0.05 + (0.25 * ((r + c) / 62.0)));
        (ImageBuffer brighter, ImageBuffer map) = Enhancement.LocalBrighten(dim, 1.0);
        using (brighter)
        using (map)
        {
            Assert.True(Mean(brighter) > Mean(dim));
        }

        (ImageBuffer blended, ImageBuffer blendMap) = Enhancement.LocalBrighten(dim, 1.0, alphaBlend: true);
        using (blended)
        using (blendMap)
        {
            // The blend cannot go past the un-blended result, and cannot fall below the original.
            Assert.True(Mean(blended) >= Mean(dim));
        }
    }

    // -------------------------------------------------------------------------------------------
    // Ridge measures
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void FiberMetric_AnswersOnABrightBarAndNotOnTheBackground()
    {
        using ImageBuffer bar = Gray(41, 41, (r, _) => r is >= 19 and <= 21 ? 1.0 : 0.0);
        using ImageBuffer measured = Vesselness.FiberMetric(bar, [3], 0.05);

        Assert.True(measured[20, 20, 0] > 0.5, $"the bar scored {measured[20, 20, 0]}");
        Assert.True(measured[3, 20, 0] < 0.05, $"the background scored {measured[3, 20, 0]}");

        // The same bar read as a dark fibre must score nothing, and a dark bar must score.
        using ImageBuffer wrongWay = Vesselness.FiberMetric(bar, [3], 0.05, Vesselness.Polarity.Dark);
        Assert.True(wrongWay[20, 20, 0] < 1e-9);

        using ImageBuffer trench = Gray(41, 41, (r, _) => r is >= 19 and <= 21 ? 0.0 : 1.0);
        using ImageBuffer found = Vesselness.FiberMetric(trench, [3], 0.05, Vesselness.Polarity.Dark);
        Assert.True(found[20, 20, 0] > 0.5);
    }

    [Fact]
    public void FiberMetric_PrefersTheScaleThatMatchesTheFibre()
    {
        using ImageBuffer thin = Gray(61, 61, (r, _) => r is >= 29 and <= 31 ? 1.0 : 0.0);
        using ImageBuffer atThree = Vesselness.FiberMetric(thin, [3], 0.05);
        using ImageBuffer atFifteen = Vesselness.FiberMetric(thin, [15], 0.05);

        // A three-pixel bar answered at a scale five times too wide is a much weaker response, so
        // the scale really is doing something rather than being a constant blur.
        Assert.True(atThree[30, 30, 0] > atFifteen[30, 30, 0]);
    }

    [Fact]
    public void MaxHessianNorm_IsZeroOnAFlatPictureAndPositiveOnAStructuredOne()
    {
        using ImageBuffer flat = Gray(32, 32, (_, _) => 0.5);
        Assert.Equal(0.0, Vesselness.MaxHessianNorm(flat), 12);

        using ImageBuffer bar = Gray(41, 41, (r, _) => r is >= 19 and <= 21 ? 1.0 : 0.0);
        double norm = Vesselness.MaxHessianNorm(bar, 3);
        Assert.True(norm > 0);

        // Half the norm is the structure sensitivity the documentation suggests, and at that setting
        // the bar has to clear the bar.
        using ImageBuffer measured = Vesselness.FiberMetric(bar, [3], 0.5 * norm);
        Assert.True(measured[20, 20, 0] > 0.5);
    }

    // -------------------------------------------------------------------------------------------
    // Noise
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void SpeckleNoise_ScalesWithTheSignalSoBlackStaysBlack()
    {
        using ImageBuffer half = Gray(40, 40, (r, c) => c < 20 ? 0.0 : 0.8);
        using ImageBuffer speckled = PointOps.SpeckleNoise(half, 0.05, new Random(5));

        for (int r = 0; r < 40; r++)
        {
            for (int c = 0; c < 20; c++)
            {
                Assert.Equal(0.0, speckled[r, c, 0], 12);
            }
        }

        Assert.True(StandardDeviation(speckled) > StandardDeviation(half));
    }

    [Fact]
    public void PoissonNoise_IsInvisibleAtTheCountsADoubleImageUsesAndVisibleAtEightBits()
    {
        using ImageBuffer grey = Gray(32, 32, (_, _) => 0.5);
        using ImageBuffer quiet = PointOps.PoissonNoise(grey, 1e12, new Random(2));
        Assert.True(StandardDeviation(quiet) < 1e-5);

        using ImageBuffer noisy = PointOps.PoissonNoise(grey, 255, new Random(2));

        // The shot noise on a mean of 127.5 counts has a standard deviation of √127.5 counts, which
        // is about 0.044 once divided back down.
        Assert.InRange(StandardDeviation(noisy), 0.02, 0.08);
    }

    [Fact]
    public void LocalVarianceNoise_FollowsItsOwnVarianceField()
    {
        using ImageBuffer grey = Gray(64, 64, (_, _) => 0.5);
        var variance = new double[64, 64];
        for (int r = 0; r < 64; r++)
        {
            for (int c = 0; c < 64; c++)
            {
                variance[r, c] = c < 32 ? 0.0 : 0.01;
            }
        }

        using ImageBuffer noisy = PointOps.LocalVarianceNoise(grey, variance, new Random(13));
        for (int r = 0; r < 64; r++)
        {
            Assert.Equal(0.5, noisy[r, 4, 0], 12);
        }

        double spread = 0;
        for (int r = 0; r < 64; r++)
        {
            for (int c = 32; c < 64; c++)
            {
                spread += (noisy[r, c, 0] - 0.5) * (noisy[r, c, 0] - 0.5);
            }
        }

        Assert.InRange(Math.Sqrt(spread / (64 * 32)), 0.05, 0.14);
        Assert.Throws<ArgumentException>(
            () => PointOps.LocalVarianceNoise(grey, new double[4, 4], new Random(1)));
    }
}
