using JGraph.Api;
using JGraph.Numerics;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The every-row plan's I5 (ADR 0328): <c>normalize</c>, <c>rescale</c>, <c>gradient</c>,
/// <c>cumtrapz</c>, <c>rmmissing</c>, <c>isoutlier</c> and <c>isprime</c> over a packed vector, the
/// prime sieve, the moving windows answered into packed storage, a long mask written past the
/// cache, native blocks reused, and the sums folded in grains. Every script runs with the roads on
/// (all threads and one thread) and off, and must print the same bytes with the same outcome; the
/// long inputs cross every threading threshold and the short ones stay under them.
/// </summary>
[Collection("JG facade")]
public class VectorRoadsI5Tests : IDisposable
{
    public VectorRoadsI5Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private sealed record Run(string Output, bool Success, string? Message, long Roads);

    private static Run RunWith(bool fast, int threads, string code)
    {
        bool roadsOn = JgsVectorRoads.Enabled;
        bool stream = PackedMath.StreamLongMasks;
        bool cache = NativeBlockCache.Enabled;
        int degree = ParallelKernels.MaxDegree;
        JgsVectorRoads.Enabled = fast;
        PackedMath.StreamLongMasks = fast;
        NativeBlockCache.Enabled = fast;
        ParallelKernels.MaxDegree = threads;
        long roads = JgsVectorRoads.Roads;
        try
        {
            JG.Reset();
            var output = new RecordingScriptOutput();
            var context = new ScriptContext(output, (_, _) => { }, null);
            ScriptRunResult result = JgsRunner.Run(
                code + Show, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
            return new Run(output.NormalText, result.Success, result.Message, JgsVectorRoads.Roads - roads);
        }
        finally
        {
            JgsVectorRoads.Enabled = roadsOn;
            PackedMath.StreamLongMasks = stream;
            NativeBlockCache.Enabled = cache;
            ParallelKernels.MaxDegree = degree;
        }
    }

    /// <summary>The roads on (every thread, then one) and off: the same bytes and the same outcome.</summary>
    private static Run AssertParity(string code, bool expectRoads = true)
    {
        // Boxed storage takes none of these roads, so on against off there is the general road
        // against itself; over a million elements or more that is minutes of boxed elements for no
        // information. The boxed lane keeps the short scripts and the storage-agreement test.
        if (!JgsPacking.Enabled
            && (code.Contains(Million, StringComparison.Ordinal) || code.Contains(SeriesLength, StringComparison.Ordinal)))
        {
            return new Run(string.Empty, true, null, 0);
        }

        Run slow = RunWith(fast: false, threads: ParallelKernels.MaxDegree, code);
        Run fast = RunWith(fast: true, threads: Math.Max(2, ParallelKernels.MaxDegree), code);
        Run single = RunWith(fast: true, threads: 1, code);
        Assert.True(slow.Success, slow.Message);
        Assert.Equal(slow.Success, fast.Success);
        Assert.Equal(slow.Message, fast.Message);
        Assert.Equal(slow.Output, fast.Output);
        Assert.Equal(slow.Success, single.Success);
        Assert.Equal(slow.Message, single.Message);
        Assert.Equal(slow.Output, single.Output);
        Assert.Equal(0, slow.Roads);

        // The roads read packed storage, so the boxed lane has none to take.
        if (expectRoads && JgsPacking.Enabled)
        {
            Assert.True(fast.Roads > 0, "the fast run took no I5 road");
        }

        return fast;
    }

    /// <summary>
    /// <c>show</c> prints a value's class, size and every element; <c>digest</c> prints the class,
    /// the size and two sums that move when any element or its place does, for the answers too
    /// long to print.
    /// </summary>
    private const string Show = """

        function show(s)
            fprintf('%s %s\n', class(s), mat2str(size(s)));
            fprintf('%.17g ', s);
            fprintf('\n');
        end

        function digest(v)
            w = double(v(:));
            ok = ~isnan(w);
            fprintf('%s %s %.17g %.17g %d\n', class(v), mat2str(size(v)), sum(w(ok)), sum(w(ok) .* find(ok)), sum(~ok));
        end
        """;

    /// <summary>What every script over a million elements or more has in it.</summary>
    private const string Million = "000000";

    /// <summary>The length of <see cref="Series"/>, which every script built on it carries.</summary>
    private const string SeriesLength = "N = 4500000;";

    private const string Series = """
        phi = 0.618033988749895;
        N = 4500000;
        x = mod((1:N)*phi, 1);
        y = sin(20*x) + 0.3*cos(137*x) + 0.05*mod((1:N)*0.381966011250105, 1);
        s = [3 -1.5 2 8 0.25 -4 7 7 1e-3];

        """;

    // --- normalize and rescale ------------------------------------------------------------------

    [Fact]
    public void NormalizeOfAVectorIsOnePassAfterTwoFolds() => AssertParity(Series + """
        nz = normalize(y); digest(nz);
        nz = normalize(y'); digest(nz);
        nz = normalize(y(1:70000)); digest(nz);
        fprintf('%.17g\n', max(abs(nz - (y(1:70000) - mean(y(1:70000))) / std(y(1:70000)))));
        show(normalize(s)); show(normalize(s'));
        show(normalize([5 5 5 5])); show(normalize([2 4]));
        """);

    [Fact]
    public void NormalizeKeepsTheGeneralRulesForEverythingElse() => AssertParity(Series + """
        t = y(1:90000); t(97:97:end) = NaN;
        nz = normalize(t); digest(nz);
        show(normalize([1 NaN 3 7])); show(normalize([1 Inf 3 7])); show(normalize([1 -Inf Inf 7]));
        show(normalize(7)); show(normalize([]));
        show(normalize(reshape(s(1:8), 2, 4))); show(normalize(reshape(s(1:8), 2, 4), 2));
        show(normalize(s, 'range')); show(normalize(s, 'zscore', 'robust')); show(normalize(s, 'center', 'median'));
        show(normalize(s, 'scale', 'iqr')); show(normalize(s, 'norm', 1));
        show(normalize(int8([1 2 3 10]))); show(normalize(single(s)));
        show(normalize([true false true true]));
        try, normalize(s, 'nosuch'), catch e, disp(e.message), end
        """, expectRoads: false);

    [Fact]
    public void RescaleOfAnArrayIsAScanAndAPass() => AssertParity(Series + """
        rs = rescale(y); digest(rs);
        rs = rescale(y'); digest(rs);
        rs = rescale(reshape(y(1:60000), 300, 200)); digest(rs);
        show(rescale(s)); show(rescale(s')); show(rescale(reshape(s(1:8), 2, 4)));
        show(rescale([-0 0 3])); show(rescale([0 -0 -3])); show(rescale([1 Inf 3])); show(rescale([-Inf 2 Inf]));
        show(rescale([1e308 -1e308 0]));
        """);

    [Fact]
    public void RescaleKeepsTheGeneralRulesForEverythingElse() => AssertParity("""
        s = [3 -1.5 2 8 0.25 -4 7 7 1e-3];
        show(rescale([1 NaN 3 7])); show(rescale([5 5 5])); show(rescale(7)); show(rescale([]));
        show(rescale(s, -1, 1)); show(rescale(s, 'InputMin', 0)); show(rescale(s, 0, 10, 'InputMax', 5));
        show(rescale(int8([1 2 3 10]))); show(rescale(single(s))); show(rescale([true false true]));
        show(rescale(cat(3, [1 2; 3 4], [5 6; 7 8])));
        """, expectRoads: false);

    // --- gradient and cumtrapz ------------------------------------------------------------------

    [Fact]
    public void GradientAndCumtrapzOfAVectorAtUnitSpacing() => AssertParity(Series + """
        g = gradient(y); digest(g);
        g = gradient(y'); digest(g);
        c = cumtrapz(y); digest(c);
        c = cumtrapz(y'); digest(c);
        show(gradient(s)); show(gradient(s')); show(gradient([2 9])); show(gradient([1 Inf 3 NaN 5 6]));
        show(cumtrapz(s)); show(cumtrapz(s')); show(cumtrapz([2 9])); show(cumtrapz([1 Inf 3 NaN 5 6]));
        show(gradient([-0 -0 -0])); show(cumtrapz([-0 -0 -0]));
        """);

    [Fact]
    public void GradientAndCumtrapzKeepTheGeneralRoadForEverythingElse() => AssertParity("""
        s = [3 -1.5 2 8 0.25 -4 7 7 1e-3];
        show(gradient(s, 0.5)); show(gradient(s, (1:9).^2)); show(gradient(7)); show(gradient([]));
        [gx, gy] = gradient(reshape(s(1:8), 2, 4)); show(gx); show(gy);
        [g1, g2] = gradient(s); show(g1); show(g2);
        show(cumtrapz(s, 2)); show(cumtrapz((1:9).^2, s)); show(cumtrapz(7)); show(cumtrapz(reshape(s(1:8), 2, 4)));
        show(cumtrapz(reshape(s(1:8), 2, 4), 2)); show(trapz(s)); show(gradient(single(s))); show(cumtrapz(int8([1 2 3])));
        """, expectRoads: false);

    // --- rmmissing and isoutlier ----------------------------------------------------------------

    [Fact]
    public void RmmissingOfAVectorCopiesTheSurvivorsWhereTheCountsSay() => AssertParity(Series + """
        g = y; g(97:97:end) = NaN; g(1) = NaN; g(end) = NaN; g(200000:330000) = NaN;
        f = rmmissing(g); digest(f);
        [f, tf] = rmmissing(g'); digest(f); digest(tf);
        f = rmmissing(y(1:100000)); digest(f);
        [f, tf] = rmmissing([1 NaN 3 NaN NaN 6]); show(f); show(tf);
        [f, tf] = rmmissing([1 NaN 3 NaN NaN 6]'); show(f); show(tf);
        [f, tf] = rmmissing([4 5 6]); show(f); show(tf);
        """);

    [Fact]
    public void RmmissingKeepsTheGeneralRoadForEverythingElse() => AssertParity("""
        [f, tf] = rmmissing([NaN NaN NaN]); show(f); show(tf);
        [f, tf] = rmmissing([NaN 4 NaN]); show(f); show(tf);
        [f, tf] = rmmissing([NaN 4 NaN]'); show(f); show(tf);
        [f, tf] = rmmissing([1 NaN; 3 4; 5 6]); show(f); show(tf);
        [f, tf] = rmmissing([1 NaN; 3 4; 5 6], 2); show(f); show(tf);
        [f, tf] = rmmissing([1 NaN 2; 3 4 NaN; 5 6 7], 'MinNumMissing', 2); show(f); show(tf);
        show(rmmissing(7)); show(rmmissing(NaN)); show(rmmissing([]));
        show(rmmissing(single([1 NaN 3]))); disp(rmmissing(["a" missing "c"]));
        """, expectRoads: false);

    [Fact]
    public void IsoutlierOfAVectorIsTwoSelectionsAndAPass() => AssertParity(Series + """
        z = y(1:2500000); z(1000:1000:end) = 40; z(5) = -40;
        io = isoutlier(z); digest(io); fprintf('%d\n', nnz(io));
        io = isoutlier(z'); digest(io);
        z = y(1:2501); z(7) = 99;
        io = isoutlier(z); digest(io); disp(find(io));
        io = isoutlier(y(1:66)); digest(io);
        io = isoutlier([ones(1, 80) 5]); digest(io); disp(find(io));
        io = isoutlier(zeros(1, 100)); digest(io);
        """);

    [Fact]
    public void IsoutlierKeepsTheGeneralRoadForEverythingElse() => AssertParity(Series + """
        z = y(1:3000); z(7) = 99; z(11) = NaN;
        io = isoutlier(z); digest(io);
        z(11) = Inf; io = isoutlier(z); digest(io);
        show(isoutlier([1 2 3 100 2 1 3])); show(isoutlier([1 2 3 100 2 1 3]'));
        [tf, lo, hi, c] = isoutlier(y(1:3000)); digest(tf); show(lo); show(hi); show(c);
        io = isoutlier(y(1:3000), 'mean'); digest(io);
        io = isoutlier(y(1:3000), 'quartiles'); digest(io);
        io = isoutlier(y(1:3000), 'movmedian', 21); digest(io);
        io = isoutlier(reshape(y(1:3000), 300, 10)); digest(io);
        show(isoutlier(single([1 2 3 100 2 1 3])));
        """, expectRoads: false);

    // --- primes ---------------------------------------------------------------------------------

    [Fact]
    public void IsprimeReadsASieveWhereTheValuesAreDense()
    {
        Run run = AssertParity("""
            ip = isprime(1:1000000); digest(ip); fprintf('%d\n', sum(ip));
            ip = isprime((1:1000000)'); digest(ip);
            ip = isprime(reshape(0:9999, 100, 100)); digest(ip);
            show(isprime([0 1 2 3 4 5 6 7 8 9 10 11 12 13]));
            show(isprime([2.5 -7 7 97 91 1e6+3 0.5 -0 49 121 169 65537 65536]));
            show(isprime([2 2])); show(isprime([4 4]));
            ip = isprime(5000000:5100000); digest(ip);
            """);
        if (JgsPacking.Enabled)
        {
            Assert.Contains("78498", run.Output);
        }
    }

    [Fact]
    public void IsprimeKeepsTheDividingRoadForEverythingElse() => AssertParity("""
        show(isprime(97)); show(isprime([]));
        show(isprime([7 NaN 11])); show(isprime([7 Inf 11])); show(isprime([7 -Inf 11]));
        show(isprime([1e12+39 1e12+40 7 9]));
        show(isprime([2147483647 2147483646 4294967311 3]));
        show(isprime(uint8([2 3 4 251 255]))); show(isprime(single([2 3 4 5])));
        show(isprime([true false]));
        """, expectRoads: false);

    [Fact]
    public void PrimesComeOffASieveOfTheOddNumbers()
    {
        Run run = AssertParity("""
            pr = primes(5000000); digest(pr); fprintf('%d %d\n', numel(pr), pr(end));
            pr = primes(1000000); digest(pr);
            pr = primes(65536); digest(pr); pr = primes(65537); digest(pr); pr = primes(65521); digest(pr);
            for n = [0 1 2 3 4 5 6 7 8 9 10 11 24 25 26 48 49 50 120 121 122 168 169 170 1000 1024 4489]
                pr = primes(n); fprintf('%d:%d:%d ', n, numel(pr), sum(pr));
            end
            fprintf('\n');
            show(primes(30)); show(primes(2)); show(primes(-3));
            """);
        if (JgsPacking.Enabled)
        {
            Assert.Contains("348513 4999999", run.Output);
        }
    }

    // --- the moving windows ---------------------------------------------------------------------

    [Fact]
    public void MovingWindowsAnswerIntoPackedStorage() => AssertParity(Series + """
        z = y(1:600000);
        m = movmean(z, 51); digest(m);
        m = movstd(z, 51); digest(m);
        m = movmedian(z(1:300000), 21); digest(m);
        m = movmax(z, 101); digest(m); m = movmin(z', 101); digest(m);
        m = movsum(z, [3 9]); digest(m); m = movvar(z, 8); digest(m); m = movprod(z(1:1000), 5); digest(m);
        m = movmean(z, 51, 'Endpoints', 'discard'); digest(m);
        m = movmean(z, 51, 'Endpoints', 'fill'); digest(m);
        m = movmean(z, 51, 'Endpoints', 2.5); digest(m);
        z(97:97:end) = NaN;
        m = movmean(z, 51); digest(m); m = movmean(z, 51, 'omitnan'); digest(m);
        m = movstd(z, 51, 'omitnan'); digest(m); m = movmedian(z(1:300000), 21, 'omitnan'); digest(m);
        show(movmean(s, 3)); show(movmean(s', 3)); show(movstd(s, 4)); show(movmedian(s, 5));
        show(movmean(s, 9, 'Endpoints', 'discard')); show(movmean(s, 8, 'Endpoints', 'discard'));
        show(movmean(s, 3, 'Endpoints', 'discard')); show(movmean([4 6], 2)); show(movmean(7, 3));
        show(movmean(s, 3, 'SamplePoints', (1:9).^1.5)); show(movmean(reshape(s(1:8), 2, 4), 3, 2));
        show(movmean(uint8([10 20 30 250 255]), 3)); show(movmax(int8([1 -5 7 2]), 3));
        """, expectRoads: false);

    // --- the long mask and the reused blocks ----------------------------------------------------


    [Fact]
    public void ALongMaskWrittenPastTheCacheIsTheSameMask() => AssertParity(Series + """
        w = y; w(5:1000:end) = NaN; w(7) = Inf; w(9) = -Inf; w(11) = 1.0; w(13) = -0;
        m = w > 1.0; digest(m); fprintf('%d\n', nnz(m));
        m = w >= 1.0; digest(m); m = w < 1.0; digest(m); m = w <= 1.0; digest(m);
        m = w == 1.0; digest(m); m = w ~= 1.0; digest(m);
        m = 1.0 > w; digest(m); m = 1.0 <= w; digest(m); m = 0 == w; digest(m); m = NaN ~= w; digest(m);
        m = w' > 0.25; digest(m); v = w(m'); digest(v);
        m = w(2:end) > 0.25; digest(m); m = w(3:end-1) < 0; digest(m);
        m = w > Inf; digest(m); m = w < Inf; digest(m); m = w > NaN; digest(m);
        """, expectRoads: false);

    [Fact]
    public void ReusedNativeBlocksHoldWhatAFreshOneWouldHold() => AssertParity("""
        x = mod((1:3000000)*0.618033988749895, 1);
        acc = 0;
        for k = 1:5
            a = x * k;
            b = zeros(1, 3000000);
            b(1:2:end) = a(1:2:end);
            acc = acc + sum(b) + sum(a);
            c = cumsum(x(1:2700000));
            acc = acc + c(end) + nnz(zeros(1, 2900000)) + sum(ones(2800000, 1));
            z = false(1, 3000000); acc = acc + nnz(z) + nnz(isnan(b));
            clear a b c z;
        end
        fprintf('%.17g\n', acc);
        """, expectRoads: false);

    [Fact]
    public void TheBlockListServesOverwriteDestinationsAndNeverAZeroedRequest()
    {
        bool was = NativeBlockCache.Enabled;
        NativeBlockCache.Enabled = true;
        try
        {
            const int Length = 1_333_337; // a length nothing else in the suite asks for
            bool reused = false;
            for (int attempt = 0; attempt < 8 && !reused; attempt++)
            {
                long before = NativeBlockCache.Reused;
                using (var first = new NativeBuffer(Length, zeroed: false))
                {
                    first.AsSpan().Fill(7.5);
                }

                // A zeroed request is never handed a used block, so it is zero whatever is held.
                using (var zeroed = new NativeBuffer(Length - 3, zeroed: true))
                {
                    Assert.Equal(-1, zeroed.AsSpan().IndexOfAnyExcept(0.0));
                }

                using var second = new NativeBuffer(Length - 5, zeroed: false);
                reused = NativeBlockCache.Reused > before;
            }

            Assert.True(reused, "a block freed and asked for again was never reused");
            Assert.True(NativeBlockCache.HeldBytes <= NativeBlockCache.CapBytes);
            NativeBlockCache.Clear();
        }
        finally
        {
            NativeBlockCache.Enabled = was;
        }
    }

    // --- the sums in grains ---------------------------------------------------------------------

    [Fact]
    public void TheGrainSumIsItsSerialDefinitionAtAnyThreadCount()
    {
        const int Length = (1 << 22) + 70_001; // past the threshold, and a ragged last grain
        using var buffer = new NativeBuffer(Length);
        Span<double> x = buffer.AsSpan();
        for (int i = 0; i < Length; i++)
        {
            // Magnitudes sixteen decades apart, so the order of the additions shows in the bits.
            x[i] = Math.Sin(i * 0.37) * Math.Pow(10, (i % 17) - 8);
        }

        double reference = 0;
        for (int start = 0; start < Length; start += ParallelKernels.GrainElements)
        {
            double grain = 0;
            int end = Math.Min(Length, start + ParallelKernels.GrainElements);
            for (int i = start; i < end; i++)
            {
                grain += x[i];
            }

            reference += grain;
        }

        int degree = ParallelKernels.MaxDegree;
        try
        {
            foreach (int threads in new[] { 1, 2, 7, Math.Max(2, degree) })
            {
                ParallelKernels.MaxDegree = threads;
                Assert.Equal(reference, ReduceKernels.GrainSum(buffer, 0, Length));
                Assert.Equal(reference, PackedMath.Sum(buffer));
            }

            Assert.Equal(reference, ReduceKernels.GrainSum(buffer.AsSpan()));
            double mean = reference / Length;
            double serial = ReduceKernels.GrainSquaredDeviations(buffer.AsSpan(), mean);
            ParallelKernels.MaxDegree = Math.Max(2, degree);
            Assert.Equal(serial, ReduceKernels.GrainSquaredDeviations(buffer, 0, Length, mean));

            // Up to one grain the definition is the plain left fold.
            double plain = 0;
            for (int i = 0; i < ParallelKernels.GrainElements; i++)
            {
                plain += x[i];
            }

            Assert.Equal(plain, ReduceKernels.GrainSum(buffer.AsSpan(0, ParallelKernels.GrainElements)));
        }
        finally
        {
            ParallelKernels.MaxDegree = degree;
        }
    }

    private const string Reductions = """
        N = 4500000;
        sc = 10.^(-8:8); x = (mod((1:N)*0.618033988749895, 1) - 0.5) .* sc(1 + mod(1:N, 17));
        fprintf('%.17g %.17g %.17g %.17g\n', sum(x), mean(x), std(x), var(x));
        fprintf('%.17g %.17g %.17g\n', sum(x'), std(x, 1), var(x', 1));
        fprintf('%.17g %.17g\n', sum(x, 'all'), mean(x, 'all'));
        u = x(1:200000); u(97:97:end) = NaN;
        fprintf('%.17g %.17g %.17g %.17g\n', sum(u, 'omitnan'), mean(u, 'omitnan'), std(u, 'omitnan'), var(u, 1, 'omitnan'));
        fprintf('%.17g %.17g\n', sum(u), mean(u));
        A = reshape(x(1:280000), 2, 140000);
        fprintf('%.17g ', sum(A, 2), mean(A, 2), std(A, 0, 2), var(A, 0, 2), sum(A', 1), std(A')); fprintf('\n');
        B = A; B(1, 5:7:end) = NaN;
        fprintf('%.17g ', sum(B, 2, 'omitnan'), mean(B, 2, 'omitnan'), std(B, 0, 2, 'omitnan'), sum(B', 'omitnan')); fprintf('\n');
        c = sum(reshape(x(1:300000), 300, 1000)); fprintf('%.17g %.17g\n', sum(c), c(77));
        c = cumsum(x(1:300000)); fprintf('%.17g %.17g %.17g\n', c(end), c(65537), c(131072));
        c = cumsum(x(1:300000)'); fprintf('%.17g\n', c(end));
        fprintf('%.17g %.17g\n', sum(uint8(mod(1:300000, 256))), sum(int32(1:300000)));
        fprintf('%.17g %.17g %.17g\n', sum([1 2 3]), mean([1 2 3 4]), std([2 4 4 4 5 5 7 9]));

        """;

    [Fact]
    public void SumMeanAndStdAnswerTheSameAtOneThreadAndAtAll()
    {
        if (!JgsPacking.Enabled)
        {
            return; // threads are the packed kernels'; the boxed fold is one thread by construction
        }

        Run many = RunWith(fast: true, threads: Math.Max(2, ParallelKernels.MaxDegree), Reductions);
        Run one = RunWith(fast: true, threads: 1, Reductions);
        Assert.True(many.Success, many.Message);
        Assert.True(one.Success, one.Message);
        Assert.Equal(one.Output, many.Output);
        Assert.Contains("6 2.5 2.1380899352993952", many.Output);
    }

    [Fact]
    public void SumMeanAndStdAnswerTheSameInBothStorages()
    {
        const string Short = """
            N = 200000;
            sc = 10.^(-8:8); x = (mod((1:N)*0.618033988749895, 1) - 0.5) .* sc(1 + mod(1:N, 17));
            fprintf('%.17g %.17g %.17g %.17g %.17g %.17g\n', sum(x), mean(x), std(x), var(x), std(x, 1), sum(x'));
            u = x; u(97:97:end) = NaN;
            fprintf('%.17g %.17g %.17g %.17g\n', sum(u, 'omitnan'), mean(u, 'omitnan'), std(u, 'omitnan'), var(u, 1, 'omitnan'));
            A = reshape(x(1:140000), 2, 70000);
            fprintf('%.17g ', sum(A, 2), mean(A, 2), std(A, 0, 2), sum(A', 1), std(A')); fprintf('\n');
            B = A; B(1, 5:7:end) = NaN;
            fprintf('%.17g ', sum(B, 2, 'omitnan'), mean(B, 2, 'omitnan'), std(B, 0, 2, 'omitnan')); fprintf('\n');
            nz = normalize(x); fprintf('%.17g %.17g\n', sum(nz), nz(77777));
            nz = normalize(u); fprintf('%.17g %.17g\n', sum(nz, 'omitnan'), nz(77777));

            """;
        bool packing = JgsPacking.Enabled;
        try
        {
            JgsPacking.Enabled = true;
            Run packed = RunWith(fast: true, threads: Math.Max(2, ParallelKernels.MaxDegree), Short);
            JgsPacking.Enabled = false;
            Run boxed = RunWith(fast: true, threads: 1, Short);
            Assert.True(packed.Success, packed.Message);
            Assert.True(boxed.Success, boxed.Message);
            Assert.Equal(boxed.Output, packed.Output);
        }
        finally
        {
            JgsPacking.Enabled = packing;
        }
    }
}
