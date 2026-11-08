using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace JGraph.Deploy;

/// <summary>
/// Everything an Excel client's installer is made from (compiler and SDK plan, stage K17f;
/// <c>compiler.package.excelClientForProductionServer</c>): the files to carry, the client library
/// and the server archive the configuration names, the server's address and limits, the installer's
/// name, version and icon.
/// </summary>
public sealed record ExcelClientInstallerSpec
{
    /// <summary>The installer file's name without its extension (<c>&lt;library&gt;ExcelClientInstaller</c> when not given).</summary>
    public required string InstallerName { get; init; }

    /// <summary>The folder the installer is written to.</summary>
    public required string OutputFolder { get; init; }

    /// <summary>The files and folders to carry, absolute; a folder goes in whole under its own name.</summary>
    public IReadOnlyList<string> Items { get; init; } = [];

    /// <summary>The client library (<c>&lt;name&gt;.dll</c>), whose file name the configuration names as <c>AutomationAddinDll</c>.</summary>
    public required string ClientLibrary { get; init; }

    /// <summary>The server archive (<c>.ctf</c>), whose file name and stem the configuration names.</summary>
    public required string ServerArchive { get; init; }

    /// <summary>The most megabytes of a reply the client accepts (<c>responseSizeLimit</c>).</summary>
    public double MaxResponseSize { get; init; } = 750;

    /// <summary>The seconds the client waits for the server (<c>timeoutSeconds</c>).</summary>
    public int ServerTimeOut { get; init; } = 60;

    /// <summary>The server's address, <c>http://host:port</c>, or empty for the installer to ask (<c>ServerInfo</c>).</summary>
    public string ServerUrl { get; init; } = "";

    /// <summary>A self-signed certificate's PEM for HTTPS, or empty (<c>certificateFile</c>).</summary>
    public string SslCertificate { get; init; } = "";

    /// <summary>The version the installer's version block carries (dotted numbers, at most four).</summary>
    public string Version { get; init; } = "1.0";

    /// <summary>The installer's icon images, largest first; none keeps the template's.</summary>
    public IReadOnlyList<IconImage> Icons { get; init; } = [];

    /// <summary>The prebuilt installer template (<c>jgxlsetup.exe</c>); null refuses the build.</summary>
    public string? TemplatePath { get; init; }
}

/// <summary>
/// An Excel client's installer (compiler and SDK plan, stage K17f), as R2025b's
/// <c>compiler.package.excelClientForProductionServer</c> writes one (<c>recordings/k17_payload.out.txt</c>):
/// a prebuilt program with the payload as its resources. R2025b's <c>mps_excel_addin_installer.exe</c>
/// carries the files zipped (<c>MW_COMPILER_EZDEPLOY_INSTALL_BUNDLE</c>), <c>_config.txt</c>
/// (<c>MW_MPS_EX_ADDIN_CONFIG</c>) and the unzip tools it runs; JGraph's <c>jgxlsetup.exe</c> carries
/// the files as a stored zip (<see cref="BundleResource"/>) and the same configuration text
/// (<see cref="ConfigurationResource"/>; R2025b's keys, byte for byte), unpacks them itself, registers
/// the client library and JGraph's server configuration add-in (for the current user, or for every
/// user when run as an administrator), writes the server's address where the client reads it and
/// leaves an entry in Apps &amp; Features; <c>/quiet</c> installs with the defaults and
/// <c>/uninstall</c> undoes it. The installer's icon is the one given, at the six sizes R2025b's
/// <c>processIcon</c> makes, and its version block the version given, where R2025b's keeps
/// <c>1.0.0.0</c>. The zip's entries have fixed times, so one package is the same bytes twice.
/// </summary>
public static class ExcelClientInstaller
{
    /// <summary>The template's file name, beside the launcher templates.</summary>
    public const string TemplateName = "jgxlsetup.exe";

    /// <summary>JGraph's server configuration add-in for Excel, carried by every installer as R2025b carries <c>ServerConfig.dll</c>.</summary>
    public const string ConfigurationAddInName = "jgxlconfig.dll";

    /// <summary>The RCDATA resource holding the files, a zip with stored entries.</summary>
    public const string BundleResource = "JGRAPH_INSTALL_BUNDLE";

    /// <summary>The RCDATA resource holding the configuration text.</summary>
    public const string ConfigurationResource = "JGRAPH_EXCEL_CLIENT_CONFIG";

    private static readonly DateTimeOffset FixedTime = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly UTF8Encoding Utf8 = new(false);

    /// <summary>The installer template beside this library, else in the JGraph Runtime; null when neither has it.</summary>
    public static string? DefaultTemplatePath => ExcelClient.TemplateBeside(TemplateName);

