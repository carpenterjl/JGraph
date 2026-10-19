using System.Globalization;
using JGraph.Core.Primitives;

namespace JGraph.Scripting.Designer;

/// <summary>How a grid shows on the canvas (App Designer's design-time grid modes, <c>DesignTimeGridRichEditingMixin.goToMode</c>).</summary>
public enum DesignGridMode
{
    /// <summary>No cells drawn.</summary>
    Quiet,

    /// <summary>Its cells drawn, dashed.</summary>
    Layout,

    /// <summary>Its cells drawn, and the button that configures it at its top-left corner.</summary>
    Simple,

    /// <summary>Being configured: its rows and columns drawn as bands to pick, and the button that ends it.</summary>
    Edit,
}

/// <summary>A row's or column's kind, as the grid configuration popup offers them.</summary>
public enum DesignTrackKind
{
    /// <summary><c>'fit'</c>.</summary>
    Fit,

    /// <summary><c>'Nx'</c>.</summary>
    Weighted,

    /// <summary>Pixels.</summary>
    Fixed,
}

/// <summary>
/// A band of a grid being configured: one row or column, which a click selects, drawn across the
/// whole grid and 20 pixels past it each side (the tabs it is picked by outside the grid).
/// </summary>
public readonly record struct DesignGridBand(GridAxis Axis, int Index, Rect2D Box, bool Hovered, bool Selected, bool Upper);

/// <summary>
/// What the canvas shows while a menu item or a popup button would add or delete a row or column:
/// a bar where the new one goes, or the one that goes, striped (<c>DesignTimeGridOperationHintsMixin</c>).
/// </summary>
public readonly record struct DesignGridHint(DesignComponent Grid, GridAxis Axis, int Index, bool Insert);

/// <summary>
/// A grid being configured, App Designer's grid "edit mode" (designer plan DS4, read from
/// <c>uicomponents_appdesigner_plugin/widget/designtimegrid/*</c>), apart from any window: its rows
/// and columns as bands, the one picked, and the popup that sets its kind - fit, weighted or fixed -
/// and its size, and adds or deletes rows and columns beside it. Every change is an edit of the
/// document's.
/// </summary>
public sealed class DesignGridConfigure
{
    /// <summary>How far a band reaches past the grid on each side (<c>GridConstants.TAB_WIDTH</c>).</summary>
    public const double TabWidth = 20;

    /// <summary>The largest weight the popup takes (its spinner's limits, 0 to 100).</summary>
    public const double MaximumWeight = 100;

    private int _hoveredRow = -1;
    private int _hoveredColumn = -1;
    private int _selectedRow = -1;
    private int _selectedColumn = -1;

