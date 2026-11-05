using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting.Designer;

/// <summary>
/// App Designer window plan W15: a custom UI component's registration for the Component Library -
/// <c>resources/appDesigner.json</c> read, checked, merged and written as R2025b's metadata Model writes it
/// (probe <c>w15_component</c>, section 4), <c>appdesigner.customcomponent.configureMetadata</c> and
/// <c>removeMetadata</c> with their refusals in R2025b's words, and the check that makes the component once.
/// </summary>
[Collection("JG facade")]
public sealed class ComponentMetadataTests : IAsyncLifetime
{
    private readonly RecordingScriptOutput _output = new();
    private readonly Designer _designer = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jgraph-w15-" + Guid.NewGuid().ToString("N"));
    private IScriptSession _session = null!;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_folder);
        foreach (string name in new[] { "comp1", "comp3", "comp4" })
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Scripting", "Designer", "W15Templates", name + ".m"), Path.Combine(_folder, name + ".m"));
        }

        File.WriteAllText(Path.Combine(_folder, "plainfn.m"), "function plainfn\nend\n");
        File.WriteAllText(Path.Combine(_folder, "PlainClass.m"), "classdef PlainClass < handle\nend\n");
        File.WriteAllText(Path.Combine(_folder, "Derived.m"), "classdef Derived < comp1 % a component through a class of the user's\nend\n");
        File.WriteAllText(Path.Combine(_folder, "Abstr.m"), "classdef Abstr < matlab.ui.componentcontainer.ComponentContainer\nend\n");
        File.WriteAllText(Path.Combine(_folder, "Rogue.m"), "classdef Rogue < matlab.ui.componentcontainer.ComponentContainer\n    methods (Access = protected)\n        function setup(comp)\n            comp.Position = [1 1 100 50];\n            uibutton(comp.Parent);\n        end\n        function update(comp)\n        end\n    end\nend\n");
        File.WriteAllText(Path.Combine(_folder, "Assigned.m"), "classdef Assigned < matlab.ui.componentcontainer.ComponentContainer\n    events (HasCallbackProperty, NotifyAccess = private)\n        Ticked\n    end\n    methods (Access = protected)\n        function setup(comp)\n            comp.Position = [1 1 100 50];\n            comp.TickedFcn = @(s, e) disp(1);\n        end\n        function update(comp)\n        end\n    end\nend\n");
        File.WriteAllText(Path.Combine(_folder, "comp1.txt"), string.Empty);
        _session = ((IScriptRepl)new MatlabScriptEngine()).CreateSession(new ScriptContext(_output, (_, _) => { }, _folder, null) { AppDesigner = _designer });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _session.DisposeAsync();
        JG.Reset();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class Designer : IScriptAppDesigner
    {
        public List<string> Configured { get; } = [];

        public string? Refusal { get; set; }

        public string? Open(string? path) => null;

        public string? ConfigureMetadata(string fullFileName)
        {
            Configured.Add(fullFileName);
            return Refusal;
        }
    }

    /// <summary>The identifier and message a statement fails with, or "ok".</summary>
    private async Task<string> Outcome(string statement)
    {
        _output.Mark();
        ScriptRunResult result = await _session.ExecuteAsync(
            $"try, {statement}; disp('ok'), catch e, disp([e.identifier ' | ' e.message]), end", string.Empty, CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.TextSinceMark.Trim();
    }

    private string Registry => Path.Combine(_folder, "resources", "appDesigner.json");

    // --- the model ---------------------------------------------------------------------------------

    [Fact]
    public void TheRootFolderAndClassNameFollowThePackages()
    {
        string root = Path.Combine(Path.GetTempPath(), "w15root");
        Assert.Equal((Path.Combine(root, "sub"), "Gauge", "Gauge"), DesignComponentMetadata.PackageParts(Path.Combine(root, "sub", "Gauge.m")));
        Assert.Equal((root, "pkg.inner.Gauge", "Gauge"), DesignComponentMetadata.PackageParts(Path.Combine(root, "+pkg", "+inner", "Gauge.m")));
        Assert.Equal((root, "Gauge", "Gauge"), DesignComponentMetadata.PackageParts(Path.Combine(root, "@Gauge", "Gauge.mlapp")));
        // R2025b takes any + in the path as a package folder; a folder named C++ is a folder (correctness outranks parity).
        Assert.Equal((Path.Combine(root, "C++"), "Gauge", "Gauge"), DesignComponentMetadata.PackageParts(Path.Combine(root, "C++", "Gauge.m")));
    }

    [Fact]
    public void TheHelpLineAndSuperclassesAreReadFromTheText()
    {
        string comp3 = File.ReadAllText(Path.Combine(_folder, "comp3.m"));
        Assert.Equal("Properties that correspond to underlying components", DesignComponentMetadata.HelpLine(comp3)); // measured: help(comp3)'s first line
        Assert.Equal([DesignComponentMetadata.Superclass], DesignComponentMetadata.Superclasses(comp3, out bool isClass));
        Assert.True(isClass);
        Assert.Equal(["handle", "matlab.mixin.Copyable"], DesignComponentMetadata.Superclasses("classdef (Abstract) X < handle & ...\n    matlab.mixin.Copyable % both\nend\n", out _));
        Assert.Empty(DesignComponentMetadata.Superclasses("function y = f(x)\ny = x;\nend\n", out isClass));
        Assert.False(isClass);
        Assert.Equal("A gauge.", DesignComponentMetadata.HelpLine("% A gauge.\nclassdef G < handle\nend\n"));
        Assert.Equal(string.Empty, DesignComponentMetadata.HelpLine("classdef G < handle\n    properties\n    end\nend\n"));
    }

    [Fact]
    public void AFreshRegistrationWritesR2025bsFile()
    {
        DesignComponentMetadata model = DesignComponentMetadata.Load(Path.Combine(_folder, "comp3.m"));
        Assert.True(model.IsValid);
        Assert.False(model.IsRegistered);
        Assert.Empty(model.Categories);
        DesignComponentMetadataRecord record = model.InitialRecord("Counts clicks");
        Assert.Equal(("comp3", "comp3", "Counts clicks", DesignComponentMetadata.DefaultCategory, DesignComponentMetadata.DefaultVersion, string.Empty), (record.ClassName, record.ComponentName, record.Description, record.Category, record.Version, record.AuthorName));
        Assert.Equal(DesignComponentMetadata.DefaultIcon, record.Icon);

        // The dialog's values, then what the check adds (the measured file's keys and PrettyPrint form).
        record.ComponentName = "Counter";
        record.Category = "Widgets";
        record.Description = "Line one\nLine two";
        record.Icon = "data:image/png;base64,iVBORw0KGgo=";
        record.AuthorName = "Pat";
        record.AuthorEmail = "pat@example.com";
        record.Version = "2.3";
        record.Avatar = string.Empty;
        record.AvatarDark = string.Empty;
        record.DefaultPosition = [1, 1, 320, 240];
        model.Save(record);
        string text = File.ReadAllText(Registry);
        Assert.Equal(
            "{\n  \"components\": [\n    {\n      \"className\": \"comp3\",\n      \"componentName\": \"Counter\",\n      \"description\": \"Line one\\nLine two\",\n      \"icon\": \"data:image/png;base64,iVBORw0KGgo=\",\n"
            + "      \"category\": \"Widgets\",\n      \"authorName\": \"Pat\",\n      \"authorEmail\": \"pat@example.com\",\n      \"version\": \"2.3\",\n      \"avatar\": \"\",\n      \"avatarDark\": \"\",\n"
            + "      \"defaultPosition\": [\n        1,\n        1,\n        320,\n        240\n      ]\n    }\n  ],\n  \"schema\": 3,\n  \"MATLABRelease\": \"R2025b\"\n}",
            text);
        Assert.DoesNotContain("\r", text, StringComparison.Ordinal);

        // An update merges over the registered record, keys in place; the library reads it back.
        DesignComponentMetadata again = DesignComponentMetadata.Load(Path.Combine(_folder, "comp3.m"));
        Assert.True(again.IsRegistered);
        Assert.Equal(["Widgets"], again.Categories);
        DesignComponentMetadataRecord held = again.InitialRecord(null);
        Assert.Equal(("Counter", "Widgets", "2.3"), (held.ComponentName, held.Category, held.Version));
        held.ComponentName = "Counter2";
        held.Version = "2.4";
        again.Save(held);
        string updated = File.ReadAllText(Registry);
        Assert.Contains("\"componentName\": \"Counter2\",\n      \"description\"", updated, StringComparison.Ordinal);
        Assert.Contains("\"version\": \"2.4\",\n      \"avatar\"", updated, StringComparison.Ordinal);
        DesignUserComponent read = Assert.Single(DesignUserComponents.Discover([_folder]));
        Assert.Equal(("comp3", "Counter2", "Widgets", 320.0, 240.0), (read.ClassName, read.ComponentName, read.Category, read.DefaultSize.Width, read.DefaultSize.Height));

        // Deregistration empties the list; a second one has nothing to do.
        Assert.True(again.Deregister());
        Assert.Equal("{\n  \"components\": [],\n  \"schema\": 3,\n  \"MATLABRelease\": \"R2025b\"\n}", File.ReadAllText(Registry));
        Assert.False(DesignComponentMetadata.Load(Path.Combine(_folder, "comp3.m")).Deregister());
    }

    [Fact]
    public void AnUnreadableRegistryIsInvalidAndARecordMissingKeysIsFilledIn()
    {
        string folder = Path.Combine(_folder, "reg");
        Directory.CreateDirectory(Path.Combine(folder, "resources"));
        File.Copy(Path.Combine(_folder, "comp1.m"), Path.Combine(folder, "comp1.m"));
        string file = Path.Combine(folder, "resources", "appDesigner.json");
        File.WriteAllText(file, "{not json");
        Assert.False(DesignComponentMetadata.Load(Path.Combine(folder, "comp1.m")).IsValid);
        File.WriteAllText(file, "{\"components\": \"x\"}");
        Assert.False(DesignComponentMetadata.Load(Path.Combine(folder, "comp1.m")).IsValid);
        File.WriteAllText(file, "{\"other\": 1}");
        Assert.False(DesignComponentMetadata.Load(Path.Combine(folder, "comp1.m")).IsValid);

        // A record with only a class name: the schema's defaults fill it in on the next write (validateComponents), the file's own keys first.
        File.WriteAllText(file, "{\"components\": [{\"className\": \"comp1\", \"componentName\": \"One\"}], \"schema\": 2, \"MATLABRelease\": \"R2021a\", \"licenseName\": \"\"}");
        DesignComponentMetadata model = DesignComponentMetadata.Load(Path.Combine(folder, "comp1.m"));
        Assert.True(model.IsValid);
        Assert.True(model.IsRegistered);
        DesignComponentMetadataRecord record = model.InitialRecord(null);
        Assert.Equal(("One", "My Category", "1", "This is a custom component created by MATLAB user"), (record.ComponentName, record.Category, record.Version, record.Description));
        record.Version = "1.1";
        model.Save(record);
        string text = File.ReadAllText(file);
        Assert.StartsWith("{\n  \"components\": [\n    {\n      \"className\": \"comp1\",\n      \"componentName\": \"One\",\n      \"version\": \"1.1\",\n      \"description\": \"This is a custom component created by MATLAB user\",\n      \"category\": \"My Category\",\n      \"icon\": \"data:image/png;base64,", text, StringComparison.Ordinal);
        Assert.EndsWith("      \"defaultPosition\": \"[100 100 100 100]\"\n    }\n  ],\n  \"schema\": 3,\n  \"MATLABRelease\": \"R2025b\",\n  \"licenseName\": \"\"\n}", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDialogsChecksAreTheViewModels()
    {
        Assert.Null(DesignComponentMetadata.WhyNotComponentName("x"));
        Assert.Equal(DesignComponentMetadata.ComponentNameEmpty, DesignComponentMetadata.WhyNotComponentName(" "));
        Assert.Equal(DesignComponentMetadata.CategoryEmpty, DesignComponentMetadata.WhyNotCategory(string.Empty));
        Assert.Equal([null, DesignComponentMetadata.InvalidVersion, DesignComponentMetadata.InvalidVersion, DesignComponentMetadata.InvalidVersion], new[] { "1.0", "1", "1.0.1", "a.b" }.Select(DesignComponentMetadata.WhyNotVersion));
        Assert.Equal([null, DesignComponentMetadata.InvalidEmail, null], new[] { string.Empty, "a@b", "a@b.cd" }.Select(DesignComponentMetadata.WhyNotEmail));
        string longPath = Path.Combine(_folder, new string('x', 60), "comp3.m");
        string shown = DesignComponentMetadata.ShortenedPath(longPath);
        Assert.Equal(76, shown.Length);
        Assert.EndsWith(Path.DirectorySeparatorChar + "comp3.m", shown, StringComparison.Ordinal);
        Assert.Contains("...", shown, StringComparison.Ordinal);
        Assert.Equal(@"C:\comps\comp3.m", DesignComponentMetadata.ShortenedPath(@"C:\comps\comp3.m"));
    }

    // --- the builtins --------------------------------------------------------------------------------

    [Theory]
    [InlineData("appdesigner.customcomponent.configureMetadata()", "MATLAB:narginchk:notEnoughInputs | Not enough input arguments.")]
    [InlineData("appdesigner.customcomponent.configureMetadata(1)", "MATLAB:appdesigner:appdesigner:InvalidInput | The input argument must be a full or partial file path, or a file name.")]
    [InlineData("appdesigner.customcomponent.configureMetadata({'comp1.m'})", "MATLAB:appdesigner:appdesigner:InvalidInput | The input argument must be a full or partial file path, or a file name.")]
    [InlineData("appdesigner.customcomponent.configureMetadata('for')", "MATLAB:appdesigner:appdesigner:FileNameFailsIsKeyword | Name cannot be a MATLAB keyword.  Rename file, and then open.")]
    [InlineData("appdesigner.customcomponent.configureMetadata('1bad.m')", "MATLAB:appdesigner:appdesigner:FileNameFailsIsVarName | File name '1bad' must start with a letter, contain only letters, numbers and underscores, and be shorter than 2048 characters.")]
    [InlineData("appdesigner.customcomponent.removeMetadata()", "MATLAB:narginchk:notEnoughInputs | Not enough input arguments.")]
    [InlineData("appdesigner.customcomponent.removeMetadata('comp1.m', 2)", "MATLAB:TooManyInputs | Too many input arguments.")]
    [InlineData("x = appdesigner.customcomponent.removeMetadata('comp1.m')", "MATLAB:TooManyOutputs | Too many output arguments.")]
    public async Task AFileIsRefusedInR2025bsWords(string statement, string expected)
    {
        Assert.Equal(expected, await Outcome(statement));
    }

    [Fact]
    public async Task AMissingFileIsNamedByItsFullPath()
    {
        // getValidatedCustomComponentFile names the file it looked for under the current folder (measured); with no extension .m then .mlapp are tried.
        Assert.Equal($"MATLAB:appdesigner:appdesigner:InvalidFileName | File '{Path.Combine(_folder, "nosuch.m")}' not found.", await Outcome("appdesigner.customcomponent.configureMetadata('nosuch.m')"));
        Assert.Equal($"MATLAB:appdesigner:appdesigner:InvalidFileName | File '{Path.Combine(_folder, "nosuch.mlapp")}' not found.", await Outcome("appdesigner.customcomponent.removeMetadata('nosuch')"));
    }

    [Fact]
    public async Task TheClassIsCheckedAndAnotherExtensionNamedRightly()
    {
        string plain = Path.Combine(_folder, "plainfn.m");
        string cls = Path.Combine(_folder, "PlainClass.m");
        Assert.Equal($"MATLAB:appdesigner:usercomponentmetadata:NotValidCustomComponentClass | File {plain} is not a valid custom UI component class.", await Outcome("appdesigner.customcomponent.configureMetadata('plainfn.m')"));
        Assert.Equal($"MATLAB:appdesigner:usercomponentmetadata:NotCustomComponentClass | File {cls} is not a valid custom UI component class because it does not inherit from matlab.ui.componentcontainer.ComponentContainer.", await Outcome("appdesigner.customcomponent.removeMetadata('PlainClass.m')"));
        // R2025b's message names the wrong extension ("must have a .txt extension", measured): JGraph names the two it takes.
        Assert.Equal("MATLAB:appdesigner:appdesigner:InvalidGeneralFileExtension | File 'comp1.txt' must have a .m or .mlapp extension.", await Outcome("appdesigner.customcomponent.configureMetadata('comp1.txt')"));
        // A class under a class of the user's that is a component is one too.
        Assert.Equal("ok", await Outcome("appdesigner.customcomponent.removeMetadata('Derived.m')"));
    }

    [Fact]
    public async Task ConfigureMetadataGoesThroughTheHostsDialogAndRemoveMetadataEditsTheRegistry()
    {
        // configureMetadata hands the validated full path to the host; a host refusal is the statement's error.
        Assert.Equal("ok", await Outcome("appdesigner.customcomponent.configureMetadata('comp1')"));
        Assert.Equal([Path.Combine(_folder, "comp1.m")], _designer.Configured);
        _designer.Refusal = "no dialog today";
        Assert.Equal("JGraph:appdesigner:CannotConfigure | no dialog today", await Outcome("appdesigner.customcomponent.configureMetadata('comp1.m')"));
        _designer.Refusal = null;

        // removeMetadata with no registry file does nothing (R2025b fails on its own isKey); an unreadable one is refused under R2025b's identifier.
        Assert.Equal("ok", await Outcome("appdesigner.customcomponent.removeMetadata('comp1.m')"));
        Assert.False(File.Exists(Registry));
        Directory.CreateDirectory(Path.GetDirectoryName(Registry)!);
        File.WriteAllText(Registry, "{not json");
        Assert.Equal($"{DesignComponentMetadata.InvalidModelIdentifier} | {DesignComponentMetadata.InvalidModelError}", await Outcome("appdesigner.customcomponent.removeMetadata('comp1.m')"));

        // A registered component is taken out and the rest kept; the file is written in R2025b's form.
        File.WriteAllText(Registry, "{\"components\":[{\"className\":\"comp1\",\"componentName\":\"One\",\"version\":\"1.0\",\"description\":\"\",\"category\":\"My Components\",\"icon\":\"\",\"authorName\":\"\",\"authorEmail\":\"\",\"avatar\":\"\",\"avatarDark\":\"\",\"defaultPosition\":[1,1,320,240]},{\"className\":\"comp3\",\"componentName\":\"Three\",\"version\":\"1.0\",\"description\":\"\",\"category\":\"My Components\",\"icon\":\"\",\"authorName\":\"\",\"authorEmail\":\"\",\"avatar\":\"\",\"avatarDark\":\"\",\"defaultPosition\":[1,1,320,240]}],\"schema\":3,\"MATLABRelease\":\"R2025b\"}");
        Assert.Equal("ok", await Outcome("appdesigner.customcomponent.removeMetadata('comp1.m')"));
        string text = File.ReadAllText(Registry);
        Assert.DoesNotContain("\"comp1\"", text, StringComparison.Ordinal);
        Assert.StartsWith("{\n  \"components\": [\n    {\n      \"className\": \"comp3\",\n      \"componentName\": \"Three\",", text, StringComparison.Ordinal);
        Assert.Equal("ok", await Outcome("appdesigner.customcomponent.removeMetadata('comp3.m')"));
        Assert.Equal("{\n  \"components\": [],\n  \"schema\": 3,\n  \"MATLABRelease\": \"R2025b\"\n}", File.ReadAllText(Registry));
    }

    // --- the check that makes the component -------------------------------------------------------------

    [Fact]
    public async Task TheCheckMakesTheComponentOnceAndSaysWhyNot()
    {
        var runner = (IScriptAppRunner)_session;
        DesignComponentCheck comp3 = await runner.CheckComponentAsync(Path.Combine(_folder, "comp3.m"), CancellationToken.None);
        Assert.Null(comp3.Error);
        Assert.Equal([1, 1, 320, 240], comp3.Position);
        DesignComponentCheck comp4 = await runner.CheckComponentAsync(Path.Combine(_folder, "comp4.m"), CancellationToken.None);
        Assert.Null(comp4.Error);
        Assert.Equal([1, 1, 200, 60], comp4.Position);
        Assert.Equal(DesignComponentMetadata.AbstractComponent("Abstr"), (await runner.CheckComponentAsync(Path.Combine(_folder, "Abstr.m"), CancellationToken.None)).Error);
        Assert.Equal(DesignComponentMetadata.RogueComponent, (await runner.CheckComponentAsync(Path.Combine(_folder, "Rogue.m"), CancellationToken.None)).Error);
        Assert.Equal(DesignComponentMetadata.CallbackHasValue(["TickedFcn"], ["Ticked"]), (await runner.CheckComponentAsync(Path.Combine(_folder, "Assigned.m"), CancellationToken.None)).Error);
        // The figures the checks made are gone.
        Assert.Equal("0", (await Outcome("disp(numel(findall(groot, 'Type', 'figure')))")).Split('\n')[0].Trim());
    }
}
