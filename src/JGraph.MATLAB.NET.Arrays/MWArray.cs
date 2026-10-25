using JGraph.Runtime;

namespace JGraph.MATLAB.NET.Arrays;

/// <summary>Whether an array is real or complex.</summary>
public enum MWArrayComplexity
{
    /// <summary>Real.</summary>
    Real = 0,

    /// <summary>Complex.</summary>
    Complex = 1,
}

/// <summary>A part of a complex array.</summary>
public enum MWArrayComponent
{
    /// <summary>The real parts.</summary>
    Real = 0,

    /// <summary>The imaginary parts.</summary>
    Imaginary = 1,
}

/// <summary>What kind of MATLAB array an MWArray holds.</summary>
public enum MWArrayType
{
    /// <summary>A value of no other kind (an object, a function handle).</summary>
    Array = 0,

    /// <summary>An index array.</summary>
    Index = 1,

    /// <summary>A numeric array.</summary>
    Numeric = 2,

    /// <summary>A logical array.</summary>
    Logical = 3,

    /// <summary>A char array.</summary>
    Character = 4,

    /// <summary>A cell array.</summary>
    Cell = 5,

    /// <summary>A .NET object.</summary>
    NativeObjArray = 6,

    /// <summary>A struct array.</summary>
    Structure = 7,

    /// <summary>A string array.</summary>
    String = 8,
}

/// <summary>A numeric array's class; the values are MATLAB's mxClassID.</summary>
public enum MWNumericType
{
    /// <summary><c>double</c>.</summary>
    Double = 6,

    /// <summary><c>single</c>.</summary>
    Single = 7,

    /// <summary><c>int8</c>.</summary>
    Int8 = 8,

    /// <summary><c>uint8</c>.</summary>
    UInt8 = 9,

    /// <summary><c>int16</c>.</summary>
    Int16 = 10,

    /// <summary><c>uint16</c>.</summary>
    UInt16 = 11,

    /// <summary><c>int32</c>.</summary>
    Int32 = 12,

    /// <summary><c>uint32</c>.</summary>
    UInt32 = 13,

    /// <summary><c>int64</c>.</summary>
    Int64 = 14,

    /// <summary><c>uint64</c>.</summary>
    UInt64 = 15,
}

/// <summary>
/// A MATLAB array a .NET client passes to a component's functions or gets back from them, as R2025b's
/// MWArray API has it (compiler and SDK plan, stage K8): its size, its kind, element access by MATLAB's
/// 1-based subscripts, conversion to .NET arrays, and <see cref="ToString"/> as MATLAB displays it
/// (<c>k8_client</c>). An array of no other kind, such as a function handle a component returned, is
/// a plain MWArray that only that component takes back.
/// </summary>
public class MWArray : ICloneable, IDisposable
{
    private int[] _dims;
    private bool _disposed;
    private readonly RuntimeValue _opaque;

    /// <summary>An array with this size, for the kinds that store their own elements.</summary>
    private protected MWArray(int[] dimensions) => _dims = Shape(dimensions);

    private MWArray(RuntimeValue opaque)
    {
        _opaque = opaque;
        _dims = [.. opaque.Dimensions];
    }

    /// <summary>An element, or a block of elements, at MATLAB's 1-based subscripts.</summary>
    public MWArray this[params int[] indices]
    {
        get => GetElement(indices);
        set => SetElement(indices, value);
    }

    /// <summary>Whether the runtime frees native memory under pressure; JGraph's arrays are managed, so this only keeps what is set.</summary>
    public static bool NativeGCEnabled { get; set; } = true;

    /// <summary>The block size of that collection; JGraph's arrays are managed.</summary>
    public static long NativeGCBlockSize => 0;

    /// <summary>What kind of array this is.</summary>
    public virtual MWArrayType ArrayType => MWArrayType.Array;

    /// <summary>The size of each dimension.</summary>
    public int[] Dimensions
    {
        get
        {
            CheckDisposed();
            return (int[])_dims.Clone();
        }
    }

    /// <summary>Whether <see cref="Dispose"/> has been called.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>Whether the array has no elements.</summary>
    public bool IsEmpty => NumberOfElements == 0;

    /// <summary>Whether this is a cell array.</summary>
    public bool IsCellArray => ArrayType == MWArrayType.Cell;

