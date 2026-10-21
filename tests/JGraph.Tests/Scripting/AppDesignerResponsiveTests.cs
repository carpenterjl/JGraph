using System.Text.Json;
using JGraph.Core.Primitives;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Workspace;
using Xunit;
using Xunit.Abstractions;

namespace JGraph.Tests.Scripting;

/// <summary>
/// DS4b of the designer plan: App Designer's responsive apps - the 2- and 3-panel apps with
/// auto-reflow - made, recognised, edited on the canvas as App Designer lets them be, and their
/// generated callback written again on every save.
/// </summary>
[Collection("JG facade")]
public class AppDesignerResponsiveTests(ITestOutputHelper output)
{
    private const string ShippedRoot = @"C:\Program Files\MATLAB\R2025b\toolbox";

    /// <summary>The responsive app R2025b ships, the only one: written by App Designer (R2025a).</summary>
    private static string? SystemLogViewer() =>
        Directory.Exists(ShippedRoot)
            ? Directory.EnumerateFiles(ShippedRoot, "SystemLogViewer.mlapp", SearchOption.AllDirectories).FirstOrDefault()
            : null;

    // --- the code App Designer generates -------------------------------------------------------------

    /// <summary>
    /// Written again from its design, the shipped responsive app's widths block and updateAppLayout are
    /// what App Designer wrote: the regions' heights the figure's, the leading region's width its
    /// column's, the names the app's own.
    /// </summary>
    [Fact]
    public void TheShippedResponsiveAppsGeneratedCodeIsWrittenAsAppDesignerWroteIt()
    {
        if (SystemLogViewer() is not { } path)
        {
            return;
        }

        string text = JgsMlapp.ReadCode(path);
        DesignReadResult read = DesignCodeReader.Read(text);
        AppDesign design = read.Design!;
        Assert.True(design.IsResponsive);
        Assert.Equal(2, DesignResponsive.Panels(design));
        Assert.Equal(["LeftPanel", "RightPanel"], DesignResponsive.Regions(design).Select(static r => r!.CodeName));
        Assert.Equal(text, DesignResponsive.Splice(text, design, read.Style));

        // A wider leading column changes its line, and only it.
        DesignComponent grid = DesignResponsive.Grid(design)!;
        grid.Set("ColumnWidth", CellValue.Row(NumericValue.Scalar(250), new CharValue("1x")));
        string[] before = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        string[] after = DesignResponsive.Splice(text, design, read.Style).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        Assert.Equal(before.Length, after.Length);
        string[] changed = before.Zip(after).Where(static p => p.First != p.Second).Select(static p => p.Second.Trim()).ToArray();
        Assert.Equal(["app.GridLayout.ColumnWidth = {250, '1x'};"], changed);
    }

    /// <summary>
    /// A new 2- or 3-panel app is App Designer's template: read back it is responsive with its
    /// regions in their columns, its code written again is itself, and the canvas makes it whole.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void ANewResponsiveAppIsAppDesignersTemplate(int panels)
    {
        string text = DesignResponsive.NewAppText(panels);
        output.WriteLine(text);
        DesignReadResult read = DesignCodeReader.Read(text);
        Assert.Empty(read.Problems);
        AppDesign design = read.Design!;
        Assert.True(design.IsResponsive);
        Assert.Equal(panels, DesignResponsive.Panels(design));
        Assert.Equal(
            panels == 2 ? ["LeftPanel", "RightPanel"] : ["LeftPanel", "CenterPanel", "RightPanel"],
            DesignResponsive.Regions(design).Select(static r => r!.CodeName));
        Assert.Equal(text, DesignCodeWriter.Splice(text, design, read.Style));
        Assert.Equal(text, DesignResponsive.Splice(text, design, read.Style));
        Assert.Contains("    % Properties that correspond to apps with auto-reflow", text, StringComparison.Ordinal);
        Assert.Contains("            app.UIFigure.SizeChangedFcn = createCallbackFcn(app, @updateAppLayout, true);", text, StringComparison.Ordinal);
        Assert.Contains("            app.UIFigure.AutoResizeChildren = 'off';", text, StringComparison.Ordinal);
        Assert.Contains(
            panels == 2 ? "                app.GridLayout.ColumnWidth = {220, '1x'};" : "                app.GridLayout.ColumnWidth = {220, '1x', 220};",
            text, StringComparison.Ordinal);

        using DesignDocument document = DesignDocument.FromText(text, null);
        Assert.Empty(document.Runtime.Problems);
        Rect2D left = document.BoxOf(document.Design.Find("LeftPanel")!)!.Value.Box;
        Assert.Equal(new Rect2D(0, 0, 220, 480), left);
    }

    private static DesignDocument NewApp(int panels) => DesignDocument.FromText(DesignResponsive.NewAppText(panels), null);

    private static DesignPaletteEntry Entry(string name) => DesignPalette.R2025b.Find(name) ?? throw new InvalidOperationException(name);

    private static Point2D Centre(Rect2D r) => new(r.X + (r.Width / 2), r.Y + (r.Height / 2));

    // --- the regions on the canvas -------------------------------------------------------------------

