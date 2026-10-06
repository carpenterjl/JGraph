using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// U9 of the app-building plan (ADR 0207): knobs, switches, gauges, the lamp, the two pickers, trees
/// and their nodes, styles, a component's context menu, focus and scroll. What a script can ask
/// headless is held by the parity fixtures (<c>u9_*</c>); these are the parts that need a person —
/// a knob turned, a switch flipped, a date typed, a colour chosen, a node picked, renamed, expanded
/// or ticked — told through the seams the window uses, and what a script's verbs leave on the model.
/// </summary>
[Collection("JG facade")]
public class UiComponentsU9Tests : IAsyncLifetime
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
        JG.Reset();
    }

    private async Task Exec(string code)
    {
        ScriptRunResult result = await _session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
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

    // --- knobs, switches, pickers --------------------------------------------------------------------

    [Fact]
    public async Task AKnobTurnedTellsItsValueChangedFcn_AndADragItsValueChangingFcn()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            k = uiknob(f, 'Tag', 'k', 'Value', 20, 'Limits', [0 50], ...
                'ValueChangingFcn', @(src, e) fprintf('turning %g while %g\n', e.Value, src.Value), ...
                'ValueChangedFcn', @(src, e) fprintf('turned %s %g<-%g %s\n', class(e), e.Value, e.PreviousValue, e.EventName));
            """);
        UiKnobModel knob = Tagged<UiKnobModel>("k");

        ScriptGraphicsCallbacks.NotifyComponent(knob, "changing", 25.0);
        ScriptGraphicsCallbacks.NotifyComponent(knob, "changing", 35.0);
        ScriptGraphicsCallbacks.NotifyComponent(knob, "value", 35.0);
        await Drain();

        Assert.Equal(35, knob.Value);
        Assert.True(Printed("turning 35 while 20"), _output.NormalText);
        Assert.False(Printed("turning 25 while 20"), "a drag's changing events coalesce");
        Assert.True(Printed("turned matlab.ui.eventdata.ValueChangedData 35<-20 ValueChanged"), _output.NormalText);

        // A turn past the limits lands on them.
        ScriptGraphicsCallbacks.NotifyComponent(knob, "value", 99.0);
        await Drain();
        Assert.Equal(50, knob.Value);
    }

    [Fact]
    public async Task ASwitchFlippedAndADiscreteKnobSetTellTheirItem_OrItsData()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            s = uiswitch(f, 'Tag', 's', 'ValueChangedFcn', @(src, e) fprintf('switch %s<-%s index %d\n', e.Value, e.PreviousValue, src.ValueIndex));
            d = uiknob(f, 'discrete', 'Tag', 'd', 'Items', {'Cold', 'Warm', 'Hot'}, 'ItemsData', [5 20 40], ...
                'ValueChangedFcn', @(src, e) fprintf('dial %g<-%g %s\n', e.Value, e.PreviousValue, src.Items{src.ValueIndex}));
            """);

        ScriptGraphicsCallbacks.NotifyComponent(Tagged<UiSwitchModel>("s"), "value", 1);
        ScriptGraphicsCallbacks.NotifyComponent(Tagged<UiDiscreteKnobModel>("d"), "value", 2);
        await Drain();

        Assert.True(Tagged<UiSwitchModel>("s").IsOn);
        Assert.Equal([2], Tagged<UiDiscreteKnobModel>("d").Selected);
        Assert.True(Printed("switch On<-Off index 2"), _output.NormalText);
        Assert.True(Printed("dial 40<-5 Hot"), _output.NormalText);
    }

    [Fact]
    public async Task ADatePickerTakesADateTypedInItsFormat_OrPicked_AndKeepsItsDateWhenTheTextIsNone()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            p = uidatepicker(f, 'Tag', 'p', 'DisplayFormat', 'dd/MM/uuuu', 'Value', datetime(2024, 1, 15), ...
                'ValueChangedFcn', @(src, e) fprintf('date %s %s<-%s\n', class(e.Value), char(string(e.Value)), char(string(e.PreviousValue))));
            """);
        UiDatePickerModel picker = Tagged<UiDatePickerModel>("p");
        double picked = UiDatePickerModel.FromDateTime(new DateTime(2024, 3, 5));

        ScriptGraphicsCallbacks.NotifyComponent(picker, "value", "02/02/2024");
        await Drain();
        Assert.Equal(UiDatePickerModel.FromDateTime(new DateTime(2024, 2, 2)), picker.ValueDays);
        Assert.True(Printed("date datetime 02/02/2024<-15/01/2024"), _output.NormalText);

        ScriptGraphicsCallbacks.NotifyComponent(picker, "value", picked);
        await Drain();
        Assert.Equal(picked, picker.ValueDays);
        Assert.Equal("05/03/2024", picker.DisplayText);
        Assert.True(Printed("date datetime 05/03/2024<-02/02/2024"), _output.NormalText);

        // Text that is no date is put back; a cleared field is NaT.
        ScriptGraphicsCallbacks.NotifyComponent(picker, "value", "nonsense");
        await Drain();
        Assert.Equal((picked, "05/03/2024"), (picker.ValueDays, picker.DisplayText));
        ScriptGraphicsCallbacks.NotifyComponent(picker, "value", "");
        await Drain();
        Assert.Null(picker.ValueDays);
        Assert.True(Printed("date datetime NaT<-05/03/2024"), _output.NormalText);
    }

    [Fact]
    public async Task AColourChosenReachesTheColorPickersValueChangedFcnAsATriplet()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            c = uicolorpicker(f, 'Tag', 'c', 'ValueChangedFcn', @(src, e) fprintf('colour %s <- %s\n', mat2str(e.Value), mat2str(e.PreviousValue)));
            """);

        ScriptGraphicsCallbacks.NotifyComponent(Tagged<UiColorPickerModel>("c"), "value", new UiColor(0, 0, 1));
        await Drain();

        Assert.Equal(new UiColor(0, 0, 1), Tagged<UiColorPickerModel>("c").Value);
        Assert.True(Printed("colour [0 0 1] <- [1 0 0]"), _output.NormalText);
    }

    // --- trees -----------------------------------------------------------------------------------

    private const string TreeScript = """
        f = uifigure('Visible', 'off');
        t = uitree(f, 'Tag', 't', 'Editable', 'on', ...
            'SelectionChangedFcn', @(src, e) fprintf('selected %d:%s was %d\n', numel(e.SelectedNodes), e.SelectedNodes(1).Text, numel(e.PreviousSelectedNodes)), ...
            'NodeExpandedFcn', @(src, e) fprintf('expanded %s %s\n', class(e), e.Node.Text), ...
            'NodeCollapsedFcn', @(src, e) fprintf('collapsed %s\n', e.Node.Text), ...
            'NodeTextChangedFcn', @(src, e) fprintf('renamed %s<-%s is %s\n', e.Text, e.PreviousText, e.Node.Text), ...
            'ClickedFcn', @(src, e) fprintf('clicked %s level %d\n', class(e.InteractionInformation), e.InteractionInformation.Level), ...
            'DoubleClickedFcn', @(src, e) fprintf('twice on %s\n', e.InteractionInformation.Node.Text));
        a = uitreenode(t, 'Text', 'a');
        b = uitreenode(t, 'Text', 'b');
        c = uitreenode(a, 'Text', 'c');
        """;

    [Fact]
    public async Task ATreeTellsSelectionExpansionAndRenaming_AndWhatWasClicked()
    {
        await Exec(TreeScript);
        UiTreeModel tree = Tagged<UiTreeModel>("t");
        UiTreeNodeModel a = tree.Nodes[0];
        UiTreeNodeModel b = tree.Nodes[1];
        UiTreeNodeModel c = a.Nodes[0];

        ScriptGraphicsCallbacks.NotifyComponent(tree, "nodeselect", new[] { b });
        ScriptGraphicsCallbacks.NotifyComponent(tree, "nodeexpand", a);
        ScriptGraphicsCallbacks.NotifyComponent(tree, "nodecollapse", a);
        ScriptGraphicsCallbacks.NotifyComponent(tree, "nodetext", new object[] { c, "see" });
        ScriptGraphicsCallbacks.NotifyComponent(tree, "clicked", c);
        ScriptGraphicsCallbacks.NotifyComponent(tree, "doubleclicked", b);
        await Drain();

        Assert.Equal([b], tree.Selected);
        Assert.False(a.Expanded);
        Assert.Equal("see", c.Text);
        Assert.True(Printed("selected 1:b was 0"), _output.NormalText);
        Assert.True(Printed("expanded matlab.ui.eventdata.NodeExpandedData a"), _output.NormalText);
        Assert.True(Printed("collapsed a"), _output.NormalText);
        Assert.True(Printed("renamed see<-c is see"), _output.NormalText);
        Assert.True(Printed("clicked matlab.ui.eventdata.TreeInteraction level 2"), _output.NormalText);
        Assert.True(Printed("twice on b"), _output.NormalText);
    }

    [Fact]
    public async Task ASingleSelectTreeKeepsOneNode_AndANodeOfAnotherTreeIsIgnored()
    {
        await Exec(TreeScript + """
            u = uitree(f, 'Tag', 'u');
            x = uitreenode(u, 'Text', 'x');
            """);
        UiTreeModel tree = Tagged<UiTreeModel>("t");
        UiTreeNodeModel foreign = Tagged<UiTreeModel>("u").Nodes[0];

        ScriptGraphicsCallbacks.NotifyComponent(tree, "nodeselect", new[] { tree.Nodes[1], tree.Nodes[0] });
        ScriptGraphicsCallbacks.NotifyComponent(tree, "nodeexpand", foreign);
        await Drain();

        Assert.Equal([tree.Nodes[1]], tree.Selected);
        Assert.False(foreign.Expanded);
        Assert.True(Printed("selected 1:b was 0"), _output.NormalText);
        Assert.DoesNotContain("expanded", _output.NormalText);
    }

    [Fact]
    public async Task ATickOnACheckBoxTreeChecksTheBranch_AndTellsEveryListOfTheEvent()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            t = uitree(f, 'checkbox', 'Tag', 't', 'CheckedNodesChangedFcn', @(src, e) fprintf('checked %d leaves %d parents %d indeterminate %d was %d\n', ...
                numel(e.CheckedNodes), numel(e.LeafCheckedNodes), numel(e.ParentCheckedNodes), numel(e.IndeterminateCheckedNodes), numel(e.PreviousCheckedNodes)));
            a = uitreenode(t, 'Text', 'a');
            b = uitreenode(t, 'Text', 'b');
            c = uitreenode(a, 'Text', 'c');
            d = uitreenode(a, 'Text', 'd');
            """);
        var tree = Assert.IsType<UiCheckBoxTreeModel>(Tagged<UiTreeModel>("t"));
        UiTreeNodeModel a = tree.Nodes[0];
        UiTreeNodeModel c = a.Nodes[0];
        UiTreeNodeModel d = a.Nodes[1];

        // c alone: a is indeterminate. Then d: a's children are all in, so a is too.
        ScriptGraphicsCallbacks.NotifyComponent(tree, "nodecheck", new object[] { c, true });
        await Drain();
        Assert.Equal([c], tree.Checked);
        Assert.True(Printed("checked 1 leaves 1 parents 0 indeterminate 1 was 0"), _output.NormalText);

        ScriptGraphicsCallbacks.NotifyComponent(tree, "nodecheck", new object[] { d, true });
        await Drain();
        Assert.Equal(3, tree.Checked.Count);
        Assert.Contains(a, tree.Checked);
        Assert.True(Printed("checked 3 leaves 2 parents 1 indeterminate 0 was 1"), _output.NormalText);

        // Unticking the branch takes it all off.
        ScriptGraphicsCallbacks.NotifyComponent(tree, "nodecheck", new object[] { a, false });
        await Drain();
        Assert.Empty(tree.Checked);
        Assert.True(Printed("checked 0 leaves 0 parents 0 indeterminate 0 was 3"), _output.NormalText);
    }

    // --- styles, context menus, scroll ----------------------------------------------------------------

    [Fact]
    public async Task AddStyleLeavesARuleOnTheModel_AndRemoveStyleTakesItOff()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            t = uitable(f, 'Tag', 't', 'Data', magic(4));
            tr = uitree(f, 'Tag', 'tr');
            n = uitreenode(tr, 'Text', 'n');
            addStyle(t, uistyle('FontWeight', 'bold', 'BackgroundColor', 'yellow'), 'row', 2);
            addStyle(t, uistyle('FontColor', [1 0 0]), 'cell', [1 1; 3 4]);
            addStyle(tr, uistyle('FontAngle', 'italic'), 'node', n);
            """);
        UiTableModel table = Tagged<UiTableModel>("t");
        UiTreeModel tree = Tagged<UiTreeModel>("tr");

        Assert.Equal(2, table.Styles.Count);
        Assert.Equal((UiStyleTarget.Row, "bold"), (table.Styles[0].Target, table.Styles[0].Style.FontWeight));
        Assert.Equal([2], table.Styles[0].Indices);
        Assert.Equal(new UiColor(1, 1, 0), table.Styles[0].Style.BackgroundColor);
        Assert.Equal((UiStyleTarget.Cell, new UiColor(1, 0, 0)), (table.Styles[1].Target, table.Styles[1].Style.FontColor));
        Assert.Equal([1, 1, 3, 4], table.Styles[1].Indices);
        Assert.Equal((UiStyleTarget.Node, "italic"), (Assert.Single(tree.Styles).Target, tree.Styles[0].Style.FontAngle));
        Assert.Same(tree.Nodes[0], Assert.Single(tree.Styles[0].Nodes));

        await Exec("removeStyle(t, 1); removeStyle(tr);");
        Assert.Equal(UiStyleTarget.Cell, Assert.Single(table.Styles).Target);
        Assert.Empty(tree.Styles);
    }

    [Fact]
    public async Task AComponentsContextMenuIsResolvedForTheWindow_AndADeletedOneIsNone()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            cm = uicontextmenu(f);
            uimenu(cm, 'Text', 'Hello');
            b = uibutton(f, 'Tag', 'b', 'ContextMenu', cm);
            k = uiknob(f, 'Tag', 'k');
            """);

        ContextMenuModel? menu = ScriptGraphicsCallbacks.ResolveContextMenu(Tagged<UiButtonModel>("b"));
        Assert.NotNull(menu);
        Assert.Same(Assert.Single(Figure().ContextMenus), menu);
        Assert.Null(ScriptGraphicsCallbacks.ResolveContextMenu(Tagged<UiKnobModel>("k")));

        await Exec("delete(cm);");
        Assert.Null(ScriptGraphicsCallbacks.ResolveContextMenu(Tagged<UiButtonModel>("b")));
    }

    [Fact]
    public async Task ScrollLeavesARequestOnTheModel_ForTheWindowToHonour()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            t = uitree(f, 'Tag', 't');
            a = uitreenode(t, 'Text', 'a');
            b = uitreenode(t, 'Text', 'b');
            l = uilistbox(f, 'Tag', 'l', 'Items', {'one', 'two', 'three'});
            scroll(t, b);
            scroll(l, 'bottom');
            scroll(l, 'top');
            """);
        UiTreeModel tree = Tagged<UiTreeModel>("t");
        UiListBoxModel list = Tagged<UiListBoxModel>("l");

        Assert.Equal(1, tree.ScrollRequests);
        Assert.Same(tree.Nodes[1], tree.ScrollTarget);
        Assert.Equal(2, list.ScrollRequests);
        Assert.Equal("top", Assert.IsType<string>(list.ScrollTarget));
    }

    [Fact]
    public async Task ASwitchKeepsItsShapeWhateverRectangleItIsGiven_AndAKnobStaysRound()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            s = uiswitch(f, 'Tag', 's', 'Position', [10 10 90 90]);
            k = uiknob(f, 'Tag', 'k', 'Position', [10 10 80 40]);
            """);

        Rect2D track = Tagged<UiSwitchModel>("s").Position;
        Assert.Equal((90, 40), (track.Width, track.Height));
        Rect2D dial = Tagged<UiKnobModel>("k").Position;
        Assert.Equal((40, 40), (dial.Width, dial.Height));
    }
}
