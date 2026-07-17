using JGraph.Core.Model;

namespace JGraph.Core.Drawing;

/// <summary>
/// A named look for figures. A theme supplies the colors for figure/axes backgrounds, axis and tick
/// lines, text, and grid lines, the typography (font family and title/label/tick sizes), and the
/// ordered palette used to auto-color series. Themes are fully customizable: construct a
/// <see cref="Theme"/> with your own values, or implement this interface. Applying a theme mutates the
/// model so the change is observable and serializable.
/// </summary>
public interface ITheme
{
    string Name { get; }

    Color FigureBackground { get; }

    Color AxesBackground { get; }

    Color AxisLine { get; }

    Color TickLabel { get; }

    Color AxisLabel { get; }

    Color Title { get; }

    Color MajorGrid { get; }

    Color MinorGrid { get; }

    /// <summary>The font family applied to titles, axis labels, and tick labels.</summary>
    string FontFamily { get; }

    /// <summary>Point size of the figure-wide title.</summary>
    double FigureTitleFontSize { get; }

    /// <summary>Point size of each axes' title.</summary>
    double AxesTitleFontSize { get; }

    /// <summary>Point size of axis (X/Y) labels.</summary>
    double AxisLabelFontSize { get; }

    /// <summary>Point size of tick labels.</summary>
    double TickLabelFontSize { get; }

    /// <summary>Whether figure and axes titles are drawn bold.</summary>
    bool BoldTitles { get; }

    /// <summary>The ordered palette used to assign colors to series that have none set explicitly.</summary>
    IReadOnlyList<Color> SeriesPalette { get; }

    /// <summary>Applies this theme's colors and typography to a figure and all its axes.</summary>
    void Apply(FigureModel figure);
}
