using System.Numerics;
using JGraph.Api;
using JGraph.Numerics.LinearAlgebra;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open item 321 (ADR 0365), through the MATLAB dialect: a singular complex <c>\</c> warns and
/// answers, <c>isfloat(true)</c> is false, <c>sqrt(-0)</c> is +0, the reciprocal, degree and special
/// functions keep single, <c>trace</c> answers double and refuses in R2025b's words, a matrix to a
/// fractional power is mpower.m's, and an integer raised with a single or a logical is
/// MATLAB:mixedClasses. Expectations are R2025b's (fixture <c>oi_numeric_leftovers</c> has the rest).
/// </summary>
[Collection("JG facade")]
public class MatlabNumericLeftovers321Tests : IDisposable
{
    private RecordingScriptOutput _output = new();

    public MatlabNumericLeftovers321Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private string Run(string code)
    {
        _output = new RecordingScriptOutput();
        var context = new ScriptContext(_output, (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Trim();
    }

    private string Identifier(string expression) => Run(
        $"try, v = {expression}; disp('answered'); catch e, disp(e.identifier); end");

    [Fact]
    public void ASingularComplexSystemWarnsAndAnswers() =>
        Assert.Equal("NaN NaN NaN NaN MATLAB:singularMatrix", Run(
            "A = [1i 2i; 2i 4i]; b = [1; 2]; lastwarn(''); x = A \\ b; [~, id] = lastwarn; " +
            "fprintf('%g %g %g %g %s', real(x(1)), imag(x(1)), real(x(2)), imag(x(2)), id);"));

    [Fact]
    public void AComplexZeroDivisorDividesPartByPart() =>
        Assert.Equal("0 -1 Inf NaN", Run(
            "A = [1i 0; 0 0]; b = [1; 1]; x = A \\ b; " +
            "fprintf('%g %g %g %g', real(x(1)), imag(x(1)), real(x(2)), imag(x(2)));"));

    [Fact]
    public void ARealMatrixSolvesAComplexRightHandSideByParts() =>
        Assert.Equal("-Inf NaN Inf NaN", Run(
            "A = [1 2; 0 0]; b = [1i; 2]; x = A \\ b; " +
            "fprintf('%g %g %g %g', real(x(1)), imag(x(1)), real(x(2)), imag(x(2)));"));

    [Fact]
    public void ANearlySingularComplexSystemQuotesItsRcond() =>
        Assert.Equal("MATLAB:nearlySingularMatrix Matrix is close to singular or badly scaled. Results may be inaccurate. RCOND =  1.000000e-17.",
            Run("A = [1 0; 0 1e-17i]; lastwarn(''); x = A \\ [1; 1]; [m, id] = lastwarn; fprintf('%s %s', id, m);"));

    [Fact]
    public void AComplexSingularSolveRefusesUnderWarningError() =>
        Assert.Equal("MATLAB:singularMatrix", Run(
            "warning('error', 'MATLAB:singularMatrix'); A = [1i 2i; 2i 4i]; " +
            "try, x = A \\ [1; 2]; disp('answered'); catch e, disp(e.identifier); end"));

    [Fact]
    public void ComplexRcondEstimateMatchesTheExactFigureOnASmallMatrix()
    {
        // n <= 4 is small enough that zlacn2's first steps already find the largest column.
        var a = new Complex[,] { { 2, Complex.ImaginaryOne }, { -Complex.ImaginaryOne, 3 } };
        double rcond = ComplexSquareSystems.ReciprocalCondition(a);
        // ‖A‖₁ = 4, A⁻¹ = [3 -i; i 2]/5, ‖A⁻¹‖₁ = 4/5: RCOND = 1/(4·0.8).
        Assert.Equal(1.0 / 3.2, rcond, 12);
    }

    [Theory]
    [InlineData("isfloat(true)", "0")]
    [InlineData("isfloat([true false])", "0")]
    [InlineData("isfloat(true(0))", "0")]
    [InlineData("isfloat(single(1))", "1")]
    [InlineData("isfloat(1i)", "1")]
    [InlineData("isfloat(int8(1))", "0")]
    public void IsFloatIsFalseForALogical(string expression, string expected) =>
        Assert.Equal(expected, Run($"fprintf('%d', {expression});"));

    [Theory]
    [InlineData("sqrt(-0)")]
    [InlineData("sqrt(single(-0))")]
    [InlineData("realsqrt(-0)")]
    [InlineData("sqrt(-zeros(1, 50000))")]
    public void TheRootOfNegativeZeroIsPositiveZero(string expression) =>
        Assert.Equal("0", Run($"v = {expression}; fprintf('%d', any(1 ./ double(v) < 0));"));

    [Theory]
    [InlineData("asec(single(2))")]
    [InlineData("acoth(single(2))")]
    [InlineData("sind(single(30))")]
    [InlineData("asecd(single(2))")]
    [InlineData("deg2rad(single(180))")]
    [InlineData("rad2deg(single(pi))")]
    [InlineData("gammaln(single(3))")]
    [InlineData("erfinv(single(0.5))")]
    [InlineData("erfcx(single(1))")]
    [InlineData("reallog(single(2))")]
    [InlineData("realsqrt(single(2))")]
    [InlineData("pow2(single(3))")]
    [InlineData("pow2(1.5, single(3))")]
    [InlineData("psi(single(2))")]
    [InlineData("psi(1, single(2))")]
    [InlineData("atan2d(1, single(2))")]
    public void TheFloatingFamilyKeepsSingle(string expression) =>
        Assert.Equal("single", Run($"disp(class({expression}));"));

    [Fact]
    public void PsisOrderDoesNotDecideTheClass() =>
        Assert.Equal("double", Run("disp(class(psi(single(1), 2)));"));

    [Fact]
    public void TheDegreeFunctionsAnswerExactSingles() =>
        Assert.Equal("0.5 180 1", Run("fprintf('%g %g %g', sind(single(30)), rad2deg(single(pi)), tand(single(45)));"));

    [Theory]
    [InlineData("trace(true)", "double 1")]
    [InlineData("trace(int8([100 2; 3 100]))", "double 200")]
    [InlineData("trace(single([1 2; 3 4]))", "single 5")]
    [InlineData("trace('a')", "double 97")]
    [InlineData("trace(magic(3))", "double 15")]
    public void TraceAnswersDoubleOrSingle(string expression, string expected) =>
        Assert.Equal(expected, Run($"v = {expression}; fprintf('%s %g', class(v), v);"));

    [Theory]
    [InlineData("trace([1 2 3])")]
    [InlineData("trace('ab')")]
    [InlineData("trace(ones(2, 2, 2))")]
    public void TraceRefusesWhatIsNotSquare(string expression) =>
        Assert.Equal("MATLAB:trace:square", Identifier(expression));

    [Theory]
    [InlineData("filloutliers(true)")]
    [InlineData("filloutliers(1:5)")]
    [InlineData("filloutliers(int8([1 2 3]))")]
    public void FilloutliersCountsItsArgumentsFirst(string expression) =>
        Assert.Equal("MATLAB:minrhs", Identifier(expression));

    [Fact]
    public void AMatrixToAFractionalPowerIsTheMatrixPower() =>
        Assert.Equal("2 0 0 3 | 0 Inf 0 0 | 2 0.25 0 2 | 1", Run(
            "A = [4 0; 0 9] ^ 0.5; B = [0 1; 0 0] ^ 0.5; C = [4 1; 0 4] ^ 0.5; P = [1 2; 3 4]; Z = P ^ 0.5; " +
            "fprintf('%g %g %g %g | %g %g %g %g | %g %g %g %g | %d', A(1,1), A(1,2), A(2,1), A(2,2), " +
            "B(1,1), B(1,2), B(2,1), B(2,2), C(1,1), C(1,2), C(2,1), C(2,2), norm(Z * Z - P, 1) < 1e-12);"));

    [Fact]
    public void ADefectiveMatrixTakesTheSchurPadeRoad() =>
        Assert.Equal("1", Run(
            "A = [2 1 0; 0 2 1; 0 0 2]; Z = A ^ (1/3); fprintf('%d', norm(Z * Z * Z - A, 1) < 1e-12);"));

    [Theory]
    [InlineData("[1 2; 3 4; 5 6] ^ 0.5", "MATLAB:mpower:notScalarAndSquareMatrix")]
    [InlineData("ones(2, 2, 2) ^ 0.5", "MATLAB:mpower:inputsMustBe2D")]
    [InlineData("int8([4 0; 0 9]) ^ 0.5", "MATLAB:mpower:integerNotSupported")]
    public void AMatrixPowerRefusesInR2025bsWords(string expression, string expected) =>
        Assert.Equal(expected, Identifier(expression));

    [Fact]
    public void AComplexMatrixHasAWholePower() =>
        Assert.Equal("5 0 8 2 12 3 22 0", Run(
            "Z = [1i 2; 3 4] ^ 2; fprintf('%g %g %g %g %g %g %g %g', real(Z(1,1)), imag(Z(1,1)), " +
            "real(Z(1,2)), imag(Z(1,2)), real(Z(2,1)), imag(Z(2,1)), real(Z(2,2)), imag(Z(2,2)));"));

    [Theory]
    [InlineData("int32(4) .^ single(0.5)")]
    [InlineData("int8(2) .^ true")]
    [InlineData("true ^ int8(2)")]
    [InlineData("single(2) .^ int8(2)")]
    [InlineData("int8(2) .^ int16(2)")]
    [InlineData("int8([]) .^ single(2)")]
    public void AnIntegerPowerWithASingleOrALogicalIsMixedClasses(string expression) =>
        Assert.Equal("MATLAB:mixedClasses", Identifier(expression));

    [Theory]
    [InlineData("int8(2) .^ 'a'", "int8 127")]
    [InlineData("int8(2) .^ 2", "int8 4")]
    [InlineData("int8(2) .^ int8(3)", "int8 8")]
    public void AnIntegerPowerWithACharOrADoubleStillAnswers(string expression, string expected) =>
        Assert.Equal(expected, Run($"v = {expression}; fprintf('%s %d', class(v), v);"));
}
