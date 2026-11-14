using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open items 199, 272, 299 and 307 (ADR 0355), through the MATLAB dialect: intmax/intmin answer in
/// their class, an integer is raised only to a whole power that is not negative, the floating-only
/// verbs refuse a logical, a char and an integer, and a singular square system warns under an
/// identifier that warning('off'|'error', id) reaches. Expectations are R2025b's
/// (fixture <c>oi_numeric_classes_warnings</c> carries the rest).
/// </summary>
[Collection("JG facade")]
public class MatlabNumericClassesWarningsTests : IDisposable
{
    private RecordingScriptOutput _output = new();

    public MatlabNumericClassesWarningsTests() => JG.Reset();

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

    [Theory]
    [InlineData("intmax", "int32 2147483647")]
    [InlineData("intmin", "int32 -2147483648")]
    [InlineData("intmax('int8') + 1", "int8 127")]
    [InlineData("intmin('uint16') - 1", "uint16 0")]
    [InlineData("intmax('uint8') / 2", "uint8 128")]
    [InlineData("intmax(\"int16\")", "int16 32767")]
    [InlineData("intmax('like', int8(3))", "int8 127")]
    [InlineData("intmin('LIKE', uint32([1 2]))", "uint32 0")]
    [InlineData("-intmin('int8')", "int8 127")]
    public void TheLimitIsOfTheClassItNames(string expression, string expected) =>
        Assert.Equal(expected, Run($"v = {expression}; fprintf('%s %d', class(v), v);"));

    [Fact]
    public void Int64LimitsAreTheClasssSaturationPoints() =>
        Assert.Equal("int64 uint64 int64 1 1 1", Run(
            "fprintf('%s %s %s %d %d %d', class(intmax('int64')), class(intmax('uint64')), class(intmin('int64')), " +
            "intmax('int64') == int64(2^63), intmax('int64') + 1 == intmax('int64'), intmin('int64') == -2^63);"));

    [Theory]
    [InlineData("intmax('foo')", "MATLAB:intmax:invalidClassName")]
    [InlineData("intmin('double')", "MATLAB:intmin:invalidClassName")]
    [InlineData("intmax(int8(1))", "MATLAB:intmax:invalidClassName")]
    [InlineData("intmax('int8', 'int8')", "MATLAB:intmax:mustBeLike")]
    [InlineData("intmax('like', 3)", "MATLAB:intmax:invalidPrototype")]
    [InlineData("intmin('like', true)", "MATLAB:intmin:invalidPrototype")]
    [InlineData("int32(4) .^ 0.5", "MATLAB:integerPower")]
    [InlineData("int8(4) ^ -1", "MATLAB:integerPower")]
    [InlineData("4 .^ int8(-1)", "MATLAB:integerPower")]
    [InlineData("int32(2) .^ NaN", "MATLAB:integerPower")]
    [InlineData("int32(2) .^ Inf", "answered")]
    [InlineData("int32([]) .^ 0.5", "answered")]
    [InlineData("sqrt(true)", "MATLAB:math:mustBeFloat")]
    [InlineData("exp(int8(1))", "MATLAB:math:mustBeFloat")]
    [InlineData("sin('a')", "MATLAB:math:mustBeFloat")]
    [InlineData("hypot(2, true)", "MATLAB:math:mustBeFloat")]
    [InlineData("sqrt(single(4))", "answered")]
    [InlineData("abs(true)", "answered")]
    [InlineData("deg2rad(true)", "MATLAB:deg2rad:nonFloatInput")]
    [InlineData("inv(int32(4))", "MATLAB:inv:inputType")]
    [InlineData("eig(true)", "MATLAB:eig:inputType")]
    [InlineData("isoutlier(true)", "MATLAB:isoutlier:invalidType")]
    [InlineData("rmoutliers(int8([1 2 3]))", "MATLAB:isoutlier:invalidType")]
    [InlineData("filloutliers(true, 'linear')", "MATLAB:filloutliers:invalidType")]
    public void R2025bsRefusalsAreMade(string expression, string expected) =>
        Assert.Equal(expected, Identifier(expression));

    [Fact]
    public void ASinglesOutlierFencesAreSingleInSingleArithmetic() =>
        Assert.Equal("single -1.94780684 6.94780684 2.5", Run(
            "[~, lo, hi, c] = isoutlier(single([1 2 3 4 100 3 2 1])); " +
            "fprintf('%s %.9g %.9g %.9g', class(lo), lo, hi, c);"));

    [Fact]
    public void ASingularInverseWarnsUnderItsIdentifierAndAnswersInf() =>
        Assert.Equal("MATLAB:singularMatrix [Inf Inf;Inf Inf]", Run(
            "A = [1 2; 2 4]; lastwarn(''); v = inv(A); [~, id] = lastwarn; fprintf('%s %s', id, mat2str(v));"));

    [Fact]
    public void ANearlySingularSolveQuotesRcond() =>
        Assert.Equal("MATLAB:nearlySingularMatrix|Matrix is close to singular or badly scaled. Results may be inaccurate. RCOND =  1.000000e-17.", Run(
            "A = [1 0; 0 1e-17]; b = [1; 1]; lastwarn(''); v = A \\ b; [m, id] = lastwarn; fprintf('%s|%s', id, m);"));

    [Fact]
    public void WarningStatesReachTheSingularWarnings()
    {
        Assert.Equal("MATLAB:singularMatrix", Run(
            "A = [1 2; 2 4]; b = [1; 2]; warning('error', 'MATLAB:singularMatrix'); " +
            "try, v = A \\ b; disp('answered'); catch e, disp(e.identifier); end; warning('on', 'MATLAB:singularMatrix');"));

        Assert.DoesNotContain("Warning:", Run(
            "A = [1 2; 2 4]; warning('off', 'MATLAB:singularMatrix'); v = inv(A); warning('on', 'MATLAB:singularMatrix');")
            + _output.ErrorText + _output.NormalText);

        Assert.Equal("MATLAB:rankDeficientMatrix", Run(
            "A = [1 2; 2 4; 3 6]; lastwarn(''); v = A \\ [1; 2; 3]; [~, id] = lastwarn; disp(id);"));
    }

    [Fact]
    public void ANegativePowerOfASingularMatrixIsInfWithoutAWarning() =>
        Assert.Equal("[Inf Inf;Inf Inf] <>", Run(
            "A = [1 2; 2 4]; lastwarn(''); v = A ^ -1; [~, id] = lastwarn; fprintf('%s <%s>', mat2str(v), id);"));
}
