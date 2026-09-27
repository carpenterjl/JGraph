using System.Numerics;

namespace JGraph.Scripting.Jgs.Net;

/// <summary>
/// The two conversion tables of MATLAB's .NET interface (interop plan, stage 1, ADR 0174): which
/// .NET parameter types a MATLAB value reaches and in what order it prefers them, how it becomes
/// the .NET value, and what a .NET value becomes when it comes back.
/// </summary>
/// <remarks>
/// <para>
/// <b>The preference order is recorded, not scored.</b> Step 0 probed every MATLAB class against a
/// method per .NET parameter type and against every pair of overloads
/// (<c>net_conversions</c>, <c>tools/interop/summarize-overloads.py</c>). For each value the
/// pairwise answers form a strict order, so each row of <see cref="Orders"/> is that order as
/// R2025b chose it, and a value's rank for a parameter is its position in the row. Parameter types
/// the probe did not cover are placed by rule: a <c>Nullable&lt;T&gt;</c> just after <c>T</c>, an array
/// of a class's own element type first among that class's arrays, and a .NET object by how far its
/// type is from the parameter's.
/// </para>
/// <para>
/// <b>Returns stay .NET where MATLAB keeps them.</b> Integer returns keep their class, a
/// <c>System.String</c> stays an object (<c>char</c> converts it), <c>null</c> is <c>[]</c>, and
/// <c>Decimal</c>, <c>DateTime</c>, <c>IntPtr</c>, arrays and every other type come back as
/// objects. A 64-bit integer past 2^53 cannot be held exactly by JGraph's numbers (ADR 0069,
/// 0156): the value comes back rounded, with the <c>JGraph:interop:int64Precision</c> warning.
/// </para>
/// </remarks>
internal static class NetConvert
{
    /// <summary>The rank a .NET object gets for a parameter it only reaches as <c>System.Object</c>.</summary>
    private const double ObjectRank = 1000;

    /// <summary>R2025b's preference order per MATLAB value category (net_conversions, step 0).</summary>
    private static readonly Dictionary<string, string[]> Orders = new(StringComparer.Ordinal)
    {
        ["double"] = ["Double", "Single", "DoubleArr", "Decimal", "Double2D", "Int64", "UInt64", "Int32", "NullableInt32", "UInt32", "Int32Arr", "Int16", "UInt16", "SByte", "Byte", "Object"],
        ["double.vector"] = ["DoubleArr", "Double2D", "Int32Arr", "Object"],
        ["double.matrix"] = ["Double2D", "Object"],
        ["empty"] = ["String", "DoubleArr", "Int32Arr", "StringArr", "ObjectArr", "BooleanArr", "CharArr", "Double2D", "Array", "NullableInt32", "Object"],
        ["single"] = ["Single", "Double", "Decimal", "DoubleArr", "Double2D", "Object"],
        ["int8"] = ["SByte", "Int16", "Int32", "NullableInt32", "Int64", "Int32Arr", "Single", "Double", "DoubleArr", "Double2D", "Object"],
        ["uint8"] = ["Byte", "UInt16", "UInt32", "UInt64", "Single", "Double", "DoubleArr", "Double2D", "Object"],
        ["int16"] = ["Int16", "Int32", "NullableInt32", "Int64", "Int32Arr", "Single", "Double", "DoubleArr", "Double2D", "Object"],
        ["uint16"] = ["UInt16", "UInt32", "UInt64", "Single", "Double", "DoubleArr", "Double2D", "Object"],
        ["int32"] = ["Int32", "NullableInt32", "Int64", "Int32Arr", "Single", "Double", "DoubleArr", "Double2D", "Object"],
        ["uint32"] = ["UInt32", "UInt64", "Single", "Double", "DoubleArr", "Double2D", "Object"],
        ["int64"] = ["Int64", "Double", "DoubleArr", "Double2D", "Object"],
        ["uint64"] = ["UInt64", "Double", "DoubleArr", "Double2D", "Object"],
        ["int32.vector"] = ["Int32Arr", "DoubleArr", "Double2D", "Object"],
        ["logical"] = ["Boolean", "Byte", "BooleanArr", "SByte", "Int16", "UInt16", "Int32", "NullableInt32", "UInt32", "Int32Arr", "Int64", "UInt64", "Single", "Double", "Object", "DoubleArr", "Double2D"],
        ["logical.vector"] = ["BooleanArr", "Int32Arr", "DoubleArr", "Object", "Double2D"],
        ["char"] = ["Char", "String", "CharArr", "Object"],
        ["char.row"] = ["CharArr", "String", "Object"],
        ["char.matrix"] = ["Object"],
        ["char.empty"] = ["CharArr", "String", "Object"],
        ["string"] = ["String", "Object"],
        ["string.vector"] = ["StringArr", "Object"],
        ["cell.str"] = ["StringArr", "ObjectArr", "Object"],
        ["cell.mixed"] = ["ObjectArr", "Object"],
        ["function"] = ["Object"],
    };

