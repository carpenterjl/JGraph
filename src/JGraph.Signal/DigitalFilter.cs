using System.Numerics;

namespace JGraph.Signal;

/// <summary>
/// Digital-filter application and analysis in MATLAB's conventions: <see cref="Filter"/> is
/// <c>filter(b, a, x)</c> (Direct Form II transposed), <see cref="Freqz"/> is
/// <c>freqz(b, a, n, fs)</c> (the complex frequency response on a one-sided grid). Coefficients are
/// z-domain polynomials <c>b</c> (numerator) and <c>a</c> (denominator), normalized by <c>a[0]</c>.
/// </summary>
public static class DigitalFilter
{
    /// <summary>
    /// Applies the filter to <paramref name="x"/> (zero initial state) and returns an output of the
    /// same length. Direct Form II transposed: y = b0·x + z0; z_j = b_{j+1}·x + z_{j+1} − a_{j+1}·y.
    /// </summary>
    public static double[] Filter(ReadOnlySpan<double> b, ReadOnlySpan<double> a, ReadOnlySpan<double> x)
    {
        if (b.Length == 0 || a.Length == 0)
        {
            throw new ArgumentException("Filter coefficients must be non-empty.");
        }

        double a0 = a[0];
        if (a0 == 0)
        {
            throw new ArgumentException("The leading denominator coefficient a[0] must not be zero.");
        }

        int order = System.Math.Max(a.Length, b.Length);
        var bn = new double[order];
        var an = new double[order];
        for (int i = 0; i < b.Length; i++)
        {
            bn[i] = b[i] / a0;
        }

        for (int i = 0; i < a.Length; i++)
        {
            an[i] = a[i] / a0;
        }

        var state = new double[order - 1];
        var y = new double[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            double input = x[i];
            double output = (bn[0] * input) + (state.Length > 0 ? state[0] : 0);
            for (int j = 0; j < state.Length; j++)
            {
                double next = j + 1 < state.Length ? state[j + 1] : 0;
                state[j] = (bn[j + 1] * input) + next - (an[j + 1] * output);
            }

            y[i] = output;
        }

        return y;
    }

    /// <summary>
    /// The complex frequency response H(e^{jω}) = B/A evaluated at <paramref name="count"/> points on
    /// the one-sided grid ω_k = πk/count, plus the matching frequency axis f_k = k·fs/(2·count).
    /// </summary>
    public static (Complex[] Response, double[] Frequencies) Freqz(
        ReadOnlySpan<double> b, ReadOnlySpan<double> a, int count, double sampleRate)
    {
        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "freqz needs at least one frequency point.");
        }

        var response = new Complex[count];
        var frequencies = new double[count];
        double[] numerator = b.ToArray();
        double[] denominator = a.ToArray();
        for (int k = 0; k < count; k++)
        {
            double omega = System.Math.PI * k / count;
            var z = new Complex(System.Math.Cos(-omega), System.Math.Sin(-omega)); // e^{-jω}
            response[k] = EvaluatePolynomial(numerator, z) / EvaluatePolynomial(denominator, z);
            frequencies[k] = k * sampleRate / (2.0 * count);
        }

        return (response, frequencies);
    }

    /// <summary>Evaluates c0 + c1·z + c2·z² + … by Horner's method (coefficients in filter order).</summary>
    private static Complex EvaluatePolynomial(double[] coefficients, Complex z)
    {
        Complex sum = Complex.Zero;
        for (int i = coefficients.Length - 1; i >= 0; i--)
        {
            sum = (sum * z) + coefficients[i];
        }

        return sum;
    }
}
