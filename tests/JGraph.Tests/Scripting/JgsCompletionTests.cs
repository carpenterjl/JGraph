using JGraph.Scripting.Completion;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Jgs.Completion;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M16: the builtin catalog, the JGS completion engine (tolerant of broken buffers), signature help, and
/// the C#/Python word lists — all black-box through the public completion API.
/// </summary>
public sealed class JgsCompletionTests
{
    // --- Catalog -----------------------------------------------------------------------------------

    [Fact]
    public void Catalog_MatchesTheLiveBuiltinRegistration_Exactly()
    {
        // The one test that makes drift impossible: the catalog documents exactly the names the
        // interpreter actually defines (including run), no more, no fewer.
        var catalog = JgsBuiltinCatalog.All.Select(static b => b.Name).ToArray();
        Assert.Equal(JgsScriptEngine.BuiltinNames().OrderBy(static n => n, StringComparer.Ordinal), catalog);
    }

    [Fact]
    public void Catalog_RendersSignatures_WithOptionalMarkers()
    {
        JgsBuiltinInfo? plot = JgsBuiltinCatalog.Find("plot");
        Assert.NotNull(plot);
        Assert.Equal("plot(x, y, spec?)", plot!.Signature);
        Assert.Equal("figure(n?)", JgsBuiltinCatalog.Find("figure")!.Signature); // M19: figure(n) selects/creates.
        Assert.Null(JgsBuiltinCatalog.Find("nosuch"));
    }

    [Fact]
    public void Catalog_Keywords_ComeFromTheLexer()
    {
        Assert.Contains("let", JgsBuiltinCatalog.Keywords);
        Assert.Contains("fn", JgsBuiltinCatalog.Keywords);
        Assert.Contains("while", JgsBuiltinCatalog.Keywords);
        Assert.Contains("continue", JgsBuiltinCatalog.Keywords);
    }

    // --- Completions --------------------------------------------------------------------------------

    [Fact]
    public void Completions_FilterByPrefix_AndReportTheReplaceSpan()
    {
        const string code = "let x = li";
        JgsCompletionResult result = JgsCompletionEngine.GetCompletions(code, code.Length);

        Assert.Equal(code.Length - 2, result.ReplaceStart);
        Assert.Contains(result.Items, static i => i.Text == "linspace" && i.Kind == CompletionItemKind.Builtin);
        Assert.DoesNotContain(result.Items, static i => i.Text == "plot");
    }

    [Fact]
    public void Completions_OfferLetBindings_DeclaredBeforeTheCursor()
    {
        const string code = "let voltage = 1\nvol";
        JgsCompletionResult result = JgsCompletionEngine.GetCompletions(code, code.Length);

        Assert.Contains(result.Items, static i => i.Text == "voltage" && i.Kind == CompletionItemKind.Variable);
    }

    [Fact]
    public void Completions_DoNotOfferBindings_DeclaredAfterTheCursor()
    {
        const string code = "x\nlet yvalue = 2";
        JgsCompletionResult result = JgsCompletionEngine.GetCompletions(code, 1);

        Assert.DoesNotContain(result.Items, static i => i.Text == "yvalue");
    }

    [Fact]
    public void Completions_OfferFunctions_EvenAboveTheirDefinition_BecauseTheyHoist()
    {
        const string code = "sca\nfn scale(v, k) { return v * k }";
        JgsCompletionResult result = JgsCompletionEngine.GetCompletions(code, 3);

        CompletionItem item = Assert.Single(result.Items, static i => i.Text == "scale");
        Assert.Equal(CompletionItemKind.Function, item.Kind);
        Assert.Equal("scale(v, k)", item.Signature);
    }

    [Fact]
    public void Completions_OfferLoopVariables()
    {
        const string code = "for index in range(0, 5) {\n  ind";
        JgsCompletionResult result = JgsCompletionEngine.GetCompletions(code, code.Length);

        Assert.Contains(result.Items, static i => i.Text == "index" && i.Kind == CompletionItemKind.Variable);
    }

    [Fact]
    public void Completions_DoNotOfferTheIdentifierBeingTyped_AsItself()
    {
        const string code = "let fo";
        JgsCompletionResult result = JgsCompletionEngine.GetCompletions(code, code.Length);

        Assert.DoesNotContain(result.Items, static i => i.Text == "fo");
    }

