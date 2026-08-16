namespace JGraph.Imaging;

/// <summary>
/// Fan-beam projections, as a change of sampling over the parallel-beam ones.
/// <para>
/// A fan ray leaving a vertex a distance <c>D</c> from the centre of rotation, at fan angle
/// <c>γ</c> when the whole fan has turned by <c>β</c>, is the same line through the object as the
/// parallel ray at angle <c>θ = β + γ</c> and signed distance <c>s = D·sin γ</c> from the centre.
/// That single relation is the whole of this file: <see cref="ParallelToFan"/> reads a parallel
/// sinogram where the fan rays fall, <see cref="FanToParallel"/> reads a fan sinogram where the
/// parallel rays fall, and the two transforms on either side — the Radon transform and its inverse —
/// already existed.
/// </para>
/// <para>
/// Writing it this way rather than integrating along fan rays directly is a deliberate trade. A
/// direct forward fan transform would be a little more accurate; a rebinning is exactly the identity
/// above, so a script can check it — <c>fan2para</c> of <c>para2fan</c> is the picture it started
/// with, to interpolation error — which is a stronger claim than a tolerance on a reconstruction.
/// </para>
/// </summary>
public static class FanBeamTransform
{
    /// <summary>Where a fan's detectors sit: on an arc about the vertex, or on a straight line.</summary>
    public enum SensorGeometry
    {
        /// <summary>Equal angles along an arc — the sensor coordinate is the fan angle in degrees.</summary>
        Arc,

        /// <summary>Equal distances along a line — the sensor coordinate is a position in pixels.</summary>
        Line,
    }

