using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.MatFile;
using JGraph.Scripting.MatFile.Hdf5;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open item 57 (ADR 0239): an App Designer file whose model is a version 7.3 MAT-file. The sample
/// is <c>U7bApp73.mlapp</c>, this project's U7b app written by R2025b's serializer under MATLAB's
/// version 7.3 MAT-file preference (probe <c>oi57_build</c>), so <c>appdesigner/appModel.mat</c> is
/// HDF5 as one shipped app's is. A save rewrites App Designer's copy of the code by appending the
/// new <c>code</c> past the file's end and re-pointing the root entry at it; the component tree
/// and the legacy object beside it are not touched. What R2025b makes of the result is the probe
/// <c>oi57_verify</c>'s to say.
/// </summary>
[Collection("JG facade")]
public class AppDesignerV73ModelTests : IAsyncLifetime
{
    private const string Model = "appdesigner/appModel.mat";
    private const string Document = "matlab/document.xml";
    private const int Superblock = 512;

    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "jgraph-tests", Path.GetRandomFileName());
    private JgsReplSession _session = null!;
    private string _app = null!;
    private string _text = null!;

    public Task InitializeAsync()
    {
        JG.Reset();
        Directory.CreateDirectory(_directory);
        _app = Path.Combine(_directory, "U7bApp73.mlapp");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "MatlabParity", "fixtures", "helpers", "U7bApp73.mlapp"), _app);
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

    /// <summary>Each root variable's name, shape and class, as the generic reader describes the model.</summary>
    private static List<string> Described(byte[] model) =>
        [.. MatV73Reader.Describe(model).Select(static v => $"{v.Name} [{string.Join(' ', v.Dims)}] {v.Class}")];

    /// <summary>The 40-byte root entry of the <c>code</c> variable, and the stored end of file: the two places a splice may write in the old bytes.</summary>
    private static (int Entry, int EndOfFile) PatchedPlaces(byte[] model)
    {
        Hdf5File file = Hdf5File.Open(model);
        (long tree, long heap) = file.Root.SymbolTable!.Value;
        int entry = file.FindEntry(tree, heap, "code");
        Assert.True(entry > 0);
        return (entry, Superblock + 40);
    }

    [Fact]
    public void TheModelIsVersion73_AndItsCopyReadsAsALevel5OneDoes()
    {
        byte[] model = Parts(_app)[Model];
        Assert.StartsWith("MATLAB 7.3 MAT-file", Encoding.ASCII.GetString(model, 0, 19), StringComparison.Ordinal);
        Assert.True(MatV73Splice.CanSplice(model));

        JgsMlappLayout layout = JgsMlappLayout.Of(_text);
        MlappCodeCopy copy = Copy(_app);

        Assert.Equal(["ClassName", "EditableSectionCode", "Callbacks", "StartupCallback", "InputParameters"], copy.Fields);
        Assert.Equal("U7bApp73", copy.ClassName);
        Assert.Equal(layout.EditableSectionCode, copy.EditableSection);
        Assert.Equal(["GoButtonPushed", "ClearButtonPushed"], copy.Callbacks.Select(static c => c.Name));
        foreach ((MlappRegion region, (string _, IReadOnlyList<string>? code)) in layout.Callbacks.Zip(copy.Callbacks))
        {
            Assert.Equal(layout.CodeOf(region), code);
        }

        Assert.Equal("startupFcn", copy.Startup?.Name);
        Assert.Equal(layout.CodeOf(layout.Startup!), copy.Startup?.Code);
        Assert.Equal("amp", copy.InputParameters);
    }

    [Fact]
    public void SavingTheTextTheFileHolds_LeavesEveryPartByteForByte()
    {
        Dictionary<string, byte[]> before = Parts(_app);

        MlappSaveResult result = JgsMlapp.WriteCode(_app, _text);

        Dictionary<string, byte[]> after = Parts(_app);
        foreach ((string name, byte[] bytes) in before)
        {
            Assert.True(bytes.AsSpan().SequenceEqual(after[name]), name);
        }

        Assert.Equal(new MlappSaveResult(HasDesignCopy: true, DesignCopyInStep: true, GeneratedCodeChanged: false), result);
    }

    [Fact]
    public void AnEditInEachOfTheUsersRegions_ReachesTheCopy_AndNoOtherByteOfTheModelMoves()
    {
        string text = Insert(_text, "function GoButtonPushed", "            note(app, 'edited callback');");
        text = Insert(text, "Log = {}", "        Extra = 7 % added outside App Designer: é 中");
        text = Insert(text, "function startupFcn", "            note(app, 'edited startup');");
        text = text.Replace("startupFcn(app, amp)", "startupFcn(app, amp, more)", StringComparison.Ordinal);
        Dictionary<string, byte[]> before = Parts(_app);

        MlappSaveResult result = JgsMlapp.WriteCode(_app, text);

        Assert.Equal(text, JgsMlapp.ReadCode(_app));
        Assert.True(result.DesignCopyInStep);
        Assert.True(result.GeneratedCodeChanged);
        MlappCodeCopy copy = Copy(_app);
        Assert.Equal(["ClassName", "EditableSectionCode", "Callbacks", "StartupCallback", "InputParameters"], copy.Fields);
        Assert.Equal("            note(app, 'edited callback');", copy.Callbacks[0].Code![0]);
        Assert.Equal(4, copy.Callbacks[0].Code!.Count);
        Assert.Equal(["            "], copy.Callbacks[1].Code);
        Assert.Contains("        Extra = 7 % added outside App Designer: é 中", copy.EditableSection!);
        Assert.Equal(13, copy.EditableSection!.Count);
        Assert.Equal("            note(app, 'edited startup');", copy.Startup!.Value.Code![0]);
        Assert.Equal("amp, more", copy.InputParameters);

        // Only the document and the model differ, and in the model the old bytes are all still
        // there: the new objects follow them, and the splice wrote in two places only - the root
        // entry of `code` and the stored end of file, which is the new length.
        Dictionary<string, byte[]> after = Parts(_app);
        Assert.Equal(
            [Model, Document],
            before.Keys.Where(name => !before[name].AsSpan().SequenceEqual(after[name])).Order(StringComparer.Ordinal));
        byte[] was = before[Model];
        byte[] now = after[Model];
        Assert.True(now.Length > was.Length);
        (int entry, int endOfFile) = PatchedPlaces(was);
        var moved = new List<int>();
        for (int i = 0; i < was.Length; i++)
        {
            if (was[i] != now[i] && !(i >= entry && i < entry + 40) && !(i >= endOfFile && i < endOfFile + 8))
            {
                moved.Add(i);
            }
        }

        Assert.Empty(moved);
        Assert.Equal(now.Length, BinaryPrimitives.ReadInt64LittleEndian(now.AsSpan(endOfFile)));

        // The other variables read as they did, and the file reads as a whole by the generic reader.
        Assert.Equal(Described(was), Described(now));
        JgsValue code = Assert.Single(MatV73Reader.Read(now, new HashSet<string> { "code" })).Value;
        Assert.Equal(JgsType.Struct, code.Type);
        Assert.Equal("amp, more", code.AsStructArray.Elements[0]["InputParameters"].AsString);

        // The section's twelve old lines are the objects they were; only the added line is new. The
        // edited callback's three old lines likewise, and the untouched callback's body is its old object.
        Hdf5Object section = Hdf5File.Open(now).Root.Child("code")!.Child("EditableSectionCode")!;
        List<long> lines = ReferencesIn(section);
        Assert.Equal(13, lines.Count);
        Assert.Equal(12, lines.Count(a => a + Superblock < was.Length));
        Hdf5Object bodies = Hdf5File.Open(now).Root.Child("code")!.Child("Callbacks")!.Child("Code")!;
        List<long> callbacks = ReferencesIn(bodies);
        Assert.Equal(2, callbacks.Count);
        Assert.True(callbacks[0] + Superblock >= was.Length);
        Assert.True(callbacks[1] + Superblock < was.Length);
        Assert.Equal(3, ReferencesIn(bodies.At(callbacks[0])).Count(a => a + Superblock < was.Length));
        Assert.True(now.Length - was.Length < 12_000, $"grew by {now.Length - was.Length}");
    }

    private static List<long> ReferencesIn(Hdf5Object dataset)
    {
        byte[] data = dataset.ReadData();
        var addresses = new List<long>();
        for (int i = 0; i < dataset.ElementCount; i++)
        {
            addresses.Add(Hdf5File.ReadWord(data, i * dataset.Datatype.Size, dataset.Datatype.Size));
        }

        return addresses;
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

        // One callback left: the one-element form, a group of the two values.
        string one = added.Replace(
            "        % Callback function\n        function Spare(app, event)\n        end\n\n        % Button pushed function: ClearButton\n        function ClearButtonPushed(app, event)\n            \n        end\n",
            string.Empty,
            StringComparison.Ordinal);
        JgsMlapp.WriteCode(_app, one);
        Assert.Equal(["GoButtonPushed"], Copy(_app).Callbacks.Select(static c => c.Name));
        Assert.Equal(3, Copy(_app).Callbacks[0].Code!.Count);

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

        // And back again: the fields return, each spliced in turn, and the model still reads whole.
        JgsMlapp.WriteCode(_app, _text);
        MlappCodeCopy back = Copy(_app);
        Assert.Equal(["ClassName", "EditableSectionCode", "Callbacks", "StartupCallback", "InputParameters"], back.Fields);
        Assert.Equal(["GoButtonPushed", "ClearButtonPushed"], back.Callbacks.Select(static c => c.Name));
        Assert.Equal("amp", back.InputParameters);
        Assert.Equal(3, MatV73Reader.Describe(Parts(_app)[Model]).Count);
    }

    [Fact]
    public void TextTheLayoutCannotBeReadFrom_LeavesTheModel_AndTheNextSaveBringsTheCopyToTheText()
    {
        byte[] model = Parts(_app)[Model];
        string open = Insert(_text, "function GoButtonPushed", "    end");

        MlappSaveResult result = JgsMlapp.WriteCode(_app, open);

        Assert.True(result.HasDesignCopy);
        Assert.False(result.DesignCopyInStep);
        Assert.True(model.AsSpan().SequenceEqual(Parts(_app)[Model]));

        string closed = Insert(_text, "function GoButtonPushed", "            beep;");
        result = JgsMlapp.WriteCode(_app, closed);
        Assert.True(result.DesignCopyInStep);
        Assert.Equal("            beep;", Copy(_app).Callbacks[0].Code![0]);
    }

    [Fact]
    public async Task TheEditedAppRuns()
    {
        string text = Insert(_text, "function GoButtonPushed", "            note(app, 'edited callback');");
        text = Insert(text, "Log = {}", "        Extra = 7");
        text = Insert(text, "function startupFcn", "            note(app, sprintf('extra %d', app.Extra));");
        JgsMlapp.WriteCode(_app, text);

        await Exec("a = U7bApp73(5); disp(strjoin(a.Log, ',')); disp(a.AmpField.Value); delete(a);");

        Assert.Contains("extra 7,startup", _output.NormalLines);
        Assert.Contains("5", _output.NormalLines.Select(static l => l.Trim()));
    }

    /// <summary>
    /// With <c>JGRAPH_U7B_OUT</c> naming a folder, the sample is saved there with an edit in each
    /// of its regions, for the probe <c>oi57_verify</c> to load, read and run with R2025b.
    /// </summary>
    [Fact]
    public void WithAFolderNamed_TheEditedSampleIsLeftForR2025bToRead()
    {
        if (Environment.GetEnvironmentVariable("JGRAPH_U7B_OUT") is not { } outFolder)
        {
            return;
        }

        string folder = Path.Combine(outFolder, "sample73");
        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, "U7bApp73.mlapp");
        File.Copy(_app, target, overwrite: true);
        string text = Insert(_text, "function GoButtonPushed", "            note(app, 'edited callback');");
        text = Insert(text, "Log = {}", "        Extra = 7 % added outside App Designer: ]]> é 中");
        text = Insert(text, "function startupFcn", "            note(app, sprintf('edited startup %d', app.Extra));");

        MlappSaveResult result = JgsMlapp.WriteCode(target, text);

        Assert.Equal(new MlappSaveResult(HasDesignCopy: true, DesignCopyInStep: true, GeneratedCodeChanged: false), result);
    }
}
