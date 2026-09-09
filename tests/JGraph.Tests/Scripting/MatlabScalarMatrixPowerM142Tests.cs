using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// A scalar raised to a matrix power (M142): <c>s ^ A</c> is the matrix function
/// <c>f(x) = s^x</c>, and not the elementwise power JGraph used to answer with.
/// </summary>
/// <remarks>
/// <para>
/// <c>2 ^ [1 2; 3 4]</c> answered <c>[2 4; 8 16]</c> here and <c>[10.48 14.15; 21.23 31.71]</c> in
/// R2025b — a silently wrong number, which is the divergence ADR 0143 recorded and ADR 0146 closes.
/// </para>
/// <para>
/// MATLAB's own <c>^</c> is <em>not</em> the oracle for these expectations, and that is the whole
/// point of the milestone. MATLAB evaluates <c>[V, D] = eig(A); V * diag(s .^ diag(D)) / V</c>,
/// which is right when <c>A</c> has a full set of eigenvectors and wrong when it does not:
/// <c>2 ^ [2 1; 0 2]</c> is <c>[4 4·ln2; 0 4]</c> by the Jordan form and R2025b answers
/// <c>[4 0; 0 4]</c>, because <c>eig</c> hands the formula the same eigenvector twice and the
/// product collapses to <c>s^λ</c> times the identity.
/// </para>
/// <para>
/// So every expectation below was printed by R2025b from <c>expm(log(s) * A)</c> — the principal
/// branch of the same function, which agrees with <c>s ^ A</c> wherever <c>s ^ A</c> is right and
/// keeps working where it is not — and pasted, never computed here. Eighty-two expressions were
/// measured that way: seventy-six print identically to twelve significant digits, and the six that
/// do not are listed in <see cref="TheDustAgreesOnlyToRounding"/>, four of them because JGraph's
/// answer is exactly real where the matrix exponential leaves a picogram of imaginary dust.
/// </para>
/// </remarks>
[Collection("JG facade")]
public class MatlabScalarMatrixPowerM142Tests : IDisposable
{
    private RecordingScriptOutput _output = new();

    public MatlabScalarMatrixPowerM142Tests() => JG.Reset();

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
    /// is a fact about the base rather than a choice anyone made.
    /// </remarks>
    private string Shape(string expression) => Run(
        $"v = {expression};\n" +
        "fprintf('%s | %d %d | %d | %s| %s', class(v), size(v, 1), size(v, 2), isreal(v), " +
        "sprintf('%.12g ', real(transpose(double(v(:))))), sprintf('%.12g ', imag(transpose(double(v(:))))));");

    /// <summary>
    /// The same to six significant digits, which is as far as a <c>single</c> answer agrees with
    /// MATLAB's: MATLAB carries the whole evaluation in single precision and JGraph rounds a double
    /// one, so the two part company at about 2.4e-7 relative.
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

