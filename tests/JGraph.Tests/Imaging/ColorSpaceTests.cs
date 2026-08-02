using JGraph.Imaging;
using Xunit;

namespace JGraph.Tests.Imaging;

/// <summary>
/// M46 wave D's colour science: the CIE chain and its white points, the transmission spaces, the
/// difference metrics against published test data, white balance, and the palette machinery.
/// </summary>
public sealed class ColorSpaceTests
{
    private static double[,] Triple(double a, double b, double c) => new[,] { { a, b, c } };

    [Fact]
    public void WhitePoint_KnowsTheStandardIlluminants()
    {
        Assert.Equal(0.9504, ColorSpaces.WhitePoint("d65")[0], 6);
        Assert.Equal(1.0, ColorSpaces.WhitePoint("d65")[1], 12);
        Assert.Equal(0.8249, ColorSpaces.WhitePoint("d50")[2], 6);
        Assert.Equal(1.0, ColorSpaces.WhitePoint("e")[2], 12);
        Assert.Throws<ArgumentException>(() => ColorSpaces.WhitePoint("d99"));
    }

    [Fact]
    public void RgbToXyz_MapsWhiteExactlyOntoTheWhitePoint()
    {
        double[] d65 = ColorSpaces.WhitePoint("d65");
        double[,] xyz = ColorSpaces.RgbToXyz(Triple(1, 1, 1), RgbColorSpace.Srgb, d65);
        Assert.Equal(d65[0], xyz[0, 0], 10);
        Assert.Equal(d65[1], xyz[0, 1], 10);
        Assert.Equal(d65[2], xyz[0, 2], 10);

        // Black is black in every space.
        double[,] black = ColorSpaces.RgbToXyz(Triple(0, 0, 0), RgbColorSpace.Srgb, d65);
        Assert.Equal(0.0, black[0, 1], 12);
    }

    [Fact]
    public void RgbToLab_PutsWhiteAtOneHundredAndNeutralGreyOnTheAxis()
    {
        double[] d65 = ColorSpaces.WhitePoint("d65");
        double[,] white = ColorSpaces.RgbToLab(Triple(1, 1, 1), RgbColorSpace.Srgb, d65);
        Assert.Equal(100.0, white[0, 0], 8);
        Assert.Equal(0.0, white[0, 1], 8);
        Assert.Equal(0.0, white[0, 2], 8);

        // Mid grey has no chroma either, and sits well below 50 because sRGB is not linear.
        double[,] grey = ColorSpaces.RgbToLab(Triple(0.5, 0.5, 0.5), RgbColorSpace.Srgb, d65);
        Assert.Equal(0.0, grey[0, 1], 8);
        Assert.Equal(0.0, grey[0, 2], 8);
        Assert.InRange(grey[0, 0], 53.0, 54.5);
    }

    [Fact]
    public void LabAndXyz_RoundTripThroughRgbInEverySpace()
    {
        double[] d65 = ColorSpaces.WhitePoint("d65");
        double[,] colours =
        {
            { 0.2, 0.7, 0.4 },
            { 0.9, 0.1, 0.05 },
            { 0.0, 0.0, 1.0 },
            { 0.42, 0.42, 0.42 },
        };

        foreach (RgbColorSpace space in new[]
                 {
                     RgbColorSpace.Srgb, RgbColorSpace.AdobeRgb1998,
                     RgbColorSpace.ProPhotoRgb, RgbColorSpace.LinearRgb,
                 })
        {
            double[,] back = ColorSpaces.LabToRgb(ColorSpaces.RgbToLab(colours, space, d65), space, d65);
            for (int i = 0; i < colours.GetLength(0); i++)
            {
                for (int c = 0; c < 3; c++)
                {
                    // Seven places: the cube root in the Lab step loses a little precision either
                    // side of zero, so a channel that started at zero comes back near 1e-8.
                    Assert.Equal(colours[i, c], back[i, c], 7);
                }
            }
        }
    }

