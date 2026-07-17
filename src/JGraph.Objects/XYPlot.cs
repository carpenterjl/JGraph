using JGraph.Core.Data;
using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Objects;

/// <summary>
/// The abstract base for plot objects backed by a 2D <see cref="IDataSeries"/> (line, scatter, bar).
/// It owns the data source and reports its extents for auto-scaling. Concrete subclasses add styling
/// and drawing.
/// </summary>
public abstract class XYPlot : PlotObject
{
    private IDataSeries _data;

    protected XYPlot(IDataSeries data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    /// <summary>The data source for this plot.</summary>
    public IDataSeries Data
    {
        get => _data;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            SetProperty(ref _data, value, InvalidationKind.Data);
        }
    }

    /// <summary>Replaces the data with new X/Y arrays.</summary>
    public void SetData(double[] xs, double[] ys) => Data = new ArrayDataSeries(xs, ys);

    public override DataRange GetXDataBounds() => _data.XBounds;

    public override DataRange GetYDataBounds() => _data.YBounds;
}
