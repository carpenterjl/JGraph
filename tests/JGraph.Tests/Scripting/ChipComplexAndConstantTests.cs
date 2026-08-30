using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Numerics;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Three places where a value MATLAB accepts was refused here, closed together because all three
/// were found by the same M106/M107 parity harnesses: a complex scalar could not be subscripted,
/// <c>Inf</c> and <c>NaN</c> could not be handed a size, and <c>double</c> would not take a complex
/// argument.
/// </summary>
/// <remarks>
/// <para>
/// Every expectation here was read off MATLAB R2024a on this machine before it was written down.
/// The assertions run inside the scripts, so what is pinned is MATLAB's answer rather than JGraph's
/// display format, and the three cases that decide a shape rather than a value are run again with
/// packing forced on and forced off, because the two interpreter lanes have been caught disagreeing
/// about exactly this kind of promotion before.
/// </para>
/// <para>
/// Two neighbouring refusals are pinned as they stand rather than fixed, and the tests say so where
/// they sit: MATLAB does mint a complex integer (<c>int32(1+2i)</c> is <c>1+2i</c>) but forbids
/// arithmetic on one, and MATLAB's <c>Inf(2,'int32')</c> is an error in both systems.
/// </para>
/// </remarks>
[Collection("JG facade")]
public class ChipComplexAndConstantTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public ChipComplexAndConstantTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private IScriptSession NewSession() => Assert
        .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
        .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure))));

    private static Task<ScriptRunResult> Run(IScriptSession session, string code) =>
        session.ExecuteAsync(code, sourceId: "", CancellationToken.None);

    private async Task Asserts(string code)
    {
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await Run(session, code);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    private async Task Refuses(string code, string fragment)
    {
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await Run(session, code);
        Assert.False(result.Success, "the script was expected to fail");
        Assert.Contains(fragment, (result.Message ?? string.Empty) + _output.ErrorText, StringComparison.Ordinal);
    }

    // --- the two lanes ---------------------------------------------------------------------------

    private static (string[] Output, bool Success, string? Message) RunWith(bool packed, string code)
    {
        bool previous = JgsPacking.Enabled;
        JgsPacking.Enabled = packed;
        try
        {
            JG.Reset();
            var output = new RecordingScriptOutput();
            var figures = new List<FigureModel>();
            var context = new ScriptContext(output, (_, figure) => figures.Add(figure), null);
            ScriptRunResult result = JgsRunner.Run(
                code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
            return (output.Normal.ToArray(), result.Success, result.Message);
        }
        finally
        {
            JgsPacking.Enabled = previous;
        }
    }

    /// <summary>Runs a script with packing forced on and forced off; both must print the same.</summary>
    private static void AssertParity(string code)
    {
        (string[] packedOut, bool packedOk, string? packedMessage) = RunWith(packed: true, code);
        (string[] boxedOut, bool boxedOk, string? boxedMessage) = RunWith(packed: false, code);

        Assert.True(boxedOk, boxedMessage);
        Assert.True(packedOk, packedMessage);
        Assert.Equal(boxedOut, packedOut);
    }

    // --- a complex scalar is a one-by-one array, and subscripts read out of it --------------------

    [Fact]
    public Task AComplexScalarAnswersEverySubscriptThatReadsItsOneElement() => Asserts("""
        z = 1 + 2i;
        assert(isequal(size(z), [1 1]));
        assert(numel(z) == 1);
        assert(z(1) == 1 + 2i);
        assert(z(1,1) == 1 + 2i);
        assert(z(end) == 1 + 2i);
        assert(isequal(size(z(:)), [1 1]));
        assert(z(:) == 1 + 2i);
        """);

    [Fact]
    public Task RepeatingTheOneIndexOfAComplexScalarRepeatsTheValue() => Asserts("""
        z = 1 + 2i;
        r = z([1 1 1]);
        assert(isequal(size(r), [1 3]));
        assert(all(r == 1 + 2i));
        """);

    [Fact]
    public Task ReadingAComplexScalarKeepsItComplexAndDouble() => Asserts("""
        z = 3 + 4i;
        assert(strcmp(class(z(1)), 'double'));
        assert(~isreal(z(1)));
        assert(real(z(1)) == 3);
        assert(imag(z(1)) == 4);
        assert(abs(z(:)) == 5);
        """);

    [Fact]
    public Task ASecondElementOfAComplexScalarIsStillPastTheEnd() =>
        Refuses("z = 1 + 2i; disp(z(2));", "out of range");

    [Fact]
    public Task AOneByOneComplexAnsweredByAVerbCanBeFlattenedLikeAnythingElse() => Asserts("""
        % The reason the gap mattered: roots of a first-degree complex polynomial is a one-by-one
        % complex, and before this the very next line could not subscript what it had just been
        % handed. MATLAB answers -0 - 1i for roots([1 1i]).
        r = roots([1 1i]);
        assert(isequal(size(r), [1 1]));
        assert(abs(r(1) - (-1i)) < 1e-12);
        assert(abs(r(:) - (-1i)) < 1e-12);
        """);

    [Fact]
    public void IndexingAComplexScalarReadsTheSameInBothLanes() => AssertParity("""
        z = 1 + 2i;
        fprintf('%.17g %.17g|', real(z(1)), imag(z(1)));
        fprintf('%d %d|', size(z(:)));
        fprintf('%d %d|', size(z([1 1 1])));
        fprintf('%.17g|', abs(z(end)));
        fprintf('%s\n', class(z(1,1)));
        """);

    [Fact]
    public void TheBracketFormReadsAComplexScalarToo()
    {
        // The bracket spelling is JGS's, and it had the same hole: a(…) and a[…] promote a scalar at
        // two different places. JGS keeps its own constants, so inf is still a plain value here and
        // not the constructor the MATLAB dialect gained — it takes no size. How an infinity is spelled
        // on the way out is a display question rather than a dialect one, though, and both dialects
        // now write the word MATLAB writes rather than the one .NET writes.
        JG.Reset();
        var output = new RecordingScriptOutput();
        var context = new ScriptContext(output, (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(
            "let z = 1 + 2i; print(z[0]); print(z(0)); print(inf)",
            context, default, sourceId: "", hook: null, JgsDialect.Jgs);

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "1+2i\n", "1+2i\n", "Inf\n" }, output.Normal.ToArray());
    }

    [Fact]
    public Task AComplexArrayAndARealScalarStillIndexAsTheyDid() => Asserts("""
        w = [1+1i 2+2i];
        assert(isequal(size(w(:)), [2 1]));
        assert(w(2) == 2 + 2i);
        x = 5;
        assert(x(1) == 5);
        assert(isequal(size(x(:)), [1 1]));
        h = true;
        assert(h(1) == true);
        """);

    // --- Inf and NaN take the size arguments zeros and ones take ---------------------------------

    [Fact]
    public Task InfBuildsTheSquareASingleSizeAsks() => Asserts("""
        A = Inf(2);
        assert(isequal(size(A), [2 2]));
        assert(all(all(isinf(A))));
        assert(all(all(A > 0)));
        """);

    [Fact]
    public Task InfAndNaNTakeEveryShapeSpellingZerosTakes() => Asserts("""
        assert(isequal(size(Inf(2,3)), [2 3]));
        assert(isequal(size(Inf([2 3])), [2 3]));
        assert(isequal(size(Inf(2,3,4)), [2 3 4]));
        assert(isequal(size(NaN(2,3)), [2 3]));
        assert(isequal(size(NaN([2 3])), [2 3]));
        assert(isequal(size(NaN(2,3,4)), [2 3 4]));
        assert(all(all(isnan(NaN(2,3)))));
        """);

    [Fact]
    public Task ASizeOfNothingBuildsTheEmptyMatlabBuilds() => Asserts("""
        % MATLAB answers 0-by-0 for both, a negative size counting as none at all.
        assert(isequal(size(Inf(0)), [0 0]));
        assert(isequal(size(Inf(-1)), [0 0]));
        assert(isequal(size(NaN(0)), [0 0]));
        assert(isempty(Inf(0)));
        """);

    [Fact]
    public Task ABareInfOrNaNIsStillTheScalarItAlwaysWas() => Asserts("""
        assert(isequal(size(Inf), [1 1]));
        assert(isequal(size(NaN), [1 1]));
        assert(isinf(Inf));
        assert(isnan(NaN));
        assert(1/0 == Inf);
        assert(-Inf < 0);
        assert(max([1 5 Inf]) == Inf);
        v = [1 Inf NaN];
        assert(isequal(size(v), [1 3]));
        assert(isinf(v(2)) && isnan(v(3)));
        """);

    [Fact]
    public Task TheLowercaseSpellingsTakeASizeToo() => Asserts("""
        assert(isequal(size(inf(2,3)), [2 3]));
        assert(isequal(size(nan(2,3)), [2 3]));
        assert(all(all(isinf(inf(2,2)))));
        assert(all(all(isnan(nan(2,2)))));
        assert(isinf(inf));
        assert(isnan(nan));
        """);

    [Fact]
    public Task AClassTailNamesTheFloatingClassToBuildIn() => Asserts("""
        assert(strcmp(class(Inf(2,'double')), 'double'));
        assert(strcmp(class(Inf(2,'single')), 'single'));
        assert(strcmp(class(NaN(2,'single')), 'single'));
        assert(strcmp(class(Inf(2,'like',single(1))), 'single'));
        assert(strcmp(class(Inf(2,'like',1)), 'double'));
        assert(all(all(isinf(Inf(2,'single')))));
        """);

    [Fact]
    public Task NoIntegerClassHoldsAnInfinity() =>
        Refuses("A = Inf(2,'int32');", "must be 'double' or 'single'");

    [Fact]
    public Task NoIntegerClassHoldsANaN() =>
        Refuses("A = NaN(2,'uint8');", "must be 'double' or 'single'");

    [Fact]
    public Task EpsTakesNoSizeBecauseMatlabsDoesNot() => Asserts("""
        % eps(x) is the spacing AT x, not a shape — so eps(2) is a number, and it must stay one
        % now that its two neighbours in the same registration have become constructors.
        assert(eps(2) == 2^-51);
        assert(eps == 2^-52);
        assert(eps('single') == 2^-23);
        """);

    [Fact]
    public void InfAndNaNBuildTheSameArrayInBothLanes() => AssertParity("""
        fprintf('%d %d|', size(Inf(2)));
        fprintf('%d %d %d|', size(NaN(2,3,4)));
        fprintf('%.17g ', Inf(1,3));
        fprintf('|%d ', isnan(NaN(1,3)));
        fprintf('|%s|%s\n', class(Inf(2,'single')), class(Inf(2)));
        """);

    // --- double and single of a complex argument -------------------------------------------------

    [Fact]
    public Task DoubleOfAComplexAnswersTheComplex() => Asserts("""
        z = double(1 + 2i);
        assert(z == 1 + 2i);
        assert(strcmp(class(z), 'double'));
        assert(~isreal(z));
        """);

    [Fact]
    public Task DoubleOfAComplexArrayAnswersEveryElement() => Asserts("""
        A = double([1+2i 3]);
        assert(isequal(size(A), [1 2]));
        assert(A(1) == 1 + 2i);
        assert(A(2) == 3);
        M = double([1+1i 2; 3 4-1i]);
        assert(isequal(size(M), [2 2]));
        assert(M(2,2) == 4 - 1i);
        """);

    [Fact]
    public Task DoubleOfAComplexKeepsTheShapeOfPlanarStorageToo() => Asserts("""
        % An fft result is held as two planes rather than as boxed elements, which is the other
        % arm of the question "does this hold a complex sample" — and the shape has to survive it.
        F = fft([1 2 3 4 5]);
        assert(~isreal(F));
        D = double(F);
        assert(isequal(size(D), [1 5]));
        assert(max(abs(D - F)) == 0);
        M = double([1+1i 2+2i; 3+3i 4+4i]);
        assert(isequal(size(M), [2 2]));
        assert(M(2,2) == 4 + 4i);
        assert(strcmp(class(single(F)), 'single'));
        """);

    [Fact]
    public Task SingleOfAComplexRoundsBothPartsToFloatPrecision() => Asserts("""
        z = single(0.1 + 0.1i);
        assert(strcmp(class(z), 'single'));
        assert(~isreal(z));
        assert(real(z) == single(0.1));
        assert(imag(z) == single(0.1));
        assert(real(z) ~= 0.1);
        """);

    [Fact]
    public Task CastReachesTheFloatingClassesWithAComplexToo() => Asserts("""
        % 1 and 2 are exact in float, so the single-class answer compares equal to the double one.
        assert(cast(1+2i, 'single') == 1 + 2i);
        assert(strcmp(class(cast(1+2i, 'single')), 'single'));
        assert(cast(1+2i, 'double') == 1 + 2i);
        assert(strcmp(class(cast(1+2i, 'like', single(1))), 'single'));
        assert(cast(1+2i, 'like', single(1)) == 1 + 2i);
        """);

    [Fact]
    public Task ARealArgumentConvertsExactlyAsItAlwaysDid() => Asserts("""
        % The complex arm is only entered when a complex sample is actually there, so the whole
        % real family has to answer what it always answered.
        assert(double('A') == 65);
        assert(isequal(double('abc'), [97 98 99]));
        assert(double(true) == 1);
        assert(strcmp(class(double(int32(5))), 'double'));
        assert(single(0.1) ~= 0.1);
        assert(int32(2.5) == 3);
        assert(uint8(300) == 255);
        assert(isequal(double([1 2; 3 4]), [1 2; 3 4]));
        assert(strcmp(class(single([1 2 3])), 'single'));
        """);

    [Fact]
    public Task AnIntegerClassStillRefusesAComplex() =>
        // MATLAB R2024a does mint a complex integer here — int32(1+2i) is 1+2i — but then refuses
        // every arithmetic operation on one ("Complex integer arithmetic is not supported"), so the
        // value is unusable the moment it exists. JGraph has no storage for it and says so instead.
        Refuses("x = int32(1+2i);", "but got a complex");

    [Fact]
    public Task LogicalStillRefusesAComplexAsMatlabDoes() =>
        Refuses("x = logical(1+2i);", "but got a complex");

    [Fact]
    public void DoubleOfAComplexReadsTheSameInBothLanes() => AssertParity("""
        z = double(1 + 2i);
        fprintf('%.17g %.17g|', real(z), imag(z));
        A = double([1+2i 3]);
        fprintf('%d %d|', size(A));
        fprintf('%.17g ', real(A));
        fprintf('|%.17g ', imag(A));
        s = single(0.1 + 0.1i);
        fprintf('|%.17g %.17g|%s\n', real(s), imag(s), class(s));
        """);
}
