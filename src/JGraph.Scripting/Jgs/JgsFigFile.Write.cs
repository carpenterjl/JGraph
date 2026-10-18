using System.IO;
using System.Text;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Objects.Annotations;
using JGraph.Scripting.MatFile;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Writing a MATLAB <c>.fig</c> (open item 77, ADR 0240). The file is a level-5 MAT-file holding two
/// variables: <c>hgS_070000</c>, the struct tree of type, handle, properties, children and special
/// that every MATLAB release reads (R2025b rebuilds a figure from it alone: probe <c>oi77_shapes</c>),
/// and <c>jgraph_document</c>, this build's own <c>.graph</c> document as UTF-8 bytes, which
/// <c>openfig</c> here reads back whole where MATLAB reads the tree and ignores the second variable.
/// <para>
/// The tree is written in the shapes R2025b's <c>hgsave</c> writes (<c>oi77_hand</c>): children in a
/// column, oldest first; a leaf's <c>children</c> and <c>special</c> 0-by-0 doubles; an axes'
/// <c>special</c> the places of its title and labels among its children; the type words of the
/// old graphics system (<c>graph2d.lineseries</c>, <c>specgraph.barseries</c>, <c>scribe.legend</c>);
/// every handle but a figure's fractional, since an integer reads as a figure's number. A
/// property is written when a script could set it, it is not one MATLAB computes (a limit, a
/// tick, a camera, with its <c>Mode</c> <c>'auto'</c>) and a MAT-file can hold it: the look as this
/// build drew it, line widths and grids included, since this build's defaults are not MATLAB's
/// factory values, and the loader passes over a name or a value it does not take
/// (<c>oi77_hand</c>).
/// </para>
/// </summary>
internal static partial class JgsFigFile
{
    /// <summary>The struct tree's variable: the format word of <c>hgsave</c>'s files.</summary>
    public const string TreeVariable = "hgS_070000";

    /// <summary>This build's own document inside the file, as UTF-8 bytes in a <c>uint8</c> column.</summary>
    public const string DocumentVariable = "jgraph_document";

    /// <summary>
    /// Writes <paramref name="figure"/> to <paramref name="path"/> as a MATLAB <c>.fig</c>: the
    /// struct tree, and <paramref name="document"/> (the <c>.graph</c> text) beside it when given.
    /// Answers the type words of the objects the tree has no node for, each once.
    /// </summary>
    public static IReadOnlyList<string> Write(string path, FigureModel figure, string? document)
    {
        var writer = new TreeWriter();
        Dictionary<string, JgsValue> root = writer.Root(figure);
        var variables = new List<(string Name, JgsValue Value)> { (TreeVariable, JgsValue.Struct(root)) };
        if (document is not null)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(document);
            var numbers = new double[bytes.Length];
            for (int i = 0; i < bytes.Length; i++)
            {
                numbers[i] = bytes[i];
            }

            JgsValue column = JgsMatrix.FromColumnMajor(numbers, bytes.Length, 1);
            column.SetNumericClass(JgsNumericClass.UInt8);
            variables.Add((DocumentVariable, column));
        }

        MatFileWriter.Write(path, variables, matlabFunctions: true); // this build's own function element makes the whole file unreadable to MATLAB
        return writer.Skipped.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>The <c>.graph</c> document a MAT-file <c>.fig</c> carries, or null when it holds none (a file MATLAB wrote).</summary>
    public static string? Document(string path)
    {
        bool held = false;
        foreach ((string name, _, _) in MatFileReader.Describe(path))
        {
            held |= name == DocumentVariable;
        }

        if (!held)
        {
            return null;
        }

        foreach ((string name, JgsValue value) in MatFileReader.Read(path, new HashSet<string>(StringComparer.Ordinal) { DocumentVariable }))
        {
            if (name == DocumentVariable)
            {
                double[] numbers = Devices.DeviceChecks.NumberArray(value);
                var bytes = new byte[numbers.Length];
                for (int i = 0; i < numbers.Length; i++)
                {
                    bytes[i] = (byte)numbers[i];
                }

                return Encoding.UTF8.GetString(bytes);
            }
        }

        return null;
    }

