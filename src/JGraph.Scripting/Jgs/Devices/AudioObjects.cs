using System.Runtime.Versioning;
using JGraph.Devices;
using JGraph.Devices.Audio;

namespace JGraph.Scripting.Jgs.Devices;

/// <summary>
/// What audioplayer and audiorecorder share (device classes plan, stage D10, ADR 0193): R2025b's
/// internal.Callback rules, the backend a session uses, and the refusals both word alike.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class AudioShared
{
    /// <summary>The session's audio: jgraph.internal.audiosim's devices, or the machine's.</summary>
    public static IAudioBackend Backend(DeviceSession session) => session.AudioSimulation ?? (IAudioBackend)WasapiAudio.Instance;

    /// <summary>internal.Callback.validate: empty, a function handle, text, or a cell led by a handle or text.</summary>
    public static bool ValidCallback(JgsValue value) =>
        IsEmpty(value) || value.Type == JgsType.Function || JgsBuiltins.IsTextScalar(value)
        || (value.Type == JgsType.Cell && value.AsCell.Length > 0 && (value.AsCell[0].Type == JgsType.Function || JgsBuiltins.IsTextScalar(value.AsCell[0])));

    public static bool IsEmpty(JgsValue value) => value.Type == JgsType.Null || DeviceChecks.Count(value) == 0 && value.Type is JgsType.Array or JgsType.Cell;

    /// <summary>
    /// internal.Callback.execute: a handle is called as fcn(obj, []), text runs in the base workspace, and
    /// {fcn, a, b} is called as fcn(obj, [], a, b). R2025b hands its internal implementation object as
    /// obj; JGraph hands the audioplayer or audiorecorder itself (div=ADR0193).
    /// </summary>
    public static void Run(DeviceObject owner, JgsValue callback)
    {
        if (IsEmpty(callback))
        {
            return;
        }

        Interpreter interpreter = owner.Interpreter;
        JgsValue self = JgsValue.External(owner);
        if (callback.Type == JgsType.Function)
        {
            JgsCallbacks.Invoke(callback.AsCallable, [self, JgsEmpty.Zero()], 0, 0);
            return;
        }

        if (JgsBuiltins.IsTextScalar(callback))
        {
            interpreter.EvaluateSource(JgsBuiltins.TextOf(callback), interpreter.Globals, 0, 0, asStatement: true);
            return;
        }

        JgsValue[] parts = callback.AsCell;
        JgsValue head = parts[0].Type == JgsType.Function ? parts[0] : interpreter.EvaluateSource("@" + JgsBuiltins.TextOf(parts[0]), interpreter.Globals, 0, 0);
        var arguments = new JgsValue[parts.Length + 1];
        arguments[0] = self;
        arguments[1] = JgsEmpty.Zero();
        Array.Copy(parts, 1, arguments, 2, parts.Length - 1);
        JgsCallbacks.Invoke(head.AsCallable, arguments, 0, 0);
    }

    /// <summary>A callback run from a device thread's event, where there is no caller to throw to: its failure is reported.</summary>
    public static void RunReported(DeviceObject owner, JgsValue callback, string which)
    {
        try
        {
            Run(owner, callback);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JgsException failure)
        {
            owner.Session.Host.WriteErr($"Error while evaluating {which} for {owner.Class.Name}\n\n{failure.Message}");
        }
    }

    /// <summary>validateattributes(value, {'numeric'}, {'positive', 'scalar'}) and the one-millisecond floor, for TimerPeriod.</summary>
    public static double TimerPeriod(JgsValue value, string owner, DeviceCall call)
    {
        var who = new DeviceChecks.Subject(null, null);
        DeviceChecks.Classes(value, ["numeric"], who, call.Line, call.Column);
        DeviceChecks.Attributes(value, ["positive", "scalar"], who, call.Line, call.Column);
        double period = DeviceChecks.Numbers(value).First();
        return period < 0.001
            ? throw call.Error($"MATLAB:audiovideo:{owner}:invalidtimerperiod", "TimerPeriod must be >= 0.001 (one millisecond).")
            : period;
    }

    /// <summary>A scalar numeric value, or null.</summary>
    public static double? Scalar(JgsValue value) =>
        DeviceChecks.NumericClasses.Contains(DeviceChecks.ClassOf(value)) && DeviceChecks.Count(value) == 1 ? DeviceChecks.Numbers(value).First() : null;

    public static bool IsNumeric(JgsValue value) => DeviceChecks.NumericClasses.Contains(DeviceChecks.ClassOf(value));

    /// <summary>R2025b's refusal of <c>a &amp;&amp; b</c> on an array: the recorder's setters test vectors that way.</summary>
    public static JgsRuntimeException NonLogicalConditional(DeviceCall call) => call.Error("MATLAB:nonLogicalConditional",
        "Operands to the logical AND (&&) and OR (||) operators must be convertible to logical scalar values. Use the ANY or ALL functions to reduce operands to logical scalar values.");

    /// <summary>The device a DeviceID stands for: -1 is the primary driver, the first of its direction.</summary>
    public static AudioDeviceInfo? Device(IAudioBackend backend, int id, bool input) =>
        id == -1
            ? backend.Devices().FirstOrDefault(d => d.Input == input)
            : backend.Devices().FirstOrDefault(d => d.Id == id && d.Input == input);

    /// <summary>Samples, column-major as a matrix holds them, normalized to [-1, 1] from their class's range.</summary>
    public static double Normalize(double value, string numericClass) => numericClass switch
    {
        "double" or "single" => Math.Clamp(value, -1, 1),
        "int16" => value / 32768.0,
        "uint8" => (value - 128) / 128.0,
        "int8" => value / 128.0,
        "int32" => value / 2147483648.0,
        "int64" => value / 9223372036854775808.0,
        "uint16" => (value - 32768) / 32768.0,
        "uint32" => (value - 2147483648.0) / 2147483648.0,
        "uint64" => (value - 9223372036854775808.0) / 9223372036854775808.0,
        _ => 0,
    };
}

