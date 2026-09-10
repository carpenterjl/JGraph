using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Numerics;
using JGraph.Numerics.LinearAlgebra;
using JGraph.Objects;
using JGraph.Signal;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The order-tracking names of M137: <c>tachorpm</c>, <c>rpmfreqmap</c>, <c>rpmordermap</c>,
/// <c>orderspectrum</c> and <c>ordertrack</c>.
/// </summary>
/// <remarks>
/// These all answer the same question — what a rotating machine was doing at each speed — from
/// different starting points. <c>tachorpm</c> turns a tachometer's pulse train into a speed signal;
/// the two maps turn a signal and a speed into a spectrogram indexed by speed; and the last two
/// read one number per frame off such a map.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the order-tracking names.</summary>
    internal static void RegisterOrderTrackingBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        void Many(string name, Func<string, IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> body) =>
            Define(name,
                (args, line, col) => First(body(name, args, 1, line, col)),
                (args, wanted, line, col) => body(name, args, wanted, line, col));

        Many("tachorpm", TachometerRpm);
        Many("rpmfreqmap", RotationMapOf);
        Many("rpmordermap", RotationMapOf);
        Many("orderspectrum", OrderSpectrum);
        Many("ordertrack", OrderTrack);
        Many("orderwaveform", OrderWaveform);
    }

    /// <summary><c>[rpm, t, tp] = tachorpm(x, fs, ...)</c>.</summary>
    private static JgsValue[] TachometerRpm(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 2, 12, line, col);
        double[] x = ToDoubles(name, args[0], line, col);
        double fs = Num(name, args, 1, line, col);
        double perRevolution = 1;
        double outputRate = fs;
        bool smooth = true;
        int fitPoints = 10;
        double[]? levels = null;
        for (int at = 2; at < args.Count; at += 2)
        {
            string word = StrOf(name, args[at], line, col).ToLowerInvariant();
            if (at + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs a value after '{word}'.");
            }

            if (Matches(word, "pulsesperrev"))
            {
                perRevolution = Num(name, args, at + 1, line, col);
            }
            else if (Matches(word, "outputfs"))
            {
                outputRate = Num(name, args, at + 1, line, col);
            }
            else if (Matches(word, "fittype"))
            {
                string kind = StrOf(name, args[at + 1], line, col).ToLowerInvariant();
                smooth = Matches(kind, "smooth");
                if (!smooth && !Matches(kind, "linear"))
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name} takes 'linear' or 'smooth' as its fit type.");
                }
            }
            else if (Matches(word, "fitpoints"))
            {
                fitPoints = (int)Num(name, args, at + 1, line, col);
            }
            else if (Matches(word, "statelevels"))
            {
                levels = ToDoubles(name, args[at + 1], line, col);
            }
            else
            {
                throw new JgsRuntimeException(line, col, $"{name} does not know the option '{word}'.");
            }
        }

        var t = new double[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            t[i] = i / fs;
        }

        levels ??= DefaultStateLevels(x);
        double lowerBound = BilevelWaveforms.Reference(levels, 10);
        double upperBound = BilevelWaveforms.Reference(levels, 90);
        double lowerRef = BilevelWaveforms.Reference(levels, 50);
        double midRef = BilevelWaveforms.Reference(levels, 50.5);
        double upperRef = BilevelWaveforms.Reference(levels, 51);
        BilevelWaveforms.Transitions transitions = BilevelWaveforms.FindTransitions(
            x, t, upperBound, lowerBound, upperRef, midRef, lowerRef);

        var rising = new List<double>();
        var falling = new List<double>();
        for (int i = 0; i < transitions.Polarity.Length; i++)
        {
            (transitions.Polarity[i] > 0 ? rising : falling).Add(transitions.LowerCross[i]);
        }

        int pulses = System.Math.Min(rising.Count, falling.Count);
        if (pulses < 3)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} needs at least three tachometer pulses.");
        }

        var centres = new double[pulses];
        for (int i = 0; i < pulses; i++)
        {
            centres[i] = (rising[i] + falling[i]) / 2;
        }

        var pulseRpm = new double[pulses - 1];
        var pulseTimes = new double[pulses - 1];
        for (int i = 0; i < pulses - 1; i++)
        {
            pulseRpm[i] = 60 / perRevolution / (centres[i + 1] - centres[i]);
            pulseTimes[i] = (centres[i] + centres[i + 1]) / 2;
        }

        var outputTimes = new List<double>();
        for (double at = 0; at < x.Length - (fs / outputRate) + (fs / outputRate / 2); at += fs / outputRate)
        {
            if (outputTimes.Count >= (int)System.Math.Round(x.Length * outputRate / fs))
            {
                break;
            }

            outputTimes.Add(at / fs);
        }

        double[] times = [.. outputTimes];
        double[] rpm = smooth
            ? SmoothFit(pulseTimes, pulseRpm, fitPoints, times)
            : RotationMaps.Interpolate(pulseTimes, pulseRpm, times);

        var results = new List<JgsValue>
        {
            ColumnOfDoubles(rpm),
            ColumnOfDoubles(times),
            ColumnOfDoubles(centres),
        };
        return [.. results.Take(System.Math.Max(wanted, 1))];
    }

    /// <summary>
    /// MATLAB's smooth fit: a least-squares cubic spline through <paramref name="points"/> knots
    /// spread evenly over the pulse times, evaluated wherever the caller asked.
    /// </summary>
    private static double[] SmoothFit(double[] t, double[] y, int points, double[] at)
    {
        var knots = new double[points];
        for (int i = 0; i < points; i++)
        {
            knots[i] = t[0] + ((t[^1] - t[0]) * i / (points - 1.0));
        }

        // Each column of the design matrix is the cardinal spline that is one at a single knot,
        // sampled at the pulse times; the fit is then a plain least-squares problem in the knots.
        var design = new System.Numerics.Complex[t.Length, points];
        for (int k = 0; k < points; k++)
        {
            var cardinal = new double[points];
            cardinal[k] = 1;
            double[] slopes = Interpolation.SplineSlopes(knots, cardinal);
            for (int i = 0; i < t.Length; i++)
            {
                design[i, k] = SplineAt(knots, cardinal, slopes, t[i]);
            }
        }

        var target = new System.Numerics.Complex[t.Length, 1];
        for (int i = 0; i < t.Length; i++)
        {
            target[i, 0] = y[i];
        }

        // The cardinal-spline design is ill conditioned when the speed barely varies, and a rank
        // cut at the default tolerance would drop a knot and bend the fit; there is nothing
        // degenerate here to protect against, so nothing is cut.
        System.Numerics.Complex[,] solved = HouseholderQr.BasicSolution(design, target, 0, out _);
        var heights = new double[points];
        for (int i = 0; i < points; i++)
        {
            heights[i] = solved[i, 0].Real;
        }

        double[] fitted = Interpolation.SplineSlopes(knots, heights);
        var values = new double[at.Length];
        for (int i = 0; i < at.Length; i++)
        {
            values[i] = SplineAt(knots, heights, fitted, at[i]);
        }

        return values;
    }

    /// <summary>A cubic Hermite spline evaluated from its knots, heights and slopes.</summary>
    private static double SplineAt(double[] x, double[] y, double[] slopes, double at)
    {
        int i = 0;
        int high = x.Length - 2;
        while (i < high && x[i + 1] <= at)
        {
            i++;
        }

        double h = x[i + 1] - x[i];
        double s = at - x[i];
        double delta = (y[i + 1] - y[i]) / h;
        double c = ((3 * delta) - (2 * slopes[i]) - slopes[i + 1]) / h;
        double d = (slopes[i] + slopes[i + 1] - (2 * delta)) / (h * h);
        return y[i] + (s * (slopes[i] + (s * (c + (s * d)))));
    }

    /// <summary><c>[map, bins, rpm, time, res] = rpmfreqmap(x, fs, rpm, ...)</c>.</summary>
    private static JgsValue[] RotationMapOf(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 3, 12, line, col);
        double[] x = ToDoubles(name, args[0], line, col);
        double fs = Num(name, args, 1, line, col);
        double[] rpm = ToDoubles(name, args[2], line, col);
        if (x.Length != rpm.Length)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} needs a speed for every sample of the signal.");
        }

        if (x.Length < 18)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs at least eighteen samples.");
        }

        bool order = name == "rpmordermap";
        double? resolution = null;
        int at = 3;
        if (args.Count > at && !IsTextScalar(args[at]))
        {
            resolution = Num(name, args, at, line, col);
            at++;
        }

        var amplitude = MapAmplitude.Rms;
        bool decibels = false;
        double overlap = 50;
        string windowName = order ? "flattopwin" : "hann";
        double? windowParameter = null;
        for (; at < args.Count; at += 2)
        {
            string word = StrOf(name, args[at], line, col).ToLowerInvariant();
            if (at + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs a value after '{word}'.");
            }

            if (Matches(word, "amplitude"))
            {
                string kind = StrOf(name, args[at + 1], line, col).ToLowerInvariant();
                amplitude = Matches(kind, "rms") ? MapAmplitude.Rms
                    : Matches(kind, "peak") ? MapAmplitude.Peak
                    : Matches(kind, "power") ? MapAmplitude.Power
                    : throw new JgsRuntimeException(line, col,
                        $"{name} takes 'rms', 'peak' or 'power' as its amplitude.");
            }
            else if (Matches(word, "scale"))
            {
                string kind = StrOf(name, args[at + 1], line, col).ToLowerInvariant();
                decibels = kind == "db";
                if (!decibels && !Matches(kind, "linear"))
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name} takes 'linear' or 'dB' as its scale.");
                }
            }
            else if (Matches(word, "overlappercent"))
            {
                overlap = Num(name, args, at + 1, line, col);
            }
            else if (Matches(word, "window"))
            {
                (windowName, windowParameter) = ReadWindowName(name, args[at + 1], line, col);
            }
            else
            {
                throw new JgsRuntimeException(line, col, $"{name} does not know the option '{word}'.");
            }
        }

        RotationMap map = RotationMaps.Compute(
            x, fs, rpm, order ? RotationAxis.Order : RotationAxis.Frequency,
            resolution, overlap, amplitude, decibels,
            n => NamedWindow(windowName, n, windowParameter));

        var results = new List<JgsValue>
        {
            MapMatrix(map.Values),
            ColumnOfDoubles(map.Bins),
            ColumnOfDoubles(map.Rpm),
            ColumnOfDoubles(map.Times),
            JgsValue.Number(map.Resolution),
        };
        return [.. results.Take(System.Math.Max(wanted, 1))];
    }

    /// <summary><c>[spec, order] = orderspectrum(map, order)</c> or <c>(x, fs, rpm)</c>.</summary>
    private static JgsValue[] OrderSpectrum(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 2, 4, line, col);
        double[][] map;
        double[] orders;
        var amplitude = MapAmplitude.Rms;
        if (args.Count == 3 && ElementCount(args[1]) == 1)
        {
            double[] x = ToDoubles(name, args[0], line, col);
            double fs = Num(name, args, 1, line, col);
            double[] rpm = ToDoubles(name, args[2], line, col);
            RotationMap computed = RotationMaps.Compute(
                x, fs, rpm, RotationAxis.Order, null, 50, MapAmplitude.Rms, false,
                n => NamedWindow("flattopwin", n, null));
            map = computed.Values;
            orders = computed.Bins;
        }
        else
        {
            (map, orders) = ReadMap(name, args[0], args[1], line, col);
            if (args.Count == 4)
            {
                string word = StrOf(name, args[2], line, col).ToLowerInvariant();
                if (!Matches(word, "amplitude"))
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name} does not know the option '{word}'.");
                }

                string kind = StrOf(name, args[3], line, col).ToLowerInvariant();
                amplitude = Matches(kind, "rms") ? MapAmplitude.Rms
                    : Matches(kind, "peak") ? MapAmplitude.Peak
                    : Matches(kind, "power") ? MapAmplitude.Power
                    : throw new JgsRuntimeException(line, col,
                        $"{name} takes 'rms', 'peak' or 'power' as its amplitude.");
            }
        }

        int bins = orders.Length;
        var spectrum = new double[bins];
        for (int i = 0; i < bins; i++)
        {
            double sum = 0;
            foreach (double[] frame in map)
            {
                sum += amplitude == MapAmplitude.Power ? frame[i] : frame[i] * frame[i];
            }

            sum /= map.Length;
            spectrum[i] = amplitude == MapAmplitude.Power ? sum : System.Math.Sqrt(sum);
        }

        return wanted <= 1
            ? [ColumnOfDoubles(spectrum)]
            : [ColumnOfDoubles(spectrum), ColumnOfDoubles(orders)];
    }

    /// <summary>
    /// <c>[mag, rpm, time] = ordertrack(map, order, rpm, time, orderlist)</c> and its signal form.
    /// </summary>
    private static JgsValue[] OrderTrack(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 4, 15, line, col);
        double[][] map;
        double[] orders;
        double[] rpm;
        double[] times;
        double[] wantedOrders;
        if (ElementCount(args[1]) == 1)
        {
            double[] x = ToDoubles(name, args[0], line, col);
            double fs = Num(name, args, 1, line, col);
            double[] speed = ToDoubles(name, args[2], line, col);
            wantedOrders = ToDoubles(name, args[3], line, col);
            var amplitude = MapAmplitude.Rms;
            bool decibels = false;
            for (int at = 4; at < args.Count; at += 2)
            {
                string word = StrOf(name, args[at], line, col).ToLowerInvariant();
                if (at + 1 >= args.Count)
                {
                    throw new JgsRuntimeException(line, col, $"{name} needs a value after '{word}'.");
                }

                if (Matches(word, "amplitude"))
                {
                    string kind = StrOf(name, args[at + 1], line, col).ToLowerInvariant();
                    amplitude = Matches(kind, "rms") ? MapAmplitude.Rms
                        : Matches(kind, "peak") ? MapAmplitude.Peak
                        : Matches(kind, "power") ? MapAmplitude.Power
                        : throw new JgsRuntimeException(line, col,
                            $"{name} takes 'rms', 'peak' or 'power' as its amplitude.");
                }
                else if (Matches(word, "scale"))
                {
                    decibels = StrOf(name, args[at + 1], line, col).ToLowerInvariant() == "db";
                }
                else if (Matches(word, "bandwidth") || Matches(word, "segmentlength")
                    || Matches(word, "decouple"))
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name} in this build has no Vold-Kalman filter, which is what those " +
                        "options select; the resampling track is the one written here.");
                }
                else
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name} does not know the option '{word}'.");
                }
            }

            RotationMap computed = RotationMaps.Compute(
                x, fs, speed, RotationAxis.Order, null, 50, amplitude, decibels,
                n => NamedWindow("flattopwin", n, null));
            map = computed.Values;
            orders = computed.Bins;
            rpm = computed.Rpm;
            times = computed.Times;
        }
        else
        {
            (map, orders) = ReadMap(name, args[0], args[1], line, col);
            rpm = ToDoubles(name, args[2], line, col);
            times = ToDoubles(name, args[3], line, col);
            if (args.Count < 5)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs a list of orders to track.");
            }

            wantedOrders = ToDoubles(name, args[4], line, col);
        }

        var magnitudes = new double[wantedOrders.Length * map.Length];
        for (int i = 0; i < wantedOrders.Length; i++)
        {
            int at = 0;
            double best = double.PositiveInfinity;
            for (int b = 0; b < orders.Length; b++)
            {
                double distance = System.Math.Abs(orders[b] - wantedOrders[i]);
                if (distance < best)
                {
                    best = distance;
                    at = b;
                }
            }

            for (int c = 0; c < map.Length; c++)
            {
                magnitudes[i + (c * wantedOrders.Length)] = map[c][at];
            }
        }

        var results = new List<JgsValue>
        {
            JgsMatrix.FromColumnMajor(magnitudes, wantedOrders.Length, map.Length),
            RowOfDoubles(rpm),
            RowOfDoubles(times),
        };
        return [.. results.Take(System.Math.Max(wanted, 1))];
    }

    /// <summary><c>xrec = orderwaveform(x, fs, rpm, orderlist, refidx, ...)</c>.</summary>
    private static JgsValue[] OrderWaveform(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 4, 13, line, col);
        double[] x = ToDoubles(name, args[0], line, col);
        double fs = Num(name, args, 1, line, col);
        (double[][] speeds, int rows) = SpeedColumns(name, args[2], line, col);
        double[] orders = ToDoubles(name, args[3], line, col);
        if (rows != x.Length)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} needs a speed for every sample of the signal.");
        }

        var reference = new int[orders.Length];
        int at = 4;
        if (args.Count > at && !IsTextScalar(args[at]))
        {
            double[] given = ToDoubles(name, args[at], line, col);
            for (int i = 0; i < orders.Length; i++)
            {
                reference[i] = (int)given[i % given.Length] - 1;
            }

            at++;
        }

        double[] bandwidths = [0.01 * fs];
        int segment = x.Length;
        bool decouple = false;
        int filterOrder = 1;
        for (; at < args.Count; at += 2)
        {
            string word = StrOf(name, args[at], line, col).ToLowerInvariant();
            if (at + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs a value after '{word}'.");
            }

            if (Matches(word, "bandwidth"))
            {
                bandwidths = ToDoubles(name, args[at + 1], line, col);
            }
            else if (Matches(word, "segmentlength"))
            {
                segment = (int)Num(name, args, at + 1, line, col);
            }
            else if (Matches(word, "decouple"))
            {
                decouple = Num(name, args, at + 1, line, col) != 0;
            }
            else if (Matches(word, "filterorder"))
            {
                filterOrder = (int)Num(name, args, at + 1, line, col);
                if (filterOrder is not 1 and not 2)
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name} takes a filter order of one or two.");
                }
            }
            else
            {
                throw new JgsRuntimeException(line, col, $"{name} does not know the option '{word}'.");
            }
        }

        var frequencies = new double[orders.Length][];
        var weights = new double[orders.Length];
        for (int i = 0; i < orders.Length; i++)
        {
            frequencies[i] = new double[x.Length];
            double[] speed = speeds[reference[i] % speeds.Length];
            for (int k = 0; k < x.Length; k++)
            {
                frequencies[i][k] = orders[i] * speed[k] / 60;
            }

            double bandwidth = bandwidths[i % bandwidths.Length];
            weights[i] = filterOrder == 1
                ? System.Math.Pow(1.58 * fs / (bandwidth * 2 * System.Math.PI), 2)
                : System.Math.Pow(1.7 * fs / (bandwidth * 2 * System.Math.PI), 3);
        }

        var reconstructed = new double[orders.Length][];
        if (decouple)
        {
            (reconstructed, _) = VoldKalman.Extract(x, fs, frequencies, weights, filterOrder, segment);
        }
        else
        {
            for (int i = 0; i < orders.Length; i++)
            {
                (double[][] one, _) = VoldKalman.Extract(
                    x, fs, [frequencies[i]], [weights[i]], filterOrder, segment);
                reconstructed[i] = one[0];
            }
        }

        var flat = new double[x.Length * orders.Length];
        for (int i = 0; i < orders.Length; i++)
        {
            Array.Copy(reconstructed[i], 0, flat, i * x.Length, x.Length);
        }

        _ = wanted;
        return [JgsMatrix.FromColumnMajor(flat, x.Length, orders.Length)];
    }

    /// <summary>A speed signal read as one column per shaft.</summary>
    private static (double[][] Columns, int Rows) SpeedColumns(
        string name, JgsValue value, int line, int col)
    {
        double[] flat = ToDoubles(name, value, line, col);
        int[] dims = SizeDims(value);
        int rows = dims.Length > 0 ? dims[0] : 1;
        int columns = flat.Length / System.Math.Max(rows, 1);
        if (rows == 1)
        {
            (rows, columns) = (columns, 1);
        }

        var speeds = new double[columns][];
        for (int c = 0; c < columns; c++)
        {
            speeds[c] = new double[rows];
            Array.Copy(flat, c * rows, speeds[c], 0, rows);
        }

        return (speeds, rows);
    }

    // --- Shared readers -----------------------------------------------------------------------------

    /// <summary>A map given as a matrix, read as one array per frame.</summary>
    private static (double[][] Map, double[] Bins) ReadMap(
        string name, JgsValue value, JgsValue axis, int line, int col)
    {
        double[] flat = ToDoubles(name, value, line, col);
        double[] bins = ToDoubles(name, axis, line, col);
        int rows = bins.Length;
        if (rows < 2 || flat.Length % rows != 0)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} needs one axis value for every row of the map.");
        }

        int frames = flat.Length / rows;
        var map = new double[frames][];
        for (int c = 0; c < frames; c++)
        {
            map[c] = new double[rows];
            Array.Copy(flat, c * rows, map[c], 0, rows);
        }

        return (map, bins);
    }

    /// <summary>A window named on its own or with its parameter.</summary>
    private static (string Name, double? Parameter) ReadWindowName(
        string name, JgsValue value, int line, int col)
    {
        if (IsTextScalar(value))
        {
            return (StrOf(name, value, line, col).ToLowerInvariant(), null);
        }

        if (value.Type != JgsType.Cell)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} takes a window name, or a cell holding a name and its parameter.");
        }

        JgsValue[] parts = value.AsCell;
        string given = StrOf(name, parts[0], line, col).ToLowerInvariant();
        return parts.Length > 1 ? (given, ToDoubles(name, parts[1], line, col)[0]) : (given, null);
    }

    /// <summary>The six windows a rotation map may be asked for.</summary>
    private static double[] NamedWindow(string name, int length, double? parameter) => name switch
    {
        "kaiser" => SignalWindows.Kaiser(length, parameter ?? 0.5),
        "chebwin" => SignalWindows.Chebyshev(length, parameter ?? 100),
        "hamming" => SignalWindows.Hamming(length),
        "flattopwin" => SignalWindows.FlatTop(length),
        "rectwin" => RectangularWindow(length),
        _ => SignalWindows.Hann(length),
    };

    /// <summary>A map as a matrix: bins down the rows, frames across the columns.</summary>
    private static JgsValue MapMatrix(double[][] frames)
    {
        int bins = frames.Length == 0 ? 0 : frames[0].Length;
        var flat = new double[bins * frames.Length];
        for (int c = 0; c < frames.Length; c++)
        {
            Array.Copy(frames[c], 0, flat, c * bins, bins);
        }

        return JgsMatrix.FromColumnMajor(flat, bins, frames.Length);
    }
}
