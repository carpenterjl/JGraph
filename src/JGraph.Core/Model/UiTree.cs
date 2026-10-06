using System.ComponentModel;
using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>
/// One node of a <c>uitree</c> (<c>matlab.ui.container.TreeNode</c>; app-building plan, U9). A
/// node has text, a picture, and nodes of its own; it has no rectangle and no visibility, and is
/// not a component. Whether it stands open is kept here too: a script's <c>expand</c> and
/// <c>collapse</c> write it, and a person's click in the window reports it.
/// </summary>
public sealed class UiTreeNodeModel : GraphObject
{
    private string _text = "Tree Node";
    private UiImage? _icon;
    private string _iconSource = string.Empty;
    private bool _expanded;

    public UiTreeNodeModel()
    {
        Name = "TreeNode";
        Nodes = new GraphObjectCollection<UiTreeNodeModel>(this);
    }

    /// <summary>The nodes under this one, in the order they stand.</summary>
    [Browsable(false)]
    public GraphObjectCollection<UiTreeNodeModel> Nodes { get; }

    [Browsable(false)]
    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value ?? string.Empty, InvalidationKind.Ui);
    }

    /// <summary>The picture beside the text, or null for none.</summary>
    [Browsable(false)]
    public UiImage? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value, InvalidationKind.Ui);
    }

    /// <summary>The file <c>Icon</c> named, when it named one.</summary>
    [Browsable(false)]
    public string IconSource
    {
        get => _iconSource;
        set => SetProperty(ref _iconSource, value ?? string.Empty, InvalidationKind.Ui);
    }

    /// <summary>Whether the node stands open, its children showing.</summary>
    [Browsable(false)]
    public bool Expanded
    {
        get => _expanded;
        set => SetProperty(ref _expanded, value, InvalidationKind.Ui);
    }

    /// <summary>The tree this node stands in, through however many nodes; null for one that stands in none.</summary>
    [Browsable(false)]
    public UiTreeModel? Tree
    {
        get
        {
            for (GraphObject? up = Parent; up is not null; up = up.Parent)
            {
                if (up is UiTreeModel tree)
                {
                    return tree;
                }
            }

            return null;
        }
    }

    /// <summary>How deep the node stands: 1 for a node of the tree itself, 2 for one under that, and so on; 0 for one in no tree.</summary>
    [Browsable(false)]
    public int Level
    {
        get
        {
            int depth = 0;
            for (GraphObject? up = Parent; up is not null; up = up.Parent)
            {
                depth++;
                if (up is UiTreeModel)
                {
                    return depth;
                }
            }

            return 0;
        }
    }

    /// <summary>Whether <paramref name="other"/> is this node or stands somewhere under it.</summary>
    public bool Holds(GraphObject other)
    {
        for (GraphObject? up = other; up is not null; up = up.Parent)
        {
            if (ReferenceEquals(up, this))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Every node under this one, children before grandchildren, each level in order.</summary>
    public IEnumerable<UiTreeNodeModel> Descendants()
    {
        var queue = new Queue<UiTreeNodeModel>(Nodes);
        while (queue.Count > 0)
        {
            UiTreeNodeModel next = queue.Dequeue();
            yield return next;
            foreach (UiTreeNodeModel child in next.Nodes)
            {
                queue.Enqueue(child);
            }
        }
    }

    /// <summary>What the window needs to draw this node and those under it.</summary>
    public UiTreeNodeFrame Snapshot() => new(
        this, _text, _icon, _expanded, [.. Nodes.Select(static node => node.Snapshot())], Tag ?? string.Empty);
}

/// <summary>
/// MATLAB's <c>uitree</c> and <c>uitree(…, 'checkbox')</c> (<c>matlab.ui.container.Tree</c>,
/// <c>CheckBoxTree</c>; U9): a component whose children are nodes. Its selection, and a check box
/// tree's checked nodes, are lists of nodes in the order they were given, which is the order
/// R2025b reads them back in.
/// </summary>
public class UiTreeModel : UiComponentModel
{
    private IReadOnlyList<UiTreeNodeModel> _selected = [];
    private IReadOnlyList<UiTreeNodeModel> _checked = [];
    private bool _multiselect;
    private bool _editable;

    public UiTreeModel()
        : this(checkBoxes: false)
    {
    }

    protected UiTreeModel(bool checkBoxes)
        : base(checkBoxes ? "CheckBoxTree" : "Tree", new Rect2D(20, 20, 150, 300))
    {
        CheckBoxes = checkBoxes;
        BackgroundColor = White;
        Nodes = new GraphObjectCollection<UiTreeNodeModel>(this);
    }

    /// <summary>Whether this is a check box tree; fixed when the tree is made.</summary>
    [Browsable(false)]
    public bool CheckBoxes { get; init; }

    /// <inheritdoc />
    public override UiComponentKind Kind => CheckBoxes ? UiComponentKind.CheckBoxTree : UiComponentKind.Tree;

    /// <summary>The tree's own nodes, in the order they stand.</summary>
    [Browsable(false)]
    public GraphObjectCollection<UiTreeNodeModel> Nodes { get; }

    /// <summary>MATLAB's <c>SelectedNodes</c>, in the order they were selected.</summary>
    [Browsable(false)]
    public IReadOnlyList<UiTreeNodeModel> Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value ?? [], InvalidationKind.Ui);
    }

    /// <summary>A check box tree's <c>CheckedNodes</c>: every node that is checked, descendants of a checked node included.</summary>
    [Browsable(false)]
    public IReadOnlyList<UiTreeNodeModel> Checked
    {
        get => _checked;
        set => SetProperty(ref _checked, value ?? [], InvalidationKind.Ui);
    }

    /// <summary>MATLAB's <c>Multiselect</c>; a check box tree has none and selects one node at a time.</summary>
    [Browsable(false)]
    public bool Multiselect
    {
        get => _multiselect;
        set => SetProperty(ref _multiselect, value, InvalidationKind.Ui);
    }

    /// <summary>MATLAB's <c>Editable</c>: whether a person may retype a node's text.</summary>
    [Browsable(false)]
    public bool Editable
    {
        get => _editable;
        set => SetProperty(ref _editable, value, InvalidationKind.Ui);
    }

    /// <summary>Every node of the tree, parents before their children, each level in order.</summary>
    public IEnumerable<UiTreeNodeModel> AllNodes()
    {
        var queue = new Queue<UiTreeNodeModel>(Nodes);
        while (queue.Count > 0)
        {
            UiTreeNodeModel next = queue.Dequeue();
            yield return next;
            foreach (UiTreeNodeModel child in next.Nodes)
            {
                queue.Enqueue(child);
            }
        }
    }

    /// <summary>Whether a node stands in this tree.</summary>
    public bool Holds(UiTreeNodeModel node) => ReferenceEquals(node.Tree, this);

    /// <summary>
    /// A node is leaving the tree, or has gone: the selection and the checked list let go of it and
    /// of everything under it.
    /// </summary>
    public void Forget(UiTreeNodeModel node)
    {
        if (_selected.Any(node.Holds))
        {
            Selected = [.. _selected.Where(kept => !node.Holds(kept))];
        }

        if (_checked.Any(node.Holds))
        {
            Checked = [.. _checked.Where(kept => !node.Holds(kept))];
        }
    }

    /// <summary>
    /// R2025b's reading of <c>CheckedNodes</c> after a list is written to it (probe <c>u9_behave</c>):
    /// the nodes given, each once, in order; then the descendants of each, children before
    /// grandchildren; then the parent of each given node whose children are all now in the list.
    /// </summary>
    public static List<UiTreeNodeModel> CheckedFrom(IEnumerable<UiTreeNodeModel> written)
    {
        var result = new List<UiTreeNodeModel>();
        var given = new List<UiTreeNodeModel>();
        foreach (UiTreeNodeModel node in written)
        {
            if (!result.Contains(node))
            {
                result.Add(node);
                given.Add(node);
            }
        }

        foreach (UiTreeNodeModel node in given)
        {
            foreach (UiTreeNodeModel below in node.Descendants())
            {
                if (!result.Contains(below))
                {
                    result.Add(below);
                }
            }
        }

        var implied = new List<UiTreeNodeModel>(result);
        foreach (UiTreeNodeModel node in given)
        {
            if (node.Parent is UiTreeNodeModel parent && !result.Contains(parent) && parent.Nodes.All(implied.Contains))
            {
                result.Add(parent);
            }
        }

        return result;
    }

    /// <inheritdoc />
    public override UiComponentFrame Snapshot() => Common() with
    {
        Multi = _multiselect,
        Editable = _editable,
        Tree = new UiTreeFrame([.. Nodes.Select(static node => node.Snapshot())], _selected, _checked, CheckBoxes),
    };
}

/// <summary>MATLAB's <c>uitree(…, 'checkbox')</c> (<c>matlab.ui.container.CheckBoxTree</c>): a tree whose nodes have check boxes.</summary>
public sealed class UiCheckBoxTreeModel : UiTreeModel
{
    public UiCheckBoxTreeModel()
        : base(checkBoxes: true)
    {
    }
}

/// <summary>One node as a frame holds it (U9).</summary>
public sealed record UiTreeNodeFrame(
    UiTreeNodeModel Source, string Text, UiImage? Icon, bool Expanded, IReadOnlyList<UiTreeNodeFrame> Children, string Tag);

/// <summary>What a tree shows and selects (U9); null on every other kind of component's frame.</summary>
public sealed record UiTreeFrame(
    IReadOnlyList<UiTreeNodeFrame> Nodes,
    IReadOnlyList<UiTreeNodeModel> Selected,
    IReadOnlyList<UiTreeNodeModel> Checked,
    bool CheckBoxes);
