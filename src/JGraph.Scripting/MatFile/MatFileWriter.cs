using System.IO;
using System.Numerics;
using System.Text;
using JGraph.Scripting.Jgs;
using static JGraph.Scripting.MatFile.MatConstants;

namespace JGraph.Scripting.MatFile;

/// <summary>
/// Writes level-5 MAT-files that real MATLAB opens: doubles (scalars, vectors, matrices), complex
/// values, logicals, char rows, cell arrays, and scalar structs. Elements are written uncompressed —
/// MATLAB reads those unconditionally — in little-endian order, which is the only order this
/// codebase runs in.
/// </summary>
internal static class MatFileWriter
{
    /// <summary>Writes <paramref name="variables"/> to <paramref name="path"/>, in order.</summary>
    /// <exception cref="NotSupportedException">A value has no MAT representation (table, image, function).</exception>
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

    /// <summary>Whether <paramref name="value"/> can be written to a MAT-file at all.</summary>
    public static bool CanWrite(JgsValue value) => value.Type switch
    {
        JgsType.Number or JgsType.Bool or JgsType.Complex or JgsType.String => true,
        JgsType.Array => true,
        JgsType.Cell => value.AsCell.All(CanWrite),
        JgsType.Struct => value.AsStruct.Values.All(CanWrite),
        _ => false,
    };

    private static byte[] MatrixElement(string name, JgsValue value)
    {
        using var buffer = new MemoryStream();
        using var w = new BinaryWriter(buffer);

        switch (value.Type)
        {
            case JgsType.Number:
                WriteNumeric(w, name, new[,] { { value.AsNumber } }, null, logical: false);
                break;
            case JgsType.Bool:
                WriteLogical(w, name, [value.AsNumber != 0]);
                break;
            case JgsType.Complex:
                WriteNumeric(w, name, new[,] { { value.AsComplex.Real } },
                    new[,] { { value.AsComplex.Imaginary } }, logical: false);
                break;
            case JgsType.String:
                WriteChar(w, name, value.AsString);
                break;
            case JgsType.Array:
                WriteArray(w, name, value);
                break;
            case JgsType.Cell:
                WriteCell(w, name, value.AsCell);
                break;
            case JgsType.Struct:
                WriteStruct(w, name, value.AsStruct);
                break;
            default:
                throw new NotSupportedException($"A {value.TypeName} cannot be saved to a MAT-file.");
        }

        w.Flush();
        return buffer.ToArray();
    }

    private static void WriteArray(BinaryWriter w, string name, JgsValue value)
    {
        // Boolean masks become logicals; complex elements a complex array; anything else doubles.
        int count = value.ArrayLength;
        bool isMatrix = !value.IsPacked && count > 0 && value.ElementAt(0).Type == JgsType.Array;

        if (isMatrix)
        {
            int rows = count;
            int cols = value.ElementAt(0).ArrayLength;
            var real = new double[rows, cols];
            for (int r = 0; r < rows; r++)
            {
                JgsValue row = value.ElementAt(r);
                if (row.ArrayLength != cols)
                {
                    throw new NotSupportedException("Only rectangular matrices can be saved.");
                }

                for (int c = 0; c < cols; c++)
                {
                    real[r, c] = row.ElementAt(c).AsNumber;
                }
            }

            WriteNumeric(w, name, real, null, logical: false);
            return;
        }

        bool allBool = !value.IsPacked && count > 0;
        bool anyComplex = false;
        for (int i = 0; i < count && !value.IsPacked; i++)
        {
            JgsType elementType = value.ElementAt(i).Type;
            allBool &= elementType == JgsType.Bool;
            anyComplex |= elementType == JgsType.Complex;
        }

        if (value.IsPacked && value.PackedKind == JgsPackedKind.Bool)
        {
            allBool = true;
        }

        if (allBool)
        {
            var bits = new bool[count];
            for (int i = 0; i < count; i++)
            {
                bits[i] = value.ElementAt(i).AsNumber != 0;
            }

            WriteLogical(w, name, bits);
            return;
        }

        var re = new double[1, count];
        double[,]? im = null;
        for (int i = 0; i < count; i++)
        {
            JgsValue element = value.ElementAt(i);
            if (element.Type == JgsType.Complex)
            {
                Complex c = element.AsComplex;
                re[0, i] = c.Real;
                im ??= new double[1, count];
                im[0, i] = c.Imaginary;
            }
            else
            {
                re[0, i] = element.AsNumber;
            }
        }

        WriteNumeric(w, name, re, anyComplex ? im ?? new double[1, count] : null, logical: false);
    }

