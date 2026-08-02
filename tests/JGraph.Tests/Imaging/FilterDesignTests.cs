using System;
using System.Numerics;
using JGraph.Imaging;
using Xunit;

namespace JGraph.Tests.Imaging;

/// <summary>
/// M46 wave I: two-dimensional filter design, the spread-function/transfer-function pair, the four
/// deblurring methods, and Gabor filtering.
/// </summary>
public sealed class FilterDesignTests
{
    // --- Frequency grids -----------------------------------------------------------------------

    [Fact]
    public void Axis_PutsASampleOnZeroOnlyWhenThereIsAMiddleOne()
    {
        double[] odd = FilterDesign.Axis(5);
        Assert.Equal(5, odd.Length);
        Assert.Equal(0.0, odd[2], 12);
        Assert.Equal(-0.8, odd[0], 12);

        double[] even = FilterDesign.Axis(4);
        Assert.Equal(4, even.Length);
        Assert.Equal(-1.0, even[0], 12);
        Assert.Equal(0.0, even[2], 12);
        Assert.Equal(0.5, even[1] - even[0], 12);
    }

    [Fact]
    public void HalfAxis_ListsTheDistinctHalfOfTheCircle()
    {
        Assert.Equal(new[] { 0.0, 0.25, 0.5, 0.75 }, FilterDesign.HalfAxis(8));
        Assert.Equal(3, FilterDesign.HalfAxis(5).Length);
    }

    // --- Frequency response --------------------------------------------------------------------

    [Fact]
    public void Response_OfASmoothingKernel_IsOneAtDcAndFallsAway()
    {
        // Any kernel that sums to one passes a flat field through untouched, which is the response at
        // zero frequency. A Gaussian then falls away monotonically — a box filter does not, because
        // its response is a ratio of sines that keeps crossing zero.
        Complex[,] box = FilterDesign.Response(Kernels.Average(5), [0.0], [0.0]);
        Assert.Equal(1.0, box[0, 0].Real, 12);
        Assert.Equal(0.0, box[0, 0].Imaginary, 12);

        Complex[,] gaussian = FilterDesign.Response(Kernels.Gaussian(9, 9, 1.5), [0.0, 0.25, 0.5, 1.0], [0.0]);
        Assert.Equal(1.0, gaussian[0, 0].Real, 8);
        for (int i = 1; i < 4; i++)
        {
            Assert.True(gaussian[0, i].Magnitude < gaussian[0, i - 1].Magnitude);
        }
    }

