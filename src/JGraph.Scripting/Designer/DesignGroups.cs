namespace JGraph.Scripting.Designer;

/// <summary>
/// A component group of App Designer's canvas (open item 150): components selected, moved and
/// deleted as one. A group's members are components of one container, or groups of them; a group
/// of exactly two components, one a label and the other not, is a labelled component (App
/// Designer's <c>LabeledComponent</c>, what the library makes for a labelled entry), which keeps its
/// label first (<c>GroupWidgetData._setLabeledComponentProperty</c>).
/// </summary>
/// <remarks>
/// App Designer's file holds a group as an id and its parent group's id (<c>components.Groups</c>)
/// and each member's group id (<c>DesignTimeProperties.GroupId</c>); nothing marks a labelled
/// component, which the client works out from the members. The design holds the members in order,
/// which the plain-text format writes.
/// </remarks>
public sealed class DesignGroup
{
    private readonly List<DesignGroupMember> _members = [];

    /// <summary>A group with the id <paramref name="id"/>, or a new id (a GUID, as App Designer's peer node ids are).</summary>
    public DesignGroup(string? id = null)
    {
        Id = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString() : id;
    }

    /// <summary>The group's id in the file.</summary>
    public string Id { get; }

    /// <summary>The group this one is a member of; null at the top.</summary>
    public DesignGroup? Parent { get; internal set; }

    /// <summary>The members in order: a labelled component's label first.</summary>
    public IReadOnlyList<DesignGroupMember> Members => _members;

    internal List<DesignGroupMember> MemberList => _members;

    /// <summary>The member components, those directly in the group.</summary>
    public IEnumerable<DesignComponent> Components => _members.Where(static m => m.Component is not null).Select(static m => m.Component!);

    /// <summary>The member groups.</summary>
    public IEnumerable<DesignGroup> Groups => _members.Where(static m => m.Group is not null).Select(static m => m.Group!);

    /// <summary>Every component in the group or in a group inside it (<c>GroupUtil.getComponentsInGroup</c>).</summary>
    public IEnumerable<DesignComponent> AllComponents()
    {
        foreach (DesignGroupMember member in _members)
        {
            if (member.Component is { } component)
            {
                yield return component;
            }
            else if (member.Group is { } group)
            {
                foreach (DesignComponent c in group.AllComponents())
                {
                    yield return c;
                }
            }
        }
    }

    /// <summary>
    /// Whether the group is a labelled component: exactly two members, neither a group, one a
    /// <c>matlab.ui.control.Label</c> and the other not.
    /// </summary>
    public bool IsLabeled =>
        _members.Count == 2 && _members[0].Component is { } a && _members[1].Component is { } b
        && (a.Type == LabelType) != (b.Type == LabelType);

    /// <summary>A labelled component's label; null for any other group.</summary>
    public DesignComponent? Label => IsLabeled ? _members[0].Component : null;

    /// <summary>A labelled component's field (the component the label labels); null for any other group.</summary>
    public DesignComponent? Field => IsLabeled ? _members[1].Component : null;

    /// <summary>The container the members are in (<c>ContentParentId</c>); null for an empty group.</summary>
    public DesignComponent? Container => AllComponents().FirstOrDefault()?.Parent;

    /// <summary>The group at the top of this one's chain: itself when it is a member of none.</summary>
    public DesignGroup Root
    {
        get
        {
            DesignGroup at = this;
            while (at.Parent is { } up)
            {
                at = up;
            }

            return at;
        }
    }

    /// <summary>Whether <paramref name="component"/> is in this group or a group inside it.</summary>
    public bool Contains(DesignComponent component) => AllComponents().Any(c => ReferenceEquals(c, component));

    /// <summary>Whether <paramref name="group"/> is this group or one inside it.</summary>
    public bool Contains(DesignGroup group)
    {
        for (DesignGroup? at = group; at is not null; at = at.Parent)
        {
            if (ReferenceEquals(at, this))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The class of a label, which makes a labelled component.</summary>
    public const string LabelType = "matlab.ui.control.Label";

    /// <summary>A labelled component keeps its label first.</summary>
    internal void Normalize()
    {
        if (IsLabeled && _members[1].Component!.Type == LabelType)
        {
            (_members[0], _members[1]) = (_members[1], _members[0]);
        }
    }

    /// <inheritdoc/>
    public override string ToString() => $"group {Id} ({string.Join(", ", _members.Select(static m => m.ToString()))})";
}

/// <summary>One member of a group: a component, or a group inside it.</summary>
public readonly record struct DesignGroupMember
{
    private DesignGroupMember(DesignComponent? component, DesignGroup? group)
    {
        Component = component;
        Group = group;
    }

    /// <summary>The member component; null for a member group.</summary>
    public DesignComponent? Component { get; }

    /// <summary>The member group; null for a member component.</summary>
    public DesignGroup? Group { get; }

    /// <summary>A component member.</summary>
    public static DesignGroupMember Of(DesignComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        return new(component, null);
    }

    /// <summary>A group member.</summary>
    public static DesignGroupMember Of(DesignGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return new(null, group);
    }

    /// <inheritdoc/>
    public override string ToString() => Component?.CodeName ?? Group?.Id ?? "?";
}

/// <summary>
/// A group as App Designer's file holds it: its id, its parent group's id (empty at the top), and
/// the code names of the components directly in it. Member groups name it as their parent.
/// </summary>
public sealed record DesignGroupRecord(string Id, string ParentId, IReadOnlyList<string> Members)
{
    /// <inheritdoc/>
    public bool Equals(DesignGroupRecord? other) =>
        other is not null && Id == other.Id && ParentId == other.ParentId && Members.SequenceEqual(other.Members, StringComparer.Ordinal);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Id, ParentId, Members.Count);

    /// <summary>Whether <paramref name="a"/> and <paramref name="b"/> hold the same groups, by id, parent and members (the members in any order: a file lists them in its objects' order, the design in its own).</summary>
    public static bool Same(IReadOnlyList<DesignGroupRecord> a, IReadOnlyList<DesignGroupRecord> b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Id != b[i].Id || a[i].ParentId != b[i].ParentId
                || !a[i].Members.Order(StringComparer.Ordinal).SequenceEqual(b[i].Members.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
