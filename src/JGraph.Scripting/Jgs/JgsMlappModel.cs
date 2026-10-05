using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using static JGraph.Scripting.MatFile.MatConstants;

namespace JGraph.Scripting.Jgs;

/// <summary>What App Designer's copy of an app's code says, read for a check.</summary>
/// <param name="Fields">The names of the <c>code</c> variable's fields, in order.</param>
/// <param name="ClassName">The class name; null when the field is absent or not text.</param>
/// <param name="EditableSection">The editable section's lines; null when the field is absent.</param>
/// <param name="Callbacks">Each callback and its lines, in order; a null body is one that is not lines of text.</param>
/// <param name="Startup">The startup function and its lines; null when the field is absent.</param>
/// <param name="InputParameters">The startup function's parameters; null when the field is absent.</param>
internal sealed record MlappCodeCopy(
    IReadOnlyList<string> Fields,
    string? ClassName,
    IReadOnlyList<string>? EditableSection,
    IReadOnlyList<(string Name, IReadOnlyList<string>? Code)> Callbacks,
    (string Name, IReadOnlyList<string>? Code)? Startup,
    string? InputParameters);

/// <summary>
/// App Designer's design-time copy of an app's code, kept in step with the text (app-building
/// plan U7b, ADR 0205). An <c>.mlapp</c> holds the code twice: the class file MATLAB runs, and in
/// <c>appdesigner/appModel.mat</c> a variable <c>code</c> that App Designer rebuilds its code view
/// from. An edit written to the first alone runs, and is thrown away the next time App Designer
/// saves the app.
/// </summary>
/// <remarks>
/// <para>
/// The MAT-file's other variables are App Designer's component tree and a legacy object, both MCOS
/// objects kept in the file's subsystem element. Nothing here decodes them: every element but
/// <c>code</c> is copied as the bytes it is, and the header's subsystem offset is moved to where
/// its element now starts - left stale, App Designer cannot load the file (measured in U0).
/// </para>
/// <para>
/// <c>code</c> is a struct of plain values. A field is rewritten only when the text it was cut
/// from accounts for it: what the old text gives for the field must be what the field holds, and
/// then the field becomes what the new text gives. A field the text never matched - a responsive
/// app's layout callback, which App Designer records in another form - is left as App Designer
/// wrote it, and so is any field this does not know. A field is present exactly when it has
/// something in it, which is App Designer's own rule.
/// </para>
/// </remarks>
internal static class JgsMlappModel
{
    private const string Variable = "code";
    private const int HeaderLength = 128;
    private const int SubsystemOffsetAt = 116;

    /// <summary>
    /// Brings the MAT-file's <c>code</c> variable to what <paramref name="now"/> says. True when
    /// the copy then agrees with the text; false when it could not be made to - the text is not
    /// laid out as an app, the file has no <c>code</c> variable, or it is a version 7.3 file
    /// (HDF5, which one shipped app is), whose variables cannot be spliced.
    /// </summary>
    /// <param name="mat">The bytes of <c>appModel.mat</c>.</param>
    /// <param name="before">The layout of the text the file was saved with.</param>
    /// <param name="now">The layout of the text being saved.</param>
    /// <param name="updated">The file to write in its place; null when it is to be left as it is.</param>
    /// <exception cref="InvalidDataException">The bytes are a level-5 MAT-file that ends inside an element.</exception>
    public static bool Update(byte[] mat, JgsMlappLayout before, JgsMlappLayout now, out byte[]? updated)
    {
        updated = null;
        if (!IsLevel5(mat) || !now.IsAppLayout || !now.IsComplete)
        {
            return false;
        }

        List<Element> elements = ElementsOf(mat);
        int at = elements.FindIndex(static e => e.Name == Variable);
        if (at < 0 || StructOf(elements[at].Matrix) is not { } code)
        {
            return false;
        }

        if (!Rewrite(code, before, now))
        {
            return true;
        }

        byte[] rewritten = code.ToMatrix(Variable);
        long subsystem = BinaryPrimitives.ReadInt64LittleEndian(mat.AsSpan(SubsystemOffsetAt));
        long moved = 0;
        using var result = new MemoryStream(mat.Length + rewritten.Length);
        result.Write(mat, 0, HeaderLength);
        for (int i = 0; i < elements.Count; i++)
        {
            if (elements[i].Offset == subsystem)
            {
                moved = result.Position;
            }

            if (i == at)
            {
                result.Write(rewritten);
            }
            else
            {
                result.Write(mat, elements[i].Offset, elements[i].Length);
            }
        }

        byte[] bytes = result.ToArray();
        if (subsystem != 0)
        {
            if (moved == 0)
            {
                throw new InvalidDataException("the model's subsystem offset names no element");
            }

            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(SubsystemOffsetAt), moved);
        }

