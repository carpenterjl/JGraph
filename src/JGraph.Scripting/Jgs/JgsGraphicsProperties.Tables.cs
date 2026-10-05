using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The property surface of <c>uitable</c> (app-building plan, U8). One class serves a classic
/// figure and a <c>uifigure</c> in R2025b, with one set of names, words and refusals, recorded
/// headless against a battery of values (probes <c>u8_matrix</c>, <c>u8_behave</c>, <c>u8_more</c>).
/// A table has <c>Units</c> and the font block of a classic control, and its own refusals,
/// <c>MATLAB:hg:uitable:…</c>, for its data and its columns.
/// </summary>
internal static partial class JgsGraphicsProperties
{
    private static readonly string[] SelectionTypeWords = ["cell", "row", "column"];

    private static readonly string[] ColumnFormatWords =
        ["char", "numeric", "logical", "short", "long", "shorte", "longe", "shortg", "longg", "shorteng", "longeng", "bank", "+", "rat"];

    private static UiTableModel TableOf(JgsHandleEntry entry) => (UiTableModel)entry.Target;

    private static JgsRuntimeException TableError(JgsHandleEntry entry, string property, string id, string reason, int line, int col) =>
        ComponentError(entry, property, "MATLAB:hg:uitable:" + id, reason, line, col);

    private static JgsValue EmptyLogical()
    {
        JgsValue none = JgsValue.Shaped([], 0, 0, JgsPackedKind.Bool);
        return none;
    }

    private static JgsValue FlagsValue(bool[]? flags, bool scalar) => flags switch
    {
        null => EmptyLogical(),
        _ when scalar => JgsValue.Bool(flags[0]),
        _ => JgsValue.Shaped([.. flags.Select(static flag => JgsValue.Bool(flag))], 1, flags.Length, JgsPackedKind.Bool),
    };

    /// <summary>Whether a value has more than one row: what R2025b refuses as "not a row vector".</summary>
    private static bool HasRows(JgsValue value) =>
        value.IsCharMatrix ? value.CharMatrixRows().Length > 1
        : value.Type is JgsType.Array or JgsType.Cell && value.ArrayLength > 0 && JgsMatrix.RowCount(value) > 1;

    private static bool IsEmptyValue(JgsValue value) =>
        (value.Type is JgsType.Array or JgsType.Cell && value.ArrayLength == 0) || (value.Type == JgsType.String && value.AsString.Length == 0);

