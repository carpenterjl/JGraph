using System.Numerics;
using JGraph.Numerics;
using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Signal;

/// <summary>
/// The subspace estimates: MUSIC and the eigenvector method, which split a correlation estimate
/// into a signal part and a noise part and read the frequencies off the noise part.
/// </summary>
/// <remarks>
/// Both are MATLAB's <c>music</c> with a flag between them. The split comes from a singular value
/// decomposition of a data matrix rather than from an eigendecomposition of a correlation matrix,
/// which matters: singular values come out in a stated order, and the pseudospectrum only ever uses
/// the projection onto the noise subspace, so nothing here depends on which basis a decomposition
/// happened to choose for that subspace.
/// </remarks>
public static class SubspaceSpectra
{
    /// <summary>The split of a signal into its signal and noise subspaces.</summary>
    public sealed class Subspaces
    {
        /// <summary>The vectors spanning the signal subspace, one a column.</summary>
        public Complex[][] Signal { get; init; } = [];

        /// <summary>The vectors spanning the noise subspace, one a column.</summary>
        public Complex[][] Noise { get; init; } = [];

        /// <summary>The eigenvalues, largest first.</summary>
        public double[] Eigenvalues { get; init; } = [];

        /// <summary>How many dimensions the signal subspace was given.</summary>
        public int SignalDimension { get; init; }
    }

    /// <summary>
    /// The subspace split MATLAB's <c>music</c> computes: from a correlation matrix when told the
    /// input is one, and otherwise from the covariance data matrix or the buffered segments.
    /// </summary>
    public static Subspaces Split(
        Complex[][] columns,
        int dimension,
        double? threshold,
        bool correlationInput,
        int? segmentLength,
        int? overlap,
        double[]? window)
    {
        ArgumentNullException.ThrowIfNull(columns);
        double[] eigenvalues;
        Complex[][] vectors;
        if (correlationInput && columns.Length > 1)
        {
            int n = columns.Length;
            var hermitian = new Complex[n, n];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    hermitian[i, j] = (columns[j][i] + Complex.Conjugate(columns[i][j])) / 2;
                }
            }

            (Complex[] values, Complex[,] basis) = ComplexEigen.Factor(hermitian);
            var order = new int[n];
            for (int i = 0; i < n; i++)
            {
                order[i] = i;
            }

