using System.Globalization;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Reading a time apart and putting it back into other shapes (M64): the field accessors, the
/// conversions to and from the older serial-date surface, and the boundary moves — <c>dateshift</c>,
/// <c>isbetween</c>, <c>timeofday</c>.
/// </summary>
/// <remarks>
/// Every accessor here is the same shape of function — take the milliseconds, turn each into a
/// <see cref="DateTime"/>, read one field off it — so they are all declared from one helper. The
/// exceptions are the ones that answer with a time rather than a number, and those say so.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the accessors and conversions into <paramref name="env"/>.</summary>
    internal static void RegisterTimePartBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        // --- The field accessors ------------------------------------------------------------------
        Field(env, "year", static m => m.Year);
        Field(env, "month", static m => m.Month);
        Field(env, "day", static m => m.Day);
        Field(env, "hour", static m => m.Hour);
        Field(env, "minute", static m => m.Minute);
        Field(env, "week", static m => ISOWeek.GetWeekOfYear(m));
        Field(env, "quarter", static m => ((m.Month - 1) / 3) + 1);

        // weekday counts from Sunday = 1, which is MATLAB's convention and not .NET's zero-based one.
        Field(env, "weekday", static m => (int)m.DayOfWeek + 1);

        // second carries the fraction, unlike every other field: MATLAB's second(t) is 30.25 for a
        // moment a quarter of a second past the half minute, where its minute(t) is a whole number.
        env.Declare("second", JgsValue.Function(new BuiltinFunction("second", (args, line, col) =>
        {
            Arity("second", args, 1, line, col);
            JgsValue moment = RequireDatetime("second", args[0], line, col);
            return MapMs(moment, static ms =>
            {
                DateTime at = JgsTime.ToDateTime(ms);
                return at.Second + (at.Millisecond / 1000.0);
            });
        })));

        // --- The grouped accessors, which answer with several numbers at once ---------------------
        Parts(env, "ymd", static m => [m.Year, m.Month, m.Day]);
        Parts(env, "hms", static m => [m.Hour, m.Minute, m.Second + (m.Millisecond / 1000.0)]);

        // --- Conversions out ----------------------------------------------------------------------
        Define("datevec", (args, line, col) =>
        {
            ArityRange("datevec", args, 1, 1, line, col);
            double[] source = args[0].IsDatetime
                ? TimeMs(args[0])
                : System.Array.ConvertAll(ToDoubles("datevec", args[0], line, col), JgsTime.FromDatenum);

            // One row per moment, six columns — the shape datetime() reads back, which is what makes
            // datetime(datevec(t)) a round trip rather than a coincidence.
            var values = new double[source.Length * 6];
            for (int r = 0; r < source.Length; r++)
            {
                DateTime at = JgsTime.ToDateTime(source[r]);
                double[] row = [at.Year, at.Month, at.Day, at.Hour, at.Minute, at.Second + (at.Millisecond / 1000.0)];
                for (int c = 0; c < 6; c++)
                {
                    values[(c * source.Length) + r] = row[c];   // column-major
                }
            }

            JgsValue built = Numbers(values);
            built.Reshape(source.Length, 6);
            return built;
        });

        Define("exceltime", (args, line, col) =>
        {
            Arity("exceltime", args, 1, line, col);
            return MapMs(RequireDatetime("exceltime", args[0], line, col), static ms => ms / JgsTime.MsPerDay);
        });

        Define("posixtime", (args, line, col) =>
        {
            Arity("posixtime", args, 1, line, col);
            double epoch = JgsTime.FromDateTime(DateTime.UnixEpoch);
            return MapMs(RequireDatetime("posixtime", args[0], line, col),
                ms => (ms - epoch) / JgsTime.MsPerSecond);
        });

        Define("juliandate", (args, line, col) =>
        {
            Arity("juliandate", args, 1, line, col);
            double epoch = JgsTime.FromDateTime(new DateTime(1858, 11, 17));
            return MapMs(RequireDatetime("juliandate", args[0], line, col),
                ms => ((ms - epoch) / JgsTime.MsPerDay) + 2400000.5);
        });

        Define("yyyymmdd", (args, line, col) =>
        {
            Arity("yyyymmdd", args, 1, line, col);
            return MapMs(RequireDatetime("yyyymmdd", args[0], line, col), static ms =>
            {
                DateTime at = JgsTime.ToDateTime(ms);
                return (at.Year * 10000) + (at.Month * 100) + at.Day;
            });
        });

        // --- Boundary moves -------------------------------------------------------------------------
        Define("timeofday", (args, line, col) =>
        {
            Arity("timeofday", args, 1, line, col);
            JgsValue moment = RequireDatetime("timeofday", args[0], line, col);
            double[] source = TimeMs(moment);
            var values = new double[source.Length];
            for (int i = 0; i < values.Length; i++)
            {
                // Modulo a day, taken the positive way round so a date before the epoch does not
                // answer with a negative time of day.
                double remainder = source[i] % JgsTime.MsPerDay;
                values[i] = remainder < 0 ? remainder + JgsTime.MsPerDay : remainder;
            }

            return TimeLike(moment, values, JgsTime.DurationTag());
        });

        Define("dateshift", (args, line, col) =>
        {
            ArityRange("dateshift", args, 3, 4, line, col);
            JgsValue moment = RequireDatetime("dateshift", args[0], line, col);
            string where = TextOfArgument("dateshift", args[1], line, col).ToLowerInvariant();
            string unit = TextOfArgument("dateshift", args[2], line, col).ToLowerInvariant();
            double offset = args.Count == 4 ? Num("dateshift", args, 3, line, col) : 0;

            if (where is not ("start" or "end" or "dayofweek"))
            {
                throw new JgsRuntimeException(line, col,
                    $"dateshift: '{where}' is not a boundary; it takes 'start', 'end' or 'dayofweek'.");
            }

            if (where == "dayofweek")
            {
                throw new JgsRuntimeException(line, col,
                    "dateshift: 'dayofweek' is not implemented; shift to the start of a week and add days.");
            }

            double[] source = TimeMs(moment);
            var values = new double[source.Length];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = ShiftToBoundary(source[i], unit, where == "end", offset, line, col);
            }

            return TimeLike(moment, values, moment.TimeTag!);
        });

        Define("isbetween", (args, line, col) =>
        {
            Arity("isbetween", args, 3, line, col);
            double[] test = TimeOrNumbers("isbetween", args[0], line, col);
            double[] lower = TimeOrNumbers("isbetween", args[1], line, col);
            double[] upper = TimeOrNumbers("isbetween", args[2], line, col);

            var flags = new JgsValue[test.Length];
            for (int i = 0; i < test.Length; i++)
            {
                double low = lower.Length == 1 ? lower[0] : lower[i];
                double high = upper.Length == 1 ? upper[0] : upper[i];
                flags[i] = JgsValue.Bool(test[i] >= low && test[i] <= high);
            }

            if (flags.Length == 1)
            {
                return flags[0];
            }

            JgsValue answer = JgsValue.Array(flags);
            answer.TakeShapeOf(args[0]);
            return answer;
        });

        Define("between", (args, line, col) => throw new JgsRuntimeException(line, col,
            "between answers with a calendarDuration, which JGraph does not have. Subtract the two " +
            "datetimes instead, which gives the duration between them."));

        // --- The older serial-date surface, taught about the new type ------------------------------
        Define("etime", (args, line, col) =>
        {
            Arity("etime", args, 2, line, col);
            double[] later = ToDoubles("etime", args[0], line, col);
            double[] earlier = ToDoubles("etime", args[1], line, col);
            if (later.Length != 6 || earlier.Length != 6)
            {
                throw new JgsRuntimeException(line, col, "etime expects two six-element clock vectors.");
            }

            double Ms(double[] v) => JgsTime.FromComponents(v[0], v[1], v[2], v[3], v[4], v[5]);
            return JgsValue.Number((Ms(later) - Ms(earlier)) / JgsTime.MsPerSecond);
        });

        Define("addtodate", (args, line, col) =>
        {
            Arity("addtodate", args, 3, line, col);
            double serial = Num("addtodate", args, 0, line, col);
            double quantity = Num("addtodate", args, 1, line, col);
            string unit = Str("addtodate", args, 2, line, col).ToLowerInvariant();

            DateTime at = JgsTime.ToDateTime(JgsTime.FromDatenum(serial));
            DateTime moved = unit switch
            {
                "year" => at.AddYears((int)quantity),
                "month" => at.AddMonths((int)quantity),
                "day" => at.AddDays(quantity),
                "hour" => at.AddHours(quantity),
                "minute" => at.AddMinutes(quantity),
                "second" => at.AddSeconds(quantity),
                "millisecond" => at.AddMilliseconds(quantity),
                _ => throw new JgsRuntimeException(line, col,
                    $"addtodate: '{unit}' is not a unit; it takes year, month, day, hour, minute, second or millisecond."),
            };

            return JgsValue.Number(JgsTime.ToDatenum(JgsTime.FromDateTime(moved)));
        });

        Define("eomday", (args, line, col) =>
        {
            Arity("eomday", args, 2, line, col);
            double[] years = ToDoubles("eomday", args[0], line, col);
            double[] months = ToDoubles("eomday", args[1], line, col);
            int length = System.Math.Max(years.Length, months.Length);
            var values = new double[length];
            for (int i = 0; i < length; i++)
            {
                int y = (int)(years.Length == 1 ? years[0] : years[i]);
                int m = (int)(months.Length == 1 ? months[0] : months[i]);
                values[i] = DateTime.DaysInMonth(y, m);
            }

            return length == 1 ? JgsValue.Number(values[0]) : Numbers(values);
        });

        Define("calendar", (args, line, col) =>
        {
            ArityRange("calendar", args, 0, 2, line, col);
            DateTime today = DateTime.Today;
            int year = args.Count >= 1 ? (int)Num("calendar", args, 0, line, col) : today.Year;
            int month = args.Count >= 2 ? (int)Num("calendar", args, 1, line, col) : today.Month;

            // Six rows of seven days, Sunday first, zero where the grid runs past the month — the
            // layout MATLAB's calendar prints and the one a script indexes into.
            var grid = new double[42];
            var first = new DateTime(year, month, 1);
            int lead = (int)first.DayOfWeek;
            int days = DateTime.DaysInMonth(year, month);
            for (int d = 0; d < days; d++)
            {
                int slot = lead + d;
                grid[((slot % 7) * 6) + (slot / 7)] = d + 1;   // column-major
            }

            JgsValue built = Numbers(grid);
            built.Reshape(6, 7);
            return built;
        });
    }

    /// <summary>Declares one accessor that reads a single field off each moment.</summary>
    private static void Field(JgsEnvironment env, string name, Func<DateTime, int> read) =>
        env.Declare(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) =>
        {
            Arity(name, args, 1, line, col);
            return MapMs(RequireDatetime(name, args[0], line, col), ms => read(JgsTime.ToDateTime(ms)));
        })));

    /// <summary>
    /// Declares one of the grouped accessors, which hands back its parts as several outputs —
    /// <c>[y, m, d] = ymd(t)</c> — and as a single row when only one output is wanted.
    /// </summary>
    private static void Parts(JgsEnvironment env, string name, Func<DateTime, double[]> read)
    {
        JgsValue[] Split(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
        {
            Arity(name, args, 1, line, col);
            JgsValue moment = RequireDatetime(name, args[0], line, col);
            double[] source = TimeMs(moment);
            int fields = read(JgsTime.Epoch).Length;

            var columns = new double[fields][];
            for (int f = 0; f < fields; f++)
            {
                columns[f] = new double[source.Length];
            }

            for (int i = 0; i < source.Length; i++)
            {
                double[] parts = read(JgsTime.ToDateTime(source[i]));
                for (int f = 0; f < fields; f++)
                {
                    columns[f][i] = parts[f];
                }
            }

            var outputs = new JgsValue[fields];
            for (int f = 0; f < fields; f++)
            {
                outputs[f] = columns[f].Length == 1 ? JgsValue.Number(columns[f][0]) : Numbers(columns[f]);
            }

            return System.Math.Max(wanted, 1) >= fields ? outputs : outputs[..System.Math.Max(wanted, 1)];
        }

        env.Declare(name, JgsValue.Function(new BuiltinFunction(name,
            (args, line, col) => Split(args, 1, line, col)[0])
        {
            MultiOutput = Split,
        }));
    }

    /// <summary>The argument as a datetime, or a clear refusal naming what it was.</summary>
    private static JgsValue RequireDatetime(string name, JgsValue value, int line, int col)
    {
        if (value.IsDatetime)
        {
            return value;
        }

        // A serial date number is still what half the older surface hands about, so it is accepted
        // and converted rather than refused — which is what makes year(now) work.
        if (!value.IsTime && value.Type is JgsType.Number or JgsType.Array or JgsType.Bool)
        {
            double[] serials = ToDoubles(name, value, line, col);
            return TimeLike(value, System.Array.ConvertAll(serials, JgsTime.FromDatenum),
                new JgsTimeTag(JgsTimeKind.Datetime, JgsTime.DefaultDatetimeFormat));
        }

        throw new JgsRuntimeException(line, col,
            $"{name} expects a datetime or a serial date number, but got a {value.TypeName}.");
    }

    /// <summary>The milliseconds of a time, or the serial dates of a plain number read as dates.</summary>
    private static double[] TimeOrNumbers(string name, JgsValue value, int line, int col) =>
        value.IsTime ? TimeMs(value) : ToDoubles(name, value, line, col);

    /// <summary>Maps every millisecond of a time through <paramref name="read"/>, keeping the shape.</summary>
    private static JgsValue MapMs(JgsValue time, Func<double, double> read)
    {
        double[] source = TimeMs(time);
        var values = new double[source.Length];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = double.IsNaN(source[i]) ? double.NaN : read(source[i]);
        }

        if (values.Length == 1)
        {
            return JgsValue.Number(values[0]);
        }

        JgsValue built = Numbers(values);
        built.TakeShapeOf(time);
        return built;
    }

    /// <summary>Moves one moment to the start or the end of the unit it falls in.</summary>
    private static double ShiftToBoundary(double ms, string unit, bool toEnd, double offset, int line, int col)
    {
        if (double.IsNaN(ms))
        {
            return ms;
        }

        DateTime at = JgsTime.ToDateTime(ms);
        DateTime start = unit switch
        {
            "year" => new DateTime(at.Year, 1, 1),
            "quarter" => new DateTime(at.Year, (((at.Month - 1) / 3) * 3) + 1, 1),
            "month" => new DateTime(at.Year, at.Month, 1),
            "week" => at.Date.AddDays(-(int)at.DayOfWeek),
            "day" => at.Date,
            "hour" => new DateTime(at.Year, at.Month, at.Day, at.Hour, 0, 0),
            "minute" => new DateTime(at.Year, at.Month, at.Day, at.Hour, at.Minute, 0),
            "second" => new DateTime(at.Year, at.Month, at.Day, at.Hour, at.Minute, at.Second),
            _ => throw new JgsRuntimeException(line, col,
                $"dateshift: '{unit}' is not a unit; it takes year, quarter, month, week, day, hour, minute or second."),
        };

        // The offset counts whole units from the one the moment falls in, so shifting to the start of
        // 'month' with an offset of 1 is the first of next month whatever the month lengths are.
        start = unit switch
        {
            "year" => start.AddYears((int)offset),
            "quarter" => start.AddMonths((int)offset * 3),
            "month" => start.AddMonths((int)offset),
            "week" => start.AddDays(offset * 7),
            "day" => start.AddDays(offset),
            "hour" => start.AddHours(offset),
            "minute" => start.AddMinutes(offset),
            _ => start.AddSeconds(offset),
        };

        if (!toEnd)
        {
            return JgsTime.FromDateTime(start);
        }

        // The end of a unit is the last instant inside it: the start of the next one, less a
        // millisecond, which is the finest thing this storage can tell apart.
        DateTime next = unit switch
        {
            "year" => start.AddYears(1),
            "quarter" => start.AddMonths(3),
            "month" => start.AddMonths(1),
            "week" => start.AddDays(7),
            "day" => start.AddDays(1),
            "hour" => start.AddHours(1),
            "minute" => start.AddMinutes(1),
            _ => start.AddSeconds(1),
        };

        return JgsTime.FromDateTime(next) - 1;
    }
}
