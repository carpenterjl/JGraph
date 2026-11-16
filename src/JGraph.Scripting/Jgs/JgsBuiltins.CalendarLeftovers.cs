namespace JGraph.Scripting.Jgs;

/// <summary>
/// The calendarDuration leftovers ADR 0375 named (open item 347, ADR 0384; probes p8d2 to p8d4, R2025b
/// 25.2): the integer refusals of the constructors, the <c>'Format'</c> option and property, a sign on
/// one, its <c>NaN</c> and <c>Inf</c> elements, the unit a zero element writes, and the verbs that move
/// its elements. Each is R2025b's reading in the MATLAB dialect only; JGS keeps its own answers.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>The order R2025b's calendarDuration display format writes its units in.</summary>
    private const string CalendarFormatLetters = "yqmwdt";

    /// <summary>
    /// R2025b's refusal of what a single-unit constructor is handed (probe p8d2 r19 to r23, p8d3 u14 to
    /// u21): text, a complex number, and a finite fraction, each unit's fraction in its own words.
    /// </summary>
    private static void RefuseCalendarUnitData(string name, JgsValue data, int line, int col)
    {
        if (!JgsRunningDialect.ThreadIsMatlab || IsCalendarDuration(data))
        {
            return;
        }

        if (data.Type == JgsType.String || data.IsCharMatrix || data.IsStringArray || data.Type is JgsType.Cell or JgsType.Struct)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:calendarDuration:InvalidCalDurationData",
                "Input data must be a numeric array containing real, integer values, or a calendarDuration array.");
        }

        if (data.Type == JgsType.Complex || data.IsPackedComplex)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:calendarDuration:InputMustBeReal", "Numeric input data must be real.");
        }

        foreach (double value in ToDoubles(name, data, line, col))
        {
            if (double.IsFinite(value) && value != Math.Floor(value))
            {
                (string unit, string advice) = name switch
                {
                    "calyears" => ("Years", "Use the CALMONTHS or CALDAYS functions to create calendar durations shorter than one year, or use the YEARS function to create fractional fixed-length years as durations."),
                    "calquarters" => ("Quarters", "Use the CALMONTHS or CALDAYS functions to create calendar durations shorter than one quarter."),
                    "calmonths" => ("Months", "Use the CALDAYS function to create calendar durations shorter than one month."),
                    "calweeks" => ("Weeks", "Use the CALDAYS function to create calendar durations shorter than one week."),
                    _ => ("Days", "Use the HOURS, MINUTES or SECONDS functions to create durations shorter than one day, or use the DAYS function to create fractional fixed-length days as durations."),
                };
                throw new JgsRuntimeException(line, col, $"MATLAB:calendarDuration:NonintegerCal{unit}Data",
                    "Input data must be a numeric array containing real, integer values. " + advice);
            }
        }
    }

    /// <summary>
    /// R2025b's refusal of a fractional year, month, day, hour or minute in <c>calendarDuration</c>'s
    /// components (probe p8d2 r24 to r27, p8d3 u16, u18 to u20); the seconds may be fractional.
    /// </summary>
    private static void RefuseFractionalCalendarParts(IEnumerable<double> parts, int line, int col)
    {
        foreach (double part in parts)
        {
            if (double.IsFinite(part) && part != Math.Floor(part))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:calendarDuration:MustBeInteger",
                    "Years, quarters, months, weeks, days, hours, and minutes must be integer values.");
            }
        }
    }

    /// <summary>
    /// <c>calendarDuration</c>'s arguments in the MATLAB dialect (probe p8d3 f13 to f16, p8d4 w3): the
    /// numbers before the first text, one, three or six of them, and a <c>'Format'</c> pair after them,
    /// read as <see cref="CalendarDisplayFormat"/> reads one; null when no format was given.
    /// </summary>
    private static string? CalendarConstructorOptions(IReadOnlyList<JgsValue> args, out List<JgsValue> positional, int line, int col)
    {
        positional = [];
        int i = 0;
        while (i < args.Count && !(i > 0 && IsTextScalar(args[i])))
        {
            positional.Add(args[i++]);
        }

        string? format = null;
        while (i < args.Count)
        {
            string name = TextOf(args[i]);
            if (!name.Equals("Format", StringComparison.OrdinalIgnoreCase))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:table:parseArgs:BadParamName", $"Invalid parameter name: {name}.");
            }

            if (i + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:table:parseArgs:WrongNumberArgs", "Incorrect number of arguments.");
            }

            format = CalendarDisplayFormat(args[i + 1], line, col);
            i += 2;
            if (i < args.Count && !IsTextScalar(args[i]))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:table:parseArgs:WrongNumberArgs", "Incorrect number of arguments.");
            }
        }

        if (positional.Count is not (1 or 3 or 6))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:calendarDuration:InvalidNumericData",
                "Numeric input data must be one matrix, or else three or six separate arrays. You can also create calendar durations from a single numeric array using the CALYEARS, CALQUARTERS, CALMONTHS, CALWEEKS, or CALDAYS functions.");
        }

        return format;
    }

    /// <summary>
    /// A calendarDuration display format as R2025b takes one, in the constructor and the <c>Format</c>
    /// property (probe p8d2 r12 to r18, p8d3 f1 to f20, p8d4 w1, w4 to w8): text of the letters
    /// <c>yqmwdt</c>, each once and in that order; one without <c>m</c>, <c>d</c> or <c>t</c> warns and
    /// takes them.
    /// </summary>
    internal static string CalendarDisplayFormat(JgsValue written, int line, int col)
    {
        if (!IsTextScalar(written))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:calendarDuration:InvalidFormat",
                "A calendar duration display format must be a string or a character vector.");
        }

        string format = TextOf(written);
        if (format.Length == 0 || format.Any(static c => !CalendarFormatLetters.Contains(c, StringComparison.Ordinal)))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:calendarDuration:UnrecognizedFormat",
                $"The format '{format}' is not valid. The format must contain only characters from the sequence 'yqmwdt'.");
        }

        if (format.Distinct().Count() != format.Length)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:calendarDuration:DuplicateFormatCharacters", "The format contains duplicate characters.");
        }

        string complete = CalendarFormatUnion(format, "mdt");
        string ordered = CalendarFormatUnion(format, string.Empty);
        if (ordered != format)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:calendarDuration:OutOfOrderFormatCharacters",
                $"The characters in the format '{format}' are not in the correct order. Use '{complete}' instead.");
        }

        if (complete != format && (JgsLifetime.Current?.Interpreter.Host ?? JgsCallbackDispatcher.Current?.Interpreter?.Host) is { } host)
        {
            Warn(host, "MATLAB:calendarDuration:InvalidCalendarDurationFormatWarn",
                $"The format '{format}' is not valid. A calendarDuration display format must include the identifiers 'm', 'd', and 't'.\nSetting format to '{complete}'.");
        }

        return complete;
    }

    /// <summary><c>-x</c> or <c>+x</c> of a calendar duration: every component negated, or kept (probe p8d2 r5, r6, r35, r36, r60).</summary>
    internal static JgsValue CalendarSign(JgsValue value, bool negate) =>
        negate ? CalendarMap(value, static (e, _) => (-e.Months, -e.Days, -e.Millis)) : value;

    /// <summary>
    /// The unit a zero element of <paramref name="value"/> writes in R2025b (probes p8d3 z1 to z29,
    /// p8d4 k1 to k17): the smallest unit any other element writes - seconds when one writes a time of
    /// day or is infinite - and days when none writes any.
    /// </summary>
    private static string CalendarZeroUnit(JgsValue value)
    {
        JgsStructArray payload = value.AsStructArray;
        string format = value.TimeTag?.Format ?? "ymdt";
        int smallest = -1;
        string[] units = ["y", "q", "mo", "w", "d", "s"];
        for (int i = 0; i < payload.Length && smallest < 5; i++)
        {
            (double months, double days, double millis) = ReadCalendar(payload, i);
            if (double.IsNaN(months) || double.IsNaN(days) || double.IsNaN(millis) || (months == 0 && days == 0 && millis == 0))
            {
                continue;
            }

            if (millis != 0 || double.IsInfinity(months) || double.IsInfinity(days))
            {
                smallest = 5;
                continue;
            }

            foreach (string part in FormatCalendarMatlab(months, days, 0, format).Split(' '))
            {
                string unit = part.TrimStart('-', '.', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
                int at = Array.IndexOf(units, unit);
                if (at >= 0)
                {
                    smallest = Math.Max(smallest, at);
                }
            }
        }

        return smallest < 0 ? "d" : units[smallest];
    }

    /// <summary>
    /// A mask over a calendar duration's elements (isnan, isinf, isfinite, ismissing; probe p8d3 u1 to u4),
    /// in its shape.
    /// </summary>
    private static JgsValue CalendarMask(JgsValue value, Func<double, double, double, bool> test, int line, int col)
    {
        JgsStructArray payload = value.AsStructArray;
        var flags = new double[payload.Length];
        for (int i = 0; i < flags.Length; i++)
        {
            (double months, double days, double millis) = ReadCalendar(payload, i);
            flags[i] = test(months, days, millis) ? 1 : 0;
        }

        return flags.Length == 1
            ? JgsValue.Bool(flags[0] != 0)
            : MapToBool("calendarDuration", JgsMatrix.FromColumnMajorDims(flags, value.Dims), static x => x != 0, line, col);
    }

    /// <summary>
    /// The verbs a calendar duration reaches as a plain struct array would, re-registered in the MATLAB
    /// dialect (open item 347, ADR 0384): <c>repmat</c> gathers its elements (it crashed on the tag),
    /// and <c>isnan</c>, <c>isinf</c>, <c>isfinite</c> and <c>ismissing</c> read its components.
    /// </summary>
    private static void WrapCalendarVerbs(JgsEnvironment env, JgsRunningDialect dialect)
    {
        void Wrap(string name, Func<IReadOnlyList<JgsValue>, BuiltinFunction, int, int, JgsValue> calendar)
        {
            if (!env.Builtins.TryGet(name, out JgsValue existing) || existing.Type != JgsType.Function
                || existing.AsCallable is not BuiltinFunction inner)
            {
                return;
            }

            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) =>
                dialect.IsMatlab && args.Count > 0 && IsCalendarDuration(args[0])
                    ? calendar(args, inner, line, col)
                    : inner.Call(args, line, col))
            {
                KeepsStringArguments = inner.KeepsStringArguments,
                BindsAnsAsStatement = inner.BindsAnsAsStatement,
                AutoCallsBare = inner.AutoCallsBare,
                KnowsWhenDiscarded = inner.KnowsWhenDiscarded,
                TakesOutputCount = inner.TakesOutputCount,
                MultiOutput = inner.MultiOutput is null ? null : (args, wanted, line, col) =>
                    dialect.IsMatlab && args.Count > 0 && IsCalendarDuration(args[0])
                        ? [calendar(args, inner, line, col)]
                        : inner.MultiOutput(args, wanted, line, col),
            }));
        }

        Wrap("repmat", (args, inner, line, col) =>
            GatheredStructs(args[0], inner.Call(WithFirst(args, StructPositionsLike(args[0])), line, col)));
        Wrap("isnan", (args, _, line, col) => args.Count != 1 ? throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.")
            : CalendarMask(args[0], static (m, d, t) => double.IsNaN(m) || double.IsNaN(d) || double.IsNaN(t), line, col));
        Wrap("ismissing", (args, _, line, col) => args.Count != 1 ? throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.")
            : CalendarMask(args[0], static (m, d, t) => double.IsNaN(m) || double.IsNaN(d) || double.IsNaN(t), line, col));
        Wrap("isinf", (args, _, line, col) => args.Count != 1 ? throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.")
            : CalendarMask(args[0], static (m, d, t) => double.IsInfinity(m) || double.IsInfinity(d) || double.IsInfinity(t), line, col));
        Wrap("isfinite", (args, _, line, col) => args.Count != 1 ? throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.")
            : CalendarMask(args[0], static (m, d, t) => double.IsFinite(m) && double.IsFinite(d) && double.IsFinite(t), line, col));
    }

    /// <summary>
    /// How R2025b writes a calendar duration with an infinite component: <c>Inf</c> or <c>-Inf</c> by the
    /// first infinite one (probe p8d2 r33, r75, p8d3 z20 to z23); null when every component is finite.
    /// </summary>
    private static string? InfiniteCalendarText(double months, double days, double millis)
    {
        foreach (double part in (ReadOnlySpan<double>)[months, days, millis])
        {
            if (double.IsInfinity(part))
            {
                return part > 0 ? "Inf" : "-Inf";
            }
        }

        return null;
    }

    /// <summary>Whether one element of a calendar duration is <c>NaN</c>, which <c>string</c> answers as missing (probe p8d3 u5).</summary>
    private static bool IsNaNCalendarElement(JgsValue value, int index)
    {
        (double months, double days, double millis) = ReadCalendar(value.AsStructArray, index);
        return double.IsNaN(months) || double.IsNaN(days) || double.IsNaN(millis);
    }

    /// <summary>A zero element's text in the unit <see cref="CalendarZeroUnit"/> names.</summary>
    private static string ZeroCalendarText(string unit) => unit == "s" ? "0s" : "0" + unit;
}