    [Fact]
    public void Completions_SurviveSyntaxErrors_BeforeTheCursor()
    {
        // An unterminated string and a stray '@' would make the parser throw on line 1; harvesting
        // must still find the fn below.
        const string code = "let s = \"unterminated @\nfn helper(x) { return x }\nhel";
        JgsCompletionResult result = JgsCompletionEngine.GetCompletions(code, code.Length);

        Assert.Contains(result.Items, static i => i.Text == "helper" && i.Kind == CompletionItemKind.Function);
    }

    [Fact]
    public void Completions_StayQuiet_InsideStringsAndComments()
    {
        const string inString = "title(\"abc";
        Assert.Empty(JgsCompletionEngine.GetCompletions(inString, inString.Length).Items);

        const string inComment = "# com";
        Assert.Empty(JgsCompletionEngine.GetCompletions(inComment, inComment.Length).Items);

        const string inSlashComment = "let a = 1 // no";
        Assert.Empty(JgsCompletionEngine.GetCompletions(inSlashComment, inSlashComment.Length).Items);

        // M17: single-quoted strings suppress completion the same way.
        const string inSingle = "title('abc";
        Assert.Empty(JgsCompletionEngine.GetCompletions(inSingle, inSingle.Length).Items);
    }

    [Fact]
    public void Completions_ResumeAfterASingleQuotedString_Closes()
    {
        const string code = "let a = 'x' + li";
        JgsCompletionResult result = JgsCompletionEngine.GetCompletions(code, code.Length);

        Assert.Contains(result.Items, static i => i.Text == "linspace");
    }

    [Fact]
    public void Completions_IncludeWorkspaceSymbols()
    {
        var workspace = new[] { new CompletionItem("normalize", CompletionItemKind.Function, "normalize(v)", "fn — util.jgs") };
        JgsCompletionResult result = JgsCompletionEngine.GetCompletions("norm", 4, workspace);

        Assert.Contains(result.Items, static i => i.Text == "normalize");
    }

    [Fact]
    public void Completions_WithEmptyPrefix_OfferKeywordsAndBuiltins()
    {
        JgsCompletionResult result = JgsCompletionEngine.GetCompletions("", 0);

        Assert.Equal(0, result.ReplaceStart);
        Assert.Contains(result.Items, static i => i.Text == "let" && i.Kind == CompletionItemKind.Keyword);
        Assert.Contains(result.Items, static i => i.Text == "plot" && i.Kind == CompletionItemKind.Builtin);
    }

    // --- Signature help ------------------------------------------------------------------------------

    [Fact]
    public void SignatureHelp_ShowsTheCall_WithTheFirstParameterActive()
    {
        JgsSignatureHelp? help = JgsCompletionEngine.GetSignatureHelp("plot(", 5);

        Assert.NotNull(help);
        Assert.Equal("plot", help!.Name);
        Assert.Equal(new[] { "x", "y", "spec?" }, help.ParameterLabels);
        Assert.Equal(0, help.ActiveParameter);
    }

    [Fact]
    public void SignatureHelp_AdvancesTheActiveParameter_OnCommas()
    {
        const string code = "plot(x, ";
        JgsSignatureHelp? help = JgsCompletionEngine.GetSignatureHelp(code, code.Length);

        Assert.Equal(1, help!.ActiveParameter);
    }

    [Fact]
    public void SignatureHelp_FollowsTheInnermostCall_AndComesBackOut()
    {
        const string inner = "plot(sin(";
        Assert.Equal("sin", JgsCompletionEngine.GetSignatureHelp(inner, inner.Length)!.Name);

        const string backOut = "plot(sin(x), ";
        JgsSignatureHelp? outer = JgsCompletionEngine.GetSignatureHelp(backOut, backOut.Length);
        Assert.Equal("plot", outer!.Name);
        Assert.Equal(1, outer.ActiveParameter);
    }

