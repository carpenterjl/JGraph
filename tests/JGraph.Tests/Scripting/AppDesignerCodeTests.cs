using System.IO.Compression;
using System.Text.Json;
using JGraph.Core.Primitives;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Workspace;
using Xunit;
using Xunit.Abstractions;

namespace JGraph.Tests.Scripting;

/// <summary>
/// DS5 of the designer plan: the app's code around its components - the callbacks block, the
/// startup function and the app's input parameters, the constructor as a single instance or not,
/// <c>delete</c>, the editable section's templates - read from a class and written again as
/// R2025b's App Designer writes it, checked against its own code run under node
/// (<c>tools/matlab-checklist/ui-probes/ds5/oracle</c>) and against the apps R2025b ships; and the
/// document's edits of that code.
/// </summary>
[Collection("JG facade")]
public class AppDesignerCodeTests(ITestOutputHelper output)
{
    private const string ShippedRoot = @"C:\Program Files\MATLAB\R2025b\toolbox";

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Scripting", "Designer", name);

    private static string Helper(string name) => Path.Combine(AppContext.BaseDirectory, "MatlabParity", "fixtures", "helpers", name);

    private static JsonDocument Oracle(string name) => JsonDocument.Parse(File.ReadAllText(Fixture(name)));

    private static (AppDesign Design, DesignCode Code, DesignCodeStyle Style) Read(string text)
    {
        DesignReadResult read = DesignCodeReader.Read(text);
        Assert.True(read.IsComplete, string.Join("\n", read.Problems));
        DesignCode code = DesignCode.Read(text) ?? throw new Xunit.Sdk.XunitException("the text is not laid out as an app");
        return (read.Design!, code, read.Style);
    }

    private static DesignComponent Make(string type, string name, params double[] position)
    {
        var component = new DesignComponent(name, DesignCatalog.R2025b.ByType(type) ?? throw new InvalidOperationException(type));
        if (position.Length == 4)
        {
            component.Set("Position", NumericValue.Row(position));
        }

        return component;
    }

    /// <summary>A 640-by-480 figure with a button and an edit field, as a class text with no code of its own.</summary>
    private static string SmallAppText()
    {
        DesignComponent figure = Make("matlab.ui.Figure", "UIFigure", 100, 100, 640, 480);
        figure.Set("Name", new CharValue("MATLAB App"));
        var design = new AppDesign(figure);
        figure.Add(Make("matlab.ui.control.Button", "Button", 20, 420, 100, 23));
        figure.Add(Make("matlab.ui.control.EditField", "EditField", 20, 380, 100, 22));
        return DesignClassWriter.Render(design, new DesignCode("app1"));
    }

    private static string[] Lines(string text) => text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

    // --- the shipped apps and the fixture ------------------------------------------------------------

    /// <summary>
    /// The fixture app (whose <c>createComponents</c> is written by hand, out of the generator's
    /// order, so that part is the writer's to put in order) comes back from its design and code
    /// model with its callback, constructor and <c>delete</c> as it has them, and a second reading
    /// and writing changes nothing.
    /// </summary>
    [Fact]
    public void TheFixtureAppsCodeAroundItsComponentsIsWrittenBack()
    {
        string text = File.ReadAllText(Fixture("DesignerSpecApp.m"));
        (AppDesign design, DesignCode code, DesignCodeStyle style) = Read(text);
        string[] expected = Lines(text);
        string[] written = DesignClassWriter.Lines(design, code, style).ToArray();

        int function = Array.FindIndex(expected, static l => l.Trim() == "function createComponents(app)");
        AssertSameLines(expected[..(function - 1)], written[..(function - 1)], "DesignerSpecApp.m before createComponents");
        int lifecycle = Array.FindIndex(expected, static l => l.Trim() == "% App creation and deletion");
        int writtenLifecycle = Array.FindIndex(written, static l => l.Trim() == "% App creation and deletion");
        AssertSameLines(expected[lifecycle..], written[writtenLifecycle..], "DesignerSpecApp.m from the lifecycle block");

        string once = string.Join('\n', written);
        (AppDesign again, DesignCode againCode, DesignCodeStyle againStyle) = Read(once);
        Assert.Equal(written, DesignClassWriter.Lines(again, againCode, againStyle));
    }

    /// <summary>
    /// A new app's text and a new responsive app's are the class writer's: the figure alone with the
    /// constructor and <c>delete</c>; the template with its widths and <c>updateAppLayout</c>, read
    /// back as a responsive template whose code model has the one generated callback.
    /// </summary>
    [Fact]
    public void ANewAppsTextIsTheClassWriters()
    {
        string blank = AppDesignerDocument.NewAppText();
        (AppDesign design, DesignCode code, _) = Read(blank);
        Assert.Equal("app1", code.ClassName);
        Assert.Empty(code.Callbacks);
        Assert.False(code.SingleInstance);
        Assert.Contains("        function app = app1\n", blank);
        Assert.Contains("            delete(app.UIFigure)\n", blank);
        Assert.Equal(blank, DesignClassWriter.Render(design, code));

        string responsive = DesignResponsive.NewAppText(3);
        (AppDesign template, DesignCode templateCode, _) = Read(responsive);
        Assert.True(template.IsResponsive && DesignResponsive.IsTemplate(template));
        DesignCallback layout = Assert.Single(templateCode.Callbacks);
        Assert.True(layout.IsGenerated);
        Assert.Equal(DesignResponsive.Callback, layout.Name);
        Assert.Contains("        twoPanelWidth = 768;\n", responsive);
        Assert.Equal(responsive, DesignClassWriter.Render(template, templateCode));
    }

