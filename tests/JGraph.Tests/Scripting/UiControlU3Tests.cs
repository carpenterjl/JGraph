using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Serialization;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// U3 of the app-building plan (ADR 0200): the rest of the <c>uicontrol</c> styles and the button
/// group, driven headless through the seam the window calls. What a user does to a control is
/// written before its callback is considered, in the shape the style's <c>Value</c> has; a button
/// group selects on a press and tells its <c>SelectionChangedFcn</c>; a control that is not
/// <c>'on'</c> answers a press with its <c>ButtonDownFcn</c>; and the frame the window is fed carries
/// what each style needs to be drawn.
/// </summary>
[Collection("JG facade")]
public class UiControlU3Tests : IAsyncLifetime
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

    private static FigureModel Figure(int number = 1) =>
        JG.TryGetFigure(number, out FigureModel figure) ? figure : throw new InvalidOperationException("no figure");

    private static IEnumerable<UiControlModel> Controls(IUiContainer holder)
    {
        foreach (UiObject component in holder.Components)
        {
            if (component is UiControlModel control)
            {
                yield return control;
            }

            if (component is IUiContainer inner)
            {
                foreach (UiControlModel deeper in Controls(inner))
                {
                    yield return deeper;
                }
            }
        }
    }

    private static UiControlModel Tagged(string tag) => Controls(Figure()).Single(c => c.Tag == tag);

    [Fact]
    public async Task AUsersValueIsWrittenInTheStylesOwnShape_BeforeTheCallbackReadsIt()
    {
        await Exec("""
            f = figure('Visible', 'off');
            show = @(s, e) fprintf('%s %s %s\n', s.Tag, mat2str(s.Value), class(e));
            uicontrol(f, 'Style', 'togglebutton', 'Tag', 'toggle', 'Callback', show);
            uicontrol(f, 'Style', 'checkbox', 'Tag', 'check', 'Min', 2, 'Max', 7, 'Value', 2, 'Callback', show);
            uicontrol(f, 'Style', 'radiobutton', 'Tag', 'radio', 'Callback', show);
            uicontrol(f, 'Style', 'slider', 'Tag', 'slider', 'Callback', show);
            uicontrol(f, 'Style', 'popupmenu', 'Tag', 'popup', 'String', {'a', 'b', 'c'}, 'Callback', show);
            uicontrol(f, 'Style', 'listbox', 'Tag', 'list', 'String', {'a', 'b', 'c'}, 'Max', 2, 'Callback', show);
            """);

        ScriptGraphicsCallbacks.NotifyUserValue(Tagged("toggle"), 1.0);
        ScriptGraphicsCallbacks.NotifyUserValue(Tagged("check"), 1.0);
        ScriptGraphicsCallbacks.NotifyUserValue(Tagged("radio"), 1.0);
        ScriptGraphicsCallbacks.NotifyUserValue(Tagged("radio"), 0.0);
        ScriptGraphicsCallbacks.NotifyUserValue(Tagged("slider"), 0.25);
        ScriptGraphicsCallbacks.NotifyUserValue(Tagged("popup"), 3.0);
        ScriptGraphicsCallbacks.NotifyUserValue(Tagged("list"), new double[] { 1, 3 });
        await Drain();

        Assert.Equal(
            [
                "toggle 1 matlab.ui.eventdata.ActionData",
                "check 1 matlab.ui.eventdata.ActionData",
                "radio 1 matlab.ui.eventdata.ActionData",
                "radio 0 matlab.ui.eventdata.ActionData",
                "slider 0.25 matlab.ui.eventdata.ActionData",
                "popup 3 matlab.ui.eventdata.ActionData",
                "list [1 3] matlab.ui.eventdata.ActionData",
            ],
            _output.NormalLines.Where(static l => l.EndsWith("ActionData", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AMultiLineEditFieldsLinesComeBackInTheShapeItsStringHad()
    {
        await Exec("""
            f = figure('Visible', 'off');
            uicontrol(f, 'Style', 'edit', 'Max', 2, 'Tag', 'cell', 'String', {'a'});
            uicontrol(f, 'Style', 'edit', 'Max', 2, 'Tag', 'chars', 'String', 'a');
            """);

        ScriptGraphicsCallbacks.NotifyUserValue(Tagged("cell"), new[] { "one", "three" });
        ScriptGraphicsCallbacks.NotifyUserValue(Tagged("chars"), new[] { "one", "three" });
        await Drain();
        await Exec("""
            c = findobj(f, 'Tag', 'cell'); d = findobj(f, 'Tag', 'chars');
            fprintf('%s %s | %s %s\n', class(c.String), mat2str(size(c.String)), class(d.String), mat2str(size(d.String)));
            """);

        Assert.Contains(_output.NormalLines, static l => l == "cell [2 1] | char [2 5]");

        ScriptGraphicsCallbacks.NotifyUserValue(Tagged("chars"), new[] { "alone" });
        await Drain();
        Assert.Equal(UiTextForm.CharRow, Tagged("chars").Text.Form);
        Assert.Equal("alone", Tagged("chars").Text.Joined);
    }

    [Fact]
    public async Task AOneElementArrayOfHandles_IsThatHandle_ToTheDot()
    {
        await Exec("""
            f = figure('Visible', 'off');
            uicontrol(f, 'Tag', 'x', 'String', 'before');
            h = findobj(f, 'Tag', 'x');
            h.String = 'after';
            fprintf('%s %s\n', mat2str(size(h)), h.String);
            """);

        Assert.Contains(_output.NormalLines, static l => l == "[1 1] after");
    }

    [Fact]
    public async Task AButtonGroupSelectsOnAPress_ZeroesTheOldButton_AndTellsItsSelectionChangedFcn()
    {
        await Exec("""
            f = figure('Visible', 'off');
            g = uibuttongroup(f, 'SelectionChangedFcn', @(s, e) fprintf('changed %s %s->%s %s %d\n', ...
                class(e), e.OldValue.Tag, e.NewValue.Tag, e.EventName, e.Source == s));
            a = uicontrol(g, 'Style', 'radiobutton', 'Tag', 'a');
            b = uicontrol(g, 'Style', 'radiobutton', 'Tag', 'b');
            t = uicontrol(g, 'Style', 'togglebutton', 'Tag', 't');
            """);
        Assert.Same(Tagged("a"), ((UiButtonGroupModel)Figure().Components[0]).SelectedObject);

        ScriptGraphicsCallbacks.NotifyUserValue(Tagged("b"), 1.0);
        await Drain();
        Assert.Equal(0, Tagged("a").Value.Data[0]);
        Assert.Equal(1, Tagged("b").Value.Data[0]);
        Assert.Contains(_output.NormalLines, static l => l == "changed matlab.ui.eventdata.SelectionChangedData a->b SelectionChanged 1");

        ScriptGraphicsCallbacks.NotifyUserValue(Tagged("t"), 1.0);
        await Drain();
        Assert.Contains(_output.NormalLines, static l => l.StartsWith("changed matlab.ui.eventdata.SelectionChangedData b->t", StringComparison.Ordinal));
        Assert.Equal(0, Tagged("b").Value.Data[0]);

        // The selected toggle button let up (R2025b, window session u3w_clicks): its 0 is written,
        // the group hears nothing and still counts it selected, and the next choice names it as
        // the one left.
        var group = (UiButtonGroupModel)Figure().Components[0];
        int before = _output.NormalLines.Count(static l => l.StartsWith("changed", StringComparison.Ordinal));
        ScriptGraphicsCallbacks.NotifyUserValue(Tagged("t"), 0.0);
        await Drain();
        Assert.Equal(0, Tagged("t").Value.Data[0]);
        Assert.Same(Tagged("t"), group.SelectedObject);
        Assert.Equal(before, _output.NormalLines.Count(static l => l.StartsWith("changed", StringComparison.Ordinal)));

        ScriptGraphicsCallbacks.NotifyUserValue(Tagged("a"), 1.0);
        await Drain();
        Assert.Contains(_output.NormalLines, static l => l.StartsWith("changed matlab.ui.eventdata.SelectionChangedData t->a", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AGroupsCallbackRunsBeforeTheButtonsOwn()
    {
        await Exec("""
            f = figure('Visible', 'off');
            g = uibuttongroup(f, 'SelectionChangedFcn', @(s, e) disp('order: group'));
            a = uicontrol(g, 'Style', 'radiobutton', 'Tag', 'a');
            b = uicontrol(g, 'Style', 'radiobutton', 'Tag', 'b', 'Callback', @(s, e) fprintf('order: button %d\n', s.Value));
            """);

        ScriptGraphicsCallbacks.NotifyUserValue(Tagged("b"), 1.0);
        await Drain();

        Assert.Equal(["order: group", "order: button 1"], _output.NormalLines.Where(static l => l.StartsWith("order", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task StopKeepsASelectionTheUserMade_AndDropsItsCallback()
    {
        await Exec("""
            f = figure('Visible', 'off');
            g = uibuttongroup(f, 'SelectionChangedFcn', @(s, e) disp('changed ran'));
            a = uicontrol(g, 'Style', 'radiobutton', 'Tag', 'a');
            b = uicontrol(g, 'Style', 'radiobutton', 'Tag', 'b');
            """);

        ScriptGraphicsCallbacks.NotifyUserValue(Tagged("b"), 1.0);
        ScriptEventQueue.Flush(); // what Stop does
        await Drain();

        Assert.Same(Tagged("b"), ((UiButtonGroupModel)Figure().Components[0]).SelectedObject);
        Assert.DoesNotContain(_output.NormalLines, static l => l.Contains("changed ran"));
    }

    [Fact]
    public async Task APressOnAControlThatIsNotOn_RunsItsButtonDownFcn_AndMakesItTheCurrentObject()
    {
        await Exec("""
            f = figure('Visible', 'off');
            t = uicontrol(f, 'Style', 'text', 'String', 'click me', 'Enable', 'inactive', 'Tag', 'label', ...
                'ButtonDownFcn', @(s, e) fprintf('down %s %d %s %s\n', s.Tag, isequal(gco, s), class(e), e.EventName), ...
                'Callback', @(s, e) disp('callback ran'));
            """);

        ScriptGraphicsCallbacks.NotifyButtonDown(Figure(), Tagged("label"), null, null, 1);
        await Drain();

        Assert.Contains(_output.NormalLines, static l => l == "down label 1 matlab.ui.eventdata.MouseData ButtonDown");
        Assert.DoesNotContain(_output.NormalLines, static l => l.Contains("callback ran"));
    }

    [Fact]
    public async Task TheFrameCarriesWhatEachStyleIsDrawnFrom()
    {
        await Exec("""
            f = figure('Visible', 'off');
            uicontrol(f, 'Style', 'slider', 'Tag', 'slider', 'Min', 2, 'Max', 10, 'Value', 4, 'SliderStep', [0.25 0.5]);
            uicontrol(f, 'Style', 'listbox', 'Tag', 'list', 'String', 'a|b|c', 'Max', 3, 'Value', [1 3], 'ListboxTop', 2);
            uicontrol(f, 'Style', 'edit', 'Tag', 'lines', 'Max', 2);
            c = uicontrol(f, 'Tag', 'face', 'CData', cat(3, [1 0; NaN 0.5], [0 1; NaN 0.5], [0 0; NaN 0.5]));
            g = uibuttongroup(f);
            uicontrol(g, 'Style', 'radiobutton', 'Tag', 'radio');
            uicontrol(c);
            """);

        UiFrame frame = UiFrame.Take(Figure());
        UiControlFrame Of(string tag) => frame.Controls.Single(c => c.Source.Tag == tag);

        UiControlFrame slider = Of("slider");
        Assert.Equal((2, 10, 4, 0.25, 0.5), (slider.Min, slider.Max, slider.Scalar, slider.StepSmall, slider.StepLarge));

        UiControlFrame list = Of("list");
        Assert.Equal(["a", "b", "c"], list.Text.Lines);
        Assert.Equal([1, 3], list.Value!.Data);
        Assert.True(list.IsMultiple);
        Assert.Equal(2, list.ListboxTop);

        Assert.True(Of("lines").IsMultiple);
        Assert.True(Of("radio").InGroup);
        Assert.Equal(1, Of("radio").Scalar);
        Assert.False(Of("slider").InGroup);

        // The picture: rows top to bottom, BGRA, and a NaN pixel shows the face beneath.
        UiControlFrame face = Of("face");
        UiImage image = Assert.IsType<UiImage>(face.Image);
        Assert.Equal((2, 2), (image.Width, image.Height));
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, image.Bgra[..4]);       // (1,1) red
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, image.Bgra[4..8]);      // (1,2) green
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, image.Bgra[8..12]);         // (2,1) NaN
        Assert.Equal(new byte[] { 128, 128, 128, 255 }, image.Bgra[12..16]); // (2,2) grey
        Assert.Equal(1, face.FocusRequests);
    }

    [Fact]
    public async Task Extent_IsInWholePoints_WithR2025bsMargins_RoundThisBuildsOwnMeasureOfTheText()
    {
        await Exec("""
            f = figure('Visible', 'off');
            t = uicontrol(f, 'Style', 'text', 'Tag', 't', 'String', 'Hello World', 'FontName', 'Arial', 'FontSize', 10);
            """);

        UiControlModel text = Tagged("t");
        Size2D extent = text.ExtentPixels();
        (double width, double line) = UiFonts.Measure("Hello World", "Arial", 10 * 96.0 / 72);
        Assert.True(width > 40 && line > 10, $"{width} x {line}");
        Assert.Equal(System.Math.Round((width * 0.75) + 4, MidpointRounding.AwayFromZero), extent.Width * 0.75, 9);
        Assert.Equal(System.Math.Round((line * 0.75) + 6, MidpointRounding.AwayFromZero), extent.Height * 0.75, 9);

        // The text fits what Extent says, with room to spare; and a name the machine does not have
        // measures as Arial, which is what the window draws it in.
        Assert.True(extent.Width >= width && extent.Height >= line);
        Assert.Equal("Arial", UiFonts.Family("NoSuchFontAtAll"));
        Assert.Equal("Microsoft Sans Serif", UiFonts.Family("MS Sans Serif"));
        Assert.Equal(UiFonts.Measure("Hello World", "NoSuchFontAtAll", 20), UiFonts.Measure("Hello World", "Arial", 20));
    }

    [Fact]
    public async Task ADocument_KeepsAButtonGroupsSelection_WhichButtonsItWatches_AndAButtonsPicture()
    {
        await Exec("""
            f = figure('Visible', 'off');
            g = uibuttongroup(f, 'Tag', 'g', 'Title', 'Pick');
            uicontrol(g, 'Style', 'radiobutton', 'Tag', 'a');
            uicontrol(g, 'Style', 'radiobutton', 'Tag', 'b', 'Value', 1);
            late = uicontrol(g, 'Tag', 'late'); late.Style = 'radiobutton';
            uicontrol(f, 'Tag', 'face', 'CData', uint8(cat(3, 255, 0, 0)));
            """);

        FigureModel loaded = GraphFormat.Deserialize(GraphFormat.Serialize(Figure()));
        var group = Assert.IsType<UiButtonGroupModel>(loaded.Components[0]);
        Assert.Equal("b", group.SelectedObject?.Tag);
        Assert.Equal([true, true, false], group.Components.Cast<UiControlModel>().Select(static c => c.GroupManaged));
        var face = Assert.IsType<UiControlModel>(loaded.Components[1]);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, face.Image?.Bgra);

        // And a copy of the group selects its own copy of the button.
        await Exec("""
            f2 = figure('Visible', 'off');
            g2 = copyobj(g, f2);
            fprintf('copy %s %s %d\n', g2.Type, g2.SelectedObject.Tag, g2.SelectedObject ~= g.SelectedObject);
            c2 = copyobj(findobj(f, 'Tag', 'face'), f2);
            fprintf('picture %s %s\n', class(c2.CData), mat2str(size(c2.CData)));
            """);
        Assert.Contains(_output.NormalLines, static l => l == "copy uibuttongroup b 1");
        Assert.Contains(_output.NormalLines, static l => l == "picture double [1 1 3]");
    }
}
