using JGraph.Runtime;

namespace JGraph.MATLAB.NET.Arrays;

/// <summary>
/// A numeric array (<c>double</c>, <c>single</c> or an integer class, real or complex, dense or
/// sparse), with R2025b's constructors and conversions as <c>k8_client</c> measured them: a scalar or
/// a .NET array makes a <c>double</c> unless <c>makeDouble</c> is false, flat data is read by rows
/// unless <c>rowMajorData</c> is false, an element is read and written at MATLAB's 1-based subscripts,
/// and <c>ToScalarDouble</c> and its kin read the first element of an array of their class.
/// </summary>
/// <remarks>
/// Where R2025b answers wrongly this answers rightly, and the divergence is recorded (ADR 0264): an
/// element of a complex array reads its value (R2025b's reads 0), a complex value written into a real
/// array makes it complex (R2025b's drops the imaginary part), and a value written into an integer
/// array is rounded to its class (R2025b's keeps the old one). The elements are kept as doubles, so
/// an <c>int64</c> is exact to 2^53, as everywhere in JGraph.
/// </remarks>
public class MWNumericArray : MWIndexArray
{
    /// <summary>R2025b's <c>Inf</c>.</summary>
    public static MWNumericArray Inf = new(double.PositiveInfinity);

    /// <summary>R2025b's <c>NaN</c>.</summary>
    public static MWNumericArray NaN = new(double.NaN);

    private MWNumericType _type;
    private double[] _re;
    private double[] _im;
    private bool _sparse;
    private int _nonZeroMax;

    /// <summary>The empty double, <c>[]</c>.</summary>
    public MWNumericArray()
        : this(MWNumericType.Double, [0, 0], [], null)
    {
    }

    /// <summary>An empty array of <paramref name="dataType"/>.</summary>
    public MWNumericArray(MWNumericType dataType)
        : this(dataType, [0, 1], [], null)
    {
    }

    /// <summary>A double scalar.</summary>
    public MWNumericArray(double scalar)
        : this(MWNumericType.Double, [1, 1], [scalar], null)
    {
    }

    /// <summary>A complex double scalar.</summary>
    public MWNumericArray(double realValue, double imaginaryValue)
        : this(MWNumericType.Double, [1, 1], [realValue], [imaginaryValue])
    {
    }

    /// <summary>A double scalar.</summary>
    public MWNumericArray(int scalar)
        : this(scalar, true)
    {
    }

    /// <summary>An <c>int32</c> scalar, or a double one when <paramref name="makeDouble"/>.</summary>
    public MWNumericArray(int scalar, bool makeDouble)
        : this(makeDouble ? MWNumericType.Double : MWNumericType.Int32, [1, 1], [scalar], null)
    {
    }

    /// <summary>A complex double scalar.</summary>
    public MWNumericArray(int realValue, int imaginaryValue)
        : this(realValue, imaginaryValue, true)
    {
    }

    /// <summary>A complex <c>int32</c> scalar, or a double one when <paramref name="makeDouble"/>.</summary>
    public MWNumericArray(int realValue, int imaginaryValue, bool makeDouble)
        : this(makeDouble ? MWNumericType.Double : MWNumericType.Int32, [1, 1], [realValue], [imaginaryValue])
    {
    }

    /// <summary>A double scalar.</summary>
    public MWNumericArray(long scalar)
        : this(scalar, true)
    {
    }

    /// <summary>An <c>int64</c> scalar, or a double one when <paramref name="makeDouble"/>.</summary>
    public MWNumericArray(long scalar, bool makeDouble)
        : this(makeDouble ? MWNumericType.Double : MWNumericType.Int64, [1, 1], [scalar], null)
    {
    }

    /// <summary>A complex double scalar.</summary>
    public MWNumericArray(long realValue, long imaginaryValue)
        : this(realValue, imaginaryValue, true)
    {
    }

    /// <summary>A complex <c>int64</c> scalar, or a double one when <paramref name="makeDouble"/>.</summary>
    public MWNumericArray(long realValue, long imaginaryValue, bool makeDouble)
        : this(makeDouble ? MWNumericType.Double : MWNumericType.Int64, [1, 1], [realValue], [imaginaryValue])
    {
    }

    /// <summary>A double scalar.</summary>
    public MWNumericArray(float scalar)
        : this(scalar, true)
    {
    }

    /// <summary>A <c>single</c> scalar, or a double one when <paramref name="makeDouble"/>.</summary>
    public MWNumericArray(float scalar, bool makeDouble)
        : this(makeDouble ? MWNumericType.Double : MWNumericType.Single, [1, 1], [scalar], null)
    {
    }

