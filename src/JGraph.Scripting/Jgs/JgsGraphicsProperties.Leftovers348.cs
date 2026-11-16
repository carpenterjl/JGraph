using System.Runtime.CompilerServices;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The graphics leftovers of ADR 0376 (open item 348, ADR 0382): a histogram's colour words, the
/// shape <c>Children</c> reads in, and the hooks the other parts hang on the property table.
/// </summary>
internal static partial class JgsGraphicsProperties
{
    /// <summary>
    /// The last word of the property table, after every block has put its names (open item 348, ADR
    /// 0382): each override wraps the entry the earlier blocks made and keeps it for the JGS dialect.
    /// </summary>
    private static void AddLeftovers348(Type type, IDictionary<string, GraphicsProperty> table)
    {
        if (typeof(HistogramPlot).IsAssignableFrom(type)
            && table.ContainsKey("FaceColor") && table.ContainsKey("EdgeColor") && table.ContainsKey("DisplayStyle"))
        {
            AddHistogramWords348(table);
        }

        if (type == typeof(TiledLayoutModel))
        {
            AddLayoutTextHandles348(table);
        }

        if (typeof(Line3DPlot).IsAssignableFrom(type))
        {
            if (!table.ContainsKey("SourceTable"))
            {
                AddSourceTable(table);
            }

            AddChannelVariables348(table, ["XVariable", "YVariable", "ZVariable"]);
            RefuseDataUnderVariables348(table, [("XData", "XVariable"), ("YData", "YVariable"), ("ZData", "ZVariable")]);
        }

        if (typeof(LinePlot).IsAssignableFrom(type) && table.ContainsKey("XVariable") && table.ContainsKey("YVariable"))
        {
            FollowChannelNames348(table, "XVariable");
            FollowChannelNames348(table, "YVariable");
            RefuseDataUnderVariables348(table, [("XData", "XVariable"), ("YData", "YVariable")]);
        }
    }

    // --- a tiled layout's text ------------------------------------------------------------------------

    private const string LayoutTextClass348 = "matlab.graphics.layout.Text";

    private static readonly string[] LayoutTextRoles348 = ["Title", "Subtitle", "XLabel", "YLabel"];

    /// <summary>
    /// A tiled layout's <c>Title</c>, <c>Subtitle</c>, <c>XLabel</c> and <c>YLabel</c> in a MATLAB-dialect
    /// script: its <see cref="JgsLayoutText"/>, which a value written whole is refused for in R2025b's
    /// words (open item 348, ADR 0382; probe p8eb). The JGS dialect reads and writes the words.
    /// </summary>
    private static void AddLayoutTextHandles348(IDictionary<string, GraphicsProperty> table)
    {
        foreach (string role in LayoutTextRoles348)
        {
            if (!table.TryGetValue(role, out GraphicsProperty? old))
            {
                continue;
            }

            string spelling = role;
            table[role] = new GraphicsProperty(old.Name,
                entry => JgsRunningDialect.ThreadIsMatlab
                    ? JgsHandleRegistry.For(JgsLayoutText.For((TiledLayoutModel)entry.Target, spelling))
                    : old.Read(entry),
                (entry, value, line, col) =>
                {
                    if (JgsRunningDialect.ThreadIsMatlab
                        && !(JgsHandleRegistry.TryGet(value, out JgsHandleEntry? named) && named.Target is JgsLayoutText))
                    {
                        throw new JgsRuntimeException(line, col, "MATLAB:class:RequireClass",
                            $"Error setting property '{spelling}' of class 'TiledChartLayout': Value must be '{LayoutTextClass348}'.");
                    }

                    if (JgsRunningDialect.ThreadIsMatlab)
                    {
                        return; // the layout's own text again: nothing changes
                    }

                    old.Write!(entry, value, line, col);
                })
            {
                Listed = old.Listed, Words = old.Words, OnlyWhen = old.OnlyWhen,
            };
        }
    }

