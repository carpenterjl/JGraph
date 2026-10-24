using System.Numerics;
using JGraph.Scripting.Jgs;

namespace JGraph.Runtime;

/// <summary>
/// The conversions between <see cref="RuntimeValue"/> and the engine's <see cref="JgsValue"/>. A value
/// coming in is minted fresh, sharing nothing with the caller's (the value-ownership rules of ADRs
/// 0162-0164 then hold for it as for any value a script made); a value going out is copied out of
/// the engine's storage, so nothing the host holds aliases the session.
/// </summary>
internal static class RuntimeValueConversion
{
    /// <summary>The engine's value for <paramref name="value"/>, which <paramref name="owner"/> is to receive.</summary>
    /// <exception cref="ArgumentException">An opaque value another component returned.</exception>
    public static JgsValue ToEngine(RuntimeValue value, object owner)
    {
        ArgumentNullException.ThrowIfNull(value);
        IReadOnlyList<int> dims = value.Dimensions;
        switch (value)
        {
            case RuntimeOpaque opaque:
                return ReferenceEquals(opaque.Owner, owner)
                    ? (JgsValue)opaque.Held
                    : throw new ArgumentException($"A {opaque.ClassName} value can only be passed back to the component that returned it.", nameof(value));

            case RuntimeNumeric numeric:
                return Numeric(numeric, dims);

            case RuntimeLogical logical:
                if (logical.Count == 1 && IsScalarShape(dims))
                {
                    return JgsValue.Bool(logical.Elements[0]);
                }

                return JgsMatrix.FromElementsDims([.. logical.Elements.Select(JgsValue.Bool)], dims);

            case RuntimeChar text:
                return Char(text, dims);

            case RuntimeString strings:
            {
                JgsValue[] elements = [.. strings.Elements.Select(static s => JgsValue.Str(s ?? JgsBuiltins.MissingSentinel))];
                JgsValue array = JgsValue.StringArray(elements, dims[0], dims.Count == 2 ? dims[1] : strings.Count / Math.Max(dims[0], 1));
                if (dims.Count > 2)
                {
                    array.ReshapeDims(dims);
                }

                return array;
            }

            case RuntimeCell cell:
            {
                JgsValue made = JgsValue.Cell([.. cell.Elements.Select(e => ToEngine(e, owner))]);
                made.ReshapeDims(dims);
                return made;
            }

            case RuntimeStruct record:
                return Struct(record, dims, owner);

            case RuntimeSparse sparse:
            {
                var triplets = new List<(int Row, int Col, double Value)>(sparse.Values.Count);
                for (int c = 0; c < dims[1]; c++)
                {
                    for (int k = sparse.ColumnStarts[c]; k < sparse.ColumnStarts[c + 1]; k++)
                    {
                        triplets.Add((sparse.RowIndices[k], c, sparse.Values[k]));
                    }
                }

                return JgsValue.Sparse(JGraph.Numerics.Sparse.CscMatrix.FromTriplets(dims[0], dims[1], triplets));
            }

            default:
                throw new ArgumentException($"A {value.GetType().Name} is not a value the runtime knows.", nameof(value));
        }
    }