    /// <summary>A complex double scalar.</summary>
    public MWNumericArray(float realValue, float imaginaryValue)
        : this(realValue, imaginaryValue, true)
    {
    }

    /// <summary>A complex <c>single</c> scalar, or a double one when <paramref name="makeDouble"/>.</summary>
    public MWNumericArray(float realValue, float imaginaryValue, bool makeDouble)
        : this(makeDouble ? MWNumericType.Double : MWNumericType.Single, [1, 1], [realValue], [imaginaryValue])
    {
    }

    /// <summary>A double scalar.</summary>
    public MWNumericArray(byte scalar)
        : this(scalar, true)
    {
    }

    /// <summary>A <c>uint8</c> scalar, or a double one when <paramref name="makeDouble"/>.</summary>
    public MWNumericArray(byte scalar, bool makeDouble)
        : this(makeDouble ? MWNumericType.Double : MWNumericType.UInt8, [1, 1], [scalar], null)
    {
    }

    /// <summary>A complex double scalar.</summary>
    public MWNumericArray(byte realValue, byte imaginaryValue)
        : this(realValue, imaginaryValue, true)
    {
    }

    /// <summary>A complex <c>uint8</c> scalar, or a double one when <paramref name="makeDouble"/>.</summary>
    public MWNumericArray(byte realValue, byte imaginaryValue, bool makeDouble)
        : this(makeDouble ? MWNumericType.Double : MWNumericType.UInt8, [1, 1], [realValue], [imaginaryValue])
    {
    }

    /// <summary>A double scalar.</summary>
    public MWNumericArray(short scalar)
        : this(scalar, true)
    {
    }

    /// <summary>An <c>int16</c> scalar, or a double one when <paramref name="makeDouble"/>.</summary>
    public MWNumericArray(short scalar, bool makeDouble)
        : this(makeDouble ? MWNumericType.Double : MWNumericType.Int16, [1, 1], [scalar], null)
    {
    }

    /// <summary>A complex double scalar.</summary>
    public MWNumericArray(short realValue, short imaginaryValue)
        : this(realValue, imaginaryValue, true)
    {
    }

    /// <summary>A complex <c>int16</c> scalar, or a double one when <paramref name="makeDouble"/>.</summary>
    public MWNumericArray(short realValue, short imaginaryValue, bool makeDouble)
        : this(makeDouble ? MWNumericType.Double : MWNumericType.Int16, [1, 1], [realValue], [imaginaryValue])
    {
    }

    /// <summary>A double array of zeros of this size.</summary>
    public MWNumericArray(int[] dimensions)
        : this(MWArrayComplexity.Real, MWNumericType.Double, dimensions)
    {
    }

    /// <summary>A double array of zeros of this size, real or complex.</summary>
    public MWNumericArray(MWArrayComplexity complexity, int[] dimensions)
        : this(complexity, MWNumericType.Double, dimensions)
    {
    }

    /// <summary>An array of zeros of this class and size, real or complex.</summary>
    public MWNumericArray(MWArrayComplexity complexity, MWNumericType dataType, int[] dimensions)
        : base(dimensions)
    {
        _type = dataType;
        _re = new double[NumberOfElements];
        _im = complexity == MWArrayComplexity.Complex ? new double[NumberOfElements] : null;
    }

    /// <summary>A rows-by-columns double matrix, its data read by rows.</summary>
    public MWNumericArray(int rows, int columns, double[] realData)
        : this(rows, columns, realData, true)
    {
    }

    /// <summary>A rows-by-columns double matrix, its data read by rows or, when <paramref name="rowMajorData"/> is false, by columns.</summary>
    public MWNumericArray(int rows, int columns, double[] realData, bool rowMajorData)
        : this(Flat(rows, columns, realData, null, true, rowMajorData))
    {
    }

    /// <summary>A rows-by-columns complex double matrix, its data read by rows.</summary>
    public MWNumericArray(int rows, int columns, double[] realData, double[] imaginaryData)
        : this(rows, columns, realData, imaginaryData, true)
    {
    }

    /// <summary>A rows-by-columns complex double matrix.</summary>
    public MWNumericArray(int rows, int columns, double[] realData, double[] imaginaryData, bool rowMajorData)
        : this(Flat(rows, columns, realData, imaginaryData, true, rowMajorData))
    {
    }

    /// <summary>A rows-by-columns double matrix of <paramref name="realData"/>, read by rows.</summary>
    public MWNumericArray(int rows, int columns, byte[] realData)
        : this(rows, columns, realData, true, true)
    {
    }

