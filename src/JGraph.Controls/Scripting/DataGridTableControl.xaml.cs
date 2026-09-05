using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using JGraph.Data;

namespace JGraph.Controls.Scripting;

/// <summary>
/// The Data Viewer grid: a virtualized spreadsheet view of a <see cref="TableGridAdapter"/> (a table
/// or an array), MATLAB's variable-viewer style. Large data is paged — the adapter caps a page at
/// <see cref="TableGridAdapter.PageSize"/> rows and the header offers page navigation. When the host
/// says the value behind the grid can take a write (<see cref="CanEdit"/>), a cell edit is reported
/// through <see cref="CellEdited"/> rather than applied here: the grid shows formatted text, and only
/// the workspace that owns the value can turn typed text back into an element of it.
/// </summary>
public partial class DataGridTableControl : UserControl
{
    private TableGridAdapter? _adapter;
    private int _page;

    /// <summary>Raised when the user commits an edit to a cell (absolute row, column, typed text).</summary>
    public event EventHandler<DataGridCellEditedEventArgs>? CellEdited;

    /// <summary>
    /// Whether cells may be edited. The host sets it with the value it shows: true for a variable of
    /// a workspace that can write one cell, false for a file opened from disk or a value nothing can
    /// write back into.
    /// </summary>
    public bool CanEdit
    {
        get => !Grid.IsReadOnly;
        set => Grid.IsReadOnly = !value;
    }

    /// <summary>Creates an empty viewer; call <see cref="Show"/> to display data.</summary>
    public DataGridTableControl()
    {
        InitializeComponent();
        UpdateHeader();
    }

    /// <summary>The most columns the viewer will build. Past this the grid stops being readable
    /// long before it stops being affordable, and the title says how many were left out.</summary>
    private const int MaxColumns = 512;

    private int _columnsShown;

    /// <summary>Displays <paramref name="adapter"/> (null clears the viewer).</summary>
    public void Show(TableGridAdapter? adapter)
    {
        _adapter = adapter;
        _page = 0;
        Grid.Columns.Clear();
        if (adapter is not null)
        {
            // A DataGrid virtualizes cells, never the column collection: every column is a live
            // object and each Add re-notifies every realized row, so the cost is quadratic in the
            // column count. A 2-by-100000 matrix passes the engine's cell budget and would hang the
            // window for minutes here. Rows are already paged; columns are bounded the same way.
            _columnsShown = Math.Min(adapter.ColumnNames.Count, MaxColumns);
            for (int i = 0; i < _columnsShown; i++)
            {
                Grid.Columns.Add(new DataGridTextColumn
                {
                    Header = adapter.ColumnNames[i],
                    Binding = new Binding($"[{i}]"),
                    Width = DataGridLength.Auto,
                });
            }
        }

        ShowCurrentPage();
    }

    /// <summary>
    /// Re-shows the same value after it changed underneath — a cell edit, a statement, a debugger
    /// step — keeping the page the user was on. <see cref="Show"/> would send them back to page 0.
    /// </summary>
    public void Refresh(TableGridAdapter? adapter)
    {
        int page = _page;
        Show(adapter);
        if (adapter is not null && page > 0)
        {
            _page = Math.Min(page, adapter.PageCount - 1);
            ShowCurrentPage();
        }
    }

    private void OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit
            || _adapter is null
            || e.EditingElement is not TextBox box)
        {
            return;
        }

        int column = Grid.Columns.IndexOf(e.Column);
        int row = _page * TableGridAdapter.PageSize + e.Row.GetIndex();
        if (column < 0 || row < 0 || row >= _adapter.RowCount
            || string.Equals(box.Text, _adapter.GetText(row, column), StringComparison.Ordinal))
        {
            return; // nothing changed, or the row is not one of the value's
        }

        CellEdited?.Invoke(this, new DataGridCellEditedEventArgs(row, column, box.Text));
    }

    private void ShowCurrentPage()
    {
        Grid.ItemsSource = _adapter?.GetPage(_page, out _);
        UpdateHeader();
    }

    private void UpdateHeader()
    {
        if (_adapter is not TableGridAdapter adapter)
        {
            TitleText.Text = "No data selected — double-click a variable or a data file.";
            PageText.Text = string.Empty;
            PrevPageButton.Visibility = Visibility.Collapsed;
            NextPageButton.Visibility = Visibility.Collapsed;
            return;
        }

        TitleText.Text = _columnsShown < adapter.ColumnNames.Count
            ? string.Format(
                CultureInfo.CurrentCulture,
                "{0} — first {1:N0} of {2:N0} columns",
                adapter.Title,
                _columnsShown,
                adapter.ColumnNames.Count)
            : adapter.Title;
        bool paged = adapter.PageCount > 1;
        PrevPageButton.Visibility = paged ? Visibility.Visible : Visibility.Collapsed;
        NextPageButton.Visibility = paged ? Visibility.Visible : Visibility.Collapsed;
        PrevPageButton.IsEnabled = _page > 0;
        NextPageButton.IsEnabled = _page < adapter.PageCount - 1;

        if (paged)
        {
            int first = _page * TableGridAdapter.PageSize;
            int last = Math.Min(first + TableGridAdapter.PageSize, adapter.RowCount) - 1;
            PageText.Text = string.Format(
                CultureInfo.CurrentCulture, "rows {0:N0}–{1:N0} of {2:N0}", first, last, adapter.RowCount);
        }
        else
        {
            PageText.Text = string.Format(CultureInfo.CurrentCulture, "{0:N0} row(s)", adapter.RowCount);
        }
    }

    private void OnPrevPageClick(object sender, RoutedEventArgs e)
    {
        if (_adapter is not null && _page > 0)
        {
            _page--;
            ShowCurrentPage();
        }
    }

    private void OnNextPageClick(object sender, RoutedEventArgs e)
    {
        if (_adapter is not null && _page < _adapter.PageCount - 1)
        {
            _page++;
            ShowCurrentPage();
        }
    }
}

/// <summary>One committed cell edit in a <see cref="DataGridTableControl"/>.</summary>
public sealed class DataGridCellEditedEventArgs : EventArgs
{
    /// <summary>Creates the event for the cell at (<paramref name="row"/>, <paramref name="column"/>).</summary>
    public DataGridCellEditedEventArgs(int row, int column, string text)
    {
        Row = row;
        Column = column;
        Text = text;
    }

    /// <summary>The 0-based absolute row (across pages).</summary>
    public int Row { get; }

    /// <summary>The 0-based column.</summary>
    public int Column { get; }

    /// <summary>What the user typed into the cell.</summary>
    public string Text { get; }
}
