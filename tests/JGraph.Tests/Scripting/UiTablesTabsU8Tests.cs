using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Rendering.Layout;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// U8 of the app-building plan (ADR 0206): tables, tabs, a figure's menu bar and its toolbars. What
/// a script can ask headless is held by the parity fixtures (<c>u8_*</c>); these are the parts that
/// need a person — a cell edited, a tab picked, a tool pressed, a menu entry chosen — told through
/// the seams the window uses, and the layout of a tab group on its own.
/// </summary>
[Collection("JG facade")]
public class UiTablesTabsU8Tests : IAsyncLifetime
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

    private const string ShowHelper = """
        show = @(v) regexprep(evalc('disp(v)'), '\s+', ' ');
        """;

    // --- a table's cells ---------------------------------------------------------------------------

    [Fact]
    public async Task AnEditedCellIsWrittenIntoTheData_AndTheCallbackIsToldWhatItWasAndIs()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            t = uitable(f, 'Tag', 't', 'Data', {1, 'a', true; 2.5, 'b', false}, 'ColumnEditable', true, ...
                'CellEditCallback', @(src, e) fprintf('edit %s [%d %d] [%d %d] %s %s prev=%s typed=%s new=%s err=%d\n', class(e), ...
                    e.Indices, e.DisplayIndices, e.EventName, class(e.NewData), mat2str(e.PreviousData), mat2str(e.EditData), mat2str(e.NewData), isempty(e.Error)));
            """);
        UiTableModel table = Tagged<UiTableModel>("t");

        // A number typed over a number.
        ScriptGraphicsCallbacks.NotifyComponent(table, "celledit", new object[] { 1, 0, "7.5" });
        await Drain();
        Assert.True(Printed("edit matlab.ui.eventdata.CellEditData [2 1] [2 1] CellEdit double prev=2.5 typed='7.5' new=7.5 err=1"), _output.NormalText);
        Assert.Equal("7.5000", table.Content.At(1, 0).Text);

        // Text that is no number, typed over a number, is NaN.
        ScriptGraphicsCallbacks.NotifyComponent(table, "celledit", new object[] { 0, 0, "abc" });
        await Drain();
        Assert.True(Printed("edit matlab.ui.eventdata.CellEditData [1 1] [1 1] CellEdit double prev=1 typed='abc' new=NaN err=1"), _output.NormalText);

        // Text over text, and a box ticked.
        ScriptGraphicsCallbacks.NotifyComponent(table, "celledit", new object[] { 0, 1, "zz" });
        ScriptGraphicsCallbacks.NotifyComponent(table, "celledit", new object[] { 1, 2, true });
        await Drain();
        Assert.True(Printed("edit matlab.ui.eventdata.CellEditData [1 2] [1 2] CellEdit char prev='a' typed='zz' new='zz' err=1"), _output.NormalText);
        Assert.True(Printed("edit matlab.ui.eventdata.CellEditData [2 3] [2 3] CellEdit logical prev=false typed=true new=true err=1"), _output.NormalText);

        await Exec("d = t.Data; fprintf('data %s %s %d %g %d\\n', class(d), d{1, 2}, d{2, 3}, d{2, 1}, isnan(d{1, 1}));");
        Assert.True(Printed("data cell zz 1 7.5 1"), _output.NormalText);
        Assert.Equal(UiTableCellKind.Checked, table.Content.At(1, 2).Kind);
    }

    [Fact]
    public async Task AnEditKeepsTheClassOfANumericArray_AndOfAStringArray_AndReachesATable()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            a = uitable(f, 'Tag', 'a', 'Data', int8([1 2; 3 4]));
            s = uitable(f, 'Tag', 's', 'Data', ["p" "q"]);
            T = table([1; 2], {'x'; 'y'}, 'VariableNames', {'N', 'S'});
            v = uitable(f, 'Tag', 'v', 'Data', T);
            """);

        ScriptGraphicsCallbacks.NotifyComponent(Tagged<UiTableModel>("a"), "celledit", new object[] { 1, 1, "9" });
        ScriptGraphicsCallbacks.NotifyComponent(Tagged<UiTableModel>("s"), "celledit", new object[] { 0, 1, "new" });
        ScriptGraphicsCallbacks.NotifyComponent(Tagged<UiTableModel>("v"), "celledit", new object[] { 1, 0, "20" });
        ScriptGraphicsCallbacks.NotifyComponent(Tagged<UiTableModel>("v"), "celledit", new object[] { 0, 1, "xx" });
        await Drain();

        await Exec("""
            fprintf('a %s %s\n', class(a.Data), mat2str(a.Data));
            fprintf('s %s %s\n', class(s.Data), strjoin(s.Data, ','));
            fprintf('v %s %g %s %s\n', class(v.Data), v.Data.N(2), v.Data.S{1}, strjoin(v.ColumnName', ','));
            """);
        Assert.True(Printed("a int8 [1 2;3 9]"), _output.NormalText);
        Assert.True(Printed("s string p,new"), _output.NormalText);
        Assert.True(Printed("v table 20 xx N,S"), _output.NormalText);
    }

    [Fact]
    public async Task AnEditOutsideTheDataIsDropped_AndOneWithNoCallbackStillWrites()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            t = uitable(f, 'Tag', 't', 'Data', magic(3));
            """);
        UiTableModel table = Tagged<UiTableModel>("t");

        ScriptGraphicsCallbacks.NotifyComponent(table, "celledit", new object[] { 9, 9, "1" });
        ScriptGraphicsCallbacks.NotifyComponent(table, "celledit", new object[] { 0, 0, "42" });
        await Drain();

        await Exec("fprintf('data %s\\n', mat2str(t.Data));");
        Assert.True(Printed("data [42 1 6;3 5 7;4 9 2]"), _output.NormalText);
    }

    [Fact]
    public async Task ASelectionIsWritten_AndBothCallbacksAreTold()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            t = uitable(f, 'Tag', 't', 'Data', magic(3), ...
                'CellSelectionCallback', @(src, e) fprintf('cells %s %s %s\n', class(e), mat2str(e.Indices), e.EventName), ...
                'SelectionChangedFcn', @(src, e) fprintf('sel %s %s<-%d %s %s\n', class(e), mat2str(e.Selection), isempty(e.PreviousSelection), e.SelectionType, e.EventName));
            r = uitable(f, 'Tag', 'r', 'Data', magic(3), 'SelectionType', 'row', ...
                'SelectionChangedFcn', @(src, e) fprintf('rows %s<-%d %s\n', mat2str(e.Selection), isempty(e.PreviousSelection), e.SelectionType));
            """);

        ScriptGraphicsCallbacks.NotifyComponent(Tagged<UiTableModel>("t"), "cellselect", new[] { 0, 1, 2, 2 });
        ScriptGraphicsCallbacks.NotifyComponent(Tagged<UiTableModel>("r"), "cellselect", new[] { 2, 0 });
        await Drain();

        Assert.True(Printed("sel matlab.ui.eventdata.TableSelectionChangedData [1 2;3 3]<-1 cell SelectionChanged"), _output.NormalText);
        Assert.True(Printed("cells matlab.ui.eventdata.CellSelectionChangeData [1 2;3 3] CellSelection"), _output.NormalText);
        Assert.True(Printed("rows [3 1]<-1 row"), _output.NormalText);
        await Exec("fprintf('now %s %s\\n', mat2str(t.Selection), mat2str(r.Selection));");
        Assert.True(Printed("now [1 2;3 3] [3 1]"), _output.NormalText);
        Assert.Equal([0, 1, 2, 2], Tagged<UiTableModel>("t").Selection);

        // The same selection again tells nobody.
        int lines = _output.NormalLines.Count;
        ScriptGraphicsCallbacks.NotifyComponent(Tagged<UiTableModel>("r"), "cellselect", new[] { 2, 0 });
        await Drain();
        Assert.Equal(lines, _output.NormalLines.Count);
    }

    [Fact]
    public async Task ATablesPictureFollowsItsDataAndItsColumnProperties()
    {
        await Exec("""
            f = figure('Visible', 'off');
            t = uitable(f, 'Tag', 't', 'Data', {pi, 'txt', true; 1e6, 'u', false}, 'ColumnName', {'Num', 'Text'}, ...
                'ColumnEditable', [true false], 'ColumnFormat', {'bank', {'txt', 'u', 'w'}}, 'ColumnWidth', {50, 'fit', '2x'}, 'RowName', {'r1', 'r2'});
            """);
        UiTableContent content = Tagged<UiTableModel>("t").Content;

        Assert.Equal(2, content.Rows);
        Assert.Equal(["Num", "Text", ""], content.Columns.Select(static c => c.Header));
        Assert.Equal([true, false, false], content.Columns.Select(static c => c.Editable));
        Assert.Equal(new UiGridTrack(UiGridTrackKind.Fixed, 50), content.Columns[0].Width);
        Assert.Equal(UiGridTrackKind.Fit, content.Columns[1].Width.Kind);
        Assert.Equal(new UiGridTrack(UiGridTrackKind.Weight, 2), content.Columns[2].Width);
        Assert.Equal(["txt", "u", "w"], content.Columns[1].Choices);
        Assert.Equal(new UiTableCell("3.14", UiTableCellKind.Number), content.At(0, 0));
        Assert.Equal(new UiTableCell("1000000.00", UiTableCellKind.Number), content.At(1, 0));
        Assert.Equal(new UiTableCell("txt", UiTableCellKind.Text), content.At(0, 1));
        Assert.Equal(UiTableCellKind.Checked, content.At(0, 2).Kind);
        Assert.Equal(UiTableCellKind.Unchecked, content.At(1, 2).Kind);
        Assert.Equal(["r1", "r2"], content.RowHeaders);

        // Numbered by default, and a table's variables in their place.
        await Exec("""
            n = uitable(f, 'Tag', 'n', 'Data', [1.5 2; 3 4e-7]);
            v = uitable(f, 'Tag', 'v', 'Data', table([1; 2], 'VariableNames', {'Alpha'}, 'RowNames', {'a', 'b'}));
            """);
        UiTableContent numbered = Tagged<UiTableModel>("n").Content;
        Assert.Equal(["1", "2"], numbered.Columns.Select(static c => c.Header));
        Assert.Equal(["1", "2"], numbered.RowHeaders);
        Assert.Equal("1.5000", numbered.At(0, 0).Text);
        Assert.Equal("2", numbered.At(0, 1).Text);
        Assert.Equal("4.0000e-07", numbered.At(1, 1).Text);
        UiTableContent named = Tagged<UiTableModel>("v").Content;
        Assert.Equal(["Alpha"], named.Columns.Select(static c => c.Header));
        Assert.Equal(["a", "b"], named.RowHeaders);
    }

    // --- tabs ----------------------------------------------------------------------------------------

    [Fact]
    public async Task APickedTabShows_AndTheGroupsCallbackIsToldTheOldAndTheNew()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            tg = uitabgroup(f, 'Tag', 'tg', 'SelectionChangedFcn', @(src, e) fprintf('tab %s %s<-%s %s %d\n', class(e), e.NewValue.Title, e.OldValue.Title, e.EventName, e.Source == src));
            a = uitab(tg, 'Title', 'A', 'Tag', 'a'); b = uitab(tg, 'Title', 'B', 'Tag', 'b');
            """);
        UiTabGroupModel group = Tagged<UiTabGroupModel>("tg");

        ScriptGraphicsCallbacks.NotifyComponent(group, "tab", Tagged<UiTabModel>("b"));
        await Drain();
        Assert.True(Printed("tab matlab.ui.eventdata.SelectionChangedData B<-A SelectionChanged 1"), _output.NormalText);
        Assert.Same(Tagged<UiTabModel>("b"), group.SelectedTab);

        // The tab that is showing, picked again, tells nobody; nor does a script's own choice.
        int lines = _output.NormalLines.Count;
        ScriptGraphicsCallbacks.NotifyComponent(group, "tab", Tagged<UiTabModel>("b"));
        await Drain();
        await Exec("tg.SelectedTab = a; drawnow;");
        Assert.Equal(lines, _output.NormalLines.Count);
        Assert.Same(Tagged<UiTabModel>("a"), group.SelectedTab);
    }

    [Fact]
    public async Task OnlyTheTabThatShowsIsLaidOutAsShowing_AndItsPageIsWhatTheStripLeaves()
    {
        await Exec("""
            f = uifigure('Visible', 'off', 'Position', [100 100 400 300]);
            tg = uitabgroup(f, 'Tag', 'tg', 'Position', [20 30 300 200]);
            a = uitab(tg, 'Title', 'A', 'Tag', 'a'); b = uitab(tg, 'Title', 'B', 'Tag', 'b');
            ba = uibutton(a, 'Tag', 'ba', 'Position', [10 10 50 20]);
            bb = uibutton(b, 'Tag', 'bb', 'Position', [10 10 50 20]);
            ax = uiaxes(b, 'Tag', 'ax');
            """);
        FigureModel figure = Figure();
        UiLayoutResult layout = UiLayout.Compute(UiFrame.Take(figure), new Size2D(400, 300));

        // The group's box, and the page: a pixel in from three sides and 24 under the top.
        UiPanelPlacement shell = layout.Find(Tagged<UiTabGroupModel>("tg"))!;
        Assert.Equal(new Rect2D(19, 71, 300, 200), shell.Box);
        Assert.Equal(new Rect2D(20, 95, 298, 175), shell.Inner);
        UiTabStripPlacement strip = Assert.Single(layout.TabStrips);
        Assert.Equal(new Rect2D(19, 71, 300, 24), strip.Box);
        Assert.Equal(0, strip.Group.Selected);
        Assert.Equal(["A", "B"], strip.Group.Headings.Select(static h => h.Title));

        Assert.True(layout.Find(Tagged<UiTabModel>("a"))!.Visible);
        Assert.False(layout.Find(Tagged<UiTabModel>("b"))!.Visible);
        Assert.True(layout.Components.Single(c => c.Component.Tag == "ba").Visible);
        Assert.False(layout.Components.Single(c => c.Component.Tag == "bb").Visible);
        Assert.Equal(new Rect2D(29, 241, 50, 20), layout.Components.Single(c => c.Component.Tag == "ba").Box);

        await Exec("tg.SelectedTab = b;");
        layout = UiLayout.Compute(UiFrame.Take(figure), new Size2D(400, 300));
        Assert.False(layout.Find(Tagged<UiTabModel>("a"))!.Visible);
        Assert.True(layout.Find(Tagged<UiTabModel>("b"))!.Visible);
        Assert.True(layout.Components.Single(c => c.Component.Tag == "bb").Visible);

        // The strip along another edge takes its room from that edge.
        await Exec("tg.TabLocation = 'bottom';");
        layout = UiLayout.Compute(UiFrame.Take(figure), new Size2D(400, 300));
        Assert.Equal(new Rect2D(20, 72, 298, 175), layout.Find(Tagged<UiTabGroupModel>("tg"))!.Inner);
        Assert.Equal(new Rect2D(19, 247, 300, 24), Assert.Single(layout.TabStrips).Box);
    }

    [Fact]
    public async Task ADeletedTabHandsThePageToItsNeighbour_AndTakesWhatItHeldWithIt()
    {
        await Exec("""
            f = figure('Visible', 'off');
            tg = uitabgroup(f, 'Tag', 'tg');
            a = uitab(tg, 'Title', 'A'); b = uitab(tg, 'Title', 'B'); c = uitab(tg, 'Title', 'C');
            ax = axes(b); h = uicontrol(b);
            tg.SelectedTab = b;
            delete(b);
            fprintf('after %s %d %d %d\n', tg.SelectedTab.Title, isgraphics(ax), isgraphics(h), numel(tg.Children));
            delete(c);
            fprintf('then %s\n', tg.SelectedTab.Title);
            delete(a);
            fprintf('none %d\n', isempty(tg.SelectedTab));
            """);

        Assert.True(Printed("after C 0 0 2"), _output.NormalText);
        Assert.True(Printed("then A"), _output.NormalText);
        Assert.True(Printed("none 1"), _output.NormalText);
        Assert.Empty(Figure().Axes);
    }

    // --- menus and tools -----------------------------------------------------------------------------

    [Fact]
    public async Task AFiguresMenusAndToolbarsAreInItsFrame_LessTheOnesThatDoNotShow()
    {
        await Exec("""
            f = figure('Visible', 'off');
            m = uimenu(f, 'Text', '&File', 'Tag', 'mf');
            uimenu(m, 'Text', 'Open', 'Accelerator', 'O', 'Checked', 'on');
            uimenu(m, 'Text', 'Hidden', 'Visible', 'off');
            uimenu(m, 'Text', 'Quit', 'Separator', 'on', 'Enable', 'off');
            uimenu(f, 'Text', 'Gone', 'Visible', 'off');
            tb = uitoolbar(f);
            uipushtool(tb, 'Tooltip', 'one', 'CData', zeros(2, 3, 3));
            uitoggletool(tb, 'Tooltip', 'two', 'State', 'on', 'Separator', 'on');
            uipushtool(tb, 'Visible', 'off');
            uitoolbar(f, 'Visible', 'off');
            """);
        UiFrame frame = UiFrame.Take(Figure());

        UiMenuFrame file = Assert.Single(frame.Menus);
        Assert.Equal("&File", file.Text);
        Assert.Equal(["Open", "Quit"], file.Items.Select(static item => item.Text));
        Assert.True(file.Items[0].Checked);
        Assert.Equal("O", file.Items[0].Accelerator);
        Assert.True(file.Items[1].Separator);
        Assert.False(file.Items[1].Enabled);

        UiToolbarFrame bar = Assert.Single(frame.Toolbars);
        Assert.Equal(["one", "two"], bar.Tools.Select(static tool => tool.Tooltip));
        Assert.Equal((3, 2), (bar.Tools[0].Picture!.Width, bar.Tools[0].Picture!.Height));
        Assert.True(bar.Tools[1].IsToggle);
        Assert.True(bar.Tools[1].State);
        Assert.True(bar.Tools[1].Separator);
    }

    [Fact]
    public async Task AMenuOfTheBarRunsItsCallbackWhenChosen()
    {
        await Exec("""
            f = figure('Visible', 'off');
            m = uimenu(f, 'Text', 'File');
            uimenu(m, 'Text', 'Open', 'MenuSelectedFcn', @(src, e) fprintf('chose %s %s\n', src.Text, get(gcbo, 'Type')));
            """);

        ScriptGraphicsCallbacks.NotifyMenuSelected(Figure().Menus[0].Items[0]);
        await Drain();

        Assert.True(Printed("chose Open uimenu"), _output.NormalText);
    }

    [Fact]
    public async Task APushToolRunsItsClickedCallback_AndAToggleToolItsOnOrOffCallbackFirst()
    {
        await Exec("""
            f = figure('Visible', 'off');
            tb = uitoolbar(f);
            p = uipushtool(tb, 'ClickedCallback', @(src, e) fprintf('push %s %s\n', class(e), e.EventName));
            g = uitoggletool(tb, 'OnCallback', @(src, e) fprintf('on %s\n', char(src.State)), ...
                'OffCallback', @(src, e) fprintf('off %s\n', char(src.State)), 'ClickedCallback', @(src, e) fprintf('clicked %s\n', char(src.State)));
            """);
        UiToolbarModel bar = Assert.Single(Figure().Toolbars);

        ScriptGraphicsCallbacks.NotifyComponent(bar.Tools[0], "pushed");
        ScriptGraphicsCallbacks.NotifyComponent(bar.Tools[1], "pushed");
        await Drain();
        ScriptGraphicsCallbacks.NotifyComponent(bar.Tools[1], "pushed");
        await Drain();

        Assert.Equal(
            ["push matlab.ui.eventdata.ActionData Action", "on on", "clicked on", "off off", "clicked off"],
            _output.NormalLines);
        Assert.False(bar.Tools[1].State);

        // A State a script writes runs the callback of that state, and never the shared one.
        await Exec("g.State = 'on'; drawnow; g.State = 'on'; drawnow;");
        Assert.Equal(["on on"], _output.NormalLines.Skip(5));
    }

    [Fact]
    public async Task ACopyOfATableKeepsItsData_AndTabsMenusAndToolsCopyToo()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            t = uitable(f, 'Data', magic(3) * pi, 'ColumnName', {'a', 'b', 'c'}, 'ColumnEditable', true);
            c = copyobj(t, f);
            fprintf('table %d %s %d\n', isequal(c.Data, t.Data), strjoin(c.ColumnName', ''), c.ColumnEditable);
            tg = uitabgroup(f); a = uitab(tg, 'Title', 'A'); uitable(a, 'Data', {1, 'x'});
            b = copyobj(a, tg);
            fprintf('tab %s %d %s\n', b.Title, numel(tg.Children), class(b.Children(1).Data));
            m = uimenu(f, 'Text', 'M'); uimenu(m, 'Text', 'sub');
            n = copyobj(m, f);
            fprintf('menu %s %d %d\n', n.Text, numel(n.Children), n.Position);
            tb = uitoolbar(f); p = uipushtool(tb, 'Tooltip', 'go');
            q = copyobj(p, tb);
            fprintf('tool %s %d\n', q.Tooltip, numel(tb.Children));
            """);

        Assert.True(Printed("table 1 abc 1"), _output.NormalText);
        Assert.True(Printed("tab A 2 cell"), _output.NormalText);
        Assert.True(Printed("menu M 1 2"), _output.NormalText);
        Assert.True(Printed("tool go 2"), _output.NormalText);
    }

    [Fact]
    public async Task ATableReadFromADocumentAnswersWhatItShows()
    {
        await Exec("""
            f = figure('Visible', 'off');
            uitable(f, 'Tag', 't', 'Data', {1.5, 'txt', true; 2, 'u', false}, 'ColumnName', {'N', 'S', 'L'});
            """);
        FigureModel loaded = JGraph.Serialization.GraphFormat.Deserialize(JGraph.Serialization.GraphFormat.Serialize(Figure()));
        var table = Assert.IsType<UiTableModel>(Assert.Single(loaded.Components));
        JgsHandleEntry entry = JgsHandleRegistry.EntryFor(table);

        JgsValue data = JgsGraphicsProperties.Get(entry, "Data", 0, 0);

        Assert.Equal(JgsType.Cell, data.Type);
        Assert.Equal((2, 3), (data.Rows, data.Cols));
        Assert.Equal(1.5, data.AsCell[0].AsNumber);
        Assert.Equal("u", data.AsCell[3].AsString);
        Assert.True(data.AsCell[4].IsTruthy);
        Assert.Equal(JgsType.Bool, data.AsCell[5].Type);
    }
}
