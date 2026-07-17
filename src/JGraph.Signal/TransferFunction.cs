using System.Numerics;

namespace JGraph.Signal;

/// <summary>The frequency response of a system sampled over a set of angular frequencies.</summary>
/// <param name="Omega">The angular frequencies (rad/s) at which the response was evaluated.</param>
/// <param name="MagnitudeDb">The magnitude at each frequency in decibels (20·log10|H|).</param>
/// <param name="PhaseDegrees">The phase at each frequency in degrees, unwrapped for continuity.</param>
public sealed record FrequencyResponse(double[] Omega, double[] MagnitudeDb, double[] PhaseDegrees);

/// <summary>
/// A continuous-time linear transfer function H(s) = N(s)/D(s), with numerator and denominator
/// coefficients given in descending powers of s (MATLAB <c>tf(num, den)</c> convention). It evaluates
/// the complex response H(jω), which drives Bode and Nyquist plots.
/// </summary>
public sealed class TransferFunction
{
    private readonly double[] _numerator;
    private readonly double[] _denominator;

    /// <summary>Creates a transfer function from numerator/denominator coefficients (descending powers of s).</summary>
    public TransferFunction(double[] numerator, double[] denominator)
    {
        ArgumentNullException.ThrowIfNull(numerator);
        ArgumentNullException.ThrowIfNull(denominator);
        if (numerator.Length == 0 || denominator.Length == 0)
        {
            throw new ArgumentException("Numerator and denominator must each have at least one coefficient.");
        }

        if (Array.TrueForAll(denominator, c => c == 0))
        {
            throw new ArgumentException("The denominator cannot be all zeros.", nameof(denominator));
        }

        _numerator = (double[])numerator.Clone();
        _denominator = (double[])denominator.Clone();
    }

    /// <summary>The numerator coefficients in descending powers of s.</summary>
    public IReadOnlyList<double> Numerator => _numerator;

    /// <summary>The denominator coefficients in descending powers of s.</summary>
    public IReadOnlyList<double> Denominator => _denominator;

    /// <summary>Evaluates H(jω) at the given angular frequency (rad/s).</summary>
    public Complex Evaluate(double omega)
    {
        var s = new Complex(0, omega);
        Complex num = EvaluatePolynomial(_numerator, s);
        Complex den = EvaluatePolynomial(_denominator, s);
        return num / den;
    }

    /// <summary>Evaluates the response over a set of angular frequencies, returning magnitude (dB) and unwrapped phase (deg).</summary>
    public FrequencyResponse Response(IReadOnlyList<double> omega)
    {
        ArgumentNullException.ThrowIfNull(omega);
        int n = omega.Count;
        var w = new double[n];
        var mag = new double[n];
        var phase = new double[n];
        for (int i = 0; i < n; i++)
        {
            Complex h = Evaluate(omega[i]);
            w[i] = omega[i];
            mag[i] = 20.0 * System.Math.Log10(System.Math.Max(h.Magnitude, 1e-12));
            phase[i] = h.Phase * 180.0 / System.Math.PI;
        }

        Unwrap(phase);
        return new FrequencyResponse(w, mag, phase);
    }

    /// <summary>
    /// Builds <paramref name="count"/> logarithmically spaced values from <paramref name="min"/> to
    /// <paramref name="max"/> (both positive), suitable for a Bode/Nyquist frequency sweep.
    /// </summary>
    public static double[] LogSpace(double min, double max, int count)
    {
        if (min <= 0 || max <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(min), "Log-spaced endpoints must be positive.");
        }

        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Count must be positive.");
        }

        var result = new double[count];
        if (count == 1)
        {
            result[0] = min;
            return result;
        }

        double logMin = System.Math.Log10(min);
        double logMax = System.Math.Log10(max);
        double step = (logMax - logMin) / (count - 1);
        for (int i = 0; i < count; i++)
        {
            result[i] = System.Math.Pow(10, logMin + (i * step));
        }

        return result;
    }

    private static Complex EvaluatePolynomial(double[] coefficients, Complex s)
    {
        Complex acc = Complex.Zero;
        foreach (double c in coefficients)
        {
            acc = (acc * s) + c;
        }

        return acc;
    }

    /// <summary>Removes 360° jumps from a phase sequence so it varies continuously.</summary>
    private static void Unwrap(double[] phaseDegrees)
    {
        for (int i = 1; i < phaseDegrees.Length; i++)
        {
            double delta = phaseDegrees[i] - phaseDegrees[i - 1];
            while (delta > 180.0)
            {
                phaseDegrees[i] -= 360.0;
                delta = phaseDegrees[i] - phaseDegrees[i - 1];
            }

            while (delta < -180.0)
            {
                phaseDegrees[i] += 360.0;
                delta = phaseDegrees[i] - phaseDegrees[i - 1];
            }
        }
    }
}
