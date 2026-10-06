using System.Globalization;
using JGraph.Core.Model;
using JGraph.Data;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// What a <c>uitable</c> holds on the script's side (app-building plan, U8). Its <c>Data</c> can be
/// of any of five classes and is kept as it was given, because it reads back as it was given; the
/// column properties are kept the same way. The model in <c>JGraph.Core</c> holds only the picture
/// worked out from them (<see cref="UiTableContent"/>), which <see cref="JgsUiTables.Rebuild"/>
/// makes again whenever one of them is written.
/// </summary>
internal sealed class JgsTableState
{
    public JgsValue Data { get; set; } = JgsMatrix.FromColumnMajor([], 0, 0);

    /// <summary><c>ColumnName</c> as written, or null while it is <c>'numbered'</c> by default.</summary>
    public UiText? ColumnName { get; set; }

    /// <summary><c>RowName</c> as written, or null while it is <c>'numbered'</c> by default.</summary>
    public UiText? RowName { get; set; }

    /// <summary><c>ColumnWidth</c> as it reads back, and the tracks it names; one track given as text serves every column.</summary>
    public JgsValue ColumnWidth { get; set; } = JgsValue.Str("auto");

    public IReadOnlyList<UiGridTrack> Widths { get; set; } = [new UiGridTrack(UiGridTrackKind.Weight, 1)];

    public bool OneWidthForAll { get; set; } = true;

    /// <summary><c>ColumnEditable</c>: null for the empty default, else one flag for every column or one each.</summary>
    public bool[]? Editable { get; set; }

    public bool EditableIsScalar { get; set; }

    public bool[]? Sortable { get; set; }

    public bool SortableIsScalar { get; set; }

    /// <summary><c>ColumnFormat</c> as written: a row cell, or null for the empty default.</summary>
    public JgsValue? ColumnFormat { get; set; }

    /// <summary><c>Selection</c> as written — it reads back as written, fractions and all — or null for none.</summary>
    public JgsValue? Selection { get; set; }

    /// <summary>Whether <c>ForegroundColor</c> was given as <c>'none'</c>, which reads back.</summary>
    public bool InkIsNone { get; set; }

    public string FontWeight { get; set; } = "normal";

    public string FontAngle { get; set; } = "normal";
}

/// <summary>
/// The arithmetic of a <c>uitable</c> (U8): its data as rows and columns of cells, the picture the
/// window shows, and what an edit a person makes does to the data. R2025b's rules for what
/// <c>Data</c> may be were measured headless (probes <c>u8_matrix</c>, <c>u8_behave</c>,
/// <c>u8_more</c>); how a number is written in a cell, and what an edit stores, follow MathWorks'
/// documentation of <c>ColumnFormat</c> and <c>CellEditCallback</c>, because R2025b draws a table
/// only in a window.
/// </summary>
internal static class JgsUiTables
{
    private static readonly string[] NumberFormats =
        ["short", "long", "shorte", "longe", "shortg", "longg", "shorteng", "longeng", "bank", "+", "rat"];

    /// <summary>
    /// A table's script-side state. A table read from a document, or copied through one, arrives
    /// with its picture and no data: its data is then what its picture shows, as nearly as the
    /// picture says it — numbers to the digits shown, text, and ticks.
    /// </summary>
    public static JgsTableState StateOf(JgsHandleEntry entry)
    {
        if (entry.Table is { } state)
        {
            return state;
        }

        state = new JgsTableState();
        if (entry.Target is UiTableModel { Content: { Rows: > 0, Columns.Count: > 0 } shown })
        {
            state.Data = FromPicture(shown);
            state.ColumnName = new UiText(UiTextForm.Cell, [.. shown.Columns.Select(static column => column.Header)]);
            state.RowName = shown.RowHeaders is { } headers ? new UiText(UiTextForm.Cell, [.. headers]) : UiText.Empty;
            state.Editable = [.. shown.Columns.Select(static column => column.Editable)];
            state.Sortable = [.. shown.Columns.Select(static column => column.Sortable)];
            state.Widths = [.. shown.Columns.Select(static column => column.Width)];
            state.OneWidthForAll = false;
        }

        entry.Table = state;
        return state;
    }

