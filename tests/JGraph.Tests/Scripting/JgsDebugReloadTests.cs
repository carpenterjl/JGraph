using System.Collections.Concurrent;
using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Jgs.Debug;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// A class file written again between two runs (App Designer's Save before Run, W11): the second run
/// executes the new text, so a breakpoint moved to the new line pauses there and the old line is quiet.
/// </summary>
[Collection("JG facade")]
public sealed class JgsDebugReloadTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private readonly RecordingScriptOutput _output = new();
    private readonly BlockingCollection<JgsPausedEventArgs> _pauses = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jgraph-w11-reload", Path.GetRandomFileName());
    private JgsReplSession _session = null!;

    public Task InitializeAsync()
    {
        JG.Reset();
        Directory.CreateDirectory(_folder);
        _session = Assert.IsType<JgsReplSession>(((IScriptRepl)new MatlabScriptEngine()).CreateSession(new ScriptContext(_output, (_, _) => { }, _folder)));
        _session.Debugger.Paused += (_, e) => _pauses.Add(e);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_session.Debugger.IsPaused)
        {
            _session.Debugger.Quit();
        }

        await _session.DisposeAsync();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // swept later
        }
    }

    private async Task Ok(string code)
    {
        ScriptRunResult result = await _session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, code + ": " + result.Message + _output.ErrorText);
    }

    [Fact]
    public async Task ARewrittenClassFileRunsItsNewLines()
    {
        string file = Path.Combine(_folder, "W11Reload.m");
        File.WriteAllText(file, "classdef W11Reload\n    methods\n        function obj = W11Reload()\n            x = 1;\n            y = x + 1;\n        end\n    end\nend\n");
        await Ok("dbstop in W11Reload at 4");
        Task<ScriptRunResult> first = _session.ExecuteAsync("a = W11Reload();", "", CancellationToken.None);
        Assert.True(_pauses.TryTake(out JgsPausedEventArgs? pause, Timeout), "no first pause");
        Assert.Equal(4, pause!.Location.Line);
        _session.Debugger.Continue();
        await first.WaitAsync(Timeout);

        // The file written again with two comment lines above the method: the statement is on line 6 now.
        await Task.Delay(50);
        File.WriteAllText(file, "classdef W11Reload\n    % one\n    % two\n    methods\n        function obj = W11Reload()\n            x = 1;\n            y = x + 1;\n        end\n    end\nend\n");
        await Ok("dbclear all");
        await Ok("dbstop in W11Reload at 6");
        Task<ScriptRunResult> second = _session.ExecuteAsync("b = W11Reload();", "", CancellationToken.None);
        Assert.True(_pauses.TryTake(out pause, Timeout), "no second pause: the class file was not read again");
        Assert.Equal(6, pause!.Location.Line);
        _session.Debugger.Continue();
        ScriptRunResult result = await second.WaitAsync(Timeout);
        Assert.True(result.Success, result.Message);
    }
}
