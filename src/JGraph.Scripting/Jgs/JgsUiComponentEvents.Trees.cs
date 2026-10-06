using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// What a person did to a tree (app-building plan, U9): a node picked, opened, closed, retyped or
/// ticked, or the tree clicked. The model is written first, as for every component; the event
/// classes are R2025b's (<c>meta.class</c>, probe <c>u9_behave</c>): <c>SelectedNodesChangedData</c>,
/// <c>NodeExpandedData</c>, <c>NodeCollapsedData</c>, <c>NodeTextChangedData</c>,
/// <c>CheckedNodesChangedData</c>, and <c>ClickedData</c> with a <c>TreeInteraction</c>.
/// R2025b runs none of them headless; what each holds follows MathWorks' documentation.
/// </summary>
internal static partial class JgsUiComponentEvents
{
    /// <summary>The actions a window reports for a tree.</summary>
    public const string NodeSelect = "nodeselect";
    public const string NodeExpand = "nodeexpand";
    public const string NodeCollapse = "nodecollapse";
    public const string NodeText = "nodetext";
    public const string NodeCheck = "nodecheck";

    private static JgsValue Nodes(IEnumerable<UiTreeNodeModel> nodes) =>
        JgsGraphicsProperties.HandleColumn([.. nodes.Where(static n => !n.BeingDeleted)]);

    private static GraphicsEvent? PrepareTree(GraphicsEvent raised, UiTreeModel tree, JgsHandleEntry entry, JgsValue source)
    {
        _ = entry;
        switch (raised.Action)
        {
            case NodeSelect when raised.Interim is UiTreeNodeModel[] picked:
            {
                IReadOnlyList<UiTreeNodeModel> before = tree.Selected;
                List<UiTreeNodeModel> wanted = [.. picked.Where(tree.Holds).Distinct()];
                if (!tree.Multiselect && wanted.Count > 1)
                {
                    wanted = [wanted[0]];
                }

                tree.UserWriteSeq = raised.UserSeq;
                if (wanted.SequenceEqual(before))
                {
                    return null;
                }

                tree.Selected = wanted;
                return raised with
                {
                    Action = "SelectionChangedFcn",
                    Interim = JgsUiEventData.Make(Prefix + "SelectedNodesChangedData", source, "SelectionChanged", new()
                    {
                        ["SelectedNodes"] = Nodes(wanted),
                        ["PreviousSelectedNodes"] = Nodes(before),
                    }),
                };
            }

            case NodeExpand or NodeCollapse when raised.Interim is UiTreeNodeModel node && tree.Holds(node):
            {
                bool open = raised.Action == NodeExpand;
                tree.UserWriteSeq = raised.UserSeq;
                if (node.Expanded == open)
                {
                    return null;
                }

                node.Expanded = open;
                return raised with
                {
                    Action = open ? "NodeExpandedFcn" : "NodeCollapsedFcn",
                    Interim = JgsUiEventData.Make(Prefix + (open ? "NodeExpandedData" : "NodeCollapsedData"), source, open ? "NodeExpanded" : "NodeCollapsed", new()
                    {
                        ["Node"] = JgsHandleRegistry.For(node),
                    }),
                };
            }

            case NodeText when raised.Interim is object[] { Length: 2 } edit && edit[0] is UiTreeNodeModel retyped && edit[1] is string text && tree.Holds(retyped):
            {
                string before = retyped.Text;
                tree.UserWriteSeq = raised.UserSeq;
                if (!tree.Editable || before == text)
                {
                    return null;
                }

                retyped.Text = text;
                return raised with
                {
                    Action = "NodeTextChangedFcn",
                    Interim = JgsUiEventData.Make(Prefix + "NodeTextChangedData", source, "NodeTextChanged", new()
                    {
                        ["Node"] = JgsHandleRegistry.For(retyped),
                        ["Text"] = JgsValue.Str(text),
                        ["PreviousText"] = JgsValue.Str(before),
                    }),
                };
            }

            case NodeCheck when raised.Interim is object[] { Length: 2 } tick && tick[0] is UiTreeNodeModel ticked && tick[1] is bool on && tree.CheckBoxes && tree.Holds(ticked):
            {
                IReadOnlyList<UiTreeNodeModel> before = tree.Checked;
                List<UiTreeNodeModel> after;
                if (on)
                {
                    after = UiTreeModel.CheckedFrom([.. before, ticked]);
                }
                else
                {
                    // The node, everything under it and everything above it come off; what is left
                    // settles as a written list would.
                    after = UiTreeModel.CheckedFrom(before.Where(kept => !ticked.Holds(kept) && !kept.Holds(ticked)));
                }

                tree.UserWriteSeq = raised.UserSeq;
                if (after.SequenceEqual(before))
                {
                    return null;
                }

                tree.Checked = after;
                static JgsValue Parents(IEnumerable<UiTreeNodeModel> nodes) => Nodes(nodes.Where(static n => n.Nodes.Count > 0));
                static JgsValue Leaves(IEnumerable<UiTreeNodeModel> nodes) => Nodes(nodes.Where(static n => n.Nodes.Count == 0));
                JgsValue Indeterminate(IReadOnlyList<UiTreeNodeModel> nodes) => Nodes(tree.AllNodes().Where(n =>
                    n.Nodes.Count > 0 && !nodes.Contains(n) && n.Descendants().Any(nodes.Contains)));
                return raised with
                {
                    Action = "CheckedNodesChangedFcn",
                    Interim = JgsUiEventData.Make(Prefix + "CheckedNodesChangedData", source, "CheckedNodesChanged", new()
                    {
                        ["CheckedNodes"] = Nodes(after),
                        ["PreviousCheckedNodes"] = Nodes(before),
                        ["ParentCheckedNodes"] = Parents(after),
                        ["PreviousParentCheckedNodes"] = Parents(before),
                        ["LeafCheckedNodes"] = Leaves(after),
                        ["PreviousLeafCheckedNodes"] = Leaves(before),
                        ["IndeterminateCheckedNodes"] = Indeterminate(after),
                        ["PreviousIndeterminateCheckedNodes"] = Indeterminate(before),
                    }),
                };
            }

            case Clicked or DoubleClicked:
            {
                bool twice = raised.Action == DoubleClicked;
                UiTreeNodeModel? node = raised.Interim as UiTreeNodeModel;
                JgsValue information = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
                {
                    ["Node"] = node is not null && tree.Holds(node) ? JgsHandleRegistry.For(node) : Empty,
                    ["Level"] = node is not null && tree.Holds(node) ? JgsValue.Number(node.Level) : Empty,
                    ["Location"] = Empty,
                    ["ScreenLocation"] = Empty,
                });
                information.SetClassName(Prefix + "TreeInteraction");
                return raised with
                {
                    Action = twice ? "DoubleClickedFcn" : "ClickedFcn",
                    Interim = JgsUiEventData.Make(Prefix + (twice ? "DoubleClickedData" : "ClickedData"), source,
                        twice ? "DoubleClicked" : "Clicked", new() { ["InteractionInformation"] = information }),
                };
            }

            default:
                return null;
        }
    }
}
