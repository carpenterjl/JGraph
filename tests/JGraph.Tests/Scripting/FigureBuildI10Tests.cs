using JGraph.Api;
using JGraph.Numerics;
using JGraph.Rendering.Skia;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using SkiaSharp;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The every-row plan's I10 (ADR 0331): a histogram keeps the bins it was asked for and counts
/// its readings once, with <c>histcounts</c>' kernels; an image's numbers are laid out in one
/// pass; and a scatter coloured point by point is stamped from masks when it is exported to
/// pixels. The histogram expectations are R2025b's (probe <c>chk4.m</c>).
/// </summary>
[Collection("JG facade")]
public sealed class FigureBuildI10Tests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "jgraph-i10-" + Guid.NewGuid().ToString("N"));

    public FigureBuildI10Tests()
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

    private async Task Asserts(string code)
    {
        await using IScriptSession session = Assert
            .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
            .CreateSession(new ScriptContext(
                _output, (_, _) => { }, _directory, resolvePath: null, new TestFigureFiles()));
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    private string Path_(string name) => Path.Combine(_directory, name).Replace(@"\", @"\\");

    // --- histogram -------------------------------------------------------------------------------

    [Fact]
    public async Task AHistogramKeepsTheBinsItWasAskedFor() => await Asserts("""
        x = sin((1:30000)*0.37)*3 + mod((1:30000)*0.618033988749895, 1);
        h = histogram(x, 200);
        assert(h.NumBins == 200 && strcmp(h.BinMethod, 'manual'));
        [n, e] = histcounts(x, 200);
        assert(isequal(h.Values, n) && isequal(h.BinEdges, e));
        h = histogram(x, [-4 -1 0 2 5]);
        assert(isequal(h.BinEdges, [-4 -1 0 2 5]) && strcmp(h.BinMethod, 'manual'));
        assert(isequal(h.Values, histcounts(x, [-4 -1 0 2 5])));
        h = histogram(x, 12, 'BinLimits', [-1 2]);
        assert(h.NumBins == 12 && isequal(h.BinLimits, [-1 2]) && strcmp(h.BinMethod, 'manual'));
        [n, e] = histcounts(x, 12, 'BinLimits', [-1 2]);
        assert(isequal(h.Values, n) && isequal(h.BinEdges, e));
        h = histogram(x, 'BinWidth', 0.5);
        assert(h.NumBins == 14 && h.BinWidth == 0.5 && strcmp(h.BinMethod, 'manual'));
        h = histogram(x, 'NumBins', 7);
        assert(h.NumBins == 7 && strcmp(h.BinMethod, 'manual'));
        h = histogram(x, 'BinMethod', 'sturges');
        assert(h.NumBins == 14 && strcmp(h.BinMethod, 'sturges'));
        h = histogram(x);
        assert(h.NumBins == 35 && strcmp(h.BinMethod, 'auto'));
        [n, e] = histcounts(x);
        assert(isequal(h.Values, n) && isequal(h.BinEdges, e));
        h = histogram(x, 200); h.NumBins = 30;
        assert(h.NumBins == 30 && strcmp(h.BinMethod, 'manual'));
        h = histogram(x, 200); h.BinMethod = 'sturges';
        assert(h.NumBins == 14 && strcmp(h.BinMethod, 'sturges'));
        h = histogram(x, 200); h.BinEdges = linspace(-5, 5, 11);
        assert(isequal(h.Values, histcounts(x, linspace(-5, 5, 11))));
        """);

    [Fact]
    public async Task AHistogramOfTheFewAndTheMissingIsWhatItWas() => await Asserts("""
        h = histogram([1 2 2 3 3 3], 3);
        assert(isequal(h.Values, [1 2 3]));
        h = histogram(zeros(1, 0), 4);
        assert(isequal(h.Values, [0 0 0 0]) && isequal(h.BinEdges, [0 0.25 0.5 0.75 1]));
        h = histogram([NaN NaN], 4);
        assert(isequal(h.Values, [0 0 0 0]) && isequal(h.BinEdges, [0 0.25 0.5 0.75 1]));
        h = histogram([1 NaN 2 Inf 2 -Inf 3], 2);
        [n, e] = histcounts([1 NaN 2 Inf 2 -Inf 3], 2);
        assert(isequal(h.Values, n) && isequal(h.BinEdges, e));
        h = histogram('BinEdges', [0 1 2], 'BinCounts', [5 7]);
        assert(isequal(h.Values, [5 7]) && isequal(h.BinEdges, [0 1 2]));
        """);

    [Fact]
    public async Task ALongHistogramIsHistcountsOnOneThreadAndOnMany()
    {
        if (!JgsPacking.Enabled)
        {
            return; // three million boxed readings, for kernels boxed storage reaches flattened anyway
        }

        const string Code = """
            x = sin((1:3000000)*0.37)*3 + mod((1:3000000)*0.618033988749895, 1);
            x(5) = NaN; x(77) = Inf; x(99) = -Inf;
            h = histogram(x, 200); [n, e] = histcounts(x, 200);
            assert(isequal(h.Values, n) && isequal(h.BinEdges, e));
            assert(sum(h.Values) == numel(x) - 3);
            h.BinEdges = linspace(-5, 5, 11);
            assert(isequal(h.Values, histcounts(x, linspace(-5, 5, 11))));
            """;
        int degree = ParallelKernels.MaxDegree;
        try
        {
            ParallelKernels.MaxDegree = 1;
            await Asserts(Code);
            JG.Reset();
            ParallelKernels.MaxDegree = Math.Max(2, degree);
            await Asserts(Code);
        }
        finally
        {
            ParallelKernels.MaxDegree = degree;
        }
    }

    // --- image data ------------------------------------------------------------------------------

    [Fact]
    public async Task AnImageHoldsTheNumbersItWasGiven() => await Asserts("""
        hi = imagesc([1 2; 3 4]);
        assert(isequal(hi.CData, [1 2; 3 4]) && isequal(caxis, [1 4]));
        hi = imagesc(1:5);
        assert(isequal(hi.CData, 1:5) && isequal(caxis, [1 5]));
        hi = imagesc((1:5)');
        assert(isequal(hi.CData, (1:5)') && isequal(caxis, [1 5]));
        U = uint8(mod(reshape(0:59, 6, 10)*7, 256));
        hi = image(U);
        assert(isequal(hi.CData, U) && isa(hi.CData, 'uint8'));
        hi = imagesc([1 2; 3 4] > 2);
        assert(islogical(hi.CData) && isequal(hi.CData, [1 2; 3 4] > 2) && isequal(caxis, [0 1]));
        Z = [1 NaN; -Inf 4; 2 Inf];
        hi = imagesc(Z);
        assert(isequaln(hi.CData, Z) && isequal(caxis, [1 4]));
        hi = imagesc(-[0 0; 0 0]);
        c = caxis; assert(c(1) < c(2));
        """);

    [Fact]
    public async Task ALargeImageHoldsItsNumbersAndItsRange()
    {
        if (!JgsPacking.Enabled)
        {
            return; // over a million boxed elements for a road boxed storage does not take
        }

        await Asserts("""
            Z = reshape(sin((1:1200*1100)*0.01), 1200, 1100);
            hi = imagesc(Z);
            assert(isequal(hi.CData, Z));
            assert(isequal(caxis, [min(Z(:)) max(Z(:))]));
            Z(3, 5) = NaN; Z(7, 9) = Inf; Z(8, 9) = -Inf;
            hi = imagesc(Z);
            assert(isequaln(hi.CData, Z));
            f = Z(isfinite(Z));
            assert(isequal(caxis, [min(f) max(f)]));
            hi.CData(1, 1) = 99;
            assert(Z(1, 1) ~= 99);
            """);
    }

    // --- a scatter coloured point by point ---------------------------------------------------------

    private async Task<SKBitmap> Exported(string name, bool stamped, string plot)
    {
        bool before = SkiaRenderContext.MarkerSprites;
        SkiaRenderContext.MarkerSprites = stamped;
        try
        {
            JG.Reset();
            await Asserts($"""
                [gxm, gym] = meshgrid(1:40, 1:30);
                gx = gxm(:)' + 0.37*mod((1:1200)*0.618033988749895, 1);
                gy = gym(:)' + 0.41*mod((1:1200)*0.381966011250105, 1);
                cval = mod(1:1200, 17);
                figure(1); clf;
                set(gcf, 'Position', [100 100 700 560]);
                {plot}
                exportgraphics(gcf, '{Path_(name)}', 'Resolution', 150);
                close all;
                """);
        }
        finally
        {
            SkiaRenderContext.MarkerSprites = before;
        }

        return SKBitmap.Decode(Path.Combine(_directory, name));
    }

    private static (int Differing, int Largest, long InkA, long InkB) Compare(SKBitmap a, SKBitmap b)
    {
        Assert.Equal(a.Width, b.Width);
        Assert.Equal(a.Height, b.Height);
        int differing = 0;
        int largest = 0;
        long inkA = 0;
        long inkB = 0;
        for (int y = 0; y < a.Height; y++)
        {
            for (int x = 0; x < a.Width; x++)
            {
                SKColor p = a.GetPixel(x, y);
                SKColor q = b.GetPixel(x, y);
                int d = Math.Max(Math.Abs(p.Red - q.Red), Math.Max(Math.Abs(p.Green - q.Green), Math.Abs(p.Blue - q.Blue)));
                differing += d > 0 ? 1 : 0;
                largest = Math.Max(largest, d);
                inkA += 765 - p.Red - p.Green - p.Blue;
                inkB += 765 - q.Red - q.Green - q.Blue;
            }
        }

        return (differing, largest, inkA, inkB);
    }

    [Theory]
    [InlineData("scatter(gx, gy, 4, cval, 'filled');")]
    [InlineData("scatter(gx, gy, 36, cval, 'filled'); xlim([3.2 31.7]); ylim([2.6 25.3]);")]
    [InlineData("scatter(gx, gy, 20, cval);")]
    [InlineData("scatter(gx, gy, 30, cval, 's', 'filled');")]
    [InlineData("scatter(gx, gy, 30, cval, '+');")]
    [InlineData("scatter(gx, gy, 36, 'filled');")]
    public async Task AStampedCloudIsThePictureDrawnOneMarkerAtATime(string plot)
    {
        using SKBitmap stamped = await Exported("stamped.png", stamped: true, plot);
        using SKBitmap drawn = await Exported("drawn.png", stamped: false, plot);
        (int differing, int largest, long inkStamped, long inkDrawn) = Compare(stamped, drawn);

        // The markers are there (the two pictures are not both blank), and nothing is stamped
        // where the clip keeps a marker out. A stamp is the marker Skia draws a sixty-fourth of a
        // pixel away at most, and Skia's own edges move in quarter-pixel steps on each of four
        // sub-rows, so an edge pixel can differ by one such step on every sub-row: a quarter of
        // full ink, and no more.
        Assert.True(inkDrawn > 100_000, "the one-at-a-time picture has no markers in it");
        Assert.True(largest <= 72, $"a pixel differs by {largest} levels");
        Assert.True(differing < stamped.Width * stamped.Height / 20, $"{differing} pixels differ");
        Assert.True(Math.Abs(inkStamped - inkDrawn) < inkDrawn / 50, $"ink {inkStamped} against {inkDrawn}");
    }

    [Theory]
    [InlineData("scatter(gx, gy, 36, cval, 'filled', 'MarkerFaceAlpha', 0.5);")]
    [InlineData("scatter(gx(1:100), gy(1:100), 36, cval(1:100), 'filled');")]
    [InlineData("scatter(gx, gy, 36, cval, 'filled', 'MarkerEdgeColor', 'k');")]
    public async Task WhatCannotBeStampedIsDrawnAsBefore(string plot)
    {
        using SKBitmap on = await Exported("on.png", stamped: true, plot);
        using SKBitmap off = await Exported("off.png", stamped: false, plot);
        (int differing, _, _, long ink) = Compare(on, off);
        Assert.True(ink > 10_000, "the picture has no markers in it");
        Assert.Equal(0, differing);
    }
}
