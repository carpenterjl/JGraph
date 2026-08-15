using System.IO;
using System.Numerics;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.MatFile.Hdf5;

namespace JGraph.Scripting.MatFile;

/// <summary>
/// Reads a version 7.3 MAT-file, which is an HDF5 file with MATLAB's conventions written on top of
/// it: every variable is a root-level dataset or group carrying a <c>MATLAB_class</c> attribute that
/// says what the bytes mean, and dimensions are stored reversed because HDF5 counts rows the other
/// way round from MATLAB.
/// </summary>
/// <remarks>
/// Reading only. Version 5 stays the only format JGraph writes (ADR 0065): a hand-rolled HDF5
/// writer would produce files MATLAB might silently mis-read, and nothing is gained by the risk.
/// </remarks>
internal static class MatV73Reader
{
    /// <summary>The groups MATLAB keeps its own bookkeeping in, which are not variables.</summary>
    private static readonly string[] Private = ["#refs#", "#subsystem#"];

    public static IReadOnlyList<(string Name, JgsValue Value)> Read(
        byte[] bytes, IReadOnlySet<string>? wanted = null)
    {
        Hdf5File file = Hdf5File.Open(bytes);
        var variables = new List<(string, JgsValue)>();
        foreach (string name in file.Root.ChildNames)
        {
            if (Private.Contains(name, StringComparer.Ordinal) || (wanted is not null && !wanted.Contains(name)))
            {
                continue;
            }

            Hdf5Object? child = file.Root.Child(name);
            if (child is not null)
            {
                variables.Add((name, ToValue(child, name)));
            }
        }

        return variables;
    }

    private static JgsValue ToValue(Hdf5Object node, string name)
    {
        string matlabClass = node.Attributes.TryGetValue("MATLAB_class", out Hdf5Attribute? tag)
            ? tag.AsText()
            : node.IsGroup ? "struct" : "double";

        if (node.Attributes.TryGetValue("MATLAB_empty", out Hdf5Attribute? empty) && empty.AsNumber() != 0)
        {
            return matlabClass == "char" ? JgsValue.Str(string.Empty) : JgsValue.Array([]);
        }

        if (node.IsGroup)
        {
            return matlabClass == "struct"
                ? ReadStruct(node, name)
                : throw new InvalidDataException(
                    $"MAT-file variable '{name}' holds a {matlabClass}, which cannot be loaded.");
        }

        return matlabClass switch
        {
            "char" => ReadChar(node),
            "cell" => ReadCell(node),
            "logical" => ReadLogical(node),
            "string" => throw new InvalidDataException(
                $"MAT-file variable '{name}' holds a string array, which cannot be loaded yet."),
            "double" or "single" or "int8" or "uint8" or "int16" or "uint16"
                or "int32" or "uint32" or "int64" or "uint64" => ReadNumeric(node, matlabClass),
            _ => throw new InvalidDataException(
                $"MAT-file variable '{name}' holds a {matlabClass}, which cannot be loaded."),
        };
    }

    /// <summary>
    /// The MATLAB shape a stored dataspace means: reversed, because an m-by-n matrix is written
    /// n-by-m. A rank of one is a row vector, which is the shape MATLAB gives such a dataset back.
    /// </summary>
    private static int[] Shape(Hdf5Object node)
    {
        IReadOnlyList<long> dims = node.Dims;
        if (dims.Count == 0)
        {
            return [1, 1];
        }

        if (dims.Count == 1)
        {
            return [1, (int)dims[0]];
        }

        var shape = new int[dims.Count];
        for (int i = 0; i < dims.Count; i++)
        {
            shape[i] = (int)dims[dims.Count - 1 - i];
        }

        return shape;
    }

