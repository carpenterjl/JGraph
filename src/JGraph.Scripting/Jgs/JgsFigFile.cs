using System.IO;
using System.Text;
using JGraph.Core.Model;
using JGraph.Scripting.MatFile;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// One object of a MATLAB <c>.fig</c> file, read out of either of its two forms (U11, ADR 0210):
/// the MATLAB type word, the properties it saved under their public names, its children in the
/// order <c>Children</c> lists them (newest first), an axes's title and labels, the properties that
/// name another saved object, and its application data.
/// </summary>
internal sealed class FigNode(string type)
{
    /// <summary>The MATLAB type word (<c>figure</c>, <c>uicontrol</c>, <c>line</c>…), or a class name this build does not rebuild.</summary>
    public string Type { get; } = type;

    /// <summary>The saved handle (struct form) or the object id (subsystem form) that links name.</summary>
    public double Key { get; set; } = double.NaN;

    public List<(string Name, JgsValue Value)> Properties { get; } = [];

    /// <summary>The children, newest first: the order <c>Children</c> lists them (the struct form stores them the other way round).</summary>
    public List<FigNode> Children { get; } = [];

    /// <summary>An axes's <c>Title</c>, <c>XLabel</c>, <c>YLabel</c> and <c>ZLabel</c> texts.</summary>
    public Dictionary<string, FigNode> Labels { get; } = new(StringComparer.Ordinal);

    /// <summary>Properties whose value is another saved object, by its key.</summary>
    public List<(string Name, double Key)> Links { get; } = [];

    public List<(string Name, JgsValue Value)> AppData { get; } = [];

    /// <summary>The saved value of <paramref name="name"/>, matched without regard to case.</summary>
    public JgsValue? Property(string name)
    {
        foreach ((string key, JgsValue value) in Properties)
        {
            if (key.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }
}

/// <summary>
/// MATLAB's <c>.fig</c> files (U11, ADR 0210): a MAT-file holding the figure twice in R2024a and
/// earlier - <c>hgS_070000</c>, a plain struct tree of type, handle, properties, children and
/// special, and <c>hgM_070000</c>, the objects themselves in the subsystem - and only once since
/// R2025b, whose <c>savefig</c> writes an empty <c>hgS_080000</c> and keeps everything in
/// <c>hgM_080000</c>. Both forms read into <see cref="FigNode"/>s, and one builder makes the
/// figure from them through this build's own makers and property table, so a file can set nothing
/// a script could not.
/// </summary>
internal static class JgsFigFile
{
    /// <summary>What every level-5 MAT-file's text header starts with.</summary>
    private const string MatHeader = "MATLAB 5.0 MAT-file";

    /// <summary>R2025b's refusal of a MAT-file that holds no figure.</summary>
    public const string InvalidFigId = "MATLAB:loadFigure:InvalidFigFile";

    public const string InvalidFigMessage = "Invalid Figure file format.";

    /// <summary>Whether <paramref name="path"/> is a MAT-file (and so a MATLAB figure file) rather than this build's own document.</summary>
    public static bool IsMatFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        var head = new byte[MatHeader.Length];
        return stream.Read(head, 0, head.Length) == head.Length && Encoding.ASCII.GetString(head) == MatHeader;
    }

    /// <summary>
    /// The figure a MATLAB <c>.fig</c> holds: its struct tree when the file has one, else the
    /// objects in its subsystem.
    /// </summary>
    /// <exception cref="InvalidDataException">The file holds no figure, in R2025b's words.</exception>
    public static FigNode Read(string path, IMatObjectBinder binder)
    {
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string name, _, _) in MatFileReader.Describe(path))
        {
            if (name.StartsWith("hgS_", StringComparison.Ordinal) || name.StartsWith("hgM_", StringComparison.Ordinal))
            {
                wanted.Add(name);
            }
        }

        if (wanted.Count == 0)
        {
            throw new InvalidDataException(InvalidFigMessage);
        }

        (IReadOnlyList<(string Name, JgsValue Value)> variables, MatMcos? store) =
            MatFileReader.ReadWithSubsystem(path, wanted, new TolerantBinder(binder));
        foreach ((string name, JgsValue value) in variables)
        {
            if (name.StartsWith("hgS_", StringComparison.Ordinal) && value.Type == JgsType.Struct && !value.IsStructArray)
            {
                return FromStruct(value.AsStruct);
            }
        }

        foreach ((string name, JgsValue value) in variables)
        {
            if (store is not null && name.StartsWith("hgM_", StringComparison.Ordinal)
                && value.Type == JgsType.Struct && !value.IsStructArray
                && value.AsStruct.TryGetValue("GraphicsObjects", out JgsValue? wrapper)
                && MatMcos.Refs(wrapper, out int[] ids) && ids.Length == 1)
            {
                foreach ((string property, JgsValue held) in store.Properties(ids[0]))
                {
                    if (property == "Format3Data" && MatMcos.Refs(held, out int[] figure) && figure.Length == 1)
                    {
                        return new SubsystemReader(store).Node(figure[0]);
                    }
                }
            }
        }