    /// <summary>Builds the struct tree of one figure.</summary>
    private sealed class TreeWriter
    {
        /// <summary>hgsave's own fraction: a handle that is not a whole number is never read as a figure's.</summary>
        private const double Fraction = 0.0001220703125;

        /// <summary>Names never written: the tree's own structure, what the builder derives, and what names another object.</summary>
        private static readonly HashSet<string> Never = new(StringComparer.OrdinalIgnoreCase)
        {
            "Parent", "Children", "Type", "BeingDeleted", "CurrentAxes", "CurrentObject", "CurrentPoint", "CurrentCharacter",
            "Number", "FileName", "Subtitle", "XLabel", "YLabel", "ZLabel", "RLabel", "ThetaLabel", "Label",
            "XAxis", "YAxis", "ZAxis", "RAxis", "ThetaAxis", "Toolbar", "Legend", "Colorbar", "Layout", "InnerPosition",
            "OuterPosition", "PositionConstraint", "ActivePositionProperty", "TightInset", "Annotation", "DataTipTemplate",
            "Interactions", "SelectionType", "Extent", "Axes", "PlotChildren", "Image", "RunningAppInstance", "Scrollable",
            "SelectedObject",
        };

        /// <summary>
        /// An object's context menu: not a property of the object in the tree but the menu node's
        /// <c>special</c>, which lists the handles of the objects that use it (hgsave's form; a
        /// handle written as a property is not connected: probe <c>oi77_hand</c>). A button group's
        /// <c>SelectedObject</c> is the radio whose <c>Value</c> is 1.
        /// </summary>
        private static readonly HashSet<string> MenuLinks = new(StringComparer.OrdinalIgnoreCase)
        {
            "ContextMenu", "UIContextMenu",
        };

        /// <summary>
        /// The properties whose <c>'auto'</c> mode means MATLAB computes them from the data and the
        /// layout, so the value is left out for it to compute again (a value followed by its mode
        /// is the same thing: probe <c>oi77_hand</c>). Any other property with an automatic mode -
        /// a line's colour from the colour order, a bar's face - is written as it reads, since MATLAB's
        /// factory value is not what the script saw.
        /// </summary>
        private static readonly HashSet<string> Computed = new(StringComparer.OrdinalIgnoreCase)
        {
            "XLim", "YLim", "ZLim", "CLim", "ALim", "RLim", "ThetaLim", "XTick", "YTick", "ZTick", "RTick", "ThetaTick",
            "XTickLabel", "YTickLabel", "ZTickLabel", "RTickLabel", "ThetaTickLabel", "XTickLabelRotation", "YTickLabelRotation",
            "ZTickLabelRotation", "DataAspectRatio", "PlotBoxAspectRatio", "CameraPosition", "CameraTarget", "CameraUpVector",
            "CameraViewAngle", "PaperPosition", "PaperSize", "FontSize", "Limits", "Ticks", "TickLabels", "ColorOrderIndex",
            "LineStyleOrderIndex", "Colormap", "Position", "LevelList", "LevelStep", "BinLimits", "BinWidth", "NumBins",
            "VertexNormals", "FaceNormals",
        };

        /// <summary>The words a legend's or a colour bar's <c>Location</c> takes when its <c>Position</c> is its own.</summary>
        private static bool PlacedByHand(JgsValue? location) =>
            location is not null && JgsBuiltins.IsTextScalar(location)
            && JgsBuiltins.TextOf(location).ToLowerInvariant() is "manual" or "none";

        /// <summary>What a title or a label carries into the tree: the text and how it is set, never where the axes placed it.</summary>
        private static readonly HashSet<string> LabelNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "String", "FontSize", "FontWeight", "FontAngle", "FontName", "Color", "Interpreter", "Visible", "Rotation",
            "HorizontalAlignment", "VerticalAlignment",
        };

        /// <summary>Written first, in this order: what later values are read against.</summary>
        private static readonly string[] First = ["Units", "FontUnits", "Style", "Max", "Min", "String", "Data"];

        /// <summary>Written last, in this order.</summary>
        private static readonly string[] Last = ["Value", "Position", "Visible"];

