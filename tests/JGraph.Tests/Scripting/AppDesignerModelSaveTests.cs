using System.IO.Compression;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.MatFile;
using Xunit;
using Xunit.Abstractions;

namespace JGraph.Tests.Scripting;

/// <summary>
/// DS6 of the designer plan: App Designer's model read whole and written back, a saved tree brought
/// to an edited design while every object the edit did not touch stays as App Designer saved it.
/// </summary>
public class AppDesignerModelSaveTests(ITestOutputHelper output)
{
    private const string ShippedRoot = @"C:\Program Files\MATLAB\R2025b\toolbox";
    private const string Model = "appdesigner/appModel.mat";

    private static byte[] ModelOf(string mlapp)
    {
        using ZipArchive package = ZipFile.OpenRead(mlapp);
        using Stream stream = package.GetEntry(Model)!.Open();
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    private static IEnumerable<string> ShippedApps() =>
        Directory.Exists(ShippedRoot) ? Directory.EnumerateFiles(ShippedRoot, "*.mlapp", SearchOption.AllDirectories) : [];

    /// <summary>
    /// The file as a list of lines that do not depend on how its objects are numbered: each
    /// variable and object in the order the variables reach them, a stored value as its bytes with
    /// every object id replaced by the referred object's place in that order.
    /// </summary>
    private static List<string> Canonical(MatMcosFile file)
    {
        var ids = new Dictionary<McosObject, int>(ReferenceEqualityComparer.Instance);
        var order = new List<McosObject>();
        void Reach(IEnumerable<McosObject?> targets)
        {
            foreach (McosObject? t in targets)
            {
                if (t is not null && !ids.ContainsKey(t))
                {
                    ids[t] = order.Count + 1;
                    order.Add(t);
                    foreach ((_, McosValue v) in t.Entries)
                    {
                        Reach(Targets(v));
                    }

                    Reach(t.Dynamic);
                }
            }
        }

        foreach (MatData.Kept v in file.Variables)
        {
            Reach(v.References.SelectMany(static r => r.Targets));
        }

        foreach (MatData d in file.Seed.ClassCells.Concat(file.Seed.ClassDefaults))
        {
            Reach(d is MatData.Kept k ? k.References.SelectMany(static r => r.Targets) : []);
        }

        string Show(MatData data)
        {
            if (data is not MatData.Kept kept)
            {
                return data.ToString()!;
            }

            byte[] bytes = (byte[])kept.Element.Clone();
            foreach (McosReference r in kept.References)
            {
                for (int i = 0; i < r.Targets.Count; i++)
                {
                    if (r.Targets[i] is { } t)
                    {
                        BitConverter.TryWriteBytes(bytes.AsSpan(r.IdsAt + (4 * i)), (uint)ids[t]);
                    }
                }
            }

            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        }

        var lines = new List<string>();
        lines.AddRange(file.Variables.Select(v => "var " + Show(v)));
        lines.AddRange(file.Seed.ClassDefaults.Select(d => "default " + Show(d)));
        foreach (McosObject o in order)
        {
            lines.Add($"object {ids[o]} {o.ClassName} saved={o.SavedForm} renamed={string.Join(',', o.Renamed)} dynamic={string.Join(',', o.Dynamic.Select(d => ids[d]))}");
            foreach ((string name, McosValue value) in o.Entries)
            {
                lines.Add("  " + name + " " + value switch
                {
                    McosValue.Word w => "word " + w.Text,
                    McosValue.Literal l => "literal " + l.Number,
                    McosValue.Value v => "value " + Show(v.Data),
                    McosValue.Refs r => "refs " + string.Join(',', r.Objects.Select(x => ids[x])),
                    _ => "?",
                });
            }
        }

        return lines;
    }

    private static IEnumerable<McosObject?> Targets(McosValue value) => value switch
    {
        McosValue.Refs refs => refs.Objects,
        McosValue.Value { Data: MatData.Kept kept } => kept.References.SelectMany(static r => r.Targets),
        _ => [],
    };

    /// <summary>
    /// Every level-5 model R2025b ships reads as objects and writes back as the same objects: the
    /// same variables, the same objects in the same forms with the same stored names and values,
    /// numbered as the variables reach them; and writing what was written changes no byte.
    /// </summary>
    [Fact]
    public void EveryShippedModelWritesBackAsTheSameObjects()
    {
        int models = 0;
        var unread = new List<string>();
        foreach (string app in ShippedApps())
        {
            byte[] model = ModelOf(app);
            if (MatMcosFile.Read(model, out string? why) is not { } file)
            {
                if (!System.Text.Encoding.ASCII.GetString(model, 0, 10).StartsWith("MATLAB 7.3", StringComparison.Ordinal))
                {
                    unread.Add(Path.GetFileName(app) + ": " + why);
                }

                continue;
            }

            models++;
            byte[] written = file.Write();
            MatMcosFile again = MatMcosFile.Read(written) ?? throw new InvalidOperationException(Path.GetFileName(app) + ": unreadable when written");
            Assert.Equal(Canonical(file), Canonical(again));
            Assert.Equal(written, again.Write());
            Assert.Equal(file.Objects.Count, again.Objects.Count);
        }

        output.WriteLine($"{models} models");
        Assert.Empty(unread);
        Assert.True(models >= 40 || !Directory.Exists(ShippedRoot), $"{models} models");
    }

    /// <summary>
    /// A tree that holds none of App Designer's design-time records - the project's U7b sample,
    /// saved by MATLAB's serializer from a figure its code made - has no saved component to match by
    /// code name: brought to its design, every component is made anew.
    /// </summary>
    [Fact]
    public void ATreeWithoutAppDesignersRecordsIsWrittenAnew()
    {
        string app = Helper("U7bApp.mlapp");
        byte[] model = ModelOf(app);
        DesignReadResult read = DesignCodeReader.Read(JgsMlapp.ReadCode(app));
        DesignTreeUpdate update = DesignTreeWriter.Update(model, read.Design!, read.Style, Path.GetDirectoryName(app));
        string told = $"changed [{string.Join("; ", update.Changed)}] added [{string.Join("; ", update.Added)}] "
            + $"rebuilt [{string.Join("; ", update.Rebuilt)}] removed [{string.Join("; ", update.Removed)}] problems [{string.Join("; ", update.Problems)}]";
        Assert.True(update.IsUpdated, told);
        Assert.Empty(update.Kept);
        Assert.Equal(read.Design!.Components.Select(static c => c.CodeName), update.Added);
    }

    /// <summary>
    /// Brought to the design its own code describes, every shipped app's tree keeps every saved
    /// component as it was; what the generator here writes otherwise than R2025b stored is listed.
    /// </summary>
    [Fact]
    public void EveryShippedTreeKeepsItsComponentsUnderItsOwnCode()
    {
        int apps = 0;
        int kept = 0;
        var changed = new List<string>();
        foreach (string app in ShippedApps())
        {
            byte[] model = ModelOf(app);
            if (MatMcosFile.Read(model) is null)
            {
                continue;
            }

            string text = JgsMlapp.ReadCode(app);
            DesignReadResult read = DesignCodeReader.Read(text);
            Assert.NotNull(read.Design);
            DesignTreeUpdate update = DesignTreeWriter.Update(model, read.Design!, read.Style, Path.GetDirectoryName(app));
            Assert.True(update.IsUpdated, string.Join("; ", update.Problems));
            apps++;
            kept += update.Kept.Count;
            changed.AddRange(update.Changed.Select(c => $"{Path.GetFileNameWithoutExtension(app)}.{c}"));
            changed.AddRange(update.Rebuilt.Select(c => $"{Path.GetFileNameWithoutExtension(app)}.{c} (rebuilt)"));
            Assert.Empty(update.Removed);
        }

        output.WriteLine($"{apps} apps, {kept} components kept, {changed.Count} written over:");
        foreach (string c in changed)
        {
            output.WriteLine("  " + c);
        }

        Assert.Empty(changed);
    }

    /// <summary>The app's text with its component properties block and <c>createComponents</c> written anew from <paramref name="design"/>.</summary>
    private static string Regenerate(string text, AppDesign design, DesignCodeStyle style)
    {
        string[] lines = text.Split('\n');
        int blockStart = Array.FindIndex(lines, static l => l.Trim() == "% Properties that correspond to app components");
        int blockEnd = Array.FindIndex(lines, blockStart, static l => l.Trim() == "end");
        int methodStart = Array.FindIndex(lines, static l => l.Trim() == "function createComponents(app)");
        string indent = lines[methodStart][..lines[methodStart].IndexOf('f', StringComparison.Ordinal)];
        int methodEnd = Array.FindIndex(lines, methodStart, l => l == indent + "end");
        var result = new List<string>();
        result.AddRange(lines[..blockStart]);
        result.AddRange(DesignCodeWriter.PropertiesBlock(design, style));
        result.AddRange(lines[(blockEnd + 1)..(methodStart - 1)]);
        result.AddRange(DesignCodeWriter.Method(design, style));
        result.AddRange(lines[(methodEnd + 1)..]);
        return string.Join('\n', result);
    }

    /// <summary>
    /// Each saved component as lines that do not depend on how objects are numbered: its stored
    /// names in order with their values, an object a value refers to written as the component's code
    /// name when it is one, and otherwise as its own lines, nested.
    /// </summary>
    private static Dictionary<string, List<string>> ComponentsByCodeName(MatMcosFile file)
    {
        var names = new Dictionary<McosObject, string>(ReferenceEqualityComparer.Instance);
        foreach (McosObject o in file.Objects)
        {
            foreach (McosObject d in o.Dynamic)
            {
                if (d.Get("DynamicValue_") is McosValue.Value { Data: MatData.Kept k }
                    && MatRaw.Plain(k.Element, file.Keep) is MatData.Structs { Elements: [var values] } s
                    && Array.IndexOf(s.Fields, "CodeName") is >= 0 and var at && values[at] is MatData.Chars name)
                {
                    names[o] = name.Characters;
                }
            }
        }

        List<string> Lines(McosObject o, int depth, HashSet<McosObject> seen)
        {
            var lines = new List<string> { $"{new string(' ', depth)}{o.ClassName} renamed={string.Join(',', o.Renamed)}" };
            if (!seen.Add(o) || depth > 12)
            {
                return lines;
            }

            string Ref(McosObject? t) => t is null ? "null" : names.TryGetValue(t, out string? n) ? "component " + n : "object";
            foreach ((string name, McosValue value) in o.Entries)
            {
                IEnumerable<McosObject?> targets = value switch
                {
                    McosValue.Refs r => r.Objects,
                    McosValue.Value { Data: MatData.Kept k } => k.References.SelectMany(static r => r.Targets),
                    _ => [],
                };
                string shown = value switch
                {
                    McosValue.Word w => "word " + w.Text,
                    McosValue.Literal l => "literal " + l.Number,
                    McosValue.Value { Data: MatData.Kept k } => "value " + Hash(k),
                    McosValue.Value v => "value " + v.Data,
                    McosValue.Refs r => "refs",
                    _ => "?",
                };
                lines.Add($"{new string(' ', depth)} {name} {shown} -> {string.Join(',', targets.Select(Ref))}");
                foreach (McosObject t in targets.OfType<McosObject>().Where(t => !names.ContainsKey(t)))
                {
                    lines.AddRange(Lines(t, depth + 2, seen));
                }
            }

            foreach (McosObject d in o.Dynamic)
            {
                lines.AddRange(Lines(d, depth + 2, seen));
            }

            return lines;
        }

        static string Hash(MatData.Kept k)
        {
            byte[] bytes = (byte[])k.Element.Clone();
            foreach (McosReference r in k.References)
            {
                for (int i = 0; i < r.Targets.Count; i++)
                {
                    BitConverter.TryWriteBytes(bytes.AsSpan(r.IdsAt + (4 * i)), 0u);
                }
            }

            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..16];
        }

        return names.ToDictionary(static p => p.Value, p => Lines(p.Key, 0, new HashSet<McosObject>(ReferenceEqualityComparer.Instance)));
    }