    private static void AddUiTableBlock(IDictionary<string, GraphicsProperty> table)
    {
        Unlist(table, "TooltipString");
        AddModernCommon(table);
        AddModernRects(table, "Position", "InnerPosition", "OuterPosition");
        Put(table, "Enable",
            entry => JgsValue.Str(EnableWords[(int)TableOf(entry).Enable]),
            (entry, value, line, col) => TableOf(entry).Enable =
                (UiEnable)EnumIndex(entry, "Enable", value, EnableWords, EnableWords, line, col));

        // --- data ---
        Put(table, "Data",
            entry => JgsUiTables.StateOf(entry).Data,
            (entry, value, line, col) =>
            {
                JgsUiTables.StateOf(entry).Data = TableData(entry, value, line, col);
                JgsUiTables.Rebuild(entry);
            });
        Put(table, "DisplayData", entry => JgsUiTables.StateOf(entry).Data);

        foreach (string name in new[] { "ColumnName", "RowName" })
        {
            bool columns = name == "ColumnName";
            Put(table, name,
                entry =>
                {
                    JgsTableState state = JgsUiTables.StateOf(entry);
                    UiText? given = columns ? state.ColumnName : state.RowName;
                    if (given is not null)
                    {
                        return TextValue(given);
                    }

                    // A table's variables and row names stand in for the default (probe u8_behave).
                    if (state.Data.Type == JgsType.Table)
                    {
                        IReadOnlyList<string>? names = columns ? state.Data.AsTable.ColumnNames : state.Data.AsTable.RowNames;
                        return TextValue(new UiText(UiTextForm.Cell, names ?? []));
                    }

                    return JgsValue.Str("numbered");
                },
                (entry, value, line, col) =>
                {
                    JgsTableState state = JgsUiTables.StateOf(entry);
                    UiText names = TableNames(entry, name, value, line, col);
                    if (columns)
                    {
                        state.ColumnName = names;
                    }
                    else
                    {
                        state.RowName = names;
                    }

                    JgsUiTables.Rebuild(entry);
                });
        }

        Put(table, "ColumnWidth",
            entry => JgsUiTables.StateOf(entry).ColumnWidth,
            (entry, value, line, col) =>
            {
                SetColumnWidth(entry, value, line, col);
                JgsUiTables.Rebuild(entry);
            });

        Put(table, "ColumnEditable",
            entry => FlagsValue(JgsUiTables.StateOf(entry).Editable, JgsUiTables.StateOf(entry).EditableIsScalar),
            (entry, value, line, col) =>
            {
                JgsTableState state = JgsUiTables.StateOf(entry);
                (state.Editable, state.EditableIsScalar) = ColumnFlags(value,
                    () => TableError(entry, "ColumnEditable", "BadColumnEditableRowVector", "ColumnEditable must be a row vector", line, col),
                    () => TableError(entry, "ColumnEditable", "BadColumnEditableLogical", "ColumnEditable must be a logical or logical array", line, col));
                JgsUiTables.Rebuild(entry);
            });
        Put(table, "ColumnSortable",
            entry => FlagsValue(JgsUiTables.StateOf(entry).Sortable, JgsUiTables.StateOf(entry).SortableIsScalar),
            (entry, value, line, col) =>
            {
                JgsTableState state = JgsUiTables.StateOf(entry);
                (state.Sortable, state.SortableIsScalar) = ColumnFlags(value,
                    () => ComponentError(entry, "ColumnSortable", "MATLAB:hg:gbtdatatypes:LogicalArray:BadLogicalArrayRowVector", "Value must be a row vector", line, col),
                    () => ComponentError(entry, "ColumnSortable", "MATLAB:hg:gbtdatatypes:LogicalArray:BadLogicalArray", "Value must be a logical or logical array", line, col));
                JgsUiTables.Rebuild(entry);
            });

        Put(table, "ColumnFormat",
            entry => JgsUiTables.StateOf(entry).ColumnFormat ?? EmptyCell(),
            (entry, value, line, col) =>
            {
                JgsUiTables.StateOf(entry).ColumnFormat = ColumnFormat(entry, value, line, col);
                JgsUiTables.Rebuild(entry);
            });

        // --- look ---
        Put(table, "BackgroundColor",
            entry =>
            {
                IReadOnlyList<UiColor> stripes = TableOf(entry).Stripes;
                var data = new double[stripes.Count * 3];
                for (int i = 0; i < stripes.Count; i++)
                {
                    data[i] = stripes[i].R;
                    data[stripes.Count + i] = stripes[i].G;
                    data[(2 * stripes.Count) + i] = stripes[i].B;
                }

                JgsValue rows = JgsMatrix.FromColumnMajor(data, stripes.Count, 3);
                rows.Reshape(stripes.Count, 3);
                return rows;
            },
            (entry, value, line, col) => TableOf(entry).Stripes = StripeColors(entry, value, line, col));
        Put(table, "ForegroundColor",
            entry => JgsUiTables.StateOf(entry).InkIsNone ? JgsValue.Str("none") : UiColorValue(TableOf(entry).FontColor),
            (entry, value, line, col) =>
            {
                // 'none' is kept and read back; the text is then drawn in the colour it would have had.
                UiColor? ink = ClearableColor(entry, "ForegroundColor", value, line, col);
                JgsUiTables.StateOf(entry).InkIsNone = ink is null;
                TableOf(entry).FontColor = ink ?? UiComponentModel.DefaultFontColor;
            });
        Put(table, "RowStriping",
            entry => OnOff(TableOf(entry).RowStriping),
            (entry, value, line, col) => TableOf(entry).RowStriping = OnOffState(entry, "RowStriping", value, line, col));
        foreach (string name in new[] { "ColumnRearrangeable", "RearrangeableColumns" })
        {
            Put(table, name,
                entry => OnOff(TableOf(entry).ColumnRearrangeable),
                (entry, value, line, col) => TableOf(entry).ColumnRearrangeable = OnOffState(entry, "ColumnRearrangeable", value, line, col));
        }

        Unlist(table, "RearrangeableColumns");

        Put(table, "FontName",
            entry => JgsValue.Str(TableOf(entry).FontName),
            (entry, value, line, col) =>
            {
                // What is neither one line of text nor a scalar is refused before it is asked what it is.
                bool oneRow = value.Type is JgsType.String or JgsType.Number or JgsType.Bool or JgsType.Function or JgsType.Struct or JgsType.Table
                    || (value.Type is JgsType.Array or JgsType.Cell && !value.IsCharMatrix && !value.IsNd && value.ArrayLength > 0 && value.Rows == 1);
                if (value.Type is JgsType.Array or JgsType.Cell && value.ArrayLength == 0 && !value.IsCharMatrix)
                {
                    throw ComponentError(entry, "FontName", "MATLAB:class:MATLABConversionError", "Character vector value must not be empty", line, col);
                }

                if (!oneRow && !(value.IsStringArray && value.ArrayLength > 0))
                {
                    throw ComponentError(entry, "FontName", "MATLAB:class:MATLABConversionError", "Value must be a single-line character vector", line, col);
                }

                if (value.IsStringArray && value.ArrayLength != 1)
                {
                    throw ComponentError(entry, "FontName", "MATLAB:class:RequireScalar", "Value must be a scalar.", line, col);
                }

                TableOf(entry).FontName = FontNameOf(entry, value, line, col);
            });
        Put(table, "FontSize",
            entry => JgsValue.Number(TableOf(entry).FontSize),
            (entry, value, line, col) => TableOf(entry).FontSize = JgsBuiltins.ClassOf(value, JgsDialect.Matlab) == "logical"
                ? throw ComponentError(entry, "FontSize", "MATLAB:datatypes:PositiveWithZeroDataType:MustBeANumericScalar",
                    "Value should be a numeric scalar.", line, col)
                : FontSizeOf(entry, value, line, col));
        Put(table, "FontUnits",
            entry => JgsValue.Str(FontUnitWords[(int)TableOf(entry).FontUnits]),
            (entry, value, line, col) =>
            {
                // A change of units keeps the size the text is drawn at: FontSize is re-expressed.
                UiTableModel model = TableOf(entry);
                var units = (UiFontUnits)EnumIndex(entry, "FontUnits", value, FontUnitWords, FontUnitWords, line, col);
                double pixels = model.FontSizeInPixels();
                model.FontUnits = units;
                double one = model.FontSizeInPixels() / model.FontSize;
                model.FontSize = one > 0 && double.IsFinite(one) ? pixels / one : model.FontSize;
            });
        Put(table, "FontWeight",
            entry => JgsValue.Str(JgsUiTables.StateOf(entry).FontWeight),
            (entry, value, line, col) =>
            {
                JgsUiTables.StateOf(entry).FontWeight =
                    FontWeightWords[EnumIndex(entry, "FontWeight", value, FontWeightWords, FontWeightShown, line, col)];
                JgsUiTables.Rebuild(entry);
            });
        Put(table, "FontAngle",
            entry => JgsValue.Str(JgsUiTables.StateOf(entry).FontAngle),
            (entry, value, line, col) =>
            {
                JgsUiTables.StateOf(entry).FontAngle =
                    FontAngleWords[EnumIndex(entry, "FontAngle", value, FontAngleWords, FontAngleShown, line, col)];
                JgsUiTables.Rebuild(entry);
            });

        // --- selection ---
        Put(table, "SelectionType",
            entry => JgsValue.Str(SelectionTypeWords[(int)TableOf(entry).SelectionType]),
            (entry, value, line, col) =>
            {
                var type = (UiTableSelectionType)Array.IndexOf(SelectionTypeWords, EnumWord(entry, "SelectionType", value, SelectionTypeWords, line, col));
                if (type != TableOf(entry).SelectionType)
                {
                    // What was selected as cells means nothing as rows: the selection is let go.
                    TableOf(entry).SelectionType = type;
                    TableOf(entry).Selection = [];
                    JgsUiTables.StateOf(entry).Selection = null;
                }
            });
        Options(table, "SelectionType", SelectionTypeWords);
        Put(table, "Multiselect",
            entry => OnOff(TableOf(entry).Multiselect),
            (entry, value, line, col) => TableOf(entry).Multiselect = OnOffState(entry, "Multiselect", value, line, col));
        foreach (string name in new[] { "Selection", "DisplaySelection" })
        {
            Put(table, name,
                entry => JgsUiTables.StateOf(entry).Selection ?? JgsMatrix.FromColumnMajor([], 0, 0),
                name == "DisplaySelection" ? null : (entry, value, line, col) => SetTableSelection(entry, value, line, col));
        }

        // What the styles added to a table come to: none, until uistyle arrives (U9).
        Put(table, "StyleConfigurations", static _ => JgsValue.Table(new JGraph.Data.Table(
        [
            new JGraph.Data.TextColumn("Target", []),
            new JGraph.Data.TextColumn("TargetIndex", []),
            new JGraph.Data.TextColumn("Style", []),
        ])));

        // R2025b answers the same rectangle whatever the table holds (probe u8_more): its own
        // size and 40 pixels each way.
        Put(table, "Extent", entry =>
        {
            Rect2D box = TableOf(entry).PixelPosition();
            return Row(0, 0, box.Width + 40, box.Height + 40);
        });
        Unlist(table, "Extent");

        foreach (string name in new[]
                 {
                     "CellEditCallback", "CellSelectionCallback", "DoubleClickedFcn", "ClickedFcn", "DisplayDataChangedFcn", "SelectionChangedFcn",
                 })
        {
            AddNamedSlot(table, name);
        }

        AddCallbackSlot(table, "KeyPressFcn", static entry => entry.KeyPressFcn, static (entry, value) => entry.KeyPressFcn = value);
        AddCallbackSlot(table, "KeyReleaseFcn", static entry => entry.KeyReleaseFcn, static (entry, value) => entry.KeyReleaseFcn = value);

        // In a grid, where the table sits; anywhere else it has no layout options to take.
        Put(table, "Layout",
            LayoutValue,
            (entry, value, line, col) =>
            {
                if (GridOf(entry.Target) is null)
                {
                    throw ComponentError(entry, "Layout", "MATLAB:ui:datatypes:LayoutOptionsDatatype:InvalidClass",
                        "'Layout' value must be specified as a matlab.ui.layout.LayoutOptions object.", line, col);
                }

                SetLayout(entry, value, line, col);
            });
        WidenCallbacks(table);
    }

