using System.ComponentModel;
using System.Globalization;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Rendering;

namespace JGraph.Objects;

/// <summary>
/// How a heatmap decides where a value sits in its colormap.
/// </summary>
public enum HeatmapScaling
{
    /// <summary>One range for the whole chart, which is what makes cells comparable across it.</summary>
    Scaled,

    /// <summary>Each column against its own range, so every column uses the full colormap.</summary>
    ScaledColumns,

    /// <summary>Each row against its own range.</summary>
    ScaledRows,

    /// <summary>One range for the whole chart, spaced logarithmically. Values at or below zero go missing.</summary>
    Log,
}

/// <summary>
/// A heatmap (MATLAB <c>heatmap</c>): a grid of labelled cells coloured by their value, with the
/// value written in each cell.
/// <para>
/// Cell <c>(r, c)</c> is a unit square centred on the data point <c>(c, r)</c>, so the rulers can be
/// plain category axes with their labels at the integers — the same machinery a category line plot
/// uses. Row zero is drawn at the top by inverting the Y ruler rather than by flipping the data,
/// which is what keeps <c>ColorData</c> answering in the order it was given.
/// </para>
/// <para>
/// The recorded divergence: MATLAB's heatmap is a chart container that owns its own title, labels
/// and colorbar and cannot share an axes with anything. Here it is an ordinary plot on ordinary
/// axes, so those four properties forward to the axes it is drawn on and a heatmap can be put in a
/// subplot beside anything else. <c>XDisplayData</c>/<c>YDisplayData</c> — MATLAB's way of
/// reordering or dropping categories after the fact — have no counterpart; reorder the data.
/// </para>
/// </summary>
public sealed class HeatmapPlot : PlotObject, IDrawable, IColorMapped
{
    private double[,] _colorData;
    private string[]? _xData;
    private string[]? _yData;
    private Colormap _colormap = Colormap.Parula;
    private DataRange? _colorLimits;
    private HeatmapScaling _colorScaling = HeatmapScaling.Scaled;
    private bool _showCellLabels = true;
    private Color? _cellLabelColor;
    private string? _cellLabelFormat;
    private TextStyle _cellLabelStyle = new(Colors.Black, 10);
    private bool _gridVisible = true;
    private Color _gridColor = Colors.White;
    private Color _missingDataColor = Color.FromScRgb(0.15, 0.15, 0.15);
    private string _missingDataLabel = "NaN";

    private uint[]? _pixels;
    private double _builtOpacity = 1;

    /// <summary>Creates a heatmap over a [rows, cols] grid of values. The array is used directly.</summary>
    public HeatmapPlot(double[,] colorData)
    {
        _colorData = colorData ?? throw new ArgumentNullException(nameof(colorData));
        Name = "Heatmap";
    }

    /// <summary>The number of rows of cells.</summary>
    [Browsable(false)]
    public int Rows => _colorData.GetLength(0);

    /// <summary>The number of columns of cells.</summary>
    [Browsable(false)]
    public int Columns => _colorData.GetLength(1);