    /// <summary>
    /// Lays elements out in the shape the dataspace gives. Reversed dimensions plus HDF5's own
    /// row-major order come out as MATLAB column-major order already, so nothing is transposed here.
    /// </summary>
    private static JgsValue Lay(JgsValue[] elements, int[] shape)
    {
        int rows = shape[0];
        int cols = elements.Length == 0 || rows == 0 ? 0 : elements.Length / rows;
        if (rows == 1 && cols == 1 && elements.Length == 1)
        {
            return elements[0];
        }

        JgsValue value = JgsMatrix.FromElements(elements, rows, cols);
        if (shape.Length > 2)
        {
            value.ReshapeDims(shape);
        }

        return value;
    }

    private static JgsValue ReadNumeric(Hdf5Object node, string matlabClass)
    {
        if (node.Attributes.TryGetValue("MATLAB_complex", out Hdf5Attribute? complex) && complex.AsNumber() != 0)
        {
            return ReadComplex(node);
        }

        double[] values = Numbers(node);
        int[] shape = Shape(node);
        if (values.Length == 1 && shape[0] == 1 && shape.Length == 2 && shape[1] == 1)
        {
            JgsValue scalar = JgsValue.Number(values[0]);
            scalar.SetNumericClass(NumericClassOf(matlabClass));
            return scalar;
        }

        int rows = shape[0];
        int cols = rows == 0 ? 0 : values.Length / rows;
        JgsValue value = JgsMatrix.FromColumnMajor(values, rows, cols);
        if (shape.Length > 2)
        {
            value.ReshapeDims(shape);
        }

        value.SetNumericClass(NumericClassOf(matlabClass));
        return value;
    }

    private static JgsValue ReadComplex(Hdf5Object node)
    {
        Hdf5Datatype type = node.Datatype;
        if (type.Class != Hdf5Datatype.ClassCompound || type.Members.Count != 2)
        {
            throw new InvalidDataException("Malformed version 7.3 MAT-file: a complex variable has no parts.");
        }

        (string _, int realAt, Hdf5Datatype realType) = type.Members[0];
        (string _, int imagAt, Hdf5Datatype imagType) = type.Members[1];
        byte[] data = node.ReadData();
        int count = (int)node.ElementCount;
        var elements = new JgsValue[count];
        for (int i = 0; i < count; i++)
        {
            double re = realType.ElementAt(data, (i * type.Size) + realAt);
            double im = imagType.ElementAt(data, (i * type.Size) + imagAt);
            elements[i] = im == 0 ? JgsValue.Number(re) : JgsValue.ComplexNum(new Complex(re, im));
        }

        return Lay(elements, Shape(node));
    }

    private static JgsValue ReadLogical(Hdf5Object node)
    {
        double[] values = Numbers(node);
        var elements = new JgsValue[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            elements[i] = JgsValue.Bool(values[i] != 0);
        }

        return Lay(elements, Shape(node));
    }

    private static JgsValue ReadChar(Hdf5Object node)
    {
        Hdf5Datatype type = node.Datatype;
        byte[] data = node.ReadData();
        int count = (int)node.ElementCount;
        var text = new char[count];
        for (int i = 0; i < count; i++)
        {
            text[i] = (char)type.UnsignedAt(data, i * type.Size);
        }

        int[] shape = Shape(node);
        int rows = shape[0];
        if (rows <= 1 || shape.Length > 2 || count % rows != 0)
        {
            return JgsValue.Str(new string(text));
        }

        // Characters arrive column by column, and a char matrix is kept as a column of equal-length
        // rows, so this is the one place a version 7.3 read genuinely transposes.
        int width = count / rows;
        var lines = new JgsValue[rows];
        for (int r = 0; r < rows; r++)
        {
            var row = new char[width];
            for (int c = 0; c < width; c++)
            {
                row[c] = text[(c * rows) + r];
            }

            lines[r] = JgsValue.Str(new string(row));
        }

        JgsValue matrix = JgsValue.Array(lines);
        matrix.Reshape(rows, 1);
        return matrix;
    }