    /// <summary>A rows-by-columns <c>uint8</c> (or, with <paramref name="makeDouble"/>, double) matrix.</summary>
    public MWNumericArray(int rows, int columns, byte[] realData, bool makeDouble, bool rowMajorData)
        : this(Flat(rows, columns, realData, null, makeDouble, rowMajorData))
    {
    }

    /// <summary>A rows-by-columns complex double matrix, read by rows.</summary>
    public MWNumericArray(int rows, int columns, byte[] realData, byte[] imaginaryData)
        : this(rows, columns, realData, imaginaryData, true, true)
    {
    }

    /// <summary>A rows-by-columns complex <c>uint8</c> (or double) matrix.</summary>
    public MWNumericArray(int rows, int columns, byte[] realData, byte[] imaginaryData, bool makeDouble, bool rowMajorData)
        : this(Flat(rows, columns, realData, imaginaryData, makeDouble, rowMajorData))
    {
    }

    /// <summary>A rows-by-columns double matrix of <paramref name="realData"/>, read by rows.</summary>
    public MWNumericArray(int rows, int columns, short[] realData)
        : this(rows, columns, realData, true, true)
    {
    }

    /// <summary>A rows-by-columns <c>int16</c> (or double) matrix.</summary>
    public MWNumericArray(int rows, int columns, short[] realData, bool makeDouble, bool rowMajorData)
        : this(Flat(rows, columns, realData, null, makeDouble, rowMajorData))
    {
    }

    /// <summary>A rows-by-columns complex double matrix, read by rows.</summary>
    public MWNumericArray(int rows, int columns, short[] realData, short[] imaginaryData)
        : this(rows, columns, realData, imaginaryData, true, true)
    {
    }

    /// <summary>A rows-by-columns complex <c>int16</c> (or double) matrix.</summary>
    public MWNumericArray(int rows, int columns, short[] realData, short[] imaginaryData, bool makeDouble, bool rowMajorData)
        : this(Flat(rows, columns, realData, imaginaryData, makeDouble, rowMajorData))
    {
    }

    /// <summary>A rows-by-columns double matrix of <paramref name="realData"/>, read by rows.</summary>
    public MWNumericArray(int rows, int columns, int[] realData)
        : this(rows, columns, realData, true, true)
    {
    }

    /// <summary>A rows-by-columns <c>int32</c> (or double) matrix.</summary>
    public MWNumericArray(int rows, int columns, int[] realData, bool makeDouble, bool rowMajorData)
        : this(Flat(rows, columns, realData, null, makeDouble, rowMajorData))
    {
    }

    /// <summary>A rows-by-columns complex double matrix, read by rows.</summary>
    public MWNumericArray(int rows, int columns, int[] realData, int[] imaginaryData)
        : this(rows, columns, realData, imaginaryData, true, true)
    {
    }

    /// <summary>A rows-by-columns complex <c>int32</c> (or double) matrix.</summary>
    public MWNumericArray(int rows, int columns, int[] realData, int[] imaginaryData, bool makeDouble, bool rowMajorData)
        : this(Flat(rows, columns, realData, imaginaryData, makeDouble, rowMajorData))
    {
    }

    /// <summary>A rows-by-columns double matrix of <paramref name="realData"/>, read by rows.</summary>
    public MWNumericArray(int rows, int columns, long[] realData)
        : this(rows, columns, realData, true, true)
    {
    }

    /// <summary>A rows-by-columns <c>int64</c> (or double) matrix.</summary>
    public MWNumericArray(int rows, int columns, long[] realData, bool makeDouble, bool rowMajorData)
        : this(Flat(rows, columns, realData, null, makeDouble, rowMajorData))
    {
    }

    /// <summary>A rows-by-columns complex double matrix, read by rows.</summary>
    public MWNumericArray(int rows, int columns, long[] realData, long[] imaginaryData)
        : this(rows, columns, realData, imaginaryData, true, true)
    {
    }

    /// <summary>A rows-by-columns complex <c>int64</c> (or double) matrix.</summary>
    public MWNumericArray(int rows, int columns, long[] realData, long[] imaginaryData, bool makeDouble, bool rowMajorData)
        : this(Flat(rows, columns, realData, imaginaryData, makeDouble, rowMajorData))
    {
    }

    /// <summary>A rows-by-columns double matrix of <paramref name="realData"/>, read by rows.</summary>
    public MWNumericArray(int rows, int columns, float[] realData)
        : this(rows, columns, realData, true, true)
    {
    }

