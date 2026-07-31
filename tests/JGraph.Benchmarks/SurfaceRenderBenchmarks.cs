using BenchmarkDotNet.Attributes;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths.Transforms;
using JGraph.Objects;
using JGraph.Rendering;
using JGraph.Rendering.Skia;
using SkiaSharp;

namespace JGraph.Benchmarks;

/// <summary>
/// The cost of drawing one frame of a 3D surface and of a filled contour. Most of the benchmarks
/// run against a counting render context, so the numbers are pipeline cost (projection, ordering,
/// colormap sampling, draw-call issue) with rasterization removed; the <c>Raster</c> pair adds a
/// real Skia surface, which is what actually decides whether a rotate drag keeps up, because the
/// point of batching is to stop paying per-cell backend cost. A rotate drag repaints the whole
/// figure per frame, so this is the budget it has to fit inside.
/// </summary>
[MemoryDiagnoser]
public class SurfaceRenderBenchmarks
{
    private static readonly Rect2D PlotArea = new(60, 20, 1100, 700);

    private SurfacePlot _surface100 = null!;
    private SurfacePlot _surface250 = null!;
    private SurfacePlot _surface500 = null!;
    private SurfacePlot _surface500LitFlat = null!;
    private SurfacePlot _surface500LitGouraud = null!;
    private Projection3D _projection = null!;
    private RenderState _state = null!;
    private CountingRenderContext _context = null!;

    private ContourPlot _contour = null!;
    private RenderState _contourState = null!;

    private SKSurface _skiaSurface = null!;
    private SkiaRenderContext _skiaContext = null!;

    [GlobalCleanup]
    public void Cleanup()
    {
        _skiaContext.Dispose();
        _skiaSurface.Dispose();
    }

    [Benchmark]
    public void Surface250Raster()
    {
        _skiaSurface.Canvas.Clear(SKColors.White);
        _surface250.Render3D(_skiaContext, _projection, _state);
        _skiaSurface.Canvas.Flush();
    }

    [Benchmark]
    public void Surface500Raster()
    {
        _skiaSurface.Canvas.Clear(SKColors.White);
        _surface500.Render3D(_skiaContext, _projection, _state);
        _skiaSurface.Canvas.Flush();
    }

    [GlobalSetup]
    public void Setup()
    {
        _context = new CountingRenderContext(new Size2D(1220, 760));
        _skiaSurface = SKSurface.Create(new SKImageInfo(1220, 760, SKColorType.Bgra8888, SKAlphaType.Premul));
        _skiaContext = new SkiaRenderContext(_skiaSurface.Canvas, new Size2D(1220, 760));
        _surface100 = MakeSurface(100);
        _surface250 = MakeSurface(250);
        _surface500 = MakeSurface(500);
        _surface500LitFlat = MakeLitSurface(500, SurfaceLighting.Flat);
        _surface500LitGouraud = MakeLitSurface(500, SurfaceLighting.Gouraud);

        _projection = new Projection3D(
            new DataRange(0, 1), new DataRange(0, 1), new DataRange(-1, 1),
            azimuthDegrees: -37.5, elevationDegrees: 30, PlotArea);
        _state = new RenderState(new NormalizedCoordinateMapper(PlotArea), PlotArea, Colors.Black);

        (double[] x, double[] y, double[,] z) = Peaks(200);
        _contour = new ContourPlot(x, y, z) { Filled = true, LevelCount = 20 };
        _contourState = new RenderState(
            new AxisTransform(
                PlotArea,
                ScaleTransforms.For(AxisScaleType.Linear), new DataRange(0, 1), false,
                ScaleTransforms.For(AxisScaleType.Linear), new DataRange(0, 1), false),
            PlotArea,
            Colors.Black);
    }

    [Benchmark]
    public int Surface100() => Render(_surface100);

    [Benchmark]
    public int Surface250() => Render(_surface250);

    [Benchmark]
    public int Surface500() => Render(_surface500);

    /// <summary>
    /// The same frame with one light on the axes. Normals and shaded colors depend on the camera and
    /// on the axes ranges, so unlike the palette they cannot be cached across frames — this is the
    /// price of turning lighting on, measured against <see cref="Surface500"/> which is the price of
    /// leaving it off (and off is the default).
    /// </summary>
    [Benchmark]
    public int Surface500LitFlat() => Render(_surface500LitFlat);

    /// <summary>Per-vertex normals rather than per-facet: one normal per grid point, not per cell.</summary>
    [Benchmark]
    public int Surface500LitGouraud() => Render(_surface500LitGouraud);

