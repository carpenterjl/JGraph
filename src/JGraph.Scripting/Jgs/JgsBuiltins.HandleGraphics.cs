using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Serialization;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Where a script is running a graphics callback, and what it was aimed at. MATLAB answers
/// <c>gcbo</c>, <c>gcbf</c> and <c>gco</c> from state like this; nothing else needs it, so it lives
/// here rather than on the registry.
/// <para>
/// Outside a callback the two callback questions answer nothing, which is exactly what MATLAB does
/// in a script — they are only ever meaningful while a click is being serviced. <c>gco</c> is the
/// last object a user clicked and outlives the callback, the way a figure's CurrentObject does.
/// </para>
/// </summary>
internal static class JgsGraphicsCallbackState
{
    /// <summary>The object whose callback is running, if one is.</summary>
    public static GraphObject? CallbackObject { get; private set; }

    /// <summary>The object the user last clicked.</summary>
    public static GraphObject? CurrentObject { get; private set; }

    /// <summary>Marks a callback as running against <paramref name="source"/> for the returned scope.
    /// A callback interrupted at a drain point can be interrupted by another, so the scope restores
    /// the caller it displaced rather than clearing — gcbo inside nested callbacks answers the
    /// innermost one, and the outer one again when the inner returns.</summary>
    /// <param name="source">The object whose callback is about to run.</param>
    /// <param name="clicked">The object the user hit, when the event was a click; null leaves the
    /// click history alone (a resize or a close request is not a click).</param>
    public static IDisposable Enter(GraphObject source, GraphObject? clicked)
    {
        GraphObject? displaced = CallbackObject;
        CallbackObject = source;
        if (clicked is not null)
        {
            CurrentObject = clicked;
        }

        return new Scope(displaced);
    }

    /// <summary>
    /// Records what the user just clicked — MATLAB updates the current object on every click,
    /// callbacks or no callbacks, and clicking the figure background clears it. Written from the
    /// window's thread; a plain reference write, racing nothing that matters.
    /// </summary>
    public static void RecordClick(GraphObject? clicked) => CurrentObject = clicked;

    /// <summary>Forgets everything — a cleared workspace has no click history.</summary>
    public static void Clear()
    {
        CallbackObject = null;
        CurrentObject = null;
    }

    private sealed class Scope(GraphObject? displaced) : IDisposable
    {
        // Only the callback question is unwound: the clicked object stays, because that is the
        // whole point of gco — it answers after the click, not only during it.
        public void Dispose() => CallbackObject = displaced;
    }
}

