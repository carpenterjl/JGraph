using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The Bessel and Airy builtins as scripts see them (M39). The kernel's accuracy is pinned in
/// <c>BesselFunctionsTests</c>; what these check is the calling convention — argument order, the
/// optional kind and scale arguments, and broadcasting over arrays.
/// </summary>
[Collection("JG facade")]
public class MatlabBesselBuiltinTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabBesselBuiltinTests() => JG.Reset();

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
    public Task TheOrderComesFirst() => RunAsserting("""
        % besselj(nu, x), not besselj(x, nu) — the two disagree everywhere off the diagonal.
        assert(abs(besselj(0, 1) - 0.765197686557966) < 1e-14);
        assert(abs(besselj(1, 0) - 0) < 1e-15);
        assert(abs(bessely(0, 1) - 0.088256964215677) < 1e-14);
        assert(abs(besseli(0, 1) - 1.266065877752008) < 1e-14);
        assert(abs(besselk(0, 1) - 0.421024438240708) < 1e-14);
        """);

    [Fact]
    public Task ItBroadcastsOverEitherArgument() => RunAsserting("""
        orders = besselj([0 1 2], 3);
        assert(length(orders) == 3);
        assert(abs(orders(1) - besselj(0, 3)) < 1e-15);
        assert(abs(orders(3) - besselj(2, 3)) < 1e-15);

        points = besselj(0, [1 2 3]);
        assert(abs(points(2) - besselj(0, 2)) < 1e-15);

        % Both at once, elementwise rather than outer product.
        pairs = besselj([0 1], [1 2]);
        assert(abs(pairs(1) - besselj(0, 1)) < 1e-15);
        assert(abs(pairs(2) - besselj(1, 2)) < 1e-15);
        """);

    [Fact]
    public Task TheScaleFlagIsTheThirdArgument() => RunAsserting("""
        assert(abs(besselk(0, 3, 1) - besselk(0, 3) * exp(3)) < 1e-14);
        assert(abs(besseli(0, 3, 1) - besseli(0, 3) * exp(-3)) < 1e-14);

        % And it is what keeps an answer at all where the plain call has none left.
        assert(besselk(0, 800) == 0);
        assert(besselk(0, 800, 1) > 0.04 && besselk(0, 800, 1) < 0.045);
        assert(isinf(besseli(0, 800)));
        assert(besseli(0, 800, 1) < 0.02);
        """);

    [Fact]
    public Task HankelDefaultsToTheFirstKind() => RunAsserting("""
        h = besselh(0, 1);
        assert(abs(real(h) - besselj(0, 1)) < 1e-15);
        assert(abs(imag(h) - bessely(0, 1)) < 1e-15);

        % The kind, when given, is the middle argument.
        h2 = besselh(0, 2, 1);
        assert(abs(imag(h2) + bessely(0, 1)) < 1e-15);
        """);

    [Fact]
    public Task AiryTakesTheKindFirstAndDefaultsToAi() => RunAsserting("""
        assert(abs(airy(0) - 0.355028053887817) < 1e-14);
        assert(abs(airy(0, 0) - 0.355028053887817) < 1e-14);
        assert(abs(airy(1, 0) + 0.258819403792807) < 1e-14);
        assert(abs(airy(2, 0) - 0.614926627446001) < 1e-14);
        assert(abs(airy(3, 0) - 0.448288357353826) < 1e-14);

        % Ai(1) and Bi(1), and the scaled forms past where the plain ones stop existing.
        assert(abs(airy(0, 1) - 0.135292416312881) < 1e-14);
        assert(abs(airy(2, 1) - 1.207423594952871) < 1e-13);
        % Scaled Ai(x) → 1/(2√π·x^(1/4)), which is 0.0750 at x = 200.
        assert(airy(0, 200) == 0);
        assert(abs(airy(0, 200, 1) - 0.075) < 0.001);

        % airy maps over an array the way every other element-wise builtin does.
        v = airy(0, [0 1]);
        assert(abs(v(1) - airy(0, 0)) < 1e-15);
        """);

    [Fact]
    public Task AnAnswerThatWouldBeComplexIsRefused() => RunAsserting("""
        % J_n(-x) is real for whole n, so this one is answered.
        assert(abs(besselj(1, -2) + besselj(1, 2)) < 1e-15);

        caught = 0;
        try
            besselk(0, -1);
        catch err
            caught = 1;
        end
        assert(caught == 1);
        """);
}
