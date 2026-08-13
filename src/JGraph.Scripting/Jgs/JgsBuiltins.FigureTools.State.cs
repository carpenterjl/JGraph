using System.ComponentModel;
using JGraph.Api;
using JGraph.Core.Model;

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
    private static void RegisterFigureStateBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        void DefineSilent(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, body) { BindsAnsAsStatement = false }));

        // --- Application data -------------------------------------------------------------------
        Define("getappdata", GetAppData);
        DefineSilent("setappdata", SetAppData);
        Define("isappdata", IsAppData);
        DefineSilent("rmappdata", RemoveAppData);

        // --- Keeping objects in step ------------------------------------------------------------
        Define("linkprop", LinkProp);
        DefineSilent("refresh", Refresh);

        // --- Transparency ------------------------------------------------------------------------
        DefineSilent("alpha", Alpha);
        // Both answer a value when asked bare, which is the form a script reads them in.
        void DefineBare(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { AutoCallsBare = true }));

        DefineBare("alim", (args, line, col) => AlphaRange("alim", args, line, col));
        DefineBare("alphamap", (args, line, col) => AlphaRange("alphamap", args, line, col));

        // --- What is drawing ----------------------------------------------------------------------
        env.Declare("rendererinfo", JgsValue.Function(
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

    private static JgsValue GetAppData(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (JgsHandleEntry entry, IReadOnlyList<JgsValue> rest) = PeelEntry("getappdata", args, line, col);

        // getappdata(h) with no name answers with the whole lot as a struct, which is how a script
        // asks what is there without knowing the names.
        if (rest.Count == 0)
        {
            return JgsValue.Struct(new Dictionary<string, JgsValue>(entry.AppData, StringComparer.Ordinal));
        }

        string name = StrOf("getappdata", rest[0], line, col);

        // A name nothing was stored under answers empty rather than erroring, which is MATLAB's rule
        // and what makes `if isempty(getappdata(h, 'x'))` the ordinary way to ask.
        return entry.AppData.TryGetValue(name, out JgsValue? stored) ? stored : JgsValue.Array([]);
    }

    private static JgsValue SetAppData(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (JgsHandleEntry entry, IReadOnlyList<JgsValue> rest) = PeelEntry("setappdata", args, line, col);
        if (rest.Count != 2)
        {
            throw new JgsRuntimeException(line, col,
                "setappdata takes a handle, a name, and the value to store under it.");
        }

        entry.AppData[StrOf("setappdata", rest[0], line, col)] = rest[1];
        return JgsValue.Null;
    }

    private static JgsValue IsAppData(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (JgsHandleEntry entry, IReadOnlyList<JgsValue> rest) = PeelEntry("isappdata", args, line, col);
        if (rest.Count != 1)
        {
            throw new JgsRuntimeException(line, col, "isappdata takes a handle and a name.");
        }

        return JgsValue.Bool(entry.AppData.ContainsKey(StrOf("isappdata", rest[0], line, col)));
    }

    private static JgsValue RemoveAppData(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (JgsHandleEntry entry, IReadOnlyList<JgsValue> rest) = PeelEntry("rmappdata", args, line, col);
        if (rest.Count != 1)
        {
            throw new JgsRuntimeException(line, col, "rmappdata takes a handle and a name.");
        }

        string name = StrOf("rmappdata", rest[0], line, col);
        if (!entry.AppData.Remove(name))
        {
            throw new JgsRuntimeException(line, col,
                $"rmappdata: nothing is stored under '{name}' on this object.");
        }

        return JgsValue.Null;
    }

    /// <summary>
    /// The live links a script has asked for. A link is kept here rather than only in the value the
    /// script holds, because it has to go on listening whether or not the script kept the handle —
    /// which is exactly what MATLAB's own <c>linkprop</c> object does when it is put into appdata.
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
        Links.Add(link);

        // MATLAB answers with a listener object a script stores to keep the link alive. Here the
        // link is alive because it exists, so the answer is a number to store — and storing it is
        // still the right habit for a script that means to work in both.
        return JgsValue.Number(Links.Count);
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
                    "alpha takes one number between 0 and 1, or the word 'opaque' or 'clear'.");
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
    /// <c>alim</c> and <c>alphamap</c> both describe a mapping from a value to an opacity, and this
    /// build has no such mapping: transparency is a number on an object. Both answer with what they
    /// would be if it existed — the full range, and the ramp <c>alphamap</c> defaults to — and
    /// setting either is accepted and changes nothing. That is a recorded divergence and the reason
    /// it is one is that the alternative, refusing, would break a script that only sets a default.
    /// </summary>
    private static JgsValue AlphaRange(string verb, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count > 0)
        {
            return JgsValue.Null;
        }

        return verb == "alim"
            ? JgsMatrix.FromColumnMajor([0, 1], 1, 2)
            : JgsMatrix.FromColumnMajor([.. Enumerable.Range(0, 64).Select(i => i / 63.0)], 1, 64);
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
