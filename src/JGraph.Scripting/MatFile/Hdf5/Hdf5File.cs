using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace JGraph.Scripting.MatFile.Hdf5;

/// <summary>
/// A hand-rolled reader for the slice of HDF5 that MATLAB's version 7.3 MAT-files use: superblock
/// version 0 through 3, object headers version 1 and 2, groups held either as a symbol-table B-tree
/// or as link messages, and data laid out compact, contiguous or in deflated chunks.
/// </summary>
/// <remarks>
/// The format is far larger than this, and the parts left out are left out loudly: anything this
/// does not understand names itself in the error rather than being guessed at, because a
/// mis-assembled chunk would come back as a plausible matrix of wrong numbers.
/// </remarks>
internal sealed class Hdf5File
{
    private static readonly byte[] Signature = [0x89, (byte)'H', (byte)'D', (byte)'F', 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly long _baseAddress;

    private Hdf5File(byte[] bytes, long baseAddress, int offsetSize, int lengthSize, long rootHeader)
    {
        Bytes = bytes;
        _baseAddress = baseAddress;
        OffsetSize = offsetSize;
        LengthSize = lengthSize;
        Root = new Hdf5Object(this, string.Empty, Abs(rootHeader));
    }

    public byte[] Bytes { get; }

    /// <summary>Bytes an address occupies in this file.</summary>
    public int OffsetSize { get; }

    /// <summary>Bytes a length occupies in this file.</summary>
    public int LengthSize { get; }

    public Hdf5Object Root { get; }

    /// <summary>Whether <paramref name="bytes"/> holds an HDF5 file at one of its legal offsets.</summary>
    public static bool Looks(byte[] bytes) => FindSignature(bytes) >= 0;

    /// <summary>Opens the file, reading only its superblock; everything else is read on demand.</summary>
    public static Hdf5File Open(byte[] bytes)
    {
        int at = FindSignature(bytes);
        if (at < 0)
        {
            throw new InvalidDataException("This file claims to be version 7.3 but holds no HDF5 data.");
        }

        int version = bytes[at + 8];
        return version switch
        {
            0 or 1 => OpenOld(bytes, at),
            2 or 3 => OpenNew(bytes, at, version),
            _ => throw new InvalidDataException(
                $"This version 7.3 MAT-file uses HDF5 superblock version {version}, which cannot be read."),
        };
    }

    private static Hdf5File OpenOld(byte[] bytes, int at)
    {
        int offsetSize = bytes[at + 13];
        int lengthSize = bytes[at + 14];
        // Past the two node-count words and the consistency flags; version 1 adds a third count.
        int cursor = at + 24 + (bytes[at + 8] == 1 ? 4 : 0);
        long baseAddress = ReadWord(bytes, cursor, offsetSize);

        // The root group's symbol table entry follows the four addresses of the superblock proper;
        // its second field is the object header this file hangs everything else from.
        int rootEntry = cursor + (4 * offsetSize);
        long rootHeader = ReadWord(bytes, rootEntry + offsetSize, offsetSize);
        return new Hdf5File(bytes, baseAddress, offsetSize, lengthSize, rootHeader);
    }

    private static Hdf5File OpenNew(byte[] bytes, int at, int version)
    {
        int offsetSize = bytes[at + 9];
        int lengthSize = bytes[at + 10];
        int cursor = at + 12;
        long baseAddress = ReadWord(bytes, cursor, offsetSize);
        long rootHeader = ReadWord(bytes, cursor + (3 * offsetSize), offsetSize);
        _ = version;
        return new Hdf5File(bytes, baseAddress, offsetSize, lengthSize, rootHeader);
    }

    /// <summary>
    /// The signature sits at the start of the file or at a power-of-two multiple of 512, because a
    /// writer may reserve a userblock in front of it — which is exactly what MATLAB does, filling it
    /// with the same description text a version 5 file opens with.
    /// </summary>
    private static int FindSignature(byte[] bytes)
    {
        for (long at = 0; at + Signature.Length <= bytes.Length; at = at == 0 ? 512 : at * 2)
        {
            if (bytes.AsSpan((int)at, Signature.Length).SequenceEqual(Signature))
            {
                return (int)at;
            }
        }

        return -1;
    }

    /// <summary>Turns a file address, which is relative to the superblock, into an index into the bytes.</summary>
    public int Abs(long address)
    {
        long absolute = _baseAddress + address;
        if (absolute < 0 || absolute >= Bytes.Length)
        {
            throw new InvalidDataException("Malformed version 7.3 MAT-file: an address points outside the file.");
        }

        return (int)absolute;
    }

    public bool IsUndefined(long address) => address == -1 || address == (1L << (8 * OffsetSize)) - 1;

    public long ReadOffset(ref int at)
    {
        long value = ReadWord(Bytes, at, OffsetSize);
        at += OffsetSize;
        return value;
    }

    public long ReadLength(ref int at)
    {
        long value = ReadWord(Bytes, at, LengthSize);
        at += LengthSize;
        return value;
    }

    public static long ReadWord(byte[] bytes, int at, int size)
    {
        long value = 0;
        bool allOnes = true;
        for (int i = size - 1; i >= 0; i--)
        {
            byte b = bytes[at + i];
            allOnes &= b == 0xFF;
            value = (value << 8) | b;
        }

        return allOnes ? -1 : value;
    }

    public ushort U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(at));