    private static readonly HashSet<string> TextClasses = new(StringComparer.Ordinal)
    {
        "matlab.ui.control.Label", "matlab.ui.control.Button", "matlab.ui.control.CheckBox",
    };

    /// <summary>
    /// A shipped app edited here - one component's text changed, a label removed, a button added to
    /// the figure - and saved back: its tree has the change written over the changed component's
    /// saved object, the new button made from its class's base, the label gone; every other saved
    /// component is, object for object, what App Designer saved, but for the children of the
    /// figure and of the removed label's container. With <c>JGRAPH_DS6_OUT</c> naming a folder, the
    /// original and the edited file are put there for the probe <c>ds6_verify</c> to hand to R2025b.
    /// </summary>
    [Fact]
    public void AnEditedShippedAppKeepsEveryOtherComponentAsSaved()
    {
        string? outFolder = Environment.GetEnvironmentVariable("JGRAPH_DS6_OUT");
        string temp = Path.Combine(Path.GetTempPath(), $"jgraph-ds6-{Guid.NewGuid():N}");
        int apps = 0;
        int index = 0;
        var skipped = new List<string>();
        try
        {
            foreach (string app in ShippedApps())
            {
                string name = Path.GetFileNameWithoutExtension(app);
                byte[] model = ModelOf(app);
                if (MatMcosFile.Read(model) is not { } before)
                {
                    continue;
                }

                string text = JgsMlapp.ReadCode(app);
                DesignReadResult read = DesignCodeReader.Read(text);
                AppDesign design = read.Design!;
                if (read.Problems.Count > 0)
                {
                    skipped.Add($"{name}: {read.Problems[0]}");
                    continue;
                }

                // The edits.
                List<DesignComponent> all = design.Components.ToList();
                bool Named(DesignComponent c) => all.Any(o => o.Properties.Any(p => p.Value is ComponentsValue v && v.CodeNames.Contains(c.CodeName)));
                DesignComponent? changed = all.FirstOrDefault(c => TextClasses.Contains(c.Type));
                DesignComponent? doomed = all.LastOrDefault(c => c.Type == "matlab.ui.control.Label" && c != changed && c.Children.Count == 0 && !Named(c));
                changed?.Set("Text", new CharValue("Edited in JGraph"));
                DesignComponent? doomedParent = doomed?.Parent;
                doomedParent?.Remove(doomed!);
                var added = new DesignComponent("JGraphAddedButton", DesignCatalog.R2025b.ByType("matlab.ui.control.Button")!);
                added.Set("Text", new CharValue("Added"));
                added.Set("Position", NumericValue.Row(10, 10, 100, 22));
                design.Figure.Add(added);

                // Two shipped apps share a name: each gets a folder of its own.
                string key = $"{++index:D2}_{name}";
                string folder = Path.Combine(temp, key);
                Directory.CreateDirectory(folder);
                string copy = Path.Combine(folder, name + ".mlapp");
                File.Copy(app, copy);
                File.SetAttributes(copy, FileAttributes.Normal);
                MlappSaveResult saved = JgsMlapp.WriteCode(copy, Regenerate(text, design, read.Style));
                DesignTreeUpdate tree = saved.Tree ?? throw new InvalidOperationException(name + ": the tree was not touched");
                Assert.True(tree.IsUpdated, name + ": " + string.Join(" ", tree.Problems));
                apps++;

                Assert.Equal(changed is null ? [] : [$"{changed.CodeName} (Text)"], tree.Changed);
                Assert.Equal(["JGraphAddedButton"], tree.Added);
                Assert.Equal(doomed is null ? [] : [doomed.CodeName], tree.Removed);
                Assert.Empty(tree.Rebuilt);

                Dictionary<string, List<string>> was = ComponentsByCodeName(before);
                Dictionary<string, List<string>> now = ComponentsByCodeName(MatMcosFile.Read(ModelOf(copy))!);
                foreach ((string codeName, List<string> lines) in was)
                {
                    if (codeName == doomed?.CodeName)
                    {
                        Assert.False(now.ContainsKey(codeName), $"{name}.{codeName} is still in the tree");
                        continue;
                    }

                    Assert.True(now.ContainsKey(codeName), $"{name}.{codeName} left the tree");
                    if (codeName == changed?.CodeName || codeName == design.Figure.CodeName || codeName == doomedParent?.CodeName)
                    {
                        continue;
                    }

                    if (!lines.SequenceEqual(now[codeName]))
                    {
                        int at = lines.Zip(now[codeName]).TakeWhile(static p => p.First == p.Second).Count();
                        Assert.Fail($"{name}.{codeName} is not as App Designer saved it: line {at} was "
                            + $"'{lines.ElementAtOrDefault(at)}', is '{now[codeName].ElementAtOrDefault(at)}'");
                    }
                }

                if (outFolder is not null)
                {
                    string into = Path.Combine(outFolder, "edits", key);
                    Directory.CreateDirectory(into);
                    File.Copy(app, Path.Combine(into, name + "__orig.mlapp"), overwrite: true);
                    File.Copy(copy, Path.Combine(into, name + "__edit.mlapp"), overwrite: true);
                    File.WriteAllLines(Path.Combine(into, "edits.txt"),
                    [
                        "changed " + (changed?.CodeName ?? string.Empty),
                        "removed " + (doomed?.CodeName ?? string.Empty),
                        "added JGraphAddedButton",
                    ]);
                }
            }
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }

        output.WriteLine($"{apps} apps edited; skipped: {string.Join("; ", skipped)}");
        Assert.True(apps >= 30 || !Directory.Exists(ShippedRoot), $"{apps} apps");
    }

