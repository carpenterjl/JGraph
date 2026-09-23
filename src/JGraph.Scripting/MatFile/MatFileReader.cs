using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.MatFile.Hdf5;
using static JGraph.Scripting.MatFile.MatConstants;

namespace JGraph.Scripting.MatFile;

/// <summary>
/// Reads level-5 MAT-files, including MATLAB's own: compressed and uncompressed elements, either
/// byte order, every integer/float numeric encoding, complex, logical, sparse, char matrices, cell
/// arrays, N-D shapes, and struct arrays; and, through an <see cref="IMatObjectBinder"/>, the
/// object and function elements <see cref="MatFileWriter"/> writes (V6, ADR 0167). Anything else
/// reports what it is rather than mis-reading it.
/// </summary>
/// <remarks>
/// An instance exists for the length of one buffer because byte order is a property of the buffer,
/// not of a call: a compressed element inflates to a second buffer that inherits the file's order,
/// so the swap decision has to travel with the bytes rather than be re-derived at each read. The
/// handle objects met so far travel with it too, so an alias inside a compressed element finds the
/// instance an earlier element made.
/// </remarks>
internal sealed class MatFileReader
{
    private readonly byte[] _bytes;

    /// <summary>Whether the file's byte order differs from this machine's, so every word needs reversing.</summary>
    private readonly bool _swap;

    private readonly IMatObjectBinder? _binder;

    /// <summary>The handle objects read so far, by the element id the writer gave them.</summary>
    private readonly Dictionary<int, JgsValue> _handles;

    private MatFileReader(byte[] bytes, bool swap, IMatObjectBinder? binder, Dictionary<int, JgsValue> handles)
    {
        _bytes = bytes;
        _swap = swap;
        _binder = binder;
        _handles = handles;
    }

