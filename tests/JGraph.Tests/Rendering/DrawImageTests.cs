using JGraph.Core.Primitives;
using JGraph.Rendering.Skia;
using SkiaSharp;
using Xunit;

namespace JGraph.Tests.Rendering;

public class DrawImageTests
{
    [Fact]
    public void DrawImage_PreservesColorsAndOrientation()
    {
        // Row-major, top row first: red, green / blue, white.
        const uint red = 0xFFFF0000;
        const uint green = 0xFF00FF00;
        const uint blue = 0xFF0000FF;
        const uint white = 0xFFFFFFFF;
        uint[] pixels = { red, green, blue, white };

        var info = new SKImageInfo(2, 2, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.Transparent);
        using (var ctx = new SkiaRenderContext(surface.Canvas, new Size2D(2, 2)))
        {
            ctx.DrawImage(pixels, 2, 2, new Rect2D(0, 0, 2, 2), interpolate: false);
        }

        surface.Canvas.Flush();
        using SKImage image = surface.Snapshot();
        using SKBitmap bitmap = SKBitmap.FromImage(image);

        AssertColor(bitmap.GetPixel(0, 0), 255, 0, 0);   // top-left red
        AssertColor(bitmap.GetPixel(1, 0), 0, 255, 0);   // top-right green
        AssertColor(bitmap.GetPixel(0, 1), 0, 0, 255);   // bottom-left blue
        AssertColor(bitmap.GetPixel(1, 1), 255, 255, 255); // bottom-right white
    }

    [Fact]
    public void DrawImage_IgnoresEmptyOrUndersizedBuffers()
    {
        var info = new SKImageInfo(4, 4);
        using var surface = SKSurface.Create(info);
        using var ctx = new SkiaRenderContext(surface.Canvas, new Size2D(4, 4));

        // Buffer too small for the claimed dimensions: must be a no-op, not a crash.
        ctx.DrawImage(new uint[] { 0xFF000000 }, 4, 4, new Rect2D(0, 0, 4, 4));
        ctx.DrawImage(System.Array.Empty<uint>(), 0, 0, new Rect2D(0, 0, 4, 4));
    }

    private static void AssertColor(SKColor actual, byte r, byte g, byte b)
    {
        Assert.Equal(r, actual.Red);
        Assert.Equal(g, actual.Green);
        Assert.Equal(b, actual.Blue);
    }
}
