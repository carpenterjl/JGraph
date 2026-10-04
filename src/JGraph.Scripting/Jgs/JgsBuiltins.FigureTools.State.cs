using System.ComponentModel;
using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M60 wave C: what a script hangs on a figure object, what it keeps in step, and the three
/// transparency verbs.
/// <para>
/// None of these draw. <c>getappdata</c> and its three siblings are a dictionary per handle;
/// <c>linkprop</c> is a listener that copies one object's property onto its fellows; <c>refresh</c>
/// asks for a repaint; and the transparency verbs reach the plots through the same property table
/// <c>set</c> uses, so a chart type that has an alpha gets these for free and one that has not is
/// left alone rather than refused.
/// </para>
/// </summary>
internal static partial class JgsBuiltins
{
    private static void RegisterFigureStateBuiltins(JgsEnvironment env, JgsRunningDialect dialect)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body)));

        void DefineSilent(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(
                new BuiltinFunction(name, body) { BindsAnsAsStatement = false }));

        // --- Application data -------------------------------------------------------------------
        Define("getappdata", GetAppData);
        DefineSilent("setappdata", (args, line, col) => SetAppData(args, dialect.CopyOnAssign, line, col));
        Define("isappdata", IsAppData);
        DefineSilent("rmappdata", RemoveAppData);
        Define("guidata", (args, line, col) => GuiData(args, dialect.CopyOnAssign, line, col));
        env.Builtins.Register("guihandles", JgsValue.Function(
            new BuiltinFunction("guihandles", GuiHandles) { AutoCallsBare = true }));

        // --- Keeping objects in step ------------------------------------------------------------
        Define("linkprop", LinkProp);
        DefineSilent("refresh", Refresh);

        // --- Transparency ------------------------------------------------------------------------
        DefineSilent("alpha", Alpha);
        // Both answer a value when asked bare, which is the form a script reads them in.
        void DefineBare(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { AutoCallsBare = true }));

        DefineBare("alim", AlphaLimits);
        DefineBare("alphamap", AlphaMap);

        // --- What is drawing ----------------------------------------------------------------------
        env.Builtins.Register("rendererinfo", JgsValue.Function(
            new BuiltinFunction("rendererinfo", RendererInfo) { AutoCallsBare = true }));
    }

    /// <summary>The entry a state verb names, defaulting to the current figure.</summary>
    private static (JgsHandleEntry Entry, IReadOnlyList<JgsValue> Remaining) PeelEntry(
        string verb, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, $"{verb} expects a handle to the object it works on.");
        }

        return (JgsHandleRegistry.Require(args[0], line, col), args.Skip(1).ToList());
    }

    // --- application data (R2025b's forms and refusals, probe u4_guidata) ---------------------------

    /// <summary>The name <c>guidata</c> keeps its value under, among the figure's application data.</summary>
    private const string GuiDataName = "UsedByGUIData_m";

    /// <summary>
    /// The object an application-data verb names — any graphics object, the root included — with
    /// R2025b's refusals for too few arguments and for what is not an object.
    /// </summary>
    private static JgsHandleEntry AppDataOwner(IReadOnlyList<JgsValue> args, int needed, int line, int col)
    {
        if (args.Count < needed)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
        }

        if (args[0].Type != JgsType.Number || !JgsHandleRegistry.TryGetOrRoot(args[0], out JgsHandleEntry? entry))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:hg:InvalidArray", "Input was not a valid graphics object.");
        }

        return entry;
    }

    /// <summary>The name an application-data verb was given, which is text and nothing else.</summary>
    private static string AppDataName(JgsValue value, int line, int col) =>
        IsTextScalar(value)
            ? TextOf(value)
            : throw new JgsRuntimeException(line, col, "MATLAB:HandleGraphics:Appdata:InvalidSecondArgumentName",
                "Second argument must be a character vector");

    private static JgsValue GetAppData(IReadOnlyList<JgsValue> args, int line, int col)
    {
        JgsHandleEntry entry = AppDataOwner(args, 1, line, col);
        if (args.Count > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:maxrhs", "Too many input arguments.");
        }

        // getappdata(h) with no name answers with the whole lot as a struct, which is how a script
        // asks what is there without knowing the names.
        if (args.Count == 1)
        {
            return JgsValue.Struct(new Dictionary<string, JgsValue>(entry.AppData, StringComparer.Ordinal));
        }

        // A name nothing was stored under answers empty rather than erroring, which is MATLAB's rule
        // and what makes `if isempty(getappdata(h, 'x'))` the ordinary way to ask.
        return entry.AppData.TryGetValue(AppDataName(args[1], line, col), out JgsValue? stored)
            ? stored
            : JgsMatrix.FromColumnMajor([], 0, 0);
    }

    private static JgsValue SetAppData(IReadOnlyList<JgsValue> args, bool sharesOnStore, int line, int col)
    {
        JgsHandleEntry entry = AppDataOwner(args, 3, line, col);
        if (args.Count > 3)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:maxrhs", "Too many input arguments.");
        }

        // The data is kept as a struct's fields in R2025b, so a name is a field name.
        string name = AppDataName(args[1], line, col);
        if (!IsValidVariableName(name))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:AddField:InvalidFieldName", $"Invalid field name: '{name}'.");
        }

        StoreAppData(entry, name, args[2], sharesOnStore);
        return JgsValue.Null;
    }

    /// <summary>
    /// Stores one value among an object's application data. M2 (appendix A #100): in the MATLAB
    /// dialect the entry holds a counted share of its own — <c>setappdata(f, 'k', v); v(1) = 7</c>
    /// leaves what the figure hands back. The JGS dialect keeps the caller's own wrapper (M17) and
    /// marks the payload exposed so <c>clear</c> cannot free what the figure can still hand back.
    /// </summary>
    internal static void StoreAppData(JgsHandleEntry entry, string name, JgsValue value, bool sharesOnStore)
    {
        JgsValue kept = RetainedForEntry(value, sharesOnStore);
        JgsLifetime.Pin(kept); // V10: the object holds it for as long as it likes
        entry.AppData[name] = kept;
    }

    /// <summary>Takes one name out, keeping the others in the order they were stored in.</summary>
    internal static bool DropAppData(JgsHandleEntry entry, string name)
    {
        if (!entry.AppData.ContainsKey(name))
        {
            return false;
        }

        // Rebuilt rather than removed from: a dictionary hands a freed slot to the next name, which
        // would then be listed where the old one was instead of last.
        List<KeyValuePair<string, JgsValue>> rest = [.. entry.AppData.Where(pair => pair.Key != name)];
        entry.AppData.Clear();
        foreach ((string key, JgsValue value) in rest)
        {
            entry.AppData[key] = value;
        }

        return true;
    }

    /// <summary>The figure an object is in, for <c>guidata</c> and <c>guihandles</c>, or R2025b's refusal.</summary>
    private static FigureModel GuiFigure(string verb, JgsValue handle, int line, int col) =>
        handle.Type == JgsType.Number && JgsHandleRegistry.TryGet(handle, out JgsHandleEntry? named)
        && FigureOf(named.Target) is { } figure
            ? figure
            : throw new JgsRuntimeException(line, col, $"MATLAB:{verb}:InvalidInput",
                "Object must be a figure or one of its child objects.");

    /// <summary>
    /// <c>guidata(h)</c> reads, and <c>guidata(h, v)</c> stores, the one value a figure keeps for the
    /// script that built it (V6, appendix A #102). The object named may be the figure or anything in
    /// it: the value lives on the figure either way, which is what lets a callback on a button find
    /// what the figure's builder stored. It is one of the figure's application data, under R2025b's
    /// name for it, and storing an empty value takes it out (U4). Stored as a counted share (M2), so
    /// the caller's later writes do not reach it; nothing stored reads as <c>[]</c>.
    /// </summary>
    private static JgsValue GuiData(IReadOnlyList<JgsValue> args, bool sharesOnStore, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (args.Count > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        JgsHandleEntry owner = JgsHandleRegistry.EntryFor(GuiFigure("guidata", args[0], line, col));
        if (args.Count == 1)
        {
            return owner.AppData.TryGetValue(GuiDataName, out JgsValue? stored) ? stored : JgsMatrix.FromColumnMajor([], 0, 0);
        }

        if (IsEmptyValue(args[1]))
        {
            DropAppData(owner, GuiDataName);
        }
        else
        {
            StoreAppData(owner, GuiDataName, args[1], sharesOnStore);
        }

        return JgsValue.Null;
    }

    /// <summary>
    /// <c>guihandles(h)</c>: a struct of the handles in an object's figure, one field for each
    /// <c>Tag</c> that can be a field name, in the order <c>findall</c> meets them. Objects sharing
    /// a tag share a field, as a row.
    /// </summary>
    private static JgsValue GuiHandles(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count > 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        FigureModel figure = args.Count == 0 ? JG.CurrentFigure : GuiFigure("guihandles", args[0], line, col);
        JgsValue all = Find("findall", [JgsHandleRegistry.For(figure)], line, col, hidden: true);
        var tagged = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        int count = all.Type == JgsType.Array ? all.ArrayLength : 1;
        for (int i = 0; i < count; i++)
        {
            JgsValue handle = all.Type == JgsType.Array ? all.ElementAt(i) : all;
            if (!JgsHandleRegistry.TryGet(handle, out JgsHandleEntry? entry)
                || !JgsGraphicsProperties.TryFind(entry.Target, "Tag", out _))
            {
                continue;
            }

            JgsValue tag = JgsGraphicsProperties.Get(entry, "Tag", line, col);
            if (!IsTextScalar(tag) || !IsValidVariableName(TextOf(tag)))
            {
                continue;
            }

            if (!tagged.TryGetValue(TextOf(tag), out List<double>? handles))
            {
                tagged[TextOf(tag)] = handles = [];
            }

            handles.Add(handle.AsNumber);
        }

        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        foreach ((string tag, List<double> handles) in tagged)
        {
            fields[tag] = handles.Count == 1
                ? JgsValue.Number(handles[0])
                : JgsMatrix.FromColumnMajor([.. handles], 1, handles.Count);
        }

        // A figure with nothing tagged answers [], which is what R2025b's loop starts from.
        return fields.Count == 0 ? JgsMatrix.FromColumnMajor([], 0, 0) : JgsValue.Struct(fields);
    }

    /// <summary>
    /// The wrapper an entry outside the workspace keeps for <paramref name="value"/>: a counted
    /// share where the dialect copies on assignment (M2), the caller's own wrapper marked exposed
    /// where it does not (M17, M6).
    /// </summary>
    internal static JgsValue RetainedForEntry(JgsValue value, bool sharesOnStore)
    {
        if (sharesOnStore)
        {
            return JgsValue.Share(value);
        }

        value.MarkExposed();
        return value;
    }

    private static JgsValue IsAppData(IReadOnlyList<JgsValue> args, int line, int col)
    {
        JgsHandleEntry entry = AppDataOwner(args, 2, line, col);
        if (args.Count > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:maxrhs", "Too many input arguments.");
        }

        return JgsValue.Bool(entry.AppData.ContainsKey(AppDataName(args[1], line, col)));
    }

    private static JgsValue RemoveAppData(IReadOnlyList<JgsValue> args, int line, int col)
    {
        JgsHandleEntry entry = AppDataOwner(args, 2, line, col);
        if (args.Count > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:maxrhs", "Too many input arguments.");
        }

        string name = AppDataName(args[1], line, col);
        if (!DropAppData(entry, name))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:HandleGraphics:Appdata:InvalidPropertyName",
                $"Invalid user property: {name}.");
        }

        return JgsValue.Null;
    }

    /// <summary>
    /// The live links a script has asked for. A link is kept here rather than only in the value the
    /// script holds, because it has to go on listening whether or not the script kept the handle —
    /// which is exactly what MATLAB's own <c>linkprop</c> object does when it is put into appdata.
    /// <para>
    /// Process-wide and written while scripts run, so every touch of it is under its own lock — an
    /// unguarded <c>Add</c> from two threads at once is what tears a <see cref="List{T}"/> (M94).
    /// </para>
    /// </summary>
    private static readonly List<PropertyLink> Links = [];

    /// <summary>
    /// One set of objects kept in step on one set of property names.
    /// <para>
    /// Two things here are decisions rather than mechanism. The first is that the link listens to
    /// <see cref="GraphObject.Invalidated"/> rather than to <c>PropertyChanged</c>: the properties
    /// worth linking mostly do not live on the object a script names them on — <c>XLim</c> is a
    /// range on an axes' x ruler, and setting it raises a change on the ruler, which an axes-level
    /// <c>PropertyChanged</c> never hears. Invalidation bubbles up the tree, so the axes does hear
    /// it. The first draft used <c>PropertyChanged</c> and the mirror silently never fired.
    /// </para>
    /// <para>
    /// The second is that invalidation says something changed and not what, so the link remembers
    /// what it last saw each object holding and treats whichever one no longer matches as the
    /// origin. That also serves as the re-entry guard: after the copy every object holds the same
    /// value the link now remembers, so the invalidations the copy itself raises find nothing to do.
    /// </para>
    /// </summary>
    private sealed class PropertyLink
    {
        private readonly List<JgsHandleEntry> _targets;
        private readonly string[] _names;
        private readonly Dictionary<(int Target, string Name), JgsValue> _lastSeen = [];
        private bool _copying;

        public PropertyLink(List<JgsHandleEntry> targets, string[] names)
        {
            _targets = targets;
            _names = names;
            foreach (JgsHandleEntry target in _targets)
            {
                target.Target.Invalidated += OnInvalidated;
            }
        }

        private void OnInvalidated(object? sender, EventArgs args)
        {
            if (_copying || sender is not GraphObject changed)
            {
                return;
            }

            int origin = _targets.FindIndex(entry => ReferenceEquals(entry.Target, changed));
            if (origin < 0)
            {
                return;
            }

            _copying = true;
            try
            {
                foreach (string name in _names)
                {
                    if (Read(origin, name) is not { } value)
                    {
                        continue;
                    }

                    if (_lastSeen.TryGetValue((origin, name), out JgsValue? was)
                        && JgsValue.AreEqual(was, value))
                    {
                        continue;
                    }

                    Spread(name, value);
                }
            }
            finally
            {
                _copying = false;
            }
        }

        /// <summary>Brings every object into step with the first, which is what creating a link does.</summary>
        public void SyncFromFirst()
        {
            _copying = true;
            try
            {
                foreach (string name in _names)
                {
                    if (Read(0, name) is { } value)
                    {
                        Spread(name, value);
                    }
                }
            }
            finally
            {
                _copying = false;
            }
        }

        /// <summary>Writes one value onto every target and records it as what each now holds.</summary>
        private void Spread(string name, JgsValue value)
        {
            for (int i = 0; i < _targets.Count; i++)
            {
                try
                {
                    JgsGraphicsProperties.Set(_targets[i], name, value, 0, 0);
                }
                catch (JgsException)
                {
                    // A property one of the objects does not have is not a reason to break the
                    // others; the link simply does not carry that name for that object.
                    continue;
                }

                _lastSeen[(i, name)] = Read(i, name) ?? value;
            }
        }

        private JgsValue? Read(int target, string name)
        {
            try
            {
                return JgsGraphicsProperties.Get(_targets[target], name, 0, 0);
            }
            catch (JgsException)
            {
                return null;
            }
        }
    }

    private static JgsValue LinkProp(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count != 2)
        {
            throw new JgsRuntimeException(line, col,
                "linkprop takes a vector of handles and the property name (or a cell of names) to keep in step.");
        }

        var targets = new List<JgsHandleEntry>();
        foreach (JgsValue handle in HandleList(args[0]))
        {
            targets.Add(JgsHandleRegistry.Require(handle, line, col));
        }

        string[] names = args[1].Type == JgsType.Cell
            ? [.. args[1].AsCell.Select(name => StrOf("linkprop", name, line, col))]
            : [StrOf("linkprop", args[1], line, col)];

        var link = new PropertyLink(targets, names);
        link.SyncFromFirst();

        // The count is read inside the lock so the number answered is this link's own place in the
        // list rather than whatever another thread's Add has just made it.
        int place;
        lock (Links)
        {
            Links.Add(link);
            place = Links.Count;
        }

        // MATLAB answers with a listener object a script stores to keep the link alive. Here the
        // link is alive because it exists, so the answer is a number to store — and storing it is
        // still the right habit for a script that means to work in both.
        return JgsValue.Number(place);
    }

    /// <summary>
    /// The handles in a value that may be one handle or a row of them. A row of handles is a row of
    /// ordinary numbers and is therefore usually packed, so it is read through <c>ElementAt</c> —
    /// reaching for the boxed elements is what a packed array refuses.
    /// </summary>
    private static IEnumerable<JgsValue> HandleList(JgsValue value)
    {
        if (value.Type is not (JgsType.Array or JgsType.Cell))
        {
            yield return value;
            yield break;
        }

        for (int i = 0; i < value.ArrayLength; i++)
        {
            yield return value.ElementAt(i);
        }
    }

    private static JgsValue Refresh(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (FigureModel figure, IReadOnlyList<JgsValue> rest) = PeelFigure(args);
        if (rest.Count > 0)
        {
            throw new JgsRuntimeException(line, col, "refresh takes a figure handle and nothing else.");
        }

        figure.Invalidate(InvalidationKind.Render);
        return JgsValue.Null;
    }

    private static JgsValue Alpha(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () =>
        {
            if (rest.Count != 1)
            {
                throw new JgsRuntimeException(line, col,
                    "alpha takes one number between 0 and 1, a matrix of them, or the word "
                    + "'opaque' or 'clear'.");
            }

            // A matrix is alpha data: one transparency per point, looked up through the axes'
            // alphamap rather than applied as a single number. Only the plots with a grid to hang it
            // on take it, which is the same rule the scalar form follows for faces.
            if (rest[0].Type == JgsType.Array && JgsMatrix.IsMatrix(rest[0]))
            {
                double[,] grid = Matrix("alpha", rest, 0, line, col);
                bool taken = false;
                foreach (PlotObject plot in JG.Gca().Plots)
                {
                    switch (plot)
                    {
                        case SurfacePlot surface
                            when grid.GetLength(0) == surface.Z.GetLength(0)
                                && grid.GetLength(1) == surface.Z.GetLength(1):
                            surface.AlphaData = grid;
                            surface.FaceAlphaFlat = true;
                            taken = true;
                            break;
                        case ImagePlot image
                            when grid.GetLength(0) == image.Rows && grid.GetLength(1) == image.Columns:
                            image.AlphaData = grid;
                            taken = true;
                            break;
                    }
                }

                if (!taken)
                {
                    throw new JgsRuntimeException(line, col,
                        "alpha: no surface or image in this axes has a grid that size to hang the "
                        + "alpha data on.");
                }

                return JgsValue.Null;
            }

            double value = rest[0].Type == JgsType.String
                ? StrOf("alpha", rest[0], line, col).ToLowerInvariant() switch
                {
                    "opaque" => 1,
                    "clear" => 0,
                    var word => throw new JgsRuntimeException(line, col,
                        $"alpha: '{word}' is not one of opaque, clear."),
                }
                : ScalarOf("alpha", rest[0], line, col);

            // Every plot that has a face gets the value and every plot that has not is left alone,
            // which is the difference between "this axes is half transparent" and an error about
            // the one line in it.
            JgsValue setting = JgsValue.Number(System.Math.Clamp(value, 0, 1));
            foreach (PlotObject plot in JG.Gca().Plots)
            {
                JgsHandleEntry entry = JgsHandleRegistry.EntryFor(plot);
                if (JgsGraphicsProperties.TableFor(plot.GetType()).ContainsKey("FaceAlpha"))
                {
                    JgsGraphicsProperties.Set(entry, "FaceAlpha", setting, line, col);
                }
            }

            return JgsValue.Null;
        });
    }

    /// <summary>
    /// <c>alim</c>: the alpha-data limits this axes spreads its alphamap over. Reading answers the
    /// limits in force — the ones pinned, or the extent of the alpha data being drawn — and writing
    /// pins them. The two mode words release the limits or freeze what is showing, exactly as
    /// <c>caxis</c> does for colour.
    /// </summary>
    private static JgsValue AlphaLimits(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        AxesModel axes = named ?? JG.Gca();

        if (rest.Count == 0)
        {
            DataRange current = CurrentAlphaRange(axes);
            return JgsMatrix.FromColumnMajor([current.Min, current.Max], 1, 2);
        }

        if (rest[0].Type == JgsType.String)
        {
            string word = StrOf("alim", rest[0], line, col).Trim().ToLowerInvariant();
            axes.AlphaLimits = word switch
            {
                "auto" => null,
                "manual" => CurrentAlphaRange(axes),
                _ => throw new JgsRuntimeException(
                    line, col, $"alim: expected 'auto' or 'manual', got '{word}'."),
            };
            return JgsValue.Null;
        }

        double[] limits = ToDoubles("alim", rest[0], line, col);
        if (limits.Length != 2)
        {
            throw new JgsRuntimeException(line, col, "alim: expected [amin amax].");
        }

        if (!double.IsFinite(limits[0]) || !double.IsFinite(limits[1]) || limits[1] <= limits[0])
        {
            throw new JgsRuntimeException(line, col,
                $"alim: the limits must be finite and increasing, but got [{limits[0]} {limits[1]}].");
        }

        axes.AlphaLimits = new DataRange(limits[0], limits[1]);
        return JgsValue.Null;
    }

    /// <summary>
    /// <c>alphamap</c>: the transparencies alpha data is looked up in. Reading answers the map in
    /// force as a row, writing takes a vector of them, and the named forms are the ramps MATLAB
    /// builds for you.
    /// </summary>
    private static JgsValue AlphaMap(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        AxesModel axes = named ?? JG.Gca();

        if (rest.Count == 0)
        {
            IReadOnlyList<double> map = axes.Alphamap ?? AlphaSampler.DefaultMap;
            return JgsMatrix.FromColumnMajor([.. map], 1, map.Count);
        }

        if (rest[0].Type == JgsType.String)
        {
            string word = StrOf("alphamap", rest[0], line, col).Trim().ToLowerInvariant();
            axes.Alphamap = word switch
            {
                "default" or "rampup" => null,
                "rampdown" => [.. AlphaSampler.DefaultMap.Reverse()],
                "increase" or "decrease" or "spin" or "vup" or "vdown" => throw new JgsRuntimeException(
                    line, col,
                    $"alphamap: '{word}' modifies the map it is given rather than naming one, "
                    + "which is not implemented; pass the vector you want instead."),
                _ => throw new JgsRuntimeException(
                    line, col, $"alphamap: '{word}' is not one of default, rampup, rampdown."),
            };
            return JgsValue.Null;
        }

        double[] requested = ToDoubles("alphamap", rest[0], line, col);
        if (requested.Length == 0)
        {
            throw new JgsRuntimeException(line, col, "alphamap: expected at least one transparency.");
        }

        foreach (double entry in requested)
        {
            if (!double.IsFinite(entry) || entry < 0 || entry > 1)
            {
                throw new JgsRuntimeException(line, col,
                    $"alphamap: every transparency must be between 0 and 1, but got {entry}.");
            }
        }

        axes.Alphamap = requested;
        return JgsValue.Null;
    }

    /// <summary>
    /// The alpha limits in force: the ones pinned, else the extent of the first alpha data being
    /// drawn, else the whole unit range — the same shape of answer <c>CLim</c> gives for colour.
    /// </summary>
    private static DataRange CurrentAlphaRange(AxesModel axes)
    {
        if (axes.AlphaLimits is { } pinned)
        {
            return pinned;
        }

        foreach (PlotObject plot in axes.Plots)
        {
            double[,]? data = plot switch
            {
                SurfacePlot surface => surface.AlphaData,
                ImagePlot image => image.AlphaData,
                _ => null,
            };

            if (data is not null)
            {
                return AlphaBoundsOf(data);
            }
        }

        return new DataRange(0, 1);
    }

    /// <summary>The extent of a grid of alpha data, ignoring the values that are not numbers.</summary>
    private static DataRange AlphaBoundsOf(double[,] data)
    {
        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        foreach (double value in data)
        {
            if (double.IsFinite(value))
            {
                min = System.Math.Min(min, value);
                max = System.Math.Max(max, value);
            }
        }

        return double.IsFinite(min) && double.IsFinite(max) && max > min
            ? new DataRange(min, max)
            : new DataRange(0, 1);
    }

    private static JgsValue RendererInfo(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (FigureModel figure, IReadOnlyList<JgsValue> rest) = PeelFigure(args);
        _ = figure;
        if (rest.Count > 0)
        {
            throw new JgsRuntimeException(line, col, "rendererinfo takes a figure or axes handle and nothing else.");
        }

        return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            // One renderer draws everything here — the screen, every export, and getframe — which is
            // the point of the arrangement and what makes a captured frame worth comparing.
            ["GraphicsRenderer"] = JgsValue.Str("Skia"),
            ["RendererDevice"] = JgsValue.Str("software"),
            ["Vendor"] = JgsValue.Str("JGraph"),
            ["Version"] = JgsValue.Str("2"),
            ["Details"] = JgsValue.Str("A single backend-independent renderer over a Skia canvas."),
        });
    }
}
