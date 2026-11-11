using JGraph.Api;
using JGraph.Numerics;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The every-row plan's I4 (ADR 0329): <c>sqrtm</c> and <c>logm</c> over the Schur form,
/// <c>pinv</c> as one product, and <c>funm</c>'s block products through the backend. The small
/// expectations are R2025b's own answers, printed to seventeen digits by a probe; the large ones
/// are residuals and the agreement of one thread with many.
/// </summary>
[Collection("JG facade")]
public class MatrixRootsI4Tests : IDisposable
{
    public MatrixRootsI4Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private sealed record Run(string Output, string Errors, bool Success, string? Message);

    private static Run RunScript(string code, int? threads = null)
    {
        int degree = ParallelKernels.MaxDegree;
        if (threads is { } wanted)
        {
            ParallelKernels.MaxDegree = wanted;
        }

        try
        {
            JG.Reset();
            var output = new RecordingScriptOutput();
            var context = new ScriptContext(output, (_, _) => { }, null);
            ScriptRunResult result = JgsRunner.Run(
                code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
            return new Run(output.NormalText, output.ErrorText, result.Success, result.Message);
        }
        finally
        {
            ParallelKernels.MaxDegree = degree;
        }
    }

    private static Run Asserts(string code)
    {
        Run run = RunScript(code);
        Assert.True(run.Success, run.Message + run.Errors);
        return run;
    }

    /// <summary>A nonsymmetric matrix with complex eigenvalues, the benchmark's own construction.</summary>
    private const string Family = """
        phi = 0.618033988749895;
        n = 150;
        M = reshape(mod((1:n*n)*phi, 1) - 0.5, n, n);
        G = M / n + eye(n);
        S = (M + M.') / 2 + n*eye(n);
        """;

    // --- sqrtm -----------------------------------------------------------------------------------

    [Fact]
    public void TheRootOfASmallMatrixIsR2025bs() => Asserts("""
        close = @(X, Y) max(max(abs(X - Y))) < 1e-13;
        assert(isequal(sqrtm([4 1; 0 9]), [2 0.2; 0 3]));
        assert(isequal(sqrtm(diag([4 9 16])), diag([2 3 4])));
        R = sqrtm([0 -1; 1 0]);
        assert(isreal(R));
        assert(close(R, [0.70710678118654746 -0.70710678118654757; 0.70710678118654757 0.70710678118654746]));
        R = sqrtm([2 1; 1 2]);
        assert(isreal(R) && isequal(R, R.'));
        assert(close(R, [1.3660254037844384 0.3660254037844386; 0.3660254037844386 1.3660254037844384]));
        R = sqrtm([4 1 2; 0.5 3 1; 1 0.2 5]);
        assert(isreal(R));
        assert(close(R, [1.9639823243406138 0.26705396619459321 0.46386303354346786; ...
            0.12014700236477852 1.7203433244279236 0.2402940047295582; ...
            0.23862150713799229 0.034678820213393857 2.2092938218448648]));
        assert(isequal(size(sqrtm([])), [0 0]));
        assert(sqrtm(9) == 3);
        """);

    [Fact]
    public void ANegativeEigenvalueMakesTheRootComplex() => Asserts("""
        close = @(X, Y) max(max(abs(X - Y))) < 1e-13;
        R = sqrtm([1 2; 3 4]);
        assert(~isreal(R));
        assert(close(R, [0.5536885671459113+0.46439416283907081i 0.80696072701321653-0.21242647876640208i; ...
            1.2104410905198248-0.31863971814960307i 1.7641296576657364+0.14575444468946769i]));
        R = sqrtm([1 2; 2 1]);
        assert(close(R, [0.86602540378443849+0.5i 0.86602540378443849-0.5i; ...
            0.86602540378443849-0.5i 0.86602540378443849+0.5i]));
        assert(sqrtm(-4) == 2i);
        """);

    [Fact]
    public void ASingularMatrixIsWarnedAboutAndAnswered()
    {
        Run run = Asserts("""
            assert(isequal(sqrtm(zeros(2)), zeros(2)));
            assert(isequal(sqrtm([0 0; 0 1]), [0 0; 0 1]));
            assert(isequal(sqrtm([0 1; 0 0]), [0 Inf; 0 0]));
            """);
        Assert.Equal(3, run.Errors.Split("Matrix is singular and may not have a square root.").Length - 1);
        Assert.DoesNotContain("singular", Asserts("R = sqrtm([4 1; 0 9]);").Errors);
    }

    [Fact]
    public void ARootOfSomethingNotFiniteIsRefused() => Asserts("""
        caught = '';
        try
            sqrtm([1 NaN; 0 1]);
        catch err
            caught = err.identifier;
        end
        assert(strcmp(caught, 'MATLAB:sqrtm:inputMustBeFinite'), ['got: ' caught]);
        caught = '';
        try
            sqrtm([1 Inf; 2 1]);
        catch err
            caught = err.identifier;
        end
        assert(strcmp(caught, 'MATLAB:sqrtm:inputMustBeFinite'), ['got: ' caught]);
        """);

    [Fact]
    public void TheRootOfALargeMatrixSquaresBack() => Asserts(Family + """
        R = sqrtm(G);
        assert(isreal(R));
        assert(max(max(abs(R*R - G))) < 1e-13);
        R = sqrtm(S);
        assert(isreal(R) && isequal(R, R.'));
        assert(max(max(abs(R*R - S))) / n < 1e-13);
        T = triu(G) + triu(M, 1);               % triangular: no Schur form at all
        R = sqrtm(T);
        assert(isequal(R, triu(R)));
        assert(max(max(abs(R*R - T))) < 1e-13);
        """);

    /// <summary>
    /// Sixty by sixty with twenty negative eigenvalues and a conjugate pair, turned by an orthogonal
    /// matrix so that nothing about it is triangular: the sums are R2025b's.
    /// </summary>
    [Fact]
    public void AMatrixWithEigenvaluesOnBothSidesAnswersWhatR2025bDoes() => Asserts("""
        n = 60;
        X = reshape(sin((1:n*n).^1.3), n, n);
        [Q, ~] = qr(X);
        d = [-(1:20)/10, (1:40)/8];
        B = diag(d) + 0.05*triu(reshape(cos(1:n*n), n, n), 1);
        B(41, 42) = 1; B(42, 41) = -1;
        A = Q*B*Q';
        R = sqrtm(A);
        assert(~isreal(R));
        assert(max(max(abs(R*R - A))) < 1e-12);
        assert(abs(sum(sum(real(R))) - 74.0420388431) < 1e-8);
        assert(abs(sum(sum(imag(R))) - 1.24229912125) < 1e-8);
        L = logm(A);
        assert(~isreal(L));
        assert(abs(sum(sum(real(L))) - 25.9945787896) < 1e-8);
        assert(abs(sum(sum(imag(L))) - 6.19437272113) < 1e-8);
        P = Q*(B + 3*eye(n))*Q';
        R = sqrtm(P);
        L = logm(P);
        assert(isreal(R) && isreal(L));
        assert(max(max(abs(R*R - P))) < 1e-12);
        assert(abs(sum(R(:)) - 128.075438051) < 1e-7);
        assert(abs(sum(L(:)) - 90.7899331341) < 1e-8);
        assert(max(max(abs(expm(L) - P))) < 1e-11);
        """);

    // --- logm ------------------------------------------------------------------------------------

    [Fact]
    public void TheLogarithmOfASmallMatrixIsR2025bs()
    {
        Run run = Asserts("""
            close = @(X, Y) max(max(abs(X - Y))) < 1e-13;
            assert(close(logm([1 2; 0 3]), [0 1.0986122886681096; 0 1.0986122886681096]));
            L = logm([0 -1; 1 0]);
            assert(isreal(L));
            assert(close(L, [0 -1.5707963267948966; 1.5707963267948966 0]));
            L = logm([2 1; 1 2]);
            assert(isreal(L) && isequal(L, L.'));
            assert(close(L, 0.54930614433405467 * ones(2)));
            L = logm([4 1 2; 0.5 3 1; 1 0.2 5]);
            assert(isreal(L));
            assert(close(L, [1.3152796003080638 0.28974708584073516 0.43333462327990446; ...
                0.11703362893911674 1.0742523639345152 0.2340672578782344; ...
                0.23058726863057738 0.01897353759439635 1.5597868259292667]));
            L = logm([5 1 0 2; -1 4 1 0; 0.5 0 6 1; 1 -2 0 3]);
            assert(isreal(L));
            assert(close(L, [1.5971990823348048 0.35826450525451092 -0.037037116508265772 0.52267591100280297; ...
                -0.23268739741035255 1.4287348766517449 0.19739572232178976 0.041089916443030838; ...
                0.075564585876630966 0.043397274958096033 1.7864892047870595 0.21360692206085641; ...
                0.18929012245225624 -0.61757606078852889 0.06191583321222887 1.0354596548563519]));
            assert(isequal(logm(eye(3)), zeros(3)));
            assert(logm(1) == 0);
            assert(all(all(isnan(logm([1 NaN; 0 1])))));
            """);
        Assert.DoesNotContain("Warning", run.Errors);
    }

    [Fact]
    public void ANonPositiveEigenvalueIsWarnedAboutAndAnswered()
    {
        Run run = Asserts("""
            close = @(X, Y) max(max(abs(X - Y))) < 1e-13;
            L = logm([1 2; 3 4]);
            assert(close(L, [-0.35043981399855378+2.3911179544517251i 0.92935120570470153-1.0937621702091036i; ...
                1.3940268085570529-1.6406432553136558i 1.0435869945584992+0.75047469913806863i]));
            L = logm([1 2; 2 1]);
            assert(close(L, [0.54930614433405467+1.5707963267948963i 0.54930614433405467-1.5707963267948963i; ...
                0.54930614433405467-1.5707963267948963i 0.54930614433405467+1.5707963267948963i]));
            assert(isequal(logm(-eye(2)), pi*1i*eye(2)));
            assert(isequal(logm(zeros(2)), [-Inf 0; 0 -Inf]));
            assert(abs(logm(-4) - (log(4) + pi*1i)) < 1e-15);
            """);
        Assert.Equal(5, run.Errors.Split("Principal matrix logarithm is not defined").Length - 1);
    }

    [Fact]
    public void TheLogarithmOfALargeMatrixUndoesTheExponential() => Asserts(Family + """
        E = expm(G);
        L = logm(E);
        assert(isreal(L));
        assert(max(max(abs(L - G))) < 1e-13);
        assert(max(max(abs(expm(L) - E))) < 1e-12);
        L = logm(S);
        assert(isreal(L) && isequal(L, L.'));
        assert(max(max(abs(expm(L) - S))) / n < 1e-12);
        W = G(1:40, 1:40) + 0.5*triu(ones(40), 1);
        assert(norm(funm(W, 'log') - logm(W)) < 1e-12);
        """);

    [Fact]
    public void OneThreadAndManyAgreeToTheLastBit()
    {
        const string code = Family + """
            R = sqrtm(G);
            L = logm(expm(G));
            fprintf('%.17g %.17g %.17g %.17g\n', R(3, 7), sum(R(:)), L(11, 2), sum(L(:)));
            """;
        Run single = RunScript(code, threads: 1);
        Run many = RunScript(code, threads: Math.Max(2, ParallelKernels.MaxDegree));
        Assert.True(single.Success, single.Message);
        Assert.True(many.Success, many.Message);
        Assert.NotEqual(string.Empty, single.Output);
        Assert.Equal(single.Output, many.Output);
    }

    // --- pinv ------------------------------------------------------------------------------------

    [Fact]
    public void ThePseudoinverseIsR2025bsAndKeepsItsFourConditions() => Asserts("""
        close = @(X, Y) max(max(abs(X - Y))) < 1e-13;
        assert(close(pinv([1 2; 2 4; 3 6]), [0.014285714285714275 0.028571428571428567 0.042857142857142851; ...
            0.02857142857142855 0.057142857142857134 0.085714285714285701]));
        assert(close(pinv([1 2 3; 4 5 6]), [-0.94444444444444509 0.44444444444444486; ...
            -0.11111111111111195 0.11111111111111152; 0.7222222222222231 -0.22222222222222265]));
        assert(isequal(pinv(zeros(2, 3)), zeros(3, 2)));
        assert(abs(pinv(4) - 0.25) < 1e-16);
        phi = 0.618033988749895;
        B = reshape(mod((1:60*30)*phi, 1), 60, 30);
        B(:, 21:30) = B(:, 1:10) * 2;           % rank 20
        P = pinv(B);
        assert(isequal(size(P), [30 60]));
        scale = max(max(abs(B)));
        assert(max(max(abs(B*P*B - B))) / scale < 1e-12);
        assert(max(max(abs(P*B*P - P))) < 1e-12);
        assert(max(max(abs((B*P).' - B*P))) < 1e-12);
        assert(max(max(abs((P*B).' - P*B))) < 1e-12);
        W = pinv(B.');
        assert(max(max(abs(W - P.'))) < 1e-12);
        """);

    // --- funm ------------------------------------------------------------------------------------

    [Fact]
    public void AFunctionOfALargeBlockAgreesWithItsOwnName() => Asserts(Family + """
        A = G(1:60, 1:60);
        assert(max(max(abs(funm(A, @exp) - expm(A)))) < 1e-12);
        B = A + diag((1:60) / 20);              % several blocks and the recurrence between them
        assert(max(max(abs(funm(B, @exp) - expm(B)))) < 1e-10);
        C = funm(B, @cos);
        Z = funm(B, @sin);
        assert(max(max(abs(C*C + Z*Z - eye(60)))) < 1e-10);
        """);
}
