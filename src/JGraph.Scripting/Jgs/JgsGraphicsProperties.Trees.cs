using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Data;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// One style added to a table, a tree, a list or a drop-down, as <c>StyleConfigurations</c> lists
/// it (app-building plan, U9): the target word, the index as it was given, the style object as
/// it was when it was added, and the rule the window draws by.
/// </summary>
internal sealed record JgsStyleRow(string Target, JgsValue TargetIndex, JgsValue Style, UiStyleRule Rule);

/// <summary>
/// The properties of <c>uitree</c>, <c>uitree(…, 'checkbox')</c> and <c>uitreenode</c> (U9), and
/// the <c>StyleConfigurations</c> every styleable component answers. R2025b's names, coercions and
/// refusals, recorded headless (probes <c>u9_matrix</c>, <c>u9_behave</c>, <c>u9_forms</c>).
/// </summary>
internal static partial class JgsGraphicsProperties
{
    private static UiTreeModel Tree(JgsHandleEntry entry) => (UiTreeModel)entry.Target;

    private static UiTreeNodeModel Node(JgsHandleEntry entry) => (UiTreeNodeModel)entry.Target;

    /// <summary>Handles as a column, which is how R2025b lists a tree's nodes.</summary>
    internal static JgsValue HandleColumn(IReadOnlyList<GraphObject> objects)
    {
        if (objects.Count == 0)
        {
            return JgsMatrix.FromColumnMajor([], 0, 0);
        }

        // In the order given: a tree lists its nodes first first, and a selection in the order it
        // was made (R2025b, probe u9_behave), where a figure's children are newest first.
        JgsValue row = HandleList(objects);
        row.Reshape(objects.Count, 1);
        return row;
    }

    /// <summary>The nodes a value names, every one standing in <paramref name="tree"/>; null when it names anything else.</summary>
    private static List<UiTreeNodeModel>? NodesOf(JgsValue value, UiTreeModel tree)
    {
        if (value.Type is not (JgsType.Number or JgsType.Array) || value.IsStringArray || value.IsCharMatrix)
        {
            return null;
        }

        int count = value.Type == JgsType.Array ? value.ArrayLength : 1;
        var nodes = new List<UiTreeNodeModel>(count);
        for (int i = 0; i < count; i++)
        {
            JgsValue one = value.Type == JgsType.Array ? value.ElementAt(i) : value;
            if (!JgsHandleRegistry.TryGet(one, out JgsHandleEntry? named) || named.Target is not UiTreeNodeModel node || !tree.Holds(node))
            {
                return null;
            }

            nodes.Add(node);
        }

        return nodes;
    }

    private static void AddTreeBlock(IDictionary<string, GraphicsProperty> table)
    {
        // A tree's children are its nodes, in the order they stand (first first; probe u9_behave).
        Put(table, "Children",
            entry => HandleColumn(VisibleChildrenOf(entry.Target)),
            (entry, value, line, col) => SetChildren(entry, value, line, col));

        foreach (string name in new[] { "SelectionChangedFcn", "NodeExpandedFcn", "NodeCollapsedFcn", "NodeTextChangedFcn", "ClickedFcn", "DoubleClickedFcn" })
        {
            AddNamedSlot(table, name);
        }

        Put(table, "Editable",
            entry => OnOff(Tree(entry).Editable),
            (entry, value, line, col) => Tree(entry).Editable = OnOffState(entry, "Editable", value, line, col));
        Options(table, "Editable", OnOffWords);
        Put(table, "BackgroundColor",
            entry => UiColorValue(Leaf(entry).BackgroundColor),
            (entry, value, line, col) => Leaf(entry).BackgroundColor = SolidColor(entry, "BackgroundColor", value, line, col));
        Put(table, "SelectedNodes",
            entry => HandleColumn([.. Tree(entry).Selected]),
            (entry, value, line, col) =>
            {
                UiTreeModel tree = Tree(entry);
                string id = tree.CheckBoxes ? "selectedCheckBoxTreeNodes" : tree.Multiselect ? "selectedNodesInvalidInputMultiSelectOn" : "selectedNodesInvalidInputMultiSelectOff";
                string sentence = tree.CheckBoxes
                    ? "'SelectedNodes' must be an empty array or a 1-by-1 TreeNode object that is a child in the CheckBoxTree."
                    : tree.Multiselect
                        ? "When 'Multiselect' is 'on', 'SelectedNodes' must be an empty array or an array of TreeNode objects that are children in the Tree."
                        : "When 'Multiselect' is 'off', 'SelectedNodes' must be an empty array or a 1-by-1 TreeNode object that is a child in the Tree.";
                if (value.Type == JgsType.Array && value.ArrayLength == 0 && !value.IsStringArray)
                {
                    tree.Selected = [];
                    return;
                }

                List<UiTreeNodeModel>? nodes = NodesOf(value, tree);
                if (nodes is null || (nodes.Count > 1 && !tree.Multiselect))
                {
                    throw UiError(entry, id, sentence, line, col);
                }

                tree.Selected = [.. nodes.Distinct()];
            });
        Put(table, "StyleConfigurations", StyleConfigurationsValue);
    }