            Array.Sort(order, (a, b) => values[b].Real.CompareTo(values[a].Real));
            eigenvalues = new double[n];
            vectors = new Complex[n][];
            for (int i = 0; i < n; i++)
            {
                eigenvalues[i] = values[order[i]].Real;
                vectors[i] = new Complex[n];
                for (int r = 0; r < n; r++)
                {
                    vectors[i][r] = basis[r, order[i]];
                }
            }
        }
        else
        {
            Complex[,] data = DataMatrix(columns, dimension, segmentLength, overlap, window);
            (_, double[] singular, Complex[,] right) = ComplexEigen.Svd(data, economy: true);
            int count = right.GetLength(1);
            eigenvalues = new double[count];
            vectors = new Complex[count][];
            int rows = right.GetLength(0);
            for (int i = 0; i < count; i++)
            {
                eigenvalues[i] = i < singular.Length ? singular[i] * singular[i] : 0;
                vectors[i] = new Complex[rows];
                for (int r = 0; r < rows; r++)
                {
                    vectors[i][r] = right[r, i];
                }
            }
        }

        int effective = dimension;
        if (threshold is double factor && eigenvalues.Length > 0)
        {
            double bound = factor * eigenvalues[^1];
            int above = 0;
            foreach (double value in eigenvalues)
            {
                if (value > bound)
                {
                    above++;
                }
            }

            if (above > 0)
            {
                effective = System.Math.Min(dimension, above);
            }
        }

        effective = System.Math.Clamp(effective, 0, vectors.Length);
        return new Subspaces
        {
            Signal = vectors[..effective],
            Noise = vectors[effective..],
            Eigenvalues = eigenvalues,
            SignalDimension = effective,
        };
    }

    /// <summary>The data matrix the split is taken of: a covariance matrix, or the buffered segments.</summary>
    private static Complex[,] DataMatrix(
        Complex[][] columns, int dimension, int? segmentLength, int? overlap, double[]? window)
    {
        Complex[,] data;
        if (columns.Length > 1)
        {
            int rows = columns[0].Length;
            data = new Complex[rows, columns.Length];
            for (int c = 0; c < columns.Length; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    data[r, c] = columns[c][r];
                }
            }
        }
        else if (segmentLength is int segment)
        {
            Complex[] x = columns[0];
            int advance = segment - (overlap ?? 0);
            int count = (x.Length - (overlap ?? 0)) / advance;
            double scale = System.Math.Sqrt(x.Length - segment);
            data = new Complex[count, segment];
            for (int i = 0; i < count; i++)
            {
                for (int j = 0; j < segment; j++)
                {
                    data[i, j] = x[(i * advance) + j] / scale;
                }
            }
        }
        else
        {
            data = AutoRegressiveModels.CorrelationMatrix(
                columns[0], (2 * dimension) - 1, CorrelationShape.Covariance);
        }

        if (window is null)
        {
            return data;
        }

        int height = data.GetLength(0);
        int width = data.GetLength(1);
        if (window.Length != width)
        {
            throw new ArgumentException("music needs a window as long as a row of its data matrix.", nameof(window));
        }

        for (int i = 0; i < height; i++)
        {
            for (int j = 0; j < width; j++)
            {
                data[i, j] *= window[j];
            }
        }

        return data;
    }

    /// <summary>
    /// The pseudospectrum over the whole circle: the reciprocal of the signal's projection onto the
    /// noise subspace, weighted by the eigenvalues when the eigenvector method is asked for.
    /// </summary>
    public static (double[] Values, double[] Frequencies) Pseudospectrum(
        Subspaces subspaces, int nfft, double? sampleRate, bool eigenvector)
    {
        bool useSignal = !eigenvector && subspaces.Signal.Length <= subspaces.Noise.Length;
        Complex[][] basis = useSignal ? subspaces.Signal : subspaces.Noise;
        double[] denominator = new double[nfft];
        double[] frequencies = FilterAnalysis.UniformGrid(
            nfft, whole: true, sampleRate ?? (2 * System.Math.PI));
        for (int n = 0; n < basis.Length; n++)
        {
            Complex[] response = ResponseOf(basis[n], nfft);
            double weight = 1;
            if (eigenvector)
            {
                weight = subspaces.Eigenvalues[subspaces.Eigenvalues.Length - basis.Length + n];
            }

            for (int i = 0; i < nfft; i++)
            {
                double magnitude = response[i].Magnitude;
                denominator[i] += magnitude * magnitude / weight;
            }
        }

        if (useSignal && !eigenvector)
        {
            int rows = basis.Length == 0 ? 0 : basis[0].Length;
            for (int i = 0; i < nfft; i++)
            {
                denominator[i] = System.Math.Abs(rows - denominator[i]);
            }
        }

        var values = new double[nfft];
        for (int i = 0; i < nfft; i++)
        {
            values[i] = 1 / denominator[i];
        }

        return (values, frequencies);
    }

    /// <summary>The transform of a complex coefficient vector over the whole circle.</summary>
    private static Complex[] ResponseOf(Complex[] coefficients, int nfft)
    {
        var padded = new Complex[nfft];
        for (int i = 0; i < coefficients.Length; i++)
        {
            padded[i % nfft] += coefficients[i];
        }

        Fft.Transform(padded, inverse: false);
        return padded;
    }

    /// <summary>
    /// The frequencies MUSIC finds by rooting rather than by searching: the roots of the noise
    /// subspace's summed autocorrelation, taken in order of how near the unit circle they lie.
    /// </summary>
    public static (double[] Frequencies, bool Found) RootFrequencies(Subspaces subspaces, bool eigenvector)
    {
        int count = subspaces.Noise.Length;
        if (count == 0 || subspaces.Noise[0].Length == 0)
        {
            return (new double[subspaces.SignalDimension], false);
        }

        int length = subspaces.Noise[0].Length;
        var d = new Complex[(2 * length) - 1];
        for (int n = 0; n < count; n++)
        {
            Complex[] v = subspaces.Noise[n];
            double weight = eigenvector ? subspaces.Eigenvalues[subspaces.Eigenvalues.Length - count + n] : 1;
            for (int i = 0; i < length; i++)
            {
                for (int j = 0; j < length; j++)
                {
                    d[i + j] += v[i] * Complex.Conjugate(v[length - 1 - j]) / weight;
                }
            }
        }

        Complex[] roots = Polynomials.Roots(d);
        List<Complex> inside = [];
        foreach (Complex r in roots)
        {
            if (r.Magnitude > 1)
            {
                continue;
            }

            bool seen = false;
            foreach (Complex other in inside)
            {
                if (other == r)
                {
                    seen = true;
                    break;
                }
            }

            if (!seen)
            {
                inside.Add(r);
            }
        }

        if (inside.Count == 0)
        {
            var missing = new double[subspaces.SignalDimension];
            Array.Fill(missing, double.NaN);
            return (missing, false);
        }

        var sorted = inside.ToArray();
        var keys = new double[sorted.Length];
        for (int i = 0; i < sorted.Length; i++)
        {
            keys[i] = System.Math.Abs(sorted[i].Magnitude - 1);
        }

        var order = new int[sorted.Length];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        // A stable sort: two roots the same distance from the circle keep the order the root
        // finder gave them, which is the only place this estimate can depend on it.
        Array.Sort(keys, order);
        int wanted = System.Math.Min(subspaces.SignalDimension, order.Length);
        var frequencies = new double[subspaces.SignalDimension];
        Array.Fill(frequencies, double.NaN);
        for (int i = 0; i < wanted; i++)
        {
            frequencies[i] = sorted[order[i]].Phase;
        }

        return (frequencies, true);
    }
}