    private static void WriteNumeric(BinaryWriter w, string name, double[,] real, double[,]? imaginary, bool logical)
    {
        int flags = MxDouble | (imaginary is not null ? FlagComplex : 0) | (logical ? FlagLogical : 0);
        WriteFlags(w, flags);
        WriteDimensions(w, real.GetLength(0), real.GetLength(1));
        WriteName(w, name);
        WriteDoubleData(w, real);
        if (imaginary is not null)
        {
            WriteDoubleData(w, imaginary);
        }
    }

    private static void WriteLogical(BinaryWriter w, string name, bool[] bits)
    {
        WriteFlags(w, MxUInt8 | FlagLogical);
        WriteDimensions(w, 1, bits.Length);
        WriteName(w, name);
        var bytes = new byte[bits.Length];
        for (int i = 0; i < bits.Length; i++)
        {
            bytes[i] = bits[i] ? (byte)1 : (byte)0;
        }

        WriteDataElement(w, MiUInt8, bytes);
    }

    private static void WriteChar(BinaryWriter w, string name, string text)
    {
        WriteFlags(w, MxChar);
        WriteDimensions(w, text.Length == 0 ? 0 : 1, text.Length);
        WriteName(w, name);
        var bytes = new byte[text.Length * 2];
        for (int i = 0; i < text.Length; i++)
        {
            bytes[2 * i] = (byte)(text[i] & 0xFF);
            bytes[(2 * i) + 1] = (byte)(text[i] >> 8);
        }

        WriteDataElement(w, MiUInt16, bytes);
    }

    private static void WriteCell(BinaryWriter w, string name, JgsValue[] elements)
    {
        WriteFlags(w, MxCell);
        WriteDimensions(w, elements.Length == 0 ? 0 : 1, elements.Length);
        WriteName(w, name);
        foreach (JgsValue element in elements)
        {
            byte[] nested = MatrixElement(string.Empty, element);
            w.Write(MiMatrix);
            w.Write(nested.Length);
            w.Write(nested);
        }
    }

    private static void WriteStruct(BinaryWriter w, string name, IReadOnlyDictionary<string, JgsValue> fields)
    {
        WriteFlags(w, MxStruct);
        WriteDimensions(w, 1, 1);
        WriteName(w, name);

        // Field name length (a small element), then the names in fixed 32-byte slots.
        w.Write((FieldNameLength << 16) | MiInt32);
        w.Write(FieldNameLength);

        var names = fields.Keys.ToArray();
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

        foreach (string field in names)
        {
            byte[] nested = MatrixElement(string.Empty, fields[field]);
            w.Write(MiMatrix);
            w.Write(nested.Length);
            w.Write(nested);
        }
    }

    // --- Subelement plumbing ---------------------------------------------------------------------

    private static void WriteFlags(BinaryWriter w, int flags)
    {
        w.Write(MiUInt32);
        w.Write(8);
        w.Write(flags);
        w.Write(0);
    }

    private static void WriteDimensions(BinaryWriter w, int rows, int cols)
    {
        w.Write(MiInt32);
        w.Write(8);
        w.Write(rows);
        w.Write(cols);
    }

    private static void WriteName(BinaryWriter w, string name) =>
        WriteDataElement(w, MiInt8, Encoding.ASCII.GetBytes(name));

    /// <summary>MATLAB stores matrices down the columns; ours read across the rows.</summary>
    private static void WriteDoubleData(BinaryWriter w, double[,] values)
    {
        int rows = values.GetLength(0);
        int cols = values.GetLength(1);
        w.Write(MiDouble);
        w.Write(rows * cols * 8);
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                w.Write(values[r, c]);
            }
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