    /// <summary>The .NET element type each MATLAB numeric class converts to.</summary>
    private static Type ElementType(JgsNumericClass numericClass) => numericClass switch
    {
        JgsNumericClass.Single => typeof(float),
        JgsNumericClass.Int8 => typeof(sbyte),
        JgsNumericClass.Int16 => typeof(short),
        JgsNumericClass.Int32 => typeof(int),
        JgsNumericClass.Int64 => typeof(long),
        JgsNumericClass.UInt8 => typeof(byte),
        JgsNumericClass.UInt16 => typeof(ushort),
        JgsNumericClass.UInt32 => typeof(uint),
        JgsNumericClass.UInt64 => typeof(ulong),
        _ => typeof(double),
    };

    // --- MATLAB to .NET ------------------------------------------------------------------------

    /// <summary>
    /// How much <paramref name="value"/> likes a parameter of <paramref name="parameter"/> — lower is
    /// preferred — or null when it cannot be passed there at all.
    /// </summary>
    public static double? Rank(JgsValue value, Type parameter)
    {
        if (parameter.IsByRef || parameter.IsPointer || parameter.IsByRefLike || parameter.ContainsGenericParameters)
        {
            return null;
        }

        if (value.Type == JgsType.External)
        {
            return value.AsExternal is NetObject net ? RankObject(net, parameter) : null;
        }

        string? category = Category(value);
        if (category is null)
        {
            return null;
        }

        string[] order = Orders.TryGetValue(category, out string[]? known) ? known : GenericOrder(category);
        string? key = Key(parameter);
        if (key is null)
        {
            return null;
        }

        int at = Array.IndexOf(order, key);
        if (at >= 0)
        {
            return at;
        }

        // Nullable<T> sits just after T (the recorded rows place Nullable<Int32> so), and an empty
        // reaches any Nullable where it reaches Nullable<Int32>.
        if (key.StartsWith("Nullable", StringComparison.Ordinal))
        {
            int under = Array.IndexOf(order, category == "empty" ? "NullableInt32" : key["Nullable".Length..]);
            return under >= 0 ? under + 0.5 : null;
        }

        return null;
    }

    /// <summary>A .NET object reaches its own type first, then each base, its interfaces, and Object last.</summary>
    private static double? RankObject(NetObject net, Type parameter)
    {
        Type? underlying = Nullable.GetUnderlyingType(parameter);
        if (underlying is not null && net.NullableOf is null)
        {
            parameter = underlying;
        }

        if (net.Target is null ? parameter.IsValueType && underlying is null : !parameter.IsInstanceOfType(net.Target))
        {
            return null;
        }

        if (parameter == typeof(object))
        {
            return ObjectRank;
        }

        int distance = 0;
        for (Type? t = net.Type; t is not null; t = t.BaseType, distance++)
        {
            if (t == parameter)
            {
                return distance;
            }
        }

        return parameter.IsInterface ? 100 : 500;
    }

    /// <summary>The order for a category the probe did not cover: the class's own array first.</summary>
    private static string[] GenericOrder(string category)
    {
        int dot = category.IndexOf('.', StringComparison.Ordinal);
        string numeric = dot < 0 ? category : category[..dot];
        string element = Key(ElementType(JgsNumericClasses.Parse(numeric) ?? JgsNumericClass.Double))!;
        return category.EndsWith(".matrix", StringComparison.Ordinal)
            ? [element + "2D", "Double2D", "Object"]
            : [element + "Arr", "DoubleArr", "Double2D", "Object"];
    }

