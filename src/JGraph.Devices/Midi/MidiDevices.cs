using System.Diagnostics;
using System.Runtime.Versioning;

namespace JGraph.Devices.Midi;

/// <summary>
/// A MIDI device as mididevinfo lists it: its ID (its place in the list), its name, the interface that
/// reaches it, whether it is an input, and the ID its own interface knows it by (WinMM's, where the
/// MIDI Mapper is 0xFFFFFFFF).
/// </summary>
public sealed record MidiDeviceInfo(int Id, string Name, string Interface, bool Input, uint NativeId);

/// <summary>
/// One message as it came off the wire: a channel or system message's bytes, or a whole system
/// exclusive message from F0 to F7; and when it came, in seconds on <see cref="MidiClock"/>.
/// </summary>
public readonly record struct MidiEvent(byte[] Bytes, double Timestamp);

/// <summary>An open MIDI input: what has arrived and not been taken, oldest first.</summary>
public interface IMidiInput : IDisposable
{
    /// <summary>How many messages are waiting.</summary>
    int Waiting { get; }

    /// <summary>Takes up to <paramref name="max"/> waiting messages, oldest first.</summary>
    IReadOnlyList<MidiEvent> Take(int max);

    /// <summary>Raised on a driver thread after a message arrives.</summary>
    event Action<MidiEvent>? Arrived;
}

/// <summary>An open MIDI output.</summary>
public interface IMidiOutput : IDisposable
{
    /// <summary>
    /// Sends <paramref name="messages"/>, each after its delay in seconds from now; returns at once.
    /// A message's bytes are one channel or system message, or one whole system exclusive message.
    /// </summary>
    void Send(IReadOnlyList<(byte[] Bytes, double Delay)> messages);

    /// <summary>Waits until what was sent has gone out, or the timeout passes.</summary>
    bool WaitSent(TimeSpan timeout);
}

/// <summary>What mididevinfo, mididevice and midicontrols stand on: the machine's MIDI devices, or a simulation.</summary>
public interface IMidiBackend
{
    /// <summary>Every device, numbered from 0 in PortMidi's order.</summary>
    IReadOnlyList<MidiDeviceInfo> Devices();

    /// <summary>The input a midicontrols object opens when it is not told one, or -1 for none.</summary>
    int DefaultInput();

    IMidiInput OpenInput(MidiDeviceInfo device);

    IMidiOutput OpenOutput(MidiDeviceInfo device);
}

/// <summary>
/// The clock received messages are stamped with: seconds since the MIDI subsystem started, which is
/// the first time anything in this process asked it the time (PortMidi's Pt_Time starts at Pm_Initialize).
/// </summary>
public static class MidiClock
{
    private static readonly Stopwatch Started = Stopwatch.StartNew();

    public static double Now => Started.Elapsed.TotalSeconds;
}

/// <summary>
/// Sends an output's messages at their times, on a thread of its own, so midisend returns at once as
/// R2025b's does. Each message goes out through <c>deliver</c> in time order; ties keep the order they
/// were given in. While a message is pending, the system timer runs at 1 ms, so a note is late by
/// about a millisecond rather than a 15.6 ms tick.
/// </summary>
public sealed class MidiScheduler : IDisposable
{
    private readonly object _gate = new();
    private readonly PriorityQueue<byte[], (double Due, long Order)> _pending = new();
    private readonly Action<byte[]> _deliver;
    private readonly Thread _thread;
    private long _order;
    private bool _closed;
    private bool _delivering;

