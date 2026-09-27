using System.Buffers.Binary;
using System.Runtime.Versioning;
using System.Text;
using JGraph.Scripting.Jgs.Native;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Pointers and structs in MATLAB's shared-library interface (interop plan, stage 9, ADR 0182):
/// <c>libpointer</c>, <c>libstruct</c>, the <c>lib.pointer</c> and <c>lib.&lt;struct&gt;</c> objects
/// (their properties, methods and operators), and how <c>calllib</c> hands each argument to native
/// code and what it hands back for it. Every sentence and identifier is R2025b's (shrlib_pointers,
/// shrlib_structs, probe_shrlib_calls, probe_shrlib_pointers2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Outputs.</b> <c>calllib</c> answers the return value, then one value per pointer argument in
/// argument order: the value that argument's memory holds after the call. A MATLAB array comes back
/// in the pointee's class and the array's shape, a struct as a struct, a C string as char, a C string
/// array as a cell; a <c>lib.pointer</c> argument comes back as its <c>Value</c> (as a fresh pointer to
/// the same place when its value is not defined, and always for <c>voidPtr</c>); a
/// pointer-to-pointer comes back as a new pointer to where the library pointed it.
/// </para>
/// <para>
/// <b>Where JGraph departs from R2025b</b> (the ADR's Divergences): a libstruct whose type has an
/// array field or non-default packing is passed as its memory, where R2025b passes NULL; its array
/// fields read as arrays of their own class from the start, where R2025b reads <c>[]</c>; a value
/// written into integer pointer memory rounds and saturates as MATLAB's own integer classes do,
/// where R2025b truncates and wraps (<c>libpointer('int32Ptr', 1e10)</c> holds -2147483648); and a
/// pointer that owns its memory refuses a <c>Value</c> that would read past it.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    private const string IllegalConversionSentence = "Array must be numeric or logical or a pointer to one";

    /// <summary>Declares <c>libpointer</c> and <c>libstruct</c> into <paramref name="env"/>.</summary>
    internal static void RegisterLibPointerBuiltins(JgsEnvironment env, Interpreter interpreter)
    {
        env.Builtins.Register("libpointer", JgsValue.Function(new BuiltinFunction("libpointer",
            (args, line, col) => OperatingSystem.IsWindows() ? MakeLibPointer(interpreter, args, line, col) : throw NotOnWindows(line, col))
        {
            KeepsStringArguments = true,
            AutoCallsBare = true,
        }));
        env.Builtins.Register("libstruct", JgsValue.Function(new BuiltinFunction("libstruct",
            (args, line, col) => OperatingSystem.IsWindows() ? MakeLibStruct(interpreter, args, line, col) : throw NotOnWindows(line, col))
        {
            KeepsStringArguments = true,
        }));
    }

    // ------------------------------------------------------------------------------------------
    // Refusals
    // ------------------------------------------------------------------------------------------

    private static JgsRuntimeException ValueNotDefined(int line, int col) =>
        new(line, col, "MATLAB:libpointer:ValueNotDefined", "The datatype and size of the value must be defined before the value can be retrieved.");

    private static JgsRuntimeException IllegalConversion(int line, int col) =>
        new(line, col, "MATLAB:libpointer:IllegalConversion", IllegalConversionSentence);

    private static JgsRuntimeException RequireStruct(int line, int col) =>
        new(line, col, "MATLAB:libstruct:RequireStruct", "A structure is required.");

    private static JgsRuntimeException FieldMismatch(int line, int col) =>
        new(line, col, "MATLAB:libstruct:FieldMismatch", "A field does not match one in the struct.");

    private static JgsRuntimeException InvalidType(int line, int col) =>
        new(line, col, "MATLAB:libpointer:InvalidType", "Type was not found.");

    private static JgsRuntimeException OnlyNumericDimensions(int line, int col) =>
        new(line, col, "MATLAB:libpointer:reshape:InvalidType", "Only numericPtr data types may have their dimensions set.");

    private static JgsRuntimeException NoOutputs(int line, int col) =>
        new(line, col, "MATLAB:maxlhs", "Too many output arguments.");

    private static JgsRuntimeException InvalidHandle(int line, int col) =>
        new(line, col, "MATLAB:class:InvalidHandle", "Invalid or deleted object.");

    private static JgsRuntimeException UnknownConversion(JgsValue value, JgsDialect dialect, int line, int col) =>
        new(line, col, "MATLAB:invalidConversion", $"Conversion to unknown from {ClassOf(value, dialect)} is not possible.");

    // ------------------------------------------------------------------------------------------
    // Types
    // ------------------------------------------------------------------------------------------

    /// <summary>The numeric class a scalar shared-library type reads back as; null for <c>bool</c>.</summary>
    private static JgsNumericClass? ElementClass(string element) => element switch
    {
        "int8" => JgsNumericClass.Int8,
        "uint8" => JgsNumericClass.UInt8,
        "int16" => JgsNumericClass.Int16,
        "uint16" => JgsNumericClass.UInt16,
        "int32" or "long" => JgsNumericClass.Int32,
        "uint32" or "ulong" => JgsNumericClass.UInt32,
        "int64" => JgsNumericClass.Int64,
        "uint64" => JgsNumericClass.UInt64,
        "single" => JgsNumericClass.Single,
        "double" => JgsNumericClass.Double,
        _ => null,
    };

    /// <summary>The struct type a loaded library defines under <paramref name="name"/>: struct types are session-wide (ADR 0181).</summary>
    [SupportedOSPlatform("windows")]
    private static (SharedLibrary Library, LibStruct Type)? FindStructType(JGraphScriptGlobals host, string name)
    {
        foreach (SharedLibrary library in host.Native.Libraries.Values)
        {
            if (library.Host.IsAlive && library.Model.Struct(name) is { } type && library.Model.StructLayout(type) is not null)
            {
                return (library, type);
            }
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static bool IsEnumType(JGraphScriptGlobals host, string name) =>
        host.Native.Libraries.Values.Any(l => l.Model.Enum(name) is not null);

    /// <summary>
    /// What a pointer of <paramref name="type"/> reads (<see cref="LibPointer.Element"/>) and the model
    /// its struct comes from; false for a type <c>libpointer</c> does not know.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static bool PointerType(JGraphScriptGlobals host, string type, out string? element, out LibraryModel? model)
    {
        element = null;
        model = null;
        if (LibTypes.IsScalar(type))
        {
            element = type;
            return true;
        }

        switch (type)
        {
            case "cstring":
                element = "cstring";
                return true;
            case "voidPtr" or "voidPtrPtr":
                return true;
            case "stringPtrPtr":
                element = "string";
                return true;
        }

        if (type.EndsWith("PtrPtr", StringComparison.Ordinal))
        {
            string inner = type[..^3];
            return PointerType(host, inner, out _, out model) && !inner.EndsWith("PtrPtr", StringComparison.Ordinal);
        }

        if (!type.EndsWith("Ptr", StringComparison.Ordinal))
        {
            return false;
        }

        string pointee = type[..^3];
        if (LibTypes.IsScalar(pointee))
        {
            element = pointee;
            return true;
        }

        if (FindStructType(host, pointee) is { } found)
        {
            element = pointee;
            model = found.Library.Model;
            return true;
        }

        if (IsEnumType(host, pointee))
        {
            element = "int32";
            return true;
        }

        return false;
    }

    /// <summary>What a pointer of <paramref name="type"/> a library handed back reads, from the library's own model.</summary>
    private static string? ElementOfPointer(LibraryModel model, string type)
    {
        if (type == "stringPtrPtr")
        {
            return "string";
        }

        if (type == "cstring")
        {
            return "cstring";
        }

        if (type.EndsWith("PtrPtr", StringComparison.Ordinal) || !type.EndsWith("Ptr", StringComparison.Ordinal))
        {
            return null;
        }

        string pointee = type[..^3];
        return LibTypes.IsScalar(pointee) || model.Struct(pointee) is not null ? pointee
            : model.Enum(pointee) is not null ? "int32"
            : null;
    }

    /// <summary>A pointer's struct element, or null.</summary>
    private static LibStruct? StructElement(LibPointer pointer) =>
        pointer.Element is { } element && pointer.Model?.Struct(element) is { } type ? type : null;

    /// <summary>Whether a pointer's type is a pointer to a pointer (not a C string array).</summary>
    private static bool IsPointerToPointer(string type) =>
        type.EndsWith("PtrPtr", StringComparison.Ordinal) && type != "stringPtrPtr";

    /// <summary>The bytes one step of <c>p + n</c> moves.</summary>
    private static int StepOf(LibPointer pointer)
    {
        if (IsPointerToPointer(pointer.DataType) || pointer.Element == "string")
        {
            return 8;
        }

        if (StructElement(pointer) is { } type)
        {
            return pointer.Model!.StructLayout(type)!.Value.Size;
        }

        return pointer.Element is { } element && LibTypes.ScalarSize(element) is { } size ? size : 1;
    }

    // ------------------------------------------------------------------------------------------
    // Values in and out of host memory
    // ------------------------------------------------------------------------------------------

    /// <summary>A numeric or logical MATLAB value laid out for native memory: column-major, with its shape and element type.</summary>
    private sealed record NumericData(double[] Values, int Rows, int Cols, string Element);

    /// <summary>A numeric or logical value's numbers, or null for anything else (char, text, cells, structs, objects, complex).</summary>
    private static NumericData? NumericOf(JgsValue value)
    {
        if (!IsNumericValue(value) || value.Type is JgsType.Complex or JgsType.Sparse || value.IsPackedComplex)
        {
            return null;
        }

        string element = IsLogicalValue(value) ? "bool" : value.NumericClass.MatlabName();
        if (value.Type is JgsType.Number or JgsType.Bool)
        {
            return new NumericData([value.Type == JgsType.Bool ? (value.AsBool ? 1 : 0) : value.AsNumber], 1, 1, element);
        }

        if (!value.IsPacked && value.AsArray.Any(static e => e.Type is not (JgsType.Number or JgsType.Bool)))
        {
            return null;
        }

        return new NumericData(ToDoubles("calllib", value, 0, 0), value.Rows, value.Cols, element);
    }

    private static bool IsComplexValue(JgsValue value) => value.Type == JgsType.Complex || value.IsPackedComplex;

    /// <summary>Writes one element of <paramref name="element"/> type, converted as MATLAB converts into that class.</summary>
    private static void PutElement(Span<byte> at, string element, double x)
    {
        switch (element)
        {
            case "bool":
                at[0] = x != 0 ? (byte)1 : (byte)0;
                return;
            case "single":
                BinaryPrimitives.WriteSingleLittleEndian(at, (float)x);
                return;
            case "double":
                BinaryPrimitives.WriteDoubleLittleEndian(at, x);
                return;
            case "int64":
            {
                double r = double.IsNaN(x) ? 0 : Math.Round(x, MidpointRounding.AwayFromZero);
                BinaryPrimitives.WriteInt64LittleEndian(at, r >= 9223372036854775807.0 ? long.MaxValue : r <= -9223372036854775808.0 ? long.MinValue : (long)r);
                return;
            }

            case "uint64":
            {
                double r = double.IsNaN(x) ? 0 : Math.Round(x, MidpointRounding.AwayFromZero);
                BinaryPrimitives.WriteUInt64LittleEndian(at, r >= 18446744073709551615.0 ? ulong.MaxValue : r <= 0 ? 0 : (ulong)r);
                return;
            }
        }

        double v = JgsNumericClasses.Convert(x, ElementClass(element) ?? JgsNumericClass.Double);
        switch (LibTypes.ScalarSize(element))
        {
            case 1:
                at[0] = element == "int8" ? unchecked((byte)(sbyte)v) : (byte)v;
                break;
            case 2:
                if (element == "int16")
                {
                    BinaryPrimitives.WriteInt16LittleEndian(at, (short)v);
                }
                else
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(at, (ushort)v);
                }

                break;
            default:
                if (element is "int32" or "long")
                {
                    BinaryPrimitives.WriteInt32LittleEndian(at, (int)v);
                }
                else
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(at, (uint)v);
                }

                break;
        }
    }

    /// <summary>Reads one element of <paramref name="element"/> type as a number.</summary>
    private static double GetElement(ReadOnlySpan<byte> at, string element) => element switch
    {
        "bool" => at[0] != 0 ? 1 : 0,
        "int8" => (sbyte)at[0],
        "uint8" => at[0],
        "int16" => BinaryPrimitives.ReadInt16LittleEndian(at),
        "uint16" => BinaryPrimitives.ReadUInt16LittleEndian(at),
        "int32" or "long" => BinaryPrimitives.ReadInt32LittleEndian(at),
        "uint32" or "ulong" => BinaryPrimitives.ReadUInt32LittleEndian(at),
        "int64" => BinaryPrimitives.ReadInt64LittleEndian(at),
        "uint64" => BinaryPrimitives.ReadUInt64LittleEndian(at),
        "single" => BinaryPrimitives.ReadSingleLittleEndian(at),
        _ => BinaryPrimitives.ReadDoubleLittleEndian(at),
    };

    /// <summary>Numbers as elements of <paramref name="element"/> type, in order.</summary>
    private static byte[] Encode(string element, double[] values)
    {
        if (element == "double" && BitConverter.IsLittleEndian)
        {
            return System.Runtime.InteropServices.MemoryMarshal.AsBytes(values.AsSpan()).ToArray();
        }

        int size = LibTypes.ScalarSize(element) ?? 8;
        byte[] bytes = new byte[values.Length * size];
        for (int i = 0; i < values.Length; i++)
        {
            PutElement(bytes.AsSpan(i * size, size), element, values[i]);
        }

        return bytes;
    }

    /// <summary>
    /// Elements of <paramref name="element"/> type as a MATLAB array of their own class
    /// (<c>bool</c> as logical), <paramref name="rows"/> by <paramref name="cols"/>.
    /// </summary>
    private static JgsValue ArrayOfBytes(ReadOnlySpan<byte> bytes, string element, int rows, int cols)
    {
        int size = LibTypes.ScalarSize(element) ?? 8;
        int count = rows * cols;
        var flat = new double[count];
        if (element == "double" && BitConverter.IsLittleEndian)
        {
            System.Runtime.InteropServices.MemoryMarshal.Cast<byte, double>(bytes[..(count * 8)]).CopyTo(flat);
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                flat[i] = GetElement(bytes.Slice(i * size, size), element);
            }
        }

        if (element == "bool")
        {
            if (count == 1)
            {
                return JgsValue.Bool(flat[0] != 0);
            }

            JgsValue[] cells = [.. flat.Select(static x => JgsValue.Bool(x != 0))];
            return JgsValue.Shaped(cells, rows, cols, JgsPackedKind.Bool);
        }

        JgsValue value = count == 1 && rows == 1 ? JgsValue.Number(flat[0]) : JgsMatrix.FromColumnMajor(flat, rows, cols);
        JgsNumericClass numericClass = ElementClass(element) ?? JgsNumericClass.Double;
        return numericClass == JgsNumericClass.Double ? value : JgsNumericClasses.Stamp(value, numericClass);
    }

    [SupportedOSPlatform("windows")]
    private static JgsValue ReadArray(NativeHostProcess process, long address, string element, int rows, int cols)
    {
        int count = rows * cols;
        byte[] bytes = count == 0 ? [] : process.Read(address, count * (LibTypes.ScalarSize(element) ?? 8));
        return ArrayOfBytes(bytes, element, rows, cols);
    }

    [SupportedOSPlatform("windows")]
    private static long ReadAddress(NativeHostProcess process, long address) =>
        BinaryPrimitives.ReadInt64LittleEndian(process.Read(address, 8));

    /// <summary>A C string array's strings as a cell of <paramref name="rows"/> by <paramref name="cols"/>; a NULL entry is <c>[]</c>.</summary>
    [SupportedOSPlatform("windows")]
    private static JgsValue ReadStrings(NativeHostProcess process, long address, int rows, int cols)
    {
        int count = rows * cols;
        byte[] table = count == 0 ? [] : process.Read(address, count * 8);
        var cells = new JgsValue[count];
        for (int i = 0; i < count; i++)
        {
            long text = BinaryPrimitives.ReadInt64LittleEndian(table.AsSpan(i * 8, 8));
            cells[i] = text == 0 ? JgsEmpty.Zero() : JgsValue.Str(ReadNativeString(process, text));
        }

        JgsValue cell = JgsValue.Cell(cells);
        cell.Reshape(rows, cols);
        return cell;
    }

    /// <summary>A C string's bytes: the text, then its NUL.</summary>
    private static byte[] CStringBytes(string text) => Encoding.UTF8.GetBytes(text + "\0");

    /// <summary>Whether a value is a cell of character vectors.</summary>
    private static bool IsCellOfChars(JgsValue value) =>
        value.Type == JgsType.Cell && value.AsCell.All(static c => c.Type == JgsType.String && !c.IsStringArray);

    // ------------------------------------------------------------------------------------------
    // Structs
    // ------------------------------------------------------------------------------------------

    /// <summary>Splits a member type <c>int32#3</c> into its element type and count (1 and false for a single value).</summary>
    private static (string Element, int Count, bool IsArray) MemberShape(string type)
    {
        int hash = type.LastIndexOf('#');
        return hash > 0 && int.TryParse(type.AsSpan(hash + 1), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int count)
            ? (type[..hash], count, true)
            : (type, 1, false);
    }

    /// <summary>The size of one element of a member type.</summary>
    private static int ElementSizeOf(LibraryModel model, string element) =>
        model.Struct(element) is { } inner ? model.StructLayout(inner)!.Value.Size : model.Layout(element)?.Size ?? 8;

    /// <summary>
    /// A struct's bytes as a MATLAB struct: scalars as double (<c>bool</c> as logical, an enum as its
    /// number), array members as rows of their own class, nested structs as structs, pointers as
    /// <c>lib.pointer</c>s into <paramref name="process"/>.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static JgsValue StructOfBytes(NativeHostProcess? process, LibraryModel model, LibStruct structure, ReadOnlySpan<byte> bytes)
    {
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        int[] offsets = model.StructLayout(structure)!.Value.Offsets;
        for (int i = 0; i < structure.Members.Count; i++)
        {
            fields[structure.Members[i].Name] = MemberValue(process, model, structure.Members[i].Type, bytes[offsets[i]..]);
        }

        return JgsValue.Struct(fields);
    }

    [SupportedOSPlatform("windows")]
    private static JgsValue MemberValue(NativeHostProcess? process, LibraryModel model, string type, ReadOnlySpan<byte> at)
    {
        (string element, int count, bool isArray) = MemberShape(type);
        int size = ElementSizeOf(model, element);
        if (model.Struct(element) is { } inner)
        {
            if (!isArray)
            {
                return StructOfBytes(process, model, inner, at);
            }

            var elements = new Dictionary<string, JgsValue>[count];
            for (int k = 0; k < count; k++)
            {
                elements[k] = StructOfBytes(process, model, inner, at.Slice(k * size, size)).WritableStruct();
            }

            return JgsValue.StructArray(elements);
        }

        if (model.Enum(element) is not null)
        {
            return isArray ? ArrayOfBytes(at, "int32", 1, count) : JgsValue.Number(BinaryPrimitives.ReadInt32LittleEndian(at));
        }

        if (LibTypes.IsPointer(element))
        {
            long address = BinaryPrimitives.ReadInt64LittleEndian(at);
            if (element == "cstring")
            {
                return JgsValue.Str(address == 0 || process is null ? "" : ReadNativeString(process, address));
            }

            return JgsValue.External(new LibPointer(element)
            {
                Process = process,
                Address = address,
                Element = ElementOfPointer(model, element),
                Model = model,
            });
        }

        if (isArray)
        {
            return ArrayOfBytes(at, element, 1, count);
        }

        double x = GetElement(at, element);
        return element == "bool" ? JgsValue.Bool(x != 0) : JgsValue.Number(x);
    }

    [SupportedOSPlatform("windows")]
    private static JgsValue ReadStruct(NativeHostProcess process, LibraryModel model, LibStruct structure, long address) =>
        StructOfBytes(process, model, structure, process.Read(address, model.StructLayout(structure)!.Value.Size));

    /// <summary>A struct of zeros of <paramref name="structure"/>'s type.</summary>
    [SupportedOSPlatform("windows")]
    private static JgsValue ZeroStruct(LibraryModel model, LibStruct structure) =>
        StructOfBytes(null, model, structure, new byte[model.StructLayout(structure)!.Value.Size]);

    /// <summary>
    /// Writes <paramref name="source"/> into <paramref name="into"/> as a struct of
    /// <paramref name="structure"/>'s type ("Structure Argument Requirements"): a MATLAB struct's
    /// fields by name, case-sensitively, each a member (a field no member has is
    /// <c>FieldMismatch</c>, a member no field names stays zero); the first element of a struct array;
    /// or a libstruct of the same type, byte for byte. Anything else is <c>RequireStruct</c>.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void EncodeStruct(LibraryModel model, LibStruct structure, JgsValue source, Span<byte> into, int line, int col)
    {
        if (source.AsExternalOrNull() is LibStructValue held)
        {
            if (held.Type.Name != structure.Name)
            {
                throw RequireStruct(line, col);
            }

            LiveStruct(held, line, col).Block.Process.Read(held.Block.Address, into[..(int)held.Block.Size]);
            return;
        }

        if (source.Type != JgsType.Struct || source.ClassName is not null)
        {
            throw RequireStruct(line, col);
        }

        JgsStructArray elements = source.AsStructArray;
        if (elements.Length == 0)
        {
            return;
        }

        int[] offsets = model.StructLayout(structure)!.Value.Offsets;
        foreach ((string field, JgsValue value) in elements.Elements[0])
        {
            int index = -1;
            for (int i = 0; i < structure.Members.Count; i++)
            {
                if (structure.Members[i].Name == field)
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                throw FieldMismatch(line, col);
            }

            LibMember member = structure.Members[index];
            try
            {
                EncodeMember(model, member.Type, value, into[offsets[index]..], line, col);
            }
            catch (JgsRuntimeException inner)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:libstruct:InvalidFieldValue",
                    $"Cannot convert data value for field {member.Name} due to error:\n{inner.Message}");
            }
        }
    }

    /// <summary>
    /// Writes one member: a number into a scalar (R2025b's <c>MustBeNumeric</c> and <c>MustBeScalar</c>),
    /// numbers into an array member (as many as it holds, the rest zero), a struct into a nested
    /// struct, a name or a number into an enum, a <c>lib.pointer</c> or <c>[]</c> into a pointer.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void EncodeMember(LibraryModel model, string type, JgsValue value, Span<byte> at, int line, int col)
    {
        (string element, int count, bool isArray) = MemberShape(type);
        int size = ElementSizeOf(model, element);
        if (model.Struct(element) is { } inner)
        {
            if (!isArray)
            {
                EncodeStruct(model, inner, value, at[..size], line, col);
                return;
            }

            if (value.Type != JgsType.Struct || value.AsStructArray.Length > count)
            {
                throw RequireStruct(line, col);
            }

            at[..(size * count)].Clear();
            for (int k = 0; k < value.AsStructArray.Length; k++)
            {
                EncodeStruct(model, inner, JgsValue.Struct(value.AsStructArray.Elements[k]), at.Slice(k * size, size), line, col);
            }

            return;
        }

        if (model.Enum(element) is { } enumeration && !isArray)
        {
            BinaryPrimitives.WriteInt32LittleEndian(at, (int)EnumArgument(enumeration, value, line, col));
            return;
        }

        if (LibTypes.IsPointer(element))
        {
            long address = value.AsExternalOrNull() is LibPointer pointer ? LivePointer(pointer, line, col).Address
                : NumericOf(value) is { Values.Length: 0 } ? 0
                : throw IllegalConversion(line, col);
            BinaryPrimitives.WriteInt64LittleEndian(at, address);
            return;
        }

        if (model.Enum(element) is not null)
        {
            element = "int32";
        }

        NumericData? numbers = NumericOf(value);
        if (!isArray)
        {
            if (numbers is null)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:class:MustBeNumeric", "Array must be numeric or logical.");
            }

            if (numbers.Values.Length != 1)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:class:MustBeScalar", "Parameter must be scalar.");
            }

            PutElement(at[..size], element, numbers.Values[0]);
            return;
        }

        if (numbers is null)
        {
            throw IllegalConversion(line, col);
        }

        if (numbers.Values.Length > count)
        {
            throw new JgsRuntimeException(line, col, "The variable can not hold the number of elements assigned.");
        }

        at[..(size * count)].Clear();
        Encode(element, numbers.Values).CopyTo(at);
    }

    /// <summary>A struct argument's bytes, from a MATLAB struct or a libstruct.</summary>
    [SupportedOSPlatform("windows")]
    private static byte[] StructBytes(LibraryModel model, LibStruct structure, JgsValue source, int line, int col)
    {
        byte[] bytes = new byte[model.StructLayout(structure)!.Value.Size];
        EncodeStruct(model, structure, source, bytes, line, col);
        return bytes;
    }

    // ------------------------------------------------------------------------------------------
    // libpointer and lib.pointer
    // ------------------------------------------------------------------------------------------

    /// <summary><c>libpointer</c>, <c>libpointer(type)</c> and <c>libpointer(type, value)</c>.</summary>
    [SupportedOSPlatform("windows")]
    private static JgsValue MakeLibPointer(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:dispatcher:noMatchingConstructor", "No constructor 'lib.pointer' with matching signature found.");
        }

        interpreter.NoteNet(); // lib.pointer's methods dispatch as a .NET object's do
        if (args.Count == 0)
        {
            return JgsValue.External(new LibPointer(""));
        }

        JGraphScriptGlobals host = SessionHost(interpreter, line, col);
        string type = IsTextScalar(args[0]) ? TextOf(args[0]) : "";
        if (!PointerType(host, type, out string? element, out LibraryModel? model))
        {
            throw InvalidType(line, col);
        }

        var pointer = new LibPointer(type) { Element = element, Model = model };
        if (args.Count == 2)
        {
            SetPointerValue(host, pointer, args[1], construct: true, line, col);
        }

        return JgsValue.External(pointer);
    }

    /// <summary>The pointer, or R2025b's refusal when it was deleted, or JGraph's when its host has exited.</summary>
    [SupportedOSPlatform("windows")]
    private static LibPointer LivePointer(LibPointer pointer, int line, int col)
    {
        if (pointer.Deleted)
        {
            throw InvalidHandle(line, col);
        }

        if (pointer.Address != 0 && pointer.Process is { IsAlive: false })
        {
            throw new JgsRuntimeException(line, col, "JGraph:libpointer:HostExited",
                "The native host this pointer points into has exited; the pointer no longer points at anything.");
        }

        return pointer;
    }

    /// <summary>The libstruct, or the refusal for a deleted one or one whose host has exited.</summary>
    [SupportedOSPlatform("windows")]
    private static LibStructValue LiveStruct(LibStructValue structure, int line, int col)
    {
        if (structure.Deleted)
        {
            throw InvalidHandle(line, col);
        }

        if (!structure.Block.Process.IsAlive)
        {
            throw new JgsRuntimeException(line, col, "JGraph:libpointer:HostExited",
                $"The native host this {structure.ClassName} lived in has exited; its fields are gone.");
        }

        return structure;
    }

    /// <summary>
    /// <c>p.Value</c> (R2025b, shrlib_pointers and probe_shrlib_pointers2): the elements the pointer's
    /// type and size say, which a pointer without both refuses; a C string; a C string array's strings
    /// as a column (one, until a size says more); the struct a struct pointer points at, zeros for a
    /// NULL one; for a NULL pointer to a pointer an empty, and for a pointer to a struct pointer the
    /// struct at the end of both.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static JgsValue PointerValue(LibPointer pointer, int line, int col)
    {
        LivePointer(pointer, line, col);
        string type = pointer.DataType;
        if (type.Length == 0 && pointer.Element is null)
        {
            throw ValueNotDefined(line, col);
        }

        if (IsPointerToPointer(type))
        {
            LibStruct? target = pointer.Model?.Struct(type[..^6]);
            if (pointer.IsNull)
            {
                if (target is null)
                {
                    return JgsEmpty.Zero();
                }

                JgsValue none = JgsValue.StructArray(new JgsStructArray([], [.. target.Members.Select(static m => m.Name)]), 0, 1);
                return none;
            }

            if (target is not null && ReadAddress(pointer.Process!, pointer.Address) is var inner and not 0)
            {
                return ReadStruct(pointer.Process!, pointer.Model!, target, inner);
            }

            throw ValueNotDefined(line, col);
        }

        if (StructElement(pointer) is { } structure)
        {
            return pointer.IsNull ? ZeroStruct(pointer.Model!, structure) : ReadStruct(pointer.Process!, pointer.Model!, structure, pointer.Address);
        }

        if (pointer.IsNull)
        {
            throw ValueNotDefined(line, col);
        }

        NativeHostProcess process = pointer.Process!;
        switch (pointer.Element)
        {
            case "cstring":
                return JgsValue.Str(ReadNativeString(process, pointer.Address));
            case "string":
                return pointer.Sized
                    ? ReadStrings(process, pointer.Address, pointer.Rows * pointer.Cols, 1)
                    : ReadStrings(process, pointer.Address, 1, 1);
            case null:
                throw ValueNotDefined(line, col);
        }

        if (!pointer.Sized)
        {
            throw ValueNotDefined(line, col);
        }

        long bytes = (long)pointer.Rows * pointer.Cols * (LibTypes.ScalarSize(pointer.Element) ?? 8);
        if (pointer.Block is { } block && !block.Holds(pointer.Address, bytes))
        {
            throw new JgsRuntimeException(line, col, "JGraph:libpointer:PastEnd",
                $"A {pointer.Rows}x{pointer.Cols} value of this pointer would read past the end of the memory it points into.");
        }

        return ReadArray(process, pointer.Address, pointer.Element, pointer.Rows, pointer.Cols);
    }

    /// <summary>Points <paramref name="pointer"/> at new memory holding <paramref name="bytes"/>, which it owns.</summary>
    [SupportedOSPlatform("windows")]
    private static void Adopt(JGraphScriptGlobals host, LibPointer pointer, ReadOnlySpan<byte> bytes, long[]? children = null, object? keeps = null)
    {
        NativeHostProcess process = host.Native.Host;
        NativeBlock block = NativeBlock.Allocate(process, bytes, children, keeps);
        pointer.Block = block;
        pointer.Process = process;
        pointer.Address = block.Address;
    }

    /// <summary>
    /// <c>p.Value = v</c> and <c>libpointer(type, v)</c>. Memory the pointer owns (or none yet) is
    /// replaced by new memory holding <paramref name="value"/>, sized to it; a library's memory is
    /// written in place and keeps its size (R2025b, probe_shrlib_pointers2 <c>lp.ret.write</c>).
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void SetPointerValue(JGraphScriptGlobals host, LibPointer pointer, JgsValue value, bool construct, int line, int col)
    {
        if (!construct)
        {
            LivePointer(pointer, line, col);
        }

        string type = pointer.DataType;
        bool inPlace = pointer.Block is null && !pointer.IsNull && pointer.Process is { IsAlive: true };
        if (type == "cstring")
        {
            if (value.Type != JgsType.String || value.IsStringArray)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:libpointer:MustBeString", "Parameter can not be converted to a character vector");
            }

            Adopt(host, pointer, CStringBytes(value.AsString));
            return;
        }

        if (type == "stringPtrPtr")
        {
            if (!IsCellOfChars(value))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:class:MustBeCellStringsParameter", "Parameter must be a cell array of character vectors.");
            }

            NativeHostProcess process = host.Native.Host;
            JgsValue[] texts = value.AsCell;
            long[] strings = new long[texts.Length];
            byte[] table = new byte[texts.Length * 8];
            for (int i = 0; i < texts.Length; i++)
            {
                byte[] text = CStringBytes(texts[i].AsString);
                strings[i] = process.Alloc(text.Length);
                process.Write(strings[i], text);
                BinaryPrimitives.WriteInt64LittleEndian(table.AsSpan(i * 8, 8), strings[i]);
            }

            Adopt(host, pointer, table, strings);
            pointer.Rows = texts.Length;
            pointer.Cols = 1;
            pointer.Sized = true;
            return;
        }

        if (IsPointerToPointer(type))
        {
            if (value.AsExternalOrNull() is LibPointer target)
            {
                LivePointer(target, line, col);
                byte[] cell = new byte[8];
                BinaryPrimitives.WriteInt64LittleEndian(cell, target.Address);
                Adopt(host, pointer, cell, keeps: target);
                return;
            }

            if (NumericOf(value) is { Values.Length: 0 })
            {
                pointer.Address = 0;
                pointer.Block = null;
                return;
            }

            throw IllegalConversion(line, col);
        }

        if (StructElement(pointer) is { } structure)
        {
            byte[] bytes = StructBytes(pointer.Model!, structure, value, line, col);
            if (inPlace)
            {
                pointer.Process!.Write(pointer.Address, bytes);
            }
            else
            {
                Adopt(host, pointer, bytes);
            }

            return;
        }

        NumericData numbers = NumericOf(value) ?? throw IllegalConversion(line, col);
        if (type == "boolPtr" && numbers.Values.Length != 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:class:MustBeScalar", "Parameter must be scalar.");
        }

        if (numbers.Values.Length == 0)
        {
            pointer.Address = 0;
            pointer.Block = null;
            pointer.Sized = false;
            return;
        }

        // A void pointer (or an untyped one) takes the class of what it is given.
        if (type is "voidPtr" or "" || pointer.Element is null)
        {
            pointer.Element = numbers.Element;
        }

        byte[] data = Encode(pointer.Element, numbers.Values);
        if (inPlace)
        {
            pointer.Process!.Write(pointer.Address, data);
            if (!pointer.Sized)
            {
                (pointer.Rows, pointer.Cols, pointer.Sized) = (numbers.Rows, numbers.Cols, true);
            }

            return;
        }

        Adopt(host, pointer, data);
        (pointer.Rows, pointer.Cols, pointer.Sized) = (numbers.Rows, numbers.Cols, true);
    }

    /// <summary>The size arguments of <c>reshape</c> and <c>setdatatype</c>: two numbers, or one size vector.</summary>
    private static (int Rows, int Cols) SizeArguments(IReadOnlyList<JgsValue> sizes, int line, int col)
    {
        double[] numbers = sizes.Count == 1 && NumericOf(sizes[0]) is { } vector ? vector.Values
            : [.. sizes.Select(s => NumericOf(s) is { Values.Length: 1 } one ? one.Values[0] : throw new JgsRuntimeException(line, col, "MATLAB:class:RequireScalar", "Value must be a scalar."))];
        return numbers switch
        {
            [var n] => (1, (int)n),
            [var r, var c] => ((int)r, (int)c),
            _ => throw new JgsRuntimeException(line, col, "MATLAB:maxrhs", "Too many input arguments."),
        };
    }

    /// <summary><c>p + n</c>: a new pointer <paramref name="steps"/> elements on, holding the same memory.</summary>
    [SupportedOSPlatform("windows")]
    private static JgsValue PointerPlus(LibPointer pointer, JgsValue steps, int line, int col)
    {
        LivePointer(pointer, line, col);
        NumericData offset = NumericOf(steps)
            ?? throw new JgsRuntimeException(line, col, "MATLAB:class:RequireNumeric", "Invalid input for argument 2 (rhs2):\nValue must be numeric or logical.");
        if (offset.Values.Length != 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:class:RequireScalar", "Invalid input for argument 2 (rhs2):\nValue must be a scalar.");
        }

        long n = (long)Math.Round(offset.Values[0], MidpointRounding.AwayFromZero);
        LibPointer moved = pointer.Alias();
        moved.Address = pointer.Address + (n * StepOf(pointer));

        // A value libpointer made has a known extent, which the step shortens (R2025b: lp + 1 of
        // [10 20 30] reads [20 30]); a library's pointer keeps the size it was given.
        if (pointer.Block is not null && pointer.Sized)
        {
            (moved.Rows, moved.Cols) = (1, (int)Math.Max(0, ((long)pointer.Rows * pointer.Cols) - n));
        }

        return JgsValue.External(moved);
    }

    /// <summary><c>setdatatype(p, type, m, n)</c>.</summary>
    [SupportedOSPlatform("windows")]
    private static void SetDataType(JGraphScriptGlobals host, LibPointer pointer, IReadOnlyList<JgsValue> args, int line, int col)
    {
        LivePointer(pointer, line, col);
        string type = args.Count > 1 && IsTextScalar(args[1]) ? TextOf(args[1]) : "";
        if (!PointerType(host, type, out string? element, out LibraryModel? model))
        {
            throw InvalidType(line, col);
        }

        if (type == "stringPtrPtr" && args.Count > 2)
        {
            throw OnlyNumericDimensions(line, col);
        }

        // A value libpointer made keeps the type it was made with (R2025b, probe_shrlib_pointers2).
        if (pointer.Block is not null && type != pointer.DataType)
        {
            throw IllegalConversion(line, col);
        }

        pointer.DataType = type;
        pointer.Element = element;
        pointer.Model = model ?? pointer.Model;
        if (args.Count > 2)
        {
            (pointer.Rows, pointer.Cols) = SizeArguments([.. args.Skip(2)], line, col);
            pointer.Sized = true;
        }
        else
        {
            pointer.Sized = false;
        }
    }

    /// <summary><c>reshape(p, m, n)</c>: the size a numeric pointer's <c>Value</c> reads.</summary>
    private static void ReshapePointer(LibPointer pointer, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw NotOnWindows(line, col);
        }

        LivePointer(pointer, line, col);
        if (pointer.Element is "string" or "cstring" || IsPointerToPointer(pointer.DataType) || StructElement(pointer) is not null)
        {
            throw OnlyNumericDimensions(line, col);
        }

        (pointer.Rows, pointer.Cols) = SizeArguments([.. args.Skip(1)], line, col);
        pointer.Sized = true;
    }

    // ------------------------------------------------------------------------------------------
    // libstruct and lib.<struct>
    // ------------------------------------------------------------------------------------------

    /// <summary><c>libstruct(type)</c> and <c>libstruct(type, s)</c>: zeros, or the struct's fields.</summary>
    [SupportedOSPlatform("windows")]
    private static JgsValue MakeLibStruct(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
        }

        if (args.Count > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:maxrhs", "Too many input arguments.");
        }

        string name = IsTextScalar(args[0]) ? TextOf(args[0]) : "";
        JGraphScriptGlobals host = SessionHost(interpreter, line, col);
        (SharedLibrary library, LibStruct type) = FindStructType(host, name)
            ?? throw new JgsRuntimeException(line, col, "MATLAB:UndefinedFunction", $"Unrecognized function or variable 'lib.{name}'.");
        byte[] bytes = new byte[library.Model.StructLayout(type)!.Value.Size];
        if (args.Count == 2)
        {
            EncodeStruct(library.Model, type, args[1], bytes, line, col);
        }

        var made = new LibStructValue(library, type, NativeBlock.Allocate(library.Host, bytes));
        library.LiveStructs.Add(new WeakReference<LibStructValue>(made));
        return JgsValue.External(made);
    }

    /// <summary>The member of a libstruct named <paramref name="field"/>, and its offset; -1 when it has none.</summary>
    private static (int Index, int Offset) MemberOf(LibStructValue structure, string field)
    {
        for (int i = 0; i < structure.Type.Members.Count; i++)
        {
            if (structure.Type.Members[i].Name == field)
            {
                return (i, structure.Model.StructLayout(structure.Type)!.Value.Offsets[i]);
            }
        }

        return (-1, 0);
    }

    [SupportedOSPlatform("windows")]
    private static JgsValue ReadMember(LibStructValue structure, int index, int offset, int line, int col)
    {
        LiveStruct(structure, line, col);
        NativeHostProcess process = structure.Block.Process;
        string type = structure.Type.Members[index].Type;
        (string element, int count, _) = MemberShape(type);
        int size = ElementSizeOf(structure.Model, element) * count;
        return MemberValue(process, structure.Model, type, process.Read(structure.Block.Address + offset, size));
    }

    [SupportedOSPlatform("windows")]
    private static void WriteMember(LibStructValue structure, string field, JgsValue value, int line, int col)
    {
        LiveStruct(structure, line, col);
        (int index, int offset) = MemberOf(structure, field);
        if (index < 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:noPublicFieldForClass", $"Unrecognized property '{field}' for class '{structure.ClassName}'.");
        }

        string type = structure.Type.Members[index].Type;
        (string element, int count, _) = MemberShape(type);
        byte[] bytes = new byte[ElementSizeOf(structure.Model, element) * count];
        EncodeMember(structure.Model, type, value, bytes, line, col);
        structure.Block.Process.Write(structure.Block.Address + offset, bytes);
    }

    /// <summary>The fields of a libstruct as a MATLAB struct: <c>get(s)</c>.</summary>
    [SupportedOSPlatform("windows")]
    private static JgsValue StructFields(LibStructValue structure, int line, int col)
    {
        LiveStruct(structure, line, col);
        return ReadStruct(structure.Block.Process, structure.Model, structure.Type, structure.Block.Address);
    }

    /// <summary>How a libstruct shows itself: its type's name and its fields (R2025b, shrlib_structs).</summary>
    [SupportedOSPlatform("windows")]
    internal static string LibStructDisplay(LibStructValue structure)
    {
        var text = new StringBuilder(structure.Type.Name).Append(" with properties:\n");
        if (structure.Deleted || !structure.Block.Process.IsAlive)
        {
            return text.ToString();
        }

        JgsValue fields = StructFields(structure, 0, 0);
        foreach ((string name, JgsValue value) in fields.WritableStruct())
        {
            text.Append("\n    ").Append(name).Append(": ").Append(Net.NetDisplay.Shown(value));
        }

        return text.ToString();
    }

    // ------------------------------------------------------------------------------------------
    // The interpreter's entry points: dots, methods, operators
    // ------------------------------------------------------------------------------------------

    /// <summary>Whether <paramref name="value"/> is a <c>lib.pointer</c> or a libstruct.</summary>
    internal static bool IsLibValue(IJgsExternal? value) => value is LibPointer or LibStructValue;

    /// <summary><c>p.Value</c>, <c>p.DataType</c>, <c>s.field</c>, <c>s.structsize</c>.</summary>
    internal static JgsValue LibMember(IJgsExternal target, string field, int line, int col)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw NotOnWindows(line, col);
        }

        switch (target)
        {
            case LibPointer pointer when field == "Value":
                return PointerValue(pointer, line, col);
            case LibPointer pointer when field == "DataType":
                return JgsValue.Str(LivePointer(pointer, line, col).DataType);
            case LibPointer pointer when field == "isNull":
                return JgsValue.Bool(LivePointer(pointer, line, col).IsNull);
            case LibStructValue structure when field == "structsize":
                return JgsValue.Number(LiveStruct(structure, line, col).Block.Size);
            case LibStructValue structure when MemberOf(structure, field) is (>= 0, var offset) found:
                return ReadMember(structure, found.Index, offset, line, col);
            default:
                throw new JgsRuntimeException(line, col, "MATLAB:noSuchMethodOrField",
                    $"Unrecognized method, property, or field '{field}' for class '{target.ClassName}'.");
        }
    }

    /// <summary><c>p.Value = v</c> and <c>s.field = v</c>.</summary>
    internal static void LibAssign(JGraphScriptGlobals? host, IJgsExternal target, string field, JgsValue value, int line, int col)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw NotOnWindows(line, col);
        }

        switch (target)
        {
            case LibPointer pointer when field == "Value":
                SetPointerValue(host ?? throw NoHost(line, col), pointer, value, construct: false, line, col);
                return;
            case LibPointer when field == "DataType":
                throw new JgsRuntimeException(line, col, "MATLAB:class:SetProhibited",
                    "Unable to set the 'DataType' property of class 'lib.pointer' because it is read-only.");
            case LibStructValue structure:
                WriteMember(structure, field, value, line, col);
                return;
            default:
                throw new JgsRuntimeException(line, col, "MATLAB:noPublicFieldForClass",
                    $"Unrecognized property '{field}' for class '{target.ClassName}'.");
        }
    }

    /// <summary>The property names <c>properties</c>, <c>fieldnames</c> and <c>get</c> list.</summary>
    internal static IEnumerable<string> LibPropertyNames(IJgsExternal target) => target switch
    {
        LibPointer => LibPointer.PropertyNames,
        LibStructValue structure => structure.Type.Members.Select(static m => m.Name),
        _ => [],
    };

    /// <summary>The method names <c>methods</c> lists.</summary>
    internal static IEnumerable<string> LibMethodNames(IJgsExternal target) => target switch
    {
        LibPointer => LibPointer.MethodNames,
        LibStructValue structure => structure.MethodNames,
        _ => [],
    };

    private static readonly HashSet<string> PointerMethods = ["isNull", "setdatatype", "reshape", "plus", "get", "set", "delete", "isvalid"];
    private static readonly HashSet<string> StructMethods = ["get", "set", "delete", "isvalid", "structsize"];

    /// <summary>
    /// The method a call <c>name(…, p, …)</c> reaches on a <c>lib.pointer</c> or a libstruct: the
    /// user-method layer of the search order, above the built-ins, as a class's methods are (M145).
    /// </summary>
    internal static bool TryLibMethod(Interpreter interpreter, string name, JgsValue dominant, out IJgsCallable? callable)
    {
        callable = null;
        IJgsExternal? target = dominant.AsExternalOrNull();
        bool known = target switch
        {
            LibPointer => PointerMethods.Contains(name),
            LibStructValue => StructMethods.Contains(name),
            _ => false,
        };
        if (!known || !OperatingSystem.IsWindows())
        {
            return false;
        }

        callable = new LibMethod(name, (args, wanted, line, col) =>
            OperatingSystem.IsWindows() ? RunLibMethod(interpreter.Host, name, target!, args, wanted, line, col) : throw NotOnWindows(line, col));
        return true;
    }

    private static JgsRuntimeException NoHost(int line, int col) =>
        new(line, col, "JGraph:loadlibrary:NotSupported", "This session has no native host.");

    /// <summary>
    /// <c>get</c>, <c>set</c>, <c>delete</c> and <c>isvalid</c> with a <c>lib.pointer</c> or a libstruct
    /// first: the built-ins answer for it themselves, which is the road the JGS dialect takes, since it
    /// dispatches no methods (ADR 0172).
    /// </summary>
    internal static bool TryLibBuiltin(string name, JGraphScriptGlobals? host, IReadOnlyList<JgsValue> args, int line, int col, out JgsValue result)
    {
        result = JgsValue.Null;
        if (args.Count == 0 || !IsLibValue(args[0].AsExternalOrNull()) || !OperatingSystem.IsWindows())
        {
            return false;
        }

        result = RunLibMethod(host, name, args[0].AsExternal, args, name is "get" or "isvalid" ? 1 : 0, line, col) is [var first, ..] ? first : JgsValue.Null;
        return true;
    }

    [SupportedOSPlatform("windows")]
    private static JgsValue[] RunLibMethod(JGraphScriptGlobals? host, string name, IJgsExternal target, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        JgsValue[] Nothing()
        {
            return wanted > 0 ? throw NoOutputs(line, col) : [];
        }

        switch (name)
        {
            case "isvalid":
                return [JgsValue.Bool(target is LibPointer { Deleted: false } or LibStructValue { Deleted: false })];
            case "delete":
                if (target is LibPointer deletedPointer)
                {
                    deletedPointer.Deleted = true;
                }
                else if (target is LibStructValue deletedStruct)
                {
                    deletedStruct.Deleted = true;
                }

                return Nothing();
            case "get":
                return [LibGet(target, args, line, col)];
            case "set":
                if (args.Count % 2 != 1)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:class:BadParamValuePairs", "Invalid parameter/value pair arguments.");
                }

                for (int i = 1; i < args.Count; i += 2)
                {
                    LibAssign(host, target, IsTextScalar(args[i]) ? TextOf(args[i]) : "", args[i + 1], line, col);
                }

                return Nothing();
            case "structsize":
                return [JgsValue.Number(LiveStruct((LibStructValue)target, line, col).Block.Size)];
        }

        var pointer = (LibPointer)target;
        switch (name)
        {
            case "isNull":
                return [JgsValue.Bool(LivePointer(pointer, line, col).IsNull)];
            case "plus":
                if (args.Count != 2)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:dispatcher:InexactMatch", "Invalid number of input arguments.");
                }

                return [LibOperator(TokenType.Plus, args[0], args[1], line, col)];
            case "reshape":
                ReshapePointer(pointer, args, line, col);
                return Nothing();
            default: // setdatatype
                SetDataType(host ?? throw NoHost(line, col), pointer, args, line, col);
                return Nothing();
        }
    }

    [SupportedOSPlatform("windows")]
    private static JgsValue LibGet(IJgsExternal target, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count > 1)
        {
            return LibMember(target, IsTextScalar(args[1]) ? TextOf(args[1]) : "", line, col);
        }

        if (target is LibStructValue structure)
        {
            return StructFields(structure, line, col);
        }

        var pointer = (LibPointer)target;
        return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Value"] = PointerValue(pointer, line, col),
            ["DataType"] = JgsValue.Str(pointer.DataType),
        });
    }

    /// <summary>
    /// An operator with a <c>lib.pointer</c> or a libstruct on either side: <c>p + n</c>, identity under
    /// <c>==</c> and <c>~=</c>, and R2025b's refusal for everything else.
    /// </summary>
    internal static JgsValue LibOperator(TokenType op, JgsValue left, JgsValue right, int line, int col)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw NotOnWindows(line, col);
        }

        if (op is TokenType.EqualEqual or TokenType.BangEqual)
        {
            bool same = left.AsExternalOrNull() is { } a && ReferenceEquals(a, right.AsExternalOrNull());
            return JgsValue.Bool(op == TokenType.EqualEqual ? same : !same);
        }

        if (op == TokenType.Plus && left.AsExternalOrNull() is LibPointer pointer)
        {
            return PointerPlus(pointer, right, line, col);
        }

        if (op == TokenType.Plus && right.AsExternalOrNull() is LibPointer onRight)
        {
            return PointerPlus(onRight, left, line, col);
        }

        throw new JgsRuntimeException(line, col, "MATLAB:math:mustBeNumericCharOrLogical", "Invalid data type. Argument must be numeric, char, or logical.");
    }

    /// <summary>A lib.pointer's or a libstruct's method as a callable the resolver hands back.</summary>
    private sealed class LibMethod(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> body) : IJgsCallable, IJgsMultiCallable
    {
        public string Name => name;

        public JgsValue Call(IReadOnlyList<JgsValue> arguments, int line, int column) =>
            body(arguments, 1, line, column) is [var first, ..] ? first : JgsValue.Null;

        public JgsValue[] CallMultiple(IReadOnlyList<JgsValue> arguments, int wanted, int line, int column) =>
            body(arguments, wanted, line, column);
    }

    // ------------------------------------------------------------------------------------------
    // calllib's arguments
    // ------------------------------------------------------------------------------------------

    /// <summary>One <c>calllib</c> in progress: the memory it made for its arguments, and its pointer outputs.</summary>
    [SupportedOSPlatform("windows")]
    private sealed class LibCall(SharedLibrary library, JgsDialect dialect, int line, int col)
    {
        public NativeHostProcess Process => library.Host;

        public LibraryModel Model => library.Model;

        public JgsDialect Dialect => dialect;

        public int Line => line;

        public int Col => col;

        /// <summary>Host memory made for this call's arguments, freed when it returns.</summary>
        public List<long> Temporaries { get; } = [];

        /// <summary>The pointer arguments' values after the call, in argument order.</summary>
        public List<Func<JgsValue>> Outputs { get; } = [];

        /// <summary>Host memory holding <paramref name="bytes"/>, freed when the call returns.</summary>
        public long Temporary(ReadOnlySpan<byte> bytes)
        {
            long address = Process.Alloc(Math.Max(bytes.Length, 1));
            Temporaries.Add(address);
            if (bytes.Length > 0)
            {
                Process.Write(address, bytes);
            }

            return address;
        }

        /// <summary>A pointer argument, alive and in this call's host.</summary>
        public LibPointer Passed(LibPointer pointer)
        {
            LivePointer(pointer, line, col);
            if (!pointer.IsNull && !ReferenceEquals(pointer.Process, Process))
            {
                throw new JgsRuntimeException(line, col, "JGraph:libpointer:HostExited",
                    "The native host this pointer points into has exited; the pointer no longer points at anything.");
            }

            return pointer;
        }
    }

    /// <summary>The host slot a parameter of <paramref name="type"/> travels in.</summary>
    private static JGraph.NativeHost.Slot ParameterSlot(LibraryModel model, string type)
    {
        if (LibTypes.IsScalar(type) || model.Enum(type) is not null)
        {
            return ScalarSlot(model, type);
        }

        if (model.Struct(type) is { } structure && model.StructLayout(structure) is { } layout)
        {
            return new JGraph.NativeHost.Slot(JGraph.NativeHost.SlotKind.Struct, layout.Size);
        }

        return new JGraph.NativeHost.Slot(JGraph.NativeHost.SlotKind.Pointer);
    }

    /// <summary>
    /// Writes one argument into its cell of the call request, making whatever host memory it needs, and
    /// records the value its pointer comes back as.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void Argument(LibCall call, string type, JgsValue value, Span<byte> cell)
    {
        int line = call.Line, col = call.Col;
        LibraryModel model = call.Model;
        if (LibTypes.IsScalar(type))
        {
            WriteScalar(cell, ScalarSlot(model, type).Kind, ScalarArgument(type, value, line, col));
            return;
        }

        if (model.Enum(type) is { } enumeration)
        {
            WriteScalar(cell, JGraph.NativeHost.SlotKind.Int32, EnumArgument(enumeration, value, line, col));
            return;
        }

        if (model.Struct(type) is { } byValue)
        {
            StructBytes(model, byValue, value, line, col).CopyTo(cell);
            return;
        }

        if (type is "error" or "FcnPtr" || type.EndsWith("PtrPtrPtr", StringComparison.Ordinal) || !LibTypes.IsPointer(type))
        {
            throw UnknownConversion(value, call.Dialect, line, col);
        }

        long address = type switch
        {
            "cstring" => CStringArgument(call, value),
            "stringPtrPtr" => StringsArgument(call, value),
            "voidPtr" => VoidArgument(call, value),
            _ when IsPointerToPointer(type) => PointerToPointerArgument(call, type, value),
            _ when model.Struct(type[..^3]) is { } pointee => StructPointerArgument(call, type, pointee, value),
            _ => NumericPointerArgument(call, type, model.Enum(type[..^3]) is not null ? "int32" : type[..^3], value),
        };
        BinaryPrimitives.WriteInt64LittleEndian(cell, address);
    }

    /// <summary>
    /// A scalar argument (R2025b, shrlib_types): a real numeric or logical scalar, rounded and saturated
    /// into the parameter by the host's cell; NaN only for a float; <c>bool</c> as whether it is nonzero.
    /// </summary>
    private static double ScalarArgument(string type, JgsValue value, int line, int col)
    {
        bool logical = type == "bool";
        if (IsComplexValue(value))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:class:RequireReal", "Value must be real.");
        }

        NumericData numbers = NumericOf(value) ?? throw (logical
            ? new JgsRuntimeException(line, col, "MATLAB:class:RequireLogical", "Value must be logical (true or false).")
            : new JgsRuntimeException(line, col, "MATLAB:class:RequireNumeric", "Value must be numeric or logical."));
        if (numbers.Values.Length != 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:class:RequireScalar", "Value must be a scalar.");
        }

        double x = numbers.Values[0];
        if (double.IsNaN(x))
        {
            if (logical)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:nologicalnan", "NaN values cannot be converted to logicals.");
            }

            if (type is not ("single" or "double"))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:class:RequireNumber", "Value must be a number.");
            }
        }

        return logical ? (x != 0 ? 1 : 0) : x;
    }

    /// <summary>An enum argument: a member's name (char or string), or a number (R2025b, shrlib_structs).</summary>
    private static double EnumArgument(LibEnum enumeration, JgsValue value, int line, int col)
    {
        if (IsTextScalar(value))
        {
            string member = TextOf(value);
            foreach ((string name, long number) in enumeration.Members)
            {
                if (name == member)
                {
                    return number;
                }
            }

            throw new JgsRuntimeException(line, col, "MATLAB:class:InvalidEnumValue", $"There is no enumerated value named '{member}'.");
        }

        return ScalarArgument("int32", value, line, col);
    }

    /// <summary>A <c>T*</c> argument: an array (its elements converted to T, and back after the call in T's class and its own shape), a pointer, or <c>[]</c> for NULL.</summary>
    [SupportedOSPlatform("windows")]
    private static long NumericPointerArgument(LibCall call, string type, string element, JgsValue value)
    {
        int line = call.Line, col = call.Col;
        if (value.AsExternalOrNull() is LibPointer pointer)
        {
            call.Passed(pointer);
            if (!pointer.IsNull && pointer.DataType != type && pointer.DataType != element && pointer.DataType is not ("voidPtr" or ""))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:libpointer:PointerTypesMustMatch", "Pointer types must match data type.");
            }

            call.Outputs.Add(() => PointerOutput(pointer));
            return pointer.Address;
        }

        NumericData numbers = NumericOf(value) ?? throw IllegalConversion(line, col);
        if (element == "bool" && numbers.Values.Length != 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:class:RequireScalar", "Value must be a scalar.");
        }

        if (numbers.Values.Length == 0)
        {
            call.Outputs.Add(static () => JgsEmpty.Zero());
            return 0;
        }

        long address = call.Temporary(Encode(element, numbers.Values));
        NativeHostProcess process = call.Process;
        call.Outputs.Add(() => ReadArray(process, address, element, numbers.Rows, numbers.Cols));
        return address;
    }

    /// <summary>A pointer argument's output: its value, or a pointer to the same place when it has none.</summary>
    [SupportedOSPlatform("windows")]
    private static JgsValue PointerOutput(LibPointer pointer)
    {
        try
        {
            return PointerValue(pointer, 0, 0);
        }
        catch (JgsRuntimeException)
        {
            return JgsValue.External(pointer.Alias());
        }
    }

    /// <summary>A <c>void*</c> argument: a pointer (back as a new pointer to the same place), a libstruct, an array in its own class, or <c>[]</c>.</summary>
    [SupportedOSPlatform("windows")]
    private static long VoidArgument(LibCall call, JgsValue value)
    {
        switch (value.AsExternalOrNull())
        {
            case LibPointer pointer:
                call.Passed(pointer);
                call.Outputs.Add(() => JgsValue.External(pointer.Alias()));
                return pointer.Address;
            case LibStructValue structure:
                LiveStruct(structure, call.Line, call.Col);
                call.Outputs.Add(() => JgsValue.External(structure));
                return structure.Block.Address;
        }

        NumericData numbers = NumericOf(value) ?? throw IllegalConversion(call.Line, call.Col);
        if (numbers.Values.Length == 0)
        {
            call.Outputs.Add(static () => JgsEmpty.Zero());
            return 0;
        }

        long address = call.Temporary(Encode(numbers.Element, numbers.Values));
        NativeHostProcess process = call.Process;
        call.Outputs.Add(() => ReadArray(process, address, numbers.Element, numbers.Rows, numbers.Cols));
        return address;
    }

    /// <summary>A <c>char*</c> argument: a character vector (back as the text the call left), a C string pointer, or <c>[]</c>.</summary>
    [SupportedOSPlatform("windows")]
    private static long CStringArgument(LibCall call, JgsValue value)
    {
        NativeHostProcess process = call.Process;
        if (value.Type == JgsType.String && !value.IsStringArray && !value.IsCharMatrix)
        {
            long address = call.Temporary(CStringBytes(value.AsString));
            call.Outputs.Add(() => JgsValue.Str(ReadNativeString(process, address)));
            return address;
        }

        if (value.AsExternalOrNull() is LibPointer pointer)
        {
            call.Passed(pointer);
            if (pointer.DataType is not ("cstring" or "voidPtr" or ""))
            {
                throw new JgsRuntimeException(call.Line, call.Col, "MATLAB:libpointer:PointerTypeMustMatch", "Pointer type must match data type");
            }

            call.Outputs.Add(() => JgsValue.Str(pointer.IsNull ? "" : ReadNativeString(process, pointer.Address)));
            return pointer.Address;
        }

        if (NumericOf(value) is { Values.Length: 0 })
        {
            call.Outputs.Add(static () => JgsValue.Str(""));
            return 0;
        }

        throw new JgsRuntimeException(call.Line, call.Col, "MATLAB:libpointer:MustBeString", "Parameter can not be converted to a character vector");
    }

    /// <summary>A <c>char**</c> argument: a cell of character vectors (back as a cell of what the strings hold after the call), a pointer, or <c>[]</c>.</summary>
    [SupportedOSPlatform("windows")]
    private static long StringsArgument(LibCall call, JgsValue value)
    {
        NativeHostProcess process = call.Process;
        if (IsCellOfChars(value))
        {
            JgsValue[] texts = value.AsCell;
            byte[] table = new byte[texts.Length * 8];
            for (int i = 0; i < texts.Length; i++)
            {
                BinaryPrimitives.WriteInt64LittleEndian(table.AsSpan(i * 8, 8), call.Temporary(CStringBytes(texts[i].AsString)));
            }

            long address = call.Temporary(table);
            (int rows, int cols) = (value.Rows, value.Cols);
            call.Outputs.Add(() => ReadStrings(process, address, rows, cols));
            return address;
        }

        if (value.AsExternalOrNull() is LibPointer pointer)
        {
            call.Passed(pointer);
            call.Outputs.Add(() => PointerOutput(pointer));
            return pointer.Address;
        }

        if (NumericOf(value) is { Values.Length: 0 })
        {
            call.Outputs.Add(static () => JgsEmpty.Zero());
            return 0;
        }

        throw new JgsRuntimeException(call.Line, call.Col, "MATLAB:class:MustBeCellStringsParameter", "Parameter must be a cell array of character vectors.");
    }

    /// <summary>A <c>S*</c> argument: a MATLAB struct or a libstruct (back as a struct of what the call left), a pointer, or <c>[]</c>.</summary>
    [SupportedOSPlatform("windows")]
    private static long StructPointerArgument(LibCall call, string type, LibStruct structure, JgsValue value)
    {
        NativeHostProcess process = call.Process;
        LibraryModel model = call.Model;
        long address;
        switch (value.AsExternalOrNull())
        {
            case LibStructValue held when held.Type.Name == structure.Name:
                address = LiveStruct(held, call.Line, call.Col).Block.Address;
                break;
            case LibPointer pointer when pointer.DataType is "voidPtr" or "" || pointer.DataType == type:
                address = call.Passed(pointer).Address;
                break;
            default:
                if (NumericOf(value) is { Values.Length: 0 })
                {
                    call.Outputs.Add(static () => JgsEmpty.Zero());
                    return 0;
                }

                address = call.Temporary(StructBytes(model, structure, value, call.Line, call.Col));
                break;
        }

        call.Outputs.Add(() => address == 0 ? JgsEmpty.Zero() : ReadStruct(process, model, structure, address));
        return address;
    }

    /// <summary>
    /// A <c>T**</c> argument (R2025b, shrlib_pointers and probe_shrlib_pointers2): a <c>T*</c> pointer is
    /// passed by the address of its address, and moves to where the call pointed it; a NULL pointer to
    /// a pointer, or <c>[]</c>, gets a cell of the call's own; either way the output is a new pointer to
    /// where the call left it (for a struct, the struct there).
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static long PointerToPointerArgument(LibCall call, string type, JgsValue value)
    {
        NativeHostProcess process = call.Process;
        LibraryModel model = call.Model;
        string inner = type[..^3];
        LibStruct? target = model.Struct(inner[..^3]);

        JgsValue Result(long address) => target is not null
            ? address == 0 ? JgsEmpty.Zero() : ReadStruct(process, model, target, address)
            : JgsValue.External(new LibPointer(inner)
            {
                Process = process,
                Address = address,
                Element = ElementOfPointer(model, inner),
                Model = model,
            });

        if (value.AsExternalOrNull() is LibPointer pointer)
        {
            call.Passed(pointer);
            if (pointer.DataType == inner || (pointer.IsNull && pointer.DataType.Length == 0))
            {
                byte[] held = new byte[8];
                BinaryPrimitives.WriteInt64LittleEndian(held, pointer.Address);
                long cellAddress = call.Temporary(held);
                call.Outputs.Add(() =>
                {
                    long now = ReadAddress(process, cellAddress);
                    if (now != pointer.Address)
                    {
                        pointer.Address = now;
                        pointer.Process = process;
                        pointer.Block = null;
                        pointer.Sized = false;
                    }

                    return Result(now);
                });
                return cellAddress;
            }

            if (pointer.DataType != type)
            {
                throw new JgsRuntimeException(call.Line, call.Col, "MATLAB:libpointer:PointerTypesMustMatch", "Pointer types must match data type.");
            }

            long cell = pointer.IsNull ? call.Temporary(new byte[8]) : pointer.Address;
            call.Outputs.Add(() => Result(ReadAddress(process, cell)));
            return cell;
        }

        NumericData numbers = NumericOf(value) ?? throw IllegalConversion(call.Line, call.Col);
        if (numbers.Values.Length == 0)
        {
            long cell = call.Temporary(new byte[8]);
            call.Outputs.Add(() => Result(ReadAddress(process, cell)));
            return cell;
        }

        long address = call.Temporary(Encode(numbers.Element, numbers.Values));
        call.Outputs.Add(() => ReadArray(process, address, numbers.Element, numbers.Rows, numbers.Cols));
        return address;
    }

    /// <summary>
    /// A pointer a library returned: of its type, its value not defined until a size is given (a C
    /// string array reads one string, and a struct pointer its struct, until then).
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static JgsValue ReturnedPointer(NativeHostProcess process, LibraryModel model, string type, long address) =>
        JgsValue.External(new LibPointer(type)
        {
            Process = process,
            Address = address,
            Element = ElementOfPointer(model, type),
            Model = model,
        });
}