    [Fact]
    public void Xyz_AdaptsBetweenWhitePoints()
    {
        // The same sRGB white read under D50 is no longer the D65 tristimulus, but converting it
        // back with the same white point has to return the colour it started from.
        double[] d50 = ColorSpaces.WhitePoint("d50");
        double[,] xyz = ColorSpaces.RgbToXyz(Triple(1, 1, 1), RgbColorSpace.Srgb, d50);
        Assert.Equal(d50[0], xyz[0, 0], 8);
        Assert.Equal(d50[2], xyz[0, 2], 8);

        double[,] back = ColorSpaces.XyzToRgb(xyz, RgbColorSpace.Srgb, d50);
        Assert.Equal(1.0, back[0, 0], 8);
        Assert.Equal(1.0, back[0, 2], 8);
    }

    [Fact]
    public void Hsv_RoundTripsAndPlacesThePrimaries()
    {
        double[,] hsv = ColorSpaces.RgbToHsv(new[,]
        {
            { 1.0, 0.0, 0.0 },
            { 0.0, 1.0, 0.0 },
            { 0.0, 0.0, 1.0 },
            { 0.5, 0.5, 0.5 },
        });

        Assert.Equal(0.0, hsv[0, 0], 10);
        Assert.Equal(1.0 / 3.0, hsv[1, 0], 10);
        Assert.Equal(2.0 / 3.0, hsv[2, 0], 10);
        Assert.Equal(0.0, hsv[3, 1], 10);  // grey has no saturation
        Assert.Equal(0.5, hsv[3, 2], 10);

        double[,] back = ColorSpaces.HsvToRgb(hsv);
        Assert.Equal(1.0, back[0, 0], 10);
        Assert.Equal(1.0, back[1, 1], 10);
        Assert.Equal(0.5, back[3, 2], 10);
    }

    [Fact]
    public void YCbCr_UsesStudioSwingAndRoundTrips()
    {
        double[,] ycbcr = ColorSpaces.RgbToYCbCr(new[,] { { 1.0, 1.0, 1.0 }, { 0.0, 0.0, 0.0 } });

        // White is 235 and black 16, both over 255, with the chroma channels sitting at 128.
        Assert.Equal(235.0 / 255.0, ycbcr[0, 0], 10);
        Assert.Equal(16.0 / 255.0, ycbcr[1, 0], 10);
        Assert.Equal(128.0 / 255.0, ycbcr[0, 1], 10);
        Assert.Equal(128.0 / 255.0, ycbcr[1, 2], 10);

        double[,] back = ColorSpaces.YCbCrToRgb(ycbcr);
        Assert.Equal(1.0, back[0, 0], 6);
        Assert.Equal(0.0, back[1, 2], 6);
    }

    [Fact]
    public void Ntsc_PutsWhiteOnLuminanceAlone()
    {
        double[,] yiq = ColorSpaces.RgbToNtsc(Triple(1, 1, 1));
        Assert.Equal(1.0, yiq[0, 0], 12);
        Assert.Equal(0.0, yiq[0, 1], 12);
        Assert.Equal(0.0, yiq[0, 2], 12);

        double[,] back = ColorSpaces.NtscToRgb(ColorSpaces.RgbToNtsc(Triple(0.3, 0.6, 0.9)));
        Assert.Equal(0.3, back[0, 0], 10);
        Assert.Equal(0.9, back[0, 2], 10);
    }

    [Fact]
    public void Gamma_MatchesTheSrgbCurveAndUndoesItself()
    {
        double[,] linear = ColorSpaces.RgbToLinear(Triple(0.5, 0.04, 1.0), RgbColorSpace.Srgb);
        Assert.Equal(0.21404114, linear[0, 0], 7);  // the piecewise power segment
        Assert.Equal(0.04 / 12.92, linear[0, 1], 10); // …and the linear toe below 0.04045
        Assert.Equal(1.0, linear[0, 2], 12);

        double[,] back = ColorSpaces.LinearToRgb(linear, RgbColorSpace.Srgb);
        Assert.Equal(0.5, back[0, 0], 10);
        Assert.Equal(0.04, back[0, 1], 10);
    }

