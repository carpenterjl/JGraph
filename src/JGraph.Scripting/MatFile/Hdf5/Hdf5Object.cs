using System.IO;
using System.IO.Compression;
using System.Text;

namespace JGraph.Scripting.MatFile.Hdf5;

/// <summary>
/// One object in an HDF5 file — a group or a dataset, which are the same thing wearing different
/// messages. Its header is parsed on construction; its data is read only when asked for.
/// </summary>
internal sealed class Hdf5Object
{
    private const int MessageDataspace = 0x0001;
    private const int MessageDatatype = 0x0003;
    private const int MessageLink = 0x0006;
    private const int MessageLayout = 0x0008;
    private const int MessageFilters = 0x000B;
    private const int MessageAttribute = 0x000C;
    private const int MessageContinuation = 0x0010;
    private const int MessageSymbolTable = 0x0011;

    private readonly Hdf5File _file;
    private readonly List<(string Name, int Header)> _links = [];
    private readonly Dictionary<string, Hdf5Attribute> _attributes = new(StringComparer.Ordinal);
    private readonly List<Filter> _filters = [];

    private long[] _dims = [];
    private Hdf5Datatype? _datatype;
    private int _layoutClass = -1;
    private long _dataAddress = -1;
    private long _dataSize;
    private int[] _chunkDims = [];
    private byte[]? _compact;

    public Hdf5Object(Hdf5File file, string name, int header)
    {
        _file = file;
        Name = name;
        ReadHeader(header);
    }

    public string Name { get; }

    /// <summary>A group is an object that holds links and no data of its own.</summary>
    public bool IsGroup => _datatype is null;

    /// <summary>The dataset's dimensions, slowest-varying first, as HDF5 counts them.</summary>
    public IReadOnlyList<long> Dims => _dims;

    public Hdf5Datatype Datatype => _datatype
        ?? throw new InvalidDataException("Malformed version 7.3 MAT-file: a dataset has no datatype.");

    public IReadOnlyDictionary<string, Hdf5Attribute> Attributes => _attributes;

    public IEnumerable<string> ChildNames => _links.Select(static link => link.Name);

    public Hdf5Object? Child(string name)
    {
        int at = _links.FindIndex(link => string.Equals(link.Name, name, StringComparison.Ordinal));
        return at < 0 ? null : new Hdf5Object(_file, name, _links[at].Header);
    }

    /// <summary>The object at a raw address, which is how a reference in a cell array points.</summary>
    public Hdf5Object At(long address) => new(_file, string.Empty, _file.Abs(address));

    /// <summary>Total elements across every dimension.</summary>
    public long ElementCount
    {
        get
        {
            long total = 1;
            foreach (long dim in _dims)
            {
                total *= dim;
            }

            return total;
        }
    }

    /// <summary>
    /// Reads the whole dataset, decoded and assembled, as one run of elements in HDF5's own
    /// row-major order.
    /// </summary>
    public byte[] ReadData()
    {
        int elementSize = Datatype.Size;
        long total = ElementCount * elementSize;
        if (total > int.MaxValue)
        {
            throw new InvalidDataException("This version 7.3 MAT-file holds a variable too large to read.");
        }

        var destination = new byte[total];
        switch (_layoutClass)
        {
            case 0:
                (_compact ?? []).AsSpan(0, Math.Min(_compact?.Length ?? 0, destination.Length))
                    .CopyTo(destination);
                return destination;

            case 1:
                if (_file.IsUndefined(_dataAddress))
                {
                    // An address of nothing means the dataset was never written to; zeros are what
                    // HDF5 itself would hand back from the fill value.
                    return destination;
                }

                Array.Copy(_file.Bytes, _file.Abs(_dataAddress),
                    destination, 0, (int)Math.Min(destination.Length, _dataSize == 0 ? destination.Length : _dataSize));
                return destination;

            case 2:
                ReadChunks(destination, elementSize);
                return destination;

            default:
                throw new InvalidDataException(
                    "This version 7.3 MAT-file stores a variable in a layout that cannot be read.");
        }
    }

    private void ReadHeader(int at)
    {
        if (_file.Ascii(at, 4) == "OHDR")
        {
            ReadHeaderVersion2(at);
            return;
        }

        int version = _file.Bytes[at];
        if (version != 1)
        {
            throw new InvalidDataException(
                $"This version 7.3 MAT-file uses HDF5 object header version {version}, which cannot be read.");
        }

        int size = _file.I32(at + 8);
        ReadMessages(at + 16, at + 16 + size, version: 1);
    }

