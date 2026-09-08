namespace JGraph.Signal;

/// <summary>
/// The Lomb–Scargle periodogram: a spectrum of a signal that was not sampled evenly, or that has
/// gaps in it, fitted one frequency at a time by least squares rather than transformed.
/// </summary>
/// <remarks>
/// Two roads, both MATLAB's. The direct one evaluates the fit at every frequency asked for and costs
/// a pass over the signal for each. The fast one — Press and Rybicki's <c>fasper</c> — spreads each
/// sample onto an oversampled grid with a Lagrange kernel and takes one transform of that, which is
/// what makes the estimate affordable at the default resolution.
/// </remarks>
public static class LombScargle
{
    /// <summary>How the periodogram is scaled once it is computed.</summary>
    public enum Scaling
    {
        /// <summary>Power per unit frequency, which is the default.</summary>
        Psd,

        /// <summary>Power, so a sinusoid reads its own amplitude squared over two.</summary>
        Power,

        /// <summary>Scaled by twice the signal's variance, which is the classical normalisation.</summary>
        Normalized,
    }

    /// <summary>The periodogram evaluated directly at each frequency in <paramref name="f"/>.</summary>
    public static double[] Direct(double[] x, double[] f, double[] t)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(f);
        ArgumentNullException.ThrowIfNull(t);
        var p = new double[f.Length];
        const double epsilon = 2.220446049250313e-16;
        for (int i = 0; i < f.Length; i++)
        {
            double sumSin2 = 0;
            double sumCos2 = 0;
            var sin = new double[t.Length];
            var cos = new double[t.Length];
            for (int j = 0; j < t.Length; j++)
            {
                double angle = 2 * f[i] * t[j];
                sin[j] = SinPi(angle);
                cos[j] = CosPi(angle);
            }

            for (int j = 0; j < t.Length; j++)
            {
                sumSin2 += 2 * cos[j] * sin[j];
                sumCos2 += (cos[j] - sin[j]) * (cos[j] + sin[j]);
            }

            double tau = 0.5 * System.Math.Atan2(sumSin2, sumCos2);
            double sinTau = System.Math.Sin(tau);
            double cosTau = System.Math.Cos(tau);
            double sinNorm = 0;
            double cosNorm = 0;
            double sinTerm = 0;
            double cosTerm = 0;
            for (int j = 0; j < t.Length; j++)
            {
                double s = (sin[j] * cosTau) - (cos[j] * sinTau);
                double c = (cos[j] * cosTau) + (sin[j] * sinTau);
                sinNorm += s * s;
                cosNorm += c * c;
                sinTerm += x[j] * s;
                cosTerm += x[j] * c;
            }

            if (System.Math.Abs(sinNorm) < epsilon)
            {
                sinNorm += epsilon;
            }

            if (System.Math.Abs(cosNorm) < epsilon)
            {
                cosNorm += epsilon;
            }

            p[i] = (cosTerm * cosTerm / cosNorm) + (sinTerm * sinTerm / sinNorm);
        }

