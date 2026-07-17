namespace JGraph.Core.Drawing;

/// <summary>A curated set of named colors mirroring the common CSS/WPF palette.</summary>
public static class Colors
{
    public static Color Transparent => Color.FromArgb(0, 0, 0, 0);

    public static Color Black => Color.FromRgb(0, 0, 0);

    public static Color White => Color.FromRgb(255, 255, 255);

    public static Color Red => Color.FromRgb(0xFF, 0x00, 0x00);

    public static Color Green => Color.FromRgb(0x00, 0x80, 0x00);

    public static Color Blue => Color.FromRgb(0x00, 0x00, 0xFF);

    public static Color Cyan => Color.FromRgb(0x00, 0xFF, 0xFF);

    public static Color Magenta => Color.FromRgb(0xFF, 0x00, 0xFF);

    public static Color Yellow => Color.FromRgb(0xFF, 0xFF, 0x00);

    public static Color Orange => Color.FromRgb(0xFF, 0xA5, 0x00);

    public static Color Purple => Color.FromRgb(0x80, 0x00, 0x80);

    public static Color Gray => Color.FromRgb(0x80, 0x80, 0x80);

    public static Color LightGray => Color.FromRgb(0xD3, 0xD3, 0xD3);

    public static Color DarkGray => Color.FromRgb(0x40, 0x40, 0x40);

    public static Color DimGray => Color.FromRgb(0x69, 0x69, 0x69);

    public static Color WhiteSmoke => Color.FromRgb(0xF5, 0xF5, 0xF5);

    /// <summary>The default MATLAB-style series color order used when a series has no explicit color.</summary>
    public static readonly IReadOnlyList<Color> DefaultSeriesOrder = new[]
    {
        Color.FromRgb(0x00, 0x72, 0xBD), // blue
        Color.FromRgb(0xD9, 0x53, 0x19), // orange-red
        Color.FromRgb(0xED, 0xB1, 0x20), // yellow
        Color.FromRgb(0x7E, 0x2F, 0x8E), // purple
        Color.FromRgb(0x77, 0xAC, 0x30), // green
        Color.FromRgb(0x4D, 0xBE, 0xEE), // light blue
        Color.FromRgb(0xA2, 0x14, 0x2F), // dark red
    };
}
