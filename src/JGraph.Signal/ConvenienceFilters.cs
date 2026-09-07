namespace JGraph.Signal;

/// <summary>
/// The four one-line filters — <c>lowpass</c>, <c>highpass</c>, <c>bandpass</c>, <c>bandstop</c> —
/// as far as the design goes: everything between a passband frequency and a <c>digitalFilter</c>
/// (M135).
/// </summary>
/// <remarks>
/// <para>
/// These names take one frequency and a signal and do the rest, and "the rest" is a specific chain
/// of decisions worth writing down. A steepness becomes a transition width as a fraction of the
/// distance to the band edge; the width becomes a stopband edge; the edge becomes a Kaiser order
/// estimate; the estimate, compared against the signal's own length, decides whether the answer is
/// FIR or IIR. Only then is a filter designed, through <see cref="FilterSpecification"/> like any
/// other.
/// </para>
/// <para>
/// The signal length matters twice over. It decides FIR against IIR — an FIR filter longer than half
/// the signal is no use — and, for an IIR design, it caps the order at a third of the length, because
/// that is what <c>filtfilt</c> can run. Both caps are the reference's, and both are worth keeping:
/// they are why a short signal gets an answer rather than an error.
/// </para>
/// </remarks>
public static class ConvenienceFilters
{
    /// <summary>Which of the four verbs is being asked for.</summary>
    public enum Verb
    {
        /// <summary><c>lowpass</c>.</summary>
        Lowpass,

        /// <summary><c>highpass</c>.</summary>
        Highpass,

        /// <summary><c>bandpass</c>.</summary>
        Bandpass,

        /// <summary><c>bandstop</c>.</summary>
        Bandstop,
    }

    /// <summary>What the caller asked for, once the defaults are in.</summary>
    /// <param name="Kind">Which verb.</param>
    /// <param name="Passband">One passband frequency, or two.</param>
    /// <param name="SampleRate">The sample rate, or null when the frequencies are normalised.</param>
    /// <param name="SignalLength">How many samples each column holds.</param>
    /// <param name="Steepness">The transition steepness, one value or two.</param>
    /// <param name="StopbandAttenuation">The stopband attenuation in dB.</param>
    /// <param name="ImpulseResponse"><c>auto</c>, <c>fir</c> or <c>iir</c>.</param>
    public sealed record Request(
        Verb Kind,
        double[] Passband,
        double? SampleRate,
        int SignalLength,
        double[] Steepness,
        double StopbandAttenuation,
        string ImpulseResponse);

    /// <summary>The design a request produces, and the two facts the caller needs about it.</summary>
    /// <param name="Filter">The designed filter, or null when the answer is an all-pass or all-stop.</param>
    /// <param name="IsFir">Whether the filtering is a delay-compensated pass or a <c>filtfilt</c>.</param>
    /// <param name="Trivial">A one-tap numerator when the design degenerated, or null.</param>
    /// <param name="Warning">What the reference warns about, or null.</param>
    public sealed record Result(
        FilterSpecification.Designed? Filter, bool IsFir, double[]? Trivial, string? Warning);

    /// <summary>The passband ripple these names design to; it is not a caller's to set.</summary>
    private const double PassbandRipple = 0.1;

    /// <summary>Designs the filter a request describes.</summary>
    public static Result Design(Request request)
    {
        double fs = request.SampleRate ?? 2;
        double nyquist = fs / 2;
        double[] normalised = [.. request.Passband.Select(f => f / nyquist)];

        // A steepness of one asks for a one-per-cent transition and a steepness of a half for a
        // fifty-per-cent one; the line between them is the whole of the parameter.
        double[] widths = [.. request.Steepness.Select(static s => (-0.98 * s) + 0.99)];

        return request.Kind switch
        {
            Verb.Lowpass => Lowpass(request, fs, normalised, widths),
            Verb.Highpass => Highpass(request, fs, normalised, widths),
            Verb.Bandpass => Bandpass(request, fs, normalised, widths),
            _ => Bandstop(request, fs, normalised, widths),
        };
    }

