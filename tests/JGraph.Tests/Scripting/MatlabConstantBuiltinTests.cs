using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The numeric constants and limits (M37): Inf/NaN, the imaginary unit, newline, and the family
/// MATLAB writes as zero-argument functions — eps, realmax, realmin, flintmax, intmax, intmin.
/// The values here are what real MATLAB reports.
/// </summary>
[Collection("JG facade")]
public class MatlabConstantBuiltinTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabConstantBuiltinTests() => JG.Reset();

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
    public Task InfAndNaN_AnswerToBothSpellings() => RunAsserting("""
        assert(Inf == inf);
        assert(isinf(Inf));
        assert(isnan(NaN));
        assert(isnan(nan));
        assert(-Inf < 0);
        """);

    [Fact]
    public Task Eps_IsAValueBareAndASpacingWhenCalled() => RunAsserting("""
        x = eps;
        assert(x == 2^(-52));
        assert(eps(1) == 2^(-52));
        assert(eps(0) > 0);
        assert(eps(2) == 2 * eps(1));
        assert(eps('single') > eps('double'));
        """);

    [Fact]
    public Task RealAndFlintLimits_MatchTheDoubleFormat() => RunAsserting("""
        assert(realmax > 1e308);
        assert(realmin < 1e-307 && realmin > 0);
        assert(flintmax == 2^53);
        assert(flintmax('single') == 2^24);
        assert(realmin('single') > realmin);
        assert(realmax('single') < realmax);
        """);

    [Fact]
    public Task IntegerLimits_DefaultToInt32_AndNameOtherClasses() => RunAsserting("""
        assert(intmax == 2147483647);
        assert(intmin == -2147483648);
        assert(intmax('int8') == 127);
        assert(intmin('int8') == -128);
        assert(intmax('uint8') == 255);
        assert(intmin('uint16') == 0);
        """);

    [Fact]
    public Task ImaginaryUnit_AnswersToIAndJ_AndYieldsToALocal() => RunAsserting("""
        assert(i * i == -1);
        assert(j * j == -1);
        assert(real(3 + 4*i) == 3 && imag(3 + 4*i) == 4);

        % A loop variable named i shadows the constant, exactly as in MATLAB.
        total = 0;
        for i = 1:3
            total = total + i;
        end
        assert(total == 6);
        assert(i == 3);
        """);

    [Fact]
    public Task Newline_IsOneLineBreak() => RunAsserting("""
        assert(length(newline) == 1);
        assert(strcmp(sprintf('a%sb', newline), sprintf('a\nb')));
        """);

    [Fact]
    public async Task EpsAsABareStatement_EchoesItsValue()
    {
        await using IScriptSession session = NewSession();
        await session.ExecuteAsync("eps", sourceId: "", CancellationToken.None);
        Assert.Contains("2.2204", _output.NormalText, StringComparison.Ordinal);
    }
}
