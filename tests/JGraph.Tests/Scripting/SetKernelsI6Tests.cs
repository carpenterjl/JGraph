using JGraph.Api;
using JGraph.Numerics;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The every-row plan's I6 (ADR 0327): <c>unique</c>, the four set operations, <c>ismember</c> and
/// <c>accumarray</c> over packed doubles (a table when the keys are whole numbers in a narrow
/// range), <c>sortrows</c> by one key, <c>sort</c> and <c>unique</c> over text through packed
/// prefixes, and <c>x(:)</c> as a counted share. Every script runs with the roads on (all threads
/// and one thread) and off, and must print the same bytes with the same outcome; the long inputs
/// cross every threading threshold and the short ones stay under them.
/// </summary>
[Collection("JG facade")]
public class SetKernelsI6Tests : IDisposable
{
    public SetKernelsI6Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private sealed record Run(string Output, bool Success, string? Message, long Roads, long Maps);

    private static Run RunWith(bool fast, int threads, string code)
    {
        bool sets = JgsSetKernels.Enabled;
        bool colon = JgsOwnership.ShareWholeColon;
        int degree = ParallelKernels.MaxDegree;
        JgsSetKernels.Enabled = fast;
        JgsOwnership.ShareWholeColon = fast;
        ParallelKernels.MaxDegree = threads;
        long roads = JgsSetKernels.Roads;
        long maps = JgsTextKernels.Maps;
        try
        {
            JG.Reset();
            var output = new RecordingScriptOutput();
            var context = new ScriptContext(output, (_, _) => { }, null);
            ScriptRunResult result = JgsRunner.Run(
                code + Show, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
            return new Run(
                output.NormalText, result.Success, result.Message, JgsSetKernels.Roads - roads, JgsTextKernels.Maps - maps);
        }
        finally
        {
            JgsSetKernels.Enabled = sets;
            JgsOwnership.ShareWholeColon = colon;
            ParallelKernels.MaxDegree = degree;
        }
    }

    /// <summary>The roads on (every thread, then one) and off: the same bytes and the same outcome.</summary>
    private static Run AssertParity(string code, bool expectRoads = true)
    {
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

        // The numeric roads read packed storage, so the boxed lane has none to take.
        if (expectRoads && JgsPacking.Enabled)
        {
            Assert.True(fast.Roads > 0, "the fast run took no I6 road");
        }

        return fast;
    }

    /// <summary>
    /// <c>show</c> prints a value's class, size and every element (a missing string as &lt;M&gt;);
    /// <c>digest</c> prints the class, the size and two sums that move when any element or its
    /// place does, for the answers too long to print.
    /// </summary>
    private const string Show = """

        function show(s)
            fprintf('%s %s\n', class(s), mat2str(size(s)));
            if isstring(s)
                m = ismissing(s);
                s(m) = "<M>";
                fprintf('%s|', s);
                fprintf('\nmissing %s\n', mat2str(find(m(:))'));
            elseif iscell(s)
                fprintf('%s|', s{:});
                fprintf('\n');
            else
                fprintf('%.17g ', s);
                fprintf('\n');
            end
        end

        function digest(v)
            w = double(v(:));
            ok = ~isnan(w);
            fprintf('%s %s %.17g %.17g %d\n', class(v), mat2str(size(v)), sum(w(ok)), sum(w(ok) .* find(ok)), sum(~ok));
        end
        """;

    // --- unique ---------------------------------------------------------------------------------

    [Fact]
    public void UniqueOfWholeNumbersInANarrowRangeReadsATable() => AssertParity("""
        k = 1 + mod((1:300000)*7919, 5000);
        [u, ia, ic] = unique(k); digest(u); digest(ia); digest(ic);
        [u, ia, ic] = unique(k'); digest(u); digest(ia); digest(ic);
        [u, ia, ic] = unique(k, 'last'); digest(u); digest(ia); digest(ic);
        [u, ia, ic] = unique(k, 'stable'); digest(u); digest(ia); digest(ic);
        u = unique(k - 2500); digest(u);
        [u, ia] = unique(k(1:900)'); digest(u); digest(ia);
        s = [3 1 2 3 1 -4 0 7 7 7];
        [u, ia, ic] = unique(s); show(u); show(ia); show(ic);
        [u, ia, ic] = unique(s', 'last'); show(u); show(ia); show(ic);
        [u, ia, ic] = unique(s, 'stable'); show(u); show(ia); show(ic);
        [u, ia, ic] = unique([5 5 5 5]); show(u); show(ia); show(ic);
        [u, ia, ic] = unique(reshape(s(1:8), 2, 4)); show(u); show(ia); show(ic);
        isequal(u(ic), reshape(s(1:8), [], 1))
        """);

    [Fact]
    public void UniqueOfOtherDoublesKeepsTheGeneralRules() => AssertParity("""
        x = mod((1:300000)*0.61803398875, 1); x = round(x*4000)/4000;
        [u, ia, ic] = unique(x); digest(u); digest(ia); digest(ic);
        [u, ia, ic] = unique(x', 'stable'); digest(u); digest(ia); digest(ic);
        [u, ia, ic] = unique(x, 'last'); digest(u); digest(ia); digest(ic);
        s = [2.5 NaN -0 0 Inf -Inf NaN 2.5 1e300 -1e-300 0 3];
        [u, ia, ic] = unique(s); show(u); show(ia); show(ic);
        [u, ia, ic] = unique(s', 'stable'); show(u); show(ia); show(ic);
        [u, ia, ic] = unique(s, 'last'); show(u); show(ia); show(ic);
        [u, ia, ic] = unique([1 2 -0 0 2]); show(u); show(ia); show(ic);
        [u, ia, ic] = unique([1 2 NaN 2 NaN]); show(u); show(ia); show(ic);
        [u, ia, ic] = unique([1e9 5 -1e9 5 1e9 2^40 2^40]); show(u); show(ia); show(ic);
        [u, ia, ic] = unique([NaN NaN NaN]); show(u); show(ia); show(ic);
        """);

    [Fact]
    public void UniqueOfOtherKindsStillTakesTheOldRoad() => AssertParity("""
        [u, ia, ic] = unique(int8([3 1 3 2 1])); show(u); show(ia); show(ic);
        [u, ia, ic] = unique([true false true true]); show(u); show(ia); show(ic);
        [u, ia, ic] = unique('hello world'); show(double(u)); show(ia); show(ic);
        [u, ia, ic] = unique([1 2; 1 2; 3 4], 'rows'); show(u); show(ia); show(ic);
        show(unique([])); show(unique(7)); show(unique(zeros(1, 0)));
        show(unique(single([2 1 2])));
        """, expectRoads: false);

    // --- the set operations and ismember ----------------------------------------------------------

    [Fact]
    public void SetOperationsOverWholeNumbersReadATable() => AssertParity("""
        k = 1 + mod((1:100000)*7919, 5000);
        a = k(1:60000); b = 2001:9000;
        [c, ia, ib] = union(a, b); digest(c); digest(ia); digest(ib);
        [c, ia, ib] = intersect(a, b); digest(c); digest(ia); digest(ib);
        [c, ia] = setdiff(a, b); digest(c); digest(ia);
        [c, ia, ib] = setxor(a, b); digest(c); digest(ia); digest(ib);
        [c, ia, ib] = intersect(a', b); digest(c); digest(ia); digest(ib);
        [c, ia] = setdiff(a', b'); digest(c); digest(ia);
        [c, ia] = setdiff(a, b'); digest(c); digest(ia);
        [c, ia, ib] = union(a', b'); digest(c); digest(ia); digest(ib);
        p = [5 3 3 -2 9 0 5]; q = [3 0 0 11 -2];
        [c, ia, ib] = union(p, q); show(c); show(ia); show(ib);
        [c, ia, ib] = intersect(p, q); show(c); show(ia); show(ib);
        [c, ia] = setdiff(p, q); show(c); show(ia);
        [c, ia, ib] = setxor(p, q); show(c); show(ia); show(ib);
        [c, ia, ib] = intersect(p, [100 200]); show(c); show(ia); show(ib);
        [c, ia] = setdiff(q, p'); show(c); show(ia);
        [c, ia] = setdiff([4 4 4], [4 4]); show(c); show(ia);
        [c, ia, ib] = setxor([1 2], [2 1]'); show(c); show(ia); show(ib);
        [c, ia, ib] = union([7 7], [7 7]); show(c); show(ia); show(ib);
        """);

    [Fact]
    public void SetOperationsOverOtherValuesKeepTheGeneralRules() => AssertParity("""
        p = [5 3.5 NaN -0 9 0 5]; q = [3.5 0 NaN 11];
        [c, ia, ib] = union(p, q); show(c); show(ia); show(ib);
        [c, ia, ib] = intersect(p, q); show(c); show(ia); show(ib);
        [c, ia] = setdiff(p, q); show(c); show(ia);
        [c, ia, ib] = setxor(p, q); show(c); show(ia); show(ib);
        [c, ia, ib] = intersect([1 2 3], [2 3 4], 'stable'); show(c); show(ia); show(ib);
        [c, ia] = setdiff([1e9 5 -1e9], [5 2^40]); show(c); show(ia);
        show(setdiff([1 2 3], [])); show(setdiff([], [1 2 3])); show(union([], [3 1])); show(intersect(5, [5 6]));
        show(setdiff({'a', 'b', 'c'}, {'b'})); show(union(int8([1 2]), int8([2 3])));
        show(setdiff([1 -0 2], 0)); show(intersect([-0 1], [0 1]));
        """, expectRoads: false);

    [Fact]
    public void IsmemberReadsATableOverAWholeNumberSet() => AssertParity("""
        k = 1 + mod((1:100000)*7919, 5000);
        m = ismember(1:6000, k); digest(m);
        [m, loc] = ismember(1:6000, k(1:3000)); digest(m); digest(loc);
        x = (1:300000)/2;
        [m, loc] = ismember(x, k); digest(m); digest(loc);
        [m, loc] = ismember(x', k'); digest(m); digest(loc);
        s = [3 2.5 NaN -0 0 Inf 11 -7 1e300 5];
        [m, loc] = ismember(s, [5 0 3 3 -7]); show(m); show(loc);
        m = ismember(s', [5 0 3 3 -7]); show(m);
        [m, loc] = ismember(reshape(1:6, 2, 3), [6 2 2]); show(m); show(loc);
        [m, loc] = ismember([1 2], [2 1]'); show(m); show(loc);
        """);

    [Fact]
    public void IsmemberOverOtherValuesKeepsTheGeneralRules() => AssertParity("""
        [m, loc] = ismember([1 NaN 2.5 -0], [NaN 2.5 0]); show(m); show(loc);
        m = ismember([1 NaN 2.5 -0], [NaN 2.5 0 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1]); show(m);
        m = ismember(3, [1 2 3]); show(m);
        [m, loc] = ismember(3, [1 2 3 3]); show(m); show(loc);
        m = ismember([1 2 3], []); show(m);
        m = ismember({'a', 'q'}, {'q', 'r'}); show(m);
        [m, loc] = ismember([1 2; 3 4], [3 4; 9 9], 'rows'); show(m); show(loc);
        m = ismember(int8([1 5]), int8([5 6])); show(m);
        m = ismember([1 5 2^40], [2^40 5 -2^40]); show(m);
        """, expectRoads: false);

    // --- accumarray -----------------------------------------------------------------------------

    [Fact]
    public void AccumarrayOverAPackedColumnSumsInArrivalOrder() => AssertParity("""
        k = 1 + mod((1:300000)*7919, 5000);
        v = sin((1:300000)*0.37) * 1e3 + 1e-9*(1:300000);
        a = accumarray(k(:), v(:)); digest(a);
        a = accumarray(k', v); digest(a);
        a = accumarray(k' + 3, v'); digest(a); show(a(1:4));
        show(accumarray([2; 2; 5; 1], [-0; -0; 1.5; -0]));
        show(accumarray([3; 3; 3], [0.1; 0.2; 0.3]));
        show(accumarray(4, 2.5));
        show(accumarray([1; 2], [10; 20]));
        """);

    [Fact]
    public void AccumarrayOutsideTheRoadKeepsItsAnswersAndRefusals() => AssertParity("""
        show(accumarray([1; 2; 1], 1));
        show(accumarray([1 1; 2 2; 1 1], [1; 2; 3]));
        show(accumarray([1; 3], [5; 6], [4 1]));
        show(accumarray([1; 2; 1], [5; 6; 7], [], @max));
        show(accumarray([1; 2], [true; true]));
        try
            accumarray([1; 0; 2], [1; 2; 3])
        catch e
            disp(e.message);
        end
        try
            accumarray([1; 1.5], [1; 2])
        catch e
            disp(e.message);
        end
        try
            accumarray([1; NaN], [1; 2])
        catch e
            disp(e.message);
        end
        try
            accumarray([1; 2; 3], [1; 2])
        catch e
            disp(e.message);
        end
        """, expectRoads: false);

    // --- sortrows -------------------------------------------------------------------------------

    [Fact]
    public void SortrowsByOneKeyRanksInBlocks()
    {
        Run run = AssertParity("""
            n = 300000;
            T = [mod((1:n)*0.61803398875, 1)' round(mod((1:n)*0.38196601125, 1)'*5000) (1:n)'];
            T([7 90000 250000], 2) = NaN; T([8 9], 2) = [-0; 0];
            [S, i] = sortrows(T, 2); digest(S); digest(i);
            [S, i] = sortrows(T, -2); digest(S); digest(i);
            [S, i] = sortrows(T, 1); digest(S); digest(i);
            S = sortrows(T, 3, 'descend'); digest(S);
            [S, i] = sortrows(T(:, 2)); digest(S); digest(i);
            [S, i] = sortrows(T, [2 1]); digest(S); digest(i);
            t = [3 1; 1 2; NaN 3; 1 4; -0 5; 0 6; 3 7];
            [S, i] = sortrows(t, 1); show(S); show(i);
            [S, i] = sortrows(t, -1); show(S); show(i);
            [S, i] = sortrows(t); show(S); show(i);
            show(sortrows(zeros(0, 3), 2)); show(sortrows([4 5 6], 2));
            """, expectRoads: false);
        Assert.True(run.Success);
    }

    // --- text -----------------------------------------------------------------------------------

    /// <summary>40,000 keys shaped like d12's, repeated values among them, four missing.</summary>
    private const string Keys = """
        N = 40000;
        k = "R" + string(mod((1:N)*7919, 1000)') + "-" + compose("%06.3f", mod((1:N)'*0.61803, 1));
        k(1:7:N) = k(2:7:N);
        k([5 100 20000 39999]) = string(NaN);

        """;

    [Fact]
    public void SortOfTextOrdersByPackedPrefixes()
    {
        Run run = AssertParity(Keys + """
            [s, i] = sort(k); show(s(1:40)); show(s(end-40:end)); digest(i); digest(strlength(s));
            [s, i] = sort(k, 'descend'); show(s(1:40)); show(s(end-40:end)); digest(i);
            [s, i] = sort(k', 'MissingPlacement', 'first'); show(s(1:12)); digest(i);
            [s, i] = sort(k, 'descend', 'MissingPlacement', 'last'); show(s(end-12:end)); digest(i);
            [s, i] = sort(k(1:5000)); show(s(1:30)); digest(i);
            [s, i] = sort(reshape(k(1:39000), 13000, 3)); show(s(1:5, :)); digest(i);
            [s, i] = sort(reshape(k(1:600), 3, 200), 2); show(s(:, 1:6)); digest(i);
            [s, i] = sort(cellstr(k(101:9000))); show(s(1:30)); digest(i);
            [s, i] = sort(k(1:50)); show(s); show(i);
            """, expectRoads: false);
        Assert.True(run.Maps > 0, "the fast run ordered no text by its prefixes");
    }

    [Fact]
    public void SortOfAwkwardTextAgreesWithTheComparison() => AssertParity("""
        n = 3000;
        z = string(char(0));
        base = ["" "a" "ab" "abc" "abcdefgh" "abcdefghi" "abcdefgh" + z "ab" + z "ab" + z + z "Abc" "b" "abcdefgZ" ...
                "prefix_shared_0001" "prefix_shared_0001x" "prefix_shared_0002" string(char([955 945])) string(char([955 946 97])) ...
                string(char([97 98 300])) "abcdefghijklmnop" "abcdefghijklmnoq"];
        w = base(1 + mod((1:n)*7, numel(base)));
        [s, i] = sort(w); show(strlength(s)); show(double(char(s(1:60))')); digest(i);
        [s, i] = sort(w', 'descend'); show(strlength(s(1:200))); digest(i);
        [u, ia, ic] = unique(w); show(strlength(u)); show(ia); digest(ic);
        [u, ia, ic] = unique(w, 'last'); show(ia); digest(ic);
        [u, ia, ic] = unique(w, 'stable'); show(strlength(u)); show(ia); digest(ic);
        p = "sample_" + string(mod((1:n)*13, 700)) + "_tail";
        [s, i] = sort(p); show(s(1:25)); digest(i);
        g = string(char(mod((1:n)'*[3 5 7 11 13 17 19 23 29 31], 3) + 955));
        [s, i] = sort(g); show(double(char(s(1:20)))); digest(i);
        [u, ia, ic] = unique(g); digest(ia); digest(ic); show(numel(u));
        r = repmat("the same text every time", 1, 500);
        [s, i] = sort(r); show(i(1:20)); [u, ia, ic] = unique(r); show(u); show(ia); digest(ic);
        """, expectRoads: false);

    [Fact]
    public void UniqueOfTextOrdersByPackedPrefixes()
    {
        Run run = AssertParity(Keys + """
            [u, ia, ic] = unique(k); show(u(1:30)); show(u(end-8:end)); digest(ia); digest(ic); show(numel(u));
            [u, ia, ic] = unique(k, 'last'); digest(ia); digest(ic);
            [u, ia, ic] = unique(k', 'stable'); show(u(1:30)); digest(ia); digest(ic); show(size(u));
            c = cellstr(k(101:9000));
            [u, ia, ic] = unique(c); show(u(1:30)); digest(ia); digest(ic);
            [u, ia, ic] = unique(k(1:50)); show(u); show(ia); show(ic);
            [u, ia, ic] = unique(reshape(k(1:600), 200, 3), 'rows'); digest(ia); digest(ic); show(size(u));
            """, expectRoads: false);
        Assert.True(run.Maps > 0, "the fast run ordered no text by its prefixes");
    }

    // --- x(:) -----------------------------------------------------------------------------------

    [Fact]
    public void WholeColonSharesAndTheFirstWriteCopies() => AssertParity("""
        a = [1 2 3; 4 5 6];
        b = a(:); b(2) = 99; show(a); show(b);
        c = a(:); a(1, 1) = -1; show(a); show(c);
        d = a(:)'; d(3) = 7; show(a); show(d);
        a(:) = 10:15; show(a); show(c);
        e = a(:); e(end + 1) = 0; show(a); show(e);
        f = a(:) + 1; g = a(:) .* a(:); show(f); show(g); show(a);
        a = a(:); a(2) = 0; show(a);
        m = [true false; true true]; n = m(:); n(1) = false; show(m); show(n);
        v = 1:5; w = v(:); v(end + 1) = 6; show(v); show(w);
        s = 7; t = s(:); show(t);
        z = zeros(0, 3); show(z(:));
        x = bump((1:4)'); show(x);
        q = 1:4; r = bump(q(:)); show(q); show(r);
        h = int8([1 2; 3 4]); j = h(:); j(1) = 100; show(h); show(j);
        k = (1:6) * 2; l = k(:); k = []; show(l);
        function y = bump(y)
            y(1) = y(1) + 100;
        end
        """, expectRoads: false);

    [Fact]
    public void TheSwitchesTurnEveryRoadOff()
    {
        Run off = RunWith(fast: false, threads: ParallelKernels.MaxDegree, """
            k = 1 + mod((1:2000)*7919, 50);
            u = unique(k); c = intersect(k, 1:10); m = ismember(1:60, k); a = accumarray(k(:), k(:));
            """);
        Assert.True(off.Success, off.Message);
        Assert.Equal(0, off.Roads);
    }
}
