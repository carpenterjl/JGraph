using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// A scalar raised to a matrix power (M139): MATLAB's <c>s ^ A</c> is the eigendecomposition
/// <c>[V, D] = eig(A); V * diag(s .^ diag(D)) / V</c>, and not the elementwise power JGraph used
/// to answer with.
/// </summary>
/// <remarks>
/// <para>
/// <c>2 ^ [1 2; 3 4]</c> answered <c>[2 4; 8 16]</c> here and <c>[10.48 21.23; 14.15 31.71]</c> in
/// R2025b — a silently wrong number, which is the divergence ADR 0143 recorded and this one closes.
/// The formula was checked against MATLAB rather than derived: <c>norm(f - g, 'fro')</c> between
/// <c>s ^ A</c> and the eigendecomposition written out is exactly zero on every well-conditioned
/// matrix probed, and <c>expm(log(s) * A)</c> — the other closed form one might reach for — is off
/// by nine on the defective <c>[2 1; 0 2]</c>.
/// </para>
/// <para>
/// Every expectation below was printed by R2025b and pasted, never computed here. Twelve
/// significant digits is what the two agree on: the worst disagreement measured over the whole set
/// is 6.9e-15 relative, on the singular <c>magic(4)</c>. Where the two differ only in roundoff dust
/// against an exact zero, or in the sign of a zero, the case is asserted against MATLAB's own
/// answer to a tolerance instead — see <see cref="TheDustAgreesOnlyToRounding"/>.
/// </para>
/// <para>
/// Realness is not a rule applied at the end. MATLAB does the arithmetic complex when the
/// eigenvalues are complex and lets the answer be whatever it is, which is why
/// <c>2 ^ [0 -1; 1 0]</c> comes back exactly real (its imaginary parts cancel to the bit) while
/// <c>2 ^ [1 -2; 3 4]</c>, whose eigenvalues are also a conjugate pair, comes back complex with
/// imaginary parts of about 1e-15. Both are asserted here.
/// </para>
/// </remarks>
[Collection("JG facade")]
public class MatlabScalarMatrixPowerM139Tests : IDisposable
{
    private RecordingScriptOutput _output = new();

    public MatlabScalarMatrixPowerM139Tests() => JG.Reset();

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