    // A defective exponent, which is the whole of this milestone: one eigenvalue with one
    // eigenvector, so the answer carries the derivative of the base as well as its value.
    [InlineData("2 ^ [2 1; 0 2]", "double | 2 2 | 1 | 4 0 2.77258872224 4 | 0 0 0 0")]
    [InlineData("3 ^ [2 1; 0 2]", "double | 2 2 | 1 | 9 0 9.88751059801 9 | 0 0 0 0")]
    [InlineData("2 ^ [1 1; 0 1]", "double | 2 2 | 1 | 2 0 1.38629436112 2 | 0 0 0 0")]
    [InlineData("2 ^ [0 1; 0 0]", "double | 2 2 | 1 | 1 0 0.69314718056 1 | 0 0 0 0")]
    [InlineData("2 ^ [2 5; 0 2]", "double | 2 2 | 1 | 4 0 13.8629436112 4 | 0 0 0 0")]
    [InlineData("0.5 ^ [2 1; 0 2]", "double | 2 2 | 1 | 0.25 0 -0.17328679514 0.25 | 0 0 0 0")]
    [InlineData("exp(1) ^ [2 1; 0 2]", "double | 2 2 | 1 | 7.38905609893 0 7.38905609893 7.38905609893 | 0 0 0 0")]
    [InlineData("1e10 ^ [2 1; 0 2]", "double | 2 2 | 1 | 1e+20 0 2.30258509299e+21 1e+20 | 0 0 0 0")]
    [InlineData("1e-10 ^ [2 1; 0 2]", "double | 2 2 | 1 | 1e-20 0 -2.30258509299e-19 1e-20 | 0 0 0 0")]
    [InlineData("2 ^ (100 * [2 1; 0 2])", "double | 2 2 | 1 | 1.60693804426e+60 0 1.11384457471e+62 1.60693804426e+60 | 0 0 0 0")]
    [InlineData("2 ^ [1 1e6; 0 1]", "double | 2 2 | 1 | 2 0 1386294.36112 2 | 0 0 0 0")]
    [InlineData("2 ^ [3 1 0; 0 3 1; 0 0 3]", "double | 3 3 | 1 | 8 0 0 5.54517744448 8 0 1.92181205567 5.54517744448 8 | 0 0 0 0 0 0 0 0 0")]
    [InlineData("0.1 ^ [3 1 0; 0 3 1; 0 0 3]", "double | 3 3 | 1 | 0.001 0 0 -0.00230258509299 0.001 0 0.00265094905524 -0.00230258509299 0.001 | 0 0 0 0 0 0 0 0 0")]
    [InlineData("2 ^ [1 1 0; 0 1 1; 0 0 2]", "double | 3 3 | 1 | 2 0 0 1.38629436112 2 0 0.61370563888 2 4 | 0 0 0 0 0 0 0 0 0")]
    [InlineData("5 ^ [2 1 0; 0 2 0; 0 0 7]", "double | 3 3 | 1 | 25 0 0 40.2359478109 25 0 0 0 78125 | 0 0 0 0 0 0 0 0 0")]
    [InlineData("2 ^ [1 1 1; 0 1 1; 0 0 1]", "double | 3 3 | 1 | 2 0 0 1.38629436112 2 0 1.86674737504 1.38629436112 2 | 0 0 0 0 0 0 0 0 0")]
    [InlineData("2 ^ [0 1 0; 0 0 1; 0 0 0]", "double | 3 3 | 1 | 1 0 0 0.69314718056 1 0 0.240226506959 0.69314718056 1 | 0 0 0 0 0 0 0 0 0")]
    [InlineData("3 ^ gallery('jordbloc', 4, 1.5)", "double | 4 4 | 1 | 5.19615242271 0 0 0 5.70855690538 5.19615242271 0 0 3.1357453834 5.70855690538 5.19615242271 0 1.14832280411 3.1357453834 5.70855690538 5.19615242271 | 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0")]
    [InlineData("2 ^ [4 1 0 0; 0 4 1 0; 0 0 4 0; 0 0 0 4]", "double | 4 4 | 1 | 16 0 0 0 11.090354889 16 0 0 3.84362411135 11.090354889 16 0 0 0 0 16 | 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0")]
    [InlineData("2 ^ toeplitz([2 1 0 0], [2 0 0 0])", "double | 4 4 | 1 | 4 2.77258872224 0.960906027836 0.222016434659 0 4 2.77258872224 0.960906027836 0 0 4 2.77258872224 0 0 0 4 | 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0")]
    [InlineData("2 ^ (eye(4) + diag([1 1 1], 1))", "double | 4 4 | 1 | 2 0 0 0 1.38629436112 2 0 0 0.480453013918 1.38629436112 2 0 0.11100821733 0.480453013918 1.38629436112 2 | 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0")]
    [InlineData("2 ^ [2 1 0 0 0 0; 0 2 1 0 0 0; 0 0 2 1 0 0; 0 0 0 2 1 0; 0 0 0 0 2 1; 0 0 0 0 0 2]", "double | 6 6 | 1 | 4 0 0 0 0 0 2.77258872224 4 0 0 0 0 0.960906027836 2.77258872224 4 0 0 0 0.222016434659 0.960906027836 2.77258872224 4 0 0 0.0384725164305 0.222016434659 0.960906027836 2.77258872224 4 0 0.00533342325857 0.0384725164305 0.222016434659 0.960906027836 2.77258872224 4 | 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0")]
    [InlineData("2 ^ (diag([1 1 1 2 2]) + diag([1 1 0 1], 1))", "double | 5 5 | 1 | 2 0 0 0 0 1.38629436112 2 0 0 0 0.480453013918 1.38629436112 2 0 0 0 0 0 4 0 0 0 0 2.77258872224 4 | 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0")]

