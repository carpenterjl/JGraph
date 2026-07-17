using JGraph.Core.Model;

namespace JGraph.Core.Drawing;

/// <summary>
/// A concrete, customizable <see cref="ITheme"/>. Use the built-in <see cref="Light"/>,
/// <see cref="Dark"/>, <see cref="Presentation"/>, and <see cref="Ieee"/> themes, or construct one
/// with your own colors and typography.
/// </summary>
public sealed class Theme : ITheme
{
    public required string Name { get; init; }

    public required Color FigureBackground { get; init; }

    public required Color AxesBackground { get; init; }

    public required Color AxisLine { get; init; }

    public required Color TickLabel { get; init; }

    public required Color AxisLabel { get; init; }

    public required Color Title { get; init; }

    public required Color MajorGrid { get; init; }

    public required Color MinorGrid { get; init; }

    /// <inheritdoc />
    public string FontFamily { get; init; } = "Segoe UI";

    /// <inheritdoc />
    public double FigureTitleFontSize { get; init; } = 16;

    /// <inheritdoc />
    public double AxesTitleFontSize { get; init; } = 15;

    /// <inheritdoc />
    public double AxisLabelFontSize { get; init; } = 13;

    /// <inheritdoc />
    public double TickLabelFontSize { get; init; } = 11;

    /// <inheritdoc />
    public bool BoldTitles { get; init; } = true;

    public IReadOnlyList<Color> SeriesPalette { get; init; } = Colors.DefaultSeriesOrder;

    /// <summary>The default light theme: white canvas, dark ink, MATLAB-style series palette.</summary>
    public static Theme Light { get; } = new()
    {
        Name = "Light",
        FigureBackground = Colors.White,
        AxesBackground = Colors.White,
        AxisLine = Color.FromRgb(0x33, 0x33, 0x33),
        TickLabel = Color.FromRgb(0x33, 0x33, 0x33),
        AxisLabel = Color.FromRgb(0x22, 0x22, 0x22),
        Title = Colors.Black,
        MajorGrid = Color.FromRgb(0xD0, 0xD0, 0xD0),
        MinorGrid = Color.FromRgb(0xEC, 0xEC, 0xEC),
    };

    /// <summary>The default dark theme: near-black canvas, light ink, brightened series palette.</summary>
    public static Theme Dark { get; } = new()
    {
        Name = "Dark",
        FigureBackground = Color.FromRgb(0x1E, 0x1E, 0x1E),
        AxesBackground = Color.FromRgb(0x25, 0x25, 0x25),
        AxisLine = Color.FromRgb(0xC8, 0xC8, 0xC8),
        TickLabel = Color.FromRgb(0xC8, 0xC8, 0xC8),
        AxisLabel = Color.FromRgb(0xE0, 0xE0, 0xE0),
        Title = Colors.White,
        MajorGrid = Color.FromRgb(0x3A, 0x3A, 0x3A),
        MinorGrid = Color.FromRgb(0x2E, 0x2E, 0x2E),
        SeriesPalette = new[]
        {
            Color.FromRgb(0x4D, 0xBE, 0xEE),
            Color.FromRgb(0xF2, 0x8E, 0x2B),
            Color.FromRgb(0xED, 0xC9, 0x48),
            Color.FromRgb(0xB1, 0x7A, 0xD6),
            Color.FromRgb(0x8F, 0xD1, 0x4F),
            Color.FromRgb(0x59, 0xA1, 0x4F),
            Color.FromRgb(0xE1, 0x57, 0x59),
        },
    };

