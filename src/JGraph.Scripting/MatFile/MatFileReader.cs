using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using JGraph.Scripting.Jgs;
using static JGraph.Scripting.MatFile.MatConstants;

namespace JGraph.Scripting.MatFile;

/// <summary>
/// Reads level-5 MAT-files, including MATLAB's own: compressed and uncompressed elements, every
/// integer/float numeric encoding (widened to double), complex, logical, char, cell, and scalar
/// struct arrays. Anything else — objects, sparse matrices, struct arrays — reports what it is
/// rather than mis-reading it.
/// </summary>
internal static class MatFileReader
{
    /// <summary>Reads every variable from <paramref name="path"/>, in file order.</summary>
    /// <exception cref="InvalidDataException">The file is not a level-5 MAT-file, or holds an unsupported type.</exception>
    public static IReadOnlyList<(string Name, JgsValue Value)> Read(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < 128)
        {
            throw new InvalidDataException("Not a MAT-file: the 128-byte header is missing.");
        }

        // Version tag at 124; 'IM' = little-endian, the only order supported here.
        if (bytes[126] == 'M' && bytes[127] == 'I')
        {
            throw new InvalidDataException("Big-endian MAT-files are not supported.");
        }

        if (bytes[126] != 'I' || bytes[127] != 'M')
        {
            throw new InvalidDataException("Not a level-5 MAT-file (its endian tag is missing).");
        }

        var variables = new List<(string, JgsValue)>();
        int at = 128;
        while (at + 8 <= bytes.Length)
        {
            (int type, int size, int dataStart, int next) = ReadTag(bytes, at);
            if (type == MiCompressed)
            {
                byte[] inflated = Inflate(bytes, dataStart, size);
                (int innerType, int innerSize, int innerStart, _) = ReadTag(inflated, 0);
                if (innerType == MiMatrix)
                {
                    variables.Add(ReadMatrix(inflated, innerStart, innerSize));
                }
            }
            else if (type == MiMatrix)
            {
                variables.Add(ReadMatrix(bytes, dataStart, size));
            }

            at = next;
        }

