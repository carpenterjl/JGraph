using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Signal;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M21's acceptance gate: the two shipped signal-processing examples (FM modulation / demodulation
/// with firpm+freqz+filter, and FFT audio compression with audioread+sound+pause) run headless end
/// to end. The scripts here mirror examples/fm-demod.jgs and examples/audio-compression.jgs, except
/// the audio test uses a short generated gc.wav so pause() stays quick.
/// </summary>
[Collection("JG facade")]
public class JgsMatlabDemoEndToEndTests : IDisposable
{
    private readonly JgsScriptEngine _engine = new();
    private readonly List<(int Number, FigureModel Figure)> _shown = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly RecordingScriptAudio _audio = new();
    private readonly string _directory;

    public JgsMatlabDemoEndToEndTests()
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
        _engine.RunAsync(code, new ScriptContext(
            _output, (n, f) => _shown.Add((n, f)), _directory, resolvePath: null,
            figureFiles: null, audio: _audio), default);

    [Fact]
    public async Task FmModulationLab_RunsEndToEnd_WithFourFigures()
    {
        // examples/fm-demod.jgs, verbatim.
        ScriptRunResult result = await Run("""
            let fc = 15;
            let fs = 1000;
            let t = 0:1/fs:3;
            let fm = 1;

            let v = sin(2*pi*fm*t);

            let w0 = 2*pi*15;
            let c = 8;

            let theta = zeros(size(t));

            for k = 2:length(t)
                theta(k) = theta(k-1) + (w0 + c*v(k))/fs;
            end

            let x_fm = cos(theta);

            figure;
            subplot(3,1,1);
            plot(t,v);
            title('v(t)');
            subplot(3,1,2);
            plot(t, x_fm);
            title('FM Signal Using VCO Method');
            xlabel('Time (s)');
            ylabel('Amplitude');

            subplot(3,1,3);
            let x_int = cumsum(v)/fs;
            let kx = c * x_int;
            plot(t,kx);
            title('k x(t)');

            let cos_carrier = cos(2*pi*fc*t);

            let sin_carrier = sin(2*pi*fc*t);

            let h = firpm(127,[0, 20, 30, fs/2]/(fs/2), [1, 1, 0, 0]);

            let [H,f] = freqz(h,1,1024,fs);
            figure;
            plot(f, abs(H));
            title('LPF Magnitude Response');
            xlabel('Frequency (Hz)');
            ylabel('|H(f)|');

            let lp_1 = filter(h, 1, cos_carrier .* x_fm);
            let lp_2 = filter(h, 1, sin_carrier .* x_fm);
            let output = atan2(-lp_2,lp_1);
            figure;
            plot(t,output(1:length(t)));
            title('Demodulated Signal')
            figure;
            plot(t, kx, 'b', t, output(1:length(t)), 'r--');
            legend('Original kx(t)', 'Demodulated');
            title('Verification of FM Demodulation');
            """);

        Assert.True(result.Success, result.Message);

        // Four figures auto-shown, in creation order.
        Assert.Equal(4, _shown.Count);
        Assert.Equal(new[] { 1, 2, 3, 4 }, _shown.Select(s => s.Number).ToArray());

        // Figure 1: the 3-pane subplot with t = 3001 samples per series.
        FigureModel first = _shown[0].Figure;
        Assert.Equal(3, first.Axes.Count);
        var vSeries = Assert.IsType<LinePlot>(Assert.Single(first.Axes[0].Plots));
        Assert.Equal(3001, vSeries.Data.Count);
        Assert.Equal("v(t)", first.Axes[0].Title);

        // Figure 2: the LPF response — passband ~1 at DC, deep stopband past 30 Hz.
        var response = Assert.IsType<LinePlot>(Assert.Single(_shown[1].Figure.Axes[0].Plots));
        Assert.True(System.Math.Abs(response.Data.GetY(0) - 1) < 0.06, $"DC gain {response.Data.GetY(0)}");
        double stopPeak = 0;
        for (int i = 0; i < response.Data.Count; i++)
        {
            if (response.Data.GetX(i) >= 30)
            {
                stopPeak = System.Math.Max(stopPeak, response.Data.GetY(i));
            }
        }

        Assert.True(stopPeak < 0.06, $"stopband peak {stopPeak}");

        // Figure 4: the verification overlay carries both series.
        Assert.Equal(2, _shown[3].Figure.Axes[0].Plots.Count);
        Assert.Equal("Verification of FM Demodulation", _shown[3].Figure.Axes[0].Title);
    }

