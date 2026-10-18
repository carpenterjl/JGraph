namespace JGraph.Scripting.Designer;

/// <summary>
/// A property value as App Designer writes it into <c>createComponents</c> (designer plan DS1): the
/// MATLAB value a component holds, or one of the few forms that name something else - a callback
/// method, other components, an image beside the app - and, for anything else, the text itself.
/// </summary>
/// <remarks>
/// The kinds are those R2025b's <c>ComponentCodeGenerator.propertyValueToString</c> writes, plus the
/// forms App Designer's code model substitutes for them (<c>createCallbackFcn</c>,
/// <c>fullfile(pathToMLAPP, ...)</c>). <see cref="DesignValueText"/> reads and writes them.
/// </remarks>
public abstract class DesignValue
{
    private protected DesignValue()
    {
    }

    /// <summary>Whether the value holds no elements, as MATLAB's <c>isempty</c> says.</summary>
    public abstract bool IsEmpty { get; }
}

/// <summary>A double or logical matrix, column-major.</summary>
public sealed class NumericValue : DesignValue
{
    /// <summary>Creates a matrix of <paramref name="rows"/> by <paramref name="cols"/> from column-major data.</summary>
    public NumericValue(int rows, int cols, IReadOnlyList<double> data, bool isLogical = false)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (rows < 0 || cols < 0 || data.Count != rows * cols)
        {
            throw new ArgumentException($"{data.Count} elements cannot fill a {rows}-by-{cols} matrix.", nameof(data));
        }

        Rows = rows;
        Cols = cols;
        Data = data.ToArray();
        IsLogical = isLogical;
    }

    /// <summary>A double scalar.</summary>
    public static NumericValue Scalar(double value) => new(1, 1, [value]);

    /// <summary>A logical scalar.</summary>
    public static NumericValue Logical(bool value) => new(1, 1, [value ? 1 : 0], isLogical: true);

    /// <summary>A double row vector.</summary>
    public static NumericValue Row(params double[] values) => new(1, values.Length, values);

    /// <summary>The number of rows.</summary>
    public int Rows { get; }

    /// <summary>The number of columns.</summary>
    public int Cols { get; }

    /// <summary>The elements, column by column.</summary>
    public IReadOnlyList<double> Data { get; }

    /// <summary>Whether the matrix is logical rather than double.</summary>
    public bool IsLogical { get; }

    /// <inheritdoc/>
    public override bool IsEmpty => Data.Count == 0;
}

/// <summary>A char row vector; the empty one is <c>''</c>.</summary>
public sealed class CharValue : DesignValue
{
    /// <summary>Creates the char row <paramref name="text"/>.</summary>
    public CharValue(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
    }

    /// <summary>The characters.</summary>
    public string Text { get; }

    /// <inheritdoc/>
    public override bool IsEmpty => Text.Length == 0;
}

/// <summary>A string scalar.</summary>
public sealed class StringValue : DesignValue
{
    /// <summary>Creates the string scalar <paramref name="text"/>.</summary>
    public StringValue(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
    }

    /// <summary>The string's characters.</summary>
    public string Text { get; }

    /// <inheritdoc/>
    public override bool IsEmpty => false;
}

/// <summary>A cell array, column-major.</summary>
public sealed class CellValue : DesignValue
{
    /// <summary>Creates a cell array of <paramref name="rows"/> by <paramref name="cols"/> from column-major items.</summary>
    public CellValue(int rows, int cols, IReadOnlyList<DesignValue> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (rows < 0 || cols < 0 || items.Count != rows * cols)
        {
            throw new ArgumentException($"{items.Count} items cannot fill a {rows}-by-{cols} cell array.", nameof(items));
        }

        Rows = rows;
        Cols = cols;
        Items = items.ToArray();
    }

    /// <summary>A cell row of <paramref name="items"/>.</summary>
    public static CellValue Row(params DesignValue[] items) => new(items.Length == 0 ? 0 : 1, items.Length, items);

    /// <summary>A cell column of <paramref name="items"/>.</summary>
    public static CellValue Column(params DesignValue[] items) => new(items.Length, items.Length == 0 ? 0 : 1, items);

    /// <summary>The number of rows.</summary>
    public int Rows { get; }

    /// <summary>The number of columns.</summary>
    public int Cols { get; }

    /// <summary>The items, column by column.</summary>
    public IReadOnlyList<DesignValue> Items { get; }

    /// <inheritdoc/>
    public override bool IsEmpty => Items.Count == 0;
}

/// <summary>An on/off value, <c>matlab.lang.OnOffSwitchState</c>.</summary>
public sealed class OnOffValue : DesignValue
{
    /// <summary>The value <c>'on'</c>.</summary>
    public static readonly OnOffValue On = new(true);

