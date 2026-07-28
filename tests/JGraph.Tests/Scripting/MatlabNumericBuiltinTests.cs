using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Bit manipulation, radix conversion, and the elementary numeric functions (M38): the answers here
/// are MATLAB's own, including the cases the accuracy-preserving functions exist for.
/// </summary>
[Collection("JG facade")]
public class MatlabNumericBuiltinTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabNumericBuiltinTests() => JG.Reset();

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
    public Task BitwiseOperations_MatchTheirTruthTables() => RunAsserting("""
        assert(bitand(12, 10) == 8);
        assert(bitor(12, 10) == 14);
        assert(bitxor(12, 10) == 6);
        assert(isequal(bitand([12 10], [10 12]), [8 8]));
        assert(bitand(255, 15, 'uint8') == 15);
        """);

    [Fact]
    public Task BitGetSetAndShift_CountFromTheLeastSignificantBit() => RunAsserting("""
        assert(bitget(5, 1) == 1);
        assert(bitget(5, 2) == 0);
        assert(bitget(5, 3) == 1);
        assert(bitset(4, 1) == 5);
        assert(bitset(5, 1, 0) == 4);
        assert(bitshift(1, 3) == 8);
        assert(bitshift(8, -3) == 1);
        assert(bitshift(1, 100) == 0);
        """);

    [Fact]
    public Task Bitcmp_ComplementsWithinTheAssumedWidth() => RunAsserting("""
        assert(bitcmp(0, 'uint8') == 255);
        assert(bitcmp(1, 'uint8') == 254);
        assert(bitcmp(0) == 2^53 - 1);
        """);

    [Fact]
    public Task BitOperations_RejectValuesTheyCannotRepresent() => RunAsserting("""
        threw = false;
        try
            bitand(-1, 2);
        catch
            threw = true;
        end
        assert(threw);
        """);

    [Fact]
    public Task RadixConversion_RoundTrips() => RunAsserting("""
        assert(strcmp(dec2bin(10), '1010'));
        assert(strcmp(dec2bin(10, 8), '00001010'));
        assert(strcmp(dec2hex(255), 'FF'));
        assert(strcmp(dec2base(255, 16), 'FF'));
        assert(bin2dec('1010') == 10);
        assert(hex2dec('FF') == 255);
        assert(hex2dec('ff') == 255);
        assert(base2dec('zz', 36) == 1295);
        assert(bin2dec(dec2bin(12345)) == 12345);
        """);

    [Fact]
    public Task AccuracyPreservingFunctions_BeatTheWrittenOutFormula() => RunAsserting("""
        assert(hypot(3, 4) == 5);
        assert(log2(1024) == 10);

        % log(1 + x) and exp(x) - 1 both round to nothing at this size; these two do not.
        tiny = 1e-18;
        assert(log1p(tiny) == tiny);
        assert(expm1(tiny) == tiny);
        assert(abs(expm1(1) - (exp(1) - 1)) < 1e-12);
        """);

    [Fact]
    public Task PowersAndRoots_KeepRealAnswersReal() => RunAsserting("""
        assert(pow2(10) == 1024);
        assert(pow2(3, 4) == 48);
        assert(nthroot(-8, 3) == -2);
        assert(nthroot(27, 3) == 3);
        assert(realsqrt(9) == 3);
        assert(abs(rad2deg(pi) - 180) < 1e-12);
        assert(abs(deg2rad(180) - pi) < 1e-15);

        threw = false;
        try
            realsqrt(-1);
        catch
            threw = true;
        end
        assert(threw);
        """);

    [Fact]
    public Task Complex_BuildsAValueFromItsParts() => RunAsserting("""
        z = complex(3, 4);
        assert(real(z) == 3);
        assert(imag(z) == 4);
        assert(abs(z) == 5);
        assert(imag(complex(2)) == 0);
        """);

    [Fact]
    public Task IntegerHelpers_AgreeWithTheirDefinitions() => RunAsserting("""
        assert(gcd(12, 18) == 6);
        assert(lcm(4, 6) == 12);
        assert(factorial(5) == 120);
        assert(factorial(0) == 1);
        assert(nchoosek(5, 2) == 10);
        assert(isequal(primes(10), [2 3 5 7]));
        assert(isequal(isprime([1 2 3 4]), [false true true false]));
        """);

    [Fact]
    public Task NchoosekOfAVector_ListsEveryCombination() => RunAsserting("""
        c = nchoosek([1 2 3], 2);
        assert(isequal(size(c), [3 2]));
        assert(isequal(c(1), [1 2]));
        assert(isequal(c(2), [1 3]));
        assert(isequal(c(3), [2 3]));
        """);

    [Fact]
    public Task DenseStorageAnswers_AreHonestAboutHavingNoSparseType() => RunAsserting("""
        A = [1 0 2; 0 0 3];
        assert(~issparse(A));
        assert(isequal(full(A), A));
        assert(nnz(A) == 3);
        assert(isequal(nonzeros(A), [1 2 3]));
        """);
}
