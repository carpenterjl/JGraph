using System.Numerics;

namespace JGraph.Signal;

/// <summary>
/// Equiripple linear-phase FIR design by the Parks–McClellan exchange, in MATLAB's <c>firpm</c>
/// conventions (M134).
/// </summary>
/// <remarks>
/// <para>
/// The algorithm is Remez's: guess where the error's extremes are, solve for the response that
/// equalises the error at those points, find where the error actually peaks, move the guesses
/// there, repeat. What makes an implementation agree with another to the last figure is none of
/// that — it is the details underneath. The candidate frequencies live on a fixed grid whose
/// spacing depends on the order and the band edges. The interpolation at each step is barycentric,
/// with the weights computed by a strided product that skips its own term. The exchange walks the
/// grid in one direction, then the other, with a rule for what to do when a peak is found beyond
/// the last extreme that has four separate cases and a flag that survives between them.
/// </para>
/// <para>
/// This is a transcription of MATLAB's, which is itself a transcription of the 1973 Fortran. The
/// previous implementation here was a clean reimplementation from the same textbook, and it
/// answered coefficients about <c>1e-5</c> from MATLAB's and warned that it had not converged at
/// order four hundred — the two facts are one fact, because the exchange's fixed point is a
/// property of the grid rather than of the mathematics. That divergence, recorded against M124, is
/// closed by this file.
/// </para>
/// </remarks>
public static class FirDesign
{
    /// <summary>The default grid density: sixteen candidate points per cosine term.</summary>
    public const int DefaultGridDensity = 16;

    /// <summary>Everything an equiripple design can be asked for.</summary>
    /// <param name="H">The impulse response.</param>
    /// <param name="Error">The ripple height, which is the same in every band after weighting.</param>
    /// <param name="GridFrequencies">The dense grid the exchange ran on.</param>
    /// <param name="Desired">The desired response at each grid point.</param>
    /// <param name="Weights">The weight at each grid point.</param>
    /// <param name="Response">The design's own response at each grid point.</param>
    /// <param name="ErrorCurve">The weighted error at each grid point.</param>
    /// <param name="ExtremalIndices">Which grid points the exchange settled on.</param>
    /// <param name="ExtremalFrequencies">Those points' frequencies.</param>
    /// <param name="Converged">False when the exchange stopped moving backwards or ran out of steps.</param>
    public readonly record struct RemezResult(
        double[] H,
        double Error,
        double[] GridFrequencies,
        double[] Desired,
        double[] Weights,
        Complex[] Response,
        double[] ErrorCurve,
        int[] ExtremalIndices,
        double[] ExtremalFrequencies,
        bool Converged);

