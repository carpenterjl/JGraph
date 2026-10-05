using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Rendering.Layout;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// U5 of the app-building plan (ADR 0202): the <c>uifigure</c> components, <c>uigridlayout</c> and
/// the dialogs a figure lays over itself. What a script can ask headless is held by the parity
/// fixtures (<c>u5_*</c>); these are the parts that need a person — a value changed, a button
/// pushed, a dialog answered — told through the seams the window uses, and the grid's arithmetic
/// on its own.
/// </summary>
[Collection("JG facade")]
public class UiComponentsU5Tests : IAsyncLifetime
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
        ScriptGraphicsCallbacks.OverlayShown = null;
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

    private async Task<string> Fails(string code)
    {
        ScriptRunResult result = await _session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.False(result.Success);
        return result.Message + _output.ErrorText;
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

    // --- a value a person changed --------------------------------------------------------------------

    [Fact]
    public async Task ASliderTellsItsValueChangedFcnTheValueAndTheOneBefore()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            s = uislider(f, 'Tag', 's', 'Value', 20, 'ValueChangedFcn', @(src, e) fprintf('changed %s %g<-%g %s %d\n', ...
                class(e), e.Value, e.PreviousValue, e.EventName, e.Source == src));
            """);

        ScriptGraphicsCallbacks.NotifyComponent(Tagged<UiSliderModel>("s"), "value", 55.0);
        await Drain();

        Assert.Equal(55, Tagged<UiSliderModel>("s").Value);
        Assert.True(Printed("changed matlab.ui.eventdata.ValueChangedData 55<-20 ValueChanged 1"), _output.NormalText);
    }

    [Fact]
    public async Task ADragTellsValueChangingFcnWithoutWritingTheValue_AndCoalesces()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            s = uislider(f, 'Tag', 's', 'ValueChangingFcn', @(src, e) fprintf('changing %g while %g %s\n', e.Value, src.Value, e.EventName), ...
                'ValueChangedFcn', @(src, e) fprintf('changed %g\n', e.Value));
            """);
        UiSliderModel slider = Tagged<UiSliderModel>("s");

        ScriptGraphicsCallbacks.NotifyComponent(slider, "changing", 10.0);
        ScriptGraphicsCallbacks.NotifyComponent(slider, "changing", 20.0);
        ScriptGraphicsCallbacks.NotifyComponent(slider, "changing", 30.0);
        ScriptGraphicsCallbacks.NotifyComponent(slider, "value", 30.0);
        await Drain();

        Assert.Equal(
            ["changing 30 while 0 ValueChanging", "changed 30"],
            _output.NormalLines.Where(static l => l.StartsWith("chang", StringComparison.Ordinal)));
        Assert.Equal(30, slider.Value);
    }

    [Fact]
    public async Task APushedButtonRunsItsButtonPushedFcn()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            b = uibutton(f, 'Tag', 'b', 'ButtonPushedFcn', @(src, e) fprintf('pushed %s %s %d\n', class(e), e.EventName, e.Source == src));
            """);

        ScriptGraphicsCallbacks.NotifyComponent(Tagged<UiButtonModel>("b"), "pushed");
        await Drain();

        Assert.True(Printed("pushed matlab.ui.eventdata.ButtonPushedData ButtonPushed 1"), _output.NormalText);
    }

    [Fact]
    public async Task ADropDownsChoiceIsItsItem_OrItsItemsData()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            d = uidropdown(f, 'Tag', 'd', 'Items', {'Red', 'Green', 'Blue'}, ...
                'ValueChangedFcn', @(src, e) fprintf('picked %s<-%s index %d\n', e.Value, e.PreviousValue, src.ValueIndex));
            n = uidropdown(f, 'Tag', 'n', 'Items', {'One', 'Two', 'Three'}, 'ItemsData', [10 20 30], ...
                'ValueChangedFcn', @(src, e) fprintf('data %g<-%g\n', e.Value, e.PreviousValue));
            """);

        ScriptGraphicsCallbacks.NotifyComponent(Tagged<UiDropDownModel>("d"), "value", 2);
        ScriptGraphicsCallbacks.NotifyComponent(Tagged<UiDropDownModel>("n"), "value", 1);
        await Drain();

        Assert.True(Printed("picked Blue<-Red index 3"), _output.NormalText);
        Assert.True(Printed("data 20<-10"), _output.NormalText);
    }

    [Fact]
    public async Task ACheckBoxAnEditFieldAndASpinnerTakeWhatWasDoneToThem()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            c = uicheckbox(f, 'Tag', 'c', 'ValueChangedFcn', @(src, e) fprintf('check %d<-%d %s\n', e.Value, e.PreviousValue, class(e.Value)));
            t = uieditfield(f, 'Tag', 't', 'ValueChangedFcn', @(src, e) fprintf('text [%s]<-[%s]\n', e.Value, e.PreviousValue));
            n = uieditfield(f, 'numeric', 'Tag', 'n', 'Limits', [0 10], 'ValueChangedFcn', @(src, e) fprintf('number %g<-%g\n', e.Value, e.PreviousValue));
            p = uispinner(f, 'Tag', 'p', 'Value', 3, 'ValueChangedFcn', @(src, e) fprintf('spin %g<-%g\n', e.Value, e.PreviousValue));
            """);

        ScriptGraphicsCallbacks.NotifyComponent(Tagged<UiCheckBoxModel>("c"), "value", true);
        ScriptGraphicsCallbacks.NotifyComponent(Tagged<UiEditFieldModel>("t"), "value", "hello");
        ScriptGraphicsCallbacks.NotifyComponent(Tagged<UiNumericEditFieldModel>("n"), "value", "7.5");
        ScriptGraphicsCallbacks.NotifyComponent(Tagged<UiSpinnerModel>("p"), "value", 4.0);
        await Drain();

        Assert.True(Printed("check 1<-0 logical"), _output.NormalText);
        Assert.True(Printed("text [hello]<-[]"), _output.NormalText);
        Assert.True(Printed("number 7.5<-0"), _output.NormalText);
        Assert.True(Printed("spin 4<-3"), _output.NormalText);
    }

    [Fact]
    public async Task ANumericFieldRefusesWhatIsOutsideItsLimits_AndKeepsItsValue()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            n = uieditfield(f, 'numeric', 'Tag', 'n', 'Value', 5, 'Limits', [0 10], 'ValueChangedFcn', @(src, e) disp('number ran'));
            """);

        ScriptGraphicsCallbacks.NotifyComponent(Tagged<UiNumericEditFieldModel>("n"), "value", "25");
        ScriptGraphicsCallbacks.NotifyComponent(Tagged<UiNumericEditFieldModel>("n"), "value", "abc");
        await Drain();

        Assert.Equal(5, Tagged<UiNumericEditFieldModel>("n").Value);
        Assert.False(Printed("number ran"), _output.NormalText);
    }

    [Fact]
    public async Task ARadioButtonPressedTakesTheSelection_AndTheGroupHears()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            g = uibuttongroup(f, 'SelectionChangedFcn', @(src, e) fprintf('selection %s->%s %s\n', e.OldValue.Tag, e.NewValue.Tag, class(e)));
            a = uiradiobutton(g, 'Tag', 'a');
            b = uiradiobutton(g, 'Tag', 'b');
            """);

        ScriptGraphicsCallbacks.NotifyComponent(Tagged<UiRadioButtonModel>("b"), "value", true);
        await Drain();

        Assert.False(Tagged<UiRadioButtonModel>("a").Value);
        Assert.True(Tagged<UiRadioButtonModel>("b").Value);
        Assert.True(Printed("selection a->b matlab.ui.eventdata.SelectionChangedData"), _output.NormalText);
    }

    [Fact]
    public async Task StopKeepsAValueThePersonSet_AndDropsItsCallback()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            s = uislider(f, 'Tag', 's', 'ValueChangedFcn', @(src, e) disp('changed ran'));
            """);

        ScriptGraphicsCallbacks.NotifyComponent(Tagged<UiSliderModel>("s"), "value", 40.0);
        ScriptEventQueue.Flush(); // what Stop does
        await Drain();

        Assert.Equal(40, Tagged<UiSliderModel>("s").Value);
        Assert.False(Printed("changed ran"));
    }

    // --- the dialogs over a figure -------------------------------------------------------------------

    [Theory]
    [InlineData(0, "[Save] 1")]
    [InlineData(2, "[Cancel] 3")]
    [InlineData(-1, "[Cancel] 3")]
    public async Task AConfirmationAnswersTheOptionPressed_AndItsCancelOptionWhenDismissed(int pressed, string expected)
    {
        var seen = new List<string>();
        ScriptGraphicsCallbacks.OverlayShown = overlay =>
        {
            seen.Add($"{overlay.Kind}|{overlay.Title}|{string.Join(",", overlay.Options)}|{overlay.Icon}|{overlay.DefaultOption}|{overlay.CancelOption}");
            ScriptGraphicsCallbacks.NotifyOverlay(overlay, pressed);
        };
        await Exec("""
            f = uifigure;
            a = uiconfirm(f, 'Save the changes?', 'Closing', 'Options', {'Save', 'Discard', 'Cancel'}, 'DefaultOption', 1, 'CancelOption', 3, ...
                'CloseFcn', @(src, e) fprintf('[%s] %d\n', e.SelectedOption, e.SelectedOptionIndex));
            fprintf('answer %s; left %d\n', a, numel(f.Children));
            """);

        Assert.Equal("Confirm|Closing|Save,Discard,Cancel|question|0|2", Assert.Single(seen));
        Assert.True(Printed(expected), _output.NormalText);
        Assert.True(Printed($"answer {expected[1..expected.IndexOf(']')]}; left 0"), _output.NormalText);
        Assert.Empty(Figure().Overlays);
    }

    [Fact]
    public async Task AnAlertDoesNotHoldTheScript_AndItsCloseFcnRunsWhenItIsDismissed()
    {
        await Exec("""
            f = uifigure;
            uialert(f, 'It broke', 'Oops', 'CloseFcn', @(src, e) fprintf('closed %s [%s] %d\n', e.EventName, e.DialogTitle, e.Source == f));
            disp('went on');
            """);
        Assert.True(Printed("went on"));
        UiOverlayModel alert = Assert.Single(Figure().Overlays);
        Assert.Equal((UiOverlayKind.Alert, "Oops", "error"), (alert.Kind, alert.Title, alert.Icon));
        Assert.Equal(["It broke"], alert.Message);

        ScriptGraphicsCallbacks.NotifyOverlay(alert, 0);
        await Drain();

        Assert.True(Printed("closed AlertDialogClosed [Oops] 1"), _output.NormalText);
        Assert.Empty(Figure().Overlays);
    }

    [Fact]
    public async Task AProgressDialogHearsItsCancelButton_AndLeavesWhenClosed()
    {
        await Exec("""
            f = uifigure;
            d = uiprogressdlg(f, 'Title', 'Working', 'Cancelable', 'on', 'Value', 0.25);
            """);
        UiOverlayModel progress = Assert.Single(Figure().Overlays);
        Assert.Equal((UiOverlayKind.Progress, 0.25, true), (progress.Kind, progress.Value, progress.Cancelable));

        ScriptGraphicsCallbacks.NotifyOverlay(progress, 0);
        await Drain();
        await Exec("fprintf('cancel %d\\n', d.CancelRequested); d.Value = 0.5; close(d); fprintf('gone %d\\n', isvalid(d));");

        Assert.True(Printed("cancel 1"), _output.NormalText);
        Assert.True(Printed("gone 0"), _output.NormalText);
        Assert.Empty(Figure().Overlays);
    }

    // --- the grid ------------------------------------------------------------------------------------

    [Fact]
    public void AGridSharesWhatItsFixedAndFitTracksLeaveAmongTheWeightedOnes()
    {
        UiGridArrangement grid = UiGridMath.Arrange(
            rows: [new UiGridTrack(UiGridTrackKind.Fixed, 40), UiGridTrack.One],
            columns: [UiGridTrack.Fit, UiGridTrack.One, new UiGridTrack(UiGridTrackKind.Weight, 2)],
            padding: [10, 10, 10, 10],
            rowSpacing: 10,
            columnSpacing: 10,
            size: new Size2D(400, 300),
            items: [new UiGridItem(new UiGridCell(1, 1), new Size2D(50, 22))]);

        // 400 - 20 padding - 20 spacing - 50 fit = 310, shared 1 : 2.
        Assert.Equal(50, grid.ColumnWidths[0]);
        Assert.Equal(310.0 / 3, grid.ColumnWidths[1], 9);
        Assert.Equal(620.0 / 3, grid.ColumnWidths[2], 9);
        Assert.Equal([40, 230], grid.RowHeights);

        // The child has its cell: from the left padding, and down from the top.
        Rect2D cell = Assert.Single(grid.Cells);
        Assert.Equal((11, 251, 50, 40), (cell.X, cell.Y, cell.Width, cell.Height));
    }

    [Fact]
    public void AFitTrackNoChildReachesTakesNoRoomAndNoSpacing()
    {
        UiGridArrangement grid = UiGridMath.Arrange(
            rows: [UiGridTrack.One],
            columns: [UiGridTrack.Fit, UiGridTrack.One],
            padding: [0, 0, 0, 0],
            rowSpacing: 10,
            columnSpacing: 10,
            size: new Size2D(200, 100),
            items: []);

        Assert.Equal([0, 200], grid.ColumnWidths);
    }

    [Fact]
    public async Task AGridPlacesItsChildrenRowByRow_GrowsToHoldThem_AndOwnsTheirPosition()
    {
        await Exec("""
            f = uifigure('Visible', 'off', 'Position', [100 100 400 300]);
            g = uigridlayout(f, [2 2]);
            a = uibutton(g); b = uibutton(g); c = uibutton(g); d = uibutton(g); e = uibutton(g);
            fprintf('cells %d,%d %d,%d %d,%d\n', b.Layout.Row, b.Layout.Column, d.Layout.Row, d.Layout.Column, e.Layout.Row, e.Layout.Column);
            fprintf('rows %d\n', numel(g.RowHeight));
            e.Layout.Row = 1; e.Layout.Column = [1 2];
            fprintf('spans %s %s\n', mat2str(e.Layout.Row), mat2str(e.Layout.Column));
            lastwarn(''); a.Position = [1 1 10 10]; [~, id] = lastwarn; disp(id);
            """);

        Assert.True(Printed("cells 1,2 2,2 3,1"), _output.NormalText);
        Assert.True(Printed("rows 3"), _output.NormalText);
        Assert.True(Printed("spans 1 [1 2]"), _output.NormalText);
        Assert.True(Printed("MATLAB:ui:components:noPositionSetWhenInLayoutContainer"), _output.NormalText);
    }

    [Fact]
    public async Task AScrollableGridWhoseRowsDoNotFitGetsABar_AndMovesUnderItsViewport()
    {
        await Exec("""
            f = uifigure('Visible', 'off', 'Position', [100 100 300 200]);
            g = uigridlayout(f, [4 1], 'RowHeight', {100, 100, 100, 100}, 'Scrollable', 'on', 'Padding', [0 0 0 0], 'RowSpacing', 0);
            a = uibutton(g, 'Tag', 'a'); b = uibutton(g, 'Tag', 'b'); c = uibutton(g, 'Tag', 'c'); d = uibutton(g, 'Tag', 'd');
            """);
        UiGridLayoutModel grid = Everything(Figure()).OfType<UiGridLayoutModel>().Single();

        UiLayoutResult layout = UiLayout.Compute(Figure().TakeComponentFrame(), new Size2D(300, 200));
        UiPanelPlacement placed = layout.Find(grid)!;
        Assert.Equal(new Size2D(283, 400), placed.Content);
        Assert.Equal(new Rect2D(283, 0, 17, 200), placed.VerticalBar);
        Assert.Null(placed.HorizontalBar);
        Assert.Equal(new Rect2D(0, 0, 283, 200), placed.Inner);
        UiComponentPlacement first = layout.Components.First(static p => p.Component.Source.Tag == "a");
        Assert.Equal(new Rect2D(0, 0, 283, 100), first.Box);
        Assert.Equal(new Rect2D(0, 0, 283, 200), first.Clip);

        // The bar moved: the grid takes where it was scrolled to, and no callback is owed.
        ScriptGraphicsCallbacks.NotifyComponent(grid, "value", new[] { 0.0, 150.0 });
        await Drain();
        Assert.Equal(150, grid.ScrollY);
        layout = UiLayout.Compute(Figure().TakeComponentFrame(), new Size2D(300, 200));
        Assert.Equal(new Rect2D(0, -150, 283, 100), layout.Components.First(static p => p.Component.Source.Tag == "a").Box);
        Assert.Equal(new Rect2D(0, 150, 283, 100), layout.Components.First(static p => p.Component.Source.Tag == "d").Box);

        // Scrolled further than there is to scroll, it stops at the end.
        grid.ScrollY = 5000;
        layout = UiLayout.Compute(Figure().TakeComponentFrame(), new Size2D(300, 200));
        Assert.Equal(200, layout.Find(grid)!.ScrollY);

        // With room for everything there is no bar and nothing is moved.
        layout = UiLayout.Compute(Figure().TakeComponentFrame(), new Size2D(300, 500));
        Assert.Null(layout.Find(grid)!.VerticalBar);
        Assert.Equal(new Rect2D(0, 0, 300, 100), layout.Components.First(static p => p.Component.Source.Tag == "a").Box);
    }

    [Fact]
    public async Task AUicontrolIsRefusedInAGrid()
    {
        string message = await Fails("f = uifigure('Visible', 'off'); g = uigridlayout(f); uicontrol(g);");
        Assert.Contains("Parent must be a Figure", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AUiaxesKeepsItsTitleWhenPlottedIntoAgain_AndIsNeverTheCurrentAxes()
    {
        await Exec("""
            f = uifigure('Visible', 'off');
            ax = uiaxes(f); title(ax, 'Kept'); plot(ax, 1:3); plot(ax, 1:5);
            fprintf('title [%s] lines %d units %s\n', ax.Title.String, numel(ax.Children), ax.Units);
            other = gca; fprintf('same %d\n', other == ax);
            """);

        Assert.True(Printed("title [Kept] lines 1 units pixels"), _output.NormalText);
        Assert.True(Printed("same 0"), _output.NormalText);
    }
}
