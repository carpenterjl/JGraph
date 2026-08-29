namespace JGraph.Numerics;

/// <summary>
/// Resampling by the transform: a record of <c>m</c> samples read at <c>n</c> evenly spaced places
/// over the same period, which is what MATLAB's <c>interpft</c> does.
/// </summary>
/// <remarks>
/// <para>
/// The record is taken to be one period of a band-limited signal, so there is exactly one
/// trigonometric polynomial through it and the answer is that polynomial read somewhere else. That
/// makes this a different kind of interpolation from every other name in this folder: it is not
/// piecewise and it is not local, and a sample at one end moves the answer at the other.
/// </para>
/// <para>
/// The one delicate part is what to do with the highest frequency an even-length record carries.
/// That bin stands for a cosine at the Nyquist rate and says nothing about its phase, so it is
/// split in half between the positive and negative frequency — which is what makes the answer real
/// for a real record and what makes reading the record at its own places give the record back.
/// </para>
/// </remarks>
public static class FourierResampling
{
    /// <summary>
    /// Reads a record at as many evenly spaced places over the period it occupies as there is
    /// room for in the spans handed back.
    /// </summary>
    /// <param name="re">The real parts of the samples.</param>
    /// <param name="im">The imaginary parts, or an empty span for a real record.</param>
    /// <param name="outRe">Receives the real parts of the answer; its length is how many to read.</param>
    /// <param name="outIm">Receives the imaginary parts; the same length again.</param>
    public static void Resample(
        ReadOnlySpan<double> re, ReadOnlySpan<double> im, Span<double> outRe, Span<double> outIm)
    {
        int m = re.Length;
        int n = outRe.Length;
        outRe.Clear();
        outIm.Clear();
        if (m == 0 || n == 0)
        {
            return;
        }

        var spectrumRe = new double[m];
        var spectrumIm = new double[m];
        re.CopyTo(spectrumRe);
        if (!im.IsEmpty)
        {
            im.CopyTo(spectrumIm);
        }

        FftKernels.Transform(spectrumRe, spectrumIm, m, inverse: false);

        // Each bin of the record's spectrum is a frequency; where that frequency lands in a
        // spectrum of length n is the same question modulo n, which is what makes reading fewer
        // places than were recorded fold the high frequencies back rather than lose them.
        var foldedRe = new double[n];
        var foldedIm = new double[n];
        int nyquist = m % 2 == 0 ? m / 2 : -1;
        for (int j = 0; j < m; j++)
        {
            if (j == nyquist)
            {
                Place(foldedRe, foldedIm, n, j, spectrumRe[j] / 2, spectrumIm[j] / 2);
                Place(foldedRe, foldedIm, n, j - m, spectrumRe[j] / 2, spectrumIm[j] / 2);
                continue;
            }

            int frequency = j <= (m - 1) / 2 ? j : j - m;
            Place(foldedRe, foldedIm, n, frequency, spectrumRe[j], spectrumIm[j]);
        }

        FftKernels.Transform(foldedRe, foldedIm, n, inverse: true);

        double gain = (double)n / m;
        for (int k = 0; k < n; k++)
        {
            outRe[k] = foldedRe[k] * gain;
            outIm[k] = foldedIm[k] * gain;
        }
    }

    private static void Place(double[] re, double[] im, int n, int frequency, double real, double imaginary)
    {
        int at = ((frequency % n) + n) % n;
        re[at] += real;
        im[at] += imaginary;
    }
}
