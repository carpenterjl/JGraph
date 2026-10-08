using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using JGraph.Scripting.Jgs;

namespace JGraph.Scripting.MatFile;

/// <summary>
/// The objects a MATLAB-written MAT-file keeps in its subsystem (U11, ADR 0210): MATLAB's
/// <c>FileWrapper__</c> cell, decoded far enough to walk an object graph - each object's class and
/// the properties it saved, in order, with references to other objects left as references.
/// <para>
/// The layout was read from R2025b's own files, not from a specification (none is published). The
/// cell's first element is a byte table: a version, the count of names, eight segment offsets, the
/// names themselves, then the segments - the classes (a package name and a class name each), the
/// property lists of objects saved through <c>saveobj</c>, the objects (a class and the property
/// list each uses), and the property lists of the rest. A property list is a count, then a
/// (name, kind, value) triple per property, padded to eight bytes: kind 0 is a name from the
/// table (an enumerated word such as <c>'manual'</c>), 1 an index into the cell (offset by its two
/// leading elements), 2 the value itself. A reference to objects is a <c>uint32</c> column:
/// <c>0xDD000000</c>, the number of dimensions, the dimensions, an object id per element, and the
/// class id.
/// </para>
/// </summary>
internal sealed partial class MatMcos
{
    /// <summary>The first word of every reference to subsystem objects.</summary>
    public const double ReferenceMark = 0xDD000000;

    private readonly IReadOnlyList<JgsValue> _cells;
    private readonly IMatObjectBinder? _binder;
    private readonly string[] _names;
    private readonly (int Package, int Name)[] _classes;
    private readonly (int Class, int Saved, int Plain)[] _objects;
    private readonly List<(int Name, int Kind, int Value)[]> _savedLists;
    private readonly List<(int Name, int Kind, int Value)[]> _plainLists;

    /// <summary>Decodes the <c>FileWrapper__</c> cell <paramref name="cells"/>.</summary>
    /// <exception cref="InvalidDataException">The byte table is not one this reader knows.</exception>
    public MatMcos(IReadOnlyList<JgsValue> cells, IMatObjectBinder? binder)
    {
        _cells = cells;
        _binder = binder;
        if (cells.Count < 2)
        {
            throw new InvalidDataException("Malformed MAT-file subsystem: its object table is missing.");
        }

        byte[] table = Bytes(cells[0]);
        if (table.Length < 40)
        {
            throw new InvalidDataException("Malformed MAT-file subsystem: its object table is too short.");
        }

        int count = U32(table, 4);
        var offsets = new int[8];
        for (int i = 0; i < 8; i++)
        {
            offsets[i] = U32(table, 8 + (4 * i));
        }

        var names = new List<string> { string.Empty };
        int at = 40;
        while (names.Count <= count && at < table.Length)
        {
            int end = Array.IndexOf(table, (byte)0, at);
            if (end < 0)
            {
                break;
            }

            names.Add(Encoding.UTF8.GetString(table, at, end - at));
            at = end + 1;
        }

        _names = [.. names];
        _classes = Records(table, offsets[0], offsets[1], 4).Select(static r => (r[0], r[1])).ToArray();
        _savedLists = PropertyLists(table, offsets[1], offsets[2]);
        _objects = Records(table, offsets[2], offsets[3], 6).Select(static r => (r[0], r[3], r[4])).ToArray();
        _plainLists = PropertyLists(table, offsets[3], offsets[4]);
    }

    /// <summary>How many objects the subsystem holds; ids run from 1.</summary>
    public int Count => _objects.Length - 1;

    /// <summary>The class of object <paramref name="id"/>, package-qualified.</summary>
    public string ClassOf(int id)
    {
        (int package, int name) = _classes[_objects[id].Class];
        return package > 0 ? $"{Name(package)}.{Name(name)}" : Name(name);
    }

    /// <summary>
    /// The properties object <paramref name="id"/> saved, in the order it saved them: a word as a
    /// char row, a stored value as it was read, a literal as a number.
    /// </summary>
    public IReadOnlyList<(string Name, JgsValue Value)> Properties(int id)
    {
        if (id <= 0 || id >= _objects.Length)
        {
            return [];
        }

        (_, int saved, int plain) = _objects[id];
        (int Name, int Kind, int Value)[] list =
            plain > 0 && plain < _plainLists.Count ? _plainLists[plain]
            : saved > 0 && saved < _savedLists.Count ? _savedLists[saved]
            : [];
        var properties = new List<(string, JgsValue)>(list.Length);
        foreach ((int name, int kind, int value) in list)
        {
            JgsValue decoded = kind switch
            {
                0 => JgsValue.Str(Name(value)),
                1 when value + 2 < _cells.Count => _cells[value + 2],
                1 => JgsValue.Array([]),
                _ => JgsValue.Number(value),
            };
            properties.Add((Name(name), decoded));
        }

        return properties;
    }

    /// <summary>
    /// The object ids <paramref name="value"/> refers to, when it is a reference; an empty reference
    /// (a placeholder, an object array of none) answers no ids.
    /// </summary>
    public static bool Refs(JgsValue value, out int[] ids)
    {
        ids = [];
        if (value.Type is not (JgsType.Array or JgsType.Number) || value.NumericClass != JgsNumericClass.UInt32)
        {
            return false;
        }

        double[] words = Words(value);
        if (words.Length < 4 || words[0] != ReferenceMark)
        {
            return false;
        }

        int dims = (int)words[1];
        if (dims < 1 || 2 + dims >= words.Length)
        {
            return false;
        }

        long elements = 1;
        for (int i = 0; i < dims; i++)
        {
            elements *= (long)words[2 + i];
        }

        if (2 + dims + elements >= words.Length)
        {
            return false;
        }

        ids = new int[elements];
        for (int i = 0; i < elements; i++)
        {
            ids[i] = (int)words[2 + dims + i];
        }

        return true;
    }

