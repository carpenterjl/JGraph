using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// V6 (ADR 0167), twelfth sub-stage: events, listeners and observable properties (appendix A #106,
/// #108). An <c>events</c> block, <c>notify</c>, <c>addlistener</c>/<c>listener</c>, the event's
/// fields, <c>Enabled</c>/<c>Recursive</c>/<c>delete</c>/<c>isvalid</c> on a listener,
/// <c>ObjectBeingDestroyed</c>, and <c>properties (SetObservable)</c> raising <c>PreSet</c> and
/// <c>PostSet</c> once for every write, indexed or whole. A callback runs inside the statement that
/// raised it, after the statement's operands were read (M5).
/// </summary>
/// <remarks>
/// The parity fixture <c>events_listeners</c> holds R2025b's answers for the forms; these pin the
/// roads the sub-stage touched, one assertion a road, and the one place this build has its own
/// answer: a class file that does not parse is a load error the script can catch, in R2025b's
/// "Error: File: …" shape.
/// </remarks>
[Collection("JG facade")]
public class EventsListenersM167Tests : IDisposable
{
    /// <summary>The parity fixtures' helpers folder: <c>vlog</c>, <c>EventBox</c>, <c>EventPair</c>, <c>ObsPair</c>, <c>TickData</c>.</summary>
    private static readonly string Helpers = Path.Combine(AppContext.BaseDirectory, "MatlabParity", "fixtures", "helpers");

    private readonly RecordingScriptOutput _output = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jgraph-events-" + Guid.NewGuid().ToString("N"));

    public EventsListenersM167Tests() => JG.Reset();

