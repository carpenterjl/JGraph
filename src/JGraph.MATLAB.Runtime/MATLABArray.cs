using System.Dynamic;
using JGraph.Runtime;
using JGraph.Scripting.Jgs;

namespace JGraph.MATLAB.Types;

/// <summary>
/// A MATLAB value not yet converted to a .NET type (R2025b's MathWorks.MATLAB.Types.MATLABArray):
/// a cast converts it by R2025b's rules, and it can be passed back to a MATLAB function of the
/// runtime instance that answered it. Members are a MATLAB object's alone (see <see cref="MATLABObject"/>).
/// </summary>
public class MATLABArray : DynamicObject, IDisposable, IConvertible
{
    private readonly MatlabValue _value;
    private bool _disposed;

    internal MATLABArray(MATLABProvider matlab, MatlabValue value)
    {
        Provider = matlab;
        _value = value;
    }

    /// <summary>The runtime instance that answered the value.</summary>
    internal MATLABProvider Provider { get; }

    /// <summary>The value, or R2025b's refusal once it has been disposed.</summary>
    internal MatlabValue Value =>
        _disposed ? throw new ObjectDisposedException("JGraph.MATLAB.Types.SafeHandles.ArrayImplHandle") : _value;

    /// <summary>The wrapper R2025b answers for a value: a struct scalar a MATLABStruct, an object scalar a MATLABObject, anything else a MATLABArray.</summary>
    internal static MATLABArray Wrap(MATLABProvider matlab, MatlabValue value) => value switch
    {
        { Kind: MatlabKind.Struct, IsScalar: true } => new MATLABStruct(matlab, value, 0),
        { IsScalarObject: true } => new MATLABObject(matlab, value),
        _ => new MATLABArray(matlab, value),
    };

    /// <summary>The engine's value for this one. Engine thread.</summary>
    internal virtual JgsValue ToEngine(ComponentSession session) => Value.ToEngine(session);

    /// <summary>Releases the value; a later conversion refuses, as R2025b's does.</summary>
    public void Dispose()
    {
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc/>
    public override bool TryConvert(ConvertBinder binder, out object result) =>
        Conversion.TryConvert(this, Value, binder.Type, out result);

    /// <inheritdoc/>
    public override bool TryGetMember(GetMemberBinder binder, out object result) => throw NoMembers();

    /// <inheritdoc/>
    public override bool TrySetMember(SetMemberBinder binder, object value) => throw NoMembers();

    /// <inheritdoc/>
    public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result) => throw NoMembers();

    /// <summary>R2025b's refusal of a member on a value that is not one MATLAB object.</summary>
    private protected InvalidOperationException NoMembers() =>
        Value.Kind is MatlabKind.ValueObject or MatlabKind.HandleObject or MatlabKind.Enumeration && !Value.IsScalar
            ? new InvalidOperationException("Accessing properties and methods not supported for nonscalar MATLAB objects.")
            : new InvalidOperationException($"Accessing properties and methods not supported for MATLAB {Value.ClassName}.");

    // IConvertible, as R2025b's: each conversion is the cast of the same name.
    private T As<T>() => Conversion.TryConvert(this, Value, typeof(T), out object result)
        ? (T)result
        : throw new InvalidCastException($"Unable to cast object of type '{GetType().FullName}' to type '{typeof(T).FullName}'.");

    TypeCode IConvertible.GetTypeCode() => TypeCode.Object;

    bool IConvertible.ToBoolean(IFormatProvider provider) => As<bool>();

    byte IConvertible.ToByte(IFormatProvider provider) => As<byte>();

    char IConvertible.ToChar(IFormatProvider provider) => As<char>();

    DateTime IConvertible.ToDateTime(IFormatProvider provider) => As<DateTime>();

    decimal IConvertible.ToDecimal(IFormatProvider provider) => As<decimal>();

    double IConvertible.ToDouble(IFormatProvider provider) => As<double>();

    short IConvertible.ToInt16(IFormatProvider provider) => As<short>();

    int IConvertible.ToInt32(IFormatProvider provider) => As<int>();

    long IConvertible.ToInt64(IFormatProvider provider) => As<long>();

    sbyte IConvertible.ToSByte(IFormatProvider provider) => As<sbyte>();

    float IConvertible.ToSingle(IFormatProvider provider) => As<float>();

    string IConvertible.ToString(IFormatProvider provider) => As<string>();

    object IConvertible.ToType(Type conversionType, IFormatProvider provider) =>
        Conversion.TryConvert(this, Value, conversionType, out object result)
            ? result
            : throw new InvalidCastException($"Unable to cast object of type '{GetType().FullName}' to type '{conversionType.FullName}'.");

    ushort IConvertible.ToUInt16(IFormatProvider provider) => As<ushort>();

