using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M28.S5: the parts of MATLAB with no JGS counterpart — cell arrays, structs, switch, try/catch,
/// functions with several outputs, <c>nargin</c>/<c>varargin</c>, anonymous functions, and
/// <c>global</c>.
/// </summary>
[Collection("JG facade")]
public class MatlabCellStructTests : IDisposable
{
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabCellStructTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private ScriptRunResult RunMatlab(string code)
    {
        var context = new ScriptContext(_output, (_, figure) => _figures.Add(figure), null);
        return JgsRunner.Run(code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
    }

    private string RunAndRead(string code)
    {
        ScriptRunResult result = RunMatlab(code);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText;
    }

    // --- Cells ----------------------------------------------------------------------------------

    [Fact]
    public void BracesTakeContentsOut_ParensGiveACellBack()
    {
        Assert.Contains("two", RunAndRead("c = {1, 'two', [3 4]};\ndisp(c{2})"), StringComparison.Ordinal);

        _output.Normal.Clear();
        Assert.Contains("{'two'}", RunAndRead("c = {1, 'two'};\ndisp(c(2))"), StringComparison.Ordinal);
    }

    [Fact]
    public void AssigningPastTheEnd_GrowsTheCell()
    {
        // The accumulation idiom: names{end+1} = 'c'.
        string output = RunAndRead("""
            names = {'a', 'b'};
            names{end+1} = 'c';
            disp(names{3})
            """);

        Assert.Contains("c", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ACellCanBeBuiltFromNothing()
    {
        Assert.Contains("first", RunAndRead("c = {};\nc{1} = 'first';\ndisp(c{1})"), StringComparison.Ordinal);
    }

    [Fact]
    public void IndexingSomethingElseWithBraces_SaysSo()
    {
        ScriptRunResult result = RunMatlab("x = [1 2 3];\ndisp(x{1})");

        Assert.False(result.Success);
        Assert.Contains("Braces index a cell array", result.Message!, StringComparison.Ordinal);
    }

    // --- Structs --------------------------------------------------------------------------------

    [Fact]
    public void FieldsCanBeAssignedIntoAStructThatDoesNotExistYet()
    {
        string output = RunAndRead("""
            opts.width = 3;
            opts.label = 'trace';
            disp(opts.width)
            disp(opts.label)
            """);

        Assert.Contains("3", output, StringComparison.Ordinal);
        Assert.Contains("trace", output, StringComparison.Ordinal);
    }

    [Fact]
    public void NestedFieldsAreCreatedOnTheWay()
    {
        Assert.Contains("5", RunAndRead("cfg.plot.width = 5;\ndisp(cfg.plot.width)"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheDynamicFormPicksTheFieldAtRunTime()
    {
        string output = RunAndRead("""
            s.alpha = 1;
            name = 'alpha';
            disp(s.(name))
            """);

        Assert.Contains("1", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadingAMissingField_SaysWhichOne()
    {
        ScriptRunResult result = RunMatlab("s.a = 1;\ndisp(s.b)");

        Assert.False(result.Success);
        Assert.Contains("no field 'b'", result.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public void AStructIsCopiedOnAssignment()
    {
        Assert.Contains("1", RunAndRead("a.v = 1;\nb = a;\nb.v = 99;\ndisp(a.v)"), StringComparison.Ordinal);
    }

    // --- switch and try/catch -------------------------------------------------------------------

    [Fact]
    public void SwitchPicksOneArm_AndNeverFallsThrough()
    {
        string output = RunAndRead("""
            mode = 'slow';
            switch mode
                case 'fast'
                    step = 1;
                case {'slow', 'careful'}
                    step = 10;
                otherwise
                    step = 5;
            end
            disp(step)
            """);

        Assert.Contains("10", output, StringComparison.Ordinal);
        Assert.DoesNotContain("1\n1", output, StringComparison.Ordinal);
    }

    [Fact]
    public void SwitchFallsBackToOtherwise()
    {
        Assert.Contains("5", RunAndRead("switch 'other'\n case 'a'\n  x = 1;\n otherwise\n  x = 5;\nend\ndisp(x)"), StringComparison.Ordinal);
    }

    [Fact]
    public void TryCatchRecovers_AndBindsTheError()
    {
        string output = RunAndRead("""
            try
                v = [1 2 3];
                disp(v(99))
            catch err
                disp('recovered')
                disp(err.message)
            end
            """);

        Assert.Contains("recovered", output, StringComparison.Ordinal);
        Assert.Contains("out of range", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ABareCatchWorksToo()
    {
        Assert.Contains("caught", RunAndRead("try\n  x = [1];\n  disp(x(9))\ncatch\n  disp('caught')\nend"), StringComparison.Ordinal);
    }

    // --- Functions ------------------------------------------------------------------------------

    [Fact]
    public void AFunctionCanReturnSeveralValues()
    {
        string output = RunAndRead("""
            [s, p] = both(3, 4);
            fprintf('%d %d\n', s, p);

            function [total, product] = both(a, b)
            total = a + b;
            product = a * b;
            end
            """);

        Assert.Contains("7 12", output, StringComparison.Ordinal);
    }

    [Fact]
    public void OneOutputCanBeDiscarded()
    {
        string output = RunAndRead("""
            [~, product] = both(3, 4);
            disp(product)

            function [total, product] = both(a, b)
            total = a + b;
            product = a * b;
            end
            """);

        Assert.Contains("12", output, StringComparison.Ordinal);
    }

    [Fact]
    public void NarginAndVararginReportWhatArrived()
    {
        string output = RunAndRead("""
            disp(counted(1, 2, 3))
            disp(counted(1))

            function n = counted(first, varargin)
            n = nargin;
            end
            """);

        Assert.Contains("3", output, StringComparison.Ordinal);
        Assert.Contains("1", output, StringComparison.Ordinal);
    }

    [Fact]
    public void VararginArrivesAsACell()
    {
        Assert.Contains("b", RunAndRead("""
            disp(second('a', 'b'))

            function v = second(varargin)
            v = varargin{2};
            end
            """), StringComparison.Ordinal);
    }

    [Fact]
    public void AFunctionThatNeverAssignsItsOutput_SaysSo()
    {
        ScriptRunResult result = RunMatlab("""
            x = forgetful(1);

            function y = forgetful(v)
            z = v;
            end
            """);

        Assert.False(result.Success);
        Assert.Contains("without assigning its output 'y'", result.Message!, StringComparison.Ordinal);
    }

    // --- Anonymous functions and handles --------------------------------------------------------

    [Fact]
    public void AnAnonymousFunctionIsCallable()
    {
        Assert.Contains("[1, 4, 9]", RunAndRead("square = @(x) x.^2;\ndisp(square([1 2 3]))"), StringComparison.Ordinal);
    }

    [Fact]
    public void AnAnonymousFunctionCapturesValues_NotVariables()
    {
        // MATLAB snapshots the free variables when the handle is made: changing 'k' afterwards must
        // not change what the handle computes.
        string output = RunAndRead("""
            k = 10;
            addk = @(x) x + k;
            k = 999;
            disp(addk(1))
            """);

        Assert.Contains("11", output, StringComparison.Ordinal);
        Assert.DoesNotContain("1000", output, StringComparison.Ordinal);
    }

    [Fact]
    public void AHandleToANamedFunctionCanBeCalled()
    {
        Assert.Contains("2", RunAndRead("f = @twice;\ndisp(f(1))\n\nfunction y = twice(x)\ny = 2*x;\nend"), StringComparison.Ordinal);
    }

    [Fact]
    public void AnAnonymousVararginCollectsTheRemainingArguments()
    {
        // MATLAB: 'varargin' is not an ordinary parameter here either — the handle takes any count.
        string output = RunAndRead("""
            f = @(varargin) numel(varargin);
            disp(f(1, 2, 3))
            disp(f())
            """);

        Assert.Contains("3", output, StringComparison.Ordinal);
        Assert.Contains("0", output, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAnonymousVararginFollowsTheFixedParameters()
    {
        string output = RunAndRead("""
            g = @(a, varargin) a + numel(varargin);
            disp(g(10, 1, 2))
            disp(g(40))
            """);

        Assert.Contains("12", output, StringComparison.Ordinal);
        Assert.Contains("40", output, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAnonymousVararginArrivesAsACell()
    {
        string output = RunAndRead("""
            pick = @(varargin) varargin{2};
            disp(pick('a', 'b', 'c'))
            kind = @(varargin) class(varargin);
            disp(kind(1))
            """);

        Assert.Contains("b", output, StringComparison.Ordinal);
        Assert.Contains("cell", output, StringComparison.Ordinal);
    }

    [Fact]
    public void NarginInsideAnAnonymousFunctionCountsItsOwnCall()
    {
        // The handle's own arity wins over the nargin of the function that defined it: MATLAB answers
        // 1 for the inner call even though 'outer' received three arguments.
        string output = RunAndRead("""
            h = @(varargin) nargin;
            disp(h(1, 2, 3, 4))
            outer(1, 2, 3)

            function outer(a, b, c)
            f = @(varargin) nargin;
            disp(f(7))
            end
            """);

        Assert.Contains("4", output, StringComparison.Ordinal);
        Assert.Contains("1", output, StringComparison.Ordinal);
        Assert.DoesNotContain("3", output, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAnonymousVararginStillRequiresTheFixedArguments()
    {
        ScriptRunResult result = RunMatlab("""
            n = @(a, varargin) a;
            n();
            """);

        Assert.False(result.Success);
        Assert.Contains("expects 2 argument(s) but got 0", result.Message!, StringComparison.Ordinal);
    }

    // --- global ---------------------------------------------------------------------------------

    [Fact]
    public void GlobalSharesOneVariableBetweenScriptAndFunction()
    {
        string output = RunAndRead("""
            global counter
            counter = 0;
            bump();
            bump();
            disp(counter)

            function bump()
            global counter
            counter = counter + 1;
            end
            """);

        Assert.Contains("2", output, StringComparison.Ordinal);
    }

    [Fact]
    public void AppendingToAGlobalCellInsideAFunction_WritesBackToTheGlobal()
    {
        // The accumulation idiom reached through a global. c{end+1} = s used to declare a cell in
        // the helper's own workspace and drop it when the call returned — in silence, no error, the
        // appended element simply not there — while the numeric append beside it worked, because
        // growing a vector mutates the value the global workspace is already holding rather than
        // rebinding the name. MATLAB R2024a counts 2 for all three.
        string output = RunAndRead("""
            global c
            c = {};
            add('a');
            add('b');
            assert(numel(c) == 2);
            assert(strcmp(c{1}, 'a'));
            assert(strcmp(c{2}, 'b'));

            global v
            v = [];
            addv(1);
            addv(2);
            assert(numel(v) == 2);

            global s2
            s2 = {};
            addwhole('p');
            addwhole('q');
            assert(numel(s2) == 2);
            disp('appended')

            function add(s)
            global c
            c{end+1} = s;
            end

            function addv(n)
            global v
            v(end+1) = n;
            end

            function addwhole(x)
            global s2
            s2 = [s2, {x}];
            end
            """);

        Assert.Contains("appended", output, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryWriteThatRebuildsAGlobal_LandsInTheGlobalWorkspace()
    {
        // A brace append is not the only write that replaces its container instead of filling one in:
        // deleting elements, writing a field, and growing a struct array all rebuild and rebind, and
        // each of them rebound the running frame's own copy of the name. Every assert here is what
        // MATLAB R2024a answers.
        string output = RunAndRead("""
            global g
            g = {'a', 'b'};
            setfirst();
            assert(strcmp(g{1}, 'y'));

            global n
            n = [1 2 3];
            drop();
            assert(numel(n) == 2);

            global board
            board = cell(2, 2);
            corner();
            assert(strcmp(board{2, 2}, 'w'));

            global s
            s = struct('f', 1);
            bump();
            assert(s.f == 9);

            global arr
            arr = struct('f', 1);
            extend();
            assert(numel(arr) == 2);

            % A global nobody has assigned holds [], which is the empty of every container — so the
            % append makes a cell of it rather than refusing the name, and it is 0-by-0 as MATLAB's is.
            global fresh
            assert(isequal(size(fresh), [0 0]));
            push();
            push();
            assert(numel(fresh) == 2);
            disp('rebound')

            function setfirst()
            global g
            g{1} = 'y';
            end

            function drop()
            global n
            n(1) = [];
            end

            function corner()
            global board
            board{2, 2} = 'w';
            end

            function bump()
            global s
            s.f = 9;
            end

            function extend()
            global arr
            arr(2).f = 7;
            end

            function push()
            global fresh
            fresh{end+1} = 'k';
            end
            """);

        Assert.Contains("rebound", output, StringComparison.Ordinal);
    }
}
