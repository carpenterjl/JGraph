using JGraph.Core.Primitives;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Workspace;
using Xunit;
using Xunit.Abstractions;

namespace JGraph.Tests.Scripting;

/// <summary>
/// DS4 of the designer plan: grids on the canvas, as R2025b's App Designer edits them - their cells
/// drawn at design time, components dropped, moved and spanned over cells, rows and columns added,
/// deleted and sized, a container's components laid out in a grid and a grid taken away, and the
/// context menu and configuring that do it.
/// </summary>
[Collection("JG facade")]
public class AppDesignerGridTests(ITestOutputHelper output)
{
    private const string ShippedRoot = @"C:\Program Files\MATLAB\R2025b\toolbox";

    private static DesignDocument Blank() => DesignDocument.FromText(AppDesignerDocument.NewAppText(), null);

    private static DesignPaletteEntry Entry(string name) => DesignPalette.R2025b.Find(name) ?? throw new InvalidOperationException(name);

    private static string[] Lines(DesignDocument document, string codeName) =>
        DesignCodeWriter.ComponentCode(document.Design.Find(codeName)!, document.Style).ToArray();

    private static DesignComponent Find(DesignDocument document, string codeName) =>
        document.Design.Find(codeName) ?? throw new InvalidOperationException(codeName);

    /// <summary>A blank app whose figure (640 by 480) App Designer's Apply Grid Layout has given 8 rows of 3 columns.</summary>
    private static (DesignDocument Document, DesignComponent Grid) GridApp()
    {
        DesignDocument document = Blank();
        DesignComponent grid = document.Gridify(document.Design.Figure);
        return (document, grid);
    }

    private static Point2D Centre(Rect2D r) => new(r.X + (r.Width / 2), r.Y + (r.Height / 2));

    // --- a grid on the canvas ------------------------------------------------------------------------

    /// <summary>
    /// Apply Grid Layout on an empty figure gives as many '1x' rows and columns as 50-by-200-pixel
    /// cells fill it (App Designer's gridifier, its empty branch): 8 rows of 3 in 640 by 480. The
    /// canvas draws its cells where the shared layout lays them out, padding 10 and spacing 10 aside,
    /// and undo takes the grid away.
    /// </summary>
    [Fact]
    public void ApplyingAGridLayoutToAnEmptyFigureGivesAppDesignersCells()
    {
        (DesignDocument document, DesignComponent grid) = GridApp();
        using (document)
        {
            Assert.Equal("GridLayout", grid.CodeName);
            Assert.Equal([grid], document.Selection);
            Assert.Equal(
            [
                "app.GridLayout = uigridlayout(app.UIFigure);",
                "app.GridLayout.ColumnWidth = {'1x', '1x', '1x'};",
                "app.GridLayout.RowHeight = {'1x', '1x', '1x', '1x', '1x', '1x', '1x', '1x'};",
            ], Lines(document, "GridLayout"));

            GridCells cells = document.CellsOf(grid)!;
            Assert.Equal((8, 3), (cells.Rows, cells.Columns));
            Assert.Equal(new Rect2D(10, 10, 200, 48.75), cells.Cell(1, 1));
            Assert.Equal(new Rect2D(430, 68.75, 200, 48.75), cells.Cell(2, 3));
            Assert.Equal(new Rect2D(10, 10, 410, 107.5), cells.SpanRect(new GridSpan(1, 2, 1, 2)));

            document.Undo();
            Assert.Null(document.Design.Find("GridLayout"));
        }
    }

    /// <summary>
    /// At design time an empty 'fit' row is drawn 10 pixels high, as App Designer draws it so that it
    /// can be dropped into; a script's grid gives it no room.
    /// </summary>
    [Fact]
    public void AnEmptyFitRowIsTenPixelsAtDesignTime()
    {
        (DesignDocument document, DesignComponent grid) = GridApp();
        using (document)
        {
            Assert.Null(document.SetTrack(grid, GridAxis.Row, 2, new CharValue("fit")));
            GridCells cells = document.CellsOf(grid)!;
            Assert.Equal(10, cells.Cell(2, 1).Height);
            Assert.Equal(cells.Cell(1, 1).Bottom + 10, cells.Cell(2, 1).Y);
            Assert.Equal(cells.Cell(2, 1).Bottom + 10, cells.Cell(3, 1).Y);
        }
    }

    // --- dropping, drawing and moving ------------------------------------------------------------------

    /// <summary>
    /// A library entry dropped on a grid takes the cells its box covers (CellBoundsValidator): one
    /// cell for a box in it, two for one across the gap whose edges are past the first's centre and
    /// short of the second's; a labelled entry's label and component take cells side by side, the
    /// label's from the component's (LabeledComponentEntryDefinition). Its place is a row and a
    /// column: no position is written. Over a grid of one column a labelled entry has no room for its
    /// label and is refused, the grid holding the pointer.
    /// </summary>
    [Fact]
    public void ALibraryEntryDroppedOnAGridTakesTheCellsItCovers()
    {
        (DesignDocument document, DesignComponent grid) = GridApp();
        using (document)
        {
            var gestures = new DesignGestures(document);
            DesignComponent button = Assert.Single(gestures.Drop(Entry("Button"), new Point2D(20, 15)));
            Assert.Same(grid, button.Parent);
            Assert.Equal(new GridSpan(1, 1, 1, 1), document.SpanOf(button));
            string[] lines = Lines(document, "Button");
            Assert.Contains("app.Button.Layout.Row = 1;", lines);
            Assert.Contains("app.Button.Layout.Column = 1;", lines);
            Assert.DoesNotContain(lines, static l => l.Contains("Position", StringComparison.Ordinal));

            DesignComponent across = Assert.Single(gestures.Drop(Entry("Button"), new Point2D(150, 15)));
            Assert.Equal(new GridSpan(1, 1, 1, 2), document.SpanOf(across));
            Assert.Contains("app.Button2.Layout.Column = [1 2];", Lines(document, "Button2"));

            IReadOnlyList<DesignComponent> labelled = gestures.Drop(Entry("Edit Field (Text)"), new Point2D(20, 75));
            Assert.Equal(2, labelled.Count);
            Assert.Equal(new GridSpan(2, 2, 1, 1), document.SpanOf(Find(document, "EditFieldLabel")));
            Assert.Equal(new GridSpan(2, 2, 2, 2), document.SpanOf(Find(document, "EditField")));

            Assert.Empty(document.Runtime.Problems);
        }

        (DesignDocument narrow, DesignComponent column) = GridApp();
        using (narrow)
        {
            var gestures = new DesignGestures(narrow);
            Assert.Null(narrow.SetProperty(column, "ColumnWidth", CellValue.Row(new CharValue("1x"))));
            Assert.Empty(gestures.Drop(Entry("Edit Field (Text)"), new Point2D(20, 250)));
            Assert.NotNull(gestures.Refusal);
            Assert.Empty(column.Children);
        }
    }

