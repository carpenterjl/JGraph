using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Workspace;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The App Designer window plan's stage W4 on an app: the anchor, Align, Same Size and Space as
/// edits of the selection, Reorder, Apply Auto Tab Order and the tab order shown, Add Label, the
/// font keys, the figure resized by its edges, a library entry dropped with Ctrl held, and the
/// canvas menu's Layout items. The arithmetic is checked against R2025b's own modules by
/// <see cref="Designer.DesignArrangeOracleTests"/>.
/// </summary>
[Collection("JG facade")]
public class AppDesignerArrangeTests
{
    private static DesignPaletteEntry Entry(string name) => DesignPalette.R2025b.Find(name) ?? throw new InvalidOperationException(name);

    private static Point2D Centre(Rect2D r) => new(r.X + (r.Width / 2), r.Y + (r.Height / 2));

    private static DesignComponent Make(string type, string name, params double[] position)
    {
        var component = new DesignComponent(name, DesignCatalog.R2025b.ByType(type)!);
        if (position.Length == 4)
        {
            component.Set("Position", NumericValue.Row(position));
        }

        return component;
    }

    /// <summary>A 640-by-480 figure: three buttons, a slider, a panel with a button, an edit field with no label and a knob.</summary>
    private static DesignDocument App()
    {
        DesignComponent figure = Make("matlab.ui.Figure", "UIFigure", 100, 100, 640, 480);
        figure.Set("Name", new CharValue("MATLAB App"));
        var design = new AppDesign(figure);
        figure.Add(Make("matlab.ui.control.Button", "Button", 20, 400, 100, 23));
        figure.Add(Make("matlab.ui.control.Button", "Button2", 150, 380, 80, 30));
        figure.Add(Make("matlab.ui.control.Button", "Button3", 300, 350, 120, 22));
        figure.Add(Make("matlab.ui.control.Slider", "Slider", 20, 200, 150, 3));
        DesignComponent panel = Make("matlab.ui.container.Panel", "Panel", 350, 50, 250, 200);
        figure.Add(panel);
        panel.Add(Make("matlab.ui.control.Button", "Inner", 20, 20, 100, 23));
        figure.Add(Make("matlab.ui.control.EditField", "EditField", 100, 100, 100, 22));
        figure.Add(Make("matlab.ui.control.Knob", "Knob", 480, 330, 60, 60));
        return DesignDocument.FromText(DesignCodeWriter.Splice(AppDesignerDocument.NewAppText(), design)!, null);
    }

    private static DesignComponent C(DesignDocument document, string name) => document.Design.Find(name)!;

    private static Rect2D P(DesignDocument document, string name) => document.Runtime.PositionOf(C(document, name))!.Value;

    private static void Select(DesignDocument document, params string[] names) =>
        document.SelectPicks(names.Select(n => DesignPick.Of(C(document, n))).ToArray());

    private static string[] Children(DesignComponent container) => container.Children.Select(static c => c.CodeName).ToArray();

    // --- the anchor --------------------------------------------------------------------------------

    /// <summary>The last selected of two or more is the anchor; a right click on another selected one makes it the anchor, outlined 2 pixels wide.</summary>
    [Fact]
    public void TheAnchorIsTheLastSelected_OrTheOneRightClicked()
    {
        using DesignDocument document = App();
        Select(document, "Button");
        Assert.Null(document.Anchor);
        Select(document, "Button", "Button3", "Button2");
        Assert.Equal("Button2", document.Anchor!.ToString());

        var gestures = new DesignGestures(document);
        gestures.Press(Centre(document.BoxOf(C(document, "Button"))!.Value.Box), DesignButton.Right, DesignModifiers.None);
        gestures.Release();
        Assert.Equal(["Button", "Button3", "Button2"], document.Picks.Select(static p => p.ToString()));
        Assert.Equal("Button", document.Anchor!.ToString());

        // A new selection drops it.
        Select(document, "Button3", "Button");
        Assert.Equal("Button", document.Anchor!.ToString());
        Select(document, "Button", "Button3");
        Assert.Equal("Button3", document.Anchor!.ToString());
    }