        return variables;
    }

    private static byte[] Inflate(byte[] bytes, int start, int size)
    {
        using var compressed = new MemoryStream(bytes, start, size);
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var inflated = new MemoryStream();
        zlib.CopyTo(inflated);
        return inflated.ToArray();
    }

    private static (string Name, JgsValue Value) ReadMatrix(byte[] bytes, int start, int size)
    {
        int at = start;
        int end = start + size;

        (int flagsType, _, int flagsStart, int afterFlags) = ReadTag(bytes, at);
        if (flagsType != MiUInt32)
        {
            throw new InvalidDataException("Malformed MAT-file: an array's flags are missing.");
        }

        int flags = BitConverter.ToInt32(bytes, flagsStart);
        int arrayClass = flags & 0xFF;
        bool isComplex = (flags & FlagComplex) != 0;
        bool isLogical = (flags & FlagLogical) != 0;
        at = afterFlags;

        (int dimsType, int dimsSize, int dimsStart, int afterDims) = ReadTag(bytes, at);
        if (dimsType != MiInt32)
        {
            throw new InvalidDataException("Malformed MAT-file: an array's dimensions are missing.");
        }

        int dimCount = dimsSize / 4;
        var dims = new int[dimCount];
        for (int i = 0; i < dimCount; i++)
        {
            dims[i] = BitConverter.ToInt32(bytes, dimsStart + (4 * i));
        }

        at = afterDims;

        (_, int nameSize, int nameStart, int afterName) = ReadTag(bytes, at);
        string name = Encoding.ASCII.GetString(bytes, nameStart, nameSize);
        at = afterName;

        int rows = dims.Length > 0 ? dims[0] : 0;
        int cols = dims.Length > 1 ? dims[1] : 0;
        for (int i = 2; i < dims.Length; i++)
        {
            cols *= dims[i]; // N-D folds into columns, the closest 2-D reading
        }

        JgsValue value = arrayClass switch
        {
            MxChar => ReadChar(bytes, ref at, rows * cols),
            MxCell => ReadCellArray(bytes, ref at, end, rows * cols),
            MxStruct => ReadStruct(bytes, ref at, end, rows * cols),
            MxDouble or MxSingle or MxInt8 or MxUInt8 or MxInt16 or MxUInt16
                or MxInt32 or MxUInt32 or MxInt64 or MxUInt64 =>
                ReadNumeric(bytes, ref at, rows, cols, isComplex, isLogical),
            _ => throw new InvalidDataException(
                $"MAT-file variable '{name}' uses an unsupported class ({arrayClass}) — objects and sparse matrices cannot be loaded."),
        };

        return (name, value);
    }

    private static JgsValue ReadNumeric(byte[] bytes, ref int at, int rows, int cols, bool isComplex, bool isLogical)
    {
        double[] real = ReadNumericData(bytes, ref at);
        double[]? imaginary = isComplex ? ReadNumericData(bytes, ref at) : null;

        if (rows * cols != real.Length)
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

            return ShapeBoxed(elements, rows, cols);
        }

        if (isLogical)
        {
            var mask = new JgsValue[real.Length];
            for (int i = 0; i < real.Length; i++)
            {
                mask[i] = JgsValue.Bool(real[i] != 0);
            }

            return real.Length == 1 ? mask[0] : ShapeBoxed(mask, rows, cols);
        }

        if (real.Length == 1)
        {
            return JgsValue.Number(real[0]);
        }

        // Column-major on disk is exactly how a shaped value stores itself, so this is a straight
        // adoption rather than a transpose (ADR 0043).
        return JgsMatrix.FromColumnMajor(real, rows, cols);
    }

    private static JgsValue ShapeBoxed(JgsValue[] columnMajor, int rows, int cols) =>
        columnMajor.Length == 1 ? columnMajor[0] : JgsMatrix.FromElements(columnMajor, rows, cols);

    private static JgsValue ReadChar(byte[] bytes, ref int at, int count)
    {
        (int type, int size, int dataStart, int next) = ReadTag(bytes, at);
        at = next;

        var sb = new StringBuilder(count);
        switch (type)
        {
            case MiUInt16:
                for (int i = 0; i + 1 < size; i += 2)
                {
                    sb.Append((char)BitConverter.ToUInt16(bytes, dataStart + i));
                }

                break;
            case MiUtf8:
                sb.Append(Encoding.UTF8.GetString(bytes, dataStart, size));
                break;
            case MiInt8 or MiUInt8:
                sb.Append(Encoding.ASCII.GetString(bytes, dataStart, size));
                break;
            default:
                throw new InvalidDataException($"MAT-file text uses an unsupported encoding ({type}).");
        }

        return JgsValue.Str(sb.ToString());
    }

    private static JgsValue ReadCellArray(byte[] bytes, ref int at, int end, int count)
    {
        var elements = new List<JgsValue>(Math.Max(count, 0));
        while (at + 8 <= end && elements.Count < count)
        {
            (int type, int size, int dataStart, int next) = ReadTag(bytes, at);
            if (type != MiMatrix)
            {
                throw new InvalidDataException("Malformed MAT-file: a cell element is not a matrix.");
            }

            elements.Add(ReadMatrix(bytes, dataStart, size).Value);
            at = next;
        }

        return JgsValue.Cell(elements.ToArray());
    }

    private static JgsValue ReadStruct(byte[] bytes, ref int at, int end, int count)
    {
        if (count != 1)
        {
            throw new InvalidDataException("Struct arrays with more than one element cannot be loaded.");
        }

        (_, _, int lengthStart, int afterLength) = ReadTag(bytes, at);
        int slot = BitConverter.ToInt32(bytes, lengthStart);
        at = afterLength;

        (_, int namesSize, int namesStart, int afterNames) = ReadTag(bytes, at);
        at = afterNames;
        int fieldCount = slot > 0 ? namesSize / slot : 0;
        var names = new string[fieldCount];
        for (int i = 0; i < fieldCount; i++)
        {
            names[i] = Encoding.ASCII.GetString(bytes, namesStart + (i * slot), slot).TrimEnd('\0');
        }

        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        foreach (string field in names)
        {
            (int type, int size, int dataStart, int next) = ReadTag(bytes, at);
            if (type != MiMatrix)
            {
                throw new InvalidDataException("Malformed MAT-file: a struct field is not a matrix.");
            }

            fields[field] = ReadMatrix(bytes, dataStart, size).Value;
            at = next;
        }

        _ = end;
        return JgsValue.Struct(fields);
    }

    /// <summary>Reads one numeric subelement, widening whatever integer encoding it uses to doubles.</summary>
    private static double[] ReadNumericData(byte[] bytes, ref int at)
    {
        (int type, int size, int dataStart, int next) = ReadTag(bytes, at);
        at = next;

        Func<int, double> read;
        int width;
        switch (type)
        {
            case MiDouble: read = i => BitConverter.ToDouble(bytes, i); width = 8; break;
            case MiSingle: read = i => BitConverter.ToSingle(bytes, i); width = 4; break;
            case MiInt8: read = i => (sbyte)bytes[i]; width = 1; break;
            case MiUInt8: read = i => bytes[i]; width = 1; break;
            case MiInt16: read = i => BitConverter.ToInt16(bytes, i); width = 2; break;
            case MiUInt16: read = i => BitConverter.ToUInt16(bytes, i); width = 2; break;
            case MiInt32: read = i => BitConverter.ToInt32(bytes, i); width = 4; break;
            case MiUInt32: read = i => BitConverter.ToUInt32(bytes, i); width = 4; break;
            case MiInt64: read = i => BitConverter.ToInt64(bytes, i); width = 8; break;
            case MiUInt64: read = i => BitConverter.ToUInt64(bytes, i); width = 8; break;
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
    private static (int Type, int Size, int DataStart, int Next) ReadTag(byte[] bytes, int at)
    {
        int first = BitConverter.ToInt32(bytes, at);
        int small = (first >> 16) & 0xFFFF;
        if (small != 0)
        {
            // Small-element form: the data lives inside the 8-byte tag itself.
            return (first & 0xFFFF, small, at + 4, at + 8);
        }

        int size = BitConverter.ToInt32(bytes, at + 4);
        return (first, size, at + 8, at + 8 + size + Pad(size));
    }

    private static int Pad(int size) => (8 - (size % 8)) % 8;
}
