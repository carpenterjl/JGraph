using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Completion;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Jgs.Completion;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The packages and class folders plan, stage P5 (ADR 0311): what the editor's completion offers
/// for a session's own <c>+package</c> and <c>@Class</c> folders - a package's members after
/// <c>pkg.</c>, a class's statics after <c>pkg.C.</c> or <c>Thing.</c>, an instance's members after
/// <c>obj.</c> in a class file and in a method file - and the files <c>edit</c>'s names stand for.
/// R2025b has no headless completion to measure against; these pin JGraph's own reading, which
/// runs none of the script's code.
/// </summary>
[Collection("JG facade")]
public sealed class PackageCompletionTests : IAsyncLifetime
{
    private readonly RecordingScriptOutput _output = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jgraph-p5-completion-" + Guid.NewGuid().ToString("N"));
    private string _library = null!;
    private IScriptSession _session = null!;

    public async Task InitializeAsync()
    {
        JG.Reset();
        _library = Path.Combine(_folder, "lib");
        Directory.CreateDirectory(_folder);
        Directory.CreateDirectory(Path.Combine(_library, "+p5pkg", "+sub"));
        Directory.CreateDirectory(Path.Combine(_library, "+p5pkg", "@D"));
        Directory.CreateDirectory(Path.Combine(_library, "@P5Thing", "private"));
        File.WriteAllText(Path.Combine(_library, "+p5pkg", "f.m"), "function r = f(x)\nr = x * 10;\nend\n");
        File.WriteAllText(Path.Combine(_library, "+p5pkg", "s.m"), "sval = 42;\n");
        File.WriteAllText(Path.Combine(_library, "+p5pkg", "C.m"),
            "classdef C\n    properties (Constant)\n        K = 7\n    end\n    properties\n        P = 1\n    end\n    methods (Static)\n        function r = stat(x)\n            r = x;\n        end\n    end\n    methods\n        function r = inst(o)\n            r = o.P;\n        end\n    end\nend\n");
        File.WriteAllText(Path.Combine(_library, "+p5pkg", "E.m"), "classdef E\n    enumeration\n        A, B\n    end\nend\n");
        File.WriteAllText(Path.Combine(_library, "+p5pkg", "+sub", "h.m"), "function r = h(x)\nr = x - 1;\nend\n");
        File.WriteAllText(Path.Combine(_library, "+p5pkg", "@D", "D.m"), "classdef D\n    methods\n        r = dm(o)\n    end\nend\n");
        File.WriteAllText(Path.Combine(_library, "+p5pkg", "@D", "dm.m"), "function r = dm(o)\nr = o;\nend\n");
        File.WriteAllText(Path.Combine(_library, "@P5Thing", "P5Thing.m"),
            "classdef P5Thing\n    properties\n        W = 1\n    end\n    methods\n        function o = P5Thing(w)\n            if nargin > 0, o.W = w; end\n        end\n        r = extent(o)\n        function r = inline1(o)\n            r = o.W;\n        end\n    end\n    methods (Static)\n        r = staticOne(x)\n    end\n    methods (Access = private)\n        r = secret(o)\n    end\nend\n");
        File.WriteAllText(Path.Combine(_library, "@P5Thing", "extent.m"), "function r = extent(o)\nr = o.W * 2;\nend\n");
        File.WriteAllText(Path.Combine(_library, "@P5Thing", "scale.m"), "function r = scale(o, k)\nr = o.W * k;\nend\n");
        File.WriteAllText(Path.Combine(_library, "@P5Thing", "staticOne.m"), "function r = staticOne(x)\nr = x;\nend\n");
        File.WriteAllText(Path.Combine(_library, "@P5Thing", "secret.m"), "function r = secret(o)\nr = o.W;\nend\n");
        File.WriteAllText(Path.Combine(_library, "@P5Thing", "private", "p5hidden.m"), "function r = p5hidden(o)\nr = o.W;\nend\n");
        File.WriteAllText(Path.Combine(_folder, "good.m"), "function r = good(x)\nr = x;\nend\n");

        _session = ((IScriptRepl)new MatlabScriptEngine()).CreateSession(new ScriptContext(_output, static (_, _) => { }, _folder));
        ScriptRunResult added = await _session.ExecuteAsync($"addpath('{_library.Replace("\\", "\\\\")}');", "", CancellationToken.None);
        Assert.True(added.Success, added.Message + _output.ErrorText);
    }

