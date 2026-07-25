namespace JGraph.Controls.Scripting;

/// <summary>
/// The token colours a JGraph-authored syntax definition uses, as <c>#AARRGGBB</c> strings ready to be
/// substituted into an <c>.xshd</c> document.
/// </summary>
/// <remarks>
/// Two palettes rather than one because a keyword colour picked for white is not a cosmetic problem on
/// black — it is a legibility one. Both sets are Visual Studio's, which is what a scientific IDE's users
/// already read code in.
/// </remarks>
/// <param name="Comment">Comments.</param>
/// <param name="Text">String and character literals.</param>
/// <param name="Number">Numeric literals.</param>
/// <param name="Keyword">Language keywords.</param>
/// <param name="Builtin">Built-in function names.</param>
internal sealed record SyntaxPalette(
    string Comment, string Text, string Number, string Keyword, string Builtin)
{
    /// <summary>Colours for a white editor background.</summary>
    public static SyntaxPalette Light { get; } = new(
        Comment: "#FF008000", Text: "#FFA31515", Number: "#FF098658",
        Keyword: "#FF0000FF", Builtin: "#FF2B91AF");

    /// <summary>Colours for a near-black editor background.</summary>
    public static SyntaxPalette Dark { get; } = new(
        Comment: "#FF57A64A", Text: "#FFD69D85", Number: "#FFB5CEA8",
        Keyword: "#FF569CD6", Builtin: "#FF4EC9B0");

    /// <summary>The palette for a theme.</summary>
    public static SyntaxPalette For(bool dark) => dark ? Dark : Light;
}