    /// <summary>
    /// What <c>Data</c> may be: numbers of any class, logicals, strings, a cell of scalars and
    /// text, or a table — never text itself, and never more than two dimensions.
    /// </summary>
    private static JgsValue TableData(JgsHandleEntry entry, JgsValue value, int line, int col)
    {
        JgsRuntimeException Bad(string id, string reason) => TableError(entry, "Data", id, reason, line, col);
        const string kinds = "Data must be a numeric, logical, string, cell, or table array";
        if (value.IsNd)
        {
            throw Bad("BadDataDimension", "Data must be a two-dimensional matrix.");
        }

        switch (value.Type)
        {
            case JgsType.Table when value.AsTable.RowTimes is null:
            case JgsType.Number or JgsType.Bool or JgsType.Complex:
            case JgsType.String when value.AsString.Length == 0:
                break;

            case JgsType.Sparse:
                {
                JGraph.Numerics.Sparse.CscMatrix sparse = value.AsSparse;
                JgsValue full = JgsMatrix.FromColumnMajor(sparse.ToColumnMajor(), sparse.Rows, sparse.Cols);
                JgsLifetime.Pin(full);
                return full;
            }


            case JgsType.Array when value.IsCharMatrix || value.IsTime:
                throw Bad("BadDataType", kinds);

            case JgsType.Array:
                break;

            case JgsType.Cell:
                foreach (JgsValue element in value.AsCell)
                {
                    bool text = element.Type == JgsType.String || element.IsCharMatrix;
                    bool number = element.Type is JgsType.Number or JgsType.Bool or JgsType.Complex
                        || (element.Type == JgsType.Array && !element.IsStringArray && !element.IsTime);
                    if (!text && !number)
                    {
                        throw Bad("BadDataCellArrayType", "Values within a cell array must be numeric, logical, or char");
                    }

                    if (number && element.Type == JgsType.Array && element.ArrayLength > 1)
                    {
                        throw Bad("BadDataCellArraySize", "Data within a cell array must have size [1 1]");
                    }
                }

                break;

            default:
                throw Bad("BadDataType", kinds);
        }

        JgsValue kept = JgsValue.Share(value);
        JgsLifetime.Pin(kept);
        return kept;
    }

