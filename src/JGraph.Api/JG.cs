using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Data;
using JGraph.Data.Import;
using JGraph.Objects;
using JGraph.Objects.Annotations;
using JGraph.Objects.Engineering;
using JGraph.Rendering;

namespace JGraph.Api;

/// <summary>
/// The MATLAB-like functional API. Static calls operate on an implicit "current figure" and "current
/// axes", mirroring MATLAB's <c>plot</c>/<c>title</c>/<c>grid</c> workflow. Every call manipulates the
/// same <see cref="FigureModel"/> object model the object-oriented API uses, so the two styles are
/// fully interchangeable. This type is intended for single-threaded (UI-thread) use.
/// </summary>
public static class JG
{
    // The registry is the one part of this type two threads reach: a script runs on the engine's
    // thread while the UI thread retires a figure whose window the user just closed. Everything
    // else here stays single-threaded (UI-thread) by design.
    private static readonly object Registry = new();
    private static readonly Dictionary<int, FigureModel> Figures = new();
    private static readonly Dictionary<int, long> TouchStamps = new();
    private static FigureModel? _currentFigure;
    private static int _currentNumber;
    private static AxesModel? _currentAxes;
    private static long _touchCounter;

    /// <summary>Raised when <see cref="Show"/> is called, so a host can open a window for the figure.</summary>
    public static event EventHandler<FigureModel>? FigureShown;

    /// <summary>
    /// Whether new plots accumulate (hold on) or replace existing content (hold off, default).
    /// Hold lives on the current axes, as in MATLAB, so it ends when those axes do.
    /// </summary>
    public static bool IsHolding => _currentAxes?.Hold ?? false;

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
        lock (Registry)
        {
            int number = 1;
            while (Figures.ContainsKey(number))
            {
                number++;
            }

            return Figure(number);
        }
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

