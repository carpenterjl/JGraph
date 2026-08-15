using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace JGraph.Scripting.MatFile.Hdf5;

/// <summary>
/// One HDF5 datatype: what a stored element is and how to widen it to a double. Only the classes
/// MATLAB writes are understood — integers, floats, fixed strings, the two-member compound that
/// carries a complex number, object references, and variable-length sequences.
/// </summary>
internal sealed class Hdf5Datatype
{
    public const int ClassFixed = 0;
    public const int ClassFloat = 1;
    public const int ClassString = 3;
    public const int ClassCompound = 6;
    public const int ClassReference = 7;
    public const int ClassEnum = 8;
    public const int ClassVariableLength = 9;
    public const int ClassArray = 10;

    /// <summary>The datatype class: <see cref="ClassFixed"/> and friends.</summary>
    public int Class { get; private init; }

    /// <summary>Bytes one element occupies in the file.</summary>
    public int Size { get; private init; }

    public bool LittleEndian { get; private init; } = true;

    public bool Signed { get; private init; }

    /// <summary>Compound members, in declaration order; empty for every other class.</summary>
    public IReadOnlyList<(string Name, int Offset, Hdf5Datatype Type)> Members { get; private init; } = [];

    /// <summary>The element type a variable-length sequence or array holds.</summary>
    public Hdf5Datatype? BaseType { get; private init; }

    /// <summary>Whether elements are numbers this reader can widen to a double.</summary>
    public bool IsNumeric => Class is ClassFixed or ClassFloat or ClassEnum;

