using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The M77 chart-primitive property wave. Three kinds of name live here: the block every plot object
/// answers to whatever it draws (its seat in the series cycle, its legend switch, its data tip rows,
/// where its numbers came from), the <c>*Mode</c> words, and the per-kind families the wave filled in.
/// <para>
/// The mode words follow M73's idiom exactly — a derived reading of state the model already carries,
/// never a second copy. Two of them cannot be derived from the value alone, because the automatic
/// value and a legal chosen value are the same thing (solid is both the default dash and a dash a
/// script may pick), and those two carry a flag the setter raises and the cycler lowers.
/// </para>
/// </summary>
internal static partial class JgsGraphicsProperties
{
    private static void AddPlotWave(Type type, IDictionary<string, GraphicsProperty> table)
    {
        AddPlotCommonBlock(table);

        if (typeof(XYPlot).IsAssignableFrom(type))
        {
            AddDataSources(table, "XDataSource", "YDataSource");
            AddImpliedXMode(table);
        }

        if (typeof(LinePlot).IsAssignableFrom(type))
        {
            AddLineModeBlock(table);
            AddFlatZData(table);
            AddDataSources(table, "ZDataSource");
            AddPolarChannels(table);
            AddLineBlock(table);
        }

        if (typeof(ScatterPlot).IsAssignableFrom(type))
        {
            AddScatterModeBlock(table);
            AddDataSources(table, "CDataSource", "SizeDataSource", "AlphaDataSource", "ZDataSource");
            AddPolarChannels(table);
            AddScatterAlphaBlock(table);
            AddScatterSourceBlock(table, spatial: false);
        }

        // A marker chart in space is its own class rather than a scatter with a height, so it takes
        // the source block by name — the table form is the same call with one more variable in it.
        if (typeof(Scatter3DPlot).IsAssignableFrom(type))
        {
            AddScatterSourceBlock(table, spatial: true);
        }

        // M86: plot3 and stem3 make the same MATLAB classes plot and stem make — Line and Stem — so
        // the two marker colours belong on them for the same reason and behave the same way,
        // 'none' included. Reflection alone would serve the names now that the model spells them
        // MATLAB's way, but not the word, and a colour property that refuses 'none' is the half-fix.
        if (typeof(Line3DPlot).IsAssignableFrom(type))
        {
            AddSpatialMarkerColours(table,
                entry => (Line3DPlot)entry.Target,
                static plot => plot.MarkerFaceColor,
                static (plot, ink) => plot.MarkerFaceColor = ink,
                static plot => plot.MarkerEdgeColor ?? plot.Color,
                static (plot, ink) => plot.MarkerEdgeColor = ink,
                "line");
        }

        if (typeof(Stem3DPlot).IsAssignableFrom(type))
        {
            AddSpatialMarkerColours(table,
                entry => (Stem3DPlot)entry.Target,
                static plot => plot.MarkerFaceColor,
                static (plot, ink) => plot.MarkerFaceColor = ink,
                static plot => plot.MarkerEdgeColor ?? plot.Color,
                static (plot, ink) => plot.MarkerEdgeColor = ink,
                "stem");
        }

        if (typeof(StemPlot).IsAssignableFrom(type))
        {
            AddColorMode(table,
                entry => ((StemPlot)entry.Target).Color,
                (entry, color) => ((StemPlot)entry.Target).Color = color);
            AddFlatZData(table);
            AddDataSources(table, "ZDataSource");
            AddStemBlock(table);
        }

        if (typeof(ErrorBarPlot).IsAssignableFrom(type))
        {
            AddColorMode(table,
                entry => ((ErrorBarPlot)entry.Target).Color,
                (entry, color) => ((ErrorBarPlot)entry.Target).Color = color);
            AddErrorBarBlock(table);
        }

        if (typeof(BarPlot).IsAssignableFrom(type))
        {
            AddFaceColorMode(table,
                entry => ((BarPlot)entry.Target).FillColor,
                (entry, color) => ((BarPlot)entry.Target).FillColor = color);
            AddBarBlock(table);
        }

        if (typeof(HistogramPlot).IsAssignableFrom(type))
        {
            AddHistogramBlock(table);
        }

        // The matrix charts feed four channels rather than two, and the same source machinery serves
        // them: refreshdata writes whatever channel a source names, and a grid is a channel.
        if (typeof(SurfacePlot).IsAssignableFrom(type) || typeof(ContourPlot).IsAssignableFrom(type))
        {
            AddDataSources(table, "XDataSource", "YDataSource", "ZDataSource", "CDataSource");
        }

        // An arrow field has six channels — a tail and a vector in each of three directions — and
        // every one of them can be fed from a variable.
        if (typeof(QuiverPlot).IsAssignableFrom(type))
        {
            AddDataSources(table,
                "XDataSource", "YDataSource", "ZDataSource",
                "UDataSource", "VDataSource", "WDataSource");
        }

        if (typeof(AreaPlot).IsAssignableFrom(type))
        {
            AddFaceColorMode(table,
                entry => ((AreaPlot)entry.Target).FaceColor,
                (entry, color) => ((AreaPlot)entry.Target).FaceColor = color);
            AddChosenFill(table, "FaceColor", "area",
                entry => ((AreaPlot)entry.Target).FaceColor,
                (entry, color) => ((AreaPlot)entry.Target).FaceColor = color,
                entry => ((AreaPlot)entry.Target).FaceAlpha > 0,
                (entry, shown) => ((AreaPlot)entry.Target).FaceAlpha = shown ? 1 : 0);
            AddBaseLineHandle(table, entry => ((AreaPlot)entry.Target).BaseLine);
            Put(table, "AlignVertexCenters",
                entry => OnOff(((AreaPlot)entry.Target).AlignVertexCenters),
                (entry, value, line, col) => ((AreaPlot)entry.Target).AlignVertexCenters =
                    ToOnOff("AlignVertexCenters", value, line, col));
        }
    }

    // --- The block every plot object answers to -------------------------------------------------

