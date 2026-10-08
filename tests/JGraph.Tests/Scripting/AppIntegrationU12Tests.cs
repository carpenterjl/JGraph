using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// U12 of the app-building plan (ADR 0211): what the Workspace pane shows for a component, an axes
/// toolbar button's press reaching its own callback, and the root's pointer. The headless surface is
/// held by the parity fixture <c>u12_props</c>; these need the pane's projection, the window's press
/// seam, or a pointer that is not the user's mouse.
/// </summary>
[Collection("JG facade")]
public class AppIntegrationU12Tests : IAsyncLifetime
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
        UiScreen.SetTestPointer(null);
        ScriptEventQueue.Flush();
        ScriptEventQueue.Flush();
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

    private ScriptVariable Variable(string name) => _session.GetVariables().Single(v => v.Name == name);

    private bool Printed(string line) => _output.NormalLines.Contains(line);

    [Fact]
    public async Task TheWorkspacePaneNamesAComponentsClass_ButLeavesNumbersAndFigureNumbersAlone()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            b = uibutton(f);
            g = figure('Visible', 'off');
            p = plot([1 2; 3 4]);
            mixed = [b; p(1)];
            gone = uibutton(f); delete(gone);
            near = b + 1000;
            k = 1;
            """);

        ScriptVariable button = Variable("b");
        Assert.Equal("double", button.Type); // what class(b) says
        Assert.Matches(@"^1×1 Button \(1\d+\.5\)$", button.DisplayValue);
        Assert.Matches(@"^\d×\d Line$", Variable("p").DisplayValue);
        Assert.Equal("2×1 graphics array", Variable("mixed").DisplayValue);

        // A figure's number, a deleted handle and a number that is no handle are numbers.
        Assert.DoesNotContain("Figure", Variable("g").DisplayValue);
        Assert.DoesNotContain("Button", Variable("gone").DisplayValue);
        Assert.DoesNotContain("Button", Variable("near").DisplayValue);
        Assert.DoesNotContain("Figure", Variable("k").DisplayValue);
    }

    [Fact]
    public async Task AToolbarPushButtonPressedRunsItsButtonPushedFcn_WithR2025bsEventData()
    {
        await Exec("""
            f = figure('Visible', 'off');
            ax = axes(f, 'Tag', 'plotted');
            tb = axtoolbar(ax);
            pb = axtoolbarbtn(tb, 'push', 'Tag', 'pb', 'ButtonPushedFcn', ...
                @(src, e) fprintf('pushed %s %s %s %d\n', class(e), e.EventName, get(e.Axes, 'Tag'), isequal(e.Source, src)));
            """);
        AxesToolbarButtonModel button = ToolbarButton("pb");

        ScriptGraphicsCallbacks.NotifyToolbarButton(button, previous: false);
        await Drain();

        Assert.True(Printed("pushed matlab.graphics.controls.eventdata.ButtonPushedEventData ButtonPushed plotted 1"), _output.NormalText);
    }

    [Fact]
    public async Task AToolbarStateButtonFlippedRunsItsValueChangedFcn_WithTheValueBeforeAndAfter()
    {
        await Exec("""
            f = figure('Visible', 'off');
            tb = axtoolbar(axes(f));
            sb = axtoolbarbtn(tb, 'state', 'Tag', 'sb', 'ValueChangedFcn', ...
                @(src, e) fprintf('changed %s %s %s<-%s\n', class(e), e.EventName, e.Value, e.PreviousValue));
            """);
        AxesToolbarButtonModel button = ToolbarButton("sb");

        button.Value = true; // the window flips a state button before it reports the press
        ScriptGraphicsCallbacks.NotifyToolbarButton(button, previous: false);
        await Drain();

        Assert.True(Printed("changed matlab.graphics.controls.eventdata.ValueChangedEventData ValueChanged on<-off"), _output.NormalText);
    }

    [Fact]
    public async Task APressOfAButtonWithNoCallbackQueuesNothing()
    {
        await Exec("""
            f = figure('Visible', 'off');
            tb = axtoolbar(axes(f));
            pb = axtoolbarbtn(tb, 'push', 'Tag', 'pb');
            """);

        ScriptGraphicsCallbacks.NotifyToolbarButton(ToolbarButton("pb"), previous: false);

        Assert.Equal(0, ScriptEventQueue.Count);
    }

    [Fact]
    public async Task ThePointerLocationIsReadInTheRootsUnits_AndAWriteMovesIt()
    {
        UiScreen.SetTestPointer(new Point2D(101, 201));
        Rect2D screen = UiScreen.Primary;

        await Exec("""
            r = groot;
            px = r.PointerLocation;
            r.Units = 'normalized';
            nx = r.PointerLocation;
            r.PointerLocation = [0.5 0.25];
            r.Units = 'pixels';
            moved = r.PointerLocation;
            """);

        Assert.Equal("[101, 201]", Compact(Variable("px").DisplayValue));
        Assert.Equal(new Point2D(1 + (0.5 * screen.Width), 1 + (0.25 * screen.Height)), UiScreen.Pointer);
        double[] normalized = Assert.IsType<double[]>(Variable("nx").RawValue);
        Assert.Equal(100 / screen.Width, normalized[0], 12);
        Assert.Equal(200 / screen.Height, normalized[1], 12);
    }

    private static string Compact(string display) => "[" + string.Join(", ",
        display.Split([' ', '\r', '\n', '[', ']', ','], StringSplitOptions.RemoveEmptyEntries)) + "]";

    private static AxesToolbarButtonModel ToolbarButton(string tag) => // the tag names it for the reader
        JG.TryGetFigure(Assert.Single(JG.FigureNumbers), out FigureModel figure)
            ? figure.Axes.SelectMany(a => a.Toolbar.Buttons).First() // the newest: axtoolbarbtn adds at the left
            : throw new InvalidOperationException("no figure");
}
