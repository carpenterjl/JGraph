using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Rendering;
using JGraph.Rendering.Skia;
using SkiaSharp;
using Xunit;

namespace JGraph.Tests.Rendering;

/// <summary>
/// M44 wave 1: the two batched primitives surfaces are drawn through, checked against real Skia
/// output rather than a recording double. The mesh path only exists on raster backends, so the
/// fallback for canvases that discard vertex meshes is exercised here too.
/// </summary>
public class SkiaMeshTests
{
    private const uint Red = 0xFFFF0000;
    private const uint Green = 0xFF00FF00;
    private const uint Blue = 0xFF0000FF;

    private static SKBitmap Render(int size, bool supportsMeshes, Action<SkiaRenderContext> draw)
    {
        var info = new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using SKSurface surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.White);
        using (var context = new SkiaRenderContext(
            surface.Canvas, new Size2D(size, size), supportsMeshes: supportsMeshes))
        {
            draw(context);
        }

        surface.Canvas.Flush();
        using SKImage image = surface.Snapshot();
        return SKBitmap.FromImage(image);
    }

    [Fact]
    public void DrawTriangles_InterpolatesBetweenTheVertexColors()
    {
        Point2D[] triangle = [new(2, 2), new(62, 2), new(2, 62)];
        uint[] colors = [Red, Green, Blue];

        using SKBitmap bitmap = Render(64, supportsMeshes: true, c => c.DrawTriangles(triangle, colors));

        // A point well inside the triangle takes a barycentric mix of all three corners, so no
        // channel is saturated and the three together account for the whole of one color.
        SKColor mid = bitmap.GetPixel(18, 18);
        Assert.InRange(mid.Red, 1, 254);
        Assert.InRange(mid.Green, 1, 254);
        Assert.InRange(mid.Blue, 1, 254);
        Assert.InRange(mid.Red + mid.Green + mid.Blue, 250, 260);
    }

    /// <summary>
    /// Skia's SVG and PDF backends drop <c>DrawVertices</c> without reporting anything, so an
    /// unguarded mesh would leave every exported surface blank while the screen looked correct.
    /// With mesh support switched off the same call has to put ink on the canvas.
    /// </summary>
    [Fact]
    public void DrawTriangles_WithoutMeshSupport_StillDrawsTheGeometry()
    {
        Point2D[] triangle = [new(2, 2), new(62, 2), new(2, 62)];
        uint[] flat = [Red, Red, Red];

        using SKBitmap bitmap = Render(64, supportsMeshes: false, c => c.DrawTriangles(triangle, flat));

        SKColor inside = bitmap.GetPixel(18, 18);
        Assert.Equal(255, inside.Red);
        Assert.Equal(0, inside.Green);
        Assert.Equal(0, inside.Blue);

        // Outside the triangle is untouched, so the fallback is not just filling the whole clip.
        SKColor outside = bitmap.GetPixel(60, 60);
        Assert.Equal(255, outside.Green);
    }

    [Fact]
    public void DrawTriangles_WithoutMeshSupport_AveragesTheVertexColors()
    {
        Point2D[] triangle = [new(2, 2), new(62, 2), new(2, 62)];
        uint[] colors = [Red, Green, Blue];

        using SKBitmap bitmap = Render(64, supportsMeshes: false, c => c.DrawTriangles(triangle, colors));

        SKColor inside = bitmap.GetPixel(18, 18);
        Assert.Equal(85, inside.Red);
        Assert.Equal(85, inside.Green);
        Assert.Equal(85, inside.Blue);
    }

    /// <summary>
    /// The point of batching sub-paths into one path: Skia scan-converts the whole thing at once,
    /// so a shared border gets full coverage from both sides instead of two half-covered
    /// antialiased edges that let the background show through as a seam.
    /// </summary>
    [Fact]
    public void DrawPaths_TilesAdjacentSubpaths_WithoutASeam()
    {
        Point2D[] vertices =
        [
            new(10, 10), new(32, 10), new(32, 50), new(10, 50),
            new(32, 10), new(54, 10), new(54, 50), new(32, 50),
        ];
        int[] starts = [0, 4];

        using SKBitmap bitmap = Render(
            64,
            supportsMeshes: true,
            c => c.DrawPaths(vertices, starts, closed: true, stroke: null, fill: Color.FromRgb(0, 0, 0)));

        for (int y = 12; y < 48; y++)
        {
            SKColor seam = bitmap.GetPixel(32, y);
            Assert.True(
                seam.Red == 0 && seam.Green == 0 && seam.Blue == 0,
                $"seam pixel at y={y} was {seam}, expected the solid fill");
        }
    }

    /// <summary>
    /// M44 wave 2, the whole point of batching the bands: a translucent <c>contourf</c> must be a
    /// flat wash of color. The old renderer drew a band one cell at a time and stroked each cell in
    /// its own fill color to paper over the antialiasing seams between them — which at opacity below
    /// one blended every cell border twice and made the seams <em>darker</em> rather than hiding
    /// them. The field here varies only along x, so the bands are vertical strips and any horizontal
    /// cell boundary that still leaves a mark shows up as a stripe down this column.
    /// </summary>
    [Fact]
    public void TranslucentFilledContour_HasNoInteriorSeams()
    {
        const int n = 21;
        var x = new double[n];
        var y = new double[n];
        var z = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            x[i] = i;
            y[i] = i;
        }

        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                z[r, c] = c; // depends on x only, so every band is a vertical strip
            }
        }

        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.Grid.ShowMajor = false; // grid lines cross the scan, and they are not what is under test
        ContourPlot contour = axes.AddContour(x, y, z, filled: true);
        contour.LevelCount = 4;
        contour.Opacity = 0.5;

        const int size = 400;
        var info = new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using SKSurface surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.White);
        using (var context = new SkiaRenderContext(surface.Canvas, new Size2D(size, size)))
        {
            new FigureRenderer().Render(figure, context);
        }

        surface.Canvas.Flush();
        using SKImage image = surface.Snapshot();
        using SKBitmap bitmap = SKBitmap.FromImage(image);

        // Well inside the plot area and inside one band: the middle column is data x = 10, which
        // sits in the band 8 <= z <= 12 rather than on one of its edges.
        SKColor first = bitmap.GetPixel(size / 2, 140);
        for (int py = 140; py < 240; py++)
        {
            SKColor pixel = bitmap.GetPixel(size / 2, py);
            Assert.True(
                pixel == first,
                $"pixel at y={py} was {pixel}, expected the uniform band color {first}");
        }
    }

    /// <summary>
    /// Bands abut each other as well as tiling internally, and each one is its own draw call, so
    /// the boundary between two bands is the one seam batching cannot remove on its own. Scanning
    /// across the strips catches a pixel that let the white background through.
    /// </summary>
    [Fact]
    public void OpaqueFilledContour_HasNoSeamsBetweenBands()
    {
        const int size = 400;
        using SKBitmap bitmap = RenderStrips(size, opacity: 1.0);

        // The field rises with x and the colormap rises in lightness with the value, so the scan is
        // a monotonically brightening staircase. Any white left showing between two bands is a
        // lightness spike, which a threshold on its own would miss: a quarter of the background
        // blended into a mid-band color is still nowhere near white.
        double previous = 0;
        for (int px = 120; px < 280; px++)
        {
            SKColor pixel = bitmap.GetPixel(px, 200);
            double lightness = (0.299 * pixel.Red) + (0.587 * pixel.Green) + (0.114 * pixel.Blue);
            Assert.True(
                lightness >= previous - 1,
                $"pixel at x={px} was {pixel} — lighter than the band to its left, so the background showed through");
            previous = System.Math.Max(previous, lightness);
        }
    }

    /// <summary>A filled contour of a field that varies only along x, so its bands are vertical strips.</summary>
    private static SKBitmap RenderStrips(int size, double opacity)
    {
        const int n = 21;
        var x = new double[n];
        var y = new double[n];
        var z = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            x[i] = i;
            y[i] = i;
        }

        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                z[r, c] = c;
            }
        }

        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.Grid.ShowMajor = false; // grid lines cross the scan, and they are not what is under test
        ContourPlot contour = axes.AddContour(x, y, z, filled: true);
        contour.LevelCount = 4;
        contour.Opacity = opacity;

        var info = new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using SKSurface surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.White);
        using (var context = new SkiaRenderContext(surface.Canvas, new Size2D(size, size)))
        {
            new FigureRenderer().Render(figure, context);
        }

        surface.Canvas.Flush();
        using SKImage image = surface.Snapshot();
        return SKBitmap.FromImage(image);
    }

    [Fact]
    public void DrawPaths_StrokesEverySubpath_InOneCall()
    {
        Point2D[] vertices = [new(8, 8), new(56, 8), new(8, 32), new(56, 32)];
        int[] starts = [0, 2];

        using SKBitmap bitmap = Render(
            64,
            supportsMeshes: true,
            c => c.DrawPaths(
                vertices, starts, closed: false, new LineStyle(Color.FromRgb(0, 0, 0), 2), fill: null));

        Assert.True(bitmap.GetPixel(32, 8).Red < 128, "the first sub-path should be stroked");
        Assert.True(bitmap.GetPixel(32, 32).Red < 128, "the second sub-path should be stroked");
    }
}
