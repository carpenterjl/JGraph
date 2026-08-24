using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Maths.Transforms;

/// <summary>
/// Maps polar data — an angle in radians and a radius — into the device space of a circular plot
/// area. It is an ordinary <see cref="ICoordinateMapper"/>, which is what lets a polar axes reuse
/// every plot object unchanged: a line plot handed this mapper instead of an
/// <see cref="AxisTransform"/> reads its X data as θ and its Y data as r without knowing it has
/// moved, and the same is true of scatter, stem, error bars and anything written later.
/// </summary>
/// <remarks>
/// <para>
/// A radius outside the visible range maps to a non-finite point rather than a position off the rim,
/// and so does an angle outside the visible turn once <c>thetalim</c> has cut the circle to a wedge —
/// angles are folded by whole turns first, so a bearing of 370° lands on the 10° spoke the way a
/// reader expects. That is this build's answer to clipping: the drawing primitives already break a
/// polyline at a non-finite sample, so a curve that leaves the wedge disappears instead of being drawn
/// over the tick labels. The trim happens at sample granularity — a segment straddling the boundary is
/// dropped whole rather than cut at the crossing.
/// </para>
/// <para>
/// Angles are always radians here, as they are in the data a script hands to <c>polarplot</c>. Degrees
/// belong to the θ ruler, whose ticks and limits a reader speaks in.
/// </para>
/// </remarks>
public sealed class PolarTransform : ICoordinateMapper
{
    private readonly double _centerX;
    private readonly double _centerY;
    private readonly double _pixelRadius;
    private readonly double _rMin;
    private readonly double _rSpan;
    private readonly double _zeroAngle;
    private readonly double _direction;
    private readonly double _thetaMin;
    private readonly double _thetaSpan;

    /// <param name="plotArea">The device rectangle the disc is inscribed in.</param>
    /// <param name="rRange">The radii the innermost and outermost rings stand for.</param>
    /// <param name="zeroLocation">Which compass point θ = 0 sits at.</param>
    /// <param name="direction">Which way θ increases.</param>
    /// <param name="thetaDegrees">The visible turn, or null for the whole circle.</param>
    /// <param name="zeroOffsetDegrees">
    /// A further rotation of the whole chart on top of <paramref name="zeroLocation"/> (M83), which is
    /// what a drag that turns the chart moves. It is added to the zero angle rather than to the visible
    /// turn, because the turn decides which angles are drawn and the zero angle decides where a drawn
    /// one lands.
    /// </param>
    public PolarTransform(
        Rect2D plotArea,
        DataRange rRange,
        ThetaZeroLocation zeroLocation,
        ThetaDirection direction,
        DataRange? thetaDegrees = null,
        double zeroOffsetDegrees = 0)
    {
        PlotArea = plotArea;
        _centerX = plotArea.CenterX;
        _centerY = plotArea.CenterY;
        _pixelRadius = System.Math.Min(plotArea.Width, plotArea.Height) / 2;
        _rMin = rRange.Min;

        double span = rRange.Max - rRange.Min;
        _rSpan = System.Math.Abs(span) > double.Epsilon ? span : 1;

        // Device angles grow clockwise because device Y grows downward, so the anticlockwise data
        // convention is the negative direction on screen and the clockwise one is the positive.
        _zeroAngle = zeroLocation switch
        {
            ThetaZeroLocation.Top => -System.Math.PI / 2,
            ThetaZeroLocation.Left => System.Math.PI,
            ThetaZeroLocation.Bottom => System.Math.PI / 2,
            _ => 0,
        };
        _direction = direction == ThetaDirection.Clockwise ? 1 : -1;

        // The continuous rotation rides on the zero angle, in device terms: a positive offset turns the
        // chart the way θ increases, whichever way that is.
        _zeroAngle += _direction * zeroOffsetDegrees * System.Math.PI / 180;

        // The visible turn, in radians. The default is the whole circle, which every angle folds into,
        // so the wedge test below only ever removes something once thetalim has said less.
        double thetaMinDegrees = thetaDegrees?.Min ?? 0;
        double thetaMaxDegrees = thetaDegrees?.Max ?? 360;
        _thetaMin = thetaMinDegrees * System.Math.PI / 180;
        _thetaSpan = System.Math.Max(0, thetaMaxDegrees - thetaMinDegrees) * System.Math.PI / 180;
    }

    /// <summary>
    /// The share of the plot area's half-width kept clear outside the rim for the angle labels. It is
    /// a fixed fraction rather than a measurement so that anything needing this mapping — the renderer,
    /// hit-testing, a data tip — can rebuild the same one from the model and a rectangle alone.
    /// </summary>
    public const double LabelMargin = 0.12;