    public async Task DisposeAsync()
    {
        await _session.DisposeAsync();
        JG.Reset();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // swept later
        }
    }

    private IScriptCompletionSource Live => Assert.IsAssignableFrom<IScriptCompletionSource>(_session);

    private static string[] Names(IEnumerable<CompletionItem> items) => [.. items.Select(static i => i.Text)];

    /// <summary>After <c>pkg.</c>: the package's functions, scripts, classes, class folders and sub-packages, each by its kind.</summary>
    [Fact]
    public void APackagesMembersFollowItsDot()
    {
        JgsCompletionResult result = JgsCompletionEngine.GetCompletions("x = p5pkg.", 10, matlab: true, live: Live);
        Assert.Equal(10, result.ReplaceStart);
        var kinds = result.Items.ToDictionary(static i => i.Text, static i => i.Kind);
        Assert.Equal(CompletionItemKind.Function, kinds["f"]);
        Assert.Equal(CompletionItemKind.Function, kinds["s"]);
        Assert.Equal(CompletionItemKind.Type, kinds["C"]);
        Assert.Equal(CompletionItemKind.Type, kinds["E"]);
        Assert.Equal(CompletionItemKind.Type, kinds["D"]);
        Assert.Equal(CompletionItemKind.Namespace, kinds["sub"]);
        Assert.Equal("script p5pkg.s", result.Items.Single(static i => i.Text == "s").Description);

        JgsCompletionResult filtered = JgsCompletionEngine.GetCompletions("p5pkg.su", 8, matlab: true, live: Live);
        Assert.Equal(["sub"], Names(filtered.Items));
        Assert.Equal(["h"], Names(JgsCompletionEngine.GetCompletions("p5pkg.sub.", 10, matlab: true, live: Live).Items));
        Assert.Empty(JgsCompletionEngine.GetCompletions("nosuchpkg_p5.", 13, matlab: true, live: Live).Items);
    }

    /// <summary>After a class's name: its statics, constants and enumeration members, read from the file before the class is loaded and from the class after.</summary>
    [Fact]
    public async Task AClassesStaticsFollowItsName()
    {
        Assert.Equal(["K", "stat"], Names(JgsCompletionEngine.GetCompletions("p5pkg.C.", 8, matlab: true, live: Live).Items));
        Assert.Equal(["A", "B"], Names(JgsCompletionEngine.GetCompletions("p5pkg.E.", 8, matlab: true, live: Live).Items));
        Assert.Equal(["staticOne"], Names(JgsCompletionEngine.GetCompletions("P5Thing.", 8, matlab: true, live: Live).Items));

        ScriptRunResult loaded = await _session.ExecuteAsync("c = p5pkg.C(); t = P5Thing(2);", "", CancellationToken.None);
        Assert.True(loaded.Success, loaded.Message + _output.ErrorText);
        Assert.Equal(["K", "stat"], Names(JgsCompletionEngine.GetCompletions("p5pkg.C.", 8, matlab: true, live: Live).Items));
        CompletionItem staticOne = Assert.Single(JgsCompletionEngine.GetCompletions("P5Thing.", 8, matlab: true, live: Live).Items);
        Assert.Equal(("staticOne", "staticOne(x)", "static method of P5Thing"), (staticOne.Text, staticOne.Signature, staticOne.Description));
    }

    /// <summary>
    /// In a class file, <c>o.</c> is the class's own members from the buffer with the folder's method
    /// files beside them; in a method file of an <c>@</c> folder, the folder's class's members; private
    /// methods and <c>private/</c> functions are offered to neither.
    /// </summary>
    [Fact]
    public void AnInstancesMembersFollowItsDotInTheClassFileAndItsMethodFiles()
    {
        string classFile = File.ReadAllText(Path.Combine(_library, "@P5Thing", "P5Thing.m"));
        string code = classFile.Replace("            r = o.W;", "            r = o.");
        int offset = code.IndexOf("r = o.", StringComparison.Ordinal) + 6;
        string[] inClassFile = Names(JgsCompletionEngine.GetCompletions(code, offset, matlab: true, live: Live).Items);
        Assert.Contains("W", inClassFile);
        Assert.Contains("inline1", inClassFile);
        Assert.Contains("extent", inClassFile); // a signature line, bound to extent.m
        Assert.Contains("scale", inClassFile); // an undeclared method file
        Assert.DoesNotContain("p5hidden", inClassFile);

        const string methodFile = "function r = scale(o, k)\nr = o.";
        string path = Path.Combine(_library, "@P5Thing", "scale.m");
        string[] inMethodFile = Names(JgsCompletionEngine.GetCompletions(methodFile, methodFile.Length, matlab: true, live: Live, filePath: path).Items);
        Assert.Equal(["extent", "inline1", "scale", "staticOne", "W"], inMethodFile); // the private secret and p5hidden are not offered
        Assert.Empty(JgsCompletionEngine.GetCompletions(methodFile, methodFile.Length, matlab: true, live: Live).Items); // no file, no folder
        Assert.Empty(JgsCompletionEngine.GetCompletions("function r = scale(o, k)\nr = k.", 31, matlab: true, live: Live, filePath: path).Items); // not the object

        string[] inPackagedMethodFile = Names(JgsCompletionEngine.GetCompletions("function r = dm(o)\nr = o.", 25, matlab: true, live: Live, filePath: Path.Combine(_library, "+p5pkg", "@D", "dm.m")).Items);
        Assert.Equal(["dm"], inPackagedMethodFile);
    }

    /// <summary>The files <c>edit</c>'s forms stand for, as the Script Workspace asks the session: dotted, slash, relative, bare and plain names.</summary>
    [Fact]
    public void TheSessionNamesTheFileAnEditNameStandsFor()
    {
        var locator = Assert.IsAssignableFrom<IScriptCodeLocator>(_session);
        string thing = Path.Combine(_library, "@P5Thing");
        Assert.Equal(Path.Combine(_library, "+p5pkg", "f.m"), locator.LocateCodeFile("p5pkg.f"));
        Assert.Equal(Path.Combine(_library, "+p5pkg", "+sub", "h.m"), locator.LocateCodeFile("p5pkg.sub.h"));
        Assert.Equal(Path.Combine(_library, "+p5pkg", "C.m"), locator.LocateCodeFile("p5pkg.C"));
        Assert.Equal(Path.Combine(_library, "+p5pkg", "C.m"), locator.LocateCodeFile("p5pkg.C.stat"));
        Assert.Equal(Path.Combine(thing, "extent.m"), locator.LocateCodeFile("P5Thing/extent"));
        Assert.Equal(Path.Combine(thing, "extent.m"), locator.LocateCodeFile("P5Thing.extent"));
        Assert.Equal(Path.Combine(thing, "P5Thing.m"), locator.LocateCodeFile("P5Thing.inline1"));
        Assert.Equal(Path.Combine(thing, "P5Thing.m"), locator.LocateCodeFile("P5Thing"));
        Assert.Equal(Path.Combine(thing, "extent.m"), locator.LocateCodeFile("@P5Thing/extent"));
        Assert.Equal(Path.Combine(_library, "+p5pkg", "f.m"), locator.LocateCodeFile("+p5pkg/f"));
        Assert.Equal(Path.Combine(thing, "scale.m"), locator.LocateCodeFile("scale"));
        Assert.Equal(Path.Combine(_folder, "good.m"), locator.LocateCodeFile("good"));
        Assert.Null(locator.LocateCodeFile("nosuch_p5_name"));
        Assert.Null(locator.LocateCodeFile("P5Thing/nosuch"));
        Assert.Null(locator.LocateCodeFile("p5pkg/f"));
    }
}
