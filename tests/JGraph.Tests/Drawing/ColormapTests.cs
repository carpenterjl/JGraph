using JGraph.Core.Drawing;
using JGraph.Objects;
using Xunit;

namespace JGraph.Tests.Drawing;

public class ColormapTests
{
    [Fact]
    public void Sample_EndpointsMatchStops()
    {
        var map = new Colormap("bw", Colors.Black, Colors.White);
        Assert.Equal(Colors.Black, map.Sample(0.0));
        Assert.Equal(Colors.White, map.Sample(1.0));
    }

    [Fact]
    public void Sample_MidpointInterpolatesLinearly()
    {
        var map = new Colormap("bw", Colors.Black, Colors.White);
        Color mid = map.Sample(0.5);
        Assert.InRange(mid.R, 126, 129);
        Assert.Equal(mid.R, mid.G);
        Assert.Equal(mid.R, mid.B);
    }

    [Fact]
    public void Sample_ClampsOutOfRange()
    {
        var map = new Colormap("bw", Colors.Black, Colors.White);
        Assert.Equal(Colors.Black, map.Sample(-3.0));
        Assert.Equal(Colors.White, map.Sample(4.0));
    }

    [Fact]
    public void Sample_WithRangeMapsValue()
    {
        var map = new Colormap("bw", Colors.Black, Colors.White);
        // Value 5 in [0, 10] is the midpoint.
        Color mid = map.Sample(5.0, 0.0, 10.0);
        Assert.InRange(mid.R, 126, 129);
    }

    [Fact]
    public void Sample_NaNMapsToLowEnd()
    {
        var map = new Colormap("bw", Colors.Black, Colors.White);
        Assert.Equal(Colors.Black, map.Sample(double.NaN));
    }

    [Fact]
    public void Sample_ThreeStopsPicksCorrectSegment()
    {
        var map = new Colormap("rgb", Colors.Red, Colors.Green, Colors.Blue);
        // t = 0.25 sits halfway through the first (red→green) segment.
        Color quarter = map.Sample(0.25);
        Assert.True(quarter.R > 100 && quarter.G > 40);
        // t = 0.75 sits halfway through the second (green→blue) segment.
        Color threeQuarter = map.Sample(0.75);
        Assert.True(threeQuarter.B > 100);
    }

    [Fact]
    public void Presets_HaveExpectedEndpoints()
    {
        Assert.Equal(Colors.Black, Colormap.Grayscale.Sample(0));
        Assert.Equal(Colors.White, Colormap.Grayscale.Sample(1));
        // Viridis runs dark-purple to yellow.
        Assert.True(Colormap.Viridis.Sample(1).R > 200 && Colormap.Viridis.Sample(1).G > 200);
        // Parula runs blue-violet to yellow — it is the one that never goes near black, which is
        // why MATLAB's surfaces read brighter than a viridis one of the same data.
        Assert.Equal(Color.FromRgb(0x3E, 0x26, 0xA8), Colormap.Parula.Sample(0));
        Assert.Equal(Color.FromRgb(0xF9, 0xFB, 0x15), Colormap.Parula.Sample(1));
    }

    [Fact]
    public void Constructor_RejectsTooFewStops()
    {
        Assert.Throws<System.ArgumentException>(() => new Colormap("x", Colors.Red));
    }

    /// <summary>
    /// M44 wave 3. Every stop has to come back at its own position — a stop count that does not
    /// divide the range the way the map's own definition does would slide the whole thing, and the
    /// stops are the only record of what each map is supposed to be. One count of slack for the
    /// truncating lerp, since a position like 1/6 is not exact in binary.
    /// </summary>
    [Theory]
    [MemberData(nameof(Presets))]
    public void Sample_ReproducesEveryStopAtItsOwnPosition(Colormap map)
    {
        for (int i = 0; i < map.Stops.Count; i++)
        {
            Color expected = map.Stops[i];
            Color actual = map.Sample(i / (double)(map.Stops.Count - 1));
            Assert.True(
                System.Math.Abs(expected.R - actual.R) <= 1
                    && System.Math.Abs(expected.G - actual.G) <= 1
                    && System.Math.Abs(expected.B - actual.B) <= 1,
                $"{map.Name} stop {i}: expected {expected}, sampled {actual}");
        }
    }

