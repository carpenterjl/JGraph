namespace JGraph.Numerics;

/// <summary>
/// The free interpolants of the stiff family — MATLAB's <c>ntrp15s</c>, <c>ntrp23s</c>,
/// <c>ntrp23t</c>, <c>ntrp23tb</c> and <c>ntrp15i</c>.
/// </summary>
/// <remarks>
/// <para>
/// "Free" is the point: none of these costs a derivative evaluation. A backward-difference formula
/// already carries the polynomial that produced the step; a Rosenbrock step already has its two
/// stage vectors; the trapezoidal rule has the scaled slopes at both ends; TR-BDF2 has a third
/// point inside the step. Reading the solution between mesh points is therefore arithmetic on what
/// the step has already paid for, which is why <c>Refine</c> and <c>deval</c> cost nothing here and
/// why a terminal event can be found to the accuracy of the bracketing search rather than to the
/// accuracy of the step.
/// </para>
/// <para>
/// Each answers the state, and writes the slope into <c>slope</c> when one is asked for. The
/// non-negativity constraint is applied last, to the value and to the slope together, so that a
/// component the constraint clipped reads as flat rather than as still falling.
/// </para>
/// </remarks>
public static class OdeStiffInterpolants
{
    /// <summary><c>ntrp15s</c>: the backward differences of the step, summed at <paramref name="at"/>.</summary>
    public static double[] Ntrp15s(double at, double tNew, double[] yNew, double h, double[][] dif, int k,
        double[]? slope, int[]? nonNegative)
    {
        int n = yNew.Length;
        double s = (at - tNew) / h;
        var value = new double[n];
        Array.Copy(yNew, value, n);

        if (k == 1)
        {
            for (int i = 0; i < n; i++)
            {
                value[i] += dif[0][i] * s;
            }

            if (slope is not null)
            {
                for (int i = 0; i < n; i++)
                {
                    slope[i] = dif[0][i] / h;
                }
            }
        }
        else
        {
            double product = 1;
            for (int j = 1; j <= k; j++)
            {
                product *= (s + j - 1) / j;
                for (int i = 0; i < n; i++)
                {
                    value[i] += dif[j - 1][i] * product;
                }
            }

            if (slope is not null)
            {
                // The derivative of the same product, carried alongside it: one running term short
                // of the product, and one running derivative built from it.
                Array.Copy(dif[0], slope, n);
                double running = 1;
                double runningDerivative = 1;
                for (int j = 2; j <= k; j++)
                {
                    running *= (j - 2 + s) / j;
                    runningDerivative = (runningDerivative * ((j - 1 + s) / j)) + running;
                    for (int i = 0; i < n; i++)
                    {
                        slope[i] += dif[j - 1][i] * runningDerivative;
                    }
                }

                for (int i = 0; i < n; i++)
                {
                    slope[i] /= h;
                }
            }
        }

        Clip(value, slope, nonNegative);
        return value;
    }

    /// <summary><c>ntrp23s</c>: the two Rosenbrock stages over a quadratic in the step.</summary>
    public static double[] Ntrp23s(double at, double t, double[] y, double h, double[] k1, double[] k2,
        double[]? slope)
    {
        int n = y.Length;
        double s = (at - t) / h;
        double d = 1 / (2 + Math.Sqrt(2));
        double e = h / (1 - (2 * d));
        double p1 = s * (1 - s) * e;
        double p2 = s * (s - (2 * d)) * e;
        var value = new double[n];
        for (int i = 0; i < n; i++)
        {
            value[i] = y[i] + (k1[i] * p1) + (k2[i] * p2);
        }

        if (slope is not null)
        {
            double dp1 = e / h * (1 - (2 * s));
            double dp2 = e / h * 2 * (s - d);
            for (int i = 0; i < n; i++)
            {
                slope[i] = (k1[i] * dp1) + (k2[i] * dp2);
            }
        }

        return value;
    }

