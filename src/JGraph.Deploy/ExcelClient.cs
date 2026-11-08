using System.Text;

namespace JGraph.Deploy;

/// <summary>
/// The four marshalling switches an Excel client for the Production Server applies to one function
/// (R2025b's <c>class{Class:file -replaceBlankWithNaN -convertDateToString -replaceNaNWithZero
/// -convertNumericToDate}</c>; compiler.build.ExcelClientForProductionServerOptions'
/// ReplaceExcelBlankWithNaN, ConvertExcelDateToString, ReplaceNaNToZeroInExcel and
/// ConvertNumericOutToDateInExcel).
/// </summary>
public readonly record struct ExcelClientSwitches(bool BlankToNaN, bool DateToString, bool NaNToZero, bool NumericToDate)
{
    /// <summary>No switch on.</summary>
    public static readonly ExcelClientSwitches None = default;

    /// <summary>Whether any switch is on.</summary>
    public bool Any => BlankToNaN || DateToString || NaNToZero || NumericToDate;

    /// <summary>The mcc switches, in R2025b's writer's order, each as it is spelled on the command line.</summary>
    public IReadOnlyList<string> MccSwitches
    {
        get
        {
            var words = new List<string>();
            if (BlankToNaN)
            {
                words.Add("-replaceBlankWithNaN");
            }

            if (DateToString)
            {
                words.Add("-convertDateToString");
            }

            if (NaNToZero)
            {
                words.Add("-replaceNaNWithZero");
            }

            if (NumericToDate)
            {
                words.Add("-convertNumericToDate");
            }

            return words;
        }
    }

    /// <summary>The words the client DLL's configuration carries for the function (<see cref="ExcelClient.ConfigurationText"/>).</summary>
    public string ConfigurationWords
    {
        get
        {
            var words = new List<string>();
            if (BlankToNaN)
            {
                words.Add("blank");
            }

            if (DateToString)
            {
                words.Add("date");
            }

            if (NaNToZero)
            {
                words.Add("nan");
            }

            if (NumericToDate)
            {
                words.Add("numdate");
            }

            return string.Join(",", words);
        }
    }

    /// <summary>The switches <paramref name="words"/> name, each <c>-switch</c> of the four; any other word is passed over, as R2025b's mcc passes it over (k17_excel: mpsxl_badswitch).</summary>
    public static ExcelClientSwitches FromMccSwitches(IEnumerable<string> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        var switches = None;
        foreach (string word in words)
        {
            switches = word switch
            {
                "-replaceBlankWithNaN" => switches with { BlankToNaN = true },
                "-convertDateToString" => switches with { DateToString = true },
                "-replaceNaNWithZero" => switches with { NaNToZero = true },
                "-convertNumericToDate" => switches with { NumericToDate = true },
                _ => switches,
            };
        }

        return switches;
    }

    /// <summary>The switches a configuration's words name (<see cref="ConfigurationWords"/>).</summary>
    public static ExcelClientSwitches FromConfigurationWords(string words)
    {
        ArgumentNullException.ThrowIfNull(words);
        var switches = None;
        foreach (string word in words.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switches = word switch
            {
                "blank" => switches with { BlankToNaN = true },
                "date" => switches with { DateToString = true },
                "nan" => switches with { NaNToZero = true },
                "numdate" => switches with { NumericToDate = true },
                _ => switches,
            };
        }

        return switches;
    }
}

/// <summary>What an Excel client for the Production Server is built with (compiler and SDK plan, stage K17f).</summary>
public sealed record ExcelClientOptions
{
    /// <summary>The folder the client is written to; made when missing.</summary>
    public required string OutputFolder { get; init; }

    /// <summary>The add-in's name: the DLL's stem, the type library's and the first part of the ProgID (<c>-W mpsxl:&lt;name&gt;</c>).</summary>
    public required string AddInName { get; init; }

    /// <summary>The class's name: the coclass and the second part of the ProgID; the add-in's name when not given.</summary>
    public required string ClassName { get; init; }

    /// <summary>The functions the client calls on the server, in the command line's order.</summary>
    public IReadOnlyList<ExcelFunction> Functions { get; init; } = [];

    /// <summary>The switches of each function, by its name; a function not named has none.</summary>
    public IReadOnlyDictionary<string, ExcelClientSwitches> Switches { get; init; } = new Dictionary<string, ExcelClientSwitches>(StringComparer.Ordinal);

