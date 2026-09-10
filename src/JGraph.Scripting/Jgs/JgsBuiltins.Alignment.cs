using System.Numerics;
using JGraph.Signal;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Lining two signals up and measuring how far apart they are (M136): the delay between them, the
/// shift that removes it, the two warping distances, the search for one signal inside another, and
/// the three correlation forms that go with them.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the alignment, distance and correlation names.</summary>
    internal static void RegisterAlignmentBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        void Many(string name, Func<string, IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> body) =>
            Define(name,
                (args, line, col) => body(name, args, 1, line, col)[0],
                (args, wanted, line, col) => body(name, args, wanted, line, col));

        Define("finddelay", DelayBetween);
        Many("alignsignals", AlignSignals);
        Many("dtw", WarpDistance);
        Many("edr", WarpDistance);
        Many("findsignal", FindSignal);
        Define("xcorr2", CrossCorrelate2);
        Define("cconv", CircularConvolution);
        Define("convmtx", ConvolutionMatrix);
    }

    /// <summary>A signal read as a matrix of rows by samples, which is how the distances see it.</summary>
    private static double[,] AlignmentMatrix(string name, JgsValue value, int line, int col)
    {
        double[] flat = ToDoubles(name, value, line, col);
        int[] dims = SizeDims(value);
        int rows = dims.Length > 0 ? dims[0] : 1;
        int columns = flat.Length / System.Math.Max(rows, 1);
        if (rows > 1 && columns == 1)
        {
            (rows, columns) = (1, rows);
        }

        var matrix = new double[rows, columns];
        for (int c = 0; c < columns; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                matrix[r, c] = flat[(c * rows) + r];
            }
        }

        return matrix;
    }

    private static SampleMetric MetricOf(string name, string word, int line, int col) => word switch
    {
        "absolute" => SampleMetric.Absolute,
        "euclidean" => SampleMetric.Euclidean,
        "squared" => SampleMetric.Squared,
        "symmkl" => SampleMetric.SymmetricKl,
        _ => throw new JgsRuntimeException(line, col,
            $"{name} knows the metrics 'absolute', 'euclidean', 'squared' and 'symmkl'."),
    };

    /// <summary><c>d = finddelay(x, y, maxlag)</c>.</summary>
    private static JgsValue DelayBetween(IReadOnlyList<JgsValue> args, int line, int col)
    {
        const string name = "finddelay";
        ArityRange(name, args, 1, 3, line, col);
        double[] x = ToDoubles(name, args[0], line, col);
        double[] y = args.Count > 1 ? ToDoubles(name, args[1], line, col) : x;
        int maxlag = args.Count > 2
            ? (int)Num(name, args, 2, line, col)
            : System.Math.Max(x.Length, y.Length) - 1;

        double energyX = x.Sum(static v => v * v);
        double energyY = y.Sum(static v => v * v);
        if (energyX == 0 || energyY == 0)
        {
            return JgsValue.Number(0);
        }

        double scale = System.Math.Sqrt(energyX * energyY);
        var correlation = new double[(2 * maxlag) + 1];
        for (int lag = -maxlag; lag <= maxlag; lag++)
        {
            double sum = 0;
            for (int i = 0; i < x.Length; i++)
            {
                int j = i - lag;
                if (j >= 0 && j < y.Length)
                {
                    sum += x[i] * y[j];
                }
            }

            correlation[lag + maxlag] = System.Math.Abs(sum) / scale;
        }

        // The positive and negative halves are searched separately so that a tie between two lags
        // of equal magnitude goes to the one nearer zero, which is MATLAB's rule.
        int positiveAt = 0;
        double positiveBest = double.NegativeInfinity;
        for (int i = maxlag; i < correlation.Length; i++)
        {
            if (correlation[i] > positiveBest)
            {
                positiveBest = correlation[i];
                positiveAt = i - maxlag + 1;
            }
        }

        int negativeAt = 0;
        double negativeBest = double.NegativeInfinity;
        for (int i = maxlag - 1; i >= 0; i--)
        {
            if (correlation[i] > negativeBest)
            {
                negativeBest = correlation[i];
                negativeAt = maxlag - i;
            }
        }

        int index;
        double best;
        if (maxlag == 0 || negativeBest == double.NegativeInfinity)
        {
            index = maxlag + positiveAt;
            best = positiveBest;
        }
        else if (positiveBest > negativeBest || (positiveBest == negativeBest && positiveAt <= negativeAt))
        {
            index = maxlag + positiveAt;
            best = positiveBest;
        }
        else
        {
            index = maxlag + 1 - negativeAt;
            best = negativeBest;
        }

        double delay = maxlag + 1 - index;
        return JgsValue.Number(best < 1e-8 ? 0 : delay);
    }

    /// <summary><c>[xa, ya, d] = alignsignals(x, y, maxlag, 'truncate')</c>.</summary>
    private static JgsValue[] AlignSignals(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 2, 12, line, col);
        double[] x = ToDoubles(name, args[0], line, col);
        double[] y = ToDoubles(name, args[1], line, col);
        int[] xdims = SizeDims(args[0]);
        int[] ydims = SizeDims(args[1]);
        bool xIsColumn = xdims.Length > 0 && xdims[0] > 1;
        bool yIsColumn = ydims.Length > 0 && ydims[0] > 1;
        int maxlag = System.Math.Max(x.Length, y.Length) - 1;
        bool truncate = false;
        for (int i = 2; i < args.Count; i++)
        {
            if (IsTextScalar(args[i]))
            {
                string word = StrOf(name, args[i], line, col).ToLowerInvariant();
                if (word == "truncate")
                {
                    truncate = true;
                    continue;
                }

                throw new JgsRuntimeException(line, col, $"{name} does not know the option '{word}'.");
            }

            if (ElementCount(args[i]) > 0)
            {
                maxlag = (int)Num(name, args, i, line, col);
            }
        }

        var forwarded = new List<JgsValue> { args[0], args[1], JgsValue.Number(maxlag) };
        int delay = (int)System.Math.Round(
            Num(name, [DelayBetween(forwarded, line, col)], 0, line, col),
            MidpointRounding.AwayFromZero);

        double[] shiftedX = x;
        double[] shiftedY = y;
        if (delay > 0)
        {
            shiftedX = Shift(x, delay, truncate);
        }
        else if (delay < 0)
        {
            shiftedY = Shift(y, -delay, truncate);
        }

        JgsValue first = xIsColumn
            ? JgsMatrix.FromColumnMajor(shiftedX, shiftedX.Length, 1)
            : JgsMatrix.FromColumnMajor(shiftedX, 1, shiftedX.Length);
        JgsValue second = yIsColumn
            ? JgsMatrix.FromColumnMajor(shiftedY, shiftedY.Length, 1)
            : JgsMatrix.FromColumnMajor(shiftedY, 1, shiftedY.Length);
        return wanted switch
        {
            <= 1 => [first],
            2 => [first, second],
            _ => [first, second, JgsValue.Number(delay)],
        };
    }

    private static double[] Shift(double[] x, int by, bool truncate)
    {
        if (!truncate)
        {
            var padded = new double[x.Length + by];
            Array.Copy(x, 0, padded, by, x.Length);
            return padded;
        }

        var kept = new double[x.Length];
        if (by < x.Length)
        {
            Array.Copy(x, 0, kept, by, x.Length - by);
        }

        return kept;
    }

    /// <summary><c>dtw</c> and <c>edr</c>, which fill the same table with different costs.</summary>
    private static JgsValue[] WarpDistance(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 2, 5, line, col);
        double[,] x = AlignmentMatrix(name, args[0], line, col);
        double[,] y = AlignmentMatrix(name, args[1], line, col);
        if (x.GetLength(0) != y.GetLength(0))
        {
            throw new JgsRuntimeException(line, col, $"{name} needs two signals with the same number of rows.");
        }

        int[] xdims = SizeDims(args[0]);
        bool wasColumn = xdims.Length > 1 && xdims[0] > 1 && xdims[1] == 1;
        int at = 2;
        double tolerance = 0;
        if (name == "edr")
        {
            tolerance = Num(name, args, 2, line, col);
            at = 3;
        }

        var metric = SampleMetric.Euclidean;
        int? band = null;
        for (int i = at; i < args.Count; i++)
        {
            if (IsTextScalar(args[i]))
            {
                metric = MetricOf(name, StrOf(name, args[i], line, col).ToLowerInvariant(), line, col);
                continue;
            }

            band = (int)Num(name, args, i, line, col);
        }

        if (name == "edr")
        {
            metric = metric == SampleMetric.Euclidean ? SampleMetric.Euclidean : metric;
        }

        double[,] table = name == "edr"
            ? SignalAlignment.EditTable(x, y, tolerance, metric)
            : Constrained(x, y, metric, band);
        int nx = x.GetLength(1);
        int ny = y.GetLength(1);
        if (nx == 0 || ny == 0)
        {
            return [JgsValue.Number(double.NaN)];
        }

        double distance = table[nx - 1, ny - 1];
        (int[] ix, int[] iy) = SignalAlignment.Traceback(table);
        JgsValue Path(int[] path)
        {
            var values = new double[path.Length];
            for (int i = 0; i < path.Length; i++)
            {
                values[i] = path[i] + 1;
            }

            return wasColumn
                ? JgsMatrix.FromColumnMajor(values, values.Length, 1)
                : JgsMatrix.FromColumnMajor(values, 1, values.Length);
        }

        return wanted switch
        {
            <= 1 => [JgsValue.Number(distance)],
            2 => [JgsValue.Number(distance), Path(ix)],
            _ => [JgsValue.Number(distance), Path(ix), Path(iy)],
        };
    }

    /// <summary>The warping table, with the longer signal down the rows when a band is asked for.</summary>
    private static double[,] Constrained(double[,] x, double[,] y, SampleMetric metric, int? band)
    {
        if (band is null || x.GetLength(1) >= y.GetLength(1))
        {
            return SignalAlignment.WarpTable(x, y, metric, band);
        }

        double[,] flipped = SignalAlignment.WarpTable(y, x, metric, band);
        var table = new double[x.GetLength(1), y.GetLength(1)];
        for (int i = 0; i < table.GetLength(0); i++)
        {
            for (int j = 0; j < table.GetLength(1); j++)
            {
                table[i, j] = flipped[j, i];
            }
        }

        return table;
    }

    /// <summary><c>[istart, istop, dist] = findsignal(data, signal, ...)</c>.</summary>
    private static JgsValue[] FindSignal(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 2, 18, line, col);
        double[,] data = AlignmentMatrix(name, args[0], line, col);
        double[,] pattern = AlignmentMatrix(name, args[1], line, col);
        int[] dims = SizeDims(args[0]);
        bool wasColumn = dims.Length > 1 && dims[0] > 1 && dims[1] == 1;
        var metric = SampleMetric.Squared;
        int maxSegments = int.MaxValue;
        double maxDistance = double.PositiveInfinity;
        for (int i = 2; i < args.Count; i += 2)
        {
            if (i + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs a value after every option name.");
            }

            string option = StrOf(name, args[i], line, col).ToLowerInvariant();
            switch (option)
            {
                case "metric":
                    metric = MetricOf(name, StrOf(name, args[i + 1], line, col).ToLowerInvariant(), line, col);
                    break;
                case "maxnumsegments":
                    maxSegments = (int)Num(name, args, i + 1, line, col);
                    break;
                case "maxdistance":
                    maxDistance = Num(name, args, i + 1, line, col);
                    break;
                case "normalization":
                case "normalizationlength":
                case "timealignment":
                case "tolerance":
                case "annotate":
                    throw new JgsRuntimeException(line, col,
                        $"{name} in this build searches with a fixed alignment and no normalisation, "
                        + "so it does not take '" + option + "'.");
                default:
                    throw new JgsRuntimeException(line, col, $"{name} does not know the option '{option}'.");
            }
        }

        // A candidate is a local minimum of the sliding distance, and MATLAB finds those by looking
        // for peaks in the negated distance padded with infinities either side — which is not the
        // same as testing each sample against its neighbours, because a run of equal distances is
        // one candidate rather than none or many.
        if (maxSegments == int.MaxValue && double.IsPositiveInfinity(maxDistance))
        {
            // Neither cap named means one segment: the best match and nothing else.
            maxSegments = 1;
        }

        double[] sliding = SignalAlignment.SlidingDistance(data, pattern, metric);
        var padded = new double[sliding.Length + 2];
        padded[0] = double.NegativeInfinity;
        padded[^1] = double.NegativeInfinity;
        for (int i = 0; i < sliding.Length; i++)
        {
            padded[i + 1] = -sliding[i];
        }

        var positions = new double[padded.Length];
        for (int i = 0; i < padded.Length; i++)
        {
            positions[i] = i + 1;
        }

        PeakFinding.Peaks minima = PeakFinding.Find(padded, positions, new PeakFinding.Criteria());
        var starts = new List<double>();
        var stops = new List<double>();
        var distances = new List<double>();
        int width = pattern.GetLength(1);
        foreach (int index in minima.Indices)
        {
            double distance = -padded[index];
            if (distance > maxDistance)
            {
                continue;
            }

            starts.Add(index);
            stops.Add(index + width - 1);
            distances.Add(distance);
        }

        int[] order = [.. Enumerable.Range(0, distances.Count)];
        Array.Sort(order, (a, b) => distances[a].CompareTo(distances[b]));
        int count = System.Math.Min(order.Length, maxSegments);
        var outStart = new double[count];
        var outStop = new double[count];
        var outDistance = new double[count];
        for (int i = 0; i < count; i++)
        {
            outStart[i] = starts[order[i]];
            outStop[i] = stops[order[i]];
            outDistance[i] = distances[order[i]];
        }

        JgsValue Row(double[] values) => wasColumn
            ? JgsMatrix.FromColumnMajor(values, values.Length, 1)
            : JgsMatrix.FromColumnMajor(values, 1, values.Length);
        return wanted switch
        {
            <= 1 => [Row(outStart)],
            2 => [Row(outStart), Row(outStop)],
            _ => [Row(outStart), Row(outStop), Row(outDistance)],
        };
    }

    /// <summary><c>c = xcorr2(a, b)</c>: the two-dimensional cross-correlation.</summary>
    private static JgsValue CrossCorrelate2(IReadOnlyList<JgsValue> args, int line, int col)
    {
        const string name = "xcorr2";
        ArityRange(name, args, 1, 2, line, col);
        (Complex[,] a, int ar, int ac) = AlignmentComplexMatrix(name, args[0], line, col);
        (Complex[,] b, int br, int bc) = args.Count > 1
            ? AlignmentComplexMatrix(name, args[1], line, col)
            : (a, ar, ac);

        int rows = ar + br - 1;
        int columns = ac + bc - 1;
        var result = new Complex[rows * columns];
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < columns; j++)
            {
                Complex sum = 0;
                for (int m = 0; m < ar; m++)
                {
                    for (int n = 0; n < ac; n++)
                    {
                        int p = i - m;
                        int q = j - n;
                        if (p >= 0 && p < br && q >= 0 && q < bc)
                        {
                            // The second matrix is turned through half a circle and conjugated,
                            // which is what makes a convolution a correlation.
                            sum += a[m, n] * Complex.Conjugate(b[br - 1 - p, bc - 1 - q]);
                        }
                    }
                }

                result[(j * rows) + i] = sum;
            }
        }

        return ComplexShaped(result, [rows, columns]);
    }

    private static (Complex[,] Matrix, int Rows, int Columns) AlignmentComplexMatrix(
        string name, JgsValue value, int line, int col)
    {
        Complex[] flat = ComplexArrayOf(name, value, line, col);
        int[] dims = SizeDims(value);
        int rows = dims.Length > 0 ? dims[0] : 1;
        int columns = flat.Length / System.Math.Max(rows, 1);
        var matrix = new Complex[rows, columns];
        for (int c = 0; c < columns; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                matrix[r, c] = flat[(c * rows) + r];
            }
        }

        return (matrix, rows, columns);
    }

    /// <summary><c>c = cconv(a, b, n)</c>: the circular convolution.</summary>
    private static JgsValue CircularConvolution(IReadOnlyList<JgsValue> args, int line, int col)
    {
        const string name = "cconv";
        ArityRange(name, args, 2, 3, line, col);
        Complex[] a = ComplexArrayOf(name, args[0], line, col);
        Complex[] b = ComplexArrayOf(name, args[1], line, col);
        int n = args.Count > 2 ? (int)Num(name, args, 2, line, col) : a.Length + b.Length - 1;
        var wrappedA = new Complex[n];
        var wrappedB = new Complex[n];
        for (int i = 0; i < a.Length; i++)
        {
            wrappedA[i % n] += a[i];
        }

        for (int i = 0; i < b.Length; i++)
        {
            wrappedB[i % n] += b[i];
        }

        Complex[] fa = Fft.Forward(wrappedA);
        Complex[] fb = Fft.Forward(wrappedB);
        for (int i = 0; i < n; i++)
        {
            fa[i] *= fb[i];
        }

        Complex[] product = Fft.Inverse(fa);
        bool real = a.All(static z => z.Imaginary == 0) && b.All(static z => z.Imaginary == 0);
        int[] dims = SizeDims(args[0]);
        bool asRow = dims.Length > 1 && dims[0] == 1;
        if (!real)
        {
            return ComplexShaped(product, asRow ? [1, n] : [n, 1]);
        }

        var values = new double[n];
        for (int i = 0; i < n; i++)
        {
            values[i] = product[i].Real;
        }

        return asRow
            ? JgsMatrix.FromColumnMajor(values, 1, n)
            : JgsMatrix.FromColumnMajor(values, n, 1);
    }

    /// <summary><c>A = convmtx(h, n)</c>: the matrix whose product with a signal is a convolution.</summary>
    private static JgsValue ConvolutionMatrix(IReadOnlyList<JgsValue> args, int line, int col)
    {
        const string name = "convmtx";
        Arity(name, args, 2, line, col);
        Complex[] h = ComplexArrayOf(name, args[0], line, col);
        int n = (int)Num(name, args, 1, line, col);
        if (n < 1)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a positive length.");
        }

        int[] dims = SizeDims(args[0]);
        bool asRow = dims.Length > 1 && dims[0] == 1 && dims[1] > 1;
        int m = h.Length + n - 1;
        int rows = asRow ? n : m;
        int columns = asRow ? m : n;
        var flat = new Complex[rows * columns];
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < h.Length; i++)
            {
                int r = asRow ? j : i + j;
                int c = asRow ? i + j : j;
                flat[(c * rows) + r] = h[i];
            }
        }

        return ComplexShaped(flat, [rows, columns]);
    }
}
