using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using JGraph.Api;
using JGraph.MATLAB.NET.Arrays;
using JGraph.Runtime;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.Runtime;

/// <summary>
/// A .NET assembly for the MWArray API (compiler and SDK plan, stage K8), built by JGraph's mcc from the
/// probe's own sources (<c>tools/matlab-checklist/compiler-probes/src/k8</c>) and called as
/// <c>k8client</c> calls R2025b's: the generated classes against R2025b's generated text
/// (<c>recordings/k8_out</c>), and the calls against R2025b's answers (<c>recordings/k8_client.out.txt</c>),
/// every one alike but the divergences ADR 0264 records.
/// </summary>
[Collection("JG facade")]
public sealed class DotNetAssemblyTests : IDisposable
{
    /// <summary>The cases where JGraph answers rightly and R2025b does not, or JGraph has no such value (ADR 0264).</summary>
    private static readonly HashSet<string> Divergent =
    [
        "make.int8", "make.uint16", "make.uint32", "make.uint64", "make.int8.toarray", "make.sparselogical",
        "native.make.int8", "native.make.uint64", "native.make.sparse",
    ];

    private static readonly string Repo = FindRepo();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _root =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-k8-" + Guid.NewGuid().ToString("N"))).FullName;

    private readonly string? _cacheBefore = Environment.GetEnvironmentVariable("MCR_CACHE_ROOT");

    public DotNetAssemblyTests()
    {
        JG.Reset();
        Environment.SetEnvironmentVariable("MCR_CACHE_ROOT", Path.Combine(_root, "cache"));
        string src = Directory.CreateDirectory(Path.Combine(_root, "src")).FullName;
        foreach (string file in Directory.GetFiles(Path.Combine(Repo, "tools", "matlab-checklist", "compiler-probes", "src", "k8"), "*.m"))
        {
            File.Copy(file, Path.Combine(src, Path.GetFileName(file)));
        }
    }

