using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// U10 of the app-building plan (ADR 0209): custom components, classes written under
/// <c>matlab.ui.componentcontainer.ComponentContainer</c>. What a script sees is held by the parity
/// fixtures (<c>u10_component</c>, <c>u10_forms</c>); these hold the parts around them - the area
/// the window draws, the order owed updates run in, what a failure reports, and the cause a
/// failing setup carries. A saved figure is <c>UiComponentSerializationTests</c>'.
/// </summary>
[Collection("JG facade")]
public class ComponentContainerU10Tests : IAsyncLifetime
{
    private const string Gauge = """
        classdef U10TGauge < matlab.ui.componentcontainer.ComponentContainer
            properties
                Value (1,1) double = 0
                Fail = ''
            end
            properties (Access = private)
                Grid
                Field
            end
            events (HasCallbackProperty, NotifyAccess = public)
                ValueChanged
            end
            methods (Access = protected)
                function setup(comp)
                    if strcmp(comp.Fail, 'never'), end
                    if evalin('base', 'exist(''failSetup'', ''var'') && failSetup')
                        error('u10t:setup', 'setup failed');
                    end
                    comp.Grid = uigridlayout(comp, [1 1], 'Padding', 0);
                    comp.Field = uieditfield(comp.Grid, 'numeric');
                end
                function update(comp)
                    if strcmp(comp.Fail, 'update')
                        error('u10t:update', 'update failed');
                    end
                    comp.Field.Value = comp.Value;
                    order = evalin('base', 'order');
                    assignin('base', 'order', [order comp.Value]);
                end
            end
        end
        """;

    /// <summary>A component whose own button writes its own property, as App Designer's component callbacks do (W16).</summary>
    private const string Counter = """
        classdef U10TCounter < matlab.ui.componentcontainer.ComponentContainer
            properties
                Value (1,1) double = 0
            end
            properties (Access = private)
                Grid
                Button
            end
            methods (Access = private)
                function pushed(comp, ~)
                    comp.Value = comp.Value + 1;
                end
            end
            methods (Access = protected)
                function setup(comp)
                    comp.Grid = uigridlayout(comp, [1 1], 'Padding', 0);
                    comp.Button = uibutton(comp.Grid, 'push', 'Text', 'Count');
                    comp.Button.ButtonPushedFcn = @(s, e) pushed(comp, e);
                end
                function update(comp)
                    comp.Button.Text = sprintf('%d', comp.Value);
                end
            end
        end
        """;

    private readonly RecordingScriptOutput _output = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jgraph-u10-" + Guid.NewGuid().ToString("N"));
    private JgsReplSession _session = null!;

