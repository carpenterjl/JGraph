using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using JGraph.Api;
using JGraph.Deploy;
using JGraph.Runtime;
using JGraph.Runtime.Com;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.Deploy;

/// <summary>
/// An Excel add-in's COM DLL (compiler and SDK plan, stage K17c): the template <c>jgxl.dll</c>
/// copied and stamped by <c>mcc -W excel</c> with the type library, the archive and the version,
/// against what R2025b's mbuild built and what its add-in answered a COM client
/// (<c>recordings/k17_excel.out.txt</c>, <c>k17_com.out.txt</c>): the interface the type library
/// declares, parameter for parameter with R2025b's IDL; the DLL's resources; the served object's
/// members in this process (nargout, varargin, varargout, the conversions, the errors); and the
/// K17a oracle itself (<c>src/k17client/k17com.ps1</c>) run from PowerShell against the unregistered
/// DLL through its own class factory, as a COM client in a process of its own. Nothing here
/// registers anything (decision KO13).
/// </summary>
[Collection("JG facade")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class ExcelAddInServerTests : IDisposable
{
    private static readonly string Repo = FindRepo();
    private static readonly string Probes = Path.Combine(Repo, "tools", "matlab-checklist", "compiler-probes");
    private readonly RecordingScriptOutput _output = new();
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-k17c-" + Guid.NewGuid().ToString("N"))).FullName;
    private readonly string? _cacheBefore = Environment.GetEnvironmentVariable("MCR_CACHE_ROOT");

    /// <summary>The probe's functions, in its build's order (probe_k17_excel: names).</summary>
    private static readonly string[] Names =
    [
        "k17_quiet", "k17_none", "k17_one", "k17_many", "k17_two", "k17_opt", "k17_varin", "k17_varout", "k17_both", "k17_args",
        "k17_noout", "k17_err", "k17_plot", "k17_h1", "k17_echo", "k17_pass", "k17_count", "k17_make",
    ];

    public ExcelAddInServerTests()
    {
        JG.Reset();
        Environment.SetEnvironmentVariable("MCR_CACHE_ROOT", Path.Combine(_root, "cache"));
        Directory.CreateDirectory(Src);
        foreach (string file in Directory.GetFiles(Path.Combine(Probes, "src", "k17"), "*.m"))
        {
            File.Copy(file, Path.Combine(Src, Path.GetFileName(file)));
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
            // A DLL a client's process still held; the temp folder clears it.
        }
    }

    private string Src => Path.Combine(_root, "src");

    private static string FindRepo()
    {
        string? folder = AppContext.BaseDirectory;
        while (folder is not null && !Directory.Exists(Path.Combine(folder, "tools", "matlab-checklist")))
        {
            folder = Path.GetDirectoryName(folder);
        }

        return folder ?? throw new DirectoryNotFoundException("The repository was not found above the test's folder.");
    }

    /// <summary>The recording's lines under <paramref name="key"/>, their <c>  |</c> prefix dropped.</summary>
    private static string[] Recorded(string file, string key)
    {
        string[] all = File.ReadAllLines(Path.Combine(Probes, "recordings", file));
        int at = Array.FindIndex(all, l => l.StartsWith(key + "\t", StringComparison.Ordinal));
        Assert.True(at >= 0, key + " is not in the recording");
        return [.. all.Skip(at + 1).TakeWhile(static l => l.StartsWith("  |", StringComparison.Ordinal)).Select(static l => l[3..])];
    }

    /// <summary>A recording's key-value rows.</summary>
    private static Dictionary<string, string> Rows(string path)
    {
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(path))
        {
            int tab = line.IndexOf('\t', StringComparison.Ordinal);
            if (tab > 0 && !line.StartsWith("  ", StringComparison.Ordinal))
            {
                rows[line[..tab]] = line[(tab + 1)..];
            }
        }

        return rows;
    }

    /// <summary>Builds the probe's add-in (k17_excel: plain, through mcc) into <paramref name="folder"/> and answers the DLL's path.</summary>
    private async Task<string> Build(string folder, string name = "k17xl", string className = "K17Xl", string version = "1.0", string extra = "")
    {
        string files = string.Join(", ", Names.Select(n => $"'{Path.Combine(Src, n + ".m").Replace("\\", "/", StringComparison.Ordinal)}'"));
        string code = $"mcc('-W', 'excel:{name},{className},version={version}', '-T', 'link:lib', '-d', '{folder.Replace("\\", "/", StringComparison.Ordinal)}'{extra}, {files});";
        await using IScriptSession session = Assert.IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
            .CreateSession(new ScriptContext(_output, static (_, _) => { }, Src));
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        string dll = Path.Combine(folder, ExcelAddIn.DllName(name, ProgramVersion.Parse(version)!.Value));
        Assert.True(File.Exists(dll), dll + " was not written");
        return dll;
    }

    [Fact]
    public async Task TheTypeLibraryDeclaresR2025bsInterface()
    {
        string dll = await Build(Path.Combine(_root, "b"));
        ExcelTypeLibInfo lib = ExcelTypeLib.Read(dll);
        Assert.Equal(("k17xl", "K17Xl", "IK17Xl"), (lib.LibraryName, lib.ClassName, lib.InterfaceName));
        Assert.Equal(new ProgramVersion(1, 0, 0, 0), lib.Version);
        Assert.Equal("k17xl.K17Xl.1_0", lib.VersionedProgId);
        Assert.Equal(ExcelComGuids.TypeLib("k17xl"), lib.LibraryId);
        Assert.Equal(ExcelComGuids.Class("k17xl", "K17Xl", lib.Version), lib.ClassId);
        Assert.Equal(ExcelComGuids.Interface("k17xl", "K17Xl", lib.Version), lib.InterfaceId);

        // plain.file.k17xl_idl.idl: each method's parameters, in R2025b's order, from its IDL.
        string idl = string.Join(" ", Recorded("k17_excel.out.txt", "plain.file.k17xl_idl.idl").Select(static l => l.Trim()));
        var expected = new List<(string Name, string[] Inputs, string[] Outputs)>();
        foreach (Match m in Regex.Matches(idl, @"helpstring\(""Method (\w+)""\)\] HRESULT \1\(([^;]*)\);"))
        {
            string[] parameters = Regex.Split(m.Groups[2].Value, @",\s*(?=\[)").Where(static s => s.Length > 0).Select(static s => s.Trim()).ToArray();
            var inputs = new List<string>();
            var outputs = new List<string>();
            foreach (string p in parameters)
            {
                string pname = p.Split(' ')[^1];
                if (p.StartsWith("[in] long nargout", StringComparison.Ordinal))
                {
                    continue;
                }

                (p.StartsWith("[in,out]", StringComparison.Ordinal) ? outputs : inputs).Add(pname);
            }

            expected.Add((m.Groups[1].Value, [.. inputs], [.. outputs]));
        }

        Assert.Equal(18, expected.Count);
        Assert.Equal(expected.Select(static e => e.Name), lib.Functions.Select(static f => f.Name));
        foreach ((string name, string[] inputs, string[] outputs) in expected)
        {
            ExcelTypeLibFunction f = lib.Functions.First(x => x.Name == name);
            Assert.Equal(inputs, f.Inputs);
            Assert.Equal(outputs, f.Outputs);
        }

        Assert.True(lib.Functions.First(static f => f.Name == "k17_both") is { HasVarargin: true, HasVarargout: true });
        Assert.False(lib.Functions.First(static f => f.Name == "k17_one").HasVarargin);

        // The help text is the H1 line as written (k17_com: help.k17_one, help.k17_h1 as JGraph writes it; ADR 0314).
        Assert.Equal("K17_ONE Doubles its one input (compiler and SDK plan, stage K17).", lib.Functions.First(static f => f.Name == "k17_one").Help);
        Assert.Equal("K17_H1 Says \"hello\" and 'bye' for 5 € — a naïve façade at 100% of résumé\\path, <tag> & more.", lib.Functions.First(static f => f.Name == "k17_h1").Help);

        // guids.*: the ids are deterministic, the library's from the name alone, the others from the names and the version.
        Assert.Equal(ExcelComGuids.TypeLib("k17m"), ExcelComGuids.TypeLib("k17m"));
        Assert.Equal(ExcelComGuids.Class("k17m", "K17M", ProgramVersion.Default), ExcelComGuids.Class("k17m", "K17M", ProgramVersion.Default));
        Assert.NotEqual(ExcelComGuids.Class("k17m", "K17M", ProgramVersion.Default), ExcelComGuids.Class("k17m", "K17M", new ProgramVersion(2, 0, 0, 0)));
        Assert.NotEqual(ExcelComGuids.Interface("k17m", "K17M", ProgramVersion.Default), ExcelComGuids.Interface("k17m", "K17M", new ProgramVersion(2, 0, 0, 0)));
        Assert.NotEqual(ExcelComGuids.TypeLib("k17xl"), ExcelComGuids.TypeLib("k17bas"));
        Assert.Equal(5, (ExcelComGuids.TypeLib("k17m").ToByteArray()[7] >> 4));
    }

    [Fact]
    public async Task TheDllCarriesTheArchiveAndItsResources()
    {
        string dll = await Build(Path.Combine(_root, "b"));
        // plain.file.k17xl.rc and b.plain.k17xl_1_0.resource (k17_pe): TYPELIB 1, RT_RCDATA 2 the archive, the version block.
        Assert.NotNull(PeResources.Read(dll, "TYPELIB", 1));
        byte[]? archive = PeResources.Read(dll, 10, 2);
        Assert.NotNull(archive);
        Assert.Equal([0x50, 0x4b, 0x03, 0x04], archive[..4]);
        byte[]? version = PeResources.Read(dll, 16, 1);
        Assert.NotNull(version);
        Assert.Contains("1.0.0.0", Encoding.Unicode.GetString(version), StringComparison.Ordinal);
        Assert.Equal(RuntimeLayout.CurrentVersion, Encoding.UTF8.GetString(PeResources.Read(dll, 10, StandaloneProgram.RuntimeVersionResource)!));
        using (CtfArchiveReader reader = CtfArchiveReader.Open(dll))
        {
            Assert.Equal(CtfTargetType.ExcelAddIn, reader.Manifest.TargetType);
            Assert.Equal(Names, reader.Manifest.PublicFunctions.Select(static f => f.Name));
        }

        Assert.False(File.Exists(Path.Combine(_root, "b", "k17xl.ctf")));
        foreach (string export in new[] { "DllGetClassObject", "DllCanUnloadNow", "DllRegisterServer", "DllUnregisterServer" })
        {
            nint module = NativeLibrary.Load(dll);
            try
            {
                Assert.True(NativeLibrary.TryGetExport(module, export, out _), export + " is not exported");
            }
            finally
            {
                NativeLibrary.Free(module);
            }
        }

        // noembed: -C writes the archive beside the DLL and nothing as resource 2.
        string separate = await Build(Path.Combine(_root, "sep"), "k17sep", "K17Sep", extra: ", '-C'");
        Assert.Null(PeResources.Read(separate, 10, 2));
        Assert.True(File.Exists(Path.Combine(_root, "sep", "k17sep.ctf")));
        Assert.NotNull(PeResources.Read(separate, "TYPELIB", 1));
    }

    [Fact]
    public async Task TheObjectServesItsMembersAsR2025bsDoes()
    {
        string dll = await Build(Path.Combine(_root, "b"));
        using var args = new Variants();
        using var component = ExcelComponent.Open(dll, 0);
        Assert.Equal("k17xl.K17Xl.1_0", component.ProgId);

        // help.*: the H1 line of the function named exactly, else ''.
        Assert.Equal("K17_ONE Doubles its one input (compiler and SDK plan, stage K17).", component.Invoke("help", ComInvokeKind.PropertyGet, args.Of("k17_one")));
        Assert.Equal("", component.Invoke("help", ComInvokeKind.PropertyGet, args.Of("nosuch")));
        Assert.Equal("", component.Invoke("help", ComInvokeKind.PropertyGet, args.Of("K17_ONE")));

        // two.nargout0 to two.nargout3: the first nargout slots filled, the rest left, one too many refused in MATLAB's words.
        nint a = args.Slot();
        nint b = args.Slot();
        component.Invoke("k17_two", ComInvokeKind.Method, args.Of(0, a, b, 5.0));
        Assert.Equal(new object?[] { null, null }, new[] { args.Read(a), args.Read(b) });
        component.Invoke("k17_two", ComInvokeKind.Method, args.Of(1, a, b, 5.0));
        Assert.Equal(new object?[] { 5.0, null }, new[] { args.Read(a), args.Read(b) });
        component.Invoke("k17_two", ComInvokeKind.Method, args.Of(2, a, b, 5.0));
        Assert.Equal(new object?[] { 5.0, -5.0 }, new[] { args.Read(a), args.Read(b) });
        ComCallException tooMany = Assert.Throws<ComCallException>(() => component.Invoke("k17_two", ComInvokeKind.Method, args.Of(3, a, b, 5.0)));
        Assert.Equal((ComCallException.Fail, "k17xl.K17Xl.1_0", "Too many output arguments."), (tooMany.Code, tooMany.ErrorSource, tooMany.Message));

        // varout.nargout2, both.nargout3: varargout as a row of Variants after the fixed outputs.
        nint v = args.Slot();
        component.Invoke("k17_varout", ComInvokeKind.Method, args.Of(2, v, 5.0));
        Assert.True(Same(new object[,] { { 5.0, 10.0 } }, args.Read(v)), Show(args.Read(v)));
        Assert.Equal(1, ((Array)args.Read(v)!).GetLowerBound(0));
        nint first = args.Slot();
        nint rest = args.Slot();
        component.Invoke("k17_both", ComInvokeKind.Method, args.Of(3, first, rest, 7.0, new object[] { "a", 2.0 }));
        Assert.Equal("x=double [1 1] [7] nargin=3 nargout=3", args.Read(first));
        Assert.True(Same(new object[,] { { "a", 2.0 } }, args.Read(rest)), Show(args.Read(rest)));
        component.Invoke("k17_both", ComInvokeKind.Method, args.Of(2, first, rest, 7.0, Type.Missing));
        Assert.Equal("x=double [1 1] [7] nargin=1 nargout=2", args.Read(first));
        Assert.True(Same(new object[,] { { 1.0 } }, args.Read(rest)), Show(args.Read(rest)));

        // varin.one, varin.vararray, varin.array, varin.vararray2d, varin.missing: what spreads and what does not.
        nint r = args.Slot();
        component.Invoke("k17_varin", ComInvokeKind.Method, args.Of(1, r, 1.0));
        Assert.Equal("nargin=1 {double [1 1] [1]}", args.Read(r));
        component.Invoke("k17_varin", ComInvokeKind.Method, args.Of(1, r, new object[] { 1.0, "a", true }));
        Assert.Equal("nargin=3 {double [1 1] [1]; char [1 1] 'a'; logical [1 1] true}", args.Read(r));
        component.Invoke("k17_varin", ComInvokeKind.Method, args.Of(1, r, new double[] { 1, 2, 3 }));
        Assert.Equal("nargin=1 {double [1 3] [1 2 3]}", args.Read(r));
        component.Invoke("k17_varin", ComInvokeKind.Method, args.Of(1, r, new object[,] { { 1.0, 2.0 }, { 3.0, 4.0 } }));
        Assert.Equal("nargin=1 {double [2 2] [1 3 2 4]}", args.Read(r));
        component.Invoke("k17_varin", ComInvokeKind.Method, args.Of(1, r, Type.Missing));
        Assert.Equal("nargin=0 {}", args.Read(r));

        // opt.1, opt.emptylast, opt.dbnull, opt.errorcell, opt.middle: trailing arguments left out, a middle one refused (in JGraph's words).
        component.Invoke("k17_opt", ComInvokeKind.Method, args.Of(1, r, 1.0, Type.Missing, Type.Missing));
        Assert.Equal("nargin=1 a=double [1 1] [1]", args.Read(r));
        component.Invoke("k17_opt", ComInvokeKind.Method, args.Of(1, r, 1.0, "b", null));
        Assert.Equal("nargin=2 a=double [1 1] [1] b=char [1 1] 'b'", args.Read(r));
        component.Invoke("k17_opt", ComInvokeKind.Method, args.Of(1, r, 1.0, DBNull.Value, true));
        Assert.Equal("nargin=3 a=double [1 1] [1] b=double [0 0] [] c=logical [1 1] true", args.Read(r));
        component.Invoke("k17_opt", ComInvokeKind.Method, args.Of(1, r, 1.0, new ErrorWrapper(unchecked((int)0x800A07FA)), true));
        Assert.Equal("nargin=3 a=double [1 1] [1] b=int32 [1 1] [-2146826246] c=logical [1 1] true", args.Read(r));
        ComCallException middle = Assert.Throws<ComCallException>(() => component.Invoke("k17_opt", ComInvokeKind.Method, args.Of(1, r, 1.0, Type.Missing, true)));
        Assert.StartsWith("Input argument 2 (b) was not given", middle.Message, StringComparison.Ordinal);

        // in.*: what each Variant becomes (k17_echo).
        foreach ((object? given, string seen) in new (object?, string)[]
        {
            (3.5, "double [1 1] [3.5]"), (1.5f, "single [1 1] [1.5]"), (7, "int32 [1 1] [7]"), ((short)-3, "int16 [1 1] [-3]"), ((byte)200, "uint8 [1 1] [200]"),
            (true, "logical [1 1] true"), ("hello", "char [1 5] 'hello'"), ("", "char [0 0] ''"), ('c', "uint16 [1 1] [99]"),
            (new DateTime(2024, 3, 15, 10, 30, 0), "double [1 1] [739326.4375]"), (2.25m, "double [1 1] [2.25]"),
            (new double[] { 1, 2, 3 }, "double [1 3] [1 2 3]"), (new double[,] { { 1, 2, 3 }, { 4, 5, 6 } }, "double [2 3] [1 4 2 5 3 6]"),
            (new double[,] { { 1 }, { 2 }, { 3 } }, "double [3 1] [1 2 3]"), (new double[,,] { { { 0, 0 }, { 0, 0 } }, { { 0, 0 }, { 0, 0 } } }, "double [2 2 2] [0 0 0 0 0 0 0 0]"),
            (new bool[] { true, false }, "logical [1 2] [true false]"), (new string[] { "a", "bb" }, "cell [1 2] {char [1 1] 'a'; char [1 2] 'bb'}"),
            (new object[,] { { "a", "b" }, { "c", "d" } }, "char [2 2] ['ab'; 'cd']"), (new object[] { 1.0, 2.0, 3.0 }, "double [1 3] [1 2 3]"),
            (new object[] { 1.0, "a", true }, "cell [1 3] {double [1 1] [1]; char [1 1] 'a'; logical [1 1] true}"),
            (new object[,] { { 1.0, "a" }, { true, 4.0 } }, "cell [2 2] {double [1 1] [1]; logical [1 1] true; char [1 1] 'a'; double [1 1] [4]}"),
            (new object?[] { 1.0, null, 3.0 }, "double [1 3] [1 0 3]"), (new object[] { 1.0, Type.Missing, 3.0 }, "cell [1 3] {double [1 1] [1]; int32 [1 1] [-2147352572]; double [1 1] [3]}"),
            (new object[] { 1, (short)2, (byte)3 }, "cell [1 3] {int32 [1 1] [1]; int16 [1 1] [2]; uint8 [1 1] [3]}"),
            (new object[] { new object[] { 1.0, 2.0 }, new object[] { 3.0, 4.0 } }, "cell [1 2] {cell [1 2] {double [1 1] [1]; double [1 1] [2]}; cell [1 2] {double [1 1] [3]; double [1 1] [4]}}"),
            (new object[] { new double[] { 1, 2 }, new double[] { 3, 4 } }, "cell [1 2] {double [1 2] [1 2]; double [1 2] [3 4]}"),
            (DBNull.Value, "double [0 0] []"), (new ErrorWrapper(unchecked((int)0x800A07FA)), "int32 [1 1] [-2146826246]"),
            (5000000000L, "int64 [1 1] [5000000000]"),
        })
        {
            component.Invoke("k17_echo", ComInvokeKind.Method, args.Of(1, r, given));
            Assert.Equal(seen, args.Read(r));
        }

        // out.*: what each MATLAB class becomes (k17_make).
        nint made = args.Slot();
        foreach ((string kind, object? expected) in new (string, object?)[]
        {
            ("double", 3.5), ("doublerow", new double[,] { { 1, 2, 3 } }), ("doublemat", new double[,] { { 1, 2 }, { 3, 4 } }), ("doubleneg0", 0.0),
            ("single", 1.5f), ("int8", (sbyte)-5), ("uint8", (byte)200), ("uint32", 4000000000u), ("logical", true),
            ("logicalmat", new bool[,] { { true, false }, { false, true } }), ("logicalempty", null), ("char", "hello"), ("charempty", null),
            ("charmat", new string[,] { { "a", "b" }, { "c", "d" } }), ("cell", new object[,] { { 1.0, "two", new double[,] { { 3, 4 } } } }),
            ("cellempty", new object[0, 0]), ("cellofempty", new object?[,] { { null, null, new object[0, 0] } }), ("empty", null), ("emptyrow", null),
            ("nan", null), ("nanmat", new object?[,] { { 1.0, null }, { null, 4.0 } }), ("inf", double.PositiveInfinity), ("datenum", 739326.4375),
            ("string", "str"), ("int64", 9007199254740992L),
        })
        {
            component.Invoke("k17_make", ComInvokeKind.Method, args.Of(1, made, kind));
            object? actual = args.Read(made);
            Assert.True(Same(expected, actual), $"out.{kind}: expected {Show(expected)}, got {Show(actual)}");
        }

        ComCallException pending = Assert.Throws<ComCallException>(() => component.Invoke("k17_make", ComInvokeKind.Method, args.Of(1, made, "struct")));
        Assert.Contains("stage K17d", pending.Message, StringComparison.Ordinal);

        // err.id, err.nested, err.none: the message with the frame, as R2025b's client reads it.
        ComCallException raised = Assert.Throws<ComCallException>(() => component.Invoke("k17_err", ComInvokeKind.Method, args.Of(1, r, "id")));
        Assert.Equal(("k17xl.K17Xl.1_0", "Custom text with an identifier\nError in => k17_err.m at line 6"), (raised.ErrorSource, raised.Message));
        Assert.Equal("Raised two frames down for nested\nError in => k17_err.m at line 14",
            Assert.Throws<ComCallException>(() => component.Invoke("k17_err", ComInvokeKind.Method, args.Of(1, r, "nested"))).Message);
        component.Invoke("k17_err", ComInvokeKind.Method, args.Of(1, r, "none"));
        Assert.Equal(0.0, args.Read(r));
        Assert.Equal(ComCallException.MemberNotFound, Assert.Throws<ComCallException>(() => component.Invoke("no_such", ComInvokeKind.Method, args.Of())).Code);
        Assert.StartsWith("The number of outputs asked for, -1, cannot be negative.",
            Assert.Throws<ComCallException>(() => component.Invoke("k17_two", ComInvokeKind.Method, args.Of(-1, a, b, 5.0))).Message, StringComparison.Ordinal);

        // flags.defaults: the flags with MathWorks' defaults, one object per component, its parts' values copied by a put.
        var flags = Assert.IsType<ExcelFlags>(component.Invoke("MWFlags", ComInvokeKind.PropertyGet, args.Of()));
        Assert.Same(flags, component.Invoke("MWFlags", ComInvokeKind.PropertyGet, args.Of()));
        Assert.Equal((1, 0, 0, 0, true, false), (flags.ArrayFormat.InputArrayFormat, flags.ArrayFormat.InputArrayIndFlag, flags.ArrayFormat.OutputArrayFormat,
            flags.ArrayFormat.OutputArrayIndFlag, flags.ArrayFormat.AutoResizeOutput, flags.ArrayFormat.TransposeOutput));
        Assert.Equal((0, 0, false, 693960, 0, 0), (flags.DataConversion.CoerceNumericToType, flags.DataConversion.InputDateFormat, flags.DataConversion.OutputAsDate,
            flags.DataConversion.DateBias, flags.DataConversion.ReplaceMissing, flags.DataConversion.ReplaceMatlabNaN));
        flags.DataConversion.Invoke("DateBias", ComInvokeKind.PropertyPut, args.Of(7));
        Assert.Equal(7, flags.DataConversion.Invoke("DateBias", ComInvokeKind.PropertyGet, args.Of()));
        var clone = Assert.IsType<ExcelFlags>(flags.Invoke("Clone", ComInvokeKind.Method, args.Of()));
        Assert.NotSame(flags, clone);
        Assert.Equal(7, clone.DataConversion.DateBias);

        // count.secondobject: a second object of the add-in shares its session.
        component.Invoke("k17_count", ComInvokeKind.Method, args.Of(1, r));
        Assert.Equal(1.0, args.Read(r));
        using var second = ExcelComponent.Open(dll, 0);
        second.Invoke("k17_count", ComInvokeKind.Method, args.Of(1, r));
        Assert.Equal(2.0, args.Read(r));
    }

    [Fact]
    public async Task ACOMClientCallsTheAddInWithoutRegistration()
    {
        string dll = await Build(Path.Combine(_root, "b"));
        string clsid = ExcelComGuids.Class("k17xl", "K17Xl", ProgramVersion.Default).ToString("B");
        string script = Path.Combine(Probes, "src", "k17client", "k17com.ps1");
        string outFile = Path.Combine(_root, "k17_com.lines");
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _root,
        };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-Dll", dll, "-Clsid", clsid, "-NoExcel", "-UtilProgId", "''", "-Out", outFile })
        {
            start.ArgumentList.Add(argument);
        }

        // The template finds jgrt.dll, and jgrt the engine beside it, through the test's own folder on the PATH.
        start.Environment["PATH"] = AppContext.BaseDirectory.TrimEnd('\\') + ";" + Environment.GetEnvironmentVariable("PATH");
        start.Environment["MCR_CACHE_ROOT"] = Path.Combine(_root, "cache-client");
        using Process process = Process.Start(start)!;
        Task<string> error = process.StandardError.ReadToEndAsync();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(300_000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("The COM client did not finish in five minutes:\n" + await output);
        }

        string console = await output;
        string errors = await error;
        Assert.True(File.Exists(outFile), $"the client wrote nothing (exit {process.ExitCode}):\n{console}\n{errors}");
        // The client's lines are kept beside the temp folder for a look after a failure.
        File.Copy(outFile, Path.Combine(Path.GetTempPath(), "jgraph-k17c-com.lines"), overwrite: true);
        Dictionary<string, string> mine = Rows(outFile);
        Dictionary<string, string> recorded = Rows(Path.Combine(Probes, "recordings", "k17_com.out.txt"));
        Assert.Equal("ok", mine.GetValueOrDefault("create"));
        Assert.False(mine.ContainsKey("FATAL"), "the client stopped: " + mine.GetValueOrDefault("FATAL") + " at " + mine.GetValueOrDefault("FATAL.where"));

        // What JGraph's add-in answers as R2025b's did, row for row (the flags' effect, Range objects, the
        // utility library and MathWorks' value classes are stage K17d's; the rest of the rows are its).
        string[] same =
        [
            "members", "quiet", "none", "one", .. Names.Where(static n => n != "k17_h1").Select(static n => "help." + n), "help.unknown", "help.empty", "help.case",
            "flags.defaults", "flags.sameobject", "flags.clone", "flags.put",
            "two.nargout0", "two.nargout1", "two.nargout2", "two.nargout3", "one.nargout0", "one.nargout2", "many.nargout3", "many.nargout1", "many.arrays",
            "varout.nargout0", "varout.nargout1", "varout.nargout2", "varout.nargout3",
            "both.nargout1", "both.nargout2", "both.nargout3", "both.nargout4", "both.scalarvarargin", "both.missingvarargin", "both.emptyvarargin", "both.threeargs",
            "noout", "noout.array", "noout.missing", "noout.empty", "count.1", "count.2", "count.secondobject", "count.firstagain",
            "varin.none", "varin.one", "varin.two", "varin.two.packed", "varin.missing", "varin.empty", "varin.array", "varin.vararray", "varin.vararray2d", "varin.emptyvararray",
            "opt.0", "opt.1", "opt.2", "opt.3", "opt.emptylast", "opt.dbnull", "opt.errorcell", "args.default", "args.both", "args.missing",
            "in.missing", "in.dbnull", "in.err_na", "in.err_div0", "in.r8", "in.r8_int", "in.r8_neg", "in.r8_big", "in.r8_nan", "in.r8_inf", "in.r4", "in.i4", "in.i2",
            "in.ui1", "in.ui2", "in.ui4", "in.i1", "in.bool_true", "in.bool_false", "in.bstr", "in.bstr_empty", "in.bstr_nonascii", "in.bstr_newline", "in.bstr_number",
            "in.char", "in.date", "in.date_1900", "in.cy", "in.decimal", "in.arr_r8", "in.arr_r8_one", "in.arr_r8_2d", "in.arr_r8_col", "in.arr_r8_row", "in.arr_r8_3d",
            "in.arr_r4", "in.arr_i4", "in.arr_i2", "in.arr_ui1", "in.arr_bool", "in.arr_str", "in.arr_str_2d", "in.arr_date", "in.arr_decimal", "in.arr_var_numbers",
            "in.arr_var_mixed", "in.arr_var_2d", "in.arr_var_2d_mixed", "in.arr_var_2d_strings", "in.arr_var_with_empty", "in.arr_var_with_missing",
            "in.arr_var_with_dbnull", "in.arr_var_with_date", "in.arr_var_ints", "in.arr_var_bools", "in.arr_var_strings", "in.arr_var_2d_with_empty",
            "in.arr_jagged", "in.arr_jagged_ragged", "in.arr_jagged_deep", "in.arr_jagged_r8", "in.arr_1based", "in.arr_1based_mixed",
            "out.double", "out.doublerow", "out.doublecol", "out.doublemat", "out.double3d", "out.doubleneg0", "out.bigint", "out.single", "out.singlemat", "out.int8",
            "out.uint8", "out.uint8mat", "out.int16", "out.uint16", "out.int32", "out.uint32", "out.logical", "out.logicalfalse", "out.logicalmat", "out.logicalempty",
            "out.char", "out.charone", "out.charmat", "out.charcol", "out.charempty", "out.charnonascii", "out.charnewline", "out.cell", "out.cellnested", "out.cellcol",
            "out.cellmat", "out.cellempty", "out.cellofempty", "out.empty", "out.emptyrow", "out.emptycol", "out.nan", "out.nanmat", "out.inf", "out.neginf", "out.datenum",
            "out.nosuchkind", "pass.r8", "pass.r4", "pass.i4", "pass.i2", "pass.ui1", "pass.bool_true", "pass.bstr", "pass.bstr_empty", "pass.bstr_nonascii", "pass.char",
            "pass.date", "pass.cy", "pass.decimal", "pass.arr_r8", "pass.arr_r8_2d", "pass.arr_r8_col", "pass.arr_i4", "pass.arr_bool", "pass.arr_str", "pass.arr_var_mixed",
            "pass.arr_var_2d", "pass.arr_var_with_empty", "pass.arr_jagged", "pass.arr_1based", "pass.arr_date", "pass.err_na", "pass.dbnull",
            "err.id", "err.plain", "err.multi", "err.nonascii", "err.nested", "err.warn", "err.nargs", "err.undefined", "err.index", "err.none", "err.badcount.more",
            "err.badcount.fewer", "err.badcount.fewer.dispatch", "err.unknownmethod", "err.argsblock.negative", "err.argsblock.text", "err.argsblock.toomany",
            "err.alldefault", "err.nargouttext", "err.slotnotbyref", "err.make.unknown", "err.errorinfo.lastwarn", "stale.1.error", "stale.4.ok", "stale.6.notenough",
            "plot.invisible", "plot.invisible.again",
        ];
        var differences = new List<string>();
        foreach (string key in same)
        {
            string expected = recorded[key];
            string? actual = mine.GetValueOrDefault(key);
            if (actual != expected)
            {
                differences.Add($"{key}\n  R2025b: {expected}\n  JGraph: {actual ?? "(missing)"}");
            }
        }

        Assert.True(differences.Count == 0, $"{differences.Count} of {same.Length} rows differ:\n" + string.Join("\n", differences));

        // help.k17_h1: the H1 line as written, where R2025b's help map mangles it (ADR 0314, a MATLAB bug not copied).
        Assert.Equal("string 'K17_H1 Says \"hello\" and 'bye' for 5 \\u20ac \\u2014 a na\\u00efve fa\\u00e7ade at 100% of r\\u00e9sum\\u00e9\\path, <tag> & more.'",
            mine["help.k17_h1"]);
    }

    /// <summary>Whether two marshalled values are the same: arrays element for element in the same shape and element type, scalars by value and type.</summary>
    private static bool Same(object? expected, object? actual)
    {
        if (expected is Array a && actual is Array b)
        {
            if (a.Rank != b.Rank || a.Length != b.Length || a.GetType().GetElementType() != b.GetType().GetElementType())
            {
                return false;
            }

            for (int d = 0; d < a.Rank; d++)
            {
                if (a.GetLength(d) != b.GetLength(d))
                {
                    return false;
                }
            }

            IEnumerator x = a.GetEnumerator();
            IEnumerator y = b.GetEnumerator();
            while (x.MoveNext() && y.MoveNext())
            {
                if (!Same(x.Current, y.Current))
                {
                    return false;
                }
            }

            return true;
        }

        return expected is null ? actual is null : actual is not null && expected.GetType() == actual.GetType() && expected.Equals(actual);
    }

    private static string Show(object? value) => value switch
    {
        null => "null",
        Array a => $"{a.GetType().Name}[{string.Join(",", Enumerable.Range(0, a.Rank).Select(a.GetLength))}] {{{string.Join(", ", a.Cast<object?>().Select(Show))}}}",
        _ => $"{value.GetType().Name} {value}",
    };

    /// <summary>VARIANTs in unmanaged memory for a served object's arguments, freed together.</summary>
    private sealed unsafe class Variants : IDisposable
    {
        private readonly List<nint> _blocks = [];

        /// <summary>The arguments, each a Variant* for the value given (a Variant* is passed through as it is).</summary>
        public ComArguments Of(params object?[] values)
        {
            nint array = Marshal.AllocHGlobal(IntPtr.Size * Math.Max(values.Length, 1));
            _blocks.Add(array);
            for (int i = 0; i < values.Length; i++)
            {
                Variant* v = values[i] is nint pointer ? (Variant*)pointer : Make(values[i]);
                Marshal.WriteIntPtr(array, i * IntPtr.Size, (nint)v);
            }

            return new ComArguments((Variant**)array, values.Length);
        }

        /// <summary>An [in, out] slot: a byref VARIANT onto an empty one, which <see cref="Read"/> answers; passed to <see cref="Of"/> as its pointer.</summary>
        public nint Slot()
        {
            Variant* target = Make(null);
            Variant* reference = Make(null);
            reference->Vt = ExcelVariants.VtByRef | ExcelVariants.VtVariant;
            reference->Pointer = (nint)target;
            return (nint)reference;
        }

        /// <summary>What a slot holds, as the COM marshaller reads it.</summary>
        public object? Read(nint slot) => Marshal.GetObjectForNativeVariant((IntPtr)((Variant*)slot)->Pointer);

        private Variant* Make(object? value)
        {
            nint block = Marshal.AllocHGlobal(24);
            _blocks.Add(block);
            Marshal.Copy(new byte[24], 0, block, 24);
            if (value is not null)
            {
                Marshal.GetNativeVariantForObject(value, block);
            }

            return (Variant*)block;
        }

        public void Dispose()
        {
            foreach (nint block in _blocks)
            {
                Marshal.FreeHGlobal(block);
            }
        }
    }
}
