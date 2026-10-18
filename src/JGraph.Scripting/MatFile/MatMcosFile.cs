using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using static JGraph.Scripting.MatFile.MatConstants;

namespace JGraph.Scripting.MatFile;

/// <summary>
/// A level-5 MAT-file with MATLAB's subsystem, read whole as objects that can be changed and written
/// back (designer plan DS6, ADR 0245): App Designer's <c>appModel.mat</c>, whose component tree
/// a save of a MATLAB-made app rewrites while every object it does not change stays as it was.
/// </summary>
/// <remarks>
/// <para>
/// Every object of the table becomes a <see cref="McosObject"/> in the form it was saved in, its
/// stored names in order: a word as its text, a literal as its number, a stored value as the
/// element the file held (<see cref="MatData.Kept"/>), whose references to other objects - a
/// child, a context menu, an enumeration value in a struct - point at those objects. Dynamic
/// properties and the names each object's serialization hook renamed go with their object. The
/// variables are kept elements too, and the names, classes and per-class cells go into a
/// <see cref="McosSeed"/>, so that the per-class values and MATLAB's enumeration records, which
/// name classes and names by index, stay right.
/// </para>
/// <para>
/// Written back (<see cref="Write"/>), the objects are numbered as the variables reach them; an
/// object nothing reaches any more is left out. Nothing else of what was read changes: an object no
/// one changed loads exactly as it did.
/// </para>
/// </remarks>
internal sealed class MatMcosFile
{
    private const int HeaderLength = 128;

    private MatMcosFile(List<MatData.Kept> variables, IReadOnlyList<McosObject> objects, McosSeed seed)
    {
        Variables = variables;
        Objects = objects;
        Seed = seed;
    }

    /// <summary>The variables in the file's order, each its element; one can be replaced by another kept or built element.</summary>
    public List<MatData.Kept> Variables { get; }

    /// <summary>The objects of the table, in its order (object id 1 first).</summary>
    public IReadOnlyList<McosObject> Objects { get; }

    /// <summary>The names, classes and per-class cells the file is written back with.</summary>
    public McosSeed Seed { get; }

    /// <summary>
    /// An element of this file - a part of a stored value - kept as it is, its references pointing at
    /// the objects whose ids they hold.
    /// </summary>
    public MatData.Kept Keep(byte[] element) => KeepIn(element, Objects);

    private static MatData.Kept KeepIn(byte[] element, IReadOnlyList<McosObject> objects) => new(element, MatRaw.References(element)
        .Select(f => new McosReference(f.IdsAt, f.Ids.Select(id => id >= 1 && id <= objects.Count ? objects[id - 1] : null).ToArray(), f.ClassAt))
        .ToArray());

