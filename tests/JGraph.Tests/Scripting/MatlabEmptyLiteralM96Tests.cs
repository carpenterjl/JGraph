using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M96b: MATLAB's <c>[]</c> is 0-by-0, and everything downstream of it agrees (ADR 0097).
/// </summary>
/// <remarks>
/// MATLAB has more than one empty. <c>[]</c> is 0-by-0, <c>zeros(1, 0)</c> is a row that happens to
/// hold nothing, and <c>zeros(0, 1)</c> is such a column. They are not interchangeable: every reader
/// that walks "the first non-singleton dimension" walks down a 0-by-0 and across a 1-by-0, so
/// <c>fft([], 4)</c> is a 4-by-0 empty where <c>fft(zeros(1, 0), 4)</c> is a 1-by-4 of zeros. This
/// build minted the literal as 1-by-0 until M96b, which made it the wrong one everywhere.
///
/// Every expectation in this file was read out of MATLAB itself (<c>matlab.exe -batch</c>) rather
/// than derived from the documentation — the empty-array corners are thinly documented, and several
/// of them, all-empty concatenation especially, are only knowable by asking. Each test runs its
/// script twice, packed storage and boxed, because the two lanes read an empty's shape by different
/// routes and had disagreed about it.
/// </remarks>
[Collection("JG facade")]
public class MatlabEmptyLiteralM96Tests : IDisposable
{
    public MatlabEmptyLiteralM96Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    /// <summary>Prints one <c>class RxC n=count</c> line per value, so a shape cannot hide.</summary>
    private const string Probe = """
        function p(v)
            fprintf('%s %dx%d n=%d\n', class(v), size(v, 1), size(v, 2), numel(v));
        end
        """ + "\n";