    private void ReadHeaderVersion2(int at)
    {
        byte flags = _file.Bytes[at + 5];
        int cursor = at + 6;
        if ((flags & 0x20) != 0)
        {
            cursor += 16; // Four timestamps.
        }

        if ((flags & 0x10) != 0)
        {
            cursor += 4; // Compact and dense link phase-change limits.
        }

        int sizeWidth = 1 << (flags & 0x03);
        long size = Hdf5File.ReadWord(_file.Bytes, cursor, sizeWidth);
        cursor += sizeWidth;
        ReadMessages(cursor, cursor + (int)size, version: 2, trackOrder: (flags & 0x04) != 0);
    }

    private void ReadMessages(int at, int end, int version, bool trackOrder = false)
    {
        while (at + (version == 1 ? 8 : 4) <= end)
        {
            int type;
            int size;
            int dataStart;
            if (version == 1)
            {
                type = _file.U16(at);
                size = _file.U16(at + 2);
                dataStart = at + 8;
            }
            else
            {
                type = _file.Bytes[at];
                size = _file.U16(at + 1);
                dataStart = at + 4 + (trackOrder ? 2 : 0);
            }

            ReadMessage(type, dataStart, size, version);
            at = dataStart + size;
        }
    }

    private void ReadMessage(int type, int at, int size, int headerVersion)
    {
        switch (type)
        {
            case MessageDataspace:
                _dims = ReadDataspace(at);
                break;

            case MessageDatatype:
            {
                int cursor = at;
                _datatype = Hdf5Datatype.Read(_file.Bytes, ref cursor);
                break;
            }

            case MessageLayout:
                ReadLayout(at);
                break;

            case MessageFilters:
                ReadFilters(at);
                break;

            case MessageAttribute:
                ReadAttribute(at, size);
                break;

            case MessageLink:
                ReadLink(at);
                break;

            case MessageSymbolTable:
            {
                int cursor = at;
                long tree = _file.ReadOffset(ref cursor);
                long heap = _file.ReadOffset(ref cursor);
                _file.WalkGroupTree(tree, heap, _links);
                break;
            }

            case MessageContinuation:
            {
                int cursor = at;
                long address = _file.ReadOffset(ref cursor);
                long length = _file.ReadLength(ref cursor);
                int block = _file.Abs(address);
                bool signed = _file.Ascii(block, 4) == "OCHK";
                ReadMessages(signed ? block + 4 : block, block + (int)length, headerVersion);
                break;
            }

            default:
                // Fill values, group info, reference counts and the rest describe how the file is
                // kept rather than what it holds; skipping them is not a loss of data.
                break;
        }
    }

    private long[] ReadDataspace(int at)
    {
        int version = _file.Bytes[at];
        int rank = _file.Bytes[at + 1];
        int flags = _file.Bytes[at + 2];
        int cursor = at + (version == 1 ? 8 : 4);
        var dims = new long[rank];
        for (int i = 0; i < rank; i++)
        {
            dims[i] = _file.ReadLength(ref cursor);
        }

        _ = flags;
        return dims;
    }

    private void ReadLayout(int at)
    {
        int version = _file.Bytes[at];
        if (version >= 4)
        {
            throw new InvalidDataException(
                "This version 7.3 MAT-file uses HDF5 layout version 4, whose chunk indexes cannot be read.");
        }

        if (version == 3)
        {
            _layoutClass = _file.Bytes[at + 1];
            int cursor = at + 2;
            switch (_layoutClass)
            {
                case 0:
                    int compactSize = _file.U16(cursor);
                    _compact = _file.Bytes.AsSpan(cursor + 2, compactSize).ToArray();
                    break;
                case 1:
                    _dataAddress = _file.ReadOffset(ref cursor);
                    _dataSize = _file.ReadLength(ref cursor);
                    break;
                default:
                    int rankPlusOne = _file.Bytes[cursor];
                    cursor++;
                    _dataAddress = _file.ReadOffset(ref cursor);
                    _chunkDims = new int[rankPlusOne];
                    for (int i = 0; i < rankPlusOne; i++)
                    {
                        _chunkDims[i] = _file.I32(cursor + (4 * i));
                    }

                    break;
            }

            return;
        }

        // Versions 1 and 2 put the dimensions after the address rather than the class.
        int rank = _file.Bytes[at + 1];
        _layoutClass = _file.Bytes[at + 2];
        int walk = at + 8;
        if (_layoutClass != 0)
        {
            _dataAddress = _file.ReadOffset(ref walk);
        }

        var dims = new int[rank];
        for (int i = 0; i < rank; i++)
        {
            dims[i] = _file.I32(walk + (4 * i));
        }

        walk += 4 * rank;
        if (_layoutClass == 2)
        {
            _chunkDims = dims;
        }
        else if (_layoutClass == 0)
        {
            int length = _file.I32(walk);
            _compact = _file.Bytes.AsSpan(walk + 4, length).ToArray();
        }
    }

