using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Interaction;

/// <summary>
/// The current data-cursor readout: which plot and data point are under the cursor, the device-space
/// location to draw the marker, and a formatted label. The host renders this as an overlay.
/// </summary>
public sealed class DataCursorInfo
{
    public DataCursorInfo(AxesModel axes, PlotObject target, Point2D dataPoint, Point2D pixelPoint)
    {
        Axes = axes;
        Target = target;
        DataPoint = dataPoint;
        PixelPoint = pixelPoint;
        Label = $"({dataPoint.X:G6}, {dataPoint.Y:G6})";
    }

    public AxesModel Axes { get; }

    public PlotObject Target { get; }

    public Point2D DataPoint { get; }

    public Point2D PixelPoint { get; }

    public string Label { get; }
}
