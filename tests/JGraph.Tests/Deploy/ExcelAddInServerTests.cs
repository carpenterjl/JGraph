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
/// DLL through its own class factory, as a COM client in a process of its own, and stage K17d's
/// extra phase of it against the COM utility library (<c>jgcomutil.dll</c>) the same way; the
/// utility library's type library; and the measured conversion rules in this process. Nothing here
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
        Dictionary<string, string> mine = RunOracle(dll, "main", "k17_com.lines", "jgraph-k17c-com.lines");
        Dictionary<string, string> recorded = Rows(Path.Combine(Probes, "recordings", "k17_com.out.txt"));
        Assert.Equal("ok", mine.GetValueOrDefault("create"));

        // What JGraph's add-in answers as R2025b's did, row for row: the rows that need no Excel (the Range
        // rows need one) and are no recorded divergence (compare-k17.py names each with its reason).
        string[] same =
        [
            "members", "quiet", "none", "one", .. Names.Where(static n => n != "k17_h1").Select(static n => "help." + n), "help.unknown", "help.empty", "help.case",
            "util.create", "util.IsMCRInitialized.before", "util.IsMCRJVMEnabled", "util.IsMCRInitialized.after", "util.IsMCRInitialized.running",
            "flags.defaults", "flags.sameobject", "flags.clone", "flags.put",
            "two.nargout0", "two.nargout1", "two.nargout2", "two.nargout3", "one.nargout0", "one.nargout2", "many.nargout3", "many.nargout1", "many.arrays",
            "varout.nargout0", "varout.nargout1", "varout.nargout2", "varout.nargout3",
            "both.nargout1", "both.nargout2", "both.nargout3", "both.nargout4", "both.scalarvarargin", "both.missingvarargin", "both.emptyvarargin", "both.threeargs", "both.packed",
            "noout", "noout.array", "noout.missing", "noout.empty", "count.1", "count.2", "count.secondobject", "count.firstagain",
            "varin.none", "varin.one", "varin.two", "varin.two.packed", "varin.missing", "varin.empty", "varin.array", "varin.vararray", "varin.vararray2d", "varin.emptyvararray",
            "varin.packed", "varin.packedarray",
            "opt.0", "opt.1", "opt.2", "opt.3", "opt.emptylast", "opt.dbnull", "opt.errorcell", "args.default", "args.both", "args.missing",
            "in.missing", "in.dbnull", "in.err_na", "in.err_div0", "in.r8", "in.r8_int", "in.r8_neg", "in.r8_big", "in.r8_nan", "in.r8_inf", "in.r4", "in.i4", "in.i2",
            "in.ui1", "in.ui2", "in.ui4", "in.i1", "in.bool_true", "in.bool_false", "in.bstr", "in.bstr_empty", "in.bstr_nonascii", "in.bstr_newline", "in.bstr_number",
            "in.char", "in.date", "in.date_1900", "in.cy", "in.decimal", "in.arr_r8", "in.arr_r8_one", "in.arr_r8_2d", "in.arr_r8_col", "in.arr_r8_row", "in.arr_r8_3d",
            "in.arr_r4", "in.arr_i4", "in.arr_i2", "in.arr_ui1", "in.arr_bool", "in.arr_str", "in.arr_str_2d", "in.arr_date", "in.arr_decimal", "in.arr_var_numbers",
            "in.arr_var_mixed", "in.arr_var_2d", "in.arr_var_2d_mixed", "in.arr_var_2d_strings", "in.arr_var_with_empty", "in.arr_var_with_missing",
            "in.arr_var_with_dbnull", "in.arr_var_with_date", "in.arr_var_ints", "in.arr_var_bools", "in.arr_var_strings", "in.arr_var_2d_with_empty",
            "in.arr_jagged", "in.arr_jagged_ragged", "in.arr_jagged_deep", "in.arr_jagged_r8", "in.arr_1based", "in.arr_1based_mixed",
            "in.struct", "in.struct_array", "in.struct_nested", "in.struct_empty", "in.complex", "in.complex_array", "in.sparse", "in.sparse_empty",
            "in.arg_plain", "in.arg_int8", "in.arg_cell", "in.arg_flagsobject", "in.field",
            "out.double", "out.doublerow", "out.doublecol", "out.doublemat", "out.double3d", "out.doubleneg0", "out.bigint", "out.single", "out.singlemat", "out.int8",
            "out.uint8", "out.uint8mat", "out.int16", "out.uint16", "out.int32", "out.uint32", "out.logical", "out.logicalfalse", "out.logicalmat", "out.logicalempty",
            "out.char", "out.charone", "out.charmat", "out.charcol", "out.charempty", "out.charnonascii", "out.charnewline", "out.cell", "out.cellnested", "out.cellcol",
            "out.cellmat", "out.cellempty", "out.cellofempty", "out.empty", "out.emptyrow", "out.emptycol", "out.nan", "out.nanmat", "out.inf", "out.neginf", "out.datenum",
            "out.struct", "out.structarr", "out.structcol", "out.structnested", "out.structempty", "out.structnofields", "out.complex", "out.complexmat",
            "out.sparse", "out.sparseempty", "out.struct.members", "out.complex.members", "out.sparse.members",
            "out.nosuchkind", "pass.r8", "pass.r4", "pass.i4", "pass.i2", "pass.ui1", "pass.bool_true", "pass.bstr", "pass.bstr_empty", "pass.bstr_nonascii", "pass.char",
            "pass.date", "pass.cy", "pass.decimal", "pass.arr_r8", "pass.arr_r8_2d", "pass.arr_r8_col", "pass.arr_i4", "pass.arr_bool", "pass.arr_str", "pass.arr_var_mixed",
            "pass.arr_var_2d", "pass.arr_var_with_empty", "pass.arr_jagged", "pass.arr_1based", "pass.arr_date", "pass.err_na", "pass.dbnull",
            .. new[] { 0, 1, 2, 3 }.SelectMany(static v => new[] { "r8_2d", "var_2d", "var_1d", "scalar" }.Select(s => $"flag.InputArrayFormat.{v}.{s}")),
            .. new[] { 0, 1, 2, 3 }.Select(static v => $"flag.InputArrayIndFlag.{v}.echo"),
            .. new[] { 0, 1, 2 }.SelectMany(static v => new[] { "doublemat", "cell", "double", "struct", "char" }.Select(s => $"flag.OutputArrayFormat.{v}.{s}")),
            "flag.OutputArrayIndFlag.0", "flag.OutputArrayIndFlag.1", "flag.OutputArrayIndFlag.2",
            .. new[] { 0, 3, 4, 6, 7, 8, 9, 10, 11, 12, 13 }.SelectMany(static t => new[] { "r8", "arr", "bstr", "bool", "i4" }.Select(s => $"flag.CoerceNumericToType.{t}.{s}")),
            .. new[] { 0, 1, 2 }.SelectMany(static v => new[] { "date", "r8" }.Select(s => $"flag.InputDateFormat.{v}.{s}")),
            .. new[] { "False", "True" }.SelectMany(static v => new[] { "r8", "datenum", "char", "small" }.Select(s => $"flag.OutputAsDate.{v}.{s}")),
            .. new[] { 0, 693960, 1 }.SelectMany(static b => new[] { "datein", "dateout" }.Select(s => $"flag.DateBias.{b}.{s}")),
            .. new[] { 0, 1, 2 }.SelectMany(static v => new[] { "arr", "scalar" }.Select(s => $"flag.ReplaceMissing.{v}.{s}")),
            .. new[] { 0, 1, 2, 3 }.SelectMany(static v => new[] { "nan", "nanmat", "inf" }.Select(s => $"flag.ReplaceMatlabNaN.{v}.{s}")),
            "flag.bad.InputArrayFormat", "flag.bad.CoerceNumericToType.set", "flag.bad.DateBias",
            "util.MWPack.three", "util.MWPack.none", "util.MWPack.one", "util.MWPack.withempty",
            "util.MWUnpack.plain", "util.MWUnpack.start1", "util.MWUnpack.fewer", "util.MWUnpack.more", "util.MWUnpack.notpacked", "util.MWUnpack.array",
            "util.MWDate2VariantDate.datenum", "util.MWDate2VariantDate.small", "util.MWDate2VariantDate.array", "util.MWDate2VariantDate.text", "util.MWDate2VariantDate.date",
            "util.CreateRemoteObject.unknown", "util.members",
            "err.id", "err.plain", "err.multi", "err.nonascii", "err.nested", "err.warn", "err.nargs", "err.undefined", "err.index", "err.none", "err.badcount.more",
            "err.badcount.fewer", "err.badcount.fewer.dispatch", "err.unknownmethod", "err.argsblock.negative", "err.argsblock.text", "err.argsblock.toomany",
            "err.alldefault", "err.nargouttext", "err.slotnotbyref", "err.make.unknown", "err.errorinfo.lastwarn", "stale.1.error", "stale.4.ok", "stale.6.notenough",
            "plot.invisible", "plot.invisible.again",
            "terminate", "util.IsMCRInitialized.terminated", "terminate.twice", "util.IsMCRInitialized.reinit",
        ];
        AssertSame(same, recorded, mine);

        Assert.Equal("string 'K17_H1 Says \"hello\" and 'bye' for 5 \\u20ac \\u2014 a na\\u00efve fa\\u00e7ade at 100% of r\\u00e9sum\\u00e9\\path, <tag> & more.'",
            mine["help.k17_h1"]);
    }

    [Fact]
    public async Task ACOMClientSeesTheFlagsAndValueClassesAsR2025bs()
    {
        string dll = await Build(Path.Combine(_root, "b"));
        Dictionary<string, string> mine = RunOracle(dll, "extra", "k17_com_extra.lines", "jgraph-k17d-com-extra.lines");
        Dictionary<string, string> recorded = Rows(Path.Combine(Probes, "recordings", "k17_com_extra.out.txt"));
        Assert.Equal("ok", mine.GetValueOrDefault("create"));
        Assert.Equal("double 4", mine.GetValueOrDefault("x.after"));

        // Every row of stage K17d's extra phase that needs no Excel and is no recorded divergence
        // (compare-k17.py's com_extra list names each divergence with its reason).
        string[] divergent =
        [
            @"^x\.Coerce\.(4|7|8|9|10|12|13)\.naninf$", @"^x\.Coerce\.8\.ui1$", @"^x\.OutputAsDate\.True\.(doublerow|doublemat|mixedrange)$", @"^x\.OutputArrayFormat\.2\.(string|int64|sparselogical)$",
            @"^x\.ReplaceMissing\.\d\.(empty_middle|coerce8)$", @"^x\.(complex\.unset_middle|sparse\.rows_only_middle)$", @"^x\.arg\.replacemissing$", @"^x\.util\.isjvm_type$",
            @"^x\.complex\.(ints|bool|text|mismatch|mismatch_lengths)$", @"^x\.sparse\.(variant_indices|int_values|length_mismatch|text_values|2d_array|logical)$",
            @"^x\.struct\.(dims_zero|dims_zero_echo|init_dims_negative|init_names_number)$", @"^x\.util\.(create_remote_.*|second_util)$",
            @"^x\.utilflags\.(arrayformat_put|arrayformat_put_shares|dataconv_put|dataconv_defaults|arrayformat_defaults|members\.af|members\.dc|put_wrong_type)$",
        ];
        string[] same =
        [
            .. recorded.Keys.Where(k => k.StartsWith("x.", StringComparison.Ordinal)
                && !k.Contains("range", StringComparison.OrdinalIgnoreCase) && !k.Contains("Range", StringComparison.Ordinal)
                && !k.StartsWith("x.util.init", StringComparison.Ordinal) && k != "x.util.call_after_inits" && k != "x.next"
                && !divergent.Any(d => Regex.IsMatch(k, d))),
        ];
        Assert.True(same.Length > 600, $"{same.Length} rows to compare");
        AssertSame(same, recorded, mine);
    }

    [Fact]
    public void TheUtilityLibraryDeclaresMathWorksClassesUnderJGraphsIds()
    {
        string? dll = ExcelAddIn.DefaultUtilityPath;
        Assert.NotNull(dll);
        ExcelTypeLibSummary lib = ExcelTypeLib.ReadLibrary(dll);
        Assert.Equal((ExcelComGuids.UtilityLibraryName, ExcelComGuids.UtilityTypeLib, new ProgramVersion(25, 2, 0, 0)), (lib.Name, lib.Id, lib.Version));
        Assert.Equal(ExcelComGuids.UtilityClasses.Keys.OrderBy(static k => k, StringComparer.Ordinal), lib.Coclasses.Select(static c => c.Name).OrderBy(static k => k, StringComparer.Ordinal));
        foreach (ExcelTypeLibCoclass coclass in lib.Coclasses)
        {
            Assert.Equal(ExcelComGuids.UtilityClasses[coclass.Name], coclass.ClassId);
            Assert.Equal("I" + coclass.Name, coclass.InterfaceName);
        }

        Dictionary<string, ExcelTypeLibCoclass> byName = lib.Coclasses.ToDictionary(static c => c.Name, StringComparer.Ordinal);
        Assert.Equal(ExcelComGuids.UtilInterface, byName["MWUtil"].InterfaceId);
        Assert.Equal(ExcelComGuids.FlagsInterface, byName["MWFlags"].InterfaceId);
        Assert.Equal(ExcelComGuids.ArrayFormatFlagsInterface, byName["MWArrayFormatFlags"].InterfaceId);
        Assert.Equal(ExcelComGuids.DataConversionFlagsInterface, byName["MWDataConversionFlags"].InterfaceId);
        Assert.Equal(ExcelComGuids.StructInterface, byName["MWStruct"].InterfaceId);
        Assert.Equal(ExcelComGuids.FieldInterface, byName["MWField"].InterfaceId);
        Assert.Equal(ExcelComGuids.ComplexInterface, byName["MWComplex"].InterfaceId);
        Assert.Equal(ExcelComGuids.SparseInterface, byName["MWSparse"].InterfaceId);
        Assert.Equal(ExcelComGuids.ArgInterface, byName["MWArg"].InterfaceId);

        // MathWorks' members, in their IDL's order (mwcomutil.h, mwcomtypes.idl).
        Assert.Equal(["MWPack", "MWUnpack", "MWDate2VariantDate", "MWInitApplication", "MWInitApplicationWithMCROptions", "CreateRemoteObject", "IsMCRJVMEnabled", "IsMCRInitialized", "MWTerminateApplication"], byName["MWUtil"].Members);
        Assert.Equal(["ArrayFormatFlags", "DataConversionFlags", "Clone"], byName["MWFlags"].Members);
        Assert.Equal(["InputArrayFormat", "InputArrayIndFlag", "OutputArrayFormat", "OutputArrayIndFlag", "AutoResizeOutput", "TransposeOutput"], byName["MWArrayFormatFlags"].Members);
        Assert.Equal(["CoerceNumericToType", "InputDateFormat", "OutputAsDate", "DateBias", "ReplaceMissing", "ReplaceMatlabNaN"], byName["MWDataConversionFlags"].Members);
        Assert.Equal(["Item", "Initialize", "NumberOfFields", "NumberOfDims", "Dims", "FieldNames", "Clone"], byName["MWStruct"].Members);
        Assert.Equal(["Value", "Name", "MWFlags", "Clone"], byName["MWField"].Members);
        Assert.Equal(["Real", "Imag", "MWFlags", "Clone"], byName["MWComplex"].Members);
        Assert.Equal(["Array", "NumRows", "NumColumns", "RowIndex", "ColumnIndex", "MWFlags", "Clone"], byName["MWSparse"].Members);
        Assert.Equal(["Value", "MWFlags", "Clone"], byName["MWArg"].Members);
        Assert.Equal("JGraphComUtil.MWUtil25.2", ExcelComGuids.UtilityVersionedProgId("MWUtil"));
        Assert.Equal("JGraphComUtil.MWStruct", ExcelComGuids.UtilityProgId("MWStruct"));
    }

    [Fact]
    public void TheConversionFollowsTheMeasuredFlagRules()
    {
        ExcelFlags flags = ExcelFlags.Defaults;
        ComServed owner = flags;
        static string Show(RuntimeValue? v) => v switch
        {
            null => "nothing",
            RuntimeNumeric n => $"{n.ClassName} [{string.Join(" ", n.Dimensions)}] [{string.Join(" ", n.Real.Select(static x => x.ToString("R", System.Globalization.CultureInfo.InvariantCulture)))}]",
            RuntimeLogical l => $"logical [{string.Join(" ", l.Dimensions)}] [{string.Join(" ", l.Elements)}]",
            RuntimeChar c => $"char [{string.Join(" ", c.Dimensions)}] '{c.Characters}'",
            RuntimeCell cell => $"cell [{string.Join(" ", cell.Dimensions)}] {{{string.Join("; ", cell.Elements.Select(Show))}}}",
            _ => v.ClassName,
        };

        // x.Coerce.8.half, .huge: rounded to even, saturated; NaN 0 (R2025b: the minimum, ADR 0317).
        flags.DataConversion.CoerceNumericToType = 8;
        Assert.Equal("int8 [1 8] [0 2 2 0 -2 -2 4 0]", Show(ExcelConversion.FromClient(new double[] { 0.5, 1.5, 2.5, -0.5, -1.5, -2.5, 3.5, 0.49999 }, flags, owner)));
        Assert.Equal("int8 [1 3] [127 -128 0]", Show(ExcelConversion.FromClient(new double[] { 1e10, -1e10, double.NaN }, flags, owner)));
        Assert.Equal("int8 [1 1] [-1]", Show(ExcelConversion.FromClient(true, flags, owner)));
        // x.Coerce.8.var_numbers: a client's array of Variants is not coerced; x.Coerce.8.var_mixed: a cell's elements are.
        Assert.Equal("double [1 2] [1.5 2.5]", Show(ExcelConversion.FromClient(new object[] { 1.5, 2.5 }, flags, owner)));
        Assert.Equal("cell [1 3] {int8 [1 1] [1]; char [1 1] 'a'; int8 [1 1] [-1]}", Show(ExcelConversion.FromClient(new object[] { 1.0, "a", true }, flags, owner)));
        flags.DataConversion.CoerceNumericToType = 9;
        Assert.Equal("uint8 [1 1] [255]", Show(ExcelConversion.FromClient(true, flags, owner)));
        flags.DataConversion.CoerceNumericToType = 4;
        Assert.Equal("cell [1 2] {char [1 3] '1.5'; char [1 4] 'True'}", Show(ExcelConversion.FromClient(new object[] { 1.5, true }, flags, owner)));
        Assert.Equal("char [1 4] 'text'", Show(ExcelConversion.FromClient("text", flags, owner)));
        flags.DataConversion.CoerceNumericToType = 3;
        Assert.Equal("logical [1 3] [False True True]", Show(ExcelConversion.FromClient(new double[] { double.NaN, 1, -2 }, flags, owner)));
        flags.DataConversion.CoerceNumericToType = 0;

        // x.InputArrayFormat.0.var_1d_chars and .1: the AsIs format keeps a cell, the Matrix format a char matrix.
        flags.ArrayFormat.InputArrayFormat = 0;
        Assert.Equal("cell [1 2] {char [1 1] 'a'; char [1 1] 'b'}", Show(ExcelConversion.FromClient(new object[] { "a", "b" }, flags, owner)));
        Assert.Equal("double [1 3] [1 2 3]", Show(ExcelConversion.FromClient(new double[] { 1, 2, 3 }, flags, owner)));
        flags.ArrayFormat.InputArrayFormat = 1;
        Assert.Equal("char [1 2] 'ab'", Show(ExcelConversion.FromClient(new object[] { "a", "b" }, flags, owner)));
        // x.InputArrayIndFlag.*.fmt2.*: the Cell format's nesting by depth.
        flags.ArrayFormat.InputArrayFormat = 2;
        Assert.Equal("cell [1 1] {double [1 1] [5]}", Show(ExcelConversion.FromClient(5.0, flags, owner)));
        Assert.Equal("cell [1 2] {double [1 1] [1]; double [1 1] [2]}", Show(ExcelConversion.FromClient(new double[] { 1, 2 }, flags, owner)));
        flags.ArrayFormat.InputArrayIndFlag = 2;
        Assert.Equal("cell [1 2] {cell [1 1] {double [1 1] [1]}; cell [1 1] {double [1 1] [2]}}", Show(ExcelConversion.FromClient(new double[] { 1, 2 }, flags, owner)));
        Assert.Equal("cell [1 2] {cell [1 1] {cell [1 1] {double [1 1] [1]}}; cell [1 1] {cell [1 1] {double [1 1] [2]}}}", Show(ExcelConversion.FromClient(new object[] { 1.0, 2.0 }, flags, owner)));
        Assert.Equal("cell [1 1] {double [1 1] [5]}", Show(ExcelConversion.FromClient(5.0, flags, owner)));
        flags.ArrayFormat.InputArrayIndFlag = 1;
        flags.ArrayFormat.InputArrayFormat = 1;
        // x.InputArrayIndFlag.1.fmt1.jagged: a nested array of numbers collapses at the depth the flag names.
        Assert.Equal("cell [1 2] {double [1 2] [1 2]; double [1 2] [3 4]}", Show(ExcelConversion.FromClient(new object[] { new object[] { 1.0, 2.0 }, new object[] { 3.0, 4.0 } }, flags, owner)));
        flags.ArrayFormat.InputArrayIndFlag = 0;
        Assert.Equal("cell [1 2] {cell [1 2] {double [1 1] [1]; double [1 1] [2]}; cell [1 2] {double [1 1] [3]; double [1 1] [4]}}", Show(ExcelConversion.FromClient(new object[] { new object[] { 1.0, 2.0 }, new object[] { 3.0, 4.0 } }, flags, owner)));

        // flag.InputDateFormat.1.date, x.InputDateFormat.1.date_midnight: a date's text as oleaut32 writes it.
        flags.DataConversion.InputDateFormat = 1;
        Assert.Equal("char [1 9] '3/16/2024'", Show(ExcelConversion.FromClient(new DateTime(2024, 3, 16), flags, owner)));
        flags.DataConversion.InputDateFormat = 0;
        Assert.Equal("double [1 1] [739326.4375]", Show(ExcelConversion.FromClient(new DateTime(2024, 3, 15, 10, 30, 0), flags, owner)));

        // x.OutputAsDate.True.*: a date when the biased value is one OLE holds, else the biased double.
        flags.DataConversion.OutputAsDate = true;
        Assert.Equal(new DateTime(2024, 3, 15, 10, 30, 0), ExcelConversion.ToClient(RuntimeValue.Scalar(739326.4375), flags, owner));
        Assert.Equal(-693960.0, ExcelConversion.ToClient(RuntimeValue.Scalar(0), flags, owner));
        Assert.Equal(2958466.0, ExcelConversion.ToClient(RuntimeValue.Scalar(3652426), flags, owner));
        Assert.Equal(new DateTime(9999, 12, 31), ExcelConversion.ToClient(RuntimeValue.Scalar(3652425), flags, owner));
        Assert.IsType<DateTime[,]>(ExcelConversion.ToClient(RuntimeNumeric.Of([739326.4375, 739327], 1, 2), flags, owner));
        flags.DataConversion.OutputAsDate = false;

        // flag.ReplaceMatlabNaN.*: NaN nothing, 0 or #N/A (an error value into a Range).
        Assert.Null(ExcelConversion.ToClient(RuntimeValue.Scalar(double.NaN), flags, owner));
        flags.DataConversion.ReplaceMatlabNaN = 1;
        Assert.Equal(0.0, ExcelConversion.ToClient(RuntimeValue.Scalar(double.NaN), flags, owner));
        flags.DataConversion.ReplaceMatlabNaN = 2;
        Assert.Equal("#N/A", ExcelConversion.ToClient(RuntimeValue.Scalar(double.NaN), flags, owner));
        Assert.Equal(ExcelConversion.ExcelErrorNA, Assert.IsType<ErrorWrapper>(ExcelConversion.ToClient(RuntimeValue.Scalar(double.NaN), flags, owner, forRange: true)).ErrorCode);
        flags.DataConversion.ReplaceMatlabNaN = 0;

        // x.OutputArrayFormat.2.*, x.OutputArrayIndFlag.1.fmt2.cell: the Cell format's arrays of Variants and their depth.
        flags.ArrayFormat.OutputArrayFormat = 2;
        Assert.True(Same(new object[,] { { 3.5 } }, ExcelConversion.ToClient(RuntimeValue.Scalar(3.5), flags, owner)));
        Assert.True(Same(new object[,] { { 1.0, null!, null!, 4.0 } }.Length == 4 ? new object?[,] { { 1.0, null }, { null, 4.0 } } : null, ExcelConversion.ToClient(RuntimeNumeric.Of([1, double.NaN, double.NaN, 4], 2, 2), flags, owner)));
        RuntimeCell cell = RuntimeCell.Row(RuntimeValue.Scalar(1), RuntimeValue.Text("two"), RuntimeNumeric.Of([3, 4], 1, 2));
        Assert.True(Same(new object[,] { { 1.0, "two", new double[,] { { 3, 4 } } } }, ExcelConversion.ToClient(cell, flags, owner)));
        flags.ArrayFormat.OutputArrayIndFlag = 1;
        Assert.True(Same(new object[,] { { new object[,] { { 1.0 } }, new object[,] { { "two" } }, new object[,] { { 3.0, 4.0 } } } }, ExcelConversion.ToClient(cell, flags, owner)));
        flags.ArrayFormat.OutputArrayIndFlag = 0;
        flags.ArrayFormat.OutputArrayFormat = 0;

        // out.struct, x.struct.item_index_first, x.struct.item_name_index: an MWStruct's members and Item's order.
        var record = new RuntimeStruct(["a"], [new Dictionary<string, RuntimeValue> { ["a"] = RuntimeValue.Scalar(1) }, new Dictionary<string, RuntimeValue> { ["a"] = RuntimeValue.Scalar(2) }], [1, 2]);
        var made = Assert.IsType<ExcelStruct>(ExcelConversion.ToClient(record, flags, owner));
        Assert.Equal([1, 2], made.Dims);
        using var args = new Variants();
        Assert.Equal(2.0, Assert.IsType<ExcelField>(made.Invoke("Item", ComInvokeKind.PropertyGet, args.Of(2.0, "a"))).Value);
        Assert.Equal(ComCallException.BadIndex, Assert.Throws<ComCallException>(() => made.Invoke("Item", ComInvokeKind.PropertyGet, args.Of("a", 2.0))).Code);
        Assert.Equal(ComCallException.BadIndex, Assert.Throws<ComCallException>(() => made.Invoke("Item", ComInvokeKind.PropertyGet, args.Of("a"))).Code);
        Assert.Equal(ComCallException.TypeMismatch, Assert.Throws<ComCallException>(() => made.Invoke("Item", ComInvokeKind.PropertyGet, args.Of("a", "2"))).Code);
        Assert.True(Assert.Throws<ComCallException>(() => made.Invoke("Item", ComInvokeKind.PropertyGet, args.Of("zz"))).IsSilent);
        Assert.Equal("struct [1 2] {a}", $"{record.ClassName} [{string.Join(" ", made.Dims)}] {{{string.Join(",", made.Names)}}}");

        // x.struct.init_twice, x.struct.init_names_2d, x.struct.dims_scalar: Initialize adds fields and reads its shapes.
        var fresh = new ExcelStruct(0);
        fresh.Invoke("Initialize", ComInvokeKind.Method, args.Of(2.0, new object[,] { { "a", "b" }, { "c", "d" } }));
        Assert.Equal([1, 2], fresh.Dims);
        Assert.Equal(["a", "c", "b", "d"], fresh.Names);
        fresh.Invoke("Initialize", ComInvokeKind.Method, args.Of(new double[] { 1, 1 }, "a"));
        Assert.Equal(4, fresh.Names.Count);
        Assert.Equal("Error initializing array, cannot initialize a 0 length struct array with field names",
            Assert.Throws<ComCallException>(() => new ExcelStruct(0).Invoke("Initialize", ComInvokeKind.Method, args.Of(new double[] { 0, 0 }, "a"))).Message);

        // in.sparse, x.sparse.unsorted, x.sparse.dup, x.sparse.fraction_index: sparse(i, j, v, m, n) from an MWSparse.
        var sparse = new ExcelSparse(0) { NumRows = 2, NumColumns = 2, RowIndex = new double[] { 2, 1, 1.5 }, ColumnIndex = new double[] { 1, 1, 1 }, Array = new double[] { 5, 6, 7 } };
        var value = Assert.IsType<RuntimeSparse>(ExcelConversion.FromClient(sparse, flags, owner));
        Assert.Equal([0, 1], value.RowIndices);
        Assert.Equal([0, 2, 2], value.ColumnStarts);
        Assert.Equal([6.0, 12.0], value.Values);
        Assert.Null(ExcelConversion.FromClient(new ExcelSparse(0) { NumRows = 2 }, flags, owner));

        // x.complex.mismatch_shape, x.complex.imag_only: the parts combined element by element in Real's shape.
        var complex = new ExcelComplex(0) { Real = new double[,] { { 1, 2 }, { 3, 4 } }, Imag = new double[] { 5, 6, 7, 8 } };
        var combined = Assert.IsType<RuntimeNumeric>(ExcelConversion.FromClient(complex, flags, owner));
        Assert.Equal([2, 2], combined.Dimensions);
        Assert.Equal([1.0, 3.0, 2.0, 4.0], combined.Real);
        Assert.Equal([5.0, 6.0, 7.0, 8.0], combined.Imaginary!);
        Assert.Equal("double [1 1] [0]", Show(ExcelConversion.FromClient(new ExcelComplex(0) { Imag = 2.0 }, flags, owner) is RuntimeNumeric im ? new RuntimeNumeric("double", im.Real, null, im.Dimensions) : null));
        Assert.Null(ExcelConversion.FromClient(new ExcelComplex(0), flags, owner));

        // x.util.pack_2d, x.util.unpack_range_two_slots, util.MWUnpack.start1: MWPack spreads a matrix alone row by row
        // and keeps one beside other arguments whole; MWUnpack hands elements from nStartAt.
        var util = new ExcelComUtility(0);
        nint packed = args.Slot();
        util.Invoke("MWPack", ComInvokeKind.Method, args.Of(packed, new double[,] { { 1, 2 }, { 3, 4 } }));
        Assert.True(Same(OneBased(1.0, 2.0, 3.0, 4.0), args.Read(packed)), ExcelAddInServerTests.Show(args.Read(packed)));
        util.Invoke("MWPack", ComInvokeKind.Method, args.Of(packed, new double[,] { { 1, 2 }, { 3, 4 } }, "x"));
        Assert.True(Same(OneBased(new double[,] { { 1, 2 }, { 3, 4 } }, "x"), args.Read(packed)), ExcelAddInServerTests.Show(args.Read(packed)));
        nint a = args.Slot();
        nint b = args.Slot();
        util.Invoke("MWUnpack", ComInvokeKind.Method, args.Of(new object[] { 1.0, "two", 3.0 }, 1, false, a, b));
        Assert.Equal(("two", 3.0), (args.Read(a), args.Read(b)));
        nint d = args.Slot(739326.4375);
        util.Invoke("MWDate2VariantDate", ComInvokeKind.Method, args.Of(d));
        Assert.Equal(45366.4375, args.Read(d));
        Assert.Equal(false, util.Invoke("IsMCRJVMEnabled", ComInvokeKind.Method, args.Of()));
    }

    private static Array OneBased(params object[] values)
    {
        Array made = Array.CreateInstance(typeof(object), [values.Length], [1]);
        for (int k = 0; k < values.Length; k++)
        {
            made.SetValue(values[k], k + 1);
        }

        return made;
    }

    /// <summary>Runs the K17a oracle (<c>k17com.ps1</c>) in a PowerShell of its own against the unregistered DLL through its own class factory and the utility library through its, and answers its rows; the lines are kept beside the temp folder as <paramref name="keep"/> for a look after a failure.</summary>
    private Dictionary<string, string> RunOracle(string dll, string phase, string lines, string keep)
    {
        string clsid = ExcelComGuids.Class("k17xl", "K17Xl", ProgramVersion.Default).ToString("B");
        string script = Path.Combine(Probes, "src", "k17client", "k17com.ps1");
        string utility = ExcelAddIn.DefaultUtilityPath ?? throw new FileNotFoundException("jgcomutil.dll is not beside the engine.");
        string outFile = Path.Combine(_root, lines);
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _root,
        };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-Phase", phase, "-Dll", dll, "-Clsid", clsid, "-NoExcel",
            "-UtilDll", utility, "-UtilPrefix", ExcelComGuids.UtilityLibraryName, "-Out", outFile })
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
            Assert.Fail("The COM client did not finish in five minutes:\n" + output.GetAwaiter().GetResult());
        }

        string console = output.GetAwaiter().GetResult();
        string errors = error.GetAwaiter().GetResult();
        Assert.True(File.Exists(outFile), $"the client wrote nothing (exit {process.ExitCode}):\n{console}\n{errors}");
        File.Copy(outFile, Path.Combine(Path.GetTempPath(), keep), overwrite: true);
        Dictionary<string, string> mine = Rows(outFile);
        Assert.False(mine.ContainsKey("FATAL"), "the client stopped: " + mine.GetValueOrDefault("FATAL") + " at " + mine.GetValueOrDefault("FATAL.where"));
        return mine;
    }

    private static void AssertSame(IReadOnlyList<string> same, Dictionary<string, string> recorded, Dictionary<string, string> mine)
    {
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

        Assert.True(differences.Count == 0, $"{differences.Count} of {same.Count} rows differ:\n" + string.Join("\n", differences));
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
        public nint Slot(object? value = null)
        {
            Variant* target = Make(value);
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
