using JGraph.Core.Model;
using JGraph.Serialization.Dto;

namespace JGraph.Serialization.Mapping;

/// <summary>
/// Maps a figure's app-building components to and from their document form (app-building plan,
/// decision Q9). A word this build does not know — a newer document's style or border — loads as the
/// default rather than as an error, the way every other part of a document does.
/// </summary>
internal static class UiComponentMapper
{
    public static UiComponentDto? ToDto(UiObject component)
    {
        UiComponentDto dto;
        switch (component)
        {
            case UiControlModel control:
                dto = new UiComponentDto
                {
                    Kind = "uicontrol",
                    Style = control.Style.ToString(),
                    Text = ToDto(control.Text),
                    Value = control.ValueIsSet ? [.. control.Value.Data] : null,
                    ValueRows = control.Value.Rows,
                    ValueColumns = control.Value.Columns,
                    Min = control.Min,
                    Max = control.Max,
                    ListboxTop = control.ListboxTop,
                    SliderStepSmall = control.SliderStepSmall,
                    SliderStepLarge = control.SliderStepLarge,
                    Alignment = control.HorizontalAlignment.ToString(),
                    BackgroundSet = control.BackgroundIsSet,
                    Background = control.BackgroundIsSet ? ToDto(control.BackgroundColor) : null,
                    Foreground = ToDto(control.ForegroundColor),
                    FontName = control.FontName,
                    FontSize = control.FontSize,
                    FontUnits = control.FontUnits.ToString(),
                    FontWeight = control.FontWeight,
                    FontAngle = control.FontAngle,
                    GroupManaged = control.GroupManaged,
                };
                if (control.Image is { } image)
                {
                    dto.ImageWidth = image.Width;
                    dto.ImageHeight = image.Height;
                    dto.ImagePixels = Convert.ToBase64String(image.Bgra);
                }

                break;

            case UiPanelModel panel:
                dto = new UiComponentDto
                {
                    Kind = panel switch
                    {
                        UiButtonGroupModel => "uibuttongroup",
                        UiProgressIndicatorModel => "uiprogressindicator",
                        _ => "uipanel",
                    },
                    Title = ToDto(panel.Title),
                    TitlePosition = panel.TitlePosition.ToString(),
                    BorderType = panel.BorderType.ToString(),
                    BorderWidth = panel.BorderWidth,
                    BorderColor = ToDto(panel.BorderColor),
                    HighlightColor = ToDto(panel.HighlightColor),
                    ShadowColor = ToDto(panel.ShadowColor),
                    BackgroundSet = true,
                    Background = ToDto(panel.BackgroundColor),
                    Foreground = ToDto(panel.ForegroundColor),
                    FontName = panel.FontName,
                    FontSize = panel.FontSize,
                    FontUnits = panel.FontUnits.ToString(),
                    FontWeight = panel.FontWeight,
                    FontAngle = panel.FontAngle,
                    AutoResizeChildren = panel.AutoResizeChildren,
                    Scrollable = panel.Scrollable,
                    Clipping = panel.Clipping,
                };
                if (panel is UiProgressIndicatorModel bar)
                {
                    dto.Progress = bar.Value;
                    dto.Indeterminate = bar.Indeterminate;
                    dto.ProgressColor = ToDto(bar.ProgressColor);
                }

                UiControlModel? selected = (panel as UiButtonGroupModel)?.SelectedObject;
                foreach (UiObject child in panel.Components)
                {
                    if (ToDto(child) is { } childDto)
                    {
                        if (ReferenceEquals(child, selected))
                        {
                            dto.SelectedChild = dto.Children.Count;
                        }

                        dto.Children.Add(childDto);
                    }
                }

                break;

            default:
                return null;
        }

        dto.Tag = component.Tag;
        dto.Visible = component.Visible;
        dto.Position = DtoConvert.ToDto(component.Position);
        dto.Units = component.Units.ToString();
        dto.Enable = component.Enable.ToString();
        dto.Tooltip = ToDto(component.Tooltip);
        return dto;
    }

