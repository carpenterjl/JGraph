using JGraph.Core.Drawing;
using JGraph.Core.Primitives;
using JGraph.Rendering.Skia;
using SkiaSharp;
using Xunit;

namespace JGraph.Tests.Rendering;

/// <summary>
/// M119 — the point marker. <c>'.'</c> is the marker MATLAB scripts reach for most often and the one
/// glyph in the set with no outline: it is not a shape with a fill inside it, it <em>is</em> its
/// fill. That made it the only case that read the fill paint whether or not a face colour had been
/// asked for, and a series drawn with <c>'.'</c> carries an edge colour and no face colour — so it
/// painted in whatever the previous caller had left on the shared paint, which was nothing.
/// </summary>
/// <remarks>
/// Checked against real Skia output rather than a recording double, because the defect was entirely
/// in what reached the canvas: the markers were mapped, batched and handed over correctly, and every
/// count a recording context keeps was right. Only the pixels were missing.
/// </remarks>
public class PointMarkerTests
{
    private static readonly Color Red = Color.FromRgb(255, 0, 0);
    private static readonly Color Blue = Color.FromRgb(0, 0, 255);

    private static SKBitmap Render(int size, Action<SkiaRenderContext> draw)
    {
        var info = new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using SKSurface surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.White);
        using (var context = new SkiaRenderContext(surface.Canvas, new Size2D(size, size)))
        {
            draw(context);
        }

        surface.Canvas.Flush();
        using SKImage image = surface.Snapshot();
        return SKBitmap.FromImage(image);
    }

    /// <summary>The defect: nothing was drawn, at any size, for any series.</summary>
    [Fact]
    public void APointWithNoFaceColourIsDrawnInItsEdgeColour()
    {
        using SKBitmap bitmap = Render(48, c => c.DrawMarkers(
            [new Point2D(24, 24)],
            new MarkerStyle(MarkerType.Point, 24, fill: null, edge: Red),
            Red));

        SKColor middle = bitmap.GetPixel(24, 24);
        Assert.True(middle.Red > 200 && middle.Green < 60 && middle.Blue < 60, $"middle was {middle}");

        // And only there: the dot is a third of the size it is given, which is MATLAB's own rule for
        // this one marker, so the corner of the canvas is untouched.
        SKColor corner = bitmap.GetPixel(2, 2);
        Assert.Equal(SKColors.White, corner);
    }

    /// <summary>A face colour, when there is one, still wins — the fallback is a fallback.</summary>
    [Fact]
    public void AFaceColourStillPaintsThePoint()
    {
        using SKBitmap bitmap = Render(48, c => c.DrawMarkers(
            [new Point2D(24, 24)],
            new MarkerStyle(MarkerType.Point, 24, fill: Blue, edge: Red),
            Red));

        SKColor middle = bitmap.GetPixel(24, 24);
        Assert.True(middle.Blue > 200 && middle.Red < 60, $"middle was {middle}");
    }

    /// <summary>
    /// The neighbours are unmoved: an open circle is an outline in the edge colour with the paper
    /// showing through it, which is what makes it the open marker MATLAB means.
    /// </summary>
    [Fact]
    public void AnOpenCircleIsStillOutlineOnlyWithItsMiddleUnpainted()
    {
        using SKBitmap bitmap = Render(48, c => c.DrawMarkers(
            [new Point2D(24, 24)],
            new MarkerStyle(MarkerType.Circle, 24, fill: null, edge: Red),
            Red));

        Assert.Equal(SKColors.White, bitmap.GetPixel(24, 24));

        // The rim is a hairline and antialiased, so no pixel of it is fully saturated; what marks it
        // out is that the ink is red rather than grey. The row through the middle crosses it twice.
        int crossings = 0;
        for (int x = 0; x < 48; x++)
        {
            SKColor at = bitmap.GetPixel(x, 24);
            if (at.Red > at.Green + 40)
            {
                crossings++;
            }
        }

        Assert.True(crossings >= 2, $"expected two rim crossings on the middle row, found {crossings}");
    }

    /// <summary>
    /// A point that is asked to be large is large. The floor of one device unit is what keeps a
    /// small one visible at all, and it is why the smallest sizes cannot be told apart.
    /// </summary>
    [Fact]
    public void ALargePointIsWiderThanASmallOne()
    {
        static int Painted(double markerSize)
        {
            using SKBitmap bitmap = Render(64, c => c.DrawMarkers(
                [new Point2D(32, 32)],
                new MarkerStyle(MarkerType.Point, markerSize, fill: null, edge: Red),
                Red));

            int count = 0;
            for (int x = 0; x < 64; x++)
            {
                if (bitmap.GetPixel(x, 32).Red > 128 && bitmap.GetPixel(x, 32).Green < 128)
                {
                    count++;
                }
            }

            return count;
        }

        Assert.True(Painted(6) >= 1);
        Assert.True(Painted(60) > Painted(6), "a point of sixty should be wider than one of six");
    }
}
