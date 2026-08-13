namespace JGraph.Maths.Volumes;

/// <summary>
/// Three scalar readings at every point of one grid, read as a direction and a speed: the field a
/// streamline follows and the one <c>curl</c> and <c>divergence</c> ask questions of.
/// </summary>
public sealed class VectorField
{
    /// <summary>Builds a vector field from three components sharing one grid.</summary>
    public VectorField(ScalarField u, ScalarField v, ScalarField w)
    {
        ArgumentNullException.ThrowIfNull(u);
        ArgumentNullException.ThrowIfNull(v);
        ArgumentNullException.ThrowIfNull(w);
        if (u.Rows != v.Rows || u.Columns != v.Columns || u.Pages != v.Pages
            || u.Rows != w.Rows || u.Columns != w.Columns || u.Pages != w.Pages)
        {
            throw new ArgumentException("The three components have to be over the same grid.");
        }

        U = u;
        V = v;
        W = w;
    }

    /// <summary>The component along x.</summary>
    public ScalarField U { get; }

    /// <summary>The component along y.</summary>
    public ScalarField V { get; }

    /// <summary>The component along z.</summary>
    public ScalarField W { get; }

    /// <summary>The direction at an arbitrary point; any component is NaN where the point is outside.</summary>
    public (double U, double V, double W) Sample(double x, double y, double z) =>
        (U.Sample(x, y, z), V.Sample(x, y, z), W.Sample(x, y, z));

    /// <summary>
    /// How much the field turns about each direction, and the angular speed that turning amounts to.
    /// </summary>
    /// <remarks>
    /// The three components of the curl are the usual mixed differences. The second answer is the
    /// angular velocity — half the magnitude of the curl — which is what MATLAB's second output of
    /// <c>curl</c> holds, and it is a rate of turn rather than the length of a vector, so it is
    /// reported separately rather than left for the caller to halve.
    /// </remarks>
    public (VectorField Curl, ScalarField AngularVelocity) Curl()
    {
        (ScalarField ux, ScalarField uy, ScalarField uz) = U.Gradient();
        (ScalarField vx, ScalarField vy, ScalarField vz) = V.Gradient();
        (ScalarField wx, ScalarField wy, ScalarField wz) = W.Gradient();
        _ = ux;
        _ = vy;
        _ = wz;

        int rows = U.Rows, columns = U.Columns, pages = U.Pages;
        var cx = new double[rows, columns, pages];
        var cy = new double[rows, columns, pages];
        var cz = new double[rows, columns, pages];
        var speed = new double[rows, columns, pages];

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                for (int p = 0; p < pages; p++)
                {
                    double ax = wy.Values[r, c, p] - vz.Values[r, c, p];
                    double ay = uz.Values[r, c, p] - wx.Values[r, c, p];
                    double az = vx.Values[r, c, p] - uy.Values[r, c, p];
                    cx[r, c, p] = ax;
                    cy[r, c, p] = ay;
                    cz[r, c, p] = az;
                    speed[r, c, p] = 0.5 * System.Math.Sqrt((ax * ax) + (ay * ay) + (az * az));
                }
            }
        }

        return (
            new VectorField(U.Like(cx), U.Like(cy), U.Like(cz)),
            U.Like(speed));
    }

    /// <summary>How much the field spreads out at each point: the sum of the three own-direction slopes.</summary>
    public ScalarField Divergence()
    {
        (ScalarField ux, _, _) = U.Gradient();
        (_, ScalarField vy, _) = V.Gradient();
        (_, _, ScalarField wz) = W.Gradient();

        int rows = U.Rows, columns = U.Columns, pages = U.Pages;
        var out3 = new double[rows, columns, pages];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                for (int p = 0; p < pages; p++)
                {
                    out3[r, c, p] = ux.Values[r, c, p] + vy.Values[r, c, p] + wz.Values[r, c, p];
                }
            }
        }

        return U.Like(out3);
    }
}
