using System.IO;
using System.Numerics;
using System.Text;
using JGraph.Scripting.Jgs;
using static JGraph.Scripting.MatFile.MatConstants;

namespace JGraph.Scripting.MatFile;

/// <summary>
/// Writes level-5 MAT-files that real MATLAB opens: numbers in every integer and floating class,
/// complex values, logicals, char rows and char matrices, N-D shapes, sparse matrices, cell arrays
/// and struct arrays. Elements go out uncompressed — MATLAB reads those unconditionally — in
/// little-endian order, which is the order this codebase runs in; the reader takes either.
/// </summary>
/// <remarks>
/// Version 5 is the only format written, and always will be: v7.3 is HDF5, and a hand-rolled writer
/// for it would risk producing files MATLAB silently mis-reads. Types v5 has no room for — string
/// arrays, datetimes, maps — are refused by name rather than flattened into numbers, which is the
/// failure this wave found and fixed.
/// </remarks>
internal static class MatFileWriter
{
    /// <summary>Writes <paramref name="variables"/> to <paramref name="path"/>, in order.</summary>
    /// <exception cref="NotSupportedException">A value has no MAT representation.</exception>
    public static void Write(string path, IEnumerable<(string Name, JgsValue Value)> variables)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        // 116 text bytes, 8 subsystem-offset bytes, version, endian tag.
        byte[] description = Encoding.ASCII.GetBytes(
            "MATLAB 5.0 MAT-file, Created by JGraph on " +
            DateTime.Now.ToString("ddd MMM dd HH:mm:ss yyyy", System.Globalization.CultureInfo.InvariantCulture));
        var header = new byte[116];
        Array.Fill(header, (byte)' ');
        Array.Copy(description, header, Math.Min(description.Length, header.Length));
        writer.Write(header);
        writer.Write(new byte[8]);
        writer.Write((ushort)0x0100);
        writer.Write((byte)'I');
        writer.Write((byte)'M');

