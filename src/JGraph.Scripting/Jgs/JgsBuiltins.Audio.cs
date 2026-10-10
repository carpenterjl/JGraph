using System.Runtime.Versioning;
using JGraph.Devices;
using JGraph.Devices.Audio;
using JGraph.Scripting.Jgs.Devices;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Audio devices (device classes plan, stage D10, ADR 0193): <c>audiodevinfo</c> and
/// <c>audiodevreset</c> transcribed from R2025b's audiodevinfoDesktop.m and audiodevreset.m, the
/// <c>audioplayer</c> and <c>audiorecorder</c> constructors, <c>sound</c> and <c>soundsc</c>
/// transcribed from sound.m and soundsc.m, and the test-only <c>jgraph.internal.audiosim</c>.
/// </summary>
internal static partial class JgsBuiltins
{
    [SupportedOSPlatform("windows")]
    private static void RegisterAudioBuiltins(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        void Keeping(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { KeepsStringArguments = true }));
        Keeping("audioplayer", (args, line, col) => AudioPlayerObject.Create(host.Devices, interpreter, args, line, col));
        Keeping("audiorecorder", (args, line, col) => AudioRecorderObject.Create(host.Devices, interpreter, args, line, col));
        env.Builtins.Register("audiodevinfo", JgsValue.Function(new BuiltinFunction("audiodevinfo",
            (args, line, col) => AudioDevInfo(host.Devices, interpreter, args, 1, line, col))
        {
            KeepsStringArguments = true,
            TakesOutputCount = true,
            AutoCallsBare = true,
            MultiOutput = (args, wanted, line, col) => [AudioDevInfo(host.Devices, interpreter, args, wanted, line, col)],
        }));
        env.Builtins.Register("audiodevreset", JgsValue.Function(new BuiltinFunction("audiodevreset", (args, line, col) =>
        {
            if (args.Count > 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
            }

            AudioShared.Backend(host.Devices).Reset();
            return JgsValue.Null;
        })
        {
            AutoCallsBare = true,
            BindsAnsAsStatement = false,
        }));
        env.Builtins.Register("sound", JgsValue.Function(new BuiltinFunction("sound", (args, line, col) =>
        {
            Sound(host.Devices, interpreter, args, line, col);
            return JgsValue.Null;
        })
        {
            BindsAnsAsStatement = false,
        }));
        env.Builtins.Register("soundsc", JgsValue.Function(new BuiltinFunction("soundsc", (args, line, col) =>
        {
            SoundSc(host.Devices, interpreter, args, line, col);
            return JgsValue.Null;
        })
        {
            BindsAnsAsStatement = false,
        }));
    }

    // --- audiodevinfo ----------------------------------------------------------------------------------------

    [SupportedOSPlatform("windows")]
    private static JgsValue AudioDevInfo(DeviceSession session, Interpreter interpreter, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        IAudioBackend backend = AudioShared.Backend(session);
        JgsRuntimeException Error(string key, string text) => new(line, col, $"MATLAB:audiovideo:audiodevinfo:{key}", text);

        bool Input(JgsValue io)
        {
            if (DeviceChecks.Count(io) > 1 && AudioShared.IsNumeric(io))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:nonLogicalConditional",
                    "Operands to the logical AND (&&) and OR (||) operators must be convertible to logical scalar values. Use the ANY or ALL functions to reduce operands to logical scalar values.");
            }

            return AudioShared.Scalar(io) switch
            {
                1 => true,
                0 => false,
                _ => throw Error("invalidDeviceType", "Device must either be input (1) or output (0)"),
            };
        }

        IReadOnlyList<AudioDeviceInfo> Of(bool input) => backend.Devices().Where(d => d.Input == input).ToList();

        AudioDeviceInfo ById(bool input, JgsValue id)
        {
            // DeviceInfo.getDeviceInfo takes the ID's whole part: 3.7 is device 3 (measured).
            double? n = AudioShared.Scalar(id);
            AudioDeviceInfo? device = n is { } k ? backend.Devices().FirstOrDefault(d => d.Id == Math.Truncate(k)) : null;
            return device is not null && device.Input == input ? device : throw Error("invalidID", "Device ID out of range");
        }

        JgsValue List(bool input)
        {
            IReadOnlyList<AudioDeviceInfo> devices = Of(input);
            return devices.Count == 0
                ? JgsEmpty.Zero()
                : JgsValue.StructArray(devices.Select(static d => new Dictionary<string, JgsValue>(StringComparer.Ordinal)
                {
                    ["Name"] = JgsValue.Str(d.Name),
                    ["DriverVersion"] = JgsValue.Str(WasapiAudio.HostApiName),
                    ["ID"] = JgsValue.Number(d.Id),
                }).ToArray());
        }

        switch (args.Count)
        {
            case 0:
                return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
                {
                    ["input"] = List(true),
                    ["output"] = List(false),
                });
            case 1:
                return JgsValue.Number(Of(Input(args[0])).Count);
            case 2:
            {
                bool input = Input(args[0]);
                if (args[1].Type == JgsType.String && !args[1].IsStringArray)
                {
                    string name = TextOf(args[1]);
                    IReadOnlyList<AudioDeviceInfo> devices = Of(input);
                    if (devices.Count == 0)
                    {
                        throw Error("invalidDeviceName", "Device Name not Found");
                    }

                    AudioDeviceInfo[] matches = devices.Where(d => d.Name.Contains(name, StringComparison.Ordinal)).ToArray();
                    return matches.Length switch
                    {
                        0 => throw Error("invalidDeviceName", "Device Name not Found"),
                        1 => JgsValue.Number(matches[0].Id),
                        _ => throw Error("multipleDevicesWithSameName", $"Found multiple devices with name: {name}"),
                    };
                }

                return JgsValue.Str(ById(input, args[1]).Name);
            }

            case 3:
                ById(Input(args[0]), args[1]);
                return JgsValue.Str(WasapiAudio.HostApiName);
            case 4:
            {
                bool input = Input(args[0]);
                foreach (AudioDeviceInfo device in Of(input))
                {
                    if (Supports(session, interpreter, input, JgsValue.Number(device.Id), args[1], args[2], args[3], line, col))
                    {
                        return JgsValue.Number(device.Id);
                    }
                }

                return JgsValue.Number(-1);
            }

            case 5:
                return JgsValue.Bool(Supports(session, interpreter, Input(args[0]), args[1], args[2], args[3], args[4], line, col));
            default:
                // audiodevinfo.m asks audiodevinfoDesktop for devInfo however it was called, so the
                // refusal holds as a statement too (open item 28, probe_28d).
                throw new JgsRuntimeException(line, col, "MATLAB:unassignedOutputs",
                    "Output argument \"devInfo\" (and possibly others) not assigned a value in the execution with \"audiovideo.internal.audiodevinfoDesktop\" function.");
        }
    }

    /// <summary>
    /// localDoesDeviceSupport: whether a recorder or player with these settings can be made on the device
    /// and its stream opened; R2025b records or plays for a moment, JGraph opens the stream and closes it
    /// at once, keeping nothing.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static bool Supports(DeviceSession session, Interpreter interpreter, bool input, JgsValue id, JgsValue rate, JgsValue bits, JgsValue channels, int line, int col)
    {
        try
        {
            double? r = AudioShared.Scalar(rate);
            double? c = AudioShared.Scalar(channels);
            double? n = AudioShared.Scalar(id);
            if (r is null || c is null || n is null)
            {
                return false;
            }

            JgsValue made = input
                ? AudioRecorderObject.Create(session, interpreter, [rate, bits, channels, id], line, col)
                : AudioPlayerObject.Create(session, interpreter, [JgsMatrix.FromColumnMajor(new double[(int)Math.Max(1, r.Value) * (int)c.Value], (int)Math.Max(1, r.Value), (int)c.Value), rate, bits, id], line, col);
            ((DeviceObject)made.AsExternal).Delete();
            IAudioBackend backend = AudioShared.Backend(session);
            AudioDeviceInfo? device = AudioShared.Device(backend, (int)n.Value, input);
            if (device is null)
            {
                return false;
            }

            if (input)
            {
                using IAudioInputStream stream = backend.OpenInput(device, (int)Math.Round(r.Value), (int)c.Value);
                stream.Start(static (_, _) => { }, static _ => { });
                stream.Stop();
            }
            else
            {
                using IAudioOutputStream stream = backend.OpenOutput(device, (int)Math.Round(r.Value), (int)c.Value);
                stream.Start(new float[(int)c.Value], 1, static () => { });
                stream.Stop();
            }

            return true;
        }
        catch (Exception e) when (e is JgsException or DeviceOpenException)
        {
            return false;
        }
    }

    // --- sound and soundsc -------------------------------------------------------------------------------------

    /// <summary>A value's real, full, floating-point check, as sound.m and soundsc.m make it.</summary>
    private static bool RealFloat(JgsValue y)
    {
        if (y.Type == JgsType.Sparse || DeviceChecks.ClassOf(y) is not ("double" or "single"))
        {
            return false;
        }

        if (y.Type == JgsType.Complex)
        {
            return false;
        }

        if (y.Type == JgsType.Array)
        {
            for (int i = 0; i < y.ArrayLength; i++)
            {
                if (y.ElementAt(i).Type == JgsType.Complex)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary><c>sound(y)</c>, <c>sound(y, fs)</c>, <c>sound(y, fs, bits)</c>: sound.m, over an audioplayer the session keeps until it has played.</summary>
    [SupportedOSPlatform("windows")]
    private static void Sound(DeviceSession session, Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:audiovideo:playsnd:invalidInputs", "Not enough input arguments.");
        }

        if (args.Count > 3)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        JgsValue y = args[0];
        JgsValue fs = args.Count >= 2 ? args[1] : JgsValue.Number(8192);
        JgsValue bits = args.Count >= 3 ? args[2] : JgsValue.Number(16);
        if (!RealFloat(y))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:audiovideo:playsnd:invalidDataType", "Audio data must be real and floating point.");
        }

        if (DeviceChecks.Count(fs) == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:audiovideo:sound:invalidfrequencyinput", "Frequency must be a scalar\n");
        }

        if (DeviceChecks.Count(bits) == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:audiovideo:sound:invalidbitdepthinput", "The Number of bits must be a scalar.\n");
        }

        if (DeviceChecks.Count(y) == 0)
        {
            return;
        }

        int[] dims = y.Type == JgsType.Array ? y.Dims : [1, 1];
        if (dims.Length > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:audiovideo:playsnd:twoDimValuesOnly", "Requires 2-D values only.");
        }

        // Clipped to [-1, 1], one column per channel.
        double[] values = DeviceChecks.Numbers(y).Select(static v => Math.Max(-1, Math.Min(v, 1))).ToArray();
        (int rows, int cols) = dims[0] == 1 ? (dims[1], 1) : (dims[0], dims[1]);
        JgsValue clipped = JgsMatrix.FromColumnMajor(values, rows, cols);
        if (DeviceChecks.ClassOf(y) == "single")
        {
            clipped.SetNumericClass(JgsNumericClass.Single);
        }

        lock (session.SoundPlayers)
        {
            foreach (DeviceObject finished in session.SoundPlayers.Where(static p => p.Deleted || p is AudioPlayerObject { Playing: false }).ToList())
            {
                session.SoundPlayers.Remove(finished);
                finished.Delete();
            }
        }

        JgsValue made = AudioPlayerObject.Create(session, interpreter, [clipped, fs, bits], line, col, counted: false);
        var player = (AudioPlayerObject)made.AsExternal;
        lock (session.SoundPlayers)
        {
            session.SoundPlayers.Add(player);
        }

        player.CallMethod("play", [made], 0, line, col);
    }

    /// <summary><c>soundsc(y, ...)</c>, <c>soundsc(y, ..., slim)</c>: soundsc.m, scaling into [-1, 1] then playing through sound.</summary>
    [SupportedOSPlatform("windows")]
    private static void SoundSc(DeviceSession session, Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:audiovideo:soundsc:invalidInputs", "Not enough input arguments.");
        }

        JgsValue x = args[0];
        if (!RealFloat(x))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:audiovideo:playsnd:invalidDataType", "Audio data must be real and floating point.");
        }

        double[] values = DeviceChecks.Numbers(x).ToArray();
        int[] dims = x.Type == JgsType.Array ? x.Dims : [1, 1];
        JGraphScriptGlobals host = session.Host;

        // any(isinf(x)) is a row for a matrix, which an if takes as true only when every column has one.
        bool EveryColumn(Func<double, bool> test)
        {
            int rows = dims[0] == 1 ? values.Length : dims[0];
            if (rows == 0)
            {
                return false;
            }

            for (int start = 0; start < values.Length; start += rows)
            {
                if (!values.Skip(start).Take(rows).Any(test))
                {
                    return false;
                }
            }

            return values.Length > 0;
        }

        if (EveryColumn(double.IsInfinity))
        {
            Warn(host, "MATLAB:audiovideo:soundsc:infValuesFound", "Input contains Inf value which can result in unexpected behavior.");
        }

        if (EveryColumn(double.IsNaN))
        {
            Warn(host, "MATLAB:audiovideo:soundsc:NaNValuesFound", "Input contains NaN value which can result in unexpected behavior.");
        }

        var rest = args.ToList();
        double low, high;
        JgsValue last = args[^1];
        if (args.Count > 1 && last.Type == JgsType.Array && last.Rows == 1 && last.Cols == 2)
        {
            double[] v = DeviceChecks.Numbers(last).ToArray();
            if (!(AudioShared.IsNumeric(last) && !v.Any(double.IsInfinity) && v[0] <= v[1]))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:audiovideo:soundsc:invalidSLIM", "YRANGE must be a numeric monotonically increasing vector of length equal to 2.");
            }

            (low, high) = (v[0], v[1]);
            rest.RemoveAt(rest.Count - 1);
        }
        else
        {
            double max = values.Where(static v => !double.IsInfinity(v)).Select(Math.Abs).DefaultIfEmpty(double.NaN).Max();
            (low, high) = (-max, max);
        }

        double dx = high - low;
        double[] scaled = dx == 0 ? new double[values.Length] : values.Select(v => (v - low) / dx * 2 - 1).ToArray();
        JgsValue shaped = JgsMatrix.FromColumnMajorDims(scaled, dims);
        rest[0] = shaped;
        Sound(session, interpreter, rest, line, col);
    }

    // --- jgraph.internal.audiosim -------------------------------------------------------------------------------

    /// <summary>
    /// <c>jgraph.internal.audiosim('on')</c>: simulated audio devices for this session (device classes plan,
    /// stage D10), so nothing is heard and no microphone records; <c>audiosim('off')</c> removes them, and
    /// <c>audiosim('played')</c> answers the last block an output played, frames by channels, and
    /// <c>audiosim('log')</c> one row of frames, channels and rate for every block played. Test-only
    /// and undocumented.
    /// </summary>
    private static JgsValue AudioSimFunction(Interpreter interpreter) =>
        JgsValue.Function(new BuiltinFunction("jgraph.internal.audiosim", (args, line, col) =>
        {
            JGraphScriptGlobals host = interpreter.Host
                ?? throw new JgsRuntimeException(line, col, "JGraph:audiosim:Arguments", "This session has no host.");
            string verb = args.Count == 1 && IsTextScalar(args[0]) ? TextOf(args[0]) : "";
            switch (verb)
            {
                case "on":
                    host.Devices.AudioSimulation ??= new JGraph.Devices.Simulation.SimulatedAudio();
                    return JgsValue.Null;
                case "off":
                    if (OperatingSystem.IsWindows())
                    {
                        foreach (DeviceObject device in host.Devices.Live.Where(static d => d is AudioPlayerObject or AudioRecorderObject))
                        {
                            device.Delete();
                        }
                    }

                    host.Devices.AudioSimulation = null;
                    return JgsValue.Null;
                case "played" when host.Devices.AudioSimulation is { } sim:
                    (float[] frames, int channels) = sim.LastPlayed;
                    int count = frames.Length / Math.Max(1, channels);
                    double[] columnMajor = new double[frames.Length];
                    for (int f = 0; f < count; f++)
                    {
                        for (int c = 0; c < channels; c++)
                        {
                            columnMajor[(c * count) + f] = frames[(f * channels) + c];
                        }
                    }

                    return JgsMatrix.FromColumnMajor(columnMajor, count, channels);
                case "log" when host.Devices.AudioSimulation is { } sim:
                    IReadOnlyList<(int Frames, int Channels, int Rate)> log = sim.Log;
                    double[] rows = new double[log.Count * 3];
                    for (int i = 0; i < log.Count; i++)
                    {
                        rows[i] = log[i].Frames;
                        rows[log.Count + i] = log[i].Channels;
                        rows[(2 * log.Count) + i] = log[i].Rate;
                    }

                    return JgsMatrix.FromColumnMajor(rows, log.Count, 3);
                default:
                    throw new JgsRuntimeException(line, col, "JGraph:audiosim:Arguments", "jgraph.internal.audiosim takes 'on', 'off', 'played' or 'log'.");
            }
        })
        {
            BindsAnsAsStatement = false,
        });
}