    /// <summary>A rows-by-columns <c>single</c> (or double) matrix.</summary>
    public MWNumericArray(int rows, int columns, float[] realData, bool makeDouble, bool rowMajorData)
        : this(Flat(rows, columns, realData, null, makeDouble, rowMajorData))
    {
    }

    /// <summary>A rows-by-columns complex double matrix, read by rows.</summary>
    public MWNumericArray(int rows, int columns, float[] realData, float[] imaginaryData)
        : this(rows, columns, realData, imaginaryData, true, true)
    {
    }

    /// <summary>A rows-by-columns complex <c>single</c> (or double) matrix.</summary>
    public MWNumericArray(int rows, int columns, float[] realData, float[] imaginaryData, bool makeDouble, bool rowMajorData)
        : this(Flat(rows, columns, realData, imaginaryData, makeDouble, rowMajorData))
    {
    }

    /// <summary>A double array of a .NET array's elements, its shape the array's (a vector is a row).</summary>
    public MWNumericArray(Array array)
        : this(array, true, true)
    {
    }

    /// <summary>An array of a .NET array's elements, of their class unless <paramref name="makeDouble"/>.</summary>
    public MWNumericArray(Array realData, bool makeDouble, bool rowMajorData)
        : this(FromArrays(realData, null, makeDouble, rowMajorData))
    {
    }

    /// <summary>A complex double array of two .NET arrays.</summary>
    public MWNumericArray(Array realData, Array imaginaryData)
        : this(realData, imaginaryData, true, true)
    {
    }

    /// <summary>A complex array of two .NET arrays.</summary>
    public MWNumericArray(Array realData, Array imaginaryData, bool makeDouble, bool rowMajorData)
        : this(FromArrays(realData, imaginaryData, makeDouble, rowMajorData))
    {
    }

    private MWNumericArray(MWNumericType type, int[] dims, double[] re, double[] im)
        : base(dims)
    {
        _type = type;
        _re = re;
        _im = im;
    }

    private MWNumericArray((MWNumericType Type, int[] Dims, double[] Re, double[] Im) made)
        : this(made.Type, made.Dims, made.Re, made.Im)
    {
    }

    /// <summary>An element, as a 1-by-1 array of this class, at MATLAB's 1-based subscripts.</summary>
    public new MWNumericArray this[params int[] indices]
    {
        get => (MWNumericArray)GetElement(indices);
        set => SetElement(indices, value);
    }

    /// <summary>One part of an element, as a 1-by-1 real array of this class.</summary>
    public MWNumericArray this[MWArrayComponent component, params int[] indices]
    {
        get
        {
            CheckDisposed();
            int at = Linear(indices);
            double part = component == MWArrayComponent.Real ? _re[at] : _im?[at] ?? 0;
            return new MWNumericArray(_type, [1, 1], [part], null);
        }

        set
        {
            CheckDisposed();
            ArgumentNullException.ThrowIfNull(value);
            int at = Linear(indices);
            double part = Held(_type, value.FirstReal);
            if (component == MWArrayComponent.Real)
            {
                _re[at] = part;
            }
            else
            {
                _im ??= new double[_re.Length];
                _im[at] = part;
            }
        }
    }

    /// <summary>The empty double.</summary>
    public static MWNumericArray Empty => new();

    /// <summary>The distance from 1 to the next double, <c>eps</c>.</summary>
    public static double FloatingPointAccuracy => Math.BitIncrement(1.0) - 1.0;

    /// <inheritdoc />
    public override MWArrayType ArrayType => MWArrayType.Numeric;

    /// <summary>The array's class.</summary>
    public MWNumericType NumericType => _type;

    /// <summary>Whether the array is <c>uint8</c>.</summary>
    public bool IsByte => _type == MWNumericType.UInt8;

    /// <summary>Whether the array is complex.</summary>
    public bool IsComplex => _im is not null;

    /// <summary>Whether the array is <c>double</c>.</summary>
    public bool IsDouble => _type == MWNumericType.Double;

    /// <summary>Whether the array is <c>single</c>.</summary>
    public bool IsFloat => _type == MWNumericType.Single;

    /// <summary>Whether the array is <c>int32</c>.</summary>
    public bool IsInteger => _type == MWNumericType.Int32;

    /// <summary>Whether the array is <c>int64</c>.</summary>
    public bool IsLong => _type == MWNumericType.Int64;

    /// <summary>Whether the array is <c>int16</c>.</summary>
    public bool IsShort => _type == MWNumericType.Int16;

