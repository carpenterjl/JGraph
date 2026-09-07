namespace JGraph.Signal;

/// <summary>
/// The <c>designfilt</c> specification table: which parameter sets name a filter, which design
/// methods each set admits, and what each combination actually designs (M135).
/// </summary>
/// <remarks>
/// <para>
/// <c>designfilt</c> designs nothing itself. It is a parser in front of designs that already exist,
/// and the whole of it is a table: a response name picks a list of parameter sets, the names the
/// caller passed pick one set out of that list, the set and the chosen method pick a design, and the
/// design is one of M134's. What makes it worth its own file is that the table is not obvious from
/// any one design — the same <c>lowpassfir</c> response reaches <c>fir1</c>, <c>firls</c>,
/// <c>firpm</c>, <c>fircls</c> or <c>maxflat</c> depending on which four words the caller used, and
/// two of those five are reached only through a minimum-order search.
/// </para>
/// <para>
/// The IIR half routes to <see cref="CascadeDesign"/>, which answers in second-order sections
/// because that is the form a <c>digitalFilter</c> keeps. The FIR half routes to M134 and answers in
/// taps. Both halves come back through the same record, so everything downstream — the filtering,
/// the analysis, the display — reads one shape.
/// </para>
/// </remarks>
public static class FilterSpecification
{
    /// <summary>What a design method was asked to do, once the parser has decided.</summary>
    public enum Algorithm
    {
        /// <summary>Butterworth (<c>butter</c>).</summary>
        Butterworth,

        /// <summary>Chebyshev type I (<c>cheby1</c>).</summary>
        Chebyshev1,

        /// <summary>Chebyshev type II (<c>cheby2</c>).</summary>
        Chebyshev2,

        /// <summary>Elliptic (<c>ellip</c>).</summary>
        Elliptic,

        /// <summary>Maximally flat (<c>maxflat</c>).</summary>
        Maxflat,

        /// <summary>The window method (<c>window</c>, through <c>fir1</c>).</summary>
        Window,

        /// <summary>Kaiser window at the least order that meets the specification (<c>kaiserwin</c>).</summary>
        KaiserWindow,

        /// <summary>Equiripple (<c>equiripple</c>, through <c>firpm</c>).</summary>
        Equiripple,

        /// <summary>Least squares (<c>ls</c>, through <c>firls</c>).</summary>
        LeastSquares,

        /// <summary>Constrained least squares (<c>cls</c>, through <c>fircls</c>).</summary>
        ConstrainedLeastSquares,

        /// <summary>Frequency sampling (<c>freqsamp</c>, through <c>fir2</c>).</summary>
        FrequencySampling,
    }

    /// <summary>The design options a method may be given, all of them optional.</summary>
    /// <remarks>
    /// A null field means the method's own default, which is not always the same default for every
    /// method — which is why they are nullable rather than pre-filled.
    /// </remarks>
    public sealed class Options
    {
        /// <summary>The window a <c>window</c> or <c>freqsamp</c> design tapers with, already evaluated.</summary>
        public double[]? Window { get; set; }

        /// <summary>Whether a windowed design scales one passband point to unit gain.</summary>
        public bool? ScalePassband { get; set; }

        /// <summary>Whether a constrained design's stopband lower bound is zero rather than the negated upper one.</summary>
        public bool? ZeroPhase { get; set; }

        /// <summary>The dB offset a constrained design's passband is centred on, one entry per passband.</summary>
        public double[]? PassbandOffset { get; set; }

        /// <summary>The parity a minimum-order Kaiser design's order is held to: any, even or odd.</summary>
        public string? MinOrder { get; set; }

        /// <summary>Which band a minimum-order IIR design meets exactly.</summary>
        public string? MatchExactly { get; set; }

        /// <summary>The band weights, in the order the response lists its bands.</summary>
        public double[]? Weights { get; set; }
    }

    /// <summary>A designed filter, in whichever of the two forms its method produces.</summary>
    /// <param name="Response">The response name as given, such as <c>lowpassfir</c>.</param>
    /// <param name="FrequencyResponse">The band alone: <c>lowpass</c>, <c>bandstop</c>, and so on.</param>
    /// <param name="ImpulseResponse"><c>fir</c> or <c>iir</c>.</param>
    /// <param name="DesignMethod">The method the design actually used.</param>
    /// <param name="SampleRate">The sample rate the frequencies were given in; two when normalised.</param>
    /// <param name="Taps">An FIR design's coefficients, or null.</param>
    /// <param name="Sections">An IIR design's sections with their scale values already folded in, or null.</param>
    /// <param name="Specifications">The parameter set that was matched, with the values as given.</param>
    public sealed record Designed(
        string Response,
        string FrequencyResponse,
        string ImpulseResponse,
        string DesignMethod,
        double SampleRate,
        double[]? Taps,
        double[,]? Sections,
        IReadOnlyList<(string Name, double[] Value)> Specifications);

    /// <summary>One row of the table: a parameter set and the methods it admits.</summary>
    private sealed record SpecSet(string[] Properties, Algorithm[] Methods);

    /// <summary>The responses <c>designfilt</c> knows, in the order it lists them.</summary>
    public static IReadOnlyList<string> Responses { get; } =
    [
        "bandpassfir", "bandpassiir",
        "bandstopfir", "bandstopiir",
        "differentiatorfir",
        "highpassfir", "highpassiir",
        "hilbertfir",
        "lowpassfir", "lowpassiir",
    ];

    private static readonly Dictionary<string, SpecSet[]> Table = BuildTable();

