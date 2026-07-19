using System.ComponentModel;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Rendering;

namespace JGraph.Objects;

/// <summary>
/// A true-colour raster plot (MATLAB <c>imshow</c> of an RGB image): pre-computed 0xAARRGGBB pixels
/// drawn as a single tile spanning a data-space rectangle, through the same <see cref="IRenderContext.DrawImage"/>
/// seam as <see cref="ImagePlot"/> but without a colormap step. Row 0 is the top row (image convention).
/// </summary>
public sealed class RgbImagePlot : PlotObject, IDrawable
{
    private readonly uint[] _pixels;
    private DataRange _xExtent;
    private DataRange _yExtent;
    private bool _interpolate;

    private uint[]? _tile;
    private double _builtOpacity = 1;

    /// <summary>
    /// Creates a true-colour image plot from row-major 0xAARRGGBB pixels (straight alpha, row 0 at the
    /// top). The array is used directly, not copied; it must have at least <c>width * height</c> entries.
    /// </summary>
    public RgbImagePlot(uint[] pixelsArgb, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(pixelsArgb);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (pixelsArgb.Length < (long)width * height)
        {
            throw new ArgumentException(
                $"pixel buffer has {pixelsArgb.Length} entries, need {(long)width * height} for {width}x{height}.",
                nameof(pixelsArgb));
        }

        _pixels = pixelsArgb;
        Width = width;
        Height = height;
        _xExtent = new DataRange(0, width);
        _yExtent = new DataRange(0, height);
        Name = "Image";
    }

    /// <summary>The image width in pixels.</summary>
    [Browsable(false)]
    public int Width { get; }

    /// <summary>The image height in pixels.</summary>
    [Browsable(false)]
    public int Height { get; }

    /// <summary>The raw 0xAARRGGBB pixels (row-major, row 0 at top).</summary>
    [Browsable(false)]
    public uint[] Pixels => _pixels;

    /// <summary>The data-space X range the image spans (left to right).</summary>
    [Category("Appearance"), DisplayName("X extent")]
    public DataRange XExtent
    {
        get => _xExtent;
        set => SetProperty(ref _xExtent, value, InvalidationKind.Layout);
    }

    /// <summary>The data-space Y range the image spans (bottom to top).</summary>
    [Category("Appearance"), DisplayName("Y extent")]
    public DataRange YExtent
    {
        get => _yExtent;
        set => SetProperty(ref _yExtent, value, InvalidationKind.Layout);
    }

    /// <summary>When true, the tile is sampled bilinearly (smooth); when false, nearest-neighbor (crisp pixels).</summary>
    [Category("Appearance")]
    public bool Interpolate
    {
        get => _interpolate;
        set => SetProperty(ref _interpolate, value, InvalidationKind.Render);
    }

    /// <inheritdoc />
    public override DataRange GetXDataBounds() => _xExtent;

    /// <inheritdoc />
    public override DataRange GetYDataBounds() => _yExtent;

    /// <inheritdoc />
    public void Render(IRenderContext context, RenderState state)
    {
        if (Width == 0 || Height == 0)
        {
            return;
        }

        uint[] tile = ResolveTile();

        ICoordinateMapper mapper = state.Mapper;
        Rect2D dest = Rect2D.FromCorners(
            mapper.DataToPixel(_xExtent.Min, _yExtent.Max),
            mapper.DataToPixel(_xExtent.Max, _yExtent.Min));

        context.DrawImage(tile, Width, Height, dest, _interpolate);
    }

    /// <summary>Returns the pixels to draw: the source array at full opacity, else an alpha-scaled cache.</summary>
    private uint[] ResolveTile()
    {
        double opacity = Opacity;
        if (opacity >= 1.0)
        {
            return _pixels;
        }

        if (_tile is not null && _builtOpacity == opacity)
        {
            return _tile;
        }

        var tile = new uint[_pixels.Length];
        for (int i = 0; i < _pixels.Length; i++)
        {
            uint argb = _pixels[i];
            uint alpha = (uint)Math.Round(((argb >> 24) & 0xFF) * opacity);
            tile[i] = (alpha << 24) | (argb & 0x00FFFFFF);
        }

        _tile = tile;
        _builtOpacity = opacity;
        return tile;
    }
}
