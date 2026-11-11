using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The every-row plan's I8 through the verbs: conv into packed storage, interp1's packed query
/// road and its already-sorted knots, interp2's packed pairs, axes read from a packed grid and the
/// threaded slope tables, and interpft's real answer. Each answer is held to the general road or to
/// the old algorithm written out in the script, exactly.
/// </summary>
[Collection("JG facade")]
public class InterpolationConvolutionI8Tests : IDisposable
{
    public InterpolationConvolutionI8Tests() => JG.Reset();

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

    /// <summary>The tap loop conv used to run, written out: each tap's scaled copy added in turn.</summary>
    private const string TapLoop = """

        function w = taploop(a, b)
            if numel(a) <= numel(b)
                taps = a(:).'; signal = b(:).';
            else
                taps = b(:).'; signal = a(:).';
            end
            w = zeros(1, numel(a) + numel(b) - 1);
            n = numel(signal);
            for t = 1:numel(taps)
                if taps(t) ~= 0
                    w(t:t+n-1) = w(t:t+n-1) + taps(t)*signal;
                end
            end
        end
        """;

    [Fact]
    public void ConvIsTheTapLoopInEveryShapeAndOrientation()
    {
        Run("""
            phi = 0.618033988749895;
            for sizes = [1 1; 3 5; 5 3; 2049 64; 64 9000; 40000 70]'
                a = mod((1:sizes(1))*phi, 1) - 0.4;
                b = mod((1:sizes(2))*phi*phi, 1) - 0.6;
                w = taploop(a, b);
                assert(isequal(conv(a, b), w));
                assert(isequal(conv(a', b'), w'));
                if numel(a) > numel(b)
                    assert(isequal(conv(a, b'), w));   % full follows the longer operand
                else
                    assert(isequal(conv(a, b'), w'));  % or the second on a tie
                end
                full = w;
                na = numel(a); nb = numel(b);
                same = full(floor(nb/2) + (1:na));
                assert(isequal(conv(a, b, 'same'), same));
                assert(isequal(conv(a', b, 'same'), same'));
                if na >= nb
                    assert(isequal(conv(a, b, 'valid'), full(nb:na)));
                else
                    assert(isempty(conv(a, b, 'valid')));
                end
            end
            """ + TapLoop);
    }

    [Fact]
    public void ConvOfABigSignalMatchesTheTapLoop()
    {
        if (!JgsPacking.Enabled)
        {
            return; // the road is the packed one, and 200,000 boxed elements is slow for nothing
        }

        Run("""
            phi = 0.618033988749895;
            a = mod((1:200000)*phi, 1);
            b = mod((1:64)*phi, 1);
            b(5) = 0; b(9) = 1;
            a(17) = Inf; a(9000) = NaN;
            w = taploop(a, b);
            c = conv(a, b);
            assert(isequaln(c, w));
            assert(isequal(size(c), [1 200063]));
            assert(isequaln(conv(b, a), w));
            assert(isequaln(conv(a, b, 'same'), w(32 + (1:200000))));
            assert(isequaln(conv(a, b, 'valid'), w(64:200000)));
            """ + TapLoop);
    }

    [Fact]
    public void ConvOfMixedStorageAndScalarsIsUnchanged()
    {
        Run("""
            assert(isequal(conv(2, 3), 6));
            assert(isequal(conv([1 2 3], 2), [2 4 6]));
            assert(isequal(conv(true(1, 3), [1 1]), [1 2 2 1]));
            assert(isequal(conv([1 2], [1 1 1]), [1 3 3 2]));
            assert(isequal(conv([1; 3; 2; 4], [1; 1]), [1; 4; 5; 6; 4]));
            assert(isequal(conv([1 2 3], [1 1], 'valid'), [3 5]));
            assert(isempty(conv([1 2], [1 1 1], 'valid')));
            """);
    }

    private const string Knots = """
        phi = 0.618033988749895;
        nk = 2000;
        xk = linspace(0, 1, nk);
        yk = sin(8*xk) .* exp(-xk);
        xq = [mod((1:20000)*phi, 1), -0.25, 1.5, xk(17), xk(end), NaN];
        """;

    [Fact]
    public void Interp1sPackedRoadIsTheMatrixRoadsColumnByColumn()
    {
        // A matrix of values takes the general road; its first column is the same curve.
        Run(Knots + """
            for m = {'linear', 'nearest', 'next', 'previous', 'pchip', 'cubic', 'spline', 'makima'}
                v = interp1(xk, yk, xq, m{1});
                V = interp1(xk, [yk; 2*yk]', xq, m{1});
                assert(isequaln(v, V(:, 1).'), m{1});
                assert(isequal(size(v), size(xq)));
                vc = interp1(xk, yk, xq.', m{1});
                assert(isequaln(vc, v.'), m{1});
                ve = interp1(xk, yk, xq, m{1}, 'extrap');
                Ve = interp1(xk, [yk; yk]', xq, m{1}, 'extrap');
                assert(isequaln(ve, Ve(:, 1).'), m{1});
                vf = interp1(xk, yk, xq, m{1}, -3);
                Vf = interp1(xk, [yk; yk]', xq, m{1}, -3);
                assert(isequaln(vf, Vf(:, 1).'), m{1});
            end
            """);
    }

