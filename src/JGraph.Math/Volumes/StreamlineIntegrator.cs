namespace JGraph.Maths.Volumes;

/// <summary>How a streamline is traced, and when it is given up on.</summary>
/// <param name="StepScale">
/// The step taken each time, as a fraction of one grid cell. MATLAB's <c>Step</c> option means the
/// same thing and defaults to 0.1. Zero or less means the default.
/// </param>
/// <param name="MaxVertices">
/// The most points a line may reach before it is stopped — MATLAB's second stream option. A line in a
/// closed orbit never leaves the box, so without this it never ends. Zero or less means the default.
/// </param>
/// <remarks>
/// Both defaults are applied where the options are read rather than by the constructor, because a
/// struct can always be made without running one — <c>new StreamlineOptions()</c> and
/// <c>default</c> both hand back zeros — and a zero step traces a line that never moves. Reading a
/// missing value as the default is the only spelling that cannot be got wrong.
/// </remarks>
public readonly record struct StreamlineOptions(double StepScale = 0.1, int MaxVertices = 10_000)
{
    /// <summary>The step actually taken, as a fraction of a cell.</summary>
    public double StepOrDefault => StepScale > 0 ? StepScale : 0.1;

    /// <summary>The point budget actually applied.</summary>
    public int VerticesOrDefault => MaxVertices > 0 ? MaxVertices : 10_000;
}

/// <summary>
/// Follows a vector field from a starting point, one step at a time, until the line leaves the grid
/// or stops going anywhere.
/// </summary>
/// <remarks>
/// <para>
/// The step is second-order Runge–Kutta: the direction is read once at the current point and once at
/// the point half a step along it, and the second reading is the one taken. Euler's method drifts
/// outward on a circular field — a closed orbit spirals visibly wider every turn — and that is the
/// field a stream plot is most often used to look at, so a straight first-order step is not good
/// enough here. The fourth-order step costs twice as many readings for an accuracy no drawing at this
/// scale can show.
/// </para>
/// <para>
/// The step length is a fraction of a grid cell rather than a fixed distance, so the same options
/// behave the same way on a field sampled coarsely and one sampled finely. A line stops when it
/// leaves the box, when the field where it stands is not a direction at all, when it has taken the
/// most points it is allowed, or when the field there is so nearly still that the line has stopped
/// moving — that last one is what keeps a starting point at a stagnation point from spending the
/// whole budget standing still.
/// </para>
/// </remarks>
public static class StreamlineIntegrator
{
    /// <summary>
    /// Traces one line from a starting point. The answer holds at least the starting point, and is
    /// empty only when the start is outside the grid.
    /// </summary>
    public static IReadOnlyList<(double X, double Y, double Z)> Trace(
        VectorField field, double x, double y, double z, StreamlineOptions options)
    {
        ArgumentNullException.ThrowIfNull(field);

        double step = options.StepOrDefault * TypicalCell(field.U);
        int budget = options.VerticesOrDefault;
        var points = new List<(double X, double Y, double Z)>();

        (double u, double v, double w) = field.Sample(x, y, z);
        if (!Finite(u, v, w))
        {
            return points;
        }

        points.Add((x, y, z));
        double smallest = step * 1e-6;

        while (points.Count < budget)
        {
            (u, v, w) = field.Sample(x, y, z);
            if (!Finite(u, v, w))
            {
                break;
            }

            double speed = System.Math.Sqrt((u * u) + (v * v) + (w * w));
            if (speed <= 0)
            {
                break;
            }

            // Midpoint: read the direction again half a step along the first reading, and take that.
            double hx = x + (u / speed * step * 0.5);
            double hy = y + (v / speed * step * 0.5);
            double hz = z + (w / speed * step * 0.5);
            (double mu, double mv, double mw) = field.Sample(hx, hy, hz);
            if (!Finite(mu, mv, mw))
            {
                (mu, mv, mw) = (u, v, w);
            }

            double midSpeed = System.Math.Sqrt((mu * mu) + (mv * mv) + (mw * mw));
            if (midSpeed <= 0)
            {
                break;
            }

            double nx = x + (mu / midSpeed * step);
            double ny = y + (mv / midSpeed * step);
            double nz = z + (mw / midSpeed * step);

            double moved = System.Math.Sqrt(
                ((nx - x) * (nx - x)) + ((ny - y) * (ny - y)) + ((nz - z) * (nz - z)));
            if (moved <= smallest)
            {
                break;
            }

            (double su, double sv, double sw) = field.Sample(nx, ny, nz);
            if (!Finite(su, sv, sw))
            {
                break;
            }

            (x, y, z) = (nx, ny, nz);
            points.Add((x, y, z));
        }

        return points;
    }

    /// <summary>
    /// The size of a typical cell of a vector field's grid — the length everything drawn along a
    /// streamline is scaled by, so a cone or a tube is the same size relative to the picture whatever
    /// units the field is in.
    /// </summary>
    public static double TypicalCellOf(VectorField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return TypicalCell(field.U);
    }

    /// <summary>
    /// The size of a typical cell of the grid — the mean spacing along each direction, combined as a
    /// length. A direction with one position only contributes nothing, which is what lets the same
    /// integrator trace a plane as a box one page deep.
    /// </summary>
    internal static double TypicalCell(ScalarField grid)
    {
        double sx = MeanSpacing(grid.X);
        double sy = MeanSpacing(grid.Y);
        double sz = MeanSpacing(grid.Z);
        double total = (sx * sx) + (sy * sy) + (sz * sz);
        double cell = System.Math.Sqrt(total);
        return cell > 0 ? cell : 1;
    }

    private static double MeanSpacing(double[] positions)
    {
        if (positions.Length < 2)
        {
            return 0;
        }

        double span = System.Math.Abs(positions[^1] - positions[0]);
        return span / (positions.Length - 1);
    }

    private static bool Finite(double u, double v, double w) =>
        double.IsFinite(u) && double.IsFinite(v) && double.IsFinite(w);
}