    uint IConvertible.ToUInt32(IFormatProvider provider) => As<uint>();

    ulong IConvertible.ToUInt64(IFormatProvider provider) => As<ulong>();
}

/// <summary>
/// A MATLAB struct (R2025b's MathWorks.MATLAB.Types.MATLABStruct): one answered by a MATLAB
/// function, its fields read by <see cref="GetField"/> or as members, or one a client builds from
/// names and values to pass in. Its fields cannot be set.
/// </summary>
public sealed class MATLABStruct : MATLABArray
{
    private readonly int _element;
    private readonly (string Name, object Value)[] _built;

    /// <summary>A struct of the fields <paramref name="args"/> name, each value converted when it is passed.</summary>
    /// <exception cref="ArgumentException">A name is not a valid MATLAB identifier, or is given twice.</exception>
    public MATLABStruct(params (string, object)[] args)
        : base(null, null)
    {
        _built = args ?? [];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string name, object _) in _built)
        {
            if (!IsValidIdentifier(name))
            {
                throw new ArgumentException($"Invalid field name {name}.");
            }

            if (!seen.Add(name))
            {
                throw new ArgumentException($"Field {name} is a duplicate field, all fields must be unique.");
            }
        }
    }

    internal MATLABStruct(MATLABProvider matlab, MatlabValue value, int element)
        : base(matlab, value)
    {
        _element = element;
    }

    /// <summary>Whether a name is a valid MATLAB identifier: a letter, then letters, digits and underscores, at most 63.</summary>
    internal static bool IsValidIdentifier(string id) =>
        id is { Length: > 0 and <= 63 } && char.IsAsciiLetter(id[0]) && id.All(static c => char.IsAsciiLetterOrDigit(c) || c == '_');

    /// <summary>The fields' names, in order.</summary>
    public IEnumerable<string> GetFieldNames() => _built is not null ? _built.Select(static f => f.Name) : Value.FieldNames;

    /// <summary>The number of fields.</summary>
    public int Count() => _built?.Length ?? Value.FieldNames.Length;

    /// <summary>Whether the struct has the field <paramref name="field"/> (the name compared exactly).</summary>
    public bool IsField(string field) => GetFieldNames().Contains(field, StringComparer.Ordinal);

    /// <summary>The value of the field <paramref name="field"/>.</summary>
    /// <exception cref="MissingFieldException">The struct has no such field.</exception>
    public object GetField(string field)
    {
        ArgumentNullException.ThrowIfNull(field, "key");
        if (_built is not null)
        {
            foreach ((string name, object value) in _built)
            {
                if (name == field)
                {
                    return value;
                }
            }
        }
        else if (Value.Fields[_element].TryGetValue(field, out MatlabValue held))
        {
            return Wrap(Provider, held);
        }

        throw new MissingFieldException($"Field {field} not found in MATLAB struct.");
    }

    /// <inheritdoc/>
    public override bool TryGetMember(GetMemberBinder binder, out object result)
    {
        result = GetField(binder.Name);
        return true;
    }

    /// <inheritdoc/>
    public override bool TrySetMember(SetMemberBinder binder, object value) =>
        throw new InvalidOperationException("Setting the values of a MATLAB struct is unsupported.");

    /// <inheritdoc/>
    public override bool TryConvert(ConvertBinder binder, out object result)
    {
        if (_built is not null)
        {
            if (binder.Type.IsInstanceOfType(this))
            {
                result = this;
                return true;
            }

            if (binder.Type == typeof(MATLABStruct[]))
            {
                result = new[] { this };
                return true;
            }

            throw new InvalidCastException($"Conversion from MATLAB struct scalar to {binder.Type.FullName} is not supported.");
        }

        return base.TryConvert(binder, out result);
    }

    /// <summary>Reference equality, as R2025b's (k9client: struct.equals.*).</summary>
    public override bool Equals(object obj) => ReferenceEquals(this, obj);

    /// <inheritdoc/>
    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);

    /// <summary>The fields as the engine's values, for a struct array or this struct. Engine thread.</summary>
    internal Dictionary<string, JgsValue> EngineFields(MATLABProvider provider, ComponentSession session)
    {
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        if (_built is not null)
        {
            foreach ((string name, object value) in _built)
            {
                fields[name] = Conversion.ToEngine(value, provider, session);
            }
        }
        else
        {
            foreach ((string name, MatlabValue value) in Value.Fields[_element])
            {
                fields[name] = value.ToEngine(session);
            }
        }

        return fields;
    }

    /// <inheritdoc/>
    internal override JgsValue ToEngine(ComponentSession session)
    {
        if (_built is null && Value.IsScalar)
        {
            return base.ToEngine(session);
        }

        return JgsValue.Struct(EngineFields(Provider, session));
    }

    /// <summary>The runtime a built struct is converted for, which is whichever it is passed to.</summary>
    internal bool IsBuilt => _built is not null;
}