    /// <summary>The client's version (<c>version=</c>), the type library's and the version block's.</summary>
    public ProgramVersion Version { get; init; } = ProgramVersion.Default;

    /// <summary><c>mcc -g</c>: a debug build (nothing differs in the template; kept for the readme).</summary>
    public bool Debug { get; init; }

    /// <summary><c>mcc -b</c>: whether the module and the workbook are among the files answered; both are written either way, as R2025b writes its <c>.bas</c> (k17_excel: nobas.files).</summary>
    public bool VisualBasicFile { get; init; }

    /// <summary>mcc's words, for the readme.</summary>
    public IReadOnlyList<string> MccArguments { get; init; } = [];

    /// <summary>
    /// The prebuilt client template the DLL is copied and stamped from (decision KO9:
    /// <c>jgxlmps.dll</c>, <see cref="ExcelClient.DefaultTemplatePath"/>). Null writes no DLL, and the
    /// build says so.
    /// </summary>
    public string? TemplatePath { get; init; }

    /// <summary>The switches of <paramref name="function"/>, none when it is not named.</summary>
    public ExcelClientSwitches SwitchesOf(string function) => Switches.TryGetValue(function, out ExcelClientSwitches s) ? s : ExcelClientSwitches.None;
}

/// <summary>What <see cref="ExcelClient.Write"/> wrote: the files <c>compiler.build.Results</c> lists (the DLL, then the module and the workbook under <c>-b</c>), the DLL's name, and whether it was written.</summary>
public sealed record ExcelClientBuild(IReadOnlyList<string> Files, string DllName, bool DllWritten);

/// <summary>
/// An Excel client for the Production Server (compiler and SDK plan, stage K17f), as R2025b's
/// <c>mcc -W mpsxl:&lt;name&gt;,&lt;class&gt;</c> builds one (<c>recordings/k17_excel.out.txt</c>, the
/// client builds): an automation add-in whose worksheet functions call the functions of a server
/// archive over HTTP in MATLAB Production Server's protobuf format (stage K16; <c>k17_mps</c>), with
/// no runtime on the client machine. R2025b generates a C# source over MathWorks' .NET client
/// assembly and compiles it with the Framework's <c>csc.exe</c>; JGraph's client is native (decision
/// KO9): a copy of the prebuilt template <c>jgxlmps.dll</c> stamped with the type library
/// <see cref="ExcelTypeLib.WriteClient"/> writes (resource <c>TYPELIB</c> 1), a configuration text
/// naming the add-in and each function's marshalling switches (<c>RT_RCDATA</c>
/// <see cref="ConfigurationResource"/>) and the version block. Beside it: the Visual Basic module
/// (<see cref="ExcelVisualBasic.ClientModule(ExcelClientOptions)"/>), the add-in workbook JGraph writes
/// as an <c>.xlam</c> with no Excel (<see cref="ExcelAddInWorkbook"/>, decision KO12; R2025b tries an
/// <c>.xla</c> through Excel and fails where Excel does not trust its VBA project), <c>readme.txt</c>
/// in JGraph's words, and the two empty-ish files R2025b's postmcc leaves (<c>buildresult.json</c>
/// with no runtime dependency, <c>includedSupportPackages.txt</c>). The DLL is <c>&lt;name&gt;.dll</c>,
/// unversioned as R2025b's is, and its ProgID <c>&lt;name&gt;.&lt;Class&gt;</c>, which the module creates.
/// </summary>
public static class ExcelClient
{
    /// <summary>The template's file name, beside the launcher templates and <c>jgxl.dll</c>.</summary>
    public const string TemplateName = "jgxlmps.dll";

    /// <summary>The RCDATA resource the stamped configuration is written to, which the DLL reads when it loads.</summary>
    public const string ConfigurationResource = "JGRAPH_EXCEL_CLIENT";

    /// <summary>The most functions one client may export: the template serves 520 vtable slots, 7 of them IUnknown's and IDispatch's.</summary>
    public const int MaxFunctions = ExcelAddIn.MaxFunctions;

    private const int ResourceRcData = 10;
    private const int ResourceVersion = 16;
    private const uint VersionFileTypeDll = 2;
    private static readonly UTF8Encoding Utf8 = new(false);

    /// <summary>
    /// The template beside this library (<c>launchers\win64\jgxlmps.dll</c>, where JGraph's build puts
    /// it), else in the JGraph Runtime's <c>launchers\win64</c>; null when neither has it.
    /// </summary>
    public static string? DefaultTemplatePath => TemplateBeside(TemplateName);

