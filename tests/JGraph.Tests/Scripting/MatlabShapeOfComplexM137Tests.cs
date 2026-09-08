using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The shape verbs over a complex array (M137), which nine of them refused outright.
/// </summary>
/// <remarks>
/// Every expectation here was measured against R2025b, including the two that read like accidents:
/// <c>isreal(flipud(complex(1,0)))</c> is true while <c>isreal(reshape(complex(1,0)))</c> is false,
/// and MATLAB draws that line consistently between the verbs that copy their elements and the verbs
/// that re-stamp a shape. A value with a real imaginary part in it is unaffected either way, which
/// is what the bulk of these check.
/// </remarks>
[Collection("JG facade")]
public class MatlabShapeOfComplexM137Tests : IDisposable
{
    private RecordingScriptOutput _output = new();

    public MatlabShapeOfComplexM137Tests() => JG.Reset();

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
    /// The real parts, the imaginary parts and the shape of an expression, printed column-major.
    /// mat2str is deliberately not used: it prints a <c>+0i</c> where MATLAB prints none, which is a
    /// difference about printing and would drown every expectation here in it.
    /// </summary>
    private string Parts(string setup, string expression) => Run(
        $"{setup}\nv = {expression};\n" +
        "fprintf('%s | %s | %d %d', mat2str(real(v(:).')), mat2str(imag(v(:).')), " +
        "size(v, 1), size(v, 2));");

    private const string Column = "z = [10; -2+2i; -2; -2-2i];";
    private const string Square = "A = [1+2i 3-1i; 4 5i];";

    [Theory]

    // The report's own two, on the column an fft answers.
    [InlineData("flipud(z)", "[-2 -2 -2 10] | [-2 0 2 0] | 4 1")]
    [InlineData("circshift(z, 2)", "[-2 -2 10 -2] | [0 -2 0 2] | 4 1")]
    [InlineData("circshift(z, -1)", "[-2 -2 -2 10] | [2 0 -2 0] | 4 1")]
    [InlineData("flip(z)", "[-2 -2 -2 10] | [-2 0 2 0] | 4 1")]
    [InlineData("flipdim(z, 1)", "[-2 -2 -2 10] | [-2 0 2 0] | 4 1")]

    // Flipping a column along its singleton direction changes nothing, and along a third even less.
    [InlineData("flip(z, 2)", "[10 -2 -2 -2] | [0 2 0 -2] | 4 1")]
    [InlineData("fliplr(z)", "[10 -2 -2 -2] | [0 2 0 -2] | 4 1")]

    // reshape and the two transposes read the same elements in the same order.
    [InlineData("reshape(z, 2, 2)", "[10 -2 -2 -2] | [0 2 0 -2] | 2 2")]
    [InlineData("transpose(z)", "[10 -2 -2 -2] | [0 2 0 -2] | 1 4")]
    [InlineData("ctranspose(z)", "[10 -2 -2 -2] | [0 -2 0 2] | 1 4")]
    [InlineData("shiftdim(reshape(z, 1, 4))", "[10 -2 -2 -2] | [0 2 0 -2] | 4 1")]
    public Task AComplexColumnIsRearranged(string expression, string expected) =>
        Task.Run(() => Assert.Equal(expected, Parts(Column, expression)));

    [Theory]
    [InlineData("flipud(A)", "[4 1 0 3] | [0 2 5 -1] | 2 2")]
    [InlineData("fliplr(A)", "[3 0 1 4] | [-1 5 2 0] | 2 2")]
    [InlineData("rot90(A)", "[3 1 0 4] | [-1 2 5 0] | 2 2")]
    [InlineData("rot90(A, 2)", "[0 3 4 1] | [5 -1 0 2] | 2 2")]
    [InlineData("rot90(A, 3)", "[4 0 1 3] | [0 5 2 -1] | 2 2")]
    [InlineData("rot90(A, -1)", "[4 0 1 3] | [0 5 2 -1] | 2 2")]
    [InlineData("rot90(A, 0)", "[1 4 3 0] | [2 0 -1 5] | 2 2")]
    [InlineData("circshift(A, 1, 2)", "[3 0 1 4] | [-1 5 2 0] | 2 2")]
    [InlineData("circshift(A, [1 1])", "[0 3 4 1] | [5 -1 0 2] | 2 2")]
    [InlineData("reshape(A, 1, 4)", "[1 4 3 0] | [2 0 -1 5] | 1 4")]
    [InlineData("permute(A, [2 1])", "[1 3 4 0] | [2 -1 0 5] | 2 2")]
    [InlineData("ipermute(A, [2 1])", "[1 3 4 0] | [2 -1 0 5] | 2 2")]
    [InlineData("squeeze(A)", "[1 4 3 0] | [2 0 -1 5] | 2 2")]