    /// <summary>The data a picture stands for: a matrix when every cell is a number, else a cell array.</summary>
    private static JgsValue FromPicture(UiTableContent shown)
    {
        int rows = shown.Rows;
        int columns = shown.Columns.Count;
        var elements = new JgsValue[rows * columns];
        bool numbers = true;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                UiTableCell cell = shown.At(r, c);
                JgsValue value = cell.Kind switch
                {
                    UiTableCellKind.Checked => JgsValue.Bool(true),
                    UiTableCellKind.Unchecked => JgsValue.Bool(false),
                    UiTableCellKind.Number => JgsValue.Number(ReadNumber(cell.Text)),
                    _ => JgsValue.Str(cell.Text),
                };
                numbers &= value.Type == JgsType.Number;
                elements[r + (c * rows)] = value;
            }
        }

        JgsValue data;
        if (numbers)
        {
            data = JgsMatrix.FromElements(elements, rows, columns);
        }
        else
        {
            data = JgsValue.Cell(elements);
            data.Reshape(rows, columns);
        }

        JgsLifetime.Pin(data);
        return data;
    }

    /// <summary>
    /// Gives every table of a copy the script-side state of the table it was copied from, the two
    /// trees being walked side by side.
    /// </summary>
    public static void CopyStates(GraphObject source, GraphObject copy)
    {
        // A uihtml's Data and page go with it (U9b, probe u9b_forms).
        if (source is UiHtmlModel page && copy is UiHtmlModel pageCopy)
        {
            JgsUiHtml.CopyState(page, pageCopy);
            pageCopy.SetSource(page.Source, page.SourceFile);
            return;
        }

        if (source is UiTableModel && copy is UiTableModel
            && JgsHandleRegistry.TryGetEntry(source, out JgsHandleEntry? from) && from.Table is { } state)
        {
            JgsHandleEntry to = JgsHandleRegistry.EntryFor(copy);
            to.Table = new JgsTableState
            {
                Data = state.Data,
                ColumnName = state.ColumnName,
                RowName = state.RowName,
                ColumnWidth = state.ColumnWidth,
                Widths = state.Widths,
                OneWidthForAll = state.OneWidthForAll,
                Editable = state.Editable,
                EditableIsScalar = state.EditableIsScalar,
                Sortable = state.Sortable,
                SortableIsScalar = state.SortableIsScalar,
                ColumnFormat = state.ColumnFormat,
                Selection = state.Selection,
                InkIsNone = state.InkIsNone,
                FontWeight = state.FontWeight,
                FontAngle = state.FontAngle,
            };
            return;
        }

        if (source is IUiContainer original && copy is IUiContainer made)
        {
            for (int i = 0; i < original.Components.Count && i < made.Components.Count; i++)
            {
                CopyStates(original.Components[i], made.Components[i]);
            }
        }
    }

    /// <summary>The rows and columns of a table's data.</summary>
    public static (int Rows, int Columns) SizeOf(JgsValue data) => data.Type switch
    {
        JgsType.Table => (data.AsTable.RowCount, data.AsTable.ColumnCount),
        JgsType.Array or JgsType.Cell => (JgsMatrix.RowCount(data), data.ArrayLength == 0 ? data.Cols : JgsMatrix.ColCount(data)),
        JgsType.Sparse => (data.AsSparse.Rows, data.AsSparse.Cols),
        _ => (1, 1),
    };

    /// <summary>One cell of the data, row and column from 0, as a value of its own.</summary>
    public static JgsValue CellOf(JgsValue data, int row, int column)
    {
        switch (data.Type)
        {
            case JgsType.Table:
            {
                TableColumn variable = data.AsTable[column];
                JgsValue whole = JgsBuiltins.TableColumnValue(data.AsTable, variable.Name, 0, 0);
                return whole.Type switch
                {
                    JgsType.Cell => whole.AsCell[row],
                    JgsType.Array when whole.IsStringArray => JgsValue.StringScalar(JgsBuiltins.TextOf(whole.ElementAt(row))),
                    JgsType.Array when whole.IsTime => JgsValue.Str(variable.GetText(row)),
                    JgsType.Array when whole.Rows == variable.RowCount && whole.Cols == 1 => whole.ElementAt(row),
                    JgsType.Array => JgsValue.Str(variable.GetText(row)),
                    _ => whole,
                };
            }

            case JgsType.Cell:
                return data.AsCell[row + (column * data.Rows)];

            case JgsType.Array when data.IsStringArray:
                return JgsValue.StringScalar(JgsBuiltins.TextOf(JgsMatrix.At(data, row, column)));

            case JgsType.Array:
                return JgsMatrix.At(data, row, column);

            default:
                return data;
        }
    }

    // --- the picture -----------------------------------------------------------------------------

    /// <summary>Works the table's picture out again from its data and column properties.</summary>
    public static void Rebuild(JgsHandleEntry entry)
    {
        var table = (UiTableModel)entry.Target;
        JgsTableState state = StateOf(entry);
        (int rows, int columns) = SizeOf(state.Data);
        IReadOnlyList<string>? formats = FormatWords(state.ColumnFormat, columns, out IReadOnlyList<IReadOnlyList<string>?> choices);

        var shown = new UiTableColumn[columns];
        IReadOnlyList<string> headers = Headers(state.ColumnName, columns, state.Data.Type == JgsType.Table ? state.Data.AsTable.ColumnNames : null);
        for (int c = 0; c < columns; c++)
        {
            UiGridTrack width = state.OneWidthForAll
                ? state.Widths[0]
                : c < state.Widths.Count ? state.Widths[c] : new UiGridTrack(UiGridTrackKind.Weight, 1);
            shown[c] = new UiTableColumn(
                headers[c],
                width,
                FlagFor(state.Editable, state.EditableIsScalar, c),
                FlagFor(state.Sortable, state.SortableIsScalar, c),
                choices[c]);
        }

        var cells = new UiTableCell[rows * columns];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                cells[(r * columns) + c] = Show(CellOf(state.Data, r, c), formats?[c]);
            }
        }

        IReadOnlyList<string>? rowHeaders = RowHeaders(state.RowName, rows,
            state.Data.Type == JgsType.Table ? state.Data.AsTable.RowNames : null, state.Data.Type == JgsType.Table);
        table.Content = new UiTableContent(rows, shown, cells, rowHeaders);
        table.Bold = state.FontWeight is "bold" or "demi";
        table.Italic = state.FontAngle is "italic" or "oblique";
    }

    private static bool FlagFor(bool[]? flags, bool scalar, int column) =>
        flags is not null && (scalar ? flags.Length > 0 && flags[0] : column < flags.Length && flags[column]);

    /// <summary>
    /// The heading of each column: its number while <c>ColumnName</c> is <c>'numbered'</c> (or a
    /// table's variable names, which R2025b shows in their place), no heading at all for an empty
    /// one, and otherwise the names given, as far as they go.
    /// </summary>
    private static IReadOnlyList<string> Headers(UiText? given, int columns, IReadOnlyList<string>? variables)
    {
        var headers = new string[columns];
        bool numbered = given is null || (given.Form == UiTextForm.CharRow && given.Lines.Count == 1 && given.Lines[0] == "numbered");
        for (int c = 0; c < columns; c++)
        {
            headers[c] = numbered
                ? variables is not null && given is null ? variables[c] : (c + 1).ToString(CultureInfo.InvariantCulture)
                : c < given!.Lines.Count ? given.Lines[c].Trim() : string.Empty;
        }

        return headers;
    }

    private static IReadOnlyList<string>? RowHeaders(UiText? given, int rows, IReadOnlyList<string>? names, bool fromTable)
    {
        if (given is null && fromTable)
        {
            return names is { Count: > 0 } ? names : null;
        }

        bool numbered = given is null || (given.Form == UiTextForm.CharRow && given.Lines.Count == 1 && given.Lines[0] == "numbered");
        if (!numbered && given!.Lines.All(static line => line.Length == 0))
        {
            return null;
        }

        var headers = new string[rows];
        for (int r = 0; r < rows; r++)
        {
            headers[r] = numbered ? (r + 1).ToString(CultureInfo.InvariantCulture) : r < given!.Lines.Count ? given.Lines[r].Trim() : string.Empty;
        }

        return headers;
    }

    /// <summary>
    /// Each column's format word, lower case and without its space (<c>'short e'</c> is
    /// <c>shorte</c>), or null where it has none; and, apart, the choices of a column whose format
    /// is a list.
    /// </summary>
    private static IReadOnlyList<string>? FormatWords(JgsValue? format, int columns, out IReadOnlyList<IReadOnlyList<string>?> choices)
    {
        var lists = new IReadOnlyList<string>?[columns];
        choices = lists;
        if (format is not { Type: JgsType.Cell })
        {
            return null;
        }

        var words = new string[columns];
        JgsValue[] given = format.AsCell;
        for (int c = 0; c < columns && c < given.Length; c++)
        {
            if (given[c].Type == JgsType.Cell)
            {
                lists[c] = [.. given[c].AsCell.Select(static choice => JgsBuiltins.TextOf(choice))];
            }
            else if (given[c].Type == JgsType.String)
            {
                words[c] = given[c].AsString.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
            }
        }

        return words;
    }

    /// <summary>One cell as the window shows it.</summary>
    private static UiTableCell Show(JgsValue value, string? format)
    {
        bool asNumber = format is not null && Array.IndexOf(NumberFormats, format) >= 0;
        switch (value.Type)
        {
            case JgsType.Bool when format is null or "logical":
                return new UiTableCell(string.Empty, value.IsTruthy ? UiTableCellKind.Checked : UiTableCellKind.Unchecked);

            case JgsType.Bool:
                return new UiTableCell(value.IsTruthy ? "1" : "0", format == "char" ? UiTableCellKind.Text : UiTableCellKind.Number);

            case JgsType.Number when format == "logical":
                return new UiTableCell(string.Empty, value.AsNumber != 0 ? UiTableCellKind.Checked : UiTableCellKind.Unchecked);

            case JgsType.Number:
                return new UiTableCell(NumberText(value.AsNumber, format), format == "char" ? UiTableCellKind.Text : UiTableCellKind.Number);

            case JgsType.Complex:
            {
                System.Numerics.Complex z = value.AsComplex;
                string imaginary = NumberText(System.Math.Abs(z.Imaginary), format);
                return new UiTableCell(
                    $"{NumberText(z.Real, format)} {(z.Imaginary < 0 ? "-" : "+")} {imaginary}i", UiTableCellKind.Number);
            }

            case JgsType.String:
                return new UiTableCell(value.AsString, asNumber && double.TryParse(value.AsString, out _) ? UiTableCellKind.Number : UiTableCellKind.Text);

            case JgsType.Array when value.IsStringArray && value.ArrayLength == 1:
            {
                string text = JgsBuiltins.TextOf(value.ElementAt(0));
                return new UiTableCell(JgsBuiltins.IsMissingText(text) ? string.Empty : text, UiTableCellKind.Text);
            }

            case JgsType.Array when value.IsCharMatrix:
                return new UiTableCell(string.Join(" ", value.CharMatrixRows()), UiTableCellKind.Text);

            default:
                return new UiTableCell(string.Empty, UiTableCellKind.Text);
        }
    }

    /// <summary>
    /// A number as a cell writes it: MATLAB's <c>format</c> of the column's word, <c>short</c>
    /// when it has none.
    /// </summary>
    internal static string NumberText(double value, string? format)
    {
        if (double.IsNaN(value))
        {
            return "NaN";
        }

        if (double.IsInfinity(value))
        {
            return value > 0 ? "Inf" : "-Inf";
        }

        CultureInfo plain = CultureInfo.InvariantCulture;
        bool whole = value == System.Math.Floor(value) && System.Math.Abs(value) < 1e10;
        switch (format)
        {
            case "long":
                return whole ? value.ToString("F0", plain) : Fixed(value, 15);
            case "shorte":
                return value.ToString("0.0000e+00", plain);
            case "longe":
                return value.ToString("0.000000000000000e+00", plain);
            case "shortg":
                return value.ToString("G5", plain);
            case "longg":
                return value.ToString("G15", plain);
            case "shorteng" or "longeng":
            {
                if (value == 0)
                {
                    return "0";
                }

                int power = (int)System.Math.Floor(System.Math.Log10(System.Math.Abs(value)) / 3) * 3;
                string mantissa = (value / System.Math.Pow(10, power)).ToString(format == "shorteng" ? "0.0000" : "0.00000000000000", plain);
                return $"{mantissa}e{(power < 0 ? "-" : "+")}{System.Math.Abs(power).ToString("000", plain)}";
            }

            case "bank":
                return value.ToString("F2", plain);
            case "+":
                return value > 0 ? "+" : value < 0 ? "-" : string.Empty;
            default:
                if (whole)
                {
                    return value.ToString("F0", plain);
                }

                double size = System.Math.Abs(value);
                return size is >= 0.001 and < 1000 ? value.ToString("F4", plain) : value.ToString("0.0000e+00", plain);
        }
    }

    /// <summary>A number in fixed notation with so many significant digits.</summary>
    private static string Fixed(double value, int digits)
    {
        double size = System.Math.Abs(value);
        if (size is < 0.001 or >= 1e5)
        {
            return value.ToString("0." + new string('0', digits) + "e+00", CultureInfo.InvariantCulture);
        }

        int before = System.Math.Max(1, (int)System.Math.Floor(System.Math.Log10(size)) + 1);
        return value.ToString("F" + System.Math.Max(0, digits - before), CultureInfo.InvariantCulture);
    }

    // --- an edit ---------------------------------------------------------------------------------

    /// <summary>What an edit a person made comes to: the value stored, or the reason none was.</summary>
    public readonly record struct Edit(JgsValue Previous, JgsValue Typed, JgsValue? Stored, string Error);

    /// <summary>
    /// Writes into the data what a person put in one cell — text typed, a box ticked or a choice
    /// picked — as MathWorks documents it: a cell that held a number takes the number the text
    /// reads as, and NaN when it reads as none; a logical cell takes the tick; anything else takes
    /// the text.
    /// </summary>
    public static Edit Apply(JgsHandleEntry entry, int row, int column, object given)
    {
        JgsTableState state = StateOf(entry);
        JgsValue previous = CellOf(state.Data, row, column);
        JgsValue typed = given is bool tick ? JgsValue.Bool(tick) : JgsValue.Str(given as string ?? string.Empty);
        JgsValue stored;
        switch (previous.Type)
        {
            case JgsType.Bool when given is bool ticked:
                stored = JgsValue.Bool(ticked);
                break;

            case JgsType.Number or JgsType.Complex or JgsType.Bool:
            {
                double number = given is bool on ? (on ? 1 : 0) : ReadNumber((string)given);
                stored = previous.Type == JgsType.Bool ? JgsValue.Bool(number != 0) : JgsValue.Number(number);
                break;
            }

            case JgsType.Array when previous.IsStringArray:
                stored = JgsValue.StringScalar(given as string ?? string.Empty);
                break;

            default:
                stored = given is bool flag ? JgsValue.Bool(flag) : JgsValue.Str((string)given);
                break;
        }

        try
        {
            state.Data = JgsLifetimeShare(Write(state.Data, row, column, stored));
        }
        catch (JgsException failure)
        {
            return new Edit(previous, typed, null, failure.Message);
        }

        Rebuild(entry);
        return new Edit(previous, typed, CellOf(state.Data, row, column), string.Empty);
    }

    private static JgsValue JgsLifetimeShare(JgsValue value)
    {
        JgsValue kept = JgsValue.Share(value);
        JgsLifetime.Pin(kept);
        return kept;
    }

    /// <summary>The number text reads as: a number, an infinity, or NaN for anything else.</summary>
    private static double ReadNumber(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Equals("inf", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("+inf", StringComparison.OrdinalIgnoreCase))
        {
            return double.PositiveInfinity;
        }

        if (trimmed.Equals("-inf", StringComparison.OrdinalIgnoreCase))
        {
            return double.NegativeInfinity;
        }

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : double.NaN;
    }

    /// <summary>The data with one cell replaced; the data given is left as it was.</summary>
    private static JgsValue Write(JgsValue data, int row, int column, JgsValue cell)
    {
        switch (data.Type)
        {
            case JgsType.Table:
            {
                Table table = data.AsTable;
                TableColumn variable = table[column];
                JgsValue whole = JgsBuiltins.TableColumnValue(table, variable.Name, 0, 0);
                JgsValue changed = WriteInto(whole, row, 0, cell);
                var columns = new List<TableColumn>(table.Columns);
                columns[column] = JgsBuiltins.TableColumnFrom("uitable", variable.Name, changed, 0, 0);
                return JgsValue.Table(table.WithColumns(columns));
            }

            case JgsType.Number or JgsType.Bool or JgsType.Complex:
                return cell;

            default:
                return WriteInto(data, row, column, cell);
        }
    }

    private static JgsValue WriteInto(JgsValue whole, int row, int column, JgsValue cell)
    {
        int rows = JgsMatrix.RowCount(whole);
        int columns = whole.ArrayLength == 0 ? 0 : JgsMatrix.ColCount(whole);
        var elements = new JgsValue[rows * columns];
        for (int c = 0; c < columns; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                elements[r + (c * rows)] = whole.Type == JgsType.Cell ? whole.AsCell[r + (c * rows)] : JgsMatrix.At(whole, r, c);
            }
        }

        if (whole.Type == JgsType.Cell)
        {
            elements[row + (column * rows)] = cell;
            JgsValue made = JgsValue.Cell(elements);
            made.Reshape(rows, columns);
            return made;
        }

        if (whole.IsStringArray)
        {
            elements[row + (column * rows)] = JgsValue.Str(JgsBuiltins.TextOf(cell));
            return JgsValue.StringArray(elements, rows, columns);
        }

        // A numeric or logical array keeps its class: the cell is brought to it.
        bool logical = JgsBuiltins.IsLogicalValue(whole);
        double number = cell.Type switch
        {
            JgsType.Bool => cell.IsTruthy ? 1 : 0,
            JgsType.Number => cell.AsNumber,
            _ => double.NaN,
        };
        elements[row + (column * rows)] = logical ? JgsValue.Bool(number != 0) : JgsValue.Number(number);
        JgsValue array = JgsMatrix.FromElements(elements, rows, columns);
        if (!logical && whole.NumericClass != JgsNumericClass.Double)
        {
            array.SetNumericClass(whole.NumericClass);
        }

        return array;
    }
}