    /// <summary>
    /// An app built with the designer's code edits (DS5): a button with its callback, an edit field
    /// bound to a second callback and to the button's as well, a startup function taking two input
    /// parameters, a single running instance, and a property and a function of the user's; what the
    /// probe <c>ds5_run</c> runs in R2025b.
    /// </summary>
    internal static string CallbacksAppText()
    {
        static DesignComponent Make(string type, string name, params double[] position)
        {
            var component = new DesignComponent(name, DesignCatalog.R2025b.ByType(type)!);
            component.Set("Position", NumericValue.Row(position));
            return component;
        }

        DesignComponent figure = Make("matlab.ui.Figure", "UIFigure", 100, 100, 640, 480);
        figure.Set("Name", new CharValue("MATLAB App"));
        var design = new AppDesign(figure);
        figure.Add(Make("matlab.ui.control.Button", "Button", 20, 420, 100, 23));
        figure.Add(Make("matlab.ui.control.EditField", "EditField", 20, 380, 100, 22));
        figure.Add(Make("matlab.ui.control.Label", "Label", 140, 420, 200, 22));
        using var document = DesignDocument.FromText(DesignClassWriter.Render(design, new DesignCode("CallbacksApp")), null);
        DesignComponent button = document.Design.Find("Button")!;
        DesignComponent field = document.Design.Find("EditField")!;
        Assert.Null(document.AddCallback(button, "ButtonPushedFcn"));
        Assert.Null(document.AddCallback(field, "ValueChangedFcn"));
        Assert.Null(document.BindCallback(field, "ValueChangingFcn", "ButtonPushed"));
        Assert.Null(document.SetInputParameters("a, b"));
        document.SetSingleInstance(true);
        Assert.Equal("Property", document.InsertUserProperty("private"));
        Assert.Equal("func", document.InsertUserFunction("private"));
        string text = document.CurrentText
            .Replace("        Property % Description", "        Count = 0 % how often the button was pushed", StringComparison.Ordinal)
            .Replace("        function results = func(app)\n            \n", "        function results = func(app)\n            results = app.Count * 10;\n", StringComparison.Ordinal)
            .Replace("        function startupFcn(app, a, b)\n            \n", "        function startupFcn(app, a, b)\n            app.Count = a + b;\n            app.Label.Text = sprintf('%d', app.Count);\n", StringComparison.Ordinal)
            .Replace("        function ButtonPushed(app, event)\n            \n", "        function ButtonPushed(app, event)\n            app.Count = app.Count + 1;\n            app.Label.Text = sprintf('%d', func(app));\n", StringComparison.Ordinal);
        Assert.True(document.ApplyEditedText(text));
        Assert.Equal(text, document.CurrentText);
        return text;
    }