    public void Dispose()
    {
        JG.Reset();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private ScriptRunResult Run(string code)
    {
        var context = new ScriptContext(_output, (_, _) => { }, Path.GetTempPath(), resolvePath: null, figureFiles: new TestFigureFiles());
        return JgsRunner.Run(code, context, default, sourceId: "", hook: null, JgsDialect.Matlab, searchFolders: [Helpers, _folder]);
    }

    private string RunAndRead(string code)
    {
        ScriptRunResult result = Run(code);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Trim().Replace("\r\n", "\n");
    }

    private string RunExpectingError(string code)
    {
        ScriptRunResult result = Run(code);
        Assert.False(result.Success);
        return result.Message ?? "";
    }

    /// <summary>Writes a class file into the test's own path folder.</summary>
    private void WriteClass(string name, string source)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, name + ".m"), source);
    }

    /// <summary>The fixtures' log: a global the helper <c>vlog</c> appends to.</summary>
    private const string Log = "global vlog_text; vlog_text = ''; ";

    // --- when a callback runs ---------------------------------------------------------------------

    [Fact]
    public void NotifyRunsItsListenersBeforeItReturns()
    {
        Assert.Equal("a;L;b;", RunAndRead(Log + """
            b = EventPair();
            lh = addlistener(b, 'Changed', @(~, ~) vlog('L'));
            vlog('a'); b.fire('Changed'); vlog('b');
            disp(vlog_text);
            """));
    }

    [Fact]
    public void ListenersFireNewestFirst()
    {
        Assert.Equal("two;one;", RunAndRead(Log + """
            b = EventPair();
            lh1 = addlistener(b, 'Changed', @(~, ~) vlog('one'));
            lh2 = addlistener(b, 'Changed', @(~, ~) vlog('two'));
            notify(b, 'Changed');
            disp(vlog_text);
            """));
    }

    [Fact]
    public void AListenerWritingAGlobalTheCallerHoldsLeavesTheCallersReadAlone()
    {
        // M5: g + fire_zero(b) reads g before the call, whose listener writes g(1) = 7.
        Assert.Equal("[1 2 3] [7 2 3]", RunAndRead("""
            global g_ev
            g_ev = [1 2 3];
            b = EventBox();
            lh = addlistener(b, 'Changed', @(~, ~) bump_ev());
            r = g_ev + fire_zero(b);
            fprintf('%s %s\n', mat2str(r), mat2str(g_ev));
            function bump_ev()
            global g_ev
            g_ev(1) = 7;
            end
            function z = fire_zero(b)
            b.fire();
            z = 0;
            end
            """));
    }

    [Fact]
    public void ACallbacksCaptureIsASnapshot()
    {
        Assert.Equal("[1 2 3]", RunAndRead("""
            global ev_out
            b = EventBox();
            v = [1 2 3];
            lh = addlistener(b, 'Changed', @(~, ~) store_ev(v));
            v(1) = 7;
            b.fire();
            disp(mat2str(ev_out));
            function store_ev(x)
            global ev_out
            ev_out = x;
            end
            """));
    }

    [Fact]
    public void TheEventCarriesItsNameAndItsSource()
    {
        Assert.Equal("event.EventData Changed 1 1", RunAndRead("""
            b = EventPair();
            lh = addlistener(b, 'Changed', @(src, evt) fprintf('%s %s %d %d\n', class(evt), evt.EventName, src == evt.Source, evt.Source == b));
            b.fire('Changed');
            """));
    }

    [Fact]
    public void CustomEventDataInheritsFromEventData()
    {
        Assert.Equal("5 Changed TickData 1 1", RunAndRead("""
            b = EventPair();
            lh = addlistener(b, 'Changed', @(~, evt) fprintf('%d %s %s %d %d\n', evt.n, evt.EventName, class(evt), isa(evt, 'event.EventData'), evt.Source == b));
            notify(b, 'Changed', TickData(5));
            """));
    }

    [Fact]
    public void AFailingCallbackIsAWarningAndTheOthersStillRun()
    {
        Assert.Equal("a;after;b;", RunAndRead(Log + """
            b = EventPair();
            lh = addlistener(b, 'Changed', @(~, ~) error('probe:boom', 'boom'));
            lh2 = addlistener(b, 'Changed', @(~, ~) vlog('after'));
            vlog('a'); b.fire('Changed'); vlog('b');
            disp(vlog_text);
            """));
        Assert.Contains(
            "Warning: Error occurred while executing the listener callback for event Changed defined for class EventPair:\nboom",
            _output.ErrorText.Replace("\r\n", "\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void AFailingCallbackIsTheLastWarning()
    {
        Assert.Equal("Error occurred while executing the listener callback for event Changed defined for class EventPair:\nboom", RunAndRead("""
            b = EventPair();
            lh = addlistener(b, 'Changed', @(~, ~) error('boom'));
            b.fire('Changed');
            disp(lastwarn);
            """));
    }

    [Fact]
    public void AZeroArgumentCallbackIsReportedNotFatal()
    {
        Assert.Equal("after", RunAndRead("""
            b = EventPair();
            lh = addlistener(b, 'Changed', @() disp('z'));
            b.fire('Changed');
            disp('after');
            """));
        Assert.Contains("Warning: Error occurred while executing the listener callback", _output.ErrorText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "1;")]
    [InlineData("lh.Recursive = true;", "3;3;3;")]
    public void AListenerReentersOnlyWhenRecursive(string setting, string expected)
    {
        Assert.Equal(expected, RunAndRead(Log + $$"""
            global ev_depth
            ev_depth = 0;
            b = EventPair();
            lh = addlistener(b, 'Changed', @(src, ~) nested(src));
            {{setting}}
            b.fire('Changed');
            disp(vlog_text);
            function nested(src)
            global ev_depth
            ev_depth = ev_depth + 1;
            if ev_depth < 3
                notify(src, 'Changed');
            end
            vlog(num2str(ev_depth));
            end
            """));
    }

    // --- the listener object -----------------------------------------------------------------------

    [Fact]
    public void AListenerIsAHandleWhoseEnabledEveryAliasSees()
    {
        Assert.Equal("[] logical 0", RunAndRead(Log + """
            b = EventPair();
            lh = addlistener(b, 'Changed', @(~, ~) vlog('D'));
            other = lh;
            other.Enabled = 0;
            b.fire('Changed');
            fprintf('[%s] %s %d\n', vlog_text, class(lh.Enabled), lh.Enabled);
            """));
    }

    [Fact]
    public void AListenerLivesWithItsSourceNotWithItsName()
    {
        Assert.Equal("A;", RunAndRead(Log + """
            b = EventBox();
            lh = addlistener(b, 'Changed', @(~, ~) vlog('A'));
            clear lh
            b.fire();
            disp(vlog_text);
            """));
    }

    [Fact]
    public void DeletingAListenerEndsItForEveryAlias()
    {
        Assert.Equal("0 0 []", RunAndRead(Log + """
            b = EventPair();
            lh = addlistener(b, 'Changed', @(~, ~) vlog('L'));
            other = lh;
            delete(lh);
            b.fire('Changed');
            fprintf('%d %d [%s]\n', isvalid(lh), isvalid(other), vlog_text);
            """));
    }

    [Theory]
    [InlineData("x = lh.Enabled;", "Invalid or deleted object.")]
    [InlineData("lh.Enabled = true;", "Invalid or deleted object.")]
    public void ADeletedListenerRefusesItsDots(string statement, string expected)
    {
        Assert.Contains(expected, RunExpectingError($$"""
            b = EventPair();
            lh = addlistener(b, 'Changed', @(~, ~) []);
            delete(lh);
            {{statement}}
            """), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("lh.Enabled = 'maybe';", "Error setting property 'Enabled' of class 'listener': Value must be a scalar.")]
    [InlineData("lh.Recursive = [true false];", "Error setting property 'Recursive' of class 'listener': Value must be a scalar.")]
    [InlineData("lh.Callback = 5;", "Error setting property 'Callback' of class 'listener': Value must be 'function_handle'.")]
    [InlineData("lh.Foo = 1;", "Unrecognized property 'Foo' for class 'event.listener'.")]
    [InlineData("x = lh.Foo;", "Unrecognized method, property, or field 'Foo' for class 'event.listener'.")]
    public void AListenersPropertiesAreCheckedInMatlabsWords(string statement, string expected)
    {
        Assert.Contains(expected, RunExpectingError($$"""
            b = EventPair();
            lh = addlistener(b, 'Changed', @(~, ~) []);
            {{statement}}
            """), StringComparison.Ordinal);
    }

    [Fact]
    public void TheListenerFunctionMakesTheSameObject()
    {
        // Its exact lifetime - ending with the last handle - is V10's (#107); until then it lives with the source.
        Assert.Equal("L; 0 event.listener", RunAndRead(Log + """
            b = EventPair();
            lh = listener(b, 'Changed', @(~, ~) vlog('L'));
            b.fire('Changed');
            delete(lh);
            b.fire('Changed');
            fprintf('%s %d %s\n', vlog_text, isvalid(lh), class(lh));
            """));
    }

    [Fact]
    public void AListenerAnswersThePredicates()
    {
        Assert.Equal("1 1 1 0 0 1", RunAndRead("""
            b = EventPair();
            lh = addlistener(b, 'Changed', @(~, ~) []);
            fprintf('%d %d %d %d %d %d\n', isa(lh, 'handle'), isa(lh, 'event.listener'), isobject(lh), isstruct(lh), ishandle(lh), isvalid(lh));
            """));
    }

    // --- delete and ObjectBeingDestroyed ---------------------------------------------------------------

    [Theory]
    [InlineData("delete(b);")]
    [InlineData("b.delete();")]
    public void DeleteRaisesObjectBeingDestroyedAfterTheObjectIsInvalid(string statement)
    {
        Assert.Equal("a;ObjectBeingDestroyed/0;b; 0 1", RunAndRead(Log + $$"""
            b = EventPair();
            lh = addlistener(b, 'ObjectBeingDestroyed', @(src, evt) vlog([evt.EventName '/' num2str(isvalid(src))]));
            vlog('a');
            {{statement}}
            vlog('b');
            fprintf('%s %d %d\n', vlog_text, isvalid(b), isvalid(lh));
            """));
    }

    [Fact]
    public void AClassWithItsOwnDestructorRaisesObjectBeingDestroyedAfterIt()
    {
        WriteClass("Closing", """
            classdef Closing < handle
                methods
                    function delete(obj)
                        vlog('dtor');
                    end
                end
            end
            """);
        Assert.Equal("dtor;gone/0;", RunAndRead(Log + """
            c = Closing();
            lh = addlistener(c, 'ObjectBeingDestroyed', @(src, ~) vlog(['gone/' num2str(isvalid(src))]));
            delete(c);
            disp(vlog_text);
            """));
    }

    [Fact]
    public void NotifyMayNotRaiseObjectBeingDestroyed()
    {
        Assert.Contains("Cannot notify listeners of event 'ObjectBeingDestroyed' in class 'handle'.", RunExpectingError("""
            b = EventPair();
            notify(b, 'ObjectBeingDestroyed');
            """), StringComparison.Ordinal);
    }

    [Fact]
    public void DeletingTheSourceLeavesTheListenerValidWithNothingToHear()
    {
        Assert.Equal("1 0", RunAndRead("""
            b = EventPair();
            lh = addlistener(b, 'Changed', @(~, ~) []);
            delete(b);
            fprintf('%d %d\n', isvalid(lh), isvalid(b));
            """));
    }

    // --- refusals ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("notify(b, 'Nope');", "Event 'Nope' is not defined for class 'EventPair'.")]
    [InlineData("addlistener(b, 'changed', @(~, ~) []);", "Event 'changed' is not defined for class 'EventPair'.")]
    [InlineData("notify(b, 'Changed', 5);", "Invalid input argument for function 'notify'.")]
    [InlineData("notify(b);", "Not enough input arguments.")]
    [InlineData("addlistener(b, 'Changed', 'disp(1)');", "Invalid input argument for function 'addlistener'.")]
    [InlineData("addlistener(struct('a', 1), 'a', @(~, ~) []);", "First argument provided is not valid for addlistener. (Check its type or validity)")]
    [InlineData("addlistener(5, 'a', @(~, ~) []);", "Double input must be an HG handle")]
    [InlineData("delete(b); notify(b, 'Changed');", "Invalid or deleted object.")]
    [InlineData("delete(b); addlistener(b, 'Changed', @(~, ~) []);", "Invalid or deleted object.")]
    public void EventsAndListenersRefuseInMatlabsWords(string statement, string expected)
    {
        Assert.Contains(expected, RunExpectingError("b = EventPair();\n" + statement), StringComparison.Ordinal);
    }

    [Fact]
    public void AValueClassMayNotDeclareEventsAndTheRefusalIsCatchable()
    {
        WriteClass("ValEv", "classdef ValEv\n events\n Changed\n end\nend\n");
        Assert.Equal("caught The class 'ValEv' may not define events because only subclasses of handle may define events.", RunAndRead("""
            try
                v = ValEv();
                disp('built');
            catch err
                disp(['caught ' strrep(err.message, newline, ' ')]);
            end
            """).Split('\n')[0]);
    }

    [Fact]
    public void AClassFileThatDoesNotParseIsALoadErrorTheScriptCanCatch()
    {
        WriteClass("BadSyntax", "classdef BadSyntax < handle\n properties\n x = [1 2\n end\nend\n");
        string shown = RunAndRead("""
            try
                b = BadSyntax();
                disp('built');
            catch err
                disp(['caught ' strrep(err.message, newline, ' ')]);
            end
            disp('after');
            """);
        Assert.StartsWith("caught Error: File: ", shown, StringComparison.Ordinal);
        Assert.Contains("BadSyntax.m Line: ", shown, StringComparison.Ordinal);
        Assert.EndsWith("after", shown, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEventsBlockAttributeIsRefusedByName()
    {
        WriteClass("Guarded", "classdef Guarded < handle\n events (ListenAccess = private)\n Changed\n end\nend\n");
        Assert.Contains("the 'events' attribute 'ListenAccess' is not supported", RunExpectingError("g = Guarded();"), StringComparison.Ordinal);
    }

    [Fact]
    public void EventsListsTheClassEventsWithObjectBeingDestroyedLast()
    {
        Assert.Equal("Changed,Other,ObjectBeingDestroyed [3 1] [0 1]", RunAndRead("""
            b = EventPair();
            c = events(b);
            fprintf('%s %s %s\n', strjoin(c', ','), mat2str(size(c)), mat2str(size(events(struct('a', 1)))));
            """));
    }

    // --- observable properties ---------------------------------------------------------------------

    [Fact]
    public void PreSetSeesTheOldValueAndPostSetTheNew()
    {
        Assert.Equal("pre:[1 2 3];post:9;", RunAndRead(Log + """
            o = ObsPair();
            lh1 = addlistener(o, 'a', 'PreSet', @(~, evt) vlog(['pre:' mat2str(evt.AffectedObject.a)]));
            lh2 = addlistener(o, 'a', 'PostSet', @(~, evt) vlog(['post:' mat2str(evt.AffectedObject.a)]));
            o.a = 9;
            disp(vlog_text);
            """));
    }

    [Fact]
    public void ThePropertyEventCarriesTheMetadataAndTheObject()
    {
        Assert.Equal("event.PropertyEvent PostSet a matlab.metadata.Property matlab.metadata.Property 1", RunAndRead("""
            o = ObsPair();
            lh = addlistener(o, 'a', 'PostSet', @(src, evt) fprintf('%s %s %s %s %s %d\n', class(evt), evt.EventName, evt.Source.Name, class(evt.Source), class(src), evt.AffectedObject == o));
            o.a = 1;
            """));
    }

    [Theory]
    [InlineData("o.a(2) = 9;", "P; [1 9 3]")]
    [InlineData("o.a(end + 1) = 4;", "P; [1 2 3 4]")]
    [InlineData("o.a(5) = 1;", "P; [1 2 3 0 1]")]
    [InlineData("o.a = o.a;", "P; [1 2 3]")]
    [InlineData("o.setA(7);", "P; 7")]
    [InlineData("p = o; p.a = [7 8];", "P; [7 8]")]
    public void EveryWriteToAnObservablePropertyRaisesOnePostSet(string statement, string expected)
    {
        Assert.Equal(expected, RunAndRead(Log + $$"""
            o = ObsPair();
            lh = addlistener(o, 'a', 'PostSet', @(~, ~) vlog('P'));
            {{statement}}
            fprintf('%s %s\n', vlog_text, mat2str(o.a));
            """));
    }

    [Fact]
    public void AnIndexedWriteLeavesEveryAliasAsItWasUntilTheSet()
    {
        // PreSet reads the property while the slot is being written: the object is untouched until the set.
        Assert.Equal("pre:[1 2 3];post:[1 9 3]; [1 9 3]", RunAndRead(Log + """
            o = ObsPair();
            lh1 = addlistener(o, 'a', 'PreSet', @(~, evt) vlog(['pre:' mat2str(evt.AffectedObject.a)]));
            lh2 = addlistener(o, 'a', 'PostSet', @(~, evt) vlog(['post:' mat2str(evt.AffectedObject.a)]));
            o.a(2) = 9;
            fprintf('%s %s\n', vlog_text, mat2str(o.a));
            """));
    }

    [Theory]
    [InlineData("o.s.v(2) = 5;", "[1 5 3];")]
    [InlineData("o.s.v = [8 9];", "[8 9];")]
    [InlineData("o.s.w = 1;", "[1 2 3];")]
    public void AWriteInsideAStructHeldByAnObservablePropertyRaisesPostSet(string statement, string expected)
    {
        Assert.Equal(expected, RunAndRead(Log + $$"""
            o = ObsPair();
            lh = addlistener(o, 's', 'PostSet', @(~, evt) vlog(mat2str(evt.AffectedObject.s.v)));
            {{statement}}
            disp(vlog_text);
            """));
    }

    [Fact]
    public void AnObjectHeldInAStructRaisesPostSetThroughThePath()
    {
        Assert.Equal("P; [1 9 3]", RunAndRead(Log + """
            holder.o = ObsPair();
            lh = addlistener(holder.o, 'a', 'PostSet', @(~, ~) vlog('P'));
            holder.o.a(2) = 9;
            fprintf('%s %s\n', vlog_text, mat2str(holder.o.a));
            """));
    }

    [Fact]
    public void APropertyNobodyListensToKeepsTheOrdinaryRoad()
    {
        Assert.Equal("[1 9 3] [1 2 3]", RunAndRead("""
            o = ObsPair();
            v = o.a;
            o.a(2) = 9;
            fprintf('%s %s\n', mat2str(o.a), mat2str(v));
            """));
    }

    [Fact]
    public void AValueReadOutOfAnObservablePropertyStaysItsOwn()
    {
        Assert.Equal("[1 2 3] [5 2 3]", RunAndRead("""
            o = ObsBox();
            v = o.data;
            o.data(1) = 5;
            fprintf('%s %s\n', mat2str(v), mat2str(o.data));
            """));
    }

    [Fact]
    public void AFailingPreSetIsAWarningAndTheSetGoesAhead()
    {
        Assert.Equal("after; 9", RunAndRead(Log + """
            o = ObsPair();
            lh = addlistener(o, 'a', 'PreSet', @(~, ~) error('veto'));
            o.a = 9;
            vlog('after');
            fprintf('%s %s\n', vlog_text, mat2str(o.a));
            """));
        Assert.Contains("Warning: Error occurred while executing the listener callback for the ObsPair class a property PreSet event:\nveto",
            _output.ErrorText.Replace("\r\n", "\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void APostSetWritingItsOwnPropertyIsNotReentered()
    {
        Assert.Equal("1; 0", RunAndRead(Log + """
            global ev_depth
            ev_depth = 0;
            o = ObsPair();
            lh = addlistener(o, 'a', 'PostSet', @(~, evt) guarded(evt));
            o.a = 1;
            fprintf('%s %s\n', vlog_text, mat2str(o.a));
            function guarded(evt)
            global ev_depth
            ev_depth = ev_depth + 1;
            if ev_depth < 3
                evt.AffectedObject.a = 0;
            end
            vlog(num2str(ev_depth));
            end
            """));
    }

    [Fact]
    public void ACellOfPropertiesMakesOneListenerOnEach()
    {
        Assert.Equal("a;b; event.proplistener 2", RunAndRead(Log + """
            o = ObsPair();
            lh = addlistener(o, {'a', 'b'}, 'PostSet', @(~, evt) vlog(evt.Source.Name));
            o.a = 1; o.b = 2; o.plain = 3;
            fprintf('%s %s %d\n', vlog_text, class(lh), numel(lh.Source));
            """));
    }

    [Theory]
    [InlineData("addlistener(o, 'plain', 'PostSet', @(~, ~) []);", "While adding a PostSet listener, property 'plain' in class 'ObsPair' is not defined to be SetObservable.")]
    [InlineData("addlistener(o, 'nope', 'PostSet', @(~, ~) []);", "The name 'nope' is not an accessible property for an instance of class 'ObsPair'.")]
    [InlineData("addlistener(o, 'a', 'PostFoo', @(~, ~) []);", "Event 'PostFoo' is not defined for class 'ObsPair'.")]
    public void PropertyListenersRefuseInMatlabsWords(string statement, string expected)
    {
        Assert.Contains(expected, RunExpectingError("o = ObsPair();\n" + statement), StringComparison.Ordinal);
    }

    [Fact]
    public void AValueClassMayBeSetObservableButNotListenedTo()
    {
        WriteClass("ValObs", "classdef ValObs\n properties (SetObservable)\n x = 1\n end\nend\n");
        Assert.Equal("1", RunAndRead("v = ValObs(); disp(v.x);"));
        Assert.Contains("First argument provided is not valid for addlistener.", RunExpectingError("""
            v = ValObs();
            addlistener(v, 'x', 'PostSet', @(~, ~) []);
            """), StringComparison.Ordinal);
    }

    [Fact]
    public void APropertyListenerCanBeDisabledAndDeleted()
    {
        Assert.Equal("2;3; 0", RunAndRead(Log + """
            o = ObsPair();
            lh = addlistener(o, 'a', 'PostSet', @(~, evt) vlog(mat2str(evt.AffectedObject.a)));
            lh.Enabled = false;
            o.a = 1;
            lh.Enabled = true;
            o.a = 2;
            o.a = 3;
            delete(lh);
            o.a = 4;
            fprintf('%s %d\n', vlog_text, isvalid(lh));
            """));
    }

    [Fact]
    public void AnInlineClassdefMayDeclareEvents()
    {
        Assert.Equal("hi;", RunAndRead(Log + """
            classdef Inline < handle
                events
                    Ping
                end
            end
            i = Inline();
            lh = addlistener(i, 'Ping', @(~, ~) vlog('hi'));
            notify(i, 'Ping');
            disp(vlog_text);
            """));
    }
}
