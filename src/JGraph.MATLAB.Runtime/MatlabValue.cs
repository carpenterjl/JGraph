using JGraph.Runtime;
using JGraph.Scripting.Jgs;

namespace JGraph.MATLAB.Types;

/// <summary>The kinds of MATLAB value the Data API tells apart, as R2025b's conversions and messages do.</summary>
internal enum MatlabKind
{
    Numeric,
    Logical,
    Char,
    String,
    Cell,
    Struct,
    Sparse,
    Enumeration,
    ValueObject,
    HandleObject,
}

/// <summary>
/// A MATLAB value as the client holds it (stage K9): copied out of the session on the engine thread
/// into the runtime's lossless value model, with what the conversions ask besides - its kind, its
/// members' names for an enumeration, a dictionary's keys and values, and a cell's elements and a
/// struct's fields as values of their own. An object, enumeration or other value the model has no
/// storage for travels as the runtime's opaque value, which only its own component takes back.
/// </summary>
internal sealed class MatlabValue
{
    private MatlabValue(RuntimeValue data, string className, MatlabKind kind)
    {
        Data = data;
        ClassName = className;
        Kind = kind;
        Dims = [.. data.Dimensions];
    }

    private MatlabValue(RuntimeValue data, string className, MatlabKind kind, int[] dims)
        : this(data, className, kind)
    {
        Dims = dims;
    }

    /// <summary>The value in the runtime's model; an opaque value for an object.</summary>
    public RuntimeValue Data { get; }

    /// <summary>MATLAB's class name.</summary>
    public string ClassName { get; }

    /// <summary>What kind of value it is.</summary>
    public MatlabKind Kind { get; }

    /// <summary>MATLAB's size.</summary>
    public int[] Dims { get; }

    /// <summary>The number of elements.</summary>
    public int Count => Dims.Aggregate(1, static (a, b) => a * b);

    /// <summary>Whether the value is 1-by-1.</summary>
    public bool IsScalar => Count == 1;

    /// <summary>Whether a numeric value has an imaginary part.</summary>
    public bool IsComplex => Data is RuntimeNumeric { IsComplex: true };

    /// <summary>An enumeration's member names, column-major.</summary>
    public string[] EnumNames { get; private init; }

    /// <summary>A cell's elements, column-major.</summary>
    public MatlabValue[] Cells { get; private init; }

    /// <summary>A struct's field names, in order.</summary>
    public string[] FieldNames { get; private init; }

    /// <summary>A struct's elements, column-major, each field's value.</summary>
    public Dictionary<string, MatlabValue>[] Fields { get; private init; }

    /// <summary>Whether this is a MATLAB dictionary.</summary>
    public bool IsDictionary { get; private init; }

    /// <summary>A configured dictionary's keys, or null for an unconfigured one.</summary>
    public MatlabValue DictionaryKeys { get; private init; }

    /// <summary>A configured dictionary's values, in the keys' order.</summary>
    public MatlabValue DictionaryValues { get; private init; }

    /// <summary>Whether this is an object as R2025b's MATLABObject wraps one: a value or handle object, scalar.</summary>
    public bool IsScalarObject => Kind is MatlabKind.ValueObject or MatlabKind.HandleObject && IsScalar;

    /// <summary>Whether the value is a vector: two dimensions, one of them 1, and not 1-by-1.</summary>
    public bool IsVector => Dims.Length == 2 && (Dims[0] == 1 || Dims[1] == 1) && Count != 1;

    /// <summary>
    /// How R2025b's messages name the value: <c>double scalar</c>, <c>complex double scalar</c>,
    /// <c>logical vector</c>, <c>sparse double array</c>, <c>enum scalar</c>, <c>value object scalar</c>.
    /// </summary>
    public string Description
    {
        get
        {
            string what = Kind switch
            {
                MatlabKind.Enumeration => "enum",
                MatlabKind.ValueObject => "value object",
                MatlabKind.HandleObject => "handle object",
                MatlabKind.Sparse => "sparse " + (ClassName == "logical" ? "logical" : SparseComplex ? "complex double" : "double"),
                _ => (IsComplex ? "complex " : "") + ClassName,
            };
            string shape = Kind == MatlabKind.Sparse ? "array" : IsScalar ? "scalar" : IsVector ? "vector" : "array";
            return what + " " + shape;
        }
    }

