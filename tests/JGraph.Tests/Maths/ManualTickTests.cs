using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths.Ticks;
using Xunit;

namespace JGraph.Tests.Maths;

/// <summary>
/// M54 wave C: an axis told where its ticks go, or what they read. The resolver is the thing under
/// test as much as the generator — every caller asks <see cref="TickGenerators.For(AxisModel)"/> for a
/// generator, so deciding it there is what makes a manual tick reach the page.
/// </summary>
public class ManualTickTests
{
    private static AxisModel Axis() => new(AxisOrientation.Horizontal, AxisPosition.Bottom)
    {
        Range = new DataRange(0, 10),
    };

    [Fact]
    public void AnAxisWithNothingToSayGetsItsScalesOwnGenerator()
    {
        Assert.Same(LinearTickGenerator.Instance, TickGenerators.For(Axis()));
    }

    [Fact]
    public void NamedValuesArePlacedExactlyWhereTheyWereNamed()
    {
        AxisModel axis = Axis();
        axis.TickPositions = new[] { 0.0, 3.0, 7.5 };

        TickSet ticks = TickGenerators.For(axis).Generate(axis.Range, axis.TargetMajorTickCount);

        Assert.Equal(new[] { 0.0, 3.0, 7.5 }, ticks.MajorTicks.Select(t => t.Value));
        Assert.Empty(ticks.MinorTicks);
    }

    [Fact]
    public void AValueOutsideTheRangeIsSkippedRatherThanForgotten()
    {
        AxisModel axis = Axis();
        axis.TickPositions = new[] { -5.0, 5.0, 50.0 };

        TickSet ticks = TickGenerators.For(axis).Generate(axis.Range, axis.TargetMajorTickCount);

        Assert.Equal(new[] { 5.0 }, ticks.MajorTicks.Select(t => t.Value));

        // Zooming out brings the others back, which is the point of keeping them.
        TickSet wider = TickGenerators.For(axis).Generate(new DataRange(-100, 100), 5);
        Assert.Equal(3, wider.MajorTicks.Count);
    }

    [Fact]
    public void LabelsAreCycledOverTheTicksAndKeepTheirPlaceWhenZoomed()
    {
        AxisModel axis = Axis();
        axis.TickPositions = new[] { 1.0, 2.0, 3.0, 4.0 };
        axis.TickLabelOverrides = new[] { "odd", "even" };

        Assert.Equal(
            new[] { "odd", "even", "odd", "even" },
            TickGenerators.For(axis).Generate(axis.Range, 5).MajorTicks.Select(t => t.Label));

        // The third tick keeps the third label even when the first two are off-screen.
        Assert.Equal(
            new[] { "odd", "even" },
            TickGenerators.For(axis).Generate(new DataRange(2.5, 10), 5).MajorTicks.Select(t => t.Label));
    }

    [Fact]
    public void LabelsAloneLeaveThePlacementToTheScale()
    {
        AxisModel axis = Axis();
        axis.TickLabelOverrides = new[] { "a", "b", "c" };

        TickSet automatic = LinearTickGenerator.Instance.Generate(axis.Range, axis.TargetMajorTickCount);
        TickSet labelled = TickGenerators.For(axis).Generate(axis.Range, axis.TargetMajorTickCount);

        Assert.Equal(automatic.MajorTicks.Select(t => t.Value), labelled.MajorTicks.Select(t => t.Value));
        Assert.Equal("a", labelled.MajorTicks[0].Label);
        Assert.NotEmpty(labelled.MinorTicks);
    }

    [Fact]
    public void AnEmptyLabelListBlanksEveryTickWithoutMovingThem()
    {
        AxisModel axis = Axis();
        axis.TickLabelOverrides = Array.Empty<string>();

        TickSet ticks = TickGenerators.For(axis).Generate(axis.Range, axis.TargetMajorTickCount);

        Assert.NotEmpty(ticks.MajorTicks);
        Assert.All(ticks.MajorTicks, t => Assert.Equal(string.Empty, t.Label));
    }

    [Fact]
    public void ManualLabelsRideOnTopOfWhicheverScaleTheAxisUses()
    {
        var axis = new AxisModel(AxisOrientation.Vertical, AxisPosition.Left)
        {
            Scale = AxisScaleType.Logarithmic,
            Range = new DataRange(1, 1000),
            TickLabelOverrides = new[] { "x" },
        };

        TickSet ticks = TickGenerators.For(axis).Generate(axis.Range, 4);

        Assert.Contains(ticks.MajorTicks, t => t.Value == 10);
        Assert.All(ticks.MajorTicks, t => Assert.Equal("x", t.Label));
    }

    [Fact]
    public void ManualValuesAreLabelledWithEnoughDecimalsForTheirSpacing()
    {
        AxisModel axis = Axis();
        axis.TickPositions = new[] { 0.0, 0.25, 0.5 };

        TickSet ticks = TickGenerators.For(axis).Generate(new DataRange(0, 1), 5);

        Assert.Equal(new[] { "0.00", "0.25", "0.50" }, ticks.MajorTicks.Select(t => t.Label));
    }
}
