using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Jgs.Net;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Stage 5 of the .NET and shared-library interop plan (ADR 0178): listeners on .NET events, the
/// script thread's queue of work .NET asked for from other threads, delegates made from function
/// handles, deadlock detection, and a deleted .NET handle. The R2025b answers are the
/// net_events_delegates fixture's and probe5a-h's; these tests pin the machinery under them.
/// </summary>
[Collection("JG facade")]
public class NetInteropM178Tests : IDisposable
{
    private static readonly string Assembly = Path.Combine(
        AppContext.BaseDirectory, "MatlabParity", "fixtures", "interop", "JGraph.Interop.TestAssembly.dll");

    /// <summary>A log every callback in these scripts writes to, read back with <c>logged()</c>.</summary>
    private const string LogFunctions =
        "\nfunction note(t)\nglobal L\nL{end + 1} = t;\nend\n"
        + "function s = logged()\nglobal L\nif isempty(L), s = 'none'; else, s = strjoin(L, ' '); end\nL = {};\nend\n"
        + "function busy(t)\nt0 = tic;\nwhile toc(t0) < t\nend\nend\n";

    private RecordingScriptOutput _output = new();

    public NetInteropM178Tests() => JG.Reset();

    public void Dispose()
    {
        NetCallbackQueue.DrainCurrent(); // nothing a test queued outlives it on this thread
        JG.Reset();
    }

