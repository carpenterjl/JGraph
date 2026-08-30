using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Numerics;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// A builtin that wants a scalar number now takes a one-element array as the number it holds, which
/// is what MATLAB has always done: <c>[1]</c> and <c>1</c> are the same value there, and this program
/// already agreed with that everywhere except in the one helper every such argument goes through —
/// <c>isscalar([1])</c> was true, <c>numel([1])</c> was 1, <c>size([1])</c> was <c>[1 1]</c>,
/// <c>zeros([1])</c> built an array, and only <c>round(2.567, [1])</c> refused.
/// </summary>
/// <remarks>
/// Every expectation here was measured against MATLAB R2024a before it was written down, including
/// the refusals, and each script runs twice — packed storage forced on, then off — because the two
/// representations reach a one-element array by different routes.
/// </remarks>
[Collection("JG facade")]
public class ChipScalarArgumentTests : IDisposable
{
    public ChipScalarArgumentTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private static (bool Success, string? Message) RunWith(bool packed, string code)
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
            return (result.Success, result.Message + output.ErrorText);
        }
        finally
        {
            JgsPacking.Enabled = previous;
        }
    }

    /// <summary>Runs the script in both storage lanes; every assertion inside it must hold in each.</summary>
    private static void Accepts(string code)
    {
        foreach (bool packed in new[] { true, false })
        {
            (bool ok, string? message) = RunWith(packed, code);
            Assert.True(ok, $"packed={packed}: {message}");
        }
    }

    /// <summary>Runs the script in both storage lanes; it must be refused, in the same words, in each.</summary>
    private static void Refuses(string code)
    {
        (bool packedOk, string? packedMessage) = RunWith(packed: true, code);
        (bool boxedOk, string? boxedMessage) = RunWith(packed: false, code);

        Assert.False(packedOk, "packed storage accepted it");
        Assert.False(boxedOk, "boxed storage accepted it");
        Assert.Equal(boxedMessage, packedMessage);
    }

    [Fact]
    public void AOneElementArrayIsTheNumberItHolds() => Accepts("""
        x = [1];
        assert(isequal(round(2.567, x), round(2.567, 1)));
        assert(isequal(round(2.567, [1]), 2.6));
        assert(isequal(linspace(0, 1, [3]), [0 0.5 1]));
        assert(isequal(nchoosek(5, [2]), 10));
        assert(isequal(mod(10, [3]), 1));
        assert(isequal(num2str(pi, [4]), '3.142'));
        assert(isequal(unwrap([0 4 8], [1], 2), unwrap([0 4 8], 1, 2)));
        """);

    [Fact]
    public void AOneElementArrayReachedByAnyRouteIsTheSameNumber() => Accepts("""
        assert(isequal(round(2.567, zeros(1, 1) + 1), 2.6));
        v = [4 1 7];
        assert(isequal(round(2.567, v(2)), 2.6));
        assert(isequal(round(2.567, ones(1)), 2.6));
        assert(isequal(round(2.567, reshape(1, [1 1])), 2.6));
        """);

    /// <summary>
    /// A bare <c>true</c> has always been read as 1 here, so the one-element logical array is read the
    /// same way. MATLAB's own <c>round</c> refuses a logical while its <c>linspace</c> accepts one, so
    /// there is no single rule to copy; the rule kept is the one this helper already had.
    /// </summary>
    [Fact]
    public void AOneElementLogicalArrayIsOneOrZero() => Accepts("""
        assert(isequal(linspace(0, 1, [true]), linspace(0, 1, true)));
        assert(isequal(round(2.567, [true]), round(2.567, true)));
        mask = [4 1 7] > 5;
        assert(isequal(round(2.567, mask(1)), round(2.567, false)));
        """);

    /// <summary>MATLAB's <c>round</c> takes an <c>int32</c> or a <c>uint8</c> digit count; so does this.</summary>
    [Fact]
    public void AOneElementIntegerClassArrayIsItsValue() => Accepts("""
        assert(isequal(round(2.567, [int32(1)]), 2.6));
        assert(isequal(round(2.567, int32([1])), 2.6));
        assert(isequal(round(2.567, uint8([1])), 2.6));
        assert(isequal(round(2.567, single([1])), 2.6));
        """);

    // The nesting the unwrap descends through — a JGS zeros(1, 1), which is an array holding a
    // one-element row rather than a bare number — is deliberately not pinned by a test here. A test
    // that runs a JGS-dialect script from this class reproducibly fails JgsFigureWindowTests'
    // ReRun_ResetsNumbering and JgsDebugSessionTests' Pause_InterruptsATightLoop, two order-fragile
    // tests in this same collection, whichever seam it runs the script through. That fragility is
    // older than this class and is recorded as a task of its own; the descent is left covered by the
    // flat one-element arrays above, and bounded in the helper so it cannot spin.

    [Fact]
    public void AnEmptyArrayIsStillRefused() => Refuses("round(2.567, []);");

    [Fact]
    public void AnArrayOfMoreThanOneIsStillRefused() => Refuses("round(2.567, [1 2]);");

    [Fact]
    public void AOneElementCellIsStillRefused() => Refuses("round(2.567, {1});");

    /// <summary>
    /// MATLAB's <c>round</c> and <c>circshift</c> both refuse a complex where a count belongs rather
    /// than quietly taking its real part, so a complex stays refused here — as it always was.
    /// </summary>
    [Fact]
    public void AComplexIsStillRefused() => Refuses("round(2.567, 1 + 2i);");

    /// <summary>
    /// A one-character char is refused, because the identical bare <c>'a'</c> always was. A char
    /// matrix stores its code points as plain numbers, so this is the one case the unwrap has to
    /// exclude by name rather than by the type of what it finds inside.
    /// </summary>
    [Fact]
    public void ASingleCharacterIsStillRefused()
    {
        Refuses("round(2.567, 'a');");
        Refuses("round(2.567, ['a']);");
        Refuses("round(2.567, \"1\");");
    }

    /// <summary>
    /// The size-vector forms read their argument as a shape before any of this reaches them, and must
    /// go on doing so. MATLAB does not tell a one-element vector from a scalar here either, which is
    /// why <c>zeros([3])</c> is the same 3-by-3 as <c>zeros(3)</c> and not a 1-by-1.
    /// </summary>
    [Fact]
    public void ASizeVectorStillMeansAShape() => Accepts("""
        assert(isequal(size(zeros([3])), [3 3]));
        assert(isequal(size(zeros(3)), [3 3]));
        assert(isequal(size(zeros([2 3])), [2 3]));
        assert(isequal(size(zeros(2, 3)), [2 3]));
        assert(isequal(size(ones([1])), [1 1]));
        assert(isequal(size(zeros([1 4])), [1 4]));
        assert(isequal(size(reshape(1:6, [2 3])), [2 3]));
        assert(isequal(size(reshape(1:6, 2, 3)), [2 3]));
        assert(isequal(size(eye([2 3])), [2 3]));
        assert(isequal(size(repmat(5, [2 3])), [2 3]));
        """);

    /// <summary>
    /// <c>max(A, [], dim)</c> tells its empty second argument from a number, and must go on doing so:
    /// the empty is not a one-element array and is refused by the same helper it always was.
    /// </summary>
    [Fact]
    public void TheDimensionFormOfMaxAndMinIsUnmoved() => Accepts("""
        A = [1 2; 3 4];
        assert(isequal(max(A, [], 1), [3 4]));
        assert(isequal(max(A, [], [1]), [3 4]));
        assert(isequal(max(A, [], 2), [2; 4]));
        assert(isequal(min(A, [], [2]), [1; 3]));
        assert(isequal(sum(A, [2]), sum(A, 2)));
        assert(isequal(size([1 2 3], [2]), 3));
        assert(isequal(cat([1], [1 2], [3 4]), [1 2; 3 4]));
        assert(isequal(circshift([1 2 3], [1]), [3 1 2]));
        """);
}
