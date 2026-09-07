namespace JGraph.Signal;

using JGraph.Numerics;

/// <summary>
/// The twenty named tapering windows of the Signal Processing Toolbox, as MATLAB computes them
/// (M132).
/// </summary>
/// <remarks>
/// <para>
/// A window is three lines of arithmetic, which is why it is tempting to write one from the formula
/// in a textbook and move on. That produces a window that is right to about eleven digits and wrong
/// after that, because MATLAB does not evaluate the formula the way the textbook writes it. Three
/// habits account for nearly all of the difference and every one of them is reproduced here.
/// </para>
/// <para>
/// The first is mirroring. The raised-cosine family — Hann, Hamming, Blackman, flat-top — is
/// computed for the first half of its length and then reflected, so the two halves are equal bit for
/// bit rather than equal to within the rounding of two different cosines. The second is the
/// periodic flag, which is not a different formula but the same one at one greater length with the
/// duplicate endpoint dropped. The third is the order of operations inside each expression: Nuttall
/// scales its index by <c>i*2*pi/(L-1)</c> and Blackman-Harris by <c>2*pi*i/(L-1)</c>, which are
/// the same number in algebra and not always the same double, so each is written the way its own
/// source writes it.
/// </para>
/// <para>
/// Two windows are not formulas at all. Dolph-Chebyshev is an inverse transform of a Chebyshev
/// polynomial sampled on the unit circle, and MATLAB computes it in a MEX file this cannot read; the
/// classical algorithm is written out here and the fixture is what decides whether it agrees.
/// Taylor is a finite sum of cosine terms whose coefficients come from a ratio of products.
/// </para>
/// </remarks>
public static class SignalWindows
{
    /// <summary>The empty window: MATLAB answers a 0-by-1, and length one answers a bare 1.</summary>
    /// <remarks>
    /// Returns null when the length is neither of those, which is the caller's signal to go on and
    /// compute something. Every window in the toolbox shares this preamble.
    /// </remarks>
    private static double[]? Trivial(int length)
    {
        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length), "A window length is a whole number that is not negative.");
        }

        return length switch
        {
            0 => [],
            1 => [1.0],
            _ => null,
        };
    }

    /// <summary>The raised-cosine family, computed on one half and reflected onto the other.</summary>
    /// <remarks>
    /// This is <c>gencoswin</c>. <paramref name="periodic"/> lengthens the window by one before the
    /// arithmetic and drops the repeated endpoint after it, which is exactly how MATLAB turns a
    /// symmetric window into a periodic one — the same coefficients over a denominator one larger.
    /// </remarks>
    private static double[] Cosine(string kind, int length, bool periodic)
    {
        double[]? trivial = Trivial(length);
        if (trivial is not null)
        {
            return trivial;
        }

        int extended = periodic ? length + 1 : length;
        int odd = extended % 2;
        int half = (extended + odd) / 2;
        var head = new double[half];
        for (int i = 0; i < half; i++)
        {
            double t = 2.0 * System.Math.PI * i / (extended - 1);
            head[i] = kind switch
            {
                "hann" => 0.5 - (0.5 * System.Math.Cos(t)),
                "hamming" => 0.54 - (0.46 * System.Math.Cos(t)),
                "blackman" => 0.42 - (0.5 * System.Math.Cos(t)) + (0.08 * System.Math.Cos(2.0 * t)),
                _ => 0.21557895
                    - (0.41663158 * System.Math.Cos(t))
                    + (0.277263158 * System.Math.Cos(2.0 * t))
                    - (0.083578947 * System.Math.Cos(3.0 * t))
                    + (0.006947368 * System.Math.Cos(4.0 * t)),
            };
        }

        // Blackman's endpoint is a difference of three numbers that should cancel exactly and does
        // not; MATLAB pins it to zero rather than let a window start at -1e-17.
        if (kind == "blackman")
        {
            head[0] = 0.0;
        }

        var w = new double[length];
        head.CopyTo(w, 0);
        int at = half;
        for (int i = half - 1 - odd; i >= (periodic ? 1 : 0); i--)
        {
            w[at++] = head[i];
        }

        return w;
    }

    /// <summary><c>hann(L)</c> and <c>hann(L, sflag)</c>.</summary>
    public static double[] Hann(int length, bool periodic = false) =>
        Cosine("hann", length, periodic);

    /// <summary><c>hamming(L)</c> and <c>hamming(L, sflag)</c>.</summary>
    public static double[] Hamming(int length, bool periodic = false) =>
        Cosine("hamming", length, periodic);

    /// <summary><c>blackman(L)</c> and <c>blackman(L, sflag)</c>.</summary>
    public static double[] Blackman(int length, bool periodic = false) =>
        Cosine("blackman", length, periodic);

    /// <summary><c>flattopwin(L)</c> and <c>flattopwin(L, sflag)</c>.</summary>
    public static double[] FlatTop(int length, bool periodic = false) =>
        Cosine("flattopwin", length, periodic);

    /// <summary><c>rectwin(L)</c>, and <c>boxcar</c>, which is the same window under its old name.</summary>
    public static double[] Rectangular(int length)
    {
        double[]? trivial = Trivial(length);
        if (trivial is not null)
        {
            return trivial;
        }

        var w = new double[length];
        Array.Fill(w, 1.0);
        return w;
    }

    /// <summary>
    /// <c>hanning(L)</c>, which is not <c>hann</c>: it drops the two zero endpoints instead of
    /// including them, so its denominator is <c>L+1</c> and its first sample is not zero.
    /// </summary>
    public static double[] Hanning(int length, bool periodic = false)
    {
        double[]? trivial = Trivial(length);
        if (trivial is not null)
        {
            return trivial;
        }

        if (periodic)
        {
            double[] inner = SymmetricHanning(length - 1);
            var padded = new double[length];
            inner.CopyTo(padded, 1);
            return padded;
        }

        return SymmetricHanning(length);
    }

    /// <summary>The half-computed, reflected body of <c>hanning</c>.</summary>
    private static double[] SymmetricHanning(int n)
    {
        if (n <= 0)
        {
            return [];
        }

        if (n == 1)
        {
            return [1.0];
        }

        int half = (n % 2 == 0) ? n / 2 : (n + 1) / 2;
        var head = new double[half];
        for (int i = 0; i < half; i++)
        {
            head[i] = 0.5 * (1.0 - System.Math.Cos(2.0 * System.Math.PI * (i + 1) / (n + 1)));
        }

        var w = new double[n];
        head.CopyTo(w, 0);
        int at = half;
        for (int i = (n % 2 == 0) ? half - 1 : half - 2; i >= 0; i--)
        {
            w[at++] = head[i];
        }

        return w;
    }

    /// <summary><c>bartlett(L)</c>: the triangle that reaches zero at both ends.</summary>
    public static double[] Bartlett(int length)
    {
        double[]? trivial = Trivial(length);
        if (trivial is not null)
        {
            return trivial;
        }

        int half = ((length - 1) / 2) + 1;
        var head = new double[half];
        for (int i = 0; i < half; i++)
        {
            head[i] = 2.0 * i / (length - 1);
        }

        var w = new double[length];
        head.CopyTo(w, 0);
        int at = half;
        int from = (length % 2 != 0) ? ((length - 1) / 2) - 1 : (length / 2) - 1;
        for (int i = from; i >= 0; i--)
        {
            w[at++] = head[i];
        }

        return w;
    }

    /// <summary><c>triang(L)</c>: the triangle whose ends are one step short of zero.</summary>
    public static double[] Triangular(int length)
    {
        double[]? trivial = Trivial(length);
        if (trivial is not null)
        {
            return trivial;
        }

        bool odd = length % 2 != 0;
        int half = odd ? (length + 1) / 2 : length / 2;
        var head = new double[half];
        for (int k = 1; k <= half; k++)
        {
            head[k - 1] = odd
                ? 2.0 * k / (length + 1)
                : ((2.0 * k) - 1) / length;
        }

        var w = new double[length];
        head.CopyTo(w, 0);
        int at = half;
        int from = odd ? ((length - 1) / 2) - 1 : (length / 2) - 1;
        for (int i = from; i >= 0; i--)
        {
            w[at++] = head[i];
        }

        return w;
    }

    /// <summary><c>barthannwin(L)</c>: a Bartlett and a Hann blended by fixed weights.</summary>
    public static double[] BartlettHann(int length)
    {
        double[]? trivial = Trivial(length);
        if (trivial is not null)
        {
            return trivial;
        }

        var w = new double[length];
        for (int i = 0; i < length; i++)
        {
            double t = ((double)i / (length - 1)) - 0.5;
            w[i] = 0.62 - (0.48 * System.Math.Abs(t)) + (0.38 * System.Math.Cos(2.0 * System.Math.PI * t));
        }

        return w;
    }

    /// <summary><c>bohmanwin(L)</c>: the convolution of two half-cycle cosine lobes.</summary>
    public static double[] Bohman(int length)
    {
        double[]? trivial = Trivial(length);
        if (trivial is not null)
        {
            return trivial;
        }

        var w = new double[length];
        for (int i = 1; i < length - 1; i++)
        {
            double p = System.Math.Abs(-1.0 + (i * 2.0 / (length - 1)));
            w[i] = ((1.0 - p) * System.Math.Cos(System.Math.PI * p))
                + ((1.0 / System.Math.PI) * System.Math.Sin(System.Math.PI * p));
        }

        return w;
    }

    /// <summary><c>parzenwin(L)</c>: the piecewise-cubic B-spline window.</summary>
    public static double[] Parzen(int length)
    {
        double[]? trivial = Trivial(length);
        if (trivial is not null)
        {
            return trivial;
        }

        double halfSpan = (length - 1) / 2.0;
        double quarter = (length - 1) / 4.0;
        double scale = length / 2.0;
        var w = new double[length];
        for (int i = 0; i < length; i++)
        {
            double k = -halfSpan + i;
            double a = System.Math.Abs(k) / scale;
            w[i] = System.Math.Abs(k) <= quarter
                ? 1.0 - (6.0 * System.Math.Pow(a, 2.0)) + (6.0 * System.Math.Pow(a, 3.0))
                : 2.0 * System.Math.Pow(1.0 - a, 3.0);
        }

        return w;
    }

    /// <summary><c>blackmanharris(L)</c> and its periodic form.</summary>
    /// <remarks>
    /// The one window in this family MATLAB does not mirror: it runs the four-term sum over every
    /// index, so the two halves agree only to the rounding of two cosine calls.
    /// </remarks>
    public static double[] BlackmanHarris(int length, bool periodic = false)
    {
        double[]? trivial = Trivial(length);
        if (trivial is not null)
        {
            return trivial;
        }

        var w = new double[length];
        for (int i = 0; i < length; i++)
        {
            double t = periodic
                ? 2.0 * System.Math.PI * i / length
                : 2.0 * System.Math.PI * i / (length - 1);
            w[i] = 0.35875
                - (0.48829 * System.Math.Cos(t))
                + (0.14128 * System.Math.Cos(2.0 * t))
                - (0.01168 * System.Math.Cos(3.0 * t));
        }

        return w;
    }

    /// <summary><c>nuttallwin(L)</c>: Nuttall's minimum four-term window.</summary>
    public static double[] Nuttall(int length, bool periodic = false)
    {
        double[]? trivial = Trivial(length);
        if (trivial is not null)
        {
            return trivial;
        }

        // The coefficients enter the sum with alternating signs, and the sum is accumulated in the
        // order MATLAB's row-times-column product accumulates it.
        double[] a = [0.3635819, -0.4891775, 0.1365995, -0.0106411];
        var w = new double[length];
        for (int i = 0; i < length; i++)
        {
            double x = periodic
                ? i * 2.0 * System.Math.PI / length
                : i * 2.0 * System.Math.PI / (length - 1);
            double sum = a[0];
            sum += System.Math.Cos(x) * a[1];
            sum += System.Math.Cos(2.0 * x) * a[2];
            sum += System.Math.Cos(3.0 * x) * a[3];
            w[i] = sum;
        }

        return w;
    }

    /// <summary><c>gausswin(L)</c> and <c>gausswin(L, alpha)</c>.</summary>
    public static double[] Gaussian(int length, double alpha = 2.5)
    {
        if (alpha < 0 || double.IsNaN(alpha) || double.IsInfinity(alpha))
        {
            throw new ArgumentOutOfRangeException(
                nameof(alpha), "gausswin's alpha is a finite number that is not negative.");
        }

        double[]? trivial = Trivial(length);
        if (trivial is not null)
        {
            return trivial;
        }

        double span = length - 1;
        var w = new double[length];
        for (int i = 0; i < length; i++)
        {
            double n = i - (span / 2.0);
            double v = alpha * n / (span / 2.0);
            w[i] = System.Math.Exp(-(1.0 / 2.0) * (v * v));
        }

        return w;
    }

    /// <summary><c>tukeywin(L)</c> and <c>tukeywin(L, r)</c>: a flat top between two cosine tapers.</summary>
    public static double[] Tukey(int length, double ratio = 0.5)
    {
        double[]? trivial = Trivial(length);
        if (trivial is not null)
        {
            return trivial;
        }

        if (ratio <= 0)
        {
            return Rectangular(length);
        }

        if (ratio >= 1)
        {
            return Hann(length);
        }

        double per = ratio / 2.0;
        int tl = (int)System.Math.Floor(per * (length - 1)) + 1;
        int th = length - tl + 1;
        var w = new double[length];
        for (int i = 0; i < tl; i++)
        {
            double t = Linspace(i, length);
            w[i] = (1.0 + System.Math.Cos(System.Math.PI / per * (t - per))) / 2.0;
        }

        for (int i = tl; i < th - 1; i++)
        {
            w[i] = 1.0;
        }

        for (int i = th - 1; i < length; i++)
        {
            double t = Linspace(i, length);
            w[i] = (1.0 + System.Math.Cos(System.Math.PI / per * (t - 1.0 + per))) / 2.0;
        }

        return w;
    }

    /// <summary>The i-th of <paramref name="count"/> points of <c>linspace(0, 1, count)</c>.</summary>
    /// <remarks>
    /// MATLAB's <c>linspace</c> steps to the second-to-last point and then writes the endpoint
    /// literally, so the last value is exactly 1 and not <c>(n-1)/(n-1)</c>.
    /// </remarks>
    private static double Linspace(int i, int count) =>
        i == count - 1 ? 1.0 : (double)i / (count - 1);

    /// <summary><c>kaiser(L)</c> and <c>kaiser(L, beta)</c>.</summary>
    public static double[] Kaiser(int length, double beta = 0.5)
    {
        double[]? trivial = Trivial(length);
        if (trivial is not null)
        {
            return trivial;
        }

        double bes = System.Math.Abs(BesselFunctions.I(0, beta));
        int odd = length % 2;
        double xind = (double)(length - 1) * (length - 1);
        int n = (length + 1) / 2;
        var head = new double[n];
        for (int i = 0; i < n; i++)
        {
            double xi = i + (0.5 * (1 - odd));
            xi = 4.0 * xi * xi;
            head[i] = BesselFunctions.I(0, beta * System.Math.Sqrt(1.0 - (xi / xind))) / bes;
        }

        var w = new double[length];
        int at = 0;
        for (int i = n - 1; i >= odd; i--)
        {
            w[at++] = System.Math.Abs(head[i]);
        }

        for (int i = 0; i < n; i++)
        {
            w[at++] = System.Math.Abs(head[i]);
        }

        return w;
    }

    /// <summary><c>chebwin(L)</c> and <c>chebwin(L, r)</c>: equal side-lobes at <c>r</c> dB down.</summary>
    /// <remarks>
    /// MATLAB computes this in a MEX file. The algorithm written here is the classical one its own
    /// M-file used before the MEX arrived: sample the Chebyshev polynomial of degree <c>L-1</c> on
    /// the unit circle, invert the transform, and normalise. An even length needs a half-bin shift
    /// before the transform, which is the <c>exp</c> factor below.
    /// </remarks>
    public static double[] Chebyshev(int length, double attenuation = 100.0)
    {
        double[]? trivial = Trivial(length);
        if (trivial is not null)
        {
            return trivial;
        }

        double r = System.Math.Abs(attenuation);
        double gamma = System.Math.Pow(10.0, -r / 20.0);
        int order = length - 1;
        double beta = System.Math.Cosh(1.0 / order * Acosh(1.0 / gamma));

        var re = new double[length];
        var im = new double[length];
        for (int k = 0; k < length; k++)
        {
            double x = beta * System.Math.Cos(System.Math.PI * k / length);
            double p;
            if (System.Math.Abs(x) <= 1.0)
            {
                p = System.Math.Cos(order * System.Math.Acos(x));
            }
            else if (x > 0)
            {
                p = System.Math.Cosh(order * Acosh(x));
            }
            else
            {
                // Below minus one the inverse hyperbolic cosine is complex — it picks up a half
                // turn — and the cosine of a whole number of half turns is a sign. MATLAB reaches
                // the same answer by carrying the imaginary part along and dropping it later.
                p = (order % 2 == 0 ? 1.0 : -1.0) * System.Math.Cosh(order * Acosh(-x));
            }

            re[k] = p;
            im[k] = 0.0;
        }

        var w = new double[length];
        if (length % 2 != 0)
        {
            FftKernels.Transform(re, im, length, inverse: false);
            int m = (length + 1) / 2;
            double first = re[0];
            for (int i = 0; i < m; i++)
            {
                w[m - 1 + i] = re[i] / first;
            }

            for (int i = 1; i < m; i++)
            {
                w[m - 1 - i] = w[m - 1 + i];
            }
        }
        else
        {
            for (int k = 0; k < length; k++)
            {
                double angle = System.Math.PI / length * k;
                double c = System.Math.Cos(angle);
                double s = System.Math.Sin(angle);
                double pr = re[k];
                re[k] = pr * c;
                im[k] = pr * s;
            }

            FftKernels.Transform(re, im, length, inverse: false);
            int m = (length / 2) + 1;
            double second = re[1];
            for (int i = 1; i < m; i++)
            {
                w[m - 1 - i] = re[i] / second;
                w[m - 2 + i] = re[i] / second;
            }
        }

        return w;
    }

    /// <summary><c>taylorwin(L)</c>, <c>(L, nbar)</c> and <c>(L, nbar, sll)</c>.</summary>
    public static double[] Taylor(int length, int nbar = 4, double sidelobe = -30.0)
    {
        if (nbar <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nbar), "taylorwin's nbar is a positive whole number.");
        }

        if (sidelobe > 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sidelobe), "taylorwin's side-lobe level is in decibels below the peak, so it is not positive.");
        }

        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length), "A window length is a whole number that is not negative.");
        }

        if (length == 0)
        {
            return [];
        }

        double a = Acosh(System.Math.Pow(10.0, -sidelobe / 20.0)) / System.Math.PI;
        double sp2 = (double)nbar * nbar / ((a * a) + ((nbar - 0.5) * (nbar - 0.5)));

        var w = new double[length];
        Array.Fill(w, 1.0);
        var sum = new double[length];
        for (int m = 1; m <= nbar - 1; m++)
        {
            double fm = TaylorCoefficient(m, sp2, a, nbar);
            for (int k = 0; k < length; k++)
            {
                double xi = (k - (0.5 * length) + 0.5) / length;
                sum[k] = (fm * System.Math.Cos(2.0 * System.Math.PI * m * xi)) + sum[k];
            }
        }

        for (int k = 0; k < length; k++)
        {
            w[k] += 2.0 * sum[k];
        }

        return w;
    }

    /// <summary>One Fourier coefficient of the Taylor window.</summary>
    private static double TaylorCoefficient(int m, double sp2, double a, int nbar)
    {
        double num = 1.0;
        for (int n = 1; n <= nbar - 1; n++)
        {
            num *= 1.0 - ((double)m * m / sp2 / ((a * a) + ((n - 0.5) * (n - 0.5))));
        }

        double den = 1.0;
        for (int p = 1; p <= nbar - 1; p++)
        {
            if (p == m)
            {
                continue;
            }

            den *= 1.0 - ((double)m * m / ((double)p * p));
        }

        return System.Math.Pow(-1.0, m + 1) * num / (2.0 * den);
    }

    /// <summary>The inverse hyperbolic cosine, which .NET spells the long way.</summary>
    private static double Acosh(double x) => System.Math.Acosh(x);
}
