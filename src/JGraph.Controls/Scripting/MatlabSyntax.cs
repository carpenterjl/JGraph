using System.Security;
using System.Text;
using JGraph.Scripting.Jgs;

namespace JGraph.Controls.Scripting;

/// <summary>
/// The AvalonEdit highlighting definition for MATLAB (<c>.m</c>) files. It shares the builtin word list
/// with <see cref="JgsSyntax"/> — one interpreter, one catalog — and differs where the two languages
/// spell things differently: <c>%</c> opens a comment, and the keyword list is MATLAB's.
/// </summary>
internal static class MatlabSyntax
{
    /// <summary>The name MATLAB is registered and looked up under (also the engine's Language).</summary>
    public const string Name = "MATLAB";

    /// <summary>The <c>.xshd</c> highlighting definition for MATLAB, coloured for <paramref name="palette"/>.</summary>
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
            <Span color="Comment" begin="%" />

            <Span color="String" multiline="false">
              <Begin>"</Begin>
              <End>"</End>
            </Span>

            <Span color="String" multiline="false">
              <Begin>'</Begin>
              <End>'</End>
            </Span>

            <Keywords color="Keyword">
        {Words(JgsBuiltinCatalog.MatlabKeywords)}
            </Keywords>

            <Keywords color="Builtin">
        {Words(JgsBuiltinCatalog.All.Select(static b => b.Name))}
            </Keywords>

            <Rule color="Number">
              \b\d+(\.\d+)?([eE][+-]?\d+)?[ij]?
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
