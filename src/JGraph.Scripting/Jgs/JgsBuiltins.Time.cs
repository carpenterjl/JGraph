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

        // A calendar day is exactly a day here, because an unzoned datetime has no daylight saving to
        // be shortened by. calweeks likewise. The three that genuinely need calendar arithmetic are
        // refused by name below rather than quietly answering with an average month.
        DefineUnit(env, "caldays", JgsTime.MsPerDay, "d");
        DefineUnit(env, "calweeks", JgsTime.MsPerDay * 7, "d");

        foreach (string name in new[] { "calmonths", "calyears", "calquarters", "calendarDuration" })
        {
            string refused = name;
            Define(refused, (args, line, col) => throw new JgsRuntimeException(line, col,
                $"{refused} builds a calendarDuration, which JGraph does not have: a month and a year are " +
                "not fixed lengths, so they cannot be a count of milliseconds. Use calweeks, caldays or " +
                "days for a fixed span, or dateshift to move to a month boundary."));
        }

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
            return JgsValue.False;   // JGraph has no calendarDuration, so nothing is one.
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

    /// <summary>Formats one time value for a caller that wants its text (<c>char</c>, <c>string</c>).</summary>
    internal static string TimeText(JgsValue value, int index) =>
        JgsTime.Format(value.ElementAt(index).AsNumber, value.TimeTag!);

    /// <summary>The name <c>class</c> gives a time value.</summary>
    internal static string TimeClassName(JgsValue value) =>
        value.IsDatetime ? "datetime" : "duration";
}