    public int I32(int at) => BinaryPrimitives.ReadInt32LittleEndian(Bytes.AsSpan(at));

    public string Ascii(int at, int length) => Encoding.ASCII.GetString(Bytes, at, length);

    /// <summary>Walks a symbol-table B-tree, yielding every (name, object header address) pair under it.</summary>
    public void WalkGroupTree(long treeAddress, long heapAddress, List<(string Name, int Header)> into)
    {
        int heap = Abs(heapAddress);
        if (Ascii(heap, 4) != "HEAP")
        {
            throw new InvalidDataException("Malformed version 7.3 MAT-file: a group's name heap is missing.");
        }

        int cursor = heap + 8;
        _ = ReadLength(ref cursor);
        _ = ReadLength(ref cursor);
        int names = Abs(ReadOffset(ref cursor));
        WalkNode(Abs(treeAddress), names, into);
    }

    private void WalkNode(int node, int names, List<(string, int)> into)
    {
        if (Ascii(node, 4) != "TREE")
        {
            throw new InvalidDataException("Malformed version 7.3 MAT-file: a group index is missing.");
        }

        int level = Bytes[node + 5];
        int used = U16(node + 6);
        int at = node + 8 + (2 * OffsetSize);
        for (int i = 0; i < used; i++)
        {
            at += LengthSize; // The key is an offset into the name heap, which the child repeats.
            int child = Abs(ReadOffset(ref at));
            if (level > 0)
            {
                WalkNode(child, names, into);
            }
            else
            {
                ReadSymbolTableNode(child, names, into);
            }
        }
    }

    private void ReadSymbolTableNode(int node, int names, List<(string, int)> into)
    {
        if (Ascii(node, 4) != "SNOD")
        {
            throw new InvalidDataException("Malformed version 7.3 MAT-file: a group's entries are missing.");
        }

        int count = U16(node + 6);
        int at = node + 8;
        for (int i = 0; i < count; i++)
        {
            long nameOffset = ReadOffset(ref at);
            long header = ReadOffset(ref at);
            int cacheType = I32(at);
            at += 8 + 16; // Cache type, reserved, and the scratch pad this reader has no use for.
            if (cacheType == 2)
            {
                // A symbolic link, which MATLAB never writes and which has no object of its own.
                continue;
            }

            into.Add((NullTerminated(names + (int)nameOffset), Abs(header)));
        }
    }

    public string NullTerminated(int at)
    {
        int end = at;
        while (end < Bytes.Length && Bytes[end] != 0)
        {
            end++;
        }

        return Encoding.UTF8.GetString(Bytes, at, end - at);
    }
}
