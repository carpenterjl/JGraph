using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Export.Writers;
using JGraph.Rendering;
using JGraph.Rendering.Skia;
using SkiaSharp;

namespace JGraph.Export;

/// <summary>
/// Exports figures to raster (PNG/JPEG/BMP/TIFF) and vector (SVG/PDF) formats. Every format runs the
/// same backend-independent <see cref="FigureRenderer"/>; only the Skia canvas underneath differs — a
/// bitmap for raster output, <see cref="SKSvgCanvas"/> for SVG, and <see cref="SKDocument"/> for PDF —
/// so exports are pixel-for-pixel (and stroke-for-stroke) consistent with the screen. Vector exports
/// contain real paths and text, not embedded images.
/// </summary>
public static class FigureExporter
{
    /// <summary>PDF points (1/72 inch) per device-independent unit (1/96 inch).</summary>
    private const float PointsPerDiu = 72f / 96f;

    /// <summary>Exports a figure to a file, inferring the format from the file extension.</summary>
    public static void Export(FigureModel figure, string path, ExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(figure);
        ArgumentNullException.ThrowIfNull(path);

        ExportFormat format = ExportFormats.FromPath(path);
        using FileStream stream = File.Create(path);
        Export(figure, stream, format, options);
    }

    /// <summary>Exports a figure to a stream in the given format.</summary>
    public static void Export(FigureModel figure, Stream stream, ExportFormat format, ExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(figure);
        ArgumentNullException.ThrowIfNull(stream);
        options ??= new ExportOptions();

        Size2D size = options.Size ?? figure.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            throw new ArgumentException("The export size must be positive.", nameof(options));
        }

        ITheme theme = options.Theme ?? Theme.Light;

        switch (format)
        {
            case ExportFormat.Png:
                EncodeBitmap(figure, size, options, theme, SKColorType.Rgba8888, SKEncodedImageFormat.Png, 100, stream);
                break;

            case ExportFormat.Jpeg:
                int quality = System.Math.Clamp(options.JpegQuality, 1, 100);
                EncodeBitmap(figure, size, options, theme, SKColorType.Rgba8888, SKEncodedImageFormat.Jpeg, quality, stream);
                break;

            case ExportFormat.Bmp:
            {
                using SKBitmap bitmap = RenderBitmap(figure, size, options.Scale, theme, SKColorType.Bgra8888);
                BmpWriter.Write(stream, bitmap);
                break;
            }

            case ExportFormat.Tiff:
            {
                using SKBitmap bitmap = RenderBitmap(figure, size, options.Scale, theme, SKColorType.Rgba8888);
                TiffWriter.Write(stream, bitmap);
                break;
            }

            case ExportFormat.Svg:
                ExportSvg(figure, size, theme, stream);
                break;

            case ExportFormat.Pdf:
                ExportPdf(figure, size, theme, stream);
                break;

            default:
                throw new NotSupportedException($"Export format '{format}' is not supported.");
        }
    }

    /// <summary>Exports a figure to a byte array (convenience for clipboard and tests).</summary>
    public static byte[] ExportBytes(FigureModel figure, ExportFormat format, ExportOptions? options = null)
    {
        using var stream = new MemoryStream();
        Export(figure, stream, format, options);
        return stream.ToArray();
    }

    private static void EncodeBitmap(
        FigureModel figure,
        Size2D size,
        ExportOptions options,
        ITheme theme,
        SKColorType colorType,
        SKEncodedImageFormat encoding,
        int quality,
        Stream stream)
    {
        using SKBitmap bitmap = RenderBitmap(figure, size, options.Scale, theme, colorType);
        using var image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(encoding, quality)
            ?? throw new InvalidOperationException($"Skia could not encode the figure as {encoding}.");
        data.SaveTo(stream);
    }

    /// <summary>Renders the figure into a new bitmap at <paramref name="scale"/> pixels per unit.</summary>
    private static SKBitmap RenderBitmap(FigureModel figure, Size2D size, double scale, ITheme theme, SKColorType colorType)
    {
        if (scale <= 0 || !double.IsFinite(scale))
        {
            throw new ArgumentException("The raster scale must be a positive finite number.", nameof(scale));
        }

        int pixelWidth = System.Math.Max(1, (int)System.Math.Round(size.Width * scale));
        int pixelHeight = System.Math.Max(1, (int)System.Math.Round(size.Height * scale));

        var info = new SKImageInfo(pixelWidth, pixelHeight, colorType, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        try
        {
            using var canvas = new SKCanvas(bitmap);
            canvas.Scale((float)scale);
            RenderTo(canvas, figure, size, theme, scale);
            canvas.Flush();
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static void ExportSvg(FigureModel figure, Size2D size, ITheme theme, Stream stream)
    {
        var bounds = SKRect.Create((float)size.Width, (float)size.Height);

        // Disposing the canvas finalizes the SVG document, so it must happen before the caller
        // reads or closes the stream. Skia's SVG backend drops dash path effects, so dashed
        // strokes are flattened into explicit segments for this format.
        using SKCanvas canvas = SKSvgCanvas.Create(bounds, stream);
        using var context = new SkiaRenderContext(canvas, size, 1.0, flattenDashes: true);
        new FigureRenderer().Render(figure, context, theme);
    }

    private static void ExportPdf(FigureModel figure, Size2D size, ITheme theme, Stream stream)
    {
        var metadata = new SKDocumentPdfMetadata
        {
            Title = string.IsNullOrEmpty(figure.Title) ? figure.Name : figure.Title,
            Creator = "JGraph",
        };

        using SKDocument document = SKDocument.CreatePdf(stream, metadata)
            ?? throw new InvalidOperationException("Skia could not create a PDF document.");

        // PDF pages are measured in points (1/72"); our layout is in DIUs (1/96"). Scaling the page
        // and canvas together preserves the figure's physical print size exactly.
        SKCanvas canvas = document.BeginPage(
            (float)(size.Width * PointsPerDiu),
            (float)(size.Height * PointsPerDiu));
        canvas.Scale(PointsPerDiu);
        RenderTo(canvas, figure, size, theme);
        document.EndPage();
        document.Close();
    }

    private static void RenderTo(SKCanvas canvas, FigureModel figure, Size2D size, ITheme theme, double pixelRatio = 1.0)
    {
        using var context = new SkiaRenderContext(canvas, size, pixelRatio);
        new FigureRenderer().Render(figure, context, theme);
    }
}
