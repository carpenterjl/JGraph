using JGraph.Signal;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>findpeaks</c> and the four one-line measurements beside it (M136): peak to peak, peak to root
/// mean square, the root sum of squares, and the rate at which a signal crosses zero.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the peak and level names.</summary>
    internal static void RegisterPeakMeasureBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.DeclareFunction(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        Define("findpeaks",
            (args, line, col) => PeaksOf(args, 1, line, col)[0],
            (args, wanted, line, col) => PeaksOf(args, wanted, line, col));
        Define("peak2peak", (args, line, col) => AlongDimension("peak2peak", args, line, col));
        Define("peak2rms", (args, line, col) => AlongDimension("peak2rms", args, line, col));
        Define("rssq", (args, line, col) => AlongDimension("rssq", args, line, col));
        Define("zerocrossrate",
            (args, line, col) => CrossingRate(args, 1, line, col)[0],
            (args, wanted, line, col) => CrossingRate(args, wanted, line, col));
    }

    /// <summary><c>[pks, locs, w, p] = findpeaks(y, x, name, value, ...)</c>.</summary>
    private static JgsValue[] PeaksOf(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        const string name = "findpeaks";
        ArityRange(name, args, 1, 22, line, col);
        double[] y = ToDoubles(name, args[0], line, col);
        if (y.Length < 3)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs at least three samples.");
        }

        int[] dims = SizeDims(args[0]);
        bool wasRow = dims.Length > 1 && dims[0] == 1 && dims[1] > 1;
        int start = 1;
        double[] x;
        bool locationsWereRow = wasRow;
        if (args.Count > 1 && !IsTextScalar(args[1]))
        {
            double[] given = ToDoubles(name, args[1], line, col);
            if (given.Length == 1)
            {
                x = new double[y.Length];
                for (int i = 0; i < y.Length; i++)
                {
                    x[i] = i / given[0];
                }
            }
            else
            {
                if (given.Length != y.Length)
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name} needs a location for every sample.");
                }

                x = given;
                int[] xdims = SizeDims(args[1]);
                locationsWereRow = xdims.Length > 1 && xdims[0] == 1 && xdims[1] > 1;
            }

            start = 2;
        }
        else
        {
            x = new double[y.Length];
            for (int i = 0; i < y.Length; i++)
            {
                x[i] = i + 1;
            }
        }

        var criteria = new PeakFinding.Criteria();
        for (int i = start; i < args.Count; i += 2)
        {
            if (i + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs a value after every option name.");
            }

            string option = StrOf(name, args[i], line, col).ToLowerInvariant();
            switch (option)
            {
                case "minpeakheight":
                    criteria.MinHeight = Num(name, args, i + 1, line, col);
                    break;
                case "minpeakprominence":
                    criteria.MinProminence = Num(name, args, i + 1, line, col);
                    break;
                case "minpeakwidth":
                    criteria.MinWidth = Num(name, args, i + 1, line, col);
                    break;
                case "maxpeakwidth":
                    criteria.MaxWidth = Num(name, args, i + 1, line, col);
                    break;
                case "minpeakdistance":
                    criteria.MinDistance = Num(name, args, i + 1, line, col);
                    break;
                case "threshold":
                    criteria.Threshold = Num(name, args, i + 1, line, col);
                    break;
                case "npeaks":
                    criteria.MaxCount = (int)Num(name, args, i + 1, line, col);
                    break;
                case "sortstr":
                    criteria.Order = StrOf(name, args[i + 1], line, col).ToLowerInvariant() switch
                    {
                        "none" => PeakOrder.None,
                        "ascend" => PeakOrder.Ascending,
                        "descend" => PeakOrder.Descending,
                        _ => throw new JgsRuntimeException(line, col,
                            $"{name} sorts 'none', 'ascend' or 'descend'."),
                    };
                    break;
                case "widthreference":
                    criteria.Reference = StrOf(name, args[i + 1], line, col).ToLowerInvariant() switch
                    {
                        "halfprom" => WidthReference.HalfProminence,
                        "halfheight" => WidthReference.HalfHeight,
                        _ => throw new JgsRuntimeException(line, col,
                            $"{name} measures width from 'halfprom' or 'halfheight'."),
                    };
                    break;
                case "annotate":
                    break;
                default:
                    throw new JgsRuntimeException(line, col, $"{name} does not know the option '{option}'.");
            }
        }

        if (criteria.Reference == WidthReference.HalfHeight)
        {
            // A width measured from half the peak's own height is meaningless for a peak below
            // zero, so MATLAB raises the height floor to zero rather than reporting one.
            criteria.MinHeight = System.Math.Max(criteria.MinHeight, 0);
        }

        PeakFinding.Peaks peaks = PeakFinding.Find(y, x, criteria);
        JgsValue Shaped(double[] values, bool row) => row
            ? JgsMatrix.FromColumnMajor(values, 1, values.Length)
            : JgsMatrix.FromColumnMajor(values, values.Length, 1);

        return wanted switch
        {
            <= 1 => [Shaped(peaks.Heights, wasRow)],
            2 => [Shaped(peaks.Heights, wasRow), Shaped(peaks.Locations, locationsWereRow)],
            3 =>
            [
                Shaped(peaks.Heights, wasRow), Shaped(peaks.Locations, locationsWereRow),
                Shaped(peaks.Widths, locationsWereRow),
            ],
            _ =>
            [
                Shaped(peaks.Heights, wasRow), Shaped(peaks.Locations, locationsWereRow),
                Shaped(peaks.Widths, locationsWereRow), Shaped(peaks.Prominences, wasRow),
            ],
        };
    }

    /// <summary><c>peak2peak</c>, <c>peak2rms</c> and <c>rssq</c>, each down columns or a named dimension.</summary>
    private static JgsValue AlongDimension(
        string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange(name, args, 1, 2, line, col);
        double[] flat = ToDoubles(name, args[0], line, col);
        int[] dims = SizeDims(args[0]);
        int rows = dims.Length > 0 ? dims[0] : 1;
        int columns = flat.Length / System.Math.Max(rows, 1);
        int dimension = args.Count > 1 ? (int)Num(name, args, 1, line, col) : (rows == 1 ? 2 : 1);
        if (dimension == 2)
        {
            (rows, columns) = (columns, rows);
            var transposed = new double[flat.Length];
            for (int r = 0; r < columns; r++)
            {
                for (int c = 0; c < rows; c++)
                {
                    transposed[(r * rows) + c] = flat[(c * columns) + r];
                }
            }

            flat = transposed;
        }

        var result = new double[columns];
        for (int c = 0; c < columns; c++)
        {
            double largest = double.NegativeInfinity;
            double smallest = double.PositiveInfinity;
            double largestMagnitude = 0;
            double energy = 0;
            for (int r = 0; r < rows; r++)
            {
                double value = flat[(c * rows) + r];
                largest = System.Math.Max(largest, value);
                smallest = System.Math.Min(smallest, value);
                largestMagnitude = System.Math.Max(largestMagnitude, System.Math.Abs(value));
                energy += value * value;
            }

            result[c] = name switch
            {
                "peak2peak" => largest - smallest,
                "rssq" => System.Math.Sqrt(energy),
                _ => largestMagnitude / System.Math.Sqrt(energy / rows),
            };
        }

        return dimension == 2
            ? JgsMatrix.FromColumnMajor(result, columns, 1)
            : JgsMatrix.FromColumnMajor(result, 1, columns);
    }

    /// <summary><c>[rate, count, indices] = zerocrossrate(x, name, value, ...)</c>.</summary>
    private static JgsValue[] CrossingRate(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        const string name = "zerocrossrate";
        ArityRange(name, args, 1, 17, line, col);
        double[] flat = ToDoubles(name, args[0], line, col);
        int[] dims = SizeDims(args[0]);
        int rows = dims.Length > 0 ? dims[0] : 1;
        int channels = flat.Length / System.Math.Max(rows, 1);
        if (rows == 1)
        {
            (rows, channels) = (channels, 1);
        }

        double[] initial = new double[channels];
        int window = rows;
        int overlap = 0;
        string method = "difference";
        double level = 0;
        double threshold = 0;
        string edge = "both";
        bool zeroPositive = false;
        for (int i = 1; i < args.Count; i += 2)
        {
            if (i + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs a value after every option name.");
            }

            string option = StrOf(name, args[i], line, col).ToLowerInvariant();
            switch (option)
            {
                case "initialstate":
                    initial = ToDoubles(name, args[i + 1], line, col);
                    break;
                case "windowlength":
                    window = (int)Num(name, args, i + 1, line, col);
                    break;
                case "overlaplength":
                    overlap = (int)Num(name, args, i + 1, line, col);
                    break;
                case "method":
                    method = StrOf(name, args[i + 1], line, col).ToLowerInvariant();
                    break;
                case "level":
                    level = Num(name, args, i + 1, line, col);
                    break;
                case "threshold":
                    threshold = Num(name, args, i + 1, line, col);
                    break;
                case "transitionedge":
                    edge = StrOf(name, args[i + 1], line, col).ToLowerInvariant();
                    break;
                case "zeropositive":
                    zeroPositive = Num(name, args, i + 1, line, col) != 0;
                    break;
                default:
                    throw new JgsRuntimeException(line, col, $"{name} does not know the option '{option}'.");
            }
        }

        if (initial.Length == 1 && channels > 1)
        {
            var spread = new double[channels];
            Array.Fill(spread, initial[0]);
            initial = spread;
        }

        if (window > rows || window <= 0)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a window no longer than the signal.");
        }

        if (overlap >= window || overlap < 0)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs an overlap shorter than the window.");
        }

        // The sign of the signal, with the level taken out and anything inside the threshold
        // treated as zero — which is what makes a noisy crossing count once rather than many times.
        var signs = new double[rows * channels];
        var initialSigns = new double[channels];
        for (int c = 0; c < channels; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                double value = flat[(c * rows) + r] - level;
                if (threshold != 0 && System.Math.Abs(value) <= threshold)
                {
                    value = 0;
                }

                double sign = System.Math.Sign(value);
                signs[(c * rows) + r] = zeroPositive && sign == 0 ? 1 : sign;
            }

            double start = initial[c % initial.Length] - level;
            if (threshold != 0 && System.Math.Abs(start) <= threshold)
            {
                start = 0;
            }

            double startSign = System.Math.Sign(start);
            initialSigns[c] = zeroPositive && startSign == 0 ? 1 : startSign;
        }

        int hop = window - overlap;
        int frames = ((rows - window) / hop) + 1;
        bool whole = window == rows && overlap == 0;
        var rate = new double[frames * channels];
        var count = new double[frames * channels];
        bool comparison = method == "comparison";
        var marks = new bool[frames * channels * window];
        for (int c = 0; c < channels; c++)
        {
            if (!comparison)
            {
                // The difference method counts half of every step the sign takes, so a crossing
                // that lands on zero on its way counts as two halves rather than as two crossings.
                var crossings = new double[rows];
                for (int r = 0; r < rows; r++)
                {
                    double previous = r == 0 ? initialSigns[c] : signs[(c * rows) + r - 1];
                    double difference = signs[(c * rows) + r] - previous;
                    if (edge == "rising" && difference < 0)
                    {
                        difference = 0;
                    }
                    else if (edge == "falling" && difference > 0)
                    {
                        difference = 0;
                    }

                    crossings[r] = System.Math.Abs(difference);
                }

                for (int frame = 0; frame < frames; frame++)
                {
                    double sum = 0;
                    int from = whole ? 0 : frame * hop;
                    for (int r = 0; r < window; r++)
                    {
                        sum += crossings[from + r];
                        marks[(((c * frames) + frame) * window) + r] = crossings[from + r] != 0;
                    }

                    count[(c * frames) + frame] = 0.5 * sum;
                    rate[(c * frames) + frame] = 0.5 * sum / window;
                }

                continue;
            }

            // The comparison method remembers the last sign that was not zero and counts one
            // crossing each time the signal comes out on the other side of it.
            for (int frame = 0; frame < frames; frame++)
            {
                int from = whole ? 0 : frame * hop;
                double previous = from == 0 ? initialSigns[c] : signs[(c * rows) + from - 1];
                double total = 0;
                for (int r = 0; r < window; r++)
                {
                    double current = signs[(c * rows) + from + r];
                    bool rising = previous < 0 && current > 0;
                    bool falling = previous > 0 && current < 0;
                    if ((rising && edge != "falling") || (falling && edge != "rising"))
                    {
                        total++;
                        marks[(((c * frames) + frame) * window) + r] = true;
                    }

                    if (current != 0)
                    {
                        previous = current;
                    }
                }

                count[(c * frames) + frame] = total;
                rate[(c * frames) + frame] = total / window;
            }
        }

        JgsValue rateValue = JgsMatrix.FromColumnMajor(rate, frames, channels);
        if (wanted <= 1)
        {
            return [rateValue];
        }

        JgsValue countValue = JgsMatrix.FromColumnMajor(count, frames, channels);
        if (wanted == 2)
        {
            return [rateValue, countValue];
        }

        // The flags come back one row per frame, which is the transpose of the way they are
        // gathered: a frame is a column while it is being counted and a row once it is reported.
        var flags = new double[frames * window * channels];
        for (int c = 0; c < channels; c++)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                for (int r = 0; r < window; r++)
                {
                    flags[(((c * window) + r) * frames) + frame] =
                        marks[(((c * frames) + frame) * window) + r] ? 1 : 0;
                }
            }
        }

        return [rateValue, countValue, JgsMatrix.FromColumnMajorDims(flags, [frames, window, channels])];
    }
}
