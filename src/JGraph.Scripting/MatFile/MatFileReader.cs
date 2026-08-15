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
/// arrays, N-D shapes, and struct arrays. Anything else — objects above all — reports what it is
/// rather than mis-reading it.
/// </summary>
/// <remarks>
/// An instance exists for the length of one buffer because byte order is a property of the buffer,
/// not of a call: a compressed element inflates to a second buffer that inherits the file's order,
/// so the swap decision has to travel with the bytes rather than be re-derived at each read.
/// </remarks>
internal sealed class MatFileReader
{
    private readonly byte[] _bytes;

    /// <summary>Whether the file's byte order differs from this machine's, so every word needs reversing.</summary>
    private readonly bool _swap;

    private MatFileReader(byte[] bytes, bool swap)
    {
        _bytes = bytes;
        _swap = swap;
    }

    /// <summary>
    /// Reads variables from <paramref name="path"/>, in file order. Naming <paramref name="wanted"/>
    /// reads only those: a variable nobody asked for is stepped over rather than decoded, so one the
    /// file holds in a form this cannot read never spoils a load that was not about it.
    /// </summary>
    /// <exception cref="InvalidDataException">The file is not a MAT-file, or holds an unsupported type.</exception>
    public static IReadOnlyList<(string Name, JgsValue Value)> Read(
        string path, IReadOnlySet<string>? wanted = null)
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

        // Endian tag at 126: 'IM' means the file is little-endian, 'MI' big-endian.
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

        var reader = new MatFileReader(bytes, fileIsLittleEndian != BitConverter.IsLittleEndian);
        return reader.ReadVariables(128, wanted);
    }

    private List<(string Name, JgsValue Value)> ReadVariables(int from, IReadOnlySet<string>? wanted)
    {
        var variables = new List<(string, JgsValue)>();
        int at = from;
        while (at + 8 <= _bytes.Length)
        {
            (int type, int size, int dataStart, int next) = ReadTag(at);
            if (type == MiCompressed)
            {
                var inner = new MatFileReader(Inflate(dataStart, size), _swap);
                (int innerType, int innerSize, int innerStart, _) = inner.ReadTag(0);
                if (innerType == MiMatrix)
                {
                    inner.Take(innerStart, innerSize, wanted, variables);
                }
            }
            else if (type == MiMatrix)
            {
                Take(dataStart, size, wanted, variables);
            }

            at = next;
        }

        return variables;
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
            MxObject => throw new InvalidDataException(
                $"MAT-file variable '{name}' holds a class object, which cannot be loaded."),
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

        // A char matrix is stored down its columns; JGraph keeps one as a column of equal-length
        // rows, so this is the one place a MAT-file read genuinely transposes.
        int width = count / rows;
        var lines = new JgsValue[rows];
        for (int r = 0; r < rows; r++)
        {
            var row = new char[width];
            for (int c = 0; c < width; c++)
            {
                row[c] = text[(c * rows) + r];
            }

            lines[r] = JgsValue.Str(new string(row));
        }

        JgsValue matrix = JgsValue.Array(lines);
        matrix.Reshape(rows, 1);
        return matrix;
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

        // Field values run element by element, each element's fields consecutively — so an empty
        // struct array still declares its fields even though no values follow.
        var elements = new Dictionary<string, JgsValue>[Math.Max(count, 0)];
        for (int e = 0; e < elements.Length; e++)
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

            elements[e] = fields;
        }

        return count == 1
            ? JgsValue.Struct(elements[0])
            : JgsValue.StructArray(new JgsStructArray(elements, names), count == 0 ? 0 : 1, count);
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
