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
}

/// <summary>
/// A UI-independent description of one editable property of a graph object: its reflection info, the
/// display name and category resolved from <see cref="System.ComponentModel"/> attributes, and which
/// editor should present it. Produced by <see cref="EditablePropertyFactory"/>; the WPF property
/// inspector builds its rows from these, and being UI-free they are unit-testable.
/// </summary>
public sealed class EditableProperty
{
    internal EditableProperty(PropertyInfo property, string displayName, string category, PropertyEditorKind editor)
    {
        Property = property;
        DisplayName = displayName;
        Category = category;
        Editor = editor;
    }

    /// <summary>The reflected property.</summary>
    public PropertyInfo Property { get; }

    /// <summary>The property's CLR name (used for undo actions).</summary>
    public string Name => Property.Name;

    /// <summary>The human-readable name shown in the inspector.</summary>
    public string DisplayName { get; }

    /// <summary>The group header this property is listed under.</summary>
    public string Category { get; }

    /// <summary>Which editor presents this property.</summary>
    public PropertyEditorKind Editor { get; }

    /// <summary>Reads the current value from a target object.</summary>
    public object? GetValue(object target) => Property.GetValue(target);

    /// <summary>Writes a value to a target object.</summary>
    public void SetValue(object target, object? value) => Property.SetValue(target, value);
}
