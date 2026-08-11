using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M55 wave G: the three verbs that draw nothing of their own.
/// <para>
/// <c>pareto</c>, <c>plotmatrix</c> and <c>plotyy</c> are compositions — each one arranges plots and
/// axes that already exist, so none of them adds a plot object, a DTO, or a line of rendering. What
/// they add is the arrangement: a sorted bar chart read against a cumulative curve on a second ruler,
/// a grid of scatter plots with each column's distribution down the diagonal, and two series measured
/// against two different scales.
/// </para>
/// </summary>
internal static partial class JgsBuiltins
{
    private static void RegisterCompositionBuiltins(JgsEnvironment env)
    {
        // Each of the three answers with more than one thing, so each is registered through the
        // multi-output seam and hands back as much of its answer as the call asked for.
        void DefineComposition(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue[]> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) => body(args, line, col)[0])
            {
                BindsAnsAsStatement = false,
                MultiOutput = (args, wanted, line, col) =>
                {
                    JgsValue[] all = body(args, line, col);
                    return wanted >= all.Length ? all : all[..System.Math.Max(1, wanted)];
                },
            }));

        DefineComposition("pareto", Pareto);
        DefineComposition("plotmatrix", PlotMatrix);
        DefineComposition("plotyy", PlotYy);
    }

    // --- pareto ---------------------------------------------------------------------------------

    /// <summary>
    /// <c>pareto(y)</c>, <c>pareto(y, names)</c>, either followed by a threshold: the values sorted
    /// largest first, drawn as bars against the left ruler, with the running share of the whole drawn
    /// as a percentage curve against the right one.
    /// <para>
    /// The two rulers are what make the chart readable: bars in the data's own units say how big each
    /// contributor is, and the curve in percent says how much of the problem the first few of them
    /// account for. Both scales are pinned rather than fitted, because the top of one has to mean the
    /// same place on the page as the top of the other.
    /// </para>
    /// </summary>
    private static JgsValue[] Pareto(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () =>
        {
            ArityRange("pareto", rest, 1, 3, line, col);

            double[] values = ToDoubles("pareto", rest[0], line, col);
            if (values.Length == 0)
            {
                throw new JgsRuntimeException(line, col, "pareto expects the values to rank: pareto(y).");
            }

            string[]? names = null;
            double threshold = 0.95;
            for (int i = 1; i < rest.Count; i++)
            {
                if (IsScalar(rest[i]))
                {
                    threshold = NumOf("pareto: threshold", rest[i], line, col);
                    if (!(threshold > 0) || threshold > 1)
                    {
                        throw new JgsRuntimeException(line, col,
                            $"pareto: the threshold is a fraction above 0 and at most 1, but got {threshold:G6}.");
                    }
                }
                else
                {
                    names = ParetoNames(rest[i], values.Length, line, col);
                }
            }

            double total = 0;
            foreach (double value in values)
            {
                if (value < 0)
                {
                    throw new JgsRuntimeException(line, col,
                        "pareto ranks contributions, so none of the values may be negative.");
                }

                total += value;
            }

            if (!(total > 0))
            {
                throw new JgsRuntimeException(line, col, "pareto needs values that add up to more than zero.");
            }

            int[] order = [.. Enumerable.Range(0, values.Length).OrderByDescending(i => values[i])];

            // MATLAB shows the bars that carry the threshold share of the whole and no more than ten
            // of them, because the point of the chart is the short head of the distribution.
            var running = new double[order.Length];
            double sum = 0;
            int keep = System.Math.Min(10, order.Length);
            for (int i = 0; i < order.Length; i++)
            {
                sum += values[order[i]];
                // Scaled before it is divided, so a bar carrying exactly half of the total reads 50
                // rather than 50.000000000000007.
                running[i] = sum * 100 / total;
                if (running[i] >= threshold * 100)
                {
                    keep = System.Math.Min(keep, i + 1);
                    break;
                }
            }

            double[] positions = Counting(keep);
            var heights = new double[keep];
            var cumulative = new double[keep];
            for (int i = 0; i < keep; i++)
            {
                heights[i] = values[order[i]];
                cumulative[i] = running[i];
            }

            AxesModel axes = JG.Gca();
            axes.UseYAxis(0);
            BarPlot bars = axes.AddBar(positions, heights);
            bars.FillColor = PaletteColorFor(bars);
            bars.Name = "Pareto";
            Pin(axes.PrimaryYAxis, 0, total);

            axes.UseYAxis(1);
            LinePlot curve = axes.AddLine(positions, cumulative);
            curve.Color = PaletteColorFor(curve);
            curve.Marker = MarkerType.Circle;
            curve.MarkerSize = 5;
            curve.Name = "Cumulative";
            Pin(axes.ActiveYAxis, 0, 100);

            // The bars are the chart; the curve explains them. Leaving the left ruler active is what
            // makes a following ylabel land on the units rather than on the percentages.
            axes.UseYAxis(0);

            // The bars stand at 1, 2, 3 … whatever the data was indexed by, so the tick labels have to
            // say which contributor each one is.
            axes.PrimaryXAxis.TickPositions = positions;
            axes.PrimaryXAxis.TickLabelOverrides = [.. Enumerable.Range(0, keep)
                .Select(i => names is not null ? names[order[i]] : (order[i] + 1).ToString("G6", System.Globalization.CultureInfo.InvariantCulture))];

            return new[]
            {
                HandlesFor<PlotObject>([bars, curve]),
                JgsHandleRegistry.For(axes),
            };
        });
    }

    /// <summary>The names under the bars, which may be written as text or as the x values themselves.</summary>
    private static string[] ParetoNames(JgsValue value, int expected, int line, int col)
    {
        bool text = value.Type == JgsType.Cell
            || (value.Type == JgsType.Array && value.ArrayLength > 0 && value.ElementAt(0).Type == JgsType.String);
        if (text)
        {
            return LabelsOf("pareto: names", value, expected, line, col);
        }

        double[] numbers = ToDoubles("pareto: names", value, line, col);
        if (numbers.Length != expected)
        {
            throw new JgsRuntimeException(line, col,
                $"pareto: there are {numbers.Length} names but {expected} of them are needed.");
        }

        return [.. numbers.Select(n => n.ToString("G6", System.Globalization.CultureInfo.InvariantCulture))];
    }

    /// <summary>Freezes a ruler on a range, so the two sides of a pareto chart line up at the top.</summary>
    private static void Pin(AxisModel ruler, double low, double high)
    {
        ruler.AutoScale = false;
        ruler.Range = new DataRange(low, high);
    }

    // --- plotmatrix -----------------------------------------------------------------------------

    /// <summary>
    /// <c>plotmatrix(X)</c> and <c>plotmatrix(X, Y)</c>, either followed by a line spec: a grid of
    /// scatter plots, one per pair of columns, so that every relationship in a table of measurements
    /// can be looked at once. <c>plotmatrix(X)</c> puts each column's own distribution down the
    /// diagonal, where a scatter of a column against itself would say nothing.
    /// </summary>
    private static JgsValue[] PlotMatrix(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count > 0 && JgsHandleRegistry.TryGet(args[0], out JgsHandleEntry? aimed) && aimed.Target is AxesModel)
        {
            throw new JgsRuntimeException(line, col,
                "plotmatrix lays out its own grid of axes, so it cannot be aimed at one. "
                + "Draw it into a figure of its own.");
        }

        ArityRange("plotmatrix", args, 1, 3, line, col);

        string? spec = null;
        var data = new List<JgsValue>();
        foreach (JgsValue arg in args)
        {
            if (arg.Type == JgsType.String)
            {
                spec = arg.AsString;
            }
            else
            {
                data.Add(arg);
            }
        }

        if (data.Count is < 1 or > 2)
        {
            throw new JgsRuntimeException(line, col,
                "plotmatrix takes plotmatrix(x), plotmatrix(x, y), and either one followed by a line spec.");
        }

        IReadOnlyList<double[]> xs = SeriesColumns("plotmatrix", data[0], line, col);
        IReadOnlyList<double[]>? ys = data.Count == 2 ? SeriesColumns("plotmatrix", data[1], line, col) : null;
        if (ys is not null && ys[0].Length != xs[0].Length)
        {
            throw new JgsRuntimeException(line, col,
                $"plotmatrix: x has {xs[0].Length} rows but y has {ys[0].Length}, and a point needs both.");
        }

        LineSpec style = LineSpec.Parse(spec);
        int rows = ys?.Count ?? xs.Count;
        int cols = xs.Count;

        // The grid replaces whatever the figure held, the way drawing any other chart into it does.
        JG.Clf();

        var scatters = new double[rows * cols];
        var cells = new double[rows * cols];
        var diagonal = new List<double>();
        var distributions = new List<double>();
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                AxesModel cell = JG.Subplot(rows, cols, (r * cols) + c + 1);
                cells[(c * rows) + r] = JgsHandleRegistry.For(cell).AsNumber;

                if (ys is null && r == c)
                {
                    // A column against itself is a straight line and says nothing, so the diagonal
                    // shows how that column is distributed instead.
                    HistogramPlot bars = cell.AddHistogram(xs[c]);
                    bars.FillColor = style.Color ?? PaletteColorFor(bars);
                    distributions.Add(JgsHandleRegistry.For(bars).AsNumber);
                    diagonal.Add(cells[(c * rows) + r]);
                    continue;
                }

                ScatterPlot points = cell.AddScatter(xs[c], ys is null ? xs[r] : ys[r]);
                points.Color = style.Color ?? PaletteColorFor(points);
                points.Marker = style.Marker ?? MarkerType.Circle;
                points.MarkerSize = 4;
                scatters[(c * rows) + r] = JgsHandleRegistry.For(points).AsNumber;
            }
        }

        return
        [
            JgsMatrix.FromColumnMajor(scatters, rows, cols),
            JgsMatrix.FromColumnMajor(cells, rows, cols),

            // MATLAB's BigAx is an invisible axes covering the grid, drawn only so that a title can
            // hang on it. An invisible axes here draws nothing at all, its title included, so rather
            // than mint one that would silently swallow the title, the slot answers with handle 0 and
            // sgtitle is the way to write over the whole grid.
            JgsValue.Number(0),
            JgsMatrix.FromColumnMajor([.. distributions], 1, distributions.Count),
            JgsMatrix.FromColumnMajor([.. diagonal], 1, diagonal.Count),
        ];
    }

    // --- plotyy ---------------------------------------------------------------------------------

    /// <summary>The verbs <c>plotyy</c> will draw a side with, in MATLAB's spellings.</summary>
    private static readonly string[] PlotYyVerbs =
        ["plot", "semilogx", "semilogy", "loglog", "bar", "stem", "stairs", "area", "scatter"];

    /// <summary>
    /// <c>plotyy(x1, y1, x2, y2)</c>, optionally naming the verb each side is drawn with: two series
    /// measured against two different scales on one axes.
    /// <para>
    /// MATLAB draws these on two axes stacked on top of each other; this build has one axes with two
    /// y rulers, which is what <c>yyaxis</c> uses and what the ruler-per-side autoscale was built for.
    /// So <c>AX</c> answers with the two rulers rather than two axes — and because every axes verb
    /// takes a ruler where it takes an axes, <c>ylabel(AX(2), 'right')</c> and <c>ylim(AX(1), …)</c>
    /// still say what they say in MATLAB.
    /// </para>
    /// </summary>
    private static JgsValue[] PlotYy(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () =>
        {
            ArityRange("plotyy", rest, 4, 6, line, col);

            double[] x1 = ToDoubles("plotyy", rest[0], line, col);
            double[] y1 = ToDoubles("plotyy", rest[1], line, col);
            double[] x2 = ToDoubles("plotyy", rest[2], line, col);
            double[] y2 = ToDoubles("plotyy", rest[3], line, col);
            Paired("plotyy", x1, y1, line, col);
            Paired("plotyy", x2, y2, line, col);

            string left = rest.Count > 4 ? PlotYyVerb(rest[4], line, col) : "plot";
            string right = rest.Count > 5 ? PlotYyVerb(rest[5], line, col) : left;

            AxesModel axes = JG.Gca();
            axes.UseYAxis(0);
            PlotObject first = DrawWith(left, axes, x1, y1, line, col);
            axes.UseYAxis(1);
            PlotObject second = DrawWith(right, axes, x2, y2, line, col);

            // Each ruler wears the colour of the series it measures, because with two scales on one
            // frame the tick numbers are the only thing saying which curve they belong to.
            Tint(axes.YAxes[0], SeriesColorOf(first));
            Tint(axes.ActiveYAxis, SeriesColorOf(second));

            // MATLAB leaves the second axes current, so a following ylabel labels the right side.
            return new[]
            {
                JgsMatrix.FromColumnMajor(
                    [JgsHandleRegistry.For(axes.YAxes[0]).AsNumber, JgsHandleRegistry.For(axes.ActiveYAxis).AsNumber],
                    1,
                    2),
                JgsHandleRegistry.For(first),
                JgsHandleRegistry.For(second),
            };
        });
    }

    /// <summary>The verb naming one side of a <c>plotyy</c>, written as a word or as a handle.</summary>
    private static string PlotYyVerb(JgsValue value, int line, int col)
    {
        string word = value.Type == JgsType.Function
            ? value.AsCallable.Name
            : StrOf("plotyy", value, line, col);

        if (!PlotYyVerbs.Contains(word, StringComparer.OrdinalIgnoreCase))
        {
            throw new JgsRuntimeException(line, col,
                $"plotyy cannot draw a side with '{word}'. It knows {string.Join(", ", PlotYyVerbs)}.");
        }

        return word.ToLowerInvariant();
    }

    /// <summary>Draws one side of a <c>plotyy</c> with the verb it named.</summary>
    private static PlotObject DrawWith(string verb, AxesModel axes, double[] xs, double[] ys, int line, int col)
    {
        switch (verb)
        {
            case "bar":
                BarPlot bars = axes.AddBar(xs, ys);
                bars.FillColor = PaletteColorFor(bars);
                return bars;
            case "stem":
                StemPlot stems = axes.AddStem(xs, ys);
                stems.Color = PaletteColorFor(stems);
                return stems;
            case "area":
                AreaPlot band = axes.AddArea(xs, ys);
                band.FaceColor = PaletteColorFor(band);
                return band;
            case "scatter":
                ScatterPlot points = axes.AddScatter(xs, ys);
                points.Color = PaletteColorFor(points);
                return points;
            default:
                LinePlot curve = verb == "stairs" ? axes.AddStairs(xs, ys) : axes.AddLine(xs, ys);
                curve.Color = PaletteColorFor(curve);
                if (verb is "semilogx" or "loglog")
                {
                    axes.PrimaryXAxis.Scale = AxisScaleType.Logarithmic;
                }

                if (verb is "semilogy" or "loglog")
                {
                    axes.ActiveYAxis.Scale = AxisScaleType.Logarithmic;
                }

                return curve;
        }
    }

    /// <summary>Paints a ruler's numbers and label in one colour.</summary>
    private static void Tint(AxisModel ruler, Color color)
    {
        ruler.TickLabelStyle = ruler.TickLabelStyle.WithColor(color);
        ruler.LabelStyle = ruler.LabelStyle.WithColor(color);
    }

    /// <summary>The colour a drawn series ended up with, whichever kind of plot it is.</summary>
    private static Color SeriesColorOf(PlotObject plot) => plot switch
    {
        LinePlot curve => curve.Color ?? PaletteColorFor(plot),
        BarPlot bars => bars.FillColor ?? PaletteColorFor(plot),
        AreaPlot band => band.FaceColor ?? PaletteColorFor(plot),
        StemPlot stems => stems.Color ?? PaletteColorFor(plot),
        ScatterPlot points => points.Color ?? PaletteColorFor(plot),
        _ => PaletteColorFor(plot),
    };

    /// <summary>Refuses an x and a y that cannot make points together.</summary>
    private static void Paired(string verb, double[] xs, double[] ys, int line, int col)
    {
        if (xs.Length != ys.Length)
        {
            throw new JgsRuntimeException(line, col,
                $"{verb}: x has {xs.Length} values but y has {ys.Length}, and a point needs both.");
        }
    }
}
