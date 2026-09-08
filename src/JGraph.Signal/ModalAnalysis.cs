using System;
using System.Numerics;
using JGraph.Numerics;
using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Signal;

/// <summary>What a measured frequency response is a response of.</summary>
public enum ModalSensor
{
    /// <summary>Dynamic flexibility: the response is already a displacement.</summary>
    Displacement,

    /// <summary>Mobility: a velocity, which is divided by <c>i·ω</c>.</summary>
    Velocity,

    /// <summary>Accelerance: an acceleration, which is divided by <c>−ω²</c>.</summary>
    Acceleration,
}

/// <summary>How <c>modalfit</c> finds the poles.</summary>
public enum ModalFit
{
    /// <summary>Peak picking: each response's own peaks, fitted one at a time.</summary>
    PeakPicking,

    /// <summary>Least-squares complex exponential: one set of poles for every response at once.</summary>
    ComplexExponential,
}

/// <summary>
/// The experimental modal analysis of M137: <c>modalfrf</c>'s frequency responses, and the poles,
/// residues and mode shapes <c>modalfit</c> and <c>modalsd</c> read off them.
/// </summary>
/// <remarks>
/// A structure's frequency response is a sum of one term per mode, each a pole and its conjugate.
/// Fitting is finding those poles: peak picking takes each resonance on its own and fits a
/// single-degree-of-freedom model to nine points around it, while the complex-exponential method
/// works in the time domain, where a sum of modes is a sum of decaying exponentials and their
/// common recurrence can be solved for in one least-squares problem.
/// </remarks>
public static class ModalAnalysis
{
    /// <summary>
    /// MATLAB's <c>toDynamicFlex</c>: a measured response divided down to a displacement, with the
    /// zero-frequency point taking its neighbour's factor so that nothing divides by nothing.
    /// </summary>
    public static void ToDisplacement(Complex[][] frf, double[] f, ModalSensor sensor)
    {
        ArgumentNullException.ThrowIfNull(frf);
        ArgumentNullException.ThrowIfNull(f);
        if (sensor == ModalSensor.Displacement || f.Length == 0)
        {
            return;
        }

        var factor = new Complex[f.Length];
        for (int i = 0; i < f.Length; i++)
        {
            double omega = 2 * System.Math.PI * f[i];
            factor[i] = sensor == ModalSensor.Acceleration
                ? -1 / (omega * omega)
                : 1 / new Complex(0, omega);
        }

        if (f.Length > 1)
        {
            factor[0] = factor[1];
        }

        foreach (Complex[] response in frf)
        {
            for (int i = 0; i < response.Length; i++)
            {
                response[i] *= factor[i];
            }
        }
    }

    /// <summary>The poles a fit finds, one column per response for peak picking and one in all for the other.</summary>
    public static Complex[][] Poles(
        Complex[][] frf, double[] f, double fs, int modes, ModalFit method, double[] range)
    {
        ArgumentNullException.ThrowIfNull(frf);
        ArgumentNullException.ThrowIfNull(f);
        ArgumentNullException.ThrowIfNull(range);

        bool[] inside = Inside(f, range);
        return method == ModalFit.PeakPicking
            ? PeakPicked(frf, f, modes, inside)
            : [ComplexExponential(frf, f, fs, modes, inside)];
    }

