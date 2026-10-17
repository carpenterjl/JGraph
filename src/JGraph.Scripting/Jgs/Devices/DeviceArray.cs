using System.Text;

namespace JGraph.Scripting.Jgs.Devices;

/// <summary>
/// A two-dimensional array of device objects of one class, as R2025b's handle-object arrays are
/// (open item 36): what <c>serialportfind</c> answers for several matches, and what the brackets,
/// <c>repmat</c>, <c>cat</c> and a vector of subscripts build. It rides the external-array machinery
/// (<see cref="IJgsExternalArray"/>, stage D10b), so indexing, growth, deletion, loops, comma lists
/// and the rearranging verbs all run through the numeric proxy; its elements are shared handles, so a
/// property written through one element is seen through every holder, and the lifetime count
/// (<see cref="JgsLifetime"/>) holds each element open while any holder keeps the array.
/// </summary>
/// <remarks>
/// A one-element answer is unwrapped to the <see cref="DeviceObject"/> itself
/// (<see cref="ExternalArrays.Value"/>): in MATLAB a scalar object array is the object, and the
/// scalar machinery (methods, callbacks, <c>get</c>/<c>set</c>) runs on <see cref="DeviceObject"/>.
/// The array keeps its <see cref="DeviceClass"/> beside the items, so an empty slice still answers
/// <c>class</c> and <c>isa</c> (probe_dev_arrays2, index_empty and disp_empty).
/// </remarks>
internal sealed class DeviceArray : IJgsExternalArray
{
    public DeviceArray(DeviceClass @class, Interpreter interpreter, IReadOnlyList<DeviceObject> items, int rows, int columns)
    {
        Class = @class;
        Interpreter = interpreter;
        Items = items;
        Rows = rows;
        Columns = columns;
    }

    /// <summary>The row <c>serialportfind</c> answers: 1-by-N.</summary>
    public DeviceArray(IReadOnlyList<DeviceObject> items)
        : this(items[0].Class, items[0].Interpreter, items, 1, items.Count)
    {
    }

    /// <summary>The elements, column-major.</summary>
    public IReadOnlyList<DeviceObject> Items { get; }

    /// <summary>The elements' class, kept beside them so an empty array still has one.</summary>
    public DeviceClass Class { get; }

    /// <summary>The interpreter whose constructors convert into the array and whose lifetimes count it.</summary>
    public Interpreter Interpreter { get; }

    public int Rows { get; }

    public int Columns { get; }

    public string ClassName => Class.Name;

    public bool IsHandle => true;

    public bool IsA(string className) =>
        className == Class.Name || className == "handle" || Class.Superclasses.Contains(className);

    public IJgsExternal CopyForBinding() => this;

    public string Kind => "device object array";

    // --- the array operations (IJgsExternalArray) -------------------------------------------------------

    public IJgsExternalArray Build(IReadOnlyList<(IJgsExternalArray Source, int Index)?> elements, int rows, int columns)
    {
        var items = new DeviceObject[rows * columns];
        for (int i = 0; i < items.Length; i++)
        {
            items[i] = elements[i] is ({ } source, int index) ? ((DeviceArray)source).Items[index] : DefaultElement();
        }

        return new DeviceArray(Class, Interpreter, items, rows, columns);
    }

    /// <summary>
    /// The element a growth fills a gap with: serialport's default object (<c>serialport()</c>, the
    /// saved settings or their absence); the other classes have none, R2025b's
    /// <c>MATLAB:class:MissingGetDefaultScalarElement</c> (probe_dev_arrays2, grow_far and udp_grow).
    /// </summary>
    private DeviceObject DefaultElement()
    {
        if (Class.GapFill is { } maker && Interpreter.Globals.Builtins.TryGet(maker, out JgsValue found) && found.Type == JgsType.Function)
        {
            JgsValue made = found.AsCallable.Call([], 0, 0);
            if (made.AsExternalOrNull() is DeviceObject device)
            {
                return device;
            }
        }

        throw new JgsRuntimeException(0, 0, "MATLAB:class:MissingGetDefaultScalarElement",
            $"During array expansion:\n No default is defined for class '{Class.Name}'.\n Method 'getDefaultScalarElement' in superclass {Class.DefaultElementOwner ?? Class.Name} is missing or incorrectly defined.");
    }