    /// <summary>
    /// <c>ColumnName</c> and <c>RowName</c>: a character row or matrix kept as it is, a cell or a
    /// string array as a column of names, and numbers as the text <c>num2str</c> writes them.
    /// </summary>
    private static UiText TableNames(JgsHandleEntry entry, string property, JgsValue value, int line, int col)
    {
        const string prefix = "MATLAB:hg:datatypes:NumericOrStringDataType:";
        if (value.Type == JgsType.String)
        {
            return UiText.Of(MissingAsEmpty(value.AsString));
        }

        if (value.IsCharMatrix)
        {
            return new UiText(UiTextForm.CharMatrix, value.CharMatrixRows());
        }

        if (value.IsStringArray)
        {
            string[] names = [.. Enumerable.Range(0, value.ArrayLength).Select(i => MissingAsEmpty(JgsBuiltins.TextOf(value.ElementAt(i))))];
            return names.Length == 1 ? UiText.Of(names[0]) : new UiText(UiTextForm.Cell, names);
        }

        if (value.Type == JgsType.Cell)
        {
            var names = new List<string>();
            foreach (JgsValue element in value.AsCell)
            {
                if (JgsBuiltins.IsTextScalar(element))
                {
                    names.Add(MissingAsEmpty(JgsBuiltins.TextOf(element)));
                }
                else if (element.Type == JgsType.Number)
                {
                    names.Add(NumberLine(element.AsNumber, line, col));
                }
                else if (element.Type == JgsType.Array && element.ArrayLength == 0 && !element.IsStringArray)
                {
                    names.Add(string.Empty);
                }
                else
                {
                    throw ComponentError(entry, property, prefix + "InvalidCellArray",
                        "Cell array can contain only non-empty character vectors, string vectors, or numbers.", line, col);
                }
            }

            return new UiText(UiTextForm.Cell, names);
        }

        string kind = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
        if (kind == "logical" || !IsNumericKind(kind) || value.Type is JgsType.Complex or JgsType.Table or JgsType.Sparse)
        {
            throw ComponentError(entry, property, prefix + "ArrayClass",
                "Value must be a character vector, categorical array, string array, numeric array, or cell array of character vectors.", line, col);
        }

        if (value.Type == JgsType.Number)
        {
            return UiText.Of(NumberLine(value.AsNumber, line, col));
        }

        // A vector of numbers is a column of their texts, set to the right; a matrix names nothing.
        if (value.IsNd || value.ArrayLength == 0 || (value.Rows > 1 && value.Cols > 1))
        {
            return UiText.Empty;
        }

        string[] lines = [.. JgsBuiltins.ToDoubles(property, value, line, col).Select(number => NumberLine(number, line, col))];
        int width = lines.Max(static l => l.Length);
        return lines.Length == 1 ? UiText.Of(lines[0]) : new UiText(UiTextForm.CharMatrix, [.. lines.Select(l => l.PadRight(width))]);
    }

