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

    private readonly RecordingScriptOutput _output = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jgraph-u10-" + Guid.NewGuid().ToString("N"));
    private JgsReplSession _session = null!;

    public Task InitializeAsync()
    {
        JG.Reset();
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "U10TGauge.m"), Gauge);
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