    private string Failure(string code)
    {
        _output = new RecordingScriptOutput();
        var context = new ScriptContext(_output, (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(
            code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.False(result.Success, "the statement was expected to be refused: " + _output.NormalText);
        return result.Message ?? _output.ErrorText;
    }

    /// <summary>
    /// The class, shape, realness and elements of an expression, printed column-major to twelve
    /// significant digits — the precision at which JGraph and R2025b agree on every row below.
    /// </summary>
    /// <remarks>
    /// The real and imaginary parts print separately because <c>isreal</c> is half the answer here:
    /// a matrix with a conjugate pair of eigenvalues may or may not come back real, and which it is
    /// is a fact about the arithmetic rather than a choice anyone made.
    /// </remarks>
    private string Shape(string expression) => Run(
        $"v = {expression};\n" +
        "fprintf('%s | %d %d | %d | %s| %s', class(v), size(v, 1), size(v, 2), isreal(v), " +
        "sprintf('%.12g ', real(transpose(double(v(:))))), sprintf('%.12g ', imag(transpose(double(v(:))))));");

    /// <summary>
    /// The same to six significant digits, which is as far as a <c>single</c> answer agrees: MATLAB
    /// carries the whole eigendecomposition in single precision and JGraph rounds a double one, so
    /// the two part company at about 2.4e-7 relative.
    /// </summary>
    private string Rounded(string expression) => Run(
        $"v = {expression};\n" +
        "fprintf('%s | %d %d | %d | %s| %s', class(v), size(v, 1), size(v, 2), isreal(v), " +
        "sprintf('%.6g ', real(transpose(double(v(:))))), sprintf('%.6g ', imag(transpose(double(v(:))))));");

    /// <summary>
    /// The class, shape and realness, and whether the answer is within a rounding of the matrix
    /// R2025b printed for the same expression.
    /// </summary>
    private string Close(string expression, string matlab) => Run(
        $"v = {expression};\nm = {matlab};\n" +
        "fprintf('%s | %d %d | %d | %d', class(v), size(v, 1), size(v, 2), isreal(v), " +
        "norm(v - m, 'fro') <= 1e-14 * max(1, norm(m, 'fro')));");

    [Theory]

    // The bug report itself, and the same matrix reached three ways.
    [InlineData("2 ^ [1 2; 3 4]", "double | 2 2 | 1 | 10.4827393896 21.2278182425 14.1518788283 31.7105576321 | 0 0 0 0")]
    [InlineData("[2] ^ [1 2; 3 4]", "double | 2 2 | 1 | 10.4827393896 21.2278182425 14.1518788283 31.7105576321 | 0 0 0 0")]
    [InlineData("2 ^ transpose([1 2; 3 4])", "double | 2 2 | 1 | 10.4827393896 14.1518788283 21.2278182425 31.7105576321 | 0 0 0 0")]

    // A defective matrix: the eigenvector matrix is near-singular and the answer is whatever the
    // eigensolver's columns make it. Equal eigenvalues happen to come out clean in both.
    [InlineData("2 ^ [2 1; 0 2]", "double | 2 2 | 1 | 4 0 0 4 | 0 0 0 0")]
    [InlineData("3 ^ [2 1; 0 2]", "double | 2 2 | 1 | 9 0 0 9 | 0 0 0 0")]
    [InlineData("2 ^ [3 1 0; 0 3 1; 0 0 3]", "double | 3 3 | 1 | 8 0 0 0 8 0 0 0 8 | 0 0 0 0 0 0 0 0 0")]
    [InlineData("2 ^ [1 1; 0 1]", "double | 2 2 | 1 | 2 0 0 2 | 0 0 0 0")]
    [InlineData("2 ^ [1 1 0; 0 1 1; 0 0 2]", "double | 3 3 | 1 | 2 0 0 0 2 0 2 2 4 | 0 0 0 0 0 0 0 0 0")]

    // A conjugate pair of eigenvalues whose imaginary parts cancel to the last bit.
    [InlineData("2 ^ [0 -1; 1 0]", "double | 2 2 | 1 | 0.769238901364 0.638961276314 -0.638961276314 0.769238901364 | 0 0 0 0")]
    [InlineData("10 ^ [0 1; -1 0]", "double | 2 2 | 1 | -0.66820151019 -0.743980336957 0.743980336957 -0.66820151019 | 0 0 0 0")]

    // A diagonal matrix is the elementwise power of its diagonal, exactly.
    [InlineData("2 ^ [1 0; 0 3]", "double | 2 2 | 1 | 2 0 0 8 | 0 0 0 0")]
    [InlineData("7 ^ [2 0 0; 0 3 0; 0 0 5]", "double | 3 3 | 1 | 49 0 0 0 343 0 0 0 16807 | 0 0 0 0 0 0 0 0 0")]
    [InlineData("2 ^ [0.5 0; 0 0.25]", "double | 2 2 | 1 | 1.41421356237 0 0 1.189207115 | 0 0 0 0")]
    [InlineData("2 ^ [-1 0; 0 -2]", "double | 2 2 | 1 | 0.5 0 0 0.25 | 0 0 0 0")]

    // Ordinary real spectra, including one a hair away from being defective.
    [InlineData("2 ^ [1 2; 3 4.0000001]", "double | 2 2 | 1 | 10.4827396641 21.227819184 14.151879456 31.7105595557 | 0 0 0 0")]
    [InlineData("2 ^ [4 1; 2 3]", "double | 2 2 | 1 | 22.6666666667 18.6666666667 9.33333333333 13.3333333333 | 0 0 0 0")]
    [InlineData("2 ^ [1 1; 1 1]", "double | 2 2 | 1 | 2.5 1.5 1.5 2.5 | 0 0 0 0")]
    [InlineData("2 ^ [-1 2; 3 4]", "double | 2 2 | 1 | 4.78571428571 13.6071428571 9.07142857143 27.4642857143 | 0 0 0 0")]
    [InlineData("1.5 ^ [2 -1; 1 2]", "double | 2 2 | 1 | 2.06756783199 0.887503949357 -0.887503949357 2.06756783199 | 0 0 0 0")]
    [InlineData("exp(1) ^ [1 2; 3 4]", "double | 2 2 | 1 | 51.9689561987 112.104846851 74.736564567 164.073803049 | 0 0 0 0")]
    [InlineData("0.5 ^ [1 2; 3 4]", "double | 2 2 | 1 | 0.990954926003 -0.663369320078 -0.442246213385 0.327585605926 | 0 0 0 0")]
    [InlineData("5 ^ [0.1 0.2; 0.3 0.4]", "double | 2 2 | 1 | 1.28399540334 0.747992627919 0.498661751946 2.03198803126 | 0 0 0 0")]
    [InlineData("pi ^ [1 1; -1 1]", "double | 2 2 | 1 | 1.29839547573 -2.8607295555 2.8607295555 1.29839547573 | 0 0 0 0")]
    [InlineData("2 ^ (0.5 * [1 2; 3 4])", "double | 2 2 | 1 | 2.20641536188 2.90201756727 1.93467837818 5.10843292915 | 0 0 0 0")]
    [InlineData("2 ^ [1e-8 1; 0 -1e-8]", "double | 2 2 | 1 | 1.00000000693 0 0.693147178543 0.999999993069 | 0 0 0 0")]

    // The zero matrix and the identity, which is where a scalar power is at its least surprising.
    [InlineData("2 ^ zeros(2, 2)", "double | 2 2 | 1 | 1 0 0 1 | 0 0 0 0")]
    [InlineData("2 ^ eye(3)", "double | 3 3 | 1 | 2 0 0 0 2 0 0 0 2 | 0 0 0 0 0 0 0 0 0")]

    // Bigger than two-by-two, symmetric and not, well conditioned and not.
    [InlineData("2 ^ [2 -1 0; -1 2 -1; 0 -1 2]", "double | 3 3 | 1 | 5.04035836994 -3.2384499433 1.04035836994 -3.2384499433 6.08071673987 -3.2384499433 1.04035836994 -3.2384499433 5.04035836994 | 0 0 0 0 0 0 0 0 0")]
    [InlineData("2 ^ [1 2 3; 4 5 6; 7 8 10]", "double | 3 3 | 1 | 11235.8874653 25331.6873999 41938.4497178 13769.8451917 31047.5674066 51399.3757943 17342.7633123 39101.5325916 64736.1793913 | 0 0 0 0 0 0 0 0 0")]
    [InlineData("2 ^ magic(4)", "double | 4 4 | 1 | 4294967674.99 4294967101.25 4294967284.99 4294967122.77 4294967018.64 4294967439.18 4294967303.5 4294967422.67 4294967119.77 4294967386.12 4294967301.5 4294967376.62 4294967370.61 4294967257.45 4294967294.01 4294967261.93 | 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0")]
    [InlineData("2 ^ hilb(3)", "double | 3 3 | 1 | 2.15808150484 0.603406050575 0.411048349257 0.603406050575 1.37548422626 0.275370078665 0.411048349257 0.275370078665 1.21106488656 | 0 0 0 0 0 0 0 0 0")]

    // A negative base leaves the reals exactly where the elementwise power does: a whole
    // eigenvalue keeps the answer real, a fractional one does not.
    [InlineData("(-2) ^ [1 2; 3 4]", "double | 2 2 | 0 | -3.63483579379 -8.60588906732 -5.73725937821 -12.2407248611 | -9.6501728169 -19.5418494109 -13.0278996073 -29.1920222278")]
    [InlineData("(-2) ^ [2 0; 0 4]", "double | 2 2 | 1 | 4 0 0 16 | 0 0 0 0")]
    [InlineData("(-2) ^ [0 -1; 1 0]", "double | 2 2 | 0 | 8.91698140232 7.4068092599 -7.4068092599 8.91698140232 | -7.37919723953 8.88373957532 -8.88373957532 -7.37919723953")]

    // A complex base, and a complex matrix.
    [InlineData("(1+2i) ^ [1 2; 3 4]", "double | 2 2 | 0 | 17.5320686815 36.8430451749 24.5620301166 54.3751138564 | -6.15449979647 -12.8055472423 -8.53703149489 -18.9600470388")]
    [InlineData("(1+2i) ^ [0 -1; 1 0]", "double | 2 2 | 0 | 1.1634564177 1.20930576955 -1.20930576955 1.1634564177 | -0.971135654432 0.93431623172 -0.93431623172 -0.971135654432")]
    [InlineData("2i ^ [1 2; 3 4]", "double | 2 2 | 0 | -4.97173269166 -12.2771907143 -8.18479380951 -17.2489234059 | 7.92598021116 18.2596183084 12.1730788723 26.1855985196")]
    [InlineData("2 ^ [1+1i 2; 3 4]", "double | 2 2 | 0 | 9.33610629512 20.2734758576 13.5156505717 31.3289625851 | 4.29884557333 5.15814129727 3.43876086485 2.69916158474")]

    // Infinities and NaNs travel through the product with the zeros they meet, which is why a
    // column of the first answer is NaN rather than the infinity its own eigenvalue would give.
    [InlineData("0 ^ [1 2; 3 4]", "double | 2 2 | 1 | NaN NaN -Inf Inf | 0 0 0 0")]
    [InlineData("Inf ^ [1 2; 3 4]", "double | 2 2 | 1 | Inf Inf Inf Inf | 0 0 0 0")]
    [InlineData("NaN ^ [1 2; 3 4]", "double | 2 2 | 1 | NaN NaN NaN NaN | 0 0 0 0")]
    [InlineData("2 ^ [Inf 0; 0 1]", "double | 2 2 | 1 | NaN NaN NaN NaN | 0 0 0 0")]

    // A logical exponent is a double matrix of ones and zeros, and answers as one. So is a char
    // matrix, which is its matrix of character codes.
    [InlineData("2 ^ true", "double | 1 1 | 1 | 2 | 0")]
    [InlineData("2 ^ logical([1 0; 0 1])", "double | 2 2 | 1 | 2 0 0 2 | 0 0 0 0")]
    [InlineData("2 ^ ['ab'; 'cd']", "double | 2 2 | 1 | 9.96027502954e+58 1.01645777274e+59 1.00619052251e+59 1.02682925364e+59 | 0 0 0 0")]
    public void AScalarRaisedToAMatrixIsItsEigendecomposition(string expression, string expected) =>
        Assert.Equal(expected, Shape(expression));

    [Theory]

    // A 1-by-1 exponent is the number it holds, and two scalars are the scalar power (M138).
    [InlineData("2 ^ [5]", "double | 1 1 | 1 | 32 | 0")]
    [InlineData("2 ^ 3", "double | 1 1 | 1 | 8 | 0")]
    [InlineData("2 ^ 0.5", "double | 1 1 | 1 | 1.41421356237 | 0")]
    [InlineData("(-8) ^ (1/3)", "double | 1 1 | 0 | 1 | 1.73205080757")]
    [InlineData("[2] ^ [2]", "double | 1 1 | 1 | 4 | 0")]

    // A matrix base with a scalar exponent is still the repeated product it always was, and the
    // dotted spelling is still elementwise.
    [InlineData("[1 2; 3 4] ^ 2", "double | 2 2 | 1 | 7 15 10 22 | 0 0 0 0")]
    [InlineData("[1 2; 3 4] ^ 3", "double | 2 2 | 1 | 37 81 54 118 | 0 0 0 0")]
    [InlineData("[1 2; 3 4] ^ 0", "double | 2 2 | 1 | 1 0 0 1 | 0 0 0 0")]
    [InlineData("[1 2; 3 4] ^ -1", "double | 2 2 | 1 | -2 1.5 1 -0.5 | 0 0 0 0")]
    [InlineData("2 .^ [1 2; 3 4]", "double | 2 2 | 1 | 2 8 4 16 | 0 0 0 0")]

    // '^' still associates left to right, so this is (2 ^ A) ^ 2 and not 2 ^ (A ^ 2).
    [InlineData("2 ^ [1 2; 3 4] ^ 2", "double | 2 2 | 1 | 410.301336668 895.671640228 597.114426819 1305.9729769 | 0 0 0 0")]
    public void TheOtherFormsOfTheOperatorAreUntouched(string expression, string expected) =>
        Assert.Equal(expected, Shape(expression));

    [Theory]

    // A single operand on either side makes the answer single, as it does for every other
    // arithmetic operator. The values agree to six figures and no further: MATLAB carries the
    // eigendecomposition itself in single precision, and JGraph rounds a double one at the end.
    [InlineData("2 ^ single([1 2; 3 4])", "single | 2 2 | 1 | 10.4827 21.2278 14.1519 31.7106 | 0 0 0 0")]
    [InlineData("single(2) ^ [1 2; 3 4]", "single | 2 2 | 1 | 10.4827 21.2278 14.1519 31.7106 | 0 0 0 0")]
    [InlineData("single(2) ^ single([1 2; 3 4])", "single | 2 2 | 1 | 10.4827 21.2278 14.1519 31.7106 | 0 0 0 0")]
    [InlineData("single(2) ^ [1 0; 0 3]", "single | 2 2 | 1 | 2 0 0 8 | 0 0 0 0")]
    [InlineData("2 ^ single([0 -1; 1 0])", "single | 2 2 | 1 | 0.769239 0.638961 -0.638961 0.769239 | 0 0 0 0")]
    public void ASingleOperandMakesTheAnswerSingle(string expression, string expected) =>
        Assert.Equal(expected, Rounded(expression));

    [Theory]

    // The rows whose answers agree with R2025b's only to a rounding. Two of them differ in the
    // dust left against an exact zero — a conjugate pair whose imaginary parts nearly but not
    // quite cancel, which is a property of the eigensolver and not of the formula — and two only
    // in the sign of a zero. The second column is what R2025b printed, to seventeen digits.
    [InlineData("2 ^ [1 -2; 3 4]",
        "[-2.9863668171566822+1.7763568394002505e-15i -5.6904847853255394-5.7331670465990088e-16i;"
        + "8.5357271779883135 5.5493603608316286+5.7331670465990088e-16i]",
        "double | 2 2 | 0 | 1")]
    [InlineData("(-1) ^ [1 2; 3 4]",
        "[0.20396341776470872-0.92057738518662435i -0.27195122368627822-4.6460891091614406e-17i;"
        + "-0.40792683552941733-1.1102230246251565e-16i -0.20396341776470869-0.92057738518662446i]",
        "double | 2 2 | 0 | 1")]
    [InlineData("2 ^ [0 1 0; 0 0 1; 1 0 0]",
        "[1.0556582457752965 0.70278056945765854+8.0123445265981764e-18i 0.24156118476704405-1.6024689053196365e-17i;"
        + "0.24156118476704425+1.1102230246251565e-16i 1.055658245775297-3.2049378106392718e-17i 0.70278056945765865+1.6024689053196365e-17i;"
        + "0.70278056945765877+1.1102230246251565e-16i 0.2415611847670438 1.0556582457752968]",
        "double | 3 3 | 0 | 1")]
    [InlineData("1 ^ [1 2; 3 4]", "[1 0;0 1]", "double | 2 2 | 1 | 1")]
    [InlineData("true ^ [1 2; 3 4]", "[1 0;0 1]", "double | 2 2 | 1 | 1")]
    [InlineData("(-2) ^ [1 0; 0 2]", "[-2 0;0 4]", "double | 2 2 | 1 | 1")]
    [InlineData("2 ^ [1 2 0 0; 3 4 0 0; 0 0 5 6; 0 0 7 8]",
        "[10.48273938962865 14.151878828320637 0 0;21.227818242480954 31.710557632109609 0 0;"
        + "0 0 3525.5721748330043 4104.7664678576457;0 0 4788.8942125005888 5577.9554087618299]",
        "double | 4 4 | 1 | 1")]
    public void TheDustAgreesOnlyToRounding(string expression, string matlab, string expected) =>
        Assert.Equal(expected, Close(expression, matlab));

    /// <summary>An empty exponent is an empty answer, and keeps the 0-by-0 shape it was given.</summary>
    [Fact]
    public void AnEmptyMatrixRaisesToAnEmptyAnswer() => Assert.Equal(
        "double | 0 0 | 1 | 0",
        Run("v = 2 ^ zeros(0, 0);\n" +
            "fprintf('%s | %d %d | %d | %d', class(v), size(v, 1), size(v, 2), isreal(v), numel(v));"));

    [Theory]

    // A matrix that is not square is not an exponent, and neither is a pair of matrices. All four
    // draw MATLAB's own sentence, which names '.^' as the fix.
    [InlineData("x = 2 ^ [1 2 3];")]
    [InlineData("x = 2 ^ [1 2; 3 4; 5 6];")]
    [InlineData("x = 2 ^ zeros(2, 0);")]
    [InlineData("x = [1 2; 3 4] ^ [1 2; 3 4];")]
    [InlineData("x = [2 0; 0 2] ^ [1 2; 3 4];")]
    [InlineData("x = single(2) ^ [1 2 3];")]
    public void AShapeThatIsNotABaseAndAnExponentIsRefused(string code) => Assert.Contains(
        "Incorrect dimensions for raising a matrix to a power.", Failure(code));

    [Theory]

    // MATLAB defines mpower for an integer class only when both operands are scalar, and says so
    // whichever side the integer is on and whichever side the matrix is.
    [InlineData("x = 2 ^ int8([1 2; 3 4]);")]
    [InlineData("x = int8(2) ^ [1 2; 3 4];")]
    [InlineData("x = uint8(2) ^ [1 2; 3 4];")]
    [InlineData("x = int8([1 2; 3 4]) ^ 2;")]
    [InlineData("x = int8([1 2; 3 4]) ^ int8(2);")]
    [InlineData("x = 2 ^ int8([1 2 3]);")]
    [InlineData("x = int8(2) ^ [1 2 3];")]
    public void AnIntegerClassIsRefusedUnlessBothOperandsAreScalar(string code) => Assert.Contains(
        "MPOWER (^) is not fully supported for integer classes.", Failure(code));

    [Theory]

    // Two integer scalars are the one shape MATLAB does define, and still answer in the class.
    [InlineData("int8(2) ^ int8(3)", "int8 | 8")]
    [InlineData("int8(2) ^ 3", "int8 | 8")]
    [InlineData("2 ^ int8(3)", "int8 | 8")]
    public void TwoIntegerScalarsStillAnswerInTheirClass(string expression, string expected) =>
        Assert.Equal(expected, Run($"v = {expression};\nfprintf('%s | %g', class(v), double(v));"));
}