    // A defective exponent that is not already triangular, so the Schur form has to find the
    // repeated eigenvalue rather than being handed it.
    [InlineData("2 ^ [1 1; -1 3]", "double | 2 2 | 1 | 1.22741127776 -2.77258872224 2.77258872224 6.77258872224 | 0 0 0 0")]
    [InlineData("2 ^ [3 -1; 1 1]", "double | 2 2 | 1 | 6.77258872224 2.77258872224 -2.77258872224 1.22741127776 | 0 0 0 0")]
    [InlineData("2 ^ ([2 1 0; 0 2 0; 0 0 2] + [0 0 0; 0 0 0; 1 0 0])", "double | 3 3 | 1 | 4 0 2.77258872224 2.77258872224 4 0.960906027836 0 0 4 | 0 0 0 0 0 0 0 0 0")]
    [InlineData("2 ^ [5 -1 0; 1 3 0; 0 0 3]", "double | 3 3 | 1 | 27.090354889 11.090354889 0 -11.090354889 4.90964511104 0 0 0 8 | 0 0 0 0 0 0 0 0 0")]

    // Eigenvalues close enough to share a block without being equal, which is where the
    // blocking tolerance decides between a Taylor series and a division.
    [InlineData("2 ^ [1 1; 0 1+1e-10]", "double | 2 2 | 1 | 2 0 1.38629436117 2.00000000014 | 0 0 0 0")]
    [InlineData("2 ^ [1 1; 0 1+1e-6]", "double | 2 2 | 1 | 2 0 1.38629484157 2.00000138629 | 0 0 0 0")]
    [InlineData("2 ^ [1e-8 1; 0 -1e-8]", "double | 2 2 | 1 | 1.00000000693 0 0.69314718056 0.999999993069 | 0 0 0 0")]
    [InlineData("2 ^ [1 2; 3 4.0000001]", "double | 2 2 | 1 | 10.4827396641 21.227819184 14.151879456 31.7105595557 | 0 0 0 0")]

    // A conjugate pair of eigenvalues, whose imaginary parts cancel because a positive base
    // carries the reals to the reals.
    [InlineData("2 ^ [0 -1; 1 0]", "double | 2 2 | 1 | 0.769238901364 0.638961276314 -0.638961276314 0.769238901364 | 0 0 0 0")]
    [InlineData("10 ^ [0 1; -1 0]", "double | 2 2 | 1 | -0.66820151019 -0.743980336957 0.743980336957 -0.66820151019 | 0 0 0 0")]
    [InlineData("2 ^ [1 -2; 3 4]", "double | 2 2 | 1 | -2.98636681716 8.53572717799 -5.69048478533 5.54936036083 | 0 0 0 0")]
    [InlineData("2 ^ [0 1 0; 0 0 1; 1 0 0]", "double | 3 3 | 1 | 1.05565824578 0.241561184767 0.702780569458 0.702780569458 1.05565824578 0.241561184767 0.241561184767 0.702780569458 1.05565824578 | 0 0 0 0 0 0 0 0 0")]
    [InlineData("1.5 ^ [2 -1; 1 2]", "double | 2 2 | 1 | 2.06756783199 0.887503949357 -0.887503949357 2.06756783199 | 0 0 0 0")]
    [InlineData("pi ^ [1 1; -1 1]", "double | 2 2 | 1 | 1.29839547573 -2.8607295555 2.8607295555 1.29839547573 | 0 0 0 0")]

