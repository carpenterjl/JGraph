using System.Text.Json;
using JGraph.Scripting.Analysis;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting.Designer;

/// <summary>
/// The Code Analyzer's parts beside the oracle (App Designer window plan W10, ADR 0304): the fixes applied to the
/// text, what the <c>%#ok</c> comments suppress and how, the complexity counts, a parse error's message, the parser's
/// trailing comma and parenthesized <c>for</c>, and App Designer's coding alerts over a callback's lines.
/// </summary>
public class CodeAnalyzerTests
{
    private static string CaseText(string name)
    {
        using JsonDocument oracle = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripting", "Designer", "CodeAnalyzerOracle.json")));
        return oracle.RootElement.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("name").GetString() == name).GetProperty("text").GetString()!;
    }

    [Fact]
    public void Fixes_RewriteTheLinesAsR2025bsFixesDo()
    {
        string text = CaseText("a2_patterns.m");
        CodeAnalysis analysis = JgsCodeAnalyzer.Analyze(text, "a2_patterns.m");
        string[] wanted = ["CHARTEN", "NBRAK2", "LOGL", "STRQUOT", "RPMT1", "RPMTT", "RPMT0", "RPMTF", "STCI", "FVAL", "NOPTS", "NOSEMI", "ISCL", "ISMAT", "EQEFF"];
        string fixedText = CodeFixer.Apply(text, analysis.Messages.Where(m => wanted.Contains(m.Id) && m.Fix is not null).Select(m => m.Fix!));
        string[] lines = fixedText.Split('\n');
        Assert.Equal("c1 = newline;", lines[2]);
        Assert.Equal("c2 = ['a' newline 'b'];", lines[3]);
        Assert.Equal("f1 = sin(1);", lines[5]);
        Assert.Equal("f2 = sin(1);", lines[6]);
        Assert.Equal("fn = 'sin'; f3 = feval(fn, 1);", lines[7]);
        Assert.Equal("i1 = isscalar(v);", lines[10]);
        Assert.Equal("i2 = isscalar(v);", lines[11]);
        Assert.Equal("i4 = isscalar(v);", lines[13]);
        Assert.Equal("s1 = \"abc\";", lines[29]);
        Assert.Equal("m1 = ismatrix(v);", lines[33]);
        Assert.Equal("l1 = true;", lines[35]);
        Assert.Equal("r1 = ones(2, 3);", lines[37]);
        Assert.Equal("r2 = true(2, 3);", lines[38]);
        Assert.Equal("r3 = zeros(2, 3);", lines[39]);
        Assert.Equal("r4 = false(2, 3);", lines[40]);
        Assert.Equal("t1 = strcmpi(s, 'ABC');", lines[41]);
        Assert.Equal("k1 = 1;", lines[44]);
        Assert.Equal("k2 = x;", lines[45]);
        Assert.Equal("k5 = x;", lines[48]);
        Assert.Equal("k6 = 1;", lines[49]); // NOPTS put the semicolon in the comma's place, NOSEMI took the extra one
        Assert.Equal("k7 = 2;", lines[50]);
        Assert.Equal("[1 2];", lines[74]);
        Assert.Equal("{1};", lines[75]);
        // The fixes of a1: the argument and the output replaced with ~, the unused global's line gone, the idle %#ok gone.
        text = CaseText("a1_unused.m");
        analysis = JgsCodeAnalyzer.Analyze(text, "a1_unused.m");
        lines = CodeFixer.Apply(text, analysis.Messages.Where(m => m.Id is "INUSD" or "ASGLU" or "NUSED" or "MSNU").Select(m => m.Fix!)).Split('\n');
        Assert.Equal("function [out1, out2, out3] = a1_unused(a, ~, c, d)", lines[0]);
        Assert.Equal("[~, q] = size(a);", lines[3]);
        Assert.Equal("w = 1; ", lines[12]);
        Assert.Equal("global G2", lines[25]);
        Assert.Equal("function r = helper2(v, ~)", lines[30]);
    }

    [Fact]
    public void Suppressed_ListsWhatTheOkCommentsTookAndHow()
    {
        CodeAnalysis analysis = JgsCodeAnalyzer.Analyze(CaseText("a5_suppress.m"), "a5_suppress.m");
        var suppressed = analysis.Suppressed.Select(m => (m.Id, m.Line, m.StartColumn, m.Suppression)).ToList();
        Assert.Equal(
        [
            ("INUSD", 1, 22, CodeSuppression.Line), ("INUSD", 1, 25, CodeSuppression.Line),
            ("NASGU", 3, 1, CodeSuppression.File), ("NASGU", 4, 1, CodeSuppression.File), ("CHARTEN", 4, 5, CodeSuppression.Line),
            ("NASGU", 5, 1, CodeSuppression.File), ("NASGU", 6, 1, CodeSuppression.File), ("NASGU", 7, 1, CodeSuppression.File),
        ], suppressed);
        Assert.Equal(["MSNU", "CHARTEN", "MSNU", "MSNU", "MSNU"], analysis.Messages.Select(static m => m.Id));
        Assert.All(analysis.Messages, static m => Assert.Equal(CodeSuppression.No, m.Suppression));
        // The fix of an idle id inside a list takes the id and its comma; of a whole comment, the comment.
        string fixedText = CodeFixer.Apply(CaseText("a5_suppress.m"), analysis.Messages.Where(static m => m.Id == "MSNU").Select(static m => m.Fix!));
        string[] lines = fixedText.Split('\n');
        Assert.Equal("y = char(10); %#ok<CHARTEN>", lines[3]);
        Assert.Equal("z = char(10); ", lines[4]);
        Assert.Equal("w = 1;  a comment after", lines[5]);
        Assert.Equal("q = 1;    <NASGU>", lines[6]);
    }

    [Fact]
    public void Complexity_CountsDecisionsAsMcCabeDoes()
    {
        CodeAnalysis analysis = JgsCodeAnalyzer.Analyze(CaseText("c1_cyc.m"), "c1_cyc.m");
        Assert.Equal([("c1_cyc", 10, 9, 1, 14, 19), ("helper2", 2, 2, 29, 10, 16)], analysis.Complexity.Select(static c => (c.Name, c.McCabe, c.Modified, c.Line, c.StartColumn, c.EndColumn)));
        Assert.False(analysis.IsScript);
        CodeAnalysis script = JgsCodeAnalyzer.Analyze(CaseText("c3_script.m"), "c3_script.m");
        Assert.True(script.IsScript);
        Assert.Equal([("c3_script", 1, 1, true)], script.Complexity.Select(static c => (c.Name, c.McCabe, c.Line, c.IsScript)));
        Assert.Equal("The McCabe cyclomatic complexity is 1.", script.Complexity[0].McCabeMessage.Text);
        Assert.Equal("SCABE", script.Complexity[0].McCabeMessage.Id);
    }

    /// <summary>
    /// The checks of a name against its file (stage P5, ADR 0311): FNDEF's fix writes the file's name over
    /// the function's; MCFIL's renames the class and its constructor; MCDIR names the file's path when its
    /// <c>@</c> folder disagrees and offers no fix; NOIN sits on a signature line's name as on a bodied
    /// method's; a signature line raises nothing else.
    /// </summary>
    [Fact]
    public void NameChecks_FixTheNameAndReadTheClassFolder()
    {
        const string misnamed = "function r = othername(x)\nr = x;\nend\n";
        CodeAnalysis analysis = JgsCodeAnalyzer.Analyze(misnamed, "misnamed.m");
        CodeMessage fndef = Assert.Single(analysis.Messages);
        Assert.Equal(("FNDEF", CodeSeverity.Warning, 1, 14, 22, true), (fndef.Id, fndef.Severity, fndef.Line, fndef.StartColumn, fndef.EndColumn, fndef.Fixable));
        Assert.Equal("function r = misnamed(x)\nr = x;\nend\n", CodeFixer.Apply(misnamed, fndef.Fix!));

        const string wrongCtor = "classdef Otherctor\n    methods\n        function o = Otherctor()\n            disp(o)\n        end\n    end\nend\n";
        CodeMessage mcfil = Assert.Single(JgsCodeAnalyzer.Analyze(wrongCtor, "Wrongctor.m").Messages);
        Assert.Equal(("MCFIL", CodeSeverity.Error, 1, 10, 18, true), (mcfil.Id, mcfil.Severity, mcfil.Line, mcfil.StartColumn, mcfil.EndColumn, mcfil.Fixable));
        string[] renamed = CodeFixer.Apply(wrongCtor, mcfil.Fix!).Split('\n');
        Assert.Equal("classdef Wrongctor", renamed[0]);
        Assert.Equal("        function o = Wrongctor()", renamed[2]);

        string folder = Path.Combine(Path.GetTempPath(), "jgraph-p5-mcdir-" + Guid.NewGuid().ToString("N"), "@Mis");
        string file = Path.Combine(folder, "Mis.m");
        List<CodeMessage> inFolder = [.. JgsCodeAnalyzer.Analyze("classdef Wrong\nend\n", file).Messages];
        Assert.Equal(["MCDIR", "MCFIL"], inFolder.Select(static m => m.Id));
        Assert.Equal($"Class name 'Wrong' and @directory name do not agree: '{file}'.", inFolder[0].Text);
        Assert.Equal((CodeSeverity.Error, 1, 10, 14, false), (inFolder[0].Severity, inFolder[0].Line, inFolder[0].StartColumn, inFolder[0].EndColumn, inFolder[0].Fixable));
        Assert.Empty(JgsCodeAnalyzer.Analyze("classdef Mis\nend\n", file).Messages); // nothing when they agree

        const string signatures = "classdef Sig\n    methods\n        r = m(o)\n        [a, b] = two(o, k)\n        bare\n        r = noin\n    end\n    methods (Static)\n        s\n    end\n    methods (Abstract)\n        a\n    end\nend\n";
        var noin = JgsCodeAnalyzer.Analyze(signatures, "Sig.m").Messages.Select(static m => (m.Id, m.Line, m.StartColumn, m.EndColumn, m.Text)).ToList();
        Assert.Equal(
        [
            ("NOIN", 5, 9, 12, "Method 'bare' should either be a static method or have at least one input argument."),
            ("NOIN", 6, 13, 16, "Method 'noin' should either be a static method or have at least one input argument."),
        ], noin);
    }

    [Fact]
    public void ParseError_IsOneSynerMessageAtTheToken()
    {
        CodeAnalysis analysis = JgsCodeAnalyzer.Analyze("function a4_error()\nx = (1 + ;\ndisp(x)\nend\n", "a4_error.m");
        CodeMessage only = Assert.Single(analysis.Messages);
        Assert.Equal(("SYNER", CodeSeverity.Error, 2, 10, 10, "Parse error at ';': usage might be invalid MATLAB syntax."), (only.Id, only.Severity, only.Line, only.StartColumn, only.EndColumn, only.Text));
        Assert.Empty(analysis.Complexity);
    }

    [Theory]
    [InlineData("x = [1, 2,];")]
    [InlineData("y = {1, 2,};")]
    [InlineData("z = [1, 2; 3, 4,];")]
    [InlineData("w = [1, 2,; 3, 4];")]
    [InlineData("for (k = 1:3)\n    disp(k)\nend")]
    [InlineData("for (k = 1:3), disp(k), end")]
    public void Parser_TakesATrailingCommaAndAParenthesizedFor(string code)
    {
        IReadOnlyList<Stmt> program = Parser.Parse(code, "t.m", JgsDialect.Matlab);
        Assert.NotEmpty(program);
        Assert.DoesNotContain(JgsCodeAnalyzer.Analyze(code + "\n", "t.m").Messages, static m => m.Id == "SYNER");
    }

    [Fact]
    public void CodingAlerts_FollowR2025bsProgrammingTip()
    {
        string[] lines =
        [
            "        function ButtonPushed(app, event)",
            "            plot(x)",
            "            plot(app.UIAxes, x)",
            "            h = plot(x);",
            "            % plot(x) in a comment",
            "            disp plot",
            "            line(x, 'Parent', app.UIAxes)",
            "            scatter(x, 'Parent', app.Other)",
            "            imshow(I)",
            "            s = 'call plot(x) here';",
            "            app.Chart.plot(x)",
            "            plot(app, x)",
            "            title(app.UIAxes, 'T'); xlabel(y)",
            "            hold(app.UIAxes, 'on')",
            "            histogram(x, ...",
            "                'Parent', app.UIAxes)",
            "            bar(x, ...",
            "                'Parent', app.Other)",
            "        end",
        ];
        IReadOnlyList<CodeMessage> alerts = DesignCodingAlerts.Find(lines, new HashSet<int>(Enumerable.Range(2, lines.Length - 2)), ["UIAxes"]);
        Assert.Equal(
        [
            (2, 13, DesignCodingAlerts.MissingAxesHandle),
            (4, 17, DesignCodingAlerts.MissingAxesHandle),
            (8, 13, DesignCodingAlerts.MissingAxesHandle),
            (9, 13, DesignCodingAlerts.MissingAxesParent),
            (13, 37, DesignCodingAlerts.MissingAxesHandle),
            (17, 13, DesignCodingAlerts.MissingAxesHandle),
        ], alerts.Select(static a => (a.Line, a.StartColumn, a.Text)));
        Assert.All(alerts, static a => Assert.Equal(CodeSeverity.Warning, a.Severity));
        // Only the editable lines are searched, and a line that is not editable - the signature - never alerts.
        Assert.Empty(DesignCodingAlerts.Find(lines, new HashSet<int> { 1 }, ["UIAxes"]));
        // Without an axes property every call alerts, as R2025b's does.
        Assert.Contains(DesignCodingAlerts.Find(lines, null, []), static a => a.Line == 3);
    }
}
