using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using System.Text;
using JGraph.Scripting.Jgs;

namespace JGraph.Scripting.MatFile;

/// <summary>
/// Writes version 7.3 MAT-files (open item 131): the HDF5 layout R2025b writes, measured from its
/// own files (probe_h5, probe_h5b). A 512-byte userblock carries the MAT-file description and the
/// version word 0x0200; the HDF5 data starts there with a version 0 superblock, version 1 object
/// headers and old-style groups (a symbol-table B-tree over symbol table nodes, the names in a local
/// heap). Every variable is a root-level dataset or group with a <c>MATLAB_class</c> attribute:
/// numbers, logicals (uint8, <c>MATLAB_int_decode</c> 1) and chars (uint16, 2) with their dimensions
/// reversed, complex numbers a compound of <c>real</c> and <c>imag</c>, an empty value a uint64 list
/// of its dimensions marked <c>MATLAB_empty</c>, a cell a dataset of object references into the
/// <c>#refs#</c> group, a struct a group of its fields named by a <c>MATLAB_fields</c> attribute
/// (variable-length char sequences in a global heap), a struct array a group of reference datasets,
/// and a sparse matrix a group of <c>jc</c>, <c>ir</c> and <c>data</c> under <c>MATLAB_sparse</c>.
/// </summary>
/// <remarks>
/// ADR 0065 kept JGraph to version 5 for fear of files MATLAB silently mis-reads; ADR 0235 lifts that
/// for the classes above, each one loaded back by R2025b itself and compared. The classes R2025b
/// keeps in its object subsystem — strings, datetimes, durations, categoricals, tables, objects,
/// function handles — are refused by name, as version 5 refused strings before.
/// </remarks>
internal static class MatV73Writer
{
    private const long Undefined = -1;
    private const int UserBlock = 512;
    private const int SuperblockSize = 96;
    private const int LeafK = 4;
    private const int InternalK = 16;
    private const int TreeNodeSize = 24 + ((2 * InternalK) + 1) * 8 + (2 * InternalK * 8);
    private const int SymbolNodeSize = 8 + (2 * LeafK * 40);

    private const int MessageDataspace = 0x0001;
    private const int MessageDatatype = 0x0003;
    private const int MessageFillValue = 0x0005;
    private const int MessageLayout = 0x0008;
    private const int MessageAttribute = 0x000C;
    private const int MessageSymbolTable = 0x0011;

    /// <summary>Why <paramref name="value"/> cannot go into a version 7.3 file, or null when it can.</summary>
    public static string? WhyNotWritable(JgsValue value)
    {
        if (value.Type == JgsType.External)
        {
            return $"a {value.AsExternal.ClassName}, a .NET value, which a MAT-file cannot hold";
        }

        string? kind = value.IsStringArray ? "a string array"
            : value.IsDatetime ? "a datetime"
            : value.IsDuration ? "a duration"
            : JgsBuiltins.IsCalendarDuration(value) ? "a calendarDuration"
            : value.IsCategorical ? "a categorical"
            : value.Type == JgsType.Table ? (value.AsTable.RowTimes is null ? "a table" : "a timetable")
            : value.Type == JgsType.Object ? $"an object of class {value.AsObject.Class.Name}"
            : value.Type == JgsType.Function ? "a function handle"
            : value.ClassName is string className ? $"a {className}"
            : null;
        if (kind is not null)
        {
            return $"{kind}, which this build writes to version 7 MAT-files but not to version 7.3 — save it without -v7.3";
        }

        switch (value.Type)
        {
            case JgsType.Number or JgsType.Bool or JgsType.Complex or JgsType.String or JgsType.Sparse:
                return null;
            case JgsType.Array:
                return value.ArrayLength == 0 || value.IsCharMatrix || Enumerable.Range(0, value.ArrayLength)
                    .All(i => value.ElementAt(i).Type is JgsType.Number or JgsType.Bool or JgsType.Complex)
                    ? null
                    : "an array of mixed kinds, which has no MAT-file shape";
            case JgsType.Cell:
                return value.AsCell.Select(WhyNotWritable).FirstOrDefault(static why => why is not null);
            case JgsType.Struct:
                return value.AsStructArray.Elements
                    .SelectMany(static element => element.Values)
                    .Select(WhyNotWritable)
                    .FirstOrDefault(static why => why is not null);
            default:
                return $"a {value.TypeName}, which cannot be written to a MAT-file";
        }
    }

