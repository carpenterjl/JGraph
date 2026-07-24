using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M28.S1: the <see cref="JgsDialect"/> seam. One pipeline serves two languages, so these tests pin the
/// two presets, the user-options overlay, and — most importantly — that threading the dialect through
/// changed nothing for JGS: an omitted dialect must behave exactly like the shipped language.
/// </summary>
[Collection("JG facade")]
public class JgsDialectTests : IDisposable
{
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public JgsDialectTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private ScriptContext Context => new(_output, (_, figure) => _figures.Add(figure), null);

    private ScriptRunResult Run(string code, JgsDialect? dialect = null) =>
        JgsRunner.Run(code, Context, default, sourceId: "", hook: null, dialect);

    [Fact]
    public void JgsPreset_IsTheLanguageAsShipped()
    {
        JgsDialect jgs = JgsDialect.Jgs;

        Assert.Equal("JGS", jgs.Name);
        Assert.Equal(0, jgs.IndexBase);
        Assert.True(jgs.RequireLet);
        Assert.False(jgs.PercentComment);
        Assert.False(jgs.QuoteTranspose);
        Assert.False(jgs.CopyOnAssign);
        Assert.False(jgs.CellBraceSyntax);
        Assert.False(jgs.FunctionScope);
        Assert.False(jgs.IsMatlab);
    }

    [Fact]
    public void MatlabPreset_IsMatlabSemantics()
    {
        JgsDialect matlab = JgsDialect.Matlab;

        Assert.Equal("MATLAB", matlab.Name);
        Assert.Equal(1, matlab.IndexBase);
        Assert.False(matlab.RequireLet);
        Assert.True(matlab.PercentComment);
        Assert.True(matlab.QuoteTranspose);
        Assert.True(matlab.CopyOnAssign);
        Assert.True(matlab.MatlabFunctions);
        Assert.True(matlab.MatlabBlocks);
        Assert.True(matlab.CellBraceSyntax);
        Assert.True(matlab.FunctionScope);
        Assert.True(matlab.IsMatlab);
    }

    [Fact]
    public void JgsWith_AppliesOnlyTheTwoUserOptions()
    {
        JgsDialect relaxed = JgsDialect.JgsWith(new JgsLanguageOptions(RequireLet: false, IndexBase: 1));

        Assert.Equal("JGS", relaxed.Name);
        Assert.False(relaxed.RequireLet);
        Assert.Equal(1, relaxed.IndexBase);

        // Everything else stays JGS — a user preference must not turn JGS into MATLAB.
        Assert.False(relaxed.PercentComment);
        Assert.False(relaxed.QuoteTranspose);
        Assert.False(relaxed.CopyOnAssign);
        Assert.False(relaxed.CellBraceSyntax);
    }

    [Fact]
    public void LanguageOptions_Defaults_MatchTheShippedLanguage()
    {
        Assert.True(JgsLanguageOptions.Default.RequireLet);
        Assert.Equal(0, JgsLanguageOptions.Default.IndexBase);
        Assert.Equal(JgsDialect.Jgs, JgsDialect.JgsWith(JgsLanguageOptions.Default));
    }

    [Fact]
    public void LanguageOptions_Sanitized_ClampsAnUnknownIndexBase()
    {
        // A hand-edited settings file must not put the interpreter in a state no rule covers.
        Assert.Equal(0, new JgsLanguageOptions(IndexBase: 7).Sanitized().IndexBase);
        Assert.Equal(1, new JgsLanguageOptions(IndexBase: 1).Sanitized().IndexBase);
    }

    [Fact]
    public void Percent_IsModuloInJgs_AndACommentInMatlab()
    {
        Assert.Equal(TokenType.Percent, Lexer.Tokenize("7 % 3")[1].Type);

        IReadOnlyList<Token> matlab = Lexer.Tokenize("7 % 3", tolerant: false, JgsDialect.Matlab);
        Assert.Equal(TokenType.Number, matlab[0].Type);
        Assert.Equal(TokenType.Eof, matlab[1].Type);
    }

    [Fact]
    public void OmittedDialect_MeansJgs()
    {
        var interpreter = new Interpreter(new JgsEnvironment(), default);
        Assert.Equal(JgsDialect.Jgs, interpreter.Dialect);
    }

    [Fact]
    public void JgsRun_IsUnchanged_WhetherTheDialectIsOmittedOrPassed()
    {
        const string code = """
            let x = [10, 20, 30]
            disp(x(0))
            disp(7 % 3)
            """;

        ScriptRunResult implicitJgs = Run(code);
        string first = _output.NormalText;
        _output.Normal.Clear();
        ScriptRunResult explicitJgs = Run(code, JgsDialect.Jgs);

        Assert.True(implicitJgs.Success, implicitJgs.Message);
        Assert.True(explicitJgs.Success, explicitJgs.Message);
        Assert.Equal(first, _output.NormalText);
        Assert.Contains("10", first);   // 0-based: x(0) is the first element
        Assert.Contains("1", first);    // 7 % 3
    }

    [Fact]
    public void Find_ReportsIndicesInTheDialectsBase()
    {
        ScriptRunResult jgs = Run("disp(find([0, 1, 1]))");
        Assert.True(jgs.Success, jgs.Message);
        Assert.Contains("[1, 2]", _output.NormalText); // 0-based positions of the two ones

        _output.Normal.Clear();
        ScriptRunResult matlab = Run("disp(find([0, 1, 1]))", JgsDialect.Matlab);
        Assert.True(matlab.Success, matlab.Message);
        Assert.Contains("[2, 3]", _output.NormalText); // the same elements, numbered from 1
    }
}
