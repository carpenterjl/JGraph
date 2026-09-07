namespace JGraph.Maths.Geometry;

/// <summary>
/// The reduced Clough–Tocher macro element: a surface over one triangle that matches a value and a
/// gradient at each of its three corners and joins its neighbours smoothly.
/// </summary>
/// <remarks>
/// <para>
/// The triangle is split at its centroid into three, each carrying its own cubic in Bernstein–Bézier
/// form. Nine of the ordinates are written down directly from the corner values and gradients; the
/// remaining seven — the value at the centroid, one ordinate along each of the three internal edges
/// and one in the middle of each piece — are what the smoothness conditions decide. Those conditions
/// are the standard ones: the pieces meet with a common gradient across each internal edge, and the
/// derivative across each outer edge varies linearly along it, which is what reduces the twelve
/// degrees of freedom of the full element to the nine a value and a gradient per corner supply.
/// </para>
/// <para>
/// The twelve conditions are consistent and pin the seven unknowns, so they are closed through their
/// normal equations rather than by writing out a hand-solved formula per unknown: the system is
/// seven by seven and is built once per triangle, where the surface is then read at every query
/// point that lands in it.
/// </para>
/// </remarks>
public static class CloughTocher
{
    private static readonly (int I, int J, int K)[] Ordinates = Build();
    private static readonly int[] Slot = Slots();

    private static (int, int, int)[] Build()
    {
        var list = new List<(int, int, int)>();
        for (int i = 3; i >= 0; i--)
        {
            for (int j = 3 - i; j >= 0; j--)
            {
                list.Add((i, j, 3 - i - j));
            }
        }

        return [.. list];
    }

    private static int[] Slots()
    {
        var slots = new int[64];
        Array.Fill(slots, -1);
        for (int n = 0; n < Ordinates.Length; n++)
        {
            (int i, int j, int k) = Ordinates[n];
            slots[(i * 16) + (j * 4) + k] = n;
        }

        return slots;
    }

    private static int At(int i, int j, int k) => Slot[(i * 16) + (j * 4) + k];

    /// <summary>
    /// The three pieces' Bézier ordinates, ten each, for the triangle
    /// <paramref name="p0"/>–<paramref name="p1"/>–<paramref name="p2"/>.
    /// </summary>
    public static double[][] Build(
        double[] p0, double[] p1, double[] p2,
        double f0, double f1, double f2,
        double[] g0, double[] g1, double[] g2)
    {
        double[][] corners = [p0, p1, p2];
        double[] values = [f0, f1, f2];
        double[][] slopes = [g0, g1, g2];
        double[] centre = [(p0[0] + p1[0] + p2[0]) / 3, (p0[1] + p1[1] + p2[1]) / 3];

        // Every ordinate is an affine function of the seven unknowns: column 0 is the constant and
        // columns 1..7 are (the centroid value, the three internal-edge ordinates, the three middles).
        var coefficients = new double[3][][];
        for (int k = 0; k < 3; k++)
        {
            var piece = new double[Ordinates.Length][];
            for (int n = 0; n < piece.Length; n++)
            {
                piece[n] = new double[8];
            }

            int a = k;
            int b = (k + 1) % 3;
            piece[At(3, 0, 0)][0] = values[a];
            piece[At(0, 3, 0)][0] = values[b];
            piece[At(0, 0, 3)][1] = 1;
            piece[At(2, 1, 0)][0] = values[a] + (Dot(slopes[a], corners[b], corners[a]) / 3);
            piece[At(1, 2, 0)][0] = values[b] + (Dot(slopes[b], corners[a], corners[b]) / 3);
            piece[At(2, 0, 1)][0] = values[a] + (Dot(slopes[a], centre, corners[a]) / 3);
            piece[At(0, 2, 1)][0] = values[b] + (Dot(slopes[b], centre, corners[b]) / 3);
            piece[At(1, 0, 2)][2 + a] = 1;
            piece[At(0, 1, 2)][2 + b] = 1;
            piece[At(1, 1, 1)][5 + k] = 1;
            coefficients[k] = piece;
        }

        var rows = new List<double[]>();
        for (int k = 0; k < 3; k++)
        {
            int kp = (k + 1) % 3;
            int far = (k + 2) % 3;

            // The two pieces either side of the internal edge from the centroid to corner k+1, read
            // in a common frame: (corner k+1, centroid, the corner each piece has of its own).
            double[] lambda = Barycentric(corners[kp], centre, corners[k], corners[far]);
            foreach ((int i, int j) in new[] { (2, 0), (1, 1), (0, 2) })
            {
                double[] row = new double[8];
                Add(row, Reordered(coefficients[kp], i, j, 1, false), 1);
                Add(row, Reordered(coefficients[k], i + 1, j, 0, true), -lambda[0]);
                Add(row, Reordered(coefficients[k], i, j + 1, 0, true), -lambda[1]);
                Add(row, Reordered(coefficients[k], i, j, 1, true), -lambda[2]);
                rows.Add(row);
            }

            // Across the outer edge the derivative must vary linearly, which is the condition that
            // takes the element from twelve degrees of freedom down to nine.
            double ex = corners[kp][0] - corners[k][0];
            double ey = corners[kp][1] - corners[k][1];
            double length = Math.Sqrt((ex * ex) + (ey * ey));
            double[] normal = [corners[k][0] - (ey / length), corners[k][1] + (ex / length)];
            double[] alpha = Barycentric(corners[k], corners[kp], centre, normal);
            alpha[0] -= 1;

            double[] linear = new double[8];
            Add(linear, Cross(coefficients[k], alpha, 1, 1), 1);
            Add(linear, Cross(coefficients[k], alpha, 2, 0), -0.5);
            Add(linear, Cross(coefficients[k], alpha, 0, 2), -0.5);
            rows.Add(linear);
        }

        // The twelve conditions are consistent, so their normal equations close them.
        var normal7 = new double[7][];
        var rhs = new double[7];
        for (int r = 0; r < 7; r++)
        {
            normal7[r] = new double[7];
        }

        foreach (double[] row in rows)
        {
            for (int r = 0; r < 7; r++)
            {
                for (int c = 0; c < 7; c++)
                {
                    normal7[r][c] += row[r + 1] * row[c + 1];
                }

                rhs[r] -= row[r + 1] * row[0];
            }
        }

        ScatteredSurface.SolveDense(normal7, rhs, 7);

        var patch = new double[3][];
        for (int k = 0; k < 3; k++)
        {
            var piece = new double[Ordinates.Length];
            for (int n = 0; n < piece.Length; n++)
            {
                double sum = coefficients[k][n][0];
                for (int u = 0; u < 7; u++)
                {
                    sum += coefficients[k][n][u + 1] * rhs[u];
                }

                piece[n] = sum;
            }

            patch[k] = piece;
        }

        return patch;
    }

