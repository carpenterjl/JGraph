using JGraph.Core.Data;
using JGraph.Core.Model;
using JGraph.Data;

namespace JGraph.Objects;

/// <summary>
/// Fluent helpers that plot columns of a <see cref="Table"/> directly (<c>axes.AddLine(table, "x",
/// "y")</c>). Each helper wires the axis up for the X column's type — a date/time column switches the
/// X axis to a date scale, a text column to a category scale — and labels the axes from the column
/// names when they are not already set.
/// </summary>
public static class TableAxesExtensions
{
    /// <summary>Adds a line plot of <paramref name="yColumn"/> against <paramref name="xColumn"/> (null = row index).</summary>
    public static LinePlot AddLine(this AxesModel axes, Table table, string? xColumn, string yColumn)
    {
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(table);
        var plot = new LinePlot(TableSeries.Create(table, xColumn, yColumn)) { DisplayName = yColumn };
        axes.Plots.Add(plot);
        ConfigureAxes(axes, table, xColumn, yColumn);
        return plot;
    }

    /// <summary>Adds a scatter plot of <paramref name="yColumn"/> against <paramref name="xColumn"/> (null = row index).</summary>
    public static ScatterPlot AddScatter(this AxesModel axes, Table table, string? xColumn, string yColumn)
    {
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(table);
        var plot = new ScatterPlot(TableSeries.Create(table, xColumn, yColumn)) { DisplayName = yColumn };
        axes.Plots.Add(plot);
        ConfigureAxes(axes, table, xColumn, yColumn);
        return plot;
    }

    /// <summary>Adds a stem plot of <paramref name="yColumn"/> against <paramref name="xColumn"/> (null = row index).</summary>
    public static StemPlot AddStem(this AxesModel axes, Table table, string? xColumn, string yColumn)
    {
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(table);
        var plot = new StemPlot(TableSeries.Create(table, xColumn, yColumn)) { DisplayName = yColumn };
        axes.Plots.Add(plot);
        ConfigureAxes(axes, table, xColumn, yColumn);
        return plot;
    }

    /// <summary>
    /// Adds a bar plot of <paramref name="valueColumn"/>, one bar per row, labeled by
    /// <paramref name="categoryColumn"/> (used verbatim per row, not de-duplicated). When the category
    /// column is null the bars sit at row-index positions with a plain numeric axis.
    /// </summary>
    public static BarPlot AddBar(this AxesModel axes, Table table, string? categoryColumn, string valueColumn)
    {
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(table);

        double[] values = TableSeries.GetNumbers(table, valueColumn);
        var positions = new double[values.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            positions[i] = i;
        }

        var plot = new BarPlot(new ArrayDataSeries(positions, values)) { DisplayName = valueColumn };
        axes.Plots.Add(plot);

        if (categoryColumn is not null)
        {
            TableColumn category = table[categoryColumn];
            var labels = new string[table.RowCount];
            for (int i = 0; i < labels.Length; i++)
            {
                labels[i] = category.GetText(i);
            }

            axes.PrimaryXAxis.UseCategories(labels);
        }

        SetAxisLabels(axes, categoryColumn ?? string.Empty, valueColumn);
        return plot;
    }

    /// <summary>Adds a histogram over the values of <paramref name="valueColumn"/>.</summary>
    public static HistogramPlot AddHistogram(this AxesModel axes, Table table, string valueColumn, int binCount = 10)
    {
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(table);

        var plot = new HistogramPlot(TableSeries.GetNumbers(table, valueColumn))
        {
            BinCount = binCount,
            DisplayName = valueColumn,
        };
        axes.Plots.Add(plot);
        SetAxisLabels(axes, valueColumn, "Count");
        return plot;
    }

    /// <summary>Adds an error-bar plot with symmetric Y errors taken from <paramref name="errorColumn"/>.</summary>
    public static ErrorBarPlot AddErrorBar(this AxesModel axes, Table table, string? xColumn, string yColumn, string errorColumn)
    {
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(table);

        double[] error = TableSeries.GetNumbers(table, errorColumn);
        var plot = new ErrorBarPlot(TableSeries.Create(table, xColumn, yColumn), error, error) { DisplayName = yColumn };
        axes.Plots.Add(plot);
        ConfigureAxes(axes, table, xColumn, yColumn);
        return plot;
    }

    private static void ConfigureAxes(AxesModel axes, Table table, string? xColumn, string yColumn)
    {
        if (xColumn is not null)
        {
            TableColumn column = table[xColumn];
            switch (column)
            {
                case DateTimeColumn:
                    axes.PrimaryXAxis.UseDateTime();
                    break;
                case TextColumn text:
                    axes.PrimaryXAxis.UseCategories(text.Categories);
                    break;
            }
        }

        SetAxisLabels(axes, xColumn ?? string.Empty, yColumn);
    }

    private static void SetAxisLabels(AxesModel axes, string xLabel, string yLabel)
    {
        if (xLabel.Length > 0 && string.IsNullOrEmpty(axes.PrimaryXAxis.Label))
        {
            axes.PrimaryXAxis.Label = xLabel;
        }

        if (string.IsNullOrEmpty(axes.PrimaryYAxis.Label))
        {
            axes.PrimaryYAxis.Label = yLabel;
        }
    }
}
