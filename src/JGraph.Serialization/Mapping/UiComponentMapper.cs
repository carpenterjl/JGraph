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
                };
                break;

            case UiPanelModel panel:
                dto = new UiComponentDto
                {
                    Kind = "uipanel",
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
                foreach (UiObject child in panel.Components)
                {
                    if (ToDto(child) is { } childDto)
                    {
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

                component = control;
                break;
            }

            case "uipanel":
            {
                var panel = new UiPanelModel
                {
                    Title = ToText(dto.Title),
                    TitlePosition = ParseOr(dto.TitlePosition, UiTitlePosition.LeftTop),
                    BorderType = ParseOr(dto.BorderType, UiBorderType.Line),
                    BorderWidth = dto.BorderWidth,
                    BorderColor = ToColor(dto.BorderColor),
                    HighlightColor = ToColor(dto.HighlightColor),
                    ForegroundColor = ToColor(dto.Foreground),
                    FontName = dto.FontName,
                    FontSize = dto.FontSize,
                    FontUnits = ParseOr(dto.FontUnits, UiFontUnits.Points),
                    FontWeight = dto.FontWeight,
                    FontAngle = dto.FontAngle,
                    AutoResizeChildren = dto.AutoResizeChildren,
                    Scrollable = dto.Scrollable,
                    Clipping = dto.Clipping,
                };
                if (ToColor(dto.Background) is { } background)
                {
                    panel.BackgroundColor = background;
                }

                if (ToColor(dto.ShadowColor) is { } shadow)
                {
                    panel.ShadowColor = shadow;
                }

                foreach (UiComponentDto childDto in dto.Children)
                {
                    if (ToModel(childDto) is { } child)
                    {
                        panel.Components.Add(child);
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
