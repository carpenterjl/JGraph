using JGraph.Core.Data;
using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Objects;

/// <summary>
/// Fluent factory helpers that make up the object-oriented API (<c>axes.AddLine(x, y)</c>). They live
/// here rather than on <see cref="AxesModel"/> because the core object model does not depend on the
/// concrete plot types defined in this assembly.
/// </summary>
public static class AxesExtensions
{
    /// <summary>Adds a line plot for the given X/Y data and returns it.</summary>
    public static LinePlot AddLine(this AxesModel axes, double[] xs, double[] ys)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new LinePlot(xs, ys);
        axes.Plots.Add(plot);
        return plot;
    }

    /// <summary>Adds a line plot with implicit X indices 0, 1, 2, ... for the given Y values.</summary>
    public static LinePlot AddLine(this AxesModel axes, double[] ys)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new LinePlot(ArrayDataSeries.FromValues(ys));
        axes.Plots.Add(plot);
        return plot;
    }

    /// <summary>Adds a line plot backed by an arbitrary data series and returns it.</summary>
    public static LinePlot AddLine(this AxesModel axes, IDataSeries data)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new LinePlot(data);
        axes.Plots.Add(plot);
        return plot;
    }

    /// <summary>
    /// Adds a line plot for date/time X values, switching the primary X axis to a date/time scale.
    /// </summary>
    public static LinePlot AddLine(this AxesModel axes, DateTime[] times, double[] ys)
    {
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(times);
        LinePlot plot = axes.AddLine(DateTimeAxis.ToValues(times), ys);
        axes.PrimaryXAxis.UseDateTime();
        return plot;
    }

    /// <summary>Adds a scatter plot for the given X/Y data and returns it.</summary>
    public static ScatterPlot AddScatter(this AxesModel axes, double[] xs, double[] ys)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new ScatterPlot(xs, ys);
        axes.Plots.Add(plot);
        return plot;
    }

    /// <summary>Adds a bar plot for the given positions/values and returns it.</summary>
    public static BarPlot AddBar(this AxesModel axes, double[] positions, double[] values)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new BarPlot(positions, values);
        axes.Plots.Add(plot);
        return plot;
    }

    /// <summary>
    /// Adds a bar plot whose X axis is a category scale labeled with <paramref name="categories"/>.
    /// Bars are placed at 0, 1, 2, … and the axis shows the category labels.
    /// </summary>
    public static BarPlot AddBar(this AxesModel axes, string[] categories, double[] values)
    {
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(values);
        var positions = new double[values.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            positions[i] = i;
        }

        BarPlot plot = axes.AddBar(positions, values);
        axes.PrimaryXAxis.UseCategories(categories);
        return plot;
    }

    /// <summary>Adds a stem plot for the given X/Y data and returns it.</summary>
    public static StemPlot AddStem(this AxesModel axes, double[] xs, double[] ys)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new StemPlot(xs, ys);
        axes.Plots.Add(plot);
        return plot;
    }

    /// <summary>Adds a stem plot with implicit X indices 0, 1, 2, … for the given Y values.</summary>
    public static StemPlot AddStem(this AxesModel axes, double[] ys)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new StemPlot(ArrayDataSeries.FromValues(ys));
        axes.Plots.Add(plot);
        return plot;
    }

    /// <summary>Adds a histogram over the given raw sample values and returns it.</summary>
    public static HistogramPlot AddHistogram(this AxesModel axes, double[] values, int binCount = 10)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new HistogramPlot(values) { BinCount = binCount };
        axes.Plots.Add(plot);
        return plot;
    }

    /// <summary>Adds an error-bar plot with symmetric Y errors and returns it.</summary>
    public static ErrorBarPlot AddErrorBar(this AxesModel axes, double[] xs, double[] ys, double[] error)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new ErrorBarPlot(xs, ys, error);
        axes.Plots.Add(plot);
        return plot;
    }

    /// <summary>Adds an error-bar plot with asymmetric lower/upper Y errors and returns it.</summary>
    public static ErrorBarPlot AddErrorBar(this AxesModel axes, double[] xs, double[] ys, double[] errorNeg, double[] errorPos)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new ErrorBarPlot(new ArrayDataSeries(xs, ys), errorNeg, errorPos);
        axes.Plots.Add(plot);
        return plot;
    }

    /// <summary>Adds an image/heatmap over a [rows, cols] scalar field spanning the unit-per-cell grid.</summary>
    public static ImagePlot AddImage(this AxesModel axes, double[,] values)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new ImagePlot(values);
        axes.Plots.Add(plot);
        return plot;
    }

    /// <summary>Adds an image/heatmap over a scalar field spanning the given data-space extents.</summary>
    public static ImagePlot AddImage(this AxesModel axes, double[,] values, DataRange xExtent, DataRange yExtent)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new ImagePlot(values) { XExtent = xExtent, YExtent = yExtent };
        axes.Plots.Add(plot);
        return plot;
    }
}
