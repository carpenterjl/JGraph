using JGraph.Runtime;

namespace JGraph.MATLAB.NET.Arrays;

/// <summary>
/// A char array, as R2025b's: a string is a row (<c>''</c> 0-by-0), several strings the rows of a
/// matrix padded with blanks, an element a 1-by-1 char at MATLAB's 1-based subscripts, and
/// <see cref="ToString"/> the rows one per line (<c>k8_client</c>).
/// </summary>
public class MWCharArray : MWArray
{
    private char[] _chars;

    /// <summary>The empty char, 0-by-0.</summary>
    public MWCharArray()
        : this([0, 0], [])
    {
    }

    /// <summary>A char array of this size, every character 0.</summary>
    public MWCharArray(int[] dimensions)
        : base(dimensions) => _chars = new char[NumberOfElements];

    /// <summary>A char row; the empty string is 0-by-0.</summary>
    public MWCharArray(string value)
        : this(value.Length == 0 ? [0, 0] : [1, value.Length], value.ToCharArray())
    {
    }

    /// <summary>A 1-by-1 char.</summary>
    public MWCharArray(char value)
        : this([1, 1], [value])
    {
    }

    /// <summary>A char row.</summary>
    public MWCharArray(char[] value)
        : this(new string(value))
    {
    }

    /// <summary>A char matrix whose rows are the strings, padded with blanks to the longest.</summary>
    public MWCharArray(string[] strings)
        : this(Rows(strings))
    {
    }

    /// <summary>A char matrix of a .NET char array (its rows), or of a string array (its strings as rows).</summary>
    public MWCharArray(Array stringArray)
        : this(FromArray(stringArray))
    {
    }

    private MWCharArray(int[] dims, char[] chars)
        : base(dims) => _chars = chars;

    private MWCharArray((int[] Dims, char[] Chars) made)
        : this(made.Dims, made.Chars)
    {
    }

    /// <summary>An element, as a 1-by-1 char, at MATLAB's 1-based subscripts.</summary>
    public new MWCharArray this[params int[] indices]
    {
        get => (MWCharArray)GetElement(indices);
        set => SetElement(indices, value);
    }

    /// <summary>The empty char.</summary>
    public static MWCharArray Empty => new();

    /// <inheritdoc />
    public override MWArrayType ArrayType => MWArrayType.Character;

    /// <inheritdoc />
    public override object Clone() => new MWCharArray(Dimensions, (char[])_chars.Clone());

    /// <inheritdoc />
    public override bool Equals(object obj) => base.Equals(obj);

    /// <inheritdoc />
    public override int GetHashCode() => base.GetHashCode();

    /// <summary>The characters as a .NET char array shaped as the array is.</summary>
    public override Array ToArray()
    {
        CheckDisposed();
        return NativeConversion.Shaped(Dimensions, typeof(char), i => _chars[i]);
    }

    /// <inheritdoc />
    public override string ToString() => base.ToString();

    /// <summary>A two-dimensional array's rows as strings.</summary>
    public string[] ToStringArrayIfIs2DArray()
    {
        CheckDisposed();
        if (Dims.Length != 2)
        {
            return null;
        }

        int rows = Dims[0];
        int cols = Dims[1];
        var strings = new string[rows];
        for (int r = 0; r < rows; r++)
        {
            var row = new char[cols];
            for (int c = 0; c < cols; c++)
            {
                row[c] = _chars[r + (c * rows)];
            }

            strings[r] = new string(row);
        }

        return strings;
    }

    /// <summary>The first character.</summary>
    public static explicit operator char(MWCharArray value)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.CheckDisposed();
        return value._chars.Length > 0 ? value._chars[0] : '\0';
    }

    /// <summary>A char row.</summary>
    public static implicit operator MWCharArray(string value) => new(value);

    private protected override string Text() => MatlabDisplay.Char(Dims, _chars);

    internal override RuntimeValue ToRuntime()
    {
        CheckDisposed();
        return new RuntimeChar(new string(_chars), Dims);
    }

    internal static MWCharArray FromRuntime(RuntimeChar value) => new([.. value.Dimensions], value.Characters.ToCharArray());

    private protected override MWArray GetElement(int[] indices)
    {
        CheckDisposed();
        return new MWCharArray(_chars[Linear(indices)]);
    }

    private protected override void SetElement(int[] indices, MWArray value)
    {
        CheckDisposed();
        int at = Linear(indices);
        _chars[at] = value is MWCharArray text && text._chars.Length > 0
            ? text._chars[0]
            : throw new ArgumentException("The value must be a char array");
    }

    private static (int[], char[]) Rows(string[] strings)
    {
        ArgumentNullException.ThrowIfNull(strings);
        int width = strings.Length == 0 ? 0 : strings.Max(static s => s.Length);
        var chars = new char[strings.Length * width];
        for (int r = 0; r < strings.Length; r++)
        {
            string row = strings[r].PadRight(width);
            for (int c = 0; c < width; c++)
            {
                chars[r + (c * strings.Length)] = row[c];
            }
        }

        return ([strings.Length, width], chars);
    }

    private static (int[], char[]) FromArray(Array array)
    {
        ArgumentNullException.ThrowIfNull(array);
        Type element = array.GetType().GetElementType();
        if (element == typeof(string))
        {
            return Rows([.. array.Cast<string>()]);
        }

        if (element != typeof(char))
        {
            throw new ArgumentException("The data array type is invalid");
        }

        (int[] dims, object[] values) = NativeConversion.UnshapedObjects(array, rowMajor: true);
        return (dims, [.. values.Select(static v => (char)v)]);
    }
}