    private static string[] RunWith(bool packed, string code)
    {
        bool previous = JgsPacking.Enabled;
        JgsPacking.Enabled = packed;
        try
        {
            JG.Reset();
            var output = new RecordingScriptOutput();
            var figures = new List<FigureModel>();
            var context = new ScriptContext(output, (_, figure) => figures.Add(figure), null);
            ScriptRunResult result = JgsRunner.Run(
                code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
            Assert.True(result.Success, result.Message);
            return Array.ConvertAll(output.Normal.ToArray(), static line => line.Trim());
        }
        finally
        {
            JgsPacking.Enabled = previous;
        }
    }

    /// <summary>Runs the probe under both storage models and checks the lines against MATLAB's.</summary>
    private static void AssertShapes(string script, params string[] expected)
    {
        Assert.Equal(expected, RunWith(packed: false, Probe + script));
        Assert.Equal(expected, RunWith(packed: true, Probe + script));
    }

    private static void AssertHolds(string script)
    {
        RunWith(packed: false, script);
        RunWith(packed: true, script);
    }

    [Fact]
    public void TheEmptyLiteralIsZeroByZero()
    {
        AssertShapes(
            "p([]); p([[]]); p([[]; []]); p([[], []]); p([]'); p(zeros(0, 0)); p(zeros(1, 0));",
            "double 0x0 n=0",
            "double 0x0 n=0",
            "double 0x0 n=0",
            "double 0x0 n=0",
            "double 0x0 n=0",  // transposing nothing leaves it shapeless
            "double 0x0 n=0",
            "double 1x0 n=0"); // the row empty is a different value and keeps its own shape
    }

    /// <summary>
    /// The two empties send a "first non-singleton dimension" reader in different directions, which
    /// is the whole reason the literal's shape is worth being right about.
    /// </summary>
    [Fact]
    public void TheTwoEmptiesArePulledApartByEveryDefaultDimension()
    {
        AssertShapes(
            """
            p(fft([])); p(fft([], 4)); p(fft(zeros(1, 0), 4));
            p(sum([], 1)); p(sum([], 2)); p(cumsum([])); p(sort([]));
            """,
            "double 0x0 n=0",
            "double 4x0 n=0",  // four points asked of no columns at all
            "double 1x4 n=4",  // the same request of a row empty: four zeros
            "double 1x0 n=0",
            "double 0x1 n=0",
            "double 0x0 n=0",
            "double 0x0 n=0");
    }

    /// <summary>
    /// An empty is omitted from a concatenation, which is what makes <c>out = [out; row]</c> work
    /// from <c>out = []</c> — and, when every piece is empty, the ones that are not 0-by-0 decide
    /// the shape between them.
    /// </summary>
    [Fact]
    public void ConcatenationOmitsAnEmptyAndKeepsTheShapeOfWhatIsLeft()
    {
        AssertShapes(
            """
            p([[], 1]); p([[]; 1]); p([[], [1 2 3]]); p([[], [1 2; 3 4]]); p([[], 'ab']);
            p([zeros(0, 0), zeros(1, 0)]); p([zeros(0, 0), zeros(0, 3)]);
            p([zeros(0, 3), zeros(0, 2)]); p([zeros(0, 0), zeros(0, 0)]);
            p([zeros(0, 0); zeros(0, 1)]); p([zeros(3, 0); zeros(2, 0)]);
            p(horzcat([], [1 2])); p(vertcat([], [1 2])); p(cat(1, [], [1 2])); p(cat(2, [], [1 2]));
            p(horzcat()); p(cat(1, zeros(0, 0), zeros(0, 0))); p(cat(3, [], []));
            """,
            "double 1x1 n=1",
            "double 1x1 n=1",
            "double 1x3 n=3",
            "double 2x2 n=4",
            "char 1x2 n=2",    // the empty is dropped before the kind of the join is decided
            "double 1x0 n=0",
            "double 0x3 n=0",
            "double 0x5 n=0",
            "double 0x0 n=0",
            "double 0x1 n=0",
            "double 5x0 n=0",
            "double 1x2 n=2",
            "double 1x2 n=2",
            "double 1x2 n=2",
            "double 1x2 n=2",
            "double 0x0 n=0",
            "double 0x0 n=0",
            "double 0x0 n=0");
    }

    /// <summary>
    /// <c>out = []; out(k) = v</c> is how a great many MATLAB scripts fill a result they did not
    /// size in advance, and the shapeless empty has to grow for it. A column empty grows downwards.
    /// </summary>
    [Fact]
    public void AnEmptyGrowsWhenWrittenPastItsEnd()
    {
        AssertShapes(
            """
            q = []; q(3) = 7; p(q);
            q = []; q(end + 1) = 7; p(q);
            q = []; q(1:3) = [1 2 3]; p(q);
            q = zeros(0, 1); q(3) = 7; p(q);
            q = zeros(0, 3); q(3) = 7; p(q);
            q = [1; 2]; q(4) = 7; p(q);
            q = []; q(2, 3) = 7; p(q);
            q = []; q(:, 1) = [1; 2]; p(q);
            q = []; q(1, :) = [1 2]; p(q);
            q = []; q(end + 1, :) = [1 2]; q(end + 1, :) = [3 4]; p(q);
            q = zeros(0, 3); q(:, 1) = 5; p(q);
            """,
            "double 1x3 n=3",
            "double 1x1 n=1",
            "double 1x3 n=3",
            "double 3x1 n=3",  // zeros(0, 1) is a column and grows as one
            "double 1x3 n=3",
            "double 4x1 n=4",
            "double 2x3 n=6",
            "double 2x1 n=2",  // ':' over a shapeless empty takes the right-hand side's extent
            "double 1x2 n=2",
            "double 2x2 n=4",
            "double 0x3 n=0"); // zeros(0, 3) has a shape already; ':' writes into no rows
    }

    [Fact]
    public void GrowingFromNothingFillsWithZerosAndKeepsTheValuesWritten()
    {
        AssertHolds("""
            q = []; q(3) = 7;
            assert(isequal(q, [0 0 7]));
            r = []; for k = 1:3, r = [r; k]; end
            assert(isequal(r, [1; 2; 3]));
            s = []; for k = 1:3, s = [s, k]; end
            assert(isequal(s, [1 2 3]));
            """);
    }

    [Fact]
    public void DeletingEveryElementLeavesTheShapelessEmpty()
    {
        AssertShapes(
            """
            q = [1 2 3]; q(:) = []; p(q);
            q = [1; 2; 3]; q(:) = []; p(q);
            q = magic(3); q(:) = []; p(q);
            q = [1 2 3 4]; q(2) = []; p(q);
            q = [1 2 3]; q(1:3) = []; p(q);
            q = [1; 2; 3]; q(1:3) = []; p(q);
            q = []; q(:) = []; p(q);
            q = zeros(0, 3); q([]) = []; p(q);
            """,
            "double 0x0 n=0",
            "double 0x0 n=0",
            "double 0x0 n=0",
            "double 1x3 n=3",
            "double 1x0 n=0",  // deleting by index keeps the orientation
            "double 0x1 n=0",
            "double 0x0 n=0",
            "double 0x3 n=0"); // deleting nothing from an empty leaves its shape alone
    }

    /// <summary>
    /// The shape of <c>A(idx)</c> is the index's unless both are vectors, and an empty index obeys
    /// the same rule — which is why <c>v([])</c> is 0-by-0 and <c>v(zeros(1, 0))</c> is 1-by-0.
    /// </summary>
    [Fact]
    public void AnEmptySubscriptTakesItsShapeFromTheIndex()
    {
        AssertShapes(
            """
            v = [1 2 3]; m = magic(3); c = {1, 2, 3};
            p(v([])); p(v(zeros(1, 0))); p(v(zeros(0, 1))); p(v(zeros(0, 3)));
            p(m([])); p(m([], :)); p(m(:, []));
            p(c([])); p(c(:));
            e = []; p(e(:)); f = zeros(0, 3); p(f(:));
            """,
            "double 0x0 n=0",
            "double 1x0 n=0",
            "double 1x0 n=0",  // a 0-by-1 index is a vector, so the row's orientation wins
            "double 0x3 n=0",
            "double 0x0 n=0",
            "double 0x3 n=0",
            "double 3x0 n=0",
            "cell 0x0 n=0",
            "cell 3x1 n=3",    // c(:) flattens to a column, the same way A(:) does
            "double 0x1 n=0",
            "double 0x1 n=0");
    }

    /// <summary>
    /// A reduction of nothing answers its fold's identity, and the shape it answers in is decided by
    /// the dimension it ran along. The 0-by-0 is the exception MATLAB documents: it has no dimension
    /// worth picking, so <c>sum([])</c> is the scalar 0.
    /// </summary>
    [Fact]
    public void EveryReductionOfNothingAnswersInTheShapeItReducedTo()
    {
        AssertShapes(
            """
            p(sum([])); p(prod([])); p(mean([])); p(any([])); p(all([]));
            p(sum(zeros(0, 3))); p(sum(zeros(3, 0))); p(sum(zeros(0, 0), 1)); p(sum(zeros(0, 0), 2));
            p(sum(zeros(0, 3), 2)); p(mean(zeros(0, 3))); p(median(zeros(3, 0)));
            p(cumsum(zeros(0, 3))); p(sort(zeros(3, 0)));
            p(diff(zeros(3, 0))); p(diff(zeros(0, 3), 1, 2));
            """,
            "double 1x1 n=1",
            "double 1x1 n=1",
            "double 1x1 n=1",
            "logical 1x1 n=1",
            "logical 1x1 n=1",
            "double 1x3 n=3",
            "double 1x0 n=0",
            "double 1x0 n=0",
            "double 0x1 n=0",
            "double 0x1 n=0",
            "double 1x3 n=3",
            "double 1x0 n=0",
            "double 0x3 n=0",  // a shape-keeping reduction answers as long as it was asked
            "double 3x0 n=0",
            "double 2x0 n=0",  // diff answers one shorter along the dimension it walked
            "double 0x2 n=0");
    }

    [Fact]
    public void EveryReductionOfNothingAnswersItsIdentity()
    {
        AssertHolds("""
            assert(sum([]) == 0);
            assert(prod([]) == 1);
            assert(isnan(mean([])));
            assert(isnan(median([])));
            assert(~any([]));
            assert(all([]));
            assert(isequal(sum(zeros(0, 3)), [0 0 0]));
            assert(all(isnan(mean(zeros(0, 3)))));
            """);
    }

    /// <summary>
    /// An extreme of nothing is nothing, and it keeps a shape: a slice with no elements answers no
    /// value, leaving the reduced dimension zero long, while no slice at all collapses it to one.
    /// </summary>
    [Fact]
    public void AnExtremeOfNothingKeepsTheShapeItReducedOver()
    {
        AssertShapes(
            """
            p(max([])); p(min([])); p(max([], 1));
            p(max(zeros(0, 3))); p(max(zeros(0, 3), [], 2)); p(max(zeros(3, 0)));
            p(max(zeros(3, 0), [], 2)); p(max(zeros(0, 3), [], 'all'));
            p(max(zeros(0, 3), 5));
            """,
            "double 0x0 n=0",
            "double 0x0 n=0",
            "double 0x0 n=0",  // the two-argument elementwise form keeps the array's shape
            "double 0x3 n=0",
            "double 0x1 n=0",
            "double 1x0 n=0",
            "double 3x0 n=0",
            "double 0x1 n=0",
            "double 0x3 n=0");
    }

    /// <summary>The shape-preserving readers, which used to mint a bare row for every empty.</summary>
    [Fact]
    public void TheReadersThatAnswerAnEmptyGiveItAShape()
    {
        AssertShapes(
            """
            p(find([])); p(find(zeros(1, 0))); p(find(zeros(0, 3)));
            p(find([1 2 3] > 5)); p(find(magic(3) > 100));
            p(unique([])); p(unique(zeros(1, 0))); p(unique(zeros(0, 3)));
            p(fliplr([])); p(flipud(zeros(0, 3))); p(flip(zeros(3, 0)));
            p(diag([])); p(diag(zeros(0, 3))); p(inv([])); p(cellfun(@(x) x, {}));
            p(repmat([], 2, 3)); p(zeros(size([]))); p(num2str([]));
            """,
            "double 0x0 n=0",
            "double 1x0 n=0",
            "double 0x1 n=0",  // anything not searched as a row answers a column
            "double 1x0 n=0",
            "double 0x1 n=0",
            "double 0x1 n=0",  // unique has no 0-by-0 case: only a row keeps the row
            "double 1x0 n=0",
            "double 0x1 n=0",
            "double 0x0 n=0",
            "double 0x3 n=0",
            "double 3x0 n=0",
            "double 0x0 n=0",
            "double 0x1 n=0",
            "double 0x0 n=0",
            "double 0x0 n=0",
            "double 0x0 n=0",
            "double 0x0 n=0",
            "char 0x0 n=0");
    }

    /// <summary>
    /// <c>[] * []</c> walked off the end of an array outright before M96b. A product over an empty
    /// still has a shape, and it is not always empty: summing over no terms fills a real rectangle.
    /// </summary>
    [Fact]
    public void AProductOverNothingHasAShape()
    {
        AssertShapes(
            """
            p([] * []); p([] * 1); p(zeros(0, 0) * zeros(0, 3)); p(ones(2, 3) * zeros(3, 0));
            p(zeros(2, 0) * zeros(0, 3));
            """,
            "double 0x0 n=0",
            "double 0x0 n=0",
            "double 0x3 n=0",
            "double 2x0 n=0",
            "double 2x3 n=6"); // every element a sum over no terms, which is zero

        AssertHolds("assert(isequal(zeros(2, 0) * zeros(0, 3), zeros(2, 3)));");
    }

    /// <summary>The other two empty literals, which carry the same shape as <c>[]</c>.</summary>
    [Fact]
    public void TheEmptyTextAndTheEmptyCellAreZeroByZeroToo()
    {
        AssertShapes(
            "p(''); p({}); p(['' '']); p(['' 'a']); p([{}, {1}]);",
            "char 0x0 n=0",
            "cell 0x0 n=0",
            "char 0x0 n=0",
            "char 1x1 n=1",
            "cell 1x1 n=1");
    }

    /// <summary>
    /// An <c>arguments</c> block fits an empty to the size it declared rather than refusing it —
    /// MATLAB reshapes one, so <c>f([])</c> against <c>x (1,:) double</c> sees a 1-by-0 and against
    /// <c>x (:,1)</c> sees a 0-by-1. This is what a 0-by-0 literal would otherwise have broken: it
    /// had passed every <c>(1,:)</c> declaration by being a 1-by-0 row.
    /// </summary>
    [Fact]
    public void AnArgumentsBlockFitsAnEmptyToTheSizeItDeclared()
    {
        AssertShapes(
            """
            p(takeRow([])); p(takeRow(zeros(1, 0))); p(takeRow(zeros(0, 1)));
            p(takeCol([])); p(takeCol(zeros(1, 0)));
            p(takeTwoRows([])); p(takeTwoRows(zeros(2, 0))); p(takeAny([])); p(takeAny(zeros(0, 3)));
            function v = takeRow(v)
                arguments
                    v (1, :) double
                end
            end
            function v = takeCol(v)
                arguments
                    v (:, 1) double
                end
            end
            function v = takeTwoRows(v)
                arguments
                    v (2, :) double
                end
            end
            function v = takeAny(v)
                arguments
                    v (:, :) double
                end
            end
            """,
            "double 1x0 n=0",
            "double 1x0 n=0",
            "double 1x0 n=0",  // a column empty is turned over to match the declaration
            "double 0x1 n=0",
            "double 0x1 n=0",
            "double 2x0 n=0",  // the shapeless empty takes whatever rows were asked for
            "double 2x0 n=0",
            "double 0x0 n=0",
            "double 0x3 n=0"); // a shape that fits already is kept, not refitted

        // An empty with a shape of its own to keep is still refused — zeros(0, 3) is not a vector,
        // and zeros(1, 0) has a real 1 where (2,:) asked for 2 — and so is a size that cannot hold
        // nothing, which is what makes (1,1) the way a script says "a scalar, and not []".
        AssertHolds("""
            assert(~isempty(refuses('takeRow(zeros(0, 3));')));
            assert(~isempty(refuses('needsScalar([]);')));
            assert(~isempty(refuses('takeTwoRows(zeros(1, 0));')));
            assert(isempty(refuses('takeRow([]);')));
            assert(isempty(refuses('takeTwoRows([]);')));
            function why = refuses(code)
                why = '';
                try
                    evalin('caller', code);
                catch e
                    why = e.message;
                end
            end
            function v = takeRow(v)
                arguments
                    v (1, :) double
                end
            end
            function v = needsScalar(v)
                arguments
                    v (1, 1) double
                end
            end
            function v = takeTwoRows(v)
                arguments
                    v (2, :) double
                end
            end
            """);
    }

    /// <summary>
    /// The questions a script actually asks of an empty, which must not have moved: emptiness,
    /// count and length read the same whichever empty it is.
    /// </summary>
    [Fact]
    public void TheOrdinaryQuestionsAboutAnEmptyReadTheSame()
    {
        AssertHolds("""
            assert(isempty([]));
            assert(numel([]) == 0);
            assert(length([]) == 0);
            assert(ndims([]) == 2);
            assert(isequal(size([]), [0 0]));
            assert(~isrow([]));
            assert(~iscolumn([]));
            assert(~isvector([]));
            assert(~isscalar([]));
            assert(ismatrix([]));
            assert(isequal([], zeros(0, 0)));
            assert(isa([], 'double'));
            assert(isempty(''));
            assert(isempty({}));
            """);
    }
}