    private static (bool[]? Flags, bool Scalar) ColumnFlags(JgsValue value, Func<JgsRuntimeException> notARow, Func<JgsRuntimeException> notLogical)
    {
        if (value.IsStringArray)
        {
            throw notLogical();
        }

        if (HasRows(value))
        {
            throw notARow();
        }

        if (IsEmptyValue(value))
        {
            return (null, false);
        }

        if (value.Type == JgsType.Bool)
        {
            return ([value.IsTruthy], true);
        }

        if (value.Type == JgsType.Array && JgsBuiltins.IsLogicalValue(value))
        {
            return ([.. Enumerable.Range(0, value.ArrayLength).Select(i => value.ElementAt(i).IsTruthy)], false);
        }

        throw notLogical();
    }

    private static JgsValue? ColumnFormat(JgsHandleEntry entry, JgsValue value, int line, int col)
    {
        JgsRuntimeException Bad(string id, string reason) => TableError(entry, "ColumnFormat", id, reason, line, col);
        if (IsEmptyValue(value) && !value.IsStringArray)
        {
            return null;
        }

        if (value.Type != JgsType.Cell)
        {
            throw Bad("BadColumnFormatCellArray", "ColumnFormat must be a cell array");
        }

        if (HasRows(value))
        {
            throw Bad("BadColumnFormatRowVector", "ColumnFormat must be a row vector");
        }

        foreach (JgsValue element in value.AsCell)
        {
            if (element.Type == JgsType.Cell)
            {
                if (element.ArrayLength == 0 || HasRows(element))
                {
                    throw Bad("BadColumnFormatPopupmenuRowVector", "ColumnFormat popupmenu definitions must be row vectors");
                }

                if (element.AsCell.Any(static choice => choice.Type != JgsType.String))
                {
                    throw Bad("BadColumnFormatChar", "ColumnFormat popupmenu definitions must contain only char values");
                }
            }
            else if (element.Type == JgsType.String)
            {
                string word = element.AsString.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
                if (word.Length > 0 && Array.IndexOf(ColumnFormatWords, word) < 0)
                {
                    throw Bad("BadColumnFormatIncorrectDataType",
                        "ColumnFormat definitions must be either 'numeric', 'logical', 'char', 'short', 'long', 'short e', 'long e', 'short g', 'long g', 'short eng', 'long eng', 'bank', '+', 'rat' or be a popupmenu definition");
                }
            }
            else if (!(element.Type == JgsType.Array && element.ArrayLength == 0 && !element.IsStringArray))
            {
                throw Bad("BadColumnFormatInvalidDataType",
                    "ColumnFormat definitions must be either 'numeric', 'logical', 'char', or be a popupmenu definition");
            }
        }

        JgsValue kept = JgsValue.Share(value);
        JgsLifetime.Pin(kept);
        return kept;
    }