    private static void AddPlotCommonBlock(IDictionary<string, GraphicsProperty> table)
    {
        // MATLAB counts seats from one and has no word for "unseated"; a plot built through the raw
        // API never took a seat, and answers 0 rather than pretending to hold the first one.
        Put(table, "SeriesIndex",
            entry => JgsValue.Number(((PlotObject)entry.Target).SeriesIndex + 1),
            (entry, value, line, col) =>
            {
                double seat = Numbers("SeriesIndex", value, 1, line, col)[0];
                if (seat < 1 || !double.IsFinite(seat))
                {
                    throw new JgsRuntimeException(
                        line, col, $"SeriesIndex is a positive whole number, but got {seat:G6}.");
                }

                ((PlotObject)entry.Target).SeriesIndex = (int)System.Math.Round(seat) - 1;
            });

        // Both of these mint a handle on a small object the plot owns, which is the same two-step
        // MATLAB documents and the same shape as an axes handing back its ruler through ax.XAxis.
        Put(table, "Annotation", entry => JgsHandleRegistry.For(((PlotObject)entry.Target).Annotation));
        Put(table, "DataTipTemplate",
            entry => JgsHandleRegistry.For(((PlotObject)entry.Target).DataTipTemplate));
    }

    /// <summary>
    /// The variable a channel is fed from, which means nothing on its own: <c>refreshdata</c> is what
    /// reads these back. Unset answers empty, as MATLAB does, and the name of the channel each one
    /// drives is its own name with the word Source taken off.
    /// </summary>
    private static void AddDataSources(IDictionary<string, GraphicsProperty> table, params string[] names)
    {
        foreach (string name in names)
        {
            string spelling = name;
            Put(table, spelling,
                entry => JgsValue.Str(
                    entry.DataSources.TryGetValue(spelling, out string? variable) ? variable : string.Empty),
                (entry, value, line, col) =>
                {
                    string variable = JgsBuiltins.StrOf(spelling, value, line, col);
                    if (variable.Length == 0)
                    {
                        entry.DataSources.Remove(spelling);
                        return;
                    }

                    entry.DataSources[spelling] = variable;
                });
        }
    }

    /// <summary>The channel a source name feeds — <c>XDataSource</c> writes <c>XData</c>.</summary>
    internal static string ChannelOf(string sourceName) =>
        sourceName.EndsWith("Source", StringComparison.OrdinalIgnoreCase)
            ? sourceName[..^"Source".Length]
            : sourceName;





    /// <summary>
    /// The scatter's transparency channel and the spatial names a flat cloud answers about but does
    /// not have. AlphaData is the third per-point channel beside size and colour, and reads through
    /// the axes' alpha map exactly as CData reads through its colormap.
    /// </summary>
    private static void AddScatterAlphaBlock(IDictionary<string, GraphicsProperty> table)
    {
        static ScatterPlot Cloud(JgsHandleEntry entry) => (ScatterPlot)entry.Target;

        Put(table, "AlphaData",
            entry => Row(Cloud(entry).AlphaData ?? []),
            (entry, value, line, col) =>
            {
                double[] alphas = JgsBuiltins.ToDoubles("AlphaData", value, line, col);
                Cloud(entry).AlphaData = alphas.Length == 0 ? null : alphas;
            });

        AddWordProperty(table, "AlphaDataMapping",
            entry => Cloud(entry).AlphaDataMapping.ToString().ToLowerInvariant(),
            (entry, word, line, col) => Cloud(entry).AlphaDataMapping = word switch
            {
                "none" => AlphaMapping.None,
                "scaled" => AlphaMapping.Scaled,
                "direct" => AlphaMapping.Direct,
                _ => throw new JgsRuntimeException(line, col,
                    $"AlphaDataMapping is 'none', 'scaled' or 'direct', but got '{word}'."),
            });

        AddChannelMode(table, "AlphaDataMode", "AlphaData",
            entry => Cloud(entry).AlphaData,
            entry => Cloud(entry).AlphaData = null);

        AddFlatZData(table);

        // A flat cloud has no third direction to spread along either, and says so rather than
        // accepting a width that would never show.
        Put(table, "ZJitter", static _ => JgsValue.Str("none"),
            static (entry, value, line, col) =>
            {
                if (!JgsBuiltins.StrOf("ZJitter", value, line, col)
                        .Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    throw new JgsRuntimeException(line, col,
                        "ZJitter spreads a cloud along its third direction, and this scatter is flat "
                        + "— draw it with scatter3.");
                }
            });
        Put(table, "ZJitterWidth", static _ => JgsValue.Number(0),
            static (entry, value, line, col) =>
            {
                if (Numbers("ZJitterWidth", value, 1, line, col)[0] != 0)
                {
                    throw new JgsRuntimeException(line, col,
                        "ZJitterWidth is the spread along a third direction this scatter does not have "
                        + "— draw it with scatter3.");
                }
            });
    }

    // --- Lines, stairs and error bars -----------------------------------------------------------

    /// <summary>
    /// The two marker colours on a chart in space, reading and writing exactly as their flat
    /// counterparts do — <c>'none'</c> is an unfilled marker, and an unset edge falls back to the
    /// series' own colour rather than answering nothing.
    /// </summary>
    private static void AddSpatialMarkerColours<T>(
        IDictionary<string, GraphicsProperty> table,
        Func<JgsHandleEntry, T> target,
        Func<T, Color?> readFill,
        Action<T, Color?> writeFill,
        Func<T, Color?> readEdge,
        Action<T, Color?> writeEdge,
        string what)
        where T : PlotObject
    {
        Put(table, "MarkerFaceColor",
            entry => readFill(target(entry)) is { } fill ? ColorRow(fill) : JgsValue.Str("none"),
            (entry, value, line, col) =>
                writeFill(target(entry), NoneOrColor(value, line, col, what)));

        Put(table, "MarkerEdgeColor",
            entry => ColorRow(readEdge(target(entry))
                ?? JgsBuiltins.PaletteColorFor(target(entry))),
            (entry, value, line, col) =>
                writeEdge(target(entry), JgsBuiltins.OptionColor(value, line, col, what)));
    }