    /// <summary>
    /// A repaint of an unchanged filled contour: a pan, a zoom, a resize, a theme switch. The band
    /// geometry lives in data space and is cached, so this is the mapping and the draw calls.
    /// </summary>
    [Benchmark]
    public int ContourFilled200x20Levels()
    {
        _context.Reset();
        _contour.Render(_context, _contourState);
        return _context.Calls;
    }

    /// <summary>
    /// The same frame with the geometry cache cold, which is what every first draw pays and what
    /// the whole render used to cost on every frame.
    /// </summary>
    [Benchmark]
    public int ContourFilled200x20LevelsCold()
    {
        _contour.SetData(_contour.X, _contour.Y, _contour.Z);
        _context.Reset();
        _contour.Render(_context, _contourState);
        return _context.Calls;
    }

    [Benchmark]
    public void ContourFilled200x20LevelsRaster()
    {
        _skiaSurface.Canvas.Clear(SKColors.White);
        _contour.Render(_skiaContext, _contourState);
        _skiaSurface.Canvas.Flush();
    }

    private int Render(SurfacePlot surface)
    {
        _context.Reset();
        surface.Render3D(_context, _projection, _state);
        return _context.Calls;
    }

    private static SurfacePlot MakeSurface(int n)
    {
        (double[] x, double[] y, double[,] z) = Peaks(n);
        return new SurfacePlot(x, y, z);
    }

    /// <summary>The same surface parented to an axes carrying one light, which is what turns lighting on.</summary>
    private static SurfacePlot MakeLitSurface(int n, SurfaceLighting lighting)
    {
        SurfacePlot surface = MakeSurface(n);
        surface.FaceLighting = lighting;

        var axes = new AxesModel { Is3D = true };
        axes.Plots.Add(surface);
        axes.Lights.Add(new LightModel { FollowsCamera = true, Position = new Vector3D(0.5, 0.5, 0.7) });
        return surface;
    }

    /// <summary>A peaks-like grid over the unit square: smooth, with both signs and a real range.</summary>
    private static (double[] X, double[] Y, double[,] Z) Peaks(int n)
    {
        var x = new double[n];
        var y = new double[n];
        for (int i = 0; i < n; i++)
        {
            x[i] = i / (double)(n - 1);
            y[i] = i / (double)(n - 1);
        }

        var z = new double[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                double u = (x[c] * 6) - 3;
                double v = (y[r] * 6) - 3;
                z[r, c] = (3 * Math.Pow(1 - u, 2) * Math.Exp(-(u * u) - Math.Pow(v + 1, 2)))
                    - (10 * ((u / 5) - Math.Pow(u, 3) - Math.Pow(v, 5)) * Math.Exp(-(u * u) - (v * v)))
                    - (Math.Exp(-Math.Pow(u + 1, 2) - (v * v)) / 3);
            }
        }

        return (x, y, z);
    }

    /// <summary>Counts draw calls and does nothing else, so the benchmark measures our pipeline only.</summary>
    private sealed class CountingRenderContext : IRenderContext
    {
        public CountingRenderContext(Size2D size) => Size = size;

        public Size2D Size { get; }

        public int Calls { get; private set; }

        public void Reset() => Calls = 0;

        public void Clear(Color color) => Calls++;

        public void PushClip(Rect2D rect) => Calls++;

        public void PopClip() => Calls++;

        public void DrawLine(Point2D a, Point2D b, LineStyle style) => Calls++;

        public void DrawPolyline(ReadOnlySpan<Point2D> points, LineStyle style) => Calls++;

        public void DrawRectangle(Rect2D rect, LineStyle? stroke, Color? fill) => Calls++;

        public void DrawPolygon(ReadOnlySpan<Point2D> points, LineStyle? stroke, Color? fill) => Calls++;

        public void DrawMarkers(ReadOnlySpan<Point2D> points, MarkerStyle style, Color seriesColor) => Calls++;

        public void DrawTriangles(ReadOnlySpan<Point2D> vertices, ReadOnlySpan<uint> colorsArgb) => Calls++;

        public void DrawPaths(
            ReadOnlySpan<Point2D> vertices,
            ReadOnlySpan<int> starts,
            bool closed,
            LineStyle? stroke,
            Color? fill) => Calls++;

        public void DrawImage(
            ReadOnlySpan<uint> pixelsArgb,
            int pixelWidth,
            int pixelHeight,
            Rect2D destination,
            bool interpolate = false) => Calls++;

        public void DrawText(
            string text,
            Point2D position,
            TextStyle style,
            HorizontalAlignment horizontal = HorizontalAlignment.Left,
            VerticalAlignment vertical = VerticalAlignment.Baseline,
            double rotationDegrees = 0) => Calls++;

        public Size2D MeasureText(string text, TextStyle style) => new(text.Length * style.FontSize * 0.5, style.FontSize * 1.2);
    }
}
