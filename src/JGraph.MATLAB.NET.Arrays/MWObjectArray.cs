using JGraph.Runtime;

namespace JGraph.MATLAB.NET.Arrays;

/// <summary>
/// A .NET object wrapped for MATLAB, as R2025b's: 1-by-1, its <see cref="ToString"/> the object's.
/// JGraph's runtime does not pass a .NET object into a component yet (a divergence, ADR 0264).
/// </summary>
public class MWObjectArray : MWArray, IEquatable<MWObjectArray>
{
    private object _object;

    /// <summary>A new <see cref="object"/>, wrapped.</summary>
    public MWObjectArray()
        : this(new object())
    {
    }

    /// <summary><paramref name="obj"/>, wrapped.</summary>
    public MWObjectArray(object obj)
        : base([1, 1]) => _object = obj;

    /// <summary>The object; it has one element.</summary>
    public new object this[params int[] indices]
    {
        get
        {
            CheckDisposed();
            _ = Linear(indices);
            return _object;
        }

        set
        {
            CheckDisposed();
            _ = Linear(indices);
            _object = value;
        }
    }

    /// <summary>Whether <see cref="MWArray.Dispose"/> has been called.</summary>
    public new bool IsDisposed => base.IsDisposed;

    /// <summary>False: it holds one object.</summary>
    public new bool IsEmpty => false;

    /// <summary>An empty object array: a new <see cref="object"/>, as R2025b's (<c>object.static.empty</c>).</summary>
    public static MWObjectArray Empty => new();

    /// <summary>The object.</summary>
    public object Object
    {
        get => _object;
        set => _object = value;
    }

    /// <inheritdoc />
    public override MWArrayType ArrayType => MWArrayType.NativeObjArray;

    /// <inheritdoc />
    public override object Clone() => new MWObjectArray(_object);

    /// <summary>Whether both wrap the same object.</summary>
    public bool Equals(MWObjectArray obj) => obj is not null && Equals(_object, obj._object);

    /// <inheritdoc />
    public override bool Equals(object obj) => obj is MWObjectArray other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _object?.GetHashCode() ?? 0;

    /// <inheritdoc />
    public override string ToString() => base.ToString();

    /// <summary>Whether both wrap the same object.</summary>
    public static bool operator ==(MWObjectArray left, MWObjectArray right) => left is null ? right is null : left.Equals(right);

    /// <summary>Whether they wrap different objects.</summary>
    public static bool operator !=(MWObjectArray left, MWObjectArray right) => !(left == right);

    private protected override string Text() => _object?.ToString() ?? string.Empty;

    internal override RuntimeValue ToRuntime() =>
        throw new ArgumentException("JGraph's runtime does not pass a .NET object into a component yet (MWObjectArray).");
}