    /// <summary>Whether this is a char array.</summary>
    public bool IsCharArray => ArrayType == MWArrayType.Character;

    /// <summary>Whether this is a logical array.</summary>
    public bool IsLogicalArray => ArrayType == MWArrayType.Logical;

    /// <summary>Whether this is a numeric array.</summary>
    public bool IsNumericArray => ArrayType == MWArrayType.Numeric;

    /// <summary>Whether this is a struct array.</summary>
    public bool IsStructArray => ArrayType == MWArrayType.Structure;

    /// <summary>Whether this is a string array.</summary>
    public bool IsStringArray => ArrayType == MWArrayType.String;

    /// <summary>The number of dimensions, at least two.</summary>
    public int NumberofDimensions => _dims.Length;

    /// <summary>The number of elements.</summary>
    public int NumberOfElements => MatlabDisplay.Count(_dims);

    /// <summary>The size, for the kinds that change it.</summary>
    private protected int[] Dims
    {
        get => _dims;
        set => _dims = value;
    }

    /// <summary>A deep copy.</summary>
    public virtual object Clone() => new MWArray(_opaque);

    /// <summary>Each array as the .NET type asked for (R2025b's <c>ConvertToNativeTypes</c>).</summary>
    public static object[] ConvertToNativeTypes(MWArray[] src, Type[] specifiedTypes)
    {
        ArgumentNullException.ThrowIfNull(src);
        ArgumentNullException.ThrowIfNull(specifiedTypes);
        var converted = new object[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            converted[i] = NativeConversion.ToType(src[i], i < specifiedTypes.Length ? specifiedTypes[i] : null);
        }

        return converted;
    }

    /// <summary>Retires the array: nothing can be read from it afterwards.</summary>
    public void Dispose()
    {
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>Disposes an MWArray, or every MWArray in an array of them.</summary>
    public static void DisposeArray(object _object)
    {
        switch (_object)
        {
            case MWArray array:
                array.Dispose();
                break;
            case Array arrays:
                foreach (object item in arrays)
                {
                    DisposeArray(item);
                }

                break;
        }
    }

    /// <summary>Whether <paramref name="obj"/> is an array of the same kind, class, size and elements.</summary>
    public override bool Equals(object obj) =>
        obj is MWArray other && other.GetType() == GetType() && !_disposed && !other._disposed && ToRuntime().Equals(other.ToRuntime());

    /// <inheritdoc />
    public override int GetHashCode() => _disposed ? 0 : ToRuntime().GetHashCode();

    /// <summary>The elements as a .NET array, as the native API answers them.</summary>
    public virtual Array ToArray()
    {
        CheckDisposed();
        throw new InvalidDataException("Data type unsupported by MATLAB .NET Assembly for conversion to array");
    }

    /// <summary>The array as MATLAB displays it.</summary>
    public override string ToString()
    {
        CheckDisposed();
        return Text();
    }

    /// <summary>A char row of <paramref name="value"/>.</summary>
    public static implicit operator MWArray(string value) => new MWCharArray(value);

    /// <summary>A double scalar.</summary>
    public static implicit operator MWArray(double value) => new MWNumericArray(value);

    /// <summary>The text <see cref="ToString"/> answers, once the array is known to be live.</summary>
    private protected virtual string Text() => "\topaque"; // R2025b's text for a value of no other kind (k8_client: make.handle)

    /// <summary>The value the runtime passes to a component.</summary>
    internal virtual RuntimeValue ToRuntime()
    {
        CheckDisposed();
        return _opaque;
    }

    /// <summary>An MWArray for a value a component returned.</summary>
    internal static MWArray FromRuntime(RuntimeValue value) => value switch
    {
        RuntimeNumeric or RuntimeSparse => MWNumericArray.FromRuntime(value),
        RuntimeLogical logical => MWLogicalArray.FromRuntime(logical),
        RuntimeChar text => MWCharArray.FromRuntime(text),
        RuntimeString strings => MWStringArray.FromRuntime(strings),
        RuntimeCell cell => MWCellArray.FromRuntime(cell),
        RuntimeStruct record => MWStructArray.FromRuntime(record),
        _ => new MWArray(value),
    };

    private protected virtual MWArray GetElement(int[] indices)
    {
        CheckDisposed();
        throw new Exception("Operation is not supported on specified MATLAB array type");
    }

    private protected virtual void SetElement(int[] indices, MWArray value)
    {
        CheckDisposed();
        throw new Exception("Operation is not supported on specified MATLAB array type");
    }

    /// <summary>R2025b's refusal of a disposed array.</summary>
    private protected void CheckDisposed()
    {
        if (_disposed)
        {
            // R2025b names MathWorks.MATLAB.Arrays.MWArray (k8_client: num.after.dispose).
            throw new ObjectDisposedException("JGraph.MATLAB.Arrays.MWArray", "Invalid access to disposed object");
        }
    }

    /// <summary>
    /// The 0-based column-major element at MATLAB's subscripts: one is a linear index, as many as the
    /// array has dimensions a subscript each; anything else, or a subscript outside, is R2025b's
    /// <c>Invalid index</c> (k8_client).
    /// </summary>
    private protected int Linear(int[] indices)
    {
        ArgumentNullException.ThrowIfNull(indices);
        int count = NumberOfElements;
        if (indices.Length == 1)
        {
            return indices[0] >= 1 && indices[0] <= count ? indices[0] - 1 : throw InvalidIndex();
        }

        if (indices.Length != _dims.Length)
        {
            throw InvalidIndex();
        }

        int linear = 0;
        int stride = 1;
        for (int d = 0; d < _dims.Length; d++)
        {
            if (indices[d] < 1 || indices[d] > _dims[d])
            {
                throw InvalidIndex();
            }

            linear += (indices[d] - 1) * stride;
            stride *= _dims[d];
        }

        return linear;
    }

    private protected static Exception InvalidIndex() => new("Invalid index");

    /// <summary>MATLAB's shape for a size: at least two dimensions, trailing singletons past the second dropped.</summary>
    private protected static int[] Shape(int[] dimensions)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        if (dimensions.Any(static d => d < 0))
        {
            throw new ArgumentException("The specified dimensions are invalid");
        }

        if (dimensions.Length < 2)
        {
            // A single size is a row of that many; none is 0-by-0.
            return dimensions.Length == 1 ? [1, dimensions[0]] : [0, 0];
        }

        int length = dimensions.Length;
        while (length > 2 && dimensions[length - 1] == 1)
        {
            length--;
        }

        return dimensions[..length];
    }
}

