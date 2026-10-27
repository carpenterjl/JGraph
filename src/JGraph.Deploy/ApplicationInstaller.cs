using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace JGraph.Deploy;

/// <summary>How an installer brings the JGraph Runtime (R2025b's RuntimeDeliveryValues; <see cref="RuntimeDelivery"/> is a program's own, stage K6).</summary>
public enum InstallerRuntimeDelivery
{
    /// <summary>The installer downloads the runtime's MSI from the release while it runs.</summary>
    Web,

    /// <summary>The runtime's MSI is inside the installer.</summary>
    Installer,

    /// <summary>No runtime: the end user has it already.</summary>
    None,
}

/// <summary>What <c>compiler.package.installer</c> writes (R2025b's PackageTypes).</summary>
public enum InstallerPackageType
{
    /// <summary>The installer program itself.</summary>
    Auto,

    /// <summary>A zip holding the installer program and a readme.</summary>
    Zip,
}

/// <summary>
/// Everything an installer is made from (compiler and SDK plan, stage K18): the files to install,
/// the words and images of its pages, the runtime to bring and where to write it. The images are
/// given ready-made: an icon file, a 290-by-112 PNG logo and a 400-by-400 BMP splash, which the
/// builtin resizes from what the options name, as R2025b's packager does.
/// </summary>
public sealed record InstallerSpec
{
    /// <summary>The application's name: the bundle's name in Apps &amp; Features, the MSI's product name, the install folder's default name.</summary>
    public required string ApplicationName { get; init; }

    /// <summary>The installer file's name without its extension.</summary>
    public string InstallerName { get; init; } = "MyAppInstaller";

    /// <summary>The application's version as given (dotted numbers).</summary>
    public string Version { get; init; } = "1.0";

    /// <summary>How the runtime comes.</summary>
    public InstallerRuntimeDelivery RuntimeDelivery { get; init; } = InstallerRuntimeDelivery.Web;

    /// <summary>The installer program, or a zip around it.</summary>
    public InstallerPackageType PackageType { get; init; } = InstallerPackageType.Auto;

    /// <summary>The files and folders to install, absolute, each landing under <c>application\</c> by its own name.</summary>
    public IReadOnlyList<string> Items { get; init; } = [];

    /// <summary>The file or folder under <c>application\</c> the Start menu shortcut opens, relative; empty for no shortcut.</summary>
    public string Shortcut { get; init; } = "";

    /// <summary>The installation folder the installer proposes, as the options give it (<c>%ProgramFiles%\Name</c>).</summary>
    public string DefaultInstallationDir { get; init; } = "";

    /// <summary>The installer's own icon (<c>.ico</c>): the program's and its entry in Apps &amp; Features.</summary>
    public string IconFile { get; init; } = "";

    /// <summary>The icon (<c>.ico</c>) the application's own MSI shows in Apps &amp; Features, or null for none.</summary>
    public string? AddRemoveProgramsIcon { get; init; }

    /// <summary>The logo the installer's pages show, a PNG of 290 by 112 pixels.</summary>
    public string LogoFile { get; init; } = "";

    /// <summary>The splash shown while the installer starts, a BMP of 400 by 400 pixels.</summary>
    public string SplashFile { get; init; } = "";

    /// <summary>The author's name, on the welcome page.</summary>
    public string AuthorName { get; init; } = "";

    /// <summary>The author's email, on the welcome page and as the MSI's contact.</summary>
    public string AuthorEmail { get; init; } = "";

    /// <summary>The author's company: the publisher in Apps &amp; Features.</summary>
    public string AuthorCompany { get; init; } = "";

    /// <summary>One line about the application, on the welcome page and as the MSI's comment.</summary>
    public string Summary { get; init; } = "";

    /// <summary>More about the application, on the welcome page.</summary>
    public string Description { get; init; } = "";

    /// <summary>Notes shown on the welcome page in a box of their own; empty for no box.</summary>
    public string InstallationNotes { get; init; } = "";

    /// <summary>The folder the installer is written to.</summary>
    public required string OutputDir { get; init; }

    /// <summary>The JGraph Runtime version the application runs on.</summary>
    public required string RuntimeVersion { get; init; }

    /// <summary>The runtime's MSI, needed for <see cref="InstallerRuntimeDelivery.Web"/> (its hash and size) and <see cref="InstallerRuntimeDelivery.Installer"/> (embedded).</summary>
    public string? RuntimeInstallerFile { get; init; }

    /// <summary>A runtime-only installer (<c>compiler.runtime.customInstaller</c>): no application, the runtime alone.</summary>
    public bool RuntimeOnly { get; init; }

    /// <summary>The help link the installer's entry in Apps &amp; Features carries, or null.</summary>
    public string? AboutUrl { get; init; }
}

/// <summary>
/// Writes an application's installer with WiX (compiler and SDK plan, stage K18, decision KO7): a
/// bundle, <c>&lt;InstallerName&gt;.exe</c>, whose pages are JGraph's own theme (the logo, the welcome
/// text R2025b's welcome panel shows, the installation notes, the installation folder) and whose
/// chain is the JGraph Runtime's MSI, downloaded from the release or carried inside, then the
/// application's own MSI, which installs the files under <c>&lt;folder&gt;\application</c> as R2025b's
/// installer does and makes the Start menu shortcut. A built installer is inspected with
/// <see cref="MsiDatabase"/> and <c>wix burn extract</c>; it is never run here.
/// </summary>
public static class ApplicationInstaller
{
    /// <summary>The namespace the bundle's and the application MSI's upgrade codes are derived in.</summary>
    public static readonly Guid Namespace = Guid.Parse("3B9A1C58-6E2F-4D7A-8C41-5F0E2A7B9D13");

    /// <summary>The characters R2025b's scrubName makes <c>_</c> in a folder name made from an application's name.</summary>
    private static readonly char[] Scrubbed = ['<', '>', ':', '"', '*', '|', '?', '&', '/', '\\', ' '];

