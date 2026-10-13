using JGraph.Api;
using JGraph.Core.Drawing;
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
/// Open items 47, 60, 65 and 87 (ADR 0230), where the parity fixture cannot reach: that the JGS
/// dialect keeps its tight plot box while the MATLAB dialect takes R2025b's, that the box grows
/// when its decorations need more than the default margins leave, what a saved figure keeps of
/// the margins; the arithmetic of a scrollable panel's and figure's layout; a sort and a drag
/// reported by the window, which the fixture cannot raise; and the JGS dialect's brackets of
/// objects, which stay lists. What a script reads is R2025b's (probes probe_87, probe_65,
/// probe_60b and probe_47; fixture oi_axes_box_styles_tables_scroll).
/// </summary>
[Collection("JG facade")]
public class OpenItemsBatch17Tests : IAsyncLifetime
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
        await _session.DisposeAsync();
        JgsHandleRegistry.Clear();
        JG.Reset();
    }

    private async Task Exec(string code)
    {
        ScriptRunResult result = await _session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    private async Task RunsJgs(string code)
    {
        ScriptRunResult result = await new JgsScriptEngine().RunAsync(code, new ScriptContext(_output, static (_, _) => { }), default);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    private Task Drain() =>
        ((IGraphicsEventSession)_session).DrainGraphicsEventsAsync(null, CancellationToken.None);

    private static FigureModel Figure() =>
        JG.TryGetFigure(Assert.Single(JG.FigureNumbers), out FigureModel figure)
            ? figure
            : throw new InvalidOperationException("no figure");

    private static IEnumerable<UiObject> Everything(IUiContainer holder)
    {
        foreach (UiObject component in holder.Components)
        {
            yield return component;
            if (component is IUiContainer inner)
            {
                foreach (UiObject deeper in Everything(inner))
                {
                    yield return deeper;
                }
            }
        }
    }

    private static T Tagged<T>(string tag)
        where T : UiObject => Everything(Figure()).OfType<T>().First(c => c.Tag == tag);

    private bool Printed(string line) => _output.NormalLines.Contains(line);

    // --- item 87: the plot box -------------------------------------------------------------------

    [Fact]
    public async Task AMatlabAxesKeepsR2025bsPlotBox_AndAJgsAxesItsTightOne()
    {
        await Exec("f = figure('Visible', 'off', 'Position', [100 100 1120 840]); ax = axes(f); plot(ax, 1:3);");
        AxesModel matlab = Assert.Single(JG.Gcf().Axes);
        Assert.Equal(AxesModel.MatlabPlotBoxMargins, matlab.PlotBoxMargins);
        Assert.True(matlab.KeepsPlotBoxMargins);

        var context = new RecordingRenderContext(new Size2D(1120, 840));
        new FigureRenderer().Render(JG.Gcf(), context, Theme.Light);
        AxesLayoutSnapshot layout = matlab.LastLayout!.Value;
        Rect2D box = layout.Normalize(layout.PlotAreaPx);
        Assert.Equal(0.13, box.X, 3);
        Assert.Equal(0.075, box.Y, 3);
        Assert.Equal(0.775, box.Width, 3);
        Assert.Equal(0.815, box.Height, 3);

        JG.Reset();
        await RunsJgs("let f = figure('Visible', 'off'); let ax = axes(f); plot(ax, [1, 2, 3]);");
        AxesModel jgs = Assert.Single(JG.Gcf().Axes);
        Assert.Null(jgs.PlotBoxMargins);
        Assert.False(jgs.KeepsPlotBoxMargins);
    }

    [Fact]
    public void TheMarginsGrowOnlyWhereTheDecorationsNeedMore()
    {
        var axes = new AxesModel { PlotBoxMargins = AxesModel.MatlabPlotBoxMargins };
        var cell = new Rect2D(0, 0, 1000, 500);

        // Small decorations: the default margins stand (130 left, 37.5 top, 95 right, 55 bottom).
        Thickness kept = axes.MarginsWithin(new Thickness(40, 10, 10, 30), cell);
        Assert.Equal(130, kept.Left, 6);
        Assert.Equal(37.5, kept.Top, 6);
        Assert.Equal(95, kept.Right, 6);
        Assert.Equal(55, kept.Bottom, 6);

        // A tall title and a wide label push their own sides out and leave the others alone.
        Thickness grown = axes.MarginsWithin(new Thickness(200, 60, 10, 30), cell);
        Assert.Equal(200, grown.Left, 6);
        Assert.Equal(60, grown.Top, 6);
        Assert.Equal(95, grown.Right, 6);
        Assert.Equal(55, grown.Bottom, 6);

        // A pinned plot box, a uiaxes and an axes without margins are left to the decorations.
        axes.InnerTarget = new Rect2D(0.2, 0.2, 0.5, 0.5);
        Assert.Equal(new Thickness(40, 10, 10, 30), axes.MarginsWithin(new Thickness(40, 10, 10, 30), cell));
        axes.InnerTarget = null;
        axes.PositionIsOuter = true;
        Assert.Equal(new Thickness(40, 10, 10, 30), axes.MarginsWithin(new Thickness(40, 10, 10, 30), cell));
        axes.PositionIsOuter = false;
        axes.PlotBoxMargins = null;
        Assert.Equal(new Thickness(40, 10, 10, 30), axes.MarginsWithin(new Thickness(40, 10, 10, 30), cell));
    }

    [Fact]
    public async Task ASavedFigureKeepsThePlotBoxMargins()
    {
        await Exec("f = figure('Visible', 'off'); ax = axes(f); plot(ax, 1:3);");
        FigureModel figure = JG.Gcf();
        string json = JGraph.Serialization.GraphFormat.Serialize(figure);
        FigureModel loaded = JGraph.Serialization.GraphFormat.Deserialize(json);
        Assert.Equal(AxesModel.MatlabPlotBoxMargins, Assert.Single(loaded.Axes).PlotBoxMargins);
    }

    // --- item 65: arrays of objects --------------------------------------------------------------

    [Fact]
    public async Task AJgsBracketOfObjectsStaysAList()
    {
        await RunsJgs("""
            let s = uistyle('FontWeight', 'bold');
            let a = [s, s];
            print(numel(a));
            print(class(a[0]));
            """);
        Assert.True(Printed("2"), _output.NormalText);
        Assert.True(Printed("matlab.ui.style.Style"), _output.NormalText);
    }

    [Fact]
    public async Task AnObjectArrayIsAValue_SharedUntilWritten()
    {
        await Exec("""
            s = uistyle('FontWeight', 'bold');
            a = [s uistyle('FontName', 'Arial')];
            b = a;
            b(1).FontWeight = 'normal';
            fprintf('%s %s %s %d %d\n', a(1).FontWeight, b(1).FontWeight, s.FontWeight, numel(a), numel(b));
            c = a(2);
            c.FontName = 'Courier';
            fprintf('%s %s\n', a(2).FontName, c.FontName);
            a(4) = s;
            fprintf('%d %s %s\n', numel(a), class(a(3)), mat2str(isempty(a(3).FontWeight)));
            """);
        Assert.True(Printed("bold normal bold 2 2"), _output.NormalText);
        Assert.True(Printed("Arial Courier"), _output.NormalText);
        Assert.True(Printed("4 matlab.ui.style.Style true"), _output.NormalText);
    }

    // --- item 60: a sort and a drag reported by the window --------------------------------------

    [Fact]
    public async Task ASortReportedByTheWindowReordersTheRows_AndTellsDisplayDataChangedFcn()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            t = uitable(f, 'Tag', 't', 'Data', [3 30; 1 10; 2 20], 'ColumnSortable', true, ...
                'DisplayDataChangedFcn', @(src, e) fprintf('changed %s %s %d %d %s %s ds=%s\n', class(e), e.Interaction, ...
                    e.InteractionColumn, e.InteractionDisplayColumn, strjoin(e.DisplayColumnName, ','), e.InteractionVariable, mat2str(e.DisplaySelection)));
            t.Selection = [1 1];
            """);
        UiTableModel table = Tagged<UiTableModel>("t");

        ScriptGraphicsCallbacks.NotifyComponent(table, "sorted", new[] { 0, 1 });
        await Drain();
        Assert.True(Printed("changed matlab.ui.eventdata.DisplayDataChangedData sort 1 1 1,2  ds=[3 1]"), _output.NormalText);
        Assert.Equal("1", table.Content.At(0, 0).Text);
        Assert.Equal("3", table.Content.At(2, 0).Text);
        await Exec("fprintf('order %s dd=%s sel=%s ds=%s\\n', mat2str(t.DisplayRowOrder), mat2str(t.DisplayData(:, 1)'), mat2str(t.Selection), mat2str(t.DisplaySelection));");
        Assert.True(Printed("order [2 3 1] dd=[1 2 3] sel=[1 1] ds=[3 1]"), _output.NormalText);

        // Falling, then a column dragged to the front.
        ScriptGraphicsCallbacks.NotifyComponent(table, "sorted", new[] { 0, -1 });
        await Drain();
        Assert.Equal("3", table.Content.At(0, 0).Text);
        ScriptGraphicsCallbacks.NotifyComponent(table, "rearranged", new[] { 1, 0 });
        await Drain();
        Assert.True(Printed("changed matlab.ui.eventdata.DisplayDataChangedData rearrange 2 1 2,1  ds=[1 2]"), _output.NormalText);
        await Exec("fprintf('columns %s dd=%s\\n', mat2str(t.DisplayColumnOrder), mat2str(t.DisplayData(1, :)));");
        Assert.True(Printed("columns [2 1] dd=[30 3]"), _output.NormalText);

        // An edit and a selection made in the window name display cells; the data they reach is the data's.
        ScriptGraphicsCallbacks.NotifyComponent(table, "celledit", new object[] { 0, 1, "7" });
        await Drain();
        await Exec("fprintf('edited %s\\n', mat2str(t.Data(1, :)));");
        Assert.True(Printed("edited [7 30]"), _output.NormalText);
    }

    // --- item 47: scrolling ---------------------------------------------------------------------

    [Fact]
    public void AContainersMeasureIsR2025bs()
    {
        // probe_47's figure: 300 by 200 with a label reaching 500 across and 320 up.
        UiScrollMeasure figure = UiScrolling.Measure(new Size2D(300, 200), 500, 320);
        Assert.True(figure.Vertical);
        Assert.True(figure.Horizontal);
        Assert.Equal(new Size2D(284, 184), figure.Viewport);
        Assert.Equal(216, figure.MaxX);
        Assert.Equal(136, figure.MaxY);

        // Nothing beyond the inner area: no bars, nothing to scroll.
        UiScrollMeasure fits = UiScrolling.Measure(new Size2D(198, 87), 150, 30);
        Assert.False(fits.Vertical);
        Assert.False(fits.Horizontal);
        Assert.Equal(0, fits.MaxX);
        Assert.Equal(0, fits.MaxY);

        // A bar along the bottom takes room the height then overflows.
        UiScrollMeasure across = UiScrolling.Measure(new Size2D(198, 87), 400, 30);
        Assert.True(across.Horizontal);
        Assert.True(across.Vertical);
        Assert.Equal(new Size2D(182, 71), across.Viewport);
    }

    [Fact]
    public async Task AScrollablePanelAndFigureMoveTheirChildrenUnderTheViewport()
    {
        await Exec("""
            uf = uifigure('Visible', 'off', 'Position', [100 100 300 200], 'Scrollable', 'on');
            far = uilabel(uf, 'Tag', 'far', 'Position', [400 300 100 20]);
            p = uipanel(uf, 'Tag', 'p', 'Position', [10 10 200 100], 'Scrollable', 'on', 'BorderType', 'none');
            above = uilabel(p, 'Tag', 'above', 'Position', [10 150 100 20]);
            """);
        FigureModel figure = Figure();
        UiLayoutResult atBottom = UiLayout.Compute(UiFrame.Take(figure), new Size2D(300, 200));
        UiScrollPlacement scroll = atBottom.FigureScroll!.Value;
        Assert.Equal(0, scroll.ScrollX, 6);
        Assert.Equal(0, scroll.ScrollY, 6); // a figure starts at its top, as a panel does (fixture oi_axes_box_styles_tables_scroll)
        Assert.NotNull(scroll.VerticalBar);
        Assert.NotNull(scroll.HorizontalBar);
        UiComponentPlacement farLabel = atBottom.Components.Single(c => c.Component.Source.Tag == "far");
        Assert.Equal(400 - 1, farLabel.Box.X, 6);

        // The panel starts at its top: the label above its inner area shows at the top of the viewport.
        UiPanelPlacement panel = Assert.Single(atBottom.Panels);
        Assert.NotNull(panel.Viewport);
        Assert.Equal(0, panel.ScrollY, 6);
        UiComponentPlacement aboveLabel = atBottom.Components.Single(c => c.Component.Source.Tag == "above");
        Assert.Equal(panel.Viewport!.Value.Y + 1, aboveLabel.Box.Y, 6); // the extent counts y + h, the placement y - 1 + h: R2025b's own pixel

        await Exec("scroll(uf, 'bottom'); scroll(uf, 'right'); scroll(p, 'bottom'); fprintf('loc %s %s\\n', mat2str(uf.ScrollableViewportLocation), mat2str(p.ScrollableViewportLocation));");
        Assert.True(Printed("loc [217 1] [1 1]"), _output.NormalText);
        UiLayoutResult atTop = UiLayout.Compute(UiFrame.Take(figure), new Size2D(300, 200));
        Assert.Equal(136, atTop.FigureScroll!.Value.ScrollY, 6);
        Assert.Equal(216, atTop.FigureScroll!.Value.ScrollX, 6);
        Assert.Equal(400 - 1 - 216, atTop.Components.Single(c => c.Component.Source.Tag == "far").Box.X, 6);
        UiPanelPlacement scrolledPanel = Assert.Single(atTop.Panels);
        Assert.Equal(scrolledPanel.Content.Height - scrolledPanel.Viewport!.Value.Height, scrolledPanel.ScrollY, 6);
    }

    [Fact]
    public async Task ABarOrTheWheelMovesAPanel_AndAWrittenLocationReadsBackAsWritten()
    {
        await Exec("""
            uf = uifigure('Visible', 'off', 'Position', [100 100 400 300]);
            p = uipanel(uf, 'Tag', 'p', 'Position', [10 10 200 100], 'Scrollable', 'on', 'BorderType', 'none');
            uilabel(p, 'Position', [10 150 100 20]);
            p.ScrollableViewportLocation = [1 50];
            fprintf('written %s\n', mat2str(p.ScrollableViewportLocation));
            """);
        Assert.True(Printed("written [1 50]"), _output.NormalText);

        // The window's bar reports pixels from the left and the top of the content.
        UiPanelModel panel = Tagged<UiPanelModel>("p");
        ScriptGraphicsCallbacks.NotifyComponent(panel, "value", new[] { 0.0, 10.0 });
        await Drain();
        Assert.Equal(10, panel.ScrollY, 6);
        Assert.Null(panel.ViewportLocationWritten);
        await Exec("fprintf('moved %s\\n', mat2str(p.ScrollableViewportLocation));");
        double toScroll = UiScrolling.MeasureContainer(panel).MaxY; // the label reaches 170 up; the viewport is the inner area less its title band
        Assert.True(Printed($"moved [1 {1 + toScroll - 10}]"), _output.NormalText);
    }
}