    /// <summary>The parameter sets a response admits, each as a comma-joined list of names.</summary>
    public static IReadOnlyList<string> ParameterSets(string response) =>
        Table.TryGetValue(response, out SpecSet[]? sets)
            ? [.. sets.Select(static s => string.Join(", ", s.Properties))]
            : throw new ArgumentException("Filter response is not valid.", nameof(response));

    /// <summary>The design methods a response admits across all of its parameter sets.</summary>
    public static IReadOnlyList<string> Methods(string response) =>
        Table.TryGetValue(response, out SpecSet[]? sets)
            ? [.. sets.SelectMany(static s => s.Methods).Distinct().Select(MethodName)]
            : throw new ArgumentException("Filter response is not valid.", nameof(response));

    /// <summary>Whether the name is one of the responses this table carries.</summary>
    public static bool IsResponse(string response) => Table.ContainsKey(response);

    /// <summary>The name <c>designfilt</c> uses for a method.</summary>
    public static string MethodName(Algorithm method) => method switch
    {
        Algorithm.Butterworth => "butter",
        Algorithm.Chebyshev1 => "cheby1",
        Algorithm.Chebyshev2 => "cheby2",
        Algorithm.Elliptic => "ellip",
        Algorithm.Maxflat => "maxflat",
        Algorithm.Window => "window",
        Algorithm.KaiserWindow => "kaiserwin",
        Algorithm.Equiripple => "equiripple",
        Algorithm.LeastSquares => "ls",
        Algorithm.ConstrainedLeastSquares => "cls",
        _ => "freqsamp",
    };

    /// <summary>The method a name stands for, or null when the name is not one.</summary>
    public static Algorithm? MethodFor(string name) => name switch
    {
        "butter" => Algorithm.Butterworth,
        "cheby1" => Algorithm.Chebyshev1,
        "cheby2" => Algorithm.Chebyshev2,
        "ellip" => Algorithm.Elliptic,
        "maxflat" => Algorithm.Maxflat,
        "window" => Algorithm.Window,
        "kaiserwin" => Algorithm.KaiserWindow,
        "equiripple" => Algorithm.Equiripple,
        "ls" => Algorithm.LeastSquares,
        "cls" => Algorithm.ConstrainedLeastSquares,
        "freqsamp" => Algorithm.FrequencySampling,
        _ => null,
    };

    /// <summary>
    /// Designs the filter a response, a set of named values and an optional method describe.
    /// </summary>
    /// <param name="response">The response name.</param>
    /// <param name="values">The parameter names and their values, in the order they were given.</param>
    /// <param name="method">The design method name, or null to take the set's default.</param>
    /// <param name="sampleRate">The sample rate the frequencies are in, or null for normalised.</param>
    /// <param name="options">The design options, or null for the method's own defaults.</param>
    public static Designed Design(
        string response,
        IReadOnlyList<(string Name, double[] Value)> values,
        string? method,
        double? sampleRate,
        Options? options)
    {
        if (!Table.TryGetValue(response, out SpecSet[]? sets))
        {
            throw new ArgumentException("Filter response is not valid.", nameof(response));
        }

        options ??= new Options();
        Algorithm? wanted = method is null ? null : MethodFor(method);
        if (method is not null && wanted is null)
        {
            throw new ArgumentException($"'{method}' is not a valid design method.", nameof(method));
        }

        SpecSet matched = Match(response, sets, values, method, wanted);
        Algorithm algorithm = wanted ?? Default(matched.Methods);

        double nyquist = (sampleRate ?? 2) / 2;
        var lookup = new Dictionary<string, double[]>(StringComparer.Ordinal);
        foreach ((string name, double[] value) in values)
        {
            lookup[name] = value;
        }

        double Frequency(string name) => lookup[name][0] / nyquist;
        double Level(string name) => lookup[name][0];
        int Order(string name) => (int)lookup[name][0];

        var ordered = new List<(string Name, double[] Value)>();
        foreach (string property in matched.Properties)
        {
            ordered.Add((property, lookup[property]));
        }

        string band = response.EndsWith("fir", StringComparison.Ordinal)
            ? response[..^3]
            : response[..^3];
        string impulse = response.EndsWith("fir", StringComparison.Ordinal) ? "fir" : "iir";

        Designed Finish(double[]? taps, double[,]? sections) => new(
            response, band, impulse, MethodName(algorithm), sampleRate ?? 2, taps, sections, ordered);

        if (impulse == "iir")
        {
            return Finish(null, CascadeSections(
                response, matched.Properties, algorithm, Frequency, Level, Order, options));
        }

        return Finish(
            FirTaps(response, matched.Properties, algorithm, Frequency, Level, Order, lookup, nyquist, options),
            null);
    }

    // --- The table -----------------------------------------------------------------------------