    [Fact]
    public void DeltaE2000_MatchesSharmasPublishedTestPairs()
    {
        // Three rows of the Sharma–Wu–Dalal test set, which exists precisely because the formula's
        // hue-angle branches are easy to get wrong.
        double[,] first =
        {
            { 50.0000, 2.6772, -79.7751 },
            { 50.0000, 3.1571, -77.2803 },
            { 50.0000, 2.8361, -74.0200 },
        };
        double[,] second =
        {
            { 50.0000, 0.0000, -82.7485 },
            { 50.0000, 0.0000, -82.7485 },
            { 50.0000, 0.0000, -82.7485 },
        };

        double[] difference = ColorSpaces.DeltaE2000(first, second, 1.0, 1.0, 1.0);
        Assert.Equal(2.0425, difference[0], 4);
        Assert.Equal(2.8615, difference[1], 4);
        Assert.Equal(3.4412, difference[2], 4);
    }

    [Fact]
    public void DeltaMetrics_AgreeThatIdenticalColoursAreZero()
    {
        double[,] lab = { { 42.0, -7.5, 19.25 } };
        Assert.Equal(0.0, ColorSpaces.DeltaE76(lab, lab)[0], 12);
        Assert.Equal(0.0, ColorSpaces.DeltaE94(lab, lab, 1.0, 0.045, 0.015)[0], 12);
        Assert.Equal(0.0, ColorSpaces.DeltaE2000(lab, lab, 1.0, 1.0, 1.0)[0], 12);

        // CIE76 is a plain Euclidean distance, so a pure lightness step is the step itself.
        double[,] lighter = { { 52.0, -7.5, 19.25 } };
        Assert.Equal(10.0, ColorSpaces.DeltaE76(lab, lighter)[0], 10);
    }

    [Fact]
    public void ColorAngle_MeasuresDirectionNotBrightness()
    {
        Assert.Equal(90.0, ColorSpaces.ColorAngle([1, 0, 0], [0, 1, 0]), 10);
        Assert.Equal(0.0, ColorSpaces.ColorAngle([1, 1, 1], [0.2, 0.2, 0.2]), 10);
    }

    [Fact]
    public void Chromadapt_TurnsTheIlluminantNeutralAndIgnoresItsScale()
    {
        // A picture lit by something warm: the illuminant itself must come back grey.
        double[] illuminant = [0.9, 0.75, 0.5];
        double[,] corrected = ColorAdaptation.Adapt(
            new[,] { { 0.9, 0.75, 0.5 } }, illuminant, RgbColorSpace.Srgb, AdaptationMethod.Bradford);

        Assert.Equal(corrected[0, 0], corrected[0, 1], 3);
        Assert.Equal(corrected[0, 1], corrected[0, 2], 3);

        // In a linear space, halving the illuminant is the same illuminant pointing the same way, so
        // it must not change the correction at all. (In sRGB it would not be, because the transfer
        // function is not a scaling.)
        double[,] full = ColorAdaptation.Adapt(
            new[,] { { 0.9, 0.75, 0.5 } }, illuminant, RgbColorSpace.LinearRgb, AdaptationMethod.Bradford);
        double[,] halved = ColorAdaptation.Adapt(
            new[,] { { 0.9, 0.75, 0.5 } }, [0.45, 0.375, 0.25], RgbColorSpace.LinearRgb, AdaptationMethod.Bradford);
        Assert.Equal(full[0, 0], halved[0, 0], 10);
        Assert.Equal(full[0, 2], halved[0, 2], 10);
    }

