using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Serialization;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// uicontextmenu / uimenu (M71 Wave E): a script defines a right-click menu, hangs it on objects
/// through their ContextMenu property, and hears picks through MenuSelectedFcn. The menu is a real
/// figure object — findable, parented, deletable — and its structure survives a save.
/// </summary>
[Collection("JG facade")]
public class MatlabUiMenuTests : IAsyncLifetime
{
    private readonly RecordingScriptOutput _output = new();
    private IScriptSession _session = null!;

    public Task InitializeAsync()
    {
        JG.Reset();
        _session = ((IScriptRepl)new MatlabScriptEngine()).CreateSession(
            new ScriptContext(_output, (_, _) => { }));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        ScriptEventQueue.Flush();
        await _session.DisposeAsync();
        JG.Reset();
    }

    private Task<ScriptRunResult> Exec(string code) =>
        _session.ExecuteAsync(code, sourceId: "", CancellationToken.None);

    private Task Drain() =>
        ((IGraphicsEventSession)_session).DrainGraphicsEventsAsync(null, CancellationToken.None);

    private async Task RunAsserting(string code)
    {
        ScriptRunResult result = await Exec(code);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    [Fact]
    public Task AMenuIsARealObject_ParentedFindableAndTyped() => RunAsserting("""
        figure; f = gcf;
        cm = uicontextmenu;
        assert(strcmp(get(cm, 'Type'), 'uicontextmenu'));
        assert(get(cm, 'Parent') == f);
        m1 = uimenu(cm, 'Text', 'Copy');
        m2 = uimenu(cm, 'Label', 'Paste', 'Separator', 'on', 'Enable', 'off');
        sub = uimenu(m1, 'Text', 'Deeper');
        assert(strcmp(get(m1, 'Type'), 'uimenu'));
        assert(strcmp(get(m1, 'Text'), 'Copy'));
        assert(strcmp(get(m2, 'Label'), 'Paste'));
        assert(strcmp(get(m2, 'Separator'), 'on'));
        assert(strcmp(get(m2, 'Enable'), 'off'));
        assert(strcmp(get(m1, 'Checked'), 'off'));
        assert(numel(get(cm, 'Children')) == 2);
        assert(numel(get(m1, 'Children')) == 1);
        assert(numel(findobj(f, 'Type', 'uimenu')) == 3);
        delete(m2);
        assert(numel(get(cm, 'Children')) == 1);
        """);

    [Fact]
    public Task TheContextMenuProperty_AssignsReadsBackAndClears_UnderBothSpellings() => RunAsserting("""
        p = plot(1:3);
        cm = uicontextmenu;
        set(p, 'ContextMenu', cm);
        assert(get(p, 'ContextMenu') == cm);
        assert(get(p, 'UIContextMenu') == cm);
        set(p, 'UIContextMenu', []);
        assert(isempty(get(p, 'ContextMenu')));
        ok = 0;
        try
            set(p, 'ContextMenu', p);
        catch err
            ok = contains(err.message, 'uicontextmenu');
        end
        assert(ok);
        """);

    [Fact]
    public Task TheMenubarForms_AreRefusedByName() => RunAsserting("""
        figure;
        ok = 0;
        try
            uimenu('Text', 'x');
        catch err
            ok = contains(err.message, 'menu bar');
        end
        assert(ok);
        """);

    [Fact]
    public async Task APickedEntry_RunsItsCallback_WithMatlabsActionEvent()
    {
        await Exec("""
            cm = uicontextmenu;
            m = uimenu(cm, 'Text', 'Copy', 'MenuSelectedFcn', ...
                @(src, event) fprintf('%s %d\n', event.EventName, src == m));
            """);
        FigureModel figure = (FigureModel)JG.Gca().Parent!;
        MenuItemModel item = figure.ContextMenus[0].Items[0];

        ScriptGraphicsCallbacks.NotifyMenuSelected(item);
        await Drain();

        Assert.Contains(_output.NormalLines, static line => line.Contains("Action 1"));
    }

    [Fact]
    public async Task TheOpeningCallback_HearsTheContextObjectAndTheSpot()
    {
        await Exec("""
            p = plot(1:3);
            cm = uicontextmenu;
            set(p, 'ContextMenu', cm);
            set(cm, 'ContextMenuOpeningFcn', @(src, event) fprintf('%s ctx%d at %g %g\n', ...
                event.EventName, event.ContextObject == p, event.Location));
            """);
        AxesModel axes = JG.Gca();
        PlotObject plot = axes.Plots[0];

        ContextMenuModel? menu = ScriptGraphicsCallbacks.ResolveContextMenu(plot);
        Assert.NotNull(menu);
        ScriptGraphicsCallbacks.NotifyContextMenuOpening(menu, plot, (120, 80));
        await Drain();

        Assert.Contains(_output.NormalLines,
            static line => line.Contains("ContextMenuOpening ctx1 at 120 80"));
    }

    [Fact]
    public async Task AnUnassignedObject_ResolvesNoMenu()
    {
        await Exec("p = plot(1:3); cm = uicontextmenu;");
        Assert.Null(ScriptGraphicsCallbacks.ResolveContextMenu(JG.Gca().Plots[0]));
    }

    [Fact]
    public async Task MenuStructureSurvivesASave_CallbacksStayScriptSide()
    {
        await Exec("""
            figure;
            cm = uicontextmenu;
            m1 = uimenu(cm, 'Text', 'Copy', 'MenuSelectedFcn', @(s, e) disp('x'));
            m2 = uimenu(cm, 'Text', 'Paste', 'Separator', 'on', 'Checked', 'on', 'Enable', 'off');
            sub = uimenu(m1, 'Text', 'Deeper', 'ForegroundColor', 'r');
            """);
        FigureModel figure = (FigureModel)JG.Gca().Parent!;

        FigureModel restored = GraphFormat.Deserialize(GraphFormat.Serialize(figure));

        ContextMenuModel menu = Assert.Single(restored.ContextMenus);
        Assert.Equal(2, menu.Items.Count);
        Assert.Equal("Copy", menu.Items[0].Text);
        Assert.True(menu.Items[1].Separator);
        Assert.True(menu.Items[1].Checked);
        Assert.False(menu.Items[1].Enable);
        MenuItemModel nested = Assert.Single(menu.Items[0].Items);
        Assert.Equal("Deeper", nested.Text);
        Assert.Equal(Colors.Red, nested.ForegroundColor);
    }
}
