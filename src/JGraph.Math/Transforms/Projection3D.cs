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
    private readonly double _ux, _uy, _uz;        // u row (z coefficient is 0 until the camera rolls)
    private readonly double _vx, _vy, _vz;        // v row
    private readonly double _dx, _dy, _dz;        // depth row

    private readonly double _xMin, _xSpan;
    private readonly double _yMin, _ySpan;
    private readonly double _zMin, _zSpan;

    private readonly double _ax, _ay, _az;        // plot box aspect, scaled so the largest side is 1

    private readonly double _scale;
    private readonly double _centerU, _centerV;   // rotated-space center of the cube's screen bbox
    private readonly double _pixelCenterX, _pixelCenterY;

    // Set only by the camera constructor. Under the angle constructor the rays stay parallel, so the
    // distance is never consulted and the divide below never runs.
    private readonly bool _perspective;
    private readonly double _distance;            // camera to target, in normalized cube units
    private readonly double _targetDepth;         // where the target sits along the depth row

    /// <summary>
    /// Builds the projection. <c>boxAspect</c> gives the relative lengths of the plot box's three
    /// sides (MATLAB <c>pbaspect</c>); the default — equal on all three — is the cube every 3D axes
    /// drew before M45, and any aspect is rescaled so the longest side is 1, so the box still fits
    /// the plot area whatever magnitudes the caller used.
    /// </summary>
    public Projection3D(
        DataRange xRange,
        DataRange yRange,
        DataRange zRange,
        double azimuthDegrees,
        double elevationDegrees,
        Rect2D plotArea,
        Vector3D? boxAspect = null,
        double rollDegrees = 0)
    {
        double az = azimuthDegrees * System.Math.PI / 180.0;
        double el = elevationDegrees * System.Math.PI / 180.0;
        double sinAz = System.Math.Sin(az);
        double cosAz = System.Math.Cos(az);
        double sinEl = System.Math.Sin(el);
        double cosEl = System.Math.Cos(el);

        double ux = cosAz, uy = sinAz, uz = 0;
        double vx = -sinEl * sinAz, vy = sinEl * cosAz, vz = cosEl;

        // Roll turns the camera about the direction it is already looking, so it mixes screen-right
        // and screen-up with each other and leaves the depth row alone. Doing it here means the fit
        // below measures the rolled box, which is what keeps a rolled axes inside its plot area.
        if (rollDegrees != 0)
        {
            double roll = rollDegrees * System.Math.PI / 180.0;
            double sinRoll = System.Math.Sin(roll);
            double cosRoll = System.Math.Cos(roll);
            (ux, vx) = ((cosRoll * ux) + (sinRoll * vx), (cosRoll * vx) - (sinRoll * ux));
            (uy, vy) = ((cosRoll * uy) + (sinRoll * vy), (cosRoll * vy) - (sinRoll * uy));
            (uz, vz) = ((cosRoll * uz) + (sinRoll * vz), (cosRoll * vz) - (sinRoll * uz));
        }

        _ux = ux;
        _uy = uy;
        _uz = uz;
        _vx = vx;
        _vy = vy;
        _vz = vz;
        _dx = cosEl * sinAz;
        _dy = -cosEl * cosAz;
        _dz = sinEl;

        _xMin = xRange.Min;
        _xSpan = NonZeroSpan(xRange);
        _yMin = yRange.Min;
        _ySpan = NonZeroSpan(yRange);
        _zMin = zRange.Min;
        _zSpan = NonZeroSpan(zRange);

        (_ax, _ay, _az) = NormalizeAspect(boxAspect);

        // Fit the rotated box's screen bounding box into the plot area, preserving aspect.
        double minU = double.PositiveInfinity, maxU = double.NegativeInfinity;
        double minV = double.PositiveInfinity, maxV = double.NegativeInfinity;
        for (int corner = 0; corner < 8; corner++)
        {
            double x = ((corner & 1) == 0 ? -0.5 : 0.5) * _ax;
            double y = ((corner & 2) == 0 ? -0.5 : 0.5) * _ay;
            double z = ((corner & 4) == 0 ? -0.5 : 0.5) * _az;
            double u = (_ux * x) + (_uy * y) + (_uz * z);
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
    /// Builds the projection from a placed camera rather than from viewing angles: the camera stands
    /// at <paramref name="cameraPosition"/>, looks at <paramref name="cameraTarget"/> with
    /// <paramref name="upVector"/> pointing up the screen, and sees a cone
    /// <paramref name="viewAngleDegrees"/> wide — or, when that is null, exactly as much as fits, which
    /// is what MATLAB's automatic view angle means. All three vectors are in data coordinates.
    /// The target lands at the center of the plot area, so moving the target pans the picture.
    /// </summary>
    public Projection3D(
        DataRange xRange,
        DataRange yRange,
        DataRange zRange,
        Rect2D plotArea,
        Vector3D? boxAspect,
        double rollDegrees,
        Vector3D cameraPosition,
        Vector3D cameraTarget,
        Vector3D upVector,
        double? viewAngleDegrees,
        bool perspective)
    {
        _xMin = xRange.Min;
        _xSpan = NonZeroSpan(xRange);
        _yMin = yRange.Min;
        _ySpan = NonZeroSpan(yRange);
        _zMin = zRange.Min;
        _zSpan = NonZeroSpan(zRange);

        (_ax, _ay, _az) = NormalizeAspect(boxAspect);

        // Everything below happens in the normalized cube: it is the space the box is a box in, so a
        // camera placed in data units looks the same whatever magnitudes the three axes carry.
        Vector3D position = Normalize(cameraPosition.X, cameraPosition.Y, cameraPosition.Z);
        Vector3D target = Normalize(cameraTarget.X, cameraTarget.Y, cameraTarget.Z);

        // Up is a direction, not a place, so it takes only the scale part of the mapping.
        var up = new Vector3D(
            upVector.X * _ax / _xSpan,
            upVector.Y * _ay / _ySpan,
            upVector.Z * _az / _zSpan);

        (double dx, double dy, double dz) = Unit(
            position.X - target.X, position.Y - target.Y, position.Z - target.Z);
        if (dx == 0 && dy == 0 && dz == 0)
        {
            // A camera standing on its own target has no direction to look; keep the +z view MATLAB
            // falls back to rather than dividing by zero.
            (dx, dy, dz) = (0, 0, 1);
        }

        (double ux, double uy, double uz) = Unit(
            (up.Y * dz) - (up.Z * dy), (up.Z * dx) - (up.X * dz), (up.X * dy) - (up.Y * dx));
        if (ux == 0 && uy == 0 && uz == 0)
        {
            // Looking straight along up: no screen-right can be derived from it, so borrow the world
            // axis the view leans on least, which is what MATLAB does with a degenerate up vector.
            double ax = System.Math.Abs(dx), ay = System.Math.Abs(dy), az = System.Math.Abs(dz);
            Vector3D substitute = ax <= ay && ax <= az ? new Vector3D(1, 0, 0)
                : ay <= az ? new Vector3D(0, 1, 0)
                : new Vector3D(0, 0, 1);
            (ux, uy, uz) = Unit(
                (substitute.Y * dz) - (substitute.Z * dy),
                (substitute.Z * dx) - (substitute.X * dz),
                (substitute.X * dy) - (substitute.Y * dx));
        }

        // v completes the right-handed frame: up the screen, exactly perpendicular to the other two.
        double vx = (dy * uz) - (dz * uy);
        double vy = (dz * ux) - (dx * uz);
        double vz = (dx * uy) - (dy * ux);

        if (rollDegrees != 0)
        {
            double roll = rollDegrees * System.Math.PI / 180.0;
            double sinRoll = System.Math.Sin(roll);
            double cosRoll = System.Math.Cos(roll);
            (ux, vx) = ((cosRoll * ux) + (sinRoll * vx), (cosRoll * vx) - (sinRoll * ux));
            (uy, vy) = ((cosRoll * uy) + (sinRoll * vy), (cosRoll * vy) - (sinRoll * uy));
            (uz, vz) = ((cosRoll * uz) + (sinRoll * vz), (cosRoll * vz) - (sinRoll * uz));
        }

        _ux = ux;
        _uy = uy;
        _uz = uz;
        _vx = vx;
        _vy = vy;
        _vz = vz;
        _dx = dx;
        _dy = dy;
        _dz = dz;

        // The target is what the camera is pointed at, so it is what the plot area centers on.
        _centerU = (ux * target.X) + (uy * target.Y) + (uz * target.Z);
        _centerV = (vx * target.X) + (vy * target.Y) + (vz * target.Z);
        _targetDepth = (dx * target.X) + (dy * target.Y) + (dz * target.Z);

        double distance = System.Math.Sqrt(
            ((position.X - target.X) * (position.X - target.X))
            + ((position.Y - target.Y) * (position.Y - target.Y))
            + ((position.Z - target.Z) * (position.Z - target.Z)));
        _distance = System.Math.Max(distance, 1e-9);
        _perspective = perspective;

        if (viewAngleDegrees is { } angle)
        {
            // A chosen angle sets the scale outright: halving the angle doubles the picture, which is
            // the whole of camzoom.
            double half = angle * System.Math.PI / 360.0;
            double visible = 2 * _distance * System.Math.Tan(half);
            _scale = System.Math.Min(plotArea.Width, plotArea.Height) / System.Math.Max(visible, 1e-9);
        }
        else
        {
            // The automatic angle is the fit, so measure the box the way the angle constructor does —
            // about the target this camera centers on rather than about the box's own middle.
            double minU = double.PositiveInfinity, maxU = double.NegativeInfinity;
            double minV = double.PositiveInfinity, maxV = double.NegativeInfinity;
            for (int corner = 0; corner < 8; corner++)
            {
                double x = ((corner & 1) == 0 ? -0.5 : 0.5) * _ax;
                double y = ((corner & 2) == 0 ? -0.5 : 0.5) * _ay;
                double z = ((corner & 4) == 0 ? -0.5 : 0.5) * _az;
                double u = System.Math.Abs((ux * x) + (uy * y) + (uz * z) - _centerU);
                double v = System.Math.Abs((vx * x) + (vy * y) + (vz * z) - _centerV);
                minU = System.Math.Min(minU, -u);
                maxU = System.Math.Max(maxU, u);
                minV = System.Math.Min(minV, -v);
                maxV = System.Math.Max(maxV, v);
            }

            double spanU = System.Math.Max(maxU - minU, 1e-9);
            double spanV = System.Math.Max(maxV - minV, 1e-9);
            _scale = System.Math.Min(plotArea.Width / spanU, plotArea.Height / spanV);
        }

        _pixelCenterX = plotArea.CenterX;
        _pixelCenterY = plotArea.CenterY;
    }

    /// <summary>
    /// Projects a data-space point. The returned depth increases toward the viewer, so a painter's
    /// algorithm draws primitives in ascending depth order (farthest first).
    /// </summary>
    public (Point2D Position, double Depth) Project(double x, double y, double z)
    {
        double nx = (((x - _xMin) / _xSpan) - 0.5) * _ax;
        double ny = (((y - _yMin) / _ySpan) - 0.5) * _ay;
        double nz = (((z - _zMin) / _zSpan) - 0.5) * _az;

        double u = (_ux * nx) + (_uy * ny) + (_uz * nz);
        double v = (_vx * nx) + (_vy * ny) + (_vz * nz);
        double depth = (_dx * nx) + (_dy * ny) + (_dz * nz);

        if (_perspective)
        {
            // MATLAB's viewmtx divide, said about the target plane: something at the target's own
            // depth is drawn life size, nearer things grow, farther things shrink. A point behind the
            // camera would flip through infinity, so the denominator is held just short of zero and
            // the point flung far outside the plot area, where the clip already deals with it.
            double toward = depth - _targetDepth;
            double denominator = System.Math.Max(_distance - toward, 1e-6 * _distance);
            double w = _distance / denominator;
            u = _centerU + ((u - _centerU) * w);
            v = _centerV + ((v - _centerV) * w);
        }

        double px = _pixelCenterX + ((u - _centerU) * _scale);
        double py = _pixelCenterY - ((v - _centerV) * _scale); // screen Y grows downward
        return (new Point2D(px, py), depth);
    }

    /// <summary>Projects a data-space point, discarding the depth.</summary>
    public Point2D ProjectPoint(double x, double y, double z) => Project(x, y, z).Position;

    /// <summary>
    /// The unit direction toward the viewer, in normalized cube space. Under an orthographic camera
    /// this is the same everywhere, which is what lets lighting resolve one view vector per frame.
    /// </summary>
    public Vector3D ViewDirection => new(_dx, _dy, _dz);

    /// <summary>The unit direction that appears as "up" on screen, in normalized cube space.</summary>
    public Vector3D ScreenUp => new(_vx, _vy, _vz);

    /// <summary>The unit direction that appears as "right" on screen, in normalized cube space.</summary>
    public Vector3D ScreenRight => new(_ux, _uy, _uz);

    /// <summary>
    /// Maps a data-space point into the plot box the camera works in: each axis' visible range becomes
    /// its side of the box, centered on the origin and at most 1 long. Surface normals and light
    /// directions are computed here rather than in data units, so a surface whose Z spans millions and
    /// whose X spans ones does not light like a wall — and a stretched box lights as it looks.
    /// </summary>
    public Vector3D Normalize(double x, double y, double z) => new(
        ((((x - _xMin) / _xSpan) - 0.5) * _ax),
        ((((y - _yMin) / _ySpan) - 0.5) * _ay),
        ((((z - _zMin) / _zSpan) - 0.5) * _az));

    /// <summary>The plot box's side lengths, scaled so the longest is 1.</summary>
    public Vector3D BoxAspect => new(_ax, _ay, _az);

    /// <summary>
    /// Reads a pixel back as the line of sight through it: the two data-space points where that line
    /// enters and leaves the plot box, nearest to the camera first. This is what MATLAB's axes
    /// <c>CurrentPoint</c> reports, and a click cannot name a single 3D point without it. A pixel
    /// whose line misses the box altogether answers the point at the target's own depth twice, which
    /// is the sight line's most useful single point rather than a refusal.
    /// </summary>
    public (Vector3D Front, Vector3D Back) Unproject(double pixelX, double pixelY)
    {
        double u = _centerU + ((pixelX - _pixelCenterX) / _scale);
        double v = _centerV - ((pixelY - _pixelCenterY) / _scale);

        // The point the sight line passes through at the depth the camera is focused on. Under a
        // perspective camera that plane is drawn life size, so this inverts the divide exactly.
        var through = new Vector3D(
            (_ux * u) + (_vx * v) + (_dx * _targetDepth),
            (_uy * u) + (_vy * v) + (_dy * _targetDepth),
            (_uz * u) + (_vz * v) + (_dz * _targetDepth));

        Vector3D origin, direction;
        if (_perspective)
        {
            // Rays fan out from where the camera stands, which is one focal distance back along the
            // depth row from the point of focus.
            origin = new Vector3D(
                _centerU * _ux + _centerV * _vx + ((_targetDepth + _distance) * _dx),
                _centerU * _uy + _centerV * _vy + ((_targetDepth + _distance) * _dy),
                _centerU * _uz + _centerV * _vz + ((_targetDepth + _distance) * _dz));
            (double ex, double ey, double ez) = Unit(
                through.X - origin.X, through.Y - origin.Y, through.Z - origin.Z);
            direction = new Vector3D(ex, ey, ez);
        }
        else
        {
            origin = through;
            direction = new Vector3D(-_dx, -_dy, -_dz); // away from the viewer, so t grows into the box
        }

        if (!ClipToBox(origin, direction, out double enter, out double exit))
        {
            Vector3D focus = Denormalize(through);
            return (focus, focus);
        }

        return (
            Denormalize(new Vector3D(
                origin.X + (direction.X * enter),
                origin.Y + (direction.Y * enter),
                origin.Z + (direction.Z * enter))),
            Denormalize(new Vector3D(
                origin.X + (direction.X * exit),
                origin.Y + (direction.Y * exit),
                origin.Z + (direction.Z * exit))));
    }

    /// <summary>Undoes <see cref="Normalize"/>: a point in the plot box read back in data units.</summary>
    public Vector3D Denormalize(Vector3D boxPoint) => new(
        (((boxPoint.X / _ax) + 0.5) * _xSpan) + _xMin,
        (((boxPoint.Y / _ay) + 0.5) * _ySpan) + _yMin,
        (((boxPoint.Z / _az) + 0.5) * _zSpan) + _zMin);

    /// <summary>
    /// The stretch of a ray that lies inside the plot box, by the slab method: for each axis the ray
    /// is inside between two crossings, and it is inside the box while it is inside all three.
    /// </summary>
    private bool ClipToBox(Vector3D origin, Vector3D direction, out double enter, out double exit)
    {
        enter = double.NegativeInfinity;
        exit = double.PositiveInfinity;

        for (int axis = 0; axis < 3; axis++)
        {
            double half = (axis == 0 ? _ax : axis == 1 ? _ay : _az) / 2;
            double o = axis == 0 ? origin.X : axis == 1 ? origin.Y : origin.Z;
            double d = axis == 0 ? direction.X : axis == 1 ? direction.Y : direction.Z;

            if (System.Math.Abs(d) < 1e-12)
            {
                // Parallel to this pair of walls: either always between them or never.
                if (o < -half || o > half)
                {
                    return false;
                }

                continue;
            }

            double first = (-half - o) / d;
            double second = (half - o) / d;
            if (first > second)
            {
                (first, second) = (second, first);
            }

            enter = System.Math.Max(enter, first);
            exit = System.Math.Min(exit, second);
            if (enter > exit)
            {
                return false;
            }
        }

        return double.IsFinite(enter) && double.IsFinite(exit);
    }

    /// <summary>A vector scaled to length one, or all zeros when it has no length to scale.</summary>
    private static (double X, double Y, double Z) Unit(double x, double y, double z)
    {
        double length = System.Math.Sqrt((x * x) + (y * y) + (z * z));
        return length < 1e-12 ? (0, 0, 0) : (x / length, y / length, z / length);
    }

    /// <summary>
    /// Rescales a requested box aspect so the longest side is 1, which keeps the fit below unchanged
    /// whatever magnitude the caller used. A non-positive or non-finite component falls back to a cube
    /// rather than collapsing the box to a plane.
    /// </summary>
    private static (double X, double Y, double Z) NormalizeAspect(Vector3D? aspect)
    {
        if (aspect is not { } a
            || !double.IsFinite(a.X) || !double.IsFinite(a.Y) || !double.IsFinite(a.Z)
            || a.X <= 0 || a.Y <= 0 || a.Z <= 0)
        {
            return (1, 1, 1);
        }

        double longest = System.Math.Max(a.X, System.Math.Max(a.Y, a.Z));
        return (a.X / longest, a.Y / longest, a.Z / longest);
    }

    private static double NonZeroSpan(DataRange range)
    {
        double span = range.Max - range.Min;
        return System.Math.Abs(span) < 1e-300 ? 1 : span;
    }
}