    public void Dispose()
    {
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

    /// <summary>Builds K8 as the probe's main build does (two classes, .NET 5.0) into <c>dn\K8</c>, and answers its folder.</summary>
    private async Task<string> BuildK8()
    {
        string folder = Path.Combine(_root, "dn", "K8");
        string Files(params string[] names) => string.Join(",", names.Select(n => Path.Combine(Src, n + ".m")));
        string code = $"mcc('-W', 'dotnet:K8,api=mwarray,K8Class,5.0,Private,local,version=1.0.0.0', '-T', 'link:lib', '-d', '{folder}', "
            + $"'class{{K8Class:{Files("k8args", "k8echo", "k8err", "k8make", "k8quiet", "k8show", "k8vout")}}}', 'class{{K8Other:{Files("k8other")}}}');";
        await using IScriptSession session = Assert.IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
            .CreateSession(new ScriptContext(_output, static (_, _) => { }, Src));
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return folder;
    }

    /// <summary>
    /// A generated file without what differs by construction: the header comment (JGraph's compiler,
    /// the date, the arguments' paths), the files' paths in the class summary, and MathWorks' namespace.
    /// </summary>
    private static string Normalized(string text)
    {
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("MathWorks.MATLAB.NET", "JGraph.MATLAB.NET", StringComparison.Ordinal);
        text = text[(text.IndexOf("*/\n", StringComparison.Ordinal) + 3)..];
        var kept = new List<string>();
        bool files = false;
        foreach (string line in text.Split('\n'))
        {
            if (line.Contains("functions contained in the files:", StringComparison.Ordinal))
            {
                files = true;
                kept.Add(line);
                continue;
            }

            if (files && line == "  /// </summary>")
            {
                files = false;
            }

            if (!files || line == "  /// <newpara></newpara>")
            {
                kept.Add(line);
            }
        }

        return string.Join("\n", kept);
    }

    [Fact]
    public async Task TheGeneratedClassesAreR2025bsWithJGraphsNamespaces()
    {
        string folder = await BuildK8();
        string recorded = Path.Combine(Repo, "tools", "matlab-checklist", "compiler-probes", "recordings", "k8_out");
        foreach (string file in new[] { "K8Class.cs", "K8ClassNative.cs", "K8Other.cs", "K8OtherNative.cs" })
        {
            Assert.Equal(Normalized(File.ReadAllText(Path.Combine(recorded, file))), Normalized(File.ReadAllText(Path.Combine(folder, file))));
        }

        Assert.Equal(
            ["K8.csproj", "K8.dll", "K8.xml", "K8Class.cs", "K8ClassNative.cs", "K8Native.csproj", "K8Native.dll", "K8Native.xml", "K8Other.cs",
                "K8OtherNative.cs", "K8_overview.html", "buildresult.json", "includedSupportPackages.txt", "mccExcludedFiles.log", "readme.txt",
                "requiredMCRProducts.txt", "unresolvedSymbols.txt"],
            Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheAssemblyAnswersAsR2025bsDoes()
    {
        string folder = await BuildK8();
        Dictionary<string, string> recorded = RecordedCases();
        Assembly managed = Assembly.LoadFrom(Path.Combine(folder, "K8.dll"));
        Assembly native = Assembly.LoadFrom(Path.Combine(folder, "K8Native.dll"));
        dynamic k8 = Activator.CreateInstance(managed.GetType("K8.K8Class", throwOnError: true)!)!;
        dynamic n8 = Activator.CreateInstance(native.GetType("K8Native.K8Class", throwOnError: true)!)!;
        k8.k8quiet();

        var cases = new Dictionary<string, Func<object?>>(StringComparer.Ordinal);
        foreach (string kind in new[] { "double", "row", "matrix", "nd", "empty", "empty03", "single", "int8", "uint8", "int16", "uint16",
            "int32", "uint32", "int64", "uint64", "complex", "complexscalar", "logical", "logicalscalar", "char", "charmatrix", "emptychar",
            "cell", "nestedcell", "struct", "structarray", "emptystruct", "string", "strings", "stringmissing", "sparse", "sparselogical",
            "inf", "pi", "large", "small", "mixed", "handle" })
        {
            cases["make." + kind] = () => k8.k8make(new MWCharArray(kind));
        }

        foreach (string kind in new[] { "double", "row", "matrix", "nd", "empty", "single", "int8", "uint8", "int16", "int32", "int64",
            "uint64", "complex", "logical", "logicalscalar", "char", "charmatrix", "emptychar", "cell", "struct", "structarray", "string",
            "strings", "sparse", "handle" })
        {
            cases["native.make." + kind] = () => n8.k8make(kind);
        }

        cases["make.uint8.toarray"] = () => ((MWNumericArray)k8.k8make(new MWCharArray("uint8"))).ToArray();
        cases["make.int8.toarray"] = () => ((MWNumericArray)k8.k8make(new MWCharArray("int8"))).ToArray();
        cases["make.cell.element.3"] = () => ((MWCellArray)k8.k8make(new MWCharArray("cell")))[3];
        cases["make.struct.name"] = () => ((MWStructArray)k8.k8make(new MWCharArray("struct")))["name"];
        cases["make.strings.missing.toarray"] = () => ((MWStringArray)k8.k8make(new MWCharArray("stringmissing"))).ToArray();
        cases["make.nargout.0"] = () => k8.k8make(0, new MWCharArray("double"));
        cases["make.nargout.2"] = () => k8.k8make(2, new MWCharArray("double"));
        cases["make.nargout.neg"] = () => k8.k8make(-1, new MWCharArray("double"));
        cases["make.noargs"] = () => k8.k8make();
        cases["make.unknown"] = () => k8.k8make(new MWCharArray("nothing"));
        cases["vout.3"] = () => k8.k8vout(3, (MWArray)1.0, (MWArray)2.0);
        cases["args.4"] = () => k8.k8args((MWArray)1.0, (MWArray)2.0, (MWArray)3.0, (MWArray)4.0);
        cases["args.n2.4"] = () => k8.k8args(2, (MWArray)1.0, (MWArray)2.0, (MWArray)3.0, (MWArray)4.0);
        cases["args.n3"] = () => k8.k8args(3, (MWArray)1.0);
        cases["native.vout.3"] = () => n8.k8vout(3, 1.0);
        cases["native.struct.fieldnames"] = () => ((JGraph.MATLAB.NET.Arrays.native.MWStructArray)n8.k8make("struct")).FieldNames;
        foreach (string kind in new[] { "id", "nested", "undefined", "cause", "index" })
        {
            cases["err." + kind] = () =>
            {
                k8.k8err(new MWCharArray(kind));
                return null;
            };
        }

        var failures = new List<string>();
        foreach ((string label, Func<object?> call) in cases)
        {
            string answer;
            try
            {
                object? result = call();
                answer = result is null ? "ok" : Describe(result);
            }
            catch (Exception e)
            {
                answer = Threw(e is TargetInvocationException { InnerException: { } inner } ? inner : e);
            }

            string expected = Answer(recorded[label]);
            bool alike = Answer(answer) == expected;
            if (alike == Divergent.Contains(label))
            {
                failures.Add($"{label}: {(alike ? "agrees, though recorded as a divergence" : "R2025b " + expected + "\n  JGraph " + answer)}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    /// <summary>R2025b's client transcript, label by label (the err cases' "ok" is what Do printed).</summary>
    private static Dictionary<string, string> RecordedCases()
    {
        var cases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(Path.Combine(Repo, "tools", "matlab-checklist", "compiler-probes", "recordings", "k8_client.out.txt")))
        {
            Match m = Regex.Match(line, @"^  ([a-z][\w.]*)\t(.*)$");
            if (m.Success)
            {
                cases[m.Groups[1].Value] = m.Groups[2].Value;
            }
        }

        return cases;
    }

    /// <summary>What differs by construction: MathWorks' namespace, and where the runtime cache holds an M-file.</summary>
    private static string Answer(string text) =>
        Regex.Replace(text.Replace("MathWorks.MATLAB.", "JGraph.MATLAB.", StringComparison.Ordinal), @"file [^,]*[\\/]([^\\/,]+\.m), name", "file <cache>\\$1, name");

    // --- The client's descriptions (src/k8client/Program.cs) ---------------------------------------

    private static string Flat(string? s) => s is null ? "null" : s.Replace("\r", "").Replace("\n", "\\n").Replace("\t", "\\t");

    private static string TypeLabel(Type t) =>
        t.Namespace is { } ns && ns.EndsWith(".native", StringComparison.Ordinal) ? "native." + t.Name
        : t.Namespace is { } system && system.StartsWith("System", StringComparison.Ordinal) ? t.FullName!
        : t.Name;

    private static string Element(object? o) => o switch
    {
        null => "null",
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        char c => "'" + c + "'",
        string s => "\"" + Flat(s) + "\"",
        Array a => Describe(a),
        MWArray m => Describe(m),
        _ when o.GetType().IsPrimitive => Convert.ToString(o, CultureInfo.InvariantCulture)!,
        _ => Describe(o),
    };

    private static string Describe(object o)
    {
        switch (o)
        {
            case MWNumericArray n:
                return $"MWNumericArray {string.Join("x", n.Dimensions)} {n.NumericType}{(n.IsComplex ? " complex" : "")}{(n.IsSparse ? " sparse" : "")} <<{Flat(n.ToString())}>>";
            case MWArray a:
                return $"{TypeLabel(a.GetType())} {string.Join("x", a.Dimensions)} {a.ArrayType} <<{Flat(a.ToString())}>>";
            case Array arr:
            {
                var lengths = Enumerable.Range(0, arr.Rank).Select(arr.GetLength);
                var parts = new List<string>();
                foreach (object? x in arr)
                {
                    parts.Add(Element(x));
                }

                return $"{TypeLabel(arr.GetType())} [{string.Join(",", lengths)}] {{{string.Join(", ", parts)}}}";
            }

            default:
                return o.GetType().Namespace is { } ns && ns.EndsWith(".native", StringComparison.Ordinal)
                    ? $"{TypeLabel(o.GetType())} <<{Flat(o.ToString())}>>"
                    : $"{TypeLabel(o.GetType())} {Element(o)}";
        }
    }

    private static string Threw(Exception e)
    {
        var parts = new List<string>();
        for (Exception? x = e; x is not null; x = x.InnerException)
        {
            parts.Add($"{TypeLabel(x.GetType())}: {Flat(x.Message.Trim())}");
        }

        return "THREW " + string.Join(" <- ", parts);
    }
}
