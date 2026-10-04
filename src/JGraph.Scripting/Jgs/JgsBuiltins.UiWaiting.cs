using JGraph.Api;
using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The verbs a script blocks in while its interface goes on working (app-building plan, U4):
/// <c>uiwait</c>, <c>uiresume</c> and <c>waitfor</c>, in R2025b's forms and refusals (probes
/// <c>u4_wait</c> and <c>u4_wait2</c>).
/// </summary>
/// <remarks>
/// <para>
/// All three wait in one loop, <see cref="BlockUntil"/>, on the script thread. Between slices it
/// delivers what <c>pause</c> delivers — queued callbacks, due timers, .NET's and the devices'
/// events — so the click, the timer or the deletion that ends a wait can happen during it.
/// </para>
/// <para>
/// Without a window R2025b's <c>uiwait</c> warns and waits all the same, and a wait nothing ends
/// hangs. Here the same warning is given and the same things end the wait; the one difference is
/// that a wait which <em>nothing</em> can end — no window, no armed timer, nothing queued — returns,
/// where R2025b would hold a batch run for ever (ADR 0201).
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    private const string NoDisplayId = "MATLAB:hg:NoDisplayNoFigureSupportSeeReleaseNotes";

    private static readonly string[] WaitStatusWords = ["waiting", "inactive"];

    /// <summary>
    /// Whether a person, or something standing in for one, can answer a window: the host delivers
    /// interface events, or a test has said it will answer blocking dialogs itself.
    /// </summary>
    internal static bool CanInteract =>
        ScriptEventQueue.PumpInstalled || ScriptGraphicsCallbacks.BlockingDialogShown is not null;

    private static void RegisterUiWaitingBuiltins(
        JgsEnvironment env, JGraphScriptGlobals host, CancellationToken cancellationToken)
    {
        void DefineQuiet(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body)
            {
                BindsAnsAsStatement = false,
                AutoCallsBare = true,
            }));

        DefineQuiet("uiwait", (args, line, col) => UiWait(host, cancellationToken, args, line, col));
        DefineQuiet("uiresume", UiResume);
        env.Builtins.Register("waitfor", JgsValue.Function(new BuiltinFunction("waitfor",
            (args, line, col) => WaitFor(host, cancellationToken, args, line, col))
        {
            BindsAnsAsStatement = false,
        }));
    }

    /// <summary>
    /// Waits on the script thread until <paramref name="done"/> answers true or the deadline passes,
    /// delivering callbacks, timers and device events between slices and waking at once for Stop.
    /// </summary>
    /// <param name="verb">The verb waiting, named in the error for a wait nested too deep.</param>
    /// <param name="host">The run's globals, whose timers fire during the wait.</param>
    /// <param name="fallbackToken">The token to wake on where no dispatcher is installed.</param>
    /// <param name="done">Asked before each slice; true ends the wait.</param>
    /// <param name="deadline">A tick count to give up at, or null to wait for as long as it takes.</param>
    /// <param name="line">The calling statement's line, for an error.</param>
    /// <param name="col">The calling statement's column.</param>
    internal static void BlockUntil(
        string verb, JGraphScriptGlobals host, CancellationToken fallbackToken, Func<bool> done, long? deadline,
        int line, int col)
    {
        JgsCallbackDispatcher? dispatcher = JgsCallbackDispatcher.Current;
        if (dispatcher is { AtDrainLimit: true })
        {
            // Callbacks stop being delivered at this depth, so nothing could end the wait.
            throw new JgsRuntimeException(line, col, "JGraph:waiting:NestedTooDeep",
                $"{verb}: waits are nested {JgsCallbackDispatcher.MaxDrainDepth} callbacks deep, and one more could never be "
                + "ended — a callback is starting the wait it is itself answering.");
        }

        CancellationToken token = dispatcher?.StatementToken ?? fallbackToken;
        using IDisposable? waiting = dispatcher?.Waiting();
        bool announced = false;
        try
        {
            while (!done())
            {
                long remaining = deadline is { } at ? at - Environment.TickCount64 : long.MaxValue;
                if (remaining <= 0)
                {
                    return;
                }

                // The dead wait: with no window, no timer armed and nothing queued for this thread,
                // nothing between here and the end of the run can change what is being waited for.
                // Only what this wait can itself deliver counts: another run's pending work, which
                // the process-wide flags also see, could never end this one.
                if (deadline is null && !CanInteract && host.Timers is not { Armed: true }
                    && ScriptEventQueue.Count == 0 && !Net.NetCallbackQueue.CurrentHasWork)
                {
                    return;
                }

                // A host with a prompt is told, once the wait is really one, so it can say that
                // what is typed meanwhile queues behind it.
                if (!announced && CanInteract)
                {
                    announced = true;
                    ScriptGraphicsCallbacks.WaitingChanged?.Invoke(verb);
                }

                WaitHandle.WaitAny([token.WaitHandle, Devices.DeviceEventQueue.Posted], TimeSpan.FromMilliseconds(
                    System.Math.Min(remaining, PumpSlice.TotalMilliseconds)));
                token.ThrowIfCancellationRequested();
                dispatcher?.Drain();
                host.Timers?.Drain();
                Net.NetCallbackQueue.DrainCurrent();
                Devices.DeviceEventQueue.DrainCurrent();

                // What the callbacks just did is shown while the wait goes on: a figure they made
                // appears, and a component they changed is sent to its window. Nothing is sent
                // when nothing changed.
                host.ShowTouchedFigures();
                ScriptComponentFrames.Flush(force: false);
            }
        }
        finally
        {
            if (announced)
            {
                ScriptGraphicsCallbacks.WaitingChanged?.Invoke(null);
            }
        }
    }

    /// <summary>
    /// A figure's <c>WaitStatus</c> as text: empty until something waits on it, then
    /// <c>'waiting'</c> or <c>'inactive'</c>.
    /// </summary>
    internal static string WaitStatusOf(FigureModel figure) =>
        JgsHandleRegistry.TryGetEntry(figure, out JgsHandleEntry? entry) ? entry.WaitStatus ?? string.Empty : string.Empty;

    /// <summary>The word a script wrote to <c>WaitStatus</c>, which may be abbreviated, or R2025b's refusal.</summary>
    internal static string WaitStatusWord(JgsValue value, int line, int col)
    {
        if (IsTextScalar(value))
        {
            string typed = TextOf(value);
            string[] matches = typed.Length == 0
                ? []
                : [.. WaitStatusWords.Where(word => word.StartsWith(typed, StringComparison.OrdinalIgnoreCase))];
            if (matches.Length == 1)
            {
                return matches[0];
            }
        }

        throw new JgsRuntimeException(line, col, "MATLAB:gbtdatatypes:WrongFormat",
            "Error setting property 'WaitStatus' of class 'Figure':\nThe data is in the wrong format.");
    }

    /// <summary>
    /// <c>uiwait</c>, <c>uiwait(f)</c> and <c>uiwait(f, timeout)</c>: shows the figure and blocks
    /// until <c>uiresume</c> is called on it, it is deleted, or the timeout passes.
    /// </summary>
    private static JgsValue UiWait(
        JGraphScriptGlobals host, CancellationToken cancellationToken, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        // R2025b warns before it looks at its arguments.
        if (!CanInteract)
        {
            Warn(host, NoDisplayId,
                "uiwait is not supported when MATLAB is started with the -nodisplay or -noFigureWindows option or there is no display.");
        }

        FigureModel figure;
        if (args.Count == 0)
        {
            figure = JG.CurrentFigure;
        }
        else if (args[0].Type == JgsType.Number && JgsHandleRegistry.TryGet(args[0], out JgsHandleEntry? named)
                 && named.Target is FigureModel given)
        {
            figure = given;
        }
        else
        {
            throw new JgsRuntimeException(line, col, "MATLAB:uiwait:InvalidInputType", "Input argument must be of type figure");
        }

        long? deadline = null;
        if (args.Count == 2)
        {
            string kind = ClassOf(args[1], JgsDialect.Matlab);
            if (kind == "logical" || JgsNumericClasses.Parse(kind) is null)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:uiwait:InvalidSecondInputType", "Second input must be numeric");
            }

            // The timeout is a timer's StartDelay in R2025b, and what a timer refuses is refused here.
            double[] seconds = ToDoubles("uiwait", args[1], line, col);
            if (seconds.Length != 1)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:validation:IncompatibleSize",
                    "Error setting property 'StartDelay' of class 'timer'. Value must be a scalar.");
            }

            double timeout = seconds[0];
            if (timeout < 1)
            {
                timeout = 1;
                Warn(host, "MATLAB:uiwait:InvalidSecondInputValue",
                    "Timeout value cannot be less than one second thus changing timeout value to one second.");
            }

            if (double.IsNaN(timeout))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeNonnegative",
                    "Error setting property 'StartDelay' of class 'timer'. Value must be nonnegative.");
            }

            if (!double.IsPositiveInfinity(timeout))
            {
                deadline = Environment.TickCount64 + (long)System.Math.Min(timeout * 1000, int.MaxValue);
            }
        }

        JgsHandleEntry entry = JgsHandleRegistry.EntryFor(figure);
        figure.Visible = true;
        JG.TouchFigure(figure);
        entry.WaitStatus = "waiting";

        // The window is up, and what the script drew in it is on screen, before the wait begins.
        host.ShowTouchedFigures();
        ScriptComponentFrames.Flush(force: true);
        ScriptRenderPump.Flush();

        BlockUntil("uiwait", host, cancellationToken,
            () => figure.BeingDeleted || !JgsHandleRegistry.TryGetEntry(figure, out _) || entry.WaitStatus != "waiting",
            deadline, line, col);

        // A timeout ends the wait the way uiresume does; a deleted figure has no status to write.
        if (!figure.BeingDeleted && JgsHandleRegistry.TryGetEntry(figure, out _))
        {
            entry.WaitStatus = "inactive";
        }

        return JgsValue.Null;
    }

    /// <summary><c>uiresume</c> and <c>uiresume(f)</c>: ends the <c>uiwait</c> on a figure.</summary>
    private static JgsValue UiResume(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count > 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        if (args.Count == 0)
        {
            JgsHandleRegistry.EntryFor(JG.CurrentFigure).WaitStatus = "inactive";
            return JgsValue.Null;
        }

        // R2025b reads Type off whatever it was given: a dead handle fails there, one object that
        // is not a figure is refused, and of several only the figures are written.
        int count = args[0].Type == JgsType.Array ? args[0].ArrayLength : 1;
        var entries = new List<JgsHandleEntry>();
        for (int i = 0; i < count; i++)
        {
            JgsValue element = args[0].Type == JgsType.Array ? args[0].ElementAt(i) : args[0];
            if (element.Type != JgsType.Number || !JgsHandleRegistry.TryGet(element, out JgsHandleEntry? entry))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:class:InvalidHandle", "Invalid or deleted object.");
            }

            entries.Add(entry);
        }

        if (entries.Count == 1 && entries[0].Target is not FigureModel)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:uiresume:InvalidInputType", "Argument must be a Figure object.");
        }

        foreach (JgsHandleEntry entry in entries.Where(static e => e.Target is FigureModel))
        {
            entry.WaitStatus = "inactive";
        }

        return JgsValue.Null;
    }

    /// <summary>
    /// <c>waitfor(h)</c>, <c>waitfor(h, 'Prop')</c>, <c>waitfor(h, 'Prop', value)</c>: blocks until
    /// the object is deleted, until a named property changes, or until it takes a given value —
    /// at once if it already has it. What is not a live object is not waited for.
    /// </summary>
    private static JgsValue WaitFor(
        JGraphScriptGlobals host, CancellationToken cancellationToken, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
        }

        if (args.Count > 3)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:maxrhs", "Too many input arguments.");
        }

        if (args[0].Type != JgsType.Number || !JgsHandleRegistry.TryGetOrRoot(args[0], out JgsHandleEntry? entry))
        {
            return JgsValue.Null;
        }

        string? property = null;
        if (args.Count > 1)
        {
            if (!IsTextScalar(args[1]) || !JgsGraphicsProperties.TryFind(entry.Target, TextOf(args[1]), out GraphicsProperty found))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:waitfor:BadProperty", "Invalid property.");
            }

            property = found.Name;
        }

        // waitfor(h, prop) returns on any change from the value it saw at entry; the three-argument
        // form returns on equality with the value asked for.
        JgsValue? watched = property is null || args.Count == 3 ? null : JgsGraphicsProperties.Get(entry, property, line, col);
        GraphObject target = entry.Target;
        bool Done()
        {
            if (target.BeingDeleted || !JgsHandleRegistry.TryGetEntry(target, out JgsHandleEntry? alive)
                || !ReferenceEquals(alive, entry))
            {
                return true;
            }

            if (property is null)
            {
                return false;
            }

            JgsValue current = JgsGraphicsProperties.Get(entry, property, line, col);
            return args.Count == 3
                ? WaitedValueReached(current, args[2])
                : !JgsStdlib.DeepEquals(current, watched!, nanEqual: true);
        }

        if (!Done())
        {
            // What the script drew so far is on screen while it waits for somebody to act on it.
            host.ShowTouchedFigures();
            ScriptComponentFrames.Flush(force: true);
            ScriptRenderPump.Flush();
            BlockUntil("waitfor", host, cancellationToken, Done, deadline: null, line, col);
        }

        return JgsValue.Null;
    }

    /// <summary>
    /// Whether a property has reached the value a <c>waitfor</c> names. Numbers compare as numbers
    /// whatever their class — a <c>Value</c> of 0 is reached by <c>false</c> — and text as text.
    /// </summary>
    private static bool WaitedValueReached(JgsValue current, JgsValue wanted)
    {
        if (JgsStdlib.DeepEquals(current, wanted, nanEqual: true))
        {
            return true;
        }

        bool Numeric(JgsValue value) => value.Type is JgsType.Number or JgsType.Bool
            || (value.Type == JgsType.Array && !value.IsStringArray);
        if (Numeric(current) && Numeric(wanted))
        {
            double[] a = ToDoubles("waitfor", current, 0, 0);
            double[] b = ToDoubles("waitfor", wanted, 0, 0);
            return a.Length == b.Length && a.AsSpan().SequenceEqual(b);
        }

        return IsTextScalar(current) && IsTextScalar(wanted) && TextOf(current) == TextOf(wanted);
    }
}
