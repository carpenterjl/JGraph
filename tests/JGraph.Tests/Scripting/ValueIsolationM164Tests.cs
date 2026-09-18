using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// V3's scopes (ADR 0164, M5): C# that holds an evaluated wrapper while more script code runs holds
/// a counted share instead, and gives the count back when the scope ends, however it ends. And M8:
/// a builtin never mutates an argument.
/// </summary>
/// <remarks>
/// The parity fixtures (<c>value_isolation_scope</c>, <c>value_isolation_gen_scope_*</c>) hold the
/// answers; these hold the bookkeeping — that each scope kind leaves the holder count where it found
/// it after an error, a <c>return</c>, a <c>break</c> and a <c>continue</c>, that a share a builtin
/// handed back is not given back twice, and that an inert right-hand side opens no scope at all.
/// </remarks>
[Collection("JG facade")]
public class ValueIsolationM164Tests : IDisposable
{
    private const string Helpers =
        "function z = boom()\n"
        + "error('probe:boom', 'boom');\n"
        + "end\n"
        + "function z = touch()\n"
        + "global g\n"
        + "g(1) = 7;\n"
        + "z = 0;\n"
        + "end\n"
        + "function a = pass_first(a, ~)\n"
        + "end\n";

    private readonly RecordingScriptOutput _output = new();
    private readonly List<FigureModel> _figures = new();

    public ValueIsolationM164Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private ScriptContext Context() => new(_output, (_, figure) => _figures.Add(figure), null);