    /// <summary>
    /// What an opaque element stands for, given to the reader: a function handle's workspace is the
    /// struct of what it captured; anything else is its reference, kept as it was read.
    /// </summary>
    public JgsValue Opaque(string className, JgsValue content)
    {
        if (className == "function_handle_workspace" && Refs(content, out int[] ids) && ids.Length == 1)
        {
            return Workspace(ids[0]);
        }

        return content;
    }

    /// <summary>The variables a function handle captured: its workspace object's <c>any</c> cell's struct.</summary>
    public JgsValue Workspace(int id)
    {
        foreach ((string name, JgsValue value) in Properties(id))
        {
            if (name == "any" && value.Type == JgsType.Cell)
            {
                foreach (JgsValue part in value.AsCell)
                {
                    if (part.Type == JgsType.Struct && !part.IsStructArray)
                    {
                        return part;
                    }
                }
            }
        }

        return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal));
    }

    /// <summary>
    /// A function handle as a subsystem object's property holds one - a two-element cell of the
    /// reference mark and the struct <c>functions</c> reports - re-made by the binder; null when
    /// <paramref name="value"/> is not one, or names something this build cannot re-make (a
    /// handle scoped inside one of MATLAB's own classes).
    /// </summary>
    public JgsValue? Function(JgsValue value)
    {
        if (_binder is not { } binder || value.Type != JgsType.Cell || value.AsCell.Length != 2)
        {
            return null;
        }

        JgsValue mark = value.AsCell[0];
        JgsValue body = value.AsCell[1];
        if (mark.Type != JgsType.Number || mark.AsNumber != ReferenceMark
            || body.Type != JgsType.Struct || body.IsStructArray
            || !body.AsStruct.TryGetValue("function_handle", out JgsValue? described)
            || described.Type != JgsType.Struct || described.IsStructArray)
        {
            return null;
        }

        IReadOnlyDictionary<string, JgsValue> handle = described.AsStruct;
        string type = handle.TryGetValue("type", out JgsValue? kind) && kind.Type == JgsType.String ? kind.AsString : "simple";
        if (!handle.TryGetValue("function", out JgsValue? text) || text.Type != JgsType.String
            || type is not ("anonymous" or "simple"))
        {
            return null;
        }

        IReadOnlyDictionary<string, JgsValue>? captured = null;
        if (handle.TryGetValue("workspace", out JgsValue? workspace))
        {
            JgsValue resolved = Refs(workspace, out int[] ids) && ids.Length == 1 ? Workspace(ids[0]) : workspace;
            captured = resolved.Type == JgsType.Struct && !resolved.IsStructArray ? resolved.AsStruct : null;
        }

        return binder.Function(FunctionText(text.AsString), type, captured, string.Empty);
    }

    /// <summary>
    /// An anonymous function's saved text without the <c>sf%N</c> MATLAB puts in front of it (its
    /// slot among the file's functions); other text is returned as it is.
    /// </summary>
    public static string FunctionText(string saved) => SlotPrefix().Replace(saved, string.Empty, 1);

    [GeneratedRegex(@"^sf%\d+(?=@)")]
    private static partial Regex SlotPrefix();

    private string Name(int index) => index >= 0 && index < _names.Length ? _names[index] : string.Empty;

    private static double[] Words(JgsValue value) =>
        value.Type == JgsType.Number ? [value.AsNumber] : JGraph.Scripting.Jgs.Devices.DeviceChecks.NumberArray(value);

    private static byte[] Bytes(JgsValue value)
    {
        double[] words = Words(value);
        var bytes = new byte[words.Length];
        for (int i = 0; i < words.Length; i++)
        {
            bytes[i] = (byte)words[i];
        }

        return bytes;
    }

    private static int U32(byte[] table, int at) =>
        at + 4 <= table.Length ? (int)BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(at)) : 0;

    /// <summary>Fixed-width records of <paramref name="width"/> words between two offsets; the first is all zeros.</summary>
    private static List<int[]> Records(byte[] table, int from, int to, int width)
    {
        var records = new List<int[]>();
        for (int at = from; at + (4 * width) <= Math.Min(to, table.Length); at += 4 * width)
        {
            var record = new int[width];
            for (int i = 0; i < width; i++)
            {
                record[i] = U32(table, at + (4 * i));
            }

            records.Add(record);
        }

        return records;
    }

    /// <summary>The property lists between two offsets; the first is the empty list at index 0.</summary>
    private static List<(int Name, int Kind, int Value)[]> PropertyLists(byte[] table, int from, int to)
    {
        var lists = new List<(int, int, int)[]>();
        int at = from;
        int end = Math.Min(to, table.Length);
        while (at + 4 <= end)
        {
            int count = U32(table, at);
            at += 4;
            var list = new (int, int, int)[Math.Max(0, Math.Min(count, (end - at) / 12))];
            for (int i = 0; i < list.Length; i++)
            {
                list[i] = (U32(table, at), U32(table, at + 4), U32(table, at + 8));
                at += 12;
            }

            if ((at - from) % 8 != 0)
            {
                at += 4;
            }

            lists.Add(list);
        }

        return lists;
    }
}