    /// <summary>Whether the file at <paramref name="path"/> exists and is a version 7.3 (HDF5) MAT-file.</summary>
    public static bool IsVersion73(string path) => File.Exists(path) && Hdf5.Hdf5File.Looks(File.ReadAllBytes(path));

    /// <summary>Writes <paramref name="variables"/> to <paramref name="path"/> as a version 7.3 MAT-file.</summary>
    public static void Write(string path, IEnumerable<(string Name, JgsValue Value)> variables) =>
        File.WriteAllBytes(path, Build(variables));

    /// <summary>
    /// Writes <paramref name="variables"/> into an existing version 7.3 file, keeping the names it does
    /// not mention: a read, a merge and a rewrite, as the version 5 append is.
    /// </summary>
    public static void Append(string path, IEnumerable<(string Name, JgsValue Value)> variables)
    {
        var merged = new List<(string Name, JgsValue Value)>();
        if (File.Exists(path))
        {
            merged.AddRange(MatFileReader.Read(path));
        }

        foreach ((string name, JgsValue value) in variables)
        {
            merged.RemoveAll(existing => string.Equals(existing.Name, name, StringComparison.Ordinal));
            merged.Add((name, value));
        }

        Write(path, merged);
    }

    /// <summary>The whole file's bytes.</summary>
    internal static byte[] Build(IEnumerable<(string Name, JgsValue Value)> variables)
    {
        var file = new Builder();
        var root = new List<(string Name, long Header)>();
        foreach ((string name, JgsValue value) in variables)
        {
            root.Add((name, file.Value(value)));
        }

        if (file.Refs.Count > 0)
        {
            root.Add(("#refs#", file.Group(file.Refs, [], out _, out _)));
        }

        long rootHeader = file.Group(root, [], out long tree, out long heap);
        return file.Finish(rootHeader, tree, heap);
    }

    /// <summary>One header message: its type, its bytes, and where in them the global heap's address goes.</summary>
    private sealed record Message(int Type, byte[] Data, int[] HeapPatches);

    /// <summary>The file as it grows: addresses are relative to the superblock, as HDF5 counts them.</summary>
    private sealed class Builder
    {
        private readonly MemoryStream _bytes = new();
        private readonly List<byte[]> _heapObjects = [];
        private readonly List<long> _heapPatches = [];
        private int _refCount;

        public Builder()
        {
            _bytes.Write(new byte[UserBlock + SuperblockSize]);
        }

        /// <summary>The objects a cell or a struct array points at, as the #refs# group lists them.</summary>
        public List<(string Name, long Header)> Refs { get; } = [];

        /// <summary>Appends <paramref name="data"/> on an 8-byte boundary and answers its address.</summary>
        private long Put(byte[] data)
        {
            Align();
            long at = _bytes.Position - UserBlock;
            _bytes.Write(data);
            return at;
        }

        private void Align()
        {
            while (_bytes.Position % 8 != 0)
            {
                _bytes.WriteByte(0);
            }
        }

        // --- Values ---------------------------------------------------------------------------

