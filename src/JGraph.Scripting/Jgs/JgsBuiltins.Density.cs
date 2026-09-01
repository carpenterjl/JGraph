using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M57 wave D: <c>binscatter</c>, the chart for the sample size a scatter cannot draw. Past a few
/// thousand markers every new one lands on one already there, and the picture stops saying how many
/// readings are underneath; counting them into bins and colouring the bins says it again.
/// </summary>
internal static partial class JgsBuiltins
{
    private static void RegisterDensityBuiltins(JgsEnvironment env)
    {
        env.Declare("binscatter", JgsValue.Function(
            new BuiltinFunction("binscatter", (args, line, col) => BinScatter(args, line, col))
            { BindsAnsAsStatement = false }));

        env.Declare("histogram2", JgsValue.Function(
            new BuiltinFunction("histogram2", (args, line, col) => Histogram2(args, line, col))
            { BindsAnsAsStatement = false }));
    }

    /// <summary>The properties <c>histogram2</c> accepts after its data.</summary>
    private static readonly string[] Histogram2OptionNames =
    [
        "NumBins", "BinWidth", "XBinEdges", "YBinEdges", "XBinLimits", "YBinLimits", "BinMethod",
        "BinCounts", "Normalization", "DisplayStyle", "FaceColor", "EdgeColor", "LineWidth",
        "FaceAlpha", "ShowEmptyBins", "DisplayName",
    ];