    private static Result Lowpass(Request request, double fs, double[] wp, double[] widths)
    {
        if (request.SignalLength <= 3 || wp[0] >= 1)
        {
            return new Result(null, true, [1],
                request.SignalLength <= 3
                    ? "The input signal has 3 or fewer samples; an all-pass filter was used."
                    : "The passband frequency is at or above Nyquist; an all-pass filter was used.");
        }

        double stopNormalised = wp[0] + (widths[0] * (1 - wp[0]));
        double stop = stopNormalised * (fs / 2);
        double pass = wp[0] * (fs / 2);
        int fir = KaiserLength([pass, stop], [1, 0], request, fs);

        if (Iir(fir, request))
        {
            double aWpass = System.Math.Tan(System.Math.PI * wp[0] / 2);
            double aWstop = System.Math.Tan(System.Math.PI * stopNormalised / 2);
            return Elliptic(request, fs, "lowpassiir", aWpass, aWstop,
                [("PassbandFrequency", pass)], [("StopbandFrequency", stop)]);
        }

        return Kaiser(request, fs, "lowpassfir",
            [("PassbandFrequency", pass), ("StopbandFrequency", stop)]);
    }

    private static Result Highpass(Request request, double fs, double[] wp, double[] widths)
    {
        if (wp[0] >= 1)
        {
            return new Result(null, true, [0],
                "The passband frequency is at or above Nyquist; an all-stop filter was used.");
        }

        if (request.SignalLength <= 3)
        {
            return new Result(null, true, [1],
                "The input signal has 3 or fewer samples; an all-pass filter was used.");
        }

        double stopNormalised = wp[0] - (widths[0] * wp[0]);
        double stop = stopNormalised * (fs / 2);
        double pass = wp[0] * (fs / 2);
        int fir = KaiserLength([stop, pass], [0, 1], request, fs);

        if (Iir(fir, request))
        {
            double aWpass = Cot(System.Math.PI * wp[0] / 2);
            double aWstop = Cot(System.Math.PI * stopNormalised / 2);
            return Elliptic(request, fs, "highpassiir", aWpass, aWstop,
                [("PassbandFrequency", pass)], [("StopbandFrequency", stop)]);
        }

        return Kaiser(request, fs, "highpassfir",
            [("StopbandFrequency", stop), ("PassbandFrequency", pass)]);
    }

    private static Result Bandpass(Request request, double fs, double[] wp, double[] widths)
    {
        if (request.SignalLength <= 6)
        {
            return new Result(null, true, [1],
                "The input signal has 6 or fewer samples; an all-pass filter was used.");
        }

        (double w1, double w2) = BandWidths(widths, wp[0], 1 - wp[1]);
        double[] stopNormalised = [wp[0] - w1, wp[1] + w2];
        double[] stop = [.. stopNormalised.Select(v => v * (fs / 2))];
        double[] pass = [.. wp.Select(v => v * (fs / 2))];
        int fir = KaiserLength([stop[0], pass[0], pass[1], stop[1]], [0, 1, 0], request, fs);

        if (Iir(fir, request))
        {
            double c = Centre(wp);
            double aWpass = System.Math.Abs(BandpassEdge(wp[1], c));
            double aWstop = System.Math.Min(
                System.Math.Abs(BandpassEdge(stopNormalised[0], c)),
                System.Math.Abs(BandpassEdge(stopNormalised[1], c)));
            return Elliptic(request, fs, "bandpassiir", aWpass, aWstop,
                [("PassbandFrequency1", pass[0]), ("PassbandFrequency2", pass[1])],
                [("StopbandFrequency1", stop[0]), ("StopbandFrequency2", stop[1])],
                doubled: true);
        }

        return Kaiser(request, fs, "bandpassfir",
        [
            ("StopbandFrequency1", stop[0]), ("PassbandFrequency1", pass[0]),
            ("PassbandFrequency2", pass[1]), ("StopbandFrequency2", stop[1]),
        ]);
    }

