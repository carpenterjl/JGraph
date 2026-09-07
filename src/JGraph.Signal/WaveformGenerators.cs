namespace JGraph.Signal;

using System.Numerics;

/// <summary>
/// The waveform generators of the Signal Processing Toolbox: swept sinusoids, periodic waves and
/// single pulses (M132).
/// </summary>
/// <remarks>
/// <para>
/// These are all one line of arithmetic per sample, and they are all written here rather than at the
/// call site because the interesting part of each is not the formula but its edges. A sawtooth is a
/// ramp until you ask what it does at exactly zero and at a negative time; a rectangular pulse is a
/// comparison until you ask which of the two edges is closed; a Dirichlet kernel is a ratio of sines
/// until the denominator vanishes, which it does at every multiple of two pi.
/// </para>
/// <para>
/// MATLAB has an answer to each of those and the answers are not the obvious ones — the rectangular
/// pulse includes its left edge and excludes its right, the triangular pulse is open at both ends
/// and closed at its apex, and the Dirichlet kernel falls back to the sign of a cosine rather than
/// to a limit. Each is reproduced from the source rather than from the definition.
/// </para>
/// </remarks>
public static class WaveformGenerators
{
    /// <summary>The sweep laws <c>chirp</c> understands.</summary>
    public enum Sweep
    {
        /// <summary>Frequency rises linearly with time.</summary>
        Linear,

        /// <summary>Frequency rises with the square of time.</summary>
        Quadratic,

        /// <summary>Frequency rises by a constant ratio per unit time.</summary>
        Logarithmic,
    }

    /// <summary>Which way a quadratic sweep bends when the caller insists.</summary>
    public enum Bend
    {
        /// <summary>Whichever way the endpoints imply.</summary>
        Default,

        /// <summary>Bending upwards away from the chord.</summary>
        Convex,

        /// <summary>Bending downwards away from the chord.</summary>
        Concave,
    }

    /// <summary>The normalised sinc, <c>sin(pi x) / (pi x)</c>, which is one at the origin.</summary>
    public static double[] Sinc(ReadOnlySpan<double> x)
    {
        var y = new double[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            y[i] = x[i] == 0
                ? 1.0
                : System.Math.Sin(System.Math.PI * x[i]) / (System.Math.PI * x[i]);
        }

        return y;
    }

    /// <summary>The Dirichlet, or periodic sinc, kernel of order <paramref name="n"/>.</summary>
    public static double[] Dirichlet(ReadOnlySpan<double> x, int n)
    {
        if (n < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(n), "diric's N is a whole number of at least one.");
        }

        const double Tolerance = 2.220446049250313e-16 * 1e4;
        var y = new double[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            double s = System.Math.Sin(0.5 * x[i]);
            y[i] = System.Math.Abs(s) > Tolerance
                ? System.Math.Sin(n * 0.5 * x[i]) / (n * s)
                : System.Math.Sign(System.Math.Cos(x[i] * ((n + 1) / 2.0)));
        }