        foreach ((string name, JgsValue value) in variables)
        {
            byte[] matrix = MatrixElement(name, value);
            writer.Write(MiMatrix);
            writer.Write(matrix.Length);
            writer.Write(matrix);
        }
    }

    /// <summary>
    /// Writes <paramref name="variables"/> into an existing file, keeping the names it does not
    /// mention. There is no way to splice one variable into a v5 file in place — the format is a
    /// flat run of elements with no directory — so appending is honestly a read, a merge and a
    /// rewrite, which is also why a name being appended replaces the one already there.
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

    /// <summary>
    /// Why <paramref name="value"/> cannot be written, or null when it can. A reason rather than a
    /// bare no, because "cannot be saved" without the type name sends a script author looking in the
    /// wrong place.
    /// </summary>
    public static string? WhyNotWritable(JgsValue value)
    {
        if (value.IsStringArray)
        {
            return "a string array, which only version 7.3 MAT-files can hold — convert it with cellstr or char";
        }

        if (value.IsDatetime)
        {
            return "a datetime, which a version 5 MAT-file cannot hold — convert it with datenum or string";
        }

        if (value.IsDuration)
        {
            return "a duration, which a version 5 MAT-file cannot hold — convert it with seconds or string";
        }

        // A class name means the value is standing in for an object — a containers.Map, an MException.
        // Version 5 has no object element, and the storage underneath is an implementation detail
        // that would come back as a bare number, so the name is what the refusal should say.
        if (value.ClassName is string className)
        {
            return $"a {className}, which cannot be written to a MAT-file";
        }

        switch (value.Type)
        {
            case JgsType.Number or JgsType.Bool or JgsType.Complex or JgsType.String or JgsType.Sparse:
                return null;
            case JgsType.Array:
                return CharMatrixRows(value) is null && value.ArrayLength > 0 && AllStrings(value)
                    ? "a ragged array of text, which has no MAT-file shape"
                    : null;
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

    private static byte[] MatrixElement(string name, JgsValue value)
    {
        using var buffer = new MemoryStream();
        using var w = new BinaryWriter(buffer);

        switch (value.Type)
        {
            case JgsType.Number:
                WriteNumeric(w, name, [value.AsNumber], null, [1, 1], value.NumericClass);
                break;
            case JgsType.Bool:
                WriteLogical(w, name, [value.AsNumber != 0], [1, 1]);
                break;
            case JgsType.Complex:
                WriteNumeric(w, name, [value.AsComplex.Real], [value.AsComplex.Imaginary], [1, 1],
                    JgsNumericClass.Double);
                break;
            case JgsType.String:
                WriteChar(w, name, [value.AsString]);
                break;
            case JgsType.Array:
                WriteArray(w, name, value);
                break;
            case JgsType.Sparse:
                WriteSparse(w, name, value.AsSparse);
                break;
            case JgsType.Cell:
                WriteCell(w, name, value);
                break;
            case JgsType.Struct:
                WriteStruct(w, name, value);
                break;
            default:
                throw new NotSupportedException($"A {value.TypeName} cannot be saved to a MAT-file.");
        }

        w.Flush();
        return buffer.ToArray();
    }

    private static void WriteArray(BinaryWriter w, string name, JgsValue value)
    {
        // A matrix built as an array of row arrays (the shape older code produces) reports the row
        // count as its length, so it has to be flattened down its columns before anything else looks
        // at it. A flat shaped array already stores itself that way and is left alone, packed buffer
        // and all — the difference shows in whether the element count matches the two dimensions.
        if (JgsMatrix.IsMatrix(value) && value.ArrayLength != JgsMatrix.RowCount(value) * JgsMatrix.ColCount(value))
        {
            int matrixRows = JgsMatrix.RowCount(value);
            int matrixCols = JgsMatrix.ColCount(value);
            var flat = new JgsValue[matrixRows * matrixCols];
            for (int c = 0; c < matrixCols; c++)
            {
                for (int r = 0; r < matrixRows; r++)
                {
                    flat[r + (c * matrixRows)] = JgsMatrix.At(value, r, c);
                }
            }

            JgsValue flattened = JgsValue.Shaped(flat, matrixRows, matrixCols);
            flattened.SetNumericClass(value.NumericClass);
            WriteArray(w, name, flattened);
            return;
        }

        int count = value.ArrayLength;
        int[] dims = value.Dims;

        if (CharMatrixRows(value) is string[] rows)
        {
            WriteChar(w, name, rows);
            return;
        }

        // Asking each element rather than the storage: a packed complex array reads as a buffer of
        // plain numbers when every imaginary part is zero, which is the answer that should be written.
        bool allBool = count > 0 && (!value.IsPacked || value.PackedKind == JgsPackedKind.Bool);
        bool anyComplex = false;
        for (int i = 0; i < count && (allBool || !anyComplex); i++)
        {
            JgsType elementType = value.ElementAt(i).Type;
            allBool &= elementType == JgsType.Bool;
            anyComplex |= elementType == JgsType.Complex;
        }

        if (allBool)
        {
            var bits = new bool[count];
            for (int i = 0; i < count; i++)
            {
                bits[i] = value.ElementAt(i).AsNumber != 0;
            }

            WriteLogical(w, name, bits, dims);
            return;
        }

        var real = new double[count];
        double[]? imaginary = anyComplex ? new double[count] : null;
        for (int i = 0; i < count; i++)
        {
            JgsValue element = value.ElementAt(i);
            if (element.Type == JgsType.Complex)
            {
                Complex c = element.AsComplex;
                real[i] = c.Real;
                imaginary![i] = c.Imaginary;
            }
            else
            {
                real[i] = element.AsNumber;
            }
        }

        // A complex array is always double: MATLAB has no complex integer class to land in.
        WriteNumeric(w, name, real, imaginary, dims,
            anyComplex ? JgsNumericClass.Double : value.NumericClass);
    }

    /// <summary>
    /// The rows of a char matrix — JGraph keeps one as a column of equal-length strings — or null
    /// when <paramref name="value"/> is not text at all or its rows are ragged.
    /// </summary>
    private static string[]? CharMatrixRows(JgsValue value)
    {
        int count = value.ArrayLength;
        if (count == 0 || value.IsPacked || value.IsStringArray || !AllStrings(value))
        {
            return null;
        }

        var rows = new string[count];
        for (int i = 0; i < count; i++)
        {
            rows[i] = value.ElementAt(i).AsString;
        }

        return rows.All(row => row.Length == rows[0].Length) ? rows : null;
    }

    private static bool AllStrings(JgsValue value)
    {
        if (value.IsPacked)
        {
            return false;
        }

        for (int i = 0; i < value.ArrayLength; i++)
        {
            if (value.ElementAt(i).Type != JgsType.String)
            {
                return false;
            }
        }

        return true;
    }

    private static void WriteNumeric(
        BinaryWriter w, string name, double[] real, double[]? imaginary, int[] dims, JgsNumericClass numericClass)
    {
        (int arrayClass, int dataType, _) = EncodingOf(numericClass);
        WriteFlags(w, arrayClass | (imaginary is not null ? FlagComplex : 0));
        WriteDimensions(w, dims);
        WriteName(w, name);
        WriteTypedData(w, real, dataType);
        if (imaginary is not null)
        {
            WriteTypedData(w, imaginary, dataType);
        }
    }

    private static void WriteLogical(BinaryWriter w, string name, bool[] bits, int[] dims)
    {
        WriteFlags(w, MxUInt8 | FlagLogical);
        WriteDimensions(w, dims);
        WriteName(w, name);
        var bytes = new byte[bits.Length];
        for (int i = 0; i < bits.Length; i++)
        {
            bytes[i] = bits[i] ? (byte)1 : (byte)0;
        }

        WriteDataElement(w, MiUInt8, bytes);
    }

    /// <summary>Writes text as a char array; several equal-length rows become a char matrix.</summary>
    private static void WriteChar(BinaryWriter w, string name, string[] rows)
    {
        int width = rows.Length == 0 ? 0 : rows[0].Length;
        WriteFlags(w, MxChar);
        WriteDimensions(w, [width == 0 ? 0 : rows.Length, width]);
        WriteName(w, name);

        // Down the columns, like every other array: row r of column c is the (c*rows + r)th unit.
        var bytes = new byte[rows.Length * width * 2];
        int at = 0;
        for (int c = 0; c < width; c++)
        {
            foreach (string row in rows)
            {
                bytes[at++] = (byte)(row[c] & 0xFF);
                bytes[at++] = (byte)(row[c] >> 8);
            }
        }

        WriteDataElement(w, MiUInt16, bytes);
    }

    private static void WriteSparse(BinaryWriter w, string name, JGraph.Numerics.Sparse.CscMatrix matrix)
    {
        // The array-flags word carries nzmax for a sparse array, where it is unused otherwise.
        w.Write(MiUInt32);
        w.Write(8);
        w.Write(MxSparse);
        w.Write(Math.Max(matrix.NonZeroCount, 1));

        WriteDimensions(w, [matrix.Rows, matrix.Cols]);
        WriteName(w, name);
        WriteInt32Data(w, matrix.RowIndices, matrix.NonZeroCount);
        WriteInt32Data(w, matrix.ColumnStarts, matrix.Cols + 1);
        WriteTypedData(w, matrix.Values.AsSpan(0, matrix.NonZeroCount).ToArray(), MiDouble);
    }

    private static void WriteCell(BinaryWriter w, string name, JgsValue value)
    {
        JgsValue[] elements = value.AsCell;
        WriteFlags(w, MxCell);
        WriteDimensions(w, elements.Length == 0 ? [0, 0] : value.Dims);
        WriteName(w, name);
        foreach (JgsValue element in elements)
        {
            WriteNested(w, element);
        }
    }

    private static void WriteStruct(BinaryWriter w, string name, JgsValue value)
    {
        JgsStructArray payload = value.AsStructArray;
        string[] names = payload.FieldNames;

        WriteFlags(w, MxStruct);
        WriteDimensions(w, payload.Length == 0 ? [0, 0] : value.Dims);
        WriteName(w, name);

        // Field name length (a small element), then the names in fixed 32-byte slots.
        w.Write((FieldNameLength << 16) | MiInt32);
        w.Write(FieldNameLength);

        var slots = new byte[names.Length * FieldNameLength];
        for (int i = 0; i < names.Length; i++)
        {
            byte[] encoded = Encoding.ASCII.GetBytes(names[i]);
            if (encoded.Length >= FieldNameLength)
            {
                throw new NotSupportedException($"Field name '{names[i]}' is too long for a MAT-file (31 characters max).");
            }

            Array.Copy(encoded, 0, slots, i * FieldNameLength, encoded.Length);
        }

        WriteDataElement(w, MiInt8, slots);

        // Element by element, each element's fields consecutively — the order the reader expects.
        foreach (Dictionary<string, JgsValue> element in payload.Elements)
        {
            foreach (string field in names)
            {
                WriteNested(w, element.TryGetValue(field, out JgsValue? stored) && stored is not null
                    ? stored
                    : JgsValue.Array([]));
            }
        }
    }

    private static void WriteNested(BinaryWriter w, JgsValue value)
    {
        byte[] nested = MatrixElement(string.Empty, value);
        w.Write(MiMatrix);
        w.Write(nested.Length);
        w.Write(nested);
    }

    // --- Subelement plumbing ---------------------------------------------------------------------

    /// <summary>The array class and data encoding a numeric class is stored as, so <c>int8</c> stays int8.</summary>
    private static (int ArrayClass, int DataType, int Width) EncodingOf(JgsNumericClass numericClass) => numericClass switch
    {
        JgsNumericClass.Single => (MxSingle, MiSingle, 4),
        JgsNumericClass.Int8 => (MxInt8, MiInt8, 1),
        JgsNumericClass.UInt8 => (MxUInt8, MiUInt8, 1),
        JgsNumericClass.Int16 => (MxInt16, MiInt16, 2),
        JgsNumericClass.UInt16 => (MxUInt16, MiUInt16, 2),
        JgsNumericClass.Int32 => (MxInt32, MiInt32, 4),
        JgsNumericClass.UInt32 => (MxUInt32, MiUInt32, 4),
        JgsNumericClass.Int64 => (MxInt64, MiInt64, 8),
        JgsNumericClass.UInt64 => (MxUInt64, MiUInt64, 8),
        _ => (MxDouble, MiDouble, 8),
    };

    private static void WriteFlags(BinaryWriter w, int flags)
    {
        w.Write(MiUInt32);
        w.Write(8);
        w.Write(flags);
        w.Write(0);
    }

    private static void WriteDimensions(BinaryWriter w, int[] dims)
    {
        w.Write(MiInt32);
        w.Write(dims.Length * 4);
        foreach (int dim in dims)
        {
            w.Write(dim);
        }

        if (dims.Length % 2 != 0)
        {
            w.Write(0); // subelements are padded to eight bytes
        }
    }

    private static void WriteName(BinaryWriter w, string name) =>
        WriteDataElement(w, MiInt8, Encoding.ASCII.GetBytes(name));

    private static void WriteInt32Data(BinaryWriter w, int[] values, int count)
    {
        w.Write(MiInt32);
        w.Write(count * 4);
        for (int i = 0; i < count; i++)
        {
            w.Write(values[i]);
        }

        if (count % 2 != 0)
        {
            w.Write(0);
        }
    }

    /// <summary>Writes the values in the encoding their class calls for; the caller already ordered them by column.</summary>
    private static void WriteTypedData(BinaryWriter w, double[] values, int dataType)
    {
        int width = dataType switch
        {
            MiInt8 or MiUInt8 => 1,
            MiInt16 or MiUInt16 => 2,
            MiSingle or MiInt32 or MiUInt32 => 4,
            _ => 8,
        };

        w.Write(dataType);
        w.Write(values.Length * width);
        foreach (double value in values)
        {
            switch (dataType)
            {
                case MiSingle: w.Write((float)value); break;
                case MiInt8: w.Write((sbyte)value); break;
                case MiUInt8: w.Write((byte)value); break;
                case MiInt16: w.Write((short)value); break;
                case MiUInt16: w.Write((ushort)value); break;
                case MiInt32: w.Write((int)value); break;
                case MiUInt32: w.Write((uint)value); break;
                case MiInt64: w.Write((long)value); break;
                case MiUInt64: w.Write((ulong)value); break;
                default: w.Write(value); break;
            }
        }

        int pad = (8 - (values.Length * width % 8)) % 8;
        for (int i = 0; i < pad; i++)
        {
            w.Write((byte)0);
        }
    }

    private static void WriteDataElement(BinaryWriter w, int type, byte[] data)
    {
        w.Write(type);
        w.Write(data.Length);
        w.Write(data);
        int pad = (8 - (data.Length % 8)) % 8;
        for (int i = 0; i < pad; i++)
        {
            w.Write((byte)0);
        }
    }
}
