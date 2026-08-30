using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Export;
using JGraph.Objects;
using SkiaSharp;
using Xunit;

namespace JGraph.Tests.Export;

/// <summary>
/// A page with nothing on it. MATLAB spells it <c>exportgraphics(fig, file, 'BackgroundColor',
/// 'none')</c>, and it is the only way to get a picture that will sit on something else — which is
/// what an animation cut out of its frame is for.
/// </summary>
public class TransparentBackgroundTests
{
    private static readonly Size2D Size = new(200, 160);

    private static FigureModel Figure()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AddLine([0, 1, 2, 3], [0, 1, 0, 1]);
        return figure;
    }

    private static SKBitmap Decode(FigureModel figure)
    {
        byte[] bytes = FigureExporter.ExportBytes(figure, ExportFormat.Png, new ExportOptions { Size = Size });
        return SKBitmap.Decode(bytes);
    }

    [Fact]
    public void ATransparentBackgroundReachesThePngAndTheCornersAreEmpty()
    {
        FigureModel figure = Figure();
        figure.Background = Colors.Transparent;

        using SKBitmap image = Decode(figure);
        Assert.Equal(0, image.GetPixel(0, 0).Alpha);
        Assert.Equal(0, image.GetPixel(image.Width - 1, image.Height - 1).Alpha);
    }

    /// <summary>
    /// The trap this had to clear: <c>InvertHardcopy</c> exists to keep a dark figure from being
    /// printed black, and it would have answered a transparent page by painting it white — writing the
    /// one thing the caller said not to.
    /// </summary>
    [Fact]
    public void InvertHardcopyDoesNotPaintATransparentPageWhite()
    {
        FigureModel figure = Figure();
        figure.InvertHardcopy = true;
        figure.Background = Colors.Transparent;

        using SKBitmap image = Decode(figure);
        Assert.Equal(0, image.GetPixel(0, 0).Alpha);

        // And the colour the figure went in wearing is the colour it comes out wearing.
        Assert.Equal(Colors.Transparent, figure.Background);
    }

    /// <summary>A dark page is still rescued, which is the behaviour the guard had to leave alone.</summary>
    [Fact]
    public void InvertHardcopyStillWhitensAnOpaqueDarkPage()
    {
        FigureModel figure = Figure();
        figure.InvertHardcopy = true;
        figure.Background = Color.FromRgb(8, 10, 20);

        using SKBitmap image = Decode(figure);
        SKColor corner = image.GetPixel(0, 0);
        Assert.Equal(255, corner.Alpha);
        Assert.Equal(255, corner.Red);
        Assert.Equal(255, corner.Blue);
    }

    /// <summary>
    /// An axes told <c>axis off</c> draws no background of its own, so the transparency reaches the
    /// middle of the picture and not only its margins — which is the whole of a cut-out.
    /// </summary>
    [Fact]
    public void AnInvisibleAxesLetsTheTransparencyThroughThePlotBoxToo()
    {
        FigureModel figure = Figure();
        figure.Background = Colors.Transparent;
        figure.Axes[0].Visible = false;

        using SKBitmap image = Decode(figure);
        int clear = 0;
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                if (image.GetPixel(x, y).Alpha == 0)
                {
                    clear++;
                }
            }
        }

        Assert.True(clear > image.Width * image.Height * 0.9,
            $"only {clear} of {image.Width * image.Height} pixels were left empty");
    }
}