    /// <summary>
    /// The line properties MATLAB names that this build either spelled differently or drew without
    /// letting anyone choose. Two of them were already modelled and rendered and simply had no name
    /// a script could reach them by: the marker's own edge colour, and the corner join.
    /// </summary>
    private static void AddLineBlock(IDictionary<string, GraphicsProperty> table)
    {
        static LinePlot Line(JgsHandleEntry entry) => (LinePlot)entry.Target;

        // The model calls these what MATLAB calls them since M86; what this entry still adds over the
        // reflected one is the word — 'none' is an unfilled marker rather than a colour, and no
        // reflected colour property knows that.
        Put(table, "MarkerFaceColor",
            entry => Line(entry).MarkerFaceColor is { } fill ? ColorRow(fill) : JgsValue.Str("none"),
            (entry, value, line, col) => Line(entry).MarkerFaceColor = NoneOrColor(value, line, col, "line"));
        Put(table, "MarkerEdgeColor",
            entry => ColorRow(Line(entry).MarkerEdgeColor ?? Line(entry).Color
                ?? JgsBuiltins.PaletteColorFor(Line(entry))),
            (entry, value, line, col) => Line(entry).MarkerEdgeColor =
                JgsBuiltins.OptionColor(value, line, col, "line"));

        // Counted from one at the surface and from zero underneath, like every other index here.
        Put(table, "MarkerIndices",
            entry => Row([.. (Line(entry).MarkerIndices ?? []).Select(static i => (double)(i + 1))]),
            (entry, value, line, col) =>
            {
                double[] wanted = JgsBuiltins.ToDoubles("MarkerIndices", value, line, col);
                if (wanted.Length == 0)
                {
                    Line(entry).MarkerIndices = null;
                    return;
                }

                var indices = new int[wanted.Length];
                for (int i = 0; i < indices.Length; i++)
                {
                    if (!(wanted[i] >= 1) || !double.IsFinite(wanted[i]))
                    {
                        throw new JgsRuntimeException(line, col,
                            $"MarkerIndices counts samples from one, but got {wanted[i]:G6}.");
                    }

                    indices[i] = (int)System.Math.Round(wanted[i]) - 1;
                }

                Line(entry).MarkerIndices = indices;
            });

        AddWordProperty(table, "LineJoin",
            entry => Line(entry).LineJoin switch
            {
                LineJoin.Bevel => "chamfer",
                LineJoin.Round => "round",
                _ => "miter",
            },
            (entry, word, line, col) => Line(entry).LineJoin = word switch
            {
                "chamfer" => LineJoin.Bevel,
                "round" => LineJoin.Round,
                "miter" => LineJoin.Miter,
                _ => throw new JgsRuntimeException(line, col,
                    $"LineJoin is 'chamfer', 'miter' or 'round', but got '{word}'."),
            });

        Put(table, "AlignVertexCenters",
            entry => OnOff(Line(entry).AlignVertexCenters),
            (entry, value, line, col) => Line(entry).AlignVertexCenters =
                ToOnOff("AlignVertexCenters", value, line, col));
    }

    /// <summary>
    /// The four reaches of an error bar under all six of MATLAB's names, plus the appearance an
    /// error bar shares with a line. The vertical pair could be read and not written before this
    /// wave, because they arrived as constructor arguments and stayed there.
    /// </summary>
    private static void AddErrorBarBlock(IDictionary<string, GraphicsProperty> table)
    {
        static ErrorBarPlot Bars(JgsHandleEntry entry) => (ErrorBarPlot)entry.Target;

        // LData and YNegativeDelta are two names for one reach, as they are in MATLAB.
        foreach (string name in new[] { "LData", "YNegativeDelta" })
        {
            string spelling = name;
            Put(table, spelling,
                entry => Row(Bars(entry).ErrorNeg),
                (entry, value, line, col) => Bars(entry).ErrorNeg =
                    JgsBuiltins.ToDoubles(spelling, value, line, col));
        }

        foreach (string name in new[] { "UData", "YPositiveDelta" })
        {
            string spelling = name;
            Put(table, spelling,
                entry => Row(Bars(entry).ErrorPos),
                (entry, value, line, col) => Bars(entry).ErrorPos =
                    JgsBuiltins.ToDoubles(spelling, value, line, col));
        }

        Put(table, "XNegativeDelta",
            entry => Row(Bars(entry).ErrorLeft ?? []),
            (entry, value, line, col) => Bars(entry).ErrorLeft =
                JgsBuiltins.ToDoubles("XNegativeDelta", value, line, col));
        Put(table, "XPositiveDelta",
            entry => Row(Bars(entry).ErrorRight ?? []),
            (entry, value, line, col) => Bars(entry).ErrorRight =
                JgsBuiltins.ToDoubles("XPositiveDelta", value, line, col));

        AddDataSources(table,
            "LDataSource", "UDataSource",
            "XNegativeDeltaSource", "XPositiveDeltaSource",
            "YNegativeDeltaSource", "YPositiveDeltaSource");

        Put(table, "LineStyle",
            entry => JgsValue.Str(JgsBuiltins.DashWord(Bars(entry).DashStyle)),
            (entry, value, line, col) =>
            {
                ErrorBarPlot plot = Bars(entry);
                plot.DashStyle = JgsBuiltins.ParseDashWord(
                    JgsBuiltins.StrOf("LineStyle", value, line, col), plot.DashStyle);
            });

        // As on a stem, the marker reads back as MATLAB spells it rather than as the enum is named.
        Put(table, "Marker",
            entry => JgsValue.Str(JgsBuiltins.MarkerWord(Bars(entry).Marker)),
            (entry, value, line, col) =>
            {
                ErrorBarPlot plot = Bars(entry);
                plot.Marker = JgsBuiltins.ParseMarkerWord(
                    JgsBuiltins.StrOf("Marker", value, line, col), plot.Marker);
            });

        Put(table, "MarkerFaceColor",
            entry => Bars(entry).MarkerFaceColor is { } fill ? ColorRow(fill) : JgsValue.Str("none"),
            (entry, value, line, col) => Bars(entry).MarkerFaceColor =
                NoneOrColor(value, line, col, "errorbar"));
        Put(table, "MarkerEdgeColor",
            entry => ColorRow(Bars(entry).MarkerEdgeColor ?? Bars(entry).Color
                ?? JgsBuiltins.PaletteColorFor(Bars(entry))),
            (entry, value, line, col) => Bars(entry).MarkerEdgeColor =
                JgsBuiltins.OptionColor(value, line, col, "errorbar"));

        AddNullableMode(table, "LineStyleMode",
            static entry => Bars(entry).LineStyleManual,
            static entry => Bars(entry).LineStyleManual = true,
            static entry =>
            {
                Bars(entry).DashStyle = DashStyle.Solid;
                Bars(entry).LineStyleManual = false;
            });
        AddNullableMode(table, "MarkerMode",
            static entry => Bars(entry).MarkerManual,
            static entry => Bars(entry).MarkerManual = true,
            static entry => Bars(entry).MarkerManual = false);

        Put(table, "AlignVertexCenters", static _ => OnOff(false),
            static (_, value, line, col) =>
            {
                if (ToOnOff("AlignVertexCenters", value, line, col))
                {
                    throw new JgsRuntimeException(line, col,
                        "AlignVertexCenters snaps a line to pixel centres, and an error bar's "
                        + "whiskers are drawn in pixels already.");
                }
            });
    }