    // A diagonal exponent is the elementwise power of its diagonal, exactly.
    [InlineData("2 ^ [1 0; 0 3]", "double | 2 2 | 1 | 2 0 0 8 | 0 0 0 0")]
    [InlineData("7 ^ [2 0 0; 0 3 0; 0 0 5]", "double | 3 3 | 1 | 49 0 0 0 343 0 0 0 16807 | 0 0 0 0 0 0 0 0 0")]
    [InlineData("2 ^ [0.5 0; 0 0.25]", "double | 2 2 | 1 | 1.41421356237 0 0 1.189207115 | 0 0 0 0")]
    [InlineData("2 ^ [-1 0; 0 -2]", "double | 2 2 | 1 | 0.5 0 0 0.25 | 0 0 0 0")]
    [InlineData("2 ^ zeros(2, 2)", "double | 2 2 | 1 | 1 0 0 1 | 0 0 0 0")]
    [InlineData("2 ^ eye(3)", "double | 3 3 | 1 | 2 0 0 0 2 0 0 0 2 | 0 0 0 0 0 0 0 0 0")]

    // Ordinary real spectra, well conditioned and not.
    [InlineData("2 ^ [4 1; 2 3]", "double | 2 2 | 1 | 22.6666666667 18.6666666667 9.33333333333 13.3333333333 | 0 0 0 0")]
    [InlineData("2 ^ [1 1; 1 1]", "double | 2 2 | 1 | 2.5 1.5 1.5 2.5 | 0 0 0 0")]
    [InlineData("2 ^ [-1 2; 3 4]", "double | 2 2 | 1 | 4.78571428571 13.6071428571 9.07142857143 27.4642857143 | 0 0 0 0")]
    [InlineData("exp(1) ^ [1 2; 3 4]", "double | 2 2 | 1 | 51.9689561987 112.104846851 74.736564567 164.073803049 | 0 0 0 0")]
    [InlineData("0.5 ^ [1 2; 3 4]", "double | 2 2 | 1 | 0.990954926003 -0.663369320078 -0.442246213385 0.327585605926 | 0 0 0 0")]
    [InlineData("5 ^ [0.1 0.2; 0.3 0.4]", "double | 2 2 | 1 | 1.28399540334 0.747992627919 0.498661751946 2.03198803126 | 0 0 0 0")]
    [InlineData("2 ^ (0.5 * [1 2; 3 4])", "double | 2 2 | 1 | 2.20641536188 2.90201756727 1.93467837818 5.10843292915 | 0 0 0 0")]
    [InlineData("2 ^ ([1 2; 3 4] * 20)", "double | 2 2 | 1 | 5.27889047764e+31 1.15403971215e+32 7.69359808099e+31 1.68192875991e+32 | 0 0 0 0")]
    [InlineData("2 ^ [2 -1 0; -1 2 -1; 0 -1 2]", "double | 3 3 | 1 | 5.04035836994 -3.2384499433 1.04035836994 -3.2384499433 6.08071673987 -3.2384499433 1.04035836994 -3.2384499433 5.04035836994 | 0 0 0 0 0 0 0 0 0")]
    [InlineData("2 ^ [1 2 3; 4 5 6; 7 8 10]", "double | 3 3 | 1 | 11235.8874653 25331.6873999 41938.4497178 13769.8451917 31047.5674066 51399.3757943 17342.7633123 39101.5325916 64736.1793913 | 0 0 0 0 0 0 0 0 0")]
    [InlineData("2 ^ [1 2 3; 4 5 6; 7 8 9]", "double | 3 3 | 1 | 7961.9238606 18029.1892908 28097.454721 9782.06720937 22153.7503315 34523.4334536 11603.2105581 26276.3113722 40950.4121862 | 0 0 0 0 0 0 0 0 0")]
    [InlineData("2 ^ magic(4)", "double | 4 4 | 1 | 4294967674.99 4294967101.25 4294967284.99 4294967122.77 4294967018.64 4294967439.18 4294967303.5 4294967422.67 4294967119.77 4294967386.12 4294967301.5 4294967376.62 4294967370.61 4294967257.45 4294967294.01 4294967261.93 | 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0")]
    [InlineData("2 ^ magic(5)", "double | 5 5 | 1 | 7.37869762948e+18 7.37869762948e+18 7.37869762948e+18 7.37869762948e+18 7.37869762948e+18 7.37869762948e+18 7.37869762948e+18 7.37869762948e+18 7.37869762948e+18 7.37869762948e+18 7.37869762948e+18 7.37869762948e+18 7.37869762948e+18 7.37869762948e+18 7.37869762948e+18 7.37869762948e+18 7.37869762948e+18 7.37869762948e+18 7.37869762948e+18 7.37869762948e+18 7.37869762948e+18 7.37869762948e+18 7.37869762948e+18 7.37869762948e+18 7.37869762948e+18 | 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0")]
    [InlineData("2 ^ hilb(3)", "double | 3 3 | 1 | 2.15808150484 0.603406050575 0.411048349257 0.603406050575 1.37548422626 0.275370078665 0.411048349257 0.275370078665 1.21106488656 | 0 0 0 0 0 0 0 0 0")]
    [InlineData("2 ^ hilb(5)", "double | 5 5 | 1 | 2.21407637481 0.644873823243 0.444744308337 0.340984845304 0.277063039096 0.644873823243 1.4061441021 0.300268678147 0.239025940948 0.198822144516 0.444744308337 0.300268678147 1.23128078416 0.189050642697 0.160178906584 0.340984845304 0.239025940948 0.189050642697 1.15750257051 0.135362855401 0.277063039096 0.198822144516 0.160178906584 0.135362855401 1.11764620206 | 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0")]
    [InlineData("2 ^ (0.001 * magic(4))", "double | 4 4 | 1 | 1.01117381499 0.00352800716113 0.00630637909935 0.00283869917665 0.00144855229843 1.00770036961 0.00492584131219 0.00977213720383 0.00214746937396 0.00700529617488 1.00423461151 0.0104595233701 0.00907706376473 0.00561322747845 0.00838006850741 1.00077654068 | 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0")]
    [InlineData("2 ^ (magic(4) / 34)", "double | 4 4 | 1 | 1.41541837811 0.171582232564 0.258582843242 0.15441654608 0.110082701435 1.30625084511 0.222584391661 0.361082061791 0.135583780983 0.284083922791 1.20375162656 0.376580669661 0.338915139468 0.238082999532 0.315081138532 1.10792072247 | 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0")]
    [InlineData("2 ^ (vander([1 2 3 4]) / 64)", "double | 4 4 | 1 | 1.01688969073 0.0964153346114 0.307878187789 0.716742852626 0.0126729435279 1.04691431975 0.104094008446 0.184603351917 0.0114622009331 0.0232766810903 1.03606791254 0.0502012236139 0.0110964594001 0.011766840788 0.0132761434851 1.01598185091 | 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0")]
    [InlineData("2 ^ [1 2 0 0; 3 4 0 0; 0 0 5 6; 0 0 7 8]", "double | 4 4 | 1 | 10.4827393896 21.2278182425 0 0 14.1518788283 31.7105576321 0 0 0 0 3525.57217483 4788.8942125 0 0 4104.76646786 5577.95540876 | 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0")]