    /// <summary>
    /// A new app written here holds App Designer's whole model: the tree, the copy of the code App
    /// Designer reads (class name, editable section, callbacks, startup function) and the app data,
    /// with the metadata parts; brought to its own design again, nothing in its tree changes. Every
    /// shipped app's code and the fixture app are written so; with <c>JGRAPH_DS6_OUT</c> they are
    /// put there for <c>ds6_verify</c>.
    /// </summary>
    [Fact]
    public void ANewAppHoldsAppDesignersWholeModel()
    {
        string? outFolder = Environment.GetEnvironmentVariable("JGRAPH_DS6_OUT");
        string temp = Path.Combine(Path.GetTempPath(), $"jgraph-ds6-{Guid.NewGuid():N}");
        var sources = new List<(string Name, string Text)>
        {
            ("DesignerSpecApp", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripting", "Designer", "DesignerSpecApp.m"))),
            ("NewApp", JGraph.Scripting.Workspace.AppDesignerDocument.NewAppText().Replace("app1", "NewApp", StringComparison.Ordinal)),
            ("CallbacksApp", CallbacksAppText()),
        };
        sources.AddRange(ShippedApps().Select(static a => (Path.GetFileNameWithoutExtension(a), JgsMlapp.ReadCode(a))));
        int written = 0;
        try
        {
            foreach ((string name, string text) in sources)
            {
                DesignReadResult read = DesignCodeReader.Read(text);
                if (read.Design is null || read.Problems.Count > 0)
                {
                    continue;
                }

                // Written where the probe reads it, since the tree records an image's full path.
                string key = $"{++written:D2}_{name}";
                string folder = Path.Combine(outFolder is null ? temp : Path.Combine(outFolder, "new"), key);
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, name + ".mlapp");
                MlappWriteResult result = JgsMlapp.WriteApp(path, text);
                Assert.True(result.HasDesign, name);

                using (ZipArchive package = ZipFile.OpenRead(path))
                {
                    Assert.Equal(
                        ["[Content_Types].xml", "_rels/.rels", "matlab/document.xml", Model, "metadata/appMetadata.xml",
                            "metadata/coreProperties.xml", "metadata/mwcoreProperties.xml", "metadata/mwcorePropertiesExtension.xml"],
                        package.Entries.Select(static e => e.FullName));
                }

                byte[] model = ModelOf(path);
                MatMcosFile file = MatMcosFile.Read(model) ?? throw new InvalidOperationException(name + ": the model does not read");
                Assert.Equal(["components", "code", "appData"], file.Variables.Select(static v => MatRaw.NameOf(v.Element)));
                JgsMlappLayout layout = JgsMlappLayout.Of(text.Replace("\r\n", "\n", StringComparison.Ordinal));
                MlappCodeCopy copy = JgsMlappModel.Read(model) ?? throw new InvalidOperationException(name + ": no code copy");
                Assert.Equal(layout.ClassName, copy.ClassName);
                Assert.Equal(layout.Callbacks.Select(static r => r.Name), copy.Callbacks.Select(static c => c.Name));
                Assert.Equal(layout.Startup?.Name, copy.Startup?.Name);

                DesignTreeUpdate again = DesignTreeWriter.Update(model, read.Design, read.Style, folder);
                Assert.True(again.IsUpdated, name);
                Assert.Same(model, again.Bytes);
                Assert.Empty(again.Changed);

                if (outFolder is not null)
                {
                    // The control: createComponents as App Designer's generator writes it.
                    File.WriteAllLines(Path.Combine(folder, name + "__code.m"), DesignCodeWriter.Method(read.Design, read.Style));
                }
            }
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }

        output.WriteLine($"{written} new apps written");
        Assert.True(written >= 2);
    }

