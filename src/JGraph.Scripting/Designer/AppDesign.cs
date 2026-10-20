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
