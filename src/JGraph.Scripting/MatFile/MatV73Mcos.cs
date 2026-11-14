using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using JGraph.Scripting.MatFile.Hdf5;
using static JGraph.Scripting.MatFile.MatConstants;

namespace JGraph.Scripting.MatFile;

/// <summary>
/// A version 7.3 MAT-file with MATLAB's object subsystem - App Designer's <c>appModel.mat</c> saved
/// under MATLAB's version 7.3 preference, and the shipped <c>serdes/SignalIntegrityLink.mlapp</c> -
/// carried to the level-5 file it stands for and back (open item 147, ADR 0357), so that
/// <see cref="MatMcosFile"/> reads its component tree and a save writes it in the form it came in.
/// </summary>
/// <remarks>
/// <para>
/// The two forms hold the same things in different wrappings (measured on both models with
/// <c>h5info</c>, 2026-11-14). The subsystem is <c>#subsystem#/MCOS</c>, a dataset of object
/// references marked <c>FileWrapper__</c> and <c>MATLAB_object_decode</c> 3, whose referents are the
/// level-5 <c>FileWrapper__</c> cell's items: the <c>uint8</c> object table, an empty value (a
/// <c>canonical empty</c> dataset, the bare element of level 5), the stored values, and the three
/// per-class cells. An object held by a variable, a field or a cell's element is its reference words,
/// a <c>uint32</c> dataset whose <c>MATLAB_class</c> is the object's class, marked
/// <c>MATLAB_object_decode</c> 3 (the level-5 opaque element); inside the subsystem's values a
/// reference is a plain <c>uint32</c> array in both forms. Everything else is MATLAB's plain-value
/// layout (<see cref="MatV73Reader"/>, <see cref="MatV73Writer"/>): char as <c>uint16</c>, logical as
/// <c>uint8</c>, an empty value as the list of its dimensions, a cell as references into
/// <c>#refs#</c>, a struct as a group, a struct array as a group of reference datasets.
/// </para>
/// <para>
/// Written back, the file is new, as App Designer's own save makes it: every value in
/// <c>#refs#</c> or at the root in the plain-value layout, without the <c>H5PATH</c> attributes
/// nothing reads, keeping the description text the file had. Anything outside the kinds both
/// models hold (a sparse array, a function handle, an old-style object) is refused, and the tree is
/// then left as it was.
/// </para>
/// </remarks>
internal static class MatV73Mcos
{
    private const string Subsystem = "#subsystem#";
    private const string Refs = "#refs#";
    private const string ObjectDecode = "MATLAB_object_decode";