    // A negative base leaves the reals exactly where the elementwise power does: a whole
    // eigenvalue keeps the answer real, a fractional one does not.
    [InlineData("(-2) ^ [1 2; 3 4]", "double | 2 2 | 0 | -3.63483579379 -8.60588906732 -5.73725937821 -12.2407248611 | -9.6501728169 -19.5418494109 -13.0278996073 -29.1920222278")]
    [InlineData("(-2) ^ [0 -1; 1 0]", "double | 2 2 | 0 | 8.91698140232 7.4068092599 -7.4068092599 8.91698140232 | -7.37919723953 8.88373957532 -8.88373957532 -7.37919723953")]

    // A complex base, and a complex exponent.
    [InlineData("(1+2i) ^ [1 2; 3 4]", "double | 2 2 | 0 | 17.5320686815 36.8430451749 24.5620301166 54.3751138564 | -6.15449979647 -12.8055472423 -8.53703149489 -18.9600470388")]
    [InlineData("(1+2i) ^ [0 -1; 1 0]", "double | 2 2 | 0 | 1.1634564177 1.20930576955 -1.20930576955 1.1634564177 | -0.971135654432 0.93431623172 -0.93431623172 -0.971135654432")]
    [InlineData("(1+2i) ^ [2 1; 0 2]", "double | 2 2 | 0 | -3 0 -6.84275173983 -3 | 4 0 -0.102570328514 4")]
    [InlineData("2i ^ [1 2; 3 4]", "double | 2 2 | 0 | -4.97173269166 -12.2771907143 -8.18479380951 -17.2489234059 | 7.92598021116 18.2596183084 12.1730788723 26.1855985196")]
    [InlineData("2 ^ [1+1i 2; 3 4]", "double | 2 2 | 0 | 9.33610629512 20.2734758576 13.5156505717 31.3289625851 | 4.29884557333 5.15814129727 3.43876086485 2.69916158474")]
    [InlineData("2 ^ [2+1i 1; 0 2+1i]", "double | 2 2 | 0 | 3.07695560546 0 2.13278310263 3.07695560546 | 2.55584510525 0 1.77157682866 2.55584510525")]
    [InlineData("2 ^ [1i 1; 0 1i]", "double | 2 2 | 0 | 0.769238901364 0 0.533195775657 0.769238901364 | 0.638961276314 0 0.442894207164 0.638961276314")]