    // The two transposes differ by exactly the conjugation, which is the whole reason ctranspose
    // cannot simply move its elements the way the rest of this family does.
    [InlineData("transpose(A)", "[1 3 4 0] | [2 -1 0 5] | 2 2")]
    [InlineData("ctranspose(A)", "[1 3 4 0] | [-2 1 0 -5] | 2 2")]
    [InlineData("A.'", "[1 3 4 0] | [2 -1 0 5] | 2 2")]
    [InlineData("A'", "[1 3 4 0] | [-2 1 0 -5] | 2 2")]
    public Task AComplexMatrixIsRearranged(string expression, string expected) =>
        Task.Run(() => Assert.Equal(expected, Parts(Square, expression)));

    [Theory]
    [InlineData("permute(N, [3 1 2])", "[1 5 2 6 3 7 4 8] | [8 4 7 3 6 2 5 1] | 2 2")]
    [InlineData("ipermute(N, [3 1 2])", "[1 3 5 7 2 4 6 8] | [8 6 4 2 7 5 3 1] | 2 2")]
    [InlineData("circshift(N, 1, 3)", "[5 6 7 8 1 2 3 4] | [4 3 2 1 8 7 6 5] | 2 2")]
    [InlineData("flip(N, 3)", "[5 6 7 8 1 2 3 4] | [4 3 2 1 8 7 6 5] | 2 2")]
    [InlineData("squeeze(N(1, :, :))", "[1 3 5 7] | [8 6 4 2] | 2 2")]
    public Task AComplexPageStackIsRearranged(string expression, string expected) => Task.Run(() =>
        Assert.Equal(expected, Parts("N = reshape((1:8) + 1i * (8:-1:1), 2, 2, 2);", expression)));

    /// <summary>
    /// A complex scalar is not an array, and the verbs that read one as a block of doubles refused
    /// it for the same reason they refused the column.
    /// </summary>
    [Theory]
    [InlineData("flipud(w)", "3 | 4 | 1 1")]
    [InlineData("fliplr(w)", "3 | 4 | 1 1")]
    [InlineData("circshift(w, 1)", "3 | 4 | 1 1")]
    [InlineData("rot90(w)", "3 | 4 | 1 1")]
    [InlineData("reshape(w, 1, 1)", "3 | 4 | 1 1")]
    [InlineData("permute(w, [2 1])", "3 | 4 | 1 1")]
    [InlineData("transpose(w)", "3 | 4 | 1 1")]
    [InlineData("ctranspose(w)", "3 | -4 | 1 1")]
    public Task AComplexScalarSurvivesTheVerbs(string expression, string expected) =>
        Task.Run(() => Assert.Equal(expected, Parts("w = 3 + 4i;", expression)));

    /// <summary>
    /// A shift of a scalar came back <em>empty</em> — for a real scalar too, so this predates the
    /// complex work and is what made the complex scalar above worth its own case. circshift was
    /// asking an array for its dimensions and a bare number is not one.
    /// </summary>
    [Theory]
    [InlineData("circshift(7, 0)", "7 | 0 | 1 1")]
    [InlineData("circshift(7, 1)", "7 | 0 | 1 1")]
    [InlineData("circshift(7, 3, 2)", "7 | 0 | 1 1")]
    public Task AShiftOfAScalarIsTheScalar(string expression, string expected) =>
        Task.Run(() => Assert.Equal(expected, Parts("", expression)));

