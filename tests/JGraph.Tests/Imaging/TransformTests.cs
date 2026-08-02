using JGraph.Imaging;
using Xunit;

namespace JGraph.Tests.Imaging;

/// <summary>
/// M46 wave H: the cosine transform, the Radon pair and the phantom they are judged on, quadtree
/// decomposition, and the two correlation-based searches.
/// </summary>
public sealed class TransformTests
{
    // --- The cosine transform ------------------------------------------------------------------

    [Fact]
    public void DctMatrix_IsOrthonormal()
    {
        // The whole point of the orthonormal form: the inverse is the transpose, so a round trip is
        // exact and no scale factor has to be remembered anywhere.
        double[,] d = CosineTransforms.Matrix(8);
        for (int i = 0; i < 8; i++)
        {
            for (int j = 0; j < 8; j++)
            {
                double dot = 0;
                for (int k = 0; k < 8; k++)
                {
                    dot += d[i, k] * d[j, k];
                }

                Assert.Equal(i == j ? 1.0 : 0.0, dot, 12);
            }
        }
    }

    [Fact]
    public void Dct2_AgreesWithTheMatrixForm()
    {
        double[,] a = Ramp(4, 5);
        double[,] fast = CosineTransforms.Forward(a);

        double[,] rowMatrix = CosineTransforms.Matrix(4);
        double[,] colMatrix = CosineTransforms.Matrix(5);
        for (int p = 0; p < 4; p++)
        {
            for (int q = 0; q < 5; q++)
            {
                double sum = 0;
                for (int m = 0; m < 4; m++)
                {
                    for (int n = 0; n < 5; n++)
                    {
                        sum += rowMatrix[p, m] * a[m, n] * colMatrix[q, n];
                    }
                }

                Assert.Equal(sum, fast[p, q], 10);
            }
        }
    }

    [Fact]
    public void Dct2_OfAFlatField_PutsEverythingInTheFirstCoefficient()
    {
        // A constant has no variation to describe, so every basis function but the flat one
        // integrates to nothing — the property that makes the transform worth using at all.
        var flat = new double[6, 6];
        for (int r = 0; r < 6; r++)
        {
            for (int c = 0; c < 6; c++)
            {
                flat[r, c] = 0.25;
            }
        }

        double[,] coefficients = CosineTransforms.Forward(flat);
        Assert.Equal(0.25 * 6, coefficients[0, 0], 10);
        for (int r = 0; r < 6; r++)
        {
            for (int c = 0; c < 6; c++)
            {
                if (r != 0 || c != 0)
                {
                    Assert.Equal(0.0, coefficients[r, c], 10);
                }
            }
        }
    }

