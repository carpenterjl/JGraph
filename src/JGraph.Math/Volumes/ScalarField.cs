namespace JGraph.Maths.Volumes;

/// <summary>
/// A scalar reading at every point of a rectilinear box: three coordinate vectors and the readings
/// taken where they cross.
/// </summary>
/// <remarks>
/// <para>
/// The readings are indexed <c>[row, column, page]</c> — rows index y, columns index x, pages index
/// z. That is the order a matrix handed to <c>surf</c> or <c>contour</c> already uses, and the order
/// <see cref="Contours.MarchingTetrahedra"/> reads, so a field built the way a script builds one needs
/// no rearranging anywhere in this namespace.
/// </para>
/// <para>
/// The grid is rectilinear rather than regular: the spacing along each direction may vary, which is
/// what lets a field carry the coordinate vectors a script chose rather than forcing it onto a
/// uniform lattice. Everything here that needs a spacing therefore reads it locally rather than
/// assuming one.
/// </para>
/// </remarks>
public sealed class ScalarField
{
    /// <summary>Builds a field over the given grid.</summary>
    /// <param name="x">The x positions of the grid columns.</param>
    /// <param name="y">The y positions of the grid rows.</param>
    /// <param name="z">The z positions of the grid pages.</param>
    /// <param name="values">The readings, indexed <c>[row, column, page]</c>.</param>
    public ScalarField(double[] x, double[] y, double[] z, double[,,] values)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(z);
        ArgumentNullException.ThrowIfNull(values);

        if (values.GetLength(0) != y.Length
            || values.GetLength(1) != x.Length
            || values.GetLength(2) != z.Length)
        {
            throw new ArgumentException(
                "The readings have to be as many rows as y, as many columns as x, and as many pages "
                + "as z.",
                nameof(values));
        }

        X = x;
        Y = y;
        Z = z;
        Values = values;
    }

    /// <summary>The x positions of the grid columns.</summary>
    public double[] X { get; }

    /// <summary>The y positions of the grid rows.</summary>
    public double[] Y { get; }

    /// <summary>The z positions of the grid pages.</summary>
    public double[] Z { get; }

    /// <summary>The readings, indexed <c>[row, column, page]</c>.</summary>
    public double[,,] Values { get; }

    /// <summary>How many rows (y positions) the grid has.</summary>
    public int Rows => Y.Length;

    /// <summary>How many columns (x positions) the grid has.</summary>
    public int Columns => X.Length;

    /// <summary>How many pages (z positions) the grid has.</summary>
    public int Pages => Z.Length;

    /// <summary>A field over the same grid as this one, holding the given readings.</summary>
    public ScalarField Like(double[,,] values) => new(X, Y, Z, values);

    /// <summary>
    /// The reading at an arbitrary point, found by straight-line interpolation along each direction
    /// in turn. A point outside the box, or one whose surrounding readings are not all finite, has no
    /// value and answers NaN — which is how a streamline finds out it has left.
    /// </summary>
    public double Sample(double x, double y, double z)
    {
        (int c, double fx) = Locate(X, x);
        (int r, double fy) = Locate(Y, y);
        (int p, double fz) = Locate(Z, z);
        if (c < 0 || r < 0 || p < 0)
        {
            return double.NaN;
        }

        int c1 = System.Math.Min(c + 1, Columns - 1);
        int r1 = System.Math.Min(r + 1, Rows - 1);
        int p1 = System.Math.Min(p + 1, Pages - 1);

        double v000 = Values[r, c, p], v100 = Values[r, c1, p];
        double v010 = Values[r1, c, p], v110 = Values[r1, c1, p];
        double v001 = Values[r, c, p1], v101 = Values[r, c1, p1];
        double v011 = Values[r1, c, p1], v111 = Values[r1, c1, p1];

        double lower = Blend(Blend(v000, v100, fx), Blend(v010, v110, fx), fy);
        double upper = Blend(Blend(v001, v101, fx), Blend(v011, v111, fx), fy);
        return Blend(lower, upper, fz);
    }

    /// <summary>
    /// The rate of change of the readings along each direction, by central differences inside and
    /// one-sided differences at the faces — the three-dimensional reading of <c>gradient</c>.
    /// </summary>
    public (ScalarField X, ScalarField Y, ScalarField Z) Gradient()
    {
        var gx = new double[Rows, Columns, Pages];
        var gy = new double[Rows, Columns, Pages];
        var gz = new double[Rows, Columns, Pages];

        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < Columns; c++)
            {
                for (int p = 0; p < Pages; p++)
                {
                    gx[r, c, p] = Slope(X, c, Columns, (i) => Values[r, i, p]);
                    gy[r, c, p] = Slope(Y, r, Rows, (i) => Values[i, c, p]);
                    gz[r, c, p] = Slope(Z, p, Pages, (i) => Values[r, c, i]);
                }
            }
        }

        return (Like(gx), Like(gy), Like(gz));
    }

    /// <summary>
    /// The slope along one direction at one index: a central difference where there are readings on
    /// both sides, and the one-sided difference at an end.
    /// </summary>
    internal static double Slope(double[] positions, int index, int count, Func<int, double> read)
    {
        if (count < 2)
        {
            return 0;
        }

        if (index == 0)
        {
            return (read(1) - read(0)) / (positions[1] - positions[0]);
        }

        if (index == count - 1)
        {
            return (read(count - 1) - read(count - 2))
                / (positions[count - 1] - positions[count - 2]);
        }

        return (read(index + 1) - read(index - 1))
            / (positions[index + 1] - positions[index - 1]);
    }

    /// <summary>
    /// Where a coordinate sits in a sorted position vector: the index below it and how far past that
    /// index it lies, or <c>-1</c> when it is outside. A vector running downwards is read backwards,
    /// so a field whose y runs the other way still samples.
    /// </summary>
    internal static (int Index, double Fraction) Locate(double[] positions, double value)
    {
        int count = positions.Length;
        if (count == 0 || !double.IsFinite(value))
        {
            return (-1, 0);
        }

        if (count == 1)
        {
            return value == positions[0] ? (0, 0) : (-1, 0);
        }

        bool ascending = positions[count - 1] >= positions[0];
        double low = ascending ? positions[0] : positions[count - 1];
        double high = ascending ? positions[count - 1] : positions[0];
        if (value < low || value > high)
        {
            return (-1, 0);
        }

        for (int i = 0; i + 1 < count; i++)
        {
            double a = positions[i];
            double b = positions[i + 1];
            bool inside = ascending ? value >= a && value <= b : value <= a && value >= b;
            if (inside)
            {
                double span = b - a;
                return (i, span == 0 ? 0 : (value - a) / span);
            }
        }

        return (count - 2, 1);
    }

    private static double Blend(double a, double b, double t) => a + ((b - a) * t);
}