    /// <summary>The server configuration add-in beside this library, else in the JGraph Runtime; null when neither has it.</summary>
    public static string? DefaultConfigurationAddInPath => ExcelClient.TemplateBeside(ConfigurationAddInName);

    /// <summary>
    /// <c>_config.txt</c> as R2025b's <c>createConfigFile</c> writes it (k17_payload: text.MW_MPS_EX_ADDIN_CONFIG):
    /// <c>CTFFile</c> and <c>CTFArchive</c> the archive's file name, <c>AutomationAddinDll</c> the
    /// library's, <c>responseSizeLimit</c> and <c>timeoutSeconds</c>, <c>AutomationAddin</c> the
    /// archive's stem, then <c>ServerInfo</c> and <c>certificateFile</c> when given; line feeds.
    /// </summary>
    public static string ConfigurationText(ExcelClientInstallerSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        string archive = Path.GetFileName(spec.ServerArchive);
        var s = new StringBuilder();
        s.Append("CTFFile=").Append(archive).Append('\n');
        s.Append("CTFArchive=").Append(archive).Append('\n');
        s.Append("AutomationAddinDll=").Append(Path.GetFileName(spec.ClientLibrary)).Append('\n');
        s.Append("responseSizeLimit=").Append(IntegerText(spec.MaxResponseSize)).Append('\n');
        s.Append("timeoutSeconds=").Append(spec.ServerTimeOut.ToString(CultureInfo.InvariantCulture)).Append('\n');
        s.Append("AutomationAddin=").Append(Path.GetFileNameWithoutExtension(spec.ServerArchive)).Append('\n');
        if (spec.ServerUrl.Length > 0)
        {
            s.Append("ServerInfo=").Append(spec.ServerUrl).Append('\n');
        }

        if (spec.SslCertificate.Length > 0)
        {
            s.Append("certificateFile=").Append(spec.SslCertificate).Append('\n');
        }

        return s.ToString();
    }

    /// <summary>A number under MATLAB's <c>%d</c>: an integer as it is, anything else in the <c>%e</c> form fprintf falls back to.</summary>
    private static string IntegerText(double value)
    {
        if (double.IsNaN(value))
        {
            return "NaN";
        }

        if (double.IsInfinity(value))
        {
            return value > 0 ? "Inf" : "-Inf";
        }

        return value == Math.Floor(value) && Math.Abs(value) < 1e15
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.000000e+00", CultureInfo.InvariantCulture);
    }

    /// <summary>The payload: every item zipped with stored entries, a folder under its own name, the entries' times fixed.</summary>
    public static byte[] Bundle(IReadOnlyList<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (string item in items)
            {
                string full = Path.GetFullPath(item);
                if (Directory.Exists(full))
                {
                    string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(full));
                    foreach (string file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                    {
                        Add(zip, name + "/" + Path.GetRelativePath(full, file).Replace('\\', '/'), file);
                    }
                }
                else
                {
                    Add(zip, Path.GetFileName(full), full);
                }
            }
        }

        return stream.ToArray();
    }

    private static void Add(ZipArchive zip, string entryName, string file)
    {
        ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.NoCompression);
        entry.LastWriteTime = FixedTime;
        using Stream target = entry.Open();
        using FileStream source = File.OpenRead(file);
        source.CopyTo(target);
    }

    /// <summary>
    /// Writes <c>&lt;InstallerName&gt;.exe</c> into the output folder: the template copied and stamped
    /// with the bundle, the configuration, the icon and the version block. Answers its path.
    /// </summary>
    /// <exception cref="FileNotFoundException">The template is missing.</exception>
    public static string Build(ExcelClientInstallerSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        string template = spec.TemplatePath ?? throw new FileNotFoundException($"The Excel client installer template '{TemplateName}' was not found beside JGraph or in the JGraph Runtime.", TemplateName);
        if (!File.Exists(template))
        {
            throw new FileNotFoundException($"The Excel client installer template '{template}' is missing.", template);
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("An Excel client installer can be built only on Windows.");
        }

        string folder = Path.GetFullPath(spec.OutputFolder);
        Directory.CreateDirectory(folder);
        string installer = Path.Combine(folder, spec.InstallerName + ".exe");
        File.Copy(template, installer, overwrite: true);
        ProgramVersion version = ProgramVersion.Parse(spec.Version) ?? ProgramVersion.Default;
        var data = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [BundleResource] = Bundle(spec.Items),
            [ConfigurationResource] = Utf8.GetBytes(ConfigurationText(spec)),
        };
        PeResources.Stamp(installer, spec.Icons, StandaloneProgram.VersionBlock(version), data);
        return installer;
    }
}
