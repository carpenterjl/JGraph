using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// What a person did to a table, a tab group or a toolbar tool (app-building plan, U8), turned
/// into the model's new state and the callbacks that are owed. As for the components of U5 the
/// state is written first and always, and the event data is made with what it then is.
/// <para>
/// R2025b runs none of these callbacks headless. The event classes and their properties are its
/// own (<c>meta.class</c>, probe <c>u8_behave</c>); what each holds after an edit follows
/// MathWorks' documentation of <c>CellEditCallback</c>, and the order of a toggle tool's
/// callbacks its documentation of <c>ClickedCallback</c>.
/// </para>
/// </summary>
internal static partial class JgsUiComponentEvents
{
    /// <summary>The actions a window reports for the objects of U8.</summary>
    public const string CellEdit = "celledit";
    public const string CellSelect = "cellselect";
    public const string TabPicked = "tab";

    private static JgsValue Empty => JgsMatrix.FromColumnMajor([], 0, 0);

    [ThreadStatic]
    private static List<GraphicsEvent>? _after;

    /// <summary>
    /// The callbacks owed after the one <see cref="Prepare"/> answered, in order: a toggle tool's
    /// <c>ClickedCallback</c> after its <c>OnCallback</c>, a table's <c>CellSelectionCallback</c>
    /// after its <c>SelectionChangedFcn</c>. Taking them empties the list.
    /// </summary>
    public static IReadOnlyList<GraphicsEvent> TakeFollowUps()
    {
        if (_after is not { Count: > 0 } owed)
        {
            return [];
        }

        _after = null;
        return owed;
    }

    private static void Then(GraphicsEvent owed) => (_after ??= []).Add(owed);

    /// <summary>Whether an event already names the callback it is owed and carries its event data.</summary>
    private static bool IsOwedAsItStands(GraphicsEvent raised) =>
        raised.Interim is JgsValue
        && (raised.Action.EndsWith("Fcn", StringComparison.Ordinal) || raised.Action.EndsWith("Callback", StringComparison.Ordinal));

    private static GraphicsEvent? PrepareBars(GraphicsEvent raised)
    {
        switch (raised.Target)
        {
            case UiTabGroupModel { BeingDeleted: false } group when raised.Action == TabPicked && raised.Interim is UiTabModel picked:
            {
                UiTabModel? old = group.SelectedTab;
                if (!ReferenceEquals(picked.Parent, group) || ReferenceEquals(old, picked))
                {
                    return null;
                }

                group.Select(picked);
                return new GraphicsEvent(GraphicsEventKind.GroupSelectionChanged, group, Clicked: picked, ContextObject: old);
            }

            case UiToolModel { BeingDeleted: false } tool when raised.Action == Pushed && JgsHandleRegistry.TryGetEntry(tool, out _):
            {
                JgsValue source = JgsHandleRegistry.For(tool);
                tool.UserWriteSeq = raised.UserSeq;
                if (!tool.IsToggle)
                {
                    return raised with { Action = "ClickedCallback", Interim = JgsUiEventData.Action(source) };
                }

                // A toggle tool goes down or comes up, runs the callback of that, and then the one
                // it shares with a push tool.
                tool.State = !tool.State;
                Then(raised with { Action = "ClickedCallback", Interim = JgsUiEventData.Action(source) });
                return raised with { Action = tool.State ? "OnCallback" : "OffCallback", Interim = JgsUiEventData.Action(source) };
            }

            default:
                return null;
        }
    }

