using JGraph.Core.Primitives;

namespace JGraph.Scripting.Designer;

/// <content>
/// Component groups on the canvas (open item 150, the designer leftovers plan G1): the selection as
/// App Designer holds it - groups among the picks - and the grouping actions, each one edit.
/// </content>
public sealed partial class DesignDocument
{
    private IReadOnlyList<DesignPick> _picks = [];

    /// <summary>
    /// The selection as App Designer's holds it: each component selected on its own, and each group
    /// selected whole. <see cref="Selection"/> is what they come to, component by component.
    /// </summary>
    public IReadOnlyList<DesignPick> Picks => _picks;

    /// <summary>The groups selected whole.</summary>
    public IEnumerable<DesignGroup> SelectedGroups => _picks.Where(static p => p.Group is not null).Select(static p => p.Group!);

    /// <summary>Selects <paramref name="picks"/> as they are; none selects the figure.</summary>
    public void SelectPicks(IReadOnlyList<DesignPick> picks)
    {
        ArgumentNullException.ThrowIfNull(picks);
        SetPicks(picks.Count == 0 ? [DesignPick.Of(Design.Figure)] : [.. picks]);
    }

    private void SetPicks(IReadOnlyList<DesignPick> picks)
    {
        DesignComponent[] leaves = picks.SelectMany(static p => p.Leaves()).Distinct(ReferenceEqualityComparer.Instance).Cast<DesignComponent>().ToArray();
        bool same = picks.Count == _picks.Count && picks.Zip(_picks).All(static z =>
            (z.First.Component is not null && ReferenceEquals(z.First.Component, z.Second.Component))
            || (z.First.Group is not null && ReferenceEquals(z.First.Group, z.Second.Group)));
        _picks = picks;
        _selection = leaves;
        if (!same)
        {
            OnChanged();
        }
    }

    /// <summary>
    /// The picks <paramref name="components"/> stand for: a member of a group as its group (each
    /// once; the group it is directly in, as a press on it selects), any other as itself.
    /// </summary>
    private IReadOnlyList<DesignPick> Promoted(IEnumerable<DesignComponent> components)
    {
        var picks = new List<DesignPick>();
        foreach (DesignComponent c in components)
        {
            DesignPick pick = Design.GroupOf(c) is { } group ? DesignPick.Of(group) : DesignPick.Of(c);
            if (!DesignGroupRules.IsSelected(picks, pick))
            {
                picks.Add(pick);
            }
        }

        return picks;
    }

    /// <summary>
    /// The group a point on the surface is over and no component is: the innermost group whose box
    /// (<see cref="BoxOfGroup"/>) holds <paramref name="point"/> among those in
    /// <paramref name="container"/> (a group's own area is drawn over its container, gaps included).
    /// </summary>
    public DesignGroup? GroupAt(Point2D point, DesignComponent container)
    {
        ArgumentNullException.ThrowIfNull(container);
        DesignGroup? found = null;
        int depth = -1;
        double area = double.MaxValue;
        foreach (DesignGroup group in Design.Groups)
        {
            if (!ReferenceEquals(group.Container, container) || BoxOfGroup(group) is not { } box || !box.Contains(point))
            {
                continue;
            }

            int d = 0;
            for (DesignGroup? up = group.Parent; up is not null; up = up.Parent)
            {
                d++;
            }

            double a = box.Width * box.Height;
            if (d > depth || (d == depth && a < area))
            {
                found = group;
                depth = d;
                area = a;
            }
        }

        return found;
    }

    /// <summary>What a press at <paramref name="point"/> is on: the component there, or the group whose area it is in, else the figure.</summary>
    public DesignPick PickAt(Point2D point)
    {
        DesignComponent hit = ComponentAt(point);
        return hit.Parent is not null && Design.GroupOf(hit) is null && GroupAt(point, hit) is { } over
            ? DesignPick.Of(over)
            : Design.GroupOf(hit) is null && GroupAt(point, hit) is { } under
                ? DesignPick.Of(under)
                : DesignPick.Of(hit);
    }

