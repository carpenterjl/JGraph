namespace JGraph.Maths.Geometry;

/// <summary>
/// The parametric coordinate grids behind MATLAB's shape generators — <c>sphere</c>,
/// <c>cylinder</c> and <c>ellipsoid</c>. Each returns the three matrices a surface is drawn from,
/// so the caller can plot them, transform them, or hand them straight back to the script.
/// </summary>
/// <remarks>
/// These are the reason a surface had to grow a parametric form: every one of them folds back over
/// itself in X and Y, so there is no pair of generating vectors that could describe it.
/// </remarks>
public static class ShapeGrids
{
    /// <summary>
    /// The unit sphere as an <c>(n+1)</c>-by-<c>(n+1)</c> grid, rows sweeping latitude from the south
    /// pole to the north and columns sweeping longitude once around.
    /// </summary>
    /// <remarks>
    /// The poles and the seam are pinned to exact zeros rather than left to <c>cos</c> and
    /// <c>sin</c> of a computed multiple of pi. Without that the top and bottom rows come out a few
    /// times 1e-17 away from the axis instead of on it, which is enough to leave a visible ring of
    /// slivers where the whole row should collapse to a point.
    /// </remarks>
    public static (double[,] X, double[,] Y, double[,] Z) Sphere(int n) =>
        Ellipsoid(0, 0, 0, 1, 1, 1, n);

    /// <summary>
    /// An ellipsoid centred at <c>(cx, cy, cz)</c> with semi-axes <c>(rx, ry, rz)</c>, as an
    /// <c>(n+1)</c>-by-<c>(n+1)</c> grid.
    /// </summary>
    public static (double[,] X, double[,] Y, double[,] Z) Ellipsoid(
        double cx, double cy, double cz, double rx, double ry, double rz, int n)
    {
        if (n < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(n), n, "An ellipsoid needs at least one facet.");
        }

        int size = n + 1;
        var x = new double[size, size];
        var y = new double[size, size];
        var z = new double[size, size];

        // Longitude runs from -pi to +pi and latitude from -pi/2 to +pi/2, both inclusive, so the
        // first and last column meet at the seam and the first and last row are the two poles.
        var cosTheta = new double[size];
        var sinTheta = new double[size];
        for (int c = 0; c < size; c++)
        {
            double theta = ((2.0 * c / n) - 1) * System.Math.PI;
            cosTheta[c] = System.Math.Cos(theta);
            sinTheta[c] = System.Math.Sin(theta);
        }

        sinTheta[0] = 0;
        sinTheta[n] = 0;
        cosTheta[0] = -1;
        cosTheta[n] = -1;

        for (int r = 0; r < size; r++)
        {
            double phi = ((2.0 * r / n) - 1) * (System.Math.PI / 2);
            double cosPhi = r == 0 || r == n ? 0 : System.Math.Cos(phi);
            double sinPhi = System.Math.Sin(phi);
            for (int c = 0; c < size; c++)
            {
                x[r, c] = cx + (rx * cosPhi * cosTheta[c]);
                y[r, c] = cy + (ry * cosPhi * sinTheta[c]);
                z[r, c] = cz + (rz * sinPhi);
            }
        }

        return (x, y, z);
    }

    /// <summary>
    /// A surface of revolution around the Z axis: <paramref name="radii"/> is the profile curve,
    /// sampled at <c>m</c> evenly spaced heights from 0 to 1, swept into <c>n</c> facets around.
    /// The result is <c>m</c>-by-<c>(n+1)</c>, which for the default profile is MATLAB's cylinder.
    /// </summary>
    /// <remarks>
    /// A single radius describes a cylinder of constant width, which needs two profile points rather
    /// than one, so it is duplicated — the same reading MATLAB gives <c>cylinder(2)</c>.
    /// </remarks>
    public static (double[,] X, double[,] Y, double[,] Z) Cylinder(double[] radii, int n)
    {
        ArgumentNullException.ThrowIfNull(radii);
        if (n < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(n), n, "A cylinder needs at least one facet.");
        }

        if (radii.Length == 0)
        {
            throw new ArgumentException("A cylinder needs at least one radius.", nameof(radii));
        }

        double[] profile = radii.Length == 1 ? [radii[0], radii[0]] : radii;
        int rows = profile.Length;
        int cols = n + 1;
        var x = new double[rows, cols];
        var y = new double[rows, cols];
        var z = new double[rows, cols];

        var cosTheta = new double[cols];
        var sinTheta = new double[cols];
        for (int c = 0; c < cols; c++)
        {
            double theta = 2 * System.Math.PI * c / n;
            cosTheta[c] = System.Math.Cos(theta);
            sinTheta[c] = System.Math.Sin(theta);
        }

        // The seam closes exactly rather than to within a rounding error of 2*pi.
        cosTheta[n] = 1;
        sinTheta[n] = 0;

        for (int r = 0; r < rows; r++)
        {
            double height = (double)r / (rows - 1);
            for (int c = 0; c < cols; c++)
            {
                x[r, c] = profile[r] * cosTheta[c];
                y[r, c] = profile[r] * sinTheta[c];
                z[r, c] = height;
            }
        }

        return (x, y, z);
    }
}
