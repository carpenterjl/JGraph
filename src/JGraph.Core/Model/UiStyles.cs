namespace JGraph.Core.Model;

/// <summary>
/// What a <c>uistyle</c> sets (app-building plan, U9): each part is null, or empty, until the
/// style says otherwise, and a style added to a component changes only the parts it has.
/// </summary>
public sealed record UiStyle
{
    public UiColor? BackgroundColor { get; init; }

    public UiColor? FontColor { get; init; }

    /// <summary><c>bold</c>, <c>normal</c>, or empty for no change.</summary>
    public string FontWeight { get; init; } = string.Empty;

    /// <summary><c>italic</c>, <c>normal</c>, or empty for no change.</summary>
    public string FontAngle { get; init; } = string.Empty;

    public string FontName { get; init; } = string.Empty;

    /// <summary><c>left</c>, <c>center</c>, <c>right</c>, or empty for no change.</summary>
    public string HorizontalAlignment { get; init; } = string.Empty;

    /// <summary>Which end of a cell's text is cut when it does not fit: <c>left</c>, <c>right</c>, or empty.</summary>
    public string HorizontalClipping { get; init; } = string.Empty;

    /// <summary>Where an icon stands by the text: <c>left</c>, <c>center</c>, <c>right</c>, <c>leftmargin</c>, <c>rightmargin</c>, or empty.</summary>
    public string IconAlignment { get; init; } = string.Empty;

    /// <summary><c>none</c>, <c>html</c>, <c>latex</c>, <c>tex</c>, or empty.</summary>
    public string Interpreter { get; init; } = string.Empty;

    public UiImage? Icon { get; init; }

    /// <summary>What <c>Icon</c> was written as when it named a file or a stock word.</summary>
    public string IconSource { get; init; } = string.Empty;

    /// <summary>A style that changes nothing.</summary>
    public static UiStyle Empty { get; } = new();

    /// <summary>This style with another laid over it: the other's parts where it has them, this one's elsewhere.</summary>
    public UiStyle Under(UiStyle over) => new()
    {
        BackgroundColor = over.BackgroundColor ?? BackgroundColor,
        FontColor = over.FontColor ?? FontColor,
        FontWeight = over.FontWeight.Length > 0 ? over.FontWeight : FontWeight,
        FontAngle = over.FontAngle.Length > 0 ? over.FontAngle : FontAngle,
        FontName = over.FontName.Length > 0 ? over.FontName : FontName,
        HorizontalAlignment = over.HorizontalAlignment.Length > 0 ? over.HorizontalAlignment : HorizontalAlignment,
        HorizontalClipping = over.HorizontalClipping.Length > 0 ? over.HorizontalClipping : HorizontalClipping,
        IconAlignment = over.IconAlignment.Length > 0 ? over.IconAlignment : IconAlignment,
        Interpreter = over.Interpreter.Length > 0 ? over.Interpreter : Interpreter,
        Icon = over.Icon ?? Icon,
        IconSource = over.Icon is not null || over.IconSource.Length > 0 ? over.IconSource : IconSource,
    };
}

/// <summary>What part of a component a style was added to (<c>addStyle</c>'s third argument).</summary>
public enum UiStyleTarget
{
    /// <summary>The whole table, tree, list or drop-down.</summary>
    Whole,
    Row,
    Column,
    Cell,
    Node,
    Level,
    Subtree,
    Item,
}

/// <summary>
/// One style added to a component (U9): what it was added to, which parts of that — rows, columns,
/// cells as row-column pairs laid end to end, levels or items, from 1 — or which nodes, and the
/// style itself. Later rules lie over earlier ones where they meet.
/// </summary>
public sealed record UiStyleRule(UiStyleTarget Target, IReadOnlyList<int> Indices, IReadOnlyList<GraphObject> Nodes, UiStyle Style)
{
    /// <summary>Whether this rule reaches a table's cell (both from 1).</summary>
    public bool Covers(int row, int column) => Target switch
    {
        UiStyleTarget.Whole => true,
        UiStyleTarget.Row => Indices.Contains(row),
        UiStyleTarget.Column => Indices.Contains(column),
        UiStyleTarget.Cell => Pairs().Any(pair => pair.Row == row && pair.Column == column),
        _ => false,
    };

    /// <summary>Whether this rule reaches an item of a list or a drop-down (from 1).</summary>
    public bool CoversItem(int item) => Target == UiStyleTarget.Whole || (Target == UiStyleTarget.Item && Indices.Contains(item));

    /// <summary>Whether this rule reaches a node of a tree.</summary>
    public bool CoversNode(UiTreeNodeModel node) => Target switch
    {
        UiStyleTarget.Whole => true,
        UiStyleTarget.Node => Nodes.Contains(node),
        UiStyleTarget.Subtree => Nodes.OfType<UiTreeNodeModel>().Any(root => root.Holds(node)),
        UiStyleTarget.Level => Indices.Contains(node.Level),
        _ => false,
    };

    /// <summary>The cell targets as row-column pairs.</summary>
    public IEnumerable<(int Row, int Column)> Pairs()
    {
        for (int i = 0; i + 1 < Indices.Count; i += 2)
        {
            yield return (Indices[i], Indices[i + 1]);
        }
    }

    /// <summary>The styles of some rules laid one over the other, in order; the empty style for none.</summary>
    public static UiStyle Combine(IEnumerable<UiStyleRule> rules)
    {
        UiStyle result = UiStyle.Empty;
        foreach (UiStyleRule rule in rules)
        {
            result = result.Under(rule.Style);
        }

        return result;
    }
}