    /// <summary>
    /// Click and draw in a grid: the press picks the cell under the pointer and a drag spans over the
    /// cells to the pointer (App Designer reuses the span resize's snapping); a press in a gap between
    /// cells is refused.
    /// </summary>
    [Fact]
    public void DrawingInAGridSpansTheCellsDrawnOver()
    {
        (DesignDocument document, DesignComponent grid) = GridApp();
        using (document)
        {
            var gestures = new DesignGestures(document) { DragThreshold = 4 };
            gestures.DrawEntry = Entry("Button");
            Assert.True(gestures.Press(new Point2D(50, 80), DesignButton.Left, DesignModifiers.None));
            gestures.Drag(new Point2D(400, 200), DesignModifiers.None);
            Assert.NotNull(gestures.DrawnCells);
            gestures.Release();
            DesignComponent button = Find(document, "Button");
            Assert.Same(grid, button.Parent);
            Assert.Equal(new GridSpan(2, 3, 1, 2), document.SpanOf(button));

            gestures.DrawEntry = Entry("Button");
            Assert.False(gestures.Press(new Point2D(215, 30), DesignButton.Left, DesignModifiers.None));
            Assert.NotNull(gestures.Refusal);
            Assert.Null(document.Design.Find("Button2"));
        }
    }

    /// <summary>
    /// A grid's child dragged to other cells takes the cells its box lands over, as one edit undone
    /// whole; dropped back over its own cells nothing changes (App Designer's move makes no command).
    /// The arrow keys move no grid child but take the key.
    /// </summary>
    [Fact]
    public void AGridChildDraggedToOtherCellsTakesThem()
    {
        (DesignDocument document, DesignComponent grid) = GridApp();
        using (document)
        {
            var gestures = new DesignGestures(document) { DragThreshold = 4 };
            DesignComponent button = Assert.Single(gestures.Drop(Entry("Button"), new Point2D(20, 15)));
            Rect2D cell = document.CellBoxOf(button)!.Value;
            Assert.Equal(document.CellsOf(grid)!.Cell(1, 1), cell);

            Point2D from = Centre(cell);
            Assert.True(gestures.Press(from, DesignButton.Left, DesignModifiers.None));
            gestures.Drag(new Point2D(from.X + 210, from.Y + 58.75), DesignModifiers.None);
            Assert.Equal(new GridSpan(2, 2, 2, 2), Assert.Single(gestures.Placement!.Spans!));
            Assert.Single(document.GestureGhosts);
            gestures.Release();
            Assert.Equal(new GridSpan(2, 2, 2, 2), document.SpanOf(button));
            Assert.Equal([button], document.Selection);

            Point2D now = Centre(document.CellBoxOf(button)!.Value);
            gestures.Press(now, DesignButton.Left, DesignModifiers.None);
            gestures.Drag(new Point2D(now.X + 6, now.Y + 5), DesignModifiers.None);
            gestures.Release();
            Assert.Equal(new GridSpan(2, 2, 2, 2), document.SpanOf(button));

            Assert.True(gestures.KeyDown(DesignKey.Right, DesignModifiers.None));
            Assert.Equal(new GridSpan(2, 2, 2, 2), document.SpanOf(button));

            document.Undo();
            Assert.Equal(new GridSpan(1, 1, 1, 1), document.SpanOf(button));
        }
    }

    /// <summary>
    /// A component placed by its position dragged over a grid's cells goes into them, its position
    /// unset; dragged out of the grid into a panel, it is placed where it was dropped at the size it
    /// had, and its cells unset. Undo takes each back.
    /// </summary>
    [Fact]
    public void ComponentsMoveIntoAndOutOfGrids()
    {
        using DesignDocument document = Blank();
        var gestures = new DesignGestures(document) { DragThreshold = 4 };
        DesignComponent panel = Assert.Single(document.Add(Entry("Panel"), document.Design.Figure, new Point2D(20, 20)));
        DesignComponent holder = Assert.Single(document.Add(Entry("Panel"), document.Design.Figure, new Point2D(340, 20)));
        DesignComponent grid = document.Gridify(holder);
        Assert.Same(holder, grid.Parent);
        DesignComponent button = Assert.Single(document.Add(Entry("Button"), panel, new Point2D(40, 60)));
        Assert.Same(panel, button.Parent);

        // Into the grid: its first cell.
        GridCells cells = document.CellsOf(grid)!;
        Rect2D first = cells.Cell(1, 1);
        Rect2D box = document.BoxOf(button)!.Value.Box;
        Point2D grab = Centre(box);
        document.Select([button]);
        gestures.Press(grab, DesignButton.Left, DesignModifiers.None);
        gestures.Drag(new Point2D(grab.X + (first.X + 5 - box.X), grab.Y + (first.Y + 5 - box.Y)), DesignModifiers.None);
        gestures.Release();
        Assert.Same(grid, button.Parent);
        Assert.Equal(new GridSpan(1, 1, 1, 1), document.SpanOf(button));
        Assert.Null(button.Get("Position"));

        // Out again, into the panel, where it is dropped.
        Rect2D inGrid = document.CellBoxOf(button)!.Value;
        Point2D at = Centre(inGrid);
        gestures.Press(at, DesignButton.Left, DesignModifiers.None);
        Rect2D panelInner = document.Runtime.InnerOnSurface(panel);
        double dx = panelInner.X + 30 - inGrid.X;
        double dy = panelInner.Y + 40 - inGrid.Y;
        gestures.Drag(new Point2D(at.X + dx, at.Y + dy), DesignModifiers.None);
        gestures.Release();
        Assert.Same(panel, button.Parent);
        Assert.Null(button.Get("Layout.Row"));
        double[] position = ((NumericValue)button.Get("Position")!).Data.ToArray();
        Assert.Equal(31, position[0]);
        Assert.Equal(System.Math.Round(inGrid.Width, MidpointRounding.AwayFromZero), position[2]);
        Assert.Empty(document.Runtime.Problems);

        document.Undo();
        Assert.Same(grid, button.Parent);
        Assert.Equal(new GridSpan(1, 1, 1, 1), document.SpanOf(button));
        document.Undo();
        Assert.Same(panel, button.Parent);
        Assert.NotNull(button.Get("Position"));
    }

