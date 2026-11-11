using System.IO.Compression;
using JGraph.Api;
using JGraph.Imaging;
using JGraph.Imaging.Codecs;
using JGraph.Numerics;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The every-row plan's I9 (ADR 0330): a square is the product on every road to a power, the
/// gradient of a matrix is taken whole columns at a time across threads, a packed matrix reaches
/// the picture verbs in one pass, and an 8-bit PNG is deflated in strips. Each is checked against
/// the general road or against what a reader gets back, with one thread and with many.
/// </summary>
[Collection("JG facade")]
public class ImagePipelineI9Tests : IDisposable
{
    public ImagePipelineI9Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private sealed record Run(string Output, bool Success, string? Message, long Roads);

    private static Run RunWith(bool fast, int threads, string code)
    {
        bool roadsOn = JgsVectorRoads.Enabled;
        int degree = ParallelKernels.MaxDegree;
        JgsVectorRoads.Enabled = fast;
        ParallelKernels.MaxDegree = threads;
        long roads = JgsVectorRoads.Roads;
        try
        {
            JG.Reset();
            var output = new RecordingScriptOutput();
            var context = new ScriptContext(output, (_, _) => { }, null);
            ScriptRunResult result = JgsRunner.Run(
                code + Digest, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
            return new Run(output.NormalText + output.ErrorText, result.Success, result.Message, JgsVectorRoads.Roads - roads);
        }
        finally
        {
            JgsVectorRoads.Enabled = roadsOn;
            ParallelKernels.MaxDegree = degree;
        }
    }

    private static Run Asserts(string code)
    {
        Run run = RunWith(fast: true, threads: ParallelKernels.MaxDegree, code);
        Assert.True(run.Success, run.Message + run.Output);
        return run;
    }

    private const string Digest = """

        function digest(v)
            w = double(v(:));
            ok = ~isnan(w) & ~isinf(w);
            fprintf('%s %s %.17g %.17g %d %d\n', class(v), mat2str(size(v)), sum(w(ok)), sum(w(ok) .* find(ok)), sum(isnan(w)), sum(isinf(w)));
        end
        """;

    // --- a square is the product ---------------------------------------------------------------

    [Fact]
    public void ASquareIsTheProductOnEveryRoad() => Asserts("""
        x = [0.1 0.7 1/3 -2.3 1e-200 1e200 pi exp(1) 4.35 -0.0073 1e-160 3e153];
        assert(isequal(x.^2, x.*x));
        assert(isequal(x.^[2 2 2 2 2 2 2 2 2 2 2 2], x.*x));
        big = mod((1:5000)*0.618033988749895, 1)*37 - 11;
        assert(isequal(big.^2, big.*big));
        assert(isequal((-big).^2, big.*big));
        A = reshape(big(1:4900), 70, 70);
        assert(isequal(A.^2, A.*A));
        s = 0.1;
        assert(s^2 == s*s);
        assert((-3)^2 == 9);
        f = @(t) t.^2 + 1;
        assert(isequal(f(big), big.*big + 1));
        g = @(t) t^2;
        assert(g(0.7) == 0.7*0.7);
        acc = 0; ref = 0;
        for k = 1:20000
            v = k*0.1;
            acc = acc + v^2;
            ref = ref + v*v;
        end
        assert(acc == ref);
        assert(isequaln([Inf -Inf NaN 0 -0].^2, [Inf Inf NaN 0 0]));
        assert(isequal(2.^[2 3], [4 8]));
        """);

    [Fact]
    public void TheOtherExponentsAreWhatTheyWere() => Asserts("""
        assert(isequal([4 9 16].^0.5, [2 3 4]));
        assert(isequal([2 3].^3, [8 27]));
        assert(isequal(2.^(0:4), [1 2 4 8 16]));
        assert(isequal([1 2 4].^-1, [1 0.5 0.25]));
        z = (-8)^(1/3);
        assert(abs(z - (1 + sqrt(3)*1i)) < 1e-14);
        assert(isequal(int32([3 -4]).^2, int32([9 16])));
        assert(isa(single(3)^2, 'single') && single(3)^2 == 9);
        assert(isequal([1 2; 3 4]^2, [7 10; 15 22]));
        nb = -(1:5000)/7;
        c = nb.^0.5;
        assert(~isreal(c) && abs(c(7) - 1i) < 1e-15);
        r3 = nb.^3;
        assert(isreal(r3) && r3(7) == -1);
        r2 = nb.^2;
        assert(isreal(r2) && isequal(r2, nb.*nb));
        assert(all(isnan(nb.^NaN)));
        """);

    // --- the gradient of a matrix ---------------------------------------------------------------

    private static void AssertGradientParity(string code)
    {
        Run slow = RunWith(fast: false, threads: ParallelKernels.MaxDegree, code);
        Run fast = RunWith(fast: true, threads: Math.Max(2, ParallelKernels.MaxDegree), code);
        Run single = RunWith(fast: true, threads: 1, code);
        Assert.True(slow.Success, slow.Message + slow.Output);
        Assert.True(fast.Success, fast.Message + fast.Output);
        Assert.Equal(slow.Output, fast.Output);
        Assert.Equal(slow.Output, single.Output);
        Assert.Equal(0, slow.Roads);
        if (JgsPacking.Enabled)
        {
            Assert.True(fast.Roads > 0, "the fast run took no gradient road");
        }
    }

    [Fact]
    public void TheGradientOfAMatrixIsTheGeneralRoads() => AssertGradientParity("""
        F = reshape(sin((1:63*41)*0.37) .* (1:63*41), 63, 41);
        [gx, gy] = gradient(F);
        g1 = gradient(F);
        digest(gx); digest(gy); digest(g1);
        assert(isequal(g1, gx));
        E = F; E(3, 4) = NaN; E(10, 7) = Inf; E(20, 1) = -Inf;
        [ex, ey] = gradient(E);
        digest(ex); digest(ey);
        [a, b] = gradient(F(1:2, 1:2)); digest(a); digest(b);
        [c, d] = gradient(F(1:2, :)); digest(c); digest(d);
        [e, f] = gradient(F(:, 1:2)); digest(e); digest(f);
        [p, q] = gradient(F(1:3, 1:3)); fprintf('%.17g ', p, q); fprintf('\n');
        """);

    [Fact]
    public void TheGradientOfAMatrixIsTheCentralDifference() => Asserts("""
        F = reshape(cos((1:57*38)*0.21) .* sqrt(1:57*38), 57, 38);
        [gx, gy] = gradient(F);
        assert(isequal(gx(:, 2:end-1), (F(:, 3:end) - F(:, 1:end-2)) / 2));
        assert(isequal(gx(:, 1), F(:, 2) - F(:, 1)));
        assert(isequal(gx(:, end), F(:, end) - F(:, end-1)));
        assert(isequal(gy(2:end-1, :), (F(3:end, :) - F(1:end-2, :)) / 2));
        assert(isequal(gy(1, :), F(2, :) - F(1, :)));
        assert(isequal(gy(end, :), F(end, :) - F(end-1, :)));
        assert(isequal(size(gx), size(F)) && isequal(size(gy), size(F)));
        assert(isequal(gradient([1 4 9; 16 25 36]), [3 4 5; 9 10 11]));
        [hx, hy] = gradient([1 4 9; 16 25 36]);
        assert(isequal(hy, [15 21 27; 15 21 27]));
        [sx, sy] = gradient(F, 0.5, 2);
        assert(max(max(abs(sx - gx / 0.5))) < 1e-12 && max(max(abs(sy - gy / 2))) < 1e-12);
        """);

    [Fact]
    public void ALargeGradientIsTheSameOnOneThreadAndOnMany()
    {
        if (!JgsPacking.Enabled)
        {
            return; // two million boxed elements a field, for a road boxed storage does not take
        }

        AssertGradientParity("""
            n = 1500;
            F = reshape(sin((1:n*n)*0.37) + mod((1:n*n)*0.618033988749895, 1), n, n);
            [gx, gy] = gradient(F);
            digest(gx); digest(gy);
            edges = sqrt(gx.^2 + gy.^2);
            edges = edges / max(edges(:));
            digest(edges);
            """);
    }

    // --- imwrite ---------------------------------------------------------------------------------

    [Fact]
    public void AWrittenPngReadsBackAsThePixelsItWasGiven() => Asserts("""
        A = uint8(mod(reshape(0:600*700-1, 600, 700)*7, 256));
        p = fullfile(tempdir, 'jgraph_i9_roundtrip.png');
        imwrite(A, p); B = imread(p); delete(p);
        assert(isequal(A, B)); assert(isa(B, 'uint8'));
        D = mod(reshape(0:299*211-1, 299, 211)*0.013, 1);
        imwrite(D, p); E = imread(p); delete(p);
        assert(isequal(E, uint8(round(D*255))));
        C = cat(3, A, 255 - A, A);
        imwrite(C, p); F = imread(p); delete(p);
        assert(isequal(C, F));
        imwrite(uint8(7), p); one = imread(p); delete(p);
        assert(isequal(one, uint8(7)));
        imwrite(uint8(1:5), p); row = imread(p); delete(p);
        assert(isequal(row, uint8(1:5)));
        imwrite(uint8((1:5)'), p); col = imread(p); delete(p);
        assert(isequal(col, uint8((1:5)')));
        imwrite(A(1:40, 1:50), p, 'Alpha', D(1:40, 1:50)); [G, ~, alpha] = imread(p); delete(p);
        assert(isequal(G(:, :, 1), A(1:40, 1:50)));
        assert(isequal(alpha, uint8(round(D(1:40, 1:50)*255))));
        """);

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public void APngOfManyStripsIsOneZlibStream(int threads)
    {
        int degree = ParallelKernels.MaxDegree;
        ParallelKernels.MaxDegree = threads == 0 ? Math.Max(2, degree) : threads;
        string path = Path.Combine(Path.GetTempPath(), $"jgraph_i9_strips_{threads}.png");
        try
        {
            // 700 rows of 900 samples is five strips; the last is short.
            const int Height = 700;
            const int Width = 900;
            using var image = new ImageBuffer(Height, Width, 1);
            var expected = new byte[Height * (Width + 1)];
            for (int r = 0; r < Height; r++)
            {
                for (int c = 0; c < Width; c++)
                {
                    int level = ((r * 31) + (c * 17) + ((r * c) % 13)) & 0xFF;
                    image[r, c, 0] = level / 255.0;
                    expected[(r * (Width + 1)) + 1 + c] = (byte)level;
                }
            }

            ImageCodec.Write(path, image);
            byte[] file = File.ReadAllBytes(path);

            // Walk the chunks: every IDAT's payload, end to end, is the zlib stream.
            using var stream = new MemoryStream();
            int chunks = 0;
            for (int at = 8; at < file.Length;)
            {
                int length = (file[at] << 24) | (file[at + 1] << 16) | (file[at + 2] << 8) | file[at + 3];
                string name = System.Text.Encoding.ASCII.GetString(file, at + 4, 4);
                if (name == "IDAT")
                {
                    stream.Write(file, at + 8, length);
                    chunks++;
                }

                at += 12 + length;
            }

            Assert.True(chunks > 1, "the picture went out as one strip");
            stream.Position = 0;
            using var inflate = new ZLibStream(stream, CompressionMode.Decompress);
            using var raw = new MemoryStream();
            inflate.CopyTo(raw);
            Assert.Equal(expected, raw.ToArray());

            using ImageBuffer back = ImageCodec.Read(path);
            Assert.Equal(Height, back.Height);
            Assert.Equal(Width, back.Width);
            for (int r = 0; r < Height; r += 37)
            {
                for (int c = 0; c < Width; c += 41)
                {
                    Assert.Equal(expected[(r * (Width + 1)) + 1 + c], (byte)Math.Round(back[r, c, 0] * 255.0));
                }
            }
        }
        finally
        {
            ParallelKernels.MaxDegree = degree;
            File.Delete(path);
        }
    }

    [Fact]
    public void APackedMatrixReachesThePictureVerbsAsItWas() => Asserts("""
        D = mod(reshape(0:120*90-1, 120, 90)*0.013, 1);
        assert(isequal(size(imresize(D, 0.5)), [60 45]));
        J = imcomplement(D);
        assert(max(max(abs(J - (1 - D)))) < 1e-15);
        U = uint8(mod(reshape(0:120*90-1, 120, 90)*7, 256));
        K = imcomplement(U);
        assert(isa(K, 'uint8') && isequal(K, 255 - U));
        r = imcomplement(0.25:0.25:1);
        assert(isequal(size(r), [1 4]) && max(abs(r - (1 - (0.25:0.25:1)))) < 1e-15);
        c = imcomplement((0.25:0.25:1)');
        assert(isequal(size(c), [4 1]));
        m = imcomplement(D > 0.5);
        assert(isequal(m, ~(D > 0.5)));
        """);
}
