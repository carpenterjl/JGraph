using System.Numerics;
using JGraph.Signal.Rf;
using Xunit;

namespace JGraph.Tests.Signal;

/// <summary>
/// The S/Z/Y/ABCD conversions, 2-port cascading, and reflection helpers, anchored on textbook
/// lumped elements in a 50 Ω system.
/// </summary>
public class NetworkConversionTests
{
    private const double Z0 = 50.0;

    private static SParameterNetwork Abcd(Complex a, Complex b, Complex c, Complex d) =>
        new(2, Z0, [1e9], [a, b, c, d]);

    private static SParameterNetwork SeriesImpedance(double ohms) =>
        NetworkConversions.AbcdToS(Abcd(1, ohms, 0, 1));

    [Fact]
    public void SeriesHundredOhms_HasHalfReflectionAndTransmission()
    {
        SParameterNetwork s = SeriesImpedance(100);

        Assert.Equal(0.5, s[0, 0, 0].Real, 12); // S11
        Assert.Equal(0.5, s[0, 1, 0].Real, 12); // S21
    }

    [Fact]
    public void ShuntFiftyOhms_MatchesClosedForm()
    {
        // A shunt admittance Y = 1/50 has ABCD [1, 0; Y, 1].
        SParameterNetwork s = NetworkConversions.AbcdToS(Abcd(1, 0, 1.0 / 50.0, 1));

        Assert.Equal(-1.0 / 3.0, s[0, 0, 0].Real, 12); // S11
        Assert.Equal(2.0 / 3.0, s[0, 1, 0].Real, 12);  // S21
    }

    [Fact]
    public void SToAbcd_RecoversTheSeriesImpedance()
    {
        SParameterNetwork abcd = NetworkConversions.SToAbcd(SeriesImpedance(100));

        Assert.Equal(1.0, abcd[0, 0, 0].Real, 9); // A
        Assert.Equal(100.0, abcd[0, 0, 1].Real, 9); // B = the series impedance
        Assert.Equal(0.0, abcd[0, 1, 0].Real, 9); // C
        Assert.Equal(1.0, abcd[0, 1, 1].Real, 9); // D
    }

    [Fact]
    public void CascadingTwoFiftyOhmSeries_EqualsOneHundredOhmSeries()
    {
        SParameterNetwork cascaded = NetworkConversions.Cascade(SeriesImpedance(50), SeriesImpedance(50));

        Assert.Equal(0.5, cascaded[0, 0, 0].Real, 9); // S11
        Assert.Equal(0.5, cascaded[0, 1, 0].Real, 9); // S21
    }

    [Fact]
    public void ZRoundTrip_ReturnsTheOriginalSParameters()
    {
        var original = new SParameterNetwork(2, Z0, [1e9],
        [
            new Complex(0.1, 0.2), new Complex(0.3, -0.1),
            new Complex(0.3, -0.1), new Complex(-0.2, 0.05),
        ]);

        SParameterNetwork roundTripped = NetworkConversions.ZToS(NetworkConversions.SToZ(original));

        for (int k = 0; k < 4; k++)
        {
            Assert.Equal(original.Data[k].Real, roundTripped.Data[k].Real, 12);
            Assert.Equal(original.Data[k].Imaginary, roundTripped.Data[k].Imaginary, 12);
        }
    }

    [Fact]
    public void GammaIn_WithMatchedLoad_EqualsS11()
    {
        SParameterNetwork s = SeriesImpedance(100);

        Complex[] gamma = NetworkConversions.GammaIn(s); // default load = Z0

        Assert.Equal(s[0, 0, 0].Real, gamma[0].Real, 12);
        Assert.Equal(s[0, 0, 0].Imaginary, gamma[0].Imaginary, 12);
    }

    [Fact]
    public void Vswr_OfHalf_IsThree()
    {
        double[] vswr = NetworkConversions.Vswr([new Complex(0.5, 0)]);
        Assert.Equal(3.0, vswr[0], 12);
    }

    [Fact]
    public void ThreePortConversion_IsRejected()
    {
        var net = new SParameterNetwork(3, Z0, [1e9], new Complex[9]);
        Assert.Throws<NotSupportedException>(() => NetworkConversions.SToZ(net));
    }
}
