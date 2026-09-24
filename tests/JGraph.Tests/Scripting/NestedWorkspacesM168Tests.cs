using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// V7 (ADR 0168): nested workspaces are bound lexically. A frame is a call boundary exactly when
/// its function is not nested in the function whose frame is its closure, so a write from any
/// depth of nesting reaches the outer variable; a nested function's write of a name nothing binds
/// yet lands in the outermost parent that mentions it; its parameters and outputs are its own; and
/// every form of <c>clear</c> resolves in the active workspace - the frame that ran it and, for a
/// nested function, its parents' frames - never the base workspace a callback was called from.
/// </summary>
/// <remarks>
/// The parity fixtures <c>nested_workspaces</c> and <c>clear_active_frame</c> hold R2025b's
/// answers for the whole matrix; these pin the roads the stage touched, one assertion a road, so
/// a regression names itself.
/// </remarks>
[Collection("JG facade")]
public class NestedWorkspacesM168Tests : IDisposable
{
    private readonly MatlabScriptEngine _engine = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;

    public NestedWorkspacesM168Tests()
    {
        JG.Reset();
        _directory = Path.Combine(Path.GetTempPath(), "jgraph-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "clear_x_here.m"), "clear x\n");
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

    // --- the lexical boundary -------------------------------------------------------------------------

    [Theory]
    [InlineData(2, "[11 2]")]
    [InlineData(3, "[11 2]")]
    [InlineData(4, "[11 2]")]
    public void AWriteFromAnyDepthOfNesting_ReachesTheOuterVariable(int depth, string expected)
    {
        // outer holds v and calls l2, which calls l3, ... which calls l_depth, which writes v;
        // each l is nested in the one before it.
        var lines = new List<string> { "fprintf('%s', outer());", "function s = outer()", "v = [1 2];", "l2();", "s = mat2str(v);" };
        for (int level = 2; level <= depth; level++)
        {
            lines.Add($"function l{level}()");
            lines.Add(level < depth ? $"l{level + 1}();" : "v = v + [10 0];");
        }

        for (int level = depth; level >= 2; level--)
        {
            lines.Add("end");
        }

        lines.Add("end");
        Assert.Equal(expected, RunAndRead(string.Join('\n', lines)));
    }

    [Fact]
    public void AnElementWrite_AGrowth_AndADeletion_ThreeLevelsDown_LandInTheOuterVariable()
    {
        string printed = RunAndRead("""
            fprintf('%s', outer());
            function s = outer()
            v = [1 2 3];
            middle();
            s = mat2str(v);
                function middle()
                    inner();
                    function inner()
                        v(2) = 20;
                        v(end + 1) = 4;
                        v(1) = [];
                    end
                end
            end
            """);

        Assert.Equal("[20 3 4]", printed);
    }

    [Fact]
    public void ALocalFunctionCalledFromANestedOne_IsABoundary()
    {
        string printed = RunAndRead("""
            fprintf('%s', outer());
            function s = outer()
            v = [1 2];
            inner();
            s = mat2str(v);
                function inner()
                    helper();
                    v(2) = v(2) + 5;
                end
            end
            function helper()
            v = [9 9]; %#ok<NASGU>
            end
            """);

        Assert.Equal("[1 7]", printed);
    }

    // --- where a nested function's write lands ---------------------------------------------------------

    [Fact]
    public void AClearedSharedName_WrittenAgainInTheNestedFunction_IsTheParentsVariable()
    {
        string printed = RunAndRead("""
            fprintf('%s', outer());
            function s = outer()
            v = 1;
            inner();
            s = mat2str(v);
                function inner()
                    clear v
                    v = [7 8];
                end
            end
            """);

        Assert.Equal("[7 8]", printed);
    }

    [Fact]
    public void ANameOnlyTheNestedFunctionMentions_IsItsOwnAndFreshEachCall()
    {
        string printed = RunAndRead("""
            fprintf('%s', outer());
            function s = outer()
            s = sprintf('%d %d %d', inner(), inner(), exist('u', 'var'));
                function r = inner()
                    if ~exist('u', 'var'), u = 0; end
                    u = u + 1;
                    r = u;
                end
            end
            """);

        Assert.Equal("1 1 0", printed);
    }

    [Fact]
    public void ANestedFunctionsParameterAndOutput_ShadowTheParentsVariables()
    {
        string printed = RunAndRead("""
            fprintf('%s', outer());
            function s = outer()
            v = 1; r = 1;
            x = inner(5);
            s = sprintf('%d %d %d', v, r, x);
                function r = inner(v)
                    v = v + 1;
                    r = v;
                end
            end
            """);

        Assert.Equal("1 1 6", printed);
    }

    // --- clear in the active workspace ----------------------------------------------------------------

    [Theory]
    [InlineData("clear victim", "0 1")]
    [InlineData("clear", "0 1")]
    [InlineData("clear variables", "0 1")]
    [InlineData("clear all", "0 1")]
    [InlineData("clear -regexp vic", "0 1")]
    [InlineData("clear('-regexp', '^vic')", "0 1")]
    public void AClearInsideACallback_TakesTheCallbacksVariable_AndLeavesTheBaseWorkspaces(string form, string expected)
    {
        string printed = RunAndRead($$"""
            victim = 42;
            r = cellfun(@wipe, {0});
            fprintf('%d %d', r, exist('victim', 'var'));
            function y = wipe(~)
            victim = 7; %#ok<NASGU>
            {{form}}
            y = exist('victim', 'var');
            end
            """);

        Assert.Equal(expected, printed);
    }

    [Fact]
    public void AClearInsideANestedFunction_ReachesTheParentsVariables()
    {
        string printed = RunAndRead("""
            fprintf('%s', outer());
            function s = outer()
            v = 1; w = 2;
            inner();
            s = sprintf('%d %d', exist('v', 'var'), w);
                function inner()
                    clear v
                end
            end
            """);

        Assert.Equal("0 2", printed);
    }

    [Fact]
    public void AClearThroughEvalinCaller_AndInAScriptRunFromAFunction_TakeTheCallersVariable()
    {
        string printed = RunAndRead("""
            x = 11;
            fprintf('%s|%s|%d', through_evalin(), through_script(), exist('x', 'var'));
            function s = through_evalin()
            x = 7; %#ok<NASGU>
            clear_in_caller();
            s = num2str(exist('x', 'var'));
            end
            function clear_in_caller()
            evalin('caller', 'clear x');
            end
            function s = through_script()
            x = 7; %#ok<NASGU>
            clear_x_here
            s = num2str(exist('x', 'var'));
            end
            """);

        Assert.Equal("0|0|1", printed);
    }

    [Fact]
    public void APlainClear_UnlinksAPersistentAndKeepsItsValue_ClearVariablesTakesIt()
    {
        string printed = RunAndRead("""
            fprintf('%d %d %d %d', plain('set'), plain('get'), variables('set'), variables('get'));
            function r = plain(cmd)
            persistent p
            if isempty(p), p = 0; end
            p = p + 5;
            if strcmp(cmd, 'set'), clear, r = exist('p', 'var'); else, r = p; end
            end
            function r = variables(cmd)
            persistent p
            if isempty(p), p = 0; end
            p = p + 5;
            if strcmp(cmd, 'set'), clear variables, r = exist('p', 'var'); else, r = p; end
            end
            """);

        Assert.Equal("0 10 0 5", printed);
    }

    [Fact]
    public void AClearOfAGlobalLink_UnbindsTheNameHere_AndTheGlobalKeepsItsValue()
    {
        string printed = RunAndRead("""
            global g
            g = 9;
            r = cellfun(@unlink, {0});
            fprintf('%d %d %d', r, exist('g', 'var'), g);
            function y = unlink(~)
            global g
            clear g
            y = exist('g', 'var');
            end
            """);

        Assert.Equal("0 1 9", printed);
    }

    [Fact]
    public void ClearAllInsideACallback_TakesTheGlobalsToo()
    {
        string printed = RunAndRead("""
            global g
            g = 4;
            keep = 5; %#ok<NASGU>
            r = cellfun(@wipe, {0});
            fprintf('%d %d %d', r, exist('keep', 'var'), exist('g', 'var'));
            function y = wipe(~)
            keep = 7; %#ok<NASGU>
            clear all
            y = exist('keep', 'var');
            end
            """);

        Assert.Equal("0 1 0", printed);
    }
}
