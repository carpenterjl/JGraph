using System.IO.Compression;
using System.Text;
using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Completion;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Jgs.Completion;
using JGraph.Scripting.Startup;
using JGraph.Scripting.Workspace;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// U7 of the app-building plan (ADR 0204): App Designer apps. What R2025b does with an app, with a
/// graphics handle's class and with an <c>.mlapp</c> on the path is pinned by the parity fixtures
/// <c>u7_types</c>, <c>u7_app</c> and <c>u7_mlapp</c>. These tests hold what a recording made
/// without a display cannot: a person pushing the app's button and closing its window, told
/// through the seams the window uses; the package reader on its own; what the editor is told
/// about an <c>.mlapp</c>; and the faults found on the way.
/// </summary>
[Collection("JG facade")]
public class AppDesignerU7Tests : IAsyncLifetime
{
    private const string AppText = """
        classdef MiniApp < matlab.apps.AppBase

            % Properties that correspond to app components
            properties (Access = public)
                UIFigure  matlab.ui.Figure
                Field     matlab.ui.control.NumericEditField
                GoButton  matlab.ui.control.Button
            end

            properties (Access = private)
                Total = 0 % what the button has added up
            end

            methods (Access = private)

                % Code that executes after component creation
                function startupFcn(app, start)
                    if nargin > 1
                        app.Total = start;
                    end
                    fprintf('started %g\n', app.Total);
                end

                % Button pushed function: GoButton
                function GoButtonPushed(app, event)
                    app.Total = app.Total + app.Field.Value;
                    fprintf('pushed %s total %g\n', class(event), app.Total);
                end

                % Value changed function: Field
                function FieldValueChanged(app, event)
                    fprintf('changed %g<-%g\n', event.Value, event.PreviousValue);
                end

                % Close request function: UIFigure
                function UIFigureCloseRequest(app, event)
                    fprintf('close asked\n');
                    delete(app)
                end
            end

            methods (Access = private)

                % Create UIFigure and components
                function createComponents(app)
                    app.UIFigure = uifigure('Visible', 'off');
                    app.UIFigure.Position = [100 100 240 120];
                    app.UIFigure.Name = 'Mini';
                    app.UIFigure.CloseRequestFcn = createCallbackFcn(app, @UIFigureCloseRequest, true);

                    app.Field = uieditfield(app.UIFigure, 'numeric');
                    app.Field.ValueChangedFcn = createCallbackFcn(app, @FieldValueChanged, true);
                    app.Field.Tag = 'field';
                    app.Field.Position = [20 70 100 22];
                    app.Field.Value = 3;

                    app.GoButton = uibutton(app.UIFigure, 'push');
                    app.GoButton.ButtonPushedFcn = createCallbackFcn(app, @GoButtonPushed, true);
                    app.GoButton.Tag = 'go';
                    app.GoButton.Position = [20 30 100 22];
                    app.GoButton.Text = 'Go';

                    app.UIFigure.Visible = 'on';
                end
            end

            methods (Access = public)

                % Construct app
                function app = MiniApp(varargin)
                    createComponents(app)
                    registerApp(app, app.UIFigure)
                    runStartupFcn(app, @(app)startupFcn(app, varargin{:}))
                    if nargout == 0
                        clear app
                    end
                end

                % Code that executes before app deletion
                function delete(app)
                    fprintf('deleted\n');
                    delete(app.UIFigure)
                end
            end
        end
        """;

    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "jgraph-tests", Path.GetRandomFileName());
    private JgsReplSession _session = null!;

    public Task InitializeAsync()
    {
        JG.Reset();
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "MiniApp.m"), AppText);
        WriteMlapp("MlMini", AppText.Replace("MiniApp", "MlMini", StringComparison.Ordinal));
        _session = Assert.IsType<JgsReplSession>(((IScriptRepl)new MatlabScriptEngine()).CreateSession(
            new ScriptContext(_output, (_, _) => { }, _directory)));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        ScriptEventQueue.Flush();
        ScriptEventQueue.Flush();
        ScriptComponentFrames.SetSink(null);
        await _session.DisposeAsync();
        JG.Reset();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A file still held by the run that just ended; the temp folder is swept elsewhere.
        }
    }

    private const string ContentTypes = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default ContentType="application/vnd.openxmlformats-package.relationships+xml" Extension="rels"/>
          <Default ContentType="application/vnd.mathworks.matlab.code.document+xml;plaincode=true" Extension="xml"/>
        </Types>
        """;

    private static string Relationships(string target) => $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Target="{target}" Type="http://schemas.mathworks.com/matlab/code/2013/relationships/document"/>
        </Relationships>
        """;

    private static string Document(params string[] runs) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\" ?><w:document "
        + "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p>"
        + "<w:pPr><w:pStyle w:val=\"code\"/></w:pPr>"
        + string.Concat(runs.Select(static run => "<w:r><w:t><![CDATA[" + run + "]]></w:t></w:r>"))
        + "</w:p></w:body></w:document>";

    private string WriteMlapp(string name, string code, string part = "matlab/document.xml", bool withRelationships = true)
    {
        string path = Path.Combine(_directory, name + ".mlapp");
        using FileStream file = File.Create(path);
        using var package = new ZipArchive(file, ZipArchiveMode.Create);
        void Add(string entry, string text)
        {
            using var writer = new StreamWriter(package.CreateEntry(entry).Open(), new UTF8Encoding(false));
            writer.Write(text);
        }

        Add("[Content_Types].xml", ContentTypes);
        if (withRelationships)
        {
            Add("_rels/.rels", Relationships(part));
        }

        Add(part, Document(code.ReplaceLineEndings("\n")));
        return path;
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

    private static T Tagged<T>(string tag)
        where T : UiObject => Figure().Components.OfType<T>().First(c => c.Tag == tag);

    private bool Printed(string line) => _output.NormalLines.Contains(line);

    // --- a person using the app ----------------------------------------------------------------------

    [Fact]
    public async Task PushingTheButtonRunsThePrivateCallbackWithTheAppAndTheEvent()
    {
        await Exec("app = MiniApp(10);");
        Assert.True(Printed("started 10"), _output.NormalText);

        ScriptGraphicsCallbacks.NotifyComponent(Tagged<UiButtonModel>("go"), "pushed");
        await Drain();
        ScriptGraphicsCallbacks.NotifyComponent(Tagged<UiButtonModel>("go"), "pushed");
        await Drain();

        Assert.True(Printed("pushed matlab.ui.eventdata.ButtonPushedData total 13"), _output.NormalText);
        Assert.True(Printed("pushed matlab.ui.eventdata.ButtonPushedData total 16"), _output.NormalText);
    }

    [Fact]
    public async Task ATypedValueReachesTheCallbackAsItsEvent()
    {
        await Exec("app = MiniApp;");

        ScriptGraphicsCallbacks.NotifyComponent(Tagged<UiNumericModel>("field"), "value", 7.0);
        await Drain();

        Assert.True(Printed("changed 7<-3"), _output.NormalText);
    }

    [Fact]
    public async Task ClosingTheWindowAsksTheApp_WhichDeletesItselfAndItsFigure()
    {
        await Exec("app = MiniApp;");

        ScriptEventQueue.Enqueue(new GraphicsEvent(GraphicsEventKind.CloseRequest, Figure()));
        await Drain();
        await Exec("fprintf('valid %d figures %d\\n', isvalid(app), numel(findall(groot, 'Type', 'figure')));");

        Assert.True(Printed("close asked"), _output.NormalText);
        Assert.Equal(1, _output.NormalLines.Count(static line => line == "deleted"));
        Assert.True(Printed("valid 0 figures 0"), _output.NormalText);
    }

    [Fact]
    public async Task AnAppRunAsAStatementLeavesNoAns_AndGoesOnRunningAfterItsVariableIsCleared()
    {
        await Exec("""
            MiniApp(1);
            fprintf('ans %d\n', exist('ans', 'var'));
            app = MiniApp(2);
            clear app
            fprintf('running %d of %d\n', numel(findall(groot, 'Type', 'figure', '-property', 'RunningAppInstance')), ...
                numel(findall(groot, 'Type', 'figure', '-property', 'Name')));
            """);
        Assert.True(Printed("ans 0"), _output.NormalText);
        Assert.True(Printed("running 2 of 2"), _output.NormalText); // how a script finds the apps that are running
        Assert.Equal(2, JG.FigureNumbers.Count);

        // Both apps are held by their figures alone, and both still answer their buttons.
        foreach (int number in JG.FigureNumbers)
        {
            Assert.True(JG.TryGetFigure(number, out FigureModel figure));
            ScriptGraphicsCallbacks.NotifyComponent(figure.Components.OfType<UiButtonModel>().Single(), "pushed");
            await Drain();
        }

        Assert.True(Printed("pushed matlab.ui.eventdata.ButtonPushedData total 4"), _output.NormalText);
        Assert.True(Printed("pushed matlab.ui.eventdata.ButtonPushedData total 5"), _output.NormalText);
        Assert.DoesNotContain("deleted", _output.NormalLines);
    }

    [Fact]
    public async Task AnAppRunsFromItsMlapp_AndTheMlappIsFoundBeforeAnMOfItsName()
    {
        File.WriteAllText(Path.Combine(_directory, "MlMini.m"), "classdef MlMini\n    properties\n        FromM = true\n    end\nend\n");

        await Exec("""
            app = MlMini(4);
            [~, name, ext] = fileparts(which('MlMini'));
            fprintf('%s%s %s %d\n', name, ext, class(app), isa(app, 'matlab.apps.AppBase'));
            """);
        ScriptGraphicsCallbacks.NotifyComponent(Tagged<UiButtonModel>("go"), "pushed");
        await Drain();

        Assert.True(Printed("MlMini.mlapp MlMini 1"), _output.NormalText);
        Assert.True(Printed("pushed matlab.ui.eventdata.ButtonPushedData total 7"), _output.NormalText);
    }

    [Fact]
    public async Task RunningAClassFileAsAFileConstructsIt_WhichIsHowAnAppIsStarted()
    {
        string path = Path.Combine(_directory, "MiniApp.m");
        ScriptRunResult result = await _session.ExecuteFileAsync(File.ReadAllText(path), path, CancellationToken.None);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.True(Printed("started 0"), _output.NormalText);
        Assert.Single(JG.FigureNumbers);
    }

    [Fact]
    public async Task ClassTextThatIsNotAFileNamedForItsClassDefinesAndConstructsNothing()
    {
        string path = Path.Combine(_directory, "other.m");
        ScriptRunResult result = await _session.ExecuteFileAsync(AppText, path, CancellationToken.None);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Empty(JG.FigureNumbers);
    }

    // --- the faults found on the way -------------------------------------------------------------------

    [Fact]
    public async Task ObjectBeingDestroyedIsHeardBeforeTheClassesOwnDelete_AndTheObjectIsAlreadyInvalidInBoth()
    {
        File.WriteAllText(Path.Combine(_directory, "Goes.m"), """
            classdef Goes < handle
                properties
                    Name = 'g'
                end
                methods
                    function delete(obj)
                        fprintf('delete valid=%d name=%s\n', isvalid(obj), obj.Name);
                    end
                end
            end
            """);

        await Exec("""
            g = Goes;
            addlistener(g, 'ObjectBeingDestroyed', @(s, e) fprintf('listener valid=%d name=%s\n', isvalid(s), s.Name));
            delete(g);
            fprintf('after %d\n', isvalid(g));
            """);

        int listener = _output.NormalLines.ToList().IndexOf("listener valid=0 name=g");
        int destructor = _output.NormalLines.ToList().IndexOf("delete valid=0 name=g");
        Assert.True(listener >= 0 && destructor > listener, _output.NormalText);
        Assert.True(Printed("after 0"), _output.NormalText);
    }

    [Fact]
    public async Task AMethodTakesACallOnlyWhenOneOfItsObjectsIsAmongTheArguments()
    {
        // A class's own method named for a built-in used to take every call of that name made
        // from the class's code, object or not: delete(app.UIFigure) ran the app's delete.
        File.WriteAllText(Path.Combine(_directory, "Shows.m"), """
            classdef Shows < handle
                methods
                    function disp(obj)
                        fprintf('a Shows\n');
                    end
                    function both(obj)
                        disp(5);
                        disp(obj);
                    end
                end
            end
            """);

        await Exec("s = Shows; both(s);");

        Assert.Equal(new[] { "5", "a Shows" }, _output.NormalLines.Select(static line => line.Trim()).ToArray());
    }

    [Fact]
    public async Task AWriteThroughAHandleHeldByAStructFieldOrAnObjectPropertyReachesTheObject()
    {
        File.WriteAllText(Path.Combine(_directory, "Holds.m"), "classdef Holds < handle\n    properties\n        H\n    end\nend\n");

        await Exec("""
            f = uifigure('Visible', 'off');
            b = uibutton(f);
            s.h = b;
            s.h.Text = 'struct';
            first = b.Text;
            o = Holds;
            o.H = b;
            o.H.Text = 'object';
            fprintf('%s %s %d\n', first, b.Text, isstruct(s.h));
            """);

        Assert.True(Printed("struct object 0"), _output.NormalText);
    }

    [Fact]
    public async Task AMethodKnowsItsOwnFile_AndAnErrorInItIsNamedClassDotMethod()
    {
        File.WriteAllText(Path.Combine(_directory, "Where.m"), """
            classdef Where
                methods
                    function r = file(~)
                        r = mfilename;
                    end
                    function fail(~)
                        error('U7:where', 'here');
                    end
                end
            end
            """);

        await Exec("""
            w = Where;
            try
                fail(w);
            catch err
                fprintf('%s %s %d\n', file(w), err.stack(1).name, err.stack(1).line);
            end
            """);

        Assert.True(Printed("Where Where.fail 7"), _output.NormalText);
    }

    [Fact]
    public async Task TooManyArgumentsToAMatlabFunctionIsRefusedInMatlabsWords()
    {
        File.WriteAllText(Path.Combine(_directory, "takes_one.m"), "function takes_one(a)\nend\n");

        await Exec("""
            try
                takes_one(1, 2);
            catch err
                fprintf('%s | %s\n', err.identifier, err.message);
            end
            """);

        Assert.True(Printed("MATLAB:TooManyInputs | Too many input arguments."), _output.NormalText);
    }

    [Fact]
    public async Task APropertyWithNoDefaultStartsAsTheZeroByZeroEmpty()
    {
        File.WriteAllText(Path.Combine(_directory, "Blank.m"), "classdef Blank\n    properties\n        A\n        B double\n        F matlab.ui.Figure\n    end\nend\n");

        await Exec("b = Blank; fprintf('%s %s %s\\n', mat2str(size(b.A)), mat2str(size(b.B)), mat2str(size(b.F)));");

        Assert.True(Printed("[0 0] [0 0] [0 0]"), _output.NormalText);
    }

    [Fact]
    public async Task APropertyListenerOnAGraphicsObjectIsRefusedAsUnsupported()
    {
        string message = await Fails("f = uifigure('Visible', 'off'); addlistener(f, 'Name', 'PostSet', @(s, e) 1);");

        Assert.Contains("a property listener on a graphics object is not supported", message, StringComparison.Ordinal);
    }

    // --- the package reader --------------------------------------------------------------------------

    [Fact]
    public void TheCodeIsReadFromThePartTheRelationshipsName_EveryRunOfItJoined()
    {
        string moved = WriteMlapp("Moved", "classdef Moved\nend\n", part: "code/main.xml");
        Assert.Equal("classdef Moved\nend\n", JgsMlapp.ReadCode(moved));

        string path = Path.Combine(_directory, "Runs.mlapp");
        using (FileStream file = File.Create(path))
        using (var package = new ZipArchive(file, ZipArchiveMode.Create))
        {
            void Add(string entry, string text)
            {
                using var writer = new StreamWriter(package.CreateEntry(entry).Open(), new UTF8Encoding(false));
                writer.Write(text);
            }

            Add("_rels/.rels", Relationships("/matlab/document.xml"));
            Add("matlab/document.xml", Document("classdef Runs\n", "    % a < b && c > d\n", "end\n"));
        }

        Assert.Equal("classdef Runs\n    % a < b && c > d\nend\n", JgsMlapp.ReadCode(path));
    }

    [Fact]
    public void APackageWithoutRelationships_AndAFileThatIsNoPackage_HoldNoCode()
    {
        string bare = WriteMlapp("Bare", "classdef Bare\nend\n", withRelationships: false);
        string text = Path.Combine(_directory, "Text.mlapp");
        File.WriteAllText(text, "classdef Text\nend\n");

        Assert.Throws<InvalidDataException>(() => JgsMlapp.ReadCode(bare));
        Assert.Throws<InvalidDataException>(() => JgsMlapp.ReadCode(text));
        Assert.Equal("x = 1;", JgsMlapp.ReadSource(WriteText("plain.m", "x = 1;")));
    }

    private string WriteText(string name, string text)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, text);
        return path;
    }

    // --- what the editor and the launcher are told ---------------------------------------------------

    [Fact]
    public void AnMlappIsADocumentOfMatlabCodeThatIsNotSavedOverItself()
    {
        string path = Path.Combine(_directory, "MlMini.mlapp");
        var document = new ScriptDocumentModel(path, ScriptDocumentModel.ReadFile(path));

        Assert.Equal(WorkspaceFileKind.Document, WorkspaceFiles.Classify(path));
        Assert.Equal("MATLAB", document.Language);
        Assert.True(document.IsAppDesignerFile);
        Assert.StartsWith("classdef MlMini < matlab.apps.AppBase", document.Text, StringComparison.Ordinal);
        Assert.False(new ScriptDocumentModel(Path.Combine(_directory, "MiniApp.m"), AppText).IsAppDesignerFile);
        Assert.Contains(".mlapp", ScriptWorkspace.ScriptExtensions);
    }

    [Fact]
    public void TheLauncherRunsAnMlappsCodeAsMatlab()
    {
        ResolvedStatement resolved = StartupStatement.Resolve("MlMini.mlapp", _directory);

        Assert.Equal("MATLAB", resolved.Language);
        Assert.StartsWith("classdef MlMini < matlab.apps.AppBase", resolved.Code, StringComparison.Ordinal);
        Assert.EndsWith("MlMini.mlapp", resolved.SourcePath, StringComparison.Ordinal);
    }

    // --- app. in the editor ----------------------------------------------------------------------------

    [Fact]
    public void AfterTheObjectsNameInAClassFile_TheClassesMembersAndTheInheritedOnesAreOffered()
    {
        string code = AppText.ReplaceLineEndings("\n");
        int offset = code.IndexOf("app.Total = start;", StringComparison.Ordinal) + "app.".Length;

        JgsCompletionResult result = JgsCompletionEngine.GetCompletions(code, offset, matlab: true);
        string[] offered = [.. result.Items.Select(static item => item.Text)];

        Assert.Contains("UIFigure", offered);
        Assert.Contains("GoButton", offered);
        Assert.Contains("Total", offered);
        Assert.Contains("GoButtonPushed", offered);
        Assert.Contains("createCallbackFcn", offered);
        Assert.Contains("registerApp", offered);
        Assert.Contains("delete", offered);
        Assert.DoesNotContain("MiniApp", offered);
        Assert.DoesNotContain("sin", offered);
        Assert.Equal("createCallbackFcn(app, callback, requiresEventData)",
            result.Items.First(static item => item.Text == "createCallbackFcn").Signature);
    }

    [Fact]
    public void ATypedPrefixNarrowsTheMembers_AndAnyOtherNameIsNotTheObject()
    {
        string code = AppText.ReplaceLineEndings("\n");
        int typed = code.IndexOf("app.GoButton = uibutton", StringComparison.Ordinal) + "app.Go".Length;

        string[] narrowed = [.. JgsCompletionEngine.GetCompletions(code, typed, matlab: true).Items.Select(static item => item.Text)];

        Assert.Equal(new[] { "GoButton", "GoButtonPushed" }, narrowed);
        Assert.Null(ClassCompletion.Members(code, "event"));
        Assert.Null(ClassCompletion.Members(code, "app.UIFigure"));
        Assert.Null(ClassCompletion.Members("x = 1;\napp.", "app"));
    }
}