        return p;
    }

    /// <summary>
    /// Press and Rybicki's fast periodogram: each sample is spread onto an oversampled grid with a
    /// Lagrange kernel, and one transform of that grid gives every frequency at once.
    /// </summary>
    public static (double[] Values, double[] Frequencies) Fast(
        double[] x, double[] t, double oversample, double? maximum)
    {
        int n = t.Length;
        double first = t[0];
        double span = t[n - 1] - first;
        double step = span / (n - 1);
        double highest = 1;
        if (maximum is double fmax)
        {
            int outputs = (int)System.Math.Round(0.5 * oversample * n, MidpointRounding.AwayFromZero);
            double top = outputs / (n * step * oversample);
            highest = fmax / top;
        }

        const int accuracy = 4;
        int frequencies = Fft.NextPowerOfTwo((int)System.Math.Ceiling(oversample * highest * n * accuracy));
        int dimension = 2 * frequencies;
        var work1 = new double[dimension];
        var work2 = new double[dimension];
        double factor = dimension / (n * step * oversample);
        double denominator = 6;
        for (int j = 0; j < n; j++)
        {
            double position = 1 + Modulo((t[j] - first) * factor, dimension);
            double doubled = 1 + Modulo(2 * (position - 1), dimension);
            Spread(x[j], work1, dimension, position, accuracy, denominator);
            Spread(1, work2, dimension, doubled, accuracy, denominator);
        }

        int count = (int)System.Math.Round(0.5 * oversample * highest * n, MidpointRounding.AwayFromZero);
        var grid = new double[count];
        for (int i = 0; i < count; i++)
        {
            grid[i] = (i + 1) / (n * step * oversample);
        }

        System.Numerics.Complex[] w1 = Fft.Forward(work1);
        System.Numerics.Complex[] w2 = Fft.Forward(work2);
        var values = new double[count];
        bool degenerate = false;
        for (int i = 0; i < count; i++)
        {
            if (w2[i + 1].Magnitude == 0)
            {
                degenerate = true;
                break;
            }
        }

        if (degenerate)
        {
            return (Direct(x, grid, t), grid);
        }

        for (int i = 0; i < count; i++)
        {
            double realOne = w1[i + 1].Real;
            double imagOne = w1[i + 1].Imaginary;
            double realTwo = w2[i + 1].Real;
            double imagTwo = w2[i + 1].Imaginary;
            double hypotenuse = w2[i + 1].Magnitude;
            double halfCos = 0.5 * realTwo / hypotenuse;
            double halfSin = 0.5 * imagTwo / hypotenuse;
            double cosWeight = System.Math.Sqrt(0.5 + halfCos);
            double sinWeight = System.Math.Sign(halfSin) * System.Math.Sqrt(0.5 - halfCos);
            double den = (0.5 * n) + (halfCos * realTwo) + (halfSin * imagTwo);
            double cosTerm = (cosWeight * realOne) + (sinWeight * imagOne);
            double sinTerm = (cosWeight * imagOne) - (sinWeight * realOne);
            values[i] = (cosTerm * cosTerm / den) + (sinTerm * sinTerm / (n - den));
        }

        return (values, grid);
    }

    /// <summary>
    /// Numerical Recipes' <c>spread</c>: adds a sample at a fractional position by the Lagrange
    /// weights of the <paramref name="m"/> grid points around it.
    /// </summary>
    private static void Spread(double y, double[] into, int n, double x, int m, double denominator)
    {
        double running = denominator;
        if (x == System.Math.Round(x))
        {
            into[(int)x - 1] += y;
            return;
        }

        int low = System.Math.Min(
            System.Math.Max((int)System.Math.Floor(x - (0.5 * m) + 1), 1), n - m + 1);
        int high = low + m - 1;
        double product = x - low;
        for (int j = low + 1; j <= high; j++)
        {
            product *= x - j;
        }

        into[high - 1] += y * product / (running * (x - high));
        for (int j = high - 1; j >= low; j--)
        {
            running = running / (j + 1 - low) * (j - high);
            into[j - 1] += y * product / (running * (x - j));
        }
    }

    private static double Modulo(double x, double y) => x - (y * System.Math.Floor(x / y));

    /// <summary>The sine of π times its argument, computed without ever forming π times it.</summary>
    private static double SinPi(double x)
    {
        if (double.IsNaN(x) || double.IsInfinity(x))
        {
            return double.NaN;
        }

        double reduced = x % 2;
        if (reduced < 0)
        {
            reduced += 2;
        }

        return reduced switch
        {
            0 => 0,
            0.5 => 1,
            1 => 0,
            1.5 => -1,
            _ => System.Math.Sin(reduced * System.Math.PI),
        };
    }

    /// <summary>The cosine of π times its argument.</summary>
    private static double CosPi(double x)
    {
        if (double.IsNaN(x) || double.IsInfinity(x))
        {
            return double.NaN;
        }

        double reduced = x % 2;
        if (reduced < 0)
        {
            reduced += 2;
        }

        return reduced switch
        {
            0 => 1,
            0.5 => 0,
            1 => -1,
            1.5 => 0,
            _ => System.Math.Cos(reduced * System.Math.PI),
        };
    }
}