    /// <summary>A file in <c>launchers\win64</c> beside this library, else in the JGraph Runtime's; null when neither has it.</summary>
    internal static string? TemplateBeside(string name)
    {
        string beside = Path.Combine(AppContext.BaseDirectory, "launchers", RuntimeLayout.Platform, name);
        if (File.Exists(beside))
        {
            return beside;
        }

        string? runtime = RuntimeLayout.Find()?.LaunchersFolder;
        return runtime is not null && File.Exists(Path.Combine(runtime, name)) ? Path.Combine(runtime, name) : null;
    }

    /// <summary>The DLL's name: <c>&lt;name&gt;.dll</c>, with no version in it (k17_excel: fromresults.files k17mps.dll).</summary>
    public static string DllName(string addIn) => addIn + ".dll";

    /// <summary>The version-independent ProgID the module creates, <c>&lt;name&gt;.&lt;Class&gt;</c>.</summary>
    public static string ProgId(string addIn, string className) => addIn + "." + className;

    /// <summary>
    /// The configuration the DLL carries (<see cref="ConfigurationResource"/>): <c>AddIn=</c>, <c>Class=</c>,
    /// <c>Version=</c>, then one <c>Function=&lt;name&gt;:&lt;words&gt;</c> line per function with the
    /// words of its switches (<see cref="ExcelClientSwitches.ConfigurationWords"/>); line feeds.
    /// </summary>
    public static string ConfigurationText(ExcelClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var s = new StringBuilder();
        s.Append("AddIn=").Append(options.AddInName).Append('\n');
        s.Append("Class=").Append(options.ClassName).Append('\n');
        s.Append("Version=").Append(options.Version.ToString()).Append('\n');
        foreach (ExcelFunction f in options.Functions)
        {
            s.Append("Function=").Append(f.Name).Append(':').Append(options.SwitchesOf(f.Name).ConfigurationWords).Append('\n');
        }

        return s.ToString();
    }

    /// <summary>The functions and their switches a stamped configuration names, by function.</summary>
    public static IReadOnlyDictionary<string, ExcelClientSwitches> ReadConfiguration(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var functions = new Dictionary<string, ExcelClientSwitches>(StringComparer.Ordinal);
        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith("Function=", StringComparison.Ordinal))
            {
                continue;
            }

