using System.Text;

namespace JGraph.MATLAB.NET.Arrays.native;

/// <summary>What kind of array a native API array holds.</summary>
public enum MWArrayType
{
    /// <summary>A value of no other kind.</summary>
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

    /// <summary>A struct array.</summary>
    Structure = 6,
}

/// <summary>
/// The native API's arrays (compiler and SDK plan, stage K8): cells and structs of plain .NET values,
/// which a native class's functions take and answer where the native API has no .NET type of its own.
/// </summary>
public abstract class MWArray : ICloneable
{
    private protected MWArray(int[] dimensions)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        Dims = dimensions.Length >= 2 ? (int[])dimensions.Clone() : dimensions.Length == 1 ? [1, dimensions[0]] : [0, 0];
    }

    /// <summary>What kind of array this is.</summary>
    public abstract MWArrayType ArrayType { get; }

    /// <summary>The size of each dimension.</summary>
    public int[] Dimensions => (int[])Dims.Clone();

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

    /// <summary>Whether the array has no elements.</summary>
    public bool IsEmpty => NumberOfElements == 0;

    /// <summary>The number of dimensions.</summary>
    public int NumberofDimensions => Dims.Length;

    /// <summary>The number of elements.</summary>
    public int NumberOfElements => Dims.Aggregate(1, static (n, d) => n * d);

    private protected int[] Dims { get; }

    /// <summary>A copy.</summary>
    public abstract object Clone();

    /// <inheritdoc />
    public override bool Equals(object obj) => ReferenceEquals(this, obj);

    /// <inheritdoc />
    public override int GetHashCode() => base.GetHashCode();

    /// <summary>The size as R2025b's native arrays write it: <c>1x2</c>.</summary>
    private protected string Size => string.Join("x", Dims);

    /// <summary>The 0-based element at MATLAB's 1-based subscripts.</summary>
    private protected int Linear(int[] indices)
    {
        ArgumentNullException.ThrowIfNull(indices);
        if (indices.Length == 1)
        {
            return indices[0] >= 1 && indices[0] <= NumberOfElements ? indices[0] - 1 : throw new Exception("Invalid index");
        }

        if (indices.Length != Dims.Length)
        {
            throw new Exception("Invalid index");
        }

        int linear = 0;
        int stride = 1;
        for (int d = 0; d < Dims.Length; d++)
        {
            if (indices[d] < 1 || indices[d] > Dims[d])
            {
                throw new Exception("Invalid index");
            }

            linear += (indices[d] - 1) * stride;
            stride *= Dims[d];
        }

        return linear;
    }
}

/// <summary>A native API cell array: its elements are .NET values, the empty double's <c>double[0,0]</c> where none was set.</summary>
public class MWCellArray : MWArray, IEquatable<MWCellArray>
{
    private readonly object[] _cells;

    /// <summary>The empty cell.</summary>
    public MWCellArray()
        : this([0, 0])
    {
    }

    /// <summary>A rows-by-columns cell.</summary>
    public MWCellArray(int rows, int columns)
        : this([rows, columns])
    {
    }

    /// <summary>A cell of this size.</summary>
    public MWCellArray(int[] dimensions)
        : base(dimensions)
    {
        _cells = new object[NumberOfElements];
        for (int i = 0; i < _cells.Length; i++)
        {
            _cells[i] = new double[0, 0];
        }
    }

    /// <summary>An element at MATLAB's 1-based subscripts.</summary>
    public object this[params int[] indices]
    {
        get => _cells[Linear(indices)];
        set => _cells[Linear(indices)] = value;
    }

    /// <summary>The empty cell.</summary>
    public static MWCellArray Empty => new();

    /// <inheritdoc />
    public override MWArrayType ArrayType => MWArrayType.Cell;

    internal IReadOnlyList<object> Cells => _cells;

    /// <inheritdoc />
    public override object Clone()
    {
        var copy = new MWCellArray(Dimensions);
        Array.Copy(_cells, copy._cells, _cells.Length);
        return copy;
    }

    /// <summary>Whether both are the same cell.</summary>
    public bool Equals(MWCellArray other) => ReferenceEquals(this, other);

    /// <inheritdoc />
    public override bool Equals(object obj) => obj is MWCellArray other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => base.GetHashCode();

    /// <summary>R2025b's text: <c>1x3 cell array</c>.</summary>
    public override string ToString() => Size + " cell array";
}