/// <summary>
/// <c>p = audioplayer(y, fs, nbits, id)</c> and <c>audioplayer(r, id)</c> (device classes plan, stage D10,
/// ADR 0193), transcribed from R2025b's audioplayer.m, audiovideo.internal.Iaudioplayer and
/// audioplayerDesktop: the properties with their setters' checks and sentences, play, playblocking,
/// pause, resume, stop and isplaying, CurrentSample counted as the desktop player counts it (buffers of
/// 25 ms, the last one padded), and StartFcn, StopFcn and TimerFcn run as internal.Callback runs them.
/// Names are case-blind and may be shortened, as the classdef's attributes allow.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class AudioPlayerObject : DeviceObject
{
    private const double DesiredLatency = 0.025;

    private static readonly DeviceClass Declaration = Declare();

    private readonly DeviceEventQueue _queue;
    private float[] _data = [];
    private int _total;
    private int _channels = 1;
    private string _dataType = "double";
    private JgsValue _sampleRate = JgsValue.Number(8000);
    private int _bits = 16;
    private int _deviceId = -1;
    private JgsValue _startFcn = JgsEmpty.Zero();
    private JgsValue _stopFcn = JgsEmpty.Zero();
    private JgsValue _timerFcn = JgsEmpty.Zero();
    private double _timerPeriod = 0.05;
    private JgsValue _tag = JgsValue.Str("");
    private JgsValue _userData = JgsEmpty.Zero();

    private IAudioOutputStream? _stream;
    private bool _open;
    private long _startIndex = 1;
    private long _endIndex;
    private long _sentBefore;
    private Timer? _timer;
    private int _generation;

    private AudioPlayerObject(DeviceSession session, Interpreter interpreter)
        : base(session, interpreter)
    {
        _queue = DeviceEventQueue.ForCurrentThread();
    }

    public override DeviceClass Class => Declaration;

    private double SampleRate => DeviceChecks.Numbers(_sampleRate).First();

    private long BufferSize => Math.Max(1, (long)Math.Round(DesiredLatency * SampleRate));

    /// <summary>Samples queued for this playback and not yet played: the channel's DataToSend.</summary>
    private long DataToSend => Math.Max(0, (_endIndex - _startIndex + 1) - Sent);

    private long Sent => _sentBefore + (_stream is { } s && _open ? s.FramesPlayed : 0);

    private bool IsPlaying => _open;

    public override string? Summary() => Deleted
        ? "deleted"
        : $"{_total} samples, {DeviceObject.Shown(_sampleRate)} Hz, {(IsPlaying ? "playing" : "stopped")}";

    /// <summary>Whether the player is playing: what sound checks before it lets a player go.</summary>
    internal bool Playing => _open;

    // --- construction ----------------------------------------------------------------------------------

    /// <summary><c>audioplayer(varargin)</c>, as audioplayerDesktop's constructor takes its arguments.</summary>
    public static JgsValue Create(DeviceSession session, Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col, bool counted = true)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (args.Count > 4)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        var player = new AudioPlayerObject(session, interpreter);
        var call = new DeviceCall { Target = player, Args = args, Line = line, Column = col };
        JgsValue signal;
        if (args[0].AsExternalOrNull() is AudioRecorderObject recorder)
        {
            if (args.Count > 2)
            {
                throw call.Error("MATLAB:audiovideo:audioplayer:numericinputs", "When creating an audioplayer object from an audio signal, all input arguments must be numeric.");
            }

            player.SetBits(JgsValue.Number(recorder.Bits), call);
            signal = recorder.AudioData(recorder.Bits switch { 8 => "uint8", 16 => "int16", _ => "double" }, call);
            player.SetSampleRate(recorder.SampleRateValue, call);
            if (args.Count == 2)
            {
                if (!AudioShared.IsNumeric(args[1]))
                {
                    throw call.Error("MATLAB:audiovideo:audioplayer:invaliddeviceID", "Could not find the specified device");
                }

                player.SetDeviceId(args[1], call);
            }
        }
        else
        {
            if (args.Count == 1)
            {
                throw call.Error("MATLAB:audiovideo:audioplayer:mustbeaudiorecorder", "Input must be an audiorecorder object.");
            }

            if (!args.All(AudioShared.IsNumeric))
            {
                throw call.Error("MATLAB:audiovideo:audioplayer:numericinputs", "When creating an audioplayer object from an audio signal, all input arguments must be numeric.");
            }

            signal = args[0];
            player.SetSampleRate(args[1], call);
            if (args.Count >= 3)
            {
                player.SetBits(args[2], call);
            }
            else
            {
                player._bits = DeviceChecks.ClassOf(signal) switch
                {
                    "double" or "single" or "int16" => 16,
                    "int8" or "uint8" => 8,
                    _ => throw call.Error("MATLAB:audiovideo:audioplayer:unsupportedtype", "Unsupported data type."),
                };
            }

            if (args.Count == 4)
            {
                player.SetDeviceId(args[3], call);
            }
        }

        player.SetAudioData(signal, call);
        player.Initialize(call);
        session.Remember(player);
        JgsValue made = JgsValue.External(player);
        if (counted)
        {
            // sound's players are held by the session until they have played, not by a variable.
            JgsLifetime.Minted(made);
        }

        return made;
    }

    /// <summary>Iaudioplayer's set.AudioData: numeric and nonempty, a row turned into a column, int8 kept as uint8.</summary>
    private void SetAudioData(JgsValue value, DeviceCall call)
    {
        if (!AudioShared.IsNumeric(value))
        {
            throw call.Error("MATLAB:audiovideo:audioplayer:invalidsignal", "Audio data must be numeric.");
        }

        if (DeviceChecks.Count(value) == 0)
        {
            throw call.Error("MATLAB:audiovideo:audioplayer:nonemptysignal", "Y must be specified as a non-empty numeric input.");
        }

        if (value.Type == JgsType.Sparse)
        {
            JGraph.Numerics.Sparse.CscMatrix sparse = value.AsSparse;
            value = JgsMatrix.FromColumnMajorDims(sparse.ToColumnMajor(), [sparse.Rows, sparse.Cols]);
        }

        int[] dims = value.Type == JgsType.Array ? value.Dims : [1, 1];
        int rows = dims[0];
        int cols = dims.Skip(1).Aggregate(1, static (a, b) => a * b);
        double[] numbers = DeviceChecks.Numbers(value).ToArray();
        string numericClass = DeviceChecks.ClassOf(value);
        int frames = rows;
        int channels = dims.Length > 1 ? dims[1] : 1;
        bool transpose = cols > rows;
        if (transpose)
        {
            frames = cols;
            channels = rows;
        }

        // A value with more than two dimensions keeps its first two: size(AudioData, 2) channels.
        _total = frames;
        _channels = channels;
        _dataType = numericClass == "int8" ? "uint8" : numericClass;
        _data = new float[(long)frames * Math.Max(1, channels)];
        for (int f = 0; f < frames; f++)
        {
            for (int c = 0; c < channels; c++)
            {
                long at = transpose ? (long)f * rows + c : (long)c * rows + f;
                _data[((long)f * channels) + c] = (float)AudioShared.Normalize(numbers[at], numericClass);
            }
        }
    }

    /// <summary>audioplayerDesktop's initialize: the channel count's check, the float rate, and the padded end.</summary>
    private void Initialize(DeviceCall call)
    {
        if (!(_channels > 0 && _channels <= 2))
        {
            throw call.Error("MATLAB:audiovideo:audioplayer:invalidnumberofchannels", "Only one- and two-channel audio supported.");
        }

        if (DeviceChecks.ClassOf(_sampleRate) is not ("double" or "single"))
        {
            throw call.Error("MATLAB:math:mustBeFloat", "Invalid data type. Argument must be double or single.");
        }

        if (NoOutputs(call))
        {
            return;
        }

        _startIndex = 1;
        _endIndex = PaddedEnd(_total);
    }

    private long PaddedEnd(long endIndex) => endIndex + BufferSize - (endIndex % BufferSize);

    /// <summary>hasNoAudioHardware: with no output at all, R2025b warns and every playback verb does nothing.</summary>
    private bool NoOutputs(DeviceCall call)
    {
        if (AudioShared.Backend(Session).Devices().Any(static d => !d.Input))
        {
            return false;
        }

        JgsBuiltins.Warn(call.Host, "MATLAB:audiovideo:audioplayer:noAudioOutputDevice", "No audio output device was found on this system.");
        return true;
    }

    // --- setters, with R2025b's checks -------------------------------------------------------------------

    private void SetSampleRate(JgsValue value, DeviceCall call)
    {
        if (!(AudioShared.Scalar(value) is { } rate && !double.IsInfinity(rate)))
        {
            throw call.Error("MATLAB:audiovideo:audioplayer:nonscalarSampleRate", "SAMPLERATE must be specified as a finite scalar value.");
        }

        if (rate < 80 || rate > 1e6)
        {
            throw call.Error("MATLAB:audiovideo:audioplayer:invalidSampleRate", "Sample rate must be a positive number between 80 and 1000000");
        }

        bool changed = _data.Length > 0 && rate != SampleRate;
        _sampleRate = value;
        if (IsPlaying && changed)
        {
            // Playing: stop and play again from where it was, so the new rate reaches the device.
            Pause(call);
            long start = CurrentSample;
            Stop(call);
            long stop = Math.Min(_endIndex, _total);
            Play(call, start, stop);
        }
    }

    private void SetBits(JgsValue value, DeviceCall call)
    {
        if (DeviceChecks.Count(value) != 1)
        {
            throw call.Error("MATLAB:audiovideo:audioplayer:nonscalarBitsPerSample", "NBITS must be specified as a finite scalar value.");
        }

        double bits = DeviceChecks.Numbers(value).First();
        if (bits is not (8 or 16 or 24))
        {
            throw call.Error("MATLAB:audiovideo:audioplayer:bitsupport", "Currently only 8, 16, and 24-bit audio is supported.");
        }

        _bits = (int)bits;
    }

    private void SetDeviceId(JgsValue value, DeviceCall call)
    {
        if (!(DeviceChecks.Count(value) == 1 && !double.IsInfinity(DeviceChecks.Numbers(value).First())))
        {
            throw call.Error("MATLAB:audiovideo:audioplayer:nonscalarDeviceID", "ID must be specified as a finite scalar value.");
        }

        double id = DeviceChecks.Numbers(value).First();
        if (!(id == -1 || AudioShared.Backend(Session).Devices().Any(d => !d.Input && d.Id == id)))
        {
            throw call.Error("MATLAB:audiovideo:audioplayer:invaliddeviceID", "Could not find the specified device");
        }

        _deviceId = (int)id;
    }

    private static JgsValue Callback(JgsValue value, DeviceCall call) =>
        AudioShared.ValidCallback(value) ? value : throw call.Error("MATLAB:audiovideo:audioplayer:invalidfunctionhandle", "Invalid function handle.");

    // --- the class -----------------------------------------------------------------------------------------

    private static AudioPlayerObject Me(DeviceObject o) => (AudioPlayerObject)o;

    private static DeviceClass Declare()
    {
        var properties = new List<DeviceProperty>
        {
            new("SampleRate", static (o, _) => Me(o)._sampleRate, static (o, v, c) => Me(o).SetSampleRate(v, c)),
            new("BitsPerSample", static (o, _) => JgsValue.Number(Me(o)._bits)),
            new("NumChannels", static (o, _) => JgsValue.Number(Me(o)._channels)),
            new("DeviceID", static (o, _) => JgsValue.Number(Me(o)._deviceId)),
            new("NumberOfChannels", static (o, _) => JgsValue.Number(Me(o)._channels), Hidden: true),
            new("CurrentSample", static (o, _) => JgsValue.Number(Me(o).CurrentSample)),
            new("TotalSamples", static (o, _) => JgsValue.Number(Me(o)._total)),
            new("Running", static (o, _) => JgsValue.Str(Me(o).IsPlaying ? "on" : "off")),
            new("StartFcn", static (o, _) => Me(o)._startFcn, static (o, v, c) => Me(o)._startFcn = Callback(v, c)),
            new("StopFcn", static (o, _) => Me(o)._stopFcn, static (o, v, c) => Me(o)._stopFcn = Callback(v, c)),
            new("TimerFcn", static (o, _) => Me(o)._timerFcn, static (o, v, c) => Me(o)._timerFcn = Callback(v, c)),
            new("TimerPeriod", static (o, _) => JgsValue.Number(Me(o)._timerPeriod), static (o, v, c) => Me(o).SetTimerPeriod(v, c)),
            new("Tag", static (o, _) => Me(o)._tag, static (o, v, c) => Me(o)._tag = JgsBuiltins.IsTextScalar(v) || (v.Type == JgsType.String)
                ? v
                : throw c.Error("MATLAB:audiovideo:audioplayer:TagMustBeString", "TAG must be a character vector or string scalar.")),
            new("UserData", static (o, _) => Me(o)._userData, static (o, v, _) => Me(o)._userData = v),
            new("Type", static (_, _) => JgsValue.Str("audioplayer")),
        };

        var methods = new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal)
        {
            ["play"] = static c => Void(c, static c => Me(c.Target).PlayMethod(c)),
            ["playblocking"] = static c => Void(c, static c => Me(c.Target).PlayBlocking(c)),
            ["pause"] = static c => Void(c, static c => Me(c.Target).Pause(c)),
            ["resume"] = static c => Void(c, static c => Me(c.Target).Resume(c)),
            ["stop"] = static c => Void(c, static c => Me(c.Target).Stop(c)),
            ["isplaying"] = static c => [JgsValue.Bool(Me(c.Target).IsPlaying)],
            ["clearAudioData"] = static c => Void(c, static c => Me(c.Target).ClearAudioData()),
        };

        return new DeviceClass("audioplayer", "audioplayer", ["matlab.mixin.SetGet", "handle", "matlab.mixin.CustomDisplay"], properties, methods,
            ["addlistener", "delete", "eq", "findobj", "findprop", "ge", "get", "gt", "horzcat", "isplaying", "isvalid", "le", "listener", "lt", "ne",
             "notify", "pause", "play", "playblocking", "resume", "set", "stop", "vertcat"],
            properties.Where(static p => !p.Hidden).Select(static p => p.Name).ToArray())
        {
            LooseNames = true,
            ConcatenationRefusal = ("MATLAB:audiovideo:audioplayer:noconcatenation", "Audioplayer objects cannot be concatenated."),
        };
    }

    private void SetTimerPeriod(JgsValue value, DeviceCall call) => _timerPeriod = AudioShared.TimerPeriod(value, "audioplayer", call);

    private static JgsValue[] Void(DeviceCall call, Action<DeviceCall> body)
    {
        if (call.Wanted > 0)
        {
            throw call.Error("MATLAB:TooManyOutputs", "Too many output arguments.");
        }

        call.Target.LiveOrThrow(call.Line, call.Column);
        body(call);
        return [];
    }

    /// <summary>The display R2025b's CustomDisplay gives: every visible property.</summary>
    protected override string ShortDisplay() => "audioplayer with properties:\n\n" + LongDisplay().TrimEnd('\n');

    /// <summary>
    /// The next sample to play: the channel's count of samples sent from StartIndex, or 1 once none are
    /// left to send, as audioplayerDesktop's get.CurrentSample reckons it.
    /// </summary>
    private long CurrentSample
    {
        get
        {
            long queued = _endIndex - _startIndex + 1;
            long sent = queued - DataToSend;
            return sent >= queued ? 1 : _startIndex + sent;
        }
    }

    // --- playback --------------------------------------------------------------------------------------------

    /// <summary><c>play(p)</c>, <c>play(p, start)</c>, <c>play(p, [start stop])</c>.</summary>
    private void PlayMethod(DeviceCall call)
    {
        if (call.Args.Count > 1)
        {
            throw call.Error("MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        if (NoOutputs(call) || IsPlaying)
        {
            return;
        }

        long start = 1;
        long end = _total;
        if (call.Args.Count == 1)
        {
            JgsValue index = call.Args[0];
            if (!AudioShared.IsNumeric(index) || DeviceChecks.Count(index) > 2 || DeviceChecks.Count(index) == 0)
            {
                throw call.Error("MATLAB:audiovideo:audioplayer:invalidIndex", "The second argument to PLAY must be a numeric scalar or 2 element vector.");
            }

            double[] bounds = DeviceChecks.Numbers(index).ToArray();
            start = (long)bounds[0];
            end = index.Type == JgsType.Array && index.Cols == 2 ? (long)bounds[1] : _total;
            if (start <= 0 || start >= end || end > _total)
            {
                JgsBuiltins.Warn(call.Host, "MATLAB:audiovideo:audioplayer:invalidselection", "Invalid playback selection.  Playback will start at the beginning.");
                start = 1;
                end = _total;
            }
        }

        Play(call, start, end);
    }

    /// <summary>audioplayerDesktop.play: queue [start, end] padded to whole buffers, then resume.</summary>
    private void Play(DeviceCall call, long start, long end)
    {
        _startIndex = start;
        _endIndex = end + BufferSize - ((end - start + 1) % BufferSize);
        _sentBefore = 0;
        _playEnd = end;
        Resume(call);
    }

    private long _playEnd;

    private void Resume(DeviceCall call)
    {
        if (NoOutputs(call) || IsPlaying)
        {
            return;
        }

        if (DataToSend == 0)
        {
            PlayMethod(new DeviceCall { Target = this, Args = [], Line = call.Line, Column = call.Column, Wanted = 0 });
            return;
        }

        AudioShared.Run(this, _startFcn);
        IAudioBackend backend = AudioShared.Backend(Session);
        AudioDeviceInfo device = AudioShared.Device(backend, _deviceId, input: false)
            ?? throw call.Error("MATLAB:audiovideo:audioplayer:DeviceError", "The audio output device is no longer available.");

        // The frames still to send: from where this playback stands to its padded end, zeros past the signal.
        long from = _startIndex + Sent;
        long frames = _endIndex - from + 1;
        float[] block = new float[frames * _channels];
        long real = Math.Max(0, Math.Min(_playEnd, _total) - from + 1);
        Array.Copy(_data, (from - 1) * _channels, block, 0, real * _channels);
        _sentBefore = Sent;
        _stream?.Dispose();
        _stream = backend.OpenOutput(device, (int)Math.Round(SampleRate), _channels);
        int generation = ++_generation;
        try
        {
            _stream.Start(block, (int)frames, () => _queue.Post(() => Done(generation)));
        }
        catch (DeviceOpenException e)
        {
            _stream.Dispose();
            _stream = null;
            throw call.Error("MATLAB:audiovideo:audioplayer:DeviceError", e.Message.Replace("PortAudio", "Device", StringComparison.Ordinal));
        }

        _open = true;
        StartTimer();
    }

    /// <summary>The channel's DoneEvent: the last buffer played, so the player stops.</summary>
    private void Done(int generation)
    {
        if (Deleted || generation != _generation || !_open)
        {
            return;
        }

        _sentBefore += _stream?.FramesPlayed ?? 0;
        _open = false;
        _sentBefore = _endIndex - _startIndex + 1; // flushed: nothing left to send
        StopTimer();
        AudioShared.RunReported(this, _stopFcn, "StopFcn");
    }

    private void StartTimer()
    {
        if (AudioShared.IsEmpty(_timerFcn))
        {
            return;
        }

        int generation = _generation;
        TimeSpan period = TimeSpan.FromSeconds(_timerPeriod);
        _timer = new Timer(_ => _queue.Post(() =>
        {
            if (!Deleted && _open && generation == _generation)
            {
                AudioShared.RunReported(this, _timerFcn, "TimerFcn");
            }
        }), null, period, period);
    }

    private void StopTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>audioplayerDesktop.pause: close the channel, keeping what is left to send, and run StopFcn.</summary>
    private void Pause(DeviceCall call)
    {
        if (NoOutputs(call) || !IsPlaying)
        {
            return;
        }

        long played = _stream?.FramesPlayed ?? 0;
        _stream?.Stop();
        _sentBefore += played;
        _open = false;
        _generation++;
        StopTimer();
        AudioShared.Run(this, _stopFcn);
    }

    /// <summary>audioplayerDesktop.stop: flush what is left to send, then pause.</summary>
    private void Stop(DeviceCall call)
    {
        if (NoOutputs(call))
        {
            return;
        }

        // Flushed first, so StopFcn already sees CurrentSample back at 1.
        bool playing = IsPlaying;
        if (playing)
        {
            _stream?.Stop();
            _open = false;
            _generation++;
            StopTimer();
        }

        _sentBefore = _endIndex - _startIndex + 1;
        if (playing)
        {
            AudioShared.Run(this, _stopFcn);
        }
    }

    /// <summary>playblocking: play, then wait — running callbacks as a pause does — until the player stops.</summary>
    private void PlayBlocking(DeviceCall call)
    {
        if (NoOutputs(call))
        {
            return;
        }

        PlayMethod(call);
        while (IsPlaying)
        {
            JgsBuiltins.PumpWait(TimeSpan.FromMilliseconds(10), Interpreter.Cancellation, call.Host.Timers);
        }

        Stop(call);
    }

    private void ClearAudioData()
    {
        // Iaudioplayer.clearAudioData: the samples become [1 1], a column of two.
        if (!IsPlaying)
        {
            _data = [1, 1];
            _total = 2;
            _channels = 1;
        }
    }

    protected override void OnDelete()
    {
        _generation++;
        StopTimer();
        _stream?.Dispose();
        _stream = null;
        _open = false;
    }
}

/// <summary>
/// <c>r = audiorecorder(fs, nbits, nchannels, id)</c> (device classes plan, stage D10, ADR 0193),
/// transcribed from R2025b's audiorecorder.m, Iaudiorecorder and audiorecorderDesktop: the checks and
/// sentences of its setters, record, recordblocking, pause, resume, stop, isrecording, getaudiodata,
/// getplayer and play, TotalSamples and CurrentSample as the desktop recorder counts them, and its
/// callbacks. The samples are kept at the recorder's bits, as R2025b's channel keeps them.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class AudioRecorderObject : DeviceObject
{
    private static readonly DeviceClass Declaration = Declare();

    private readonly DeviceEventQueue _queue;
    private readonly object _gate = new();
    private JgsValue _sampleRate = JgsValue.Number(8000);
    private int _bits = 8;
    private int _channels = 1;
    private int _deviceId = -1;
    private JgsValue _startFcn = JgsEmpty.Zero();
    private JgsValue _stopFcn = JgsEmpty.Zero();
    private JgsValue _timerFcn = JgsEmpty.Zero();
    private double _timerPeriod = 0.05;
    private JgsValue _tag = JgsValue.Str("");
    private JgsValue _userData = JgsEmpty.Zero();
    private JgsValue _bufferLength = JgsEmpty.Zero();
    private JgsValue _numberOfBuffers = JgsEmpty.Zero();

    // Samples as R2025b's channel delivers them, quantized to the recorder's bits: frames, interleaved.
    private readonly List<double> _audio = [];
    private readonly List<double> _incoming = [];
    private ulong _samplesToRead = ulong.MaxValue;
    private bool _stopCalled = true;
    private IAudioInputStream? _stream;
    private bool _open;
    private Timer? _timer;
    private int _generation;

    private AudioRecorderObject(DeviceSession session, Interpreter interpreter)
        : base(session, interpreter)
    {
        _queue = DeviceEventQueue.ForCurrentThread();
    }

    public override DeviceClass Class => Declaration;

    internal int Bits => _bits;

    internal JgsValue SampleRateValue => _sampleRate;

    private double SampleRate => DeviceChecks.Numbers(_sampleRate).First();

    private bool IsRecording => _open;

    public override string? Summary() => Deleted
        ? "deleted"
        : $"{DeviceObject.Shown(_sampleRate)} Hz, {_bits} bits, {(_channels == 1 ? "mono" : _channels == 2 ? "stereo" : _channels + " channels")}, {(IsRecording ? "recording" : "stopped")}";

    // --- construction ----------------------------------------------------------------------------------

    /// <summary><c>audiorecorder</c>, <c>audiorecorder(fs, nbits, nchannels)</c>, <c>audiorecorder(fs, nbits, nchannels, id)</c>.</summary>
    public static JgsValue Create(DeviceSession session, Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count > 4)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        var recorder = new AudioRecorderObject(session, interpreter);
        var call = new DeviceCall { Target = recorder, Args = args, Line = line, Column = col };
        if (args.Count is 1 or 2)
        {
            throw call.Error("MATLAB:audiovideo:audiorecorder:incorrectnumberinputs", "Incorrect number of parameters to audiorecorder.");
        }

        if (args.Count >= 3)
        {
            recorder.SetSampleRate(args[0], call);
            recorder.SetBits(args[1], call);
            recorder.SetChannels(args[2], call);
        }

        if (args.Count == 4)
        {
            recorder.SetDeviceId(args[3], call);
        }

        if (DeviceChecks.ClassOf(recorder._sampleRate) is not ("double" or "single"))
        {
            throw call.Error("MATLAB:audiovideo:audiorecorder:DeviceError", "Assertion failed.");
        }

        session.Remember(recorder);
        JgsValue made = JgsValue.External(recorder);
        JgsLifetime.Minted(made);
        return made;
    }

    private void SetSampleRate(JgsValue value, DeviceCall call)
    {
        if (DeviceChecks.Count(value) != 1)
        {
            throw AudioShared.NonLogicalConditional(call);
        }

        double rate = DeviceChecks.Numbers(value).First();
        if (rate <= 80 || rate > 1e6 || double.IsNaN(rate))
        {
            throw call.Error("MATLAB:audiovideo:audiorecorder:invalidSampleRate", "Sample rate must be a positive number between 80 and 1000000.");
        }

        _sampleRate = value;
    }

    private void SetBits(JgsValue value, DeviceCall call)
    {
        if (DeviceChecks.Count(value) != 1)
        {
            throw AudioShared.NonLogicalConditional(call);
        }

        double bits = DeviceChecks.Numbers(value).First();
        _bits = bits is 8 or 16 or 24
            ? (int)bits
            : throw call.Error("MATLAB:audiovideo:audiorecorder:bitsupport", "Currently only 8, 16, and 24-bit audio is supported.");
    }

    private void SetChannels(JgsValue value, DeviceCall call)
    {
        double channels = DeviceChecks.Count(value) == 1 ? DeviceChecks.Numbers(value).First() : 0;
        _channels = DeviceChecks.Count(value) == 1 && channels > 0 && channels <= 2
            ? (int)channels
            : throw call.Error("MATLAB:audiovideo:audiorecorder:numchannelsupport", "Currently only one and two channel audio is supported.");
    }

    private void SetDeviceId(JgsValue value, DeviceCall call)
    {
        if (!(DeviceChecks.Count(value) == 1 && !double.IsInfinity(DeviceChecks.Numbers(value).First())))
        {
            throw call.Error("MATLAB:audiovideo:audiorecorder:NonscalarDeviceID", "ID must be a positive integer");
        }

        IReadOnlyList<AudioDeviceInfo> devices = AudioShared.Backend(Session).Devices();
        if (!devices.Any(static d => d.Input))
        {
            throw call.Error("MATLAB:audiovideo:audiorecorder:noAudioInputDevice", "No audio input device was found on this system.");
        }

        double id = DeviceChecks.Numbers(value).First();
        if (!(id == -1 || devices.Any(d => d.Input && d.Id == id)))
        {
            throw call.Error("MATLAB:audiovideo:audiorecorder:InvalidDeviceID", "Could not find the specified device.");
        }

        _deviceId = (int)id;
    }

    private static JgsValue Callback(JgsValue value, DeviceCall call) =>
        AudioShared.ValidCallback(value) ? value : throw call.Error("MATLAB:audiovideo:audiorecorder:invalidfunctionhandle", "Invalid function handle");

    // --- the class -----------------------------------------------------------------------------------------

    private static AudioRecorderObject Me(DeviceObject o) => (AudioRecorderObject)o;

    private static DeviceClass Declare()
    {
        var properties = new List<DeviceProperty>
        {
            new("SampleRate", static (o, _) => Me(o)._sampleRate),
            new("BitsPerSample", static (o, _) => JgsValue.Number(Me(o)._bits)),
            new("NumChannels", static (o, _) => JgsValue.Number(Me(o)._channels)),
            new("DeviceID", static (o, _) => JgsValue.Number(Me(o)._deviceId)),
            new("CurrentSample", static (o, _) => JgsValue.Number(Me(o).CurrentSample)),
            new("TotalSamples", static (o, _) => JgsValue.Number(Me(o).TotalSamples)),
            new("Running", static (o, _) => JgsValue.Str(Me(o).IsRecording ? "on" : "off")),
            new("NumberOfChannels", static (o, _) => JgsValue.Number(Me(o)._channels), Hidden: true),
            new("StartFcn", static (o, _) => Me(o)._startFcn, static (o, v, c) => Me(o)._startFcn = Callback(v, c)),
            new("StopFcn", static (o, _) => Me(o)._stopFcn, static (o, v, c) => Me(o)._stopFcn = Callback(v, c)),
            new("TimerFcn", static (o, _) => Me(o)._timerFcn, static (o, v, c) => Me(o)._timerFcn = Callback(v, c)),
            new("TimerPeriod", static (o, _) => JgsValue.Number(Me(o)._timerPeriod),
                static (o, v, c) => Me(o)._timerPeriod = AudioShared.TimerPeriod(v, "audiorecorder", c)),
            new("Tag", static (o, _) => Me(o)._tag, static (o, v, c) => Me(o)._tag = JgsBuiltins.IsTextScalar(v)
                ? v
                : throw c.Error("MATLAB:audiovideo:audiorecorder:TagMustBeString", "TAG must be a character vector or string scalar")),
            new("UserData", static (o, _) => Me(o)._userData, static (o, v, _) => Me(o)._userData = v),
            new("Type", static (_, _) => JgsValue.Str("audiorecorder")),
            new("BufferLength", static (o, _) => Me(o)._bufferLength, static (o, v, _) => Me(o)._bufferLength = v, Hidden: true),
            new("NumberOfBuffers", static (o, _) => Me(o)._numberOfBuffers, static (o, v, _) => Me(o)._numberOfBuffers = v, Hidden: true),
        };

        var methods = new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal)
        {
            ["record"] = static c => Void(c, static c => Me(c.Target).Record(c)),
            ["recordblocking"] = static c => Void(c, static c => Me(c.Target).RecordBlocking(c)),
            ["pause"] = static c => Void(c, static c => Me(c.Target).Pause()),
            ["resume"] = static c => Void(c, static c => Me(c.Target).Resume(c)),
            ["stop"] = static c => Void(c, static c => Me(c.Target).Stop()),
            ["isrecording"] = static c => [JgsValue.Bool(Me(c.Target).IsRecording)],
            ["getaudiodata"] = static c => [Me(c.Target).GetAudioData(c)],
            ["getplayer"] = static c => [Me(c.Target).Player(c)],
            ["play"] = static c => Me(c.Target).Play(c),
        };

        return new DeviceClass("audiorecorder", "audiorecorder", ["matlab.mixin.SetGet", "handle", "matlab.mixin.CustomDisplay"], properties, methods,
            ["addlistener", "audiorecorder", "delete", "eq", "findobj", "findprop", "ge", "get", "getaudiodata", "getplayer", "gt", "horzcat",
             "isrecording", "isvalid", "le", "listener", "lt", "ne", "notify", "pause", "play", "record", "recordblocking", "resume", "set", "stop",
             "vertcat"],
            properties.Where(static p => !p.Hidden).Select(static p => p.Name).ToArray())
        {
            LooseNames = true,
            ConcatenationRefusal = ("MATLAB:audiovideo:audiorecorder:noconcatenation", "Audiorecorder objects cannot be concatenated."),
        };
    }

    private static JgsValue[] Void(DeviceCall call, Action<DeviceCall> body)
    {
        if (call.Wanted > 0)
        {
            throw call.Error("MATLAB:TooManyOutputs", "Too many output arguments.");
        }

        call.Target.LiveOrThrow(call.Line, call.Column);
        body(call);
        return [];
    }

    protected override string ShortDisplay() => "audiorecorder with properties:\n\n" + LongDisplay().TrimEnd('\n');

    // --- counting ------------------------------------------------------------------------------------------

    /// <summary>What was recorded plus what the device delivered since (the channel's DataAvailable), up to what was asked for.</summary>
    private long TotalSamples
    {
        get
        {
            lock (_gate)
            {
                return (long)Math.Min((ulong)((_audio.Count + _incoming.Count) / _channels), _samplesToRead);
            }
        }
    }

    private long CurrentSample => !IsRecording && _stopCalled ? 1 : TotalSamples + 1;

    // --- recording -----------------------------------------------------------------------------------------

    /// <summary><c>record(r)</c>, <c>record(r, seconds)</c>.</summary>
    private void Record(DeviceCall call)
    {
        if (IsRecording)
        {
            return;
        }

        if (call.Args.Count > 1)
        {
            throw call.Error("MATLAB:TooManyInputs", "Too many input arguments.");
        }

        if (call.Args.Count == 1)
        {
            JgsValue seconds = call.Args[0];
            if (AudioShared.IsNumeric(seconds) && DeviceChecks.Count(seconds) > 1)
            {
                throw AudioShared.NonLogicalConditional(call);
            }

            double s = AudioShared.IsNumeric(seconds) && DeviceChecks.Count(seconds) > 0 ? DeviceChecks.Numbers(seconds).First() : double.NaN;
            if (DeviceChecks.Count(seconds) == 0 || !AudioShared.IsNumeric(seconds) || s <= 0 || double.IsNaN(s))
            {
                throw call.Error("MATLAB:audiovideo:audiorecorder:recordTimeInvalid", "Recording times must be a double value greater than zero.");
            }

            _samplesToRead = (ulong)Math.Round(s * SampleRate);
            if (!_stopCalled)
            {
                _samplesToRead += (ulong)TotalSamples;
            }
        }
        else
        {
            _samplesToRead = ulong.MaxValue;
        }

        Resume(call);
    }

    private void Resume(DeviceCall call)
    {
        if (IsRecording)
        {
            return;
        }

        if (_stopCalled)
        {
            _stopCalled = false;
            _audio.Clear();
            lock (_gate)
            {
                _incoming.Clear();
            }
        }

        AudioShared.Run(this, _startFcn);
        IAudioBackend backend = AudioShared.Backend(Session);
        AudioDeviceInfo device = AudioShared.Device(backend, _deviceId, input: true)
            ?? throw call.Error("MATLAB:audiovideo:audiorecorder:DeviceError", "The audio input device is no longer available.");
        _stream?.Dispose();
        _stream = backend.OpenInput(device, (int)Math.Round(SampleRate), _channels);
        int generation = ++_generation;
        try
        {
            _stream.Start((block, frames) => Arrived(block, frames, generation), _ => _queue.Post(() => Done(generation)));
        }
        catch (DeviceOpenException e)
        {
            _stream.Dispose();
            _stream = null;
            throw call.Error("MATLAB:audiovideo:audiorecorder:DeviceError", e.Message.Replace("PortAudio", "Device", StringComparison.Ordinal));
        }

        _open = true;
        StartTimer();
    }

    /// <summary>A block from the device thread: kept, quantized to the recorder's bits, until the recording has all it asked for.</summary>
    private void Arrived(float[] block, int frames, int generation)
    {
        bool full;
        lock (_gate)
        {
            if (generation != _generation)
            {
                return;
            }

            for (int i = 0; i < frames * _channels; i++)
            {
                _incoming.Add(Quantize(block[i]));
            }

            full = (ulong)(_audio.Count / _channels + _incoming.Count / _channels) >= _samplesToRead;
        }

        if (full)
        {
            _stream?.Stop();
            _queue.Post(() => Done(generation));
        }
    }

    /// <summary>A sample as the recorder's bits keep it, as a normalized double.</summary>
    private double Quantize(float sample)
    {
        double x = Math.Clamp(sample, -1f, 1f);
        return _bits switch
        {
            8 => (Math.Clamp(Math.Round(x * 128) + 128, 0, 255) - 128) / 128.0,
            16 => Math.Clamp(Math.Round(x * 32768), -32768, 32767) / 32768.0,
            _ => x,
        };
    }

    /// <summary>The channel's DoneEvent: the recording has what it asked for, so it stops.</summary>
    private void Done(int generation)
    {
        if (!Deleted && generation == _generation && _open)
        {
            _stopCalled = true;
            PauseCore(reportErrors: true);
        }
    }

    private void StartTimer()
    {
        if (AudioShared.IsEmpty(_timerFcn))
        {
            return;
        }

        int generation = _generation;
        TimeSpan period = TimeSpan.FromSeconds(_timerPeriod);
        _timer = new Timer(_ => _queue.Post(() =>
        {
            if (!Deleted && _open && generation == _generation)
            {
                AudioShared.RunReported(this, _timerFcn, "TimerFcn");
            }
        }), null, period, period);
    }

    private void Pause() => PauseCore(reportErrors: false);

    private void PauseCore(bool reportErrors)
    {
        if (!IsRecording)
        {
            return;
        }

        _stream?.Stop();
        _open = false;
        _timer?.Dispose();
        _timer = null;
        MoveIncoming();
        if (reportErrors)
        {
            AudioShared.RunReported(this, _stopFcn, "StopFcn");
        }
        else
        {
            AudioShared.Run(this, _stopFcn);
        }
    }

    private void Stop()
    {
        _stopCalled = true;
        Pause();
    }

    /// <summary>recordblocking(r, seconds): record, wait the time out as a pause does, then until the recorder stops.</summary>
    private void RecordBlocking(DeviceCall call)
    {
        if (call.Args.Count < 1)
        {
            throw call.Error("MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (call.Args.Count > 1)
        {
            throw call.Error("MATLAB:TooManyInputs", "Too many input arguments.");
        }

        if (IsRecording)
        {
            return;
        }

        Record(call);
        double seconds = DeviceChecks.Numbers(call.Args[0]).First();
        JgsBuiltins.PumpWait(TimeSpan.FromSeconds(seconds), Interpreter.Cancellation, call.Host.Timers);
        while (IsRecording)
        {
            JgsBuiltins.PumpWait(TimeSpan.FromMilliseconds(10), Interpreter.Cancellation, call.Host.Timers);
        }
    }

    /// <summary>Moves what the device delivered into the recording, keeping no more than was asked for.</summary>
    private void MoveIncoming()
    {
        lock (_gate)
        {
            _audio.AddRange(_incoming);
            _incoming.Clear();
            long keep = (long)Math.Min((ulong)(_audio.Count / _channels), _samplesToRead) * _channels;
            if (_audio.Count > keep)
            {
                _audio.RemoveRange((int)keep, _audio.Count - (int)keep);
            }
        }
    }

    /// <summary><c>getaudiodata(r)</c>, <c>getaudiodata(r, type)</c>: the recording as a frames-by-channels array of that class.</summary>
    private JgsValue GetAudioData(DeviceCall call)
    {
        call.Target.LiveOrThrow(call.Line, call.Column);
        if (call.Args.Count > 1)
        {
            throw call.Error("MATLAB:TooManyInputs", "Too many input arguments.");
        }

        string type = "double";
        if (call.Args.Count == 1)
        {
            JgsValue asked = call.Args[0];
            if (!(JgsBuiltins.IsTextScalar(asked) || (asked.Type == JgsType.String)))
            {
                throw call.Error("MATLAB:audiovideo:audiorecorder:unsupportedtype", "Unsupported data type.");
            }

            type = JgsBuiltins.TextOf(asked);
            if (type is not ("double" or "single" or "int16" or "uint8" or "int8"))
            {
                throw call.Error("MATLAB:audiovideo:audiorecorder:unsupportedtype", "Unsupported data type.");
            }
        }

        return AudioData(type, call);
    }

    /// <summary>The recording in <paramref name="type"/>; recorderempty when there is none.</summary>
    internal JgsValue AudioData(string type, DeviceCall call)
    {
        MoveIncoming();
        int frames = _audio.Count / _channels;
        if (frames == 0)
        {
            throw call.Error("MATLAB:audiovideo:audiorecorder:recorderempty", "Recorder is empty.");
        }

        double[] columnMajor = new double[frames * _channels];
        for (int f = 0; f < frames; f++)
        {
            for (int c = 0; c < _channels; c++)
            {
                double x = _audio[(f * _channels) + c];
                columnMajor[(c * frames) + f] = type switch
                {
                    "int16" => Math.Clamp(Math.Round(x * 32768), -32768, 32767),
                    "uint8" => Math.Clamp(Math.Round(x * 128) + 128, 0, 255),
                    "int8" => Math.Clamp(Math.Round(x * 128), -128, 127),
                    "single" => (float)x,
                    _ => x,
                };
            }
        }

        JgsValue data = JgsMatrix.FromColumnMajor(columnMajor, frames, _channels);
        data.SetNumericClass(type switch
        {
            "int16" => JgsNumericClass.Int16,
            "uint8" => JgsNumericClass.UInt8,
            "int8" => JgsNumericClass.Int8,
            "single" => JgsNumericClass.Single,
            _ => JgsNumericClass.Double,
        });
        return data;
    }

    private JgsValue Player(DeviceCall call) =>
        AudioPlayerObject.Create(Session, Interpreter, [JgsValue.External(this)], call.Line, call.Column);

    /// <summary><c>p = play(r)</c>, <c>play(r, start)</c>, <c>play(r, [start stop])</c>: a player of the recording, playing.</summary>
    private JgsValue[] Play(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        if (call.Args.Count > 1)
        {
            throw call.Error("MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        JgsValue player = Player(call);
        var device = (DeviceObject)player.AsExternal;
        device.CallMethod("play", [player, .. call.Args], 0, call.Line, call.Column);
        return [player];
    }

    protected override void OnDelete()
    {
        _generation++;
        _timer?.Dispose();
        _timer = null;
        _stream?.Dispose();
        _stream = null;
        _open = false;
    }
}
