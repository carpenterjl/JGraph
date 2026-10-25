using System.Numerics;
using System.Reflection;
using System.Text.RegularExpressions;
using JGraph.Api;
using JGraph.Deploy;
using JGraph.MATLAB.Exceptions;
using JGraph.MATLAB.Runtime;
using JGraph.MATLAB.Types;
using JGraph.Runtime;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.Runtime;

/// <summary>
/// An archive for the MATLAB Data API for .NET (compiler and SDK plan, stage K9), built by JGraph's mcc
/// from the probe's own sources (<c>tools/matlab-checklist/compiler-probes/src/k9</c>) and called as
/// <c>k9client</c> calls R2025b's: the strongly typed interface against R2025b's generated text
/// (<c>recordings/k9_out</c>), and the calls against R2025b's answers
/// (<c>recordings/k9_client.out.txt</c>). <c>run-k9.ps1</c> runs the whole client; these are its
/// landmarks, and the divergences ADR 0265 records where JGraph answers rightly.
/// </summary>
[Collection("JG facade")]
public sealed class DotNetDataApiTests : IDisposable
{
    private static readonly string Repo = FindRepo();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _root =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-k9-" + Guid.NewGuid().ToString("N"))).FullName;

    private readonly string? _cacheBefore = Environment.GetEnvironmentVariable("MCR_CACHE_ROOT");

    public DotNetDataApiTests()
    {
        JG.Reset();
        Environment.SetEnvironmentVariable("MCR_CACHE_ROOT", Path.Combine(_root, "cache"));
        string from = Path.Combine(Repo, "tools", "matlab-checklist", "compiler-probes", "src", "k9");
        foreach (string file in Directory.GetFiles(from, "*.m", SearchOption.AllDirectories))
        {
            string to = Path.Combine(Src, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(file, to);
        }
    }

    public void Dispose()
    {
        // The runtime ends with the test, not through TerminateApplication, which ends it for the process.
        JgRuntime.Terminate();
        Environment.SetEnvironmentVariable("MCR_CACHE_ROOT", _cacheBefore);
        JG.Reset();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An assembly the test process still holds; the temp folder clears it.
        }
    }

    private static string FindRepo()
    {
        string? folder = AppContext.BaseDirectory;
        while (folder is not null && !Directory.Exists(Path.Combine(folder, "tools", "matlab-checklist")))
        {
            folder = Path.GetDirectoryName(folder);
        }

        return folder ?? throw new DirectoryNotFoundException("The repository was not found above the test's folder.");
    }

    private string Src => Path.Combine(_root, "src");

    private static string Recordings => Path.Combine(Repo, "tools", "matlab-checklist", "compiler-probes", "recordings");

    /// <summary>Builds K9 as the probe's main build does (every file but the two variadic ones and k9opt, which its own build takes) into <c>dn\K9</c>, and answers its folder.</summary>
    private async Task<string> BuildK9()
    {
        string folder = Path.Combine(_root, "dn", "K9");
        IEnumerable<string> files = Directory.GetFiles(Src, "*.m", SearchOption.AllDirectories)
            .Where(static f => Path.GetFileNameWithoutExtension(f) is not ("k9nargs" or "k9nout" or "k9opt"))
            .Select(static f => $"'{f}'");
        string code = $"mcc('-W', 'dotnet:K9,api=matlab-data', '-T', 'link:lib', '-d', '{folder}', {string.Join(", ", files)});";
        await using IScriptSession session = Assert.IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
            .CreateSession(new ScriptContext(_output, static (_, _) => { }, Src));
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return folder;
    }

    /// <summary>A generated file without what differs by construction: the header's product and date, and MathWorks' namespaces.</summary>
    private static string Normalized(string text)
    {
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("MathWorks.MATLAB.", "JGraph.MATLAB.", StringComparison.Ordinal);
        text = Regex.Replace(text, @"\* (MATLAB|JGraph) Strongly Typed Interface Version: [^\n]*", "* <VERSION>");
        return Regex.Replace(text, @"C# source code generated on: [^\n]*", "C# source code generated on: <DATE>");
    }