    public static UiObject? ToModel(UiComponentDto dto)
    {
        UiObject component;
        switch (dto.Kind)
        {
            case "uicontrol":
            {
                var control = new UiControlModel
                {
                    Style = ParseOr(dto.Style, UiControlStyle.PushButton),
                    Text = ToText(dto.Text),
                    Min = dto.Min,
                    Max = dto.Max,
                    ListboxTop = dto.ListboxTop,
                    SliderStepSmall = dto.SliderStepSmall,
                    SliderStepLarge = dto.SliderStepLarge,
                    HorizontalAlignment = ParseOr(dto.Alignment, UiHorizontalAlignment.Center),
                    ForegroundColor = ToColor(dto.Foreground),
                    FontName = dto.FontName,
                    FontSize = dto.FontSize,
                    FontUnits = ParseOr(dto.FontUnits, UiFontUnits.Points),
                    FontWeight = dto.FontWeight,
                    FontAngle = dto.FontAngle,
                };
                if (dto.Value is { } value && value.Length == dto.ValueRows * dto.ValueColumns)
                {
                    control.Value = new UiNumbers(value, dto.ValueRows, dto.ValueColumns);
                }

                if (dto.BackgroundSet)
                {
                    control.BackgroundColor = ToColor(dto.Background);
                }

                control.GroupManaged = dto.GroupManaged;
                if (dto.ImagePixels is { } pixels && dto.ImageWidth > 0 && dto.ImageHeight > 0)
                {
                    try
                    {
                        byte[] bytes = Convert.FromBase64String(pixels);
                        if (bytes.Length == dto.ImageWidth * dto.ImageHeight * 4)
                        {
                            control.Image = new UiImage(dto.ImageWidth, dto.ImageHeight, bytes);
                        }
                    }
                    catch (FormatException)
                    {
                        // A picture that does not read is no picture; the control still loads.
                    }
                }

                component = control;
                break;
            }

            case "uipanel":
            case "uibuttongroup":
            case "uiprogressindicator":
            {
                UiPanelModel panel = dto.Kind switch
                {
                    "uibuttongroup" => new UiButtonGroupModel(),
                    "uiprogressindicator" => new UiProgressIndicatorModel
                    {
                        Value = dto.Progress ?? 0,
                        Indeterminate = dto.Indeterminate,
                    },
                    _ => new UiPanelModel(),
                };
                if (panel is UiProgressIndicatorModel indicator && ToColor(dto.ProgressColor) is { } fill)
                {
                    indicator.ProgressColor = fill;
                }

                panel.Title = ToText(dto.Title);
                panel.TitlePosition = ParseOr(dto.TitlePosition, UiTitlePosition.LeftTop);
                panel.BorderType = ParseOr(dto.BorderType, UiBorderType.Line);
                panel.BorderWidth = dto.BorderWidth;
                panel.BorderColor = ToColor(dto.BorderColor);
                panel.HighlightColor = ToColor(dto.HighlightColor);
                panel.ForegroundColor = ToColor(dto.Foreground);
                panel.FontName = dto.FontName;
                panel.FontSize = dto.FontSize;
                panel.FontUnits = ParseOr(dto.FontUnits, UiFontUnits.Points);
                panel.FontWeight = dto.FontWeight;
                panel.FontAngle = dto.FontAngle;
                panel.AutoResizeChildren = dto.AutoResizeChildren;
                panel.Scrollable = dto.Scrollable;
                panel.Clipping = dto.Clipping;
                if (ToColor(dto.Background) is { } background)
                {
                    panel.BackgroundColor = background;
                }

                if (ToColor(dto.ShadowColor) is { } shadow)
                {
                    panel.ShadowColor = shadow;
                }

                for (int i = 0; i < dto.Children.Count; i++)
                {
                    if (ToModel(dto.Children[i]) is { } child)
                    {
                        panel.Components.Add(child);
                        if (dto.SelectedChild == i && panel is UiButtonGroupModel group && child is UiControlModel button)
                        {
                            group.RestoreSelection(button);
                        }
                    }
                }

                component = panel;
                break;
            }

            default:
                // A kind from a later build: the document still opens, without that component.
                return null;
        }

        component.Tag = dto.Tag;
        component.Visible = dto.Visible;
        component.Units = ParseOr(dto.Units, UiUnits.Pixels);
        component.Position = DtoConvert.ToRect(dto.Position);
        component.Enable = ParseOr(dto.Enable, UiEnable.On);
        component.Tooltip = ToText(dto.Tooltip);
        return component;
    }

    /// <summary>The steps from a figure's components down to a container, by index at each level.</summary>
    public static int[]? PathTo(FigureModel figure, UiContainerModel? container)
    {
        if (container is null)
        {
            return null;
        }

        var steps = new List<int>();
        for (UiObject at = container; ; )
        {
            if (at.Container is not { } holder)
            {
                return null;
            }

            steps.Insert(0, holder.Components.IndexOf(at));
            if (ReferenceEquals(holder, figure))
            {
                return [.. steps];
            }

            if (holder is not UiObject up)
            {
                return null;
            }

            at = up;
        }
    }

    /// <summary>The container a path names in a figure just loaded, or null when it names none.</summary>
    public static UiContainerModel? Follow(FigureModel figure, int[]? path)
    {
        if (path is null || path.Length == 0)
        {
            return null;
        }

        IUiContainer holder = figure;
        UiContainerModel? found = null;
        foreach (int step in path)
        {
            if (step < 0 || step >= holder.Components.Count || holder.Components[step] is not UiContainerModel next)
            {
                return null;
            }

            found = next;
            holder = next;
        }

        return found;
    }

    private static UiTextDto? ToDto(UiText text) =>
        text.Lines.Count == 0 && text.Form == UiTextForm.CharRow
            ? null
            : new UiTextDto { Form = text.Form.ToString(), Lines = [.. text.Lines] };

    private static UiText ToText(UiTextDto? dto) =>
        dto is null ? UiText.Empty : new UiText(ParseOr(dto.Form, UiTextForm.CharRow), [.. dto.Lines]);

    private static double[]? ToDto(UiColor? color) => color is { } rgb ? [rgb.R, rgb.G, rgb.B] : null;

    private static UiColor? ToColor(double[]? rgb) => rgb is { Length: 3 } ? new UiColor(rgb[0], rgb[1], rgb[2]) : null;

    private static TEnum ParseOr<TEnum>(string? word, TEnum fallback)
        where TEnum : struct, Enum =>
        Enum.TryParse(word, ignoreCase: true, out TEnum parsed) ? parsed : fallback;
}
