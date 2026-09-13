using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// ADR 0159 (item 12a): a paren read through a colon range, a lone ':' or a scalar on a packed
/// array is a copy off the buffer when its bounds are unclassed whole numbers inside the extent,
/// and nothing may notice. Every test here runs its script twice, the copy path forced on and
/// then forced off, and the printed output must be byte-identical at seventeen significant digits;
/// a script that must fail has to fail with the same words both ways. The scripts lean on the
/// places the two roads could split: the shape rules for vectors, matrices and one element, empty
/// and descending ranges, <c>end</c>-relative bounds, positions outside the extent (low, high and
/// zero), fractional and classed bounds, logical, integer, char and time targets, and bounds with
/// side effects that must be evaluated exactly once.
/// </summary>
[Collection("JG facade")]
public class AffineIndexM159Tests : IDisposable
{
    public AffineIndexM159Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private static (string[] Output, bool Success, string? Message, long Reads) RunWith(bool affine, string code)
    {
        bool previous = JgsAffineIndex.Enabled;
        JgsAffineIndex.Enabled = affine;
        long before = JgsAffineIndex.Reads;
        try
        {
            JG.Reset();
            var output = new RecordingScriptOutput();
            var figures = new List<FigureModel>();
            var context = new ScriptContext(output, (_, figure) => figures.Add(figure), null);
            ScriptRunResult result = JgsRunner.Run(
                code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
            return (output.Normal.ToArray(), result.Success, result.Message, JgsAffineIndex.Reads - before);
        }
        finally
        {
            JgsAffineIndex.Enabled = previous;
        }
    }

    /// <summary>
    /// Runs the script (the shared data ahead of <paramref name="body"/>, the helpers after it) both
    /// ways and asserts byte-identical output. When <paramref name="expectCopied"/> is set,
    /// additionally asserts that the copy path really ran (or really refused) — a fast path that
    /// silently never fires would pass every parity test while buying nothing.
    /// </summary>
    private static string[] AssertParity(string body, bool expectSuccess = true, bool? expectCopied = null)
    {
        string code = Data + body + Helpers;
        (string[] fastOut, bool fastOk, string? fastMessage, long reads) = RunWith(affine: true, code);
        (string[] walkOut, bool walkOk, string? walkMessage, long walkReads) = RunWith(affine: false, code);

        Assert.Equal(0, walkReads);
        Assert.Equal(walkOk, fastOk);
        if (expectSuccess)
        {
            Assert.True(walkOk, walkMessage);
        }
        else
        {
            Assert.False(walkOk);
            Assert.Equal(walkMessage, fastMessage); // the refusal must read the same too
        }

        Assert.Equal(walkOut, fastOut);
        if (expectCopied is { } expected)
        {
            // The copy path serves packed storage only: in the unpacked lane (JGRAPH_JGS_PACKED=0)
            // nothing is packed, no read is a copy, and both roads are the general gather.
            Assert.Equal(expected && JgsPacking.Enabled, reads > 0);
        }

        return fastOut;
    }

    private const string Data = """
        phi = 0.618033988749895;
        x = mod((1:5000) * phi, 1) - 0.5;
        x(7) = -0;
        x(11) = NaN;
        M = reshape(mod((1:6000) * phi, 1), 60, 100);
        global calls
        calls = 0;

        """;

    private const string Helpers = """

        function show(name, v)
            fprintf('%s|%d %d|%s|%.17g\n', name, size(v, 1), size(v, 2), class(v), sum(double(v(:)), 'omitnan'));
        end
        function showbits(name, v)
            fprintf('%s|%d %d|%s\n', name, size(v, 1), size(v, 2), reshape(num2hex(double(v(:))).', 1, []));
        end
        function n = tick(n)
            global calls
            calls = calls + 1;
            fprintf('tick %d -> %g\n', calls, n);
        end
        """;

    [Fact]
    public void VectorRangesCopyTheSameElementsAndShapes()
    {
        string[] output = AssertParity("""
            showbits('full', x(1:5000));
            showbits('prefix', x(1:100));
            showbits('inner', x(5:20));
            showbits('step2', x(1:2:end));
            showbits('step7', x(3:7:end));
            showbits('desc', x(end:-1:1));
            showbits('desc3', x(end-5:-3:2));
            showbits('one', x(5:5));
            showbits('end_only', x(end:end));
            showbits('signed_zero', x(6:8));
            showbits('nan_payload', x(10:12));
            xc = x';
            showbits('col_read', xc(2:2:end));
            showbits('col_one', xc(9:9));
            showbits('matrix_linear', M(3:3:300));
            show('matrix_linear_shape', M(3:3:300));
            """, expectCopied: true);
        Assert.Equal(15, output.Length);
    }

    [Fact]
    public void TwoSubscriptBlocksCopyTheSameElementsAndShapes() => AssertParity("""
        show('row', M(17, :));
        show('col', M(:, 41));
        show('colblock', M(:, 21:40));
        show('rowblock', M(11:30, :));
        show('subblock', M(11:20, 21:40));
        show('stepped', M(1:3:end, end:-2:1));
        show('scalar_range', M(7, 10:90));
        show('range_scalar', M(10:50, 7));
        show('one_by_range', M(7, 10:10));
        show('range_by_one', M(10:10, 7));
        show('element', M(7, 9));
        showbits('block_bits', M(11:20, 21:40));
        showbits('stepped_bits', M(1:3:end, end:-2:1));
        showbits('all_all', M(:, :));
        v = x(1:100);
        show('vector_row_all', v(1, :));
        show('vector_all_range', v(:, 5:10));
        show('vector_one_one', v(1, 1:1));
        """, expectCopied: true);

    [Fact]
    public void EmptyRangesTakeTheGeneralPathAndAgree() => AssertParity("""
        show('empty_desc', x(5:1));
        show('empty_step', x(1:-1:5));
        show('empty_nan', x(1:NaN));
        show('empty_col_range', M(:, 5:1));
        show('empty_row_range', M(5:1, :));
        show('empty_both', M(5:1, 5:1));
        z0 = zeros(0, 3);
        show('empty_target_all', z0(:, 1:0));
        """);

    /// <summary>
    /// A fractional stop with a whole start and step is still a copy (the colon's own count makes
    /// <c>1:2.5</c> two elements); a fractional step or a classed bound is the general path. Both
    /// roads must agree either way, so the copy count is not asserted here.
    /// </summary>
    [Fact]
    public void FractionalAndClassedBoundsAgree() => AssertParity("""
        show('frac_stop', x(1:2.5));
        show('frac_step', x(1:0.5:1));
        show('uint8_bounds', x(uint8(2):5));
        show('uint8_saturating', x(uint8(254):258));
        show('int32_step', x(1:int32(3):20));
        show('single_bound', x(single(3):9));
        show('logical_bound', x(true:3));
        show('block_uint8', M(uint8(2):4, 3:5));
        """);

    [Fact]
    public void PositionsOutsideTheExtentFailWithTheSameWords()
    {
        AssertParity("y = x(0:5);", expectSuccess: false);
        AssertParity("y = x(4990:5010);", expectSuccess: false);
        AssertParity("y = x(-3:-1:-10);", expectSuccess: false);
        AssertParity("y = x(5001:5001);", expectSuccess: false);
        AssertParity("y = x(2:0.5:3);", expectSuccess: false);
        AssertParity("y = M(0:2, 1);", expectSuccess: false);
        AssertParity("y = M(1:3, 99:101);", expectSuccess: false);
        AssertParity("y = M(61, :);", expectSuccess: false);
        AssertParity("y = M(1.5, :);", expectSuccess: false);
        AssertParity("y = x(1:0:5);", expectSuccess: false);
    }

    [Fact]
    public void OtherTargetsKeepTheirClassAndTag() => AssertParity("""
        mask = x > 0;
        show('logical_slice', mask(1:2:end));
        mk = reshape(mask(1:600), 20, 30);
        show('logical_block', mk(2:5, :));
        u = uint8(mod((1:1000) * 7, 251));
        show('uint8_slice', u(3:3:end));
        i16 = reshape(int16(mod((1:400) * 13, 200)) - 100, 20, 20);
        show('int16_block', i16(1:5, 2:2:20));
        c = char(65 + mod(reshape((1:600) * 3, 20, 30), 26));
        show('char_rows', c(3:7, :));
        show('char_slice', c(2:2:end, 5:20));
        fprintf('%s\n', c(4, 1:10));
        s = 'abcdefghij';
        fprintf('%s|%s\n', s(2:5), s(end:-1:1));
        d = datetime(2024, 1, 1) + days(0:29);
        e = d(5:10);
        fprintf('%s|%s|%d\n', class(e), datestr(e(1), 'yyyy-mm-dd'), numel(e));
        z = complex(x(1:20), x(21:40));
        show('complex_slice', real(z(2:2:end)) + 2 * imag(z(2:2:end)));
        """);

    [Fact]
    public void BoundsWithSideEffectsAreEvaluatedOnceInOrder() => AssertParity("""
        y = x(tick(3):tick(2):tick(30));
        show('ticked', y);
        fprintf('calls %d\n', calls);
        z = M(tick(2):tick(5), tick(3):tick(1):tick(7));
        show('ticked_block', z);
        fprintf('calls %d\n', calls);
        w = x(tick(uint8(10)):tick(1):tick(20));
        show('ticked_general', w);
        fprintf('calls %d\n', calls);
        """, expectCopied: true);

    [Fact]
    public void ARowReadInsideALoopAgreesEveryIteration() => AssertParity("""
        acc = 0;
        for k = 1:60
            r = M(k, :);
            acc = acc + r(k) + sum(r(1:2:end));
        end
        fprintf('%.17g\n', acc);
        c = cumsum(x(1:2:end));
        fprintf('%.17g|%d\n', c(end), numel(c));
        """, expectCopied: true);
}