    private void ReadFilters(int at)
    {
        int version = _file.Bytes[at];
        int count = _file.Bytes[at + 1];
        int cursor = at + (version == 1 ? 8 : 2);
        for (int i = 0; i < count; i++)
        {
            int id = _file.U16(cursor);
            int nameLength = version == 1 || id >= 256 ? _file.U16(cursor + 2) : 0;
            int flags = _file.U16(cursor + (version == 1 || id >= 256 ? 4 : 2));
            int valueCount = _file.U16(cursor + (version == 1 || id >= 256 ? 6 : 4));
            cursor += version == 1 || id >= 256 ? 8 : 6;
            cursor += version == 1 ? Align8(nameLength) : nameLength;

            var values = new int[valueCount];
            for (int v = 0; v < valueCount; v++)
            {
                values[v] = _file.I32(cursor + (4 * v));
            }

            cursor += 4 * valueCount;
            if (version == 1 && valueCount % 2 == 1)
            {
                cursor += 4;
            }

            _filters.Add(new Filter(id, (flags & 0x01) != 0, values));
        }
    }

    private void ReadAttribute(int at, int size)
    {
        int version = _file.Bytes[at];
        int nameLength = _file.U16(at + 2);
        int typeLength = _file.U16(at + 4);
        int spaceLength = _file.U16(at + 6);
        int cursor = at + (version == 1 ? 8 : 8 + (version >= 3 ? 1 : 0));

        string name = _file.Ascii(cursor, Math.Max(nameLength - 1, 0));
        cursor += version == 1 ? Align8(nameLength) : nameLength;

        int typeAt = cursor;
        int scan = typeAt;
        Hdf5Datatype datatype = Hdf5Datatype.Read(_file.Bytes, ref scan);
        cursor += version == 1 ? Align8(typeLength) : typeLength;

        long[] dims = ReadDataspace(cursor);
        cursor += version == 1 ? Align8(spaceLength) : spaceLength;

        long count = 1;
        foreach (long dim in dims)
        {
            count *= dim;
        }

        int length = Math.Min((int)(count * datatype.Size), at + size - cursor);
        byte[] data = length > 0 ? _file.Bytes.AsSpan(cursor, length).ToArray() : [];
        _attributes[name] = new Hdf5Attribute(datatype, dims, data, ReadGlobalHeap);
    }

    private void ReadLink(int at)
    {
        int flags = _file.Bytes[at + 1];
        int cursor = at + 2;
        int linkType = 0;
        if ((flags & 0x08) != 0)
        {
            linkType = _file.Bytes[cursor];
            cursor++;
        }

        if ((flags & 0x04) != 0)
        {
            cursor += 8;
        }

        if ((flags & 0x10) != 0)
        {
            cursor++;
        }

        int lengthWidth = 1 << (flags & 0x03);
        int nameLength = (int)Hdf5File.ReadWord(_file.Bytes, cursor, lengthWidth);
        cursor += lengthWidth;
        string name = Encoding.UTF8.GetString(_file.Bytes, cursor, nameLength);
        cursor += nameLength;

        if (linkType != 0)
        {
            // Soft and external links point at a path rather than an object; MATLAB writes neither.
            return;
        }

        long address = _file.ReadOffset(ref cursor);
        _links.Add((name, _file.Abs(address)));
    }

    /// <summary>Reads one variable-length element's payload out of the global heap it was put in.</summary>
    private byte[] ReadGlobalHeap(long address, int index, int itemLength, int itemSize)
    {
        int collection = _file.Abs(address);
        if (_file.Ascii(collection, 4) != "GCOL")
        {
            throw new InvalidDataException("Malformed version 7.3 MAT-file: a text heap is missing.");
        }

        int cursor = collection + 8;
        int end = collection + (int)Hdf5File.ReadWord(_file.Bytes, collection + 8, _file.LengthSize);
        cursor = collection + 8 + _file.LengthSize;
        while (cursor + 8 <= end)
        {
            int objectIndex = _file.U16(cursor);
            int walk = cursor + 8;
            long objectSize = _file.ReadLength(ref walk);
            if (objectIndex == 0)
            {
                break;
            }

            if (objectIndex == index)
            {
                return _file.Bytes.AsSpan(walk, Math.Min(itemLength * itemSize, (int)objectSize)).ToArray();
            }

            cursor = walk + Align8((int)objectSize);
        }

        throw new InvalidDataException("Malformed version 7.3 MAT-file: a text heap entry is missing.");
    }

    private void ReadChunks(byte[] destination, int elementSize)
    {
        if (_file.IsUndefined(_dataAddress))
        {
            return;
        }

        int rank = _dims.Length;
        var strides = new long[rank];
        long stride = 1;
        for (int i = rank - 1; i >= 0; i--)
        {
            strides[i] = stride;
            stride *= _dims[i];
        }

        foreach (Chunk chunk in Chunks(_file.Abs(_dataAddress), rank))
        {
            byte[] raw = Decode(chunk, elementSize);
            CopyChunk(raw, destination, chunk.Offsets, strides, elementSize);
        }
    }

