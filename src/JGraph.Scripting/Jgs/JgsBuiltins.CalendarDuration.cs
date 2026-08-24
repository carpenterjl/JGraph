using System.Globalization;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>calendarDuration</c> (M82): a length of time counted in calendar units, where a month is a month
/// whatever its length turns out to be.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0064 refused this type, and the reason it gave was right: a month is not a fixed number of
/// milliseconds, so it cannot be one. What it did not say — because the storage did not exist yet — is
/// what a calendar duration <em>is</em> storage for. It needs three numbers per element (months, days
/// and a time of day), and M64's rule says where to put them: <em>a type here is a meaning attached to
/// storage that already knows how to be an array.</em>
/// </para>
/// <para>
/// M65 made struct arrays real, and a struct array is exactly that storage. So a calendar duration is
/// a <see cref="JgsType.Struct"/> array of <c>months</c>/<c>days</c>/<c>millis</c> wearing a
/// <see cref="JgsTimeTag"/> whose kind is <see cref="JgsTimeKind.CalendarDuration"/>. Shape, indexing,
/// growth, masks and concatenation are M65's machinery used again; only meaning is added. As with
/// <c>MException</c> in M68, the tag is what makes <c>isstruct</c> answer false: a tagged value is not
/// the thing its storage happens to be.
/// </para>
/// <para>
/// The three components do not collapse into each other and must not be allowed to. Adding a month to
/// the 31st of January is the 29th of February; adding thirty-one days is the 3rd of March. Keeping
/// them apart is the whole reason the type exists, which is also why the components are applied in
/// order — months, then days, then time — and why two calendar durations have no ordering.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>The field names of one element, in the order the components are applied.</summary>
    private static readonly string[] CalendarFields = ["months", "days", "millis"];

    /// <summary>Whether a value is a calendar duration.</summary>
    internal static bool IsCalendarDuration(JgsValue value) =>
        value.TimeTag?.Kind == JgsTimeKind.CalendarDuration;

    /// <summary>Registers the calendar-duration constructors and the two verbs that answer with one.</summary>
    internal static void RegisterCalendarDurationBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        // The single-unit constructors. caldays and calweeks moved here from the unit family: they had
        // been plain durations, on the argument that an unzoned datetime has no daylight saving to
        // shorten a day. M82's zones made that argument expire, and MATLAB calls them calendar units.
        CalendarUnit(Define, "calyears", static n => (n * 12, 0.0));
        CalendarUnit(Define, "calquarters", static n => (n * 3, 0.0));
        CalendarUnit(Define, "calmonths", static n => (n, 0.0));
        CalendarUnit(Define, "calweeks", static n => (0.0, n * 7));
        CalendarUnit(Define, "caldays", static n => (0.0, n));

        Define("calendarDuration", (args, line, col) =>
        {
            ArityRange("calendarDuration", args, 1, 6, line, col);

            // calendarDuration(Y, M, D) and calendarDuration(Y, M, D, h, m, s) are MATLAB's two shapes,
            // and a single argument is a matrix of those rows — the same rule datetime follows.
            if (args.Count == 1)
            {
                return CalendarFromMatrix(args[0], line, col);
            }

            if (args.Count is not (3 or 6))
            {
                throw new JgsRuntimeException(line, col,
                    "calendarDuration expects a matrix of components, or Y, M, D, or Y, M, D, h, m, s.");
            }

            double[][] parts = new double[args.Count][];
            int length = 1;
            for (int i = 0; i < args.Count; i++)
            {
                parts[i] = ToDoubles("calendarDuration", args[i], line, col);
                length = System.Math.Max(length, parts[i].Length);
            }

            var elements = new Dictionary<string, JgsValue>[length];
            for (int i = 0; i < length; i++)
            {
                double At(int c) => parts[c].Length == 1 ? parts[c][0] : parts[c][i];
                double months = (At(0) * 12) + At(1);
                double time = args.Count == 6
                    ? (At(3) * JgsTime.MsPerHour) + (At(4) * JgsTime.MsPerMinute) + (At(5) * JgsTime.MsPerSecond)
                    : 0;
                elements[i] = CalendarElement(months, At(2), time);
            }

            return CalendarValue(elements, args[0]);
        });

        // caldiff and between both answer with the calendar difference between two moments, and both
        // needed this type before they could exist. between takes the components it is asked for;
        // caldiff takes successive differences down a vector, as diff does.
        Define("caldiff", (args, line, col) =>
        {
            ArityRange("caldiff", args, 1, 2, line, col);
            JgsValue moments = RequireDatetime("caldiff", args[0], line, col);
            string components = args.Count == 2
                ? TextOfArgument("caldiff", args[1], line, col).ToLowerInvariant()
                : "ymdt";
            double[] source = TimeMs(moments);
            if (source.Length < 2)
            {
                return CalendarValue([], JgsValue.Number(0));
            }

            JgsTimeTag? tag = moments.TimeTag;
            var elements = new Dictionary<string, JgsValue>[source.Length - 1];
            for (int i = 0; i < elements.Length; i++)
            {
                elements[i] = CalendarBetween(source[i], source[i + 1], components, tag, line, col);
            }

            return CalendarValue(elements, JgsValue.Number(0));
        });

        Define("between", (args, line, col) =>
        {
            ArityRange("between", args, 2, 3, line, col);
            JgsValue from = RequireDatetime("between", args[0], line, col);
            JgsValue to = RequireDatetime("between", args[1], line, col);
            string components = args.Count == 3
                ? TextOfArgument("between", args[2], line, col).ToLowerInvariant()
                : "ymdt";

            double[] lower = TimeMs(from);
            double[] upper = TimeMs(to);
            int length = System.Math.Max(lower.Length, upper.Length);
            JgsTimeTag? tag = from.TimeTag;
            var elements = new Dictionary<string, JgsValue>[length];
            for (int i = 0; i < length; i++)
            {
                elements[i] = CalendarBetween(
                    lower.Length == 1 ? lower[0] : lower[i],
                    upper.Length == 1 ? upper[0] : upper[i],
                    components, tag, line, col);
            }

            return CalendarValue(elements, lower.Length >= upper.Length ? from : to);
        });

    }

    /// <summary>
    /// <c>split(cd, units)</c>: a calendar duration broken into the units asked for.
    /// </summary>
    /// <remarks>
    /// Reached from the text <c>split</c> rather than registered beside it, because MATLAB spells two
    /// different verbs the same way and the first argument is what tells them apart. Declaring a
    /// second <c>split</c> replaced the text one outright, which three string tests caught at once.
    /// </remarks>
    private static JgsValue SplitCalendar(JgsValue value, JgsValue wanted, int line, int col)
    {
        string[] units = TextElementsOf(wanted)
            ?? throw new JgsRuntimeException(line, col,
                "split: the units are text — 'years', 'months', 'days' or 'time', or a cell of them.");

        JgsStructArray payload = value.AsStructArray;
        var outputs = new JgsValue[units.Length];
        for (int u = 0; u < units.Length; u++)
        {
            string unit = units[u].Trim().ToLowerInvariant();
            var column = new double[payload.Length];
            for (int i = 0; i < payload.Length; i++)
            {
                (double months, double days, double millis) = ReadCalendar(payload, i);
                column[i] = unit switch
                {
                    "years" => System.Math.Truncate(months / 12),
                    "quarters" => System.Math.Truncate(months / 3),
                    "months" => months,
                    "weeks" => System.Math.Truncate(days / 7),
                    "days" => days,
                    "time" => millis,
                    _ => throw new JgsRuntimeException(line, col,
                        $"split: '{unit}' is not a calendar unit; it takes years, quarters, months, weeks, days or time."),
                };
            }

            outputs[u] = unit == "time"
                ? WrapTime(Numbers(column), JgsTime.DurationTag())
                : column.Length == 1 ? JgsValue.Number(column[0]) : Numbers(column);
        }

        return outputs.Length == 1 ? outputs[0] : JgsValue.Array(outputs);
    }

    /// <summary>Declares one single-unit calendar constructor, and its reader for the round trip.</summary>
    private static void CalendarUnit(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> define,
        string name, Func<double, (double Months, double Days)> build) =>
        define(name, (args, line, col) =>
        {
            Arity(name, args, 1, line, col);

            // Handed a calendar duration these read back rather than build, which is the unit family's
            // own two-way rule: caldays(caldays(3)) is 3.
            if (IsCalendarDuration(args[0]))
            {
                JgsStructArray payload = args[0].AsStructArray;
                var counts = new double[payload.Length];
                for (int i = 0; i < counts.Length; i++)
                {
                    (double months, double days, _) = ReadCalendar(payload, i);
                    (double perMonths, double perDays) = build(1);
                    counts[i] = perMonths != 0 ? months / perMonths : days / perDays;
                }

                return counts.Length == 1 ? JgsValue.Number(counts[0]) : Numbers(counts);
            }

            double[] source = ToDoubles(name, args[0], line, col);
            var elements = new Dictionary<string, JgsValue>[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                (double months, double days) = build(source[i]);
                elements[i] = CalendarElement(months, days, 0);
            }

            return CalendarValue(elements, args[0]);
        });

    /// <summary>One element: three numbers that do not collapse into each other.</summary>
    private static Dictionary<string, JgsValue> CalendarElement(double months, double days, double millis) =>
        new()
        {
            ["months"] = JgsValue.Number(months),
            ["days"] = JgsValue.Number(days),
            ["millis"] = JgsValue.Number(millis),
        };

    /// <summary>The three numbers of one element of a calendar duration's storage.</summary>
    internal static (double Months, double Days, double Millis) ReadCalendar(JgsStructArray payload, int index)
    {
        Dictionary<string, JgsValue> element = payload.Elements[index];
        double Read(string field) => element.TryGetValue(field, out JgsValue? value) ? value.AsNumber : 0;
        return (Read("months"), Read("days"), Read("millis"));
    }

    /// <summary>A calendar duration over <paramref name="elements"/>, shaped like <paramref name="model"/>.</summary>
    internal static JgsValue CalendarValue(Dictionary<string, JgsValue>[] elements, JgsValue model)
    {
        JgsValue built = JgsValue.StructArray(elements);
        if (model.Type == JgsType.Array && model.ArrayLength == elements.Length && elements.Length > 1)
        {
            built.TakeShapeOf(model);
        }

        return built.MarkTime(new JgsTimeTag(JgsTimeKind.CalendarDuration, "ymdt"));
    }

    /// <summary>Reads a matrix of Y/M/D or Y/M/D/h/m/s rows as calendar durations.</summary>
    private static JgsValue CalendarFromMatrix(JgsValue matrix, int line, int col)
    {
        if (IsCalendarDuration(matrix))
        {
            return matrix;
        }

        int rows = JgsMatrix.RowCount(matrix);
        int cols = JgsMatrix.ColCount(matrix);
        if (matrix.Type is JgsType.Number or JgsType.Bool)
        {
            (rows, cols) = (1, 1);
        }

        if (cols is not (1 or 3 or 6))
        {
            throw new JgsRuntimeException(line, col,
                "calendarDuration: a single argument is a matrix of components with 1, 3 or 6 columns.");
        }

        double[] flat = ToDoubles("calendarDuration", matrix, line, col);
        var elements = new Dictionary<string, JgsValue>[rows];
        for (int r = 0; r < rows; r++)
        {
            double At(int c) => c < cols ? flat[(c * rows) + r] : 0;
            double months = cols == 1 ? 0 : (At(0) * 12) + At(1);
            double days = cols == 1 ? At(0) : At(2);
            double time = cols == 6
                ? (At(3) * JgsTime.MsPerHour) + (At(4) * JgsTime.MsPerMinute) + (At(5) * JgsTime.MsPerSecond)
                : 0;
            elements[r] = CalendarElement(months, days, time);
        }

        return CalendarValue(elements, JgsValue.Number(0));
    }

    /// <summary>
    /// The calendar difference between two moments, expressed in the components asked for.
    /// </summary>
    /// <remarks>
    /// Whole months first, then whole days, then whatever time is left — which is the order the
    /// components are applied in when the answer is added back, and the only order in which adding it
    /// back lands on the moment it was measured to.
    /// </remarks>
    private static Dictionary<string, JgsValue> CalendarBetween(
        double fromMs, double toMs, string components, JgsTimeTag? tag, int line, int col)
    {
        foreach (char c in components)
        {
            if (c is not ('y' or 'q' or 'm' or 'w' or 'd' or 't'))
            {
                throw new JgsRuntimeException(line, col,
                    $"between: '{components}' is not a set of components; it takes the letters y, q, m, w, d and t.");
            }
        }

        if (double.IsNaN(fromMs) || double.IsNaN(toMs))
        {
            return CalendarElement(double.NaN, double.NaN, double.NaN);
        }

        bool wantsMonths = components.Contains('y') || components.Contains('q') || components.Contains('m');
        bool wantsDays = components.Contains('d') || components.Contains('w');
        bool wantsTime = components.Contains('t');

        DateTime from = JgsTime.WallClock(fromMs, tag);
        DateTime to = JgsTime.WallClock(toMs, tag);
        int sign = to >= from ? 1 : -1;
        if (sign < 0)
        {
            (from, to) = (to, from);
        }

        double months = 0;
        if (wantsMonths)
        {
            months = ((to.Year - from.Year) * 12) + to.Month - from.Month;
            if (from.AddMonths((int)months) > to)
            {
                months--;
            }

            from = from.AddMonths((int)months);
        }

        double days = 0;
        if (wantsDays)
        {
            days = System.Math.Floor((to - from).TotalDays);
            from = from.AddDays(days);
        }

        double millis = wantsTime ? (to - from).TotalMilliseconds : 0;
        return CalendarElement(sign * months, sign * days, sign * millis);
    }

    // --- Arithmetic --------------------------------------------------------------------------------

    /// <summary>
    /// Applies a binary operator where at least one side is a calendar duration.
    /// </summary>
    /// <remarks>
    /// The four things a calendar duration can be in: added to or taken from another one, scaled by a
    /// number, added to a plain duration (which lands in the time component), and added to or taken
    /// from a datetime — which is the only one where the components stop being bookkeeping and become
    /// calendar arithmetic. Everything else is refused by name, because a calendar duration that
    /// quietly answered with an average month would be the exact failure ADR 0064 declined to build.
    /// </remarks>
    private static JgsValue CalendarBinary(
        TokenType op, string symbol, JgsValue left, JgsValue right, int line, int col)
    {
        bool leftIsCalendar = IsCalendarDuration(left);
        bool rightIsCalendar = IsCalendarDuration(right);

        if (leftIsCalendar && rightIsCalendar)
        {
            if (op is not (TokenType.Plus or TokenType.Minus))
            {
                throw new JgsRuntimeException(line, col,
                    $"'{symbol}' cannot combine two calendarDurations; add them or subtract them.");
            }

            int sign = op == TokenType.Minus ? -1 : 1;
            return CalendarZip(left, right, (a, b) => (
                a.Months + (sign * b.Months), a.Days + (sign * b.Days), a.Millis + (sign * b.Millis)));
        }

        // A datetime on either side of a plus, or on the left of a minus.
        if (left.IsDatetime || right.IsDatetime)
        {
            JgsValue moment = left.IsDatetime ? left : right;
            JgsValue span = left.IsDatetime ? right : left;
            if (op == TokenType.Plus || (op == TokenType.Minus && left.IsDatetime))
            {
                return CalendarShift(moment, span, op == TokenType.Minus ? -1 : 1);
            }

            throw new JgsRuntimeException(line, col, op == TokenType.Minus
                ? $"'{symbol}' cannot subtract a datetime from a calendarDuration; a length of time minus a point in time is nothing."
                : $"'{symbol}' cannot combine a datetime with a calendarDuration; add or subtract it instead.");
        }

        // A plain duration joins the time component, which is the component that is a count of
        // milliseconds and the only one it can join without inventing a month length.
        JgsValue calendar = leftIsCalendar ? left : right;
        JgsValue other = leftIsCalendar ? right : left;
        if (other.IsDuration)
        {
            if (op is not (TokenType.Plus or TokenType.Minus))
            {
                throw new JgsRuntimeException(line, col,
                    $"'{symbol}' cannot combine a calendarDuration with a duration; add or subtract it.");
            }

            double[] spans = TimeMs(other);
            int sign = op == TokenType.Minus ? (leftIsCalendar ? -1 : 1) : 1;
            JgsValue shifted = CalendarMap(calendar,
                (element, i) => (element.Months, element.Days,
                    element.Millis + (sign * (spans.Length == 1 ? spans[0] : spans[i]))));

            // duration - calendarDuration negates the calendar part as well as adding the span.
            return op == TokenType.Minus && !leftIsCalendar
                ? CalendarMap(shifted, static (e, _) => (-e.Months, -e.Days, e.Millis))
                : shifted;
        }

        if (other.IsTime)
        {
            throw new JgsRuntimeException(line, col,
                $"'{symbol}' cannot combine a calendarDuration with a {TimeClassName(other)}.");
        }

        // A plain number scales every component. MATLAB allows it, and it is the one operation where
        // the components do not need to know anything about each other.
        if (op is TokenType.Star or TokenType.DotStar
            || ((op is TokenType.Slash or TokenType.DotSlash) && leftIsCalendar))
        {
            double[] factors = ToDoubles(symbol, other, line, col);
            bool dividing = op is TokenType.Slash or TokenType.DotSlash;
            return CalendarMap(calendar, (element, i) =>
            {
                double by = factors.Length == 1 ? factors[0] : factors[i];
                double scale = dividing ? 1.0 / by : by;
                return (element.Months * scale, element.Days * scale, element.Millis * scale);
            });
        }

        throw new JgsRuntimeException(line, col,
            $"'{symbol}' cannot combine a calendarDuration with a number: a calendar length has no units of its own. "
            + "Scale it by a number, or add a duration to say how much time to add.");
    }

    /// <summary>Applies a calendar duration to each moment: months, then days, then time.</summary>
    /// <remarks>
    /// The order is what makes the type worth having. Adding a month to the 31st of January is the
    /// 29th of February; adding thirty-one days is the 3rd of March. Doing the months first and
    /// letting <see cref="DateTime.AddMonths"/> clamp is MATLAB's rule as well as .NET's.
    /// </remarks>
    private static JgsValue CalendarShift(JgsValue moment, JgsValue span, int sign)
    {
        JgsTimeTag tag = moment.TimeTag!;
        double[] source = TimeMs(moment);
        JgsStructArray parts = span.AsStructArray;
        int length = System.Math.Max(source.Length, parts.Length);
        var values = new double[length];

        for (int i = 0; i < length; i++)
        {
            double ms = source.Length == 1 ? source[0] : source[i];
            (double months, double days, double millis) =
                ReadCalendar(parts, parts.Length == 1 ? 0 : i);
            if (double.IsNaN(ms) || double.IsNaN(months) || double.IsNaN(days) || double.IsNaN(millis))
            {
                values[i] = double.NaN;
                continue;
            }

            DateTime at = JgsTime.WallClock(ms, tag);
            at = at.AddMonths((int)(sign * months));
            at = at.AddDays(sign * days);
            values[i] = JgsTime.FromWallClock(at, tag) + (sign * millis);
        }

        return TimeLike(source.Length >= parts.Length ? moment : JgsValue.Number(0), values, tag);
    }

    /// <summary>Maps every element of a calendar duration through <paramref name="map"/>.</summary>
    private static JgsValue CalendarMap(
        JgsValue value, Func<(double Months, double Days, double Millis), int, (double, double, double)> map)
    {
        JgsStructArray parts = value.AsStructArray;
        var elements = new Dictionary<string, JgsValue>[parts.Length];
        for (int i = 0; i < elements.Length; i++)
        {
            (double months, double days, double millis) = map(ReadCalendar(parts, i), i);
            elements[i] = CalendarElement(months, days, millis);
        }

        return CalendarValue(elements, value);
    }

    /// <summary>Pairs two calendar durations elementwise, broadcasting a scalar over an array.</summary>
    private static JgsValue CalendarZip(
        JgsValue left, JgsValue right,
        Func<(double Months, double Days, double Millis), (double Months, double Days, double Millis),
            (double, double, double)> combine)
    {
        JgsStructArray a = left.AsStructArray;
        JgsStructArray b = right.AsStructArray;
        int length = System.Math.Max(a.Length, b.Length);
        var elements = new Dictionary<string, JgsValue>[length];
        for (int i = 0; i < length; i++)
        {
            (double months, double days, double millis) = combine(
                ReadCalendar(a, a.Length == 1 ? 0 : i),
                ReadCalendar(b, b.Length == 1 ? 0 : i));
            elements[i] = CalendarElement(months, days, millis);
        }

        return CalendarValue(elements, a.Length >= b.Length ? left : right);
    }

    /// <summary>How one calendar duration writes itself, in MATLAB's own composition.</summary>
    internal static string FormatCalendar(double months, double days, double millis)
    {
        if (double.IsNaN(months) || double.IsNaN(days) || double.IsNaN(millis))
        {
            return "NaN";
        }

        var parts = new List<string>(4);
        double years = System.Math.Truncate(months / 12);
        double leftoverMonths = months - (years * 12);
        if (years != 0)
        {
            parts.Add(years.ToString("0.####", CultureInfo.InvariantCulture) + "y");
        }

        if (leftoverMonths != 0)
        {
            parts.Add(leftoverMonths.ToString("0.####", CultureInfo.InvariantCulture) + "mo");
        }

        if (days != 0)
        {
            parts.Add(days.ToString("0.####", CultureInfo.InvariantCulture) + "d");
        }

        if (millis != 0)
        {
            parts.Add(JgsTime.Format(millis, JgsTime.DurationTag()));
        }

        return parts.Count == 0 ? "0d" : string.Join(" ", parts);
    }
}
