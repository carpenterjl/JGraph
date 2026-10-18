using System.Buffers.Binary;
using System.IO;
using System.Text;
using static JGraph.Scripting.MatFile.MatConstants;

namespace JGraph.Scripting.MatFile;

/// <summary>
/// A plain value as a level-5 matrix element holds it, for <see cref="MatMcosWriter"/>: a number
/// array of one class, a char array, a cell array or a struct array, each with its size.
/// </summary>
internal abstract record MatData(int[] Size)
{
    /// <summary>A numeric or logical array; <paramref name="ClassName"/> is MATLAB's (<c>double</c>, <c>logical</c>, <c>uint32</c>, ...), the data in column order.</summary>
    public sealed record Numbers(string ClassName, int[] Size, double[] Data) : MatData(Size);

    /// <summary>A char array; the characters in column order.</summary>
    public sealed record Chars(int[] Size, string Characters) : MatData(Size);

    /// <summary>A cell array; the items in column order.</summary>
    public sealed record Cells(int[] Size, MatData[] Items) : MatData(Size);

    /// <summary>A struct array: its field names in order, and each element's values in that order.</summary>
    public sealed record Structs(int[] Size, string[] Fields, MatData[][] Elements) : MatData(Size);

    /// <summary>A 1-by-1 double.</summary>
    public static Numbers Scalar(double value) => new("double", [1, 1], [value]);

    /// <summary>A double row.</summary>
    public static Numbers Row(params double[] values) => new("double", [1, values.Length], values);

    /// <summary>A char row; the empty text is 0-by-0, as MATLAB's <c>''</c> is.</summary>
    public static Chars Text(string text) => new(text.Length == 0 ? [0, 0] : [1, text.Length], text);

    /// <summary>A 1-by-1 struct of the given fields, in order.</summary>
    public static Structs Struct(params (string Name, MatData Value)[] fields) =>
        new([1, 1], fields.Select(static f => f.Name).ToArray(), [fields.Select(static f => f.Value).ToArray()]);
}

/// <summary>What one stored name of a subsystem object holds.</summary>
internal abstract record McosValue
{
    /// <summary>A name from the file's table, which MATLAB stores for an enumerated word such as <c>'manual'</c>.</summary>
    public sealed record Word(string Text) : McosValue;

    /// <summary>A number stored in the property list itself.</summary>
    public sealed record Literal(int Number) : McosValue;

    /// <summary>A stored value.</summary>
    public sealed record Value(MatData Data) : McosValue;

    /// <summary>References to other objects, of one class; none is an empty reference of <paramref name="EmptyClass"/>.</summary>
    public sealed record Refs(IReadOnlyList<McosObject> Objects, string? EmptyClass = null) : McosValue;
}

/// <summary>
/// A MATLAB class instance as a MAT-file's subsystem saves it: its class and the names it stores,
/// in the order <c>load</c> assigns them, and its dynamic properties.
/// </summary>
internal sealed class McosObject(string className)
{
    private readonly List<(string Name, McosValue Value)> _entries = [];

    public string ClassName { get; } = className;

    /// <summary>The stored names in order.</summary>
    public IReadOnlyList<(string Name, McosValue Value)> Entries => _entries;

    /// <summary>The instance's dynamic properties, as <c>meta.DynamicProperty</c> objects.</summary>
    public List<McosObject> Dynamic { get; } = [];

    /// <summary>Stores <paramref name="name"/>: in the place it already has, or last. <c>load</c> assigns a name once, so a name is never stored twice.</summary>
    public McosObject Set(string name, McosValue value)
    {
        int at = _entries.FindIndex(e => e.Name == name);
        if (at >= 0)
        {
            _entries[at] = (name, value);
        }
        else
        {
            _entries.Add((name, value));
        }

        return this;
    }

    /// <summary>What <paramref name="name"/> holds, or null when it is not stored.</summary>
    public McosValue? Get(string name) => _entries.Find(e => e.Name == name).Value;

    /// <summary>Whether <paramref name="name"/> is stored.</summary>
    public bool Has(string name) => _entries.Exists(e => e.Name == name);

    /// <summary>Removes <paramref name="name"/> when stored.</summary>
    public void Remove(string name) => _entries.RemoveAll(e => e.Name == name);
}

