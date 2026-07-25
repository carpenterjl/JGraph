using System.Security;
using System.Text;
using JGraph.Scripting.Jgs;

namespace JGraph.Controls.Scripting;

/// <summary>
/// The AvalonEdit syntax-highlighting definition (an <c>.xshd</c> document) for the JGS language. It is
/// registered with the shared <see cref="ICSharpCode.AvalonEdit.Highlighting.HighlightingManager"/> so the
/// editor can highlight JGS the same way it does the built-in C# and Python definitions. The keyword and
/// builtin word lists are generated from <see cref="JgsBuiltinCatalog"/> — the same registry that feeds
/// completion — so highlighting can never drift from the language.
/// </summary>
internal static class JgsSyntax
{
    /// <summary>The name JGS is registered and looked up under.</summary>
    public const string Name = "JGS";

    /// <summary>
    /// The <c>.xshd</c> highlighting definition for JGS, built from the builtin catalog and coloured
    /// for <paramref name="palette"/>.
    /// </summary>
    /// <param name="definitionName">The name to register the definition under.</param>
    /// <param name="palette">The token colours for the theme in force.</param>
    public static string Xshd(string definitionName, SyntaxPalette palette) => $"""
        <?xml version="1.0"?>
        <SyntaxDefinition name="{SecurityElement.Escape(definitionName)}" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
          <Color name="Comment" foreground="{palette.Comment}" />
          <Color name="String" foreground="{palette.Text}" />
          <Color name="Number" foreground="{palette.Number}" />
          <Color name="Keyword" foreground="{palette.Keyword}" fontWeight="bold" />
          <Color name="Builtin" foreground="{palette.Builtin}" />

          <RuleSet ignoreCase="false">
            <Span color="Comment" begin="\#" />
            <Span color="Comment" begin="//" />

            <Span color="String" multiline="false">
              <Begin>"</Begin>
              <End>"</End>
              <RuleSet>
                <Span begin="\\" end="." />
              </RuleSet>
            </Span>

            <Span color="String" multiline="false">
              <Begin>'</Begin>
              <End>'</End>
              <RuleSet>
                <Span begin="\\" end="." />
              </RuleSet>
            </Span>

            <Keywords color="Keyword">
        {Words(JgsBuiltinCatalog.Keywords)}
            </Keywords>

            <Keywords color="Builtin">
        {Words(JgsBuiltinCatalog.All.Select(static b => b.Name))}
            </Keywords>

            <Rule color="Number">
              \b0[xX][0-9a-fA-F]+|\b\d+(\.\d+)?([eE][+-]?\d+)?[ij]?
            </Rule>
          </RuleSet>
        </SyntaxDefinition>
        """;

    private static string Words(IEnumerable<string> words)
    {
        var sb = new StringBuilder();
        foreach (string word in words)
        {
            if (sb.Length > 0)
            {
                sb.AppendLine();
            }

            sb.Append("      <Word>").Append(SecurityElement.Escape(word)).Append("</Word>");
        }

        return sb.ToString();
    }
}