    private string Run(string code)
    {
        _output = new RecordingScriptOutput();
        var context = new ScriptContext(_output, (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(Load + code + LogFunctions, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Trim().ReplaceLineEndings("\n");
    }

    private static string Load => $"NET.addAssembly('{Assembly}'); logged();\n";

    private static string Id(string expression) =>
        $"try, {expression}; fprintf('none|'); catch e, fprintf('%s|', e.identifier); end\n";

    // --- events -----------------------------------------------------------------------------------

    [Fact]
    public void AnEventRaisedInsideTheScriptsOwnNetCallRunsItsListenersBeforeTheCallReturns()
    {
        Assert.Equal("before B A after", Run(
            "p = JGTest.Publisher();\n"
            + "a = addlistener(p, 'Fired', @(s, e) note('A')); b = addlistener(p, 'Fired', @(s, e) note('B'));\n"
            + "note('before'); p.RaiseSync(); note('after'); fprintf('%s', logged());"));
    }

    [Fact]
    public void AnEventFromAnotherThreadWaitsForPauseAndIsNotDeliveredBetweenStatements()
    {
        Assert.Equal("none|none|1 2", Run(
            "p = JGTest.Publisher(); l = addlistener(p, 'Custom', @(s, e) note(num2str(e.Value)));\n"
            + "p.RaiseOnThreadPool(int32(2), int32(5)); busy(0.3);\n"
            + "x = 1; y = x + 1; fprintf('%s|', logged()); drawnow nocallbacks; fprintf('%s|', logged());\n"
            + "pause(0.1); fprintf('%s', logged());"));
    }

    [Fact]
    public void OneNetHandlerStandsForEveryListenerAndTheLastOneToGoRemovesIt()
    {
        Assert.Equal("1|1|0|1|0", Run(
            "p = JGTest.Publisher();\n"
            + "a = addlistener(p, 'Fired', @(s, e) 1); fprintf('%d|', p.FiredHandlerCount);\n"
            + "b = addlistener(p, 'Fired', @(s, e) 2); fprintf('%d|', p.FiredHandlerCount);\n"
            + "delete(a); delete(b); fprintf('%d|', p.FiredHandlerCount);\n"
            + "c = listener(p, 'Fired', @(s, e) 3); fprintf('%d|', p.FiredHandlerCount);\n"
            + "clear c\nfprintf('%d', p.FiredHandlerCount);"));
    }

    [Fact]
    public void AQueuedEventReachingAListenerDisabledOrDeletedSinceIsDropped()
    {
        // R2025b crashes here (probe5d, probe5e); JGraph drops the event (ADR 0178's divergence).
        Assert.Equal("none|none", Run(
            "p = JGTest.Publisher(); l = addlistener(p, 'Custom', @(s, e) note('late'));\n"
            + "p.RaiseOnThreadPool(int32(2), int32(5)); busy(0.3); l.Enabled = false; pause(0.1); fprintf('%s|', logged());\n"
            + "l.Enabled = true; p.RaiseOnThreadPool(int32(2), int32(5)); busy(0.3); delete(l); pause(0.1); fprintf('%s', logged());"));
    }

    [Fact]
    public void AListenerHearsTheSourceItWasPutOnAndTheEventsDeclaredArguments()
    {
        Assert.Equal("1|JGTest.CustomArgs|2.5|tag", Run(
            "p = JGTest.Publisher(); l = addlistener(p, 'Custom', @(s, e) show(s == p, e));\n"
            + "p.RaiseCustom(2.5, 'tag');\n"
            + "function show(same, e)\nfprintf('%d|%s|%g|%s', same, class(e), e.Value, char(e.Tag));\nend\n"));
    }

    [Fact]
    public void AnEventWhoseDelegateIsNotSenderAndArgumentsIsRefused()
    {
        Assert.Equal("MATLAB:NET:UnsupportedDelegateType|MATLAB:class:invalidEvent|MATLAB:addlistener:invalidinput|", Run(
            "p = JGTest.Publisher();\n"
            + Id("addlistener(p, 'NonStandard', @(varargin) 1)") + Id("addlistener(p, 'StaticFired', @(s, e) 1)")
            + Id("addlistener(System.DateTime.Now, 'Fired', @(s, e) 1)")));
    }

    // --- delete -----------------------------------------------------------------------------------

    [Fact]
    public void DeletingANetObjectRaisesObjectBeingDestroyedWhileItIsStillValidThenEndsEveryName()
    {
        Assert.Equal("inside 1|0 0|MATLAB:class:InvalidHandle|1 pub", Run(
            "p = JGTest.Publisher(); q = p; box = System.Collections.ArrayList(); box.Add(p);\n"
            + "l = addlistener(p, 'ObjectBeingDestroyed', @(s, e) note(sprintf('inside %d', isvalid(s))));\n"
            + "delete(p); fprintf('%s|%d %d|', logged(), isvalid(p), isvalid(q));\n"
            + Id("q.Name")
            + "r = box.Item(0); fprintf('%d %s', isvalid(r), char(r.Name));"));
    }

    // --- delegates --------------------------------------------------------------------------------

    [Fact]
    public void ADelegateAsksTheHandleForTheReturnValueAndEachRefOrOutParameter()
    {
        Assert.Equal("3007|MATLAB:maxlhs|", Run(
            "fprintf('%g|', JGTest.Invoker.CallRefOut(JGTest.RefOut(@two), 2));\n"
            + Id("JGTest.Invoker.CallRefOut(JGTest.RefOut(@(a) a + 1), 2)")
            + "function [a, b] = two(a)\na = a + 1;\nb = 7;\nend\n"));
    }

    [Fact]
    public void AnErrorInsideADelegateComesOutOfTheScriptsNetCallAsItself()
    {
        Assert.Equal("MException|my:named|named failure", Run(
            "try, JGTest.Invoker.Apply(@failing, 1); catch e, fprintf('%s|%s|%s', class(e), e.identifier, e.message); end\n"
            + "function y = failing(x) %#ok<STOUT,INUSD>\nerror('my:named', 'named failure');\nend\n"));
    }

    [Fact]
    public void ADelegatesAnswerIsConvertedAsAPropertyWriteIsAndAnEmptyIsNoConversion()
    {
        Assert.Equal("3|MATLAB:class:RequireScalar|MATLAB:class:RequireNumeric|MATLAB:class:RequireReal|MATLAB:NET:NetConversion:UndefinedConversion|", Run(
            "fprintf('%g|', JGTest.Invoker.Apply(@(x) int8(3), 0));\n"
            + Id("JGTest.Invoker.Apply(@(x) [1 2], 0)") + Id("JGTest.Invoker.Apply(@(x) struct(), 0)")
            + Id("JGTest.Invoker.Apply(@(x) 2i, 0)") + Id("JGTest.Invoker.Apply(@(x) [], 0)")));
    }

    [Fact]
    public void DelegatesCombineInOrderAndAParenCallInvokesThem()
    {
        Assert.Equal("a b|b|a|42", Run(
            "a = System.Action(@() note('a')); b = System.Action(@() note('b'));\n"
            + "c = System.Delegate.Combine(a, b); JGTest.Invoker.Run(c); fprintf('%s|', logged());\n"
            + "r = System.Delegate.Remove(c, a); r(); fprintf('%s|', logged());\n"
            + "a(); fprintf('%s|', logged()); d = JGTest.Invoker.Doubler(); fprintf('%g', d(21));"));
    }

    [Fact]
    public void ADelegateInvokedOnAnotherThreadRunsAtTheNextPauseAndItsTaskCompletesThen()
    {
        Assert.Equal("0 none|1 x4|40", Run(
            "t = JGTest.Invoker.ApplyLater(@(v) later(v), 4); busy(0.3);\n"
            + "fprintf('%d %s|', t.IsCompleted, logged()); pause(0.2); fprintf('%d %s|', t.IsCompleted, logged());\n"
            + "fprintf('%g', t.Result);\n"
            + "function y = later(v)\nnote(sprintf('x%d', v));\ny = 10 * v;\nend\n"));
    }

    [Fact]
    public void ADelegateWaitingOnAScriptBlockedInNetGivesUpAsADeadlock()
    {
        TimeSpan saved = NetCallbackQueue.DeadlockTimeout;
        NetCallbackQueue.DeadlockTimeout = TimeSpan.FromMilliseconds(300);
        try
        {
            Assert.Equal("JGraph:NET:DelegateDeadlock|JGraph:NET:DelegateDeadlock|10", Run(
                "try, JGTest.Invoker.ApplyOnThread(@(x) 2 * x, 3); catch e, fprintf('%s|', e.identifier); end\n"
                + "t = JGTest.Invoker.ApplyLater(@(x) 2 * x, 3);\n"
                + "try, r = t.Result; catch e, fprintf('%s|', e.identifier); end\n"
                + "t = JGTest.Invoker.ApplyLater(@(x) 2 * x, 5); pause(0.2); fprintf('%g', t.Result);"));
        }
        finally
        {
            NetCallbackQueue.DeadlockTimeout = saved;
        }
    }

    [Fact]
    public void AnEmptyReachesAnyReferenceTypeParameterAsNull()
    {
        Assert.Equal("MATLAB:NET:CLRException:MethodInvoke|", Run(
            "u = JGTest.Unary(@(x) x);\n" + Id("u.EndInvoke([])")));
    }

    // --- MATLAB behaviour stage 5 needed -----------------------------------------------------------

    [Fact]
    public void AnAnonymousFunctionTakesFewerArgumentsThanItNamesUnlessTheBodyReadsTheMissingOne()
    {
        Assert.Equal("1|MATLAB:minrhs|MATLAB:TooManyInputs|0", Run(
            "f = @(a, b) a; fprintf('%g|', f(1));\n"
            + Id("g = @(a, b) a + b; g(1)") + Id("h = @() 1; h(1)")
            + "k = @(a, varargin) numel(varargin); fprintf('%d', k(1));"));
    }

    [Fact]
    public void EvalcCapturesAWarning()
    {
        Assert.Equal("[Warning: hi]", Run(
            "w = evalc('warning(''a:b'', ''hi'')'); fprintf('[%s]', strtrim(w));"));
    }

    [Fact]
    public void EventsAsAStatementListsANetTypesEventsAndAnswersThemOtherwise()
    {
        string listing = Run("p = JGTest.Publisher(); events(p)\nc = events('JGTest.Publisher'); fprintf('%d', numel(c));");
        Assert.StartsWith("Events for class JGTest.Publisher:\n\n    Fired\n    Custom", listing, StringComparison.Ordinal);
        Assert.EndsWith("5", listing, StringComparison.Ordinal);
    }
}