    public Task InitializeAsync()
    {
        JG.Reset();
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "U10TGauge.m"), Gauge);
        File.WriteAllText(Path.Combine(_folder, "U10TCounter.m"), Counter);
        _session = Assert.IsType<JgsReplSession>(((IScriptRepl)new MatlabScriptEngine()).CreateSession(
            new ScriptContext(_output, (_, _) => { }, _folder)));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        ScriptEventQueue.Flush();
        await _session.DisposeAsync();
        JG.Reset();
        Directory.Delete(_folder, recursive: true);
    }

    private async Task Exec(string code)
    {
        ScriptRunResult result = await _session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    /// <summary>What <c>disp</c> shows of an expression, trimmed.</summary>
    private async Task<string> Show(string expression)
    {
        int before = _output.NormalText.Length;
        await Exec($"disp({expression});");
        return _output.NormalText[before..].Trim();
    }

    private static IEnumerable<FigureModel> Figures() =>
        JG.FigureNumbers.Select(n => JG.TryGetFigure(n, out FigureModel f) ? f : null).OfType<FigureModel>();

    private static UiComponentContainerModel TheArea() =>
        Figures().SelectMany(f => f.Components).OfType<UiComponentContainerModel>().Single();

    [Fact]
    public async Task TheComponentIsABorderlessPanelHoldingWhatSetupBuilt()
    {
        await Exec("order = []; f = uifigure('Visible', 'off'); c = U10TGauge(f, 'Value', 4, 'Position', [10 20 200 30]);");

        UiComponentContainerModel area = TheArea();
        Assert.Equal("U10TGauge", area.ClassName);
        Assert.Equal("u10tgauge", area.TypeName);
        Assert.Equal(UiBorderType.None, area.BorderType);
        Assert.Equal(new Core.Primitives.Rect2D(10, 20, 200, 30), area.Position);
        Assert.False(area.InSetup);
        UiGridLayoutModel grid = Assert.IsType<UiGridLayoutModel>(Assert.Single(area.Components));
        Assert.Single(grid.Components);

        await Exec("drawnow;");
        Assert.Equal("4", await Show("mat2str(order)"));
    }

    [Fact]
    public async Task OwedUpdatesRunOnceEach_InTheOrderTheyWereOwed()
    {
        await Exec("order = []; f = uifigure('Visible', 'off'); a = U10TGauge(f, 'Value', 1); b = U10TGauge(f, 'Value', 2); drawnow;");
        await Exec("order = []; b.Value = 20; a.Value = 10; b.Value = 21; drawnow; drawnow;");
        Assert.Equal("[21 10]", await Show("mat2str(order)"));
    }

    /// <summary>
    /// A button inside a component whose callback writes the component's own property (W16, the acceptance
    /// set's Counter): the update it owes runs in the drain that ran the callback - R2025b runs it before the
    /// window is drawn again - not at the next drawnow. Read from the model, before any statement can drain.
    /// </summary>
    [Fact]
    public async Task AnUpdateOwedByAComponentsOwnCallbackRunsInTheSameDrain()
    {
        // A second session beside the first, as the shell has its languages' sessions from the start (made before
        // the app, since a new engine starts the shared registries afresh).
        await using JgsReplSession other = Assert.IsType<JgsReplSession>(((IScriptRepl)new MatlabScriptEngine()).CreateSession(
            new ScriptContext(new RecordingScriptOutput(), (_, _) => { }, _folder)));
        await Exec("order = []; f = uifigure('Visible', 'off'); c = U10TCounter(f); drawnow;");
        UiComponentContainerModel area = TheArea();
        UiGridLayoutModel grid = Assert.IsType<UiGridLayoutModel>(Assert.Single(area.Components));
        UiCaptionModel button = Assert.IsAssignableFrom<UiCaptionModel>(Assert.Single(grid.Components));
        Assert.Equal("0", button.Text.Joined);

        ScriptGraphicsCallbacks.NotifyComponent(button, "pushed");
        await _session.DrainGraphicsEventsAsync(null, CancellationToken.None);
        Assert.Equal("1", button.Text.Joined);
        Assert.Equal("1", await Show("mat2str(c.Value)"));

        // The shell's pump drains in its active session, which may not be the one that made the app (W16, found in
        // the window): the callback runs there all the same, and so does the update it owes.
        ScriptGraphicsCallbacks.NotifyComponent(button, "pushed");
        await other.DrainGraphicsEventsAsync(null, CancellationToken.None);
        Assert.Equal("2", await Show("mat2str(c.Value)")); // the callback ran in the other session's drain
        Assert.Equal("2", button.Text.Joined); // and so did the update it owed
    }

    [Fact]
    public async Task DeletingTheObjectTakesItsAreaOut_AndDeletingTheFigureDeletesTheObject()
    {
        await Exec("order = []; f = uifigure('Visible', 'off'); c = U10TGauge(f); delete(c); gone = isvalid(c);");
        Assert.Empty(Figures().SelectMany(f => f.Components).OfType<UiComponentContainerModel>());
        Assert.Equal("false", await Show("mat2str(gone)"));

        await Exec("c = U10TGauge(f); delete(f); gone = isvalid(c);");
        Assert.Equal("false", await Show("mat2str(gone)"));
    }

    [Fact]
    public async Task AFailingUpdateIsReported_AndTheNextDrainGoesOn()
    {
        await Exec("order = []; f = uifigure('Visible', 'off'); c = U10TGauge(f, 'Fail', 'update'); d = U10TGauge(f, 'Value', 7); drawnow;");
        Assert.Contains("Unable to execute 'update' method.", _output.ErrorText, StringComparison.Ordinal);
        Assert.Contains("update failed", _output.ErrorText, StringComparison.Ordinal);
        Assert.Equal("7", await Show("mat2str(order)"));
    }

    [Fact]
    public async Task AFailingCallbackIsReported_NotRaised()
    {
        await Exec("order = []; f = uifigure('Visible', 'off'); c = U10TGauge(f); c.ValueChangedFcn = @(s, e) error('u10t:cb', 'cb failed'); notify(c, 'ValueChanged'); ok = 1;");
        Assert.Contains("Error while evaluating U10TGauge ValueChangedFcn.", _output.ErrorText, StringComparison.Ordinal);
        Assert.Equal("1", await Show("mat2str(ok)"));
    }

    [Fact]
    public async Task AFailingSetupCarriesTheClasssErrorAsItsCause()
    {
        await Exec("""
            order = []; failSetup = true; f = uifigure('Visible', 'off');
            try
                c = U10TGauge(f);
            catch ME
                id = ME.identifier; inner = ME.cause{1}.identifier; said = ME.cause{1}.message;
            end
            failSetup = false;
            """);
        Assert.Equal("MATLAB:ui:componentcontainer:ErrorWhileExecutingSetup", await Show("id"));
        Assert.Equal("u10t:setup", await Show("inner"));
        Assert.Equal("setup failed", await Show("said"));
    }
}