    // --- align ---------------------------------------------------------------------------------------

    [Fact]
    public void AlignMovesTheSelectionOntoTheAnchor_AsOneEdit()
    {
        using DesignDocument document = App();
        Select(document, "Button", "Button3", "Button2");
        Assert.True(document.CanAlign);

        Assert.True(document.Align(DesignAlign.Left));
        Assert.Equal(150, P(document, "Button").X);
        Assert.Equal(150, P(document, "Button3").X);
        Assert.Equal(150, P(document, "Button2").X);
        Assert.Equal(NumericValue.Row(150, 400, 100, 23).Data, ((NumericValue)C(document, "Button").Get("Position")!).Data);

        // The selection and its anchor stay, through the undo as well.
        Assert.Equal(["Button", "Button3", "Button2"], document.Picks.Select(static p => p.ToString()));
        Assert.Equal("Button2", document.Anchor!.ToString());
        document.Undo();
        Assert.Equal(20, P(document, "Button").X);
        Assert.Equal(300, P(document, "Button3").X);
        Assert.Equal("Button2", document.Anchor!.ToString());

        // Middles round as JavaScript's Math.round does: 395 - 411.5 is -16.5, which goes to -16.
        Assert.True(document.Align(DesignAlign.Middle));
        Assert.Equal(384, P(document, "Button").Y);
        Assert.Equal(384, P(document, "Button3").Y);
        Assert.Equal(380, P(document, "Button2").Y);

        // On another anchor: right edges at Button's, 120.
        document.SetAnchor(DesignPick.Of(C(document, "Button")));
        Assert.True(document.Align(DesignAlign.Right));
        Assert.Equal(40, P(document, "Button2").X);
        Assert.Equal(0, P(document, "Button3").X);
    }

    [Fact]
    public void AlignNeedsTwoAndNoneOfTheFigureTabsMenusOrGrids()
    {
        using DesignDocument document = App();
        Select(document, "Button");
        Assert.False(document.CanAlign);
        Assert.False(document.Align(DesignAlign.Left));
        document.Select([]);
        Assert.False(document.CanAlign);

        // Components in a grid are placed by cells: no align.
        DesignComponent grid = document.Gridify(document.Design.Figure);
        Assert.NotNull(grid);
        Select(document, "Button", "Button2");
        Assert.False(document.CanAlign);
        Assert.False(document.CanSpace);
        Assert.False(document.CanSameSize);
    }

    [Fact]
    public void AGroupAlignsAsOneBox()
    {
        using DesignDocument document = App();
        DesignGroup group = document.Design.AddGroup([DesignGroupMember.Of(C(document, "Button")), DesignGroupMember.Of(C(document, "Button2"))], "g");
        document.SelectPicks([DesignPick.Of(group), DesignPick.Of(C(document, "Button3"))]);
        Assert.True(document.CanAlign);
        Assert.False(document.CanSameSize); // GroupModel: CanSameSize false

        // The group's top is its box's, 423; Button3's is 372: the group moves down 51 as one.
        Assert.True(document.Align(DesignAlign.Top));
        Assert.Equal(349, P(document, "Button").Y);
        Assert.Equal(329, P(document, "Button2").Y);
        Assert.Equal(350, P(document, "Button3").Y);
    }

    // --- same size ---------------------------------------------------------------------------------

    [Fact]
    public void SameSizeKeepsTheTopEdge_AndASidesTheShapeFixes()
    {
        using DesignDocument document = App();
        Select(document, "Button", "Button2");
        Assert.True(document.SameSize(DesignSameSize.Size));
        Assert.Equal(new Rect2D(20, 393, 80, 30), P(document, "Button"));
        document.Undo();
        Assert.Equal(new Rect2D(20, 400, 100, 23), P(document, "Button"));

        // A horizontal slider keeps its height; its width follows.
        Select(document, "Slider", "Button2");
        Assert.False(document.SameSize(DesignSameSize.Height));
        Assert.True(document.SameSize(DesignSameSize.Width));
        Assert.Equal(80, P(document, "Slider").Width);
        Assert.Equal(3, P(document, "Slider").Height);
    }

