namespace JGraph.Core.Drawing;

/// <summary>An immutable description of how text is rendered.</summary>
public readonly struct TextStyle
{
    public TextStyle(
        Color color,
        double fontSize = 12.0,
        string fontFamily = "Segoe UI",
        bool bold = false,
        bool italic = false)
    {
        Color = color;
        FontSize = fontSize;
        FontFamily = fontFamily;
        Bold = bold;
        Italic = italic;
    }

    public Color Color { get; }

    public double FontSize { get; }

    public string FontFamily { get; }

    public bool Bold { get; }

    public bool Italic { get; }

    public static TextStyle Default => new(Colors.Black);

    public TextStyle WithColor(Color color) => new(color, FontSize, FontFamily, Bold, Italic);

    public TextStyle WithSize(double fontSize) => new(Color, fontSize, FontFamily, Bold, Italic);

    public TextStyle WithBold(bool bold) => new(Color, FontSize, FontFamily, bold, Italic);
}