/// <summary>
/// A MATLAB object (R2025b's MathWorks.MATLAB.Types.MATLABObject): a scalar instance of a value or
/// handle class, a dictionary, a table, a function handle, a datetime. Its properties are read as
/// members and its methods called as members; a handle object's properties can be set too.
/// </summary>
public sealed class MATLABObject : MATLABArray
{
    // The provider under the name R2025b's generated classes read by reflection:
    // typeof(MATLABObject).GetField("Matlab", BindingFlags.Instance | BindingFlags.NonPublic).
#pragma warning disable IDE1006, SA1306, CA1823
    private readonly MATLABProvider Matlab;
#pragma warning restore IDE1006, SA1306, CA1823

    internal MATLABObject(MATLABProvider matlab, MatlabValue value)
        : base(matlab, value)
    {
        Matlab = matlab;
    }

    /// <inheritdoc/>
    public override bool TryGetMember(GetMemberBinder binder, out object result)
    {
        result = Matlab.GetProperty(this, binder.Name);
        return true;
    }

    /// <inheritdoc/>
    public override bool TrySetMember(SetMemberBinder binder, object value)
    {
        if (Value.Kind != MatlabKind.HandleObject)
        {
            throw new InvalidOperationException("Setting properties on a MATLAB value object not supported.");
        }

        Matlab.SetProperty(this, binder.Name, value);
        return true;
    }

    /// <inheritdoc/>
    public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result)
    {
        result = Matlab.Invoke(binder.Name, [this, .. args], binder.CallInfo.ArgumentNames, method: true);
        return true;
    }
}

/// <summary>
/// A name the provider reached by a dot that names no function: a package, a class, an enumeration
/// member (R2025b's internal MathWorks.MATLAB.Types.MATLABPackage). A call through it calls the
/// function its full name names.
/// </summary>
internal sealed class MATLABPackage(MATLABProvider matlab, string name) : DynamicObject
{
    /// <summary>The dotted name so far.</summary>
    public string Name { get; } = name;

    /// <inheritdoc/>
    public override bool TryGetMember(GetMemberBinder binder, out object result)
    {
        result = new MATLABPackage(matlab, Name + "." + binder.Name);
        return true;
    }

    /// <inheritdoc/>
    public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result)
    {
        result = matlab.Invoke(Name + "." + binder.Name, args, binder.CallInfo.ArgumentNames, method: false);
        return true;
    }

    /// <inheritdoc/>
    public override bool TryConvert(ConvertBinder binder, out object result)
    {
        if (binder.Type.IsInstanceOfType(this))
        {
            result = this;
            return true;
        }

        throw new InvalidCastException(
            $"Conversion from {typeof(MATLABPackage).FullName} to {binder.Type.FullName} is not supported. Call MATLAB functions using the () operator.");
    }
}

/// <summary>
/// The outputs of a call asked for other than one (R2025b's internal FevalTaskResult): cast to a
/// ValueTuple of as many elements to unpack them; none for a call asked for none.
/// </summary>
internal sealed class FevalTaskResult(MATLABArray[] outputs) : DynamicObject
{
    /// <summary>The outputs, in order.</summary>
    public MATLABArray[] Outputs { get; } = outputs;

    /// <inheritdoc/>
    public override bool TryConvert(ConvertBinder binder, out object result)
    {
        Type target = binder.Type;
        if (target.IsInstanceOfType(this))
        {
            result = this;
            return true;
        }

        if (Outputs.Length == 0)
        {
            throw new InvalidCastException("The MATLAB function did not return any outputs.");
        }

        if (!Conversion.IsTuple(target))
        {
            throw new InvalidCastException($"Unable to convert to {target} from {Outputs.Length} MATLAB outputs.");
        }

        int count = Conversion.TupleCount(target);
        if (count != Outputs.Length)
        {
            throw new InvalidCastException($"Unable to convert to {count} element tuple from {Outputs.Length} MATLAB outputs.");
        }

        Type[] elements = [.. Conversion.TupleElements(target)];
        var items = new object[count];
        for (int i = 0; i < count; i++)
        {
            items[i] = Conversion.TupleElement(Outputs[i], elements[i]);
        }

        result = Conversion.MakeTuple(target, items);
        return true;
    }

    /// <inheritdoc/>
    public override bool TryGetMember(GetMemberBinder binder, out object result) => throw Unpack();

    /// <inheritdoc/>
    public override bool TrySetMember(SetMemberBinder binder, object value) => throw Unpack();

    /// <inheritdoc/>
    public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result) => throw Unpack();

    private static InvalidOperationException Unpack() =>
        new("Multiple MATLAB outputs must be cast to a ValueTuple and unpacked before accessing properties or methods.");
}