    /// <summary>The value in each cell, in the order it was given.</summary>
    [Browsable(false)]
    public double[,] ColorData
    {
        get => _colorData;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _colorData = value;
            Rebuild();
            Invalidate(InvalidationKind.Data);
        }
    }

    /// <summary>What the columns are called, or null to number them from one.</summary>
    [Browsable(false)]
    public string[]? XData
    {
        get => _xData;
        set => SetProperty(ref _xData, value, InvalidationKind.Layout);
    }

    /// <summary>What the rows are called, or null to number them from one.</summary>
    [Browsable(false)]
    public string[]? YData
    {
        get => _yData;
        set => SetProperty(ref _yData, value, InvalidationKind.Layout);
    }

    /// <summary>The colormap the cells are coloured through.</summary>
    [Browsable(false)]
    public Colormap Colormap
    {
        get => _colormap;
        set
        {
            if (SetProperty(ref _colormap, value ?? Colormap.Parula, InvalidationKind.Render))
            {
                Rebuild();
            }
        }
    }

    /// <summary>
    /// The values at the two ends of the colormap, or null to take them from the data. Column and
    /// row scaling work from each slice's own extent, so this is ignored under either of them.
    /// </summary>
    [Browsable(false)]
    public DataRange? ColorLimits
    {
        get => _colorLimits;
        set
        {
            if (SetProperty(ref _colorLimits, value, InvalidationKind.Render))
            {
                Rebuild();
            }
        }
    }

    /// <summary>How a value is turned into a position in the colormap.</summary>
    [Category("Appearance"), DisplayName("Color scaling")]
    public HeatmapScaling ColorScaling
    {
        get => _colorScaling;
        set
        {
            if (SetProperty(ref _colorScaling, value, InvalidationKind.Render))
            {
                Rebuild();
            }
        }
    }

    /// <summary>Whether the value is written in each cell.</summary>
    [Category("Appearance"), DisplayName("Show cell labels")]
    public bool ShowCellLabels
    {
        get => _showCellLabels;
        set => SetProperty(ref _showCellLabels, value, InvalidationKind.Render);
    }

    /// <summary>
    /// What colour the cell labels are written in, or null to pick black or white per cell against
    /// what it is sitting on — which is the only setting that stays legible across a whole colormap.
    /// </summary>
    [Category("Appearance"), DisplayName("Cell label color")]
    public Color? CellLabelColor
    {
        get => _cellLabelColor;
        set => SetProperty(ref _cellLabelColor, value, InvalidationKind.Render);
    }

    /// <summary>
    /// A .NET numeric format for the cell labels, or null for a general four-digit one. Scripts write
    /// this as a printf spec and it is translated on the way in, the same as a tick format.
    /// </summary>
    [Browsable(false)]
    public string? CellLabelFormat
    {
        get => _cellLabelFormat;
        set => SetProperty(ref _cellLabelFormat, string.IsNullOrEmpty(value) ? null : value, InvalidationKind.Render);
    }

    /// <summary>The font the cell labels are written in. Its colour is used when no cell label colour is set to none.</summary>
    [Category("Appearance"), DisplayName("Cell label style")]
    public TextStyle CellLabelStyle
    {
        get => _cellLabelStyle;
        set => SetProperty(ref _cellLabelStyle, value, InvalidationKind.Render);
    }

    /// <summary>Whether the lines between cells are drawn.</summary>
    [Category("Appearance"), DisplayName("Grid visible")]
    public bool GridVisible
    {
        get => _gridVisible;
        set => SetProperty(ref _gridVisible, value, InvalidationKind.Render);
    }

    /// <summary>The colour of the lines between cells.</summary>
    [Category("Appearance"), DisplayName("Grid color")]
    public Color GridColor
    {
        get => _gridColor;
        set => SetProperty(ref _gridColor, value, InvalidationKind.Render);
    }

    /// <summary>The colour a cell with no value takes.</summary>
    [Category("Appearance"), DisplayName("Missing data color")]
    public Color MissingDataColor
    {
        get => _missingDataColor;
        set
        {
            if (SetProperty(ref _missingDataColor, value, InvalidationKind.Render))
            {
                Rebuild();
            }
        }
    }

    /// <summary>What is written in a cell with no value.</summary>
    [Category("Appearance"), DisplayName("Missing data label")]
    public string MissingDataLabel
    {
        get => _missingDataLabel;
        set => SetProperty(ref _missingDataLabel, value ?? string.Empty, InvalidationKind.Render);
    }

    /// <inheritdoc />
    (double Min, double Max) IColorMapped.ColorRange => (EffectiveLimits().Min, EffectiveLimits().Max);

    /// <inheritdoc />
    bool IColorMapped.HasMappedData => Rows > 0 && Columns > 0;

    /// <summary>The column names as they are drawn: the ones given, or the numbers one upward.</summary>
    public IReadOnlyList<string> ColumnLabels() => Names(_xData, Columns);

    /// <summary>The row names as they are drawn.</summary>
    public IReadOnlyList<string> RowLabels() => Names(_yData, Rows);

    /// <summary>
    /// The colour range the whole chart is measured against: the one that was set, or the extent of
    /// the finite values. Under column or row scaling this is still the answer to what the colorbar
    /// legends, since the colorbar has only one strip to say it with.
    /// </summary>
    public DataRange EffectiveLimits()
    {
        if (_colorLimits is { } given && given.IsValid)
        {
            return given;
        }

        DataRange bounds = DataRange.Empty;
        foreach (double value in _colorData)
        {
            if (double.IsFinite(value))
            {
                bounds = bounds.Include(value);
            }
        }

        return bounds.IsValid ? bounds : new DataRange(0, 1);
    }

    /// <summary>
    /// Where cell <c>(r, c)</c> sits in the colormap, from 0 at the low end to 1 at the high end, or
    /// NaN when it has no value to place — which is what missing means to everything downstream.
    /// </summary>
    public double Fraction(int row, int column)
    {
        double value = _colorData[row, column];
        if (!double.IsFinite(value))
        {
            return double.NaN;
        }

        (double low, double high) = SliceLimits(row, column);
        if (_colorScaling == HeatmapScaling.Log)
        {
            // A logarithmic scale has nothing to say about zero or less, so such a cell is missing
            // rather than clamped onto the low end where it would read as the smallest value.
            if (value <= 0 || low <= 0 || high <= 0)
            {
                return double.NaN;
            }

            value = System.Math.Log10(value);
            low = System.Math.Log10(low);
            high = System.Math.Log10(high);
        }

        return high > low ? System.Math.Clamp((value - low) / (high - low), 0, 1) : 0.5;
    }

    /// <summary>The colour cell <c>(r, c)</c> is filled with.</summary>
    public Color ColorOf(int row, int column)
    {
        double fraction = Fraction(row, column);
        return double.IsNaN(fraction) ? _missingDataColor : _colormap.Sample(fraction);
    }

    /// <summary>What is written in cell <c>(r, c)</c>.</summary>
    public string CellLabel(int row, int column)
    {
        double value = _colorData[row, column];
        if (!double.IsFinite(value))
        {
            return _missingDataLabel;
        }

        return _cellLabelFormat is { } format
            ? value.ToString(format, CultureInfo.CurrentCulture)
            : value.ToString("G4", CultureInfo.CurrentCulture);
    }

    /// <inheritdoc />
    public override DataRange GetXDataBounds() =>
        Columns == 0 ? DataRange.Empty : new DataRange(-0.5, Columns - 0.5);

    /// <inheritdoc />
    public override DataRange GetYDataBounds() =>
        Rows == 0 ? DataRange.Empty : new DataRange(-0.5, Rows - 0.5);

    /// <inheritdoc />
    public void Render(IRenderContext context, RenderState state)
    {
        int rows = Rows;
        int columns = Columns;
        if (rows == 0 || columns == 0)
        {
            return;
        }

        ICoordinateMapper mapper = state.Mapper;
        Point2D first = mapper.DataToPixel(-0.5, -0.5);
        Point2D last = mapper.DataToPixel(columns - 0.5, rows - 0.5);
        Rect2D destination = Rect2D.FromCorners(first, last);

        // The tile's first row is drawn along the top edge, so it holds data row zero only when the
        // ruler puts row zero there — which is the usual way up, and not the only one.
        if (_pixels is null || _builtOpacity != Opacity)
        {
            BuildTile(first.Y <= last.Y);
        }

        context.DrawImage(_pixels!, columns, rows, destination, interpolate: false);

        if (_gridVisible)
        {
            DrawGrid(context, mapper, rows, columns);
        }

        if (_showCellLabels)
        {
            DrawCellLabels(context, mapper, rows, columns);
        }
    }

    /// <inheritdoc />
    public override PlotHitResult? HitTest(Point2D pixelPoint, ICoordinateMapper mapper, double tolerancePixels)
    {
        if (!HitTestVisible || Rows == 0 || Columns == 0)
        {
            return null;
        }

        Point2D point = mapper.PixelToData(pixelPoint.X, pixelPoint.Y);
        int column = (int)System.Math.Round(point.X, MidpointRounding.AwayFromZero);
        int row = (int)System.Math.Round(point.Y, MidpointRounding.AwayFromZero);
        if (column < 0 || column >= Columns || row < 0 || row >= Rows)
        {
            return null;
        }

        // The point index is the cell in column-major order, which is where the value would be found
        // in the matrix a script passed in.
        return new PlotHitResult(
            this, new Point2D(column, row), 0, (column * Rows) + row);
    }

    private static IReadOnlyList<string> Names(string[]? given, int count)
    {
        if (given is not null && given.Length == count)
        {
            return given;
        }

        var names = new string[count];
        for (int i = 0; i < count; i++)
        {
            names[i] = given is not null && i < given.Length
                ? given[i]
                : (i + 1).ToString(CultureInfo.CurrentCulture);
        }

        return names;
    }

    /// <summary>The range cell <c>(r, c)</c> is measured against, which is what the scaling names.</summary>
    private (double Low, double High) SliceLimits(int row, int column)
    {
        switch (_colorScaling)
        {
            case HeatmapScaling.ScaledColumns:
                return Extent(row: -1, column: column);
            case HeatmapScaling.ScaledRows:
                return Extent(row: row, column: -1);
            default:
                DataRange limits = EffectiveLimits();
                return (limits.Min, limits.Max);
        }
    }

    /// <summary>The extent of one row, or of one column — whichever of the two is not -1.</summary>
    private (double Low, double High) Extent(int row, int column)
    {
        DataRange bounds = DataRange.Empty;
        int rows = Rows;
        int columns = Columns;
        for (int r = row < 0 ? 0 : row; r < (row < 0 ? rows : row + 1); r++)
        {
            for (int c = column < 0 ? 0 : column; c < (column < 0 ? columns : column + 1); c++)
            {
                if (double.IsFinite(_colorData[r, c]))
                {
                    bounds = bounds.Include(_colorData[r, c]);
                }
            }
        }

        return bounds.IsValid ? (bounds.Min, bounds.Max) : (0, 1);
    }

    private void Rebuild() => _pixels = null;

    private void BuildTile(bool rowZeroFirst)
    {
        int rows = Rows;
        int columns = Columns;
        var pixels = new uint[rows * columns];
        double opacity = Opacity;

        for (int r = 0; r < rows; r++)
        {
            int target = (rowZeroFirst ? r : rows - 1 - r) * columns;
            for (int c = 0; c < columns; c++)
            {
                pixels[target + c] = ColorOf(r, c).WithOpacity(opacity).ToArgb();
            }
        }

        _pixels = pixels;
        _builtOpacity = opacity;
    }

    private void DrawGrid(IRenderContext context, ICoordinateMapper mapper, int rows, int columns)
    {
        var vertices = new List<Point2D>(2 * (rows + columns + 2));
        var starts = new List<int>(rows + columns + 2);

        for (int c = 0; c <= columns; c++)
        {
            starts.Add(vertices.Count);
            vertices.Add(mapper.DataToPixel(c - 0.5, -0.5));
            vertices.Add(mapper.DataToPixel(c - 0.5, rows - 0.5));
        }

        for (int r = 0; r <= rows; r++)
        {
            starts.Add(vertices.Count);
            vertices.Add(mapper.DataToPixel(-0.5, r - 0.5));
            vertices.Add(mapper.DataToPixel(columns - 0.5, r - 0.5));
        }

        context.DrawPaths(
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(vertices),
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(starts),
            closed: false,
            new LineStyle(_gridColor.WithOpacity(Opacity), 1),
            fill: null);
    }

    private void DrawCellLabels(IRenderContext context, ICoordinateMapper mapper, int rows, int columns)
    {
        // One cell's size in pixels is every cell's, so a label that does not fit in this one does
        // not fit in any — but the text differs per cell, so each is measured.
        Point2D corner = mapper.DataToPixel(-0.5, -0.5);
        Point2D opposite = mapper.DataToPixel(0.5, 0.5);
        double cellWidth = System.Math.Abs(opposite.X - corner.X);
        double cellHeight = System.Math.Abs(opposite.Y - corner.Y);

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                string text = CellLabel(r, c);
                if (text.Length == 0)
                {
                    continue;
                }

                TextStyle style = _cellLabelStyle.WithColor(
                    (_cellLabelColor ?? Contrasting(ColorOf(r, c))).WithOpacity(Opacity));
                Size2D size = context.MeasureText(text, style);
                if (size.Width > cellWidth || size.Height > cellHeight)
                {
                    continue;
                }

                context.DrawText(
                    text,
                    mapper.DataToPixel(c, r),
                    style,
                    HorizontalAlignment.Center,
                    VerticalAlignment.Middle);
            }
        }
    }

    /// <summary>Black or white, whichever stands out against what the label is written on.</summary>
    private static Color Contrasting(Color background)
    {
        double luminance = ((0.299 * background.R) + (0.587 * background.G) + (0.114 * background.B)) / 255;
        return luminance > 0.55 ? Colors.Black : Colors.White;
    }
}