    /// <summary>
    /// The names <paramref name="className"/>'s values at the time of saving hold (the third
    /// per-class cell); empty when the file keeps none for the class.
    /// </summary>
    public IReadOnlySet<string> DefaultNames(string className)
    {
        for (int i = 1; i < Seed.Classes.Count && i < Seed.ClassDefaults.Count; i++)
        {
            (int package, int name) = Seed.Classes[i];
            string qualified = package > 0 ? $"{Seed.Names[package]}.{Seed.Names[name]}" : Seed.Names[name];
            if (qualified == className && Seed.ClassDefaults[i] is MatData.Kept kept)
            {
                return (MatRaw.StructFields(kept.Element) ?? []).Select(static f => f.Name).ToHashSet(StringComparer.Ordinal);
            }
        }

        return new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>The variable named <paramref name="name"/>, or null.</summary>
    public MatData.Kept? Variable(string name) => Variables.Find(v => MatRaw.NameOf(v.Element) == name);

    /// <summary>
    /// The file in <paramref name="mat"/>; null when it is not a level-5 file with a subsystem in the
    /// layout this reads, which is then left to be written as it was.
    /// </summary>
    public static MatMcosFile? Read(byte[] mat) => Read(mat, out _);

    /// <summary><see cref="Read(byte[])"/>, saying why a file was not read.</summary>
    public static MatMcosFile? Read(byte[] mat, out string? why)
    {
        ArgumentNullException.ThrowIfNull(mat);
        why = null;
        if (mat.Length < HeaderLength || mat[126] != (byte)'I' || mat[127] != (byte)'M'
            || !mat.AsSpan().StartsWith("MATLAB 5.0 MAT-file"u8))
        {
            why = "refusal 1 at line 78";
            return null;
        }

        long subsystemAt = BinaryPrimitives.ReadInt64LittleEndian(mat.AsSpan(116));
        var elements = new List<byte[]>();
        byte[]? subsystem = null;
        int at = HeaderLength;
        try
        {
            while (at + 8 <= mat.Length)
            {
                int type = BinaryPrimitives.ReadInt32LittleEndian(mat.AsSpan(at));
                int length = BinaryPrimitives.ReadInt32LittleEndian(mat.AsSpan(at + 4));
                if (length < 0 || at + 8L + length > mat.Length)
                {
                    why = "refusal 2 at line 93";
                    return null;
                }

                byte[] element = type == MiCompressed ? Inflate(mat, at + 8, length) : mat[at..(at + 8 + length)];
                if (at == subsystemAt)
                {
                    subsystem = element;
                }
                else
                {
                    elements.Add(element);
                }

                at += 8 + length;
            }
        }
        catch (InvalidDataException ex)
        {
            why = ex.Message + " refusal 3 at line 111";
            return null;
        }

        if (subsystem is null || MatRaw.Uint8Data(subsystem) is not { Length: > 16 } stream)
        {
            why = "refusal 4 at line 116";
            return null;
        }

        int holderLength = BinaryPrimitives.ReadInt32LittleEndian(stream.AsSpan(12));
        byte[] holder = stream[8..(16 + holderLength)];
        byte[] trailer = stream[(16 + holderLength)..];
        if (MatRaw.StructFields(holder) is not [("MCOS", var wrapper)]
            || MatRaw.Opaque(wrapper) is not { ClassName: "FileWrapper__" } opaque
            || MatRaw.CellItems(opaque.Content) is not { Count: >= 4 } cells
            || MatRaw.Uint8Data(cells[0]) is not { Length: >= 40 } table)
        {
            why = "refusal 5 at line 127";
            return null;
        }

        return Decode(mat, elements, table, cells, trailer, out why);
    }

    /// <summary>The file with its objects as they now are, each variable compressed as MATLAB writes it.</summary>
    public byte[] Write() =>
        MatMcosWriter.WriteFile(Variables.Select(static v => (string.Empty, (MatData)v)).ToList(), Seed, compress: true);

    private static byte[] Inflate(byte[] mat, int at, int length)
    {
        using var input = new MemoryStream(mat, at, length);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    private static MatMcosFile? Decode(byte[] mat, List<byte[]> elements, byte[] table, List<byte[]> cells, byte[] trailer, out string? why)
    {
        why = null;
        uint U32(int a) => a + 4 <= table.Length ? BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(a)) : 0;
        int count = (int)U32(4);
        var offsets = new int[8];
        for (int i = 0; i < 8; i++)
        {
            offsets[i] = (int)U32(8 + (4 * i));
        }

        // A version 3 table (older releases) keeps two per-class cells, not three. A table may end
        // with the dynamic-property lists, its last two offsets zero: it has no lists of renamed
        // names. Written back it is a version 4 table, as R2025b writes.
        uint version = U32(0);
        if (version is not (3 or 4))
        {
            why = $"The subsystem's object table is version {version}, which this does not read.";
            return null;
        }

        if (offsets[6] == 0 && offsets[7] == 0)
        {
            offsets[6] = offsets[7] = offsets[5];
        }

        if (offsets.Any(o => o < 40 || o > table.Length) || offsets[5] != offsets[6])
        {
            why = "refusal 7 at line 170";
            return null; // a segment this has not seen filled
        }

        var names = new List<string> { string.Empty };
        int at = 40;
        while (names.Count <= count)
        {
            int end = Array.IndexOf(table, (byte)0, at);
            if (end < 0 || end >= offsets[0])
            {
                why = "refusal 8 at line 180";
                return null;
            }

            names.Add(Encoding.UTF8.GetString(table, at, end - at));
            at = end + 1;
        }

        var classes = new List<(int, int)>();
        for (int a = offsets[0]; a + 16 <= offsets[1]; a += 16)
        {
            classes.Add(((int)U32(a), (int)U32(a + 4)));
        }

        List<(int Name, int Kind, uint Datum)[]> saved = PropertyLists(U32, offsets[1], offsets[2]);
        var records = new List<uint[]>();
        for (int a = offsets[2]; a + 24 <= offsets[3]; a += 24)
        {
            records.Add([U32(a), U32(a + 4), U32(a + 8), U32(a + 12), U32(a + 16), U32(a + 20)]);
        }

        List<(int Name, int Kind, uint Datum)[]> plain = PropertyLists(U32, offsets[3], offsets[4]);
        List<int[]> dynamic = IdLists(U32, offsets[4], offsets[5]);

        string NameAt(long i) => i >= 0 && i < names.Count ? names[(int)i] : throw new InvalidDataException("A name index is past the table.");
        string ClassAt(int c) => classes[c] is var (package, name) && package > 0 ? $"{NameAt(package)}.{NameAt(name)}" : NameAt(classes[c].Item2);

        try
        {
            var objects = new List<McosObject>(records.Count);
            for (int i = 1; i < records.Count; i++)
            {
                objects.Add(new McosObject(ClassAt((int)records[i][0])) { SavedForm = records[i][3] > 0 && records[i][4] == 0 });
            }

            int perClass = version == 3 ? 2 : 3;
            int valueCount = cells.Count - 2 - perClass;
            MatData.Kept Keep(byte[] element) => KeepIn(element, objects);

            for (int i = 1; i < records.Count; i++)
            {
                McosObject o = objects[i - 1];
                (int Name, int Kind, uint Datum)[] list = o.SavedForm ? ListAt(saved, records[i][3]) : ListAt(plain, records[i][4]);
                foreach ((int name, int kind, uint datum) in list)
                {
                    o.Set(NameAt(name), kind switch
                    {
                        0 => new McosValue.Word(NameAt(datum)),
                        2 => new McosValue.Literal((int)datum),
                        1 when datum < valueCount => new McosValue.Value(Keep(cells[(int)datum + 2])),
                        _ => throw new InvalidDataException("A stored value is past the subsystem's cell."),
                    });
                }

                uint dependency = records[i][5];
                if (dependency < dynamic.Count)
                {
                    o.Dynamic.AddRange(dynamic[(int)dependency].Select(d => d >= 1 && d <= objects.Count
                        ? objects[d - 1]
                        : throw new InvalidDataException("A dynamic property names no object.")));
                }
            }

            // Each object whose hook renamed names: its id, how many, and the names.
            int r = offsets[6];
            uint records6 = r + 4 <= offsets[7] ? U32(r) : 0;
            r += 4;
            for (uint k = 0; k < records6; k++)
            {
                uint id = U32(r);
                uint n = U32(r + 4);
                r += 8;
                for (uint j = 0; j < n; j++, r += 4)
                {
                    if (id >= 1 && id <= objects.Count)
                    {
                        objects[(int)id - 1].Renamed.Add(NameAt(U32(r)));
                    }
                }
            }

            var seed = new McosSeed(
                names,
                classes,
                perClass == 3 ? (MatRaw.CellItems(cells[^3]) ?? []).Select(i => (MatData)Keep(i)).ToList() : [],
                MatRaw.Integers(cells[^2]) ?? [],
                (MatRaw.CellItems(cells[^1]) ?? []).Select(i => (MatData)Keep(i)).ToList(),
                mat[..116],
                trailer);
            return new MatMcosFile(elements.Select(Keep).ToList(), objects, seed);
        }
        catch (InvalidDataException ex)
        {
            why = ex.Message + " refusal 9 at line 271";
            return null;
        }
    }

    private static (int Name, int Kind, uint Datum)[] ListAt(List<(int Name, int Kind, uint Datum)[]> lists, uint index) =>
        index < lists.Count ? lists[(int)index] : throw new InvalidDataException("An object names a property list past the table.");

    /// <summary>The property lists between two offsets; the first is the empty list at index 0.</summary>
    private static List<(int Name, int Kind, uint Datum)[]> PropertyLists(Func<int, uint> u32, int from, int to)
    {
        var lists = new List<(int, int, uint)[]>();
        int at = from;
        while (at + 4 <= to)
        {
            int count = (int)u32(at);
            at += 4;
            if (count < 0 || at + (12L * count) > to)
            {
                throw new InvalidDataException("A property list runs past its segment.");
            }

            var list = new (int, int, uint)[count];
            for (int i = 0; i < count; i++)
            {
                list[i] = ((int)u32(at), (int)u32(at + 4), u32(at + 8));
                at += 12;
            }

            if ((at - from) % 8 != 0)
            {
                at += 4;
            }

            lists.Add(list);
        }

        return lists;
    }

    private static List<int[]> IdLists(Func<int, uint> u32, int from, int to)
    {
        var lists = new List<int[]>();
        int at = from;
        while (at + 4 <= to)
        {
            int count = (int)u32(at);
            var ids = new int[count];
            for (int i = 0; i < count; i++)
            {
                ids[i] = (int)u32(at + 4 + (4 * i));
            }

            at += 4 + (4 * count);
            if ((at - from) % 8 != 0)
            {
                at += 4;
            }

            lists.Add(ids);
        }

        return lists;
    }
}