/// <summary>
/// Writes a level-5 MAT-file whose one variable is a struct holding a MATLAB class instance - the
/// shape of App Designer's <c>components</c> - with the instance and every object it refers to in
/// MATLAB's subsystem (designer plan DS1b, ADR 0244).
/// </summary>
/// <remarks>
/// <para>
/// The layout is the one <see cref="MatMcos"/> reads, measured further in DS1b against R2025b's own
/// files (rewritten from the parsed table they are byte for byte the same): the header's offset to
/// a <c>uint8</c> variable whose bytes are a small MAT stream - a struct whose field <c>MCOS</c>
/// holds the opaque <c>FileWrapper__</c> cell, then a second stream whose <c>MCOS</c> is empty.
/// The cell is the byte table, an empty element, the stored values, and three cells with one entry
/// per class. The table holds the names, the classes (package and name), the objects (a class, a
/// property list, and a dependency id that indexes the dynamic properties), the property lists
/// (a name, a kind - 0 a word from the names, 1 a stored value, 2 a literal - and the datum), the
/// dynamic-property lists, a list per object of names its serialization hook renamed, and a last
/// segment of <c>1</c> and its own offset plus eight.
/// </para>
/// <para>
/// What this leaves out, and R2025b still loads the same: every class's saved-time defaults (the
/// three per-class cells are written empty), and so the enumeration objects those defaults refer
/// to; the renamed-names lists; and any object not reachable from the root. Without the defaults,
/// <c>load</c> assigns exactly the names stored, each through the class's own set methods and
/// serialization hooks, in the order stored; a name the file does not store keeps the class's own
/// default. Strings are not written (MATLAB saves them through <c>saveobj</c>); a char takes their
/// place.
/// </para>
/// </remarks>
internal static class MatMcosWriter
{
    /// <summary>The first word of every reference to subsystem objects.</summary>
    private const uint ReferenceMark = 0xDD000000;

