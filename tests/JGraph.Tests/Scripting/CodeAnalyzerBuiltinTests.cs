using System.Text.Json;
using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// <c>checkcode</c>, <c>mlint</c> and <c>codeIssues</c> at the prompt (App Designer window plan W10, ADR 0304): the display
/// forms, the string and struct outputs, the two outputs over a list of files, <c>-text</c>, <c>-cyc</c>, <c>-modcyc</c>
/// and <c>-notok</c>, the refusals in R2025b's identifiers and words, <c>codeIssues</c>' object, its tables, its folder
/// walk and <c>fix</c> writing the files - as R2025b did headless over the same files (<c>w10_analyzer</c>, <c>w10_rules</c>).
/// </summary>
[Collection("JG facade")]
public sealed class CodeAnalyzerBuiltinTests : IAsyncLifetime
{
    private readonly RecordingScriptOutput _output = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jgraph-w10-words", Path.GetRandomFileName());
    private JgsReplSession _session = null!;

    public Task InitializeAsync()
    {
        JG.Reset();
        Directory.CreateDirectory(_folder);
        using JsonDocument oracle = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripting", "Designer", "CodeAnalyzerOracle.json")));
        foreach (JsonElement c in oracle.RootElement.GetProperty("cases").EnumerateArray())
        {
            string name = c.GetProperty("name").GetString()!;
            if (name is "a7_cyc.m" or "a8_clean.m" or "a5_suppress.m" or "a2_patterns.m" or "a1_unused.m" or "c3_script.m" or "c1_cyc.m")
            {
                File.WriteAllText(Path.Combine(_folder, name), c.GetProperty("text").GetString()!);
            }
        }

        _session = Assert.IsType<JgsReplSession>(((IScriptRepl)new MatlabScriptEngine()).CreateSession(new ScriptContext(_output, (_, _) => { }, _folder)));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _session.DisposeAsync();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // swept later
        }
    }

    private string File_(string name) => Path.Combine(_folder, name);

    private async Task<ScriptRunResult> Run(string code) => await _session.ExecuteAsync(code, sourceId: "", CancellationToken.None);

    private async Task Ok(string code)
    {
        ScriptRunResult result = await Run(code);
        Assert.True(result.Success, code + ": " + result.Message + _output.ErrorText);
    }

    private async Task<string> Printed(string code)
    {
        _output.Mark();
        await Ok(code);
        return _output.TextSinceMark.TrimEnd('\n');
    }

    private async Task<(string Identifier, string Message)> Refused(string code)
    {
        ScriptRunResult result = await Run(code);
        Assert.False(result.Success, code + " was accepted");
        return (result.ErrorIdentifier ?? string.Empty, result.Message ?? string.Empty);
    }

    // --- checkcode's display ------------------------------------------------------------------------------------

    [Fact]
    public async Task Display_PrintsR2025bsLines()
    {
        Assert.Equal("L 14 (C 10-15): Function might be unused.", await Printed("checkcode('a7_cyc')"));
        Assert.Equal("L 14 (C 10-15): DEFNU: Function might be unused.", await Printed("checkcode a7_cyc -id"));
        Assert.Equal("L 14 (C 10-15): Function might be unused.", await Printed("checkcode('a7_cyc.m')"));
        Assert.Equal("L 14 (C 10-15): Function might be unused.", await Printed("mlint('a7_cyc')"));
        Assert.Equal(string.Empty, await Printed("checkcode('a8_clean')"));
        Assert.Equal("L 14 (C 10-15): Function might be unused.", await Printed("checkcode('a7_cyc', '-disp')"));
        Assert.Equal("L 14 (C 10-15): Function might be unused.", await Printed("checkcode('a7_cyc', '-config=factory')"));
        Assert.Equal("L 14 (C 10-15): Function might be unused.", await Printed("checkcode('a7_cyc', '-string', '-disp')"));
    }

    [Fact]
    public async Task Display_ListsSeveralFilesUnderHeaders()
    {
        string expected = $"========== {File_("a7_cyc.m")} ==========\nL 14 (C 10-15): Function might be unused.\n========== {File_("a8_clean.m")} ==========";
        Assert.Equal(expected, await Printed("checkcode({'a7_cyc', 'a8_clean'})"));
        Assert.Equal(expected, await Printed("checkcode('a7_cyc', 'a8_clean')"));
        Assert.Equal(expected.Replace("L 14 (C 10-15): ", "L 14 (C 10-15): DEFNU: "), await Printed("checkcode({'a7_cyc', 'a8_clean'}, '-id')"));
    }

    [Fact]
    public async Task Display_CycAndModcycAddTheComplexityLinesInR2025bsOrder()
    {
        Assert.Equal("L 1 (C 14-19): The McCabe cyclomatic complexity of 'a7_cyc' is 4.\nL 14 (C 10-15): The McCabe cyclomatic complexity of 'helper' is 1.\nL 14 (C 10-15): Function might be unused.",
            await Printed("checkcode('a7_cyc', '-cyc')"));
        Assert.Equal("L 1 (C 14-19): The modified cyclomatic complexity of 'a7_cyc' is 4.\nL 14 (C 10-15): Function might be unused.\nL 14 (C 10-15): The modified cyclomatic complexity of 'helper' is 1.",
            await Printed("checkcode('a7_cyc', '-modcyc')"));
        Assert.Equal("L 1 (C 14-19): CABE: The McCabe cyclomatic complexity of 'c1_cyc' is 10.\nL 1 (C 14-19): MCABE: The modified cyclomatic complexity of 'c1_cyc' is 9.\nL 29 (C 10-16): CABE: The McCabe cyclomatic complexity of 'helper2' is 2.\nL 29 (C 10-16): DEFNU: Function might be unused.\nL 29 (C 10-16): MCABE: The modified cyclomatic complexity of 'helper2' is 2.",
            await Printed("checkcode('c1_cyc', '-cyc', '-modcyc', '-id')"));
        Assert.Equal("L 1 (C 1): SCABE: The McCabe cyclomatic complexity is 1.\nL 1 (C 3): NOPTS: Add a semicolon after the statement to hide the output (in a script).\nL 2 (C 5): NBRAK2: Use of brackets [] is unnecessary.",
            await Printed("checkcode('c3_script', '-cyc', '-id')"));
    }

    [Fact]
    public async Task Display_NotokListsWhatTheOkCommentsSuppressed()
    {
        Assert.Equal(string.Join("\n",
            "L 1 (C 22): INUSD: Input argument might be unused. Consider replacing the argument with ~ instead.",
            "L 1 (C 25): INUSD: Input argument might be unused. Consider replacing the argument with ~ instead.",
            "L 3 (C 1): NASGU: Value assigned to variable might be unused.",
            "L 4 (C 1): NASGU: Value assigned to variable might be unused.",
            "L 4 (C 5-12): CHARTEN: For readability, consider using 'newline' instead of 'char(10)'.",
            "L 5 (C 1): NASGU: Value assigned to variable might be unused.",
            "L 5 (C 5-12): CHARTEN: For readability, consider using 'newline' instead of 'char(10)'.",
            "L 6 (C 1): NASGU: Value assigned to variable might be unused.",
            "L 7 (C 1): NASGU: Value assigned to variable might be unused."), await Printed("checkcode('a5_suppress', '-notok', '-id')"));
    }

    [Fact]
    public async Task Display_TextFormAnalyzesTheTextGiven()
    {
        Assert.Equal("L 2 (C 1): NASGU: Value assigned to variable might be unused.", await Printed("checkcode('-text', sprintf('function t()\\nx = 1;\\nend\\n'), 't.m', '-id')"));
        Assert.Equal("L 2 (C 1): Value assigned to variable might be unused.", await Printed("checkcode(sprintf('function t()\\nx = 1;\\nend\\n'), '-text', 't.m')"));
        Assert.Equal("L 0 (C 0): Unable to run code analysis. 'anything.txt' has an invalid file extension.", await Printed("checkcode('-text', sprintf('function t()\\nx = 1;\\nend\\n'), 'anything.txt')"));
        Assert.Equal("L 0 (C 0): Unable to run code analysis. 'a7_cyc' has an invalid file extension.", await Printed("checkcode('a7_cyc', '-fullpath')"));
        Assert.Equal("L 14 (C 10-15): Function might be unused.", await Printed("checkcode('a7_cyc.m', '-fullpath')"));
        Assert.Equal("L 0 (C 0): Option '-bogus' is ignored because it is invalid.\nL 14 (C 10-15): Function might be unused.", await Printed("checkcode('a7_cyc', '-bogus')"));
    }

    // --- the outputs --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Outputs_StringStructAndFiles()
    {
        Assert.Equal("char [1 42]", await Printed("s = checkcode('a7_cyc', '-string'); fprintf('%s %s\\n', class(s), mat2str(size(s)))"));
        Assert.Equal("L 14 (C 10-15): DEFNU: Function might be unused.", await Printed("fprintf('%s', checkcode('a7_cyc', '-string', '-id'))"));
        Assert.Equal("message,fix,line,column", await Printed("m = checkcode('c3_script'); disp(strjoin(fieldnames(m)', ','))"));
        Assert.Equal("id,message,fix,line,column", await Printed("m = checkcode('c3_script', '-id'); disp(strjoin(fieldnames(m)', ','))"));
        Assert.Equal("id,message,fix,severity,line,column", await Printed("m = checkcode('c3_script', '-id', '-severity'); disp(strjoin(fieldnames(m)', ','))"));
        Assert.Equal("char double 1 double [3 3] double 1 0", await Printed("m = checkcode('c3_script', '-severity'); fprintf('%s %s %s %s %s %s %d %d\\n', class(m(1).message), class(m(1).line), mat2str(m(1).line), class(m(1).column), mat2str(m(1).column), class(m(1).fix), m(1).fix, m(1).severity)"));
        Assert.Equal("struct [0 0] message,line,column,fix", await Printed("m = checkcode('a8_clean'); fprintf('%s %s %s\\n', class(m), mat2str(size(m)), strjoin(fieldnames(m)', ','))"));
        Assert.Equal("struct [0 0] message,line,column,fix,id", await Printed("m = checkcode('a8_clean', '-id'); fprintf('%s %s %s\\n', class(m), mat2str(size(m)), strjoin(fieldnames(m)', ','))"));
        Assert.Equal("cell [2 1] struct [1 1] struct [0 0]", await Printed("m = checkcode({'a7_cyc', 'a8_clean'}); fprintf('%s %s %s %s %s %s\\n', class(m), mat2str(size(m)), class(m{1}), mat2str(size(m{1})), class(m{2}), mat2str(size(m{2})))"));
        Assert.Equal("cell [1 1]", await Printed("m = checkcode({'a7_cyc'}); fprintf('%s %s\\n', class(m), mat2str(size(m)))"));
        Assert.Equal("cell [2 1]", await Printed("m = checkcode([\"c1_cyc\", \"a8_clean\"]); fprintf('%s %s\\n', class(m), mat2str(size(m)))"));
        Assert.Equal($"struct [1 1] cell [1 1] {File_("a7_cyc.m")}", await Printed("[m, f] = checkcode('a7_cyc'); fprintf('%s %s %s %s %s\\n', class(m), mat2str(size(m)), class(f), mat2str(size(f)), f{1})"));
        Assert.Equal($"cell [2 1] {File_("a7_cyc.m")};{File_("a8_clean.m")}", await Printed("[m, f] = checkcode({'a7_cyc', 'a8_clean'}); fprintf('%s %s %s\\n', class(f), mat2str(size(f)), strjoin(f', ';'))"));
        Assert.Equal("CABE 1 DEFNU 29", await Printed("m = checkcode('c1_cyc', '-cyc', '-id'); fprintf('%s %d %s %d\\n', m(1).id, m(1).line, m(3).id, m(3).line)"));
        Assert.Equal("9", await Printed("m = checkcode('a5_suppress', '-notok'); disp(numel(m))"));
        Assert.Equal("2", await Printed("[m, f] = checkcode('-text', sprintf('function t()\\nx = 1;\\nend\\n'), 't.m'); disp(numel(f))"));
    }

    // --- the refusals -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Refusals_AreR2025bs()
    {
        Assert.Equal(("MATLAB:narginchk:notEnoughInputs", "Not enough input arguments."), await Refused("checkcode()"));
        Assert.Equal(("MATLAB:narginchk:notEnoughInputs", "Not enough input arguments."), await Refused("mlint()"));
        Assert.Equal(("MATLAB:mlint:FileNotFound", "Unable to open file \"nosuch_w10\". File is not found or is not a supported MATLAB code file."), await Refused("checkcode('nosuch_w10')"));
        Assert.Equal(("MATLAB:mlint:FileNotFound", "Unable to open file \"a2_patterns.txt\". File is not found or is not a supported MATLAB code file."), await Refused("checkcode('a2_patterns.txt')"));
        Assert.Equal("MATLAB:mlint:CheckCodeInputMustBeOfCharType", (await Refused("checkcode(5)")).Identifier);
        Assert.Equal("MATLAB:mlint:CheckCodeInputMustBeOfCharType", (await Refused("checkcode({'a7_cyc', 5})")).Identifier);
        Assert.Equal(("MATLAB:mlint:EmptyInput", "Empty arguments not supported."), await Refused("checkcode('')"));
        Assert.Equal(("MATLAB:mlint:EmptyInput", "Empty arguments not supported."), await Refused("checkcode('a7_cyc', '')"));
        Assert.Equal(("MATLAB:mlint:NoFile", "No file name provided."), await Refused("checkcode('-id')"));
        Assert.Equal("MATLAB:mlint:TooManyCellArgs", (await Refused("checkcode({'a7_cyc'}, {'a8_clean'})")).Identifier);
        Assert.Equal(("MATLAB:mlint:NestedCell", "Input arguments to checkcode should not contain nested cell arrays."), await Refused("checkcode({{'a7_cyc'}})"));
        Assert.Equal(("MATLAB:mlint:InvalidInput", "-TEXT requires one argument with text, and a second one with a .m filename"), await Refused("checkcode('a7_cyc', '-text')"));
        Assert.Equal(("MATLAB:nargoutchk:tooManyOutputs", "Too many output arguments."), await Refused("[a, b, c] = checkcode('a7_cyc')"));
        Assert.Equal("Too many output arguments.", (await Refused("x = checkcode('a7_cyc', '-disp')")).Message);
        Assert.Equal(("MATLAB:mlint:NotCharacterRowVector", "Character vector arguments must be row vectors."), await Refused("checkcode(['ab'; 'cd'])"));
        Assert.True((await Run("checkcode({})")).Success);
    }

    // --- codeIssues ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task CodeIssues_HoldsTheTablesR2025bHolds()
    {
        Assert.Equal("codeIssues", await Printed("c = codeIssues('a7_cyc.m'); disp(class(c))"));
        Assert.Equal($"string [1 1] {File_("a7_cyc.m")}", await Printed("fprintf('%s %s %s\\n', class(c.Files), mat2str(size(c.Files)), c.Files)"));
        Assert.Equal("Location,Severity,Fixability,Description,CheckID,LineStart,LineEnd,ColumnStart,ColumnEnd,FullFilename", await Printed("disp(strjoin(c.Issues.Properties.VariableNames, ','))"));
        Assert.Equal("1 a7_cyc.m warning manual Function might be unused. DEFNU 14 14 10 15", await Printed("t = c.Issues; fprintf('%d %s %s %s %s %s %d %d %d %d\\n', height(t), t.Location(1), string(t.Severity(1)), string(t.Fixability(1)), t.Description(1), string(t.CheckID(1)), t.LineStart(1), t.LineEnd(1), t.ColumnStart(1), t.ColumnEnd(1))"));
        Assert.Equal("Location,Severity,Fixability,Description,Suppression,CheckID,LineStart,LineEnd,ColumnStart,ColumnEnd,FullFilename 0", await Printed("fprintf('%s %d\\n', strjoin(c.SuppressedIssues.Properties.VariableNames, ','), height(c.SuppressedIssues))"));
        Assert.Equal("datetime string active", await Printed("fprintf('%s %s %s\\n', class(c.Date), class(c.Release), c.CodeAnalyzerConfiguration)"));
        Assert.Equal("5 8 line INUSD 1 22 file NASGU 3", await Printed("c5 = codeIssues('a5_suppress'); s = c5.SuppressedIssues; fprintf('%d %d %s %s %d %d %s %s %d\\n', height(c5.Issues), height(s), string(s.Suppression(1)), string(s.CheckID(1)), s.LineStart(1), s.ColumnStart(1), string(s.Suppression(3)), string(s.CheckID(3)), s.LineStart(3))"));
        string display = await Printed("disp(c)");
        Assert.Contains("codeIssues with properties:", display);
        Assert.Contains("Issues: [1×10 table]", display);
        Assert.Contains("SuppressedIssues: [0×11 table]", display);
        Assert.Contains("Issues table preview", display);
        Assert.Contains("\"a7_cyc.m\"", display);
        Assert.Contains("DEFNU", display);
        Assert.DoesNotContain("Issues table preview", await Printed("disp(codeIssues('a8_clean.m'))"));
    }

    [Fact]
    public async Task CodeIssues_WalksFoldersAndListsFilesInR2025bsOrders()
    {
        Directory.CreateDirectory(File_("sub"));
        File.Copy(File_("a7_cyc.m"), File_(Path.Combine("sub", "c4_sub.m")));
        Assert.Equal("8 7", await Printed("cf = codeIssues(pwd); c0 = codeIssues(); fprintf('%d %d\\n', numel(cf.Files), numel(codeIssues(pwd, 'IncludeSubfolders', false).Files))"));
        Assert.Equal("8", await Printed("disp(numel(c0.Files))"));
        Assert.Equal("[8 1] string", await Printed("fprintf('%s %s\\n', mat2str(size(cf.Files)), class(cf.Files))"));
        // The files case-blind by name, the issues by name in ordinal order.
        string listed = await Printed("disp(strjoin(cf.Files', ';'))");
        Assert.Equal("a1_unused.m,a2_patterns.m,a5_suppress.m,a7_cyc.m,a8_clean.m,c1_cyc.m,c3_script.m,c4_sub.m", string.Join(",", listed.Split(';').Select(Path.GetFileName)));
        Assert.Equal("a1_unused.m", await Printed("disp(cf.Issues.Location(1))"));
        Assert.Contains("Files: [8×1 string]", await Printed("disp(cf)"));
        Assert.Equal($"2 {File_("a7_cyc.m")};{File_("a8_clean.m")}", await Printed("cm = codeIssues({'a7_cyc.m', 'a8_clean.m'}); fprintf('%d %s\\n', numel(cm.Files), strjoin(cm.Files', ';'))"));
    }

    [Fact]
    public async Task CodeIssues_RefusesAsR2025bDoes()
    {
        Assert.Equal(("codeanalysis:reports:ccrAnalysis:FileNotFound", "Unable to open file 'nosuch_w10.m'. File is not found or is not a supported MATLAB code file."), await Refused("codeIssues('nosuch_w10.m')"));
        Assert.Equal("codeanalysis:reports:ccrAnalysis:InvalidFile", (await Refused("codeIssues('a2_patterns.txt')")).Identifier);
        Assert.Equal(("MATLAB:validators:mustBeNonzeroLengthText", "Invalid argument at position 1. Value must be a character vector, string array, or cell array of character vectors."), await Refused("codeIssues(5)"));
        Assert.Equal(("MATLAB:validators:mustBeNonzeroLengthText", "Invalid argument at position 1. Value must be text with one or more characters."), await Refused("codeIssues('')"));
        Assert.Equal(("MATLAB:TooManyInputs", "Invalid argument at position 2. Function requires 0 to 1 positional input(s)."), await Refused("codeIssues('a7_cyc.m', 'a8_clean.m')"));
        Assert.Equal("MATLAB:codeanalyzer:ExportNotAvailable", (await Refused("c = codeIssues('a7_cyc.m'); export(c, 'x.sarif')")).Identifier);
    }

    [Fact]
    public async Task CodeIssues_FixRewritesTheFilesAndReportsEachFix()
    {
        Directory.CreateDirectory(File_("fix"));
        File.Copy(File_("a2_patterns.m"), File_(Path.Combine("fix", "a2_patterns.m")));
        File.Copy(File_("a1_unused.m"), File_(Path.Combine("fix", "a1_unused.m")));
        // 64 issues where R2025b counts 67: its SEPEX and MDUPC are not JGraph's checks.
        Assert.Equal("64 62 2 1 1 Success,ErrorMessage,FullFilename,CheckID,LineStart,LineEnd,ColumnStart,ColumnEnd,ErrorID",
            await Printed("cfx = codeIssues(fullfile(pwd, 'fix', 'a2_patterns.m')); before = height(cfx.Issues); [cfx, status] = fix(cfx, 'CHARTEN'); fprintf('%d %d %d %d %d %s\\n', before, height(cfx.Issues), height(status), status.Success(1), status.Success(2), strjoin(status.Properties.VariableNames, ','))"));
        string[] lines = File.ReadAllLines(File_(Path.Combine("fix", "a2_patterns.m")));
        Assert.Equal("c1 = newline;", lines[2]);
        Assert.Equal("c2 = ['a' newline 'b'];", lines[3]);
        await Ok("cfx = fix(cfx, [\"NBRAK2\", \"LOGL\", \"STRQUOT\", \"RPMT1\", \"RPMTT\", \"STCI\", \"FVAL\", \"ISCL\", \"ISMAT\", \"DSPSP\", \"STLOW\"]);");
        lines = File.ReadAllLines(File_(Path.Combine("fix", "a2_patterns.m")));
        Assert.Equal("f1 = sin(1);", lines[5]);
        Assert.Equal("f2 = sin(1);", lines[6]);
        Assert.Equal("i1 = isscalar(v);", lines[10]);
        Assert.Equal("i2 = isscalar(v);", lines[11]);
        Assert.Equal("s1 = \"abc\";", lines[29]);
        Assert.Equal("m1 = ismatrix(v);", lines[33]);
        Assert.Equal("l1 = true;", lines[35]);
        Assert.Equal("l2 = false;", lines[36]);
        Assert.Equal("r1 = ones(2, 3);", lines[37]);
        Assert.Equal("r2 = true(2, 3);", lines[38]);
        Assert.Equal("t1 = strcmpi(s, 'ABC');", lines[41]); // JGraph unwraps both sides; R2025b's fix left upper('ABC') (divergence)
        Assert.Equal("t3 = strcmpi(s, 'ABC');", lines[43]);
        Assert.Equal("k1 = 1;", lines[44]);
        Assert.Equal("k2 = x;", lines[45]);
        Assert.Equal("k5 = x;", lines[48]); // the trailing comma goes with the brackets (R2025b left 'x,;')
        Assert.Equal("disp(sprintf('%d', x))", lines[81].Replace("fprintf('%d\\n', x)", "disp(sprintf('%d', x))")); // placeholder equality on the original when unfixed
        Assert.Equal("fprintf('%d\\n', x)", lines[81]);
        await Ok("cfa = codeIssues(fullfile(pwd, 'fix', 'a1_unused.m')); cfa = fix(cfa, [\"INUSD\", \"ASGLU\", \"NUSED\", \"MSNU\"]);");
        lines = File.ReadAllLines(File_(Path.Combine("fix", "a1_unused.m")));
        Assert.Equal("function [out1, out2, out3] = a1_unused(a, ~, c, d)", lines[0]);
        Assert.Equal("[~, q] = size(a);", lines[3]);
        Assert.Equal("w = 1; ", lines[12]);
        Assert.Equal("global G2", lines[25]); // the unused global's line went, as R2025b's fix takes it
        Assert.Equal("disp(v)", lines[31]);
    }
}
