using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The every-row plan's I12 through the verbs: filter with feedback over packed storage (the
/// recurrence written out in the script is the reference, to the bit, final conditions included),
/// dct/idct on both roads, and fft2 of a real matrix (exactly conjugate-symmetric, the general
/// transform to rounding).
/// </summary>
[Collection("JG facade")]
public class SignalTransformsI12Tests : IDisposable
{
    public SignalTransformsI12Tests() => JG.Reset();

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

    /// <summary>The transposed direct-form II recurrence filter has always walked, written out.</summary>
    private const string Recurrence = """

        function [y, z] = recur(b, a, x, z)
            L = max(numel(a), numel(b));
            bn = zeros(1, L); an = zeros(1, L);
            bn(1:numel(b)) = b / a(1);
            an(1:numel(a)) = a / a(1);
            S = L - 1;
            y = zeros(size(x));
            for i = 1:numel(x)
                in = x(i);
                out = bn(1)*in + z(1);
                for j = 1:S
                    if j < S
                        nxt = z(j+1);
                    else
                        nxt = 0;
                    end
                    z(j) = (bn(j+1)*in + nxt) - an(j+1)*out;
                end
                y(i) = out;
            end
        end
        """;

    [Fact]
    public void FilterWithFeedbackIsTheRecurrenceInEveryForm()
    {
        Run("""
            phi = 0.618033988749895;
            x = mod((1:3000)*phi, 1) - 0.5;
            [b, a] = butter(4, 0.3);
            sets = {{b, a}, {[0.0201 0.0402 0.0201], [1 -1.5610 0.6414]}, ...
                    {[1 2], [2 -0.5 0.1 -0.02 0.01 0.003]}, {0.5, [1 -0.9]}};
            for s = 1:numel(sets)
                bb = sets{s}{1}; aa = sets{s}{2};
                S = max(numel(aa), numel(bb)) - 1;
                [w, zw] = recur(bb, aa, x, zeros(1, S));
                assert(isequal(filter(bb, aa, x), w));
                assert(isequal(filter(bb, aa, x'), w'));
                [y, zf] = filter(bb, aa, x);
                assert(isequal(y, w) && isequal(zf(:), zw(:)));
                zi = mod((1:S)*phi*phi, 1)';
                [w2, zw2] = recur(bb, aa, x, zi');
                [y2, zf2] = filter(bb, aa, x, zi);
                assert(isequal(y2, w2) && isequal(zf2(:), zw2(:)));
            end

            % A matrix along each dimension, with conditions per slice.
            X = reshape(mod((1:600)*phi, 1) - 0.5, 100, 6);
            [Y1, Z1] = filter(b, a, X);
            [Y2, Z2] = filter(b, a, X, [], 2);
            for c = 1:6
                [w, zw] = recur(b, a, X(:, c), zeros(1, 4));
                assert(isequal(Y1(:, c), w) && isequal(Z1(:, c), zw'));
            end
            for r = 1:100
                [w, zw] = recur(b, a, X(r, :), zeros(1, 4));
                assert(isequal(Y2(r, :), w) && isequal(Z2(r, :), zw));
            end

            % A non-finite sample travels as the loop carried it.
            xn = x; xn(40) = Inf; xn(900) = NaN;
            yn = filter(b, a, xn);
            wn = recur(b, a, xn, zeros(1, 4));
            assert(isequal(isnan(yn), isnan(wn)) && isequal(yn(~isnan(yn)), wn(~isnan(wn))));
            disp('ok');
            """ + Recurrence);
    }

    [Fact]
    public void DctTakesBothRoadsAndUndoesItself()
    {
        string text = Run("""
            phi = 0.618033988749895;
            for n = [7 4095 4096 6000 8193]
                x = mod((1:n)'*phi, 1) - 0.5;
                d = dct(x);
                % Selected coefficients against the definition.
                k = unique([0 1 2 floor(n/3) floor(n/2) n-1]);
                j = (0:n-1)';
                for q = k
                    w = sqrt(2/n); if q == 0, w = sqrt(1/n); end
                    ref = w * sum(x .* cos(pi*q*(2*j + 1)/(2*n)));
                    assert(abs(d(q+1) - ref) < 1e-12, sprintf('n=%d k=%d', n, q));
                end
                r = idct(d);
                assert(max(abs(r - x)) < 1e-13);
                % A matrix's column goes through the slice road and agrees with the vector road.
                M = [x, flipud(x)];
                D = dct(M);
                assert(isequal(D(:, 1), d));
                assert(isequal(idct(D), [idct(d), idct(dct(flipud(x)))]));
                assert(isequal(dct(x'), d'));
            end
            disp('ok');
            """);
        Assert.Contains("ok", text);
    }

    [Fact]
    public void Fft2OfARealMatrixIsExactlyHermitianAndTheGeneralTransformToRounding()
    {
        string text = Run("""
            phi = 0.618033988749895;
            for sz = [256 256; 64 1024; 2048 32; 16 16; 48 64]'
                m = sz(1); n = sz(2);
                A = reshape(mod((1:m*n)*phi, 1) - 0.5, m, n);
                F = fft2(A);
                G = fft(fft(A), [], 2);
                assert(max(abs(F(:) - G(:))) <= 1e-13 * max(abs(G(:))));
                assert(isequal(F, conj(F([1 m:-1:2], [1 n:-1:2]))));
                assert(~isreal(F));
                assert(isequal(size(F), [m n]));
                B = ifft2(F);
                assert(isreal(B) && max(abs(B(:) - A(:))) < 1e-14);
            end
            disp('ok');
            """);
        Assert.Contains("ok", text);
    }
}
