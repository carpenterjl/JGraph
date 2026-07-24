using System.Reflection;

namespace JGraph.Interaction.Editing;

/// <summary>The editor a property inspector should present for a property.</summary>
public enum PropertyEditorKind
{
    /// <summary>A free-form text box (string).</summary>
    Text,

    /// <summary>A validating numeric text box (double or int).</summary>
    Number,

    /// <summary>A check box (bool).</summary>
    Toggle,

    /// <summary>A combo box over the enum's values.</summary>
    Enum,

    /// <summary>A color editor (hex entry + palette).</summary>
    Color,

    /// <summary>A color editor with an "automatic" state (nullable color).</summary>
    OptionalColor,

    /// <summary>A min/max pair editor (<see cref="Core.Primitives.DataRange"/>).</summary>
    Range,

    /// <summary>An editable combo box over the installed font families (a font name string).</summary>
    FontFamily,

    /// <summary>
    /// Not an editor: a non-editable caption row introducing the child rows of a composite property
    /// (a <c>TextStyle</c>, <c>LineStyle</c>, rectangle…). The inspector renders it as an expander.
    /// </summary>
    Header,
}

/// <summary>
/// A UI-independent description of one editable property of a graph object: the display name and
/// category resolved from <see cref="System.ComponentModel"/> attributes, which editor should present
/// it, and accessors that read and write the value.
/// <para>
/// A descriptor does not have to address a whole property. Composite values (<c>TextStyle</c>,
/// <c>LineStyle</c>, <c>Rect2D</c>…) are expanded into one <see cref="PropertyEditorKind.Header"/>
/// row plus a child row per member; a child's accessors read the root struct, swap one member, and
/// write the whole struct back. <see cref="Property"/> and <see cref="Name"/> therefore always name
/// the <em>root</em> property, so an undo entry recorded against a child restores the entire struct
/// in one step.
/// </para>
/// Produced by <see cref="EditablePropertyFactory"/>; the WPF property inspector builds its rows from
/// these, and being UI-free they are unit-testable.
/// </summary>
public sealed class EditableProperty
{
    private readonly Func<object, object?> _getter;
    private readonly Action<object, object?>? _setter;

    internal EditableProperty(
        PropertyInfo property,
        string displayName,
        string category,
        PropertyEditorKind editor,
        Type valueType,
        string? group,
        Func<object, object?> getter,
        Action<object, object?>? setter)
    {
        Property = property;
        DisplayName = displayName;
        Category = category;
        Editor = editor;
        ValueType = valueType;
        Group = group;
        _getter = getter;
        _setter = setter;
    }

    /// <summary>The reflected root property. For a child row this is the composite-valued property.</summary>
    public PropertyInfo Property { get; }

    /// <summary>The root property's CLR name (used for undo actions).</summary>
    public string Name => Property.Name;

    /// <summary>The human-readable name shown in the inspector.</summary>
    public string DisplayName { get; }

    /// <summary>The group header this property is listed under.</summary>
    public string Category { get; }

    /// <summary>Which editor presents this property.</summary>
    public PropertyEditorKind Editor { get; }

    /// <summary>
    /// The type of the value this descriptor edits — the member type for a child row, the property
    /// type otherwise. Editors must branch on this rather than on <see cref="Property"/>'s type,
    /// which for a child names the composite.
    /// </summary>
    public Type ValueType { get; }

    /// <summary>
    /// Null for a top-level row; the parent's display name for a child of a composite property.
    /// Drives indentation and which header row collapses this one.
    /// </summary>
    public string? Group { get; }

    /// <summary>True for the caption row of a composite property, which has no editor and no setter.</summary>
    public bool IsHeader => Editor == PropertyEditorKind.Header;

    /// <summary>Reads the current value from a target object.</summary>
    public object? GetValue(object target) => _getter(target);

    /// <summary>
    /// Reads the whole root property, which for a child row is the composite that contains the edited
    /// member. Undo entries are recorded against this so one step restores the entire struct.
    /// </summary>
    public object? GetRootValue(object target) => Property.GetValue(target);

    /// <summary>Writes a value to a target object. Does nothing for a header row.</summary>
    public void SetValue(object target, object? value) => _setter?.Invoke(target, value);
}