    [Fact]
    public void SignatureHelp_IgnoresCommas_InsideArrayLiterals()
    {
        const string code = "plot([1, 2";
        JgsSignatureHelp? help = JgsCompletionEngine.GetSignatureHelp(code, code.Length);

        Assert.Equal("plot", help!.Name);
        Assert.Equal(0, help.ActiveParameter);
    }

    [Fact]
    public void SignatureHelp_ClampsToTheLastParameter_ForExtraArguments()
    {
        const string code = "xlim(1, 2, 3, ";
        JgsSignatureHelp? help = JgsCompletionEngine.GetSignatureHelp(code, code.Length);

        Assert.Equal("xlim", help!.Name);
        Assert.Equal(1, help.ActiveParameter); // clamped to 'max'
    }

    [Fact]
    public void SignatureHelp_ResolvesFunctions_DefinedInTheBuffer()
    {
        const string code = "fn scale(v, k) { return v * k }\nscale(1, ";
        JgsSignatureHelp? help = JgsCompletionEngine.GetSignatureHelp(code, code.Length);

        Assert.Equal("scale", help!.Name);
        Assert.Equal(new[] { "v", "k" }, help.ParameterLabels);
        Assert.Equal(1, help.ActiveParameter);
    }

    [Fact]
    public void SignatureHelp_ResolvesWorkspaceSymbols()
    {
        var workspace = new[] { new CompletionItem("helper", CompletionItemKind.Function, "helper(a, b)", "fn — util.jgs") };
        const string code = "helper(";
        JgsSignatureHelp? help = JgsCompletionEngine.GetSignatureHelp(code, code.Length, workspace);

        Assert.Equal(new[] { "a", "b" }, help!.ParameterLabels);
    }

    [Fact]
    public void SignatureHelp_IsNotOffered_ForAFnDeclarationsParameterList()
    {
        const string code = "fn scale(v, ";
        Assert.Null(JgsCompletionEngine.GetSignatureHelp(code, code.Length));
    }

    [Fact]
    public void SignatureHelp_IsNull_OutsideAnyCall_OrForUnknownCallees()
    {
        Assert.Null(JgsCompletionEngine.GetSignatureHelp("let x = 1", 9));
        Assert.Null(JgsCompletionEngine.GetSignatureHelp("mystery(", 8));
    }

    [Fact]
    public void ParameterLabels_RoundTripASignature()
    {
        Assert.Equal(new[] { "x", "y", "spec?" }, JgsCompletionEngine.ParameterLabels("plot(x, y, spec?)"));
        Assert.Empty(JgsCompletionEngine.ParameterLabels("figure()"));
    }

    // --- Harvesting for workspace symbols --------------------------------------------------------------

    [Fact]
    public void HarvestFunctions_ReturnsFnsWithSignatures_AndTheOriginNote()
    {
        IReadOnlyList<CompletionItem> items = JgsCompletionEngine.HarvestFunctions(
            "fn one() { return 1 }\nlet notAFn = 2\nfn add(a, b) { return a + b }", "util.jgs");

        Assert.Equal(2, items.Count);
        CompletionItem add = Assert.Single(items, static i => i.Text == "add");
        Assert.Equal("add(a, b)", add.Signature);
        Assert.Contains("util.jgs", add.Description);
        Assert.DoesNotContain(items, static i => i.Text == "notAFn");
    }

    // --- Word lists -------------------------------------------------------------------------------------

    [Fact]
    public void WordLists_CarryKeywords_FacadeMembers_AndHelpers()
    {
        Assert.Contains(WordListCompletion.CSharp, static i => i.Text == "foreach");
        Assert.Contains(WordListCompletion.CSharp, static i => i.Text == "JG");
        Assert.Contains(WordListCompletion.CSharp, static i => i.Text == "Plot");
        Assert.Contains(WordListCompletion.CSharp, static i => i.Text == "readcsv");

        Assert.Contains(WordListCompletion.Python, static i => i.Text == "def");
        Assert.Contains(WordListCompletion.Python, static i => i.Text == "Plot");

        Assert.Same(WordListCompletion.CSharp, WordListCompletion.ForLanguage("C#"));
        Assert.Same(WordListCompletion.Python, WordListCompletion.ForLanguage("Python"));
        Assert.Null(WordListCompletion.ForLanguage("JGS"));
    }
}
