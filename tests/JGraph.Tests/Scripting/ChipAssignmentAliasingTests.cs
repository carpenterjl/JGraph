using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Numerics;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// A MATLAB assignment copies the container it binds, so that <c>b = a; b(1) = 0</c> leaves
/// <c>a</c> alone. M109 lets one shape of right-hand side skip that copy: an operator's answer,
/// which is a buffer the expression itself just minted and nothing else can reach. These tests are
/// about the cases where the copy must still happen, because those are what the change risks.
/// </summary>
/// <remarks>
/// Every test runs its script twice — packing forced on, then forced off — and demands the same
/// output. The elision only ever fires for a packed array, so the boxed run is a control that says
/// the answer under test is the answer the interpreter gave before there was anything to elide.
/// </remarks>
[Collection("JG facade")]
public class ChipAssignmentAliasingTests : IDisposable
{
    public ChipAssignmentAliasingTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private static (string[] Output, bool Success, string? Message) RunWith(bool packed, string code)
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
            return (output.Normal.ToArray(), result.Success, result.Message);
        }
        finally
        {
            JgsPacking.Enabled = previous;
        }
    }

    /// <summary>
    /// Runs the script in both storage lanes and demands the one expected transcript. The recorder
    /// keeps a fragment per <c>fprintf</c> rather than a line, so the pieces are joined before the
    /// comparison and the newlines in the script are what divide the lines.
    /// </summary>
    private static void AssertBothLanes(string code, params string[] expected)
    {
        (string[] packedOut, bool packedOk, string? packedMessage) = RunWith(packed: true, code);
        (string[] boxedOut, bool boxedOk, string? boxedMessage) = RunWith(packed: false, code);

        Assert.True(packedOk, packedMessage);
        Assert.True(boxedOk, boxedMessage);
        string wanted = string.Join("\n", expected) + "\n";
        Assert.Equal(wanted, string.Concat(packedOut));
        Assert.Equal(wanted, string.Concat(boxedOut));
    }

    /// <summary>Prints a row on one line, so a transcript comparison is a value comparison.</summary>
    private const string Show = """
        function show(name, v)
            fprintf('%s:', name);
            fprintf(' %.17g', v(:).');
            fprintf('\n');
        end
        """ + "\n";

    // --- The copy that must still happen -------------------------------------------------------

    /// <summary>A name on the right is a name someone else still holds, whatever it points at.</summary>
    [Fact]
    public void ANameOnTheRightIsStillCopied()
    {
        AssertBothLanes(
            Show + """
            a = [1 2 3] + [10 20 30];
            b = a;
            b(1) = 99;
            show('a', a);
            show('b', b);
            """,
            "a: 11 22 33",
            "b: 99 22 33");
    }

    /// <summary>
    /// A user function that hands back what it was given hands back a value its caller's name still
    /// holds. The returned value is not an operator's answer, so the binding copies as it always did.
    /// </summary>
    [Fact]
    public void AFunctionThatReturnsItsArgumentIsStillCopied()
    {
        AssertBothLanes(
            Show + """
            function y = same(x)
                y = x;
            end
            a = [1 2 3] * 2;
            b = same(a);
            b(1) = 99;
            show('a', a);
            show('b', b);
            """,
            "a: 2 4 6",
            "b: 99 4 6");
    }

    /// <summary>
    /// An identity-like builtin — <c>double</c> of a double, <c>reshape</c> to the shape it already
    /// has — may answer with the very value it was handed. A call is never elided, so it copies.
    /// </summary>
    [Fact]
    public void ABuiltinThatMayAnswerWithItsArgumentIsStillCopied()
    {
        AssertBothLanes(
            Show + """
            a = [1 2 3] + 0;
            b = double(a);
            b(1) = 99;
            c = reshape(a, 1, 3);
            c(2) = 88;
            d = a(:);
            d(3) = 77;
            show('a', a);
            show('b', b);
            show('c', c);
            show('d', d);
            """,
            "a: 1 2 3",
            "b: 99 2 3",
            "c: 1 88 3",
            "d: 1 2 77");
    }

    /// <summary>
    /// A class decides what its own operators mean, and may decide they mean "give back a property".
    /// An object operand keeps the operator off the numeric roads, so the answer is copied.
    /// </summary>
    [Fact]
    public void AnOperatorOverloadThatReturnsAPropertyIsStillCopied()
    {
        AssertBothLanes(
            Show + """
            classdef Holder
                properties
                    V = [1 2 3]
                end
                methods
                    function obj = Holder(v)
                        if nargin > 0
                            obj.V = v;
                        end
                    end
                    function r = plus(a, ~)
                        r = a.V;
                    end
                    function r = uminus(a)
                        r = a.V;
                    end
                end
            end
            h = Holder([4 5 6]);
            b = h + 1;
            b(1) = 99;
            c = -h;
            c(2) = 88;
            show('V', h.V);
            show('b', b);
            show('c', c);
            """,
            "V: 4 5 6",
            "b: 99 5 6",
            "c: 4 88 6");
    }

    /// <summary>
    /// A field read and a cell read are names in every sense that matters, so what they answer with
    /// is copied — including when the value stored there was itself an elided operator answer.
    /// </summary>
    [Fact]
    public void AFieldOrCellReadIsStillCopied()
    {
        AssertBothLanes(
            Show + """
            a = [1 2 3];
            s.f = a * 2;
            t = s.f;
            t(1) = 99;
            c{1} = a * 3;
            u = c{1};
            u(1) = 88;
            show('s.f', s.f);
            show('t', t);
            show('c1', c{1});
            show('u', u);
            """,
            "s.f: 2 4 6",
            "t: 99 4 6",
            "c1: 3 6 9",
            "u: 88 6 9");
    }

    /// <summary>
    /// A global is one value under several names. Writing an operator's answer into it is safe —
    /// nothing else held that buffer — but reading it back out is a name read, and copies.
    /// </summary>
    [Fact]
    public void AGlobalReadBackOutIsStillCopied()
    {
        AssertBothLanes(
            Show + """
            function stash(v)
                global g
                g = v * 2;
            end
            function y = fetch()
                global g
                y = g;
            end
            global g
            stash([1 2 3]);
            y = fetch();
            y(1) = 99;
            show('g', g);
            show('y', y);
            """,
            "g: 2 4 6",
            "y: 99 4 6");
    }

    /// <summary>
    /// The case that decided the mechanism. A handle object holds the very value it was handed and
    /// hands it back on a later statement. Nothing is written on a value to say it was freshly
    /// minted, so no such mark can survive the store and make that later read alias what the map is
    /// still holding — which is exactly what a mark carried on the value would have done.
    /// </summary>
    [Fact]
    public void AValueAnObjectIsHoldingIsStillCopiedWhenItComesBack()
    {
        AssertBothLanes(
            Show + """
            m = containers.Map();
            a = [1 2 3];
            m('k') = a + [10 20 30];
            u = m('k');
            u(1) = 99;
            w = m('k');
            show('u', u);
            show('w', w);
            """,
            "u: 99 22 33",
            "w: 11 22 33");
    }

    /// <summary>
    /// An anonymous function captures the workspace it was written in, so a name it closes over must
    /// keep answering what it answered — a later write through another name cannot reach it.
    /// </summary>
    [Fact]
    public void AClosureKeepsWhatItCapturedWhenAnotherNameIsWritten()
    {
        AssertBothLanes(
            Show + """
            a = [1 2 3] + 0;
            f = @() a;
            b = a;
            b(1) = 99;
            c = a * 1;
            c(2) = 88;
            show('a', a);
            show('f', f());
            show('b', b);
            show('c', c);
            """,
            "a: 1 2 3",
            "f: 1 2 3",
            "b: 99 2 3",
            "c: 1 88 3");
    }

    /// <summary>
    /// A decomposition is a struct wearing a class name and holds its factors. Its operators claim
    /// the token before any numeric reading, so a struct operand is never elided and the object can
    /// be solved against twice.
    /// </summary>
    [Fact]
    public void ADecompositionSurvivesBeingSolvedAgainstTwice()
    {
        AssertBothLanes(
            Show + """
            A = [4 1; 1 3];
            d = decomposition(A);
            x = d \ [1; 2];
            x(1) = 99;
            y = d \ [1; 2];
            fprintf(' %.6f', y);
            fprintf('\n');
            """,
            " 0.090909 0.636364");
    }

    // --- The copy that may now be skipped, which must still be right ---------------------------

    /// <summary>An operator's answer is the assignee's own, and writing to it reaches no operand.</summary>
    [Fact]
    public void WritingToAnOperatorsAnswerReachesNeitherOperand()
    {
        AssertBothLanes(
            Show + """
            a = [1 2 3];
            b = [10 20 30];
            c = a + b;
            c(1) = 99;
            d = a .* a;
            d(2) = 88;
            e = -a;
            e(3) = 77;
            f = (a > 1);
            f(1) = 1;
            show('a', a);
            show('b', b);
            show('c', c);
            show('d', d);
            show('e', e);
            show('f', f);
            """,
            "a: 1 2 3",
            "b: 10 20 30",
            "c: 99 22 33",
            "d: 1 88 9",
            "e: -1 -2 77",
            "f: 1 1 1");
    }

    /// <summary>
    /// Implicit expansion, the matrix product, the recognized <c>A'*A</c> product and the blocked
    /// transpose all mint their answer, and none of them lets a write reach the matrix it read.
    /// </summary>
    [Fact]
    public void TheMatrixRoadsMintTheirAnswerToo()
    {
        AssertBothLanes(
            Show + """
            A = [1 2; 3 4];
            col = [1; 2];
            row = [10 20];
            outer = col + row;
            outer(1) = 99;
            P = A * A;
            P(1) = 88;
            G = A' * A;
            G(1) = 77;
            T = A';
            T(1) = 66;
            show('A', A);
            show('col', col);
            show('row', row);
            show('outer', outer);
            show('P', P);
            show('G', G);
            show('T', T);
            """,
            "A: 1 3 2 4",
            "col: 1 2",
            "row: 10 20",
            "outer: 99 12 21 22",
            "P: 88 15 10 22",
            "G: 77 14 14 20",
            "T: 66 2 3 4");
    }

    /// <summary>
    /// Growing an answer past its edge grows it in place where there is room. The operands it was
    /// computed from must not see the growth, nor the zero fill.
    /// </summary>
    [Fact]
    public void GrowingAnAnswerLeavesItsOperandsAlone()
    {
        AssertBothLanes(
            Show + """
            a = [1 2 3];
            b = a * 2;
            b(6) = 7;
            show('a', a);
            show('b', b);
            """,
            "a: 1 2 3",
            "b: 2 4 6 0 0 7");
    }

    /// <summary>
    /// Rebinding a name to something computed from itself drops the old value rather than writing
    /// through it, so a second name taken before the rebind keeps the old numbers.
    /// </summary>
    [Fact]
    public void RebindingANameFromItselfLeavesAnEarlierNameAlone()
    {
        AssertBothLanes(
            Show + """
            a = [1 2 3];
            before = a;
            for k = 1:3
                a = a + 1;
            end
            show('a', a);
            show('before', before);
            """,
            "a: 4 5 6",
            "before: 1 2 3");
    }

    /// <summary>
    /// An answer written into a field or a cell is that container's alone: the temporary was never
    /// anywhere else, and a second read of the container still copies.
    /// </summary>
    [Fact]
    public void AnAnswerStoredInAContainerIsThatContainersAlone()
    {
        AssertBothLanes(
            Show + """
            a = [1 2 3];
            s.f = a + 1;
            c{1} = a + 2;
            s2 = s;
            s2.f(1) = 99;
            c2 = c;
            c2{1}(1) = 88;
            show('s.f', s.f);
            show('s2.f', s2.f);
            show('c1', c{1});
            show('c21', c2{1});
            show('a', a);
            """,
            "s.f: 2 3 4",
            "s2.f: 99 3 4",
            "c1: 3 4 5",
            "c21: 88 4 5",
            "a: 1 2 3");
    }

    /// <summary>
    /// A complex answer is two planes behind one payload, and the same reasoning has to hold for
    /// both of them.
    /// </summary>
    [Fact]
    public void AComplexAnswerIsOwnedPlaneForPlane()
    {
        AssertBothLanes(
            """
            a = [1 2 3] + 1i * [4 5 6];
            b = a * 2;
            b(1) = 99;
            fprintf(' %g', real(a)); fprintf(' |'); fprintf(' %g', imag(a)); fprintf('\n');
            fprintf(' %g', real(b)); fprintf(' |'); fprintf(' %g', imag(b)); fprintf('\n');
            """,
            " 1 2 3 | 4 5 6",
            " 99 4 6 | 0 10 12");
    }

    /// <summary>
    /// An integer class is applied by the kernel that computed the element, so an answer taken
    /// rather than copied still wears its class, still saturated as it was computed, and still
    /// leaves the array it was computed from alone.
    /// </summary>
    [Fact]
    public void AnElidedAnswerKeepsItsClass()
    {
        AssertBothLanes(
            """
            a = uint8([100 200 250]);
            b = a + a;
            b(1) = 7;
            fprintf('%s', class(b));
            fprintf(' %d', b);
            fprintf('\n');
            fprintf('%s', class(a));
            fprintf(' %d', a);
            fprintf('\n');
            """,
            "uint8 7 255 255",
            "uint8 100 200 250");
    }

    // --- The mechanism the rule rests on -------------------------------------------------------

    /// <summary>
    /// The elision is allowed only where the answer's storage is not an operand's storage, and that
    /// question is asked of the wrapper's own reference. Two wrappers over one buffer must say so,
    /// two wrappers over two buffers must not, and asking must not compact anything — a value with
    /// growth capacity keeps it, because compaction reallocates and a question about identity may
    /// not move what it is asking about.
    /// </summary>
    [Fact]
    public void SharedStorageIsSeenThroughTheWrapper()
    {
        using var buffer = new ManagedBuffer(4);
        using var other = new ManagedBuffer(4);
        JgsValue first = JgsValue.Packed(buffer);
        JgsValue second = JgsValue.Packed(buffer);
        JgsValue apart = JgsValue.Packed(other);

        Assert.True(first.SharesStorageWith(second));
        Assert.True(second.SharesStorageWith(first));
        Assert.False(first.SharesStorageWith(apart));

        // Two planes behind one payload: sharing either plane is sharing.
        var planes = new JgsPackedComplex(new ManagedBuffer(4), new ManagedBuffer(4));
        JgsValue complex = JgsValue.PackedComplexArray(planes);
        JgsValue realPart = JgsValue.Packed(planes.Re);
        Assert.True(complex.SharesStorageWith(realPart));
        Assert.True(realPart.SharesStorageWith(complex));
        Assert.False(complex.SharesStorageWith(apart));

        // A boxed array and a scalar are neither shared nor asked about.
        Assert.False(JgsValue.Number(1).SharesStorageWith(first));
        Assert.False(first.SharesStorageWith(JgsValue.Number(1)));
    }

    /// <summary>
    /// A datetime and a duration are arrays wearing a time tag, and their arithmetic is not the
    /// numeric road — it resolves above every numeric reading of the operands. The tag keeps them
    /// off the elision, and the answers are the ones they were.
    /// </summary>
    [Fact]
    public void TimeArithmeticIsUntouched()
    {
        AssertBothLanes(
            """
            t = datetime(2026, 8, 30) + days(0:2);
            d = t - t(1);
            e = d * 2;
            fprintf(' %g', days(d));
            fprintf('\n');
            fprintf(' %g', days(e));
            fprintf('\n');
            fprintf(' %d', day(t));
            fprintf('\n');
            """,
            " 0 1 2",
            " 0 2 4",
            " 30 31 1");
    }
}
