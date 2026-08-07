using System;
using JGraph.Imaging;
using Xunit;

namespace JGraph.Tests.Imaging;

/// <summary>
/// M46 wave J: whole-picture statistics, the quality metrics, texture by co-occurrence, and the
/// display composites.
/// </summary>
public sealed class QualityMetricTests
{
    // --- ImageStatistics ----------------------------------------------------------------------------

    [Fact]
    public void Mean_And_StandardDeviation_MatchTheHandComputedValues()
    {
        double[,] values = { { 1, 2 }, { 3, 4 } };
        Assert.Equal(2.5, ImageStatistics.Mean(values), 12);

        // Normalized by n − 1, which is MATLAB's default for std and so for std2.
        Assert.Equal(Math.Sqrt(5.0 / 3.0), ImageStatistics.StandardDeviation(values), 12);
    }

    [Fact]
    public void Correlation_IsBlindToBrightnessAndContrast()
    {
        double[,] values = { { 1, 2, 3 }, { 4, 5, 6 } };
        var doubled = new double[2, 3];
        for (int r = 0; r < 2; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                doubled[r, c] = (2 * values[r, c]) + 10;
            }
        }

        // The same picture brighter and with more contrast is still the same picture as far as a
        // correlation is concerned — that is what taking the mean out and normalizing does.
        Assert.Equal(1.0, ImageStatistics.Correlation(values, doubled), 12);
    }

    [Fact]
    public void Correlation_OfAFlatPicture_IsNotDefined()
    {
        double[,] flat = { { 0.5, 0.5 }, { 0.5, 0.5 } };
        double[,] ramp = { { 0.0, 0.25 }, { 0.5, 0.75 } };
        Assert.True(double.IsNaN(ImageStatistics.Correlation(flat, ramp)));
    }

    [Fact]
    public void Entropy_IsZeroForAFlatFieldAndOneBitForABalancedMask()
    {
        double[,] flat = new double[8, 8];
        Assert.Equal(0.0, ImageStatistics.Entropy(flat), 12);

        var half = new double[8, 8];
        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                half[r, c] = c < 4 ? 0 : 1;
            }
        }

        Assert.Equal(1.0, ImageStatistics.Entropy(half, 2), 12);
    }

    // --- Error metrics -------------------------------------------------------------------------

    [Fact]
    public void MeanSquaredError_AndPeakSignalToNoise_AgreeWithTheDefinition()
    {
        var a = new double[4, 4];
        var b = new double[4, 4];
        for (int r = 0; r < 4; r++)
        {
            for (int c = 0; c < 4; c++)
            {
                a[r, c] = 0.5;
                b[r, c] = 0.6;
            }
        }

        Assert.Equal(0.01, QualityMetrics.MeanSquaredError(a, b), 12);
        Assert.Equal(20.0, QualityMetrics.PeakSignalToNoise(a, b, 1.0), 12);
    }

    [Fact]
    public void PeakSignalToNoise_OfAPictureAgainstItself_IsInfinite()
    {
        double[,] values = Ramp(8, 8);
        Assert.True(double.IsPositiveInfinity(QualityMetrics.PeakSignalToNoise(values, values, 1.0)));
    }

    [Fact]
    public void StructuralSimilarity_OfAPictureAgainstItself_IsExactlyOne()
    {
        double[,] values = Ramp(32, 32);
        (double score, double[,] map) = QualityMetrics.StructuralSimilarity(values, values);
        Assert.Equal(1.0, score, 10);
        Assert.Equal(32, map.GetLength(0));

        foreach (double value in map)
        {
            Assert.Equal(1.0, value, 10);
        }
    }

    [Fact]
    public void StructuralSimilarity_PunishesStructuralDamageMoreThanAnEvenShift()
    {
        double[,] reference = Checkerboard(48, 48, 6);
        var brighter = new double[48, 48];
        var scrambled = new double[48, 48];
        var noise = new Random(11);
        for (int r = 0; r < 48; r++)
        {
            for (int c = 0; c < 48; c++)
            {
                brighter[r, c] = Math.Min(1, reference[r, c] + 0.1);
                scrambled[r, c] = noise.NextDouble();
            }
        }

        double shifted = QualityMetrics.StructuralSimilarity(brighter, reference).Score;
        double broken = QualityMetrics.StructuralSimilarity(scrambled, reference).Score;

        // A picture that is uniformly a little brighter still has all of its structure; one made of
        // noise has none. A metric that scored these the same way would be measuring the wrong thing.
        Assert.True(shifted > 0.5, $"an even brightness shift scored {shifted:F3}");
        Assert.True(broken < shifted / 2, $"noise scored {broken:F3} against {shifted:F3}");
    }

    [Fact]
    public void StructuralSimilarity_WithAllOnesExponents_MatchesTheGeneralForm()
    {
        // The short form and the three-factor form are algebraically identical at unit exponents, and
        // the code takes the short path there. If they ever disagreed, one of the two would be wrong.
        // The pair has to be positively correlated everywhere for the comparison to mean anything:
        // the three-factor form raises the structure term to a power, and a negative structure taken
        // to a non-integer power is not a number at all.
        double[,] a = Ramp(24, 24);
        var b = new double[24, 24];
        for (int r = 0; r < 24; r++)
        {
            for (int c = 0; c < 24; c++)
            {
                b[r, c] = (a[r, c] * 0.8) + 0.05;
            }
        }

        double plain = QualityMetrics.StructuralSimilarity(a, b).Score;
        double general = QualityMetrics.StructuralSimilarity(
            a, b, new QualityMetrics.SsimOptions(Exponents: [1.0, 1.0, 1.0000000001])).Score;
        Assert.Equal(plain, general, 8);
    }

    [Fact]
    public void MultiScaleSimilarity_OfAPictureAgainstItself_IsOne()
    {
        double[,] values = Ramp(64, 64);
        (double score, double[][,] maps) = QualityMetrics.MultiScaleSimilarity(values, values, 3);
        Assert.Equal(1.0, score, 8);
        Assert.Equal(3, maps.Length);

        // Each level is half the last, which is the whole point of running down a pyramid.
        Assert.Equal(64, maps[0].GetLength(0));
        Assert.Equal(32, maps[1].GetLength(0));
        Assert.Equal(16, maps[2].GetLength(0));
    }

    [Fact]
    public void MultiScaleSimilarity_RefusesAPictureTooSmallForTheScalesAsked()
    {
        double[,] small = Ramp(8, 8);
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => QualityMetrics.MultiScaleSimilarity(small, small, 5));
        Assert.Contains("fewer scales", error.Message, StringComparison.Ordinal);
    }

    // --- Overlap -------------------------------------------------------------------------------

    [Fact]
    public void Dice_AndJaccard_AgreeWithTheirDefinitionsOnTwoOverlappingSquares()
    {
        double[,] a = Square(10, 1, 5);
        double[,] b = Square(10, 2, 5);

        // Two 5×5 squares offset by one in each direction share 4×4 = 16 pixels.
        Assert.Equal(2.0 * 16 / (25 + 25), Assert.Single(QualityMetrics.Dice(a, b)), 12);
        Assert.Equal(16.0 / (25 + 25 - 16), Assert.Single(QualityMetrics.Jaccard(a, b)), 12);
    }

    [Fact]
    public void Dice_IsAlwaysAtLeastJaccard()
    {
        double[,] a = Square(12, 1, 6);
        double[,] b = Square(12, 4, 6);
        double dice = Assert.Single(QualityMetrics.Dice(a, b));
        double jaccard = Assert.Single(QualityMetrics.Jaccard(a, b));

        // The two order every pair identically; Dice is the kinder of them because the shared area
        // is counted twice in its numerator.
        Assert.True(dice >= jaccard, $"dice {dice:F4} fell below jaccard {jaccard:F4}");
    }

    [Fact]
    public void Dice_OnLabelMaps_AnswersOncePerLabel()
    {
        var a = new double[6, 6];
        var b = new double[6, 6];
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 6; c++)
            {
                a[r, c] = 1;
                b[r, c] = 1;
            }
        }

        for (int r = 3; r < 6; r++)
        {
            for (int c = 0; c < 6; c++)
            {
                a[r, c] = 2;
                b[r, c] = r < 5 ? 2 : 0;
            }
        }

        double[] scores = QualityMetrics.Dice(a, b);
        Assert.Equal(2, scores.Length);
        Assert.Equal(1.0, scores[0], 12);
        Assert.True(scores[1] < 1.0);
    }

    [Fact]
    public void BoundaryFScore_IsOneForAnExactMatchAndFallsWithDisplacement()
    {
        double[,] truth = Square(40, 10, 12);
        double[,] exact = Square(40, 10, 12);
        Assert.Equal(1.0, Assert.Single(QualityMetrics.BoundaryFScore(exact, truth, 2).Score), 12);

        double[,] moved = Square(40, 16, 12);
        double far = Assert.Single(QualityMetrics.BoundaryFScore(moved, truth, 2).Score);
        Assert.True(far < 0.6, $"a square moved six pixels still scored {far:F3}");
    }

    [Fact]
    public void BoundaryFScore_IsIndifferentToHowMuchInteriorTheRegionHas()
    {
        // The same one-pixel boundary error costs the same whether the region behind it is large or
        // small, which is exactly what an overlap measure cannot say.
        double[,] smallTruth = Square(60, 20, 6);
        double[,] smallOff = Square(60, 21, 6);
        double[,] bigTruth = Square(60, 10, 40);
        double[,] bigOff = Square(60, 11, 40);

        double small = Assert.Single(QualityMetrics.BoundaryFScore(smallOff, smallTruth, 1.5).Score);
        double big = Assert.Single(QualityMetrics.BoundaryFScore(bigOff, bigTruth, 1.5).Score);
        Assert.Equal(small, big, 2);

        double smallDice = Assert.Single(QualityMetrics.Dice(smallOff, smallTruth));
        double bigDice = Assert.Single(QualityMetrics.Dice(bigOff, bigTruth));
        Assert.True(bigDice - smallDice > 0.1, "the overlap measure was expected to be size-sensitive");
    }

    [Fact]
    public void DefaultBoundaryTolerance_ScalesWithThePicture()
    {
        Assert.Equal(0.0075 * Math.Sqrt(200), QualityMetrics.DefaultBoundaryTolerance(10, 10), 12);
        Assert.True(QualityMetrics.DefaultBoundaryTolerance(1000, 1000)
            > QualityMetrics.DefaultBoundaryTolerance(100, 100));
    }

    // --- Texture -------------------------------------------------------------------------------

    [Fact]
    public void Comatrix_CountsEveryPairAtTheGivenOffset()
    {
        double[,] values = { { 0, 0.5 }, { 0.5, 1 } };
        (double[][,] matrices, double[,] scaled) = TextureAnalysis.Comatrix(
            values, 2, (0, 1), [(0, 1)], symmetric: false);

        // One offset, one step right, on a 2×2 picture: two pairs.
        double total = 0;
        foreach (double count in matrices[0])
        {
            total += count;
        }

        Assert.Equal(2.0, total, 12);
        Assert.Equal(1.0, scaled[0, 0], 12);
        Assert.Equal(2.0, scaled[1, 1], 12);
    }

    [Fact]
    public void Comatrix_Symmetric_IsItsOwnTranspose()
    {
        double[,] values = Ramp(16, 16);
        double[][,] matrices = TextureAnalysis.Comatrix(
            values, 8, (0, 1), [(0, 1)], symmetric: true).Matrices;
        double[,] glcm = matrices[0];
        for (int i = 0; i < 8; i++)
        {
            for (int j = 0; j < 8; j++)
            {
                Assert.Equal(glcm[i, j], glcm[j, i], 12);
            }
        }
    }

    [Fact]
    public void Comatrix_TakesOneMatrixPerOffset()
    {
        double[,] values = Checkerboard(16, 16, 1);
        double[][,] matrices = TextureAnalysis.Comatrix(
            values, 2, (0, 1), [(0, 1), (1, 0), (1, 1)], symmetric: false).Matrices;
        Assert.Equal(3, matrices.Length);

        // A one-pixel checkerboard read one step diagonally always lands on the same colour; read one
        // step across it never does. That difference is the whole reason the offset is a parameter.
        Assert.Equal(0.0, matrices[2][0, 1], 12);
        Assert.True(matrices[0][0, 1] > 0);
    }

    [Fact]
    public void Properties_SeparateAFlatFieldFromANoisyOne()
    {
        double[,] flat = new double[32, 32];
        var noisy = new double[32, 32];
        var random = new Random(5);
        for (int r = 0; r < 32; r++)
        {
            for (int c = 0; c < 32; c++)
            {
                noisy[r, c] = random.NextDouble();
            }
        }

        double[,] flatGlcm = TextureAnalysis.Comatrix(flat, 8, (0, 1), [(0, 1)], false).Matrices[0];
        double[,] noisyGlcm = TextureAnalysis.Comatrix(noisy, 8, (0, 1), [(0, 1)], false).Matrices[0];

        (double flatContrast, _, double flatEnergy, double flatHomogeneity) =
            TextureAnalysis.Properties(flatGlcm);
        (double noisyContrast, _, double noisyEnergy, double noisyHomogeneity) =
            TextureAnalysis.Properties(noisyGlcm);

        // Everything about a flat field lands on the diagonal: no contrast, perfect homogeneity, and
        // all the probability in one cell so the energy is one.
        Assert.Equal(0.0, flatContrast, 12);
        Assert.Equal(1.0, flatEnergy, 12);
        Assert.Equal(1.0, flatHomogeneity, 12);
        Assert.True(noisyContrast > 1, $"noise scored {noisyContrast:F3} for contrast");
        Assert.True(noisyEnergy < 0.05);
        Assert.True(noisyHomogeneity < flatHomogeneity);
    }

    [Fact]
    public void Properties_OfAFlatPicture_LeaveTheCorrelationUndefined()
    {
        double[,] flat = new double[16, 16];
        double[,] glcm = TextureAnalysis.Comatrix(flat, 4, (0, 1), [(0, 1)], false).Matrices[0];
        Assert.True(double.IsNaN(TextureAnalysis.Properties(glcm).Correlation));
    }

    // --- Composites ----------------------------------------------------------------------------

    [Fact]
    public void Montage_LaysTilesOutWithTheBorderAsked()
    {
        using var one = new ImageBuffer(4, 6, 1);
        using var two = new ImageBuffer(4, 6, 1);
        using ImageBuffer sheet = Compositing.Montage([one, two], 1, 2, border: 2, background: [0.25]);

        Assert.Equal(4 + (2 * 2), sheet.Height);
        Assert.Equal((2 * 6) + (3 * 2), sheet.Width);
        Assert.Equal(0.25, sheet[0, 0, 0], 12);
    }

    [Fact]
    public void Montage_ChoosesANearSquareGridWhenNobodySaidOtherwise()
    {
        var tiles = new List<ImageBuffer>();
        for (int i = 0; i < 5; i++)
        {
            tiles.Add(new ImageBuffer(8, 8, 1));
        }

        try
        {
            // Five pictures fit in three columns and two rows, spare cells at the end.
            using ImageBuffer sheet = Compositing.Montage(tiles, 0, 0, 0, [0]);
            Assert.Equal(16, sheet.Height);
            Assert.Equal(24, sheet.Width);
        }
        finally
        {
            foreach (ImageBuffer tile in tiles)
            {
                tile.Dispose();
            }
        }
    }

    [Fact]
    public void Montage_RefusesAGridTooSmallForThePicturesGiven()
    {
        using var one = new ImageBuffer(4, 4, 1);
        using var two = new ImageBuffer(4, 4, 1);
        using var three = new ImageBuffer(4, 4, 1);
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Compositing.Montage([one, two, three], 1, 2, 0, [0]));
        Assert.Contains("holds 2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Fuse_FalseColor_MakesAgreementGreyAndDisagreementColoured()
    {
        using var a = new ImageBuffer(4, 4, 1);
        using var b = new ImageBuffer(4, 4, 1);
        for (int r = 0; r < 4; r++)
        {
            for (int c = 0; c < 4; c++)
            {
                a[r, c, 0] = c / 3.0;
                b[r, c, 0] = r < 2 ? c / 3.0 : 1 - (c / 3.0);
            }
        }

        using ImageBuffer fused = Compositing.Fuse(
            a, b, Compositing.FuseMethod.FalseColor, Compositing.FuseScaling.None);
        Assert.Equal(3, fused.Channels);

        // Where the two agree, red equals green equals blue — that is what "grey means agreement"
        // means, and it is the whole reason the default channel map is [2 1 2].
        Assert.Equal(fused[0, 1, 0], fused[0, 1, 1], 12);
        Assert.Equal(fused[0, 1, 1], fused[0, 1, 2], 12);
        Assert.NotEqual(fused[3, 0, 0], fused[3, 0, 1]);
    }

    [Fact]
    public void Fuse_Difference_IsZeroForAPictureAgainstItself()
    {
        using var a = new ImageBuffer(4, 4, 1);
        for (int r = 0; r < 4; r++)
        {
            for (int c = 0; c < 4; c++)
            {
                a[r, c, 0] = (r + c) / 6.0;
            }
        }

        using ImageBuffer fused = Compositing.Fuse(
            a, a, Compositing.FuseMethod.Difference, Compositing.FuseScaling.None);
        Assert.Equal(1, fused.Channels);
        foreach (double value in fused.Pixels)
        {
            Assert.Equal(0.0, value, 12);
        }
    }

    [Fact]
    public void Fuse_PadsToTheLargerOfTwoDifferentSizes()
    {
        using var small = new ImageBuffer(3, 4, 1);
        using var large = new ImageBuffer(6, 5, 1);
        using ImageBuffer fused = Compositing.Fuse(
            small, large, Compositing.FuseMethod.Blend, Compositing.FuseScaling.None);
        Assert.Equal(6, fused.Height);
        Assert.Equal(5, fused.Width);
    }

    [Fact]
    public void Profile_SamplesEvenlyAlongTheWholePathAndNotWithinEachLeg()
    {
        using var image = new ImageBuffer(16, 16, 1);
        for (int r = 0; r < 16; r++)
        {
            for (int c = 0; c < 16; c++)
            {
                image[r, c, 0] = c / 15.0;
            }
        }

        (double[,] values, double[] x, double[] y) = Compositing.Profile(
            image, [0, 15], [0, 0], 16, Compositing.Sampling.Bilinear);

        Assert.Equal(16, values.GetLength(0));
        Assert.Equal(0.0, values[0, 0], 12);
        Assert.Equal(1.0, values[15, 0], 12);
        Assert.Equal(0.0, y[8], 12);

        // Sixteen samples across fifteen pixels are one pixel apart.
        Assert.Equal(1.0, x[1] - x[0], 12);
    }

    [Fact]
    public void Profile_WithNoCountAsked_TakesOnePerPixelOfPathLength()
    {
        using var image = new ImageBuffer(16, 16, 1);
        (double[,] values, _, _) = Compositing.Profile(image, [0, 9], [0, 0], 0);
        Assert.Equal(10, values.GetLength(0));
    }

    [Fact]
    public void PixelValues_AnswerThreeColumnsEvenForAGreyPicture()
    {
        using var image = new ImageBuffer(4, 4, 1);
        image[1, 2, 0] = 0.75;
        double[,] values = Compositing.PixelValues(image, [2, 99], [1, 0]);

        Assert.Equal(2, values.GetLength(0));
        Assert.Equal(3, values.GetLength(1));
        Assert.Equal(0.75, values[0, 0], 12);
        Assert.Equal(values[0, 0], values[0, 2], 12);

        // A point off the picture has no colour, and answering zero beats refusing a click near an
        // edge outright.
        Assert.Equal(0.0, values[1, 0], 12);
    }

    // --- Fixtures ------------------------------------------------------------------------------

    private static double[,] Ramp(int rows, int cols)
    {
        var values = new double[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                values[r, c] = (r + c) / (double)(rows + cols);
            }
        }

        return values;
    }

    private static double[,] Checkerboard(int rows, int cols, int square)
    {
        var values = new double[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                values[r, c] = ((r / square) + (c / square)) % 2 == 0 ? 0.2 : 0.8;
            }
        }

        return values;
    }

    private static double[,] Square(int side, int start, int size)
    {
        var values = new double[side, side];
        for (int r = start; r < start + size; r++)
        {
            for (int c = start; c < start + size; c++)
            {
                values[r, c] = 1;
            }
        }

        return values;
    }
}