    /// <summary>
    /// A group's box on the surface (<c>GroupUtil.calculateGroupSizeProperties</c>): the least box
    /// round its components' boxes, each edge rounded - a grid child's its cells'; null when none
    /// of them shows.
    /// </summary>
    public Rect2D? BoxOfGroup(DesignGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        double left = double.PositiveInfinity, top = double.PositiveInfinity, right = double.NegativeInfinity, bottom = double.NegativeInfinity;
        foreach (DesignComponent c in group.AllComponents())
        {
            if (BoxOf(c) is not { Shown: true } box)
            {
                continue;
            }

            Rect2D b = IsGridChild(c) ? CellBoxOf(c) ?? box.Box : box.Box;
            double x = System.Math.Round(b.X, MidpointRounding.AwayFromZero);
            double y = System.Math.Round(b.Y, MidpointRounding.AwayFromZero);
            left = System.Math.Min(left, x);
            top = System.Math.Min(top, y);
            right = System.Math.Max(right, x + System.Math.Round(b.Width, MidpointRounding.AwayFromZero));
            bottom = System.Math.Max(bottom, y + System.Math.Round(b.Height, MidpointRounding.AwayFromZero));
        }

        return double.IsInfinity(left) ? null : new Rect2D(left, top, right - left, bottom - top);
    }

    /// <summary>The box of <paramref name="pick"/> on the surface: the component's (a grid child's cells'), or the group's; null when it does not show.</summary>
    public Rect2D? BoxOfPick(DesignPick pick) =>
        pick.Group is { } group ? BoxOfGroup(group)
        : pick.Component is { } c && BoxOf(c) is { Shown: true } box ? (IsGridChild(c) ? CellBoxOf(c) ?? box.Box : box.Box)
        : null;

    /// <summary>The labelled component selected whole, when that is the whole selection; null otherwise.</summary>
    public DesignGroup? SelectedLabeled => _picks is [{ Group: { IsLabeled: true } group }] ? group : null;

    // --- the grouping actions (GroupingAction) ----------------------------------------------------

    /// <summary>Whether Group applies to the selection.</summary>
    public bool CanGroup => DesignGroupRules.CanGroup(Design, _picks);

    /// <summary>Whether Add to Group applies to the selection.</summary>
    public bool CanAddToGroup => DesignGroupRules.CanAddToGroup(Design, _picks);

    /// <summary>Whether Remove from Group applies to the selection.</summary>
    public bool CanRemoveFromGroup => DesignGroupRules.CanRemoveFromGroup(Design, _picks);

    /// <summary>Whether Ungroup applies to the selection.</summary>
    public bool CanUngroup => DesignGroupRules.CanUngroup(Design, _picks);

    /// <summary>Groups the selection as one edit (<c>GroupCommand</c>); the group is selected. Null when Group does not apply.</summary>
    public DesignGroup? Group()
    {
        if (!CanGroup)
        {
            return null;
        }

        EndGesture(commit: false);
        var group = new DesignGroup();
        DesignGroupMember[] members = _picks.Select(static p => p.Component is { } c ? DesignGroupMember.Of(c) : DesignGroupMember.Of(p.Group!)).ToArray();
        Do([new GroupEdit(members[0].Component ?? members[0].Group!.AllComponents().First(), group, members, null, null)]);
        return group;
    }

    /// <summary>Takes the selected groups apart as one edit (<c>UngroupCommand</c>); their members are selected. False when Ungroup does not apply.</summary>
    public bool Ungroup()
    {
        if (!CanUngroup)
        {
            return false;
        }

        EndGesture(commit: false);
        Do(SelectedGroups.Select(static g => (Edit)new UngroupEdit(g.AllComponents().First(), g, g.Members.ToArray(), null, null)).ToArray());
        return true;
    }

    /// <summary>Puts the other selected picks into the selected group as one edit (<c>AddToGroupCommand</c>); the group is selected.</summary>
    public bool AddToGroup()
    {
        if (!CanAddToGroup)
        {
            return false;
        }

        EndGesture(commit: false);
        DesignGroup group = SelectedGroups.First();
        DesignGroupMember[] members = _picks.Where(p => !ReferenceEquals(p.Group, group))
            .Select(static p => p.Component is { } c ? DesignGroupMember.Of(c) : DesignGroupMember.Of(p.Group!)).ToArray();
        Do([new MembersEdit(members[0].Component ?? members[0].Group!.AllComponents().First(), group, members, Add: true)]);
        return true;
    }

    /// <summary>Takes the selected members out of their group as one edit (<c>RemoveFromGroupCommand</c>); they are selected.</summary>
    public bool RemoveFromGroup()
    {
        if (!CanRemoveFromGroup)
        {
            return false;
        }

        EndGesture(commit: false);
        DesignGroup group = DesignGroupRules.GroupOfPick(Design, _picks[0])!;
        DesignGroupMember[] members = _picks.Select(static p => p.Component is { } c ? DesignGroupMember.Of(c) : DesignGroupMember.Of(p.Group!)).ToArray();
        Do([new MembersEdit(members[0].Component ?? members[0].Group!.AllComponents().First(), group, members, Add: false)]);
        return true;
    }

