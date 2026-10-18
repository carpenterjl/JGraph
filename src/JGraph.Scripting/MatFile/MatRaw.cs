using System.Buffers.Binary;
using System.Text;
using static JGraph.Scripting.MatFile.MatConstants;

namespace JGraph.Scripting.MatFile;

/// <summary>
/// Level-5 matrix elements read as the bytes they are (designer plan DS6, ADR 0245): where in an
/// element it refers to subsystem objects, its items when it is a cell, and its plain value when it
/// is one, so that a value MATLAB saved is written back unchanged but for the objects it names.
/// </summary>
/// <remarks>
/// A reference to subsystem objects is a <c>uint32</c> array anywhere inside an element whose words
/// are <c>0xDD000000</c>, the number of dimensions, the dimensions, an object id per element and the
/// class id (<see cref="MatMcos"/>). Every other part of an element is copied as it is.
/// </remarks>
internal static class MatRaw
{
    /// <summary>The first word of every reference to subsystem objects.</summary>
    public const uint ReferenceMark = 0xDD000000;

    /// <summary>One reference inside an element: where its first object id is, its ids, and where its class id is.</summary>
    public readonly record struct Found(int IdsAt, int[] Ids, int ClassAt);

    /// <summary>Every reference to subsystem objects inside the matrix element <paramref name="element"/>.</summary>
    public static List<Found> References(byte[] element)
    {
        var found = new List<Found>();
        if (element.Length >= 8)
        {
            Walk(element, 0, found);
        }

        return found;
    }