    /// <summary>Whether a scalar is infinite.</summary>
    public bool IsInfinity => double.IsInfinity(ScalarPart());

    /// <summary>Whether a scalar is NaN.</summary>
    public bool IsNaN => double.IsNaN(ScalarPart());

    /// <inheritdoc />
    public override bool IsSparse => _sparse;

    /// <inheritdoc />
    public override int NonZeroMaxStorage => _sparse ? Math.Max(_nonZeroMax, _re.Count(static x => x != 0)) : 0;

    /// <summary>The first element's real part, for a value written into an element.</summary>
    internal double FirstReal => _re.Length > 0 ? _re[0] : 0;

    /// <inheritdoc />
    public override object Clone() =>
        new MWNumericArray(_type, Dimensions, (double[])_re.Clone(), (double[])_im?.Clone()) { _sparse = _sparse, _nonZeroMax = _nonZeroMax };

    /// <inheritdoc />
    public override bool Equals(object obj) => base.Equals(obj);

    /// <inheritdoc />
    public override int GetHashCode() => base.GetHashCode();

    /// <summary>A sparse double matrix of the values at the 1-based rows and columns, as small as holds them.</summary>
    public static MWNumericArray MakeSparse(int[] rowIndex, int[] columnIndex, double[] realData) =>
        MakeSparse(MaxOf(rowIndex), MaxOf(columnIndex), rowIndex, columnIndex, realData, null, 0);

    /// <summary>A complex sparse double matrix.</summary>
    public static MWNumericArray MakeSparse(int[] rowIndex, int[] columnIndex, double[] realData, double[] imaginaryData) =>
        MakeSparse(MaxOf(rowIndex), MaxOf(columnIndex), rowIndex, columnIndex, realData, imaginaryData, 0);

    /// <summary>A rows-by-columns sparse double matrix.</summary>
    public static MWNumericArray MakeSparse(int rows, int columns, int[] rowIndex, int[] columnIndex, double[] realData) =>
        MakeSparse(rows, columns, rowIndex, columnIndex, realData, null, 0);

    /// <summary>A rows-by-columns complex sparse double matrix.</summary>
    public static MWNumericArray MakeSparse(int rows, int columns, int[] rowIndex, int[] columnIndex, double[] realData, double[] imaginaryData) =>
        MakeSparse(rows, columns, rowIndex, columnIndex, realData, imaginaryData, 0);

    /// <summary>A rows-by-columns sparse double matrix with room for <paramref name="nonZeroMax"/> values.</summary>
    public static MWNumericArray MakeSparse(int rows, int columns, int[] rowIndex, int[] columnIndex, double[] realData, int nonZeroMax) =>
        MakeSparse(rows, columns, rowIndex, columnIndex, realData, null, nonZeroMax);

    /// <summary>A rows-by-columns complex sparse double matrix with room for <paramref name="nonZeroMax"/> values.</summary>
    public static MWNumericArray MakeSparse(int rows, int columns, int[] rowIndex, int[] columnIndex, double[] realData, double[] imaginaryData, int nonZeroMax)
    {
        ArgumentNullException.ThrowIfNull(rowIndex);
        ArgumentNullException.ThrowIfNull(columnIndex);
        ArgumentNullException.ThrowIfNull(realData);
        if (rowIndex.Length != columnIndex.Length || rowIndex.Length != realData.Length || (imaginaryData is not null && imaginaryData.Length != realData.Length))
        {
            throw new ArgumentException("The index and data arrays must be the same length");
        }

        var made = new MWNumericArray(imaginaryData is null ? MWArrayComplexity.Real : MWArrayComplexity.Complex, MWNumericType.Double, [rows, columns])
        {
            _sparse = true,
            _nonZeroMax = nonZeroMax,
        };
        for (int k = 0; k < rowIndex.Length; k++)
        {
            if (rowIndex[k] < 1 || rowIndex[k] > rows || columnIndex[k] < 1 || columnIndex[k] > columns)
            {
                throw InvalidIndex();
            }

            int at = (rowIndex[k] - 1) + ((columnIndex[k] - 1) * rows);
            made._re[at] += realData[k];
            if (imaginaryData is not null)
            {
                made._im[at] += imaginaryData[k];
            }
        }

        return made;
    }

    /// <summary>An all-zero sparse matrix with room for <paramref name="nonZeroMax"/> values.</summary>
    public static MWNumericArray MakeSparse(int rows, int columns, MWArrayComplexity complexity, int nonZeroMax) =>
        new(complexity, MWNumericType.Double, [rows, columns]) { _sparse = true, _nonZeroMax = nonZeroMax };

