using JGraph.Core.Primitives;
using Xunit;

namespace JGraph.Tests.Primitives;

public class DataRangeTests
{
    [Fact]
    public void Empty_IsIdentityForUnion()
    {
        DataRange result = DataRange.Empty.Union(new DataRange(2, 5));
        Assert.Equal(2, result.Min);
        Assert.Equal(5, result.Max);
    }

    [Fact]
    public void Include_ExpandsToContainValue()
    {
        DataRange range = new DataRange(0, 1).Include(5).Include(-3);
        Assert.Equal(-3, range.Min);
        Assert.Equal(5, range.Max);
    }

    [Fact]
    public void Include_IgnoresNaN()
    {
        DataRange range = new DataRange(0, 1).Include(double.NaN);
        Assert.Equal(new DataRange(0, 1), range);
    }

    [Fact]
    public void Union_TakesOuterBounds()
    {
        DataRange result = new DataRange(0, 4).Union(new DataRange(-2, 3));
        Assert.Equal(new DataRange(-2, 4), result);
    }

    [Fact]
    public void Expand_GrowsSymmetrically()
    {
        DataRange result = new DataRange(0, 10).Expand(0.1);
        Assert.Equal(-1, result.Min, 10);
        Assert.Equal(11, result.Max, 10);
    }

    [Theory]
    [InlineData(0, 1, true)]
    [InlineData(5, 5, false)]   // zero width
    [InlineData(5, 1, false)]   // inverted
    public void IsValid_ReflectsPositiveWidth(double min, double max, bool expected)
    {
        Assert.Equal(expected, new DataRange(min, max).IsValid);
    }

    [Fact]
    public void EnsureValid_ExpandsZeroWidthRange()
    {
        DataRange result = new DataRange(5, 5).EnsureValid();
        Assert.True(result.IsValid);
        Assert.True(result.Contains(5));
    }

    [Fact]
    public void EnsureValid_ReturnsUnitForEmpty()
    {
        Assert.Equal(DataRange.Unit, DataRange.Empty.EnsureValid());
    }
}