    /// <summary>
    /// Several components placed by position are not taken into a grid (SelectionValidator): over it,
    /// the grid holds the pointer and refuses, and they go back.
    /// </summary>
    [Fact]
    public void SeveralComponentsFromOutsideAGridAreRefusedByIt()
    {
        using DesignDocument document = Blank();
        var gestures = new DesignGestures(document) { DragThreshold = 4 };
        DesignComponent holder = Assert.Single(document.Add(Entry("Panel"), document.Design.Figure, new Point2D(340, 20)));
        DesignComponent grid = document.Gridify(holder);
        DesignComponent a = Assert.Single(document.Add(Entry("Button"), document.Design.Figure, new Point2D(20, 300)));
        DesignComponent b = Assert.Single(document.Add(Entry("Button"), document.Design.Figure, new Point2D(20, 340)));
        document.Select([a, b]);
        Rect2D target = document.CellsOf(grid)!.Cell(1, 1);
        Point2D grab = Centre(document.BoxOf(a)!.Value.Box);
        gestures.Press(grab, DesignButton.Left, DesignModifiers.None);
        gestures.Drag(new Point2D(target.X + 20, target.Y + 10), DesignModifiers.None);
        Assert.False(gestures.Placement!.CanDrop);
        Assert.Same(grid, gestures.Placement.Container);
        gestures.Release();
        Assert.Same(document.Design.Figure, a.Parent);
        Assert.Same(document.Design.Figure, b.Parent);
    }

    // --- spans -----------------------------------------------------------------------------------------

    /// <summary>
    /// A grid child's handle spans it over cells (GridChildResizeHelpers): an edge snaps to the next
    /// cell's edge once past the midpoint between the two, the box following the pointer meanwhile;
    /// release sets its cells as one edit.
    /// </summary>
    [Fact]
    public void AGridChildsHandleSpansItOverCells()
    {
        (DesignDocument document, DesignComponent grid) = GridApp();
        using (document)
        {
            var gestures = new DesignGestures(document) { DragThreshold = 4 };
            DesignComponent button = Assert.Single(gestures.Drop(Entry("Button"), new Point2D(20, 15)));
            document.Select([button]);
            DesignHandle east = gestures.Handles().Single(static h => h.Edges == ResizeEdges.East);
            Rect2D cell = document.CellBoxOf(button)!.Value;
            Assert.Equal(cell.Right, east.Box.X + (DesignGestures.HandleSize / 2));

            Point2D at = Centre(east.Box);
            Assert.True(gestures.Press(at, DesignButton.Left, DesignModifiers.None));
            gestures.Drag(new Point2D(at.X + 104, at.Y), DesignModifiers.None);
            Assert.Equal(new GridSpan(1, 1, 1, 1), gestures.SpanPreview!.Value.Span);
            Assert.Equal(cell.Width + 104, gestures.SpanPreview!.Value.Box.Width);
            gestures.Drag(new Point2D(at.X + 106, at.Y), DesignModifiers.None);
            Assert.Equal(new GridSpan(1, 1, 1, 2), gestures.SpanPreview!.Value.Span);
            gestures.Release();
            Assert.Equal(new GridSpan(1, 1, 1, 2), document.SpanOf(button));
            Assert.Contains("app.Button.Layout.Column = [1 2];", Lines(document, "Button"));
            document.Undo();
            Assert.Equal(new GridSpan(1, 1, 1, 1), document.SpanOf(button));
        }
    }

    // --- rows and columns ------------------------------------------------------------------------------

    /// <summary>
    /// A row inserted moves the children at and after it down a row and grows a child spanning across
    /// it; a row deleted deletes the children in it alone, shrinks a child spanning it and moves the
    /// rest up (AddRowCommand, DeleteRowCommand, GridUtil). Each is one edit, undone whole.
    /// </summary>
    [Fact]
    public void RowsInsertedAndDeletedMoveTheChildrenAsAppDesignersDo()
    {
        (DesignDocument document, DesignComponent grid) = GridApp();
        using (document)
        {
            var gestures = new DesignGestures(document);
            DesignComponent a = Assert.Single(gestures.Drop(Entry("Button"), new Point2D(20, 15)));
            DesignComponent b = Assert.Single(gestures.Drop(Entry("Button"), new Point2D(230, 15)));
            DesignComponent c = Assert.Single(gestures.Drop(Entry("Button"), new Point2D(440, 75)));
            document.SetGridSpan(b, new GridSpan(1, 2, 2, 2));

            document.InsertTrack(grid, GridAxis.Row, 2);
            Assert.Equal(9, document.TracksOf(grid, GridAxis.Row).Count);
            Assert.Equal(new GridSpan(1, 1, 1, 1), document.SpanOf(a));
            Assert.Equal(new GridSpan(1, 3, 2, 2), document.SpanOf(b));
            Assert.Equal(new GridSpan(3, 3, 3, 3), document.SpanOf(c));
            Assert.Equal([grid], document.Selection);
            document.Undo();
            Assert.Equal(8, document.TracksOf(grid, GridAxis.Row).Count);
            Assert.Equal(new GridSpan(1, 2, 2, 2), document.SpanOf(b));

            document.DeleteTrack(grid, GridAxis.Row, 2);
            Assert.Equal(7, document.TracksOf(grid, GridAxis.Row).Count);
            Assert.Null(document.Design.Find(c.CodeName));
            Assert.Equal(new GridSpan(1, 1, 2, 2), document.SpanOf(b));
            Assert.Contains("app.Button2.Layout.Row = 1;", Lines(document, b.CodeName));
            document.Undo();
            Assert.Same(grid, c.Parent);
            Assert.Equal(new GridSpan(2, 2, 3, 3), document.SpanOf(c));
            Assert.Equal(new GridSpan(1, 2, 2, 2), document.SpanOf(b));

            document.InsertTrack(grid, GridAxis.Column, 1);
            Assert.Equal(new GridSpan(1, 1, 2, 2), document.SpanOf(a));
            Assert.Equal(4, document.TracksOf(grid, GridAxis.Column).Count);
            Assert.Empty(document.Runtime.Problems);
        }
    }

