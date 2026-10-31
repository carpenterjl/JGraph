using System.IO.Compression;
using System.Text;
using JGraph.Imaging;
using JGraph.Imaging.Codecs;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Workspace;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The App Designer window plan's stage W8a: App Details and the app's help text in an app - one step, kept in both file formats, a thumbnail each way, what follows the class's
/// name - against what R2025b's <c>FileWriter</c> and <c>AppThumbnailUtils</c> were measured to write (ui-probes/w8/w8_details).
/// </summary>
[Collection("JG facade")]
public sealed class AppDesignerDetailsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jgw8a_" + Guid.NewGuid().ToString("N"));

    public AppDesignerDetailsTests() => Directory.CreateDirectory(_folder);

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

    private const string PngOfTwo = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAFklEQVR4nGP4z8DAwMDAxMDAwMDAAAANHQEDasKb6QAAAABJRU5ErkJggg==";

    private string Saved(string name = "DetailsApp")
    {
        string path = Path.Combine(_folder, name + ".mlapp");
        JgsMlapp.WriteApp(path, AppDesignerDocument.NewAppText(name));
        return path;
    }

    private string Picture(int width, int height, string name = "picture.png")
    {
        string path = Path.Combine(_folder, name);
        using var image = new ImageBuffer(height, width, 3);
        ImageCodec.Write(path, image);
        return path;
    }

    private static string Part(string mlapp, string name)
    {
        using ZipArchive package = ZipFile.OpenRead(mlapp);
        using var reader = new StreamReader(package.GetEntry(name)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string[] Parts(string mlapp)
    {
        using ZipArchive package = ZipFile.OpenRead(mlapp);
        return package.Entries.Select(static e => e.FullName).ToArray();
    }

    [Theory]
    [InlineData("1", "1.0")]
    [InlineData("1.2", "1.2")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.3.4", "1.2.3.4")]
    [InlineData("abc", "1.0")]
    [InlineData("", "1.0")]
    [InlineData(" 3.0 ", "1.0")]
    [InlineData("3.0 x", "3.0 x")]
    [InlineData(".5", "1.0")]
    [InlineData("5.", "1.0")]
    public void APackageKeepsAVersionAsFileWriterKeepsIt(string version, string kept) => Assert.Equal(kept, DesignAppDetails.VersionForPackage(version));

    [Theory]
    [InlineData(100, 100, 100, 100)]
    [InlineData(100, 200, 83, 165)] // portrait, as measured
    [InlineData(200, 100, 200, 100)]
    [InlineData(1000, 300, 220, 66)]
    [InlineData(300, 1000, 50, 165)]
    [InlineData(220, 165, 220, 165)]
    [InlineData(400, 120, 220, 66)]
    public void AThumbnailFitsAsR2025bFitsIt(int width, int height, int fittedWidth, int fittedHeight) =>
        Assert.Equal((fittedWidth, fittedHeight), DesignAppDetails.FittedSize(width, height));

    [Theory]
    [InlineData(1000, 1000, 165, 165)] // R2025b leaves a square any size
    [InlineData(221, 166, 220, 165)] // and this one 166 high
    public void AThumbnailReallyFitsWhereR2025bsDoesNot(int width, int height, int fittedWidth, int fittedHeight) =>
        Assert.Equal((fittedWidth, fittedHeight), DesignAppDetails.FittedSize(width, height));

    [Theory]
    [InlineData("One line", "", "One line", "")]
    [InlineData("", "Only a description", "", "Only a description")]
    [InlineData("A long summary that is long enough to wrap over more than one line of the file as the class builder wraps it", "", "A long summary that is long enough to wrap over more than one line of the file as the class builder wraps it", "")]
    [InlineData("Sum", "First line\n\nThird line", "Sum", "First line\nThird line")]
    // wrapComment takes the blanks after the sign as its prefix, so the first line's indent stands before them all (the oracle's lines).
    [InlineData("Sum", "  Indented line\nA description long enough that it goes over more than one line when the class builder wraps its text there", "Sum", "  Indented line\n  A description long enough that it goes over more than one line\n  when the class builder wraps its text there")]
    public void HelpTextReadsBackAsHelpCommentsParserReadsIt(string summary, string description, string summaryOut, string descriptionOut)
    {
        IReadOnlyList<string> lines = DesignHelpText.Lines("W8Probe", summary, description, "    ");
        Assert.Equal((summaryOut, descriptionOut), DesignHelpText.Parse(lines, "W8Probe"));
    }

    [Fact]
    public void HelpTextNotHeadedByTheClassIsNoHelpText()
    {
        Assert.Equal((string.Empty, string.Empty), DesignHelpText.Parse(["    % Just a comment", "    %   more"], "app1"));
        Assert.Empty(DesignHelpText.Lines("app1", string.Empty, string.Empty, "    "));
    }

    [Fact]
    public void HelpTextGoesUnderTheClassLineAsOneStep()
    {
        DesignDocument document = DesignDocument.FromText(AppDesignerDocument.NewAppText("app1"), null);
        document.SetHelpText("Adds numbers", "It adds them.\nAnd shows the sum.");

        string[] lines = document.CurrentText.Split('\n');
        Assert.Equal("classdef app1 < matlab.apps.AppBase", lines[0]);
        Assert.Equal("    %APP1 Adds numbers", lines[1]);
        Assert.Equal("    %   It adds them.", lines[2]);
        Assert.Equal("    %   And shows the sum.", lines[3]);
        Assert.Equal(("Adds numbers", "It adds them.\nAnd shows the sum."), document.HelpText);

        document.Undo();
        Assert.Equal((string.Empty, string.Empty), document.HelpText);
        Assert.DoesNotContain("%APP1", document.CurrentText);
        document.Redo();
        Assert.Contains("%APP1 Adds numbers", document.CurrentText);
    }

    [Fact]
    public void TheSameHelpTextIsNoStep()
    {
        DesignDocument document = DesignDocument.FromText(AppDesignerDocument.NewAppText("app1"), null);
        document.SetHelpText("Same", string.Empty);
        document.SetHelpText("Same", string.Empty);
        document.Undo();
        Assert.Equal((string.Empty, string.Empty), document.HelpText);
    }

    [Fact]
    public void HelpTextFollowsTheClassThroughSaveAs()
    {
        string path = Saved("OldName");
        DesignDocument document = DesignDocument.Open(path);
        document.SetHelpText("Sums", "Body");
        document.Save();

        string target = Path.Combine(_folder, "NewName.mlapp");
        document.SaveAs(target);

        Assert.Contains("    %NEWNAME Sums", document.CurrentText);
        Assert.DoesNotContain("%OLDNAME", document.CurrentText);
        Assert.Equal(("Sums", "Body"), DesignDocument.Open(target).HelpText);
    }

    [Fact]
    public void ANewAppsDetailsStartAsApplicationsDo()
    {
        DesignDocument document = DesignDocument.FromText(AppDesignerDocument.NewAppText("app1"), null);
        DesignAppDetailsValues details = document.AppDetailsValues;
        Assert.Equal("app1", details.Name);
        Assert.Equal("1.0", details.Version);
        Assert.Equal(string.Empty, details.Author);
        Assert.Equal(DesignScreenshotMode.Auto, details.Mode);
    }

    [Fact]
    public void DetailsArreOneStepWithTheInputArgumentsAndTheSingleInstance()
    {
        DesignDocument document = DesignDocument.FromText(AppDesignerDocument.NewAppText("app1"), null);
        DesignAppDetailsValues held = document.AppDetailsValues;

        document.SetAppDetails(held with { Name = "Named", Author = "Me" }, "a  b", true);

        Assert.Equal("Named", document.AppDetailsValues.Name);
        Assert.Equal("a, b", document.InputParameters);
        Assert.True(document.SingleInstance);
        Assert.NotNull(document.Code!.Startup);

        document.Undo();
        Assert.Equal("app1", document.AppDetailsValues.Name);
        Assert.Equal(string.Empty, document.InputParameters);
        Assert.False(document.SingleInstance);
        Assert.Null(document.Code!.Startup);
    }

    [Fact]
    public void AVersionThatWillNotDoLeavesTheOneHeldAndTheRowSaysSo()
    {
        DesignDocument document = DesignDocument.FromText(AppDesignerDocument.NewAppText("app1"), null);
        document.SetAppDetails(document.AppDetailsValues with { Version = "abc", Author = "Me" }, null, null);
        Assert.Equal("1.0", document.AppDetailsValues.Version);
        Assert.Equal("Me", document.AppDetailsValues.Author);

        Assert.Equal(DesignAppDetails.InvalidVersion, document.SetAppDetail("Version", "1"));
        Assert.Equal("1.0", document.AppDetailsValues.Version);
        Assert.Null(document.SetAppDetail("Version", "2.5"));
        Assert.Equal("2.5", document.AppDetailsValues.Version);
        document.Undo();
        Assert.Equal("1.0", document.AppDetailsValues.Version);
    }

    [Fact]
    public void DetailsGoIntoThePackageWhereFileWriterPutsThem()
    {
        string path = Saved();
        DesignDocument document = DesignDocument.Open(path);
        document.SetAppDetails(document.AppDetailsValues with { Name = "A <name> & more", Author = "An \"author\"", Version = "2.5", Summary = "A summary", Description = "Line one\nLine two" }, null, null);
        Assert.True(document.IsDirty);
        document.Save();

        string core = Part(path, "metadata/coreProperties.xml");
        Assert.Contains("<dc:title>A &lt;name&gt; &amp; more</dc:title>", core);
        Assert.Contains("<cp:version>2.5</cp:version>", core);
        Assert.Contains("<dc:description>A summary</dc:description>", core);
        Assert.Matches("<dcterms:modified xsi:type=\"dcterms:W3CDTF\">", core);
        Assert.Contains("<description>Line one\nLine two</description>", Part(path, "metadata/appMetadata.xml"));

        MlappMetadata read = JgsMlapp.ReadMetadata(path);
        Assert.Equal("A <name> & more", read.Name);
        Assert.Equal("An \"author\"", read.Author);
        Assert.Equal("2.5", read.Version);
        Assert.Equal("Line one\nLine two", read.Description);

        DesignAppDetailsValues reopened = DesignDocument.Open(path).AppDetailsValues;
        Assert.Equal("A <name> & more", reopened.Name);
        Assert.Equal("Line one\nLine two", reopened.Description);
    }

    [Fact]
    public void ASaveWithNoDetailsChangedKeepsThePartsBytes()
    {
        string path = Saved();
        string before = Part(path, "metadata/coreProperties.xml");
        DesignDocument document = DesignDocument.Open(path);
        _ = document.AppDetailsValues; // reading them changes nothing
        document.SetHelpText("Text", string.Empty);
        document.Save();
        Assert.Equal(before, Part(path, "metadata/coreProperties.xml"));
    }

    [Fact]
    public void ACustomImageIsAPartOfThePackageUnderItsTwoRelationships()
    {
        string path = Saved();
        DesignDocument document = DesignDocument.Open(path);
        string image = Picture(40, 30);
        document.SetAppDetails(document.AppDetailsValues with { Mode = DesignScreenshotMode.Manual, Thumbnail = DesignAppDetails.UriForPackage(image) }, null, null);
        document.Save();

        Assert.Contains("metadata/appScreenshot.png", Parts(path));
        string relationships = Part(path, "_rels/.rels");
        Assert.Contains("Target=\"metadata/appScreenshot.png\" Type=\"http://schemas.mathworks.com/appDesigner/app/2017/relationships/appScreenshot\"", relationships);
        Assert.Contains("Target=\"metadata/appScreenshot.png\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/thumbnail\"", relationships);
        Assert.Contains("<Default ContentType=\"image/png\" Extension=\"png\"/>", Part(path, "[Content_Types].xml"));
        Assert.Contains("<screenshotMode>manual</screenshotMode>", Part(path, "metadata/appMetadata.xml"));

        MlappMetadata read = JgsMlapp.ReadMetadata(path);
        Assert.Equal("manual", read.ScreenshotMode);
        Assert.Equal(File.ReadAllBytes(image), DesignAppDetails.OfDataUri(read.Screenshot)!.Value.Bytes);
        Assert.Equal(DesignScreenshotMode.Manual, DesignDocument.Open(path).AppDetailsValues.Mode);

        // Another format takes its place, and its two relationships with it.
        string jpeg = Picture(40, 30, "other.jpg");
        document = DesignDocument.Open(path);
        document.SetAppDetails(document.AppDetailsValues with { Thumbnail = DesignAppDetails.UriForPackage(jpeg) }, null, null);
        document.Save();
        Assert.Contains("metadata/appScreenshot.jpg", Parts(path));
        Assert.DoesNotContain("metadata/appScreenshot.png", Parts(path));
        relationships = Part(path, "_rels/.rels");
        Assert.DoesNotContain("appScreenshot.png", relationships);
        Assert.Equal(2, relationships.Split("appScreenshot.jpg").Length - 1);
        Assert.Contains("Extension=\"jpg\"", Part(path, "[Content_Types].xml"));
    }

    [Fact]
    public void APlainTextAppHoldsItsThumbnailInItsAppendix()
    {
        string path = Path.Combine(_folder, "PlainApp.m");
        DesignDocument document = DesignDocument.FromText(AppDesignerDocument.NewAppText("PlainApp"), null);
        document.SaveAs(path);
        Assert.True(document.DetailsAsPlainText);

        string uri = DesignAppDetails.UriForPlainText(Picture(1000, 300));
        document.SetAppDetails(document.AppDetailsValues with { Mode = DesignScreenshotMode.Manual, Thumbnail = uri, Author = "Me" }, null, null);
        document.Save();
        string text = File.ReadAllText(path);
        Assert.Contains("<Thumbnail autoCapture='false'>data:image/jpg;base64,", text);

        DesignDocument reopened = DesignDocument.Open(path);
        Assert.Equal(DesignScreenshotMode.Manual, reopened.AppDetailsValues.Mode);
        Assert.Equal(uri, reopened.AppDetailsValues.Thumbnail);
        Assert.Equal("Me", reopened.AppDetailsValues.Author);

        reopened.SetAppDetails(reopened.AppDetailsValues with { Mode = DesignScreenshotMode.None }, null, null);
        reopened.Save();
        Assert.Contains("<Thumbnail autoCapture='false'></Thumbnail>", File.ReadAllText(path));
        Assert.Equal(DesignScreenshotMode.None, DesignDocument.Open(path).AppDetailsValues.Mode);
    }

    [Fact]
    public void APlainTextThumbnailIsAJpegFittedInItsBox()
    {
        string uri = DesignAppDetails.UriForPlainText(Picture(1000, 300));
        (byte[] bytes, string format) = DesignAppDetails.OfDataUri(uri)!.Value;
        Assert.Equal("jpg", format);
        string written = Path.Combine(_folder, "back.jpg");
        File.WriteAllBytes(written, bytes);
        using ImageBuffer image = ImageCodec.Read(written);
        Assert.Equal((220, 66), (image.Width, image.Height));
    }

    [Fact]
    public void DetailsAndThumbnailGoAcrossTheFormats()
    {
        string path = Saved("Across");
        DesignDocument document = DesignDocument.Open(path);
        string uri = DesignAppDetails.UriForPackage(Picture(30, 20));
        document.SetAppDetails(document.AppDetailsValues with { Name = "Own name", Summary = "S", Mode = DesignScreenshotMode.Manual, Thumbnail = uri }, null, null);
        document.Save();

        string plain = Path.Combine(_folder, "Across2.m");
        document.SaveAs(plain);
        DesignAppDetailsValues there = DesignDocument.Open(plain).AppDetailsValues;
        Assert.Equal("Own name", there.Name);
        Assert.Equal("S", there.Summary);
        Assert.Equal(DesignScreenshotMode.Manual, there.Mode);
        Assert.Equal(uri, there.Thumbnail);

        string back = Path.Combine(_folder, "Across3.mlapp");
        document.SaveAs(back);
        MlappMetadata read = JgsMlapp.ReadMetadata(back);
        Assert.Equal("Own name", read.Name);
        Assert.Equal("manual", read.ScreenshotMode);
        Assert.Equal(uri, read.Screenshot);
    }

    /// <summary>Writes the .mlapp the window check opens (tools/runtime-checks/w8/W8aBin.mlapp) when <c>JGRAPH_W8A_FIXTURE</c> names where.</summary>
    [Fact]
    public void TheWindowChecksFixtureIsAnAppThisWriterWrites()
    {
        string file = Saved("W8aBin");
        Assert.Contains("metadata/appMetadata.xml", Parts(file));
        if (Environment.GetEnvironmentVariable("JGRAPH_W8A_FIXTURE") is { Length: > 0 } target)
        {
            File.Copy(file, target, overwrite: true);

            // Stage W8b's: an app with one input argument, which its startup function reads.
            string second = Saved("W8bApp");
            using DesignDocument document = DesignDocument.Open(second);
            Assert.Null(document.SetInputParameters("n"));
            string empty = "        function startupFcn(app, n)\n            \n        end";
            Assert.True(document.ApplyEditedText(document.CurrentText.Replace(empty, "        function startupFcn(app, n)\n            app.UIFigure.Name = sprintf('n is %d', n);\n        end", StringComparison.Ordinal)));
            document.Save();
            File.Copy(second, Path.Combine(Path.GetDirectoryName(target)!, "W8bApp.mlapp"), overwrite: true);
        }
    }

    [Fact]
    public void TheActionsAreLive()
    {
        Assert.True(DesignActions.IsLive("AD_APP_DETAILS"));
        Assert.True(DesignActions.IsLive("AD_HELP_COMMENTS"));
    }

    [Fact]
    public void AnImageOfAnotherKindIsRefused()
    {
        Assert.Equal(DesignAppDetails.InvalidImage, DesignAppDetails.WhyNotImage("a.bmp"));
        Assert.Null(DesignAppDetails.WhyNotImage("a.JPEG"));
        Assert.StartsWith("data:image/png;base64,", PngOfTwo);
    }
}
