using System.Runtime.CompilerServices;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>timer</c> and the verbs that drive it — <c>start</c>, <c>stop</c>, <c>wait</c>, <c>delete</c>
/// (V6, ADR 0167, appendix A #105).
/// </summary>
/// <remarks>
/// <para>
/// <b>The object.</b> A timer is a struct wearing the class name, like a <c>VideoWriter</c>, and a
/// handle class as MATLAB's is: <c>start(t)</c> has to be visible to every <c>t</c> the script holds,
/// and a callback's first argument is the timer itself. The clock, the count and the deleted mark
/// live beside the value in a <see cref="JgsTimerState"/>; the properties are the struct's fields,
/// so <c>t.Period</c> reads like any dot, and the read-only ones (<c>Running</c>,
/// <c>TasksExecuted</c>, the two periods) are written here as the state changes.
/// </para>
/// <para>
/// <b>When a callback runs.</b> On the script thread at a drain point only — see
/// <see cref="JgsTimerScheduler"/>. R2025b was recorded for the order: <c>start</c> runs the
/// <c>StartFcn</c> before it returns and a <c>TimerFcn</c> already due (a zero <c>StartDelay</c>)
/// after it, still before it returns; <c>stop</c> runs the <c>StopFcn</c> before it returns, and runs
/// it whether or not the timer was running; the last task's <c>StopFcn</c> runs before <c>wait</c>
/// returns; a running timer that is deleted is stopped first, with MATLAB's warning.
/// </para>
/// <para>
/// <b>ErrorFcn.</b> R2025b's <c>ErrorFcn</c> never ran in any form the probes tried — a handle of two
/// arguments, of none, of <c>varargin</c>, a named function, a command string — while its
/// documentation gives it the two arguments every callback gets, with <c>event.Data.message</c> and
/// <c>event.Data.messageID</c> from the error. The documented contract is what is built, and the
/// fixture line that R2025b answers with nothing is recorded as a divergence (appendix A #105).
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>The class name a timer answers to.</summary>
    internal const string TimerClassName = "timer";

    private static readonly ConditionalWeakTable<JgsStructArray, JgsTimerState> TimerStates = new();

    /// <summary>MATLAB's four execution modes, in their canonical spelling.</summary>
    private static readonly string[] TimerModes = ["singleShot", "fixedSpacing", "fixedDelay", "fixedRate"];

    /// <summary>The properties a script may not write.</summary>
    private static readonly HashSet<string> ReadOnlyTimerProperties = new(StringComparer.Ordinal)
    {
        "AveragePeriod", "InstantPeriod", "Running", "TasksExecuted", "Type",
    };

    /// <summary>The four callbacks, each a slot in the struct.</summary>
    private static readonly HashSet<string> TimerCallbacks = new(StringComparer.Ordinal)
    {
        "TimerFcn", "StartFcn", "StopFcn", "ErrorFcn",
    };

    private const string DeletedTimerMessage =
        "Invalid timer object. This object has been deleted and should be removed from your workspace using CLEAR.";

    /// <summary>Whether a value is a timer, deleted or not.</summary>
    internal static bool IsTimer(JgsValue value) =>
        value.Type == JgsType.Struct && value.ClassName == TimerClassName;

    /// <summary>Whether a value is a timer that <c>delete</c> has ended — what <c>isvalid</c> asks.</summary>
    internal static bool IsDeletedTimer(JgsValue value) =>
        IsTimer(value) && TimerStates.TryGetValue(value.AsStructArray, out JgsTimerState? state) && state.Deleted;

    private static JgsTimerState TimerStateOf(JgsValue timer, int line, int col) =>
        TimerStates.TryGetValue(timer.AsStructArray, out JgsTimerState? state)
            ? state
            : throw new JgsRuntimeException(line, col,
                "this timer has lost track of its clock — it was copied out of the run that made it.");

    /// <summary>Registers <c>timer</c>, <c>start</c>, <c>stop</c> and <c>wait</c>, and gives the run its scheduler.</summary>
    internal static void RegisterTimerBuiltins(
        JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host, JgsDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(interpreter);
        ArgumentNullException.ThrowIfNull(host);

        var scheduler = new JgsTimerScheduler(interpreter, host);
        host.Timers = scheduler;
        interpreter.Timers = scheduler;
        bool shares = dialect.CopyOnAssign;

        // `t = timer;` auto-calls the bare name, as containers.Map does.
        env.Builtins.Register(TimerClassName, JgsValue.Function(new BuiltinFunction(TimerClassName,
            (args, line, col) => NewTimer(scheduler, args, shares, line, col))
        {
            AutoCallsBare = true,
        }));

        // Each verb is spelled out as a call so the ownership audit can see it reach script code.
        void DefineVerb(string name, Action<JgsTimerState, int, int> verb) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) =>
            {
                Arity(name, args, 1, line, col);
                verb(RequireTimer(name, args[0], line, col), line, col);
                return JgsValue.Null;
            })
            {
                BindsAnsAsStatement = false,
            }));

        DefineVerb("start", (state, line, col) => StartTimer(state, scheduler, line, col));
        DefineVerb("stop", (state, line, col) => StopTimer(state, runStopFcn: true, line, col));
        DefineVerb("wait", (state, line, col) => WaitTimer(state, scheduler, line, col));
    }

    /// <summary>The argument as a timer, refused in MATLAB's words when it is anything else.</summary>
    private static JgsTimerState RequireTimer(string verb, JgsValue value, int line, int col)
    {
        if (!IsTimer(value))
        {
            throw new JgsRuntimeException(line, col,
                $"Undefined function '{verb}' for input arguments of type '{ClassOf(value, JgsDialect.Matlab)}'.");
        }

        JgsTimerState state = TimerStateOf(value, line, col);
        if (state.Deleted)
        {
            throw new JgsRuntimeException(line, col, DeletedTimerMessage);
        }

        return state;
    }

    // --- the constructor ------------------------------------------------------------------------

    private static JgsValue NewTimer(
        JgsTimerScheduler scheduler, IReadOnlyList<JgsValue> args, bool shares, int line, int col)
    {
        if (args.Count % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, "timer takes its properties as name/value pairs.");
        }

        string name = scheduler.NextName();
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["AveragePeriod"] = JgsValue.Number(double.NaN),
            ["BusyMode"] = JgsValue.Str("drop"),
            ["ErrorFcn"] = Empty00(),
            ["ExecutionMode"] = JgsValue.Str("singleShot"),
            ["InstantPeriod"] = JgsValue.Number(double.NaN),
            ["Name"] = JgsValue.Str(name),
            ["ObjectVisibility"] = JgsValue.Str("on"),
            ["Period"] = JgsValue.Number(1),
            ["Running"] = JgsValue.Str("off"),
            ["StartDelay"] = JgsValue.Number(0),
            ["StartFcn"] = Empty00(),
            ["StopFcn"] = Empty00(),
            ["Tag"] = JgsValue.Str(string.Empty),
            ["TasksExecuted"] = JgsValue.Number(0),
            ["TasksToExecute"] = JgsValue.Number(double.PositiveInfinity),
            ["TimerFcn"] = Empty00(),
            ["Type"] = JgsValue.Str("timer"),
            ["UserData"] = Empty00(),
        };

        JgsValue timer = JgsValue.Struct(fields);
        timer.SetClassName(TimerClassName);
        timer.AsStructArray.External = true; // V10: the timer manager's, not a container the count releases
        var state = new JgsTimerState { Value = timer, Scheduler = scheduler, Name = name };
        TimerStates.Add(timer.AsStructArray, state);

        for (int i = 0; i < args.Count; i += 2)
        {
            if (!IsTextScalar(args[i]))
            {
                throw new JgsRuntimeException(line, col, "timer takes its properties as name/value pairs.");
            }

            // The constructor reads a name in any case ('timerfcn' was accepted in R2025b); the dot
            // is exact, as a classdef property's is.
            string given = TextOf(args[i]);
            string? canonical = null;
            foreach (string property in fields.Keys)
            {
                if (property.Equals(given, StringComparison.OrdinalIgnoreCase))
                {
                    canonical = property;
                    break;
                }
            }

            if (canonical is null)
            {
                throw new JgsRuntimeException(line, col,
                    $"The name '{given}' is not an accessible property for an instance of class 'timer'.");
            }

            SetTimerProperty(state, canonical, args[i + 1], retain: shares, line, col);
        }

        return timer;
    }

    private static JgsValue Empty00() => JgsMatrix.FromColumnMajor([], 0, 0);

    /// <summary>
    /// One property written through M7's gate. A timer is a handle, so no name ever holds a counted
    /// share of its struct (<see cref="JgsValue.Share"/> hands a handle back as it is) and the gate
    /// never copies: every alias reads the write.
    /// </summary>
    private static void SetTimerField(JgsTimerState state, string name, JgsValue value)
    {
        JgsLifetime.Pin(value); // V10: the timer manager holds the timer, and it holds this, for as long as it likes
        state.Value.WritableStruct()[name] = value;
    }

    // --- properties -----------------------------------------------------------------------------

    /// <summary><c>t.Name</c>: a field read that refuses on a deleted timer and names an unknown property.</summary>
    internal static JgsValue GetTimerProperty(JgsValue timer, string field, int line, int col)
    {
        JgsTimerState state = TimerStateOf(timer, line, col);
        if (state.Deleted)
        {
            throw new JgsRuntimeException(line, col, "Invalid or deleted object.");
        }

        if (state.Fields.TryGetValue(field, out JgsValue? held))
        {
            return held;
        }

        throw new JgsRuntimeException(line, col,
            $"Unrecognized method, property, or field '{field}' for class 'timer'.");
    }

    /// <summary>
    /// <c>t.Name = v</c>, from the dot (the value is already the binding's share) or the
    /// constructor (<paramref name="retain"/>: take the entry's share of a builtin argument, M2).
    /// Every property is checked in MATLAB's words: read-only ones refuse, a number out of range
    /// refuses, a callback that is not one refuses, and <c>Period</c> cannot change while running.
    /// </summary>
    internal static void SetTimerProperty(JgsValue timer, string field, JgsValue value, bool retain, int line, int col) =>
        SetTimerProperty(TimerStateOf(timer, line, col), field, value, retain, line, col);

    private static void SetTimerProperty(JgsTimerState state, string field, JgsValue value, bool retain, int line, int col)
    {
        if (state.Deleted)
        {
            throw new JgsRuntimeException(line, col, "Invalid or deleted object.");
        }

        if (!state.Fields.ContainsKey(field))
        {
            throw new JgsRuntimeException(line, col, $"Unrecognized property '{field}' for class 'timer'.");
        }

        if (ReadOnlyTimerProperties.Contains(field))
        {
            // MATLAB's own message, doubled quotes and all.
            throw new JgsRuntimeException(line, col,
                $"Unable to set the '{field}' property of class ''timer'' because it is read-only.");
        }

        switch (field)
        {
            case "Period":
                if (state.Running)
                {
                    throw new JgsRuntimeException(line, col, "Period cannot be set while Timer is running.");
                }

                state.Period = TimerNumber(field, value, static x => x > 0, "positive", line, col);
                SetTimerField(state, field, JgsValue.Number(state.Period));
                return;

            case "StartDelay":
                state.StartDelay = TimerNumber(field, value, static x => x >= 0, "nonnegative", line, col);
                SetTimerField(state, field, JgsValue.Number(state.StartDelay));
                return;

            case "TasksToExecute":
                state.TasksToExecute = TimerNumber(field, value, static x => x > 0, "positive", line, col);
                SetTimerField(state, field, JgsValue.Number(state.TasksToExecute));
                return;

            case "ExecutionMode":
                state.Mode = TimerWord(field, value, TimerModes, line, col);
                SetTimerField(state, field, JgsValue.Str(state.Mode));
                return;

            case "BusyMode":
                SetTimerField(state, field, JgsValue.Str(TimerWord(field, value, ["drop", "queue", "error"], line, col)));
                return;

            case "ObjectVisibility":
                SetTimerField(state, field, JgsValue.Str(TimerWord(field, value, ["on", "off"], line, col)));
                return;

            case "Name":
            case "Tag":
                if (!IsTextScalar(value))
                {
                    throw new JgsRuntimeException(line, col,
                        $"Error setting property '{field}' of class 'timer'. Value must be a character vector or a string scalar.");
                }

                SetTimerField(state, field, JgsValue.Str(TextOf(value)));
                if (field == "Name")
                {
                    state.Name = TextOf(value);
                }

                return;

            case "UserData":
                SetTimerField(state, field, retain ? RetainedForEntry(value, sharesOnStore: true) : value);
                return;

            default:
                if (!IsTimerCallback(value))
                {
                    throw new JgsRuntimeException(line, col,
                        $"{field} callback must be set to a string scalar, a function handle, or a 1-by-N cell array. "
                        + "The first element of the 1-by-N cell array must be the callback function name or handle.");
                }

                SetTimerField(state, field, retain ? RetainedForEntry(value, sharesOnStore: true) : value);
                return;
        }
    }

    private static double TimerNumber(string field, JgsValue value, Func<double, bool> accepts, string must, int line, int col)
    {
        if (value.Type is not (JgsType.Number or JgsType.Bool) || !accepts(value.AsNumber) || double.IsNaN(value.AsNumber))
        {
            throw new JgsRuntimeException(line, col,
                $"Error setting property '{field}' of class 'timer'. Value must be {must}.");
        }

        return value.AsNumber;
    }

    private static string TimerWord(string field, JgsValue value, string[] words, int line, int col)
    {
        if (IsTextScalar(value))
        {
            string given = TextOf(value);
            foreach (string word in words)
            {
                if (word.Equals(given, StringComparison.OrdinalIgnoreCase))
                {
                    return word;
                }
            }
        }

        string shown = IsTextScalar(value) ? $"'{TextOf(value)}'" : $"a {value.TypeName}";
        string choices = words.Length == 2
            ? $"'{words[0]}' or '{words[1]}'"
            : string.Join(", ", words[..^1].Select(static w => $"'{w}'")) + $", or '{words[^1]}'";
        throw new JgsRuntimeException(line, col,
            $"Error setting property '{field}' of class 'timer'. {shown} is invalid. Value must be {choices}.");
    }

    /// <summary>A function handle, a piece of text to evaluate, a cell whose first element is one
    /// of those (the rest are extra arguments), or an empty to clear the slot.</summary>
    private static bool IsTimerCallback(JgsValue value)
    {
        if (value.Type == JgsType.Function || IsTextScalar(value))
        {
            return true;
        }

        if (value.Type == JgsType.Array && value.ArrayLength == 0 && !value.IsStringArray)
        {
            return true;
        }

        if (value.Type == JgsType.Cell && value.Rows == 1)
        {
            JgsValue[] parts = value.AsCell;
            return parts.Length > 0 && (parts[0].Type == JgsType.Function || IsTextScalar(parts[0]));
        }

        return false;
    }

    private static bool HasTimerCallback(JgsTimerState state, string which) =>
        state.Fields.TryGetValue(which, out JgsValue? callback)
        && !(callback.Type == JgsType.Array && callback.ArrayLength == 0);

    // --- the verbs ------------------------------------------------------------------------------

    private static void StartTimer(JgsTimerState state, JgsTimerScheduler scheduler, int line, int col)
    {
        if (state.Running)
        {
            throw new JgsRuntimeException(line, col, "Cannot start timer because it is already running.");
        }

        if (!HasTimerCallback(state, "TimerFcn"))
        {
            throw new JgsRuntimeException(line, col, "Cannot start timer without specifying a 'TimerFcn' callback.");
        }

        state.Running = true;
        state.Executed = 0;
        state.FirstFire = null;
        state.LastFire = null;
        SetTimerField(state, "Running", JgsValue.Str("on"));
        SetTimerField(state, "TasksExecuted", JgsValue.Number(0));
        SetTimerField(state, "AveragePeriod", JgsValue.Number(double.NaN));
        SetTimerField(state, "InstantPeriod", JgsValue.Number(double.NaN));
        scheduler.Arm(state, Environment.TickCount64 + (long)Math.Round(state.StartDelay * 1000));

        // R2025b: the StartFcn runs inside start, and so does a TimerFcn that is already due.
        RunTimerCallback(state, "StartFcn", error: null);
        scheduler.Drain();
    }

    /// <summary>
    /// Ends a run of the timer. The <c>StopFcn</c> runs when asked for: <c>stop(t)</c> runs it whether
    /// or not the timer was running (R2025b does), the last task and a failing task run it, and
    /// <c>delete</c> runs it only for a timer it had to stop.
    /// </summary>
    private static void StopTimer(JgsTimerState state, bool runStopFcn, int line, int col)
    {
        _ = line;
        _ = col;
        if (state.Running)
        {
            state.Running = false;
            SetTimerField(state, "Running", JgsValue.Str("off"));
            state.Scheduler.Disarm(state);
        }

        if (runStopFcn)
        {
            RunTimerCallback(state, "StopFcn", error: null);
        }
    }

    private static void WaitTimer(JgsTimerState state, JgsTimerScheduler scheduler, int line, int col)
    {
        if (!state.Running)
        {
            return;
        }

        if (state.Mode != "singleShot" && double.IsPositiveInfinity(state.TasksToExecute))
        {
            throw new JgsRuntimeException(line, col, "Can't wait with a timer that has an infinite TasksToExecute.");
        }

        long deadline = Environment.TickCount64 + (long)WaitLimit.TotalMilliseconds;
        while (state.Running && !state.Deleted)
        {
            if (Environment.TickCount64 >= deadline)
            {
                throw new JgsRuntimeException(line, col,
                    $"wait: timer '{state.Name}' did not stop within an hour, so the wait was given up rather than held for the rest of the session.");
            }

            PumpWait(PumpSlice, CancellationToken.None, scheduler);
        }
    }

    /// <summary>
    /// <c>delete(t)</c>: a running timer is stopped first, with MATLAB's warning, and then the timer
    /// is ended for every alias — <c>isvalid</c> answers false and every dot and verb refuses. A
    /// second delete is nothing. Answers false for anything that is not a timer.
    /// </summary>
    internal static bool TryDeleteTimer(JGraphScriptGlobals host, JgsValue value, int line, int col)
    {
        if (!IsTimer(value))
        {
            return false;
        }

        JgsTimerState state = TimerStateOf(value, line, col);
        if (state.Deleted)
        {
            return true;
        }

        if (state.Running)
        {
            const string warning = "You are deleting one or more running timer objects. MATLAB has automatically stopped them before deletion.";
            host.Warnings.Record("MATLAB:timer:deleterunning", warning);
            if (host.Warnings.IsOn("MATLAB:timer:deleterunning"))
            {
                host.WriteErr("Warning: " + warning);
            }

            StopTimer(state, runStopFcn: true, line, col);
        }

        state.Deleted = true;
        state.Scheduler.Disarm(state);
        return true;
    }

    // --- firing ---------------------------------------------------------------------------------

    /// <summary>
    /// One due <c>TimerFcn</c>, from the scheduler's drain: the count and the period readings move,
    /// the callback runs, and the timer is re-armed for its next period or stopped after its last
    /// task. A failing <c>TimerFcn</c> is reported, its <c>ErrorFcn</c> runs, and the timer stops
    /// (R2025b: one task executed, <c>Running</c> off).
    /// </summary>
    internal static void FireTimer(JgsTimerState state)
    {
        long now = Environment.TickCount64;
        state.Executed++;
        SetTimerField(state, "TasksExecuted", JgsValue.Number(state.Executed));
        if (state.LastFire is long last && state.FirstFire is long first)
        {
            SetTimerField(state, "InstantPeriod", JgsValue.Number((now - last) / 1000.0));
            SetTimerField(state, "AveragePeriod", JgsValue.Number((now - first) / 1000.0 / (state.Executed - 1)));
        }
        else
        {
            state.FirstFire = now;
        }

        state.LastFire = now;

        if (!RunTimerCallback(state, "TimerFcn", error: null))
        {
            if (!state.Deleted && state.Running)
            {
                StopTimer(state, runStopFcn: true, 0, 0);
            }

            return;
        }

        // The callback may have stopped or deleted its own timer.
        if (state.Deleted || !state.Running)
        {
            return;
        }

        bool more = state.Mode != "singleShot" && state.Executed < state.TasksToExecute;
        if (!more)
        {
            StopTimer(state, runStopFcn: true, 0, 0);
            return;
        }

        // fixedRate counts from when the task was due, the other two from when it finished; a
        // period already missed (a busy loop ran long) fires at the next drain point rather than
        // catching up — MATLAB's 'drop' busy mode.
        long period = (long)Math.Round(state.Period * 1000);
        long next = state.Mode == "fixedRate" ? state.Due + period : Environment.TickCount64 + period;
        state.Scheduler.Arm(state, Math.Max(next, Environment.TickCount64));
    }

    /// <summary>
    /// Runs one of the timer's callbacks with MATLAB's two arguments — the timer and an event whose
    /// <c>Type</c> names the callback and whose <c>Data.time</c> is a clock vector — reporting a
    /// failure the way a failed statement is and answering false for it. Stop is the exception:
    /// cancellation always unwinds.
    /// </summary>
    private static bool RunTimerCallback(JgsTimerState state, string which, (string Message, string Identifier)? error)
    {
        if (!HasTimerCallback(state, which))
        {
            return true;
        }

        JgsValue callback = state.Fields[which];
        JgsValue eventData = TimerEvent(which, error);
        state.CallbackDepth++;
        try
        {
            InvokeTimerCallback(state, callback, eventData);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JgsException ex)
        {
            ReportTimerCallbackFailure(state, which, ex.Message, (ex as JgsRuntimeException)?.Identifier ?? string.Empty);
            return false;
        }
        catch (Exception ex) when (ScriptExitException.Unwrap(ex) is null)
        {
            ReportTimerCallbackFailure(state, which, ex.Message, string.Empty);
            return false;
        }
        finally
        {
            state.CallbackDepth--;
        }
    }

    /// <summary>MATLAB's report, then the <c>ErrorFcn</c> for a task that failed (the documented
    /// contract: <c>event.Data.message</c> and <c>event.Data.messageID</c>).</summary>
    private static void ReportTimerCallbackFailure(JgsTimerState state, string which, string message, string identifier)
    {
        state.Scheduler.Host.WriteErr($"Error while evaluating {which} for timer '{state.Name}'\n\n{message}");
        if (which == "TimerFcn")
        {
            RunTimerCallback(state, "ErrorFcn", (message, identifier));
        }
    }

    private static void InvokeTimerCallback(JgsTimerState state, JgsValue callback, JgsValue eventData)
    {
        Interpreter interpreter = state.Scheduler.Interpreter;
        if (callback.Type == JgsType.Function)
        {
            JgsCallbacks.Invoke(callback.AsCallable, [state.Value, eventData], 0, 0);
            return;
        }

        // A command string runs in the base workspace, as MATLAB's does.
        if (IsTextScalar(callback))
        {
            interpreter.EvaluateSource(TextOf(callback), interpreter.Globals, 0, 0, asStatement: true); // a command, asked for nothing (V9.1)
            return;
        }

        // {@fn, a, b} calls fn(t, event, a, b); a name in the first cell is looked up as a handle.
        JgsValue[] parts = callback.AsCell;
        JgsValue head = parts[0].Type == JgsType.Function
            ? parts[0]
            : interpreter.EvaluateSource("@" + TextOf(parts[0]), interpreter.Globals, 0, 0);
        var arguments = new JgsValue[parts.Length + 1];
        arguments[0] = state.Value;
        arguments[1] = eventData;
        Array.Copy(parts, 1, arguments, 2, parts.Length - 1);
        JgsCallbacks.Invoke(head.AsCallable, arguments, 0, 0);
    }

    /// <summary>The event every timer callback receives: <c>Type</c> and <c>Data</c>, whose
    /// <c>time</c> is a clock vector and which carries the error for an <c>ErrorFcn</c>.</summary>
    private static JgsValue TimerEvent(string which, (string Message, string Identifier)? error)
    {
        DateTime moment = DateTime.Now;
        var data = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        if (error is { } failure)
        {
            data["message"] = JgsValue.Str(failure.Message);
            data["messageID"] = JgsValue.Str(failure.Identifier);
        }

        data["time"] = JgsMatrix.FromColumnMajor(
            [moment.Year, moment.Month, moment.Day, moment.Hour, moment.Minute, moment.Second + (moment.Millisecond / 1000.0)],
            1, 6);

        return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Type"] = JgsValue.Str(which),
            ["Data"] = JgsValue.Struct(data),
        });
    }
}