        lock (Registry)
        {
            if (!Figures.TryGetValue(number, out FigureModel? figure))
            {
                figure = CreateFigure();
                Figures[number] = figure;
            }

            _currentFigure = figure;
            _currentNumber = number;
            _currentAxes = figure.Axes.Count > 0 ? figure.Axes[^1] : null;
            Touch(number);
            return figure;
        }
    }

    /// <summary>Gets the figure registered under <paramref name="number"/>, if any.</summary>
    public static bool TryGetFigure(int number, out FigureModel figure)
    {
        lock (Registry)
        {
            if (Figures.TryGetValue(number, out FigureModel? found))
            {
                figure = found;
                return true;
            }
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
        lock (Registry)
        {
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
            Touch(number);
            return number;
        }
    }

    /// <summary>
    /// A marker for "now" in the figure-touch sequence. Pair it with <see cref="FiguresTouchedSince"/>
    /// to learn which figures a stretch of script work selected, created, or drew into.
    /// </summary>
    public static long TouchStamp => _touchCounter;

    /// <summary>
    /// The registered figures touched after <paramref name="stamp"/>, ascending by number. A script run
    /// uses this to display exactly the figures it worked on, leaving figures opened elsewhere alone.
    /// </summary>
    public static IReadOnlyList<int> FiguresTouchedSince(long stamp)
    {
        var numbers = new List<int>();
        lock (Registry)
        {
            foreach ((int number, long touched) in TouchStamps)
            {
                if (touched > stamp && Figures.ContainsKey(number))
                {
                    numbers.Add(number);
                }
            }
        }

        numbers.Sort();
        return numbers;
    }

    /// <summary>Records that figure <paramref name="number"/> was selected, created, or drawn into.</summary>
    private static void Touch(int number)
    {
        if (number >= 1)
        {
            TouchStamps[number] = ++_touchCounter;
        }
    }

    /// <summary>The numbers of every registered figure, ascending.</summary>
    public static IReadOnlyList<int> FigureNumbers
    {
        get
        {
            List<int> numbers;
            lock (Registry)
            {
                numbers = new List<int>(Figures.Keys);
            }

            numbers.Sort();
            return numbers;
        }
    }

    /// <summary>The number a figure is registered under, or 0 when it is not registered.</summary>
    public static int GetFigureNumber(FigureModel figure)
    {
        lock (Registry)
        {
            foreach ((int number, FigureModel candidate) in Figures)
            {
                if (ReferenceEquals(candidate, figure))
                {
                    return number;
                }
            }
        }

        return 0;
    }

    /// <summary>Returns the current figure (MATLAB <c>gcf</c>).</summary>
    public static FigureModel Gcf() => CurrentFigure;

    /// <summary>The current axes, or null when no figure has been drawn into yet.</summary>
    public static AxesModel? CurrentAxesOrNull => _currentAxes;

    /// <summary>
    /// Makes <paramref name="axes"/> current, along with the figure that owns it. This is how a verb
    /// aimed at a named axes (<c>plot(ax, …)</c>) reaches it without the caller having to select it
    /// first — and, since the caller puts the previous axes back, without moving <c>gca</c>.
    /// </summary>
    public static void MakeCurrent(AxesModel axes)
    {
        ArgumentNullException.ThrowIfNull(axes);
        if (axes.Parent is FigureModel figure)
        {
            _currentFigure = figure;
            _currentNumber = GetFigureNumber(figure);
        }

        _currentAxes = axes;
        Touch(_currentNumber);
    }

    /// <summary>Returns the current axes, creating a figure and axes if necessary (MATLAB <c>gca</c>).</summary>
    public static AxesModel Gca()
    {
        AxesModel axes = _currentAxes ??= CurrentFigure.Axes.Count > 0
            ? CurrentFigure.Axes[^1]
            : CurrentFigure.AddAxes();

        // Every drawing verb funnels through here, so this is where a figure earns its "touched
        // this run" stamp — the run then knows which windows to display when it finishes.
        Touch(_currentNumber);
        return axes;
    }

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

    /// <summary>
    /// Plots bubbles whose sizes come from a third variable (MATLAB <c>bubblechart</c>). The sizes are
    /// data values read against the axes' bubble scale, not marker areas — see <c>BubbleScale</c>.
    /// </summary>
    public static ScatterPlot BubbleChart(double[] xs, double[] ys, double[] sizes)
    {
        ArgumentNullException.ThrowIfNull(sizes);

        AxesModel axes = PrepareAxes();
        return axes.AddBubbleChart(xs, ys, sizes);
    }

    /// <summary>Shows (default) or hides the current axes' bubble legend (MATLAB <c>bubblelegend</c>).</summary>
    public static BubbleLegendModel BubbleLegend(bool on = true)
    {
        BubbleLegendModel legend = Gca().BubbleLegend;
        legend.Visible = on;
        return legend;
    }

    /// <summary>Plots a filled area band (MATLAB <c>area</c>).</summary>
    public static AreaPlot Area(double[] xs, double[] ys)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddArea(xs, ys);
    }

    /// <summary>Plots one area band per column, each stacked on the ones before it.</summary>
    public static IReadOnlyList<AreaPlot> Area(double[] xs, IReadOnlyList<double[]> columns)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddStackedArea(xs, columns);
    }

    /// <summary>Plots a pie chart on a round, frameless axes (MATLAB <c>pie</c>).</summary>
    public static PiePlot Pie(double[] values)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddPie(values);
    }

    /// <summary>
    /// Plots a heatmap on category rulers naming its columns and rows (MATLAB <c>heatmap</c>).
    /// </summary>
    public static HeatmapPlot Heatmap(double[,] colorData, string[]? xLabels = null, string[]? yLabels = null)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddHeatmap(colorData, xLabels, yLabels);
    }

    /// <summary>
    /// Plots the readings as a grid of bins coloured by how many fell in each (MATLAB
    /// <c>binscatter</c>).
    /// </summary>
    public static BinScatterPlot BinScatter(double[] x, double[] y)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddBinScatter(x, y);
    }

    /// <summary>
    /// Plots a box and whiskers per group of observations (MATLAB <c>boxchart</c>).
    /// </summary>
    public static BoxChartPlot BoxChart(double[]? xData, double[] yData)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddBoxChart(xData, yData);
    }

    /// <summary>Plots a bar series.</summary>
    public static BarPlot Bar(double[] positions, double[] values)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddBar(positions, values);
    }

    /// <summary>Plots one bar series per column, grouped side by side or stacked.</summary>
    public static IReadOnlyList<BarPlot> Bar(
        double[] positions, IReadOnlyList<double[]> columns, bool stacked)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddBar(positions, columns, stacked);
    }

    /// <summary>Plots a stairstep line (MATLAB <c>stairs</c>).</summary>
    public static LinePlot Stairs(double[] xs, double[] ys)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddStairs(xs, ys);
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

    /// <summary>
    /// Plots samples whose error reaches a different distance below and above each one — MATLAB's
    /// <c>errorbar(x, y, neg, pos)</c>. The model has carried the two arrays separately since M6;
    /// until M70 no verb handed it two.
    /// </summary>
    public static ErrorBarPlot ErrorBar(double[] xs, double[] ys, double[] errorNeg, double[] errorPos)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddErrorBar(xs, ys, errorNeg, errorPos);
    }

    /// <summary>Displays a scalar field as a colormapped image/heatmap (MATLAB <c>imagesc</c>).</summary>
    public static ImagePlot Image(double[,] values)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddImage(values);
    }

    /// <summary>Displays a true-colour image from row-major 0xAARRGGBB pixels (MATLAB <c>imshow</c> of RGB).</summary>
    public static RgbImagePlot RgbImage(uint[] pixelsArgb, int width, int height)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddRgbImage(pixelsArgb, width, height);
    }

    /// <summary>Displays a scalar field as a colormapped image over explicit X/Y extents (MATLAB <c>pcolor</c>).</summary>
    public static ImagePlot Pcolor(double[] x, double[] y, double[,] values)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(values);
        AxesModel axes = PrepareAxes();
        ImagePlot image = axes.AddImage(
            values,
            VectorExtent(x, values.GetLength(1)),
            VectorExtent(y, values.GetLength(0)));
        image.RowZeroAtTop = false; // row 0 is y[0] (the low end), math convention
        return image;
    }

    /// <summary>Plots a colormap-filled 3D surface (MATLAB <c>surf</c>) and switches the axes to 3D.</summary>
    public static SurfacePlot Surf(double[] x, double[] y, double[,] z)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddSurface(x, y, z, SurfaceStyle.FilledWithWireframe);
    }

    /// <summary>Plots a surface of <c>z[row, col]</c> with unit-spaced X/Y (MATLAB <c>surf(Z)</c>).</summary>
    public static SurfacePlot Surf(double[,] z) => Surf(Ramp(z.GetLength(1)), Ramp(z.GetLength(0)), z);

    /// <summary>
    /// Plots a parametric surface, with a position per vertex rather than per row and column — the
    /// form <c>sphere</c>, <c>cylinder</c> and <c>ellipsoid</c> produce.
    /// </summary>
    public static SurfacePlot Surf(double[,] x, double[,] y, double[,] z)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddSurface(x, y, z, SurfaceStyle.FilledWithWireframe);
    }

    /// <summary>Plots a wireframe 3D surface (MATLAB <c>mesh</c>) and switches the axes to 3D.</summary>
    public static SurfacePlot Mesh(double[] x, double[] y, double[,] z)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddSurface(x, y, z, SurfaceStyle.Wireframe);
    }

    /// <summary>Plots a parametric wireframe surface (MATLAB <c>mesh</c> with full X/Y matrices).</summary>
    public static SurfacePlot Mesh(double[,] x, double[,] y, double[,] z)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddSurface(x, y, z, SurfaceStyle.Wireframe);
    }

    /// <summary>Plots a wireframe surface with unit-spaced X/Y (MATLAB <c>mesh(Z)</c>).</summary>
    public static SurfacePlot Mesh(double[,] z) => Mesh(Ramp(z.GetLength(1)), Ramp(z.GetLength(0)), z);

    /// <summary>Plots a wireframe surface with contour lines on the floor (MATLAB <c>meshc</c>).</summary>
    public static SurfacePlot MeshC(double[] x, double[] y, double[,] z)
    {
        SurfacePlot surface = Mesh(x, y, z);
        surface.ShowContourBelow = true;
        return surface;
    }

    /// <summary>
    /// The parametric form of <see cref="MeshC(double[], double[], double[,])"/>. The floor contours
    /// are recorded but not drawn: tracing them needs a height field over a rectilinear grid, which
    /// a parametric surface is not.
    /// </summary>
    public static SurfacePlot MeshC(double[,] x, double[,] y, double[,] z)
    {
        SurfacePlot surface = Mesh(x, y, z);
        surface.ShowContourBelow = true;
        return surface;
    }

    /// <summary>Plots a polyline through points in space (MATLAB <c>plot3</c>) and switches to 3D.</summary>
    public static Line3DPlot Plot3(double[] x, double[] y, double[] z, string? lineSpec = null)
    {
        AxesModel axes = PrepareAxes();
        Line3DPlot plot = axes.AddLine3D(x, y, z);
        ApplyLineSpec(plot, LineSpec.Parse(lineSpec));
        return plot;
    }

    /// <summary>Plots markers at points in space (MATLAB <c>scatter3</c>) and switches to 3D.</summary>
    public static Scatter3DPlot Scatter3(double[] x, double[] y, double[] z)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddScatter3D(x, y, z);
    }

    /// <summary>Plots a stem per sample in space (MATLAB <c>stem3</c>) and switches to 3D.</summary>
    public static Stem3DPlot Stem3(double[] x, double[] y, double[] z)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddStem3D(x, y, z);
    }

    /// <summary>Plots a matrix as a field of bars on the floor (MATLAB <c>bar3</c>).</summary>
    public static Bar3DPlot Bar3(double[,] z, double[]? rowPositions = null)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddBar3D(z, rowPositions);
    }

    /// <summary>Plots a raised pie chart on round, frameless axes (MATLAB <c>pie3</c>).</summary>
    public static Pie3DPlot Pie3(double[] values)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddPie3D(values);
    }

    /// <summary>
    /// Fills one polygon in the current 2D axes (MATLAB <c>fill</c>). The polygon closes itself, so the
    /// caller need not repeat the first point.
    /// </summary>
    public static PatchPlot Fill(double[] x, double[] y, Core.Drawing.Color color)
    {
        ArgumentNullException.ThrowIfNull(x);
        AxesModel axes = PrepareAxes();
        PatchPlot patch = axes.AddPatch(x, y, new double[x.Length]);
        patch.FaceColor = color;
        return patch;
    }

    /// <summary>Fills one polygon in space (MATLAB <c>fill3</c>) and switches the axes to 3D.</summary>
    public static PatchPlot Fill3(double[] x, double[] y, double[] z, Core.Drawing.Color color)
    {
        AxesModel axes = PrepareAxes();
        PatchPlot patch = axes.AddPatch(x, y, z);
        patch.FaceColor = color;
        axes.Is3D = true;
        return patch;
    }

    /// <summary>
    /// Adds a patch over an explicit vertex list and face table (MATLAB
    /// <c>patch('Faces', F, 'Vertices', V)</c>). The axes mode is left alone; set
    /// <see cref="AxesModel.Is3D"/> for a 3D patch.
    /// </summary>
    public static PatchPlot Patch(double[] x, double[] y, double[] z, int[][] faces)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddPatch(x, y, z, faces);
    }

    /// <summary>Plots a filled surface with floor contours over unit-spaced X/Y (MATLAB <c>surfc(Z)</c>).</summary>
    public static SurfacePlot SurfC(double[,] z) => SurfC(Ramp(z.GetLength(1)), Ramp(z.GetLength(0)), z);

    /// <summary>Plots a curtained wireframe over unit-spaced X/Y (MATLAB <c>meshz(Z)</c>).</summary>
    public static SurfacePlot MeshZ(double[,] z) => MeshZ(Ramp(z.GetLength(1)), Ramp(z.GetLength(0)), z);

    /// <summary>Plots waterfall curves over unit-spaced X/Y (MATLAB <c>waterfall(Z)</c>).</summary>
    public static PatchPlot Waterfall(double[,] z) => Waterfall(Ramp(z.GetLength(1)), Ramp(z.GetLength(0)), z);

    /// <summary>Plots a filled surface with contour lines on the floor (MATLAB <c>surfc</c>).</summary>
    public static SurfacePlot SurfC(double[] x, double[] y, double[,] z)
    {
        SurfacePlot surface = Surf(x, y, z);
        surface.ShowContourBelow = true;
        return surface;
    }

    /// <summary>
    /// The parametric form of <see cref="SurfC(double[], double[], double[,])"/>. As with
    /// <c>meshc</c>, the floor contours are recorded but not drawn — tracing them needs a height
    /// field over a rectilinear grid.
    /// </summary>
    public static SurfacePlot SurfC(double[,] x, double[,] y, double[,] z)
    {
        SurfacePlot surface = Surf(x, y, z);
        surface.ShowContourBelow = true;
        return surface;
    }

    /// <summary>
    /// Plots a wireframe surface with a curtain dropped from its perimeter to the floor (MATLAB
    /// <c>meshz</c>).
    /// </summary>
    /// <remarks>
    /// The curtain is not a second object: the grid is padded with one ring of vertices that repeat
    /// the border positions at the base height, so the extra cells are vertical walls in the same
    /// mesh. That is how MATLAB builds it too, and it means the curtain rotates, colors, and paints
    /// in order with everything else for free.
    /// </remarks>
    public static SurfacePlot MeshZ(double[] x, double[] y, double[,] z)
    {
        (double[] px, double[] py, double[,] pz) = WithCurtain(x, y, z);
        return Mesh(px, py, pz);
    }

    /// <summary>The parametric form of <see cref="MeshZ(double[], double[], double[,])"/>.</summary>
    public static SurfacePlot MeshZ(double[,] x, double[,] y, double[,] z)
    {
        double baseHeight = BaseHeightOf(z);
        return Mesh(
            RingedWith(x, (r, c) => x[r, c]),
            RingedWith(y, (r, c) => y[r, c]),
            RingedWith(z, (_, _) => baseHeight));
    }

    /// <summary>
    /// Plots each row of <paramref name="z"/> as a curve in space with the area beneath it filled
    /// down to a common base (MATLAB <c>waterfall</c>). The fill is what hides the rows behind.
    /// </summary>
    /// <remarks>
    /// A row containing a non-finite height is dropped whole rather than broken in two: a patch face
    /// is one polygon, and the closed area under a broken curve is not well defined.
    /// </remarks>
    public static PatchPlot Waterfall(double[] x, double[] y, double[,] z)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(z);
        int rows = z.GetLength(0);
        int cols = z.GetLength(1);
        if (rows < 1 || cols < 2)
        {
            throw new ArgumentException("A waterfall needs at least one row of at least two heights.", nameof(z));
        }

        double baseHeight = BaseHeightOf(z);

        // Each row becomes one closed polygon: down to the base at the left, along the curve, and
        // back down to the base at the right.
        int perRow = cols + 2;
        var vx = new double[rows * perRow];
        var vy = new double[rows * perRow];
        var vz = new double[rows * perRow];
        var faces = new int[rows][];
        var levels = new double[rows];
        for (int r = 0; r < rows; r++)
        {
            int at = r * perRow;
            var face = new int[perRow];
            double sum = 0;
            int finite = 0;
            for (int i = 0; i < perRow; i++)
            {
                int c = System.Math.Clamp(i - 1, 0, cols - 1);
                vx[at + i] = x[c];
                vy[at + i] = y[r];
                vz[at + i] = i == 0 || i == perRow - 1 ? baseHeight : z[r, c];
                face[i] = at + i;
                if (i > 0 && i < perRow - 1 && double.IsFinite(z[r, c]))
                {
                    sum += z[r, c];
                    finite++;
                }
            }

            faces[r] = face;
            levels[r] = finite > 0 ? sum / finite : baseHeight;
        }

        AxesModel axes = PrepareAxes();
        PatchPlot patch = axes.AddPatch(vx, vy, vz, faces);
        patch.ColorData = levels;
        patch.Name = "Waterfall";
        axes.Is3D = true;
        return patch;
    }

    /// <summary>
    /// Plots each column of <paramref name="z"/> as a flat strip standing in space (MATLAB
    /// <c>ribbon</c>): strip <c>j</c> is centred at <c>x = j + 1</c> and <paramref name="width"/>
    /// wide, runs along <paramref name="y"/>, and rises to that column's values.
    /// </summary>
    /// <remarks>
    /// Every strip is its own surface, as it is in MATLAB, but they share one color range so that a
    /// height means the same thing across the whole plot rather than being rescaled per strip.
    /// </remarks>
    public static IReadOnlyList<SurfacePlot> Ribbon(double[] y, double[,] z, double width = 0.75)
    {
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(z);
        int rows = z.GetLength(0);
        int cols = z.GetLength(1);
        if (y.Length != rows)
        {
            throw new ArgumentException(
                $"ribbon needs one y value per row of z, but got {y.Length} for {rows} rows.", nameof(y));
        }

        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        foreach (double v in z)
        {
            if (double.IsFinite(v))
            {
                min = System.Math.Min(min, v);
                max = System.Math.Max(max, v);
            }
        }

        AxesModel axes = PrepareAxes();
        var strips = new SurfacePlot[cols];
        for (int c = 0; c < cols; c++)
        {
            var gx = new double[rows, 2];
            var gy = new double[rows, 2];
            var gz = new double[rows, 2];
            for (int r = 0; r < rows; r++)
            {
                gx[r, 0] = c + 1 - (width / 2);
                gx[r, 1] = c + 1 + (width / 2);
                gy[r, 0] = gy[r, 1] = y[r];
                gz[r, 0] = gz[r, 1] = z[r, c];
            }

            SurfacePlot strip = axes.AddSurface(gx, gy, gz);
            strip.Name = $"Ribbon {c + 1}";
            if (min < max)
            {
                strip.AutoScaleColor = false;
                strip.ColorMin = min;
                strip.ColorMax = max;
            }

            strips[c] = strip;
        }

        axes.Is3D = true;
        return strips;
    }

    /// <summary>
    /// Plots iso-lines of a scalar field at the height of their own level (MATLAB <c>contour3</c>)
    /// and switches the axes to 3D.
    /// </summary>
    public static ContourPlot Contour3(double[] x, double[] y, double[,] z, double[]? levels = null)
    {
        AxesModel axes = PrepareAxes();
        ContourPlot contour = axes.AddContour(x, y, z, levels);
        axes.Is3D = true;
        return contour;
    }

    /// <summary>
    /// Plots a triangulated surface over a vertex list (MATLAB <c>trisurf</c>) and switches to 3D.
    /// Faces are zero-based here; the script layer converts MATLAB's one-based table.
    /// </summary>
    public static PatchPlot TriSurf(int[][] faces, double[] x, double[] y, double[] z, double[]? c = null)
    {
        AxesModel axes = PrepareAxes();
        PatchPlot patch = axes.AddPatch(x, y, z, faces);
        patch.ColorData = c ?? z;
        patch.Name = "Trisurf";
        axes.Is3D = true;
        return patch;
    }

    /// <summary>
    /// The same triangulation drawn as edges only, each triangle outlined in the color its face
    /// would have had (MATLAB <c>trimesh</c>).
    /// </summary>
    public static PatchPlot TriMesh(int[][] faces, double[] x, double[] y, double[] z, double[]? c = null)
    {
        PatchPlot patch = TriSurf(faces, x, y, z, c);
        patch.FaceVisible = false;
        patch.Name = "Trimesh";
        return patch;
    }

    /// <summary>Plots a field of arrows in the plane (MATLAB <c>quiver</c>).</summary>
    public static QuiverPlot Quiver(double[] x, double[] y, double[] u, double[] v)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddQuiver(x, y, u, v);
    }

    /// <summary>Plots a field of arrows in space (MATLAB <c>quiver3</c>) and switches to 3D.</summary>
    public static QuiverPlot Quiver3(double[] x, double[] y, double[] z, double[] u, double[] v, double[] w)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddQuiver3(x, y, z, u, v, w);
    }

    /// <summary>
    /// A grid ringed by one extra row and column that repeat the border positions at the lowest
    /// height, which is exactly the curtain <c>meshz</c> draws.
    /// </summary>
    private static (double[] X, double[] Y, double[,] Z) WithCurtain(double[] x, double[] y, double[,] z)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(z);
        int rows = z.GetLength(0);
        int cols = z.GetLength(1);
        if (rows != y.Length || cols != x.Length)
        {
            throw new ArgumentException(
                $"z must be [{y.Length} rows x {x.Length} cols] to match y and x, but was [{rows} x {cols}].",
                nameof(z));
        }

        double baseHeight = BaseHeightOf(z);
        var px = new double[cols + 2];
        var py = new double[rows + 2];
        for (int c = 0; c < cols + 2; c++)
        {
            px[c] = x[System.Math.Clamp(c - 1, 0, cols - 1)];
        }

        for (int r = 0; r < rows + 2; r++)
        {
            py[r] = y[System.Math.Clamp(r - 1, 0, rows - 1)];
        }

        var pz = new double[rows + 2, cols + 2];
        for (int r = 0; r < rows + 2; r++)
        {
            for (int c = 0; c < cols + 2; c++)
            {
                pz[r, c] = r == 0 || r == rows + 1 || c == 0 || c == cols + 1
                    ? baseHeight
                    : z[r - 1, c - 1];
            }
        }

        return (px, py, pz);
    }

    /// <summary>
    /// A matrix ringed by one extra row and column, the border values repeated outward except where
    /// <paramref name="onRing"/> supplies something else — which is how the height matrix gets its
    /// base while the two coordinate matrices keep their edge positions.
    /// </summary>
    private static double[,] RingedWith(double[,] values, Func<int, int, double> onRing)
    {
        int rows = values.GetLength(0);
        int cols = values.GetLength(1);
        var ringed = new double[rows + 2, cols + 2];
        for (int r = 0; r < rows + 2; r++)
        {
            int sr = System.Math.Clamp(r - 1, 0, rows - 1);
            for (int c = 0; c < cols + 2; c++)
            {
                int sc = System.Math.Clamp(c - 1, 0, cols - 1);
                ringed[r, c] = r == 0 || r == rows + 1 || c == 0 || c == cols + 1
                    ? onRing(sr, sc)
                    : values[sr, sc];
            }
        }

        return ringed;
    }

    /// <summary>The lowest finite height in a grid, or zero when there is none.</summary>
    private static double BaseHeightOf(double[,] z)
    {
        double lowest = double.PositiveInfinity;
        foreach (double v in z)
        {
            if (double.IsFinite(v) && v < lowest)
            {
                lowest = v;
            }
        }

        return double.IsFinite(lowest) ? lowest : 0;
    }

    /// <summary>Plots iso-line contours of a scalar field (MATLAB <c>contour</c>).</summary>
    public static ContourPlot Contour(double[] x, double[] y, double[,] z, double[]? levels = null)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddContour(x, y, z, levels);
    }

    /// <summary>Plots filled contour bands of a scalar field (MATLAB <c>contourf</c>).</summary>
    public static ContourPlot ContourF(double[] x, double[] y, double[,] z, double[]? levels = null)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddContour(x, y, z, levels, filled: true);
    }

    /// <summary>Sets the current Z axis label.</summary>
    public static void ZLabel(string text) => Gca().ZAxis.Label = text;

    /// <summary>Sets the current Z axis limits and disables auto-scaling on it.</summary>
    public static void ZLim(double min, double max)
    {
        AxisModel axis = Gca().ZAxis;
        axis.AutoScale = false;
        axis.Range = new DataRange(min, max);
    }

    /// <summary>Sets the current axes' 3D camera angles in degrees (MATLAB <c>view(az, el)</c>).</summary>
    public static void View(double azimuth, double elevation)
    {
        AxesModel axes = Gca();
        axes.Azimuth = azimuth;
        axes.Elevation = elevation;
    }

    /// <summary>
    /// Applies a built-in colormap ("parula", "viridis", "turbo", "jet", "hot", "cool", "gray",
    /// "hsv", "bone", "copper", "pink", "spring", "summer", "autumn", "winter", "lines") to every
    /// color-mapped plot in the current axes (MATLAB <c>colormap</c>).
    /// </summary>
    public static void Colormap(string name)
    {
        if (!Core.Drawing.Colormap.TryGetByName(name, out Core.Drawing.Colormap map))
        {
            throw new ArgumentException(
                $"Unknown colormap '{name}'. Known colormaps: {string.Join(", ", Core.Drawing.Colormap.KnownNames)}.");
        }

        Colormap(map);
    }

    /// <summary>
    /// Applies a colormap object to every color-mapped plot in the current axes — the form
    /// <c>colormap(map)</c> with an N-by-3 table takes, once the table has been turned into a map.
    /// </summary>
    public static void Colormap(Core.Drawing.Colormap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        foreach (PlotObject plot in Gca().Plots)
        {
            switch (plot)
            {
                case ImagePlot image:
                    image.Colormap = map;
                    break;
                case SurfacePlot surface:
                    surface.Colormap = map;
                    break;
                case ContourPlot contour:
                    contour.Colormap = map;
                    break;
            }
        }
    }

    /// <summary>The colormap of the first color-mapped plot in the current axes, or parula if there is none.</summary>
    public static Core.Drawing.Colormap CurrentColormap()
    {
        foreach (PlotObject plot in Gca().Plots)
        {
            if (plot is IColorMapped mapped)
            {
                return mapped.Colormap;
            }
        }

        return Core.Drawing.Colormap.Parula;
    }

    /// <summary>
    /// Pins the color limits of every color-mapped plot in the current axes (MATLAB <c>caxis</c> /
    /// <c>clim</c>), which is what makes several plots share one colorbar scale.
    /// </summary>
    public static void CLim(double min, double max)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max) || min >= max)
        {
            throw new ArgumentException($"Color limits must be finite and increasing, but were [{min}, {max}].");
        }

        foreach (PlotObject plot in Gca().Plots)
        {
            switch (plot)
            {
                case ImagePlot image:
                    image.AutoScaleColor = false;
                    image.ColorMin = min;
                    image.ColorMax = max;
                    break;
                case SurfacePlot surface:
                    surface.AutoScaleColor = false;
                    surface.ColorMin = min;
                    surface.ColorMax = max;
                    break;
                case ContourPlot contour:
                    contour.AutoScaleColor = false;
                    contour.ColorMin = min;
                    contour.ColorMax = max;
                    break;
            }
        }
    }

    /// <summary>Returns the color limits to each plot's own data range (MATLAB <c>caxis auto</c>).</summary>
    public static void CLimAuto()
    {
        foreach (PlotObject plot in Gca().Plots)
        {
            switch (plot)
            {
                case ImagePlot image:
                    image.AutoScaleColor = true;
                    break;
                case SurfacePlot surface:
                    surface.AutoScaleColor = true;
                    break;
                case ContourPlot contour:
                    contour.AutoScaleColor = true;
                    break;
            }
        }
    }

    /// <summary>
    /// The color limits currently in force — the first color-mapped plot's, which is the one the
    /// colorbar is drawn from. <c>[0, 1]</c> when the axes has nothing color-mapped in it.
    /// </summary>
    public static (double Min, double Max) GetCLim()
    {
        foreach (PlotObject plot in Gca().Plots)
        {
            if (plot is IColorMapped mapped)
            {
                return mapped.ColorRange;
            }
        }

        return (0, 1);
    }

    /// <summary>
    /// Brightens (<paramref name="beta"/> &gt; 0) or darkens (&lt; 0) the current colormap by MATLAB's
    /// gamma rule, and applies the result (MATLAB <c>brighten</c>).
    /// </summary>
    public static void Brighten(double beta) => Colormap(CurrentColormap().Brighten(beta));

    /// <summary>
    /// Sets the colors plots in the current axes cycle through (MATLAB <c>colororder</c>). An empty
    /// list hands the axes back to the theme.
    /// </summary>
    public static void ColorOrder(IReadOnlyList<Core.Drawing.Color> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        Gca().ColorOrder = colors.Count > 0 ? colors.ToArray() : null;
    }

    /// <summary>The colors the current axes cycles through, or null when the theme decides.</summary>
    public static IReadOnlyList<Core.Drawing.Color>? GetColorOrder() => Gca().ColorOrder;

    /// <summary>Shows or hides the current axes' colorbar (MATLAB <c>colorbar</c>).</summary>
    public static void Colorbar(bool on = true) => Gca().Colorbar.Visible = on;

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

    /// <summary>
    /// Clears the current axes and makes it a polar one (MATLAB <c>polaraxes</c>), returning it. Like
    /// every other verb that draws afresh it obeys <c>hold</c>, so holding a polar axes and asking for
    /// one again keeps what is already drawn.
    /// </summary>
    public static AxesModel PolarAxes()
    {
        AxesModel axes = PrepareAxes();
        axes.MakePolar();
        return axes;
    }

    /// <summary>
    /// Plots a histogram of angles in radians on a polar axes (MATLAB <c>polarhistogram</c>). The bin
    /// edges are given rather than chosen: picking them is the caller's business, because the rule
    /// that picks them is the one <c>histcounts</c> uses.
    /// </summary>
    public static PolarHistogramPlot PolarHistogram(double[] thetaRadians, double[] binEdges)
    {
        AxesModel axes = PrepareAxes();
        axes.MakePolar();
        return axes.AddPolarHistogram(thetaRadians, binEdges);
    }

    /// <summary>Plots a histogram of angles from counts already taken (MATLAB's <c>'BinCounts'</c> form).</summary>
    public static PolarHistogramPlot PolarHistogramOfCounts(double[] binEdges, double[] binCounts)
    {
        AxesModel axes = PrepareAxes();
        axes.MakePolar();
        return axes.AddPolarHistogramOfCounts(binEdges, binCounts);
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

    /// <summary>Plots a reflection-coefficient locus on a Smith chart, given Γ directly (Γ = real + j·imag).</summary>
    public static LinePlot SmithGamma(double[] gammaReal, double[] gammaImag)
    {
        AxesModel axes = PrepareAxes();
        return axes.AddSmithReflection(gammaReal, gammaImag);
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
        Touch(_currentNumber);
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
                Touch(_currentNumber);
                return existing;
            }
        }

        AxesModel axes = figure.AddSubplot(rows, cols, index);
        _currentAxes = axes;
        Touch(_currentNumber);
        return axes;
    }

    /// <summary>
    /// Selects (creating if needed) the axes spanning cells <paramref name="firstIndex"/>..
    /// <paramref name="lastIndex"/> of a <paramref name="rows"/> × <paramref name="cols"/> grid and
    /// makes it current (MATLAB <c>subplot(m, n, [p1 p2])</c>). The cells must form a rectangle.
    /// </summary>
    public static AxesModel Subplot(int rows, int cols, int firstIndex, int lastIndex)
    {
        FigureModel figure = CurrentFigure;
        Rect2D bounds = FigureModel.SubplotBounds(rows, cols, firstIndex, lastIndex);
        foreach (AxesModel existing in figure.Axes)
        {
            if (BoundsClose(existing.NormalizedBounds, bounds))
            {
                _currentAxes = existing;
                Touch(_currentNumber);
                return existing;
            }
        }

        AxesModel axes = figure.AddSubplot(rows, cols, firstIndex, lastIndex);
        _currentAxes = axes;
        Touch(_currentNumber);
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

    /// <summary>Sets the second line under the current axes' title (MATLAB <c>subtitle</c>).</summary>
    public static void Subtitle(string text) => Gca().Subtitle = text;

    /// <summary>Sets the title over the whole current figure (MATLAB <c>sgtitle</c>).</summary>
    public static void SgTitle(string text) => CurrentFigure.Title = text;

    /// <summary>Shows or hides the rectangular frame around the current axes (MATLAB <c>box</c>).</summary>
    public static void Box(bool on = true) => Gca().FrameVisible = on;

    /// <summary>Adds a reference line down the current axes at one X (MATLAB <c>xline</c>).</summary>
    public static ConstantLinePlot XLine(double x) => Gca().AddXLine(x);

    /// <summary>Adds a reference line across the current axes at one Y (MATLAB <c>yline</c>).</summary>
    public static ConstantLinePlot YLine(double y) => Gca().AddYLine(y);

    /// <summary>Sets the current X axis label.</summary>
    public static void XLabel(string text) => Gca().PrimaryXAxis.Label = text;

    /// <summary>Sets the label of the Y ruler <c>yyaxis</c> has made active (the left one by default).</summary>
    public static void YLabel(string text) => Gca().ActiveYAxis.Label = text;

    /// <summary>
    /// Makes one side's Y ruler the active one (MATLAB <c>yyaxis left</c> / <c>yyaxis right</c>),
    /// creating the right-hand ruler on first use. Everything y-facing — the label, the limits, the
    /// ticks, and the plots drawn next — follows the active side.
    /// </summary>
    public static AxisModel YyAxis(bool right) => Gca().UseYAxis(right ? 1 : 0);

    /// <summary>Turns the current axes grid on or off.</summary>
    public static void Grid(bool on = true)
    {
        Gca().Grid.ShowMajor = on;
        Gca().Grid.Visible = true;
    }

    /// <summary>
    /// Enables the legend, optionally assigning display names to the plots in order. Only plots that
    /// can appear in a legend are counted: a backdrop image (imshow) or another non-legend plot must
    /// not swallow the first name and push every series' label one place along.
    /// </summary>
    public static void Legend(params string[] names)
    {
        AxesModel axes = Gca();
        int next = 0;
        foreach (PlotObject plot in axes.Plots)
        {
            if (next >= names.Length)
            {
                break;
            }

            if (plot is ILegendItem)
            {
                plot.DisplayName = names[next++];
            }
        }

        axes.Legend.Visible = true;
    }

    /// <summary>
    /// Enables <paramref name="axes"/>' legend showing exactly <paramref name="plots"/>, in that
    /// order. Every other series on the axes keeps its row but is left out of the drawing, which is
    /// what MATLAB's <c>legend(ax, h)</c> means: name these, hide the rest.
    /// </summary>
    public static LegendModel Legend(AxesModel axes, IReadOnlyList<PlotObject> plots)
    {
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(plots);

        LegendModel legend = axes.Legend;
        legend.SyncEntries(axes.Plots.OfType<ILegendItem>().Cast<PlotObject>().ToList());

        foreach (LegendEntryModel entry in legend.Entries)
        {
            entry.Visible = entry.Plot is not null && plots.Any(p => ReferenceEquals(p, entry.Plot));
        }

        // Rows are drawn in list order, so the requested order has to become the list order.
        for (int wanted = 0; wanted < plots.Count; wanted++)
        {
            for (int at = wanted; at < legend.Entries.Count; at++)
            {
                if (ReferenceEquals(legend.Entries[at].Plot, plots[wanted]))
                {
                    if (at != wanted)
                    {
                        legend.Entries.Move(at, wanted);
                    }

                    break;
                }
            }
        }

        legend.Visible = true;
        Touch(GetFigureNumber(axes.Parent as FigureModel ?? CurrentFigure));
        return legend;
    }

    /// <summary>Adds a text label at the given data point on the current axes (MATLAB <c>text</c>).</summary>
    public static TextAnnotation Text(double x, double y, string text) => Gca().AddText(x, y, text);

    /// <summary>
    /// Adds a text label at a point in space (MATLAB <c>text(x, y, z, str)</c>). The height is only
    /// read in a 3D axes, so this is safe to call before the axes has been switched into 3D.
    /// </summary>
    public static TextAnnotation Text(double x, double y, double z, string text)
    {
        TextAnnotation annotation = Gca().AddText(x, y, text);
        annotation.Z = z;
        return annotation;
    }

    /// <summary>Adds an arrow between two data points on the current axes (MATLAB <c>annotation('arrow')</c>).</summary>
    public static ArrowAnnotation Arrow(double x1, double y1, double x2, double y2) =>
        Gca().AddArrow(x1, y1, x2, y2);

    /// <summary>Adds a plain line annotation between two data points on the current axes.</summary>
    public static ArrowAnnotation Line(double x1, double y1, double x2, double y2) =>
        Gca().AddLineAnnotation(x1, y1, x2, y2);

    /// <summary>Sets whether subsequent plots accumulate (MATLAB <c>hold on/off</c>).</summary>
    public static void Hold(bool on = true) => Gca().Hold = on;

    /// <summary>Sets the current X axis limits and disables auto-scaling on it.</summary>
    public static void XLim(double min, double max)
    {
        AxisModel axis = Gca().PrimaryXAxis;
        axis.AutoScale = false;
        axis.Range = new DataRange(min, max);
    }

    /// <summary>Sets the active Y ruler's limits and disables auto-scaling on it.</summary>
    public static void YLim(double min, double max)
    {
        AxisModel axis = Gca().ActiveYAxis;
        axis.AutoScale = false;
        axis.Range = new DataRange(min, max);
    }

    /// <summary>Clears the current figure's axes and annotations (MATLAB <c>clf</c>).</summary>
    public static void Clf()
    {
        _currentFigure?.Axes.Clear();
        _currentFigure?.Annotations.Clear();
        _currentAxes = null;
        Touch(_currentNumber);
    }

    /// <summary>Clears figure <paramref name="number"/>, making it current first (MATLAB <c>clf(n)</c>).</summary>
    public static void Clf(int number)
    {
        Figure(number);
        Clf();
    }

    /// <summary>
    /// Removes figure <paramref name="number"/> from the registry (MATLAB <c>close</c>), returning
    /// whether it was there. The most recently touched surviving figure becomes current, so a later
    /// <c>gcf</c> or bare <c>plot</c> lands somewhere sensible; with none left, the next figure verb
    /// starts again at figure 1.
    /// </summary>
    public static bool CloseFigure(int number)
    {
        lock (Registry)
        {
            if (!Figures.Remove(number))
            {
                return false;
            }

            TouchStamps.Remove(number);
            if (_currentNumber != number)
            {
                return true;
            }

            _currentFigure = null;
            _currentNumber = 0;
            _currentAxes = null;

            int successor = 0;
            long best = long.MinValue;
            foreach ((int candidate, long touched) in TouchStamps)
            {
                if (touched > best && Figures.ContainsKey(candidate))
                {
                    successor = candidate;
                    best = touched;
                }
            }

            if (successor != 0)
            {
                Figure(successor);
            }

            return true;
        }
    }

    /// <summary>Signals a host to display the current figure.</summary>
    public static void Show() => FigureShown?.Invoke(null, CurrentFigure);

    /// <summary>Resets the figure registry and the current-figure/current-axes/hold state (run start, tests).</summary>
    public static void Reset()
    {
        lock (Registry)
        {
            Figures.Clear();
            TouchStamps.Clear();
        }

        _currentFigure = null;
        _currentNumber = 0;
        _currentAxes = null; // hold lives on the axes, so dropping them drops it
    }

    private static FigureModel CreateFigure()
    {
        var figure = new FigureModel();
        _currentAxes = null;
        return figure;
    }

    /// <summary>The data extent of a grid vector (falls back to 0..count for empty/degenerate input).</summary>
    private static DataRange VectorExtent(double[] values, int count)
    {
        if (values.Length == 0)
        {
            return new DataRange(0, System.Math.Max(count, 1));
        }

        double min = values[0];
        double max = values[0];
        foreach (double v in values)
        {
            min = System.Math.Min(min, v);
            max = System.Math.Max(max, v);
        }

        return max > min ? new DataRange(min, max) : new DataRange(min, min + 1);
    }

    private static double[] Ramp(int count)
    {
        var values = new double[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = i;
        }

        return values;
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

    /// <summary>Returns the current axes, resetting it first when not holding.</summary>
    private static AxesModel PrepareAxes()
    {
        AxesModel axes = Gca();
        if (!IsHolding)
        {
            ResetAxesForReplace(axes);
        }

        return axes;
    }

    /// <summary>
    /// Returns axes to the state a fresh plot expects — MATLAB's <c>NextPlot = 'replace'</c>. Plotting
    /// over an old plot must not inherit its title, labels, frozen limits, colorbar, or 3D view, or a
    /// script run a second time draws its new data inside the first run's decoration.
    /// The subplot cell (<see cref="AxesModel.NormalizedBounds"/>), the background, the title's font,
    /// and hold are kept: those describe the axes, not the plot that was in it.
    /// </summary>
    private static void ResetAxesForReplace(AxesModel axes)
    {
        // An axes with two Y rulers belongs to yyaxis, and there each side is replaced on its own:
        // plotting against the right ruler must not wipe the left series, or the second half of every
        // two-sided script would erase the first. The shared decoration is left alone for the same
        // reason — the other side is still using the x ruler, the title, and the legend.
        if (axes.YAxes.Count > 1)
        {
            int active = axes.ActiveYAxisIndex;
            for (int i = axes.Plots.Count - 1; i >= 0; i--)
            {
                if (axes.Plots[i].YAxisIndex == active)
                {
                    axes.Plots.RemoveAt(i);
                }
            }

            ResetAxis(axes.ActiveYAxis);
            return;
        }

        axes.Plots.Clear();
        axes.Annotations.Clear();
        axes.Lights.Clear();
        axes.Title = string.Empty;

        while (axes.XAxes.Count > 1)
        {
            axes.XAxes.RemoveAt(axes.XAxes.Count - 1);
        }

        while (axes.YAxes.Count > 1)
        {
            axes.YAxes.RemoveAt(axes.YAxes.Count - 1);
        }

        ResetAxis(axes.PrimaryXAxis);
        ResetAxis(axes.PrimaryYAxis);
        ResetAxis(axes.ZAxis);
        ResetAxis(axes.RAxis);
        ResetAxis(axes.ThetaAxis);
        axes.ThetaAxis.AutoScale = false;
        axes.ThetaAxis.Range = new DataRange(0, 360);

        axes.Grid.ShowMajor = false;
        axes.Grid.ShowMinor = false;
        axes.Legend.Visible = false;
        axes.Colorbar.Visible = false;
        axes.EqualAspect = false;
        axes.FrameVisible = true;
        axes.Is3D = false;
        axes.Azimuth = -37.5;
        axes.Elevation = 30;

        // Polar is a mode like 3-D, so replacing what is drawn puts the axes back on square paper.
        // A script that means to keep the circle says polaraxes, or draws with an angular verb.
        axes.IsPolar = false;
        axes.ThetaZeroLocation = ThetaZeroLocation.Right;
        axes.ThetaDirection = ThetaDirection.CounterClockwise;
        axes.ThetaAxisUnits = AngleUnits.Degrees;
        axes.RAxisLocation = 80;
    }

    private static void ResetAxis(AxisModel axis)
    {
        axis.Label = string.Empty;
        axis.AutoScale = true;
        axis.Scale = AxisScaleType.Linear;
    }

    private static void ApplyLineSpec(LinePlot plot, LineSpec spec)
    {
        (Core.Drawing.Color? color, Core.Drawing.DashStyle? dash, Core.Drawing.MarkerType? marker) = ResolveLineSpec(spec);
        if (color is { } c)
        {
            plot.Color = c;
        }

        if (dash is { } d)
        {
            plot.DashStyle = d;
        }

        if (marker is { } m)
        {
            plot.Marker = m;
        }
    }

    /// <summary>The 3D twin of the line-spec applier; the two plot types share no base that carries these.</summary>
    private static void ApplyLineSpec(Line3DPlot plot, LineSpec spec)
    {
        (Core.Drawing.Color? color, Core.Drawing.DashStyle? dash, Core.Drawing.MarkerType? marker) = ResolveLineSpec(spec);
        if (color is { } c)
        {
            plot.Color = c;
        }

        if (dash is { } d)
        {
            plot.DashStyle = d;
        }

        if (marker is { } m)
        {
            plot.Marker = m;
        }
    }

    /// <summary>
    /// What a parsed line-spec actually changes, with null meaning "leave the plot's own value". A
    /// marker with no line character means markers only, which is MATLAB's reading of a bare "o".
    /// </summary>
    private static (Core.Drawing.Color? Color, Core.Drawing.DashStyle? Dash, Core.Drawing.MarkerType? Marker) ResolveLineSpec(LineSpec spec)
    {
        Core.Drawing.DashStyle? dash = null;
        if (spec.LineSpecified && spec.Dash is { } explicitDash)
        {
            dash = explicitDash;
        }
        else if (spec.MarkerSpecified && !spec.LineSpecified)
        {
            dash = Core.Drawing.DashStyle.None;
        }

        return (spec.Color, dash, spec.Marker);
    }
}
