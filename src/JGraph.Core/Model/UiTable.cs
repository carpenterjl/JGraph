using System.ComponentModel;
using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>What one cell of a table shows, and so how it is edited.</summary>
public enum UiTableCellKind
{
    /// <summary>Text, set to the left.</summary>
    Text,

    /// <summary>A number as text, set to the right.</summary>
    Number,

    /// <summary>A check box, ticked.</summary>
    Checked,

    /// <summary>A check box, clear.</summary>
    Unchecked,
}

/// <summary>One cell as the window draws it.</summary>
public readonly record struct UiTableCell(string Text, UiTableCellKind Kind);

/// <summary>MATLAB's <c>SelectionType</c>: what a click in a table selects.</summary>
public enum UiTableSelectionType
{
    Cell,
    Row,
    Column,
}

/// <summary>
/// One column of a table as the window draws it: its heading, how wide it is (a
/// <see cref="UiGridTrack"/>: <c>'auto'</c> is a weight of one, <c>'fit'</c> the widest of its
/// cells, a number so many pixels), whether a person may edit its cells and sort by it, and the
/// choices its cells are picked from when its <c>ColumnFormat</c> is a list.
/// </summary>
public sealed record UiTableColumn(
    string Header,
    UiGridTrack Width,
    bool Editable,
    bool Sortable,
    IReadOnlyList<string>? Choices = null);

/// <summary>
/// Everything a table shows, worked out on the script thread from its <c>Data</c> and its column
/// properties and never changed afterwards: the cells row by row, the columns, and the row
/// headings (null when the table has none). The data itself can be of any class and stays on the
/// script's side; this is its picture.
/// </summary>
public sealed record UiTableContent(
    int Rows,
    IReadOnlyList<UiTableColumn> Columns,
    IReadOnlyList<UiTableCell> Cells,
    IReadOnlyList<string>? RowHeaders)
{
    public static readonly UiTableContent Empty = new(0, [], [], null);

    /// <summary>The cell of a row and a column, both from 0.</summary>
    public UiTableCell At(int row, int column) => Cells[(row * Columns.Count) + column];
}

/// <summary>
/// MATLAB's <c>uitable</c> (<c>matlab.ui.control.Table</c>; app-building plan, U8). One class
/// serves a classic figure and a <c>uifigure</c>: only the font it starts with differs. It has
/// <c>Units</c>, as a <c>uicontrol</c> has and the other components do not. Defaults are R2025b's
/// (probe <c>u8_matrix</c>).
/// </summary>
public sealed class UiTableModel : UiComponentModel
{
    /// <summary>R2025b's two stripes: white, and 245/255 grey.</summary>
    public static readonly IReadOnlyList<UiColor> DefaultStripes = [White, Grey];

    private UiTableContent _content = UiTableContent.Empty;
    private IReadOnlyList<UiColor> _stripes = DefaultStripes;
    private bool _rowStriping = true;
    private bool _columnRearrangeable;
    private UiTableSelectionType _selectionType = UiTableSelectionType.Cell;
    private bool _multiselect = true;
    private IReadOnlyList<int> _selection = [];
    private UiFontUnits _fontUnits = UiFontUnits.Pixels;
    private int _scrollRequests;
    private int _scrollRow = -1;
    private int _scrollColumn = -1;

    public UiTableModel()
        : base("Table", new Rect2D(20, 20, 300, 300))
    {
    }

    /// <summary>A table with the font one made in a classic figure starts with: 8-point MS Sans Serif.</summary>
    public static UiTableModel ForClassicFigure() => new()
    {
        FontName = "MS Sans Serif",
        FontUnits = UiFontUnits.Points,
        FontSize = 8,
    };

    public override UiComponentKind Kind => UiComponentKind.Table;

    /// <summary>What the table shows; replaced whole whenever anything it depends on is written.</summary>
    [Browsable(false)]
    public UiTableContent Content
    {
        get => _content;
        set => SetProperty(ref _content, value ?? UiTableContent.Empty, InvalidationKind.Ui);
    }

    /// <summary>MATLAB's <c>BackgroundColor</c>: the colours the rows take in turn.</summary>
    [Browsable(false)]
    public IReadOnlyList<UiColor> Stripes
    {
        get => _stripes;
        set => SetProperty(ref _stripes, value ?? DefaultStripes, InvalidationKind.Ui);
    }

    /// <summary>MATLAB's <c>RowStriping</c>: off, every row takes the first colour.</summary>
    [Browsable(false)]
    public bool RowStriping
    {
        get => _rowStriping;
        set => SetProperty(ref _rowStriping, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public bool ColumnRearrangeable
    {
        get => _columnRearrangeable;
        set => SetProperty(ref _columnRearrangeable, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public UiTableSelectionType SelectionType
    {
        get => _selectionType;
        set => SetProperty(ref _selectionType, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public bool Multiselect
    {
        get => _multiselect;
        set => SetProperty(ref _multiselect, value, InvalidationKind.Ui);
    }

    /// <summary>
    /// What is selected, from 0: pairs of row and column for cells, or the rows, or the columns,
    /// in the order they were given.
    /// </summary>
    [Browsable(false)]
    public IReadOnlyList<int> Selection
    {
        get => _selection;
        set => SetProperty(ref _selection, value ?? [], InvalidationKind.Ui);
    }

    /// <summary>MATLAB's <c>FontUnits</c>; <see cref="UiComponentModel.FontSize"/> is counted in it.</summary>
    [Browsable(false)]
    public UiFontUnits FontUnits
    {
        get => _fontUnits;
        set => SetProperty(ref _fontUnits, value, InvalidationKind.Ui);
    }

    /// <summary>The font size in pixels of 1/96 inch.</summary>
    public double FontSizeInPixels() => _fontUnits switch
    {
        UiFontUnits.Points => FontSize * 96 / 72,
        UiFontUnits.Inches => FontSize * 96,
        UiFontUnits.Centimeters => FontSize * 96 / 2.54,
        UiFontUnits.Normalized => FontSize * PixelPosition().Height,
        _ => FontSize,
    };

    /// <summary>Asks the window to bring a cell into view — <c>scroll(t, …)</c>. Either may be -1 for "as it is".</summary>
    public void RequestScroll(int row, int column)
    {
        _scrollRow = row;
        _scrollColumn = column;
        _scrollRequests++;
        Invalidate(InvalidationKind.Ui);
    }

    public override UiComponentFrame Snapshot() => Common() with
    {
        FontSize = FontSizeInPixels(),
        Enabled = Enable != UiEnable.Off,
        Table = new UiTableFrame(
            _content,
            _rowStriping ? _stripes : [.. _stripes.Take(1)],
            _selectionType,
            _multiselect,
            _selection,
            _columnRearrangeable,
            Enable == UiEnable.Inactive,
            _scrollRequests,
            _scrollRow,
            _scrollColumn),
    };
}

/// <summary>The part of a component's frame only a table has.</summary>
public sealed record UiTableFrame(
    UiTableContent Content,
    IReadOnlyList<UiColor> Stripes,
    UiTableSelectionType SelectionType,
    bool Multiselect,
    IReadOnlyList<int> Selection,
    bool Rearrangeable,
    bool Inactive,
    int ScrollRequests,
    int ScrollRow,
    int ScrollColumn);
