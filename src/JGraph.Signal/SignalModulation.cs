namespace JGraph.Signal;

using JGraph.Numerics;

/// <summary>
/// <c>modulate</c>, <c>demod</c> and <c>vco</c>: putting a message onto a carrier and taking it off
/// again (M132).
/// </summary>
/// <remarks>
/// <para>
/// Nine modulation methods share one file because they share one skeleton — build a time axis from
/// the sampling rate, do one line of arithmetic per sample against a carrier — and differ only in
/// that line. Two of them do not: pulse-width and pulse-position modulation produce an output at the
/// sampling rate rather than at the message rate, so their outputs are longer than their inputs by
/// the ratio of the two, and their time axes are built separately.
/// </para>
/// <para>
/// Demodulation is not the inverse of modulation and does not pretend to be. Amplitude
/// demodulation multiplies by the carrier again, which produces the message plus a copy at twice
/// the carrier, and then removes the copy with a low-pass filter — a fifth-order Butterworth run
/// forwards and backwards, so that the message is not delayed. That filter is the one piece of
/// M133 this milestone had to borrow, and it is in <see cref="ZeroPhaseFilter"/>.
/// </para>
/// </remarks>
public static class SignalModulation
{
    /// <summary>Puts one column of a message onto a carrier.</summary>
    /// <param name="x">The message, column-major, <paramref name="length"/> rows by columns.</param>
    /// <param name="length">How many samples each column holds.</param>
    /// <param name="columns">How many columns there are.</param>
    /// <param name="carrier">The carrier frequency.</param>
    /// <param name="rate">The sampling rate.</param>
    /// <param name="method">One of MATLAB's nine method names, lower case.</param>
    /// <param name="option">The method's extra parameter, or null for its default.</param>
    /// <param name="word">The method's extra word, for the two that take one.</param>
    public static (double[] Signal, double[] Time, int Rows) Modulate(
        ReadOnlySpan<double> x, int length, int columns, double carrier, double rate,
        string method, double? option, string? word)
    {
        if (carrier >= rate / 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(carrier), "modulate needs a carrier below half the sampling rate.");
        }

