using JGraph.Signal.Rf;
using Xunit;

namespace JGraph.Tests.Signal;

/// <summary>
/// The microstrip (Hammerstad–Jensen) and stripline (Pozar) calculators, anchored on the worked
/// examples in Pozar's <em>Microwave Engineering</em> for εr = 2.20.
/// </summary>
public class TransmissionLineTests
{
    [Fact]
    public void Microstrip_AtPozarAnchor_GivesFiftyOhms()
    {
        // Pozar: εr = 2.20, W/d ≈ 3.081 → Z0 ≈ 50 Ω, ε_eff ≈ 1.87.
        (double z0, double eeff) = TransmissionLine.Microstrip(3.081, 1.0, 2.2);

        Assert.Equal(50.0, z0, 0); // within ±0.5 Ω
        Assert.Equal(1.87, eeff, 1); // within ±0.05
    }

    [Fact]
    public void MicrostripWidth_SynthesizesThePozarWidthRatio()
    {
        double width = TransmissionLine.MicrostripWidth(50.0, 1.0, 2.2);
        Assert.Equal(3.081, width, 1); // W/d within ±0.05
    }

    [Fact]
    public void Microstrip_SynthesisThenAnalysis_RoundTripsTheImpedance()
    {
        double width = TransmissionLine.MicrostripWidth(50.0, 1.0, 2.2);
        (double z0, _) = TransmissionLine.Microstrip(width, 1.0, 2.2);
        Assert.Equal(50.0, z0, 6); // synthesis and analysis are consistent to machine tolerance
    }

    [Fact]
    public void Stripline_AtPozarAnchor_GivesFiftyOhms()
    {
        // Pozar: εr = 2.20, b = 0.32 cm, Z0 = 50 Ω → W ≈ 0.266 cm.
        double width = TransmissionLine.StriplineWidth(50.0, 0.32, 2.2);
        Assert.Equal(0.266, width, 2);

        double z0 = TransmissionLine.Stripline(width, 0.32, 2.2);
        Assert.Equal(50.0, z0, 0);
    }

    [Fact]
    public void GuidedWavelength_MatchesClosedForm()
    {
        // λg = c / (f·√ε_eff): at 10 GHz with ε_eff = 1.88, ≈ 21.9 mm.
        double lambda = TransmissionLine.GuidedWavelength(10e9, 1.88);
        Assert.Equal(0.0219, lambda, 3);
    }

    [Fact]
    public void Microstrip_RejectsNonPhysicalInput()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TransmissionLine.Microstrip(0, 1, 2.2));
        Assert.Throws<ArgumentOutOfRangeException>(() => TransmissionLine.Microstrip(1, 1, 0.5));
    }
}