    /// <summary><c>ntrp23t</c>: the cubic through both ends of the step and both scaled slopes.</summary>
    public static double[] Ntrp23t(double at, double t, double[] y, double[] yNew, double h, double[] z,
        double[] zNew, double[]? slope, int[]? nonNegative)
    {
        int n = y.Length;
        double s = (at - t) / h;
        double s2 = s * s;
        double s3 = s * s2;
        var value = new double[n];
        for (int i = 0; i < n; i++)
        {
            double v1 = yNew[i] - y[i] - z[i];
            double v2 = zNew[i] - z[i];
            value[i] = y[i] + (z[i] * s) + (((3 * v1) - v2) * s2) + ((v2 - (2 * v1)) * s3);
            if (slope is not null)
            {
                slope[i] = (z[i] / h) + (2 / h * ((3 * v1) - v2) * s) + (3 / h * (v2 - (2 * v1)) * s2);
            }
        }

        Clip(value, slope, nonNegative);
        return value;
    }

    /// <summary><c>ntrp23tb</c>: the quadratic through the step's ends and the trapezoidal point inside it.</summary>
    public static double[] Ntrp23tb(double at, double t, double[] y, double tNew, double[] yNew, double t2,
        double[] y2, double[]? slope, int[]? nonNegative)
    {
        int n = y.Length;
        double a1 = (at - tNew) * (at - t2) / ((t - tNew) * (t - t2));
        double a2 = (at - t) * (at - tNew) / ((t2 - t) * (t2 - tNew));
        double a3 = (at - t) * (at - t2) / ((tNew - t) * (tNew - t2));
        var value = new double[n];
        for (int i = 0; i < n; i++)
        {
            value[i] = (y[i] * a1) + (y2[i] * a2) + (yNew[i] * a3);
        }

        if (slope is not null)
        {
            double b1 = ((at - tNew) + (at - t2)) / ((t - tNew) * (t - t2));
            double b2 = ((at - t) + (at - tNew)) / ((t2 - t) * (t2 - tNew));
            double b3 = ((at - t) + (at - t2)) / ((tNew - t) * (tNew - t2));
            for (int i = 0; i < n; i++)
            {
                slope[i] = (y[i] * b1) + (y2[i] * b2) + (yNew[i] * b3);
            }
        }

        Clip(value, slope, nonNegative);
        return value;
    }

    /// <summary>
    /// <c>ntrp15i</c>: the Lagrange polynomial through the step's end and the mesh points behind it
    /// — the same polynomial the formula was written on.
    /// </summary>
    public static double[] Ntrp15i(double at, double tNew, double[] yNew, double[] mesh,
        double[][] meshSolution, double[]? slope)
    {
        int n = yNew.Length;
        int m = mesh.Length + 1;
        var nodes = new double[m];
        var states = new double[m][];
        nodes[0] = tNew;
        states[0] = yNew;
        for (int i = 1; i < m; i++)
        {
            nodes[i] = mesh[i - 1];
            states[i] = meshSolution[i - 1];
        }

        var weight = new double[m];
        var weightSlope = new double[m];
        for (int j = 0; j < m; j++)
        {
            weight[j] = 1;
            for (int i = 0; i < m; i++)
            {
                if (i == j)
                {
                    continue;
                }

                double gap = nodes[j] - nodes[i];
                double scaled = (at - nodes[i]) / gap;
                weightSlope[j] = (weightSlope[j] * scaled) + (weight[j] / gap);
                weight[j] *= scaled;
            }
        }

        var value = new double[n];
        if (slope is not null)
        {
            Array.Clear(slope);
        }

        for (int j = 0; j < m; j++)
        {
            for (int i = 0; i < n; i++)
            {
                value[i] += states[j][i] * weight[j];
                if (slope is not null)
                {
                    slope[i] += states[j][i] * weightSlope[j];
                }
            }
        }

        return value;
    }

    private static void Clip(double[] value, double[]? slope, int[]? nonNegative)
    {
        if (nonNegative is null)
        {
            return;
        }

        foreach (int index in nonNegative)
        {
            if (value[index] < 0)
            {
                value[index] = 0;
                if (slope is not null)
                {
                    slope[index] = 0;
                }
            }
        }
    }
}
