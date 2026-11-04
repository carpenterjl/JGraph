using System.IO.Compression;
using JGraph.Api;
using JGraph.Deploy.Apps;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.Deploy;

/// <summary>
/// The add-on builtins in a MATLAB session (App Designer window plan W13): <c>matlab.addons.toolbox.ToolboxOptions</c>'
/// checks in R2025b's words (probe <c>w13_apps</c>), <c>packageToolbox</c> and <c>toolboxVersion</c>, the installs through
/// <c>installToolbox</c> and <c>matlab.apputil.install</c> into an add-ons folder of the test's own (named by
/// <c>JGRAPH_ADDONS</c>), what <c>installedToolboxes</c>, <c>getInstalledAppInfo</c> and <c>installedAddons</c> answer, the
/// path a toolbox puts its folders on, and the refusals.
/// </summary>
[Collection("JG facade")]
public sealed class AddOnBuiltinsTests : IAsyncLifetime
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Deploy", "fixtures");
    private readonly RecordingScriptOutput _output = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jgraph-addonbuiltins-" + Guid.NewGuid().ToString("N"));
    private readonly string? _previousRoot = Environment.GetEnvironmentVariable(InstalledAddOns.RootVariable);
    private IScriptSession _session = null!;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_folder);
        Environment.SetEnvironmentVariable(InstalledAddOns.RootVariable, Path.Combine(_folder, "Add-Ons"));
        ToolboxPackage.Extract(Path.Combine(Fixtures, "AcceptForm-r2025b.mltbx"), Path.Combine(_folder, "apps"));
        File.Delete(Path.Combine(_folder, "apps", "DesktopToolset.xml"));
        File.Copy(Path.Combine(Fixtures, "AcceptForm-r2025b.mltbx"), Path.Combine(_folder, "AcceptForm.mltbx"));
        File.Copy(Path.Combine(Fixtures, "AcceptFormApp-r2025b.mlappinstall"), Path.Combine(_folder, "AcceptFormApp.mlappinstall"));
        _session = ((IScriptRepl)new MatlabScriptEngine()).CreateSession(new ScriptContext(_output, (_, _) => { }, _folder, null));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _session.DisposeAsync();
        JG.Reset();
        Environment.SetEnvironmentVariable(InstalledAddOns.RootVariable, _previousRoot);
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>The identifier and message a statement fails with, or "ok", and the warning it raised.</summary>
    private async Task<string> Outcome(string statement)
    {
        _output.Mark();
        ScriptRunResult result = await _session.ExecuteAsync(
            $"lastwarn(''); try, {statement}; disp('ok'), catch e, disp([e.identifier ' | ' strrep(e.message, newline, ' / ')]), end; [m, id] = lastwarn; if ~isempty(id), disp(['warn ' id]), end",
            string.Empty, CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.TextSinceMark.Trim();
    }

    private async Task<string> Printed(string statement)
    {
        _output.Mark();
        ScriptRunResult result = await _session.ExecuteAsync(statement, string.Empty, CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.TextSinceMark.Trim();
    }

    private string Apps => Path.Combine(_folder, "apps");

    private string Q(string path) => path.Replace("'", "''", StringComparison.Ordinal);

    [Fact]
    public async Task ToolboxOptionsRefusesAsR2025bDoes()
    {
        Assert.Equal("MATLAB:toolbox_packaging:packaging:ToolboxPathCannotBeEmpty | Toolbox project or folder must be non-empty.", await Outcome("matlab.addons.toolbox.ToolboxOptions('')"));
        Assert.Equal("MATLAB:toolbox_packaging:packaging:ToolboxIdentifierMissing | Unique toolbox identifier must be specified.", await Outcome($"matlab.addons.toolbox.ToolboxOptions('{Q(Apps)}')"));
        Assert.Equal($"MATLAB:toolbox_packaging:packaging:ToolboxRootNotFound | Toolbox folder \"{Path.Combine(Apps, "nothere")}\" does not exist.", await Outcome($"matlab.addons.toolbox.ToolboxOptions('{Q(Path.Combine(Apps, "nothere"))}', 'id')"));
        Assert.Equal("MATLAB:toolbox_packaging:packaging:InvalidIdentifier | Invalid toolbox identifier 'bad id!'.  Identifier must be alphanumeric and may contain hyphens ('-').", await Outcome($"matlab.addons.toolbox.ToolboxOptions('{Q(Apps)}', 'bad id!')"));
        Assert.Equal($"MATLAB:toolbox_packaging:packaging:FileMustExist | Specified file does not exist: {Path.Combine(Apps, "nothere.m")}", await Outcome($"matlab.addons.toolbox.ToolboxOptions('{Q(Apps)}', 'id', ToolboxFiles={{'{Q(Path.Combine(Apps, "nothere.m"))}'}})"));
        Assert.Equal("MATLAB:toolbox_packaging:packaging:ToolboxFolderCannotBeEmpty | ToolboxFiles cannot be empty.", await Outcome($"matlab.addons.toolbox.ToolboxOptions('{Q(Apps)}', 'id', ToolboxFiles={{}})"));
        Assert.Equal("MATLAB:toolbox_packaging:packaging:NotValidToolboxPRJ | Project file must be a toolbox project (PRJ) file.", await Outcome($"matlab.addons.toolbox.ToolboxOptions('{Q(Path.Combine(Apps, "data.txt"))}')"));

        Assert.Equal("ok", await Outcome($"o = matlab.addons.toolbox.ToolboxOptions('{Q(Apps)}', 'w13-probe-id', ToolboxFiles={{'{Q(Path.Combine(Apps, "AcceptForm.mlapp"))}', '{Q(Path.Combine(Apps, "data.txt"))}', '{Q(Path.Combine(Apps, "sub", "deep.m"))}'}})"));
        Assert.Equal("apps", await Printed("disp(o.ToolboxName)"));
        Assert.Equal(Apps + ".mltbx", await Printed("disp(o.OutputFile)"));
        Assert.Equal("1", await Printed("fprintf('%d\\n', isempty(o.ToolboxMatlabPath))"));
        Assert.Equal("3", await Printed("disp(numel(o.ToolboxFiles))"));
        Assert.Equal("1.0.0", await Printed("disp(o.ToolboxVersion)"));
        Assert.Equal("MATLAB:toolbox_packaging:packaging:InvalidToolboxVersion | Invalid version 'x'. The version must be a scalar string or character vector of the form Major.Minor.Bug.Build. Bug and Build are optional.", await Outcome("o.ToolboxVersion = 'x'"));
        Assert.Equal("MATLAB:toolbox_packaging:packaging:InvalidReleaseString | Release must be a string with the format 'RXXXXx', for example 'R2022b'.", await Outcome("o.MinimumMatlabRelease = 'R20'"));
        Assert.Equal("MATLAB:toolbox_packaging:packaging:InvalidMinimumRelease | Minimum release must either be between R2014b and the specified maximum release (R2025b), or be empty ('').", await Outcome("o.MinimumMatlabRelease = 'R2010a'"));
        Assert.Equal("ok", await Outcome("o.MinimumMatlabRelease = 'R2020a'"));
        Assert.Equal("MATLAB:toolbox_packaging:packaging:InvalidMaximumRelease | Maximum release must either be between the specified minimum release (R2020a) and the current MATLAB release (R2025b), or be empty ('').", await Outcome("o.MaximumMatlabRelease = 'R2015a'"));
        Assert.Equal($"MATLAB:toolbox_packaging:packaging:InvalidAppGalleryFile | Invalid file '{Path.Combine(Apps, "data.txt")}'. App gallery files must be valid MATLAB code files (.m, .mlapp, .mlx, .p)", await Outcome($"o.AppGalleryFiles = {{'{Q(Path.Combine(Apps, "data.txt"))}'}}"));
        Assert.Equal($"MATLAB:toolbox_packaging:packaging:FileMissingFromToolboxFiles | File is missing from the ToolboxFiles list: {Path.Combine(Apps, "helper.m")}", await Outcome($"o.AppGalleryFiles = {{'{Q(Path.Combine(Apps, "helper.m"))}'}}"));
        Assert.Equal($"MATLAB:toolbox_packaging:packaging:InvalidToolboxImageFileFormat | Unsupported toolbox image file format '{Path.Combine(Apps, "data.txt")}'.  Format must be 'bmp', 'gif', 'jpg', 'jpeg', or 'png'.", await Outcome($"o.ToolboxImageFile = '{Q(Path.Combine(Apps, "data.txt"))}'"));
        Assert.Equal($"MATLAB:toolbox_packaging:packaging:ParentFolderMissingFromToolboxFiles | MATLAB path folder does not contain any files that will be packaged: {Path.Combine(Apps, "release")}", await Outcome($"o.ToolboxMatlabPath = {{'{Q(Path.Combine(Apps, "release"))}'}}"));
        Assert.Equal("MATLAB:validators:mustBeNonzeroLengthText | Error setting property 'OutputFile' of class 'matlab.addons.toolbox.ToolboxOptions'. Value must be text with one or more characters.", await Outcome("o.OutputFile = ''"));
        Assert.Equal("MATLAB:class:SetProhibited | You cannot set the read-only property 'Identifier' of ToolboxOptions.", await Outcome("o.Identifier = 'other'"));
        Assert.Equal("MATLAB:toolbox_packaging:packaging:NoOutputWithOptions | Specify the output file location in the toolbox options object.", await Outcome("matlab.addons.toolbox.packageToolbox(o, 'x.mltbx')"));
        Assert.Equal("MATLAB:toolbox_packaging:packaging:NotValidToolboxPRJ | Project file must be a toolbox project (PRJ) file.", await Outcome($"matlab.addons.toolbox.packageToolbox('{Q(Path.Combine(Apps, "data.txt"))}')"));
        Assert.Equal($"MATLAB:toolbox_packaging:packaging:ToolboxFileNotFound | Unable to find file '{Path.Combine(Apps, "nothere.prj")}'.", await Outcome($"matlab.addons.toolbox.packageToolbox('{Q(Path.Combine(Apps, "nothere.prj"))}')"));
    }

    [Fact]
    public async Task PackageToolboxWritesAndToolboxVersionReads()
    {
        string app = Path.Combine(Apps, "AcceptForm.mlapp");
        string output = Path.Combine(Apps, "release", "Probe.mltbx");
        Assert.Equal("ok", await Outcome(
            $"o = matlab.addons.toolbox.ToolboxOptions('{Q(Apps)}', 'w13-probe-id', ToolboxFiles={{'{Q(app)}', '{Q(Path.Combine(Apps, "data.txt"))}'}}, ToolboxName='Probe Tools', ToolboxVersion='3.0.1', AuthorName='W13 Probe', Summary='Sum.', Description='Desc.', AppGalleryFiles={{'{Q(app)}'}}, ToolboxMatlabPath={{'{Q(Apps)}'}}, OutputFile='{Q(output)}'); matlab.addons.toolbox.packageToolbox(o)"));
        Assert.True(File.Exists(output));
        ToolboxPackageInfo info = ToolboxPackage.Read(output);
        Assert.Equal(("Probe Tools", "3.0.1", "w13-probe-id", "Sum.", "Desc."), (info.Name, info.Version, info.Guid, info.Summary, info.Description));
        Assert.Equal("AcceptForm", Assert.Single(info.GalleryEntries).Callback);
        // The platforms the options hold as logicals reach the package as R2025b writes them (R2025b warns on an install otherwise, probe w13_warn).
        using (ZipArchive zip = ZipFile.OpenRead(output))
        using (var reader = new StreamReader(zip.GetEntry("metadata/systemRequirements.xml")!.Open()))
        {
            Assert.Contains("<platformCompatibility MATLABOnline=\"true\" linux=\"true\" mac=\"true\" win=\"true\"/>", reader.ReadToEnd());
        }

        Assert.Equal("3.0.1", await Printed($"disp(matlab.addons.toolbox.toolboxVersion('{Q(output)}'))"));
        Assert.Equal($"MATLAB:toolbox_packaging:packaging:InvalidFile | Invalid file type.  File '{Path.Combine(Apps, "data.txt")}' must be a MATLAB toolbox (MLTBX) or toolbox project (PRJ) file.", await Outcome($"matlab.addons.toolbox.toolboxVersion('{Q(Path.Combine(Apps, "data.txt"))}')"));
        Assert.Equal($"MATLAB:toolbox_packaging:packaging:ToolboxFileNotFound | Unable to find file '{Path.Combine(Apps, "nothere.mltbx")}'.", await Outcome($"matlab.addons.toolbox.toolboxVersion('{Q(Path.Combine(Apps, "nothere.mltbx"))}')"));
        Assert.Equal("MATLAB:toolbox_packaging:packaging:CannotSetVersionOnMLTBX | Version is read-only for MATLAB toolbox files (*.mltbx).", await Outcome($"matlab.addons.toolbox.toolboxVersion('{Q(output)}', '2')"));
    }

    [Fact]
    public async Task AToolboxInstallsOntoThePathAndUninstalls()
    {
        string mltbx = Path.Combine(_folder, "AcceptForm.mltbx");
        Assert.Equal($"toolboxmanagement_matlab_api:installToolbox:invalidFilePath | Unable to find toolbox file {Path.Combine(_folder, "nothere.mltbx")}.", await Outcome($"matlab.addons.toolbox.installToolbox('{Q(Path.Combine(_folder, "nothere.mltbx"))}')"));
        Assert.Equal("toolboxmanagement_matlab_api:installToolbox:invalidFileType | File must be a toolbox file (*.mltbx).", await Outcome($"matlab.addons.toolbox.installToolbox('{Q(Path.Combine(Apps, "data.txt"))}')"));
        Assert.Equal("0", await Printed("disp(exist('helper'))"));
        Assert.Equal("ok", await Outcome($"info = matlab.addons.toolbox.installToolbox('{Q(mltbx)}')"));
        Assert.Equal("Accept Form | 2.1 | 079c16c6-1827-4ad5-9249-17eaa4de3a27", await Printed("disp([info.Name ' | ' info.Version ' | ' info.Guid])"));
        Assert.Equal("1", await Printed("disp(numel(matlab.addons.toolbox.installedToolboxes))"));
        Assert.Equal("Accept Form", await Printed("t = matlab.addons.toolbox.installedToolboxes; disp(t(1).Name)"));
        // The toolbox's folders went on the path: its helper.m answers its name.
        Assert.Equal("2", await Printed("disp(exist('helper'))"));
        Assert.Equal("6", await Printed("disp(helper(5))"));
        Assert.Equal("1", await Printed("fprintf('%d\\n', matlab.addons.isAddonEnabled('Accept Form'))"));
        Assert.Equal("MATLAB:matlab:addons:toolbox:uninstallToolbox:invalidType | Expected input number 1, InstalledToolbox, to be one of these types: /  / struct", await Outcome("matlab.addons.toolbox.uninstallToolbox('x')"));
        Assert.Equal("ok\nwarn MATLAB:matlab:addons:toolbox:uninstallToolbox:notFound", await Outcome("matlab.addons.toolbox.uninstallToolbox(struct('Name', 'x', 'Version', '1', 'Guid', 'nothere-guid'))"));
        Assert.Equal("ok", await Outcome("matlab.addons.toolbox.uninstallToolbox(info)"));
        Assert.Equal("0", await Printed("disp(numel(matlab.addons.toolbox.installedToolboxes))"));
        Assert.Equal("0", await Printed("disp(exist('helper'))"));
        Assert.False(Directory.Exists(Path.Combine(_folder, "Add-Ons", "Toolboxes", "Accept Form")));
    }

    [Fact]
    public async Task AnAppInstallsListsAndUninstallsThroughApputil()
    {
        string package = Path.Combine(_folder, "AcceptFormApp.mlappinstall");
        Assert.Equal($"MATLAB:apputil:create:filenotfound | The specified file '{Path.Combine(Apps, "data.txt")}' could not be found.", await Outcome($"matlab.apputil.install('{Q(Path.Combine(Apps, "data.txt"))}')"));
        Assert.Equal("1", await Printed("fprintf('%d\\n', isempty(matlab.apputil.getInstalledAppInfo))"));
        Assert.Equal("No apps are installed.  See matlab.apputil.install for information on installing apps.", await Printed("matlab.apputil.getInstalledAppInfo()"));
        Assert.Equal("ok", await Outcome($"s = matlab.apputil.install('{Q(package[..^".mlappinstall".Length])}')"));
        Assert.Equal("AcceptFormAppAPP | AcceptFormApp | installed", await Printed("disp([s.id ' | ' s.name ' | ' s.status])"));
        Assert.Equal(Path.Combine(_folder, "Add-Ons", "Apps", "AcceptFormApp"), await Printed("disp(s.location)"));
        Assert.Equal("updated", await Printed($"s2 = matlab.apputil.install('{Q(package)}'); disp(s2.status)"));
        Assert.Equal("ID                   Name\n----------------     -------------\nAcceptFormAppAPP     AcceptFormApp", await Printed("matlab.apputil.getInstalledAppInfo()"));
        Assert.Equal("AcceptFormAppAPP", await Printed("i = matlab.apputil.getInstalledAppInfo; disp(i(1).id)"));
        Assert.Equal("1", await Printed("fprintf('%d\\n', matlab.addons.isAddonEnabled('4e716c9a-8857-448e-b536-fb847eec6f66'))"));
        string table = await Printed("a = matlab.addons.installedAddons; disp(a.Properties.VariableNames)");
        Assert.Contains("Identifier", table);
        Assert.Equal("1", await Printed("disp(height(matlab.addons.installedAddons))"));
        Assert.Equal("MATLAB:apputil:run:notinstalled | The specified app \"bogusAPP\" cannot be found. Please install apps before running them. Refer to matlab.apputil.install for information on installing apps.", await Outcome("matlab.apputil.run('bogusAPP')"));
        Assert.Equal("MATLAB:apputil:uninstall:notinstalled | The specified ID cannot be found.  When uninstalling an app, the specified ID must exactly match the installed ID.  Use matlab.apputil.getInstalledAppInfo to determine the installed apps and their IDs.", await Outcome("matlab.apputil.uninstall('acceptformappapp')"));
        Assert.Equal("ok", await Outcome("matlab.apputil.uninstall('AcceptFormAppAPP')"));
        Assert.Equal("1", await Printed("fprintf('%d\\n', isempty(matlab.apputil.getInstalledAppInfo))"));
    }

    [Fact]
    public async Task TheOtherRefusalsAreR2025bs()
    {
        Assert.Equal("JGraph:apputil:noPackagingDialog | JGraph has no toolbox packaging dialog. Package an app with matlab.addons.toolbox.ToolboxOptions and packageToolbox, or with App Designer's Share > MATLAB App.\nwarn MATLAB:apps:errorcheck:CreateRemoval", await Outcome("matlab.apputil.create"));
        Assert.Equal($"MATLAB:apputil:create:filenotfound | The specified file '{Path.Combine(_folder, "nothere.prj")}' could not be found.\nwarn MATLAB:apps:errorcheck:CreateRemoval", await Outcome($"matlab.apputil.create('{Q(Path.Combine(_folder, "nothere.prj"))}')"));
        Assert.Equal($"MATLAB:apputil:create:filenotfound | The specified file '{Path.Combine(_folder, "AcceptForm.mltbx")}' could not be found.", await Outcome($"matlab.apputil.package('{Q(Path.Combine(_folder, "AcceptForm.mltbx"))}')"));
        Assert.Equal("matlab_addons:install:invalidFileType | File must be a toolbox file (*.mltbx).", await Outcome($"matlab.addons.install('{Q(Path.Combine(_folder, "AcceptFormApp.mlappinstall"))}')"));
        Assert.Equal($"matlab_addons:install:invalidFilePath | Unable to find {Path.Combine(_folder, "nothere.mltbx")}.", await Outcome($"matlab.addons.install('{Q(Path.Combine(_folder, "nothere.mltbx"))}')"));
        Assert.Equal("matlab_addons:enableDisableManagement:invalidIdentifier | Unable to find add-on with specified identifier.", await Outcome("matlab.addons.uninstall('bogus')"));
        Assert.Equal("matlab_addons:enableDisableManagement:invalidIdentifier | Unable to find add-on with specified identifier.", await Outcome("matlab.addons.isAddonEnabled('bogus')"));
        Assert.Equal("ok", await Outcome($"tb = matlab.addons.install('{Q(Path.Combine(_folder, "AcceptForm.mltbx"))}')"));
        Assert.Equal("Accept Form", await Printed("disp(char(tb.Name))"));
        Assert.Equal("ok", await Outcome("matlab.addons.uninstall('Accept Form')"));
        Assert.Equal("0", await Printed("disp(height(matlab.addons.installedAddons))"));
    }
}
