using System.Windows;
using System.Windows.Controls;
using JGraph.Interaction.Editing;

namespace JGraph.Controls.Inspector;

/// <summary>Picks the editor template for a property row from its <see cref="PropertyEditorKind"/>.</summary>
public sealed class EditorTemplateSelector : DataTemplateSelector
{
    public DataTemplate? TextTemplate { get; set; }

    public DataTemplate? NumberTemplate { get; set; }

    public DataTemplate? ToggleTemplate { get; set; }

    public DataTemplate? EnumTemplate { get; set; }

    public DataTemplate? ColorTemplate { get; set; }

    public DataTemplate? OptionalColorTemplate { get; set; }

    public DataTemplate? RangeTemplate { get; set; }

    /// <inheritdoc />
    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        if (item is not PropertyRowViewModel row)
        {
            return base.SelectTemplate(item, container);
        }

        return row.Kind switch
        {
            PropertyEditorKind.Text => TextTemplate,
            PropertyEditorKind.Number => NumberTemplate,
            PropertyEditorKind.Toggle => ToggleTemplate,
            PropertyEditorKind.Enum => EnumTemplate,
            PropertyEditorKind.Color => ColorTemplate,
            PropertyEditorKind.OptionalColor => OptionalColorTemplate,
            PropertyEditorKind.Range => RangeTemplate,
            _ => base.SelectTemplate(item, container),
        };
    }
}
