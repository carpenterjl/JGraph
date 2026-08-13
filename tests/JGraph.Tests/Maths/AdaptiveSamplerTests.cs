using JGraph.Maths.Sampling;
using Xunit;

namespace JGraph.Tests.Maths;

/// <summary>
/// M58: the adaptive sampler every function plotter draws with. The claims tested here are the ones
/// the drawing verbs rely on and cannot check for themselves — that flatness costs nothing, that
/// curvature buys points, and that a curve running away becomes a gap rather than a wall.
/// </summary>
public class AdaptiveSamplerTests
{
    private const int Seed = 33;

    [Fact]
    public void AStraightLineTakesOnlyTheEvenReadings()
    {
        AdaptiveSamples samples = AdaptiveSampler1D.Sample(x => (2 * x) + 1, 0, 1);

        Assert.Equal(Seed, samples.Count);
        Assert.Equal(0, samples.Parameters[0], 12);
        Assert.Equal(1, samples.Parameters[^1], 12);
    }

    [Fact]
    public void AConstantTakesOnlyTheEvenReadings()
    {
        AdaptiveSamples samples = AdaptiveSampler1D.Sample(_ => 7, -3, 3);

        Assert.Equal(Seed, samples.Count);
        Assert.All(samples.Values, v => Assert.Equal(7, v, 12));
    }

    [Fact]
    public void ACurveBuysMoreReadingsThanTheEvenPass()
    {
        AdaptiveSamples samples = AdaptiveSampler1D.Sample(System.Math.Sin, -5, 5);

        Assert.True(samples.Count > Seed, $"sin was sampled only {samples.Count} times.");

        // Every reading is the function's own value, and the parameters only ever increase.
        for (int i = 0; i < samples.Count; i++)
        {
            Assert.Equal(System.Math.Sin(samples.Parameters[i]), samples.Values[i], 12);
            if (i > 0)
            {
                Assert.True(samples.Parameters[i] > samples.Parameters[i - 1]);
            }
        }
    }

    /// <summary>
    /// The point of sampling adaptively: the readings go where the curve bends. <c>atan(50x)</c> is
    /// flat everywhere except a short stretch about the origin, and that stretch is a twentieth of the
    /// domain.
    /// </summary>
    [Fact]
    public void TheReadingsGoWhereTheCurveBends()
    {
        AdaptiveSamples samples = AdaptiveSampler1D.Sample(x => System.Math.Atan(50 * x), -5, 5);

        int nearTheBend = samples.Parameters.Count(p => System.Math.Abs(p) <= 0.25);
        Assert.True(
            nearTheBend > samples.Count / 2,
            $"only {nearTheBend} of {samples.Count} readings landed on the bend.");
    }

    /// <summary>
    /// The straight lines drawn between the readings have to look like the curve, which is the whole
    /// claim: the midpoint of every chord sits close to the function there.
    /// </summary>
    [Fact]
    public void EveryChordFollowsTheCurve()
    {
        AdaptiveSamples samples = AdaptiveSampler1D.Sample(x => System.Math.Sin(3 * x), -3, 3);

        double worst = 0;
        for (int i = 0; i < samples.Count - 1; i++)
        {
            double middle = (samples.Parameters[i] + samples.Parameters[i + 1]) / 2;
            double chord = (samples.Values[i] + samples.Values[i + 1]) / 2;
            worst = System.Math.Max(worst, System.Math.Abs(chord - System.Math.Sin(3 * middle)));
        }

        Assert.True(worst < 0.01, $"a chord missed the curve by {worst}.");
    }

    [Fact]
    public void APoleBecomesAGapRatherThanAWall()
    {
        AdaptiveSamples samples = AdaptiveSampler1D.Sample(x => 1 / x, -5, 5);

        Assert.Contains(samples.Values, double.IsNaN);
        Assert.True(samples.PoleCount > 0, "nothing was found to have run away.");

        // No reading survives at a height that would draw a wall across the picture, and the gap is
        // narrow: the last finite reading on each side sits close to the pole.
        double tallest = samples.Values.Where(double.IsFinite).Max(System.Math.Abs);
        Assert.True(tallest < 100, $"a reading of {tallest} survived beside the pole.");

        double closest = samples.Parameters
            .Where((_, i) => double.IsFinite(samples.Values[i]))
            .Min(System.Math.Abs);
        Assert.True(closest < 0.1, $"the gap reaches out to {closest} on each side.");
    }