    // --- applying and removing grid layouts ------------------------------------------------------------

    /// <summary>
    /// Apply Grid Layout on a figure holding three buttons in a row gives the grid App Designer's
    /// gridifier gives (its own code run on the same scene, gridify spec sample A): fixed tracks for
    /// the buttons, weighted ones round them, and each button in its cell. Remove Grid Layout puts the
    /// buttons back in the figure where they are drawn, and the grid goes.
    /// </summary>
    [Fact]
    public void ApplyAndRemoveGridLayoutAsAppDesignersDo()
    {
        using DesignDocument document = Blank();
        foreach (double x in new[] { 100.0, 250, 400 })
        {
            // [x 400 100 22] in MATLAB's pixels: from the top-left, (x - 1, 480 - 399 - 22).
            document.Add(Entry("Button"), document.Design.Figure, new Point2D(x - 1, 59));
        }

        DesignComponent grid = document.Gridify(document.Design.Figure);
        Assert.Equal(
        [
            "app.GridLayout = uigridlayout(app.UIFigure);",
            "app.GridLayout.ColumnWidth = {'1.18x', 100, '1x', 100, '1x', 100, '2.02x'};",
            "app.GridLayout.RowHeight = {'1x', 22, '9.72x'};",
        ], Lines(document, "GridLayout"));
        Assert.Equal(new GridSpan(2, 2, 2, 2), document.SpanOf(Find(document, "Button")));
        Assert.Equal(new GridSpan(2, 2, 4, 4), document.SpanOf(Find(document, "Button2")));
        Assert.Equal(new GridSpan(2, 2, 6, 6), document.SpanOf(Find(document, "Button3")));
        Assert.Equal([grid], document.Selection);
        Assert.Empty(document.Runtime.Problems);

        Rect2D drawn = document.BoxOf(Find(document, "Button2"))!.Value.Box;
        document.Ungridify(grid);
        Assert.Null(document.Design.Find("GridLayout"));
        DesignComponent button2 = Find(document, "Button2");
        Assert.Same(document.Design.Figure, button2.Parent);
        Assert.Null(button2.Get("Layout.Row"));
        Rect2D placed = document.BoxOf(button2)!.Value.Box;
        Assert.True(System.Math.Abs(placed.X - drawn.X) <= 0.5 && System.Math.Abs(placed.Y - drawn.Y) <= 0.5, $"{placed} for {drawn}");
        Assert.Equal((drawn.Width, drawn.Height), (placed.Width, placed.Height));

        document.Undo();
        Assert.Same(Find(document, "GridLayout"), button2.Parent);
        Assert.Equal(new GridSpan(2, 2, 4, 4), document.SpanOf(button2));
        document.Undo();
        Assert.Same(document.Design.Figure, button2.Parent);
        Assert.Null(document.Design.Find("GridLayout"));
        Assert.Empty(document.Runtime.Problems);
    }

    /// <summary>
    /// A Grid Layout dropped on a grid makes a grid in the cells its avatar (204 by 141) covers,
    /// sized as the empty gridifier sizes one for them (GridifyCommand._addGridIntoGrid). The inner
    /// grid has handles; one filling its container has none.
    /// </summary>
    [Fact]
    public void AGridLayoutDroppedOnACellNestsAGrid()
    {
        (DesignDocument document, DesignComponent grid) = GridApp();
        using (document)
        {
            var gestures = new DesignGestures(document);
            Rect2D cell = document.CellsOf(grid)!.Cell(1, 1);
            DesignComponent inner = Assert.Single(gestures.Drop(Entry("Grid Layout"), new Point2D(cell.X + 2, cell.Y + 2)));
            Assert.Same(grid, inner.Parent);
            Assert.Equal("GridLayout2", inner.CodeName);
            Assert.Equal(new GridSpan(1, 3, 1, 1), document.SpanOf(inner));
            Assert.Equal(3, document.TracksOf(inner, GridAxis.Row).Count);
            Assert.Equal(2, document.TracksOf(inner, GridAxis.Column).Count);
            document.Select([inner]);
            Assert.Equal(8, gestures.Handles().Count());
            document.Select([grid]);
            Assert.Empty(gestures.Handles());
            Assert.Empty(document.Runtime.Problems);
        }
    }

    // --- the context menu and configuring --------------------------------------------------------------

    /// <summary>
    /// The context menu of a selected grid offers App Designer's grid items, by where the click is: in
    /// a cell, a row above or below it and a column either side, and its row and column to delete; in
    /// a gap, a row into the gap and nothing to delete; in the bottom padding, a row after the last
    /// (App Designer tests the last row's top there, so it offers none).
    /// </summary>
    [Fact]
    public void TheGridMenuOffersRowsAndColumnsByWhereTheClickIs()
    {
        (DesignDocument document, DesignComponent grid) = GridApp();
        using (document)
        {
            var gestures = new DesignGestures(document);
            GridCells cells = document.CellsOf(grid)!;
            IReadOnlyList<DesignMenuItem> menu = gestures.MenuAt(Centre(cells.Cell(2, 2)));
            Assert.Equal(
                ["Configure Grid Layout", "Add Row", "Add Column", "Delete Row", "Delete Column", "Remove Grid Layout", "-", "Delete"],
                menu.Select(static i => i.Label));
            DesignMenuItem addRow = menu.Single(static i => i.Label == "Add Row");
            Assert.Equal([2, 3], addRow.Items!.Select(static i => i.Hint!.Value.Index));
            Assert.True(menu.Single(static i => i.Label == "Delete Row").Enabled);

            menu = gestures.MenuAt(new Point2D(100, cells.Cell(1, 1).Bottom + 5));
            Assert.Equal([2, 2], menu.Single(static i => i.Label == "Add Row").Items!.Select(static i => i.Hint!.Value.Index));
            Assert.False(menu.Single(static i => i.Label == "Delete Row").Enabled);

            menu = gestures.MenuAt(new Point2D(100, cells.Cell(8, 1).Bottom + 5));
            Assert.Equal([9, 9], menu.Single(static i => i.Label == "Add Row").Items!.Select(static i => i.Hint!.Value.Index));

            menu.Single(static i => i.Label == "Add Row").Items![0].Run!();
            Assert.Equal(9, document.TracksOf(grid, GridAxis.Row).Count);

            document.Select([document.Design.Figure]);
            Assert.Contains(gestures.MenuAt(new Point2D(5, 5)), static i => i.Label == "Apply Grid Layout");
        }
    }

