using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using static JGraph.Scripting.MatFile.MatConstants;

namespace JGraph.Scripting.MatFile;

/// <summary>
/// A plain value as a level-5 matrix element holds it, for <see cref="MatMcosWriter"/>: a number
/// array of one class, a char array, a cell array or a struct array, each with its size; an object
/// of the subsystem; or an element kept as another file held it.
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

    /// <summary>A subsystem object, as MATLAB stores one in a variable: an opaque element of its class holding a reference to it.</summary>
    public sealed record Object(McosObject Target) : MatData([1, 1]);

    /// <summary>
    /// A matrix element kept as the file it came from held it (designer plan DS6, ADR 0245): its
    /// bytes, and where in them it refers to subsystem objects, which are written as the ids those
    /// objects have in the new file.
    /// </summary>
    public sealed record Kept(byte[] Element, IReadOnlyList<McosReference> References) : MatData([]);

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

/// <summary>
/// One reference to subsystem objects inside a kept element: where its first object id is, the
/// objects in id order (null for an id that named no object, which is written as it was), and
/// where its class id is. The class id is rewritten only when <paramref name="ClassName"/> is given.
/// </summary>
internal sealed record McosReference(int IdsAt, IReadOnlyList<McosObject?> Targets, int ClassAt, string? ClassName = null);

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

    /// <summary>
    /// Whether the object was saved through its class's <c>saveobj</c>, which MATLAB keeps in a list
    /// of its own (a string is); a file read keeps the form each object had.
    /// </summary>
    public bool SavedForm { get; set; }

    /// <summary>The names the object's serialization hook stored under another name, which MATLAB lists for each object (a public name stored for its <c>_I</c> storage).</summary>
    public List<string> Renamed { get; } = [];

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

    /// <summary>Stores <paramref name="name"/> last, wherever it was before, so that <c>load</c> assigns it after everything else.</summary>
    public McosObject SetLast(string name, McosValue value)
    {
        Remove(name);
        _entries.Add((name, value));
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
/// What a file read for rewriting brings to the new subsystem (designer plan DS6, ADR 0245): its
/// names and classes, kept in their places because the per-class values and MATLAB's enumeration
/// records name them by index, the three per-class cells, and the bytes around the object table.
/// </summary>
/// <param name="Names">The name table; the first is the empty name.</param>
/// <param name="Classes">The class table (a package name's index and a class name's index); the first is the empty class.</param>
/// <param name="ClassCells">The first per-class cell's items, one per class.</param>
/// <param name="ClassNumbers">The second per-class cell, an <c>int32</c> per class.</param>
/// <param name="ClassDefaults">The third per-class cell's items: each class's values at the time of saving.</param>
/// <param name="Header">The file's 116-byte description text.</param>
/// <param name="Trailer">The subsystem stream's bytes after the <c>FileWrapper__</c> struct.</param>
internal sealed record McosSeed(
    IReadOnlyList<string> Names,
    IReadOnlyList<(int Package, int Name)> Classes,
    IReadOnlyList<MatData> ClassCells,
    IReadOnlyList<double> ClassNumbers,
    IReadOnlyList<MatData> ClassDefaults,
    byte[] Header,
    byte[] Trailer);

/// <summary>
/// Writes a level-5 MAT-file whose variables hold MATLAB class instances - App Designer's
/// <c>components</c> and <c>appData</c> beside the plain <c>code</c> - with every object they
/// refer to in MATLAB's subsystem (designer plan DS1b, ADR 0244; several variables and files read
/// for rewriting, DS6, ADR 0245).
/// </summary>
/// <remarks>
/// <para>
/// The layout is the one <see cref="MatMcos"/> reads, measured further in DS1b against R2025b's own
/// files (rewritten from the parsed table they are byte for byte the same): the header's offset to
/// a <c>uint8</c> variable whose bytes are a small MAT stream - a struct whose field <c>MCOS</c>
/// holds the opaque <c>FileWrapper__</c> cell, then a second stream whose <c>MCOS</c> is empty.
/// The cell is the byte table, an empty element, the stored values, and three cells with one entry
/// per class. The table holds the names, the classes (package and name), the property lists of
/// objects saved through <c>saveobj</c>, the objects (a class, the property list in one of the two
/// forms, and a dependency id that indexes the dynamic properties), the other property lists (a
/// name, a kind - 0 a word from the names, 1 a stored value, 2 a literal - and the datum), the
/// dynamic-property lists, and for each object whose serialization hook stored a name under another
/// the names it renamed.
/// </para>
/// <para>
/// What a new file leaves out, and R2025b still loads the same: every class's saved-time defaults
/// (the three per-class cells are written empty), and so the enumeration objects those defaults
/// refer to; the renamed-names lists; and any object not reachable from the variables. Without the
/// defaults, <c>load</c> assigns exactly the names stored, each through the class's own set methods
/// and serialization hooks, in the order stored; a name the file does not store keeps the class's
/// own default. Strings are not made here (MATLAB saves them through <c>saveobj</c>); a char takes
/// their place. A file read for rewriting (<see cref="McosSeed"/>) keeps all of that: its names and
/// classes in their places, its per-class cells, each object's form and renamed names, and every
/// value it held as its bytes, the object ids inside them written anew. Objects are numbered as
/// they are reached from the variables, and each object's dependency id is its own id.
/// </para>
/// </remarks>
internal static class MatMcosWriter
{
    /// <summary>The first word of every reference to subsystem objects.</summary>
    private const uint ReferenceMark = MatRaw.ReferenceMark;

    /// <summary>The description text of a file this writes.</summary>
    private const string Description = "MATLAB 5.0 MAT-file, Platform: PCWIN64, Created by JGraph's designer";

    /// <summary>
    /// The MAT-file whose variable <paramref name="variable"/> is a 1-by-1 struct with the field
    /// <paramref name="field"/> holding <paramref name="root"/>.
    /// </summary>
    public static byte[] Write(McosObject root, string variable, string field)
    {
        ArgumentNullException.ThrowIfNull(root);
        return WriteFile([(variable, MatData.Struct((field, new MatData.Object(root))))], null, compress: false);
    }

    /// <summary>
    /// The MAT-file holding <paramref name="variables"/> in order, each a value under its name (a
    /// kept element carries its own name), with every object they reach in the subsystem. A
    /// <paramref name="seed"/> brings a read file's tables; <paramref name="compress"/> writes each
    /// element compressed, as MATLAB does.
    /// </summary>
    public static byte[] WriteFile(IReadOnlyList<(string Name, MatData Value)> variables, McosSeed? seed, bool compress)
    {
        ArgumentNullException.ThrowIfNull(variables);
        var table = new Table(seed);
        List<Built> roots = variables.Select(static v => v.Value is MatData.Kept kept ? new Built(kept.Element, kept.References) : Build(v.Name, v.Value)).ToList();
        byte[] subsystem = table.Subsystem(roots, seed, out List<byte[]> patched);

        using var stream = new MemoryStream();
        var header = new byte[116];
        Array.Fill(header, (byte)' ');
        if (seed is not null)
        {
            Array.Copy(seed.Header, header, Math.Min(116, seed.Header.Length));
        }
        else
        {
            byte[] text = Encoding.ASCII.GetBytes(Description);
            Array.Copy(text, header, text.Length);
        }

        stream.Write(header);
        stream.Write(new byte[12]);
        foreach (byte[] root in patched)
        {
            stream.Write(Packed(root, compress));
        }

        long subsystemAt = stream.Position;
        stream.Write(Packed(Uint8Row(subsystem), compress));
        byte[] file = stream.ToArray();
        BinaryPrimitives.WriteInt64LittleEndian(file.AsSpan(116), subsystemAt);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(124), 0x0100);
        file[126] = (byte)'I';
        file[127] = (byte)'M';
        return file;
    }

    /// <summary>An element as it goes into the file: compressed with zlib inside a <c>miCOMPRESSED</c> element, or as it is.</summary>
    private static byte[] Packed(byte[] element, bool compress)
    {
        if (!compress)
        {
            return element;
        }

        using var packed = new MemoryStream();
        using (var zlib = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(element);
        }

        var tagged = new byte[8 + packed.Length];
        BinaryPrimitives.WriteInt32LittleEndian(tagged, MiCompressed);
        BinaryPrimitives.WriteInt32LittleEndian(tagged.AsSpan(4), (int)packed.Length);
        packed.ToArray().CopyTo(tagged, 8);
        return tagged;
    }

    /// <summary>The names, classes, objects and stored values of one file, gathered as the variables are walked.</summary>
    private sealed class Table
    {
        private readonly List<string> _names = [string.Empty];
        private readonly Dictionary<string, int> _nameIds = new(StringComparer.Ordinal) { [string.Empty] = 0 };
        private readonly List<(int Package, int Name)> _classes = [(0, 0)];
        private readonly Dictionary<string, int> _classIds = new(StringComparer.Ordinal);
        private readonly List<byte[]> _cells = [];
        private readonly Dictionary<McosObject, int> _ids = new(ReferenceEqualityComparer.Instance);

        public Table(McosSeed? seed)
        {
            if (seed is null)
            {
                return;
            }

            _names.Clear();
            _nameIds.Clear();
            for (int i = 0; i < seed.Names.Count; i++)
            {
                _names.Add(seed.Names[i]);
                _nameIds.TryAdd(seed.Names[i], i);
            }

            _classes.Clear();
            _classes.AddRange(seed.Classes);
            for (int i = 1; i < _classes.Count; i++)
            {
                _classIds.TryAdd(QualifiedName(_classes[i]), i);
            }
        }

        private string QualifiedName((int Package, int Name) c) =>
            c.Package > 0 ? $"{_names[c.Package]}.{_names[c.Name]}" : _names[c.Name];

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

        /// <summary>A built element with every object it refers to written as that object's id, and its class where the reference names it.</summary>
        public byte[] Patch(Built built)
        {
            if (built.References.Count == 0)
            {
                return built.Bytes;
            }

            byte[] bytes = (byte[])built.Bytes.Clone();
            foreach (McosReference reference in built.References)
            {
                for (int i = 0; i < reference.Targets.Count; i++)
                {
                    if (reference.Targets[i] is { } target)
                    {
                        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(reference.IdsAt + (4 * i)), (uint)_ids[target]);
                    }
                }

                if (reference.ClassName is { } className)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(reference.ClassAt), (uint)ClassId(className));
                }
            }

            return bytes;
        }

        /// <summary>
        /// The subsystem's bytes: the <c>FileWrapper__</c> stream for every object reachable from
        /// <paramref name="roots"/>; and the roots with their references written, before the table is.
        /// </summary>
        public byte[] Subsystem(List<Built> roots, McosSeed? seed, out List<byte[]> patchedRoots)
        {
            var order = new List<McosObject>();
            var pending = new Stack<McosObject>();

            void Reach(IEnumerable<McosObject?> targets)
            {
                foreach (McosObject? t in targets)
                {
                    if (t is not null && !_ids.ContainsKey(t))
                    {
                        Visit(t);
                    }
                }
            }

            void Visit(McosObject o)
            {
                if (_ids.ContainsKey(o))
                {
                    return;
                }

                _ids[o] = order.Count + 1;
                order.Add(o);
                foreach ((_, McosValue value) in o.Entries)
                {
                    switch (value)
                    {
                        case McosValue.Refs refs:
                            Reach(refs.Objects);
                            break;
                        case McosValue.Value v:
                            Reach(Targets(v.Data));
                            break;
                    }
                }

                Reach(o.Dynamic);
            }

            foreach (Built root in roots)
            {
                Reach(root.References.SelectMany(static r => r.Targets));
            }

            if (seed is not null)
            {
                Reach(seed.ClassCells.Concat(seed.ClassDefaults).SelectMany(Targets));
            }

            foreach (McosObject o in order)
            {
                ClassId(o.ClassName);
            }

            patchedRoots = roots.Select(Patch).ToList();
            var saved = new List<(int Name, int Kind, uint Datum)[]> { Array.Empty<(int, int, uint)>() };
            var plain = new List<(int Name, int Kind, uint Datum)[]> { Array.Empty<(int, int, uint)>() };
            var records = new List<uint>(new uint[6]);
            foreach (McosObject o in order)
            {
                var list = new List<(int, int, uint)>(o.Entries.Count);
                foreach ((string name, McosValue value) in o.Entries)
                {
                    list.Add(value switch
                    {
                        McosValue.Word w => (Name(name), 0, (uint)Name(w.Text)),
                        McosValue.Literal l => (Name(name), 2, (uint)l.Number),
                        McosValue.Value v => (Name(name), 1, Store(Patch(Build(string.Empty, v.Data)))),
                        McosValue.Refs r => (Name(name), 1, Store(Patch(Reference(r)))),
                        _ => throw new InvalidOperationException("Unknown stored value."),
                    });
                }

                List<(int, int, uint)[]> into = o.SavedForm ? saved : plain;
                into.Add([.. list]);
                int id = _ids[o];
                records.AddRange([(uint)ClassId(o.ClassName), 0, 0,
                    o.SavedForm ? (uint)(into.Count - 1) : 0, o.SavedForm ? 0 : (uint)(into.Count - 1), (uint)id]);
            }

            // The dependency id is the object's own id; it indexes the dynamic-property lists.
            var dynamic = new List<int[]> { Array.Empty<int>() };
            dynamic.AddRange(order.Select(o => o.Dynamic.Select(d => _ids[d]).ToArray()));
            var renamed = new List<uint>();
            int renamedCount = 0;
            foreach (McosObject o in order.Where(static o => o.Renamed.Count > 0))
            {
                renamed.AddRange([(uint)_ids[o], (uint)o.Renamed.Count, .. o.Renamed.Select(n => (uint)Name(n))]);
                renamedCount++;
            }

            renamed.Insert(0, (uint)renamedCount);
            if (renamed.Count % 2 != 0)
            {
                renamed.Add(0);
            }

            byte[] table = TableBytes(saved, records, plain, dynamic, renamed);

            int classes = _classes.Count;
            byte[] emptyStruct = StructElement(string.Empty, [], [], [1, 0]);
            byte[] PerClass(IReadOnlyList<MatData>? kept, int i) =>
                kept is not null && i < kept.Count ? Patch(Build(string.Empty, kept[i])) : emptyStruct;
            var cells = new List<byte[]> { Uint8Column(table), EmptyElement() };
            cells.AddRange(_cells);
            cells.Add(CellElement(Enumerable.Range(0, classes).Select(i => PerClass(seed?.ClassCells, i)).ToArray(), [classes, 1]));
            cells.Add(Build(string.Empty, new MatData.Numbers("int32", [classes, 1],
                Enumerable.Range(0, classes).Select(i => seed is not null && i < seed.ClassNumbers.Count ? seed.ClassNumbers[i] : 0).ToArray())).Bytes);
            cells.Add(CellElement(Enumerable.Range(0, classes).Select(i => PerClass(seed?.ClassDefaults, i)).ToArray(), [classes, 1]));

            byte[] wrapper = Opaque(string.Empty, "FileWrapper__", CellElement([.. cells], [cells.Count, 1]));
            byte[] holder = StructElement(string.Empty, ["MCOS"], [[wrapper]], [1, 1]);
            byte[] trailer = seed?.Trailer ?? Uint8Row([.. StreamHead, .. StructElement(string.Empty, ["MCOS"], [[EmptyElement()]], [1, 1])]);
            return [.. StreamHead, .. holder, .. trailer];
        }

        private uint Store(byte[] element)
        {
            _cells.Add(element);
            return (uint)(_cells.Count - 1);
        }

        private byte[] TableBytes(
            List<(int Name, int Kind, uint Datum)[]> saved, List<uint> records, List<(int Name, int Kind, uint Datum)[]> plain,
            List<int[]> dynamic, List<uint> renamed)
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
                PropertyLists(saved.Count > 1 ? saved : []),
                Words(records),
                PropertyLists(plain),
                IdLists(dynamic),
                Array.Empty<byte>(),
                Words(renamed),
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

    /// <summary>The objects a value refers to, at any depth.</summary>
    private static IEnumerable<McosObject?> Targets(MatData data) => data switch
    {
        MatData.Kept kept => kept.References.SelectMany(static r => r.Targets),
        MatData.Object o => [o.Target],
        MatData.Cells c => c.Items.SelectMany(Targets),
        MatData.Structs s => s.Elements.SelectMany(static e => e).SelectMany(Targets),
        _ => [],
    };

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

    /// <summary>An element's bytes and where in them it refers to subsystem objects.</summary>
    internal sealed record Built(byte[] Bytes, IReadOnlyList<McosReference> References)
    {
        public static Built Plain(byte[] bytes) => new(bytes, []);
    }

    /// <summary>A stored value's matrix element, with no name; for a value that refers to no object.</summary>
    public static byte[] Element(MatData data) => Build(string.Empty, data).Bytes;

    /// <summary>A value's matrix element under <paramref name="name"/>, with its references.</summary>
    private static Built Build(string name, MatData data) => data switch
    {
        MatData.Kept kept => new Built(kept.Element, kept.References),
        MatData.Object o => Matrix([Built.Plain(Flags(MxOpaque)), Built.Plain(Sub(MiInt8, Encoding.ASCII.GetBytes(name))),
            Built.Plain(Sub(MiInt8, "MCOS"u8.ToArray())), Built.Plain(Sub(MiInt8, Encoding.ASCII.GetBytes(o.Target.ClassName))),
            Reference(new McosValue.Refs([o.Target]))]),
        MatData.Numbers n => Built.Plain(NumericElement(name, n)),
        MatData.Chars c => Built.Plain(Matrix(Flags(MxChar), Dimensions(c.Size), Sub(MiInt8, Encoding.ASCII.GetBytes(name)),
            Sub(MiUtf8, Encoding.UTF8.GetBytes(c.Characters)))),
        MatData.Cells c => Matrix([Built.Plain(Flags(MxCell)), Built.Plain(Dimensions(c.Size)),
            Built.Plain(Sub(MiInt8, Encoding.ASCII.GetBytes(name))), .. c.Items.Select(i => Build(string.Empty, i))]),
        MatData.Structs s => StructBuilt(name, s),
        _ => throw new InvalidOperationException("Unknown value."),
    };

    private static Built StructBuilt(string name, MatData.Structs s)
    {
        int width = s.Fields.Length == 0 ? 1 : s.Fields.Max(static f => Encoding.ASCII.GetByteCount(f)) + 1;
        var packed = new byte[width * s.Fields.Length];
        for (int i = 0; i < s.Fields.Length; i++)
        {
            Encoding.ASCII.GetBytes(s.Fields[i]).CopyTo(packed, i * width);
        }

        var widthBytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(widthBytes, width);
        return Matrix([Built.Plain(Flags(MxStruct)), Built.Plain(Dimensions(s.Size)), Built.Plain(Sub(MiInt8, Encoding.ASCII.GetBytes(name))),
            Built.Plain(Sub(MiInt32, widthBytes)), Built.Plain(Sub(MiInt8, packed)),
            .. s.Elements.SelectMany(static e => e).Select(v => Build(string.Empty, v))]);
    }

    /// <summary>A reference column to <paramref name="refs"/>' objects, with its ids and class written when the file is.</summary>
    private static Built Reference(McosValue.Refs refs)
    {
        string className = refs.Objects.Count > 0 ? refs.Objects[0].ClassName
            : refs.EmptyClass ?? throw new InvalidOperationException("An empty reference needs its class.");
        var words = new List<uint> { ReferenceMark, 2 };
        words.AddRange(refs.Objects.Count > 0 ? [1, (uint)refs.Objects.Count] : [0, 0]);
        words.AddRange(refs.Objects.Select(static _ => 0u));
        words.Add(0);
        byte[] column = Uint32Column([.. words]);
        int dataAt = column.Length - (((words.Count * 4) + 7) / 8 * 8);
        return new Built(column, [new McosReference(dataAt + 16, refs.Objects, dataAt + (4 * (words.Count - 1)), className)]);
    }

    private static byte[] NumericElement(string name, MatData.Numbers n)
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

        return Matrix(Flags(arrayClass | flagBits), Dimensions(n.Size), Sub(MiInt8, Encoding.ASCII.GetBytes(name)), Sub(dataType, data));
    }

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

    private static byte[] Matrix(params byte[][] parts) => Matrix(parts.Select(Built.Plain).ToArray()).Bytes;

    /// <summary>A matrix element of <paramref name="parts"/>, their references moved to where each part lands.</summary>
    private static Built Matrix(Built[] parts)
    {
        int length = parts.Sum(static p => p.Bytes.Length);
        var element = new byte[8 + length];
        BinaryPrimitives.WriteInt32LittleEndian(element, MiMatrix);
        BinaryPrimitives.WriteInt32LittleEndian(element.AsSpan(4), length);
        var references = new List<McosReference>();
        int at = 8;
        foreach (Built part in parts)
        {
            part.Bytes.CopyTo(element, at);
            references.AddRange(part.References.Select(r => r with { IdsAt = r.IdsAt + at, ClassAt = r.ClassAt + at }));
            at += part.Bytes.Length;
        }

        return new Built(element, references);
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