    private static Result Bandstop(Request request, double fs, double[] wp, double[] widths)
    {
        if (request.SignalLength <= 6)
        {
            return new Result(null, true, [1],
                "The input signal has 6 or fewer samples; an all-pass filter was used.");
        }

        double centre = (wp[0] / 2) + (wp[1] / 2);
        (double w1, double w2) = BandWidths(widths, centre - wp[0], wp[1] - centre);
        double[] stopNormalised = [wp[0] + w1, wp[1] - w2];
        double[] stop = [.. stopNormalised.Select(v => v * (fs / 2))];
        double[] pass = [.. wp.Select(v => v * (fs / 2))];
        int fir = KaiserLength([pass[0], stop[0], stop[1], pass[1]], [1, 0, 1], request, fs);

        if (Iir(fir, request))
        {
            double c = Centre(wp);
            double aWpass = System.Math.Abs(BandstopEdge(wp[1], c));
            double aWstop = System.Math.Min(
                System.Math.Abs(BandstopEdge(stopNormalised[0], c)),
                System.Math.Abs(BandstopEdge(stopNormalised[1], c)));
            return Elliptic(request, fs, "bandstopiir", aWpass, aWstop,
                [("PassbandFrequency1", pass[0]), ("PassbandFrequency2", pass[1])],
                [("StopbandFrequency1", stop[0]), ("StopbandFrequency2", stop[1])],
                doubled: true);
        }

        return Kaiser(request, fs, "bandstopfir",
        [
            ("PassbandFrequency1", pass[0]), ("StopbandFrequency1", stop[0]),
            ("StopbandFrequency2", stop[1]), ("PassbandFrequency2", pass[1]),
        ]);
    }

    /// <summary>
    /// The two transition widths a band design uses: separate when the caller gave two steepnesses,
    /// and otherwise the narrower of the two on both sides, so that one window can hold both.
    /// </summary>
    private static (double First, double Second) BandWidths(double[] widths, double first, double second)
    {
        if (widths.Length == 1)
        {
            double narrow = System.Math.Min(widths[0] * first, widths[0] * second);
            return (narrow, narrow);
        }

        return (widths[0] * first, widths[1] * second);
    }

    private static int KaiserLength(double[] edges, double[] magnitudes, Request request, double fs)
    {
        double[] deviations =
        [
            .. magnitudes.Select(m => m == 0
                ? FilterSpecification.StopbandDeviation(request.StopbandAttenuation)
                : FilterSpecification.PassbandDeviation(PassbandRipple)),
        ];
        return FirWindowDesign.KaiserOrder(edges, magnitudes, deviations, fs).Order;
    }

    /// <summary>
    /// Whether the answer is an IIR filter: because the caller asked for one, or because the FIR one
    /// the specification needs would be more than half as long as the signal it is meant to filter.
    /// </summary>
    private static bool Iir(int firOrder, Request request) => request.ImpulseResponse switch
    {
        "iir" => true,
        "fir" => request.SignalLength <= 2 * firOrder
            ? throw new ArgumentException(
                $"The signal must have more than {2 * firOrder} samples to be filtered by an FIR "
                + "filter that meets the specification.")
            : false,
        _ => request.SignalLength <= 2 * firOrder,
    };

    private static Result Kaiser(
        Request request, double fs, string response, (string Name, double Value)[] frequencies)
    {
        var values = new List<(string, double[])>();
        foreach ((string name, double value) in frequencies)
        {
            values.Add((name, [value]));
        }

        AddLevels(values, response, request.StopbandAttenuation);
        FilterSpecification.Designed designed = FilterSpecification.Design(
            response, values, "kaiserwin", request.SampleRate,
            new FilterSpecification.Options { MinOrder = "even" });
        _ = fs;
        return new Result(designed, true, null, null);
    }