    /// <summary>What a plain tree has and a check box tree has not, and the other way about.</summary>
    private static void AddTreeKindBlock(bool checkBoxes, IDictionary<string, GraphicsProperty> table)
    {
        if (!checkBoxes)
        {
            Put(table, "Multiselect",
                entry => OnOff(Tree(entry).Multiselect),
                (entry, value, line, col) =>
                {
                    UiTreeModel tree = Tree(entry);
                    tree.Multiselect = OnOffState(entry, "Multiselect", value, line, col);
                    if (!tree.Multiselect && tree.Selected.Count > 1)
                    {
                        tree.Selected = [tree.Selected[0]];
                    }
                });
            Options(table, "Multiselect", OnOffWords);
            return;
        }

        AddNamedSlot(table, "CheckedNodesChangedFcn");
        Put(table, "CheckedNodes",
            entry => HandleColumn([.. Tree(entry).Checked]),
            (entry, value, line, col) =>
            {
                UiTreeModel tree = Tree(entry);
                if (value.Type == JgsType.Array && value.ArrayLength == 0 && !value.IsStringArray)
                {
                    tree.Checked = [];
                    return;
                }

                List<UiTreeNodeModel> nodes = NodesOf(value, tree)
                    ?? throw UiError(entry, "checkedNodesInvalid",
                        "'CheckedNodes' must be an empty array or an array of TreeNode objects that are children in the Tree.", line, col);
                tree.Checked = UiTreeModel.CheckedFrom(nodes);
            });
    }

