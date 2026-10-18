using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using static JGraph.Scripting.MatFile.MatConstants;

namespace JGraph.Scripting.Jgs;

/// <content>
/// The level-5 form of the model (ADR 0205): a run of elements, each variable one matrix element,
/// compressed in every shipped app. <c>code</c> is read apart into its fields' elements and put back
/// together; every other element is copied as its bytes, and the subsystem offset follows its element.
/// </content>
internal static partial class JgsMlappModel
{
    private sealed class Level5Copy : ICodeCopy
    {
        private const int HeaderLength = 128;
        private const int SubsystemOffsetAt = 116;

        private readonly byte[] _mat;
        private readonly List<Element> _elements;
        private readonly int _at;

        private Level5Copy(byte[] mat, List<Element> elements, int at, CodeFields fields)
        {
            _mat = mat;
            _elements = elements;
            _at = at;
            Fields = fields;
        }

        public CodeFields Fields { get; }

        /// <summary>The file's <c>code</c> struct; null when the bytes are not a level-5 file or it has no such struct.</summary>
        public static Level5Copy? Open(byte[] mat)
        {
            if (!IsLevel5(mat))
            {
                return null;
            }

            List<Element> elements = ElementsOf(mat);
            int at = elements.FindIndex(static e => e.Name == Variable);
            if (at < 0 || FieldsOf(elements[at].Matrix) is not { } code || code.Header.Count != 1)
            {
                return null;
            }

            var fields = new List<(string, FieldValue)>();
            for (int i = 0; i < code.Names.Count; i++)
            {
                fields.Add((code.Names[i], Decode(code.Cells[i])));
            }

            return new Level5Copy(mat, elements, at, new CodeFields(fields));
        }

        /// <summary>A new <c>code</c> element holding <paramref name="fields"/>, for a model written from nothing.</summary>
        public static byte[] NewElement(CodeFields fields) =>
            Struct(Variable, [.. fields.All.Select(static f => f.Name)], 1, [.. fields.All.Select(static f => Encode(f.Value))]);

        public byte[] Write()
        {
            byte[] rewritten = Struct(Variable, [.. Fields.All.Select(static f => f.Name)], 1, [.. Fields.All.Select(static f => Encode(f.Value))]);
            long subsystem = BinaryPrimitives.ReadInt64LittleEndian(_mat.AsSpan(SubsystemOffsetAt));
            long moved = 0;
            using var result = new MemoryStream(_mat.Length + rewritten.Length);
            result.Write(_mat, 0, HeaderLength);
            for (int i = 0; i < _elements.Count; i++)
            {
                if (_elements[i].Offset == subsystem)
                {
                    moved = result.Position;
                }

                if (i == _at)
                {
                    result.Write(rewritten);
                }
                else
                {
                    result.Write(_mat, _elements[i].Offset, _elements[i].Length);
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

            return bytes;
        }

        /// <summary>Whether the bytes are a little-endian level-5 MAT-file, the form whose variables are a run of elements.</summary>
        private static bool IsLevel5(byte[] mat) =>
            mat.Length >= HeaderLength && mat[126] == (byte)'I' && mat[127] == (byte)'M'
            && mat.AsSpan().StartsWith("MATLAB 5.0 MAT-file"u8);

        // --- Between the model and a matrix element ---------------------------------------------

        private static FieldValue Decode(byte[] matrix)
        {
            if (HeaderOf(matrix).Count == 0)
            {
                return new EmptyValue(matrix);
            }

            if (TextOf(matrix) is { } text)
            {
                return new TextValue(text, matrix);
            }

            if (LinesOf(matrix) is { } lines)
            {
                return new LinesValue(lines, matrix);
            }

            if (CallbacksOf(matrix) is { } entries)
            {
                return new FunctionsValue(entries, matrix);
            }

            return new OpaqueValue(matrix);
        }

        private static byte[] Encode(FieldValue value) => value.Origin is byte[] origin ? origin : value switch
        {
            TextValue text => Text(text.Text),
            LinesValue lines => Lines(lines.Lines),
            FunctionsValue functions => Functions(functions.Functions),
            _ => throw new InvalidOperationException("a value this did not read must be text, lines or functions"),
        };

        /// <summary>A function's body: the bytes the file held, or its lines, or the empty char App Designer records for a body with no lines.</summary>
        private static byte[] Body(FunctionEntry entry) => entry.Body is byte[] held
            ? held
            : entry.Lines is { Count: > 0 } lines ? Lines(lines) : Text(string.Empty);

        // --- The file's elements -----------------------------------------------------------------

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

                elements.Add(new Element(at, length, HeaderOf(matrix).Name, matrix));
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

        // --- Reading a matrix element ------------------------------------------------------------

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

        /// <summary>A struct array with the fields <c>Name</c> and <c>Code</c>; null when the value is anything else.</summary>
        private static List<FunctionEntry>? CallbacksOf(byte[] matrix)
        {
            if (HeaderOf(matrix).Count == 0)
            {
                return [];
            }

            if (FieldsOf(matrix) is not { } array || array.Names.Count != 2 || array.Names[0] != "Name" || array.Names[1] != "Code")
            {
                return null;
            }

            var entries = new List<FunctionEntry>();
            for (int i = 0; i < array.Header.Count; i++)
            {
                if (TextOf(array.Cells[2 * i]) is not { } name)
                {
                    return null;
                }

                entries.Add(new FunctionEntry(name, LinesOf(array.Cells[(2 * i) + 1]), array.Cells[(2 * i) + 1]));
            }

            return entries;
        }

        // --- Writing a matrix element ------------------------------------------------------------

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

        /// <summary>A 1-by-N struct array with the fields <c>Name</c> and <c>Code</c>.</summary>
        private static byte[] Functions(IReadOnlyList<FunctionEntry> functions)
        {
            var cells = new List<byte[]>();
            foreach (FunctionEntry function in functions)
            {
                cells.Add(Text(function.Name));
                cells.Add(Body(function));
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
}
