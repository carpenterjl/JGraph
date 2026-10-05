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
            case CellEdit when raised.Interim is object[] { Length: 3 } edit && edit[0] is int row && edit[1] is int column && edit[2] is not null:
            {
                (int rows, int columns) = JgsUiTables.SizeOf(JgsUiTables.StateOf(entry).Data);
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
                        ["DisplayIndices"] = indices,
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
                JgsValue Index(int position) => at.Length > position && at[position] >= 0 ? JgsValue.Number(at[position] + 1) : Empty;
                JgsValue information = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
                {
                    ["DisplayRow"] = Index(0),
                    ["DisplayColumn"] = Index(1),
                    ["Row"] = Index(0),
                    ["Column"] = Index(1),
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

            default:
                return null;
        }
    }

    /// <summary>
    /// A person changed what is selected: cells as pairs of row and column, or rows, or columns,
    /// all from 0. The table's <c>Selection</c> takes it; <c>CellSelectionCallback</c> is told the
    /// cells and <c>SelectionChangedFcn</c> the selection before and after.
    /// </summary>
    private static GraphicsEvent? Select(GraphicsEvent raised, UiTableModel table, JgsHandleEntry entry, JgsValue source, int[] picked)
    {
        JgsTableState state = JgsUiTables.StateOf(entry);
        (int rows, int columns) = JgsUiTables.SizeOf(state.Data);
        bool cells = table.SelectionType == UiTableSelectionType.Cell;
        JgsValue before = state.Selection ?? Empty;
        JgsValue after;
        var cellPairs = new List<(int Row, int Column)>();
        if (cells)
        {
            int count = picked.Length / 2;
            var data = new double[count * 2];
            for (int i = 0; i < count; i++)
            {
                data[i] = picked[2 * i] + 1;
                data[count + i] = picked[(2 * i) + 1] + 1;
                cellPairs.Add((picked[2 * i], picked[(2 * i) + 1]));
            }

            after = count == 0 ? Empty : JgsMatrix.FromColumnMajor(data, count, 2);
        }
        else
        {
            after = picked.Length == 0 ? Empty : JgsGraphicsProperties.Row([.. picked.Select(static index => (double)(index + 1))]);
            foreach (int index in picked)
            {
                for (int other = 0; other < (table.SelectionType == UiTableSelectionType.Row ? columns : rows); other++)
                {
                    cellPairs.Add(table.SelectionType == UiTableSelectionType.Row ? (index, other) : (other, index));
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

        var indexData = new double[cellPairs.Count * 2];
        for (int i = 0; i < cellPairs.Count; i++)
        {
            indexData[i] = cellPairs[i].Row + 1;
            indexData[cellPairs.Count + i] = cellPairs[i].Column + 1;
        }

        JgsValue indices = cellPairs.Count == 0 ? Empty : JgsMatrix.FromColumnMajor(indexData, cellPairs.Count, 2);
        if (entry.NamedCallbacks.ContainsKey("CellSelectionCallback"))
        {
            Then(raised with
            {
                Action = "CellSelectionCallback",
                Interim = JgsUiEventData.Make(Prefix + "CellSelectionChangeData", source, "CellSelection", new()
                {
                    ["Indices"] = indices,
                    ["DisplayIndices"] = indices,
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
                ["DisplaySelection"] = after,
                ["PreviousDisplaySelection"] = before,
            }),
        };
    }
}