    /// <summary>
    /// Designs an order-<paramref name="order"/> equiripple filter against a piecewise-linear
    /// response given by band edges and amplitudes.
    /// </summary>
    public static RemezResult Remez(
        int order,
        ReadOnlySpan<double> bandEdges,
        ReadOnlySpan<double> desired,
        ReadOnlySpan<double> weights,
        LinearPhaseType type,
        int gridDensity)
    {
        if (order < 3)
        {
            throw new ArgumentOutOfRangeException(nameof(order), "firpm needs an order of at least 3.");
        }

        if (bandEdges.Length % 2 != 0)
        {
            throw new ArgumentException("firpm needs an even number of band edges.", nameof(bandEdges));
        }

        if (bandEdges.Length != desired.Length)
        {
            throw new ArgumentException("firpm needs one amplitude per band edge.", nameof(desired));
        }

        foreach (double edge in bandEdges)
        {
            if (edge < 0 || edge > 1)
            {
                throw new ArgumentException("firpm band edges must lie between 0 and 1.", nameof(bandEdges));
            }
        }

        for (int i = 1; i < bandEdges.Length; i++)
        {
            if (bandEdges[i] < bandEdges[i - 1])
            {
                throw new ArgumentException("firpm band edges must be non-decreasing.", nameof(bandEdges));
            }
        }

        bool everyBandEmpty = true;
        for (int i = 0; i < bandEdges.Length; i += 2)
        {
            if (bandEdges[i + 1] - bandEdges[i] != 0)
            {
                everyBandEmpty = false;
                break;
            }
        }

        if (everyBandEmpty)
        {
            throw new ArgumentException("firpm needs at least one band of non-zero width.", nameof(bandEdges));
        }

        int bands = bandEdges.Length / 2;
        var wtx = new double[bands];
        if (weights.Length == 0)
        {
            Array.Fill(wtx, 1);
        }
        else if (weights.Length != bands)
        {
            throw new ArgumentException("firpm needs one weight per band.", nameof(weights));
        }
        else
        {
            weights.CopyTo(wtx);
            foreach (double w in wtx)
            {
                if (w <= 0)
                {
                    throw new ArgumentException("firpm's weights must all be positive.", nameof(weights));
                }
            }
        }

        bool differentiator = type == LinearPhaseType.Differentiator;
        bool hilbert = type == LinearPhaseType.Hilbert;
        order = FirWindowDesign.CheckOrder(order, bandEdges[^1], desired, differentiator || hilbert, out _);

        int nfilt = order + 1;
        bool nodd = nfilt % 2 == 1;
        bool neg = differentiator || hilbert;

        int lgrid = gridDensity <= 0 ? DefaultGridDensity : gridDensity;
        double[] grid = BuildGrid(nfilt, lgrid, bandEdges, neg, nodd);
        while (grid.Length <= nfilt)
        {
            lgrid *= 4;
            grid = BuildGrid(nfilt, lgrid, bandEdges, neg, nodd);
        }

        (double[] des, double[] wt) = Response(bandEdges, desired, grid, wtx, differentiator);

        var halfEdges = new double[bandEdges.Length];
        for (int i = 0; i < halfEdges.Length; i++)
        {
            halfEdges[i] = bandEdges[i] / 2;
        }

        var halfGrid = new double[grid.Length];
        for (int i = 0; i < grid.Length; i++)
        {
            halfGrid[i] = grid[i] / 2;
        }

        (double[] halfResponse, double deviation, int[] extremes, bool converged) =
            Exchange(nfilt, halfEdges, halfGrid, des, wt, neg);

        // The half response is mirrored, with the sign that belongs to the symmetry, and then read
        // back to front — the exchange builds the filter from its centre outwards.
        int mirrored = halfResponse.Length - (nfilt % 2);
        var h = new double[halfResponse.Length + mirrored];
        Array.Copy(halfResponse, h, halfResponse.Length);
        double sign = neg ? -1 : 1;
        for (int i = 0; i < mirrored; i++)
        {
            h[halfResponse.Length + i] = sign * halfResponse[mirrored - 1 - i];
        }

        Array.Reverse(h);
        if (neg && !hilbert)
        {
            for (int i = 0; i < h.Length; i++)
            {
                h[i] = -h[i];
            }
        }

        var response = new Complex[grid.Length];
        var errorCurve = new double[grid.Length];
        for (int i = 0; i < grid.Length; i++)
        {
            Complex z = Complex.Exp(new Complex(0, -grid[i] * System.Math.PI));
            Complex value = Complex.Zero;
            for (int k = h.Length - 1; k >= 0; k--)
            {
                value = (value * z) + h[k];
            }

            response[i] = value;
            Complex linear = neg
                ? Complex.Exp(new Complex(0, (grid[i] * System.Math.PI * order / 2) - (System.Math.PI / 2)))
                : Complex.Exp(new Complex(0, grid[i] * System.Math.PI * order / 2));
            errorCurve[i] = hilbert
                ? (des[i] + (response[i] * linear)).Real
                : (des[i] - (response[i] * linear)).Real;
        }

        var extremalFrequencies = new double[extremes.Length];
        for (int i = 0; i < extremes.Length; i++)
        {
            extremalFrequencies[i] = grid[extremes[i] - 1];
        }

        return new RemezResult(
            h, System.Math.Abs(deviation), grid, des, wt, response, errorCurve,
            extremes, extremalFrequencies, converged);
    }