        switch (method)
        {
            case "am":
            case "amdsb-sc":
                return Simple(x, length, columns, carrier, rate, (v, t) => v * Carrier(carrier, t));

            case "amdsb-tc":
            {
                double offset = option ?? Least(x);
                return Simple(x, length, columns, carrier, rate, (v, t) => (v - offset) * Carrier(carrier, t));
            }

            case "amssb":
            {
                var quadrature = new double[length * columns];
                for (int c = 0; c < columns; c++)
                {
                    var column = new double[length];
                    for (int i = 0; i < length; i++)
                    {
                        column[i] = x[(c * length) + i];
                    }

                    System.Numerics.Complex[] analytic = SignalTransforms.Hilbert(column, length);
                    for (int i = 0; i < length; i++)
                    {
                        quadrature[(c * length) + i] = analytic[i].Imaginary;
                    }
                }

                var y = new double[length * columns];
                double[] time = TimeAxis(length, rate);
                for (int c = 0; c < columns; c++)
                {
                    for (int i = 0; i < length; i++)
                    {
                        double angle = 2.0 * System.Math.PI * carrier * time[i];
                        y[(c * length) + i] = (x[(c * length) + i] * System.Math.Cos(angle))
                            + (quadrature[(c * length) + i] * System.Math.Sin(angle));
                    }
                }

                return (y, time, length);
            }

            case "fm":
            {
                double peak = LargestMagnitude(x);
                double kf = option ?? (peak > 0 ? carrier / rate * 2 * System.Math.PI / peak : 0.0);
                var y = new double[length * columns];
                double[] time = TimeAxis(length, rate);
                for (int c = 0; c < columns; c++)
                {
                    double running = 0;
                    for (int i = 0; i < length; i++)
                    {
                        running += x[(c * length) + i];
                        y[(c * length) + i] =
                            System.Math.Cos((2.0 * System.Math.PI * carrier * time[i]) + (kf * running));
                    }
                }

                return (y, time, length);
            }

            case "pm":
            {
                double kp = option ?? System.Math.PI / LargestMagnitude(x);
                return Simple(x, length, columns, carrier, rate,
                    (v, t) => System.Math.Cos((2.0 * System.Math.PI * carrier * t) + (kp * v)));
            }

            case "pwm":
                return PulseWidth(x, length, columns, carrier, rate, word ?? "left");

            case "ptm":
            case "ppm":
                return PulsePosition(x, length, columns, carrier, rate, option ?? 0.1);

            case "qam":
                throw new ArgumentException(
                    "modulate's 'qam' needs its second message as the option.", nameof(method));

            default:
                throw new ArgumentException($"modulate does not know the method '{method}'.", nameof(method));
        }
    }

    /// <summary>Quadrature amplitude modulation, which takes a second message rather than a number.</summary>
    public static (double[] Signal, double[] Time, int Rows) ModulateQuadrature(
        ReadOnlySpan<double> x, ReadOnlySpan<double> second, int length, int columns,
        double carrier, double rate)
    {
        double[] time = TimeAxis(length, rate);
        var y = new double[length * columns];
        for (int c = 0; c < columns; c++)
        {
            for (int i = 0; i < length; i++)
            {
                double angle = 2.0 * System.Math.PI * carrier * time[i];
                y[(c * length) + i] = (x[(c * length) + i] * System.Math.Cos(angle))
                    + (second[(c * length) + i] * System.Math.Sin(angle));
            }
        }

        return (y, time, length);
    }

    /// <summary>Takes a message back off a carrier.</summary>
    public static (double[] Message, double[] Second, int Rows) Demodulate(
        ReadOnlySpan<double> y, int length, int columns, double carrier, double rate,
        string method, double? option, string? word)
    {
        if (carrier >= rate / 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(carrier), "demod needs a carrier below half the sampling rate.");
        }

        double[] time = TimeAxis(length, rate);
        switch (method)
        {
            case "am":
            case "amdsb-sc":
            case "amdsb-tc":
            case "amssb":
            {
                double[] mixed = Mix(y, length, columns, carrier, time, 1.0, sine: false);
                double[] smoothed = LowPass(mixed, length, columns, carrier, rate);
                if (method == "amdsb-tc")
                {
                    double offset = option ?? 0.0;
                    for (int i = 0; i < smoothed.Length; i++)
                    {
                        smoothed[i] -= offset;
                    }
                }

                return (smoothed, [], length);
            }

            case "fm":
            case "pm":
            {
                double scale = option ?? 1.0;
                var message = new double[length * columns];
                for (int c = 0; c < columns; c++)
                {
                    var column = new double[length];
                    for (int i = 0; i < length; i++)
                    {
                        column[i] = y[(c * length) + i];
                    }

                    System.Numerics.Complex[] analytic = SignalTransforms.Hilbert(column, length);
                    var phase = new double[length];
                    for (int i = 0; i < length; i++)
                    {
                        var rotated = analytic[i] * System.Numerics.Complex.Exp(
                            -System.Numerics.Complex.ImaginaryOne * 2.0 * System.Math.PI * carrier * time[i]);
                        phase[i] = System.Math.Atan2(rotated.Imaginary, rotated.Real);
                    }

                    if (method == "pm")
                    {
                        for (int i = 0; i < length; i++)
                        {
                            message[(c * length) + i] = phase[i] / scale;
                        }

                        continue;
                    }

                    PhaseSequences.Unwrap(phase, System.Math.PI);
                    message[c * length] = 0;
                    for (int i = 1; i < length; i++)
                    {
                        message[(c * length) + i] = (phase[i] - phase[i - 1]) / scale;
                    }
                }

                return (message, [], length);
            }

            case "pwm":
            {
                int rows = (int)System.Math.Ceiling(length * carrier / rate);
                var message = new double[rows * columns];
                bool centred = string.Equals(word, "centered", StringComparison.OrdinalIgnoreCase);
                for (int i = 0; i < rows; i++)
                {
                    for (int c = 0; c < columns; c++)
                    {
                        double count = 0;
                        for (int j = 0; j < length; j++)
                        {
                            double shifted = time[j] - (i / carrier);
                            bool inside = centred
                                ? shifted >= -1 / 2.0 / carrier && shifted < 1 / 2.0 / carrier
                                : shifted >= 0 && shifted < 1 / carrier;
                            if (inside && y[(c * length) + j] > 0.5)
                            {
                                count++;
                            }
                        }

                        message[(c * rows) + i] = count * carrier / rate;
                    }
                }

                if (centred)
                {
                    for (int c = 0; c < columns; c++)
                    {
                        message[c * rows] *= 2;
                    }
                }

                return (message, [], rows);
            }

            case "ptm":
            case "ppm":
            {
                int rows = (int)System.Math.Ceiling(length * carrier / rate);
                var message = new double[rows * columns];
                for (int i = 0; i < rows; i++)
                {
                    for (int c = 0; c < columns; c++)
                    {
                        for (int j = 0; j < length; j++)
                        {
                            double shifted = (time[j] * carrier) - i;
                            if (shifted >= 0 && shifted < 1 && y[(c * length) + j] > 0.5)
                            {
                                message[(c * rows) + i] = shifted;
                                break;
                            }
                        }
                    }
                }

                return (message, [], rows);
            }

            case "qam":
            {
                double[] inPhase = LowPass(Mix(y, length, columns, carrier, time, 2.0, sine: false),
                    length, columns, carrier, rate);
                double[] quadrature = LowPass(Mix(y, length, columns, carrier, time, 2.0, sine: true),
                    length, columns, carrier, rate);
                return (inPhase, quadrature, length);
            }

            default:
                throw new ArgumentException($"demod does not know the method '{method}'.", nameof(method));
        }
    }

    /// <summary>A voltage-controlled oscillator, which is frequency modulation with a scaled range.</summary>
    public static double[] VoltageControlled(
        ReadOnlySpan<double> x, int length, int columns, ReadOnlySpan<double> range, double rate)
    {
        foreach (double v in x)
        {
            if (v > 1 || v < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(x), "vco's input lies between minus one and one.");
            }
        }

        double centre;
        double swing;
        if (range.Length > 1)
        {
            double sum = 0;
            foreach (double v in range)
            {
                sum += v;
            }

            centre = sum / range.Length;
            swing = (range[1] - centre) / rate * 2 * System.Math.PI;
        }
        else
        {
            centre = range[0];
            swing = centre / rate * 2 * System.Math.PI;
        }

        (double[] y, _, _) = Modulate(x, length, columns, centre, rate, "fm", swing, null);
        return y;
    }

    /// <summary>The time axis every method shares.</summary>
    private static double[] TimeAxis(int length, double rate)
    {
        double step = 1.0 / rate;
        var t = new double[length];
        for (int i = 0; i < length; i++)
        {
            t[i] = i * step;
        }

        return t;
    }

    /// <summary>The carrier's value at one instant.</summary>
    private static double Carrier(double frequency, double t) =>
        System.Math.Cos(2.0 * System.Math.PI * frequency * t);

    /// <summary>One line of arithmetic per sample against the time axis.</summary>
    private static (double[] Signal, double[] Time, int Rows) Simple(
        ReadOnlySpan<double> x, int length, int columns, double carrier, double rate,
        Func<double, double, double> body)
    {
        double[] time = TimeAxis(length, rate);
        var y = new double[length * columns];
        for (int c = 0; c < columns; c++)
        {
            for (int i = 0; i < length; i++)
            {
                y[(c * length) + i] = body(x[(c * length) + i], time[i]);
            }
        }

        return (y, time, length);
    }

    /// <summary>Multiplies a signal by the carrier again, which is what demodulation starts with.</summary>
    private static double[] Mix(
        ReadOnlySpan<double> y, int length, int columns, double carrier, double[] time,
        double gain, bool sine)
    {
        var mixed = new double[length * columns];
        for (int c = 0; c < columns; c++)
        {
            for (int i = 0; i < length; i++)
            {
                double angle = 2.0 * System.Math.PI * carrier * time[i];
                double wave = sine ? System.Math.Sin(angle) : System.Math.Cos(angle);
                mixed[(c * length) + i] = gain * y[(c * length) + i] * wave;
            }
        }

        return mixed;
    }

    /// <summary>The zero-phase fifth-order Butterworth every amplitude demodulation ends with.</summary>
    private static double[] LowPass(double[] mixed, int length, int columns, double carrier, double rate)
    {
        (double[] b, double[] a) = IirDesign.Butterworth(5, [carrier * 2 / rate], FilterBandType.LowPass);
        var answer = new double[mixed.Length];
        var column = new double[length];
        for (int c = 0; c < columns; c++)
        {
            Array.Copy(mixed, c * length, column, 0, length);
            double[] filtered = ZeroPhaseFilter.Apply(b, a, column);
            Array.Copy(filtered, 0, answer, c * length, length);
        }

        return answer;
    }

    /// <summary>Pulse-width modulation, whose output runs at the sampling rate.</summary>
    private static (double[] Signal, double[] Time, int Rows) PulseWidth(
        ReadOnlySpan<double> x, int length, int columns, double carrier, double rate, string word)
    {
        RequireUnitInterval(x);
        bool centred = string.Equals(word, "centered", StringComparison.OrdinalIgnoreCase);
        if (!centred && !string.Equals(word, "left", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("modulate's pulse-width option is 'left' or 'centered'.", nameof(word));
        }

        int rows = (int)(rate / carrier * length);
        double period = rate / carrier;
        var y = new double[rows * columns];
        for (int i = 0; i < rows; i++)
        {
            double fraction = Remainder(i, period) / period;
            int which = (int)System.Math.Floor((i * carrier / rate) + 1) - 1;
            for (int c = 0; c < columns; c++)
            {
                double here = which < length ? x[(c * length) + which] : 0;
                if (!centred)
                {
                    y[(c * rows) + i] = fraction < here ? 1 : 0;
                    continue;
                }

                double next = which + 1 < length ? x[(c * length) + which + 1] : 0;
                y[(c * rows) + i] = (fraction < here / 2 ? 1 : 0) + (1 - fraction <= next / 2 ? 1 : 0);
            }
        }

        var time = new double[rows];
        for (int i = 0; i < rows; i++)
        {
            time[i] = i / rate;
        }

        return (y, time, rows);
    }

    /// <summary>Pulse-position modulation, which places a fixed pulse at a time set by the message.</summary>
    private static (double[] Signal, double[] Time, int Rows) PulsePosition(
        ReadOnlySpan<double> x, int length, int columns, double carrier, double rate, double width)
    {
        RequireUnitInterval(x);
        if (width is <= 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "modulate's pulse width lies between zero and one.");
        }

        int rows = (int)(length * rate / carrier);
        int span = (int)System.Math.Floor(width * rate / carrier);
        var y = new double[rows * columns];
        for (int c = 0; c < columns; c++)
        {
            for (int i = 0; i < length; i++)
            {
                int at = (int)System.Math.Ceiling((x[(c * length) + i] + i) * rate / carrier);
                for (int k = 0; k < span; k++)
                {
                    int position = at + k;
                    if (position >= 0 && position < rows)
                    {
                        y[(c * rows) + position] = 1;
                    }
                }
            }
        }

        var time = new double[rows];
        for (int i = 0; i < rows; i++)
        {
            time[i] = i / rate;
        }

        return (y, time, rows);
    }

    /// <summary>Refuses a message that is not between zero and one, which the pulse methods need.</summary>
    private static void RequireUnitInterval(ReadOnlySpan<double> x)
    {
        foreach (double v in x)
        {
            if (v > 1 || v < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(x), "modulate's pulse methods need a message between zero and one.");
            }
        }
    }

    /// <summary>The smallest sample.</summary>
    private static double Least(ReadOnlySpan<double> x)
    {
        double least = double.PositiveInfinity;
        foreach (double v in x)
        {
            least = System.Math.Min(least, v);
        }

        return double.IsPositiveInfinity(least) ? 0 : least;
    }

    /// <summary>The largest sample by absolute value.</summary>
    private static double LargestMagnitude(ReadOnlySpan<double> x)
    {
        double largest = 0;
        foreach (double v in x)
        {
            largest = System.Math.Max(largest, System.Math.Abs(v));
        }

        return largest;
    }

    /// <summary>MATLAB's <c>rem</c>, whose answer takes the sign of the dividend.</summary>
    private static double Remainder(double x, double y) =>
        y == 0 ? double.NaN : x - (System.Math.Truncate(x / y) * y);
}