    /// <summary>
    /// MATLAB's <c>hot</c> saturates red three eighths of the way up and green six eighths, not at
    /// the thirds. Four evenly spaced stops put the turns in the wrong place, which is what this
    /// map used to have.
    /// </summary>
    [Fact]
    public void Hot_TurnsAtThreeEighthsAndSixEighths()
    {
        Assert.Equal(Color.FromRgb(255, 0, 0), Colormap.Hot.Sample(3 / 8.0));
        Assert.Equal(Color.FromRgb(255, 255, 0), Colormap.Hot.Sample(6 / 8.0));
        Assert.Equal(Colors.White, Colormap.Hot.Sample(1));
    }

    /// <summary>Copper's red saturates at four fifths and the other two channels keep climbing.</summary>
    [Fact]
    public void Copper_SaturatesRedAtFourFifths()
    {
        Color turn = Colormap.Copper.Sample(0.8);
        Assert.Equal(255, turn.R);
        Assert.True(Colormap.Copper.Sample(1).G > turn.G);
        Assert.True(Colormap.Copper.Sample(1).B > turn.B);
    }

    /// <summary>
    /// The maps MATLAB defines as a straight line in every channel — the whole spring/summer/autumn/
    /// winter family plus cool — must agree with that definition everywhere, not just at the ends.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    [InlineData(1.0)]
    public void LinearPresets_MatchTheirClosedForm(double t)
    {
        AssertChannels(Colormap.Spring.Sample(t), 1.0, t, 1.0 - t);
        AssertChannels(Colormap.Summer.Sample(t), t, 0.5 + (0.5 * t), 0.4);
        AssertChannels(Colormap.Autumn.Sample(t), 1.0, t, 0.0);
        AssertChannels(Colormap.Winter.Sample(t), 0.0, t, 1.0 - (0.5 * t));
        AssertChannels(Colormap.Cool.Sample(t), t, 1.0 - t, 1.0);

        static void AssertChannels(Color actual, double r, double g, double b)
        {
            Assert.InRange(actual.R, (r * 255) - 1.5, (r * 255) + 1.5);
            Assert.InRange(actual.G, (g * 255) - 1.5, (g * 255) + 1.5);
            Assert.InRange(actual.B, (b * 255) - 1.5, (b * 255) + 1.5);
        }
    }

    /// <summary>
    /// A discrete palette hands back one of its own colors and never a mixture of two. Scanning the
    /// whole range catches an interpolated bin edge, which is what would happen if the flag were
    /// dropped somewhere between the map and the sampler.
    /// </summary>
    [Fact]
    public void Lines_IsAPaletteAndNeverBlendsTwoOfItsColors()
    {
        Assert.True(Colormap.Lines.Discrete);
        for (int i = 0; i <= 200; i++)
        {
            Color sampled = Colormap.Lines.Sample(i / 200.0);
            Assert.Contains(sampled, Colormap.Lines.Stops);
        }

        // Every one of the seven is actually reachable, so the bins really do divide the range.
        Assert.Equal(7, Colormap.Lines.Stops.Count);
        Assert.Equal(
            7,
            Enumerable.Range(0, 201).Select(i => Colormap.Lines.Sample(i / 200.0)).Distinct().Count());
    }

