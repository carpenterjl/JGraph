using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Rendering.Layout;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Workspace;
using Xunit;
using Xunit.Abstractions;

namespace JGraph.Tests.Scripting;

/// <summary>
/// DS2 of the designer plan: the canvas's model - the runtime's components made from a design in a
/// figure no script reaches, the boxes the canvas selects and moves, App Designer's guides, grid and
/// resize arithmetic, and the edits a gesture makes, undone and saved.
/// </summary>
[Collection("JG facade")]
public class AppDesignerCanvasTests(ITestOutputHelper output)
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Scripting", "Designer", name);

    private static DesignComponent Make(string type, string name, params double[] position)
    {
        var component = new DesignComponent(name, DesignCatalog.R2025b.ByType(type)!);
        if (position.Length == 4)
        {
            component.Set("Position", NumericValue.Row(position));
        }

        return component;
    }

    /// <summary>
    /// A small app laid out by positions: a 640-by-480 figure holding a button, a panel with a label,
    /// a knob, a slider and a button group with two radio buttons.
    /// </summary>
    private static string SmallAppText()
    {
        DesignComponent figure = Make("matlab.ui.Figure", "UIFigure", 100, 100, 640, 480);
        figure.Set("Name", new CharValue("MATLAB App"));
        var design = new AppDesign(figure);
        figure.Add(Make("matlab.ui.control.Button", "Button", 20, 420, 100, 23));
        DesignComponent panel = Make("matlab.ui.container.Panel", "Panel", 200, 200, 300, 200);
        panel.Set("Title", new CharValue("Panel"));
        figure.Add(panel);
        panel.Add(Make("matlab.ui.control.Label", "Label", 20, 120, 80, 22));
        figure.Add(Make("matlab.ui.control.Knob", "Knob", 40, 300, 60, 60));
        figure.Add(Make("matlab.ui.control.Slider", "Slider", 40, 100, 150, 3));
        DesignComponent group = Make("matlab.ui.container.ButtonGroup", "ButtonGroup", 520, 300, 100, 106);
        figure.Add(group);
        group.Add(Make("matlab.ui.control.RadioButton", "Button1", 11, 60, 70, 22));
        group.Add(Make("matlab.ui.control.RadioButton", "Button2", 11, 38, 70, 22));
        return DesignCodeWriter.Splice(AppDesignerDocument.NewAppText(), design)!;
    }

    // --- the runtime ---------------------------------------------------------------------------------

    /// <summary>
    /// The fixture app, which makes every class App Designer offers, is made whole by the runtime: each
    /// component has its object, and the figure has no number, so no script and no search finds it.
    /// Disposing lets go of every handle.
    /// </summary>
    [Fact]
    public void TheRuntimeMakesEveryComponentOfTheFixtureAppInAFigureNoScriptReaches()
    {
        IReadOnlyList<int> before = JG.FigureNumbers;
        AppDesign design = DesignCodeReader.Read(File.ReadAllText(Fixture("DesignerSpecApp.m"))).Design!;
        GraphObject button;
        using (DesignRuntime runtime = DesignRuntime.Build(design, Path.GetDirectoryName(Fixture("DesignerSpecApp.m"))))
        {
            // The fixture's image is not beside it in the test's folder, which the image refuses.
            Assert.All(runtime.Problems, p => Assert.StartsWith("Image.ImageSource:", p, StringComparison.Ordinal));
            Assert.All(design.Components, c => Assert.NotNull(runtime.ObjectOf(c)));
            Assert.All(design.Components, c => Assert.Same(c, runtime.ComponentOf(runtime.ObjectOf(c)!)));
            Assert.Equal(before, JG.FigureNumbers);
            Assert.Equal(0, JG.GetFigureNumber(runtime.Figure));
            button = runtime.ObjectOf(design.Find("Button")!)!;
            Assert.True(JgsHandleRegistry.TryGetEntry(button, out _));
        }

        Assert.False(JgsHandleRegistry.TryGetEntry(button, out _));
    }

    /// <summary>
    /// The canvas's boxes are where the window draws the components (the shared layout's, a
    /// scrollable tab's content from its top among them), and a component's Position turned into a
    /// box on the surface - what a move or resize writes back through - lands on the box drawn,
    /// for every component placed by its position, the fixture's on both tabs.
    /// </summary>
    [Fact]
    public void APositionOnTheSurfaceIsWhereTheComponentIsDrawn()
    {
        AppDesign design = DesignCodeReader.Read(File.ReadAllText(Fixture("DesignerSpecApp.m"))).Design!;
        using DesignRuntime runtime = DesignRuntime.Build(design, Path.GetDirectoryName(Fixture("DesignerSpecApp.m")));
        int compared = 0;
        foreach (string tab in (string[])["ControlsTab", "GaugesTab"])
        {
            runtime.ShowTab(design.Find(tab)!);
            foreach (DesignBox box in runtime.Boxes().Where(static b => b.Shown))
            {
                DesignComponent c = box.Component;
                if (c.IsInGrid || c.Type == "matlab.ui.container.Tab" || runtime.PositionOf(c) is not { } position)
                {
                    continue;
                }

                Rect2D at = runtime.ToSurface(c.Parent!, position);
                Assert.True(
                    System.Math.Abs(at.X - box.Box.X) < 1e-6 && System.Math.Abs(at.Y - box.Box.Y) < 1e-6
                    && System.Math.Abs(at.Width - box.Box.Width) < 1e-6 && System.Math.Abs(at.Height - box.Box.Height) < 1e-6,
                    $"{c.CodeName}: its Position {position} is {at} on the surface, drawn at {box.Box}");
                Assert.Equal(position, runtime.ToPosition(c.Parent!, box.Box));
                compared++;
            }
        }

        Assert.True(compared >= 30, $"{compared} boxes compared");
    }

    /// <summary>A component on a tab that is not showing has no box to click until its tab is shown.</summary>
    [Fact]
    public void ATabsComponentsShowWhenItsTabIsShown()
    {
        AppDesign design = DesignCodeReader.Read(File.ReadAllText(Fixture("DesignerSpecApp.m"))).Design!;
        using DesignRuntime runtime = DesignRuntime.Build(design, Path.GetDirectoryName(Fixture("DesignerSpecApp.m")));
        DesignComponent knob = design.Find("Knob")!;
        Assert.False(runtime.Boxes().Single(b => b.Component == knob).Shown);
        runtime.ShowTab(design.Find("GaugesTab")!);
        Assert.True(runtime.Boxes().Single(b => b.Component == knob).Shown);
        Assert.False(runtime.Boxes().Single(b => b.Component == design.Find("Button")).Shown);
    }

    /// <summary>
    /// A hidden component is made visible on the canvas, as App Designer's canvas shows it, and its box
    /// says it is hidden so that the canvas fades it.
    /// </summary>
    [Fact]
    public void AHiddenComponentIsShownAndMarkedHidden()
    {
        DesignDocument document = DesignDocument.FromText(SmallAppText(), null);
        DesignComponent button = document.Design.Find("Button")!;
        button.Set("Visible", OnOffValue.Off);
        using DesignRuntime runtime = DesignRuntime.Build(document.Design, null);
        Assert.True(((UiObject)runtime.ObjectOf(button)!).Visible);
        Assert.True(runtime.Boxes().Single(b => b.Component == button).Hidden);
        document.Dispose();
    }

    // --- the guides, the grid and resizing ----------------------------------------------------------------

    /// <summary>
    /// A moving box's left edge 7 pixels from another's left edge snaps to it, 8 does not; the guide
    /// runs 20 pixels beyond both boxes.
    /// </summary>
    [Fact]
    public void AnEdgeWithinSevenPixelsSnapsAndItsGuideRunsPastBothBoxes()
    {
        var other = new Rect2D(100, 100, 80, 22);
        DesignSnap near = DesignGeometry.Snap(new Rect2D(107, 200, 30, 22), [other]);
        Assert.Equal(-7, near.DX);
        DesignGuide guide = Assert.Single(near.Guides, static g => g.Vertical);
        Assert.Equal(100, guide.Position);
        Assert.Equal(100 - 20, guide.From);
        Assert.Equal(222 + 20, guide.To);

        Assert.Equal(0, DesignGeometry.Snap(new Rect2D(108, 200, 30, 22), [other]).DX);
    }

    /// <summary>Centres snap to centres, and right edges to right edges (a pixel inside the box, as App Designer counts them).</summary>
    [Fact]
    public void CentresAndRightEdgesSnapToo()
    {
        var other = new Rect2D(100, 100, 100, 50); // centre x 149, right edge 199
        Assert.Equal(149 - 154, DesignGeometry.Snap(new Rect2D(130, 300, 50, 20), [other]).DX); // centre 154
        Assert.Equal(199 - 196, DesignGeometry.Snap(new Rect2D(147, 300, 50, 20), [other]).DX); // right 196, centre 171
    }

    /// <summary>Two small boxes of nearly one size align by their centres only, not by edges a pixel or two apart.</summary>
    [Fact]
    public void SmallBoxesOfNearlyOneSizeAlignByCentresOnly()
    {
        var lamp = new Rect2D(100, 100, 20, 20);
        DesignSnap snap = DesignGeometry.Snap(new Rect2D(103, 200, 22, 22), [lamp]);
        Assert.All(snap.Guides.Where(static g => g.Vertical), g => Assert.Equal(109, g.Position)); // the lamp's centre
    }

    /// <summary>A resize aligns only the edges it moves, and marks an edge it keeps that is aligned already.</summary>
    [Fact]
    public void AResizeAlignsTheEdgesItMoves()
    {
        var other = new Rect2D(100, 100, 100, 50);
        DesignSnap snap = DesignGeometry.Snap(new Rect2D(100, 300, 97, 20), [other], ResizeEdges.East);
        Assert.Equal(3, snap.DX); // its right edge, 196, to the other's, 199
        Assert.Contains(snap.Guides, static g => g.Vertical && g.Fixed && g.Position == 100);
        Assert.Equal(0, DesignGeometry.Snap(new Rect2D(104, 300, 50, 20), [other], ResizeEdges.East).DX);
    }

    /// <summary>
    /// A resize keeps the least size, a shape's ratio with its far corner where it was, a slider's
    /// height, and grows about the centre with Ctrl; its moving edges go to the grid.
    /// </summary>
    [Fact]
    public void AResizeKeepsTheShapesRules()
    {
        var start = new Rect2D(100, 100, 60, 60);
        Assert.Equal(new Rect2D(100, 100, 10, 10), DesignGeometry.Resize(start, ResizeEdges.SouthEast, -100, -100, DesignShape.Free));
        Assert.Equal(new Rect2D(70, 70, 90, 90), DesignGeometry.Resize(start, ResizeEdges.NorthWest, -30, -10, new DesignShape(1, false, false, new Size2D(10, 10))));
        Assert.Equal(new Rect2D(100, 100, 90, 3), DesignGeometry.Resize(new Rect2D(100, 100, 60, 3), ResizeEdges.SouthEast, 30, 40, new DesignShape(null, false, true, new Size2D(10, 10))));
        Assert.Equal(new Rect2D(90, 100, 80, 60), DesignGeometry.Resize(start, ResizeEdges.East, 10, 0, DesignShape.Free, fromCentre: true));
        Assert.Equal(new Rect2D(100, 100, 70, 60), DesignGeometry.Resize(start, ResizeEdges.East, 13, 0, DesignShape.Free, grid: 10));
        Assert.Equal(new Rect2D(100, 100, 120, 120), DesignGeometry.Resize(start, ResizeEdges.East, 60, 0, DesignShape.Free, keepAspect: true));
    }

    /// <summary>
    /// A rubber band selects what it holds whole, in the container of the box nearest where it began;
    /// a container it holds is selected rather than what is in it.
    /// </summary>
    [Fact]
    public void ARubberBandSelectsWholeBoxesInOneContainer()
    {
        DesignDocument document = DesignDocument.FromText(SmallAppText(), null);
        IReadOnlyList<DesignBox> boxes = document.Boxes;
        DesignComponent[] AtBand(Rect2D band, Point2D start) => [.. DesignGeometry.RubberBand(band, start, boxes)];

        Assert.Equal(["Button", "Knob"], AtBand(new Rect2D(0, 0, 200, 200), new Point2D(0, 0)).Select(static c => c.CodeName));
        Assert.Equal(["Panel"], AtBand(new Rect2D(150, 50, 400, 300), new Point2D(150, 50)).Select(static c => c.CodeName));
        Assert.Equal(["Button1", "Button2"], AtBand(new Rect2D(520, 90, 100, 60), new Point2D(520, 90)).Select(static c => c.CodeName));
        document.Dispose();
    }

    /// <summary>Ctrl or Shift adds a component of the same container, takes a selected one out, and selects one elsewhere alone.</summary>
    [Fact]
    public void AClickWithCtrlAddsComponentsOfOneContainerOnly()
    {
        DesignDocument document = DesignDocument.FromText(SmallAppText(), null);
        AppDesign d = document.Design;
        DesignComponent button = d.Find("Button")!, knob = d.Find("Knob")!, label = d.Find("Label")!;
        Assert.Equal([button, knob], DesignGeometry.Click([button], knob, control: true, shift: false));
        Assert.Equal([label], DesignGeometry.Click([button, knob], label, control: true, shift: false));
        Assert.Equal([knob], DesignGeometry.Click([button, knob], button, control: false, shift: true));
        Assert.Equal([label], DesignGeometry.Click([button, knob], label, control: false, shift: false));
        document.Dispose();
    }

    // --- the document --------------------------------------------------------------------------------------

    /// <summary>
    /// A move is one edit: the component's Position moves by the drag (down the screen is down in
    /// MATLAB's pixels), undo puts it back and redo again; the runtime's component follows.
    /// </summary>
    [Fact]
    public void AMoveIsOneEditThatUndoesAndRedoes()
    {
        using DesignDocument document = DesignDocument.FromText(SmallAppText(), null);
        DesignComponent button = document.Design.Find("Button")!;
        Assert.False(document.IsDirty);

        document.Move([button], 5, 3);
        Assert.Equal([25, 417, 100, 23], Numbers(button));
        Assert.Equal([25, 417, 100, 23], document.Runtime.ReadNumbers(button, "Position")!);
        Assert.True(document.IsDirty);

        document.Undo();
        Assert.Equal([20, 420, 100, 23], Numbers(button));
        Assert.Equal([20, 420, 100, 23], document.Runtime.ReadNumbers(button, "Position")!);
        Assert.False(document.IsDirty);

        document.Redo();
        Assert.Equal([25, 417, 100, 23], Numbers(button));
    }

    /// <summary>
    /// A drop inside a panel moves the component into it, last among its children, its Position now
    /// in the panel; undo puts it back where it was in the figure, in its old place among its siblings.
    /// </summary>
    [Fact]
    public void ADropInAPanelMovesTheComponentIntoIt()
    {
        using DesignDocument document = DesignDocument.FromText(SmallAppText(), null);
        AppDesign d = document.Design;
        DesignComponent button = d.Find("Button")!;
        DesignComponent panel = d.Find("Panel")!;
        Rect2D inner = document.Runtime.InnerOnSurface(panel);
        var box = new Rect2D(inner.X + 10, inner.Y + 10, 100, 23);

        document.BeginGesture([button]);
        Assert.Same(panel, document.DropTarget([button], box));
        document.Preview(new Dictionary<DesignComponent, Rect2D>(ReferenceEqualityComparer.Instance) { [button] = box });
        document.EndGesture(commit: true, panel);

        Assert.Same(panel, button.Parent);
        Assert.Same(button, panel.Children[^1]);
        Assert.Equal(11, Numbers(button)[0]);
        Assert.Equal(inner.Height - 10 - 23 + 1, Numbers(button)[1]);
        Assert.Same(document.Runtime.ObjectOf(panel), JgsGraphicsProperties.ParentOf(document.Runtime.ObjectOf(button)!));

        document.Undo();
        Assert.Same(d.Figure, button.Parent);
        Assert.Same(button, d.Figure.Children[0]);
        Assert.Equal([20, 420, 100, 23], Numbers(button));
    }

    /// <summary>A radio button cannot be dropped out of its button group, nor a panel into itself.</summary>
    [Fact]
    public void ARadioButtonStaysInAButtonGroup()
    {
        using DesignDocument document = DesignDocument.FromText(SmallAppText(), null);
        AppDesign d = document.Design;
        DesignComponent radio = d.Find("Button1")!;
        Assert.False(document.CanHold(d.Figure, radio));
        Assert.False(document.CanHold(d.Find("Panel")!, radio));
        Assert.Same(d.Find("ButtonGroup"), document.DropTarget([radio], new Rect2D(10, 10, 70, 22)));
        Assert.False(document.CanHold(d.Find("Panel")!, d.Find("Panel")!));
    }

    /// <summary>A knob resized out of square comes back square, as the runtime keeps it, and the design holds what it kept.</summary>
    [Fact]
    public void AResizeHoldsWhatTheComponentKeeps()
    {
        using DesignDocument document = DesignDocument.FromText(SmallAppText(), null);
        DesignComponent knob = document.Design.Find("Knob")!;
        Assert.Equal(1, document.ShapeOf(knob).Ratio);
        Rect2D start = document.BoxOf(knob)!.Value.Box;
        document.BeginGesture([knob]);
        document.Preview(new Dictionary<DesignComponent, Rect2D>(ReferenceEqualityComparer.Instance)
        {
            [knob] = new Rect2D(start.X, start.Y, 100, 80),
        });
        document.EndGesture(commit: true);
        double[] kept = Numbers(knob);
        Assert.Equal(kept[2], kept[3]);
        Assert.Equal(kept, document.Runtime.ReadNumbers(knob, "Position")!);
    }

    /// <summary>A cancelled gesture leaves the design and the component as they were, and is no edit.</summary>
    [Fact]
    public void ACancelledGestureLeavesEverythingAsItWas()
    {
        using DesignDocument document = DesignDocument.FromText(SmallAppText(), null);
        DesignComponent button = document.Design.Find("Button")!;
        document.BeginGesture([button]);
        document.Preview(new Dictionary<DesignComponent, Rect2D>(ReferenceEqualityComparer.Instance) { [button] = new Rect2D(300, 300, 100, 23) });
        Assert.NotEqual([20, 420, 100, 23], document.Runtime.ReadNumbers(button, "Position")!);
        document.EndGesture(commit: false);
        Assert.Equal([20, 420, 100, 23], document.Runtime.ReadNumbers(button, "Position")!);
        Assert.Equal([20, 420, 100, 23], Numbers(button));
        Assert.False(document.CanUndo);
    }

    /// <summary>
    /// Writing an unchanged design back into code App Designer laid out gives the text unchanged, line
    /// endings and all, so a save writes only what the edits changed.
    /// </summary>
    [Fact]
    public void SplicingAnUnchangedDesignGivesTheTextBack()
    {
        // The fixture sets its properties out of the generator's order on purpose, so it is written
        // once in App Designer's own form, which is the form an App Designer app's code is in.
        AppDesign fixture = DesignCodeReader.Read(File.ReadAllText(Fixture("DesignerSpecApp.m"))).Design!;
        string text = DesignCodeWriter.Splice(AppDesignerDocument.NewAppText(), fixture)!;
        Assert.Equal(text, DesignCodeWriter.Splice(text, DesignCodeReader.Read(text).Design!));
        string crlf = text.Replace("\n", "\r\n", StringComparison.Ordinal);
        Assert.Equal(crlf, DesignCodeWriter.Splice(crlf, DesignCodeReader.Read(crlf).Design!));
        string small = SmallAppText();
        Assert.Equal(small, DesignCodeWriter.Splice(small, DesignCodeReader.Read(small).Design!));
    }

    /// <summary>
    /// A save writes the moved component's new Position into the app, which opens again with it there;
    /// a document read with lines the designer cannot place refuses to save.
    /// </summary>
    [Fact]
    public void ASaveWritesTheEditsIntoTheApp()
    {
        string folder = Path.Combine(Path.GetTempPath(), "jgraph-ds2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "SmallApp.mlapp");
            AppDesignerDocument.SaveNew(path, SmallAppText());
            using (DesignDocument document = DesignDocument.Open(path))
            {
                document.Move([document.Design.Find("Button")!], 10, 0);
                Assert.Contains("Saved", document.Save(), StringComparison.Ordinal);
                Assert.False(document.IsDirty);
            }

            using (DesignDocument reopened = DesignDocument.Open(path))
            {
                Assert.Equal([30, 420, 100, 23], Numbers(reopened.Design.Find("Button")!));
            }

            string broken = SmallAppText().Replace("% Create Button", "x = 1;\n            % Create Button", StringComparison.Ordinal);
            using DesignDocument partial = DesignDocument.FromText(broken, path);
            Assert.False(partial.CanSave);
            Assert.Throws<InvalidOperationException>(() => partial.Save());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // --- the gestures ---------------------------------------------------------------------------------
    // The small app's 640-by-480 figure has on its surface the button at (19, 38, 100, 23), the knob at
    // (39, 121, 60, 60), the panel at (199, 81, 300, 200) and the button group at (519, 75, 100, 106).

    private static (DesignDocument Document, DesignGestures Gestures) Gestures(bool hints = false)
    {
        DesignDocument document = DesignDocument.FromText(SmallAppText(), null);
        return (document, new DesignGestures(document) { ShowAlignmentHints = hints });
    }

    /// <summary>A press and a drag on a component move it by the drag, as one edit; down the screen is down in MATLAB's pixels.</summary>
    [Fact]
    public void ADragMovesTheComponentPressed()
    {
        (DesignDocument document, DesignGestures gestures) = Gestures();
        using (document)
        {
            DesignComponent button = document.Design.Find("Button")!;
            Assert.True(gestures.Press(new Point2D(60, 50), DesignButton.Left, DesignModifiers.None));
            Assert.Equal([button], document.Selection);
            gestures.Drag(new Point2D(75, 60), DesignModifiers.None);
            gestures.Drag(new Point2D(90, 70), DesignModifiers.None);
            Assert.Equal([20, 420, 100, 23], Numbers(button)); // nothing in the design until the release
            gestures.Release();
            Assert.Equal([50, 400, 100, 23], Numbers(button));
            document.Undo();
            Assert.Equal([20, 420, 100, 23], Numbers(button));
            Assert.False(document.CanUndo);
        }
    }

    /// <summary>With the guides on, a drag that brings the button's left edge within 7 pixels of the knob's snaps it there.</summary>
    [Fact]
    public void ADragSnapsToAnotherComponentsEdge()
    {
        (DesignDocument document, DesignGestures gestures) = Gestures(hints: true);
        using (document)
        {
            DesignComponent button = document.Design.Find("Button")!;
            gestures.Press(new Point2D(60, 50), DesignButton.Left, DesignModifiers.None);
            gestures.Drag(new Point2D(83, 50), DesignModifiers.None);
            Assert.Contains(gestures.Guides, static g => g.Vertical && g.Position == 39);
            gestures.Release();
            Assert.Empty(gestures.Guides);
            Assert.Equal([40, 420, 100, 23], Numbers(button));
        }
    }

    /// <summary>With the grid shown, a drag puts the component's corner on the grid of its container.</summary>
    [Fact]
    public void ADragSnapsToTheGridWhenItIsShown()
    {
        (DesignDocument document, DesignGestures gestures) = Gestures(hints: true);
        using (document)
        {
            gestures.ShowGrid = true;
            DesignComponent button = document.Design.Find("Button")!;
            gestures.Press(new Point2D(60, 50), DesignButton.Left, DesignModifiers.None);
            gestures.Drag(new Point2D(73, 54), DesignModifiers.None);
            gestures.Release();
            Assert.Equal([31, 418, 100, 23], Numbers(button)); // its corner at (30, 40) on the surface
        }
    }

    /// <summary>A drag on a selected component's corner handle resizes it from that corner.</summary>
    [Fact]
    public void ADragOnAHandleResizes()
    {
        (DesignDocument document, DesignGestures gestures) = Gestures();
        using (document)
        {
            DesignComponent button = document.Design.Find("Button")!;
            document.Select([button]);
            Assert.Equal(8, gestures.Handles().Count());
            Assert.Equal(DesignCursor.SizeNorthWestSouthEast, gestures.CursorAt(new Point2D(119, 61)));
            gestures.Press(new Point2D(119, 61), DesignButton.Left, DesignModifiers.None);
            gestures.Drag(new Point2D(139, 71), DesignModifiers.None);
            gestures.Release();
            Assert.Equal([20, 410, 120, 33], Numbers(button));

            // A slider has no handle that would change its height alone.
            document.Select([document.Design.Find("Slider")!]);
            Assert.Equal(2, gestures.Handles().Count());
        }
    }

    /// <summary>A drag from the figure's empty space draws a band that selects what it holds whole.</summary>
    [Fact]
    public void ADragFromTheFigureDrawsARubberBand()
    {
        (DesignDocument document, DesignGestures gestures) = Gestures();
        using (document)
        {
            Assert.True(gestures.Press(new Point2D(5, 5), DesignButton.Left, DesignModifiers.None));
            gestures.Drag(new Point2D(130, 200), DesignModifiers.None);
            Assert.NotNull(gestures.Band);
            Assert.Equal(["Button", "Knob"], gestures.Banded.Select(static c => c.CodeName));
            gestures.Release();
            Assert.Null(gestures.Band);
            Assert.Equal(["Button", "Knob"], document.Selection.Select(static c => c.CodeName));
            Assert.False(document.CanUndo);

            // A click there selects the figure.
            gestures.Press(new Point2D(5, 5), DesignButton.Left, DesignModifiers.None);
            gestures.Release();
            Assert.Equal([document.Design.Figure], document.Selection);
        }
    }

    /// <summary>
    /// Ctrl adds a component of the same container. A press on one of several selected keeps them for
    /// a drag, which moves them all, and selects it alone when no drag follows.
    /// </summary>
    [Fact]
    public void APressOnOneOfSeveralSelectedMovesThemAllOrSelectsItAlone()
    {
        (DesignDocument document, DesignGestures gestures) = Gestures();
        using (document)
        {
            DesignComponent button = document.Design.Find("Button")!, knob = document.Design.Find("Knob")!;
            gestures.Press(new Point2D(60, 50), DesignButton.Left, DesignModifiers.None);
            gestures.Release();
            gestures.Press(new Point2D(60, 140), DesignButton.Left, DesignModifiers.Control);
            gestures.Release();
            Assert.Equal([button, knob], document.Selection);

            gestures.Press(new Point2D(60, 50), DesignButton.Left, DesignModifiers.None);
            gestures.Drag(new Point2D(90, 50), DesignModifiers.None);
            gestures.Release();
            Assert.Equal([50, 420, 100, 23], Numbers(button));
            Assert.Equal([70, 300, 60, 60], Numbers(knob));
            Assert.Equal([button, knob], document.Selection);

            gestures.Press(new Point2D(90, 50), DesignButton.Left, DesignModifiers.None);
            gestures.Release();
            Assert.Equal([button], document.Selection);
        }
    }

    /// <summary>A drag released inside the panel moves the component into it.</summary>
    [Fact]
    public void ADragIntoAPanelMovesTheComponentIntoIt()
    {
        (DesignDocument document, DesignGestures gestures) = Gestures();
        using (document)
        {
            DesignComponent button = document.Design.Find("Button")!;
            gestures.Press(new Point2D(60, 50), DesignButton.Left, DesignModifiers.None);
            gestures.Drag(new Point2D(310, 150), DesignModifiers.None);
            Assert.Same(document.Design.Find("Panel"), gestures.DropTarget);
            gestures.Release();
            Assert.Same(document.Design.Find("Panel"), button.Parent);
            Assert.Null(gestures.DropTarget);
        }
    }

    /// <summary>The arrow keys move the selection a pixel, ten with Shift, and a run of them is one edit.</summary>
    [Fact]
    public void ARunOfArrowKeysIsOneEdit()
    {
        (DesignDocument document, DesignGestures gestures) = Gestures();
        using (document)
        {
            DesignComponent button = document.Design.Find("Button")!;
            document.Select([button]);
            Assert.True(gestures.KeyDown(DesignKey.Right, DesignModifiers.None));
            Assert.True(gestures.KeyDown(DesignKey.Right, DesignModifiers.None));
            Assert.True(gestures.KeyDown(DesignKey.Right, DesignModifiers.None));
            Assert.True(gestures.KeyDown(DesignKey.Down, DesignModifiers.Shift));
            gestures.ArrowUp(arrowHeld: true);
            Assert.False(document.CanUndo);
            gestures.ArrowUp(arrowHeld: false);
            Assert.Equal([23, 410, 100, 23], Numbers(button));
            document.Undo();
            Assert.Equal([20, 420, 100, 23], Numbers(button));
            Assert.False(document.CanUndo);

            // The figure does not move.
            document.Select([]);
            Assert.False(gestures.KeyDown(DesignKey.Left, DesignModifiers.None));
        }
    }

    /// <summary>Escape during a drag puts the component back, and no edit is made.</summary>
    [Fact]
    public void EscapeCancelsADrag()
    {
        (DesignDocument document, DesignGestures gestures) = Gestures();
        using (document)
        {
            DesignComponent button = document.Design.Find("Button")!;
            gestures.Press(new Point2D(60, 50), DesignButton.Left, DesignModifiers.None);
            gestures.Drag(new Point2D(160, 50), DesignModifiers.None);
            Assert.Equal([120, 420, 100, 23], document.Runtime.ReadNumbers(button, "Position")!);
            Assert.True(gestures.KeyDown(DesignKey.Escape, DesignModifiers.None));
            Assert.False(gestures.IsPressed);
            Assert.Equal([20, 420, 100, 23], document.Runtime.ReadNumbers(button, "Position")!);
            gestures.Release();
            Assert.False(document.CanUndo);
        }
    }

    /// <summary>A press on a tab's heading shows the tab and selects it.</summary>
    [Fact]
    public void APressOnATabsHeadingShowsTheTab()
    {
        AppDesign design = DesignCodeReader.Read(File.ReadAllText(Fixture("DesignerSpecApp.m"))).Design!;
        string text = DesignCodeWriter.Splice(AppDesignerDocument.NewAppText(), design)!;
        using DesignDocument document = DesignDocument.FromText(text, null);
        DesignComponent gauges = document.Design.Find("GaugesTab")!;
        var gestures = new DesignGestures(document) { TabHeadingAt = at => at.X < 0 ? gauges : null };
        Assert.False(document.Runtime.Boxes().Single(b => b.Component == document.Design.Find("Knob")).Shown);
        Assert.False(gestures.Press(new Point2D(-1, 0), DesignButton.Left, DesignModifiers.None));
        Assert.Equal([gauges], document.Selection);
        Assert.True(document.Boxes.Single(b => b.Component == document.Design.Find("Knob")).Shown);
    }

    // --- every app R2025b ships ----------------------------------------------------------------------

    private const string ShippedRoot = @"C:\Program Files\MATLAB\R2025b\toolbox";

    /// <summary>
    /// Where R2025b is installed, each <c>.mlapp</c> it ships opens on the canvas (read in place, never
    /// saved): its components are made, each placed by its position lands where it is drawn, and a
    /// move of the first such one undoes back to the design and the component it started from. What
    /// the runtime refused is listed, not failed: it is a runtime gap, not the canvas's.
    /// </summary>
    [Fact]
    public void EveryShippedAppOpensOnTheCanvas()
    {
        if (!Directory.Exists(ShippedRoot))
        {
            return;
        }

        var failures = new List<string>();
        var refused = new List<string>();
        int apps = 0;
        int placed = 0;
        foreach (string path in Directory.EnumerateFiles(ShippedRoot, "*.mlapp", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string key = Path.GetRelativePath(ShippedRoot, path).Replace('\\', '/');
            DesignDocument document;
            try
            {
                document = DesignDocument.FromText(JgsMlapp.ReadCode(path), path);
            }
            catch (InvalidDataException ex)
            {
                failures.Add($"{key}: {ex.Message}");
                continue;
            }

            using (document)
            {
                apps++;
                refused.AddRange(document.Runtime.Problems.Select(p => $"{key}: {p}"));
                DesignComponent? first = null;
                foreach (DesignBox box in document.Boxes.Where(static b => b.Shown))
                {
                    DesignComponent c = box.Component;
                    if (!document.IsMovable(c) || document.Runtime.PositionOf(c) is not { } position)
                    {
                        continue;
                    }

                    first ??= c;
                    placed++;
                    Rect2D at = document.Runtime.ToSurface(c.Parent!, position);
                    if (System.Math.Abs(at.X - box.Box.X) > 1e-6 || System.Math.Abs(at.Y - box.Box.Y) > 1e-6
                        || System.Math.Abs(at.Width - box.Box.Width) > 1e-6 || System.Math.Abs(at.Height - box.Box.Height) > 1e-6)
                    {
                        failures.Add($"{key}: {c.CodeName} at {at} from its Position, drawn at {box.Box}");
                    }
                }

                if (first is not null)
                {
                    double[] before = document.Runtime.ReadNumbers(first, "Position")!;
                    DesignValue? held = first.Get("Position");
                    document.Move([first], 7, 5);
                    document.Undo();
                    if (!before.SequenceEqual(document.Runtime.ReadNumbers(first, "Position")!) || !ReferenceEquals(held, first.Get("Position")))
                    {
                        failures.Add($"{key}: moving {first.CodeName} and undoing did not give it back");
                    }
                }
            }
        }

        output.WriteLine($"{apps} apps opened, {placed} components placed by position, {refused.Count} lines refused by the runtime");
        foreach (string line in refused)
        {
            output.WriteLine(line);
        }

        Assert.True(apps > 40, $"{apps} apps opened");
        Assert.True(placed > 400, $"{placed} components placed");
        Assert.True(failures.Count == 0, string.Join("\n", failures) + "\n--- refused by the runtime ---\n" + string.Join("\n", refused));
    }

    private static double[] Numbers(DesignComponent component) => [.. ((NumericValue)component.Get("Position")!).Data];
}