    [Fact]
    public async Task AudioCompressionLab_RunsEndToEnd_AndPlaysTwice()
    {
        // A short guitar-ish pluck (0.2 s at 8 kHz — odd length, exercising Bluestein) stands in
        // for gc.wav so the script's pause(N/fs + 1) waits ~1.2 s instead of ~27 s.
        const int rate = 8000;
        double[] pluck = new double[1601];
        var random = new Random(7);
        for (int i = 0; i < 40; i++)
        {
            pluck[i] = (random.NextDouble() * 2) - 1; // Karplus-Strong-style noise burst
        }

        for (int i = 40; i < pluck.Length; i++)
        {
            pluck[i] = 0.996 * ((pluck[i - 40] + pluck[i - 39]) / 2);
        }

        using (FileStream stream = File.Create(Path.Combine(_directory, "gc.wav")))
        {
            WaveFile.Write16BitPcm(stream, pluck, rate);
        }

        // examples/audio-compression.jgs, verbatim.
        ScriptRunResult result = await Run("""
            let [audio_sample, fs] = audioread('gc.wav');

            let N_orig = length(audio_sample);
            let X = fft(audio_sample);
            let X_shifted = fftshift(X);
            let f_axis_orig = (-N_orig/2 : N_orig/2 - 1) * (fs / N_orig);

            figure;
            subplot(3,1,1);
            plot(f_axis_orig,20 * log10(abs(X_shifted)));
            title('Original Audio Spectrum');
            xlabel('Frequency');
            ylabel('Magnitude (dB)');
            xlim([-fs/2, fs/2]);

            let N = N_orig;
            let rem8 = mod(N, 8);
            let x_pad = 0;
            if rem8 ~= 0
                x_pad = [audio_sample; zeros(8 - rem8, 1)];
                N = length(x_pad);
            else
                x_pad = audio_sample;
            end
            let X_pad = fft(x_pad);
            let X_pad_shifted = fftshift(X_pad);

            let X_comp75 = X_pad_shifted;
            X_comp75(1 : N/8) = 0;
            X_comp75(end - N/8 + 1 : end) = 0;
            let f_axis_pad = (-N/2 : N/2 - 1) * (fs / N);

            subplot(3,1,2);
            plot(f_axis_pad, 20 * log10(abs(X_comp75)));
            title('75% Compressed Spectrum (Edge Frequencies Zeroed)');
            xlabel('Frequency');
            ylabel('Magnitude (dB)');
            xlim([-fs/2, fs/2]);

            let X_back75 = ifftshift(X_comp75);
            let y75 = real(ifft(X_back75));
            disp('Playing 75% compressed audio...');
            sound(y75, fs);
            pause(N/fs + 1);

            N = N_orig;
            let rem4 = mod(N, 4);
            let x_pad4 = 0;
            if rem4 ~= 0
                x_pad4 = [audio_sample; zeros(4 - rem4, 1)];
                N = length(x_pad4);
            else
                x_pad4 = audio_sample;
            end
            let X_pad4 = fft(x_pad4);
            let X_pad4_shifted = fftshift(X_pad4);

            let X_comp50 = X_pad4_shifted;
            X_comp50(1 : N/4) = 0;
            X_comp50(end - N/4 + 1 : end) = 0;
            let f_axis_pad4 = (-N/2 : N/2 - 1) * (fs / N);

            subplot(3,1,3);
            plot(f_axis_pad4, 20 * log10(abs(X_comp50)));
            title('50% Compressed Spectrum (Edge Frequencies Zeroed)');
            xlabel('Frequency');
            ylabel('Magnitude (dB)');
            xlim([-fs/2, fs/2]);

            let X_back50 = ifftshift(X_comp50);
            let y50 = real(ifft(X_back50));

            disp('Playing 50% compressed audio...');
            sound(y50, fs);
            """);

        Assert.True(result.Success, result.Message);

        // One figure with the three spectrum panes, each spanning ±fs/2.
        (int number, FigureModel figure) = Assert.Single(_shown);
        Assert.Equal(1, number);
        Assert.Equal(3, figure.Axes.Count);
        foreach (AxesModel axes in figure.Axes)
        {
            Assert.Equal(-rate / 2.0, axes.PrimaryXAxis.Range.Min);
            Assert.Equal(rate / 2.0, axes.PrimaryXAxis.Range.Max);
        }

        // Both compressed versions played, padded to multiples of 8 and 4 respectively.
        Assert.Equal(2, _audio.Played.Count);
        Assert.Equal(1608, _audio.Played[0].Samples.Length); // 1601 padded to /8
        Assert.Equal(1604, _audio.Played[1].Samples.Length); // 1601 padded to /4
        Assert.All(_audio.Played, p => Assert.Equal(rate, p.SampleRate));

        Assert.Contains("Playing 75% compressed audio...", _output.NormalText);
        Assert.Contains("Playing 50% compressed audio...", _output.NormalText);
    }
}