        return y;
    }

    /// <summary>A sawtooth of period two pi, rising to <paramref name="width"/> of the way through.</summary>
    public static double[] Sawtooth(ReadOnlySpan<double> t, double width = 1.0)
    {
        if (width is < 0 or > 1 || double.IsNaN(width))
        {
            throw new ArgumentOutOfRangeException(nameof(width), "sawtooth's width lies between zero and one.");
        }

        double twoPi = 2.0 * System.Math.PI;
        double c1 = width is > 0 and < 1 ? 2.0 / width : 2.0;
        double c2 = width is > 0 and < 1 ? 2.0 / (1.0 - width) : 2.0;
        var y = new double[t.Length];
        for (int i = 0; i < t.Length; i++)
        {
            double rt = Remainder(t[i], twoPi) * (1.0 / twoPi);
            double negative = t[i] < 0 ? 1.0 : 0.0;
            if (rt > 0)
            {
                y[i] = rt > width
                    ? (-negative - rt + 1.0 - (0.5 * (1.0 - width))) * c2
                    : (negative + rt - (0.5 * width)) * c1;
            }
            else if (rt < 0)
            {
                y[i] = rt < width - 1.0
                    ? (negative + rt - (0.5 * width)) * c1
                    : (-negative - rt + 1.0 - (0.5 * (1.0 - width))) * c2;
            }
            else if (width > 0)
            {
                y[i] = (rt - (0.5 * width)) * c1;
            }
            else
            {
                y[i] = 1.0;
            }
        }

        return y;
    }

    /// <summary>A square wave of period two pi with the given duty cycle in per cent.</summary>
    public static double[] Square(ReadOnlySpan<double> t, double duty = 50.0)
    {
        double w0 = 2.0 * System.Math.PI * duty / 100.0;
        var y = new double[t.Length];
        for (int i = 0; i < t.Length; i++)
        {
            double m = Modulo(t[i], 2.0 * System.Math.PI);
            y[i] = m < w0 ? 1.0 : -1.0;
        }

        return y;
    }

    /// <summary>A rectangular pulse of width <paramref name="width"/> centred on the origin.</summary>
    public static double[] RectangularPulse(ReadOnlySpan<double> t, double width = 1.0)
    {
        const double Eps = 2.220446049250313e-16;
        var y = new double[t.Length];
        for (int i = 0; i < t.Length; i++)
        {
            y[i] = System.Math.Abs(t[i]) < (width / 2.0) - Eps ? 1.0 : 0.0;

            // The left edge belongs to the pulse and the right edge does not, which is what keeps
            // two abutting pulses from overlapping by one sample.
            if (System.Math.Abs(t[i] - (-width / 2.0)) < Eps)
            {
                y[i] = 1.0;
            }
        }

        return y;
    }

    /// <summary>A triangular pulse of width <paramref name="width"/>, skewed by <paramref name="skew"/>.</summary>
    public static double[] TriangularPulse(ReadOnlySpan<double> t, double width = 1.0, double skew = 0.0)
    {
        if (System.Math.Abs(skew) > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(skew), "tripuls's skew lies between minus one and one.");
        }

        double apex = width / 2.0 * skew;
        double half = width / 2.0;
        var y = new double[t.Length];
        for (int i = 0; i < t.Length; i++)
        {
            double x = t[i];
            if (x > -half && x < apex)
            {
                y[i] = ((2.0 * x) + width) / (width * (1.0 + skew));
            }
            else if (x > apex && x < half)
            {
                y[i] = 1.0 - (((2.0 * x) - (skew * width)) / (width * (1.0 - skew)));
            }
            else if (x == apex)
            {
                y[i] = 1.0;
            }
        }

        return y;
    }

    /// <summary>A Gaussian-modulated sinusoid: the in-phase part, the quadrature part and the envelope.</summary>
    public static (double[] InPhase, double[] Quadrature, double[] Envelope) GaussianPulse(
        ReadOnlySpan<double> t, double centre = 1e3, double bandwidth = 0.5, double bandwidthReference = -6.0)
    {
        double tv = GaussianPulseTimeVariance(centre, bandwidth, bandwidthReference);
        var envelope = new double[t.Length];
        var inPhase = new double[t.Length];
        var quadrature = new double[t.Length];
        for (int i = 0; i < t.Length; i++)
        {
            envelope[i] = System.Math.Exp(-t[i] * t[i] / (2.0 * tv));
            inPhase[i] = envelope[i] * CosPi(2.0 * centre * t[i]);
            quadrature[i] = envelope[i] * SinPi(2.0 * centre * t[i]);
            if (double.IsInfinity(t[i]))
            {
                inPhase[i] = 0;
                quadrature[i] = 0;
            }
        }

        return (inPhase, quadrature, envelope);
    }

    /// <summary>The time at which a Gaussian pulse has fallen to <paramref name="trailing"/> decibels.</summary>
    public static double GaussianPulseCutoff(
        double centre, double bandwidth, double bandwidthReference, double trailing)
    {
        double tv = GaussianPulseTimeVariance(centre, bandwidth, bandwidthReference);
        double delta = System.Math.Pow(10.0, trailing / 20.0);
        return System.Math.Sqrt(-2.0 * tv * System.Math.Log(delta));
    }

    /// <summary>The variance of the pulse's time envelope, which both of its forms need.</summary>
    private static double GaussianPulseTimeVariance(double centre, double bandwidth, double bandwidthReference)
    {
        if (centre < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(centre), "gauspuls's centre frequency is not negative.");
        }

        if (bandwidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bandwidth), "gauspuls's fractional bandwidth is positive.");
        }

        if (bandwidthReference >= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bandwidthReference), "gauspuls's reference level is in decibels below the peak, so it is negative.");
        }

        double r = System.Math.Pow(10.0, bandwidthReference / 20.0);
        double fv = -bandwidth * bandwidth * centre * centre / (8.0 * System.Math.Log(r));
        return 1.0 / (4.0 * System.Math.PI * System.Math.PI * fv);
    }

    /// <summary>A Gaussian monopulse of centre frequency <paramref name="centre"/>.</summary>
    public static double[] GaussianMonopulse(ReadOnlySpan<double> t, double centre = 1e3)
    {
        if (centre < 0 || double.IsNaN(centre) || double.IsInfinity(centre))
        {
            throw new ArgumentOutOfRangeException(nameof(centre), "gmonopuls's centre frequency is finite and not negative.");
        }

        double gain = 2.0 * System.Math.Sqrt(System.Math.E);
        var y = new double[t.Length];
        for (int i = 0; i < t.Length; i++)
        {
            double u = System.Math.PI * t[i] * centre;
            y[i] = gain * u * System.Math.Exp(-2.0 * (u * u));
        }

        return y;
    }

    /// <summary>The time between a monopulse's smallest and largest amplitudes.</summary>
    public static double GaussianMonopulseCutoff(double centre) => 1.0 / (System.Math.PI * centre);

    /// <summary>A swept-frequency cosine, real or analytic.</summary>
    /// <remarks>
    /// <paramref name="rows"/> and <paramref name="columns"/> describe the shape of the time axis in
    /// column-major order, and they matter for exactly one reason: forcing a quadratic sweep to bend
    /// against its endpoints makes MATLAB reverse the time axis with <c>fliplr</c>, which flips a row
    /// of samples and leaves a column of them alone.
    /// </remarks>
    public static Complex[] Chirp(
        ReadOnlySpan<double> t, double f0, double t1, double f1, double phase,
        Sweep sweep, Bend bend, bool complex, int rows, int columns)
    {
        var y = new Complex[t.Length];
        if (sweep == Sweep.Logarithmic)
        {
            if (f0 <= 0 || f1 <= 0 || f0 == f1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(f0), "A logarithmic sweep needs two different frequencies, both above zero.");
            }

            double scale = t1 / System.Math.Log(f1 / f0) * f0;
            for (int i = 0; i < t.Length; i++)
            {
                double phi = scale * (System.Math.Pow(f1 / f0, t[i] / t1) - 1.0);
                y[i] = Sample(phi, phase, complex);
            }

            return y;
        }

        double power = sweep == Sweep.Linear ? 1.0 : 2.0;
        double start = f0;
        double end = f1;

        // A quadratic sweep bends one way by default and the other on request; MATLAB gets the
        // other one by running the same arithmetic over a reversed, negated time axis.
        bool reverse = sweep == Sweep.Quadratic
            && ((f0 < f1 && bend == Bend.Convex) || (f0 > f1 && bend == Bend.Concave));
        if (reverse)
        {
            start = f1;
            end = f0;
        }

        double beta = (end - start) * System.Math.Pow(t1, -power);
        for (int i = 0; i < t.Length; i++)
        {
            double time = reverse ? -t[MirroredColumn(i, rows, columns)] : t[i];
            double x = (beta / (1.0 + power) * System.Math.Pow(time, 1.0 + power)) + (start * time);
            y[i] = Sample(x, phase, complex);
        }

        return y;
    }

    /// <summary>A polynomial sweep: the instantaneous frequency is <paramref name="coefficients"/>.</summary>
    public static Complex[] ChirpPolynomial(ReadOnlySpan<double> t, ReadOnlySpan<double> coefficients)
    {
        // The phase is the integral of the frequency, and MATLAB integrates with a zero constant.
        int n = coefficients.Length;
        var integral = new double[n + 1];
        for (int i = 0; i < n; i++)
        {
            integral[i] = coefficients[i] / (n - i);
        }

        var y = new Complex[t.Length];
        for (int i = 0; i < t.Length; i++)
        {
            double acc = 0;
            foreach (double c in integral)
            {
                acc = (acc * t[i]) + c;
            }

            y[i] = new Complex(System.Math.Cos(2.0 * System.Math.PI * acc), 0);
        }

        return y;
    }

    /// <summary>Where element <paramref name="i"/> lands when the columns are reversed.</summary>
    private static int MirroredColumn(int i, int rows, int columns)
    {
        if (rows <= 0 || columns <= 1)
        {
            return i;
        }

        int row = i % rows;
        int column = i / rows;
        return ((columns - 1 - column) * rows) + row;
    }

    /// <summary>One sample of a chirp from its phase in cycles.</summary>
    private static Complex Sample(double cycles, double phase, bool complex)
    {
        double twoPi = 2.0 * System.Math.PI;
        if (!complex)
        {
            return new Complex(System.Math.Cos(twoPi * (cycles + (phase / 360.0))), 0);
        }

        // The analytic form is not a Hilbert transform of the real one; MATLAB writes the quadrature
        // part as the same cosine a quarter cycle later, negated, which is exact rather than filtered.
        var rotation = Complex.Exp(Complex.ImaginaryOne * twoPi * phase / 360.0);
        var body = new Complex(
            System.Math.Cos(twoPi * cycles),
            -System.Math.Cos(twoPi * (cycles + 0.25)));
        return rotation * body;
    }

    /// <summary><c>cospi</c>: the cosine of pi times its argument, exact at the half-integers.</summary>
    private static double CosPi(double x)
    {
        if (double.IsInfinity(x) || double.IsNaN(x))
        {
            return double.NaN;
        }

        double half = Modulo(x, 2.0);
        return half switch
        {
            0.5 or 1.5 => 0.0,
            0.0 => 1.0,
            1.0 => -1.0,
            _ => System.Math.Cos(System.Math.PI * x),
        };
    }

    /// <summary><c>sinpi</c>: the sine of pi times its argument, exact at the integers.</summary>
    private static double SinPi(double x)
    {
        if (double.IsInfinity(x) || double.IsNaN(x))
        {
            return double.NaN;
        }

        double half = Modulo(x, 2.0);
        return half switch
        {
            0.0 or 1.0 => 0.0,
            0.5 => 1.0,
            1.5 => -1.0,
            _ => System.Math.Sin(System.Math.PI * x),
        };
    }

    /// <summary>MATLAB's <c>rem</c>: the remainder that keeps the sign of the dividend.</summary>
    private static double Remainder(double x, double y) =>
        y == 0 ? double.NaN : x - (System.Math.Truncate(x / y) * y);

    /// <summary>MATLAB's <c>mod</c>: the remainder that keeps the sign of the divisor.</summary>
    private static double Modulo(double x, double y)
    {
        if (y == 0)
        {
            return x;
        }

        double m = x - (System.Math.Floor(x / y) * y);
        return m;
    }
}
