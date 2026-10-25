using System.Globalization;
using JGraph.Runtime;
using NativeCell = JGraph.MATLAB.NET.Arrays.native.MWCellArray;
using NativeStruct = JGraph.MATLAB.NET.Arrays.native.MWStructArray;

namespace JGraph.MATLAB.NET.Arrays;

/// <summary>
/// The .NET shapes of MATLAB arrays (compiler and SDK plan, stage K8), as R2025b's MWArray API reads
/// and writes them (<c>k8_client</c>): a .NET array's last two dimensions are MATLAB's rows and
/// columns and its earlier ones MATLAB's pages, last first (<c>double[2,3,4]</c> is 3-by-4-by-2); a
/// vector is a row; the native API answers every numeric, logical and char value as an array of at
/// least two dimensions, a string array as a vector, a cell or struct as the native API's own.
/// </summary>
internal static class NativeConversion
{
    private const string Unsupported = "Data type unsupported by MATLAB .NET Assembly";

    /// <summary>The class a .NET element type makes: double with <paramref name="makeDouble"/>, else its own; R2025b's refusal for the rest.</summary>
    internal static MWNumericType ClassOfElement(Type element, bool makeDouble)
    {
        MWNumericType own = element == typeof(double) ? MWNumericType.Double
            : element == typeof(float) ? MWNumericType.Single
            : element == typeof(int) ? MWNumericType.Int32
            : element == typeof(long) ? MWNumericType.Int64
            : element == typeof(short) ? MWNumericType.Int16
            : element == typeof(byte) ? MWNumericType.UInt8
            : throw new ArgumentException("The data array type is invalid");
        return makeDouble ? MWNumericType.Double : own;
    }

    /// <summary>The C# lengths of an array of MATLAB size <paramref name="dims"/>.</summary>
    internal static int[] CsLengths(int[] dims)
    {
        int k = dims.Length;
        var lengths = new int[k];
        lengths[k - 2] = dims[0];
        lengths[k - 1] = dims[1];
        for (int d = 2; d < k; d++)
        {
            lengths[k - 1 - d] = dims[d];
        }

        return lengths;
    }

    /// <summary>The C# index of MATLAB's 0-based column-major element <paramref name="linear"/>.</summary>
    internal static int[] CsIndex(int[] dims, int linear)
    {
        int k = dims.Length;
        var index = new int[k];
        for (int d = 0; d < k; d++)
        {
            int sub = linear % dims[d];
            linear /= dims[d];
            index[d < 2 ? k - 2 + d : k - 1 - d] = sub;
        }

        return index;
    }

    /// <summary>A .NET array shaped as MATLAB's <paramref name="dims"/>, each element from <paramref name="at"/> (0-based column-major).</summary>
    internal static Array Shaped(int[] dims, Type element, Func<int, object> at)
    {
        Array array = Array.CreateInstance(element, CsLengths(dims));
        int count = MatlabDisplay.Count(dims);
        for (int i = 0; i < count; i++)
        {
            array.SetValue(at(i), CsIndex(dims, i));
        }

        return array;
    }

    /// <summary>A .NET array's MATLAB size and its elements in column-major order, read by rows unless <paramref name="rowMajor"/> is false.</summary>
    internal static (int[] Dims, object[] Values) UnshapedObjects(Array array, bool rowMajor)
    {
        int k = array.Rank;
        int[] lengths = [.. Enumerable.Range(0, k).Select(array.GetLength)];
        int[] dims;
        if (k == 1)
        {
            dims = [1, lengths[0]];
        }
        else
        {
            dims = new int[k];
            dims[0] = lengths[k - 2];
            dims[1] = lengths[k - 1];
            for (int d = 2; d < k; d++)
            {
                dims[d] = lengths[k - 1 - d];
            }
        }

        var values = new object[array.Length];
        var index = new int[k];
        for (int n = 0; n < array.Length; n++)
        {
            object value = array.GetValue(index);
            int target = n;
            if (rowMajor && k > 1)
            {
                // The C# index's MATLAB subscripts, in column-major order.
                target = 0;
                int stride = 1;
                for (int d = 0; d < k; d++)
                {
                    int sub = d < 2 ? index[k - 2 + d] : index[k - 1 - d];
                    target += sub * stride;
                    stride *= dims[d];
                }
            }

            values[target] = value;
            for (int d = k - 1; d >= 0; d--)
            {
                if (++index[d] < lengths[d])
                {
                    break;
                }

                index[d] = 0;
            }
        }

        return (dims, values);
    }