    /// <summary>The edit that groups a new labelled component's label and field, which the library makes together (<c>AddComponentCommand._addLabeledComponent</c>).</summary>
    private static GroupEdit LabeledGroupEdit(DesignComponent label, DesignComponent field) =>
        new(field, new DesignGroup(), [DesignGroupMember.Of(label), DesignGroupMember.Of(field)], null, null) { Selects = false };

    /// <summary>
    /// The components a delete of <paramref name="components"/> takes (<c>DeleteCommand._filterAllAreComponentMembers</c>):
    /// when every one is the field of a labelled component, the labelled components whole, labels included.
    /// </summary>
    private DesignComponent[] WithLabels(IReadOnlyList<DesignComponent> components)
    {
        if (components.Count == 0 || !components.All(c => Design.LabeledGroupOf(c) is { Field: { } field } && ReferenceEquals(field, c)))
        {
            return [.. components];
        }

        return components.SelectMany(c => Design.LabeledGroupOf(c)!.AllComponents()).ToArray();
    }

    /// <summary>
    /// The edits that take deleted components out of their groups (<c>DeleteCommand</c>,
    /// <c>DeleteComponentInGroupCommand</c>): a group selected whole goes with every group inside it;
    /// each other deleted member leaves its own group, and that group goes when it is left with one
    /// member or none, its last member joining the group it was in - the group around is not looked
    /// at in turn. Put before the deletes, inner groups before outer and members from the last, so
    /// that an undo (the step backwards) puts each group back before what goes into it.
    /// </summary>
    private List<Edit> GroupEditsForDelete(IReadOnlyList<DesignComponent> gone)
    {
        var edits = new List<Edit>();
        var goneSet = new HashSet<DesignComponent>(gone.SelectMany(static g => Walk(g)), ReferenceEqualityComparer.Instance);

        // The groups selected whole, and every group inside them (findGroupsInSelection).
        var whole = new List<DesignGroup>();
        foreach (DesignGroup group in SelectedGroups)
        {
            if (group.AllComponents().All(goneSet.Contains))
            {
                foreach (DesignGroup g in Inside(group))
                {
                    if (!whole.Contains(g))
                    {
                        whole.Add(g);
                    }
                }
            }
        }

        // Each deleted member out of its group, from the last member down, so that an undo puts them back first to last.
        var leaving = new List<(DesignComponent Component, DesignGroup Group, int Index)>();
        foreach (DesignComponent component in goneSet)
        {
            if (Design.GroupOf(component) is { } group)
            {
                leaving.Add((component, group, group.Members.ToList().FindIndex(m => ReferenceEquals(m.Component, component))));
            }
        }

        foreach ((DesignComponent component, DesignGroup group, int index) in leaving.OrderByDescending(static l => l.Index))
        {
            edits.Add(new LeaveGroupEdit(component, group, index) { Selects = false });
        }

        // The groups that go: those selected whole, deepest first; and each other member's own group
        // when it is left with one member or none, that member joining the group around.
        var dissolving = new List<(DesignGroup Group, DesignGroupMember? Last)>();
        foreach (DesignGroup group in whole.OrderByDescending(Depth))
        {
            dissolving.Add((group, null));
        }

        foreach (DesignGroup group in leaving.Select(static l => l.Group).Distinct(ReferenceEqualityComparer.Instance).Cast<DesignGroup>())
        {
            if (whole.Contains(group))
            {
                continue;
            }

            List<DesignGroupMember> left = group.Members.Where(m => m.Component is null || !goneSet.Contains(m.Component)).ToList();
            if (left.Count <= 1)
            {
                dissolving.Add((group, left.Count == 1 ? left[0] : null));
            }
        }

        foreach ((DesignGroup group, DesignGroupMember? last) in dissolving)
        {
            DesignGroup? parent = group.Parent;
            int parentIndex = parent is null ? -1 : parent.Members.ToList().FindIndex(m => ReferenceEquals(m.Group, group));
            bool join = last is not null && parent is not null && !whole.Contains(parent);
            edits.Add(new DissolveEdit(group.AllComponents().FirstOrDefault() ?? gone[0], group, join ? last : null, parent, parentIndex) { Selects = false });
        }

        return edits;

        static IEnumerable<DesignGroup> Inside(DesignGroup group)
        {
            foreach (DesignGroup inner in group.Groups)
            {
                foreach (DesignGroup g in Inside(inner))
                {
                    yield return g;
                }
            }

            yield return group;
        }

        static int Depth(DesignGroup group)
        {
            int d = 0;
            for (DesignGroup? up = group.Parent; up is not null; up = up.Parent)
            {
                d++;
            }

            return d;
        }
    }