    /// <summary>
    /// The row of <see cref="Orders"/> a MATLAB value belongs to: its class, and whether it is a
    /// scalar, a vector, a matrix or empty. Null for a value nothing on the .NET side accepts.
    /// </summary>
    private static string? Category(JgsValue value)
    {
        if (value.Type == JgsType.Function)
        {
            return "function";
        }

        if (value.Type == JgsType.Cell)
        {
            JgsValue[] cells = value.AsCell;
            return cells.Length == 0 || cells.All(static c => c.Type == JgsType.String) ? "cell.str" : "cell.mixed";
        }

        if (value.IsStringArray)
        {
            return value.ArrayLength == 1 ? "string" : "string.vector";
        }

        if (value.IsCharMatrix)
        {
            return value.Rows == 0 || value.Cols == 0 ? "char.empty" : "char.matrix";
        }

        if (value.Type == JgsType.String)
        {
            int length = value.AsString.Length;
            return length == 0 ? "char.empty" : length == 1 ? "char" : "char.row";
        }

        if (value.Type is not (JgsType.Number or JgsType.Bool or JgsType.Complex or JgsType.Array))
        {
            return null;
        }

        string cls = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
        if (JgsNumericClasses.Parse(cls) is null && cls != "logical")
        {
            return null;
        }

        int count = value.Type == JgsType.Array ? value.ArrayLength : 1;
        int rows = value.Type == JgsType.Array ? value.Rows : 1;
        int cols = value.Type == JgsType.Array ? value.Cols : 1;
        if (count == 0)
        {
            return rows == 0 && cols == 0 ? "empty" : cls + ".vector";
        }

        // A complex or a NaN double ranks exactly as a real one does, and fails later, at the
        // conversion (the recorded pairs: Byte beats Object for NaN, and the chosen Byte fails).
        if (count == 1)
        {
            return cls;
        }

        return rows == 1 || cols == 1 ? cls + ".vector" : cls + ".matrix";
    }

