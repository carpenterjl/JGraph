using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open items 51, 59, 63 and 64 (ADR 0236): the event data a component's callback receives, as
/// R2025b's window sessions recorded it (probes <c>u5w_events</c>, <c>u8w_events</c>,
/// <c>u9w_events</c>, <c>u9w_tree</c>) — the indices a ValueChangedData carries after its
/// EventName, the typed interaction classes with the click's place, a table edit's
/// DisplayDataChangedFcn, a context menu entry's MenuSelectedData — and the knob's outer
/// rectangle and the numeric field's refusal.
/// </summary>
[Collection("JG facade")]
public class OpenItemsBatch23Tests : IAsyncLifetime
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
        await _session.DisposeAsync();
        JgsHandleRegistry.Clear();
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

    // --- item 51: the indices beside a value, and the click's interaction --------------------------

    [Fact]
    public async Task AListsValueChangeNamesTheItemsPlaces_AfterTheEventName()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            lb = uilistbox(f, 'Tag', 'lb', 'Items', {'one', 'two', 'three'}, ...
                'ValueChangedFcn', @(s, e) fprintf('value %s>%s prev=%d now=%d order=%s\n', ...
                    e.PreviousValue, e.Value, e.PreviousValueIndex, e.ValueIndex, strjoin(fieldnames(e)', ',')));
            """);
        UiListBoxModel list = Tagged<UiListBoxModel>("lb");

        ScriptGraphicsCallbacks.NotifyComponent(list, "value", new[] { 1 });
        await Drain();

        Assert.True(
            Printed("value one>two prev=1 now=2 order=Value,PreviousValue,Source,EventName,PreviousValueIndex,ValueIndex"),
            _output.NormalText + _output.ErrorText);
    }

    [Fact]
    public async Task ADropDownSaysWhetherItWasTypedOrPicked()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            dd = uidropdown(f, 'Tag', 'dd', 'Editable', 'on', 'Items', {'red', 'green'}, ...
                'ValueChangedFcn', @(s, e) fprintf('value %s edited=%d now=[%s] order=%s\n', ...
                    e.Value, e.Edited, num2str(e.ValueIndex), strjoin(fieldnames(e)', ',')));
            """);
        UiDropDownModel drop = Tagged<UiDropDownModel>("dd");

        ScriptGraphicsCallbacks.NotifyComponent(drop, "value", 1);
        await Drain();
        Assert.True(
            Printed("value green edited=0 now=[2] order=Value,PreviousValue,Source,EventName,PreviousValueIndex,Edited,ValueIndex"),
            _output.NormalText + _output.ErrorText);

        // Typed text that is no item: Edited, and no place.
        ScriptGraphicsCallbacks.NotifyComponent(drop, "value", "mauve");
        await Drain();
        Assert.True(Printed("value mauve edited=1 now=[] order=Value,PreviousValue,Source,EventName,PreviousValueIndex,Edited,ValueIndex"), _output.NormalText);
    }

    [Fact]
    public async Task ASwitchNamesTheNewPlaceFirst()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            sw = uiswitch(f, 'Tag', 'sw', 'ValueChangedFcn', @(s, e) fprintf('switch %s now=%d prev=%d order=%s\n', ...
                e.Value, e.ValueIndex, e.PreviousValueIndex, strjoin(fieldnames(e)', ',')));
            """);
        UiSwitchModel toggle = Tagged<UiSwitchModel>("sw");

        ScriptGraphicsCallbacks.NotifyComponent(toggle, "value", 1);
        await Drain();

        Assert.True(
            Printed("switch On now=2 prev=1 order=Value,PreviousValue,Source,EventName,ValueIndex,PreviousValueIndex"),
            _output.NormalText + _output.ErrorText);
    }

    [Fact]
    public async Task AListsClickSaysWhichItemAndWhere_BeforeTheValueSettles()
    {
        await Exec("""
            f = uifigure('Visible', 'off', 'Position', [200 200 400 300]);
            lb = uilistbox(f, 'Tag', 'lb', 'Items', {'one', 'two'}, ...
                'ClickedFcn', @(s, e) fprintf('clicked %s item=%d at=[%g %g] screen=[%g %g]\n', ...
                    class(e.InteractionInformation), e.InteractionInformation.Item, ...
                    e.InteractionInformation.Location, e.InteractionInformation.ScreenLocation));
            """);
        UiListBoxModel list = Tagged<UiListBoxModel>("lb");

        ScriptGraphicsCallbacks.NotifyComponent(list, "clicked", 1, (76, 262));
        await Drain();

        Assert.True(
            Printed("clicked matlab.ui.eventdata.ListBoxInteraction item=2 at=[76 262] screen=[276 462]"),
            _output.NormalText + _output.ErrorText);
    }

    [Fact]
    public async Task ASpinnersTypedTextIsToldAsTheNumberItReads()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            sp = uispinner(f, 'Tag', 'sp', ...
                'ValueChangingFcn', @(s, e) fprintf('changing %g class=%s\n', e.Value, class(e.Value)));
            """);
        UiSpinnerModel spinner = Tagged<UiSpinnerModel>("sp");

        ScriptGraphicsCallbacks.NotifyComponent(spinner, "changing", "7");
        await Drain();
        ScriptGraphicsCallbacks.NotifyComponent(spinner, "changing", "abc");
        await Drain();

        Assert.True(Printed("changing 7 class=double"), _output.NormalText + _output.ErrorText);
        Assert.DoesNotContain(_output.NormalLines, static line => line.Contains("abc"));
        Assert.Equal(1, _output.NormalLines.Count(static line => line.StartsWith("changing", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ANumericFieldRefusesAValueChangingFcn()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            nf = uieditfield(f, 'numeric');
            try
                nf.ValueChangingFcn = @(s, e) 0;
                disp('took it');
            catch e
                fprintf('refused %s\n', e.identifier);
            end
            """);
        Assert.True(Printed("refused MATLAB:noPublicFieldForClass"), _output.NormalText);
    }

    // --- item 59: a table edit's follow-up, and a context menu entry's event ------------------------

    [Fact]
    public async Task ATableEditAlsoTellsDisplayDataChanged_NamingTheEditAndTheNumberedRows()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            t = uitable(f, 'Tag', 't', 'Data', {1, 'a'; 2, 'b'}, 'ColumnName', {'Num', 'Text'}, 'ColumnEditable', true, ...
                'CellEditCallback', @(src, e) fprintf('edit [%d %d]\n', e.Indices), ...
                'DisplayDataChangedFcn', @(src, e) fprintf('display %s rows=%s cols=%s col=%d shown=%d var=[%s]\n', ...
                    e.Interaction, e.DisplayRowName, strjoin(e.DisplayColumnName, '+'), ...
                    e.InteractionColumn, e.InteractionDisplayColumn, e.InteractionVariable));
            """);
        UiTableModel table = Tagged<UiTableModel>("t");

        ScriptGraphicsCallbacks.NotifyComponent(table, "celledit", new object[] { 0, 0, "9" });
        await Drain();

        Assert.True(Printed("edit [1 1]"), _output.NormalText + _output.ErrorText);
        Assert.True(Printed("display edit rows=numbered cols=Num+Text col=1 shown=1 var=[]"), _output.NormalText + _output.ErrorText);
    }

    [Fact]
    public async Task AContextMenuEntryHearsWhatWasRightClickedAndWhere()
    {
        await Exec("""
            f = uifigure('Visible', 'off', 'Position', [200 200 400 300]);
            lab = uilabel(f, 'Tag', 'lab', 'Text', 'here');
            cm = uicontextmenu(f);
            lab.ContextMenu = cm;
            mi = uimenu(cm, 'Text', 'Act', 'MenuSelectedFcn', @(s, e) fprintf('%s %s ctx=%d at=[%g %g] screen=[%g %g]\n', ...
                class(e), e.EventName, e.ContextObject == lab, e.InteractionInformation.Location, ...
                e.InteractionInformation.ScreenLocation));
            """);
        UiLabelModel label = Tagged<UiLabelModel>("lab");
        ContextMenuModel menu = Figure().ContextMenus[0];

        ScriptGraphicsCallbacks.NotifyContextMenuOpening(menu, label, (540, 211));
        ScriptGraphicsCallbacks.NotifyMenuSelected(menu.Items[0]);
        await Drain();

        Assert.True(
            Printed("matlab.ui.eventdata.MenuSelectedData MenuSelected ctx=1 at=[540 211] screen=[740 411]"),
            _output.NormalText + _output.ErrorText);
    }

    // --- item 59: what a cell writes under each ColumnFormat word -----------------------------------

    [Theory]
    [InlineData(Math.PI, "short", "3.1416")]
    [InlineData(1234567.891, "short", "1.2346e+06")]
    [InlineData(-0.5, "short", "-0.5000")]
    [InlineData(0.001, "short", "1.0000e-03")]
    [InlineData(0, "short", "0")]
    [InlineData(1e10, "short", "1.0000e+10")]
    [InlineData(Math.PI, "long", "3.141592653589793")]
    [InlineData(1234567.891, "long", "1.234567891000000e+06")]
    [InlineData(-0.5, "long", "-0.500000000000000")]
    [InlineData(0.001, "long", "1.000000000000000e-03")]
    [InlineData(0, "long", "0")]
    [InlineData(Math.PI, "shorte", "3.1416")]
    [InlineData(Math.PI, "longe", "3.1416")]
    [InlineData(Math.PI, "shortg", "3.1416")]
    [InlineData(Math.PI, "longg", "3.1416")]
    [InlineData(Math.PI, "bank", "3.14")]
    [InlineData(1234567.891, "bank", "1234567.89")]
    [InlineData(0.001, "bank", "0.00")]
    [InlineData(0, "bank", "0")]
    [InlineData(1e10, "bank", "10000000000.00")]
    [InlineData(Math.PI, "+", "+")]
    [InlineData(-0.5, "+", "-")]
    [InlineData(0, "+", "")]
    [InlineData(Math.PI, "rat", "355/113")]
    [InlineData(1234567.891, "rat", "*")]
    [InlineData(-0.5, "rat", "-1/2")]
    [InlineData(0.001, "rat", "1/1000")]
    [InlineData(1.0 / 3, "rat", "1/3")]
    [InlineData(0, "rat", "0")]
    [InlineData(1e10, "rat", "*")]
    [InlineData(Math.PI, "char", "3.1415926535898")]
    [InlineData(1234567.891, "char", "1234567.891")]
    [InlineData(0.001, "char", "0.001")]
    [InlineData(1.0 / 3, "char", "0.33333333333333")]
    [InlineData(1e10, "char", "10000000000")]
    public void ACellWritesItsNumberAsR2025bsFormatWordDoes(double value, string format, string text) =>
        Assert.Equal(text, JgsUiTables.NumberText(value, format));

    [Fact]
    public async Task ANumberUnderTheLogicalWordIsAClearBox()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            t = uitable(f, 'Tag', 't', 'Data', {pi; 0}, 'ColumnFormat', {'logical'});
            """);
        UiTableModel table = Tagged<UiTableModel>("t");
        Assert.Equal(UiTableCellKind.Unchecked, table.Content.At(0, 0).Kind);
        Assert.Equal(UiTableCellKind.Unchecked, table.Content.At(1, 0).Kind);
    }

    // --- item 64: the knob's outer rectangle and the settled ticks ----------------------------------

    [Fact]
    public async Task AKnobsOuterRectangleIsTheHullOfItsLabelRing()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            k = uiknob(f, 'Tag', 'k', 'Position', [200 200 60 60]);
            fprintf('ticks %s\n', mat2str(k.MajorTicks));
            fprintf('outer %s\n', mat2str(round(k.OuterPosition)));
            """);

        // R2025b's measured box is [175.14 179.18 108.72 102.48] (probe u9_settle): the sides
        // agree to the pixel; the vertical reach sits about five pixels shy (ADR 0236).
        Assert.True(Printed("ticks [0 10 20 30 40 50 60 70 80 90 100]"), _output.NormalText);
        Assert.True(Printed("outer [175 185 110 101]"), _output.NormalText);
    }

    [Fact]
    public async Task ADiscreteKnobNeverReachesBelowItsDial()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            k = uiknob(f, 'discrete', 'Position', [200 200 60 60]);
            o = k.OuterPosition;
            fprintf('bottom %d left %d right %d\n', o(2) == 200, o(1) < 200, o(1) + o(3) > 260);
            """);
        Assert.True(Printed("bottom 1 left 1 right 1"), _output.NormalText);
    }
}