    private static int Walk(byte[] bytes, int at, List<Found> found)
    {
        int type = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
        int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at + 4));
        int end = at + 8 + length;
        if (type != MiMatrix || length == 0)
        {
            return end;
        }

        int p = at + 8;
        (int flagsType, int flagsAt, int flagsLength, p) = Sub(bytes, p);
        int arrayClass = flagsLength > 0 && flagsType == MiUInt32 ? bytes[flagsAt] : 0;
        while (p < end)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p)) == MiMatrix)
            {
                p = Walk(bytes, p, found);
                continue;
            }

            (int subType, int dataAt, int dataLength, int next) = Sub(bytes, p);
            if (arrayClass == MxUInt32 && subType == MiUInt32 && dataLength >= 16)
            {
                ReadReference(bytes, dataAt, dataLength, found);
            }

            p = next;
        }

        return end;
    }

    private static void ReadReference(byte[] bytes, int at, int length, List<Found> found)
    {
        int words = length / 4;
        uint Word(int i) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at + (4 * i)));
        if (Word(0) != ReferenceMark)
        {
            return;
        }

        long dims = Word(1);
        if (dims < 1 || 2 + dims >= words)
        {
            return;
        }

        long count = 1;
        for (int i = 0; i < dims; i++)
        {
            count *= Word(2 + i);
        }

        if (2 + dims + count + 1 != words)
        {
            return;
        }

        var ids = new int[count];
        for (int i = 0; i < count; i++)
        {
            ids[i] = (int)Word((int)(2 + dims + i));
        }

        found.Add(new Found(at + (4 * (int)(2 + dims)), ids, at + (4 * (words - 1))));
    }

    /// <summary>A data element at <paramref name="at"/>: its type, where its data is, the data's length, and where the next element starts.</summary>
    private static (int Type, int DataAt, int Length, int Next) Sub(byte[] bytes, int at)
    {
        uint tag = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
        if (tag >> 16 != 0)
        {
            return ((int)(tag & 0xFFFF), at + 4, (int)(tag >> 16), at + 8);
        }

        int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at + 4));
        return ((int)tag, at + 8, length, at + 8 + ((length + 7) / 8 * 8));
    }

    /// <summary>The parts of a matrix element: its class, dimensions, name, and the offsets of what follows the name.</summary>
    private static (int Class, int[] Size, string Name, int Start, int End)? Head(byte[] element)
    {
        if (element.Length < 8 || BinaryPrimitives.ReadUInt32LittleEndian(element) != MiMatrix)
        {
            return null;
        }

        int end = 8 + (int)BinaryPrimitives.ReadUInt32LittleEndian(element.AsSpan(4));
        if (end == 8)
        {
            return null;
        }

        (_, int flagsAt, _, int p) = Sub(element, 8);
        int arrayClass = element[flagsAt];
        if (arrayClass == MxOpaque)
        {
            return (arrayClass, [], string.Empty, p, end);
        }

        (_, int dimsAt, int dimsLength, p) = Sub(element, p);
        int[] size = new int[dimsLength / 4];
        for (int i = 0; i < size.Length; i++)
        {
            size[i] = BinaryPrimitives.ReadInt32LittleEndian(element.AsSpan(dimsAt + (4 * i)));
        }

        (_, int nameAt, int nameLength, p) = Sub(element, p);
        return (arrayClass, size, Encoding.ASCII.GetString(element, nameAt, nameLength), p, end);
    }

    /// <summary>Whether <paramref name="element"/> is a matrix element with nothing in it (an empty value MATLAB writes as a bare tag).</summary>
    public static bool IsBare(byte[] element) =>
        element.Length == 8 && BinaryPrimitives.ReadUInt32LittleEndian(element.AsSpan(4)) == 0;

    /// <summary>The variable name a top-level matrix element carries; empty for a bare one.</summary>
    public static string NameOf(byte[] element) =>
        Opaque(element) is { } opaque ? opaque.Name : Head(element)?.Name ?? string.Empty;

    /// <summary>An opaque element's name, class and content element; null when it is not one.</summary>
    public static (string Name, string ClassName, byte[] Content)? Opaque(byte[] element)
    {
        if (Head(element) is not { Class: MxOpaque } head)
        {
            return null;
        }

        (_, int nameAt, int nameLength, int p) = Sub(element, head.Start);
        (_, _, _, p) = Sub(element, p);
        (_, int classAt, int classLength, p) = Sub(element, p);
        if (p + 8 > head.End)
        {
            return null;
        }

        int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(element.AsSpan(p + 4));
        return (Encoding.ASCII.GetString(element, nameAt, nameLength), Encoding.ASCII.GetString(element, classAt, classLength),
            element[p..(p + 8 + length)]);
    }

    /// <summary>The values of an integer element, in column order; null when it is not one.</summary>
    public static double[]? Integers(byte[] element)
    {
        if (Head(element) is not { Class: >= MxInt8 and <= MxUInt64 } head)
        {
            return null;
        }

        (int type, int at, int length, _) = Sub(element, head.Start);
        int width = type switch { MiInt8 or MiUInt8 => 1, MiInt16 or MiUInt16 => 2, MiInt32 or MiUInt32 => 4, MiInt64 or MiUInt64 => 8, _ => 0 };
        if (width == 0)
        {
            return null;
        }

        var values = new double[length / width];
        for (int i = 0; i < values.Length; i++)
        {
            ReadOnlySpan<byte> v = element.AsSpan(at + (i * width), width);
            values[i] = type switch
            {
                MiInt8 => (sbyte)v[0],
                MiUInt8 => v[0],
                MiInt16 => BinaryPrimitives.ReadInt16LittleEndian(v),
                MiUInt16 => BinaryPrimitives.ReadUInt16LittleEndian(v),
                MiInt32 => BinaryPrimitives.ReadInt32LittleEndian(v),
                MiUInt32 => BinaryPrimitives.ReadUInt32LittleEndian(v),
                MiInt64 => BinaryPrimitives.ReadInt64LittleEndian(v),
                _ => BinaryPrimitives.ReadUInt64LittleEndian(v),
            };
        }

        return values;
    }

    /// <summary>The item elements of a cell element, in column order; null when it is not a cell.</summary>
    public static List<byte[]>? CellItems(byte[] element)
    {
        if (Head(element) is not { Class: MxCell } head)
        {
            return null;
        }

        var items = new List<byte[]>();
        int p = head.Start;
        while (p < head.End)
        {
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(element.AsSpan(p + 4));
            items.Add(element[p..(p + 8 + length)]);
            p += 8 + length;
        }

        return items;
    }

    /// <summary>The struct element's field names and each field's element (a 1-by-1 struct); null when it is not one.</summary>
    public static List<(string Name, byte[] Element)>? StructFields(byte[] element)
    {
        if (Head(element) is not { Class: MxStruct } head || head.Size.Aggregate(1, static (a, b) => a * b) != 1)
        {
            return null;
        }

        (_, int widthAt, _, int p) = Sub(element, head.Start);
        int width = BinaryPrimitives.ReadInt32LittleEndian(element.AsSpan(widthAt));
        (_, int namesAt, int namesLength, p) = Sub(element, p);
        var fields = new List<(string, byte[])>();
        for (int i = 0; width > 0 && i < namesLength / width; i++)
        {
            string name = Encoding.ASCII.GetString(element, namesAt + (i * width), width).TrimEnd('\0');
            int cut = name.IndexOf('\0', StringComparison.Ordinal);
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(element.AsSpan(p + 4));
            fields.Add((cut >= 0 ? name[..cut] : name, element[p..(p + 8 + length)]));
            p += 8 + length;
        }

        return fields;
    }

    /// <summary>The bytes of a <c>uint8</c> element's data; null when it is not one.</summary>
    public static byte[]? Uint8Data(byte[] element)
    {
        if (Head(element) is not { Class: MxUInt8 } head)
        {
            return null;
        }

        (int type, int at, int length, _) = Sub(element, head.Start);
        return type == MiUInt8 ? element[at..(at + length)] : null;
    }

    /// <summary>The text of a char element; null when it is not one this reads (UTF-8 or UTF-16 data).</summary>
    public static string? Text(byte[] element)
    {
        if (IsBare(element))
        {
            return null;
        }

        if (Head(element) is not { Class: MxChar } head)
        {
            return null;
        }

        if (head.Start >= head.End)
        {
            return string.Empty;
        }

        (int type, int at, int length, _) = Sub(element, head.Start);
        string text = type switch
        {
            MiUtf8 or MiUInt8 or MiInt8 => Encoding.UTF8.GetString(element, at, length),
            MiUInt16 or MiUtf16 => Encoding.Unicode.GetString(element, at, length),
            _ => null!,
        };
        return text is null || (head.Size.Length == 2 && head.Size[0] > 1) ? null : text;
    }

    /// <summary>
    /// A plain value as <see cref="MatData"/> - a char row, a cell, a 1-by-1 struct - each part this
    /// does not read kept as its element; null when the element itself is not one of those.
    /// </summary>
    public static MatData? Plain(byte[] element, Func<byte[], MatData> keep)
    {
        if (Text(element) is { } text)
        {
            return MatData.Text(text);
        }

        if (Head(element) is not { } head)
        {
            return null;
        }

        if (head.Class == MxCell && CellItems(element) is { } items)
        {
            return new MatData.Cells(head.Size, items.Select(i => Plain(i, keep) ?? keep(i)).ToArray());
        }

        if (head.Class == MxStruct && StructFields(element) is { } fields)
        {
            return new MatData.Structs(head.Size, fields.Select(static f => f.Name).ToArray(),
                [fields.Select(f => Plain(f.Element, keep) ?? keep(f.Element)).ToArray()]);
        }

        return null;
    }
}
