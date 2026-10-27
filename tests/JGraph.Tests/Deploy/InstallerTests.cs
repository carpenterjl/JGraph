using System.IO.Compression;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using JGraph.Deploy;
using JGraph.Imaging;
using JGraph.Imaging.Codecs;
using Xunit;

namespace JGraph.Tests.Deploy;

/// <summary>
/// Installers (compiler and SDK plan, stage K18): the runtime installer's identity (its upgrade
/// code derived from the version as installer/build-runtime-installer.ps1 derives it, found and read
/// through <see cref="MsiDatabase"/>), what <see cref="ApplicationInstaller"/> writes for WiX (the
/// application's package, the bundle, the theme and its words, the versions and folders as R2025b's
/// packager treats them), and, where WiX 6 and its bootstrapper extension are at hand, installers
/// built in every delivery and inspected: the application MSI's tables through msi.dll and the
/// bundle's manifest through <c>wix burn extract</c>. No installer is ever run (the plan's house rule).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class InstallerTests : IDisposable
{
    private readonly string _root =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-k18-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
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

    private static readonly string Repo = FindRepo();

    // --- The runtime installer's identity ----------------------------------------------------

    [Fact]
    public void TheRuntimeUpgradeCodeIsDerivedFromTheVersionAsTheBuildScriptDerivesIt()
    {
        // installer/build-runtime-installer.ps1's Get-RuntimeUpgradeCode for these versions (recorded).
        Assert.Equal("3A409E74-1E68-5CCE-A675-1D83C2DF4053", RuntimeInstaller.UpgradeCode("0.2.3"));
        Assert.Equal("526A5296-F40C-5719-9D9E-49115AEA5E5B", RuntimeInstaller.UpgradeCode("1.0.0"));
        Assert.NotEqual(RuntimeInstaller.UpgradeCode("0.2.3"), RuntimeInstaller.UpgradeCode("0.2.4"));

        // RFC 4122's own example: the version 5 UUID of "www.example.com" in the DNS namespace.
        Guid dns = Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8");
        Assert.Equal("2ed6657d-e927-568b-95e1-2665a8aea6a2", RuntimeInstaller.NameBasedGuid(dns, "www.example.com").ToString("D"));

        Assert.Equal("JGraph-Runtime-0.2.3.msi", RuntimeInstaller.FileName("0.2.3"));
        Assert.Equal("https://github.com/jgraph/jgraph/releases/download/v0.2.3/JGraph-Runtime-0.2.3.msi", RuntimeInstaller.ReleaseUrl("0.2.3"));
    }

    [Fact]
    public void TheRuntimeInstallerIsKnownByItsPackageProperties()
    {
        Assert.False(RuntimeInstaller.IsValid(Path.Combine(_root, "none.msi"), "0.2.3"));
        string text = Path.Combine(_root, "text.msi");
        File.WriteAllText(text, "not a package");
        Assert.False(RuntimeInstaller.IsValid(text, "0.2.3"));

        // The checkout's own build of the runtime MSI, when it has been built, reads as the runtime's.
        string version = RuntimeLayout.CurrentVersion;
        string? built = RuntimeInstaller.Find(version);
        if (built is not null && OperatingSystem.IsWindows())
        {
            using MsiDatabase msi = MsiDatabase.Open(built);
            Assert.Equal(version, msi.Property("ProductVersion"));
            Assert.Equal(RuntimeInstaller.UpgradeCode(version), msi.Property("UpgradeCode")?.Trim('{', '}'));
            Assert.Equal("JGraph Runtime " + version, msi.Property("ProductName"));
            Assert.Equal("JGraph contributors", msi.Property("Manufacturer"));
            Assert.Contains("File", msi.Tables);
            Assert.Contains("Registry", msi.Tables);
            Assert.Contains("Environment", msi.Tables);
            // The registry key the launcher reads, and the PATH entry, in the package.
            IReadOnlyList<string[]> registry = msi.Rows("SELECT `Key`, `Name`, `Value` FROM `Registry`");
            Assert.Contains(registry, r => r[0] == $@"SOFTWARE\JGraph\JGraph Runtime\{version}" && r[1] == "Location" && r[2] == "[INSTALLFOLDER]");
            Assert.Contains(registry, r => r[1] == "Release" && r[2] == "25.2");
            IReadOnlyList<string[]> environment = msi.Rows("SELECT `Name`, `Value` FROM `Environment`");
            Assert.Contains(environment, r => r[0].EndsWith("PATH", StringComparison.Ordinal) && r[1].Contains(@"[INSTALLFOLDER]runtime\win64", StringComparison.Ordinal));
            IReadOnlyList<string[]> files = msi.Rows("SELECT `FileName` FROM `File`");
            Assert.Contains(files, f => f[0].EndsWith("|jgraph-runtime.json", StringComparison.Ordinal) || f[0] == "jgraph-runtime.json");
            Assert.Contains(files, f => f[0].Contains("jgrt.dll", StringComparison.Ordinal));
        }
    }

    // --- What the packager writes ------------------------------------------------------------

    [Fact]
    public void VersionsFoldersAndNamesAreTreatedAsR2025bTreatsThem()
    {
        // k18_package: set.Version.* - each dotted part reads as str2double reads it.
        foreach (string accepted in new[] { "1", "2.3.4.5.6", "2.0", " 1.0", "1.0 ", "1,2", "007.08" })
        {
            Assert.True(ApplicationInstaller.IsVersionText(accepted), accepted);
        }

        foreach (string refused in new[] { "a.b", "1.0-beta", "1..2" })
        {
            Assert.False(ApplicationInstaller.IsVersionText(refused), refused);
        }

        Assert.Equal("1.0.0", ApplicationInstaller.MsiVersion("1.0"));
        Assert.Equal("2.3.4", ApplicationInstaller.MsiVersion("2.3.4.5.6"));
        Assert.Equal("12.0.0", ApplicationInstaller.MsiVersion("1,2"));
        Assert.Equal("7.8.0", ApplicationInstaller.MsiVersion("007.08"));
        Assert.Equal("255.255.65535", ApplicationInstaller.MsiVersion("300.300.70000"));
        Assert.Equal("2.3.4.5", ApplicationInstaller.BundleVersion("2.3.4.5.6"));
        Assert.Equal("1.0.0.0", ApplicationInstaller.BundleVersion("1.0"));

        // scrubName: the bad folder characters, separators and blanks.
        Assert.Equal("a_b_c_d_e_f_g_h_i_j_k_l", ApplicationInstaller.ScrubName("a b:c/d\\e<f>g\"h*i|j?k&l"));
        Assert.Equal("My_App", ApplicationInstaller.ScrubName("My App"));

        // DefaultInstallationDir as the bundle's install folder.
        Assert.Equal("[ProgramFiles64Folder]k18hello", ApplicationInstaller.InstallFolderValue(@"%ProgramFiles%\k18hello"));
        Assert.Equal(@"C:\Apps\K18", ApplicationInstaller.InstallFolderValue(@"C:\Apps\K18"));
        Assert.Equal(@"[ProgramFiles64Folder]rel\dir", ApplicationInstaller.InstallFolderValue(@"rel\dir"));
        Assert.Equal(@"[LocalAppDataFolder]x", ApplicationInstaller.InstallFolderValue(@"%LOCALAPPDATA%\x"));
        Assert.Equal(@"[%CUSTOM]\x", ApplicationInstaller.InstallFolderValue(@"%CUSTOM%\x"));

        // The welcome panel text (k18_payload: named.k18setup's WelcomePanelText).
        var spec = new InstallerSpec
        {
            ApplicationName = "k18hello",
            Version = "2.5",
            Summary = "The best app ever",
            Description = "Makes a magic square for you.\nAnd more.",
            AuthorName = "Frog",
            AuthorEmail = "frog@pond.org",
            AuthorCompany = "Boston Common",
            OutputDir = _root,
            RuntimeVersion = "0.2.3",
        };
        Assert.Equal("k18hello 2.5\nThe best app ever\n\nMakes a magic square for you.\nAnd more.\nFrog\nfrog@pond.org", ApplicationInstaller.WelcomeText(spec));
        Assert.Equal("Boston Common", ApplicationInstaller.Manufacturer(spec));
        Assert.Equal("Frog", ApplicationInstaller.Manufacturer(spec with { AuthorCompany = "" }));
        Assert.Equal("k18hello", ApplicationInstaller.Manufacturer(spec with { AuthorCompany = "", AuthorName = "" }));
        Assert.Equal("k18hello 1.0", ApplicationInstaller.WelcomeText(new InstallerSpec { ApplicationName = "k18hello", OutputDir = _root, RuntimeVersion = "0.2.3" }));
    }

    [Fact]
    public void TheSourcesNameTheFilesTheShortcutTheRuntimeAndTheWords()
    {
        string data = Path.Combine(_root, "k18data.txt");
        File.WriteAllText(data, "data\n");
        string folder = Directory.CreateDirectory(Path.Combine(_root, "k18folder")).FullName;
        File.WriteAllText(Path.Combine(folder, "inside.txt"), "inside\n");
        string runtime = Path.Combine(_root, "JGraph-Runtime-0.2.3.msi");
        File.WriteAllText(runtime, "stand-in");
        var spec = new InstallerSpec
        {
            ApplicationName = "My App",
            InstallerName = "k18setup",
            Version = "2.5",
            Items = [data, folder],
            Shortcut = @"k18folder\inside.txt",
            DefaultInstallationDir = @"C:\Apps\K18",
            AuthorCompany = "Boston Common",
            AuthorEmail = "frog@pond.org",
            Summary = "The best [app] ever",
            InstallationNotes = "Needs a pond.\nAnd {lilies}.",
            OutputDir = _root,
            RuntimeVersion = "0.2.3",
            RuntimeInstallerFile = runtime,
            IconFile = Path.Combine(_root, "icon.ico"),
            LogoFile = Path.Combine(_root, "logo.png"),
            SplashFile = Path.Combine(_root, "splash.bmp"),
        };

        XDocument package = XDocument.Parse(ApplicationInstaller.ApplicationWxs(spec));
        XNamespace wxs = "http://wixtoolset.org/schemas/v4/wxs";
        XElement root = package.Root!.Element(wxs + "Package")!;
        Assert.Equal("My App", (string)root.Attribute("Name")!);
        Assert.Equal("Boston Common", (string)root.Attribute("Manufacturer")!);
        Assert.Equal("2.5.0", (string)root.Attribute("Version")!);
        Assert.Equal(ApplicationInstaller.PackageUpgradeCode("My App"), (string)root.Attribute("UpgradeCode")!);
        Assert.Equal("perMachine", (string)root.Attribute("Scope")!);
        Assert.Equal("My_App", (string)root.Descendants(wxs + "Directory").First(d => (string)d.Attribute("Id")! == "INSTALLFOLDER").Attribute("Name")!);
        Assert.Equal("application", (string)root.Descendants(wxs + "Directory").First(d => (string)d.Attribute("Id")! == "ApplicationFolder").Attribute("Name")!);
        Assert.Contains(root.Descendants(wxs + "File"), f => (string)f.Attribute("Source")! == data && (string)f.Attribute("Name")! == "k18data.txt");
        Assert.Contains(root.Descendants(wxs + "Files"), f => (string)f.Attribute("Subdirectory")! == "k18folder" && ((string)f.Attribute("Include")!).StartsWith(folder, StringComparison.Ordinal));
        XElement shortcut = root.Descendants(wxs + "Shortcut").Single();
        Assert.Equal(@"[ApplicationFolder]k18folder\inside.txt", (string)shortcut.Attribute("Target")!);
        Assert.Equal("My App", (string)shortcut.Attribute("Name")!);
        Assert.Contains(root.Elements(wxs + "Property"), p => (string)p.Attribute("Id")! == "ARPCOMMENTS" && (string)p.Attribute("Value")! == "The best [app] ever");
        Assert.Contains(root.Elements(wxs + "Property"), p => (string)p.Attribute("Id")! == "ARPCONTACT" && (string)p.Attribute("Value")! == "frog@pond.org");

        XDocument bundle = XDocument.Parse(ApplicationInstaller.BundleWxs(spec, notes: true));
        XNamespace bal = "http://wixtoolset.org/schemas/v4/wxs/bal";
        XElement b = bundle.Root!.Element(wxs + "Bundle")!;
        Assert.Equal("My App", (string)b.Attribute("Name")!);
        Assert.Equal("2.5.0.0", (string)b.Attribute("Version")!);
        Assert.Equal(ApplicationInstaller.BundleUpgradeCode("My App"), (string)b.Attribute("UpgradeCode")!);
        Assert.Equal(spec.IconFile, (string)b.Attribute("IconSourceFile")!);
        Assert.Equal(spec.SplashFile, (string)b.Attribute("SplashScreenSourceFile")!);
        XElement ba = b.Element(wxs + "BootstrapperApplication")!.Element(bal + "WixStandardBootstrapperApplication")!;
        Assert.Equal("rtfLicense", (string)ba.Attribute("Theme")!);
        Assert.Equal("notes.rtf", (string)ba.Attribute("LicenseFile")!);
        Assert.Equal("theme.xml", (string)ba.Attribute("ThemeFile")!);
        Assert.Equal(spec.LogoFile, (string)ba.Attribute("LogoFile")!);
        XElement variable = b.Elements(wxs + "Variable").Single(v => (string)v.Attribute("Name")! == "InstallFolder");
        Assert.Equal(@"C:\Apps\K18", (string)variable.Attribute("Value")!);
        List<XElement> chain = [.. b.Element(wxs + "Chain")!.Elements(wxs + "MsiPackage")];
        Assert.Equal(["JGraphRuntime", "Application"], chain.Select(p => (string)p.Attribute("Id")!));
        Assert.Equal("no", (string)chain[0].Attribute("Compressed")!);
        Assert.Equal(RuntimeInstaller.ReleaseUrl("0.2.3"), (string)chain[0].Attribute("DownloadUrl")!);
        Assert.Equal("yes", (string)chain[0].Attribute("Permanent")!);
        Assert.Equal("yes", (string)chain[0].Attribute("Visible")!);
        Assert.Equal("[InstallFolder]", (string)chain[1].Element(wxs + "MsiProperty")!.Attribute("Value")!);

        // An offline installer carries the runtime; an application-only one has no runtime package.
        XDocument offline = XDocument.Parse(ApplicationInstaller.BundleWxs(spec with { RuntimeDelivery = InstallerRuntimeDelivery.Installer }, notes: false));
        XElement carried = offline.Root!.Element(wxs + "Bundle")!.Element(wxs + "Chain")!.Elements(wxs + "MsiPackage").First();
        Assert.Equal("yes", (string)carried.Attribute("Compressed")!);
        Assert.Null(carried.Attribute("DownloadUrl"));
        XDocument alone = XDocument.Parse(ApplicationInstaller.BundleWxs(spec with { RuntimeDelivery = InstallerRuntimeDelivery.None, RuntimeInstallerFile = null }, notes: false));
        Assert.Single(alone.Root!.Element(wxs + "Bundle")!.Element(wxs + "Chain")!.Elements(wxs + "MsiPackage"));
        XElement hyperlink = alone.Root!.Element(wxs + "Bundle")!.Element(wxs + "BootstrapperApplication")!.Element(bal + "WixStandardBootstrapperApplication")!;
        Assert.Equal("hyperlinkLicense", (string)hyperlink.Attribute("Theme")!);
        Assert.Equal("", (string)hyperlink.Attribute("LicenseUrl")!);

        // The theme: the welcome text as the install message with its brackets escaped for Burn, the notes box only with notes.
        XDocument theme = XDocument.Parse(ApplicationInstaller.ThemeXml(notes: true, runtimeOnly: false));
        XNamespace thm = "http://wixtoolset.org/schemas/v4/thmutil";
        Assert.Contains(theme.Descendants(thm + "Richedit"), r => (string)r.Attribute("Name")! == "EulaRichedit");
        Assert.Contains(theme.Descendants(thm + "Editbox"), e => (string)e.Attribute("Name")! == "InstallFolder");
        Assert.DoesNotContain(XDocument.Parse(ApplicationInstaller.ThemeXml(notes: false, runtimeOnly: false)).Descendants(thm + "Richedit"), r => true);
        Assert.DoesNotContain(XDocument.Parse(ApplicationInstaller.ThemeXml(notes: false, runtimeOnly: true)).Descendants(thm + "Page"), p => (string)p.Attribute("Name")! == "Options");
        XDocument words = XDocument.Parse(ApplicationInstaller.ThemeWxl(spec));
        XNamespace wxl = "http://wixtoolset.org/schemas/v4/wxl";
        string message = (string)words.Descendants(wxl + "String").Single(s => (string)s.Attribute("Id")! == "InstallMessage").Attribute("Value")!;
        Assert.Equal("My App 2.5\nThe best [\\[]app[\\]] ever\nfrog@pond.org", message);

        // The notes as RTF.
        Assert.Equal(@"{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\fs18 Needs a pond.\par And \{lilies\}.}", ApplicationInstaller.Rtf("Needs a pond.\nAnd {lilies}."));
        Assert.Contains(@"\u233?", ApplicationInstaller.Rtf("caf\u00e9"));
    }

    // --- Installers built with WiX -----------------------------------------------------------

    private static readonly WixTool? Wix = WixTool.Find();

    private sealed class WixFactAttribute : FactAttribute
    {
        public WixFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || Wix is null || Wix.Extension(WixTool.BootstrapperExtension) is null)
            {
                Skip = "WiX 6 with its bootstrapper applications extension was not found (toolbox\\compiler\\wix, JGRAPH_WIX or the NuGet package cache; ADR 0273).";
            }
        }
    }

    /// <summary>A stand-in runtime MSI: a package of one file whose upgrade code and version are the runtime's, so the packager takes it for the runtime's installer.</summary>
    private string StandInRuntimeMsi(string version)
    {
        string folder = Directory.CreateDirectory(Path.Combine(_root, "runtime-" + version)).FullName;
        File.WriteAllText(Path.Combine(folder, "jgraph-runtime.json"), RuntimeLayout.MarkerText(version));
        string wxs = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Package Name="JGraph Runtime {version}" Manufacturer="JGraph contributors" Version="{version}" UpgradeCode="{RuntimeInstaller.UpgradeCode(version)}" Scope="perMachine" Compressed="yes">
                <MediaTemplate EmbedCab="yes" />
                <MajorUpgrade AllowSameVersionUpgrades="yes" DowngradeErrorMessage="newer" />
                <StandardDirectory Id="ProgramFiles64Folder"><Directory Id="INSTALLFOLDER" Name="JGraph Runtime {version}" /></StandardDirectory>
                <ComponentGroup Id="Files" Directory="INSTALLFOLDER"><Component Guid="*"><File Source="jgraph-runtime.json" /></Component></ComponentGroup>
                <Feature Id="Main" Level="1"><ComponentGroupRef Id="Files" /></Feature>
              </Package>
            </Wix>
            """;
        File.WriteAllText(Path.Combine(folder, "runtime.wxs"), wxs);
        Wix!.Run(["build", "runtime.wxs", "-arch", "x64", "-pdbtype", "none", "-o", RuntimeInstaller.FileName(version)], folder);
        return Path.Combine(folder, RuntimeInstaller.FileName(version));
    }

    private static readonly string[] ImageSizes = ["64", "300x120", "250"];

    /// <summary>The images an installer is made from, ready as the packager hands them over: an icon file, a 290-by-112 logo, a 400-by-400 bitmap.</summary>
    private (string Icon, string Logo, string Splash) Images()
    {
        string icon = Path.Combine(_root, "icon.ico");
        File.WriteAllBytes(icon, File.ReadAllBytes(Path.Combine(Repo, "src", "JGraph.Application", "Assets", "jgraph.ico")));
        string logo = Path.Combine(_root, "logo.png");
        using (var image = new ImageBuffer(112, 290, 3))
        {
            for (int r = 0; r < 112; r++)
            {
                for (int c = 0; c < 290; c++)
                {
                    image[r, c, 0] = c / 290.0;
                    image[r, c, 1] = r / 112.0;
                    image[r, c, 2] = 0.5;
                }
            }

            ImageCodec.Write(logo, image);
        }

        string splash = Path.Combine(_root, "splash.bmp");
        byte[] rgb = new byte[400 * 400 * 3];
        for (int r = 0; r < 400; r++)
        {
            for (int c = 0; c < 400; c++)
            {
                int at = ((r * 400) + c) * 3;
                rgb[at] = 230;
                rgb[at + 1] = (byte)(230 * c / 400);
                rgb[at + 2] = (byte)(230 * r / 400);
            }
        }

        ApplicationInstaller.WriteBmp(splash, 400, 400, rgb);
        using (ImageBuffer back = ImageCodec.Read(splash))
        {
            Assert.Equal((400, 400), (back.Height, back.Width));
        }

        return (icon, logo, splash);
    }

    /// <summary>The bundle's manifest, extracted with <c>wix burn extract</c>, and the folder its packages were put in.</summary>
    private (XDocument Manifest, string Folder) Extracted(string bundle)
    {
        string folder = Directory.CreateDirectory(Path.Combine(_root, "extract-" + Path.GetFileNameWithoutExtension(bundle))).FullName;
        string ba = Directory.CreateDirectory(Path.Combine(folder, "ba")).FullName;
        // The intermediate folder must be there before the tool writes its cabinet into it.
        string intermediate = Directory.CreateDirectory(Path.Combine(_root, "wixtmp-" + Path.GetFileNameWithoutExtension(bundle))).FullName;
        Wix!.Run(["burn", "extract", bundle, "-o", folder, "-oba", ba, "-intermediateFolder", intermediate], _root);
        string manifest = Directory.GetFiles(folder, "manifest.xml", SearchOption.AllDirectories).Concat(Directory.GetFiles(ba, "manifest.xml", SearchOption.AllDirectories)).First();
        return (XDocument.Load(manifest), folder);
    }

    [WixFact]
    public void AnInstallerIsBuiltInEveryDeliveryAndReadsBack()
    {
        string version = "0.2.3";
        string runtimeMsi = StandInRuntimeMsi(version);
        Assert.True(RuntimeInstaller.IsValid(runtimeMsi, version));
        (string icon, string logo, string splash) = Images();
        string exe = Path.Combine(_root, "k18hello.exe");
        File.WriteAllBytes(exe, File.ReadAllBytes(Path.Combine(Repo, "native", "win-x64", "launchers", "jglaunch-console.exe")));
        string data = Path.Combine(_root, "k18data.txt");
        File.WriteAllText(data, "data\n");
        string folder = Directory.CreateDirectory(Path.Combine(_root, "k18folder")).FullName;
        File.WriteAllText(Path.Combine(folder, "inside.txt"), "inside\n");
        var spec = new InstallerSpec
        {
            ApplicationName = "k18hello",
            InstallerName = "k18setup",
            Version = "2.5",
            Items = [exe, data, folder],
            Shortcut = "k18hello.exe",
            DefaultInstallationDir = @"%ProgramFiles%\k18hello",
            IconFile = icon,
            AddRemoveProgramsIcon = icon,
            LogoFile = logo,
            SplashFile = splash,
            AuthorName = "Frog",
            AuthorEmail = "frog@pond.org",
            AuthorCompany = "Boston Common",
            Summary = "The best app ever",
            Description = "Makes a magic square for you.",
            InstallationNotes = "Needs a pond.",
            OutputDir = Path.Combine(_root, "web"),
            RuntimeVersion = version,
            RuntimeInstallerFile = runtimeMsi,
        };
        XNamespace burn = "http://wixtoolset.org/schemas/v4/2008/Burn";

        // Web: the runtime's MSI is named by its download URL and not carried; the application's is inside.
        string web = ApplicationInstaller.Build(spec, Wix!);
        Assert.Equal(Path.Combine(spec.OutputDir, "k18setup.exe"), web);
        Assert.True(File.Exists(web));
        Assert.Equal(["k18setup.exe"], Directory.GetFiles(spec.OutputDir).Select(Path.GetFileName));
        (XDocument manifest, string extracted) = Extracted(web);
        XElement registration = manifest.Descendants(burn + "Registration").Single();
        Assert.Equal("2.5.0.0", (string)registration.Attribute("Version")!);
        XElement arp = registration.Element(burn + "Arp")!;
        Assert.Equal("k18hello", (string)arp.Attribute("DisplayName")!);
        Assert.Equal("Boston Common", (string)arp.Attribute("Publisher")!);
        List<XElement> packages = [.. manifest.Descendants(burn + "MsiPackage")];
        Assert.Equal(["JGraphRuntime", "Application"], packages.Select(p => (string)p.Attribute("Id")!));
        XElement runtime = packages[0];
        Assert.Equal("yes", (string)runtime.Attribute("Permanent")!);
        XElement runtimePayload = manifest.Descendants(burn + "Payload").Single(p => (string)p.Attribute("FilePath")! == RuntimeInstaller.FileName(version));
        Assert.Equal(RuntimeInstaller.ReleaseUrl(version), (string)runtimePayload.Attribute("DownloadUrl")!);
        Assert.Equal("no", (string)runtimePayload.Attribute("Packaging")! == "external" ? "no" : "yes");
        Assert.Equal("external", (string)runtimePayload.Attribute("Packaging")!);
        Assert.Equal(new FileInfo(runtimeMsi).Length.ToString(System.Globalization.CultureInfo.InvariantCulture), (string)runtimePayload.Attribute("FileSize")!);
        XElement applicationPayload = manifest.Descendants(burn + "Payload").Single(p => (string)p.Attribute("FilePath")! == "application.msi");
        Assert.Equal("embedded", (string)applicationPayload.Attribute("Packaging")!);
        string appMsi = Directory.GetFiles(extracted, "application.msi", SearchOption.AllDirectories).Single();
        using (MsiDatabase msi = MsiDatabase.Open(appMsi))
        {
            Assert.Equal("k18hello", msi.Property("ProductName"));
            Assert.Equal("2.5.0", msi.Property("ProductVersion"));
            Assert.Equal("Boston Common", msi.Property("Manufacturer"));
            Assert.Equal("The best app ever", msi.Property("ARPCOMMENTS"));
            Assert.Equal("frog@pond.org", msi.Property("ARPCONTACT"));
            Assert.Equal(ApplicationInstaller.PackageUpgradeCode("k18hello"), msi.Property("UpgradeCode")?.Trim('{', '}'));
            string[] files = [.. msi.Rows("SELECT `FileName` FROM `File`").Select(r => r[0].Split('|')[^1])];
            Assert.Contains("k18hello.exe", files);
            Assert.Contains("k18data.txt", files);
            Assert.Contains("inside.txt", files);
            IReadOnlyList<string[]> directories = msi.Rows("SELECT `Directory`, `DefaultDir` FROM `Directory`");
            Assert.Contains(directories, d => d[0] == "ApplicationFolder" && d[1].EndsWith("application", StringComparison.Ordinal));
            Assert.Contains(directories, d => d[1].EndsWith("k18folder", StringComparison.Ordinal));
            IReadOnlyList<string[]> shortcuts = msi.Rows("SELECT `Name`, `Target` FROM `Shortcut`");
            Assert.Single(shortcuts);
            Assert.Equal("[ApplicationFolder]k18hello.exe", shortcuts[0][1]);
            Assert.Contains("Icon", msi.Tables);
        }

        // The theme and its words travel with the bootstrapper application: the welcome text, the notes.
        string themeWxl = Directory.GetFiles(_root, "*.wxl", SearchOption.AllDirectories).FirstOrDefault(f => f.Contains("extract-k18setup", StringComparison.Ordinal))
            ?? Directory.GetFiles(extracted, "*.wxl", SearchOption.AllDirectories).FirstOrDefault() ?? "";
        if (themeWxl.Length > 0)
        {
            Assert.Contains("k18hello 2.5", File.ReadAllText(themeWxl), StringComparison.Ordinal);
        }

        // Offline: the runtime's MSI is inside; zip: the program and a readme in a zip and nothing else.
        string offline = ApplicationInstaller.Build(spec with
        {
            OutputDir = Path.Combine(_root, "offline"),
            RuntimeDelivery = InstallerRuntimeDelivery.Installer,
            PackageType = InstallerPackageType.Zip,
            InstallationNotes = "",
        }, Wix!);
        Assert.Equal(Path.Combine(_root, "offline", "k18setup.zip"), offline);
        Assert.Equal(["k18setup.zip"], Directory.GetFiles(Path.Combine(_root, "offline")).Select(Path.GetFileName));
        using (ZipArchive zip = ZipFile.OpenRead(offline))
        {
            Assert.Equal(["k18setup.exe", "readme.txt"], zip.Entries.Select(e => e.FullName).Order());
            using StreamReader reader = new(zip.GetEntry("readme.txt")!.Open());
            Assert.Equal("Run k18setup.exe to install the application.", reader.ReadToEnd());
            zip.GetEntry("k18setup.exe")!.ExtractToFile(Path.Combine(_root, "offline-k18setup.exe"));
        }

        (XDocument offlineManifest, _) = Extracted(Path.Combine(_root, "offline-k18setup.exe"));
        XElement carried = offlineManifest.Descendants(burn + "Payload").Single(p => (string)p.Attribute("FilePath")! == RuntimeInstaller.FileName(version));
        Assert.Equal("embedded", (string)carried.Attribute("Packaging")!);
        Assert.Null(carried.Attribute("DownloadUrl"));

        // Application only: no runtime package at all, and no runtime MSI needed to build it.
        string alone = ApplicationInstaller.Build(spec with
        {
            OutputDir = Path.Combine(_root, "alone"),
            RuntimeDelivery = InstallerRuntimeDelivery.None,
            RuntimeInstallerFile = null,
            InstallationNotes = "",
            Shortcut = "",
        }, Wix!);
        (XDocument aloneManifest, _) = Extracted(alone);
        Assert.Equal(["Application"], aloneManifest.Descendants(burn + "MsiPackage").Select(p => (string)p.Attribute("Id")!));

        // Runtime only (compiler.runtime.customInstaller): the runtime's MSI alone, the bundle named after the runtime.
        string runtimeOnly = ApplicationInstaller.Build(new InstallerSpec
        {
            ApplicationName = "JGraph Runtime",
            InstallerName = "k18rt",
            Version = version,
            RuntimeDelivery = InstallerRuntimeDelivery.Web,
            IconFile = icon,
            LogoFile = logo,
            SplashFile = splash,
            OutputDir = Path.Combine(_root, "custom"),
            RuntimeVersion = version,
            RuntimeInstallerFile = runtimeMsi,
            RuntimeOnly = true,
        }, Wix!);
        Assert.Equal(Path.Combine(_root, "custom", "k18rt.exe"), runtimeOnly);
        (XDocument runtimeManifest, _) = Extracted(runtimeOnly);
        Assert.Equal(["JGraphRuntime"], runtimeManifest.Descendants(burn + "MsiPackage").Select(p => (string)p.Attribute("Id")!));
        Assert.Equal("JGraph Runtime " + version, (string)runtimeManifest.Descendants(burn + "Arp").Single().Attribute("DisplayName")!);
    }

    [WixFact]
    public void TheRuntimeInstallerIsFoundWhereTheVariableNamesIt()
    {
        string version = "9.9.9";
        string msi = StandInRuntimeMsi(version);
        string? before = Environment.GetEnvironmentVariable(RuntimeInstaller.LocationVariable);
        try
        {
            Environment.SetEnvironmentVariable(RuntimeInstaller.LocationVariable, msi);
            Assert.Equal(msi, RuntimeInstaller.Find(version));
            // A file named for another version is not that version's installer.
            Assert.Null(RuntimeInstaller.Find("9.9.8"));
            Assert.Equal(msi, RuntimeInstaller.Candidates(version).First());
        }
        finally
        {
            Environment.SetEnvironmentVariable(RuntimeInstaller.LocationVariable, before);
        }

        // The text a Burn formatted string gets, and the tool's own answers.
        Assert.Matches(new Regex(@"wix\.dll$"), Wix!.WixDll);
        Assert.StartsWith("6.", Wix.Run(["--version"], _root).Trim(), StringComparison.Ordinal);
    }
}
