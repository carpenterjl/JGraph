using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The class a builtin answers in (M123).
/// </summary>
/// <remarks>
/// Every answer here was measured in R2024a rather than reasoned about, over the same hundred and
/// thirty expressions against single, int16, uint8, logical and double. That matters more than usual
/// for this family, because the rule is not one rule: a verb that <em>chooses</em> an element keeps
/// whatever class it was in, a verb that <em>computes</em> one keeps single and widens an integer,
/// and the two are not separable by looking at the names.
/// </remarks>
[Collection("JG facade")]
public class MatlabNumericClassM123Tests : IDisposable
{
    private RecordingScriptOutput _output = new();

    public MatlabNumericClassM123Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private string Run(string code)
    {
        _output = new RecordingScriptOutput();
        var context = new ScriptContext(_output, (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(
            code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Trim();
    }

    /// <summary>
    /// The verbs that select or rearrange: the class survives whatever it is.
    /// </summary>
    [Theory]
    [InlineData("sort(s)")]
    [InlineData("unique(s)")]
    [InlineData("fliplr(s)")]
    [InlineData("flipud(s.')")]
    [InlineData("reshape(s, 2, 2)")]
    [InlineData("permute(reshape(s, 2, 2), [2 1])")]
    [InlineData("circshift(s, 1)")]
    [InlineData("repmat(s, 1, 2)")]
    [InlineData("[s s]")]
    [InlineData("cat(2, s, s)")]
    [InlineData("triu(reshape(s, 2, 2))")]
    [InlineData("diag(s)")]
    [InlineData("max(s)")]
    [InlineData("min(s)")]
    [InlineData("median(s)")]
    [InlineData("mode(s)")]
    [InlineData("cummax(s)")]
    [InlineData("cumsum(s)")]
    [InlineData("diff(s)")]
    [InlineData("abs(s)")]
    [InlineData("sign(s)")]
    [InlineData("round(s)")]
    [InlineData("mod(s, 3)")]
    [InlineData("rem(s, 3)")]
    [InlineData("-s")]
    [InlineData("s.'")]
    [InlineData("s(2:3)")]
    [InlineData("sortrows(reshape(s, 2, 2))")]
    [InlineData("union(s, s)")]
    [InlineData("intersect(s, s)")]
    [InlineData("maxk(s, 2)")]
    [InlineData("movmax(s, 2)")]
    [InlineData("kron(s, s)")]
    public void ChoosingAnElementKeepsTheClassItWasIn(string call)
    {
        string classes = Run($$"""
            for cc = {'single', 'int16', 'uint8', 'double'}
              c = cc{1};
              s = cast([1 2 3 4], c);
              fprintf('%s ', class({{call}}));
            end
            """);

        Assert.Equal("single int16 uint8 double", classes);
    }

    /// <summary>
    /// The verbs that compute a new quantity: a single survives and an integer widens, because a sum
    /// can leave the range its terms lived in and MATLAB widens rather than saturate one.
    /// </summary>
    [Theory]
    [InlineData("sum(s)")]
    [InlineData("prod(s)")]
    [InlineData("mean(s)")]
    [InlineData("trapz(s)")]
    [InlineData("conv(s, s)")]
    [InlineData("movmean(s, 2)")]
    [InlineData("movsum(s, 2)")]
    [InlineData("filter(1, 1, s)")]
    [InlineData("rescale(s)")]
    public void ComputingANewQuantityKeepsSingleAndWidensAnInteger(string call)
    {
        string classes = Run($$"""
            for cc = {'single', 'int16', 'uint8', 'double'}
              c = cc{1};
              s = cast([1 2 3 4], c);
              fprintf('%s ', class({{call}}));
            end
            """);

        Assert.Equal("single double double double", classes);
    }

    /// <summary>
    /// A single survives the whole transcendental family, which is where the head-to-head report
    /// first noticed any of this.
    /// </summary>
    [Theory]
    [InlineData("sqrt")]
    [InlineData("exp")]
    [InlineData("log")]
    [InlineData("sin")]
    [InlineData("cos")]
    [InlineData("tanh")]
    [InlineData("erf")]
    [InlineData("gamma")]
    [InlineData("var")]
    [InlineData("std")]
    [InlineData("norm")]
    [InlineData("fft")]
    [InlineData("dct")]
    public void ASingleSurvivesAFunctionThatComputesInIt(string name) =>
        Assert.Equal("single", Run($"disp(class({name}(single([1 2 3 4]) ./ 10)));"));

    /// <summary>
    /// A mask stays a mask through the verbs that move it about, and becomes a double through the
    /// ones that do arithmetic on it. That split is MATLAB's and it is narrower than the numeric
    /// one: <c>sort</c> of a mask is a mask, <c>diff</c> of one is not.
    /// </summary>
    [Fact]
    public void AMaskStaysAMaskThroughTheVerbsThatOnlyMoveIt()
    {
        string classes = Run("""
            s = logical([1 0 1 0]);
            fprintf('%s %s %s %s %s %s | %s %s %s %s', ...
              class(sort(s)), class(reshape(s, 2, 2)), class(fliplr(s)), class(max(s)), ...
              class(repmat(s, 1, 2)), class(permute(reshape(s, 2, 2), [2 1])), ...
              class(diff(s)), class(cumsum(s)), class(abs(s)), class(sum(s)));
            """);

        Assert.Equal("logical logical logical logical logical logical | double double double double", classes);
    }

    /// <summary>
    /// A mask joined to a number is a double row, so the mask lane has to look at every subject and
    /// not merely find one.
    /// </summary>
    [Fact]
    public void AMaskMixedWithNumbersIsNotAMask()
    {
        string classes = Run("""
            s = logical([1 0 1 0]);
            fprintf('%s %s', class([s 2]), class(max(s, [1 2 3 4])));
            """);

        Assert.Equal("double double", classes);
    }

    /// <summary>
    /// The argument a verb takes its class from is the one holding its data. A digit count is not
    /// data, and reading it as data would have made <c>round(2.567, int32(1))</c> answer an int32 3.
    /// </summary>
    [Fact]
    public void AClassOnAnArgumentThatIsNotDataIsNotTheAnswersClass()
    {
        string answers = Run("""
            fprintf('%s %s %s %s', mat2str(round(2.567, int32(1))), class(round(2.567, int32(1))), ...
              mat2str(movmean([1 2 3 4], int32(2))), class(circshift([1 2 3 4], int32(1))));
            """);

        Assert.Equal("2.6 double [1 1.5 2.5 3.5] double", answers);
    }

    /// <summary>
    /// A running total saturates as it runs, not once at the end. MATLAB answers 100, 127, 27; a
    /// finished row stamped afterwards would answer 100, 127, 100 and look entirely reasonable.
    /// </summary>
    [Fact]
    public void ARunningTotalSaturatesAtEveryStep()
    {
        string sums = Run("""
            fprintf('%s %s %s', mat2str(cumsum(int8([100 100 -100])), 'class'), ...
              mat2str(cumsum(uint8([200 200 1])), 'class'), mat2str(cumprod(int8([100 100 0])), 'class'));
            """);

        Assert.Equal("int8([100 127 27]) uint8([200 255 255]) int8([100 127 0])", sums);
    }

    /// <summary>
    /// A value that lands between two of the class's values is rounded into it, which is why the
    /// answer has to go through the class and not merely be tagged with it.
    /// </summary>
    [Fact]
    public void AnAnswerBetweenTwoIntegersIsRoundedIntoTheClass()
    {
        string answers = Run("""
            fprintf('%s %s', mat2str(median(int16([1 2 3 4])), 'class'), mat2str(diff(uint8([1 5 3])), 'class'));
            """);

        // 2.5 rounds away from zero, and 3 - 5 saturates at the bottom of uint8 rather than wrapping.
        // mat2str names the class only when asked with 'class', as MATLAB's does (measured).
        Assert.Equal("int16(3) uint8([4 0])", answers);
    }

    /// <summary>A range takes its class from its ends, and a loop over one binds it.</summary>
    [Fact]
    public void ARangeAndTheLoopOverItCarryTheClassOfItsEnds()
    {
        string answers = Run("""
            a = int16(1):int16(4);
            b = single(1):0.5:single(3);
            for i = int16(1):int16(4)
            end
            s = 0;
            for k = int16(1):int16(200000)
              s = s + 1;
            end
            fprintf('%s %s %s %s %d', class(a), class(b), class(i), class(k), s);
            """);

        // 200000 does not fit in an int16, so the end of the range saturates to 32767 and the loop
        // runs that many times — which is MATLAB's answer, and the compiled loop's too.
        Assert.Equal("int16 single int16 int16 32767", answers);
    }

    /// <summary>
    /// The bit operations are defined on integers and refused for a single, so an integer class is
    /// carried through them and a single is not claimed.
    /// </summary>
    [Fact]
    public void TheBitOperationsCarryAnIntegerAndNotASingle()
    {
        string classes = Run("""
            fprintf('%s %s %s', class(bitand(int16(6), int16(3))), ...
              class(bitshift(uint8(1), 2)), class(idivide(int16(7), int16(2))));
            """);

        Assert.Equal("int16 uint8 int16", classes);
    }

    /// <summary>
    /// A complex answer keeps the class in both of its parts, which is the arm that did not exist:
    /// asking a packed complex array for its class threw rather than answering, so <c>fft</c> of a
    /// single stopped running altogether the moment anything asked.
    /// </summary>
    [Fact]
    public void AComplexAnswerCarriesTheClassInBothParts()
    {
        string answer = Run("""
            v = fft(single([1 2 3 4]));
            fprintf('%s %s %s', class(v), mat2str(real(v), 'class'), mat2str(imag(v), 'class'));
            """);

        Assert.Equal("single single([10 -2 -2 -2]) single([0 2 0 -2])", answer);
    }

    /// <summary>
    /// <c>single</c> is a real precision here and not only a label: the samples are rounded to float
    /// as they are written, and a sum of them is rounded again.
    /// </summary>
    [Fact]
    public void ASingleIsStoredAtSinglePrecision()
    {
        string answers = Run("""
            fprintf('%.17g %.17g', double(single(0.1)), double(sum(single([0.1 0.2 0.3]))));
            """);

        // Both are R2024a's own digits.
        Assert.Equal("0.10000000149011612 0.60000002384185791", answers);
    }

    /// <summary>
    /// The guard: a plain double array must come back a plain double, whatever it has been through.
    /// The whole retrofit is a wrapper on a hundred names and its cost has to be nothing at all for
    /// the arrays every script actually holds.
    /// </summary>
    [Fact]
    public void APlainDoubleIsStillAPlainDouble()
    {
        string classes = Run("""
            x = [1 2 3 4];
            fprintf('%s %s %s %s %s', class(sort(x)), class(sum(x)), class(reshape(x, 2, 2)), ...
              class(cumsum(x)), class(sqrt(x)));
            """);

        Assert.Equal("double double double double double", classes);
    }
}