        /// <summary>Writes one value and answers the address of its object header.</summary>
        public long Value(JgsValue value)
        {
            if (value.Type == JgsType.Sparse)
            {
                return Sparse(value.AsSparse);
            }

            if (value.Type == JgsType.Cell)
            {
                return value.AsCell.Length == 0 ? Empty("cell", value.Dims, null) : Cell(value);
            }

            if (value.Type == JgsType.Struct)
            {
                return Struct(value);
            }

            if (value.Type == JgsType.String)
            {
                string text = value.AsString;
                return text.Length == 0 ? Empty("char", [0, 0], null) : Chars([text], 1, text.Length);
            }

            if (value.IsCharMatrix)
            {
                string[] rows = value.CharMatrixRows();
                int columns = rows.Length == 0 ? 0 : rows[0].Length;
                return rows.Length == 0 || columns == 0 ? Empty("char", value.Dims, null) : Chars(rows, rows.Length, columns);
            }

            (JgsValue[] elements, int[] dims) = Elements(value);
            if (elements.Length == 0)
            {
                bool logical = value.IsPacked && value.PackedKind == JgsPackedKind.Bool;
                return Empty(logical ? "logical" : ClassName(value.NumericClass), dims, null);
            }

            if (Array.TrueForAll(elements, static e => e.Type == JgsType.Bool))
            {
                byte[] bits = [.. elements.Select(static e => (byte)(e.AsNumber != 0 ? 1 : 0))];
                return Dataset(Fixed(1, signed: false), Reversed(dims), bits,
                    [ClassAttribute("logical"), IntDecode(1)]);
            }

            if (Array.Exists(elements, static e => e.Type == JgsType.Complex))
            {
                var data = new byte[elements.Length * 16];
                for (int i = 0; i < elements.Length; i++)
                {
                    Complex c = elements[i].Type == JgsType.Complex ? elements[i].AsComplex : new Complex(elements[i].AsNumber, 0);
                    BinaryPrimitives.WriteDoubleLittleEndian(data.AsSpan(i * 16), c.Real);
                    BinaryPrimitives.WriteDoubleLittleEndian(data.AsSpan((i * 16) + 8), c.Imaginary);
                }

                return Dataset(Compound(Float(8), 8), Reversed(dims), data, [ClassAttribute("double")]);
            }

            JgsNumericClass numericClass = value.NumericClass;
            (byte[] type, int width) = NumericType(numericClass);
            var bytes = new byte[elements.Length * width];
            for (int i = 0; i < elements.Length; i++)
            {
                WriteNumber(bytes.AsSpan(i * width, width), elements[i].AsNumber, numericClass);
            }

            return Dataset(type, Reversed(dims), bytes, [ClassAttribute(ClassName(numericClass))]);
        }

        /// <summary>The elements column-major and the MATLAB dimensions, a nested matrix flattened down its columns.</summary>
        private static (JgsValue[] Elements, int[] Dims) Elements(JgsValue value)
        {
            if (value.Type is JgsType.Number or JgsType.Bool or JgsType.Complex)
            {
                return ([value], [1, 1]);
            }

            int[] dims = value.Dims;
            int count = dims.Aggregate(1, static (p, d) => p * d);
            if (JgsMatrix.IsMatrix(value) && value.ArrayLength != count)
            {
                int rows = JgsMatrix.RowCount(value);
                int cols = JgsMatrix.ColCount(value);
                var flat = new JgsValue[rows * cols];
                for (int c = 0; c < cols; c++)
                {
                    for (int r = 0; r < rows; r++)
                    {
                        flat[r + (c * rows)] = JgsMatrix.At(value, r, c);
                    }
                }

                return (flat, [rows, cols]);
            }

            return ([.. Enumerable.Range(0, value.ArrayLength).Select(value.ElementAt)], value.ArrayLength == 0 && dims.Length == 0 ? [0, 0] : dims);
        }

        private long Chars(string[] rows, int height, int width)
        {
            var data = new byte[height * width * 2];
            for (int c = 0; c < width; c++)
            {
                for (int r = 0; r < height; r++)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(((c * height) + r) * 2), rows[r][c]);
                }
            }

