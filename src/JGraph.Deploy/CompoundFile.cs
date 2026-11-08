using System.Buffers.Binary;
using System.Text;

namespace JGraph.Deploy;

/// <summary>A storage of a compound file: its name, the storages under it and its streams.</summary>
public sealed class CompoundFileStorage
{
    /// <summary>The storage's name; the root's is <c>Root Entry</c>.</summary>
    public required string Name { get; init; }

    /// <summary>The storages under this one.</summary>
    public List<CompoundFileStorage> Storages { get; } = [];

    /// <summary>The streams under this one, each with its bytes.</summary>
    public List<(string Name, byte[] Data)> Streams { get; } = [];

    /// <summary>The storage named <paramref name="name"/> under this one, or null.</summary>
    public CompoundFileStorage? Storage(string name) => Storages.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The bytes of the stream named <paramref name="name"/> under this one, or null.</summary>
    public byte[]? Stream(string name) => Streams.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)).Data;
}

/// <summary>
/// A compound file (MS-CFB, version 3: 512-byte sectors, 64-byte mini sectors, streams under 4,096
/// bytes in the mini stream) written and read without ole32, so an Excel add-in workbook's
/// <c>vbaProject.bin</c> (compiler and SDK plan, stage K17e, decision KO12) is written the same on any
/// machine and read back by the tests. The directory is a balanced binary tree over each storage's
/// children in the file's own order (by name length, then case-insensitively), every entry black;
/// no times are written.
/// </summary>
public static class CompoundFile
{
    private const int SectorSize = 512;
    private const int MiniSectorSize = 64;
    private const int MiniCutoff = 4096;
    private const uint FreeSector = 0xFFFFFFFF;
    private const uint EndOfChain = 0xFFFFFFFE;
    private const uint FatSector = 0xFFFFFFFD;
    private const uint DifatSector = 0xFFFFFFFC;
    private const uint NoStream = 0xFFFFFFFF;
    private const byte TypeStorage = 1;
    private const byte TypeStream = 2;
    private const byte TypeRoot = 5;
    private static readonly byte[] Signature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private sealed class Entry
    {
        public required string Name;
        public byte Type;
        public byte[] Data = [];
        public List<Entry> Children = [];
        public uint Id;
        public uint Left = NoStream;
        public uint Right = NoStream;
        public uint Child = NoStream;
        public uint Start = EndOfChain;
        public long Size;
    }

