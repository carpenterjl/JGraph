using JGraph.Core.Primitives;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Workspace;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The App Designer window plan's stage W5 on an app: Copy, Cut and Paste - where a paste goes, how
/// far it moves, the copies' names, callbacks and groups - Paste at a point, Duplicate, a Ctrl-drag's
/// copy, a paste into a grid and into another app, Select All, and the canvas menu's items. The
/// naming and the grid's cells are checked against R2025b's own modules by
/// <see cref="Designer.DesignClipboardOracleTests"/>.
/// </summary>
[Collection("JG facade")]
public class AppDesignerClipboardTests
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

    /// <summary>A 640-by-480 figure: three buttons, a panel with a button, and a slider.</summary>
    private static DesignDocument App()
    {
        DesignComponent figure = Make("matlab.ui.Figure", "UIFigure", 100, 100, 640, 480);
        figure.Set("Name", new CharValue("MATLAB App"));
        var design = new AppDesign(figure);
        figure.Add(Make("matlab.ui.control.Button", "Button", 20, 400, 100, 23));
        figure.Add(Make("matlab.ui.control.Button", "Button2", 150, 380, 80, 30));
        figure.Add(Make("matlab.ui.control.Button", "Button3", 300, 350, 120, 22));
        DesignComponent panel = Make("matlab.ui.container.Panel", "Panel", 350, 50, 250, 200);
        figure.Add(panel);
        panel.Add(Make("matlab.ui.control.Button", "Inner", 20, 20, 100, 23));
        figure.Add(Make("matlab.ui.control.Slider", "Slider", 20, 200, 150, 3));
        return DesignDocument.FromText(DesignCodeWriter.Splice(AppDesignerDocument.NewAppText(), design)!, null);
    }

    private static DesignComponent C(DesignDocument document, string name) => document.Design.Find(name)!;

    private static Rect2D P(DesignDocument document, string name) => document.Runtime.PositionOf(C(document, name))!.Value;

    private static void Select(DesignDocument document, params string[] names) =>
        document.SelectPicks(names.Select(n => DesignPick.Of(C(document, n))).ToArray());

    private static string[] Picked(DesignDocument document) => document.Picks.Select(static p => p.ToString()!).ToArray();

    // --- copy and paste ------------------------------------------------------------------------------

    /// <summary>Into the container they came from, each paste lands 20 pixels along and down from the last, named Button_2, Button_3; the copies are selected.</summary>
    [Fact]
    public void PasteIntoTheSameContainerStepsAlongAndDown()
    {
        using DesignDocument document = App();
        var clipboard = new DesignClipboard();
        Select(document, "Button");
        Assert.True(document.CanCopy);
        Assert.False(document.CanPaste(clipboard));
        Assert.True(document.Copy(clipboard));
        Assert.Equal(["Button"], clipboard.CodeNames);
        Assert.False(document.CanUndo); // a copy changes nothing

        Assert.True(document.Paste(clipboard));
        Assert.Equal(new Rect2D(40, 380, 100, 23), P(document, "Button_2"));
        Assert.Equal(["Button_2"], Picked(document));

        // The selection is the copy now, in the same container: the next one steps on from the first place.
        Assert.True(document.Paste(clipboard));
        Assert.Equal(new Rect2D(60, 360, 100, 23), P(document, "Button_3"));

        // Undone, its paste uncounted: the next lands where the undone one did.
        document.Undo();
        Assert.Null(document.Design.Find("Button_3"));
        Assert.Equal(["Button_2"], Picked(document));
        Assert.True(document.Paste(clipboard));
        Assert.Equal(new Rect2D(60, 360, 100, 23), P(document, "Button_3"));
    }

    /// <summary>A copy takes no callbacks; a cut's first paste goes back where the components were with them, the callback made again.</summary>
    [Fact]
    public void ACutsFirstPasteKeepsItsPlaceAndCallbacks()
    {
        using DesignDocument document = App();
        Assert.Null(document.AddCallback(C(document, "Button"), "ButtonPushedFcn"));
        string callback = ((CallbackValue)C(document, "Button").Get("ButtonPushedFcn")!).Method;
        var clipboard = new DesignClipboard();

        Select(document, "Button");
        document.Copy(clipboard);
        Assert.True(document.Paste(clipboard));
        Assert.Null(C(document, "Button_2").Get("ButtonPushedFcn"));
        document.Undo();

        // Cut: the button and its untouched callback go; nothing is selected.
        Select(document, "Button");
        Assert.True(document.Cut(clipboard));
        Assert.True(clipboard.WasCut);
        Assert.Null(document.Design.Find("Button"));
        Assert.Null(document.Code!.Find(callback));
        Assert.True(document.SelectionIsEmpty);

        // The first paste: back in the figure, in its place, under its name, its callback made again.
        Assert.True(document.Paste(clipboard));
        Assert.Equal(new Rect2D(20, 400, 100, 23), P(document, "Button"));
        Assert.Equal(callback, ((CallbackValue)C(document, "Button").Get("ButtonPushedFcn")!).Method);
        Assert.NotNull(document.Code!.Find(callback));
        Assert.Contains(callback, document.CurrentText, StringComparison.Ordinal);

        // The second steps along and takes no callback.
        Assert.True(document.Paste(clipboard));
        Assert.Equal(new Rect2D(40, 380, 100, 23), P(document, "Button_2"));
        Assert.Null(C(document, "Button_2").Get("ButtonPushedFcn"));

        // The cut is one edit.
        document.Undo();
        document.Undo();
        document.Undo();
        Assert.Equal(new Rect2D(20, 400, 100, 23), P(document, "Button"));
        Assert.Equal(callback, ((CallbackValue)C(document, "Button").Get("ButtonPushedFcn")!).Method);
    }

    /// <summary>Into another container the copies' box has its top-left corner 8 pixels in from the container's.</summary>
    [Fact]
    public void PasteIntoAnotherContainerLandsAtItsTopLeft()
    {
        using DesignDocument document = App();
        var clipboard = new DesignClipboard();
        Select(document, "Button", "Button2");
        document.Copy(clipboard);

        // With the panel's button selected the paste goes into the panel.
        Select(document, "Inner");
        Assert.True(document.Paste(clipboard));
        DesignComponent panel = C(document, "Panel");
        Assert.Same(panel, C(document, "Button_2").Parent);
        Assert.Same(panel, C(document, "Button2_2").Parent);
        double height = document.Runtime.InnerOnSurface(panel).Height;

        // The box round both (outer boxes 20..230 across, 380..423 up) at (8, height - 8).
        Assert.Equal(new Rect2D(8, height - 8 - 23, 100, 23), P(document, "Button_2"));
        Assert.Equal(new Rect2D(138, height - 8 - 43, 80, 30), P(document, "Button2_2"));
        Assert.Equal(["Button_2", "Button2_2"], Picked(document));

        // With the panel itself selected, into it too, the next one stepped along.
        Select(document, "Panel");
        Assert.True(document.Paste(clipboard));
        Assert.Equal(new Rect2D(28, height - 8 - 23 - 20, 100, 23), P(document, "Button_3"));
    }

    /// <summary>The canvas menu's Paste puts the copies' top-left corner where the menu was opened.</summary>
    [Fact]
    public void PasteAtPutsTheBoxWhereTheMenuOpened()
    {
        using DesignDocument document = App();
        var clipboard = new DesignClipboard();
        Select(document, "Button");
        document.Copy(clipboard);
        Rect2D figure = document.Runtime.InnerOnSurface(document.Design.Figure);
        Point2D at = new(figure.X + 200, figure.Y + 300);
        Assert.True(document.CanPasteAt(clipboard, at));
        Assert.True(document.PasteAt(clipboard, at));

        // 200 along and 300 down from the figure's top-left: left 201, top 480 - 300 + 1.
        Assert.Equal(new Rect2D(201, 181 - 23, 100, 23), P(document, "Button_2"));
    }

    /// <summary>A labelled component copies as one: its field and label renamed together, the pair grouped and selected.</summary>
    [Fact]
    public void ALabelledComponentCopiesWhole()
    {
        using DesignDocument document = App();
        var clipboard = new DesignClipboard();
        IReadOnlyList<DesignComponent> made = document.Add(Entry("Edit Field (Text)"), document.Design.Figure, new Point2D(30, 30));
        Assert.NotNull(document.SelectedLabeled);
        Assert.True(document.Copy(clipboard));
        Assert.True(document.Paste(clipboard));

        DesignGroup pasted = Assert.IsType<DesignGroup>(document.SelectedLabeled);
        Assert.Equal("EditField_2Label", pasted.Label!.CodeName);
        Assert.Equal("EditField_2", pasted.Field!.CodeName);
        Assert.Equal(2, document.Design.Groups.Count(static g => g.IsLabeled));
        document.Undo();
        Assert.Single(document.Design.Groups, static g => g.IsLabeled);
        Assert.Null(document.Design.Find("EditField_2"));
    }

    // --- duplicate -----------------------------------------------------------------------------------

    /// <summary>Duplicate copies the selection 20 pixels along and down in its container, as one edit, with no callbacks.</summary>
    [Fact]
    public void DuplicateCopiesInPlace()
    {
        using DesignDocument document = App();
        Assert.Null(document.AddCallback(C(document, "Button"), "ButtonPushedFcn"));
        Select(document, "Button", "Inner");
        Assert.False(document.CanDuplicate); // not one container

        Select(document, "Button", "Button3");
        Assert.True(document.CanDuplicate);
        Assert.True(document.Duplicate());
        Assert.Equal(new Rect2D(40, 380, 100, 23), P(document, "Button_2"));
        Assert.Equal(new Rect2D(320, 330, 120, 22), P(document, "Button3_2"));
        Assert.Null(C(document, "Button_2").Get("ButtonPushedFcn"));
        Assert.Equal(["Button_2", "Button3_2"], Picked(document));
        document.Undo();
        Assert.Null(document.Design.Find("Button_2"));
        Assert.Equal(["Button", "Button3"], Picked(document));

        // Never the figure.
        document.Select([]);
        Assert.False(document.CanDuplicate);
        Assert.False(document.Duplicate());
    }

    /// <summary>In a grid, a duplicate takes the first free cells; none free, nothing is made.</summary>
    [Fact]
    public void DuplicateInAGridTakesTheFirstFreeCell()
    {
        using DesignDocument document = App();
        DesignComponent grid = document.Gridify(document.Design.Figure);
        Select(document, "Button");
        int rows = document.TracksOf(grid, GridAxis.Row).Count;
        int columns = document.TracksOf(grid, GridAxis.Column).Count;
        int children = grid.Children.Count;
        bool full = children == rows * columns;

        if (full)
        {
            Assert.False(document.Duplicate());
        }

        // A row more: room in its first cell.
        document.InsertTrack(grid, GridAxis.Row, rows + 1);
        Select(document, "Button");
        Assert.True(document.Duplicate());
        DesignComponent copy = C(document, "Button_2");
        Assert.Same(grid, copy.Parent);
        Assert.Null(copy.Get("Position"));
        GridSpan span = document.SpanOf(copy)!.Value;
        Assert.True(span.RowStart >= 1 && span.ColumnStart >= 1);
        Assert.DoesNotContain(grid.Children.Where(c => !ReferenceEquals(c, copy)), c => document.SpanOf(c) == span);
    }

    /// <summary>A drag with Ctrl held copies the selection where it is dropped and leaves it where it was.</summary>
    [Fact]
    public void ACtrlDragCopies()
    {
        using DesignDocument document = App();
        var gestures = new DesignGestures(document) { ShowAlignmentHints = false };
        Select(document, "Button");
        Point2D from = Centre(document.BoxOf(C(document, "Button"))!.Value.Box);
        Assert.True(gestures.Press(from, DesignButton.Left, DesignModifiers.Control));
        gestures.Drag(new Point2D(from.X + 30, from.Y), DesignModifiers.Control);
        gestures.Drag(new Point2D(from.X + 60, from.Y + 10), DesignModifiers.Control);
        Assert.True(document.IsDuplicating);
        Assert.Equal(new Rect2D(20, 400, 100, 23), P(document, "Button")); // it stays while its copy is dragged
        Assert.Single(document.GestureGhosts);
        gestures.Release();

        Assert.Equal(new Rect2D(20, 400, 100, 23), P(document, "Button"));
        Assert.Equal(new Rect2D(80, 390, 100, 23), P(document, "Button_2"));
        Assert.Equal(["Button_2"], Picked(document));
        document.Undo();
        Assert.Null(document.Design.Find("Button_2"));

        // Dropped in the panel, the copy goes in.
        Select(document, "Button");
        Rect2D panel = document.BoxOf(C(document, "Panel"))!.Value.Box;
        Assert.True(gestures.Press(from, DesignButton.Left, DesignModifiers.Control));
        gestures.Drag(new Point2D(from.X + 5, from.Y + 5), DesignModifiers.Control);
        gestures.Drag(Centre(panel), DesignModifiers.Control);
        gestures.Release();
        Assert.Same(C(document, "Panel"), C(document, "Button_2").Parent);
        Assert.Same(document.Design.Figure, C(document, "Button").Parent);
    }

    // --- another app ---------------------------------------------------------------------------------

    /// <summary>Into another app: into its figure at the same place, named among its names, with no callbacks.</summary>
    [Fact]
    public void PasteIntoAnotherApp()
    {
        using DesignDocument first = App();
        using DesignDocument second = App();
        Assert.Null(first.AddCallback(C(first, "Button2"), "ButtonPushedFcn"));
        var clipboard = new DesignClipboard();
        Select(first, "Button2");
        first.Copy(clipboard);

        Select(second, "Inner"); // anything but the figure still pastes into the selection's container
        second.Select([]);
        Assert.True(second.CanPaste(clipboard));
        Assert.True(second.Paste(clipboard));
        Assert.Equal(new Rect2D(150, 380, 80, 30), P(second, "Button2_2"));
        Assert.Null(C(second, "Button2_2").Get("ButtonPushedFcn"));
        Assert.Null(second.Code!.Callbacks.FirstOrDefault(static c => !c.IsGenerated && !c.IsStartup));
        Assert.Null(first.Design.Find("Button2_2"));
    }

    // --- select all and the menu ---------------------------------------------------------------------

    /// <summary>Select All selects the figure's components - not what is inside them - a group whole.</summary>
    [Fact]
    public void SelectAllSelectsTheFiguresComponents()
    {
        using DesignDocument document = App();
        Select(document, "Inner");
        document.SelectAll();
        Assert.Equal(["Button", "Button2", "Button3", "Panel", "Slider"], Picked(document));

        Select(document, "Button", "Button2");
        DesignGroup group = document.Group()!;
        document.Select([]);
        document.SelectAll();
        Assert.Equal(4, document.Picks.Count);
        Assert.Same(group, document.Picks[0].Group);
    }

    /// <summary>The canvas menu has Cut, Copy, Paste and Duplicate before Delete, with their keys; Paste waits for something to paste.</summary>
    [Fact]
    public void TheCanvasMenuHasTheClipboardItems()
    {
        using DesignDocument document = App();
        var clipboard = new DesignClipboard();
        var gestures = new DesignGestures(document) { Clipboard = clipboard };
        Select(document, "Button");
        Point2D at = Centre(document.BoxOf(C(document, "Button"))!.Value.Box);
        IReadOnlyList<DesignMenuItem> items = gestures.MenuAt(at);
        string[] labels = items.Select(static i => i.Label).ToArray();
        int cut = Array.IndexOf(labels, "Cut");
        Assert.Equal(["Cut", "Copy", "Paste", "Duplicate", "Delete"], labels[cut..(cut + 5)]);
        Assert.Equal(["Ctrl+X", "Ctrl+C", "Ctrl+V", "Ctrl+D", "Delete"], items.Skip(cut).Take(5).Select(static i => i.Shortcut));
        Assert.True(items[cut].Enabled);
        Assert.False(items[cut + 2].Enabled); // nothing to paste

        items[cut + 1].Run!();
        Assert.True(gestures.MenuAt(at).First(static i => i.Label == "Paste").Enabled);

        // On the figure: no Duplicate, nor Cut or Copy enabled.
        document.Select([]);
        Point2D empty = new(document.Runtime.InnerOnSurface(document.Design.Figure).X + 600, document.Runtime.InnerOnSurface(document.Design.Figure).Y + 20);
        IReadOnlyList<DesignMenuItem> onFigure = gestures.MenuAt(empty);
        Assert.DoesNotContain(onFigure, static i => i.Label == "Duplicate");
        Assert.False(onFigure.First(static i => i.Label == "Cut").Enabled);
        Assert.True(onFigure.First(static i => i.Label == "Paste").Enabled);
    }
}