    /// <summary>
    /// <c>histogram2(x, y)</c>, with a bin count or a pair of edge vectors in the positional slot,
    /// and MATLAB's name/value pairs after it — including the data-free
    /// <c>histogram2('XBinEdges', …, 'YBinEdges', …, 'BinCounts', …)</c> form.
    /// </summary>
    /// <remarks>
    /// The counting is not done here. The edges and the counts are the chart's own, worked out by the
    /// same <c>Binning</c> code <c>histcounts2</c> answers from, so a script that draws the histogram
    /// and then checks it against <c>histcounts2</c> is comparing two readings of one rule rather
    /// than two rules that happen to agree today.
    /// </remarks>
    private static JgsValue Histogram2(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () =>
        {
            int optionStart = 0;
            double[] xs = [];
            double[] ys = [];
            if (rest.Count > 0 && !IsTextScalar(rest[0]))
            {
                if (rest.Count < 2 || IsTextScalar(rest[1]))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:minrhs",
                        "histogram2 expects both coordinates: histogram2(x, y).");
                }

                xs = ToDoubles("histogram2", rest[0], line, col);
                ys = ToDoubles("histogram2", rest[1], line, col);
                if (xs.Length != ys.Length)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:histogram2:incorrectSize",
                        $"histogram2: x has {xs.Length} readings but y has {ys.Length}.");
                }

                optionStart = 2;
            }

            // What may stand between the data and the pairs is a bin count, or a pair of edge
            // vectors — which is two arguments, so the shape of the third decides how far to read.
            (int Across, int Up)? bins = null;
            double[]? xEdges = null;
            double[]? yEdges = null;
            if (rest.Count > optionStart && !IsTextScalar(rest[optionStart]))
            {
                if (rest.Count > optionStart + 1 && !IsTextScalar(rest[optionStart + 1]))
                {
                    xEdges = ToDoubles("histogram2", rest[optionStart], line, col);
                    yEdges = ToDoubles("histogram2", rest[optionStart + 1], line, col);
                    optionStart += 2;
                }
                else
                {
                    bins = BinCounts("histogram2: the bin count", rest[optionStart], line, col);
                    optionStart++;
                }
            }

            var options = new List<(string Name, JgsValue Value)>();
            if ((rest.Count - optionStart) % 2 != 0)
            {
                throw new JgsRuntimeException(line, col, "histogram2: options come in 'Name', value pairs.");
            }

            for (int i = optionStart; i < rest.Count; i += 2)
            {
                options.Add((StrOf("histogram2", rest[i], line, col), rest[i + 1]));
            }

            // The three that decide how the chart is built rather than how it looks are read before it
            // exists: a grid handed over already counted has no readings to bin, so the constructor
            // that takes readings is the wrong one to have called by then.
            double[,]? counts = null;
            foreach ((string name, JgsValue value) in options)
            {
                switch (name.ToLowerInvariant())
                {
                    case "xbinedges":
                        xEdges = ToDoubles("histogram2: XBinEdges", value, line, col);
                        break;
                    case "ybinedges":
                        yEdges = ToDoubles("histogram2: YBinEdges", value, line, col);
                        break;
                    case "bincounts":
                        counts = CountsGrid("histogram2: BinCounts", value, line, col);
                        break;
                }
            }

            Histogram2Plot plot;
            if (counts is not null)
            {
                if (xEdges is null || yEdges is null)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:histogram2:MissingBinEdges",
                        "BinCounts needs XBinEdges and YBinEdges beside it, since counts on their "
                        + "own do not say where the bins are.");
                }

                plot = JG.Histogram2(xEdges, yEdges, counts);
            }
            else
            {
                if (xs.Length == 0 && xEdges is null)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:minrhs",
                        "histogram2 expects the readings to count: histogram2(x, y).");
                }

                plot = JG.Histogram2(xs, ys);
                if (xEdges is not null && yEdges is not null)
                {
                    plot.SetBinEdges(xEdges, yEdges);
                }
            }

            if (bins is { } counted)
            {
                plot.NumBins = counted;
            }

            Histogram2Options(plot, options, line, col);
            return Handle(plot);
        });
    }

    private static void Histogram2Options(
        Histogram2Plot plot, IReadOnlyList<(string Name, JgsValue Value)> options, int line, int col)
    {
        foreach ((string name, JgsValue value) in options)
        {
            switch (name.ToLowerInvariant())
            {
                // Read already, before the chart was built.
                case "xbinedges":
                case "ybinedges":
                case "bincounts":
                    break;
                case "numbins":
                    plot.NumBins = BinCounts("histogram2: NumBins", value, line, col);
                    break;
                case "binwidth":
                    double[] widths = ToDoubles("histogram2: BinWidth", value, line, col);
                    if (widths.Length is not (1 or 2) || Array.Exists(widths, w => !(w > 0)))
                    {
                        throw new JgsRuntimeException(line, col, "MATLAB:histogram2:expectedPositive",
                            "histogram2: BinWidth is one positive number or a positive pair.");
                    }

                    plot.BinWidth = (widths[0], widths[^1]);
                    break;
                case "xbinlimits":
                    plot.XBinLimits = SpanOption("histogram2: XBinLimits", value, line, col);
                    break;
                case "ybinlimits":
                    plot.YBinLimits = SpanOption("histogram2: YBinLimits", value, line, col);
                    break;
                case "binmethod":
                    plot.BinMethod = Word("histogram2: BinMethod", value, line, col,
                        "auto", "scott", "fd", "integers");
                    break;
                case "normalization":
                    plot.Normalization = Word("histogram2: Normalization", value, line, col,
                        "count", "countdensity", "cumcount", "probability", "pdf", "cdf");
                    break;
                case "displaystyle":
                    plot.DisplayStyle = Word("histogram2: DisplayStyle", value, line, col, "bar3", "tile")
                        == "tile" ? Histogram2DisplayStyle.Tile : Histogram2DisplayStyle.Bar3;

                    // The box field is a 3-D chart and the tile is a flat one, so the style decides
                    // what kind of axes this is — and the tile wants the colorbar the boxes' own
                    // heights make unnecessary.
                    JG.Gca().Is3D = plot.DisplayStyle == Histogram2DisplayStyle.Bar3;
                    JG.Gca().Colorbar.Visible = plot.DisplayStyle == Histogram2DisplayStyle.Tile;
                    break;
                case "facecolor":
                    if (IsTextScalar(value) && Histogram2Plot.IsFaceColorWord(TextOf(value)))
                    {
                        plot.FaceColorWord = TextOf(value).ToLowerInvariant();
                    }
                    else
                    {
                        plot.FaceColor = OptionColor(value, line, col, "histogram2: FaceColor");
                    }

                    break;
                case "edgecolor":
                    plot.EdgeColor = IsTextScalar(value) && TextOf(value).Equals("none", StringComparison.OrdinalIgnoreCase)
                        ? null
                        : OptionColor(value, line, col, "histogram2: EdgeColor");
                    break;
                case "linewidth":
                    plot.LineWidth = Num("histogram2: LineWidth", [value], 0, line, col);
                    break;
                case "facealpha":
                    plot.FaceAlpha = Num("histogram2: FaceAlpha", [value], 0, line, col);
                    break;
                case "showemptybins":
                    plot.ShowEmptyBins =
                        JgsGraphicsProperties.ToOnOff("histogram2: ShowEmptyBins", value, line, col);
                    break;
                case "displayname":
                    SetDisplayName(plot, StrOf("histogram2: DisplayName", value, line, col));
                    break;
                default:
                    throw new JgsRuntimeException(line, col,
                        $"histogram2 has no option '{name}'. "
                        + $"It takes {string.Join(", ", Histogram2OptionNames)}.");
            }
        }
    }

    /// <summary>
    /// A grid of counts in whatever shape it was written: a scalar is one bin, a row is one bin
    /// across and several up, and a column the other way round. <c>Matrix</c> refuses everything but
    /// a stack of rows, which is right for a matrix argument and wrong for this one — a grid one bin
    /// wide is an ordinary thing to hand a two-dimensional histogram.
    /// </summary>
    private static double[,] CountsGrid(string what, JgsValue value, int line, int col)
    {
        int rows = JgsMatrix.RowCount(value);
        int cols = JgsMatrix.ColCount(value);
        if (rows > 1 && cols > 1)
        {
            return Matrix(what, [value], 0, line, col);
        }

        double[] flat = ToDoubles(what, value, line, col);
        var grid = new double[rows > 1 ? flat.Length : 1, rows > 1 ? 1 : flat.Length];
        for (int i = 0; i < flat.Length; i++)
        {
            grid[rows > 1 ? i : 0, rows > 1 ? 0 : i] = flat[i];
        }

        return grid;
    }

    /// <summary>The properties <c>binscatter</c> accepts after its data.</summary>
    private static readonly string[] BinScatterOptionNames =
    [
        "NumBins", "XLimits", "YLimits", "ShowEmptyBins", "UseParallel", "Colormap", "DisplayName",
    ];

    /// <summary>
    /// <c>binscatter(x, y)</c>, <c>binscatter(x, y, N)</c> and <c>binscatter(x, y, [nx ny])</c>, any
    /// of them on a named axes and followed by name/value pairs.
    /// <para>
    /// The bin count is the one thing that can be said either positionally or by name, which is
    /// MATLAB's grammar; saying it both ways is not an error, and the later word wins, since that is
    /// what the reader of the call would expect.
    /// </para>
    /// </summary>
    private static JgsValue BinScatter(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () =>
        {
            if (rest.Count < 2)
            {
                throw new JgsRuntimeException(line, col,
                    "binscatter expects the readings to count: binscatter(x, y).");
            }

            double[] xs = ToDoubles("binscatter", rest[0], line, col);
            double[] ys = ToDoubles("binscatter", rest[1], line, col);
            if (xs.Length != ys.Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"binscatter: x has {xs.Length} readings but y has {ys.Length}.");
            }

            if (xs.Length == 0)
            {
                throw new JgsRuntimeException(line, col, "binscatter: there are no readings to count.");
            }

            // The bin count is the only thing that can stand between the data and the pairs, so a
            // third argument that is not a name is it — and that is the whole of the positional part.
            int optionStart = 2;
            (int Across, int Up)? bins = null;
            if (rest.Count > 2 && rest[2].Type != JgsType.String)
            {
                bins = BinCounts("binscatter: the bin count", rest[2], line, col);
                optionStart = 3;
            }

            BinScatterPlot plot = JG.BinScatter(xs, ys);
            if (bins is { } counted)
            {
                plot.NumBinsX = counted.Across;
                plot.NumBinsY = counted.Up;
            }

            BinScatterOptions(plot, rest, optionStart, line, col);
            return Handle(plot);
        });
    }

    private static void BinScatterOptions(
        BinScatterPlot plot, IReadOnlyList<JgsValue> args, int start, int line, int col)
    {
        if ((args.Count - start) % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, "binscatter: options come in 'Name', value pairs.");
        }

        for (int i = start; i < args.Count; i += 2)
        {
            string name = StrOf("binscatter", args[i], line, col);
            JgsValue value = args[i + 1];
            switch (name.ToLowerInvariant())
            {
                case "numbins":
                    (int across, int up) = BinCounts("binscatter: NumBins", value, line, col);
                    plot.NumBinsX = across;
                    plot.NumBinsY = up;
                    break;
                case "xlimits":
                    plot.XLimits = SpanOption("binscatter: XLimits", value, line, col);
                    break;
                case "ylimits":
                    plot.YLimits = SpanOption("binscatter: YLimits", value, line, col);
                    break;
                case "showemptybins":
                    plot.ShowEmptyBins =
                        JgsGraphicsProperties.ToOnOff("binscatter: ShowEmptyBins", value, line, col);
                    break;

                // There is no parallel pool to spread the counting over, so the option is read and
                // then ignored — a script written against one still runs, and gets the same answer.
                case "useparallel":
                    JgsGraphicsProperties.ToOnOff("binscatter: UseParallel", value, line, col);
                    break;
                case "colormap":
                    plot.Colormap = OptionColormap("binscatter", value, line, col);
                    break;
                case "displayname":
                    SetDisplayName(plot, StrOf("binscatter: DisplayName", value, line, col));
                    break;
                default:
                    throw new JgsRuntimeException(line, col,
                        $"binscatter has no option '{name}'. "
                        + $"It takes {string.Join(", ", BinScatterOptionNames)}.");
            }
        }
    }

    /// <summary>
    /// A bin count said as one number for both directions or as <c>[across up]</c> for each, which is
    /// how MATLAB says it in both the positional slot and the <c>NumBins</c> option.
    /// </summary>
    internal static (int Across, int Up) BinCounts(string what, JgsValue value, int line, int col)
    {
        double[] counts = ToDoubles(what, value, line, col);
        if (counts.Length is not (1 or 2))
        {
            throw new JgsRuntimeException(line, col,
                $"{what} is one number for both directions or two, such as [40 20].");
        }

        int across = OneCount(what, counts[0], line, col);
        return (across, counts.Length == 2 ? OneCount(what, counts[1], line, col) : across);
    }

    private static int OneCount(string what, double count, int line, int col)
    {
        if (!double.IsFinite(count) || count < 1 || count != System.Math.Floor(count))
        {
            throw new JgsRuntimeException(line, col, $"{what} is a whole number of bins, at least one.");
        }

        if (count > BinScatterPlot.MaxBinsPerSide)
        {
            throw new JgsRuntimeException(line, col,
                $"{what} is at most {BinScatterPlot.MaxBinsPerSide} in each direction.");
        }

        return (int)count;
    }

    /// <summary>The two increasing numbers a limits option is, as the span the bins fill.</summary>
    internal static DataRange SpanOption(string what, JgsValue value, int line, int col)
    {
        double[] ends = ToDoubles(what, value, line, col);
        if (ends.Length != 2 || !(ends[0] < ends[1]) || !double.IsFinite(ends[0]) || !double.IsFinite(ends[1]))
        {
            throw new JgsRuntimeException(line, col,
                $"{what} is two increasing numbers, such as [0 10].");
        }

        return new DataRange(ends[0], ends[1]);
    }
}
