using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using JGraph.Deploy;
using Microsoft.Win32;
using Xunit;

namespace JGraph.Tests.Deploy;

/// <summary>
/// The Excel client for the Production Server (compiler and SDK plan, stage K17f) against what
/// R2025b's client sent and got (<c>recordings/k17_mps.out.txt</c>, the wire log) and what its build
/// wrote (<c>recordings/k17_excel.out.txt</c>): the native client DLL from the template, stamped
/// and driven through its own class factory in this process against a server of the test's own,
/// its requests byte for byte R2025b's, its cells' values from the recorded replies; the module to
/// the character; the installer's configuration and payload.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class ExcelClientTests : IDisposable
{
    private static readonly string Recordings = Path.Combine(FindRepo(), "tools", "matlab-checklist", "compiler-probes", "recordings");
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-k17f-" + Guid.NewGuid().ToString("N"))).FullName;
    private readonly string _addIn = "k17t" + Guid.NewGuid().ToString("N")[..8];
    private readonly List<string> _keys = [];

    public void Dispose()
    {
        foreach (string key in _keys)
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A key already gone.
            }
        }

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The DLL is still mapped; the temp folder clears it.
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

    /// <summary>A file's lines as the recording prints them under <paramref name="key"/> (its <c>  |</c> prefix dropped).</summary>
    internal static string[] Recorded(string file, string key)
    {
        string[] all = File.ReadAllLines(Path.Combine(Recordings, file));
        int at = Array.FindIndex(all, l => l.StartsWith(key + "\t", StringComparison.Ordinal) || l == key);
        Assert.True(at >= 0, key + " is not in the recording");
        return [.. all.Skip(at + 1).TakeWhile(static l => l.StartsWith("  |", StringComparison.Ordinal)).Select(static l => l[3..])];
    }

    /// <summary>The probe's eighteen functions as the client declares them (src/k17).</summary>
    private static readonly ExcelFunction[] Probe =
    [
        new("k17_quiet", [], [], "K17_QUIET Does nothing quietly.", "k17_quiet.m"),
        new("k17_none", [], [], "K17_NONE Takes and gives nothing.", "k17_none.m"),
        new("k17_one", ["x"], ["y"], "K17_ONE Doubles its one input (compiler and SDK plan, stage K17).", "k17_one.m"),
        new("k17_many", ["a", "b", "c"], ["s", "d", "p"], "K17_MANY Sum, difference and product.", "k17_many.m"),
        new("k17_two", ["x"], ["a", "b"], "K17_TWO Two outputs.", "k17_two.m"),
        new("k17_opt", ["a", "b", "c"], ["r"], "K17_OPT Optional inputs.", "k17_opt.m"),
        new("k17_varin", ["varargin"], ["r"], "K17_VARIN Any inputs.", "k17_varin.m"),
        new("k17_varout", ["n"], ["varargout"], "K17_VAROUT Any outputs.", "k17_varout.m"),
        new("k17_both", ["x", "varargin"], ["first", "varargout"], "K17_BOTH Both.", "k17_both.m"),
        new("k17_args", ["x", "k"], ["y"], "K17_ARGS An arguments block.", "k17_args.m"),
        new("k17_noout", ["x"], [], "K17_NOOUT No outputs.", "k17_noout.m"),
        new("k17_err", ["kind"], ["y"], "K17_ERR Errors.", "k17_err.m"),
        new("k17_plot", ["visible"], ["n"], "K17_PLOT Plots.", "k17_plot.m"),
        new("k17_h1", [], ["t"], "K17_H1 Says hello.", "k17_h1.m"),
        new("k17_echo", ["x"], ["d"], "K17_ECHO Echoes.", "k17_echo.m"),
        new("k17_pass", ["x"], ["y"], "K17_PASS Passes.", "k17_pass.m"),
        new("k17_count", [], ["n"], "K17_COUNT Counts.", "k17_count.m"),
        new("k17_make", ["kind"], ["v"], "K17_MAKE Makes.", "k17_make.m"),
    ];

    private ExcelClientOptions Options(string folder, IReadOnlyDictionary<string, ExcelClientSwitches>? switches = null) => new()
    {
        OutputFolder = folder,
        AddInName = _addIn,
        ClassName = "K17T",
        Functions = Probe,
        Switches = switches ?? new Dictionary<string, ExcelClientSwitches>(StringComparer.Ordinal),
        VisualBasicFile = true,
        TemplatePath = ExcelClient.DefaultTemplatePath ?? throw new FileNotFoundException("jgxlmps.dll is not beside the test"),
    };

    /// <summary>Points the client at <paramref name="url"/> through the key it reads, removed when the test ends.</summary>
    private void Configure(string? url, int? timeoutSeconds = null, int? limitMb = null)
    {
        string key = @"Software\JGraph\Excel Client\" + _addIn;
        _keys.Add(key);
        using RegistryKey made = Registry.CurrentUser.CreateSubKey(key);
        if (url is null)
        {
            made.DeleteValue("ServerURL", throwOnMissingValue: false);
        }
        else
        {
            made.SetValue("ServerURL", url);
        }

        if (timeoutSeconds is { } t)
        {
            made.SetValue("timeoutSeconds", t, RegistryValueKind.DWord);
        }

        if (limitMb is { } m)
        {
            made.SetValue("responseSizeLimit", m, RegistryValueKind.DWord);
        }
    }

    private static byte[] Hex(string text) => [.. text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(static h => Convert.ToByte(h, 16))];

    [Fact]
    public void TheModuleIsR2025bsToTheCharacter()
    {
        // fromresults.file.k17mps.bas: eighteen functions, no InitModule, the versionless ProgID, V0.. passed straight.
        string expected = string.Join("\r\n", Recorded("k17_excel.out.txt", "fromresults.file.k17mps.bas")) + "\r\n";
        string module = ExcelVisualBasic.ClientModule("K17Mps", "k17mps.K17Mps", Probe);
        Assert.Equal(expected, module);

        // mpsxl_default.file.k17x.bas: one function.
        string one = string.Join("\r\n", Recorded("k17_excel.out.txt", "mpsxl_default.file.k17x.bas")) + "\r\n";
        Assert.Equal(one, ExcelVisualBasic.ClientModule("K17X", "k17x.K17X", [Probe[2]]));
    }

    [Fact]
    public void TheBuildWritesTheDllTheModuleAndTheWorkbook()
    {
        string folder = Path.Combine(_root, "b");
        var switches = new Dictionary<string, ExcelClientSwitches>(StringComparer.Ordinal) { ["k17_one"] = new(false, false, true, true) };
        ExcelClientBuild built = ExcelClient.Write(Options(folder, switches));
        Assert.True(built.DllWritten);
        Assert.Equal(_addIn + ".dll", built.DllName);
        Assert.Equal([Path.Combine(folder, _addIn + ".dll"), Path.Combine(folder, _addIn + ".bas"), Path.Combine(folder, _addIn + ".xlam")], built.Files);
        Assert.Equal(new[] { "buildresult.json", "includedSupportPackages.txt", _addIn + ".bas", _addIn + ".dll", _addIn + ".xlam", "readme.txt" },
            Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));

        // The type library: the interface and the coclass alone, the functions with their parameters.
        ExcelTypeLibInfo lib = ExcelTypeLib.Read(built.Files[0]);
        Assert.Equal((_addIn, "K17T", "IK17T"), (lib.LibraryName, lib.ClassName, lib.InterfaceName));
        Assert.Equal(ExcelComGuids.ClientClass(_addIn, "K17T", ProgramVersion.Default), lib.ClassId);
        Assert.NotEqual(ExcelComGuids.Class(_addIn, "K17T", ProgramVersion.Default), lib.ClassId);
        Assert.Equal(Probe.Select(static f => f.Name), lib.Functions.Select(static f => f.Name));
        // varargin as V0.. up to 29 arguments in all, as the module passes them (R2025b's .NET class takes params object[]).
        ExcelTypeLibFunction both = lib.Functions.First(static f => f.Name == "k17_both");
        Assert.Equal(29, both.Inputs.Count);
        Assert.Equal(("x", "V0", "V27"), (both.Inputs[0], both.Inputs[1], both.Inputs[^1]));
        Assert.Equal(["first", "varargout"], both.Outputs);
        Assert.Equal(["x"], lib.Functions.First(static f => f.Name == "k17_one").Inputs);

        // The configuration: the add-in, its class and version, each function with its switches.
        string configuration = Encoding.UTF8.GetString(PeResources.Read(built.Files[0], 10, ExcelClient.ConfigurationResource)!);
        Assert.StartsWith($"AddIn={_addIn}\nClass=K17T\nVersion=1.0.0.0\nFunction=k17_quiet:\n", configuration, StringComparison.Ordinal);
        Assert.Contains("\nFunction=k17_one:nan,numdate\n", configuration, StringComparison.Ordinal);
        Assert.Equal(new ExcelClientSwitches(false, false, true, true), ExcelClient.ReadConfiguration(configuration)["k17_one"]);

        // Without -b the module and the workbook are written but not listed (k17_excel: nobas.files, nobas.Files).
        ExcelClientBuild quiet = ExcelClient.Write(Options(Path.Combine(_root, "q")) with { VisualBasicFile = false });
        Assert.Equal([Path.Combine(_root, "q", _addIn + ".dll")], quiet.Files);
        Assert.True(File.Exists(Path.Combine(_root, "q", _addIn + ".bas")));
        Assert.True(File.Exists(Path.Combine(_root, "q", _addIn + ".xlam")));
    }

    [Fact]
    public void TheClientSendsR2025bsRequestsAndAnswersItsReplies()
    {
        string folder = Path.Combine(_root, "c");
        ExcelClientBuild built = ExcelClient.Write(Options(folder));
        using var server = new FakeServer();
        Configure(server.Url + "/k17arch");
        using var client = ComClient.Open(built.Files[0], ExcelComGuids.ClientClass(_addIn, "K17T", ProgramVersion.Default));

        // k17_mps request 3: k17_one(2) and its reply, 4.
        server.Reply(Hex("0a 17 08 09 12 02 01 01 30 00 38 00 ca 01 0a 0a 08 00 00 00 00 00 00 10 40"));
        object?[] outs = client.Call("k17_one", 1, 2.0);
        Assert.Equal(4.0, outs[0]);
        FakeServer.Request request = server.Requests.Single();
        Assert.Equal("POST /k17arch/k17_one HTTP/1.1", request.Line);
        Assert.Equal("application/x-google-protobuf", request.Headers["Content-Type"]);
        Assert.Equal(Hex("08 01 12 13 08 09 12 02 01 01 ca 01 0a 0a 08 00 00 00 00 00 00 00 40"), request.Body);

        // The request bodies of the recorded shapes (k17_mps requests 7, 11, 19, 20, 21, 22, 23, 24, 25, 27, 52).
        foreach ((string function, object?[] inputs, string body) in new (string, object?[], string)[]
        {
            ("k17_varin", [1.0, "a"], "08 01 12 13 08 09 12 02 01 01 ca 01 0a 0a 08 00 00 00 00 00 00 f0 3f 12 0c 08 0b 12 02 01 01 da 01 03 0a 01 61"),
            ("k17_noout", ["x"], "08 00 12 0c 08 0b 12 02 01 01 da 01 03 0a 01 78"),
            ("k17_echo", [new double[,] { { 1, 2 }, { 3, 4 } }], "08 01 12 2b 08 09 12 02 02 02 ca 01 22 0a 20 00 00 00 00 00 00 f0 3f 00 00 00 00 00 00 08 40 00 00 00 00 00 00 00 40 00 00 00 00 00 00 10 40"),
            ("k17_echo", [null], "08 01 12 13 08 09 12 02 01 01 ca 01 0a 0a 08 00 00 00 00 00 00 00 00"),
            ("k17_echo", [Type.Missing], "08 01"),
            ("k17_echo", [new DateTime(2024, 3, 15, 10, 30, 0)], "08 01 12 13 08 09 12 02 01 01 ca 01 0a 0a 08 00 00 00 e0 fc 8f 26 41"),
            ("k17_echo", [true], "08 01 12 0c 08 0a 12 02 01 01 d2 01 03 0a 01 01"),
            ("k17_echo", [7], "08 01 12 0f 08 05 12 02 01 01 aa 01 06 0a 04 07 00 00 00"),
            ("k17_echo", [new object?[] { 1.0, null, 3.0 }], "08 01 12 23 08 09 12 02 01 03 ca 01 1a 0a 18 00 00 00 00 00 00 f0 3f 00 00 00 00 00 00 00 00 00 00 00 00 00 00 08 40"),
            ("k17_echo", ["text"], "08 01 12 0f 08 0b 12 02 01 04 da 01 06 0a 04 74 65 78 74"),
            ("k17_opt", [1.0, null, true], "08 01 12 13 08 09 12 02 01 01 ca 01 0a 0a 08 00 00 00 00 00 00 f0 3f 12 13 08 09 12 02 01 01 ca 01 0a 0a 08 00 00 00 00 00 00 00 00 12 0c 08 0a 12 02 01 01 d2 01 03 0a 01 01"),
        })
        {
            server.Requests.Clear();
            server.Reply(Hex("0a 15 08 0b 12 02 01 06 30 00 38 00 da 01 08 0a 06 6b 31 37 5f 68 31"));
            client.Call(function, function == "k17_noout" ? 0 : 1, inputs);
            Assert.Equal(Hex(body), server.Requests.Single().Body);
        }

        // The mixed range a cell: its elements in column-major order (k17_mps: echo.var2d).
        server.Requests.Clear();
        server.Reply(Hex("0a 15 08 0b 12 02 01 06 30 00 38 00 da 01 08 0a 06 6b 31 37 5f 68 31"));
        client.Call("k17_echo", 1, [new object[,] { { 1.0, "a" }, { true, 4.0 } }]);
        byte[] mixed = server.Requests.Single().Body;
        Assert.Equal(Hex("08 01"), mixed[..2]);
        Assert.Equal(13, mixed[5]); // MATLAB_Array.type CELL
        Assert.Equal(Hex("12 02 02 02"), mixed[6..10]);

        // The replies (k17_mps: make.*, call.*, two.*, request 46's error): what each lands in the cell as.
        foreach ((string reply, object? expected) in new (string, object?)[]
        {
            ("0a 17 08 09 12 02 01 01 30 00 38 00 ca 01 0a 0a 08 00 00 00 00 00 00 0c 40", 3.5),
            ("0a 2f 08 09 12 02 02 02 30 00 38 00 ca 01 22 0a 20 00 00 00 00 00 00 f0 3f 00 00 00 00 00 00 08 40 00 00 00 00 00 00 00 40 00 00 00 00 00 00 10 40", new double[,] { { 1, 2 }, { 3, 4 } }),
            ("0a 27 08 09 12 02 01 03 30 00 38 00 ca 01 1a 0a 18 00 00 00 00 00 00 f0 3f 00 00 00 00 00 00 00 40 00 00 00 00 00 00 08 40", new double[,] { { 1, 2, 3 } }),
            ("0a 14 08 0b 12 02 01 05 30 00 38 00 da 01 07 0a 05 68 65 6c 6c 6f", "hello"),
            ("0a 10 08 01 12 02 01 01 30 00 38 00 8a 01 03 0a 01 fb", (sbyte)-5),
            ("0a 10 08 0a 12 02 01 01 30 00 38 00 d2 01 03 0a 01 01", true),
            ("0a 13 08 0a 12 02 02 02 30 00 38 00 d2 01 06 0a 04 01 00 00 01", new bool[,] { { true, false }, { false, true } }),
            ("0a 17 08 09 12 02 01 01 30 00 38 00 ca 01 0a 0a 08 00 00 00 00 00 00 f8 7f", double.NaN),
            ("0a 17 08 09 12 02 01 01 30 00 38 00 ca 01 0a 0a 08 00 00 00 e0 fc 8f 26 41", 739326.4375),
            ("0a 15 08 13 12 02 01 01 30 00 38 00 b2 02 08 0a 03 73 74 72 12 01 03", "str"),
            ("0a 0a 08 09 12 02 00 00 30 00 38 00", null),
            ("0a 24 08 0d 12 02 01 02 30 00 38 00 e2 01 17 0a 13 08 09 12 02 01 01 ca 01 0a 0a 08 00 00 00 00 00 00 f0 3f 0a 00", new object?[,] { { 1.0, null } }),
            ("0a 1b 08 0c 12 02 01 01 30 00 38 00 ea 01 0e 0a 01 61 0a 01 62 12 02 08 09 12 02 08 0b", "Exception:A MATLAB structure with fields [a,b] cannot be shown in a cell"),
            ("12 31 0a 19 54 6f 6f 20 6d 61 6e 79 20 69 6e 70 75 74 20 61 72 67 75 6d 65 6e 74 73 2e 12 14 4d 41 54 4c 41 42 3a 54 6f 6f 4d 61 6e 79 49 6e 70 75 74 73", "MATLABException:MATLAB:TooManyInputs"),
        })
        {
            server.Reply(Hex(reply));
            object? actual = client.Call("k17_make", 1, ["x"])[0];
            Assert.True(Same(expected, actual), $"reply {reply}: expected {Show(expected)}, got {Show(actual)}");
        }

        // Two outputs (k17_mps: call.K17Mps.k17_two): both slots filled, nargout on the wire the declared count.
        server.Requests.Clear();
        server.Reply(Hex("0a 17 08 09 12 02 01 01 30 00 38 00 ca 01 0a 0a 08 00 00 00 00 00 00 14 40 0a 17 08 09 12 02 01 01 30 00 38 00 ca 01 0a 0a 08 00 00 00 00 00 00 14 c0"));
        object?[] two = client.Call("k17_two", 2, [5.0]);
        Assert.Equal([5.0, -5.0], two);
        Assert.Equal(Hex("08 02 12 13 08 09 12 02 01 01 ca 01 0a 0a 08 00 00 00 00 00 00 14 40"), server.Requests.Single().Body);

        // A failing call: the server's status (WebException:<reason>), no server at all, and no address configured.
        server.Reply([], 404, "Not Found");
        Assert.Equal("WebException:Not Found", client.Call("k17_one", 1, [2.0])[0]);
        Configure("http://127.0.0.1:1/k17arch");
        Assert.Equal("WebException:ConnectFailure", client.Call("k17_one", 1, [2.0])[0]);
        Configure(null);
        Assert.Equal("MATLAB error thrown : MPS server url is not provided for " + _addIn, client.Call("k17_one", 1, [2.0])[0]);
    }

    [Fact]
    public void TheSwitchesConvertAsTheirNamesSay()
    {
        string folder = Path.Combine(_root, "s");
        var switches = new Dictionary<string, ExcelClientSwitches>(StringComparer.Ordinal)
        {
            ["k17_one"] = new(BlankToNaN: true, DateToString: true, NaNToZero: false, NumericToDate: false),
            ["k17_two"] = new(BlankToNaN: false, DateToString: false, NaNToZero: true, NumericToDate: true),
        };
        ExcelClientBuild built = ExcelClient.Write(Options(folder, switches));
        using var server = new FakeServer();
        Configure(server.Url + "/k17arch");
        using var client = ComClient.Open(built.Files[0], ExcelComGuids.ClientClass(_addIn, "K17T", ProgramVersion.Default));

        // A blank goes as NaN, a date as its text (k17_one); a NaN comes back as 0 and a number as a date (k17_two).
        server.Reply(Hex("0a 17 08 09 12 02 01 01 30 00 38 00 ca 01 0a 0a 08 00 00 00 00 00 00 10 40"));
        client.Call("k17_one", 1, [null]);
        Assert.Equal(Hex("08 01 12 13 08 09 12 02 01 01 ca 01 0a 0a 08 00 00 00 00 00 00 f8 7f"), server.Requests.Single().Body);
        server.Requests.Clear();
        server.Reply(Hex("0a 17 08 09 12 02 01 01 30 00 38 00 ca 01 0a 0a 08 00 00 00 00 00 00 10 40"));
        client.Call("k17_one", 1, [new DateTime(2024, 3, 15, 10, 30, 0)]);
        Assert.Equal(11, server.Requests.Single().Body[5]); // CHAR: the date's text
        server.Reply(Hex("0a 17 08 09 12 02 01 01 30 00 38 00 ca 01 0a 0a 08 00 00 00 00 00 00 f8 7f 0a 17 08 09 12 02 01 01 30 00 38 00 ca 01 0a 0a 08 00 00 00 e0 fc 8f 26 41"));
        object?[] outs = client.Call("k17_two", 2, [5.0]);
        Assert.Equal(0.0, outs[0]); // the NaN made 0, which less the bias is no date OLE holds, so the number stays
        Assert.Equal(new DateTime(2024, 3, 15, 10, 30, 0), outs[1]);
        server.Reply(Hex("0a 27 08 09 12 02 01 03 30 00 38 00 ca 01 1a 0a 18 00 00 00 e0 fc 8f 26 41 00 00 00 00 00 00 f8 7f 00 00 00 00 00 00 00 40 0a 17 08 09 12 02 01 01 30 00 38 00 ca 01 0a 0a 08 00 00 00 00 00 00 f0 3f"));
        outs = client.Call("k17_two", 2, [5.0]);
        Assert.Equal(new object[,] { { new DateTime(2024, 3, 15, 10, 30, 0), 0.0, 2.0 } }, outs[0]); // a row of dates and numbers as Variants
        Assert.Equal(1.0, outs[1]);
    }

    [Fact]
    public void TheInstallerCarriesTheConfigurationAndThePayload()
    {
        string folder = Path.Combine(_root, "p");
        Directory.CreateDirectory(folder);
        string dll = Path.Combine(folder, "k17mps.dll");
        string bas = Path.Combine(folder, "k17mps.bas");
        string ctf = Path.Combine(folder, "k17arch.ctf");
        File.WriteAllBytes(dll, [1, 2, 3]);
        File.WriteAllText(bas, "Dim x\r\n");
        File.WriteAllBytes(ctf, [4, 5]);
        var spec = new ExcelClientInstallerSpec
        {
            InstallerName = "k17mpsExcelClientInstaller",
            OutputFolder = Path.Combine(folder, "out"),
            Items = [dll, bas],
            ClientLibrary = dll,
            ServerArchive = ctf,
            ServerUrl = "https://localhost:9911",
            SslCertificate = @"C:\k17\k17cert.pem",
            TemplatePath = ExcelClientInstaller.DefaultTemplatePath,
        };

        // k17_payload: p.cert.*.text.MW_MPS_EX_ADDIN_CONFIG, every key in R2025b's order; p.default without ServerInfo and certificateFile.
        Assert.Equal("CTFFile=k17arch.ctf\nCTFArchive=k17arch.ctf\nAutomationAddinDll=k17mps.dll\nresponseSizeLimit=750\ntimeoutSeconds=60\nAutomationAddin=k17arch\n"
            + "ServerInfo=https://localhost:9911\ncertificateFile=C:\\k17\\k17cert.pem\n", ExcelClientInstaller.ConfigurationText(spec));
        Assert.Equal("CTFFile=k17arch.ctf\nCTFArchive=k17arch.ctf\nAutomationAddinDll=k17mps.dll\nresponseSizeLimit=10\ntimeoutSeconds=5\nAutomationAddin=k17arch\n",
            ExcelClientInstaller.ConfigurationText(spec with { ServerUrl = "", SslCertificate = "", MaxResponseSize = 10, ServerTimeOut = 5 }));
        Assert.Equal("responseSizeLimit=1.500000e+00", ExcelClientInstaller.ConfigurationText(spec with { MaxResponseSize = 1.5 }).Split('\n')[3]);

        // The payload: stored entries by name, a folder under its own name, the same bytes twice.
        byte[] bundle = ExcelClientInstaller.Bundle([dll, bas, folder]);
        Assert.Equal(bundle, ExcelClientInstaller.Bundle([dll, bas, folder]));
        using (var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bundle)))
        {
            Assert.Equal(["k17mps.dll", "k17mps.bas", "p/k17arch.ctf", "p/k17mps.bas", "p/k17mps.dll"], zip.Entries.Select(static e => e.FullName));
            Assert.All(zip.Entries, static e => Assert.Equal(e.Length, e.CompressedLength));
        }

        if (spec.TemplatePath is null)
        {
            return; // The installer template is built from native\jgxlsetup; without it the resources alone are checked.
        }

        string installer = ExcelClientInstaller.Build(spec with { Version = "2.1" });
        Assert.Equal(Path.Combine(folder, "out", "k17mpsExcelClientInstaller.exe"), installer);
        Assert.Equal(ExcelClientInstaller.ConfigurationText(spec), Encoding.UTF8.GetString(PeResources.Read(installer, 10, ExcelClientInstaller.ConfigurationResource)!));
        Assert.Equal(ExcelClientInstaller.Bundle([dll, bas]), PeResources.Read(installer, 10, ExcelClientInstaller.BundleResource));
        Assert.Equal("2.1.0.0", System.Diagnostics.FileVersionInfo.GetVersionInfo(installer).FileVersion);
    }

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

            System.Collections.IEnumerator x = a.GetEnumerator();
            System.Collections.IEnumerator y = b.GetEnumerator();
            while (x.MoveNext() && y.MoveNext())
            {
                if (!Same(x.Current, y.Current))
                {
                    return false;
                }
            }

            return true;
        }

        if (expected is double e && actual is double f)
        {
            return e.Equals(f);
        }

        return expected is null ? actual is null : actual is not null && expected.GetType() == actual.GetType() && expected.Equals(actual);
    }

    private static string Show(object? value) => value switch
    {
        null => "null",
        Array a => $"{a.GetType().Name}[{string.Join(",", Enumerable.Range(0, a.Rank).Select(a.GetLength))}] {{{string.Join(", ", a.Cast<object?>().Select(Show))}}}",
        _ => $"{value.GetType().Name} {value}",
    };

    /// <summary>A Production Server of the test's own: one request answered with the reply queued for it, every request kept.</summary>
    private sealed class FakeServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Queue<(int Status, string Reason, byte[] Body)> _replies = new();
        private readonly object _gate = new();
        private volatile bool _closed;

        public FakeServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Url = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port;
            var thread = new Thread(Serve) { IsBackground = true };
            thread.Start();
        }

        public string Url { get; }

        public List<Request> Requests { get; } = [];

        public sealed record Request(string Line, Dictionary<string, string> Headers, byte[] Body);

        public void Reply(byte[] body, int status = 200, string reason = "OK")
        {
            lock (_gate)
            {
                _replies.Enqueue((status, reason, body));
            }
        }

        private void Serve()
        {
            while (!_closed)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (SocketException)
                {
                    return;
                }

                var worker = new Thread(() => Talk(client)) { IsBackground = true };
                worker.Start();
            }
        }

        private void Talk(TcpClient client)
        {
            using (client)
            using (NetworkStream stream = client.GetStream())
            {
                while (!_closed)
                {
                    var head = new MemoryStream();
                    int last4 = 0;
                    while (true)
                    {
                        int b = stream.ReadByte();
                        if (b < 0)
                        {
                            return;
                        }

                        head.WriteByte((byte)b);
                        last4 = ((last4 << 8) | b) & 0x7FFFFFFF;
                        if (last4 == 0x0D0A0D0A)
                        {
                            break;
                        }
                    }

                    string[] lines = Encoding.ASCII.GetString(head.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string line in lines.Skip(1))
                    {
                        int colon = line.IndexOf(':', StringComparison.Ordinal);
                        if (colon > 0)
                        {
                            headers[line[..colon]] = line[(colon + 1)..].Trim();
                        }
                    }

                    int length = headers.TryGetValue("Content-Length", out string? l) ? int.Parse(l, System.Globalization.CultureInfo.InvariantCulture) : 0;
                    byte[] body = new byte[length];
                    int read = 0;
                    while (read < length)
                    {
                        int n = stream.Read(body, read, length - read);
                        if (n <= 0)
                        {
                            return;
                        }

                        read += n;
                    }

                    (int status, string reason, byte[] reply) = (200, "OK", []);
                    lock (_gate)
                    {
                        Requests.Add(new Request(lines[0], headers, body));
                        if (_replies.Count > 0)
                        {
                            (status, reason, reply) = _replies.Dequeue();
                        }
                    }

                    string response = $"HTTP/1.1 {status} {reason}\r\nContent-Length: {reply.Length}\r\n" + (reply.Length > 0 ? "Content-Type: application/x-google-protobuf\r\n" : "") + "\r\n";
                    stream.Write(Encoding.ASCII.GetBytes(response));
                    stream.Write(reply);
                    stream.Flush();
                }
            }
        }

        public void Dispose()
        {
            _closed = true;
            _listener.Stop();
        }
    }

    /// <summary>The client's COM object made in this process through the DLL's own class factory, called late-bound.</summary>
    private sealed class ComClient : IDisposable
    {
        [ComImport, Guid("00000001-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IClassFactory
        {
            [PreserveSig]
            int CreateInstance(IntPtr outer, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object made);

            [PreserveSig]
            int LockServer(bool lockIt);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetClassObjectFn(ref Guid clsid, ref Guid iid, out IntPtr ppv);

        private readonly nint _module;
        private readonly object _object;

        private ComClient(nint module, object made)
        {
            _module = module;
            _object = made;
        }

        public static ComClient Open(string dll, Guid clsid)
        {
            nint module = NativeLibrary.Load(dll);
            var get = Marshal.GetDelegateForFunctionPointer<GetClassObjectFn>(NativeLibrary.GetExport(module, "DllGetClassObject"));
            Guid factoryId = new("00000001-0000-0000-C000-000000000046");
            Marshal.ThrowExceptionForHR(get(ref clsid, ref factoryId, out IntPtr pointer));
            var factory = (IClassFactory)Marshal.GetObjectForIUnknown(pointer);
            Marshal.Release(pointer);
            Guid dispatchId = new("00020400-0000-0000-C000-000000000046");
            int hr = factory.CreateInstance(IntPtr.Zero, ref dispatchId, out object made);
            Marshal.FinalReleaseComObject(factory);
            Marshal.ThrowExceptionForHR(hr);
            return new ComClient(module, made);
        }

        [ComImport, Guid("00020400-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDispatch
        {
            [PreserveSig]
            int GetTypeInfoCount(out int count);

            [PreserveSig]
            int GetTypeInfo(int index, int lcid, out IntPtr info);

            [PreserveSig]
            int GetIDsOfNames(ref Guid riid, [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] names, int count, int lcid, out int id);

            [PreserveSig]
            int Invoke(int member, ref Guid riid, int lcid, short flags, ref DispParams parameters, IntPtr result, IntPtr excepInfo, IntPtr argErr);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DispParams
        {
            public IntPtr Arguments;
            public IntPtr NamedIds;
            public int Count;
            public int NamedCount;
        }

        /// <summary>
        /// Calls <paramref name="function"/> through IDispatch::Invoke as VBA does: <paramref name="outputs"/>
        /// byref slots after <c>nargout</c>, then <paramref name="inputs"/> (a 2-D array a 2-D SAFEARRAY,
        /// Type.Missing a left-out argument); answers what the slots hold.
        /// </summary>
        public object?[] Call(string function, int outputs, params object?[] inputs)
        {
            var dispatch = (IDispatch)_object;
            Guid none = Guid.Empty;
            int named = dispatch.GetIDsOfNames(ref none, [function], 1, 0, out int id);
            if (named < 0)
            {
                throw new InvalidOperationException($"GetIDsOfNames({function}) answered 0x{named:X8}");
            }

            int[] ids = [id];
            var values = new List<object?>();
            if (outputs > 0)
            {
                values.Add(outputs);
                for (int k = 0; k < outputs; k++)
                {
                    values.Add(null);
                }
            }

            values.AddRange(inputs);
            int count = values.Count;
            // rgvarg runs right to left; a byref slot is a VT_BYREF | VT_VARIANT onto an empty VARIANT of its own.
            IntPtr block = Marshal.AllocHGlobal(24 * Math.Max(count, 1));
            var slots = new IntPtr[outputs];
            try
            {
                for (int i = 0; i < count; i++)
                {
                    IntPtr at = block + 24 * (count - 1 - i);
                    Marshal.Copy(new byte[24], 0, at, 24);
                    if (outputs > 0 && i >= 1 && i <= outputs)
                    {
                        IntPtr target = Marshal.AllocHGlobal(24);
                        Marshal.Copy(new byte[24], 0, target, 24);
                        slots[i - 1] = target;
                        Marshal.WriteInt16(at, (short)(0x4000 | 12)); // VT_BYREF | VT_VARIANT
                        Marshal.WriteIntPtr(at + 8, target);
                    }
                    else if (values[i] is not null)
                    {
                        Marshal.GetNativeVariantForObject(values[i], at);
                    }
                }

                var parameters = new DispParams { Arguments = block, NamedIds = IntPtr.Zero, Count = count, NamedCount = 0 };
                int hr = dispatch.Invoke(ids[0], ref none, 0, 1, ref parameters, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (hr < 0)
                {
                    throw new InvalidOperationException($"Invoke({function}, member {ids[0]}, {count} arguments) answered 0x{hr:X8}");
                }
                var answers = new object?[outputs];
                for (int k = 0; k < outputs; k++)
                {
                    answers[k] = Marshal.GetObjectForNativeVariant(slots[k]);
                }

                return answers;
            }
            finally
            {
                for (int i = 0; i < count; i++)
                {
                    IntPtr at = block + 24 * (count - 1 - i);
                    if (!(outputs > 0 && i >= 1 && i <= outputs))
                    {
                        VariantClear(at);
                    }
                }

                foreach (IntPtr slot in slots)
                {
                    VariantClear(slot);
                    Marshal.FreeHGlobal(slot);
                }

                Marshal.FreeHGlobal(block);
            }
        }

        [DllImport("oleaut32.dll", PreserveSig = true)]
        private static extern int VariantClear(IntPtr variant);

        public void Dispose()
        {
            // The object released, the DLL left mapped: a wrapper the collector has not finalized yet must still find its code.
            Marshal.FinalReleaseComObject(_object);
            _ = _module;
        }
    }
}
