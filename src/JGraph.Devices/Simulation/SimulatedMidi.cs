using JGraph.Devices.Midi;

namespace JGraph.Devices.Simulation;

/// <summary>
/// Simulated MIDI devices for tests and fixtures (device classes plan, stage D10b, ADR 0194): an input
/// and an output both named "JGraph Loopback", where what is sent to the output arrives at the input
/// when it is due, and an output "JGraph Sink" that keeps what it is sent. Nothing is heard.
/// </summary>
public sealed class SimulatedMidi : IMidiBackend
{
    public const string LoopbackName = "JGraph Loopback";
    public const string SinkName = "JGraph Sink";

    private readonly object _gate = new();
    private readonly List<SimulatedInput> _inputs = [];
    private readonly List<(byte[] Bytes, double Time)> _sunk = [];

    private static readonly MidiDeviceInfo[] List =
    [
        new(0, LoopbackName, "Simulated", true, 0),
        new(1, LoopbackName, "Simulated", false, 0),
        new(2, SinkName, "Simulated", false, 1),
    ];

    public IReadOnlyList<MidiDeviceInfo> Devices() => List;

    public int DefaultInput() => 0;

    /// <summary>What the sink and the loopback output have been sent, in the order they sent it, with when.</summary>
    public IReadOnlyList<(byte[] Bytes, double Time)> Sent
    {
        get
        {
            lock (_gate)
            {
                return [.. _sunk];
            }
        }
    }

    public IMidiInput OpenInput(MidiDeviceInfo device)
    {
        var input = new SimulatedInput(this);
        lock (_gate)
        {
            _inputs.Add(input);
        }

        return input;
    }

    public IMidiOutput OpenOutput(MidiDeviceInfo device) => new SimulatedOutput(this, device.NativeId == 0);

    private void Deliver(byte[] bytes, bool loop)
    {
        double now = MidiClock.Now;
        SimulatedInput[] listening;
        lock (_gate)
        {
            _sunk.Add((bytes, now));
            listening = loop ? [.. _inputs] : [];
        }

        foreach (SimulatedInput input in listening)
        {
            input.Receive(new MidiEvent(bytes, now));
        }
    }

    private void Closed(SimulatedInput input)
    {
        lock (_gate)
        {
            _inputs.Remove(input);
        }
    }

    private sealed class SimulatedInput(SimulatedMidi owner) : IMidiInput
    {
        private readonly Queue<MidiEvent> _received = new();

        public event Action<MidiEvent>? Arrived;

        public int Waiting
        {
            get
            {
                lock (_received)
                {
                    return _received.Count;
                }
            }
        }

        public IReadOnlyList<MidiEvent> Take(int max)
        {
            lock (_received)
            {
                var taken = new List<MidiEvent>(Math.Min(max, _received.Count));
                while (taken.Count < max && _received.TryDequeue(out MidiEvent e))
                {
                    taken.Add(e);
                }

                return taken;
            }
        }

        public void Receive(MidiEvent e)
        {
            lock (_received)
            {
                if (_received.Count >= MidiLimits.Queue)
                {
                    _received.Dequeue();
                }

                _received.Enqueue(e);
            }

            Arrived?.Invoke(e);
        }

        public void Dispose() => owner.Closed(this);
    }

    private sealed class SimulatedOutput : IMidiOutput
    {
        private readonly MidiScheduler _scheduler;

        public SimulatedOutput(SimulatedMidi owner, bool loop) =>
            _scheduler = new MidiScheduler(bytes => owner.Deliver(bytes, loop), "MIDI out: simulated");

        public void Send(IReadOnlyList<(byte[] Bytes, double Delay)> messages) => _scheduler.Schedule(messages);

        public bool WaitSent(TimeSpan timeout) => _scheduler.WaitIdle(timeout);

        public void Dispose() => _scheduler.Dispose();
    }
}
