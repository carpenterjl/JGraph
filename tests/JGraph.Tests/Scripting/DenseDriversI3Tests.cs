using System.Globalization;
using JGraph.Api;
using JGraph.Numerics.LinearAlgebra;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The every-row plan's I3 (ADR 0332) through the verbs: lu's outputs, chol's triangle, qr's Q
/// expanded in place, the singular values in the caller's copy, the norms read where the matrix
/// lies, expm, hess, schur and balance column-major, null and orth from the factors' own columns,
/// and polyfit's design built column-major. Each answer is held to an exact relation with another
/// verb, or to the old algorithm run beside it.
/// </summary>
[Collection("JG facade")]
public class DenseDriversI3Tests : IDisposable
{
    public DenseDriversI3Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private static string Run(string code)
    {
        JG.Reset();
        var output = new RecordingScriptOutput();
        var context = new ScriptContext(output, (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + output.NormalText + output.ErrorText);
        return output.NormalText;
    }

    /// <summary>A matrix of sin entries, deterministic and well away from singular.</summary>
    private const string Field = """

        function A = field(m, n)
            [I, J] = ndgrid(1:m, 1:n);
            A = sin(0.7*I + 1.3*J.*J/n) + 0.01*I;
        end
        """;

    [Fact]
    public void LuHandsBackTheFactorsAsTheyStandInEveryForm()
    {
        Run("""
            for n = [1 2 7 60]
                A = field(n, n) + n*eye(n);
                Y = lu(A);
                [L, U, P] = lu(A);
                assert(isequal(L, tril(Y, -1) + eye(n)));
                assert(isequal(U, triu(Y)));
                [L2, U2] = lu(A);
                assert(isequal(U2, U) && isequal(L2, P.' * L));
                [L3, U3, p] = lu(A, 'vector');
                I = eye(n);
                assert(isequal(L3, L) && isequal(U3, U) && isequal(P, I(p, :)));
            end
            """ + Field);
    }

    [Fact]
    public void LuOfAThreadedSizeHandsBackTheSameFactors()
    {
        if (!JgsPacking.Enabled)
        {
            return; // a million boxed elements is minutes of marshalling, and the road is the packed one
        }

        Run("""
            n = 1100;
            A = field(n, n) + n*eye(n);
            Y = lu(A);
            [L, U, P] = lu(A);
            assert(isequal(L, tril(Y, -1) + eye(n)));
            assert(isequal(U, triu(Y)));
            [L2, U2] = lu(A);
            [~, ~, p] = lu(A, 'vector');
            assert(isequal(U2, U) && isequal(L2(p, :), L));
            """ + Field);
    }

    [Fact]
    public void CholReadsAndWritesOnlyTheTriangleAskedFor()
    {
        Run("""
            for n = [1 3 40]
                B = field(n, n);
                S = B*B.' + n*eye(n);
                R = chol(S);
                Lo = chol(S, 'lower');
                assert(isequal(R, triu(R)) && isequal(Lo, tril(Lo)));
                assert(norm(R.'*R - S, 'fro') <= 1e-12*norm(S, 'fro'));
                assert(norm(Lo*Lo.' - S, 'fro') <= 1e-12*norm(S, 'fro'));
                % The other triangle is never read: rubbish there changes nothing.
                Su = triu(S) + tril(ones(n)*NaN, -1);
                assert(isequal(chol(Su), R));
                Sl = tril(S) + triu(ones(n)*NaN, 1);
                assert(isequal(chol(Sl, 'lower'), Lo));
                [Rf, flag] = chol(S);
                assert(flag == 0 && isequal(Rf, R));
            end
            S = [4 2 0; 2 1 0; 0 0 3];
            [Rp, flag] = chol(S);
            assert(flag == 2 && isequal(size(Rp), [1 1]) && Rp == 2);
            """ + Field);
    }

    [Fact]
    public void CholOfAThreadedSizeIgnoresTheOtherTriangle()
    {
        if (!JgsPacking.Enabled)
        {
            return;
        }

        Run("""
            n = 1100;
            B = field(n, n);
            S = B*B.'/n + n*eye(n);
            R = chol(S);
            assert(isequal(R, triu(R)));
            Su = triu(S) + tril(ones(n)*NaN, -1);
            assert(isequal(chol(Su), R));
            Lo = chol(S, 'lower');
            assert(isequal(Lo, tril(Lo)));
            assert(isequal(chol(tril(S) + triu(ones(n)*NaN, 1), 'lower'), Lo));
            """ + Field);
    }

    [Fact]
    public void QrExpandsTheSameQWhereverItIsExpanded()
    {
        Run("""
            shapes = [7 7; 30 12; 12 30; 1 1; 1 5; 5 1; 80 80];
            for s = 1:size(shapes, 1)
                m = shapes(s, 1); n = shapes(s, 2);
                A = field(m, n);
                R1 = qr(A);
                [Q, R] = qr(A);
                assert(isequal(R, R1) && isequal(size(Q), [m m]) && isequal(size(R), [m n]));
                assert(norm(Q*R - A, 'fro') <= 1e-13*max(1, norm(A, 'fro')));
                assert(norm(Q.'*Q - eye(m), 'fro') <= 1e-13*m);
                [Qe, Re] = qr(A, 'econ');
                k = min(m, n);
                assert(isequal(size(Qe), [m k]) && isequal(size(Re), [k n]));
                assert(isequal(Re, R(1:k, :)) && norm(Qe - Q(:, 1:k), 'fro') <= 1e-13*m);
                [Q0, R0, p] = qr(A, 0);
                assert(norm(Q0*R0 - A(:, p), 'fro') <= 1e-13*max(1, norm(A, 'fro')));
                [C, Rc] = qr(A, ones(m, 1));
                assert(isequal(Rc, R) && norm(C - Q.'*ones(m, 1)) <= 1e-12*m);
            end
            """ + Field);
    }

    [Fact]
    public void TheSingularValuesAndTheNormsAgreeWithEachOther()
    {
        Run("""
            for sz = [1 1; 2 3; 40 25; 25 40; 120 120; 200 300]'
                A = field(sz(1), sz(2)) - 0.3;
                s = svd(A);
                assert(norm(A) == s(1) && norm(A, 2) == s(1));
                one = 0; inf_ = 0; fro = 0;
                for c = 1:size(A, 2)
                    t = 0;
                    for r = 1:size(A, 1)
                        t = t + abs(A(r, c));
                    end
                    one = max(one, t);
                end
                for r = 1:size(A, 1)
                    t = 0;
                    for c = 1:size(A, 2)
                        t = t + abs(A(r, c));
                    end
                    inf_ = max(inf_, t);
                end
                for k = 1:numel(A)
                    fro = fro + A(k)*A(k);
                end
                if min(sz) > 1
                    assert(norm(A, 1) == one && norm(A, Inf) == inf_ && norm(A, 'inf') == inf_);
                    assert(norm(A, 'fro') == sqrt(fro));
                end
            end
            assert(norm([3 4]) == 5 && norm([1; 2; 2], Inf) == 2 && norm([3 0; 0 4], 'fro') == 5);
            assert(abs(norm([1+1i 1-1i; 0 2], 'fro') - sqrt(8)) < 1e-15);
            assert(isnan(norm([1 NaN; 2 3], 1)) && isnan(norm([1 NaN; 2 3], Inf)));
            assert(norm(zeros(3), 'fro') == 0 && norm(zeros(3), 1) == 0);
            """ + Field);
    }

    [Fact]
    public void HessAndSchurAnswerTheSameFactorWhicheverOutputsAreAsked()
    {
        Run("""
            for n = [1 2 3 9 50]
                A = field(n, n) - 0.4;
                H1 = hess(A);
                [P, H] = hess(A);
                assert(isequal(H1, H) && isequal(H, triu(H, -1)));
                assert(norm(P*H*P.' - A, 'fro') <= 1e-13*max(1, norm(A, 'fro')));
                T1 = schur(A);
                [U, T] = schur(A);
                [U2, T2] = schur(A, 'real');
                assert(isequal(T1, T) && isequal(T2, T) && isequal(U2, U));
                assert(norm(U*T*U.' - A, 'fro') <= 1e-12*max(1, norm(A, 'fro')));
                [Uc, Tc] = schur(A, 'complex');
                assert(isequal(Tc, triu(Tc)) && norm(Uc*Tc*Uc' - A, 'fro') <= 1e-12*max(1, norm(A, 'fro')));
            end
            try
                schur(magic(4), 'other');
                assert(false);
            catch err
                assert(contains(err.message, '''other'' is not ''real'' or ''complex'''));
            end
            """ + Field);
    }

    [Fact]
    public void BalanceScalesByPowersOfTwoAndExpmIsUnchanged()
    {
        Run("""
            A = [1 100 10000; 0.01 1 100; 0.0001 0.01 1];
            [T, B] = balance(A);
            t = diag(T);
            assert(isequal(T, diag(t)) && all(log2(t) == round(log2(t))));
            assert(isequal(B, (A .* t.') ./ t));
            assert(isequal(balance(A), B));
            G = field(30, 30)/30 + eye(30);
            [T2, B2] = balance(G);
            assert(isequal(B2, (G .* diag(T2).') ./ diag(T2)));
            E = expm(zeros(3));
            assert(isequal(E, eye(3)));
            assert(abs(expm(1) - exp(1)) < 2e-15);
            D = expm(diag([0 1 2]));
            assert(norm(D - diag(exp([0 1 2])), 'fro') < 1e-13);
            N = [0 1; 0 0];
            assert(isequal(expm(N), [1 1; 0 1]));
            """ + Field);
    }

    [Fact]
    public void NullAndOrthAreTheSingularFactorsOwnColumns()
    {
        Run("""
            B = reshape(mod((1:60*30)*0.618033988749895, 1), 60, 30);
            B(:, 21:30) = B(:, 1:10) * 2;
            [U, S, V] = svd(B, 'econ');
            r = rank(B);
            assert(r == 20);
            assert(isequal(orth(B), U(:, 1:r)));
            assert(isequal(null(B), V(:, r+1:end)));
            W = B.';
            [Uw, Sw, Vw] = svd(W);
            assert(isequal(null(W), Vw(:, r+1:end)));
            assert(size(orth(eye(3)), 2) == 3 && size(null(eye(3)), 1) == 3);
            """ + Field);
    }

    /// <summary>polyfit as it was before I3: the row-major design, its QR, and the column solve.</summary>
    private static (double[] Coefficients, double Normr) PolyfitAsItWas(double[] x, double[] y, int order)
    {
        int terms = order + 1;
        var design = new double[x.Length, terms];
        for (int r = 0; r < x.Length; r++)
        {
            double power = 1;
            for (int c = terms - 1; c >= 0; c--)
            {
                design[r, c] = power;
                power *= x[r];
            }
        }

        QrDecomposition qr = QrDecomposition.Factor(design);
        var target = new double[y.Length, 1];
        for (int r = 0; r < y.Length; r++)
        {
            target[r, 0] = y[r];
        }

        double[,] solved = qr.SolveColumns(target);
        var coefficients = new double[terms];
        for (int i = 0; i < terms; i++)
        {
            coefficients[i] = solved[i, 0];
        }

        double residual = 0;
        for (int r = 0; r < y.Length; r++)
        {
            double fitted = 0;
            for (int c = 0; c < terms; c++)
            {
                fitted += design[r, c] * coefficients[c];
            }

            residual += (y[r] - fitted) * (y[r] - fitted);
        }

        return (coefficients, Math.Sqrt(residual));
    }

    private void AssertPolyfitAsItWas(int n, int order)
    {
        var x = new double[n];
        var y = new double[n];
        for (int k = 0; k < n; k++)
        {
            x[k] = (k + 1) / (double)n;
            y[k] = (3 * x[k]) - (x[k] * x[k]);
        }

        (double[] expected, double normr) = PolyfitAsItWas(x, y, order);
        string text = Run($"""
            n = {n};
            x = (1:n)/n;
            y = 3*x - x.*x;
            p = polyfit(x, y, {order});
            [p2, S] = polyfit(x, y, {order});
            assert(isequal(p, p2));
            fprintf('%.17g\n', p);
            fprintf('normr %.17g\n', S.normr);
            """);
        string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        double[] got = lines.Take(order + 1).Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(expected, got);
        Assert.Equal(normr, double.Parse(lines[order + 1]["normr ".Length..], CultureInfo.InvariantCulture));
    }

    [Fact]
    public void PolyfitIsTheFitItWasOnSmallData() => AssertPolyfitAsItWas(50, 3);

    [Fact]
    public void PolyfitIsTheFitItWasOnALongThinDesign()
    {
        if (!JgsPacking.Enabled)
        {
            return;
        }

        AssertPolyfitAsItWas(500_000, 8);
    }

    [Fact]
    public void PolyfitStillRefusesWhatItRefused()
    {
        Run("""
            try
                polyfit([1 2], [1 2 3], 1);
                assert(false);
            catch err
                assert(contains(err.message, 'same length'));
            end
            [p, S, mu] = polyfit(1:10, (1:10).^2, 2);
            assert(numel(p) == 3 && numel(mu) == 2 && S.df == 7 && abs(mu(1) - 5.5) < 1e-15);
            """);
    }
}
