using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// V9 (ADR 0170): the call contract. The output count a call asks for reaches the callee on every
/// road, zero for a statement; the call site reaches the frame on every road that has argument
/// syntax and is cleared for a callback; a call asked for more outputs than the callee has is
/// refused before the callee runs, with R2025b's identifier and in R2025b's order; <c>load</c>
/// binds the loaded names only when asked for none; an unassigned output is refused in R2025b's
/// words.
/// </summary>
/// <remarks>
/// The parity fixtures <c>nargout_propagation</c>, <c>inputname_roads</c> and
/// <c>output_count_checks</c> hold R2025b's answers for the whole matrix; these pin the roads the
/// stage touched, one assertion a road, so a regression names itself.
/// </remarks>
[Collection("JG facade")]
public class CallContractM170Tests : IDisposable
{
    private readonly MatlabScriptEngine _engine = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;

    public CallContractM170Tests()
    {
        JG.Reset();
        _directory = Path.Combine(Path.GetTempPath(), "jgraph-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "cc_file_fn.m"), "function [a, b] = cc_file_fn()\nb = 2;\nend\n");
        File.WriteAllText(Path.Combine(_directory, "cc_script_inputname.m"),
            "try\n    q = inputname(1);\n    fprintf('[%s]', q);\ncatch e\n    fprintf('%s', e.identifier);\nend\n");
        File.WriteAllText(Path.Combine(_directory, "cc_noout_file.m"), "function cc_noout_file(varargin)\nfprintf('ran');\nend\n");
    }

    public void Dispose()
    {
        JG.Reset();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }

    private string RunAndRead(string code)
    {
        ScriptRunResult result = _engine.RunAsync(
            code,
            new ScriptContext(_output, (_, _) => { }, _directory, resolvePath: null, figureFiles: new TestFigureFiles()),
            CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Trim();
    }

    /// <summary>Runs <paramref name="code"/> as the file <paramref name="fileName"/>, so a local function's refusal names the file.</summary>
    private string RunAsFileAndRead(string code, string fileName)
    {
        string path = Path.Combine(_directory, fileName);
        File.WriteAllText(path, code);
        ScriptRunResult result = _engine.RunAsync(
            code,
            new ScriptContext(_output, (_, _) => { }, _directory, resolvePath: null, figureFiles: new TestFigureFiles())
            {
                ScriptPath = path,
            },
            CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Trim();
    }

    private const string Np = "function y = np(varargin)\nfprintf('%d;', nargout);\ny = 0;\nend\n";
    private const string Np2 = "function [a, b] = np2(varargin)\nfprintf('%d;', nargout);\na = 1;\nb = 2;\nend\n";

    // --- V9.1: the count reaches every callee ------------------------------------------------------

    [Theory]
    [InlineData("np();", "0;")]
    [InlineData("np;", "0;")]
    [InlineData("v = np();", "1;")]
    [InlineData("h = @np; h();", "0;")]
    [InlineData("h = @np; v = h();", "1;")]
    [InlineData("feval(@np);", "0;")]
    [InlineData("feval('np');", "0;")]
    [InlineData("g = @() np(); g();", "0;")]
    [InlineData("g = @() np(); v = g();", "1;")]
    [InlineData("cellfun(@np, {1});", "0;")]
    [InlineData("r = cellfun(@np, {1});", "1;")]
    [InlineData("arrayfun(@np, 1);", "0;")]
    [InlineData("structfun(@np, struct('a', 1));", "0;")]
    [InlineData("eval('np();');", "0;")]
    [InlineData("h = str2func('np'); h();", "0;")]
    [InlineData("c = {@np}; c{1}();", "0;")]
    [InlineData("s.f = @np; s.f();", "0;")]
    [InlineData("builtin('feval', @np);", "0;")]
    [InlineData("if np(), end", "1;")]
    [InlineData("q = [np() np()];", "1;1;")]
    [InlineData("for k = np(), end", "1;")]
    public void AStatementAsksForZeroOutputsAndAValueForOne_OnEveryRoad(string code, string expected)
    {
        Assert.Equal(expected, RunAndRead(code + "\n" + Np));
    }

    [Theory]
    [InlineData("[a, b] = np2();", "2;")]
    [InlineData("h = @np2; [a, b] = h();", "2;")]
    [InlineData("[a, b] = feval(@np2);", "2;")]
    [InlineData("g = @() np2(); [a, b] = g();", "2;")]
    [InlineData("[r1, r2] = cellfun(@np2, {1});", "2;")]
    [InlineData("[~, ~] = np2();", "2;")]
    public void AMultipleAssignmentAsksForItsTargetCount_OnEveryRoad(string code, string expected)
    {
        Assert.Equal(expected, RunAndRead(code + "\n" + Np2));
    }

    [Fact]
    public void AVarargoutRelayHandsOnTheCountItWasAskedFor()
    {
        string code = "relay();\nv = relay();\n[a, b] = relay();\n"
            + "function varargout = relay()\nfprintf('r%d;', nargout);\n[varargout{1:nargout}] = np2();\nend\n" + Np2;
        Assert.Equal("r0;0;r1;1;r2;2;", RunAndRead(code));
    }

    [Theory]
    [InlineData("two();", "1 1")]
    [InlineData("vo();", "1 5")]
    [InlineData("nu();", "0")]
    [InlineData("h = @two; h();", "1 1")]
    [InlineData("feval(@two);", "1 1")]
    [InlineData("g = @() two(); g();", "1 1")]
    [InlineData("cellfun(@two, {1});", "1 1")]
    [InlineData("h = @sin; h(0);", "1 0")]
    [InlineData("h = @size; h(1);", "1 [1 1]")]
    [InlineData("g = @() 42; g();", "1 42")]
    public void AStatementCallBindsAnsToTheFirstOutputTheCalleeMadeAnyway(string code, string expected)
    {
        string body = "clear ans\n" + code + "\nif exist('ans', 'var'), fprintf('1 %s', mat2str(ans)); else, fprintf('0'); end\n"
            + "function [a, b] = two(varargin)\na = 1;\nb = 2;\nend\n"
            + "function varargout = vo()\nvarargout{1} = 5;\nend\n"
            + "function y = nu()\nend\n";
        Assert.Equal(expected, RunAndRead(body));
    }

    [Fact]
    public void AStatementCallOfAFunctionThatAssignedNoOutputIsNotAnError()
    {
        Assert.Equal("ok", RunAndRead("nu();\nfprintf('ok');\nfunction y = nu()\nend\n"));
    }

    [Fact]
    public void AShadowingFileWithNoOutputsRunsAsABareStatement()
    {
        // clear.m in m145's fixture is the measured case: `clear;` calls the file asked for nothing.
        File.WriteAllText(Path.Combine(_directory, "clear.m"), "function clear(varargin)\nfprintf('shadow');\nend\n");
        Assert.Equal("shadow", RunAndRead("clear;\n"));
    }

    // --- V9.1: load binds only when asked for none ---------------------------------------------------

    [Theory]
    [InlineData("load(fn);", "1")]
    [InlineData("h = @load; h(fn);", "1")]
    [InlineData("feval(@load, fn);", "1")]
    [InlineData("builtin('load', fn);", "1")]
    [InlineData("S = load(fn);", "0")]
    [InlineData("h = @load; S = h(fn);", "0")]
    [InlineData("S = feval('load', fn);", "0")]
    [InlineData("g = @(f) load(f); S = g(fn);", "0")]
    [InlineData("C = cellfun(@load, {fn}, 'UniformOutput', false);", "0")]
    public void LoadBindsTheWorkspaceOnlyWhenAskedForNoOutput(string code, string expected)
    {
        string body = "fn = [tempname '.mat'];\nv = [1 2 3];\nsave(fn, 'v');\nclear v\n" + code
            + "\nfprintf('%d', exist('v', 'var'));\ndelete(fn);\n";
        Assert.Equal(expected, RunAndRead(body));
    }

    [Fact]
    public void LoadIntoAnAnonymousFunctionsWorkspaceIsRefused()
    {
        string body = "fn = [tempname '.mat'];\nv = 1;\nsave(fn, 'v');\ng = @(f) load(f);\n"
            + "try\n    g(fn);\ncatch e\n    fprintf('%s|%s', e.identifier, e.message);\nend\ndelete(fn);\n";
        Assert.Equal("MATLAB:err_static_workspace_violation|Attempt to add \"v\" to a static workspace.", RunAndRead(body));
    }

    [Fact]
    public void AValueLoadLeavesTheCallersVariableAlone()
    {
        string body = "fn = [tempname '.mat'];\nv = [1 2 3];\nsave(fn, 'v');\nv(1) = 7;\nS = load(fn);\n"
            + "fprintf('%s %s', mat2str(S.v), mat2str(v));\ndelete(fn);\n";
        Assert.Equal("[1 2 3] [7 2 3]", RunAndRead(body));
    }

    // --- V9.2: the call site --------------------------------------------------------------------

    [Theory]
    [InlineData("in1(x);", "[x]")]
    [InlineData("h = @in1; h(x);", "[x]")]
    [InlineData("h = @two_named; [a, b] = h(x);", "[x]")]
    [InlineData("feval(@in1, x);", "[x]")]
    [InlineData("[a, b] = feval(@two_named, x);", "[x]")]
    [InlineData("feval(@in1, x + 1);", "[]")]
    [InlineData("g = @(q) in1(q); g(x);", "[q]")]
    [InlineData("g = @(q) in1(x); g(1);", "[x]")]
    [InlineData("cellfun(@in1, {x});", "[]")]
    [InlineData("arrayfun(@in1, x);", "[]")]
    [InlineData("structfun(@in1, struct('a', x));", "[]")]
    [InlineData("in1(x(1));", "[]")]
    [InlineData("in1(5);", "[]")]
    [InlineData("in_k(x, x, 3);", "[]")]
    [InlineData("eval('in1(x);');", "[x]")]
    [InlineData("h = str2func('in1'); h(x);", "[x]")]
    [InlineData("c = {@in1}; c{1}(x);", "[x]")]
    [InlineData("s.f = @in1; s.f(x);", "[x]")]
    public void InputnameReadsTheCallSiteOnEveryRoad(string code, string expected)
    {
        string body = "x = 5;\n" + code + "\n"
            + "function v = in1(a)\nfprintf('[%s]', inputname(1));\nv = a;\nend\n"
            + "function in_k(a, b, k)\nfprintf('[%s]', inputname(k));\nend\n"
            + "function [a, b] = two_named(v)\na = inputname(1);\nb = v;\nfprintf('[%s]', a);\nend\n";
        Assert.Equal(expected, RunAndRead(body));
    }

    [Fact]
    public void ADottedMethodCallsReceiverIsItsFirstArgument()
    {
        File.WriteAllText(Path.Combine(_directory, "CcBox.m"),
            "classdef CcBox\n    methods\n        function v = inp(obj, x)\n            fprintf('[%s][%s]', inputname(1), inputname(2));\n            v = 0;\n        end\n    end\nend\n");
        Assert.Equal("[b][x][b][x]", RunAndRead("x = 5;\nb = CcBox;\nb.inp(x);\ninp(b, x);\n"));
    }

    [Fact]
    public void InputnameIsRefusedInAScript()
    {
        Assert.Equal("MATLAB:inputname:notSupportedInScript", RunAndRead("x = 5;\ncc_script_inputname\n"));
    }

    [Fact]
    public void InputnameIsRefusedForIndexZero()
    {
        Assert.Equal("MATLAB:inputname:argNumberNotValid", RunAndRead(
            "x = 5;\nf(x);\nfunction f(a)\ntry\n    inputname(0);\ncatch e\n    fprintf('%s', e.identifier);\nend\nend\n"));
    }

    [Fact]
    public void AnErrorHandlerIsCalledWithNoSite()
    {
        string body = "x = 5;\ncellfun(@(q) error('a:b', 'c'), {x}, 'ErrorHandler', @in2);\n"
            + "function v = in2(a, b)\nfprintf('[%s][%s]', inputname(1), inputname(2));\nv = 0;\nend\n";
        Assert.Equal("[][]", RunAndRead(body));
    }

    // --- V9.3: too many outputs ------------------------------------------------------------------

    [Theory]
    [InlineData("x = none_out();", "MATLAB:TooManyOutputs|")]
    [InlineData("x = none_out_arg(bump());", "MATLAB:TooManyOutputs|")]
    [InlineData("[a, b] = one_out_arg(bump());", "MATLAB:TooManyOutputs|")]
    [InlineData("x = disp(bump());", "bump;MATLAB:maxlhs|")]
    [InlineData("f = @(v) none_out_arg(v); x = f(bump());", "bump;MATLAB:TooManyOutputs|")]
    [InlineData("f = @(v) disp(v); x = f(bump());", "bump;MATLAB:maxlhs|")]
    [InlineData("f = @(v) none_out_arg(bump()); x = f(1);", "MATLAB:TooManyOutputs|")]
    [InlineData("h = @none_out_arg; x = h(bump());", "bump;MATLAB:TooManyOutputs|")]
    [InlineData("x = feval(@none_out_arg, bump());", "bump;MATLAB:TooManyOutputs|")]
    [InlineData("x = feval('disp', bump());", "bump;MATLAB:maxlhs|")]
    [InlineData("[a, b, c] = max([1 2]);", "MATLAB:maxlhs|")]
    [InlineData("r = cellfun(@none_out_arg, {1});", "MATLAB:TooManyOutputs|")]
    [InlineData("r = cellfun(@(x) error('a:b', 'c'), {1});", "MATLAB:maxlhs|")]
    [InlineData("r = arrayfun(@none_out_arg, 1);", "MATLAB:TooManyOutputs|")]
    [InlineData("x = builtin('disp', 1);", "MATLAB:maxlhs|")]
    [InlineData("x = none_out;", "MATLAB:TooManyOutputs|")]
    [InlineData("x = eval('none_out()');", "MATLAB:TooManyOutputs|")]
    [InlineData("x = hold('on');", "MATLAB:TooManyOutputs|")]
    [InlineData("x = clc;", "MATLAB:maxlhs|")]
    [InlineData("x = error('a:b', '%d', bump());", "bump;MATLAB:maxlhs|")]
    [InlineData("f = @(x) x; [a, b] = f(1);", "MATLAB:needMoreRhsOutputs|")]
    [InlineData("x = cc_noout_file(bump());", "MATLAB:TooManyOutputs|")]
    public void ACallAskedForMoreOutputsThanTheCalleeHasIsRefused_InMatlabsOrder(string code, string expected)
    {
        string body = "try\n" + code + "\ncatch e\n    fprintf('%s|', e.identifier);\nend\n"
            + "function none_out()\nend\n"
            + "function none_out_arg(v)\nend\n"
            + "function y = one_out_arg(v)\ny = v;\nend\n"
            + "function y = bump()\nfprintf('bump;');\ny = 1;\nend\n";
        Assert.Equal(expected, RunAndRead(body));
    }

    [Fact]
    public void AValidatorIsAskedForNothing()
    {
        // stess_34's shape: mustBePositive has no outputs, and the arguments block runs it as a statement.
        string body = "fprintf('%g', scaled(2));\nfunction y = scaled(x, factor)\n    arguments\n        x (1,1) double {mustBePositive}\n"
            + "        factor (1,1) double {mustBePositive} = 2\n    end\n    y = x * factor;\nend\n";
        Assert.Equal("4", RunAndRead(body));
    }

    [Fact]
    public void AMultiOutputBuiltinAnswersForItsOwnCount()
    {
        // nargout('fgets') is 1 in R2025b and fgets gives two (stess_48); the recorded count does not
        // bound a builtin with a multi-output body.
        string body = "fn = [tempname '.txt'];\nfid = fopen(fn, 'w');\nfprintf(fid, '1 2 3\\n4 5 6\\n');\nfclose(fid);\n"
            + "fid = fopen(fn, 'r');\n[ln, lt] = fgets(fid);\nfclose(fid);\ndelete(fn);\nfprintf('%s %d', strtrim(ln), lt);\n";
        Assert.Equal("1 2 3 1", RunAndRead(body));
    }

    [Fact]
    public void AStatementCallIsNeverTooMany()
    {
        Assert.Equal("ran;ran;ran;", RunAndRead(
            "none_out();\ncellfun(@none_out, {1});\nh = @none_out; h();\nfunction none_out(varargin)\nfprintf('ran;');\nend\n"));
    }

    [Fact]
    public void ACallbackIsInvokedAskedForNothing()
    {
        // A timer's TimerFcn whose body calls a function with no outputs (a105's shape).
        string body = "t = timer('TimerFcn', @(src, evt) tick(), 'StartDelay', 0.01);\nstart(t);\nwait(t);\ndelete(t);\n"
            + "function tick()\nfprintf('tick');\nend\n";
        Assert.Equal("tick", RunAndRead(body));
    }

    [Fact]
    public void NargoutQueryWorksInsideAFunctionBody()
    {
        Assert.Equal("1 2 0 -1 -1", RunAndRead(
            "q();\nfunction q()\nfprintf('%d %d %d %d %d', nargout('np'), nargout('np2'), nargout('np0'), nargout('vo'), nargout(@() np()));\nend\n"
            + "function y = np()\ny = 0;\nend\nfunction [a, b] = np2()\na = 1;\nb = 2;\nend\nfunction np0()\nend\nfunction varargout = vo()\nend\n"));
        Assert.Equal(0, JgsBuiltinOutputCountsView.MaxOf("disp"));
        Assert.Equal(2, JgsBuiltinOutputCountsView.MaxOf("max"));
        Assert.Null(JgsBuiltinOutputCountsView.MaxOf("size"));
        Assert.Null(JgsBuiltinOutputCountsView.MaxOf("pause"));
    }

    // --- V9.1: the unassigned-output refusal (#84) -------------------------------------------------

    [Theory]
    [InlineData("x = nu();", "Output argument \"y\" (and possibly others) not assigned a value in the execution with \"main>nu\" function.")]
    [InlineData("[~, b] = second_only();", "Output argument \"a\" (and possibly others) not assigned a value in the execution with \"main>second_only\" function.")]
    [InlineData("[a, b] = feval(@second_only);", "Output argument \"a\" (and possibly others) not assigned a value in the execution with \"main>second_only\" function.")]
    [InlineData("x = vo_none();", "One or more output arguments not assigned during call to \"varargout\".")]
    [InlineData("[a, b] = vo_first();", "Output argument \"varargout{2}\" (and possibly others) not assigned a value in the execution with \"main>vo_first\" function.")]
    [InlineData("[~, b] = cc_file_fn();", "Output argument \"a\" (and possibly others) not assigned a value in the execution with \"cc_file_fn\" function.")]
    [InlineData("x = outer();", "Output argument \"y\" (and possibly others) not assigned a value in the execution with \"main>outer/nun\" function.")]
    public void AnUnassignedOutputIsRefusedInMatlabsWords(string code, string expected)
    {
        string body = "try\n" + code + "\ncatch e\n    fprintf('%s|%s', e.identifier, e.message);\nend\n"
            + "function y = nu()\nend\n"
            + "function [a, b] = second_only()\nb = 2;\nend\n"
            + "function varargout = vo_none()\nend\n"
            + "function varargout = vo_first()\nvarargout{1} = 1;\nend\n"
            + "function s = outer()\ns = nun();\n    function y = nun()\n    end\nend\n";
        Assert.Equal("MATLAB:unassignedOutputs|" + expected, RunAsFileAndRead(body, "main.m"));
    }
}

/// <summary>A test's window onto the generated output-count table.</summary>
internal static class JgsBuiltinOutputCountsView
{
    public static int? MaxOf(string name) => JgsBuiltinOutputCounts.TryGet(name, out int count, out _) ? count : null;
}