    /// <summary>
    /// Configuring a grid (App Designer's grid edit mode): a band picked shows its kind in the popup; a
    /// kind chosen sets the track - fixed at what it is drawn, rounded; a weight past 100 is refused; a
    /// row inserted below is picked; the last row deleted deletes the grid.
    /// </summary>
    [Fact]
    public void ConfiguringAGridSetsItsRowsAndColumns()
    {
        (DesignDocument document, DesignComponent grid) = GridApp();
        using (document)
        {
            var gestures = new DesignGestures(document);
            Assert.True(gestures.DoubleClick(new Point2D(5, 5)));
            DesignGridConfigure configure = gestures.Configuring!;
            Assert.Equal(DesignGridMode.Edit, gestures.GridModes()[grid]);
            Assert.Equal(11, configure.Bands().Count);
            Assert.Empty(gestures.Handles());

            GridCells cells = document.CellsOf(grid)!;
            Rect2D row2 = cells.Cell(2, 1);
            Point2D tab = new(document.BoxOf(grid)!.Value.Box.X - 10, row2.Y + 10);
            Assert.False(gestures.Press(tab, DesignButton.Left, DesignModifiers.None));
            Assert.Equal((GridAxis.Row, 2), configure.Selected);
            Assert.Equal(DesignTrackKind.Weighted, configure.Kind);
            Assert.Equal(1, configure.Weight);
            Assert.Equal(49, configure.Pixels);

            Assert.Null(configure.Choose(DesignTrackKind.Fixed));
            Assert.Equal(NumericValue.Scalar(49).Data, ((NumericValue)document.TracksOf(grid, GridAxis.Row)[1]).Data);
            Assert.NotNull(configure.SetWeight(150));
            Assert.Null(configure.Choose(DesignTrackKind.Weighted));
            Assert.Equal("1x", ((CharValue)document.TracksOf(grid, GridAxis.Row)[1]).Text);
            Assert.Null(configure.SetWeight(2.5));
            Assert.Equal("2.5x", ((CharValue)document.TracksOf(grid, GridAxis.Row)[1]).Text);

            configure.InsertAfter();
            Assert.Equal(9, document.TracksOf(grid, GridAxis.Row).Count);
            Assert.Equal((GridAxis.Row, 3), configure.Selected);

            Assert.True(gestures.KeyDown(DesignKey.Escape, DesignModifiers.None));
            Assert.Null(gestures.Configuring);

            // A grid of one row: deleting it deletes the grid.
            Assert.Null(document.SetProperty(grid, "RowHeight", CellValue.Row(new CharValue("1x"))));
            gestures.Configure(grid);
            gestures.Press(new Point2D(tab.X, document.CellsOf(grid)!.Cell(1, 1).Y + 5), DesignButton.Left, DesignModifiers.None);
            Assert.Equal((GridAxis.Row, 1), gestures.Configuring!.Selected);
            Assert.True(gestures.Configuring!.DeleteTrack());
            Assert.Null(document.Design.Find("GridLayout"));
        }
    }

    /// <summary>
    /// Which grids show their cells (SelectableController._visualize): with a child selected its grid
    /// shows its cells and configure button and a grid around that one its cells; with the figure
    /// selected, the grids not in grids show their buttons and the ones in grids their cells.
    /// </summary>
    [Fact]
    public void GridsShowTheirCellsAsTheSelectionHasThem()
    {
        (DesignDocument document, DesignComponent grid) = GridApp();
        using (document)
        {
            var gestures = new DesignGestures(document);
            Rect2D cell = document.CellsOf(grid)!.Cell(1, 1);
            DesignComponent inner = Assert.Single(gestures.Drop(Entry("Grid Layout"), new Point2D(cell.X + 2, cell.Y + 2)));
            Rect2D innerCell = document.CellsOf(inner)!.Cell(1, 1);
            DesignComponent button = Assert.Single(gestures.Drop(Entry("Button"), new Point2D(innerCell.X + 1, innerCell.Y + 1)));
            Assert.Same(inner, button.Parent);

            document.Select([button]);
            IReadOnlyDictionary<DesignComponent, DesignGridMode> modes = gestures.GridModes();
            Assert.Equal(DesignGridMode.Simple, modes[inner]);
            Assert.Equal(DesignGridMode.Layout, modes[grid]);

            document.Select([document.Design.Figure]);
            modes = gestures.GridModes();
            Assert.Equal(DesignGridMode.Simple, modes[grid]);
            Assert.Equal(DesignGridMode.Layout, modes[inner]);
            Assert.Equal(grid, gestures.ConfigureButtonAt(new Point2D(-14, -14)));
        }
    }

