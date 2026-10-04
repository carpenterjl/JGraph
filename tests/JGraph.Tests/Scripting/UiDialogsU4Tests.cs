using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// U4 of the app-building plan (ADR 0201): blocking, and the classic dialogs. The dialogs that hold
/// a script are answered here through the seam a test stands in for the user with — it is told of
/// the dialog's figure once it is built, and presses its controls the way the window does — so what
/// is exercised is the whole path: the figure, its controls, their callbacks, <c>uiwait</c> and the
/// value handed back.
/// </summary>
[Collection("JG facade")]
public class UiDialogsU4Tests : IAsyncLifetime
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
        ScriptGraphicsCallbacks.BlockingDialogShown = null;
        ScriptGraphicsCallbacks.NativeDialogs = null;
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

    private static UiControlModel Tagged(FigureModel figure, string tag) => Controls(figure).First(c => c.Tag == tag);

    private static void Press(FigureModel figure, string tag) =>
        ScriptGraphicsCallbacks.NotifyUserValue(Tagged(figure, tag), null);

    // --- questdlg -------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Btn1", "Yes")]
    [InlineData("Btn2", "No")]
    [InlineData("Btn3", "Cancel")]
    public async Task AQuestionAnswersTheButtonThatWasPressed(string tag, string expected)
    {
        ScriptGraphicsCallbacks.BlockingDialogShown = figure => Press(figure, tag);
        await Exec("a = questdlg('Do you want to continue?', 'Question'); fprintf('[%s]\\n', a);");
        Assert.Contains($"[{expected}]", _output.NormalLines);
    }

    [Fact]
    public async Task AQuestionIsAModalFigureBuiltFromControls_AndIsGoneOnceAnswered()
    {
        var seen = new List<string>();
        ScriptGraphicsCallbacks.BlockingDialogShown = figure =>
        {
            seen.Add($"{figure.Name}|{figure.WindowStyle}|{figure.Visible}|{figure.Resizable}|{figure.MenuBar}");
            seen.Add(string.Join(",", Controls(figure).Select(static c => $"{c.Tag}:{c.Text.Joined}")));
            seen.Add($"axes={figure.Axes.Count}");
            Press(figure, "Btn2");
        };
        await Exec("a = questdlg('Keep the changes?', 'Two', 'Keep', 'Discard', 'Keep'); fprintf('[%s] %d\\n', a, numel(findall(0, 'Type', 'figure')));");

        Assert.Equal("Two|Modal|True|False|False", seen[0]);
        Assert.Equal("Btn1:Keep,Btn2:Discard", seen[1]);
        Assert.Equal("axes=2", seen[2]);
        Assert.Contains("[Discard] 0", _output.NormalLines);
    }

    [Fact]
    public async Task ReturnOnTheFigureAnswersTheDefault_AndEscapeAnswersNothing()
    {
        ScriptGraphicsCallbacks.BlockingDialogShown = figure =>
            ScriptGraphicsCallbacks.NotifyKey(figure, pressed: true, "\r", "return", []);
        await Exec("a = questdlg('Proceed?', 'T', 'Go', 'Stop', 'Stop'); fprintf('[%s]\\n', a);");
        Assert.Contains("[Stop]", _output.NormalLines);

        ScriptGraphicsCallbacks.BlockingDialogShown = figure =>
            ScriptGraphicsCallbacks.NotifyKey(figure, pressed: true, "\u001b", "escape", []);
        await Exec("a = questdlg('Proceed?', 'T', 'Go', 'Stop', 'Stop'); fprintf('<%s> %d\\n', a, isempty(a));");
        Assert.Contains("<> 1", _output.NormalLines);
    }

    [Fact]
    public async Task ClosingAQuestionAnswersEmpty_AndADefaultNoButtonNamesWarns()
    {
        ScriptGraphicsCallbacks.BlockingDialogShown = figure =>
            ScriptEventQueue.Enqueue(new GraphicsEvent(GraphicsEventKind.CloseRequest, figure));
        await Exec("""
            lastwarn('');
            a = questdlg('Proceed?', 'T', 'Go', 'Stop', 'Neither');
            [~, id] = lastwarn;
            fprintf('<%s> %s\n', a, id);
            """);
        Assert.Contains("<> MATLAB:questdlg:StringMismatch", _output.NormalLines);
    }

    // --- inputdlg -------------------------------------------------------------------------------------

    [Fact]
    public async Task AnInputDialogAnswersWhatItsFieldsHold_AsAColumnCell()
    {
        ScriptGraphicsCallbacks.BlockingDialogShown = figure =>
        {
            List<UiControlModel> edits = [.. Controls(figure).Where(static c => c.Tag == "Edit")];
            Assert.Equal(2, edits.Count);
            ScriptGraphicsCallbacks.NotifyUserValue(edits[0], "Grace");
            Press(figure, "OK");
        };
        await Exec("""
            a = inputdlg({'Name:', 'Age:'}, 'Input', [1 35], {'Ada', '36'});
            fprintf('%s %s %s|%s\n', class(a), mat2str(size(a)), a{1}, a{2});
            """);
        Assert.Contains("cell [2 1] Grace|36", _output.NormalLines);
    }

    [Fact]
    public async Task ACancelledInputDialogAnswersAnEmptyCell()
    {
        ScriptGraphicsCallbacks.BlockingDialogShown = figure => Press(figure, "Cancel");
        await Exec("a = inputdlg('Single value:'); fprintf('%s %s\\n', class(a), mat2str(size(a)));");
        Assert.Contains("cell [0 0]", _output.NormalLines);
    }

    [Fact]
    public async Task AnInputDialogsFieldsFollowItsDimensions()
    {
        var seen = new List<string>();
        ScriptGraphicsCallbacks.BlockingDialogShown = figure =>
        {
            UiControlModel edit = Tagged(figure, "Edit");
            seen.Add($"{edit.Max}|{edit.Style}|{figure.Name}");
            Press(figure, "OK");
        };
        await Exec("a = inputdlg({'Notes:'}, 'Lines', [5 50]); disp(size(a));");
        Assert.Equal("5|Edit|Lines", seen[0]);
        string refused = await Fails("inputdlg({'a', 'b'}, 't', [1 2 3])");
        Assert.Contains("NumLines size is incorrect.", refused);
    }

    // --- listdlg --------------------------------------------------------------------------------------

    [Fact]
    public async Task AListDialogAnswersTheItemsPickedAndWhetherItWasAccepted()
    {
        ScriptGraphicsCallbacks.BlockingDialogShown = figure =>
        {
            ScriptGraphicsCallbacks.NotifyUserValue(Tagged(figure, "listbox"), new double[] { 2, 3 });
            Press(figure, "ok_btn");
        };
        await Exec("""
            [s, ok] = listdlg('ListString', {'alpha', 'beta', 'gamma'});
            fprintf('%s %d\n', mat2str(s), ok);
            """);
        Assert.Contains("[2 3] 1", _output.NormalLines);

        ScriptGraphicsCallbacks.BlockingDialogShown = figure => Press(figure, "cancel_btn");
        await Exec("""
            [s, ok] = listdlg('ListString', {'one', 'two'}, 'SelectionMode', 'single', 'PromptString', 'Pick one', 'Name', 'Chooser');
            fprintf('%s %d %d\n', mat2str(size(s)), ok, numel(findall(0, 'Type', 'figure')));
            """);
        Assert.Contains("[0 0] 0 0", _output.NormalLines);
    }

    [Fact]
    public async Task SelectAllPicksEveryItem_AndIsOnlyThereWhereSeveralMayBePicked()
    {
        var tags = new List<string>();
        ScriptGraphicsCallbacks.BlockingDialogShown = figure =>
        {
            tags.Add(string.Join(",", Controls(figure).Select(static c => c.Tag).OrderBy(static t => t, StringComparer.Ordinal)));
            if (Controls(figure).Any(static c => c.Tag == "selectall_btn"))
            {
                Press(figure, "selectall_btn");
            }

            Press(figure, "ok_btn");
        };
        await Exec("[s, ok] = listdlg('ListString', {'a', 'b', 'c'}); fprintf('%s\\n', mat2str(s));");
        await Exec("[s, ok] = listdlg('ListString', {'a', 'b', 'c'}, 'SelectionMode', 'single'); fprintf('%s\\n', mat2str(s));");
        Assert.Equal("cancel_btn,listbox,ok_btn,selectall_btn", tags[0]);
        Assert.Equal("cancel_btn,listbox,ok_btn", tags[1]);
        Assert.Contains("[1 2 3]", _output.NormalLines);
        Assert.Contains("1", _output.NormalLines);
    }

    // --- with nobody to answer ------------------------------------------------------------------------

    [Fact]
    public async Task WithNobodyToAnswer_TheBlockingDialogsRefuseInR2025bsWords()
    {
        foreach (string call in new[] { "questdlg('a')", "inputdlg('a')", "listdlg('ListString', {'a'})", "uigetfile()", "uisetcolor()" })
        {
            string message = await Fails(call);
            Assert.Contains("Creating dialog boxes that block execution is not supported", message);
        }

        Assert.Empty(JG.FigureNumbers);
    }

    // --- uiwait and uiresume in a session -------------------------------------------------------------

    [Fact]
    public async Task UiwaitHoldsTheStatement_UntilAControlsCallbackResumes()
    {
        // The guidata pattern (research C, ex2): the figure keeps a struct, the button's callback
        // writes the answer into it and resumes, and the waiting code reads it back.
        ScriptGraphicsCallbacks.BlockingDialogShown = static _ => { };
        Task run = Exec("""
            f = figure('Visible', 'off', 'Tag', 'settings');
            h = guihandles(f);
            h.figure = f;
            h.edit = uicontrol(f, 'Style', 'edit', 'Tag', 'gain', 'String', '1');
            h.result = [];
            ok = uicontrol(f, 'Style', 'pushbutton', 'Tag', 'ok', 'Callback', ...
                'd = guidata(gcbo); d.result = str2double(get(d.edit, ''String'')); guidata(gcbo, d); uiresume(d.figure);');
            guidata(f, h);
            uiwait(f);
            d = guidata(f);
            fprintf('result=%g status=%s visible=%s\n', d.result, get(f, 'WaitStatus'), char(get(f, 'Visible')));
            """);

        FigureModel figure = await WaitForFigure(static f => JgsBuiltins.WaitStatusOf(f) == "waiting");
        ScriptGraphicsCallbacks.NotifyUserValue(Tagged(figure, "gain"), "2.5");
        Press(figure, "ok");
        await run.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Contains("result=2.5 status=inactive visible=on", _output.NormalLines);
    }

    [Fact]
    public async Task WaitforEndsWhenAUsersClickWritesTheValueItWaitsFor()
    {
        ScriptGraphicsCallbacks.BlockingDialogShown = static _ => { };
        Task run = Exec("""
            f = figure('Visible', 'off');
            go = uicontrol(f, 'Style', 'togglebutton', 'Tag', 'go');
            set(f, 'UserData', 'ready');
            waitfor(go, 'Value', 1);
            disp('went');
            """);

        FigureModel figure = await WaitForFigure(static f => f.Components.Count > 0);
        await Task.Delay(100);
        ScriptGraphicsCallbacks.NotifyUserValue(Tagged(figure, "go"), 1.0);
        await run.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Contains("went", _output.NormalLines);
    }

    [Fact]
    public async Task AWaitNothingCanEndReturns_WithR2025bsWarning()
    {
        await Exec("""
            f = figure('Visible', 'off');
            lastwarn('');
            t = tic; uiwait(f); e = toc(t);
            [~, id] = lastwarn;
            fprintf('%d %s %s\n', e < 5, id, get(f, 'WaitStatus'));
            t = tic; waitfor(f, 'Name', 'never'); e = toc(t);
            fprintf('waitfor %d\n', e < 5);
            """);
        Assert.Contains("1 MATLAB:hg:NoDisplayNoFigureSupportSeeReleaseNotes inactive", _output.NormalLines);
        Assert.Contains("waitfor 1", _output.NormalLines);
    }

    private static async Task<FigureModel> WaitForFigure(Func<FigureModel, bool> ready)
    {
        for (int i = 0; i < 400; i++)
        {
            foreach (int number in JG.FigureNumbers)
            {
                if (JG.TryGetFigure(number, out FigureModel figure) && ready(figure))
                {
                    return figure;
                }
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("The script never reached its wait.");
    }

    // --- message boxes and the waitbar ---------------------------------------------------------------

    [Fact]
    public async Task AMessageBoxsButtonAndItsKeysTakeItDown()
    {
        await Exec("m = msgbox('Saved.', 'Done');");
        FigureModel box = Dialogs().Single();
        Press(box, "OKButton");
        await Drain();
        Assert.Empty(Dialogs());

        await Exec("m = errordlg('Bad value', 'Oops', 'modal');");
        box = Dialogs().Single();
        Assert.Equal(FigureWindowStyle.Modal, box.WindowStyle);
        Assert.False(box.MenuBar);
        Assert.False(box.ShowsToolBar);
        ScriptGraphicsCallbacks.NotifyKey(box, pressed: true, " ", "space", []);
        await Drain();
        Assert.Empty(Dialogs());
    }

    [Fact]
    public async Task AWaitbarsBarIsAFilledPartOfItsFrame()
    {
        await Exec("w = waitbar(0.25, 'Working...');");
        FigureModel bar = Dialogs().Single();
        UiPanelFrame frame = Assert.IsType<UiPanelFrame>(UiFrame.Take(bar).Roots.Single());
        Assert.Equal(0.25, frame.Fill);
        Assert.NotNull(frame.FillColor);

        await Exec("waitbar(0.6, w, 'Nearly');");
        frame = Assert.IsType<UiPanelFrame>(UiFrame.Take(bar).Roots.Single());
        Assert.Equal(0.6, frame.Fill);
        Assert.Equal("Nearly", bar.Axes.Single().Title);
    }

    [Fact]
    public async Task AFiguresMenuBarDecidesItsFurniture_AndItsToolBarFollowsUnlessToldOtherwise()
    {
        await Exec("""
            a = figure('Visible', 'off');
            b = figure('Visible', 'off', 'MenuBar', 'none');
            c = figure('Visible', 'off', 'MenuBar', 'none', 'ToolBar', 'figure');
            d = figure('Visible', 'off', 'ToolBar', 'none');
            fprintf('%s %s %s %s\n', get(a, 'MenuBar'), get(b, 'MenuBar'), get(c, 'MenuBar'), get(d, 'MenuBar'));
            """);
        Assert.Contains("figure none none figure", _output.NormalLines);
        bool[] shows = [.. new[] { 1, 2, 3, 4 }.Select(n => JG.TryGetFigure(n, out FigureModel f) && f.ShowsToolBar)];
        Assert.Equal([true, false, true, false], shows);
    }

    private static List<FigureModel> Dialogs()
    {
        var found = new List<FigureModel>();
        foreach (int number in JG.FigureNumbers)
        {
            if (JG.TryGetFigure(number, out FigureModel figure))
            {
                found.Add(figure);
            }
        }

        return found;
    }

    private Task Drain() =>
        ((IGraphicsEventSession)_session).DrainGraphicsEventsAsync(null, CancellationToken.None);

    // --- the system's dialogs, behind a stand-in ------------------------------------------------------

    private sealed class FakeDialogs : IScriptNativeDialogs
    {
        public List<string> Asked { get; } = [];

        public IReadOnlyList<string>? Open { get; set; }

        public string? Save { get; set; }

        public string? Folder { get; set; }

        public (double, double, double)? Color { get; set; }

        public ScriptFontChoice? Font { get; set; }

        public (IReadOnlyList<string> Paths, int FilterIndex)? OpenFiles(
            string title, IReadOnlyList<ScriptFileFilter> filters, string? initialPath, bool multiSelect)
        {
            Asked.Add($"open|{title}|{string.Join(";", filters.Select(static f => f.Pattern + "=" + f.Description))}|{multiSelect}");
            return Open is null ? null : (Open, 2);
        }

        public (string Path, int FilterIndex)? SaveFile(string title, IReadOnlyList<ScriptFileFilter> filters, string? initialPath)
        {
            Asked.Add($"save|{title}|{Path.GetFileName(initialPath)}");
            return Save is null ? null : (Save, 1);
        }

        public string? PickFolder(string title, string? initialPath)
        {
            Asked.Add($"folder|{title}");
            return Folder;
        }

        public (double R, double G, double B)? PickColor(string title, (double R, double G, double B)? initial)
        {
            Asked.Add($"color|{title}|{initial}");
            return Color;
        }

        public ScriptFontChoice? PickFont(string title, ScriptFontChoice? initial)
        {
            Asked.Add($"font|{title}|{initial?.Name}|{initial?.SizePoints}");
            return Font;
        }
    }

    [Fact]
    public async Task UigetfileAnswersTheNameTheFolderAndTheFilter_AndZerosWhenCancelled()
    {
        var dialogs = new FakeDialogs { Open = [@"C:\data\run 1.mat"] };
        ScriptGraphicsCallbacks.NativeDialogs = dialogs;
        await Exec("""
            [f, p, k] = uigetfile({'*.mat', 'Data'; '*.csv', 'Tables'}, 'Pick a run');
            fprintf('%s|%s|%d\n', f, p, k);
            """);
        Assert.Contains(@"run 1.mat|C:\data\|2", _output.NormalLines);
        Assert.Equal("open|Pick a run|*.mat=Data;*.csv=Tables;*.*=All Files (*.*)|False", dialogs.Asked[0]);

        dialogs.Open = [@"C:\data\a.m", @"C:\data\b.m"];
        await Exec("""
            [f, p] = uigetfile('*.m', 'Several', 'MultiSelect', 'on');
            fprintf('%s %s %s %s\n', class(f), mat2str(size(f)), f{2}, p);
            """);
        Assert.Contains(@"cell [1 2] b.m C:\data\", _output.NormalLines);

        dialogs.Open = null;
        await Exec("[f, p, k] = uigetfile('*.m'); fprintf('%d %d %d %s\\n', f, p, k, class(f));");
        Assert.Contains("0 0 0 double", _output.NormalLines);
    }

    [Fact]
    public async Task UiputfileUigetdirUisetcolorAndUisetfontAnswerWhatWasChosen()
    {
        var dialogs = new FakeDialogs
        {
            Save = @"C:\out\result.png",
            Folder = @"C:\out",
            Color = (0.2, 0.4, 0.6),
            Font = new ScriptFontChoice("Consolas", 11, Bold: true, Italic: false),
        };
        ScriptGraphicsCallbacks.NativeDialogs = dialogs;
        await Exec("""
            [f, p] = uiputfile('*.png', 'Save the picture', 'result.png');
            fprintf('%s|%s\n', f, p);
            d = uigetdir('C:\', 'Where to');
            fprintf('%s\n', d);
            fig = figure('Visible', 'off');
            c = uisetcolor(fig, 'Background');
            fprintf('%s %s\n', mat2str(c), mat2str(get(fig, 'Color')));
            s = uisetfont;
            fprintf('%s %g %s %s %s\n', s.FontName, s.FontSize, s.FontWeight, s.FontAngle, s.FontUnits);
            t = uicontrol(fig, 'Style', 'text');
            uisetfont(t, 'Pick');
            fprintf('%s %g %s\n', get(t, 'FontName'), get(t, 'FontSize'), get(t, 'FontWeight'));
            """);

        Assert.Contains(@"result.png|C:\out\", _output.NormalLines);
        Assert.Contains(@"C:\out", _output.NormalLines);
        Assert.Contains("[0.2 0.4 0.6] [0.2 0.4 0.6]", _output.NormalLines);
        Assert.Contains("Consolas 11 bold normal points", _output.NormalLines);
        Assert.Contains("Consolas 11 bold", _output.NormalLines);
        Assert.Contains("save|Save the picture|result.png", dialogs.Asked);
        Assert.Contains("folder|Where to", dialogs.Asked);

        dialogs.Color = null;
        dialogs.Font = null;
        dialogs.Folder = null;
        await Exec("fprintf('%s %d %d %d\\n', mat2str(uisetcolor([1 0 0])), uisetcolor, uisetfont, uigetdir);");
        Assert.Contains("[1 0 0] 0 0 0", _output.NormalLines);
    }

    [Fact]
    public async Task UisaveAndUiloadGoThroughTheFileTheUserChose()
    {
        string folder = Path.Combine(Path.GetTempPath(), "jgraph-u4-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string file = Path.Combine(folder, "kept.mat");
            ScriptGraphicsCallbacks.NativeDialogs = new FakeDialogs { Save = file, Open = [file] };
            await Exec("""
                alpha = 7; beta = 'two';
                uisave({'alpha', 'beta'}, 'kept');
                clear alpha beta
                uiload
                fprintf('%d %s\n', alpha, beta);
                """);
            Assert.True(File.Exists(file));
            Assert.Contains("7 two", _output.NormalLines);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
