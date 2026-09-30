using System.Diagnostics;
using System.Runtime.Versioning;
using JGraph.Devices.Audio;
using JGraph.Devices.Simulation;
using Xunit;

namespace JGraph.Tests.Devices;

/// <summary>
/// Audio devices (device classes plan, stage D10, ADR 0193): the machine's list in R2025b's order and
/// names, one silent playback on its primary output (zeros, which nothing hears), and the simulated
/// devices the fixtures use. No test records from a microphone.
/// </summary>
[SupportedOSPlatform("windows")]
public class AudioTests
{
    [Fact]
    public void TheMachinesDevicesAreListedAsR2025bListsThem()
    {
        IReadOnlyList<AudioDeviceInfo> devices = WasapiAudio.Instance.Devices();
        Assert.Equal(Enumerable.Range(0, devices.Count), devices.Select(static d => d.Id));
        Assert.All(devices, static d => Assert.EndsWith(" (Windows DirectSound)", d.Name, StringComparison.Ordinal));

        // Inputs first, then outputs, each led by its primary driver, which follows the default device.
        int firstOutput = devices.TakeWhile(static d => d.Input).Count();
        Assert.All(devices.Skip(firstOutput), static d => Assert.False(d.Input));
        if (firstOutput > 0)
        {
            Assert.Equal("Primary Sound Capture Driver (Windows DirectSound)", devices[0].Name);
            Assert.Null(devices[0].EndpointId);
        }

        if (firstOutput < devices.Count)
        {
            Assert.Equal("Primary Sound Driver (Windows DirectSound)", devices[firstOutput].Name);
            Assert.Null(devices[firstOutput].EndpointId);
        }
    }

    [Fact]
    public void ZerosPlayOnThePrimaryOutputAndFinish()
    {
        AudioDeviceInfo? output = WasapiAudio.Instance.Devices().FirstOrDefault(static d => !d.Input);
        if (output is null)
        {
            return; // a machine with no audio output
        }

        using IAudioOutputStream stream = WasapiAudio.Instance.OpenOutput(output, 8000, 2);
        using var done = new ManualResetEventSlim();
        var clock = Stopwatch.StartNew();
        stream.Start(new float[800 * 2], 800, done.Set); // 0.1 s of silence
        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(800, stream.FramesPlayed);
        Assert.InRange(clock.Elapsed.TotalSeconds, 0.05, 5);
    }

    [Fact]
    public void TheSimulatedOutputPlaysInRealTimeAndKeepsWhatItPlayed()
    {
        var audio = new SimulatedAudio();
        AudioDeviceInfo output = audio.Devices().First(static d => !d.Input);
        using IAudioOutputStream stream = audio.OpenOutput(output, 8000, 1);
        using var done = new ManualResetEventSlim();
        var clock = Stopwatch.StartNew();
        stream.Start([0.5f, -0.5f, .. new float[798]], 800, done.Set);
        Assert.True(stream.Running);
        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        Assert.InRange(clock.Elapsed.TotalSeconds, 0.08, 5);
        Assert.Equal(800, stream.FramesPlayed);
        Assert.Equal(0.5f, audio.LastPlayed.Frames[0]);
        Assert.Equal(-0.5f, audio.LastPlayed.Frames[1]);
    }

    [Fact]
    public void TheSimulatedMicrophoneGivesItsSineFromSampleZero()
    {
        var audio = new SimulatedAudio();
        AudioDeviceInfo input = audio.Devices().First(static d => d.Input);
        using IAudioInputStream stream = audio.OpenInput(input, 8000, 1);
        var samples = new List<float>();
        stream.Start((block, frames) =>
        {
            lock (samples)
            {
                samples.AddRange(block.Take(frames));
            }
        }, static _ => { });
        Thread.Sleep(200);
        stream.Stop();
        lock (samples)
        {
            Assert.InRange(samples.Count, 800, 4000);
            Assert.Equal(0f, samples[0]);
            Assert.Equal(0.5f, samples[150], 5); // 440 Hz at 8000 Hz peaks at sample 150
        }
    }
}
