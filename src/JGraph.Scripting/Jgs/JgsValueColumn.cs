using JGraph.Data;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// A table variable that holds the script value it was given, as it is (V6, ADR 0167): an integer,
/// single or logical array, a cell whose elements are not all text, a struct array. The columns
/// <see cref="JGraph.Data"/> knows — numbers, text, dates — store doubles and strings, so a value
/// of any other class came back out of a table as a double or as its own display text; this one
/// comes back as what went in, which is what makes <c>T{1, 1} = 3.7</c> into an <c>int8</c>
/// variable saturate and round, and <c>T.Var1{1}(1) = 9</c> reach a cell's element.
/// </summary>
/// <remarks>
/// The value's first dimension is the rows. The drawing and preview sides still get a numeric and
/// a text view of each row: the first column's number where the value has numbers, NaN and the
/// element's display where it does not.
/// </remarks>
internal sealed class JgsValueColumn : TableColumn
{
    public JgsValueColumn(string name, JgsValue value)
        : base(name, RowsOf(value ?? throw new ArgumentNullException(nameof(value))))
    {
        Value = value;
    }

    /// <summary>The variable's value. Shared out to readers, never written in place.</summary>
    public JgsValue Value { get; }

    /// <summary>Whether <paramref name="value"/> is one the plain column kinds would not give back as it is.</summary>
    public static bool Needs(JgsValue value) => value.Type switch
    {
        JgsType.Struct => true,
        JgsType.Bool => true,
        JgsType.Number => value.NumericClass != JgsNumericClass.Double,
        JgsType.Cell => value.AsCell.Any(element => element.Type != JgsType.String),

        // A string array stays a string array (V6, table forms): it read back as a cell of char.
        JgsType.Array => value.IsStringArray
            || (!value.IsTime && (value.NumericClass != JgsNumericClass.Double || IsLogical(value))),
        _ => false,
    };

    private static bool IsLogical(JgsValue array) => JgsBuiltins.IsLogicalValue(array); // packed or boxed

    /// <summary>How many table rows a value fills: its first dimension, and one for a scalar.</summary>
    public static int RowsOf(JgsValue value) => value.Type switch
    {
        JgsType.Array or JgsType.Cell => value.Rows,
        JgsType.Struct => value.AsStructArray.Length == 1 ? 1 : value.Rows,
        _ => 1,
    };

    private bool HasNumbers => Value.Type is JgsType.Number or JgsType.Bool
        || (Value.Type == JgsType.Array && !Value.IsStringArray);

    public override ColumnType Type => HasNumbers ? ColumnType.Number : ColumnType.Text;

    public override bool IsMissing(int row) => HasNumbers && double.IsNaN(GetNumber(row));

    public override double GetNumber(int row)
    {
        if (!HasNumbers)
        {
            return double.NaN;
        }

        JgsValue element = Value.Type == JgsType.Array ? Value.ElementAt(row) : Value;
        return element.Type switch
        {
            JgsType.Number => element.AsNumber,
            JgsType.Bool => element.AsBool ? 1 : 0,
            _ => double.NaN,
        };
    }

    public override string GetText(int row) => Value.Type switch
    {
        JgsType.Array => TextOfElement(Value.ElementAt(row)),
        JgsType.Cell => TextOfElement(Value.AsCell[row]),
        _ => Value.Display(),
    };

    /// <summary>A string element is its text — what a heatmap's labels read — and anything else how it displays.</summary>
    private static string TextOfElement(JgsValue element) =>
        element.Type == JgsType.String ? element.AsString : element.Display();

    public override TableColumn TakeRows(IReadOnlyList<int> rows) => new JgsValueColumn(Name, RowsOfValue(Value, rows));

    /// <summary>This column under another name.</summary>
    public JgsValueColumn Renamed(string name) => new(name, Value);

    /// <summary>
    /// This column grown to <paramref name="rows"/> rows, the new ones holding the kind's default:
    /// zero of the class, false, <c>[]</c> in a cell, a struct with every field empty.
    /// </summary>
    public JgsValueColumn Grown(int rows)
    {
        int old = RowCount;
        var picks = new int[rows];
        for (int r = 0; r < rows; r++)
        {
            picks[r] = r < old ? r : -1;
        }

        return new JgsValueColumn(Name, RowsOfValue(Value, picks));
    }

    /// <summary>
    /// The given rows of <paramref name="value"/> (a pick of -1 is a default-filled row), as a value
    /// of the same kind: every tag the value wears is carried, and each element is a share (M2).
    /// </summary>
    internal static JgsValue RowsOfValue(JgsValue value, IReadOnlyList<int> rows)
    {
        int count = rows.Count;
        switch (value.Type)
        {
            case JgsType.Struct:
            {
                JgsStructArray source = value.AsStructArray;
                var elements = new Dictionary<string, JgsValue>[count];
                for (int r = 0; r < count; r++)
                {
                    if (rows[r] < 0)
                    {
                        elements[r] = source.NewElement();
                        continue;
                    }

                    var copy = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
                    foreach (KeyValuePair<string, JgsValue> field in source.Elements[rows[r]])
                    {
                        copy[field.Key] = JgsValue.Share(field.Value);
                    }

                    elements[r] = copy;
                }

                return JgsValue.StructArray(new JgsStructArray(elements, source.FieldNames), count, 1);
            }

            case JgsType.Cell:
            {
                int oldRows = Math.Max(value.Rows, 1);
                int width = value.AsCell.Length / oldRows;
                var picked = new JgsValue[count * width];
                for (int c = 0; c < width; c++)
                {
                    for (int r = 0; r < count; r++)
                    {
                        picked[(c * count) + r] = rows[r] < 0
                            ? JgsEmpty.Zero()
                            : JgsValue.Share(value.AsCell[(c * oldRows) + rows[r]]);
                    }
                }

                JgsValue cell = JgsValue.Cell(picked);
                cell.Reshape(count, width);
                return cell;
            }

            case JgsType.Array:
            {
                int oldRows = Math.Max(value.Rows, 1);
                int width = value.ArrayLength / oldRows;
                bool logical = IsLogical(value);
                var picked = new JgsValue[count * width];
                for (int c = 0; c < width; c++)
                {
                    for (int r = 0; r < count; r++)
                    {
                        picked[(c * count) + r] = rows[r] >= 0 ? value.ElementAt((c * oldRows) + rows[r])
                            : logical ? JgsValue.Bool(false)
                            : value.IsStringArray ? JgsValue.Str(JgsBuiltins.MissingSentinel)
                            : JgsValue.Number(0);
                    }
                }

                return Interpreter.CarryValueTags(value, JgsMatrix.FromElements(picked, count, width));
            }

            default:
            {
                // A scalar fills one row; more rows of it are an array of it.
                if (count == 1 && rows[0] >= 0)
                {
                    return value;
                }

                var picked = new JgsValue[count];
                for (int r = 0; r < count; r++)
                {
                    picked[r] = rows[r] >= 0 ? JgsValue.Share(value)
                        : value.Type == JgsType.Bool ? JgsValue.Bool(false)
                        : JgsValue.Number(0);
                }

                JgsValue array = JgsMatrix.FromElements(picked, count, 1);
                array.SetNumericClass(value.NumericClass);
                return array;
            }
        }
    }
}