    /// <summary>
    /// The surface of a tiled layout's text, <see cref="JgsLayoutText"/> (open item 348, ADR 0382):
    /// R2025b's fourteen names of <c>matlab.graphics.layout.Text</c> and its hidden <c>FontSizeMode</c>
    /// (probes p8eb, p8ed, p8ee). The words, size, weight, angle, interpreter and colour are the
    /// layout's own style and drawn; the angle, alignments, font name and visibility are kept on the
    /// object. The refusals are R2025b's sentences.
    /// </summary>
    private static void AddLayoutTextBlock348(IDictionary<string, GraphicsProperty> table)
    {
        static JgsLayoutText Text(JgsHandleEntry entry) => (JgsLayoutText)entry.Target;

        Put(table, "String",
            entry => Text(entry).ReadString(),
            (entry, value, line, col) => Text(entry).WriteString(value, line, col));
        Put(table, "FontName",
            entry => JgsValue.Str(Text(entry).FontName ?? "Helvetica"),
            (entry, value, line, col) =>
            {
                string name = JgsBuiltins.StrOf("FontName", value, line, col);
                Text(entry).FontName = name;
                Text(entry).Style = Text(entry).Style.WithFamily(name);
            });
        Put(table, "FontSize",
            entry => JgsValue.Number(Text(entry).Style.FontSize),
            (entry, value, line, col) =>
            {
                double size = value.Type is JgsType.Number or JgsType.Bool ? value.AsNumber : Numbers("FontSize", value, 1, line, col)[0];
                if (!(size > 0) || !double.IsFinite(size))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:datatypes:OutsideRangeFiniteGreater0",
                        "Error setting property 'FontSize' of class 'Text':\nValue should be a finite number greater than 0");
                }

                Text(entry).Style = Text(entry).Style.WithSize(size);
                Text(entry).FontSizeManual = true;
            });
        Put(table, "FontAngle",
            entry => JgsValue.Str(Text(entry).Style.Italic ? "italic" : "normal"),
            (entry, value, line, col) => Text(entry).Style = Text(entry).Style.WithItalic(
                LegendTextWord("FontAngle", value, ["normal", "italic"], line, col) == "italic"));
        Put(table, "FontWeight",
            entry => JgsValue.Str(Text(entry).Style.Bold ? "bold" : "normal"),
            (entry, value, line, col) => Text(entry).Style = Text(entry).Style.WithBold(
                LegendTextWord("FontWeight", value, ["normal", "bold"], line, col) == "bold"));
        Put(table, "Rotation",
            entry => JgsValue.Number(Text(entry).Rotation),
            (entry, value, line, col) => Text(entry).Rotation = Numbers("Rotation", value, 1, line, col)[0]);
        Put(table, "HorizontalAlignment",
            entry => JgsValue.Str(Text(entry).HorizontalAlignment),
            (entry, value, line, col) => Text(entry).HorizontalAlignment =
                LegendTextWord("HorizontalAlignment", value, ["left", "center", "right"], line, col));
        Put(table, "VerticalAlignment",
            entry => JgsValue.Str(Text(entry).VerticalAlignment),
            (entry, value, line, col) => Text(entry).VerticalAlignment =
                LegendTextWord("VerticalAlignment", value, ["baseline", "top", "cap", "middle", "bottom"], line, col));
        Put(table, "Interpreter",
            entry => JgsValue.Str(JgsBuiltins.InterpreterWord(Text(entry).Style.Interpreter)),
            (entry, value, line, col) => Text(entry).Style = Text(entry).Style.WithInterpreter(
                LegendTextWord("Interpreter", value, ["none", "tex", "latex"], line, col) switch
                {
                    "none" => TextInterpreter.None,
                    "latex" => TextInterpreter.Latex,
                    _ => TextInterpreter.Tex,
                }));
        Put(table, "Color",
            entry => ColorRow(Text(entry).Style.Color),
            (entry, value, line, col) => Text(entry).Style = Text(entry).Style.WithColor(
                JgsBuiltins.OptionColor(value, line, col, "Color")));

        // It holds nothing, and it belongs to its layout for good.
        Put(table, "Children",
            static _ => JgsMatrix.FromColumnMajor([], 0, 0),
            (entry, value, line, col) =>
            {
                if (value.Type == JgsType.Array && value.ArrayLength == 0)
                {
                    return;
                }

                throw new JgsRuntimeException(line, col, "A tiled layout's text has no children to set.");
            });
        Put(table, "Parent",
            entry => JgsHandleRegistry.For(Text(entry).Layout),
            static (_, _, line, col) => throw new JgsRuntimeException(line, col, "MATLAB:hg:TryingToReparentConnectedObject",
                "Trying to reparent an already connected internal component object."));
        Put(table, "Visible",
            entry => OnOff(Text(entry).Shown),
            (entry, value, line, col) => Text(entry).Show(ToOnOff("Visible", value, line, col)));
        Put(table, "HandleVisibility",
            entry => JgsValue.Str(entry.HandleVisibility),
            (entry, value, line, col) => entry.HandleVisibility = value.Type is JgsType.Bool or JgsType.Number
                ? (value.IsTruthy ? "on" : "off")
                : HandleVisibilityWords[Word(entry, "HandleVisibility", value, HandleVisibilityWords, HandleVisibilityWords, line, col)]);

        // The one mode a script reads in the probes, hidden as R2025b's is.
        table["FontSizeMode"] = new GraphicsProperty("FontSizeMode",
            entry => AutoManual(Text(entry).FontSizeManual),
            (entry, value, line, col) => Text(entry).FontSizeManual =
                LegendTextWord("FontSizeMode", value, ["auto", "manual"], line, col) == "manual")
        {
            Listed = false,
        };
    }

    /// <summary>
    /// A MATLAB-dialect tiled layout's text drawn in R2025b's defaults (probe p8ee): the title 13 points,
    /// the others 12, none bold, all in 33/255 grey. The JGS dialect keeps the model's own.
    /// </summary>
    internal static void MatlabLayoutTextStyles348(TiledLayoutModel layout)
    {
        if (!JgsRunningDialect.ThreadIsMatlab)
        {
            return;
        }

        Color grey = JgsBuiltins.MatlabFactoryEdge;
        layout.TitleStyle = layout.TitleStyle.WithSize(13).WithBold(false).WithColor(grey);
        layout.SubtitleStyle = layout.SubtitleStyle.WithSize(12).WithBold(false).WithColor(grey);
        layout.XLabelStyle = layout.XLabelStyle.WithSize(12).WithBold(false).WithColor(grey);
        layout.YLabelStyle = layout.YLabelStyle.WithSize(12).WithBold(false).WithColor(grey);
    }

    /// <summary>
    /// Whether a name is one a text object answers to, which <c>title(t, ..., name, value)</c> sets on
    /// the layout's text or refuses as R2025b's <c>set</c> does; any other name is
    /// <c>MATLAB:hg:InvalidProperty</c> (probe p8ed: <c>'Position'</c> and <c>'Bogus'</c>).
    /// </summary>
    internal static bool IsTextPropertyName348(string name) =>
        TableFor(typeof(JgsLayoutText)).ContainsKey(name) || TableFor(typeof(JgsTextLabel)).ContainsKey(name);

    // --- a tiled layout in a panel or a tab --------------------------------------------------------------

    private static readonly ConditionalWeakTable<UiContainerModel, TiledLayoutModel> ContainerLayouts348 = new();
    private static readonly ConditionalWeakTable<TiledLayoutModel, UiContainerModel> LayoutContainers348 = new();

    /// <summary>The layout a panel or a tab holds, or null (open item 348, ADR 0382).</summary>
    internal static TiledLayoutModel? LayoutIn348(UiContainerModel container) =>
        ContainerLayouts348.TryGetValue(container, out TiledLayoutModel? layout) ? layout : null;

    /// <summary>The panel or tab a layout stands in, or null for a figure's layout.</summary>
    internal static UiContainerModel? ContainerOfLayout348(TiledLayoutModel layout) =>
        LayoutContainers348.TryGetValue(layout, out UiContainerModel? container) ? container : null;

    /// <summary>Stands a layout in a panel or a tab: its box is the container's whole inner box.</summary>
    internal static void PlaceLayoutIn348(TiledLayoutModel layout, UiContainerModel container)
    {
        ContainerLayouts348.AddOrUpdate(container, layout);
        LayoutContainers348.AddOrUpdate(layout, container);
    }

    /// <summary>A deleted layout leaves its panel or tab.</summary>
    internal static void ForgetContainerLayout348(TiledLayoutModel layout)
    {
        if (ContainerOfLayout348(layout) is { } container)
        {
            LayoutContainers348.Remove(layout);
            if (ReferenceEquals(LayoutIn348(container), layout))
            {
                ContainerLayouts348.Remove(container);
            }
        }
    }

    /// <summary>
    /// Adds to a sweep's live set every tiled layout of a live figure or a live panel or tab, and the
    /// text objects a script reached on them (open item 348, ADR 0382): a JGS-dialect layout stands in
    /// no tree walk, so the first sweep after it was made reaped its handle and <c>nexttile(t)</c> then
    /// read the dead number as a tile location.
    /// </summary>
    internal static void KeepLayoutsAlive348(HashSet<GraphObject> live)
    {
        var layouts = new List<TiledLayoutModel>();
        foreach (GraphObject alive in live)
        {
            if (alive is FigureModel { TiledLayout: { } layout })
            {
                layouts.Add(layout);
            }
            else if (alive is UiContainerModel container && LayoutIn348(container) is { } held)
            {
                layouts.Add(held);
            }
            else if (alive is TiledLayoutModel listed)
            {
                layouts.Add(listed);
            }
        }

        foreach (TiledLayoutModel layout in layouts)
        {
            live.Add(layout);
            foreach (JgsLayoutText text in JgsLayoutText.Existing(layout))
            {
                live.Add(text);
            }
        }
    }

    /// <summary>The layout a panel's or a tab's axes is a tile of, or null.</summary>
    private static TiledLayoutModel? ContainerLayoutHolding348(AxesModel axes) =>
        axes.Container is { } container && LayoutIn348(container) is { } layout && layout.Tiles.Contains(axes)
            ? layout
            : null;

    /// <summary>
    /// A panel's or a tab's axes in a MATLAB-dialect script, with its tiled layout standing for its
    /// tiles where the container holds one (open item 348, ADR 0382; probe p8eb: <c>p.Children</c> is
    /// the layout).
    /// </summary>
    private static void AddContainerAxes348(List<GraphObject> children, UiContainerModel container)
    {
        IReadOnlyList<AxesModel> axes = container.ContainedAxes();
        if (!JgsRunningDialect.ThreadIsMatlab || LayoutIn348(container) is not { } layout)
        {
            AddAxesAndFurniture(children, axes);
            return;
        }

        var free = new List<AxesModel>();
        bool placed = false;
        foreach (AxesModel one in axes)
        {
            if (layout.Tiles.Contains(one))
            {
                continue;
            }

            if (!placed && one.CreationOrder > layout.CreationOrder)
            {
                AddAxesAndFurniture(children, free);
                free.Clear();
                children.Add(layout);
                placed = true;
            }

            free.Add(one);
        }

        AddAxesAndFurniture(children, free);
        if (!placed)
        {
            children.Add(layout);
        }
    }

    // --- a table's lines -----------------------------------------------------------------------------

    /// <summary>
    /// A <c>plot3(tbl, ...)</c> line's channels in the MATLAB dialect, as a <c>plot(tbl, ...)</c> line's
    /// are (open item 348, ADR 0382; probe p8ea2): the variable behind each, a number reading back as
    /// the number; writing one redraws the line and its name follows. A line drawn from arrays reads
    /// them empty.
    /// </summary>
    private static void AddChannelVariables348(IDictionary<string, GraphicsProperty> table, string[] channels)
    {
        static bool Matlab(GraphObject _) => JgsRunningDialect.ThreadIsMatlab;
        foreach (string channel in channels)
        {
            string spelling = channel;
            table[spelling] = new GraphicsProperty(spelling,
                entry => entry.ScatterSource is { } source
                    ? source.Given is { } given && given.TryGetValue(spelling, out JgsValue? kept) ? kept
                        : JgsValue.Str(ChannelName348(source, spelling))
                    : JgsValue.Str(string.Empty),
                (entry, value, line, col) =>
                {
                    ScatterSource source = RequireScatterSource(entry, spelling, line, col);
                    JGraph.Data.Table held = source.Table.AsTable;
                    string name;
                    if (JgsBuiltins.NumberedVariable(held, value) is { } number)
                    {
                        name = held.ColumnNames[number - 1];
                        source.Remember(spelling, JgsValue.Number(number));
                    }
                    else
                    {
                        name = JgsBuiltins.StrOf(spelling, value, line, col);
                        source.Remember(spelling, null);
                    }

                    switch (spelling)
                    {
                        case "XVariable": source.XVariable = name; break;
                        case "YVariable": source.YVariable = name; break;
                        default: source.ZVariable = name; break;
                    }

                    JgsBuiltins.ReplotFromSource(entry, line, col);
                    JgsBuiltins.RenameTableLine348(entry);
                })
            { OnlyWhen = Matlab };
        }
    }

    private static string ChannelName348(ScatterSource source, string channel) => channel switch
    {
        "XVariable" => source.XVariable,
        "YVariable" => source.YVariable,
        _ => source.ZVariable,
    };

    /// <summary>A <c>plot(tbl, ...)</c> line's channel written: its name follows when its channels vary (probe p8ec).</summary>
    private static void FollowChannelNames348(IDictionary<string, GraphicsProperty> table, string channel)
    {
        GraphicsProperty old = table[channel];
        if (old.Write is not { } write)
        {
            return;
        }

        table[channel] = new GraphicsProperty(old.Name, old.Read, (entry, value, line, col) =>
        {
            write(entry, value, line, col);
            JgsBuiltins.RenameTableLine348(entry);
        })
        {
            Listed = old.Listed, Words = old.Words, OnlyWhen = old.OnlyWhen,
        };
    }

    /// <summary>
    /// A table line's coordinates are its variables': writing one while its variable is named is R2025b's
    /// <c>TableToMatrixNotSupported</c> in a MATLAB-dialect script (probes p8ea2, p8ec).
    /// </summary>
    private static void RefuseDataUnderVariables348(IDictionary<string, GraphicsProperty> table, (string Data, string Variable)[] pairs)
    {
        foreach ((string data, string variable) in pairs)
        {
            if (!table.TryGetValue(data, out GraphicsProperty? old) || old.Write is not { } write)
            {
                continue;
            }

            string dataName = data;
            string variableName = variable;
            table[data] = new GraphicsProperty(old.Name, old.Read, (entry, value, line, col) =>
            {
                if (JgsRunningDialect.ThreadIsMatlab && entry.ScatterSource is { } source
                    && ChannelName348(source, variableName).Length > 0)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:graphics:chart:TableToMatrixNotSupported",
                        $"Setting the '{dataName}' property while the '{variableName}' property is non-empty is not supported.");
                }

                write(entry, value, line, col);
            })
            {
                Listed = old.Listed, Words = old.Words, OnlyWhen = old.OnlyWhen,
            };
        }
    }

    // --- a histogram's FaceColor and EdgeColor --------------------------------------------------------

    /// <summary>
    /// The edge a MATLAB-dialect histogram keeps for the display style it is not showing: R2025b holds
    /// one edge for the bars and one for the stairs, and switching <c>DisplayStyle</c> shows the other
    /// (probe p8ec: bars edged green, shown as stairs, read <c>'auto'</c>, and back as bars green again).
    /// A null colour is <c>'auto'</c>, a transparent one <c>'none'</c>.
    /// </summary>
    private sealed class HistogramOtherEdge348
    {
        public Color? Edge { get; set; }
    }

    private static readonly ConditionalWeakTable<HistogramPlot, HistogramOtherEdge348> HistogramOtherEdges348 = new();

    /// <summary>
    /// A histogram's <c>FaceColor</c>, <c>EdgeColor</c> and <c>DisplayStyle</c> in the MATLAB dialect
    /// (open item 348, ADR 0382; probes p8ea, p8ec). An unchosen face reads <c>'auto'</c> where it used
    /// to read the series colour, and a stairs histogram's face reads <c>'none'</c> and refuses anything
    /// else in R2025b's words; an unchosen edge reads <c>'auto'</c> (a stairs histogram's own), a
    /// written <c>'none'</c> is drawn as no edge (a transparent colour) and reads <c>'none'</c>; both
    /// take <c>'auto'</c> and <c>'none'</c> in any case and from two letters. The JGS dialect reads and
    /// writes as before.
    /// </summary>
    private static void AddHistogramWords348(IDictionary<string, GraphicsProperty> table)
    {
        GraphicsProperty oldFace = table["FaceColor"];
        GraphicsProperty oldEdge = table["EdgeColor"];
        GraphicsProperty oldStyle = table["DisplayStyle"];
        static HistogramPlot Histogram(JgsHandleEntry entry) => (HistogramPlot)entry.Target;

        table["FaceColor"] = new GraphicsProperty(oldFace.Name,
            entry =>
            {
                if (!JgsRunningDialect.ThreadIsMatlab)
                {
                    return oldFace.Read(entry);
                }

                HistogramPlot plot = Histogram(entry);
                return plot.DisplayStyle == HistogramDisplayStyle.Stairs ? JgsValue.Str("none")
                    : plot.FaceColor is { } face ? ColorRow(face)
                    : JgsValue.Str("auto");
            },
            (entry, value, line, col) =>
            {
                if (!JgsRunningDialect.ThreadIsMatlab)
                {
                    oldFace.Write!(entry, value, line, col);
                    return;
                }

                WriteHistogramFace348(entry, value, line, col);
            })
        {
            Listed = oldFace.Listed, Words = oldFace.Words, OnlyWhen = oldFace.OnlyWhen,
        };

        table["EdgeColor"] = new GraphicsProperty(oldEdge.Name,
            entry => JgsRunningDialect.ThreadIsMatlab
                ? HistogramEdgeValue348(Histogram(entry).EdgeColor)
                : oldEdge.Read(entry),
            (entry, value, line, col) =>
            {
                if (!JgsRunningDialect.ThreadIsMatlab)
                {
                    oldEdge.Write!(entry, value, line, col);
                    return;
                }

                Histogram(entry).EdgeColor = HistogramColourWord348(entry, "EdgeColor", value, line, col);
            })
        {
            Listed = oldEdge.Listed, Words = oldEdge.Words, OnlyWhen = oldEdge.OnlyWhen,
        };

        table["DisplayStyle"] = new GraphicsProperty(oldStyle.Name,
            oldStyle.Read,
            (entry, value, line, col) =>
            {
                HistogramPlot plot = Histogram(entry);
                HistogramDisplayStyle before = plot.DisplayStyle;
                oldStyle.Write!(entry, value, line, col);
                if (JgsRunningDialect.ThreadIsMatlab && plot.DisplayStyle != before)
                {
                    SwapHistogramEdge348(plot);
                }
            })
        {
            Listed = oldStyle.Listed, Words = oldStyle.Words, OnlyWhen = oldStyle.OnlyWhen,
        };
    }

    /// <summary>An edge as R2025b reads it: the colour, <c>'none'</c> (transparent) or <c>'auto'</c> (null).</summary>
    private static JgsValue HistogramEdgeValue348(Color? edge) =>
        edge is { } colour ? ColorRow(colour) : JgsValue.Str("auto");

    /// <summary>
    /// A MATLAB-dialect face written (open item 348): on a stairs histogram only <c>'none'</c> is taken,
    /// and it changes nothing; on bars <c>'auto'</c> is the series colour (null) and <c>'none'</c> no
    /// fill (transparent).
    /// </summary>
    internal static void WriteHistogramFace348(JgsHandleEntry entry, JgsValue value, int line, int col)
    {
        var plot = (HistogramPlot)entry.Target;
        Color? face = HistogramColourWord348(entry, "FaceColor", value, line, col);
        if (plot.DisplayStyle == HistogramDisplayStyle.Stairs)
        {
            if (face != Colors.Transparent)
            {
                throw HistogramStairsFaceRefusal348(line, col);
            }

            return;
        }

        plot.FaceColor = face;
    }

    /// <summary>R2025b's refusal of a stairs histogram's face (probe p8ec).</summary>
    internal static JgsRuntimeException HistogramStairsFaceRefusal348(int line, int col) =>
        new(line, col, "MATLAB:histogram:InvalidStairsFaceColor",
            "'FaceColor' value must be 'none' when 'DisplayStyle' value is 'stairs'.");

    /// <summary>
    /// A histogram colour written in the MATLAB dialect: <c>'auto'</c> is null, <c>'none'</c> transparent
    /// (both from two letters, in any case, as R2025b takes <c>'au'</c>), else a colour; a word that names
    /// no colour is R2025b's <c>RGBAColor:ParseError</c> naming <c>'auto'</c> and <c>'none'</c>.
    /// </summary>
    internal static Color? HistogramColourWord348(JgsHandleEntry entry, string property, JgsValue value, int line, int col)
    {
        if (JgsBuiltins.IsTextScalar(value))
        {
            string word = JgsBuiltins.TextOf(value).Trim().ToLowerInvariant();
            if (word.Length >= 2 && "auto".StartsWith(word, StringComparison.Ordinal))
            {
                return null;
            }

            if (word.Length >= 2 && "none".StartsWith(word, StringComparison.Ordinal))
            {
                return Colors.Transparent;
            }

            if (MarkerColourRefusal(entry, property, value, ["auto", "none"], line, col) is { } refused)
            {
                throw refused;
            }
        }

        return JgsBuiltins.OptionColor(value, line, col, property);
    }

    /// <summary>
    /// Switching a MATLAB-dialect histogram's display style: the edge shown goes to the style left, and
    /// the style shown takes the edge it kept - R2025b's 33/255 grey for bars it never had, <c>'auto'</c>
    /// for stairs (probe p8ec).
    /// </summary>
    private static void SwapHistogramEdge348(HistogramPlot plot)
    {
        HistogramOtherEdge348 other = HistogramOtherEdges348.GetValue(plot, static p => new HistogramOtherEdge348
        {
            Edge = p.DisplayStyle == HistogramDisplayStyle.Bar ? JgsBuiltins.MatlabFactoryEdge : null,
        });
        Color? shown = other.Edge;
        other.Edge = plot.EdgeColor;
        plot.EdgeColor = shown;
    }

    /// <summary>
    /// Marks a MATLAB-dialect histogram made with stairs: the bars it has not shown keep R2025b's grey
    /// edge for when it shows them (probe p8ec <c>sw_stairs_m_to_bar</c>).
    /// </summary>
    internal static void HistogramMadeAsStairs348(HistogramPlot plot) =>
        HistogramOtherEdges348.AddOrUpdate(plot, new HistogramOtherEdge348 { Edge = JgsBuiltins.MatlabFactoryEdge });

    // --- Children as a column -----------------------------------------------------------------------

    /// <summary>
    /// The handles a <c>Children</c> property lists, newest first: R2025b's column (0-by-0 when there is
    /// none) in a MATLAB-dialect script, this build's row in the JGS dialect (open item 348, ADR 0382;
    /// probe p8ea: an axes, a figure, a group, a transform, a panel, a tiled layout and the root).
    /// </summary>
    internal static JgsValue ChildrenList348(IReadOnlyList<GraphObject> objects)
    {
        JgsValue row = HandleRow(objects);
        if (!JgsRunningDialect.ThreadIsMatlab)
        {
            return row;
        }

        if (objects.Count == 0)
        {
            return JgsMatrix.FromColumnMajor([], 0, 0);
        }

        row.Reshape(objects.Count, 1);
        return row;
    }
}