    /// <summary>The readme in a zip package.</summary>
    public const string ZipReadme = "Run {0} to install the application.";

    /// <summary>
    /// Builds the installer <paramref name="spec"/> describes with <paramref name="wix"/> and answers
    /// its path: <c>&lt;OutputDir&gt;\&lt;InstallerName&gt;.exe</c>, or <c>.zip</c> for the zip package type.
    /// </summary>
    /// <exception cref="ArgumentException">The specification is incomplete.</exception>
    /// <exception cref="WixException">WiX refused it.</exception>
    public static string Build(InstallerSpec spec, WixTool wix, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(wix);
        if (spec.RuntimeDelivery != InstallerRuntimeDelivery.None && string.IsNullOrEmpty(spec.RuntimeInstallerFile))
        {
            throw new ArgumentException("The runtime's MSI is needed for a web or an offline installer.", nameof(spec));
        }

        if (spec.RuntimeOnly && spec.RuntimeDelivery == InstallerRuntimeDelivery.None)
        {
            throw new ArgumentException("A runtime-only installer needs a runtime delivery.", nameof(spec));
        }

        string? bal = wix.Extension(WixTool.BootstrapperExtension)
            ?? throw new WixException($"The WiX extension {WixTool.BootstrapperExtension} was not found beside {wix.Folder} or in the package cache.", "");
        Directory.CreateDirectory(spec.OutputDir);
        string scratch = Path.Combine(Path.GetTempPath(), "jgraph-installer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            if (!spec.RuntimeOnly)
            {
                File.WriteAllText(Path.Combine(scratch, "application.wxs"), ApplicationWxs(spec), Utf8);
                log?.Invoke("application.wxs");
                wix.Run(["build", "application.wxs", "-arch", "x64", "-pdbtype", "none", "-o", "application.msi"], scratch);
            }

            bool notes = spec.InstallationNotes.Length > 0;
            File.WriteAllText(Path.Combine(scratch, "theme.xml"), ThemeXml(notes, spec.RuntimeOnly), Utf8);
            File.WriteAllText(Path.Combine(scratch, "theme.wxl"), ThemeWxl(spec), Utf8);
            if (notes)
            {
                File.WriteAllText(Path.Combine(scratch, "notes.rtf"), Rtf(spec.InstallationNotes), Encoding.ASCII);
            }

            File.WriteAllText(Path.Combine(scratch, "bundle.wxs"), BundleWxs(spec, notes), Utf8);
            log?.Invoke("bundle.wxs");
            string exe = Path.Combine(scratch, spec.InstallerName + ".exe");
            wix.Run(["build", "bundle.wxs", "-ext", bal, "-pdbtype", "none", "-o", exe], scratch);

            string target = Path.Combine(spec.OutputDir, spec.InstallerName + (spec.PackageType == InstallerPackageType.Zip ? ".zip" : ".exe"));
            if (spec.PackageType == InstallerPackageType.Zip)
            {
                File.Delete(target);
                using ZipArchive zip = ZipFile.Open(target, ZipArchiveMode.Create);
                zip.CreateEntryFromFile(exe, spec.InstallerName + ".exe", CompressionLevel.Optimal);
                ZipArchiveEntry readme = zip.CreateEntry("readme.txt", CompressionLevel.Optimal);
                using StreamWriter writer = new(readme.Open(), Utf8);
                writer.Write(string.Format(CultureInfo.InvariantCulture, ZipReadme, spec.InstallerName + ".exe"));
            }
            else
            {
                File.Copy(exe, target, overwrite: true);
            }

            return target;
        }
        finally
        {
            try
            {
                Directory.Delete(scratch, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    // --- Names and versions ----------------------------------------------------------------

    /// <summary>R2025b's scrubName: the bad folder characters, the separators and blanks made <c>_</c>.</summary>
    public static string ScrubName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return new string([.. name.Select(c => Scrubbed.Contains(c) ? '_' : c)]);
    }

    /// <summary>
    /// Whether <paramref name="version"/> is a version R2025b's InstallerOptions takes: split at the
    /// dots, every part reads as a number the way <c>str2double</c> reads it (blanks around it and
    /// thousands separators allowed, so <c>1,2</c> is 12; an empty part is not a number).
    /// </summary>
    public static bool IsVersionText(string version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return version.Split('.').All(part => VersionPart(part) is not null);
    }

    /// <summary>A version's part as <c>str2double</c> reads it, or null when it is not a number.</summary>
    private static double? VersionPart(string part)
    {
        string text = part.Trim().Replace(",", "", StringComparison.Ordinal);
        if (text.Length == 0)
        {
            return null;
        }

        if (text.Equals("inf", StringComparison.OrdinalIgnoreCase) || text.Equals("+inf", StringComparison.OrdinalIgnoreCase))
        {
            return double.PositiveInfinity;
        }

        if (text.Equals("-inf", StringComparison.OrdinalIgnoreCase))
        {
            return double.NegativeInfinity;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && !double.IsNaN(value) ? value : null;
    }

    /// <summary>
    /// The version a Windows Installer package carries for <paramref name="version"/>: its first
    /// three parts as whole numbers, within what ProductVersion allows (255.255.65535), the rest dropped.
    /// </summary>
    public static string MsiVersion(string version)
    {
        long[] parts = NumericParts(version, 3);
        return $"{Math.Min(parts[0], 255)}.{Math.Min(parts[1], 255)}.{Math.Min(parts[2], 65535)}";
    }

    /// <summary>The version the bundle carries for <paramref name="version"/>: its first four parts as whole numbers.</summary>
    public static string BundleVersion(string version)
    {
        long[] parts = NumericParts(version, 4);
        return string.Join('.', parts);
    }

    private static long[] NumericParts(string version, int count)
    {
        string[] given = version.Split('.');
        long[] parts = new long[count];
        for (int i = 0; i < count && i < given.Length; i++)
        {
            double value = VersionPart(given[i]) ?? 0;
            parts[i] = double.IsInfinity(value) ? (value > 0 ? uint.MaxValue : 0) : (long)Math.Max(0, Math.Min(Math.Floor(value), uint.MaxValue));
        }

        return parts;
    }

    /// <summary>The upgrade code of the application MSI of <paramref name="applicationName"/>: derived from the name, so a rebuild upgrades the install before it.</summary>
    public static string PackageUpgradeCode(string applicationName) =>
        RuntimeInstaller.NameBasedGuid(Namespace, "JGraph application " + applicationName).ToString("D").ToUpperInvariant();

    /// <summary>The upgrade code of the bundle of <paramref name="applicationName"/>.</summary>
    public static string BundleUpgradeCode(string applicationName) =>
        RuntimeInstaller.NameBasedGuid(Namespace, "JGraph installer " + applicationName).ToString("D").ToUpperInvariant();

    /// <summary>The publisher the installer names: the author's company, else the author, else the application.</summary>
    public static string Manufacturer(InstallerSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return spec.AuthorCompany.Length > 0 ? spec.AuthorCompany : spec.AuthorName.Length > 0 ? spec.AuthorName : spec.ApplicationName;
    }

    /// <summary>
    /// R2025b's welcome panel text: the application's name and version, then the summary on the next
    /// line, the description after a blank line, the author's name and email each on a line.
    /// </summary>
    public static string WelcomeText(InstallerSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var text = new StringBuilder();
        text.Append(spec.ApplicationName).Append(' ').Append(spec.Version);
        if (spec.Summary.Length > 0)
        {
            text.Append('\n').Append(spec.Summary);
        }

        if (spec.Description.Length > 0)
        {
            text.Append("\n\n").Append(spec.Description);
        }

        if (spec.AuthorName.Length > 0)
        {
            text.Append('\n').Append(spec.AuthorName);
        }

        if (spec.AuthorEmail.Length > 0)
        {
            text.Append('\n').Append(spec.AuthorEmail);
        }

        return text.ToString();
    }

    /// <summary>
    /// The bundle's install folder for the options' <c>DefaultInstallationDir</c>: a Windows folder
    /// variable (<c>%ProgramFiles%</c> and the like) as Burn's own, any other environment variable as
    /// Burn reads one, an absolute path as it is, a relative one under Program Files.
    /// </summary>
    public static string InstallFolderValue(string defaultInstallationDir)
    {
        ArgumentNullException.ThrowIfNull(defaultInstallationDir);
        string text = defaultInstallationDir.Trim();
        if (text.StartsWith('%') && text.IndexOf('%', 1) is int close && close > 1)
        {
            string variable = text[1..close];
            string rest = text[(close + 1)..].TrimStart('\\', '/');
            string folder = variable.ToUpperInvariant() switch
            {
                "PROGRAMFILES" or "PROGRAMW6432" => "[ProgramFiles64Folder]",
                "PROGRAMFILES(X86)" => "[ProgramFilesFolder]",
                "LOCALAPPDATA" => "[LocalAppDataFolder]",
                "APPDATA" => "[AppDataFolder]",
                "PROGRAMDATA" or "ALLUSERSPROFILE" => "[CommonAppDataFolder]",
                "USERPROFILE" => "[%USERPROFILE]\\",
                "SYSTEMDRIVE" => "[WindowsVolume]",
                "TEMP" or "TMP" => "[TempFolder]",
                _ => $"[%{variable}]\\",
            };
            return folder + rest;
        }

        if (Path.IsPathRooted(text))
        {
            return text;
        }

        return "[ProgramFiles64Folder]" + text.TrimStart('\\', '/');
    }

    // --- The application MSI ----------------------------------------------------------------

    /// <summary>The WiX source of the application's MSI.</summary>
    public static string ApplicationWxs(InstallerSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var wxs = new StringBuilder();
        wxs.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
        wxs.Append("<!-- The application's own package, written by JGraph's compiler.package.installer (compiler and SDK plan, stage K18). -->\n");
        wxs.Append("<Wix xmlns=\"http://wixtoolset.org/schemas/v4/wxs\">\n");
        wxs.Append($"  <Package Name=\"{X(spec.ApplicationName)}\" Manufacturer=\"{X(Manufacturer(spec))}\" Version=\"{MsiVersion(spec.Version)}\"");
        wxs.Append($" UpgradeCode=\"{PackageUpgradeCode(spec.ApplicationName)}\" Scope=\"perMachine\" Compressed=\"yes\">\n");
        wxs.Append("    <MediaTemplate EmbedCab=\"yes\" />\n");
        wxs.Append("    <MajorUpgrade AllowDowngrades=\"yes\" />\n");
        if (spec.AddRemoveProgramsIcon is { Length: > 0 } arp)
        {
            wxs.Append($"    <Icon Id=\"ApplicationIcon\" SourceFile=\"{X(arp)}\" />\n");
            wxs.Append("    <Property Id=\"ARPPRODUCTICON\" Value=\"ApplicationIcon\" />\n");
        }

        if (spec.Summary.Length > 0)
        {
            wxs.Append($"    <Property Id=\"ARPCOMMENTS\" Value=\"{X(spec.Summary)}\" />\n");
        }

        if (spec.AuthorEmail.Length > 0)
        {
            wxs.Append($"    <Property Id=\"ARPCONTACT\" Value=\"{X(spec.AuthorEmail)}\" />\n");
        }

        wxs.Append("    <Property Id=\"ARPNOMODIFY\" Value=\"1\" />\n");
        wxs.Append("    <StandardDirectory Id=\"ProgramFiles64Folder\">\n");
        wxs.Append($"      <Directory Id=\"INSTALLFOLDER\" Name=\"{X(ScrubName(spec.ApplicationName))}\">\n");
        wxs.Append("        <Directory Id=\"ApplicationFolder\" Name=\"application\" />\n");
        wxs.Append("      </Directory>\n");
        wxs.Append("    </StandardDirectory>\n");
        wxs.Append("    <ComponentGroup Id=\"ApplicationFiles\" Directory=\"ApplicationFolder\">\n");
        foreach (string item in spec.Items)
        {
            string full = Path.GetFullPath(item);
            if (Directory.Exists(full))
            {
                wxs.Append($"      <Files Include=\"{X(Path.Combine(full, "**"))}\" Subdirectory=\"{X(Path.GetFileName(Path.TrimEndingDirectorySeparator(full)))}\" />\n");
            }
            else
            {
                wxs.Append($"      <Component Guid=\"*\"><File Source=\"{X(full)}\" Name=\"{X(Path.GetFileName(full))}\" /></Component>\n");
            }
        }

        wxs.Append("    </ComponentGroup>\n");
        if (spec.Shortcut.Length > 0)
        {
            wxs.Append("    <StandardDirectory Id=\"ProgramMenuFolder\">\n");
            wxs.Append($"      <Directory Id=\"ApplicationMenuFolder\" Name=\"{X(spec.ApplicationName)}\">\n");
            wxs.Append("        <Component Id=\"StartMenuShortcut\" Guid=\"*\" Bitness=\"always64\">\n");
            wxs.Append($"          <Shortcut Id=\"ApplicationShortcut\" Name=\"{X(spec.ApplicationName)}\" Target=\"[ApplicationFolder]{X(spec.Shortcut)}\" WorkingDirectory=\"ApplicationFolder\" />\n");
            wxs.Append("          <RemoveFolder Id=\"RemoveApplicationMenuFolder\" On=\"uninstall\" />\n");
            wxs.Append($"          <RegistryValue Root=\"HKMU\" Key=\"SOFTWARE\\JGraph\\Installed Applications\\{X(spec.ApplicationName)}\" Name=\"Shortcut\" Type=\"string\" Value=\"{X(spec.Shortcut)}\" KeyPath=\"yes\" />\n");
            wxs.Append("        </Component>\n");
            wxs.Append("      </Directory>\n");
            wxs.Append("    </StandardDirectory>\n");
        }

        wxs.Append($"    <Feature Id=\"Main\" Title=\"{X(spec.ApplicationName)}\" Level=\"1\">\n");
        wxs.Append("      <ComponentGroupRef Id=\"ApplicationFiles\" />\n");
        if (spec.Shortcut.Length > 0)
        {
            wxs.Append("      <ComponentRef Id=\"StartMenuShortcut\" />\n");
        }

        wxs.Append("    </Feature>\n");
        wxs.Append("  </Package>\n");
        wxs.Append("</Wix>\n");
        return wxs.ToString();
    }

    // --- The bundle --------------------------------------------------------------------------

    /// <summary>The WiX source of the bundle: the standard bootstrapper application over JGraph's theme, the runtime's MSI and the application's.</summary>
    public static string BundleWxs(InstallerSpec spec, bool notes)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var wxs = new StringBuilder();
        wxs.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
        wxs.Append("<!-- The installer, written by JGraph's compiler.package.installer (compiler and SDK plan, stage K18). -->\n");
        wxs.Append("<Wix xmlns=\"http://wixtoolset.org/schemas/v4/wxs\" xmlns:bal=\"http://wixtoolset.org/schemas/v4/wxs/bal\">\n");
        string name = spec.RuntimeOnly ? $"JGraph Runtime {spec.RuntimeVersion}" : spec.ApplicationName;
        wxs.Append($"  <Bundle Name=\"{X(name)}\" Manufacturer=\"{X(spec.RuntimeOnly ? "JGraph contributors" : Manufacturer(spec))}\"");
        wxs.Append($" Version=\"{BundleVersion(spec.RuntimeOnly ? spec.RuntimeVersion : spec.Version)}\" UpgradeCode=\"{BundleUpgradeCode(spec.RuntimeOnly ? spec.InstallerName : spec.ApplicationName)}\"");
        wxs.Append(" DisableModify=\"yes\"");
        if (spec.IconFile.Length > 0)
        {
            wxs.Append($" IconSourceFile=\"{X(spec.IconFile)}\"");
        }

        if (spec.SplashFile.Length > 0)
        {
            wxs.Append($" SplashScreenSourceFile=\"{X(spec.SplashFile)}\"");
        }

        if (spec.AboutUrl is { Length: > 0 } about)
        {
            wxs.Append($" AboutUrl=\"{X(about)}\"");
        }

        wxs.Append(">\n");
        wxs.Append("    <BootstrapperApplication>\n");
        wxs.Append($"      <bal:WixStandardBootstrapperApplication Theme=\"{(notes ? "rtfLicense" : "hyperlinkLicense")}\" ThemeFile=\"theme.xml\" LocalizationFile=\"theme.wxl\"");
        wxs.Append(notes ? " LicenseFile=\"notes.rtf\"" : " LicenseUrl=\"\"");
        if (spec.LogoFile.Length > 0)
        {
            wxs.Append($" LogoFile=\"{X(spec.LogoFile)}\"");
        }

        wxs.Append($" ShowVersion=\"no\" SuppressOptionsUI=\"{(spec.RuntimeOnly ? "yes" : "no")}\" />\n");
        wxs.Append("    </BootstrapperApplication>\n");
        if (!spec.RuntimeOnly)
        {
            wxs.Append($"    <Variable Name=\"InstallFolder\" Type=\"formatted\" Value=\"{X(InstallFolderValue(spec.DefaultInstallationDir))}\" bal:Overridable=\"yes\" />\n");
        }

        wxs.Append("    <Chain>\n");
        if (spec.RuntimeDelivery != InstallerRuntimeDelivery.None)
        {
            string msi = Path.GetFullPath(spec.RuntimeInstallerFile!);
            wxs.Append($"      <MsiPackage Id=\"JGraphRuntime\" SourceFile=\"{X(msi)}\" Visible=\"yes\" Permanent=\"yes\"");
            wxs.Append(spec.RuntimeDelivery == InstallerRuntimeDelivery.Installer
                ? " Compressed=\"yes\" />\n"
                : $" Compressed=\"no\" DownloadUrl=\"{X(RuntimeInstaller.ReleaseUrl(spec.RuntimeVersion))}\" />\n");
        }

        if (!spec.RuntimeOnly)
        {
            wxs.Append("      <MsiPackage Id=\"Application\" SourceFile=\"application.msi\" Compressed=\"yes\" Visible=\"no\">\n");
            wxs.Append("        <MsiProperty Name=\"INSTALLFOLDER\" Value=\"[InstallFolder]\" />\n");
            wxs.Append("      </MsiPackage>\n");
        }

        wxs.Append("    </Chain>\n");
        wxs.Append("  </Bundle>\n");
        wxs.Append("</Wix>\n");
        return wxs.ToString();
    }

    /// <summary>
    /// JGraph's theme for the standard bootstrapper application, after WiX's own hyperlink theme: the
    /// logo and the application's name at the top; a welcome page with R2025b's welcome text and,
    /// when there are notes, a box of them; the options page with the installation folder; and
    /// WiX's progress, modify, success, failure and help pages.
    /// </summary>
    public static string ThemeXml(bool notes, bool runtimeOnly)
    {
        var xml = new StringBuilder();
        xml.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
        xml.Append("<Theme xmlns=\"http://wixtoolset.org/schemas/v4/thmutil\">\n");
        xml.Append("  <Font Id=\"0\" Height=\"-12\" Weight=\"500\" Foreground=\"windowtext\" Background=\"window\">Segoe UI</Font>\n");
        xml.Append("  <Font Id=\"1\" Height=\"-24\" Weight=\"500\" Foreground=\"windowtext\">Segoe UI</Font>\n");
        xml.Append("  <Font Id=\"2\" Height=\"-22\" Weight=\"500\" Foreground=\"graytext\">Segoe UI</Font>\n");
        xml.Append("  <Font Id=\"3\" Height=\"-12\" Weight=\"500\" Foreground=\"windowtext\" Background=\"window\">Segoe UI</Font>\n");
        xml.Append("  <Window Width=\"520\" Height=\"400\" HexStyle=\"100a0000\" FontId=\"0\" Caption=\"#(loc.Caption)\">\n");
        xml.Append("    <ImageControl X=\"11\" Y=\"11\" Width=\"290\" Height=\"112\" ImageFile=\"logo.png\" Visible=\"yes\" />\n");
        xml.Append("    <Label X=\"312\" Y=\"11\" Width=\"-11\" Height=\"112\" FontId=\"1\" Visible=\"yes\" DisablePrefix=\"yes\">#(loc.Title)</Label>\n");
        xml.Append("    <Page Name=\"Help\">\n");
        xml.Append("      <Label X=\"11\" Y=\"135\" Width=\"-11\" Height=\"30\" FontId=\"2\" DisablePrefix=\"yes\">#(loc.HelpHeader)</Label>\n");
        xml.Append("      <Label X=\"11\" Y=\"167\" Width=\"-11\" Height=\"-45\" FontId=\"3\" DisablePrefix=\"yes\">#(loc.HelpText)</Label>\n");
        xml.Append("      <Button Name=\"HelpCloseButton\" X=\"-11\" Y=\"-11\" Width=\"75\" Height=\"23\" TabStop=\"yes\" FontId=\"0\">\n");
        xml.Append("        <Text>#(loc.HelpCloseButton)</Text>\n        <CloseWindowAction />\n      </Button>\n");
        xml.Append("    </Page>\n");
        xml.Append("    <Page Name=\"Loading\">\n");
        xml.Append("      <Label X=\"11\" Y=\"135\" Width=\"-11\" Height=\"30\" FontId=\"2\" DisablePrefix=\"yes\" Visible=\"no\" Name=\"CheckingForUpdatesLabel\" />\n");
        xml.Append("    </Page>\n");
        xml.Append("    <Page Name=\"Install\">\n");
        xml.Append($"      <Label X=\"11\" Y=\"135\" Width=\"-11\" Height=\"{(notes ? 100 : 215)}\" FontId=\"3\" DisablePrefix=\"yes\">#(loc.InstallMessage)</Label>\n");
        if (notes)
        {
            xml.Append("      <Richedit Name=\"EulaRichedit\" X=\"11\" Y=\"240\" Width=\"-11\" Height=\"-45\" TabStop=\"yes\" FontId=\"0\" HexStyle=\"00800000\" />\n");
        }

        if (!runtimeOnly)
        {
            xml.Append("      <Button Name=\"OptionsButton\" X=\"-171\" Y=\"-11\" Width=\"75\" Height=\"23\" TabStop=\"yes\" FontId=\"0\" VisibleCondition=\"NOT WixStdBASuppressOptionsUI\">\n");
            xml.Append("        <Text>#(loc.InstallOptionsButton)</Text>\n        <ChangePageAction Page=\"Options\" />\n      </Button>\n");
        }

        xml.Append("      <Button Name=\"InstallButton\" X=\"-91\" Y=\"-11\" Width=\"75\" Height=\"23\" TabStop=\"yes\" FontId=\"0\" ElevationShieldCondition=\"NOT WixBundleElevated\">#(loc.InstallInstallButton)</Button>\n");
        xml.Append("      <Button Name=\"InstallCancelButton\" X=\"-11\" Y=\"-11\" Width=\"75\" Height=\"23\" TabStop=\"yes\" FontId=\"0\">\n");
        xml.Append("        <Text>#(loc.InstallCancelButton)</Text>\n        <CloseWindowAction />\n      </Button>\n");
        xml.Append("    </Page>\n");
        if (!runtimeOnly)
        {
            xml.Append("    <Page Name=\"Options\">\n");
            xml.Append("      <Label X=\"11\" Y=\"135\" Width=\"-11\" Height=\"30\" FontId=\"2\" DisablePrefix=\"yes\">#(loc.OptionsHeader)</Label>\n");
            xml.Append("      <Label X=\"11\" Y=\"176\" Width=\"-11\" Height=\"17\" FontId=\"3\" DisablePrefix=\"yes\">#(loc.OptionsLocationLabel)</Label>\n");
            xml.Append("      <Editbox Name=\"InstallFolder\" X=\"11\" Y=\"198\" Width=\"-91\" Height=\"21\" TabStop=\"yes\" FontId=\"3\" FileSystemAutoComplete=\"yes\" />\n");
            xml.Append("      <Button Name=\"BrowseButton\" X=\"-11\" Y=\"197\" Width=\"75\" Height=\"23\" TabStop=\"yes\" FontId=\"3\">\n");
            xml.Append("        <Text>#(loc.OptionsBrowseButton)</Text>\n        <BrowseDirectoryAction VariableName=\"InstallFolder\" />\n      </Button>\n");
            xml.Append("      <Button Name=\"OptionsOkButton\" X=\"-91\" Y=\"-11\" Width=\"75\" Height=\"23\" TabStop=\"yes\" FontId=\"0\">\n");
            xml.Append("        <Text>#(loc.OptionsOkButton)</Text>\n        <ChangePageAction Page=\"Install\" />\n      </Button>\n");
            xml.Append("      <Button Name=\"OptionsCancelButton\" X=\"-11\" Y=\"-11\" Width=\"75\" Height=\"23\" TabStop=\"yes\" FontId=\"0\">\n");
            xml.Append("        <Text>#(loc.OptionsCancelButton)</Text>\n        <ChangePageAction Page=\"Install\" Cancel=\"yes\" />\n      </Button>\n");
            xml.Append("    </Page>\n");
        }

        xml.Append("    <Page Name=\"Progress\">\n");
        xml.Append("      <Label X=\"11\" Y=\"135\" Width=\"-11\" Height=\"30\" FontId=\"2\" DisablePrefix=\"yes\">#(loc.ProgressHeader)</Label>\n");
        xml.Append("      <Label X=\"11\" Y=\"176\" Width=\"70\" Height=\"17\" FontId=\"3\" DisablePrefix=\"yes\">#(loc.ProgressLabel)</Label>\n");
        xml.Append("      <Label Name=\"OverallProgressPackageText\" X=\"85\" Y=\"176\" Width=\"-11\" Height=\"17\" FontId=\"3\" DisablePrefix=\"yes\">#(loc.OverallProgressPackageText)</Label>\n");
        xml.Append("      <Progressbar Name=\"OverallCalculatedProgressbar\" X=\"11\" Y=\"198\" Width=\"-11\" Height=\"15\" />\n");
        xml.Append("      <Button Name=\"ProgressCancelButton\" X=\"-11\" Y=\"-11\" Width=\"75\" Height=\"23\" TabStop=\"yes\" FontId=\"0\">#(loc.ProgressCancelButton)</Button>\n");
        xml.Append("    </Page>\n");
        xml.Append("    <Page Name=\"Modify\">\n");
        xml.Append("      <Label X=\"11\" Y=\"135\" Width=\"-11\" Height=\"30\" FontId=\"2\" DisablePrefix=\"yes\">#(loc.ModifyHeader)</Label>\n");
        xml.Append("      <Button Name=\"RepairButton\" X=\"-171\" Y=\"-11\" Width=\"75\" Height=\"23\" TabStop=\"yes\" FontId=\"0\" HideWhenDisabled=\"yes\" ElevationShieldCondition=\"NOT WixBundleElevated\">#(loc.ModifyRepairButton)</Button>\n");
        xml.Append("      <Button Name=\"UninstallButton\" X=\"-91\" Y=\"-11\" Width=\"75\" Height=\"23\" TabStop=\"yes\" FontId=\"0\" ElevationShieldCondition=\"NOT WixBundleElevated\">#(loc.ModifyUninstallButton)</Button>\n");
        xml.Append("      <Button Name=\"ModifyCancelButton\" X=\"-11\" Y=\"-11\" Width=\"75\" Height=\"23\" TabStop=\"yes\" FontId=\"0\">\n");
        xml.Append("        <Text>#(loc.ModifyCancelButton)</Text>\n        <CloseWindowAction />\n      </Button>\n");
        xml.Append("    </Page>\n");
        xml.Append("    <Page Name=\"Success\">\n");
        xml.Append("      <Label X=\"11\" Y=\"135\" Width=\"-11\" Height=\"30\" FontId=\"2\" DisablePrefix=\"yes\">\n");
        xml.Append("        <Text>#(loc.SuccessHeader)</Text>\n");
        xml.Append("        <Text Condition=\"WixBundleAction = 2\">#(loc.SuccessLayoutHeader)</Text>\n");
        xml.Append("        <Text Condition=\"WixBundleAction = 3\">#(loc.SuccessUnsafeUninstallHeader)</Text>\n");
        xml.Append("        <Text Condition=\"WixBundleAction = 4\">#(loc.SuccessUninstallHeader)</Text>\n");
        xml.Append("        <Text Condition=\"WixBundleAction = 5\">#(loc.SuccessCacheHeader)</Text>\n");
        xml.Append("        <Text Condition=\"WixBundleAction = 6\">#(loc.SuccessInstallHeader)</Text>\n");
        xml.Append("        <Text Condition=\"WixBundleAction = 7\">#(loc.SuccessModifyHeader)</Text>\n");
        xml.Append("        <Text Condition=\"WixBundleAction = 8\">#(loc.SuccessRepairHeader)</Text>\n");
        xml.Append("      </Label>\n");
        xml.Append("      <Label X=\"-11\" Y=\"-51\" Width=\"400\" Height=\"34\" FontId=\"3\" DisablePrefix=\"yes\" VisibleCondition=\"WixStdBARestartRequired\">\n");
        xml.Append("        <Text>#(loc.SuccessRestartText)</Text>\n");
        xml.Append("        <Text Condition=\"WixBundleAction = 3\">#(loc.SuccessUninstallRestartText)</Text>\n");
        xml.Append("      </Label>\n");
        xml.Append("      <Button Name=\"SuccessRestartButton\" X=\"-91\" Y=\"-11\" Width=\"75\" Height=\"23\" TabStop=\"yes\" FontId=\"0\" HideWhenDisabled=\"yes\">#(loc.SuccessRestartButton)</Button>\n");
        xml.Append("      <Button Name=\"SuccessCloseButton\" X=\"-11\" Y=\"-11\" Width=\"75\" Height=\"23\" TabStop=\"yes\" FontId=\"0\">\n");
        xml.Append("        <Text>#(loc.SuccessCloseButton)</Text>\n        <CloseWindowAction />\n      </Button>\n");
        xml.Append("    </Page>\n");
        xml.Append("    <Page Name=\"Failure\">\n");
        xml.Append("      <Label X=\"11\" Y=\"135\" Width=\"-11\" Height=\"30\" FontId=\"2\" DisablePrefix=\"yes\">\n");
        xml.Append("        <Text>#(loc.FailureHeader)</Text>\n");
        xml.Append("        <Text Condition=\"WixBundleAction = 2\">#(loc.FailureLayoutHeader)</Text>\n");
        xml.Append("        <Text Condition=\"WixBundleAction = 3\">#(loc.FailureUnsafeUninstallHeader)</Text>\n");
        xml.Append("        <Text Condition=\"WixBundleAction = 4\">#(loc.FailureUninstallHeader)</Text>\n");
        xml.Append("        <Text Condition=\"WixBundleAction = 5\">#(loc.FailureCacheHeader)</Text>\n");
        xml.Append("        <Text Condition=\"WixBundleAction = 6\">#(loc.FailureInstallHeader)</Text>\n");
        xml.Append("        <Text Condition=\"WixBundleAction = 7\">#(loc.FailureModifyHeader)</Text>\n");
        xml.Append("        <Text Condition=\"WixBundleAction = 8\">#(loc.FailureRepairHeader)</Text>\n");
        xml.Append("      </Label>\n");
        xml.Append("      <Hypertext Name=\"FailureLogFileLink\" X=\"11\" Y=\"176\" Width=\"-11\" Height=\"42\" FontId=\"3\" TabStop=\"yes\" HideWhenDisabled=\"yes\">#(loc.FailureHyperlinkLogText)</Hypertext>\n");
        xml.Append("      <Hypertext Name=\"FailureMessageText\" X=\"22\" Y=\"218\" Width=\"-11\" Height=\"51\" FontId=\"3\" TabStop=\"yes\" HideWhenDisabled=\"yes\" />\n");
        xml.Append("      <Label X=\"-11\" Y=\"-51\" Width=\"400\" Height=\"34\" FontId=\"3\" DisablePrefix=\"yes\" VisibleCondition=\"WixStdBARestartRequired\">#(loc.FailureRestartText)</Label>\n");
        xml.Append("      <Button Name=\"FailureRestartButton\" X=\"-91\" Y=\"-11\" Width=\"75\" Height=\"23\" TabStop=\"yes\" FontId=\"0\" HideWhenDisabled=\"yes\">#(loc.FailureRestartButton)</Button>\n");
        xml.Append("      <Button Name=\"FailureCloseButton\" X=\"-11\" Y=\"-11\" Width=\"75\" Height=\"23\" TabStop=\"yes\" FontId=\"0\">\n");
        xml.Append("        <Text>#(loc.FailureCloseButton)</Text>\n        <CloseWindowAction />\n      </Button>\n");
        xml.Append("    </Page>\n");
        xml.Append("  </Window>\n");
        xml.Append("</Theme>\n");
        return xml.ToString();
    }

    /// <summary>The theme's words: WiX's own but the welcome text, which is R2025b's welcome panel text.</summary>
    public static string ThemeWxl(InstallerSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        string welcome = spec.RuntimeOnly
            ? $"Setup will install JGraph Runtime {spec.RuntimeVersion} on your computer, which programs and libraries built with JGraph's compiler run on. Click Install to continue or Cancel to exit."
            : WelcomeText(spec);
        var wxl = new StringBuilder();
        wxl.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
        wxl.Append("<WixLocalization Culture=\"en-us\" Language=\"1033\" xmlns=\"http://wixtoolset.org/schemas/v4/wxl\">\n");
        void S(string id, string value) => wxl.Append($"  <String Id=\"{id}\" Value=\"{X(value)}\" />\n");
        S("Caption", "[WixBundleName] Setup");
        S("Title", "[WixBundleName]");
        S("CheckingForUpdatesLabel", "Checking for updates");
        S("InstallHeader", "Welcome");
        S("InstallMessage", Formatted(welcome));
        S("InstallVersion", "Version [WixBundleVersion]");
        S("ConfirmCancelMessage", "Are you sure you want to cancel?");
        S("ExecuteUpgradeRelatedBundleMessage", "Previous version");
        S("HelpHeader", "Setup Help");
        S("HelpText", "/install | /repair | /uninstall | /layout [directory] - installs, repairs, uninstalls or\n   creates a complete local copy of the bundle in directory. Install is the default.\n\n/passive | /quiet -  displays minimal UI with no prompts or displays no UI and\n   no prompts. By default UI and all prompts are displayed.\n\n/norestart   - suppress any attempts to restart. By default UI will prompt before restart.\n/log log.txt - logs to a specific file. By default a log file is created in %TEMP%.");
        S("HelpCloseButton", "&Close");
        S("InstallOptionsButton", "&Options");
        S("InstallInstallButton", "&Install");
        S("InstallCancelButton", "&Cancel");
        S("OptionsHeader", "Setup Options");
        S("OptionsLocationLabel", "Install location:");
        S("OptionsBrowseButton", "&Browse");
        S("OptionsOkButton", "&OK");
        S("OptionsCancelButton", "&Cancel");
        S("ProgressHeader", "Setup Progress");
        S("ProgressLabel", "Processing:");
        S("OverallProgressPackageText", "Initializing...");
        S("ProgressCancelButton", "&Cancel");
        S("ModifyHeader", "Modify Setup");
        S("ModifyRepairButton", "&Repair");
        S("ModifyUninstallButton", "&Uninstall");
        S("ModifyCancelButton", "&Cancel");
        S("SuccessHeader", "Setup Successful");
        S("SuccessCacheHeader", "Cache Successfully Completed");
        S("SuccessInstallHeader", "Installation Successfully Completed");
        S("SuccessLayoutHeader", "Layout Successfully Completed");
        S("SuccessModifyHeader", "Modify Successfully Completed");
        S("SuccessRepairHeader", "Repair Successfully Completed");
        S("SuccessUninstallHeader", "Uninstall Successfully Completed");
        S("SuccessUnsafeUninstallHeader", "Uninstall Successfully Completed");
        S("SuccessRestartText", "You must restart your computer before you can use the software.");
        S("SuccessUninstallRestartText", "You must restart your computer to complete the removal of the software.");
        S("SuccessRestartButton", "&Restart");
        S("SuccessCloseButton", "&Close");
        S("FailureHeader", "Setup Failed");
        S("FailureCacheHeader", "Cache Failed");
        S("FailureInstallHeader", "Setup Failed");
        S("FailureLayoutHeader", "Layout Failed");
        S("FailureModifyHeader", "Modify Failed");
        S("FailureRepairHeader", "Repair Failed");
        S("FailureUninstallHeader", "Uninstall Failed");
        S("FailureUnsafeUninstallHeader", "Uninstall Failed");
        S("FailureHyperlinkLogText", "One or more issues caused the setup to fail. Please fix the issues and then retry setup. For more information see the <a href=\"#\">log file</a>.");
        S("FailureRestartText", "You must restart your computer to complete the rollback of the software.");
        S("FailureRestartButton", "&Restart");
        S("FailureCloseButton", "&Close");
        S("FilesInUseTitle", "Files In Use");
        S("FilesInUseLabel", "The following applications are using files that need to be updated:");
        S("FilesInUseNetfxCloseRadioButton", "Close the &applications.");
        S("FilesInUseCloseRadioButton", "Close the &applications and attempt to restart them.");
        S("FilesInUseDontCloseRadioButton", "&Do not close applications. A reboot will be required.");
        S("FilesInUseRetryButton", "&Retry");
        S("FilesInUseIgnoreButton", "&Ignore");
        S("FilesInUseExitButton", "E&xit");
        wxl.Append("</WixLocalization>\n");
        return wxl.ToString();
    }

    /// <summary>Text for a Burn formatted string: its brackets escaped so nothing in it reads as a variable.</summary>
    private static string Formatted(string text)
    {
        var escaped = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            escaped.Append(c switch { '[' => "[\\[]", ']' => "[\\]]", _ => c.ToString() });
        }

        return escaped.ToString();
    }

    /// <summary>
    /// Writes a 24-bit Windows bitmap (the format a bundle's splash screen takes) from row-major RGB
    /// bytes, three per pixel, the top row first; the codecs encode no BMP.
    /// </summary>
    public static void WriteBmp(string path, int width, int height, ReadOnlySpan<byte> rgb)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (rgb.Length != width * height * 3)
        {
            throw new ArgumentException("The pixel bytes do not match the size.", nameof(rgb));
        }

        int stride = ((width * 3) + 3) & ~3;
        byte[] bmp = new byte[54 + (stride * height)];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BitConverter.TryWriteBytes(bmp.AsSpan(2), bmp.Length);
        BitConverter.TryWriteBytes(bmp.AsSpan(10), 54);
        BitConverter.TryWriteBytes(bmp.AsSpan(14), 40);
        BitConverter.TryWriteBytes(bmp.AsSpan(18), width);
        BitConverter.TryWriteBytes(bmp.AsSpan(22), height);
        BitConverter.TryWriteBytes(bmp.AsSpan(26), (short)1);
        BitConverter.TryWriteBytes(bmp.AsSpan(28), (short)24);
        BitConverter.TryWriteBytes(bmp.AsSpan(34), stride * height);
        BitConverter.TryWriteBytes(bmp.AsSpan(38), 2835);
        BitConverter.TryWriteBytes(bmp.AsSpan(42), 2835);
        for (int r = 0; r < height; r++)
        {
            int at = 54 + ((height - 1 - r) * stride);
            for (int c = 0; c < width; c++)
            {
                int pixel = ((r * width) + c) * 3;
                bmp[at + (3 * c)] = rgb[pixel + 2];
                bmp[at + (3 * c) + 1] = rgb[pixel + 1];
                bmp[at + (3 * c) + 2] = rgb[pixel];
            }
        }

        File.WriteAllBytes(path, bmp);
    }

    /// <summary>The installation notes as the RTF the welcome page's box shows.</summary>
    public static string Rtf(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var rtf = new StringBuilder("{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0 Segoe UI;}}\\fs18 ");
        foreach (char c in text.Replace("\r\n", "\n", StringComparison.Ordinal))
        {
            switch (c)
            {
                case '\\' or '{' or '}':
                    rtf.Append('\\').Append(c);
                    break;
                case '\n':
                    rtf.Append("\\par ");
                    break;
                case '\r':
                    break;
                default:
                    if (c < 128)
                    {
                        rtf.Append(c);
                    }
                    else
                    {
                        rtf.Append("\\u").Append((short)c).Append('?');
                    }

                    break;
            }
        }

        rtf.Append('}');
        return rtf.ToString();
    }

    private static string X(string text) => SecurityElement.Escape(text).Replace("\n", "&#xA;", StringComparison.Ordinal);
}
