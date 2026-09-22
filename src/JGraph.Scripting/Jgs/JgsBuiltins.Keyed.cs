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

    /// <summary>
    /// M8 (ADR 0164): a builtin never mutates an argument. <c>e = insert(d, 1, 20)</c> and
    /// <c>remove</c> answer a changed collection, so they start from a wrapper of their own over the
    /// argument's payload and write through M7's setters, which detach it (#20, #21): <c>d</c> keeps
    /// its entries, and a failure part-way leaves it as it was. A <c>containers.Map</c> is a handle
    /// and is changed in place, as in MATLAB. The share is an internal one (M17), so it is taken in
    /// either dialect.
    /// </summary>
    private static JgsValue Private(JgsValue map) => IsHandleClass(map) ? map : JgsValue.Share(map);

    /// <summary>Whether this value is one of the two keyed collections.</summary>
    internal static bool IsKeyedCollection(JgsValue value) =>
        value.Type == JgsType.Struct
        && value.ClassName is MapClassName or DictionaryClassName;

    /// <summary>Registers <c>containers.Map</c>, <c>dictionary</c> and their verbs.</summary>
    internal static void RegisterKeyedCollectionBuiltins(JgsEnvironment env, JgsDialect dialect)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body)));

        // M2: a keyed collection's value is an entry, so a store takes what the dialect gives an
        // entry — a counted share in MATLAB, the caller's own wrapper in JGS (M17).
        bool shares = dialect.CopyOnAssign;

        // containers.Map is a dotted name, so it is a struct with a Map field holding the builtin —
        // the same shape M51 used for graphics.primitive.Line.empty. A bare `containers.Map` with no
        // arguments auto-calls through the member path, so `m = containers.Map;` makes an empty one.
        var mapConstructor = JgsValue.Function(new BuiltinFunction(MapClassName,
            (args, line, col) => NewKeyed(MapClassName, args, shares, line, col))
        {
            AutoCallsBare = true,
        });

        env.Builtins.RegisterConstant("containers", JgsValue.Struct(
            new Dictionary<string, JgsValue>(StringComparer.Ordinal) { ["Map"] = mapConstructor }));

        env.Builtins.Register("dictionary", JgsValue.Function(new BuiltinFunction(DictionaryClassName,
            (args, line, col) => NewKeyed(DictionaryClassName, args, shares, line, col))
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
            // M2: the answer's slots are entries of their own over the collection's values, so
            // each takes a share — `c = values(m); c{1}(1) = 9` must not reach the map.
            if (args.Count == 1)
            {
                return JgsValue.Cell([.. ValueCell(map).Select(JgsValue.Share)]);
            }

            // values(m, {'a', 'b'}) picks the ones asked for, in the order asked.
            JgsValue[] wanted = KeysAsked(args[1]);
            var picked = new JgsValue[wanted.Length];
            for (int i = 0; i < wanted.Length; i++)
            {
                picked[i] = JgsValue.Share(Lookup(map, wanted[i], line, col));
            }

            return JgsValue.Cell(picked);
        });

        Define("remove", (args, line, col) =>
        {
            Arity("remove", args, 2, line, col);
            JgsValue map = Private(RequireKeyed("remove", args[0], line, col));
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
            JgsValue map = Private(RequireKeyed("insert", args[0], line, col));
            JgsValue[] wanted = KeysAsked(args[1]);
            JgsValue[] given = args[2].Type == JgsType.Cell ? CellValues(map.ClassName!, args[2]) : [args[2]];
            for (int i = 0; i < wanted.Length; i++)
            {
                Put(map, wanted[i], RetainedForEntry(given.Length == 1 ? given[0] : given[i], shares), line, col);
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
                // M2: each row's fields are entries over the collection's own key and value.
                rows[i] = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
                {
                    ["Key"] = JgsValue.Share(keys[i]),
                    ["Value"] = JgsValue.Share(vals[i]),
                });
            }

            return JgsValue.Cell(rows);
        });
    }

    // --- Construction ------------------------------------------------------------------------------

    private static JgsValue NewKeyed(
        string className, IReadOnlyList<JgsValue> args, bool sharesOnStore, int line, int col)
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
        JgsValue[] given = args[1].Type == JgsType.Cell ? CellValues(className, args[1])
            : args[1].Type == JgsType.Array && !args[1].IsStringArray ? args[1].BoxedElements()
            : [args[1]];

        if (given.Length != keys.Length && given.Length != 1)
        {
            throw new JgsRuntimeException(line, col,
                $"{className}: {keys.Length} keys were given {given.Length} values.");
        }

        for (int i = 0; i < keys.Length; i++)
        {
            Put(map, keys[i], RetainedForEntry(given.Length == 1 ? given[0] : given[i], sharesOnStore), line, col);
        }

        if (keys.Length > 0)
        {
            fields["KeyType"] = JgsValue.Str(IsTextScalar(keys[0]) ? "char" : "double");
        }

        return map;
    }

    /// <summary>
    /// The values a cell argument gives, one a key. A <c>containers.Map</c> takes the cell's
    /// elements as the values; a <c>dictionary</c> given a cell is a dictionary of cell values,
    /// each entry the one-element cell around a share of its element (M2; V6, #136) - <c>d(k)</c>
    /// answers the cell and <c>d{k}</c> its content, as in MATLAB.
    /// </summary>
    private static JgsValue[] CellValues(string className, JgsValue cell) =>
        className == DictionaryClassName
            ? Array.ConvertAll(cell.AsCell, static v => JgsValue.Cell([JgsValue.Share(v)]))
            : cell.AsCell;

    // --- Reading and writing through the subscript --------------------------------------------------

    /// <summary>The value <c>m(key)</c> names, when the collection has the key.</summary>
    internal static bool TryLookup(JgsValue map, JgsValue key, out JgsValue value)
    {
        int at = IsTextScalar(key) || key.Type is JgsType.Number or JgsType.Bool ? FindKey(map, key) : -1;
        value = at >= 0 ? ValueCell(map)[at] : JgsValue.Null;
        return at >= 0;
    }

    /// <summary>
    /// What <c>d{key}</c> names: the content of the one-element cell a cell-valued dictionary
    /// holds under the key (V6, #136). Any other value has no braces to open.
    /// </summary>
    internal static JgsValue DictionaryBraceContent(JgsValue stored, int line, int col)
    {
        if (stored.Type == JgsType.Cell && stored.AsCell.Length == 1)
        {
            return stored.AsCell[0];
        }

        throw new JgsRuntimeException(line, col,
            $"Using curly braces on a dictionary with '{ClassOf(stored, JgsDialect.Matlab)}' value type is not supported. "
            + "The dictionary value type must be 'cell'.");
    }

    /// <summary>The value <c>m(key)</c> names, or a refusal saying which key was missing.</summary>
    internal static JgsValue Lookup(JgsValue map, JgsValue key, int line, int col)
    {
        // d(keys) on a dictionary reads every key named, in the keys' shape (V6, #149): its keys
        // are scalars, so an array of them is several lookups and never one key.
        if (map.ClassName == DictionaryClassName && key.Type == JgsType.Array && key.ArrayLength > 1)
        {
            var values = new JgsValue[key.ArrayLength];
            JgsValue[] cell = ValueCell(map);
            for (int i = 0; i < values.Length; i++)
            {
                JgsValue one = key.ElementAt(i);
                int found = FindKey(map, key.IsStringArray ? JgsValue.StringScalar(one.AsString) : one);
                if (found < 0)
                {
                    throw new JgsRuntimeException(line, col, $"Element {i + 1} of the key array not found.");
                }

                values[i] = cell[found];
            }

            bool texts = Array.TrueForAll(values, static v => v.IsStringArray && v.ArrayLength == 1);
            JgsValue gathered = texts
                ? JgsValue.StringArray(Array.ConvertAll(values, static v => v.ElementAt(0)), key.Rows, key.Cols)
                : JgsMatrix.FromElements(Array.ConvertAll(values, JgsValue.Share), key.Rows, key.Cols);
            return gathered;
        }

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
    /// The value stored is the caller's business (M2): every road here hands in what the dialect
    /// gives an entry — <see cref="RetainedForEntry"/> — never the script's own wrapper.
    /// </summary>
    internal static void Put(JgsValue map, JgsValue key, JgsValue value, int line, int col)
    {
        if (!IsTextScalar(key) && key.Type is not (JgsType.Number or JgsType.Bool))
        {
            throw new JgsRuntimeException(line, col,
                $"A key must be text or a number, but got a {key.TypeName}.");
        }

        int at = FindKey(map, key);
        Dictionary<string, JgsValue> fields = map.WritableStruct(); // M7: a value dictionary copies here
        if (at >= 0)
        {
            fields["values"].SetSlot(at, value);
            return;
        }

        JgsValue storedKey = IsTextScalar(key) ? JgsValue.Str(TextOf(key)) : key;
        fields["keys"] = JgsValue.Cell([.. KeyCell(map), storedKey]);
        fields["values"] = JgsValue.Cell([.. ValueCell(map), value]);
        fields["Count"] = JgsValue.Number(KeyCell(map).Length);
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

        Dictionary<string, JgsValue> fields = map.WritableStruct(); // M7, as in Put
        fields["keys"] = JgsValue.Cell([.. keptKeys]);
        fields["values"] = JgsValue.Cell([.. keptValues]);
        fields["Count"] = JgsValue.Number(keptKeys.Count);
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
