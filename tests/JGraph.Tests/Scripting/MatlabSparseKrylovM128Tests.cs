using JGraph.Api;
using JGraph.Numerics;
using JGraph.Numerics.Sparse;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The Krylov solvers and the rest of <c>sparfun</c> (M128): the eleven iterative methods, their
/// flags and iteration counts, <c>svds</c>, the diagonal and pattern verbs, and the graph layouts.
/// </summary>
/// <remarks>
/// The counts pinned here are R2025b's, recorded by <c>m128_sparse.m</c>. They are the strong claim
/// of the milestone: an iteration count is exact only if the residual norm, the convergence test,
/// the re-test against <c>b - A*x</c> and the stagnation rule all match, so a wrong rule anywhere
/// shows up as a number one out rather than as a slightly different answer.
/// </remarks>
[Collection("JG facade")]
public class MatlabSparseKrylovM128Tests : IDisposable
{
    private RecordingScriptOutput _output = new();

    public MatlabSparseKrylovM128Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    /// <summary>The 5-point Laplacian on a 5-by-5 grid: <c>gallery('poisson', 5)</c>, built by hand.</summary>
    private const string Poisson = """
        n = 5; N = n*n;
        i = []; j = []; s = [];
        for c = 1:n
            for r = 1:n
                k = (c-1)*n + r;
                i(end+1) = k; j(end+1) = k; s(end+1) = 4;
                if r > 1, i(end+1) = k; j(end+1) = k-1; s(end+1) = -1; end
                if r < n, i(end+1) = k; j(end+1) = k+1; s(end+1) = -1; end
                if c > 1, i(end+1) = k; j(end+1) = k-n; s(end+1) = -1; end
                if c < n, i(end+1) = k; j(end+1) = k+n; s(end+1) = -1; end
            end
        end
        A = sparse(i, j, s, N, N);
        b = ones(N, 1);

        """;

