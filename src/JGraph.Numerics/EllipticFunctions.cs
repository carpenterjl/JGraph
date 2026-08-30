namespace JGraph.Numerics;

/// <summary>
/// The complete elliptic integrals and the Jacobi elliptic functions, both by the
/// arithmetic-geometric mean.
/// </summary>
/// <remarks>
/// <para>
/// Both routines take the <em>whole array</em> rather than one parameter at a time, and that is not
/// an optimisation. The mean stops when the largest remaining correction anywhere in the array
/// falls under the tolerance, so how many times the recurrence runs is a property of the array and
/// not of the element — every element is carried for as many passes as the slowest one needs, and a
/// settled element's extra passes each add a correction that is below the tolerance but not zero.
/// That is why <c>ellipke</c> of one parameter need not answer, to the last bit, what
/// <c>ellipke</c> of a vector containing it answers in its place. Reproducing MATLAB means
/// iterating the array, so that is what these do.
/// </para>
/// <para>
/// The stopping test measures the largest correction with NaN <em>skipped</em>, which is what
/// MATLAB's <c>max</c> does: a NaN parameter therefore neither stops the recurrence early for the
/// parameters beside it nor keeps it running, and comes back NaN on its own.
/// </para>
/// </remarks>
public static class EllipticFunctions
{
    /// <summary>Raised when the mean stalls with the tolerance still unmet.</summary>
    public sealed class StalledException : Exception
    {
        /// <summary>Creates the exception with the message the caller should report.</summary>
        /// <param name="message">What to say.</param>
        public StalledException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// The complete elliptic integrals of the first and second kind, K(m) and E(m), for a whole
    /// array of parameters at once.
    /// </summary>
    /// <param name="m">The parameters, each in [0, 1]. Not modified.</param>
    /// <param name="tol">The convergence tolerance.</param>
    /// <returns>The two integrals, element for element with <paramref name="m"/>.</returns>
    public static (double[] K, double[] E) Complete(double[] m, double tol)
    {
        int count = m.Length;
        var a0 = new double[count];
        var b0 = new double[count];
        var c0 = new double[count];
        var s0 = new double[count];
        var c1 = new double[count];
        var w1 = new double[count];
        var a1 = new double[count];
        for (int i = 0; i < count; i++)
        {
            a0[i] = 1.0;
            b0[i] = Math.Sqrt(1.0 - m[i]);
            c0[i] = double.NaN;
            s0[i] = m[i];
        }

        double largest = double.PositiveInfinity;
        int pass = 0;
        while (largest > tol)
        {
            pass++;
            double weight = Math.Pow(2.0, pass);
            for (int i = 0; i < count; i++)
            {
                a1[i] = (a0[i] + b0[i]) / 2.0;
                double b1 = Math.Sqrt(a0[i] * b0[i]);
                c1[i] = (a0[i] - b0[i]) / 2.0;
                w1[i] = weight * (c1[i] * c1[i]);
                b0[i] = b1;
            }

            largest = LargestSkippingNaN(w1);
            if (SameNumbers(c0, c1))
            {
                throw new StalledException("ELLIPKE did not converge. Consider increasing TOL.");
            }

            for (int i = 0; i < count; i++)
            {
                s0[i] += w1[i];
                a0[i] = a1[i];
                c0[i] = c1[i];
            }
        }

        var k = new double[count];
        var e = new double[count];
        for (int i = 0; i < count; i++)
        {
            k[i] = Math.PI / (2.0 * a1[i]);
            e[i] = k[i] * (1.0 - (s0[i] / 2.0));

            // The mean cannot reach the singular parameter, so that endpoint is written in.
            if (m[i] == 1.0)
            {
                k[i] = double.PositiveInfinity;
                e[i] = 1.0;
            }
        }

        return (k, e);
    }

