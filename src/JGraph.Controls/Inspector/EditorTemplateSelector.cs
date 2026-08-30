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

    public DataTemplate? FontFamilyTemplate { get; set; }

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
            PropertyEditorKind.FontFamily => FontFamilyTemplate,

            // Header rows have no editor: the value column stays empty.
            _ => NoEditor,
        };
    }

    /// <summary>
    /// What "no editor" has to be. Returning null does not leave the cell blank — a
    /// <see cref="ContentPresenter"/> whose selector returns null falls back to its default template,
    /// which renders the bound object's <c>ToString()</c>, so the value column of every composite
    /// caption row read out the view model's full type name.
    /// </summary>
    private static readonly DataTemplate NoEditor = EmptyTemplate();

    private static DataTemplate EmptyTemplate()
    {
        var template = new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(Grid)) };
        template.Seal();
        return template;
    }
}
