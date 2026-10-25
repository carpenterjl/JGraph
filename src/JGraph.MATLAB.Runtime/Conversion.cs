using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using JGraph.MATLAB.Exceptions;
using JGraph.Runtime;
using JGraph.Scripting.Jgs;

namespace JGraph.MATLAB.Types;

/// <summary>
/// The MATLAB Data API's conversions (stage K9), as k9client measured R2025b's: a MATLAB value to
/// the .NET type a cast names - a scalar to its own type and to every type C# widens it to implicitly
/// (and to Complex), an array only to arrays of its own element type, in a rank its size fits - and
/// a .NET value to the MATLAB value it stands for. A conversion R2025b has no rule for is refused in
/// its words.
/// </summary>
internal static class Conversion
{
    /// <summary>The .NET element type each MATLAB numeric class converts to.</summary>
    private static readonly Dictionary<string, Type> NumericTypes = new(StringComparer.Ordinal)
    {
        ["double"] = typeof(double), ["single"] = typeof(float), ["int8"] = typeof(sbyte), ["uint8"] = typeof(byte),
        ["int16"] = typeof(short), ["uint16"] = typeof(ushort), ["int32"] = typeof(int), ["uint32"] = typeof(uint),
        ["int64"] = typeof(long), ["uint64"] = typeof(ulong),
    };

