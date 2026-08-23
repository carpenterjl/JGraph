using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>How much room is left between one tile and the next (MATLAB <c>TileSpacing</c>).</summary>
public enum TileSpacingMode
{
    /// <summary>The default: room for each tile's own labels between them.</summary>
    Loose,

    /// <summary>Closer, leaving room for the ticks but not for a label on every tile.</summary>
    Compact,

    /// <summary>Closer still: a hairline, so the tiles read as one picture.</summary>
    Tight,

    /// <summary>None at all — the tiles touch.</summary>
    None,
}

/// <summary>How much room is left around the whole grid (MATLAB <c>Padding</c>).</summary>
public enum TilePaddingMode
{
    /// <summary>The default: a band of figure between the grid and the edge.</summary>
    Loose,

    /// <summary>Half of it.</summary>
    Compact,

    /// <summary>None: the grid reaches the figure's edge, which is where a subplot grid starts.</summary>
    Tight,
}

/// <summary>Which way the tiles are counted (MATLAB <c>TileIndexing</c>).</summary>
public enum TileIndexingMode
{
    /// <summary>Left to right, then down — the way <c>subplot</c> counts and MATLAB's default.</summary>
    RowMajor,

    /// <summary>Top to bottom, then across.</summary>
    ColumnMajor,
}

/// <summary>
/// A grid of tiles laid over a figure (MATLAB <c>tiledlayout</c>), and the object <c>nexttile</c>
/// hands cells out of.
/// <para>
/// Until M80 this was three integers and a flag living in a closure inside the script layer, which is
/// why <c>t.TileSpacing</c>, <c>nexttile(span)</c> and <c>tiledlayout(parent, …)</c> could not work:
/// there was no <c>t</c>. Making it an object is what gives those forms something to name, and gives
/// every axes in the grid a <c>Layout</c> to answer with.
/// </para>
/// <para>
/// Its geometry is deliberately its own rather than <see cref="FigureModel.SubplotBounds"/>: a
/// subplot grid keeps the arithmetic it has always had, and a tiled layout gets the one its own
/// spacing and padding describe. A default tiled figure therefore sits a little inside the frame
/// where a subplot figure meets it, which is what <c>Padding</c> means and the only way for the word
/// to mean anything.
/// </para>
/// </summary>
public sealed class TiledLayoutModel : GraphObject
{
    /// <summary>The gutter each spacing word leaves, as a fraction of one cell.</summary>
    private const double LooseSpacing = 0.12;
    private const double CompactSpacing = 0.05;
    private const double TightSpacing = 0.02;

    /// <summary>The band each padding word leaves outside the grid, as a fraction of the figure.</summary>
    private const double LoosePadding = 0.02;
    private const double CompactPadding = 0.01;

    /// <summary>The bands a title, a subtitle and a shared label take, as fractions of the figure.</summary>
    private const double TitleBand = 0.07;
    private const double SubtitleBand = 0.05;
    private const double LabelBand = 0.05;

    private readonly List<AxesModel> _tiles = [];

    private int _rows = 1;
    private int _columns = 1;
    private bool _flow;
    private TileSpacingMode _tileSpacing = TileSpacingMode.Loose;
    private TilePaddingMode _padding = TilePaddingMode.Loose;
    private TileIndexingMode _tileIndexing = TileIndexingMode.RowMajor;
    private string _title = string.Empty;
    private string _subtitle = string.Empty;
    private string _xLabel = string.Empty;
    private string _yLabel = string.Empty;
    private TextStyle _titleStyle = new(Colors.Black, 14, bold: true);
    private TextStyle _subtitleStyle = new(Color.FromScRgb(0.35, 0.35, 0.35), 11);
    private TextStyle _xLabelStyle = new(Colors.Black, 11);
    private TextStyle _yLabelStyle = new(Colors.Black, 11);
    private Rect2D _bounds = new(0, 0, 1, 1);

    public TiledLayoutModel() => Name = "Tiled layout";

    /// <summary>How many rows the grid has. A flowing layout grows this as tiles are asked for.</summary>
    [Category("Layout")]
    public int Rows
    {
        get => _rows;
        set => SetProperty(ref _rows, System.Math.Max(1, value), InvalidationKind.Layout);
    }

