using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Signal;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M21c: the DSP/audio builtins from JGS — fft/ifft/fftshift, filter/butter/firpm/freqz,
/// audioread/sound/pause, and the MATLAB helpers (mod, size, disp, zeros(size(t)), xlim([a,b]),
/// multi-series plot).
/// </summary>
[Collection("JG facade")]
public class JgsDspBuiltinTests : IDisposable
{
    private readonly JgsScriptEngine _engine = new();
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly RecordingScriptAudio _audio = new();
    private readonly string _directory;

    public JgsDspBuiltinTests()
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

    private Task<ScriptRunResult> Run(string code, CancellationToken cancellationToken = default) =>
        _engine.RunAsync(code, new ScriptContext(
            _output, (_, figure) => _figures.Add(figure), _directory, resolvePath: null,
            figureFiles: null, audio: _audio), cancellationToken);

    [Fact]
    public async Task Fft_Ifft_RoundTrip_AndSpectrumPeak()
    {
        // A pure 8-of-64 tone: |X| peaks at bins 8 and 56, and ifft restores the signal.
        ScriptRunResult result = await Run("""
            let n = 0:63;
            let x = sin(2 * pi * 8 * n / 64);
            let X = fft(x);
            let mags = abs(X);
            print(find(mags > 30))
            let back = real(ifft(X));
            print(max(abs(back - x)) < 1e-10)
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "[8, 56]\n", "true\n" }, _output.Normal); // 0-based bins
    }

    [Fact]
    public async Task Fftshift_CentersDc_AndIfftshiftUndoesIt()
    {
        ScriptRunResult result = await Run("""
            print(fftshift([0, 1, 2, 3]))
            print(fftshift([0, 1, 2, 3, 4]))
            print(ifftshift(fftshift([5, 6, 7, 8, 9])))
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "[2, 3, 0, 1]\n", "[3, 4, 0, 1, 2]\n", "[5, 6, 7, 8, 9]\n" }, _output.Normal);
    }

    [Fact]
    public async Task Filter_AndButter_AreCallable_WithScalarDenominator()
    {
        ScriptRunResult result = await Run("""
            let y = filter([0.5, 0.5], 1, [1, 1, 1, 1]);
            print(y)
            let [b, a] = butter(2, 0.4);
            print(length(b), length(a))
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "[0.5, 1, 1, 1]\n", "3 3\n" }, _output.Normal);
    }

    [Fact]
    public async Task Firpm_And_Freqz_ComposeLikeTheDemo()
    {
        ScriptRunResult result = await Run("""
            let fs = 1000;
            let h = firpm(40, [0, 0.2, 0.4, 1], [1, 1, 0, 0]);
            let [H, f] = freqz(h, 1, 256, fs);
            print(length(h), length(H), length(f))
            print(f(end))
            print(abs(H(1)) > 0.9)
            print(abs(H(end)) < 0.1)
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "41 256 256\n", "498.046875\n", "true\n", "true\n" }, _output.Normal);
    }

    [Fact]
    public async Task Audioread_And_Sound_FlowThroughTheHost()
    {
        // Write a little 100-sample wav, read it back in JGS, and play it.
        double[] samples = Enumerable.Range(0, 100).Select(i => System.Math.Sin(0.2 * i) * 0.5).ToArray();
        using (FileStream stream = File.Create(Path.Combine(_directory, "tone.wav")))
        {
            WaveFile.Write16BitPcm(stream, samples, 8000);
        }

        ScriptRunResult result = await Run("""
            let [y, fs] = audioread("tone.wav");
            print(length(y), fs)
            sound(y, fs);
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "100 8000\n" }, _output.Normal);
        (double[] played, int rate) = Assert.Single(_audio.Played);
        Assert.Equal(100, played.Length);
        Assert.Equal(8000, rate);
    }

    [Fact]
    public async Task Sound_WithoutAHostSink_FailsClearly()
    {
        ScriptRunResult result = await _engine.RunAsync(
            "sound([0, 0.5], 8000)",
            new ScriptContext(_output, (_, _) => { }, _directory), default);

        Assert.False(result.Success);
        Assert.Contains("not supported by this host", Assert.Single(result.Diagnostics).Message);
    }

    [Fact]
    public async Task Pause_IsCancellable()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var watch = System.Diagnostics.Stopwatch.StartNew();

        ScriptRunResult result = await Run("pause(30)", cts.Token);
        watch.Stop();

        Assert.False(result.Success);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"pause ignored cancellation ({watch.Elapsed})");
    }

    [Fact]
    public async Task MatlabHelpers_Mod_Size_Disp()
    {
        ScriptRunResult result = await Run("""
            print(mod(7, 3), mod(-1, 8), mod(10, -3))
            print(size([1, 2, 3]))
            print(size([1, 2; 3, 4]))
            print(size("abc"))
            disp("hello")
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "1 7 -2\n", "[1, 3]\n", "[2, 2]\n", "[1, 3]\n", "hello\n" }, _output.Normal);
    }

    [Fact]
    public async Task Zeros_AcceptsASizeVector()
    {
        ScriptRunResult result = await Run("""
            let t = 1:5;
            let z = zeros(size(t));
            print(length(z), sum(z))
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "5 0\n" }, _output.Normal);
    }

    [Fact]
    public async Task Xlim_AcceptsATwoElementArray()
    {
        ScriptRunResult result = await Run("""
            plot([1, 2, 3], [4, 5, 6]);
            xlim([-2, 2]);
            show;
            """);

        Assert.True(result.Success, result.Message);
        AxesModel axes = Assert.Single(_figures).Axes[0];
        Assert.Equal(-2, axes.PrimaryXAxis.Range.Min);
        Assert.Equal(2, axes.PrimaryXAxis.Range.Max);
    }

    [Fact]
    public async Task Plot_MultiSeriesGroups_AddSeparateSeries_AndRestoreHold()
    {
        ScriptRunResult result = await Run("""
            let t = [1, 2, 3];
            plot(t, [1, 2, 3], 'b', t, [3, 2, 1], 'r--');
            show;
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, Assert.Single(_figures).Axes[0].Plots.Count);
        Assert.False(JG.IsHolding);
    }
}