    // --- space -------------------------------------------------------------------------------------

    [Fact]
    public void SpaceEvenlyOrByThePixelsTyped()
    {
        using DesignDocument document = App();
        Select(document, "Button3", "Button", "Button2");
        Assert.True(document.CanSpace);

        // 400 across, 300 of buttons: two gaps of 50 between the outer two, which stay.
        Assert.True(document.Space(horizontal: true, DesignArrange.Evenly));
        Assert.Equal(20, P(document, "Button").X);
        Assert.Equal(170, P(document, "Button2").X);
        Assert.Equal(300, P(document, "Button3").X);
        document.Undo();

        // parseFloat's reading of the box: "20 px" is 20, from the leftmost.
        Assert.True(document.Space(horizontal: true, "20 px"));
        Assert.Equal(140, P(document, "Button2").X);
        Assert.Equal(240, P(document, "Button3").X);
        Assert.False(document.Space(horizontal: true, "wide"));

        // Evenly with two moves nothing.
        Select(document, "Button", "Button2");
        Assert.False(document.Space(horizontal: false, DesignArrange.Evenly));
        Assert.Equal(20, DesignDocument.ParseFloat(" 20.0abc"));
        Assert.Null(DesignDocument.ParseFloat("Evenly"));
    }

    // --- reorder -----------------------------------------------------------------------------------

    [Fact]
    public void ReorderMovesTheSelectionInTheStack_AndTheCodesOrder()
    {
        using DesignDocument document = App();
        DesignComponent figure = document.Design.Figure;
        Select(document, "Button");
        Assert.False(document.CanStack(DesignStack.Back));
        Assert.False(document.CanStack(DesignStack.Backward));
        Assert.True(document.CanStack(DesignStack.Front));

        Assert.True(document.Stack(DesignStack.Forward));
        Assert.Equal(["Button2", "Button", "Button3", "Slider", "Panel", "EditField", "Knob"], Children(figure));
        Assert.Equal(["Button"], document.Picks.Select(static p => p.ToString()));
        Assert.True(document.Stack(DesignStack.Front));
        Assert.Equal(["Button2", "Button3", "Slider", "Panel", "EditField", "Knob", "Button"], Children(figure));
        Assert.False(document.CanStack(DesignStack.Front));

        // The code makes them in that order, the later in front.
        string text = document.CurrentText;
        Assert.True(text.IndexOf("app.Knob = uiknob", StringComparison.Ordinal) < text.IndexOf("app.Button = uibutton", StringComparison.Ordinal));

        document.Undo();
        document.Undo();
        Assert.Equal(["Button", "Button2", "Button3", "Slider", "Panel", "EditField", "Knob"], Children(figure));

        // Two together go to the back keeping the gap between them (ReorderCommand's moves, one at a time).
        Select(document, "Button3", "EditField");
        Assert.True(document.Stack(DesignStack.Back));
        Assert.Equal(["Button3", "Button", "Button2", "EditField", "Slider", "Panel", "Knob"], Children(figure));

        // The figure does not restack; a group does not either.
        document.Select([]);
        Assert.False(document.CanStack(DesignStack.Front));
    }

    // --- tab order ---------------------------------------------------------------------------------

    private static DesignDocument Square()
    {
        DesignComponent figure = Make("matlab.ui.Figure", "UIFigure", 100, 100, 640, 480);
        var design = new AppDesign(figure);
        figure.Add(Make("matlab.ui.control.Button", "D", 200, 300, 100, 22));
        figure.Add(Make("matlab.ui.control.Button", "A", 20, 400, 100, 22));
        figure.Add(Make("matlab.ui.control.Label", "L", 20, 300, 100, 22));
        figure.Add(Make("matlab.ui.control.Button", "B", 200, 400, 100, 22));
        return DesignDocument.FromText(DesignCodeWriter.Splice(AppDesignerDocument.NewAppText(), design)!, null);
    }

