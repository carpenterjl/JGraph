using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>
/// The size a thing asks for in a <c>'fit'</c> track of a grid (app-building plan, U5). R2025b
/// measures these in its browser view; the rules here were worked out from its settled answers
/// (probe <c>u5_grid</c>) and reproduce them to the 64th of a pixel it reports in, given the same
/// font: a line of text is 1.23 times its font size high, whatever the font; text is as wide as its
/// glyphs, rounded up to a 64th; and each class adds its own margins.
/// </summary>
public static class UiFit
{
    /// <summary>The width of an edit field per pixel of font size, less its 10-pixel margin.</summary>
    private const double EditWidthPerPixel = 113.265625 / 12;

    /// <summary>The width of a text area per pixel of font size, less its 10-pixel margin.</summary>
    private const double AreaWidthPerPixel = 169.90625 / 12;

    /// <summary>A length rounded up to the 64th of a pixel a browser lays text out in.</summary>
    public static double Ceil64(double length) => System.Math.Ceiling((length * 64) - 1e-9) / 64;

    /// <summary>The height of one line of text at a font size.</summary>
    public static double LineHeight(double fontSize) => Ceil64(1.23 * fontSize);

    /// <summary>The width of the widest of some lines, rounded up to a 64th; an empty line is a space wide.</summary>
    public static double TextWidth(IReadOnlyList<string> lines, string fontName, double fontSize, bool bold, bool italic)
    {
        ArgumentNullException.ThrowIfNull(lines);
        double widest = 0;
        foreach (string line in lines.Count == 0 ? [string.Empty] : lines)
        {
            string shown = line.Length == 0 ? " " : line;
            widest = System.Math.Max(
                widest,
                UiFonts.Measure(shown, fontName, fontSize, bold, italic).Width + UiFonts.Kerning(shown, fontName, fontSize, bold, italic));
        }

        return Ceil64(widest);
    }

    /// <summary>What anything a grid can hold asks for.</summary>
    public static Size2D Of(GraphObject child) => child switch
    {
        UiComponentModel component => Of(component),
        UiGridLayoutModel grid => grid.Arrange(null).Content,
        UiPanelModel panel => OfPanel(panel),
        UiObject other => new Size2D(other.Position.Width, other.Position.Height),
        AxesModel axes => axes.PixelBounds is { } pinned ? new Size2D(pinned.Width, pinned.Height) : new Size2D(400, 300),
        _ => new Size2D(0, 0),
    };

    /// <summary>
    /// A panel asks for what the grid inside it asks for, plus its border and its title; one with no
    /// grid, for the size it was given, plus the 2 and 1 pixels R2025b adds.
    /// </summary>
    private static Size2D OfPanel(UiPanelModel panel)
    {
        foreach (UiObject inside in panel.Components)
        {
            if (inside is UiGridLayoutModel grid)
            {
                Size2D content = grid.Arrange(null).Content;
                Thickness inset = panel.GridInsets();
                return new Size2D(content.Width + inset.Left + inset.Right, content.Height + inset.Top + inset.Bottom);
            }
        }

        Rect2D box = panel.Position;
        return new Size2D(box.Width + 2, box.Height + 1);
    }

    /// <summary>What a component of a <c>uifigure</c> asks for.</summary>
    public static Size2D Of(UiComponentModel component)
    {
        ArgumentNullException.ThrowIfNull(component);
        double size = component.FontSize;
        double line = LineHeight(size);
        IReadOnlyList<string> lines = component.ShownLines;
        double Text() => TextWidth(lines, component.FontName, size, component.Bold, component.Italic);
        int count = System.Math.Max(1, lines.Count);
        switch (component)
        {
            case UiLabelModel or UiHyperlinkModel:
                return new Size2D(Text() + 2, (count * line) + 2);

            case UiCheckBoxModel or UiRadioButtonModel:
                return new Size2D(Text() + 19, (count * line) + 2);

            case UiCaptionModel button: // the three buttons: whole pixels, and room for an icon
            {
                double icon = button.Icon is null && button.IconSource.Length == 0 ? 0 : 20;
                return new Size2D(
                    System.Math.Ceiling(Text() - 1e-9) + 10 + icon,
                    System.Math.Ceiling((count * 1.23 * size) - 1e-9) + 8);
            }

            case UiEditFieldModel:
                return new Size2D(Ceil64(EditWidthPerPixel * size) + 10, line + 8);

            case UiSpinnerModel:
                return new Size2D(Digits(component) + 32, System.Math.Max(22, line + 4));

            case UiNumericModel:
                return new Size2D(Digits(component) + 10, line + 8);

            case UiTextAreaModel:
                return new Size2D(Ceil64(AreaWidthPerPixel * size) + 10, (4 * line) + 6);

            case UiDropDownModel:
                return new Size2D(Text() + 33, line + 8);

            case UiListBoxModel list:
                return new Size2D(
                    System.Math.Ceiling(Text() - 1e-9) + 10,
                    (System.Math.Max(1, list.Items.Count) * System.Math.Round(1.5 * size)) + 2);

            case UiSliderModel slider:
                return slider.Vertical ? new Size2D(39, 164) : new Size2D(164, 39);

            default:
            {
                Rect2D box = component.Position;
                return new Size2D(box.Width, box.Height);
            }
        }
    }

    /// <summary>The width of the ten digits in a component's font: what a numeric field is sized by.</summary>
    private static double Digits(UiComponentModel component) =>
        TextWidth(["0123456789"], component.FontName, component.FontSize, component.Bold, component.Italic);
}