    /// <summary>The elements as a .NET array of the class's element type, shaped as the array is; a complex array has no such form.</summary>
    public override Array ToArray()
    {
        CheckDisposed();
        if (_im is not null)
        {
            throw new InvalidDataException("Data type unsupported by MATLAB .NET Assembly for output");
        }

        return NativeConversion.Shaped(Dimensions, ElementType(_type), i => NativeConversion.Element(ElementType(_type), _re[i]));
    }

    /// <summary>The real or the imaginary parts as a .NET array shaped as the array is.</summary>
    public Array ToArray(MWArrayComponent component)
    {
        CheckDisposed();
        double[] parts = Part(component);
        return NativeConversion.Shaped(Dimensions, ElementType(_type), i => NativeConversion.Element(ElementType(_type), parts[i]));
    }

    /// <summary>An empty vector of the class's element type.</summary>
    public Array ToEmptyVector()
    {
        CheckDisposed();
        return Array.CreateInstance(ElementType(_type), 0);
    }

    /// <summary>The real or the imaginary parts in MATLAB's column order.</summary>
    public Array ToVector(MWArrayComponent component)
    {
        CheckDisposed();
        double[] parts = Part(component);
        Type element = ElementType(_type);
        var vector = Array.CreateInstance(element, parts.Length);
        for (int i = 0; i < parts.Length; i++)
        {
            vector.SetValue(NativeConversion.Element(element, parts[i]), i);
        }

        return vector;
    }

    /// <summary>The first element of a <c>uint8</c> array.</summary>
    public byte ToScalarByte() => (byte)First(MWNumericType.UInt8);

    /// <summary>The first element of a double array.</summary>
    public double ToScalarDouble() => First(MWNumericType.Double);

    /// <summary>The first element of a <c>single</c> array.</summary>
    public float ToScalarFloat() => (float)First(MWNumericType.Single);

    /// <summary>The first element of an <c>int32</c> array, or of a double one with its fraction dropped.</summary>
    public int ToScalarInteger() => (int)First(MWNumericType.Int32, MWNumericType.Double);

    /// <summary>The first element of an <c>int64</c> array.</summary>
    public long ToScalarLong() => (long)First(MWNumericType.Int64);

    /// <summary>The first element of an <c>int16</c> array.</summary>
    public short ToScalarShort() => (short)First(MWNumericType.Int16);

    /// <inheritdoc />
    public override string ToString() => base.ToString();

    /// <summary>R2025b's <c>ToScalarInteger</c>.</summary>
    public static explicit operator int(MWNumericArray value) => value.ToScalarInteger();

    /// <summary>R2025b's <c>ToScalarShort</c>.</summary>
    public static explicit operator short(MWNumericArray value) => value.ToScalarShort();

    /// <summary>R2025b's <c>ToScalarByte</c>.</summary>
    public static explicit operator byte(MWNumericArray value) => value.ToScalarByte();

    /// <summary>R2025b's <c>ToScalarLong</c>.</summary>
    public static explicit operator long(MWNumericArray value) => value.ToScalarLong();

    /// <summary>R2025b's <c>ToScalarDouble</c>.</summary>
    public static explicit operator double(MWNumericArray value) => value.ToScalarDouble();

    /// <summary>R2025b's <c>ToScalarFloat</c>.</summary>
    public static explicit operator float(MWNumericArray value) => value.ToScalarFloat();

    /// <summary>A double row.</summary>
    public static implicit operator MWNumericArray(double[] values) => new(values, true, true);

    /// <summary>A <c>single</c> row.</summary>
    public static implicit operator MWNumericArray(float[] values) => new(values, false, true);

    /// <summary>A <c>uint8</c> scalar.</summary>
    public static implicit operator MWNumericArray(byte value) => new(value, false);

    /// <summary>A double array of a .NET array.</summary>
    public static implicit operator MWNumericArray(Array values) => new(values);

    /// <summary>An <c>int64</c> scalar.</summary>
    public static implicit operator MWNumericArray(long value) => new(value, false);

    /// <summary>A <c>single</c> scalar.</summary>
    public static implicit operator MWNumericArray(float value) => new(value, false);

    /// <summary>An <c>int16</c> scalar.</summary>
    public static implicit operator MWNumericArray(short value) => new(value, false);

    /// <summary>A double scalar.</summary>
    public static implicit operator MWNumericArray(int value) => new(value);

    /// <summary>A double scalar.</summary>
    public static implicit operator MWNumericArray(double value) => new(value);

    /// <summary>A double row.</summary>
    public static implicit operator MWNumericArray(int[] values) => new(values, true, true);

