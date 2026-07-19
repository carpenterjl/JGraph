using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Imaging;
using JGraph.Imaging.Codecs;
using JGraph.Objects;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M24: the image builtins driven from JGS — imread/imwrite round-trips, and imshow producing the
/// right plot object (grayscale via ImagePlot, RGB via RgbImagePlot) with MATLAB imshow axes styling.
/// </summary>
[Collection("JG facade")]
public sealed class JgsImageBuiltinTests : IDisposable
{
    private readonly JgsScriptEngine _engine = new();
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;

    public JgsImageBuiltinTests()
    {
        JG.Reset();
        _directory = Path.Combine(Path.GetTempPath(), "jgraph-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        JG.Reset();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private Task<ScriptRunResult> Run(string code) =>
        _engine.RunAsync(code, new ScriptContext(_output, (_, figure) => _figures.Add(figure), _directory), default);

    private string WriteRgb(string name = "in.png")
    {
        using var image = new ImageBuffer(2, 2, 3);
        image[0, 0, 0] = 1.0;   image[0, 1, 1] = 1.0;
        image[1, 0, 2] = 1.0;   image[1, 1, 0] = image[1, 1, 1] = image[1, 1, 2] = 1.0;
        ImageCodec.Write(Path.Combine(_directory, name), image);
        return name;
    }

    private string WriteGray(string name = "g.png")
    {
        using var image = new ImageBuffer(3, 3, 1);
        image[1, 1, 0] = 204 / 255.0;
        ImageCodec.Write(Path.Combine(_directory, name), image);
        return name;
    }

    [Fact]
    public async Task Imwrite_ThenImread_RoundTripsPixels()
    {
        string source = WriteRgb();
        ScriptRunResult result = await Run($"""
            let a = imread('{source}');
            imwrite(a, 'out.png');
            let b = imread('out.png');
            print(b(1, 1, 1))
            print(b(1, 2, 2))
            print(b(2, 1, 3))
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal("1\n1\n1", _output.NormalText.Trim().ReplaceLineEndings("\n"));
        Assert.True(File.Exists(Path.Combine(_directory, "out.png")));
    }

    [Fact]
    public async Task Imshow_Rgb_ProducesAnRgbImagePlotWithImshowAxes()
    {
        string source = WriteRgb();
        ScriptRunResult result = await Run($"""
            imshow(imread('{source}'));
            show()
            """);

        Assert.True(result.Success, result.Message);
        AxesModel axes = Assert.Single(_figures)!.Axes[^1];
        Assert.Contains(axes.Plots, p => p is RgbImagePlot);
        Assert.True(axes.EqualAspect);
        Assert.False(axes.FrameVisible);
        Assert.False(axes.PrimaryXAxis.ShowTickLabels);
        Assert.False(axes.PrimaryYAxis.ShowMajorTicks);
    }

    [Fact]
    public async Task Imshow_Grayscale_ProducesAGrayColormappedImagePlot()
    {
        string source = WriteGray();
        ScriptRunResult result = await Run($"""
            imshow(imread('{source}'));
            show()
            """);

        Assert.True(result.Success, result.Message);
        AxesModel axes = Assert.Single(_figures)!.Axes[^1];
        var plot = Assert.IsType<ImagePlot>(axes.Plots.First(p => p is ImagePlot));
        Assert.False(plot.AutoScaleColor);
        Assert.Equal(0, plot.ColorMin);
        Assert.Equal(1, plot.ColorMax);
        Assert.True(axes.EqualAspect);
    }

    [Fact]
    public async Task Imshow_OfAMatrix_PointsAtImagesc()
    {
        ScriptRunResult result = await Run("imshow([[1, 2], [3, 4]])");
        Assert.False(result.Success);
        Assert.Contains("imagesc", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Imread_MissingFile_ReportsAScriptError()
    {
        ScriptRunResult result = await Run("let x = imread('nope.png')");
        Assert.False(result.Success);
        Assert.Contains("imread", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShippedExample_RunsEndToEnd_AndFindsThreeRegions()
    {
        // Runs examples/matlab-image-processing.jgs against examples/sample-image.png exactly as
        // shipped, so the documented walkthrough (and the segmentation count) can never silently rot.
        string examples = LocateExamplesDirectory();
        string script = await File.ReadAllTextAsync(Path.Combine(examples, "matlab-image-processing.jgs"));

        ScriptRunResult result = await _engine.RunAsync(
            script, new ScriptContext(_output, (_, figure) => _figures.Add(figure), examples), default);

        try
        {
            Assert.True(result.Success, result.Message);
            Assert.Equal(2, _figures.Count); // the original and the Sobel-edge views
            Assert.Contains("Found 3 regions", _output.NormalText);
        }
        finally
        {
            // The example writes a mask next to the sources; keep the tree clean.
            string mask = Path.Combine(examples, "sample-mask.png");
            if (File.Exists(mask))
            {
                File.Delete(mask);
            }
        }
    }

    private static string LocateExamplesDirectory()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "examples");
            if (File.Exists(Path.Combine(candidate, "matlab-image-processing.jgs")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository's examples directory.");
    }
}
