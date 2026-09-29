using System.Buffers.Binary;
using System.Text;

namespace JGraph.Scripting.Jgs.Devices;

/// <summary>The precisions a transport client reads and writes (GenericClient.PrecisionOptions).</summary>
internal enum Precision
{
    UInt8,
    Int8,
    UInt16,
    Int16,
    UInt32,
    Int32,
    UInt64,
    Int64,
    Single,
    Double,
    Char,
    String,
}

/// <summary>
/// Bytes to values and values to bytes, as R2025b's AsyncIO transport converts them (probe_sp_io):
/// a numeric value is cast to the precision as MATLAB casts — rounded half away from zero, saturated,
/// NaN to 0, the real part of a complex — and its bytes put in the object's byte order; a value written
/// as <c>char</c> or <c>string</c> is truncated toward zero and saturated to 0–255, so a code above 255
/// is written as 255. Bytes read as <c>char</c> are the characters with those codes, one per byte.
/// </summary>
internal static class PrecisionCodec
{
    /// <summary>The names in the order R2025b's refusals list them.</summary>
    public static readonly string[] Names = ["uint8", "int8", "uint16", "int16", "uint32", "int32", "uint64", "int64", "single", "double", "char", "string"];

    public static Precision Parse(string name) => (Precision)Array.IndexOf(Names, name);

    public static string Name(Precision precision) => Names[(int)precision];

    public static int Size(Precision precision) => precision switch
    {
        Precision.UInt16 or Precision.Int16 => 2,
        Precision.UInt32 or Precision.Int32 or Precision.Single => 4,
        Precision.UInt64 or Precision.Int64 or Precision.Double => 8,
        _ => 1,
    };

    public static bool IsText(Precision precision) => precision is Precision.Char or Precision.String;

    /// <summary>Encodes <paramref name="values"/> in <paramref name="precision"/>.</summary>
    public static byte[] Encode(IReadOnlyList<double> values, Precision precision, bool bigEndian)
    {
        int size = Size(precision);
        var bytes = new byte[values.Count * size];
        for (int i = 0; i < values.Count; i++)
        {
            Span<byte> at = bytes.AsSpan(i * size, size);
            double x = values[i];
            switch (precision)
            {
                case Precision.UInt8:
                    at[0] = (byte)Saturate(x, 0, byte.MaxValue);
                    break;
                case Precision.Int8:
                    at[0] = (byte)(sbyte)Saturate(x, sbyte.MinValue, sbyte.MaxValue);
                    break;
                case Precision.Char or Precision.String:
                    at[0] = double.IsNaN(x) ? (byte)0 : (byte)Math.Clamp(Math.Truncate(x), 0, 255);
                    break;
                case Precision.UInt16:
                    Write16(at, (ushort)Saturate(x, 0, ushort.MaxValue), bigEndian);
                    break;
                case Precision.Int16:
                    Write16(at, (ushort)(short)Saturate(x, short.MinValue, short.MaxValue), bigEndian);
                    break;
                case Precision.UInt32:
                    Write32(at, (uint)Saturate(x, 0, uint.MaxValue), bigEndian);
                    break;
                case Precision.Int32:
                    Write32(at, (uint)(int)Saturate(x, int.MinValue, int.MaxValue), bigEndian);
                    break;
                case Precision.UInt64:
                    Write64(at, ToUInt64(x), bigEndian);
                    break;
                case Precision.Int64:
                    Write64(at, (ulong)ToInt64(x), bigEndian);
                    break;
                case Precision.Single:
                    Write32(at, BitConverter.SingleToUInt32Bits((float)x), bigEndian);
                    break;
                case Precision.Double:
                    Write64(at, BitConverter.DoubleToUInt64Bits(x), bigEndian);
                    break;
            }
        }

        return bytes;
    }

