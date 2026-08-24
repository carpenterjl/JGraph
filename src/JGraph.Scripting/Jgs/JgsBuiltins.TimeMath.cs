namespace JGraph.Scripting.Jgs;

/// <summary>
/// What the operators and the reductions do when they meet a time (M64): the one place that knows
/// a datetime minus a datetime is a duration, and the one pass that teaches the reductions to hand
/// back what they were given.
/// </summary>
/// <remarks>
/// <para>
/// The rule table below is the whole of time arithmetic. It is deliberately a table rather than a
/// tree of conditions, because MATLAB's rules are a table: which pairs of kinds an operator accepts,
/// and what kind the answer is. Everything the table refuses is refused by name, which is what makes
/// <c>t * 2</c> say that a point in time cannot be scaled instead of quietly answering with a date
/// in the year 4048.
/// </para>
/// <para>
/// This is also the generalized dispatch M68's operator overloading is meant to reuse: an operator
/// asking the values what they are before the numeric kernel sees them.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Whether <paramref name="op"/> is one this file has an opinion about.</summary>
    private static bool IsTimeOperator(TokenType op) => op is TokenType.Plus or TokenType.Minus
        or TokenType.Star or TokenType.DotStar or TokenType.Slash or TokenType.DotSlash;

    /// <summary>
    /// Whether an operator applied to these two operands is time arithmetic rather than ordinary
    /// numeric work. Comparisons are deliberately not here: two times compare on their milliseconds,
    /// which is already the right answer, and the kernel that does it needs no teaching.
    /// </summary>
    internal static bool IsTimeArithmetic(TokenType op, JgsValue left, JgsValue right) =>
        (left.IsTime || right.IsTime) && IsTimeOperator(op);

    /// <summary>
    /// Applies a binary operator to at least one time operand, deciding what kind of thing the
    /// answer is before handing the milliseconds to the ordinary numeric kernel.
    /// </summary>
    internal static JgsValue TimeBinary(
        TokenType op, JgsValue left, JgsValue right, int line, int col,
        Func<TokenType, JgsValue, JgsValue, JgsValue> numeric)
    {
        string symbol = TimeOperatorSymbol(op);
        bool leftIsTime = left.IsTime;
        bool rightIsTime = right.IsTime;

        // A calendar duration is not a count of milliseconds, so it resolves before the table below
        // and never reaches the numeric kernel (M82). Its three components are applied in order, and
        // that order is the whole point of the type: a month then a day is not a day then a month.
        if (IsCalendarDuration(left) || IsCalendarDuration(right))
        {
            return CalendarBinary(op, symbol, left, right, line, col);
        }

        // --- Two times ------------------------------------------------------------------------
        if (leftIsTime && rightIsTime)
        {
            if (left.IsDatetime && right.IsDatetime)
            {
                if (op != TokenType.Minus)
                {
                    throw new JgsRuntimeException(line, col,
                        $"'{symbol}' cannot combine two datetimes; only subtraction is defined on two points " +
                        "in time, and it answers with the duration between them.");
                }

                return WrapTime(numeric(op, Untagged(left), Untagged(right)), JgsTime.DurationTag());
            }

            if (left.IsDuration && right.IsDuration)
            {
                if (op is TokenType.Plus or TokenType.Minus)
                {
                    return WrapTime(numeric(op, Untagged(left), Untagged(right)), left.TimeTag!);
                }

                if (op is TokenType.Slash or TokenType.DotSlash)
                {
                    // A duration over a duration is how many times one goes into the other — a plain
                    // number, and the reason `elapsed / seconds(1)` is how a script counts seconds.
                    return numeric(op, Untagged(left), Untagged(right));
                }

                throw new JgsRuntimeException(line, col,
                    $"'{symbol}' cannot combine two durations; add them, subtract them, or divide one by the other.");
            }

            // A datetime and a duration, in one order or the other.
            JgsValue moment = left.IsDatetime ? left : right;
            JgsValue span = left.IsDatetime ? right : left;

            if (op == TokenType.Plus)
            {
                return WrapTime(numeric(op, Untagged(moment), Untagged(span)), moment.TimeTag!);
            }

            if (op == TokenType.Minus && left.IsDatetime)
            {
                return WrapTime(numeric(op, Untagged(moment), Untagged(span)), moment.TimeTag!);
            }

            throw new JgsRuntimeException(line, col, op == TokenType.Minus
                ? $"'{symbol}' cannot subtract a datetime from a duration; a length of time minus a point in time is nothing."
                : $"'{symbol}' cannot combine a datetime with a duration; add or subtract the duration instead.");
        }

        // --- One time and one plain number ------------------------------------------------------
        JgsValue time = leftIsTime ? left : right;
        JgsValue plain = leftIsTime ? right : left;

        if (time.IsDatetime)
        {
            throw new JgsRuntimeException(line, col,
                $"'{symbol}' cannot combine a datetime with a number: a point in time has no units of its own. " +
                "Wrap the number in days, hours, minutes or seconds to say how much time it is.");
        }

        // A duration scaled by a number is a duration; anything else is refused. Dividing a number by
        // a duration would be a rate, which has no type here.
        if (op is TokenType.Star or TokenType.DotStar
            || ((op is TokenType.Slash or TokenType.DotSlash) && leftIsTime))
        {
            return WrapTime(
                numeric(op, leftIsTime ? Untagged(time) : plain, leftIsTime ? plain : Untagged(time)),
                time.TimeTag!);
        }

        throw new JgsRuntimeException(line, col,
            $"'{symbol}' cannot combine a duration with a plain number; multiply or divide it by one, " +
            "or make the number a duration with seconds, minutes, hours or days.");
    }

    /// <summary>The same value with its time tag taken off, so the numeric kernel sees milliseconds.</summary>
    /// <remarks>
    /// A fresh wrapper rather than a mutation: the tag is mint-time-only by design, and the operand
    /// belongs to whatever variable the caller wrote.
    /// </remarks>
    private static JgsValue Untagged(JgsValue time)
    {
        JgsValue plain = Numbers(TimeMs(time));
        if (time.ArrayLength > 1 || time.IsShaped || time.IsNd)
        {
            plain.TakeShapeOf(time);
        }

        return plain;
    }

    private static string TimeOperatorSymbol(TokenType op) => op switch
    {
        TokenType.Plus => "+",
        TokenType.Minus => "-",
        TokenType.Star => "*",
        TokenType.DotStar => ".*",
        TokenType.Slash => "/",
        _ => "./",
    };

    // --- The reductions ---------------------------------------------------------------------------

    /// <summary>
    /// The reductions and orderings that answer with the same kind of time they were handed: the
    /// smallest of some datetimes is a datetime, and sorting durations gives durations back.
    /// </summary>
    private static readonly string[] TimeKeepingBuiltins =
    [
        "min", "max", "sort", "median", "mode", "unique", "mean",
        "cummax", "cummin", "sortrows", "fliplr", "flipud", "flip", "circshift", "transpose",
    ];

    /// <summary>
    /// The ones whose answer is a length of time whatever they were handed: the gap between two
    /// datetimes is a duration, and so is the spread of a set of them.
    /// </summary>
    private static readonly string[] TimeToDurationBuiltins = ["diff", "std", "range"];

    /// <summary>
    /// The ones that make sense for a duration and not for a datetime. Adding up points in time is
    /// the arithmetic MATLAB refuses outright, and refusing it here says so rather than answering.
    /// </summary>
    private static readonly string[] DurationOnlyBuiltins = ["sum", "cumsum", "abs"];

    /// <summary>
    /// Teaches the reductions about time, as one pass over the finished environment rather than as
    /// twenty edits — the same shape M63 used for the elementwise text rule, and for the same reason:
    /// it is one rule. Each wrapper strips the tag, calls what was already there, and puts the right
    /// tag back on the answer.
    /// </summary>
    /// <remarks>
    /// The pass must run after every ordinary define and after the dimension-aware wrapping, because
    /// what it wraps is whatever name is finally reachable. M63 learned this the hard way: a mark
    /// applied to a builtin that is re-declared afterwards is a mark on nothing.
    /// </remarks>
    internal static void TimeAwareReductions(JgsEnvironment env)
    {
        foreach (string name in TimeKeepingBuiltins)
        {
            WrapForTime(env, name, keepKind: true, allowDatetime: true);
        }

        foreach (string name in TimeToDurationBuiltins)
        {
            WrapForTime(env, name, keepKind: false, allowDatetime: true);
        }

        foreach (string name in DurationOnlyBuiltins)
        {
            WrapForTime(env, name, keepKind: true, allowDatetime: false);
        }
    }

    private static void WrapForTime(JgsEnvironment env, string name, bool keepKind, bool allowDatetime)
    {
        if (!env.TryGet(name, out JgsValue existing)
            || existing.Type != JgsType.Function
            || existing.AsCallable is not BuiltinFunction inner)
        {
            return;
        }

        // Strips the tags off every time argument and says what to put back on the answer, or null
        // when nothing here is a time and the inner function should simply be handed what it was given.
        JgsTimeTag? Prepare(IReadOnlyList<JgsValue> args, out IReadOnlyList<JgsValue> forwarded, int line, int col)
        {
            forwarded = args;
            if (args.Count == 0 || !args[0].IsTime)
            {
                return null;
            }

            JgsValue time = args[0];
            if (!allowDatetime && time.IsDatetime)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name} is not defined on datetimes: adding or accumulating points in time has no meaning. " +
                    "Subtract them first, which gives durations.");
            }

            var list = new List<JgsValue>(args.Count);
            foreach (JgsValue arg in args)
            {
                list.Add(arg.IsTime ? Untagged(arg) : arg);
            }

            forwarded = list;
            return keepKind ? time.TimeTag! : JgsTime.DurationTag();
        }

        var wrapper = new BuiltinFunction(name, (args, line, col) =>
        {
            JgsTimeTag? tag = Prepare(args, out IReadOnlyList<JgsValue> forwarded, line, col);
            JgsValue answer = inner.Call(forwarded, line, col);
            return tag is null ? answer : WrapTime(answer, tag);
        })
        {
            // The flags of whatever was there have to come across, or a two-output name silently
            // becomes a one-output one — which is exactly how M61's arrayfun lost its second output.
            // Only the FIRST output is a time: the second is an index into the input ([m, i] = min(t)),
            // and stamping that with a datetime tag would say the position was a date.
            MultiOutput = inner.MultiOutput is null ? null : (args, wanted, line, col) =>
            {
                JgsTimeTag? tag = Prepare(args, out IReadOnlyList<JgsValue> forwarded, line, col);
                JgsValue[] answers = inner.MultiOutput(forwarded, wanted, line, col);
                if (tag is not null && answers.Length > 0)
                {
                    answers[0] = WrapTime(answers[0], tag);
                }

                return answers;
            },
            BindsAnsAsStatement = inner.BindsAnsAsStatement,
            AutoCallsBare = inner.AutoCallsBare,
            KeepsStringArguments = inner.KeepsStringArguments,
        };

        env.Declare(name, JgsValue.Function(wrapper));
    }
}