    // A logical exponent is a double matrix of ones and zeros, and answers as one. So is a
    // char matrix, which is its matrix of character codes.
    [InlineData("2 ^ ['ab'; 'cd']", "double | 2 2 | 1 | 9.96027502954e+58 1.01645777274e+59 1.00619052251e+59 1.02682925364e+59 | 0 0 0 0")]
    [InlineData("2 ^ logical([1 0; 0 1])", "double | 2 2 | 1 | 2 0 0 2 | 0 0 0 0")]
    public void AScalarRaisedToAMatrixIsAMatrixFunction(string expression, string expected) =>
        Assert.Equal(expected, Shape(expression));

    [Theory]

    // The six rows whose answers agree with the matrix exponential's only to a rounding. The two
    // at the top differ in the last bits of an entry that is itself roundoff against an exact
    // zero. The four below them have a negative base, where JGraph raises a whole eigenvalue with
    // Math.Pow and gets an exactly real number, and expm(log(s) * A) carries log(-2)'s iπ through
    // the whole evaluation and leaves about 1e-15 of imaginary dust on answers that are real —
    // R2025b's own '^' agrees with JGraph on the first two being real. The second column is what
    // R2025b printed for expm(log(s) * A), to seventeen digits.
    [InlineData("2 ^ ([4 1 1; 0 4 1; 0 0 4] + 1e-13 * ones(3))", "[16.00000000000199 11.090354888961697 14.933979000308021;1.493397900030653e-12 16.00000000000199 11.090354888961697;1.109035488896058e-12 1.4933979000306528e-12 16.00000000000199]", "double | 3 3 | 1 | 1")]
    [InlineData("2 ^ ([1 2; 3 4] * 1e-6)", "[1.0000006931488621 1.3862967633879576e-06;2.0794451450819362e-06 1.0000027725940073]", "double | 2 2 | 1 | 1")]
    [InlineData("(-2) ^ [2 0; 0 4]", "[4-9.7971743931788257e-16i 0;0 15.999999999999998-7.8377395145430589e-15i]", "double | 2 2 | 1 | 1")]
    [InlineData("(-2) ^ [1 0; 0 2]", "[-2+2.4492935982947064e-16i 0;0 4-9.7971743931788257e-16i]", "double | 2 2 | 1 | 1")]
    [InlineData("(-2) ^ [2 1; 0 2]", "[4-9.7971743931788257e-16i 2.7725887222397843+12.566370614359172i;0 4-9.7971743931788257e-16i]", "double | 2 2 | 0 | 1")]
    [InlineData("(-1) ^ [1 2; 3 4]", "[0.20396341776470883-0.9205773851866248i -0.27195122368627828-1.6874180086617083e-16i;-0.40792683552941733-1.7778016484833621e-16i -0.20396341776470855-0.92057738518662502i]", "double | 2 2 | 0 | 1")]
    public void TheDustAgreesOnlyToRounding(string expression, string matlab, string expected) =>
        Assert.Equal(expected, Close(expression, matlab));

    [Theory]

