namespace JGraph.Scripting.Designer;

/// <summary>
/// The visual designer's model of an app's components (designer plan DS1): the tree
/// <c>createComponents</c> builds, each component with the properties set on it. It is what the
/// canvas shows and the inspector edits, and what the designer writes the generated code from.
/// </summary>
/// <remarks>
/// <para>
/// A property is held under the name App Designer's generator knows it by: the property itself, or
/// the generator's alias for what it writes another way - <c>TitleString</c> for
/// <c>title(app.UIAxes, ...)</c>, <c>ToolbarVisible</c> for <c>app.UIAxes.Toolbar.Visible</c>,
/// <c>AD_ColormapString</c> for <c>colormap(...)</c>, <c>AD_AliasedThemeChangedFcn</c> for the
/// figure's <c>ThemeChangedFcn</c>. A grid child's place is held as <c>Layout.Row</c> and
/// <c>Layout.Column</c>, and a context menu assigned to a component as its <c>ContextMenu</c>.
/// </para>
/// <para>
/// Only what is set is held. Whether a set property is written is the writer's to decide, as App
/// Designer's generator decides it: not when it holds its default.
/// </para>
/// </remarks>
public sealed class AppDesign
{
    /// <summary>Starts a design whose figure is <paramref name="figure"/>.</summary>
    public AppDesign(DesignComponent figure)
    {
        ArgumentNullException.ThrowIfNull(figure);
        if (figure.Parent is not null)
        {
            throw new ArgumentException("The figure is the root of the design and has no parent.", nameof(figure));
        }

        Figure = figure;
    }

    /// <summary>The app's figure, the root of the tree.</summary>
    public DesignComponent Figure { get; }

    /// <summary>
    /// Whether the app is one of App Designer's responsive (auto-reflow) apps, whose figure's grid
    /// and panels App Designer lays out itself.
    /// </summary>
    public bool IsResponsive { get; set; }

    /// <summary>Every component in the order <c>createComponents</c> makes them: each before its children.</summary>
    public IEnumerable<DesignComponent> Components => Walk(Figure);

    /// <summary>The component named <paramref name="codeName"/>, or null.</summary>
    public DesignComponent? Find(string codeName) =>
        Components.FirstOrDefault(c => string.Equals(c.CodeName, codeName, StringComparison.Ordinal));

    /// <summary>Whether any component shows an image kept beside the app, which needs the app's folder.</summary>
    public bool UsesImageFiles => Components.Any(static c => c.Properties.Any(static p => p.Value is ImageFileValue));

    // --- component groups (open item 150) ---------------------------------------------------------

    private readonly List<DesignGroup> _groups = [];
    private readonly Dictionary<DesignComponent, DesignGroup> _groupOf = new(ReferenceEqualityComparer.Instance);

    /// <summary>Every group, in the order they were made (App Designer's groups manager order): a group inside another is its own entry too.</summary>
    public IReadOnlyList<DesignGroup> Groups => _groups;

    /// <summary>The group <paramref name="component"/> is directly in; null when it is in none.</summary>
    public DesignGroup? GroupOf(DesignComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        return _groupOf.GetValueOrDefault(component);
    }

    /// <summary>The labelled component <paramref name="component"/> is the label or the field of; null when it is in none.</summary>
    public DesignGroup? LabeledGroupOf(DesignComponent component) => GroupOf(component) is { IsLabeled: true } group ? group : null;

    /// <summary>The top group of the chain <paramref name="component"/> is in; null when it is in none.</summary>
    public DesignGroup? RootGroupOf(DesignComponent component) => GroupOf(component)?.Root;

    /// <summary>
    /// Makes a group of <paramref name="members"/> (components in none, or groups at the top), with
    /// the id <paramref name="id"/> or a new one, inside <paramref name="parent"/> when given.
    /// </summary>
    /// <exception cref="ArgumentException">A member is in a group already, or a member group is this group.</exception>
    public DesignGroup AddGroup(IEnumerable<DesignGroupMember> members, string? id = null, DesignGroup? parent = null)
    {
        ArgumentNullException.ThrowIfNull(members);
        var group = new DesignGroup(id);
        if (_groups.Any(g => g.Id == group.Id))
        {
            throw new ArgumentException($"A group with the id '{group.Id}' is in the design already.", nameof(id));
        }

        DesignGroupMember[] list = [.. members];
        Check(group, list);
        if (parent is not null)
        {
            Check(parent, [DesignGroupMember.Of(group)]);
        }

        _groups.Add(group);
        AddToGroup(group, list);
        if (parent is not null)
        {
            AddToGroup(parent, [DesignGroupMember.Of(group)]);
        }

        return group;
    }