    /// <summary>
    /// <c>ColumnWidth</c>: <c>'auto'</c>, <c>'fit'</c> or a weight for every column, or a row cell
    /// of those and pixel widths, one a column. It reads back as it was written.
    /// </summary>
    private static void SetColumnWidth(JgsHandleEntry entry, JgsValue value, int line, int col)
    {
        const string words =
            "'ColumnWidth' must be an array containing 'fit', 'auto', positive numbers, or positive integers paired with 'x'. You can specify 'ColumnWidth' as a cell array containing any combination of values, or as a string array when all elements are of the same type.";
        JgsRuntimeException Bad() => TableError(entry, "ColumnWidth", "BadColumnWidthValue", words, line, col);
        if (HasRows(value))
        {
            throw TableError(entry, "ColumnWidth", "BadColumnWidthRowVector", "ColumnWidth must be a row vector", line, col);
        }

        static UiGridTrack? TrackOfWord(string text)
        {
            string word = text.Trim().ToLowerInvariant();
            if (word == "auto")
            {
                return new UiGridTrack(UiGridTrackKind.Weight, 1);
            }

            if (word == "fit")
            {
                return new UiGridTrack(UiGridTrackKind.Fit, 0);
            }

            return word.EndsWith('x') && word.Length > 1 && word[..^1].All(char.IsAsciiDigit)
                ? new UiGridTrack(UiGridTrackKind.Weight, double.Parse(word[..^1], System.Globalization.CultureInfo.InvariantCulture))
                : null;
        }

        JgsTableState state = JgsUiTables.StateOf(entry);
        if (value.Type == JgsType.String)
        {
            UiGridTrack track = TrackOfWord(value.AsString) ?? throw Bad();
            state.ColumnWidth = value;
            state.Widths = [track];
            state.OneWidthForAll = true;
            return;
        }

        JgsValue[] elements;
        if (value.IsStringArray)
        {
            elements = [.. Enumerable.Range(0, value.ArrayLength).Select(i => JgsValue.Str(JgsBuiltins.TextOf(value.ElementAt(i))))];
        }
        else if (value.Type == JgsType.Cell && value.ArrayLength > 0)
        {
            elements = [.. value.AsCell.Select(static element => JgsBuiltins.IsTextScalar(element) ? JgsValue.Str(JgsBuiltins.TextOf(element)) : element)];
        }
        else
        {
            throw Bad();
        }

        var tracks = new List<UiGridTrack>(elements.Length);
        foreach (JgsValue element in elements)
        {
            if (element.Type == JgsType.String)
            {
                tracks.Add(TrackOfWord(element.AsString) ?? throw Bad());
            }
            else if (element.Type == JgsType.Number && !(element.AsNumber < 0))
            {
                double pixels = element.AsNumber;
                tracks.Add(double.IsFinite(pixels) ? new UiGridTrack(UiGridTrackKind.Fixed, pixels) : new UiGridTrack(UiGridTrackKind.Weight, 1));
            }
            else
            {
                throw Bad();
            }
        }

        JgsValue kept = JgsValue.Cell(elements);
        kept.Reshape(1, elements.Length);
        state.ColumnWidth = kept;
        state.Widths = tracks;
        state.OneWidthForAll = false;
    }

