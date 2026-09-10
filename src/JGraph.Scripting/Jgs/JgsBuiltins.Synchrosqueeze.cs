using System.Numerics;
using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Signal;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The synchrosqueezed transform and the ridges read off a time–frequency map (M137):
/// <c>fsst</c>, <c>ifsst</c> and <c>tfridge</c>.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the synchrosqueezing names.</summary>
    internal static void RegisterSynchrosqueezeBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        void Many(string name, Func<string, IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> body) =>
            Define(name,
                (args, line, col) => First(body(name, args, 1, line, col)),
                (args, wanted, line, col) => body(name, args, wanted, line, col));

        Many("fsst", SqueezedTransform);
        Many("ifsst", SqueezedInverse);
        Many("tfridge", TimeFrequencyRidges);
    }

    /// <summary><c>[sst, f, t] = fsst(x, fs, window, freqloc)</c>.</summary>
    private static JgsValue[] SqueezedTransform(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 1, 4, line, col);
        (Complex[][] channels, _, _) = SpectralChannels(name, args[0], line, col);
        if (channels.Length != 1)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a vector signal.");
        }

        Complex[] x = channels[0];
        if (x.Length < 2)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs at least two samples.");
        }

        double? fs = null;
        double[]? window = null;
        int at = 1;
        if (args.Count > at && !IsTextScalar(args[at]))
        {
            if (ElementCount(args[at]) > 0)
            {
                fs = Num(name, args, at, line, col);
                if (!(fs > 0) || !double.IsFinite(fs.Value))
                {
                    throw new JgsRuntimeException(line, col, $"{name} needs a positive sample rate.");
                }
            }

            at++;
        }

        if (args.Count > at && !IsTextScalar(args[at]))
        {
            if (ElementCount(args[at]) > 0)
            {
                double[] given = ToDoubles(name, args[at], line, col);
                window = given.Length == 1 ? SignalWindows.Kaiser((int)given[0], 10) : given;
            }

            at++;
        }

        for (; at < args.Count; at++)
        {
            string word = StrOf(name, args[at], line, col).ToLowerInvariant();
            if (!Matches(word, "xaxis") && !Matches(word, "yaxis"))
            {
                throw new JgsRuntimeException(line, col, $"{name} does not know the option '{word}'.");
            }
        }

        window ??= SignalWindows.Kaiser(System.Math.Min(256, x.Length), 10);
        if (window.Length < 2)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a window of at least two samples.");
        }

        if (window.Length > x.Length)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a window no longer than the signal.");
        }

        bool normalised = fs is null;
        SqueezedAnswer answer = Synchrosqueezing.Squeeze(
            x, window, fs ?? (2 * System.Math.PI), normalised);
        if (wanted == 0)
        {
            DrawSqueezed(answer, !normalised);
            return [];
        }

        JgsValue map = FrameMatrix(answer.Values, downRows: false);
        if (wanted <= 1)
        {
            return [map];
        }

        return wanted == 2
            ? [map, ColumnOfDoubles(answer.Frequencies)]
            : [map, ColumnOfDoubles(answer.Frequencies), RowOfDoubles(answer.Times)];
    }

    /// <summary>
    /// <c>xrec = ifsst(sst)</c>, and its three narrowing forms: a window, a set of ridge indices
    /// with a band around each, or a frequency vector and the range to keep out of it.
    /// </summary>
    private static JgsValue[] SqueezedInverse(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 1, 5, line, col);
        Complex[][] sst = ShortTimeMap(name, args[0], downRows: false, line, col);
        int[] shape = SizeDims(args[0]);
        if (shape.Length < 2 || shape[0] < 2 || shape[1] < 2)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a time-frequency matrix.");
        }

        double[] window = args.Count > 1 && ElementCount(args[1]) > 0 && !IsTextScalar(args[1])
            ? WindowOrLength(name, args[1], line, col)
            : SignalWindows.Kaiser(System.Math.Min(256, sst.Length), 10);
        int nfft = window.Length;
        bool real = sst[0].Length != nfft;

        int[][]? ridges = null;
        double[]? frequencies = null;
        double[]? range = null;
        int bins = 4;
        if (args.Count > 2 && !IsTextScalar(args[2]))
        {
            if (args.Count > 3 && !IsTextScalar(args[3]))
            {
                frequencies = ToDoubles(name, args[2], line, col);
                range = ToDoubles(name, args[3], line, col);
            }
            else
            {
                ridges = RidgeColumns(name, args[2], line, col);
            }
        }

        for (int i = 3; i < args.Count; i++)
        {
            if (!IsTextScalar(args[i]))
            {
                continue;
            }

            string word = StrOf(name, args[i], line, col).ToLowerInvariant();
            if (!Matches(word, "numfrequencybins") || i + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col, $"{name} does not know the option '{word}'.");
            }

            bins = (int)Num(name, args, i + 1, line, col);
            i++;
        }

        if (ridges is not null)
        {
            var columns = new Complex[ridges.Length][];
            for (int r = 0; r < ridges.Length; r++)
            {
                int[] path = ridges[r];
                columns[r] = Synchrosqueezing.Invert(sst, window, real, nfft,
                    (row, frame) => row >= path[frame] - 1 - bins && row <= path[frame] - 1 + bins);
            }

            var flat = new Complex[sst.Length * ridges.Length];
            for (int r = 0; r < ridges.Length; r++)
            {
                for (int c = 0; c < sst.Length; c++)
                {
                    flat[c + (r * sst.Length)] = columns[r][c];
                }
            }

            return [ComplexShaped(flat, [sst.Length, ridges.Length])];
        }

        if (frequencies is not null && range is not null)
        {
            int low = Nearest(frequencies, range[0]);
            int high = Nearest(frequencies, range[range.Length - 1]);
            return [ComplexShaped(
                Synchrosqueezing.Invert(sst, window, real, nfft, (row, _) => row >= low && row <= high),
                [sst.Length, 1])];
        }

        return [ComplexShaped(
            Synchrosqueezing.Invert(sst, window, real, nfft, null), [sst.Length, 1])];
    }

    /// <summary>
    /// <c>[fridge, iridge, lridge] = tfridge(tfm, f, penalty, 'NumRidges', n, ...)</c>.
    /// </summary>
    private static JgsValue[] TimeFrequencyRidges(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 2, 7, line, col);
        Complex[][] tfm = ShortTimeMap(name, args[0], downRows: false, line, col);
        double[] f = ToDoubles(name, args[1], line, col);
        int rows = tfm.Length == 0 ? 0 : tfm[0].Length;
        if (f.Length != rows)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} needs one frequency for every row of the map.");
        }

        double penalty = 0;
        int at = 2;
        if (args.Count > at && !IsTextScalar(args[at]))
        {
            penalty = Num(name, args, at, line, col);
            if (!(penalty >= 0) || !double.IsFinite(penalty))
            {
                throw new JgsRuntimeException(line, col, $"{name} needs a non-negative penalty.");
            }

            at++;
        }

        int count = 1;
        int bins = 4;
        for (; at < args.Count; at += 2)
        {
            string word = StrOf(name, args[at], line, col).ToLowerInvariant();
            if (at + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs a value after '{word}'.");
            }

            if (Matches(word, "numridges"))
            {
                count = (int)Num(name, args, at + 1, line, col);
            }
            else if (Matches(word, "numfrequencybins"))
            {
                bins = (int)Num(name, args, at + 1, line, col);
            }
            else
            {
                throw new JgsRuntimeException(line, col, $"{name} does not know the option '{word}'.");
            }
        }

        if (count < 1)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs at least one ridge.");
        }

        int[][] paths = Synchrosqueezing.Ridges(tfm, penalty, count, bins);
        int columns = tfm.Length;
        var ridgeFrequencies = new double[columns * count];
        var indices = new double[columns * count];
        var linear = new double[columns * count];
        for (int r = 0; r < count; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                ridgeFrequencies[c + (r * columns)] = f[paths[r][c]];
                indices[c + (r * columns)] = paths[r][c] + 1;
                linear[c + (r * columns)] = paths[r][c] + 1 + (c * rows);
            }
        }

        JgsValue first = JgsMatrix.FromColumnMajor(ridgeFrequencies, columns, count);
        if (wanted <= 1)
        {
            return [first];
        }

        JgsValue second = JgsMatrix.FromColumnMajor(indices, columns, count);
        return wanted == 2
            ? [first, second]
            : [first, second, JgsMatrix.FromColumnMajor(linear, columns, count)];
    }

    // --- Shared readers -----------------------------------------------------------------------------

    /// <summary>A window given either as its samples or as the length of a Kaiser one.</summary>
    private static double[] WindowOrLength(string name, JgsValue value, int line, int col)
    {
        double[] given = ToDoubles(name, value, line, col);
        return given.Length == 1 ? SignalWindows.Kaiser((int)given[0], 10) : given;
    }

    /// <summary>Ridge indices as one path per column.</summary>
    private static int[][] RidgeColumns(string name, JgsValue value, int line, int col)
    {
        double[] flat = ToDoubles(name, value, line, col);
        int[] dims = SizeDims(value);
        int rows = dims.Length > 0 ? dims[0] : 1;
        int columns = flat.Length / System.Math.Max(rows, 1);
        if (rows == 1)
        {
            (rows, columns) = (columns, 1);
        }

        var paths = new int[columns][];
        for (int c = 0; c < columns; c++)
        {
            paths[c] = new int[rows];
            for (int i = 0; i < rows; i++)
            {
                paths[c][i] = (int)flat[i + (c * rows)];
            }
        }

        return paths;
    }

    /// <summary>The index of the entry nearest a given value.</summary>
    private static int Nearest(double[] values, double target)
    {
        int at = 0;
        double best = double.PositiveInfinity;
        for (int i = 0; i < values.Length; i++)
        {
            double distance = System.Math.Abs(values[i] - target);
            if (distance < best)
            {
                best = distance;
                at = i;
            }
        }

        return at;
    }

    /// <summary>The image <c>fsst</c> draws when nothing asked for its numbers.</summary>
    private static void DrawSqueezed(SqueezedAnswer answer, bool inHertz)
    {
        int nt = answer.Values.Length;
        int nf = nt == 0 ? 0 : answer.Values[0].Length;
        var image = new double[nf, nt];
        for (int c = 0; c < nt; c++)
        {
            for (int i = 0; i < nf; i++)
            {
                image[i, c] = answer.Values[c][i].Magnitude;
            }
        }

        AxesModel axes = JG.Gca();
        if (nf > 0 && nt > 0)
        {
            axes.AddImage(
                image,
                new DataRange(answer.Times[0], answer.Times[nt - 1]),
                new DataRange(answer.Frequencies[0], answer.Frequencies[nf - 1]));
        }

        axes.PrimaryXAxis.Label = "Time (s)";
        axes.PrimaryYAxis.Label = inHertz ? "Frequency (Hz)" : "Normalized frequency (rad/sample)";
    }
}