    /// <summary>
    /// Puts a group taken out of the design back (an undo): <paramref name="group"/>, empty and in
    /// no group, with <paramref name="members"/>, inside <paramref name="parent"/> at
    /// <paramref name="parentIndex"/> among its members (last when null).
    /// </summary>
    /// <exception cref="ArgumentException">The group is in the design, or a member cannot go in.</exception>
    public void AddExistingGroup(DesignGroup group, IReadOnlyList<DesignGroupMember> members, DesignGroup? parent, int? parentIndex)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(members);
        if (_groups.Contains(group) || group.MemberList.Count > 0 || group.Parent is not null)
        {
            throw new ArgumentException("The group is in the design already.", nameof(group));
        }

        Check(group, members);
        if (parent is not null)
        {
            Check(parent, [DesignGroupMember.Of(group)]);
        }

        _groups.Add(group);
        AddToGroup(group, members);
        if (parent is not null)
        {
            InsertIntoGroup(parent, parentIndex ?? parent.MemberList.Count, DesignGroupMember.Of(group));
        }
    }

    /// <summary>Puts <paramref name="member"/> into <paramref name="group"/> at <paramref name="index"/> among its members (an undo puts a member back where it was).</summary>
    /// <exception cref="ArgumentException">The member cannot go in.</exception>
    public void InsertIntoGroup(DesignGroup group, int index, DesignGroupMember member)
    {
        ArgumentNullException.ThrowIfNull(group);
        Check(group, [member]);
        if (member.Component is { } component)
        {
            _groupOf[component] = group;
        }
        else if (member.Group is { } inner)
        {
            inner.Parent = group;
        }
        else
        {
            return;
        }

        group.MemberList.Insert(System.Math.Clamp(index, 0, group.MemberList.Count), member);
        group.Normalize();
    }

    /// <summary>Puts <paramref name="members"/> into <paramref name="group"/>, last (<c>addGroupMembers</c>); nothing changes when one cannot go in.</summary>
    /// <exception cref="ArgumentException">A member is in a group already, or a member group is the group itself or one around it.</exception>
    public void AddToGroup(DesignGroup group, IEnumerable<DesignGroupMember> members)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(members);
        DesignGroupMember[] list = [.. members];
        Check(group, list);
        foreach (DesignGroupMember member in list)
        {
            if (member.Component is { } component)
            {
                _groupOf[component] = group;
            }
            else if (member.Group is { } inner)
            {
                inner.Parent = group;
            }
            else
            {
                continue;
            }

            group.MemberList.Add(member);
        }

        group.Normalize();
    }

    /// <summary>Why <paramref name="members"/> cannot all go into <paramref name="group"/>, thrown.</summary>
    private void Check(DesignGroup group, IReadOnlyList<DesignGroupMember> members)
    {
        foreach (DesignGroupMember member in members)
        {
            if (member.Component is { } component && _groupOf.ContainsKey(component))
            {
                throw new ArgumentException($"'{component.CodeName}' is in a group already.", nameof(members));
            }

            if (member.Group is { } inner)
            {
                if (inner.Parent is not null)
                {
                    throw new ArgumentException($"The group {inner.Id} is in a group already.", nameof(members));
                }

                if (inner.Contains(group))
                {
                    throw new ArgumentException("A group cannot be put inside itself.", nameof(members));
                }
            }
        }
    }

    /// <summary>Takes <paramref name="members"/> out of <paramref name="group"/>; they are in no group then (<c>removeGroupMembers</c>).</summary>
    public void RemoveFromGroup(DesignGroup group, IEnumerable<DesignGroupMember> members)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(members);
        foreach (DesignGroupMember member in members)
        {
            int at = group.MemberList.FindIndex(m => m == member);
            if (at < 0)
            {
                continue;
            }

            group.MemberList.RemoveAt(at);
            if (member.Component is { } component)
            {
                _groupOf.Remove(component);
            }
            else if (member.Group is { } inner)
            {
                inner.Parent = null;
            }
        }

        group.Normalize();
    }

    /// <summary>
    /// Takes <paramref name="group"/> out of the design (<c>DesignerWidgetDataManager._removeGroup</c>):
    /// out of the group it was in, its members in no group then - a member group at the top.
    /// </summary>
    public bool RemoveGroup(DesignGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (!_groups.Remove(group))
        {
            return false;
        }

        if (group.Parent is { } parent)
        {
            RemoveFromGroup(parent, [DesignGroupMember.Of(group)]);
        }

        RemoveFromGroup(group, group.Members.ToArray());
        return true;
    }

    /// <summary>
    /// Takes <paramref name="component"/> out of its group when it leaves the design
    /// (<c>_handleGroupMemberDeletedEvent</c>). The group is left with what it has.
    /// </summary>
    public void LeaveGroup(DesignComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (GroupOf(component) is { } group)
        {
            RemoveFromGroup(group, [DesignGroupMember.Of(component)]);
        }
    }

    /// <summary>The groups as the file holds them (<see cref="DesignGroupRecord"/>), in <see cref="Groups"/> order.</summary>
    public IReadOnlyList<DesignGroupRecord> GroupRecords() =>
        _groups.Select(g => new DesignGroupRecord(g.Id, g.Parent?.Id ?? string.Empty, g.Components.Select(static c => c.CodeName).ToArray())).ToArray();

    /// <summary>
    /// Makes the groups a file holds (<paramref name="records"/>) over the components, in place of
    /// any the design has: each group's members in the order the components come in the design,
    /// a member group where its first component is met (<c>GroupUtil.buildGroupComponentHierarchyData</c>,
    /// <c>DesignerWidgetDataManager._createGroups</c>); a labelled component's label first. A record
    /// naming no component the design has, or a group whose parent is not among the records, is
    /// made a top group, as is a group whose parents loop; a name the design does not have is left
    /// out; a group left with fewer than two members is left out (<c>_createGroups</c>).
    /// </summary>
    public void AttachGroups(IReadOnlyList<DesignGroupRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        foreach (DesignGroup old in _groups.ToArray())
        {
            RemoveGroup(old);
        }

        var byId = new Dictionary<string, DesignGroup>(StringComparer.Ordinal);
        var recordOf = new Dictionary<string, DesignGroupRecord>(StringComparer.Ordinal);
        foreach (DesignGroupRecord record in records)
        {
            if (record.Id.Length == 0 || recordOf.ContainsKey(record.Id))
            {
                continue;
            }

            recordOf[record.Id] = record;
            byId[record.Id] = new DesignGroup(record.Id);
        }

        // The group of each component, by name.
        var groupOfName = new Dictionary<string, DesignGroup>(StringComparer.Ordinal);
        foreach (DesignGroupRecord record in records)
        {
            foreach (string name in record.Members)
            {
                if (byId.TryGetValue(record.Id, out DesignGroup? g))
                {
                    groupOfName.TryAdd(name, g);
                }
            }
        }

        // Members in the design's order, a member group where its first component is met.
        var placed = new HashSet<DesignGroup>(ReferenceEqualityComparer.Instance);
        foreach (DesignComponent component in Components)
        {
            if (!groupOfName.TryGetValue(component.CodeName, out DesignGroup? group))
            {
                continue;
            }

            group.MemberList.Add(DesignGroupMember.Of(component));
            _groupOf[component] = group;
            for (DesignGroup at = group; placed.Add(at);)
            {
                string parentId = recordOf[at.Id].ParentId;
                if (parentId.Length == 0 || !byId.TryGetValue(parentId, out DesignGroup? parent) || ReferenceEquals(parent, at) || at.Contains(parent))
                {
                    break; // no parent, one the file lacks, or a loop of parents: a top group
                }

                parent.MemberList.Add(DesignGroupMember.Of(at));
                at.Parent = parent;
                at = parent;
            }
        }

        foreach (DesignGroupRecord record in records)
        {
            if (byId.TryGetValue(record.Id, out DesignGroup? group) && group.MemberList.Count > 0 && !_groups.Contains(group))
            {
                group.Normalize();
                _groups.Add(group);
            }
        }

        // A group of fewer than two members is not made (_createGroups: M.length > 1), nor one
        // whose members are all such groups; a member group of one that goes stands at the top.
        bool pruned;
        do
        {
            pruned = false;
            foreach (DesignGroup group in _groups.ToArray())
            {
                if (group.Members.Count < 2 || !group.AllComponents().Any())
                {
                    RemoveGroup(group);
                    pruned = true;
                }
            }
        }
        while (pruned);
    }

    private static IEnumerable<DesignComponent> Walk(DesignComponent component)
    {
        yield return component;
        foreach (DesignComponent child in component.Children)
        {
            foreach (DesignComponent c in Walk(child))
            {
                yield return c;
            }
        }
    }
}

