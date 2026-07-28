using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The special functions as scripts see them (M38): the identities that hold between them are what
/// the assertions check, so a wrong argument order or a swapped tail cannot pass.
/// </summary>
[Collection("JG facade")]
public class MatlabSpecialFunctionTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabSpecialFunctionTests() => JG.Reset();

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
    public Task ErrorFunctions_AgreeWithEachOther() => RunAsserting("""
        assert(abs(erf(1) - 0.842700792949715) < 1e-14);
        assert(abs(erf(0.5) + erfc(0.5) - 1) < 1e-15);
        assert(erf(0) == 0);
        assert(abs(erfc(-1) - (2 - erfc(1))) < 1e-15);

        % erfcx is erfc with the exponential taken out, which is the only way to see erfc(30).
        assert(erfc(30) == 0);
        assert(abs(erfcx(30) - 0.0187958888614167) < 1e-14);
        assert(abs(erfcx(1) - exp(1) * erfc(1)) < 1e-15);
        """);

    [Fact]
    public Task ErrorFunctionInverses_UndoTheirFunctions() => RunAsserting("""
        assert(abs(erf(erfinv(0.5)) - 0.5) < 1e-13);
        assert(abs(erfc(erfcinv(0.25)) - 0.25) < 1e-13);
        assert(abs(erfinv(0.5) - 0.476936276204470) < 1e-13);
        assert(erfinv(0) == 0);
        """);

    [Fact]
    public Task Gamma_ExtendsTheFactorial() => RunAsserting("""
        assert(abs(gamma(5) - 24) < 1e-12);
        assert(abs(gamma(0.5) - sqrt(pi)) < 1e-14);
        assert(abs(gammaln(100) - 359.134205369575) < 1e-10);

        % gamma(200) overflows a double; the point of gammaln is that its logarithm does not.
        assert(isinf(gamma(200)));
        assert(isfinite(gammaln(200)));
        assert(isequal(gamma([1 2 3 4]), [1 1 2 6]));
        """);

    [Fact]
    public Task IncompleteGamma_MatchesTheExponentialAndItsInverse() => RunAsserting("""
        assert(abs(gammainc(1, 1) - (1 - exp(-1))) < 1e-14);
        assert(abs(gammainc(3, 2) - (1 - exp(-3) * 4)) < 1e-14);
        assert(abs(gammainc(3, 2) + gammainc(3, 2, 'upper') - 1) < 1e-14);
        assert(abs(gammainc(gammaincinv(0.3, 2), 2) - 0.3) < 1e-9);
        """);

    [Fact]
    public Task IncompleteBeta_MatchesItsBinomialSum() => RunAsserting("""
        assert(abs(betainc(0.5, 2, 3) - 0.6875) < 1e-13);
        assert(abs(betainc(0.5, 1, 1) - 0.5) < 1e-14);
        assert(abs(beta(2, 3) - 1/12) < 1e-14);
        assert(abs(betaln(2, 3) - log(1/12)) < 1e-14);
        assert(abs(betainc(betaincinv(0.4, 2, 5), 2, 5) - 0.4) < 1e-10);
        assert(abs(betainc(0.5, 2, 3, 'upper') - 0.3125) < 1e-13);
        """);

    [Fact]
    public Task Psi_IsTheDigammaAndItsDerivatives() => RunAsserting("""
        assert(abs(psi(1) + 0.577215664901533) < 1e-13);
        assert(abs(psi(2) - psi(1) - 1) < 1e-13);
        assert(abs(psi(1, 1) - pi^2/6) < 1e-11);
        """);
}
