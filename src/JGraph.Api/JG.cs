using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Data;
using JGraph.Data.Import;
using JGraph.Objects;
using JGraph.Objects.Annotations;
using JGraph.Objects.Engineering;

namespace JGraph.Api;

/// <summary>
/// The MATLAB-like functional API. Static calls operate on an implicit "current figure" and "current
/// axes", mirroring MATLAB's <c>plot</c>/<c>title</c>/<c>grid</c> workflow. Every call manipulates the
/// same <see cref="FigureModel"/> object model the object-oriented API uses, so the two styles are
/// fully interchangeable. This type is intended for single-threaded (UI-thread) use.
/// </summary>
public static class JG
{
    private static readonly Dictionary<int, FigureModel> Figures = new();
    private static FigureModel? _currentFigure;
    private static int _currentNumber;
    private static AxesModel? _currentAxes;

    /// <summary>Raised when <see cref="Show"/> is called, so a host can open a window for the figure.</summary>
    public static event EventHandler<FigureModel>? FigureShown;

    /// <summary>Whether new plots accumulate (hold on) or replace existing content (hold off, default).</summary>
    public static bool IsHolding { get; private set; }

    /// <summary>The current figure, creating figure 1 if none exists yet.</summary>
    public static FigureModel CurrentFigure => _currentFigure ?? Figure(1);

    /// <summary>The current figure's number (1-based, MATLAB-style), creating figure 1 if none exists.</summary>
    public static int CurrentFigureNumber
    {
        get
        {
            _ = CurrentFigure;
            return _currentNumber;
        }
    }

    /// <summary>Creates a new figure under the next unused number, makes it current, and returns it.</summary>
    public static FigureModel Figure()
    {
        int number = 1;
        while (Figures.ContainsKey(number))
        {
            number++;
        }

        return Figure(number);
    }

    /// <summary>
    /// Selects figure <paramref name="number"/> (creating it if needed, MATLAB <c>figure(n)</c>),
    /// makes it current, and returns it. Numbers are 1-based.
    /// </summary>
    public static FigureModel Figure(int number)
    {
        if (number < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(number), "Figure numbers are 1-based.");
        }

        if (!Figures.TryGetValue(number, out FigureModel? figure))
        {
            figure = CreateFigure();
            Figures[number] = figure;
        }

