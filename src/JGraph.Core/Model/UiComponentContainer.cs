using System.ComponentModel;
using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>
/// The area a custom component - a script's class written under
/// <c>matlab.ui.componentcontainer.ComponentContainer</c> - stands for in its figure (app-building
/// plan, U10, ADR 0209). The object is the script's; this is what it occupies: a borderless panel
/// with R2025b's defaults (probe <c>u10_matrix</c>: pixels, <c>[100 100 100 100]</c>, the uifigure
/// grey), holding what the class's <c>setup</c> built in it. Drawn and laid out as any panel is.
/// </summary>
public sealed class UiComponentContainerModel : UiPanelModel
{
    /// <summary>Makes the area for an instance of the class named <paramref name="className"/>.</summary>
    public UiComponentContainerModel(string className)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);
        ClassName = className;
        Name = "ComponentContainer";
        Units = UiUnits.Pixels;
        Position = new Rect2D(100, 100, 100, 100);
        BorderType = UiBorderType.None;
        FontUnits = UiFontUnits.Pixels;
        FontSize = 12;
        FontName = "Helvetica";
    }

    /// <summary>The script's class, as <c>class(c)</c> answers it.</summary>
    [Browsable(false)]
    public string ClassName { get; }

    /// <summary>R2025b's <c>Type</c>: the class's name in lower case (<c>SpinnerGauge</c> is <c>spinnergauge</c>).</summary>
    [Browsable(false)]
    public string TypeName => (ClassName ?? "ComponentContainer").ToLowerInvariant(); // a model made without its constructor has no class

    /// <summary>
    /// Whether the class's <c>setup</c> is running: the one time R2025b lets anything be made in
    /// the area or moved into it (probe <c>u10_more</c>).
    /// </summary>
    [Browsable(false)]
    public bool InSetup { get; set; }
}
