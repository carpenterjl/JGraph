using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>
/// What the renderer measured for one axes on the last frame it drew: the cell it was given, the
/// plot box it ended up with, the margins the text claimed, and the canvas those are fractions of.
/// </summary>
/// <remarks>
/// This is the channel MATLAB's <c>Position</c>, <c>InnerPosition</c>, <c>OuterPosition</c> and
/// <c>TightInset</c> read through: three of the four are answers about pixels the renderer chose,
/// and nothing but the renderer knows them. It is a report, never an instruction — writing it back
/// into the model would make the next frame depend on the last one.
/// </remarks>
/// <param name="OuterPx">The cell the axes occupies, in device pixels.</param>
/// <param name="PlotAreaPx">The plot box inside it, after the margins were deflated.</param>
/// <param name="TightInsetPx">
/// The part of those margins the ticks, labels and titles claimed. Excludes the colorbar and the
/// extra rulers <c>yyaxis</c> stands outside the right edge, which MATLAB's TightInset excludes too.
/// </param>
/// <param name="CanvasPx">The figure region the normalized bounds are fractions of.</param>
public readonly record struct AxesLayoutSnapshot(
    Rect2D OuterPx,
    Rect2D PlotAreaPx,
    Thickness TightInsetPx,
    Rect2D CanvasPx)
{
    /// <summary>Expresses a device rectangle as fractions of the canvas, still with Y downward.</summary>
    public Rect2D Normalize(Rect2D device)
    {
        double width = CanvasPx.Width > 0 ? CanvasPx.Width : 1;
        double height = CanvasPx.Height > 0 ? CanvasPx.Height : 1;
        return new Rect2D(
            (device.X - CanvasPx.X) / width,
            (device.Y - CanvasPx.Y) / height,
            device.Width / width,
            device.Height / height);
    }

    /// <summary>
    /// What the layout would come to for an axes nothing has drawn yet: the same arithmetic the
    /// renderer does, with a guess at the text sizes instead of a measurement. A script that asks
    /// where an axes sits before the first frame gets this rather than nothing, which is the only
    /// honest answer available and is close enough to place a second axes beside it.
    /// </summary>
    public static AxesLayoutSnapshot Estimate(AxesModel axes, Size2D canvasSize)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var canvas = new Rect2D(0, 0, canvasSize.Width, canvasSize.Height);
        Thickness inset = EstimateInset(axes);
        Rect2D normalized = axes.InnerTarget ?? axes.NormalizedBounds;
        var placed = new Rect2D(
            normalized.X * canvas.Width,
            normalized.Y * canvas.Height,
            normalized.Width * canvas.Width,
            normalized.Height * canvas.Height);

        Rect2D outer = axes.InnerTarget is null
            ? placed
            : new Rect2D(
                placed.X - inset.Left,
                placed.Y - inset.Top,
                placed.Width + inset.Horizontal,
                placed.Height + inset.Vertical);

        return new AxesLayoutSnapshot(outer, outer.Deflate(inset), inset, canvas);
    }

    /// <summary>
    /// A reasonable margin estimate from which decorations are present, in pixels. Used before the
    /// first frame and by the layout tests; the renderer measures the real text instead.
    /// </summary>
    public static Thickness EstimateInset(AxesModel axes)
    {
        ArgumentNullException.ThrowIfNull(axes);
        bool hasTitle = !string.IsNullOrEmpty(axes.Title);
        bool xLabels = axes.PrimaryXAxis.ShowTickLabels;
        bool xTitle = !string.IsNullOrEmpty(axes.PrimaryXAxis.Label);
        bool yLabels = axes.PrimaryYAxis.ShowTickLabels;
        bool yTitle = !string.IsNullOrEmpty(axes.PrimaryYAxis.Label);

        double left = 12 + (yLabels ? 34 : 0) + (yTitle ? 18 : 0);
        double bottom = 10 + (xLabels ? 20 : 0) + (xTitle ? 18 : 0);
        double top = 10 + (hasTitle ? 24 : 0);
        double right = 12;

        return new Thickness(left, top, right, bottom);
    }

    /// <summary>Expresses the tight inset as fractions of the canvas, side by side.</summary>
    public Thickness NormalizeInset()
    {
        double width = CanvasPx.Width > 0 ? CanvasPx.Width : 1;
        double height = CanvasPx.Height > 0 ? CanvasPx.Height : 1;
        return new Thickness(
            TightInsetPx.Left / width,
            TightInsetPx.Top / height,
            TightInsetPx.Right / width,
            TightInsetPx.Bottom / height);
    }
}