    // The closed form itself, written out rather than pasted: a Jordan block of size n contributes
    // f(λ), f'(λ), f''(λ)/2! ... along its superdiagonals, and for f(x) = s^x the k-th derivative
    // is (ln s)^k · s^x. This is the assertion that would have caught the old answer, and the one
    // that catches an eigendecomposition standing in for a matrix function: R2025b answers every
    // row here with the diagonal alone.
    [InlineData("2 ^ [2 1; 0 2]", "[4 4*log(2); 0 4]")]
    [InlineData("3 ^ [2 1; 0 2]", "[9 9*log(3); 0 9]")]
    [InlineData("2 ^ [1 1; 0 1]", "[2 2*log(2); 0 2]")]
    [InlineData("2 ^ [0 1; 0 0]", "[1 log(2); 0 1]")]
    [InlineData("2 ^ [2 5; 0 2]", "[4 5*4*log(2); 0 4]")]
    [InlineData("0.5 ^ [2 1; 0 2]", "[0.25 0.25*log(0.5); 0 0.25]")]
    [InlineData("2 ^ [3 1 0; 0 3 1; 0 0 3]",
        "8 * [1 log(2) log(2)*log(2)/2; 0 1 log(2); 0 0 1]")]
    [InlineData("2 ^ [0 1 0; 0 0 1; 0 0 0]",
        "[1 log(2) log(2)*log(2)/2; 0 1 log(2); 0 0 1]")]
    [InlineData("3 ^ gallery('jordbloc', 4, 1.5)",
        "3^1.5 * [1 log(3) log(3)^2/2 log(3)^3/6; 0 1 log(3) log(3)^2/2; "
        + "0 0 1 log(3); 0 0 0 1]")]
    public void ADefectiveExponentIsTheJordanFormAnswer(string expression, string jordan) =>
        Assert.Equal("1", Run(
            $"v = {expression};\nj = {jordan};\n" +
            "fprintf('%d', norm(v - j, 'fro') <= 1e-13 * max(1, norm(j, 'fro')));"));

    [Theory]

    // s^A is expm(log(s) * A) for a positive base, and JGraph's expm reaches the same number by a
    // different road — scaling and squaring rather than Schur-Parlett — so this holds the two
    // against each other with nothing pasted in between. It is also the check that runs in all
    // four lanes: an eigensolver's columns differ between them, and neither of these paths uses
    // eigenvectors at all.
    [InlineData("2", "[1 2; 3 4]")]
    [InlineData("2", "[2 1; 0 2]")]
    [InlineData("3", "[3 1 0; 0 3 1; 0 0 3]")]
    [InlineData("0.5", "[1 1; 0 1]")]
    [InlineData("2", "[0 -1; 1 0]")]
    [InlineData("2", "(magic(4)/34)")]
    [InlineData("2", "hilb(3)")]
    [InlineData("7", "[2 0 0; 0 3 0; 0 0 5]")]
    [InlineData("2", "[1 1; -1 3]")]
    [InlineData("1.5", "[2 -1; 1 2]")]
    public void TheAnswerIsTheMatrixExponentialOfTheScaledExponent(string scalar, string matrix) =>
        Assert.Equal("1", Run(
            $"v = {scalar} ^ {matrix};\nm = expm(log({scalar}) * {matrix});\n" +
            "fprintf('%d', norm(v - m, 'fro') <= 1e-12 * max(1, norm(m, 'fro')));"));

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

    // A one raised to anything is the identity, and a logical scalar is the one it holds.
    [InlineData("2 ^ true", "double | 1 1 | 1 | 2 | 0")]
    [InlineData("1 ^ [1 2; 3 4]", "double | 2 2 | 1 | 1 0 0 1 | 0 0 0 0")]
    [InlineData("true ^ [1 2; 3 4]", "double | 2 2 | 1 | 1 0 0 1 | 0 0 0 0")]
    public void TheOtherFormsOfTheOperatorAreUntouched(string expression, string expected) =>
        Assert.Equal(expected, Shape(expression));

    [Theory]