    private string Run(string code)
    {
        _output = new RecordingScriptOutput();
        var context = new ScriptContext(_output, (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(
            code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText;
    }

    private string Refuses(string code)
    {
        _output = new RecordingScriptOutput();
        var context = new ScriptContext(_output, (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(
            code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.False(result.Success, "expected a refusal");
        return result.Message ?? string.Empty;
    }

    [Fact]
    public void EverySolverStopsWhereMatlabStopsOnThePoissonMatrix() =>
        Assert.Equal(
            "pcg 0 5|bicg 0 5|bicgstab 0 4.5|bicgstabl 0 2.25|cgs 0 5|"
            + "minres 0 5|qmr 0 5|symmlq 0 4|tfqmr 0 4|lsqr 0 5|",
            Run(Poisson + """
                names = {'pcg', 'bicg', 'bicgstab', 'bicgstabl', 'cgs', 'minres', 'qmr', 'symmlq', 'tfqmr', 'lsqr'};
                for k = 1:numel(names)
                    f = str2func(names{k});
                    [~, flag, ~, iter] = f(A, b, 1e-10, 40);
                    fprintf('%s %d %g|', names{k}, flag, iter);
                end
                """));

    [Fact]
    public void GmresReportsAnOuterAndAnInnerIteration() =>
        Assert.Equal(
            "full 0 [1 5]|restart5 0 [1 5]|restart3 1 [2 3]|",
            Run(Poisson + """
                [~, f1, ~, i1] = gmres(A, b, [], 1e-10, 25);
                fprintf('full %d [%d %d]|', f1, i1(1), i1(2));
                [~, f2, ~, i2] = gmres(A, b, 5, 1e-10, 10);
                fprintf('restart5 %d [%d %d]|', f2, i2(1), i2(2));
                [~, f3, ~, i3] = gmres(A, b, 3, 1e-14, 2);
                fprintf('restart3 %d [%d %d]|', f3, i3(1), i3(2));
                """));

    [Fact]
    public void ARunThatDoesNotConvergeReportsFlagOneAndItsBestIterate() =>
        Assert.Equal(
            "1 3 0.1358697177|",
            Run(Poisson + """
                [~, flag, relres, iter] = pcg(A, b, 1e-14, 3);
                fprintf('%d %d %.11g|', flag, iter, relres);
                """));

    [Fact]
    public void AnIncompleteCholeskyPreconditionerCutsTheIterationCount() =>
        Assert.Equal(
            "plain 5|ichol 10 0|",
            Run(Poisson + """
                [~, ~, ~, i0] = pcg(A, b, 1e-10, 40);
                fprintf('plain %d|', i0);
                L = ichol(A);
                [~, flag, ~, i1] = pcg(A, b, 1e-10, 40, L, L');
                fprintf('ichol %d %d|', i1, flag);
                """));

    [Fact]
    public void AZeroRightHandSideIsAnsweredWithoutIterating() =>
        Assert.Equal(
            "0 0 0 0|",
            Run(Poisson + """
                [x, flag, relres, iter] = pcg(A, zeros(25, 1));
                fprintf('%d %g %d %g|', flag, relres, iter, norm(x));
                """));

    [Fact]
    public void AGoodEnoughInitialGuessIsReturnedUntouched() =>
        Assert.Equal(
            "0 0 1|",
            Run(Poisson + """
                x0 = A \ b;
                [x, flag, relres, iter] = pcg(A, b, 1e-1, 10, [], [], x0);
                fprintf('%d %d %d|', flag, iter, isequal(x, x0));
                """));

    [Fact]
    public void AFunctionHandleStandsInForTheMatrix() =>
        Assert.Equal(
            "0 5 1|",
            Run(Poisson + """
                [x1, ~, ~, ~] = pcg(A, b, 1e-10, 40);
                [x2, flag, ~, iter] = pcg(@(v) A*v, b, 1e-10, 40);
                fprintf('%d %d %d|', flag, iter, double(norm(x1 - x2) < 1e-12));
                """));

    /// <summary>
    /// The three solvers that need <c>A'</c> call their handle with a trailing direction flag, and
    /// a handle written without it is a refusal rather than a wrong answer.
    /// </summary>
    [Fact]
    public void BicgAsksItsHandleForBothDirections()
    {
        Assert.Equal(
            "0 5|",
            Run(Poisson + """
                function y = both(A, v, flag)
                    if strcmp(flag, 'transp')
                        y = A' * v;
                    else
                        y = A * v;
                    end
                end
                [~, flag, ~, iter] = bicg(@(v, t) both(A, v, t), b, 1e-10, 40);
                fprintf('%d %d|', flag, iter);
                """));

        Assert.Contains("2", Refuses(Poisson + "bicg(@(v) A*v, b, 1e-10, 40);"));
    }

    [Fact]
    public void SpdiagsPutsDiagonalsInAndTakesThemOut() =>
        Assert.Equal(
            "11 22 0 0 0 0 0 12 23 0 0 0 1 0 13 24 0 0 0 2 0 14 25 0 0 0 3 0 15 26 0 0 0 4 0 16 |"
            + "-2 0 1 |1 11 0 2 12 22 3 13 23 4 14 24 0 15 25 0 16 26 |",
            Run("""
                A = spdiags([(1:6)' (11:16)' (21:26)'], [-2 0 1], 6, 6);
                F = full(A);
                for r = 1:6
                    for c = 1:6
                        fprintf('%g ', F(r, c));
                    end
                end
                fprintf('|');
                [B, d] = spdiags(A);
                fprintf('%g ', d(1), d(2), d(3));
                fprintf('|');
                for r = 1:6
                    for c = 1:3
                        fprintf('%g ', B(r, c));
                    end
                end
                fprintf('|');
                """));

    /// <summary>Which end of a short diagonal is padded depends on the matrix's shape, not on the diagonal.</summary>
    [Fact]
    public void ATallAndAWideMatrixReadTheirDiagonalsFromOppositeEnds() =>
        Assert.Equal(
            "wide 1 11 2 12 3 13 4 14|tall 1 11 2 12 3 13 4 14|",
            Run("""
                W = spdiags([(1:4)' (11:14)'], [0 2], 4, 6);
                fprintf('wide');
                for i = 1:4
                    fprintf(' %g %g', W(i, i), W(i, i + 2));
                end
                fprintf('|tall');
                T = spdiags([(1:4)' (11:14)'], [0 -2], 6, 4);
                for i = 1:4
                    fprintf(' %g %g', T(i, i), T(i + 2, i));
                end
                fprintf('|');
                """));

    [Fact]
    public void SpfunLeavesTheZerosAlone() =>
        Assert.Equal(
            "4 1 2 3 4|",
            Run("""
                S = sparse([1 2 3 1], [1 2 3 3], [1 4 9 16], 3, 3);
                F = spfun(@sqrt, S);
                fprintf('%d %g %g %g %g|', nnz(F), F(1,1), F(2,2), F(3,3), F(1,3));
                """));

    [Fact]
    public void StructuralRankCountsTheLargestMatchingInThePattern() =>
        Assert.Equal(
            "25 3 1 0|",
            Run(Poisson + """
                R = sparse([1 2 3 4 5 6 1 3 5], [1 1 2 2 3 3 3 1 2], [1 2 3 4 5 6 7 8 9], 6, 3);
                fprintf('%d %d %d %d|', sprank(A), sprank(R), ...
                    sprank(sparse([1 2], [1 1], [1 1], 3, 3)), sprank(sparse(4, 4)));
                """));

    [Fact]
    public void SvdsFindsARepeatedSingularValueTwice() =>
        Assert.Equal(
            "1 1 1|",
            Run(Poisson + """
                s = svds(A, 3);
                d = svd(full(A));
                fprintf('%d %d %d|', double(abs(s(1) - d(1)) < 1e-12), ...
                    double(abs(s(2) - d(2)) < 1e-12), double(abs(s(3) - d(3)) < 1e-12));
                """));

    [Fact]
    public void SvdsAnswersTheSmallEndDescendingToo() =>
        Assert.Equal(
            "1 1 1|",
            Run(Poisson + """
                s = svds(A, 3, 'smallest');
                d = svd(full(A));
                fprintf('%d %d %d|', double(abs(s(1) - d(23)) < 1e-10), ...
                    double(abs(s(2) - d(24)) < 1e-10), double(abs(s(3) - d(25)) < 1e-10));
                """));

    /// <summary>
    /// A parent vector whose parents are numbered below their children is renumbered first and the
    /// answer permuted back — MATLAB's own <c>treeplot</c> example is written that way.
    /// </summary>
    [Fact]
    public void TreelayoutAcceptsAParentVectorThatIsNotInEliminationOrder() =>
        Assert.Equal(
            "0.5 0.75 0.25 2 1|",
            Run("""
                [x, y, h, s] = treelayout([2 4 2 0 6 4 6]);
                fprintf('%g %g %g %d %d|', x(4), y(4), y(1), h, s);
                """));

    [Fact]
    public void GplotOrdersItsEdgesByTheirLargerEndpoint() =>
        Assert.Equal(
            "24 1 0 NaN 0 1 NaN |",
            Run("""
                A = sparse([1 2 3 1], [2 3 4 4], 1, 4, 4);
                A = A + A';
                xy = [0 0; 1 0; 1 1; 0 1];
                [X, Y] = gplot(A, xy);
                fprintf('%d ', numel(X));
                for k = 1:6
                    fprintf('%g ', X(k));
                end
                fprintf('|');
                """));

    [Fact]
    public void UnmeshTurnsAnEdgeListIntoALaplacian() =>
        Assert.Equal(
            "4 14 10 0|",
            Run("""
                E = [0 0 1 0; 1 0 1 1; 1 1 0 1; 0 1 0 0; 0 0 1 1];
                [A, xy] = unmesh(E);
                fprintf('%d %d %g %g|', size(A, 1), nnz(A), full(sum(diag(A))), ...
                    max(abs(full(sum(A, 2)))));
                """));

    [Fact]
    public void SpaugmentBuildsTheLeastSquaresSystem() =>
        Assert.Equal(
            "9 2 0|",
            Run("""
                R = sparse([1 2 3 4 5 6], [1 1 2 2 3 3], [1 2 3 4 5 6], 6, 3);
                S = spaugment(R, 2);
                fprintf('%d %g %g|', size(S, 1), S(1, 1), S(9, 9));
                """));

    [Fact]
    public void ASolverNamesWhatItRefuses()
    {
        Assert.Contains("square", Refuses("pcg(sparse(3, 4), ones(3, 1));"));
        Assert.Contains("column", Refuses(Poisson + "pcg(A, ones(3, 1));"));
    }

    /// <summary>
    /// The row-blocked matrix–vector product answers what a single thread answers, to the bit. A
    /// row's dot product is one serial accumulation and the split never runs through one, so this
    /// is a structural promise rather than a lucky one — and it is the promise that lets a Krylov
    /// iteration count be reproducible on a machine of any width.
    /// </summary>
    [Fact]
    public void TheThreadedMatrixVectorProductIsBitIdenticalToTheSerialOne()
    {
        var random = new Random(4711);
        // Enough stored entries to cross the threshold that turns the row blocks into threads.
        int n = 8000;
        var triplets = new List<(int, int, double)>();
        for (int r = 0; r < n; r++)
        {
            for (int k = 0; k < 300; k++)
            {
                triplets.Add((r, random.Next(n), (random.NextDouble() - 0.5) * 1e3));
            }
        }

        CscMatrix matrix = CscMatrix.FromTriplets(n, n, triplets);
        var x = new double[n];
        for (int i = 0; i < n; i++)
        {
            x[i] = random.NextDouble() - 0.5;
        }

        int degree = ParallelKernels.MaxDegree;
        try
        {
            ParallelKernels.MaxDegree = 1;
            double[] serial = matrix.MultiplyVector(x);
            ParallelKernels.MaxDegree = Math.Max(2, degree);
            double[] threaded = matrix.MultiplyVector(x);
            for (int i = 0; i < n; i++)
            {
                Assert.Equal(BitConverter.DoubleToInt64Bits(serial[i]),
                    BitConverter.DoubleToInt64Bits(threaded[i]));
            }
        }
        finally
        {
            ParallelKernels.MaxDegree = degree;
        }
    }
}
