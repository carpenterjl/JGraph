using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// V5 (ADR 0166): a persistent variable is one binding. A frame that declares one holds no value
/// of its own for the name — every read and write reaches the function's slot at once — so the
/// frames of a recursive or re-entered function see each other's writes, and a <c>clear</c> of
/// functions spares the ones that are running.
/// </summary>
/// <remarks>
/// The parity fixture <c>value_isolation_persist</c> holds R2025b's answers for the whole matrix;
/// these pin the roads the stage touched, one assertion a road, so a regression names itself.
/// </remarks>
[Collection("JG facade")]
public class PersistentBindingM166Tests : IDisposable
{
    private readonly MatlabScriptEngine _engine = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;

    public PersistentBindingM166Tests()
    {
        JG.Reset();
        _directory = Path.Combine(Path.GetTempPath(), "jgraph-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "tally.m"), """
            function y = tally()
            persistent n
            if isempty(n), n = 0; end
            n = n + 1;
            y = n;
            end
            """);
        File.WriteAllText(Path.Combine(_directory, "clear_rec.m"), """
            function y = clear_rec(depth)
            persistent p
            if isempty(p), p = zeros(1, 2); end
            p(1) = p(1) + 1;
            if depth > 0
                clear functions
                clear_rec(depth - 1);
            end
            p(2) = p(2) + 10;
            y = p;
            end
            """);
        File.WriteAllText(Path.Combine(_directory, "clear_in_callback.m"), """
            function y = clear_in_callback(n)
            persistent p
            if isempty(p), p = 0; end
            p = p + 1;
            if n
                y = cellfun(@(k) clear_then_call(k), {0});
            else
                y = p;
            end
            end
            """);
        File.WriteAllText(Path.Combine(_directory, "clear_then_call.m"), """
            function y = clear_then_call(k)
            clear functions
            y = clear_in_callback(k);
            end
            """);
        File.WriteAllText(Path.Combine(_directory, "clear_sibling.m"), """
            function y = clear_sibling(n)
            persistent p
            if isempty(p), p = 0; end
            p = p + 1;
            if n
                do_clear();
            end
            y = p;
            end
            """);
        File.WriteAllText(Path.Combine(_directory, "do_clear.m"), """
            function do_clear()
            clear functions
            end
            """);
        File.WriteAllText(Path.Combine(_directory, "clear_self.m"), """
            function y = clear_self(n)
            persistent p
            if isempty(p), p = 0; end
            p = p + 1;
            if n
                clear clear_self
            end
            y = p;
            end
            """);
        File.WriteAllText(Path.Combine(_directory, "clear_other.m"), """
            function y = clear_other()
            persistent p
            if isempty(p), p = 0; end
            p = p + 1;
            clear functions
            p = p + 1;
            y = p;
            end
            """);
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

    [Theory]
    [InlineData("p(1) = p(1) + 1;", "p(2) = p(2) + 10;", "[3 30 0]")]
    [InlineData("p = p + [1 0 0];", "p = p + [0 10 0];", "[3 30 0]")]
    [InlineData("p(end + 1) = 1;", "p(end + 1) = 2;", "[0 0 0 1 1 1 2 2 2]")]
    [InlineData("p(1) = [];", "p(end + 1) = 7;", "[7 7 7]")]
    [InlineData("eval('p(1) = p(1) + 1;');", "eval('p = p + [0 10 0];');", "[3 30 0]")]
    public void ARecursiveCall_ReadsAndWritesTheOneSlot(string before, string after, string expected)
    {
        string printed = RunAndRead($$"""
            fprintf('%s', mat2str(rec(2)));
            function r = rec(depth)
            persistent p
            if isempty(p), p = zeros(1, 3); end
            {{before}}
            if depth > 0, rec(depth - 1); end
            {{after}}
            r = p;
            end
            """);

        Assert.Equal(expected, printed);
    }

    [Fact]
    public void ARecursiveCall_SharesACellAndAStructSlot()
    {
        string printed = RunAndRead("""
            [c, s] = rec(1);
            fprintf('%d %d %d | %d %s', c{1}, c{2}, c{3}, s.n, s.log);
            function [oc, os] = rec(depth)
            persistent c s
            if isempty(c), c = {0}; s = struct('n', 0, 'log', ''); end
            c{1} = c{1} + 1;
            s.n = s.n + 1;
            if depth > 0, rec(depth - 1); end
            c{end + 1} = depth;
            s.log = [s.log sprintf('%d', depth)];
            oc = c;
            os = s;
            end
            """);

        Assert.Equal("2 0 1 | 2 01", printed);
    }

    [Fact]
    public void ACallbackReenteringTheOwner_SeesTheOuterWrite_AndIsSeenByIt()
    {
        string printed = RunAndRead("""
            fprintf('%d', reenter(1));
            function r = reenter(depth)
            persistent p
            if isempty(p), p = 0; end
            p = p + 1;
            if depth > 0, cellfun(@(k) reenter(k), {depth - 1}); end
            p = p + 10;
            r = p;
            end
            """);

        Assert.Equal("22", printed);
    }

    [Fact]
    public void AnErrorAfterAnUpdate_KeepsTheUpdate()
    {
        string printed = RunAndRead("""
            try, bump('go'); catch, end
            try, bump('go'); catch, end
            fprintf('%d', bump('peek'));
            function r = bump(cmd)
            persistent p
            if isempty(p), p = 0; end
            if strcmp(cmd, 'peek'), r = p; return; end
            p = p + 1;
            error('t:boom', 'boom');
            end
            """);

        Assert.Equal("2", printed);
    }

    [Fact]
    public void AnErrorInsideARecursiveCall_KeepsEveryLevelsUpdatesMadeBeforeIt()
    {
        string printed = RunAndRead("""
            try, rec(2); catch, end
            fprintf('%s', mat2str(rec(-1)));
            function r = rec(depth)
            persistent p
            if isempty(p), p = zeros(1, 2); end
            if depth < 0, r = p; return; end
            p(1) = p(1) + 1;
            if depth == 0, error('t:boom', 'boom'); end
            rec(depth - 1);
            p(2) = p(2) + 10;
            r = p;
            end
            """);

        Assert.Equal("[3 0]", printed);
    }

    [Fact]
    public void TwoFunctionsPersistentsOfOneName_AndAGlobalOfIt_StayApart()
    {
        string printed = RunAndRead("""
            one(); one(); two(); three();
            fprintf('%d %d %d', one(), two(), three());
            function r = one()
            persistent shared
            if isempty(shared), shared = 0; end
            shared = shared + 1; r = shared;
            end
            function r = two()
            persistent shared
            if isempty(shared), shared = 100; end
            shared = shared + 1; r = shared;
            end
            function r = three()
            global shared
            if isempty(shared), shared = 500; end
            shared = shared + 1; r = shared;
            end
            """);

        Assert.Equal("3 102 502", printed);
    }

    [Fact]
    public void ANestedFunction_WritesItsParentsSlot_AcrossARecursion()
    {
        string printed = RunAndRead("""
            fprintf('%s', mat2str(parent(1)));
            function r = parent(depth)
            persistent p
            if isempty(p), p = zeros(1, 2); end
            bump();
            if depth > 0, parent(depth - 1); end
            bump10();
            r = p;
                function bump()
                    p(1) = p(1) + 1;
                end
                function bump10()
                    p(2) = p(2) + 10;
                end
            end
            """);

        Assert.Equal("[2 20]", printed);
    }

    [Fact]
    public void ACopyOfTheSlot_IsIsolatedFromLaterWrites()
    {
        string printed = RunAndRead("""
            fprintf('%s', mat2str(rec(1)));
            function r = rec(depth)
            persistent p
            if isempty(p), p = [0 0]; end
            p(1) = p(1) + 1;
            r = p;
            if depth > 0
                inner = rec(depth - 1);
                r = [r; inner; p];
            end
            end
            """);

        Assert.Equal("[1 0;2 0;2 0]", printed);
    }

    [Fact]
    public void ALoopOverThePersistent_WithARecursiveCallInside_SharesTheSlot()
    {
        string printed = RunAndRead("""
            fprintf('%s', mat2str(rec(1)));
            function r = rec(depth)
            persistent v
            if isempty(v), v = zeros(1, 3); end
            for i = 1:3
                v(i) = v(i) + 1;
                if depth > 0 && i == 2, rec(depth - 1); end
            end
            r = v;
            end
            """);

        Assert.Equal("[2 2 2]", printed);
    }

    [Fact]
    public void TheWorkspace_ListsThePersistent_AndItsFirstReadIsTheEmptyDouble()
    {
        string printed = RunAndRead("""
            ask();
            function ask()
            persistent fresh
            names = who;
            fprintf('%d %d %s %s', exist('fresh', 'var'), any(strcmp(names, 'fresh')), class(fresh), mat2str(size(fresh)));
            end
            """);

        Assert.Equal("1 1 double [0 0]", printed);
    }

    [Theory]
    [InlineData("clear functions")]
    [InlineData("clear all")]
    [InlineData("clear tally")]
    public void AClear_ResetsAnIdleFunctionFilesPersistents(string clear)
    {
        string printed = RunAndRead($$"""
            tally(); tally();
            {{clear}}
            fprintf('%d', tally());
            """);

        Assert.Equal("1", printed);
    }

    [Fact]
    public void ClearFunctions_SparesTheFunctionsOfTheScriptThatIsRunning()
    {
        string printed = RunAndRead("""
            count(); count();
            clear functions
            fprintf('%d', count());
            function r = count()
            persistent n
            if isempty(n), n = 0; end
            n = n + 1; r = n;
            end
            """);

        Assert.Equal("3", printed);
    }

    [Fact]
    public void ClearFunctions_InsideARecursiveCall_ResetsNoLevel()
    {
        Assert.Equal("[3 30]", RunAndRead("fprintf('%s', mat2str(clear_rec(2)));"));
    }

    [Fact]
    public void ClearFunctions_InsideACallbackReenteringTheOwner_LeavesTheOwnersSlot()
    {
        Assert.Equal("2", RunAndRead("fprintf('%d', clear_in_callback(1));"));
    }

    [Fact]
    public void ClearFunctions_FromASiblingWhileTheOwnerIsSuspended_LeavesTheOwnersSlot()
    {
        Assert.Equal("1 2", RunAndRead("a = clear_sibling(1); fprintf('%d %d', a, clear_sibling(0));"));
    }

    [Fact]
    public void ClearingARunningFunctionByName_LeavesItsSlot()
    {
        Assert.Equal("1 2", RunAndRead("a = clear_self(1); fprintf('%d %d', a, clear_self(0));"));
    }

    [Fact]
    public void ClearFunctions_SparesTheRunningFunction_AndResetsTheIdleOnes()
    {
        Assert.Equal("2 1", RunAndRead("tally(); tally(); a = clear_other(); fprintf('%d %d', a, tally());"));
    }

    [Fact]
    public void ClearFunctions_ResetsAFunctionOnceItHasReturned()
    {
        Assert.Equal("[1 10]", RunAndRead("""
            clear_rec(1);
            clear functions
            fprintf('%s', mat2str(clear_rec(0)));
            """));
    }
}