    // A base or an entry that is not finite has no matrix function to speak of, and the answer is
    // whatever the arithmetic makes of an infinity meeting a zero. All six come back real, which
    // is the part worth holding: a base that does not leave the reals must not make the answer
    // complex on its way through a complex Schur form. The last three are R2025b's answers too;
    // the first two are not, and ADR 0146 records why.
    [InlineData("0 ^ [1 2; 3 4]", "double | 2 2 | 1 | NaN NaN NaN NaN | 0 0 0 0")]
    [InlineData("Inf ^ [1 2; 3 4]", "double | 2 2 | 1 | NaN NaN NaN NaN | 0 0 0 0")]
    [InlineData("NaN ^ [1 2; 3 4]", "double | 2 2 | 1 | NaN NaN NaN NaN | 0 0 0 0")]
    [InlineData("2 ^ [Inf 0; 0 1]", "double | 2 2 | 1 | NaN NaN NaN NaN | 0 0 0 0")]
    [InlineData("2 ^ [NaN 0; 0 1]", "double | 2 2 | 1 | NaN NaN NaN NaN | 0 0 0 0")]
    [InlineData("2 ^ [1 Inf; 0 1]", "double | 2 2 | 1 | NaN NaN NaN NaN | 0 0 0 0")]
    public void ABaseThatIsNotFiniteAnswersRealNaNs(string expression, string expected) =>
        Assert.Equal(expected, Shape(expression));

    [Theory]

    // A single operand on either side makes the answer single, as it does for every other
    // arithmetic operator. The values agree with R2025b to six figures and no further: MATLAB
    // carries the evaluation itself in single precision, and JGraph rounds a double one at the
    // end. The last row is not R2025b's answer at all — it is the true one, in single.
    [InlineData("2 ^ single([1 2; 3 4])", "single | 2 2 | 1 | 10.4827 21.2278 14.1519 31.7106 | 0 0 0 0")]
    [InlineData("single(2) ^ [1 2; 3 4]", "single | 2 2 | 1 | 10.4827 21.2278 14.1519 31.7106 | 0 0 0 0")]
    [InlineData("single(2) ^ single([1 2; 3 4])", "single | 2 2 | 1 | 10.4827 21.2278 14.1519 31.7106 | 0 0 0 0")]
    [InlineData("single(2) ^ [1 0; 0 3]", "single | 2 2 | 1 | 2 0 0 8 | 0 0 0 0")]
    [InlineData("2 ^ single([0 -1; 1 0])", "single | 2 2 | 1 | 0.769239 0.638961 -0.638961 0.769239 | 0 0 0 0")]
    [InlineData("2 ^ single([2 1; 0 2])", "single | 2 2 | 1 | 4 0 2.77259 4 | 0 0 0 0")]
    public void ASingleOperandMakesTheAnswerSingle(string expression, string expected) =>
        Assert.Equal(expected, Rounded(expression));

    /// <summary>An empty exponent is an empty answer, and keeps the 0-by-0 shape it was given.</summary>
    [Fact]
    public void AnEmptyMatrixRaisesToAnEmptyAnswer() => Assert.Equal(
        "double | 0 0 | 1 | 0",
        Run("v = 2 ^ zeros(0, 0);\n" +
            "fprintf('%s | %d %d | %d | %d', class(v), size(v, 1), size(v, 2), isreal(v), numel(v));"));

    [Theory]

    // A matrix that is not square is not an exponent, and neither is a pair of matrices. All six
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

    // An operand with pages is refused for having them, which MATLAB says in a sentence of its
    // own — not the one above about a shape. It is the same complaint on either side of the
    // operator, so `A ^ p` draws it too. `2 ^ ones(2, 2, 2)` used to answer a 2-by-2, having
    // quietly dropped the third dimension on its way to the elementwise power.
    [InlineData("x = 2 ^ ones(2, 2, 2);")]
    [InlineData("x = 2 ^ ones(2, 2, 3);")]
    [InlineData("x = 2 ^ ones(2, 3, 4);")]
    [InlineData("x = 2 ^ ones(1, 1, 2);")]
    [InlineData("x = 2 ^ reshape(1:8, 2, 2, 2);")]
    [InlineData("x = ones(2, 2, 2) ^ 2;")]
    [InlineData("x = reshape(1:8, 2, 2, 2) ^ 0;")]
    public void AnOperandWithPagesIsRefused(string code) => Assert.Contains(
        "Arguments must be 2-D.", Failure(code));

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
