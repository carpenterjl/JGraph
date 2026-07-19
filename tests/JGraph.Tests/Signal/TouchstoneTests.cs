using System.Numerics;
using JGraph.Signal.Rf;
using Xunit;

namespace JGraph.Tests.Signal;

/// <summary>
/// The Touchstone (.sNp) reader: option-line parsing, the RI/MA/DB pair formats, frequency-unit
/// scaling, the 2-port column-order swap, and multi-line record wrapping.
/// </summary>
public class TouchstoneTests
{
    private static SParameterNetwork Read(string text, int ports) =>
        Touchstone.Read(new StringReader(text), ports);

    [Fact]
    public void TwoPort_SwapsColumnOrder_SoOffDiagonalsAreNotTransposed()
    {
        // File order is S11 S21 S12 S22. With distinct off-diagonals the swap is observable:
        // the reader must land S21 = 0.9 and S12 = 0.05, not the reverse.
        SParameterNetwork net = Read("# GHz S RI R 50\n1.0  0.1 0.0  0.9 0.0  0.05 0.0  0.2 0.0\n", 2);

        Assert.Equal(1, net.PointCount);
        Assert.Equal(new Complex(0.1, 0), net[0, 0, 0]);   // S11
        Assert.Equal(new Complex(0.05, 0), net[0, 0, 1]);  // S12
        Assert.Equal(new Complex(0.9, 0), net[0, 1, 0]);   // S21
        Assert.Equal(new Complex(0.2, 0), net[0, 1, 1]);   // S22
    }

    [Fact]
    public void MagnitudeAngle_ConvertsToRectangular()
    {
        // 0.5 ∠ 90° is 0 + 0.5j.
        SParameterNetwork net = Read("# GHz S MA R 50\n1.0  0.5 90\n", 1);

        Assert.Equal(0.0, net[0, 0, 0].Real, 12);
        Assert.Equal(0.5, net[0, 0, 0].Imaginary, 12);
    }

    [Fact]
    public void Decibel_ConvertsMagnitude_AndMhzScalesFrequency()
    {
        // −6.0206 dB is a linear magnitude of 0.5; the MHz unit scales 1000 → 1e9 Hz.
        SParameterNetwork net = Read("# MHz S DB R 50\n1000  -6.0206 0\n", 1);

        Assert.Equal(0.5, net[0, 0, 0].Magnitude, 4);
        Assert.Equal(1e9, net.Frequencies[0], 3);
    }

    [Fact]
    public void ThreePort_ReadsRowMajor_AcrossWrappedLines()
    {
        // A 3-port record is 1 + 2·9 numbers; here it wraps across three physical lines. No column
        // swap applies for N ≥ 3, so the values land in reading order.
        SParameterNetwork net = Read(
            "! a 3-port\n# GHz S RI R 50\n" +
            "1.0  0.1 0.0  0.2 0.0  0.3 0.0\n" +
            "     0.4 0.0  0.5 0.0  0.6 0.0\n" +
            "     0.7 0.0  0.8 0.0  0.9 0.0\n", 3);

        Assert.Equal(3, net.Ports);
        Assert.Equal(1, net.PointCount);
        Assert.Equal(0.1, net[0, 0, 0].Real, 12);
        Assert.Equal(0.6, net[0, 1, 2].Real, 12);
        Assert.Equal(0.9, net[0, 2, 2].Real, 12);
    }

    [Fact]
    public void MissingOptionLine_UsesGhzMagnitudeAngle50Defaults()
    {
        SParameterNetwork net = Read("1.0  0.5 0\n", 1);

        Assert.Equal(50.0, net.ReferenceImpedance);
        Assert.Equal(1e9, net.Frequencies[0], 3);
        Assert.Equal(0.5, net[0, 0, 0].Real, 12);
    }

    [Fact]
    public void TrailingBangComment_IsIgnored()
    {
        SParameterNetwork net = Read("# GHz S RI R 50\n1.0  0.5 0.0  ! trailing note\n", 1);

        Assert.Equal(0.5, net[0, 0, 0].Real, 12);
    }

    [Fact]
    public void UnsupportedParameterType_IsRejected()
    {
        InvalidDataException ex = Assert.Throws<InvalidDataException>(
            () => Read("# GHz Y RI R 50\n1.0  0.5 0.0\n", 1));
        Assert.Contains("S parameters", ex.Message);
    }

    [Fact]
    public void PortsFromExtension_ReadsTheDigits()
    {
        Assert.Equal(1, Touchstone.PortsFromExtension("probe.s1p"));
        Assert.Equal(2, Touchstone.PortsFromExtension("amp.S2P"));
        Assert.Equal(4, Touchstone.PortsFromExtension(@"C:\data\coupler.s4p"));
        Assert.Throws<InvalidDataException>(() => Touchstone.PortsFromExtension("notes.txt"));
    }
}
