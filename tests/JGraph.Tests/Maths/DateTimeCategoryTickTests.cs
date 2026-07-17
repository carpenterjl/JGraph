using System;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths.Ticks;
using Xunit;

namespace JGraph.Tests.Maths;

public class DateTimeCategoryTickTests
{
    // ---- Category ----

    [Fact]
    public void Category_OneTickPerCategoryWithLabel()
    {
        var gen = new CategoryTickGenerator(new[] { "Mon", "Tue", "Wed" });
        TickSet ticks = gen.Generate(new DataRange(-0.5, 2.5), 5);

        Assert.Equal(3, ticks.MajorTicks.Count);
        Assert.Equal(0, ticks.MajorTicks[0].Value);
        Assert.Equal("Mon", ticks.MajorTicks[0].Label);
        Assert.Equal("Wed", ticks.MajorTicks[2].Label);
        Assert.Empty(ticks.MinorTicks);
    }

    [Fact]
    public void Category_ThinsLabelsWhenCrowded()
    {
        var labels = new string[40];
        for (int i = 0; i < labels.Length; i++)
        {
            labels[i] = "c" + i;
        }

        var gen = new CategoryTickGenerator(labels);
        TickSet ticks = gen.Generate(new DataRange(-0.5, 39.5), 5);
        Assert.True(ticks.MajorTicks.Count < 40);
        Assert.True(ticks.MajorTicks.Count >= 4);
    }

    [Fact]
    public void Category_OnlyShowsVisibleCategories()
    {
        var gen = new CategoryTickGenerator(new[] { "a", "b", "c", "d", "e" });
        TickSet ticks = gen.Generate(new DataRange(1.5, 3.5), 10);
        // Only positions 2 and 3 fall inside [1.5, 3.5].
        Assert.All(ticks.MajorTicks, t => Assert.InRange(t.Value, 2, 3));
    }

    [Fact]
    public void TickGenerators_ForCategoryAxisUsesLabels()
    {
        var axis = new AxisModel(AxisOrientation.Horizontal, AxisPosition.Bottom);
        axis.UseCategories(new[] { "x", "y" });
        ITickGenerator gen = TickGenerators.For(axis);
        Assert.IsType<CategoryTickGenerator>(gen);
    }

    // ---- DateTime ----

    [Fact]
    public void DateTimeAxis_RoundTrips()
    {
        var dt = new DateTime(2026, 7, 15, 13, 30, 0);
        double v = DateTimeAxis.ToValue(dt);
        Assert.Equal(dt, DateTimeAxis.FromValue(v));
    }

    [Fact]
    public void DateTime_OneDaySpanProducesClockTicks()
    {
        double min = DateTimeAxis.ToValue(new DateTime(2026, 1, 1, 0, 0, 0));
        double max = DateTimeAxis.ToValue(new DateTime(2026, 1, 2, 0, 0, 0));
        TickSet ticks = DateTimeTickGenerator.Instance.Generate(new DataRange(min, max), 6);

        Assert.NotEmpty(ticks.MajorTicks);
        Assert.All(ticks.MajorTicks, t => Assert.Contains(":", t.Label));
        AssertAscendingWithinRange(ticks, min, max);
    }

    [Fact]
    public void DateTime_MultiYearSpanProducesYearTicks()
    {
        double min = DateTimeAxis.ToValue(new DateTime(2000, 1, 1));
        double max = DateTimeAxis.ToValue(new DateTime(2025, 1, 1));
        TickSet ticks = DateTimeTickGenerator.Instance.Generate(new DataRange(min, max), 5);

        var labels = ticks.MajorTicks.Select(t => t.Label).ToList();
        Assert.Contains("2000", labels);
        Assert.Contains("2025", labels);
        Assert.All(labels, l => Assert.Matches(@"^\d{4}$", l));
        AssertAscendingWithinRange(ticks, min, max);
    }

    [Fact]
    public void DateTime_AlignsToNaturalBoundaries()
    {
        // Start off a boundary; the first tick should snap to a round clock time.
        double min = DateTimeAxis.ToValue(new DateTime(2026, 1, 1, 3, 17, 0));
        double max = DateTimeAxis.ToValue(new DateTime(2026, 1, 2, 3, 17, 0));
        TickSet ticks = DateTimeTickGenerator.Instance.Generate(new DataRange(min, max), 6);

        DateTime first = DateTimeAxis.FromValue(ticks.MajorTicks[0].Value);
        Assert.Equal(0, first.Minute); // snapped to a whole-hour boundary
        Assert.Equal(0, first.Second);
    }

    [Fact]
    public void TickGenerators_ForDateTimeAxis()
    {
        var axis = new AxisModel(AxisOrientation.Horizontal, AxisPosition.Bottom);
        axis.UseDateTime();
        Assert.IsType<DateTimeTickGenerator>(TickGenerators.For(axis));
    }

    private static void AssertAscendingWithinRange(TickSet ticks, double min, double max)
    {
        for (int i = 0; i < ticks.MajorTicks.Count; i++)
        {
            Assert.InRange(ticks.MajorTicks[i].Value, min - 1e-6, max + 1e-6);
            if (i > 0)
            {
                Assert.True(ticks.MajorTicks[i].Value > ticks.MajorTicks[i - 1].Value);
            }
        }
    }
}