    [Fact]
    public void EveryPoleOfTheTangentIsFound()
    {
        AdaptiveSamples samples = AdaptiveSampler1D.Sample(System.Math.Tan, -5, 5);

        // tan has three poles inside [-5, 5]: -3π/2, -π/2, π/2, and 3π/2.
        double[] poles = [-3 * System.Math.PI / 2, -System.Math.PI / 2, System.Math.PI / 2, 3 * System.Math.PI / 2];
        foreach (double pole in poles)
        {
            bool gapped = false;
            for (int i = 0; i < samples.Count; i++)
            {
                if (double.IsNaN(samples.Values[i]) && System.Math.Abs(samples.Parameters[i] - pole) < 0.2)
                {
                    gapped = true;
                }
            }

            Assert.True(gapped, $"no gap was left at {pole}.");
        }
    }

    /// <summary>
    /// A curve that will not flatten is not the same thing as a curve running away.
    /// <c>sin(1/x)</c> oscillates without bound in frequency near the origin and stays inside
    /// [-1, 1], so it is sampled densely and left whole.
    /// </summary>
    [Fact]
    public void AnUnresolvableButBoundedCurveIsNotBroken()
    {
        AdaptiveSamples samples = AdaptiveSampler1D.Sample(x => System.Math.Sin(1 / x), 0.01, 1);

        Assert.Equal(0, samples.PoleCount);
        Assert.DoesNotContain(samples.Values, double.IsNaN);
        Assert.True(samples.Count > Seed);
    }

    /// <summary>Growth that is merely steep is a reading, not a pole: nothing of e^x is dropped.</summary>
    [Fact]
    public void ASteeplyGrowingCurveKeepsAllOfItsReadings()
    {
        AdaptiveSamples samples = AdaptiveSampler1D.Sample(System.Math.Exp, -5, 5);

        Assert.Equal(0, samples.PoleCount);
        Assert.DoesNotContain(samples.Values, double.IsNaN);
        Assert.Equal(System.Math.Exp(5), samples.Values[^1], 9);
    }

    [Fact]
    public void ACurveOfSeveralComponentsIsSampledOnAllOfThemAtOnce()
    {
        AdaptiveSamples samples = AdaptiveSampler1D.Sample(
            parameters =>
            [
                parameters.Select(System.Math.Cos).ToArray(),
                parameters.Select(System.Math.Sin).ToArray(),
            ],
            2,
            0,
            2 * System.Math.PI);

        Assert.Equal(2, samples.Components.Length);
        for (int i = 0; i < samples.Count; i++)
        {
            double radius = System.Math.Sqrt(
                (samples.Components[0][i] * samples.Components[0][i])
                + (samples.Components[1][i] * samples.Components[1][i]));
            Assert.Equal(1, radius, 12);
        }
    }

    /// <summary>
    /// A round of probes is one question, not one question per probe — which is what lets a script's
    /// function handle be evaluated over an array.
    /// </summary>
    [Fact]
    public void TheFunctionIsAskedForAWholeRoundAtATime()
    {
        int rounds = 0;
        int readings = 0;
        AdaptiveSamples samples = AdaptiveSampler1D.Sample(
            parameters =>
            {
                rounds++;
                readings += parameters.Count;
                return [parameters.Select(p => System.Math.Sin(5 * p)).ToArray()];
            },
            1,
            -3,
            3);

        Assert.True(rounds <= 13, $"the function was asked {rounds} times.");
        Assert.True(readings > samples.Count, "no probe was ever discarded as unneeded.");
    }

    [Fact]
    public void TheBudgetIsKept()
    {
        AdaptiveSamples samples = AdaptiveSampler1D.Sample(
            x => System.Math.Sin(1 / x),
            0.001,
            1,
            new AdaptiveSamplerOptions { MaxPoints = 300 });

        Assert.True(samples.Count <= 300, $"{samples.Count} readings were taken.");
    }

    [Fact]
    public void ADomainThatIsNotOneIsRefused()
    {
        Assert.Throws<ArgumentException>(() => AdaptiveSampler1D.Sample(x => x, 1, 1));
        Assert.Throws<ArgumentException>(() => AdaptiveSampler1D.Sample(x => x, 5, 2));
        Assert.Throws<ArgumentException>(() => AdaptiveSampler1D.Sample(x => x, 0, double.PositiveInfinity));
    }
}