    /// <summary>A property whose value is one of a few words.</summary>
    private static void AddWordProperty(
        IDictionary<string, GraphicsProperty> table,
        string name,
        Func<JgsHandleEntry, string> read,
        Action<JgsHandleEntry, string, int, int> write)
    {
        string spelling = name;
        Put(table, spelling,
            entry => JgsValue.Str(read(entry)),
            (entry, value, line, col) => write(
                entry, JgsBuiltins.StrOf(spelling, value, line, col).ToLowerInvariant(), line, col));
    }

    // --- Bars, stems and the line they stand on --------------------------------------------------

    /// <summary>The chart's own handle on the line it stands on.</summary>
    private static void AddBaseLineHandle(
        IDictionary<string, GraphicsProperty> table, Func<JgsHandleEntry, BaseLineModel> pick)
    {
        Func<JgsHandleEntry, BaseLineModel> chosen = pick;
        Put(table, "BaseLine", entry => JgsHandleRegistry.For(chosen(entry)));
    }

    /// <summary>
    /// What a bar chart is arranged like, what colour each bar is, and where each one ends. The
    /// layout is the interesting one: MATLAB lets a script switch a chart between grouped and
    /// stacked after it is drawn, which needs every series standing on the same positions to be
    /// re-arranged together — one series cannot be stacked on its own.
    /// </summary>
    private static void AddBarBlock(IDictionary<string, GraphicsProperty> table)
    {
        static BarPlot Bar(JgsHandleEntry entry) => (BarPlot)entry.Target;

        AddBaseLineHandle(table, entry => Bar(entry).BaseLine);

        // The bottom series of a stacked chart has no floor under it, so the arrangement is a
        // question about the whole set rather than about one bar series.
        Put(table, "BarLayout",
            entry => JgsValue.Str(
                AxesExtensions.BarSiblingsOf(Bar(entry)).Any(static bar => bar.LowerEdge is not null)
                    ? "stacked"
                    : "grouped"),
            (entry, value, line, col) =>
            {
                string word = JgsBuiltins.StrOf("BarLayout", value, line, col).ToLowerInvariant();
                bool stacked = word switch
                {
                    "stacked" => true,
                    "grouped" => false,
                    _ => throw new JgsRuntimeException(line, col,
                        $"BarLayout is 'grouped' or 'stacked', but got '{word}'."),
                };

                AxesExtensions.LayOutBars(AxesExtensions.BarSiblingsOf(Bar(entry)), stacked);
            });

        // One colour per bar, given as an n-by-3 table of colours or as one colormap index each.
        Put(table, "CData",
            entry => Bar(entry).ColorData is { } colors
                ? ColorTable(colors)
                : JgsValue.Array([]),
            (entry, value, line, col) => Bar(entry).ColorData =
                BarColors(Bar(entry), value, line, col));

        // Where each bar begins and ends along the two directions. MATLAB answers the positions the
        // chart actually drew at, which for a grouped chart is not where the data said — the whole
        // point of asking is that the group shifted them.
        Put(table, "XEndPoints", entry => Row(EndPoints(Bar(entry), along: true)));
        Put(table, "YEndPoints", entry => Row(EndPoints(Bar(entry), along: false)));

        // The model carries this as its own number rather than in the edge colour's alpha byte, so
        // it round-trips exactly: a byte would answer 0.25098 to a script that wrote 0.25.
        Put(table, "EdgeAlpha",
            entry => JgsValue.Number(Bar(entry).EdgeAlpha),
            (entry, value, line, col) =>
            {
                double alpha = Numbers("EdgeAlpha", value, 1, line, col)[0];
                if (alpha is < 0 or > 1)
                {
                    throw new JgsRuntimeException(line, col,
                        $"EdgeAlpha is between 0 and 1, but got {alpha:G6}.");
                }

                Bar(entry).EdgeAlpha = alpha;
            });
    }

    /// <summary>Bar centres along the positions, or bar tops across them.</summary>
    private static double[] EndPoints(BarPlot bar, bool along)
    {
        var points = new double[bar.Data.Count];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = along == !bar.Horizontal ? bar.CenterAt(i) : bar.TopAt(i);
        }