    [Fact]
    public void Response_OfASymmetricKernel_IsReal()
    {
        double[,] gaussian = Kernels.Gaussian(7, 7, 1.2);
        Complex[,] h = FilterDesign.Response(gaussian, FilterDesign.Axis(8), FilterDesign.Axis(8));
        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                Assert.Equal(0.0, h[r, c].Imaginary, 10);
            }
        }
    }

    // --- Frequency sampling --------------------------------------------------------------------

    [Fact]
    public void FromSamples_InvertsResponse()
    {
        // Design a filter from a sampled response, then measure the response of what came out: at the
        // sample points the two must agree, because that is the only promise frequency sampling makes.
        double[] axis = FilterDesign.Axis(8);
        var desired = new double[8, 8];
        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                desired[r, c] = Math.Sqrt((axis[r] * axis[r]) + (axis[c] * axis[c])) <= 0.5 ? 1 : 0;
            }
        }

        double[,] kernel = FilterDesign.FromSamples(desired);
        Complex[,] measured = FilterDesign.Response(kernel, axis, axis);
        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                Assert.Equal(desired[r, c], measured[r, c].Real, 10);
            }
        }
    }

    [Fact]
    public void FromSamples_AtNamedFrequencies_AgreesWithTheGridForm()
    {
        // The two forms of one function: the transform and the sum it stands for. Handed the grid the
        // transform assumes, the sum has to reproduce it term for term.
        double[] axis = FilterDesign.Axis(6);
        var desired = new double[6, 6];
        for (int r = 0; r < 6; r++)
        {
            for (int c = 0; c < 6; c++)
            {
                desired[r, c] = 1.0 / (1 + (4 * ((axis[r] * axis[r]) + (axis[c] * axis[c]))));
            }
        }

        double[,] byTransform = FilterDesign.FromSamples(desired);
        double[,] bySum = FilterDesign.FromSamples(axis, axis, desired, 6, 6);
        for (int r = 0; r < 6; r++)
        {
            for (int c = 0; c < 6; c++)
            {
                Assert.Equal(byTransform[r, c], bySum[r, c], 10);
            }
        }
    }

    // --- Windowed design -----------------------------------------------------------------------

    [Fact]
    public void Windowed_RingsLessThanPlainTruncation()
    {
        // The reason windows exist: the same ideal filter, cut off sharply and cut off gently. The
        // gentle one must have the smaller overshoot outside the passband.
        double[] axis = FilterDesign.Axis(32);
        var desired = new double[32, 32];
        for (int r = 0; r < 32; r++)
        {
            for (int c = 0; c < 32; c++)
            {
                desired[r, c] = Math.Sqrt((axis[r] * axis[r]) + (axis[c] * axis[c])) <= 0.4 ? 1 : 0;
            }
        }

        var box = new double[11, 11];
        for (int r = 0; r < 11; r++)
        {
            for (int c = 0; c < 11; c++)
            {
                box[r, c] = 1;
            }
        }

        double[] hamming = new double[11];
        for (int i = 0; i < 11; i++)
        {
            hamming[i] = 0.54 - (0.46 * Math.Cos(2 * Math.PI * i / 10));
        }

        double[,] truncated = FilterDesign.Windowed(desired, box);
        double[,] tapered = FilterDesign.Windowed(desired, FilterDesign.OuterWindow(hamming, hamming));

        Assert.True(Overshoot(tapered, axis) < Overshoot(truncated, axis));
        Assert.Equal(11, tapered.GetLength(0));
    }

    [Fact]
    public void RotateWindow_IsCircularAndZeroInTheCorners()
    {
        double[] hann = new double[9];
        for (int i = 0; i < 9; i++)
        {
            hann[i] = 0.5 - (0.5 * Math.Cos(2 * Math.PI * i / 8));
        }

        double[,] rotated = FilterDesign.RotateWindow(hann);
        Assert.Equal(9, rotated.GetLength(0));
        Assert.Equal(9, rotated.GetLength(1));

        // The middle is the window's peak, the corners are outside the circle, and any two points the
        // same distance from the middle carry the same weight.
        Assert.Equal(1.0, rotated[4, 4], 10);
        Assert.Equal(0.0, rotated[0, 0], 12);
        Assert.Equal(rotated[4, 1], rotated[1, 4], 10);
        Assert.Equal(rotated[4, 1], rotated[7, 4], 10);
    }

    // --- Frequency transformation --------------------------------------------------------------

    [Fact]
    public void FrequencyTransform_TurnsALowpassIntoACircularOne()
    {
        // A 1-D lowpass, mapped onto the plane. What comes out must still be a lowpass, and its
        // response must depend on how far from the origin a frequency is rather than on which way.
        double[] b = HalfBandLowpass(9);
        double[,] h = FilterDesign.FrequencyTransform(b, FilterDesign.McClellan);

        Assert.Equal(9, h.GetLength(0));
        Assert.Equal(9, h.GetLength(1));

        Complex[,] dc = FilterDesign.Response(h, [0.0], [0.0]);
        Assert.Equal(1.0, dc[0, 0].Real, 6);

        // The transformation is only nearly circular — it is a three-by-three approximation to a
        // circle — so the two readings agree to a percent, not to the last digit.
        Complex[,] along = FilterDesign.Response(h, [0.35], [0.0]);
        Complex[,] diagonal = FilterDesign.Response(h, [0.35 / Math.Sqrt(2)], [0.35 / Math.Sqrt(2)]);
        Assert.True(Math.Abs(along[0, 0].Real - diagonal[0, 0].Real) < 0.02,
            $"along {along[0, 0].Real:F4} against diagonal {diagonal[0, 0].Real:F4}");

        Complex[,] corner = FilterDesign.Response(h, [1.0], [1.0]);
        Assert.True(Math.Abs(corner[0, 0].Real) < 0.05);
    }

    [Fact]
    public void FrequencyTransform_RefusesAnEvenLengthFilter()
    {
        Assert.Throws<ArgumentException>(() =>
            FilterDesign.FrequencyTransform([0.25, 0.5, 0.25, 0.1], FilterDesign.McClellan));
    }

    // --- Convolution matrix --------------------------------------------------------------------

    [Fact]
    public void ConvolutionMatrix_MultipliesTheSameWayConv2Filters()
    {
        double[,] kernel = { { 1, 2 }, { 3, 4 } };
        double[,] picture = { { 1, 2, 3 }, { 4, 5, 6 }, { 7, 8, 9 } };

        double[,] direct = Filters.Convolve2(picture, kernel);
        double[,] matrix = FilterDesign.ConvolutionMatrix(kernel, 3, 3);

        Assert.Equal(16, matrix.GetLength(0));
        Assert.Equal(9, matrix.GetLength(1));

        // Read the picture out column by column, multiply, read the answer back the same way.
        for (int outRow = 0; outRow < 4; outRow++)
        {
            for (int outCol = 0; outCol < 4; outCol++)
            {
                double sum = 0;
                for (int c = 0; c < 3; c++)
                {
                    for (int r = 0; r < 3; r++)
                    {
                        sum += matrix[outRow + (outCol * 4), r + (c * 3)] * picture[r, c];
                    }
                }

                Assert.Equal(direct[outRow, outCol], sum, 10);
            }
        }
    }

    [Fact]
    public void ConvolutionMatrix_RefusesToBuildAnAbsurdOne()
    {
        Assert.Throws<ArgumentException>(() =>
            FilterDesign.ConvolutionMatrix(Kernels.Average(3), 512, 512));
    }

    // --- Spread functions and transfer functions -----------------------------------------------

    [Fact]
    public void PsfToOtf_PutsTheCentreTapAtZeroFrequency()
    {
        // A spread function that sums to one has a transfer function of one at DC — no blur changes a
        // picture's average — and a perfectly centred one has no phase at all.
        double[,] psf = Kernels.Gaussian(5, 5, 1.0);
        Complex[] otf = FilterDesign.PsfToOtf(psf, 16, 16);
        Assert.Equal(1.0, otf[0].Real, 10);
        for (int i = 0; i < otf.Length; i++)
        {
            Assert.Equal(0.0, otf[i].Imaginary, 10);
        }
    }

    [Fact]
    public void OtfToPsf_UndoesPsfToOtf()
    {
        double[,] psf = Kernels.Motion(7, 30);
        Complex[] otf = FilterDesign.PsfToOtf(psf, 24, 20);
        double[,] back = FilterDesign.OtfToPsf(otf, 24, 20, psf.GetLength(0), psf.GetLength(1));
        for (int r = 0; r < psf.GetLength(0); r++)
        {
            for (int c = 0; c < psf.GetLength(1); c++)
            {
                Assert.Equal(psf[r, c], back[r, c], 10);
            }
        }
    }

    [Fact]
    public void PsfToOtf_RefusesASpreadFunctionBiggerThanThePicture()
    {
        Assert.Throws<ArgumentException>(() => FilterDesign.PsfToOtf(Kernels.Average(9), 5, 5));
    }

    // --- Edge tapering -------------------------------------------------------------------------

    [Fact]
    public void EdgeTaper_LeavesTheMiddleAloneAndClosesTheSeam()
    {
        // A picture that gets brighter towards the bottom, so its top and bottom rows are genuine
        // strangers — the seam a wrapped deconvolution would ring against.
        var picture = new double[48, 48];
        for (int r = 0; r < 48; r++)
        {
            for (int c = 0; c < 48; c++)
            {
                picture[r, c] = 0.1 + (0.8 * r / 47.0);
            }
        }

        double[,] psf = Kernels.Gaussian(9, 9, 2.0);
        double[,] tapered = FilterDesign.EdgeTaper(picture, psf);

        // Far from any border nothing has moved.
        Assert.Equal(picture[24, 24], tapered[24, 24], 8);

        // At the border it has: the step between the last row and the first, which is what a wrapped
        // deconvolution would ring against, is smaller than it was.
        double before = 0;
        double after = 0;
        for (int c = 0; c < 48; c++)
        {
            before += Math.Abs(picture[0, c] - picture[47, c]);
            after += Math.Abs(tapered[0, c] - tapered[47, c]);
        }

        Assert.True(after < before, $"the seam grew: {before:F3} became {after:F3}");
    }

    // --- Deblurring ----------------------------------------------------------------------------

    [Fact]
    public void Wiener_RecoversAPictureFromANoiselessBlur()
    {
        double[,] original = Wedge(32, 32);
        double[,] psf = Kernels.Gaussian(7, 7, 1.5);
        double[,] blurred = Blur(original, psf);

        double[,] restored = Deconvolution.Wiener(blurred, psf, null, 0.0);
        Assert.True(Error(restored, original) < 1e-8);
        Assert.True(Error(blurred, original) > 1e-3);
    }

    [Fact]
    public void Wiener_WithNoise_NeedsTheRatioToBeatThePlainInverse()
    {
        double[,] original = Wedge(32, 32);
        double[,] psf = Kernels.Gaussian(9, 9, 2.0);
        double[,] blurred = Noisy(Blur(original, psf), 0.004, seed: 7);

        double[,] naive = Deconvolution.Wiener(blurred, psf, null, 0.0);
        double[,] guarded = Deconvolution.Wiener(blurred, psf, null, 0.01);

        Assert.True(Error(guarded, original) < Error(naive, original));
        Assert.True(Error(guarded, original) < Error(blurred, original));
    }

    [Fact]
    public void Regularized_SaysMoreNoiseWithABiggerMultiplierAndASmootherAnswer()
    {
        // The multiplier is not a knob the caller turns; it is solved for from how much noise the
        // caller says is there. Claim more noise and less of the data is believed, which shows up as
        // a larger multiplier and a smoother answer.
        double[,] original = Wedge(32, 32);
        double[,] psf = Kernels.Gaussian(9, 9, 2.0);
        double[,] blurred = Noisy(Blur(original, psf), 0.004, seed: 11);

        (double[,] loose, double looseLambda) = Deconvolution.Regularized(
            blurred, psf, 0.02, 1e-9, 1e9, Deconvolution.Laplacian);
        (double[,] tight, double tightLambda) = Deconvolution.Regularized(
            blurred, psf, 0.0005, 1e-9, 1e9, Deconvolution.Laplacian);

        Assert.True(tightLambda < looseLambda, $"{tightLambda:E2} was not below {looseLambda:E2}");
        Assert.True(Roughness(loose) < Roughness(tight));
    }

    [Fact]
    public void Regularized_WithNoNoiseStated_BarelyRegularizesAtAll()
    {
        double[,] original = Wedge(32, 32);
        double[,] psf = Kernels.Gaussian(7, 7, 1.5);
        double[,] blurred = Blur(original, psf);
        (double[,] restored, double lambda) = Deconvolution.Regularized(
            blurred, psf, 0.0, 1e-9, 1e9, Deconvolution.Laplacian);

        Assert.Equal(1e-9, lambda, 15);
        Assert.True(Error(restored, original) < Error(blurred, original) / 100);
    }

    [Fact]
    public void Lucy_SharpensTowardsTheOriginalAndKeepsItPositive()
    {
        double[,] original = Wedge(32, 32);
        double[,] psf = Kernels.Gaussian(7, 7, 1.5);
        double[,] blurred = Blur(original, psf);

        double[,] restored = Deconvolution.Lucy(blurred, psf, 20, 0, null, 0);
        Assert.True(Error(restored, original) < Error(blurred, original));
        foreach (double value in restored)
        {
            Assert.True(value >= 0);
        }
    }

    [Fact]
    public void Lucy_KeepsTheTotalBrightness()
    {
        // The property that makes it the method for counted light: multiplying can move brightness
        // around but never creates or destroys it.
        double[,] original = Wedge(24, 24);
        double[,] psf = Kernels.Gaussian(5, 5, 1.2);
        double[,] blurred = Blur(original, psf);

        double[,] restored = Deconvolution.Lucy(blurred, psf, 15, 0, null, 0);
        Assert.Equal(Total(blurred), Total(restored), 6);
    }

    [Fact]
    public void Lucy_WithDamping_MovesSmoothAreasLess()
    {
        double[,] original = Wedge(32, 32);
        double[,] psf = Kernels.Gaussian(7, 7, 1.5);
        double[,] blurred = Noisy(Blur(original, psf), 0.002, seed: 3);

        double[,] plain = Deconvolution.Lucy(blurred, psf, 20, 0, null, 0);
        double[,] damped = Deconvolution.Lucy(blurred, psf, 20, 0.2, null, 0);

        Assert.True(Roughness(damped) < Roughness(plain));
    }

    [Fact]
    public void Blind_ReducesToLucyWhenTheBlurIsAlreadyKnown()
    {
        // Handed the right blur, the blind form must not wander off it: the picture improves the way
        // it would if the blur were being held fixed, and the blur it hands back is still the one it
        // was given. This is what says the two halves of the alternation agree with each other.
        double[,] original = Wedge(32, 32);
        double[,] psf = Kernels.Gaussian(7, 7, 1.4);
        double[,] blurred = Blur(original, psf);

        (double[,] restored, double[,] found) = Deconvolution.Blind(blurred, psf, 10, 0, null, 0);

        Assert.True(Error(restored, original) < Error(blurred, original) / 1.5);
        Assert.Equal(1.0, Total(found), 8);
        Assert.True(Math.Abs(found[3, 3] - psf[3, 3]) < 0.05 * psf[3, 3]);
    }

    [Fact]
    public void Blind_MovesAFlatGuessTowardsAConcentratedBlur()
    {
        // From a guess that says nothing — every tap equal — the alternation must at least learn that
        // the middle of a blur matters more than its corners. It learns it slowly: a blur that is far
        // too wide lets the picture over-sharpen faster than the blur can narrow, which is why this is
        // read after a few rounds rather than after a hundred.
        double[,] original = Wedge(32, 32);
        double[,] blurred = Blur(original, Kernels.Gaussian(7, 7, 1.4));

        var guess = new double[7, 7];
        for (int r = 0; r < 7; r++)
        {
            for (int c = 0; c < 7; c++)
            {
                guess[r, c] = 1.0 / 49;
            }
        }

        (double[,] restored, double[,] found) = Deconvolution.Blind(blurred, guess, 5, 0, null, 0);

        Assert.Equal(1.0, Total(found), 8);
        Assert.True(found[3, 3] > 1.0 / 49, $"the middle tap stayed at {found[3, 3]:F5}");
        Assert.True(found[0, 0] < 1.0 / 49, $"the corner tap stayed at {found[0, 0]:F5}");
        foreach (double value in restored)
        {
            Assert.True(value >= 0);
        }
    }

    // --- Gabor ---------------------------------------------------------------------------------

    [Fact]
    public void GaborKernel_HasAlmostNoAverage()
    {
        // A Gabor filter answers about a wavelength, so it must not answer about a flat field. The
        // envelope leaves a little through, but only a little.
        (double[,] real, double[,] imaginary) = GaborFilters.Kernel(new GaborParameters(6, 0));
        Assert.True(Math.Abs(Total(real)) < 0.05);
        Assert.Equal(0.0, Total(imaginary), 10);
    }

    [Fact]
    public void GaborKernel_TurnsWithItsOrientation()
    {
        (double[,] flat, _) = GaborFilters.Kernel(new GaborParameters(8, 0));
        (double[,] turned, _) = GaborFilters.Kernel(new GaborParameters(8, 90));

        // Ninety degrees swaps the axes, so one kernel is the other transposed.
        Assert.Equal(flat.GetLength(0), turned.GetLength(1));
        Assert.Equal(flat.GetLength(1), turned.GetLength(0));
        for (int r = 0; r < turned.GetLength(0); r++)
        {
            for (int c = 0; c < turned.GetLength(1); c++)
            {
                Assert.Equal(flat[c, r], turned[r, c], 10);
            }
        }
    }

    [Fact]
    public void GaborFilter_AnswersLoudestForItsOwnWavelengthAndDirection()
    {
        // Vertical stripes eight pixels apart: the filter tuned to them must beat one tuned across
        // them and one tuned to the wrong wavelength.
        var stripes = new double[64, 64];
        for (int r = 0; r < 64; r++)
        {
            for (int c = 0; c < 64; c++)
            {
                stripes[r, c] = 0.5 + (0.5 * Math.Cos(2 * Math.PI * c / 8));
            }
        }

        double tuned = Middle(GaborFilters.Apply(stripes, new GaborParameters(8, 0)).Magnitude);
        double across = Middle(GaborFilters.Apply(stripes, new GaborParameters(8, 90)).Magnitude);
        double wrong = Middle(GaborFilters.Apply(stripes, new GaborParameters(24, 0)).Magnitude);

        Assert.True(tuned > 4 * across, $"tuned {tuned:F4} against across {across:F4}");
        Assert.True(tuned > wrong, $"tuned {tuned:F4} against wrong wavelength {wrong:F4}");
    }

    [Fact]
    public void GaborFilter_MagnitudeIsSteadyAcrossAStripePattern()
    {
        // The point of taking a magnitude: the response does not flicker with the stripes the way the
        // real part alone would, so it can be thresholded.
        var stripes = new double[48, 48];
        for (int r = 0; r < 48; r++)
        {
            for (int c = 0; c < 48; c++)
            {
                stripes[r, c] = 0.5 + (0.5 * Math.Cos(2 * Math.PI * c / 6));
            }
        }

        (double[,] magnitude, _) = GaborFilters.Apply(stripes, new GaborParameters(6, 0));
        double low = double.MaxValue;
        double high = 0;
        for (int c = 12; c < 36; c++)
        {
            low = Math.Min(low, magnitude[24, c]);
            high = Math.Max(high, magnitude[24, c]);
        }

        Assert.True(high - low < 0.05 * high, $"the magnitude ran from {low:F4} to {high:F4}");
    }

    [Fact]
    public void GaborParameters_RefuseAWavelengthTooShortToSample()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GaborFilters.Kernel(new GaborParameters(1, 0)));
    }

    // --- Fixtures ------------------------------------------------------------------------------

    /// <summary>A picture with edges in it: a bright block on a ramp, so blurring visibly costs something.</summary>
    private static double[,] Wedge(int rows, int cols)
    {
        var picture = new double[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                picture[r, c] = 0.2 + (0.3 * c / cols);
                if (r > rows / 4 && r < rows / 2 && c > cols / 4 && c < 3 * cols / 4)
                {
                    picture[r, c] = 0.9;
                }
            }
        }

        return picture;
    }

    private static double[,] Blur(double[,] picture, double[,] psf)
    {
        int rows = picture.GetLength(0);
        int cols = picture.GetLength(1);
        Complex[] transfer = FilterDesign.PsfToOtf(psf, rows, cols);
        Complex[] spectrum = FourierGrid.Forward(Flatten(picture), rows, cols);
        for (int i = 0; i < spectrum.Length; i++)
        {
            spectrum[i] *= transfer[i];
        }

        FourierGrid.Transform(spectrum, rows, cols, inverse: true);
        var blurred = new double[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                blurred[r, c] = spectrum[(r * cols) + c].Real;
            }
        }

        return blurred;
    }

    private static double[,] Noisy(double[,] picture, double sigma, int seed)
    {
        var random = new Random(seed);
        int rows = picture.GetLength(0);
        int cols = picture.GetLength(1);
        var noisy = new double[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                double u = Math.Max(1e-12, random.NextDouble());
                double v = random.NextDouble();
                noisy[r, c] = picture[r, c] +
                    (sigma * Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v));
            }
        }

        return noisy;
    }

    private static double[] Flatten(double[,] values)
    {
        int rows = values.GetLength(0);
        int cols = values.GetLength(1);
        var flat = new double[rows * cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                flat[(r * cols) + c] = values[r, c];
            }
        }

        return flat;
    }

    private static double Error(double[,] a, double[,] b)
    {
        double sum = 0;
        for (int r = 0; r < a.GetLength(0); r++)
        {
            for (int c = 0; c < a.GetLength(1); c++)
            {
                double difference = a[r, c] - b[r, c];
                sum += difference * difference;
            }
        }

        return sum / (a.GetLength(0) * a.GetLength(1));
    }

    private static double Total(double[,] values)
    {
        double sum = 0;
        foreach (double value in values)
        {
            sum += value;
        }

        return sum;
    }

    private static double Roughness(double[,] values)
    {
        double sum = 0;
        for (int r = 1; r < values.GetLength(0); r++)
        {
            for (int c = 1; c < values.GetLength(1); c++)
            {
                sum += Math.Abs(values[r, c] - values[r - 1, c]) +
                       Math.Abs(values[r, c] - values[r, c - 1]);
            }
        }

        return sum;
    }

    private static double Middle(double[,] values) =>
        values[values.GetLength(0) / 2, values.GetLength(1) / 2];

    /// <summary>The overshoot outside the passband: how far the response strays above zero where it should be zero.</summary>
    private static double Overshoot(double[,] kernel, double[] axis)
    {
        Complex[,] response = FilterDesign.Response(kernel, axis, axis);
        double worst = 0;
        for (int r = 0; r < axis.Length; r++)
        {
            for (int c = 0; c < axis.Length; c++)
            {
                if (Math.Sqrt((axis[r] * axis[r]) + (axis[c] * axis[c])) > 0.6)
                {
                    worst = Math.Max(worst, Math.Abs(response[r, c].Real));
                }
            }
        }

        return worst;
    }

    /// <summary>A windowed half-band lowpass: the 1-D filter the frequency transformation is fed.</summary>
    private static double[] HalfBandLowpass(int taps)
    {
        int half = (taps - 1) / 2;
        var b = new double[taps];
        double total = 0;
        for (int i = 0; i < taps; i++)
        {
            int n = i - half;
            double ideal = n == 0 ? 0.5 : Math.Sin(Math.PI * n / 2) / (Math.PI * n);
            double window = 0.54 - (0.46 * Math.Cos(2 * Math.PI * i / (taps - 1)));
            b[i] = ideal * window;
            total += b[i];
        }

        for (int i = 0; i < taps; i++)
        {
            b[i] /= total;
        }

        return b;
    }
}