    /// <summary>
    /// MATLAB's peak-picking fit: nine points around each peak, and one single-degree-of-freedom
    /// model fitted to them by real least squares.
    /// </summary>
    private static Complex[][] PeakPicked(Complex[][] frf, double[] f, int modes, bool[] inside)
    {
        double[] kept = Where(f, inside);
        var poles = new Complex[frf.Length][];
        for (int r = 0; r < frf.Length; r++)
        {
            Complex[] response = WhereComplex(frf[r], inside);
            var decibels = new double[response.Length];
            for (int i = 0; i < response.Length; i++)
            {
                decibels[i] = 20 * System.Math.Log10(System.Math.Max(response[i].Magnitude, 1e-300));
            }

            var index = new double[decibels.Length];
            for (int i = 0; i < index.Length; i++)
            {
                index[i] = i + 1;
            }

            PeakFinding.Peaks found = PeakFinding.Find(decibels, index, new PeakFinding.Criteria
            {
                MaxCount = modes,
                MinProminence = 0.5,
                Order = PeakOrder.Descending,
            });

            // The natural frequency is the square root of a fitted coefficient, and nothing makes
            // that coefficient positive: a badly conditioned peak gives a negative one, whose root
            // MATLAB takes into the imaginary axis rather than calling it undefined. The imaginary
            // frequency then produces a real pole, which is a legitimate — heavily damped — answer.
            var natural = new Complex[modes];
            var damping = new Complex[modes];
            Array.Fill(natural, Complex.NaN);
            Array.Fill(damping, Complex.NaN);
            for (int m = 0; m < found.Indices.Length && m < modes; m++)
            {
                int at = found.Indices[m];
                int first = System.Math.Max(at - 4, 0);
                int last = System.Math.Min(at + 4, kept.Length - 1);
                int count = last - first + 1;
                var design = new Complex[count, 3];
                var target = new Complex[count, 1];
                for (int i = 0; i < count; i++)
                {
                    double omega = kept[first + i] * 2 * System.Math.PI;
                    Complex h = response[first + i];
                    target[i, 0] = omega * omega * h.Real;
                    design[i, 0] = h.Real;
                    design[i, 1] = (new Complex(0, 2 * omega) * h).Real;
                    design[i, 2] = -1;
                }

                Complex[,] solution = HouseholderQr.MinimumNormSolution(design, target, -1, out _);
                Complex root = Complex.Sqrt(solution[0, 0].Real);
                natural[m] = root / (2 * System.Math.PI);
                damping[m] = solution[1, 0].Real / root;
            }

            int[] order = Order(natural);
            var column = new Complex[modes];
            bool anyComplex = false;
            for (int m = 0; m < modes; m++)
            {
                column[m] = FromNaturalFrequency(natural[order[m]], damping[order[m]]);
                anyComplex |= column[m].Imaginary != 0;
            }


            // MATLAB writes this as `real(poles)<=0 & ~isreal(poles)`, and `isreal` asks whether the
            // whole array carries an imaginary part rather than whether one entry does. A real pole
            // therefore survives as long as some other pole in the same column is complex.
            for (int m = 0; m < modes; m++)
            {
                if (!(column[m].Real <= 0 && anyComplex))
                {
                    column[m] = Complex.NaN;
                }
            }

            poles[r] = column;
        }

        return poles;
    }

