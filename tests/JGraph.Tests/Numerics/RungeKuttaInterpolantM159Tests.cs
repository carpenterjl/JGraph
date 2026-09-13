using JGraph.Numerics;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// ADR 0159 (item 12e.1): the explicit schemes' interpolant forms its weights once per call
/// instead of once per component, and skips the gradient when no slope is asked for. The claim is
/// bit identity, so the oracle here is the loop as it stood — the weight recomputed inside the
/// component loop — over every scheme, with and without a slope, with and without a component held
/// non-negative, at the ends of the step and inside it. The output sink's reused lists are checked
/// through a solver run: two runs answer the same numbers, and no two stored states share an array.
/// </summary>
public class RungeKuttaInterpolantM159Tests
{
    public static TheoryData<string> Schemes() => ["ode23", "ode45", "ode78", "ode89"];

    [Theory]
    [MemberData(nameof(Schemes))]
    public void TheHoistedWeightsAnswerTheSameBitsAsThePerComponentLoop(string name)
    {
        RungeKuttaScheme scheme = RungeKuttaScheme.Named(name)!;
        var random = new Random(159);
        int n = 7;
        double[] y = Draw(random, n);
        double[][] stages = new double[scheme.InterpolationStages.Length][];
        for (int j = 0; j < stages.Length; j++)
        {
            stages[j] = Draw(random, n);
        }

        stages[1][2] = -0.0;
        stages[0][5] = 0;
        double t = 0.37;
        double h = 0.0125;
        foreach (double at in new[] { t, t + (h / 3), t + (0.5 * h), t + (0.999 * h), t + h })
        {
            foreach (int[]? nonNegative in new int[]?[] { null, [2, 5], [0, 1, 2, 3, 4, 5, 6] })
            {
                double[] expected = Reference(scheme, t, h, y, stages, at, null, nonNegative);
                double[] actual = scheme.Interpolate(t, h, y, stages, at, null, nonNegative);
                AssertBitsEach(expected, actual);

                var expectedSlope = new double[n];
                var actualSlope = new double[n];
                expected = Reference(scheme, t, h, y, stages, at, expectedSlope, nonNegative);
                actual = scheme.Interpolate(t, h, y, stages, at, actualSlope, nonNegative);
                AssertBitsEach(expected, actual);
                AssertBitsEach(expectedSlope, actualSlope);
            }
        }
    }

    [Fact]
    public void TheReusedOutputListsLeaveEveryStoredStateItsOwnArray()
    {
        OdeFunction orbit = (_, y) =>
        {
            double r3 = Math.Pow((y[0] * y[0]) + (y[1] * y[1]), 1.5);
            return [y[2], y[3], -y[0] / r3, -y[1] / r3];
        };
        double[] y0 = [1, 0, 0, 1];
        var named = new OdeOptions { RelativeTolerance = 1e-6, AbsoluteTolerance = [1e-8, 1e-8, 1e-8, 1e-8] };
        var refined = new OdeOptions { Refine = 6 };
        double[] tspan = new double[41];
        for (int i = 0; i < tspan.Length; i++)
        {
            tspan[i] = i * 0.25;
        }

        foreach ((IReadOnlyList<double> span, OdeOptions options) in new[] { (tspan, named), ([0.0, 10.0], refined) })
        {
            OdeResult first = ExplicitRungeKutta.Run(RungeKuttaScheme.DormandPrince, orbit, span, y0, options);
            OdeResult second = ExplicitRungeKutta.Run(RungeKuttaScheme.DormandPrince, orbit, span, y0, options);
            Assert.Equal(first.Times, second.Times);
            Assert.Equal(first.States.Count, second.States.Count);
            for (int i = 0; i < first.States.Count; i++)
            {
                AssertBitsEach(first.States[i], second.States[i]);
                for (int j = 0; j < i; j++)
                {
                    Assert.NotSame(first.States[j], first.States[i]);
                }
            }

            Assert.True(first.States.Count > 40);
        }
    }

    /// <summary>The interpolant as it stood before the hoist, kept verbatim as the oracle.</summary>
    private static double[] Reference(RungeKuttaScheme scheme, double t, double h, double[] y, double[][] stages,
        double at, double[]? slope, int[]? nonNegative)
    {
        double s = (at - t) / h;
        int n = y.Length;
        var value = new double[n];
        for (int i = 0; i < n; i++)
        {
            double sum = 0;
            double rate = 0;
            for (int j = 0; j < scheme.InterpolationStages.Length; j++)
            {
                double[] row = scheme.Dense[j];
                double stage = stages[j][i];
                double power = s;
                double lower = 1;
                double weight = 0;
                double gradient = 0;
                for (int p = 0; p < row.Length; p++)
                {
                    weight += row[p] * power;
                    gradient += row[p] * (p + 1) * lower;
                    lower = power;
                    power *= s;
                }

                sum += weight * stage;
                rate += gradient * stage;
            }

            value[i] = y[i] + (h * sum);
            if (slope is not null)
            {
                slope[i] = rate;
            }
        }

        if (nonNegative is not null)
        {
            foreach (int index in nonNegative)
            {
                if (value[index] < 0)
                {
                    value[index] = 0;
                    if (slope is not null)
                    {
                        slope[index] = 0;
                    }
                }
            }
        }

        return value;
    }

    private static double[] Draw(Random random, int n)
    {
        var values = new double[n];
        for (int i = 0; i < n; i++)
        {
            values[i] = (random.NextDouble() - 0.5) * Math.Pow(10, random.Next(-3, 4));
        }

        return values;
    }

    private static void AssertBitsEach(double[] expected, double[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(BitConverter.DoubleToInt64Bits(expected[i]), BitConverter.DoubleToInt64Bits(actual[i]));
        }
    }
}