    [Fact]
    public void IlluminantEstimators_FindTheCastInAGreyWorld()
    {
        // A flat grey scene tinted towards red: every estimator should report more red than blue.
        var scene = new double[400, 3];
        var random = new Random(7);
        for (int i = 0; i < 400; i++)
        {
            double grey = 0.2 + (0.4 * random.NextDouble());
            scene[i, 0] = grey * 0.9;
            scene[i, 1] = grey * 0.6;
            scene[i, 2] = grey * 0.35;
        }

        foreach (double[] estimate in new[]
                 {
                     ColorAdaptation.GrayWorld(scene, 1, 1, 1),
                     ColorAdaptation.WhitePatch(scene, 5),
                     ColorAdaptation.PrincipalComponent(scene, 20),
                 })
        {
            Assert.True(estimate[0] > estimate[1], "red should lead");
            Assert.True(estimate[1] > estimate[2], "green should beat blue");
            Assert.Equal(1.0, estimate[0], 10); // normalized on the largest channel
        }
    }

    [Fact]
    public void MedianCut_SplitsWhereTheColoursActuallyAre()
    {
        // Two tight clusters far apart: a two-entry palette must land one on each.
        var pixels = new double[200, 3];
        for (int i = 0; i < 100; i++)
        {
            pixels[i, 0] = 0.9 + (0.01 * (i % 5));
            pixels[i, 1] = 0.1;
            pixels[i, 2] = 0.1;
            pixels[100 + i, 0] = 0.1;
            pixels[100 + i, 1] = 0.1;
            pixels[100 + i, 2] = 0.9 + (0.01 * (i % 5));
        }

        double[,] map = IndexedImages.MedianCut(pixels, 2);
        Assert.Equal(2, map.GetLength(0));
        double redEntry = Math.Max(map[0, 0], map[1, 0]);
        double blueEntry = Math.Max(map[0, 2], map[1, 2]);
        Assert.InRange(redEntry, 0.89, 0.95);
        Assert.InRange(blueEntry, 0.89, 0.95);
    }

    [Fact]
    public void Quantize_RoundTripsThroughAPaletteItWasBuiltFrom()
    {
        double[,] pixels =
        {
            { 1.0, 0.0, 0.0 }, { 0.0, 1.0, 0.0 },
            { 0.0, 0.0, 1.0 }, { 1.0, 1.0, 1.0 },
        };
        double[,] map = IndexedImages.MedianCut(pixels, 4);
        double[] indices = IndexedImages.Quantize(pixels, 2, 2, map, dither: false);
        double[,] back = IndexedImages.Expand(indices, map);

        for (int i = 0; i < 4; i++)
        {
            for (int c = 0; c < 3; c++)
            {
                Assert.Equal(pixels[i, c], back[i, c], 10);
            }
        }
    }

    [Fact]
    public void ColormapToGray_LeavesAGreyRampAlone()
    {
        double[,] ramp = IndexedImages.GrayColormap(5);
        Assert.Equal(0.0, ramp[0, 0], 12);
        Assert.Equal(1.0, ramp[4, 2], 12);

        double[,] gray = IndexedImages.ColormapToGray(ramp);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(ramp[i, 0], gray[i, 0], 8);
        }
    }

    [Fact]
    public void Demosaic_ReproducesAFlatFieldAndPlacesTheChannels()
    {
        var flat = new double[16];
        Array.Fill(flat, 0.4);
        double[,] rgb = IndexedImages.Demosaic(flat, 4, 4, SensorAlignment.Rggb);
        for (int i = 0; i < 16; i++)
        {
            for (int c = 0; c < 3; c++)
            {
                Assert.Equal(0.4, rgb[i, c], 10);
            }
        }

        // Under 'rggb' the top-left pixel measured red, so its red is the sample itself.
        var patterned = new double[16];
        patterned[0] = 1.0;
        double[,] single = IndexedImages.Demosaic(patterned, 4, 4, SensorAlignment.Rggb);
        Assert.Equal(1.0, single[0, 0], 10);

        Assert.Throws<ArgumentException>(() => IndexedImages.Demosaic(new double[9], 3, 3, SensorAlignment.Rggb));
    }
}
