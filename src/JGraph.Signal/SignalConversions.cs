namespace JGraph.Signal;

using JGraph.Numerics;

/// <summary>
/// The unit and word-length conversions of the Signal Processing Toolbox, and Marcum's Q function
/// (M132).
/// </summary>
/// <remarks>
/// <para>
/// The decibel conversions are one line each and are here for one reason: there are two of them per
/// direction, and which one is right depends on whether the number is an amplitude or a power. A
/// factor of ten separates them and the mistake is silent, so each of the four names exists
/// separately rather than one taking a flag.
/// </para>
/// <para>
/// The word-length pair quantises a real signal to a fixed number of bits and back. They are not
/// inverses: encoding throws away everything below the last bit, so decoding recovers the middle of
/// the interval the sample fell in, not the sample.
/// </para>
/// <para>
/// Marcum's Q is the tail probability of a non-central chi distribution and it is the odd one out
/// here — it is the one function in this file that cannot be written as an expression. It is
/// computed by the Cantrell-Ojha series with the starting index chosen so that the terms begin
/// where they are large enough to matter, which is what keeps it accurate when its arguments are
/// far apart.
/// </para>
/// </remarks>
public static class SignalConversions
{
    /// <summary>Decibels to an amplitude ratio.</summary>
    public static double DecibelsToMagnitude(double db) => System.Math.Pow(10.0, db / 20.0);

    /// <summary>An amplitude ratio to decibels; a negative amplitude has none.</summary>
    public static double MagnitudeToDecibels(double magnitude) =>
        magnitude < 0 ? double.NaN : 20.0 * System.Math.Log10(magnitude);

    /// <summary>Decibels to a power ratio.</summary>
    public static double DecibelsToPower(double db) => System.Math.Pow(10.0, db / 10.0);

    /// <summary>A power ratio to decibels.</summary>
    /// <remarks>
    /// The detour through three hundred is MATLAB's, and it is not decoration: adding and then
    /// subtracting the same number rounds the result to the same place a table of decibels would,
    /// so that a power of exactly one comes back as exactly zero.
    /// </remarks>
    public static double PowerToDecibels(double power)
    {
        if (power < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(power), "pow2db needs a power that is not negative.");
        }