    /// <summary>
    /// The dense grid of candidate frequencies, laid out band by band with the spacing the order
    /// asks for and each band's upper edge forced onto it exactly.
    /// </summary>
    private static double[] BuildGrid(int nfilt, int lgrid, ReadOnlySpan<double> ff, bool neg, bool nodd)
    {
        int nfcns = nfilt / 2;
        if (nodd && !neg)
        {
            nfcns++;
        }

        var grid = new List<double> { ff[0] };
        double delf = 1.0 / (lgrid * nfcns);

        if (neg && grid[0] < delf)
        {
            // An antisymmetric response is zero at the origin, so the grid must not start there.
            grid[0] = ff[0] > System.Math.Sqrt(2.220446049250313e-16) ? ff[0]
                : delf < ff[1] ? delf
                : 0.5 * (ff[1] - ff[0]);
        }

        int j = 1;
        int l = 1;
        while (l + 1 <= ff.Length)
        {
            double fup = ff[l];
            double start = grid[j - 1] + delf;
            List<double> added = Colon(start, delf, fup + delf);
            if (added.Count < 11)
            {
                // Fewer than eleven points in a band is too coarse for the exchange to find its
                // extremes, so the band is re-cut into ten steps of its own.
                double delf1 = (fup + delf - start) / 10;
                added = Colon(grid[j - 1] + delf1, delf1, fup + delf1);
            }

            grid.AddRange(added);
            int jend = grid.Count;
            if (jend > 1)
            {
                grid[jend - 2] = fup;
                j = jend;
            }
            else
            {
                j = jend + 1;
            }

            l += 2;
            if (l + 1 <= ff.Length)
            {
                if (j - 1 < grid.Count)
                {
                    grid[j - 1] = ff[l - 1];
                }
                else
                {
                    grid.Add(ff[l - 1]);
                }
            }
        }

        int ngrid = j - 1;
        if (neg == nodd && grid[ngrid - 1] > 1 - delf)
        {
            if (ff[^2] < 1 - delf)
            {
                ngrid--;
            }
            else
            {
                grid[ngrid - 1] = ff[^2];
            }
        }

        return [.. grid.GetRange(0, ngrid)];
    }

    /// <summary>
    /// MATLAB's colon over a non-integer step: the count is worked out once and each point is the
    /// start plus a multiple of the step, rather than a running sum — which is a different grid by
    /// one point at the end, and one point is the difference between two exchanges.
    /// </summary>
    private static List<double> Colon(double start, double step, double stop)
    {
        var values = new List<double>();
        if (step <= 0)
        {
            return values;
        }

        double span = (stop - start) / step;
        int count = (int)System.Math.Floor(span + (3 * 2.220446049250313e-16 * System.Math.Abs(span)));
        for (int k = 0; k <= count; k++)
        {
            values.Add(start + (k * step));
        }

        return values;
    }

    /// <summary>
    /// The desired response and weight at every grid point: a straight line across each band, and
    /// a band of zero width reading as the average of its two endpoints.
    /// </summary>
    private static (double[] Desired, double[] Weight) Response(
        ReadOnlySpan<double> f, ReadOnlySpan<double> a, double[] grid, double[] w, bool differentiator)
    {
        var des = new double[grid.Length];
        var wt = new double[grid.Length];
        int bands = a.Length / 2;

        for (int band = 0; band < bands; band++)
        {
            int l = 2 * band;
            for (int i = 0; i < grid.Length; i++)
            {
                if (grid[i] < f[l] || grid[i] > f[l + 1])
                {
                    continue;
                }

                des[i] = f[l + 1] != f[l]
                    ? (((a[l + 1] - a[l]) / (f[l + 1] - f[l])) * (grid[i] - f[l])) + a[l]
                    : (a[l] + a[l + 1]) / 2;

                // A differentiator's error is measured relative to the frequency, so its weight
                // falls across the band rather than being flat.
                double scale = differentiator && a[l + 1] >= 0.0001 ? (grid[i] / 2) - 1 : 0;
                wt[i] = w[band] / (1 + scale);
            }
        }

        return (des, wt);
    }