    /// <summary>
    /// The regions are App Designer's to lay out (RegionPanelModel, ResponsiveAppGridLayoutModel): not
    /// deleted, moved, resized or nudged; their position, visibility and auto-resizing, and the
    /// figure's position, shown and not edited; the grid holding them never hit, selected by Select
    /// All or deleted; no grid layout applied to the figure.
    /// </summary>
    [Fact]
    public void TheRegionsAreLockedAsAppDesignerLocksThem()
    {
        using DesignDocument document = NewApp(2);
        var gestures = new DesignGestures(document);
        DesignComponent left = document.Design.Find("LeftPanel")!;
        DesignComponent grid = document.Design.Find("GridLayout")!;
        Assert.True(document.IsResponsive);
        Assert.True(document.IsRegion(left));
        Assert.False(document.CanDelete(left));
        Assert.False(document.CanDelete(grid));
        Assert.False(document.CanDrag(left));
        Assert.False(document.IsEditable(left, "Position"));
        Assert.False(document.IsEditable(left, "Visible"));
        Assert.True(document.IsEditable(left, "Title"));
        Assert.False(document.IsEditable(document.Design.Figure, "Position"));
        Assert.True(document.IsEditable(document.Design.Figure, "Name"));
        Assert.False(document.CanGridify(document.Design.Figure));

        Assert.Same(left, document.ComponentAt(new Point2D(100, 100)));
        document.Select([left]);
        Assert.Empty(gestures.Handles());
        Assert.True(gestures.KeyDown(DesignKey.Right, DesignModifiers.None));
        Assert.False(gestures.KeyDown(DesignKey.Delete, DesignModifiers.None));
        Assert.NotNull(document.Design.Find("LeftPanel"));
        document.Select([document.Design.Figure]);
        Assert.True(gestures.KeyDown(DesignKey.SelectAll, DesignModifiers.None));
        Assert.DoesNotContain(grid, document.Selection);
        Assert.Empty(gestures.Drop(Entry("Grid Layout"), new Point2D(-20, -20)));
        Assert.Single(document.Design.Components, static c => c.Type == DesignDocument.GridType);
    }

    /// <summary>
    /// A component in a region stays in it (TemplateComponentMove, BeginTemplateComponentResize): a
    /// drag over the region's edge is refused, a resize and the arrow keys stop 6 pixels from it.
    /// </summary>
    [Fact]
    public void AComponentInARegionStaysInIt()
    {
        using DesignDocument document = NewApp(2);
        var gestures = new DesignGestures(document) { DragThreshold = 4, ShowAlignmentHints = false };
        DesignComponent left = document.Design.Find("LeftPanel")!;
        DesignComponent button = Assert.Single(document.Add(Entry("Button"), left, new Point2D(40, 40)));
        Rect2D region = document.BoxOf(left)!.Value.Box;
        Rect2D start = document.BoxOf(button)!.Value.Box;

        // Over the edge into the other region: refused, back where it was.
        document.Select([button]);
        Point2D grab = Centre(start);
        gestures.Press(grab, DesignButton.Left, DesignModifiers.None);
        gestures.Drag(new Point2D(grab.X + (region.Right - start.Right) + 30, grab.Y), DesignModifiers.None);
        Assert.False(gestures.Placement!.CanDrop);
        gestures.Release();
        Assert.Same(left, button.Parent);
        Assert.Equal(start, document.BoxOf(button)!.Value.Box);

        // A resize east stops 6 pixels from the region's edge.
        DesignHandle east = gestures.Handles().Single(static h => h.Edges == ResizeEdges.East);
        Point2D at = Centre(east.Box);
        gestures.Press(at, DesignButton.Left, DesignModifiers.None);
        gestures.Drag(new Point2D(at.X + 500, at.Y), DesignModifiers.None);
        gestures.Release();
        Assert.Equal(region.Right - DesignDocument.RegionInset, document.BoxOf(button)!.Value.Box.Right, 0.5);

        // The arrows stop there too.
        for (int i = 0; i < 5; i++)
        {
            gestures.KeyDown(DesignKey.Right, DesignModifiers.Shift);
        }

        gestures.ArrowUp(arrowHeld: false);
        Assert.Equal(region.Right - DesignDocument.RegionInset, document.BoxOf(button)!.Value.Box.Right, 0.5);
    }

    /// <summary>
    /// The splitter between the regions resizes them (App Designer's LayoutResizeEngine, its numbers
    /// from App Designer's own modules under node): dragged right 50, the leading region is 270 and
    /// the right region's component is pushed to 6 pixels from its moving edge; dragged right 200 it
    /// stops where the right region has nothing to spare (328); dragged left 150 it stops at 112 and
    /// the right region's component keeps its place on the canvas. Each drag is one edit, and the
    /// generated updateAppLayout follows the width on save.
    /// </summary>
    [Fact]
    public void TheSplitterResizesTheRegionsAsAppDesignersDoes()
    {
        using DesignDocument document = NewApp(2);
        var gestures = new DesignGestures(document) { DragThreshold = 4 };
        DesignComponent left = document.Design.Find("LeftPanel")!;
        DesignComponent right = document.Design.Find("RightPanel")!;
        Rect2D leftInner = document.Runtime.InnerOnSurface(left);
        Rect2D rightInner = document.Runtime.InnerOnSurface(right);

        // The scene of App Designer's own run: a 100-wide box at 20 in the left, a 300-wide one at 10 in the right.
        DesignComponent a = Assert.Single(document.Add(Entry("Button"), left, new Point2D(leftInner.X + 20, 100)));
        Assert.Null(document.SetProperty(a, "Position", NumericValue.Row(21, 300, 100, 22)));
        DesignComponent b = Assert.Single(document.Add(Entry("Button"), right, new Point2D(rightInner.X + 10, 100)));
        Assert.Null(document.SetProperty(b, "Position", NumericValue.Row(11, 300, 300, 22)));
        Assert.Equal(rightInner.X + 10, document.BoxOf(b)!.Value.Box.X);

        DesignSplitter splitter = Assert.Single(document.Splitters());
        Point2D at = Centre(splitter.Box);
        Assert.Equal(DesignCursor.SizeWestEast, gestures.CursorAt(at));
        void Drag(double d)
        {
            Assert.True(gestures.Press(at, DesignButton.Left, DesignModifiers.None));
            gestures.Drag(new Point2D(at.X + d, at.Y), DesignModifiers.None);
            gestures.Release();
        }

        Drag(50);
        Assert.Equal(270, document.BoxOf(left)!.Value.Box.Width);
        Assert.Equal(["270", "1x"], ((CellValue)document.Design.Find("GridLayout")!.Get("ColumnWidth")!).Items.Select(static v => DesignValueText.Format(v)).Select(static t => t.Trim('\'')));
        Assert.Equal(document.Runtime.InnerOnSurface(right).X + 6, document.BoxOf(b)!.Value.Box.X, 0.5);
        document.Undo();
        Assert.Equal(220, document.BoxOf(left)!.Value.Box.Width);
        Assert.Equal(rightInner.X + 10, document.BoxOf(b)!.Value.Box.X);

        Drag(200);
        Assert.Equal(328, document.BoxOf(left)!.Value.Box.Width);
        document.Undo();

        double before = document.BoxOf(b)!.Value.Box.X;
        Drag(-150);
        Assert.Equal(112, document.BoxOf(left)!.Value.Box.Width);
        Assert.Equal(before, document.BoxOf(b)!.Value.Box.X, 0.5);
        Assert.Empty(document.Runtime.Problems);

        string saved = DesignResponsive.Splice(DesignCodeWriter.Splice(document.Text, document.Design, document.Style)!, document.Design, document.Style);
        Assert.Contains("app.GridLayout.ColumnWidth = {112, '1x'};", saved, StringComparison.Ordinal);
    }