    /// <summary>
    /// The Jacobi elliptic functions sn, cn and dn by the descending Landen transformation: the
    /// mean is run forward until the arithmetic and geometric terms agree, then the amplitude is
    /// carried back down the same ladder.
    /// </summary>
    /// <param name="u">The arguments, one per parameter.</param>
    /// <param name="m">The parameters, each in [0, 1]. Same length as <paramref name="u"/>.</param>
    /// <param name="tol">The convergence tolerance.</param>
    /// <returns>The three functions, element for element.</returns>
    public static (double[] Sn, double[] Cn, double[] Dn) Jacobi(double[] u, double[] m, double tol)
    {
        int count = u.Length;
        var first = new double[count];
        var firstB = new double[count];
        for (int i = 0; i < count; i++)
        {
            first[i] = Math.Sqrt(m[i]);
            firstB[i] = Math.Sqrt(1.0 - m[i]);
        }

        var a = new List<double[]> { Filled(count, 1.0) };
        var b = new List<double[]> { firstB };
        var c = new List<double[]> { first };

        var settled = new int[count];
        int last = 0;
        while (AnyAbove(c[last], tol))
        {
            double[] previousA = a[last];
            double[] previousB = b[last];
            double[] previousC = c[last];
            var nextA = new double[count];
            var nextB = new double[count];
            var nextC = new double[count];
            for (int i = 0; i < count; i++)
            {
                nextA[i] = 0.5 * (previousA[i] + previousB[i]);
                nextB[i] = Math.Sqrt(previousA[i] * previousB[i]);
                nextC[i] = 0.5 * (previousA[i] - previousB[i]);
            }

            if (SameNumbers(nextC, previousC))
            {
                throw new StalledException("ELLIPJ did not converge. Consider increasing TOL.");
            }

            a.Add(nextA);
            b.Add(nextB);
            c.Add(nextC);
            last++;

            // How many rungs an element climbed before settling is the height it has to be carried
            // back down from, and it is also the power of two its amplitude starts at. One already
            // under the tolerance never enters this set and is not carried at all.
            //
            // Recording one rung too few very nearly works, because the step it then skips is a
            // halving and the amplitude it then starts from is half as large — so the two errors
            // cancel exactly while the last rung's c is negligible. At a tolerance loose enough for
            // that c to matter they stop cancelling, and ellipj(2, 0.5, 1e-3) drifts in the seventh
            // digit. Measured against R2024a, which is the only way that showed.
            for (int i = 0; i < count; i++)
            {
                if (Math.Abs(nextC[i]) <= tol && Math.Abs(previousC[i]) > tol)
                {
                    settled[i] = last;
                }
            }
        }

        var phi = new double[count];
        for (int i = 0; i < count; i++)
        {
            phi[i] = Math.Pow(2.0, settled[i]) * a[last][i] * u[i];
        }

        for (int level = last; level > 0; level--)
        {
            var below = new double[count];
            for (int i = 0; i < count; i++)
            {
                below[i] = settled[i] >= level
                    ? 0.5 * (Math.Asin(c[level][i] * Math.Sin(Remainder(phi[i], 2.0 * Math.PI)) / a[level][i])
                        + phi[i])
                    : phi[i];
            }

            phi = below;
        }

        var sn = new double[count];
        var cn = new double[count];
        var dn = new double[count];
        for (int i = 0; i < count; i++)
        {
            double wrapped = Remainder(phi[i], 2.0 * Math.PI);
            sn[i] = Math.Sin(wrapped);
            cn[i] = Math.Cos(wrapped);
            dn[i] = Math.Sqrt(1.0 - (m[i] * sn[i] * sn[i]));

            // The two ends of the parameter range are the degenerate cases the ladder cannot reach:
            // at m = 1 the functions are hyperbolic, at m = 0 they are circular.
            if (m[i] == 1.0)
            {
                sn[i] = Math.Tanh(u[i]);
                cn[i] = 1.0 / Math.Cosh(u[i]);
                dn[i] = cn[i];
            }
            else if (m[i] == 0.0)
            {
                dn[i] = 1.0;
            }
        }

        return (sn, cn, dn);
    }

    /// <summary>MATLAB's <c>rem</c>: the remainder that keeps the dividend's sign.</summary>
    private static double Remainder(double x, double y) => x - (Math.Truncate(x / y) * y);

    private static double[] Filled(int count, double value)
    {
        var made = new double[count];
        System.Array.Fill(made, value);
        return made;
    }

    /// <summary>MATLAB's <c>max</c> over a vector: NaN is passed over, not propagated.</summary>
    private static double LargestSkippingNaN(double[] values)
    {
        double largest = double.NaN;
        bool any = false;
        foreach (double value in values)
        {
            if (!double.IsNaN(value) && (!any || value > largest))
            {
                largest = value;
                any = true;
            }
        }

        return largest;
    }

    private static bool AnyAbove(double[] values, double tol)
    {
        foreach (double value in values)
        {
            if (Math.Abs(value) > tol)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>MATLAB's <c>isequal</c> over two vectors: NaN equals nothing, itself included.</summary>
    private static bool SameNumbers(double[] left, double[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }
}
