using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Tests.TestDoubles;

/// <summary>A minimal concrete <see cref="PlotObject"/> for exercising the object model in tests.</summary>
internal sealed class TestPlot : PlotObject
{
    public TestPlot(DataRange xBounds, DataRange yBounds)
    {
        XBoundsValue = xBounds;
        YBoundsValue = yBounds;
        Name = "TestPlot";
    }

    public DataRange XBoundsValue { get; set; }

    public DataRange YBoundsValue { get; set; }

    public override DataRange GetXDataBounds() => XBoundsValue;

    public override DataRange GetYDataBounds() => YBoundsValue;
}
