namespace JGraph.Scripting.Jgs;

/// <summary>
/// The <c>datetime</c> and <c>duration</c> types (M64): their constructors, the unit functions that
/// build and read them, and the predicates that tell them apart from the numbers underneath.
/// </summary>
/// <remarks>
/// <para>
/// As in M63 there is no new <see cref="JgsType"/> member. A time is an ordinary numeric array of
/// milliseconds wearing a <see cref="JgsTimeTag"/>, so shape, indexing, growth, reshape, transpose,
/// <c>end</c>, logical masks and concatenation are the machinery that was already there. The
/// difference from M63 is worth stating, because it is the reason the same answer came from a
/// different argument: for strings the sweep found the storage already built and only the identity
/// missing, while here the storage had to be built either way — and building it as a tagged numeric
/// array is what buys all of that machinery a second time.
/// </para>
/// <para>
/// What a time genuinely does differently is arithmetic, display and conversion. Those are taught at
/// choke points in <c>JgsBuiltins.TimeMath</c> and <see cref="JgsTime"/>; everything else may go on
/// believing it is holding numbers.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// Marks <paramref name="numeric"/> as a time, promoting a bare number to the 1-by-1 array a
    /// scalar time is. Every mint site goes through here so the shape and the tag can never disagree.
    /// </summary>
    internal static JgsValue WrapTime(JgsValue numeric, JgsTimeTag tag)
    {
        if (numeric.Type is JgsType.Number or JgsType.Bool)
        {
            return JgsValue.Array([JgsValue.Number(numeric.AsNumber)]).MarkTime(tag);
        }

        if (numeric.Type != JgsType.Array)
        {
            return numeric;
        }

        return numeric.MarkTime(tag);
    }

    /// <summary>A time value over <paramref name="values"/>, shaped like <paramref name="model"/>.</summary>
    internal static JgsValue TimeLike(JgsValue model, double[] values, JgsTimeTag tag)
    {
        JgsValue built = Numbers(values);

        // Only an array that is the same length has a shape worth copying. A scalar model is a bare
        // Number whose shape reads 1-by-0, which would claim to describe one element and does not.
        if (model.Type == JgsType.Array && model.ArrayLength == values.Length)
        {
            built.TakeShapeOf(model);
        }

        return built.MarkTime(tag);
    }

    /// <summary>The milliseconds a time value is holding, in column-major order.</summary>
    internal static double[] TimeMs(JgsValue value)
    {
        var values = new double[value.ArrayLength];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = value.ElementAt(i).AsNumber;
        }

        return values;
    }

    /// <summary>Registers the time builtins into <paramref name="env"/>.</summary>
    internal static void RegisterTimeBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        // A bare NaT or datetime is the value, not the function — the same reading tic, eps and now
        // take, and the reason `if isnat(t)` after `t = NaT` works at all.
        void DefineBare(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { AutoCallsBare = true }));

        DefineBare("datetime", (args, line, col) => Datetime(args, line, col));
        Define("duration", (args, line, col) => Duration(args, line, col));

        DefineBare("NaT", (args, line, col) =>
        {
            ArityRange("NaT", args, 0, 2, line, col);
            int rows = args.Count >= 1 ? Count("NaT", args, 0, line, col) : 1;
            int cols = args.Count >= 2 ? Count("NaT", args, 1, line, col) : rows;
            var values = new double[rows * cols];
            System.Array.Fill(values, JgsTime.NotATime);
            JgsValue built = Numbers(values);
            built.Reshape(rows, cols);
            return built.MarkTime(new JgsTimeTag(JgsTimeKind.Datetime, JgsTime.DefaultDatetimeFormat));
        });

        // --- The unit functions, which read in both directions ---------------------------------
        // seconds(90) is a duration of ninety seconds; seconds(d) is how many seconds a duration is.
        // One name doing both is MATLAB's design, and the argument's own type says which is meant.
        DefineUnit(env, "seconds", JgsTime.MsPerSecond, "s");
        DefineUnit(env, "minutes", JgsTime.MsPerMinute, "m");
        DefineUnit(env, "hours", JgsTime.MsPerHour, "h");
        DefineUnit(env, "days", JgsTime.MsPerDay, "d");
        DefineUnit(env, "years", JgsTime.MsPerDay * 365.2425, "y");
        DefineUnit(env, "milliseconds", 1.0, "s");

        // The storage was always finer than the readers were (M82): a millisecond is the unit, not the
        // resolution, so a fractional count of them is an ordinary value and these two units are what
        // make it reachable from a script.
        DefineUnit(env, "microseconds", 1.0 / 1000.0, "s");
        DefineUnit(env, "nanoseconds", 1.0 / 1_000_000.0, "s");

        RegisterCalendarDurationBuiltins(env);

        // --- Predicates -------------------------------------------------------------------------
        Define("isdatetime", (args, line, col) =>
        {
            Arity("isdatetime", args, 1, line, col);
            return JgsValue.Bool(args[0].IsDatetime);
        });

        Define("isduration", (args, line, col) =>
        {
            Arity("isduration", args, 1, line, col);
            return JgsValue.Bool(args[0].IsDuration);
        });

        Define("iscalendarduration", (args, line, col) =>
        {
            Arity("iscalendarduration", args, 1, line, col);
            return JgsValue.Bool(IsCalendarDuration(args[0]));
        });

        Define("isnat", (args, line, col) =>
        {
            Arity("isnat", args, 1, line, col);
            if (!args[0].IsDatetime)
            {
                throw new JgsRuntimeException(line, col, "isnat expects a datetime.");
            }

            double[] values = TimeMs(args[0]);
            var flags = new JgsValue[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                flags[i] = JgsValue.Bool(double.IsNaN(values[i]));
            }

            JgsValue answer = flags.Length == 1 ? flags[0] : JgsValue.Array(flags);
            if (flags.Length > 1)
            {
                answer.TakeShapeOf(args[0]);
            }

            return answer;
        });

        RegisterZoneBuiltins(env, Define);
    }

    /// <summary>
    /// The three verbs a zone makes answerable (M82): what a value's offset from UTC is, whether that
    /// offset is a daylight-saving one, and what zone names this machine will accept.
    /// </summary>
    private static void RegisterZoneBuiltins(
        JgsEnvironment env, Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define)
    {
        // tzoffset answers a duration, because an offset is a length of time and MATLAB's answers one
        // too. An unzoned datetime has no offset, which is a missing value rather than zero: zero is
        // what UTC answers, and the two must not read alike.
        Define("tzoffset", (args, line, col) =>
        {
            Arity("tzoffset", args, 1, line, col);
            JgsValue moment = RequireDatetime("tzoffset", args[0], line, col);
            TimeZoneInfo? zone = JgsTime.ZoneOf(moment.TimeTag);
            double[] source = TimeMs(moment);
            var values = new double[source.Length];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = zone is null || double.IsNaN(source[i])
                    ? double.NaN
                    : JgsTime.OffsetAt(source[i], zone).TotalMilliseconds;
            }

            return TimeLike(moment, values, JgsTime.DurationTag("hh:mm:ss"));
        });

        Define("isdst", (args, line, col) =>
        {
            Arity("isdst", args, 1, line, col);
            JgsValue moment = RequireDatetime("isdst", args[0], line, col);
            TimeZoneInfo? zone = JgsTime.ZoneOf(moment.TimeTag);
            double[] source = TimeMs(moment);
            var flags = new JgsValue[source.Length];
            for (int i = 0; i < flags.Length; i++)
            {
                flags[i] = JgsValue.Bool(zone is not null && !double.IsNaN(source[i])
                    && zone.IsDaylightSavingTime(
                        DateTime.SpecifyKind(JgsTime.ToDateTime(source[i]), DateTimeKind.Utc)));
            }

            JgsValue answer = flags.Length == 1 ? flags[0] : JgsValue.Array(flags);
            if (flags.Length > 1)
            {
                answer.TakeShapeOf(moment);
            }

            return answer;
        });

        // timezones answers the names this machine will accept, as a cell of strings. MATLAB's is a
        // table with three columns; a cell is the honest shape here, because the UTC offset a zone has
        // is a question about a moment rather than about the zone, and a table column would have to
        // pick one moment and not say which.
        env.Declare("timezones", JgsValue.Function(new BuiltinFunction("timezones", (args, line, col) =>
        {
            ArityRange("timezones", args, 0, 1, line, col);
            string? area = args.Count == 1 ? TextOfArgument("timezones", args[0], line, col) : null;
            // A substring rather than a prefix, because this machine's zone ids may be Windows ones —
            // the zone a script calls 'Europe/Berlin' is spelled 'W. Europe Standard Time' here, and a
            // prefix filter would answer with nothing for every area a person would think to ask about.
            var names = new List<JgsValue>();
            foreach (TimeZoneInfo zone in TimeZoneInfo.GetSystemTimeZones())
            {
                if (area is null || zone.Id.Contains(area, StringComparison.OrdinalIgnoreCase)
                    || zone.DisplayName.Contains(area, StringComparison.OrdinalIgnoreCase))
                {
                    names.Add(JgsValue.Str(zone.Id));
                }
            }

            names.Sort(static (a, b) => string.CompareOrdinal(a.AsString, b.AsString));
            return JgsValue.Cell(names.ToArray());
        })
        { AutoCallsBare = true }));
    }

    /// <summary>
    /// Registers one of the unit names. Handed a number it builds a duration of that many units;
    /// handed a duration it says how many units the duration is; handed a datetime it is an error,
    /// because <c>hours(t)</c> on a point in time is asking two different questions at once and
    /// MATLAB spells the other one <c>hour</c>.
    /// </summary>
    private static void DefineUnit(JgsEnvironment env, string name, double msPerUnit, string format)
    {
        env.Declare(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) =>
        {
            Arity(name, args, 1, line, col);
            JgsValue input = args[0];

            if (input.IsDatetime)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name} expects a number or a duration; a datetime is a point in time, not a length of one. " +
                    $"Use {name[..^1]} to read that field of a datetime.");
            }

            double[] source = input.IsDuration
                ? TimeMs(input)
                : ToDoubles(name, input, line, col);

            var values = new double[source.Length];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = input.IsDuration ? source[i] / msPerUnit : source[i] * msPerUnit;
            }

            if (input.IsDuration)
            {
                JgsValue plain = Numbers(values);
                plain.TakeShapeOf(input);
                return plain;
            }

            return TimeLike(input, values, JgsTime.DurationTag(format));
        })));
    }

    // --- datetime -------------------------------------------------------------------------------

    private static JgsValue Datetime(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (List<JgsValue> positional, Dictionary<string, JgsValue> options) = SplitTimeOptions("datetime", args, line, col);

        string? inputFormat = OptionText("datetime", options, "inputformat", line, col);
        string? timeZone = OptionText("datetime", options, "timezone", line, col);
        string? displayFormat = OptionText("datetime", options, "format", line, col);
        string? convertFrom = OptionText("datetime", options, "convertfrom", line, col);

        double[] values;
        JgsValue shapeModel;

        if (positional.Count == 0)
        {
            values = [JgsTime.FromDateTime(DateTime.Now)];
            shapeModel = JgsValue.Number(0);
        }
        else if (positional.Count == 1 && convertFrom is not null)
        {
            double[] raw = ToDoubles("datetime", positional[0], line, col);
            values = System.Array.ConvertAll(raw, x => ConvertFrom(convertFrom, x, line, col));
            shapeModel = positional[0];
        }
        else if (positional.Count == 1 && positional[0].IsDatetime)
        {
            values = TimeMs(positional[0]);
            shapeModel = positional[0];
        }
        else if (positional.Count == 1 && TextElementsOf(positional[0]) is string[] texts)
        {
            values = new double[texts.Length];
            for (int i = 0; i < texts.Length; i++)
            {
                values[i] = KeywordMoment(texts[i]) ?? ParsedMoment(texts[i], inputFormat, line, col);
            }

            shapeModel = positional[0];
        }
        else if (positional.Count == 1)
        {
            // A matrix of date vectors: one row per moment, three or six columns, which is what
            // datevec hands back and therefore what a script round-trips through.
            JgsValue matrix = positional[0];
            int rows = matrix.Rows;
            int cols = matrix.Cols;
            if (cols is not (3 or 6))
            {
                throw new JgsRuntimeException(line, col,
                    "datetime: a single numeric argument must be a matrix of date vectors with 3 or 6 columns.");
            }

            values = new double[rows];
            for (int r = 0; r < rows; r++)
            {
                double At(int c) => c < cols ? matrix.ElementAt((c * rows) + r).AsNumber : 0;
                values[r] = JgsTime.FromComponents(At(0), At(1), At(2), At(3), At(4), At(5));
            }

            shapeModel = JgsValue.Number(0);
        }
        else if (positional.Count is 3 or 6)
        {
            values = ComponentwiseMoments("datetime", positional, line, col, out shapeModel);
        }
        else
        {
            throw new JgsRuntimeException(line, col,
                "datetime expects text, a date vector, year/month/day (optionally hour/minute/second), or no arguments at all.");
        }

        // A zone turns the numbers just built from a wall-clock reading into the instant they name
        // (M82). Everything above produced wall clock — components, parsed text, 'now' — so the whole
        // conversion happens here, once, and every path gets it. The one exception is a datetime
        // rebuilt from another datetime, which is already an instant in its own zone: re-reading its
        // storage as wall clock would shift it by the offset every time it were copied.
        if (timeZone is { Length: > 0 })
        {
            if (!JgsTime.TryResolveZone(timeZone, out TimeZoneInfo zone))
            {
                throw new JgsRuntimeException(line, col,
                    $"datetime: '{timeZone}' is not a time zone this machine knows. Use 'UTC', 'local', "
                    + "a fixed offset like '+05:30', or a zone name like 'America/New_York'.");
            }

            bool alreadyAnInstant = positional.Count == 1 && positional[0].IsDatetime
                && positional[0].TimeTag?.TimeZone is { Length: > 0 };
            if (!alreadyAnInstant)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    if (!double.IsNaN(values[i]))
                    {
                        values[i] = JgsTime.ToUtc(values[i], zone);
                    }
                }
            }
        }

        JgsTimeTag tag = JgsTime.DatetimeTag(values, timeZone);
        if (displayFormat is not null)
        {
            tag = tag with { Format = displayFormat };
        }

        JgsValue built = Numbers(values);
        if (shapeModel.Type == JgsType.Array && shapeModel.ArrayLength == values.Length)
        {
            built.TakeShapeOf(shapeModel);
        }

        return built.MarkTime(tag);
    }

    /// <summary>The four words MATLAB accepts in place of a date, or null when the text is a date.</summary>
    private static double? KeywordMoment(string text) => text.Trim().ToLowerInvariant() switch
    {
        "now" => JgsTime.FromDateTime(DateTime.Now),
        "today" => JgsTime.FromDateTime(DateTime.Today),
        "yesterday" => JgsTime.FromDateTime(DateTime.Today.AddDays(-1)),
        "tomorrow" => JgsTime.FromDateTime(DateTime.Today.AddDays(1)),
        _ => null,
    };

    private static double ParsedMoment(string text, string? inputFormat, int line, int col)
    {
        if (JgsTime.TryParse(text, inputFormat, out double ms))
        {
            return ms;
        }

        throw new JgsRuntimeException(line, col, inputFormat is null
            ? $"datetime: '{text}' is not a date this recognizes. Give the shape with 'InputFormat'."
            : $"datetime: '{text}' does not match the InputFormat '{inputFormat}'.");
    }

    private static double ConvertFrom(string source, double value, int line, int col) =>
        source.ToLowerInvariant() switch
        {
            "datenum" => JgsTime.FromDatenum(value),
            "excel" => JgsTime.FromDateTime(new DateTime(1899, 12, 30).AddDays(value)),
            "posixtime" => JgsTime.FromDateTime(DateTime.UnixEpoch.AddSeconds(value)),
            "juliandate" => JgsTime.FromDateTime(new DateTime(1858, 11, 17).AddDays(value - 2400000.5)),
            "epochtime" => JgsTime.FromDateTime(DateTime.UnixEpoch.AddSeconds(value)),
            _ => throw new JgsRuntimeException(line, col,
                $"datetime: 'ConvertFrom' does not know '{source}'; it reads datenum, excel, posixtime, juliandate and epochtime."),
        };

    // --- duration -------------------------------------------------------------------------------

    private static JgsValue Duration(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (List<JgsValue> positional, Dictionary<string, JgsValue> options) = SplitTimeOptions("duration", args, line, col);
        string? displayFormat = OptionText("duration", options, "format", line, col);

        double[] values;
        JgsValue shapeModel = JgsValue.Number(0);

        if (positional.Count == 1 && positional[0].IsDuration)
        {
            values = TimeMs(positional[0]);
            shapeModel = positional[0];
        }
        else if (positional.Count == 1 && TextElementsOf(positional[0]) is string[] texts)
        {
            values = new double[texts.Length];
            for (int i = 0; i < texts.Length; i++)
            {
                if (!JgsTime.TryParseDuration(texts[i], out values[i]))
                {
                    throw new JgsRuntimeException(line, col,
                        $"duration: '{texts[i]}' is not a length of time; write it as hh:mm:ss, dd:hh:mm:ss or mm:ss.");
                }
            }

            shapeModel = positional[0];
        }
        else if (positional.Count == 1)
        {
            JgsValue matrix = positional[0];
            int rows = matrix.Rows;
            if (matrix.Cols != 3)
            {
                throw new JgsRuntimeException(line, col,
                    "duration: a single numeric argument must be a matrix of [hours, minutes, seconds] rows.");
            }

            values = new double[rows];
            for (int r = 0; r < rows; r++)
            {
                values[r] = (matrix.ElementAt(r).AsNumber * JgsTime.MsPerHour)
                    + (matrix.ElementAt(rows + r).AsNumber * JgsTime.MsPerMinute)
                    + (matrix.ElementAt((2 * rows) + r).AsNumber * JgsTime.MsPerSecond);
            }
        }
        else if (positional.Count is 3 or 4)
        {
            double[][] parts = new double[positional.Count][];
            int length = 1;
            for (int i = 0; i < positional.Count; i++)
            {
                parts[i] = ToDoubles("duration", positional[i], line, col);
                length = System.Math.Max(length, parts[i].Length);
                if (positional[i].Type == JgsType.Array && positional[i].ArrayLength == length)
                {
                    shapeModel = positional[i];
                }
            }

            values = new double[length];
            for (int k = 0; k < length; k++)
            {
                double Part(int i) => parts[i].Length == 1 ? parts[i][0] : parts[i][k];
                values[k] = (Part(0) * JgsTime.MsPerHour)
                    + (Part(1) * JgsTime.MsPerMinute)
                    + (Part(2) * JgsTime.MsPerSecond)
                    + (positional.Count == 4 ? Part(3) : 0);
            }
        }
        else
        {
            throw new JgsRuntimeException(line, col,
                "duration expects hours, minutes and seconds (optionally milliseconds), a matrix of those, or text.");
        }

        JgsTimeTag tag = JgsTime.DurationTag(displayFormat ?? JgsTime.DefaultDurationFormat);
        JgsValue built = Numbers(values);
        if (shapeModel.Type == JgsType.Array && shapeModel.ArrayLength == values.Length)
        {
            built.TakeShapeOf(shapeModel);
        }

        return built.MarkTime(tag);
    }

    /// <summary>
    /// Builds one moment per element from the three or six component arguments, expanding scalars —
    /// which is what makes <c>datetime(2024, 1, 1:31)</c> a month of days.
    /// </summary>
    private static double[] ComponentwiseMoments(
        string name, List<JgsValue> positional, int line, int col, out JgsValue shapeModel)
    {
        double[][] parts = new double[positional.Count][];
        int length = 1;
        shapeModel = JgsValue.Number(0);
        for (int i = 0; i < positional.Count; i++)
        {
            parts[i] = ToDoubles(name, positional[i], line, col);
            if (parts[i].Length > length)
            {
                length = parts[i].Length;
                shapeModel = positional[i];
            }
        }

        foreach (double[] part in parts)
        {
            if (part.Length != 1 && part.Length != length)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: the parts must all be scalars or all the same length.");
            }
        }

        var values = new double[length];
        for (int k = 0; k < length; k++)
        {
            double Part(int i) => parts[i].Length == 1 ? parts[i][0] : parts[i][k];
            values[k] = JgsTime.FromComponents(
                Part(0), Part(1), Part(2),
                positional.Count > 3 ? Part(3) : 0,
                positional.Count > 4 ? Part(4) : 0,
                positional.Count > 5 ? Part(5) : 0);
        }

        return values;
    }

    // --- Shared argument reading ------------------------------------------------------------------

    /// <summary>
    /// Splits a time constructor's arguments into its positional ones and its name/value options.
    /// </summary>
    /// <remarks>
    /// The scan runs from the right and stops at the first pair whose name is not one of the four
    /// this understands, because a positional argument can be text too: <c>datetime('now')</c> must
    /// not have its only argument read as half of an option pair.
    /// </remarks>
    private static (List<JgsValue> Positional, Dictionary<string, JgsValue> Options) SplitTimeOptions(
        string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        var options = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        int end = args.Count;
        while (end >= 2)
        {
            if (!IsTextScalar(args[end - 2]))
            {
                break;
            }

            string key = TextOf(args[end - 2]).ToLowerInvariant();
            if (key is not ("inputformat" or "timezone" or "format" or "convertfrom"))
            {
                break;
            }

            options[key] = args[end - 1];
            end -= 2;
        }

        if (end == 1 && args.Count > 1 && options.Count == 0)
        {
            throw new JgsRuntimeException(line, col, $"{name}: an option needs a value after its name.");
        }

        var positional = new List<JgsValue>(end);
        for (int i = 0; i < end; i++)
        {
            positional.Add(args[i]);
        }

        return (positional, options);
    }

    /// <summary>The text of one option, or null when the caller did not give it.</summary>
    private static string? OptionText(
        string name, Dictionary<string, JgsValue> options, string key, int line, int col) =>
        options.TryGetValue(key, out JgsValue? value) ? TextOfArgument(name, value, line, col) : null;

    /// <summary>The text of an option's value, which may be written either way round the quotes.</summary>
    private static string TextOfArgument(string name, JgsValue value, int line, int col)
    {
        if (IsTextScalar(value))
        {
            return TextOf(value);
        }

        throw new JgsRuntimeException(line, col, $"{name}: that option's value must be text.");
    }

    // --- The properties a time value answers to (M82) ---------------------------------------------
    //
    // MATLAB writes t.TimeZone, t.Format and the calendar fields as properties rather than as calls,
    // and until M82 a dot on a time value was refused outright: a time is a tagged array, not a
    // struct. TimeZone is the name the whole zone wave exists to make answerable, so the machinery
    // arrives with it and the other names ride along.

    /// <summary>Reads one property off a time value.</summary>
    internal static JgsValue GetTimeProperty(JgsValue value, string field, int line, int col)
    {
        JgsTimeTag tag = value.TimeTag!;
        switch (field)
        {
            case "Format":
                return JgsValue.Str(tag.Format);

            case "TimeZone" when value.IsDatetime:
                // The empty char row rather than [] — MATLAB's answer for an unzoned datetime, and the
                // one a script can compare with '' without knowing which it got.
                return JgsValue.Str(tag.TimeZone ?? string.Empty);

            case "TimeZone":
                throw new JgsRuntimeException(line, col,
                    $"A {TimeClassName(value)} has no time zone; only a datetime is a point in time that one applies to.");

            case "SystemTimeZone":
                return JgsValue.Str(TimeZoneInfo.Local.Id);
        }

        // The calendar fields are the accessor functions written the other way round, which is how
        // MATLAB spells them too. Reading them through the same builtins is what keeps t.Year and
        // year(t) from ever disagreeing.
        string? accessor = field switch
        {
            "Year" => "year",
            "Month" => "month",
            "Day" => "day",
            "Hour" => "hour",
            "Minute" => "minute",
            "Second" => "second",
            _ => null,
        };

        if (accessor is not null && value.IsDatetime)
        {
            return TimeFieldOf(accessor, value, line, col);
        }

        throw new JgsRuntimeException(line, col,
            $"'.{field}' is not a property of a {TimeClassName(value)}; it has Format"
            + (value.IsDatetime ? ", TimeZone, and Year through Second." : "."));
    }

    /// <summary>Writes one property, handing back the value the variable should now hold.</summary>
    /// <remarks>
    /// <c>TimeZone</c> does two different things and MATLAB means both: setting it on an unzoned
    /// datetime <em>attaches</em> a zone, reading the stored wall clock as a reading in that zone;
    /// setting it on a zoned one <em>converts</em>, keeping the instant and changing the lens. Setting
    /// it to the empty text strips the zone and keeps the wall clock, which is the inverse of
    /// attaching.
    /// </remarks>
    internal static JgsValue SetTimeProperty(JgsValue value, string field, JgsValue written, int line, int col)
    {
        JgsTimeTag tag = value.TimeTag!;
        if (field == "Format")
        {
            return value.MarkTime(tag with { Format = TextOfArgument("Format", written, line, col) });
        }

        if (field != "TimeZone")
        {
            throw new JgsRuntimeException(line, col,
                $"'.{field}' cannot be set on a {TimeClassName(value)}; Format can be, and on a datetime so can TimeZone.");
        }

        if (!value.IsDatetime)
        {
            throw new JgsRuntimeException(line, col,
                $"A {TimeClassName(value)} has no time zone; only a datetime is a point in time that one applies to.");
        }

        string wanted = TextOfArgument("TimeZone", written, line, col).Trim();
        bool wasZoned = tag.TimeZone is { Length: > 0 };
        double[] source = TimeMs(value);
        var values = new double[source.Length];

        if (wanted.Length == 0)
        {
            // Stripping keeps the reading and drops the lens, so the wall clock the value showed is
            // the wall clock it goes on showing.
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = double.IsNaN(source[i]) ? source[i] : JgsTime.FromDateTime(JgsTime.WallClock(source[i], tag));
            }

            return TimeLike(value, values, tag with { TimeZone = null });
        }

        if (!JgsTime.TryResolveZone(wanted, out TimeZoneInfo zone))
        {
            throw new JgsRuntimeException(line, col,
                $"'{wanted}' is not a time zone this machine knows. Use 'UTC', 'local', a fixed offset "
                + "like '+05:30', or a zone name like 'America/New_York'.");
        }

        if (wasZoned)
        {
            // Already an instant: the lens changes and the storage does not.
            return TimeLike(value, source, tag with { TimeZone = wanted });
        }

        for (int i = 0; i < values.Length; i++)
        {
            values[i] = double.IsNaN(source[i]) ? source[i] : JgsTime.ToUtc(source[i], zone);
        }

        return TimeLike(value, values, tag with { TimeZone = wanted });
    }

    /// <summary>Calls one of the field accessors by name, for the dotted spelling of it.</summary>
    private static JgsValue TimeFieldOf(string accessor, JgsValue value, int line, int col)
    {
        if (TimeFieldReaders.TryGetValue(accessor, out Func<JgsValue, int, int, JgsValue>? read))
        {
            return read(value, line, col);
        }

        throw new JgsRuntimeException(line, col, $"{accessor} is not available.");
    }

    /// <summary>
    /// The field accessors, by name, filled in as they are registered so the dotted spelling and the
    /// call reach exactly the same code.
    /// </summary>
    internal static readonly Dictionary<string, Func<JgsValue, int, int, JgsValue>> TimeFieldReaders = new(StringComparer.Ordinal);

    /// <summary>Formats one time value for a caller that wants its text (<c>char</c>, <c>string</c>).</summary>
    internal static string TimeText(JgsValue value, int index)
    {
        if (IsCalendarDuration(value))
        {
            (double months, double days, double millis) = ReadCalendar(value.AsStructArray, index);
            return FormatCalendar(months, days, millis);
        }

        return JgsTime.Format(value.ElementAt(index).AsNumber, value.TimeTag!);
    }

    /// <summary>The name <c>class</c> gives a time value.</summary>
    internal static string TimeClassName(JgsValue value) => value.TimeTag!.Kind switch
    {
        JgsTimeKind.Datetime => "datetime",
        JgsTimeKind.CalendarDuration => "calendarDuration",
        _ => "duration",
    };
}
