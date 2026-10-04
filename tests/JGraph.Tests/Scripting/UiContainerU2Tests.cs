using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Rendering;
using JGraph.Rendering.Layout;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// U2 of the app-building plan (ADR 0199): the container tree where a fixture cannot reach — the
/// layout the window and the renderer share, what a resize does on the script thread, the screen a
/// host reports, and a <c>uifigure</c>'s place among the figures. What R2025b answers headless is
/// held by the <c>u2_*</c> parity fixtures; these hold the parts that need a window's side of it.
/// </summary>
[Collection("JG facade")]
public class UiContainerU2Tests : IAsyncLifetime
{
    private readonly RecordingScriptOutput _output = new();
    private JgsReplSession _session = null!;

    public Task InitializeAsync()
    {
        JG.Reset();
        _session = Assert.IsType<JgsReplSession>(((IScriptRepl)new MatlabScriptEngine()).CreateSession(
            new ScriptContext(_output, (_, _) => { })));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        ScriptEventQueue.Flush();
        ScriptEventQueue.Flush();
        ScriptComponentFrames.SetSink(null);
        UiScreen.SetProvider(null);
        await _session.DisposeAsync();
        JG.Reset();
    }

    private async Task Exec(string code)
    {
        ScriptRunResult result = await _session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    private Task Drain() =>
        ((IGraphicsEventSession)_session).DrainGraphicsEventsAsync(null, CancellationToken.None);

    private static FigureModel OnlyFigure() =>
        JG.TryGetFigure(Assert.Single(JG.FigureNumbers), out FigureModel figure)
            ? figure
            : throw new InvalidOperationException("no figure");

    private static void AssertRect(Rect2D expected, Rect2D actual)
    {
        Assert.Equal(expected.X, actual.X, 9);
        Assert.Equal(expected.Y, actual.Y, 9);
        Assert.Equal(expected.Width, actual.Width, 9);
        Assert.Equal(expected.Height, actual.Height, 9);
    }

    // --- the units engine ------------------------------------------------------------------------

    [Theory]
    [InlineData(UiUnits.Points, 14.25, 21.75, 75, 30)]
    [InlineData(UiUnits.Inches, 19 / 96.0, 29 / 96.0, 100 / 96.0, 40 / 96.0)]
    [InlineData(UiUnits.Characters, 19 / 5.6, 29 / 15.0, 100 / 5.6, 40 / 15.0)]
    [InlineData(UiUnits.Normalized, 19 / 560.0, 29 / 420.0, 100 / 560.0, 40 / 420.0)]
    public void Units_ConvertR2025bsWay_PixelsCountFromOneAndEverythingElseFromZero(
        UiUnits units, double x, double y, double width, double height)
    {
        var reference = new Size2D(560, 420);
        var pixels = new Rect2D(20, 30, 100, 40);
        Rect2D converted = UiUnitConverter.FromPixels(pixels, units, reference);
        AssertRect(new Rect2D(x, y, width, height), converted);
        AssertRect(pixels, UiUnitConverter.ToPixels(converted, units, reference));
    }

    // --- the layout --------------------------------------------------------------------------------

    [Fact]
    public async Task Layout_PlacesAPanelsChildrenInItsInnerArea_AndClipsThemToIt()
    {
        await Exec("""
            f = figure('Visible', 'off', 'Position', [100 100 560 420]);
            p = uipanel(f, 'Units', 'pixels', 'Position', [50 60 200 150], 'Title', 'T');
            fill = uicontrol(p, 'Style', 'text', 'Units', 'normalized', 'Position', [0 0 1 1]);
            over = uicontrol(p, 'Position', [150 100 100 100]);
            """);

        UiLayoutResult layout = UiLayout.Compute(OnlyFigure().TakeComponentFrame(), new Size2D(560, 420));
        UiPanelPlacement panel = Assert.Single(layout.Panels);

        // Top-left origin: the box's top is the figure's height less the panel's top edge. The title
        // takes eleven pixels from the top, the border one from every other side.
        AssertRect(new Rect2D(49, 420 - 59 - 150, 200, 150), panel.Box);
        AssertRect(new Rect2D(50, 222, 198, 138), panel.Inner);

        Assert.Equal(2, layout.Controls.Count);
        AssertRect(panel.Inner, layout.Controls[0].Box);
        AssertRect(panel.Inner, layout.Controls[0].Clip);

        // The second child runs past the panel's right and top edges, and is clipped to the inside.
        UiPlacement over = layout.Controls[1];
        AssertRect(new Rect2D(50 + 149, 222 + 138 - 99 - 100, 100, 100), over.Box);
        AssertRect(panel.Inner, over.Clip);
        Assert.True(over.Box.Right > panel.Inner.Right && over.Box.Y < panel.Inner.Y);
    }

    [Fact]
    public async Task Layout_APanelStackedOverAControlCutsItAway_AndOneUnderItDoesNot()
    {
        await Exec("""
            f = figure('Visible', 'off', 'Position', [100 100 400 300]);
            under = uicontrol(f, 'Position', [60 60 100 40]);
            p = uipanel(f, 'Units', 'pixels', 'Position', [100 50 200 100]);
            above = uicontrol(f, 'Position', [150 70 100 40]);
            inside = uicontrol(p, 'Position', [10 10 50 20]);
            """);

        UiLayoutResult layout = UiLayout.Compute(OnlyFigure().TakeComponentFrame(), new Size2D(400, 300));
        UiPanelPlacement panel = Assert.Single(layout.Panels);
        UiPlacement Placed(int creation) => layout.Controls.Single(
            c => ReferenceEquals(c.Control.Source, OnlyFigure().Components.OfType<UiControlModel>().Concat(
                OnlyFigure().Components.OfType<UiPanelModel>().SelectMany(q => q.Components.OfType<UiControlModel>())).ElementAt(creation)));

        // Made before the panel: the panel is drawn over it, so the window cuts it where they cross.
        AssertRect(panel.Box, Assert.Single(Placed(0).Occluders));

        // Made after the panel, or inside it: nothing lies over it.
        Assert.Empty(Placed(1).Occluders);
        Assert.Empty(Placed(2).Occluders);
    }

    [Fact]
    public async Task Layout_AHiddenPanelHidesEverythingInIt_WithoutChangingWhatTheChildrenSay()
    {
        await Exec("""
            f = figure('Visible', 'off', 'Position', [100 100 400 300]);
            p = uipanel(f, 'Visible', 'off');
            q = uipanel(p);
            c = uicontrol(q);
            """);

        UiLayoutResult layout = UiLayout.Compute(OnlyFigure().TakeComponentFrame(), new Size2D(400, 300));
        UiPlacement control = Assert.Single(layout.Controls);
        Assert.False(control.Visible);
        Assert.True(control.Control.Visible);
        Assert.False(Assert.Single(layout.Panels).Children[0].Visible);
    }

    [Fact]
    public async Task Layout_FollowsAResize_WithoutAnotherFrame()
    {
        await Exec("""
            f = figure('Visible', 'off', 'Position', [100 100 400 300]);
            p = uipanel(f, 'BorderType', 'none');
            c = uicontrol(p, 'Units', 'normalized', 'Position', [0.5 0.5 0.5 0.5]);
            """);

        // One frame, two sizes: the window lays the last frame out again when it is resized.
        UiFrame frame = OnlyFigure().TakeComponentFrame();
        AssertRect(new Rect2D(200, 0, 200, 150), UiLayout.Compute(frame, new Size2D(400, 300)).Controls[0].Box);
        AssertRect(new Rect2D(400, 0, 400, 300), UiLayout.Compute(frame, new Size2D(800, 600)).Controls[0].Box);
    }

    // --- the renderer ------------------------------------------------------------------------------

    [Fact]
    public async Task Renderer_DrawsAnAxesInsideItsPanel_ClippedToThePanelsInnerArea()
    {
        await Exec("""
            f = figure('Visible', 'off', 'Position', [100 100 560 420]);
            p = uipanel(f, 'Units', 'pixels', 'Position', [50 60 300 250], 'Title', 'Outer');
            a = axes(p, 'OuterPosition', [0 0 1 1]);
            plot(a, 1:3);
            """);

        FigureModel figure = OnlyFigure();
        var context = new RecordingRenderContext(new Size2D(560, 420));
        new FigureRenderer().Render(figure, context);

        // The area the axes' fractions are fractions of is the panel's inner area, not the figure.
        AxesModel axes = Assert.Single(figure.Axes);
        Assert.Same(figure.Components[0], axes.Container);
        AxesLayoutSnapshot drawn = Assert.NotNull(axes.LastLayout);
        AssertRect(new Rect2D(50, 420 - 59 - 250 + 11, 298, 238), drawn.CanvasPx);
        AssertRect(drawn.CanvasPx, drawn.OuterPx);
        Assert.True(context.MaxClipDepth >= 2, "the panel and its inner area each clip what is in them");
    }

    [Fact]
    public async Task Renderer_AnAxesPlacedInPixels_KeepsItsPixelsWhenTheFigureIsResized()
    {
        await Exec("""
            f = figure('Visible', 'off', 'Position', [100 100 560 420]);
            a = axes(f, 'Units', 'pixels', 'Position', [50 60 300 200]);
            """);

        FigureModel figure = OnlyFigure();
        AxesModel axes = Assert.Single(figure.Axes);
        foreach ((double width, double height) in new[] { (560.0, 420.0), (700.0, 500.0) })
        {
            new FigureRenderer().Render(figure, new RecordingRenderContext(new Size2D(width, height)));
            AssertRect(new Rect2D(49, height - 59 - 200, 300, 200), Assert.NotNull(axes.LastLayout).PlotAreaPx);
        }
    }

    // --- a resize, on the script thread ----------------------------------------------------------------

    [Fact]
    public async Task Resize_AutoResizeChildren_SilencesSizeChangedFcn_AndLeavesPixelPlacedChildrenInPlace()
    {
        await Exec("""
            calls = 0;
            uf = uifigure('Visible', 'off', 'Position', [100 100 400 300]);
            b = uicontrol(uf, 'Position', [101 51 100 50]);
            n = uicontrol(uf, 'Units', 'normalized', 'Position', [0.5 0.5 0.25 0.25]);
            p = uipanel(uf, 'Position', [201 151 100 100]);
            k = uicontrol(p, 'Position', [11 11 40 20]);
            warning('off', 'MATLAB:ui:containers:SizeChangedFcnDisabledWhenAutoResizeOn');
            uf.SizeChangedFcn = 'calls = calls + 1;';
            """);

        // What the window does when it is dragged to twice the size: the model's size, then the event.
        FigureModel figure = OnlyFigure();
        figure.Size = new Size2D(800, 600);
        ScriptGraphicsCallbacks.NotifySizeChanged(figure);
        await Drain();

        // R2025b, with a window (probe u2w_resize): a uifigure that grows leaves what is placed in
        // pixels where it is, and what is placed in fractions follows by itself.
        var button = (UiControlModel)figure.Components[0];
        var normalized = (UiControlModel)figure.Components[1];
        var panel = (UiPanelModel)figure.Components[2];
        AssertRect(new Rect2D(101, 51, 100, 50), button.Position);
        AssertRect(new Rect2D(0.5, 0.5, 0.25, 0.25), normalized.Position);
        AssertRect(new Rect2D(401, 301, 200, 150), normalized.PixelPosition());
        AssertRect(new Rect2D(201, 151, 100, 100), panel.Position);
        AssertRect(new Rect2D(11, 11, 40, 20), ((UiControlModel)panel.Components[0]).Position);

        await Exec("fprintf('calls=%d\\n', calls);");
        Assert.Contains("calls=0", _output.NormalText);
    }

    [Fact]
    public async Task Resize_WithAutoResizeOff_RunsSizeChangedFcn_ForTheFigureAndEachContainerThatChanged()
    {
        // A host showing the figure: frames are taken at each flush, which is also where a
        // container's size is looked at.
        ScriptComponentFrames.SetSink(ScriptComponentFrames.Applied);
        await Exec("""
            f = figure('Position', [100 100 400 300]);
            setappdata(f, 'log', {});
            f.SizeChangedFcn = @(s, e) setappdata(s, 'log', [getappdata(s, 'log'), {['figure:' class(e) ':' e.EventName]}]);
            note = @(s, what) setappdata(ancestor(s, 'figure'), 'log', [getappdata(ancestor(s, 'figure'), 'log'), {what}]);
            p = uipanel(f, 'Units', 'normalized', 'Position', [0.1 0.1 0.5 0.5], 'SizeChangedFcn', @(s, e) note(s, 'panel'));
            q = uipanel(f, 'Units', 'pixels', 'Position', [10 10 50 50], 'SizeChangedFcn', @(s, e) note(s, 'fixed'));
            b = uicontrol(f, 'Position', [100 100 100 50]);
            """);

        // Shown for the first time, each container is told once (R2025b tells it several times).
        await Drain();
        await Exec("fprintf('first=%s\\n', strjoin(getappdata(f, 'log'), ','));");
        Assert.Contains("first=panel,fixed\n", _output.NormalText);

        FigureModel figure = OnlyFigure();
        figure.Size = new Size2D(800, 600);
        ScriptGraphicsCallbacks.NotifySizeChanged(figure);

        // Two drains: a container's callback is queued by the figure's resize, behind it, and a
        // drain delivers only what was waiting when it began.
        await Drain();
        await Drain();

        // The figure's, with R2025b's event data, then the panel whose pixel size followed it; the
        // panel placed in pixels did not change and is not told, and the control placed in pixels
        // stays where it was.
        await Exec("fprintf('resized=%s\\n', strjoin(getappdata(f, 'log'), ','));");
        Assert.Contains(
            "resized=panel,fixed,figure:matlab.ui.eventdata.SizeChangedData:SizeChanged,panel\n", _output.NormalText);
        AssertRect(new Rect2D(100, 100, 100, 50), ((UiControlModel)figure.Components[2]).Position);

        // The same size again is no resize at all.
        ScriptGraphicsCallbacks.NotifySizeChanged(figure);
        await Drain();
        await Drain();

        // A script's own write to a container's size is one, at the next flush.
        await Exec("q.Position = [10 10 80 60];");
        await Drain();
        await Exec("L = getappdata(f, 'log'); fprintf('own=%d %s\\n', numel(L), L{end});");
        Assert.Contains("own=5 fixed\n", _output.NormalText);
    }

    // --- uifigure ------------------------------------------------------------------------------------

    [Fact]
    public async Task UiFigure_TakesNoFigureNumber_IsNeverCurrent_AndAPlotIntoItMakesNoFigure()
    {
        await Exec("""
            uf = uifigure('Visible', 'off');
            ax = axes(uf);
            plot(ax, 1:3);
            title(ax, 'in the app');
            """);

        // One figure, registered under a key that is not a figure number, and nothing current.
        FigureModel app = OnlyFigure();
        Assert.True(app.IsUiFigure);
        Assert.True(JG.IsHiddenNumber(JG.GetFigureNumber(app)));
        Assert.Equal(0, JG.CurrentFigureNumberOrZero);
        Assert.Single(app.Axes[0].Plots);

        // The first numbered figure is still figure 1, and it — not the app — is what gcf answers.
        await Exec("f = figure('Visible', 'off'); fprintf('n=%d same=%d\\n', f.Number, isequal(gcf, f));");
        Assert.Contains("n=1 same=1", _output.NormalText);
        Assert.Equal(2, JG.FigureNumbers.Count);
    }

    [Fact]
    public async Task UiFigure_ComponentsTakeTheAppDefaults_AndItsPanelResizesItsChildren()
    {
        await Exec("""
            uf = uifigure('Visible', 'off');
            p = uipanel(uf);
            """);

        var panel = (UiPanelModel)OnlyFigure().Components[0];
        Assert.Equal(UiUnits.Pixels, panel.Units);
        AssertRect(new Rect2D(20, 20, 260, 221), panel.Position);
        Assert.True(panel.AutoResizeChildren);
        Assert.Equal("Helvetica", panel.FontName);
        Assert.Equal(UiFontUnits.Pixels, panel.FontUnits);
    }

    // --- copyobj, through the document format ------------------------------------------------------------

    [Fact]
    public async Task Copyobj_CopiesAPanelWithItsComponentsAndItsAxes_IntoAnotherFigure()
    {
        await Exec("""
            f = figure('Visible', 'off', 'Position', [100 100 400 300]);
            p = uipanel(f, 'Title', 'P', 'Units', 'pixels', 'Position', [20 20 200 150]);
            c = uicontrol(p, 'Style', 'edit', 'String', 'hello', 'Position', [10 10 80 20]);
            a = axes(p);
            plot(a, 1:3);
            g = figure('Visible', 'off');
            q = copyobj(p, g);
            fprintf('title=%s kids=%d parent=%d\n', get(q, 'Title'), numel(get(q, 'Children')), get(q, 'Parent') == g);
            k = findobj(q, 'Type', 'uicontrol');
            fprintf('string=%s\n', get(k, 'String'));
            set(k, 'String', 'changed');
            fprintf('original=%s\n', get(c, 'String'));
            """);

        Assert.Contains("title=P kids=2 parent=1\n", _output.NormalText);
        Assert.Contains("string=hello\n", _output.NormalText);
        Assert.Contains("original=hello\n", _output.NormalText);

        // The copy's axes went with it, into the other figure's list, still placed in the copy.
        Assert.True(JG.TryGetFigure(2, out FigureModel second));
        AxesModel copied = Assert.Single(second.Axes);
        Assert.Same(second.Components[0], copied.Container);
        Assert.Single(copied.Plots);
        Assert.True(JG.TryGetFigure(1, out FigureModel first));
        Assert.Single(first.Axes);
    }

    // --- the screen ------------------------------------------------------------------------------------

    [Fact]
    public async Task Screen_TheRootReportsEveryMonitor_AndAFiguresNormalizedUnitsAreFractionsOfThePrimary()
    {
        UiScreen.SetProvider(() => [new Rect2D(1, 1, 1000, 500), new Rect2D(1001, 101, 800, 600)]);
        await Exec("""
            r = groot;
            fprintf('screen=%s\n', mat2str(r.ScreenSize));
            fprintf('monitors=%s\n', mat2str(r.MonitorPositions));
            set(r, 'Units', 'normalized');
            fprintf('normalized=%s\n', mat2str(r.MonitorPositions));
            set(r, 'Units', 'pixels');
            f = figure('Visible', 'off', 'MenuBar', 'none', 'ToolBar', 'none', 'Units', 'normalized', 'Position', [0.25 0.5 0.5 0.25]);
            f.Units = 'pixels';
            fprintf('figure=%s\n', mat2str(f.Position));
            movegui(f, 'center');
            fprintf('centred=%s\n', mat2str(f.Position));
            f.Position = [1300 300 300 200];
            movegui(f, 'southwest');
            fprintf('second=%s\n', mat2str(f.Position));
            """);

        string text = _output.NormalText;
        Assert.Contains("screen=[1 1 1000 500]\n", text);
        Assert.Contains("monitors=[1 1 1000 500;1001 101 800 600]\n", text);
        Assert.Contains("normalized=[0 0 1 1;1 0.2 0.8 1.2]\n", text);
        Assert.Contains("figure=[251 251 500 125]\n", text);

        // Centred by the whole window: R2025b's estimate of a bare window is eight pixels of border
        // all round and thirty-one of title bar over the top one.
        Assert.Contains("centred=[251 177 500 125]\n", text);

        // A figure on the second monitor is moved within the second monitor.
        Assert.Contains("second=[1009 109 300 200]\n", text);
    }
}
