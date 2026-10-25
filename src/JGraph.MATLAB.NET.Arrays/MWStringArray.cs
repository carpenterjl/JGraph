using JGraph.Runtime;

namespace JGraph.MATLAB.NET.Arrays;

/// <summary>
/// A MATLAB string array, as R2025b's: an element is a .NET string, null for <c>&lt;missing&gt;</c>;
/// <see cref="ToString"/> is a scalar's text (null for a missing or empty one) and the .NET array's
/// name for any other (<c>k8_client</c>: <c>string.strings</c>).
/// </summary>
public class MWStringArray : MWArray
{
    private string[] _strings;

    /// <summary>The empty string array, 0-by-0.</summary>
    public MWStringArray()
        : this([0, 0], [])
    {
    }

    /// <summary>A string array of this size, every element empty.</summary>
    public MWStringArray(int[] dimensions)
        : base(dimensions) => _strings = Enumerable.Repeat(string.Empty, NumberOfElements).ToArray();

    /// <summary>A string row of these elements; null is <c>&lt;missing&gt;</c>.</summary>
    public MWStringArray(params string[] strings)
        : this([1, (strings ?? throw new ArgumentNullException(nameof(strings))).Length], (string[])strings.Clone())
    {
    }

    private MWStringArray(int[] dims, string[] strings)
        : base(dims) => _strings = strings;

    /// <summary>An element at MATLAB's 1-based subscripts.</summary>
    public new string this[params int[] indices]
    {
        get
        {
            CheckDisposed();
            return _strings[Linear(indices)];
        }

        set
        {
            CheckDisposed();
            _strings[Linear(indices)] = value;
        }
    }

    /// <summary>The empty string array.</summary>
    public static MWStringArray Empty => new();

    /// <inheritdoc />
    public override MWArrayType ArrayType => MWArrayType.String;

    /// <inheritdoc />
    public override object Clone() => new MWStringArray(Dimensions, (string[])_strings.Clone());

    /// <inheritdoc />
    public override bool Equals(object obj) => base.Equals(obj);

    /// <inheritdoc />
    public override int GetHashCode() => base.GetHashCode();

    /// <summary>Replaces the elements, in MATLAB's column order.</summary>
    public void SetArray(params string[] data)
    {
        CheckDisposed();
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length != _strings.Length)
        {
            throw new ArgumentException("The data array must have as many elements as the array");
        }

        _strings = (string[])data.Clone();
    }

    /// <summary>The elements in MATLAB's column order.</summary>
    public override Array ToArray()
    {
        CheckDisposed();
        return (string[])_strings.Clone();
    }

    /// <inheritdoc />
    public override string ToString() => base.ToString();

    /// <summary>The elements in MATLAB's column order.</summary>
    public static explicit operator string[](MWStringArray value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return (string[])value.ToArray();
    }

    /// <summary>A scalar's text; R2025b's refusal for any other.</summary>
    public static explicit operator string(MWStringArray value)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.CheckDisposed();
        return value._strings.Length == 1
            ? value._strings[0]
            : throw new ArgumentException("Failed to cast to string", new ApplicationException("Not a scalar mxArray"));
    }

    /// <summary>A string row.</summary>
    public static implicit operator MWStringArray(string[] value) => new(value);

    /// <summary>A string scalar.</summary>
    public static implicit operator MWStringArray(string value) => new([value]);

    private protected override string Text() => _strings.Length switch
    {
        0 => null,
        1 => _strings[0],
        _ => typeof(string[]).ToString(),
    };

    internal override RuntimeValue ToRuntime()
    {
        CheckDisposed();
        return new RuntimeString(_strings, Dims);
    }

    internal static MWStringArray FromRuntime(RuntimeString value) => new([.. value.Dimensions], [.. value.Elements]);
}