    /// <summary>As <see cref="UnshapedObjects"/>, each element converted.</summary>
    internal static (int[] Dims, double[] Values) Unshaped(Array array, bool rowMajor, Func<object, double> convert)
    {
        (int[] dims, object[] values) = UnshapedObjects(array, rowMajor);
        return (dims, [.. values.Select(convert)]);
    }

    /// <summary>A boxed element of <paramref name="element"/> holding <paramref name="value"/>.</summary>
    internal static object Element(Type element, double value) =>
        element == typeof(double) ? value : Convert.ChangeType(value, element, CultureInfo.InvariantCulture);

    /// <summary>A dense column-major matrix in compressed sparse column form.</summary>
    internal static (int[] Rows, int[] Starts, double[] Values) Compressed(int rows, int cols, double[] dense)
    {
        var rowIndices = new List<int>();
        var values = new List<double>();
        var starts = new int[cols + 1];
        for (int c = 0; c < cols; c++)
        {
            starts[c] = values.Count;
            for (int r = 0; r < rows; r++)
            {
                double x = dense[r + (c * rows)];
                if (x != 0)
                {
                    rowIndices.Add(r);
                    values.Add(x);
                }
            }
        }

        starts[cols] = values.Count;
        return ([.. rowIndices], starts, [.. values]);
    }

    /// <summary>A value as the native API answers it.</summary>
    internal static object ToNative(RuntimeValue value)
    {
        int[] dims = [.. value.Dimensions];
        switch (value)
        {
            case RuntimeNumeric numeric:
            {
                // The native API has no .NET type for a complex value or for int8, uint16, uint32 and
                // uint64, which it does not take either (k8_client: native.make.complex).
                MWNumericType type = MWNumericArray.TypeOf(numeric.ClassName);
                if (numeric.IsComplex || type is MWNumericType.Int8 or MWNumericType.UInt16 or MWNumericType.UInt32 or MWNumericType.UInt64)
                {
                    throw new InvalidDataException(Unsupported + " for output");
                }

                Type element = MWNumericArray.ElementType(type);
                return Shaped(dims, element, i => Element(element, numeric.Real[i]));
            }

            case RuntimeSparse sparse:
            {
                var dense = new double[sparse.Count];
                for (int c = 0; c < dims[1]; c++)
                {
                    for (int k = sparse.ColumnStarts[c]; k < sparse.ColumnStarts[c + 1]; k++)
                    {
                        dense[sparse.RowIndices[k] + (c * dims[0])] = sparse.Values[k];
                    }
                }

                return Shaped(dims, typeof(double), i => dense[i]);
            }

            case RuntimeLogical logical:
                return Shaped(dims, typeof(bool), i => logical.Elements[i]);
            case RuntimeChar text:
                return Shaped(dims, typeof(char), i => text.Characters[i]);
            case RuntimeString strings:
                return strings.Elements.ToArray();
            case RuntimeCell cell:
            {
                var made = new NativeCell(dims);
                for (int i = 0; i < cell.Count; i++)
                {
                    made[i + 1] = ToNative(cell.Elements[i]);
                }

                return made;
            }

            case RuntimeStruct record:
            {
                var made = new NativeStruct(dims, [.. record.FieldNames]);
                for (int e = 0; e < record.Count; e++)
                {
                    foreach (string field in record.FieldNames)
                    {
                        made[field, e + 1] = ToNative(record.Elements[e][field]);
                    }
                }

                return made;
            }

            default:
                throw new InvalidDataException(Unsupported + " for conversion to array");
        }
    }

