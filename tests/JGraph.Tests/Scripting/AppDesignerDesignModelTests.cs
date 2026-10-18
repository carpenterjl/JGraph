using JGraph.Scripting.Designer;
using JGraph.Scripting.Jgs;
using Xunit;
using Xunit.Abstractions;

namespace JGraph.Tests.Scripting;

/// <summary>
/// DS1 of the designer plan: the designer's model of an app's components, read from the generated
/// <c>createComponents</c> and written back as R2025b's App Designer writes it.
/// </summary>
public class AppDesignerDesignModelTests(ITestOutputHelper output)
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Scripting", "Designer", name);

    // --- this project's fixture app ------------------------------------------------------------------

    /// <summary>
    /// The fixture app makes every uifigure component class App Designer offers, its properties set
    /// out of order, some to their defaults, some through the forms App Designer reads back (a
    /// one-element cell of text, a line break, a doubled quote, a date, an image beside the app,
    /// a grid child's span). Each component is written as R2025b's generator wrote it from the
    /// same lines run in R2025b (probe <c>ds1_generate</c>).
    /// </summary>
    [Fact]
    public void TheFixtureAppsComponentsAreWrittenAsR2025bsGeneratorWritesThem()
    {
        DesignReadResult read = DesignCodeReader.Read(File.ReadAllText(Fixture("DesignerSpecApp.m")));
        Assert.True(read.IsComplete, string.Join("\n", read.Problems));
        AppDesign design = read.Design!;

        var expected = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (string line in File.ReadLines(Fixture("DesignerSpecApp.generated.txt")))
        {
            string[] parts = line.Split('\t', 2);
            (expected.TryGetValue(parts[0], out List<string>? lines) ? lines : expected[parts[0]] = []).Add(parts[1]);
        }

        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), design.Components.Select(static c => c.CodeName).Order(StringComparer.Ordinal));
        Assert.Equal(DesignCatalog.R2025b.Classes.Select(static c => c.Type).Order(StringComparer.Ordinal),
            design.Components.Select(static c => c.Type).Distinct().Order(StringComparer.Ordinal));
        Assert.All(design.Components, c => Assert.Equal(expected[c.CodeName], Generated(c)));
    }

    /// <summary>
    /// Every value the fixture app's generated lines hold is read back as a value and written as it
    /// was: none is kept as bare text.
    /// </summary>
    [Fact]
    public void EveryValueTheGeneratorWroteForTheFixtureReadsBackAsAValue()
    {
        AppDesign design = DesignCodeReader.Read(File.ReadAllText(Fixture("DesignerSpecApp.m"))).Design!;
        Assert.All(design.Components.SelectMany(static c => c.Properties), p => Assert.IsNotType<ExpressionValue>(p.Value));
    }
    // --- values -------------------------------------------------------------------------------------

    /// <summary>Each form App Designer writes reads back as its value and is written as it was.</summary>
    [Theory]
    [InlineData("5")]
    [InlineData("-0.5")]
    [InlineData("1e-05")]
    [InlineData("[0 Inf]")]
    [InlineData("[-Inf NaN]")]
    [InlineData("[1 2;3 4]")]
    [InlineData("[0.902 0.902 0.902]")]
    [InlineData("true")]
    [InlineData("[true false]")]
    [InlineData("[]")]
    [InlineData("zeros(1,0)")]
    [InlineData("''")]
    [InlineData("'It''s'")]
    [InlineData("['Two' newline 'lines']")]
    [InlineData("\"text\"")]
    [InlineData("\"a\" + newline + \"b\"")]
    [InlineData("{}")]
    [InlineData("{'A'}")]
    [InlineData("{'A', 'B', 'C'}")]
    [InlineData("{'A'; 'B'}")]
    [InlineData("{'fit', '1x', 40}")]
    [InlineData("{100, '1x'}")]
    [InlineData("{1}")]
    [InlineData("{['a' newline 'b'], 'c'}")]
    [InlineData("'on'")]
    [InlineData("datetime([2026 10 18])")]
    [InlineData("NaT")]
    [InlineData("[datetime([2026 12 25]) NaT]")]
    [InlineData("[datetime([2026 12 25]); datetime([2027 1 1])]")]
    [InlineData("createCallbackFcn(app, @ButtonPushed, true)")]
    [InlineData("[app.Node1, app.Node2]")]
    [InlineData("app.ContextMenu")]
    [InlineData("fullfile(pathToMLAPP, 'img', 'logo.png')")]
    public void AWrittenValueReadsBackAndIsWrittenAsItWas(string text)
    {
        DesignValue value = DesignValueText.Parse(text);
        Assert.IsNotType<ExpressionValue>(value);
        Assert.Equal(text, DesignValueText.Format(value));
    }

    /// <summary>
    /// Text in no form App Designer writes is kept whole, and written back as it was: the design
    /// holds it, though it cannot compare it with a default.
    /// </summary>
    [Theory]
    [InlineData("myColors(3)")]
    [InlineData("[1 2] * 2")]
    [InlineData("'unterminated")]
    [InlineData("{'a' 'b'}")]
    public void TextInNoWrittenFormIsKeptAsItWas(string text)
    {
        DesignValue value = DesignValueText.Parse(text);
        Assert.Equal(text, Assert.IsType<ExpressionValue>(value).Text);
        Assert.Equal(text, DesignValueText.Format(value));
    }

    /// <summary>A matrix written with spaces after its separators reads, and is written as mat2str writes it.</summary>
    [Theory]
    [InlineData("[1 0 0; 0 1 0]", "[1 0 0;0 1 0]")]
    [InlineData("[1, 2, 3]", "[1 2 3]")]
    [InlineData("[ 1 2 ]", "[1 2]")]
    public void AMatrixAsAPersonWritesItIsWrittenAsMat2StrWritesIt(string text, string written)
    {
        Assert.Equal(written, DesignValueText.Format(DesignValueText.Parse(text)));
    }

    /// <summary>
    /// A string with a line break is written so that it reads back with the break: R2025b's
    /// generator writes <c>"a" + "b"</c> and loses it (correctness before parity).
    /// </summary>
    [Fact]
    public void AStringsLineBreakIsWrittenSoThatItReadsBack()
    {
        Assert.Equal("\"a\" + newline + \"b\"", DesignValueText.Format(new StringValue("a\nb")));
    }

    /// <summary>
    /// A cell R2025b's generator cannot write - a matrix of cells, a string or an array inside - is
    /// written as a cell literal that reads back, where R2025b leaves the line empty or drops it.
    /// </summary>
    [Fact]
    public void ACellTheGeneratorCannotWriteIsWrittenSoThatItReadsBack()
    {
        var matrix = new CellValue(2, 2, [new CharValue("a"), new CharValue("c"), new CharValue("b"), NumericValue.Scalar(4)]);
        Assert.Equal("{'a', 'b'; 'c', 4}", DesignValueText.Format(matrix));
        Assert.Equal("{\"x\"}", DesignValueText.Format(CellValue.Row(new StringValue("x"))));
        Assert.Equal("{[1 2], 'b'}", DesignValueText.Format(CellValue.Row(NumericValue.Row(1, 2), new CharValue("b"))));
    }

    /// <summary>Values compare as MATLAB's isequal compares them.</summary>
    [Fact]
    public void ValuesCompareAsIsequalComparesThem()
    {
        Assert.True(DesignValueText.IsEqual(new CharValue("on"), OnOffValue.On));
        Assert.True(DesignValueText.IsEqual(NumericValue.Logical(true), NumericValue.Scalar(1)));
        Assert.True(DesignValueText.IsEqual(new CharValue("a"), new StringValue("a")));
        Assert.False(DesignValueText.IsEqual(NumericValue.Row(1, 2), new NumericValue(2, 1, [1, 2])));
        Assert.False(DesignValueText.IsEqual(NumericValue.Scalar(double.NaN), NumericValue.Scalar(double.NaN)));
        Assert.True(DesignValueText.IsEqual(NumericValue.Scalar(double.NaN), NumericValue.Scalar(double.NaN), nanEqual: true));
        Assert.True(DesignValueText.IsEqual(CellValue.Row(new CharValue("x")), CellValue.Row(new StringValue("x"))));
        Assert.False(DesignValueText.IsEqual(new ExpressionValue("x"), new ExpressionValue("x")));
    }

    // --- reading -------------------------------------------------------------------------------------

    /// <summary>A line that is not code App Designer generates is listed with its line number, and the rest is read.</summary>
    [Fact]
    public void ALineTheReaderCannotPlaceIsListed()
    {
        string text = string.Join('\n',
            "classdef A < matlab.apps.AppBase",
            "    methods (Access = private)",
            "        function createComponents(app)",
            "            app.UIFigure = uifigure('Visible', 'off');",
            "            app.Button = uibutton(app.UIFigure, 'push');",
            "            disp('hello')",
            "            app.Missing.Text = 'x';",
            "            app.Button.Text = 'Go';",
            "            app.Button.Text = 'Again';",
            "            app.UIFigure.Visible = 'on';",
            "        end",
            "    end",
            "end");
        DesignReadResult read = DesignCodeReader.Read(text);
        Assert.False(read.IsComplete);
        Assert.Collection(read.Problems,
            p => Assert.StartsWith("line 6: disp('hello')", p, StringComparison.Ordinal),
            p => Assert.StartsWith("line 7: app.Missing.Text", p, StringComparison.Ordinal),
            p => Assert.Contains("set twice", p, StringComparison.Ordinal));
        Assert.Equal("Go", Assert.IsType<CharValue>(read.Design!.Find("Button")!.Get("Text")).Text);
    }

    /// <summary>Code without createComponents gives no design.</summary>
    [Fact]
    public void CodeWithoutCreateComponentsGivesNoDesign()
    {
        DesignReadResult read = DesignCodeReader.Read("classdef A\nend");
        Assert.Null(read.Design);
        Assert.Single(read.Problems);
    }

    // --- writing a design ----------------------------------------------------------------------------

    /// <summary>
    /// A design made here, not read: the generated code App Designer would write for it, with the
    /// figure first, each component under its comment, and the properties block lined up.
    /// </summary>
    [Fact]
    public void ADesignMadeHereIsWrittenAsAppDesignerWritesOne()
    {
        DesignCatalog catalog = DesignCatalog.R2025b;
        var figure = new DesignComponent("UIFigure", catalog.ByType("matlab.ui.Figure")!);
        figure.Set("Position", NumericValue.Row(100, 100, 640, 480));
        figure.Set("Name", new CharValue("MATLAB App"));
        var design = new AppDesign(figure);
        var grid = new DesignComponent("GridLayout", catalog.ByType("matlab.ui.container.GridLayout")!);
        figure.Add(grid);
        var button = new DesignComponent("GoButton", catalog.ByType("matlab.ui.control.Button")!);
        grid.Add(button);
        button.Set("Text", new CharValue("Go"));
        button.Set("Layout.Column", NumericValue.Scalar(2));
        button.Set("Layout.Row", NumericValue.Scalar(1));
        button.Set("Position", NumericValue.Row(1, 1, 100, 22)); // a grid child's position is not written
        button.Set("ButtonPushedFcn", new CallbackValue("GoButtonPushed"));
        button.Set("FontSize", NumericValue.Scalar(12)); // the default

        Assert.Equal(
            [
                "    % Properties that correspond to app components",
                "    properties (Access = public)",
                "        UIFigure    matlab.ui.Figure",
                "        GridLayout  matlab.ui.container.GridLayout",
                "        GoButton    matlab.ui.control.Button",
                "    end",
            ],
            DesignCodeWriter.PropertiesBlock(design));
        Assert.Equal(
            [
                "        % Create UIFigure and components",
                "        function createComponents(app)",
                "",
                "            % Create UIFigure and hide until all components are created",
                "            app.UIFigure = uifigure('Visible', 'off');",
                "            app.UIFigure.Position = [100 100 640 480];",
                "            app.UIFigure.Name = 'MATLAB App';",
                "",
                "            % Create GridLayout",
                "            app.GridLayout = uigridlayout(app.UIFigure);",
                "",
                "            % Create GoButton",
                "            app.GoButton = uibutton(app.GridLayout, 'push');",
                "            app.GoButton.ButtonPushedFcn = createCallbackFcn(app, @GoButtonPushed, true);",
                "            app.GoButton.Layout.Row = 1;",
                "            app.GoButton.Layout.Column = 2;",
                "            app.GoButton.Text = 'Go';",
                "",
                "            % Show the figure after all components are created",
                "            app.UIFigure.Visible = 'on';",
                "        end",
            ],
            DesignCodeWriter.Method(design));
    }

    /// <summary>Renaming, unsetting and moving a component change what is written, and nothing else.</summary>
    [Fact]
    public void AnEditedDesignIsWrittenWithItsEdits()
    {
        AppDesign design = DesignCodeReader.Read(File.ReadAllText(Fixture("DesignerSpecApp.m"))).Design!;
        DesignComponent button = design.Find("Button")!;
        button.CodeName = "RunButton";
        Assert.True(button.Unset("Tooltip"));
        DesignComponent panel = button.Parent!;
        panel.Remove(button);
        design.Find("ControlsTab")!.Add(button);

        IReadOnlyList<string> code = DesignCodeWriter.ComponentCode(button);
        Assert.Equal("app.RunButton = uibutton(app.ControlsTab, 'push');", code[0]);
        Assert.DoesNotContain(code, static l => l.Contains("Tooltip", StringComparison.Ordinal));
        IReadOnlyList<string> method = DesignCodeWriter.Method(design);
        Assert.Contains("            % Create RunButton", method);
        Assert.DoesNotContain(method, static l => l.Contains("app.Button.", StringComparison.Ordinal));
    }

    /// <summary>A colour is at its default by the app's theme: the dark theme's default button colour is not the light one's.</summary>
    [Fact]
    public void ADefaultIsTheThemes()
    {
        DesignComponentClass cls = DesignCatalog.R2025b.ByType("matlab.ui.control.Button")!;
        DesignProperty background = cls.Property("BackgroundColor")!;
        var button = new DesignComponent("B", cls);
        DesignValue light = background.DefaultFor(DesignTheme.Light)!;
        DesignValue dark = background.DefaultFor(DesignTheme.Dark)!;
        Assert.False(DesignValueText.IsEqual(light, dark));
        Assert.True(DesignCodeWriter.IsDefault(button, background, light, DesignTheme.Light));
        Assert.False(DesignCodeWriter.IsDefault(button, background, light, DesignTheme.Dark));
        Assert.True(DesignCodeWriter.IsDefault(button, background, dark, DesignTheme.Dark));
    }

    /// <summary>The catalog knows every uifigure component class App Designer offers in R2025b, and finds a class by its creation call.</summary>
    [Fact]
    public void TheCatalogKnowsEveryComponentClass()
    {
        DesignCatalog catalog = DesignCatalog.R2025b;
        Assert.Equal("2025b", catalog.Release);
        Assert.Equal(45, catalog.Classes.Count);
        Assert.Same(catalog.ByType("matlab.ui.control.StateButton"), catalog.ByCreation("uibutton", "state"));
        Assert.Same(catalog.ByType("matlab.ui.container.CheckBoxTree"), catalog.ByCreation("uitree", "checkbox"));
        Assert.Same(catalog.ByType("matlab.ui.container.Tree"), catalog.ByCreation("uitree", null));
        Assert.Equal("Text", catalog.ByType("matlab.ui.control.Button")!.ValueProperty);
        Assert.True(catalog.ByType("matlab.ui.control.Button")!.Property("BackgroundColor")!.HasMode);
    }

    // --- every app R2025b ships ----------------------------------------------------------------------

    private const string ShippedRoot = @"C:\Program Files\MATLAB\R2025b\toolbox";

    /// <summary>
    /// Where R2025b is installed, each <c>.mlapp</c> it ships is read in place, its components taken
    /// into a design, and the design written back: the component properties block and
    /// <c>createComponents</c> come out line for line as the app has them. Where the probe
    /// <c>ds1_oracle</c> has left R2025b's own generator's lines (its ignored <c>rt/</c> folder), an
    /// app whose <c>createComponents</c> differs passes only when every one of its components is
    /// written as R2025b's generator writes it: the text is then an older release's. Nothing of
    /// MathWorks' is kept or printed but where a line differs.
    /// </summary>
    [Fact]
    public void EveryShippedAppsComponentCodeIsWrittenBackAsItWas()
    {
        if (!Directory.Exists(ShippedRoot))
        {
            return;
        }

        Dictionary<(string App, string Component), List<string>>? oracle = Oracle();
        var failures = new List<string>();
        var older = new List<string>();
        int apps = 0;
        int components = 0;
        foreach ((string key, string path) in ShippedApps())
        {
            apps++;
            string[] lines = JgsMlapp.ReadCode(path).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            DesignReadResult read = DesignCodeReader.Read(string.Join('\n', lines));
            if (read.Design is not { } design)
            {
                failures.Add($"{key}: no design ({string.Join("; ", read.Problems)})");
                continue;
            }

            components += design.Components.Count();
            foreach (string problem in read.Problems)
            {
                failures.Add($"{key}: {problem}");
            }

            Compare(key, "properties", Block(lines), DesignCodeWriter.PropertiesBlock(design, read.Style), failures);
            var methodDifferences = new List<string>();
            Compare(key, "createComponents", MethodOf(lines), DesignCodeWriter.Method(design, read.Style), methodDifferences);
            if (methodDifferences.Count == 0)
            {
                continue;
            }

            if (oracle is not null && design.Components.All(c => WrittenAsGenerated(oracle, key, c)))
            {
                older.Add($"{key}: {methodDifferences.Count} lines as an older release wrote them");
            }
            else
            {
                failures.AddRange(methodDifferences);
            }
        }

        foreach (string line in older.Concat(failures))
        {
            output.WriteLine(line);
        }

        output.WriteLine($"{apps} apps, {components} components, {older.Count} apps with an older release's text, {failures.Count} differences");
        Assert.True(apps >= 40, $"{apps} apps");
        Assert.Empty(failures);
    }

    /// <summary>
    /// Where the probe <c>ds1_oracle</c> has left R2025b's generator's lines for every component of
    /// every shipped app, the designer writes each component as that generator does - wherever the
    /// app's code says what its saved component tree says. The generator reads the tree; where the
    /// tree holds a value the code never sets (an older release's default kept in the tree, which
    /// R2025b's light theme no longer takes for a default), the code is what runs, and the designer
    /// reads the code: those components are counted, not compared.
    /// </summary>
    [Fact]
    public void EveryShippedComponentIsWrittenAsR2025bsGeneratorWritesIt()
    {
        if (!Directory.Exists(ShippedRoot) || Oracle() is not { } oracle)
        {
            return;
        }

        var failures = new List<string>();
        int compared = 0;
        int treeDiffers = 0;
        foreach ((string key, string path) in ShippedApps())
        {
            string text = JgsMlapp.ReadCode(path);
            AppDesign design = DesignCodeReader.Read(text).Design!;
            string[] body = MethodOf(text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')).Select(static l => l.Trim()).ToArray();
            foreach (DesignComponent component in design.Components)
            {
                // The same lines in any order: the tree and the code agree on what is set, and the
                // order is the generator's to say.
                if (!oracle.TryGetValue((key, component.CodeName), out List<string>? generated)
                    || !generated.Order(StringComparer.Ordinal).SequenceEqual(CodeLines(body, design, component).Order(StringComparer.Ordinal)))
                {
                    treeDiffers++;
                    continue;
                }

                compared++;
                if (!WrittenAsGenerated(oracle, key, component))
                {
                    List<string> expected = oracle.GetValueOrDefault((key, component.CodeName)) ?? [];
                    List<string> actual = Generated(component);
                    string first = expected.Zip(actual).FirstOrDefault(p => p.First != p.Second) is ({ } e, { } a)
                        ? $"generator [{e}] / written [{a}]"
                        : $"{expected.Count} lines generated, {actual.Count} written";
                    failures.Add($"{key} {component.CodeName}: {first}");
                }
            }
        }

        foreach (string failure in failures)
        {
            output.WriteLine(failure);
        }

        output.WriteLine($"{compared} components compared, {failures.Count} differ; {treeDiffers} whose saved tree says more than their code");
        Assert.True(compared >= 1000, $"{compared} components");
        Assert.Empty(failures);
    }

    /// <summary>The lines of <c>createComponents</c> that make and set <paramref name="component"/>, in the order the code has them.</summary>
    private static IEnumerable<string> CodeLines(string[] body, AppDesign design, DesignComponent component)
    {
        string self = "app." + component.CodeName;
        foreach (string line in body)
        {
            bool mine = line.StartsWith(self + " = ", StringComparison.Ordinal) || line.StartsWith(self + ".", StringComparison.Ordinal)
                || (line.Contains('(' + self + ", ", StringComparison.Ordinal) && !line.StartsWith("app.", StringComparison.Ordinal));
            bool assignment = line.StartsWith(self + ".ContextMenu = ", StringComparison.Ordinal);
            bool shown = ReferenceEquals(component, design.Figure) && line == self + ".Visible = 'on';";
            if (mine && !assignment && !shown)
            {
                yield return line;
            }
        }
    }

    private static IEnumerable<(string Key, string Path)> ShippedApps() =>
        Directory.EnumerateFiles(ShippedRoot, "*.mlapp", SearchOption.AllDirectories)
            .Select(static p => (Path.GetRelativePath(ShippedRoot, p).Replace('\\', '/'), p))
            .OrderBy(static e => e.Item1, StringComparer.Ordinal);

    /// <summary>A component's lines as the generator's list holds them.</summary>
    private static List<string> Generated(DesignComponent component) => DesignCodeWriter.ComponentCode(component).ToList();

    private static bool WrittenAsGenerated(Dictionary<(string, string), List<string>> oracle, string app, DesignComponent component) =>
        oracle.TryGetValue((app, component.CodeName), out List<string>? expected) && expected.SequenceEqual(Generated(component));

    /// <summary>The probe <c>ds1_oracle</c>'s lines by app and component, or null when the probe has not been run here.</summary>
    private static Dictionary<(string App, string Component), List<string>>? Oracle()
    {
        string? folder = AppContext.BaseDirectory;
        while (folder is not null && !Directory.Exists(Path.Combine(folder, "tools", "matlab-checklist")))
        {
            folder = Path.GetDirectoryName(folder);
        }

        string? file = folder is null ? null : Path.Combine(folder, "tools", "matlab-checklist", "ui-probes", "ds1", "rt", "ds1_oracle.txt");
        if (file is null || !File.Exists(file))
        {
            return null;
        }

        var oracle = new Dictionary<(string, string), List<string>>();
        foreach (string line in File.ReadLines(file))
        {
            string[] parts = line.Split('\t', 3);
            if (parts.Length == 3)
            {
                (oracle.TryGetValue((parts[0], parts[1]), out List<string>? lines) ? lines : oracle[(parts[0], parts[1])] = []).Add(parts[2]);
            }
        }

        return oracle;
    }

    private static void Compare(string app, string part, IReadOnlyList<string> expected, IReadOnlyList<string> actual, List<string> failures)
    {
        int n = Math.Max(expected.Count, actual.Count);
        int reported = 0;
        for (int i = 0, j = 0; i < expected.Count || j < actual.Count;)
        {
            string? e = i < expected.Count ? expected[i] : null;
            string? a = j < actual.Count ? actual[j] : null;
            if (e == a)
            {
                i++;
                j++;
                continue;
            }

            failures.Add($"{app} {part} line {i}: app has [{e}] / written [{a}]");
            if (++reported >= 3)
            {
                failures.Add($"{app} {part}: ... ({expected.Count} lines in the app, {actual.Count} written)");
                return;
            }

            // resynchronise on the next line both share
            int k = actual.Skip(j).ToList().IndexOf(e ?? "\0");
            int l = expected.Skip(i).ToList().IndexOf(a ?? "\0");
            if (k > 0 && (l < 0 || k <= l))
            {
                j += k;
            }
            else if (l > 0)
            {
                i += l;
            }
            else
            {
                i++;
                j++;
            }
        }

        _ = n;
    }

    /// <summary>The component properties block as the app has it.</summary>
    private static List<string> Block(string[] lines)
    {
        int start = Array.FindIndex(lines, static l => l.Trim() == "% Properties that correspond to app components");
        int end = Array.FindIndex(lines, start, static l => l.Trim() == "end");
        return lines[start..(end + 1)].ToList();
    }

    /// <summary><c>createComponents</c> as the app has it, from the comment above it to its end.</summary>
    private static List<string> MethodOf(string[] lines)
    {
        int start = Array.FindIndex(lines, static l => l.Trim() == "function createComponents(app)");
        string indent = lines[start][..lines[start].IndexOf('f', StringComparison.Ordinal)];
        int end = Array.FindIndex(lines, start, l => l == indent + "end");
        return lines[(start - 1)..(end + 1)].ToList();
    }
}