    private static Dictionary<string, SpecSet[]> BuildTable()
    {
        Algorithm[] classic =
        [
            Algorithm.Butterworth, Algorithm.Chebyshev1, Algorithm.Chebyshev2, Algorithm.Elliptic,
        ];
        Algorithm[] eqripKaiser = [Algorithm.Equiripple, Algorithm.KaiserWindow];
        Algorithm[] eqripLs = [Algorithm.Equiripple, Algorithm.LeastSquares];

        return new Dictionary<string, SpecSet[]>(StringComparer.Ordinal)
        {
            ["lowpassfir"] =
            [
                new(["FilterOrder", "CutoffFrequency"], [Algorithm.Window]),
                new(["FilterOrder", "CutoffFrequency", "PassbandRipple", "StopbandAttenuation"],
                    [Algorithm.ConstrainedLeastSquares]),
                new(["FilterOrder", "HalfPowerFrequency"], [Algorithm.Maxflat]),
                new(["FilterOrder", "PassbandFrequency", "StopbandFrequency"], eqripLs),
                new(["PassbandFrequency", "StopbandFrequency", "PassbandRipple", "StopbandAttenuation"],
                    eqripKaiser),
            ],
            ["lowpassiir"] =
            [
                new(["FilterOrder", "HalfPowerFrequency"], [Algorithm.Butterworth]),
                new(["FilterOrder", "PassbandFrequency", "PassbandRipple"], [Algorithm.Chebyshev1]),
                new(["FilterOrder", "PassbandFrequency", "PassbandRipple", "StopbandAttenuation"],
                    [Algorithm.Elliptic]),
                new(["FilterOrder", "StopbandFrequency", "StopbandAttenuation"], [Algorithm.Chebyshev2]),
                new(["PassbandFrequency", "StopbandFrequency", "PassbandRipple", "StopbandAttenuation"],
                    classic),
            ],
            ["highpassfir"] =
            [
                new(["FilterOrder", "CutoffFrequency"], [Algorithm.Window]),
                new(["FilterOrder", "CutoffFrequency", "StopbandAttenuation", "PassbandRipple"],
                    [Algorithm.ConstrainedLeastSquares]),
                new(["FilterOrder", "StopbandFrequency", "PassbandFrequency"], eqripLs),
                new(["StopbandFrequency", "PassbandFrequency", "StopbandAttenuation", "PassbandRipple"],
                    eqripKaiser),
            ],
            ["highpassiir"] =
            [
                new(["FilterOrder", "HalfPowerFrequency"], [Algorithm.Butterworth]),
                new(["FilterOrder", "PassbandFrequency", "PassbandRipple"], [Algorithm.Chebyshev1]),
                new(["FilterOrder", "PassbandFrequency", "StopbandAttenuation", "PassbandRipple"],
                    [Algorithm.Elliptic]),
                new(["FilterOrder", "StopbandFrequency", "StopbandAttenuation"], [Algorithm.Chebyshev2]),
                new(["StopbandFrequency", "PassbandFrequency", "StopbandAttenuation", "PassbandRipple"],
                    classic),
            ],
            ["bandpassfir"] =
            [
                new(["FilterOrder", "CutoffFrequency1", "CutoffFrequency2"], [Algorithm.Window]),
                new(
                [
                    "FilterOrder", "CutoffFrequency1", "CutoffFrequency2",
                    "StopbandAttenuation1", "PassbandRipple", "StopbandAttenuation2",
                ], [Algorithm.ConstrainedLeastSquares]),
                new(
                [
                    "FilterOrder", "StopbandFrequency1", "PassbandFrequency1",
                    "PassbandFrequency2", "StopbandFrequency2",
                ], eqripLs),
                new(
                [
                    "StopbandFrequency1", "PassbandFrequency1", "PassbandFrequency2", "StopbandFrequency2",
                    "StopbandAttenuation1", "PassbandRipple", "StopbandAttenuation2",
                ], eqripKaiser),
            ],
            ["bandpassiir"] =
            [
                new(["FilterOrder", "HalfPowerFrequency1", "HalfPowerFrequency2"], [Algorithm.Butterworth]),
                new(["FilterOrder", "PassbandFrequency1", "PassbandFrequency2", "PassbandRipple"],
                    [Algorithm.Chebyshev1]),
                new(
                [
                    "FilterOrder", "PassbandFrequency1", "PassbandFrequency2",
                    "StopbandAttenuation1", "PassbandRipple", "StopbandAttenuation2",
                ], [Algorithm.Elliptic]),
                new(["FilterOrder", "StopbandFrequency1", "StopbandFrequency2", "StopbandAttenuation"],
                    [Algorithm.Chebyshev2]),
                new(
                [
                    "StopbandFrequency1", "PassbandFrequency1", "PassbandFrequency2", "StopbandFrequency2",
                    "StopbandAttenuation1", "PassbandRipple", "StopbandAttenuation2",
                ], classic),
            ],
            ["bandstopfir"] =
            [
                new(["FilterOrder", "CutoffFrequency1", "CutoffFrequency2"], [Algorithm.Window]),
                new(
                [
                    "FilterOrder", "CutoffFrequency1", "CutoffFrequency2",
                    "PassbandRipple1", "StopbandAttenuation", "PassbandRipple2",
                ], [Algorithm.ConstrainedLeastSquares]),
                new(
                [
                    "FilterOrder", "PassbandFrequency1", "StopbandFrequency1",
                    "StopbandFrequency2", "PassbandFrequency2",
                ], eqripLs),
                new(
                [
                    "PassbandFrequency1", "StopbandFrequency1", "StopbandFrequency2", "PassbandFrequency2",
                    "PassbandRipple1", "StopbandAttenuation", "PassbandRipple2",
                ], eqripKaiser),
            ],
            ["bandstopiir"] =
            [
                new(["FilterOrder", "HalfPowerFrequency1", "HalfPowerFrequency2"], [Algorithm.Butterworth]),
                new(["FilterOrder", "PassbandFrequency1", "PassbandFrequency2", "PassbandRipple"],
                    [Algorithm.Chebyshev1]),
                new(
                [
                    "FilterOrder", "PassbandFrequency1", "PassbandFrequency2",
                    "PassbandRipple", "StopbandAttenuation",
                ], [Algorithm.Elliptic]),
                new(["FilterOrder", "StopbandFrequency1", "StopbandFrequency2", "StopbandAttenuation"],
                    [Algorithm.Chebyshev2]),
                new(
                [
                    "PassbandFrequency1", "StopbandFrequency1", "StopbandFrequency2", "PassbandFrequency2",
                    "PassbandRipple1", "StopbandAttenuation", "PassbandRipple2",
                ], classic),
            ],
            ["differentiatorfir"] =
            [
                new(["FilterOrder"], eqripLs),
                new(["FilterOrder", "PassbandFrequency", "StopbandFrequency"], eqripLs),
            ],
            ["hilbertfir"] =
            [
                new(["FilterOrder", "TransitionWidth"], eqripLs),
            ],
        };
    }