        private readonly Dictionary<GraphObject, double> _handles = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<GraphObject, Dictionary<string, JgsValue>> _menuNodes = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<GraphObject, List<double>> _menuUsers = new(ReferenceEqualityComparer.Instance);
        private int _count;

        /// <summary>What MATLAB's copy leaves out, in the order met: the type words of the objects it has no node for, and the callbacks it cannot carry.</summary>
        public List<string> Skipped { get; } = [];

        /// <summary>The words for a callback MATLAB's copy cannot carry (an anonymous function's workspace is MATLAB's own object).</summary>
        private const string CapturingCallback = "a callback capturing variables";

        /// <summary>The words for a cell callback holding an anonymous function (the cell's handle cannot be a char command).</summary>
        private const string CellAnonymousCallback = "an anonymous function in a cell callback";

        /// <summary>
        /// How many elements a captured value may have and still be written into the callback's
        /// text (open item 139): a table's worth is a command, a picture's worth is not.
        /// </summary>
        private const int LiteralElements = 256;

        public Dictionary<string, JgsValue> Root(FigureModel figure)
        {
            HandleOf(figure);
            var children = new List<Dictionary<string, JgsValue>>();
            var annotations = new List<AnnotationObject>();
            foreach (GraphObject child in JgsGraphicsProperties.ChildrenOf(figure))
            {
                if (child is AnnotationObject annotation)
                {
                    annotations.Add(annotation); // all of them on one overlay, after the rest, as hgsave writes them
                }
                else
                {
                    Append(children, child);
                }
            }

            if (annotations.Count > 0)
            {
                children.Add(Overlay(annotations));
            }

            // Each context menu's special: the handles of the objects that use it, known once the whole tree is walked.
            foreach ((GraphObject menu, List<double> users) in _menuUsers)
            {
                if (_menuNodes.TryGetValue(menu, out Dictionary<string, JgsValue>? node))
                {
                    node["special"] = JgsMatrix.FromColumnMajor([.. users], users.Count, 1);
                }
            }

            return Node(figure, "figure", children, null);
        }

        /// <summary>A figure's is its number; every other handle is a count and the fraction (hgsave: 0.00012207, 13.00012207 ...).</summary>
        private double HandleOf(GraphObject target)
        {
            if (!_handles.TryGetValue(target, out double handle))
            {
                handle = target is FigureModel ? ++_count : NextHandle();
                _handles[target] = handle;
            }

            return handle;
        }

        private double NextHandle() => ++_count + Fraction;

        private void Append(List<Dictionary<string, JgsValue>> siblings, GraphObject target)
        {
            if (target is AxesModel axes)
            {
                AppendAxes(siblings, axes);
                return;
            }

            if (Word(target) is not { } word)
            {
                Skipped.Add(JgsGraphicsProperties.TypeNameOf(target));
                return;
            }

            var children = new List<Dictionary<string, JgsValue>>();
            foreach (GraphObject child in JgsGraphicsProperties.ChildrenOf(target))
            {
                Append(children, child);
            }

            siblings.Add(Node(target, word, children, null));
        }