    /// <summary>The value <c>'off'</c>.</summary>
    public static readonly OnOffValue Off = new(false);

    private OnOffValue(bool isOn)
    {
        IsOn = isOn;
    }

    /// <summary>Whether the value is <c>'on'</c>.</summary>
    public bool IsOn { get; }

    /// <inheritdoc/>
    public override bool IsEmpty => false;
}

/// <summary>
/// A date as App Designer writes one, <c>datetime([y m d])</c>. MATLAB's years run past .NET's
/// (a date picker's limits start at year 0), so this is the three numbers and nothing more.
/// </summary>
/// <param name="Year">The year.</param>
/// <param name="Month">The month, 1 to 12.</param>
/// <param name="Day">The day of the month.</param>
public readonly record struct DesignDate(int Year, int Month, int Day);

/// <summary>A datetime array of dates (App Designer writes year, month and day), column-major; a null element is <c>NaT</c>.</summary>
public sealed class DatetimeValue : DesignValue
{
    /// <summary>Creates a datetime array of <paramref name="rows"/> by <paramref name="cols"/>.</summary>
    public DatetimeValue(int rows, int cols, IReadOnlyList<DesignDate?> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (rows < 0 || cols < 0 || items.Count != rows * cols)
        {
            throw new ArgumentException($"{items.Count} dates cannot fill a {rows}-by-{cols} array.", nameof(items));
        }

        Rows = rows;
        Cols = cols;
        Items = items.ToArray();
    }

    /// <summary>The number of rows.</summary>
    public int Rows { get; }

    /// <summary>The number of columns.</summary>
    public int Cols { get; }

    /// <summary>The dates, column by column; null is <c>NaT</c>.</summary>
    public IReadOnlyList<DesignDate?> Items { get; }

    /// <inheritdoc/>
    public override bool IsEmpty => Items.Count == 0;
}

/// <summary>A callback bound to a method of the app: <c>createCallbackFcn(app, @Method, true)</c>.</summary>
public sealed class CallbackValue : DesignValue
{
    /// <summary>Binds the method <paramref name="method"/>.</summary>
    public CallbackValue(string method)
    {
        ArgumentException.ThrowIfNullOrEmpty(method);
        Method = method;
    }

    /// <summary>The app method the callback runs.</summary>
    public string Method { get; }

    /// <inheritdoc/>
    public override bool IsEmpty => false;
}

/// <summary>
/// Other components of the app by code name: <c>app.X</c> (a context menu assigned to a component),
/// or the bracketed list <c>[app.A, app.B]</c> R2025b writes for tree nodes.
/// </summary>
public sealed class ComponentsValue : DesignValue
{
    /// <summary>Names <paramref name="codeNames"/>; <paramref name="bracketed"/> for the list form.</summary>
    public ComponentsValue(IReadOnlyList<string> codeNames, bool bracketed)
    {
        ArgumentNullException.ThrowIfNull(codeNames);
        CodeNames = codeNames.ToArray();
        Bracketed = bracketed;
    }

    /// <summary>The components' code names.</summary>
    public IReadOnlyList<string> CodeNames { get; }

    /// <summary>Whether the value is written as a bracketed list rather than one bare name.</summary>
    public bool Bracketed { get; }

    /// <inheritdoc/>
    public override bool IsEmpty => CodeNames.Count == 0;
}

/// <summary>
/// An image file kept beside the app, written <c>fullfile(pathToMLAPP, 'folder', 'file.png')</c>:
/// its path relative to the app's folder, one element per folder.
/// </summary>
public sealed class ImageFileValue : DesignValue
{
    /// <summary>The image at the relative path given by <paramref name="parts"/>.</summary>
    public ImageFileValue(IReadOnlyList<string> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Count == 0)
        {
            throw new ArgumentException("An image path has at least a file name.", nameof(parts));
        }

        Parts = parts.ToArray();
    }

    /// <summary>The path's elements, folders first and the file last.</summary>
    public IReadOnlyList<string> Parts { get; }

    /// <summary>The relative path with forward slashes, as App Designer records it.</summary>
    public string RelativePath => string.Join('/', Parts);

    /// <inheritdoc/>
    public override bool IsEmpty => false;
}

/// <summary>
/// A value the designer does not take apart: the text of the expression, written back as it was.
/// The design keeps it, but cannot compare it with a default or show it in the inspector.
/// </summary>
public sealed class ExpressionValue : DesignValue
{
    /// <summary>Keeps the expression <paramref name="text"/>.</summary>
    public ExpressionValue(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
    }

    /// <summary>The expression as written.</summary>
    public string Text { get; }

    /// <inheritdoc/>
    public override bool IsEmpty => false;
}
