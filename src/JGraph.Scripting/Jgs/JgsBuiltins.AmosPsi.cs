namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>psi(k, x)</c> in MATLAB code by D. E. Amos's algorithm, ACM TOMS 610 (SLATEC's DPSIFN), which
/// R2025b's psi follows (open item 349, ADR 0386).
/// </summary>
/// <remarks>
/// <para>
/// The routine's text could not be fetched in this lane, so this is a reconstruction of its structure
/// (the machine constants of IEEE double, the term count and XMIN from the precision, the series
/// over (x+k)^-(n+1) for a small x and an order past 0, otherwise the asymptotic expansion over
/// Bernoulli numbers at x + XINC and the backward recurrence down to x), checked bit for bit
/// against R2025b: 2,207 of 2,411 values of psi(k, x) for k in 0..6, 10, 20, 40 and 100 over 343 x
/// from 1e-8 to 1e300 agree to the bit (the algorithm this replaced agreed on 516); the rest differ
/// by an ulp or two, about as often toward the true value as away from it.
/// </para>
/// <para>
/// The routine's overflow and underflow exits (an x so large that x^-(k+1) leaves the doubles, or
/// x + XINC's logarithm past ELIM) and an order past 100 answer null, and the caller keeps its own road.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>B(1..22) of DPSIFN's DATA statement: 1, -1/2, then B₂, B₄, …, B₄₀.</summary>
    private static readonly double[] AmosBernoulli =
    [
        double.NaN, // 1-based
        1.0, -0.5, 1.0 / 6, -1.0 / 30, 1.0 / 42, -1.0 / 30, 5.0 / 66, -691.0 / 2730, 7.0 / 6, -3617.0 / 510,
        43867.0 / 798, -174611.0 / 330, 854513.0 / 138, -236364091.0 / 2730, 8553103.0 / 6,
        -23749461029.0 / 870, 8615841276005.0 / 14322, -7709321041217.0 / 510, 2577687858367.0 / 6,
        -1.3711655205088332772e+13, 4.8833231897359316667e+14, -1.9296579341940068149e+16,
    ];

    /// <summary>ψ⁽ᵏ⁾(x) for x &gt; 0 by DPSIFN (KODE = 1, M = 1), or null where the routine would not answer.</summary>
    internal static double? AmosPolygamma(int k, double x)
    {
        if (!(x > 0) || double.IsInfinity(x) || k < 0 || k > 100)
        {
            return null;
        }

        double? scaled = AmosDpsifn(x, k);
        if (scaled is not double ans)
        {
            return null;
        }

        if (k == 0)
        {
            return -ans;
        }

        double factorial = 1;
        for (int i = 2; i <= k; i++)
        {
            factorial *= i;
        }

        return ((k % 2) == 0 ? -1.0 : 1.0) * factorial * ans;
    }

    /// <summary>DPSIFN's ANS(1) = (-1)^(n+1)/n! · ψ⁽ⁿ⁾(x) for KODE = 1 and M = 1.</summary>
    private static double? AmosDpsifn(double x, int n)
    {
        const double R1M5 = 0.30102999566398119521; // log10(2)
        const double WDTOL = 1.1102230246251565e-16; // max(D1MACH(4)/2, 0.5e-18)
        const int Digits = 53; // I1MACH(14)
        double elim = 2.302 * ((1021 * R1M5) - 3.0); // NX = min(-I1MACH(15), I1MACH(16)) = 1021

        double xln = Math.Log(x);
        double fn = n;
        double t = (fn + 1) * xln;
        if (Math.Abs(t) > elim)
        {
            return null;
        }

        if (x < WDTOL)
        {
            return Math.Pow(x, -n - 1);
        }

        double rln = Math.Min(R1M5 * Digits, 18.06);
        double fln = Math.Max(rln, 3.0) - 3.0;
        double yint = 3.50 + (0.40 * fln);
        double slope = 0.21 + (fln * ((0.0006038 * fln) + 0.008677));
        double xm = yint + (slope * fn);
        int mx = (int)xm + 1;
        double xmin = mx;
        if (n != 0)
        {
            xm = (-2.302 * rln) - Math.Min(0.0, xln);
            double arg = Math.Min(0.0, xm / n);
            double eps = Math.Exp(arg);
            xm = 1.0 - eps;
            if (Math.Abs(arg) < 1.0e-3)
            {
                xm = -arg;
            }

            fln = x * xm / eps;
            xm = xmin - x;
            if (xm > 7.0 && fln < 15.0)
            {
                // The series Σ (x+k)^-(n+1), k = 0..NN.
                int terms = (int)fln + 1;
                int np = n + 1;
                double s0 = Math.Exp(-(np * xln));
                double den = x;
                for (int i = 1; i <= terms; i++)
                {
                    den += 1.0;
                    s0 += Math.Pow(den, -np);
                }

                return s0;
            }
        }

        double xdmy = x;
        double xdmln = xln;
        double xinc = 0.0;
        if (x < xmin)
        {
            int whole = (int)x;
            xinc = xmin - whole;
            xdmy = x + xinc;
            xdmln = Math.Log(xdmy);
        }

        // The asymptotic expansion at x + XINC.
        t = fn * xdmln;
        double t1 = xdmln + xdmln;
        double t2 = t + xdmln;
        if (Math.Max(Math.Abs(t), Math.Max(Math.Abs(t1), Math.Abs(t2))) > elim)
        {
            return null;
        }

        double tss = Math.Exp(-t);
        double tt = 0.5 / xdmy;
        t1 = tt;
        double tst = WDTOL * tt;
        if (n != 0)
        {
            t1 = tt + (1.0 / fn);
        }

        double rxsq = 1.0 / (xdmy * xdmy);
        double ta = 0.5 * rxsq;
        t = (fn + 1) * ta;
        double s = t * AmosBernoulli[3];
        if (Math.Abs(s) >= tst)
        {
            double tk = 2.0;
            for (int kk = 4; kk <= 22; kk++)
            {
                t = t * ((tk + fn + 1) / (tk + 1.0)) * ((tk + fn) / (tk + 2.0)) * rxsq;
                double trm = t * AmosBernoulli[kk];
                if (Math.Abs(trm) < tst)
                {
                    break;
                }

                s += trm;
                tk += 2.0;
            }
        }

        s = (s + t1) * tss;
        if (xinc != 0.0)
        {
            // The backward recurrence from x + XINC down to x.
            int steps = (int)xinc;
            if (n == 0)
            {
                for (int i = 1; i <= steps; i++)
                {
                    s += 1.0 / (x + (steps - i));
                }
            }
            else
            {
                int np = n + 1;
                double back = xinc - 1.0;
                double fx = x + back;
                for (int i = 1; i <= steps; i++)
                {
                    s += Math.Pow(fx, -np);
                    back -= 1.0;
                    fx = x + back;
                }
            }
        }

        return n == 0 ? s - xdmln : s;
    }
}
