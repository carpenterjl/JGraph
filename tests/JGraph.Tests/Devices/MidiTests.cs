using System.Diagnostics;
using System.Runtime.Versioning;
using JGraph.Devices.Midi;
using JGraph.Devices.Simulation;
using JGraph.Scripting.Jgs.Devices;
using Xunit;

namespace JGraph.Tests.Devices;

/// <summary>
/// MIDI devices (device classes plan, stage D10b, ADR 0194): the machine's list in PortMidi's order,
/// the output scheduler's timing and order, the simulated loopback, how a system exclusive message
/// goes on the wire and comes back as midimsgs, and a device shared by two holders. No test sends to
/// a real output.
/// </summary>
[SupportedOSPlatform("windows")]
public class MidiTests
{
    [Fact]
    public void TheMachinesDevicesAreListedInPortMidisOrder()
    {
        IReadOnlyList<MidiDeviceInfo> devices = WinMmMidi.Instance.Devices();
        Assert.Equal(Enumerable.Range(0, devices.Count), devices.Select(static d => d.Id));
        Assert.All(devices, static d => Assert.Equal("MMSystem", d.Interface));

        // Inputs first, then outputs, the MIDI Mapper leading the outputs (probe_midi_env).
        int firstOutput = devices.TakeWhile(static d => d.Input).Count();
        Assert.All(devices.Skip(firstOutput), static d => Assert.False(d.Input));
        if (firstOutput < devices.Count)
        {
            Assert.Equal("Microsoft MIDI Mapper", devices[firstOutput].Name);
            Assert.Equal(0xFFFFFFFFu, devices[firstOutput].NativeId);
        }
    }

    [Fact]
    public void TheSchedulerSendsInTimeOrderEachAtItsDelay()
    {
        var delivered = new List<(byte[] Bytes, double At)>();
        double start = MidiClock.Now;
        using var scheduler = new MidiScheduler(bytes =>
        {
            lock (delivered)
            {
                delivered.Add((bytes, MidiClock.Now - start));
            }
        }, "test");

        var clock = Stopwatch.StartNew();
        scheduler.Schedule([([3], 0.2), ([1], 0), ([2], 0.1), ([4], 0.2)]);
        Assert.True(clock.Elapsed.TotalSeconds < 0.05, "Schedule returns at once");
        Assert.True(scheduler.WaitIdle(TimeSpan.FromSeconds(5)));
        lock (delivered)
        {
            Assert.Equal([1, 2, 3, 4], delivered.Select(static d => (int)d.Bytes[0]));
            Assert.InRange(delivered[1].At, 0.09, 0.16);
            Assert.InRange(delivered[2].At, 0.19, 0.26);
        }
    }

    [Fact]
    public void TheLoopbackDeliversWhatItsOutputSendsAndTheSinkKeepsIt()
    {
        var midi = new SimulatedMidi();
        IReadOnlyList<MidiDeviceInfo> devices = midi.Devices();
        using IMidiInput input = midi.OpenInput(devices[0]);
        using IMidiOutput loop = midi.OpenOutput(devices[1]);
        using IMidiOutput sink = midi.OpenOutput(devices[2]);
        loop.Send([([0x90, 60, 64], 0)]);
        sink.Send([([0xFC], 0)]);
        Assert.True(loop.WaitSent(TimeSpan.FromSeconds(5)));
        Assert.True(sink.WaitSent(TimeSpan.FromSeconds(5)));
        IReadOnlyList<MidiEvent> got = input.Take(10);
        Assert.Single(got);
        Assert.Equal([0x90, 60, 64], got[0].Bytes);
        Assert.Equal(2, midi.Sent.Count);
        Assert.Equal(0, input.Waiting);
    }

    [Fact]
    public void ASystemExclusiveMessageIsOneWireMessageAndComesBackAsMidimsgs()
    {
        MidiMsgItem[] sent =
        [
            new(0xF0, 0.5),
            MidiMsgItem.Of([1, 2, 3, 4, 5, 6, 7, 8], 0.5),
            MidiMsgItem.Of([9, 10, 0xF4], 0.5),
            new(0xF7, 0.5),
            MidiMsgItem.Of([0x90, 60, 64], 1),
        ];
        List<(byte[] Bytes, double Delay)> wire = MidiShared.ToWire(sent);
        Assert.Equal(2, wire.Count);
        Assert.Equal([0xF0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 0xF7], wire[0].Bytes);
        Assert.Equal(0.5, wire[0].Delay);
        Assert.Equal([0x90, 60, 64], wire[1].Bytes);

        MidiMsgItem[] back = MidiShared.ToMessages(new MidiEvent(wire[0].Bytes, 2)).ToArray();
        Assert.Equal(["SystemExclusive", "Data", "Data", "EOX"], back.Select(MidiMsgs.TypeName));
        Assert.Equal([9, 10], MidiMsgs.MsgBytes(back[2]));
        Assert.All(back, static m => Assert.Equal(2, m.Timestamp));
    }

    [Fact]
    public void TwoHoldersShareOneOpenOutput()
    {
        var midi = new SimulatedMidi();
        MidiDeviceInfo sinkInfo = midi.Devices()[2];
        IMidiOutput first = MidiShared.OpenOutput(midi, sinkInfo);
        IMidiOutput second = MidiShared.OpenOutput(midi, sinkInfo);
        first.Dispose();
        second.Send([([0xFA], 0)]); // the port stays open for the second holder
        Assert.True(second.WaitSent(TimeSpan.FromSeconds(5)));
        second.Dispose();
        Assert.Single(midi.Sent);
    }

    [Fact]
    public void MidicontrolsScalesAsR2025bDoes()
    {
        // probe_midi_scale: raw r reads r/126 up to 63 and (r-1)/126 above; x goes to round(x*126), one more above 63.
        Assert.Equal(0.5, MidicontrolsObject.FromRaw(63));
        Assert.Equal(0.5, MidicontrolsObject.FromRaw(64));
        Assert.Equal(1, MidicontrolsObject.FromRaw(127));
        Assert.Equal(32.0 / 126, MidicontrolsObject.FromRaw(32));
        Assert.Equal(63, MidicontrolsObject.ToRaw(0.5));
        Assert.Equal(65, MidicontrolsObject.ToRaw(0.51));
        Assert.Equal(127, MidicontrolsObject.ToRaw(1));
        Assert.Equal(3, MidicontrolsObject.ToRaw(0.02));
    }
}