    [Fact]
    public async Task TheInterfaceIsR2025bsWithItsTwoDefectsCorrected()
    {
        string folder = await BuildK9();
        string recorded = Path.Combine(Recordings, "k9_out", "strongly_typed_interface");
        string built = Path.Combine(folder, DotNetDataAssembly.InterfaceFolder);
        Assert.Equal(
            Directory.GetFiles(recorded).Select(Path.GetFileName).Order(StringComparer.Ordinal),
            Directory.GetFiles(built).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        foreach (string file in Directory.GetFiles(recorded))
        {
            string expected = Normalized(File.ReadAllText(file));
            if (Path.GetFileName(file) == "k9nv.cs")
            {
                // R2025b passed the name-value arguments by position, which k9nv refuses (k9client: typed.nv.2).
                expected = expected.Replace(",x,Scale,Label)", ",x,\"Scale\",Scale,\"Label\",Label)", StringComparison.Ordinal)
                    .Replace(",x,Scale)", ",x,\"Scale\",Scale)", StringComparison.Ordinal);
            }

            Assert.Equal(expected, Normalized(File.ReadAllText(Path.Combine(built, Path.GetFileName(file)))));
        }

        Assert.Equal(
            ["K9.csproj", "K9.ctf", "K9.deps.json", "K9.dll", "buildresult.json", "includedSupportPackages.txt", "mccExcludedFiles.log", "readme.txt",
                "requiredMCRProducts.txt", "unresolvedSymbols.txt"],
            Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AnInstanceMethodWithOnlyAnOptionalInputCompiles()
    {
        // R2025b wrote "_objrep.bump(new RunOptions(nargout:0),);" for the overload without the input (k9_build: optmethod).
        TypedClass cls = new("k9opt")
        {
            IsHandle = true,
            Methods = [new TypedMethod("bump", [new TypedArgument("obj"), new TypedArgument("by") { Optional = true, ClassName = "double", Size = [1, 1], MustBeReal = true }], [])],
        };
        string text = TypedInterface.Generate([], [cls], "K9o", new DateTime(2026, 10, 24), static _ => false, "0.0.0").Single().Text;
        Assert.Contains("    public void bump(double @by){\n        _objrep.bump(new RunOptions(nargout:0),@by);\n    }\n", text, StringComparison.Ordinal);
        Assert.Contains("    public void bump(){\n        _objrep.bump(new RunOptions(nargout:0));\n    }\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AVariadicFunctionHasNoInterface()
    {
        var function = new TypedFunction("k9nargs", [new TypedArgument("varargin") { Kind = TypedArgumentKind.Repeating }], []);
        Assert.Throws<TypedInterfaceException>(() => TypedInterface.Generate([function], [], "K9V", DateTime.Now, static _ => false, "0.0.0"));
    }

    [Fact]
    public async Task TheArchiveAnswersAsR2025bsDoes()
    {
        string folder = await BuildK9();
        dynamic matlab = MATLABRuntime.StartMATLAB(Path.Combine(folder, "K9.ctf"));
        var m = (MATLABProvider)matlab;
        TextWriter quiet = TextWriter.Null;

        // Values out and in (out.double.double, out.double.float, out.matrix.double[,], in.strings2, in.dict).
        Assert.Equal(2.5, (double)matlab.k9make("double"));
        Assert.Equal("Conversion from MATLAB double scalar to System.Single is not supported.",
            Assert.Throws<InvalidCastException>(() => (float)matlab.k9make("double")).Message);
        Assert.Equal(new double[,] { { 1, 2, 3 }, { 4, 5, 6 } }, (double[,])matlab.k9make("matrix"));
        Assert.Equal("string [2 2] [\"a\" \"c\" \"b\" \"d\"]", (string)matlab.k9describe(new string[,] { { "a", "b" }, { "c", "d" } }));
        Assert.Equal("dictionary [1 1] string->double [[\"b\" \"a\"]] -> [[2 1]]",
            (string)matlab.k9describe(new Dictionary<string, double> { ["b"] = 2, ["a"] = 1 }));

        // Name-value arguments by name (call.nv.named.*), and R2025b's refusals with the function's own frame.
        Assert.Equal("2 3 none", (string)matlab.k9nv(2.0, scale: 3.0));
        string bad = Assert.Throws<MATLABExecutionException>(() => (string)matlab.k9nv(new RunOptions(quiet, quiet), 2.0, Bad: 1.0)).Message;
        Assert.StartsWith("MATLAB:TooManyInputs: Invalid argument name 'Bad'. Name must be 'Scale' or 'Label'.\nError in k9nv (line 1) in file: ", bad);
        string first = Assert.Throws<MATLABExecutionException>(() => (string)matlab.k9nv(new RunOptions(quiet, quiet), Scale: 3.0)).Message;
        Assert.StartsWith("MATLAB:TooManyInputs: Invalid argument at position 1. Function requires exactly 1 positional input(s) before name 'Scale'.", first);

        // Errors, names not in the component, and a disposed instance (error.id, call.missing, after.dispose).
        string id = Assert.Throws<MATLABExecutionException>(() => matlab.k9err(new RunOptions(quiet, quiet), "id")).Message;
        Assert.StartsWith("k9:bad: An error with an identifier: 7.\nError in k9err (line 5) in file: ", id);
        Assert.Equal("Function k9nosuch not found in the component.", Assert.Throws<MATLABExecutionException>(() => matlab.k9nosuch(1.0)).Message);

        // The strongly typed interface (typed.multi.3, typed.nv.1, typed.point.norm, typed.counter.increment, typed.objects.2).
        Assembly k9 = Assembly.LoadFrom(Path.Combine(folder, "K9.dll"));
        Type functions = k9.GetType("K9.MATLABFunctions", throwOnError: true)!;
        object?[] multi = [m, new double[] { 1, 2, 3 }, null, null, null];
        Method(functions, "k9t_multi", 5).Invoke(null, multi);
        Assert.Equal("6|3|n=3", $"{multi[2]}|{multi[3]}|{multi[4]}");
        object?[] nv = [m, new Complex(2, 0), new Complex(3, 0), null];
        Method(functions, "k9nv", 4).Invoke(null, nv);
        Assert.Equal("2 3 none", (string)(dynamic)nv[3]!); // R2025b's refused this (typed.nv.2, a divergence)

        Type point = k9.GetType("K9.k9point", throwOnError: true)!;
        object p = Activator.CreateInstance(point, m, 3.0, 4.0)!;
        object?[] norm = [null];
        point.GetMethods().Single(static x => x.Name == "norm" && x.GetParameters().Length == 1).Invoke(p, norm);
        Assert.Equal(5.0, (double)(dynamic)norm[0]!);
        Assert.Equal(p.GetHashCode(), Activator.CreateInstance(point, m, 3.0, 4.0)!.GetHashCode()); // keyHash, which R2025b's refuses

        Type counter = k9.GetType("K9.k9counter", throwOnError: true)!;
        object c = Activator.CreateInstance(counter, m, 1.0)!;
        counter.GetMethods().Single(static x => x.Name == "increment").Invoke(c, [2.0]);
        Assert.Equal(new Complex(3, 0), counter.GetProperty("Count")!.GetValue(c));
        counter.GetMethods().Single(static x => x.Name == "delete").Invoke(c, null);
        var deleted = Assert.Throws<TargetInvocationException>(() => counter.GetProperty("Count")!.GetValue(c));
        Assert.Equal("MATLAB:class:InvalidHandle: Invalid or deleted object.", deleted.InnerException!.Message);

        Type color = k9.GetType("K9.k9color", throwOnError: true)!;
        object?[] objects = [m, Enum.Parse(color, "blue"), null, null];
        Method(functions, "k9t_objects", 4).Invoke(null, objects);
        Assert.Equal("blue", objects[3]!.ToString());
        Assert.Equal(new Complex(1, 0), point.GetProperty("X")!.GetValue(objects[2]));

        ((IDisposable)m).Dispose();
        Assert.Equal("MATLABProvider", Assert.Throws<ObjectDisposedException>(() => matlab.k9echo(1.0)).ObjectName);
    }

    /// <summary>The overload of <paramref name="parameters"/> parameters whose last is an output.</summary>
    private static MethodInfo Method(Type type, string name, int parameters) =>
        type.GetMethods().Single(m => m.Name == name && m.GetParameters().Length == parameters && m.GetParameters()[^1].IsOut);
}