    /// <summary>
    /// The MAT-file whose variable <paramref name="variable"/> is a 1-by-1 struct with the field
    /// <paramref name="field"/> holding <paramref name="root"/>.
    /// </summary>
    public static byte[] Write(McosObject root, string variable, string field)
    {
        ArgumentNullException.ThrowIfNull(root);
        var table = new Table();
        byte[] subsystem = table.Subsystem(root);
        byte[] figure = Opaque(string.Empty, root.ClassName,
            Uint32Column([ReferenceMark, 2, 1, 1, 1, (uint)table.ClassId(root.ClassName)]));
        byte[] body = StructElement(variable, [field], [[figure]], [1, 1]);

        using var stream = new MemoryStream();
        var header = new byte[116];
        Array.Fill(header, (byte)' ');
        byte[] text = Encoding.ASCII.GetBytes("MATLAB 5.0 MAT-file, Platform: PCWIN64, Created by JGraph's designer");
        Array.Copy(text, header, text.Length);
        stream.Write(header);
        Span<byte> tail = stackalloc byte[12];
        BinaryPrimitives.WriteInt64LittleEndian(tail, 128 + body.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(tail[8..], 0x0100);
        tail[10] = (byte)'I';
        tail[11] = (byte)'M';
        stream.Write(tail);
        stream.Write(body);
        stream.Write(Uint8Row(subsystem));
        return stream.ToArray();
    }

    /// <summary>The names, classes, objects and stored values of one file, gathered as the root is walked.</summary>
    private sealed class Table
    {
        private readonly List<string> _names = [string.Empty];
        private readonly Dictionary<string, int> _nameIds = new(StringComparer.Ordinal) { [string.Empty] = 0 };
        private readonly List<(int Package, int Name)> _classes = [(0, 0)];
        private readonly Dictionary<string, int> _classIds = new(StringComparer.Ordinal);
        private readonly List<byte[]> _cells = [];

        public int ClassId(string className)
        {
            if (!_classIds.TryGetValue(className, out int id))
            {
                int dot = className.LastIndexOf('.');
                _classes.Add((dot > 0 ? Name(className[..dot]) : 0, Name(className[(dot + 1)..])));
                id = _classIds[className] = _classes.Count - 1;
            }

            return id;
        }

        private int Name(string text)
        {
            if (!_nameIds.TryGetValue(text, out int id))
            {
                _names.Add(text);
                id = _nameIds[text] = _names.Count - 1;
            }

            return id;
        }

        /// <summary>The subsystem's bytes: the <c>FileWrapper__</c> stream for every object reachable from <paramref name="root"/>, the root first.</summary>
        public byte[] Subsystem(McosObject root)
        {
            var order = new List<McosObject>();
            var ids = new Dictionary<McosObject, int>(ReferenceEqualityComparer.Instance);
            Visit(root);

            void Visit(McosObject o)
            {
                if (ids.ContainsKey(o))
                {
                    return;
                }

                ids[o] = order.Count + 1;
                order.Add(o);
                foreach ((_, McosValue value) in o.Entries)
                {
                    if (value is McosValue.Refs refs)
                    {
                        foreach (McosObject other in refs.Objects)
                        {
                            Visit(other);
                        }
                    }
                }

                foreach (McosObject d in o.Dynamic)
                {
                    Visit(d);
                }
            }

            foreach (McosObject o in order)
            {
                ClassId(o.ClassName);
            }

            var lists = new List<(int Name, int Kind, uint Datum)[]> { Array.Empty<(int, int, uint)>() };
            foreach (McosObject o in order)
            {
                var list = new List<(int, int, uint)>(o.Entries.Count);
                foreach ((string name, McosValue value) in o.Entries)
                {
                    list.Add(value switch
                    {
                        McosValue.Word w => (Name(name), 0, (uint)Name(w.Text)),
                        McosValue.Literal l => (Name(name), 2, (uint)l.Number),
                        McosValue.Value v => (Name(name), 1, Store(Element(v.Data))),
                        McosValue.Refs r => (Name(name), 1, Store(Reference(r, ids))),
                        _ => throw new InvalidOperationException("Unknown stored value."),
                    });
                }

                lists.Add([.. list]);
            }

            // The dependency id is the object's own id; it indexes the dynamic-property lists.
            var dynamic = new List<int[]> { Array.Empty<int>() };
            dynamic.AddRange(order.Select(o => o.Dynamic.Select(d => ids[d]).ToArray()));
            byte[] table = TableBytes(order, lists, dynamic);

            int classes = _classes.Count;
            byte[] emptyStruct = StructElement(string.Empty, [], [], [1, 0]);
            var cells = new List<byte[]> { Uint8Column(table), EmptyElement() };
            cells.AddRange(_cells);
            cells.Add(CellElement(Enumerable.Repeat(emptyStruct, classes).ToArray(), [classes, 1]));
            cells.Add(NumericElement(new MatData.Numbers("int32", [classes, 1], new double[classes])));
            cells.Add(CellElement(Enumerable.Repeat(emptyStruct, classes).ToArray(), [classes, 1]));

            byte[] wrapper = Opaque(string.Empty, "FileWrapper__", CellElement([.. cells], [cells.Count, 1]));
            byte[] holder = StructElement(string.Empty, ["MCOS"], [[wrapper]], [1, 1]);
            byte[] trailer = Uint8Row([.. StreamHead, .. StructElement(string.Empty, ["MCOS"], [[EmptyElement()]], [1, 1])]);
            return [.. StreamHead, .. holder, .. trailer];
        }

        private uint Store(byte[] element)
        {
            _cells.Add(element);
            return (uint)(_cells.Count - 1);
        }

        private byte[] Reference(McosValue.Refs refs, Dictionary<McosObject, int> ids)
        {
            string className = refs.Objects.Count > 0 ? refs.Objects[0].ClassName
                : refs.EmptyClass ?? throw new InvalidOperationException("An empty reference needs its class.");
            var words = new List<uint> { ReferenceMark, 2 };
            words.AddRange(refs.Objects.Count > 0 ? [1, (uint)refs.Objects.Count] : [0, 0]);
            words.AddRange(refs.Objects.Select(o => (uint)ids[o]));
            words.Add((uint)ClassId(className));
            return Uint32Column([.. words]);
        }

        private byte[] TableBytes(List<McosObject> order, List<(int Name, int Kind, uint Datum)[]> lists, List<int[]> dynamic)
        {
            using var names = new MemoryStream();
            foreach (string n in _names.Skip(1))
            {
                names.Write(Encoding.UTF8.GetBytes(n));
                names.WriteByte(0);
            }

            while ((40 + names.Length) % 8 != 0)
            {
                names.WriteByte(0);
            }

            var segments = new List<byte[]>
            {
                Words(_classes.SelectMany(static c => new uint[] { (uint)c.Package, (uint)c.Name, 0, 0 })),
                Array.Empty<byte>(),
                Words(new uint[6].Concat(order.SelectMany((o, i) => new uint[] { (uint)ClassId(o.ClassName), 0, 0, 0, (uint)(i + 1), (uint)(i + 1) }))),
                PropertyLists(lists),
                IdLists(dynamic),
                Array.Empty<byte>(),
                Words([0, 0]),
            };

            var offsets = new uint[8];
            uint at = (uint)(40 + names.Length);
            for (int i = 0; i < segments.Count; i++)
            {
                offsets[i] = at;
                at += (uint)segments[i].Length;
            }

            offsets[7] = at;
            using var table = new MemoryStream();
            table.Write(Words([4, (uint)(_names.Count - 1), .. offsets]));
            names.Position = 0;
            names.CopyTo(table);
            foreach (byte[] segment in segments)
            {
                table.Write(segment);
            }

            table.Write(Words([1, offsets[7] + 8, 0, 0]));
            return table.ToArray();
        }
    }

    private static readonly byte[] StreamHead = [0x00, 0x01, (byte)'I', (byte)'M', 0, 0, 0, 0];

    private static byte[] PropertyLists(List<(int Name, int Kind, uint Datum)[]> lists)
    {
        var words = new List<uint>();
        foreach ((int Name, int Kind, uint Datum)[] list in lists)
        {
            words.Add((uint)list.Length);
            foreach ((int name, int kind, uint datum) in list)
            {
                words.AddRange([(uint)name, (uint)kind, datum]);
            }

            if (words.Count % 2 != 0)
            {
                words.Add(0);
            }
        }

        return Words(words);
    }

    private static byte[] IdLists(List<int[]> lists)
    {
        var words = new List<uint>();
        foreach (int[] ids in lists)
        {
            words.Add((uint)ids.Length);
            words.AddRange(ids.Select(static i => (uint)i));
            if (words.Count % 2 != 0)
            {
                words.Add(0);
            }
        }

        return Words(words);
    }

    private static byte[] Words(IEnumerable<uint> words)
    {
        uint[] all = words.ToArray();
        var bytes = new byte[all.Length * 4];
        for (int i = 0; i < all.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4 * i), all[i]);
        }