    /// <summary>Walks the version 1 B-tree that indexes a chunked dataset, leaf nodes only.</summary>
    private IEnumerable<Chunk> Chunks(int node, int rank)
    {
        if (_file.Ascii(node, 4) != "TREE")
        {
            throw new InvalidDataException("Malformed version 7.3 MAT-file: a chunk index is missing.");
        }

        int level = _file.Bytes[node + 5];
        int used = _file.U16(node + 6);
        int keyWidth = 8 + ((rank + 1) * 8);
        int at = node + 8 + (2 * _file.OffsetSize);
        for (int i = 0; i < used; i++)
        {
            int size = _file.I32(at);
            int mask = _file.I32(at + 4);
            var offsets = new long[rank];
            for (int d = 0; d < rank; d++)
            {
                offsets[d] = Hdf5File.ReadWord(_file.Bytes, at + 8 + (8 * d), 8);
            }

            at += keyWidth;
            long child = _file.ReadOffset(ref at);
            if (level > 0)
            {
                foreach (Chunk inner in Chunks(_file.Abs(child), rank))
                {
                    yield return inner;
                }
            }
            else
            {
                yield return new Chunk(_file.Abs(child), size, mask, offsets);
            }
        }
    }

    private byte[] Decode(Chunk chunk, int elementSize)
    {
        byte[] data = _file.Bytes.AsSpan(chunk.Address, chunk.Size).ToArray();
        for (int i = _filters.Count - 1; i >= 0; i--)
        {
            if ((chunk.FilterMask & (1 << i)) != 0)
            {
                continue; // This chunk was written with the filter skipped.
            }

            data = _filters[i].Undo(data, elementSize);
        }

        return data;
    }

    private void CopyChunk(byte[] raw, byte[] destination, long[] offsets, long[] strides, int elementSize)
    {
        int rank = _dims.Length;
        var coordinate = new long[rank];
        long chunkElements = 1;
        for (int i = 0; i < rank; i++)
        {
            chunkElements *= _chunkDims[i];
        }

        for (long e = 0; e < chunkElements; e++)
        {
            bool inside = true;
            long target = 0;
            for (int d = 0; d < rank; d++)
            {
                long position = offsets[d] + coordinate[d];
                if (position >= _dims[d])
                {
                    inside = false;
                    break;
                }

                target += position * strides[d];
            }

            if (inside)
            {
                long from = e * elementSize;
                long to = target * elementSize;
                if (from + elementSize <= raw.Length && to + elementSize <= destination.Length)
                {
                    Array.Copy(raw, from, destination, to, elementSize);
                }
            }

            for (int d = rank - 1; d >= 0; d--)
            {
                if (++coordinate[d] < _chunkDims[d])
                {
                    break;
                }

                coordinate[d] = 0;
            }
        }
    }

    private static int Align8(int length) => (length + 7) & ~7;

    private readonly record struct Chunk(int Address, int Size, int FilterMask, long[] Offsets);

    /// <summary>One entry of a dataset's filter pipeline, run backwards when the data is read.</summary>
    private sealed record Filter(int Id, bool Optional, int[] Values)
    {
        public byte[] Undo(byte[] data, int elementSize) => Id switch
        {
            1 => Inflate(data),
            2 => Unshuffle(data, Values.Length > 0 ? Values[0] : elementSize),
            3 => data.AsSpan(0, Math.Max(data.Length - 4, 0)).ToArray(),
            _ when Optional => data,
            _ => throw new InvalidDataException(
                $"This version 7.3 MAT-file compresses a variable with HDF5 filter {Id}, which cannot be read."),
        };

        private static byte[] Inflate(byte[] data)
        {
            using var source = new MemoryStream(data);
            using var zlib = new ZLibStream(source, CompressionMode.Decompress);
            using var inflated = new MemoryStream();
            zlib.CopyTo(inflated);
            return inflated.ToArray();
        }

        /// <summary>Shuffling groups each byte of every element together; this puts them back.</summary>
        private static byte[] Unshuffle(byte[] data, int elementSize)
        {
            if (elementSize <= 1)
            {
                return data;
            }

            var result = new byte[data.Length];
            int elements = data.Length / elementSize;
            int at = 0;
            for (int b = 0; b < elementSize; b++)
            {
                for (int e = 0; e < elements; e++)
                {
                    result[(e * elementSize) + b] = data[at++];
                }
            }

            // Any tail too short for a whole element is stored unshuffled at the end.
            Array.Copy(data, at, result, elements * elementSize, data.Length - at);
            return result;
        }
    }
}