/// <summary>One component of a design.</summary>
public sealed class DesignComponent
{
    private readonly List<DesignComponent> _children = [];
    private readonly List<KeyValuePair<string, DesignValue>> _properties = [];

    /// <summary>
    /// A component of the catalog's class <paramref name="componentClass"/>, named
    /// <paramref name="codeName"/>.
    /// </summary>
    public DesignComponent(string codeName, DesignComponentClass componentClass)
        : this(codeName, componentClass.Type, componentClass.Function, componentClass.Style is null ? string.Empty : $"'{componentClass.Style}'")
    {
        Class = componentClass;
    }

    /// <summary>
    /// A component of a class the catalog does not know, made by <paramref name="function"/> with
    /// <paramref name="extraArguments"/> after the parent: kept as written, its properties in the
    /// order they were read.
    /// </summary>
    public DesignComponent(string codeName, string type, string function, string extraArguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(codeName);
        ArgumentException.ThrowIfNullOrEmpty(type);
        ArgumentException.ThrowIfNullOrEmpty(function);
        ArgumentNullException.ThrowIfNull(extraArguments);
        CodeName = codeName;
        Type = type;
        Function = function;
        ExtraArguments = extraArguments;
    }

    /// <summary>The name the app's code knows the component by (<c>app.CodeName</c>).</summary>
    public string CodeName { get; set; }

