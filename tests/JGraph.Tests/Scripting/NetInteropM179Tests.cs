using System.Diagnostics;
using System.Runtime.CompilerServices;
using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Jgs.Net;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Stage 6 of the .NET and shared-library interop plan (ADR 0179): <c>jgraph.net.compile</c>, the
/// inline C# helper. The script-level answers are the JGraph-only jgnet_compile fixture's; these tests
/// pin what it cannot see: the image cache across sessions, one session's recompilation leaving
/// another's objects alone, and a replaced build's context unloading.
/// </summary>
[Collection("JG facade")]
public class NetInteropM179Tests : IDisposable
{
    private RecordingScriptOutput _output = new();

    public NetInteropM179Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private string Run(string code)
    {
        _output = new RecordingScriptOutput();
        var context = new ScriptContext(_output, (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Trim().ReplaceLineEndings("\n");
    }

    private static NetCompileRequest Request(string source, string name) =>
        new([("<string>", source)], name, [], Unloadable: true, AllowUnsafe: false, LanguageVersion.Latest, Optimize: true);

    [Fact]
    public void TheSameBuildInASecondSessionLoadsTheCachedImageInsteadOfCompiling()
    {
        string source = "namespace M179Cache { public static class C { public static int V() { return 3; } } }";
        var first = new NetCatalog();
        NetCompiler.Compile(first, Request(source, "M179Cache"));

        var second = new NetCatalog();
        var clock = Stopwatch.StartNew();
        (NetCompiledAssembly compiled, _, bool fresh) = NetCompiler.Compile(second, Request(source, "M179Cache"));
        clock.Stop();

        Assert.True(fresh);
        Assert.NotSame(first.Compiled["M179Cache"].Assembly, compiled.Assembly); // each session its own copy
        Assert.Equal(first.Compiled["M179Cache"].Hash, compiled.Hash);
        Assert.True(clock.ElapsedMilliseconds < 200, $"a cached build took {clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void OneSessionsRecompilationLeavesAnotherSessionsObjectsAlone()
    {
        string v1 = "namespace M179Iso { public class B { public int Get() { return 1; } } }";
        string v2 = "namespace M179Iso { public class B { public int Get() { return 2; } } }";
        var a = new NetCatalog();
        var b = new NetCatalog();
        NetCompiler.Compile(a, Request(v1, "M179Iso"));
        NetCompiler.Compile(b, Request(v1, "M179Iso"));
        object heldByA = Activator.CreateInstance(a.TypeNamed("M179Iso.B")!)!;
        var wrapped = new NetObject(heldByA, heldByA.GetType());

        NetCompiler.Compile(b, Request(v2, "M179Iso"));

        Assert.Same(wrapped, wrapped.Live(0, 0));
        Assert.False(NetCompiler.IsRetired(a.TypeNamed("M179Iso.B")!, out _));
        Assert.NotEqual(a.TypeNamed("M179Iso.B"), b.TypeNamed("M179Iso.B"));
    }

    [Fact]
    public void AReplacedBuildWhoseObjectsAreGoneUnloads()
    {
        var catalog = new NetCatalog();
        WeakReference context = CompileUseAndLetGo(catalog);
        NetCompiler.Compile(catalog, Request("namespace M179Unload { public static class S { public static int V() { return 2; } } }", "M179Unload"));

        for (int i = 0; i < 20 && context.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(context.IsAlive, "the replaced build's load context is still alive");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CompileUseAndLetGo(NetCatalog catalog)
    {
        (NetCompiledAssembly compiled, _, _) = NetCompiler.Compile(
            catalog, Request("namespace M179Unload { public static class S { public static int V() { return 1; } } }", "M179Unload"));

        // A call fills the process-wide member caches, which the retirement must empty.
        JgsValue answer = NetInvoke.StaticMember(catalog.TypeNamed("M179Unload.S")!, "V", autoCall: true, 0, 0, catalog);
        Assert.Equal(1, answer.AsNumber);
        return new WeakReference(compiled.Context);
    }

    [Fact]
    public void AnObjectOfAReplacedBuildRefusesConversionsAndArgumentsToo()
    {
        Assert.Equal("JGraph:NET:AssemblyRecompiled|JGraph:NET:AssemblyRecompiled|JGraph:NET:AssemblyRecompiled|1", Run(
            "jgraph.net.compile(\"namespace M179Old { public class T { public override string ToString() { return \"\"t\"\"; } public static int Take(T t) { return 1; } } }\", AssemblyName=\"M179Old\");\n"
            + "old = M179Old.T();\n"
            + "jgraph.net.compile(\"namespace M179Old { public class T { public static int Take(T t) { return 1; } } }\", AssemblyName=\"M179Old\");\n"
            + "try, char(old); catch e, fprintf('%s|', e.identifier); end\n"
            + "try, M179Old.T.Take(old); catch e, fprintf('%s|', e.identifier); end\n"
            + "try, old.ToString(); catch e, fprintf('%s|', e.identifier); end\n"
            + "fprintf('%d', M179Old.T.Take(M179Old.T()));"));
    }

    [Fact]
    public void TheSameBuildAgainInASessionWarnsOnlyOnce()
    {
        string output = Run(
            "src = \"namespace M179Warn { public class X { public int Y() { int unused; return 1; } } }\";\n"
            + "jgraph.net.compile(src); jgraph.net.compile(src); fprintf('done');") + _output.ErrorText;
        Assert.Equal(1, output.Split("CS0168").Length - 1);
    }

    [Fact]
    public void AssemblyNameMustBeAName()
    {
        Assert.Equal("JGraph:NET:compile:InvalidOption", Run(
            "try, jgraph.net.compile(\"namespace Q { }\", AssemblyName=\"1bad name\"); catch e, fprintf('%s', e.identifier); end"));
    }
}