    /// <summary>
    /// The exchange itself, index for index as MATLAB's <c>remezm</c> has it. The arrays are
    /// one-based here for the same reason: every branch below is a transcription, and re-basing the
    /// indices would be a chance to lose one.
    /// </summary>
    private static (double[] H, double Deviation, int[] Extremes, bool Converged) Exchange(
        int nfilt, double[] edge, double[] gridIn, double[] desIn, double[] wtIn, bool negFlag)
    {
        int neg = negFlag ? 1 : 0;
        int nbands = edge.Length / 2;
        int jb = 2 * nbands;
        int nodd = nfilt % 2;
        int nfcns = nfilt / 2;
        if (nodd == 1 && neg == 0)
        {
            nfcns++;
        }

        int ngrid = gridIn.Length;
        var grid = OneBased(gridIn);
        var des = OneBased(desIn);
        var wt = OneBased(wtIn);

        // The three symmetries fold their own factor into the desired response and the weight, so
        // that the exchange itself only ever sees a plain cosine series.
        if (neg <= 0)
        {
            if (nodd != 1)
            {
                for (int i = 1; i <= ngrid; i++)
                {
                    des[i] /= System.Math.Cos(System.Math.PI * grid[i]);
                    wt[i] *= System.Math.Cos(System.Math.PI * grid[i]);
                }
            }
        }
        else if (nodd != 1)
        {
            for (int i = 1; i <= ngrid; i++)
            {
                des[i] /= System.Math.Sin(System.Math.PI * grid[i]);
                wt[i] *= System.Math.Sin(System.Math.PI * grid[i]);
            }
        }
        else
        {
            for (int i = 1; i <= ngrid; i++)
            {
                des[i] /= System.Math.Sin(2 * System.Math.PI * grid[i]);
                wt[i] *= System.Math.Sin(2 * System.Math.PI * grid[i]);
            }
        }

        double temp = (double)(ngrid - 1) / nfcns;
        int nz = nfcns + 1;
        int nzz = nz + 1;
        var iext = new int[nzz + 1];
        for (int j = 1; j <= nfcns; j++)
        {
            iext[j] = (int)(temp * (j - 1)) + 1;
        }

        iext[nz] = ngrid;

        double comp = 0;
        const int itrmax = 250;
        double devl = -1;
        int niter = 0;
        int jchnge = 1;
        int jet = ((nfcns - 1) / 15) + 1;
        var ad = new double[nz + 1];
        var x = new double[nzz + 1];
        var y = new double[nz + 1];
        double dev = 0;
        double y1 = 0;
        int luck = 0;
        bool converged = true;

        while (jchnge > 0)
        {
            iext[nzz] = ngrid + 1;
            niter++;
            if (niter > itrmax)
            {
                converged = false;
                break;
            }

            for (int i = 1; i <= nz; i++)
            {
                x[i] = System.Math.Cos(2 * System.Math.PI * grid[iext[i]]);
            }

            for (int nn = 1; nn <= nz; nn++)
            {
                ad[nn] = BarycentricWeight(nn, nz, jet, x);
            }

            double dnum = 0;
            double dden = 0;
            for (int i = 1; i <= nz; i++)
            {
                double alternate = i % 2 == 0 ? -1 : 1;
                dnum += ad[i] * des[iext[i]];
                dden += alternate * ad[i] / wt[iext[i]];
            }

            dev = dnum / dden;
            int nu = dev > 0 ? -1 : 1;
            dev = -nu * dev;
            for (int i = 1; i <= nz; i++)
            {
                double alternate = i % 2 == 0 ? -1 : 1;
                y[i] = des[iext[i]] + (nu * dev * alternate / wt[iext[i]]);
            }

            if (dev <= devl)
            {
                converged = false;
                break;
            }

            devl = dev;
            jchnge = 0;
            int k1 = iext[1];
            int knz = iext[nz];
            int klow = 0;
            int nut = -nu;
            int j = 1;
            bool flag34 = true;
            double err = 0;
            double dtemp = 0;
            int nut1 = 0;

            while (j < nzz)
            {
                int kup = iext[j + 1];
                int l = iext[j] + 1;
                nut = -nut;
                if (j == 2)
                {
                    y1 = comp;
                }

                comp = dev;
                bool flag = true;
                if (l < kup)
                {
                    err = WeightedError(ad, x, y, nz, grid, des, wt, l);
                    dtemp = (nut * err) - comp;
                    if (dtemp > 0)
                    {
                        comp = nut * err;
                        l++;
                        while (l < kup)
                        {
                            err = WeightedError(ad, x, y, nz, grid, des, wt, l);
                            dtemp = (nut * err) - comp;
                            if (dtemp <= 0)
                            {
                                break;
                            }

                            comp = nut * err;
                            l++;
                        }

                        iext[j] = l - 1;
                        j++;
                        klow = l - 1;
                        jchnge++;
                        flag = false;
                    }
                }

                if (flag)
                {
                    l -= 2;
                    while (l > klow)
                    {
                        err = WeightedError(ad, x, y, nz, grid, des, wt, l);
                        dtemp = (nut * err) - comp;
                        if (dtemp > 0 || jchnge > 0)
                        {
                            break;
                        }

                        l--;
                    }

                    if (l <= klow)
                    {
                        l = iext[j] + 1;
                        if (jchnge > 0)
                        {
                            iext[j] = l - 1;
                            j++;
                            klow = l - 1;
                            jchnge++;
                        }
                        else
                        {
                            l++;
                            while (l < kup)
                            {
                                err = WeightedError(ad, x, y, nz, grid, des, wt, l);
                                dtemp = (nut * err) - comp;
                                if (dtemp > 0)
                                {
                                    break;
                                }

                                l++;
                            }

                            if (l < kup && dtemp > 0)
                            {
                                comp = nut * err;
                                l++;
                                while (l < kup)
                                {
                                    err = WeightedError(ad, x, y, nz, grid, des, wt, l);
                                    dtemp = (nut * err) - comp;
                                    if (dtemp <= 0)
                                    {
                                        break;
                                    }

                                    comp = nut * err;
                                    l++;
                                }

                                iext[j] = l - 1;
                                j++;
                                klow = l - 1;
                                jchnge++;
                            }
                            else
                            {
                                klow = iext[j];
                                j++;
                            }
                        }
                    }
                    else if (dtemp > 0)
                    {
                        comp = nut * err;
                        l--;
                        while (l > klow)
                        {
                            err = WeightedError(ad, x, y, nz, grid, des, wt, l);
                            dtemp = (nut * err) - comp;
                            if (dtemp <= 0)
                            {
                                break;
                            }

                            comp = nut * err;
                            l--;
                        }

                        klow = iext[j];
                        iext[j] = l + 1;
                        j++;
                        jchnge++;
                    }
                    else
                    {
                        klow = iext[j];
                        j++;
                    }
                }
            }

            while (j == nzz)
            {
                double ynz = comp;
                k1 = System.Math.Min(k1, iext[1]);
                knz = System.Math.Max(knz, iext[nz]);
                nut1 = nut;
                nut = -nu;
                int l = 0;
                int kup = k1;
                comp = ynz * 1.00001;
                luck = 1;
                bool flag = true;
                l++;
                while (l < kup)
                {
                    err = WeightedError(ad, x, y, nz, grid, des, wt, l);
                    dtemp = (err * nut) - comp;
                    if (dtemp > 0)
                    {
                        comp = nut * err;
                        j = nzz;
                        l++;
                        while (l < kup)
                        {
                            err = WeightedError(ad, x, y, nz, grid, des, wt, l);
                            dtemp = (nut * err) - comp;
                            if (dtemp <= 0)
                            {
                                break;
                            }

                            comp = nut * err;
                            l++;
                        }

                        iext[j] = l - 1;
                        j++;
                        jchnge++;
                        flag = false;
                        break;
                    }

                    l++;
                }

                if (flag)
                {
                    luck = 6;
                    l = ngrid + 1;
                    klow = knz;
                    nut = -nut1;
                    comp = y1 * 1.00001;
                    l--;
                    while (l > klow)
                    {
                        err = WeightedError(ad, x, y, nz, grid, des, wt, l);
                        dtemp = (err * nut) - comp;
                        if (dtemp > 0)
                        {
                            j = nzz;
                            comp = nut * err;
                            luck += 10;
                            l--;
                            while (l > klow)
                            {
                                err = WeightedError(ad, x, y, nz, grid, des, wt, l);
                                dtemp = (nut * err) - comp;
                                if (dtemp <= 0)
                                {
                                    break;
                                }

                                comp = nut * err;
                                l--;
                            }

                            iext[j] = l + 1;
                            j++;
                            jchnge++;
                            flag = false;
                            break;
                        }

                        l--;
                    }

                    if (flag)
                    {
                        flag34 = false;
                        if (luck != 6)
                        {
                            ShiftDown(iext, k1, nz, nfcns);
                            jchnge++;
                        }

                        break;
                    }
                }
            }

            if (flag34 && j > nzz)
            {
                if (luck > 9)
                {
                    ShiftUp(iext, nz, nzz, nfcns);
                    jchnge++;
                }
                else
                {
                    y1 = System.Math.Max(y1, comp);
                    k1 = iext[nzz];
                    int l = ngrid + 1;
                    klow = knz;
                    nut = -nut1;
                    comp = y1 * 1.00001;
                    l--;
                    while (l > klow)
                    {
                        err = WeightedError(ad, x, y, nz, grid, des, wt, l);
                        dtemp = (err * nut) - comp;
                        if (dtemp > 0)
                        {
                            j = nzz;
                            comp = nut * err;
                            luck += 10;
                            l--;
                            while (l > klow)
                            {
                                err = WeightedError(ad, x, y, nz, grid, des, wt, l);
                                dtemp = (nut * err) - comp;
                                if (dtemp <= 0)
                                {
                                    break;
                                }

                                comp = nut * err;
                                l--;
                            }

                            iext[j] = l + 1;
                            jchnge++;
                            ShiftUp(iext, nz, nzz, nfcns);
                            break;
                        }

                        l--;
                    }

                    if (luck != 6)
                    {
                        ShiftDown(iext, k1, nz, nfcns);
                        jchnge++;
                    }
                }
            }
        }

        double[] h = Coefficients(nfcns, nz, nzz, ngrid, nodd, neg, edge, jb, grid, ad, x, y);
        var extremes = new int[nz];
        Array.Copy(iext, 1, extremes, 0, nz);
        return (h, dev, extremes, converged);
    }

