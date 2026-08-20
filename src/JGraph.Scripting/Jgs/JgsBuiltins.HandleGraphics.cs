using JGraph.Api;
using JGraph.Core.Model;
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
    internal static void PumpEvents() => JgsCallbackDispatcher.Current?.Drain();

    /// <summary>
    /// Waits out <paramref name="duration"/> in slices, delivering queued graphics events between
    /// them — <c>pause</c> is one of MATLAB's interruption points, and a click during a pause is
    /// answered during the pause, not after it. Wakes early only for cancellation, which throws.
    /// </summary>
    /// <param name="duration">How long to wait in total.</param>
    /// <param name="fallbackToken">The token to wake on when no session dispatcher is installed —
    /// a one-shot run's own token, captured when its globals were built.</param>
    internal static void PumpWait(TimeSpan duration, CancellationToken fallbackToken)
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

            token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(
                System.Math.Min(remaining, PumpSlice.TotalMilliseconds)));
            token.ThrowIfCancellationRequested();
            dispatcher?.Drain();
        }
    }

    private static void RegisterHandleGraphicsBuiltins(JgsEnvironment env, JGraphScriptGlobals host)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        // A verb that acts rather than answers prints nothing as a bare statement, even though it
        // hands something back for the script that wants it.
        void DefineSilent(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, body) { BindsAnsAsStatement = false }));

        Define("get", Get);
        DefineSilent("set", Set);

        // Both answer a question with no arguments — every object there is — so the bare name has to
        // be that answer rather than the function itself, or numel(findobj) counts a function.
        void DefineSearch(string name, bool hidden) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, (args, line, col) => Find(name, args, line, col, hidden))
                { AutoCallsBare = true }));

        DefineSearch("findobj", hidden: false);
        DefineSearch("findall", hidden: true);

        // ishandle and ishghandle ask the same question of the same registry. MATLAB separates them
        // because its ishandle also accepted the old Java-backed handles; there is only one kind here.
        Define("ishandle", (args, line, col) => IsHandle("ishandle", args, line, col));
        Define("ishghandle", (args, line, col) => IsHandle("ishghandle", args, line, col));
        Define("isgraphics", IsGraphics);

        Define("ancestor", Ancestor);
        DefineSilent("copyobj", Copy);
        Define("gobjects", Gobjects);

        // The three "which object" questions. gco outlives its click; the other two are only true
        // while a callback is running, and answer empty otherwise.
        env.Declare("gco", JgsValue.Function(new BuiltinFunction("gco", (args, line, col) =>
        {
            ArityRange("gco", args, 0, 1, line, col);
            return Named(JgsGraphicsCallbackState.CurrentObject);
        })
        { AutoCallsBare = true, BindsAnsAsStatement = false }));

        env.Declare("gcbo", JgsValue.Function(new BuiltinFunction("gcbo", (args, line, col) =>
        {
            Arity("gcbo", args, 0, line, col);
            return Named(JgsGraphicsCallbackState.CallbackObject);
        })
        { AutoCallsBare = true, BindsAnsAsStatement = false }));

        env.Declare("gcbf", JgsValue.Function(new BuiltinFunction("gcbf", (args, line, col) =>
        {
            Arity("gcbf", args, 0, line, col);
            return Named(FigureOf(JgsGraphicsCallbackState.CallbackObject));
        })
        { AutoCallsBare = true, BindsAnsAsStatement = false }));

        DefineSilent("cla", Cla);

        // --- uicontextmenu / uimenu (M71) --------------------------------------------------------

        // AutoCallsBare, because the documented spelling is the bare name on an assignment's right
        // side — cm = uicontextmenu — and a bare name in expression position is otherwise the
        // function rather than a call of it.
        void DefineMenuVerb(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, body) { AutoCallsBare = true, BindsAnsAsStatement = false }));

        DefineMenuVerb("uicontextmenu", (args, line, col) =>
        {
            // cm = uicontextmenu | uicontextmenu(parent) | uicontextmenu(___, Name, Value)
            int start = 0;
            FigureModel? figure = null;
            if (args.Count > 0 && args[0].Type == JgsType.Number)
            {
                JgsHandleEntry owner = JgsHandleRegistry.Require(args[0], line, col);
                if (owner.Target is not FigureModel named)
                {
                    throw new JgsRuntimeException(line, col,
                        $"uicontextmenu: the parent is a figure, and this handle names a {owner.TypeName}.");
                }

                figure = named;
                start = 1;
            }

            figure ??= JG.CurrentFigureNumber > 0 && JG.TryGetFigure(JG.CurrentFigureNumber, out FigureModel current)
                ? current
                : JG.Figure();

            var menu = new ContextMenuModel();
            figure.ContextMenus.Add(menu);
            ApplyMenuOptions("uicontextmenu", menu, args, start, line, col);
            return JgsHandleRegistry.For(menu);
        });

        DefineMenuVerb("uimenu", (args, line, col) =>
        {
            // m = uimenu(parent) | uimenu(parent, Name, Value) — the parent is a uicontextmenu or
            // another uimenu. MATLAB's no-parent form adds to the current figure's menu bar, which
            // this build does not have; that spelling is refused by name rather than half-honoured.
            int start = 0;
            GraphObject? parent = null;
            if (args.Count > 0 && args[0].Type == JgsType.Number)
            {
                JgsHandleEntry owner = JgsHandleRegistry.Require(args[0], line, col);
                if (owner.Target is not (ContextMenuModel or MenuItemModel))
                {
                    throw new JgsRuntimeException(line, col,
                        $"uimenu: the parent is a uicontextmenu or a uimenu, and this handle names a {owner.TypeName} — a figure's menu bar is not supported.");
                }

                parent = owner.Target;
                start = 1;
            }

            if (parent is null)
            {
                throw new JgsRuntimeException(line, col,
                    "uimenu needs a uicontextmenu or uimenu parent — a figure's menu bar is not supported.");
            }

            var item = new MenuItemModel();
            switch (parent)
            {
                case ContextMenuModel menu:
                    menu.Items.Add(item);
                    break;
                case MenuItemModel above:
                    above.Items.Add(item);
                    break;
            }

            ApplyMenuOptions("uimenu", item, args, start, line, col);
            return JgsHandleRegistry.For(item);
        });

        env.Declare("ishold", JgsValue.Function(new BuiltinFunction("ishold", (args, line, col) =>
        {
            ArityRange("ishold", args, 0, 1, line, col);
            (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
            Arity("ishold", rest, 0, line, col);
            return JgsValue.Bool((named ?? JG.Gca()).Hold);
        })
        { AutoCallsBare = true }));

        env.Declare("newplot", JgsValue.Function(new BuiltinFunction("newplot", (args, line, col) =>
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
            JgsValue[] rows = targets
                .Select(entry => JgsValue.Cell(
                    names.Select(name => JgsGraphicsProperties.Get(entry, name, line, col)).ToArray()))
                .ToArray();
            return rows.Length == 1 ? rows[0] : JgsValue.Cell(rows);
        }

        string one = StrOf("get", args[1], line, col);
        JgsValue[] answers = targets
            .Select(entry => JgsGraphicsProperties.Get(entry, one, line, col))
            .ToArray();
        return answers.Length == 1 ? answers[0] : JgsValue.Cell(answers);
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

        List<JgsHandleEntry> targets = HandleList("set", args[0], line, col);
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
            if (args[2].Type != JgsType.Cell || args[2].ArrayLength != names.Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"set: {names.Length} names need a cell of {names.Length} values.");
            }

            for (int i = 0; i < names.Length; i++)
            {
                JgsValue value = args[2].ElementAt(i);
                foreach (JgsHandleEntry entry in targets)
                {
                    JgsGraphicsProperties.Set(entry, names[i], value, line, col);
                }
            }

            return JgsValue.Null;
        }

        if ((args.Count - 1) % 2 != 0)
        {
            throw new JgsRuntimeException(line, col,
                $"set: every property needs a value, but '{StrOf("set", args[^1], line, col)}' has none.");
        }

        for (int i = 1; i < args.Count; i += 2)
        {
            string name = StrOf("set", args[i], line, col);
            foreach (JgsHandleEntry entry in targets)
            {
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
            foreach (int number in JG.FigureNumbers)
            {
                if (JG.TryGetFigure(number, out FigureModel figure))
                {
                    roots.Add(figure);
                }
            }
        }

        int depth = int.MaxValue;
        var wanted = new List<(string Name, JgsValue Value)>();
        for (int i = first; i < args.Count; i++)
        {
            string word = StrOf(verb, args[i], line, col);
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
                    $"{verb} understands 'flat' and '-depth', but not '{word}'.");
            }

            if (i + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col, $"{verb}: '{word}' needs a value to match.");
            }

            wanted.Add((word, args[++i]));
        }

        var found = new List<GraphObject>();
        var seen = new HashSet<GraphObject>();
        foreach (GraphObject root in roots)
        {
            Walk(root, 0);
        }

        var handles = new double[found.Count];
        for (int i = 0; i < found.Count; i++)
        {
            handles[i] = JgsHandleRegistry.For(found[i]).AsNumber;
        }

        // A column, which is the shape MATLAB's findobj answers in and what a for-loop over the
        // result expects.
        return JgsMatrix.FromColumnMajor(handles, handles.Length, 1);

        void Walk(GraphObject target, int level)
        {
            if (!seen.Add(target))
            {
                return;
            }

            JgsHandleEntry entry = JgsHandleRegistry.EntryFor(target);
            if ((hidden || entry.HandleVisible) && wanted.All(pair => Matches(entry, pair.Name, pair.Value)))
            {
                found.Add(target);
            }

            if (level >= depth)
            {
                return;
            }

            foreach (GraphObject child in hidden
                ? JgsGraphicsProperties.DescendantsOf(target)
                : JgsGraphicsProperties.ChildrenOf(target))
            {
                Walk(child, level + 1);
            }
        }
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
        return MapToBool(verb, args[0], IsLiveHandle, line, col);
    }

    /// <summary>
    /// <c>isgraphics(h)</c> asks whether a number names a live object; <c>isgraphics(h, 'axes')</c>
    /// asks whether it names one of that kind.
    /// </summary>
    private static JgsValue IsGraphics(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("isgraphics", args, 1, 2, line, col);
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
        JgsHandleRegistry.TryGet(JgsValue.Number(handle), out _);

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

        GraphObject? best = null;
        for (GraphObject? walk = start.Target.Parent; walk is not null; walk = walk.Parent)
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
        for (int i = 0; i < sources.Count; i++)
        {
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
                $"copyobj cannot copy a {JgsGraphicsProperties.TypeNameOf(source)}; only the things a figure holds — axes, plotted series, annotations and lights — are copied.");

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

        Attach(copy, parent, line, col);
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
                break;
            case AxesModel axes:
                parts.AddRange(axes.Plots);
                parts.AddRange(axes.Annotations);
                parts.AddRange(axes.Lights);
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
                figure.Axes.Add(copied);
                return;
            case (FigureModel figure, AnnotationObject annotation):
                figure.Annotations.Add(annotation);
                return;
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
        }
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
        target is null ? JgsValue.Array([]) : JgsHandleRegistry.For(target);

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