    private static JgsValue ReadCell(Hdf5Object node)
    {
        JgsValue[] elements = Referenced(node);
        JgsValue cell = JgsValue.Cell(elements);
        int[] shape = Shape(node);
        if (shape.Length > 2 || shape[0] * shape[1] == elements.Length)
        {
            cell.ReshapeDims(shape);
        }

        return cell;
    }

    /// <summary>Follows a dataset of object references, reading whatever each one points at.</summary>
    private static JgsValue[] Referenced(Hdf5Object node)
    {
        Hdf5Datatype type = node.Datatype;
        if (type.Class != Hdf5Datatype.ClassReference)
        {
            throw new InvalidDataException("Malformed version 7.3 MAT-file: a cell holds no references.");
        }

        byte[] data = node.ReadData();
        int count = (int)node.ElementCount;
        var elements = new JgsValue[count];
        for (int i = 0; i < count; i++)
        {
            long address = Hdf5File.ReadWord(data, i * type.Size, type.Size);
            elements[i] = ToValue(node.At(address), "a cell element");
        }

        return elements;
    }

    private static JgsValue ReadStruct(Hdf5Object node, string name)
    {
        IReadOnlyList<string> fields = node.Attributes.TryGetValue("MATLAB_fields", out Hdf5Attribute? declared)
            ? declared.Texts()
            : [.. node.ChildNames];

        var children = new List<(string Field, Hdf5Object Node)>();
        foreach (string field in fields)
        {
            Hdf5Object? child = node.Child(field)
                ?? throw new InvalidDataException(
                    $"MAT-file variable '{name}' declares a field '{field}' it does not hold.");
            children.Add((field, child));
        }

        if (StructArrayShape(children) is int[] shape)
        {
            int count = shape.Aggregate(1, static (total, dim) => total * dim);
            var elements = new Dictionary<string, JgsValue>[count];
            for (int e = 0; e < count; e++)
            {
                elements[e] = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
            }

            foreach ((string field, Hdf5Object child) in children)
            {
                JgsValue[] values = Referenced(child);
                for (int e = 0; e < count && e < values.Length; e++)
                {
                    elements[e][field] = values[e];
                }
            }

            return JgsValue.StructArray(
                new JgsStructArray(elements, [.. children.Select(static c => c.Field)]), shape[0], count / Math.Max(shape[0], 1));
        }

        var scalar = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        foreach ((string field, Hdf5Object child) in children)
        {
            scalar[field] = ToValue(child, field);
        }

        return JgsValue.Struct(scalar);
    }

    /// <summary>
    /// The shape a struct array is stored with, or null for a scalar struct. MATLAB gives a struct
    /// array's fields one reference per element and a scalar struct's fields their values directly,
    /// so more than one reference in every field is what a struct array looks like from here. A
    /// scalar struct whose every field is an equally shaped cell looks the same and is read as an
    /// array — the one ambiguity the format leaves, recorded in ADR 0065.
    /// </summary>
    private static int[]? StructArrayShape(List<(string Field, Hdf5Object Node)> children)
    {
        int[]? shape = null;
        foreach ((string _, Hdf5Object child) in children)
        {
            if (child.IsGroup || child.Datatype.Class != Hdf5Datatype.ClassReference || child.ElementCount <= 1)
            {
                return null;
            }

            int[] mine = Shape(child);
            if (shape is null)
            {
                shape = mine;
            }
            else if (!shape.SequenceEqual(mine))
            {
                return null;
            }
        }

        return shape;
    }

    private static double[] Numbers(Hdf5Object node)
    {
        Hdf5Datatype type = node.Datatype;
        if (!type.IsNumeric)
        {
            throw new InvalidDataException(
                "This version 7.3 MAT-file stores a number in a form that cannot be read.");
        }

        byte[] data = node.ReadData();
        int count = (int)node.ElementCount;
        var values = new double[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = type.ElementAt(data, i * type.Size);
        }

        return values;
    }

    private static JgsNumericClass NumericClassOf(string matlabClass) => matlabClass switch
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
}
