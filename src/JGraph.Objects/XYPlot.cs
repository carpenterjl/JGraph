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

    /// <summary>
    /// Whether the X coordinates are the counting numbers this plot supplied for itself, because the
    /// call gave only Y. MATLAB calls the same thing <c>XDataMode</c>, and answers 'auto' for exactly
    /// this case — which is why it is a flag rather than a comparison against 1:n: a script that plots
    /// against a genuine 1:n has chosen those positions, and MATLAB says so.
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    public bool XImplied { get; set; }

    /// <summary>
    /// Whether this series' dash pattern was chosen rather than handed out by the axes' line-style
    /// cycle. MATLAB's <c>LineStyleMode</c> is this question, and it cannot be derived from the dash
    /// alone: solid is both the default and a legal choice. The setter that writes a dash sets this,
    /// and the cycler clears it again after writing its own.
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    public bool LineStyleManual { get; set; }

    /// <summary>Whether the marker was chosen rather than handed out by the cycle — <c>MarkerMode</c>.</summary>
    [System.ComponentModel.Browsable(false)]
    public bool MarkerManual { get; set; }

    /// <summary>Replaces the data with new X/Y arrays. Chosen X positions are no longer implied ones.</summary>
    public void SetData(double[] xs, double[] ys)
    {
        Data = new ArrayDataSeries(xs, ys);
        XImplied = false;
    }

    public override DataRange GetXDataBounds() => _data.XBounds;

    public override DataRange GetYDataBounds() => _data.YBounds;
}