    /// <summary>
    /// The set the given parameter names pick out, or the error that lists what would have worked.
    /// </summary>
    /// <remarks>
    /// The reference distinguishes a set that is short of the mark from one that overshoots it and
    /// from one that half matches, and phrases its advice accordingly. Here the three become one
    /// message that lists the sets, because a caller who has the list can see which of the three it
    /// was.
    /// </remarks>
    private static SpecSet Match(
        string response,
        SpecSet[] sets,
        IReadOnlyList<(string Name, double[] Value)> values,
        string? method,
        Algorithm? wanted)
    {
        var given = new HashSet<string>(values.Select(static v => v.Name), StringComparer.Ordinal);
        SpecSet? exact = null;
        foreach (SpecSet set in sets)
        {
            if (set.Properties.Length == given.Count && set.Properties.All(given.Contains))
            {
                exact = set;
                break;
            }
        }

        if (exact is null)
        {
            string list = string.Join("\n", sets.Select(static s => "  - " + string.Join(", ", s.Properties)));
            throw new ArgumentException(
                $"You have specified an invalid set of parameters for '{response}'.\n"
                + $"The following are the valid parameter sets:\n{list}");
        }

        if (wanted is { } algorithm && !exact.Methods.Contains(algorithm))
        {
            throw new ArgumentException(
                $"'{method}' is an invalid design method for {response} designs with "
                + $"{string.Join(", ", exact.Properties)} specifications.");
        }

        return exact;
    }

    /// <summary>
    /// The method a set falls back on when the caller named none: frequency sampling first,
    /// equiripple next, Butterworth next, and otherwise whichever the set lists first.
    /// </summary>
    private static Algorithm Default(Algorithm[] methods)
    {
        if (methods.Contains(Algorithm.FrequencySampling))
        {
            return Algorithm.FrequencySampling;
        }

        if (methods.Contains(Algorithm.Equiripple))
        {
            return Algorithm.Equiripple;
        }

        return methods.Contains(Algorithm.Butterworth) ? Algorithm.Butterworth : methods[0];
    }

    // --- The IIR half --------------------------------------------------------------------------

    private static double[,] CascadeSections(
        string response,
        string[] properties,
        Algorithm algorithm,
        Func<string, double> frequency,
        Func<string, double> level,
        Func<string, int> order,
        Options options)
    {
        var set = new HashSet<string>(properties, StringComparer.Ordinal);
        CascadeBand band = response.StartsWith("lowpass", StringComparison.Ordinal) ? CascadeBand.Lowpass
            : response.StartsWith("highpass", StringComparison.Ordinal) ? CascadeBand.Highpass
            : response.StartsWith("bandpass", StringComparison.Ordinal) ? CascadeBand.Bandpass
            : CascadeBand.Bandstop;
        bool wide = band is CascadeBand.Bandpass or CascadeBand.Bandstop;

        CascadeMethod method = algorithm switch
        {
            Algorithm.Butterworth => CascadeMethod.Butterworth,
            Algorithm.Chebyshev1 => CascadeMethod.Chebyshev1,
            Algorithm.Chebyshev2 => CascadeMethod.Chebyshev2,
            _ => CascadeMethod.Elliptic,
        };

        bool minimum = !set.Contains("FilterOrder");
        double[] edges;
        double[] stops = [];
        double rp = 0;
        double rs = 0;

        if (minimum)
        {
            edges = wide
                ? [frequency("PassbandFrequency1"), frequency("PassbandFrequency2")]
                : [frequency("PassbandFrequency")];
            stops = wide
                ? [frequency("StopbandFrequency1"), frequency("StopbandFrequency2")]
                : [frequency("StopbandFrequency")];
            rp = band == CascadeBand.Bandstop
                ? System.Math.Min(level("PassbandRipple1"), level("PassbandRipple2"))
                : level("PassbandRipple");
            rs = band == CascadeBand.Bandpass
                ? System.Math.Max(level("StopbandAttenuation1"), level("StopbandAttenuation2"))
                : level("StopbandAttenuation");
        }
        else
        {
            // The frequency a specification set names is the frequency the prototype is placed at,
            // and which one that is follows from the method: half-power for a Butterworth, passband
            // for a Chebyshev I or elliptic, stopband for a Chebyshev II.
            string prefix = set.Contains("HalfPowerFrequency") || set.Contains("HalfPowerFrequency1")
                ? "HalfPowerFrequency"
                : set.Contains("PassbandFrequency") || set.Contains("PassbandFrequency1")
                    ? "PassbandFrequency"
                    : "StopbandFrequency";
            edges = wide
                ? [frequency(prefix + "1"), frequency(prefix + "2")]
                : [frequency(prefix)];
            if (set.Contains("PassbandRipple"))
            {
                rp = level("PassbandRipple");
            }

            if (set.Contains("StopbandAttenuation"))
            {
                rs = level("StopbandAttenuation");
            }
            else if (set.Contains("StopbandAttenuation1"))
            {
                rs = System.Math.Max(level("StopbandAttenuation1"), level("StopbandAttenuation2"));
            }
        }

        MatchBand match = options.MatchExactly switch
        {
            "passband" => MatchBand.Passband,
            "both" => MatchBand.Both,
            "stopband" => MatchBand.Stopband,
            _ => method switch
            {
                CascadeMethod.Chebyshev1 => MatchBand.Passband,
                CascadeMethod.Elliptic => MatchBand.Both,
                _ => MatchBand.Stopband,
            },
        };

        CascadeDesign.Cascade cascade = CascadeDesign.Design(new CascadeDesign.Request(
            band, method, minimum ? 0 : order("FilterOrder"), edges, stops, rp, rs, match));
        return Fold(cascade);
    }

