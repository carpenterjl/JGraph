using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Workspace;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// <c>appdesigner</c> (designer plan DS2, decision DD3): the file it is given found and checked in
/// R2025b's words (probe <c>ds2_appdesigner</c>), handed to the host's designer window, and refused
/// where there is no window.
/// </summary>
[Collection("JG facade")]
public sealed class AppDesignerCommandTests : IAsyncLifetime
{
    private readonly RecordingScriptOutput _output = new();
    private readonly Designer _designer = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jgraph-appdesigner-" + Guid.NewGuid().ToString("N"));
    private IScriptSession _session = null!;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_folder);
        AppDesignerDocument.SaveNew(Path.Combine(_folder, "BorrowCopy.mlapp"), AppDesignerDocument.NewAppText());
        File.WriteAllText(Path.Combine(_folder, "NotAnApp.txt"), string.Empty);
        _session = Session(_designer);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _session.DisposeAsync();
        JG.Reset();
        Directory.Delete(_folder, recursive: true);
    }

    private IScriptSession Session(IScriptAppDesigner? designer) =>
        ((IScriptRepl)new MatlabScriptEngine()).CreateSession(
            new ScriptContext(_output, (_, _) => { }, _folder, null) { AppDesigner = designer });

    private sealed class Designer : IScriptAppDesigner
    {
        public List<string?> Opened { get; } = [];

        public string? Refusal { get; set; }

        public string? Open(string? path)
        {
            Opened.Add(path);
            return Refusal;
        }
    }

    /// <summary>The identifier and message a statement fails with, or "ok".</summary>
    private async Task<string> Outcome(string statement, IScriptSession? session = null)
    {
        _output.Mark();
        ScriptRunResult result = await (session ?? _session).ExecuteAsync(
            $"try, {statement}; disp('ok'), catch e, disp([e.identifier ' | ' e.message]), end", string.Empty, CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.TextSinceMark.Trim();
    }

    [Theory]
    [InlineData("appdesigner(5)", "MATLAB:appdesigner:appdesigner:InvalidInput | The input argument must be a full or partial file path, or a file name.")]
    [InlineData("appdesigner([\"a\" \"b\"])", "MATLAB:appdesigner:appdesigner:InvalidInput | The input argument must be a full or partial file path, or a file name.")]
    [InlineData("appdesigner('for')", "MATLAB:appdesigner:appdesigner:FileNameFailsIsKeyword | Name cannot be a MATLAB keyword.  Rename file, and then open.")]
    [InlineData("appdesigner('1abc')", "MATLAB:appdesigner:appdesigner:FileNameFailsIsVarName | File name '1abc' must start with a letter, contain only letters, numbers and underscores, and be shorter than 2048 characters.")]
    [InlineData("appdesigner('NoSuchApp')", "MATLAB:appdesigner:appdesigner:InvalidFileName | File 'NoSuchApp.mlapp' not found.")]
    [InlineData("appdesigner('NoSuchApp.mlapp')", "MATLAB:appdesigner:appdesigner:InvalidFileName | File 'NoSuchApp.mlapp' not found.")]
    [InlineData("appdesigner('NotAnApp.txt')", "MATLAB:appdesigner:appdesigner:InvalidFileExtension | 'NotAnApp.txt' file type must be a MATLAB App (*.mlapp).")]
    [InlineData("appdesigner('BorrowCopy.MLAPP')", "MATLAB:appdesigner:appdesigner:InvalidFileExtensionCase | 'BorrowCopy.MLAPP' file type must be a MATLAB App (*.mlapp).  The extension is case sensitive.")]
    [InlineData("appdesigner('BorrowCopy.m')", "MATLAB:appdesigner:appdesigner:InvalidFileExtension | 'BorrowCopy.m' file type must be a MATLAB App (*.mlapp).")]
    [InlineData("appdesigner('NoSuchApp', 2)", "MATLAB:TooManyInputs | Too many input arguments.")]
    [InlineData("x = appdesigner('BorrowCopy')", "MATLAB:TooManyOutputs | Too many output arguments.")]
    public async Task ANameIsRefusedInR2025bsWords(string statement, string expected)
    {
        Assert.Equal(expected, await Outcome(statement));
        Assert.Empty(_designer.Opened);
    }

    /// <summary>
    /// The app is found in the current folder, .mlapp added when left out, its name spelled as the
    /// file is (probe <c>ds2_appdesigner</c>: 'borrowcopy.mlapp' opens BorrowCopy.mlapp), and handed
    /// to the window by its full path; with no name the window comes up as it is.
    /// </summary>
    [Fact]
    public async Task TheAppIsHandedToTheWindowByItsFullPath()
    {
        string full = Path.Combine(_folder, "BorrowCopy.mlapp");
        foreach (string named in (string[])["'BorrowCopy'", "'BorrowCopy.mlapp'", "'borrowcopy.mlapp'", "\"BorrowCopy.mlapp\"", $"'{full[..^6]}'", $"'{full.Replace('\\', '/')}'"])
        {
            Assert.Equal("ok", await Outcome($"appdesigner({named})"));
            Assert.Equal(full, _designer.Opened[^1]);
        }

        Assert.Equal("ok", await Outcome("appdesigner"));
        Assert.Null(_designer.Opened[^1]);
    }

    /// <summary>What the window could not open is the call's error.</summary>
    [Fact]
    public async Task WhatTheWindowCannotOpenIsTheCallsError()
    {
        _designer.Refusal = "The app's components cannot be read.";
        Assert.Equal("JGraph:appdesigner:CannotOpen | The app's components cannot be read.", await Outcome("appdesigner('BorrowCopy')"));
    }

    /// <summary>A session with no window - the CLI, a batch run - refuses it, where R2025b would open its window.</summary>
    [Fact]
    public async Task ASessionWithNoWindowRefusesIt()
    {
        await using IScriptSession headless = Session(null);
        Assert.StartsWith("JGraph:appdesigner:NoWindow | appdesigner opens App Designer's window in the JGraph app", await Outcome("appdesigner('BorrowCopy')", headless));
    }
}