        return points;
    }

    /// <summary>
    /// A colour per bar, from a table of them or from one index each into the axes' colormap. Empty
    /// puts the series back on one colour, which is what clearing CData means.
    /// </summary>
    private static Color[]? BarColors(BarPlot bar, JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.Array && value.ArrayLength == 0)
        {
            return null;
        }

        double[][] rows = JgsMatrix.ToRows("CData", value, line, col);
        if (rows.Length > 0 && rows[0].Length == 3)
        {
            var colors = new Color[rows.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                colors[i] = Color.FromScRgb(
                    System.Math.Clamp(rows[i][0], 0, 1),
                    System.Math.Clamp(rows[i][1], 0, 1),
                    System.Math.Clamp(rows[i][2], 0, 1));
            }

            return colors;
        }

        double[] indices = JgsBuiltins.ToDoubles("CData", value, line, col);
        Colormap map = bar.Axes?.Colormap ?? Colormap.Viridis;
        double low = double.PositiveInfinity;
        double high = double.NegativeInfinity;
        foreach (double index in indices)
        {
            if (double.IsFinite(index))
            {
                low = System.Math.Min(low, index);
                high = System.Math.Max(high, index);
            }
        }

        var sampled = new Color[indices.Length];
        for (int i = 0; i < indices.Length; i++)
        {
            sampled[i] = map.Sample(indices[i], low, high < low + 1e-12 ? low + 1 : high);
        }

        return sampled;
    }

    /// <summary>
    /// The stem properties MATLAB spells differently or that arrived with this wave: the dash the
    /// stems are drawn with, the two marker colours, and the line they stand on.
    /// </summary>
    private static void AddStemBlock(IDictionary<string, GraphicsProperty> table)
    {
        static StemPlot Stem(JgsHandleEntry entry) => (StemPlot)entry.Target;

        AddBaseLineHandle(table, entry => Stem(entry).BaseLine);

        Put(table, "LineStyle",
            entry => JgsValue.Str(JgsBuiltins.DashWord(Stem(entry).DashStyle)),
            (entry, value, line, col) =>
            {
                StemPlot plot = Stem(entry);
                plot.DashStyle = JgsBuiltins.ParseDashWord(
                    JgsBuiltins.StrOf("LineStyle", value, line, col), plot.DashStyle);
            });

        // The marker reads back as MATLAB spells it — 'o', not 'circle'. A stem had no Marker alias
        // of its own before this wave, so the raw enum word was what a script saw.
        Put(table, "Marker",
            entry => JgsValue.Str(JgsBuiltins.MarkerWord(Stem(entry).Marker)),
            (entry, value, line, col) =>
            {
                StemPlot plot = Stem(entry);
                plot.Marker = JgsBuiltins.ParseMarkerWord(
                    JgsBuiltins.StrOf("Marker", value, line, col), plot.Marker);
            });

        Put(table, "MarkerFaceColor",
            entry => Stem(entry).MarkerFaceColor is { } fill ? ColorRow(fill) : JgsValue.Str("none"),
            (entry, value, line, col) => Stem(entry).MarkerFaceColor =
                value.Type == JgsType.String
                && value.AsString.Equals("none", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : JgsBuiltins.OptionColor(value, line, col, "stem"));
        Put(table, "MarkerEdgeColor",
            entry => ColorRow(Stem(entry).MarkerEdgeColor ?? Stem(entry).Color
                ?? JgsBuiltins.PaletteColorFor(Stem(entry))),
            (entry, value, line, col) => Stem(entry).MarkerEdgeColor =
                JgsBuiltins.OptionColor(value, line, col, "stem"));

        // The same two flags a line carries: solid and none are both defaults and choices, so
        // neither can be read off the value.
        AddNullableMode(table, "LineStyleMode",
            static entry => Stem(entry).LineStyleManual,
            static entry => Stem(entry).LineStyleManual = true,
            static entry =>
            {
                Stem(entry).DashStyle = DashStyle.Solid;
                Stem(entry).LineStyleManual = false;
            });
        AddNullableMode(table, "MarkerMode",
            static entry => Stem(entry).MarkerManual,
            static entry => Stem(entry).MarkerManual = true,
            static entry => Stem(entry).MarkerManual = false);
    }

    // --- The histogram ---------------------------------------------------------------------------

    /// <summary>
    /// Everything a histogram is made of. None of it is a type reflection carries — they are arrays,
    /// a list of names, and one dash pattern MATLAB spells differently — and until this wave none of
    /// them could be reached at all, which is why a histogram answered to 21 of its 46 names.
    /// <para>
    /// The two counted names are worth reading carefully, because MATLAB uses one of them for the
    /// other thing: <c>Data</c> is the readings and <c>Values</c> is the bin heights. The model used
    /// to call the readings Values, which is the collision this block resolves.
    /// </para>
    /// </summary>
    private static void AddHistogramBlock(IDictionary<string, GraphicsProperty> table)
    {
        static HistogramPlot Histogram(JgsHandleEntry entry) => (HistogramPlot)entry.Target;

        // An unchosen fill is the series colour, not the absence of one. Left to reflection a
        // nullable colour reads back as 'none', which on a histogram says the bars are hollow — so
        // this is the same "say what was drawn" rule the bar chart's own Color follows.
        AddChosenFill(table, "FaceColor", "histogram",
            entry => Histogram(entry).FaceColor,
            (entry, color) => Histogram(entry).FaceColor = color,
            entry => Histogram(entry).FaceAlpha > 0,
            (entry, shown) => Histogram(entry).FaceAlpha = shown ? 1 : 0);

        Put(table, "Data",
            entry => Row(Histogram(entry).Data),
            (entry, value, line, col) =>
                Histogram(entry).Data = JgsBuiltins.ToDoubles("Data", value, line, col));
        Put(table, "BinEdges",
            entry => Row(Histogram(entry).BinEdges),
            (entry, value, line, col) =>
                Histogram(entry).BinEdges = JgsBuiltins.ToDoubles("BinEdges", value, line, col));
        Put(table, "BinCounts",
            entry => Row(Histogram(entry).BinCounts),
            (entry, value, line, col) =>
                Histogram(entry).BinCounts = JgsBuiltins.ToDoubles("BinCounts", value, line, col));
        Put(table, "BinLimits",
            entry => Row(Histogram(entry).BinLimits),
            (entry, value, line, col) =>
                Histogram(entry).BinLimits = Numbers("BinLimits", value, 2, line, col));
        Put(table, "BinMethod",
            entry => JgsValue.Str(Histogram(entry).BinMethod),
            (entry, value, line, col) => Histogram(entry).BinMethod = BinRule(
                JgsBuiltins.StrOf("BinMethod", value, line, col), line, col));

        // What is drawn, as opposed to what was counted: the same numbers only while the
        // normalization is 'count'.
        Put(table, "Values", entry => Row(Histogram(entry).BinHeights));

        Put(table, "LineStyle",
            entry => JgsValue.Str(JgsBuiltins.DashWord(Histogram(entry).LineStyle)),
            (entry, value, line, col) =>
            {
                HistogramPlot plot = Histogram(entry);
                plot.LineStyle = JgsBuiltins.ParseDashWord(
                    JgsBuiltins.StrOf("LineStyle", value, line, col), plot.LineStyle);
            });

        // The names a categorical histogram counted, and how many of them it shows.
        Put(table, "Categories",
            entry => JgsValue.Cell([.. (Histogram(entry).Categories ?? []).Select(JgsValue.Str)]),
            (entry, value, line, col) =>
                Histogram(entry).Categories = TextRows("Categories", value, line, col));
        Put(table, "DisplayOrder",
            entry => JgsValue.Str(Histogram(entry).DisplayOrder.ToString().ToLowerInvariant()),
            (entry, value, line, col) => Histogram(entry).DisplayOrder =
                JgsBuiltins.StrOf("DisplayOrder", value, line, col).ToLowerInvariant() switch
                {
                    "data" => CategoryDisplayOrder.Data,
                    "ascend" => CategoryDisplayOrder.Ascend,
                    "descend" => CategoryDisplayOrder.Descend,
                    var word => throw new JgsRuntimeException(line, col,
                        $"DisplayOrder is 'data', 'ascend' or 'descend', but got '{word}'."),
                });
        Put(table, "NumDisplayBins",
            entry => JgsValue.Number(Histogram(entry).NumDisplayBins == 0
                ? Histogram(entry).Categories?.Length ?? Histogram(entry).BinCounts.Length
                : Histogram(entry).NumDisplayBins),
            (entry, value, line, col) => Histogram(entry).NumDisplayBins =
                (int)System.Math.Round(Numbers("NumDisplayBins", value, 1, line, col)[0]));
        Put(table, "ShowOthers",
            entry => OnOff(Histogram(entry).ShowOthers),
            (entry, value, line, col) => Histogram(entry).ShowOthers =
                ToOnOff("ShowOthers", value, line, col));

        // Both counted things carry a mode saying whether anybody chose them. Neither is a second
        // copy: the limits remember whether they were given, and counts given outright are exactly
        // the histogram that has no readings left behind it.
        AddNullableMode(table, "BinLimitsMode",
            static entry => Histogram(entry).BinLimitsChosen,
            static entry => Histogram(entry).BinLimits = Histogram(entry).BinLimits,
            static entry => Histogram(entry).Data = Histogram(entry).Data);
        AddNullableMode(table, "BinCountsMode",
            static entry => Histogram(entry).SampleCount == 0,
            static entry => Histogram(entry).BinCounts = Histogram(entry).BinCounts,
            static entry => Histogram(entry).Data = Histogram(entry).Data);
    }


    /// <summary>
    /// A fill colour that is the series colour until somebody chooses one, and that answers
    /// <c>'none'</c> while the shape is unfilled. MATLAB's <c>'none'</c> is not a colour but the
    /// absence of a fill, and on a shape with no separate switch for that, a fully transparent fill
    /// is the same picture and the same question answered the same way.
    /// </summary>
    private static void AddChosenFill(
        IDictionary<string, GraphicsProperty> table,
        string name,
        string what,
        Func<JgsHandleEntry, Color?> read,
        Action<JgsHandleEntry, Color?> write,
        Func<JgsHandleEntry, bool> shown,
        Action<JgsHandleEntry, bool> show)
    {
        string spelling = name;
        string kind = what;
        Put(table, spelling,
            entry => shown(entry)
                ? ColorRow(read(entry) ?? JgsBuiltins.PaletteColorFor((PlotObject)entry.Target))
                : JgsValue.Str("none"),
            (entry, value, line, col) =>
            {
                if (value.Type == JgsType.String
                    && value.AsString.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    show(entry, false);
                    return;
                }

                if (value.Type == JgsType.String
                    && value.AsString.Equals("auto", StringComparison.OrdinalIgnoreCase))
                {
                    write(entry, null);
                    show(entry, true);
                    return;
                }

                write(entry, JgsBuiltins.OptionColor(value, line, col, kind));
                show(entry, true);
            });
    }

    /// <summary>The binning rules MATLAB names, refused by name when the word is not one of them.</summary>
    internal static string BinRule(string word, int line, int col) =>
        word.ToLowerInvariant() switch
        {
            "auto" or "scott" or "fd" or "integers" or "sturges" or "sqrt" => word.ToLowerInvariant(),
            _ => throw new JgsRuntimeException(line, col,
                $"BinMethod is 'auto', 'scott', 'fd', 'integers', 'sturges' or 'sqrt', but got '{word}'."),
        };

    // --- The mode words -------------------------------------------------------------------------

    /// <summary>
    /// A mode over a slot that is null while it is automatic. Writing <c>'manual'</c> freezes whatever
    /// is showing, and <c>'auto'</c> hands the slot back — the M73 idiom, unchanged.
    /// </summary>
    private static void AddNullableMode(
        IDictionary<string, GraphicsProperty> table,
        string name,
        Func<JgsHandleEntry, bool> chosen,
        Action<JgsHandleEntry> freeze,
        Action<JgsHandleEntry> release)
    {
        string spelling = name;
        Put(table, spelling,
            entry => AutoManual(chosen(entry)),
            (entry, value, line, col) =>
            {
                if (ToAutoManual(spelling, value, line, col))
                {
                    freeze(entry);
                }
                else
                {
                    release(entry);
                }
            });
    }

    private static void AddColorMode(
        IDictionary<string, GraphicsProperty> table,
        Func<JgsHandleEntry, Color?> read,
        Action<JgsHandleEntry, Color?> write) =>
        AddNullableMode(table, "ColorMode",
            entry => read(entry) is not null,
            entry => write(entry, read(entry) ?? JgsBuiltins.PaletteColorFor((PlotObject)entry.Target)),
            entry => write(entry, null));

    private static void AddFaceColorMode(
        IDictionary<string, GraphicsProperty> table,
        Func<JgsHandleEntry, Color?> read,
        Action<JgsHandleEntry, Color?> write) =>
        AddNullableMode(table, "FaceColorMode",
            entry => read(entry) is not null,
            entry => write(entry, read(entry) ?? JgsBuiltins.PaletteColorFor((PlotObject)entry.Target)),
            entry => write(entry, null));

    /// <summary>
    /// <c>XDataMode</c>: whether the positions were given or counted out. Releasing it counts them out
    /// again, which is what makes <c>set(h,'XDataMode','auto')</c> mean anything after a script has
    /// moved a series sideways.
    /// </summary>
    private static void AddImpliedXMode(IDictionary<string, GraphicsProperty> table) =>
        AddNullableMode(table, "XDataMode",
            entry => !((XYPlot)entry.Target).XImplied,
            entry => ((XYPlot)entry.Target).XImplied = false,
            entry =>
            {
                var plot = (XYPlot)entry.Target;
                int count = plot.Data.Count;
                var counted = new double[count];
                var kept = new double[count];
                for (int i = 0; i < count; i++)
                {
                    counted[i] = i + 1;
                    kept[i] = plot.Data.GetY(i);
                }

                plot.SetData(counted, kept);
                plot.XImplied = true;
            });

    /// <summary>
    /// A line's three appearance modes. Colour is the ordinary nullable slot; the dash and the marker
    /// are not, because their automatic values — solid and none — are also values a script may choose,
    /// so each carries the flag its setter raises.
    /// </summary>
    private static void AddLineModeBlock(IDictionary<string, GraphicsProperty> table)
    {
        AddColorMode(table,
            entry => ((LinePlot)entry.Target).Color,
            (entry, color) => ((LinePlot)entry.Target).Color = color);

        AddNullableMode(table, "LineStyleMode",
            entry => ((LinePlot)entry.Target).LineStyleManual,
            entry => ((LinePlot)entry.Target).LineStyleManual = true,
            entry =>
            {
                var plot = (LinePlot)entry.Target;
                plot.DashStyle = CycledStyle(plot).Dash;
                plot.LineStyleManual = false;
            });

        AddNullableMode(table, "MarkerMode",
            entry => ((LinePlot)entry.Target).MarkerManual,
            entry => ((LinePlot)entry.Target).MarkerManual = true,
            entry =>
            {
                var plot = (LinePlot)entry.Target;
                plot.Marker = CycledStyle(plot).Marker;
                plot.MarkerManual = false;
            });
    }

    /// <summary>
    /// What the axes' line-style cycle would hand this plot at its seat — the same div-and-mod law the
    /// axes' own <c>LineStyleOrderIndex</c> reads, so releasing a mode restores exactly what creation
    /// would have given.
    /// </summary>
    private static SeriesLineStyle CycledStyle(PlotObject plot)
    {
        if (plot.Axes is not { LineStyleOrder: { Count: > 0 } } axes || plot.SeriesIndex < 0)
        {
            return SeriesLineStyle.Solid;
        }

        IReadOnlyList<SeriesLineStyle> order = axes.LineStyleOrder!;
        int colors = (axes.ColorOrder ?? Colors.DefaultSeriesOrder).Count;
        return order[plot.SeriesIndex / System.Math.Max(1, colors) % order.Count];
    }

    /// <summary>
    /// The scatter channels that are automatic while they are empty. Freezing one that was never
    /// filled has nothing to freeze, and says so rather than answering a word it cannot keep.
    /// </summary>
    private static void AddScatterModeBlock(IDictionary<string, GraphicsProperty> table)
    {
        AddChannelMode(table, "CDataMode", "CData",
            entry => ((ScatterPlot)entry.Target).ColorData,
            entry => ((ScatterPlot)entry.Target).ColorData = null);
        AddChannelMode(table, "SizeDataMode", "SizeData",
            entry => ((ScatterPlot)entry.Target).SizeData,
            entry => ((ScatterPlot)entry.Target).SizeData = null);

        // A scatter is given both coordinates or neither, so the two answer together.
        AddNullableMode(table, "YDataMode",
            entry => !((XYPlot)entry.Target).XImplied,
            entry => ((XYPlot)entry.Target).XImplied = false,
            entry => ((XYPlot)entry.Target).XImplied = true);
    }

    private static void AddChannelMode(
        IDictionary<string, GraphicsProperty> table,
        string name,
        string channel,
        Func<JgsHandleEntry, IReadOnlyList<double>?> read,
        Action<JgsHandleEntry> clear)
    {
        string spelling = name;
        string filled = channel;
        AddNullableMode(table, spelling,
            entry => read(entry) is not null,
            entry =>
            {
                if (read(entry) is null)
                {
                    throw new JgsRuntimeException(0, 0,
                        $"{spelling} is 'manual' only once {filled} has been given values.");
                }
            },
            clear);
    }

    // --- The channels a flat chart answers about but does not have ------------------------------

    /// <summary>
    /// <c>ZData</c> on a chart drawn on flat paper. MATLAB answers empty for one and turns the object
    /// spatial when a script writes one; there is no such promotion here, so the write is refused by
    /// name rather than silently dropped.
    /// </summary>
    private static void AddFlatZData(IDictionary<string, GraphicsProperty> table)
    {
        Put(table, "ZData",
            _ => JgsValue.Array([]),
            (entry, value, line, col) =>
            {
                // Zeros are where a flat chart already sits, so writing them changes nothing and is
                // accepted — which is what lets rotate turn a 2-D line about the z axis and write
                // back the z it computed. It is measured against the chart's own size rather than
                // against nought, because a right angle's cosine is 6e-17 and not 0, and a rotation
                // that stayed in the plane must not be read as leaving it. A height that is really
                // not zero is the promotion to a spatial object, and that is what this build does
                // not do.
                double[] heights = JgsBuiltins.ToDoubles("ZData", value, line, col);
                double scale = FlatScale(entry.Target);
                if (Array.TrueForAll(heights, z => System.Math.Abs(z) <= 1e-9 * scale))
                {
                    return;
                }

                throw new JgsRuntimeException(line, col,
                    $"ZData turns a flat {TypeNameOf(entry.Target)} into a spatial one, which this build "
                    + "does not do — draw it with plot3, scatter3 or stem3 instead.");
            });

        AddNullableMode(table, "ZDataMode", static _ => false, static _ => { }, static _ => { });
    }


    /// <summary>
    /// How big the numbers on a chart are, which is the yardstick a "this is still zero" test needs.
    /// Never smaller than one, so a chart of tiny numbers does not make the test infinitely fussy.
    /// </summary>
    private static double FlatScale(GraphObject target)
    {
        if (target is not PlotObject plot)
        {
            return 1;
        }

        DataRange x = plot.GetXDataBounds();
        DataRange y = plot.GetYDataBounds();
        double largest = 1;
        foreach (double edge in new[] { x.Min, x.Max, y.Min, y.Max })
        {
            if (double.IsFinite(edge))
            {
                largest = System.Math.Max(largest, System.Math.Abs(edge));
            }
        }

        return largest;
    }

    /// <summary>
    /// The polar spellings of the two coordinates. On a circular axes θ and r <em>are</em> x and y —
    /// the same numbers read the other way, which is why <c>polarplot</c> and <c>plot</c> share one
    /// model — so these are that pair under the names a script working in polar already uses. On
    /// square paper they mean nothing and say so.
    /// </summary>
    private static void AddPolarChannels(IDictionary<string, GraphicsProperty> table)
    {
        AddPolarChannel(table, "ThetaData", x: true);
        AddPolarChannel(table, "RData", x: false);
        AddDataSources(table, "ThetaDataSource", "RDataSource");

        foreach (string name in new[] { "ThetaDataMode", "RDataMode" })
        {
            AddNullableMode(table, name,
                static entry => IsOnPolarAxes(entry) && !((XYPlot)entry.Target).XImplied,
                static entry => ((XYPlot)entry.Target).XImplied = false,
                static entry => ((XYPlot)entry.Target).XImplied = true);
        }
    }

    private static void AddPolarChannel(
        IDictionary<string, GraphicsProperty> table, string name, bool x)
    {
        string spelling = name;
        bool angle = x;
        Put(table, spelling,
            entry => IsOnPolarAxes(entry)
                ? SeriesRow((XYPlot)entry.Target, angle)
                : JgsValue.Array([]),
            (entry, value, line, col) =>
            {
                if (!IsOnPolarAxes(entry))
                {
                    throw new JgsRuntimeException(line, col,
                        $"{spelling} is the {(angle ? "angle" : "radius")} of a series on a polar axes, and "
                        + $"this {TypeNameOf(entry.Target)} is on square paper — write "
                        + $"{(angle ? "XData" : "YData")}.");
                }

                SetSeriesData(entry, value, angle, line, col);
            });
    }

    private static bool IsOnPolarAxes(JgsHandleEntry entry) =>
        entry.Target is PlotObject { Axes.IsPolar: true };

    // --- The small objects a plot owns ----------------------------------------------------------

    private static void AddPlotWaveNested(Type type, IDictionary<string, GraphicsProperty> table)
    {
        if (typeof(BaseLineModel).IsAssignableFrom(type))
        {
            Put(table, "LineStyle",
                entry => JgsValue.Str(JgsBuiltins.DashWord(((BaseLineModel)entry.Target).LineStyle)),
                (entry, value, line, col) =>
                {
                    var baseLine = (BaseLineModel)entry.Target;
                    baseLine.LineStyle = JgsBuiltins.ParseDashWord(
                        JgsBuiltins.StrOf("LineStyle", value, line, col), baseLine.LineStyle);
                });
            Put(table, "Color",
                entry => ColorRow(((BaseLineModel)entry.Target).Color ?? Colors.Black),
                (entry, value, line, col) => ((BaseLineModel)entry.Target).Color =
                    JgsBuiltins.OptionColor(value, line, col, "baseline"));
        }

        if (typeof(PlotAnnotationModel).IsAssignableFrom(type))
        {
            Put(table, "LegendInformation",
                entry => JgsHandleRegistry.For(((PlotAnnotationModel)entry.Target).LegendInformation));
        }

        if (typeof(DataTipTemplateModel).IsAssignableFrom(type))
        {
            Put(table, "DataTipRows",
                entry => JgsValue.Array([.. ((DataTipTemplateModel)entry.Target).DataTipRows
                    .Select(JgsHandleRegistry.For)]),
                (entry, value, line, col) =>
                    ((DataTipTemplateModel)entry.Target).SetRows(RowsOf(value, line, col)));
        }

        if (typeof(DataTipRowModel).IsAssignableFrom(type))
        {
            // One name over two slots: a row either names one of the plot's own channels or carries
            // its own numbers, and which of the two it is, is what was written.
            Put(table, "Value",
                entry =>
                {
                    var row = (DataTipRowModel)entry.Target;
                    return row.ValueSource.Length > 0
                        ? JgsValue.Str(row.ValueSource)
                        : Row(row.ValueData ?? []);
                },
                (entry, value, line, col) =>
                {
                    var row = (DataTipRowModel)entry.Target;
                    if (value.Type == JgsType.String)
                    {
                        row.ValueSource = value.AsString;
                        row.ValueData = null;
                        return;
                    }

                    row.ValueData = JgsBuiltins.ToDoubles("Value", value, line, col);
                    row.ValueSource = string.Empty;
                });
        }
    }

    /// <summary>The rows a handle or a row of handles names, refusing anything that is not one.</summary>
    private static List<DataTipRowModel> RowsOf(JgsValue value, int line, int col)
    {
        var rows = new List<DataTipRowModel>();
        int count = value.Type == JgsType.Array ? value.ArrayLength : 1;
        for (int i = 0; i < count; i++)
        {
            JgsValue element = value.Type == JgsType.Array ? value.ElementAt(i) : value;
            JgsHandleEntry entry = JgsHandleRegistry.Require(element, line, col);
            rows.Add(entry.Target as DataTipRowModel
                ?? throw new JgsRuntimeException(line, col,
                    "DataTipRows takes datatiptextrow objects, made by dataTipTextRow(label, value)."));
        }

        return rows;
    }
}
