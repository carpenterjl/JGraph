using System.Buffers.Binary;
using System.IO;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.MatFile.Hdf5;

namespace JGraph.Scripting.MatFile;

/// <summary>
/// Replaces one root-level variable of an existing version 7.3 MAT-file without touching any other
/// byte of it (open item 57): the new objects are appended past the file's end, in the layout
/// <see cref="MatV73Writer"/> writes, and the root group's entry for the variable is re-pointed at
/// the new one. The old objects stay where they were, unreachable; the file's stored end moves.
/// </summary>
/// <remarks>
/// <para>
/// This is how App Designer's copy of an app's code is rewritten in a model kept as HDF5
/// (<c>JgsMlappModel</c>): the component tree and the legacy object beside it are MCOS objects this
/// build cannot decode, let alone write, and a splice leaves them as they are. Objects a new value
/// refers to (a cell's elements) are appended without a link in <c>#refs#</c>: a reference is an
/// address, and HDF5 opens an object by its address whether or not a group names it.
/// </para>
/// <para>
/// Only R2025b's own shape is spliced - the superblock at 512 after the MAT-file userblock, version
/// 0, eight-byte addresses, an old-style root group - which is what every file R2025b writes has;
/// anything else is refused by <see cref="CanSplice"/> rather than guessed at.
/// </para>
/// </remarks>
internal sealed class MatV73Splice
{
    private const int UserBlock = 512;
    private const int EndOfFileAt = 40;
    private const int EntryHeaderAt = 8;
    private const int EntryCacheTypeAt = 16;
    private const int EntryScratchAt = 24;

    private readonly byte[] _file;
    private readonly Hdf5File _hdf5;
    private readonly MatV73Writer.Builder _builder;
    private readonly long _appendAt;

    /// <exception cref="InvalidDataException">The file is not one this can splice.</exception>
    public MatV73Splice(byte[] file)
    {
        if (!CanSplice(file))
        {
            throw new InvalidDataException("the version 7.3 file is not laid out as R2025b lays one out, so it cannot be spliced");
        }

        _file = file;
        _hdf5 = Hdf5File.Open(file);
        _appendAt = Align8(file.Length);
        _builder = new MatV73Writer.Builder(_appendAt);
    }

    /// <summary>Whether <paramref name="file"/> is a version 7.3 MAT-file in the shape R2025b writes, which is the one spliced.</summary>
    public static bool CanSplice(byte[] file)
    {
        if (file.Length < UserBlock + 96 || !Hdf5File.Looks(file))
        {
            return false;
        }

        try
        {
            Hdf5File hdf5 = Hdf5File.Open(file);
            return hdf5.SuperblockAt == UserBlock
                && file[UserBlock + 8] == 0
                && hdf5.OffsetSize == 8
                && hdf5.LengthSize == 8
                && hdf5.Rel(UserBlock) == 0
                && hdf5.Root.SymbolTable is not null;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>Appends <paramref name="value"/> and answers the address of its object header.</summary>
    public long Value(JgsValue value) => _builder.Value(value);

    /// <summary>Appends a struct array's field: references to objects already in the file, in MATLAB's <paramref name="dims"/>.</summary>
    public long References(IReadOnlyList<long> addresses, int[] dims) => _builder.References(addresses, dims);

    /// <summary>Appends a cell over objects already in the file, in MATLAB's <paramref name="dims"/>.</summary>
    public long Cell(IReadOnlyList<long> addresses, int[] dims) => _builder.References(addresses, dims, cell: true);

    /// <summary>Appends a struct's group over objects already in the file, its fields in the order given; answers the header, with the group's tree and heap for the root entry's cache.</summary>
    public (long Header, long Tree, long Heap) Struct(IReadOnlyList<(string Name, long Header)> fields)
    {
        long header = _builder.StructGroup(fields, out long tree, out long heap);
        return (header, tree, heap);
    }

    /// <summary>
    /// The whole file with the appended objects, the root group's entry for <paramref name="name"/>
    /// pointing at <paramref name="group"/> and the stored end of file moved.
    /// </summary>
    /// <exception cref="InvalidDataException">The root group has no entry of that name.</exception>
    public byte[] Finish(string name, (long Header, long Tree, long Heap) group)
    {
        (long tree, long heap) = _hdf5.Root.SymbolTable!.Value;
        int entry = _hdf5.FindEntry(tree, heap, name);
        if (entry < 0)
        {
            throw new InvalidDataException($"the file's root group has no entry '{name}'");
        }

        byte[] appendix = _builder.Appendix();
        var result = new byte[_appendAt + appendix.Length];
        _file.CopyTo(result, 0);
        appendix.CopyTo(result, (int)_appendAt);

        BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(entry + EntryHeaderAt), group.Header);
        if (BinaryPrimitives.ReadInt32LittleEndian(result.AsSpan(entry + EntryCacheTypeAt)) == 1)
        {
            // The entry caches the group's B-tree and heap; left stale, they would name the old group.
            BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(entry + EntryScratchAt), group.Tree);
            BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(entry + EntryScratchAt + 8), group.Heap);
        }

        // The end of file, which R2025b stores as the file's whole length (probe_h5, ADR 0235).
        BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(UserBlock + EndOfFileAt), result.Length);
        return result;
    }

    private static long Align8(long length) => (length + 7) & ~7L;
}