    /// <summary>An <c>int64</c> row.</summary>
    public static implicit operator MWNumericArray(long[] values) => new(values, false, true);

    /// <summary>A <c>uint8</c> row.</summary>
    public static implicit operator MWNumericArray(byte[] values) => new(values, false, true);

    /// <summary>An <c>int16</c> row.</summary>
    public static implicit operator MWNumericArray(short[] values) => new(values, false, true);

    private protected override string Text() => _sparse
        ? MatlabDisplay.Sparse(Dims[0], Dims[1], _re, _im, logical: false)
        : MatlabDisplay.Numeric(Dims, _re, _im, _type);

    internal override RuntimeValue ToRuntime()
    {
        CheckDisposed();
        if (_sparse)
        {
            if (_im is not null)
            {
                throw new ArgumentException("JGraph's runtime takes no complex sparse matrix: pass it dense, or its real and imaginary parts.");
            }

            (int[] rows, int[] starts, double[] values) = NativeConversion.Compressed(Dims[0], Dims[1], _re);
            return new RuntimeSparse(Dims[0], Dims[1], rows, starts, values);
        }

        // JGraph keeps no complex integer, so a complex integer array goes in as a complex double.
        string className = _im is not null && _type is not (MWNumericType.Double or MWNumericType.Single) ? "double" : ClassName(_type);
        return new RuntimeNumeric(className, _re, _im, Dims);
    }

    /// <summary>An MWNumericArray for a numeric value or a sparse matrix a component returned.</summary>
    internal static new MWNumericArray FromRuntime(RuntimeValue value)
    {
        if (value is RuntimeSparse sparse)
        {
            var made = new MWNumericArray(MWArrayComplexity.Real, MWNumericType.Double, [sparse.Dimensions[0], sparse.Dimensions[1]]) { _sparse = true };
            for (int c = 0; c < sparse.Dimensions[1]; c++)
            {
                for (int k = sparse.ColumnStarts[c]; k < sparse.ColumnStarts[c + 1]; k++)
                {
                    made._re[sparse.RowIndices[k] + (c * sparse.Dimensions[0])] = sparse.Values[k];
                }
            }

            return made;
        }

        var numeric = (RuntimeNumeric)value;
        return new MWNumericArray(TypeOf(numeric.ClassName), [.. numeric.Dimensions], [.. numeric.Real], numeric.Imaginary is { } im ? [.. im] : null);
    }

    internal static string ClassName(MWNumericType type) => type switch
    {
        MWNumericType.Single => "single",
        MWNumericType.Int8 => "int8",
        MWNumericType.UInt8 => "uint8",
        MWNumericType.Int16 => "int16",
        MWNumericType.UInt16 => "uint16",
        MWNumericType.Int32 => "int32",
        MWNumericType.UInt32 => "uint32",
        MWNumericType.Int64 => "int64",
        MWNumericType.UInt64 => "uint64",
        _ => "double",
    };

    internal static MWNumericType TypeOf(string className) => className switch
    {
        "single" => MWNumericType.Single,
        "int8" => MWNumericType.Int8,
        "uint8" => MWNumericType.UInt8,
        "int16" => MWNumericType.Int16,
        "uint16" => MWNumericType.UInt16,
        "int32" => MWNumericType.Int32,
        "uint32" => MWNumericType.UInt32,
        "int64" => MWNumericType.Int64,
        "uint64" => MWNumericType.UInt64,
        _ => MWNumericType.Double,
    };

    /// <summary>The .NET element type of a class.</summary>
    internal static Type ElementType(MWNumericType type) => type switch
    {
        MWNumericType.Single => typeof(float),
        MWNumericType.Int8 => typeof(sbyte),
        MWNumericType.UInt8 => typeof(byte),
        MWNumericType.Int16 => typeof(short),
        MWNumericType.UInt16 => typeof(ushort),
        MWNumericType.Int32 => typeof(int),
        MWNumericType.UInt32 => typeof(uint),
        MWNumericType.Int64 => typeof(long),
        MWNumericType.UInt64 => typeof(ulong),
        _ => typeof(double),
    };

