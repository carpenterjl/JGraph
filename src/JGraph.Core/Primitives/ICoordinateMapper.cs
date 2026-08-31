namespace JGraph.Core.Primitives;

/// <summary>
/// Maps between data space (the values a plot is expressed in) and device/pixel space (where it is
/// drawn). Concrete implementations live in the math layer and account for axis scale, range,
/// inversion, and the plot rectangle. Interaction and hit-testing code depends only on this
/// abstraction so it never needs the rendering backend.
/// </summary>
public interface ICoordinateMapper
{
    /// <summary>The device-space rectangle that data is mapped into.</summary>
    Rect2D PlotArea { get; }

    /// <summary>
    /// Whether one device column stands for a range of data x and nothing else, so that samples may
    /// be reduced column by column before they are mapped. True of a pair of rulers at right angles,
    /// and so the default; false of an angular mapping, where a column crosses every angle it passes
    /// through. Only a reduction that happens before mapping needs to ask.
    /// </summary>
    bool ColumnsAreXRanges => true;

    /// <summary>Maps a data-space coordinate to device/pixel space.</summary>
    Point2D DataToPixel(double x, double y);

    /// <summary>Maps a device/pixel-space coordinate back to data space.</summary>
    Point2D PixelToData(double px, double py);
}