    private static GraphicsEvent? PrepareTable(GraphicsEvent raised, UiTableModel table, JgsHandleEntry entry, JgsValue source)
    {
        switch (raised.Action)
        {
            case CellEdit when raised.Interim is object[] { Length: 3 } edit && edit[0] is int shownRow && edit[1] is int shownColumn && edit[2] is not null:
            {
                // The window names the cell as shown; the data cell is where the edit lands (open item 60).
                JgsTableState state = JgsUiTables.StateOf(entry);
                (int rows, int columns) = JgsUiTables.SizeOf(state.Data);
                int row = JgsUiTables.DataRow(state, shownRow);
                int column = JgsUiTables.DataColumn(state, shownColumn);
                table.UserWriteSeq = raised.UserSeq;
                if (row < 0 || row >= rows || column < 0 || column >= columns)
                {
                    return null;
                }

                JgsUiTables.Edit made = JgsUiTables.Apply(entry, row, column, edit[2]);
                JgsValue indices = JgsGraphicsProperties.Row(row + 1, column + 1);
                return raised with
                {
                    Action = "CellEditCallback",
                    Interim = JgsUiEventData.Make(Prefix + "CellEditData", source, "CellEdit", new()
                    {
                        ["Indices"] = indices,
                        ["DisplayIndices"] = JgsGraphicsProperties.Row(shownRow + 1, shownColumn + 1),
                        ["PreviousData"] = made.Previous,
                        ["EditData"] = made.Typed,
                        ["NewData"] = made.Stored ?? Empty,
                        ["Error"] = made.Error.Length == 0 ? Empty : JgsValue.Str(made.Error),
                    }),
                };
            }

            case CellSelect when raised.Interim is int[] picked:
                return Select(raised, table, entry, source, picked);

            case Clicked or DoubleClicked:
            {
                bool twice = raised.Action == DoubleClicked;
                int[] at = raised.Interim as int[] ?? [];
                JgsTableState state = JgsUiTables.StateOf(entry);
                JgsValue Index(int position) => at.Length > position && at[position] >= 0 ? JgsValue.Number(at[position] + 1) : Empty;
                JgsValue Data(int position, Func<JgsTableState, int, int> map) =>
                    at.Length > position && at[position] >= 0 ? JgsValue.Number(map(state, at[position]) + 1) : Empty;
                JgsValue information = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
                {
                    ["DisplayRow"] = Index(0),
                    ["DisplayColumn"] = Index(1),
                    ["Row"] = Data(0, JgsUiTables.DataRow),
                    ["Column"] = Data(1, JgsUiTables.DataColumn),
                    ["RowHeader"] = JgsValue.Bool(at.Length > 2 && at[2] == 1),
                    ["ColumnHeader"] = JgsValue.Bool(at.Length > 2 && at[2] == 2),
                    ["Location"] = Empty,
                    ["ScreenLocation"] = Empty,
                });
                return raised with
                {
                    Action = twice ? "DoubleClickedFcn" : "ClickedFcn",
                    Interim = JgsUiEventData.Make(Prefix + (twice ? "DoubleClickedData" : "ClickedData"), source,
                        twice ? "DoubleClicked" : "Clicked", new() { ["InteractionInformation"] = information }),
                };
            }

            // A person sorted by a column or dragged one (open item 60): the rows or columns take
            // their new order, the picture is made again, and DisplayDataChangedFcn is told what
            // R2025b's DisplayDataChangedData tells — the headings as shown, the selection as shown
            // before and after, the interaction, and the column it was on.
            case Sorted when raised.Interim is int[] { Length: 2 } sort:
            case Rearranged when raised.Interim is int[]:
            {
                JgsTableState state = JgsUiTables.StateOf(entry);
                JgsValue selectionBefore = DisplaySelectionOf(table, state);
                int displayColumn;
                if (raised.Action == Sorted)
                {
                    int[] sorted = (int[])raised.Interim!;
                    displayColumn = sorted[0];
                    JgsUiTables.Sort(entry, sorted[0], sorted[1] < 0);
                }
                else
                {
                    int[] order = (int[])raised.Interim!;
                    int moved = 0;
                    for (int c = 0; c < order.Length; c++)
                    {
                        if (order[c] != c)
                        {
                            moved = c;
                            break;
                        }
                    }

                    JgsUiTables.Rearrange(entry, order);
                    displayColumn = moved;
                }

                table.UserWriteSeq = raised.UserSeq;
                table.Selection = ShownSelection(table, state);
                int dataColumn = JgsUiTables.DataColumn(state, displayColumn);
                (IReadOnlyList<string> rowNames, IReadOnlyList<string> columnNames) = JgsUiTables.DisplayNames(state);
                string variable = state.Data.Type == JgsType.Table && dataColumn < state.Data.AsTable.ColumnCount
                    ? state.Data.AsTable.ColumnNames[dataColumn]
                    : string.Empty;
                return raised with
                {
                    Action = "DisplayDataChangedFcn",
                    Interim = JgsUiEventData.Make(Prefix + "DisplayDataChangedData", source, "DisplayDataChanged", new()
                    {
                        ["DisplayRowName"] = CellColumnOf(rowNames),
                        ["DisplayColumnName"] = CellRowOf(columnNames),
                        ["DisplaySelection"] = DisplaySelectionOf(table, state),
                        ["PreviousDisplaySelection"] = selectionBefore,
                        ["Interaction"] = JgsValue.Str(raised.Action == Sorted ? "sort" : "rearrange"),
                        ["InteractionColumn"] = JgsValue.Number(dataColumn + 1),
                        ["InteractionDisplayColumn"] = JgsValue.Number(displayColumn + 1),
                        ["InteractionVariable"] = JgsValue.Str(variable),
                    }),
                };
            }

            default:
                return null;
        }
    }

    /// <summary>The actions a window reports when a person sorts a table's rows or drags a column (open item 60).</summary>
    public const string Sorted = "sorted";
    public const string Rearranged = "rearranged";

    private static JgsValue CellColumnOf(IReadOnlyList<string> names)
    {
        JgsValue cell = JgsValue.Cell([.. names.Select(JgsValue.Str)]);
        cell.Reshape(names.Count, names.Count == 0 ? 0 : 1);
        return cell;
    }

    private static JgsValue CellRowOf(IReadOnlyList<string> names)
    {
        JgsValue cell = JgsValue.Cell([.. names.Select(JgsValue.Str)]);
        cell.Reshape(names.Count == 0 ? 0 : 1, names.Count);
        return cell;
    }

    /// <summary>
    /// The selection as the window shows it: the data selection the script side keeps, mapped
    /// through the display order — pairs for cells, positions for rows or columns, from 0.
    /// </summary>
    internal static int[] ShownSelection(UiTableModel table, JgsTableState state)
    {
        if (state.Selection is not { } selection)
        {
            return [];
        }

        bool cells = table.SelectionType == UiTableSelectionType.Cell;
        int rows = selection.Type == JgsType.Number ? 1 : selection.Rows;
        int columns = selection.Type == JgsType.Number ? 1 : selection.Cols;
        var shown = new List<int>();
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                double index = selection.Type == JgsType.Number ? selection.AsNumber : JgsMatrix.At(selection, r, c).AsNumber;
                int data = (int)Math.Round(index, MidpointRounding.AwayFromZero) - 1;
                bool rowIndex = cells ? c == 0 : table.SelectionType == UiTableSelectionType.Row;
                shown.Add(rowIndex ? JgsUiTables.DisplayRow(state, data) : JgsUiTables.DisplayColumn(state, data));
            }
        }

        return [.. shown];
    }

    /// <summary>
    /// <c>DisplaySelection</c>: the selection as the window shows it, from 1, in the shape
    /// <c>Selection</c> has. It is the model's own display selection rather than a map of the data
    /// selection: R2025b leaves it where it was when a script writes the hidden order properties
    /// (fixture oi_axes_box_styles_tables_scroll), and moves it when the window sorts or selects.
    /// </summary>
    internal static JgsValue DisplaySelectionOf(UiTableModel table, JgsTableState state)
    {
        _ = state;
        int[] shown = [.. table.Selection];
        if (shown.Length == 0)
        {
            return Empty;
        }

        if (table.SelectionType == UiTableSelectionType.Cell)
        {
            int count = shown.Length / 2;
            var data = new double[count * 2];
            for (int i = 0; i < count; i++)
            {
                data[i] = shown[2 * i] + 1;
                data[count + i] = shown[(2 * i) + 1] + 1;
            }

            return JgsMatrix.FromColumnMajor(data, count, 2);
        }

        return JgsGraphicsProperties.Row([.. shown.Select(static index => (double)(index + 1))]);
    }

    /// <summary>
    /// A person changed what is selected: cells as pairs of row and column, or rows, or columns,
    /// all from 0 and as shown. The table's <c>Selection</c> takes it in data positions (open item
    /// 60); <c>CellSelectionCallback</c> is told the cells and <c>SelectionChangedFcn</c> the
    /// selection before and after, in both reckonings.
    /// </summary>
    private static GraphicsEvent? Select(GraphicsEvent raised, UiTableModel table, JgsHandleEntry entry, JgsValue source, int[] picked)
    {
        JgsTableState state = JgsUiTables.StateOf(entry);
        (int rows, int columns) = JgsUiTables.SizeOf(state.Data);
        bool cells = table.SelectionType == UiTableSelectionType.Cell;
        JgsValue before = state.Selection ?? Empty;
        JgsValue shownBefore = DisplaySelectionOf(table, state);
        JgsValue after;
        JgsValue shownAfter;
        var cellPairs = new List<(int Row, int Column)>();
        var shownPairs = new List<(int Row, int Column)>();
        if (cells)
        {
            int count = picked.Length / 2;
            var data = new double[count * 2];
            var shownData = new double[count * 2];
            for (int i = 0; i < count; i++)
            {
                int row = JgsUiTables.DataRow(state, picked[2 * i]);
                int column = JgsUiTables.DataColumn(state, picked[(2 * i) + 1]);
                data[i] = row + 1;
                data[count + i] = column + 1;
                shownData[i] = picked[2 * i] + 1;
                shownData[count + i] = picked[(2 * i) + 1] + 1;
                cellPairs.Add((row, column));
                shownPairs.Add((picked[2 * i], picked[(2 * i) + 1]));
            }

            after = count == 0 ? Empty : JgsMatrix.FromColumnMajor(data, count, 2);
            shownAfter = count == 0 ? Empty : JgsMatrix.FromColumnMajor(shownData, count, 2);
        }
        else
        {
            bool byRow = table.SelectionType == UiTableSelectionType.Row;
            int[] data = [.. picked.Select(index => byRow ? JgsUiTables.DataRow(state, index) : JgsUiTables.DataColumn(state, index))];
            after = picked.Length == 0 ? Empty : JgsGraphicsProperties.Row([.. data.Select(static index => (double)(index + 1))]);
            shownAfter = picked.Length == 0 ? Empty : JgsGraphicsProperties.Row([.. picked.Select(static index => (double)(index + 1))]);
            for (int i = 0; i < picked.Length; i++)
            {
                for (int other = 0; other < (byRow ? columns : rows); other++)
                {
                    cellPairs.Add(byRow ? (data[i], JgsUiTables.DataColumn(state, other)) : (JgsUiTables.DataRow(state, other), data[i]));
                    shownPairs.Add(byRow ? (picked[i], other) : (other, picked[i]));
                }
            }
        }

        table.UserWriteSeq = raised.UserSeq;
        if (JgsStdlib.DeepEquals(before, after, nanEqual: true))
        {
            return null;
        }

        JgsLifetime.Pin(after);
        state.Selection = picked.Length == 0 ? null : after;
        table.Selection = picked;

        JgsValue indices = PairsValue(cellPairs);
        if (entry.NamedCallbacks.ContainsKey("CellSelectionCallback"))
        {
            Then(raised with
            {
                Action = "CellSelectionCallback",
                Interim = JgsUiEventData.Make(Prefix + "CellSelectionChangeData", source, "CellSelection", new()
                {
                    ["Indices"] = indices,
                    ["DisplayIndices"] = PairsValue(shownPairs),
                }),
            });
        }

        string[] words = ["cell", "row", "column"];
        return raised with
        {
            Action = "SelectionChangedFcn",
            Interim = JgsUiEventData.Make(Prefix + "TableSelectionChangedData", source, "SelectionChanged", new()
            {
                ["Selection"] = after,
                ["PreviousSelection"] = before,
                ["SelectionType"] = JgsValue.Str(words[(int)table.SelectionType]),
                ["DisplaySelection"] = shownAfter,
                ["PreviousDisplaySelection"] = shownBefore,
            }),
        };
    }

    private static JgsValue PairsValue(List<(int Row, int Column)> pairs)
    {
        if (pairs.Count == 0)
        {
            return Empty;
        }

        var data = new double[pairs.Count * 2];
        for (int i = 0; i < pairs.Count; i++)
        {
            data[i] = pairs[i].Row + 1;
            data[pairs.Count + i] = pairs[i].Column + 1;
        }

        return JgsMatrix.FromColumnMajor(data, pairs.Count, 2);
    }
}
