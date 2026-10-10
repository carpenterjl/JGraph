using System.Runtime.CompilerServices;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Objects.Annotations;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The <c>Default*</c> and <c>Factory*</c> names (open item 44), measured in R2025b (probes
/// <c>probe_44</c>, <c>probe_44b</c>). <c>Factory&lt;Class&gt;&lt;Prop&gt;</c> is what a new object of
/// the class reads before anything is set — this build's own value, so a line's width is decision D1's
/// 1.5. <c>Default&lt;Class&gt;&lt;Prop&gt;</c> is a value an object holds for the new objects of that
/// class made beneath it: <c>set(0, 'DefaultAxesFontSize', 14)</c> holds it on the root, a figure or an
/// axes holds its own, and a new object takes the nearest one above it, before the call that made it
/// applies its own options. A read looks up the same way and ends at the factory value;
/// <c>'remove'</c> forgets one; <c>get(h, 'Default')</c> is a struct of the ones the object holds
/// (<c>[]</c> when none), and <c>get(0, 'Factory')</c> of every factory value this build can make.
/// As a value for any property, <c>'factory'</c> is the factory value and <c>'default'</c> the
/// nearest default or, without one, the factory value.
/// </summary>
internal static partial class JgsGraphicsProperties
{
    /// <summary>A class R2025b takes defaults for: its word, as written into the names, and a fresh object of it, when this build makes one.</summary>
    private sealed record DefaultClass(string Word, Func<GraphObject>? Make);

    private static readonly DefaultClass[] DefaultClasses =
    [
        new("Figure", static () => new FigureModel()),
        new("Axes", static () => new AxesModel()),
        new("Line", static () => new LinePlot([], [])),
        new("Text", static () => new TextAnnotation()),
        new("Patch", static () => new PatchPlot([], [], [])),
        new("Surface", static () => new SurfacePlot(new double[2, 2])),
        new("Image", static () => new ImagePlot(new double[1, 1])),
        new("Light", static () => new LightModel()),
        new("Uicontrol", static () => new UiControlModel()),
        new("Uipanel", static () => new UiPanelModel()),
        new("Uimenu", null),
        new("Uicontextmenu", null),
        new("Uitable", null),
        new("Uibuttongroup", null),
        new("Rectangle", null),
        new("Hggroup", null),
        new("Hgtransform", null),
    ];

    /// <summary>The defaults each object holds, by "class|property"; the root's are kept apart so a new run can forget them.</summary>
    private static readonly ConditionalWeakTable<GraphObject, Dictionary<string, (string Class, string Property, JgsValue Value)>> DefaultStores = new();

    private static readonly Dictionary<string, (string Class, string Property, JgsValue Value)> RootDefaults = new(StringComparer.Ordinal);

    /// <summary>Whether any object holds a default — what keeps a new object's adoption free when none does.</summary>
    private static bool s_anyDefaults;

    static JgsGraphicsProperties() => GraphObjectLifecycle.Adopted += ApplyDefaults;

    /// <summary>Forgets every default the root holds (a new run's root has none).</summary>
    internal static void ForgetDefaults()
    {
        RootDefaults.Clear();
        s_anyDefaults = false;
    }

    private static Dictionary<string, (string Class, string Property, JgsValue Value)>? StoreOf(GraphObject holder, bool create) =>
        holder is JgsGraphicsRoot ? RootDefaults
        : create ? DefaultStores.GetOrCreateValue(holder)
        : DefaultStores.TryGetValue(holder, out var held) ? held : null;

    /// <summary>
    /// A read of <c>Default</c>, <c>Factory</c>, <c>Default&lt;Class&gt;&lt;Prop&gt;</c> or
    /// <c>Factory&lt;Class&gt;&lt;Prop&gt;</c>; false for any other name.
    /// </summary>
    private static bool TryGetDefaultName(JgsHandleEntry entry, string name, int line, int col, out JgsValue value)
    {
        value = JgsValue.Null;
        if (name.Equals("Default", StringComparison.OrdinalIgnoreCase))
        {
            value = DefaultsStruct(entry.Target);
            return true;
        }

        if (name.Equals("Factory", StringComparison.OrdinalIgnoreCase))
        {
            value = FactoryStruct(line, col);
            return true;
        }

        if (!TrySplitDefaultName(name, out bool factory, out string rest))
        {
            return false;
        }

        (DefaultClass cls, string property) = ParseDefaultName(rest, line, col);
        if (!factory)
        {
            for (GraphObject? holder = entry.Target; holder is not null; holder = HolderAbove(holder))
            {
                if (StoreOf(holder, create: false) is { } store
                    && store.TryGetValue(Key(cls.Word, property), out var held))
                {
                    value = held.Value;
                    return true;
                }
            }
        }

        value = FactoryValue(cls, property, line, col);
        return true;
    }

    /// <summary>A write of <c>Default&lt;Class&gt;&lt;Prop&gt;</c>, held by the object; false for any other name.</summary>
    private static bool TrySetDefaultName(JgsHandleEntry entry, string name, JgsValue value, int line, int col)
    {
        if (!TrySplitDefaultName(name, out bool factory, out string rest))
        {
            return false;
        }

        (DefaultClass cls, string property) = ParseDefaultName(rest, line, col);
        if (factory)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:hg:propswch:FactoryReadOnly",
                $"Factory values are read-only: '{name}' cannot be set.");
        }

        string key = Key(cls.Word, property);
        if (JgsBuiltins.IsTextScalar(value) && JgsBuiltins.TextOf(value).Equals("remove", StringComparison.OrdinalIgnoreCase))
        {
            StoreOf(entry.Target, create: false)?.Remove(key);
            return true;
        }

        // The value is checked by writing it to a fresh object of the class, which refuses it as a
        // write to a real one would.
        string named = property;
        if (cls.Make?.Invoke() is { } fresh)
        {
            var trial = new JgsHandleEntry(fresh);
            if (!TryFind(fresh, property, out GraphicsProperty found))
            {
                throw UnknownForClass(cls, property, line, col);
            }

            named = found.Name;
            Set(trial, named, value, line, col);
        }

        JgsLifetime.Pin(value);
        StoreOf(entry.Target, create: true)![Key(cls.Word, named)] = (cls.Word, named, JgsValue.Share(value));
        s_anyDefaults = true;
        return true;
    }

    /// <summary>
    /// <c>'factory'</c> or <c>'default'</c> written to a property: the value either word stands for
    /// on this object (its class's factory value, or the nearest default above it); null for any other value.
    /// </summary>
    private static JgsValue? ResolveDefaultWord(JgsHandleEntry entry, GraphicsProperty property, JgsValue value, int line, int col)
    {
        if (!JgsBuiltins.IsTextScalar(value))
        {
            return null;
        }

        string word = JgsBuiltins.TextOf(value);
        bool factory = word.Equals("factory", StringComparison.OrdinalIgnoreCase);
        if (!factory && !word.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (ClassOfObject(entry.Target) is not { } cls)
        {
            return null;
        }

        if (!factory)
        {
            for (GraphObject? holder = HolderAbove(entry.Target); holder is not null; holder = HolderAbove(holder))
            {
                if (StoreOf(holder, create: false) is { } store
                    && store.TryGetValue(Key(cls.Word, property.Name), out var held))
                {
                    return held.Value;
                }
            }
        }

        return FactoryValue(cls, property.Name, line, col);
    }

    /// <summary>
    /// A new object takes the defaults held above it (open item 44), nearest first, as it joins its
    /// parent — before the call that made it applies its own options. A default that no longer suits
    /// the object is passed over.
    /// </summary>
    private static void ApplyDefaults(GraphObject child)
    {
        if (!s_anyDefaults || ClassOfObject(child) is not { } cls)
        {
            return;
        }

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        JgsHandleEntry? entry = null;
        for (GraphObject? holder = HolderAbove(child); holder is not null; holder = HolderAbove(holder))
        {
            if (StoreOf(holder, create: false) is not { Count: > 0 } store)
            {
                continue;
            }

            foreach ((string Class, string Property, JgsValue Value) held in store.Values)
            {
                if (held.Class != cls.Word || !taken.Add(held.Property))
                {
                    continue;
                }

                // A line's colour comes from its axes' ColorOrder, which R2025b's plot sets over the
                // default (measured); this build's lines are all plot's, so DefaultLineColor is held
                // and read back but given to none.
                if (cls.Word == "Line" && held.Property.Equals("Color", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    Set(entry ??= JgsHandleRegistry.EntryFor(child), held.Property, held.Value, 0, 0);
                }
                catch (JgsException)
                {
                    // A default the object cannot take is not the creating call's failure.
                }
            }
        }
    }

    /// <summary>The root's defaults for figures, given to a figure the <c>figure</c> verb has just made.</summary>
    internal static void ApplyFigureDefaults(FigureModel figure) => ApplyDefaults(figure);

    /// <summary>The object above this one that may hold defaults for it: its parent, and above a figure the root.</summary>
    private static GraphObject? HolderAbove(GraphObject target) =>
        target is JgsGraphicsRoot ? null : target.Parent ?? JgsGraphicsRoot.Instance;

    private static string Key(string cls, string property) => cls.ToLowerInvariant() + "|" + property.ToLowerInvariant();

    private static bool TrySplitDefaultName(string name, out bool factory, out string rest)
    {
        factory = name.StartsWith("Factory", StringComparison.OrdinalIgnoreCase);
        bool isDefault = name.StartsWith("Default", StringComparison.OrdinalIgnoreCase);
        rest = factory || isDefault ? name[7..] : string.Empty;
        return rest.Length > 0;
    }

    /// <summary>
    /// The class and property a default's name holds: the longest class word it starts with, and the
    /// rest. A name that starts with none is R2025b's invalid class, named whole.
    /// </summary>
    private static (DefaultClass Class, string Property) ParseDefaultName(string rest, int line, int col)
    {
        DefaultClass? best = null;
        foreach (DefaultClass candidate in DefaultClasses)
        {
            if (rest.Length > candidate.Word.Length && rest.StartsWith(candidate.Word, StringComparison.OrdinalIgnoreCase)
                && (best is null || candidate.Word.Length > best.Word.Length))
            {
                best = candidate;
            }
        }

        return best is null
            ? throw new JgsRuntimeException(line, col, "MATLAB:hgutils:InvalidClassName", $"{rest} is an invalid class name.")
            : (best, rest[best.Word.Length..]);
    }

    /// <summary>The class an object is among those that take defaults, by its type; null for any other object.</summary>
    private static DefaultClass? ClassOfObject(GraphObject target)
    {
        string type = TypeNameOf(target);
        return DefaultClasses.FirstOrDefault(c => c.Word.Equals(type, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>What a fresh object of the class reads for the property, in the script's dialect.</summary>
    private static JgsValue FactoryValue(DefaultClass cls, string property, int line, int col)
    {
        // R2025b's factory line colour is its text grey, which the auto colour of a fresh line is not.
        if (cls.Word == "Line" && property.Equals("Color", StringComparison.OrdinalIgnoreCase))
        {
            return Row(33.0 / 255, 33.0 / 255, 33.0 / 255);
        }

        if (cls.Make?.Invoke() is not { } fresh)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:hg:InvalidProperty",
                $"Unrecognized property {property} for class {cls.Word}.");
        }

        if (!TryFind(fresh, property, out GraphicsProperty found))
        {
            throw UnknownForClass(cls, property, line, col);
        }

        return found.Read(new JgsHandleEntry(fresh));
    }

    /// <summary>R2025b's refusal of a property a class does not have, named by the class's last word.</summary>
    private static JgsRuntimeException UnknownForClass(DefaultClass cls, string property, int line, int col)
    {
        string word = cls.Make?.Invoke() is { } fresh && MatlabClassOf(fresh) is { } full ? full[(full.LastIndexOf('.') + 1)..] : cls.Word;
        return new JgsRuntimeException(line, col, "MATLAB:hg:InvalidProperty", $"Unrecognized property {property} for class {word}.");
    }

    /// <summary>
    /// <c>get(h, 'Default')</c>: the defaults the object holds, or <c>[]</c> when it holds none (measured
    /// on a figure). The root's is always a struct: R2025b's root starts holding three of its own, which
    /// this build's does not (a recorded divergence), so it may be one with no fields.
    /// </summary>
    private static JgsValue DefaultsStruct(GraphObject holder)
    {
        if (StoreOf(holder, create: false) is not { Count: > 0 } store)
        {
            return holder is JgsGraphicsRoot ? JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)) : JgsEmpty.Zero();
        }

        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        foreach ((string cls, string property, JgsValue value) in store.Values)
        {
            fields["default" + cls + property] = value;
        }

        return JgsValue.Struct(fields);
    }

    /// <summary><c>get(0, 'Factory')</c>: every writable property of every class this build makes a fresh object of.</summary>
    private static JgsValue FactoryStruct(int line, int col)
    {
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        foreach (DefaultClass cls in DefaultClasses)
        {
            if (cls.Make?.Invoke() is not { } fresh)
            {
                continue;
            }

            var entry = new JgsHandleEntry(fresh);
            foreach (string name in NamesOf(fresh))
            {
                if (TryFind(fresh, name, out GraphicsProperty property) && property.Write is not null
                    && property.Name is not ("Parent" or "Children"))
                {
                    try
                    {
                        fields["factory" + cls.Word + property.Name] = property.Read(entry);
                    }
                    catch (JgsException)
                    {
                        // A property that cannot be read off an object nothing holds is left out.
                    }
                }
            }
        }

        return JgsValue.Struct(fields);
    }
}
