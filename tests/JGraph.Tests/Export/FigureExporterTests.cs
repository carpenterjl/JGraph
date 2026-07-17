using System.Xml.Linq;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Export;
using JGraph.Objects;
using SkiaSharp;
using Xunit;

namespace JGraph.Tests.Export;

public class FigureExporterTests
{
    private static readonly Size2D Size = new(320, 240);

    private static FigureModel CreateFigure()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AddLine(new double[] { 0, 1, 2, 3 }, new double[] { 0, 1, 0, 1 }).DisplayName = "signal";
        axes.Title = "Export Test";
        axes.Grid.ShowMajor = true;
        return figure;
    }

    private static ExportOptions Options(double scale = 1.0, ITheme? theme = null) =>
        new() { Size = Size, Scale = scale, Theme = theme };

    // --- Format inference ---------------------------------------------------------------------

    [Theory]
    [InlineData("plot.png", ExportFormat.Png)]
    [InlineData("plot.JPG", ExportFormat.Jpeg)]
    [InlineData("plot.jpeg", ExportFormat.Jpeg)]
    [InlineData("plot.bmp", ExportFormat.Bmp)]
    [InlineData("plot.tif", ExportFormat.Tiff)]
    [InlineData("plot.tiff", ExportFormat.Tiff)]
    [InlineData("plot.svg", ExportFormat.Svg)]
    [InlineData(@"c:\out\plot.pdf", ExportFormat.Pdf)]
    public void FromPath_InfersFormatFromExtension(string path, ExportFormat expected) =>
        Assert.Equal(expected, ExportFormats.FromPath(path));

    [Fact]
    public void FromPath_RejectsUnknownOrMissingExtensions()
    {
        Assert.Throws<NotSupportedException>(() => ExportFormats.FromPath("plot.docx"));
        Assert.Throws<NotSupportedException>(() => ExportFormats.FromPath("plot"));
    }

    // --- Raster formats -----------------------------------------------------------------------

    [Fact]
    public void Png_DecodesWithExactPixelSizeAndBackground()
    {
        byte[] bytes = FigureExporter.ExportBytes(CreateFigure(), ExportFormat.Png, Options());

        using SKBitmap decoded = SKBitmap.Decode(bytes);
        Assert.NotNull(decoded);
        Assert.Equal(320, decoded.Width);
        Assert.Equal(240, decoded.Height);

        SKColor corner = decoded.GetPixel(2, 2); // figure background, outside the axes frame
        Assert.Equal(255, corner.Red);
        Assert.Equal(255, corner.Green);
        Assert.Equal(255, corner.Blue);
    }

    [Fact]
    public void Png_ScaleMultipliesPixelSizeOnly()
    {
        byte[] bytes = FigureExporter.ExportBytes(CreateFigure(), ExportFormat.Png, Options(scale: 2.0));

        using SKBitmap decoded = SKBitmap.Decode(bytes);
        Assert.Equal(640, decoded.Width);
        Assert.Equal(480, decoded.Height);
    }

    [Fact]
    public void Png_DarkTheme_PaintsDarkBackground()
    {
        // Like on screen, a theme is applied to the model and then passed for chrome defaults.
        FigureModel figure = CreateFigure();
        Theme.Dark.Apply(figure);

        byte[] bytes = FigureExporter.ExportBytes(figure, ExportFormat.Png, Options(theme: Theme.Dark));

        using SKBitmap decoded = SKBitmap.Decode(bytes);
        SKColor corner = decoded.GetPixel(2, 2);
        Assert.Equal(0x1E, corner.Red);
        Assert.Equal(0x1E, corner.Green);
        Assert.Equal(0x1E, corner.Blue);
    }

    [Fact]
    public void Jpeg_DecodesWithExactPixelSize()
    {
        byte[] bytes = FigureExporter.ExportBytes(CreateFigure(), ExportFormat.Jpeg, Options());

        using SKBitmap decoded = SKBitmap.Decode(bytes);
        Assert.NotNull(decoded);
        Assert.Equal(320, decoded.Width);
        Assert.Equal(240, decoded.Height);
    }

    [Fact]
    public void Bmp_DecodesWithExactPixelSizeAndBackground()
    {
        byte[] bytes = FigureExporter.ExportBytes(CreateFigure(), ExportFormat.Bmp, Options());

        Assert.Equal((byte)'B', bytes[0]);
        Assert.Equal((byte)'M', bytes[1]);

        using SKBitmap decoded = SKBitmap.Decode(bytes);
        Assert.NotNull(decoded);
        Assert.Equal(320, decoded.Width);
        Assert.Equal(240, decoded.Height);

        SKColor corner = decoded.GetPixel(2, 2);
        Assert.Equal(255, corner.Red);
        Assert.Equal(255, corner.Green);
        Assert.Equal(255, corner.Blue);
    }

    [Fact]
    public void Tiff_HasValidBaselineStructure()
    {
        byte[] bytes = FigureExporter.ExportBytes(CreateFigure(), ExportFormat.Tiff, Options());

        Dictionary<ushort, uint> tags = ParseTiffTags(bytes);
        Assert.Equal(320u, tags[256]);              // ImageWidth
        Assert.Equal(240u, tags[257]);              // ImageLength
        Assert.Equal(1u, tags[259]);                // Compression = none
        Assert.Equal(2u, tags[262]);                // Photometric = RGB
        Assert.Equal(3u, tags[277]);                // SamplesPerPixel
        Assert.Equal(320u * 240 * 3, tags[279]);    // StripByteCounts
        Assert.Equal((uint)bytes.Length, tags[273] + tags[279]); // strip runs to end of file
    }

    // --- Vector formats -----------------------------------------------------------------------

    [Fact]
    public void Svg_IsWellFormedVectorMarkupAtTheRightSize()
    {
        byte[] bytes = FigureExporter.ExportBytes(CreateFigure(), ExportFormat.Svg, Options());
        string svg = System.Text.Encoding.UTF8.GetString(bytes);

        XDocument document = XDocument.Parse(svg); // throws when malformed
        XElement root = document.Root!;
        Assert.Equal("svg", root.Name.LocalName);
        Assert.Contains("320", (string?)root.Attribute("width"));
        Assert.Contains("240", (string?)root.Attribute("height"));

        Assert.Contains("<path", svg);      // real vector geometry…
        Assert.DoesNotContain("<image", svg); // …not an embedded raster
    }

    [Fact]
    public void Svg_FlattensDashedStrokesIntoSegments()
    {
        // Skia's SVG backend drops dash path effects, so the exporter chops dashed strokes into
        // explicit segments. A dashed rectangle must therefore produce many path move commands.
        var figure = new FigureModel();
        figure.Annotations.Add(new JGraph.Objects.Annotations.RectangleAnnotation(0.1, 0.1, 0.9, 0.9)
        {
            Space = AnnotationSpace.Figure,
            DashStyle = DashStyle.Dash,
        });

        byte[] bytes = FigureExporter.ExportBytes(figure, ExportFormat.Svg, Options());
        string svg = System.Text.Encoding.UTF8.GetString(bytes);

        int moveCommands = svg.Count(c => c == 'M');
        Assert.True(moveCommands >= 20, $"Expected many dash segments; found {moveCommands} move commands.");
    }

    [Fact]
    public void Pdf_HasPdfHeaderAndTrailer()
    {
        byte[] bytes = FigureExporter.ExportBytes(CreateFigure(), ExportFormat.Pdf, Options());

        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
        string tail = System.Text.Encoding.ASCII.GetString(bytes, bytes.Length - 32, 32);
        Assert.Contains("%%EOF", tail);
    }

    // --- General behavior ---------------------------------------------------------------------

    [Fact]
    public void Export_ToFile_InfersFormatAndWrites()
    {
        string path = Path.Combine(Path.GetTempPath(), $"jgraph-test-{Guid.NewGuid():N}.png");
        try
        {
            FigureExporter.Export(CreateFigure(), path, Options());

            byte[] bytes = File.ReadAllBytes(path);
            using SKBitmap decoded = SKBitmap.Decode(bytes);
            Assert.Equal(320, decoded.Width);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Export_UsesFigureSizeWhenNoSizeGiven()
    {
        FigureModel figure = CreateFigure();
        figure.Size = new Size2D(200, 150);

        byte[] bytes = FigureExporter.ExportBytes(figure, ExportFormat.Png);

        using SKBitmap decoded = SKBitmap.Decode(bytes);
        Assert.Equal(200, decoded.Width);
        Assert.Equal(150, decoded.Height);
    }

    [Fact]
    public void Export_RejectsInvalidScaleAndSize()
    {
        FigureModel figure = CreateFigure();
        Assert.ThrowsAny<ArgumentException>(() =>
            FigureExporter.ExportBytes(figure, ExportFormat.Png, new ExportOptions { Size = Size, Scale = 0 }));
        Assert.ThrowsAny<ArgumentException>(() =>
            FigureExporter.ExportBytes(figure, ExportFormat.Png, new ExportOptions { Size = new Size2D(0, 100) }));
    }

    [Fact]
    public void Export_DoesNotDisturbTheFigureModel()
    {
        FigureModel figure = CreateFigure();
        figure.RecomputeDataBounds();
        DataRange xBefore = figure.Axes[0].PrimaryXAxis.Range;

        FigureExporter.ExportBytes(figure, ExportFormat.Png, Options());
        FigureExporter.ExportBytes(figure, ExportFormat.Pdf, Options());

        Assert.Equal(xBefore, figure.Axes[0].PrimaryXAxis.Range);
        Assert.True(figure.Axes[0].PrimaryXAxis.AutoScale);
    }

    /// <summary>Minimal little-endian baseline-TIFF reader: returns tag → value for the first IFD.</summary>
    private static Dictionary<ushort, uint> ParseTiffTags(byte[] bytes)
    {
        Assert.Equal((byte)'I', bytes[0]);
        Assert.Equal((byte)'I', bytes[1]);
        Assert.Equal(42, BitConverter.ToUInt16(bytes, 2));

        uint ifdOffset = BitConverter.ToUInt32(bytes, 4);
        ushort count = BitConverter.ToUInt16(bytes, (int)ifdOffset);

        var tags = new Dictionary<ushort, uint>();
        for (int i = 0; i < count; i++)
        {
            int entry = (int)ifdOffset + 2 + (i * 12);
            ushort tag = BitConverter.ToUInt16(bytes, entry);
            ushort type = BitConverter.ToUInt16(bytes, entry + 2);
            uint value = type == 3 // SHORT values sit inline in the low two bytes
                ? BitConverter.ToUInt16(bytes, entry + 8)
                : BitConverter.ToUInt32(bytes, entry + 8);
            tags[tag] = value;
        }

        return tags;
    }
}