    /// <summary>A table's row colours: RGB triplets as rows, or one or several colour names.</summary>
    private static IReadOnlyList<UiColor> StripeColors(JgsHandleEntry entry, JgsValue value, int line, int col)
    {
        JgsRuntimeException Bad() => ComponentError(entry, "BackgroundColor", "MATLAB:datatypes:RGBMatrixColor:InvalidColor",
            "Invalid color. Specify color as an n-by-3 array of RGB triplets, or a 1-D array of color names or hexadecimal color codes.", line, col);
        UiColor Named(string word) =>
            !word.Trim().Equals("none", StringComparison.OrdinalIgnoreCase) && word.Trim().Length > 0 && NamedColor(word.Trim()) is { } named
                ? named
                : throw Bad();

        if (value.Type == JgsType.String)
        {
            return [Named(value.AsString)];
        }

        if (value.Type == JgsType.Cell || value.IsStringArray)
        {
            if (value.ArrayLength > 0 && value.Rows > 1 && value.Cols > 1)
            {
                throw Bad();
            }

            var named = new List<UiColor>();
            for (int i = 0; i < value.ArrayLength; i++)
            {
                JgsValue element = value.Type == JgsType.Cell ? value.AsCell[i] : value.ElementAt(i);
                named.Add(JgsBuiltins.IsTextScalar(element) ? Named(JgsBuiltins.TextOf(element)) : throw Bad());
            }

            return named;
        }

        string kind = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
        if (kind == "logical" || !IsNumericKind(kind) || value.Type is JgsType.Complex or JgsType.Number || value.IsNd || value.Cols != 3)
        {
            throw Bad();
        }

        double scale = kind switch
        {
            "uint8" => 255,
            "uint16" => 65535,
            _ => 1,
        };
        var colors = new List<UiColor>(value.Rows);
        for (int r = 0; r < value.Rows; r++)
        {
            double[] rgb = [.. Enumerable.Range(0, 3).Select(c => JgsMatrix.At(value, r, c).AsNumber / scale)];
            if (rgb.Any(static channel => !(channel >= 0 && channel <= 1)))
            {
                throw ComponentError(entry, "BackgroundColor", "MATLAB:hg:ColorBase:BadColorValue",
                    "Invalid RGB triplet. Specify a three-element vector of values between 0 and 1.", line, col);
            }

            colors.Add(new UiColor(rgb[0], rgb[1], rgb[2]));
        }

        return colors;
    }

