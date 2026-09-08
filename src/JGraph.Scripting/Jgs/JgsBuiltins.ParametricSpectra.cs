using System.Numerics;
using JGraph.Signal;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The parametric spectral estimates (M136): the four that fit an autoregressive model and read its
/// response, the two subspace estimates and their rooted forms, the correlation matrix they are all
/// built on, and the maximum-entropy estimate.
/// </summary>
/// <remarks>
/// The four AR names are one function with four fits behind it, which is how MATLAB writes them: a
/// model, its residual variance, the response of one over its polynomial, and the same fold onto one
/// side that every other spectral name uses. The subspace pair are one function with a flag. What
/// differs between them all is only how the model is fitted.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the parametric spectral names.</summary>
    internal static void RegisterParametricSpectralBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.DeclareFunction(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        void Estimate(string name, Func<string, IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> body) =>
            Define(name,
                (args, line, col) => body(name, args, 1, line, col)[0],
                (args, wanted, line, col) => body(name, args, wanted, line, col));

        Estimate("pburg", AutoRegressiveSpectrum);
        Estimate("pcov", AutoRegressiveSpectrum);
        Estimate("pmcov", AutoRegressiveSpectrum);
        Estimate("pyulear", AutoRegressiveSpectrum);
        Estimate("pmusic", SubspaceSpectrum);
        Estimate("peig", SubspaceSpectrum);
        Estimate("rootmusic", RootedSubspace);
        Estimate("rooteig", RootedSubspace);
        Estimate("corrmtx", CorrelationMatrixOf);
        Estimate("pmem", MaximumEntropy);
    }

    // --- The four autoregressive estimates ----------------------------------------------------------

    /// <summary><c>pburg</c>, <c>pcov</c>, <c>pmcov</c> and <c>pyulear</c>.</summary>
    private static JgsValue[] AutoRegressiveSpectrum(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 2, 8, line, col);
        (Complex[][] channels, bool real, bool wasVector) = SpectralChannels(name, args[0], line, col);
        int order = (int)Num(name, args, 1, line, col);
        if (order < 0)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a model order at or above zero.");
        }

        SpectralWords words = ReadSpectralWords(name, args, 2, line, col);
        if (words.Reassign || words.Trace != SpectralTrace.Mean)
        {
            throw new JgsRuntimeException(line, col, $"{name} has no trace and no reassignment.");
        }

        SpectralRequest request = SpectralRequestOf(name, words, real, 256, line, col);
        bool scalarNfft = words.Frequencies is null;
        int nfft = scalarNfft ? request.Nfft : words.Frequencies!.Length;
        double fs = request.SampleRate;

        var values = new Complex[channels.Length][];
        double[] frequencies = [];
        for (int c = 0; c < channels.Length; c++)
        {
            AutoRegressiveModels.Model model = name switch
            {
                "pburg" => AutoRegressiveModels.Burg(channels[c], order),
                "pyulear" => AutoRegressiveModels.YuleWalker(channels[c], order),
                "pcov" => AutoRegressiveModels.ParametricFit(channels[c], order, CorrelationShape.Covariance),
                _ => AutoRegressiveModels.ParametricFit(channels[c], order, CorrelationShape.Modified),
            };

            if (model.Variance < 0)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs a model with a positive variance.");
            }

            (Complex[] response, double[] grid) = InverseResponse(model.A, nfft, words.Frequencies, fs);
            var raw = new Complex[response.Length];
            for (int i = 0; i < response.Length; i++)
            {
                double magnitude = response[i].Magnitude;
                raw[i] = model.Variance * magnitude * magnitude;
            }

            (Complex[] pxx, double[] w) = SpectralEstimation.ToPsd(
                raw, grid, request.Range, scalarNfft, fs, SpectralScaling.Psd);
            values[c] = pxx;
            frequencies = w;
        }

        var answer = new SpectralAnswer { Values = values, Frequencies = frequencies };
        if (words.ConfidenceLevel is double level || wanted > 2)
        {
            level = words.ConfidenceLevel ?? SpectralEstimation.DefaultConfidence;
            AutoRegressiveInterval(answer, level, order, channels[0].Length, line, col);
        }

        if (request.CenterDc && scalarNfft)
        {
            CenterParametric(answer, request.Range, nfft, fs);
        }

        return PackEstimate(answer, request, wanted, wasVector, !scalarNfft);
    }

    /// <summary>
    /// MATLAB's <c>arconfinterval</c>: a normal interval whose width comes from the model order and
    /// the signal's length rather than from a chi-squared count of segments.
    /// </summary>
    private static void AutoRegressiveInterval(
        SpectralAnswer answer, double level, int order, int length, int line, int col)
    {
        double normal = System.Math.Sqrt(2) * JGraph.Numerics.SpecialFunctions.ErfcInverse(2 * (1 - ((1 - level) / 2)));
        normal = -normal;
        if (order <= 0 || length / (2.0 * order) <= normal * normal)
        {
            return;
        }

        double beta = System.Math.Sqrt(2.0 * order / length) * normal;
        var lower = new double[answer.Values.Length][];
        var upper = new double[answer.Values.Length][];
        for (int c = 0; c < answer.Values.Length; c++)
        {
            lower[c] = new double[answer.Values[c].Length];
            upper[c] = new double[answer.Values[c].Length];
            for (int i = 0; i < lower[c].Length; i++)
            {
                lower[c][i] = answer.Values[c][i].Real * (1 - beta);
                upper[c][i] = answer.Values[c][i].Real * (1 + beta);
            }
        }

        answer.ConfidenceLower = lower;
        answer.ConfidenceUpper = upper;
    }

    /// <summary>Moves an already-built parametric answer so that zero frequency sits in the middle.</summary>
    private static void CenterParametric(SpectralAnswer answer, SpectralRange range, int nfft, double fs)
    {
        double[] original = answer.Frequencies;
        double[] moved = original;
        for (int c = 0; c < answer.Values.Length; c++)
        {
            (Complex[] v, double[] f) = SpectralEstimation.CenterDc(
                answer.Values[c], original, range, nfft, fs, scaled: true);
            answer.Values[c] = v;
            moved = f;
        }

        if (answer.ConfidenceLower is not null && answer.ConfidenceUpper is not null)
        {
            for (int c = 0; c < answer.ConfidenceLower.Length; c++)
            {
                answer.ConfidenceLower[c] = SpectralEstimators.RealPart(SpectralEstimation.CenterDc(
                    ToComplex(answer.ConfidenceLower[c]), original, range, nfft, fs, scaled: true).Values);
                answer.ConfidenceUpper[c] = SpectralEstimators.RealPart(SpectralEstimation.CenterDc(
                    ToComplex(answer.ConfidenceUpper[c]), original, range, nfft, fs, scaled: true).Values);
            }
        }

        answer.Frequencies = moved;
    }

    private static Complex[] ToComplex(double[] values)
    {
        var boxed = new Complex[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            boxed[i] = values[i];
        }

        return boxed;
    }

    /// <summary>The response of one over a model polynomial, over the whole circle or at named points.</summary>
    private static (Complex[] Response, double[] Frequencies) InverseResponse(
        Complex[] a, int nfft, double[]? frequencies, double fs)
    {
        if (frequencies is not null)
        {
            var at = new Complex[frequencies.Length];
            for (int i = 0; i < frequencies.Length; i++)
            {
                Complex z = Complex.Exp(new Complex(0, -2 * System.Math.PI * frequencies[i] / fs));
                Complex sum = 0;
                for (int k = a.Length - 1; k >= 0; k--)
                {
                    sum = (sum * z) + a[k];
                }

                // Horner ran from the top, so the sum is the polynomial in z read backwards.
                Complex value = 0;
                for (int k = 0; k < a.Length; k++)
                {
                    value += a[k] * Complex.Pow(z, k);
                }

                at[i] = 1 / value;
            }

            return (at, frequencies);
        }

        var padded = new Complex[nfft];
        for (int i = 0; i < a.Length; i++)
        {
            padded[i % nfft] += a[i];
        }

        JGraph.Signal.Fft.Transform(padded, inverse: false);
        var response = new Complex[nfft];
        for (int i = 0; i < nfft; i++)
        {
            response[i] = 1 / padded[i];
        }

        return (response, FilterAnalysis.UniformGrid(nfft, whole: true, fs));
    }

    // --- The subspace estimates ---------------------------------------------------------------------

    /// <summary>What <c>pmusic</c> reads out of its arguments beyond the shared words.</summary>
    private sealed record SubspaceWords(
        int Nfft, double[]? Frequencies, double SampleRate, bool SampleRateGiven, int? SegmentLength,
        int? Overlap, bool Correlation, bool Eigenvector, SpectralRange Range, bool CenterDc, double[]? Window);

    /// <summary>Reads <c>pmusic</c>'s arguments: four numbers in order, then the words.</summary>
    private static SubspaceWords ReadSubspaceWords(
        string name, IReadOnlyList<JgsValue> args, int start, bool real, bool eigenvector, int line, int col)
    {
        int nfft = 256;
        double[]? frequencies = null;
        double? fs = null;
        int? nw = null;
        int? overlap = null;
        double[]? window = null;
        bool correlation = false;
        bool ev = eigenvector;
        bool oneSided = false;
        bool twoSided = false;
        bool centered = false;
        int numbers = 0;
        for (int i = start; i < args.Count; i++)
        {
            if (IsTextScalar(args[i]))
            {
                string word = StrOf(name, args[i], line, col).ToLowerInvariant();
                if ("corr".StartsWith(word, StringComparison.Ordinal) && word.Length > 0)
                {
                    correlation = true;
                }
                else if (word == "ev")
                {
                    ev = true;
                }
                else if (word is "onesided" or "half" || "onesided".StartsWith(word, StringComparison.Ordinal))
                {
                    oneSided = true;
                }
                else if (word is "twosided" or "whole" || "twosided".StartsWith(word, StringComparison.Ordinal))
                {
                    twoSided = true;
                }
                else if ("centered".StartsWith(word, StringComparison.Ordinal))
                {
                    centered = true;
                }
                else if (word == "whole")
                {
                    twoSided = true;
                }
                else if (word == "half")
                {
                    oneSided = true;
                }
                else
                {
                    throw new JgsRuntimeException(line, col, $"{name} does not know the option '{word}'.");
                }

                continue;
            }

            numbers++;
            if (ElementCount(args[i]) == 0)
            {
                continue;
            }

            double[] given = ToDoubles(name, args[i], line, col);
            switch (numbers)
            {
                case 1:
                    if (given.Length == 1)
                    {
                        nfft = (int)given[0];
                    }
                    else
                    {
                        frequencies = given;
                    }

                    break;
                case 2:
                    fs = given[0];
                    break;
                case 3:
                    if (given.Length > 1)
                    {
                        window = given;
                        nw = given.Length;
                    }
                    else
                    {
                        nw = (int)given[0];
                    }

                    break;
                case 4:
                    overlap = (int)given[0];
                    break;
                default:
                    throw new JgsRuntimeException(line, col, $"{name} reads at most four numeric options.");
            }
        }

        if (nw is not null && overlap is null)
        {
            overlap = nw.Value - 1;
        }

        bool scalarNfft = frequencies is null;
        SpectralRange range = oneSided
            ? SpectralRange.OneSided
            : twoSided
                ? SpectralRange.TwoSided
                : real && scalarNfft ? SpectralRange.OneSided : SpectralRange.TwoSided;
        if (!scalarNfft)
        {
            range = SpectralRange.TwoSided;
        }

        return new SubspaceWords(
            nfft, frequencies, fs ?? (2 * System.Math.PI), fs is not null, nw, overlap,
            correlation, ev, range, centered && scalarNfft, window);
    }

    /// <summary><c>pmusic</c> and <c>peig</c>.</summary>
    private static JgsValue[] SubspaceSpectrum(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 2, 10, line, col);
        (Complex[][] channels, bool real, _) = SpectralChannels(name, args[0], line, col);
        (int dimension, double? threshold) = SubspaceDimension(name, args[1], line, col);
        SubspaceWords words = ReadSubspaceWords(name, args, 2, real, name == "peig", line, col);
        SubspaceSpectra.Subspaces split = SubspaceSpectra.Split(
            channels, dimension, threshold, words.Correlation, words.SegmentLength, words.Overlap, words.Window);
        int nfft = words.Frequencies is null ? words.Nfft : words.Frequencies.Length;
        (double[] values, double[] grid) = SubspaceSpectra.Pseudospectrum(
            split, nfft, words.SampleRateGiven ? words.SampleRate : null, words.Eigenvector);

        int keep = words.Range == SpectralRange.OneSided && words.Frequencies is null
            ? SpectralEstimation.OneSidedLength(nfft)
            : nfft;
        var kept = new Complex[keep];
        var frequencies = new double[keep];
        for (int i = 0; i < keep; i++)
        {
            kept[i] = values[i];
            frequencies[i] = grid[i];
        }

        if (words.CenterDc)
        {
            (kept, frequencies) = SpectralEstimation.CenterDc(
                kept, frequencies, words.Range, nfft, words.SampleRate, scaled: false);
        }

        JgsValue spectrum = ColumnOfDoubles(SpectralEstimators.RealPart(kept));
        JgsValue f = ColumnOfDoubles(frequencies);
        if (wanted <= 1)
        {
            return [spectrum];
        }

        if (wanted == 2)
        {
            return [spectrum, f];
        }

        JgsValue noise = SpectralMatrix(split.Noise);
        return wanted == 3
            ? [spectrum, f, noise]
            : [spectrum, f, noise, ColumnOfDoubles(split.Eigenvalues)];
    }

    /// <summary>The subspace dimension, which may carry a threshold beside it.</summary>
    private static (int Dimension, double? Threshold) SubspaceDimension(
        string name, JgsValue value, int line, int col)
    {
        double[] given = ToDoubles(name, value, line, col);
        if (given.Length == 0 || given.Length > 2)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} needs a subspace dimension, on its own or with a threshold beside it.");
        }

        if (given[0] != System.Math.Floor(given[0]))
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a whole subspace dimension.");
        }

        return ((int)given[0], given.Length == 2 ? given[1] : null);
    }

    /// <summary><c>rootmusic</c> and <c>rooteig</c>.</summary>
    private static JgsValue[] RootedSubspace(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 2, 5, line, col);
        (Complex[][] channels, bool real, _) = SpectralChannels(name, args[0], line, col);
        (int dimension, double? threshold) = SubspaceDimension(name, args[1], line, col);
        if (real && (dimension % 2) != 0)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} needs an even subspace dimension for a real signal.");
        }

        // MATLAB passes an empty transform length in front of the caller's arguments, so the first
        // number a caller gives is the sample rate rather than a transform length.
        var shifted = new List<JgsValue> { JgsMatrix.FromColumnMajor([], 0, 0) };
        for (int i = 2; i < args.Count; i++)
        {
            shifted.Add(args[i]);
        }

        var forwarded = new List<JgsValue> { args[0], args[1] };
        forwarded.AddRange(shifted);
        SubspaceWords words = ReadSubspaceWords(name, forwarded, 2, real, name == "rooteig", line, col);
        SubspaceSpectra.Subspaces split = SubspaceSpectra.Split(
            channels, dimension, threshold, words.Correlation, words.SegmentLength, words.Overlap, words.Window);
        (double[] frequencies, bool found) = SubspaceSpectra.RootFrequencies(split, words.Eigenvector);
        if (!found)
        {
            JgsValue nan = ColumnOfDoubles(frequencies);
            return wanted <= 1 ? [nan] : [nan, nan];
        }

        double[] powers = SubspacePowers(split, frequencies, real);
        var reported = new double[frequencies.Length];
        for (int i = 0; i < frequencies.Length; i++)
        {
            reported[i] = words.SampleRateGiven
                ? frequencies[i] * words.SampleRate / (2 * System.Math.PI)
                : frequencies[i];
        }

        JgsValue answer = ColumnOfDoubles(reported);
        return wanted <= 1 ? [answer] : [answer, ColumnOfDoubles(powers)];
    }

    /// <summary>
    /// The power at each frequency: a non-negative least-squares fit of the signal eigenvalues
    /// against the signal subspace's response at those frequencies.
    /// </summary>
    private static double[] SubspacePowers(
        SubspaceSpectra.Subspaces split, double[] frequencies, bool real)
    {
        double noise = 0;
        int noiseCount = split.Noise.Length;
        for (int i = split.SignalDimension; i < split.Eigenvalues.Length; i++)
        {
            noise += split.Eigenvalues[i];
        }

        noise = noiseCount == 0 ? 0 : noise / noiseCount;

        double[] used = real
            ? frequencies.Where(static w => w >= 0).ToArray()
            : frequencies;
        int count = used.Length;
        if (count == 0)
        {
            return [];
        }

        var design = new double[count, count];
        for (int n = 0; n < count; n++)
        {
            Complex[] vector = split.Signal.Length > n ? split.Signal[n] : [];
            for (int i = 0; i < count; i++)
            {
                Complex z = Complex.Exp(new Complex(0, -used[i]));
                Complex value = 0;
                for (int k = 0; k < vector.Length; k++)
                {
                    value += vector[k] * Complex.Pow(z, k);
                }

                double magnitude = value.Magnitude;
                design[n, i] = magnitude * magnitude;
            }
        }

        var target = new double[count];
        double nudge = System.Math.Sqrt(2.220446049250313e-16);
        for (int i = 0; i < count; i++)
        {
            target[i] = split.Eigenvalues[i] - noise;
            for (int j = 0; j < count; j++)
            {
                target[i] += design[i, j] * nudge;
            }
        }

        return NonNegativeSolve(design, target);
    }

    /// <summary>Lawson and Hanson's active-set solve, which is what <c>lsqnonneg</c> is.</summary>
    private static double[] NonNegativeSolve(double[,] matrix, double[] rhs)
    {
        int rows = matrix.GetLength(0);
        int columns = matrix.GetLength(1);
        var x = new double[columns];
        var free = new bool[columns];
        double tolerance = 10 * 2.220446049250313e-16 * rows * columns;
        for (int pass = 0; pass < 3 * columns; pass++)
        {
            var gradient = new double[columns];
            for (int j = 0; j < columns; j++)
            {
                double residual = 0;
                for (int i = 0; i < rows; i++)
                {
                    double predicted = 0;
                    for (int k = 0; k < columns; k++)
                    {
                        predicted += matrix[i, k] * x[k];
                    }

                    residual += matrix[i, j] * (rhs[i] - predicted);
                }

                gradient[j] = residual;
            }

            int best = -1;
            double largest = tolerance;
            for (int j = 0; j < columns; j++)
            {
                if (!free[j] && gradient[j] > largest)
                {
                    largest = gradient[j];
                    best = j;
                }
            }

            if (best < 0)
            {
                break;
            }

            free[best] = true;
            for (int inner = 0; inner < 3 * columns; inner++)
            {
                double[] trial = SolveOnFreeSet(matrix, rhs, free, columns, rows);
                bool negative = false;
                for (int j = 0; j < columns; j++)
                {
                    if (free[j] && trial[j] <= 0)
                    {
                        negative = true;
                        break;
                    }
                }

                if (!negative)
                {
                    x = trial;
                    break;
                }

                double step = double.PositiveInfinity;
                for (int j = 0; j < columns; j++)
                {
                    if (free[j] && trial[j] <= 0)
                    {
                        double candidate = x[j] / (x[j] - trial[j]);
                        step = System.Math.Min(step, candidate);
                    }
                }

                for (int j = 0; j < columns; j++)
                {
                    x[j] += step * (trial[j] - x[j]);
                    if (free[j] && System.Math.Abs(x[j]) < tolerance)
                    {
                        free[j] = false;
                        x[j] = 0;
                    }
                }
            }
        }

        return x;
    }

    /// <summary>The unconstrained least-squares solution over the columns currently in the active set.</summary>
    private static double[] SolveOnFreeSet(
        double[,] matrix, double[] rhs, bool[] free, int columns, int rows)
    {
        var indices = new List<int>();
        for (int j = 0; j < columns; j++)
        {
            if (free[j])
            {
                indices.Add(j);
            }
        }

        int m = indices.Count;
        var normal = new double[m, m + 1];
        for (int i = 0; i < m; i++)
        {
            for (int j = 0; j < m; j++)
            {
                double sum = 0;
                for (int r = 0; r < rows; r++)
                {
                    sum += matrix[r, indices[i]] * matrix[r, indices[j]];
                }

                normal[i, j] = sum;
            }

            double target = 0;
            for (int r = 0; r < rows; r++)
            {
                target += matrix[r, indices[i]] * rhs[r];
            }

            normal[i, m] = target;
        }

        for (int pivot = 0; pivot < m; pivot++)
        {
            int best = pivot;
            for (int r = pivot + 1; r < m; r++)
            {
                if (System.Math.Abs(normal[r, pivot]) > System.Math.Abs(normal[best, pivot]))
                {
                    best = r;
                }
            }

            if (best != pivot)
            {
                for (int c = pivot; c <= m; c++)
                {
                    (normal[pivot, c], normal[best, c]) = (normal[best, c], normal[pivot, c]);
                }
            }

            double head = normal[pivot, pivot];
            if (head == 0)
            {
                continue;
            }

            for (int r = pivot + 1; r < m; r++)
            {
                double factor = normal[r, pivot] / head;
                for (int c = pivot; c <= m; c++)
                {
                    normal[r, c] -= factor * normal[pivot, c];
                }
            }
        }

        var solution = new double[columns];
        var partial = new double[m];
        for (int i = m - 1; i >= 0; i--)
        {
            double sum = normal[i, m];
            for (int j = i + 1; j < m; j++)
            {
                sum -= normal[i, j] * partial[j];
            }

            partial[i] = normal[i, i] == 0 ? 0 : sum / normal[i, i];
        }

        for (int i = 0; i < m; i++)
        {
            solution[indices[i]] = partial[i];
        }

        return solution;
    }

    // --- corrmtx and pmem ---------------------------------------------------------------------------

    /// <summary><c>[X, R] = corrmtx(x, m, method)</c>.</summary>
    private static JgsValue[] CorrelationMatrixOf(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 2, 3, line, col);
        Complex[] x = ComplexArrayOf(name, args[0], line, col);
        int m = (int)Num(name, args, 1, line, col);
        CorrelationShape shape = CorrelationShape.Autocorrelation;
        if (args.Count > 2)
        {
            string given = StrOf(name, args[2], line, col).ToLowerInvariant();
            string[] allowed = ["autocorrelation", "covariance", "modified", "prewindowed", "postwindowed"];
            string? matched = null;
            foreach (string candidate in allowed)
            {
                if (given.Length > 0 && candidate.StartsWith(given, StringComparison.Ordinal))
                {
                    if (matched is not null)
                    {
                        throw new JgsRuntimeException(line, col, $"{name} cannot tell which method '{given}' means.");
                    }

                    matched = candidate;
                }
            }

            shape = matched switch
            {
                "covariance" => CorrelationShape.Covariance,
                "modified" => CorrelationShape.Modified,
                "prewindowed" => CorrelationShape.Prewindowed,
                "postwindowed" => CorrelationShape.Postwindowed,
                "autocorrelation" => CorrelationShape.Autocorrelation,
                _ => throw new JgsRuntimeException(line, col,
                    $"{name} knows the methods 'autocorrelation', 'covariance', 'modified', 'prewindowed' and 'postwindowed'."),
            };
        }

        Complex[,] matrix = AutoRegressiveModels.CorrelationMatrix(x, m, shape);
        int rows = matrix.GetLength(0);
        int width = matrix.GetLength(1);
        var flat = new Complex[rows * width];
        for (int j = 0; j < width; j++)
        {
            for (int i = 0; i < rows; i++)
            {
                flat[(j * rows) + i] = matrix[i, j];
            }
        }

        JgsValue value = ComplexShaped(flat, [rows, width]);
        if (wanted <= 1)
        {
            return [value];
        }

        var gram = new Complex[width * width];
        for (int i = 0; i < width; i++)
        {
            for (int j = 0; j < width; j++)
            {
                Complex sum = 0;
                for (int r = 0; r < rows; r++)
                {
                    sum += Complex.Conjugate(matrix[r, i]) * matrix[r, j];
                }

                gram[(j * width) + i] = sum;
            }
        }

        for (int i = 0; i < width; i++)
        {
            for (int j = i; j < width; j++)
            {
                Complex mean = (gram[(j * width) + i] + Complex.Conjugate(gram[(i * width) + j])) / 2;
                gram[(j * width) + i] = mean;
                gram[(i * width) + j] = Complex.Conjugate(mean);
            }
        }

        return [value, ComplexShaped(gram, [width, width])];
    }

    /// <summary><c>[pxx, f, a] = pmem(x, p, nfft, fs, 'corr')</c>, the maximum-entropy estimate.</summary>
    private static JgsValue[] MaximumEntropy(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 2, 5, line, col);
        Complex[] flat = ComplexArrayOf(name, args[0], line, col);
        int[] dims = SizeDims(args[0]);
        int rows = dims.Length > 0 ? dims[0] : 1;
        int columns = flat.Length == 0 ? 0 : flat.Length / System.Math.Max(rows, 1);
        int order = (int)Num(name, args, 1, line, col);
        int nfft = 256;
        double fs = 2;
        bool correlation = false;
        int numbers = 0;
        for (int i = 2; i < args.Count; i++)
        {
            if (IsTextScalar(args[i]))
            {
                correlation = StrOf(name, args[i], line, col).ToUpperInvariant().Contains("CORR", StringComparison.Ordinal);
                continue;
            }

            numbers++;
            if (ElementCount(args[i]) == 0)
            {
                continue;
            }

            if (numbers == 1)
            {
                nfft = (int)Num(name, args, i, line, col);
            }
            else if (numbers == 2)
            {
                fs = Num(name, args, i, line, col);
            }
            else
            {
                throw new JgsRuntimeException(line, col, $"{name} reads at most a transform length and a sample rate.");
            }
        }

        Complex[,] r;
        bool matrixInput = rows > 1 && columns > 1;
        if (!matrixInput)
        {
            Complex[] lags = AutoRegressiveModels.BiasedAutocorrelation(flat, order);
            r = new Complex[order + 1, order + 1];
            for (int i = 0; i <= order; i++)
            {
                for (int j = 0; j <= order; j++)
                {
                    int lag = i - j;
                    r[i, j] = lag >= 0 ? lags[lag] : Complex.Conjugate(lags[-lag]);
                }
            }
        }
        else if (correlation)
        {
            if (rows != columns)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs a square correlation matrix.");
            }

            r = new Complex[rows, columns];
            for (int j = 0; j < columns; j++)
            {
                for (int i = 0; i < rows; i++)
                {
                    r[i, j] = flat[(j * rows) + i];
                }
            }
        }
        else
        {
            throw new JgsRuntimeException(line, col,
                $"{name} needs the 'corr' flag to read a matrix as a correlation matrix.");
        }

        int size = r.GetLength(0) - 1;
        order = System.Math.Min(order, size);
        var a = new Complex[order + 1];
        a[0] = 1;
        if (order > 0)
        {
            var system = new Complex[order, order + 1];
            for (int i = 0; i < order; i++)
            {
                for (int j = 0; j < order; j++)
                {
                    system[i, j] = r[i + 1, j + 1];
                }

                system[i, order] = -r[i + 1, 0];
            }

            Complex[] solution = SolveDense(system, order);
            for (int i = 0; i < order; i++)
            {
                a[i + 1] = solution[i];
            }
        }

        Complex variance = 0;
        for (int i = 0; i <= order; i++)
        {
            variance += r[0, i] * a[i];
        }

        double e = variance.Magnitude;
        var padded = new Complex[nfft];
        for (int i = 0; i < a.Length && i < nfft; i++)
        {
            padded[i] = a[i];
        }

        JGraph.Signal.Fft.Transform(padded, inverse: false);
        bool real = true;
        for (int i = 0; i <= size && real; i++)
        {
            for (int j = 0; j <= size; j++)
            {
                if (r[i, j].Imaginary != 0)
                {
                    real = false;
                    break;
                }
            }
        }

        int keep = real ? SpectralEstimation.OneSidedLength(nfft) : nfft;
        var pxx = new double[keep];
        var frequencies = new double[keep];
        for (int i = 0; i < keep; i++)
        {
            double magnitude = padded[i].Magnitude;
            pxx[i] = e / (magnitude * magnitude);
            frequencies[i] = i * fs / nfft;
        }

        JgsValue spectrum = ColumnOfDoubles(pxx);
        if (wanted <= 1)
        {
            return [spectrum];
        }

        JgsValue f = ColumnOfDoubles(frequencies);
        return wanted == 2 ? [spectrum, f] : [spectrum, f, ComplexColumn(a)];
    }

    /// <summary>Gaussian elimination over a dense complex system laid out with its right side attached.</summary>
    private static Complex[] SolveDense(Complex[,] system, int n)
    {
        for (int pivot = 0; pivot < n; pivot++)
        {
            int best = pivot;
            for (int r = pivot + 1; r < n; r++)
            {
                if (system[r, pivot].Magnitude > system[best, pivot].Magnitude)
                {
                    best = r;
                }
            }

            if (best != pivot)
            {
                for (int c = pivot; c <= n; c++)
                {
                    (system[pivot, c], system[best, c]) = (system[best, c], system[pivot, c]);
                }
            }

            Complex head = system[pivot, pivot];
            if (head == Complex.Zero)
            {
                continue;
            }

            for (int r = pivot + 1; r < n; r++)
            {
                Complex factor = system[r, pivot] / head;
                for (int c = pivot; c <= n; c++)
                {
                    system[r, c] -= factor * system[pivot, c];
                }
            }
        }

        var solution = new Complex[n];
        for (int i = n - 1; i >= 0; i--)
        {
            Complex sum = system[i, n];
            for (int j = i + 1; j < n; j++)
            {
                sum -= system[i, j] * solution[j];
            }

            solution[i] = system[i, i] == Complex.Zero ? 0 : sum / system[i, i];
        }

        return solution;
    }
}