/// <summary>
/// An array that can index another: R2025b's base of the numeric and logical arrays. An int converts
/// to one of these directly, as R2025b's does (<c>k8_client</c>: <c>index.implicit.int</c>).
/// </summary>
public class MWIndexArray : MWArray
{
    private readonly MWNumericArray _value;

    private protected MWIndexArray(int[] dimensions)
        : base(dimensions)
    {
    }

    private MWIndexArray(MWNumericArray value)
        : base(value.Dimensions) => _value = value;

    /// <inheritdoc />
    public override MWArrayType ArrayType => MWArrayType.Index;

    /// <summary>Whether the array is sparse.</summary>
    public virtual bool IsSparse => false;

    /// <summary>The most nonzero elements a sparse array has room for.</summary>
    public virtual int NonZeroMaxStorage => 0;

    /// <inheritdoc />
    public override object Clone() => new MWIndexArray((MWNumericArray)_value.Clone());

    /// <inheritdoc />
    public override bool Equals(object obj) => base.Equals(obj);

    /// <inheritdoc />
    public override int GetHashCode() => base.GetHashCode();

    /// <inheritdoc />
    public override string ToString() => base.ToString();

    /// <summary>A double row.</summary>
    public static implicit operator MWIndexArray(int[] values) => new MWNumericArray(values, true, true);

    /// <summary>A double row.</summary>
    public static implicit operator MWIndexArray(long[] values) => new MWNumericArray(values, true, true);

    /// <summary>A double row.</summary>
    public static implicit operator MWIndexArray(short[] values) => new MWNumericArray(values, true, true);

    /// <summary>An index array holding a double scalar.</summary>
    public static implicit operator MWIndexArray(int value) => new(new MWNumericArray(value));

    /// <summary>A double row.</summary>
    public static implicit operator MWIndexArray(byte[] values) => new MWNumericArray(values, true, true);

    private protected override string Text() => _value.ToString();

    internal override RuntimeValue ToRuntime()
    {
        CheckDisposed();
        return _value.ToRuntime();
    }
}