    /// <summary>
    /// The weighted error at one grid point, read off the barycentric interpolant through the
    /// current extremes.
    /// </summary>
    private static double WeightedError(
        double[] ad, double[] x, double[] y, int nz, double[] grid, double[] des, double[] wt, int l)
    {
        double top = 0;
        double bottom = 0;
        double at = System.Math.Cos(2 * System.Math.PI * grid[l]);
        for (int i = 1; i <= nz; i++)
        {
            double c = ad[i] / (at - x[i]);
            top += c * y[i];
            bottom += c;
        }

        return ((top / bottom) - des[l]) * wt[l];
    }

    /// <summary>
    /// One barycentric weight, by a product that steps through the other extremes
    /// <paramref name="m"/> at a time — a stride that keeps the product away from overflow at high
    /// orders, and is the reason a plain product disagrees in the last figures.
    /// </summary>
    private static double BarycentricWeight(int k, int n, int m, double[] x)
    {
        double y = 1;
        double q = x[k];
        for (int l = 1; l <= m; l++)
        {
            for (int i = l; i <= n; i += m)
            {
                double xx = 2 * (q - x[i]);
                if (xx != 0)
                {
                    y *= xx;
                }
            }
        }

        return 1 / y;
    }

    /// <summary>The extreme list rotated down by one, which is what a peak past the last one asks for.</summary>
    private static void ShiftDown(int[] iext, int k1, int nz, int nfcns)
    {
        var updated = new int[iext.Length];
        updated[1] = k1;
        int at = 2;
        for (int i = 2; i <= nz - nfcns; i++)
        {
            updated[at++] = iext[i];
        }

        for (int i = nz - nfcns; i <= nz - 1; i++)
        {
            updated[at++] = iext[i];
        }

        Array.Copy(updated, 1, iext, 1, at - 1);
    }