    /// <summary>
    /// The configure button shown with the figure selected configures its grid; configuring ends when
    /// the selection changes and does not come back with it. A grid with no columns has no bands, and
    /// its menu adds a column. The popup follows an undo.
    /// </summary>
    [Fact]
    public void ConfiguringStartsAndEndsAsAppDesignersDoes()
    {
        (DesignDocument document, DesignComponent grid) = GridApp();
        using (document)
        {
            var gestures = new DesignGestures(document);
            document.Select([document.Design.Figure]);
            Assert.False(gestures.Press(new Point2D(-14, -14), DesignButton.Left, DesignModifiers.None));
            Assert.Same(grid, gestures.Configuring?.Grid);
            document.Select([grid]);
            Assert.Null(gestures.Configuring);
            document.Select([document.Design.Figure]);
            Assert.Null(gestures.Configuring);

            // A pick, an edit, and the popup after its undo.
            gestures.Configure(grid);
            DesignGridConfigure configure = gestures.Configuring!;
            Rect2D row = document.CellsOf(grid)!.Cell(2, 1);
            gestures.Press(new Point2D(-10, row.Y + 5), DesignButton.Left, DesignModifiers.None);
            Assert.Null(configure.Choose(DesignTrackKind.Fixed));
            Assert.Equal(DesignTrackKind.Fixed, configure.Kind);
            document.Undo();
            configure.Refresh();
            Assert.Equal(DesignTrackKind.Weighted, configure.Kind);
            Assert.False(gestures.Press(new Point2D(-10, row.Y + 5), DesignButton.Right, DesignModifiers.None));
            Assert.Equal((GridAxis.Row, 2), configure.Selected);

            // No columns: no bands, and the menu's Add Column takes the first.
            Assert.Null(document.SetProperty(grid, "ColumnWidth", CellValue.Row()));
            gestures.Configure(grid);
            Assert.Empty(gestures.Configuring!.Bands());
            gestures.Configuring!.Refresh();
            DesignMenuItem addColumn = gestures.MenuAt(new Point2D(20, 20)).Single(static i => i.Label == "Add Column");
            Assert.True(addColumn.Enabled);
            addColumn.Items![0].Run!();
            Assert.Single(document.TracksOf(grid, GridAxis.Column));
        }
    }

    /// <summary>
    /// Undo puts components moved by one edit back in their order: three buttons laid out in a grid and
    /// taken out again come back in the grid as they were in it, and in the figure as they were there.
    /// </summary>
    [Fact]
    public void UndoPutsMovedComponentsBackInTheirOrder()
    {
        using DesignDocument document = Blank();
        foreach (double x in new[] { 100.0, 250, 400 })
        {
            document.Add(Entry("Button"), document.Design.Figure, new Point2D(x - 1, 59));
        }

        DesignComponent grid = document.Gridify(document.Design.Figure);
        string[] inGrid = grid.Children.Select(static c => c.CodeName).ToArray();
        Assert.Equal(["Button", "Button2", "Button3"], inGrid);
        document.Ungridify(grid);
        Assert.Equal([document.Design.Figure], document.Selection);
        document.Undo();
        Assert.Equal(inGrid, grid.Children.Select(static c => c.CodeName));
        document.Undo();
        Assert.Equal(["Button", "Button2", "Button3"], document.Design.Figure.Children.Select(static c => c.CodeName));
    }

    /// <summary>
    /// An axes laid out in a grid - which pins it to its cell - and the edit undone is where it was, in
    /// the design and on the canvas.
    /// </summary>
    [Fact]
    public void AnAxesLaidOutInAGridAndUndoneIsWhereItWas()
    {
        using DesignDocument document = Blank();
        DesignComponent axes = Assert.Single(document.Add(Entry("Axes"), document.Design.Figure, new Point2D(40, 30)));
        Rect2D before = document.BoxOf(axes)!.Value.Box;
        Rect2D? position = document.Runtime.PositionOf(axes);
        document.Gridify(document.Design.Figure);
        Assert.True(axes.IsInGrid);
        document.Undo();
        Assert.False(axes.IsInGrid);
        Assert.Equal(before, document.BoxOf(axes)!.Value.Box);
        Assert.Equal(position, document.Runtime.PositionOf(axes));
    }

    /// <summary>
    /// A container under the pointer that cannot take a drop refuses it there: a Grid Layout on a
    /// button group does not fall through to lay out the figure (RestrictedParentValidator).
    /// </summary>
    [Fact]
    public void AButtonGroupRefusesAGridLayout()
    {
        using DesignDocument document = Blank();
        var gestures = new DesignGestures(document);
        DesignComponent group = document.Add(Entry("Radio Button Group"), document.Design.Figure, new Point2D(20, 20))[0];
        Rect2D box = document.BoxOf(group)!.Value.Box;
        Assert.Empty(gestures.Drop(Entry("Grid Layout"), Centre(box)));
        Assert.NotNull(gestures.Refusal);
        Assert.DoesNotContain(document.Design.Components, static c => c.Type == DesignDocument.GridType);
    }

    /// <summary>
    /// A panel in a grid's cell takes a component dragged over it only when the whole of it is inside
    /// (BoundsPositionValidator); otherwise the grid takes it into the cells it covers.
    /// </summary>
    [Fact]
    public void APanelInACellTakesOnlyWhatIsWhollyInsideIt()
    {
        (DesignDocument document, DesignComponent grid) = GridApp();
        using (document)
        {
            var gestures = new DesignGestures(document) { DragThreshold = 4 };
            GridCells cells = document.CellsOf(grid)!;
            Rect2D second = cells.Cell(1, 2);
            DesignComponent panel = Assert.Single(gestures.Drop(Entry("Panel"), new Point2D(second.X + 1, second.Y + 1)));
            document.SetGridSpan(panel, new GridSpan(1, 3, 2, 2));
            DesignComponent button = Assert.Single(gestures.Drop(Entry("Button"), new Point2D(20, 15)));
            Rect2D panelBox = document.BoxOf(panel)!.Value.Box;
            Rect2D cell = document.CellBoxOf(button)!.Value;

            // Half over the panel: into the grid's cells, the panel's included.
            Point2D grab = Centre(cell);
            gestures.Press(grab, DesignButton.Left, DesignModifiers.None);
            gestures.Drag(new Point2D(grab.X + 120, grab.Y), DesignModifiers.None);
            gestures.Release();
            Assert.Same(grid, button.Parent);
            Assert.Equal(1, document.SpanOf(button)!.Value.RowStart);

            // Wholly inside the panel - the box dragged is the button's cell, as wide as the panel: into the panel.
            Rect2D now = document.CellBoxOf(button)!.Value;
            Assert.True(now.Width <= panelBox.Width);
            grab = Centre(now);
            gestures.Press(grab, DesignButton.Left, DesignModifiers.None);
            gestures.Drag(new Point2D(grab.X + (panelBox.X - now.X), grab.Y + (panelBox.Y + 40 - now.Y)), DesignModifiers.None);
            Assert.Same(panel, gestures.Placement!.Container);
        }
    }