        _currentFigure = figure;
        _currentNumber = number;
        _currentAxes = figure.Axes.Count > 0 ? figure.Axes[^1] : null;
        return figure;
    }

    /// <summary>Gets the figure registered under <paramref name="number"/>, if any.</summary>
    public static bool TryGetFigure(int number, out FigureModel figure)
    {
        if (Figures.TryGetValue(number, out FigureModel? found))
        {
            figure = found;
            return true;
        }

        figure = null!;
        return false;
    }

    /// <summary>
    /// Registers an externally created figure (e.g. one loaded from a <c>.graph</c> file) under the
    /// next unused number, makes it current, and returns that number.
    /// </summary>
    public static int RegisterFigure(FigureModel figure)
    {
        ArgumentNullException.ThrowIfNull(figure);
        int number = GetFigureNumber(figure);
        if (number == 0)
        {
            number = 1;
            while (Figures.ContainsKey(number))
            {
                number++;
            }

            Figures[number] = figure;
        }

        _currentFigure = figure;
        _currentNumber = number;
        _currentAxes = figure.Axes.Count > 0 ? figure.Axes[^1] : null;
        return number;
    }

    /// <summary>The number a figure is registered under, or 0 when it is not registered.</summary>
    public static int GetFigureNumber(FigureModel figure)
    {
        foreach ((int number, FigureModel candidate) in Figures)
        {
            if (ReferenceEquals(candidate, figure))
            {
                return number;
            }
        }

        return 0;
    }

    /// <summary>Returns the current figure (MATLAB <c>gcf</c>).</summary>
    public static FigureModel Gcf() => CurrentFigure;

    /// <summary>Returns the current axes, creating a figure and axes if necessary (MATLAB <c>gca</c>).</summary>
    public static AxesModel Gca() => _currentAxes ??= CurrentFigure.Axes.Count > 0
        ? CurrentFigure.Axes[^1]
        : CurrentFigure.AddAxes();

    /// <summary>Plots a line, applying an optional MATLAB line-spec such as <c>"r--o"</c>.</summary>
    public static LinePlot Plot(double[] xs, double[] ys, string? lineSpec = null)
    {
        AxesModel axes = PrepareAxes();
        var plot = axes.AddLine(xs, ys);
        ApplyLineSpec(plot, LineSpec.Parse(lineSpec));
        return plot;
    }

    /// <summary>Plots a line for Y values against implicit X indices.</summary>
    public static LinePlot Plot(double[] ys, string? lineSpec = null)
    {
        AxesModel axes = PrepareAxes();
        var plot = axes.AddLine(ys);
        ApplyLineSpec(plot, LineSpec.Parse(lineSpec));
        return plot;
    }

    /// <summary>Plots a scatter series.</summary>
    public static ScatterPlot Scatter(double[] xs, double[] ys)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddScatter(xs, ys);
    }

    /// <summary>Plots a bar series.</summary>
    public static BarPlot Bar(double[] positions, double[] values)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddBar(positions, values);
    }

    /// <summary>Plots a bar series with a category X axis.</summary>
    public static BarPlot Bar(string[] categories, double[] values)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddBar(categories, values);
    }

    /// <summary>Plots a stem series (MATLAB <c>stem</c>).</summary>
    public static StemPlot Stem(double[] xs, double[] ys)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddStem(xs, ys);
    }

    /// <summary>Plots a stem series for Y values against implicit X indices.</summary>
    public static StemPlot Stem(double[] ys)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddStem(ys);
    }

    /// <summary>Plots a histogram over raw sample values (MATLAB <c>histogram</c>).</summary>
    public static HistogramPlot Histogram(double[] values, int binCount = 10)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddHistogram(values, binCount);
    }

    /// <summary>Plots samples with symmetric vertical error bars (MATLAB <c>errorbar</c>).</summary>
    public static ErrorBarPlot ErrorBar(double[] xs, double[] ys, double[] error)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddErrorBar(xs, ys, error);
    }

    /// <summary>Displays a scalar field as a colormapped image/heatmap (MATLAB <c>imagesc</c>).</summary>
    public static ImagePlot Image(double[,] values)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddImage(values);
    }

    /// <summary>Reads a table from a file (MATLAB <c>readtable</c>); <c>.xlsx</c> uses the workbook reader.</summary>
    public static Table ReadTable(string path, ImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
            ? Table.ReadXlsx(path, options)
            : Table.ReadCsv(path, options);
    }

    /// <summary>Reads a table from a delimited-text (CSV/TSV) file.</summary>
    public static Table ReadCsv(string path, ImportOptions? options = null) => Table.ReadCsv(path, options);

    /// <summary>Plots a table column against another, applying an optional MATLAB line-spec.</summary>
    public static LinePlot Plot(Table table, string xColumn, string yColumn, string? lineSpec = null)
    {
        AxesModel axes = PrepareAxes();
        LinePlot plot = axes.AddLine(table, xColumn, yColumn);
        ApplyLineSpec(plot, LineSpec.Parse(lineSpec));
        return plot;
    }

    /// <summary>Plots one table column as a scatter series against another.</summary>
    public static ScatterPlot Scatter(Table table, string xColumn, string yColumn)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddScatter(table, xColumn, yColumn);
    }

    /// <summary>Plots a table value column as bars labeled by a category column.</summary>
    public static BarPlot Bar(Table table, string categoryColumn, string valueColumn)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddBar(table, categoryColumn, valueColumn);
    }

    /// <summary>Plots a histogram over the values of a table column.</summary>
    public static HistogramPlot Histogram(Table table, string valueColumn, int binCount = 10)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddHistogram(table, valueColumn, binCount);
    }

    /// <summary>Plots a table column with symmetric vertical error bars from an error column.</summary>
    public static ErrorBarPlot ErrorBar(Table table, string xColumn, string yColumn, string errorColumn)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddErrorBar(table, xColumn, yColumn, errorColumn);
    }

    /// <summary>Plots angle/radius data on a polar chart (MATLAB <c>polarplot</c>); θ is in radians.</summary>
    public static LinePlot Polar(double[] thetaRadians, double[] r)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddPolar(thetaRadians, r);
    }

    /// <summary>Plots a normalized-impedance locus on a Smith chart (z = real + j·imag).</summary>
    public static LinePlot Smith(double[] impedanceReal, double[] impedanceImag)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddSmith(impedanceReal, impedanceImag);
    }

    /// <summary>Plots an eye diagram of a signal sampled at <paramref name="samplesPerSymbol"/> samples per symbol.</summary>
    public static EyeDiagramPlot EyeDiagram(double[] signal, int samplesPerSymbol, int symbolsPerTrace = 2)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddEyeDiagram(signal, samplesPerSymbol, symbolsPerTrace);
    }

    /// <summary>Displays the spectrogram (STFT magnitude, dB) of a real signal (MATLAB <c>spectrogram</c>).</summary>
    public static ImagePlot Spectrogram(double[] signal, double sampleRate, int windowSize = 256, int overlap = 128)
    {
        AxesModel axes = PrepareAxes();
        ImagePlot image = axes.AddSpectrogram(signal, sampleRate, windowSize, overlap);
        axes.PrimaryXAxis.Label = "Time (s)";
        axes.PrimaryYAxis.Label = "Frequency (Hz)";
        return image;
    }

    /// <summary>
    /// Plots the Nyquist diagram of a transfer function num(s)/den(s) (descending powers of s), with
    /// the critical (−1, 0) point marked (MATLAB <c>nyquist</c>).
    /// </summary>
    public static LinePlot Nyquist(double[] numerator, double[] denominator, double omegaMin = 0.01, double omegaMax = 100, int points = 400)
    {
        AxesModel axes = PrepareAxes();
        LinePlot locus = axes.AddNyquist(numerator, denominator, omegaMin, omegaMax, points);
        axes.Title = "Nyquist";
        axes.PrimaryXAxis.Label = "Real";
        axes.PrimaryYAxis.Label = "Imaginary";
        axes.Grid.ShowMajor = true;
        return locus;
    }

    /// <summary>
    /// Plots the Bode diagram (magnitude and phase versus log frequency) of a transfer function
    /// num(s)/den(s) on the current figure, returning both panels (MATLAB <c>bode</c>).
    /// </summary>
    public static BodeChart Bode(double[] numerator, double[] denominator, double omegaMin = 0.1, double omegaMax = 1000, int points = 300)
    {
        FigureModel figure = CurrentFigure;
        figure.Axes.Clear();
        _currentAxes = null;
        BodeChart chart = figure.AddBode(numerator, denominator, omegaMin, omegaMax, points);
        _currentAxes = chart.Magnitude;
        return chart;
    }

    /// <summary>
    /// Selects (creating if needed) the axes at cell <paramref name="index"/> of a
    /// <paramref name="rows"/> × <paramref name="cols"/> grid and makes it current (MATLAB <c>subplot</c>).
    /// </summary>
    public static AxesModel Subplot(int rows, int cols, int index)
    {
        FigureModel figure = CurrentFigure;
        Rect2D bounds = FigureModel.SubplotBounds(rows, cols, index, index);
        foreach (AxesModel existing in figure.Axes)
        {
            if (BoundsClose(existing.NormalizedBounds, bounds))
            {
                _currentAxes = existing;
                return existing;
            }
        }

        AxesModel axes = figure.AddSubplot(rows, cols, index);
        _currentAxes = axes;
        return axes;
    }

    /// <summary>Links the ranges of several axes so they pan/zoom together (MATLAB <c>linkaxes</c>).</summary>
    public static AxisLinkGroup LinkAxes(AxisLinkMode mode, params AxesModel[] axes) =>
        AxisLinkGroup.Link(mode, axes);

    /// <summary>Links both the X and Y ranges of several axes so they pan/zoom together.</summary>
    public static AxisLinkGroup LinkAxes(params AxesModel[] axes) =>
        AxisLinkGroup.Link(AxisLinkMode.Both, axes);

    /// <summary>Plots a line with a logarithmic Y axis.</summary>
    public static LinePlot SemilogY(double[] xs, double[] ys, string? lineSpec = null)
    {
        LinePlot plot = Plot(xs, ys, lineSpec);
        Gca().PrimaryYAxis.Scale = AxisScaleType.Logarithmic;
        return plot;
    }

    /// <summary>Plots a line with a logarithmic X axis.</summary>
    public static LinePlot SemilogX(double[] xs, double[] ys, string? lineSpec = null)
    {
        LinePlot plot = Plot(xs, ys, lineSpec);
        Gca().PrimaryXAxis.Scale = AxisScaleType.Logarithmic;
        return plot;
    }

    /// <summary>Plots a line with logarithmic X and Y axes.</summary>
    public static LinePlot LogLog(double[] xs, double[] ys, string? lineSpec = null)
    {
        LinePlot plot = Plot(xs, ys, lineSpec);
        Gca().PrimaryXAxis.Scale = AxisScaleType.Logarithmic;
        Gca().PrimaryYAxis.Scale = AxisScaleType.Logarithmic;
        return plot;
    }

    /// <summary>Sets the current axes title.</summary>
    public static void Title(string text) => Gca().Title = text;

    /// <summary>Sets the current X axis label.</summary>
    public static void XLabel(string text) => Gca().PrimaryXAxis.Label = text;

    /// <summary>Sets the current Y axis label.</summary>
    public static void YLabel(string text) => Gca().PrimaryYAxis.Label = text;

    /// <summary>Turns the current axes grid on or off.</summary>
    public static void Grid(bool on = true)
    {
        Gca().Grid.ShowMajor = on;
        Gca().Grid.Visible = true;
    }

    /// <summary>Enables the legend, optionally assigning display names to the plots in order.</summary>
    public static void Legend(params string[] names)
    {
        AxesModel axes = Gca();
        for (int i = 0; i < names.Length && i < axes.Plots.Count; i++)
        {
            if (axes.Plots[i] is PlotObject plot)
            {
                plot.DisplayName = names[i];
            }
        }

        axes.Legend.Visible = true;
    }

    /// <summary>Adds a text label at the given data point on the current axes (MATLAB <c>text</c>).</summary>
    public static TextAnnotation Text(double x, double y, string text) => Gca().AddText(x, y, text);

    /// <summary>Adds an arrow between two data points on the current axes (MATLAB <c>annotation('arrow')</c>).</summary>
    public static ArrowAnnotation Arrow(double x1, double y1, double x2, double y2) =>
        Gca().AddArrow(x1, y1, x2, y2);

    /// <summary>Adds a plain line annotation between two data points on the current axes.</summary>
    public static ArrowAnnotation Line(double x1, double y1, double x2, double y2) =>
        Gca().AddLineAnnotation(x1, y1, x2, y2);

    /// <summary>Sets whether subsequent plots accumulate (MATLAB <c>hold on/off</c>).</summary>
    public static void Hold(bool on = true) => IsHolding = on;

    /// <summary>Sets the current X axis limits and disables auto-scaling on it.</summary>
    public static void XLim(double min, double max)
    {
        AxisModel axis = Gca().PrimaryXAxis;
        axis.AutoScale = false;
        axis.Range = new DataRange(min, max);
    }

    /// <summary>Sets the current Y axis limits and disables auto-scaling on it.</summary>
    public static void YLim(double min, double max)
    {
        AxisModel axis = Gca().PrimaryYAxis;
        axis.AutoScale = false;
        axis.Range = new DataRange(min, max);
    }

    /// <summary>Clears the current figure's axes and annotations (MATLAB <c>clf</c>).</summary>
    public static void Clf()
    {
        _currentFigure?.Axes.Clear();
        _currentFigure?.Annotations.Clear();
        _currentAxes = null;
    }

    /// <summary>Signals a host to display the current figure.</summary>
    public static void Show() => FigureShown?.Invoke(null, CurrentFigure);

    /// <summary>Resets the figure registry and the current-figure/current-axes/hold state (run start, tests).</summary>
    public static void Reset()
    {
        Figures.Clear();
        _currentFigure = null;
        _currentNumber = 0;
        _currentAxes = null;
        IsHolding = false;
    }

    private static FigureModel CreateFigure()
    {
        var figure = new FigureModel();
        _currentAxes = null;
        return figure;
    }

    /// <summary>Whether two normalized-bounds rectangles refer to the same subplot cell.</summary>
    private static bool BoundsClose(Rect2D a, Rect2D b)
    {
        const double tol = 1e-6;
        return System.Math.Abs(a.X - b.X) < tol
            && System.Math.Abs(a.Y - b.Y) < tol
            && System.Math.Abs(a.Width - b.Width) < tol
            && System.Math.Abs(a.Height - b.Height) < tol;
    }

    /// <summary>Returns the current axes, clearing it first when not holding.</summary>
    private static AxesModel PrepareAxes()
    {
        AxesModel axes = Gca();
        if (!IsHolding)
        {
            axes.Plots.Clear();
            axes.Annotations.Clear();
        }

        return axes;
    }

    private static void ApplyLineSpec(LinePlot plot, LineSpec spec)
    {
        if (spec.Color is { } color)
        {
            plot.Color = color;
        }

        if (spec.LineSpecified && spec.Dash is { } dash)
        {
            plot.DashStyle = dash;
        }
        else if (spec.MarkerSpecified && !spec.LineSpecified)
        {
            // Markers only, no connecting line (MATLAB behavior for e.g. "o").
            plot.DashStyle = Core.Drawing.DashStyle.None;
        }

        if (spec.Marker is { } marker)
        {
            plot.Marker = marker;
        }
    }
}