    /// <summary>
    /// MATLAB's own seam, measured: a verb that copies its elements into a new array answers real
    /// when nothing imaginary landed in it, and a verb that stamps a new shape on the same data
    /// keeps the complexity. Nothing about the numbers differs between the two lists.
    /// </summary>
    [Theory]
    [InlineData("flipud(x)", "1")]
    [InlineData("fliplr(x)", "1")]
    [InlineData("flip(x)", "1")]
    [InlineData("flipdim(x, 1)", "1")]
    [InlineData("circshift(x, 1)", "1")]
    [InlineData("rot90(x)", "1")]
    [InlineData("reshape(x, 1, 4)", "0")]
    [InlineData("permute(x, [2 1])", "0")]
    [InlineData("ipermute(x, [2 1])", "0")]
    [InlineData("squeeze(x)", "0")]
    [InlineData("transpose(x)", "0")]
    [InlineData("ctranspose(x)", "0")]
    public Task AllZeroImaginaryPartsNarrowForSomeVerbsAndNotOthers(string expression, string expected)
        => Task.Run(() => Assert.Equal(expected, Run(
            $"x = zeros(2, 2, 'like', 1i);\nfprintf('%d', isreal({expression}));")));

    /// <summary>
    /// A numeric class survives a complex rearrangement, which it would not if the retrofit for the
    /// one had been written inside the retrofit for the other (M123 wraps this one from outside).
    /// </summary>
    [Theory]
    [InlineData("circshift(single(A), 1)", "single")]
    [InlineData("flipud(single(A))", "single")]
    [InlineData("reshape(single(A), 1, 4)", "single")]
    public Task ANumericClassSurvivesAComplexRearrangement(string expression, string expected) =>
        Task.Run(() => Assert.Equal(expected, Run($"{Square}\nfprintf('%s', class({expression}));")));

    /// <summary>
    /// The verbs' own empty rules still settle an empty, which they would not if the complex lane
    /// rebuilt one from a dimension list: flipping a 0-by-3 leaves it 0-by-3.
    /// </summary>
    [Theory]
    [InlineData("flipud(e)", "0 3")]
    [InlineData("fliplr(e)", "0 3")]
    [InlineData("circshift(e, 1)", "0 3")]
    [InlineData("reshape(e, 3, 0)", "3 0")]
    [InlineData("transpose(e)", "3 0")]
    [InlineData("ctranspose(e)", "3 0")]
    public Task AnEmptyKeepsItsShape(string expression, string expected) => Task.Run(() =>
        Assert.Equal(expected, Run(
            $"e = zeros(0, 3, 'like', 1i);\nv = {expression};\n" +
            "fprintf('%d %d', size(v, 1), size(v, 2));")));

    /// <summary>
    /// The transposes lost an empty's shape for a <em>real</em> empty too, which is how this was
    /// found: the apostrophe answered the 3-by-0 MATLAB answers and the named function answered
    /// 0-by-0, so one operation was carrying two readings of an empty.
    /// </summary>
    [Theory]
    [InlineData("transpose(zeros(0, 3))", "3 0")]
    [InlineData("ctranspose(zeros(0, 3))", "3 0")]
    [InlineData("zeros(0, 3)'", "3 0")]
    [InlineData("transpose(zeros(2, 0))", "0 2")]
    [InlineData("transpose(zeros(0, 0))", "0 0")]
    public Task ATransposedEmptyKeepsItsDimensions(string expression, string expected) =>
        Task.Run(() => Assert.Equal(expected, Run(
            $"v = {expression};\nfprintf('%d %d', size(v, 1), size(v, 2));")));

    /// <summary>
    /// The text lanes are untouched by the complex one — a char row, a string array and a cell hold
    /// nothing complex, and each still leaves in the container it arrived in (M122).
    /// </summary>
    [Theory]
    [InlineData("circshift('abcd', 1)", "char 1 4")]
    [InlineData("flipud(['ab'; 'cd'])", "char 2 2")]
    [InlineData("fliplr([\"a\" \"b\"])", "string 1 2")]
    [InlineData("circshift({1, 2}, 1)", "cell 1 2")]
    public Task TextStillRearrangesInItsOwnContainer(string expression, string expected) =>
        Task.Run(() => Assert.Equal(expected, Run(
            $"v = {expression};\nfprintf('%s %d %d', class(v), size(v, 1), size(v, 2));")));
}
