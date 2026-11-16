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
    /// collection that is one, and M68's <c>classdef … &lt; handle</c> joins it. A <c>VideoWriter</c>
    /// (M108) and a <c>timer</c> (V6) are the other two builtin handle classes.
    /// </summary>
    internal static bool IsHandleClass(JgsValue value) =>
        value.Type == JgsType.Struct
        && value.ClassName is MapClassName or VideoWriterClassName or TimerClassName
            or ListenerClassName or PropListenerClassName or MatFileClassName;

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
    internal static void RegisterKeyedCollectionBuiltins(JgsEnvironment env, JgsRunningDialect dialect)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body)));

        // M2: a keyed collection's value is an entry, so a store takes what the dialect gives an
        // entry — a counted share in MATLAB, the caller's own wrapper in JGS (M17). The dialect is
        // the storing code's, read at the call (V11, ADR 0172).

        // containers.Map is a dotted name, so it is a struct with a Map field holding the builtin —
        // the same shape M51 used for graphics.primitive.Line.empty. A bare `containers.Map` with no
        // arguments auto-calls through the member path, so `m = containers.Map;` makes an empty one.
        var mapConstructor = JgsValue.Function(new BuiltinFunction(MapClassName,
            (args, line, col) => NewKeyed(MapClassName, args, dialect.CopyOnAssign, line, col))
        {
            AutoCallsBare = true,
        });

        env.Builtins.RegisterConstant("containers", JgsValue.Struct(
            new Dictionary<string, JgsValue>(StringComparer.Ordinal) { ["Map"] = mapConstructor }));

        // A dictionary keeps a 1-by-1 string it is given as a string: dictionary("a", "x") holds
        // the string "x", not the char row a builtin is otherwise handed (V6.17, measured).
        env.Builtins.Register("dictionary", JgsValue.Function(new BuiltinFunction(DictionaryClassName,
            (args, line, col) => NewKeyed(DictionaryClassName, args, dialect.CopyOnAssign, line, col))
        {
            AutoCallsBare = true,
            KeepsStringArguments = true,
        }));

        // configureDictionary(keyType, valueType): an empty dictionary that already knows its types,
        // so it is configured (stage K9: k9make's dictconfigured, which a .NET client receives).
        Define("configureDictionary", (args, line, col) =>
        {
            Arity("configureDictionary", args, 2, line, col);
            if (!IsTextScalar(args[0]) || !IsTextScalar(args[1]))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:dictionary:InvalidType", "Key and value types must be specified as text.");
            }

            string keyType = TextOf(args[0]) is "string" or "char" ? "char" : "double";
            return NewKeyed(DictionaryClassName,
                [JgsValue.Str("KeyType"), JgsValue.Str(keyType), JgsValue.Str("ValueType"), JgsValue.Str(TextOf(args[1]))],
                dialect.CopyOnAssign, line, col);
        });

        // keyHash(A): the uint64 a dictionary key is filed under. Equal values hash alike, a handle by
        // its identity; the strongly typed interface's GetHashCode calls it, where R2025b's client
        // refuses the name (stage K9: typed.point.hash, a divergence). JGraph keeps a uint64 as a
        // double, so the hash is folded into 53 bits.
        Define("keyHash", (args, line, col) =>
        {
            Arity("keyHash", args, 1, line, col);
            ulong hash = KeyHashOf(args[0]) & ((1UL << 53) - 1);
            return JgsNumericClasses.Stamp(JgsValue.Number(hash), JgsNumericClass.UInt64);
        });

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

            // A Map answers with a cell, as MATLAB's does; a dictionary answers with a column of the
            // keys themselves, which is the newer surface and the reason the two names differ.
            return map.ClassName == MapClassName
                ? JgsValue.Cell([.. KeyCell(map)])
                : DictionaryKeys(map);
        });

        Define("values", (args, line, col) =>
        {
            ArityRange("values", args, 1, 2, line, col);
            JgsValue map = RequireKeyed("values", args[0], line, col);
            // M2: the answer's slots are entries of their own over the collection's values, so
            // each takes a share — `c = values(m); c{1}(1) = 9` must not reach the map.
            if (args.Count == 1)
            {
                return map.ClassName == MapClassName
                    ? JgsValue.Cell([.. ValueCell(map).Select(JgsValue.Share)])
                    : DictionaryValues(map);
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
            return JgsValue.Bool(IsConfigured(RequireKeyed("isConfigured", args[0], line, col)));
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

        // insert keeps a 1-by-1 string value as a string, as the constructor does.
        env.Builtins.Register("insert", JgsValue.Function(new BuiltinFunction("insert", (args, line, col) =>
        {
            ArityRange("insert", args, 3, 3, line, col);
            JgsValue map = Private(RequireKeyed("insert", args[0], line, col));
            JgsValue[] wanted = KeysAsked(args[1]);
            JgsValue[] given = args[2].Type == JgsType.Cell ? CellValues(map.ClassName!, args[2]) : [args[2]];
            for (int i = 0; i < wanted.Length; i++)
            {
                Put(map, wanted[i], RetainedForEntry(given.Length == 1 ? given[0] : given[i], dialect.CopyOnAssign), line, col);
            }

            return map;
        })
        {
            KeepsStringArguments = true,
        }));

        Define("entries", (args, line, col) =>
        {
            Arity("entries", args, 1, line, col);
            return Entries(RequireKeyed("entries", args[0], line, col));
        });
    }

    /// <summary>
    /// One struct per entry, which is the shape a for-loop over the entries wants. MATLAB answers
    /// with a table; a struct array carries the same two columns and is what this build has until
    /// M65 makes a struct array a real thing.
    /// </summary>
    private static JgsValue Entries(JgsValue map)
    {
        JgsValue[] keys = KeyCell(map);
        JgsValue[] vals = ValueCell(map);
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
            ["Count"] = CountOf(0),
        };

        JgsValue map = JgsValue.Struct(fields);
        map.SetClassName(className);

        if (args.Count == 0)
        {
            return map;
        }

        // dictionary(d) of a .NET dictionary: a copy of its entries, each key and value converted as a
        // member declared with the dictionary's types answers it (ADR 0177).
        if (className == DictionaryClassName && args.Count == 1
            && args[0].AsExternalOrNull() is NetObject { Target: System.Collections.IDictionary entries } net)
        {
            return FromNetDictionary(map, fields, entries, net.Type, sharesOnStore, line, col);
        }

        // containers.Map's declared form takes the two types and nothing more, and its keys-and-values
        // form one trailing 'UniformValues' pair (open item 347, ADR 0384, probe p8d1 m1 to m18).
        bool? uniform = null;
        if (className == MapClassName && JgsRunningDialect.ThreadIsMatlab)
        {
            uniform = MapUniformValues(ref args, line, col);
            if (uniform is not null && args.Count == 0)
            {
                return map; // containers.Map('UniformValues', true) is an empty map
            }
        }

        // containers.Map('KeyType', 'char', 'ValueType', 'any') declares the types and stays empty.
        if (args.Count == 4 && IsTextScalar(args[0]) && TextOf(args[0]).Equals("KeyType", StringComparison.OrdinalIgnoreCase))
        {
            if (className == MapClassName && JgsRunningDialect.ThreadIsMatlab)
            {
                (fields["KeyType"], fields["ValueType"]) = MapDeclaredTypes(args[1], args[3], line, col);
                return map;
            }

            fields["KeyType"] = JgsValue.Str(DeclaredTypeText(args[1], line, col));
            fields["ValueType"] = JgsValue.Str(DeclaredTypeText(args[3], line, col));
            return map;
        }

        // The pair the other way round, containers.Map('ValueType', 'double', 'KeyType', 'single'), in
        // the MATLAB dialect (open item 342, ADR 0375, probe p7e3 vt14, vt15).
        if (args.Count == 4 && className == MapClassName && JgsRunningDialect.ThreadIsMatlab
            && IsTextScalar(args[0]) && TextOf(args[0]).Equals("ValueType", StringComparison.OrdinalIgnoreCase)
            && IsTextScalar(args[2]) && TextOf(args[2]).Equals("KeyType", StringComparison.OrdinalIgnoreCase))
        {
            (fields["KeyType"], fields["ValueType"]) = MapDeclaredTypes(args[3], args[1], line, col);
            return map;
        }

        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col,
                $"{className} expects keys and values, a 'KeyType'/'ValueType' pair, or no arguments.");
        }

        JgsValue[] keys = KeysAsked(args[0]);
        // An array of values is one a key: a numeric array's elements, or a string array's as
        // 1-by-1 strings (V6.17: dictionary([1 2], ["a" "b"]) holds "a" under 1, measured).
        JgsValue[] given = args[1].Type == JgsType.Cell ? CellValues(className, args[1])
            : args[1].IsStringArray && args[1].ArrayLength > 1
                ? System.Array.ConvertAll(args[1].BoxedElements(), static e => JgsValue.StringScalar(e.AsString))
            : args[1].Type == JgsType.Array && !args[1].IsStringArray ? args[1].BoxedElements()
            : [args[1]];

        if (given.Length != keys.Length && given.Length != 1)
        {
            throw new JgsRuntimeException(line, col,
                $"{className}: {keys.Length} keys were given {given.Length} values.");
        }

        string? numericKeyType = className == MapClassName && JgsRunningDialect.ThreadIsMatlab ? MapKeyTypeOfKeys(keys, args[0]) : null;
        if (numericKeyType is not null)
        {
            keys = System.Array.ConvertAll(keys, k => MapKeyAs(numericKeyType, k, line, col));
        }

        for (int i = 0; i < keys.Length; i++)
        {
            Put(map, keys[i], RetainedForEntry(given.Length == 1 ? given[0] : given[i], sharesOnStore), line, col);
        }

        if (keys.Length > 0)
        {
            fields["KeyType"] = JgsValue.Str(numericKeyType ?? (IsTextScalar(keys[0]) ? "char" : "double"));
            fields["ValueType"] = JgsValue.Str(className == DictionaryClassName ? ValueKind(ValueCell(map)[0]) : MapValueType(given));
        }

        if (uniform is bool asked)
        {
            fields["ValueType"] = asked ? JgsValue.Str(UniformMapValueType(given, line, col)) : JgsValue.Str("any");
        }

        return map;
    }

    /// <summary>
    /// containers.Map's trailing <c>'UniformValues', u</c> pair in the MATLAB dialect, taken off the
    /// arguments: null when there is none. R2025b refuses it after a declared pair, and any other count
    /// of arguments around it, as <c>IncorrectNumberInputs</c>; a declared form whose third argument is
    /// not the other type's name is its <c>Param</c> refusal; <c>u</c> is a logical or numeric scalar
    /// (open item 347, ADR 0384, probe p8d1 m1 to m18).
    /// </summary>
    private static bool? MapUniformValues(ref IReadOnlyList<JgsValue> args, int line, int col)
    {
        static bool Named(JgsValue value, string name) => IsTextScalar(value) && TextOf(value).Equals(name, StringComparison.OrdinalIgnoreCase);

        bool declared = Named(args[0], "KeyType") || Named(args[0], "ValueType");
        bool trailing = args.Count >= 2 && Named(args[^2], "UniformValues");
        if (declared && args.Count > 4)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:Containers:Map:IncorrectNumberInputs", "Incorrect number of inputs specified.");
        }

        if (declared && args.Count == 4 && !Named(args[2], Named(args[0], "KeyType") ? "ValueType" : "KeyType"))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:Containers:Map:Param",
                "Missing parameter name 'KeyType' or 'ValueType'. \nUse syntax: newMap = containers.Map('KeyType', myKeyType, 'ValueType', myValueType).");
        }

        // Another name after the keys and values is ignored with R2025b's warning (probe p8d3 m1).
        if (!declared && !trailing && args.Count == 4 && !IsTextScalar(args[0]) && IsTextScalar(args[2]))
        {
            if ((JgsLifetime.Current?.Interpreter.Host ?? JgsCallbackDispatcher.Current?.Interpreter?.Host) is { } host)
            {
                Warn(host, "MATLAB:Containers:Map:InvalidParameterName",
                    $"Unknown parameter name {TextOf(args[2])} will be ignored.  Check documentation for list of known parameters.");
            }

            args = [args[0], args[1]];
            return null;
        }

        if (declared || !trailing)
        {
            return null;
        }

        if (args.Count is not (2 or 4))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:Containers:Map:IncorrectNumberInputs", "Incorrect number of inputs specified.");
        }

        JgsValue flag = args[^1];
        if (!(flag.Type is JgsType.Bool or JgsType.Number) || IsTextScalar(flag))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:Containers:Map:ExpectScalarLogical", "Expected 'UniformValues' to be a logical scalar.");
        }

        bool uniform = flag.Type == JgsType.Bool ? flag.AsBool : flag.AsNumber != 0;
        args = args.Count == 2 ? [] : [args[0], args[1]];
        return uniform;
    }

    /// <summary>
    /// The value type of a map made with <c>'UniformValues', true</c>: R2025b's inferred type, or its
    /// refusal of values that are not all char rows or all scalars of one class (open item 347, ADR 0384).
    /// </summary>
    private static string UniformMapValueType(JgsValue[] values, int line, int col)
    {
        string inferred = MapValueType(values);
        if (inferred != "any")
        {
            return inferred;
        }

        const string Words = "When 'UniformValues' is true, values must be all character arrays or all scalars of matching type.";
        bool nonScalar = System.Array.Exists(values, static v => v.Type == JgsType.Array && !v.IsStringArray && v.ArrayLength != 1);
        throw new JgsRuntimeException(line, col, nonScalar ? "MATLAB:Containers:Map:UniformNonScalar" : "MATLAB:Containers:Map:UniformMismatch", Words);
    }

    /// <summary>The key types R2025b's containers.Map declares (open item 342, ADR 0375, probe p7e1 k1 to k20).</summary>
    private static readonly string[] MapKeyTypes = ["char", "double", "single", "int32", "uint32", "int64", "uint64"];

    /// <summary>The value types R2025b's containers.Map declares (probe p7e3 vt1 to vt20).</summary>
    private static readonly string[] MapValueTypes =
        ["any", "char", "logical", "double", "single", "int8", "uint8", "int16", "uint16", "int32", "uint32", "int64", "uint64"];

    /// <summary>
    /// <c>containers.Map('KeyType', k, 'ValueType', v)</c>'s two types in the MATLAB dialect: each a text
    /// scalar, matched in any case and kept in lower case; the key's refusal first, then the value's,
    /// in R2025b's words (open item 342, ADR 0375). R2025b refuses a uint8, int8, int16, uint16,
    /// logical, string, cell or struct key and a string, cell, struct or function_handle value.
    /// </summary>
    private static (JgsValue KeyType, JgsValue ValueType) MapDeclaredTypes(JgsValue keyType, JgsValue valueType, int line, int col)
    {
        string key = DeclaredTypeText(keyType, line, col);
        string value = DeclaredTypeText(valueType, line, col);
        string? keyFound = System.Array.Find(MapKeyTypes, t => t.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (keyFound is null)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:Containers:Map:KeyUnsupportedType",
                $"Unsupported KeyType '{key}' specified.  See documentation for valid key types.");
        }

        string? valueFound = System.Array.Find(MapValueTypes, t => t.Equals(value, StringComparison.OrdinalIgnoreCase));
        return valueFound is null
            ? throw new JgsRuntimeException(line, col, "MATLAB:Containers:Map:ValueUnsupportedType",
                $"Unsupported ValueType '{value}' specified.  See documentation for valid value types.")
            : (JgsValue.Str(keyFound), JgsValue.Str(valueFound));
    }

    /// <summary>A declared key or value type's text; anything but a text scalar is R2025b's refusal (it was a crash).</summary>
    private static string DeclaredTypeText(JgsValue value, int line, int col) =>
        IsTextScalar(value)
            ? TextOf(value)
            : throw new JgsRuntimeException(line, col, "MATLAB:string:MustBeStringScalarOrCharacterVector", "Argument must be a text scalar.");

    /// <summary>
    /// The key type a containers.Map takes from numeric keys in the MATLAB dialect (open item 342, ADR
    /// 0375, probe p7e3 mk1 to mk10): keys all of one class among single, int32, uint32, int64 and uint64
    /// keep it; any other mix is double (an int8, uint8, int16, uint16 or logical key too), with R2025b's
    /// warning when a 64-bit integer is converted. Null when a key is not a real numeric scalar or is NaN,
    /// which keeps the road it had.
    /// </summary>
    private static string? MapKeyTypeOfKeys(JgsValue[] keys, JgsValue given)
    {
        if (keys.Length == 0 || System.Array.Exists(keys, static k => IsTextScalar(k) || MapScalarNumber(k) is not double n || double.IsNaN(n)))
        {
            return null;
        }

        // An array of keys is of its own class; a boxed element of it may not say so.
        string[] classes = given.Type == JgsType.Array
            ? System.Array.ConvertAll(keys, _ => ClassOf(given, JgsDialect.Matlab))
            : System.Array.ConvertAll(keys, static k => ClassOf(k, JgsDialect.Matlab));
        if (System.Array.TrueForAll(classes, c => c == classes[0]) && classes[0] is "single" or "int32" or "uint32" or "int64" or "uint64")
        {
            return classes[0];
        }

        if (System.Array.Exists(classes, static c => c is "int64" or "uint64")
            && (JgsLifetime.Current?.Interpreter.Host ?? JgsCallbackDispatcher.Current?.Interpreter?.Host) is { } host)
        {
            Warn(host, "MATLAB:Containers:Map:Convert64BitIntToDouble",
                "One or more of the input arrays will be converted from a 64 bit integer to double.  This may result in a loss of precision.");
        }

        return "double";
    }

    /// <summary>
    /// The value type a containers.Map takes from the values it is made with, as R2025b's does
    /// (measured, stage K12): real scalars all of one numeric or logical class are that class, char
    /// rows are char, and anything else - a mix, a vector, a string, a complex number - any.
    /// </summary>
    private static string MapValueType(JgsValue[] values)
    {
        string? kind = null;
        foreach (JgsValue value in values)
        {
            string these = value.Type == JgsType.String && !value.IsCharMatrix ? "char"
                : value.Type is JgsType.Number or JgsType.Bool ? ClassOf(value, JgsDialect.Matlab)
                : "any";
            if (these == "any" || (kind is not null && kind != these))
            {
                return "any";
            }

            kind = these;
        }

        return kind ?? "any";
    }

    /// <summary>
    /// A MATLAB dictionary holding a copy of a .NET dictionary's entries: a <c>System.String</c> key
    /// becomes a string (R2025b keeps it a <c>System.String</c>, which its own <c>keys</c> then cannot
    /// combine — ADR 0177's divergence), any other key and every value what a member declared with the
    /// dictionary's key and value types answers. An empty one stays unconfigured, as R2025b's does.
    /// </summary>
    private static JgsValue FromNetDictionary(
        JgsValue map, Dictionary<string, JgsValue> fields, System.Collections.IDictionary entries, Type type,
        bool sharesOnStore, int line, int col)
    {
        Type keyType = typeof(object);
        Type valueType = typeof(object);
        if (type.GetInterfaces().Append(type).FirstOrDefault(static i => i.IsGenericType
                && i.GetGenericTypeDefinition() == typeof(IDictionary<,>)) is { } generic)
        {
            keyType = generic.GetGenericArguments()[0];
            valueType = generic.GetGenericArguments()[1];
        }

        JgsValue? first = null;
        foreach (System.Collections.DictionaryEntry entry in entries)
        {
            JgsValue key = entry.Key is string text ? JgsValue.StringScalar(text) : Net.NetConvert.ToMatlab(entry.Key, keyType, line, col);
            JgsValue value = Net.NetConvert.ToMatlab(entry.Value, valueType, line, col);
            first ??= key;
            Put(map, key, RetainedForEntry(value, sharesOnStore), line, col);
        }

        if (first is { } firstKey)
        {
            fields["KeyType"] = JgsValue.Str(IsTextScalar(firstKey) ? "char" : "double");
            fields["ValueType"] = JgsValue.Str(ValueKind(ValueCell(map)[0]));
        }

        return map;
    }

    /// <summary>
    /// The value type a dictionary takes from its first value (V6.17): what <c>values</c> answers
    /// once every entry is removed still has this kind (measured: a 0-by-1 double).
    /// </summary>
    private static string ValueKind(JgsValue value) => ClassOf(value, JgsDialect.Matlab);

    /// <summary>
    /// A dictionary is configured once it knows its key and value types: once it has an entry, or
    /// had one - removing every entry leaves it configured (V6.17, measured).
    /// </summary>
    private static bool IsConfigured(JgsValue map) =>
        KeyCell(map).Length > 0 || map.AsStruct["ValueType"].AsString != "any";

    /// <summary>How many entries a collection holds.</summary>
    internal static int EntryCount(JgsValue map) => KeyCell(map).Length;

    /// <summary>
    /// Whether <paramref name="value"/> is a <c>containers.Map</c> read in the MATLAB dialect, where
    /// the class's own <c>size</c> makes it a column of its keys: <c>size(m)</c> is <c>[Count 1]</c>,
    /// and <c>isempty</c>, <c>isscalar</c>, <c>isrow</c>, <c>iscolumn</c> and <c>isvector</c> answer
    /// for that shape, while <c>numel</c> stays 1 (open item 200, ADR 0351, measured). The JGS
    /// dialect keeps its 1-by-1 reading. <paramref name="count"/> is the Count.
    /// </summary>
    internal static bool IsMatlabMapShape(JgsValue value, out int count)
    {
        if (value.Type == JgsType.Struct && value.ClassName == MapClassName && JgsRunningDialect.ThreadIsMatlab)
        {
            count = EntryCount(value);
            return true;
        }

        count = 0;
        return false;
    }

    /// <summary>A <c>containers.Map</c>'s Count is a uint64, as MATLAB's (V6.17, measured).</summary>
    private static JgsValue CountOf(int count) =>
        JgsNumericClasses.Stamp(JgsValue.Number(count), JgsNumericClass.UInt64);

    /// <summary>A dictionary's keys as a column of their own kind - text or numbers (V6.17, measured).</summary>
    internal static JgsValue DictionaryKeys(JgsValue map)
    {
        JgsValue[] stored = KeyCell(map);
        if (stored.Length == 0)
        {
            return map.AsStruct["KeyType"].AsString == "char"
                ? JgsValue.StringArray([], 0, 1)
                : JgsEmpty.Shaped(0, 1);
        }

        if (IsTextScalar(stored[0]))
        {
            return JgsValue.StringArray(System.Array.ConvertAll(stored, k => JgsValue.Str(TextOf(k))), stored.Length, 1);
        }

        // Keys of an integer class stay of it, as values do (a .NET Dictionary<Int32, …>'s: ADR 0177).
        JgsValue column = JgsMatrix.FromElements([.. stored], stored.Length, 1);
        if (stored[0].NumericClass != JgsNumericClass.Double)
        {
            column.SetNumericClass(stored[0].NumericClass);
        }

        return column;
    }

    /// <summary>
    /// A dictionary's values as a column of their kind (V6.17, measured): numbers as a numeric
    /// column, strings as a string column, a cell-valued dictionary's as a cell column of the
    /// contents, anything else as a cell of shares. M2: each value handed out is a share.
    /// </summary>
    // audit: mints — every arm builds its answer here; the one .NET value gets a wrapper of its own (open item 38)
    internal static JgsValue DictionaryValues(JgsValue map)
    {
        JgsValue[] stored = ValueCell(map);
        int count = stored.Length;
        if (count == 0)
        {
            return map.AsStruct["ValueType"].AsString switch
            {
                "string" => JgsValue.StringArray([], 0, 1),
                "cell" => ShapedCell([], 0, 1),
                "any" => JgsValue.Array([]),
                _ => JgsEmpty.Shaped(0, 1),
            };
        }

        if (System.Array.TrueForAll(stored, static v => v.Type is JgsType.Number or JgsType.Bool))
        {
            // The column is minted here, so it is tagged at mint time (the scalars already hold
            // values of their class); the audit adopts values because every return is fresh.
            JgsValue column = JgsMatrix.FromElements(System.Array.ConvertAll(stored, JgsValue.Share), count, 1);
            JgsNumericClass numericClass = stored[0].NumericClass;
            if (numericClass != JgsNumericClass.Double)
            {
                column.SetNumericClass(numericClass);
            }

            return column;
        }

        if (System.Array.TrueForAll(stored, static v => v.IsStringArray && v.ArrayLength == 1))
        {
            return JgsValue.StringArray(System.Array.ConvertAll(stored, static v => v.ElementAt(0)), count, 1);
        }

        if (System.Array.TrueForAll(stored, static v => v.Type == JgsType.Cell && v.AsCell.Length == 1))
        {
            return ShapedCell(System.Array.ConvertAll(stored, static v => JgsValue.Share(v.AsCell[0])), count, 1);
        }

        // One .NET value is itself, as R2025b's values of a Dictionary<Int32, String> (ADR 0177): the
        // same object in a wrapper of its own, so the answer is minted here and values may be adopted.
        if (count == 1 && stored[0].Type == JgsType.External)
        {
            return JgsValue.External(stored[0].AsExternal);
        }

        return ShapedCell(System.Array.ConvertAll(stored, JgsValue.Share), count, 1);
    }

    private static JgsValue ShapedCell(JgsValue[] elements, int rows, int cols)
    {
        JgsValue cell = JgsValue.Cell(elements);
        cell.Reshape(rows, cols);
        return cell;
    }

    /// <summary>
    /// <c>d.name</c> on a dictionary (V6.17, measured): the verbs that take the dictionary alone -
    /// <c>d.keys</c>, <c>d.values</c>, <c>d.numEntries</c>, <c>d.entries</c>, <c>d.isConfigured</c> -
    /// are called with it; any other name, its storage fields included, is refused in MATLAB's
    /// words, because a dictionary has no properties.
    /// </summary>
    internal static JgsValue DictionaryMember(JgsValue map, string field, int line, int col) => field switch
    {
        "keys" => DictionaryKeys(map),
        "values" => DictionaryValues(map),
        "numEntries" => JgsValue.Number(EntryCount(map)),
        "entries" => Entries(map),
        "isConfigured" => JgsValue.Bool(IsConfigured(map)),
        _ => throw new JgsRuntimeException(line, col,
            $"Unrecognized method, property, or field '{field}' for class 'dictionary'."),
    };

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
        if (map.ClassName == DictionaryClassName && KeyCell(map).Length == 0 && fields["ValueType"].AsString == "any")
        {
            fields["ValueType"] = JgsValue.Str(ValueKind(value)); // the first entry settles the kind
        }

        // A containers.Map keeps its keys sorted, so keys and values answer in that order (stage K8:
        // R2025b's keys(m) is 'Alpha Zeta' after m('Zeta') then m('Alpha')); a dictionary keeps the order of insertion.
        JgsValue[] held = KeyCell(map);
        int place = held.Length;
        if (map.ClassName == MapClassName)
        {
            place = 0;
            while (place < held.Length && CompareKeys(held[place], storedKey) < 0)
            {
                place++;
            }
        }

        fields["keys"] = JgsValue.Cell([.. held[..place], storedKey, .. held[place..]]);
        JgsValue[] values = ValueCell(map);
        ReplaceValueCell(map, fields, JgsValue.Cell([.. values[..place], value, .. values[place..]]));
        fields["Count"] = CountOf(KeyCell(map).Length);
    }

    /// <summary>A Map's key order: text by its characters' codes, numbers by value.</summary>
    private static int CompareKeys(JgsValue a, JgsValue b) =>
        IsTextScalar(a) && IsTextScalar(b) ? string.CompareOrdinal(TextOf(a), TextOf(b))
        : !IsTextScalar(a) && !IsTextScalar(b) ? a.AsNumber.CompareTo(b.AsNumber)
        : 0;

    /// <summary>
    /// Puts a rebuilt value cell into the collection (V10, ADR 0171): the old cell's hold on its
    /// values goes, the new one's is counted, so the entries live exactly as long as the
    /// collection does (measured: <c>clear m</c> runs a Map entry's destructor).
    /// </summary>
    private static void ReplaceValueCell(JgsValue map, Dictionary<string, JgsValue> fields, JgsValue rebuilt)
    {
        fields.TryGetValue("values", out JgsValue? old);
        fields["values"] = rebuilt;
        JgsLifetime.Stored(map, old, rebuilt);
    }

    /// <summary>
    /// Removes the entries <paramref name="keys"/> name, in place as <see cref="Put"/> writes -
    /// <c>d(key) = []</c> (V6.17). A key the collection does not have is nothing to remove
    /// (measured: <c>d(9) = []</c> leaves the dictionary as it was).
    /// </summary>
    internal static void Remove(JgsValue map, JgsValue keys)
    {
        foreach (JgsValue key in KeysAsked(keys))
        {
            int at = FindKey(map, key);
            if (at >= 0)
            {
                RemoveAt(map, at);
            }
        }
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
        ReplaceValueCell(map, fields, JgsValue.Cell([.. keptValues]));
        fields["Count"] = CountOf(keptKeys.Count);
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

        // A 1-by-1 numeric array ([1.5], a computed key) is the one number it holds: Put refused it as
        // an array, so containers.Map([1.5], 2) could not be made (open item 317, ADR 0358).
        if (value.Type == JgsType.Array && !value.IsStringArray && !value.IsCharMatrix && value.ArrayLength == 1
            && value.TimeTag is null && !value.IsCategorical)
        {
            return value.BoxedElements();
        }

        return [value];
    }

    private static string KeyText(JgsValue key) =>
        IsTextScalar(key) ? $"'{TextOf(key)}'" : key.Display();

    private static JgsValue RequireKeyed(string name, JgsValue value, int line, int col) =>
        IsKeyedCollection(value)
            ? value
            : throw new JgsRuntimeException(line, col,
                $"{name} expects a containers.Map or a dictionary, but got a {value.TypeName}.");

    // --- keyHash ---------------------------------------------------------------------------------

    /// <summary><c>keyHash</c>'s 64 bits: FNV-1a over the class, the size and the content.</summary>
    private static ulong KeyHashOf(JgsValue value)
    {
        var hash = new KeyHasher();
        hash.Add(ClassOf(value, JgsDialect.Matlab));
        foreach (int d in SizeDims(value))
        {
            hash.Add(d);
        }

        AddKeyContent(ref hash, value);
        return hash.Value;
    }

    private static void AddKeyContent(ref KeyHasher hash, JgsValue value)
    {
        switch (value.Type)
        {
            case JgsType.Number or JgsType.Bool:
                hash.Add(value.AsNumber);
                break;
            case JgsType.Complex:
                hash.Add(value.AsComplex.Real);
                hash.Add(value.AsComplex.Imaginary);
                break;
            case JgsType.String:
                hash.Add(value.AsString);
                break;
            case JgsType.Array:
                foreach (JgsValue element in value.BoxedElements())
                {
                    AddKeyContent(ref hash, element);
                }

                break;
            case JgsType.Cell:
                foreach (JgsValue element in value.AsCell)
                {
                    hash.Add(KeyHashOf(element));
                }

                break;
            case JgsType.Struct:
                IEnumerable<Dictionary<string, JgsValue>> records = value.IsStructArray ? value.AsStructArray.Elements : [value.AsStruct];
                foreach (Dictionary<string, JgsValue> element in records)
                {
                    foreach ((string name, JgsValue field) in element.OrderBy(static f => f.Key, StringComparer.Ordinal))
                    {
                        hash.Add(name);
                        hash.Add(KeyHashOf(field));
                    }
                }

                break;
            case JgsType.Object:
                IEnumerable<JgsObject> instances = value.IsObjectArray ? value.AsObjectArray.Elements : [value.AsObject];
                foreach (JgsObject instance in instances)
                {
                    if (instance.Class.IsHandle)
                    {
                        hash.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(instance)); // a handle is its identity
                    }
                    else if (instance.EnumerationName is { } member)
                    {
                        hash.Add(member);
                    }
                    else
                    {
                        foreach ((string name, JgsValue field) in instance.Fields.OrderBy(static f => f.Key, StringComparer.Ordinal))
                        {
                            hash.Add(name);
                            hash.Add(KeyHashOf(field));
                        }
                    }
                }

                break;
        }
    }

    /// <summary>FNV-1a, 64 bits; -0 hashes as 0 and every NaN alike, since they compare so as keys.</summary>
    private struct KeyHasher
    {
        public KeyHasher()
        {
        }

        public ulong Value { get; private set; } = 14695981039346656037UL;

        public void Add(ulong bits)
        {
            for (int i = 0; i < 8; i++)
            {
                Value = (Value ^ ((bits >> (8 * i)) & 0xFF)) * 1099511628211UL;
            }
        }

        public void Add(int number) => Add((ulong)(uint)number);

        public void Add(double number) =>
            Add((ulong)BitConverter.DoubleToInt64Bits(number == 0 ? 0.0 : double.IsNaN(number) ? double.NaN : number));

        public void Add(string text)
        {
            Add(text.Length);
            foreach (char c in text)
            {
                Add((ulong)c);
            }
        }
    }
}