    [Fact]
    public void ApplyAutoTabOrderReadsTheLayoutRowByRow()
    {
        using DesignDocument document = Square();
        DesignComponent figure = document.Design.Figure;
        Select(document, "A");
        Assert.True(document.ApplyAutoTabOrder());
        Assert.Equal(["A", "B", "L", "D"], Children(figure));
        Assert.Equal(["A"], document.Picks.Select(static p => p.ToString())); // CANVAS > Tab Order keeps the selection

        // The tab order goes past the label, which takes no focus.
        Assert.Equal(["A", "B", "D"], document.TabOrder().Select(static c => c.CodeName));

        document.Undo();
        Assert.Equal(["D", "A", "L", "B"], Children(figure));

        // From the canvas menu, on a container: that container's, which is selected then.
        Assert.True(document.CanApplyAutoTabOrder(figure));
        Assert.True(document.ApplyAutoTabOrder(figure));
        Assert.Equal(["A", "B", "L", "D"], Children(figure));
        Assert.Equal([figure], document.Selection);
        Assert.False(document.ApplyAutoTabOrder(figure));
    }

    [Fact]
    public void VisualizeTabOrderShowsTheFocusOrder()
    {
        using DesignDocument document = Square();
        var gestures = new DesignGestures(document);
        (IReadOnlyList<DesignComponent> Order, int? Limit)? shown = null;
        gestures.TabOrderShown += (_, s) => shown = s;
        document.Select([]);
        DesignMenuItem visualize = gestures.MenuAt(new Point2D(5, 5)).Single(i => i.Label == "Visualize Tab Order");
        visualize.Run!();
        Assert.Equal(["D", "A", "B"], shown!.Value.Order.Select(static c => c.CodeName));
        Assert.Null(shown.Value.Limit);

        DesignMenuItem apply = gestures.MenuAt(new Point2D(5, 5)).Single(i => i.Label == "Apply Auto Tab Order");
        apply.Run!();
        Assert.Equal(["A", "B", "D"], shown.Value.Order.Select(static c => c.CodeName));
        Assert.Equal(DesignGestures.VisualizeLimit, shown.Value.Limit);
    }

    // --- add label ---------------------------------------------------------------------------------

    [Fact]
    public void AddLabelPutsTheEntrysLabelBesideTheComponent_Grouped()
    {
        using DesignDocument document = App();
        DesignComponent field = C(document, "EditField");
        Select(document, "EditField");
        Assert.True(document.CanAddLabel);
        Assert.Null(document.AddLabel());

        DesignGroup group = document.Design.LabeledGroupOf(field)!;
        DesignComponent label = group.Label!;
        Assert.Equal("EditField_2Label", label.CodeName); // named as a new labelled Edit Field's would be
        Assert.Equal("Edit Field", ((CharValue)label.Get("Text")!).Text);
        Assert.Equal("right", ((CharValue)label.Get("HorizontalAlignment")!).Text);
        Size2D size = DesignPalette.LabelSize("Edit Field");
        Assert.Equal(new Rect2D(100 - size.Width - 15, 100, size.Width, 22), document.Runtime.PositionOf(label)!.Value);
        Assert.Equal(new Rect2D(100, 100, 100, 22), P(document, "EditField")); // the component stays
        Assert.Equal(["Button", "Button2", "Button3", "Slider", "EditField_2Label", "Panel", "EditField", "Knob"], Children(document.Design.Figure));
        Assert.Equal("group", document.Picks.Single().ToString().Split(' ')[0]);
        Assert.False(document.CanAddLabel);

        document.Undo();
        Assert.Null(document.Design.Find("EditField_2Label"));
        Assert.Null(document.Design.GroupOf(field));

        // A button's entry has no label.
        Select(document, "Button");
        Assert.False(document.CanAddLabel);
    }