    /// <summary>
    /// A value of another class joined to this one converts through the class's constructor, and the
    /// join quotes the constructor's refusal (probe_dev_arrays, join_number and mixed): a same-class
    /// object or array is simply taken.
    /// </summary>
    public IJgsExternalArray Coerce(JgsValue value, int line, int col)
    {
        if (TakeSame(value) is { } same)
        {
            return same;
        }

        string from = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
        string maker = Class.Constructor ?? Class.Name;
        if (!Interpreter.Globals.Builtins.TryGet(maker, out JgsValue found) || found.Type != JgsType.Function)
        {
            throw Unconvertible(from, $"Undefined function '{maker}' for input arguments of type '{from}'.", line, col);
        }

        JgsValue made;
        try
        {
            made = found.AsCallable.Call([value], line, col);
        }
        catch (JgsRuntimeException refused)
        {
            throw Unconvertible(from, refused.Message, line, col);
        }

        return made.AsExternalOrNull() is DeviceObject device && device.Class == Class
            ? One(device)
            : throw Unconvertible(from, $"Conversion to {Class.Name} from {from} is not possible.", line, col);
    }

    /// <summary>
    /// The same conversion asked by an indexed assignment, which refuses flat, without running the
    /// constructor (probe_dev_arrays2, foreign_write).
    /// </summary>
    public IJgsExternalArray CoerceForStore(JgsValue value, int line, int col) =>
        TakeSame(value) ?? throw new JgsRuntimeException(line, col, "MATLAB:UnableToConvert",
            $"Unable to perform assignment because value of type '{JgsBuiltins.ClassOf(value, JgsDialect.Matlab)}' is not convertible to '{Class.Name}'.");

    private JgsRuntimeException Unconvertible(string from, string why, int line, int col) =>
        new(line, col, "MATLAB:UnableToConvert",
            $"The following error occurred converting from {from} to {Class.Name}:\n{why}");

    private DeviceArray? TakeSame(JgsValue value) => value.AsExternalOrNull() switch
    {
        DeviceObject device when device.Class == Class => One(device),
        DeviceArray array when array.Class == Class => array,
        _ => null,
    };

    private DeviceArray One(DeviceObject device) => new(Class, Interpreter, [device], 1, 1);

    /// <summary>A scalar wrapped as a 1-by-1 array of its class, for the shared array machinery.</summary>
    internal static DeviceArray Over(DeviceObject device) => new(device.Class, device.Interpreter, [device], 1, 1);

    public JgsValue GetMember(int index, string name, int line, int col) =>
        Items[index].Member(name, autoCall: true, wanted: 1, line, col);

    /// <summary>A member write through one element writes the shared handle, so the array is itself the answer.</summary>
    public IJgsExternalArray WithMember(int index, string name, JgsValue value, int line, int col)
    {
        DeviceObject item = Items[index];
        item.SetProperty(name, value, new DeviceCall { Target = item, Args = [], Line = line, Column = col }, ignoreCase: false);
        return this;
    }

    public bool SameAs(IJgsExternalArray other)
    {
        if (other is not DeviceArray that || that.Rows != Rows || that.Columns != Columns)
        {
            return false;
        }

        for (int i = 0; i < Items.Count; i++)
        {
            if (!ReferenceEquals(Items[i], that.Items[i]))
            {
                return false;
            }
        }

        return true;
    }

    // --- display (probe_dev_arrays2: disp_row, disp_empty, disp_deleted) --------------------------------

    public string Disp()
    {
        if (Items.Count == 0)
        {
            var sb = new StringBuilder($"  {Rows}×{Columns} {Class.ShortName} array with properties:\n\n");
            foreach (string name in Class.VisibleNames)
            {
                sb.Append("    ").Append(name).Append('\n');
            }

            return sb.ToString();
        }

        if (Items.All(static item => item.Deleted))
        {
            return $"{Rows}x{Columns} Invalid or deleted object";
        }

        var text = new StringBuilder($"  {Rows}×{Columns} {Class.ShortName} array with properties:\n");
        foreach (DeviceObject item in Items)
        {
            if (item.Deleted)
            {
                text.Append("\nInvalid or deleted object.\n");
            }
            else
            {
                text.Append("\n   ").Append(Class.ShortName).Append(" with properties:\n").Append(item.ArrayElementDisplay()).Append('\n');
            }
        }

        return text.ToString();
    }

    public string Display() => Disp().TrimEnd('\n');

    public string? Summary() => $"{Items.Count} objects";
}
