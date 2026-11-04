using System.Collections.Concurrent;
using JGraph.Api;
using JGraph.Deploy.Apps;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Jgs.Debug;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The debugger's words and the session's own debugger (App Designer window plan W11, ADR 0303): <c>dbstop</c>,
/// <c>dbclear</c>, <c>dbstatus</c>, <c>dbstack</c>, <c>dbtype</c> and the prompt words print and refuse as R2025b
/// did headless (<c>w11_debug</c>, <c>w0_debug</c>), a breakpoint set by <c>dbstop</c> pauses a statement typed
/// later, <c>dbstop if error</c> pauses at the failing line, a quit unwinds quietly, and a breakpoint in an app's
/// startup function pauses App Designer's Run.
/// </summary>
[Collection("JG facade")]
public sealed class JgsDebugWordsTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Deploy", "fixtures");

    private readonly RecordingScriptOutput _output = new();
    private readonly BlockingCollection<JgsPausedEventArgs> _pauses = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jgraph-w11-words", Path.GetRandomFileName());
    private JgsReplSession _session = null!;

    public Task InitializeAsync()
    {
        JG.Reset();
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "w11fun.m"), "function y = w11fun(x)\n% a comment\n\ny = helper(x) + 1;\n\nz = y * 2;\ny = z;\nend\n\nfunction r = helper(x)\n% help\nr = x + 1;\nend\n");
        File.WriteAllText(Path.Combine(_folder, "w11script.m"), "% script\na = 1;\n\nb = a + 1;\ndisp(b)\n");
        File.WriteAllText(Path.Combine(_folder, "w11boom.m"), "function w11boom(x)\ny = x + 1;\nerror('w11:boom', 'boom %d', y);\nend\n");
        File.WriteAllText(Path.Combine(_folder, "W11Class.m"), "classdef W11Class\n    properties\n        X = 1\n    end\n    methods\n        function obj = W11Class(v)\n            obj.X = v;\n        end\n        function r = twice(obj)\n            r = obj.X * 2;\n        end\n    end\nend\n");
        _session = Assert.IsType<JgsReplSession>(((IScriptRepl)new MatlabScriptEngine()).CreateSession(new ScriptContext(_output, (_, _) => { }, _folder)));
        _session.Debugger.Paused += (_, e) => _pauses.Add(e);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_session.Debugger.IsPaused)
        {
            _session.Debugger.Quit(); // a pause a failed assertion left behind must not hold the thread
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

    private string File_(string name) => Path.Combine(_folder, name);

    private async Task<ScriptRunResult> Run(string code) => await _session.ExecuteAsync(code, sourceId: "", CancellationToken.None);

    private async Task Ok(string code)
    {
        ScriptRunResult result = await Run(code);
        Assert.True(result.Success, code + ": " + result.Message + _output.ErrorText);
    }

    private async Task<string> Printed(string code)
    {
        _output.Mark();
        await Ok(code);
        return _output.TextSinceMark.TrimEnd('\n');
    }

    private async Task<(string Identifier, string Message)> Refused(string code)
    {
        ScriptRunResult result = await Run(code);
        Assert.False(result.Success, code + " was accepted");
        return (result.ErrorIdentifier ?? string.Empty, result.Message ?? string.Empty);
    }

    private JgsPausedEventArgs NextPause()
    {
        Assert.True(_pauses.TryTake(out JgsPausedEventArgs? args, Timeout), "Timed out waiting for a pause.");
        return args!;
    }

    private static ScriptRunResult Await(Task<ScriptRunResult> task)
    {
        Assert.True(task.Wait(Timeout), "Timed out waiting for the statement to finish.");
        return task.Result;
    }

    /// <summary>Where breakpoints land and how dbstatus names them, as R2025b printed (w11_debug): a comment or blank moves to the next statement, a function's end counts, the local function is file>name.</summary>
    [Fact]
    public async Task DbstopLandsWhereR2025bDoesAndDbstatusNamesTheFunctions()
    {
        await Ok("dbstop in w11fun");
        Assert.Equal("Breakpoint for w11fun is on line 4.", await Printed("dbstatus"));
        await Ok("dbclear all");
        foreach (string line in new[] { "2", "3", "5", "8", "9", "11" })
        {
            await Ok($"dbstop in w11fun at {line}");
        }

        Assert.Equal("Breakpoints for w11fun are on lines 4, 6, 8.\nBreakpoint for w11fun>helper is on line 12.", await Printed("dbstatus"));
        await Ok("dbclear all");
        await Ok("dbstop in w11fun at 4");
        await Ok("dbstop in w11fun at 4");
        await Ok("dbstop in w11fun.m at 6");
        await Ok("dbstop in w11fun>helper");
        await Ok($"dbstop in '{File_("w11fun.m")}' at 7");
        Assert.Equal("Breakpoints for w11fun are on lines 4, 6, 7.\nBreakpoint for w11fun>helper is on line 12.", await Printed("dbstatus w11fun"));
        Assert.Equal($"Breakpoint for {File_("w11fun.m")}>helper is on line 12.\nBreakpoints for {File_("w11fun.m")}>w11fun are on lines 4, 6, 7.", await Printed("dbstatus -completenames"));
        Assert.Equal(4, _session.Breakpoints.In(File_("w11fun.m")).Count);

        await Ok("dbclear in w11fun at 4");
        await Ok("dbclear in w11fun>helper");
        Assert.Equal("Breakpoints for w11fun are on lines 6, 7.", await Printed("dbstatus"));
        await Ok("dbclear in w11fun");
        Assert.Equal(string.Empty, await Printed("dbstatus"));
        await Ok("dbclear in w11fun");
        await Ok("dbclear all in w11fun");
    }

    /// <summary>A condition is listed under its line, replaced by the next dbstop and dropped by a plain one; the struct has R2025b's fields.</summary>
    [Fact]
    public async Task ConditionsAreListedReplacedAndAnswered()
    {
        await Ok("dbstop in w11fun at 4 if x > 1");
        Assert.Equal("Breakpoint for w11fun is on line 4.\n  Breakpoint on line 4 has conditional expression \"x > 1\".", await Printed("dbstatus"));
        await Ok("dbstop in w11fun at 4 if x > 2");
        Assert.Equal("Breakpoint for w11fun is on line 4.\n  Breakpoint on line 4 has conditional expression \"x > 2\".", await Printed("dbstatus"));
        await Ok("dbstop in w11fun at 4");
        Assert.Equal("Breakpoint for w11fun is on line 4.", await Printed("dbstatus"));
        await Ok("dbstop in w11fun at 6 if y > 1 && x < 3");
        Assert.Equal("w11fun|[4 6]|[0 0]|;y > 1 && x < 3||", await Printed(
            "s = dbstatus; fprintf('%s|%s|%s|%s|%s|%s', s.name, mat2str(s.line), mat2str(s.anonymous), strjoin(s.expression, ';'), s.cond, strjoin(s.identifier, ';'))"));
        Assert.Equal("[1 1]|name,file,line,anonymous,expression,cond,identifier", await Printed("s = dbstatus; fprintf('%s|%s', mat2str(size(s)), strjoin(fieldnames(s)', ','))"));
        await Ok("dbclear all");
        Assert.Equal("[0 1]|name,file,line,anonymous,expression,cond,identifier", await Printed("s = dbstatus; fprintf('%s|%s', mat2str(size(s)), strjoin(fieldnames(s)', ','))"));
    }

    /// <summary>The refusals, in R2025b's identifiers and words (w11_debug).</summary>
    [Theory]
    [InlineData("dbstop", "MATLAB:minrhs", "Not enough input arguments.")]
    [InlineData("dbstop in", "MATLAB:badopt", "Unknown command option.")]
    [InlineData("dbstop at", "MATLAB:badopt", "Unknown command option.")]
    [InlineData("dbstop if", "MATLAB:badopt", "Unknown command option.")]
    [InlineData("dbstop in w11fun at", "MATLAB:badopt", "Unknown command option.")]
    [InlineData("dbstop in w11fun at 4 if", "MATLAB:badopt", "Unknown command option.")]
    [InlineData("dbstop in w11fun at 12 extra", "MATLAB:badopt", "Unknown command option.")]
    [InlineData("dbstop if error MATLAB:a MATLAB:b", "MATLAB:badopt", "Unknown command option.")]
    [InlineData("dbstop if warning nocolon", "MATLAB:badopt", "Unknown command option.")]
    [InlineData("dbstop in w11fun at -1", "MATLAB:dispatcher:cannotFindFunction", "Cannot find function \"-1\" within \"w11fun\".")]
    [InlineData("dbstop in w11fun at abc", "MATLAB:dispatcher:cannotFindFunction", "Cannot find function \"abc\" within \"w11fun\".")]
    [InlineData("dbstop in w11fun at 100", "MATLAB:lineBeyondFileEnd", "Setting or clearing breakpoints past the start of the last expression is not supported.")]
    [InlineData("dbstop in NoSuchW11 at 3", "MATLAB:dispatcher:cannotFindFunction", "Cannot find function \"NoSuchW11\".")]
    [InlineData("dbstop in w11fun>nosuch", "MATLAB:dispatcher:cannotFindFunction", "Cannot find function \"nosuch\" within \"w11fun\".")]
    [InlineData("dbstop in w11fun>helper at 12", "MATLAB:dbstop:lineNumberAndLocalNestedCombinationInvalid", "Invalid option combination. Specify the line number or specify the local or nested function.")]
    [InlineData("dbstop bogus w11fun", "MATLAB:dispatcher:cannotFindFunction", "Cannot find function \"bogus\".")]
    [InlineData("dbstop('in', 42)", "MATLAB:string:MustBeStringScalarOrCharacterVector", "Argument must be a text scalar.")]
    [InlineData("dbstop('w11fun', 4)", "MATLAB:string:MustBeStringScalarOrCharacterVector", "Argument must be a text scalar.")]
    [InlineData("dbclear in NoSuchW11", "MATLAB:dispatcher:cannotFindFunction", "Cannot find function \"NoSuchW11\".")]
    [InlineData("dbclear in w11fun at 100", "MATLAB:lineBeyondFileEnd", "Setting or clearing breakpoints past the start of the last expression is not supported.")]
    [InlineData("dbcont", "MATLAB:dbOnlyInDebugMode", "Debug commands only allowed when stopped in debug mode.")]
    [InlineData("dbstep in", "MATLAB:dbOnlyInDebugMode", "Debug commands only allowed when stopped in debug mode.")]
    [InlineData("dbquit", "MATLAB:dbOnlyInDebugMode", "Debug commands only allowed when stopped in debug mode.")]
    [InlineData("dbup", "MATLAB:dbOnlyInDebugMode", "Debug commands only allowed when stopped in debug mode.")]
    [InlineData("dbtype NoSuchW11", "MATLAB:type:fileNotFound", "File 'NoSuchW11' not found.")]
    [InlineData("dbtype w11fun 50:60", "MATLAB:dbtype:dbLineBeyondEnd", "Line number(s) beyond end-of-file.")]
    public async Task RefusesInR2025bsWords(string code, string identifier, string message)
    {
        (string id, string text) = await Refused(code);
        Assert.Equal(identifier, id);
        Assert.Equal(message, text);
    }

    /// <summary>What R2025b accepts silently: at without a file, at 0, an unknown condition, an empty condition, the functional forms.</summary>
    [Fact]
    public async Task AcceptsWhatR2025bAccepts()
    {
        await Ok("dbstop at 3");
        await Ok("dbstop if bogus");
        await Ok("dbstop in w11fun at 0");
        await Ok("dbstop in w11fun at 4 if ''");
        Assert.Equal("Breakpoint for w11fun is on line 4.", await Printed("dbstatus"));
        await Ok("dbclear all");
        await Ok("dbstop('w11fun', '6')");
        await Ok("dbstop('w11fun')");
        await Ok("dbstop(\"in\", \"w11fun\", \"at\", \"7\")");
        Assert.Equal("Breakpoints for w11fun are on lines 4, 6, 7.", await Printed("dbstatus"));
        await Ok("dbclear all");
    }

    /// <summary>A script, a class and the plain-text acceptance app: the names dbstatus prints and their order (ordinal by name).</summary>
    [Fact]
    public async Task NamesScriptsClassesAndAppsAsR2025bDoes()
    {
        ToolboxPackage.Extract(Path.Combine(Fixtures, "AcceptForm-r2025b.mltbx"), Path.Combine(_folder, "apps"));
        await Ok($"addpath('{Path.Combine(_folder, "apps")}')");
        await Ok("dbstop in w11script at 1");
        await Ok("dbstop in w11script at 2");
        await Ok("dbstop in W11Class at 7");
        await Ok("dbstop in W11Class>W11Class.twice");
        await Ok("dbstop in W11Class at 3");
        await Ok("dbstop in W11Class at 1");
        await Ok("dbstop in AcceptForm at 34");
        await Ok("dbstop in AcceptForm>AcceptForm.startupFcn");
        Assert.Equal(
            "Breakpoint for AcceptForm>AcceptForm.ComputeButtonPushed is on line 34.\n" +
            "Breakpoint for AcceptForm>AcceptForm.startupFcn is on line 29.\n" +
            "Breakpoint for W11Class>W11Class.W11Class is on line 7.\n" +
            "Breakpoint for W11Class>W11Class.twice is on line 10.\n" +
            "Breakpoint for w11script is on line 2.", await Printed("dbstatus"));
        await Ok("dbclear all");
    }

    /// <summary>dbstop if: the words dbstatus prints, all winning over an identifier, the refusal of clearing one identifier while all are on, infnan for naninf.</summary>
    [Fact]
    public async Task StopIfConditionsPrintAndClearAsR2025bDoes()
    {
        await Ok("dbstop if error");
        await Ok("dbstop if error MATLAB:badsubscript");
        Assert.Equal("Stop if error.", await Printed("dbstatus"));
        await Ok("dbstop if caught error MATLAB:badsubscript");
        await Ok("dbstop if warning MATLAB:singularMatrix");
        await Ok("dbstop if warning");
        await Ok("dbstop if infnan");
        Assert.Equal("Stop if error.\nStop if caught error with identifier MATLAB:badsubscript.\nStop if warning.\nStop if naninf.", await Printed("dbstatus"));
        Assert.Equal("error|all;caught error|MATLAB:badsubscript;warning|all;naninf|", await Printed(
            "s = dbstatus; t = {}; for k = 1:numel(s), t{end+1} = [s(k).cond '|' strjoin(s(k).identifier, ',')]; end; fprintf('%s', strjoin(t, ';'))"));
        (string id, string text) = await Refused("dbclear if error MATLAB:nomem");
        Assert.Equal("MATLAB:dbclear:cannotClearWhenAllEnabled", id);
        Assert.Equal("Cannot clear the 'dbstop if error' setting for 'MATLAB:nomem' when the setting is enabled for all identifiers. Use 'dbclear if error' to clear the setting for all identifiers.", text);
        await Ok("dbclear if error");
        await Ok("dbclear if infnan");
        Assert.Equal("Stop if caught error with identifier MATLAB:badsubscript.\nStop if warning.", await Printed("dbstatus"));
        await Ok("dbclear all");
        Assert.Equal(string.Empty, await Printed("dbstatus"));
    }

    /// <summary>dbstack at the prompt prints nothing and answers the empty struct; dbtype prints the numbered lines in R2025b's layout.</summary>
    [Fact]
    public async Task DbstackAndDbtypePrintAsR2025bDoes()
    {
        Assert.Equal(string.Empty, await Printed("dbstack"));
        Assert.Equal("[0 1]|file,name,line|1", await Printed("[s, i] = dbstack; fprintf('%s|%s|%d', mat2str(size(s)), strjoin(fieldnames(s)', ','), i)"));
        Assert.Equal("\n4     y = helper(x) + 1;\n5     \n6     z = y * 2;", await Printed("dbtype w11fun 4:6"));
        Assert.Equal("\n10    function r = helper(x)\n11    % help\n12    r = x + 1;", await Printed("dbtype('w11fun', '10:12')"));
    }

    /// <summary>A breakpoint set by dbstop pauses a statement typed later, in the function's frame, with its variables; Continue finishes it.</summary>
    [Fact]
    public async Task ABreakpointPausesALaterStatementInTheFunctionsFrame()
    {
        await Ok("dbstop in w11fun at 6");
        Assert.True(_session.Debugger.IsActive);
        Task<ScriptRunResult> run = Run("y = w11fun(2)");
        JgsPausedEventArgs pause = NextPause();
        Assert.Equal(PauseReason.Breakpoint, pause.Reason);
        Assert.Equal(6, pause.Location.Line);
        Assert.Equal(File_("w11fun.m"), pause.Location.SourceId, ignoreCase: true);
        Assert.Equal("w11fun", pause.CallStack[0].FunctionName);
        IReadOnlyList<ScriptVariable> variables = _session.Debugger.GetVariables();
        Assert.Equal(4.0, Assert.Single(variables, v => v.Name == "y").RawValue);
        Assert.Equal(2.0, Assert.Single(variables, v => v.Name == "x").RawValue);
        Assert.Equal("> In w11fun (line 6)", (await _session.Debugger.EvaluateAsync("dbstack", 0, CancellationToken.None)).Success ? _output.NormalLines.Last(static l => l.Length > 0).TrimEnd('\n') : "failed");
        _session.Debugger.Continue();
        ScriptRunResult result = Await(run);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("8", await Printed("fprintf('%g', y)"));
        await Ok("dbclear all");
        Assert.False(_session.Debugger.IsActive);
    }

    /// <summary>dbstop if error pauses at the failing line of a file with the error's text; a caught error and an error typed at the prompt do not pause; Quit unwinds the statement quietly.</summary>
    [Fact]
    public async Task StopIfErrorPausesAtTheFailingLineAndQuitUnwindsQuietly()
    {
        await Ok("dbstop if error");
        await Ok("try, w11boom(1); catch, end"); // no pause: the error is caught
        Assert.False((await Run("error('w11:prompt', 'at the prompt')")).Success); // no pause: no file to stop in
        Assert.Empty(_pauses);
        Task<ScriptRunResult> run = Run("w11boom(1)");
        JgsPausedEventArgs pause = NextPause();
        Assert.Equal(PauseReason.Error, pause.Reason);
        Assert.Equal(3, pause.Location.Line);
        Assert.Equal(File_("w11boom.m"), pause.Location.SourceId, ignoreCase: true);
        Assert.Equal("boom 2", pause.Detail);
        Assert.Equal(2.0, Assert.Single(_session.Debugger.GetVariables(), v => v.Name == "y").RawValue);
        _output.Mark();
        _session.Debugger.Quit();
        ScriptRunResult result = Await(run);
        Assert.True(result.Success, result.Message);
        Assert.Equal(string.Empty, _output.TextSinceMark);
        await Ok("dbclear all");
    }

    /// <summary>A breakpoint in an app's startup function pauses App Designer's Run, the frame named Class.method, the argument in its variables.</summary>
    [Fact]
    public async Task ABreakpointInTheStartupFunctionPausesTheAppsRun()
    {
        ToolboxPackage.Extract(Path.Combine(Fixtures, "AcceptForm-r2025b.mltbx"), Path.Combine(_folder, "apps"));
        string app = Path.Combine(_folder, "apps", "AcceptForm.mlapp");
        await Ok($"addpath('{Path.Combine(_folder, "apps")}')");
        await Ok("dbstop in AcceptForm>AcceptForm.startupFcn");
        Task<ScriptRunResult> run = _session.RunAppAsync(app, false, "3", false, CancellationToken.None);
        JgsPausedEventArgs pause = NextPause();
        Assert.Equal(29, pause.Location.Line);
        Assert.Equal(app, pause.Location.SourceId, ignoreCase: true);
        Assert.Equal("AcceptForm.startupFcn", pause.CallStack[0].FunctionName);
        Assert.Equal(3.0, Assert.Single(_session.Debugger.GetVariables(), v => v.Name == "n").RawValue);
        _session.Debugger.StepOver();
        JgsPausedEventArgs step = NextPause();
        Assert.Equal(PauseReason.Step, step.Reason);
        _session.Debugger.Continue();
        ScriptRunResult result = Await(run);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("3", await Printed("f = findall(groot, 'Type', 'figure', '-property', 'RunningAppInstance'); fprintf('%g', f.RunningAppInstance.EditField.Value)"));
        await Ok("dbclear all");
    }
}
