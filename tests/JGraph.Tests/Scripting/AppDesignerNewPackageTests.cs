using System.IO.Compression;
using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Workspace;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open item 58 (ADR 0239): a new <c>.mlapp</c> from a class that never was one. The editor writes
/// the three parts MATLAB needs to run an app and nothing else - the package the U7 fixtures run
/// in both engines (<c>build_mlapp.py</c>) - and says that App Designer cannot open it.
/// </summary>
[Collection("JG facade")]
public class AppDesignerNewPackageTests : IAsyncLifetime
{
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "jgraph-tests", Path.GetRandomFileName());
    private JgsReplSession _session = null!;

    public Task InitializeAsync()
    {
        JG.Reset();
        Directory.CreateDirectory(_directory);
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

    private static List<(string Name, byte[] Bytes)> Parts(string path)
    {
        using ZipArchive package = ZipFile.OpenRead(path);
        var parts = new List<(string, byte[])>();
        foreach (ZipArchiveEntry entry in package.Entries)
        {
            using Stream stream = entry.Open();
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            parts.Add((entry.FullName, bytes.ToArray()));
        }

        return parts;
    }

    private async Task Exec(string code)
    {
        ScriptRunResult result = await _session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    [Fact]
    public void ANewPackageHoldsThePartsTheFixtureBuilderWrites_ByteForByte()
    {
        string text = JgsMlapp.ReadCode(Helper("U7MlApp.mlapp"));
        string target = Path.Combine(_directory, "U7MlApp.mlapp");

        JgsMlapp.WritePackage(target, text);

        List<(string Name, byte[] Bytes)> built = Parts(Helper("U7MlApp.mlapp"));
        List<(string Name, byte[] Bytes)> written = Parts(target);
        Assert.Equal(built.Select(static p => p.Name), written.Select(static p => p.Name));
        foreach (((string name, byte[] expected), (string _, byte[] actual)) in built.Zip(written))
        {
            Assert.True(expected.AsSpan().SequenceEqual(actual), name);
        }

        Assert.Equal(["[Content_Types].xml", "_rels/.rels", "matlab/document.xml"], written.Select(static p => p.Name));
        Assert.Equal(text, JgsMlapp.ReadCode(target));
    }

    [Fact]
    public async Task AClassSavedAsANewApp_TakesTheFilesName_OpensAsAnAppHere_AndRuns()
    {
        string text = JgsMlapp.ReadCode(Helper("U7bApp.mlapp"));
        string target = Path.Combine(_directory, "FreshApp.mlapp");

        string saved = AppDesignerDocument.SaveNew(target, text);

        Assert.StartsWith("classdef FreshApp < matlab.apps.AppBase\n", saved, StringComparison.Ordinal);
        Assert.Contains("        function app = FreshApp(varargin)", saved, StringComparison.Ordinal);
        Assert.Equal(saved, JgsMlapp.ReadCode(target));
        Assert.True(new ScriptDocumentModel(target, ScriptDocumentModel.ReadFile(target)).IsAppDesignerFile);
        Assert.Equal([target], Directory.GetFiles(_directory));

        await Exec("a = FreshApp(4); disp(class(a)); disp(strjoin(a.Log, ',')); disp(a.AmpField.Value); delete(a);");
        Assert.Contains("FreshApp", _output.NormalLines.Select(static l => l.Trim()));
        Assert.Contains("startup", _output.NormalLines.Select(static l => l.Trim()));
        Assert.Contains("4", _output.NormalLines.Select(static l => l.Trim()));

        // From then on it is an App Designer file with no design copy: a save goes back into it.
        MlappSaveResult result = JgsMlapp.WriteCode(target, saved + "\n% saved again");
        Assert.Equal(new MlappSaveResult(HasDesignCopy: false, DesignCopyInStep: false, GeneratedCodeChanged: true), result);
        Assert.Equal(saved + "\n% saved again", JgsMlapp.ReadCode(target));
    }

    [Fact]
    public void APlainClassIsSavedAsAnAppToo_AndSavingOverAnExistingFileReplacesIt()
    {
        string target = Path.Combine(_directory, "Plain.mlapp");
        File.WriteAllText(target, "not a package");

        string saved = AppDesignerDocument.SaveNew(target, "classdef Other < handle\r\n    properties\r\n        X = 1\r\n    end\r\nend\r\n");

        Assert.Equal("classdef Plain < handle\n    properties\n        X = 1\n    end\nend\n", JgsMlapp.ReadCode(target));
        Assert.Equal(saved.Replace("\r\n", "\n", StringComparison.Ordinal), JgsMlapp.ReadCode(target));
        Assert.False(File.Exists(target + ".saving"));
    }

    [Fact]
    public void TextThatIsNoClass_AndANameAClassCannotHave_AreRefused_AndNothingIsLeft()
    {
        string function = Path.Combine(_directory, "Fn.mlapp");
        IOException refused = Assert.Throws<IOException>(() => AppDesignerDocument.SaveNew(function, "function y = f(x)\ny = x;\nend"));
        Assert.Contains("is not a class", refused.Message, StringComparison.Ordinal);

        string script = Path.Combine(_directory, "Script.mlapp");
        Assert.Throws<IOException>(() => AppDesignerDocument.SaveNew(script, "x = 1;\ndisp(x)"));

        string odd = Path.Combine(_directory, "my app.mlapp");
        refused = Assert.Throws<IOException>(() => AppDesignerDocument.SaveNew(odd, "classdef A\nend"));
        Assert.Contains("'my app' cannot be the name of an app", refused.Message, StringComparison.Ordinal);

        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public void ACDataTerminatorInTheCodeSurvives_AndTheNoteSaysWhatTheFileHolds()
    {
        string target = Path.Combine(_directory, "Edge.mlapp");

        AppDesignerDocument.SaveNew(target, "classdef Edge\n    methods\n        function f(~)\n            s = 'a]]>b';\n        end\n    end\nend");

        Assert.Contains("s = 'a]]>b';", JgsMlapp.ReadCode(target), StringComparison.Ordinal);
        Assert.Contains("App Designer cannot open it", AppDesignerDocument.CodeOnlyNote, StringComparison.Ordinal);
        Assert.Contains("runs here and in MATLAB", AppDesignerDocument.CodeOnlyNote, StringComparison.Ordinal);
    }
}
