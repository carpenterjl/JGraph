using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Workspace;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// U7b of the app-building plan (ADR 0205): editing an App Designer file and saving the edit back
/// into it. The sample is <c>U7bApp.mlapp</c>, an app of this project's own written by R2025b's
/// serializer (probe <c>u7b_build</c>), so it holds what App Designer's save writes: the code, the
/// component tree as MCOS objects, and App Designer's own copy of the code. What R2025b makes of a
/// file saved here is the probe <c>u7b_verify</c>'s to say; these tests hold the save to its own
/// rules, and - where R2025b is installed - to every app R2025b ships, read in place.
/// </summary>
[Collection("JG facade")]
public class AppDesignerU7bTests : IAsyncLifetime
{
    private const string Model = "appdesigner/appModel.mat";
    private const string Document = "matlab/document.xml";

    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "jgraph-tests", Path.GetRandomFileName());
    private JgsReplSession _session = null!;
    private string _app = null!;
    private string _text = null!;

    public Task InitializeAsync()
    {
        JG.Reset();
        Directory.CreateDirectory(_directory);
        _app = Path.Combine(_directory, "U7bApp.mlapp");
        File.Copy(Helper("U7bApp.mlapp"), _app);
        _text = JgsMlapp.ReadCode(_app);
        _session = Assert.IsType<JgsReplSession>(((IScriptRepl)new MatlabScriptEngine()).CreateSession(
            new ScriptContext(_output, (_, _) => { }, _directory)));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
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

    private static string Helper(string name) =>
        Path.Combine(AppContext.BaseDirectory, "MatlabParity", "fixtures", "helpers", name);

    private static Dictionary<string, byte[]> Parts(string path)
    {
        using ZipArchive package = ZipFile.OpenRead(path);
        var parts = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (ZipArchiveEntry entry in package.Entries)
        {
            using Stream stream = entry.Open();
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            parts.Add(entry.FullName, bytes.ToArray());
        }

        return parts;
    }

    private static List<string> Order(string path)
    {
        using ZipArchive package = ZipFile.OpenRead(path);
        return package.Entries.Select(static e => e.FullName).ToList();
    }

    /// <summary>The text with <paramref name="line"/> put in after the first line that contains <paramref name="after"/>.</summary>
    private static string Insert(string text, string after, string line)
    {
        List<string> lines = [.. text.Split('\n')];
        int at = lines.FindIndex(l => l.Contains(after, StringComparison.Ordinal));
        Assert.True(at >= 0, after);
        lines.Insert(at + 1, line);
        return string.Join('\n', lines);
    }

    private static MlappCodeCopy Copy(string path) =>
        JgsMlappModel.Read(Parts(path)[Model]) ?? throw new InvalidOperationException("no code copy");

    private async Task Exec(string code)
    {
        ScriptRunResult result = await _session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    // --- which lines are whose -----------------------------------------------------------------------

    [Fact]
    public void AnAppsTextIsCutIntoTheEditableSectionAndEachFunctionOfTheCallbacksBlock()
    {
        JgsMlappLayout layout = JgsMlappLayout.Of(_text);

        Assert.True(layout.IsAppLayout);
        Assert.True(layout.IsComplete);
        Assert.Equal("U7bApp", layout.ClassName);
        Assert.Equal("amp", layout.InputParameters);
        Assert.Equal(
            [MlappRegionKind.EditableSection, MlappRegionKind.Startup, MlappRegionKind.Callback, MlappRegionKind.Callback],
            layout.Regions.Select(static r => r.Kind));
        Assert.Equal(["", "startupFcn", "GoButtonPushed", "ClearButtonPushed"], layout.Regions.Select(static r => r.Name));

        // The section is recorded without the empty line App Designer puts on each side of it.
        IReadOnlyList<string> section = layout.EditableSectionCode;
        Assert.Equal(12, section.Count);
        Assert.Equal("    ", section[0]);
        Assert.Equal("    properties (Access = public)", section[1]);
        Assert.Equal("    ", section[^1]);

        // A body is every line between the signature and the function's own end, the loop's end included.
        Assert.Equal(
            ["            for k = 1:2", "                note(app, sprintf('go %g', k * app.AmpField.Value));", "            end"],
            layout.CodeOf(layout.Regions[2]));
        Assert.Equal(["            "], layout.CodeOf(layout.Regions[3]));
    }

    [Fact]
    public void TheCopyAppDesignerKeepsIsWhatTheLayoutCutsOutOfTheText()
    {
        JgsMlappLayout layout = JgsMlappLayout.Of(_text);
        MlappCodeCopy copy = Copy(_app);

        Assert.Equal(["ClassName", "EditableSectionCode", "Callbacks", "StartupCallback", "InputParameters"], copy.Fields);
        Assert.Equal(layout.ClassName, copy.ClassName);
        Assert.Equal(layout.EditableSectionCode, copy.EditableSection);
        Assert.Equal(layout.Callbacks.Select(static r => r.Name), copy.Callbacks.Select(static c => c.Name));
        foreach ((MlappRegion region, (string _, IReadOnlyList<string>? code)) in layout.Callbacks.Zip(copy.Callbacks))
        {
            Assert.Equal(layout.CodeOf(region), code);
        }

        Assert.Equal("startupFcn", copy.Startup?.Name);
        Assert.Equal(layout.CodeOf(layout.Startup!), copy.Startup?.Code);
        Assert.Equal("amp", copy.InputParameters);
    }

    [Fact]
    public void AFunctionsEndIsFoundByItsBlocks_HoweverItsBodyIsIndented()
    {
        string text = Insert(_text, "function GoButtonPushed", string.Join('\n',
            "if app.Log{end} == \"x\", disp(1); end",
            "        switch numel(app.Log(1:end))",
            "        case 1",
            "        try, x.end = 1; catch",
            "        end",
            "        end",
            "            s = 'end'; % end",
            "            h = @() cellfun(@(c) c(end), {1});"));

        JgsMlappLayout layout = JgsMlappLayout.Of(text);

        Assert.True(layout.IsComplete);
        MlappRegion go = layout.Regions.Single(static r => r.Name == "GoButtonPushed");
        Assert.Equal(11, go.LineCount);
        Assert.Equal("            end", layout.CodeOf(go)[^1]);
        Assert.Equal(["            "], layout.CodeOf(layout.Regions.Single(static r => r.Name == "ClearButtonPushed")));
    }

    [Fact]
    public void AFunctionNestedInACallbackIsPartOfItsBody_NotACallback()
    {
        string text = Insert(_text, "function ClearButtonPushed", string.Join('\n',
            "            function inner(x)",
            "                disp(x);",
            "            end"));

        JgsMlappLayout layout = JgsMlappLayout.Of(text);

        Assert.DoesNotContain(layout.Regions, static r => r.Name == "inner");
        Assert.Equal(4, layout.Regions.Single(static r => r.Name == "ClearButtonPushed").LineCount);
    }

    [Fact]
    public void AFunctionLeftOpen_LeavesNoCallbacksToTell()
    {
        string text = Insert(_text, "function GoButtonPushed", "            while true");

        JgsMlappLayout layout = JgsMlappLayout.Of(text);

        Assert.True(layout.IsAppLayout);
        Assert.False(layout.IsComplete);
        Assert.Equal([MlappRegionKind.EditableSection], layout.Regions.Select(static r => r.Kind));
    }

    [Fact]
    public void TextThatIsNotLaidOutAsAnApp_HasNoRegionsAndNothingGenerated()
    {
        JgsMlappLayout plain = JgsMlappLayout.Of("classdef P < handle\n    properties\n        X\n    end\nend");
        JgsMlappLayout function = JgsMlappLayout.Of("function y = f(x)\ny = x;\nend");

        Assert.False(plain.IsAppLayout);
        Assert.Equal("P", plain.ClassName);
        Assert.Empty(plain.Regions);
        Assert.Empty(plain.GeneratedSpans);
        Assert.False(function.IsAppLayout);
        Assert.Equal(string.Empty, function.ClassName);
    }

    [Fact]
    public void AResponsiveAppsSecondGeneratedBlockAndItsLayoutFunctionAreNotTheUsers()
    {
        string text = Insert(_text, "ClearButton  matlab.ui.control.Button", "PLACEHOLDER");
        text = text.Replace(
            "PLACEHOLDER\n    end\n",
            "PLACEHOLDER\n    end\n\n    % Properties that correspond to apps with auto-reflow\n    properties (Access = private)\n"
            + "        onePanelWidth = 576;\n    end\n",
            StringComparison.Ordinal).Replace("PLACEHOLDER\n", string.Empty, StringComparison.Ordinal);
        text = text.Replace(
            "        % Button pushed function: ClearButton\n",
            "        % Changes arrangement of the app based on UIFigure width\n        function updateAppLayout(app, event)\n"
            + "            currentFigureWidth = app.UIFigure.Position(3);\n        end\n\n        % Button pushed function: ClearButton\n",
            StringComparison.Ordinal);

        JgsMlappLayout layout = JgsMlappLayout.Of(text);

        Assert.Equal(12, layout.EditableSectionCode.Count);
        Assert.Equal("    properties (Access = public)", layout.EditableSectionCode[1]);
        MlappRegion generated = layout.Regions.Single(static r => r.Kind == MlappRegionKind.GeneratedCallback);
        Assert.Equal("updateAppLayout", generated.Name);
        Assert.False(generated.IsUserCode);
        Assert.Contains(layout.GeneratedSpans, s => s.FirstLine <= generated.FirstLine && generated.FirstLine < s.FirstLine + s.LineCount);
    }

    [Fact]
    public void TheGeneratedLinesAreEveryLineButTheUsersRegions()
    {
        JgsMlappLayout layout = JgsMlappLayout.Of(_text);

        var generated = new bool[layout.Lines.Count];
        foreach ((int first, int count) in layout.GeneratedSpans)
        {
            Array.Fill(generated, true, first, count);
        }

        Assert.True(generated[0]); // classdef
        Assert.True(generated[layout.Lines.ToList().FindIndex(static l => l.Contains("UIFigure     matlab.ui.Figure", StringComparison.Ordinal))]);
        Assert.True(generated[layout.Lines.ToList().FindIndex(static l => l.Contains("function GoButtonPushed", StringComparison.Ordinal))]);
        Assert.True(generated[layout.Lines.ToList().FindIndex(static l => l.Contains("function createComponents", StringComparison.Ordinal))]);
        Assert.True(generated[^1]);
        Assert.False(generated[layout.Lines.ToList().FindIndex(static l => l.Contains("Log = {}", StringComparison.Ordinal))]);
        Assert.False(generated[layout.Lines.ToList().FindIndex(static l => l.Contains("note(app, 'startup')", StringComparison.Ordinal))]);
        Assert.False(generated[layout.Lines.ToList().FindIndex(static l => l.Contains("for k = 1:2", StringComparison.Ordinal))]);
    }

    [Fact]
    public void AnEditInsideAUsersRegionLeavesTheGeneratedCodeTheSame_OneOutsideDoesNot()
    {
        JgsMlappLayout layout = JgsMlappLayout.Of(_text);

        Assert.True(layout.SameGeneratedCode(JgsMlappLayout.Of(_text)));
        Assert.True(layout.SameGeneratedCode(JgsMlappLayout.Of(Insert(_text, "function GoButtonPushed", "            beep;"))));
        Assert.True(layout.SameGeneratedCode(JgsMlappLayout.Of(Insert(_text, "Log = {}", "        More = 1"))));
        Assert.False(layout.SameGeneratedCode(JgsMlappLayout.Of(Insert(_text, "function createComponents", "            beep;"))));
        Assert.False(layout.SameGeneratedCode(JgsMlappLayout.Of(_text.Replace("GoButtonPushed(app, event)", "GoButtonPushed(app, ~)", StringComparison.Ordinal))));
        Assert.False(layout.SameGeneratedCode(JgsMlappLayout.Of(_text.Replace("% Component initialization", "% gone", StringComparison.Ordinal))));
    }

    // --- saving --------------------------------------------------------------------------------------

    [Fact]
    public void SavingTheTextTheFileHolds_LeavesEveryPartByteForByte()
    {
        Dictionary<string, byte[]> before = Parts(_app);
        List<string> order = Order(_app);

        MlappSaveResult result = JgsMlapp.WriteCode(_app, _text);

        Dictionary<string, byte[]> after = Parts(_app);
        Assert.Equal(order, Order(_app));
        foreach ((string name, byte[] bytes) in before)
        {
            Assert.True(bytes.AsSpan().SequenceEqual(after[name]), name);
        }

        Assert.Equal(new MlappSaveResult(HasDesignCopy: true, DesignCopyInStep: true, GeneratedCodeChanged: false), result);
        Assert.False(File.Exists(_app + ".saving"));
    }

    [Fact]
    public void AnEditInEachOfTheUsersRegions_ReachesTheCodeAndAppDesignersCopyOfIt()
    {
        string text = Insert(_text, "function GoButtonPushed", "            note(app, 'edited callback');");
        text = Insert(text, "Log = {}", "        Extra = 7 % added outside App Designer");
        text = Insert(text, "function startupFcn", "            note(app, 'edited startup');");
        text = text.Replace("startupFcn(app, amp)", "startupFcn(app, amp, more)", StringComparison.Ordinal);
        Dictionary<string, byte[]> before = Parts(_app);

        MlappSaveResult result = JgsMlapp.WriteCode(_app, text);

        Assert.Equal(text, JgsMlapp.ReadCode(_app));
        Assert.True(result.DesignCopyInStep);
        Assert.True(result.GeneratedCodeChanged); // the startup function's signature is App Designer's
        MlappCodeCopy copy = Copy(_app);
        Assert.Equal(["ClassName", "EditableSectionCode", "Callbacks", "StartupCallback", "InputParameters"], copy.Fields);
        Assert.Equal("            note(app, 'edited callback');", copy.Callbacks[0].Code![0]);
        Assert.Equal(4, copy.Callbacks[0].Code!.Count);
        Assert.Equal(["            "], copy.Callbacks[1].Code);
        Assert.Contains("        Extra = 7 % added outside App Designer", copy.EditableSection!);
        Assert.Equal(13, copy.EditableSection!.Count);
        Assert.Equal("            note(app, 'edited startup');", copy.Startup!.Value.Code![0]);
        Assert.Equal("amp, more", copy.InputParameters);

        // Nothing else in the package moved: only the document and the model differ.
        Dictionary<string, byte[]> after = Parts(_app);
        Assert.Equal(
            [Model, Document],
            before.Keys.Where(name => !before[name].AsSpan().SequenceEqual(after[name])).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheModelsOtherVariablesAreTheBytesTheyWere_AndTheSubsystemOffsetFollowsItsElement()
    {
        byte[] before = Parts(_app)[Model];
        JgsMlapp.WriteCode(_app, Insert(_text, "function GoButtonPushed", "            beep;"));
        byte[] after = Parts(_app)[Model];

        List<(int Offset, int Length, int Type)> was = Elements(before);
        List<(int Offset, int Length, int Type)> now = Elements(after);
        Assert.Equal(was.Count, now.Count);
        Assert.True(before.AsSpan(0, 116).SequenceEqual(after.AsSpan(0, 116)));

        // One element was rewritten (uncompressed); every other is the same bytes, wherever it now is.
        int rewritten = 0;
        for (int i = 0; i < was.Count; i++)
        {
            if (!before.AsSpan(was[i].Offset, was[i].Length).SequenceEqual(after.AsSpan(now[i].Offset, now[i].Length)))
            {
                rewritten++;
                Assert.Equal(14, now[i].Type);
            }
        }

        Assert.Equal(1, rewritten);
        long oldSubsystem = BinaryPrimitives.ReadInt64LittleEndian(before.AsSpan(116));
        long newSubsystem = BinaryPrimitives.ReadInt64LittleEndian(after.AsSpan(116));
        int at = was.FindIndex(e => e.Offset == oldSubsystem);
        Assert.True(at >= 0);
        Assert.Equal(now[at].Offset, newSubsystem);
        Assert.NotEqual(oldSubsystem, newSubsystem);
    }

    private static List<(int Offset, int Length, int Type)> Elements(byte[] mat)
    {
        var elements = new List<(int, int, int)>();
        int at = 128;
        while (at + 8 <= mat.Length)
        {
            int type = BinaryPrimitives.ReadInt32LittleEndian(mat.AsSpan(at));
            int size = BinaryPrimitives.ReadInt32LittleEndian(mat.AsSpan(at + 4));
            int length = type == 15 ? 8 + size : 8 + ((size + 7) & ~7);
            elements.Add((at, length, type));
            at += length;
        }

        Assert.Equal(mat.Length, at);
        return elements;
    }

    [Fact]
    public void ACallbackAddedOrTakenAway_IsAddedToOrTakenFromTheCopy()
    {
        string added = _text.Replace(
            "        % Button pushed function: ClearButton\n",
            "        % Callback function\n        function Spare(app, event)\n        end\n\n        % Button pushed function: ClearButton\n",
            StringComparison.Ordinal);
        JgsMlapp.WriteCode(_app, added);

        MlappCodeCopy copy = Copy(_app);
        Assert.Equal(["GoButtonPushed", "Spare", "ClearButtonPushed"], copy.Callbacks.Select(static c => c.Name));
        Assert.Empty(copy.Callbacks[1].Code!);

        // Every callback and the startup function gone, and the editable section with them: App
        // Designer leaves out a field that has nothing in it.
        JgsMlappLayout layout = JgsMlappLayout.Of(_text);
        List<string> lines = [.. layout.Lines];
        int callbacks = lines.FindIndex(static l => l.Contains("% Callbacks that handle component events", StringComparison.Ordinal));
        int initialization = lines.FindIndex(static l => l.Contains("% Component initialization", StringComparison.Ordinal));
        MlappRegion section = layout.Regions[0];
        lines.RemoveRange(callbacks, initialization - callbacks);
        lines.RemoveRange(section.FirstLine + 1, section.LineCount - 1);
        JgsMlapp.WriteCode(_app, string.Join('\n', lines));

        Assert.Equal(["ClassName"], Copy(_app).Fields);

        // And back again: the fields return.
        JgsMlapp.WriteCode(_app, _text);
        MlappCodeCopy back = Copy(_app);
        Assert.Equal(["ClassName", "EditableSectionCode", "Callbacks", "StartupCallback", "InputParameters"], back.Fields);
        Assert.Equal(["GoButtonPushed", "ClearButtonPushed"], back.Callbacks.Select(static c => c.Name));
        Assert.Equal("amp", back.InputParameters);
    }

    [Fact]
    public void TextTheLayoutCannotBeReadFrom_IsSavedAndRuns_AndTheCopyIsLeftAndSaidToBeBehind()
    {
        byte[] model = Parts(_app)[Model];
        string open = Insert(_text, "function GoButtonPushed", "            while true");

        MlappSaveResult result = JgsMlapp.WriteCode(_app, open);

        Assert.Equal(open, JgsMlapp.ReadCode(_app));
        Assert.True(result.HasDesignCopy);
        Assert.False(result.DesignCopyInStep);
        Assert.True(model.AsSpan().SequenceEqual(Parts(_app)[Model]));

        // The next save of text that can be read brings the copy to it.
        string closed = Insert(_text, "function GoButtonPushed", "            beep;");
        result = JgsMlapp.WriteCode(_app, closed);
        Assert.True(result.DesignCopyInStep);
        Assert.Equal("            beep;", Copy(_app).Callbacks[0].Code![0]);
        Assert.Equal(4, Copy(_app).Callbacks[0].Code!.Count);
    }

    [Fact]
    public void ASaveThatFailsBeforeTheFileIsReplaced_LeavesTheAppAsItWas()
    {
        byte[] before = File.ReadAllBytes(_app);
        string text = Insert(_text, "function GoButtonPushed", "            beep;");

        Assert.Throws<IOException>(() => JgsMlapp.WriteCode(_app, text, () => throw new IOException("the disk is full")));

        Assert.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(_app)));
        Assert.Equal([_app], Directory.GetFiles(_directory));
        Assert.Equal(_text, JgsMlapp.ReadCode(_app));
    }

    [Fact]
    public void TheTextIsWrittenInTheDocumentsLineEndings_AndACDataTerminatorInItSurvives()
    {
        string text = Insert(_text, "function GoButtonPushed", "            s = 'a]]>b'; % x(y{1}]]>");

        JgsMlapp.WriteCode(_app, text.Replace("\n", "\r\n", StringComparison.Ordinal));

        Assert.Equal(text, JgsMlapp.ReadCode(_app));
        string xml = Encoding.UTF8.GetString(Parts(_app)[Document]);
        Assert.DoesNotContain("\r", xml, StringComparison.Ordinal);
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\" ?><w:document ", xml, StringComparison.Ordinal);
        Assert.EndsWith("]]></w:t></w:r></w:p></w:body></w:document>", xml, StringComparison.Ordinal);
        Assert.Equal("            s = 'a]]>b'; % x(y{1}]]>", Copy(_app).Callbacks[0].Code![0]);
    }

    [Fact]
    public void APackageWithNoDesignCopyIsSavedToo_AndOneWithNoDocumentIsRefused()
    {
        string plain = Path.Combine(_directory, "U7MlApp.mlapp");
        File.Copy(Helper("U7MlApp.mlapp"), plain);
        string text = JgsMlapp.ReadCode(plain) + "\n% saved";

        MlappSaveResult result = JgsMlapp.WriteCode(plain, text);

        Assert.False(result.HasDesignCopy);
        Assert.Equal(text, JgsMlapp.ReadCode(plain));

        string bare = Path.Combine(_directory, "U7Bare.mlapp");
        File.Copy(Helper("U7Bare.mlapp"), bare);
        byte[] bytes = File.ReadAllBytes(bare);
        Assert.Throws<InvalidDataException>(() => JgsMlapp.WriteCode(bare, "x"));
        Assert.True(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(bare)));
    }

    [Fact]
    public async Task TheEditedAppRuns()
    {
        string text = Insert(_text, "function GoButtonPushed", "            note(app, 'edited callback');");
        text = Insert(text, "Log = {}", "        Extra = 7");
        text = Insert(text, "function startupFcn", "            note(app, sprintf('extra %d', app.Extra));");
        JgsMlapp.WriteCode(_app, text);

        await Exec("a = U7bApp(5); disp(strjoin(a.Log, ',')); disp(a.AmpField.Value); delete(a);");

        Assert.Contains("extra 7,startup", _output.NormalLines);
        Assert.Contains("5", _output.NormalLines.Select(static l => l.Trim()));
    }

    [Fact]
    public async Task ASessionThatRanTheApp_RunsTheSavedEditTheNextTime()
    {
        await Exec("a = U7bApp(5); disp(strjoin(a.Log, ',')); delete(a); clear a");
        Assert.Contains("startup", _output.NormalLines.Select(static l => l.Trim()));

        JgsMlapp.WriteCode(_app, Insert(_text, "function startupFcn", "            note(app, 'second run');"));
        await Exec("a = U7bApp(5); disp(strjoin(a.Log, ',')); delete(a); clear a");

        Assert.Contains("second run,startup", _output.NormalLines.Select(static l => l.Trim()));
    }

    // --- exporting -----------------------------------------------------------------------------------

    [Fact]
    public void ExportedCodeIsTheSameTextUnderTheNewFilesName()
    {
        string exported = JgsMlapp.ExportedCode(_text, "Renamed");

        Assert.StartsWith("classdef Renamed < matlab.apps.AppBase\n", exported, StringComparison.Ordinal);
        Assert.Contains("        function app = Renamed(varargin)", exported, StringComparison.Ordinal);
        Assert.DoesNotContain("U7bApp", exported.Replace("'U7b App'", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Equal(_text.Length - (2 * "U7bApp".Length) + (2 * "Renamed".Length), exported.Length);
        Assert.Same(_text, JgsMlapp.ExportedCode(_text, "U7bApp"));
    }

    [Fact]
    public void SavingAnAppUnderANewName_CopiesThePackageAndRenamesItsClass_AsAppDesignersSaveAsDoes()
    {
        byte[] original = File.ReadAllBytes(_app);
        string target = Path.Combine(_directory, "Second.mlapp");
        string edited = Insert(_text, "function GoButtonPushed", "            beep;");

        (string text, MlappSaveResult result) = AppDesignerDocument.Save(_app, target, edited);

        Assert.StartsWith("classdef Second < matlab.apps.AppBase\n", text, StringComparison.Ordinal);
        Assert.Contains("function app = Second(varargin)", text, StringComparison.Ordinal);
        Assert.Equal(text, JgsMlapp.ReadCode(target));
        Assert.Equal("Second", Copy(target).ClassName);
        Assert.Equal("            beep;", Copy(target).Callbacks[0].Code![0]);

        // The new name is not an edit to generated code, and the app it was saved from is untouched.
        Assert.Equal(new MlappSaveResult(HasDesignCopy: true, DesignCopyInStep: true, GeneratedCodeChanged: false), result);
        Assert.True(original.AsSpan().SequenceEqual(File.ReadAllBytes(_app)));
        Assert.Equal([target, _app], Directory.GetFiles(_directory).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ANameAClassCannotHave_IsRefused_AndNothingIsLeftBehind()
    {
        string target = Path.Combine(_directory, "my app.mlapp");

        IOException refused = Assert.Throws<IOException>(() => AppDesignerDocument.Save(_app, target, _text));

        Assert.Contains("'my app' cannot be the name of an app", refused.Message, StringComparison.Ordinal);
        Assert.Equal([_app], Directory.GetFiles(_directory));
        Assert.Throws<IOException>(() => AppDesignerDocument.Export(Path.Combine(_directory, "2fast.m"), _text));
    }

    [Fact]
    public void SavingIntoTheFileItCameFrom_IsTheSaveBack()
    {
        string edited = Insert(_text, "function createComponents", "            beep;");

        (string text, MlappSaveResult result) = AppDesignerDocument.Save(_app, _app, edited);

        Assert.Same(edited, text);
        Assert.True(result.GeneratedCodeChanged);
        Assert.Equal(edited, JgsMlapp.ReadCode(_app));
    }

    [Fact]
    public async Task ExportWritesTheClassUnderTheFilesName_AndItRunsBesideTheApp()
    {
        byte[] original = File.ReadAllBytes(_app);
        string path = Path.Combine(_directory, "U7bApp_exported.m");

        string exported = AppDesignerDocument.Export(path, _text);

        Assert.Equal(exported, File.ReadAllText(path));
        Assert.StartsWith("classdef U7bApp_exported < matlab.apps.AppBase", exported, StringComparison.Ordinal);
        Assert.True(original.AsSpan().SequenceEqual(File.ReadAllBytes(_app)));
        await Exec("a = U7bApp_exported(4); disp(class(a)); disp(strjoin(a.Log, ',')); delete(a);");
        Assert.Contains("U7bApp_exported", _output.NormalLines.Select(static l => l.Trim()));
        Assert.Contains("startup", _output.NormalLines.Select(static l => l.Trim()));
    }

    [Fact]
    public void WhatASaveLeftToBeKnownIsSaid_AndASaveThatLeftNothingSaysNothing()
    {
        Assert.Equal(string.Empty, AppDesignerDocument.StatusNote(new MlappSaveResult(true, true, false)));
        Assert.Equal(string.Empty, AppDesignerDocument.StatusNote(new MlappSaveResult(false, false, true)));
        Assert.Contains("App Designer will overwrite that change", AppDesignerDocument.StatusNote(new MlappSaveResult(true, true, true)), StringComparison.Ordinal);
        Assert.Contains("App Designer will show the code it had", AppDesignerDocument.StatusNote(new MlappSaveResult(true, false, true)), StringComparison.Ordinal);
        Assert.Contains("Export to .m File", AppDesignerDocument.GeneratedCodeNotice, StringComparison.Ordinal);
    }

    [Fact]
    public void ADocumentOverAnMlappSaysSo()
    {
        var document = new ScriptDocumentModel(_app, ScriptDocumentModel.ReadFile(_app));

        Assert.True(document.IsAppDesignerFile);
        Assert.Equal(_text, document.Text);
    }

    // --- every app R2025b ships ----------------------------------------------------------------------

    /// <summary>
    /// Where R2025b is installed, each <c>.mlapp</c> it ships is read in place and held to the
    /// rules above: its copy of the code is what the layout cuts from its text, and saving its own
    /// text changes no part. With <c>JGRAPH_U7B_OUT</c> naming a folder, an edited copy of each is
    /// saved there for the probe <c>u7b_verify</c> to hand to R2025b. Nothing of MathWorks' is
    /// kept: the folder is the probe's ignored one.
    /// </summary>
    [Fact]
    public void EveryShippedAppsCopyIsWhatItsTextGives_AndSavingItsOwnTextChangesNothing()
    {
        const string Root = @"C:\Program Files\MATLAB\R2025b\toolbox";
        if (!Directory.Exists(Root))
        {
            return;
        }

        string? outFolder = Environment.GetEnvironmentVariable("JGRAPH_U7B_OUT");
        var unaccounted = new List<string>();
        var unreadable = new List<string>();
        var wide = new List<string>();
        int apps = 0;
        foreach (string source in Directory.EnumerateFiles(Root, "*.mlapp", SearchOption.AllDirectories))
        {
            apps++;
            string name = Path.GetFileName(source);
            string copyPath = Path.Combine(_directory, "shipped", apps.ToString(System.Globalization.CultureInfo.InvariantCulture), name);
            Directory.CreateDirectory(Path.GetDirectoryName(copyPath)!);
            File.Copy(source, copyPath);
            File.SetAttributes(copyPath, FileAttributes.Normal);

            string text = JgsMlapp.ReadCode(copyPath);
            JgsMlappLayout layout = JgsMlappLayout.Of(text);
            Assert.True(layout.IsAppLayout && layout.IsComplete, name);
            if (JgsMlappModel.Read(Parts(copyPath)[Model]) is not { } copy)
            {
                // A model kept as a version 7.3 MAT-file: the code is saved, the copy is left and said to be.
                unreadable.Add(name);
                byte[] kept = Parts(copyPath)[Model];
                MlappSaveResult left = JgsMlapp.WriteCode(copyPath, text + "\n% saved");
                Assert.Equal(new MlappSaveResult(HasDesignCopy: true, DesignCopyInStep: false, GeneratedCodeChanged: true), left);
                Assert.True(kept.AsSpan().SequenceEqual(Parts(copyPath)[Model]));
                Assert.Equal(text + "\n% saved", JgsMlapp.ReadCode(copyPath));
                continue;
            }

            Assert.Equal(layout.ClassName, copy.ClassName);
            if (!layout.EditableSectionCode.SequenceEqual(copy.EditableSection ?? []))
            {
                // One Simulink app's record takes in the generated block ahead of the section.
                Assert.Equal(layout.EditableSectionCodeWithGeneratedBlocks, copy.EditableSection);
                wide.Add(name);
            }

            Assert.Equal(layout.Callbacks.Select(static r => r.Name), copy.Callbacks.Select(static c => c.Name));
            foreach ((MlappRegion region, (string callback, IReadOnlyList<string>? code)) in layout.Callbacks.Zip(copy.Callbacks))
            {
                if (code is null || !layout.CodeOf(region).SequenceEqual(code))
                {
                    unaccounted.Add($"{name}:{callback}:{region.Kind}");
                }
            }

            Assert.Equal(layout.Startup?.Name, copy.Startup?.Name);
            if (layout.Startup is { } startup)
            {
                Assert.Equal(layout.CodeOf(startup), copy.Startup!.Value.Code);
            }

            Assert.Equal(layout.InputParameters, copy.InputParameters ?? string.Empty);

            Dictionary<string, byte[]> before = Parts(copyPath);
            MlappSaveResult same = JgsMlapp.WriteCode(copyPath, text);
            Dictionary<string, byte[]> after = Parts(copyPath);
            Assert.True(same is { HasDesignCopy: true, DesignCopyInStep: true, GeneratedCodeChanged: false }, name);
            Assert.True(before.All(part => part.Value.AsSpan().SequenceEqual(after[part.Key])), name);

            if (outFolder is not null)
            {
                SaveEdited(copyPath, text, layout, Path.Combine(outFolder, apps.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
        }

        Assert.True(apps >= 40, $"{apps} apps");
        Assert.True(unreadable.Count <= 1, string.Join(' ', unreadable));
        Assert.True(wide.Count <= 1, string.Join(' ', wide));

        // The one body App Designer records in another form is the one it generates itself.
        Assert.All(unaccounted, static entry => Assert.EndsWith(":updateAppLayout:GeneratedCallback", entry, StringComparison.Ordinal));
    }

    /// <summary>
    /// With <c>JGRAPH_U7B_OUT</c> naming a folder, the sample is saved there with an edit in each
    /// of its regions, for the probe <c>u7b_verify</c> to read with R2025b and run.
    /// </summary>
    [Fact]
    public void WithAFolderNamed_TheEditedSampleIsLeftForR2025bToRead()
    {
        if (Environment.GetEnvironmentVariable("JGRAPH_U7B_OUT") is not { } outFolder)
        {
            return;
        }

        string folder = Path.Combine(outFolder, "sample");
        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, "U7bApp.mlapp");
        File.Copy(_app, target, overwrite: true);
        string text = Insert(_text, "function GoButtonPushed", "            note(app, 'edited callback');");
        text = Insert(text, "Log = {}", "        Extra = 7 % added outside App Designer: ]]> é 中");
        text = Insert(text, "function startupFcn", "            note(app, sprintf('edited startup %d', app.Extra));");

        MlappSaveResult result = JgsMlapp.WriteCode(target, text);

        Assert.Equal(new MlappSaveResult(HasDesignCopy: true, DesignCopyInStep: true, GeneratedCodeChanged: false), result);

        // And a MAT-file of text past ASCII as `save` writes it, for the probe u7b_char to load.
        JGraph.Scripting.MatFile.MatFileWriter.Write(
            Path.Combine(outFolder, "jgraph_char.mat"), [
                ("s", JgsValue.Str("Aé中€B")),
                ("t", JgsValue.Str("plain")),
                ("st", JgsValue.Struct(new Dictionary<string, JgsValue> { ["first"] = JgsValue.Number(1), ["second"] = JgsValue.Str("two") })),
            ]);
    }

    /// <summary>Saves the app with a line added in each kind of the user's regions it has, and says which in a list beside it.</summary>
    private static void SaveEdited(string app, string text, JgsMlappLayout layout, string folder)
    {
        Directory.CreateDirectory(folder);
        List<string> lines = [.. layout.Lines];
        var edits = new List<string>();

        // From the bottom up, so that an insertion does not move the regions above it.
        foreach (MlappRegion region in layout.Regions.Reverse())
        {
            switch (region.Kind)
            {
                case MlappRegionKind.Callback when !edits.Any(static e => e.StartsWith("callback", StringComparison.Ordinal)):
                    lines.Insert(region.FirstLine, "            % jgraph-edited-callback");
                    edits.Add("callback " + region.Name);
                    break;
                case MlappRegionKind.Startup:
                    lines.Insert(region.FirstLine, "            % jgraph-edited-startup");
                    edits.Add("startup " + region.Name);
                    break;
                case MlappRegionKind.EditableSection when layout.EditableSectionCode.Count > 0:
                    lines.Insert(region.FirstLine + 1, "    % jgraph-edited-section");
                    edits.Add("section -");
                    break;
                default:
                    break;
            }
        }

        string target = Path.Combine(folder, Path.GetFileName(app));
        File.Copy(app, target, overwrite: true);
        MlappSaveResult result = JgsMlapp.WriteCode(target, string.Join('\n', lines));
        Assert.True(result is { DesignCopyInStep: true, GeneratedCodeChanged: false }, app);
        File.WriteAllLines(Path.Combine(folder, "edits.txt"), edits);
    }
}