/// <summary>A native API struct array: its fields are .NET values.</summary>
public class MWStructArray : MWArray, IEquatable<MWStructArray>
{
    private readonly List<string> _names;
    private readonly List<object>[] _elements;

    /// <summary>The empty struct.</summary>
    public MWStructArray()
        : this([0, 0], [])
    {
    }

    /// <summary>A rows-by-columns struct array with these fields.</summary>
    public MWStructArray(int rows, int cols, string[] fNames)
        : this([rows, cols], fNames)
    {
    }

    /// <summary>A struct array of this size with these fields, each the empty double.</summary>
    public MWStructArray(int[] inDims, string[] fNames)
        : base(inDims)
    {
        ArgumentNullException.ThrowIfNull(fNames);
        _names = [.. fNames];
        _elements = new List<object>[NumberOfElements];
        for (int e = 0; e < _elements.Length; e++)
        {
            _elements[e] = [.. _names.Select(static _ => (object)new double[0, 0])];
        }
    }

    /// <summary>A 1-by-1 struct of name, value pairs.</summary>
    public MWStructArray(object[] fieldDefs)
        : this([1, 1], [])
    {
        ArgumentNullException.ThrowIfNull(fieldDefs);
        for (int i = 0; i + 1 < fieldDefs.Length; i += 2)
        {
            SetField(fieldDefs[i].ToString(), fieldDefs[i + 1]);
        }
    }

    /// <summary>A field of an element at MATLAB's 1-based subscripts.</summary>
    public object this[string fieldName, params int[] indices]
    {
        get => _elements[Linear(indices)][Field(fieldName)];
        set => _elements[Linear(indices)][Field(fieldName)] = value;
    }

    /// <summary>A field of the first element.</summary>
    public object this[string fieldName]
    {
        get => this[fieldName, 1];
        set => this[fieldName, 1] = value;
    }

    /// <summary>The empty struct.</summary>
    public static MWStructArray Empty => new();

    /// <summary>The field names.</summary>
    public string[] FieldNames => [.. _names];

    /// <summary>The number of fields.</summary>
    public int NumberOfFields => _names.Count;

    /// <inheritdoc />
    public override MWArrayType ArrayType => MWArrayType.Structure;

    internal object At(int element, int field) => _elements[element][field];

    /// <inheritdoc />
    public override object Clone()
    {
        var copy = new MWStructArray(Dimensions, [.. _names]);
        for (int e = 0; e < _elements.Length; e++)
        {
            copy._elements[e] = [.. _elements[e]];
        }

        return copy;
    }

    /// <inheritdoc />
    public override bool Equals(object obj) => obj is MWStructArray other && Equals(other);

    /// <summary>Whether both are the same struct.</summary>
    public bool Equals(MWStructArray obj) => ReferenceEquals(this, obj);

    /// <summary>A field of the element at a 0-based linear index.</summary>
    public object GetField(string fieldName, int index) =>
        index >= 0 && index < _elements.Length ? _elements[index][Field(fieldName)] : throw new ArgumentException("The specified dimensions are invalid");

    /// <summary>A field of the first element.</summary>
    public object GetField(string fieldName) => this[fieldName];

    /// <inheritdoc />
    public override int GetHashCode() => base.GetHashCode();

    /// <summary>Whether the struct has the field.</summary>
    public bool IsField(string fieldName) => _names.Contains(fieldName);

    /// <summary>Removes the field, and answers this struct.</summary>
    public MWStructArray RemoveField(string fName)
    {
        int at = Field(fName);
        _names.RemoveAt(at);
        foreach (List<object> element in _elements)
        {
            element.RemoveAt(at);
        }

        return this;
    }

    /// <summary>Sets the field of the first element, adding it to every element when new.</summary>
    public void SetField(string fieldName, object fieldValue)
    {
        if (!_names.Contains(fieldName))
        {
            _names.Add(fieldName);
            foreach (List<object> element in _elements)
            {
                element.Add(new double[0, 0]);
            }
        }

        this[fieldName] = fieldValue;
    }

    /// <summary>R2025b's text: <c>1x1 struct array with fields:</c>, then each field after a tab.</summary>
    public override string ToString()
    {
        var text = new StringBuilder(Size).Append(" struct array with fields:");
        foreach (string name in _names)
        {
            text.Append("\n\t").Append(name);
        }

        return text.ToString();
    }

    internal IReadOnlyList<string> Names => _names;

    private int Field(string fieldName)
    {
        int at = _names.IndexOf(fieldName);
        return at >= 0 ? at : throw new ArgumentException("The specified field was not found");
    }
}