    /// <summary>
    /// <c>parula(64)</c> and friends: a gradient is sampled across the full range including both
    /// ends, and a palette cycles, which is exactly what <c>lines(n)</c> does.
    /// </summary>
    [Fact]
    public void Resample_SpansAGradientAndCyclesAPalette()
    {
        Color[] rows = Colormap.Parula.Resample(64);
        Assert.Equal(64, rows.Length);
        Assert.Equal(Colormap.Parula.Sample(0), rows[0]);
        Assert.Equal(Colormap.Parula.Sample(1), rows[^1]);

        Color[] palette = Colormap.Lines.Resample(10);
        Assert.Equal(Colormap.Lines.Stops[0], palette[0]);
        Assert.Equal(Colormap.Lines.Stops[0], palette[7]);
        Assert.Equal(Colormap.Lines.Stops[2], palette[9]);

        // MATLAB's colormap generators accept a count of one and answer with the low end.
        Assert.Equal([Colormap.Parula.Sample(0)], Colormap.Parula.Resample(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Colormap.Parula.Resample(0));
    }

    /// <summary>
    /// <c>colormap(map)</c> takes an m-by-3 matrix. Handing back what <see cref="Colormap.Resample"/>
    /// produced has to give the same map, since that is the round trip a script makes when it reads
    /// a colormap out, edits a row and puts it back.
    /// </summary>
    [Fact]
    public void FromRows_RoundTripsAResampledMap()
    {
        Color[] rows = Colormap.Parula.Resample(17);
        var matrix = new double[rows.Length, 3];
        for (int r = 0; r < rows.Length; r++)
        {
            matrix[r, 0] = rows[r].R / 255.0;
            matrix[r, 1] = rows[r].G / 255.0;
            matrix[r, 2] = rows[r].B / 255.0;
        }

        Colormap rebuilt = Colormap.FromRows("Custom", matrix);
        Assert.Equal("Custom", rebuilt.Name);
        Assert.Equal(rows, rebuilt.Stops);
        for (int r = 0; r < rows.Length; r++)
        {
            Color sampled = rebuilt.Sample(r / (double)(rows.Length - 1));
            Assert.True(
                System.Math.Abs(rows[r].R - sampled.R) <= 2
                    && System.Math.Abs(rows[r].G - sampled.G) <= 2
                    && System.Math.Abs(rows[r].B - sampled.B) <= 2,
                $"row {r}: expected {rows[r]}, sampled {sampled}");
        }
    }

    [Fact]
    public void FromRows_ClampsComponentsAndAcceptsASingleRow()
    {
        Colormap one = Colormap.FromRows("One", new[,] { { 2.0, -1.0, 0.5 } });
        Assert.Equal(Color.FromRgb(255, 0, 128), one.Sample(0));
        Assert.Equal(one.Sample(0), one.Sample(1));

        Assert.Throws<ArgumentException>(() => Colormap.FromRows("Bad", new double[2, 4]));
        Assert.Throws<ArgumentException>(() => Colormap.FromRows("Bad", new double[0, 3]));
    }

    [Theory]
    [MemberData(nameof(KnownNames))]
    public void TryGetByName_ResolvesEveryAdvertisedName(string name)
    {
        Assert.True(Colormap.TryGetByName(name, out Colormap map));
        Assert.NotEmpty(map.Stops);
    }

    /// <summary>The map every color-mapped plot starts with, and the one an unknown name falls back to.</summary>
    [Fact]
    public void Parula_IsTheDefault()
    {
        Assert.False(Colormap.TryGetByName("plasma", out Colormap fallback));
        Assert.Same(Colormap.Parula, fallback);
        Assert.Same(Colormap.Parula, new SurfacePlot(new double[2, 2]).Colormap);
        Assert.Same(Colormap.Parula, new ContourPlot([0, 1], [0, 1], new double[2, 2]).Colormap);
        Assert.Same(Colormap.Parula, new ImagePlot(new double[2, 2]).Colormap);
    }

    public static TheoryData<Colormap> Presets =>
    [
        Colormap.Parula, Colormap.Viridis, Colormap.Turbo, Colormap.Jet, Colormap.Hot, Colormap.Cool,
        Colormap.Grayscale, Colormap.Hsv, Colormap.Bone, Colormap.Copper, Colormap.Pink,
        Colormap.Spring, Colormap.Summer, Colormap.Autumn, Colormap.Winter,
    ];

    public static TheoryData<string> KnownNames
    {
        get
        {
            TheoryData<string> data = [];
            foreach (string name in Colormap.KnownNames)
            {
                data.Add(name);
            }

            return data;
        }
    }
}