    /// <summary>The host's value for the engine's <paramref name="value"/>, returned by <paramref name="owner"/>.</summary>
    public static RuntimeValue FromEngine(JgsValue value, object owner)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Type == JgsType.Null)
        {
            return RuntimeValue.Empty();
        }

        string cls = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
        int[] dims = JgsBuiltins.SizeDims(value);
        int count = Count(dims);

        if (value.IsStringArray)
        {
            return new RuntimeString(
                [.. Enumerable.Range(0, count).Select(i => value.ElementAt(i).AsString is var s && s == JgsBuiltins.MissingSentinel ? null : s)],
                dims);
        }

        switch (cls)
        {
            case "char" when value.IsCharMatrix:
                return new RuntimeChar(value.CharMatrixText(), dims);
            case "char" when value.Type == JgsType.String:
                return new RuntimeChar(value.AsString, dims);
            case "logical" when value.Type is JgsType.Bool or JgsType.Array:
                return new RuntimeLogical([.. Elements(value, dims).Select(static e => e.AsNumber != 0)], dims);
            case "double" when value.Type == JgsType.Sparse:
            {
                JGraph.Numerics.Sparse.CscMatrix matrix = value.AsSparse;
                int stored = matrix.NonZeroCount;
                return new RuntimeSparse(
                    matrix.Rows, matrix.Cols,
                    matrix.RowIndices.AsSpan(0, stored).ToArray(), matrix.ColumnStarts, matrix.Values.AsSpan(0, stored).ToArray());
            }

            case "cell" when value.Type == JgsType.Cell:
                return new RuntimeCell([.. value.AsCell.Select(e => FromEngine(e, owner))], dims);
            case "struct" when value.Type == JgsType.Struct:
            {
                JgsStructArray payload = value.AsStructArray;
                string[] names = payload.FieldNames;
                return new RuntimeStruct(
                    names,
                    [.. payload.Elements.Select(e => (IReadOnlyDictionary<string, RuntimeValue>)names.ToDictionary(n => n, n => FromEngine(e[n], owner), StringComparer.Ordinal))],
                    dims);
            }
        }

        if (RuntimeNumeric.Classes.Contains(cls) && value.Type is JgsType.Number or JgsType.Complex or JgsType.Array
            && !value.IsTime && !value.IsCategorical && !value.IsCharMatrix)
        {
            JgsValue[] elements = Elements(value, dims);
            double[] real = new double[count];
            double[]? imaginary = null;
            for (int i = 0; i < count; i++)
            {
                JgsValue element = elements[i];
                if (element.Type == JgsType.Complex)
                {
                    imaginary ??= new double[count];
                    Complex c = element.AsComplex;
                    real[i] = c.Real;
                    imaginary[i] = c.Imaginary;
                }
                else
                {
                    real[i] = element.AsNumber;
                }
            }

            // A complex array is double or single; JGraph keeps no complex integer.
            return new RuntimeNumeric(imaginary is not null && cls is not ("double" or "single") ? "double" : cls, real, imaginary, dims);
        }

        return new RuntimeOpaque(cls, dims, owner, value);
    }

    private static bool IsScalarShape(IReadOnlyList<int> dims) => dims.Count == 2 && dims[0] == 1 && dims[1] == 1;

    private static int Count(IReadOnlyList<int> dims)
    {
        int count = 1;
        foreach (int d in dims)
        {
            count *= d;
        }

        return count;
    }

    /// <summary>
    /// The elements in column-major order. A matrix built as an array of row arrays, the shape older
    /// code produces, is read down its columns as MAT-file saving reads it.
    /// </summary>
    private static JgsValue[] Elements(JgsValue value, int[] dims)
    {
        int count = Count(dims);
        if (value.Type is JgsType.Number or JgsType.Bool or JgsType.Complex)
        {
            return [value];
        }

        if (dims.Length == 2 && value.ArrayLength != count && JgsMatrix.IsMatrix(value))
        {
            var flat = new JgsValue[count];
            for (int c = 0; c < dims[1]; c++)
            {
                for (int r = 0; r < dims[0]; r++)
                {
                    flat[r + (c * dims[0])] = JgsMatrix.At(value, r, c);
                }
            }

            return flat;
        }

        var elements = new JgsValue[count];
        for (int i = 0; i < count; i++)
        {
            elements[i] = value.ElementAt(i);
        }

        return elements;
    }

    private static JgsValue Numeric(RuntimeNumeric numeric, IReadOnlyList<int> dims)
    {
        JgsNumericClass numericClass = numeric.ClassName switch
        {
            "single" => JgsNumericClass.Single,
            "int8" => JgsNumericClass.Int8,
            "uint8" => JgsNumericClass.UInt8,
            "int16" => JgsNumericClass.Int16,
            "uint16" => JgsNumericClass.UInt16,
            "int32" => JgsNumericClass.Int32,
            "uint32" => JgsNumericClass.UInt32,
            "int64" => JgsNumericClass.Int64,
            "uint64" => JgsNumericClass.UInt64,
            _ => JgsNumericClass.Double,
        };

        JgsValue made;
        if (numeric.Imaginary is { } imaginary)
        {
            // JGraph keeps an element with no imaginary part as a real number, as MAT-file loading does.
            JgsValue[] elements =
            [
                .. numeric.Real.Select((re, i) => imaginary[i] == 0 ? JgsValue.Number(re) : JgsValue.ComplexNum(new Complex(re, imaginary[i]))),
            ];
            made = numeric.Count == 1 && IsScalarShape(dims) ? elements[0] : JgsMatrix.FromElementsDims(elements, dims);
        }
        else
        {
            made = numeric.Count == 1 && IsScalarShape(dims)
                ? JgsValue.Number(numeric.Real[0])
                : JgsMatrix.FromColumnMajorDims([.. numeric.Real], dims);
        }

        made.SetNumericClass(numericClass);
        return made;
    }

    private static JgsValue Char(RuntimeChar text, IReadOnlyList<int> dims)
    {
        if (dims.Count == 2 && dims[0] <= 1)
        {
            return JgsValue.Str(text.Characters);
        }

        if (dims.Count == 2)
        {
            return JgsValue.CharMatrix([.. text.Rows]);
        }

        JgsValue codes = JgsMatrix.FromColumnMajorDims([.. text.Characters.Select(static c => (double)c)], dims);
        return codes.MarkCharMatrix();
    }

    private static JgsValue Struct(RuntimeStruct record, IReadOnlyList<int> dims, object owner)
    {
        Dictionary<string, JgsValue>[] elements =
        [
            .. record.Elements.Select(e =>
            {
                var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
                foreach (string name in record.FieldNames)
                {
                    fields[name] = ToEngine(e[name], owner);
                }

                return fields;
            }),
        ];

        if (elements.Length == 1 && IsScalarShape(dims))
        {
            return JgsValue.Struct(elements[0]);
        }

        JgsValue array = JgsValue.StructArray(
            new JgsStructArray(elements, [.. record.FieldNames]), dims[0], dims.Count == 2 ? dims[1] : elements.Length / Math.Max(dims[0], 1));
        if (dims.Count > 2)
        {
            array.ReshapeDims(dims);
        }

        return array;
    }
}