    /// <inheritdoc cref="Rows" />
    [Category("Layout")]
    public int Columns
    {
        get => _columns;
        set => SetProperty(ref _columns, System.Math.Max(1, value), InvalidationKind.Layout);
    }

    /// <summary>
    /// Whether the grid chooses its own shape as tiles are asked for (<c>tiledlayout('flow')</c>).
    /// A flowing layout never wraps: it grows until it holds the tile just asked for.
    /// </summary>
    [Browsable(false)]
    public bool Flow
    {
        get => _flow;
        set => SetProperty(ref _flow, value, InvalidationKind.Layout);
    }

    [Category("Layout"), DisplayName("Tile spacing")]
    public TileSpacingMode TileSpacing
    {
        get => _tileSpacing;
        set => SetProperty(ref _tileSpacing, value, InvalidationKind.Layout);
    }

    [Category("Layout")]
    public TilePaddingMode Padding
    {
        get => _padding;
        set => SetProperty(ref _padding, value, InvalidationKind.Layout);
    }

    [Category("Layout"), DisplayName("Tile indexing")]
    public TileIndexingMode TileIndexing
    {
        get => _tileIndexing;
        set => SetProperty(ref _tileIndexing, value, InvalidationKind.Layout);
    }

    /// <summary>A title over the whole grid, rather than over any one tile.</summary>
    [Category("General")]
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value ?? string.Empty, InvalidationKind.Layout);
    }

    [Category("General")]
    public string Subtitle
    {
        get => _subtitle;
        set => SetProperty(ref _subtitle, value ?? string.Empty, InvalidationKind.Layout);
    }

    /// <summary>One label under the whole grid, shared by every tile in it.</summary>
    [Category("General"), DisplayName("X label")]
    public string XLabel
    {
        get => _xLabel;
        set => SetProperty(ref _xLabel, value ?? string.Empty, InvalidationKind.Layout);
    }

    /// <inheritdoc cref="XLabel" />
    [Category("General"), DisplayName("Y label")]
    public string YLabel
    {
        get => _yLabel;
        set => SetProperty(ref _yLabel, value ?? string.Empty, InvalidationKind.Layout);
    }

    [Category("General"), DisplayName("Title style")]
    public TextStyle TitleStyle
    {
        get => _titleStyle;
        set => SetProperty(ref _titleStyle, value, InvalidationKind.Layout);
    }

    [Category("General"), DisplayName("Subtitle style")]
    public TextStyle SubtitleStyle
    {
        get => _subtitleStyle;
        set => SetProperty(ref _subtitleStyle, value, InvalidationKind.Layout);
    }

    [Category("General"), DisplayName("X label style")]
    public TextStyle XLabelStyle
    {
        get => _xLabelStyle;
        set => SetProperty(ref _xLabelStyle, value, InvalidationKind.Layout);
    }

    [Category("General"), DisplayName("Y label style")]
    public TextStyle YLabelStyle
    {
        get => _yLabelStyle;
        set => SetProperty(ref _yLabelStyle, value, InvalidationKind.Layout);
    }

    /// <summary>
    /// The part of the figure the grid is laid in, in the same fractions and the same downward Y as
    /// an axes' own bounds. The whole figure by default, which is what <c>tiledlayout</c> asks for.
    /// </summary>
    [Browsable(false)]
    public Rect2D Bounds
    {
        get => _bounds;
        set => SetProperty(ref _bounds, value, InvalidationKind.Layout);
    }

    /// <summary>The tiles this layout has handed out, in the order it handed them out.</summary>
    [Browsable(false)]
    public IReadOnlyList<AxesModel> Tiles => _tiles;

    /// <summary>How many cells the grid holds.</summary>
    [Browsable(false)]
    public int TileCount => _rows * _columns;

    /// <summary>
    /// The band each of the four pieces of shared text takes, from the outside in: the title and the
    /// subtitle at the top, the two labels at the bottom and the left. The renderer draws into
    /// exactly these, so the arithmetic that reserves the room and the arithmetic that fills it are
    /// the same arithmetic.
    /// </summary>
    [Browsable(false)]
    public double TopBand =>
        (_title.Length > 0 ? TitleBand : 0) + (_subtitle.Length > 0 ? SubtitleBand : 0);

    /// <inheritdoc cref="TopBand" />
    [Browsable(false)]
    public double BottomBand => _xLabel.Length > 0 ? LabelBand : 0;

    /// <inheritdoc cref="TopBand" />
    [Browsable(false)]
    public double LeftBand => _yLabel.Length > 0 ? LabelBand : 0;

    /// <summary>Where the title sits, when there is one: the top of the layout's own band.</summary>
    [Browsable(false)]
    public Rect2D TitleBox => Band(0, _title.Length > 0 ? TitleBand : 0);

    /// <inheritdoc cref="TitleBox" />
    [Browsable(false)]
    public Rect2D SubtitleBox =>
        Band(_title.Length > 0 ? TitleBand : 0, _subtitle.Length > 0 ? SubtitleBand : 0);

    /// <summary>Takes a tile into the grid, in the order the tiles were asked for.</summary>
    public void Adopt(AxesModel tile)
    {
        ArgumentNullException.ThrowIfNull(tile);
        if (!_tiles.Contains(tile))
        {
            _tiles.Add(tile);
        }
    }

    /// <summary>Forgets every tile, which is what a fresh <c>tiledlayout</c> call does.</summary>
    public void Clear() => _tiles.Clear();

    /// <summary>
    /// Grows a flowing grid until it holds <paramref name="tile"/>, a column at a time and then a
    /// row — which keeps the tiles as square as a fixed grid's without knowing the count in advance.
    /// </summary>
    public void GrowToHold(int tile)
    {
        while (tile > TileCount)
        {
            if (_columns <= _rows)
            {
                Columns = _columns + 1;
            }
            else
            {
                Rows = _rows + 1;
            }
        }
    }

    /// <summary>
    /// Lays every tile out where the grid now says it goes. A flowing layout calls this each time it
    /// grows, because every tile already handed out belongs to the grid as it is rather than as it
    /// was — moving them is what makes the layout flow instead of pile up.
    /// </summary>
    public void Arrange()
    {
        foreach (AxesModel tile in _tiles)
        {
            if (tile.LayoutTile is { } cell)
            {
                tile.NormalizedBounds = BoundsFor(cell, tile.LayoutRowSpan, tile.LayoutColumnSpan);
            }
        }
    }

    /// <summary>
    /// Where one tile sits: its cell in the grid, less the spacing between cells, inside the padding
    /// and whatever the shared text is taking.
    /// </summary>
    public Rect2D BoundsFor(int tile, int rowSpan, int columnSpan)
    {
        int index = System.Math.Clamp(tile, 1, System.Math.Max(1, TileCount)) - 1;
        (int row, int column) = _tileIndexing == TileIndexingMode.ColumnMajor
            ? (index % _rows, index / _rows)
            : (index / _columns, index % _columns);

        int rows = System.Math.Clamp(rowSpan, 1, _rows - row);
        int columns = System.Math.Clamp(columnSpan, 1, _columns - column);

        double pad = _padding switch
        {
            TilePaddingMode.Loose => LoosePadding,
            TilePaddingMode.Compact => CompactPadding,
            _ => 0,
        };

        double left = _bounds.Left + ((pad + LeftBand) * _bounds.Width);
        double top = _bounds.Top + ((pad + TopBand) * _bounds.Height);
        double width = _bounds.Width * (1 - (2 * pad) - LeftBand);
        double height = _bounds.Height * (1 - (2 * pad) - TopBand - BottomBand);

        double cellW = width / _columns;
        double cellH = height / _rows;
        double gutter = _tileSpacing switch
        {
            TileSpacingMode.Loose => LooseSpacing,
            TileSpacingMode.Compact => CompactSpacing,
            TileSpacingMode.Tight => TightSpacing,
            _ => 0,
        };

        double marginX = gutter * cellW * 0.5;
        double marginY = gutter * cellH * 0.5;
        return new Rect2D(
            left + (column * cellW) + marginX,
            top + (row * cellH) + marginY,
            (columns * cellW) - (2 * marginX),
            (rows * cellH) - (2 * marginY));
    }

    private Rect2D Band(double from, double height) => new(
        _bounds.Left, _bounds.Top + (from * _bounds.Height), _bounds.Width, height * _bounds.Height);
}
