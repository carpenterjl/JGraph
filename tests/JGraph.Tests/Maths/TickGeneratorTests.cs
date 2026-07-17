using JGraph.Core.Primitives;
using JGraph.Maths.Ticks;
using Xunit;

namespace JGraph.Tests.Maths;

public class TickGeneratorTests
{
    [Fact]
    public void Linear_ProducesRoundStep()
    {
        TickSet ticks = LinearTickGenerator.Instance.Generate(new DataRange(0, 100), 5);
        Assert.Equal(20, ticks.Step, 10);
        Assert.Contains(ticks.MajorTicks, t => t.Value == 0);
        Assert.Contains(ticks.MajorTicks, t => t.Value == 100);
    }

    [Fact]
    public void Linear_TickCountNearTarget()
    {
        TickSet ticks = LinearTickGenerator.Instance.Generate(new DataRange(0, 1), 5);
        Assert.InRange(ticks.MajorTicks.Count, 4, 8);
    }

    [Fact]
    public void Linear_TicksAreAscendingAndWithinRange()
    {
        TickSet ticks = LinearTickGenerator.Instance.Generate(new DataRange(-3.2, 7.9), 6);
        for (int i = 1; i < ticks.MajorTicks.Count; i++)
        {
            Assert.True(ticks.MajorTicks[i].Value > ticks.MajorTicks[i - 1].Value);
        }

        Assert.All(ticks.MajorTicks, t => Assert.InRange(t.Value, -3.2, 7.9));
    }

    [Fact]
    public void Linear_SnapsAwayFloatingPointNoise()
    {
        TickSet ticks = LinearTickGenerator.Instance.Generate(new DataRange(0, 1), 10);
        // 0.1 spacing must produce a clean 0.3, not 0.30000000000000004.
        Assert.Contains(ticks.MajorTicks, t => System.Math.Abs(t.Value - 0.3) < 1e-12 && t.Label == "0.3");
    }

    [Fact]
    public void Linear_InvalidRangeYieldsEmpty()
    {
        Assert.Empty(LinearTickGenerator.Instance.Generate(DataRange.Empty, 5).MajorTicks);
        Assert.Empty(LinearTickGenerator.Instance.Generate(new DataRange(5, 5), 5).MajorTicks);
    }

    [Fact]
    public void Linear_HonorsLabelFormat()
    {
        TickSet ticks = LinearTickGenerator.Instance.Generate(new DataRange(0, 1), 5, "P0");
        Assert.All(ticks.MajorTicks, t => Assert.Contains("%", t.Label));
    }

    [Fact]
    public void Log_ProducesDecadeMajors()
    {
        TickSet ticks = LogarithmicTickGenerator.Instance.Generate(new DataRange(1, 1000), 5);
        double[] values = ticks.MajorTicks.Select(t => t.Value).ToArray();
        Assert.Contains(1, values);
        Assert.Contains(10, values);
        Assert.Contains(100, values);
        Assert.Contains(1000, values);
    }

    [Fact]
    public void Log_MinorTicksBetweenDecades()
    {
        TickSet ticks = LogarithmicTickGenerator.Instance.Generate(new DataRange(1, 10), 5);
        // 2..9 within the first decade.
        Assert.Contains(2.0, ticks.MinorTicks);
        Assert.Contains(5.0, ticks.MinorTicks);
    }
}
