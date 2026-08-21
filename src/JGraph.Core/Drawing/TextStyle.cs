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
        TextInterpreter interpreter = TextInterpreter.Tex,
        bool antialias = true)
    {
        Color = color;
        FontSize = fontSize;
        FontFamily = fontFamily;
        Bold = bold;
        Italic = italic;
        Interpreter = interpreter;
        Antialias = antialias;
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

    /// <summary>
    /// Whether the glyphs are drawn with antialiased edges (MATLAB's <c>FontSmoothing</c>). On is the
    /// default everywhere; off is for the pixel-exact exports a screenshot comparison wants.
    /// </summary>
    public bool Antialias { get; }

    public static TextStyle Default => new(Colors.Black);

    public TextStyle WithColor(Color color) =>
        new(color, FontSize, FontFamily, Bold, Italic, Interpreter, Antialias);

    public TextStyle WithSize(double fontSize) =>
        new(Color, fontSize, FontFamily, Bold, Italic, Interpreter, Antialias);

    public TextStyle WithBold(bool bold) =>
        new(Color, FontSize, FontFamily, bold, Italic, Interpreter, Antialias);

    /// <summary>The same style reading its text as <paramref name="interpreter"/> says.</summary>
    public TextStyle WithInterpreter(TextInterpreter interpreter) =>
        new(Color, FontSize, FontFamily, Bold, Italic, interpreter, Antialias);

    /// <summary>The same style with the given font family.</summary>
    public TextStyle WithFamily(string fontFamily) =>
        new(Color, FontSize, fontFamily, Bold, Italic, Interpreter, Antialias);

    /// <summary>The same style with italics turned on or off.</summary>
    public TextStyle WithItalic(bool italic) =>
        new(Color, FontSize, FontFamily, Bold, italic, Interpreter, Antialias);

    /// <summary>The same style with glyph smoothing turned on or off.</summary>
    public TextStyle WithAntialias(bool antialias) =>
        new(Color, FontSize, FontFamily, Bold, Italic, Interpreter, antialias);
}
