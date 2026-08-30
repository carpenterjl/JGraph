using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using SkiaSharp;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// <c>BackgroundColor 'none'</c> at the script's end of the wire, and the two words MATLAB accepts
/// beside it.
/// </summary>
[Collection("JG facade")]
public sealed class MatlabTransparentExportTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "jgraph-transparent-" + Guid.NewGuid().ToString("N"));

    public MatlabTransparentExportTests()
    {
        JG.Reset();
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
            // A leftover temporary directory is not worth failing a test over.
        }
    }

    private async Task<ScriptRunResult> Run(string code)
    {
        await using IScriptSession session = Assert
            .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
            .CreateSession(new ScriptContext(
                _output, (_, _) => { }, _directory, resolvePath: null, new TestFigureFiles()));
        return await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
    }

    private string Path_(string name) => System.IO.Path.Combine(_directory, name).Replace(@"\", @"\\");

    [Fact]
    public async Task NoneWritesAPngWhoseCornersAreEmpty()
    {
        string file = System.IO.Path.Combine(_directory, "cutout.png");
        ScriptRunResult result = await Run($"""
            [X, Y] = meshgrid(linspace(-3, 3, 12));
            surf(X, Y, sin(sqrt(X.^2 + Y.^2) + eps), 'EdgeColor', 'none');
            axis off
            exportgraphics(gcf, '{Path_("cutout.png")}', 'BackgroundColor', 'none');
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        using SKBitmap image = SKBitmap.Decode(file);
        Assert.Equal(0, image.GetPixel(0, 0).Alpha);
        Assert.Equal(0, image.GetPixel(image.Width - 1, 0).Alpha);
    }

    /// <summary>
    /// The figure keeps the colour it had: an export option is a property of the export and not a
    /// change to the figure, so the next frame of an animation loop is drawn as before.
    /// </summary>
    [Fact]
    public async Task NoneDoesNotLeaveTheFigureTransparent()
    {
        ScriptRunResult result = await Run($"""
            figure('Color', [0.03 0.04 0.08]);
            plot(1:10);
            exportgraphics(gcf, '{Path_("once.png")}', 'BackgroundColor', 'none');
            c = get(gcf, 'Color');
            assert(abs(c(3) - 0.08) < 0.01, 'the figure kept its own colour');
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    /// <summary>A format that cannot carry alpha says so, rather than writing a black rectangle.</summary>
    [Fact]
    public async Task NoneIsRefusedByNameForAFormatThatCannotCarryIt()
    {
        ScriptRunResult result = await Run($"""
            plot(1:10);
            exportgraphics(gcf, '{Path_("flat.jpg")}', 'BackgroundColor', 'none');
            """);

        Assert.False(result.Success);
        Assert.Contains("transparency", result.Message + _output.ErrorText, StringComparison.Ordinal);
    }

    /// <summary>'current' is MATLAB's word for "the colour it already wears".</summary>
    [Fact]
    public async Task CurrentKeepsTheFiguresOwnColour()
    {
        string file = System.IO.Path.Combine(_directory, "asis.png");
        ScriptRunResult result = await Run($"""
            figure('Color', [1 0 0]);
            plot(1:10);
            axis off
            exportgraphics(gcf, '{Path_("asis.png")}', 'BackgroundColor', 'current');
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        using SKBitmap image = SKBitmap.Decode(file);
        SKColor corner = image.GetPixel(0, 0);
        Assert.Equal(255, corner.Alpha);
        Assert.Equal(255, corner.Red);
        Assert.Equal(0, corner.Green);
    }
}