            string rest = line["Function=".Length..];
            int colon = rest.IndexOf(':', StringComparison.Ordinal);
            string name = colon < 0 ? rest : rest[..colon];
            functions[name] = ExcelClientSwitches.FromConfigurationWords(colon < 0 ? "" : rest[(colon + 1)..]);
        }

        return functions;
    }

    /// <summary>
    /// Writes what mcc writes: the DLL (the template copied and stamped, when one is given), the module,
    /// the workbook, <c>readme.txt</c>, <c>buildresult.json</c> and <c>includedSupportPackages.txt</c>.
    /// The files answered are the DLL and, under <see cref="ExcelClientOptions.VisualBasicFile"/>, the
    /// module and the workbook, where written (k17_excel: fromresults.Files, nobas.Files).
    /// </summary>
    /// <exception cref="InvalidOperationException">The client exports more functions than the template serves.</exception>
    /// <exception cref="FileNotFoundException">The template is missing.</exception>
    public static ExcelClientBuild Write(ExcelClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string folder = Path.GetFullPath(options.OutputFolder);
        Directory.CreateDirectory(folder);
        string dllName = DllName(options.AddInName);
        string dll = Path.Combine(folder, dllName);
        var files = new List<string>();
        bool dllWritten = false;
        if (options.TemplatePath is { } template)
        {
            if (!File.Exists(template))
            {
                throw new FileNotFoundException($"The Excel client template '{template}' is missing.", template);
            }

            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("An Excel client can be built only on Windows.");
            }

            byte[] typeLib = ExcelTypeLib.WriteClient(options);
            File.Copy(template, dll, overwrite: true);
            ProgramVersion stamped = options.Version.Major == 0 ? ProgramVersion.Default : options.Version;
            PeResources.Replace(dll,
            [
                ("TYPELIB", 1, typeLib),
                (ResourceVersion, 1, StandaloneProgram.VersionBlock(stamped, VersionFileTypeDll)),
                (ResourceRcData, ConfigurationResource, Utf8.GetBytes(ConfigurationText(options))),
            ]);
            files.Add(dll);
            dllWritten = true;
        }
        else
        {
            File.Delete(dll);
        }

        // The module and the workbook, written whether or not -b asked for them (R2025b writes the .bas
        // for every client build and lists it under -b alone: nobas.files, nobas.Files).
        string module = ExcelVisualBasic.WriteClient(folder, options);
        string workbook = ExcelAddInWorkbook.Write(folder, options.AddInName, ExcelVisualBasic.ClientModule(options));
        if (options.VisualBasicFile)
        {
            files.Add(module);
            files.Add(workbook);
        }

        File.WriteAllText(Path.Combine(folder, "readme.txt"), Readme(options, dllWritten), Utf8);
        // R2025b's postmcc: an "empty" buildresult.json (the client needs no runtime) and an empty support packages list.
        File.WriteAllText(Path.Combine(folder, BuildLogs.BuildResultFile), "{\r\n\t\"RuntimeDependencies\": []\r\n}\r\n", Utf8);
        File.WriteAllText(Path.Combine(folder, "includedSupportPackages.txt"), "", Utf8);
        return new ExcelClientBuild(files, dllName, dllWritten);
    }

    /// <summary>
    /// readme.txt: JGraph's own text under R2025b's title line and sections (k17_excel:
    /// fromresults.file.readme.txt), with CR LF line ends as R2025b writes them.
    /// </summary>
    public static string Readme(ExcelClientOptions options, bool dllWritten = true)
    {
        ArgumentNullException.ThrowIfNull(options);
        string name = options.AddInName;
        string dll = DllName(name);
        var s = new StringBuilder();
        s.Append(name).Append(" Deployable Archive with Excel Integration\n\n");
        s.Append("Before you can use the functions packaged by the compiler, you need to do the following:\n\n");
        s.Append("* Deploy the server archive (the .ctf compiler.build.productionServerArchive wrote) to a jgraph-mps instance.\n");
        s.Append("* Install the Excel client on the client machine.\n");
        s.Append("* Configure the client with the address of the server instance.\n\n");
        s.Append("1. Deploy the server archive\n\n");
        s.Append("Copy the archive into the instance's auto_deploy folder; jgraph-mps deploys it as it starts or\n")
            .Append("while it runs (jgraph-mps run <instance>). The instance needs no MATLAB Runtime: it is a JGraph\n")
            .Append("Production Server, which carries its own engine.\n\n");
        s.Append("2. Install the Excel client\n\n");
        s.Append("2.1 Using the installer\n");
        s.Append("compiler.package.excelClientForProductionServer writes an installer that copies the client's\n")
            .Append("files, registers ").Append(dll).Append(" and JGraph's server configuration add-in for the\n")
            .Append("current user (for every user when run as an administrator), and asks for the server's address.\n\n");
        s.Append("2.2 Manual installation\n");
        s.Append("In the output folder, you will find the following files:\n\n");
        s.Append("* `").Append(dll).Append("` - the client, a COM automation add-in whose functions call the server").Append(dllWritten ? "" : " (pending: the template jgxlmps.dll was not found)").Append('\n');
        s.Append("* `").Append(name).Append(".bas` - VBA module file created for the add-in\n");
        s.Append("* `").Append(name).Append(".xlam` - Excel add-in workbook holding that module\n");
        s.Append("* `jgxlconfig.dll` - JGraph's server configuration add-in (beside JGraph's launchers), which the installer carries\n\n");
        s.Append("On the client machine, register the client once with `regsvr32 ").Append(dll).Append("` (for the current user\n")
            .Append("when not elevated), then open ").Append(name).Append(".xlam in Excel or import ").Append(name).Append(".bas into a workbook's\n")
            .Append("Visual Basic project. Excel installed by Click-to-Run sees the machine's registrations alone, so for\n")
            .Append("Excel register as an administrator.\n\n");
        s.Append("Set the server's address with the Configure button JGraph's server configuration add-in puts on\n")
            .Append("Excel's ribbon, or write it under HKEY_CURRENT_USER\\Software\\JGraph\\Excel Client\\").Append(name).Append(":\n")
            .Append("ServerURL (http://host:port/<archive>), timeoutSeconds and responseSizeLimit (megabytes).\n\n");
        s.Append("The client needs no JGraph Runtime: it talks to the server over HTTP in MATLAB Production Server's\n")
            .Append("protobuf format, so a MATLAB Production Server instance serves it too.\n");
        return s.ToString().Replace("\n", "\r\n", StringComparison.Ordinal);
    }
}