    [Fact]
    public void AddLabelPutsAGaugesLabelUnderIt()
    {
        using DesignDocument document = App();
        DesignComponent knob = C(document, "Knob");
        double[] outer = document.Runtime.ReadNumbers(knob, "OuterPosition")!;
        Select(document, "Knob");
        Assert.Null(document.AddLabel());
        DesignComponent label = document.Design.LabeledGroupOf(knob)!.Label!;
        Assert.Equal("center", ((CharValue)label.Get("HorizontalAlignment")!).Text);
        Rect2D at = document.Runtime.PositionOf(label)!.Value;
        Assert.Equal(outer[1] - 22 - 15, at.Y, 9);
        Assert.True(at.X >= outer[0] && at.X + at.Width <= outer[0] + outer[2]);
    }

    [Fact]
    public void AddLabelInAGridNeedsACellBeside()
    {
        using DesignDocument document = App();
        DesignComponent grid = document.Gridify(document.Design.Figure);
        DesignComponent field = C(document, "EditField");
        GridSpan span = document.SpanOf(field)!.Value;
        Select(document, "EditField");
        if (span.ColumnStart == 1)
        {
            Assert.Equal(DesignDocument.AddLabelFailedMessage, document.AddLabel());
            Assert.Null(document.Design.LabeledGroupOf(field));
        }
        else
        {
            Assert.Null(document.AddLabel());
            DesignComponent label = document.Design.LabeledGroupOf(field)!.Label!;
            Assert.Same(grid, label.Parent);
            Assert.Equal(span.ColumnStart - 1, document.SpanOf(label)!.Value.ColumnStart);
            Assert.Equal(span.RowStart, document.SpanOf(label)!.Value.RowStart);
        }
    }

    // --- the font keys -----------------------------------------------------------------------------

    [Fact]
    public void TheFontKeysSetTheFontOnTheSelectionAndWhatItHolds()
    {
        using DesignDocument document = App();
        Select(document, "Button", "Panel");
        Assert.True(document.CanChangeFont(DesignFontChange.Bold));
        Assert.True(document.ChangeFont(DesignFontChange.Bold));
        foreach (string name in new[] { "Button", "Panel", "Inner" })
        {
            Assert.Equal("bold", ((CharValue)document.ValueOf(C(document, name), "FontWeight")!).Text);
        }

        Assert.Equal(["Button", "Panel"], document.Picks.Select(static p => p.ToString()));
        Assert.True(document.ChangeFont(DesignFontChange.Bold)); // the anchor's is bold: off again
        Assert.Equal("normal", ((CharValue)document.ValueOf(C(document, "Inner"), "FontWeight")!).Text);

        Select(document, "Button");
        Assert.True(document.ChangeFont(DesignFontChange.Larger));
        Assert.Equal([14.0], ((NumericValue)document.ValueOf(C(document, "Button"), "FontSize")!).Data);
        Assert.True(document.ChangeFont(DesignFontChange.Smaller));
        Assert.True(document.ChangeFont(DesignFontChange.Smaller));
        Assert.Equal([11.0], ((NumericValue)document.ValueOf(C(document, "Button"), "FontSize")!).Data);
        document.Undo();
        Assert.Equal([12.0], ((NumericValue)document.ValueOf(C(document, "Button"), "FontSize")!).Data);

        // A lamp has no font (LampModel); the figure takes no font key.
        document.Select([]);
        Assert.False(document.CanChangeFont(DesignFontChange.Bold));
    }

    // --- the figure's edges ------------------------------------------------------------------------