    /// <summary>
    /// A theme tuned for slides and posters: a white canvas with large, bold typography and a
    /// saturated, high-contrast palette that stays legible when projected.
    /// </summary>
    public static Theme Presentation { get; } = new()
    {
        Name = "Presentation",
        FigureBackground = Colors.White,
        AxesBackground = Colors.White,
        AxisLine = Color.FromRgb(0x20, 0x20, 0x20),
        TickLabel = Color.FromRgb(0x20, 0x20, 0x20),
        AxisLabel = Colors.Black,
        Title = Colors.Black,
        MajorGrid = Color.FromRgb(0xC4, 0xC4, 0xC4),
        MinorGrid = Color.FromRgb(0xE4, 0xE4, 0xE4),
        FontFamily = "Segoe UI Semibold",
        FigureTitleFontSize = 26,
        AxesTitleFontSize = 22,
        AxisLabelFontSize = 19,
        TickLabelFontSize = 16,
        BoldTitles = true,
        SeriesPalette = new[]
        {
            Color.FromRgb(0x00, 0x6B, 0xB6),
            Color.FromRgb(0xE1, 0x4B, 0x00),
            Color.FromRgb(0x0B, 0x8A, 0x3E),
            Color.FromRgb(0x7A, 0x2E, 0xA6),
            Color.FromRgb(0xC9, 0x00, 0x76),
            Color.FromRgb(0xB8, 0x86, 0x00),
            Color.FromRgb(0x00, 0x7C, 0x91),
        },
    };

    /// <summary>
    /// A theme tuned for IEEE-style two-column papers: a white canvas with a compact serif face,
    /// small type, thin faint gridlines, and a conservative, print-friendly palette.
    /// </summary>
    public static Theme Ieee { get; } = new()
    {
        Name = "IEEE",
        FigureBackground = Colors.White,
        AxesBackground = Colors.White,
        AxisLine = Colors.Black,
        TickLabel = Colors.Black,
        AxisLabel = Colors.Black,
        Title = Colors.Black,
        MajorGrid = Color.FromRgb(0xDA, 0xDA, 0xDA),
        MinorGrid = Color.FromRgb(0xEF, 0xEF, 0xEF),
        FontFamily = "Times New Roman",
        FigureTitleFontSize = 11,
        AxesTitleFontSize = 10,
        AxisLabelFontSize = 9,
        TickLabelFontSize = 8,
        BoldTitles = false,
        SeriesPalette = new[]
        {
            Color.FromRgb(0x1F, 0x3A, 0x8A),
            Color.FromRgb(0xA6, 0x1B, 0x1B),
            Color.FromRgb(0x1B, 0x5E, 0x20),
            Color.FromRgb(0x4A, 0x2C, 0x6D),
            Color.FromRgb(0x8A, 0x5A, 0x00),
            Color.FromRgb(0x00, 0x5A, 0x66),
            Color.FromRgb(0x3A, 0x3A, 0x3A),
        },
    };

    /// <inheritdoc />
    public void Apply(FigureModel figure)
    {
        ArgumentNullException.ThrowIfNull(figure);
        figure.Background = FigureBackground;
        figure.TitleStyle = Restyle(figure.TitleStyle, Title, FigureTitleFontSize, BoldTitles);

        foreach (AxesModel axes in figure.Axes)
        {
            axes.Background = AxesBackground;
            axes.TitleStyle = Restyle(axes.TitleStyle, Title, AxesTitleFontSize, BoldTitles);
            axes.Grid.MajorLineStyle = axes.Grid.MajorLineStyle.WithColor(MajorGrid);
            axes.Grid.MinorLineStyle = axes.Grid.MinorLineStyle.WithColor(MinorGrid);

            foreach (AxisModel axis in axes.XAxes)
            {
                ApplyAxis(axis);
            }

            foreach (AxisModel axis in axes.YAxes)
            {
                ApplyAxis(axis);
            }
        }
    }

    private void ApplyAxis(AxisModel axis)
    {
        axis.LabelStyle = Restyle(axis.LabelStyle, AxisLabel, AxisLabelFontSize, bold: false);
        axis.TickLabelStyle = Restyle(axis.TickLabelStyle, TickLabel, TickLabelFontSize, bold: false);
    }

    /// <summary>Rebuilds a text style with this theme's color, size, family, and weight (keeping italics).</summary>
    private TextStyle Restyle(TextStyle style, Color color, double fontSize, bool bold) =>
        new(color, fontSize, FontFamily, bold, style.Italic);
}