        throw new InvalidDataException(InvalidFigMessage);
    }

    /// <summary>
    /// The workspace binder, except that a handle to a function that is not on the path - a figure
    /// keeps handles to MATLAB's own internals, a legend's <c>legendpostdeserialize</c> - comes back
    /// as the function's name, which fails as R2025b's handle does: when it is called, not when the
    /// figure opens.
    /// </summary>
    private sealed class TolerantBinder(IMatObjectBinder inner) : IMatObjectBinder
    {
        public JgsValue NewObject(string className, bool deleted, string variable) => inner.NewObject(className, deleted, variable);

        public void SetProperties(JgsValue instance, IReadOnlyDictionary<string, JgsValue> properties) =>
            inner.SetProperties(instance, properties);

        public JgsValue Function(string text, string type, IReadOnlyDictionary<string, JgsValue>? workspace, string variable)
        {
            try
            {
                return inner.Function(text, type, workspace, variable);
            }
            catch (JgsException) when (type != "anonymous" && !text.StartsWith('@'))
            {
                return JgsValue.Str(text);
            }
        }
    }

    // --- The struct tree ----------------------------------------------------------------------

    /// <summary>Properties of the struct form whose value is another object's saved handle.</summary>
    private static readonly HashSet<string> LinkNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "UIContextMenu", "ContextMenu", "SelectedObject", "Axes",
    };

    /// <summary>Properties the builder sets itself or that only name parts of the saved tree.</summary>
    private static readonly HashSet<string> StructSkipped = new(StringComparer.OrdinalIgnoreCase)
    {
        "Children", "Parent", "Type", "BeingDeleted", "Title", "Subtitle", "XLabel", "YLabel", "ZLabel",
        "FileName", "CurrentAxes", "CurrentObject", "PlotChildren", "PeerAxes", "Internal",
    };

    private static FigNode FromStruct(IReadOnlyDictionary<string, JgsValue> saved)
    {
        string type = saved.TryGetValue("type", out JgsValue? word) && JgsBuiltins.IsTextScalar(word) ? JgsBuiltins.TextOf(word) : "?";
        var node = new FigNode(type switch
        {
            "graph2d.lineseries" => "line",
            "scribe.legend" => "legend",
            "scribe.colorbar" => "colorbar",
            _ => type,
        });
        if (saved.TryGetValue("handle", out JgsValue? handle) && handle.Type == JgsType.Number)
        {
            node.Key = handle.AsNumber;
        }

        if (saved.TryGetValue("properties", out JgsValue? properties) && properties.Type == JgsType.Struct && !properties.IsStructArray)
        {
            foreach ((string name, JgsValue value) in properties.AsStruct)
            {
                if (name.Equals("ApplicationData", StringComparison.OrdinalIgnoreCase))
                {
                    if (value.Type == JgsType.Struct && !value.IsStructArray)
                    {
                        node.AppData.AddRange(value.AsStruct.Select(static p => (p.Key, p.Value)));
                    }
                }
                else if (LinkNames.Contains(name) && value.Type == JgsType.Number)
                {
                    node.Links.Add((name, value.AsNumber));
                }
                else if (!StructSkipped.Contains(name))
                {
                    node.Properties.Add((name, value));
                }
            }
        }

        if (saved.TryGetValue("children", out JgsValue? children) && children.Type == JgsType.Struct)
        {
            IEnumerable<IReadOnlyDictionary<string, JgsValue>> each = children.IsStructArray
                ? children.AsStructArray.Elements
                : [children.AsStruct];
            foreach (IReadOnlyDictionary<string, JgsValue> child in each)
            {
                node.Children.Add(FromStruct(child));
            }
        }

        // An axes's special is its title and labels, as places among its children (1-based; 0
        // for none): [title; xlabel; ylabel; zlabel]. They leave the children for the labels.
        if (node.Type == "axes" && saved.TryGetValue("special", out JgsValue? special)
            && special.Type is JgsType.Array or JgsType.Number)
        {
            double[] places = Devices.DeviceChecks.NumberArray(special);
            string[] which = ["Title", "XLabel", "YLabel", "ZLabel"];
            var taken = new HashSet<FigNode>();
            for (int i = 0; i < Math.Min(places.Length, which.Length); i++)
            {
                int place = (int)places[i] - 1;
                if (place >= 0 && place < node.Children.Count && node.Children[place].Type == "text")
                {
                    node.Labels[which[i]] = node.Children[place];
                    taken.Add(node.Children[place]);
                }
            }

            node.Children.RemoveAll(taken.Contains);
        }

        // The struct form keeps children oldest first, the reverse of what Children lists (probe
        // u11_guide: a tree saved with Children M1,C2,P1,A2,A1 holds A1,A2,P1,C2,M1).
        node.Children.Reverse();
        return node;
    }

    // --- The subsystem's objects --------------------------------------------------------------

    /// <summary>
    /// Reads R2025b's graphics objects out of the subsystem. Each saved what differs from its
    /// class's default as <c>Name_I</c> (or <c>Name_IS</c>), beside <c>NameMode</c>; a few keep
    /// what a script sees in a helper object - a container's place in its <c>UnitPos</c>, an
    /// axes's limits in its data space, its labels on its rulers - which is read from there.
    /// </summary>
    private sealed class SubsystemReader(MatMcos store)
    {
        private static readonly Dictionary<string, string> Types = new(StringComparer.Ordinal)
        {
            ["matlab.ui.Figure"] = "figure",
            ["matlab.graphics.axis.Axes"] = "axes",
            ["matlab.graphics.chart.primitive.Line"] = "line",
            ["matlab.graphics.primitive.Line"] = "line",
            ["matlab.graphics.primitive.Text"] = "text",
            ["matlab.graphics.primitive.Patch"] = "patch",
            ["matlab.graphics.primitive.Surface"] = "surface",
            ["matlab.graphics.chart.primitive.Surface"] = "surface",
            ["matlab.graphics.primitive.Image"] = "image",
            ["matlab.graphics.primitive.Rectangle"] = "rectangle",
            ["matlab.graphics.primitive.Light"] = "light",
            ["matlab.graphics.primitive.Group"] = "hggroup",
            ["matlab.graphics.primitive.Transform"] = "hgtransform",
            ["matlab.ui.control.UIControl"] = "uicontrol",
            ["matlab.ui.container.Panel"] = "uipanel",
            ["matlab.ui.container.ButtonGroup"] = "uibuttongroup",
            ["matlab.ui.container.Menu"] = "uimenu",
            ["matlab.ui.container.ContextMenu"] = "uicontextmenu",
            ["matlab.ui.control.Table"] = "uitable",
            ["matlab.ui.container.Toolbar"] = "uitoolbar",
            ["matlab.ui.container.toolbar.PushTool"] = "uipushtool",
            ["matlab.ui.container.toolbar.ToggleTool"] = "uitoggletool",
            ["matlab.graphics.illustration.Legend"] = "legend",
            ["matlab.graphics.illustration.ColorBar"] = "colorbar",
        };

        /// <summary>Saved state that is the object's machinery rather than anything a script set.</summary>
        private static readonly HashSet<string> Skipped = new(StringComparer.Ordinal)
        {
            "Internal", "Copyable", "CustomThemePropertyMappings", "BeingDeleted", "TopLevelSerializedObject",
            "SerializableBehavior", "SerializableTheme", "SerializableInteractionOptions", "DataPropertiesStorage",
            "DisplayNameAssignedByLegend", "Description", "DefaultTools", "DisplayData", "PositionCache",
            "LooseInset", "InnerPosition", "NextSeriesIndex", "SeriesIndex", "ColorOrderIndex", "Alphamap",
            "SortMethod", "ToolbarMode", "CameraPosition", "CameraTarget", "CameraUpVector", "CameraViewAngle",
            "PlotBoxAspectRatio", "DataAspectRatio", "MarkerIndices", "DimensionNames", "PointerShapeCData",
            "PointerShapeHotSpot", "LimitMaxLegendEntries", "PlotChildrenSpecified", "SelectionHandle",
            "ScreenPixelsPerInch", "Children", "Layout", "Annotation", "DataTipTemplate", "LegendInformation",
        };

        private readonly HashSet<int> _visited = [];

        public FigNode Node(int id)
        {
            string className = store.ClassOf(id);
            var node = new FigNode(Types.GetValueOrDefault(className, className)) { Key = id };
            if (!_visited.Add(id))
            {
                return node; // an object reached twice is built once, where its parent lists it
            }

            int unitPos = 0;
            int fontStorage = 0;
            foreach ((string raw, JgsValue value) in store.Properties(id))
            {
                string name = Short(raw);
                switch (name)
                {
                    case "SerializableChildren":
                        if (MatMcos.Refs(value, out int[] children))
                        {
                            foreach (int child in children)
                            {
                                if (!_visited.Contains(child))
                                {
                                    node.Children.Add(Node(child));
                                }
                            }
                        }

                        continue;
                    case "SerializableApplicationData":
                        if (value.Type == JgsType.Struct && !value.IsStructArray)
                        {
                            foreach ((string key, JgsValue held) in value.AsStruct)
                            {
                                if (!MatMcos.Refs(held, out _) && !HoldsReference(held))
                                {
                                    node.AppData.Add((key, held));
                                }
                            }
                        }

                        continue;
                    case "StoragePosition" or "StorageUnitPos":
                        if (MatMcos.Refs(value, out int[] storage) && storage.Length == 1 && unitPos == 0)
                        {
                            unitPos = storage[0];
                        }

                        continue;
                    case "FontStorage":
                        if (MatMcos.Refs(value, out int[] font) && font.Length == 1)
                        {
                            fontStorage = font[0];
                        }

                        continue;
                    case "SerializableUIContextMenu":
                        if (MatMcos.Refs(value, out int[] menu) && menu.Length == 1)
                        {
                            node.Links.Add(("ContextMenu", menu[0]));
                        }

                        continue;
                    case "Axes" when node.Type is "legend" or "colorbar":
                        if (MatMcos.Refs(value, out int[] peer) && peer.Length == 1)
                        {
                            node.Links.Add(("Axes", peer[0]));
                        }

                        continue;
                    case "Title" when node.Type == "axes":
                        if (MatMcos.Refs(value, out int[] title) && title.Length == 1)
                        {
                            node.Labels["Title"] = Node(title[0]);
                        }

                        continue;
                    case "XRuler" or "YRuler" or "ZRuler" when node.Type == "axes":
                        if (MatMcos.Refs(value, out int[] ruler) && ruler.Length == 1)
                        {
                            Ruler(node, name[..1], ruler[0]);
                        }

                        continue;
                    case "DataSpace" when node.Type == "axes":
                        if (MatMcos.Refs(value, out int[] space) && space.Length == 1)
                        {
                            DataSpace(node, space[0]);
                        }

                        continue;
                    case "ColorSpace" when node.Type == "axes":
                        if (MatMcos.Refs(value, out int[] colors) && colors.Length == 1)
                        {
                            ColorSpace(node, colors[0]);
                        }

                        continue;
                    case "SerializableString" when node.Type == "legend":
                        node.Properties.Add(("String", value));
                        continue;
                }

                if (Skipped.Contains(name) || MatMcos.Refs(value, out _))
                {
                    continue;
                }

                if (store.Function(value) is { } function)
                {
                    node.Properties.Add((name, function));
                }
                else if (value.Type != JgsType.Cell || !IsFunctionCell(value))
                {
                    node.Properties.Add((name, value));
                }
            }

            // A container's and a figure's place is in its UnitPos, in that object's units.
            if (unitPos > 0 && node.Property("Position") is null)
            {
                IReadOnlyList<(string Name, JgsValue Value)> place = store.Properties(unitPos);
                if (node.Property("Units") is null && Find(place, "Units_I") is { } units)
                {
                    node.Properties.Insert(0, ("Units", units));
                }

                if (Find(place, "PositionCache") is { } position)
                {
                    node.Properties.Add(("Position", position));
                }
            }

            // The font size is the font storage's height, when the object set one.
            if (fontStorage > 0 && node.Property("FontSizeMode") is { } sizeMode
                && JgsBuiltins.IsTextScalar(sizeMode) && JgsBuiltins.TextOf(sizeMode) == "manual")
            {
                IReadOnlyList<(string Name, JgsValue Value)> font = store.Properties(fontStorage);
                if (Find(font, "Units_I") is { } fontUnits)
                {
                    node.Properties.Add(("FontUnits", fontUnits));
                }

                if (Find(font, "PositionCache") is { Type: JgsType.Array } cache
                    && Devices.DeviceChecks.NumberArray(cache) is { Length: 4 } box)
                {
                    node.Properties.Add(("FontSize", JgsValue.Number(box[3])));
                }
            }

            return node;
        }

        /// <summary>A ruler's label, and its ticks and tick labels when they were set.</summary>
        private void Ruler(FigNode axes, string axis, int id)
        {
            IReadOnlyList<(string Name, JgsValue Value)> saved = store.Properties(id);
            foreach ((string raw, JgsValue value) in saved)
            {
                string name = Short(raw);
                if (name == "Label" && MatMcos.Refs(value, out int[] label) && label.Length == 1)
                {
                    axes.Labels[axis + "Label"] = Node(label[0]);
                }
                else if (name == "TickValues" && IsManual(saved, "TickValuesMode"))
                {
                    axes.Properties.Add((axis + "Tick", value));
                }
                else if (name == "TickLabels" && IsManual(saved, "TickLabelsMode"))
                {
                    axes.Properties.Add((axis + "TickLabel", value));
                }
            }
        }

        /// <summary>An axes's limits (when set), directions and scales, kept in its data space.</summary>
        private void DataSpace(FigNode axes, int id)
        {
            // A limit a script set is marked manual either as itself or, since R2025b keeps limits
            // with infinite ends, as its WithInfs twin - which then holds the value as it was set.
            IReadOnlyList<(string Name, JgsValue Value)> saved = store.Properties(id);
            foreach (string axis in new[] { "X", "Y", "Z" })
            {
                if (IsManual(saved, axis + "LimWithInfsMode") && Find(saved, axis + "LimWithInfs_I") is { } withInfs)
                {
                    axes.Properties.Add((axis + "Lim", withInfs));
                }
                else if (IsManual(saved, axis + "LimMode") && Find(saved, axis + "Lim_I") is { } limits)
                {
                    axes.Properties.Add((axis + "Lim", limits));
                }
            }

            foreach ((string raw, JgsValue value) in saved)
            {
                string name = Short(raw);
                if (name is "XDir" or "YDir" or "ZDir" or "XScale" or "YScale" or "ZScale")
                {
                    axes.Properties.Add((name, value));
                }
            }
        }

        /// <summary>An axes's colour limits and colour map, kept in its colour space, when set.</summary>
        private void ColorSpace(FigNode axes, int id)
        {
            IReadOnlyList<(string Name, JgsValue Value)> saved = store.Properties(id);
            foreach ((string raw, JgsValue value) in saved)
            {
                string name = Short(raw);
                if (name is "CLim" or "Colormap" && IsManual(saved, name + "Mode"))
                {
                    axes.Properties.Add((name, value));
                }
            }
        }

        private static bool IsManual(IReadOnlyList<(string Name, JgsValue Value)> saved, string mode) =>
            Find(saved, mode) is { } word && JgsBuiltins.IsTextScalar(word) && JgsBuiltins.TextOf(word) == "manual";

        private static JgsValue? Find(IReadOnlyList<(string Name, JgsValue Value)> saved, string name)
        {
            foreach ((string key, JgsValue value) in saved)
            {
                if (Short(key) == Short(name) || key == name)
                {
                    return value;
                }
            }

            return null;
        }

        /// <summary>
        /// A saved name as a script spells it: the last part of a class-qualified name, without the
        /// <c>_I</c> or <c>_IS</c> storage suffix.
        /// </summary>
        private static string Short(string raw)
        {
            int dot = raw.LastIndexOf('.');
            string name = dot >= 0 ? raw[(dot + 1)..] : raw;
            return name.EndsWith("_IS", StringComparison.Ordinal) ? name[..^3]
                : name.EndsWith("_I", StringComparison.Ordinal) ? name[..^2]
                : name;
        }

        private static bool IsFunctionCell(JgsValue value) =>
            value.AsCell.Length == 2 && value.AsCell[0].Type == JgsType.Number && value.AsCell[0].AsNumber == MatMcos.ReferenceMark;

        /// <summary>Whether a struct of application data reaches a subsystem object somewhere inside it.</summary>
        private static bool HoldsReference(JgsValue value) => value.Type switch
        {
            JgsType.Struct when !value.IsStructArray => value.AsStruct.Values.Any(static v => MatMcos.Refs(v, out _) || HoldsReference(v)),
            JgsType.Cell => value.AsCell.Any(static v => MatMcos.Refs(v, out _) || HoldsReference(v)),
            _ => false,
        };
    }

    // --- Building -----------------------------------------------------------------------------

    /// <summary>The makers this builder calls, by type, and how each is told its parent.</summary>
    private static readonly HashSet<string> PositionalParent = new(StringComparer.Ordinal)
    {
        "uicontrol", "uipanel", "uibuttongroup", "uimenu", "uicontextmenu", "uitable", "uitoolbar",
        "uipushtool", "uitoggletool",
    };

    private static readonly HashSet<string> ParentPair = new(StringComparer.Ordinal)
    {
        "axes", "line", "text", "patch", "surface", "image", "rectangle", "light", "hggroup", "hgtransform",
    };

    /// <summary>The names set first, in this order: what later values are read against.</summary>
    private static readonly string[] SetFirst = ["Style", "Units", "FontUnits", "String", "Max", "Min", "Data"];

    /// <summary>The names set last, in this order: what depends on everything before it.</summary>
    private static readonly string[] SetLast = ["Value", "Position", "OuterPosition"];

    /// <summary>What every builder skips: the maker's own business, or set after the tree exists.</summary>
    private static readonly HashSet<string> BuilderSkipped = new(StringComparer.OrdinalIgnoreCase)
    {
        "CreateFcn", "Parent", "Children", "Type",
    };

    /// <summary>
    /// The figure a read file describes, built invisible, its objects' <c>CreateFcn</c>s run parent
    /// first in <c>Children</c> order once everything exists (R2025b's order for the struct form),
    /// then shown as saved or as <paramref name="visible"/> says. Objects of a kind this build does
    /// not rebuild are left out and named in <paramref name="skipped"/>.
    /// </summary>
    public static FigureModel Build(
        FigNode root, Interpreter interpreter, bool? visible, List<string> skipped, int line, int col)
    {
        var builder = new Builder(interpreter, skipped, line, col);
        return builder.Run(root, visible);
    }

    private sealed class Builder(Interpreter interpreter, List<string> skipped, int line, int col)
    {
        private readonly Dictionary<double, GraphObject> _made = [];
        private readonly List<(GraphObject Target, FigNode Node)> _order = [];
        private readonly List<(FigNode Node, GraphObject Figure)> _late = [];

        public FigureModel Run(FigNode root, bool? visible)
        {
            if (root.Type != "figure")
            {
                throw new InvalidDataException(InvalidFigMessage);
            }

            var options = new List<JgsValue> { JgsValue.Str("Visible"), JgsValue.Str("off") };
            if (root.Property("IntegerHandle") is { } integer)
            {
                options.Add(JgsValue.Str("IntegerHandle"));
                options.Add(integer);
            }

            JgsValue handle = Call("figure", options);
            FigureModel figure = (FigureModel)Target(handle);
            Remember(root, figure);
            Apply(figure, root, ["Visible", "IntegerHandle"]);
            MakeChildren(figure, root);

            // Legends and colour bars need their axes, and links their targets.
            foreach ((FigNode node, GraphObject parent) in _late)
            {
                MakeAnnotation(node, parent);
            }

            // A child drawn into an axes can widen its limits, and a legend or a colour bar makes
            // room beside it; the file says where the axes were and what limits were set.
            foreach ((GraphObject made, FigNode node) in _order)
            {
                if (node.Type == "axes")
                {
                    foreach (string name in new[] { "XLim", "YLim", "ZLim", "CLim", "Position" })
                    {
                        if (node.Property(name) is { } value
                            && !(node.Property(name + "Mode") is { } mode && JgsBuiltins.IsTextScalar(mode) && JgsBuiltins.TextOf(mode) == "auto"))
                        {
                            Set(made, name, value);
                        }
                    }
                }
            }

            Link(root);
            Fire(root);

            string shown = visible is { } asked ? (asked ? "on" : "off")
                : root.Property("Visible") is { } saved && JgsBuiltins.IsTextScalar(saved) ? JgsBuiltins.TextOf(saved)
                : "on";
            Set(figure, "Visible", JgsValue.Str(shown));
            return figure;
        }

        private void MakeChildren(GraphObject parent, FigNode node)
        {
            // Made oldest first, so that each lands where Children lists it.
            for (int i = node.Children.Count - 1; i >= 0; i--)
            {
                FigNode child = node.Children[i];
                if (child.Type is "legend" or "colorbar")
                {
                    _late.Add((child, parent));
                    continue;
                }

                if (Make(parent, child) is { } made)
                {
                    MakeChildren(made, child);
                }
            }
        }

        private GraphObject? Make(GraphObject parent, FigNode node)
        {
            JgsValue parentHandle = JgsHandleRegistry.For(parent);
            var args = new List<JgsValue>();
            var given = new List<string>();
            if (PositionalParent.Contains(node.Type))
            {
                args.Add(parentHandle);
                if (node.Type == "uicontrol" && node.Property("Style") is { } style)
                {
                    args.Add(JgsValue.Str("Style"));
                    args.Add(style);
                    given.Add("Style");
                }
            }
            else if (node.Type == "axes")
            {
                args.Add(JgsValue.Str("Parent"));
                args.Add(parentHandle);
            }
            else if (ParentPair.Contains(node.Type))
            {
                // The drawing primitives take their axes and their data positionally.
                args.Add(parentHandle);
                Primitive(node, args, given);
            }
            else
            {
                skipped.Add(node.Type);
                return null;
            }

            GraphObject made;
            try
            {
                made = Target(Call(node.Type, args));
            }
            catch (JgsException)
            {
                skipped.Add(node.Type);
                return null;
            }

            Remember(node, made);
            Apply(made, node, given);
            if (node.Type == "axes")
            {
                Labels(made, node);
            }

            return made;
        }

        /// <summary>
        /// A drawing primitive's data, in the positional form its maker takes after the axes: a
        /// line's and a surface's coordinates, a text's place and string, an image's colours, a
        /// patch's faces and vertices (or its coordinates) as pairs.
        /// </summary>
        private static void Primitive(FigNode node, List<JgsValue> args, List<string> given)
        {
            void Take(string name, JgsValue value)
            {
                args.Add(value);
                given.Add(name);
            }

            JgsValue? x = node.Property("XData");
            JgsValue? y = node.Property("YData");
            JgsValue? z = node.Property("ZData");
            switch (node.Type)
            {
                case "line" when y is not null:
                    Take("XData", x ?? JgsMatrix.FromColumnMajor(
                        [.. Enumerable.Range(1, Math.Max(1, y.ArrayLength)).Select(static i => (double)i)], 1, Math.Max(1, y.ArrayLength)));
                    Take("YData", y);
                    if (z is not null && z.ArrayLength > 0)
                    {
                        Take("ZData", z);
                    }

                    break;
                case "surface" when z is not null && x is not null && y is not null:
                    Take("XData", x);
                    Take("YData", y);
                    Take("ZData", z);
                    break;
                case "image" when node.Property("CData") is { } colors:
                    Take("CData", colors);
                    break;
                case "text":
                    double[] place = node.Property("Position") is { } where ? Devices.DeviceChecks.NumberArray(where) : [0, 0];
                    args.Add(JgsValue.Number(place.Length > 0 ? place[0] : 0));
                    args.Add(JgsValue.Number(place.Length > 1 ? place[1] : 0));
                    if (place.Length > 2)
                    {
                        args.Add(JgsValue.Number(place[2]));
                    }

                    given.Add("Position");
                    Take("String", node.Property("String") ?? JgsValue.Str(string.Empty));
                    break;
                case "patch":
                    // patch takes coordinates here, one column per face: faces and vertices become
                    // those, and the colour is set with the rest of the saved properties.
                    if (node.Property("Vertices") is { } vertices && vertices.Type == JgsType.Array)
                    {
                        // One face through every vertex is the default a coordinate patch saves without.
                        int count = JgsMatrix.RowCount(vertices);
                        JgsValue faces = node.Property("Faces")
                            ?? JgsMatrix.FromColumnMajor([.. Enumerable.Range(1, count).Select(static i => (double)i)], 1, count);
                        (x, y, z) = FaceColumns(faces, vertices);
                        given.AddRange(["Faces", "Vertices"]);
                    }

                    if (x is not null && y is not null)
                    {
                        Take("XData", x);
                        Take("YData", y);
                        if (z is not null && Devices.DeviceChecks.NumberArray(z).Any(static v => v != 0))
                        {
                            Take("ZData", z);
                        }

                        args.Add(JgsMatrix.FromColumnMajor([0, 0, 0], 1, 3));
                    }

                    break;
            }
        }

        /// <summary>A patch's faces and vertices as coordinate columns, one per face; a short face is padded with NaN.</summary>
        private static (JgsValue X, JgsValue Y, JgsValue? Z) FaceColumns(JgsValue faces, JgsValue vertices)
        {
            double[] f = Devices.DeviceChecks.NumberArray(faces);
            double[] v = Devices.DeviceChecks.NumberArray(vertices);
            int faceCount = faces.Type == JgsType.Number ? 1 : JgsMatrix.RowCount(faces);
            int corners = faceCount == 0 ? 0 : f.Length / faceCount;
            int vertexCount = vertices.Type == JgsType.Number ? 1 : JgsMatrix.RowCount(vertices);
            int dims = vertexCount == 0 ? 0 : v.Length / vertexCount;
            var columns = new double[3][];
            for (int d = 0; d < 3; d++)
            {
                columns[d] = new double[corners * faceCount];
            }

            for (int face = 0; face < faceCount; face++)
            {
                for (int corner = 0; corner < corners; corner++)
                {
                    double index = f[face + (corner * faceCount)];
                    for (int d = 0; d < 3; d++)
                    {
                        columns[d][corner + (face * corners)] = double.IsNaN(index) || index < 1 || index > vertexCount || d >= dims
                            ? (d >= dims && !double.IsNaN(index) ? 0 : double.NaN)
                            : v[(int)index - 1 + (d * vertexCount)];
                    }
                }
            }

            return (JgsMatrix.FromColumnMajor(columns[0], corners, faceCount),
                JgsMatrix.FromColumnMajor(columns[1], corners, faceCount),
                dims >= 3 ? JgsMatrix.FromColumnMajor(columns[2], corners, faceCount) : null);
        }

        /// <summary>A legend or a colour bar, made on its axes once the axes exist.</summary>
        private void MakeAnnotation(FigNode node, GraphObject parent)
        {
            GraphObject? axes = null;
            foreach ((string name, double key) in node.Links)
            {
                if (name.Equals("Axes", StringComparison.OrdinalIgnoreCase) && _made.TryGetValue(key, out GraphObject? peer))
                {
                    axes = peer;
                }
            }

            if (axes is null)
            {
                skipped.Add(node.Type);
                return;
            }

            var args = new List<JgsValue> { JgsHandleRegistry.For(axes) };
            if (node.Type == "legend" && node.Property("String") is { } strings)
            {
                args.Add(strings);
            }

            GraphObject made;
            try
            {
                made = Target(Call(node.Type, args));
            }
            catch (JgsException)
            {
                skipped.Add(node.Type);
                return;
            }

            Remember(node, made);
            Apply(made, node, ["String", "Position", "Units"]);
        }

        /// <summary>An axes's title and labels: the axes's own text objects, given the saved text's properties.</summary>
        private void Labels(GraphObject axes, FigNode node)
        {
            JgsHandleEntry entry = JgsHandleRegistry.EntryFor(axes);
            foreach ((string which, FigNode label) in node.Labels)
            {
                JgsValue handle;
                try
                {
                    handle = JgsGraphicsProperties.Get(entry, which, line, col);
                }
                catch (JgsException)
                {
                    continue;
                }

                if (JgsHandleRegistry.TryGet(handle, out JgsHandleEntry? text))
                {
                    Apply(text.Target, label, ["Position", "Units", "HorizontalAlignment", "VerticalAlignment", "Rotation"]);
                }
            }
        }

        /// <summary>
        /// The saved properties, set one by one through the property table. What a mode marks
        /// <c>'auto'</c> was computed and is left to compute again; a mode itself is never set; a
        /// value this build refuses is left at its default, as a property it does not have is.
        /// </summary>
        private void Apply(GraphObject target, FigNode node, IReadOnlyCollection<string> except)
        {
            var auto = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach ((string name, JgsValue value) in node.Properties)
            {
                if (name.Length > 4 && name.EndsWith("Mode", StringComparison.Ordinal)
                    && JgsBuiltins.IsTextScalar(value) && JgsBuiltins.TextOf(value) == "auto")
                {
                    auto.Add(name[..^4]);
                }
            }

            var names = new HashSet<string>(node.Properties.Select(static p => p.Name), StringComparer.OrdinalIgnoreCase);
            if (names.Contains("Position"))
            {
                auto.Add("OuterPosition"); // the saved Position is the one kept; its outer box follows from it
            }
            IEnumerable<(string Name, JgsValue Value)> wanted = node.Properties.Where(p =>
                !except.Contains(p.Name, StringComparer.OrdinalIgnoreCase)
                && !BuilderSkipped.Contains(p.Name)
                && !(auto.Contains(p.Name) && p.Name is not ("Position" or "Units"))
                && !(p.Name.EndsWith("Mode", StringComparison.Ordinal) && p.Name.Length > 4
                     && (names.Contains(p.Name[..^4]) || JgsGraphicsProperties.TryFind(target, p.Name[..^4], out _))));

            foreach ((string name, JgsValue value) in wanted.OrderBy(p => Rank(p.Name)))
            {
                Set(target, name, value);
            }

            foreach ((string name, JgsValue value) in node.AppData)
            {
                try
                {
                    Call("setappdata", [JgsHandleRegistry.For(target), JgsValue.Str(name), value]);
                }
                catch (JgsException)
                {
                    // an entry this build cannot hold is left out, as an unknown property is
                }
            }
        }

        private static int Rank(string name)
        {
            int first = Array.FindIndex(SetFirst, n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (first >= 0)
            {
                return first - 100;
            }

            int last = Array.FindIndex(SetLast, n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
            return last >= 0 ? 100 + last : 0;
        }

        private void Set(GraphObject target, string name, JgsValue value)
        {
            try
            {
                JgsGraphicsProperties.Set(JgsHandleRegistry.EntryFor(target), name, value, line, col);
            }
            catch (JgsException)
            {
                // left at its default: a name this build does not have, or a value it refuses
            }
        }

        /// <summary>The properties that name another saved object, now that every object exists.</summary>
        private void Link(FigNode node)
        {
            if (_made.TryGetValue(node.Key, out GraphObject? target))
            {
                foreach ((string name, double key) in node.Links)
                {
                    if (!name.Equals("Axes", StringComparison.OrdinalIgnoreCase) && _made.TryGetValue(key, out GraphObject? linked))
                    {
                        Set(target, name.Equals("UIContextMenu", StringComparison.OrdinalIgnoreCase) ? "ContextMenu" : name,
                            JgsHandleRegistry.For(linked));
                    }
                }
            }

            foreach (FigNode child in node.Children)
            {
                Link(child);
            }
        }

        /// <summary>Each object's <c>CreateFcn</c>, set and run: parent first, children in <c>Children</c> order.</summary>
        private void Fire(FigNode node)
        {
            if (_made.TryGetValue(node.Key, out GraphObject? target) && node.Property("CreateFcn") is { } create
                && !(JgsBuiltins.IsTextScalar(create) && JgsBuiltins.TextOf(create).Length == 0))
            {
                Set(target, "CreateFcn", create);
                JgsCallbackDispatcher.Current?.FireCreateFcn(target);
            }

            foreach (FigNode child in node.Children)
            {
                Fire(child);
            }
        }

        private void Remember(FigNode node, GraphObject made)
        {
            if (!double.IsNaN(node.Key))
            {
                _made[node.Key] = made;
            }

            _order.Add((made, node));
        }

        private JgsValue Call(string name, IReadOnlyList<JgsValue> args)
        {
            if (!interpreter.Globals.Builtins.TryGet(name, out JgsValue function) || function.Type != JgsType.Function)
            {
                throw new JgsRuntimeException(line, col, $"openfig: '{name}' is not available here.");
            }

            return function.AsCallable.Call(args, line, col);
        }

        private GraphObject Target(JgsValue handle) =>
            JgsHandleRegistry.TryGet(handle, out JgsHandleEntry? entry)
                ? entry.Target
                : throw new JgsRuntimeException(line, col, "openfig: a maker did not answer a graphics object.");
    }
}