    /// <summary>
    /// Reads variables from <paramref name="path"/>, in file order. Naming <paramref name="wanted"/>
    /// reads only those: a variable nobody asked for is stepped over rather than decoded, so one the
    /// file holds in a form this cannot read never spoils a load that was not about it. An object
    /// or a function handle is built by <paramref name="binder"/>; with none, either is refused.
    /// </summary>
    /// <exception cref="InvalidDataException">The file is not a MAT-file, or holds an unsupported type.</exception>
    public static IReadOnlyList<(string Name, JgsValue Value)> Read(
        string path, IReadOnlySet<string>? wanted = null, IMatObjectBinder? binder = null)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < 128)
        {
            throw new InvalidDataException("Not a MAT-file: the 128-byte header is missing.");
        }

        // The v7.3 format is HDF5 all the way down, and MATLAB writes its description text into a
        // userblock in front of it. Recognising the HDF5 signature is what tells the two apart; the
        // endian tag an HDF5 file has no reason to carry never comes into it.
        if (Hdf5File.Looks(bytes))
        {
            return MatV73Reader.Read(bytes, wanted);
        }

        var reader = new MatFileReader(bytes, Swaps(bytes), binder, new Dictionary<int, JgsValue>());
        return reader.ReadVariables(128, wanted);
    }

    /// <summary>
    /// What <paramref name="path"/> holds, by header alone: each variable's name, shape and class,
    /// read without decoding a value — what <c>whos(m)</c>, <c>who(m)</c> and <c>size(m, 'v')</c>
    /// on a <c>matfile</c> ask (V6, #112). An object's class is the name it was saved under.
    /// </summary>
    /// <exception cref="InvalidDataException">The file is not a MAT-file.</exception>
    public static IReadOnlyList<(string Name, int[] Dims, string Class)> Describe(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < 128)
        {
            throw new InvalidDataException("Not a MAT-file: the 128-byte header is missing.");
        }

        if (Hdf5File.Looks(bytes))
        {
            return MatV73Reader.Describe(bytes);
        }

        var reader = new MatFileReader(bytes, Swaps(bytes), null, new Dictionary<int, JgsValue>());
        var described = new List<(string, int[], string)>();
        reader.WalkElements(128, (inner, start, _) => described.Add(inner.HeaderOf(start)));
        return described;
    }

    /// <summary>Whether the file's byte order differs from this machine's. Endian tag at 126: 'IM' little-endian, 'MI' big-endian.</summary>
    private static bool Swaps(byte[] bytes)
    {
        bool fileIsLittleEndian;
        if (bytes[126] == 'I' && bytes[127] == 'M')
        {
            fileIsLittleEndian = true;
        }
        else if (bytes[126] == 'M' && bytes[127] == 'I')
        {
            fileIsLittleEndian = false;
        }
        else
        {
            throw new InvalidDataException("Not a level-5 MAT-file (its endian tag is missing).");
        }

        return fileIsLittleEndian != BitConverter.IsLittleEndian;
    }

    private List<(string Name, JgsValue Value)> ReadVariables(int from, IReadOnlySet<string>? wanted)
    {
        var variables = new List<(string, JgsValue)>();
        WalkElements(from, (inner, start, size) => inner.Take(start, size, wanted, variables));
        return variables;
    }

    /// <summary>
    /// Visits every top-level matrix element, inflating a compressed one into a reader of its own
    /// first; <paramref name="visit"/> is handed the reader the element's bytes live in.
    /// </summary>
    private void WalkElements(int from, Action<MatFileReader, int, int> visit)
    {
        int at = from;
        while (at + 8 <= _bytes.Length)
        {
            (int type, int size, int dataStart, int next) = ReadTag(at);
            if (type == MiCompressed)
            {
                var inner = new MatFileReader(Inflate(dataStart, size), _swap, _binder, _handles);
                (int innerType, int innerSize, int innerStart, _) = inner.ReadTag(0);
                if (innerType == MiMatrix)
                {
                    visit(inner, innerStart, innerSize);
                }
            }
            else if (type == MiMatrix)
            {
                visit(this, dataStart, size);
            }

            // Compressed top-level elements are not padded to an eight-byte boundary.
            at = type == MiCompressed ? dataStart + size : next;
        }
    }

    private void Take(
        int start, int size, IReadOnlySet<string>? wanted, List<(string, JgsValue)> into)
    {
        if (wanted is not null && !wanted.Contains(NameOf(start)))
        {
            return;
        }

        into.Add(ReadMatrix(start, size));
    }

    /// <summary>The name a variable's element carries, read without decoding what follows it.</summary>
    private string NameOf(int start)
    {
        int at = start;
        (_, _, _, at) = ReadTag(at); // Array flags.
        (_, _, _, at) = ReadTag(at); // Dimensions.
        (_, int nameSize, int nameStart, _) = ReadTag(at);
        return Encoding.ASCII.GetString(_bytes, nameStart, nameSize);
    }

    /// <summary>A variable's name, shape and class from its element's header, without decoding the value.</summary>
    private (string Name, int[] Dims, string Class) HeaderOf(int start)
    {
        int at = start;
        (_, _, int flagsStart, at) = ReadTag(at);
        int flags = I32(flagsStart);
        int arrayClass = flags & 0xFF;

        (_, int dimsSize, int dimsStart, at) = ReadTag(at);
        var dims = new int[dimsSize / 4];
        for (int i = 0; i < dims.Length; i++)
        {
            dims[i] = I32(dimsStart + (4 * i));
        }

        (_, int nameSize, int nameStart, at) = ReadTag(at);
        string name = Encoding.ASCII.GetString(_bytes, nameStart, nameSize);

        string className = arrayClass switch
        {
            _ when (flags & FlagLogical) != 0 => "logical",
            MxChar => "char",
            MxCell => "cell",
            MxStruct => "struct",
            MxFunction => "function_handle",
            MxObject => ReadText(ref at), // the class name element follows the variable's name
            MxSingle => "single",
            MxInt8 => "int8",
            MxUInt8 => "uint8",
            MxInt16 => "int16",
            MxUInt16 => "uint16",
            MxInt32 => "int32",
            MxUInt32 => "uint32",
            MxInt64 => "int64",
            MxUInt64 => "uint64",
            _ => "double",
        };

        return (name, dims, className);
    }

    /// <summary>One text element — a class name — as ASCII, stepping past it.</summary>
    private string ReadText(ref int at)
    {
        (_, int size, int start, int next) = ReadTag(at);
        at = next;
        return Encoding.ASCII.GetString(_bytes, start, size);
    }

    private byte[] Inflate(int start, int size)
    {
        using var compressed = new MemoryStream(_bytes, start, size);
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var inflated = new MemoryStream();
        zlib.CopyTo(inflated);
        return inflated.ToArray();
    }

    private (string Name, JgsValue Value) ReadMatrix(int start, int size)
    {
        int at = start;
        int end = start + size;

        (int flagsType, _, int flagsStart, int afterFlags) = ReadTag(at);
        if (flagsType != MiUInt32)
        {
            throw new InvalidDataException("Malformed MAT-file: an array's flags are missing.");
        }

        int flags = I32(flagsStart);
        int arrayClass = flags & 0xFF;
        bool isComplex = (flags & FlagComplex) != 0;
        bool isLogical = (flags & FlagLogical) != 0;
        int second = I32(flagsStart + 4); // nzmax for a sparse array; an object's element id
        at = afterFlags;

        (int dimsType, int dimsSize, int dimsStart, int afterDims) = ReadTag(at);
        if (dimsType != MiInt32)
        {
            throw new InvalidDataException("Malformed MAT-file: an array's dimensions are missing.");
        }

        var dims = new int[dimsSize / 4];
        for (int i = 0; i < dims.Length; i++)
        {
            dims[i] = I32(dimsStart + (4 * i));
        }

        at = afterDims;

        (_, int nameSize, int nameStart, int afterName) = ReadTag(at);
        string name = Encoding.ASCII.GetString(_bytes, nameStart, nameSize);
        at = afterName;

        int rows = dims.Length > 0 ? dims[0] : 0;
        int cols = dims.Length > 1 ? dims[1] : 0;
        long count = rows;
        for (int i = 1; i < dims.Length; i++)
        {
            count *= dims[i];
        }

        JgsValue value = arrayClass switch
        {
            MxChar => ReadChar(ref at, rows, (int)count),
            MxCell => ReadCellArray(ref at, end, (int)count, dims),
            MxStruct => ReadStruct(ref at, end, (int)count),
            MxSparse => ReadSparse(ref at, rows, cols, isComplex),
            MxObject => ReadObject(ref at, end, name, second, (flags & FlagDeletedHandle) != 0),
            MxFunction => ReadFunction(ref at, end, name),
            MxDouble or MxSingle or MxInt8 or MxUInt8 or MxInt16 or MxUInt16
                or MxInt32 or MxUInt32 or MxInt64 or MxUInt64 =>
                ReadNumeric(ref at, arrayClass, rows, cols, isComplex, isLogical),
            _ => throw new InvalidDataException(
                $"MAT-file variable '{name}' uses an unsupported class ({arrayClass})."),
        };

        // The dims element is authoritative; a 2-D shape is already right, so only N-D needs saying.
        if (dims.Length > 2 && value.Type == JgsType.Array && value.ArrayLength == count)
        {
            value.ReshapeDims(dims);
        }

        return (name, value);
    }

    private JgsValue ReadNumeric(ref int at, int arrayClass, int rows, int cols, bool isComplex, bool isLogical)
    {
        double[] real = ReadNumericData(ref at);
        double[]? imaginary = isComplex ? ReadNumericData(ref at) : null;

        if ((long)rows * cols != real.Length)
        {
            rows = 1;
            cols = real.Length;
        }

        if (isComplex)
        {
            var elements = new JgsValue[real.Length];
            for (int i = 0; i < real.Length; i++)
            {
                double im = imaginary![i];
                elements[i] = im == 0 ? JgsValue.Number(real[i]) : JgsValue.ComplexNum(new Complex(real[i], im));
            }

            return elements.Length == 1 ? elements[0] : JgsMatrix.FromElements(elements, rows, cols);
        }

        if (isLogical)
        {
            // A logical is a class, not a width: whatever integer type carried the bits on disk, what
            // comes back has to answer 'logical' to class(), which is what the Bool elements do.
            var mask = new JgsValue[real.Length];
            for (int i = 0; i < real.Length; i++)
            {
                mask[i] = JgsValue.Bool(real[i] != 0);
            }

            return real.Length == 1 && rows == 1 && cols == 1
                ? mask[0]
                : JgsMatrix.FromElements(mask, rows, cols);
        }

        // Column-major on disk is exactly how a shaped value stores itself, so this is a straight
        // adoption rather than a transpose (ADR 0043).
        JgsValue value = real.Length == 1 && rows == 1 && cols == 1
            ? JgsValue.Number(real[0])
            : JgsMatrix.FromColumnMajor(real, rows, cols);
        value.SetNumericClass(NumericClassOf(arrayClass));
        return value;
    }

    /// <summary>The class tag a stored array class means, so <c>int8</c> survives a round trip (M47).</summary>
    private static JgsNumericClass NumericClassOf(int arrayClass) => arrayClass switch
    {
        MxSingle => JgsNumericClass.Single,
        MxInt8 => JgsNumericClass.Int8,
        MxUInt8 => JgsNumericClass.UInt8,
        MxInt16 => JgsNumericClass.Int16,
        MxUInt16 => JgsNumericClass.UInt16,
        MxInt32 => JgsNumericClass.Int32,
        MxUInt32 => JgsNumericClass.UInt32,
        MxInt64 => JgsNumericClass.Int64,
        MxUInt64 => JgsNumericClass.UInt64,
        _ => JgsNumericClass.Double,
    };

    private JgsValue ReadSparse(ref int at, int rows, int cols, bool isComplex)
    {
        if (isComplex)
        {
            throw new InvalidDataException("Complex sparse matrices cannot be loaded.");
        }

        double[] rowIndices = ReadNumericData(ref at);
        double[] columnStarts = ReadNumericData(ref at);
        double[] values = ReadNumericData(ref at);

        // Compressed sparse column on disk is the same layout CscMatrix keeps, but its constructor
        // takes triplets, which also re-checks the file's arithmetic instead of trusting it.
        var triplets = new List<(int Row, int Col, double Value)>(values.Length);
        for (int c = 0; c + 1 < columnStarts.Length; c++)
        {
            for (int k = (int)columnStarts[c]; k < (int)columnStarts[c + 1] && k < values.Length; k++)
            {
                triplets.Add(((int)rowIndices[k], c, values[k]));
            }
        }

        try
        {
            return JgsValue.Sparse(JGraph.Numerics.Sparse.CscMatrix.FromTriplets(rows, cols, triplets));
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new InvalidDataException("Malformed MAT-file: a sparse matrix indexes outside itself.");
        }
    }

    private JgsValue ReadChar(ref int at, int rows, int count)
    {
        (int type, int size, int dataStart, int next) = ReadTag(at);
        at = next;

        var sb = new StringBuilder(Math.Max(count, 0));
        switch (type)
        {
            case MiUInt16:
                for (int i = 0; i + 1 < size; i += 2)
                {
                    sb.Append((char)U16(dataStart + i));
                }

                break;
            case MiUtf8:
                sb.Append(Encoding.UTF8.GetString(_bytes, dataStart, size));
                break;
            case MiInt8 or MiUInt8:
                sb.Append(Encoding.ASCII.GetString(_bytes, dataStart, size));
                break;
            default:
                throw new InvalidDataException($"MAT-file text uses an unsupported encoding ({type}).");
        }

        string text = sb.ToString();
        if (rows <= 1 || text.Length != count || count == 0)
        {
            return JgsValue.Str(text);
        }

        // A char matrix is stored down its columns, which since M105 is exactly how JGraph holds one
        // too — so the transpose this used to do is now only the read back into rows that
        // JgsValue.CharMatrix takes.
        int width = count / rows;
        var lines = new string[rows];
        for (int r = 0; r < rows; r++)
        {
            var row = new char[width];
            for (int c = 0; c < width; c++)
            {
                row[c] = text[(c * rows) + r];
            }

            lines[r] = new string(row);
        }

        return JgsValue.CharMatrix(lines);
    }

    private JgsValue ReadCellArray(ref int at, int end, int count, int[] dims)
    {
        var elements = new List<JgsValue>(Math.Max(count, 0));
        while (at + 8 <= end && elements.Count < count)
        {
            (int type, int size, int dataStart, int next) = ReadTag(at);
            if (type != MiMatrix)
            {
                throw new InvalidDataException("Malformed MAT-file: a cell element is not a matrix.");
            }

            elements.Add(ReadMatrix(dataStart, size).Value);
            at = next;
        }

        JgsValue cell = JgsValue.Cell([.. elements]);
        if (elements.Count == count)
        {
            cell.ReshapeDims(dims);
        }

        return cell;
    }

    private JgsValue ReadStruct(ref int at, int end, int count)
    {
        string[] names = ReadFieldNames(ref at);
        var elements = new Dictionary<string, JgsValue>[Math.Max(count, 0)];
        for (int e = 0; e < elements.Length; e++)
        {
            elements[e] = ReadFields(ref at, end, names);
        }

        return count == 1
            ? JgsValue.Struct(elements[0])
            : JgsValue.StructArray(new JgsStructArray(elements, names), count == 0 ? 0 : 1, count);
    }

    /// <summary>
    /// An object element (V6, #111): the class name, then the properties as a struct's fields. A
    /// handle's element id names the instance every mention of it shares — the instance is made
    /// before its properties are read, so a property that holds the object itself finds it — and a
    /// mention after the first carries no fields and answers the instance already made. Without a
    /// binder the element is refused by name, as it always was.
    /// </summary>
    private JgsValue ReadObject(ref int at, int end, string name, int id, bool deleted)
    {
        string className = ReadText(ref at);
        if (_binder is null)
        {
            throw new InvalidDataException(
                $"MAT-file variable '{name}' holds a class object, which cannot be loaded.");
        }

        string[] names = ReadFieldNames(ref at);
        if (id > 0 && _handles.TryGetValue(id, out JgsValue? known))
        {
            ReadFields(ref at, end, names); // the writer sends none; stepping past any is harmless
            return known;
        }

        JgsValue instance = _binder.NewObject(className, deleted, name);
        if (id > 0)
        {
            _handles[id] = instance;
        }

        _binder.SetProperties(instance, ReadFields(ref at, end, names));
        return instance;
    }

    /// <summary>A function element (V6, #113): the struct <c>functions</c> reports, handed to the binder to re-make.</summary>
    private JgsValue ReadFunction(ref int at, int end, string name)
    {
        if (_binder is null)
        {
            throw new InvalidDataException(
                $"MAT-file variable '{name}' holds a function handle, which cannot be loaded.");
        }

        Dictionary<string, JgsValue> body = ReadFields(ref at, end, ReadFieldNames(ref at));
        string text = body.TryGetValue("function", out JgsValue? function) && function.Type == JgsType.String
            ? function.AsString
            : throw new InvalidDataException($"MAT-file variable '{name}' is a function handle with no function.");
        string type = body.TryGetValue("type", out JgsValue? kind) && kind.Type == JgsType.String ? kind.AsString : "simple";
        IReadOnlyDictionary<string, JgsValue>? workspace =
            body.TryGetValue("workspace", out JgsValue? captured) && captured.Type == JgsType.Struct && !captured.IsStructArray
                ? captured.AsStruct
                : null;
        return _binder.Function(text, type, workspace, name);
    }

    /// <summary>The field-name-length element, then the names in their fixed slots.</summary>
    private string[] ReadFieldNames(ref int at)
    {
        (_, _, int lengthStart, int afterLength) = ReadTag(at);
        int slot = I32(lengthStart);
        at = afterLength;

        (_, int namesSize, int namesStart, int afterNames) = ReadTag(at);
        at = afterNames;
        int fieldCount = slot > 0 ? namesSize / slot : 0;
        var names = new string[fieldCount];
        for (int i = 0; i < fieldCount; i++)
        {
            names[i] = Encoding.ASCII.GetString(_bytes, namesStart + (i * slot), slot).TrimEnd('\0');
        }

        return names;
    }

    /// <summary>
    /// One element's fields, consecutively, in the order the names were declared — so an empty
    /// struct array still declares its fields even though no values follow.
    /// </summary>
    private Dictionary<string, JgsValue> ReadFields(ref int at, int end, string[] names)
    {
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        foreach (string field in names)
        {
            if (at + 8 > end)
            {
                throw new InvalidDataException("Malformed MAT-file: a struct array ends mid-element.");
            }

            (int type, int size, int dataStart, int next) = ReadTag(at);
            if (type != MiMatrix)
            {
                throw new InvalidDataException("Malformed MAT-file: a struct field is not a matrix.");
            }

            fields[field] = ReadMatrix(dataStart, size).Value;
            at = next;
        }

        return fields;
    }

    /// <summary>Reads one numeric subelement, widening whatever integer encoding it uses to doubles.</summary>
    private double[] ReadNumericData(ref int at)
    {
        (int type, int size, int dataStart, int next) = ReadTag(at);
        at = next;

        Func<int, double> read;
        int width;
        switch (type)
        {
            case MiDouble: read = i => BitConverter.Int64BitsToDouble(I64(i)); width = 8; break;
            case MiSingle: read = i => BitConverter.Int32BitsToSingle(I32(i)); width = 4; break;
            case MiInt8: read = i => (sbyte)_bytes[i]; width = 1; break;
            case MiUInt8: read = i => _bytes[i]; width = 1; break;
            case MiInt16: read = i => (short)U16(i); width = 2; break;
            case MiUInt16: read = i => U16(i); width = 2; break;
            case MiInt32: read = i => I32(i); width = 4; break;
            case MiUInt32: read = i => (uint)I32(i); width = 4; break;
            case MiInt64: read = i => I64(i); width = 8; break;
            case MiUInt64: read = i => (ulong)I64(i); width = 8; break;
            default:
                throw new InvalidDataException($"MAT-file data uses an unsupported encoding ({type}).");
        }

        var values = new double[size / width];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = read(dataStart + (i * width));
        }

        return values;
    }

    /// <summary>
    /// Reads a tag at <paramref name="at"/>: either the normal 8-byte form or the small-element
    /// form, where sizes of 4 bytes or less pack the data into the tag itself.
    /// </summary>
    private (int Type, int Size, int DataStart, int Next) ReadTag(int at)
    {
        int first = I32(at);
        int small = (first >> 16) & 0xFFFF;
        if (small != 0)
        {
            // Small-element form: the data lives inside the 8-byte tag itself.
            return (first & 0xFFFF, small, at + 4, at + 8);
        }

        int size = I32(at + 4);
        return (first, size, at + 8, at + 8 + size + Pad(size));
    }

    private int I32(int at)
    {
        int value = BitConverter.ToInt32(_bytes, at);
        return _swap ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    private long I64(int at)
    {
        long value = BitConverter.ToInt64(_bytes, at);
        return _swap ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    private ushort U16(int at)
    {
        ushort value = BitConverter.ToUInt16(_bytes, at);
        return _swap ? BinaryPrimitives.ReverseEndianness(value) : value;
    }

    private static int Pad(int size) => (8 - (size % 8)) % 8;
}