    /// <summary>C#'s implicit numeric conversions, the scalar conversions R2025b allows (k9client: out.int8.long and the rest).</summary>
    private static readonly Dictionary<Type, Type[]> Widenings = new()
    {
        [typeof(sbyte)] = [typeof(short), typeof(int), typeof(long), typeof(float), typeof(double), typeof(decimal)],
        [typeof(byte)] = [typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal)],
        [typeof(short)] = [typeof(int), typeof(long), typeof(float), typeof(double), typeof(decimal)],
        [typeof(ushort)] = [typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal)],
        [typeof(int)] = [typeof(long), typeof(float), typeof(double), typeof(decimal)],
        [typeof(uint)] = [typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal)],
        [typeof(long)] = [typeof(float), typeof(double), typeof(decimal)],
        [typeof(ulong)] = [typeof(float), typeof(double), typeof(decimal)],
        [typeof(float)] = [typeof(double)],
        [typeof(double)] = [],
    };

    // --- MATLAB to .NET ------------------------------------------------------------------------

    /// <summary>R2025b's refusal of a conversion it has no rule for.</summary>
    public static InvalidCastException NotSupported(MatlabValue value, Type target) =>
        new($"Conversion from MATLAB {value.Description} to {target.FullName} is not supported.");

    /// <summary>
    /// <paramref name="value"/> as <paramref name="target"/>, or the reason it cannot be; null and false
    /// when the cast is the CLR's to make (a type the wrapper is not, which then fails in .NET's words).
    /// </summary>
    public static bool TryConvert(MATLABArray wrapper, MatlabValue value, Type target, out object result)
    {
        result = null;
        if (target.IsInstanceOfType(wrapper))
        {
            result = wrapper;
            return true;
        }

        if (IsTuple(target))
        {
            throw new InvalidCastException($"Unable to convert to {target} from 1 MATLAB outputs.");
        }

        if (wrapper is MATLABStruct or MATLABObject)
        {
            return ConvertScalarHolder(wrapper, value, target, out result);
        }

        // A plain array's cast to a MATLAB type it is not, or to an interface, is the CLR's (R2025b
        // answers "Unable to cast object of type ..." there).
        if (typeof(MATLABArray).IsAssignableFrom(target) || target.IsInterface)
        {
            return false;
        }

        result = Convert(wrapper.Provider, value, target);
        return true;
    }

    /// <summary>A struct's or object's own conversions (k9client: out.struct.*, out.point.*).</summary>
    private static bool ConvertScalarHolder(MATLABArray wrapper, MatlabValue value, Type target, out object result)
    {
        result = null;
        if (wrapper is MATLABStruct)
        {
            if (target == typeof(MATLABStruct[]))
            {
                result = new MATLABStruct[] { (MATLABStruct)wrapper };
                return true;
            }

            throw NotSupported(value, target);
        }

        // A generated class stands for the object through its own implicit operator, which the binder finds.
        if (target.GetCustomAttribute<MATLABClassAttribute>() is not null)
        {
            return false;
        }

        if (target == typeof(object[]))
        {
            result = new object[] { wrapper };
            return true;
        }

        if (target == typeof(object[,]))
        {
            result = new object[,] { { wrapper } };
            return true;
        }

        if (target == typeof(MATLABObject[]))
        {
            result = new MATLABObject[] { (MATLABObject)wrapper };
            return true;
        }

        if (value.IsDictionary && DictionaryTypes(target) is { } kv)
        {
            result = ToDictionary(wrapper.Provider, value, kv.Key, kv.Value, target);
            return true;
        }

        throw NotSupported(value, target);
    }

    /// <summary>The value as <paramref name="target"/> by R2025b's table; the tuple and wrapper casts are the callers'.</summary>
    public static object Convert(MATLABProvider provider, MatlabValue value, Type target)
    {
        if (target.IsArray)
        {
            return ToArray(provider, value, target);
        }

        switch (value.Kind)
        {
            case MatlabKind.Numeric:
            {
                Type own = value.IsComplex ? typeof(Complex) : NumericTypes[value.ClassName];
                bool allowed = target == own || target == typeof(Complex) && !value.IsComplex
                    || (!value.IsComplex && Widenings.TryGetValue(own, out Type[] wider) && Array.IndexOf(wider, target) >= 0);
                if (!allowed)
                {
                    throw NotSupported(value, target);
                }

                if (!value.IsScalar)
                {
                    throw new InvalidCastException($"Unable to convert from nonscalar MATLAB array to {own.FullName}.");
                }

                var numeric = (RuntimeNumeric)value.Data;
                return target == typeof(Complex)
                    ? new Complex(numeric.Real[0], numeric.Imaginary?[0] ?? 0)
                    : NumberAs(numeric.Real[0], value.ClassName, target);
            }

            case MatlabKind.Logical when target == typeof(bool):
                return value.IsScalar
                    ? ((RuntimeLogical)value.Data).Elements[0]
                    : throw new InvalidCastException("Unable to convert from nonscalar MATLAB array to System.Boolean.");

            case MatlabKind.Char when target == typeof(char):
                return value.IsScalar
                    ? ((RuntimeChar)value.Data).Characters[0]
                    : throw new InvalidCastException("Unable to convert from nonscalar MATLAB array to System.Char.");

            case MatlabKind.Char when target == typeof(string):
                if (value.Count == 0)
                {
                    return string.Empty;
                }

                return value.Dims.Length == 2 && (value.Dims[0] == 1 || value.Dims[1] == 1)
                    ? ((RuntimeChar)value.Data).Characters
                    : throw new InvalidCastException("Unable to convert from nonvector MATLAB Array to System.String.");

            case MatlabKind.String when target == typeof(string):
                return value.IsScalar
                    ? ((RuntimeString)value.Data).Elements[0]
                    : throw new InvalidCastException("Unable to convert from nonscalar MATLAB array to System.String.");

            case MatlabKind.Enumeration when target.IsEnum && EnumName(target) == value.ClassName:
                return value.IsScalar
                    ? EnumMember(target, value.EnumNames[0])
                    : throw new InvalidCastException($"Unable to convert from nonscalar MATLAB array to {target.FullName}.");
        }

        throw NotSupported(value, target);
    }

    /// <summary>A real number as the .NET type <paramref name="target"/>, from MATLAB class <paramref name="cls"/>.</summary>
    private static object NumberAs(double number, string cls, Type target) => target switch
    {
        _ when target == typeof(double) => number,
        _ when target == typeof(float) => (float)number,
        _ when target == typeof(decimal) => (decimal)number,
        _ when target == typeof(sbyte) => (sbyte)number,
        _ when target == typeof(byte) => (byte)number,
        _ when target == typeof(short) => (short)number,
        _ when target == typeof(ushort) => (ushort)number,
        _ when target == typeof(int) => (int)number,
        _ when target == typeof(uint) => (uint)number,
        _ when target == typeof(long) => cls == "uint64" ? (long)(ulong)number : (long)number,
        _ when target == typeof(ulong) => number >= 18446744073709551615.0 ? ulong.MaxValue : (ulong)number,
        _ => throw new InvalidCastException($"Conversion from MATLAB {cls} scalar to {target.FullName} is not supported."),
    };

    /// <summary>The MATLAB enumeration a .NET enum stands for, by its MATLABEnum attribute, or null.</summary>
    private static string EnumName(Type type) => type.GetCustomAttribute<MATLABEnumAttribute>()?.Name;

    private static object EnumMember(Type target, string name) =>
        Enum.TryParse(target, name, ignoreCase: false, out object member)
            ? member
            : throw new InvalidCastException($"The MATLAB enumeration member '{name}' is not a member of {target.FullName}.");

    /// <summary>An array of <paramref name="target"/>'s element type and rank, or R2025b's refusal (k9client: out.*.double[,] and the rest).</summary>
    private static Array ToArray(MATLABProvider provider, MatlabValue value, Type target)
    {
        Type element = target.GetElementType()!;
        int rank = target.GetArrayRank();
        bool memory; // numeric, logical and char arrays are copied as memory, and say "nonvector" for a rank-1 miss
        Func<int, object> item;
        switch (value.Kind)
        {
            case MatlabKind.Numeric when element == (value.IsComplex ? typeof(Complex) : NumericTypes[value.ClassName])
                || (element == typeof(Complex) && value.ClassName == "double"):
            {
                var numeric = (RuntimeNumeric)value.Data;
                memory = true;
                item = element == typeof(Complex)
                    ? i => new Complex(numeric.Real[i], numeric.Imaginary?[i] ?? 0)
                    : i => NumberAs(numeric.Real[i], value.ClassName, element);
                break;
            }

            case MatlabKind.Logical when element == typeof(bool):
            {
                var logical = (RuntimeLogical)value.Data;
                memory = true;
                item = i => logical.Elements[i];
                break;
            }

            case MatlabKind.Char when element == typeof(char):
            {
                string chars = ((RuntimeChar)value.Data).Characters;
                memory = true;
                item = i => chars[i];
                break;
            }

            case MatlabKind.String when element == typeof(string):
            {
                var strings = (RuntimeString)value.Data;
                memory = false;
                item = i => strings.Elements[i];
                break;
            }

            case MatlabKind.Cell when element == typeof(string):
                // The rank is checked before the contents (k9client: out.cellm.string[]).
                _ = NetDims(value.Dims, rank, memory: false, target);
                if (!Array.TrueForAll(value.Cells, static c => c.Kind == MatlabKind.Char && (c.Count == 0 || c.Dims[0] == 1)))
                {
                    throw new InvalidCastException($"Unable to convert from non-cellstring MALTAB array to {target.FullName}.");
                }

                memory = false;
                item = i => ((RuntimeChar)value.Cells[i].Data).Characters;
                break;

            case MatlabKind.Cell when element == typeof(object):
                memory = false;
                item = i => MATLABArray.Wrap(provider, value.Cells[i]);
                break;

            case MatlabKind.Struct when element == typeof(MATLABStruct):
                memory = false;
                item = i => new MATLABStruct(provider, value, i);
                break;

            case MatlabKind.Enumeration when element.IsEnum && EnumName(element) == value.ClassName:
                memory = false;
                item = i => EnumMember(element, value.EnumNames[i]);
                break;

            case MatlabKind.ValueObject or MatlabKind.HandleObject when element == typeof(object) || element == typeof(MATLABObject):
            {
                MatlabValue[] objects = value.IsScalar ? [value] : provider.ObjectElements(value);
                memory = false;
                item = i => objects[i].Count == 1 ? new MATLABObject(provider, objects[i]) : MATLABArray.Wrap(provider, objects[i]);
                break;
            }

            default:
                throw NotSupported(value, target);
        }

        int[] dims = NetDims(value.Dims, rank, memory, target);
        Array made = Array.CreateInstance(element, dims);
        int count = value.Count;
        var subscripts = new int[rank];
        for (int linear = 0; linear < count; linear++)
        {
            // MATLAB's column-major position in .NET's dimensions: the first subscript moves fastest.
            int rest = linear;
            for (int d = 0; d < rank; d++)
            {
                subscripts[d] = dims[d] == 0 ? 0 : rest % dims[d];
                rest = dims[d] == 0 ? 0 : rest / dims[d];
            }

            made.SetValue(item(linear), subscripts);
        }

        return made;
    }

    /// <summary>
    /// A MATLAB size in <paramref name="rank"/> .NET dimensions: padded with trailing ones, or with
    /// leading ones removed, and a vector of either orientation one dimension (k9client: out.trailing,
    /// out.column, out.empty).
    /// </summary>
    private static int[] NetDims(int[] matlab, int rank, bool memory, Type target)
    {
        var dims = matlab.ToList();
        if (rank == 1)
        {
            if (dims.Count == 2 && (dims[0] == 1 || dims[1] == 1))
            {
                return [dims[0] * dims[1]];
            }

            throw memory
                ? new InvalidCastException($"Unable to convert from nonvector MATLAB Array to {target.FullName}.")
                : new InvalidCastException($"Unable to convert {matlab.Length}-dimensional MATLAB array to 1-dimensional .NET array.");
        }

        while (dims.Count < rank)
        {
            dims.Add(1);
        }

        while (dims.Count > rank && dims[0] == 1)
        {
            dims.RemoveAt(0);
        }

        return dims.Count == rank
            ? [.. dims]
            : throw new InvalidCastException($"Unable to convert {matlab.Length}-dimensional MATLAB array to {rank}-dimensional .NET array.");
    }

    /// <summary>The key and value types of a dictionary type a MATLAB dictionary converts to, or null.</summary>
    private static KeyValuePair<Type, Type>? DictionaryTypes(Type target)
    {
        if (!target.IsGenericType)
        {
            return null;
        }

        Type open = target.GetGenericTypeDefinition();
        if (open != typeof(Dictionary<,>) && open != typeof(IDictionary<,>) && open != typeof(IReadOnlyDictionary<,>))
        {
            return null;
        }

        Type[] arguments = target.GetGenericArguments();
        return new KeyValuePair<Type, Type>(arguments[0], arguments[1]);
    }

    /// <summary>A MATLAB dictionary as a <c>Dictionary&lt;K, V&gt;</c>, its keys and values each converted (k9client: out.dict.*).</summary>
    private static object ToDictionary(MATLABProvider provider, MatlabValue value, Type key, Type item, Type target)
    {
        var made = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(key, item))!;

        // With no entries there is nothing to convert, whatever the types (k9client: out.dictconfigured).
        if (value.DictionaryKeys is not { Count: > 0 } keys)
        {
            return made;
        }

        // R2025b converts entry by entry and names the entry's MATLAB type (k9client: out.dict.Dictionary<double,string>).
        static InvalidCastException Entry(MatlabValue part, Type type) =>
            new($"Conversion of a MATLAB dictionary entry from MATLAB type {part.ClassName} to {type.FullName} is not supported.");
        Array keyArray;
        Array itemArray;
        try
        {
            keyArray = ToArray(provider, keys, key.MakeArrayType());
        }
        catch (InvalidCastException)
        {
            throw Entry(keys, key);
        }

        try
        {
            itemArray = value.DictionaryValues.Kind == MatlabKind.Cell && item == typeof(object)
                ? ToArray(provider, value.DictionaryValues, typeof(object[]))
                : ToArray(provider, value.DictionaryValues, item.MakeArrayType());
        }
        catch (InvalidCastException)
        {
            throw Entry(value.DictionaryValues, item);
        }
        for (int i = 0; i < keyArray.Length; i++)
        {
            made[keyArray.GetValue(i)!] = itemArray.GetValue(i);
        }

        _ = target;
        return made;
    }

    /// <summary>Whether <paramref name="type"/> is a ValueTuple.</summary>
    public static bool IsTuple(Type type) =>
        type.IsValueType && type.IsGenericType && type.FullName!.StartsWith("System.ValueTuple`", StringComparison.Ordinal);

    /// <summary>How many elements a ValueTuple type holds, its nested rest counted.</summary>
    public static int TupleCount(Type type)
    {
        Type[] arguments = type.GetGenericArguments();
        return arguments.Length == 8 && IsTuple(arguments[7]) ? 7 + TupleCount(arguments[7]) : arguments.Length;
    }

    /// <summary>The element types of a ValueTuple type, its nested rest flattened.</summary>
    public static IEnumerable<Type> TupleElements(Type type)
    {
        Type[] arguments = type.GetGenericArguments();
        for (int i = 0; i < arguments.Length; i++)
        {
            if (i == 7 && IsTuple(arguments[7]))
            {
                foreach (Type nested in TupleElements(arguments[7]))
                {
                    yield return nested;
                }
            }
            else
            {
                yield return arguments[i];
            }
        }
    }

    /// <summary>A ValueTuple of <paramref name="type"/> holding <paramref name="items"/>, nesting past the seventh.</summary>
    public static object MakeTuple(Type type, IReadOnlyList<object> items)
    {
        Type[] arguments = type.GetGenericArguments();
        var values = new object[arguments.Length];
        for (int i = 0; i < arguments.Length; i++)
        {
            values[i] = i == 7 && IsTuple(arguments[7]) ? MakeTuple(arguments[7], [.. items.Skip(7)]) : items[i];
        }

        return Activator.CreateInstance(type, values)!;
    }

    /// <summary>
    /// One output as a tuple element: as any cast converts it, but an element of type object is
    /// refused (k9client: call.nargout3.tuple.dynamic), and a generated class is reached through its
    /// implicit operator.
    /// </summary>
    public static object TupleElement(MATLABArray wrapper, Type element)
    {
        if (element == typeof(object))
        {
            throw NotSupported(wrapper.Value, element);
        }

        if (element.GetCustomAttribute<MATLABClassAttribute>() is not null && wrapper is MATLABObject obj)
        {
            return ThroughOperator(element, obj);
        }

        if (TryConvert(wrapper, wrapper.Value, element, out object result))
        {
            return result;
        }

        throw new InvalidCastException($"Unable to cast object of type '{wrapper.GetType().FullName}' to type '{element.FullName}'.");
    }

    /// <summary>A generated class's instance over <paramref name="obj"/>, made by its <c>implicit operator T(MATLABObject)</c>.</summary>
    public static object ThroughOperator(Type target, MATLABObject obj)
    {
        MethodInfo op = target.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "op_Implicit" && m.ReturnType == target && m.GetParameters() is [{ ParameterType: var p }] && p == typeof(MATLABObject))
            ?? throw NotSupported(obj.Value, target);
        return op.Invoke(null, [obj])!;
    }

    // --- .NET to MATLAB ------------------------------------------------------------------------

    /// <summary>R2025b's refusal of a .NET type it has no MATLAB type for.</summary>
    public static UnsupportedTypeException Unsupported(Type type, string more = "") =>
        new($"Conversion of .NET type {type.FullName} to a native MATLAB type is not supported.{more}");

    /// <summary>The engine's value for the .NET <paramref name="value"/>, passed to <paramref name="provider"/>'s component. Engine thread.</summary>
    public static JgsValue ToEngine(object value, MATLABProvider provider, ComponentSession session)
    {
        switch (value)
        {
            case null:
                return JgsMatrix.FromColumnMajor([], 0, 0);
            case MATLABStruct { IsBuilt: true } built:
                return JgsValue.Struct(built.EngineFields(provider, session));
            case MATLABArray array:
                // Data travels to another instance as the copy it already is; an object is its own
                // component's, which R2025b serializes across with warnings and JGraph refuses
                // (k9client: second.array.from.first, second.object.from.first).
                if (!ReferenceEquals(array.Provider, provider) && ContainsOpaque(array.Value))
                {
                    throw new UnsupportedTypeException(
                        "A MATLAB object can be passed only to the MATLAB runtime instance that returned it.");
                }

                return array.ToEngine(session);
            case FevalTaskResult:
                throw new UnsupportedTypeException("Multiple MATLAB outputs must be cast to a ValueTuple and unpacked before being used as function inputs.");
            case MATLABPackage:
                throw Unsupported(value.GetType(), " Call MATLAB functions using the () operator.");
            case string text:
                return RuntimeValueConversion.ToEngine(RuntimeValue.String(text), session);
            case char c:
                return JgsValue.Str(c.ToString());
            case bool b:
                return JgsValue.Bool(b);
            case Complex z:
                return RuntimeValueConversion.ToEngine(RuntimeNumeric.Complex([z], 1, 1), session);
            case Array arrayValue:
                return ArrayToEngine(arrayValue, provider, session);
        }

        Type type = value.GetType();
        if (Scalar(value) is { } numeric)
        {
            return RuntimeValueConversion.ToEngine(numeric, session);
        }

        if (type.IsEnum)
        {
            return EnumToEngine(type, [value], [1, 1], session);
        }

        if (type.GetCustomAttribute<MATLABClassAttribute>() is not null)
        {
            return ToEngine(ToObject(value), provider, session);
        }

        if (DictionaryInterface(type) is { } dictionary)
        {
            return DictionaryToEngine((IEnumerable)value, dictionary.Key, dictionary.Value, provider, session);
        }

        throw Unsupported(type);
    }

    /// <summary>A generated class's instance as the MATLABObject its implicit operator answers.</summary>
    private static MATLABObject ToObject(object value)
    {
        Type type = value.GetType();
        MethodInfo op = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "op_Implicit" && m.ReturnType == typeof(MATLABObject) && m.GetParameters() is [{ ParameterType: var p }] && p == type)
            ?? throw Unsupported(type);
        try
        {
            return (MATLABObject)op.Invoke(null, [value])!;
        }
        catch (TargetInvocationException wrapped) when (wrapped.InnerException is { } inner)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(inner).Throw();
            throw;
        }
    }

    /// <summary>A .NET number as a 1-by-1 of its MATLAB class, or null for anything else.</summary>
    private static RuntimeNumeric Scalar(object value) => value switch
    {
        double d => RuntimeValue.Scalar(d),
        float f => RuntimeNumeric.Single([f], 1, 1),
        sbyte v => RuntimeNumeric.Int8([v], 1, 1),
        byte v => RuntimeNumeric.UInt8([v], 1, 1),
        short v => RuntimeNumeric.Int16([v], 1, 1),
        ushort v => RuntimeNumeric.UInt16([v], 1, 1),
        int v => RuntimeNumeric.Int32([v], 1, 1),
        uint v => RuntimeNumeric.UInt32([v], 1, 1),
        long v => RuntimeNumeric.Int64([v], 1, 1),
        ulong v => RuntimeNumeric.UInt64([v], 1, 1),
        _ => null,
    };

    /// <summary>A .NET array as the MATLAB array of its element type, its dimensions MATLAB's (k9client: in.doubles3).</summary>
    private static JgsValue ArrayToEngine(Array array, MATLABProvider provider, ComponentSession session)
    {
        Type element = array.GetType().GetElementType()!;
        int rank = array.Rank;
        int[] dims = rank == 1 ? [1, array.Length] : [.. Enumerable.Range(0, rank).Select(array.GetLength)];
        while (dims.Length > 2 && dims[^1] == 1)
        {
            dims = dims[..^1];
        }

        // MATLAB's column-major order over .NET's subscripts: the first subscript moves fastest.
        object[] ordered = new object[array.Length];
        int[] lengths = [.. Enumerable.Range(0, rank).Select(array.GetLength)];
        var subscripts = new int[rank];
        for (int linear = 0; linear < ordered.Length; linear++)
        {
            int rest = linear;
            for (int d = 0; d < rank; d++)
            {
                subscripts[d] = rest % lengths[d];
                rest /= lengths[d];
            }

            ordered[linear] = array.GetValue(subscripts);
        }

        RuntimeValue made = element switch
        {
            _ when element == typeof(double) => RuntimeNumeric.Of([.. ordered.Cast<double>()], dims),
            _ when element == typeof(float) => RuntimeNumeric.Single([.. ordered.Cast<float>()], dims),
            _ when element == typeof(sbyte) => RuntimeNumeric.Int8([.. ordered.Cast<sbyte>()], dims),
            _ when element == typeof(byte) => RuntimeNumeric.UInt8([.. ordered.Cast<byte>()], dims),
            _ when element == typeof(short) => RuntimeNumeric.Int16([.. ordered.Cast<short>()], dims),
            _ when element == typeof(ushort) => RuntimeNumeric.UInt16([.. ordered.Cast<ushort>()], dims),
            _ when element == typeof(int) => RuntimeNumeric.Int32([.. ordered.Cast<int>()], dims),
            _ when element == typeof(uint) => RuntimeNumeric.UInt32([.. ordered.Cast<uint>()], dims),
            _ when element == typeof(long) => RuntimeNumeric.Int64([.. ordered.Cast<long>()], dims),
            _ when element == typeof(ulong) => RuntimeNumeric.UInt64([.. ordered.Cast<ulong>()], dims),
            _ when element == typeof(Complex) => RuntimeNumeric.Complex([.. ordered.Cast<Complex>()], dims),
            _ when element == typeof(bool) => new RuntimeLogical([.. ordered.Cast<bool>()], dims),
            _ when element == typeof(char) => new RuntimeChar(new string([.. ordered.Cast<char>()]), dims),
            _ when element == typeof(string) => new RuntimeString([.. ordered.Cast<string>()], dims),
            _ => null,
        };
        if (made is not null)
        {
            return RuntimeValueConversion.ToEngine(made, session);
        }

        if (element == typeof(object))
        {
            JgsValue cell = JgsValue.Cell([.. ordered.Select(o => ToEngine(o, provider, session))]);
            cell.ReshapeDims(dims);
            return cell;
        }

        if (element == typeof(MATLABStruct))
        {
            return StructArrayToEngine([.. ordered.Cast<MATLABStruct>()], dims, provider, session);
        }

        if (element.IsEnum)
        {
            return EnumToEngine(element, ordered, dims, session);
        }

        if (element == typeof(MATLABObject) || element.GetCustomAttribute<MATLABClassAttribute>() is not null)
        {
            MATLABObject[] objects = [.. ordered.Select(o => o as MATLABObject ?? ToObject(o))];
            if (objects.Length == 0)
            {
                throw new InvalidCastException("Creating an empty MATLAB object array is not supported.");
            }

            JgsValue[] engine = [.. objects.Select(o => ToEngine(o, provider, session))];
            if (engine.Select(static e => JgsBuiltins.ClassOf(e, JgsDialect.Matlab)).Distinct(StringComparer.Ordinal).Count() > 1)
            {
                // R2025b's words for objects of more than one class (k9client: object.array.mixed).
                throw new MATLABException("An unmanaged API returned the error code InvalidHeterogeneousArray.");
            }

            JgsValue joined = JgsBuiltins.Feval(session.Interpreter, [JgsValue.Str("horzcat"), .. engine], 1, 0, 0)[0];
            return dims[0] == 1 ? joined : JgsBuiltins.Feval(session.Interpreter, [JgsValue.Str("reshape"), joined, .. dims.Select(d => JgsValue.Number(d))], 1, 0, 0)[0];
        }

        throw Unsupported(array.GetType());
    }

    /// <summary>Whether a value holds an object anywhere: an opaque value, or a cell or struct with one inside.</summary>
    private static bool ContainsOpaque(MatlabValue value) =>
        value.Data is RuntimeOpaque
        || (value.Cells?.Any(ContainsOpaque) ?? false)
        || (value.Fields?.Any(static f => f.Values.Any(ContainsOpaque)) ?? false);

    /// <summary>MATLABStructs as a struct array; every one must have the same fields (k9client: in.structsdiffer).</summary>
    private static JgsValue StructArrayToEngine(MATLABStruct[] structs, int[] dims, MATLABProvider provider, ComponentSession session)
    {
        if (structs.Length == 0)
        {
            // R2025b refuses an empty one as though its fields differed; it is a 1-by-0 struct array of no fields.
            return JgsBuiltins.Feval(session.Interpreter,
                [JgsValue.Str("repmat"), JgsValue.EmptyStruct(), JgsValue.Number(1), JgsValue.Number(0)], 1, 0, 0)[0];
        }

        string[] names = [.. structs[0].GetFieldNames()];
        if (!Array.TrueForAll(structs, s => s.GetFieldNames().SequenceEqual(names, StringComparer.Ordinal)))
        {
            throw new InvalidCastException("All structs in a MATLAB struct array must have the same fields.");
        }

        var elements = new Dictionary<string, JgsValue>[structs.Length];
        for (int i = 0; i < structs.Length; i++)
        {
            elements[i] = structs[i].EngineFields(provider, session);
        }

        JgsValue array = JgsValue.StructArray(elements);
        array.ReshapeDims(dims);
        return array;
    }

    /// <summary>.NET enum members as the members of the MATLAB enumeration their attribute names.</summary>
    private static JgsValue EnumToEngine(Type type, object[] members, int[] dims, ComponentSession session)
    {
        string name = EnumName(type) ?? throw Unsupported(type);
        if (session.Interpreter.ClassForLoad(name) is not { IsEnumeration: true } definition)
        {
            throw new MATLABExecutionException($"Class {name} not found.");
        }

        var values = new JgsValue[members.Length];
        for (int i = 0; i < members.Length; i++)
        {
            string member = members[i]!.ToString()!;
            values[i] = definition.TryMember(member, out JgsValue found)
                ? found
                : throw new MATLABExecutionException($"No enumeration member named '{member}' in class '{name}'.");
        }

        if (values.Length == 1)
        {
            return values[0];
        }

        JgsValue joined = JgsBuiltins.Feval(session.Interpreter, [JgsValue.Str("horzcat"), .. values], 1, 0, 0)[0];
        return dims[0] == 1 ? joined : JgsBuiltins.Feval(session.Interpreter, [JgsValue.Str("reshape"), joined, .. dims.Select(d => JgsValue.Number(d))], 1, 0, 0)[0];
    }

    /// <summary>The key and value types of the <c>IDictionary&lt;K, V&gt;</c> a type implements, or null.</summary>
    private static KeyValuePair<Type, Type>? DictionaryInterface(Type type)
    {
        Type[] found = [.. type.GetInterfaces().Where(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>))];
        if (found.Length != 1)
        {
            return null;
        }

        Type[] arguments = found[0].GetGenericArguments();
        return new KeyValuePair<Type, Type>(arguments[0], arguments[1]);
    }

    /// <summary>
    /// A .NET dictionary as a MATLAB dictionary (k9client: in.dict*): its keys and values each as one
    /// array of their element type, a char key a string, values of type object a cell; an empty
    /// dictionary an unconfigured one.
    /// </summary>
    private static JgsValue DictionaryToEngine(IEnumerable pairs, Type key, Type item, MATLABProvider provider, ComponentSession session)
    {
        var keys = new List<object>();
        var items = new List<object>();
        foreach (object pair in pairs)
        {
            Type kv = pair.GetType();
            keys.Add(kv.GetProperty("Key")!.GetValue(pair));
            items.Add(kv.GetProperty("Value")!.GetValue(pair));
        }

        if (keys.Count == 0)
        {
            return JgsBuiltins.Feval(session.Interpreter, [JgsValue.Str("dictionary")], 1, 0, 0)[0];
        }

        JgsValue Column(List<object> values, Type type)
        {
            if (type == typeof(char))
            {
                return ToEngine(values.Select(static v => v.ToString()).ToArray(), provider, session);
            }

            Array typed = Array.CreateInstance(type, values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                typed.SetValue(values[i], i);
            }

            return ToEngine(typed, provider, session);
        }

        return JgsBuiltins.Feval(session.Interpreter, [JgsValue.Str("dictionary"), Column(keys, key), Column(items, item)], 1, 0, 0)[0];
    }
}