    /// <summary>A parameter type's name in the order rows, or null for a type no MATLAB value reaches.</summary>
    private static string? Key(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } under)
        {
            return Key(under) is { } inner ? "Nullable" + inner : null;
        }

        if (type == typeof(Array))
        {
            return "Array";
        }

        if (type.IsArray)
        {
            string? element = Key(type.GetElementType()!);
            return element is null || element.EndsWith("Arr", StringComparison.Ordinal) ? null
                : type.GetArrayRank() switch { 1 => element + "Arr", 2 => element + "2D", _ => null };
        }

        return Type.GetTypeCode(type) switch
        {
            TypeCode.Boolean => "Boolean",
            TypeCode.Byte => "Byte",
            TypeCode.SByte => "SByte",
            TypeCode.Int16 => "Int16",
            TypeCode.UInt16 => "UInt16",
            TypeCode.Int32 when !type.IsEnum => "Int32",
            TypeCode.UInt32 when !type.IsEnum => "UInt32",
            TypeCode.Int64 when !type.IsEnum => "Int64",
            TypeCode.UInt64 when !type.IsEnum => "UInt64",
            TypeCode.Single => "Single",
            TypeCode.Double => "Double",
            TypeCode.Char => "Char",
            TypeCode.String => "String",
            TypeCode.Decimal => "Decimal",
            _ when type == typeof(object) => "Object",
            _ => null,
        };
    }

    /// <summary>
    /// The .NET value <paramref name="value"/> becomes as an argument of type <paramref name="parameter"/>,
    /// which <see cref="Rank"/> has already accepted. A refusal names the argument as R2025b does.
    /// </summary>
    public static object? ToNet(JgsValue value, Type parameter, int position, string parameterName, int line, int col)
    {
        if (value.Type == JgsType.External)
        {
            return ((NetObject)value.AsExternal).Target;
        }

        if (Nullable.GetUnderlyingType(parameter) is { } under)
        {
            return IsEmptyNumeric(value) ? null : ToNet(value, under, position, parameterName, line, col);
        }

        if (value.Type == JgsType.Complex || (value.Type == JgsType.Array && value.IsPackedComplex))
        {
            if (parameter == typeof(object))
            {
                throw ObjectConversion(value, line, col);
            }

            // Decimal alone takes a complex, as its real part (R2025b, net_conversions); every
            // other parameter it reaches refuses it.
            if (parameter == typeof(decimal) && value.Type == JgsType.Complex)
            {
                return (decimal)value.AsComplex.Real;
            }

            throw new JgsRuntimeException(line, col,
                $"Invalid input for argument {position} ({parameterName}):\nValue must be real.");
        }

        if (parameter == typeof(object))
        {
            return ToObject(value, line, col);
        }

        if (parameter == typeof(string))
        {
            return TextOf(value);
        }

        if (parameter == typeof(char))
        {
            return value.AsString[0];
        }

        if (parameter == typeof(Array) || (parameter.IsArray && IsEmptyNumeric(value)))
        {
            return null;
        }

        if (parameter.IsArray)
        {
            return ToArray(value, parameter.GetElementType()!, parameter.GetArrayRank(), line, col);
        }

        double x = value.Type == JgsType.Array ? ToDoubles(value)[0] : value.AsNumber;
        if (double.IsNaN(x) && Type.GetTypeCode(parameter) is not (TypeCode.Double or TypeCode.Single or TypeCode.Boolean))
        {
            // A NaN reaches an integer or Decimal parameter by rank and has no value there (the
            // recorded Only_Int32(NaN) fails); an Int32[] element takes it as 0, as MATLAB's does.
            throw new JgsRuntimeException(line, col,
                $"Invalid input for argument {position} ({parameterName}):\nNaN cannot be converted to {NetNames.ClassName(parameter)}.");
        }

        return Scalar(x, parameter);
    }

    /// <summary>What a value becomes where a method takes <c>System.Object</c> (measured: object_slot).</summary>
    public static object? ToObject(JgsValue value, int line, int col)
    {
        switch (value.Type)
        {
            case JgsType.External:
                return ((NetObject)value.AsExternal).Target;
            case JgsType.Function:
                throw ObjectConversion(value, line, col);
            case JgsType.Cell:
                return value.AsCell.Select(c => ToObject(c, line, col)).ToArray();
        }

        if (value.Type == JgsType.Complex || (value.Type == JgsType.Array && value.IsPackedComplex))
        {
            throw ObjectConversion(value, line, col);
        }

        if (value.IsStringArray)
        {
            return value.ArrayLength == 1 ? TextOf(value) : StringElements(value);
        }

        if (value.Type == JgsType.String)
        {
            return value.AsString.Length == 1 ? value.AsString[0] : value.AsString;
        }

        if (IsEmptyNumeric(value))
        {
            return null;
        }

        if (value.IsCharMatrix)
        {
            return ToArray(value, typeof(char), 2, line, col);
        }

        string cls = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
        Type element = cls == "logical" ? typeof(bool) : ElementType(JgsNumericClasses.Parse(cls) ?? JgsNumericClass.Double);
        int count = value.Type == JgsType.Array ? value.ArrayLength : 1;
        if (count == 1)
        {
            return Scalar(value.Type == JgsType.Array ? ToDoubles(value)[0] : value.AsNumber, element);
        }

        bool vector = value.Rows == 1 || value.Cols == 1;
        return ToArray(value, element, vector ? 1 : 2, line, col);
    }

    private static JgsRuntimeException ObjectConversion(JgsValue value, int line, int col) =>
        new(line, col, "MATLAB:NET:NetConversion:ObjectConversion",
            $"A value of class '{JgsBuiltins.ClassOf(value, JgsDialect.Matlab)}' cannot be converted to 'System.Object'.");

    private static bool IsEmptyNumeric(JgsValue value) =>
        value.Type == JgsType.Array && !value.IsStringArray && !value.IsCharMatrix && value.ArrayLength == 0
        && value.Rows == 0 && value.Cols == 0;

    /// <summary>The text of a char row or a string scalar, null for a missing string or <c>[]</c>.</summary>
    private static string? TextOf(JgsValue value)
    {
        if (value.Type == JgsType.String)
        {
            return value.AsString;
        }

        if (value.IsStringArray && value.ArrayLength == 1)
        {
            string text = value.ElementAt(0).AsString;
            return text == JgsBuiltins.MissingSentinel ? null : text;
        }

        return null;
    }

    private static string?[] StringElements(JgsValue value)
    {
        var texts = new string?[value.ArrayLength];
        for (int i = 0; i < texts.Length; i++)
        {
            string text = value.ElementAt(i).AsString;
            texts[i] = text == JgsBuiltins.MissingSentinel ? null : text;
        }

        return texts;
    }

    private static double[] ToDoubles(JgsValue value) => JgsBuiltins.ToDoubles("NET", value, 0, 0);

    /// <summary>One number into a .NET primitive: integers round half away and saturate, as MATLAB's classes do.</summary>
    private static object Scalar(double x, Type type) => Type.GetTypeCode(type) switch
    {
        TypeCode.Boolean => x != 0,
        TypeCode.Byte => (byte)JgsNumericClasses.Convert(x, JgsNumericClass.UInt8),
        TypeCode.SByte => (sbyte)JgsNumericClasses.Convert(x, JgsNumericClass.Int8),
        TypeCode.Int16 => (short)JgsNumericClasses.Convert(x, JgsNumericClass.Int16),
        TypeCode.UInt16 => (ushort)JgsNumericClasses.Convert(x, JgsNumericClass.UInt16),
        TypeCode.Int32 => (int)JgsNumericClasses.Convert(x, JgsNumericClass.Int32),
        TypeCode.UInt32 => (uint)JgsNumericClasses.Convert(x, JgsNumericClass.UInt32),
        TypeCode.Int64 => SaturatedInt64(x),
        TypeCode.UInt64 => SaturatedUInt64(x),
        TypeCode.Single => (float)x,
        TypeCode.Decimal => double.IsFinite(x) ? (decimal)x : throw new OverflowException("Value was either too large or too small for a Decimal."),
        TypeCode.Char => (char)x,
        _ => x,
    };

    private static long SaturatedInt64(double x)
    {
        double r = Math.Round(double.IsNaN(x) ? 0 : x, MidpointRounding.AwayFromZero);
        return r >= 9.2233720368547758e18 ? long.MaxValue : r <= -9.2233720368547758e18 ? long.MinValue : (long)r;
    }

    private static ulong SaturatedUInt64(double x)
    {
        double r = Math.Round(double.IsNaN(x) ? 0 : x, MidpointRounding.AwayFromZero);
        return r >= 1.8446744073709552e19 ? ulong.MaxValue : r <= 0 ? 0 : (ulong)r;
    }

    /// <summary><c>NET.convertArray</c>'s array: <paramref name="value"/> as a .NET array of <paramref name="element"/> and <paramref name="rank"/>.</summary>
    public static Array ConvertArray(JgsValue value, Type element, int rank, int line, int col) =>
        ToArray(value, element, rank, line, col);

    /// <summary>The .NET element type a MATLAB array's class maps to (<c>double</c> to <c>System.Double</c>, <c>char</c> to <c>System.Char</c>), or null.</summary>
    public static Type? ElementTypeOf(JgsValue value)
    {
        if (value.Type == JgsType.String || value.IsCharMatrix)
        {
            return typeof(char);
        }

        if (value.Type is not (JgsType.Number or JgsType.Bool or JgsType.Array) || value.IsStringArray)
        {
            return null;
        }

        string cls = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
        return cls == "logical" ? typeof(bool) : JgsNumericClasses.Parse(cls) is { } numeric ? ElementType(numeric) : null;
    }

    /// <summary>A MATLAB array into a .NET array of <paramref name="element"/>: a vector to rank 1, a matrix to rank 2.</summary>
    private static Array ToArray(JgsValue value, Type element, int rank, int line, int col)
    {
        if (element == typeof(string))
        {
            return value.Type == JgsType.Cell
                ? value.AsCell.Select(static c => c.AsString).ToArray()
                : StringElements(value);
        }

        if (element == typeof(object))
        {
            return value.AsCell.Select(c => ToObject(c, line, col)).ToArray();
        }

        if (element == typeof(char))
        {
            if (value.Type == JgsType.String)
            {
                return rank == 1 ? value.AsString.ToCharArray() : To2D(value.AsString.Select(static c => (double)c).ToArray(), 1, value.AsString.Length, element);
            }

            double[] codes = ToDoubles(value);
            return To2D(codes, value.Rows, value.Cols, element);
        }

        double[] flat = value.Type is JgsType.Number or JgsType.Bool ? [value.AsNumber] : ToDoubles(value);
        int rows = value.Type == JgsType.Array ? value.Rows : 1;
        int cols = value.Type == JgsType.Array ? value.Cols : 1;
        if (rank == 1)
        {
            Array made = Array.CreateInstance(element, flat.Length);
            for (int i = 0; i < flat.Length; i++)
            {
                made.SetValue(Scalar(flat[i], element), i);
            }

            return made;
        }

        return To2D(flat, rows, cols, element);
    }

    private static Array To2D(double[] columnMajor, int rows, int cols, Type element)
    {
        Array made = Array.CreateInstance(element, rows, cols);
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                made.SetValue(Scalar(columnMajor[r + (c * rows)], element), r, c);
            }
        }

        return made;
    }

    // --- .NET to MATLAB ------------------------------------------------------------------------

    /// <summary>
    /// What a .NET value becomes in MATLAB, given the type the member declared (a Nullable is known
    /// only from the declaration, because the runtime boxes it as its value or null).
    /// </summary>
    public static JgsValue ToMatlab(object? value, Type declared, int line = 0, int col = 0, NetCatalog? session = null)
    {
        if (Nullable.GetUnderlyingType(declared) is not null)
        {
            return JgsValue.External(new NetObject(value, declared));
        }

        if (value is null)
        {
            return JgsEmpty.Zero();
        }

        return value switch
        {
            bool b => JgsValue.Bool(b),
            byte v => Classed(v, JgsNumericClass.UInt8),
            sbyte v => Classed(v, JgsNumericClass.Int8),
            short v => Classed(v, JgsNumericClass.Int16),
            ushort v => Classed(v, JgsNumericClass.UInt16),
            int v when !value.GetType().IsEnum => Classed(v, JgsNumericClass.Int32),
            uint v when !value.GetType().IsEnum => Classed(v, JgsNumericClass.UInt32),
            long v when !value.GetType().IsEnum => Classed(Wide(v, session), JgsNumericClass.Int64),
            ulong v when !value.GetType().IsEnum => Classed(Wide(v, session), JgsNumericClass.UInt64),
            float v => Classed(v, JgsNumericClass.Single),
            double v => JgsValue.Number(v),
            char c => JgsValue.Str(c.ToString()),
            _ => JgsValue.External(new NetObject(value, value.GetType())),
        };
    }

    private static JgsValue Classed(double x, JgsNumericClass numericClass)
    {
        JgsValue made = JgsValue.Number(x);
        made.SetNumericClass(numericClass);
        return made;
    }

    /// <summary>A 64-bit integer as JGraph's number, warning when it is past what a double holds exactly.</summary>
    private static double Wide(long v, NetCatalog? session)
    {
        if (v is > (1L << 53) or < -(1L << 53))
        {
            WarnPrecision(v.ToString(System.Globalization.CultureInfo.InvariantCulture), session);
        }

        return v;
    }

    private static double Wide(ulong v, NetCatalog? session)
    {
        if (v > (1UL << 53))
        {
            WarnPrecision(v.ToString(System.Globalization.CultureInfo.InvariantCulture), session);
        }

        return v;
    }

    /// <summary>
    /// The user's decision on 64-bit integers (interop plan, question 2): the value is kept to a
    /// double's precision, and the session's <c>warning</c> state decides whether that is said.
    /// </summary>
    private static void WarnPrecision(string exact, NetCatalog? session) =>
        session?.Warn?.Invoke("JGraph:interop:int64Precision",
            $"The 64-bit integer {exact} is past 2^53 and is held to the nearest value a double represents.");

    /// <summary>A .NET value's numbers, for <c>double</c> and the other numeric conversions: null when it has none.</summary>
    public static JgsValue? NumbersOf(NetObject net, JgsNumericClass wanted)
    {
        object? target = net.Target;
        if (target is Array array && array.Rank <= 2 && IsNumericElement(array.GetType().GetElementType()!))
        {
            int rows = array.Rank == 1 ? 1 : array.GetLength(0);
            int cols = array.Rank == 1 ? array.Length : array.GetLength(1);
            var flat = new double[array.Length];
            for (int c = 0; c < cols; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    object? held = array.Rank == 1 ? array.GetValue(c) : array.GetValue(r, c);
                    flat[r + (c * rows)] = System.Convert.ToDouble(held, System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            return JgsNumericClasses.Stamp(JgsMatrix.FromColumnMajor(flat, rows, cols), wanted);
        }

        if (target is not null && target.GetType().IsEnum)
        {
            return JgsNumericClasses.Stamp(JgsValue.Number(System.Convert.ToDouble(target, System.Globalization.CultureInfo.InvariantCulture)), wanted);
        }

        return null;
    }

    private static bool IsNumericElement(Type element) => Type.GetTypeCode(element) is
        TypeCode.Boolean or TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32
        or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single or TypeCode.Double;
}