    [Fact]
    public void Idct2_UndoesDct2_AtEveryShape()
    {
        // Odd sizes and a single row exercise the even-extension path where a length-2n transform
        // has to fold back onto exactly n samples.
        foreach ((int rows, int cols) in new[] { (1, 1), (1, 7), (7, 1), (5, 5), (6, 9) })
        {
            double[,] a = Ramp(rows, cols);
            double[,] back = CosineTransforms.Inverse(CosineTransforms.Forward(a));
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    Assert.Equal(a[r, c], back[r, c], 10);
                }
            }
        }
    }

    [Fact]
    public void Resize_PadsWithZerosAndCrops()
    {
        double[,] a = Ramp(3, 3);
        double[,] bigger = CosineTransforms.Resize(a, 4, 5);
        Assert.Equal(a[2, 2], bigger[2, 2]);
        Assert.Equal(0.0, bigger[3, 4]);

        double[,] smaller = CosineTransforms.Resize(a, 2, 2);
        Assert.Equal(2, smaller.GetLength(0));
        Assert.Equal(a[1, 1], smaller[1, 1]);
    }

    // --- Radon ---------------------------------------------------------------------------------

    [Fact]
    public void Radon_ConservesTheTotalAtEveryAngle()
    {
        // A shadow of a thing weighs what the thing weighs, whichever way the light comes from.
        // Splitting each pixel between the two bins it straddles is what buys this.
        double[,] image = Ramp(12, 9);
        double total = 0;
        foreach (double value in image)
        {
            total += value;
        }

        double[] angles = [0, 17, 45, 90, 133, 179];
        (double[,] sinogram, double[] coordinates) = RadonTransform.Forward(image, angles);

        Assert.Equal(RadonTransform.ProjectionLength(12, 9), sinogram.GetLength(0));
        Assert.Equal(angles.Length, sinogram.GetLength(1));
        Assert.Equal(-coordinates[^1], coordinates[0], 12);
        Assert.Equal(1.0, coordinates[1] - coordinates[0], 12);

        for (int a = 0; a < angles.Length; a++)
        {
            double column = 0;
            for (int i = 0; i < sinogram.GetLength(0); i++)
            {
                column += sinogram[i, a];
            }

            Assert.Equal(total, column, 8);
        }
    }

    [Fact]
    public void Radon_OfACentredPoint_LandsOnTheCentreBin()
    {
        var point = new double[9, 9];
        point[4, 4] = 1.0;
        (double[,] sinogram, double[] coordinates) = RadonTransform.Forward(point, [0, 30, 90]);

        int centre = Array.IndexOf(coordinates, 0.0);
        Assert.True(centre >= 0, "the bin coordinates should include zero");
        for (int a = 0; a < 3; a++)
        {
            // The pixel sits on the axis of rotation, so no angle moves it off the middle bin.
            Assert.Equal(1.0, sinogram[centre, a], 10);
        }
    }

    [Fact]
    public void Iradon_ReconstructsTheHeadPhantom()
    {
        double[,] phantom = Phantoms.Draw(Phantoms.ModifiedSheppLogan, 128);
        var angles = new double[180];
        for (int i = 0; i < 180; i++)
        {
            angles[i] = i;
        }

        (double[,] sinogram, _) = RadonTransform.Forward(phantom, angles);
        (double[,] reconstruction, double[] response) = RadonTransform.Inverse(
            sinogram, angles, RadonTransform.Interpolation.Linear, RadonTransform.Filter.RamLak, 1.0, 128);

        // The ramp is all but zero at DC, which is the whole reason it is built from its impulse
        // response: sampling |w| directly leaves a constant offset over the reconstruction. What is
        // left is the truncated tail of a series that sums to exactly zero, three orders below the
        // band's own weight.
        Assert.True(Math.Abs(response[0]) < 0.01 * response[response.Length / 2],
            $"the ramp should pass almost nothing at DC, but it passed {response[0]:F4}");

        double error = 0;
        double energy = 0;
        for (int r = 0; r < 128; r++)
        {
            for (int c = 0; c < 128; c++)
            {
                double difference = reconstruction[r, c] - phantom[r, c];
                error += difference * difference;
                energy += phantom[r, c] * phantom[r, c];
            }
        }

        // What error remains is almost all on the skull, a shell two pixels thick that a hundred and
        // eighty projections through ninety-odd bins cannot resolve to its own edge.
        Assert.True(Math.Sqrt(error / energy) < 0.25,
            $"the reconstruction should follow the phantom, but the relative error was {Math.Sqrt(error / energy):F3}");

        // The interior reads 0.2, the background nothing, and the total is conserved: the three
        // things a reconstruction has to get right before sharpness is worth discussing.
        Assert.Equal(0.2, reconstruction[64, 64], 1);
        Assert.True(Math.Abs(reconstruction[2, 2]) < 0.05, $"the corner read {reconstruction[2, 2]:F3}");

        double reconstructed = 0;
        double original = 0;
        foreach (double value in reconstruction)
        {
            reconstructed += value;
        }

        foreach (double value in phantom)
        {
            original += value;
        }

        Assert.True(Math.Abs(reconstructed - original) < 0.02 * original,
            $"the reconstruction totals {reconstructed:F1} against the phantom's {original:F1}");
    }

    [Fact]
    public void Iradon_WithNoFilter_IsBlurrier()
    {
        // Plain backprojection counts the low frequencies once per angle; the picture survives, but
        // smeared. Saying so numerically is what makes the ramp's job visible.
        double[,] phantom = Phantoms.Draw(Phantoms.ModifiedSheppLogan, 48);
        var angles = new double[120];
        for (int i = 0; i < 120; i++)
        {
            angles[i] = i * 1.5;
        }

        (double[,] sinogram, _) = RadonTransform.Forward(phantom, angles);
        double filtered = Roughness(RadonTransform.Inverse(
            sinogram, angles, RadonTransform.Interpolation.Linear,
            RadonTransform.Filter.RamLak, 1.0, 48).Image);
        double unfiltered = Roughness(RadonTransform.Inverse(
            sinogram, angles, RadonTransform.Interpolation.Linear,
            RadonTransform.Filter.None, 1.0, 48).Image);

        Assert.True(filtered > unfiltered * 2,
            $"the filtered reconstruction should be sharper ({filtered:F4} against {unfiltered:F4})");
    }

    [Fact]
    public void FilterResponse_IsWindowedAndBandLimited()
    {
        var flat = new double[65, 4];
        double[] angles = [0, 45, 90, 135];
        double[] ramLak = RadonTransform.Inverse(
            flat, angles, RadonTransform.Interpolation.Linear,
            RadonTransform.Filter.RamLak, 1.0, 8).FilterResponse;
        double[] hann = RadonTransform.Inverse(
            flat, angles, RadonTransform.Interpolation.Linear,
            RadonTransform.Filter.Hann, 1.0, 8).FilterResponse;

        // A window only ever takes away, and it takes away most at the top of the band.
        int top = ramLak.Length / 2;
        Assert.True(hann[top] < ramLak[top] * 0.1);
        Assert.True(hann[4] < ramLak[4]);

        // Halving the frequency scaling throws away everything above half the band.
        double[] narrow = RadonTransform.Inverse(
            flat, angles, RadonTransform.Interpolation.Linear,
            RadonTransform.Filter.RamLak, 0.5, 8).FilterResponse;
        Assert.Equal(0.0, narrow[top]);
        Assert.True(narrow[top / 2] > 0);
    }

    // --- The phantom ---------------------------------------------------------------------------

    [Fact]
    public void Phantom_DrawsASkullAroundADarkerInterior()
    {
        double[,] p = Phantoms.Draw(Phantoms.ModifiedSheppLogan, 128);
        Assert.Equal(128, p.GetLength(0));

        // The outer ellipse is 1 and the inner one subtracts 0.8, so the shell is bright and what it
        // encloses is dim — and the corner is outside everything. The shell is thin: the two ellipses
        // differ by 0.0276 of a half-width, which is under two pixels here.
        Assert.Equal(0.0, p[2, 2], 12);
        Assert.Equal(0.2, p[64, 64], 10);

        double brightest = 0;
        for (int c = 0; c < 128; c++)
        {
            brightest = Math.Max(brightest, p[64, c]);
        }

        Assert.Equal(1.0, brightest, 10);

        // The three small ellipses low in the picture are the reason the phantom exists: one part in
        // ten above their surroundings, and a reconstruction that loses them has failed.
        Assert.Equal(0.3, p[45, 64], 10);
    }

    [Fact]
    public void Phantom_OriginalAndModified_ShareTheirGeometryOnly()
    {
        double[,] original = Phantoms.SheppLogan;
        double[,] modified = Phantoms.ModifiedSheppLogan;
        Assert.Equal(10, original.GetLength(0));
        Assert.Equal(6, original.GetLength(1));
        Assert.Equal(-0.98, original[1, 0], 12);
        Assert.Equal(-0.8, modified[1, 0], 12);
        for (int e = 0; e < 10; e++)
        {
            for (int k = 1; k < 6; k++)
            {
                Assert.Equal(original[e, k], modified[e, k], 12);
            }
        }
    }

    // --- Quadtrees -----------------------------------------------------------------------------

    [Fact]
    public void Quadtree_KeepsUniformBlocksWholeAndSplitsTheRest()
    {
        // Left half flat, right half a checkerboard: the left survives as one block per quadrant and
        // the right is chased all the way down to single pixels.
        var image = new double[8, 8];
        for (int r = 0; r < 8; r++)
        {
            for (int c = 4; c < 8; c++)
            {
                image[r, c] = (r + c) % 2 == 0 ? 1.0 : 0.0;
            }
        }

        int[,] sizes = Quadtree.Decompose(image, blocks => Quadtree.SpreadTest(blocks, 0), 1, 8);

        Assert.Equal(4, sizes[0, 0]);
        Assert.Equal(4, sizes[4, 0]);
        Assert.Equal(1, sizes[0, 4]);
        Assert.Equal(1, sizes[7, 7]);
        Assert.Equal(0, sizes[1, 1]);

        // Every entry is the corner of a block, and the blocks tile the square exactly.
        int covered = 0;
        foreach (int size in sizes)
        {
            covered += size * size;
        }

        Assert.Equal(64, covered);
    }

    [Fact]
    public void Quadtree_HonoursTheSmallestAndLargestBlock()
    {
        var noisy = new double[8, 8];
        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                noisy[r, c] = (r * 8) + c;
            }
        }

        // Nothing here is uniform, so only the floor stops the split.
        int[,] floored = Quadtree.Decompose(noisy, blocks => Quadtree.SpreadTest(blocks, 0), 2, 8);
        Assert.Equal(2, floored[0, 0]);
        Assert.Equal(2, floored[6, 6]);

        // A ceiling below the picture's side splits the top levels without asking.
        var flat = new double[8, 8];
        int[,] capped = Quadtree.Decompose(flat, blocks => Quadtree.SpreadTest(blocks, 0), 1, 4);
        Assert.Equal(4, capped[0, 0]);
        Assert.Equal(4, capped[4, 4]);
    }

    [Fact]
    public void Quadtree_RejectsSizesItCannotHalveInto()
    {
        Assert.Throws<ArgumentException>(() =>
            Quadtree.Decompose(new double[8, 6], b => Quadtree.SpreadTest(b, 0), 1, 8));
        Assert.Throws<ArgumentException>(() =>
            Quadtree.Decompose(new double[12, 12], b => Quadtree.SpreadTest(b, 0), 1, 12));
    }

    [Fact]
    public void Quadtree_BlocksReadBackAndWriteThrough()
    {
        var image = new double[4, 4];
        for (int r = 0; r < 4; r++)
        {
            for (int c = 0; c < 4; c++)
            {
                image[r, c] = (r * 4) + c;
            }
        }

        int[,] sizes = Quadtree.Decompose(image, blocks => Quadtree.SpreadTest(blocks, 0), 2, 4);
        IReadOnlyList<(int Row, int Col)> corners = Quadtree.Corners(sizes, 2);
        Assert.Equal(4, corners.Count);

        // Column-major order, the order a sparse matrix is walked in.
        Assert.Equal((0, 0), corners[0]);
        Assert.Equal((2, 0), corners[1]);
        Assert.Equal((0, 2), corners[2]);

        double[][] blocks = Quadtree.Read(image, corners, 2);
        Assert.Equal([0, 1, 4, 5], blocks[0]);

        double[][] replaced = blocks.Select(b => b.Select(_ => -1.0).ToArray()).ToArray();
        double[,] written = Quadtree.Write(image, corners, 2, replaced);
        Assert.Equal(-1.0, written[3, 3]);
        Assert.Equal(0.0, image[0, 0]); // the original is untouched
    }

    // --- Correlation ---------------------------------------------------------------------------

    [Fact]
    public void Normxcorr2_PeaksWhereTheTemplateCameFrom()
    {
        var image = new double[20, 24];
        var random = new Random(11);
        for (int r = 0; r < 20; r++)
        {
            for (int c = 0; c < 24; c++)
            {
                image[r, c] = random.NextDouble();
            }
        }

        var template = new double[5, 6];
        for (int r = 0; r < 5; r++)
        {
            for (int c = 0; c < 6; c++)
            {
                template[r, c] = image[7 + r, 9 + c];
            }
        }

        double[,] surface = Correlation.Normalized(template, image);
        Assert.Equal(24, surface.GetLength(0));
        Assert.Equal(29, surface.GetLength(1));

        (int peakRow, int peakCol, double peak) = Peak(surface);
        Assert.Equal(1.0, peak, 8);

        // Offset zero is where only the template's last pixel overlaps the picture's first, so the
        // peak sits at the template's bottom-right corner in the picture.
        Assert.Equal(7 + 5 - 1, peakRow);
        Assert.Equal(9 + 6 - 1, peakCol);
    }

    [Fact]
    public void Normxcorr2_IgnoresBrightnessAndContrast()
    {
        // The normalization is the point: the same shape twice as bright and lifted off zero must
        // still score one, or the answer would just be "where is the picture brightest".
        var image = new double[12, 12];
        for (int r = 4; r < 8; r++)
        {
            for (int c = 5; c < 9; c++)
            {
                image[r, c] = 0.2 + (0.05 * ((r * 3) + c));
            }
        }

        var template = new double[4, 4];
        for (int r = 0; r < 4; r++)
        {
            for (int c = 0; c < 4; c++)
            {
                template[r, c] = 5.0 + (2.0 * (0.2 + (0.05 * (((r + 4) * 3) + c + 5))));
            }
        }

        (int peakRow, int peakCol, double peak) = Peak(Correlation.Normalized(template, image));
        Assert.Equal(1.0, peak, 6);
        Assert.Equal(7, peakRow);
        Assert.Equal(8, peakCol);
    }

    [Fact]
    public void Normxcorr2_OfAFlatTemplate_AnswersNothingRatherThanDividingByNothing()
    {
        double[,] surface = Correlation.Normalized(new double[3, 3], Ramp(8, 8));
        foreach (double value in surface)
        {
            Assert.Equal(0.0, value);
        }
    }

    [Fact]
    public void Normxcorr2_RefusesATemplateLargerThanThePicture() =>
        Assert.Throws<ArgumentException>(() => Correlation.Normalized(new double[9, 9], new double[4, 4]));

    [Fact]
    public void Register_FindsAPureTranslationExactly()
    {
        // The phantom, because phase correlation weights every frequency equally and a picture with
        // nothing at the top of its band has nothing there but rounding error to correlate.
        double[,] scene = Phantoms.Draw(Phantoms.ModifiedSheppLogan, 64);
        double[,] shifted = Shifted(scene, 7, -5);

        (double scale, double rotation, double dx, double dy, _) =
            Correlation.Register(shifted, scene, allowRotation: false, allowScale: false);

        // The scene was pushed down seven and left five, so putting it back means up seven, right five.
        Assert.Equal(1.0, scale);
        Assert.Equal(0.0, rotation);
        Assert.Equal(5, dx, 6);
        Assert.Equal(-7, dy, 6);
    }

    [Fact]
    public void Register_RecoversARotation()
    {
        double[,] scene = Phantoms.Draw(Phantoms.ModifiedSheppLogan, 96);
        double[,] turned = Rotated(scene, 20);

        (_, double rotation, _, _, _) =
            Correlation.Register(turned, scene, allowRotation: true, allowScale: false);

        // The log-polar match is quantized to the angle bins it resamples on, so a degree is the
        // resolution rather than the error. Getting the sign right is the harder half: the spectrum
        // is symmetric through the origin, so a turn and the same turn plus half a circle look
        // identical there and only the pictures themselves can tell them apart.
        Assert.True(Math.Abs(rotation + 20) < 2.0,
            $"expected about -20 degrees to undo the turn, but got {rotation:F2}");
    }

    // --- Helpers -------------------------------------------------------------------------------

    private static double[,] Ramp(int rows, int cols)
    {
        var values = new double[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                values[r, c] = ((r * cols) + c) / (double)(rows * cols);
            }
        }

        return values;
    }

    /// <summary>
    /// Average step between neighbouring rows, as a fraction of the picture's own range —
    /// normalized because an unfiltered backprojection is enormously brighter than a filtered one,
    /// and comparing raw gradients would measure that instead of sharpness.
    /// </summary>
    private static double Roughness(double[,] image)
    {
        double low = double.PositiveInfinity;
        double high = double.NegativeInfinity;
        foreach (double value in image)
        {
            low = Math.Min(low, value);
            high = Math.Max(high, value);
        }

        double total = 0;
        for (int r = 1; r < image.GetLength(0); r++)
        {
            for (int c = 0; c < image.GetLength(1); c++)
            {
                total += Math.Abs(image[r, c] - image[r - 1, c]);
            }
        }

        return total / image.Length / Math.Max(high - low, 1e-12);
    }

    private static (int Row, int Col, double Value) Peak(double[,] surface)
    {
        int bestRow = 0;
        int bestCol = 0;
        double best = double.NegativeInfinity;
        for (int r = 0; r < surface.GetLength(0); r++)
        {
            for (int c = 0; c < surface.GetLength(1); c++)
            {
                if (surface[r, c] > best)
                {
                    best = surface[r, c];
                    bestRow = r;
                    bestCol = c;
                }
            }
        }

        return (bestRow, bestCol, best);
    }

    private static double[,] Shifted(double[,] image, int downBy, int rightBy)
    {
        int rows = image.GetLength(0);
        int cols = image.GetLength(1);
        var moved = new double[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int source = r - downBy;
                int sourceCol = c - rightBy;
                if (source >= 0 && source < rows && sourceCol >= 0 && sourceCol < cols)
                {
                    moved[r, c] = image[source, sourceCol];
                }
            }
        }

        return moved;
    }

    private static double[,] Rotated(double[,] image, double degrees)
    {
        int rows = image.GetLength(0);
        int cols = image.GetLength(1);
        double radians = degrees * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        double centreRow = (rows - 1) / 2.0;
        double centreCol = (cols - 1) / 2.0;

        var turned = new double[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            double y = r - centreRow;
            for (int c = 0; c < cols; c++)
            {
                double x = c - centreCol;
                double sourceX = (x * cos) + (y * sin);
                double sourceY = (-x * sin) + (y * cos);
                int sr = (int)Math.Round(sourceY + centreRow);
                int sc = (int)Math.Round(sourceX + centreCol);
                if (sr >= 0 && sr < rows && sc >= 0 && sc < cols)
                {
                    turned[r, c] = image[sr, sc];
                }
            }
        }

        return turned;
    }
}