            return Dataset(Fixed(2, signed: false), [width, height], data, [ClassAttribute("char"), IntDecode(2)]);
        }

        /// <summary>An empty value: the list of its dimensions, marked empty, as R2025b writes one.</summary>
        private long Empty(string className, int[] dims, string[]? fields)
        {
            int[] shape = dims.Length >= 2 ? dims : [0, 0];
            var data = new byte[shape.Length * 8];
            for (int i = 0; i < shape.Length; i++)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(i * 8), (ulong)shape[i]);
            }

            var attributes = new List<Message> { ClassAttribute(className), EmptyAttribute() };
            if (fields is not null)
            {
                attributes.Add(FieldsAttribute(fields));
            }

            return Dataset(Fixed(8, signed: false), [shape.Length], data, attributes);
        }

        private long Cell(JgsValue value)
        {
            JgsValue[] elements = value.AsCell;
            var data = new byte[elements.Length * 8];
            for (int i = 0; i < elements.Length; i++)
            {
                BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(i * 8), Ref(elements[i]));
            }

            return Dataset(Reference(), Reversed(value.Dims), data, [ClassAttribute("cell")]);
        }

        /// <summary>Writes a value into #refs# and answers its address, which is what a reference holds.</summary>
        private long Ref(JgsValue value)
        {
            long header = Value(value);
            Refs.Add((RefName(_refCount++), header));
            return header;
        }

        private static string RefName(int index)
        {
            var name = new StringBuilder();
            do
            {
                name.Insert(0, (char)('a' + (index % 26)));
                index = (index / 26) - 1;
            }
            while (index >= 0);

            return name.ToString();
        }

        private long Struct(JgsValue value)
        {
            JgsStructArray payload = value.AsStructArray;
            string[] fields = payload.FieldNames;
            if (payload.Length == 0)
            {
                return Empty("struct", value.Dims, fields);
            }

            var children = new List<(string Name, long Header)>();
            if (payload.Length == 1)
            {
                foreach (string field in fields)
                {
                    children.Add((field, Value(payload.Elements[0][field])));
                }
            }
            else
            {
                // A struct array: each field a dataset of references, one per element, in the
                // array's shape and with no class of its own.
                long[] dims = Reversed(value.Dims);
                foreach (string field in fields)
                {
                    var data = new byte[payload.Length * 8];
                    for (int i = 0; i < payload.Length; i++)
                    {
                        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(i * 8), Ref(payload.Elements[i][field]));
                    }

                    children.Add((field, Dataset(Reference(), dims, data, [])));
                }
            }

            return Group(children, [ClassAttribute("struct"), FieldsAttribute(fields)], out _, out _);
        }

        private long Sparse(JGraph.Numerics.Sparse.CscMatrix matrix)
        {
            int nonzeros = matrix.NonZeroCount;
            var children = new List<(string Name, long Header)>
            {
                ("jc", Dataset(Fixed(8, signed: false), [matrix.Cols + 1], UInt64s(matrix.ColumnStarts, matrix.Cols + 1), [])),
            };
            if (nonzeros > 0)
            {
                children.Add(("ir", Dataset(Fixed(8, signed: false), [nonzeros], UInt64s(matrix.RowIndices, nonzeros), [])));
                var values = new byte[nonzeros * 8];
                for (int i = 0; i < nonzeros; i++)
                {
                    BinaryPrimitives.WriteDoubleLittleEndian(values.AsSpan(i * 8), matrix.Values[i]);
                }

                children.Add(("data", Dataset(Float(8), [nonzeros], values, [])));
            }

            var rows = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(rows, (ulong)matrix.Rows);
            Message sparse = Attribute("MATLAB_sparse", Fixed(8, signed: false), Dataspace([]), rows);
            return Group(children, [ClassAttribute("double"), sparse], out _, out _);

            static byte[] UInt64s(int[] values, int count)
            {
                var bytes = new byte[count * 8];
                for (int i = 0; i < count; i++)
                {
                    BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(i * 8), (ulong)values[i]);
                }

                return bytes;
            }
        }

        // --- Objects --------------------------------------------------------------------------

        private long Dataset(byte[] datatype, long[] dims, byte[] data, IReadOnlyList<Message> attributes)
        {
            long address = data.Length == 0 ? Undefined : Put(data);
            var layout = new byte[18];
            layout[0] = 3; // version 3
            layout[1] = 1; // contiguous
            BinaryPrimitives.WriteInt64LittleEndian(layout.AsSpan(2), address);
            BinaryPrimitives.WriteInt64LittleEndian(layout.AsSpan(10), data.Length);
            var messages = new List<Message>
            {
                new(MessageDataspace, Dataspace(dims), []),
                new(MessageDatatype, datatype, []),
                new(MessageFillValue, [2, 1, 2, 0], []), // version 2: allocated early, written if set, none set
                new(MessageLayout, layout, []),
            };
            messages.AddRange(attributes);
            return Header(messages);
        }

        /// <summary>
        /// An old-style group: its names in a local heap, its entries in symbol table nodes of eight,
        /// the nodes under a B-tree; the group's object header holds the symbol table message.
        /// </summary>
        public long Group(List<(string Name, long Header)> entries, IReadOnlyList<Message> attributes, out long tree, out long heap)
        {
            var sorted = entries.OrderBy(static e => e.Name, StringComparer.Ordinal).ToList();
            using var names = new MemoryStream();
            names.Write(new byte[8]); // the empty name at offset 0
            var offsets = new long[sorted.Count];
            for (int i = 0; i < sorted.Count; i++)
            {
                offsets[i] = names.Position;
                byte[] encoded = Encoding.UTF8.GetBytes(sorted[i].Name);
                names.Write(encoded);
                names.Write(new byte[Align8(encoded.Length + 1) - encoded.Length]);
            }

            byte[] segment = names.ToArray();
            long segmentAddress = Put(segment);
            var heapHeader = new byte[32];
            Encoding.ASCII.GetBytes("HEAP").CopyTo(heapHeader, 0);
            BinaryPrimitives.WriteInt64LittleEndian(heapHeader.AsSpan(8), segment.Length);
            BinaryPrimitives.WriteInt64LittleEndian(heapHeader.AsSpan(16), 1); // no free block (H5HL_FREE_NULL)
            BinaryPrimitives.WriteInt64LittleEndian(heapHeader.AsSpan(24), segmentAddress);
            heap = Put(heapHeader);

            var leaves = new List<(long Address, long LastKey)>();
            for (int start = 0; start < sorted.Count; start += 2 * LeafK)
            {
                int count = Math.Min(2 * LeafK, sorted.Count - start);
                var node = new byte[SymbolNodeSize];
                Encoding.ASCII.GetBytes("SNOD").CopyTo(node, 0);
                node[4] = 1;
                BinaryPrimitives.WriteUInt16LittleEndian(node.AsSpan(6), (ushort)count);
                for (int k = 0; k < count; k++)
                {
                    int at = 8 + (k * 40);
                    BinaryPrimitives.WriteInt64LittleEndian(node.AsSpan(at), offsets[start + k]);
                    BinaryPrimitives.WriteInt64LittleEndian(node.AsSpan(at + 8), sorted[start + k].Header);
                }

                leaves.Add((Put(node), offsets[start + count - 1]));
            }

            tree = Tree(leaves, level: 0);
            var table = new byte[16];
            BinaryPrimitives.WriteInt64LittleEndian(table, tree);
            BinaryPrimitives.WriteInt64LittleEndian(table.AsSpan(8), heap);
            var messages = new List<Message> { new(MessageSymbolTable, table, []) };
            messages.AddRange(attributes);
            return Header(messages);
        }

        /// <summary>The B-tree over <paramref name="children"/>, as many levels as their count needs; answers the root.</summary>
        private long Tree(List<(long Address, long LastKey)> children, int level)
        {
            int nodes = Math.Max(1, (children.Count + (2 * InternalK) - 1) / (2 * InternalK));
            Align();
            long first = _bytes.Position - UserBlock;
            var parents = new List<(long Address, long LastKey)>();
            long leftKey = 0;
            for (int n = 0; n < nodes; n++)
            {
                int start = n * 2 * InternalK;
                int count = Math.Min(2 * InternalK, children.Count - start);
                var node = new byte[TreeNodeSize];
                Encoding.ASCII.GetBytes("TREE").CopyTo(node, 0);
                node[4] = 0; // a group node
                node[5] = (byte)level;
                BinaryPrimitives.WriteUInt16LittleEndian(node.AsSpan(6), (ushort)count);
                BinaryPrimitives.WriteInt64LittleEndian(node.AsSpan(8), n == 0 ? Undefined : first + ((n - 1) * TreeNodeSize));
                BinaryPrimitives.WriteInt64LittleEndian(node.AsSpan(16), n == nodes - 1 ? Undefined : first + ((n + 1) * TreeNodeSize));
                int at = 24;
                BinaryPrimitives.WriteInt64LittleEndian(node.AsSpan(at), leftKey);
                at += 8;
                for (int k = 0; k < count; k++)
                {
                    BinaryPrimitives.WriteInt64LittleEndian(node.AsSpan(at), children[start + k].Address);
                    BinaryPrimitives.WriteInt64LittleEndian(node.AsSpan(at + 8), children[start + k].LastKey);
                    at += 16;
                }

                long address = Put(node);
                long lastKey = count > 0 ? children[start + count - 1].LastKey : leftKey;
                parents.Add((address, lastKey));
                leftKey = lastKey;
            }

            return parents.Count == 1 ? parents[0].Address : Tree(parents, level + 1);
        }

        /// <summary>A version 1 object header over <paramref name="messages"/>, each padded to eight bytes.</summary>
        private long Header(List<Message> messages)
        {
            int body = messages.Sum(static m => 8 + Align8(m.Data.Length));
            var header = new byte[16 + body];
            header[0] = 1;
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2), (ushort)messages.Count);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), (uint)body);
            int at = 16;
            var patches = new List<int>();
            foreach (Message message in messages)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(at), (ushort)message.Type);
                BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(at + 2), (ushort)Align8(message.Data.Length));
                message.Data.CopyTo(header, at + 8);
                patches.AddRange(message.HeapPatches.Select(p => at + 8 + p));
                at += 8 + Align8(message.Data.Length);
            }

            long address = Put(header);
            _heapPatches.AddRange(patches.Select(p => UserBlock + address + p));
            return address;
        }

        // --- Attributes -----------------------------------------------------------------------

        private static Message Attribute(string name, byte[] datatype, byte[] dataspace, byte[] data, int[]? heapPatches = null)
        {
            byte[] encodedName = Encoding.ASCII.GetBytes(name + "\0");
            int nameAt = 8;
            int typeAt = nameAt + Align8(encodedName.Length);
            int spaceAt = typeAt + Align8(datatype.Length);
            int dataAt = spaceAt + Align8(dataspace.Length);
            var message = new byte[dataAt + data.Length];
            message[0] = 1;
            BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(2), (ushort)encodedName.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(4), (ushort)datatype.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(6), (ushort)dataspace.Length);
            encodedName.CopyTo(message, nameAt);
            datatype.CopyTo(message, typeAt);
            dataspace.CopyTo(message, spaceAt);
            data.CopyTo(message, dataAt);
            return new Message(MessageAttribute, message, [.. (heapPatches ?? []).Select(p => dataAt + p)]);
        }

        private static Message ClassAttribute(string className)
        {
            byte[] text = Encoding.ASCII.GetBytes(className);
            return Attribute("MATLAB_class", Text(text.Length), Dataspace([]), text);
        }

        private static Message IntDecode(int value)
        {
            var data = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(data, value);
            return Attribute("MATLAB_int_decode", Fixed(4, signed: true), Dataspace([]), data);
        }

        private static Message EmptyAttribute() => Attribute("MATLAB_empty", Fixed(1, signed: false), Dataspace([]), [1]);

        /// <summary>The field names, each a variable-length run of chars kept in the global heap.</summary>
        private Message FieldsAttribute(string[] fields)
        {
            var data = new byte[fields.Length * 16];
            var patches = new int[fields.Length];
            for (int i = 0; i < fields.Length; i++)
            {
                byte[] name = Encoding.ASCII.GetBytes(fields[i]);
                _heapObjects.Add(name);
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i * 16), (uint)name.Length);
                patches[i] = (i * 16) + 4; // the collection's address, known once it is written
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan((i * 16) + 12), (uint)_heapObjects.Count);
            }

            return Attribute("MATLAB_fields", VlenOfChars(), Dataspace([fields.Length]), data, patches);
        }

        // --- Finishing ------------------------------------------------------------------------

        /// <summary>Writes the global heap, patches the addresses that point at it, then the userblock and superblock.</summary>
        public byte[] Finish(long rootHeader, long rootTree, long rootHeap)
        {
            if (_heapObjects.Count > 0)
            {
                using var collection = new MemoryStream();
                collection.Write(new byte[16]);
                for (int i = 0; i < _heapObjects.Count; i++)
                {
                    var head = new byte[16];
                    BinaryPrimitives.WriteUInt16LittleEndian(head, (ushort)(i + 1));
                    BinaryPrimitives.WriteInt64LittleEndian(head.AsSpan(8), _heapObjects[i].Length);
                    collection.Write(head);
                    collection.Write(_heapObjects[i]);
                    collection.Write(new byte[Align8(_heapObjects[i].Length) - _heapObjects[i].Length]);
                }

                long used = collection.Length;
                long size = Math.Max(4096, Align8((int)used + 16));
                if (size - used >= 16)
                {
                    var free = new byte[16];
                    BinaryPrimitives.WriteInt64LittleEndian(free.AsSpan(8), size - used); // object 0: the free space, header and all
                    collection.Write(free);
                }

                collection.Write(new byte[size - collection.Length]);
                byte[] bytes = collection.ToArray();
                Encoding.ASCII.GetBytes("GCOL").CopyTo(bytes, 0);
                bytes[4] = 1;
                BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(8), size);
                long address = Put(bytes);
                foreach (long patch in _heapPatches)
                {
                    _bytes.Position = patch;
                    _bytes.Write(BitConverter.GetBytes(address));
                }

                _bytes.Position = _bytes.Length;
            }

            Align();
            byte[] file = _bytes.ToArray();
            string description = "MATLAB 7.3 MAT-file, Platform: PCWIN64, Created on: "
                + DateTime.Now.ToString("ddd MMM dd HH:mm:ss yyyy", System.Globalization.CultureInfo.InvariantCulture)
                + " HDF5 schema 1.00 .";
            byte[] text = Encoding.ASCII.GetBytes(description.PadRight(116));
            Array.Copy(text, file, 116);
            file[124] = 0x00;
            file[125] = 0x02;
            file[126] = (byte)'I';
            file[127] = (byte)'M';

            Span<byte> super = file.AsSpan(UserBlock, SuperblockSize);
            ReadOnlySpan<byte> signature = [0x89, (byte)'H', (byte)'D', (byte)'F', 0x0D, 0x0A, 0x1A, 0x0A];
            signature.CopyTo(super);
            super[13] = 8; // size of offsets
            super[14] = 8; // size of lengths
            BinaryPrimitives.WriteUInt16LittleEndian(super[16..], LeafK);
            BinaryPrimitives.WriteUInt16LittleEndian(super[18..], InternalK);
            BinaryPrimitives.WriteInt64LittleEndian(super[24..], UserBlock); // base address: the superblock
            BinaryPrimitives.WriteInt64LittleEndian(super[32..], Undefined); // no free-space info
            BinaryPrimitives.WriteInt64LittleEndian(super[40..], file.Length); // end of file: absolute, unlike every other address (R2025b's own, probe_h5)
            BinaryPrimitives.WriteInt64LittleEndian(super[48..], Undefined); // no driver info
            BinaryPrimitives.WriteInt64LittleEndian(super[56..], 0); // the root's name: the empty one
            BinaryPrimitives.WriteInt64LittleEndian(super[64..], rootHeader);
            BinaryPrimitives.WriteInt32LittleEndian(super[72..], 1); // cached: the root's tree and heap
            BinaryPrimitives.WriteInt64LittleEndian(super[80..], rootTree);
            BinaryPrimitives.WriteInt64LittleEndian(super[88..], rootHeap);
            return file;
        }
    }

    // --- Encodings ------------------------------------------------------------------------------

    private static int Align8(int length) => (length + 7) & ~7;

    /// <summary>The reversed dimensions HDF5 stores, MATLAB counting the other way round.</summary>
    private static long[] Reversed(int[] dims) => [.. dims.Reverse().Select(static d => (long)d)];

    /// <summary>A version 1 dataspace; no dimensions is a scalar.</summary>
    private static byte[] Dataspace(long[] dims)
    {
        var bytes = new byte[8 + (dims.Length * 8)];
        bytes[0] = 1;
        bytes[1] = (byte)dims.Length;
        for (int i = 0; i < dims.Length; i++)
        {
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(8 + (i * 8)), dims[i]);
        }

        return bytes;
    }

    private static byte[] Fixed(int size, bool signed)
    {
        var bytes = new byte[12];
        bytes[0] = 0x10; // version 1, fixed-point
        bytes[1] = (byte)(signed ? 0x08 : 0x00); // little-endian
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), size);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10), (ushort)(size * 8));
        return bytes;
    }

    private static byte[] Float(int size)
    {
        bool wide = size == 8;
        var bytes = new byte[20];
        bytes[0] = 0x11; // version 1, floating-point
        bytes[1] = 0x20; // little-endian, the mantissa's leading bit implied
        bytes[2] = (byte)(wide ? 63 : 31); // the sign bit
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), size);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10), (ushort)(size * 8));
        bytes[12] = (byte)(wide ? 52 : 23); // exponent location
        bytes[13] = (byte)(wide ? 11 : 8); // exponent size
        bytes[14] = 0; // mantissa location
        bytes[15] = (byte)(wide ? 52 : 23); // mantissa size
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), wide ? 1023 : 127);
        return bytes;
    }

    /// <summary>A fixed-length ASCII string, null-terminated if there were room.</summary>
    private static byte[] Text(int size)
    {
        var bytes = new byte[8];
        bytes[0] = 0x13;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), size);
        return bytes;
    }

    private static byte[] Reference()
    {
        var bytes = new byte[8];
        bytes[0] = 0x17; // version 1, reference; an object reference
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 8);
        return bytes;
    }

    /// <summary>A complex number: the compound of a real and an imaginary part of one type.</summary>
    private static byte[] Compound(byte[] part, int partSize)
    {
        using var bytes = new MemoryStream();
        var head = new byte[8];
        head[0] = 0x16; // version 1, compound
        BinaryPrimitives.WriteUInt16LittleEndian(head.AsSpan(1), 2);
        BinaryPrimitives.WriteInt32LittleEndian(head.AsSpan(4), 2 * partSize);
        bytes.Write(head);
        foreach ((string name, int offset) in new[] { ("real", 0), ("imag", partSize) })
        {
            var member = new byte[8 + 32];
            Encoding.ASCII.GetBytes(name).CopyTo(member, 0);
            BinaryPrimitives.WriteInt32LittleEndian(member.AsSpan(8), offset);
            bytes.Write(member);
            bytes.Write(part);
        }

        return bytes.ToArray();
    }

    /// <summary>A variable-length sequence of one-byte strings: how R2025b keeps a struct's field names.</summary>
    private static byte[] VlenOfChars()
    {
        var bytes = new byte[8];
        bytes[0] = 0x19; // version 1, variable-length; a sequence
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 16);
        return [.. bytes, .. Text(1)];
    }

    private static (byte[] Type, int Width) NumericType(JgsNumericClass numericClass) => numericClass switch
    {
        JgsNumericClass.Single => (Float(4), 4),
        JgsNumericClass.Int8 => (Fixed(1, true), 1),
        JgsNumericClass.UInt8 => (Fixed(1, false), 1),
        JgsNumericClass.Int16 => (Fixed(2, true), 2),
        JgsNumericClass.UInt16 => (Fixed(2, false), 2),
        JgsNumericClass.Int32 => (Fixed(4, true), 4),
        JgsNumericClass.UInt32 => (Fixed(4, false), 4),
        JgsNumericClass.Int64 => (Fixed(8, true), 8),
        JgsNumericClass.UInt64 => (Fixed(8, false), 8),
        _ => (Float(8), 8),
    };

    private static string ClassName(JgsNumericClass numericClass) => numericClass switch
    {
        JgsNumericClass.Single => "single",
        JgsNumericClass.Int8 => "int8",
        JgsNumericClass.UInt8 => "uint8",
        JgsNumericClass.Int16 => "int16",
        JgsNumericClass.UInt16 => "uint16",
        JgsNumericClass.Int32 => "int32",
        JgsNumericClass.UInt32 => "uint32",
        JgsNumericClass.Int64 => "int64",
        JgsNumericClass.UInt64 => "uint64",
        _ => "double",
    };

    /// <summary>One number in its class's bytes, an integer held at its class's ends as MATLAB holds it.</summary>
    private static void WriteNumber(Span<byte> into, double value, JgsNumericClass numericClass)
    {
        static double Clamp(double v, double low, double high) => double.IsNaN(v) ? 0 : Math.Clamp(Math.Round(v, MidpointRounding.AwayFromZero), low, high);
        switch (numericClass)
        {
            case JgsNumericClass.Single: BinaryPrimitives.WriteSingleLittleEndian(into, (float)value); break;
            case JgsNumericClass.Int8: into[0] = (byte)(sbyte)Clamp(value, sbyte.MinValue, sbyte.MaxValue); break;
            case JgsNumericClass.UInt8: into[0] = (byte)Clamp(value, 0, byte.MaxValue); break;
            case JgsNumericClass.Int16: BinaryPrimitives.WriteInt16LittleEndian(into, (short)Clamp(value, short.MinValue, short.MaxValue)); break;
            case JgsNumericClass.UInt16: BinaryPrimitives.WriteUInt16LittleEndian(into, (ushort)Clamp(value, 0, ushort.MaxValue)); break;
            case JgsNumericClass.Int32: BinaryPrimitives.WriteInt32LittleEndian(into, (int)Clamp(value, int.MinValue, int.MaxValue)); break;
            case JgsNumericClass.UInt32: BinaryPrimitives.WriteUInt32LittleEndian(into, (uint)Clamp(value, 0, uint.MaxValue)); break;
            case JgsNumericClass.Int64:
                BinaryPrimitives.WriteInt64LittleEndian(into, value >= 9.2233720368547758E18 ? long.MaxValue : value <= -9.2233720368547758E18 ? long.MinValue : (long)Clamp(value, long.MinValue, long.MaxValue));
                break;
            case JgsNumericClass.UInt64:
                BinaryPrimitives.WriteUInt64LittleEndian(into, value >= 1.8446744073709552E19 ? ulong.MaxValue : (ulong)Clamp(value, 0, ulong.MaxValue));
                break;
            default: BinaryPrimitives.WriteDoubleLittleEndian(into, value); break;
        }
    }
}