    private static string Helper(string name) => Path.Combine(AppContext.BaseDirectory, "MatlabParity", "fixtures", "helpers", name);

    /// <summary>
    /// A model kept as a version 7.3 (HDF5) file holds its tree in a form this does not rewrite: an
    /// edit to createComponents is saved and runs, the copy of the code is spliced (ADR 0239), the
    /// tree is left as it was, and the status line says App Designer will show the components it had.
    /// </summary>
    [Fact]
    public void AVersion73ModelsTreeIsLeft_AndTheSaveSaysSo()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"jgraph-ds6-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            string app = Path.Combine(folder, "U7bApp73.mlapp");
            File.Copy(Helper("U7bApp73.mlapp"), app);
            string text = JgsMlapp.ReadCode(app);
            string edited = text.Replace("app.GoButton.Text = 'Go';", "app.GoButton.Text = 'Run';", StringComparison.Ordinal);
            Assert.NotEqual(text, edited);

            MlappSaveResult saved = JgsMlapp.WriteCode(app, edited);

            Assert.Equal(edited, JgsMlapp.ReadCode(app));
            Assert.True(saved.GeneratedCodeChanged);
            Assert.NotNull(saved.Tree);
            Assert.False(saved.Tree.IsUpdated);
            Assert.Contains("version 7.3", saved.Tree.Problems[0], StringComparison.Ordinal);
            Assert.Contains("App Designer will show the components it had", JGraph.Scripting.Workspace.AppDesignerDocument.StatusNote(saved), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// File > New Script > MATLAB app opens App Designer's blank app: a class whose figure is its
    /// only component, which the designer reads and lays out as App Designer does; saved, it takes
    /// the file's name and is written whole, and the status line says App Designer opens it.
    /// </summary>
    [Fact]
    public void ABlankAppIsLaidOutAsAppDesignerLaysItOut_AndIsSavedWhole()
    {
        string text = JGraph.Scripting.Workspace.AppDesignerDocument.NewAppText();
        JgsMlappLayout layout = JgsMlappLayout.Of(text);
        Assert.True(layout.IsAppLayout && layout.IsComplete);
        Assert.Equal("app1", layout.ClassName);
        DesignReadResult read = DesignCodeReader.Read(text);
        Assert.Empty(read.Problems);
        Assert.Equal(["UIFigure"], read.Design!.Components.Select(static c => c.CodeName));
        Assert.Equal(DesignCodeWriter.Method(read.Design), text.Split('\n').SkipWhile(static l => l.Trim() != "% Create UIFigure and components").Take(DesignCodeWriter.Method(read.Design).Count));

        var document = new JGraph.Scripting.Workspace.ScriptDocumentModel(null, text, "MATLAB") { IsNewApp = true };
        Assert.Equal("NewApp.mlapp", document.FileName);

        string folder = Path.Combine(Path.GetTempPath(), $"jgraph-ds6-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            string target = Path.Combine(folder, "Blank.mlapp");
            (string saved, MlappWriteResult result) = JGraph.Scripting.Workspace.AppDesignerDocument.SaveNew(target, text);
            Assert.StartsWith("classdef Blank < matlab.apps.AppBase", saved, StringComparison.Ordinal);
            Assert.Contains("function app = Blank", saved, StringComparison.Ordinal);
            Assert.True(result.HasDesign);
            Assert.Empty(result.Problems);
            Assert.Contains("App Designer opens it with its canvas", JGraph.Scripting.Workspace.AppDesignerDocument.NewFileNote(result), StringComparison.Ordinal);
            Assert.Equal("Blank", JgsMlappModel.Read(ModelOf(target))!.ClassName);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>The status line after a save that wrote the tree says what changed in it, and that the rest is as App Designer saved it.</summary>
    [Fact]
    public void TheStatusLineSaysWhatTheTreeSaveDid()
    {
        var tree = new DesignTreeUpdate([1], ["A", "B"], ["Label (Text)"], ["New1", "New2"], [], ["Gone"], []);
        string note = JGraph.Scripting.Workspace.AppDesignerDocument.StatusNote(new MlappSaveResult(true, true, true, tree));
        Assert.Contains("component tree was brought up to date (1 component changed, 2 added, 1 removed)", note, StringComparison.Ordinal);
        Assert.Contains("every other component is as App Designer saved it", note, StringComparison.Ordinal);

        var left = new DesignTreeUpdate(null, [], [], [], [], [], ["Its createComponents could not be read."]);
        Assert.Contains("could not be brought up to date (Its createComponents could not be read.)",
            JGraph.Scripting.Workspace.AppDesignerDocument.StatusNote(new MlappSaveResult(true, true, true, left)), StringComparison.Ordinal);
        Assert.Contains("A change to createComponents", JGraph.Scripting.Workspace.AppDesignerDocument.GeneratedCodeNotice, StringComparison.Ordinal);
    }

    /// <summary>
    /// A property whose line went is set back over the saved object - its value the default's, its
    /// mode stored <c>'auto'</c> last - and a component moved out of a grid, which cannot be written
    /// over its saved object, is made anew in that object's place among its new container's
    /// children, keeping nothing of the old object but its group; it is not listed as removed.
    /// </summary>
    [Fact]
    public void ALineThatWentSetsItsPropertyBack_AndAMoveOutOfAGridMakesTheComponentAnew()
    {
        DesignCatalog catalog = DesignCatalog.R2025b;
        var figure = new DesignComponent("UIFigure", catalog.ByType("matlab.ui.Figure")!);
        figure.Set("Position", NumericValue.Row(100, 100, 640, 480));
        var design = new AppDesign(figure);
        var grid = new DesignComponent("GridLayout", catalog.ByType("matlab.ui.container.GridLayout")!);
        figure.Add(grid);
        var inGrid = new DesignComponent("GridLabel", catalog.ByType("matlab.ui.control.Label")!);
        inGrid.Set("Text", new CharValue("In the grid"));
        inGrid.Set("Layout.Row", NumericValue.Scalar(1));
        inGrid.Set("Layout.Column", NumericValue.Scalar(1));
        grid.Add(inGrid);
        var coloured = new DesignComponent("RedLabel", catalog.ByType("matlab.ui.control.Label")!);
        coloured.Set("Text", new CharValue("Red"));
        coloured.Set("FontColor", NumericValue.Row(1, 0, 0));
        coloured.Set("Layout.Row", NumericValue.Scalar(2));
        coloured.Set("Layout.Column", NumericValue.Scalar(1));
        grid.Add(coloured);

        McosObject root = DesignTreeWriter.Tree(design, DesignCodeStyle.Default, null, []);
        byte[] model = MatMcosWriter.WriteFile([(DesignTreeWriter.Variable, MatData.Struct((DesignTreeWriter.FigureField, new MatData.Object(root))))], null, compress: true);

        coloured.Unset("FontColor");
        grid.Remove(inGrid);
        inGrid.Unset("Layout.Row");
        inGrid.Unset("Layout.Column");
        inGrid.Set("Position", NumericValue.Row(20, 20, 100, 22));
        figure.Add(inGrid);
        DesignTreeUpdate update = DesignTreeWriter.Update(model, design);

        Assert.Equal(["RedLabel (FontColor set back)"], update.Changed);
        Assert.Equal(["GridLabel"], update.Rebuilt);
        Assert.Empty(update.Removed);
        Assert.Empty(update.Added);

        MatMcosFile file = MatMcosFile.Read(update.Bytes!)!;
        McosObject red = file.Objects.Single(o => o.ClassName == "matlab.ui.control.Label" && Has(file, o, "RedLabel"));
        Assert.Equal(("FontColorMode", (McosValue)new McosValue.Word("auto")), red.Entries[^1]);
        Assert.Equal("FontColor", red.Entries[^2].Name);

        // The figure's children: the moved label in front (made last), then the grid.
        McosObject newFigure = file.Objects.Single(static o => o.ClassName == "matlab.ui.Figure");
        Assert.True(newFigure.Get("matlab.ui.container.Container.SerializableChildren") is McosValue.Value { Data: MatData.Kept kids }
            && kids.References.SelectMany(static r => r.Targets).Select(t => Has(file, t!, "GridLabel") ? "GridLabel" : t!.ClassName).SequenceEqual(
                ["GridLabel", "matlab.ui.container.GridLayout"]));

        static bool Has(MatMcosFile file, McosObject o, string codeName) =>
            o.Dynamic.Any(d => d.Get("DynamicValue_") is McosValue.Value { Data: MatData.Kept k }
                && MatRaw.Plain(k.Element, file.Keep) is MatData.Structs { Elements: [var v] } s
                && v[Array.IndexOf(s.Fields, "CodeName")] is MatData.Chars { Characters: var c } && c == codeName);
    }

    /// <summary>
    /// A responsive app's grid on the figure and the panels in it carry App Designer's record that
    /// they are its responsive containers, without which App Designer's reader refuses the app.
    /// </summary>
    [Fact]
    public void AResponsiveAppsGridAndPanelsAreMarkedAsItsContainers()
    {
        string app = Path.Combine(ShippedRoot, "slrealtime", "slrealtime", "+slrealtime", "+internal", "+guis", "SystemLogViewer.mlapp");
        if (!File.Exists(app))
        {
            return;
        }

        DesignReadResult read = DesignCodeReader.Read(JgsMlapp.ReadCode(app));
        Assert.True(read.Design!.IsResponsive);
        McosObject root = DesignTreeWriter.Tree(read.Design, read.Style, null, []);
        var marked = new List<string>();
        void Walk(McosObject o)
        {
            foreach (McosObject d in o.Dynamic)
            {
                if (d.Get("DynamicValue_") is McosValue.Value { Data: MatData.Structs { Elements: [var v] } s }
                    && Array.IndexOf(s.Fields, "AppTypeProperties") is >= 0 and var at
                    && v[at] is MatData.Structs { Fields: ["IsResponsiveContainer"], Elements: [[MatData.Numbers { ClassName: "logical", Data: [1] }]] })
                {
                    marked.Add(((MatData.Chars)v[Array.IndexOf(s.Fields, "CodeName")]).Characters);
                }
            }

            if (o.Get("matlab.ui.container.Container.SerializableChildren") is McosValue.Refs kids)
            {
                foreach (McosObject k in kids.Objects)
                {
                    Walk(k);
                }
            }
        }

        Walk(root);
        Assert.Equal(["GridLayout", "LeftPanel", "RightPanel"], marked.Order(StringComparer.Ordinal));
    }
}
