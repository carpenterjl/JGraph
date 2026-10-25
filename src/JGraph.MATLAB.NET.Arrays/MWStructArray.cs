using System.Text.RegularExpressions;
using JGraph.Runtime;

namespace JGraph.MATLAB.NET.Arrays;

/// <summary>
/// A struct array, as R2025b's: a field of an element read and written by name and MATLAB's 1-based
/// subscripts (the first element by name alone), <see cref="GetField(string, int)"/> by a 0-based
/// index, a field set on an element that lacks it added to all, and <see cref="ToString"/> MATLAB's
/// older struct display (<c>k8_client</c>). Field names must be MATLAB's and distinct: R2025b takes
/// <c>1bad</c> and a name twice, which MATLAB cannot hold (a divergence, ADR 0264).
/// </summary>
public class MWStructArray : MWArray
{
    private readonly List<string> _names;
    private List<MWArray>[] _elements;

    /// <summary>The empty struct, 0-by-0, with no fields.</summary>
    public MWStructArray()
        : this([0, 0], [])
    {
    }

    /// <summary>A rows-by-columns struct array with these fields, each empty.</summary>
    public MWStructArray(int rows, int columns, string[] fieldNames)
        : this([rows, columns], fieldNames)
    {
    }

    /// <summary>A struct array of this size with these fields, each empty.</summary>
    public MWStructArray(int[] dimensions, string[] fieldNames)
        : base(dimensions)
    {
        ArgumentNullException.ThrowIfNull(fieldNames);
        _names = [];
        foreach (string name in fieldNames)
        {
            AddName(name);
        }

        _elements = new List<MWArray>[NumberOfElements];
        for (int e = 0; e < _elements.Length; e++)
        {
            _elements[e] = [.. _names.Select(static _ => (MWArray)new MWNumericArray())];
        }
    }

    /// <summary>A 1-by-1 struct of name, value pairs.</summary>
    public MWStructArray(MWArray[] fieldDefs)
        : this([1, 1], [])
    {
        ArgumentNullException.ThrowIfNull(fieldDefs);
        for (int i = 0; i + 1 < fieldDefs.Length; i += 2)
        {
            SetField(fieldDefs[i].ToString(), fieldDefs[i + 1]);
        }
    }

    private MWStructArray(int[] dims, List<string> names, List<MWArray>[] elements)
        : base(dims)
    {
        _names = names;
        _elements = elements;
    }

    /// <summary>A field of an element at MATLAB's 1-based subscripts.</summary>
    public MWArray this[string fieldName, params int[] indices]
    {
        get
        {
            CheckDisposed();
            return (MWArray)_elements[Element(indices)][Field(fieldName)].Clone();
        }

        set
        {
            CheckDisposed();
            ArgumentNullException.ThrowIfNull(value);
            int e = Element(indices);
            _elements[e][Field(fieldName)] = (MWArray)value.Clone();
        }
    }

    /// <summary>A field of the first element.</summary>
    public MWArray this[string fieldName]
    {
        get => this[fieldName, 1];
        set => this[fieldName, 1] = value;
    }

    /// <summary>The empty struct.</summary>
    public static MWStructArray Empty => new();

    /// <inheritdoc />
    public override MWArrayType ArrayType => MWArrayType.Structure;

    /// <summary>The field names, in order.</summary>
    public string[] FieldNames
    {
        get
        {
            CheckDisposed();
            return [.. _names];
        }
    }

    /// <summary>The number of fields.</summary>
    public int NumberOfFields => _names.Count;

    /// <summary>The elements' fields, for the display and the conversions.</summary>
    internal IReadOnlyList<string> Names => _names;

    internal MWArray At(int element, int field) => _elements[element][field];

    /// <inheritdoc />
    public override object Clone() =>
        new MWStructArray(Dimensions, [.. _names], [.. _elements.Select(static e => e.Select(static v => (MWArray)v.Clone()).ToList())]);

    /// <summary>Whether <paramref name="other"/> holds the same struct.</summary>
    public bool Equals(MWStructArray other) => Equals((object)other);