    // --- the edits -------------------------------------------------------------------------------

    /// <summary>A group made of <paramref name="Members"/>, inside <paramref name="Parent"/> at <paramref name="ParentIndex"/> when given; undone, taken apart again.</summary>
    private sealed record GroupEdit(DesignComponent Component, DesignGroup Group, IReadOnlyList<DesignGroupMember> Members, DesignGroup? Parent, int? ParentIndex) : Edit(Component)
    {
        public override void Do(DesignDocument document) => document.Design.AddExistingGroup(Group, Members, Parent, ParentIndex);

        public override void Undo(DesignDocument document) => document.Design.RemoveGroup(Group);

        public override IReadOnlyList<DesignPick>? PicksAfter(bool done) =>
            done ? [DesignPick.Of(Group)] : Members.Select(static m => m.Component is { } c ? DesignPick.Of(c) : DesignPick.Of(m.Group!)).ToArray();
    }

    /// <summary>A group taken apart; undone, made again of the same members in the same place.</summary>
    private sealed record UngroupEdit(DesignComponent Component, DesignGroup Group, IReadOnlyList<DesignGroupMember> Members, DesignGroup? Parent, int? ParentIndex) : Edit(Component)
    {
        public override void Do(DesignDocument document) => document.Design.RemoveGroup(Group);

        public override void Undo(DesignDocument document) => document.Design.AddExistingGroup(Group, Members, Parent, ParentIndex);

        public override IReadOnlyList<DesignPick>? PicksAfter(bool done) =>
            done ? Members.Select(static m => m.Component is { } c ? DesignPick.Of(c) : DesignPick.Of(m.Group!)).ToArray() : [DesignPick.Of(Group)];
    }

    /// <summary>Members put into a group (<paramref name="Add"/>) or taken out of it; undone the other way, each back in its place.</summary>
    private sealed record MembersEdit(DesignComponent Component, DesignGroup Group, IReadOnlyList<DesignGroupMember> Members, bool Add) : Edit(Component)
    {
        private int[] _indexes = [];

        public override void Do(DesignDocument document) => Apply(document, Add);

        public override void Undo(DesignDocument document) => Apply(document, !Add);

        private void Apply(DesignDocument document, bool add)
        {
            if (add)
            {
                if (_indexes.Length == Members.Count)
                {
                    // Back in their places, the lowest first, so that each index is right when it is used.
                    foreach ((int index, DesignGroupMember member) in _indexes.Zip(Members).OrderBy(static p => p.First))
                    {
                        document.Design.InsertIntoGroup(Group, index, member);
                    }
                }
                else
                {
                    document.Design.AddToGroup(Group, Members);
                }
            }
            else
            {
                _indexes = Members.Select(m => Group.Members.ToList().FindIndex(x => x == m)).ToArray();
                document.Design.RemoveFromGroup(Group, Members);
            }
        }

        public override IReadOnlyList<DesignPick>? PicksAfter(bool done) =>
            done == Add ? [DesignPick.Of(Group)] : Members.Select(static m => m.Component is { } c ? DesignPick.Of(c) : DesignPick.Of(m.Group!)).ToArray();
    }

    /// <summary>A deleted component out of its group; undone, back at its place in it.</summary>
    private sealed record LeaveGroupEdit(DesignComponent Component, DesignGroup Group, int Index) : Edit(Component)
    {
        public override void Do(DesignDocument document) => document.Design.RemoveFromGroup(Group, [DesignGroupMember.Of(Component)]);

        public override void Undo(DesignDocument document) => document.Design.InsertIntoGroup(Group, Index, DesignGroupMember.Of(Component));
    }

    /// <summary>
    /// A group gone: one selected whole, or one left with one member or none, that member
    /// (<paramref name="Last"/>) joining the group it was in. Undone, back with the members it had
    /// when it went, in its place.
    /// </summary>
    private sealed record DissolveEdit(DesignComponent Component, DesignGroup Group, DesignGroupMember? Last, DesignGroup? Parent, int ParentIndex) : Edit(Component)
    {
        private DesignGroupMember[] _had = [];

        public override void Do(DesignDocument document)
        {
            _had = Group.Members.ToArray();
            document.Design.RemoveGroup(Group);
            if (Parent is not null && Last is { } last)
            {
                document.Design.AddToGroup(Parent, [last]);
            }
        }

        public override void Undo(DesignDocument document)
        {
            if (Parent is not null && Last is { } last)
            {
                document.Design.RemoveFromGroup(Parent, [last]);
            }

            document.Design.AddExistingGroup(Group, _had, Parent, ParentIndex < 0 ? null : ParentIndex);
        }
    }
}