    /// <summary>
    /// A grid child drawn as a shape smaller than its cells - a knob - is dragged out at its drawn
    /// size, where its drawn self was dragged; it has its four corner handles only, as every shape
    /// that keeps its ratio does.
    /// </summary>
    [Fact]
    public void AShapeInACellMovesOutAsItIsDrawn()
    {
        using DesignDocument document = Blank();
        var gestures = new DesignGestures(document) { DragThreshold = 4 };
        DesignComponent holder = Assert.Single(document.Add(Entry("Panel"), document.Design.Figure, new Point2D(300, 20)));
        DesignComponent grid = document.Gridify(holder);
        Rect2D first = document.CellsOf(grid)!.Cell(1, 1);
        gestures.Drop(Entry("Knob"), new Point2D(first.X + 1, first.Y + 1));
        DesignComponent knob = Find(document, "Knob");
        Assert.Same(grid, knob.Parent);
        document.Select([knob]);
        Assert.Equal(4, gestures.Handles().Count());

        Rect2D drawn = document.BoxOf(knob)!.Value.Box;
        Rect2D cell = document.CellBoxOf(knob)!.Value;
        Point2D grab = Centre(cell);
        double dx = 20 - cell.X;
        double dy = 250 - cell.Y;
        gestures.Press(grab, DesignButton.Left, DesignModifiers.None);
        gestures.Drag(new Point2D(grab.X + dx, grab.Y + dy), DesignModifiers.None);
        gestures.Release();
        Assert.Same(document.Design.Figure, knob.Parent);
        Rect2D placed = document.BoxOf(knob)!.Value.Box;
        Assert.True(System.Math.Abs(placed.X - (drawn.X + dx)) <= 1 && System.Math.Abs(placed.Y - (drawn.Y + dy)) <= 1, $"{placed} for {drawn} moved by ({dx}, {dy})");
        Assert.True(System.Math.Abs(placed.Width - drawn.Width) <= 1);
    }

    /// <summary>
    /// App Designer's comma form, as its grid's design-time controller reads it: a part of several
    /// numbers gives as many; an empty part among numbers is dropped, among keywords refused; empty
    /// text is refused.
    /// </summary>
    [Fact]
    public void TracksTypedInTheCommaFormAreReadAsAppDesignerReadsThem()
    {
        (DesignDocument document, DesignComponent grid) = GridApp();
        using (document)
        {
            Assert.Equal(2, ((CellValue)document.ParseTracks(grid, GridAxis.Column, "100 200").Value!).Items.Count);
            Assert.Equal(2, ((CellValue)document.ParseTracks(grid, GridAxis.Column, "100, , 200").Value!).Items.Count);
            Assert.Null(document.ParseTracks(grid, GridAxis.Column, "1x, , 2x").Value);
            Assert.Null(document.ParseTracks(grid, GridAxis.Column, "").Value);
        }
    }

    // --- the inspector ---------------------------------------------------------------------------------

    /// <summary>
    /// A grid child's Position is shown and not edited (DisabledPropertiesInGrid). A grid's rows typed
    /// in App Designer's comma form set them; fewer than its children reach are refused
    /// (GridUtil.isNotEnoughSpaceOnGrid).
    /// </summary>
    [Fact]
    public void TheInspectorEditsAGridsTracksAndNotAChildsPosition()
    {
        (DesignDocument document, DesignComponent grid) = GridApp();
        using (document)
        {
            var gestures = new DesignGestures(document);
            Rect2D cell = document.CellsOf(grid)!.Cell(3, 1);
            DesignComponent button = Assert.Single(gestures.Drop(Entry("Button"), new Point2D(cell.X + 1, cell.Y + 1)));
            Assert.False(document.IsEditable(button, "Position"));
            Assert.True(document.IsEditable(button, "Text"));

            (DesignValue? rows, string? refused) = document.ParseTracks(grid, GridAxis.Row, "fit, 2X, 40");
            Assert.Null(refused);
            Assert.Null(document.SetProperty(grid, "RowHeight", rows));
            Assert.Contains("app.GridLayout.RowHeight = {'fit', '2x', 40};", Lines(document, "GridLayout"));

            (rows, refused) = document.ParseTracks(grid, GridAxis.Row, "1x, 1x");
            Assert.Null(rows);
            Assert.NotNull(refused);
        }
    }

    // --- shipped apps and the round trip ---------------------------------------------------------------

    /// <summary>
    /// Where R2025b is installed, every grid in the apps it ships is drawn with its cells, and every
    /// child of one is drawn inside the cells it sits in - the whole cell, or a shape centred in it.
    /// </summary>
    [Fact]
    public void EveryShippedGridIsDrawnWithItsChildrenInTheirCells()
    {
        if (!Directory.Exists(ShippedRoot))
        {
            return;
        }

        int grids = 0;
        int children = 0;
        foreach (string path in Directory.EnumerateFiles(ShippedRoot, "*.mlapp", SearchOption.AllDirectories))
        {
            using DesignDocument document = DesignDocument.Open(path);
            foreach (DesignComponent grid in document.Design.Components.Where(document.IsGrid))
            {
                if (document.BoxOf(grid) is not { Shown: true })
                {
                    continue;
                }

                GridCells cells = Assert.IsType<GridCells>(document.CellsOf(grid));
                grids++;
                Assert.Equal(document.TracksOf(grid, GridAxis.Row).Count, cells.Rows);
                foreach (DesignComponent child in grid.Children)
                {
                    if (document.BoxOf(child) is not { } drawn || document.CellBoxOf(child) is not { } cell)
                    {
                        continue;
                    }

                    children++;
                    Rect2D box = drawn.Box;
                    Assert.True(
                        box.X >= cell.X - 0.5 && box.Y >= cell.Y - 0.5 && box.Right <= cell.Right + 0.5 && box.Bottom <= cell.Bottom + 0.5,
                        $"{Path.GetFileName(path)}: {child.CodeName} drawn at {box} outside its cells {cell}");
                }
            }
        }

        output.WriteLine($"{grids} grids, {children} children in their cells");
        Assert.True(grids >= 15);
    }