        /// <summary>
        /// An axes: its plots, texts, lights and groups as children, its title and labels as text
        /// children named by <c>special</c>; its legend and colour bar as the figure's children after
        /// it, each naming the axes, as hgsave writes them.
        /// </summary>
        private void AppendAxes(List<Dictionary<string, JgsValue>> siblings, AxesModel axes)
        {
            var children = new List<Dictionary<string, JgsValue>>();
            var furniture = new List<Dictionary<string, JgsValue>>();
            foreach (GraphObject child in JgsGraphicsProperties.ChildrenOf(axes))
            {
                switch (child)
                {
                    case LegendModel legend:
                        if (Legend(legend, axes) is { } node)
                        {
                            furniture.Add(node);
                        }

                        break;
                    case ColorbarModel bar:
                        furniture.Add(Colorbar(bar, axes));
                        break;
                    case BubbleLegendModel:
                        Skipped.Add("bubblelegend");
                        break;
                    default:
                        Append(children, child);
                        break;
                }
            }

            // An axes' special is four places (an empty one is an access violation in R2025b's
            // loader: oi77_hand). hgsave's own tree has no place for a subtitle, a polar axes'
            // title or (a JGraph extra) a polar axes' labels - R2025b's hgsave writes none and
            // reopens its own tree without them, and no hand-built form is taken (open item 139,
            // probe oi139_shapes) - so they are named as left out when they say something.
            var special = new double[4];
            JgsHandleEntry entry = JgsHandleRegistry.EntryFor(axes);
            if (!axes.IsPolar)
            {
                string[] roles = ["Title", "XLabel", "YLabel", "ZLabel"];
                for (int i = 0; i < roles.Length; i++)
                {
                    if (Label(entry, roles[i]) is { } label)
                    {
                        children.Add(Node(label, "text", [], null));
                        special[i] = children.Count;
                    }
                }

                if (Label(entry, "Subtitle") is not null)
                {
                    Skipped.Add("an axes' subtitle");
                }
            }
            else
            {
                if (Label(entry, "Title") is not null)
                {
                    Skipped.Add("a polar axes' title");
                }

                if (Label(entry, "RLabel") is not null)
                {
                    Skipped.Add("a polar axes' label");
                }
            }

            siblings.Add(Node(axes, axes.IsPolar ? "matlab.graphics.axis.PolarAxes" : "axes", children, special));
            siblings.AddRange(furniture);
        }

        /// <summary>An axes' title or label when it says something; null when it is empty.</summary>
        private static JgsTextLabel? Label(JgsHandleEntry axes, string role)
        {
            try
            {
                JgsValue handle = JgsGraphicsProperties.Get(axes, role, 0, 0);
                return JgsHandleRegistry.TryGet(handle, out JgsHandleEntry? entry) && entry.Target is JgsTextLabel { Text.Length: > 0 } label
                    ? label
                    : null;
            }
            catch (JgsException)
            {
                return null;
            }
        }