    [Fact]
    public void Interp1OfUnsortedKnotsIsTheSortedOnes()
    {
        Run(Knots + """
            p = [nk:-1:1];
            for m = {'linear', 'nearest', 'pchip', 'spline', 'makima'}
                assert(isequaln(interp1(xk(p), yk(p), xq, m{1}), interp1(xk, yk, xq, m{1})), m{1});
            end
            r = mod((1:nk)*37, nk) + 1;
            assert(isequaln(interp1(xk(r), yk(r), xq, 'spline'), interp1(xk, yk, xq, 'spline')));
            try
                interp1([0 1 1 2], [1 2 3 4], 0.5);
                error('a repeated position was accepted');
            catch e
                assert(contains(e.message, 'must all be different'));
            end
            """);
    }

    [Fact]
    public void Interp1OfOnePointIsANumber()
    {
        Run(Knots + """
            v = interp1(xk, yk, 0.5);
            assert(isscalar(v) && isa(v, 'double'));
            two = interp1(xk, yk, [0.5 0.5]);
            assert(isequal(two, [v v]));
            """);
    }

    private const string Grid = """
        gx = linspace(-3, 3, 120);
        gy = linspace(-2, 2, 90);
        [GX, GY] = meshgrid(gx, gy);
        GZ = sin(GX) .* cos(GY) + 0.2*GX;
        qxs = linspace(-3.3, 3.3, 61);
        qys = linspace(-2.2, 2.2, 47);
        [QX, QY] = meshgrid(qxs, qys);
        """;

    [Fact]
    public void Interp2sPackedPairsAreTheGridFormsPoints()
    {
        // Vectors lying along the two directions take the general road to the same points.
        Run(Grid + """
            for m = {'linear', 'nearest', 'cubic', 'spline'}
                W = interp2(GX, GY, GZ, QX, QY, m{1});
                G = interp2(GX, GY, GZ, qxs, qys', m{1});
                assert(isequaln(W, G), m{1});
                assert(isequaln(interp2(gx, gy, GZ, QX, QY, m{1}), W), m{1});
                assert(isequaln(interp2(gx', gy', GZ, QX, QY, m{1}), W), m{1});
                w = interp2(GX, GY, GZ, QX(:)', QY(:)', m{1});
                assert(isequaln(w, W(:)'), m{1});
                Wf = interp2(GX, GY, GZ, QX, QY, m{1}, -9);
                Gf = interp2(GX, GY, GZ, qxs, qys', m{1}, -9);
                assert(isequaln(Wf, Gf), m{1});
            end
            assert(isequal(interp2(GX, GY, GZ, 0.5, 0.25), interp2(gx, gy, GZ, 0.5, 0.25)));
            """);
    }

    [Fact]
    public void Interp2OfAnUnevenGridIsUnchangedInForm()
    {
        Run(Grid + """
            ux = gx + 0.01*sin(7*gx);
            [UX, UY] = meshgrid(ux, gy);
            for m = {'linear', 'spline'}
                W = interp2(UX, UY, GZ, QX, QY, m{1});
                G = interp2(ux, gy, GZ, qxs, qys', m{1});
                assert(isequaln(W, G), m{1});
            end
            V3 = interp3(reshape(1:120, 4, 5, 6), 2.5, 1.5, 3.25);
            assert(abs(V3 - (1.5 + 4*1.5 + 20*2.25)) < 1e-12);
            """);
    }

    [Fact]
    public void Interp2OfAMillionPointsIsThePointByPointAnswer()
    {
        if (!JgsPacking.Enabled)
        {
            return; // the road is the packed one; a boxed 1200-square grid is minutes for nothing
        }

        Run("""
            phi = 0.618033988749895;
            gx = linspace(-3, 3, 1200);
            [GX, GY] = meshgrid(gx, gx);
            GZ = sin(GX) .* cos(GY) + 0.2*GX;
            qx = -3 + 6*mod((1:300000)*phi, 1);
            qy = -3 + 6*mod((1:300000)*(1-phi), 1);
            for m = {'linear', 'spline'}
                w = interp2(GX, GY, GZ, qx, qy, m{1});
                k = 1:7919:300000;
                g = zeros(size(k));
                for i = 1:numel(k)
                    g(i) = interp2(gx, gx, GZ, qx(k(i)), qy(k(i)), m{1});
                end
                assert(isequaln(w(k), g), m{1});
            end
            """);
    }

    [Fact]
    public void InterpftOfARealRecordIsRealAndKeepsItsSamples()
    {
        Run("""
            x = sin(8*linspace(0, 1, 200)) .* exp(-linspace(0, 1, 200));
            y = interpft(x, 800);
            assert(isreal(y) && isequal(size(y), [1 800]));
            assert(max(abs(y(1:4:end) - x)) < 1e-12);
            yc = interpft(x.', 800);
            assert(isequal(yc, y.'));
            M = [x; 2*x].';
            Y = interpft(M, 400);
            assert(isreal(Y) && isequal(size(Y), [400 2]));
            assert(isequal(Y(:, 1), interpft(x.', 400)));
            Z = interpft(complex(x, x), 800);
            assert(~isreal(Z));
            assert(max(abs(real(Z) - y)) < 1e-12 && max(abs(imag(Z) - y)) < 1e-12);
            """);
    }
}