    public MidiScheduler(Action<byte[]> deliver, string name)
    {
        _deliver = deliver;
        _thread = new Thread(Run) { IsBackground = true, Name = name, Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    /// <summary>A delivery that threw, kept for the next <see cref="Schedule"/> to report.</summary>
    public Exception? Fault { get; private set; }

    /// <summary>Messages scheduled and not yet delivered.</summary>
    public int Pending
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count;
            }
        }
    }

    public void Schedule(IReadOnlyList<(byte[] Bytes, double Delay)> messages)
    {
        double now = MidiClock.Now;
        lock (_gate)
        {
            if (Fault is { } fault)
            {
                Fault = null;
                throw new DeviceIOException(fault.Message);
            }

            foreach ((byte[] bytes, double delay) in messages)
            {
                _pending.Enqueue(bytes, (now + Math.Max(0, delay), _order++));
            }

            Monitor.PulseAll(_gate);
        }
    }

    /// <summary>Waits until every message scheduled has been delivered, or the timeout passes.</summary>
    public bool WaitIdle(TimeSpan timeout)
    {
        DateTime until = DateTime.UtcNow + timeout;
        lock (_gate)
        {
            while (_pending.Count > 0 || _delivering)
            {
                TimeSpan left = until - DateTime.UtcNow;
                if (left <= TimeSpan.Zero || _closed)
                {
                    return false;
                }

                Monitor.Wait(_gate, left);
            }

            return true;
        }
    }

    /// <summary>Drops every message not yet sent.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _pending.Clear();
            Monitor.PulseAll(_gate);
        }
    }

    private void Run()
    {
        bool fine = false;
        try
        {
            while (true)
            {
                byte[] next;
                lock (_gate)
                {
                    while (!_closed && _pending.Count == 0)
                    {
                        Fine(false);
                        Monitor.Wait(_gate);
                    }

                    if (_closed)
                    {
                        return;
                    }

                    Fine(true);
                    _pending.TryPeek(out _, out (double Due, long Order) when);
                    double wait = when.Due - MidiClock.Now;
                    if (wait > 0.002)
                    {
                        Monitor.Wait(_gate, TimeSpan.FromSeconds(wait - 0.0015));
                        continue;
                    }

                    if (wait > 0)
                    {
                        Monitor.Exit(_gate);
                        try
                        {
                            SpinWait.SpinUntil(() => MidiClock.Now >= when.Due);
                        }
                        finally
                        {
                            Monitor.Enter(_gate);
                        }

                        continue; // the queue may have changed while the lock was let go
                    }

                    next = _pending.Dequeue();
                    _delivering = true;
                }

                try
                {
                    _deliver(next);
                }
                catch (Exception e) when (e is DeviceIOException or InvalidOperationException)
                {
                    lock (_gate)
                    {
                        Fault = e;
                    }
                }
                finally
                {
                    lock (_gate)
                    {
                        _delivering = false;
                        Monitor.PulseAll(_gate);
                    }
                }
            }
        }
        finally
        {
            Fine(false);
        }

        void Fine(bool on)
        {
            if (on == fine || !OperatingSystem.IsWindows())
            {
                return;
            }

            fine = on;
            if (on)
            {
                MidiNative.timeBeginPeriod(1);
            }
            else
            {
                MidiNative.timeEndPeriod(1);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _closed = true;
            _pending.Clear();
            Monitor.PulseAll(_gate);
        }

        if (Thread.CurrentThread != _thread)
        {
            _thread.Join(TimeSpan.FromSeconds(2));
        }
    }
}

/// <summary>How much an input keeps that no one has taken.</summary>
public static class MidiLimits
{
    /// <summary>Messages an input keeps; past this the oldest go.</summary>
    public const int Queue = 65536;
}

/// <summary>What the midimsg layer needs of MIDI wire messages: their lengths by status byte.</summary>
public static class MidiWire
{
    /// <summary>The length of the message a status byte starts, as midimsg.m's msgnbytes counts it.</summary>
    public static int LengthOf(byte status) => status switch
    {
        <= 0xBF => 3,
        <= 0xDF => 2,
        <= 0xEF => 3,
        0xF0 => 1,
        0xF1 => 2,
        0xF2 => 3,
        0xF3 => 2,
        _ => 1,
    };

    /// <summary>
    /// A short message packed as WinMM's midiOutShortMsg takes it, status byte lowest. Running status
    /// is not used: every message carries its own status byte.
    /// </summary>
    public static uint Pack(ReadOnlySpan<byte> bytes)
    {
        uint packed = 0;
        for (int i = 0; i < Math.Min(3, bytes.Length); i++)
        {
            packed |= (uint)bytes[i] << (8 * i);
        }

        return packed;
    }
}

/// <summary>The machine's MIDI devices through WinMM (device classes plan, stage D10b, ADR 0194).</summary>
/// <remarks>
/// R2025b's midicontrols.devices is PortMidi over WinMM ("MMSystem"), which lists the MIDI Mapper as an
/// input when WinMM has one, then the inputs, then the MIDI Mapper as an output, then the outputs, and
/// numbers them from 0 in that order (probe_midi_env: "Microsoft MIDI Mapper" 0, "Microsoft GS
/// Wavetable Synth" 1). The list is read afresh each time, as a device may come and go.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WinMmMidi : IMidiBackend
{
    public static WinMmMidi Instance { get; } = new();

    private WinMmMidi()
    {
    }

    public IReadOnlyList<MidiDeviceInfo> Devices()
    {
        var found = new List<MidiDeviceInfo>();
        void Add(string? name, bool input, uint native)
        {
            if (name is not null)
            {
                found.Add(new MidiDeviceInfo(found.Count, name, "MMSystem", input, native));
            }
        }

        Add(MidiNative.InputName(MidiNative.MidiMapper), true, MidiNative.MidiMapper);
        uint inputs = MidiNative.midiInGetNumDevs();
        for (uint i = 0; i < inputs; i++)
        {
            Add(MidiNative.InputName(i), true, i);
        }

        Add(MidiNative.OutputName(MidiNative.MidiMapper), false, MidiNative.MidiMapper);
        uint outputs = MidiNative.midiOutGetNumDevs();
        for (uint i = 0; i < outputs; i++)
        {
            Add(MidiNative.OutputName(i), false, i);
        }

        return found;
    }

    /// <summary>PortMidi's default input on Windows: the first input, when there is one.</summary>
    public int DefaultInput() => Devices().FirstOrDefault(static d => d.Input)?.Id ?? -1;

    public IMidiInput OpenInput(MidiDeviceInfo device) => new WinMmMidiInput(device);

    public IMidiOutput OpenOutput(MidiDeviceInfo device) => new WinMmMidiOutput(device);
}