        /// <summary>
        /// A legend as hgsave writes one: a <c>scribe.legend</c> among the figure's children naming
        /// its axes and the plots it lists (the handles the loader connects it by), its rows as
        /// <c>String</c>, and the application data its post-deserialize function reads. R2025b's
        /// loader is an access violation on a legend without the plots or without the application
        /// data (probe <c>oi77_hand</c>), so a legend that lists nothing is left out.
        /// </summary>
        private Dictionary<string, JgsValue>? Legend(LegendModel legend, AxesModel axes)
        {
            Dictionary<string, JgsValue> props = Properties(legend);
            PlaceByLocation(props);
            JgsHandleEntry entry = JgsHandleRegistry.EntryFor(legend);
            JgsValue strings;
            try
            {
                strings = JgsGraphicsProperties.Get(entry, "String", 0, 0); // brings the rows up to date, and names the unnamed
            }
            catch (JgsException)
            {
                strings = JgsValue.Cell([]);
            }

            bool matlab = JgsRunningDialect.ThreadIsMatlab;
            var plots = new List<double>();
            foreach (LegendEntryModel row in legend.Entries)
            {
                if (row.Plot is { } plot && (!matlab || row.Visible) && _handles.ContainsKey(plot))
                {
                    plots.Add(HandleOf(plot));
                }
            }

            if (plots.Count == 0)
            {
                Skipped.Add("legend");
                return null;
            }

            if (!string.IsNullOrEmpty(legend.Title))
            {
                Skipped.Add("a legend's title"); // hgsave's tree has no place for one (open item 139, oi139_shapes)
            }

            JgsValue children = JgsMatrix.FromColumnMajor([.. plots], plots.Count, 1);
            props.Remove("String");
            props["String"] = strings;
            props["ApplicationData"] = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["NonDataObject"] = JgsEmpty.Zero(),
                ["PostDeserializeFcn"] = Named("legendpostdeserialize"),
                ["PlotChildren"] = children,
                ["PeerAxes"] = JgsValue.Number(HandleOf(axes)),
            });
            props["Axes"] = JgsValue.Number(HandleOf(axes));
            props["PlotChildren"] = children;
            return Node(legend, "scribe.legend", props, [], null);
        }

        /// <summary>
        /// A colour bar as hgsave writes one: a <c>scribe.colorbar</c> among the figure's children
        /// naming its axes, with its <c>Limits</c> and the limits of the axes it is made of (the
        /// colour span along it, <c>[-1 2]</c> across; without them the loader refuses: oi77_hand).
        /// </summary>
        private Dictionary<string, JgsValue> Colorbar(ColorbarModel bar, AxesModel axes)
        {
            Dictionary<string, JgsValue> props = Properties(bar);
            PlaceByLocation(props);
            if (!string.IsNullOrEmpty(bar.Label))
            {
                Skipped.Add("a colour bar's label"); // hgsave's tree has no place for one (open item 139, oi139_shapes)
            }

            JgsHandleEntry entry = JgsHandleRegistry.EntryFor(bar);
            JgsValue limits = Read(entry, "Limits") ?? JgsMatrix.FromColumnMajor([0, 1], 1, 2);
            double[] span = Devices.DeviceChecks.NumberArray(limits);
            if (span.Length != 2 || !(span[1] > span[0]))
            {
                limits = JgsMatrix.FromColumnMajor([0, 1], 1, 2);
            }

            JgsValue across = JgsMatrix.FromColumnMajor([-1, 2], 1, 2);
            props["Limits"] = limits;
            props["XLim"] = bar.IsHorizontal ? limits : across;
            props["YLim"] = bar.IsHorizontal ? across : limits;
            props["ApplicationData"] = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["NonDataObject"] = JgsEmpty.Zero(),
                ["PostDeserializeFcn"] = Named("colorbarpostdeserialize"),
            });
            props["Axes"] = JgsValue.Number(HandleOf(axes));
            return Node(bar, "scribe.colorbar", props, [], null);
        }

        /// <summary>
        /// A legend or a colour bar placed by its <c>Location</c> word is placed by MATLAB too: its
        /// <c>Position</c> is written only when the word says the position is its own, since a
        /// position written beside a word makes MATLAB squeeze the axes for a bar that then sits
        /// elsewhere (oi77_verify).
        /// </summary>
        private static void PlaceByLocation(Dictionary<string, JgsValue> props)
        {
            if (!PlacedByHand(props.TryGetValue("Location", out JgsValue? location) ? location : null))
            {
                props.Remove("Position");
                props.Remove("Units");
            }
        }

        /// <summary>A handle to one of MATLAB's own functions by name, written as the function element of that name.</summary>
        private static JgsValue Named(string name) =>
            JgsValue.Function(new BuiltinFunction(name, static (_, _, _) => JgsValue.Null));

        /// <summary>The figure's annotations on one <c>scribe.scribeaxes</c>, as hgsave writes them.</summary>
        private Dictionary<string, JgsValue> Overlay(List<AnnotationObject> annotations)
        {
            var children = new List<Dictionary<string, JgsValue>>();
            foreach (AnnotationObject annotation in annotations)
            {
                string? word = annotation is TextAnnotation ? "scribe.textbox" : Word(annotation);
                if (word is null)
                {
                    Skipped.Add(JgsGraphicsProperties.TypeNameOf(annotation));
                    continue;
                }

                children.Add(Node(annotation, word, [], null));
            }

            return new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["type"] = JgsValue.Str("scribe.scribeaxes"),
                ["handle"] = JgsValue.Number(NextHandle()),
                ["properties"] = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
                {
                    ["Description"] = JgsValue.Str("overlay"),
                    ["Serializable"] = JgsValue.Str("on"),
                    ["HandleVisibility"] = JgsValue.Str("off"),
                    ["Tag"] = JgsValue.Str("scribeOverlay"),
                }),
                ["children"] = Column(children),
                ["special"] = JgsEmpty.Zero(),
            };
        }

        /// <summary>The old graphics system's type word for an object, or null for a kind the tree has no node for.</summary>
        private static string? Word(GraphObject target) => JgsGraphicsProperties.TypeNameOf(target) switch
        {
            "figure" => "figure",
            "axes" => "axes",
            "polaraxes" => "matlab.graphics.axis.PolarAxes",
            "line" when target is PlotObject => "graph2d.lineseries",
            "line" when target is ArrowAnnotation => "scribe.line",
            "stair" => "specgraph.stairseries",
            "scatter" => "specgraph.scattergroup",
            "bar" => "specgraph.barseries",
            "area" => "specgraph.areaseries",
            "stem" => "specgraph.stemseries",
            "errorbar" => "specgraph.errorbarseries",
            "histogram" => "histogram",
            "quiver" => "specgraph.quivergroup",
            "contour" => "specgraph.contourgroup",
            "surface" when target is SurfacePlot => "graph3d.surfaceplot",
            "image" => "image",
            "patch" => "patch",
            "rectangle" when target is PatchPlot => "rectangle",
            "rectangle" => "scribe.scriberect",
            "text" => "text",
            "textbox" => "scribe.textbox",
            "ellipse" => "scribe.scribeellipse",
            "arrow" => "scribe.arrow",
            "doublearrow" => "scribe.doublearrow",
            "textarrow" => "scribe.textarrow",
            "light" => "light",
            "hggroup" => "hggroup",
            "hgtransform" => "hgtransform",
            "uicontrol" => "uicontrol",
            "uipanel" => "uipanel",
            "uibuttongroup" => "uitools.uibuttongroup",
            "uimenu" => "uimenu",
            "uicontextmenu" => "uicontextmenu",
            "uitable" => "uitable",
            "uitoolbar" => "uitoolbar",
            "uipushtool" => "uipushtool",
            "uitoggletool" => "uitoggletool",
            _ => null,
        };

        private Dictionary<string, JgsValue> Node(GraphObject target, string word, List<Dictionary<string, JgsValue>> children, double[]? special)
        {
            Dictionary<string, JgsValue> properties = Properties(target);
            Extras(target, word, properties);
            return Node(target, word, properties, children, special);
        }

        private Dictionary<string, JgsValue> Node(
            GraphObject target, string word, Dictionary<string, JgsValue> properties, List<Dictionary<string, JgsValue>> children, double[]? special)
        {
            var node = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["type"] = JgsValue.Str(word),
                ["handle"] = JgsValue.Number(HandleOf(target)),
                ["properties"] = JgsValue.Struct(properties),
                ["children"] = Column(children),
                ["special"] = special is null ? JgsEmpty.Zero() : JgsMatrix.FromColumnMajor(special, special.Length, 1),
            };
            if (target is ContextMenuModel)
            {
                _menuNodes[target] = node; // its special is filled once every user of it has been walked
            }

            return node;
        }

        /// <summary>
        /// What a kind needs in the tree beyond what its properties say (oi77_hand): a bar or an
        /// area the peers word, without which the loader leaves a placeholder; a scatter its sizes
        /// and colours, without which its data lengths do not match; a patch given by faces and
        /// vertices those alone, not the coordinate columns derived from them as well.
        /// </summary>
        private static void Extras(GraphObject target, string word, Dictionary<string, JgsValue> properties)
        {
            switch (word)
            {
                case "specgraph.barseries":
                    properties["BarPeers"] = JgsValue.Number(0);
                    break;
                case "specgraph.areaseries":
                    properties["AreaPeers"] = JgsValue.Number(0);
                    break;
                case "specgraph.scattergroup":
                    JgsHandleEntry entry = JgsHandleRegistry.EntryFor(target);
                    foreach (string name in new[] { "SizeData", "CData", "MarkerFaceColor", "MarkerEdgeColor" })
                    {
                        if (!properties.ContainsKey(name) && Read(entry, name) is { } value && MatFileWriter.WhyNotWritable(value) is null)
                        {
                            properties[name] = value;
                        }
                    }

                    break;
                case "patch":
                    if (properties.ContainsKey("Faces") && properties.ContainsKey("Vertices"))
                    {
                        properties.Remove("XData");
                        properties.Remove("YData");
                        properties.Remove("ZData");
                    }

                    break;
            }
        }

        /// <summary>The children in a column, oldest first; none is a 0-by-0 double, as hgsave writes a leaf's.</summary>
        private static JgsValue Column(List<Dictionary<string, JgsValue>> nodes) =>
            nodes.Count == 0 ? JgsEmpty.Zero() : JgsValue.StructArray(new JgsStructArray([.. nodes]), nodes.Count, 1);

        /// <summary>
        /// The properties of one object, in the order they are to be set: what a script could set,
        /// that MATLAB does not compute and a MAT-file holds. A callback that is an anonymous
        /// function becomes the command that re-makes and calls it, since MATLAB's copy cannot hold
        /// its workspace; one that captured variables is left out and said to be.
        /// </summary>
        private Dictionary<string, JgsValue> Properties(GraphObject target)
        {
            JgsHandleEntry entry = JgsHandleRegistry.EntryFor(target);
            bool label = target is JgsTextLabel;
            var chosen = new List<(string Name, JgsValue Value)>();
            foreach (string name in JgsGraphicsProperties.NamesOf(target))
            {
                if (Never.Contains(name) || name.EndsWith("Mode", StringComparison.Ordinal) || (label && !LabelNames.Contains(name)))
                {
                    continue;
                }

                // An axes' or a legend's title is a text object, named by the axes' special; a
                // panel's is its text.
                if (name.Equals("Title", StringComparison.OrdinalIgnoreCase) && target is not UiContainerModel)
                {
                    continue;
                }

                if (!JgsGraphicsProperties.TryFind(target, name, out GraphicsProperty property) || property.Write is null)
                {
                    continue;
                }

                if (IsAuto(entry, target, name))
                {
                    continue;
                }

                if (Read(entry, name) is not { } value)
                {
                    continue;
                }

                if (value.Type == JgsType.Function && value.AsCallable is AnonymousFunction anonymous)
                {
                    if (CommandText(anonymous) is not { } command)
                    {
                        Skipped.Add(CapturingCallback);
                        continue;
                    }

                    value = JgsValue.Str(CallbackCommand(command));
                }
                else if (value.Type == JgsType.Cell && value.AsCell.Any(static held => held.Type == JgsType.Function && held.AsCallable is AnonymousFunction))
                {
                    Skipped.Add(CellAnonymousCallback); // a cell's first element must be a handle, and MATLAB's form of an anonymous one has no workspace
                    continue;
                }

                if (MenuLinks.Contains(name))
                {
                    if (JgsHandleRegistry.TryGet(value, out JgsHandleEntry? linked) && linked.Target is ContextMenuModel menu)
                    {
                        if (!_menuUsers.TryGetValue(menu, out List<double>? users))
                        {
                            _menuUsers[menu] = users = [];
                        }

                        users.Add(HandleOf(target));
                    }

                    continue;
                }

                if (IsHandle(value) || MatFileWriter.WhyNotWritable(value) is not null)
                {
                    continue;
                }

                chosen.Add((property.Name, value));
            }

            var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
            foreach ((string name, JgsValue value) in chosen.OrderBy(static p => Rank(p.Name)))
            {
                fields[name] = value;
            }

            return fields;
        }

        private static int Rank(string name)
        {
            int first = Array.FindIndex(First, n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (first >= 0)
            {
                return first - 100;
            }

            int last = Array.FindIndex(Last, n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
            return last >= 0 ? 100 + last : 0;
        }

        /// <summary>
        /// Whether a property is one MATLAB computes and its <c>Mode</c> reads <c>'auto'</c>, so that it
        /// is left for MATLAB to compute again. An axes' <c>Position</c> is always written: its place
        /// in the figure is what a subplot is.
        /// </summary>
        private static bool IsAuto(JgsHandleEntry entry, GraphObject target, string name)
        {
            if (!Computed.Contains(name) || (name.Equals("Position", StringComparison.OrdinalIgnoreCase) && target is not FigureModel))
            {
                return false;
            }

            return JgsGraphicsProperties.TryFind(target, name + "Mode", out _)
                && Read(entry, name + "Mode") is { } mode && JgsBuiltins.IsTextScalar(mode)
                && JgsBuiltins.TextOf(mode).Equals("auto", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>A property's reading as a MAT-file can hold it, or null for one that cannot be read or held.</summary>
        private static JgsValue? Read(JgsHandleEntry entry, string name)
        {
            JgsValue value;
            try
            {
                value = JgsGraphicsProperties.Get(entry, name, 0, 0);
            }
            catch (Exception ex) when (ex is JgsException or InvalidOperationException or NullReferenceException or ArgumentException or IndexOutOfRangeException)
            {
                return null; // a reading an object nothing holds cannot give, or one this build refuses
            }

            if (value.Type == JgsType.External)
            {
                return value.AsExternal is OnOffSwitchStateValue { States.Length: 1 } state
                    ? JgsValue.Str(OnOffSwitchStateValue.NameOf(state.States[0]))
                    : null;
            }

            if (value.IsStringArray)
            {
                if (value.ArrayLength == 1)
                {
                    return JgsValue.Str(value.ElementAt(0).AsString);
                }

                JgsValue cell = JgsValue.Cell([.. Enumerable.Range(0, value.ArrayLength).Select(i => JgsValue.Str(value.ElementAt(i).AsString))]);
                cell.Reshape(value.Rows, value.Cols);
                return cell;
            }

            return value.Type is JgsType.Object or JgsType.Table or JgsType.Null || value.IsCategorical ? null : value;
        }

        /// <summary>Whether a value is a handle, or handles, to objects (a figure's number is left to the reader of a numeric value).</summary>
        private static bool IsHandle(JgsValue value)
        {
            static bool One(JgsValue v) =>
                v.Type == JgsType.Number && v.AsNumber >= 1_000_000 && JgsHandleRegistry.TryGet(v, out JgsHandleEntry? entry) && entry.Target is not FigureModel;

            if (value.Type == JgsType.Number)
            {
                return One(value);
            }

            if (value.Type == JgsType.Array && value.ArrayLength > 0 && !value.IsCharMatrix)
            {
                for (int i = 0; i < value.ArrayLength; i++)
                {
                    if (!One(value.ElementAt(i)))
                    {
                        return false;
                    }
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// The command MATLAB runs for a callback that was an anonymous function: the function
        /// re-made from its text and called with the object and no event, which is the callback's
        /// own behaviour for one that captured nothing.
        /// </summary>
        private static string CallbackCommand(string text) =>
            "feval(str2func('" + text.Replace("'", "''") + "'), gcbo, [])";

        /// <summary>
        /// The text that re-makes an anonymous callback in MATLAB: its own when it captured nothing,
        /// else its text with each captured variable's value written in the variable's place as a
        /// literal (open item 139: MATLAB's form keeps a workspace as an object of its own, which a
        /// file written here cannot hold, probe <c>oi77_fh</c>). Null when a capture is not a
        /// literal - a struct, a cell, an object, a handle, a value past <see cref="LiteralElements"/>
        /// elements - or is indexed, called or reached into in the body, where a literal cannot stand.
        /// </summary>
        private static string? CommandText(AnonymousFunction anonymous)
        {
            var literals = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach ((string name, JgsValue value) in anonymous.CapturedVariables)
            {
                if (LiteralText(value) is not { } literal)
                {
                    return null;
                }

                literals[name] = literal;
            }

            return literals.Count == 0 ? anonymous.Text : AstPrinter.Print(anonymous.Declaration, literals);
        }

        /// <summary>A value as MATLAB reads it back from text (<c>mat2str</c>'s spelling), or null for one that has none.</summary>
        private static string? LiteralText(JgsValue value)
        {
            if (value.IsStringArray)
            {
                return value.ArrayLength == 1 ? "\"" + value.ElementAt(0).AsString.Replace("\"", "\"\"") + "\"" : null;
            }

            if (value.IsCategorical || value.IsObjectArray || value.Type is not (JgsType.Number or JgsType.Bool or JgsType.Complex or JgsType.String or JgsType.Array))
            {
                return null;
            }

            if (value.Type == JgsType.Array && (value.Dims.Length > 2 || value.ArrayLength > LiteralElements))
            {
                return null;
            }

            try
            {
                string text = JgsBuiltins.MatrixText([value], 0, 0).AsString;
                return text.StartsWith('-') ? "(" + text + ")" : text; // a negative number keeps its sign to itself: x^-3 reads, 2*-3 does not everywhere
            }
            catch (JgsException)
            {
                return null;
            }
        }
    }
}