    /// <summary>A .NET value a native API call was given, as the runtime passes it (<c>k8_client</c>: <c>native.echo.*</c>).</summary>
    internal static RuntimeValue FromNative(object value)
    {
        switch (value)
        {
            case null:
                throw new NullReferenceException();
            case MWArray array:
                return array.ToRuntime();
            case NativeCell cell:
                return new RuntimeCell([.. cell.Cells.Select(FromNative)], cell.Dimensions);
            case NativeStruct record:
            {
                int count = record.NumberOfElements;
                var elements = new IReadOnlyDictionary<string, RuntimeValue>[count];
                for (int e = 0; e < count; e++)
                {
                    int element = e;
                    elements[e] = record.Names.Select((n, f) => (n, f)).ToDictionary(p => p.n, p => FromNative(record.At(element, p.f)), StringComparer.Ordinal);
                }

                return new RuntimeStruct([.. record.Names], elements, record.Dimensions);
            }

            case string text:
                return RuntimeChar.Row(text);
            case char single:
                return new RuntimeChar(single.ToString(), [1, 1]);
            case bool flag:
                return RuntimeValue.Scalar(flag);
            case Array array:
                return FromNativeArray(array);
        }

        Type type = value.GetType();
        if (type == typeof(double) || type == typeof(float) || type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte))
        {
            MWNumericType numeric = ClassOfElement(type, makeDouble: false);
            return new RuntimeNumeric(MWNumericArray.ClassName(numeric), [Convert.ToDouble(value, CultureInfo.InvariantCulture)], null, [1, 1]);
        }

        throw new InvalidDataException(Unsupported);
    }

    private static RuntimeValue FromNativeArray(Array array)
    {
        Type element = array.GetType().GetElementType();
        if (element == typeof(string))
        {
            var text = new MWCharArray(array);
            return text.ToRuntime();
        }

        (int[] dims, object[] values) = UnshapedObjects(array, rowMajor: true);
        if (element == typeof(char))
        {
            return new RuntimeChar(new string([.. values.Select(static v => (char)v)]), dims);
        }

        if (element == typeof(bool))
        {
            return new RuntimeLogical([.. values.Select(static v => (bool)v)], dims);
        }

        if (element == typeof(object))
        {
            return new RuntimeCell([.. values.Select(FromNative)], dims);
        }

        MWNumericType numeric;
        try
        {
            numeric = ClassOfElement(element, makeDouble: false);
        }
        catch (ArgumentException)
        {
            throw new InvalidDataException(Unsupported);
        }

        return new RuntimeNumeric(MWNumericArray.ClassName(numeric), [.. values.Select(static v => Convert.ToDouble(v, CultureInfo.InvariantCulture))], null, dims);
    }

    /// <summary>An array as the .NET type a client asked for (R2025b's <c>ConvertToNativeTypes</c>).</summary>
    internal static object ToType(MWArray array, Type type)
    {
        ArgumentNullException.ThrowIfNull(array);
        RuntimeValue value = array.ToRuntime();
        if (type is null)
        {
            return ToNative(value);
        }

        Exception Refusal() => new ArgumentException("Cannot convert MWArray to requested type",
            new ApplicationException($"Cannot convert from {array.GetType().Name}[,] to {type.FullName}"));

        object native;
        try
        {
            native = ToNative(value);
        }
        catch (InvalidDataException)
        {
            throw Refusal();
        }

        if (type.IsInstanceOfType(native))
        {
            return native;
        }

        if (type == typeof(string) && value is RuntimeChar text)
        {
            return string.Join("\n", text.Rows);
        }

        if (native is Array shaped && shaped.GetType().GetElementType() is { } element)
        {
            object[] flat = [.. Enumerable.Range(0, value.Count).Select(i => shaped.GetValue(CsIndex([.. value.Dimensions], i)))];
            if (type.IsArray && type.GetArrayRank() == 1 && type.GetElementType() is { } wanted && Convertible(element, wanted))
            {
                var vector = Array.CreateInstance(wanted, flat.Length);
                for (int i = 0; i < flat.Length; i++)
                {
                    vector.SetValue(Convert.ChangeType(flat[i], wanted, CultureInfo.InvariantCulture), i);
                }

                return vector;
            }

            if (!type.IsArray && flat.Length >= 1 && Convertible(element, type))
            {
                return Convert.ChangeType(flat[0], type, CultureInfo.InvariantCulture);
            }
        }

        throw Refusal();
    }

    private static bool Convertible(Type from, Type to) =>
        from == to || (from != typeof(char) && to != typeof(char) && from != typeof(bool) && to != typeof(bool) && from.IsPrimitive && to.IsPrimitive);
}