    /// <summary>Configures <paramref name="grid"/> of <paramref name="document"/>.</summary>
    public DesignGridConfigure(DesignDocument document, DesignComponent grid)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(grid);
        Document = document;
        Grid = grid;
    }

    /// <summary>The app.</summary>
    public DesignDocument Document { get; }

    /// <summary>The grid being configured.</summary>
    public DesignComponent Grid { get; }

    /// <summary>Raised when the bands, the pick or the popup changed.</summary>
    public event EventHandler? Changed;

    /// <summary>The track picked - a row or a column, from 1 - or null when none is: the popup shows for it.</summary>
    public (GridAxis Axis, int Index)? Selected
    {
        get
        {
            int rows = Document.TracksOf(Grid, GridAxis.Row).Count;
            int columns = Document.TracksOf(Grid, GridAxis.Column).Count;

            // An index past the end is the last track (getSelectedColumn / getSelectedRow).
            if (_selectedColumn >= 0 && columns > 0)
            {
                return (GridAxis.Column, System.Math.Min(_selectedColumn, columns - 1) + 1);
            }

            return _selectedRow >= 0 && rows > 0 ? (GridAxis.Row, System.Math.Min(_selectedRow, rows - 1) + 1) : null;
        }
    }

    /// <summary>The track's kind the popup shows.</summary>
    public DesignTrackKind Kind { get; private set; }

    /// <summary>The weight in the popup's weight box: the last weighted track's it showed, 100 before any.</summary>
    public double Weight { get; private set; } = MaximumWeight;

    /// <summary>The pixels in the popup's pixel box: a fixed track's, else what the track is drawn at, rounded.</summary>
    public double Pixels { get; private set; } = 100;

    /// <summary>Whether the weight box is for typing in: a weighted track.</summary>
    public bool WeightEnabled => Kind == DesignTrackKind.Weighted;

    /// <summary>Whether the pixel box is for typing in: a fixed track.</summary>
    public bool PixelsEnabled => Kind == DesignTrackKind.Fixed;

    /// <summary>Where the pick was made, on the surface: the popup opens there.</summary>
    public Point2D PickedAt { get; private set; }

    /// <summary>
    /// Whether a press at <paramref name="at"/> is on the configuring controls, which take it: the
    /// grid, and its bands where they reach past it (the tabs). A press beside them - in a corner past
    /// the grid, or past a gap or the padding - is not.
    /// </summary>
    public bool Takes(Point2D at) =>
        (Document.BoxOf(Grid) is { } box && box.Box.Contains(at)) || BandAt(at) is not null;

    /// <summary>
    /// The bands, rows then columns (<c>GridEditModeComponent._createRowsModel</c> and
    /// <c>_createColumnsModel</c>): a row across the grid and 20 pixels past each side, a column down
    /// it and 20 pixels past top and bottom. Rows lie over columns until the pointer picks a column
    /// out by its tabs above or below the grid.
    /// </summary>
    public IReadOnlyList<DesignGridBand> Bands()
    {
        // With no rows or no columns there are no cells, and no bands (GridEditModeComponent).
        if (Document.CellsOf(Grid) is not { Rows: > 0, Columns: > 0 } cells || Document.BoxOf(Grid) is not { } grid)
        {
            return [];
        }

        bool columnsOnTop = _hoveredColumn != -1;
        (GridAxis Axis, int Index)? selected = Selected;
        var bands = new List<DesignGridBand>(cells.Rows + cells.Columns);
        Rect2D box = grid.Box;
        for (int r = 1; r <= cells.Rows; r++)
        {
            Rect2D cell = cells.Cell(r, 1);
            bands.Add(new DesignGridBand(
                GridAxis.Row, r, new Rect2D(box.X - TabWidth, cell.Y, box.Width + (2 * TabWidth), cell.Height),
                _hoveredRow == r - 1, selected == (GridAxis.Row, r), !columnsOnTop));
        }

        for (int c = 1; c <= cells.Columns; c++)
        {
            Rect2D cell = cells.Cell(1, c);
            bands.Add(new DesignGridBand(
                GridAxis.Column, c, new Rect2D(cell.X, box.Y - TabWidth, cell.Width, box.Height + (2 * TabWidth)),
                _hoveredColumn == c - 1, selected == (GridAxis.Column, c), columnsOnTop));
        }

        return bands;
    }

    /// <summary>
    /// The pointer at <paramref name="at"/> (the controls' mousemove): over the grid the kind on top
    /// keeps the pointer; outside it, a tab picks its band's kind out.
    /// </summary>
    public void Hover(Point2D at)
    {
        if (BandAt(at) is not { } band)
        {
            return;
        }

        bool inside = Document.BoxOf(Grid) is { } grid && grid.Box.Contains(at);
        if (band.Axis == GridAxis.Column)
        {
            if (_hoveredRow != -1 && inside)
            {
                return;
            }

            Set(ref _hoveredColumn, band.Index - 1);
            Set(ref _hoveredRow, -1);
        }
        else
        {
            if (_hoveredColumn != -1 && inside)
            {
                return;
            }

            Set(ref _hoveredRow, band.Index - 1);
            Set(ref _hoveredColumn, -1);
        }
    }

    /// <summary>
    /// A click at <paramref name="at"/>: on the band under the pointer, picks it, or lets it go when
    /// it is picked already; one track at a time.
    /// </summary>
    /// <returns>Whether the click was on the controls, which take it.</returns>
    public bool Click(Point2D at)
    {
        if (!Takes(at))
        {
            return false;
        }

        Hover(at);
        if (BandAt(at) is { } band && ((band.Axis == GridAxis.Column && _hoveredColumn == band.Index - 1)
            || (band.Axis == GridAxis.Row && _hoveredRow == band.Index - 1)))
        {
            PickedAt = at;
            if (band.Axis == GridAxis.Column)
            {
                _selectedColumn = _selectedColumn == band.Index - 1 ? -1 : band.Index - 1;
                _selectedRow = -1;
            }
            else
            {
                _selectedRow = _selectedRow == band.Index - 1 ? -1 : band.Index - 1;
                _selectedColumn = -1;
            }

            Refresh();
        }

        return true;
    }

    /// <summary>
    /// The popup shown again for the picked track (<c>GridConfigWidget.update</c>): a number is fixed,
    /// <c>'Nx'</c> weighted, <c>'fit'</c> fit; the pixel box holds a fixed track's pixels and else
    /// what the track is drawn at, rounded; the weight box keeps the last weight shown.
    /// </summary>
    public void Refresh()
    {
        if (Selected is { } track && Document.TracksOf(Grid, track.Axis) is { } tracks
            && Document.CellsOf(Grid) is { Rows: > 0, Columns: > 0 } cells && track.Index <= tracks.Count)
        {
            DesignValue value = tracks[track.Index - 1];
            Rect2D cell = track.Axis == GridAxis.Row ? cells.Cell(track.Index, 1) : cells.Cell(1, track.Index);
            double drawn = JsRound(track.Axis == GridAxis.Row ? cell.Height : cell.Width);
            switch (value)
            {
                case NumericValue { Data: [var pixels] }:
                    Kind = DesignTrackKind.Fixed;
                    Pixels = pixels;
                    break;
                case CharValue { Text: var text } when text.EndsWith('x'):
                    Kind = DesignTrackKind.Weighted;
                    Weight = double.Parse(text[..^1], CultureInfo.InvariantCulture);
                    Pixels = drawn;
                    break;
                default:
                    Kind = DesignTrackKind.Fit;
                    Pixels = drawn;
                    break;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// A kind picked in the popup: fit; weighted, at the weight box's weight; fixed, at the pixel
    /// box's pixels - one edit.
    /// </summary>
    /// <returns>Null when the grid took it; else what it said.</returns>
    public string? Choose(DesignTrackKind kind)
    {
        if (Selected is not { } track || kind == Kind)
        {
            return null;
        }

        return kind switch
        {
            DesignTrackKind.Fit => Set(track, new CharValue("fit")),
            DesignTrackKind.Weighted => Set(track, new CharValue(WeightText(Weight))),
            _ => Set(track, NumericValue.Scalar(Pixels)),
        };
    }

    /// <summary>A weight typed in the popup: from 0 to 100; one edit.</summary>
    /// <returns>Null when the grid took it; else why not.</returns>
    public string? SetWeight(double weight)
    {
        if (!double.IsFinite(weight) || weight < 0 || weight > MaximumWeight)
        {
            return $"A weight is a number from 0 to {MaximumWeight}.";
        }

        Weight = weight;
        return Selected is { } track ? Set(track, new CharValue(WeightText(weight))) : null;
    }

    /// <summary>Pixels typed in the popup: 0 or more; one edit.</summary>
    /// <returns>Null when the grid took it; else why not.</returns>
    public string? SetPixels(double pixels)
    {
        if (!double.IsFinite(pixels) || pixels < 0)
        {
            return "A size in pixels is a number from 0 up.";
        }

        Pixels = pixels;
        return Selected is { } track ? Set(track, NumericValue.Scalar(pixels)) : null;
    }

    /// <summary>A new <c>'1x'</c> row above, or column left of, the picked one; the new one is picked.</summary>
    public void InsertBefore()
    {
        if (Selected is { } track)
        {
            Document.InsertTrack(Grid, track.Axis, track.Index);
            Refresh();
        }
    }

    /// <summary>A new <c>'1x'</c> row below, or column right of, the picked one; the new one is picked.</summary>
    public void InsertAfter()
    {
        if (Selected is { } track)
        {
            Document.InsertTrack(Grid, track.Axis, track.Index + 1);
            if (track.Axis == GridAxis.Row)
            {
                _selectedRow++;
                _hoveredRow = _selectedRow;
            }
            else
            {
                _selectedColumn++;
                _hoveredColumn = _selectedColumn;
            }

            Refresh();
        }
    }

    /// <summary>
    /// Deletes the picked row or column, and the components in it alone; the next is picked. The
    /// grid's last row or column deletes the grid (<c>delete-grid-layout</c>).
    /// </summary>
    /// <returns>Whether the grid itself was deleted, which ends the configuring.</returns>
    public bool DeleteTrack()
    {
        if (Selected is not { } track)
        {
            return false;
        }

        if (Document.TracksOf(Grid, track.Axis).Count == 1)
        {
            Document.Delete([Grid]);
            return true;
        }

        Document.DeleteTrack(Grid, track.Axis, track.Index);
        Refresh();
        return false;
    }

    /// <summary>The hint for a popup button the pointer is on: where a row or column goes or which one goes.</summary>
    public DesignGridHint? HintFor(bool insert, bool after)
    {
        if (Selected is not { } track)
        {
            return null;
        }

        return new DesignGridHint(Grid, track.Axis, insert && after ? track.Index + 1 : track.Index, insert);
    }

    /// <summary>The band under <paramref name="at"/>: the kind on top first.</summary>
    private DesignGridBand? BandAt(Point2D at)
    {
        IReadOnlyList<DesignGridBand> bands = Bands();
        foreach (DesignGridBand band in bands.Where(static b => b.Upper).Concat(bands.Where(static b => !b.Upper)))
        {
            if (band.Box.Contains(at))
            {
                return band;
            }
        }

        return null;
    }

    private string? Set((GridAxis Axis, int Index) track, DesignValue value)
    {
        string? refused = Document.SetTrack(Grid, track.Axis, track.Index, value);
        Refresh();
        return refused;
    }

    private void Set(ref int field, int value)
    {
        if (field != value)
        {
            field = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>A weight as App Designer writes it, <c>`${w}x`</c>: the shortest number that reads back.</summary>
    public static string WeightText(double weight) => weight.ToString("R", CultureInfo.InvariantCulture) + "x";

    private static double JsRound(double value) => System.Math.Floor(value + 0.5);

    /// <summary>
    /// Where <paramref name="hint"/> is drawn, on the surface (<c>DesignTimeGridOperationHintsMixin</c>):
    /// a row or column that goes, over its cells; a row or column added, a bar 5 pixels thick across
    /// the cells and 9 past them each side, midway in the gap it goes into - or, first or last, midway
    /// between the grid's edge and the cells.
    /// </summary>
    public static Rect2D? HintBox(DesignDocument document, DesignGridHint hint)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.CellsOf(hint.Grid) is not { Rows: > 0, Columns: > 0 } cells || document.BoxOf(hint.Grid) is not { } grid)
        {
            return null;
        }

        Rect2D box = grid.Box;
        if (!hint.Insert)
        {
            if (hint.Index < 1 || hint.Index > (hint.Axis == GridAxis.Row ? cells.Rows : cells.Columns))
            {
                return null;
            }

            return hint.Axis == GridAxis.Row
                ? Span(cells.Cell(hint.Index, 1), cells.Cell(hint.Index, cells.Columns))
                : Span(cells.Cell(1, hint.Index), cells.Cell(cells.Rows, hint.Index));
        }

        const double thick = 5;
        const double over = 9;
        if (hint.Axis == GridAxis.Row)
        {
            Rect2D first = cells.Cell(1, 1);
            Rect2D last = cells.Cell(cells.Rows, 1);
            double middle = hint.Index <= 1 ? (box.Y + first.Y) / 2
                : hint.Index > cells.Rows ? (last.Bottom + box.Bottom) / 2
                : (cells.Cell(hint.Index - 1, 1).Bottom + cells.Cell(hint.Index, 1).Y) / 2;
            double left = first.X - over;
            double right = cells.Cell(1, cells.Columns).Right + over;
            return new Rect2D(left, middle - (thick / 2), right - left, thick);
        }
        else
        {
            Rect2D first = cells.Cell(1, 1);
            Rect2D last = cells.Cell(1, cells.Columns);
            double middle = hint.Index <= 1 ? (box.X + first.X) / 2
                : hint.Index > cells.Columns ? (last.Right + box.Right) / 2
                : (cells.Cell(1, hint.Index - 1).Right + cells.Cell(1, hint.Index).X) / 2;
            double top = first.Y - over;
            double bottom = cells.Cell(cells.Rows, 1).Bottom + over;
            return new Rect2D(middle - (thick / 2), top, thick, bottom - top);
        }

        static Rect2D Span(Rect2D a, Rect2D b) => new(a.X, a.Y, b.Right - a.X, b.Bottom - a.Y);
    }
}
