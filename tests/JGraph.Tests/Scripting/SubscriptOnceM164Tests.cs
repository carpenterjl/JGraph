using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M15 (ADR 0164): a write evaluates each of its subscripts exactly once, whatever road it takes —
/// the brace fast path used to evaluate a subscript, decline it, and evaluate it again on the
/// general path (#50, #51). Every road is written with a subscript that counts its own calls.
/// </summary>
[Collection("JG facade")]
public class SubscriptOnceM164Tests : IDisposable
{
    private const string Helpers =
        "function k = tick()\n"
        + "global n\n"
        + "n = n + 1;\n"
        + "k = 2;\n"
        + "end\n"
        + "function k = tick_vector()\n"
        + "global n\n"
        + "n = n + 1;\n"
        + "k = [1 2];\n"
        + "end\n"
        + "function k = tick_mask()\n"
        + "global n\n"
        + "n = n + 1;\n"
        + "k = logical([1 0 1]);\n"
        + "end\n"
        + "function k = tick_pair()\n"
        + "global n\n"
        + "n = n + 1;\n"
        + "k = logical([1 0]);\n"
        + "end\n"
        + "function k = tick_one()\n"
        + "global n\n"
        + "n = n + 1;\n"
        + "k = 1;\n"
        + "end\n"
        + "function k = tick_boom()\n"
        + "global n\n"
        + "n = n + 1;\n"
        + "error('probe:boom', 'boom');\n"
        + "end\n"
        + "function k = tick_name()\n"
        + "global n\n"
        + "n = n + 1;\n"
        + "k = 'g';\n"
        + "end\n";

    private readonly RecordingScriptOutput _output = new();
    private readonly List<FigureModel> _figures = new();

    public SubscriptOnceM164Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private ScriptContext Context() => new(_output, (_, figure) => _figures.Add(figure), null);

    private async Task<JgsReplSession> RunAsync(string code)
    {
        JgsReplSession session = Assert.IsType<JgsReplSession>(
            Assert.IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine()).CreateSession(Context()));
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message);
        return session;
    }

    private static double Counted(JgsReplSession session)
    {
        Assert.True(session.Interpreter.GlobalWorkspace.TryGet("n", out JgsValue value), "global 'n' is not bound");
        return value.Type == JgsType.Array ? value.ElementAt(0).AsNumber : value.AsNumber;
    }

    /// <summary>Every write road, each with one counting subscript.</summary>
    public static TheoryData<string, string> Roads => new()
    {
        { "paren on a variable", "x = [1 2 3]; x(tick()) = 9;" },
        { "paren, two subscripts", "A = [1 2; 3 4]; A(tick(), 1) = 9;" },
        { "paren, three subscripts", "B = zeros(2, 2, 2); B(1, 1, tick()) = 9;" },
        { "paren, growth", "x = [1 2 3]; x(tick() + 5) = 9;" },
        { "paren, deletion", "x = [1 2 3]; x(tick()) = [];" },
        { "paren, a compound operator's read", "x = [1 2 3]; x(tick()) = x(1);" },
        { "brace on a variable", "c = {1, 2, 3}; c{tick()} = 9;" },
        { "brace, two subscripts", "C = {1, 2; 3, 4}; C{tick(), 1} = 9;" },
        { "brace, growth", "c = {1, 2, 3}; c{tick() + 5} = 9;" },
        { "field then paren", "s.f = [1 2 3]; s.f(tick()) = 9;" },
        { "field then paren, growth", "s.f = [1 2 3]; s.f(tick() + 5) = 9;" },
        { "brace then paren", "c = {[1 2 3]}; c{1}(tick()) = 9;" },
        { "paren then field", "st = struct('f', {1, 2, 3}); st(tick()).f = 9;" },
        { "two fields then paren", "s.a.b = [1 2 3]; s.a.b(tick()) = 9;" },
        { "a dynamic field name", "s.f = 1; s.(tick_name()) = 9;" },
        { "cell paren", "c = {1, 2, 3}; c(tick()) = {9};" },
        { "cell paren, deletion", "c = {1, 2, 3}; c(tick()) = [];" },
        { "a char row", "t = 'abc'; t(tick()) = 'z';" },
        { "a string array", "w = [\"a\" \"b\" \"c\"]; w(tick()) = \"z\";" },
        { "a struct array's element", "st = struct('f', {1, 2, 3}); st(tick()) = st(1);" },
        { "a struct array's element, deletion", "st = struct('f', {1, 2, 3}); st(tick()) = [];" },
        { "a dictionary key", "d = dictionary([1 2], [10 20]); d(tick()) = 9;" },
    };

    [Theory]
    [MemberData(nameof(Roads))]
    public async Task EveryWriteRoadEvaluatesItsSubscriptOnce(string road, string statement)
    {
        await using JgsReplSession session = await RunAsync("global n\nn = 0;\n" + statement + "\n" + Helpers);

        Assert.True(1 == Counted(session), $"{road}: the subscript ran {Counted(session)} times");
    }

    /// <summary>The subscript shapes the brace fast path used to decline and re-evaluate.</summary>
    public static TheoryData<string, string> Shapes => new()
    {
        { "a vector into a paren", "x = [1 2 3]; x(tick_vector()) = 9;" },
        { "a mask into a paren", "x = [1 2 3]; x(tick_mask()) = 9;" },
        { "a mask into a brace row (#50)", "C = {10; 20}; C{tick_pair(), 1} = 99;" },
        { "a mask into a brace column (#51)", "C = {10, 20}; C{1, tick_pair()} = 99;" },
        { "a range to end", "x = [1 2 3]; x(tick():end) = 9;" },
        { "a range from one", "c = {1, 2, 3}; c(tick_one():2) = {9};" },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task EverySubscriptShapeIsEvaluatedOnce(string shape, string statement)
    {
        await using JgsReplSession session = await RunAsync("global n\nn = 0;\n" + statement + "\n" + Helpers);

        Assert.True(1 == Counted(session), $"{shape}: the subscript ran {Counted(session)} times");
    }

    public static TheoryData<string, string> Throwing => new()
    {
        { "paren", "x = [1 2 3]; x(tick_boom()) = 9;" },
        { "brace", "c = {1, 2, 3}; c{tick_boom()} = 9;" },
        { "field then paren", "s.f = [1 2 3]; s.f(tick_boom()) = 9;" },
        { "paren then field", "st = struct('f', {1, 2, 3}); st(tick_boom()).f = 9;" },
    };

    [Theory]
    [MemberData(nameof(Throwing))]
    public async Task ASubscriptThatThrowsRanOnceAndTheTargetIsUntouched(string road, string statement)
    {
        await using JgsReplSession session = await RunAsync(
            "global n\nn = 0;\ntry\n" + statement + "\ncatch\nend\n" + Helpers);

        Assert.True(1 == Counted(session), $"{road}: the subscript ran {Counted(session)} times");
    }
}