    /// <summary>
    /// The level-5 file a version 7.3 file with a subsystem stands for; null, with
    /// <paramref name="why"/>, when it is not one or holds a kind this does not carry.
    /// </summary>
    public static byte[]? ToLevel5(byte[] v73, out string? why)
    {
        why = null;
        try
        {
            Hdf5File file = Hdf5File.Open(v73);
            Hdf5Object root = file.Root;
            if (root.Child(Subsystem)?.Child("MCOS") is not { IsGroup: false } wrapper
                || ClassOf(wrapper) != "FileWrapper__" || wrapper.Datatype.Class != Hdf5Datatype.ClassReference)
            {
                why = "The version 7.3 model has no object subsystem.";
                return null;
            }

            var variables = new List<byte[]>();
            foreach (string name in root.ChildNames)
            {
                if (name is Subsystem or Refs || root.Child(name) is not { } node)
                {
                    continue;
                }

                variables.Add(Element(node, name));
            }

            byte[][] cells = [.. References(wrapper).Select(a => Element(wrapper.At(a), string.Empty))];
            return MatMcosWriter.Level5File(variables, cells);
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or IndexOutOfRangeException or ArgumentException)
        {
            why = $"The version 7.3 model cannot be read as objects: {ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// The version 7.3 file holding what the level-5 file <paramref name="level5"/> holds, its
    /// description the 116 bytes of <paramref name="description"/>.
    /// </summary>
    /// <exception cref="NotSupportedException">A value is of a kind this does not carry.</exception>
    public static byte[] FromLevel5(byte[] level5, byte[] description)
    {
        long subsystemAt = BinaryPrimitives.ReadInt64LittleEndian(level5.AsSpan(116));
        var builder = new MatV73Writer.Builder();
        var root = new List<(string Name, long Header)>();
        byte[]? subsystem = null;
        int at = 128;
        while (at + 8 <= level5.Length)
        {
            int type = BinaryPrimitives.ReadInt32LittleEndian(level5.AsSpan(at));
            int length = BinaryPrimitives.ReadInt32LittleEndian(level5.AsSpan(at + 4));
            byte[] element = type == MiCompressed ? Inflate(level5, at + 8, length) : level5[at..(at + 8 + length)];
            if (at == subsystemAt)
            {
                subsystem = element;
            }
            else
            {
                root.Add((MatRaw.NameOf(element), Write(builder, element)));
            }

            at += 8 + length;
        }

        if (subsystem is null || MatRaw.Uint8Data(subsystem) is not { Length: > 16 } stream)
        {
            throw new NotSupportedException("the model has no subsystem");
        }

        int holderLength = BinaryPrimitives.ReadInt32LittleEndian(stream.AsSpan(12));
        if (MatRaw.StructFields(stream[8..(16 + holderLength)]) is not [("MCOS", var holder)]
            || MatRaw.Opaque(holder) is not { ClassName: "FileWrapper__" } opaque
            || MatRaw.CellItems(opaque.Content) is not { } items)
        {
            throw new NotSupportedException("the model's subsystem is not a FileWrapper__ cell");
        }

        long[] addresses = [.. items.Select(i => builder.Listed(Write(builder, i)))];
        root.Add((Subsystem, builder.Group([("MCOS", builder.Wrapper(addresses, [items.Count, 1]))], [], out _, out _)));
        if (builder.Refs.Count > 0)
        {
            root.Add((Refs, builder.Group(builder.Refs, [], out _, out _)));
        }

        long rootHeader = builder.Group(root, [], out long tree, out long heap);
        return builder.Finish(rootHeader, tree, heap, description);
    }

    // --- HDF5 to level 5 -----------------------------------------------------------------------------

    private static string ClassOf(Hdf5Object node) =>
        node.Attributes.TryGetValue("MATLAB_class", out Hdf5Attribute? tag) ? tag.AsText() : node.IsGroup ? "struct" : "double";

    private static bool Flagged(Hdf5Object node, string attribute) =>
        node.Attributes.TryGetValue(attribute, out Hdf5Attribute? flag) && flag.AsNumber() != 0;

    /// <summary>MATLAB's dimensions for a dataset: HDF5's reversed; a scalar is 1-by-1 and one dimension a row.</summary>
    private static int[] Shape(Hdf5Object node)
    {
        IReadOnlyList<long> dims = node.Dims;
        return dims.Count switch
        {
            0 => [1, 1],
            1 => [1, (int)dims[0]],
            _ => [.. dims.Reverse().Select(static d => (int)d)],
        };
    }

    private static List<long> References(Hdf5Object node)
    {
        byte[] data = node.ReadData();
        int size = node.Datatype.Size;
        var addresses = new List<long>(data.Length / size);
        for (int i = 0; i + size <= data.Length; i += size)
        {
            addresses.Add(Hdf5File.ReadWord(data, i, size));
        }

        return addresses;
    }

    /// <summary>One object as the level-5 matrix element under <paramref name="name"/> that holds the same value.</summary>
    private static byte[] Element(Hdf5Object node, string name)
    {
        string className = ClassOf(node);
        if (Flagged(node, "MATLAB_empty"))
        {
            return EmptyElement(node, name, className);
        }

        if (node.IsGroup)
        {
            if (node.Attributes.ContainsKey("MATLAB_sparse") || className != "struct")
            {
                throw new NotSupportedException($"a {(node.Attributes.ContainsKey("MATLAB_sparse") ? "sparse array" : className)} is not carried");
            }

            return StructElement(node, name);
        }

        if (Flagged(node, ObjectDecode))
        {
            // An object where a variable, field or cell holds one: its reference words in an opaque element.
            byte[] words = Numbers(node, "uint32");
            return MatMcosWriter.Opaque(name, className, NumericElement(string.Empty, "uint32", Shape(node), words, null));
        }

        int[] shape = Shape(node);
        switch (className)
        {
            case "cell":
                if (node.Datatype.Class != Hdf5Datatype.ClassReference)
                {
                    throw new InvalidDataException("a cell is not a dataset of references");
                }

                return MatMcosWriter.Matrix([MatMcosWriter.Flags(MxCell), MatMcosWriter.Dimensions(shape), Name(name),
                    .. References(node).Select(a => Element(node.At(a), string.Empty))]);
            case "char":
                return MatMcosWriter.Matrix(MatMcosWriter.Flags(MxChar), MatMcosWriter.Dimensions(shape), Name(name),
                    MatMcosWriter.Sub(MiUInt16, Numbers(node, "uint16")));
            case "logical":
                return NumericElement(name, "logical", shape, Numbers(node, "uint8"), null);
            default:
                if (node.Datatype.Class == Hdf5Datatype.ClassCompound)
                {
                    (byte[] real, byte[] imaginary) = ComplexParts(node, className);
                    return NumericElement(name, className, shape, real, imaginary);
                }

                return NumericElement(name, className, shape, Numbers(node, className), null);
        }
    }

    private static byte[] EmptyElement(Hdf5Object node, string name, string className)
    {
        if (className == "canonical empty")
        {
            return MatMcosWriter.EmptyElement();
        }

        int[] dims = node.IsGroup || node.Datatype.Class == Hdf5Datatype.ClassReference
            ? [0, 0]
            : [.. Numbers(node, "uint64", out int count).Chunk(8).Take(count).Select(static b => (int)BinaryPrimitives.ReadUInt64LittleEndian(b))];
        if (dims.Length < 2)
        {
            dims = [0, 0];
        }

        switch (className)
        {
            case "struct":
                string[] fields = node.Attributes.TryGetValue("MATLAB_fields", out Hdf5Attribute? declared) ? [.. declared.Texts()] : [];
                return MatMcosWriter.StructElement(name, fields, [], dims);
            case "cell":
                return MatMcosWriter.Matrix([MatMcosWriter.Flags(MxCell), MatMcosWriter.Dimensions(dims), Name(name)]);
            case "char":
                return MatMcosWriter.Matrix(MatMcosWriter.Flags(MxChar), MatMcosWriter.Dimensions(dims), Name(name), MatMcosWriter.Sub(MiUInt16, []));
            default:
                return NumericElement(name, className, dims, [], null);
        }
    }

    private static byte[] StructElement(Hdf5Object node, string name)
    {
        string[] fields = node.Attributes.TryGetValue("MATLAB_fields", out Hdf5Attribute? declared)
            ? [.. declared.Texts()]
            : [.. node.ChildNames];
        Hdf5Object[] children = [.. fields.Select(f => node.Child(f) ?? throw new InvalidDataException($"a struct names a field '{f}' it does not hold"))];

        // A struct array: each field a dataset of references, one per element, with no class of its own.
        bool array = children.Length > 0 && Array.TrueForAll(children, static c => !c.IsGroup
            && c.Datatype.Class == Hdf5Datatype.ClassReference && !c.Attributes.ContainsKey("MATLAB_class"));
        if (!array)
        {
            return MatMcosWriter.StructElement(name, fields, [[.. children.Select(static c => Element(c, string.Empty))]], [1, 1]);
        }

        int[] shape = Shape(children[0]);
        List<long>[] columns = [.. children.Select(References)];
        int count = columns[0].Count;
        var elements = new byte[count][];
        for (int i = 0; i < count; i++)
        {
            elements[i] = [.. Enumerable.Range(0, fields.Length).SelectMany(f => Element(node.At(columns[f][i]), string.Empty))];
        }

        // StructElement takes each element's values as separate parts; flattening them keeps the order.
        return MatMcosWriter.StructElement(name, fields, [.. elements.Select(static e => new[] { e })], shape);
    }

    private static byte[] Name(string name) => MatMcosWriter.Sub(MiInt8, Encoding.ASCII.GetBytes(name));

    /// <summary>A dataset's elements in <paramref name="className"/>'s little-endian bytes, column-major.</summary>
    private static byte[] Numbers(Hdf5Object node, string className) => Numbers(node, className, out _);

    private static byte[] Numbers(Hdf5Object node, string className, out int count)
    {
        Hdf5Datatype type = node.Datatype;
        byte[] data = node.ReadData();
        count = type.Size == 0 ? 0 : data.Length / type.Size;
        (int width, bool isFloat, bool signed) = Natural(className);
        if (type.LittleEndian && type.Size == width && (type.Class == Hdf5Datatype.ClassFloat) == isFloat
            && (isFloat || type.Signed == signed || className is "char" or "logical" or "uint8" or "uint16" or "uint32" or "uint64"))
        {
            return data;
        }

        var bytes = new byte[count * width];
        for (int i = 0; i < count; i++)
        {
            WriteAs(bytes.AsSpan(i * width, width), className, type.ElementAt(data, i * type.Size));
        }

        return bytes;
    }

    private static (byte[] Real, byte[] Imaginary) ComplexParts(Hdf5Object node, string className)
    {
        Hdf5Datatype type = node.Datatype;
        if (type.Members.Count != 2)
        {
            throw new InvalidDataException("a complex array has no parts");
        }

        byte[] data = node.ReadData();
        int count = data.Length / type.Size;
        int width = Natural(className).Width;
        var real = new byte[count * width];
        var imaginary = new byte[count * width];
        for (int i = 0; i < count; i++)
        {
            WriteAs(real.AsSpan(i * width, width), className, type.Members[0].Type.ElementAt(data, (i * type.Size) + type.Members[0].Offset));
            WriteAs(imaginary.AsSpan(i * width, width), className, type.Members[1].Type.ElementAt(data, (i * type.Size) + type.Members[1].Offset));
        }

        return (real, imaginary);
    }

    private static byte[] NumericElement(string name, string className, int[] dims, byte[] data, byte[]? imaginary)
    {
        (int arrayClass, int dataType, int flags) = className switch
        {
            "double" => (MxDouble, MiDouble, 0),
            "single" => (MxSingle, MiSingle, 0),
            "logical" => (MxUInt8, MiUInt8, FlagLogical),
            "int8" => (MxInt8, MiInt8, 0),
            "uint8" => (MxUInt8, MiUInt8, 0),
            "int16" => (MxInt16, MiInt16, 0),
            "uint16" => (MxUInt16, MiUInt16, 0),
            "int32" => (MxInt32, MiInt32, 0),
            "uint32" => (MxUInt32, MiUInt32, 0),
            "int64" => (MxInt64, MiInt64, 0),
            "uint64" => (MxUInt64, MiUInt64, 0),
            _ => throw new NotSupportedException($"a {className} is not carried"),
        };
        return imaginary is null
            ? MatMcosWriter.Matrix(MatMcosWriter.Flags(arrayClass | flags), MatMcosWriter.Dimensions(dims), Name(name), MatMcosWriter.Sub(dataType, data))
            : MatMcosWriter.Matrix(MatMcosWriter.Flags(arrayClass | flags | FlagComplex), MatMcosWriter.Dimensions(dims), Name(name),
                MatMcosWriter.Sub(dataType, data), MatMcosWriter.Sub(dataType, imaginary));
    }

    /// <summary>The bytes one element of a class takes, whether it is a float, and whether it is signed.</summary>
    private static (int Width, bool IsFloat, bool Signed) Natural(string className) => className switch
    {
        "double" => (8, true, true),
        "single" => (4, true, true),
        "logical" or "uint8" => (1, false, false),
        "int8" => (1, false, true),
        "char" or "uint16" => (2, false, false),
        "int16" => (2, false, true),
        "uint32" => (4, false, false),
        "int32" => (4, false, true),
        "uint64" => (8, false, false),
        "int64" => (8, false, true),
        _ => throw new NotSupportedException($"a {className} is not carried"),
    };

    private static void WriteAs(Span<byte> into, string className, double value)
    {
        switch (className)
        {
            case "double": BinaryPrimitives.WriteDoubleLittleEndian(into, value); break;
            case "single": BinaryPrimitives.WriteSingleLittleEndian(into, (float)value); break;
            case "logical" or "uint8": into[0] = (byte)value; break;
            case "int8": into[0] = (byte)(sbyte)value; break;
            case "char" or "uint16": BinaryPrimitives.WriteUInt16LittleEndian(into, (ushort)value); break;
            case "int16": BinaryPrimitives.WriteInt16LittleEndian(into, (short)value); break;
            case "uint32": BinaryPrimitives.WriteUInt32LittleEndian(into, (uint)value); break;
            case "int32": BinaryPrimitives.WriteInt32LittleEndian(into, (int)value); break;
            case "uint64": BinaryPrimitives.WriteUInt64LittleEndian(into, (ulong)value); break;
            default: BinaryPrimitives.WriteInt64LittleEndian(into, (long)value); break;
        }
    }

    // --- level 5 to HDF5 -----------------------------------------------------------------------------

    private static byte[] Inflate(byte[] bytes, int at, int length)
    {
        using var input = new MemoryStream(bytes, at, length);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>A data element at <paramref name="at"/>: its type, data, and where the next element starts.</summary>
    private static (int Type, byte[] Data, int Next) Sub(byte[] bytes, int at)
    {
        uint tag = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
        if (tag >> 16 != 0)
        {
            return ((int)(tag & 0xFFFF), bytes[(at + 4)..(at + 4 + (int)(tag >> 16))], at + 8);
        }

        int length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + 4));
        return ((int)tag, bytes[(at + 8)..(at + 8 + length)], at + 8 + ((length + 7) / 8 * 8));
    }

    /// <summary>Writes one level-5 matrix element as the object holding the same value, and answers its address.</summary>
    private static long Write(MatV73Writer.Builder builder, byte[] element)
    {
        if (MatRaw.IsBare(element))
        {
            return builder.EmptyOf("canonical empty", [0, 0], null);
        }

        int end = 8 + BinaryPrimitives.ReadInt32LittleEndian(element.AsSpan(4));
        (_, byte[] flagBytes, int p) = Sub(element, 8);
        int flags = BinaryPrimitives.ReadInt32LittleEndian(flagBytes);
        int arrayClass = flags & 0xFF;
        if (arrayClass == MxOpaque)
        {
            if (MatRaw.Opaque(element) is not { } opaque || MatRaw.Integers(opaque.Content) is null)
            {
                throw new NotSupportedException("an opaque value that is not an object reference is not carried");
            }

            (int[] contentDims, byte[] words) = Words(opaque.Content);
            return builder.ObjectWords(opaque.ClassName, contentDims, words);
        }

        (_, byte[] dimBytes, p) = Sub(element, p);
        int[] dims = new int[dimBytes.Length / 4];
        for (int i = 0; i < dims.Length; i++)
        {
            dims[i] = BinaryPrimitives.ReadInt32LittleEndian(dimBytes.AsSpan(4 * i));
        }

        (_, _, p) = Sub(element, p); // the name, which the caller links the object under
        int count = dims.Aggregate(1, static (a, b) => a * b);
        switch (arrayClass)
        {
            case MxCell:
            {
                var items = new List<long>(count);
                for (int i = 0; i < count; i++)
                {
                    int length = BinaryPrimitives.ReadInt32LittleEndian(element.AsSpan(p + 4));
                    items.Add(builder.Listed(Write(builder, element[p..(p + 8 + length)])));
                    p += 8 + length;
                }

                return count == 0 ? builder.EmptyOf("cell", dims, null) : builder.References(items, dims, cell: true);
            }

            case MxStruct:
            {
                (_, byte[] widthBytes, p) = Sub(element, p);
                int width = BinaryPrimitives.ReadInt32LittleEndian(widthBytes);
                (_, byte[] names, p) = Sub(element, p);
                string[] fields = new string[width == 0 ? 0 : names.Length / width];
                for (int f = 0; f < fields.Length; f++)
                {
                    string field = Encoding.ASCII.GetString(names, f * width, width);
                    int cut = field.IndexOf('\0', StringComparison.Ordinal);
                    fields[f] = cut >= 0 ? field[..cut] : field;
                }

                if (count == 0)
                {
                    return builder.EmptyOf("struct", dims, fields);
                }

                var values = new long[count, fields.Length];
                for (int i = 0; i < count; i++)
                {
                    for (int f = 0; f < fields.Length; f++)
                    {
                        int length = BinaryPrimitives.ReadInt32LittleEndian(element.AsSpan(p + 4));
                        byte[] value = element[p..(p + 8 + length)];
                        values[i, f] = count == 1 ? Write(builder, value) : builder.Listed(Write(builder, value));
                        p += 8 + length;
                    }
                }

                if (count == 1)
                {
                    return builder.StructOf([.. fields.Select((f, k) => (f, values[0, k]))]);
                }

                return builder.StructOf([.. fields.Select((f, k) => (f, builder.References([.. Enumerable.Range(0, count).Select(i => values[i, k])], dims)))]);
            }

            case MxChar:
            {
                string text = p < end ? Text(element, p) : string.Empty;
                if (count == 0)
                {
                    return builder.EmptyOf("char", dims, null);
                }

                byte[] units = Encoding.Unicode.GetBytes(text);
                if (units.Length != count * 2)
                {
                    throw new NotSupportedException("a char array whose text does not fill it is not carried");
                }

                return builder.CharUnits(dims, units);
            }

            case MxDouble or MxSingle or MxInt8 or MxUInt8 or MxInt16 or MxUInt16 or MxInt32 or MxUInt32 or MxInt64 or MxUInt64:
            {
                string className = (flags & FlagLogical) != 0 ? "logical" : ClassName(arrayClass);
                if (count == 0)
                {
                    return builder.EmptyOf(className, dims, null);
                }

                (int type, byte[] real, p) = Sub(element, p);
                byte[] realBytes = AsClass(type, real, className, count);
                byte[]? imaginaryBytes = null;
                if ((flags & FlagComplex) != 0)
                {
                    (int imaginaryType, byte[] imaginary, _) = Sub(element, p);
                    imaginaryBytes = AsClass(imaginaryType, imaginary, className, count);
                }

                return builder.Numeric(className, dims, realBytes, imaginaryBytes);
            }

            default:
                throw new NotSupportedException(arrayClass switch
                {
                    MxSparse => "a sparse array is not carried",
                    MxFunction => "a function handle is not carried",
                    MxObject => "an old-style object is not carried",
                    _ => $"an array of class {arrayClass} is not carried",
                });
        }
    }

    private static (int[] Dims, byte[] Words) Words(byte[] content)
    {
        (_, _, int p) = Sub(content, 8);
        (_, byte[] dimBytes, p) = Sub(content, p);
        int[] dims = new int[dimBytes.Length / 4];
        for (int i = 0; i < dims.Length; i++)
        {
            dims[i] = BinaryPrimitives.ReadInt32LittleEndian(dimBytes.AsSpan(4 * i));
        }

        (_, _, p) = Sub(content, p);
        (int type, byte[] data, _) = Sub(content, p);
        return (dims, AsClass(type, data, "uint32", dims.Aggregate(1, static (a, b) => a * b)));
    }

    private static string Text(byte[] element, int at)
    {
        (int type, byte[] data, _) = Sub(element, at);
        return type switch
        {
            MiUtf8 or MiUInt8 or MiInt8 => Encoding.UTF8.GetString(data),
            MiUInt16 or MiUtf16 => Encoding.Unicode.GetString(data),
            MiUtf32 => Encoding.UTF32.GetString(data),
            _ => throw new NotSupportedException($"char data of type {type} is not carried"),
        };
    }

    private static string ClassName(int arrayClass) => arrayClass switch
    {
        MxDouble => "double",
        MxSingle => "single",
        MxInt8 => "int8",
        MxUInt8 => "uint8",
        MxInt16 => "int16",
        MxUInt16 => "uint16",
        MxInt32 => "int32",
        MxUInt32 => "uint32",
        MxInt64 => "int64",
        _ => "uint64",
    };

    /// <summary>
    /// A data element's values in <paramref name="className"/>'s bytes: as they are when the element
    /// is the class's own type, else converted (MATLAB stores a double array of small whole numbers
    /// as bytes).
    /// </summary>
    private static byte[] AsClass(int type, byte[] data, string className, int count)
    {
        (int width, bool isFloat, bool signed) = Natural(className);
        (int typeWidth, bool typeFloat, bool typeSigned) = type switch
        {
            MiDouble => (8, true, true),
            MiSingle => (4, true, true),
            MiInt8 => (1, false, true),
            MiUInt8 => (1, false, false),
            MiInt16 => (2, false, true),
            MiUInt16 => (2, false, false),
            MiInt32 => (4, false, true),
            MiUInt32 => (4, false, false),
            MiInt64 => (8, false, true),
            MiUInt64 => (8, false, false),
            _ => throw new NotSupportedException($"numeric data of type {type} is not carried"),
        };
        if (typeWidth == width && typeFloat == isFloat && typeSigned == signed && data.Length >= count * width)
        {
            return data[..(count * width)];
        }

        var bytes = new byte[count * width];
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> v = data.AsSpan(i * typeWidth, typeWidth);
            double value = type switch
            {
                MiDouble => BinaryPrimitives.ReadDoubleLittleEndian(v),
                MiSingle => BinaryPrimitives.ReadSingleLittleEndian(v),
                MiInt8 => (sbyte)v[0],
                MiUInt8 => v[0],
                MiInt16 => BinaryPrimitives.ReadInt16LittleEndian(v),
                MiUInt16 => BinaryPrimitives.ReadUInt16LittleEndian(v),
                MiInt32 => BinaryPrimitives.ReadInt32LittleEndian(v),
                MiUInt32 => BinaryPrimitives.ReadUInt32LittleEndian(v),
                MiInt64 => BinaryPrimitives.ReadInt64LittleEndian(v),
                _ => BinaryPrimitives.ReadUInt64LittleEndian(v),
            };
            WriteAs(bytes.AsSpan(i * width, width), className, value);
        }

        return bytes;
    }
}