    /// <summary>The names a tree node answers to, and nothing else (R2025b, probe <c>u9_matrix</c>: fifteen).</summary>
    private static readonly HashSet<string> TreeNodeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Text", "NodeData", "Children", "Parent", "HandleVisibility", "ContextMenu", "UIContextMenu", "BusyAction", "BeingDeleted",
        "Interruptible", "CreateFcn", "DeleteFcn", "Type", "Tag", "UserData", "Icon",
    };

    private static void AddTreeNodeBlock(IDictionary<string, GraphicsProperty> table)
    {
        foreach (string name in table.Keys.Where(static name => !TreeNodeNames.Contains(name)).ToList())
        {
            table.Remove(name);
        }

        Unlist(table, "UIContextMenu");
        Put(table, "Tag",
            entry => JgsValue.Str(entry.Target.Tag ?? string.Empty),
            (entry, value, line, col) => entry.Target.Tag = OneText(entry, "Tag", value, line, col));
        Put(table, "HandleVisibility",
            entry => JgsValue.Str(entry.HandleVisibility),
            (entry, value, line, col) =>
                entry.HandleVisibility = EnumWord(entry, "HandleVisibility", value, HandleVisibilityWords, line, col));
        Put(table, "BusyAction",
            entry => JgsValue.Str(entry.BusyActionQueues ? "queue" : "cancel"),
            (entry, value, line, col) =>
                entry.BusyActionQueues = EnumWord(entry, "BusyAction", value, BusyActionWords, line, col) == "queue");
        Put(table, "Interruptible",
            entry => OnOff(entry.Interruptible),
            (entry, value, line, col) => entry.Interruptible = OnOffState(entry, "Interruptible", value, line, col));
        Options(table, "HandleVisibility", HandleVisibilityWords);
        Options(table, "BusyAction", "queue", "cancel");
        Options(table, "Interruptible", OnOffWords);
        Put(table, "Text",
            entry => JgsValue.Str(Node(entry).Text),
            (entry, value, line, col) =>
            {
                WarnDatetimeAgainstText(value, Node(entry).Text.Length > 0);
                if (value.IsCharMatrix || (value.IsStringArray && value.ArrayLength != 1) || !JgsBuiltins.IsTextScalar(value))
                {
                    throw UiError(entry, "invalidText", "'Text' must be a character vector or a string scalar.", line, col);
                }

                Node(entry).Text = MissingAsEmpty(JgsBuiltins.TextOf(value));
            });
        Put(table, "NodeData",
            entry => entry.NodeData ?? JgsMatrix.FromColumnMajor([], 0, 0),
            (entry, value, _, _) => entry.NodeData = JgsValue.Share(value));
        Put(table, "Icon",
            entry => entry.UiCData ?? JgsValue.Str(Node(entry).IconSource),
            (entry, value, line, col) =>
            {
                (UiImage? image, string source, JgsValue? kept) = IconOf(entry, value, line, col);
                Node(entry).Icon = image;
                Node(entry).IconSource = source;
                entry.UiCData = kept;
            });
        Put(table, "Children",
            entry => HandleColumn(VisibleChildrenOf(entry.Target)),
            (entry, value, line, col) => SetChildren(entry, value, line, col));
        AddSameFigureContextMenu(table);
        WidenCallbacks(table);
    }

    /// <summary>
    /// A component's <c>ContextMenu</c>, with R2025b's refusals (probe <c>u9_behave</c>): a menu
    /// of another figure is refused, a handle to anything else likewise, and a menu since deleted
    /// reads as none.
    /// </summary>
    private static void AddSameFigureContextMenu(IDictionary<string, GraphicsProperty> table)
    {
        foreach (string name in new[] { "ContextMenu", "UIContextMenu" })
        {
            string captured = name;
            table[captured] = new GraphicsProperty(captured,
                entry => entry.ContextMenu is { BeingDeleted: false } menu && JgsHandleRegistry.TryGetEntry(menu, out _)
                    ? JgsHandleRegistry.For(menu)
                    : JgsMatrix.FromColumnMajor([], 0, 0),
                (entry, value, line, col) =>
                {
                    if ((value.Type == JgsType.Array && value.ArrayLength == 0 && !value.IsStringArray)
                        || (JgsBuiltins.IsTextScalar(value) && JgsBuiltins.TextOf(value).Length == 0))
                    {
                        entry.ContextMenu = null;
                        return;
                    }

                    JgsValue first = value.Type == JgsType.Array && value.ArrayLength > 0 ? value.ElementAt(0) : value;
                    if (!JgsHandleRegistry.TryGet(first, out JgsHandleEntry? menu))
                    {
                        throw ComponentError(entry, captured, "MATLAB:datatypes:handleoremptydatatype:InvalidHGHandle",
                            "The value set for this property must be a valid HG handle.", line, col);
                    }

                    if (menu.Target is not ContextMenuModel)
                    {
                        throw new JgsRuntimeException(line, col, "MATLAB:hgutils:InvalidContextMenu", "Handle must be a uicontextmenu.");
                    }

                    FigureModel? own = FigureOf(entry.Target);
                    if (own is not null && menu.Target.Parent is FigureModel home && !ReferenceEquals(home, own))
                    {
                        throw new JgsRuntimeException(line, col, "MATLAB:hg:InvalidObject",
                            "The UIContextMenu's parent figure is not the same as the parent figure of this object.");
                    }

                    entry.ContextMenu = menu.Target;
                })
            {
                Listed = name == "ContextMenu",
            };
        }
    }

    /// <summary>The figure an object stands in, through whatever holds it; null for one in none.</summary>
    internal static FigureModel? FigureOf(GraphObject target)
    {
        for (GraphObject? up = target; up is not null; up = up.Parent)
        {
            if (up is FigureModel figure)
            {
                return figure;
            }

            if (up is UiObject component)
            {
                return component.Figure;
            }
        }

        return null;
    }

    // --- styles ----------------------------------------------------------------------------------

    /// <summary>Whether a component takes <c>addStyle</c>: a table, a tree, a list box or a drop-down.</summary>
    internal static bool TakesStyles(GraphObject target) => target is UiTableModel or UiTreeModel or UiListBoxModel or UiDropDownModel;

    /// <summary>
    /// <c>StyleConfigurations</c>: a table of the styles added, one row each, in order — the target
    /// word, the index given, and the style object (U9). R2025b's <c>Target</c> is categorical;
    /// here it is text, and its <c>Style</c> column is a cell of style objects.
    /// </summary>
    internal static JgsValue StyleConfigurationsValue(JgsHandleEntry entry)
    {
        List<JgsStyleRow> rows = entry.Styles ?? [];
        var targets = new string?[rows.Count];
        var indices = new JgsValue[rows.Count];
        var styles = new JgsValue[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            targets[i] = rows[i].Target;
            indices[i] = rows[i].TargetIndex;
            styles[i] = rows[i].Style;
        }

        JgsValue indexColumn = JgsValue.Cell(indices);
        indexColumn.Reshape(rows.Count, 1);
        JgsValue styleColumn = JgsValue.Cell(styles);
        styleColumn.Reshape(rows.Count, 1);
        var table = new Table(
        [
            new TextColumn("Target", targets),
            new JgsValueColumn("TargetIndex", indexColumn),
            new JgsValueColumn("Style", styleColumn),
        ]);
        return JgsValue.Table(table);
    }

    /// <summary>
    /// Moves a node under <paramref name="into"/> — a tree or a node — beside <paramref name="beside"/>
    /// when one is named, else at the end. A node cannot go under one of its own (R2025b's
    /// <c>InvalidParentToChild</c>); a tree it leaves lets go of it.
    /// </summary>
    internal static void MoveTreeNode(UiTreeNodeModel node, GraphObject into, UiTreeNodeModel? beside, bool after, int line, int col)
    {
        if (node.Holds(into))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:container:InvalidParentToChild",
                "You are trying to set the parent property to a descendant child object.");
        }

        GraphObjectCollection<UiTreeNodeModel> target = into switch
        {
            UiTreeModel tree => tree.Nodes,
            UiTreeNodeModel above => above.Nodes,
            _ => throw new JgsRuntimeException(line, col, "MATLAB:ui:TreeNode:invalidParent", "'Parent' must be a valid Tree object or TreeNode object."),
        };
        UiTreeModel? oldTree = node.Tree;
        UiTreeModel? newTree = into as UiTreeModel ?? (into as UiTreeNodeModel)?.Tree;
        if (oldTree is not null && !ReferenceEquals(oldTree, newTree))
        {
            oldTree.Forget(node);
        }

        using (GraphObjectLifecycle.SuppressNotifications())
        {
            (node.Parent switch
            {
                UiTreeModel from => from.Nodes,
                UiTreeNodeModel above => above.Nodes,
                _ => null,
            })?.Remove(node);
            int at = beside is not null && target.Contains(beside) ? target.IndexOf(beside) + (after ? 1 : 0) : target.Count;
            target.Insert(System.Math.Clamp(at, 0, target.Count), node);
        }

        if (FigureOf(into) is { } figure)
        {
            JG.TouchFigure(figure);
        }
    }

    /// <summary>A colour by its name or hexadecimal code, for a style; null for a word that is none.</summary>
    internal static UiColor? NamedColorOf(string word) => NamedColor(word);

    /// <summary>A warning raised while a property or a style is written.</summary>
    internal static void WarnOnce(string identifier, string text) => PropertyWarning(identifier, text);

    /// <summary>An m-by-n-by-3 array as a picture, each class spanning its own range; null for an empty one.</summary>
    internal static UiImage? PictureFromCube(JgsValue value)
    {
        string kind = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
        int[] dims = value.Dims;
        if (dims.Length != 3 || dims[2] != 3)
        {
            return null;
        }

        double[] data = JgsBuiltins.ToDoubles("Icon", value, 0, 0);
        double high = kind switch
        {
            "uint8" => 255,
            "uint16" => 65535,
            _ => 1,
        };
        int rows = dims[0];
        int cols = dims[1];
        if (rows == 0 || cols == 0)
        {
            return null;
        }

        var pixels = new byte[rows * cols * 4];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int at = ((r * cols) + c) * 4;
                byte Channel(int plane) => (byte)System.Math.Round(255 * System.Math.Clamp(data[r + (c * rows) + (plane * rows * cols)] / high, 0, 1));
                pixels[at] = Channel(2);
                pixels[at + 1] = Channel(1);
                pixels[at + 2] = Channel(0);
                pixels[at + 3] = 255;
            }
        }

        return new UiImage(cols, rows, pixels);
    }

    /// <summary>Hands the component's model the rules of its style rows, in order.</summary>
    internal static void SyncStyles(JgsHandleEntry entry)
    {
        if (entry.Target is UiComponentModel component)
        {
            component.Styles = [.. (entry.Styles ?? []).Select(static row => row.Rule)];
        }
    }
}