    /// <summary>
    /// Parses a datatype message beginning at <paramref name="at"/>, advancing past it.
    /// </summary>
    public static Hdf5Datatype Read(byte[] bytes, ref int at)
    {
        int start = at;
        byte classAndVersion = bytes[at];
        int version = classAndVersion >> 4;
        int typeClass = classAndVersion & 0x0F;
        int bits = bytes[at + 1] | (bytes[at + 2] << 8) | (bytes[at + 3] << 16);
        int size = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + 4));
        at += 8;

        switch (typeClass)
        {
            case ClassFixed:
                at += 4; // Bit offset and precision; MATLAB never stores a partial-width integer.
                return new Hdf5Datatype
                {
                    Class = ClassFixed,
                    Size = size,
                    LittleEndian = (bits & 0x01) == 0,
                    Signed = (bits & 0x08) != 0,
                };

            case ClassFloat:
                at += 12;
                return new Hdf5Datatype
                {
                    Class = ClassFloat,
                    Size = size,
                    LittleEndian = (bits & 0x01) == 0,
                };

            case ClassString:
                return new Hdf5Datatype { Class = ClassString, Size = size };

            case ClassReference:
                return new Hdf5Datatype { Class = ClassReference, Size = size };

            case ClassEnum:
            {
                // The values are named constants this reader has no use for; the base type is what
                // the bytes actually are, so read it and skip the names and values wholesale.
                Hdf5Datatype baseType = Read(bytes, ref at);
                int names = bits & 0xFFFF;
                for (int i = 0; i < names; i++)
                {
                    at = SkipName(bytes, at, version < 3);
                }

                at += names * baseType.Size;
                return new Hdf5Datatype
                {
                    Class = ClassEnum,
                    Size = size,
                    LittleEndian = baseType.LittleEndian,
                    Signed = baseType.Signed,
                };
            }

            case ClassVariableLength:
            {
                Hdf5Datatype baseType = Read(bytes, ref at);
                return new Hdf5Datatype { Class = ClassVariableLength, Size = size, BaseType = baseType };
            }

            case ClassArray:
            {
                int rank = bytes[at];
                at += version < 3 ? 4 : 1;
                at += rank * 4;
                if (version < 3)
                {
                    at += rank * 4; // Permutation indices, unused since HDF5 1.4.
                }

                Hdf5Datatype baseType = Read(bytes, ref at);
                return new Hdf5Datatype { Class = ClassArray, Size = size, BaseType = baseType };
            }

            case ClassCompound:
            {
                int count = bits & 0xFFFF;
                var members = new List<(string, int, Hdf5Datatype)>(count);
                for (int i = 0; i < count; i++)
                {
                    int nameStart = at;
                    at = SkipName(bytes, at, version < 3);
                    string name = Encoding.ASCII.GetString(
                        bytes, nameStart, NameLength(bytes, nameStart));

                    int offset;
                    if (version < 3)
                    {
                        offset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at));
                        at += 4;
                        if (version == 1)
                        {
                            at += 28; // Dimensionality, permutation and four dimension sizes.
                        }
                    }
                    else
                    {
                        // Version 3 packs the offset into as few bytes as the member size needs.
                        int width = OffsetWidth(size);
                        offset = 0;
                        for (int b = 0; b < width; b++)
                        {
                            offset |= bytes[at + b] << (8 * b);
                        }

                        at += width;
                    }

                    members.Add((name, offset, Read(bytes, ref at)));
                }

                return new Hdf5Datatype { Class = ClassCompound, Size = size, Members = members };
            }

            default:
                throw new InvalidDataException(
                    $"This version 7.3 MAT-file uses an HDF5 datatype ({typeClass}) that cannot be read.");
        }

        static int NameLength(byte[] bytes, int at)
        {
            int end = at;
            while (end < bytes.Length && bytes[end] != 0)
            {
                end++;
            }

            return end - at;
        }

        static int SkipName(byte[] bytes, int at, bool padded)
        {
            int next = at + NameLength(bytes, at) + 1;
            return padded ? at + Align8(next - at) : next;
        }
    }

    /// <summary>Reads the element at byte <paramref name="at"/> as a double.</summary>
    public double ElementAt(byte[] data, int at)
    {
        if (Class == ClassFloat)
        {
            return Size switch
            {
                4 => BitConverter.Int32BitsToSingle(ReadInt32(data, at)),
                8 => BitConverter.Int64BitsToDouble(ReadInt64(data, at)),
                _ => throw new InvalidDataException(
                    $"This version 7.3 MAT-file stores {Size}-byte floating point numbers, which cannot be read."),
            };
        }

        long raw = Size switch
        {
            1 => data[at],
            2 => (ushort)ReadInt16(data, at),
            4 => (uint)ReadInt32(data, at),
            8 => ReadInt64(data, at),
            _ => throw new InvalidDataException(
                $"This version 7.3 MAT-file stores {Size}-byte integers, which cannot be read."),
        };

        if (!Signed)
        {
            // An unsigned 64-bit value above long.MaxValue only survives as a double anyway.
            return Size == 8 ? (ulong)raw : raw;
        }

        return Size switch
        {
            1 => (sbyte)raw,
            2 => (short)raw,
            4 => (int)raw,
            _ => raw,
        };
    }

    /// <summary>Reads element <paramref name="at"/> as the unsigned integer a character or address is.</summary>
    public ulong UnsignedAt(byte[] data, int at) => Size switch
    {
        1 => data[at],
        2 => (ushort)ReadInt16(data, at),
        4 => (uint)ReadInt32(data, at),
        8 => (ulong)ReadInt64(data, at),
        _ => throw new InvalidDataException(
            $"This version 7.3 MAT-file stores {Size}-byte integers, which cannot be read."),
    };

    private short ReadInt16(byte[] data, int at) => LittleEndian
        ? BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(at))
        : BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(at));

    private int ReadInt32(byte[] data, int at) => LittleEndian
        ? BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at))
        : BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(at));

    private long ReadInt64(byte[] data, int at) => LittleEndian
        ? BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(at))
        : BinaryPrimitives.ReadInt64BigEndian(data.AsSpan(at));

    private static int OffsetWidth(int size) => size switch
    {
        <= 0xFF => 1,
        <= 0xFFFF => 2,
        <= 0xFFFFFF => 3,
        _ => 4,
    };

    private static int Align8(int length) => (length + 7) & ~7;
}
