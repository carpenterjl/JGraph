using System.Numerics;
using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Signal;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The experimental modal analysis of M137: <c>modalfrf</c>, <c>modalfit</c> and <c>modalsd</c>.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the modal names.</summary>
    internal static void RegisterModalBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        void Many(string name, Func<string, IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> body) =>
            Define(name,
                (args, line, col) => First(body(name, args, 1, line, col)),
                (args, wanted, line, col) => body(name, args, wanted, line, col));

        Many("modalfrf", ModalFrequencyResponse);
        Many("modalfit", ModalFitting);
        Many("modalsd", ModalStabilisation);
    }

    /// <summary><c>[frf, f, coh] = modalfrf(x, y, fs, window, noverlap, ...)</c>.</summary>
    private static JgsValue[] ModalFrequencyResponse(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 4, 15, line, col);
        (Complex[][] inputs, bool realX, _) = SpectralChannels(name, args[0], line, col);
        (Complex[][] outputs, bool realY, _) = SpectralChannels(name, args[1], line, col);
        double fs = Num(name, args, 2, line, col);
        double[] given = ToDoubles(name, args[3], line, col);
        double[] window = given.Length == 1 ? RectangularWindow((int)given[0]) : given;
        int overlap = 0;
        int at = 4;
        if (args.Count > at && !IsTextScalar(args[at]))
        {
            overlap = (int)Num(name, args, at, line, col);
            at++;
        }

        var sensor = ModalSensor.Acceleration;
        bool secondEstimator = false;
        bool hv = false;
        string measurement = "fixed";
        for (; at < args.Count; at += 2)
        {
            string word = StrOf(name, args[at], line, col).ToLowerInvariant();
            if (at + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs a value after '{word}'.");
            }

            if (Matches(word, "sensor"))
            {
                string kind = StrOf(name, args[at + 1], line, col).ToLowerInvariant();
                sensor = Matches(kind, "displacement") ? ModalSensor.Displacement
                    : Matches(kind, "velocity") ? ModalSensor.Velocity
                    : ModalSensor.Acceleration;
                if (!Matches(kind, "displacement") && !Matches(kind, "velocity")
                    && !Matches(kind, "acceleration"))
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name} takes 'displacement', 'velocity' or 'acceleration' as its sensor.");
                }
            }
            else if (Matches(word, "estimator"))
            {
                string kind = StrOf(name, args[at + 1], line, col).ToLowerInvariant();
                secondEstimator = kind == "h2";
                hv = kind == "hv";
                if (kind == "subspace")
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name} in this build has no subspace estimator: MATLAB's rests on the " +
                        "Control System Toolbox's subspace identification, which is not written here.");
                }

                if (kind != "h1" && kind != "h2" && kind != "hv")
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name} takes 'H1', 'H2' or 'Hv' as its estimator.");
                }
            }
            else if (Matches(word, "measurement"))
            {
                string kind = StrOf(name, args[at + 1], line, col).ToLowerInvariant();
                measurement = Matches(kind, "rovinginput") ? "rovinginput"
                    : Matches(kind, "rovingoutput") ? "rovingoutput"
                    : "fixed";
            }
            else if (Matches(word, "order") || Matches(word, "feedthrough"))
            {
                continue;
            }
            else
            {
                throw new JgsRuntimeException(line, col, $"{name} does not know the option '{word}'.");
            }
        }

        bool real = realX && realY;
        bool fixedMeasurement = measurement == "fixed";
        var request = new SpectralRequest
        {
            Nfft = window.Length,
            SampleRate = fs,
            SampleRateGiven = true,
            Range = real ? SpectralRange.OneSided : SpectralRange.TwoSided,
            Window = window,
            Overlap = overlap,
            SecondEstimator = secondEstimator,
        };

        Complex[][] responses;
        double[] frequencies;
        int pageOut;
        int pageIn;
        if (hv)
        {
            if (inputs.Length != outputs.Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name} needs the same number of inputs and outputs for the Hv estimator.");
            }

            SpectralAnswer cross = SpectralEstimators.Welch(outputs, inputs, real, request);
            SpectralAnswer yy = SpectralEstimators.Welch(outputs, null, real, request);
            SpectralAnswer xx = SpectralEstimators.Welch(inputs, null, real, request);
            responses = new Complex[cross.Values.Length][];
            for (int c = 0; c < responses.Length; c++)
            {
                responses[c] = new Complex[cross.Values[c].Length];
                for (int i = 0; i < responses[c].Length; i++)
                {
                    Complex gyx = cross.Values[c][i];
                    responses[c][i] = gyx / gyx.Magnitude
                        * System.Math.Sqrt(yy.Values[c][i].Real / xx.Values[c][i].Real);
                }
            }

            frequencies = cross.Frequencies;
            pageOut = responses.Length;
            pageIn = 1;
        }
        else if (fixedMeasurement)
        {
            SpectralAnswer answer = inputs.Length > 1 || outputs.Length > 1
                ? SpectralEstimators.MimoTransfer(inputs, outputs, real, request)
                : SpectralEstimators.TransferEstimate(inputs, outputs, real, request);
            responses = answer.Values;
            frequencies = answer.Frequencies;
            pageOut = outputs.Length;
            pageIn = inputs.Length;
        }
        else
        {
            if (inputs.Length != outputs.Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name} needs the same number of inputs and outputs for a roving measurement.");
            }

            SpectralAnswer answer = SpectralEstimators.TransferEstimate(inputs, outputs, real, request);
            responses = answer.Values;
            frequencies = answer.Frequencies;
            pageOut = measurement == "rovinginput" ? 1 : responses.Length;
            pageIn = measurement == "rovinginput" ? responses.Length : 1;
        }

        ModalAnalysis.ToDisplacement(responses, frequencies, sensor);
        var results = new List<JgsValue>
        {
            ComplexPages(responses, pageOut, pageIn),
            ColumnOfDoubles(frequencies),
        };
        if (wanted > 2)
        {
            SpectralAnswer coherence = fixedMeasurement && (inputs.Length > 1 || outputs.Length > 1)
                ? SpectralEstimators.MimoCoherence(inputs, outputs, real, request)
                : SpectralEstimators.Coherence(inputs, outputs, real, request);
            results.Add(SpectralRealMatrix(coherence.Values));
        }

        return [.. results.Take(System.Math.Max(wanted, 1))];
    }

    /// <summary><c>[fn, dr, ms, ofrf] = modalfit(frf, f, fs, mnum, ...)</c>.</summary>
    private static JgsValue[] ModalFitting(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 4, 14, line, col);
        (Complex[][] responses, int rows, int outputs, int inputs) =
            ModalResponses(name, args[0], line, col);
        double[] f = ToDoubles(name, args[1], line, col);
        double fs = Num(name, args, 2, line, col);
        int modes = (int)Num(name, args, 3, line, col);
        if (f.Length != rows)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} needs one frequency for every row of the response.");
        }

        var method = ModalFit.ComplexExponential;
        double[] range = [f[0], f[^1]];
        double[]? physical = null;
        int driveOut = 0;
        int driveIn = 0;
        for (int at = 4; at < args.Count; at += 2)
        {
            string word = StrOf(name, args[at], line, col).ToLowerInvariant();
            if (at + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs a value after '{word}'.");
            }

            if (Matches(word, "fitmethod"))
            {
                string kind = StrOf(name, args[at + 1], line, col).ToLowerInvariant();
                method = kind == "pp" ? ModalFit.PeakPicking : ModalFit.ComplexExponential;
                if (kind == "lsrf")
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name} in this build fits by 'pp' or 'lsce': MATLAB's 'lsrf' rests on the " +
                        "Control System Toolbox's rational fit, which is not written here.");
                }

                if (kind != "pp" && kind != "lsce")
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name} takes 'PP' or 'LSCE' as its fit method.");
                }
            }
            else if (Matches(word, "freqrange"))
            {
                range = ToDoubles(name, args[at + 1], line, col);
            }
            else if (Matches(word, "physfreq"))
            {
                physical = ElementCount(args[at + 1]) == 0
                    ? null
                    : ToDoubles(name, args[at + 1], line, col);
            }
            else if (Matches(word, "driveindex"))
            {
                double[] drive = ToDoubles(name, args[at + 1], line, col);
                driveOut = (int)drive[0] - 1;
                driveIn = (int)drive[1] - 1;
            }
            else if (Matches(word, "feedthrough") || Matches(word, "order"))
            {
                continue;
            }
            else
            {
                throw new JgsRuntimeException(line, col, $"{name} does not know the option '{word}'.");
            }
        }

        bool[] inside = ModalAnalysis.Inside(f, range);
        Complex[][] poles = ModalAnalysis.Poles(responses, f, fs, modes, method, range);
        if (physical is not null)
        {
            poles = Nearest(poles, physical);
        }

        var natural = new double[poles.Length * poles[0].Length];
        var damping = new double[natural.Length];
        for (int c = 0; c < poles.Length; c++)
        {
            for (int m = 0; m < poles[c].Length; m++)
            {
                (double fn, double dr) = ModalAnalysis.ToNaturalFrequency(poles[c][m]);
                natural[m + (c * poles[c].Length)] = fn;
                damping[m + (c * poles[c].Length)] = dr;
            }
        }

        JgsValue first = JgsMatrix.FromColumnMajor(natural, poles[0].Length, poles.Length);
        if (wanted <= 1)
        {
            return [first];
        }

        JgsValue second = JgsMatrix.FromColumnMajor(damping, poles[0].Length, poles.Length);
        if (wanted == 2)
        {
            return [first, second];
        }

        Complex[][] narrowed = Narrow(responses, inside);
        double[] band = Narrow(f, inside);
        Complex[][] residues = ModalAnalysis.Residues(narrowed, band, poles, method);
        Complex[][] shapes = ModalAnalysis.ModeShapes(residues, outputs, inputs, driveOut, driveIn);
        var flat = new Complex[shapes.Length * (shapes.Length == 0 ? 0 : shapes[0].Length)];
        int stations = shapes.Length == 0 ? 0 : shapes[0].Length;
        for (int m = 0; m < shapes.Length; m++)
        {
            for (int s = 0; s < stations; s++)
            {
                flat[s + (m * stations)] = shapes[m][s];
            }
        }

        JgsValue third = ComplexShaped(flat, [stations, shapes.Length]);
        if (wanted == 3)
        {
            return [first, second, third];
        }

        Complex[][] rebuilt = ModalAnalysis.Reconstruct(f, inside, residues, poles, method);
        return [first, second, third, ComplexPages(rebuilt, outputs, inputs)];
    }

    /// <summary><c>fn = modalsd(frf, f, fs, ...)</c>: the stabilisation diagram's stable frequencies.</summary>
    private static JgsValue[] ModalStabilisation(
        string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 3, 11, line, col);
        (Complex[][] responses, int rows, _, _) = ModalResponses(name, args[0], line, col);
        double[] f = ToDoubles(name, args[1], line, col);
        double fs = Num(name, args, 2, line, col);
        if (f.Length != rows)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} needs one frequency for every row of the response.");
        }

        double[] range = [f[0], f[^1]];
        double[] criteria = [0.01, 0.05];
        int most = 50;
        for (int at = 3; at < args.Count; at += 2)
        {
            string word = StrOf(name, args[at], line, col).ToLowerInvariant();
            if (at + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs a value after '{word}'.");
            }

            if (Matches(word, "freqrange"))
            {
                range = ToDoubles(name, args[at + 1], line, col);
            }
            else if (Matches(word, "scriteria"))
            {
                criteria = ToDoubles(name, args[at + 1], line, col);
            }
            else if (Matches(word, "maxmodes"))
            {
                most = (int)Num(name, args, at + 1, line, col);
            }
            else if (Matches(word, "fitmethod"))
            {
                string kind = StrOf(name, args[at + 1], line, col).ToLowerInvariant();
                if (kind != "lsce")
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name} in this build fits by 'lsce' only: MATLAB's 'lsrf' rests on the " +
                        "Control System Toolbox's rational fit, which is not written here.");
                }
            }
            else
            {
                throw new JgsRuntimeException(line, col, $"{name} does not know the option '{word}'.");
            }
        }

        bool[] inside = ModalAnalysis.Inside(f, range);
        int ceiling = ModalAnalysis.MaxModes(responses[0], f, fs, inside);
        most = System.Math.Min(most, System.Math.Max(ceiling, 1));

        var frequencies = new double[most][];
        var damping = new double[most][];
        // The diagram starts as zeros and only its last row is unknown; an entry stays zero when
        // no order ever reached that column, which is how MATLAB tells "not tried" from "not stable".
        var stable = new double[most * most];
        for (int c = 0; c < most; c++)
        {
            stable[most - 1 + (c * most)] = double.NaN;
        }
        for (int m = 1; m <= most; m++)
        {
            Complex[] poles = ModalAnalysis.Poles(
                responses, f, fs, m, ModalFit.ComplexExponential, range)[0];
            var fn = new double[m];
            var dr = new double[m];
            for (int i = 0; i < m; i++)
            {
                (fn[i], dr[i]) = ModalAnalysis.ToNaturalFrequency(poles[i]);
            }

            int[] order = SortedOrder(fn);
            frequencies[m - 1] = new double[m];
            damping[m - 1] = new double[m];
            for (int i = 0; i < m; i++)
            {
                frequencies[m - 1][i] = fn[order[i]];
                damping[m - 1][i] = dr[order[i]];
            }

            if (m == 1)
            {
                continue;
            }

            for (int i = 0; i < m - 1; i++)
            {
                double closest = double.PositiveInfinity;
                foreach (double candidate in frequencies[m - 1])
                {
                    // A pole that the fit could not find is padded with NaN, and MATLAB's `min`
                    // passes over those rather than letting one poison the comparison.
                    double distance = System.Math.Abs(frequencies[m - 2][i] - candidate);
                    if (!double.IsNaN(distance) && distance < closest)
                    {
                        closest = distance;
                    }
                }

                bool steady = closest < criteria[0] * frequencies[m - 2][i];
                stable[m - 2 + (i * most)] = steady ? frequencies[m - 2][i] : double.NaN;
            }
        }

        return wanted == 0 ? [] : [JgsMatrix.FromColumnMajor(stable, most, most)];
    }

    // --- Shapes -------------------------------------------------------------------------------------

    /// <summary>A frequency-response array read as one column per input-output pair.</summary>
    private static (Complex[][] Responses, int Rows, int Outputs, int Inputs) ModalResponses(
        string name, JgsValue value, int line, int col)
    {
        Complex[] flat = ComplexArrayOf(name, value, line, col);
        int[] dims = SizeDims(value);
        int rows = dims.Length > 0 ? dims[0] : 1;
        int outputs = dims.Length > 1 ? dims[1] : 1;
        int inputs = 1;
        for (int i = 2; i < dims.Length; i++)
        {
            inputs *= dims[i];
        }

        var responses = new Complex[outputs * inputs][];
        for (int c = 0; c < responses.Length; c++)
        {
            responses[c] = new Complex[rows];
            Array.Copy(flat, c * rows, responses[c], 0, rows);
        }

        return (responses, rows, outputs, inputs);
    }

    /// <summary>The poles nearest the physical frequencies the caller named.</summary>
    private static Complex[][] Nearest(Complex[][] poles, double[] physical)
    {
        var picked = new Complex[poles.Length][];
        for (int c = 0; c < poles.Length; c++)
        {
            picked[c] = new Complex[physical.Length];
            for (int p = 0; p < physical.Length; p++)
            {
                double best = double.PositiveInfinity;
                int at = 0;
                for (int m = 0; m < poles[c].Length; m++)
                {
                    (double fn, _) = ModalAnalysis.ToNaturalFrequency(poles[c][m]);
                    double distance = System.Math.Abs(fn - physical[p]);
                    if (distance < best)
                    {
                        best = distance;
                        at = m;
                    }
                }

                picked[c][p] = poles[c][at];
            }
        }

        return picked;
    }

    private static Complex[][] Narrow(Complex[][] responses, bool[] inside)
    {
        var narrowed = new Complex[responses.Length][];
        for (int c = 0; c < responses.Length; c++)
        {
            var kept = new List<Complex>();
            for (int i = 0; i < responses[c].Length; i++)
            {
                if (inside[i])
                {
                    kept.Add(responses[c][i]);
                }
            }

            narrowed[c] = [.. kept];
        }

        return narrowed;
    }

    private static double[] Narrow(double[] f, bool[] inside)
    {
        var kept = new List<double>();
        for (int i = 0; i < f.Length; i++)
        {
            if (inside[i])
            {
                kept.Add(f[i]);
            }
        }

        return [.. kept];
    }

    private static int[] SortedOrder(double[] values)
    {
        var index = new int[values.Length];
        for (int i = 0; i < index.Length; i++)
        {
            index[i] = i;
        }

        Array.Sort(index, (a, b) =>
        {
            bool left = double.IsNaN(values[a]);
            bool right = double.IsNaN(values[b]);
            if (left || right)
            {
                return left == right ? a.CompareTo(b) : left ? 1 : -1;
            }

            int compared = values[a].CompareTo(values[b]);
            return compared != 0 ? compared : a.CompareTo(b);
        });
        return index;
    }
}
