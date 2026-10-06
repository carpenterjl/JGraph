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
    private const string TabGroupKind = "uitabgroup";
    private const string TabKind = "uitab";
    private const string TreeNodeKind = "uitreenode";

    private static readonly JsonSerializerOptions BagOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>What the common part of a component already carries, and what is not the document's to keep.</summary>
    private static readonly HashSet<string> NotInBag = new(StringComparer.Ordinal)
    {
        nameof(UiComponentModel.UserWriteSeq),

        // U9: a style rule may name nodes, which are not a document's to keep.
        nameof(UiComponentModel.Styles),
    };

    /// <summary>A tree's selection and checks name nodes and stay out of a document (U9); a list's Selected is kept.</summary>
    private static readonly HashSet<string> TreeOnly = new(StringComparer.Ordinal)
    {
        nameof(UiTreeModel.Selected),
        nameof(UiTreeModel.Checked),
    };

    private static UiComponentDto? ComponentToDto(UiObject component)
    {
        switch (component)
        {
            // A tree's nodes are its children, each a bag of its own and its nodes under it (U9).
            case UiTreeModel tree:
            {
                var dto = new UiComponentDto { Kind = ComponentPrefix + tree.Kind, Properties = Bag(tree) };
                foreach (UiTreeNodeModel node in tree.Nodes)
                {
                    dto.Children.Add(NodeToDto(node));
                }

                return dto;
            }

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

            // A tab group and its tabs (U8): each its own bag, the tabs as the group's children,
            // and the tab that shows by its place among them.
            case UiTabGroupModel group:
            {
                var dto = new UiComponentDto { Kind = TabGroupKind, Properties = Bag(group) };
                foreach (UiObject child in group.Components)
                {
                    if (ToDto(child) is { } childDto)
                    {
                        if (ReferenceEquals(child, group.SelectedTab))
                        {
                            dto.SelectedChild = dto.Children.Count;
                        }

                        dto.Children.Add(childDto);
                    }
                }

                return dto;
            }

            case UiTabModel tab:
            {
                var dto = new UiComponentDto { Kind = TabKind, Properties = Bag(tab) };
                foreach (UiObject child in tab.Components)
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
        if (dto.Kind is TabGroupKind or TabKind)
        {
            UiContainerModel holder = dto.Kind == TabGroupKind ? new UiTabGroupModel() : new UiTabModel();
            Fill(holder, dto.Properties);
            foreach (UiComponentDto childDto in dto.Children)
            {
                // A tab group holds tabs and nothing else.
                if (ToModel(childDto) is { } child && (holder is UiTabModel || child is UiTabModel))
                {
                    holder.Components.Add(child);
                }
            }

            if (holder is UiTabGroupModel group && dto.SelectedChild is { } shown && shown >= 0 && shown < group.Components.Count
                && group.Components[shown] is UiTabModel showing)
            {
                group.Select(showing);
            }

            return holder;
        }

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
            UiComponentKind.Knob => new UiKnobModel(),
            UiComponentKind.DiscreteKnob => new UiDiscreteKnobModel(),
            UiComponentKind.Switch => new UiSwitchModel(UiSwitchStyle.Slider),
            UiComponentKind.RockerSwitch => new UiSwitchModel(UiSwitchStyle.Rocker),
            UiComponentKind.ToggleSwitch => new UiSwitchModel(UiSwitchStyle.Toggle),
            UiComponentKind.Gauge => new UiGaugeModel(),
            UiComponentKind.LinearGauge => new UiLinearGaugeModel(),
            UiComponentKind.NinetyDegreeGauge => new UiNinetyDegreeGaugeModel(),
            UiComponentKind.SemicircularGauge => new UiSemicircularGaugeModel(),
            UiComponentKind.Lamp => new UiLampModel(),
            UiComponentKind.DatePicker => new UiDatePickerModel(),
            UiComponentKind.ColorPicker => new UiColorPickerModel(),
            UiComponentKind.Tree => new UiTreeModel(),
            UiComponentKind.CheckBoxTree => new UiCheckBoxTreeModel(),
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
            UiComponentKind.Table => new UiTableModel(),
            _ => null,
        };
        if (made is not null)
        {
            Fill(made, dto.Properties);
        }

        if (made is UiTreeModel grown)
        {
            foreach (UiComponentDto childDto in dto.Children)
            {
                if (NodeToModel(childDto) is { } node)
                {
                    grown.Nodes.Add(node);
                }
            }
        }

        return made;
    }

    private static UiComponentDto NodeToDto(UiTreeNodeModel node)
    {
        var dto = new UiComponentDto
        {
            Kind = TreeNodeKind,
            Tag = node.Tag,
            Properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                [nameof(UiTreeNodeModel.Text)] = JsonSerializer.SerializeToElement(node.Text, BagOptions),
                [nameof(UiTreeNodeModel.IconSource)] = JsonSerializer.SerializeToElement(node.IconSource, BagOptions),
                [nameof(UiTreeNodeModel.Expanded)] = JsonSerializer.SerializeToElement(node.Expanded, BagOptions),
                [nameof(UiTreeNodeModel.Icon)] = JsonSerializer.SerializeToElement(node.Icon, BagOptions),
            },
        };
        foreach (UiTreeNodeModel child in node.Nodes)
        {
            dto.Children.Add(NodeToDto(child));
        }

        return dto;
    }

    private static UiTreeNodeModel? NodeToModel(UiComponentDto dto)
    {
        if (dto.Kind != TreeNodeKind)
        {
            return null;
        }

        var node = new UiTreeNodeModel { Tag = dto.Tag };
        if (dto.Properties is { } bag)
        {
            try
            {
                if (bag.TryGetValue(nameof(UiTreeNodeModel.Text), out JsonElement text))
                {
                    node.Text = text.Deserialize<string>(BagOptions) ?? string.Empty;
                }

                if (bag.TryGetValue(nameof(UiTreeNodeModel.IconSource), out JsonElement source))
                {
                    node.IconSource = source.Deserialize<string>(BagOptions) ?? string.Empty;
                }

                if (bag.TryGetValue(nameof(UiTreeNodeModel.Expanded), out JsonElement open))
                {
                    node.Expanded = open.Deserialize<bool>(BagOptions);
                }

                if (bag.TryGetValue(nameof(UiTreeNodeModel.Icon), out JsonElement icon))
                {
                    node.Icon = icon.Deserialize<UiImage>(BagOptions);
                }
            }
            catch (JsonException)
            {
                // A value that does not read leaves the node as it was made.
            }
        }

        foreach (UiComponentDto childDto in dto.Children)
        {
            if (NodeToModel(childDto) is { } child)
            {
                node.Nodes.Add(child);
            }
        }

        return node;
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
                    && !NotInBag.Contains(property.Name) && !(component is UiTreeModel && TreeOnly.Contains(property.Name)))
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
