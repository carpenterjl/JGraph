using System.Buffers.Binary;
using System.Text;

namespace JGraph.Scripting.MatFile.Hdf5;

/// <summary>
/// One attribute hanging off an object. MATLAB uses these to say what a dataset means — its class,
/// whether it is complex or empty, and which fields a struct has — so reading them is what turns
/// HDF5 bytes back into MATLAB values.
/// </summary>
internal sealed class Hdf5Attribute
{
    private readonly byte[] _data;
    private readonly Func<long, int, int, int, byte[]> _readHeap;

    public Hdf5Attribute(
        Hdf5Datatype datatype, long[] dims, byte[] data, Func<long, int, int, int, byte[]> readHeap)
    {
        Datatype = datatype;
        Dims = dims;
        _data = data;
        _readHeap = readHeap;
    }

    public Hdf5Datatype Datatype { get; }

    public IReadOnlyList<long> Dims { get; }

    public int Count
    {
        get
        {
            long total = 1;
            foreach (long dim in Dims)
            {
                total *= dim;
            }

            return (int)Math.Min(total, _data.Length / Math.Max(Datatype.Size, 1));
        }
    }

    /// <summary>The attribute read as text, however it was stored.</summary>
    public string AsText() => Datatype.Class == Hdf5Datatype.ClassVariableLength
        ? Texts().FirstOrDefault() ?? string.Empty
        : Encoding.ASCII.GetString(_data).TrimEnd('\0');

    /// <summary>The attribute's first element as a number, or zero if it holds none.</summary>
    public double AsNumber() =>
        Datatype.IsNumeric && _data.Length >= Datatype.Size ? Datatype.ElementAt(_data, 0) : 0;

    /// <summary>
    /// Each element read as text. MATLAB stores a struct's field names this way: an array of
    /// variable-length character sequences, one per field, each living in the file's global heap.
    /// </summary>
    public IReadOnlyList<string> Texts()
    {
        if (Datatype.Class != Hdf5Datatype.ClassVariableLength)
        {
            return [Encoding.ASCII.GetString(_data).TrimEnd('\0')];
        }

        int itemSize = Datatype.BaseType?.Size ?? 1;
        var texts = new List<string>(Count);
        for (int i = 0; i < Count; i++)
        {
            int at = i * Datatype.Size;
            int length = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(at));
            long address = Hdf5File.ReadWord(_data, at + 4, Datatype.Size - 8);
            int index = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(at + Datatype.Size - 4));
            byte[] payload = length == 0 ? [] : _readHeap(address, index, length, itemSize);
            texts.Add(Encoding.UTF8.GetString(payload).TrimEnd('\0'));
        }

        return texts;
    }

    /// <summary>The object addresses this attribute holds, when it is a list of references.</summary>
    public IReadOnlyList<long> References()
    {
        var addresses = new List<long>(Count);
        for (int i = 0; i < Count; i++)
        {
            addresses.Add(Hdf5File.ReadWord(_data, i * Datatype.Size, Datatype.Size));
        }

        return addresses;
    }
}
