using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M52: a scalar where a builtin asks for an array. Four helpers guarded against one reaching
/// <c>AsArray</c> and returning null, and the guard threw rather than promoting, so <c>sum(7)</c>,
/// <c>cumsum(5)</c>, <c>diff(5)</c> and every sibling were errors where MATLAB answers.
/// </summary>
/// <remarks>
/// The direction of the change is error to answer, which is why the same fix is safe in both dialects
/// and why no existing script can be reading differently now: a builtin that means something else by
/// a scalar — the elementwise <c>max(a, b)</c>, the image reductions — branches on the type long
/// before these helpers see it. The cases below are the representative spread across the helpers, not
/// every name that reaches them.
/// </remarks>
[Collection("JG facade")]
public class MatlabScalarArgumentTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabScalarArgumentTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private IScriptSession NewSession() => Assert
        .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
        .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure))));

    private async Task RunAsserting(string code)
    {
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    [Fact]
    public Task TheReductionsAnswerAScalarWithItself() => RunAsserting("""
        assert(sum(7) == 7);
        assert(prod(7) == 7);
        assert(mean(7) == 7);
        assert(median(7) == 7);
        assert(mode(7) == 7);
        assert(max(7) == 7);
        assert(min(7) == 7);
        assert(any(7));
        assert(all(7));
        assert(~any(0));
        """);

    [Fact]
    public Task TheShapeKeepingReductionsAnswerAScalarWithItself() => RunAsserting("""
        assert(cumsum(5) == 5);
        assert(cumprod(5) == 5);
        assert(isequal(sort(5), 5));
        """);

    /// <summary>
    /// The one place a scalar means something other than "a one-element array of it": there is no
    /// gap between one reading and the next, so differencing it leaves nothing.
    /// </summary>
    [Fact]
    public Task DifferencingOneValueLeavesNothing() => RunAsserting("""
        assert(isempty(diff(5)));
        assert(isequal(size(diff(5)), [1 0]));
        """);

    [Fact]
    public Task TheSpreadOfOneReadingIsZero() => RunAsserting("""
        assert(std(4) == 0);
        assert(var(4) == 0);
        assert(variance(4) == 0);
        assert(std(4, 1) == 0);
        """);

    [Fact]
    public Task AScalarPassesThroughTheShapeAndArrayBuiltins() => RunAsserting("""
        assert(isequal(fliplr(3), 3));
        assert(isequal(flipud(3), 3));
        assert(isequal(unique(3), 3));
        assert(isequal(cumsum(true), 1));
        assert(numel(7) == 1);
        assert(length(7) == 1);
        assert(isequal(size(7), [1 1]));
        """);

    [Fact]
    public Task ABoolCountsAsAOneElementArrayToo() => RunAsserting("""
        assert(sum(true) == 1);
        assert(sum(false) == 0);
        assert(mean(true) == 1);
        """);

    /// <summary>
    /// A scalar reaching a helper is now an array of one, but a value that is not a number at all
    /// still has to be named rather than silently becoming something.
    /// </summary>
    [Fact]
    public Task SomethingThatIsNotANumberIsStillRefusedByName() => RunAsserting("""
        ok = 0;
        try
            sum('abc');
        catch err
            ok = ok + 1;
        end
        try
            sum({1, 2});
        catch err
            ok = ok + 1;
        end
        assert(ok == 2);
        """);

    [Fact]
    public Task PlottingTwoScalarsDrawsOnePoint() => RunAsserting("""
        plot(2, 3);
        h = gca;
        assert(~isempty(h));
        """);

    /// <summary>
    /// M57 wave G: subscripting a scalar. A single number is a one-by-one array, so <c>x(1)</c> reads
    /// it back — found writing M57's stress script, where a chart verb that drew one thing handed back
    /// one handle and <c>h(1)</c>, the spelling that works when it drew several, could not read it.
    /// </summary>
    [Fact]
    public Task ASingleNumberIsAOneByOneArray() => RunAsserting("""
        x = 7;
        assert(x(1) == 7);
        assert(x(1, 1) == 7);
        assert(isequal(x([1 1]), [7 7]));
        assert(isequal(x(:), 7));
        assert(numel(x(1)) == 1);

        % A logical is one too, and the class of the reading is the class of the value.
        b = true;
        assert(b(1) == true);
        assert(strcmp(class(x(1)), 'double'));
        y = uint8(200);
        assert(strcmp(class(y(1)), 'uint8'));

        % Reaching past the one element is still out of bounds.
        ok = 0;
        try
            x(2);
        catch err
            ok = 1;
        end
        assert(ok == 1);
        """);

    /// <summary>
    /// The same reading through a handle, which is what found it: one plot, one handle, and the two
    /// spellings agreeing.
    /// </summary>
    [Fact]
    public Task ALoneHandleCanBeSubscripted() => RunAsserting("""
        figure(1);
        h = plot([1 2 3]);
        assert(numel(h) == 1);
        assert(strcmp(get(h(1), 'Type'), 'line'));
        assert(h(1) == h);
        """);
}