    /// <summary>Decodes whole elements of <paramref name="precision"/> from <paramref name="bytes"/> into doubles.</summary>
    public static double[] Decode(ReadOnlySpan<byte> bytes, Precision precision, bool bigEndian)
    {
        int size = Size(precision);
        var values = new double[bytes.Length / size];
        for (int i = 0; i < values.Length; i++)
        {
            ReadOnlySpan<byte> at = bytes.Slice(i * size, size);
            values[i] = precision switch
            {
                Precision.Int8 => (sbyte)at[0],
                Precision.UInt16 => Read16(at, bigEndian),
                Precision.Int16 => (short)Read16(at, bigEndian),
                Precision.UInt32 => Read32(at, bigEndian),
                Precision.Int32 => (int)Read32(at, bigEndian),
                Precision.UInt64 => Read64(at, bigEndian),
                Precision.Int64 => (long)Read64(at, bigEndian),
                Precision.Single => BitConverter.UInt32BitsToSingle(Read32(at, bigEndian)),
                Precision.Double => BitConverter.UInt64BitsToDouble(Read64(at, bigEndian)),
                _ => at[0],
            };
        }

        return values;
    }

    /// <summary>Bytes as the characters with those codes, one per byte (no UTF-8 decoding, as R2025b reads them).</summary>
    public static string Latin1(ReadOnlySpan<byte> bytes) => Encoding.Latin1.GetString(bytes);

    /// <summary>Text as bytes, a code above 255 written as 255.</summary>
    public static byte[] TextBytes(string text)
    {
        var bytes = new byte[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            bytes[i] = text[i] > 255 ? (byte)255 : (byte)text[i];
        }

        return bytes;
    }

    private static double Saturate(double x, double min, double max)
    {
        if (double.IsNaN(x))
        {
            return 0;
        }

        double rounded = Math.Round(x, MidpointRounding.AwayFromZero);
        return Math.Clamp(rounded, min, max);
    }

    private static ulong ToUInt64(double x)
    {
        if (double.IsNaN(x) || x <= 0)
        {
            return 0;
        }

        double rounded = Math.Round(x, MidpointRounding.AwayFromZero);
        return rounded >= 18446744073709551615.0 ? ulong.MaxValue : (ulong)rounded;
    }

    private static long ToInt64(double x)
    {
        if (double.IsNaN(x))
        {
            return 0;
        }

        double rounded = Math.Round(x, MidpointRounding.AwayFromZero);
        return rounded >= 9223372036854775807.0 ? long.MaxValue : rounded <= -9223372036854775808.0 ? long.MinValue : (long)rounded;
    }

    private static void Write16(Span<byte> at, ushort v, bool big)
    {
        if (big)
        {
            BinaryPrimitives.WriteUInt16BigEndian(at, v);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(at, v);
        }
    }

    private static void Write32(Span<byte> at, uint v, bool big)
    {
        if (big)
        {
            BinaryPrimitives.WriteUInt32BigEndian(at, v);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(at, v);
        }
    }

    private static void Write64(Span<byte> at, ulong v, bool big)
    {
        if (big)
        {
            BinaryPrimitives.WriteUInt64BigEndian(at, v);
        }
        else
        {
            BinaryPrimitives.WriteUInt64LittleEndian(at, v);
        }
    }

    private static ushort Read16(ReadOnlySpan<byte> at, bool big) =>
        big ? BinaryPrimitives.ReadUInt16BigEndian(at) : BinaryPrimitives.ReadUInt16LittleEndian(at);

    private static uint Read32(ReadOnlySpan<byte> at, bool big) =>
        big ? BinaryPrimitives.ReadUInt32BigEndian(at) : BinaryPrimitives.ReadUInt32LittleEndian(at);

    private static ulong Read64(ReadOnlySpan<byte> at, bool big) =>
        big ? BinaryPrimitives.ReadUInt64BigEndian(at) : BinaryPrimitives.ReadUInt64LittleEndian(at);
}
