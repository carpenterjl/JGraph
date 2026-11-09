using JGraph.Api;
using JGraph.Numerics;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The every-row plan's I7 (ADR 0326): the text verbs over string arrays - the search predicates
/// scored into a packed logical, <c>compose</c> and <c>num2str</c> through a format parsed once,
/// <c>string</c> of a packed array, the string <c>+</c> chain, the extraction, insertion, padding,
/// trimming, case and edit verbs - run a block of elements per thread. Every script runs with the
/// roads on (all threads and one thread) and off, and must print the same bytes with the same
/// outcome; the arrays are long enough (40,000 elements) to cut into several blocks, and hold
/// missing strings.
/// </summary>
[Collection("JG facade")]
public class TextKernelsI7Tests : IDisposable
{
    public TextKernelsI7Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private sealed record Run(string Output, bool Success, string? Message, long Maps);

    private static Run RunWith(bool fast, int threads, string code)
    {
        bool previous = JgsTextKernels.Enabled;
        int degree = ParallelKernels.MaxDegree;
        JgsTextKernels.Enabled = fast;
        ParallelKernels.MaxDegree = threads;
        long before = JgsTextKernels.Maps;
        try
        {
            JG.Reset();
            var output = new RecordingScriptOutput();
            var context = new ScriptContext(output, (_, _) => { }, null);
            ScriptRunResult result = JgsRunner.Run(
                code + Show, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
            return new Run(output.NormalText, result.Success, result.Message, JgsTextKernels.Maps - before);
        }
        finally
        {
            JgsTextKernels.Enabled = previous;
            ParallelKernels.MaxDegree = degree;
        }
    }

    /// <summary>The roads on (every thread, then one) and off: the same bytes and the same outcome.</summary>
    private static Run AssertParity(string code, bool expectSuccess = true, bool expectMaps = true)
    {
        Run slow = RunWith(fast: false, threads: ParallelKernels.MaxDegree, code);
        Run fast = RunWith(fast: true, threads: Math.Max(2, ParallelKernels.MaxDegree), code);
        Run single = RunWith(fast: true, threads: 1, code);
        Assert.True(expectSuccess == slow.Success, slow.Message);
        Assert.Equal(slow.Success, fast.Success);
        Assert.Equal(slow.Message, fast.Message);
        Assert.Equal(slow.Output, fast.Output);
        Assert.Equal(slow.Success, single.Success);
        Assert.Equal(slow.Message, single.Message);
        Assert.Equal(slow.Output, single.Output);
        Assert.Equal(0, slow.Maps);
        if (expectMaps)
        {
            Assert.True(fast.Maps > 0, "the fast run took no I7 road");
        }

        return fast;
    }

    /// <summary>40,000 keys shaped like d12's, four of them missing.</summary>
    private const string Keys = """
        N = 40000;
        k = "R" + string(mod((1:N)*7919, 1000)') + "-" + compose("%06.3f", mod((1:N)'*0.61803, 1));
        k([5 100 20000 39999]) = string(NaN);

        """;

    /// <summary>Prints a value's class, size and every element; a missing string as &lt;M&gt;.</summary>
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
            elseif ischar(s)
                for r = 1:size(s, 1)
                    fprintf('[%s]\n', s(r, :));
                end
            else
                fprintf('%.17g ', s);
                fprintf('\n');
            end
        end
        """;

    [Fact]
    public void PredicatesOverALargeStringArrayScoreIntoALogical()
    {
        Run run = AssertParity(Keys + """
            show(contains(k, "7")); show(startsWith(k, "R1")); show(endsWith(k, "5"));
            show(matches(k, "R7-0.618")); show(count(k, "1"));
            show(contains(k, ["99" "-0.0"])); show(startsWith(k, ["R5" "R6"])); show(count(k, ["1" "2"]));
            show(contains(k, "r1", 'IgnoreCase', true)); show(count(k, "R", 'IgnoreCase', 1));
            show(endsWith(k, "")); show(contains(k, strings(1, 0)));
            fprintf('%d %d %d\n', sum(contains(k, "7")), sum(startsWith(k, "R1")), sum(endsWith(k, "5")));
            show(contains(k', "3")); show(startsWith(reshape(k(1:24), 2, 3, 4), "R"));
            """);
        Assert.True(run.Maps > 0);
    }

    [Fact]
    public void PredicatesOverOtherContainersKeepTheirAnswers() => AssertParity(Keys + """
        c = cellstr(k(1:3000)); c{7} = 'R12-x';
        show(contains(c, "-")); show(startsWith(c, 'R1')); show(count(c, '1'));
        show(contains('R12-0.5', "2")); show(endsWith("abc", "c")); show(count("aaa", "a"));
        show(contains(strings(0, 0), "a")); show(startsWith(strings(1, 0), "a")); show(count(strings(0, 3), "a"));
        show(contains(string(NaN), "a")); show(count([string(NaN) "aXa"], "a"));
        try
            contains(['ab'; 'cd'], 'a')
        catch e
            disp(e.identifier); disp(e.message);
        end
        try
            startsWith(k, 5)
        catch e
            disp(e.message);
        end
        try
            contains(k, "a", 'Ignore')
        catch e
            disp(e.message);
        end
        """);

    [Fact]
    public void StrlengthKeepsMissingAsNaN() => AssertParity(Keys + """
        show(strlength(k)); show(strlength(cellstr(k(1:20000)))); show(strlength(k(1:7)'));
        fprintf('%.17g\n', sum(strlength(k), 'omitnan'));
        """);

    [Fact]
    public void ComposeOverOneColumnFormatsEachRowOnce() => AssertParity("""
        v = repmat([0.5; -0; NaN; Inf; -Inf; 1e300; -2.5; 123456789; 1e-10; 7; 255; -17], 3400, 1);
        fmts = ["%08.5f" "%d" "%x" "%+.3e" "%G" "%#.0f" "id %5.1f%%" "%-12.4g|" "%u" "%o" "%i" "% 7.2f" "%X" "%.0e" "%#g" "%5.3d"];
        for f = fmts
            show(compose(f, v));
        end
        show(compose('%08.3f', v(1:20000)));
        show(compose('a\tb %6.2f\n', v(1:20000)));
        show(compose("%d", logical(mod((1:20000)', 3))));
        show(compose("%x", int32((1:20000)' - 300)));
        show(compose("%.4f", single(v(1:20000))));
        """, expectMaps: JgsPacking.Enabled); // the column road reads a packed buffer; boxed storage keeps its road

    [Fact]
    public void ComposeShapesOutsideTheColumnRoadStayOnTheirOwnRoad() => AssertParity("""
        v = (1:20000)' / 7;
        show(compose("%d %d", v)); show(compose("%s", v)); show(compose("%5.2f", v'));
        show(compose("%.2f-%.1f", [v v])); show(compose("%c", 65 + mod((1:20000)', 26)));
        show(compose("no operator", v(1:5))); show(compose("%%", v(1:5)));
        show(compose("%.3f", zeros(0, 1)));
        try
            compose("%d", {1, 2})
        catch e
            disp(e.identifier); disp(e.message);
        end
        try
            compose("%q", v)
        catch e
            disp(e.message);
        end
        try
            compose("%-+-d", v)
        catch e
            disp(e.message);
        end
        """, expectMaps: false);

    [Fact]
    public void Num2strWithAFormatWritesEachRowOnce() => AssertParity("""
        v = repmat([0.5; -0; NaN; Inf; -Inf; 1e10; -2.5; 123456789; 1e-10; 7], 3000, 1);
        show(num2str(v, '%.6f')); show(num2str(v, '%10.3e')); show(num2str(v, '%d'));
        show(num2str(v, '[%g]')); show(num2str(v, '%x'));
        show(num2str([v v], '%.2f ')); show(num2str(v(1:5)', '%.1f,'));
        """);

    [Fact]
    public void StringOfAPackedArrayAndTheConcatenationChain() => AssertParity(Keys + """
        ids = mod((1:N)*2654435761, 100000);
        show(string(ids)); show(string([ids NaN -0.5 1e17 3.25]')); show(string(ids / 7));
        show(k + "|" + (1:N)'); show("x" + k + "y");
        show(k(1:3)' + ["a" "b" "c"]);
        show(["a" "b" "c"] + ["1"; "2"]);
        show("<miss" + "ing>" + "x"); show(k(1:4) + {'p'; 'q'; 'r'; 's'}');
        show("v" + [1.5 NaN 2] + "w");
        try
            show(k(1:3)' + [1 2]);
        catch e
            disp(e.message);
        end
        """);

    [Fact]
    public void ExtractionAndInsertionByMarkerAndByPosition() => AssertParity(Keys + """
        show(extractBefore(k, "-")); show(extractAfter(k, "-")); show(extractAfter(k, "0."));
        show(extractBefore(k, "Q")); show(extractAfter(k, ""));
        m = repmat(["-"; "R"; "."], ceil(N/3), 1); m = m(1:N);
        show(extractBefore(k, m)); show(insertAfter(k, m, "#"));
        show(insertAfter(k, "R", "#")); show(insertBefore(k, "-", "::")); show(insertAfter(k(1:3000), "", "."));
        show(extractBefore(k(1:3000), 3)); show(extractAfter(k(1:3000), 2)); show(insertBefore(k(1:3000), 2, "_"));
        c = cellstr(k(1:3000)); c{9} = 'no marker';
        show(extractBefore(c, '-')); show(extractAfter(c, '-')); show(insertAfter(c, 'R', '#'));
        try
            show(extractBefore(['ab-c'; 'xy-z'], '-'));
        catch e
            disp(e.message);
        end
        try
            extractBefore(k(1:10), 40)
        catch e
            disp(e.identifier); disp(e.message);
        end
        """);

    [Fact]
    public void PaddingTrimmingCaseAndEdits() => AssertParity(Keys + """
        p = pad(k, 24); show(p); show(pad(k, 24, 'left')); show(pad(k, 'both', '*')); show(pad(k, 5));
        show(strtrim(p)); show(strtrim(pad(k, 24, 'left'))); show(deblank(p)); show(strip(pad(k, 'both', '*'), '*'));
        show(upper(k)); show(lower(upper(k))); show(upper(cellstr(k(1:3000))));
        show(erase(k, "-")); show(erase(k, "")); show(erase(k, ["R" "."]));
        show(replace(k, "R", "row")); show(replace(k, "", "_")); show(replace(k, ["R" "-"], ["r" "+"]));
        show(replace(cellstr(k(1:3000)), '0', 'o')); show(erase(cellstr(k(1:3000)), 'R'));
        try
            erase(k, 5)
        catch e
            disp(e.message);
        end
        try
            replace(k, "a", ["b" "c"])
        catch e
            disp(e.message);
        end
        """);

    [Fact]
    public void D12ShapedRowsAgreeOnEveryCheck() => AssertParity("""
        N = 200000;
        ids = mod((1:N)*2654435761, 100000);
        vals = mod((1:N)*0.618033988749895, 1);
        sv = compose("%08.5f", vals');
        keys = "R" + string(ids') + "-" + sv;
        head = extractBefore(keys, "-"); tail = extractAfter(keys, "-");
        joined = head(1:50000) + "|" + tail(1:50000);
        e1 = erase(keys, "-"); e2 = replace(keys, "R", "row"); e3 = insertAfter(keys(1:50000), "R", "#");
        p1 = pad(keys(1:50000), 32); p4 = lower(upper(strtrim(p1)));
        ns = num2str(vals(1:20000)', '%.6f');
        fprintf('%.17g\n', sum(strlength(sv)), size(ns, 1) * size(ns, 2), sum(strlength(keys)), ...
            sum(strlength(head)) + sum(strlength(tail)), sum(strlength(joined)), ...
            sum(contains(keys, "7")) + sum(startsWith(keys, "R1")) + sum(endsWith(keys, "5")), ...
            sum(strlength(e1)) + sum(strlength(e2)) + sum(strlength(e3)), sum(strlength(p1)) + sum(strlength(p4)));
        fprintf('%s|', keys([1 2 3 777 199999 200000]), head(9), tail(9), joined(50000), e2(4), e3(5), p4(6));
        fprintf('\n%s\n', ns(20000, :));
        """);
}
