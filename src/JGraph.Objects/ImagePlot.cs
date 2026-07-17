using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Rendering;

namespace JGraph.Objects;

/// <summary>
/// An image / heatmap plot (MATLAB <c>imagesc</c>): a 2D scalar field colored through a
/// <see cref="Colormap"/> and drawn as a single raster tile spanning a data-space rectangle. The tile
/// is built once and cached; it is redrawn scaled by the renderer, so pan/zoom stays cheap. Non-finite
/// samples are drawn transparent.
/// </summary>
public sealed class ImagePlot : PlotObject, IDrawable
{
    private double[,] _values;
    private Colormap _colormap = Colormap.Viridis;
    private DataRange _xExtent;
    private DataRange _yExtent;
    private bool _autoScaleColor = true;
    private double _colorMin;
    private double _colorMax = 1;
    private bool _interpolate;
    private bool _rowZeroAtTop = true;

    private uint[]? _pixels;
    private double _builtOpacity = 1;

    /// <summary>Creates an image plot over a [rows, cols] scalar field. The array is used directly.</summary>
    public ImagePlot(double[,] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = values;
        _xExtent = new DataRange(0, values.GetLength(1));
        _yExtent = new DataRange(0, values.GetLength(0));
        Name = "Image";
    }

    /// <summary>The number of rows in the scalar field.</summary>
    [Browsable(false)]
    public int Rows => _values.GetLength(0);

    /// <summary>The number of columns in the scalar field.</summary>
    [Browsable(false)]
    public int Columns => _values.GetLength(1);

    /// <summary>The scalar field. Replacing it rebuilds the color tile.</summary>
    [Browsable(false)]
    public double[,] Values
    {
        get => _values;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _values = value;
            _pixels = null;
            Invalidate(InvalidationKind.Data);
        }
    }

    /// <summary>The colormap used to color values across the color range.</summary>
    [Browsable(false)]
    public Colormap Colormap
    {
        get => _colormap;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!ReferenceEquals(_colormap, value))
            {
                _colormap = value;
                _pixels = null;
                Invalidate(InvalidationKind.Render);
            }
        }
    }

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

    /// <summary>When true, the color range is taken from the data extent; otherwise from <see cref="ColorMin"/>/<see cref="ColorMax"/>.</summary>
    [Category("Appearance"), DisplayName("Auto color scale")]
    public bool AutoScaleColor
    {
        get => _autoScaleColor;
        set
        {
            if (SetProperty(ref _autoScaleColor, value, InvalidationKind.Render))
            {
                _pixels = null;
            }
        }
    }

    /// <summary>The value mapped to the low end of the colormap (used when <see cref="AutoScaleColor"/> is false).</summary>
    [Category("Appearance"), DisplayName("Color min")]
    public double ColorMin
    {
        get => _colorMin;
        set
        {
            if (SetProperty(ref _colorMin, value, InvalidationKind.Render))
            {
                _pixels = null;
            }
        }
    }

    /// <summary>The value mapped to the high end of the colormap (used when <see cref="AutoScaleColor"/> is false).</summary>
    [Category("Appearance"), DisplayName("Color max")]
    public double ColorMax
    {
        get => _colorMax;
        set
        {
            if (SetProperty(ref _colorMax, value, InvalidationKind.Render))
            {
                _pixels = null;
            }
        }
    }

    /// <summary>When true, the tile is sampled bilinearly (smooth); when false, nearest-neighbor (crisp cells).</summary>
    [Category("Appearance")]
    public bool Interpolate
    {
        get => _interpolate;
        set => SetProperty(ref _interpolate, value, InvalidationKind.Render);
    }

    /// <summary>When true (default), row 0 of the field is drawn at the top (image convention); otherwise at the bottom.</summary>
    [Category("Appearance"), DisplayName("Row zero at top")]
    public bool RowZeroAtTop
    {
        get => _rowZeroAtTop;
        set
        {
            if (SetProperty(ref _rowZeroAtTop, value, InvalidationKind.Render))
            {
                _pixels = null;
            }
        }
    }

    /// <inheritdoc />
    public override DataRange GetXDataBounds() => _xExtent;

    /// <inheritdoc />
    public override DataRange GetYDataBounds() => _yExtent;

    /// <inheritdoc />
    public void Render(IRenderContext context, RenderState state)
    {
        int rows = Rows;
        int cols = Columns;
        if (rows == 0 || cols == 0)
        {
            return;
        }

        if (_pixels is null || _builtOpacity != Opacity)
        {
            BuildTile();
        }

        ICoordinateMapper mapper = state.Mapper;
        Rect2D dest = Rect2D.FromCorners(
            mapper.DataToPixel(_xExtent.Min, _yExtent.Max),
            mapper.DataToPixel(_xExtent.Max, _yExtent.Min));

        context.DrawImage(_pixels!, cols, rows, dest, _interpolate);
    }

    private void BuildTile()
    {
        int rows = Rows;
        int cols = Columns;
        var pixels = new uint[rows * cols];

        (double min, double max) = ResolveColorRange();
        double opacity = Opacity;

        for (int r = 0; r < rows; r++)
        {
            int srcRow = _rowZeroAtTop ? r : rows - 1 - r;
            int rowOffset = r * cols;
            for (int c = 0; c < cols; c++)
            {
                double v = _values[srcRow, c];
                if (!double.IsFinite(v))
                {
                    pixels[rowOffset + c] = 0; // transparent
                    continue;
                }

                Color color = _colormap.Sample(v, min, max);
                pixels[rowOffset + c] = color.WithOpacity(opacity).ToArgb();
            }
        }

        _pixels = pixels;
        _builtOpacity = opacity;
    }

    private (double Min, double Max) ResolveColorRange()
    {
        if (!_autoScaleColor)
        {
            return _colorMin < _colorMax ? (_colorMin, _colorMax) : (_colorMin, _colorMin + 1);
        }

        DataRange bounds = DataRange.Empty;
        foreach (double v in _values)
        {
            if (double.IsFinite(v))
            {
                bounds = bounds.Include(v);
            }
        }

        if (!bounds.IsValid)
        {
            bounds = bounds.EnsureValid();
        }

        return (bounds.Min, bounds.Max);
    }
}