    private static readonly Lazy<JsonDocument> SplitterOracle = new(() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripting", "Designer", "ResponsiveSplitterOracle.json"))));

    /// <summary>
    /// A splitter dragged through a path of pointer moves does what App Designer's own engine does with
    /// it (ResponsiveSplitterOracle.json: <c>LayoutResizeEngine</c> run unaltered under node, its
    /// gesture's anchor moving on by the part of each move the regions take - 5 scenes, 8 paths, each
    /// splitter): after every move the regions' places and widths, each component's place across its
    /// region, and whether the regions stopped short of the pointer; on release the grid's columns and
    /// the components' positions, one edit that undo takes back whole.
    /// </summary>
    [Fact]
    public void ASplitterDraggedThroughAPathFollowsAppDesignersEngine()
    {
        JsonElement root = SplitterOracle.Value.RootElement;
        JsonElement scenes = root.GetProperty("scenes");
        int moves = 0;
        foreach (JsonElement c in root.GetProperty("cases").EnumerateArray())
        {
            JsonElement scene = scenes.GetProperty(c.GetProperty("scene").GetString()!);
            int panels = scene.GetArrayLength();
            string name = $"{c.GetProperty("scene").GetString()} {c.GetProperty("splitter").GetString()} {c.GetProperty("path").GetString()}";
            using DesignDocument document = NewApp(panels);
            var gestures = new DesignGestures(document) { DragThreshold = 4 };
            DesignComponent[] regions = [.. DesignResponsive.Regions(document.Design).Select(static r => r!)];
            var children = new List<DesignComponent>[panels];
            var startPositions = new List<string>();
            for (int i = 0; i < panels; i++)
            {
                children[i] = [];
                Rect2D inner = document.Runtime.InnerOnSurface(regions[i]);
                foreach (JsonElement kid in scene[i][1].EnumerateArray())
                {
                    double[] k = [.. kid.EnumerateArray().Select(static e => e.GetDouble())];
                    DesignComponent button = Assert.Single(document.Add(Entry("Button"), regions[i], new Point2D(inner.X + 20, inner.Y + 20)));
                    Assert.Null(document.SetProperty(button, "Position", NumericValue.Row(k[0] + 1, 100, k[2], k[3])));
                    children[i].Add(button);
                    startPositions.Add(DesignValueText.Format(button.Get("Position")!));
                }
            }

            string columnsBefore = DesignValueText.Format(DesignResponsive.Grid(document.Design)!.Get("ColumnWidth")!);
            Rect2D[] regionsBefore = [.. regions.Select(r => document.BoxOf(r)!.Value.Box)];
            bool leading = c.GetProperty("splitter").GetString() == "leading";
            Point2D at = Centre(document.Splitters().Single(s => s.Leading == leading).Box);
            Assert.True(gestures.Press(at, DesignButton.Left, DesignModifiers.None));
            JsonElement[] last = [];
            foreach (JsonElement step in c.GetProperty("after").EnumerateArray())
            {
                double p = step.GetProperty("pointer").GetDouble();
                gestures.Drag(new Point2D(at.X + p, at.Y), DesignModifiers.None);
                moves++;
                last = [.. step.GetProperty("regions").EnumerateArray()];
                for (int i = 0; i < panels; i++)
                {
                    Rect2D box = document.BoxOf(regions[i])!.Value.Box;
                    Assert.True(
                        last[i].GetProperty("x").GetDouble() == box.X && last[i].GetProperty("width").GetDouble() == box.Width,
                        $"{name} at {p}: region {i} is {box}, App Designer's x {last[i].GetProperty("x")} width {last[i].GetProperty("width")}");
                    Rect2D inner = document.Runtime.InnerOnSurface(regions[i]);
                    double[] kids = [.. last[i].GetProperty("children").EnumerateArray().Select(static e => e.GetDouble())];
                    for (int k = 0; k < kids.Length; k++)
                    {
                        double x = document.BoxOf(children[i][k])!.Value.Box.X - inner.X;
                        Assert.True(System.Math.Abs(x - kids[k]) < 1e-9, $"{name} at {p}: region {i} component {k} at {x}, App Designer's {kids[k]}");
                    }
                }

                Assert.True(
                    (step.GetProperty("leftover").GetDouble() != 0) == gestures.Splitting!.Value.Resize.Stopped,
                    $"{name} at {p}: stopped {gestures.Splitting!.Value.Resize.Stopped}, App Designer's leftover {step.GetProperty("leftover")}");
            }

            // Released: the columns and the components' positions where the drag left them, as one edit.
            gestures.Release();
            Assert.Null(gestures.Splitting);
            IReadOnlyList<DesignValue> columns = ((CellValue)DesignResponsive.Grid(document.Design)!.Get("ColumnWidth")!).Items;
            Assert.Equal(last[0].GetProperty("width").GetDouble(), ((NumericValue)columns[0]).Data[0]);
            Assert.Equal("'1x'", DesignValueText.Format(columns[1]));
            if (panels == 3)
            {
                Assert.Equal(last[2].GetProperty("width").GetDouble(), ((NumericValue)columns[2]).Data[0]);
            }

            // A region the drag left where it began keeps its components where they were, whatever the
            // drag did to them on the way (cacheRegionsResizeCommand's isSame); the rest are moved.
            int startAt = 0;
            for (int i = 0; i < panels; i++)
            {
                bool same = last[i].GetProperty("x").GetDouble() == regionsBefore[i].X && last[i].GetProperty("width").GetDouble() == regionsBefore[i].Width;
                double[] kids = [.. last[i].GetProperty("children").EnumerateArray().Select(static e => e.GetDouble())];
                for (int k = 0; k < kids.Length; k++, startAt++)
                {
                    if (same)
                    {
                        Assert.Equal(startPositions[startAt], DesignValueText.Format(children[i][k].Get("Position")!));
                    }
                    else
                    {
                        Assert.Equal(kids[k] + 1, ((NumericValue)children[i][k].Get("Position")!).Data[0]);
                    }
                }
            }

            string[] positionsAfter = [.. children.SelectMany(static l => l).Select(static b => DesignValueText.Format(b.Get("Position")!))];
            if (DesignValueText.Format(DesignResponsive.Grid(document.Design)!.Get("ColumnWidth")!) != columnsBefore || !positionsAfter.SequenceEqual(startPositions))
            {
                document.Undo();
                Assert.Equal(columnsBefore, DesignValueText.Format(DesignResponsive.Grid(document.Design)!.Get("ColumnWidth")!));
                Assert.Equal(startPositions, children.SelectMany(static l => l).Select(static b => DesignValueText.Format(b.Get("Position")!)));
            }

            Assert.Empty(document.Runtime.Problems);
        }

        output.WriteLine($"{SplitterOracle.Value.RootElement.GetProperty("cases").GetArrayLength()} drags, {moves} moves");
        Assert.Equal(248, moves);
    }

    /// <summary>
    /// While a splitter is dragged nothing is undone or redone, the runtime holding the drag; App
    /// Designer's alert shows while the regions stop short of the pointer and goes when the pointer
    /// comes back; a cancelled drag leaves the regions as they were.
    /// </summary>
    [Fact]
    public void ADraggedSplitterHoldsOffUndoAndShowsWhereItStops()
    {
        using DesignDocument document = NewApp(2);
        var gestures = new DesignGestures(document) { DragThreshold = 4 };
        DesignComponent left = document.Design.Find("LeftPanel")!;
        DesignComponent button = Assert.Single(document.Add(Entry("Button"), left, new Point2D(40, 40)));
        document.Select([button]);
        Assert.True(document.CanUndo);
        Point2D at = Centre(Assert.Single(document.Splitters()).Box);

        Assert.True(gestures.Press(at, DesignButton.Left, DesignModifiers.None));
        gestures.Drag(new Point2D(at.X + 30, at.Y), DesignModifiers.None);
        Assert.Equal(250, document.BoxOf(left)!.Value.Box.Width);
        Assert.False(document.CanUndo);
        document.Undo();
        Assert.Single(left.Children);
        Assert.False(gestures.Splitting!.Value.Resize.Stopped);

        // The right region is empty: it gives all but 12 pixels, and the splitter stops at 628.
        gestures.Drag(new Point2D(at.X + 1000, at.Y), DesignModifiers.None);
        Assert.Equal(628, document.BoxOf(left)!.Value.Box.Width);
        Assert.True(gestures.Splitting!.Value.Resize.Stopped);
        gestures.Drag(new Point2D(at.X + 400, at.Y), DesignModifiers.None);
        Assert.Equal(620, document.BoxOf(left)!.Value.Box.Width);
        Assert.False(gestures.Splitting!.Value.Resize.Stopped);
        gestures.Release();
        Assert.True(document.CanUndo);
        Assert.Equal([button], document.Selection);
        document.Undo();
        Assert.Equal(220, document.BoxOf(left)!.Value.Box.Width);
        Assert.Single(left.Children);
        Assert.Equal([button], document.Selection);

        // A drag that ends where it began is no edit.
        Assert.True(gestures.Press(at, DesignButton.Left, DesignModifiers.None));
        gestures.Drag(new Point2D(at.X + 60, at.Y), DesignModifiers.None);
        gestures.Drag(at, DesignModifiers.None);
        gestures.Release();
        Assert.True(document.CanRedo);

        Assert.True(gestures.Press(at, DesignButton.Left, DesignModifiers.None));
        gestures.Drag(new Point2D(at.X + 60, at.Y), DesignModifiers.None);
        Assert.Equal(280, document.BoxOf(left)!.Value.Box.Width);
        gestures.Cancel();
        Assert.Equal(220, document.BoxOf(left)!.Value.Box.Width);
        Assert.True(document.CanUndo);
    }

    /// <summary>
    /// The responsive grid is never the user's: a band over the whole figure selects the regions, not
    /// it; the inspector edits none of its properties; the figure takes nothing dragged or dropped
    /// outside every region, not a grid moved out of one (PlacementUtil.canPlaceGrid) nor a new
    /// component; and an app whose grid is not one of App Designer's templates is a standard app here,
    /// as App Designer opens it.
    /// </summary>
    [Fact]
    public void TheResponsiveGridIsNeverTheUsers()
    {
        using DesignDocument document = NewApp(2);
        var gestures = new DesignGestures(document) { DragThreshold = 4 };
        DesignComponent grid = DesignResponsive.Grid(document.Design)!;
        DesignComponent left = document.Design.Find("LeftPanel")!;
        DesignComponent right = document.Design.Find("RightPanel")!;

        Assert.True(gestures.Press(new Point2D(-10, -10), DesignButton.Left, DesignModifiers.None));
        gestures.Drag(new Point2D(700, 520), DesignModifiers.None);
        gestures.Release();
        Assert.DoesNotContain(grid, document.Selection);
        Assert.Contains(left, document.Selection);
        Assert.Contains(right, document.Selection);
        Assert.False(document.IsEditable(grid, "RowHeight"));
        Assert.False(document.IsEditable(grid, "ColumnWidth"));

        // A grid in a cell of a grid in the left region, and a button: neither goes onto the figure.
        DesignComponent inLeft = document.Gridify(left);
        DesignComponent nested = Assert.Single(document.Add(Entry("Grid Layout"), inLeft, Centre(document.CellsOf(inLeft)!.Cell(1, 1))));
        Assert.Equal(DesignDocument.GridType, nested.Type);
        Assert.True(document.IsGrid(nested));
        Assert.False(document.IsResponsiveGrid(nested));
        DesignPlacement offGrid = document.PlacementFor([nested], [new Rect2D(700, 20, 60, 60)], new Point2D(720, 40));
        Assert.False(offGrid.CanDrop);
        DesignComponent button = Assert.Single(document.Add(Entry("Button"), right, new Point2D(document.Runtime.InnerOnSurface(right).X + 30, 40)));
        DesignPlacement offButton = document.PlacementFor([button], [new Rect2D(700, 20, 100, 22)], new Point2D(720, 30));
        Assert.False(offButton.CanDrop);
        Assert.Empty(gestures.Drop(Entry("Button"), new Point2D(-40, -40)));
        Assert.Single(document.Design.Figure.Children, static c => c.Type == DesignDocument.GridType);

    }

    /// <summary>
    /// Whether an app is responsive is what its code says, by the properties block App Designer writes
    /// for one alone - the text's counterpart of the file's AppType, which App Designer never infers
    /// from the components - and one answer runs the canvas, the code and the file. A responsive app
    /// whose code was edited out of a template's shape keeps its grid locked, its splitters gone; one
    /// whose block was taken out is standard, and saved into its .mlapp it is standard there too; one
    /// whose layout callback was lost has it put back; a centre column given in pixels is laid out as
    /// App Designer lays its centre region out, taking the rest of the figure.
    /// </summary>
    [Fact]
    public void TheCodeSaysWhetherAnAppIsResponsive()
    {
        // A grid of two rows: responsive still, by its code, the grid locked; nothing to split.
        string twoRows = DesignResponsive.NewAppText(2).Replace(
            "\n            app.GridLayout.RowHeight = {'1x'};", "\n            app.GridLayout.RowHeight = {'1x', '1x'};", StringComparison.Ordinal);
        Assert.NotEqual(DesignResponsive.NewAppText(2), twoRows);
        using (DesignDocument document = DesignDocument.FromText(twoRows, null))
        {
            DesignComponent grid = document.Design.Find("GridLayout")!;
            Assert.True(document.IsResponsive);
            Assert.True(document.IsResponsiveGrid(grid));
            Assert.False(document.IsGrid(grid));
            Assert.False(document.CanDelete(grid));
            Assert.Empty(document.Splitters());
            Assert.False(document.CanGridify(document.Design.Figure));
        }

        // A 3-panel app whose centre panel has gone: the two left are regions, locked; nothing to split.
        string noCentre = string.Join('\n', DesignResponsive.NewAppText(3).Split('\n').Where(static l => !l.Contains("CenterPanel", StringComparison.Ordinal)));
        using (DesignDocument document = DesignDocument.FromText(noCentre, null))
        {
            Assert.True(document.IsResponsive);
            Assert.True(document.IsRegion(document.Design.Find("LeftPanel")!));
            Assert.False(document.CanDelete(document.Design.Find("LeftPanel")!));
            Assert.Empty(document.Splitters());
        }

        // The auto-reflow block taken out: a standard app, its grid edited as any; and in its .mlapp.
        string[] lines = DesignResponsive.NewAppText(2).Split('\n');
        int from = Array.FindIndex(lines, static l => l.Trim() == DesignResponsive.PropertiesComment);
        int to = Array.FindIndex(lines, from, static l => l.Trim() == "end");
        string standardText = string.Join('\n', lines.Where((_, i) => i < from || i > to));
        using (DesignDocument document = DesignDocument.FromText(standardText, null))
        {
            Assert.False(document.IsResponsive);
            Assert.True(document.IsGrid(document.Design.Find("GridLayout")!));
            Assert.True(document.CanDelete(document.Design.Find("LeftPanel")!));
        }

        string folder = Path.Combine(Path.GetTempPath(), "jgraph-ds4b-type-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "TypeApp.mlapp");
            AppDesignerDocument.SaveNew(path, DesignResponsive.NewAppText(2));
            Assert.Contains("<AppType>Responsive</AppType>", AppMetadata(path), StringComparison.Ordinal);
            JgsMlapp.WriteCode(path, JgsMlapp.ReadCode(path) + "\n");
            Assert.Contains("<AppType>Responsive</AppType>", AppMetadata(path), StringComparison.Ordinal);
            JgsMlapp.WriteCode(path, standardText);
            Assert.Contains("<AppType>Standard</AppType>", AppMetadata(path), StringComparison.Ordinal);

            // Its tree has taken the responsive flags off: written again from the same code, nothing moves.
            byte[] model = AppModel(path);
            DesignTreeUpdate again = JgsMlapp.UpdateTree(model, standardText, folder);
            Assert.Empty(again.Problems);
            Assert.Same(model, again.Bytes);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }

        // The figure's binding and the layout callback lost: both put back, as App Designer's are.
        const string BindingLine = "            app.UIFigure.SizeChangedFcn = createCallbackFcn(app, @updateAppLayout, true);\n";
        static string LayoutFunctionOf(string text)
        {
            int start = text.IndexOf("        " + DesignResponsive.CallbackComment, StringComparison.Ordinal);
            int stop = text.IndexOf("\n        end\n", start, StringComparison.Ordinal) + "\n        end\n".Length;
            return text[start..stop];
        }

        static string WithoutCentre(string text) =>
            string.Join('\n', text.Split('\n').Where(static l => !l.Contains("CenterPanel", StringComparison.Ordinal)));

        static string Saved(string text)
        {
            using DesignDocument document = DesignDocument.FromText(text, null);
            return DesignResponsive.Splice(DesignCodeWriter.Splice(document.Text, document.Design, document.Style)!, document.Design, document.Style);
        }

        string full = DesignResponsive.NewAppText(2);
        string layoutFn = LayoutFunctionOf(full);
        string lost = full.Replace(layoutFn, string.Empty, StringComparison.Ordinal).Replace(BindingLine, string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("updateAppLayout", lost, StringComparison.Ordinal);
        Assert.Equal(full, Saved(lost));

        // Found by its blocks, whatever its signature or the space after its end; written as App Designer writes it.
        Assert.Equal(full, Saved(full.Replace("function updateAppLayout(app, event)", "function updateAppLayout(app, ~)", StringComparison.Ordinal)));
        string spaced = full.Replace(layoutFn, layoutFn[..^"        end\n".Length] + "        end \n", StringComparison.Ordinal);
        Assert.Equal(spaced, Saved(spaced));

        // Lost from among the user's callbacks: put back last, where App Designer keeps it.
        string userFn = "        % Button pushed function: Button\n        function ButtonPushed(app, event)\n            disp(1)\n        end\n";
        Assert.Equal(full.Replace(layoutFn, userFn + "\n" + layoutFn, StringComparison.Ordinal), Saved(full.Replace(layoutFn, userFn, StringComparison.Ordinal)));

        // ... after the last callback as its blocks end it, whatever closes the block.
        string blockEnd = "        end\n    end\n\n    % Component initialization";
        string commented = full.Replace(layoutFn, userFn, StringComparison.Ordinal).Replace(blockEnd, "        end\n    end % callbacks\n\n    % Component initialization", StringComparison.Ordinal);
        Assert.Contains("    end % callbacks\n", commented, StringComparison.Ordinal);
        Assert.Equal(
            full.Replace(layoutFn, userFn + "\n" + layoutFn, StringComparison.Ordinal).Replace(blockEnd, "        end\n    end % callbacks\n\n    % Component initialization", StringComparison.Ordinal),
            Saved(commented));

        // A layout callback the class has elsewhere is not made twice.
        string elsewhere = full.Replace("    % Callbacks that handle component events", "    % The app's callbacks", StringComparison.Ordinal);
        Assert.Single(Saved(elsewhere).Split('\n'), static l => l.Contains("function updateAppLayout(", StringComparison.Ordinal));

        // Nor is the figure bound to one no save can put back: here createComponents' comment has gone.
        string unmarked = lost.Replace("    % Component initialization\n", string.Empty, StringComparison.Ordinal);
        Assert.NotEqual(lost, unmarked);
        Assert.DoesNotContain("SizeChangedFcn", Saved(unmarked), StringComparison.Ordinal);

        // Not a template: the binding comes back only to a callback the class still has.
        string three = DesignResponsive.NewAppText(3);
        string threeFn = LayoutFunctionOf(three);
        Assert.DoesNotContain("SizeChangedFcn", Saved(WithoutCentre(three.Replace(threeFn, string.Empty, StringComparison.Ordinal).Replace(BindingLine, string.Empty, StringComparison.Ordinal))), StringComparison.Ordinal);
        Assert.Contains(BindingLine, Saved(WithoutCentre(three.Replace(BindingLine, string.Empty, StringComparison.Ordinal))), StringComparison.Ordinal);

        // A centre column given in pixels that do not fill the figure: laid out taking the rest of it,
        // and a drag out and back is no edit.
        string narrow = DesignResponsive.NewAppText(2).Replace(
            "\n            app.GridLayout.ColumnWidth = {220, '1x'};", "\n            app.GridLayout.ColumnWidth = [220 300];", StringComparison.Ordinal);
        using (DesignDocument document = DesignDocument.FromText(narrow, null))
        {
            var gestures = new DesignGestures(document) { DragThreshold = 4 };
            DesignComponent right = document.Design.Find("RightPanel")!;
            Assert.Equal(new Rect2D(220, 0, 420, 480), document.BoxOf(right)!.Value.Box);
            DesignComponent button = Assert.Single(document.Add(Entry("Button"), right, new Point2D(260, 40)));
            Assert.Null(document.SetProperty(button, "Position", NumericValue.Row(11, 300, 100, 22)));
            Point2D at = Centre(Assert.Single(document.Splitters()).Box);
            Assert.True(gestures.Press(at, DesignButton.Left, DesignModifiers.None));
            gestures.Drag(new Point2D(at.X + 10, at.Y), DesignModifiers.None);
            gestures.Drag(at, DesignModifiers.None);
            gestures.Release();
            Assert.Equal("[11 300 100 22]", DesignValueText.Format(button.Get("Position")!));
            Assert.Equal(new Rect2D(220, 0, 420, 480), document.BoxOf(right)!.Value.Box);
            document.Undo();
            Assert.NotEqual("[11 300 100 22]", DesignValueText.Format(button.Get("Position")!));
        }
    }

    private static string AppMetadata(string path)
    {
        using var package = System.IO.Compression.ZipFile.OpenRead(path);
        using var reader = new StreamReader(package.GetEntry("metadata/appMetadata.xml")!.Open());
        return reader.ReadToEnd();
    }

    private static byte[] AppModel(string path)
    {
        using var package = System.IO.Compression.ZipFile.OpenRead(path);
        using var stream = package.Entries.Single(static e => e.FullName.EndsWith(".mat", StringComparison.Ordinal)).Open();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>
    /// A resize in a region is held 6 pixels inside it after it is snapped (App Designer's
    /// ResizeCalculator): against the alignment hints, from the centre, and for a shape that keeps
    /// its ratio; and a move over a region's edge shows no alignment hints (TemplateComponentMove).
    /// </summary>
    [Fact]
    public void AResizeInARegionIsHeldInsideItAfterSnapping()
    {
        using DesignDocument document = NewApp(2);
        var gestures = new DesignGestures(document) { DragThreshold = 4 };
        Assert.True(gestures.ShowAlignmentHints);
        DesignComponent left = document.Design.Find("LeftPanel")!;
        DesignComponent right = document.Design.Find("RightPanel")!;
        Rect2D region = document.BoxOf(left)!.Value.Box;
        DesignComponent button = Assert.Single(document.Add(Entry("Button"), left, new Point2D(40, 40)));
        Assert.Null(document.SetProperty(button, "Position", NumericValue.Row(21, 400, 100, 22)));
        document.Select([button]);

        void Resize(ResizeEdges edges, double dx, double dy, DesignModifiers modifiers)
        {
            DesignHandle handle = gestures.Handles().Single(h => h.Edges == edges);
            Point2D at = Centre(handle.Box);
            Assert.True(gestures.Press(at, DesignButton.Left, modifiers));
            gestures.Drag(new Point2D(at.X + dx, at.Y + dy), modifiers);
            gestures.Release();
        }

        // East past the region, with the hints on: 6 pixels inside, though the region's edge is 6 away.
        Resize(ResizeEdges.East, 500, 0, DesignModifiers.None);
        Assert.Equal(region.Right - DesignDocument.RegionInset, document.BoxOf(button)!.Value.Box.Right, 6);
        document.Undo();

        // From the centre near the region's left: both sides give back what the left one passes.
        Assert.Null(document.SetProperty(button, "Position", NumericValue.Row(9, 400, 100, 22)));
        Rect2D start = document.BoxOf(button)!.Value.Box;
        Resize(ResizeEdges.East, 60, 0, DesignModifiers.Control);
        Rect2D centred = document.BoxOf(button)!.Value.Box;
        Assert.Equal(region.Left + DesignDocument.RegionInset, centred.Left, 6);
        Assert.Equal(start.Left - centred.Left, centred.Right - start.Right, 6);

        // A knob keeps its ratio: dragged by its corner past the region's bottom, it stops inside.
        // The knob on its own (a labelled component resizes by its sides, open item 150), by its corner.
        DesignComponent knob = document.Add(Entry("Knob"), left, new Point2D(60, 200)).Single(static c => c.Type == "matlab.ui.control.Knob");
        document.SelectPicks([DesignPick.Of(knob)]);
        Rect2D knobStart = document.BoxOf(knob)!.Value.Box;
        Resize(ResizeEdges.South | ResizeEdges.East, 40, 1000, DesignModifiers.None);
        Rect2D knobEnd = document.BoxOf(knob)!.Value.Box;
        Assert.True(knobEnd.Bottom <= region.Bottom - DesignDocument.RegionInset + 1e-9, $"{knobEnd} in {region}");
        Assert.True(knobEnd.Right <= region.Right - DesignDocument.RegionInset + 1e-9, $"{knobEnd} in {region}");
        Assert.Equal(knobStart.Width / knobStart.Height, knobEnd.Width / knobEnd.Height, 1);

        // A move over the region's edge, its top in line with a button in the other region: no hints.
        DesignComponent other = Assert.Single(document.Add(Entry("Button"), right, new Point2D(document.Runtime.InnerOnSurface(right).X + 40, 40)));
        Assert.Null(document.SetProperty(other, "Position", NumericValue.Row(41, 400, 100, 22)));
        Assert.Null(document.SetProperty(button, "Position", NumericValue.Row(21, 400, 100, 22)));
        document.Select([button]);
        Rect2D from = document.BoxOf(button)!.Value.Box;
        Point2D grab = Centre(from);
        Assert.True(gestures.Press(grab, DesignButton.Left, DesignModifiers.None));
        gestures.Drag(new Point2D(grab.X + 5, grab.Y + 1), DesignModifiers.None);
        Assert.NotEmpty(gestures.Guides);
        gestures.Drag(new Point2D(grab.X + (region.Right - from.Right) + 50, grab.Y + 1), DesignModifiers.None);
        Assert.True(gestures.Placement!.OverRegionEdge);
        Assert.Empty(gestures.Guides);
        gestures.Release();
        Assert.Equal(from, document.BoxOf(button)!.Value.Box);
    }

    /// <summary>
    /// A grid whose columns the code gives as numbers (<c>[220 420]</c>, which App Designer's
    /// validator takes) is a 2-panel app whose splitter works as any: the centre takes the rest of the
    /// figure, as App Designer's centre region does, so the splitter stops where it has 12 pixels left;
    /// a press that moves only down is no edit; the columns are written with the centre <c>'1x'</c>.
    /// </summary>
    [Fact]
    public void ColumnsGivenAsNumbersStillSplit()
    {
        string text = DesignResponsive.NewAppText(2).Replace(
            "\n            app.GridLayout.ColumnWidth = {220, '1x'};", "\n            app.GridLayout.ColumnWidth = [220 420];", StringComparison.Ordinal);
        Assert.NotEqual(DesignResponsive.NewAppText(2), text);
        using DesignDocument document = DesignDocument.FromText(text, null);
        Assert.True(document.IsResponsive);
        var gestures = new DesignGestures(document) { DragThreshold = 4 };
        DesignComponent left = document.Design.Find("LeftPanel")!;
        DesignComponent right = document.Design.Find("RightPanel")!;
        Point2D at = Centre(Assert.Single(document.Splitters()).Box);

        Assert.True(gestures.Press(at, DesignButton.Left, DesignModifiers.None));
        gestures.Drag(new Point2D(at.X, at.Y + 40), DesignModifiers.None);
        gestures.Release();
        Assert.False(document.CanUndo);

        Assert.True(gestures.Press(at, DesignButton.Left, DesignModifiers.None));
        gestures.Drag(new Point2D(at.X + 1000, at.Y), DesignModifiers.None);
        gestures.Drag(new Point2D(at.X + 1500, at.Y), DesignModifiers.None);
        Assert.Equal(628, document.BoxOf(left)!.Value.Box.Width);
        Assert.Equal(new Rect2D(628, 0, 12, 480), document.BoxOf(right)!.Value.Box);
        Assert.True(gestures.Splitting!.Value.Resize.Stopped);
        gestures.Cancel();
        Assert.Equal(new Rect2D(220, 0, 420, 480), document.BoxOf(right)!.Value.Box);

        Assert.True(gestures.Press(at, DesignButton.Left, DesignModifiers.None));
        gestures.Drag(new Point2D(at.X + 30, at.Y), DesignModifiers.None);
        gestures.Release();
        Assert.Equal(250, document.BoxOf(left)!.Value.Box.Width);
        Assert.Equal(new Rect2D(250, 0, 390, 480), document.BoxOf(right)!.Value.Box);
        Assert.Equal("{250, '1x'}", DesignValueText.Format(DesignResponsive.Grid(document.Design)!.Get("ColumnWidth")!));
        string saved = DesignResponsive.Splice(DesignCodeWriter.Splice(document.Text, document.Design, document.Style)!, document.Design, document.Style);
        Assert.Contains("app.GridLayout.ColumnWidth = {250, '1x'};", saved, StringComparison.Ordinal);
    }

    /// <summary>
    /// A new 2-panel app built here - components in its regions, the splitter dragged - saves and opens
    /// again the same, its updateAppLayout written for the widths it has. With <c>JGRAPH_DS6_OUT</c> it
    /// is written to <c>new/</c> there, with its createComponents beside it, for R2025b's App Designer
    /// to read (<c>ds6_verify</c>, <c>DS6_PART=new</c>; its MLAPPResponsiveAppValidator included).
    /// </summary>
    [Fact]
    public void ANewResponsiveAppBuiltHereSavesAndOpensTheSame()
    {
        string? outFolder = Environment.GetEnvironmentVariable("JGRAPH_DS6_OUT");
        string temp = Path.Combine(Path.GetTempPath(), "jgraph-ds4b-" + Guid.NewGuid().ToString("N"));
        string folder = Path.Combine(outFolder is null ? temp : Path.Combine(outFolder, "new"), "93_ResponsiveApp");
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "ResponsiveApp.mlapp");
            AppDesignerDocument.SaveNew(path, DesignResponsive.NewAppText(3));
            string[] expected;
            string text;
            using (DesignDocument document = DesignDocument.Open(path))
            {
                Assert.True(document.IsResponsive);
                var gestures = new DesignGestures(document) { DragThreshold = 4 };
                DesignComponent left = document.Design.Find("LeftPanel")!;
                DesignComponent centre = document.Design.Find("CenterPanel")!;
                document.Add(Entry("Button"), left, new Point2D(30, 40));
                document.Add(Entry("Edit Field (Text)"), centre, new Point2D(document.Runtime.InnerOnSurface(centre).X + 20, 60));
                DesignSplitter trailing = document.Splitters().Single(static s => !s.Leading);
                Point2D at = Centre(trailing.Box);
                gestures.Press(at, DesignButton.Left, DesignModifiers.None);
                gestures.Drag(new Point2D(at.X - 40, at.Y), DesignModifiers.None);
                gestures.Release();
                Assert.Empty(document.Runtime.Problems);
                expected = DesignCodeWriter.Method(document.Design, document.Style).ToArray();
                document.Save();
                text = document.Text;
            }

            Assert.Contains("app.GridLayout.ColumnWidth = {220, '1x', 260};", text, StringComparison.Ordinal);
            Assert.Contains("        twoPanelWidth = 768;", text, StringComparison.Ordinal);
            using DesignDocument reopened = DesignDocument.Open(path);
            Assert.True(reopened.IsResponsive);
            Assert.Empty(reopened.ReadProblems);
            Assert.Empty(reopened.Runtime.Problems);
            Assert.Equal(expected, DesignCodeWriter.Method(reopened.Design, reopened.Style));
            if (outFolder is not null)
            {
                File.WriteAllLines(Path.Combine(folder, "ResponsiveApp__code.m"), expected);
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