        updated = bytes;
        return true;
    }

    /// <summary>Whether the bytes are a little-endian level-5 MAT-file, the form whose variables are a run of elements.</summary>
    private static bool IsLevel5(byte[] mat) =>
        mat.Length >= HeaderLength && mat[126] == (byte)'I' && mat[127] == (byte)'M'
        && mat.AsSpan().StartsWith("MATLAB 5.0 MAT-file"u8);

    /// <summary>What the MAT-file's <c>code</c> variable holds, or null when it has none that can be read.</summary>
    /// <exception cref="InvalidDataException">The bytes are a level-5 MAT-file that ends inside an element.</exception>
    public static MlappCodeCopy? Read(byte[] mat)
    {
        if (!IsLevel5(mat))
        {
            return null;
        }

        Element? element = ElementsOf(mat).FirstOrDefault(static e => e.Name == Variable);
        if (element is null || StructOf(element.Matrix) is not { } code)
        {
            return null;
        }

        var callbacks = new List<(string, IReadOnlyList<string>?)>();
        if (code.Field("Callbacks") is { } stored && CallbacksOf(stored) is { } entries)
        {
            callbacks.AddRange(entries.Select(static e => (e.Name, (IReadOnlyList<string>?)e.Lines)));
        }

        (string, IReadOnlyList<string>?)? startup = null;
        if (code.Field("StartupCallback") is { } function && CallbacksOf(function) is { Count: 1 } one)
        {
            startup = (one[0].Name, one[0].Lines);
        }

        return new MlappCodeCopy(
            code.Names,
            code.Field("ClassName") is { } name ? TextOf(name) : null,
            code.Field("EditableSectionCode") is { } section ? LinesOf(section) : null,
            callbacks,
            startup,
            code.Field("InputParameters") is { } parameters ? TextOf(parameters) : null);
    }

    // --- Keeping the copy in step ----------------------------------------------------------------

    /// <summary>Brings each field the old text accounts for to what the new text gives. False when nothing changed.</summary>
    private static bool Rewrite(StructValue code, JgsMlappLayout before, JgsMlappLayout now)
    {
        // Text saved while it could not be read left the copy behind it. There is then no old text
        // to hold a field against, and the copy is brought to the new text outright.
        bool told = before.IsAppLayout && before.IsComplete;
        bool changed = false;

        if (code.Field("ClassName") is { } name && TextOf(name) is { } className && (!told || className == before.ClassName)
            && now.ClassName != className && now.ClassName.Length > 0)
        {
            code.Set("ClassName", Text(now.ClassName));
            changed = true;
        }

        IReadOnlyList<string>? section = code.Field("EditableSectionCode") is { } stored ? LinesOf(stored) : [];
        if (section is not null)
        {
            // Cut as the record was cut: with a Simulink app's generated block in it, when that is
            // the cut of the old text the record matches, and without otherwise.
            bool wide = told && !Same(section, before.EditableSectionCode) && Same(section, before.EditableSectionCodeWithGeneratedBlocks);
            IReadOnlyList<string> was = wide ? before.EditableSectionCodeWithGeneratedBlocks : before.EditableSectionCode;
            IReadOnlyList<string> cut = wide ? now.EditableSectionCodeWithGeneratedBlocks : now.EditableSectionCode;
            if ((!told || Same(section, was)) && !Same(section, cut))
            {
                code.Set("EditableSectionCode", cut.Count == 0 ? null : Lines(cut));
                changed = true;
            }
        }

        changed |= RewriteCallbacks(code, told ? before : null, now);
        changed |= RewriteStartup(code, told ? before : null, now);

        string? parameters = code.Field("InputParameters") is { } held ? TextOf(held) : string.Empty;
        if (parameters is not null && (!told || parameters == before.InputParameters) && parameters != now.InputParameters)
        {
            code.Set("InputParameters", now.InputParameters.Length == 0 ? null : Text(now.InputParameters));
            changed = true;
        }

        return changed;
    }

    private static bool RewriteCallbacks(StructValue code, JgsMlappLayout? before, JgsMlappLayout now)
    {
        List<CallbackEntry>? held = code.Field("Callbacks") is { } stored ? CallbacksOf(stored) : [];
        List<MlappRegion> was = before?.Callbacks.ToList() ?? [];
        if (held is null || (before is not null && !held.Select(static e => e.Name).SequenceEqual(was.Select(static r => r.Name))))
        {
            return false; // the old text does not account for the list
        }

        bool changed = false;
        var entries = new List<(string Name, byte[] Code)>();
        foreach (MlappRegion region in now.Callbacks)
        {
            IReadOnlyList<string> lines = now.CodeOf(region);
            CallbackEntry? old = held.FirstOrDefault(e => e.Name == region.Name);
            MlappRegion? oldRegion = was.FirstOrDefault(r => r.Name == region.Name);

            // With no old text to hold it against, a body is App Designer's own when it is one
            // App Designer generates, and the user's otherwise.
            bool accounted = old?.Lines is not null && (before is null
                ? region.IsUserCode
                : oldRegion is not null && Same(old.Lines, before.CodeOf(oldRegion)));
            if (old is not null && (!accounted || Same(old.Lines!, lines)))
            {
                entries.Add((old.Name, old.Code)); // as App Designer wrote it
            }
            else
            {
                entries.Add((region.Name, Body(lines)));
                changed = true;
            }
        }

        changed |= !entries.Select(static e => e.Name).SequenceEqual(held.Select(static e => e.Name));
        if (changed)
        {
            code.Set("Callbacks", entries.Count == 0 ? null : Functions(entries));
        }

        return changed;
    }

    private static bool RewriteStartup(StructValue code, JgsMlappLayout? before, JgsMlappLayout now)
    {
        List<CallbackEntry>? held = code.Field("StartupCallback") is { } stored ? CallbacksOf(stored) : [];
        if (held is null || held.Count > 1 || (held.Count == 1 && held[0].Lines is null))
        {
            return false;
        }

        if (before is not null)
        {
            MlappRegion? was = before.Startup;
            if ((held.Count == 1) != (was is not null)
                || (was is not null && (held[0].Name != was.Name || !Same(held[0].Lines!, before.CodeOf(was)))))
            {
                return false;
            }
        }

        MlappRegion? startup = now.Startup;
        if (startup is null)
        {
            if (held.Count == 0)
            {
                return false;
            }

            code.Set("StartupCallback", null);
            return true;
        }

        IReadOnlyList<string> lines = now.CodeOf(startup);
        if (held.Count == 1 && held[0].Name == startup.Name && Same(held[0].Lines!, lines))
        {
            return false;
        }

        code.Set("StartupCallback", Functions([(startup.Name, Body(lines))]));
        return true;
    }

    private static bool Same(IReadOnlyList<string> a, IReadOnlyList<string> b) => a.SequenceEqual(b, StringComparer.Ordinal);

    // --- The file's elements ---------------------------------------------------------------------

    /// <summary>One top-level variable: where its element is in the file, and its matrix element inflated.</summary>
    private sealed record Element(int Offset, int Length, string Name, byte[] Matrix);

    private static List<Element> ElementsOf(byte[] mat)
    {
        var elements = new List<Element>();
        int at = HeaderLength;
        while (at + 8 <= mat.Length)
        {
            int type = BinaryPrimitives.ReadInt32LittleEndian(mat.AsSpan(at));
            int size = BinaryPrimitives.ReadInt32LittleEndian(mat.AsSpan(at + 4));
            if (size < 0 || at + 8 + size > mat.Length)
            {
                throw new InvalidDataException("the app's model ends inside an element");
            }

            // A compressed element is not padded; any other is, to eight bytes.
            int length;
            byte[] matrix;
            if (type == MiCompressed)
            {
                length = 8 + size;
                matrix = Inflate(mat, at + 8, size);
            }
            else if (type == MiMatrix)
            {
                length = Math.Min(mat.Length - at, 8 + Padded(size));
                matrix = mat.AsSpan(at, 8 + size).ToArray();
            }
            else
            {
                throw new InvalidDataException($"the app's model holds an element of type {type}");
            }

            elements.Add(new Element(at, length, NameOf(matrix), matrix));
            at += length;
        }

        return elements;
    }

    private static byte[] Inflate(byte[] mat, int start, int size)
    {
        using var compressed = new MemoryStream(mat, start, size);
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var inflated = new MemoryStream();
        zlib.CopyTo(inflated);
        return inflated.ToArray();
    }

    private static int Padded(int size) => (size + 7) & ~7;

    // --- Reading a matrix element ----------------------------------------------------------------

    /// <summary>A data element inside a matrix: its type, where its data is, and where the next element starts.</summary>
    private readonly record struct Part(int Type, int Start, int Size, int Next);

    private static Part PartAt(ReadOnlySpan<byte> bytes, int at)
    {
        if (at + 8 > bytes.Length)
        {
            throw new InvalidDataException("the app's model ends inside a value");
        }

        int word = BinaryPrimitives.ReadInt32LittleEndian(bytes[at..]);
        if ((word >> 16) != 0)
        {
            // The small form: type and size share the first word, the data is the second.
            return new Part(word & 0xFFFF, at + 4, word >> 16, at + 8);
        }

        int size = BinaryPrimitives.ReadInt32LittleEndian(bytes[(at + 4)..]);
        if (size < 0 || at + 8 + size > bytes.Length)
        {
            throw new InvalidDataException("the app's model ends inside a value");
        }

        return new Part(word, at + 8, size, at + 8 + Padded(size));
    }

    /// <summary>A matrix element's class and shape, and where what follows its name starts.</summary>
    private readonly record struct Header(int Class, int[] Dims, string Name, int Body)
    {
        public int Count => Dims.Aggregate(1, static (product, d) => product * d);
    }

    private static Header HeaderOf(ReadOnlySpan<byte> matrix)
    {
        Part outer = PartAt(matrix, 0);
        if (outer.Type != MiMatrix)
        {
            throw new InvalidDataException("the app's model holds a value that is not a matrix");
        }

        if (outer.Size == 0)
        {
            return new Header(MxDouble, [0, 0], string.Empty, outer.Start); // an empty element is []
        }

        Part flags = PartAt(matrix, outer.Start);
        Part dims = PartAt(matrix, flags.Next);
        Part name = PartAt(matrix, dims.Next);
        var shape = new int[dims.Size / 4];
        for (int i = 0; i < shape.Length; i++)
        {
            shape[i] = BinaryPrimitives.ReadInt32LittleEndian(matrix[(dims.Start + (4 * i))..]);
        }

        return new Header(
            matrix[flags.Start], shape, Encoding.ASCII.GetString(matrix.Slice(name.Start, name.Size)), name.Next);
    }

    private static string NameOf(byte[] matrix) => HeaderOf(matrix).Name;

    /// <summary>A char row's text; null when the value is not a char row or an empty array.</summary>
    private static string? TextOf(byte[] matrix)
    {
        Header header = HeaderOf(matrix);
        if (header.Count == 0)
        {
            return string.Empty;
        }

        if (header.Class != MxChar || header.Dims.Length != 2 || header.Dims[0] != 1)
        {
            return null;
        }

        Part data = PartAt(matrix, header.Body);
        ReadOnlySpan<byte> bytes = matrix.AsSpan(data.Start, data.Size);
        return data.Type switch
        {
            MiUInt16 or MiInt16 or MiUtf16 => Encoding.Unicode.GetString(bytes),
            MiUtf8 => Encoding.UTF8.GetString(bytes),
            MiUInt8 or MiInt8 => Encoding.Latin1.GetString(bytes),
            _ => null,
        };
    }

    /// <summary>The elements of a cell or the cells of a struct, each a matrix element of its own.</summary>
    private static List<byte[]> Children(byte[] matrix, int from, int count)
    {
        var children = new List<byte[]>(count);
        int at = from;
        for (int i = 0; i < count; i++)
        {
            Part child = PartAt(matrix, at);
            children.Add(matrix.AsSpan(at, child.Next - at).ToArray());
            at = child.Next;
        }

        return children;
    }

    /// <summary>A cell of char rows as lines; null when the value is anything else. An empty array is no lines.</summary>
    private static List<string>? LinesOf(byte[] matrix)
    {
        Header header = HeaderOf(matrix);
        if (header.Count == 0)
        {
            return [];
        }

        if (header.Class != MxCell)
        {
            return null;
        }

        var lines = new List<string>(header.Count);
        foreach (byte[] cell in Children(matrix, header.Body, header.Count))
        {
            if (TextOf(cell) is not { } line)
            {
                return null;
            }

            lines.Add(line);
        }

        return lines;
    }

    /// <summary>A struct's field names, and its cells element by element.</summary>
    private static (List<string> Names, List<byte[]> Cells, Header Header)? FieldsOf(byte[] matrix)
    {
        Header header = HeaderOf(matrix);
        if (header.Class != MxStruct)
        {
            return null;
        }

        Part length = PartAt(matrix, header.Body);
        int slot = BinaryPrimitives.ReadInt32LittleEndian(matrix.AsSpan(length.Start));
        Part names = PartAt(matrix, length.Next);
        var fields = new List<string>();
        for (int at = names.Start; slot > 0 && at + slot <= names.Start + names.Size; at += slot)
        {
            ReadOnlySpan<byte> name = matrix.AsSpan(at, slot);
            int end = name.IndexOf((byte)0);
            fields.Add(Encoding.ASCII.GetString(end < 0 ? name : name[..end]));
        }

        return (fields, Children(matrix, names.Next, header.Count * fields.Count), header);
    }

    /// <summary>A callback as the copy holds it: its name, its body as lines when it is lines, and the body's own bytes.</summary>
    private sealed record CallbackEntry(string Name, List<string>? Lines, byte[] Code);

    /// <summary>A struct array with the fields <c>Name</c> and <c>Code</c>; null when the value is anything else.</summary>
    private static List<CallbackEntry>? CallbacksOf(byte[] matrix)
    {
        if (HeaderOf(matrix).Count == 0)
        {
            return [];
        }

        if (FieldsOf(matrix) is not { } array || array.Names.Count != 2 || array.Names[0] != "Name" || array.Names[1] != "Code")
        {
            return null;
        }

        var entries = new List<CallbackEntry>();
        for (int i = 0; i < array.Header.Count; i++)
        {
            if (TextOf(array.Cells[2 * i]) is not { } name)
            {
                return null;
            }

            entries.Add(new CallbackEntry(name, LinesOf(array.Cells[(2 * i) + 1]), array.Cells[(2 * i) + 1]));
        }

        return entries;
    }

    /// <summary>The <c>code</c> struct, its fields held as the matrix elements they are.</summary>
    private sealed class StructValue(List<string> names, List<byte[]> cells)
    {
        public List<string> Names { get; } = names;

        public byte[]? Field(string name) => Names.IndexOf(name) is >= 0 and var at ? cells[at] : null;

        /// <summary>Gives a field a value, adding it at the end when it is new; null removes it.</summary>
        public void Set(string name, byte[]? value)
        {
            int at = Names.IndexOf(name);
            if (value is null)
            {
                if (at >= 0)
                {
                    Names.RemoveAt(at);
                    cells.RemoveAt(at);
                }
            }
            else if (at >= 0)
            {
                cells[at] = value;
            }
            else
            {
                Names.Add(name);
                cells.Add(value);
            }
        }

        public byte[] ToMatrix(string name) => Struct(name, Names, 1, cells);
    }

    private static StructValue? StructOf(byte[] matrix) =>
        FieldsOf(matrix) is { } fields && fields.Header.Count == 1 ? new StructValue(fields.Names, fields.Cells) : null;

    // --- Writing a matrix element ----------------------------------------------------------------

    private static byte[] Matrix(int arrayClass, int rows, int columns, string name, Action<BinaryWriter> body)
    {
        using var buffer = new MemoryStream();
        using var w = new BinaryWriter(buffer);
        w.Write(MiMatrix);
        w.Write(0);
        w.Write(MiUInt32);
        w.Write(8);
        w.Write(arrayClass);
        w.Write(0);
        w.Write(MiInt32);
        w.Write(8);
        w.Write(rows);
        w.Write(columns);
        Data(w, MiInt8, Encoding.ASCII.GetBytes(name));
        body(w);
        w.Flush();
        byte[] bytes = buffer.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), bytes.Length - 8);
        return bytes;
    }

    private static void Data(BinaryWriter w, int type, byte[] data)
    {
        w.Write(type);
        w.Write(data.Length);
        w.Write(data);
        w.Write(new byte[Padded(data.Length) - data.Length]);
    }

    /// <summary>
    /// A char row, or the 0-by-0 char an empty text is. The units are UTF-16 and said to be once
    /// one is past ASCII: MATLAB reads units typed miUINT16 in the machine's code page, and a
    /// unit past 255 loses its high byte (probe <c>u7b_char</c>).
    /// </summary>
    private static byte[] Text(string text) =>
        Matrix(MxChar, text.Length == 0 ? 0 : 1, text.Length, string.Empty, w => Data(
            w, text.AsSpan().ContainsAnyExceptInRange('\0', '\x7F') ? MiUtf16 : MiUInt16, Encoding.Unicode.GetBytes(text)));

    /// <summary>A 1-by-N cell of char rows.</summary>
    private static byte[] Lines(IReadOnlyList<string> lines) =>
        Matrix(MxCell, 1, lines.Count, string.Empty, w =>
        {
            foreach (string line in lines)
            {
                w.Write(Text(line));
            }
        });

    /// <summary>A function's body: its lines, or the empty char App Designer records for a body with no lines.</summary>
    private static byte[] Body(IReadOnlyList<string> lines) => lines.Count == 0 ? Text(string.Empty) : Lines(lines);

    /// <summary>A 1-by-N struct array with the fields <c>Name</c> and <c>Code</c>.</summary>
    private static byte[] Functions(IReadOnlyList<(string Name, byte[] Code)> functions)
    {
        var cells = new List<byte[]>();
        foreach ((string name, byte[] code) in functions)
        {
            cells.Add(Text(name));
            cells.Add(code);
        }

        return Struct(string.Empty, ["Name", "Code"], functions.Count, cells);
    }

    private static byte[] Struct(string name, IReadOnlyList<string> fields, int count, IReadOnlyList<byte[]> cells)
    {
        // Each name in a slot long enough for the longest and its terminator; MATLAB reads any length.
        int slot = Math.Max(FieldNameLength, fields.Select(static f => f.Length + 1).DefaultIfEmpty(0).Max());
        return Matrix(MxStruct, 1, count, name, w =>
        {
            w.Write((4 << 16) | MiInt32);
            w.Write(slot);
            var names = new byte[fields.Count * slot];
            for (int i = 0; i < fields.Count; i++)
            {
                Encoding.ASCII.GetBytes(fields[i], names.AsSpan(i * slot));
            }

            Data(w, MiInt8, names);
            foreach (byte[] cell in cells)
            {
                w.Write(cell);
            }
        });
    }
}