    /// <summary>
    /// MATLAB's least-squares complex-exponential fit: every response's impulse response stacked
    /// into one Hankel system, whose solution is the recurrence all the modes obey.
    /// </summary>
    private static Complex[] ComplexExponential(
        Complex[][] frf, double[] f, double fs, int modes, bool[] inside)
    {
        int count = Count(inside);
        double[] kept = Where(f, inside);
        double rate = count == f.Length ? fs : fs * count / f.Length;
        int order = 2 * modes;
        const int Oversample = 10;
        int rows = order * Oversample;
        var design = new Complex[rows * frf.Length, order];
        var target = new Complex[rows * frf.Length, 1];
        for (int r = 0; r < frf.Length; r++)
        {
            double[] impulse = ImpulseResponse(WhereComplex(frf[r], inside), kept, rate);
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < order; j++)
                {
                    design[(r * rows) + i, j] = impulse[i + j];
                }

                target[(r * rows) + i, 0] = -impulse[i + order];
            }
        }

        Complex[,] beta = HouseholderQr.BasicSolution(design, target, -1, out _);
        var polynomial = new Complex[order + 1];
        polynomial[0] = 1;
        for (int i = 0; i < order; i++)
        {
            polynomial[i + 1] = beta[order - 1 - i, 0].Real;
        }

        Complex[] roots = Polynomials.Roots(polynomial);
        var chosen = new List<Complex>();
        foreach (Complex root in roots)
        {
            // MATLAB keeps only the poles that come in conjugate pairs, which it does by
            // intersecting the list with the conjugates of its own lower half. The test is easier
            // on the roots than on their logarithms: a root with a positive imaginary part has its
            // conjugate in the list because the polynomial is real, while a negative real root has
            // a logarithm with an imaginary part of pi and no partner at all.
            if (root.Imaginary > 0 && root.Magnitude <= 1)
            {
                chosen.Add(Complex.Log(root) * rate);
            }
        }

        // MATLAB reaches this list through `intersect`, which sorts complex values by magnitude
        // and then by angle; the order is part of the answer because the poles come back in it.
        chosen.Sort((a, b) =>
        {
            int compared = a.Magnitude.CompareTo(b.Magnitude);
            return compared != 0 ? compared : a.Phase.CompareTo(b.Phase);
        });
        var answer = new Complex[modes];
        for (int i = 0; i < modes; i++)
        {
            answer[i] = i < chosen.Count ? chosen[i] : Complex.NaN;
        }

        if (count != f.Length || kept[0] != 0)
        {
            // The truncated band was fitted as though it started at zero, so every pole is short by
            // the offset and its damping ratio must be rescaled to the frequency it really sits at.
            for (int i = 0; i < answer.Length; i++)
            {
                if (Complex.IsNaN(answer[i]))
                {
                    continue;
                }

                (double natural, double damping) = ToNaturalFrequency(answer[i]);
                double shifted = natural + kept[0];
                answer[i] = FromNaturalFrequency(shifted, damping * natural / shifted);
            }
        }

        return answer;
    }

    /// <summary>MATLAB's <c>computeResidues</c>: the weight of every pole in every response.</summary>
    public static Complex[][] Residues(
        Complex[][] frf, double[] f, Complex[][] poles, ModalFit method)
    {
        ArgumentNullException.ThrowIfNull(frf);
        ArgumentNullException.ThrowIfNull(f);
        ArgumentNullException.ThrowIfNull(poles);

        int modes = poles[0].Length;
        var residues = new Complex[frf.Length][];
        for (int r = 0; r < frf.Length; r++)
        {
            Complex[] column = method == ModalFit.PeakPicking ? poles[r] : poles[0];
            var live = new List<int>();
            for (int m = 0; m < modes; m++)
            {
                if (!Complex.IsNaN(column[m]))
                {
                    live.Add(m);
                }
            }

            int k = live.Count;
            var design = new Complex[f.Length, (2 * k) + 2];
            var target = new Complex[f.Length, 1];
            for (int i = 0; i < f.Length; i++)
            {
                double omega = 2 * System.Math.PI * f[i];
                Complex j = new(0, omega);
                for (int m = 0; m < k; m++)
                {
                    design[i, m] = 1 / (j - column[live[m]]);
                    design[i, k + m] = 1 / (j - Complex.Conjugate(column[live[m]]));
                }

                design[i, 2 * k] = 1;
                design[i, (2 * k) + 1] = -1 / (omega * omega);
                target[i, 0] = frf[r][i];
            }

            if (f[0] < 1e-3 && f.Length > 1)
            {
                design[0, (2 * k) + 1] = design[1, (2 * k) + 1];
            }

            Complex[,] solution = HouseholderQr.MinimumNormSolution(design, target, -1, out _);
            var row = new Complex[modes];
            Array.Fill(row, Complex.NaN);
            for (int m = 0; m < k; m++)
            {
                row[live[m]] = solution[m, 0];
            }

            residues[r] = row;
        }

        return residues;
    }

    /// <summary>MATLAB's <c>computeIR</c>: the impulse response a one-sided frequency response implies.</summary>
    public static double[] ImpulseResponse(Complex[] frf, double[] f, double fs)
    {
        ArgumentNullException.ThrowIfNull(frf);
        ArgumentNullException.ThrowIfNull(f);
        int n = (int)System.Math.Round(fs / (f[1] - f[0]), MidpointRounding.AwayFromZero);
        var whole = new Complex[n];
        int keep = System.Math.Min(frf.Length, n);
        for (int i = 0; i < keep; i++)
        {
            whole[i] = frf[i];
        }

        // 'symmetric' asks the transform to treat the half it was given as one side of a
        // conjugate-symmetric whole, which is what makes the answer real.
        for (int i = 1; i < n - i + 1 && i < keep; i++)
        {
            whole[n - i] = Complex.Conjugate(whole[i]);
        }

        Fft.Transform(whole, inverse: true);
        var h = new double[n];
        for (int i = 0; i < n; i++)
        {
            h[i] = whole[i].Real;
        }

        return h;
    }

    /// <summary>MATLAB's <c>computeMaxM</c>: how many modes the record can support.</summary>
    public static int MaxModes(Complex[] frf, double[] f, double fs, bool[] inside)
    {
        ArgumentNullException.ThrowIfNull(inside);
        int count = Count(inside);
        double rate = fs * count / f.Length;
        double[] kept = Where(f, inside);
        int length = ImpulseResponse(WhereComplex(frf, inside), kept, rate).Length;
        return length / ((2 * 10) + 2);
    }

    /// <summary>MATLAB's <c>computeRFRF</c>: the response the fitted modes reconstruct.</summary>
    public static Complex[][] Reconstruct(
        double[] f, bool[] inside, Complex[][] residues, Complex[][] poles, ModalFit method)
    {
        ArgumentNullException.ThrowIfNull(f);
        ArgumentNullException.ThrowIfNull(inside);
        ArgumentNullException.ThrowIfNull(residues);
        ArgumentNullException.ThrowIfNull(poles);

        var rebuilt = new Complex[residues.Length][];
        for (int r = 0; r < residues.Length; r++)
        {
            rebuilt[r] = new Complex[f.Length];
            Complex[] column = method == ModalFit.PeakPicking ? poles[r] : poles[0];
            for (int i = 0; i < f.Length; i++)
            {
                if (!inside[i])
                {
                    rebuilt[r][i] = Complex.NaN;
                    continue;
                }

                Complex j = new(0, 2 * System.Math.PI * f[i]);
                Complex sum = 0;
                for (int m = 0; m < column.Length; m++)
                {
                    if (Complex.IsNaN(column[m]) || Complex.IsNaN(residues[r][m]))
                    {
                        continue;
                    }

                    sum += (residues[r][m] / (j - column[m]))
                        + (Complex.Conjugate(residues[r][m]) / (j - Complex.Conjugate(column[m])));
                }

                rebuilt[r][i] = sum;
            }
        }

        return rebuilt;
    }

    /// <summary>MATLAB's <c>computeModeShapes</c>, scaled by the driving point's own residue.</summary>
    public static Complex[][] ModeShapes(
        Complex[][] residues, int outputs, int inputs, int driveOut, int driveIn)
    {
        ArgumentNullException.ThrowIfNull(residues);
        int modes = residues.Length == 0 ? 0 : residues[0].Length;
        bool byColumn = outputs >= inputs;
        int stations = byColumn ? outputs : inputs;
        var shapes = new Complex[modes][];
        for (int m = 0; m < modes; m++)
        {
            shapes[m] = new Complex[stations];
        }

        for (int m = 0; m < modes; m++)
        {
            Complex driving = byColumn
                ? residues[driveOut + (driveIn * outputs)][m]
                : residues[driveOut + (driveIn * outputs)][m];
            Complex scale = Complex.Sqrt(driving);
            if (scale == Complex.Zero)
            {
                scale = 1;
            }

            for (int s = 0; s < stations; s++)
            {
                int linear = byColumn ? s + (driveIn * outputs) : driveOut + (s * outputs);
                Complex residue = residues[linear][m];

                // A mode the fit did not find is carried as a real NaN, and MATLAB's complex
                // arithmetic leaves its imaginary part at nought rather than making it a NaN too.
                shapes[m][s] = Complex.IsNaN(residue)
                    ? new Complex(double.NaN, 0)
                    : residue / scale;
            }
        }

        return shapes;
    }

    /// <summary>A pole's natural frequency in hertz and its damping ratio.</summary>
    public static (double Natural, double Damping) ToNaturalFrequency(Complex pole)
    {
        double magnitude = pole.Magnitude;
        return (magnitude / (2 * System.Math.PI), -pole.Real / magnitude);
    }

    /// <summary>The pole a natural frequency and damping ratio stand for.</summary>
    public static Complex FromNaturalFrequency(double natural, double damping) =>
        FromNaturalFrequency(new Complex(natural, 0), new Complex(damping, 0));

    /// <summary>
    /// The same, when the fit produced an imaginary frequency or a damping ratio above one — both
    /// of which happen, and both of which MATLAB carries through in complex arithmetic.
    /// </summary>
    public static Complex FromNaturalFrequency(Complex natural, Complex damping)
    {
        Complex omega = 2 * System.Math.PI * natural;
        return (-damping * omega) + (Complex.ImaginaryOne * omega * Complex.Sqrt(1 - (damping * damping)));
    }

    /// <summary>Which frequencies fall in the band a fit was asked for.</summary>
    public static bool[] Inside(double[] f, double[] range)
    {
        ArgumentNullException.ThrowIfNull(f);
        ArgumentNullException.ThrowIfNull(range);
        var inside = new bool[f.Length];
        for (int i = 0; i < f.Length; i++)
        {
            inside[i] = f[i] >= range[0] && f[i] <= range[1];
        }

        return inside;
    }

    private static int Count(bool[] inside)
    {
        int count = 0;
        foreach (bool value in inside)
        {
            if (value)
            {
                count++;
            }
        }

        return count;
    }

    private static double[] Where(double[] f, bool[] inside)
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

    private static Complex[] WhereComplex(Complex[] values, bool[] inside)
    {
        var kept = new List<Complex>();
        for (int i = 0; i < values.Length; i++)
        {
            if (inside[i])
            {
                kept.Add(values[i]);
            }
        }

        return [.. kept];
    }

    /// <summary>
    /// The order that sorts the natural frequencies, with the absent ones last. A frequency that
    /// came out imaginary sorts by its magnitude and then its angle, which is how MATLAB orders a
    /// complex array.
    /// </summary>
    private static int[] Order(Complex[] natural)
    {
        var index = new int[natural.Length];
        for (int i = 0; i < index.Length; i++)
        {
            index[i] = i;
        }

        Array.Sort(index, (a, b) =>
        {
            bool left = Complex.IsNaN(natural[a]);
            bool right = Complex.IsNaN(natural[b]);
            if (left || right)
            {
                return left == right ? a.CompareTo(b) : left ? 1 : -1;
            }

            int compared = natural[a].Magnitude.CompareTo(natural[b].Magnitude);
            if (compared != 0)
            {
                return compared;
            }

            compared = natural[a].Phase.CompareTo(natural[b].Phase);
            return compared != 0 ? compared : a.CompareTo(b);
        });
        return index;
    }
}
