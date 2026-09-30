using JGraph.Devices.Audio;

namespace JGraph.Devices.Simulation;

/// <summary>
/// Audio devices for the tests and <c>jgraph.internal.audiosim</c> (device classes plan, stage D10, ADR
/// 0193), so nothing records from a microphone and nothing is heard:
/// <list type="bullet">
/// <item>two inputs: a primary driver and a microphone, each giving a 440 Hz sine of amplitude 0.5 in
/// real time, 10 ms at a time;</item>
/// <item>two outputs that take frames at the rate asked and keep the last block played.</item>
/// </list>
/// The names follow the machine's pattern, so audiodevinfo's rules hold for them.
/// </summary>
public sealed class SimulatedAudio : IAudioBackend
{
    private readonly AudioDeviceInfo[] _devices =
    [
        new(0, $"Primary Sound Capture Driver ({WasapiAudioName})", true, 2, null),
        new(1, $"Simulated Microphone ({WasapiAudioName})", true, 2, "sim-in"),
        new(2, $"Primary Sound Driver ({WasapiAudioName})", false, 2, null),
        new(3, $"Simulated Speakers ({WasapiAudioName})", false, 2, "sim-out"),
    ];

    private const string WasapiAudioName = "Windows DirectSound";

    public IReadOnlyList<AudioDeviceInfo> Devices() => _devices;

    public void Reset()
    {
    }

    /// <summary>The frames the last output stream played, interleaved, and its channel count.</summary>
    public (float[] Frames, int Channels) LastPlayed { get; private set; } = ([], 1);

    public IAudioOutputStream OpenOutput(AudioDeviceInfo device, int sampleRate, int channels) => new Output(this, sampleRate, channels);

    public IAudioInputStream OpenInput(AudioDeviceInfo device, int sampleRate, int channels) => new Input(sampleRate, channels);

    private sealed class Output(SimulatedAudio owner, int rate, int channels) : IAudioOutputStream
    {
        private Timer? _timer;
        private long _played;
        private DateTime _started;
        private long _frames;
        private Action? _done;
        private volatile bool _running;

        public long FramesPlayed => Interlocked.Read(ref _played);

        public bool Running => _running;

        public void Start(float[] interleaved, int frames, Action done)
        {
            Stop();
            owner.LastPlayed = (interleaved[..(frames * channels)], channels);
            _frames = frames;
            _done = done;
            _played = 0;
            _started = DateTime.UtcNow;
            _running = true;
            _timer = new Timer(_ => Tick(), null, 10, 10);
        }

        private void Tick()
        {
            if (!_running)
            {
                return;
            }

            long now = Math.Min(_frames, (long)((DateTime.UtcNow - _started).TotalSeconds * rate));
            Interlocked.Exchange(ref _played, now);
            if (now >= _frames)
            {
                _running = false;
                _timer?.Dispose();
                _done?.Invoke();
            }
        }

        public void Stop()
        {
            _running = false;
            _timer?.Dispose();
            _timer = null;
        }

        public void Dispose() => Stop();
    }

    private sealed class Input(int rate, int channels) : IAudioInputStream
    {
        private Timer? _timer;
        private long _sent;
        private DateTime _started;
        private Action<float[], int>? _deliver;
        private volatile bool _running;
        private readonly object _gate = new();

        public bool Running => _running;

        public void Start(Action<float[], int> frames, Action<Exception> failed)
        {
            Stop();
            _deliver = frames;
            _started = DateTime.UtcNow;
            _running = true;
            _timer = new Timer(_ => Tick(), null, 10, 10);
        }

        private void Tick()
        {
            lock (_gate)
            {
                if (!_running)
                {
                    return;
                }

                long due = (long)((DateTime.UtcNow - _started).TotalSeconds * rate);
                int count = (int)(due - _sent);
                if (count <= 0)
                {
                    return;
                }

                float[] block = new float[count * channels];
                for (int i = 0; i < count; i++)
                {
                    float sample = (float)(0.5 * Math.Sin(2 * Math.PI * 440 * (_sent + i) / rate));
                    for (int c = 0; c < channels; c++)
                    {
                        block[(i * channels) + c] = sample;
                    }
                }

                _sent += count;
                _deliver?.Invoke(block, count);
            }
        }

        public void Stop()
        {
            lock (_gate)
            {
                _running = false;
                _timer?.Dispose();
                _timer = null;
            }
        }

        public void Dispose() => Stop();
    }
}