    /// <summary>
    /// Where R2025b is installed, every app it ships is read in place and written again: the class
    /// comes out line for line, callbacks' comments, constructor and <c>delete</c> included. An app
    /// whose only differences are lines of <c>createComponents</c> holds an older release's component
    /// order, which DS1's test holds against R2025b's generator; one whose only differences are
    /// callback comments holds an older release's comment form, and is listed.
    /// </summary>
    [Fact]
    public void EveryShippedAppIsWrittenBackLineForLine()
    {
        if (!Directory.Exists(ShippedRoot))
        {
            return;
        }

        var failures = new List<string>();
        var older = new List<string>();
        int apps = 0;
        foreach (string path in Directory.EnumerateFiles(ShippedRoot, "*.mlapp", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(path);
            string text;
            try
            {
                text = JgsMlapp.ReadCode(path);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
                continue;
            }

            DesignReadResult read = DesignCodeReader.Read(text);
            DesignCode? code = DesignCode.Read(text);
            if (read.Design is null || code is null)
            {
                failures.Add($"{name}: not read ({string.Join(" ", read.Problems)})");
                continue;
            }

            apps++;
            string[] expected = Lines(text);
            string[] written = DesignClassWriter.Lines(read.Design, code, read.Style).ToArray();
            List<(int Line, string Expected, string Written)> differences = Differences(expected, written);
            if (differences.Count == 0)
            {
                continue;
            }

            int function = Array.FindIndex(expected, static l => l.Trim() == "function createComponents(app)");
            int functionEnd = function < 0 ? -1 : Array.FindIndex(expected, function, l => l.TrimEnd() == expected[function][..(expected[function].Length - expected[function].TrimStart().Length)] + "end");
            if (differences.All(static d => d.Expected.TrimStart().StartsWith('%') && d.Written.TrimStart().StartsWith('%')))
            {
                older.Add($"{name}: {differences.Count} comment line(s) in an older form:\n    " + string.Join("\n    ", differences.Select(Show)));
                continue;
            }

            if (function >= 0 && differences.All(d => d.Line > function + 1 && d.Line <= functionEnd + 1))
            {
                older.Add($"{name}: {differences.Count} line(s) of createComponents in an older release's order");
                continue;
            }

            failures.Add($"{name}:\n  " + string.Join("\n  ", differences.Take(12).Select(Show)));
        }

        foreach (string line in older)
        {
            output.WriteLine(line);
        }

        output.WriteLine($"{apps} apps, {older.Count} with an older release's forms");
        Assert.True(apps >= 40, $"only {apps} apps read");
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    // --- App Designer's own code, run under node ----------------------------------------------------

    /// <summary>
    /// The lifecycle block - the constructor, as a single instance or not, with or without a startup
    /// function and input parameters, and <c>delete</c> - is what App Designer's
    /// <c>LifecycleMethodsBlockBehavior</c> writes, for both indent sizes and two figure and class names (48 cases).
    /// </summary>
    [Fact]
    public void TheLifecycleBlockIsAppDesigners()
    {
        using JsonDocument oracle = Oracle("CodeLifecycleOracle.json");
        int cases = 0;
        foreach (JsonElement c in oracle.RootElement.GetProperty("lifecycle").EnumerateArray())
        {
            var style = new DesignCodeStyle(new string(' ', c.GetProperty("indentSize").GetInt32()));
            var code = new DesignCode(c.GetProperty("className").GetString()!) { SingleInstance = c.GetProperty("singleton").GetString() == "FOCUS" };
            string startup = c.GetProperty("startup").GetString()!;
            if (startup != "none")
            {
                code.Callbacks.Add(new DesignCallback("startupFcn", isStartup: true));
                code.InputParameters = startup.Contains('(') ? startup[(startup.IndexOf('(') + 1)..^1] : string.Empty;
            }

            string figure = c.GetProperty("figureCodeName").GetString()!;
            var lines = new List<string> { style.Indent(1) + DesignClassWriter.LifecycleComment, style.Indent(1) + "methods (Access = public)", string.Empty };
            lines.AddRange(DesignClassWriter.Constructor(code, figure, style));
            lines.Add(string.Empty);
            lines.AddRange(DesignClassWriter.Delete(figure, style));
            lines.Add(style.Indent(1) + "end");
            string[] expected = c.GetProperty("lines").EnumerateArray().Select(static e => e.GetString()!).ToArray();
            AssertSameLines(expected, [.. lines], $"{c.GetProperty("className")} {c.GetProperty("singleton")} {startup} indent {c.GetProperty("indentSize")}");
            cases++;
        }

        Assert.Equal(48, cases);
    }

    /// <summary>
    /// A callback's comment and signature are what App Designer's <c>CallbackModel</c> and
    /// <c>MethodBuilder</c> make of its bindings: the property's comment or the mixed one, the names
    /// sorted by character code, the budget of 75 columns measured with four-space indents whatever
    /// the file's are, a first name that does not fit on a line of its own, the rest replaced by
    /// "...and N other components", and the lines wrapped again by words at the file's indent; a
    /// callback with no binding reads "Callback function" alone.
    /// </summary>
    [Fact]
    public void ACallbacksCommentIsAppDesigners()
    {
        using JsonDocument oracle = Oracle("CodeCallbacksOracle.json");
        int cases = 0;
        foreach (JsonElement c in oracle.RootElement.GetProperty("comments").EnumerateArray())
        {
            if (!c.TryGetProperty("bindings", out JsonElement bindings) || c.GetProperty("key").GetString() == "UseCustomComment"
                || c.GetProperty("comment").ValueKind != JsonValueKind.Array)
            {
                continue; // a custom comment, or the startup function's (whose comment is a plain string)
            }

            var style = new DesignCodeStyle(new string(' ', c.GetProperty("indentSize").GetInt32()));
            string[] start = c.GetProperty("methodStartLines").EnumerateArray().Select(static e => e.GetString()!).ToArray();
            string name = System.Text.RegularExpressions.Regex.Match(start[^1], @"function (\w+)\(").Groups[1].Value;
            var design = new AppDesign(Make("matlab.ui.Figure", "UIFigure"));
            var code = new DesignCode("app1");
            var callback = new DesignCallback(name);
            code.Callbacks.Add(callback);
            foreach (JsonElement b in bindings.EnumerateArray())
            {
                string codeName = b.GetProperty("codeName").GetString()!;
                string type = b.GetProperty("type").GetString()!;
                DesignComponent component = design.Find(codeName) ?? (type == "matlab.ui.Figure" && codeName == "UIFigure" ? design.Figure : Make(type, codeName));
                if (component.Parent is null && !ReferenceEquals(component, design.Figure))
                {
                    design.Figure.Add(component);
                }

                component.Set(DesignCallbacks.DesignPropertyName(component, b.GetProperty("prop").GetString()!), new CallbackValue(name));
            }

            string[] comment = c.GetProperty("comment").EnumerateArray().Select(static e => DesignCallbacks.Format(e.GetString()!)).ToArray();
            Assert.Equal(comment, DesignCallbacks.R2025b.Comment(design, callback));
            string[] written = DesignClassWriter.Function(design, code, callback, style).Take(start.Length).ToArray();
            AssertSameLines(start, written, c.GetProperty("key").GetString()! + $" indent {style.IndentUnit.Length}");
            cases++;
        }

        Assert.True(cases >= 80, $"{cases} cases");
    }

    /// <summary>A new callback's default name, and a name made unique, are App Designer's (<c>getDefaultCallbackName</c>, <c>UniqueNameGenerator</c>).</summary>
    [Fact]
    public void ANewCallbacksNameIsAppDesigners()
    {
        using JsonDocument oracle = Oracle("CodeCallbacksOracle.json");
        foreach (JsonElement c in oracle.RootElement.GetProperty("defaultCallbackNames").EnumerateArray())
        {
            DesignComponent component = Make(c.GetProperty("type").GetString()!, c.GetProperty("codeName").GetString()!);
            Assert.Equal(c.GetProperty("name").GetString(), DesignCallbacks.DefaultName(component, c.GetProperty("prop").GetString()!));
        }

        foreach (JsonElement c in oracle.RootElement.GetProperty("uniqueNames").EnumerateArray())
        {
            if (c.TryGetProperty("note", out _))
            {
                continue; // a code name's counting, DS3's
            }

            string[] existing = c.GetProperty("existing").EnumerateArray().Select(static e => e.GetString()!).ToArray();
            Assert.Equal(c.GetProperty("name").GetString(), DesignCallbacks.Unique(c.GetProperty("base").GetString()!, existing));
        }
    }

    /// <summary>
    /// Add Property and Add Function put App Designer's templates where its <c>UserEditableBlockBehavior</c>
    /// puts them: into the block of that access before its <c>end</c>, or as a new block - a
    /// properties block at the top or after the other access's, a methods block at the end - with
    /// its whitespace-only lines, for both indent sizes (24 cases).
    /// </summary>
    [Fact]
    public void TheEditableSectionsTemplatesAreAppDesigners()
    {
        using JsonDocument oracle = Oracle("CodeTemplatesOracle.json");
        int cases = 0;
        foreach (JsonElement c in oracle.RootElement.GetProperty("editable").EnumerateArray())
        {
            var style = new DesignCodeStyle(new string(' ', c.GetProperty("indentSize").GetInt32()));
            var design = new AppDesign(Make("matlab.ui.Figure", "UIFigure", 100, 100, 640, 480));
            var code = new DesignCode("app1");
            code.EditableSection.AddRange(c.GetProperty("existing").EnumerateArray().Select(static e => e.GetString()!));
            using var document = DesignDocument.FromText(DesignClassWriter.Render(design, code, style), null);
            Assert.Equal(code.EditableSection, document.Code!.EditableSection);
            foreach (JsonElement step in c.GetProperty("steps").EnumerateArray())
            {
                JsonElement what = step.GetProperty("step");
                string access = what.GetProperty("access").GetString()!;
                string? name = what.GetProperty("kind").GetString() == "property" ? document.InsertUserProperty(access) : document.InsertUserFunction(access);
                Assert.Equal(what.GetProperty("name").GetString(), name);
                string[] rendered = step.GetProperty("rendered").EnumerateArray().Select(static e => e.GetString()!).ToArray();
                Assert.Equal(string.Empty, rendered[^1]);
                AssertSameLines(rendered[..^1], [.. document.Code.EditableSection], $"{c.GetProperty("key")} / {what}");
            }

            cases++;
        }

        Assert.Equal(24, cases);
    }

    // --- the document's edits ------------------------------------------------------------------------

    /// <summary>
    /// Input parameters make the constructor take <c>varargin</c> and the startup function the names,
    /// and make a startup function when the app has none; cleared, they take a startup function whose
    /// code is still blank away; the startup function deleted takes them away. A name that is no
    /// MATLAB name, or named twice, is refused.
    /// </summary>
    [Fact]
    public void InputParametersMakeAndTakeTheStartupFunction()
    {
        using var document = DesignDocument.FromText(SmallAppText(), null);
        Assert.Null(document.SetInputParameters("a,b"));
        Assert.Equal("a, b", document.InputParameters);
        DesignCallback startup = Assert.Single(document.Callbacks);
        Assert.True(startup.IsStartup);
        Assert.Equal("startupFcn", startup.Name);
        Assert.Contains("        function app = app1(varargin)\n", document.CurrentText);
        Assert.Contains("            runStartupFcn(app, @(app)startupFcn(app, varargin{:}))\n", document.CurrentText);
        Assert.Contains("        % Code that executes after component creation\n        function startupFcn(app, a, b)\n            \n        end\n", document.CurrentText);

        Assert.Null(document.SetInputParameters(string.Empty));
        Assert.Empty(document.Callbacks);
        Assert.Contains("        function app = app1\n", document.CurrentText);
        document.Undo();
        Assert.Equal("a, b", document.InputParameters);
        Assert.Single(document.Callbacks);
        document.Undo();
        Assert.Empty(document.Callbacks);
        Assert.False(document.IsDirty);

        Assert.Null(document.AddStartup());
        Assert.Contains("            runStartupFcn(app, @startupFcn)\n", document.CurrentText);
        Assert.Null(document.SetInputParameters("x"));
        Assert.Contains("function startupFcn(app, x)", document.CurrentText);
        Assert.Null(document.DeleteCallback(document.Callbacks[0]));
        Assert.Equal(string.Empty, document.InputParameters);
        Assert.DoesNotContain("runStartupFcn", document.CurrentText);

        Assert.NotNull(document.SetInputParameters("a, 1b"));
        Assert.NotNull(document.SetInputParameters("a, a"));
        Assert.NotNull(document.SetInputParameters("if"));
        Assert.Null(document.SetInputParameters(" a ,, b "));
        Assert.Equal("a, b", document.InputParameters);
    }

    /// <summary>
    /// A callback added for a component's property is named as App Designer names it, bound, and
    /// written with its comment, signature and blank body; bound to another property too, its
    /// comment is the mixed one; renamed, the bindings and the user's calls follow; unbound from
    /// everything it is "not associated with a component"; deleted, the properties are unbound; and
    /// every step undoes.
    /// </summary>
    [Fact]
    public void ACallbackIsAddedBoundRenamedAndDeleted()
    {
        using var document = DesignDocument.FromText(SmallAppText(), null);
        DesignComponent button = document.Design.Find("Button")!;
        DesignComponent field = document.Design.Find("EditField")!;
        Assert.Equal(["ButtonPushedFcn"], document.CallbackPropertiesOf(button));
        Assert.Equal("ButtonPushed", document.DefaultCallbackName(button, "ButtonPushedFcn"));

        Assert.Null(document.AddCallback(button, "ButtonPushedFcn"));
        DesignCallback callback = Assert.Single(document.Callbacks);
        Assert.Equal("ButtonPushed", callback.Name);
        Assert.Equal("ButtonPushed", document.CallbackOf(button, "ButtonPushedFcn"));
        Assert.Equal(["            "], callback.Body);
        Assert.Contains("        % Button pushed function: Button\n        function ButtonPushed(app, event)\n            \n        end\n", document.CurrentText);
        Assert.Contains("app.Button.ButtonPushedFcn = createCallbackFcn(app, @ButtonPushed, true);", document.CurrentText);
        Assert.Equal([button], document.Selection);

        // A second callback for the same property takes the next number; the first is kept, unbound.
        Assert.Null(document.AddCallback(button, "ButtonPushedFcn"));
        Assert.Equal("ButtonPushed2", document.CallbackOf(button, "ButtonPushedFcn"));
        Assert.Contains("        % Callback function: not associated with a component\n        function ButtonPushed(app, event)\n", document.CurrentText);
        document.Undo();
        Assert.Equal("ButtonPushed", document.CallbackOf(button, "ButtonPushedFcn"));
        Assert.Single(document.Callbacks);

        // Bound to a property of another kind too: the mixed comment, the names sorted.
        Assert.Null(document.BindCallback(field, "ValueChangedFcn", "ButtonPushed"));
        Assert.Contains("        % Callback function: Button, EditField\n", document.CurrentText);
        Assert.Null(document.BindCallback(field, "ValueChangedFcn", null));
        Assert.Contains("        % Button pushed function: Button\n", document.CurrentText);
        Assert.NotNull(document.BindCallback(field, "ValueChangedFcn", "NoSuchCallback"));

        // The user calls it; a rename follows into that call and the binding.
        string edited = document.CurrentText.Replace("            \n        end", "            ButtonPushed(app, []); % also app.ButtonPushed\n        end", StringComparison.Ordinal);
        Assert.True(document.ApplyEditedText(edited));
        Assert.True(document.IsDirty);
        Assert.NotNull(document.RenameCallback(callback, "Button"));
        Assert.NotNull(document.RenameCallback(callback, "for"));
        Assert.Null(document.RenameCallback(callback, "Go"));
        Assert.Equal("Go", callback.Name);
        Assert.Equal("Go", document.CallbackOf(button, "ButtonPushedFcn"));
        Assert.Contains("            Go(app, []); % also app.Go\n", document.CurrentText);
        Assert.Contains("createCallbackFcn(app, @Go, true)", document.CurrentText);
        Assert.Equal([(callback.Body.Count > 0 ? document.LineOf(callback) + 1 : 0, "            Go(app, []); % also app.Go")], document.CallbackReferences("Go"));
        document.Undo();
        Assert.Equal("ButtonPushed", callback.Name);
        Assert.Contains("            ButtonPushed(app, []); % also app.ButtonPushed\n", document.CurrentText);

        // Deleted: the property is unbound and the function gone; undone, both are back.
        Assert.Null(document.DeleteCallback(callback));
        Assert.Empty(document.Callbacks);
        Assert.Null(document.CallbackOf(button, "ButtonPushedFcn"));
        Assert.DoesNotContain("% Callbacks that handle component events", document.CurrentText);
        document.Undo();
        Assert.Single(document.Callbacks);
        Assert.Equal("ButtonPushed", document.CallbackOf(button, "ButtonPushedFcn"));
    }

    /// <summary>
    /// A component renamed takes its default-named callback with it (<c>CodeController.renameComponent</c>);
    /// deleted, it takes a callback bound to it alone whose code is still blank, and leaves one with
    /// code of its own, no longer associated with a component.
    /// </summary>
    [Fact]
    public void AComponentsCallbackFollowsItsRenameAndItsDeletion()
    {
        using var document = DesignDocument.FromText(SmallAppText(), null);
        DesignComponent button = document.Design.Find("Button")!;
        DesignComponent field = document.Design.Find("EditField")!;
        Assert.Null(document.AddCallback(button, "ButtonPushedFcn"));
        Assert.Null(document.AddCallback(field, "ValueChangedFcn"));
        Assert.Equal(["ButtonPushed", "EditFieldValueChanged"], document.Callbacks.Select(static c => c.Name));
        Assert.Equal(["            value = app.EditField.Value;", "            "], document.Callbacks[1].Body);

        Assert.Null(document.Rename(button, "StartButton"));
        Assert.Equal("StartButtonPushed", document.Callbacks[0].Name);
        Assert.Equal("StartButtonPushed", document.CallbackOf(button, "ButtonPushedFcn"));
        Assert.Contains("% Button pushed function: StartButton\n        function StartButtonPushed(app, event)", document.CurrentText);
        document.Undo();
        Assert.Equal("ButtonPushed", document.Callbacks[0].Name);
        Assert.Equal("Button", button.CodeName);

        // The field's callback keeps its code: deleting the field leaves it, unassociated.
        string edited = document.CurrentText.Replace("            value = app.EditField.Value;\n", "            value = app.EditField.Value;\n            disp(value)\n", StringComparison.Ordinal);
        Assert.True(document.ApplyEditedText(edited));
        document.Delete([field]);
        Assert.Equal(["ButtonPushed", "EditFieldValueChanged"], document.Callbacks.Select(static c => c.Name));
        Assert.Contains("        % Callback function: not associated with a component\n        function EditFieldValueChanged(app, event)", document.CurrentText);

        // The button's callback is still blank: deleting the button takes it along.
        document.Delete([button]);
        Assert.Equal(["EditFieldValueChanged"], document.Callbacks.Select(static c => c.Name));
        document.Undo();
        Assert.Equal(["ButtonPushed", "EditFieldValueChanged"], document.Callbacks.Select(static c => c.Name));
        Assert.Equal("ButtonPushed", document.CallbackOf(button, "ButtonPushedFcn"));
    }

    /// <summary>
    /// The user's code comes back from the code view: a changed body or editable section is taken
    /// (the app dirty, no edit to undo), text whose functions no longer match the model's is not.
    /// The lines that name a component are listed for a rename's preview.
    /// </summary>
    [Fact]
    public void TheCodeViewsEditsAreTakenBack()
    {
        using var document = DesignDocument.FromText(SmallAppText(), null);
        DesignComponent button = document.Design.Find("Button")!;
        Assert.Null(document.AddCallback(button, "ButtonPushedFcn"));
        Assert.Equal("Property", document.InsertUserProperty("private"));
        Assert.False(document.CanUndo == false);

        string edited = document.CurrentText
            .Replace("        Property % Description\n", "        Property % Description\n        Count = 0 % app.Button is counted\n", StringComparison.Ordinal)
            .Replace("            \n        end", "            app.Count = app.Count + 1;\n            app.Button.Text = string(app.Count);\n        end", StringComparison.Ordinal);
        Assert.True(document.ApplyEditedText(edited));
        Assert.Equal(["Property", "Count"], document.UserProperties());
        Assert.Equal(["            app.Count = app.Count + 1;", "            app.Button.Text = string(app.Count);"], document.Callbacks[0].Body);
        Assert.Equal(edited, document.CurrentText);
        Assert.True(document.IsDirty);

        Assert.Equal(2, document.UserCodeReferences("Button").Count);
        Assert.Contains(document.UserCodeReferences("Button"), static r => r.Text.Contains("app.Button.Text", StringComparison.Ordinal));
        Assert.Null(document.Rename(button, "Counter"));
        Assert.Contains("            app.Counter.Text = string(app.Count);\n", document.CurrentText);
        Assert.Contains("% app.Counter is counted", document.CurrentText);

        Assert.False(document.ApplyEditedText(document.CurrentText.Replace("function CounterPushed(app, event)", "function Other(app, event)", StringComparison.Ordinal)));
        Assert.True(document.ApplyEditedText(document.CurrentText));
    }

    /// <summary>
    /// The single-instance setting writes the constructor that looks for a running instance, and a
    /// save writes <c>SingletonMode</c> into App Designer's copy of the code where its serializer
    /// puts it - and takes it out again when the setting is cleared.
    /// </summary>
    [Fact]
    public void UserFunctionsAndPropertiesAreListedDeletedAndRenamed()
    {
        using var document = DesignDocument.FromText(SmallAppText(), null);
        Assert.Equal("Property", document.InsertUserProperty("private"));
        Assert.Equal("func", document.InsertUserFunction("private"));
        Assert.Equal("func2", document.InsertUserFunction("public"));
        Assert.Equal(["Property"], document.UserProperties());
        Assert.Equal([("func", "app"), ("func2", "app")], document.UserFunctions().Select(static f => (f.Name, f.Parameters)));

        string edited = document.CurrentText
            .Replace("        function results = func(app)\n            \n", "        function results = func(app)\n            results = app.Property + func2(app); % app.Property again\n", StringComparison.Ordinal);
        Assert.True(document.ApplyEditedText(edited));

        Assert.NotNull(document.RenameUserProperty("Property", "Button"));
        Assert.Null(document.RenameUserProperty("Property", "Count"));
        Assert.Equal(["Count"], document.UserProperties());
        Assert.Contains("        Count % Description\n", document.CurrentText);
        Assert.Contains("results = app.Count + func2(app); % app.Count again", document.CurrentText);

        Assert.Null(document.RenameUserFunction("func2", "helper"));
        Assert.Contains("function results = helper(app)", document.CurrentText);
        Assert.Contains("results = app.Count + helper(app);", document.CurrentText);
        document.Undo();
        Assert.Contains("results = app.Count + func2(app);", document.CurrentText);

        Assert.Null(document.DeleteUserFunction("func2"));
        Assert.Equal([("func", "app")], document.UserFunctions().Select(static f => (f.Name, f.Parameters)));
        Assert.Contains("    methods (Access = public)\n        \n    end\n", document.CurrentText); // the blank line before the function stays, as App Designer leaves it
        Assert.Null(document.DeleteUserProperty("Count"));
        Assert.Empty(document.UserProperties());
        Assert.Contains("    properties (Access = private)\n    end\n", document.CurrentText);
        document.Undo();
        Assert.Equal(["Count"], document.UserProperties());
    }

    [Fact]
    public void SingleInstanceWritesTheSingletonConstructorAndTheCodeCopy()
    {
        string directory = Path.Combine(Path.GetTempPath(), "jgraph-ds5-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string app = Path.Combine(directory, "U7bApp.mlapp");
            File.Copy(Helper("U7bApp.mlapp"), app);
            using (var document = DesignDocument.Open(app))
            {
                Assert.False(document.SingleInstance);
                Assert.DoesNotContain("SingletonMode", Copy(app).Fields);
                document.SetSingleInstance(true);
                Assert.True(document.SingleInstance);
                Assert.Contains("            runningApp = getRunningApp(app);\n", document.CurrentText);
                Assert.Contains("                figure(runningApp.UIFigure)\n", document.CurrentText);
                document.Save();
            }

            MlappCodeCopy copy = Copy(app);
            Assert.Contains("SingletonMode", copy.Fields);
            Assert.True(copy.Fields.ToList().IndexOf("SingletonMode") > copy.Fields.ToList().IndexOf("Callbacks"));
            Assert.True(JgsMlappLayout.Of(JgsMlapp.ReadCode(app)).SingleInstance);

            using (var document = DesignDocument.Open(app))
            {
                Assert.True(document.SingleInstance);
                document.SetSingleInstance(false);
                document.Save();
            }

            Assert.DoesNotContain("SingletonMode", Copy(app).Fields);
            Assert.False(JgsMlappLayout.Of(JgsMlapp.ReadCode(app)).SingleInstance);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // --- the review's findings (ADR 0250, "Measured") ----------------------------------------------------

    /// <summary>
    /// A body the user is typing a block into is taken as it is: an <c>if</c> with no <c>end</c> yet
    /// leaves the function whole, and so does a stray <c>end</c> inside it, because the functions are
    /// told apart by the lines App Designer generates, not by the balance of the body.
    /// </summary>
    [Fact]
    public void AnOpenBlockTypedIntoABodyIsStillTheBody()
    {
        using var document = DesignDocument.FromText(SmallAppText(), null);
        DesignComponent button = document.Design.Find("Button")!;
        Assert.Null(document.AddCallback(button, "ButtonPushedFcn"));
        string typing = document.CurrentText.Replace("function ButtonPushed(app, event)\n            \n", "function ButtonPushed(app, event)\n            if app.Button.Enable\n", StringComparison.Ordinal);
        Assert.NotEqual(document.CurrentText, typing);
        Assert.True(document.ApplyEditedText(typing));
        Assert.Equal(["            if app.Button.Enable"], document.Callbacks[0].Body);

        string stray = document.CurrentText.Replace("            if app.Button.Enable\n", "            if app.Button.Enable\n        end\n            disp(1)\n", StringComparison.Ordinal);
        Assert.True(document.ApplyEditedText(stray));
        Assert.Equal(["            if app.Button.Enable", "        end", "            disp(1)"], document.Callbacks[0].Body);
    }

    /// <summary>
    /// A callback read from a file learns its first binding's default code, as App Designer's loader
    /// gives it: deleting the field whose callback still reads <c>value = app.EditField.Value;</c>
    /// takes the callback along, while the button's, with code of its own, stays and reads "not
    /// associated with a component" though it was never bound in this session.
    /// </summary>
    [Fact]
    public void ALoadedCallbackKnowsItsDefaultCodeAndLosesItsComponent()
    {
        using var made = DesignDocument.FromText(SmallAppText(), null);
        Assert.Null(made.AddCallback(made.Design.Find("Button")!, "ButtonPushedFcn"));
        Assert.Null(made.AddCallback(made.Design.Find("EditField")!, "ValueChangedFcn"));
        Assert.True(made.ApplyEditedText(made.CurrentText.Replace("function ButtonPushed(app, event)\n            \n", "function ButtonPushed(app, event)\n            disp(1)\n", StringComparison.Ordinal)));

        using var document = DesignDocument.FromText(made.CurrentText, null);
        Assert.Equal(["value = app.EditField.Value;", ""], document.Callbacks[1].DefaultBody);
        Assert.True(document.Callbacks[1].IsDefaultOrEmpty);
        Assert.False(document.Callbacks[0].IsDefaultOrEmpty);
        Assert.Contains("        % Button pushed function: Button\n", document.CurrentText);

        document.Delete([document.Design.Find("EditField")!]);
        Assert.Equal(["ButtonPushed"], document.Callbacks.Select(static c => c.Name));
        document.Delete([document.Design.Find("Button")!]);
        Assert.Equal(["ButtonPushed"], document.Callbacks.Select(static c => c.Name));
        Assert.Contains("        % Callback function: not associated with a component\n        function ButtonPushed(app, event)", document.CurrentText);
    }

    /// <summary>Two orphaned callbacks taken away by one deletion come back in their order when it is undone.</summary>
    [Fact]
    public void OrphanedCallbacksComeBackInOrderWhenADeletionIsUndone()
    {
        using var document = DesignDocument.FromText(SmallAppText(), null);
        DesignComponent button = document.Design.Find("Button")!;
        DesignComponent field = document.Design.Find("EditField")!;
        Assert.Null(document.AddCallback(button, "ButtonPushedFcn"));
        Assert.Null(document.AddCallback(field, "ValueChangedFcn"));
        Assert.Null(document.AddCallback(field, "ValueChangingFcn", "Typing"));
        Assert.True(document.ApplyEditedText(document.CurrentText.Replace("function Typing(app, event)\n", "function Typing(app, event)\n            disp(1)\n", StringComparison.Ordinal)));
        Assert.False(document.Callbacks[2].IsDefaultOrEmpty);
        Assert.Null(document.BindCallback(button, "ButtonPushedFcn", "Typing"));
        Assert.Null(document.BindCallback(button, "ButtonPushedFcn", "ButtonPushed"));

        document.Delete([button, field]);
        Assert.Equal(["Typing"], document.Callbacks.Select(static c => c.Name));
        document.Undo();
        Assert.Equal(["ButtonPushed", "EditFieldValueChanged", "Typing"], document.Callbacks.Select(static c => c.Name));
        Assert.Equal("ButtonPushed", document.CallbackOf(button, "ButtonPushedFcn"));
    }

    /// <summary>
    /// A callback follows its component's rename only while it still has the name App Designer
    /// gave it, and only while that component alone is bound to it.
    /// </summary>
    [Fact]
    public void ACallbackTheUserRenamedOrSharedNoLongerFollowsItsComponent()
    {
        using var document = DesignDocument.FromText(SmallAppText(), null);
        DesignComponent button = document.Design.Find("Button")!;
        DesignComponent field = document.Design.Find("EditField")!;
        Assert.Null(document.AddCallback(button, "ButtonPushedFcn"));
        Assert.Null(document.RenameCallback(document.Callbacks[0], "ButtonHandler"));
        Assert.Null(document.Rename(button, "Start"));
        Assert.Equal("ButtonHandler", document.Callbacks[0].Name);
        document.Undo();
        document.Undo();
        Assert.Equal("ButtonPushed", document.Callbacks[0].Name);
        Assert.Null(document.Rename(button, "Start"));
        Assert.Equal("StartPushed", document.Callbacks[0].Name);
        Assert.Equal("StartPushed", document.Callbacks[0].GeneratedName);

        Assert.Null(document.AddCallback(field, "ValueChangedFcn"));
        Assert.Null(document.BindCallback(field, "ValueChangingFcn", "EditFieldValueChanged"));
        Assert.Null(document.Rename(field, "Name"));
        Assert.Equal("EditFieldValueChanged", document.Callbacks[1].Name);
    }

    /// <summary>
    /// A property's rename takes <c>app.Name</c> and its declaration line, and nothing else: a longer
    /// word, a string, and a bare use are left as App Designer leaves them.
    /// </summary>
    [Fact]
    public void APropertyRenameLeavesOtherWordsAlone()
    {
        using var document = DesignDocument.FromText(SmallAppText(), null);
        Assert.Null(document.AddCallback(document.Design.Find("Button")!, "ButtonPushedFcn"));
        Assert.Equal("Property", document.InsertUserProperty("private"));
        Assert.Null(document.RenameUserProperty("Property", "Data"));
        string edited = document.CurrentText
            .Replace("        Data % Description\n", "        Data % Description\n        Other = 'Data' % app.Data\n", StringComparison.Ordinal)
            .Replace("            \n        end", "            metaData = Data;\n            app.Data = 1;\n            msg = 'Data';\n        end", StringComparison.Ordinal);
        Assert.True(document.ApplyEditedText(edited));
        Assert.Equal([(12, "        Data % Description", "        Total % Description"), (13, "        Other = 'Data' % app.Data", "        Other = 'Data' % app.Total"), (23, "            app.Data = 1;", "            app.Total = 1;")],
            document.RenamePreview("Data", "Total", property: true).Select(static c => (c.Line, c.Before, c.After)));
        Assert.Null(document.RenameUserProperty("Data", "Total"));
        Assert.Contains("        Total % Description\n        Other = 'Data' % app.Total\n", document.CurrentText);
        Assert.Equal(["            metaData = Data;", "            app.Total = 1;", "            msg = 'Data';"], document.Callbacks[0].Body);
    }

    /// <summary>
    /// Undoing an Add Property, an Add Function or a rename after typing in the code view takes back
    /// only what the edit did, where it still stands; the typed code stays, and redo puts the edit's
    /// lines back. An edit's lines the user typed into are left as they are.
    /// </summary>
    [Fact]
    public void UndoOfACodeEditKeepsTheCodeTypedSince()
    {
        using var document = DesignDocument.FromText(SmallAppText(), null);
        Assert.Null(document.AddCallback(document.Design.Find("Button")!, "ButtonPushedFcn"));
        Assert.Equal("Property", document.InsertUserProperty("private"));
        Assert.Equal("Property2", document.InsertUserProperty("private"));
        Assert.Equal("func", document.InsertUserFunction("private"));
        Assert.True(document.ApplyEditedText(document.CurrentText
            .Replace("        Property2 % Description\n", "        Property2 % Description\n        Count = 0\n", StringComparison.Ordinal)
            .Replace("function ButtonPushed(app, event)\n            \n", "function ButtonPushed(app, event)\n            app.Count = func(app);\n", StringComparison.Ordinal)));
        Assert.Equal(["Property", "Property2", "Count"], document.UserProperties());

        Assert.Null(document.RenameUserFunction("func", "compute"));
        Assert.Equal(["            app.Count = compute(app);"], document.Callbacks[0].Body);
        Assert.True(document.ApplyEditedText(document.CurrentText.Replace("            app.Count = compute(app);\n", "            app.Count = compute(app);\n            disp(app.Count)\n", StringComparison.Ordinal)));
        document.Undo(); // the rename
        Assert.Equal(["            app.Count = func(app);", "            disp(app.Count)"], document.Callbacks[0].Body);
        document.Undo(); // the function
        Assert.Empty(document.UserFunctions());
        Assert.Equal(["Property", "Property2", "Count"], document.UserProperties());
        document.Undo(); // Property2
        Assert.Equal(["Property", "Count"], document.UserProperties());
        Assert.Equal(["            app.Count = func(app);", "            disp(app.Count)"], document.Callbacks[0].Body);
        document.Redo();
        Assert.Equal(["Property", "Property2", "Count"], document.UserProperties());

        // The first property's block was typed into (Count stands inside it): its undo leaves it as it is.
        document.Undo();
        document.Undo();
        Assert.Equal(["Property", "Count"], document.UserProperties());
        Assert.Contains("        Property % Description\n        Count = 0\n", document.CurrentText, StringComparison.Ordinal);
    }

    /// <summary>Deleting a bound callback, like every other edit of the code, leaves the selection where it was.</summary>
    [Fact]
    public void DeletingABoundCallbackKeepsTheSelection()
    {
        using var document = DesignDocument.FromText(SmallAppText(), null);
        DesignComponent button = document.Design.Find("Button")!;
        DesignComponent field = document.Design.Find("EditField")!;
        Assert.Null(document.AddCallback(button, "ButtonPushedFcn"));
        document.Select([field]);
        Assert.Null(document.DeleteCallback(document.Callbacks[0]));
        Assert.Equal([field], document.Selection);
        document.Undo();
        Assert.Equal([field], document.Selection);
    }

    /// <summary>The names every app has from <c>matlab.apps.AppBase</c> are refused with App Designer's words, for a callback and for a component alike.</summary>
    [Fact]
    public void AppDesignersOwnNamesAreRefused()
    {
        using var document = DesignDocument.FromText(SmallAppText(), null);
        DesignComponent button = document.Design.Find("Button")!;
        Assert.Equal("Name cannot be an App Designer keyword.", document.AddCallback(button, "ButtonPushedFcn", "registerApp"));
        Assert.Equal("Name cannot be an App Designer keyword.", document.WhyNotFunctionName("delete"));
        Assert.Equal("Name cannot be an App Designer keyword.", document.Rename(button, "TimingFields"));
        Assert.Null(document.AddCallback(button, "ButtonPushedFcn", "registerApp2"));
    }

    /// <summary>
    /// A responsive template's layout callback is App Designer's own: lost from the class, it is put
    /// back last; its comment changed, it is App Designer's again, written with App Designer's comment.
    /// </summary>
    [Fact]
    public void AResponsiveTemplatesLayoutCallbackIsAppDesignersAgain()
    {
        string full = DesignResponsive.NewAppText(2);
        int start = full.IndexOf("        " + DesignResponsive.CallbackComment, StringComparison.Ordinal);
        int stop = full.IndexOf("\n        end\n", start, StringComparison.Ordinal) + "\n        end\n".Length;
        string layoutFn = full[start..stop];

        using var lost = DesignDocument.FromText(full.Replace(layoutFn, string.Empty, StringComparison.Ordinal), null);
        Assert.True(lost.Callbacks[^1] is { Name: "updateAppLayout", IsGenerated: true });
        Assert.Contains("createCallbackFcn(app, @updateAppLayout, true)", lost.CurrentText, StringComparison.Ordinal);
        Assert.Contains(layoutFn, lost.CurrentText, StringComparison.Ordinal);

        using var commented = DesignDocument.FromText(full.Replace(DesignResponsive.CallbackComment, "% Reflow the regions", StringComparison.Ordinal), null);
        Assert.True(commented.Callbacks.Single(static c => c.Name == "updateAppLayout").IsGenerated);
        Assert.Contains(layoutFn, commented.CurrentText, StringComparison.Ordinal);
        Assert.DoesNotContain("Reflow", commented.CurrentText, StringComparison.Ordinal);
        Assert.Null(commented.AddCallback(commented.Design.Find("LeftPanel")!, "ButtonDownFcn"));
        Assert.Equal(["LeftPanelButtonDown", "updateAppLayout"], commented.Callbacks.Select(static c => c.Name));
    }

    /// <summary>
    /// The component properties block read from a file keeps its order through a move as App Designer's
    /// does: the moved component's declaration goes after the component before it in the browser's
    /// order, and the others stay where the file had them.
    /// </summary>
    [Fact]
    public void ThePropertiesBlockFollowsAMoveAsAppDesignersDoes()
    {
        DesignComponent figure = Make("matlab.ui.Figure", "UIFigure", 100, 100, 640, 480);
        var design = new AppDesign(figure);
        figure.Add(Make("matlab.ui.container.Panel", "Panel", 20, 20, 300, 300));
        figure.Add(Make("matlab.ui.control.Button", "Button", 400, 400, 100, 23));
        figure.Add(Make("matlab.ui.control.Label", "Label", 400, 300, 50, 22));
        string text = DesignClassWriter.Render(design, new DesignCode("app1"));
        string[] browserOrder = ["        UIFigure  matlab.ui.Figure", "        Label     matlab.ui.control.Label", "        Button    matlab.ui.control.Button", "        Panel     matlab.ui.container.Panel"];
        string[] olderOrder = ["        UIFigure  matlab.ui.Figure", "        Panel     matlab.ui.container.Panel", "        Button    matlab.ui.control.Button", "        Label     matlab.ui.control.Label"];
        Assert.Contains(string.Join('\n', browserOrder), text, StringComparison.Ordinal);
        text = text.Replace(string.Join('\n', browserOrder), string.Join('\n', olderOrder), StringComparison.Ordinal);

        using var document = DesignDocument.FromText(text, null);
        Assert.Contains(string.Join('\n', olderOrder), document.CurrentText, StringComparison.Ordinal);
        DesignComponent button = document.Design.Find("Button")!;
        DesignComponent panel = document.Design.Find("Panel")!;
        Rect2D inner = document.Runtime.InnerOnSurface(panel);
        document.BeginGesture([button]);
        document.Preview(new Dictionary<DesignComponent, Rect2D>(ReferenceEqualityComparer.Instance) { [button] = new Rect2D(inner.X + 10, inner.Y + 10, 100, 23) });
        document.EndGesture(commit: true, panel);
        Assert.Same(panel, button.Parent);
        Assert.Contains("        UIFigure  matlab.ui.Figure\n        Panel     matlab.ui.container.Panel\n        Button    matlab.ui.control.Button\n        Label     matlab.ui.control.Label", document.CurrentText, StringComparison.Ordinal);

        document.Undo();
        Assert.Contains("        UIFigure  matlab.ui.Figure\n        Panel     matlab.ui.container.Panel\n        Label     matlab.ui.control.Label\n        Button    matlab.ui.control.Button", document.CurrentText, StringComparison.Ordinal);
    }

    /// <summary>
    /// A callback bound to one kind of property on two classes takes the comment of its first
    /// binding: on load the first App Designer's reader finds (the newest component first), in a
    /// session the first made.
    /// </summary>
    [Fact]
    public void ASharedCallbacksCommentIsItsFirstBindings()
    {
        DesignComponent figure = Make("matlab.ui.Figure", "UIFigure", 100, 100, 640, 480);
        var design = new AppDesign(figure);
        figure.Add(Make("matlab.ui.container.TabGroup", "TabGroup", 20, 20, 200, 200));
        figure.Add(Make("matlab.ui.container.ButtonGroup", "ButtonGroup", 300, 20, 200, 200));
        using var document = DesignDocument.FromText(DesignClassWriter.Render(design, new DesignCode("app1")), null);
        DesignComponent tabs = document.Design.Find("TabGroup")!;
        DesignComponent group = document.Design.Find("ButtonGroup")!;
        Assert.Null(document.AddCallback(tabs, "SelectionChangedFcn", "Chosen"));
        Assert.Null(document.BindCallback(group, "SelectionChangedFcn", "Chosen"));
        Assert.Contains("        % Selection change function: ButtonGroup, TabGroup\n", document.CurrentText, StringComparison.Ordinal);

        using var loaded = DesignDocument.FromText(document.CurrentText, null);
        Assert.Contains("        % Selection changed function: ButtonGroup, TabGroup\n", loaded.CurrentText, StringComparison.Ordinal);
    }

    /// <summary>A tab-indented app wraps a callback's comment at App Designer's width for an indent size of four, not at its tabs' two characters.</summary>
    [Fact]
    public void ATabIndentedAppWrapsItsCommentsAtAppDesignersWidth()
    {
        DesignComponent figure = Make("matlab.ui.Figure", "UIFigure", 100, 100, 640, 480);
        var design = new AppDesign(figure);
        figure.Add(Make("matlab.ui.control.Button", "AAAAAAAAAAAAAAAAAAAAAAAA", 20, 420, 100, 23));
        figure.Add(Make("matlab.ui.control.EditField", "BBBBBBBBBBBBBBBBBBBBBB", 20, 380, 100, 22));
        figure.Children[0].Set("ButtonPushedFcn", new CallbackValue("Shared"));
        figure.Children[1].Set("ValueChangedFcn", new CallbackValue("Shared"));
        var code = new DesignCode("app1");
        var callback = new DesignCallback("Shared");
        code.Callbacks.Add(callback);
        Assert.Equal(["% Callback function: AAAAAAAAAAAAAAAAAAAAAAAA, BBBBBBBBBBBBBBBBBBBBBB"], DesignCallbacks.R2025b.Comment(design, callback));

        IReadOnlyList<string> tabs = DesignClassWriter.Function(design, code, callback, new DesignCodeStyle("\t"));
        Assert.Equal(["\t\t% Callback function: AAAAAAAAAAAAAAAAAAAAAAAA,", "\t\t% BBBBBBBBBBBBBBBBBBBBBB", "\t\tfunction Shared(app, event)"], tabs.Take(3));
        IReadOnlyList<string> spaces = DesignClassWriter.Function(design, code, callback, new DesignCodeStyle("  "));
        Assert.Equal("    % Callback function: AAAAAAAAAAAAAAAAAAAAAAAA, BBBBBBBBBBBBBBBBBBBBBB", spaces[0]);
    }

    private static MlappCodeCopy Copy(string path)
    {
        using ZipArchive zip = ZipFile.OpenRead(path);
        using Stream stream = zip.GetEntry("appdesigner/appModel.mat")!.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return JgsMlappModel.Read(memory.ToArray()) ?? throw new InvalidOperationException("no code copy");
    }

    // --- helpers -------------------------------------------------------------------------------------

    /// <summary>The lines that differ (one-based), the first 40 of them.</summary>
    private static List<(int Line, string Expected, string Written)> Differences(string[] expected, string[] written)
    {
        var differences = new List<(int, string, string)>();
        int n = Math.Max(expected.Length, written.Length);
        for (int i = 0; i < n; i++)
        {
            string e = i < expected.Length ? expected[i] : "<none>";
            string w = i < written.Length ? written[i] : "<none>";
            if (e != w)
            {
                differences.Add((i + 1, e, w));
                if (differences.Count > 40)
                {
                    break;
                }
            }
        }

        return differences;
    }

    private static string Show((int Line, string Expected, string Written) d) => $"line {d.Line}: |{d.Expected}| vs |{d.Written}|";

    private static void AssertSameLines(string[] expected, string[] written, string what)
    {
        List<(int, string, string)> differences = Differences(expected, written);
        Assert.True(differences.Count == 0, $"{what}:\n" + string.Join("\n", differences.Select(Show)));
    }
}