    /// <summary>
    /// <c>Selection</c>: pairs of row and column for cells, a row of rows or of columns otherwise,
    /// all within the data. R2025b refuses these without naming the property.
    /// </summary>
    private static void SetTableSelection(JgsHandleEntry entry, JgsValue value, int line, int col)
    {
        const string prefix = "MATLAB:hg:gbtdatatypes:TableSelection:";
        UiTableModel model = TableOf(entry);
        JgsTableState state = JgsUiTables.StateOf(entry);
        if (IsEmptyValue(value) && !value.IsStringArray)
        {
            state.Selection = null;
            model.Selection = [];
            return;
        }

        bool cells = model.SelectionType == UiTableSelectionType.Cell;
        string kind = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
        bool numeric = kind != "logical" && IsNumericKind(kind) && value.Type is JgsType.Number or JgsType.Array && !value.IsNd;
        int rows = value.Type == JgsType.Number ? 1 : value.Rows;
        int columns = value.Type == JgsType.Number ? 1 : value.Cols;
        if (cells && (!numeric || columns != 2))
        {
            throw new JgsRuntimeException(line, col, prefix + "InvalidCellSelection", "Selection indices for cells must be a N-by-2 numeric array.");
        }

        if (!cells && (!numeric || rows != 1))
        {
            throw model.SelectionType == UiTableSelectionType.Row
                ? new JgsRuntimeException(line, col, prefix + "InvalidRowSelection", "Selection indices for rows must be a 1-by-N numeric array.")
                : new JgsRuntimeException(line, col, prefix + "InvalidColumnSelection", "Selection indices for columns must be a 1-by-N numeric array.");
        }

        if (!model.Multiselect && (cells ? rows : columns) > 1)
        {
            throw new JgsRuntimeException(line, col, prefix + "InvalidSingleSelection",
                "When 'Multiselect' is 'off', 'Selection' must be empty, or a scalar number for 'row' and 'column' selection type, or a 1-by-2 numeric array for 'cell' selection type.");
        }

        (int dataRows, int dataColumns) = JgsUiTables.SizeOf(state.Data);
        var picked = new List<int>(rows * columns);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                double index = value.Type == JgsType.Number ? value.AsNumber : JgsMatrix.At(value, r, c).AsNumber;
                int limit = cells ? (c == 0 ? dataRows : dataColumns) : model.SelectionType == UiTableSelectionType.Row ? dataRows : dataColumns;
                if (!(index >= 1 && index <= limit))
                {
                    throw new JgsRuntimeException(line, col, prefix + "SelectionOutOfBoundary", "Selection indices are out of data boundary.");
                }

                picked.Add((int)System.Math.Round(index, MidpointRounding.AwayFromZero) - 1);
            }
        }

        JgsValue kept = JgsValue.Share(value);
        JgsLifetime.Pin(kept);
        state.Selection = kept;
        model.Selection = picked;
    }
}