    /// <summary>The component's class, as <c>matlab.ui.control.Button</c>.</summary>
    public string Type { get; }

    /// <summary>
    /// What App Designer's generator knows of the class; null for a class it does not know. A
    /// custom component read as a call of its class is given its class once the runtime has
    /// described it (designer plan DS7b).
    /// </summary>
    public DesignComponentClass? Class { get; internal set; }

    /// <summary>The function that makes the component.</summary>
    public string Function { get; }

    /// <summary>What the creation call passes after the parent, as written (<c>'push'</c>); empty when nothing.</summary>
    public string ExtraArguments { get; }

    /// <summary>The container the component is in; null for the figure.</summary>
    public DesignComponent? Parent { get; private set; }

    /// <summary>The components inside this one, in the order they are made.</summary>
    public IReadOnlyList<DesignComponent> Children => _children;

    /// <summary>The properties set on the component, in the order they were set.</summary>
    public IReadOnlyList<KeyValuePair<string, DesignValue>> Properties => _properties;

    /// <summary>Whether the component is laid out by a grid, so that its place is a row and a column rather than a position.</summary>
    public bool IsInGrid => Parent?.Type == "matlab.ui.container.GridLayout";

    /// <summary>Adds <paramref name="child"/> as the last component inside this one.</summary>
    public void Add(DesignComponent child) => Insert(_children.Count, child);

    /// <summary>Puts <paramref name="child"/> inside this one at <paramref name="index"/>.</summary>
    public void Insert(int index, DesignComponent child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child.Parent is not null)
        {
            throw new InvalidOperationException($"'{child.CodeName}' is already inside '{child.Parent.CodeName}'.");
        }

        for (DesignComponent? at = this; at is not null; at = at.Parent)
        {
            if (ReferenceEquals(at, child))
            {
                throw new InvalidOperationException($"'{child.CodeName}' cannot be put inside itself.");
            }
        }

        _children.Insert(index, child);
        child.Parent = this;
    }

    /// <summary>Takes <paramref name="child"/> out of this component.</summary>
    public bool Remove(DesignComponent child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (!_children.Remove(child))
        {
            return false;
        }

        child.Parent = null;
        return true;
    }

    /// <summary>The value set for <paramref name="name"/>, or null when it is not set.</summary>
    public DesignValue? Get(string name)
    {
        int i = IndexOf(name);
        return i < 0 ? null : _properties[i].Value;
    }

    /// <summary>Sets <paramref name="name"/> to <paramref name="value"/>, in its place when it was already set.</summary>
    public void Set(string name, DesignValue value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);
        int i = IndexOf(name);
        if (i < 0)
        {
            _properties.Add(new(name, value));
        }
        else
        {
            _properties[i] = new(name, value);
        }
    }

    /// <summary>Unsets <paramref name="name"/>, which returns it to its default.</summary>
    public bool Unset(string name)
    {
        int i = IndexOf(name);
        if (i < 0)
        {
            return false;
        }

        _properties.RemoveAt(i);
        return true;
    }

    private int IndexOf(string name) => _properties.FindIndex(p => string.Equals(p.Key, name, StringComparison.Ordinal));

    /// <inheritdoc/>
    public override string ToString() => $"{CodeName} ({Type})";
}