    /// <inheritdoc />
    public override bool Equals(object obj) => base.Equals(obj);

    /// <summary>A field of the first element.</summary>
    public MWArray GetField(string fieldName) => this[fieldName];

    /// <summary>A field of the element at a 0-based linear index, as R2025b counts there (<c>struct.getfield.b.0</c>).</summary>
    public MWArray GetField(string fieldName, int index)
    {
        CheckDisposed();
        if (index < 0 || index >= _elements.Length)
        {
            throw new ArgumentException("The specified dimensions are invalid");
        }

        return (MWArray)_elements[index][Field(fieldName)].Clone();
    }

    /// <inheritdoc />
    public override int GetHashCode() => base.GetHashCode();

    /// <summary>Whether the struct has the field.</summary>
    public bool IsField(string fieldName) => _names.Contains(fieldName);

    /// <summary>Removes the field from every element, and answers this struct.</summary>
    public MWStructArray RemoveField(string fieldName)
    {
        CheckDisposed();
        int at = Field(fieldName);
        _names.RemoveAt(at);
        foreach (List<MWArray> element in _elements)
        {
            element.RemoveAt(at);
        }

        return this;
    }

    /// <summary>Sets the field of the first element, adding it to every element when new.</summary>
    public void SetField(string fieldName, MWArray fieldValue)
    {
        CheckDisposed();
        if (!_names.Contains(fieldName))
        {
            AddName(fieldName);
            foreach (List<MWArray> element in _elements)
            {
                element.Add(new MWNumericArray());
            }
        }

        this[fieldName] = fieldValue;
    }

    /// <summary>Every field of every element as the native API's values: an array of fields, then the struct's own dimensions.</summary>
    public override Array ToArray()
    {
        CheckDisposed();
        var lengths = new int[1 + Dims.Length];
        lengths[0] = _names.Count;
        int[] csShape = NativeConversion.CsLengths(Dims);
        Array.Copy(csShape, 0, lengths, 1, csShape.Length);
        var array = Array.CreateInstance(typeof(object), lengths);
        for (int e = 0; e < _elements.Length; e++)
        {
            int[] at = NativeConversion.CsIndex(Dims, e);
            for (int f = 0; f < _names.Count; f++)
            {
                int[] index = [f, .. at];
                array.SetValue(NativeConversion.ToNative(_elements[e][f].ToRuntime()), index);
            }
        }

        return array;
    }

    /// <inheritdoc />
    public override string ToString() => base.ToString();

    private protected override string Text() => LegacyDisplay.Struct(this);

    internal override RuntimeValue ToRuntime()
    {
        CheckDisposed();
        return new RuntimeStruct(
            [.. _names],
            [.. _elements.Select(e => (IReadOnlyDictionary<string, RuntimeValue>)_names.Select((n, f) => (n, f)).ToDictionary(p => p.n, p => e[p.f].ToRuntime(), StringComparer.Ordinal))],
            Dims);
    }

    internal static MWStructArray FromRuntime(RuntimeStruct value) =>
        new([.. value.Dimensions], [.. value.FieldNames],
            [.. value.Elements.Select(e => value.FieldNames.Select(n => MWArray.FromRuntime(e[n])).ToList())]);

    private void AddName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!Regex.IsMatch(name, "^[A-Za-z][A-Za-z0-9_]*$"))
        {
            throw new ArgumentException($"'{name}' is not a valid field name: a field name starts with a letter and holds letters, digits and underscores.");
        }

        if (_names.Contains(name))
        {
            throw new ArgumentException($"The field name '{name}' is given twice: a struct's field names must be distinct.");
        }

        _names.Add(name);
    }

    private int Field(string fieldName)
    {
        int at = _names.IndexOf(fieldName);
        return at >= 0 ? at : throw new ArgumentException("The specified field was not found");
    }

    private int Element(int[] indices)
    {
        try
        {
            return Linear(indices);
        }
        catch (Exception ex) when (ex.Message == "Invalid index")
        {
            throw new ArgumentException("The specified dimensions are invalid");
        }
    }
}