    /// <summary>
    /// The mapping a polar axes draws through: the largest circle its plot area holds, less the margin
    /// the angle labels stand in, turned and directed as the axes says.
    /// </summary>
    public static PolarTransform Create(AxesModel axes, Rect2D plotArea)
    {
        ArgumentNullException.ThrowIfNull(axes);

        double side = System.Math.Min(plotArea.Width, plotArea.Height) * (1 - LabelMargin);
        var disc = new Rect2D(
            plotArea.CenterX - (side / 2), plotArea.CenterY - (side / 2), side, side);
        return new PolarTransform(
            disc, axes.RAxis.Range, axes.ThetaZeroLocation, axes.ThetaDirection, axes.ThetaAxis.Range,
            axes.ThetaZeroOffset);
    }

    /// <inheritdoc />
    public Rect2D PlotArea { get; }

    /// <summary>The device-space radius of the outermost ring.</summary>
    public double PixelRadius => _pixelRadius;

    /// <summary>The device-space point the whole chart turns about.</summary>
    public Point2D Center => new(_centerX, _centerY);

    /// <summary>The radius at the middle of the chart — which is not zero once <c>rlim</c> has said so.</summary>
    public double RMin => _rMin;

    /// <summary>The radius at the outermost ring.</summary>
    public double RMax => _rMin + _rSpan;

    /// <inheritdoc />
    public Point2D DataToPixel(double theta, double r)
    {
        double fraction = (r - _rMin) / _rSpan;
        if (!double.IsFinite(fraction) || !double.IsFinite(theta) || fraction < 0 || fraction > 1)
        {
            return new Point2D(double.NaN, double.NaN);
        }

        // The wedge test: fold the angle by whole turns from the start of the visible turn, and a
        // sample past its end vanishes the way one past the rim does. On the default full circle every
        // angle folds inside, so this removes nothing until thetalim asks for less.
        double turn = theta - _thetaMin;
        turn -= System.Math.Tau * System.Math.Floor(turn / System.Math.Tau);
        if (turn > _thetaSpan + 1e-9)
        {
            return new Point2D(double.NaN, double.NaN);
        }

        return Rim(theta, fraction * _pixelRadius);
    }

    /// <summary>
    /// The device point at a data angle and a device radius, with no range check. The chart's own
    /// furniture — rings, spokes and labels — is placed with this: it is drawn at the rim and a little
    /// outside it by construction, so the clipping rule that protects data would erase it.
    /// </summary>
    public Point2D Rim(double theta, double pixelRadius)
    {
        double angle = _zeroAngle + (_direction * theta);
        return new Point2D(
            _centerX + (pixelRadius * System.Math.Cos(angle)),
            _centerY + (pixelRadius * System.Math.Sin(angle)));
    }

    /// <summary>The device radius a data radius stands at, unclamped.</summary>
    public double RadiusToPixels(double r) => (r - _rMin) / _rSpan * _pixelRadius;

    /// <summary>
    /// The change in data angle, in radians, between two device points seen from the centre — the
    /// tangential half of a drag (M83).
    /// </summary>
    /// <remarks>
    /// Unwrapped to the half turn either side of nothing, so a drag that crosses the seam of the atan2
    /// branch reports a small step rather than a whole turn back. Taken from the raw device angles
    /// rather than from two <see cref="PixelToData"/> readings, which fold into the visible turn and
    /// would report the same jump for the same reason.
    /// </remarks>
    public double AngleDeltaBetween(Point2D from, Point2D to)
    {
        double before = System.Math.Atan2(from.Y - _centerY, from.X - _centerX);
        double after = System.Math.Atan2(to.Y - _centerY, to.X - _centerX);
        double delta = (after - before) / _direction;
        delta -= System.Math.Tau * System.Math.Round(delta / System.Math.Tau);
        return delta;
    }

    /// <inheritdoc />
    public Point2D PixelToData(double px, double py)
    {
        double dx = px - _centerX;
        double dy = py - _centerY;
        double pixelRadius = System.Math.Sqrt((dx * dx) + (dy * dy));
        double angle = System.Math.Atan2(dy, dx);

        // Undo the zero offset and the direction, then fold into one turn starting where the visible
        // one does, so a reading inside a wedge answers with the wedge's own angles — a chart cut to
        // [-90°, 90°] should read -45° at that spoke, not the 315° another branch of atan2 would say.
        double theta = (angle - _zeroAngle) / _direction;
        theta -= System.Math.Tau * System.Math.Floor((theta - _thetaMin) / System.Math.Tau);

        double r = _rMin + (pixelRadius / (_pixelRadius > 0 ? _pixelRadius : 1) * _rSpan);
        return new Point2D(theta, r);
    }
}