        return ((10.0 * System.Math.Log10(power)) + 300.0) - 300.0;
    }

    /// <summary>Quantises one sample to <paramref name="bits"/> bits over the range +/- <paramref name="peak"/>.</summary>
    public static double Encode(double u, int bits, double peak, bool signed)
    {
        if (bits is < 2 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(bits), "uencode uses between 2 and 32 bits.");
        }

        if (peak <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(peak), "uencode's peak is above zero.");
        }

        double signMax = System.Math.Pow(2.0, bits - 1) - 1;
        double signMin = -(1.0 + signMax);
        double q = System.Math.Pow(2.0, bits) - 1;
        double t = (q + 1) / (2 * peak);
        double scaled = (u + peak) * t;
        if (!signed)
        {
            scaled = System.Math.Min(q, System.Math.Max(0, scaled));
            return System.Math.Floor(scaled);
        }

        scaled += signMin;
        scaled = System.Math.Min(signMax, System.Math.Max(signMin, scaled));
        return System.Math.Floor(scaled);
    }

    /// <summary>Recovers a sample from its <paramref name="bits"/>-bit code.</summary>
    public static double Decode(double u, int bits, double peak, bool signed, bool saturate)
    {
        if (bits is < 2 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(bits), "udecode reads between 2 and 32 bits.");
        }

        if (peak <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(peak), "udecode's peak is above zero.");
        }

        double w = signed ? System.Math.Pow(2.0, bits - 1) : 0.0;
        double t = peak * System.Math.Pow(2.0, 1 - bits);
        double upper = signed ? System.Math.Pow(2.0, bits - 1) - 1 : System.Math.Pow(2.0, bits) - 1;
        double lower = signed ? -System.Math.Pow(2.0, bits - 1) : 0.0;
        double v = u;
        if (saturate)
        {
            v = System.Math.Min(upper, System.Math.Max(lower, v));
        }
        else
        {
            double span = System.Math.Pow(2.0, bits);
            v = signed
                ? Modulo(v - lower, span) + lower
                : Modulo(v, span);
        }

        return ((v + w) * t) - peak;
    }

    /// <summary>Marcum's generalised Q function of order <paramref name="m"/>.</summary>
    public static double MarcumQ(double a, double b, double m)
    {
        if (a < 0 || b < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(a), "marcumq's arguments are not negative.");
        }

        if (m < 1 || m != System.Math.Floor(m))
        {
            throw new ArgumentOutOfRangeException(nameof(m), "marcumq's order is a whole number of at least one.");
        }

        if (!double.IsPositiveInfinity(a) && b == 0)
        {
            return 1.0;
        }

        if (m == 1)
        {
            if (!double.IsPositiveInfinity(a) && double.IsPositiveInfinity(b))
            {
                return 0.0;
            }

            if (double.IsPositiveInfinity(a) && !double.IsPositiveInfinity(b))
            {
                return 1.0;
            }
        }

        double x = (a * a / 2.0) / m;
        double y = b * b / 2.0;
        return Probability(m, x, y);
    }

    /// <summary>The series itself, in whichever of its two forms converges from the right side.</summary>
    private static double Probability(double n, double x, double y)
    {
        double nx = n * x;
        double logCb;
        if (y == 0)
        {
            logCb = 0;
        }
        else
        {
            double lambda = 1.0 - (n / (2 * y)) - System.Math.Sqrt(((n / (2 * y)) * (n / (2 * y))) + (nx / y));
            logCb = (-lambda * y) + (nx * lambda / (1 - lambda)) - (n * System.Math.Log(1 - lambda));
        }

        if (System.Math.Exp(logCb) < RealMin)
        {
            return y <= nx + n ? 1.0 : 0.0;
        }

        // Below the mean the complement converges faster, and above it the function itself does.
        bool complement = y < nx + n;
        double outerScale = complement ? nx : y;
        double innerScale = complement ? y : nx;
        int startOuter;
        int startInner;
        double innerArg;
        double innerSum;
        if (complement)
        {
            startInner = Start(y, 0);
            startOuter = Start(nx, System.Math.Max(startInner - (int)n + 1, 0));
            int inner = (int)n - 1 + startOuter;
            (innerArg, innerSum) = Accumulate(innerScale, startInner, inner);
            double outerArg = Term(outerScale, startOuter);
            double outerSum = outerArg * (1 - innerSum);
            int m = inner;
            int k = startOuter;
            while (innerArg > Ulp(innerSum) && outerArg * (1 - innerSum) > RealMin)
            {
                m++;
                k++;
                innerArg = innerArg * innerScale / m;
                innerSum += innerArg;
                outerArg = outerArg * outerScale / k;
                outerSum += outerArg * (1 - innerSum);
            }

            return 1 - outerSum;
        }

        startInner = Start(nx, 0);
        startOuter = Start(y, startInner + (int)n);
        int outer = startOuter;
        int index = outer - (int)n;
        (innerArg, innerSum) = Accumulate(innerScale, startInner, index);
        double outerTerm = Term(outerScale, outer);
        double sum = outerTerm * (1 - innerSum);
        while (innerArg > Ulp(innerSum) && outerTerm * (1 - innerSum) > RealMin)
        {
            outer++;
            index++;
            innerArg = innerArg * innerScale / index;
            innerSum += innerArg;
            outerTerm = outerTerm * outerScale / outer;
            sum += outerTerm * (1 - innerSum);
        }

        // The inner sum has been running behind the outer one; whole millions of it are added back
        // in blocks, which is how MATLAB keeps a large order from costing a large loop.
        for (long block = 1; block <= (long)(n / 1e6); block++)
        {
            for (long i = (block - 1) * 1000000; i < block * 1000000; i++)
            {
                sum += Term(outerScale, i);
            }
        }

        for (long i = (long)(n / 1e6) * 1000000; i < (long)n; i++)
        {
            sum += Term(outerScale, i);
        }

        return sum;
    }

    /// <summary>The partial inner sum from <paramref name="from"/> up to <paramref name="to"/>.</summary>
    private static (double Last, double Sum) Accumulate(double scale, int from, int to)
    {
        if (from >= to)
        {
            double only = Term(scale, to);
            return (only, only);
        }

        double sum = 0;
        double last = 0;
        for (int i = from; i <= to; i++)
        {
            last = Term(scale, i);
            if (last > RealMin)
            {
                sum += last;
            }
        }

        return (last, sum);
    }

    /// <summary>Where a Poisson series may safely be started without losing anything that matters.</summary>
    private static int Start(double constant, int floorAt)
    {
        const double Epsilon = 1e-40;
        double g = -4 * System.Math.Log(4 * Epsilon * (1 - Epsilon)) / 5;
        if (Term(constant, floorAt) > RealMin || (2 * constant) - g < 0)
        {
            return floorAt;
        }

        return (int)System.Math.Floor(constant + 0.5 - System.Math.Sqrt(g * ((2 * constant) - g)));
    }

    /// <summary>One Poisson term, computed through a continued fraction when the mean is large.</summary>
    private static double Term(double y, long n)
    {
        if (y <= 0)
        {
            return n == 0 ? 1.0 : 0.0;
        }

        if (y > 1e4)
        {
            double z = n + 1;
            return System.Math.Exp(((z - 0.5) * (((1 - (y / z)) / (1 - (1 / (2 * z)))) + System.Math.Log(y / z)))
                - (0.5 * System.Math.Log(2 * System.Math.PI * y))
                - Correction(z));
        }

        return System.Math.Exp(-y + (n * System.Math.Log(y)) - SpecialFunctions.LogGamma(n + 1.0));
    }

    /// <summary>Stirling's correction, as a continued fraction.</summary>
    private static double Correction(double z) =>
        1.0 / ((12 * z) + (2.0 / ((5 * z) + (53.0 / ((42 * z) + (1170.0 / ((53 * z) + (53.0 / z))))))));

    /// <summary>The distance from <paramref name="x"/> to the next double, which MATLAB spells <c>eps</c>.</summary>
    private static double Ulp(double x)
    {
        double a = System.Math.Abs(x);
        if (a == 0)
        {
            return double.Epsilon;
        }

        long bits = BitConverter.DoubleToInt64Bits(a);
        return BitConverter.Int64BitsToDouble(bits + 1) - a;
    }

    /// <summary>MATLAB's <c>mod</c>, whose answer takes the sign of the divisor.</summary>
    private static double Modulo(double x, double y) =>
        y == 0 ? x : x - (System.Math.Floor(x / y) * y);

    /// <summary>The smallest positive normal double, which MATLAB calls <c>realmin</c>.</summary>
    private const double RealMin = 2.2250738585072014e-308;
}