    private static Result Elliptic(
        Request request, double fs, string response, double aWpass, double aWstop,
        (string Name, double Value)[] passband, (string Name, double Value)[] stopband,
        bool doubled = false)
    {
        int order = EllipticOrder(aWpass, aWstop, PassbandRipple, request.StopbandAttenuation);
        if (doubled)
        {
            order *= 2;
        }

        var values = new List<(string, double[])>();
        bool truncated = request.SignalLength <= 3 * order;
        if (truncated)
        {
            // filtfilt needs three times the order in samples, so a signal that cannot carry the
            // order the specification wants gets the order it can carry instead.
            int n = System.Math.Max(doubled ? 2 : 1, request.SignalLength / 3);
            if (n > 1 && 3 * n == request.SignalLength)
            {
                n--;
            }

            if (doubled && n % 2 == 1)
            {
                n = System.Math.Max(2, n - 1);
            }

            values.Add(("FilterOrder", [n]));
            foreach ((string name, double value) in passband)
            {
                values.Add((name, [value]));
            }
        }
        else
        {
            foreach ((string name, double value) in passband)
            {
                values.Add((name, [value]));
            }

            foreach ((string name, double value) in stopband)
            {
                values.Add((name, [value]));
            }
        }

        AddLevels(values, response, request.StopbandAttenuation, truncated);
        FilterSpecification.Designed designed =
            FilterSpecification.Design(response, values, "ellip", request.SampleRate, null);
        _ = fs;
        return new Result(designed, false, null,
            truncated ? "The signal is too short for the order the specification needs; the order was reduced." : null);
    }

    /// <summary>
    /// The ripple and attenuation names a response's parameter set wants, which differ by band and
    /// by whether the order was given.
    /// </summary>
    private static void AddLevels(
        List<(string, double[])> values, string response, double attenuation, bool orderGiven = false)
    {
        bool bandpass = response.StartsWith("bandpass", StringComparison.Ordinal);
        bool bandstop = response.StartsWith("bandstop", StringComparison.Ordinal);
        bool iir = response.EndsWith("iir", StringComparison.Ordinal);

        if (bandpass && !(iir && orderGiven))
        {
            values.Add(("StopbandAttenuation1", [attenuation]));
            values.Add(("PassbandRipple", [PassbandRipple]));
            values.Add(("StopbandAttenuation2", [attenuation]));
            return;
        }

        if (bandpass)
        {
            values.Add(("StopbandAttenuation1", [attenuation]));
            values.Add(("PassbandRipple", [PassbandRipple]));
            values.Add(("StopbandAttenuation2", [attenuation]));
            return;
        }

        if (bandstop && !(iir && orderGiven))
        {
            values.Add(("PassbandRipple1", [PassbandRipple]));
            values.Add(("StopbandAttenuation", [attenuation]));
            values.Add(("PassbandRipple2", [PassbandRipple]));
            return;
        }

        if (bandstop)
        {
            values.Add(("PassbandRipple", [PassbandRipple]));
            values.Add(("StopbandAttenuation", [attenuation]));
            return;
        }

        values.Add(("PassbandRipple", [PassbandRipple]));
        values.Add(("StopbandAttenuation", [attenuation]));
    }

    /// <summary>The least elliptic order that meets two analogue edges, by the selectivity series.</summary>
    private static int EllipticOrder(double aWpass, double aWstop, double apass, double astop)
    {
        double wc = System.Math.Sqrt(aWpass * aWstop);
        double q = CascadeDesign.SelectivityNome(aWpass / wc);
        double d = (System.Math.Pow(10, 0.1 * astop) - 1) / (System.Math.Pow(10, 0.1 * apass) - 1);
        int n = (int)System.Math.Ceiling(System.Math.Log10(16 * d) / System.Math.Log10(1 / q));
        return n <= 0 ? 2 : n;
    }

    private static double Centre(double[] wp) =>
        System.Math.Sin(System.Math.PI * (wp[0] + wp[1]))
        / (System.Math.Sin(System.Math.PI * wp[0]) + System.Math.Sin(System.Math.PI * wp[1]));

    private static double BandpassEdge(double f, double c) =>
        (c - System.Math.Cos(System.Math.PI * f)) / System.Math.Sin(System.Math.PI * f);

    private static double BandstopEdge(double f, double c) =>
        System.Math.Sin(System.Math.PI * f) / (System.Math.Cos(System.Math.PI * f) - c);

    private static double Cot(double x) => 1 / System.Math.Tan(x);
}
