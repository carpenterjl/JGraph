namespace JGraph.Scripting.Jgs;

/// <summary>
/// The keyed collections (M64): <c>containers.Map</c> and <c>dictionary</c>.
/// </summary>
/// <remarks>
/// <para>
/// Both are a struct carrying a class name (M62's tag) and four fields — the keys, the values, and
/// the two type words. One representation serves both, because the only thing that genuinely
/// separates them is what happens on assignment: a <c>containers.Map</c> is a MATLAB <em>handle</em>
/// class, so two names for it are the same collection, and a <c>dictionary</c> is a value class, so
/// they are not.
/// </para>
/// <para>
/// That difference is expressed once, as a list of class names the binding copy leaves alone, rather
/// than as two representations with two sets of verbs. It is also the rule M68 needs for
/// <c>classdef Name &lt; handle</c>, which is why it is worth stating as a rule now.
/// </para>
/// <para>
/// Lookup is a scan of the key cell rather than a hash. A script's map is small — option tables,
/// name-to-index lookups, counters over a few dozen categories — and a scan keeps the whole
/// collection inside the value model, where copying, displaying and saving already work. A map big
/// enough for the difference to show is a table.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>The class name a <c>containers.Map</c> answers to.</summary>
    internal const string MapClassName = "containers.Map";

    /// <summary>The class name a <c>dictionary</c> answers to.</summary>
    internal const string DictionaryClassName = "dictionary";

    /// <summary>
    /// The classes whose values are references rather than copies, so binding one name to another
    /// does not clone it. MATLAB calls these handle classes; today the list holds the one keyed
    /// collection that is one, and M68's <c>classdef … &lt; handle</c> joins it.
    /// </summary>
    internal static bool IsHandleClass(JgsValue value) =>
        value.Type == JgsType.Struct
        && value.ClassName is MapClassName or VideoWriterClassName;

    /// <summary>Whether this value is one of the two keyed collections.</summary>
    internal static bool IsKeyedCollection(JgsValue value) =>
        value.Type == JgsType.Struct
        && value.ClassName is MapClassName or DictionaryClassName;

    /// <summary>Registers <c>containers.Map</c>, <c>dictionary</c> and their verbs.</summary>
    internal static void RegisterKeyedCollectionBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        // containers.Map is a dotted name, so it is a struct with a Map field holding the builtin —
        // the same shape M51 used for graphics.primitive.Line.empty. A bare `containers.Map` with no
        // arguments auto-calls through the member path, so `m = containers.Map;` makes an empty one.
        var mapConstructor = JgsValue.Function(new BuiltinFunction(MapClassName,
            (args, line, col) => NewKeyed(MapClassName, args, line, col))
        {
            AutoCallsBare = true,
        });

        env.Declare("containers", JgsValue.Struct(
            new Dictionary<string, JgsValue>(StringComparer.Ordinal) { ["Map"] = mapConstructor }));

        env.Declare("dictionary", JgsValue.Function(new BuiltinFunction(DictionaryClassName,
            (args, line, col) => NewKeyed(DictionaryClassName, args, line, col))
        {
            AutoCallsBare = true,
        }));

        Define("isKey", (args, line, col) =>
        {
            Arity("isKey", args, 2, line, col);
            JgsValue map = RequireKeyed("isKey", args[0], line, col);
            JgsValue[] wanted = KeysAsked(args[1]);
            var flags = new JgsValue[wanted.Length];
            for (int i = 0; i < wanted.Length; i++)
            {
                flags[i] = JgsValue.Bool(FindKey(map, wanted[i]) >= 0);
            }

            return flags.Length == 1 ? flags[0] : JgsValue.Array(flags);
        });

        Define("keys", (args, line, col) =>
        {
            Arity("keys", args, 1, line, col);
            JgsValue map = RequireKeyed("keys", args[0], line, col);

            // A Map answers with a cell, as MATLAB's does; a dictionary answers with an array of the
            // keys themselves, which is the newer surface and the reason the two names differ.
            JgsValue[] stored = KeyCell(map);
            return map.ClassName == MapClassName
                ? JgsValue.Cell([.. stored])
                : KeysAsArray(map, stored);
        });

        Define("values", (args, line, col) =>
        {
            ArityRange("values", args, 1, 2, line, col);
            JgsValue map = RequireKeyed("values", args[0], line, col);
            if (args.Count == 1)
            {
                return JgsValue.Cell([.. ValueCell(map)]);
            }

            // values(m, {'a', 'b'}) picks the ones asked for, in the order asked.
            JgsValue[] wanted = KeysAsked(args[1]);
            var picked = new JgsValue[wanted.Length];
            for (int i = 0; i < wanted.Length; i++)
            {
                picked[i] = Lookup(map, wanted[i], line, col);
            }

            return JgsValue.Cell(picked);
        });

        Define("remove", (args, line, col) =>
        {
            Arity("remove", args, 2, line, col);
            JgsValue map = RequireKeyed("remove", args[0], line, col);
            foreach (JgsValue key in KeysAsked(args[1]))
            {
                int at = FindKey(map, key);
                if (at < 0)
                {
                    throw new JgsRuntimeException(line, col,
                        $"remove: the collection has no key {KeyText(key)}.");
                }

                RemoveAt(map, at);
            }

            return map;
        });

        Define("numEntries", (args, line, col) =>
        {
            Arity("numEntries", args, 1, line, col);
            return JgsValue.Number(KeyCell(RequireKeyed("numEntries", args[0], line, col)).Length);
        });

        Define("isConfigured", (args, line, col) =>
        {
            Arity("isConfigured", args, 1, line, col);
            JgsValue map = RequireKeyed("isConfigured", args[0], line, col);

            // A dictionary is configured once it knows its key and value types, which here is once it
            // has an entry: the types are read from what was put in rather than declared up front.
            return JgsValue.Bool(KeyCell(map).Length > 0);
        });

        Define("lookup", (args, line, col) =>
        {
            ArityRange("lookup", args, 2, 4, line, col);
            JgsValue map = RequireKeyed("lookup", args[0], line, col);
            JgsValue[] wanted = KeysAsked(args[1]);

            // lookup(d, k, 'FallbackValue', v) answers with v for a key that is not there, which is
            // the whole reason the name exists beside plain indexing.
            JgsValue? fallback = null;
            if (args.Count == 4 && IsTextScalar(args[2])
                && TextOf(args[2]).Equals("FallbackValue", StringComparison.OrdinalIgnoreCase))
            {
                fallback = args[3];
            }

            var found = new JgsValue[wanted.Length];
            for (int i = 0; i < wanted.Length; i++)
            {
                int at = FindKey(map, wanted[i]);
                found[i] = at >= 0 ? ValueCell(map)[at]
                    : fallback ?? throw new JgsRuntimeException(line, col,
                        $"lookup: the collection has no key {KeyText(wanted[i])}, and no 'FallbackValue' was given.");
            }

            return found.Length == 1 ? found[0] : JgsValue.Cell(found);
        });

        Define("insert", (args, line, col) =>
        {
            ArityRange("insert", args, 3, 3, line, col);
            JgsValue map = RequireKeyed("insert", args[0], line, col);
            JgsValue[] wanted = KeysAsked(args[1]);
            JgsValue[] given = args[2].Type == JgsType.Cell ? args[2].AsCell : [args[2]];
            for (int i = 0; i < wanted.Length; i++)
            {
                Put(map, wanted[i], given.Length == 1 ? given[0] : given[i], line, col);
            }

            return map;
        });

        Define("entries", (args, line, col) =>
        {
            Arity("entries", args, 1, line, col);
            JgsValue map = RequireKeyed("entries", args[0], line, col);
            JgsValue[] keys = KeyCell(map);
            JgsValue[] vals = ValueCell(map);

            // One struct per entry, which is the shape a for-loop over the entries wants. MATLAB
            // answers with a table; a struct array carries the same two columns and is what this
            // build has until M65 makes a struct array a real thing.
            var rows = new JgsValue[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                rows[i] = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
                {
                    ["Key"] = keys[i],
                    ["Value"] = vals[i],
                });
            }

            return JgsValue.Cell(rows);
        });
    }

    // --- Construction ------------------------------------------------------------------------------

    private static JgsValue NewKeyed(string className, IReadOnlyList<JgsValue> args, int line, int col)
    {
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["keys"] = JgsValue.Cell([]),
            ["values"] = JgsValue.Cell([]),
            ["KeyType"] = JgsValue.Str("char"),
            ["ValueType"] = JgsValue.Str("any"),
            ["Count"] = JgsValue.Number(0),
        };

        JgsValue map = JgsValue.Struct(fields);
        map.SetClassName(className);

        if (args.Count == 0)
        {
            return map;
        }

        // containers.Map('KeyType', 'char', 'ValueType', 'any') declares the types and stays empty.
        if (args.Count == 4 && IsTextScalar(args[0]) && TextOf(args[0]).Equals("KeyType", StringComparison.OrdinalIgnoreCase))
        {
            fields["KeyType"] = JgsValue.Str(TextOf(args[1]));
            fields["ValueType"] = JgsValue.Str(TextOf(args[3]));
            return map;
        }

        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col,
                $"{className} expects keys and values, a 'KeyType'/'ValueType' pair, or no arguments.");
        }

        JgsValue[] keys = KeysAsked(args[0]);
        JgsValue[] given = args[1].Type == JgsType.Cell ? args[1].AsCell
            : args[1].Type == JgsType.Array && !args[1].IsStringArray ? args[1].BoxedElements()
            : [args[1]];

        if (given.Length != keys.Length && given.Length != 1)
        {
            throw new JgsRuntimeException(line, col,
                $"{className}: {keys.Length} keys were given {given.Length} values.");
        }

        for (int i = 0; i < keys.Length; i++)
        {
            Put(map, keys[i], given.Length == 1 ? given[0] : given[i], line, col);
        }

        if (keys.Length > 0)
        {
            fields["KeyType"] = JgsValue.Str(IsTextScalar(keys[0]) ? "char" : "double");
        }

        return map;
    }

    // --- Reading and writing through the subscript --------------------------------------------------

    /// <summary>The value <c>m(key)</c> names, or a refusal saying which key was missing.</summary>
    internal static JgsValue Lookup(JgsValue map, JgsValue key, int line, int col)
    {
        int at = FindKey(map, key);
        if (at < 0)
        {
            throw new JgsRuntimeException(line, col,
                $"The collection has no key {KeyText(key)}. Ask isKey first, or use lookup with a 'FallbackValue'.");
        }

        return ValueCell(map)[at];
    }

    /// <summary>
    /// Writes <c>m(key) = value</c> in place. In place is right for both collections: a Map is a
    /// handle and its holders share it, and a dictionary was already copied when it was bound.
    /// </summary>
    internal static void Put(JgsValue map, JgsValue key, JgsValue value, int line, int col)
    {
        if (!IsTextScalar(key) && key.Type is not (JgsType.Number or JgsType.Bool))
        {
            throw new JgsRuntimeException(line, col,
                $"A key must be text or a number, but got a {key.TypeName}.");
        }

        int at = FindKey(map, key);
        if (at >= 0)
        {
            JgsValue[] values = ValueCell(map);
            values[at] = value;
            return;
        }

        JgsValue storedKey = IsTextScalar(key) ? JgsValue.Str(TextOf(key)) : key;
        map.AsStruct["keys"] = JgsValue.Cell([.. KeyCell(map), storedKey]);
        map.AsStruct["values"] = JgsValue.Cell([.. ValueCell(map), value]);
        map.AsStruct["Count"] = JgsValue.Number(KeyCell(map).Length);
    }

    private static void RemoveAt(JgsValue map, int index)
    {
        JgsValue[] keys = KeyCell(map);
        JgsValue[] values = ValueCell(map);
        var keptKeys = new List<JgsValue>(keys.Length - 1);
        var keptValues = new List<JgsValue>(values.Length - 1);
        for (int i = 0; i < keys.Length; i++)
        {
            if (i == index)
            {
                continue;
            }

            keptKeys.Add(keys[i]);
            keptValues.Add(values[i]);
        }

        map.AsStruct["keys"] = JgsValue.Cell([.. keptKeys]);
        map.AsStruct["values"] = JgsValue.Cell([.. keptValues]);
        map.AsStruct["Count"] = JgsValue.Number(keptKeys.Count);
    }

    private static JgsValue[] KeyCell(JgsValue map) => map.AsStruct["keys"].AsCell;

    private static JgsValue[] ValueCell(JgsValue map) => map.AsStruct["values"].AsCell;

    /// <summary>Where <paramref name="key"/> sits in the collection, or -1 when it is not there.</summary>
    private static int FindKey(JgsValue map, JgsValue key)
    {
        JgsValue[] keys = KeyCell(map);
        bool asText = IsTextScalar(key);
        string text = asText ? TextOf(key) : string.Empty;
        double number = asText ? 0 : key.AsNumber;

        for (int i = 0; i < keys.Length; i++)
        {
            bool storedIsText = IsTextScalar(keys[i]);
            if (asText != storedIsText)
            {
                continue;
            }

            if (asText ? string.Equals(TextOf(keys[i]), text, StringComparison.Ordinal) : keys[i].AsNumber == number)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The keys an argument names: one, a cell of them, or a string array of them.</summary>
    private static JgsValue[] KeysAsked(JgsValue value)
    {
        if (value.Type == JgsType.Cell)
        {
            return value.AsCell;
        }

        if (value.IsStringArray && value.ArrayLength > 1)
        {
            return value.BoxedElements();
        }

        if (value.Type == JgsType.Array && !value.IsStringArray && value.ArrayLength > 1)
        {
            return value.BoxedElements();
        }

        return [value];
    }

    /// <summary>A dictionary's keys as an array of their own kind — text or numbers.</summary>
    private static JgsValue KeysAsArray(JgsValue map, JgsValue[] stored)
    {
        if (stored.Length == 0)
        {
            return JgsValue.Array([]);
        }

        if (IsTextScalar(stored[0]))
        {
            return JgsValue.StringArray(System.Array.ConvertAll(stored, k => JgsValue.Str(TextOf(k))));
        }

        return JgsValue.Array([.. stored]);
    }

    private static string KeyText(JgsValue key) =>
        IsTextScalar(key) ? $"'{TextOf(key)}'" : key.Display();

    private static JgsValue RequireKeyed(string name, JgsValue value, int line, int col) =>
        IsKeyedCollection(value)
            ? value
            : throw new JgsRuntimeException(line, col,
                $"{name} expects a containers.Map or a dictionary, but got a {value.TypeName}.");
}