    [Fact]
    public void TheFiguresEdgesResizeIt()
    {
        using DesignDocument document = App();
        var gestures = new DesignGestures(document);
        Assert.Equal(ResizeEdges.East, gestures.FigureEdgeAt(new Point2D(642, 240)));
        Assert.Equal(ResizeEdges.SouthEast, gestures.FigureEdgeAt(new Point2D(640, 480)));
        Assert.Equal(ResizeEdges.South, gestures.FigureEdgeAt(new Point2D(300, 482)));
        Assert.Equal(ResizeEdges.West, gestures.FigureEdgeAt(new Point2D(-3, 240)));
        Assert.Equal(ResizeEdges.SouthWest, gestures.FigureEdgeAt(new Point2D(5, 481)));
        Assert.Equal(ResizeEdges.None, gestures.FigureEdgeAt(new Point2D(320, 240)));
        Assert.Equal(DesignCursor.SizeWestEast, gestures.CursorAt(new Point2D(642, 240)));

        gestures.Press(new Point2D(642, 240), DesignButton.Left, DesignModifiers.None);
        gestures.Drag(new Point2D(682, 250), DesignModifiers.None);
        Assert.Equal(new Size2D(680, 480), gestures.FigureSize);
        gestures.Release();
        Assert.Equal(new Rect2D(100, 100, 680, 480), document.Runtime.PositionOf(document.Design.Figure)!.Value);
        Assert.Equal(new Rect2D(20, 400, 100, 23), P(document, "Button")); // positions from the bottom-left, kept
        Assert.Equal([document.Design.Figure], document.Selection);
        document.Undo();
        Assert.Equal(640, document.Runtime.Figure.Size.Width);

        // The left edge goes no further than the room left of the figure; the size never under 10.
        gestures.Press(new Point2D(-3, 240), DesignButton.Left, DesignModifiers.None);
        gestures.Drag(new Point2D(-200, 240), DesignModifiers.None);
        Assert.Equal(new Size2D(640 + 22, 480), gestures.FigureSize);
        Assert.Equal(-22, gestures.FigureShift);
        gestures.Cancel();
        Assert.Equal(640, document.Runtime.Figure.Size.Width);
        gestures.Press(new Point2D(640, 480), DesignButton.Left, DesignModifiers.None);
        gestures.Drag(new Point2D(-900, -900), DesignModifiers.None);
        gestures.Release();
        Assert.Equal(new Size2D(10, 10), document.Runtime.Figure.Size);
    }

    // --- the library and the menu ------------------------------------------------------------------

    [Fact]
    public void ALabelledEntryDroppedWithCtrlComesWithoutItsLabel()
    {
        using DesignDocument document = App();
        var gestures = new DesignGestures(document);
        IReadOnlyList<DesignComponent> added = gestures.Drop(Entry("Edit Field (Text)"), new Point2D(200, 200), withoutLabel: true);
        DesignComponent field = Assert.Single(added);
        Assert.Equal("matlab.ui.control.EditField", field.Type);
        Assert.Equal("EditField2", field.CodeName);
        Assert.Null(document.Design.GroupOf(field));
        Assert.Equal(2, gestures.Drop(Entry("Edit Field (Text)"), new Point2D(200, 300)).Count);
    }

    [Fact]
    public void TheCanvasMenuHasR2025bsLayoutItems()
    {
        using DesignDocument document = App();
        var gestures = new DesignGestures(document);
        Select(document, "EditField");
        IReadOnlyList<DesignMenuItem> items = gestures.MenuAt(new Point2D(150, 370));
        Assert.Equal("Add Label", items[0].Label);
        string[] labels = items.Where(static i => !i.IsSeparator).Select(static i => i.Label).ToArray();
        Assert.Equal(["Add Label", "Delete", "Zoom", "Align", "Same Size", "Group", "Reorder"], labels);
        DesignMenuItem align = items.Single(static i => i.Label == "Align");
        Assert.False(align.Enabled);
        Assert.Equal(["Left", "Center", "Right", "Top", "Middle", "Bottom"], align.Items!.Select(static i => i.Label));
        Assert.Equal(["Bring to Front", "Bring Forward", "Send Backward", "Send to Back"], items.Single(static i => i.Label == "Reorder").Items!.Select(static i => i.Label));

        Select(document, "Button", "Button2");
        DesignMenuItem same = gestures.MenuAt(new Point2D(150, 370)).Single(static i => i.Label == "Same Size");
        Assert.True(same.Enabled);
        same.Items![2].Run!(); // Height
        Assert.Equal(30, P(document, "Button").Height);

        document.Select([]);
        string[] figureItems = gestures.MenuAt(new Point2D(5, 5)).Where(static i => !i.IsSeparator).Select(static i => i.Label).ToArray();
        Assert.Equal(["Zoom", "Grid", "Snap to Grid", "Alignment Hints", "Resizing Hints", "Apply Grid Layout", "Apply Auto Tab Order", "Visualize Tab Order"], figureItems);
    }
}
