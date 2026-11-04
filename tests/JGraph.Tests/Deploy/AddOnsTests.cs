using System.IO.Compression;
using JGraph.Deploy;
using JGraph.Deploy.Apps;
using Xunit;

namespace JGraph.Tests.Deploy;

/// <summary>
/// Toolboxes and apps as packages and installs (App Designer window plan W13), free of a session: the <c>.mltbx</c>
/// JGraph writes compared part by part with the one R2025b wrote for the same files (probe <c>w13_apps</c>, the
/// fixtures <c>AcceptForm-r2025b.mltbx</c>, <c>ProbeTools-r2025b.mltbx</c> and <c>AcceptFormApp-r2025b.mlappinstall</c>),
/// R2025b's packages read and installed into an add-ons folder laid out as R2025b lays out its own (probe
/// <c>w13_installed</c>), and the MATLAB App share script.
/// </summary>
public sealed class AddOnsTests : IDisposable
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Deploy", "fixtures");
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jgraph-addons-" + Guid.NewGuid().ToString("N"));

    public AddOnsTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static string Fixture(string name) => Path.Combine(Fixtures, name);

    /// <summary>The probe's app folder again: the app from R2025b's package, and the five files beside it.</summary>
    private string AppsFolder()
    {
        string apps = Path.Combine(_folder, "apps");
        ToolboxPackage.Extract(Fixture("AcceptForm-r2025b.mltbx"), apps);
        File.Delete(Path.Combine(apps, "DesktopToolset.xml"));
        return apps;
    }

    [Fact]
    public void R2025bsToolboxPackageReadsBack()
    {
        ToolboxPackageInfo info = ToolboxPackage.Read(Fixture("AcceptForm-r2025b.mltbx"));
        Assert.Equal("Accept Form", info.Name);
        Assert.Equal("2.1", info.Version);
        Assert.Equal("079c16c6-1827-4ad5-9249-17eaa4de3a27", info.Guid);
        Assert.Equal("A form that adds or multiplies.", info.Summary);
        Assert.Equal("One line.", info.Description);
        Assert.Equal("W13 Probe", info.AuthorName);
        Assert.Equal("R2025b", info.MatlabRelease);
        Assert.Equal(["/AcceptForm.mlapp", "/art.png", "/data.txt", "/helper.m", "/sub/deep.m", "/table.mat"], info.Files.Select(f => f.PackagedPath));
        Assert.Equal(["", "/sub"], info.MatlabPaths);
        ToolboxGalleryEntry entry = Assert.Single(info.GalleryEntries);
        Assert.Equal(("AcceptForm.mlapp", "4caa76ce-c63e-42ba-9889-05fe82cd083b:AcceptForm.mlapp", "Accept Form", "AcceptForm"), (entry.AppFilePath, entry.Id, entry.Label, entry.Callback));
        Assert.Equal(("R2014b", "latest"), (info.ReleaseStart, info.ReleaseEnd));
        Assert.True(info.IsSigned);
        Assert.Null(info.ScreenshotPart);

        ToolboxPackageInfo tools = ToolboxPackage.Read(Fixture("ProbeTools-r2025b.mltbx"));
        Assert.Equal(("Probe Tools", "3.0.1", "w13-probe-id", "Sum.", "Desc.", "W13 Probe", "probe@example.com", "JGraph"),
            (tools.Name, tools.Version, tools.Guid, tools.Summary, tools.Description, tools.AuthorName, tools.AuthorContact, tools.AuthorOrganization));
        Assert.Equal("metadata/primaryScreenShot.png", tools.ScreenshotPart);
        Assert.Equal("R2020a", tools.ReleaseStart);
        Assert.Empty(tools.MatlabPaths);
        Assert.NotNull(ToolboxPackage.ReadScreenshot(Fixture("ProbeTools-r2025b.mltbx")));
    }

    [Fact]
    public void JGraphsToolboxPackageHasR2025bsPartsButTheSignature()
    {
        string apps = AppsFolder();
        string app = Path.Combine(apps, "AcceptForm.mlapp");
        var options = new ToolboxPackageOptions(apps, "079c16c6-1827-4ad5-9249-17eaa4de3a27")
        {
            ToolboxName = "Accept Form",
            ToolboxVersion = "2.1",
            AuthorName = "W13 Probe",
            Summary = "A form that adds or multiplies.",
            Description = "One line.",
            OutputFile = Path.Combine(apps, "release", "AcceptForm.mltbx"),
        };
        options.ToolboxFiles.AddRange([app, Path.Combine(apps, "data.txt"), Path.Combine(apps, "helper.m"), Path.Combine(apps, "sub", "deep.m"), Path.Combine(apps, "art.png"), Path.Combine(apps, "table.mat")]);
        options.ToolboxMatlabPath.AddRange([apps, Path.Combine(apps, "sub")]);
        options.AppGalleryFiles.Add(app);
        string written = ToolboxPackage.Write(options);
        Assert.True(File.Exists(written));

        using ZipArchive ours = ZipFile.OpenRead(written);
        using ZipArchive theirs = ZipFile.OpenRead(Fixture("AcceptForm-r2025b.mltbx"));
        string[] theirParts = [.. theirs.Entries.Select(e => e.FullName).Where(n => !n.StartsWith("_xmlsignatures", StringComparison.Ordinal))];
        Assert.Equal(theirParts, ours.Entries.Select(e => e.FullName));

        static string Text(ZipArchive zip, string part)
        {
            using var reader = new StreamReader(zip.GetEntry(part)!.Open());
            return reader.ReadToEnd().Replace("\r\n", "\n");
        }

        string[] theirTypes = Text(theirs, "[Content_Types].xml").Split('\n').Where(l => !l.Contains("sigs", StringComparison.Ordinal) && !l.Contains("signature", StringComparison.Ordinal)).ToArray();
        Assert.Equal(theirTypes, Text(ours, "[Content_Types].xml").Split('\n'));
        Assert.Equal(Text(theirs, "fsroot/DesktopToolset.xml"), Text(ours, "fsroot/DesktopToolset.xml"));
        Assert.Equal(Text(theirs, "metadata/addonProperties.xml"), Text(ours, "metadata/addonProperties.xml"));
        Assert.Equal(Text(theirs, "metadata/configuration.xml"), Text(ours, "metadata/configuration.xml"));
        Assert.Equal(Text(theirs, "metadata/systemRequirements.xml"), Text(ours, "metadata/systemRequirements.xml"));
        Assert.Equal(Text(theirs, "metadata/mwcoreProperties.xml"), Text(ours, "metadata/mwcoreProperties.xml"));
        string[] theirRels = Text(theirs, "_rels/.rels").Split('\n').Where(l => !l.Contains("digitalsignature", StringComparison.Ordinal)).ToArray();
        Assert.Equal(theirRels, Text(ours, "_rels/.rels").Split('\n'));
        Assert.Contains("name=\"/sub/deep.m\" permissions=\"010666\" type=\"File\"", Text(ours, "metadata/filesystemManifest.xml"));

        ToolboxPackageInfo info = ToolboxPackage.Read(written);
        Assert.False(info.IsSigned);
        Assert.Equal(("Accept Form", "2.1", "079c16c6-1827-4ad5-9249-17eaa4de3a27"), (info.Name, info.Version, info.Guid));
        Assert.Equal(["", "/sub"], info.MatlabPaths);
        Assert.Equal("2.1", ToolboxPackage.Read(written).Version);
    }

    [Fact]
    public void AnImageBecomesTheScreenshotPart()
    {
        string apps = AppsFolder();
        var options = new ToolboxPackageOptions(apps, "w13-probe-id") { ToolboxImageFile = Path.Combine(apps, "art.png"), OutputFile = Path.Combine(apps, "release", "ProbeTools.mltbx"), MinimumMatlabRelease = "R2020a" };
        options.ToolboxFiles.AddRange([Path.Combine(apps, "AcceptForm.mlapp"), Path.Combine(apps, "data.txt")]);
        ToolboxPackage.Write(options);
        using ZipArchive ours = ZipFile.OpenRead(options.OutputFile);
        using ZipArchive theirs = ZipFile.OpenRead(Fixture("ProbeTools-r2025b.mltbx"));
        Assert.NotNull(ours.GetEntry("metadata/primaryScreenShot.png"));
        using var reader = new StreamReader(ours.GetEntry("_rels/.rels")!.Open());
        string rels = reader.ReadToEnd();
        Assert.Contains("Id=\"rId100\" Target=\"metadata/primaryScreenShot.png\"", rels);
        Assert.Contains("Id=\"rId7\" Target=\"metadata/systemRequirements.xml\"", rels);
        Assert.DoesNotContain("configuration.xml", rels);
        Assert.Equal("R2020a", ToolboxPackage.Read(options.OutputFile).ReleaseStart);
    }

    [Fact]
    public void R2025bsAppInstallPackageReadsBack()
    {
        MlappInstallInfo info = MlappInstallPackage.Read(Fixture("AcceptFormApp-r2025b.mlappinstall"));
        Assert.Equal(("AcceptForm", "AcceptFormApp", "2.1", "(R2025b)", "4e716c9a-8857-448e-b536-fb847eec6f66"), (info.EntryPoint, info.Name, info.Version, info.CreatedByMatlabRelease, info.Guid));
        Assert.Equal(("A form that adds or multiplies.", "One line.", "W13 Probe", "probe@example.com", "JGraph"), (info.Summary, info.Description, info.AuthorName, info.AuthorContact, info.AuthorOrganization));
        Assert.Equal(["/AcceptForm.mlapp"], info.Files);
        Assert.Equal(["/AcceptForm.mlapp"], info.AppEntries);
        Assert.Throws<InvalidDataException>(() => MlappInstallPackage.Read(Fixture("AcceptForm-r2025b.mltbx")));
    }

    [Fact]
    public void AToolboxInstallsAsR2025bLaysItOut()
    {
        var addons = new InstalledAddOns(Path.Combine(_folder, "Add-Ons"));
        InstalledToolbox installed = addons.InstallToolbox(Fixture("AcceptForm-r2025b.mltbx"));
        Assert.Equal(("Accept Form", "2.1", "079c16c6-1827-4ad5-9249-17eaa4de3a27"), (installed.Name, installed.Version, installed.Guid));
        string folder = Path.Combine(addons.ToolboxesFolder, "Accept Form");
        Assert.Equal(folder, installed.Folder);
        Assert.Equal([folder, Path.Combine(folder, "sub")], installed.PathFolders);
        foreach (string file in new[] { "AcceptForm.mlapp", "DesktopToolset.xml", "art.png", "data.txt", "helper.m", "table.mat", @"sub\deep.m", @"resources\addons_core.xml", @"resources\matlab_path_entries.xml", @"resources\mltbx_app_gallery_registration.xml" })
        {
            Assert.True(File.Exists(Path.Combine(folder, file)), file);
        }

        string core = File.ReadAllText(Path.Combine(folder, "resources", "addons_core.xml"));
        Assert.Contains("<label>Accept Form</label>", core);
        Assert.Contains("<type>Toolbox</type>", core);
        Assert.Contains("<createdBy name=\"W13 Probe\"/>", core);
        Assert.Equal("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<paths>\n  <path>.</path>\n  <path>sub</path>\n</paths>\n", File.ReadAllText(Path.Combine(folder, "resources", "matlab_path_entries.xml")));

        Assert.Single(addons.Toolboxes());
        GalleryEntry entry = Assert.Single(addons.Gallery());
        Assert.Equal(("Accept Form", "AcceptForm", "A form that adds or multiplies.", false, installed.Guid), (entry.Label, entry.Callback, entry.Summary, entry.IsApp, entry.Id));
        Assert.Equal(installed.PathFolders, entry.PathFolders);

        // The same identifier again replaces the install; a screenshot comes along as resources\screenshot.png.
        addons.InstallToolbox(Fixture("AcceptForm-r2025b.mltbx"));
        Assert.Single(addons.Toolboxes());
        InstalledToolbox tools = addons.InstallToolbox(Fixture("ProbeTools-r2025b.mltbx"));
        Assert.Equal(Path.Combine(tools.Folder, "resources", "screenshot.png"), tools.Screenshot);
        Assert.Equal(2, addons.Toolboxes().Count);
        Assert.True(addons.UninstallToolbox(installed.Guid));
        Assert.False(Directory.Exists(folder));
        Assert.False(addons.UninstallToolbox(installed.Guid));
        Assert.Single(addons.Toolboxes());
    }

    [Fact]
    public void AnAppInstallsUnderItsFolderName()
    {
        var addons = new InstalledAddOns(Path.Combine(_folder, "Add-Ons"));
        InstalledApp app = addons.InstallApp(Fixture("AcceptFormApp-r2025b.mlappinstall"));
        Assert.Equal(("AcceptFormAppAPP", "AcceptFormApp", "installed", "AcceptForm"), (app.Id, app.Name, app.Status, app.EntryPoint));
        Assert.Equal(Path.Combine(addons.AppsFolder, "AcceptFormApp"), app.Location);
        Assert.True(File.Exists(Path.Combine(app.Location, "AcceptForm.mlapp")));
        Assert.Contains("<callback>matlab.apputil.run('AcceptFormAppAPP')</callback>", File.ReadAllText(Path.Combine(app.Location, "resources", "mlappinstall_app_gallery_registration.xml")));
        Assert.Contains("<type>app</type>", File.ReadAllText(Path.Combine(app.Location, "resources", "addons_core.xml")));
        Assert.Equal("updated", addons.InstallApp(Fixture("AcceptFormApp-r2025b.mlappinstall")).Status);
        Assert.Equal([app.Location], InstalledAddOns.AppPathFolders(app.Location));
        Assert.Single(addons.FindApps("acceptform", strict: false));
        Assert.Empty(addons.FindApps("acceptform", strict: true));
        GalleryEntry entry = Assert.Single(addons.Gallery());
        Assert.Equal(("AcceptFormApp", "matlab.apputil.run('AcceptFormAppAPP')", true), (entry.Label, entry.Callback, entry.IsApp));
        Assert.True(addons.UninstallApp("AcceptFormAppAPP"));
        Assert.Empty(addons.Apps());
    }

    [Theory]
    [InlineData("Accept Form", "AcceptForm")]
    [InlineData("2D Plots!", "x2DPlots")]
    [InlineData("private", "privateapp")]
    [InlineData("resources", "resourcesapp")]
    [InlineData("my_app v2", "my_appv2")]
    public void TheAppFolderNameFollowsConvertNameToDir(string name, string folder) => Assert.Equal(folder, InstalledAddOns.AppFolderName(name));

    [Fact]
    public void TheAppFolderNameIsHeldToSixtyThreeCharacters() => Assert.Equal(63, InstalledAddOns.AppFolderName(new string('a', 80)).Length);

    [Fact]
    public void DetailsComeFromAnMlappOrAPlainTextApp()
    {
        string apps = AppsFolder();
        AppPackageDetails mlapp = AppPackageDetails.Read(Path.Combine(apps, "AcceptForm.mlapp"));
        Assert.Equal(("Accept Form", "2.1", "W13 Probe", "A form that adds or multiplies.", "One line.", "4caa76ce-c63e-42ba-9889-05fe82cd083b"), (mlapp.Name, mlapp.Version, mlapp.Author, mlapp.Summary, mlapp.Description, mlapp.Uuid));
        string text = "classdef W4App < matlab.apps.App\nend\n\n%[appendix]\n%---\n%[app:appDetails]\n%{\n<?xml version='1.0' encoding='UTF-8'?>\n<AppDetails>\n    <Name>Plain App</Name>\n    <Version>1.2</Version>\n    <Summary>Sum.</Summary>\n</AppDetails>\n%}\n\n%---\n%[app:internalData]\n%{\n<?xml version='1.0' encoding='UTF-8'?>\n<InternalData>\n    <AppId>abc-123</AppId>\n</InternalData>\n%}\n";
        AppPackageDetails plain = AppPackageDetails.ReadPlainText(text, "W4App");
        Assert.Equal(("Plain App", "1.2", "Sum.", "abc-123"), (plain.Name, plain.Version, plain.Summary, plain.Uuid));
        AppPackageDetails bare = AppPackageDetails.ReadPlainText("classdef W4App\nend\n", "W4App");
        Assert.Equal(("W4App", "1.0"), (bare.Name, bare.Version));
        Assert.NotEmpty(bare.Uuid);
    }

    [Fact]
    public void TheMatlabAppScriptIsR2025bsWithJGraphsWordsPrinted()
    {
        const string apps = @"C:\apps";
        var details = new AppShareDetails("Accept Form", "2.1", "W13 Probe", "A form that adds or multiplies.", "One line.");
        string script = AppShareScript.MatlabApp(apps + @"\AcceptForm.mlapp",
            [apps + @"\data.txt", apps + @"\helper.m", apps + @"\sub\deep.m", apps + @"\art.png", apps + @"\table.mat"], "079c16c6-1827-4ad5-9249-17eaa4de3a27", details);
        string expected = string.Join("\n",
        [
            "disp(\"Packaging MATLAB App...\");packageOpts=matlab.addons.toolbox.ToolboxOptions(\"C:\\apps\", \"079c16c6-1827-4ad5-9249-17eaa4de3a27\", ToolboxFiles={\"C:\\apps\\AcceptForm.mlapp\", \"C:\\apps\\data.txt\", \"C:\\apps\\helper.m\", \"C:\\apps\\sub\\deep.m\", \"C:\\apps\\art.png\", \"C:\\apps\\table.mat\"});",
            "packageOpts.ToolboxName = \"Accept Form\";",
            "packageOpts.ToolboxVersion = \"2.1\";",
            "packageOpts.AuthorName = \"W13 Probe\";",
            "packageOpts.Summary = \"A form that adds or multiplies.\";",
            "packageOpts.Description = \"One line.\";",
            "packageOpts.AppGalleryFiles = {\"C:\\apps\\AcceptForm.mlapp\"};",
            "packageOpts.ToolboxMatlabPath = {\"C:\\apps\", \"C:\\apps\\sub\"};",
            "packageOpts.OutputFile = \"C:\\apps\\release\\AcceptForm.mltbx\";",
            "matlab.addons.toolbox.packageToolbox(packageOpts);disp(\"MATLAB App packaging complete\");",
        ]);
        Assert.Equal(expected, script);
        Assert.Equal(@"C:\apps\release\AcceptForm.mltbx", AppShareScript.MatlabAppFile(apps + @"\AcceptForm.mlapp"));
    }

    [Fact]
    public void TheOptionsChecksAreR2025bs()
    {
        Assert.Null(ToolboxPackageOptions.WhyNotIdentifier("w13-probe-id"));
        Assert.StartsWith("Invalid toolbox identifier 'bad id!'.", ToolboxPackageOptions.WhyNotIdentifier("bad id!"));
        Assert.Null(ToolboxPackageOptions.WhyNotVersion("3.0.1"));
        Assert.StartsWith("Invalid version 'x'.", ToolboxPackageOptions.WhyNotVersion("x"));
        Assert.NotNull(ToolboxPackageOptions.WhyNotVersion("3"));
        Assert.True(ToolboxPackageOptions.IsReleaseName("R2022b"));
        Assert.False(ToolboxPackageOptions.IsReleaseName("R20"));
        Assert.True(ToolboxPackageOptions.ReleaseAtLeast("R2025b", "R2014b"));
        var options = new ToolboxPackageOptions(_folder, "id");
        Assert.Equal(Path.GetFileName(_folder), options.ToolboxName);
        Assert.Equal(_folder + ".mltbx", options.OutputFile);
        Assert.Equal("sub/deep.m", options.PackagedPath(Path.Combine(_folder, "sub", "deep.m")));
        Assert.Equal("", options.PackagedFolder(_folder));
        Assert.Equal("/sub", options.PackagedFolder(Path.Combine(_folder, "sub")));
    }
}
