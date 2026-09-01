using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Export;
using Xunit;

namespace JGraph.Tests.Rendering;

/// <summary>
/// Text with a character the figure's font does not carry (M123).
/// </summary>
/// <remarks>
/// <para>
/// The failure this pins was invisible from the console: the markup turned <c>\in</c> into <c>∈</c>
/// correctly and the label read back correctly, and then the renderer drew a box, because Skia draws
/// one string with one face and puts <c>.notdef</c> where that face has nothing. Only the exported
/// picture was wrong.
/// </para>
/// <para>
/// So the test is written against the picture, and written the way the <c>'.'</c> marker's own defect
/// was eventually found: <b>two labels that should look different must not render identically.</b>
/// Every missing glyph is the same box, so a run of them collapses to one picture — which is exactly
/// what an assertion on the label text, the width, or the absence of an error would have missed.
/// </para>
/// </remarks>
[Collection("JG facade")]
public class GlyphFallbackM123Tests : IDisposable
{
    public GlyphFallbackM123Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private static byte[] Rendered(string title)
    {
        JG.Reset();
        AxesModel axes = JG.Gca();
        axes.Title = title;
        (int width, int height, byte[] rgba) =
            FigureExporter.RenderRgba(JG.Gcf(), new ExportOptions { Scale = 1 });
        Assert.True(width > 0 && height > 0);
        return rgba;
    }

    /// <summary>
    /// Nine symbols the report found drawing as boxes, each against the next: if any two render the
    /// same the font is drawing <c>.notdef</c> for both of them.
    /// </summary>
    [Fact]
    public void EverySymbolDrawsAsItselfAndNotAsTheSameBox()
    {
        string[] symbols = ["∈", "∉", "⊂", "∪", "∇", "⇐", "∠", "⊥", "∝"];
        var seen = new List<(string Symbol, byte[] Pixels)>();
        foreach (string symbol in symbols)
        {
            seen.Add((symbol, Rendered(symbol)));
        }

        for (int i = 0; i < seen.Count; i++)
        {
            for (int j = i + 1; j < seen.Count; j++)
            {
                Assert.False(
                    seen[i].Pixels.AsSpan().SequenceEqual(seen[j].Pixels),
                    $"'{seen[i].Symbol}' and '{seen[j].Symbol}' render identically, "
                    + "which is what a missing glyph looks like");
            }
        }
    }

    /// <summary>
    /// A symbol reached through its control word draws the same as the character itself, which is
    /// the half the markup was already getting right and must keep getting right.
    /// </summary>
    /// <remarks>
    /// Written with nothing after the control word, because a space is what ends one: TeX reads
    /// <c>\in A</c> as the symbol followed straight by <c>A</c>, so comparing it against
    /// <c>∈ A</c> would be comparing two different strings rather than two spellings of one.
    /// </remarks>
    [Fact]
    public void AControlWordDrawsWhatTheCharacterDraws() =>
        Assert.True(Rendered("\\in").AsSpan().SequenceEqual(Rendered("∈")));

    /// <summary>
    /// The guard on the fast road: plain text still goes through one call with one face, so no figure
    /// that already drew can move. Two spellings of the same ASCII must be identical, and adding one
    /// letter must not be.
    /// </summary>
    [Fact]
    public void PlainTextIsUnchanged()
    {
        Assert.True(Rendered("Speed of sound").AsSpan().SequenceEqual(Rendered("Speed of sound")));
        Assert.False(Rendered("Speed of sound").AsSpan().SequenceEqual(Rendered("Speed of sounds")));
    }
}
