using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using JGraph.Core.Model;
using JGraph.Serialization.Dto;

namespace JGraph.Serialization.Mapping;

/// <summary>
/// The <c>uifigure</c> components and <c>uigridlayout</c> in the document form (app-building plan,
/// U5). There are sixteen kinds with some eighty properties between them, so they are not spelled
/// out one by one: a component is written as its kind and a bag of what it holds, name by name, and
/// read back the same way. A name this build does not know, or a value that does not read, is left
/// at the component's default — the rule every other part of a document follows.
/// </summary>
internal static partial class UiComponentMapper
{
    private const string ComponentPrefix = "ui:";
    private const string GridKind = "uigridlayout";

    private static readonly JsonSerializerOptions BagOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>What the common part of a component already carries, and what is not the document's to keep.</summary>
    private static readonly HashSet<string> NotInBag = new(StringComparer.Ordinal)
    {
        nameof(UiComponentModel.UserWriteSeq),
    };

    private static UiComponentDto? ComponentToDto(UiObject component)
    {
        switch (component)
        {
            case UiComponentModel leaf:
                return new UiComponentDto { Kind = ComponentPrefix + leaf.Kind, Properties = Bag(leaf) };

            case UiGridLayoutModel grid:
            {
                var dto = new UiComponentDto { Kind = GridKind, Properties = Bag(grid) };
                foreach (UiObject child in grid.Components)
                {
                    if (ToDto(child) is { } childDto)
                    {
                        dto.Children.Add(childDto);
                    }
                }

                return dto;
            }

            default:
                return null;
        }
    }

    private static UiObject? ComponentToModel(UiComponentDto dto)
    {
        if (dto.Kind == GridKind)
        {
            var grid = new UiGridLayoutModel();
            Fill(grid, dto.Properties);
            foreach (UiComponentDto childDto in dto.Children)
            {
                if (ToModel(childDto) is { } child)
                {
                    grid.Components.Add(child);
                }
            }

            return grid;
        }

        if (!dto.Kind.StartsWith(ComponentPrefix, StringComparison.Ordinal)
            || !Enum.TryParse(dto.Kind[ComponentPrefix.Length..], out UiComponentKind kind))
        {
            return null;
        }

        UiComponentModel? made = kind switch
        {
            UiComponentKind.Label => new UiLabelModel(),
            UiComponentKind.Button => new UiButtonModel(),
            UiComponentKind.StateButton => new UiStateButtonModel(),
            UiComponentKind.CheckBox => new UiCheckBoxModel(),
            UiComponentKind.RadioButton => new UiRadioButtonModel(),
            UiComponentKind.ToggleButton => new UiToggleButtonModel(),
            UiComponentKind.Hyperlink => new UiHyperlinkModel(),
            UiComponentKind.EditField => new UiEditFieldModel(),
            UiComponentKind.NumericEditField => new UiNumericEditFieldModel(),
            UiComponentKind.Spinner => new UiSpinnerModel(),
            UiComponentKind.TextArea => new UiTextAreaModel(),
            UiComponentKind.DropDown => new UiDropDownModel(),
            UiComponentKind.ListBox => new UiListBoxModel(),
            UiComponentKind.Slider => new UiSliderModel(),
            UiComponentKind.RangeSlider => new UiRangeSliderModel(),
            UiComponentKind.Image => new UiImageModel(),
            _ => null,
        };
        if (made is not null)
        {
            Fill(made, dto.Properties);
        }

        return made;
    }

    /// <summary>The cell a grid's child sits in, as row, last row, column, last column.</summary>
    public static int[]? ToDto(UiGridCell? cell) =>
        cell is { } at ? [at.Row, at.RowEnd, at.Column, at.ColumnEnd] : null;

    public static UiGridCell? ToCell(int[]? cell) =>
        cell is { Length: 4 } ? new UiGridCell(cell[0], cell[1], cell[2], cell[3]) : null;

    /// <summary>
    /// The properties a component's own classes declare — everything between it and
    /// <see cref="UiObject"/>, whose part the common fields of the DTO already hold.
    /// </summary>
    private static IEnumerable<PropertyInfo> Own(UiObject component)
    {
        var chain = new List<Type>();
        for (Type? type = component.GetType(); type is not null && type != typeof(UiObject); type = type.BaseType)
        {
            chain.Insert(0, type);
        }

        foreach (Type type in chain)
        {
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (property.CanRead && property.SetMethod is { IsPublic: true } && property.GetIndexParameters().Length == 0
                    && !NotInBag.Contains(property.Name))
                {
                    yield return property;
                }
            }
        }
    }

    private static Dictionary<string, JsonElement> Bag(UiObject component)
    {
        var bag = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (PropertyInfo property in Own(component))
        {
            bag[property.Name] = JsonSerializer.SerializeToElement(property.GetValue(component), property.PropertyType, BagOptions);
        }

        return bag;
    }

    private static void Fill(UiObject component, Dictionary<string, JsonElement>? bag)
    {
        if (bag is null)
        {
            return;
        }

        foreach (PropertyInfo property in Own(component))
        {
            if (!bag.TryGetValue(property.Name, out JsonElement stored))
            {
                continue;
            }

            try
            {
                object? value = stored.Deserialize(property.PropertyType, BagOptions);
                if (value is not null || Nullable.GetUnderlyingType(property.PropertyType) is not null || !property.PropertyType.IsValueType)
                {
                    property.SetValue(component, value);
                }
            }
            catch (Exception e) when (e is JsonException or NotSupportedException or TargetInvocationException or ArgumentException)
            {
                // A value that does not read leaves the property as the component made it.
            }
        }
    }
}
