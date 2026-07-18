using JGraph.Core.Primitives;

namespace JGraph.Maths.Transforms;

/// <summary>
/// An orthographic (axonometric) projection of a 3D data box onto a 2D plot rectangle, following
/// MATLAB's <c>view(az, el)</c> camera convention. The data box is normalized to a unit cube centered
/// at the origin, rotated by azimuth about the vertical (Z) axis and by elevation toward the viewer,
/// then scale-fit so the rotated cube's screen bounding box fills the plot area. Pure math, no
/// rendering dependencies — built per frame by the 3D axes renderer and shared with interaction.
/// </summary>
public sealed class Projection3D
{
    // Rotation rows of MATLAB's viewmtx(az, el): screen-right (u), screen-up (v), and depth toward
    // the viewer (larger = closer). view(0, 90) reduces to u = x, v = y, depth = z (top-down 2D).
    private readonly double _ux, _uy;             // u row (z coefficient is 0)
    private readonly double _vx, _vy, _vz;        // v row
    private readonly double _dx, _dy, _dz;        // depth row

    private readonly double _xMin, _xSpan;
    private readonly double _yMin, _ySpan;
    private readonly double _zMin, _zSpan;

    private readonly double _scale;
    private readonly double _centerU, _centerV;   // rotated-space center of the cube's screen bbox
    private readonly double _pixelCenterX, _pixelCenterY;

    public Projection3D(
        DataRange xRange,
        DataRange yRange,
        DataRange zRange,
        double azimuthDegrees,
        double elevationDegrees,
        Rect2D plotArea)
    {
        double az = azimuthDegrees * System.Math.PI / 180.0;
        double el = elevationDegrees * System.Math.PI / 180.0;
        double sinAz = System.Math.Sin(az);
        double cosAz = System.Math.Cos(az);
        double sinEl = System.Math.Sin(el);
        double cosEl = System.Math.Cos(el);

        _ux = cosAz;
        _uy = sinAz;
        _vx = -sinEl * sinAz;
        _vy = sinEl * cosAz;
        _vz = cosEl;
        _dx = cosEl * sinAz;
        _dy = -cosEl * cosAz;
        _dz = sinEl;

        _xMin = xRange.Min;
        _xSpan = NonZeroSpan(xRange);
        _yMin = yRange.Min;
        _ySpan = NonZeroSpan(yRange);
        _zMin = zRange.Min;
        _zSpan = NonZeroSpan(zRange);

        // Fit the rotated unit cube's screen bounding box into the plot area, preserving aspect.
        double minU = double.PositiveInfinity, maxU = double.NegativeInfinity;
        double minV = double.PositiveInfinity, maxV = double.NegativeInfinity;
        for (int corner = 0; corner < 8; corner++)
        {
            double x = (corner & 1) == 0 ? -0.5 : 0.5;
            double y = (corner & 2) == 0 ? -0.5 : 0.5;
            double z = (corner & 4) == 0 ? -0.5 : 0.5;
            double u = (_ux * x) + (_uy * y);
            double v = (_vx * x) + (_vy * y) + (_vz * z);
            minU = System.Math.Min(minU, u);
            maxU = System.Math.Max(maxU, u);
            minV = System.Math.Min(minV, v);
            maxV = System.Math.Max(maxV, v);
        }

        double spanU = System.Math.Max(maxU - minU, 1e-9);
        double spanV = System.Math.Max(maxV - minV, 1e-9);
        _scale = System.Math.Min(plotArea.Width / spanU, plotArea.Height / spanV);
        _centerU = (minU + maxU) / 2;
        _centerV = (minV + maxV) / 2;
        _pixelCenterX = plotArea.CenterX;
        _pixelCenterY = plotArea.CenterY;
    }

    /// <summary>
    /// Projects a data-space point. The returned depth increases toward the viewer, so a painter's
    /// algorithm draws primitives in ascending depth order (farthest first).
    /// </summary>
    public (Point2D Position, double Depth) Project(double x, double y, double z)
    {
        double nx = ((x - _xMin) / _xSpan) - 0.5;
        double ny = ((y - _yMin) / _ySpan) - 0.5;
        double nz = ((z - _zMin) / _zSpan) - 0.5;

        double u = (_ux * nx) + (_uy * ny);
        double v = (_vx * nx) + (_vy * ny) + (_vz * nz);
        double depth = (_dx * nx) + (_dy * ny) + (_dz * nz);

        double px = _pixelCenterX + ((u - _centerU) * _scale);
        double py = _pixelCenterY - ((v - _centerV) * _scale); // screen Y grows downward
        return (new Point2D(px, py), depth);
    }

    /// <summary>Projects a data-space point, discarding the depth.</summary>
    public Point2D ProjectPoint(double x, double y, double z) => Project(x, y, z).Position;

    private static double NonZeroSpan(DataRange range)
    {
        double span = range.Max - range.Min;
        return System.Math.Abs(span) < 1e-300 ? 1 : span;
    }
}
