using JGraph.Signal;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>designfilt</c>, the <c>digitalFilter</c> value it returns, and the four one-line filters that
/// design one and use it in the same breath (M135).
/// </summary>
/// <remarks>
/// <para>
/// A <c>digitalFilter</c> is a value here rather than an object, tagged with its class name the way
/// M62's exception and M129's decomposition are. That is what lets <c>class(d)</c> answer
/// <c>digitalFilter</c>, <c>d.PassbandFrequency</c> reach the specification it was designed from, and
/// every analysis name take it in place of a numerator without learning a new kind of argument: each
/// of them asks <see cref="TryFilterCoefficients"/> first and carries on with the coefficients it
/// gets back.
/// </para>
/// <para>
/// The four verbs are one design and one filtering pass each, but neither half is a default. The
/// design comes from <see cref="ConvenienceFilters"/>, which decides FIR against IIR by comparing
/// the order the specification needs against the length of the signal in hand; the filtering follows
/// that decision, a delay-compensated forward pass for an FIR filter and a <c>filtfilt</c> for an
/// IIR one. A caller who takes the second output gets the filter and can see which it was.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>What <c>class(d)</c> reports for a designed filter.</summary>
    internal const string DigitalFilterClass = "digitalFilter";

    /// <summary>Registers <c>designfilt</c>, <c>digitalFilter</c> and the four one-line filters.</summary>
    internal static void RegisterDesignFiltBuiltins(JgsEnvironment env, JGraphScriptGlobals host)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        void DefineMulti(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> body) =>
            Define(name, (args, line, col) => body(args, 1, line, col)[0], body);

        Define("designfilt", (args, line, col) => DesignFilter(env, host, args, line, col));
        Define(DigitalFilterClass, DigitalFilterValue);

        Define("isfir", (args, line, col) =>
        {
            ArityRange("isfir", args, 1, 1, line, col);
            return JgsValue.Bool(FilterOf("isfir", args[0], line, col).Fir);
        });
        Define("isdouble", (args, line, col) =>
        {
            ArityRange("isdouble", args, 1, 1, line, col);
            _ = FilterOf("isdouble", args[0], line, col);
            return JgsValue.True;
        });
        Define("issingle", (args, line, col) =>
        {
            ArityRange("issingle", args, 1, 1, line, col);
            _ = FilterOf("issingle", args[0], line, col);
            return JgsValue.False;
        });
        Define("info", (args, line, col) => FilterInformation(args, line, col));

        DefineMulti("tf", TransferFunctionOf);
        DefineMulti("zpk", RootsOf);
        DefineMulti("ss", StateSpaceOf);

        foreach (string verb in (string[])["lowpass", "highpass", "bandpass", "bandstop"])
        {
            string name = verb;
            DefineMulti(name, (args, wanted, line, col) =>
                ApplyConvenience(env, host, name, args, wanted, line, col));
        }
    }

    /// <summary>Whether a value is a designed filter.</summary>
    internal static bool IsDigitalFilter(JgsValue value) =>
        value.Type == JgsType.Struct && !value.IsStructArray && value.ClassName == DigitalFilterClass;

    /// <summary>
    /// The numerator and denominator a designed filter stands for, or false when the value is not
    /// one. An IIR filter's sections are multiplied out, which is the one place a cascade's
    /// conditioning is given up.
    /// </summary>
    internal static bool TryFilterCoefficients(JgsValue value, out double[] b, out double[] a)
    {
        b = [];
        a = [1];
        if (!IsDigitalFilter(value))
        {
            return false;
        }

        (double[] taps, double[] denominator, _, _) = Unpack(value);
        b = taps;
        a = denominator;
        return true;
    }

    /// <summary>The sections a designed IIR filter carries, or false for an FIR one.</summary>
    internal static bool TryFilterSections(JgsValue value, out double[] sos, out int rows)
    {
        sos = [];
        rows = 0;
        if (!IsDigitalFilter(value))
        {
            return false;
        }

        (_, _, double[]? sections, int count) = Unpack(value);
        if (sections is null)
        {
            return false;
        }

        sos = sections;
        rows = count;
        return true;
    }

    /// <summary>The filter a value holds, refusing anything else by name.</summary>
    private static (double[] B, double[] A, double[]? Sos, int Rows, bool Fir) FilterOf(
        string name, JgsValue value, int line, int col)
    {
        if (!IsDigitalFilter(value))
        {
            throw new JgsRuntimeException(line, col,
                $"{name} needs a digitalFilter; use designfilt to make one.");
        }

        (double[] b, double[] a, double[]? sos, int rows) = Unpack(value);
        return (b, a, sos, rows, sos is null);
    }

    /// <summary>The four pieces a designed filter's struct holds, read back out.</summary>
    private static (double[] B, double[] A, double[]? Sos, int Rows) Unpack(JgsValue value)
    {
        Dictionary<string, JgsValue> fields = value.AsStruct;
        JgsValue coefficients = fields["Coefficients"];
        if (fields["ImpulseResponse"].AsString == "fir")
        {
            double[] taps = ColumnMajorOf(coefficients);
            return (taps, [1.0], null, 0);
        }

        double[] sos = ColumnMajorOf(coefficients);
        int rows = sos.Length / 6;
        (double[] b, double[] a) = SecondOrderSections.ToTransferFunction(sos, rows, 1);
        return (b, a, sos, rows);
    }

    /// <summary>A numeric value's elements, column by column.</summary>
    private static double[] ColumnMajorOf(JgsValue value)
    {
        int count = value.Type == JgsType.Array ? value.Rows * value.Cols : 1;
        var flat = new double[count];
        for (int i = 0; i < count; i++)
        {
            flat[i] = value.Type == JgsType.Array ? value.ElementAt(i).AsNumber : value.AsNumber;
        }

        return flat;
    }

    // --- designfilt --------------------------------------------------------------------------------

    /// <summary><c>d = designfilt(response, name, value, ...)</c>.</summary>
    private static JgsValue DesignFilter(
        JgsEnvironment env, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "signal:designfilt:MustSpecifyValidSpecifications",
                "designfilt needs a filter response and a set of specifications; the interactive "
                + "design assistant is not available.");
        }

        string response = StrOf("designfilt", args[0], line, col);
        if (!FilterSpecification.IsResponse(response))
        {
            throw new JgsRuntimeException(line, col, "signal:designfilt:FilterResponseIsNotValid",
                "Filter response is not valid.");
        }

        if (args.Count == 1)
        {
            throw new JgsRuntimeException(line, col, "signal:designfilt:MustSpecifyValidSpecifications",
                $"You have specified too few parameters for '{response}'.\n"
                + "The following are the valid parameter sets:\n"
                + string.Join("\n", FilterSpecification.ParameterSets(response).Select(static s => "  - " + s)));
        }

        if ((args.Count - 1) % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, "signal:designfilt:InputsMustBePVP",
                "Specifications must be given as name-value pairs.");
        }

        var values = new List<(string Name, double[] Value)>();
        var options = new FilterSpecification.Options();
        string? method = null;
        double? sampleRate = null;
        int order = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 1; i < args.Count; i += 2)
        {
            string name = StrOf("designfilt", args[i], line, col);
            JgsValue given = args[i + 1];
            if (!seen.Add(name))
            {
                throw new JgsRuntimeException(line, col, "signal:designfilt:RepeatedParam",
                    $"The parameter {name} has been specified more than once.");
            }

            switch (name)
            {
                case "SampleRate":
                    sampleRate = Num("designfilt", args, i + 1, line, col);
                    break;
                case "DesignMethod":
                    method = StrOf("designfilt", given, line, col);
                    break;
                case "Window":
                    options.Window = WindowSamples(env, host, given, order, line, col);
                    break;
                case "ScalePassband":
                    options.ScalePassband = given.AsBool;
                    break;
                case "ZeroPhase":
                    options.ZeroPhase = given.AsBool;
                    break;
                case "PassbandOffset":
                    options.PassbandOffset = ColumnMajorOf(given);
                    break;
                case "MinOrder":
                    options.MinOrder = StrOf("designfilt", given, line, col);
                    break;
                case "MatchExactly":
                    options.MatchExactly = StrOf("designfilt", given, line, col);
                    break;
                case "PassbandWeight" or "StopbandWeight" or "PassbandWeight1" or "PassbandWeight2"
                    or "StopbandWeight1" or "StopbandWeight2" or "Weights":
                    options.Weights = (options.Weights ?? []).Concat(ColumnMajorOf(given)).ToArray();
                    break;
                default:
                    double[] numbers = ColumnMajorOf(given);
                    if (name == "FilterOrder")
                    {
                        order = (int)numbers[0];
                    }

                    values.Add((name, numbers));
                    break;
            }
        }

        // The window is evaluated at the filter's length, so a call that named the window before the
        // order has to be finished once the order is known.
        if (options.Window is null && seen.Contains("Window"))
        {
            options.Window = [];
        }

        FilterSpecification.Designed designed;
        try
        {
            designed = FilterSpecification.Design(response, values, method, sampleRate, options);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, "signal:designfilt:MustSpecifyValidSpecifications",
                ex.Message);
        }
        catch (NotSupportedException ex)
        {
            throw new JgsRuntimeException(line, col, ex.Message);
        }

        return Packed(designed);
    }

    /// <summary>The window a design option names, evaluated at the filter's length.</summary>
    private static double[] WindowSamples(
        JgsEnvironment env, JGraphScriptGlobals host, JgsValue given, int order, int line, int col)
    {
        _ = host;
        if (given.Type == JgsType.Array && !given.IsStringArray && given.Rows * given.Cols > 1)
        {
            return ColumnMajorOf(given);
        }

        var call = new List<JgsValue> { JgsValue.Number(order + 1) };
        string name;
        if (given.Type == JgsType.Cell)
        {
            JgsValue[] parts = given.AsCell;
            name = StrOf("designfilt", parts[0], line, col);
            for (int i = 1; i < parts.Length; i++)
            {
                call.Add(parts[i]);
            }
        }
        else if (given.Type == JgsType.Function)
        {
            return ColumnMajorOf(given.AsCallable!.Call(call, line, col));
        }
        else
        {
            name = StrOf("designfilt", given, line, col);
        }

        if (!env.TryGet(name, out JgsValue window) || window.AsCallable is not { } callable)
        {
            throw new JgsRuntimeException(line, col, $"designfilt does not know the window '{name}'.");
        }

        return ColumnMajorOf(callable.Call(call, line, col));
    }

    /// <summary>The designed filter as the tagged struct the rest of the session sees.</summary>
    private static JgsValue Packed(FilterSpecification.Designed designed)
    {
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        if (designed.Taps is { } taps)
        {
            fields["Coefficients"] = Numbers(taps);
            fields["Numerator"] = Numbers(taps);
            fields["Denominator"] = JgsValue.Number(1);
        }
        else
        {
            double[,] sections = designed.Sections!;
            int rows = sections.GetLength(0);
            var flat = new double[rows * 6];
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < 6; j++)
                {
                    flat[i + (j * rows)] = sections[i, j];
                }
            }

            fields["Coefficients"] = JgsMatrix.FromColumnMajor(flat, rows, 6);
            fields["Numerator"] = JgsMatrix.FromColumnMajor(Half(flat, rows, 0), rows, 3);
            fields["Denominator"] = JgsMatrix.FromColumnMajor(Half(flat, rows, 3), rows, 3);
        }

        fields["FrequencyResponse"] = JgsValue.Str(designed.FrequencyResponse);
        fields["ImpulseResponse"] = JgsValue.Str(designed.ImpulseResponse);
        fields["SampleRate"] = JgsValue.Number(designed.SampleRate);
        foreach ((string name, double[] value) in designed.Specifications)
        {
            fields[name] = value.Length == 1 ? JgsValue.Number(value[0]) : Numbers(value);
        }

        fields["DesignMethod"] = JgsValue.Str(designed.DesignMethod);

        JgsValue packed = JgsValue.Struct(fields);
        packed.SetClassName(DigitalFilterClass);
        return packed;
    }

    /// <summary>The numerator or denominator half of a section matrix.</summary>
    private static double[] Half(double[] flat, int rows, int from)
    {
        var half = new double[rows * 3];
        Array.Copy(flat, from * rows, half, 0, rows * 3);
        return half;
    }

    /// <summary><c>digitalFilter(d)</c>: the value it was given, once it is one.</summary>
    private static JgsValue DigitalFilterValue(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("digitalFilter", args, 1, 1, line, col);
        if (!IsDigitalFilter(args[0]))
        {
            throw new JgsRuntimeException(line, col,
                "digitalFilter takes a filter that designfilt made; there is no other constructor.");
        }

        return args[0];
    }

    // --- The methods a designed filter answers to ---------------------------------------------------

    /// <summary><c>[b, a] = tf(d)</c>.</summary>
    private static JgsValue[] TransferFunctionOf(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("tf", args, 1, 1, line, col);
        (double[] b, double[] a, _, _, _) = FilterOf("tf", args[0], line, col);
        return wanted >= 2 ? [Numbers(b), Numbers(a)] : [Numbers(b)];
    }

    /// <summary><c>[z, p, k] = zpk(d)</c>.</summary>
    private static JgsValue[] RootsOf(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("zpk", args, 1, 1, line, col);
        (double[] b, double[] a, _, _, _) = FilterOf("zpk", args[0], line, col);
        FilterCoefficients.Zpk roots = FilterCoefficients.TfToZpk(b, a);
        JgsValue[] all =
        [
            ComplexColumn(roots.Zeros),
            ComplexColumn(roots.Poles),
            JgsValue.Number(roots.Gains.Length > 0 ? roots.Gains[0].Real : 1),
        ];
        return all[..System.Math.Clamp(wanted, 1, 3)];
    }

    /// <summary><c>[A, B, C, D] = ss(d)</c>.</summary>
    private static JgsValue[] StateSpaceOf(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("ss", args, 1, 1, line, col);
        (double[] b, double[] a, _, _, _) = FilterOf("ss", args[0], line, col);
        FilterCoefficients.StateSpace block = FilterCoefficients.TfToSs(b, 1, b.Length, a);
        JgsValue[] all =
        [
            JgsMatrix.FromColumnMajor(Flatten(block.A), block.A.GetLength(0), block.A.GetLength(1)),
            JgsMatrix.FromColumnMajor(Flatten(block.B), block.B.GetLength(0), block.B.GetLength(1)),
            JgsMatrix.FromColumnMajor(Flatten(block.C), block.C.GetLength(0), block.C.GetLength(1)),
            JgsMatrix.FromColumnMajor(Flatten(block.D), block.D.GetLength(0), block.D.GetLength(1)),
        ];
        return all[..System.Math.Clamp(wanted, 1, 4)];
    }

    /// <summary>A rectangular array read column by column.</summary>
    private static double[] Flatten(double[,] matrix)
    {
        int rows = matrix.GetLength(0);
        int columns = matrix.GetLength(1);
        var flat = new double[rows * columns];
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < columns; j++)
            {
                flat[i + (j * rows)] = matrix[i, j];
            }
        }

        return flat;
    }

    /// <summary><c>info(d)</c>: the few lines MATLAB prints about a filter, as a char matrix.</summary>
    private static JgsValue FilterInformation(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("info", args, 1, 1, line, col);
        (double[] b, double[] a, _, int rows, bool fir) = FilterOf("info", args[0], line, col);
        Dictionary<string, JgsValue> fields = args[0].AsStruct;

        var lines = new List<string>();
        if (fir)
        {
            lines.Add("FIR Digital Filter (real)");
            lines.Add("-------------------------");
            lines.Add($"Filter Length  : {b.Length}");
        }
        else
        {
            lines.Add("IIR Digital Filter (real)");
            lines.Add("-------------------------");
            lines.Add($"Number of Sections  : {rows}");
        }

        lines.Add($"Stable         : {(FilterAnalysis.IsStable(a) ? "Yes" : "No")}");
        lines.Add($"Linear Phase   : {(FilterAnalysis.IsLinearPhase(b, a, FilterAnalysis.DefaultTolerance) ? "Yes" : "No")}");
        lines.Add(string.Empty);
        lines.Add("Design Method Information");
        lines.Add($"Design Algorithm : {fields["DesignMethod"].AsString}");
        return CharacterMatrix([.. lines]);
    }

    /// <summary>
    /// <c>filter(d, x)</c>, <c>filtfilt(d, x)</c> and <c>fftfilt(d, x)</c>, or false when the first
    /// argument is not a designed filter and the ordinary road applies.
    /// </summary>
    /// <remarks>
    /// An IIR filter is run through its sections rather than through the transfer function they
    /// multiply out to, which is the reference's choice and the right one: the cascade is what the
    /// design produced, and expanding it to a twentieth-order polynomial to run it would throw away
    /// the conditioning the sections were for.
    /// </remarks>
    internal static bool TryDigitalFilterPass(
        string name, IReadOnlyList<JgsValue> args, int line, int col, out JgsValue result)
    {
        result = JgsValue.Null;
        if (args.Count == 0 || !IsDigitalFilter(args[0]))
        {
            return false;
        }

        if (args.Count != 2)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} takes a digitalFilter and a signal, and nothing else.");
        }

        (double[] b, double[] a, double[]? sos, int sections, bool fir) =
            FilterOf(name, args[0], line, col);
        (double[] x, int rows, int columns, bool wasRow) = SignalColumns(name, args[1], line, col);

        double[] y;
        if (name == "fftfilt" || (fir && name == "filter"))
        {
            y = Convolved(b, x, rows, columns);
        }
        else if (name == "filter")
        {
            y = FilterPasses.Cascade(sos!, sections, x, rows, columns);
        }
        else if (fir)
        {
            var single = new double[1, b.Length];
            var unit = new double[1, b.Length];
            for (int i = 0; i < b.Length; i++)
            {
                single[0, i] = b[i];
            }

            unit[0, 0] = 1;
            y = FilterPasses.ZeroPhase(single, unit, 1, x, rows, columns);
        }
        else
        {
            var num = new double[sections, 3];
            var den = new double[sections, 3];
            for (int i = 0; i < sections; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    num[i, j] = sos![i + (j * sections)];
                    den[i, j] = sos[i + ((j + 3) * sections)];
                }
            }

            y = FilterPasses.ZeroPhase(num, den, sections, x, rows, columns);
        }

        _ = a;
        result = wasRow ? Numbers(y) : JgsMatrix.FromColumnMajor(y, rows, columns);
        return true;
    }

    // --- The four one-line filters ------------------------------------------------------------------

    /// <summary><c>[y, d] = lowpass(x, wpass, fs, name, value, ...)</c> and its three siblings.</summary>
    private static JgsValue[] ApplyConvenience(
        JgsEnvironment env, JGraphScriptGlobals host, string name,
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, 2, 9, line, col);
        JgsValue signal = args[0];
        bool wide = name is "bandpass" or "bandstop";

        double[] passband = ColumnMajorOf(args[1]);
        if (passband.Length != (wide ? 2 : 1))
        {
            throw new JgsRuntimeException(line, col,
                $"{name} needs {(wide ? "two passband frequencies" : "one passband frequency")}.");
        }

        int at = 2;
        double? sampleRate = null;
        if (args.Count > 2 && IsNumericValue(args[2]) && !IsEmptyValue(args[2]))
        {
            sampleRate = Num(name, args, 2, line, col);
            at = 3;
        }

        double[] steepness = wide ? [0.85, 0.85] : [0.85];
        double attenuation = 60;
        string impulse = "auto";
        for (int i = at; i < args.Count; i += 2)
        {
            if (i + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs name-value pairs.");
            }

            string option = StrOf(name, args[i], line, col);
            switch (option)
            {
                case "Steepness":
                    steepness = ColumnMajorOf(args[i + 1]);
                    break;
                case "StopbandAttenuation":
                    attenuation = Num(name, args, i + 1, line, col);
                    break;
                case "ImpulseResponse":
                    impulse = StrOf(name, args[i + 1], line, col);
                    break;
                default:
                    throw new JgsRuntimeException(line, col,
                        $"{name} does not know the option '{option}'.");
            }
        }

        (double[] x, int rows, int columns, bool wasRow) = SignalColumns(name, signal, line, col);

        ConvenienceFilters.Result designed;
        try
        {
            designed = ConvenienceFilters.Design(new ConvenienceFilters.Request(
                name switch
                {
                    "lowpass" => ConvenienceFilters.Verb.Lowpass,
                    "highpass" => ConvenienceFilters.Verb.Highpass,
                    "bandpass" => ConvenienceFilters.Verb.Bandpass,
                    _ => ConvenienceFilters.Verb.Bandstop,
                },
                passband, sampleRate, rows, steepness, attenuation, impulse));
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"{name}: {ex.Message}");
        }

        if (designed.Warning is { } warning)
        {
            Warn(env, host, warning, line, col);
        }

        double[] y = designed.Trivial is { } trivial
            ? Scaled(x, trivial[0])
            : Filtered(designed, x, rows, columns);

        JgsValue output = wasRow
            ? Numbers(y)
            : JgsMatrix.FromColumnMajor(y, rows, columns);
        JgsValue filter = designed.Filter is null
            ? JgsValue.Null
            : Packed(designed.Filter);
        return wanted >= 2 ? [output, filter] : [output];
    }

    /// <summary>The signal as columns, remembering whether it arrived as a row.</summary>
    private static (double[] X, int Rows, int Columns, bool WasRow) SignalColumns(
        string name, JgsValue value, int line, int col)
    {
        double[] flat = ColumnMajorOf(value);
        if (flat.Length == 0)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a signal.");
        }

        int rows = value.Type == JgsType.Array ? value.Rows : 1;
        int columns = value.Type == JgsType.Array ? value.Cols : 1;
        return rows == 1 && columns > 1
            ? (flat, columns, 1, true)
            : (flat, rows, System.Math.Max(columns, 1), false);
    }

    /// <summary>Every column filtered the way the design says it should be.</summary>
    private static double[] Filtered(
        ConvenienceFilters.Result designed, double[] x, int rows, int columns)
    {
        FilterSpecification.Designed filter = designed.Filter!;
        if (designed.IsFir)
        {
            // An FIR design is run forwards over the signal followed by half its order in zeros, and
            // the answer is read from half the order in — which is the delay taken back out.
            double[] taps = filter.Taps!;
            int delay = (taps.Length - 1) / 2;
            var padded = new double[(rows + delay) * columns];
            for (int c = 0; c < columns; c++)
            {
                Array.Copy(x, c * rows, padded, c * (rows + delay), rows);
            }

            double[] all = Convolved(taps, padded, rows + delay, columns);
            var y = new double[rows * columns];
            for (int c = 0; c < columns; c++)
            {
                Array.Copy(all, (c * (rows + delay)) + delay, y, c * rows, rows);
            }

            return y;
        }

        double[,] sections = filter.Sections!;
        int stages = sections.GetLength(0);
        var b = new double[stages, 3];
        var a = new double[stages, 3];
        for (int i = 0; i < stages; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                b[i, j] = sections[i, j];
                a[i, j] = sections[i, j + 3];
            }
        }

        return FilterPasses.ZeroPhase(b, a, stages, x, rows, columns);
    }

    /// <summary>Every column run through an FIR filter, one output sample at a time.</summary>
    private static double[] Convolved(double[] taps, double[] x, int rows, int columns)
    {
        var y = new double[x.Length];
        for (int c = 0; c < columns; c++)
        {
            int start = c * rows;
            for (int n = 0; n < rows; n++)
            {
                double sum = 0;
                int last = System.Math.Min(taps.Length - 1, n);
                for (int k = 0; k <= last; k++)
                {
                    sum += taps[k] * x[start + n - k];
                }

                y[start + n] = sum;
            }
        }

        return y;
    }

    private static double[] Scaled(double[] x, double by)
    {
        var y = new double[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            y[i] = x[i] * by;
        }

        return y;
    }
}
