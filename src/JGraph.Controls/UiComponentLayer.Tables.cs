using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Rendering.Layout;
using JGraph.Scripting;

namespace JGraph.Controls;

/// <summary>
/// The table and the tab headings of the layer (app-building plan, U8). A <c>uitable</c> is a WPF
/// <see cref="DataGrid"/> fed from the table's frame: the cells are the picture the script side
/// worked out, an edit goes back as the text typed or the box ticked, and the data itself never
/// leaves the script thread. A tab group's headings are a <see cref="TabControl"/> laid along the
/// strip the layout left for them; the pages are drawn below, with every other panel.
/// </summary>
public sealed partial class UiComponentLayer
{
    /// <summary>One cell as the grid binds to it.</summary>
    private sealed class CellView(RowView row, int column) : INotifyPropertyChanged
    {
        private string _text = string.Empty;
        private bool _checked;

        public event PropertyChangedEventHandler? PropertyChanged;

        public RowView Row { get; } = row;

        public int Column { get; } = column;

        public UiTableCellKind Kind { get; set; }

        public TextAlignment Alignment => Kind == UiTableCellKind.Number ? TextAlignment.Right : TextAlignment.Left;

        public string Text
        {
            get => _text;
            set
            {
                if (_text != value)
                {
                    _text = value ?? string.Empty;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
                    Row.Owner.Edited(this, _text);
                }
            }
        }

        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked != value)
                {
                    _checked = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Checked)));
                    Row.Owner.Edited(this, value);
                }
            }
        }

        /// <summary>Shows a cell of a frame, telling nobody.</summary>
        public void Show(UiTableCell cell)
        {
            Kind = cell.Kind;
            _text = cell.Text;
            _checked = cell.Kind == UiTableCellKind.Checked;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }

    /// <summary>One row as the grid binds to it: where it stands in the data, its heading, its cells.</summary>
    private sealed class RowView(TableView owner, int index)
    {
        public TableView Owner { get; } = owner;

        public int Index { get; } = index;

        public string Header { get; set; } = string.Empty;

        public CellView[] Cells { get; set; } = [];
    }

    /// <summary>What a table's grid keeps between frames.</summary>
    private sealed class TableView(Shown shown, DataGrid grid)
    {
        public Shown Shown { get; } = shown;

        public DataGrid Grid { get; } = grid;

        public ObservableCollection<RowView> Rows { get; } = [];

        public UiTableContent? Content { get; set; }

        public IReadOnlyList<UiColor> Stripes { get; set; } = [];

        public IReadOnlyList<UiStyleRule> Styles { get; set; } = [];

        public int ScrollSeen { get; set; }

        /// <summary>A person put something in a cell: the script side hears what, and where in the data.</summary>
        public void Edited(CellView cell, object value)
        {
            if (!Shown.Applying)
            {
                Tell(Shown, "celledit", new object[] { cell.Row.Index, cell.Column, value });
            }
        }
    }

    private readonly Dictionary<UiTableModel, TableView> _tables = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<UiTabGroupModel, TabControl> _tabStrips = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<TabControl> _applyingTabs = [];

    private Shown MakeTable(UiComponentFrame frame)
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserResizeRows = false,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.Cell,
            GridLinesVisibility = DataGridGridLinesVisibility.All,
            HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xD9)),
            VerticalGridLinesBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xD9)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x7D, 0x7D, 0x7D)),
            BorderThickness = new System.Windows.Thickness(1),
            Background = Brushes.White,
            RowHeaderWidth = double.NaN,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var shown = new Shown((UiTableModel)frame.Source, frame.Kind, grid) { Input = grid };
        var view = new TableView(shown, grid);
        _tables[(UiTableModel)frame.Source] = view;
        grid.ItemsSource = view.Rows;

        // A row's heading, and its stripe, come from the row it shows.
        grid.LoadingRow += (_, e) =>
        {
            if (e.Row.Item is RowView row)
            {
                e.Row.Header = row.Header;
                e.Row.Background = view.Stripes.Count > 0 ? BrushOf(view.Stripes[row.Index % view.Stripes.Count]) : Brushes.White;
                StyleRow(grid, view, e.Row, row.Index);
            }
        };

        // The grid keeps an edited cell's value back until its whole row is committed, which is
        // when the person leaves the row. A script is owed each cell as it is finished, so the
        // row is committed as soon as a cell is.
        bool committing = false;
        grid.CellEditEnding += (_, e) =>
        {
            if (e.EditAction != DataGridEditAction.Commit || committing)
            {
                return;
            }

            Dispatcher.BeginInvoke(() =>
            {
                committing = true;
                try
                {
                    grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
                }
                finally
                {
                    committing = false;
                }
            });
        };

        grid.SelectedCellsChanged += (_, _) =>
        {
            if (!shown.Applying && shown.Frame?.Table is { } table)
            {
                Tell(shown, "cellselect", SelectionOf(grid, table.SelectionType));
            }
        };
        grid.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (CellUnder(grid, e.OriginalSource as DependencyObject) is { } at)
            {
                Dispatcher.BeginInvoke(() => Tell(shown, "clicked", at));
            }
        };
        grid.MouseDoubleClick += (_, e) =>
        {
            if (CellUnder(grid, e.OriginalSource as DependencyObject) is { } at)
            {
                Tell(shown, "doubleclicked", at);
            }
        };
        return shown;
    }

    /// <summary>
    /// Lays the styles added with <c>addStyle</c> (U9) over a row as it loads: the row's own on the
    /// row, and each cell's on the cell once the row has its cells.
    /// </summary>
    private static void StyleRow(DataGrid grid, TableView view, DataGridRow row, int index)
    {
        IReadOnlyList<UiStyleRule> rules = view.Styles;
        if (rules.Count == 0)
        {
            row.ClearValue(Control.ForegroundProperty);
            row.ClearValue(Control.FontWeightProperty);
            row.ClearValue(Control.FontStyleProperty);
            return;
        }

        UiStyle rowStyle = UiStyleRule.Combine(rules.Where(rule => rule.Target is UiStyleTarget.Whole or UiStyleTarget.Row && rule.Covers(index + 1, 1)));
        if (rowStyle.BackgroundColor is { } fill)
        {
            row.Background = BrushOf(fill);
        }

        if (rowStyle.FontColor is { } ink)
        {
            row.Foreground = BrushOf(ink);
        }
        else
        {
            row.ClearValue(Control.ForegroundProperty);
        }

        row.FontWeight = rowStyle.FontWeight == "bold" ? FontWeights.Bold : FontWeights.Normal;
        row.FontStyle = rowStyle.FontAngle == "italic" ? FontStyles.Italic : FontStyles.Normal;
        void Cells()
        {
            for (int c = 0; c < grid.Columns.Count; c++)
            {
                if (grid.Columns[c].GetCellContent(row)?.Parent is not DataGridCell cell)
                {
                    continue;
                }

                UiStyle cellStyle = UiStyleRule.Combine(rules.Where(rule => rule.Target is UiStyleTarget.Column or UiStyleTarget.Cell && rule.Covers(index + 1, c + 1)));
                cell.Background = BrushOf(cellStyle.BackgroundColor) ?? Brushes.Transparent;
                if (cellStyle.FontColor is { } cellInk)
                {
                    cell.Foreground = BrushOf(cellInk);
                }
                else
                {
                    cell.ClearValue(Control.ForegroundProperty);
                }

                if (cellStyle.FontWeight.Length > 0)
                {
                    cell.FontWeight = cellStyle.FontWeight == "bold" ? FontWeights.Bold : FontWeights.Normal;
                }
                else
                {
                    cell.ClearValue(Control.FontWeightProperty);
                }

                if (cellStyle.FontAngle.Length > 0)
                {
                    cell.FontStyle = cellStyle.FontAngle == "italic" ? FontStyles.Italic : FontStyles.Normal;
                }
                else
                {
                    cell.ClearValue(Control.FontStyleProperty);
                }
            }
        }

        if (row.IsLoaded)
        {
            Cells();
        }
        else
        {
            RoutedEventHandler? once = null;
            once = (_, _) =>
            {
                row.Loaded -= once;
                Cells();
            };
            row.Loaded += once;
        }
    }

    /// <summary>The data row and column of what was pressed, and whether it was a heading: 1 for a row's, 2 for a column's.</summary>
    private static int[]? CellUnder(DataGrid grid, DependencyObject? hit)
    {
        for (DependencyObject? walk = hit; walk is not null && !ReferenceEquals(walk, grid);
             walk = walk is Visual ? VisualTreeHelper.GetParent(walk) : LogicalTreeHelper.GetParent(walk))
        {
            switch (walk)
            {
                case DataGridCell { DataContext: RowView row } cell:
                    return [row.Index, grid.Columns.IndexOf(cell.Column), 0];
                case DataGridRowHeader { DataContext: RowView headed }:
                    return [headed.Index, -1, 1];
                case DataGridColumnHeader { Column: { } column }:
                    return [-1, grid.Columns.IndexOf(column), 2];
            }
        }

        return null;
    }

    /// <summary>What is selected, as the script side is told: pairs of row and column, or rows, or columns, from 0.</summary>
    private static int[] SelectionOf(DataGrid grid, UiTableSelectionType type)
    {
        var cells = new List<(int Row, int Column)>();
        foreach (DataGridCellInfo info in grid.SelectedCells)
        {
            if (info.Item is RowView row && info.Column is not null)
            {
                cells.Add((row.Index, grid.Columns.IndexOf(info.Column)));
            }
        }

        return type switch
        {
            UiTableSelectionType.Row => [.. cells.Select(static cell => cell.Row).Distinct()],
            UiTableSelectionType.Column => [.. cells.Select(static cell => cell.Column).Distinct()],
            _ => [.. cells.SelectMany(static cell => new[] { cell.Row, cell.Column })],
        };
    }

    private void RefreshTable(Shown shown, UiComponentFrame frame, bool waiting)
    {
        if (frame.Table is not { } table || !_tables.TryGetValue((UiTableModel)frame.Source, out TableView? view))
        {
            return;
        }

        DataGrid grid = view.Grid;
        UiTableContent content = table.Content;
        grid.IsReadOnly = table.Inactive;
        grid.IsHitTestVisible = !table.Inactive;
        grid.CanUserReorderColumns = table.Rearrangeable;
        grid.SelectionMode = table.Multiselect ? DataGridSelectionMode.Extended : DataGridSelectionMode.Single;
        grid.SelectionUnit = table.SelectionType == UiTableSelectionType.Row ? DataGridSelectionUnit.FullRow : DataGridSelectionUnit.Cell;
        bool rowHeaders = content.RowHeaders is not null;
        bool columnHeaders = content.Columns.Any(static column => column.Header.Length > 0);
        grid.HeadersVisibility = (rowHeaders, columnHeaders) switch
        {
            (true, true) => DataGridHeadersVisibility.All,
            (true, false) => DataGridHeadersVisibility.Row,
            (false, true) => DataGridHeadersVisibility.Column,
            _ => DataGridHeadersVisibility.None,
        };

        bool restriped = !view.Stripes.SequenceEqual(table.Stripes) || !ReferenceEquals(view.Styles, frame.Styles);
        view.Stripes = table.Stripes;
        view.Styles = frame.Styles;
        if (!ReferenceEquals(view.Content, content))
        {
            // A person part-way through a cell keeps it: the frame that answers the edit is on its way.
            if (waiting)
            {
                return;
            }

            bool sameColumns = view.Content is { } old && SameColumns(old, content);
            if (!sameColumns)
            {
                BuildColumns(grid, content);
            }

            if (view.Content is { } before && sameColumns && before.Rows == content.Rows)
            {
                for (int r = 0; r < content.Rows; r++)
                {
                    view.Rows[r].Header = content.RowHeaders?[r] ?? string.Empty;
                    for (int c = 0; c < content.Columns.Count; c++)
                    {
                        view.Rows[r].Cells[c].Show(content.At(r, c));
                    }
                }
            }
            else
            {
                view.Rows.Clear();
                for (int r = 0; r < content.Rows; r++)
                {
                    var row = new RowView(view, r) { Header = content.RowHeaders?[r] ?? string.Empty };
                    row.Cells = [.. Enumerable.Range(0, content.Columns.Count).Select(c => new CellView(row, c))];
                    for (int c = 0; c < content.Columns.Count; c++)
                    {
                        row.Cells[c].Show(content.At(r, c));
                    }

                    view.Rows.Add(row);
                }
            }

            view.Content = content;
        }
        else if (restriped)
        {
            grid.Items.Refresh();
        }

        if (!waiting)
        {
            ShowSelection(grid, view, table);
        }

        if (table.ScrollRequests != view.ScrollSeen)
        {
            view.ScrollSeen = table.ScrollRequests;
            int row = System.Math.Clamp(table.ScrollRow < 0 ? 0 : table.ScrollRow, 0, System.Math.Max(0, view.Rows.Count - 1));
            if (view.Rows.Count > 0)
            {
                DataGridColumn? column = table.ScrollColumn >= 0 && table.ScrollColumn < grid.Columns.Count ? grid.Columns[table.ScrollColumn] : null;
                grid.ScrollIntoView(view.Rows[row], column);
            }
        }
    }

    private static bool SameColumns(UiTableContent a, UiTableContent b)
    {
        if (a.Columns.Count != b.Columns.Count)
        {
            return false;
        }

        for (int c = 0; c < a.Columns.Count; c++)
        {
            UiTableColumn x = a.Columns[c];
            UiTableColumn y = b.Columns[c];
            if (x.Header != y.Header || x.Width != y.Width || x.Editable != y.Editable || x.Sortable != y.Sortable
                || !(x.Choices ?? []).SequenceEqual(y.Choices ?? []) || ColumnShows(a, c) != ColumnShows(b, c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether a column is one of check boxes: every one of its cells is a box, and it has a cell.</summary>
    private static bool ColumnShows(UiTableContent content, int column)
    {
        for (int r = 0; r < content.Rows; r++)
        {
            if (content.At(r, column).Kind is not (UiTableCellKind.Checked or UiTableCellKind.Unchecked))
            {
                return false;
            }
        }

        return content.Rows > 0;
    }

    private static void BuildColumns(DataGrid grid, UiTableContent content)
    {
        grid.Columns.Clear();
        for (int c = 0; c < content.Columns.Count; c++)
        {
            UiTableColumn column = content.Columns[c];
            DataGridColumn made;
            if (ColumnShows(content, c))
            {
                made = new DataGridCheckBoxColumn
                {
                    Binding = new Binding($"Cells[{c}].Checked") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                };
            }
            else if (column.Choices is { } choices)
            {
                made = new DataGridComboBoxColumn
                {
                    ItemsSource = choices,
                    SelectedItemBinding = new Binding($"Cells[{c}].Text") { Mode = BindingMode.TwoWay },
                };
            }
            else
            {
                var text = new Style(typeof(TextBlock));
                text.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, new Binding($"Cells[{c}].Alignment")));
                text.Setters.Add(new Setter(FrameworkElement.MarginProperty, new System.Windows.Thickness(4, 1, 4, 1)));
                made = new DataGridTextColumn
                {
                    Binding = new Binding($"Cells[{c}].Text") { Mode = BindingMode.TwoWay },
                    ElementStyle = text,
                };
            }

            made.Header = column.Header;
            made.IsReadOnly = !column.Editable;
            made.CanUserSort = column.Sortable;
            made.SortMemberPath = $"Cells[{c}].Text";
            made.Width = column.Width.Kind switch
            {
                UiGridTrackKind.Fit => new DataGridLength(1, DataGridLengthUnitType.Auto),
                UiGridTrackKind.Fixed => new DataGridLength(column.Width.Value),
                _ => new DataGridLength(System.Math.Max(column.Width.Value, 0.0001), DataGridLengthUnitType.Star),
            };
            made.MinWidth = 20;
            grid.Columns.Add(made);
        }
    }

    /// <summary>Puts the grid's selection where the frame says it is, when it is somewhere else.</summary>
    private static void ShowSelection(DataGrid grid, TableView view, UiTableFrame table)
    {
        int[] current = SelectionOf(grid, table.SelectionType);
        if (current.SequenceEqual(table.Selection))
        {
            return;
        }

        if (table.SelectionType == UiTableSelectionType.Row)
        {
            grid.SelectedItems.Clear();
            foreach (int row in table.Selection)
            {
                if (row >= 0 && row < view.Rows.Count)
                {
                    if (table.Multiselect)
                    {
                        grid.SelectedItems.Add(view.Rows[row]);
                    }
                    else
                    {
                        grid.SelectedItem = view.Rows[row];
                    }
                }
            }

            return;
        }

        grid.SelectedCells.Clear();
        void Pick(int row, int column)
        {
            if (row >= 0 && row < view.Rows.Count && column >= 0 && column < grid.Columns.Count)
            {
                grid.SelectedCells.Add(new DataGridCellInfo(view.Rows[row], grid.Columns[column]));
            }
        }

        if (table.SelectionType == UiTableSelectionType.Column)
        {
            foreach (int column in table.Selection)
            {
                for (int row = 0; row < view.Rows.Count; row++)
                {
                    Pick(row, column);
                }
            }
        }
        else
        {
            for (int i = 0; i + 1 < table.Selection.Count; i += 2)
            {
                Pick(table.Selection[i], table.Selection[i + 1]);
            }
        }
    }

    // --- tab headings ----------------------------------------------------------------------------

    private void ApplyTabStrips(UiFrame frame)
    {
        var present = new HashSet<UiTabGroupModel>(ReferenceEqualityComparer.Instance);
        foreach (UiTabGroupFrame group in frame.TabGroups)
        {
            present.Add(group.Source);
            if (!_tabStrips.TryGetValue(group.Source, out TabControl? strip))
            {
                strip = new TabControl
                {
                    Padding = new System.Windows.Thickness(0),
                    BorderThickness = new System.Windows.Thickness(0),
                    Background = Brushes.Transparent,
                    FontFamily = new FontFamily(UiLayout.FontFamily("Helvetica")),
                    FontSize = 12,
                };
                UiTabGroupModel source = group.Source;
                TabControl made = strip;
                strip.SelectionChanged += (_, e) =>
                {
                    if (ReferenceEquals(e.OriginalSource, made) && !_applyingTabs.Contains(made)
                        && made.SelectedItem is TabItem { Tag: UiTabModel picked })
                    {
                        ScriptGraphicsCallbacks.NotifyComponent(source, "tab", picked);
                    }
                };
                _tabStrips[group.Source] = strip;
                Children.Add(strip);
            }

            _applyingTabs.Add(strip);
            try
            {
                strip.TabStripPlacement = group.Location switch
                {
                    UiTabLocation.Bottom => Dock.Bottom,
                    UiTabLocation.Left => Dock.Left,
                    UiTabLocation.Right => Dock.Right,
                    _ => Dock.Top,
                };
                AutomationProperties.SetAutomationId(strip, group.Tag);
                strip.ToolTip = group.Tooltip.Length > 0 ? group.Tooltip : null;

                // The headings are made again only when they are other tabs, or in another order.
                bool same = strip.Items.Count == group.Headings.Count;
                for (int i = 0; same && i < group.Headings.Count; i++)
                {
                    same = strip.Items[i] is TabItem { Tag: UiTabModel shown } && ReferenceEquals(shown, group.Headings[i].Source);
                }

                if (!same)
                {
                    strip.Items.Clear();
                    foreach (UiTabHeading heading in group.Headings)
                    {
                        strip.Items.Add(new TabItem { Tag = heading.Source, Content = null });
                    }
                }

                for (int i = 0; i < group.Headings.Count; i++)
                {
                    UiTabHeading heading = group.Headings[i];
                    var item = (TabItem)strip.Items[i]!;
                    item.Header = heading.Title;
                    item.Foreground = BrushOf(heading.Foreground) ?? Brushes.Black;
                    item.ToolTip = heading.Tooltip.Length > 0 ? heading.Tooltip : null;
                    AutomationProperties.SetAutomationId(item, heading.Tag);
                    AutomationProperties.SetName(item, heading.Title);
                }

                strip.SelectedIndex = group.Selected;
            }
            finally
            {
                _applyingTabs.Remove(strip);
            }
        }

        foreach (UiTabGroupModel gone in _tabStrips.Keys.Where(model => !present.Contains(model)).ToList())
        {
            Children.Remove(_tabStrips[gone]);
            _tabStrips.Remove(gone);
        }
    }

    private void ArrangeTabStrips(UiLayoutResult layout)
    {
        foreach (UiTabStripPlacement placement in layout.TabStrips)
        {
            if (!_tabStrips.TryGetValue(placement.Group.Source, out TabControl? strip))
            {
                continue;
            }

            Rect2D box = placement.Box;
            Rect2D seen = UiLayout.Intersect(placement.Clip, box);
            SetLeft(strip, box.X);
            SetTop(strip, box.Y);
            strip.Width = System.Math.Max(0, box.Width);
            strip.Height = System.Math.Max(0, box.Height);
            strip.Visibility = placement.Visible && !seen.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
            strip.Clip = ClipOf(placement.Occluders, box, seen);
            SetZIndex(strip, placement.Order);
        }
    }
}