    /// <summary>Reads the element at one point of the triangle it was built for.</summary>
    public static double Sample(double[][] patch, double[] p0, double[] p1, double[] p2, double x, double y)
    {
        double[][] corners = [p0, p1, p2];
        double[] centre = [(p0[0] + p1[0] + p2[0]) / 3, (p0[1] + p1[1] + p2[1]) / 3];
        double[] point = [x, y];

        int best = 0;
        double bestLeast = double.NegativeInfinity;
        double[] bestWeights = [1, 0, 0];
        for (int k = 0; k < 3; k++)
        {
            double[] weights = Barycentric(corners[k], corners[(k + 1) % 3], centre, point);
            double least = Math.Min(weights[0], Math.Min(weights[1], weights[2]));
            if (least > bestLeast)
            {
                bestLeast = least;
                best = k;
                bestWeights = weights;
            }
        }

        double u = bestWeights[0];
        double v = bestWeights[1];
        double w = bestWeights[2];
        double value = 0;
        for (int n = 0; n < Ordinates.Length; n++)
        {
            (int i, int j, int m) = Ordinates[n];
            value += patch[best][n] * Multinomial(i, j, m) * Power(u, i) * Power(v, j) * Power(w, m);
        }

        return value;
    }

    private static double Power(double x, int n) => n switch
    {
        0 => 1,
        1 => x,
        2 => x * x,
        _ => x * x * x,
    };

    private static double Multinomial(int i, int j, int k)
    {
        int[] factorial = [1, 1, 2, 6];
        return 6.0 / (factorial[i] * factorial[j] * factorial[k]);
    }

    private static double Dot(double[] gradient, double[] to, double[] from) =>
        (gradient[0] * (to[0] - from[0])) + (gradient[1] * (to[1] - from[1]));

    private static void Add(double[] into, double[] row, double scale)
    {
        for (int i = 0; i < into.Length; i++)
        {
            into[i] += scale * row[i];
        }
    }

    /// <summary>
    /// One ordinate of a piece, read in the frame the smoothness condition is written in. The two
    /// pieces meeting at an internal edge each carry their own corner ordering, so the index triple
    /// has to be permuted into each of them.
    /// </summary>
    private static double[] Reordered(double[][] piece, int i, int j, int k, bool ownFrame) =>
        ownFrame ? piece[At(k, i, j)] : piece[At(i, k, j)];

    /// <summary>The Bézier coefficients of the derivative along a barycentric direction, on the outer edge.</summary>
    private static double[] Cross(double[][] piece, double[] alpha, int i, int j)
    {
        var row = new double[8];
        Add(row, piece[At(i + 1, j, 0)], alpha[0]);
        Add(row, piece[At(i, j + 1, 0)], alpha[1]);
        Add(row, piece[At(i, j, 1)], alpha[2]);
        return row;
    }

    /// <summary>The barycentric coordinates of <paramref name="q"/> in the triangle a–b–c.</summary>
    private static double[] Barycentric(double[] a, double[] b, double[] c, double[] q)
    {
        double d = ((b[1] - c[1]) * (a[0] - c[0])) + ((c[0] - b[0]) * (a[1] - c[1]));
        if (d == 0)
        {
            return [double.NaN, double.NaN, double.NaN];
        }

        double u = (((b[1] - c[1]) * (q[0] - c[0])) + ((c[0] - b[0]) * (q[1] - c[1]))) / d;
        double v = (((c[1] - a[1]) * (q[0] - c[0])) + ((a[0] - c[0]) * (q[1] - c[1]))) / d;
        return [u, v, 1 - u - v];
    }
}