/// <summary>
/// The handle-graphics verbs: the ones that treat a figure object as a thing to be interrogated,
/// searched for, and copied, rather than a thing to draw into.
/// <para>
/// Every one of them reads the same property table the dot does
/// (<see cref="JgsGraphicsProperties"/>), so <c>get(h, 'Color')</c> and <c>h.Color</c> cannot
/// disagree, and a chart type added in a later milestone becomes gettable, settable and findable
/// without anything here being touched.
/// </para>
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>How long a pumping wait sleeps between looking around — short enough that a click
    /// feels answered, long enough that an idle wait costs nothing.</summary>
    private static readonly TimeSpan PumpSlice = TimeSpan.FromMilliseconds(25);

    /// <summary>Delivers queued graphics events, when a session is running one. This is what makes
    /// a builtin a drain point; where no session exists (a one-shot or batch run) it is a no-op.</summary>
    internal static void PumpEvents(JGraphScriptGlobals host)
    {
        JgsComponentContainers.RunUpdates(host); // custom components' owed updates first (U10), session or not
        JgsCallbackDispatcher.Current?.Drain();
        host.Timers?.Drain(); // a due timer fires here too (V6, #105)
        Net.NetCallbackQueue.DrainCurrent(); // and .NET's work from other threads (ADR 0178)
        Devices.DeviceEventQueue.DrainCurrent(); // and a serialport's BytesAvailableFcn (device classes plan)
    }

    /// <summary>
    /// Waits out <paramref name="duration"/> in slices, delivering queued graphics events between
    /// them — <c>pause</c> is one of MATLAB's interruption points, and a click during a pause is
    /// answered during the pause, not after it. Wakes early only for cancellation, which throws.
    /// </summary>
    /// <param name="duration">How long to wait in total.</param>
    /// <param name="fallbackToken">The token to wake on when no session dispatcher is installed —
    /// a one-shot run's own token, captured when its globals were built.</param>
    /// <param name="timers">The run's timers, fired between slices as well (V6, #105); null where
    /// the caller has no run to reach them through.</param>
    internal static void PumpWait(TimeSpan duration, CancellationToken fallbackToken, JgsTimerScheduler? timers = null)
    {
        JgsCallbackDispatcher? dispatcher = JgsCallbackDispatcher.Current;
        CancellationToken token = dispatcher?.StatementToken ?? fallbackToken;

        long deadline = Environment.TickCount64 + (long)duration.TotalMilliseconds;
        while (true)
        {
            long remaining = deadline - Environment.TickCount64;
            if (remaining <= 0)
            {
                return;
            }

            // A device event wakes the wait at once (device classes plan); the rest wait out the slice.
            WaitHandle.WaitAny([token.WaitHandle, Devices.DeviceEventQueue.Posted], TimeSpan.FromMilliseconds(
                System.Math.Min(remaining, PumpSlice.TotalMilliseconds)));
            token.ThrowIfCancellationRequested();
            JgsComponentContainers.RunUpdates(); // U10
            dispatcher?.Drain();
            timers?.Drain();
            Net.NetCallbackQueue.DrainCurrent(); // .NET's events and delegates from other threads (ADR 0178)
            Devices.DeviceEventQueue.DrainCurrent(); // device callbacks (device classes plan)
        }
    }

    private static void RegisterHandleGraphicsBuiltins(JgsEnvironment env, JGraphScriptGlobals host)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body)));

        // A verb that acts rather than answers prints nothing as a bare statement, even though it
        // hands something back for the script that wants it.
        void DefineSilent(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(
                new BuiltinFunction(name, body) { BindsAnsAsStatement = false }));

        Define("get", (args, line, col) => TryDeviceBuiltin("get", args, 1, line, col, out JgsValue device) ? device
            : TryLibBuiltin("get", host, args, line, col, out JgsValue got) ? got : Get(args, line, col));
        // set sees a string scalar as the string it is only where a component's property tells the
        // two apart (U5): everywhere else it is handed the char row, as every builtin is.
        env.Builtins.Register("set", JgsValue.Function(new BuiltinFunction("set", (given, line, col) =>
        {
            IReadOnlyList<JgsValue> args = StringsOnlyWhereTold(given);
            return TryDeviceBuiltin("set", args, 0, line, col, out JgsValue device) ? device
                : TryLibBuiltin("set", host, args, line, col, out JgsValue none) ? none : Set(args, line, col);
        })
        {
            BindsAnsAsStatement = false,
            KeepsStringArguments = true,
        }));

        // Both answer a question with no arguments — every object there is — so the bare name has to
        // be that answer rather than the function itself, or numel(findobj) counts a function.
        void DefineSearch(string name, bool hidden) =>
            env.Builtins.Register(name, JgsValue.Function(
                new BuiltinFunction(name, (args, line, col) => Find(name, args, line, col, hidden))
                { AutoCallsBare = true }));

        DefineSearch("findobj", hidden: false);
        DefineSearch("findall", hidden: true);

        // ishandle and ishghandle ask the same question of the same registry. MATLAB separates them
        // because its ishandle also accepted the old Java-backed handles; there is only one kind here.
        Define("ishandle", (args, line, col) => IsHandle("ishandle", args, line, col));
        Define("ishghandle", (args, line, col) => IsHandle("ishghandle", args, line, col));
        Define("isgraphics", IsGraphics);
        Define("isvalid", (args, line, col) => TryDeviceBuiltin("isvalid", args, 1, line, col, out JgsValue device) ? device
            : TryLibBuiltin("isvalid", host, args, line, col, out JgsValue valid) ? valid : IsValid(args, line, col));

        Define("ancestor", (args, line, col) => Ancestor(JgsComponentContainers.AsHandles(args), line, col)); // a custom component is its area (U10)
        DefineSilent("copyobj", Copy);
        Define("gobjects", Gobjects);

        // The three "which object" questions. gco outlives its click; the other two are only true
        // while a callback is running, and answer empty otherwise.
        env.Builtins.Register("gco", JgsValue.Function(new BuiltinFunction("gco", (args, line, col) =>
        {
            ArityRange("gco", args, 0, 1, line, col);
            return Named(JgsGraphicsCallbackState.CurrentObject);
        })
        { AutoCallsBare = true, BindsAnsAsStatement = false }));

        env.Builtins.Register("gcbo", JgsValue.Function(new BuiltinFunction("gcbo", (args, line, col) =>
        {
            Arity("gcbo", args, 0, line, col);
            return Named(JgsGraphicsCallbackState.CallbackObject);
        })
        { AutoCallsBare = true, BindsAnsAsStatement = false }));

        env.Builtins.Register("gcbf", JgsValue.Function(new BuiltinFunction("gcbf", (args, line, col) =>
        {
            Arity("gcbf", args, 0, line, col);
            return Named(FigureOf(JgsGraphicsCallbackState.CallbackObject));
        })
        { AutoCallsBare = true, BindsAnsAsStatement = false }));

        DefineSilent("cla", Cla);

        env.Builtins.Register("ishold", JgsValue.Function(new BuiltinFunction("ishold", (args, line, col) =>
        {
            ArityRange("ishold", args, 0, 1, line, col);
            (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
            Arity("ishold", rest, 0, line, col);
            return JgsValue.Bool((named ?? JG.Gca()).Hold);
        })
        { AutoCallsBare = true }));

        env.Builtins.Register("newplot", JgsValue.Function(new BuiltinFunction("newplot", (args, line, col) =>
        {
            ArityRange("newplot", args, 0, 1, line, col);
            (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
            Arity("newplot", rest, 0, line, col);

            // MATLAB's newplot readies an axes for the next drawing verb by honouring NextPlot: with
            // hold off the axes is emptied first, with hold on it is left alone.
            AxesModel axes = named ?? JG.Gca();
            if (!axes.Hold)
            {
                ClearAxes(axes, reset: false);
            }

            return JgsHandleRegistry.For(axes);
        })
        { AutoCallsBare = true, BindsAnsAsStatement = false }));

        DefineSilent("shg", (args, line, col) =>
        {
            Arity("shg", args, 0, line, col);
            host.show();
            return JgsValue.Null;
        });
    }

    // --- get ----------------------------------------------------------------------------------

    /// <summary>
    /// <c>get(h)</c> lists everything, <c>get(h, 'Name')</c> reads one, <c>get(h, {'A','B'})</c>
    /// reads several. Asking a vector of handles answers one entry per handle, as MATLAB does.
    /// </summary>
    private static JgsValue Get(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("get", args, 1, 2, line, col);

        // A custom component is a graphics object (U10); any other object whose class did not
        // inherit get from matlab.mixin.SetGet has none (U6, measured).
        if (args[0].Type == JgsType.Object && JgsComponentContainers.TryState(args[0].AsObject, out JgsComponentContainerState? component))
        {
            return JgsComponentContainers.Get(component, args, line, col);
        }

        if (args[0].Type == JgsType.Object)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:graphics:GetMethodUnknown",
                $"Cannot find 'get' method for {args[0].AsObject.Class.Name} class.");
        }
        List<JgsHandleEntry> targets = HandleList("get", args[0], line, col);

        if (args.Count == 1)
        {
            JgsValue[] listings = targets.Select(AllProperties).ToArray();
            return listings.Length == 1 ? listings[0] : JgsValue.Cell(listings);
        }

        if (args[1].Type == JgsType.Cell)
        {
            // A cell of names asks for a row of answers, one per name, per handle.
            string[] names = CellOfNames("get", args[1], line, col);
            var values = new JgsValue[targets.Count * names.Length];
            for (int c=0;c<names.Length;c++) for (int row=0;row<targets.Count;row++)
                values[c*targets.Count+row] = JgsGraphicsProperties.Get(targets[row],names[c],line,col);
            var result = JgsValue.Cell(values); result.Reshape(targets.Count,names.Length); return result;
        }

        string one = StrOf("get", args[1], line, col);
        JgsValue[] answers = targets
            .Select(entry => JgsGraphicsProperties.Get(entry, one, line, col))
            .ToArray();
        if (answers.Length == 1) return answers[0];
        var column = JgsValue.Cell(answers); column.Reshape(answers.Length,1); return column;
    }

    /// <summary>Every property an object answers to, as a struct, in the order <c>get</c> lists them.</summary>
    private static JgsValue AllProperties(JgsHandleEntry entry)
    {
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        foreach (string name in JgsGraphicsProperties.NamesOf(entry.Target))
        {
            fields[name] = JgsGraphicsProperties.Get(entry, name, 0, 0);
        }

        return JgsValue.Struct(fields);
    }

    // --- set ----------------------------------------------------------------------------------

    /// <summary>
    /// <c>set(h, 'Name', value, …)</c> writes pairs to every handle given, so
    /// <c>set(findobj('Type','line'), 'LineWidth', 2)</c> is one call. <c>set(h)</c> with nothing to
    /// write answers the names that can be written.
    /// </summary>
    private static JgsValue Set(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col,
                "set wants a handle: set(h, 'Name', value) writes a property, set(h) lists the writable ones.");
        }

        // A custom component is a graphics object (U10); any other object whose class did not
        // inherit set from matlab.mixin.SetGet has none (U6; a uistyle, U9).
        if (args[0].Type == JgsType.Object && JgsComponentContainers.TryState(args[0].AsObject, out JgsComponentContainerState? component))
        {
            return JgsComponentContainers.Set(component, args, line, col);
        }

        if (args[0].Type == JgsType.Object)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:graphics:SetMethodUnknown",
                $"Cannot find 'set' method for {args[0].AsObject.Class.Name} class.");
        }

        List<JgsHandleEntry> targets = HandleList("set", args[0], line, col);

        // A component answers as R2025b's does (U3): set(h) is a struct of the names that can be
        // written, each with the words it takes, and set(h, name) is one name's words.
        if (targets.Count == 1 && JgsGraphicsProperties.SpeaksAsComponent(targets[0].Target))
        {
            if (args.Count == 1)
            {
                return JgsGraphicsProperties.OptionsOf(targets[0]);
            }

            if (args.Count == 2 && IsTextScalar(args[1]))
            {
                return JgsGraphicsProperties.OptionsOf(targets[0], TextOf(args[1]), line, col);
            }
        }

        if (args.Count == 1)
        {
            return JgsValue.Cell(Writable(targets[0]).Select(JgsValue.Str).ToArray());
        }

        // set(h, {'A','B'}, {1, 2}) — the cell form, which is how a script written as a table of
        // properties applies them without unrolling itself into a chain of pairs.
        if (args[1].Type == JgsType.Cell)
        {
            Arity("set", args, 3, line, col);
            string[] names = CellOfNames("set", args[1], line, col);
            if (args[2].Type != JgsType.Cell || args[2].Rows != targets.Count || args[2].Cols != names.Length)
                throw new JgsRuntimeException(line,col,$"set: values must be a {targets.Count}-by-{names.Length} cell array.");
            for (int c=0;c<names.Length;c++) for (int row=0;row<targets.Count;row++)
                JgsGraphicsProperties.Set(targets[row],names[c],args[2].ElementAt(c*targets.Count+row),line,col);

            return JgsValue.Null;
        }

        if (args.Count == 2 && args[1].Type == JgsType.Struct)
        {
            foreach (var pair in args[1].AsStruct) foreach (var target in targets)
                JgsGraphicsProperties.Set(target,pair.Key,pair.Value,line,col);
            return JgsValue.Null;
        }
        if ((args.Count - 1) % 2 != 0)
        {
            throw new JgsRuntimeException(line, col,
                $"set: every property needs a value, but '{StrOf("set", args[^1], line, col)}' has none.");
        }

        // set(h, 'XData', x, 'YData', y) writes a series' two coordinates together, which is the
        // one way to change how many points it has: either alone would leave the pair uneven.
        int xAt = 0;
        int yAt = 0;
        for (int i = 1; i < args.Count; i += 2)
        {
            string name = StrOf("set", args[i], line, col);
            xAt = name.Equals("XData", StringComparison.OrdinalIgnoreCase) ? i : xAt;
            yAt = name.Equals("YData", StringComparison.OrdinalIgnoreCase) ? i : yAt;
        }

        bool paired = xAt > 0 && yAt > 0;
        for (int i = 1; i < args.Count; i += 2)
        {
            string name = StrOf("set", args[i], line, col);
            foreach (JgsHandleEntry entry in targets)
            {
                if (paired && (i == xAt || i == yAt) && entry.Target is XYPlot series)
                {
                    if (i == System.Math.Max(xAt, yAt))
                    {
                        double[] xs = ToDoubles("XData", args[xAt + 1], line, col);
                        double[] ys = ToDoubles("YData", args[yAt + 1], line, col);
                        if (xs.Length != ys.Length)
                        {
                            throw new JgsRuntimeException(line, col,
                                $"set: XData has {xs.Length} values and YData has {ys.Length}. A series is a pair.");
                        }

                        series.SetData(xs, ys);
                        series.XImplied = false;
                    }

                    continue;
                }

                JgsGraphicsProperties.Set(entry, name, args[i + 1], line, col);
            }
        }

        return JgsValue.Null;
    }

    /// <summary>The names of the properties an object will let a script write.</summary>
    private static IEnumerable<string> Writable(JgsHandleEntry entry) =>
        JgsGraphicsProperties.NamesOf(entry.Target)
            .Where(name => JgsGraphicsProperties.TryFind(entry.Target, name, out GraphicsProperty property)
                && property.Write is not null);

    // --- findobj / findall --------------------------------------------------------------------

    /// <summary>
    /// Searches a figure — or every figure — for objects whose properties match. <c>findall</c> is
    /// the same search over objects that asked to stay out of it, which is the only difference
    /// MATLAB draws between the two.
    /// </summary>
    private static JgsValue Find(string verb, IReadOnlyList<JgsValue> args, int line, int col, bool hidden)
    {
        int first = 0;
        var roots = new List<GraphObject>();

        // A leading handle (or vector of them) names where to look; without one, every figure is
        // searched, which is what makes findobj('Type', 'line') a whole-session question.
        if (args.Count > 0 && args[0].Type != JgsType.String)
        {
            roots.AddRange(HandleList(verb, args[0], line, col).Select(static e => e.Target));
            first = 1;
        }
        else
        {
            // With no handle named the search starts at the root, which is itself the first thing
            // searched — and from there a figure whose handle is hidden is passed over (U2).
            roots.Add(JgsGraphicsRoot.Instance);
        }

        int depth = int.MaxValue;
        var properties = new List<string>();
        var wanted = new List<(string Name, JgsValue Value)>();
        for (int i = first; i < args.Count; i++)
        {
            string word = StrOf(verb, args[i], line, col);
            if (word.Equals("-property", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Count)
                {
                    throw new JgsRuntimeException(line, col, $"{verb}: '-property' needs a property name.");
                }

                properties.Add(StrOf($"{verb}: -property", args[++i], line, col));
                continue;
            }

            if (word.Equals("flat", StringComparison.OrdinalIgnoreCase)
                || word.Equals("-depth", StringComparison.OrdinalIgnoreCase))
            {
                if (word[0] == 'f')
                {
                    depth = 0;
                    continue;
                }

                if (i + 1 >= args.Count)
                {
                    throw new JgsRuntimeException(line, col, $"{verb}: '-depth' needs a number of levels.");
                }

                depth = (int)NumOf($"{verb}: -depth", args[++i], line, col);
                continue;
            }

            if (word.Length > 0 && word[0] == '-')
            {
                throw new JgsRuntimeException(line, col,
                    $"{verb} understands 'flat', '-depth' and '-property', but not '{word}'.");
            }

            if (i + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col, $"{verb}: '{word}' needs a value to match.");
            }

            wanted.Add((word, args[++i]));
        }

        // Level by level, as R2025b answers (probe u2_tree): an object's children all come before
        // any of its grandchildren. An object named as a root is searched even when its handle is
        // hidden; a hidden one met on the way down is passed over, with everything under it.
        var found = new List<GraphObject>();
        var seen = new HashSet<GraphObject>();
        var level = new List<GraphObject>(roots);
        for (int at = 0; level.Count > 0; at++)
        {
            var next = new List<GraphObject>();
            foreach (GraphObject target in level)
            {
                if (!seen.Add(target))
                {
                    continue;
                }

                JgsHandleEntry entry = JgsHandleRegistry.EntryFor(target);
                if (at > 0 && !hidden && !entry.HandleVisible)
                {
                    continue;
                }

                if ((hidden || at == 0 || entry.HandleVisible)
                    && properties.All(name => JgsGraphicsProperties.TryFind(target, name, out _)
                        || entry.AddedProperties?.ContainsKey(name) == true) // an app's figure has RunningAppInstance (U7)
                    && wanted.All(pair => Matches(entry, pair.Name, pair.Value)))
                {
                    found.Add(target);
                }

                // What a custom component's setup built is no one's to find (U10, probe u10_more).
                if (at < depth && target is not UiComponentContainerModel)
                {
                    // Front first, the order Children lists them in.
                    next.AddRange((hidden
                        ? JgsGraphicsProperties.DescendantsOf(target)
                        : JgsGraphicsProperties.ChildrenOf(target)).Reverse());
                }
            }

            level = next;
        }

        var handles = new double[found.Count];
        for (int i = 0; i < found.Count; i++)
        {
            handles[i] = JgsHandleRegistry.For(found[i]).AsNumber;
        }

        // A column, which is the shape MATLAB's findobj answers in and what a for-loop over the
        // result expects.
        return JgsMatrix.FromColumnMajor(handles, handles.Length, 1);
    }

    /// <summary>
    /// Whether one object matches one filter. An object that has no such property simply does not
    /// match — a search is not the place to complain that a bar has no LineStyle.
    /// </summary>
    private static bool Matches(JgsHandleEntry entry, string name, JgsValue wanted)
    {
        if (!JgsGraphicsProperties.TryFind(entry.Target, name, out GraphicsProperty property))
        {
            return false;
        }

        JgsValue actual = property.Read(entry);
        if (actual.Type == JgsType.String && wanted.Type == JgsType.String)
        {
            return string.Equals(actual.AsString, wanted.AsString, StringComparison.OrdinalIgnoreCase);
        }

        // Element by element, not by identity: half the properties worth searching on are colours,
        // and a colour is a 1-by-3 row. JgsValue.AreEqual is the '==' of handle comparison, where two
        // arrays are the same only when they are the same array — right for handles, useless here.
        return JgsStdlib.DeepEquals(actual, wanted);
    }

    // --- the predicates -------------------------------------------------------------------------

    private static JgsValue IsHandle(string verb, IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity(verb, args, 1, line, col);
        if (args[0].Type == JgsType.Object && args[0].AsObject.Class.IsComponentContainer)
        {
            return JgsValue.Bool(JgsComponentContainers.TryEntry(args[0], out _)); // a custom component is a graphics object (U10)
        }

        if (args[0].Type is JgsType.Struct or JgsType.Object or JgsType.Cell or JgsType.Function)
        {
            return JgsValue.Bool(false); // a listener, an object, a cell: not a graphics handle (V6, #106)
        }

        return MapToBool(verb, args[0], IsLiveHandle, line, col);
    }

    /// <summary>
    /// <c>isgraphics(h)</c> asks whether a number names a live object; <c>isgraphics(h, 'axes')</c>
    /// asks whether it names one of that kind.
    /// </summary>
    private static JgsValue IsGraphics(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("isgraphics", args, 1, 2, line, col);
        if (args[0].Type == JgsType.Object && args[0].AsObject.Class.IsComponentContainer)
        {
            // A custom component is a graphics object of its own type (U10).
            return JgsValue.Bool(JgsComponentContainers.TryEntry(args[0], out JgsHandleEntry? area)
                && (args.Count == 1 || area.TypeName.Equals(StrOf("isgraphics", args[1], line, col), StringComparison.OrdinalIgnoreCase)));
        }

        if (args.Count == 1)
        {
            return MapToBool("isgraphics", args[0], IsLiveHandle, line, col);
        }

        string type = StrOf("isgraphics", args[1], line, col);
        return MapToBool("isgraphics", args[0],
            handle => JgsHandleRegistry.TryGet(JgsValue.Number(handle), out JgsHandleEntry? entry)
                && entry.TypeName.Equals(type, StringComparison.OrdinalIgnoreCase),
            line, col);
    }

    private static bool IsLiveHandle(double handle) =>
        JgsHandleRegistry.TryGet(JgsValue.Number(handle), out JgsHandleEntry? entry) && !entry.Target.BeingDeleted;

    /// <summary>
    /// <c>isvalid(h)</c> (V6, appendix A #104): whether a graphics handle still names a live object,
    /// or whether a handle object has not been deleted. It is a method of the handle class in
    /// MATLAB, so anything that is neither — a value object, a plain number that is not a handle,
    /// a char — is refused in MATLAB's words rather than answered false: <c>isvalid(5)</c> is a
    /// question about nothing, and <c>ishandle</c> is the verb that answers false for it.
    /// </summary>
    private static JgsValue IsValid(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("isvalid", args, 1, line, col);
        JgsValue asked = args[0];

        // A .NET reference object is a handle that stays valid while it is held (ADR 0174).
        if (asked.Type == JgsType.External && asked.AsExternal.IsHandle)
        {
            return JgsValue.Bool(asked.AsExternal is not NetObject { Deleted: true }); // delete(obj) ends it (ADR 0178)
        }

        if (asked.Type == JgsType.Object)
        {
            JgsObject instance = asked.AsObject;
            if (instance.Class.IsHandle)
            {
                return JgsValue.Bool(!instance.Deleted && !instance.Destroying);
            }
        }
        else if (asked.Type is JgsType.Number or JgsType.Bool)
        {
            return MapToBool("isvalid", asked, IsLiveHandle, line, col);
        }
        else if (IsHandleClass(asked))
        {
            // A builtin handle class: a timer is valid until delete(t) (V6, #105) and a listener
            // until delete(lh) (#106); a containers.Map and a VideoWriter have no delete and are
            // valid as long as they are held.
            return JgsValue.Bool(!IsDeletedTimer(asked) && !IsDeletedListener(asked));
        }
        else if (asked.Type == JgsType.Array)
        {
            // A handle array (a deleted object keeps its place in one, and answers false there), or
            // gobjects(0) — an empty logical of the same shape.
            return asked.ArrayLength == 0
                ? EmptyLogical(asked.Rows, asked.Cols)
                : MapToBool("isvalid", asked, IsLiveHandle, line, col);
        }

        throw new JgsRuntimeException(line, col,
            $"Undefined function 'isvalid' for input arguments of type '{ClassOf(asked, JgsDialect.Matlab)}'.");
    }

    /// <summary>
    /// <c>delete(obj)</c> on a handle object with no <c>delete</c> method of its own: the object is
    /// marked deleted, so <c>isvalid</c> answers false through every alias (V6, #104). A value object
    /// has no lifetime to end and is refused. Answers false for anything that is not an object, so
    /// the file command can go on to its own complaint.
    /// </summary>
    internal static bool TryDeleteObject(JgsValue value, int line, int col)
    {
        if (value.Type != JgsType.Object)
        {
            return false;
        }

        JgsObject instance = value.AsObject;
        if (!instance.Class.IsHandle)
        {
            throw new JgsRuntimeException(line, col,
                $"Undefined function 'delete' for input arguments of type '{instance.Class.Name}'.");
        }

        instance.MarkDeleted();
        FireObjectBeingDestroyed(instance); // after the mark: isvalid is already false inside (V6, #106)
        JgsLifetime.ObjectDeleted(instance); // V10: the properties' contents go with the object
        return true;
    }

    // --- ancestor -------------------------------------------------------------------------------

    /// <summary>
    /// The nearest enclosing object of a named kind: <c>ancestor(p, 'axes')</c> is the axes a series
    /// is drawn in. A cell of kinds takes the first that matches, and <c>'toplevel'</c> keeps going
    /// to the outermost one, which for us is always the figure.
    /// </summary>
    private static JgsValue Ancestor(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("ancestor", args, 2, 3, line, col);
        JgsHandleEntry start = JgsHandleRegistry.Require(args[0], line, col);
        string[] kinds = args[1].Type == JgsType.Cell
            ? CellOfNames("ancestor", args[1], line, col)
            : [StrOf("ancestor", args[1], line, col)];

        bool toplevel = args.Count == 3
            && StrOf("ancestor", args[2], line, col).Equals("toplevel", StringComparison.OrdinalIgnoreCase);

        // MATLAB's ancestor starts at the object itself: ancestor(fig, 'figure') is fig.
        GraphObject? best = null;
        for (GraphObject? walk = start.Target; walk is not null; walk = JgsGraphicsProperties.ParentOf(walk))
        {
            string type = JgsGraphicsProperties.TypeNameOf(walk);
            if (!kinds.Any(kind => type.Equals(kind, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            best = walk;
            if (!toplevel)
            {
                break;
            }
        }

        return Named(best);
    }

    // --- copyobj --------------------------------------------------------------------------------

    /// <summary>
    /// Copies an object into another parent. The copy is made by writing the owning figure out in the
    /// document format and reading it back: nothing here knows how to clone a plot, so nothing here
    /// can drift out of step with what a saved figure holds. Whatever survives a save survives a copy.
    /// </summary>
    private static JgsValue Copy(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("copyobj", args, 2, line, col);
        List<JgsHandleEntry> sources = HandleList("copyobj", args[0], line, col);
        JgsHandleEntry destination = JgsHandleRegistry.Require(args[1], line, col);

        var copies = new double[sources.Count];
        foreach (int i in Enumerable.Range(0, sources.Count).OrderBy(i => sources[i].Target is LegendModel ? 1 : 0))
        {
            if (sources[i].Target is LegendModel legend && legend.Parent is AxesModel sourceAxes)
            {
                int index = sources.FindIndex(e => ReferenceEquals(e.Target,sourceAxes));
                if (index >= 0 && copies[index] != 0)
                {
                    var copiedAxes = (AxesModel)JgsHandleRegistry.Require(JgsValue.Number(copies[index]),line,col).Target;
                    copiedAxes.Legend.Visible = legend.Visible;
                    copies[i] = JgsHandleRegistry.For(copiedAxes.Legend).AsNumber; continue;
                }
            }
            copies[i] = JgsHandleRegistry.For(CopyOne(sources[i].Target, destination.Target, line, col)).AsNumber;
        }

        return copies.Length == 1
            ? JgsValue.Number(copies[0])
            : JgsMatrix.FromColumnMajor(copies, copies.Length, 1);
    }

    private static GraphObject CopyOne(GraphObject source, GraphObject parent, int line, int col)
    {
        if (source is FigureModel)
        {
            throw new JgsRuntimeException(line, col,
                "copyobj copies things inside a figure; to copy a whole figure, save it and load it back.");
        }

        FigureModel owner = FigureOf(source)
            ?? throw new JgsRuntimeException(line, col,
                "copyobj: that object does not belong to a figure any more.");

        List<int> path = PathTo(owner, source)
            ?? throw new JgsRuntimeException(line, col,
                $"copyobj cannot copy a {JgsGraphicsProperties.TypeNameOf(source)}; only the things a figure holds — axes, plotted series, annotations, lights and components — are copied.");

        FigureModel clone;
        try
        {
            clone = GraphFormat.Deserialize(GraphFormat.Serialize(owner));
        }
        catch (GraphFormatException ex)
        {
            throw new JgsRuntimeException(line, col, $"copyobj could not copy that object: {ex.Message}");
        }

        GraphObject copy = clone;
        foreach (int step in path)
        {
            copy = CopyableParts(copy)[step];
        }

        if (copy is AxesModel copiedAxes) copiedAxes.Legend.Visible = false;

        // A container brings the axes placed in it, which the document keeps in its figure's list.
        List<AxesModel> held = copy is UiContainerModel container ? [.. clone.Axes.Where(container.Holds)] : [];
        Attach(copy, parent, line, col);

        // A table's data stays on the script's side of a document: the copy is given its own (U8).
        JgsUiTables.CopyStates(source, copy);
        if (held.Count > 0 && JgsGraphicsProperties.AxesHolder(parent) is { } home)
        {
            using (GraphObjectLifecycle.SuppressNotifications())
            {
                foreach (AxesModel axes in held)
                {
                    UiContainerModel? keep = axes.Container;
                    clone.Axes.Remove(axes);
                    home.Axes.Add(axes);
                    axes.Container = keep;
                }
            }
        }

        return copy;
    }

    /// <summary>
    /// The children of an object in the order the document format writes them, which is what makes a
    /// position in the original name the same position in the copy.
    /// </summary>
    private static IReadOnlyList<GraphObject> CopyableParts(GraphObject target)
    {
        var parts = new List<GraphObject>();
        switch (target)
        {
            case FigureModel figure:
                parts.AddRange(figure.Axes);
                parts.AddRange(figure.Annotations);
                parts.AddRange(figure.Components);
                parts.AddRange(figure.Menus);
                parts.AddRange(figure.Toolbars);
                parts.AddRange(figure.ContextMenus);
                break;
            case UiContainerModel container:
                parts.AddRange(container.Components);
                break;
            case MenuItemModel item:
                parts.AddRange(item.Items);
                break;
            case ContextMenuModel menu:
                parts.AddRange(menu.Items);
                break;
            case UiTreeModel tree:
                parts.AddRange(tree.Nodes);
                break;
            case UiTreeNodeModel node:
                parts.AddRange(node.Nodes);
                break;
            case UiToolbarModel bar:
                parts.AddRange(bar.Tools);
                break;
            case AxesModel axes:
                parts.AddRange(axes.Plots);
                parts.AddRange(axes.Annotations);
                parts.AddRange(axes.Lights);
                parts.Add(axes.Legend);
                break;
        }

        return parts;
    }

    /// <summary>The steps from a figure down to one of its parts, or null when it is not one.</summary>
    private static List<int>? PathTo(GraphObject root, GraphObject target)
    {
        IReadOnlyList<GraphObject> parts = CopyableParts(root);
        for (int i = 0; i < parts.Count; i++)
        {
            if (ReferenceEquals(parts[i], target))
            {
                return [i];
            }

            if (PathTo(parts[i], target) is { } deeper)
            {
                deeper.Insert(0, i);
                return deeper;
            }
        }

        return null;
    }

    private static void Attach(GraphObject copy, GraphObject parent, int line, int col)
    {
        switch (parent, copy)
        {
            case (AxesModel axes, PlotObject plot):
                axes.Plots.Add(plot);
                return;
            case (AxesModel axes, AnnotationObject annotation):
                axes.Annotations.Add(annotation);
                return;
            case (AxesModel axes, LightModel light):
                axes.Lights.Add(light);
                return;
            case (FigureModel figure, AxesModel copied):
                copied.Container = null;
                figure.Axes.Add(copied);
                return;
            case (UiContainerModel { Figure: { } home } container, AxesModel copied):
                home.Axes.Add(copied);
                copied.Container = container;
                JG.TouchFigure(home);
                return;
            // The bars of U8: a menu into a figure, a context menu or another menu; a toolbar and
            // a context menu into a figure; a tool into a toolbar.
            case (FigureModel or ContextMenuModel or MenuItemModel, MenuItemModel item):
                using (GraphObjectLifecycle.SuppressNotifications())
                {
                    JgsGraphicsProperties.SiblingsOf(item)?.Remove(item);
                }

                (parent switch
                {
                    FigureModel bar => bar.Menus,
                    ContextMenuModel menu => menu.Items,
                    _ => ((MenuItemModel)parent).Items,
                }).Add(item);
                return;
            case (FigureModel figure, UiToolbarModel bar):
                using (GraphObjectLifecycle.SuppressNotifications())
                {
                    (bar.Parent as FigureModel)?.Toolbars.Remove(bar);
                }

                figure.Toolbars.Add(bar);
                JG.TouchFigure(figure);
                return;
            case (FigureModel figure, ContextMenuModel menu):
                using (GraphObjectLifecycle.SuppressNotifications())
                {
                    (menu.Parent as FigureModel)?.ContextMenus.Remove(menu);
                }

                figure.ContextMenus.Add(menu);
                return;
            case (UiToolbarModel bar, UiToolModel tool):
                using (GraphObjectLifecycle.SuppressNotifications())
                {
                    (tool.Parent as UiToolbarModel)?.Tools.Remove(tool);
                }

                bar.Tools.Add(tool);
                return;

            // A tree node into a tree or another node (U9).
            case (UiTreeModel or UiTreeNodeModel, UiTreeNodeModel node):
                using (GraphObjectLifecycle.SuppressNotifications())
                {
                    (node.Parent switch
                    {
                        UiTreeModel from => from.Nodes,
                        UiTreeNodeModel above => above.Nodes,
                        _ => null,
                    })?.Remove(node);
                }

                (parent is UiTreeModel into ? into.Nodes : ((UiTreeNodeModel)parent).Nodes).Add(node);
                return;

            // A tab goes in a tab group, and a tab group takes nothing else.
            case (IUiContainer holder, UiObject component) when (component is UiTabModel) != (holder is UiTabGroupModel):
                throw new JgsRuntimeException(line, col,
                    $"copyobj cannot put a {JgsGraphicsProperties.TypeNameOf(copy)} inside a {JgsGraphicsProperties.TypeNameOf(parent)}.");

            case (IUiContainer holder, UiObject component):
                // Out of the clone it was read into, and into the parent named, at the front.
                using (GraphObjectLifecycle.SuppressNotifications())
                {
                    component.Container?.Components.Remove(component);
                }

                holder.Components.Add(component);
                if (component is UiControlModel button && holder is UiButtonGroupModel group)
                {
                    group.Added(button);
                }

                if (component.Figure is { } shown)
                {
                    JG.TouchFigure(shown);
                }

                return;
            case (FigureModel figure, AnnotationObject annotation):
                figure.Annotations.Add(annotation);
                return;
            case (_, UiTreeNodeModel):
                throw new JgsRuntimeException(line, col, "MATLAB:ui:TreeNode:invalidParent", "'Parent' must be a valid Tree object or TreeNode object.");
            default:
                throw new JgsRuntimeException(line, col,
                    $"copyobj cannot put a {JgsGraphicsProperties.TypeNameOf(copy)} inside a {JgsGraphicsProperties.TypeNameOf(parent)}.");
        }
    }

    // --- gobjects, cla ----------------------------------------------------------------------------

    /// <summary>
    /// A block of empty handles to fill in, MATLAB's way of sizing an array of objects before the
    /// loop that draws them. The blanks are zeros: zero is never minted as a handle, so an unfilled
    /// slot fails <c>isgraphics</c> — where MATLAB would answer a placeholder object. Recorded as a
    /// divergence; what a script does with the array, which is overwrite it, works either way.
    /// </summary>
    private static JgsValue Gobjects(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("gobjects", args, 0, 2, line, col);
        int rows = args.Count == 0 ? 1 : Count("gobjects", args, 0, line, col);
        int cols = args.Count switch
        {
            0 => 1,
            1 => rows,
            _ => Count("gobjects", args, 1, line, col),
        };

        if (rows < 0 || cols < 0)
        {
            throw new JgsRuntimeException(line, col, "gobjects: a size cannot be negative.");
        }

        return JgsMatrix.FromColumnMajor(new double[rows * cols], rows, cols);
    }

    /// <summary>Empties an axes of what was drawn in it; <c>cla reset</c> also puts its settings back.</summary>
    private static JgsValue Cla(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("cla", args, 0, 2, line, col);
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        ArityRange("cla", rest, 0, 1, line, col);

        bool reset = false;
        if (rest.Count == 1)
        {
            string word = StrOf("cla", rest[0], line, col);
            if (!word.Equals("reset", StringComparison.OrdinalIgnoreCase))
            {
                throw new JgsRuntimeException(line, col, $"cla takes 'reset', but got '{word}'.");
            }

            reset = true;
        }

        ClearAxes(named ?? JG.Gca(), reset);
        return JgsValue.Null;
    }

    private static void ClearAxes(AxesModel axes, bool reset)
    {
        axes.Plots.Clear();
        axes.Annotations.Clear();
        axes.Lights.Clear();
        axes.Legend.Entries.Clear();
        axes.Legend.Visible = false;

        if (!reset)
        {
            return;
        }

        // 'reset' puts back everything a script had set on the axes itself, not only what it drew.
        axes.Title = string.Empty;
        axes.Hold = false;
        axes.Colorbar.Visible = false;
        foreach (AxisModel ruler in axes.XAxes.Concat(axes.YAxes).Append(axes.ZAxis))
        {
            ruler.Label = string.Empty;
            ruler.AutoScale = true;
            ruler.Inverted = false;
            ruler.Scale = AxisScaleType.Linear;
        }
    }

    /// <summary>
    /// Takes an object out of the figure it is in, for <c>delete(h)</c>. Only the handle half of the
    /// verb lives here; the name <c>delete</c> belongs to the file command, which hands anything that
    /// is not a path over to this.
    /// <para>
    /// Returns false when the value is not a handle at all, so the file command can go on to complain
    /// about it in its own words.
    /// </para>
    /// </summary>
    internal static bool TryDeleteGraphics(JgsValue value, JGraphScriptGlobals host)
    {
        int count = value.Type == JgsType.Array ? value.ArrayLength : 1;
        if (count == 0)
        {
            return false;
        }

        // Either every element is a handle or none is: half-deleting a mixed list and then erroring
        // would leave the figure in a state the script never asked for.
        var entries = new List<JgsHandleEntry>(count);
        for (int i = 0; i < count; i++)
        {
            JgsValue one = value.Type == JgsType.Array ? value.ElementAt(i) : value;
            if (!JgsHandleRegistry.TryGet(one, out JgsHandleEntry? entry))
            {
                return false;
            }

            entries.Add(entry);
        }

        foreach (JgsHandleEntry entry in entries)
        {
            Remove(entry.Target, host);
        }

        JgsHandleRegistry.DropUnreachable();
        return true;
    }

    /// <summary>
    /// Applies a menu verb's trailing name-value pairs through the property table — one definition
    /// of each name, however it is spelled into the object — then fires <c>CreateFcn</c> if the
    /// call carried one, which is MATLAB's one moment for it.
    /// </summary>
    private static void ApplyMenuOptions(
        string verb, GraphObject created, IReadOnlyList<JgsValue> args, int start, int line, int col)
    {
        if ((args.Count - start) % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, $"{verb}: options come in 'Name', value pairs.");
        }

        JgsHandleEntry entry = JgsHandleRegistry.Require(JgsHandleRegistry.For(created), line, col);
        bool fireCreate = false;
        for (int i = start; i < args.Count; i += 2)
        {
            string name = StrOf($"{verb} option name", args[i], line, col);
            JgsGraphicsProperties.Set(entry, name, args[i + 1], line, col);
            fireCreate |= name.Equals("CreateFcn", StringComparison.OrdinalIgnoreCase);
        }

        if (fireCreate)
        {
            JgsCallbackDispatcher.Current?.FireCreateFcn(created);
        }
    }

    private static void Remove(GraphObject target, JGraphScriptGlobals host)
    {
        switch (target)
        {
            case FigureModel figure:
                // Deleting a figure is closing it, which is the one removal the host has to hear
                // about. delete(fig) never consults CloseRequestFcn — that is close(fig)'s manner.
                int number = JG.GetFigureNumber(figure);
                if (number > 0)
                {
                    host.CloseFigure(number);
                }

                return;

            case AxesModel axes when axes.Parent is FigureModel owner:
                owner.Axes.Remove(axes);
                return;

            // A legend, colorbar or bubble legend belongs to its axes for the axes' whole life, so
            // deleting one is hiding it — but the deletion is real to the script, so it is announced
            // as one. An ordinary set(h, 'Visible', 'off') never comes through here and fires nothing.
            case LegendModel legend:
                GraphObjectLifecycle.NotifyDeleting(legend);
                legend.Visible = false;
                return;

            case ColorbarModel colorbar:
                GraphObjectLifecycle.NotifyDeleting(colorbar);
                colorbar.Visible = false;
                return;

            case BubbleLegendModel bubbles:
                GraphObjectLifecycle.NotifyDeleting(bubbles);
                bubbles.Visible = false;
                return;

            // The legend reconciles its own rows against the plots before each layout, so taking the
            // plot out is the whole of it.
            case PlotObject plot when plot.Parent is AxesModel drawn:
                drawn.Plots.Remove(plot);
                return;

            case AnnotationObject annotation when annotation.Parent is AxesModel owning:
                owning.Annotations.Remove(annotation);
                return;

            case AnnotationObject banner when banner.Parent is FigureModel figure:
                figure.Annotations.Remove(banner);
                return;

            case LightModel light when light.Parent is AxesModel lit:
                lit.Lights.Remove(light);
                return;

            case ContextMenuModel menu when menu.Parent is FigureModel owner:
                owner.ContextMenus.Remove(menu);
                return;

            case MenuItemModel item when item.Parent is ContextMenuModel menu:
                menu.Items.Remove(item);
                return;

            case MenuItemModel item when item.Parent is MenuItemModel parent:
                parent.Items.Remove(item);
                return;

            // The bars of U8: a figure's own menus, its toolbars, and their tools.
            case MenuItemModel item when item.Parent is FigureModel bar:
                bar.Menus.Remove(item);
                return;

            case UiToolbarModel toolbar when toolbar.Parent is FigureModel owner:
                owner.Toolbars.Remove(toolbar);
                return;

            case UiToolModel tool when tool.Parent is UiToolbarModel toolbar:
                toolbar.Tools.Remove(tool);
                return;

            case UiObject component when component.Container is { } holder:
                // Announced first, while everything in it still stands; then the axes placed in it,
                // which live in the figure's list and would otherwise outlive their panel.
                GraphObjectLifecycle.NotifyDeleting(component);

                // A tab that was showing hands the page to its neighbour (U8).
                if (component is UiTabModel page && holder is UiTabGroupModel pages)
                {
                    pages.Leaving(page);
                }

                if (component is UiContainerModel gone && component.Figure is { } home)
                {
                    foreach (AxesModel held in home.Axes.Where(gone.Holds).ToList())
                    {
                        home.Axes.Remove(held);
                    }
                }

                holder.Components.Remove(component);

                // A group whose selected button is deleted selects its first one (U5).
                if (component is UiCaptionModel { Value: true } and (UiRadioButtonModel or UiToggleButtonModel) && holder is UiButtonGroupModel bereft
                    && bereft.Components.OfType<UiCaptionModel>().FirstOrDefault(static b => b is UiRadioButtonModel or UiToggleButtonModel) is { } first)
                {
                    first.Value = true;
                }

                return;

            // A tree node (U9): its tree lets go of it, then its parent does.
            case UiTreeNodeModel node:
                GraphObjectLifecycle.NotifyDeleting(node);
                node.Tree?.Forget(node);
                (node.Parent switch
                {
                    UiTreeModel tree => tree.Nodes,
                    UiTreeNodeModel above => above.Nodes,
                    _ => null,
                })?.Remove(node);
                return;

            case UiOverlayModel overlay:
                RemoveOverlay(overlay);
                return;
        }
    }

    /// <summary>
    /// The arguments of <c>set</c> with every string scalar read as the char row it stands for —
    /// except the value of a property that tells a string from text, on a component that has one.
    /// </summary>
    private static IReadOnlyList<JgsValue> StringsOnlyWhereTold(IReadOnlyList<JgsValue> given)
    {
        if (!given.Any(IsStringScalar))
        {
            return given;
        }

        var args = new JgsValue[given.Count];
        for (int i = 0; i < given.Count; i++)
        {
            args[i] = IsStringScalar(given[i]) ? given[i].ElementAt(0) : given[i];
        }

        if (given.Count >= 3 && given[0].Type == JgsType.Number
            && JgsHandleRegistry.TryGet(given[0], out JgsHandleEntry? entry) && JgsGraphicsProperties.SpeaksAsComponent(entry.Target))
        {
            for (int i = 1; i + 1 < given.Count; i += 2)
            {
                if (IsStringScalar(given[i + 1]) && IsTextScalar(given[i])
                    && JgsGraphicsProperties.KeepsStringScalar(entry.Target, TextOf(given[i])))
                {
                    args[i + 1] = given[i + 1];
                }
            }
        }

        return args;
    }

    // --- shared plumbing ---------------------------------------------------------------------------

    /// <summary>The entries a handle or vector of handles names, in the order given.</summary>
    private static List<JgsHandleEntry> HandleList(string verb, JgsValue value, int line, int col)
    {
        var entries = new List<JgsHandleEntry>();
        int count = value.Type == JgsType.Array ? value.ArrayLength : 1;
        if (count == 0)
        {
            throw new JgsRuntimeException(line, col, $"{verb}: there are no handles to work on.");
        }

        for (int i = 0; i < count; i++)
        {
            entries.Add(JgsHandleRegistry.Require(
                value.Type == JgsType.Array ? value.ElementAt(i) : value, line, col));
        }

        return entries;
    }

    // --- refreshdata --------------------------------------------------------------------------

    /// <summary>
    /// <c>refreshdata</c>, which is what the <c>XDataSource</c> family is for: each source names a
    /// workspace variable, and this reads those variables again and writes what they now hold back
    /// into the chart. Without a handle it refreshes every chart in every figure, which is the form
    /// a script uses after recomputing the numbers a figure was drawn from.
    /// <para>
    /// The two position channels are written together. A series is held as a pair here and refuses a
    /// half-written one, and a refresh that lengthened both would otherwise fail on whichever it
    /// happened to reach first.
    /// </para>
    /// </summary>
    private static JgsValue RefreshData(
        IReadOnlyList<JgsValue> args, Interpreter interpreter, int line, int col)
    {
        ArityRange("refreshdata", args, 0, 2, line, col);

        JgsEnvironment workspace = interpreter.Globals;
        int given = args.Count;
        if (given > 0 && args[^1].Type == JgsType.String)
        {
            string word = StrOf("refreshdata", args[^1], line, col);
            workspace = word.ToLowerInvariant() switch
            {
                "base" => interpreter.Globals,
                "caller" => interpreter.CallerFrame ?? interpreter.Globals,
                _ => throw new JgsRuntimeException(
                    line, col, $"refreshdata: '{word}' is not 'base' or 'caller'."),
            };
            given--;
        }

        var roots = new List<GraphObject>();
        if (given > 0)
        {
            roots.AddRange(HandleList("refreshdata", args[0], line, col).Select(static e => e.Target));
        }
        else
        {
            foreach (int number in JG.FigureNumbers)
            {
                if (JG.TryGetFigure(number, out FigureModel figure))
                {
                    roots.Add(figure);
                }
            }
        }

        foreach (GraphObject root in roots)
        {
            RefreshOne(root, workspace, line, col);
            foreach (GraphObject child in JgsGraphicsProperties.DescendantsOf(root))
            {
                RefreshOne(child, workspace, line, col);
            }
        }

        return JgsValue.Array([]);
    }

    /// <summary>Re-reads one object's linked variables, if it has any and has ever been handled.</summary>
    private static void RefreshOne(GraphObject target, JgsEnvironment workspace, int line, int col)
    {
        if (!JgsHandleRegistry.TryGetEntry(target, out JgsHandleEntry? entry)
            || entry.DataSources.Count == 0)
        {
            return;
        }

        // XData and YData go in together; everything else is written on its own.
        bool hasX = entry.DataSources.ContainsKey("XDataSource");
        bool hasY = entry.DataSources.ContainsKey("YDataSource");
        if (hasX && hasY && target is XYPlot series)
        {
            double[] xs = SourceValues(entry, "XDataSource", workspace, line, col);
            double[] ys = SourceValues(entry, "YDataSource", workspace, line, col);
            if (xs.Length != ys.Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"refreshdata: {entry.DataSources["XDataSource"]} has {xs.Length} values and "
                    + $"{entry.DataSources["YDataSource"]} has {ys.Length}. A series is a pair.");
            }

            series.SetData(xs, ys);
        }

        foreach ((string source, string variable) in entry.DataSources)
        {
            if (hasX && hasY && source is "XDataSource" or "YDataSource")
            {
                continue;
            }

            if (!workspace.TryGet(variable, out JgsValue value))
            {
                throw new JgsRuntimeException(line, col,
                    $"refreshdata: {source} names '{variable}', which is not a variable there.");
            }

            JgsGraphicsProperties.Set(
                entry, JgsGraphicsProperties.ChannelOf(source), value, line, col);
        }
    }

    private static double[] SourceValues(
        JgsHandleEntry entry, string source, JgsEnvironment workspace, int line, int col)
    {
        string variable = entry.DataSources[source];
        if (!workspace.TryGet(variable, out JgsValue value))
        {
            throw new JgsRuntimeException(line, col,
                $"refreshdata: {source} names '{variable}', which is not a variable there.");
        }

        return ToDoubles(source, value, line, col);
    }

    private static string[] CellOfNames(string verb, JgsValue value, int line, int col)
    {
        var names = new string[value.ArrayLength];
        for (int i = 0; i < names.Length; i++)
        {
            names[i] = StrOf($"{verb}: a property name", value.ElementAt(i), line, col);
        }

        return names;
    }

    /// <summary>A handle on an object, or the empty answer that stands for "there isn't one".</summary>
    private static JgsValue Named(GraphObject? target) =>
        target is null ? JgsValue.Array([]) : JgsComponentContainers.ValueFor(target); // a custom component is its object (U10)

    /// <summary>The figure an object is drawn in, walking up until there is nothing above.</summary>
    internal static FigureModel? FigureOf(GraphObject? target)
    {
        for (GraphObject? walk = target; walk is not null; walk = walk.Parent)
        {
            if (walk is FigureModel figure)
            {
                return figure;
            }
        }

        return null;
    }
}
