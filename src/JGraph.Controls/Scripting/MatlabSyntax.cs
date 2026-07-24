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

    /// <summary>The <c>.xshd</c> highlighting definition for MATLAB.</summary>
    public static string Xshd { get; } = $"""
        <?xml version="1.0"?>
        <SyntaxDefinition name="MATLAB" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
          <Color name="Comment" foreground="#FF57A64A" />
          <Color name="String" foreground="#FFD69D85" />
          <Color name="Number" foreground="#FFB5CEA8" />
          <Color name="Keyword" foreground="#FF569CD6" fontWeight="bold" />
          <Color name="Builtin" foreground="#FF4EC9B0" />

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