    /// <summary>A value as an element of <paramref name="type"/> holds it: rounded and saturated for an integer class.</summary>
    internal static double Held(MWNumericType type, double x)
    {
        (double low, double high) = type switch
        {
            MWNumericType.Int8 => (sbyte.MinValue, sbyte.MaxValue),
            MWNumericType.UInt8 => (byte.MinValue, byte.MaxValue),
            MWNumericType.Int16 => (short.MinValue, short.MaxValue),
            MWNumericType.UInt16 => (ushort.MinValue, ushort.MaxValue),
            MWNumericType.Int32 => (int.MinValue, int.MaxValue),
            MWNumericType.UInt32 => (uint.MinValue, uint.MaxValue),
            MWNumericType.Int64 => (long.MinValue, long.MaxValue),
            MWNumericType.UInt64 => (0, ulong.MaxValue),
            MWNumericType.Single => (double.NegativeInfinity, double.PositiveInfinity),
            _ => (double.NaN, double.NaN),
        };

        if (double.IsNaN(low))
        {
            return x;
        }

        if (type == MWNumericType.Single)
        {
            return (float)x;
        }

        return double.IsNaN(x) ? 0 : Math.Clamp(Math.Round(x, MidpointRounding.AwayFromZero), low, high);
    }

    private protected override MWArray GetElement(int[] indices)
    {
        CheckDisposed();
        int at = Linear(indices);
        return new MWNumericArray(_type, [1, 1], [_re[at]], _im is null ? null : [_im[at]]);
    }

    private protected override void SetElement(int[] indices, MWArray value)
    {
        CheckDisposed();
        if (value is not MWNumericArray number)
        {
            throw new ArgumentException("The value must be a numeric array");
        }

        int at = Linear(indices);
        _re[at] = Held(_type, number.FirstReal);
        if (number._im is { Length: > 0 } im && im[0] != 0)
        {
            _im ??= new double[_re.Length];
            _im[at] = Held(_type, im[0]);
        }
        else if (_im is not null)
        {
            _im[at] = 0;
        }
    }

    private double[] Part(MWArrayComponent component) => component == MWArrayComponent.Real ? _re : _im ?? new double[_re.Length];

    private double ScalarPart()
    {
        CheckDisposed();
        return _re.Length == 1 ? _re[0] : throw new Exception("Value must be a scalar");
    }

    /// <summary>The first element's real part, for an array of one of <paramref name="types"/>; R2025b's refusal otherwise.</summary>
    private double First(params MWNumericType[] types)
    {
        CheckDisposed();
        if (!types.Contains(_type) || _re.Length == 0)
        {
            throw new ApplicationException("Unexpected error - Invalid numeric array data type");
        }

        return _type == MWNumericType.Double && types[0] == MWNumericType.Int32 ? Math.Truncate(_re[0]) : _re[0];
    }

    private static int MaxOf(int[] values) => values is { Length: > 0 } ? values.Max() : 0;

    private static (MWNumericType, int[], double[], double[]) Flat<T>(int rows, int columns, T[] realData, T[] imaginaryData, bool makeDouble, bool rowMajorData)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(realData);
        if ((long)rows * columns != realData.Length)
        {
            throw new ArgumentException("The data array must have (row * columns) elements", nameof(realData));
        }

        if (imaginaryData is not null && imaginaryData.Length != realData.Length)
        {
            throw new ArgumentException("The data array must have (row * columns) elements", nameof(imaginaryData));
        }

        MWNumericType type = NativeConversion.ClassOfElement(typeof(T), makeDouble);
        double[] Read(T[] data)
        {
            var values = new double[data.Length];
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    int source = rowMajorData ? (r * columns) + c : r + (c * rows);
                    values[r + (c * rows)] = Held(type, Convert.ToDouble(data[source], System.Globalization.CultureInfo.InvariantCulture));
                }
            }

            return values;
        }

        return (type, [rows, columns], Read(realData), imaginaryData is null ? null : Read(imaginaryData));
    }

    private static (MWNumericType, int[], double[], double[]) FromArrays(Array realData, Array imaginaryData, bool makeDouble, bool rowMajorData)
    {
        ArgumentNullException.ThrowIfNull(realData);
        MWNumericType type = NativeConversion.ClassOfElement(realData.GetType().GetElementType(), makeDouble);
        (int[] dims, double[] re) = NativeConversion.Unshaped(realData, rowMajorData, x => Held(type, Convert.ToDouble(x, System.Globalization.CultureInfo.InvariantCulture)));
        double[] im = null;
        if (imaginaryData is not null)
        {
            (int[] imDims, im) = NativeConversion.Unshaped(imaginaryData, rowMajorData, x => Held(type, Convert.ToDouble(x, System.Globalization.CultureInfo.InvariantCulture)));
            if (!imDims.SequenceEqual(dims))
            {
                throw new ArgumentException("The real and imaginary arrays must be the same size");
            }
        }

        return (type, dims, re, im);
    }
}
