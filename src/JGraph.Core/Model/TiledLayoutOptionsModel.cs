using System.ComponentModel;

namespace JGraph.Core.Model;

/// <summary>
/// Where one axes sits in a tiled layout — MATLAB's <c>ax.Layout</c>, a small object with two
/// properties on it. It is a view of the axes rather than a thing of its own: reading it asks the
/// axes, and writing it moves the axes and lays the grid out again.
/// <para>
/// It exists because MATLAB reaches a tile's place through a nested object rather than through a
/// property on the axes, and a script that says <c>ax.Layout.Tile = 3</c> is saying something this
/// build can act on.
/// </para>
/// </summary>
public sealed class TiledLayoutOptionsModel : GraphObject
{
    private readonly AxesModel _axes;

    public TiledLayoutOptionsModel(AxesModel axes)
    {
        _axes = axes ?? throw new ArgumentNullException(nameof(axes));
        Name = "Tile";
    }

    /// <summary>The axes this describes the place of.</summary>
    [Browsable(false)]
    public AxesModel Axes => _axes;

    /// <summary>Which cell of the grid the axes holds, counting from one.</summary>
    [Category("Layout")]
    public int Tile
    {
        get => _axes.LayoutTile ?? 0;
        set
        {
            _axes.LayoutTile = value;
            Rearrange();
        }
    }

    /// <summary>How many rows of the grid the tile covers.</summary>
    [Category("Layout"), DisplayName("Row span")]
    public int RowSpan
    {
        get => _axes.LayoutRowSpan;
        set
        {
            _axes.LayoutRowSpan = value;
            Rearrange();
        }
    }

    /// <inheritdoc cref="RowSpan" />
    [Category("Layout"), DisplayName("Column span")]
    public int ColumnSpan
    {
        get => _axes.LayoutColumnSpan;
        set
        {
            _axes.LayoutColumnSpan = value;
            Rearrange();
        }
    }

    private void Rearrange() => (_axes.Parent as FigureModel)?.TiledLayout?.Arrange();
}
