using System.Numerics;

namespace JGraph.Signal;

/// <summary>The band shape of an IIR design (MATLAB's 'low' / 'high' / 'bandpass' / 'stop').</summary>
public enum FilterBandType
{
    LowPass,
    HighPass,
    BandPass,
    BandStop,
}

/// <summary>
/// Classic analog-prototype IIR design in MATLAB's conventions: <see cref="Butterworth"/> is
/// <c>butter(n, Wn, type)</c> with cutoffs normalized to the Nyquist frequency (1 = fs/2). The
/// pipeline is the textbook one — analog lowpass prototype poles, frequency pre-warp, band
/// transform in the s-plane, bilinear map to z, polynomial expansion, and gain normalization at the
/// band's reference frequency.
/// </summary>
public static class IirDesign
{
    /// <summary>
    /// Designs an order-<paramref name="order"/> Butterworth filter. <paramref name="cutoffs"/> holds
    /// one normalized cutoff for low/high-pass or two (low, high) for band-pass/stop, each in (0, 1)
    /// where 1 is Nyquist. Returns numerator <c>b</c> and denominator <c>a</c> (a[0] = 1). Band-pass
    /// and band-stop double the final order, as in MATLAB.
    /// </summary>
    public static (double[] B, double[] A) Butterworth(int order, ReadOnlySpan<double> cutoffs, FilterBandType type)
    {
        if (order < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(order), "The filter order must be at least 1.");
        }

        bool twoCutoffs = type is FilterBandType.BandPass or FilterBandType.BandStop;
        if (cutoffs.Length != (twoCutoffs ? 2 : 1))
        {
            throw new ArgumentException($"A {type} design needs {(twoCutoffs ? 2 : 1)} cutoff frequency(ies).");
        }

        foreach (double w in cutoffs)
        {
            if (w <= 0 || w >= 1)
            {
                throw new ArgumentException("Cutoff frequencies must lie strictly between 0 and 1 (1 = Nyquist).");
            }
        }

        if (twoCutoffs && cutoffs[1] <= cutoffs[0])
        {
            throw new ArgumentException("The upper cutoff must be greater than the lower cutoff.");
        }

        // 1. Analog lowpass prototype: n poles on the unit circle's left half.
        var poles = new List<Complex>(order);
        for (int k = 1; k <= order; k++)
        {
            double angle = System.Math.PI * ((2 * k) + order - 1) / (2 * order);
            poles.Add(new Complex(System.Math.Cos(angle), System.Math.Sin(angle)));
        }

        var zeros = new List<Complex>();

        // 2. Pre-warp the digital cutoffs (bilinear with T = 2).
        double W0 = System.Math.Tan(System.Math.PI * cutoffs[0] / 2);
        double W1 = twoCutoffs ? System.Math.Tan(System.Math.PI * cutoffs[1] / 2) : 0;

        // 3. Band transform in the s-plane.
        double referenceOmega; // digital frequency where |H| is normalized to 1
        switch (type)
        {
            case FilterBandType.LowPass:
                poles = poles.ConvertAll(p => p * W0);
                referenceOmega = 0;
                break;

            case FilterBandType.HighPass:
                poles = poles.ConvertAll(p => W0 / p);
                zeros.AddRange(Enumerable.Repeat(Complex.Zero, order));
                referenceOmega = System.Math.PI;
                break;

            case FilterBandType.BandPass:
            {
                double center = System.Math.Sqrt(W0 * W1);
                double bandwidth = W1 - W0;
                var transformed = new List<Complex>(2 * order);
                foreach (Complex p in poles)
                {
                    Complex half = p * bandwidth / 2;
                    Complex root = Complex.Sqrt((half * half) - (center * center));
                    transformed.Add(half + root);
                    transformed.Add(half - root);
                }

                poles = transformed;
                zeros.AddRange(Enumerable.Repeat(Complex.Zero, order));
                referenceOmega = 2 * System.Math.Atan(center);
                break;
            }

            default: // BandStop
            {
                double center = System.Math.Sqrt(W0 * W1);
                double bandwidth = W1 - W0;
                var transformed = new List<Complex>(2 * order);
                foreach (Complex p in poles)
                {
                    Complex half = bandwidth / (2 * p);
                    Complex root = Complex.Sqrt((half * half) - (center * center));
                    transformed.Add(half + root);
                    transformed.Add(half - root);
                }

                poles = transformed;
                for (int k = 0; k < order; k++)
                {
                    zeros.Add(new Complex(0, center));
                    zeros.Add(new Complex(0, -center));
                }

                referenceOmega = 0;
                break;
            }
        }

        // 4. Bilinear transform z = (1 + s)/(1 − s); analog zeros at infinity land on z = −1.
        var zPoles = poles.ConvertAll(static s => (1 + s) / (1 - s));
        var zZeros = zeros.ConvertAll(static s => (1 + s) / (1 - s));
        while (zZeros.Count < zPoles.Count)
        {
            zZeros.Add(-Complex.One);
        }

        // 5. Expand to real polynomials and normalize the gain at the reference frequency.
        double[] b = RealPolynomial(zZeros);
        double[] a = RealPolynomial(zPoles);
        var zRef = new Complex(System.Math.Cos(referenceOmega), System.Math.Sin(referenceOmega));
        double gain = Complex.Abs(Evaluate(a, zRef)) / Complex.Abs(Evaluate(b, zRef));
        for (int i = 0; i < b.Length; i++)
        {
            b[i] *= gain;
        }

        return (b, a);
    }

    /// <summary>Expands Π(z − rᵢ) into real coefficients, highest power first (roots are conjugate-paired).</summary>
    private static double[] RealPolynomial(List<Complex> roots)
    {
        var coefficients = new Complex[roots.Count + 1];
        coefficients[0] = Complex.One;
        for (int r = 0; r < roots.Count; r++)
        {
            for (int i = r + 1; i >= 1; i--)
            {
                coefficients[i] -= roots[r] * coefficients[i - 1];
            }
        }

        var real = new double[coefficients.Length];
        for (int i = 0; i < real.Length; i++)
        {
            real[i] = coefficients[i].Real; // imaginary parts cancel for conjugate-paired roots
        }

        return real;
    }

    /// <summary>Evaluates c0·z^m + c1·z^{m-1} + … + cm at z (coefficients highest power first).</summary>
    private static Complex Evaluate(double[] coefficients, Complex z)
    {
        Complex sum = Complex.Zero;
        foreach (double c in coefficients)
        {
            sum = (sum * z) + c;
        }

        return sum;
    }
}
