using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Stage 10 of the .NET and shared-library interop plan (ADR 0183): <c>methodsview</c> and
/// <c>libfunctionsview</c> reaching the host's table window (or printing without one), and the
/// Workspace pane's short summary of a value from outside the language. What R2025b does is held by
/// the <c>views_methods</c> fixture; these tests hold what only the host sees.
/// </summary>
[Collection("JG facade")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class InteropViewsM183Tests
{
    private static readonly string Interop = Path.Combine(AppContext.BaseDirectory, "MatlabParity", "fixtures", "interop");
    private static readonly string Library = Path.Combine(Interop, "jgtestlib.dll");

    [Fact]
    public void MethodsviewHandsItsTableToTheHostsWindow()
    {
        var viewer = new RecordingViewer();
        Run("methodsview System.Math", viewer);

        (string title, IReadOnlyList<string> headers, IReadOnlyList<string[]> rows) = Assert.Single(viewer.Tables);
        Assert.Equal("Methods for class System.Math", title);
        Assert.Equal(new[] { "Name", "Return Type", "Arguments", "Qualifiers", "Inherited From" }, headers);
        Assert.Equal(148, rows.Count);
        Assert.Equal(new[] { "Abs", "int16 scalar RetVal", "(int16 scalar value)", "Static", "" }, rows[0]);
    }

    [Fact]
    public void LibfunctionsviewShowsTheLibrarysFunctionsWithoutTheInheritanceColumn()
    {
        var viewer = new RecordingViewer();
        Run($"loadlibrary('{Library}', @jgtestlib_proto, 'alias', 'lfv'); libfunctionsview lfv; unloadlibrary lfv;", viewer);

        (string title, IReadOnlyList<string> headers, IReadOnlyList<string[]> rows) = Assert.Single(viewer.Tables);
        Assert.Equal("Functions in library lfv", title);
        Assert.Equal(new[] { "Name", "Return Type", "Arguments", "Qualifiers" }, headers);
        Assert.Contains(rows, r => r is ["jg_add_ref", "[double lhs1, doublePtr lhs2]", "(double scalar rhs1, doublePtr rhs2, double scalar rhs3)", "Static"]);
    }

    [Fact]
    public void WithoutAWindowTheTableIsPrinted()
    {
        // methodsview.m titles every lib. class a library, lib.pointer included.
        string text = Run("methodsview('lib.pointer')", viewer: null);
        Assert.Matches(@"^\s*Functions in library pointer\r?\n\r?\nName +Return Type +Arguments +Inherited From\r?\n-", text);
        Assert.Contains("isNull       logical scalar lhs1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryInheritedLineNamesItsClass()
    {
        // methodsview.m blanks the inheritance of the lines named like the first line R2025b happens
        // to list, which changes between runs (views_methods, ADR 0183).
        string text = Run("[h, d] = methodsview('lib.pointer', 'noUI'); fprintf('%s|', d(d(:, 1) == \"addlistener\", 4));", viewer: null);
        Assert.Equal("handle|handle|handle|handle|handle|", text);
    }

    [Fact]
    public void TheWorkspacePaneSumsUpExternalValuesWithoutRunningTheirCode()
    {
        string code =
            "jgraph.net.compile(\"namespace M183 { public class Probe { public static int Reads; public int P { get { Reads++; return 1; } } public override string ToString() { Reads++; return \"\"p\"\"; } } }\", AssemblyName=\"M183Probe\");\n"
            + "o = M183.Probe(); s = System.String('hi'); d = System.DayOfWeek.Monday; a = NET.createArray('System.Double', 3);\n"
            + "lp = libpointer('doublePtr', [1 2 3]); z = libpointer;\n";
        ScriptRunResult result = RunResult(code, viewer: null, out _);
        Dictionary<string, ScriptVariable> byName = result.Variables.ToDictionary(v => v.Name);

        Assert.Equal("1×1 M183.Probe", byName["o"].DisplayValue);
        Assert.Equal("1×1 System.String: hi", byName["s"].DisplayValue);
        Assert.Equal("1×1 System.DayOfWeek: Monday", byName["d"].DisplayValue);
        Assert.Equal("1×1 System.Double[]: 3 elements", byName["a"].DisplayValue);
        Assert.Equal("1×1 lib.pointer: doublePtr, 1×3", byName["lp"].DisplayValue);
        Assert.Equal("1×1 lib.pointer: NULL", byName["z"].DisplayValue);
        Assert.Equal("M183.Probe", byName["o"].Type);

        var raw = Assert.IsType<ScriptExternalValue>(byName["lp"].RawValue);
        Assert.Equal(("lib.pointer", "C library value"), (raw.ClassName, raw.Kind));
        Assert.Equal(".NET object", Assert.IsType<ScriptExternalValue>(byName["o"].RawValue).Kind);

        // Neither the getter nor ToString ran for the pane.
        Type probe = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("M183.Probe")).First(t => t is not null)!;
        Assert.Equal(0, (int)probe.GetField("Reads")!.GetValue(null)!);
    }

    [Fact]
    public void ADotAfterANetNameOffersWhatFollowsIt()
    {
        _ = JGraph.Scripting.Jgs.Net.NetCatalog.FrameworkChildren("System"); // build the index here, not on a pool thread

        JgsCompletionResultAssert("x = System.", ["IO", "Math", "String", "Text"], replaceStart: 11);
        JgsCompletionResultAssert("x = System.Math.Ma", ["Max", "MaxMagnitude"], replaceStart: 16);
        JgsCompletionResultAssert("d = System.DayOfWeek.Mo", ["Monday"], replaceStart: 21);
        JgsCompletionResultAssert("sb = System.Text.StringB", ["StringBuilder"], replaceStart: 17);

        // After anything that is not a .NET namespace or type, a dot offers nothing (not the builtins).
        Assert.Empty(JGraph.Scripting.Jgs.Completion.JgsCompletionEngine.GetCompletions("s.", 2, matlab: true).Items);
        Assert.Empty(JGraph.Scripting.Jgs.Completion.JgsCompletionEngine.GetCompletions("x = 1.", 6, matlab: true).Items);
    }

    private static void JgsCompletionResultAssert(string code, string[] expected, int replaceStart)
    {
        var result = JGraph.Scripting.Jgs.Completion.JgsCompletionEngine.GetCompletions(code, code.Length, matlab: true);
        Assert.Equal(replaceStart, result.ReplaceStart);
        string[] offered = [.. result.Items.Select(static i => i.Text)];
        Assert.All(expected, name => Assert.Contains(name, offered));
    }

    [Fact]
    public async Task CalllibStringsOfferTheSessionsLibrariesAndTheirFunctions()
    {
        var context = new ScriptContext(new RecordingScriptOutput(), (_, _) => { }, Interop, null);
        IScriptSession session = new JGraph.Scripting.Jgs.MatlabScriptEngine().CreateSession(context);
        try
        {
            ScriptRunResult loaded = await session.ExecuteAsync($"loadlibrary('{Library}', @jgtestlib_proto, 'alias', 'cmp');", "", default);
            Assert.True(loaded.Success, loaded.Message);
            var live = Assert.IsAssignableFrom<JGraph.Scripting.Completion.IScriptCompletionSource>(session);

            var libraries = JGraph.Scripting.Jgs.Completion.JgsCompletionEngine.GetCompletions("calllib('c", 10, matlab: true, live: live);
            Assert.Equal(9, libraries.ReplaceStart);
            Assert.Equal("cmp", Assert.Single(libraries.Items).Text);

            var functions = JGraph.Scripting.Jgs.Completion.JgsCompletionEngine.GetCompletions("r = calllib('cmp', 'jg_add", 26, matlab: true, live: live);
            JGraph.Scripting.Completion.CompletionItem add = Assert.Single(functions.Items);
            Assert.Equal(("jg_add_ref", "[double lhs1, doublePtr lhs2] jg_add_ref(double rhs1, doublePtr rhs2, double rhs3)"), (add.Text, add.Signature));
            Assert.Equal(20, functions.ReplaceStart);

            Assert.Empty(JGraph.Scripting.Jgs.Completion.JgsCompletionEngine.GetCompletions("calllib('", 9, matlab: true).Items); // no session, no library
            await session.ExecuteAsync("unloadlibrary cmp", "", default);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(@"C:\work dir\sub.m(4,12): Undefined function 'q'.", @"C:\work dir\sub.m", 4, 12)]
    [InlineData(@"D:\src\Helper.cs(12,5): error CS1002: ; expected", @"D:\src\Helper.cs", 12, 5)]
    [InlineData("C:/work/sub.m(40,1): message", "C:/work/sub.m", 40, 1)]
    [InlineData("--- Failed: (4,12): methods: a string has no methods to list. ---", null, 4, 12)]
    public void AConsoleLineNamesTheFileAndLineOfAnError(string text, string? path, int line, int column) =>
        Assert.Equal(new ScriptLocation(path, line, column), ScriptLocation.Find(text));

    [Fact]
    public void AConsoleLineWithoutALocationNamesNothing()
    {
        Assert.Null(ScriptLocation.Find("x = 3"));
        Assert.Null(ScriptLocation.Find("f(2, 3)"));
        Assert.Null(ScriptLocation.Find("f(2,3): not a location"));
        Assert.Null(ScriptLocation.Find("<string>(1,40): error CS1525: Invalid expression term '}'"));
    }

    private static string Run(string code, IScriptTableViewer? viewer)
    {
        ScriptRunResult result = RunResult(code, viewer, out RecordingScriptOutput output);
        Assert.True(result.Success, result.Message + output.ErrorText);
        return output.NormalText;
    }

    private static ScriptRunResult RunResult(string code, IScriptTableViewer? viewer, out RecordingScriptOutput output)
    {
        output = new RecordingScriptOutput();
        var context = new ScriptContext(output, (_, _) => { }, Interop, null) { TableViewer = viewer };
        ScriptRunResult result = JgsRunner.Run(code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + output.ErrorText);
        return result;
    }

    private sealed class RecordingViewer : IScriptTableViewer
    {
        public List<(string Title, IReadOnlyList<string> Headers, IReadOnlyList<string[]> Rows)> Tables { get; } = [];

        public void ShowTable(string title, IReadOnlyList<string> headers, IReadOnlyList<string[]> rows) =>
            Tables.Add((title, headers, rows));
    }
}