    /// <summary>Whether a sparse value is complex.</summary>
    public bool SparseComplex { get; private init; }

    /// <summary>The engine's value for this one, to pass back to <paramref name="session"/>.</summary>
    public JgsValue ToEngine(ComponentSession session) => RuntimeValueConversion.ToEngine(Data, session);

    /// <summary>The client's copy of <paramref name="value"/>, which <paramref name="session"/> answered. Engine thread.</summary>
    public static MatlabValue Wrap(JgsValue value, ComponentSession session)
    {
        string cls = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
        RuntimeValue data = RuntimeValueConversion.FromEngine(value, session);
        if (value.Type == JgsType.Null)
        {
            return new MatlabValue(data, "double", MatlabKind.Numeric);
        }

        if (value.Type == JgsType.Sparse)
        {
            bool complex = data is RuntimeOpaque && cls == "double";
            return new MatlabValue(data, cls, MatlabKind.Sparse) { SparseComplex = complex };
        }

        switch (data)
        {
            case RuntimeNumeric:
                return new MatlabValue(data, data.ClassName, MatlabKind.Numeric);
            case RuntimeLogical:
                return new MatlabValue(data, "logical", MatlabKind.Logical);
            case RuntimeChar:
                return new MatlabValue(data, "char", MatlabKind.Char);
            case RuntimeString:
                return new MatlabValue(data, "string", MatlabKind.String);
            case RuntimeCell:
                return new MatlabValue(data, "cell", MatlabKind.Cell) { Cells = [.. value.AsCell.Select(e => Wrap(e, session))] };
            case RuntimeStruct record:
            {
                JgsStructArray payload = value.AsStructArray;
                return new MatlabValue(data, "struct", MatlabKind.Struct)
                {
                    FieldNames = [.. record.FieldNames],
                    Fields = [.. payload.Elements.Select(e => record.FieldNames.ToDictionary(n => n, n => Wrap(e[n], session), StringComparer.Ordinal))],
                };
            }
        }

        if (JgsBuiltins.IsEnumerationValue(value))
        {
            return new MatlabValue(data, cls, MatlabKind.Enumeration) { EnumNames = JgsBuiltins.EnumerationNamesOf(value) };
        }

        if (cls == "dictionary")
        {
            bool configured = JgsBuiltins.Feval(session.Interpreter, [JgsValue.Str("isConfigured"), value], 1, 0, 0) is [var c, ..] && c.AsBool;
            MatlabValue keys = null;
            MatlabValue values = null;
            if (configured)
            {
                keys = Wrap(JgsBuiltins.Feval(session.Interpreter, [JgsValue.Str("keys"), value], 1, 0, 0)[0], session);
                values = Wrap(JgsBuiltins.Feval(session.Interpreter, [JgsValue.Str("values"), value], 1, 0, 0)[0], session);
            }

            return new MatlabValue(data, cls, MatlabKind.ValueObject) { IsDictionary = true, DictionaryKeys = keys, DictionaryValues = values };
        }

        bool handle = value.Type == JgsType.Function
            || (value.Type == JgsType.Object && value.ObjectClass.IsHandle)
            || cls == "containers.Map";

        // A class JGraph keeps as a value of its own - a table, a categorical, a datetime - is one
        // object to R2025b's client whatever its size, as an MCOS object is (k9client: out.table,
        // out.categorical); a user class's object array keeps its size.
        int[] dims = value.Type == JgsType.Object ? [.. data.Dimensions] : [1, 1];
        return new MatlabValue(data, cls, handle ? MatlabKind.HandleObject : MatlabKind.ValueObject, dims);
    }
}