    /// <summary>
    /// A cascade with its scale values folded into the numerators, which is the form
    /// <c>digitalFilter</c> keeps its coefficients in.
    /// </summary>
    public static double[,] Fold(CascadeDesign.Cascade cascade)
    {
        int rows = cascade.Sections.GetLength(0);
        var numerators = new double[rows * 3];
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                numerators[i + (j * rows)] = cascade.Sections[i, j];
            }
        }

        double[] scaled = SecondOrderSections.ScaleSections(numerators, rows, 3, cascade.ScaleValues);
        var folded = new double[rows, 6];
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                folded[i, j] = scaled[i + (j * rows)];
                folded[i, j + 3] = cascade.Sections[i, j + 3];
            }
        }

        return folded;
    }

    // --- The FIR half --------------------------------------------------------------------------

    private static double[] FirTaps(
        string response,
        string[] properties,
        Algorithm algorithm,
        Func<string, double> frequency,
        Func<string, double> level,
        Func<string, int> order,
        Dictionary<string, double[]> lookup,
        double nyquist,
        Options options)
    {
        var set = new HashSet<string>(properties, StringComparer.Ordinal);
        return response switch
        {
            "differentiatorfir" => Differentiator(set, algorithm, frequency, order, options),
            "hilbertfir" => Hilbert(algorithm, frequency, order, options),
            _ => BandFir(response, set, algorithm, frequency, level, order, options),
        };
    }

    private static double[] Differentiator(
        HashSet<string> set, Algorithm algorithm, Func<string, double> frequency,
        Func<string, int> order, Options options)
    {
        int n = order("FilterOrder");
        bool banded = set.Contains("PassbandFrequency");

        // A full-band differentiator is a type IV filter and needs an odd order; a banded one is a
        // type III and needs an even one. The parity is not a preference either way — the wrong one
        // has a zero where the response is asked to be largest.
        if (banded == (n % 2 != 0))
        {
            throw new ArgumentException(banded
                ? "The filter order must be even. Use the 'N' specification for odd orders."
                : "The filter order must be odd. Use the 'N,Fp,Fst' specification for even orders.");
        }

        if (!banded)
        {
            double[] weights = options.Weights ?? [1];
            return algorithm == Algorithm.Equiripple
                ? FirDesign.Remez(n, [0, 1], [0, System.Math.PI], weights,
                    LinearPhaseType.Differentiator, FirDesign.DefaultGridDensity).H
                : FirWindowDesign.LeastSquares(n, [0, 1], [0, System.Math.PI], weights,
                    LinearPhaseType.Differentiator, out _);
        }

        double pass = frequency("PassbandFrequency");
        double stop = frequency("StopbandFrequency");
        double[] edges = [0, pass, stop, 1];
        double[] amplitudes = [0, pass * System.Math.PI, 0, 0];
        return algorithm == Algorithm.Equiripple
            ? FirDesign.Remez(n, edges, amplitudes, options.Weights ?? [1, 1],
                LinearPhaseType.Differentiator, FirDesign.DefaultGridDensity).H
            : FirWindowDesign.LeastSquares(n, edges, amplitudes, [],
                LinearPhaseType.Differentiator, out _);
    }

    private static double[] Hilbert(
        Algorithm algorithm, Func<string, double> frequency, Func<string, int> order, Options options)
    {
        int n = order("FilterOrder");
        double half = frequency("TransitionWidth") / 2;
        double[] edges = [half, 1 - half];
        double[] amplitudes = [1, 1];
        double[] weights = options.Weights ?? [1];
        return algorithm == Algorithm.Equiripple
            ? FirDesign.Remez(n, edges, amplitudes, weights, LinearPhaseType.Hilbert,
                FirDesign.DefaultGridDensity).H
            : FirWindowDesign.LeastSquares(n, edges, amplitudes, weights,
                LinearPhaseType.Hilbert, out _);
    }

    private static double[] BandFir(
        string response,
        HashSet<string> set,
        Algorithm algorithm,
        Func<string, double> frequency,
        Func<string, double> level,
        Func<string, int> order,
        Options options)
    {
        bool lowpass = response.StartsWith("lowpass", StringComparison.Ordinal);
        bool highpass = response.StartsWith("highpass", StringComparison.Ordinal);
        bool bandpass = response.StartsWith("bandpass", StringComparison.Ordinal);

        if (algorithm == Algorithm.Maxflat)
        {
            int n = order("FilterOrder");
            return ConstrainedFirDesign.FlatDesign(n, 0, frequency("HalfPowerFrequency"), symmetric: true).B;
        }

        if (algorithm == Algorithm.Window)
        {
            int n = order("FilterOrder");
            double[] cutoffs = set.Contains("CutoffFrequency")
                ? [frequency("CutoffFrequency")]
                : [frequency("CutoffFrequency1"), frequency("CutoffFrequency2")];
            WindowBandType? band = lowpass || bandpass
                ? null
                : highpass ? WindowBandType.High : WindowBandType.Stop;
            bool exception = !lowpass && !bandpass && n % 2 == 1;
            return FirWindowDesign.Windowed(
                n, cutoffs, band, options.Window ?? [], options.ScalePassband ?? true, exception, out _);
        }

        if (algorithm == Algorithm.ConstrainedLeastSquares)
        {
            return Constrained(response, frequency, level, order, options);
        }

        if (algorithm is Algorithm.Equiripple or Algorithm.LeastSquares && set.Contains("FilterOrder"))
        {
            (double[] edges, double[] amplitudes) = FixedBands(response, set, frequency);
            double[] weights = options.Weights ?? DefaultWeights(response);
            int n = order("FilterOrder");
            bool exception = (highpass || !lowpass && !bandpass) && n % 2 == 1;
            LinearPhaseType type = exception ? LinearPhaseType.Hilbert : LinearPhaseType.Symmetric;
            return algorithm == Algorithm.Equiripple
                ? FirDesign.Remez(n, edges, amplitudes, weights, type, FirDesign.DefaultGridDensity).H
                : FirWindowDesign.LeastSquares(n, edges, amplitudes, weights, type, out _);
        }

        return MinimumOrderFir(response, algorithm, frequency, level, options);
    }

    /// <summary>The band edges and amplitudes an order-given equiripple or least-squares design uses.</summary>
    private static (double[] Edges, double[] Amplitudes) FixedBands(
        string response, HashSet<string> set, Func<string, double> frequency)
    {
        _ = set;
        if (response.StartsWith("lowpass", StringComparison.Ordinal))
        {
            return ([0, frequency("PassbandFrequency"), frequency("StopbandFrequency"), 1], [1, 1, 0, 0]);
        }

        if (response.StartsWith("highpass", StringComparison.Ordinal))
        {
            return ([0, frequency("StopbandFrequency"), frequency("PassbandFrequency"), 1], [0, 0, 1, 1]);
        }

        if (response.StartsWith("bandpass", StringComparison.Ordinal))
        {
            return (
            [
                0, frequency("StopbandFrequency1"), frequency("PassbandFrequency1"),
                frequency("PassbandFrequency2"), frequency("StopbandFrequency2"), 1,
            ], [0, 0, 1, 1, 0, 0]);
        }

        return (
        [
            0, frequency("PassbandFrequency1"), frequency("StopbandFrequency1"),
            frequency("StopbandFrequency2"), frequency("PassbandFrequency2"), 1,
        ], [1, 1, 0, 0, 1, 1]);
    }

    private static double[] DefaultWeights(string response) =>
        response.StartsWith("bandpass", StringComparison.Ordinal)
        || response.StartsWith("bandstop", StringComparison.Ordinal)
            ? [1, 1, 1]
            : [1, 1];

    private static double[] Constrained(
        string response, Func<string, double> frequency, Func<string, double> level,
        Func<string, int> order, Options options)
    {
        bool zeroPhase = options.ZeroPhase ?? false;
        double[] offsets = options.PassbandOffset ?? [0];

        // The offset is an amplitude, not a deviation: a passband is centred on it, so a zero-dB
        // offset centres the band on one rather than on nothing.
        double Offset(int i) => System.Math.Pow(10, (i < offsets.Length ? offsets[i] : offsets[^1]) / 20);

        int n = order("FilterOrder");
        double Pass(string name) => PassbandDeviation(level(name) / 2);
        double Stop(string name) => StopbandDeviation(level(name));

        if (response.StartsWith("lowpass", StringComparison.Ordinal))
        {
            double upass = Pass("PassbandRipple");
            double ustop = Stop("StopbandAttenuation");
            double[] up = [upass + Offset(0), ustop];
            double[] lo = [-upass + Offset(0), zeroPhase ? 0 : -ustop];
            double[] a = [(up[0] + lo[0]) / 2, 0];
            return ConstrainedFirDesign.ConstrainedLeastSquares(
                n, [0, frequency("CutoffFrequency"), 1], a, up, lo, out _);
        }

        if (response.StartsWith("highpass", StringComparison.Ordinal))
        {
            double upass = Pass("PassbandRipple");
            double ustop = Stop("StopbandAttenuation");
            double[] up = [ustop, upass + Offset(0)];
            double[] lo = [zeroPhase ? 0 : -ustop, -upass + Offset(0)];
            double[] a = [0, (up[1] + lo[1]) / 2];
            return ConstrainedFirDesign.ConstrainedLeastSquares(
                n, [0, frequency("CutoffFrequency"), 1], a, up, lo, out _);
        }

        if (response.StartsWith("bandpass", StringComparison.Ordinal))
        {
            double upass = Pass("PassbandRipple");
            double ustop1 = Stop("StopbandAttenuation1");
            double ustop2 = Stop("StopbandAttenuation2");
            double[] up = [ustop1, upass + Offset(0), ustop2];
            double[] lo = [zeroPhase ? 0 : -ustop1, -upass + Offset(0), zeroPhase ? 0 : -ustop2];
            double[] a = [0, (up[1] + lo[1]) / 2, 0];
            return ConstrainedFirDesign.ConstrainedLeastSquares(
                n, [0, frequency("CutoffFrequency1"), frequency("CutoffFrequency2"), 1], a, up, lo, out _);
        }

        double upass1 = Pass("PassbandRipple1");
        double upass2 = Pass("PassbandRipple2");
        double stop = Stop("StopbandAttenuation");
        double[] upper = [upass1 + Offset(0), stop, upass2 + Offset(1)];
        double[] lower = [-upass1 + Offset(0), zeroPhase ? 0 : -stop, -upass2 + Offset(1)];
        double[] amplitudes = [(upper[0] + lower[0]) / 2, 0, (upper[2] + lower[2]) / 2];
        return ConstrainedFirDesign.ConstrainedLeastSquares(
            n, [0, frequency("CutoffFrequency1"), frequency("CutoffFrequency2"), 1],
            amplitudes, upper, lower, out _);
    }

    /// <summary><c>convertmagunits</c> for a passband ripple: the deviation a dB figure allows.</summary>
    internal static double PassbandDeviation(double db) =>
        (System.Math.Pow(10, db / 20) - 1) / (System.Math.Pow(10, db / 20) + 1);

    /// <summary><c>convertmagunits</c> for a stopband attenuation.</summary>
    internal static double StopbandDeviation(double db) => System.Math.Pow(10, -db / 20);

    /// <summary>
    /// The two minimum-order FIR designs, which agree in shape: estimate an order, design, measure,
    /// and grow the order until the specification is actually met.
    /// </summary>
    /// <remarks>
    /// The estimates are empirical fits and can be a tap or two short, which is why neither method
    /// trusts its own estimate. The measurement is a response on a fixed grid — four thousand points
    /// across each stopband and a thousand across each passband — and the loop stops at the first
    /// order that passes, or gives the first design back when none of ten (or thirty-five) does.
    /// </remarks>
    private static double[] MinimumOrderFir(
        string response, Algorithm algorithm, Func<string, double> frequency,
        Func<string, double> level, Options options)
    {
        (double[] edges, double[] magnitudes, double[] deviations) = MinimumBands(response, frequency, level);
        (double[][] stopbands, double[][] passbands, double[] astop, double[] apass) =
            MeasurementBands(response, frequency, level);

        bool Met(double[] taps)
        {
            for (int i = 0; i < stopbands.Length; i++)
            {
                double peak = PeakResponse(taps, stopbands[i][0], stopbands[i][1], 4096);
                if (-20 * System.Math.Log10(peak) <= astop[i])
                {
                    return false;
                }
            }

            for (int i = 0; i < passbands.Length; i++)
            {
                (double high, double low) = Extremes(taps, passbands[i][0], passbands[i][1], 1024);
                if ((20 * System.Math.Log10(high)) - (20 * System.Math.Log10(low)) >= apass[i])
                {
                    return false;
                }
            }

            return true;
        }

        if (algorithm == Algorithm.KaiserWindow)
        {
            // A bandpass or bandstop Kaiser design equalises its two transition widths first — the
            // narrower one wins and the stopband edge moves — because one window has one shape and
            // cannot hold two different transitions.
            (double[] kaiserEdges, double[] kaiserMagnitudes, double[] kaiserDeviations) =
                Equalised(response, edges, magnitudes, deviations);
            FirWindowDesign.KaiserEstimate estimate =
                FirWindowDesign.KaiserOrder(kaiserEdges, kaiserMagnitudes, kaiserDeviations, 2);

            // A highpass or bandstop passes Nyquist, so its order has to be even whatever the
            // caller asked for: an odd-order symmetric design has a zero exactly where the response
            // is meant to be one.
            bool forceEven = options.MinOrder == "even"
                || response.StartsWith("highpass", StringComparison.Ordinal)
                || response.StartsWith("bandstop", StringComparison.Ordinal);
            int n = estimate.Order;
            if (options.MinOrder == "even" && n % 2 == 1)
            {
                n++;
            }

            double[] Windowed(int taps) => FirWindowDesign.Windowed(
                taps, estimate.Cutoffs, estimate.Type, SignalWindows.Kaiser(taps + 1, estimate.Beta),
                options.ScalePassband ?? true, hilbert: false, out _);

            double[] first = Windowed(n);
            if (Met(first))
            {
                return first;
            }

            for (int step = 0; step < 9; step++)
            {
                n++;
                if (forceEven && n % 2 == 1)
                {
                    n++;
                }

                double[] grown = Windowed(n);
                if (Met(grown))
                {
                    return grown;
                }
            }

            return first;
        }

        FirWindowDesign.RemezEstimate remez = FirWindowDesign.RemezOrder(edges, magnitudes, deviations, 2);
        int order = System.Math.Max(remez.Order, 3);
        double[] weights = remez.Weights;
        double[] design = FirDesign.Remez(
            order, remez.Frequencies, remez.Amplitudes, weights,
            LinearPhaseType.Symmetric, FirDesign.DefaultGridDensity).H;
        if (Met(design))
        {
            return design;
        }

        double[] cached = design;
        weights = Normalised(weights);
        for (int step = 0; step < 35; step++)
        {
            order++;
            try
            {
                design = FirDesign.Remez(
                    order, remez.Frequencies, remez.Amplitudes, weights,
                    LinearPhaseType.Symmetric, FirDesign.DefaultGridDensity).H;
            }
            catch (ArgumentException)
            {
                return cached;
            }

            if (Met(design))
            {
                return design;
            }
        }

        return cached;
    }

    /// <summary>
    /// The band edges a Kaiser design actually uses: the same as the specification's for a lowpass
    /// or highpass, and with the two transitions made equal for a bandpass or bandstop.
    /// </summary>
    private static (double[] Edges, double[] Magnitudes, double[] Deviations) Equalised(
        string response, double[] edges, double[] magnitudes, double[] deviations)
    {
        if (edges.Length != 4)
        {
            return (edges, magnitudes, deviations);
        }

        var moved = (double[])edges.Clone();
        if (response.StartsWith("bandpass", StringComparison.Ordinal))
        {
            double first = edges[1] - edges[0];
            double second = edges[3] - edges[2];
            if (first < second)
            {
                moved[3] = edges[2] + first;
            }
            else if (first > second)
            {
                moved[0] = edges[1] - second;
            }
        }
        else if (response.StartsWith("bandstop", StringComparison.Ordinal))
        {
            double first = edges[1] - edges[0];
            double second = edges[3] - edges[2];
            if (first < second)
            {
                moved[2] = edges[3] - first;
            }
            else if (first > second)
            {
                moved[1] = edges[0] + second;
            }
        }

        return (moved, magnitudes, deviations);
    }

    /// <summary>
    /// Deviations turned into weights, which is what the equiripple loop does before it grows the
    /// order — and only then, because the first design is made with the deviations themselves.
    /// </summary>
    private static double[] Normalised(double[] deviations)
    {
        foreach (double d in deviations)
        {
            if (d == 1)
            {
                return deviations;
            }
        }

        double most = deviations.Max();
        return [.. deviations.Select(d => most / d)];
    }

    private static (double[] Edges, double[] Magnitudes, double[] Deviations) MinimumBands(
        string response, Func<string, double> frequency, Func<string, double> level)
    {
        double Pass(string name) => PassbandDeviation(level(name));
        double Stop(string name) => StopbandDeviation(level(name));

        if (response.StartsWith("lowpass", StringComparison.Ordinal))
        {
            return (
                [frequency("PassbandFrequency"), frequency("StopbandFrequency")],
                [1, 0],
                [Pass("PassbandRipple"), Stop("StopbandAttenuation")]);
        }

        if (response.StartsWith("highpass", StringComparison.Ordinal))
        {
            return (
                [frequency("StopbandFrequency"), frequency("PassbandFrequency")],
                [0, 1],
                [Stop("StopbandAttenuation"), Pass("PassbandRipple")]);
        }

        if (response.StartsWith("bandpass", StringComparison.Ordinal))
        {
            return (
            [
                frequency("StopbandFrequency1"), frequency("PassbandFrequency1"),
                frequency("PassbandFrequency2"), frequency("StopbandFrequency2"),
            ],
                [0, 1, 0],
                [Stop("StopbandAttenuation1"), Pass("PassbandRipple"), Stop("StopbandAttenuation2")]);
        }

        return (
        [
            frequency("PassbandFrequency1"), frequency("StopbandFrequency1"),
            frequency("StopbandFrequency2"), frequency("PassbandFrequency2"),
        ],
            [1, 0, 1],
            [Pass("PassbandRipple1"), Stop("StopbandAttenuation"), Pass("PassbandRipple2")]);
    }

    private static (double[][] Stopbands, double[][] Passbands, double[] Astop, double[] Apass)
        MeasurementBands(string response, Func<string, double> frequency, Func<string, double> level)
    {
        if (response.StartsWith("lowpass", StringComparison.Ordinal))
        {
            return (
                [[frequency("StopbandFrequency"), 1]],
                [[0, frequency("PassbandFrequency")]],
                [level("StopbandAttenuation")],
                [level("PassbandRipple")]);
        }

        if (response.StartsWith("highpass", StringComparison.Ordinal))
        {
            return (
                [[0, frequency("StopbandFrequency")]],
                [[frequency("PassbandFrequency"), 1]],
                [level("StopbandAttenuation")],
                [level("PassbandRipple")]);
        }

        if (response.StartsWith("bandpass", StringComparison.Ordinal))
        {
            return (
                [[0, frequency("StopbandFrequency1")], [frequency("StopbandFrequency2"), 1]],
                [[frequency("PassbandFrequency1"), frequency("PassbandFrequency2")]],
                [level("StopbandAttenuation1"), level("StopbandAttenuation2")],
                [level("PassbandRipple")]);
        }

        return (
            [[frequency("StopbandFrequency1"), frequency("StopbandFrequency2")]],
            [[0, frequency("PassbandFrequency1")], [frequency("PassbandFrequency2"), 1]],
            [level("StopbandAttenuation")],
            [level("PassbandRipple1"), level("PassbandRipple2")]);
    }

    private static double PeakResponse(double[] taps, double from, double to, int points)
    {
        double most = 0;
        for (int i = 0; i < points; i++)
        {
            double f = points == 1 ? from : from + ((to - from) * i / (points - 1.0));
            double magnitude = System.Numerics.Complex.Abs(
                FilterAnalysis.ResponseAt(taps, [1], [f], 2)[0]);
            most = System.Math.Max(most, magnitude);
        }

        return most;
    }

    private static (double High, double Low) Extremes(double[] taps, double from, double to, int points)
    {
        double high = 0;
        double low = double.PositiveInfinity;
        for (int i = 0; i < points; i++)
        {
            double f = points == 1 ? from : from + ((to - from) * i / (points - 1.0));
            double magnitude = System.Numerics.Complex.Abs(
                FilterAnalysis.ResponseAt(taps, [1], [f], 2)[0]);
            high = System.Math.Max(high, magnitude);
            low = System.Math.Min(low, magnitude);
        }

        return (high, low);
    }
}