    /// <summary>
    /// The sensor coordinates a fan of the given geometry needs to cover an object of radius
    /// <paramref name="radius"/> pixels, symmetric about the middle ray.
    /// </summary>
    /// <param name="distance">The vertex-to-centre distance, in pixels.</param>
    /// <param name="radius">How far from the centre the object reaches, in pixels.</param>
    /// <param name="geometry">Where the sensors sit.</param>
    /// <param name="spacing">Degrees between sensors for an arc, pixels for a line.</param>
    public static double[] SensorPositions(
        double distance, double radius, SensorGeometry geometry, double spacing)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spacing);
        if (!(distance > radius))
        {
            throw new ArgumentException(
                $"the fan vertex has to be outside the object: D is {distance:0.###} and the object "
                + $"reaches {radius:0.###} pixels from the centre.",
                nameof(distance));
        }

        double widest = Math.Asin(radius / distance) * 180 / Math.PI;
        double reach = geometry == SensorGeometry.Arc
            ? widest
            : distance * Math.Tan(widest * Math.PI / 180);

        int half = Math.Max(1, (int)Math.Ceiling(reach / spacing));
        var positions = new double[(2 * half) + 1];
        for (int i = 0; i < positions.Length; i++)
        {
            positions[i] = (i - half) * spacing;
        }

        return positions;
    }

    /// <summary>The fan angle in radians a sensor coordinate stands for.</summary>
    public static double FanAngle(double sensor, double distance, SensorGeometry geometry) =>
        geometry == SensorGeometry.Arc
            ? sensor * Math.PI / 180
            : Math.Atan(sensor / distance);

    /// <summary>
    /// Reads a parallel sinogram where a fan's rays fall. <paramref name="parallel"/> is
    /// <c>radon</c>'s output: one column per angle in <paramref name="thetaDegrees"/>, its rows the
    /// signed distances in <paramref name="offsets"/>.
    /// </summary>
    public static double[,] ParallelToFan(
        double[,] parallel,
        double[] offsets,
        double[] thetaDegrees,
        double[] sensors,
        double[] betaDegrees,
        double distance,
        SensorGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(parallel);
        var fan = new double[sensors.Length, betaDegrees.Length];
        for (int b = 0; b < betaDegrees.Length; b++)
        {
            for (int g = 0; g < sensors.Length; g++)
            {
                double gamma = FanAngle(sensors[g], distance, geometry);
                fan[g, b] = SampleParallel(
                    parallel, offsets, thetaDegrees,
                    distance * Math.Sin(gamma),
                    betaDegrees[b] + (gamma * 180 / Math.PI));
            }
        }

        return fan;
    }

    /// <summary>
    /// Reads a fan sinogram where a parallel set of rays falls — the inverse of
    /// <see cref="ParallelToFan"/>, and what turns fan data into something <c>iradon</c> can invert.
    /// </summary>
    public static double[,] FanToParallel(
        double[,] fan,
        double[] sensors,
        double[] betaDegrees,
        double[] offsets,
        double[] thetaDegrees,
        double distance,
        SensorGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(fan);

        // The sensor coordinates are what the fan is indexed by, so the fan angle each one stands
        // for is worked out once rather than per parallel ray.
        var gammas = new double[sensors.Length];
        for (int i = 0; i < sensors.Length; i++)
        {
            gammas[i] = FanAngle(sensors[i], distance, geometry) * 180 / Math.PI;
        }

        var parallel = new double[offsets.Length, thetaDegrees.Length];
        for (int t = 0; t < thetaDegrees.Length; t++)
        {
            for (int s = 0; s < offsets.Length; s++)
            {
                double ratio = offsets[s] / distance;
                if (Math.Abs(ratio) > 1)
                {
                    continue; // No fan ray passes this far from the centre; the sample stays zero.
                }

                double gamma = Math.Asin(ratio) * 180 / Math.PI;
                parallel[s, t] = SampleFan(
                    fan, gammas, betaDegrees, gamma, thetaDegrees[t] - gamma);
            }
        }

        return parallel;
    }

    /// <summary>
    /// One parallel sample, taken bilinearly and through the symmetry that a projection at
    /// <c>θ + 180°</c> is the same as the one at <c>θ</c> read backwards. That symmetry is what lets
    /// a half-turn of Radon data serve a fan that goes all the way round.
    /// </summary>
    private static double SampleParallel(
        double[,] parallel, double[] offsets, double[] thetaDegrees, double offset, double theta)
    {
        theta = Wrap(theta, 360);
        if (theta >= 180)
        {
            theta -= 180;
            offset = -offset;
        }

        return Bilinear(
            parallel, offsets, thetaDegrees, offset, theta, wrapColumns: 180, mirrorOnWrap: true);
    }

    /// <summary>One fan sample, taken bilinearly, with the rotation angle brought back into a turn.</summary>
    private static double SampleFan(
        double[,] fan, double[] gammas, double[] betaDegrees, double gamma, double beta) =>
        Bilinear(fan, gammas, betaDegrees, gamma, Wrap(beta, 360), wrapColumns: 360);

    /// <summary>
    /// A bilinear read of a table whose rows and columns are given by their own coordinate vectors.
    /// Rows outside the table read as zero — outside the object there is nothing — while columns wrap,
    /// because a rotation angle past the end of the sweep is the same angle come round again.
    /// </summary>
    /// <param name="table">The values, indexed by row then column.</param>
    /// <param name="rows">The coordinate each row stands for, evenly spaced and ascending.</param>
    /// <param name="columns">The coordinate each column stands for, evenly spaced and ascending.</param>
    /// <param name="row">The row coordinate to read at.</param>
    /// <param name="column">The column coordinate to read at.</param>
    /// <param name="wrapColumns">The period the column coordinate repeats over.</param>
    /// <param name="mirrorOnWrap">
    /// Set for a half-turn table, where the column past the last one is the first one <em>read
    /// backwards</em>: a projection at 180° is the projection at 0° seen from the other side. Without
    /// this the last few degrees of a sweep interpolate towards the wrong end of the object, which is
    /// invisible in the middle of a sinogram and wrong at its edges.
    /// </param>
    private static double Bilinear(
        double[,] table,
        double[] rows,
        double[] columns,
        double row,
        double column,
        double wrapColumns,
        bool mirrorOnWrap = false)
    {
        (int r0, int r1, double rf) = Bracket(rows, row);
        if (r0 < 0)
        {
            return 0;
        }

        (int c0, int c1, double cf, bool wrapped) = BracketWrapping(columns, column, wrapColumns);
        (int m0, int m1) = mirrorOnWrap && wrapped
            ? (rows.Length - 1 - r0, rows.Length - 1 - r1)
            : (r0, r1);

        double near = ((1 - rf) * table[r0, c0]) + (rf * table[r1, c0]);
        double far = ((1 - rf) * table[m0, c1]) + (rf * table[m1, c1]);
        return ((1 - cf) * near) + (cf * far);
    }

    /// <summary>The two samples either side of a coordinate, or (-1, -1, 0) when it is outside them.</summary>
    private static (int Low, int High, double Fraction) Bracket(double[] coordinates, double at)
    {
        if (coordinates.Length == 0 || at < coordinates[0] || at > coordinates[^1])
        {
            return (-1, -1, 0);
        }

        // The coordinate vectors here are always evenly spaced, so the index is arithmetic rather
        // than a search — which matters because this runs once per sample of a whole sinogram.
        double step = coordinates.Length > 1
            ? (coordinates[^1] - coordinates[0]) / (coordinates.Length - 1)
            : 1;
        double position = step == 0 ? 0 : (at - coordinates[0]) / step;
        int low = Math.Clamp((int)Math.Floor(position), 0, coordinates.Length - 1);
        int high = Math.Min(low + 1, coordinates.Length - 1);
        return (low, high, position - low);
    }

    /// <summary>As <see cref="Bracket"/>, but the last sample's neighbour is the first one again.</summary>
    private static (int Low, int High, double Fraction, bool Wrapped) BracketWrapping(
        double[] coordinates, double at, double period)
    {
        if (coordinates.Length == 1)
        {
            return (0, 0, 0, false);
        }

        double step = (coordinates[^1] - coordinates[0]) / (coordinates.Length - 1);
        if (step <= 0)
        {
            return (0, 0, 0, false);
        }

        double position = (at - coordinates[0]) / step;
        double turns = period / step;
        position = Wrap(position, turns <= 0 ? coordinates.Length : turns);

        int low = (int)Math.Floor(position);
        double fraction = position - low;
        low = ((low % coordinates.Length) + coordinates.Length) % coordinates.Length;
        int high = (low + 1) % coordinates.Length;
        return (low, high, fraction, high < low);
    }

    private static double Wrap(double value, double period)
    {
        double wrapped = value % period;
        return wrapped < 0 ? wrapped + period : wrapped;
    }
}