    /// <summary>Writes <paramref name="root"/> and everything under it as a compound file.</summary>
    public static byte[] Write(CompoundFileStorage root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var entries = new List<Entry>();
        Entry rootEntry = Flatten(root, entries, isRoot: true);
        foreach (Entry e in entries)
        {
            if (e.Children.Count > 0)
            {
                e.Child = Tree(e.Children);
            }
        }

        // The mini stream holds every stream under the cutoff; its FAT is one chain per stream.
        var miniStream = new MemoryStream();
        var miniFat = new List<uint>();
        var big = new List<Entry>();
        foreach (Entry e in entries.Where(static e => e.Type == TypeStream))
        {
            e.Size = e.Data.Length;
            if (e.Size == 0)
            {
                e.Start = EndOfChain;
            }
            else if (e.Size < MiniCutoff)
            {
                uint first = (uint)(miniStream.Length / MiniSectorSize);
                int count = (int)((e.Size + MiniSectorSize - 1) / MiniSectorSize);
                miniStream.Write(e.Data);
                miniStream.Write(new byte[count * MiniSectorSize - e.Size]);
                for (int k = 0; k < count; k++)
                {
                    miniFat.Add(k + 1 < count ? first + (uint)k + 1 : EndOfChain);
                }

                e.Start = first;
            }
            else
            {
                big.Add(e);
            }
        }

        // Sectors: the directory, the mini FAT, the mini stream, the large streams, then the FAT.
        var sectors = new List<byte[]>();
        var fat = new List<uint>();
        uint AddChain(ReadOnlySpan<byte> data)
        {
            int count = (data.Length + SectorSize - 1) / SectorSize;
            if (count == 0)
            {
                return EndOfChain;
            }

            uint first = (uint)sectors.Count;
            for (int k = 0; k < count; k++)
            {
                var sector = new byte[SectorSize];
                ReadOnlySpan<byte> part = data.Slice(k * SectorSize, Math.Min(SectorSize, data.Length - k * SectorSize));
                part.CopyTo(sector);
                sectors.Add(sector);
                fat.Add(k + 1 < count ? first + (uint)k + 1 : EndOfChain);
            }

            return first;
        }

        int directorySectors = Math.Max(1, (entries.Count * 128 + SectorSize - 1) / SectorSize);
        uint directoryStart = AddChain(new byte[directorySectors * SectorSize]);
        byte[] miniFatBytes = Pack(miniFat);
        uint miniFatStart = AddChain(miniFatBytes);
        int miniFatSectors = (miniFatBytes.Length + SectorSize - 1) / SectorSize;
        rootEntry.Start = AddChain(miniStream.ToArray());
        rootEntry.Size = miniStream.Length;
        foreach (Entry e in big)
        {
            e.Start = AddChain(e.Data);
        }

        int fatSectors = 0;
        while (true)
        {
            int needed = (fat.Count + fatSectors + 127) / 128;
            if (needed == fatSectors)
            {
                break;
            }

            fatSectors = needed;
        }

        if (fatSectors > 109)
        {
            throw new InvalidOperationException("The compound file is larger than this writer's header DIFAT holds.");
        }

        uint fatStart = (uint)sectors.Count;
        for (int k = 0; k < fatSectors; k++)
        {
            fat.Add(FatSector);
        }

        while (fat.Count % 128 != 0)
        {
            fat.Add(FreeSector);
        }

        byte[] fatBytes = Pack(fat);
        for (int k = 0; k < fatSectors; k++)
        {
            sectors.Add(fatBytes[(k * SectorSize)..((k + 1) * SectorSize)]);
        }

        // The directory, now that every start sector is known.
        var directory = new byte[directorySectors * SectorSize];
        for (int i = 0; i < directorySectors * SectorSize / 128; i++)
        {
            Span<byte> record = directory.AsSpan(i * 128, 128);
            if (i < entries.Count)
            {
                Entry e = entries[i];
                byte[] name = Encoding.Unicode.GetBytes(e.Name + "\0");
                name.CopyTo(record);
                BinaryPrimitives.WriteUInt16LittleEndian(record[64..], (ushort)name.Length);
                record[66] = e.Type;
                record[67] = 1;
                BinaryPrimitives.WriteUInt32LittleEndian(record[68..], e.Left);
                BinaryPrimitives.WriteUInt32LittleEndian(record[72..], e.Right);
                BinaryPrimitives.WriteUInt32LittleEndian(record[76..], e.Child);
                bool hasData = e.Type is TypeStream or TypeRoot;
                BinaryPrimitives.WriteUInt32LittleEndian(record[116..], hasData ? e.Start : 0);
                BinaryPrimitives.WriteUInt64LittleEndian(record[120..], hasData ? (ulong)e.Size : 0);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(record[68..], NoStream);
                BinaryPrimitives.WriteUInt32LittleEndian(record[72..], NoStream);
                BinaryPrimitives.WriteUInt32LittleEndian(record[76..], NoStream);
            }
        }

        for (int k = 0; k < directorySectors; k++)
        {
            sectors[(int)directoryStart + k] = directory[(k * SectorSize)..((k + 1) * SectorSize)];
        }

        var header = new byte[SectorSize];
        Signature.CopyTo(header, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(24), 0x003E);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(26), 0x0003);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(28), 0xFFFE);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(30), 9);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(32), 6);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(44), (uint)fatSectors);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(48), directoryStart);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(56), MiniCutoff);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(60), miniFatSectors > 0 ? miniFatStart : EndOfChain);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(64), (uint)miniFatSectors);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(68), EndOfChain);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(72), 0);
        for (int k = 0; k < 109; k++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(76 + 4 * k), k < fatSectors ? fatStart + (uint)k : FreeSector);
        }

        var file = new MemoryStream();
        file.Write(header);
        foreach (byte[] sector in sectors)
        {
            file.Write(sector);
        }

        return file.ToArray();
    }

    /// <summary>Reads a compound file back into its root storage.</summary>
    /// <exception cref="InvalidDataException">The bytes are not a compound file this reader takes.</exception>
    public static CompoundFileStorage Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length < SectorSize || !bytes.AsSpan(0, 8).SequenceEqual(Signature))
        {
            throw new InvalidDataException("Not a compound file.");
        }

        int sectorShift = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(30));
        int miniShift = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(32));
        int sectorSize = 1 << sectorShift;
        int miniSize = 1 << miniShift;
        uint fatCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(44));
        uint directoryStart = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(48));
        uint miniCutoff = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(56));
        uint miniFatStart = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(60));
        uint difatStart = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(68));
        ReadOnlySpan<byte> Sector(uint number)
        {
            long at = (long)(number + 1) * sectorSize;
            if (at + sectorSize > bytes.Length)
            {
                throw new InvalidDataException($"Sector {number} is past the end of the compound file.");
            }

            return bytes.AsSpan((int)at, sectorSize);
        }

        // The FAT through the header's DIFAT, then the DIFAT sectors.
        var fatSectors = new List<uint>();
        for (int k = 0; k < 109 && fatSectors.Count < fatCount; k++)
        {
            fatSectors.Add(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(76 + 4 * k)));
        }

        uint difat = difatStart;
        while (difat != EndOfChain && difat != FreeSector && fatSectors.Count < fatCount)
        {
            ReadOnlySpan<byte> sector = Sector(difat);
            for (int k = 0; k < sectorSize / 4 - 1 && fatSectors.Count < fatCount; k++)
            {
                fatSectors.Add(BinaryPrimitives.ReadUInt32LittleEndian(sector[(4 * k)..]));
            }

            difat = BinaryPrimitives.ReadUInt32LittleEndian(sector[(sectorSize - 4)..]);
        }

        var fat = new List<uint>();
        foreach (uint s in fatSectors)
        {
            ReadOnlySpan<byte> sector = Sector(s);
            for (int k = 0; k < sectorSize / 4; k++)
            {
                fat.Add(BinaryPrimitives.ReadUInt32LittleEndian(sector[(4 * k)..]));
            }
        }

        byte[] Chain(uint start, long size)
        {
            var data = new MemoryStream();
            uint at = start;
            int guard = 0;
            while (at != EndOfChain && at != FreeSector && (size < 0 || data.Length < size))
            {
                data.Write(Sector(at));
                at = at < fat.Count ? fat[(int)at] : EndOfChain;
                if (++guard > fat.Count + 1)
                {
                    throw new InvalidDataException("The compound file's FAT chain loops.");
                }
            }

            byte[] all = data.ToArray();
            return size >= 0 && size < all.Length ? all[..(int)size] : all;
        }

        byte[] directory = Chain(directoryStart, -1);
        int count = directory.Length / 128;
        var entries = new Entry[count];
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> record = directory.AsSpan(i * 128, 128);
            int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(record[64..]);
            string name = nameLength >= 2 ? Encoding.Unicode.GetString(record[..(nameLength - 2)]) : string.Empty;
            entries[i] = new Entry
            {
                Name = name,
                Type = record[66],
                Id = (uint)i,
                Left = BinaryPrimitives.ReadUInt32LittleEndian(record[68..]),
                Right = BinaryPrimitives.ReadUInt32LittleEndian(record[72..]),
                Child = BinaryPrimitives.ReadUInt32LittleEndian(record[76..]),
                Start = BinaryPrimitives.ReadUInt32LittleEndian(record[116..]),
                Size = (long)BinaryPrimitives.ReadUInt64LittleEndian(record[120..]),
            };
        }

        if (count == 0 || entries[0].Type != TypeRoot)
        {
            throw new InvalidDataException("The compound file has no root entry.");
        }

        byte[] miniStream = Chain(entries[0].Start, entries[0].Size);
        var miniFat = new List<uint>();
        if (miniFatStart != EndOfChain && miniFatStart != FreeSector)
        {
            byte[] miniFatBytes = Chain(miniFatStart, -1);
            for (int k = 0; k + 4 <= miniFatBytes.Length; k += 4)
            {
                miniFat.Add(BinaryPrimitives.ReadUInt32LittleEndian(miniFatBytes.AsSpan(k)));
            }
        }

        byte[] StreamData(Entry e)
        {
            if (e.Size == 0)
            {
                return [];
            }

            if (e.Size < miniCutoff)
            {
                var data = new MemoryStream();
                uint at = e.Start;
                int guard = 0;
                while (at != EndOfChain && at != FreeSector && data.Length < e.Size)
                {
                    long offset = (long)at * miniSize;
                    if (offset + miniSize > miniStream.Length)
                    {
                        throw new InvalidDataException("A mini sector is past the end of the mini stream.");
                    }

                    data.Write(miniStream.AsSpan((int)offset, miniSize));
                    at = at < miniFat.Count ? miniFat[(int)at] : EndOfChain;
                    if (++guard > miniFat.Count + 1)
                    {
                        throw new InvalidDataException("The compound file's mini FAT chain loops.");
                    }
                }

                return data.ToArray()[..(int)Math.Min(e.Size, data.Length)];
            }

            return Chain(e.Start, e.Size);
        }

        CompoundFileStorage Build(Entry e)
        {
            var storage = new CompoundFileStorage { Name = e.Name };
            var seen = new HashSet<uint>();
            void Visit(uint id)
            {
                if (id == NoStream || id >= count || !seen.Add(id))
                {
                    return;
                }

                Entry child = entries[id];
                Visit(child.Left);
                if (child.Type == TypeStream)
                {
                    storage.Streams.Add((child.Name, StreamData(child)));
                }
                else if (child.Type == TypeStorage)
                {
                    storage.Storages.Add(Build(child));
                }

                Visit(child.Right);
            }

            Visit(e.Child);
            return storage;
        }

        return Build(entries[0]);
    }

    private static Entry Flatten(CompoundFileStorage storage, List<Entry> entries, bool isRoot)
    {
        var e = new Entry { Name = isRoot ? "Root Entry" : storage.Name, Type = isRoot ? TypeRoot : TypeStorage, Id = (uint)entries.Count };
        entries.Add(e);
        foreach (CompoundFileStorage s in storage.Storages)
        {
            e.Children.Add(Flatten(s, entries, isRoot: false));
        }

        foreach ((string name, byte[] data) in storage.Streams)
        {
            var child = new Entry { Name = name, Type = TypeStream, Data = data, Id = (uint)entries.Count };
            entries.Add(child);
            e.Children.Add(child);
        }

        return e;
    }

    /// <summary>The children as a balanced binary tree in the file's order; answers the root child's id.</summary>
    private static uint Tree(List<Entry> children)
    {
        List<Entry> sorted = [.. children.OrderBy(static c => c.Name.Length).ThenBy(static c => c.Name.ToUpperInvariant(), StringComparer.Ordinal)];
        uint Build(int lo, int hi)
        {
            if (lo >= hi)
            {
                return NoStream;
            }

            int mid = (lo + hi) / 2;
            Entry e = sorted[mid];
            e.Left = Build(lo, mid);
            e.Right = Build(mid + 1, hi);
            return e.Id;
        }

        return Build(0, sorted.Count);
    }

    private static byte[] Pack(List<uint> values)
    {
        var bytes = new byte[values.Count * 4];
        for (int k = 0; k < values.Count; k++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4 * k), values[k]);
        }

        return bytes;
    }
}
