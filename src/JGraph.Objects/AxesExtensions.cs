using JGraph.Core.Data;
using JGraph.Core.Drawing;
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

    /// <summary>
    /// Adds a scatter whose marker sizes are read against the axes' bubble scale (MATLAB
    /// <c>bubblechart</c>) and returns it.
    /// </summary>
    public static ScatterPlot AddBubbleChart(this AxesModel axes, double[] xs, double[] ys, double[] sizes)
    {
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(sizes);

        var plot = new ScatterPlot(xs, ys) { BubbleSizing = true };
        plot.SizeData = sizes;
        axes.Plots.Add(plot);
        return plot;
    }

    /// <summary>Adds a filled area band for the given X/Y data and returns it.</summary>
    public static AreaPlot AddArea(this AxesModel axes, double[] xs, double[] ys)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new AreaPlot(xs, ys);
        axes.Plots.Add(plot);
        return plot;
    }

    /// <summary>
    /// Adds one band per column of <paramref name="columns"/>, each stacked on the ones before it,
    /// and returns them in column order. Stacking is the whole reason the plural form exists: the
    /// floor of a band is the running total beneath it, which only the caller who has every column
    /// can work out.
    /// </summary>
    public static IReadOnlyList<AreaPlot> AddStackedArea(
        this AxesModel axes, double[] xs, IReadOnlyList<double[]> columns)
    {
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(xs);
        ArgumentNullException.ThrowIfNull(columns);

        var running = new double[xs.Length];
        var created = new List<AreaPlot>(columns.Count);
        foreach (double[] column in columns)
        {
            AreaPlot plot = axes.AddArea(xs, column);
            if (created.Count > 0)
            {
                plot.LowerEdge = (double[])running.Clone();
            }

            for (int i = 0; i < running.Length && i < column.Length; i++)
            {
                if (double.IsFinite(column[i]))
                {
                    running[i] += column[i];
                }
            }

            created.Add(plot);
        }

        return created;
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

    /// <summary>
    /// Adds one bar series per column of <paramref name="columns"/> and returns them in column
    /// order. The series share the slot at each position when <paramref name="stacked"/> is false,
    /// standing side by side inside it; when it is true they stand on one another instead. Both
    /// arrangements are decided here, for the same reason the stacked area is: only the caller
    /// holding every column knows what the running total under each series is.
    /// </summary>
    public static IReadOnlyList<BarPlot> AddBar(
        this AxesModel axes, double[] positions, IReadOnlyList<double[]> columns, bool stacked)
    {
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(columns);

        var created = new List<BarPlot>(columns.Count);
        foreach (double[] column in columns)
        {
            created.Add(axes.AddBar(positions, column));
        }

        LayOutBars(created, stacked);
        return created;
    }

    /// <summary>
    /// Arranges a set of bar series side by side or on top of one another. It is a separate step
    /// from making them so that <c>h.BarLayout = 'stacked'</c> can run the same arithmetic on
    /// series that already exist: before M77 the layout was decided once, at creation, and there
    /// was no way back.
    /// </summary>
    public static void LayOutBars(IReadOnlyList<BarPlot> series, bool stacked)
    {
        ArgumentNullException.ThrowIfNull(series);
        if (series.Count == 0)
        {
            return;
        }

        int length = 0;
        foreach (BarPlot plot in series)
        {
            length = System.Math.Max(length, plot.Data.Count);
        }

        var running = new double[length];
        for (int index = 0; index < series.Count; index++)
        {
            BarPlot plot = series[index];
            if (!stacked)
            {
                plot.LowerEdge = null;
                plot.GroupIndex = index;
                plot.GroupCount = series.Count;
                continue;
            }

            plot.GroupIndex = 0;
            plot.GroupCount = 1;
            plot.LowerEdge = index > 0 ? (double[])running.Clone() : null;
            for (int i = 0; i < running.Length && i < plot.Data.Count; i++)
            {
                double value = plot.Data.GetY(i);
                if (double.IsFinite(value))
                {
                    running[i] += value;
                }
            }
        }
    }

    /// <summary>
    /// The bar series on one axes that share a layout: those standing on the same positions. A
    /// grouped chart and a second, separate one drawn over it are two arrangements, not one.
    /// </summary>
    public static IReadOnlyList<BarPlot> BarSiblingsOf(BarPlot plot)
    {
        ArgumentNullException.ThrowIfNull(plot);
        if (plot.Axes is not { } axes)
        {
            return [plot];
        }

        var kin = new List<BarPlot>();
        foreach (PlotObject other in axes.Plots)
        {
            if (other is BarPlot bar && SamePositions(bar, plot))
            {
                kin.Add(bar);
            }
        }

        return kin.Count > 0 ? kin : [plot];
    }

    private static bool SamePositions(BarPlot a, BarPlot b)
    {
        if (a.Data.Count != b.Data.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Data.Count; i++)
        {
            if (a.Data.GetX(i) != b.Data.GetX(i))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Adds a stairstep line for the given X/Y data and returns it.</summary>
    public static LinePlot AddStairs(this AxesModel axes, double[] xs, double[] ys)
    {
        ArgumentNullException.ThrowIfNull(axes);
        LinePlot plot = axes.AddLine(xs, ys);
        plot.Steps = StepMode.Post;
        plot.Name = "Stairs";
        return plot;
    }

    /// <summary>
    /// Adds a pie chart of <paramref name="values"/> and returns it, turning the axes into the kind
    /// of axes a pie needs: equal-aspect, so the circle is round, and without a frame or rulers,
    /// which have nothing to measure on a pie. That is done here rather than left to the caller
    /// because a pie drawn on ordinary axes is an ellipse in a box, which nobody wants.
    /// </summary>
    public static PiePlot AddPie(this AxesModel axes, double[] values)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new PiePlot(values);
        axes.Plots.Add(plot);
        axes.MakeCircular();
        return plot;
    }

    /// <summary>
    /// Turns the axes into a round, frameless canvas: equal aspect, no frame, and no ticks or tick
    /// labels on either ruler. This is what a pie — and later a polar chart — is drawn on.
    /// </summary>
    public static void MakeCircular(this AxesModel axes)
    {
        ArgumentNullException.ThrowIfNull(axes);
        axes.EqualAspect = true;
        axes.FrameVisible = false;
        foreach (AxisModel ruler in axes.XAxes.Concat(axes.YAxes))
        {
            ruler.ShowMajorTicks = false;
            ruler.ShowMinorTicks = false;
            ruler.ShowTickLabels = false;
        }
    }

    /// <summary>
    /// Switches the axes into the polar mode (MATLAB <c>polaraxes</c>): its plots' first coordinate
    /// becomes an angle in radians and their second a radius, the rings and spokes of the r and θ
    /// rulers stand in for the rectangular grid, and the Cartesian frame and rulers are put away.
    /// </summary>
    /// <remarks>
    /// The x and y rulers keep their state rather than being deleted, so a script that turns the mode
    /// back off finds the axes it had. That is the same bargain <see cref="AxesModel.Is3D"/> struck
    /// with the Z ruler, and it is what lets the mode be a property rather than a different object.
    /// </remarks>
    public static void MakePolar(this AxesModel axes)
    {
        ArgumentNullException.ThrowIfNull(axes);
        axes.IsPolar = true;
        axes.FrameVisible = false;
        axes.Grid.Visible = true;
        axes.Grid.ShowMajor = true;
        foreach (AxisModel ruler in axes.XAxes.Concat(axes.YAxes))
        {
            ruler.ShowMajorTicks = false;
            ruler.ShowMinorTicks = false;
            ruler.ShowTickLabels = false;
        }
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

    /// <summary>
    /// Adds a reference line across the whole axes at one X (MATLAB <c>xline</c>). It does not enter
    /// the auto-scale, so marking a threshold never moves the view.
    /// </summary>
    public static ConstantLinePlot AddXLine(this AxesModel axes, double x)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new ConstantLinePlot(ConstantLineDirection.Vertical, x);
        axes.Plots.Add(plot);
        return plot;
    }

    /// <summary>Adds a reference line across the whole axes at one Y (MATLAB <c>yline</c>).</summary>
    public static ConstantLinePlot AddYLine(this AxesModel axes, double y)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new ConstantLinePlot(ConstantLineDirection.Horizontal, y);
        axes.Plots.Add(plot);
        return plot;
    }

    /// <summary>Adds a histogram over the given raw sample values and returns it.</summary>
    public static HistogramPlot AddHistogram(this AxesModel axes, double[] values, int binCount = 10)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new HistogramPlot(values, binCount);
        axes.Plots.Add(plot);
        return plot;
    }

    /// <summary>
    /// Adds a histogram of angles — in radians — over the given bin edges, and returns it. The axes
    /// is left alone: what makes the wedges come out round is the polar mode, and turning that on is
    /// the caller's decision, not a side effect of adding a plot.
    /// </summary>
    public static PolarHistogramPlot AddPolarHistogram(
        this AxesModel axes, double[] data, double[] binEdges)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new PolarHistogramPlot(data, binEdges);
        axes.Plots.Add(plot);
        return plot;
    }

    /// <summary>Adds a histogram of angles from counts already taken, and returns it.</summary>
    public static PolarHistogramPlot AddPolarHistogramOfCounts(
        this AxesModel axes, double[] binEdges, double[] binCounts)
    {
        ArgumentNullException.ThrowIfNull(axes);
        PolarHistogramPlot plot = PolarHistogramPlot.FromCounts(binEdges, binCounts);
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

    /// <summary>Adds a true-colour image from row-major 0xAARRGGBB pixels (row 0 at the top).</summary>
    public static RgbImagePlot AddRgbImage(this AxesModel axes, uint[] pixelsArgb, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new RgbImagePlot(pixelsArgb, width, height);
        axes.Plots.Add(plot);
        return plot;
    }

    /// <summary>
    /// Adds a 3D surface over <c>z[row, col]</c> sampled at <c>x[col]</c>/<c>y[row]</c> and switches
    /// the axes into 3D mode. The style selects surf/mesh appearance.
    /// </summary>
    public static SurfacePlot AddSurface(
        this AxesModel axes,
        double[] x,
        double[] y,
        double[,] z,
        SurfaceStyle style = SurfaceStyle.FilledWithWireframe)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new SurfacePlot(x, y, z) { Style = style };
        axes.Plots.Add(plot);
        axes.Is3D = true;
        return plot;
    }

    /// <summary>
    /// Adds a parametric 3D surface — a position per vertex rather than per row and column — and
    /// switches the axes into 3D mode. This is the form a sphere or a cylinder needs.
    /// </summary>
    public static SurfacePlot AddSurface(
        this AxesModel axes,
        double[,] x,
        double[,] y,
        double[,] z,
        SurfaceStyle style = SurfaceStyle.FilledWithWireframe)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new SurfacePlot(x, y, z) { Style = style };
        axes.Plots.Add(plot);
        axes.Is3D = true;
        return plot;
    }

    /// <summary>
    /// Adds a polyline through points in space (MATLAB <c>plot3</c>) and switches the axes into 3D mode.
    /// </summary>
    public static Line3DPlot AddLine3D(this AxesModel axes, double[] x, double[] y, double[] z)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new Line3DPlot(x, y, z);
        axes.Plots.Add(plot);
        axes.Is3D = true;
        return plot;
    }

    /// <summary>
    /// Adds markers at points in space (MATLAB <c>scatter3</c>) and switches the axes into 3D mode.
    /// </summary>
    public static Scatter3DPlot AddScatter3D(this AxesModel axes, double[] x, double[] y, double[] z)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new Scatter3DPlot(x, y, z);
        axes.Plots.Add(plot);
        axes.Is3D = true;
        return plot;
    }

    /// <summary>
    /// Adds a stem per sample in space (MATLAB <c>stem3</c>) and switches the axes into 3D mode.
    /// </summary>
    public static Stem3DPlot AddStem3D(this AxesModel axes, double[] x, double[] y, double[] z)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new Stem3DPlot(x, y, z);
        axes.Plots.Add(plot);
        axes.Is3D = true;
        return plot;
    }

    /// <summary>
    /// Adds a field of bars standing on the floor (MATLAB <c>bar3</c>) and switches the axes into 3D
    /// mode. The horizontal form is the same chart with the bars laid along X, which is a property of
    /// the plot rather than a kind of its own.
    /// </summary>
    public static Bar3DPlot AddBar3D(this AxesModel axes, double[,] z, double[]? rowPositions = null)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new Bar3DPlot(z) { RowPositions = rowPositions };
        axes.Plots.Add(plot);
        axes.Is3D = true;
        return plot;
    }

    /// <summary>
    /// Adds a raised pie chart (MATLAB <c>pie3</c>) and turns the axes into the round, frameless one
    /// a pie belongs on, seen from above and to the side.
    /// </summary>
    public static Pie3DPlot AddPie3D(this AxesModel axes, double[] values)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new Pie3DPlot(values);
        axes.Plots.Add(plot);
        axes.Is3D = true;

        // A pie has nothing to say about position, so the rulers say nothing either — the same
        // frameless round canvas the flat pie is drawn on, with the height ruler quietened too.
        axes.MakeCircular();
        axes.ZAxis.ShowMajorTicks = false;
        axes.ZAxis.ShowMinorTicks = false;
        axes.ZAxis.ShowTickLabels = false;
        return plot;
    }

    /// <summary>
    /// Adds filled polygons over a shared vertex list (MATLAB <c>patch</c>). The axes is left in
    /// whatever mode it is already in — a patch draws in both — so <c>fill</c> and <c>fill3</c> differ
    /// only in whether the caller sets <see cref="AxesModel.Is3D"/> afterwards.
    /// </summary>
    public static PatchPlot AddPatch(this AxesModel axes, double[] x, double[] y, double[] z, int[][] faces)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new PatchPlot(x, y, z, faces);
        axes.Plots.Add(plot);
        return plot;
    }

    /// <summary>Adds a single-face patch through the given points, in the order given.</summary>
    public static PatchPlot AddPatch(this AxesModel axes, double[] x, double[] y, double[] z)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new PatchPlot(x, y, z);
        axes.Plots.Add(plot);
        return plot;
    }

    /// <summary>
    /// Adds a heatmap of <paramref name="colorData"/> and turns the axes into the labelled grid one
    /// belongs on: category rulers naming the columns and rows, no frame, no minor ticks, and the Y
    /// ruler inverted so that row zero is at the top the way a table reads.
    /// </summary>
    public static HeatmapPlot AddHeatmap(
        this AxesModel axes, double[,] colorData, string[]? xLabels = null, string[]? yLabels = null)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new HeatmapPlot(colorData) { XData = xLabels, YData = yLabels };
        axes.Plots.Add(plot);
        axes.LabelCells(plot);
        return plot;
    }

    /// <summary>
    /// Points the axes' rulers at the cells of <paramref name="plot"/>. Kept separate from
    /// <see cref="AddHeatmap"/> because relabelling has to happen again whenever the data or the
    /// names change, and the caller that changed them is the one that knows.
    /// </summary>
    public static void LabelCells(this AxesModel axes, HeatmapPlot plot)
    {
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(plot);

        axes.FrameVisible = false;
        // The rulers show the display labels when the chart has been given any, and the names it
        // knows the cells by otherwise — the two are the same until a script separates them.
        axes.PrimaryXAxis.UseCategories(plot.ColumnText());
        axes.PrimaryYAxis.UseCategories(plot.RowText());
        axes.PrimaryYAxis.Inverted = true;
        axes.PrimaryXAxis.ShowMinorTicks = false;
        axes.PrimaryYAxis.ShowMinorTicks = false;

        // The chart draws the lines between its own cells, and axes gridlines would run through the
        // middle of them rather than between them.
        axes.Grid.ShowMajor = false;
        axes.Grid.ShowMinor = false;
    }

    /// <summary>
    /// Adds a binned scatter of the readings (MATLAB <c>binscatter</c>) and shows the colorbar, since
    /// the colours are the only thing on the chart that says how many readings a bin holds.
    /// </summary>
    public static BinScatterPlot AddBinScatter(this AxesModel axes, double[] x, double[] y)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new BinScatterPlot(x, y);
        axes.Plots.Add(plot);
        axes.Colorbar.Visible = true;
        return plot;
    }

    /// <summary>
    /// Adds a box chart of <paramref name="yData"/>, cut into one box per distinct value of
    /// <paramref name="xData"/> when that is given and drawn as a single box when it is not.
    /// </summary>
    public static BoxChartPlot AddBoxChart(this AxesModel axes, double[]? xData, double[] yData)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new BoxChartPlot(xData, yData);
        axes.Plots.Add(plot);
        return plot;
    }

    /// <summary>Adds a field of arrows in the plane (MATLAB <c>quiver</c>) and returns it.</summary>
    public static QuiverPlot AddQuiver(this AxesModel axes, double[] x, double[] y, double[] u, double[] v)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new QuiverPlot(x, y, u, v);
        axes.Plots.Add(plot);
        return plot;
    }

    /// <summary>
    /// Adds a field of arrows in space (MATLAB <c>quiver3</c>) and switches the axes into 3D mode.
    /// </summary>
    public static QuiverPlot AddQuiver3(
        this AxesModel axes, double[] x, double[] y, double[] z, double[] u, double[] v, double[] w)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new QuiverPlot(x, y, z, u, v, w);
        axes.Plots.Add(plot);
        axes.Is3D = true;
        return plot;
    }

    /// <summary>Adds a 2D contour (or filled contour) plot of <c>z[row, col]</c> and returns it.</summary>
    public static ContourPlot AddContour(
        this AxesModel axes,
        double[] x,
        double[] y,
        double[,] z,
        double[]? levels = null,
        bool filled = false)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new ContourPlot(x, y, z) { Levels = levels, Filled = filled };
        axes.Plots.Add(plot);
        return plot;
    }
}
