using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Workspace;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Designer plan DS8: App Designer's Run, as R2025b's <c>AppModel.runApp</c> and
/// <c>AppManagementService.runApp</c> do it (read for the rules, ADR 0253). The MATLAB session runs
/// the saved app in its base workspace as a call of its name would; its folder goes on the path; a
/// second run closes the instance the first made; App Designer's refusals come back as failed results
/// in its words.
/// </summary>
[Collection("JG facade")]
public sealed class AppDesignerRunTests : IAsyncLifetime
{
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "jgraph-ds8-run", Path.GetRandomFileName());
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
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // swept later
        }
    }

    /// <summary>A blank app named <paramref name="name"/> saved as an <c>.mlapp</c> in <paramref name="folder"/>.</summary>
    private static string SaveApp(string folder, string name)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name + ".mlapp");
        AppDesignerDocument.SaveNew(path, AppDesignerDocument.NewAppText().Replace("app1", name, StringComparison.Ordinal));
        return path;
    }

    private async Task<string> Eval(string expression)
    {
        _output.Mark();
        ScriptRunResult result = await _session.ExecuteAsync($"disp({expression})", sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.TextSinceMark.Trim();
    }

    /// <summary>
    /// Run constructs the app in the base workspace with its folder on the path, binds nothing there,
    /// and a second run closes the first instance before making the next - one running app, its
    /// figure naming the file it came from; and the plain-text form runs the same way.
    /// </summary>
    [Fact]
    public async Task RunMakesOneRunningInstanceOfTheSavedApp()
    {
        string path = SaveApp(Path.Combine(_directory, "apps"), "RunOne");
        ScriptRunResult first = await _session.RunAppAsync(path, false, CancellationToken.None);
        Assert.True(first.Success, first.Message + _output.ErrorText);
        Assert.Contains(Path.Combine(_directory, "apps"), _session.PathFolders);
        Assert.Equal("1", await Eval("numel(findall(groot, 'Type', 'figure', '-property', 'RunningAppInstance'))"));
        Assert.Equal(path, await Eval("get(findall(groot, 'Type', 'figure', '-property', 'RunningAppInstance'), 'RunningInstanceFullFileName')"));
        Assert.Equal("0", await Eval("exist('RunOne', 'var') + exist('ans', 'var')"));

        ScriptRunResult second = await _session.RunAppAsync(path, false, CancellationToken.None);
        Assert.True(second.Success, second.Message + _output.ErrorText);
        Assert.Equal("1", await Eval("numel(findall(groot, 'Type', 'figure', '-property', 'RunningAppInstance'))"));

        // The same app saved as plain text runs through matlab.apps.App (DS7a).
        string plain = Path.Combine(_directory, "apps", "RunOnePlain.m");
        using (DesignDocument document = DesignDocument.Open(path))
        {
            Assert.StartsWith("Saved ", document.SaveAs(plain));
        }

        ScriptRunResult third = await _session.RunAppAsync(plain, false, CancellationToken.None);
        Assert.True(third.Success, third.Message + _output.ErrorText);
        Assert.Equal("2", await Eval("numel(findall(groot, 'Type', 'figure', '-property', 'RunningAppInstance'))"));
        Assert.Equal("1", await Eval("nnz(cellfun(@(a) isa(a, 'RunOnePlain'), get(findall(groot, 'Type', 'figure', '-property', 'RunningAppInstance'), 'RunningAppInstance')))"));
    }

    /// <summary>
    /// App Designer's refusals, in its words: a file that is not there; an app of the same name
    /// running from another file; a file in the current folder that shadows the app's name.
    /// </summary>
    [Fact]
    public async Task RunRefusesAsAppDesignerDoes()
    {
        ScriptRunResult missing = await _session.RunAppAsync(Path.Combine(_directory, "Gone.mlapp"), false, CancellationToken.None);
        Assert.False(missing.Success);
        Assert.Equal($"Error running 'Gone.mlapp'.\n{Path.Combine(_directory, "Gone.mlapp")} not found.", missing.Message);

        string here = SaveApp(Path.Combine(_directory, "a"), "Twin");
        string there = SaveApp(Path.Combine(_directory, "b"), "Twin");
        Assert.True((await _session.RunAppAsync(here, false, CancellationToken.None)).Success, _output.ErrorText);
        ScriptRunResult twin = await _session.RunAppAsync(there, false, CancellationToken.None);
        Assert.False(twin.Success);
        Assert.Equal("An app named 'Twin' is already running. Please close it before running a different app with that name.", twin.Message);

        // A function file of the app's name in the session's current folder outranks the path.
        File.WriteAllText(Path.Combine(_directory, "Shadowed.m"), "function Shadowed\nend\n");
        string shadowed = SaveApp(Path.Combine(_directory, "c"), "Shadowed");
        ScriptRunResult byCwd = await _session.RunAppAsync(shadowed, false, CancellationToken.None);
        Assert.False(byCwd.Success);
        Assert.Equal(
            $"Unable to run the app because \n{Path.Combine(_directory, "Shadowed.m")} shadows it in the current working folder.\n\nTo run the app, change the MATLAB current folder.",
            byCwd.Message);
        Assert.Equal("1", await Eval("numel(findall(groot, 'Type', 'figure', '-property', 'RunningAppInstance'))"));
    }

    /// <summary>
    /// A startup function that takes arguments Run does not give: the half-made app is deleted and
    /// the error is App Designer's (<c>fireAppError</c>, <c>isBadNumberOfArguments</c>), the cause
    /// under it. And a path said with forward slashes, as a command line says one, still finds the
    /// app's running instance to close.
    /// </summary>
    [Fact]
    public async Task RunWithoutTheStartupsArgumentsDeletesTheAppAndSaysSo()
    {
        string path = SaveApp(Path.Combine(_directory, "args"), "NeedsOne");
        using (DesignDocument document = DesignDocument.Open(path))
        {
            Assert.Null(document.AddStartup());
            Assert.Null(document.SetInputParameters("n"));
            string empty = "        function startupFcn(app, n)\n            \n        end";
            Assert.Contains(empty, document.CurrentText, StringComparison.Ordinal);
            Assert.True(document.ApplyEditedText(document.CurrentText.Replace(empty,
                "        function startupFcn(app, n)\n            app.UIFigure.Name = sprintf('%d', n);\n        end", StringComparison.Ordinal)));
            Assert.StartsWith("Saved ", document.Save());
        }

        ScriptRunResult result = await _session.RunAppAsync(path, false, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal("Cannot run with provided input arguments.\nCaused by: Not enough input arguments.", result.Message);
        Assert.Equal("0", await Eval("numel(findall(groot, 'Type', 'figure'))"));

        string slashed = Path.Combine(_directory, "args", "Plain").Replace('\\', '/');
        string plain = SaveApp(Path.Combine(_directory, "args", "Plain"), "RunTwice");
        Assert.True((await _session.RunAppAsync(plain, false, CancellationToken.None)).Success, _output.ErrorText);
        Assert.True((await _session.RunAppAsync(slashed + "/RunTwice.mlapp", false, CancellationToken.None)).Success, _output.ErrorText);
        Assert.Equal("1", await Eval("numel(findall(groot, 'Type', 'figure', '-property', 'RunningAppInstance'))"));
    }

    /// <summary>
    /// Run closes the instance it launched and nothing else (App Designer's <c>RunningApp</c>): an
    /// instance started at the prompt stays up and valid through two runs, and the same-name check
    /// passes the designer's own instance by. A single-instance app the designer has none of has the
    /// prompt's instance closed instead (<c>closeExternallyExecutedSingletonApp</c>).
    /// </summary>
    [Fact]
    public async Task RunClosesOnlyTheInstanceItLaunched()
    {
        string own = Path.Combine(_directory, "own");
        string path = SaveApp(own, "Shared");
        await Exec($"addpath('{own}'); h = Shared;");
        Assert.True((await _session.RunAppAsync(path, false, CancellationToken.None)).Success, _output.ErrorText);
        Assert.Equal("2", await Eval("numel(findall(groot, 'Type', 'figure', '-property', 'RunningAppInstance'))"));
        Assert.Equal("true", await Eval("isvalid(h)"));
        Assert.True((await _session.RunAppAsync(path, false, CancellationToken.None)).Success, _output.ErrorText);
        Assert.Equal("2", await Eval("numel(findall(groot, 'Type', 'figure', '-property', 'RunningAppInstance'))"));
        Assert.Equal("true", await Eval("isvalid(h)"));

        string solo = SaveApp(Path.Combine(_directory, "solo"), "Solo");
        using (DesignDocument document = DesignDocument.Open(solo))
        {
            document.SetSingleInstance(true);
            Assert.StartsWith("Saved ", document.Save());
        }

        await Exec($"addpath('{Path.Combine(_directory, "solo")}'); s = Solo;");
        Assert.True((await _session.RunAppAsync(solo, true, CancellationToken.None)).Success, _output.ErrorText);
        Assert.Equal("false", await Eval("isvalid(s)"));
        Assert.Equal("1", await Eval("nnz(cellfun(@(a) isa(a, 'Solo'), get(findall(groot, 'Type', 'figure', '-property', 'RunningAppInstance'), 'RunningAppInstance')))"));
    }

    /// <summary>
    /// A missing-argument error from deeper than the startup function itself (an anonymous function
    /// it calls) is reported as it is, and the app it made stays up, as App Designer's stack test
    /// leaves it; a file of the app's name in the folder the session <c>cd</c>'d into refuses as the
    /// current folder's; a file deleted on disk is saved again by the document and runs.
    /// </summary>
    [Fact]
    public async Task RunKeepsAnAppWhoseErrorIsNotTheStartupsArguments()
    {
        string path = SaveApp(Path.Combine(_directory, "deep"), "Deep");
        using (DesignDocument document = DesignDocument.Open(path))
        {
            Assert.Null(document.AddStartup());
            string empty = "        function startupFcn(app)\n            \n        end";
            Assert.Contains(empty, document.CurrentText, StringComparison.Ordinal);
            Assert.True(document.ApplyEditedText(document.CurrentText.Replace(empty,
                "        function startupFcn(app)\n            f = @(a, b) a + b;\n            app.UIFigure.Name = num2str(f(1));\n        end", StringComparison.Ordinal)));
            Assert.StartsWith("Saved ", document.Save());
        }

        ScriptRunResult deep = await _session.RunAppAsync(path, false, CancellationToken.None);
        Assert.False(deep.Success);
        Assert.Equal("Not enough input arguments.", deep.Message);
        Assert.Equal("true", await Eval("isa(get(findall(groot, 'Type', 'figure', '-property', 'RunningAppInstance'), 'RunningAppInstance'), 'Deep')"));

        string work = Path.Combine(_directory, "work");
        Directory.CreateDirectory(work);
        File.WriteAllText(Path.Combine(work, "Cwd.m"), "function Cwd\nend\n");
        string app = SaveApp(Path.Combine(_directory, "elsewhere"), "Cwd");
        await Exec($"cd('{work}')");
        ScriptRunResult byCwd = await _session.RunAppAsync(app, false, CancellationToken.None);
        Assert.False(byCwd.Success);
        Assert.StartsWith("Unable to run the app because \n" + Path.Combine(work, "Cwd.m") + " shadows it in the current working folder.", byCwd.Message);
        await Exec($"cd('{_directory}')");

        string gone = SaveApp(Path.Combine(_directory, "gone"), "Gone");
        using (DesignDocument document = DesignDocument.Open(gone))
        {
            File.Delete(gone);
            Assert.StartsWith("Saved ", document.SaveAs(gone));
        }

        Assert.True(File.Exists(gone));
        Assert.True((await _session.RunAppAsync(gone, false, CancellationToken.None)).Success, _output.ErrorText);
    }

    private async Task Exec(string code)
    {
        ScriptRunResult result = await _session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }
}