    private JgsReplSession NewSession() => Assert.IsType<JgsReplSession>(
        Assert.IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine()).CreateSession(Context()));

    private async Task<JgsReplSession> RunAsync(string code)
    {
        JgsReplSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message);
        return session;
    }

    // ---- every scope kind gives its count back, whatever ends it -------------------------------

    public static TheoryData<string, string> ScopesThatThrow => new()
    {
        { "operand", "r = g + boom();" },
        { "argument", "r = pass_first(g, boom());" },
        { "builtin argument", "r = plus(g, boom());" },
        { "bracket", "r = [g, boom()];" },
        { "cell literal", "r = {g, boom()};" },
        { "cell literal rows", "r = {g; boom()};" },
        { "index target", "r = g(boom());" },
        { "whole call", "r = arrayfun(@(x) boom(), g);" },
        { "loop source", "for e = g, boom(); end" },
    };

    [Theory]
    [MemberData(nameof(ScopesThatThrow))]
    public async Task AScopeGivesItsCountBackWhenItsCodeThrows(string kind, string statement)
    {
        await using JgsReplSession session = await RunAsync(
            "global g\ng = ones(1, 5);\ntry\n" + statement + "\ncatch\nend\n" + Helpers);
        JgsValue g = Global(session, "g");

        Assert.True(1 == JgsHolders.Of(g.ScopePayload), $"the {kind} scope left the count at {JgsHolders.Of(g.ScopePayload)}");
        Assert.True(session.Interpreter.ScopesOpened > 0, $"the {kind} scope never opened");
    }

    public static TheoryData<string, string> LoopsThatLeaveEarly => new()
    {
        { "break", "for e = g, break; end" },
        { "continue", "for e = g, continue; end" },
        { "return", "leave_early();" },
    };

    [Theory]
    [MemberData(nameof(LoopsThatLeaveEarly))]
    public async Task ALoopSourceGivesItsCountBackHoweverTheLoopEnds(string how, string statement)
    {
        await using JgsReplSession session = await RunAsync(
            "global g\ng = ones(1, 5);\n" + statement + "\n"
            + "function leave_early()\nglobal g\nfor e = g\nreturn;\nend\nend\n");

        Assert.True(1 == JgsHolders.Of(Global(session, "g").ScopePayload), $"a loop left by {how} kept its share");
    }

    [Fact]
    public async Task AScopeThatSawAWriteLeavesTheEntryAndItsViewApart()
    {
        await using JgsReplSession session = await RunAsync("global g\ng = ones(1, 5);\nr = g + touch();\n" + Helpers);
        JgsValue g = Global(session, "g");
        JgsValue r = session.Workspace.TryGet("r", out JgsValue value) ? value : throw new Xunit.Sdk.XunitException("r");

        Assert.Equal(7, g.ElementAt(0).AsNumber);
        Assert.Equal(1, r.ElementAt(0).AsNumber);      // #5: the sum read g as it was
        Assert.Equal(1, JgsHolders.Of(g.ScopePayload));     // the entry's detached copy is its own
        if (JgsPacking.Enabled)
        {
            // Boxed answers are never adopted (M109's elision is packed-only), so a boxed r is a
            // share of a dead temporary's payload and counts two — an over-count, never a leak.
            Assert.Equal(1, JgsHolders.Of(r.ScopePayload));
        }
    }

    [Fact]
    public async Task AShareABuiltinHandsBackIsKeptNotGivenBackTwice()
    {
        // deal hands its argument wrappers back as they are, so the whole-call share of g becomes
        // r's value; the scope keeps it (M5's release rule) and the count stays at least two.
        await using JgsReplSession session = await RunAsync(
            "global g\ng = ones(1, 5);\nr = feval(@deal, g);\ng(2) = 3;\n" + Helpers);
        JgsValue g = Global(session, "g");
        Assert.True(session.Workspace.TryGet("r", out JgsValue r));

        Assert.Equal(3, g.ElementAt(1).AsNumber);
        Assert.Equal(1, r.ElementAt(1).AsNumber);       // r kept what it was handed
    }

    // ---- inert expressions open no scope ---------------------------------------------------------

    [Fact]
    public async Task AnInertRightHandSideOpensNoScope()
    {
        await using JgsReplSession session = await RunAsync(
            "x = ones(1, 5); k = 2; z = ones(1, 5); c = {1, 2}; s.f = 3;\n"
            + "for i = 1:50\n"
            + "  y = x + k * z;\n"
            + "  y = x .* z(1:5) - x';\n"
            + "  y = [x, z; z, x];\n"
            + "  y = {x, c{1}, s.f};\n"
            + "  y = max(x, z(end));\n"
            + "  y = x(k) + x(end - 1);\n"
            + "end\n");

        Assert.Equal(0, session.Interpreter.ScopesOpened);
    }

    [Fact]
    public async Task ACallOnTheRightOpensOne()
    {
        await using JgsReplSession session = await RunAsync(
            "x = ones(1, 5);\ny = x + helper_value();\nfunction v = helper_value()\nv = 1;\nend\n");

        Assert.Equal(1, session.Interpreter.ScopesOpened);
    }

    // ---- M8: a builtin never mutates an argument --------------------------------------------------

    [Fact]
    public async Task InsertAndRemoveLeaveTheirArgument()
    {
        await using JgsReplSession session = await RunAsync(
            "d = dictionary([1 2], [10 20]); e = insert(d, 1, 99); f = remove(d, 2);\n"
            + "a = d(1); n = numEntries(d); b = e(1); m = numEntries(f);");

        Assert.Equal(10, Number(session, "a"));   // #20
        Assert.Equal(2, Number(session, "n"));    // #21
        Assert.Equal(99, Number(session, "b"));
        Assert.Equal(1, Number(session, "m"));
    }

    [Fact]
    public async Task AContainersMapIsStillChangedInPlace()
    {
        await using JgsReplSession session = await RunAsync(
            "m = containers.Map({'a', 'b'}, {1, 2}); remove(m, 'a'); n = m.Count;");

        Assert.Equal(1, Number(session, "n"));
    }

    // ---- M10: a write never reads what it is writing ---------------------------------------------

    [Theory]
    [InlineData("a([2 3 1]) = a;", "[3 1 2]")]                 // #31: a permutation of itself
    [InlineData("a(4:6) = a;", "[1 2 3 1 2 3]")]              // #40: growth from itself
    [InlineData("a(end+1:end+3) = a;", "[1 2 3 1 2 3]")]      // #41
    [InlineData("a(a) = a;", "[1 2 3]")]                      // #42: the subscript is the target too
    [InlineData("a(logical([0 0 0 1])) = 9;", "[1 2 3 9]")]   // #62: a write mask grows the target
    [InlineData("a(logical([0 1])) = 9;", "[1 9 3]")]         // #68: a short mask names what it names
    public async Task AWriteThatOverlapsItsTargetReadsWhatItRead(string statement, string expected)
    {
        await using JgsReplSession session = await RunAsync("a = [1 2 3];\n" + statement + "\ns = mat2str(a);");
        Assert.True(session.Workspace.TryGet("a", out JgsValue a));
        Assert.True(session.Workspace.TryGet("s", out JgsValue s));

        Assert.Equal(expected, s.AsString);
        Assert.Equal(1, JgsHolders.Of(a.ScopePayload)); // the write's share of the old payload was given back
    }

    // ---- M14: a refused write changes nothing ----------------------------------------------------

    [Theory]
    [InlineData("a = [1 2]; b = a; try, a(4:5) = [7 8 9]; catch, end", "[1 2]", "[1 2]")]          // #47
    [InlineData("a = [1 2; 3 4]; b = a; try, a(3:4, 1) = [7 8 9]; catch, end", "[1 2;3 4]", "[1 2;3 4]")] // #48
    [InlineData("a = {1, 2}; b = a; try, a(4:5) = {7, 8, 9}; catch, end", "2", "2")]              // #49
    public async Task ARefusedWriteLeavesTheTargetAndItsAliasWhole(string script, string expectedA, string expectedB)
    {
        await using JgsReplSession session = await RunAsync(
            script + "\nsa = show(a); sb = show(b);\n"
            + "function s = show(x)\nif iscell(x), s = num2str(numel(x)); else, s = mat2str(x); end\nend\n");

        Assert.Equal(expectedA, Text(session, "sa"));
        Assert.Equal(expectedB, Text(session, "sb"));
    }

    [Fact]
    public async Task AStructElementWithOtherFieldsIsRefusedBeforeAnythingChanges()
    {
        await using JgsReplSession session = await RunAsync(
            "st = struct('a', {1, 2}); t = struct('b', 3); id = '';\n"
            + "try, st(2) = t; catch e, id = e.identifier; end\n"
            + "f = isfield(st, 'b'); n = numel(st);\n"
            + "u = struct('a', {}); u(1) = struct('a', 7); m = numel(u);");

        Assert.Equal("MATLAB:heterogeneousStrucAssignment", Text(session, "id")); // #76, #86
        Assert.Equal(0, Number(session, "f"));
        Assert.Equal(2, Number(session, "n"));
        Assert.Equal(1, Number(session, "m"));                                    // #87: growth from empty
    }

    // ---- M11: a multiple assignment holds every output -------------------------------------------

    [Fact]
    public async Task AMultipleAssignmentBindsEveryOutputAsTheCallAnsweredIt()
    {
        await using JgsReplSession session = await RunAsync("v = [1 2 3]; [v(1), b] = deal(7, v);");
        Assert.True(session.Workspace.TryGet("v", out JgsValue v));
        Assert.True(session.Workspace.TryGet("b", out JgsValue b));

        Assert.Equal(7, v.ElementAt(0).AsNumber);
        Assert.Equal(1, b.ElementAt(0).AsNumber);          // #35: b is the v deal was handed
        Assert.Equal(1, JgsHolders.Of(v.ScopePayload));    // the shares taken for the outputs were given back
        Assert.Equal(1, JgsHolders.Of(b.ScopePayload));
    }

    [Fact]
    public async Task AMultipleAssignmentThatFailsPartWayGivesItsSharesBack()
    {
        await using JgsReplSession session = await RunAsync(
            "v = [1 2 3];\ntry\n[v(1), q(0)] = deal(7, v);\ncatch\nend\n");
        Assert.True(session.Workspace.TryGet("v", out JgsValue v));

        Assert.Equal(7, v.ElementAt(0).AsNumber);
        Assert.Equal(1, JgsHolders.Of(v.ScopePayload));
    }

    // ---- M16: order by the target's shape, and a cleared global ----------------------------------

    [Fact]
    public async Task AWriteRunsItsPartsInTheTargetShapesOrder()
    {
        await using JgsReplSession session = await RunAsync(
            "global L\nL = '';\nx = [1 2 3]; c = {1, 2, 3}; s.f = [1 2 3];\n"
            + "x(idx()) = rhs(); a = L; L = '';\n"
            + "c{idx()} = rhs(); b = L; L = '';\n"
            + "s.f(idx()) = rhs(); d = L;\n"
            + "function k = idx()\nglobal L\nL = [L 'i'];\nk = 2;\nend\n"
            + "function v = rhs()\nglobal L\nL = [L 'r'];\nv = 9;\nend\n");

        Assert.Equal("ri", Text(session, "a")); // a paren on a variable: right-hand side first
        Assert.Equal("ir", Text(session, "b")); // a brace: subscripts first (#151)
        Assert.Equal("ir", Text(session, "d")); // a deeper path: subscripts first
    }

    [Fact]
    public async Task AGlobalClearedByTheWritesOwnPartsIsRecreatedByTheWriteAndRefusedByEnd()
    {
        await using JgsReplSession session = await RunAsync(
            "global g\ng = [1 2 3];\ng(2) = wipe();\na = mat2str(g);\n"
            + "global h\nh = [1 2 3];\nmsg = '';\ntry\nh(end) = wipe_h();\ncatch e\nmsg = e.message;\nend\n"
            + "function v = wipe()\nglobal g\nclear global g\nv = 9;\nend\n"
            + "function v = wipe_h()\nglobal h\nclear global h\nv = 9;\nend\n");

        Assert.Equal("[0 9]", Text(session, "a"));                                 // #158
        Assert.Equal("Reference to a cleared variable h.", Text(session, "msg"));
    }

    // ---- the whole-call flag --------------------------------------------------------------------

    [Fact]
    public async Task EveryScriptRunningBuiltinIsRegisteredWithTheFlag()
    {
        // A name on the list that no handle can reach (innerintegral is the integrand dblquad builds
        // inside itself) is flagged all the same, because the flag is read from the list at mint time.
        await using JgsReplSession session = NewSession();
        int reached = 0;
        foreach (string name in JgsBuiltins.ScriptRunningBuiltins)
        {
            ScriptRunResult result = await session.ExecuteAsync($"h = @{name};", sourceId: "", CancellationToken.None);
            if (!result.Success)
            {
                Assert.True(new BuiltinFunction(name, (_, _, _) => JgsValue.Null).RunsScript);
                continue;
            }

            reached++;
            Assert.True(session.Workspace.TryGet("h", out JgsValue handle));
            Assert.True(
                handle.AsCallable is NamedHandle { Captured: BuiltinFunction { RunsScript: true } },
                $"'{name}' is on JgsBuiltins.ScriptRunningBuiltins but its builtin does not carry RunsScript");
        }

        Assert.True(reached >= JgsBuiltins.ScriptRunningBuiltins.Count - 2, $"only {reached} of the list are reachable by name");
    }

    private static JgsValue Global(JgsReplSession session, string name)
    {
        Assert.True(session.Interpreter.GlobalWorkspace.TryGet(name, out JgsValue value), $"global '{name}' is not bound");
        return value;
    }

    private static string Text(JgsReplSession session, string name)
    {
        Assert.True(session.Workspace.TryGet(name, out JgsValue value), $"'{name}' is not bound");
        return value.AsString;
    }

    private static double Number(JgsReplSession session, string name)
    {
        Assert.True(session.Workspace.TryGet(name, out JgsValue value), $"'{name}' is not bound");
        return value.Type == JgsType.Array ? value.ElementAt(0).AsNumber : value.AsNumber;
    }
}