    /// <summary>
    /// A shipped app's grids edited here - a row inserted in each grid, a child spanned one more
    /// column, a row deleted - save, keeping every other component as App Designer saved it, and open
    /// again the same. With <c>JGRAPH_DS6_OUT</c> the edited app is written to <c>new/</c> there, with
    /// its createComponents beside it, for R2025b's App Designer to read against a control made from
    /// that code (<c>ds6_verify</c>, <c>DS6_PART=new</c>).
    /// </summary>
    [Fact]
    public void AShippedAppsGridsEditedHereSaveAndOpenTheSame()
    {
        string shipped = Path.Combine(ShippedRoot, @"matlab\guide\+guide\+internal\GUIDEAppMaintenanceOptions.mlapp");
        if (!File.Exists(shipped))
        {
            return;
        }

        string? outFolder = Environment.GetEnvironmentVariable("JGRAPH_DS6_OUT");
        string temp = Path.Combine(Path.GetTempPath(), "jgraph-ds4-" + Guid.NewGuid().ToString("N"));
        string folder = Path.Combine(outFolder is null ? temp : Path.Combine(outFolder, "new"), "92_GUIDEAppMaintenanceOptions");
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "GUIDEAppMaintenanceOptions.mlapp");
            File.Copy(shipped, path, overwrite: true);
            File.SetAttributes(path, FileAttributes.Normal);
            string[] expected;
            int edits = 0;
            using (DesignDocument document = DesignDocument.Open(path))
            {
                Assert.Empty(document.ReadProblems);
                foreach (DesignComponent grid in document.Design.Components.Where(document.IsGrid).ToArray())
                {
                    document.InsertTrack(grid, GridAxis.Row, 1);
                    edits++;
                    if (grid.Children.FirstOrDefault(c => document.SpanOf(c) is { } s && s.ColumnEnd < document.TracksOf(grid, GridAxis.Column).Count) is { } child)
                    {
                        GridSpan span = document.SpanOf(child)!.Value;
                        document.SetGridSpan(child, span with { ColumnEnd = span.ColumnEnd + 1 });
                        edits++;
                    }
                }

                DesignComponent first = document.Design.Components.First(document.IsGrid);
                document.DeleteTrack(first, GridAxis.Row, 1);
                Assert.Empty(document.Runtime.Problems);
                expected = DesignCodeWriter.Method(document.Design, document.Style).ToArray();
                document.Save();
            }

            output.WriteLine($"{edits} grid edits");
            using DesignDocument reopened = DesignDocument.Open(path);
            Assert.Empty(reopened.ReadProblems);
            Assert.Empty(reopened.Runtime.Problems);
            Assert.Equal(expected, DesignCodeWriter.Method(reopened.Design, reopened.Style));
            if (outFolder is not null)
            {
                File.WriteAllLines(Path.Combine(folder, "GUIDEAppMaintenanceOptions__code.m"), expected);
            }
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    /// <summary>
    /// An app with grids built on the canvas - a grid laid over a panel's components, one nested in a
    /// cell, children spanning cells, rows added - saves and opens again the same. With
    /// <c>JGRAPH_DS6_OUT</c> it is written to <c>new/</c> there, with its createComponents beside it,
    /// for R2025b's App Designer to read (<c>ds6_verify</c>, <c>DS6_PART=new</c>).
    /// </summary>
    [Fact]
    public void AnAppWithGridsBuiltOnTheCanvasSavesAndOpensTheSame()
    {
        string? outFolder = Environment.GetEnvironmentVariable("JGRAPH_DS6_OUT");
        string temp = Path.Combine(Path.GetTempPath(), "jgraph-ds4-" + Guid.NewGuid().ToString("N"));
        string folder = Path.Combine(outFolder is null ? temp : Path.Combine(outFolder, "new"), "91_GridApp");
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "GridApp.mlapp");
            AppDesignerDocument.SaveNew(path, AppDesignerDocument.NewAppText());
            string[] expected;
            using (DesignDocument document = DesignDocument.Open(path))
            {
                var gestures = new DesignGestures(document);

                // A panel of components laid out in a grid.
                DesignComponent panel = Assert.Single(document.Add(Entry("Panel"), document.Design.Figure, new Point2D(20, 20)));
                document.Add(Entry("Button"), panel, new Point2D(40, 60));
                document.Add(Entry("Edit Field (Text)"), panel, new Point2D(40, 100));
                document.Add(Entry("List Box"), panel, new Point2D(40, 140));
                DesignComponent panelGrid = document.Gridify(panel);
                Assert.Equal(5, panelGrid.Children.Count);

                // A second panel with a grid of its own, a grid nested in its cell, and spans.
                DesignComponent holder = Assert.Single(document.Add(Entry("Panel"), document.Design.Figure, new Point2D(330, 20)));
                DesignComponent grid = document.Gridify(holder);
                Rect2D cell = document.CellsOf(grid)!.Cell(1, 1);
                DesignComponent inner = Assert.Single(gestures.Drop(Entry("Grid Layout"), new Point2D(cell.X + 1, cell.Y + 1)));
                document.SetGridSpan(inner, GridSpan.Cell(1, 1));
                Rect2D innerCell = document.CellsOf(inner)!.Cell(1, 1);
                gestures.Drop(Entry("Lamp"), new Point2D(innerCell.X + 1, innerCell.Y + 1));
                Rect2D second = document.CellsOf(grid)!.Cell(2, 1);
                Assert.NotEmpty(gestures.Drop(Entry("Slider"), new Point2D(second.X + 1, second.Y + 1)));
                DesignComponent slider = Find(document, "Slider");
                Assert.Same(grid, slider.Parent);
                document.SetGridSpan(slider, new GridSpan(2, 3, 1, 2));
                document.InsertTrack(grid, GridAxis.Row, 1);
                Assert.Null(document.SetTrack(grid, GridAxis.Column, 1, NumericValue.Scalar(90)));

                Assert.Empty(document.Runtime.Problems);
                expected = DesignCodeWriter.Method(document.Design, document.Style).ToArray();
                document.Save();
            }

            using DesignDocument reopened = DesignDocument.Open(path);
            Assert.Empty(reopened.ReadProblems);
            Assert.Empty(reopened.Runtime.Problems);
            Assert.Equal(expected, DesignCodeWriter.Method(reopened.Design, reopened.Style));
            Assert.Equal(new GridSpan(3, 4, 1, 2), reopened.SpanOf(reopened.Design.Find("Slider")!));
            if (outFolder is not null)
            {
                File.WriteAllLines(Path.Combine(folder, "GridApp__code.m"), DesignCodeWriter.Method(reopened.Design, reopened.Style));
            }
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }
}