    /// <summary>The extreme list rotated up by one, for a peak found before the first.</summary>
    private static void ShiftUp(int[] iext, int nz, int nzz, int nfcns)
    {
        var updated = new int[iext.Length];
        int at = 1;
        for (int i = 2; i <= nfcns + 1; i++)
        {
            updated[at++] = iext[i];
        }

        for (int i = nfcns + 1; i <= nz - 1; i++)
        {
            updated[at++] = iext[i];
        }

        updated[at++] = iext[nzz];
        updated[at++] = iext[nzz];
        Array.Copy(updated, 1, iext, 1, at - 1);
    }

    /// <summary>
    /// The impulse response, recovered from the converged interpolant by sampling it at the
    /// Chebyshev points and taking the discrete cosine transform of what comes back.
    /// </summary>
    /// <remarks>
    /// When the design's bands do not reach both ends of the axis, the cosine variable is stretched
    /// onto the part that is used before the transform and the polynomial is un-stretched
    /// afterwards, by a recursion on the Chebyshev coefficients. That stretch is what keeps a
    /// narrow-band design's coefficients accurate, and it is skipped exactly when MATLAB skips it.
    /// </remarks>
    private static double[] Coefficients(
        int nfcns, int nz, int nzz, int ngrid, int nodd, int neg,
        double[] edge, int jb, double[] grid, double[] ad, double[] x, double[] y)
    {
        int nm1 = nfcns - 1;
        const double fsh = 1.0e-6;
        x[nzz] = -2;
        double cn = (2 * nfcns) - 1;
        double delf = 1 / cn;
        int l = 1;
        bool stretched = !((edge[0] == 0 && edge[jb - 1] == 0.5) || nfcns <= 3);

        double aa = 0;
        double bb = 0;
        if (stretched)
        {
            double first = System.Math.Cos(2 * System.Math.PI * grid[1]);
            double last = System.Math.Cos(2 * System.Math.PI * grid[ngrid]);
            aa = 2 / (first - last);
            bb = -(first + last) / (first - last);
        }

        var a = new double[nfcns + 3];
        for (int j = 1; j <= nfcns; j++)
        {
            double ft = (j - 1) * delf;
            double xt = System.Math.Cos(2 * System.Math.PI * ft);
            if (stretched)
            {
                xt = (xt - bb) / aa;
                ft = System.Math.Acos(xt) / (2 * System.Math.PI);
            }

            double xe = x[l];
            while (xt <= xe && xe - xt >= fsh)
            {
                l++;
                xe = x[l];
            }

            if (System.Math.Abs(xt - xe) < fsh)
            {
                a[j] = y[l];
            }
            else
            {
                grid[1] = ft;
                double top = 0;
                double bottom = 0;
                double at = System.Math.Cos(2 * System.Math.PI * ft);
                for (int i = 1; i <= nz; i++)
                {
                    double c = ad[i] / (at - x[i]);
                    top += c * y[i];
                    bottom += c;
                }

                a[j] = top / bottom;
            }

            l = System.Math.Max(1, l - 1);
        }

        double dden = 2 * System.Math.PI / cn;
        var alpha = new double[nfcns + 3];
        for (int j = 1; j <= nfcns; j++)
        {
            double dnum = (j - 1) * dden;
            if (nm1 < 1)
            {
                alpha[j] = a[1];
            }
            else
            {
                double sum = a[1];
                for (int i = 1; i <= nm1; i++)
                {
                    sum += 2 * System.Math.Cos(dnum * i) * a[i + 1];
                }

                alpha[j] = sum;
            }
        }

        alpha[1] /= cn;
        for (int j = 2; j <= nfcns; j++)
        {
            alpha[j] = 2 * alpha[j] / cn;
        }

        if (stretched)
        {
            var p = new double[nfcns + 3];
            var q = new double[nfcns + 3];
            p[1] = (2 * alpha[nfcns] * bb) + alpha[nm1];
            p[2] = 2 * aa * alpha[nfcns];
            q[1] = alpha[nfcns - 2] - alpha[nfcns];
            for (int j = 2; j <= nm1; j++)
            {
                if (j == nm1)
                {
                    aa /= 2;
                    bb /= 2;
                }

                p[j + 1] = 0;
                for (int i = 1; i <= j; i++)
                {
                    a[i] = p[i];
                }

                for (int i = 1; i <= j; i++)
                {
                    p[i] = 2 * bb * a[i];
                }

                p[2] += 2 * a[1] * aa;
                for (int i = 1; i <= j - 1; i++)
                {
                    p[i] += q[i] + (aa * a[i + 1]);
                }

                for (int i = 3; i <= j + 1; i++)
                {
                    p[i] += aa * a[i - 1];
                }

                if (j != nm1)
                {
                    for (int i = 1; i <= j; i++)
                    {
                        q[i] = -a[i];
                    }

                    q[1] += alpha[nfcns - 1 - j];
                }
            }

            for (int j = 1; j <= nfcns; j++)
            {
                alpha[j] = p[j];
            }
        }

        if (nfcns <= 3)
        {
            alpha[nfcns + 1] = 0;
            alpha[nfcns + 2] = 0;
        }

        var h = new List<double>();
        if (neg <= 0)
        {
            if (nodd != 0)
            {
                for (int i = nz - 1; i >= nz - nm1; i--)
                {
                    h.Add(0.5 * alpha[i]);
                }

                h.Add(alpha[1]);
            }
            else
            {
                h.Add(0.25 * alpha[nfcns]);
                for (int i = 0; i < nm1 - 1; i++)
                {
                    h.Add(0.25 * (alpha[nz - 2 - i] + alpha[nfcns - i]));
                }

                h.Add(0.25 * ((2 * alpha[1]) + alpha[2]));
            }
        }
        else if (nodd != 0)
        {
            h.Add(0.25 * alpha[nfcns]);
            h.Add(0.25 * alpha[nm1]);
            for (int i = 0; i < nm1 - 2; i++)
            {
                h.Add(0.25 * (alpha[nz - 3 - i] - alpha[nfcns - i]));
            }

            h.Add(0.25 * ((2 * alpha[1]) - alpha[3]));
            h.Add(0);
        }
        else
        {
            h.Add(0.25 * alpha[nfcns]);
            for (int i = 0; i < nm1 - 1; i++)
            {
                h.Add(0.25 * (alpha[nz - 2 - i] - alpha[nfcns - i]));
            }

            h.Add(0.25 * ((2 * alpha[1]) - alpha[2]));
        }

        return [.. h];
    }

    private static double[] OneBased(double[] values)
    {
        var result = new double[values.Length + 1];
        Array.Copy(values, 0, result, 1, values.Length);
        return result;
    }
}
