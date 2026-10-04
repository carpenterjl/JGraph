namespace JGraph.Serialization.Dto;

/// <summary>
/// The serialized form of an app-building component (app-building plan, decision Q9): a
/// <c>uicontrol</c> or a <c>uipanel</c>, told apart by <see cref="Kind"/>, with a panel's components
/// nested in <see cref="Children"/>. One class for both, so a kind a later stage adds needs a word
/// and its own fields rather than a new shape. What is saved is the component as a script could read
/// it back; its callbacks are script-side state and are not, as with every other object's.
/// A colour is three numbers in [0, 1], or null for MATLAB's <c>'none'</c>.
/// </summary>
public sealed class UiComponentDto
{
    /// <summary><c>uicontrol</c>, <c>uipanel</c> or <c>uibuttongroup</c>.</summary>
    public string Kind { get; set; } = "uicontrol";

    public string? Tag { get; set; }

    public bool Visible { get; set; } = true;

    public RectDto Position { get; set; } = new(20, 20, 60, 20);

    public string Units { get; set; } = "Pixels";

    public string Enable { get; set; } = "On";

    public UiTextDto? Tooltip { get; set; }

    public string FontName { get; set; } = "MS Sans Serif";

    public double FontSize { get; set; } = 8;

    public string FontUnits { get; set; } = "Points";

    public string FontWeight { get; set; } = "normal";

    public string FontAngle { get; set; } = "normal";

    /// <summary>Whether <see cref="Background"/> was set; a uicontrol's otherwise follows its style.</summary>
    public bool BackgroundSet { get; set; }

    public double[]? Background { get; set; }

    public double[]? Foreground { get; set; }

    // --- uicontrol ---

    public string? Style { get; set; }

    public UiTextDto? Text { get; set; }

    /// <summary>Null while the control's <c>Value</c> still follows its style.</summary>
    public double[]? Value { get; set; }

    public int ValueRows { get; set; } = 1;

    public int ValueColumns { get; set; } = 1;

    public double Min { get; set; }

    public double Max { get; set; } = 1;

    public double ListboxTop { get; set; } = 1;

    public double SliderStepSmall { get; set; } = 0.01;

    public double SliderStepLarge { get; set; } = 0.1;

    public string? Alignment { get; set; }

    /// <summary>Whether the button group the control sits in watches its <c>Value</c> (U3).</summary>
    public bool GroupManaged { get; set; }

    /// <summary>The control's <c>CData</c> picture: its width, and its pixels as base-64 BGRA rows (U3).</summary>
    public int ImageWidth { get; set; }

    public int ImageHeight { get; set; }

    public string? ImagePixels { get; set; }

    // --- uipanel ---

    public UiTextDto? Title { get; set; }

    public string? TitlePosition { get; set; }

    public string? BorderType { get; set; }

    public double BorderWidth { get; set; } = 1;

    public double[]? BorderColor { get; set; }

    public double[]? HighlightColor { get; set; }

    public double[]? ShadowColor { get; set; }

    public bool AutoResizeChildren { get; set; }

    public bool Scrollable { get; set; }

    public bool Clipping { get; set; } = true;

    /// <summary>A button group's selected button, as its place among <see cref="Children"/>; null for none (U3).</summary>
    public int? SelectedChild { get; set; }

    /// <summary>A progress indicator's value from 0 to 1; null for every other kind (U4).</summary>
    public double? Progress { get; set; }

    /// <summary>Whether a progress indicator shows activity of no known length (U4).</summary>
    public bool Indeterminate { get; set; }

    /// <summary>A progress indicator's fill colour (U4).</summary>
    public double[]? ProgressColor { get; set; }

    public List<UiComponentDto> Children { get; set; } = new();
}

/// <summary>A component's text and the shape it reads back in: a char row, a char matrix or a cell.</summary>
public sealed class UiTextDto
{
    public string Form { get; set; } = "CharRow";

    public List<string> Lines { get; set; } = new();
}
