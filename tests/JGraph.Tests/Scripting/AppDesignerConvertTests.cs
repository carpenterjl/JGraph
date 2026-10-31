using JGraph.Core.Primitives;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Workspace;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The App Designer window plan's stage W8c: Convert both ways as a new app that leaves the old one alone, Save Copy As, Export to .m File.
/// </summary>
[Collection("JG facade")]
public sealed class AppDesignerConvertTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jgw8c_" + Guid.NewGuid().ToString("N"));

    public AppDesignerConvertTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A folder under the temp folder; the next clean takes it.
        }
    }

    private static DesignPaletteEntry Entry(string name) => DesignPalette.R2025b.Find(name) ?? throw new InvalidOperationException(name);

    private static DesignDocument App(string name = "app1")
    {
        var figure = new DesignComponent("UIFigure", DesignCatalog.R2025b.ByType("matlab.ui.Figure")!);
        figure.Set("Position", NumericValue.Row(100, 100, 640, 480));
        figure.Set("Name", new CharValue("My Title"));
        return DesignDocument.FromText(DesignCodeWriter.Splice(AppDesignerDocument.NewAppText(name), new AppDesign(figure))!, null);
    }

    private static DesignComponent One(DesignDocument document, string entry, double x, double y) =>
        document.Add(Entry(entry), document.Design.Figure, new Point2D(x, y), withoutLabel: true)[^1];

    private static void PutAt(DesignComponent component, double x, double y, double w, double h) => component.Set("Position", NumericValue.Row(x, y, w, h));

    [Fact]
    public void ComponentsGoToTheRegionTheirMiddleIsIn()
    {
        DesignDocument document = App();
        DesignComponent left = One(document, "Button", 50, 100);
        DesignComponent right = One(document, "Slider", 400, 100);
        PutAt(left, 50, 100, 100, 22);
        PutAt(right, 400, 100, 150, 3);

        DesignConverted made = document.ConvertToPanels(2, "app1_converted");
        DesignDocument converted = made.Document;
        Assert.True(converted.IsDirty);
        Assert.Null(converted.Path);
        Assert.Equal("app1_converted", converted.AppName);
        Assert.True(converted.Design.IsResponsive);
        Assert.Null(made.Detached);

        DesignComponent leftPanel = converted.Design.Find("LeftPanel")!;
        DesignComponent rightPanel = converted.Design.Find("RightPanel")!;
        DesignComponent movedLeft = converted.Design.Find(left.CodeName)!;
        DesignComponent movedRight = converted.Design.Find(right.CodeName)!;
        Assert.Same(leftPanel, movedLeft.Parent);
        Assert.Same(rightPanel, movedRight.Parent);

        // Its place less the region's left edge: 1 for the left, 221 for the right.
        Assert.Equal(49, ((NumericValue)movedLeft.Get("Position")!).Data[0]);
        Assert.Equal(179, ((NumericValue)movedRight.Get("Position")!).Data[0]);

        // The old app is as it was.
        Assert.Same(document.Design.Figure, left.Parent);
        Assert.False(document.Design.IsResponsive);
    }

    [Fact]
    public void TheFiguresPropertiesComeOverButThoseTheTemplateSets()
    {
        DesignDocument document = App();
        document.Design.Figure.Set("SizeChangedFcn", new CallbackValue("own"));
        DesignDocument converted = document.ConvertToPanels(3, "x").Document;
        Assert.Equal("My Title", ((CharValue)converted.Design.Figure.Get("Name")!).Text);
        Assert.Equal(640, ((NumericValue)converted.Design.Figure.Get("Position")!).Data[2]);
        Assert.Equal(DesignResponsive.Callback, ((CallbackValue)converted.Design.Figure.Get("SizeChangedFcn")!).Method);
    }

    [Fact]
    public void TheColumnsFollowTheFiguresWidth()
    {
        DesignDocument document = App();
        document.Design.Figure.Set("Position", NumericValue.Row(100, 100, 1000, 480));
        DesignDocument converted = document.ConvertToPanels(2, "x").Document;
        IReadOnlyList<DesignValue> columns = DesignResponsive.Tracks(DesignResponsive.Grid(converted.Design)!.Get("ColumnWidth"));
        Assert.Equal(344, ((NumericValue)columns[0]).Data[0]); // round(1000 * 221 / 640 - 1)
        Assert.Equal("1x", ((CharValue)columns[1]).Text);
    }

    [Fact]
    public void TheFiguresOwnSizeChangedCallbackIsDisconnectedAndKept()
    {
        DesignDocument document = App();
        Assert.Null(document.AddCallback(document.Design.Figure, "SizeChangedFcn", "whenResized"));

        DesignConverted made = document.ConvertToPanels(2, "x");
        Assert.Equal("whenResized", made.Detached);
        Assert.NotNull(made.Document.Code!.Find("whenResized"));
        Assert.Equal(DesignResponsive.Callback, made.Document.CallbackOf(made.Document.Design.Figure, "SizeChangedFcn"));
    }

    [Fact]
    public void TheCodeOptionsAndTheEditableCodeComeOver()
    {
        DesignDocument document = App();
        Assert.Null(document.SetInputParameters("a, b"));
        document.SetSingleInstance(true);
        document.InsertUserProperty("private");
        document.SetHelpText("Sums", "Body");
        document.SetAppDetails(document.AppDetailsValues with { Author = "Me", Name = "Old" }, null, null);

        DesignDocument converted = document.ConvertToPanels(2, "app1_converted").Document;
        Assert.Equal("a, b", converted.InputParameters);
        Assert.True(converted.SingleInstance);
        Assert.Contains("function startupFcn(app, a, b)", converted.CurrentText);
        Assert.Contains("Property", string.Join("\n", converted.Code!.EditableSection));
        Assert.Equal("Me", converted.AppDetailsValues.Author);
        Assert.Equal("app1_converted", converted.AppDetailsValues.Name);
        Assert.Contains("%APP1_CONVERTED Sums", converted.CurrentText);
    }

    [Fact]
    public void AConvertedAppSavesAndOpensAgain()
    {
        DesignDocument document = App();
        One(document, "Button", 50, 100);
        DesignDocument converted = document.ConvertToPanels(3, "SavedConverted").Document;
        string path = Path.Combine(_folder, "SavedConverted.mlapp");
        converted.SaveAs(path);
        DesignDocument reopened = DesignDocument.Open(path);
        Assert.True(reopened.Design.IsResponsive);
        Assert.Equal(3, DesignResponsive.Panels(reopened.Design));
        Assert.NotNull(reopened.Design.Find("Button"));
        Assert.Contains("function updateAppLayout(app, event)", reopened.CurrentText);
    }

    [Fact]
    public void BackToStandardTheRegionsArePanels()
    {
        DesignDocument document = DesignDocument.FromText(DesignResponsive.NewAppText(2, "panels"), null);
        document.Add(Entry("Button"), document.Design.Find("LeftPanel")!, new Point2D(10, 10), withoutLabel: true);

        DesignDocument standard = document.ConvertToStandard("panels_converted").Document;
        Assert.False(standard.Design.IsResponsive);
        Assert.Equal(0, DesignResponsive.Panels(standard.Design));
        DesignComponent left = standard.Design.Find("LeftPanel")!;
        Assert.Same(standard.Design.Figure, left.Parent);
        Assert.Null(left.Get("Layout.Row"));
        Assert.Equal([1d, 1, 220, 480], ((NumericValue)left.Get("Position")!).Data);
        Assert.Equal([221d, 1, 420, 480], ((NumericValue)standard.Design.Find("RightPanel")!.Get("Position")!).Data);
        Assert.Single(left.Children);
        Assert.Null(standard.Code!.Find(DesignResponsive.Callback));
        Assert.DoesNotContain("updateAppLayout", standard.CurrentText);
    }

    [Fact]
    public void APlainTextAppConvertsToAPlainTextApp()
    {
        string path = Path.Combine(_folder, "PlainOne.m");
        DesignDocument document = App("PlainOne");
        document.SaveAs(path);
        DesignDocument converted = DesignDocument.Open(path).ConvertToPanels(2, "PlainOne_converted").Document;
        Assert.Equal(DesignFileFormat.PlainText, converted.Format);
        Assert.Contains("classdef PlainOne_converted < matlab.apps.App", converted.CurrentText);
    }

    [Fact]
    public void ASavedCopyLeavesTheAppAsItIs()
    {
        string path = Path.Combine(_folder, "Original.mlapp");
        JgsMlapp.WriteApp(path, AppDesignerDocument.NewAppText("Original"));
        DesignDocument document = DesignDocument.Open(path);
        document.SetHelpText("Summary here", string.Empty);
        Assert.True(document.IsDirty);

        string copy = Path.Combine(_folder, "TheCopy.mlapp");
        document.SaveCopy(copy);

        Assert.Equal(path, document.Path);
        Assert.True(document.IsDirty);
        Assert.Equal("Original", document.AppName);
        Assert.DoesNotContain("TheCopy", document.CurrentText);
        DesignDocument opened = DesignDocument.Open(copy);
        Assert.Equal("TheCopy", opened.AppName);
        Assert.Equal(("Summary here", string.Empty), opened.HelpText);
        Assert.DoesNotContain("Summary here", JgsMlapp.ReadCode(path));
    }

    [Fact]
    public void ASavedCopyTakesTheFormatItsNameSays()
    {
        DesignDocument document = App("Either");
        string plain = Path.Combine(_folder, "EitherCopy.m");
        document.SaveCopy(plain);
        Assert.Contains("classdef EitherCopy < matlab.apps.App", File.ReadAllText(plain));
        Assert.Null(document.Path);

        string binary = Path.Combine(_folder, "EitherPackage.mlapp");
        document.SaveCopy(binary);
        Assert.Equal("EitherPackage", DesignDocument.Open(binary).AppName);
        Assert.Equal(DesignFileFormat.Mlapp, document.Format);
    }

    [Fact]
    public void AnExportIsTheClassAloneUnderItsName()
    {
        DesignDocument document = App("Exporting");
        string target = Path.Combine(_folder, "Exporting_exported.m");
        document.Export(target);
        string text = File.ReadAllText(target);
        Assert.StartsWith("classdef Exporting_exported < matlab.apps.AppBase", text);
        Assert.Contains("function app = Exporting_exported", text);
        Assert.DoesNotContain("%[app:", text);
        Assert.Equal("Exporting", document.AppName);
    }

    [Fact]
    public void APlainTextAppsExportDerivesFromAppBase()
    {
        string path = Path.Combine(_folder, "PlainExp.m");
        App("PlainExp").SaveAs(path);
        string target = Path.Combine(_folder, "PlainExp_exported.m");
        DesignDocument.Open(path).Export(target);
        string text = File.ReadAllText(target);
        Assert.StartsWith("classdef PlainExp_exported < matlab.apps.AppBase", text);
        Assert.DoesNotContain("%[app:", text);
    }
}