        return bytes;
    }

    // --- Elements --------------------------------------------------------------------------------

    /// <summary>A stored value's matrix element, with no name.</summary>
    public static byte[] Element(MatData data) => data switch
    {
        MatData.Numbers n => NumericElement(n),
        MatData.Chars c => CharElement(c),
        MatData.Cells c => CellElement(c.Items.Select(Element).ToArray(), c.Size),
        MatData.Structs s => StructElement(string.Empty, s.Fields, s.Elements.Select(e => e.Select(Element).ToArray()).ToArray(), s.Size),
        _ => throw new InvalidOperationException("Unknown value."),
    };

    private static byte[] NumericElement(MatData.Numbers n)
    {
        (int arrayClass, int dataType, int width, int flagBits) = n.ClassName switch
        {
            "double" => (MxDouble, MiDouble, 8, 0),
            "single" => (MxSingle, MiSingle, 4, 0),
            "logical" => (MxUInt8, MiUInt8, 1, FlagLogical),
            "int8" => (MxInt8, MiInt8, 1, 0),
            "uint8" => (MxUInt8, MiUInt8, 1, 0),
            "int16" => (MxInt16, MiInt16, 2, 0),
            "uint16" => (MxUInt16, MiUInt16, 2, 0),
            "int32" => (MxInt32, MiInt32, 4, 0),
            "uint32" => (MxUInt32, MiUInt32, 4, 0),
            "int64" => (MxInt64, MiInt64, 8, 0),
            "uint64" => (MxUInt64, MiUInt64, 8, 0),
            _ => throw new NotSupportedException($"A value of class {n.ClassName} cannot be stored."),
        };
        var data = new byte[n.Data.Length * width];
        for (int i = 0; i < n.Data.Length; i++)
        {
            Span<byte> at = data.AsSpan(i * width, width);
            double v = n.Data[i];
            switch (dataType)
            {
                case MiDouble: BinaryPrimitives.WriteDoubleLittleEndian(at, v); break;
                case MiSingle: BinaryPrimitives.WriteSingleLittleEndian(at, (float)v); break;
                case MiInt8: at[0] = (byte)(sbyte)v; break;
                case MiUInt8: at[0] = (byte)v; break;
                case MiInt16: BinaryPrimitives.WriteInt16LittleEndian(at, (short)v); break;
                case MiUInt16: BinaryPrimitives.WriteUInt16LittleEndian(at, (ushort)v); break;
                case MiInt32: BinaryPrimitives.WriteInt32LittleEndian(at, (int)v); break;
                case MiUInt32: BinaryPrimitives.WriteUInt32LittleEndian(at, (uint)v); break;
                case MiInt64: BinaryPrimitives.WriteInt64LittleEndian(at, (long)v); break;
                default: BinaryPrimitives.WriteUInt64LittleEndian(at, (ulong)v); break;
            }
        }

        return Matrix(Flags(arrayClass | flagBits), Dimensions(n.Size), Sub(MiInt8, []), Sub(dataType, data));
    }

    private static byte[] CharElement(MatData.Chars c) =>
        Matrix(Flags(MxChar), Dimensions(c.Size), Sub(MiInt8, []), Sub(MiUtf8, Encoding.UTF8.GetBytes(c.Characters)));

    private static byte[] CellElement(byte[][] items, int[] size) =>
        Matrix([Flags(MxCell), Dimensions(size), Sub(MiInt8, []), .. items]);

    private static byte[] StructElement(string name, string[] fields, byte[][][] elements, int[] size)
    {
        int width = fields.Length == 0 ? 1 : fields.Max(static f => Encoding.ASCII.GetByteCount(f)) + 1;
        var packed = new byte[width * fields.Length];
        for (int i = 0; i < fields.Length; i++)
        {
            Encoding.ASCII.GetBytes(fields[i]).CopyTo(packed, i * width);
        }

        var widthBytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(widthBytes, width);
        return Matrix([Flags(MxStruct), Dimensions(size), Sub(MiInt8, Encoding.ASCII.GetBytes(name)),
            Sub(MiInt32, widthBytes), Sub(MiInt8, packed), .. elements.SelectMany(static e => e)]);
    }

    private static byte[] Opaque(string name, string className, byte[] content) =>
        Matrix(Flags(MxOpaque), Sub(MiInt8, Encoding.ASCII.GetBytes(name)), Sub(MiInt8, "MCOS"u8.ToArray()),
            Sub(MiInt8, Encoding.ASCII.GetBytes(className)), content);

    private static byte[] Uint32Column(uint[] words) =>
        Matrix(Flags(MxUInt32), Dimensions([words.Length, 1]), Sub(MiInt8, []), Sub(MiUInt32, Words(words)));

    private static byte[] Uint8Column(byte[] data) =>
        Matrix(Flags(MxUInt8), Dimensions([data.Length, 1]), Sub(MiInt8, []), Sub(MiUInt8, data));

    private static byte[] Uint8Row(byte[] data) =>
        Matrix(Flags(MxUInt8), Dimensions([1, data.Length]), Sub(MiInt8, []), Sub(MiUInt8, data));

    /// <summary>A matrix element with nothing in it, which MATLAB writes for the subsystem cell's second entry.</summary>
    private static byte[] EmptyElement() => Words([MiMatrix, 0]);

    private static byte[] Flags(int flags) => Sub(MiUInt32, Words([(uint)flags, 0]));

    private static byte[] Dimensions(int[] size) => Sub(MiInt32, Words(size.Select(static d => (uint)d)));

    private static byte[] Matrix(params byte[][] parts)
    {
        int length = parts.Sum(static p => p.Length);
        var element = new byte[8 + length];
        BinaryPrimitives.WriteInt32LittleEndian(element, MiMatrix);
        BinaryPrimitives.WriteInt32LittleEndian(element.AsSpan(4), length);
        int at = 8;
        foreach (byte[] part in parts)
        {
            part.CopyTo(element, at);
            at += part.Length;
        }

        return element;
    }

    /// <summary>A data element: four bytes or fewer packed into its tag, as MATLAB packs them; longer data padded to eight.</summary>
    private static byte[] Sub(int type, byte[] data)
    {
        if (data.Length is > 0 and <= 4)
        {
            var small = new byte[8];
            BinaryPrimitives.WriteUInt16LittleEndian(small, (ushort)type);
            BinaryPrimitives.WriteUInt16LittleEndian(small.AsSpan(2), (ushort)data.Length);
            data.CopyTo(small, 4);
            return small;
        }

        var element = new byte[8 + ((data.Length + 7) / 8 * 8)];
        BinaryPrimitives.WriteInt32LittleEndian(element, type);
        BinaryPrimitives.WriteInt32LittleEndian(element.AsSpan(4), data.Length);
        data.CopyTo(element, 8);
        return element;
    }
}
