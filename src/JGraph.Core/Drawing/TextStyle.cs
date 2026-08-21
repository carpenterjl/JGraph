namespace JGraph.Core.Drawing;

/// <summary>An immutable description of how text is rendered.</summary>
public readonly struct TextStyle
{
    public TextStyle(
        Color color,
        double fontSize = 12.0,
        string fontFamily = "Segoe UI",
        bool bold = false,
        bool italic = false,
        TextInterpreter interpreter = TextInterpreter.Tex)
    {
        Color = color;
        FontSize = fontSize;
        FontFamily = fontFamily;
        Bold = bold;
        Italic = italic;
        Interpreter = interpreter;
    }

    public Color Color { get; }

    public double FontSize { get; }

    public string FontFamily { get; }

    public bool Bold { get; }

    public bool Italic { get; }

    /// <summary>
    /// Which markup this run is written in (MATLAB's <c>Interpreter</c> property). TeX is the default
    /// there and here, so a label written with \sigma or ^{2} reads as one without being asked to.
    /// </summary>
    public TextInterpreter Interpreter { get; }

    public static TextStyle Default => new(Colors.Black);

    public TextStyle WithColor(Color color) =>
        new(color, FontSize, FontFamily, Bold, Italic, Interpreter);

    public TextStyle WithSize(double fontSize) =>
        new(Color, fontSize, FontFamily, Bold, Italic, Interpreter);

    public TextStyle WithBold(bool bold) =>
        new(Color, FontSize, FontFamily, bold, Italic, Interpreter);

    /// <summary>The same style reading its text as <paramref name="interpreter"/> says.</summary>
    public TextStyle WithInterpreter(TextInterpreter interpreter) =>
        new(Color, FontSize, FontFamily, Bold, Italic, interpreter);
}
